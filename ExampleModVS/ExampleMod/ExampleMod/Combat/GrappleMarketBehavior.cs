using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **钩索两件（绳 + 钩）的货架上架**（2026-10-17 立；用户要求"所有的主城商店和村庄都能买到"）。
	///
	/// ── 为什么"上架"这件事只能我们自己做 ──
	/// ① 城镇货架上的武器/箭是**隐藏的"工匠"工坊**每天产出来的（`SandBox/ModuleData/spworkshops.xml`
	///    的 `ItemCategory.ranged_weapons*` / `arrows` 产出 → `WorkshopsCampaignBehavior.GetItemsToProduce`
	///    → `GetRandomItem` 按 `1/(max(100,价值)+100)` 加权抽一件）；村庄只产它的土产。
	///    **没有任何一条链会凭空产出一件钩索。**
	/// ② 玩家存档里已经存在的城镇，货架是**存档的一部分**（建号那一刻之后由经济慢慢长）
	///    ⇒ 光改物品定义，老档永远见不到货。
	/// ⇒ 本行为负责"保底上架"：**每次进游戏**（新档建号完 / 读档完都会触发）与**每天**检查一遍，
	///    货架上不足 <see cref="PerShopStock"/> 件就补到那个数。
	///
	/// ── 🔴 两件物品的 `is_merchandise` **保持 `false`，这是故意的**（2026-10-17 查证后回退过一次）──
	/// 一度想把 `is_merchandise` 改成 `true`（"正规商品"），**已否决**，理由是它有真实副作用：
	/// 我们这两件的品类（绳 Bow → `ranged_weapons`；钩 Arrow → `arrows`）**正好都在工匠工坊的产出表里**，
	/// 而抽取权重 = `1/(max(100,价值)+100)`、两件价值又极低（绳 ≈ 36 / 钩 ≈ 275）
	/// ⇒ 权重 1/136 ≈ 普通一等弓（1/400~1/1100）的 **3~8 倍**，且没写 `culture`（= 每个城都优先）
	/// ⇒ 一改 `true`，**全图城镇会天天产钩索绳、把真弓从货架上挤掉**。
	/// 而 `is_merchandise="false"`（= `ItemObject.NotMerchandise`）正是"别让经济/奖池自己捡起这件"的开关；
	/// **交易界面不读它**（`InventoryLogic` 与三个 VM/UI 程序集全库 0 命中）⇒ 摆在货架上照常可见、可买、可卖回。
	/// （同一份推理写进了物品文件头 —— 改物品的人先看那儿。）
	///
	/// ── 货架是哪一个（反编译实证，1.2.12 / 1.5.1 同名成员一致）──
	/// `Settlement.ItemRoster`（= `Settlement.Party.ItemRoster`）**就是**城镇与村庄的货架本体：
	/// · 城镇菜单【Trade】 → `InventoryManager.ActivateTradeWithCurrentSettlement`
	///   → `OpenScreenAsTrade(Settlement.CurrentSettlement.ItemRoster, …)`
	/// · 村庄菜单【Buy products】 → `game_menu_village_buy_good_on_consequence` 同一句
	/// （城镇里跟**具体商人**对话开的那张交易屏读的也是它，只是按商人职业多一层品类过滤：
	///  武器匠只显示 Weapon 类 —— 我们这两件是 Bow / Arrows 类，走城镇菜单【Trade】看得到全部。）
	///
	/// ── 数量与价格 ──
	/// 默认**每店各 1 件**（`custom.grapple market &lt;n&gt;` 可调，0 = 停止补货）。
	/// 价格不写死：引擎按物品数值折算（绳 ≈ 36 / 钩 ≈ 275 第纳尔）。`custom.grapple market` 打印实价。
	///
	/// ── 边界（接受的，不是 bug）──
	/// · **城堡不上架**（城堡没有交易菜单；`settlement.IsTown` 天然把城堡排除在外）。
	/// · 玩家把多余的钩索**卖回**店里 ⇒ 数量会超过目标值，我们**不去减**（只补不足）。
	/// · 村庄货架会被村民队搬去城里卖（原版经济）⇒ 补货是"每天补回"，不是"永远钉在 1"。
	/// · 城镇自己会按"品类需求"消耗货架上的东西（`ItemConsumptionBehavior`）⇒ 同样是靠每天补回来兜。
	///
	/// 存档：**没有任何自有存档字段**（`SyncData` 空）—— 货架本身随原版存档走，不需要我们记什么。
	/// </summary>
	internal sealed class GrappleMarketBehavior : CampaignBehaviorBase
	{
		/// <summary>钩索两件在**每家店**保底的数量（`custom.grapple market &lt;n&gt;` 热调；0 = 停止补货）。</summary>
		public static int PerShopStock = 1;

		/// <summary>总开关（诊断用；关掉 = 不再补，已经在货架上的留着）。</summary>
		public static bool Enabled = true;

		/// <summary>上一次全量补货扫了几个定居点 / 新加了几件（命令回显用）。</summary>
		public static int LastRestockScanned;
		public static int LastRestockAdded;

		/// <summary>当前行为实例（命令侧入口；行为没挂载时为 null —— 命令会明说，不静默）。</summary>
		public static GrappleMarketBehavior Instance { get; private set; }

		private ItemObject _rope;
		private ItemObject _hook;
		private bool _resolveLogged;

		public GrappleMarketBehavior()
		{
			Instance = this;
		}

		public override void RegisterEvents()
		{
			// 进游戏（新档建号完 / 读档完）都触发 ⇒ 老档也能立刻买到。
			CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
			// 每天逐定居点：玩家买走之后，隔天回来又有货。
			CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, DailyTickSettlement);
		}

		public override void SyncData(IDataStore dataStore)
		{
			// 无自有数据（货架 = 原版 `Settlement.ItemRoster`，自己随存档走）。
		}

		private void OnSessionLaunched(CampaignGameStarter starter)
		{
			// ⚠️ `MBObjectManager` 是**按 Game 实例**的：新开一局 = 新的 ItemObject 实例
			//    ⇒ 每局开局（本回调）都要重新解析，不能把上一局的对象缓存带过来。
			_rope = null;
			_hook = null;
			_resolveLogged = false;
			if (!Enabled) return;
			string line = RestockAll();
			DebugLogger.Log($"[Grapple] 商店上架（进游戏全量补货）：{line}");
		}

		private void DailyTickSettlement(Settlement settlement)
		{
			if (!Enabled) return;
			EnsureStock(settlement);
		}

		// ───────────────────────────── 核心 ─────────────────────────────

		/// <summary>
		/// 把**所有城镇 + 所有村庄**的货架补到 <see cref="PerShopStock"/>，返回一行**纯英文**回执
		/// （控制台纪律）。静态入口：没有实例时如实报错，不假装成功。
		/// </summary>
		public static string RestockAllStatic()
		{
			GrappleMarketBehavior b = Instance;
			if (b == null) return "Error: market behavior is not registered (no active campaign?).";
			return b.RestockAll();
		}

		/// <summary>全量补货（实例入口）。</summary>
		public string RestockAll()
		{
			int scanned = 0, added = 0;
			try
			{
				if (Campaign.Current == null) return "Error: no campaign.";
				foreach (Settlement s in Settlement.All)
				{
					if (s == null) continue;
					if (!s.IsTown && !s.IsVillage) continue;   // 城堡 / 据点营地一律不碰
					scanned++;
					added += EnsureStock(s, out _);
				}
			}
			catch (Exception ex)
			{
				return "Error: restock failed (" + ex.GetType().Name + ": " + ex.Message + ")";
			}

			LastRestockScanned = scanned;
			LastRestockAdded = added;
			return $"OK: market restock | scanned={scanned} added={added} per-shop={PerShopStock}"
				+ (Enabled ? "" : " (DISABLED)");
		}

		/// <summary>单点补货（每天 tick 走这条；不记账、不抛异常）。</summary>
		public void EnsureStock(Settlement settlement)
		{
			EnsureStock(settlement, out _);
		}

		private int EnsureStock(Settlement settlement, out int added)
		{
			added = 0;
			try
			{
				if (settlement == null || PerShopStock <= 0) return 0;
				if (!settlement.IsTown && !settlement.IsVillage) return 0;
				PartyBase party = settlement.Party;
				if (party == null) return 0;                 // 极早期 / 异常档：没有队伍就没有货架
				ItemRoster roster = settlement.ItemRoster;
				if (roster == null) return 0;

				ItemObject rope = ResolveRope();
				ItemObject hook = ResolveHook();
				if (rope != null) added += TopUp(roster, rope);
				if (hook != null) added += TopUp(roster, hook);
				return added;
			}
			catch (Exception)
			{
				return 0;   // 铁律 1：上架失败绝不影响游戏流程
			}
		}

		private static int TopUp(ItemRoster roster, ItemObject item)
		{
			int have = roster.GetItemNumber(item);
			if (have >= PerShopStock) return 0;              // 够了就不动（玩家卖回来的也不去减）
			int want = PerShopStock - have;
			roster.AddToCounts(item, want);                  // 虚空来源（铁律 4 的 Grant 语义：对接"世界"）
			return want;
		}

		// ───────────────────────────── 状态 / 诊断 ─────────────────────────────

		/// <summary>状态行（`custom.grapple market` 用）：覆盖面 + 两件的解析结果 + 引擎给的实价。</summary>
		public string StatusLine()
		{
			int towns = 0, villages = 0, townsWith = 0, villagesWith = 0;
			ItemObject rope = ResolveRope();
			ItemObject hook = ResolveHook();
			try
			{
				if (Campaign.Current != null)
				{
					foreach (Settlement s in Settlement.All)
					{
						if (s == null) continue;
						bool has = false;
						try
						{
							has = s.ItemRoster != null
								&& ((rope != null && s.ItemRoster.GetItemNumber(rope) > 0)
									|| (hook != null && s.ItemRoster.GetItemNumber(hook) > 0));
						}
						catch (Exception) { }
						if (s.IsTown) { towns++; if (has) townsWith++; }
						else if (s.IsVillage) { villages++; if (has) villagesWith++; }
					}
				}
			}
			catch (Exception ex)
			{
				return "Error: market status failed (" + ex.GetType().Name + ": " + ex.Message + ")";
			}

			string priceRope = rope != null ? rope.Value.ToString() : "n/a";
			string priceHook = hook != null ? hook.Value.ToString() : "n/a";
			return $"OK: market | enabled={Enabled} per-shop={PerShopStock}"
				+ $" | towns={townsWith}/{towns} villages={villagesWith}/{villages} (stocked/total)"
				+ $" | rope '{GrappleFirePatch.RopeItemId}' price={priceRope}"
				+ $" | hook '{GrappleFirePatch.HookItemId}' price={priceHook}"
				+ $" | last restock: scanned={LastRestockScanned} added={LastRestockAdded}";
		}

		// ───────────────────────────── 物品解析 ─────────────────────────────

		/// <summary>解析绳那件（本模块物品；查不到 = 没加载，只记一次日志）。</summary>
		public ItemObject ResolveRope() { return Resolve(GrappleFirePatch.RopeItemId, ref _rope); }

		/// <summary>解析钩那件。</summary>
		public ItemObject ResolveHook() { return Resolve(GrappleFirePatch.HookItemId, ref _hook); }

		private ItemObject Resolve(string id, ref ItemObject cache)
		{
			if (cache != null) return cache;
			try
			{
				cache = MBObjectManager.Instance?.GetObject<ItemObject>(id);
			}
			catch (Exception)
			{
				cache = null;
			}
			if (cache == null && !_resolveLogged)
			{
				_resolveLogged = true;      // 只记一次（每天每店都刷会淹掉日志）
				DebugLogger.Log($"[Grapple] ⚠ 商店上架：物品 '{id}' 查不到 —— ModuleData/items/grapple.xml 没加载？");
			}
			return cache;
		}
	}
}
