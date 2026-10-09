using System;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **钩索开火拦截** —— 拦掉引擎导弹，改发我们自己的钩头（方案 = plans\钩索-实施计划.md §3.0）。
	///
	/// 不拦会怎样：弓正常射出一颗**真箭**（绳弹自己的 flying_mesh），玩家看到的是"射了支箭"，
	/// 而不是"甩出钩索"。前缀返回 false = **真的没有导弹被建出来**（不是"建了再藏起来"）。
	///
	/// 判据 = **这一发用的弹药物品是绳弹**（与武器无关）：
	///   · 引擎的弹种只能借现成枚举（`Arrow`），别的弓也能装这发弹 ⇒ 判据必须落在"弹药是谁"上；
	///   · 认不出 = 一行都不多做，引擎照常发它的导弹（原版弓箭/弩矢/投掷物**一律不管**）。
	///
	/// 🔴 只拦**玩家**的（`shooterAgent != Agent.Main` 一律放行）：NPC 拿这发弹我们不会帮他钩，
	///   拦下来 = NPC 的箭凭空消失（比"射了支箭"更怪）。平权是目标，但拉拽那套（木板/冻结）
	///   目前是玩家专属（§八 #3），等 NPC 侧做出来再放开这条判据。
	///
	/// 🔴 与 `SpellSealFirePatch` 同款纪律（照抄那份）：前缀内**吞异常并放行**（出错顶多退回"射真箭"）、
	///   可单独关（config.json 的 `DisabledPatchClasses` 写 `GrappleFirePatch`）、
	///   补引擎跳过的记账（`UpdateLastRangedAttackTimeDueToAnAttack`）。
	///
	/// 🔴 物品在**本模块**里（`ModuleData/items/grapple.xml`，2026-10-09 随通用玩法从 Taikou 搬来），
	///   所以**永远挂载**——不再按"内容包在不在"门控（原先登记在 MySubModule 的 contentPackOnly 清单里）。
	///   物品真缺席时也不会坏事：本条只在"弹药 StringId 等于钩"时才动手，拉不到弹就退化成普通弓箭。
	/// </summary>
	[HarmonyPatch(typeof(Mission), "OnAgentShootMissile")]
	public static class GrappleFirePatch
	{
		/// <summary>**钩**的物品 StringId（= 弹药那件，飞出去勾东西的本体；2026-10-08 由 `taikou_grapple_dart` 改名，
		/// 2026-10-09 随通用玩法搬进 LWN 时把 `taikou_` 前缀改成 `lwn_`）。
		/// 定义在**本模块** `ModuleData/items/grapple.xml`（SubModule 的 Items 段，path="items"）——
		/// 改了那边这里要跟着改。⚠️ 另有第二处字面量在 `CampaignMode/Tools/GrappleCommands.cs`，已改成引用本常量。</summary>
		public const string HookItemId = "lwn_grapple_hook";

		/// <summary>**绳**的物品 StringId（= 握着的那件，弓型；判"玩家此刻握着的是不是钩索"用；同上，改物品要跟着改）。</summary>
		public const string RopeItemId = "lwn_grapple_rope";

		/// <summary>钩"应该有"的数量（玩家身上永远是 1 个 —— 打完立刻退还）。</summary>
		private const short HookExpectedAmount = 1;

		private static readonly System.Collections.Generic.HashSet<string> _logged =
			new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

		[HarmonyPrefix]
		public static bool Prefix(Mission __instance, Agent shooterAgent, EquipmentIndex weaponIndex,
			Vec3 position, Vec3 velocity, int forcedMissileIndex)
		{
			try
			{
				if (__instance == null || shooterAgent == null)
				{
					return true;
				}
				// 脚本/native 强制的弹不是"谁开了一枪"，一律不碰
				if (forcedMissileIndex != -1)
				{
					return true;
				}
				// 只拦玩家的（见类注释：NPC 侧还没有拉拽那套）
				if (shooterAgent != Agent.Main)
				{
					return true;
				}

				ItemObject ammoItem = ResolveAmmoItem(shooterAgent, weaponIndex);
				if (ammoItem == null || ammoItem.StringId != HookItemId)
				{
					return true;             // 不是绳弹（普通弓箭/弩矢/投掷物）→ 一行都不多做
				}

				// 🔴🔴 **只排队，不在这里干活**（2026-10-03 实机教训）：
				//    这里是**引擎发弹流程的内部**（native 回调进来的）—— 在里面打瞄准射线 / 造实体 / 退弹，
				//    实测出事：钩头飞出去了、绳随即消失，且**弹药被扣成 0 退不回来**
				//    （引擎的扣弹发生在这个回调**之后**，当场退 = 退了个寂寞，它随后自己扣掉）。
				//    ⇒ 决定：**只记"谁要开这一枪"，真正的发射与退弹挪到下一帧的常规 tick**
				//    （`GrappleLogic.OnMissionTick` → `ProcessPending`）—— 与命令 `throw` 走**同一条路**。
				PendingShooter = shooterAgent;
				HasPendingFire = true;
				_pendingDartItem = ammoItem;
				_pendingRefundSlot = FindDartSlot(shooterAgent);
				_refundWatchTicks = RefundWatchTicks;   // 盯几帧再确认（引擎的扣弹发生在本次回调之后）
				_refundConfirmed = false;
				// 🔴 记下**这一枪的起点与方向**（引擎给的）——相机瞄准打空时用它们兜底（见 ThrowFromShot）；
				//    也记开火武器槽，退弹时把弓的"已装填"状态一并对齐（HUD 的子弹数读的是它）。
				_pendingShotOrigin = position;
				_pendingShotDir = velocity.LengthSquared < 1e-8f
					? shooterAgent.LookDirection
					: velocity.NormalizedCopy();

				// 🔴 补引擎跳过的记账（规矩 5）：原方法末尾那句"刚射击过"的计时是 AI 用的，
				//    我们提前 return 就跳过了它 —— 自己补一次，免得 AI 的远程决策时序错乱。
				shooterAgent.UpdateLastRangedAttackTimeDueToAnAttack(MBCommon.GetTotalMissionTime());

				// 当场余量只用于诊断（槽找不到时别去索引，None = -1 会抛）
				string beforeNote = _pendingRefundSlot == EquipmentIndex.None
					? "槽未找到"
					: shooterAgent.Equipment[_pendingRefundSlot].Amount.ToString();
				DebugLogger.Log($"[Grapple] 拦截开火 → 排队（下一帧发射）| 退弹槽={_pendingRefundSlot} 当场余量={beforeNote}");
				return false;
			}
			catch (Exception ex)
			{
				// 出错就放行 —— 引擎照常发它那颗弹，顶多退回"射了支箭"，绝不掐断游戏
				LogOnce("exception", $"拦截判定异常（已放行引擎导弹）：{ex.GetType().Name} {ex.Message}");
				return true;
			}
		}

		// ─────────────────────────────── 排队状态的执行（下一帧） ───────────────────────────────

		/// <summary>这一帧有一发被拦下的开火待处理（只有 0/1 发：同一帧不可能开两枪）。</summary>
		private static bool HasPendingFire;
		private static Agent PendingShooter;
		private static ItemObject _pendingDartItem;
		private static EquipmentIndex _pendingRefundSlot = EquipmentIndex.None;
		private static Vec3 _pendingShotOrigin;
		private static Vec3 _pendingShotDir;

		/// <summary>
		/// 退弹要**盯几帧**（2026-10-03 实机）：引擎的扣弹发生在拦截回调**之后**（实测：回调里读是 1，
		/// 紧跟着它自己扣成 0）⇒ 只退一次会被它再扣掉。盯满这几帧、每帧幂等补一次，才能保证补得住。
		/// </summary>
		private const int RefundWatchTicks = 4;
		private static int _refundWatchTicks;
		private static bool _refundConfirmed;

		/// <summary>
		/// **由 <c>GrappleLogic.OnMissionTick</c> 每帧调**：先退弹（盯 <see cref="RefundWatchTicks"/> 帧）、再发射。
		/// 任何一步出问题都只记日志、不抛（这是每帧路径，抛了会刷屏）。
		/// </summary>
		public static void ProcessPending()
		{
			bool fire = HasPendingFire;
			Agent shooter = PendingShooter;
			HasPendingFire = false;
			PendingShooter = null;

			// ① 退弹（无论发射成不成功都要退 —— 这一枪是我们拦下的，不该花掉玩家的弹）
			if (_refundWatchTicks > 0)
			{
				_refundWatchTicks--;
				RefundDart();
			}

			// ② 发射（走与命令 `throw` 完全相同的链路）
			if (!fire || shooter == null)
			{
				return;
			}
			try
			{
				// 🔴 用 `Ensure()` 而不是 `Current`（2026-10-03 实机教训）：`Current` 曾经因为
				//    "本行为拿不到 OnBehaviorInitialize"恒为 null（见 `GrappleLogic.OnCreated` 的注释）——
				//    那一次的症状就是"日志显示拦截成功、退弹成功，但什么都没飞出来"（被"没有 GrappleLogic"挡掉）。
				//    `Ensure()` 兜底更硬：找不到就现挂一个，两条路都通。
				GrappleLogic logic = GrappleLogic.Ensure();
				if (logic == null)
				{
					DebugLogger.Log("[Grapple] 开火排队作废：没有 GrappleLogic（Ensure 也拿不到）");
					return;
				}
				string note = logic.ThrowFromShot(_pendingShotOrigin, _pendingShotDir);
				DebugLogger.Log($"[Grapple] 开火（排队执行）→ {note}");
			}
			catch (Exception ex)
			{
				LogOnce("pendingfire", $"排队开火异常（忽略）：{ex.GetType().Name} {ex.Message}");
			}
		}

		/// <summary>在玩家身上找绳弹所在的装备槽（找不到 = Invalid）。</summary>
		private static EquipmentIndex FindDartSlot(Agent agent)
		{
			try
			{
				for (EquipmentIndex slot = EquipmentIndex.WeaponItemBeginSlot;
					slot < EquipmentIndex.NumAllWeaponSlots; slot++)
				{
					MissionWeapon weapon = agent.Equipment[slot];
					if (weapon.Item != null && weapon.Item.StringId == HookItemId)
					{
						return slot;
					}
				}
			}
			catch (Exception)
			{
			}
			return EquipmentIndex.None;
		}

		/// <summary>
		/// 把绳弹补回"应该有 1 个"（幂等）。**盯帧期间每帧调**（见 <see cref="ProcessPending"/>）。
		/// 🔴 三种情况都要处理（实机都可能有）：① 槽还在、余量被扣 ② 槽被引擎**清空**（弹尽即删）③ 找不着槽。
		/// 日志只在**有变化**或**首次确认**时打，避免盯帧期间刷 4 行。
		/// </summary>
		private static void RefundDart()
		{
			EquipmentIndex slot = _pendingRefundSlot;
			ItemObject dartItem = _pendingDartItem;
			Agent main = Agent.Main;
			if (slot == EquipmentIndex.None || dartItem == null || main == null)
			{
				_refundWatchTicks = 0;
				_pendingRefundSlot = EquipmentIndex.None;
				_pendingDartItem = null;
				return;
			}
			try
			{
				MissionWeapon weapon = main.Equipment[slot];
				if (weapon.Item == null)
				{
					// ② 槽被清空 —— 重新装一份回去（否则弓永远"没弹"）
					MissionWeapon reload = new MissionWeapon(dartItem, null, main.Origin?.Banner);
					main.EquipWeaponWithNewEntity(slot, ref reload);
					main.SetWeaponAmountInSlot(slot, HookExpectedAmount, false);
					main.UpdateAgentStats();
					DebugLogger.Log($"[Grapple] 钩退还：弹药槽 {slot} 被清空 → 重新装填 {HookExpectedAmount} 发");
					_refundConfirmed = true;
				}
				else if (weapon.Amount != HookExpectedAmount)
				{
					// 🔴 数量**钉死在 1**（物品的 stack_amount 是 20，那只是"HUD 计数门槛"，见物品文件注释）。
					//    HUD 每帧重算 `GetAmmoAmount()`（读的就是这个 Amount），所以改完当帧就刷新 —— 不需要别的动作。
					short before = weapon.Amount;
					main.SetWeaponAmountInSlot(slot, HookExpectedAmount, false);
					DebugLogger.Log($"[Grapple] 钩退还：弹药槽 {slot} {before} → {HookExpectedAmount}");
					_refundConfirmed = true;
				}
				else if (!_refundConfirmed)
				{
					DebugLogger.Log($"[Grapple] 钩退还：弹药槽 {slot} 余量已是 {weapon.Amount}（引擎没扣）");
					_refundConfirmed = true;
				}

				if (_refundConfirmed)
				{
					// 已经补到位：不需要再盯（但保留 slot/item 供下一次开火复用）
					_refundWatchTicks = 0;
				}
			}
			catch (Exception ex)
			{
				LogOnce("refund", $"绳弹退还失败（忽略）：{ex.GetType().Name} {ex.Message}");
				_refundWatchTicks = 0;
			}
		}

		/// <summary>
		/// 取"这一发打的是哪个弹藥物品" —— 与引擎在 <c>Mission.OnAgentShootMissile</c> 里的取法**逐字同源**
		/// （与 `SpellSealFirePatch.ResolveAmmoItem` 是同一份口径；两处都改要一起改）。
		/// </summary>
		private static ItemObject ResolveAmmoItem(Agent shooterAgent, EquipmentIndex weaponIndex)
		{
			MissionWeapon wielded = shooterAgent.Equipment[weaponIndex];
			if (wielded.IsEqualTo(MissionWeapon.Invalid) || wielded.Item == null)
			{
				return null;
			}
			WeaponComponentData usage = wielded.CurrentUsageItem;
			if (usage == null)
			{
				return null;
			}
			if (usage.IsRangedWeapon && usage.IsConsumable)
			{
				return wielded.Item;
			}
			MissionWeapon ammo = wielded.AmmoWeapon;
			return ammo.Item;
		}

		private static void LogOnce(string key, string message)
		{
			if (_logged.Add(key))
			{
				DebugLogger.Log($"[Grapple] {message}");
			}
		}
	}
}
