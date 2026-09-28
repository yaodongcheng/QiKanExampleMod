using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using TaleWorlds.SaveSystem;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **手环宝石槽的配装仓**（阶段 5，计划 §16.3「承载」）—— 存角色身上 + 进存档。
	///
	/// 🔴 **为什么必须存在角色身上**：骑砍的物品**没有实例状态**（背包只有"某物品 × 个数"），
	///   所以"哪颗宝石插在哪个槽"不可能记在物品上；宝石物品只当**解锁凭证**。
	///   （另外两条路都走不通：一套配装 = 一个物品定义 → 定义爆炸；扫背包 → 玩家没法表达
	///    "装 3 颗里的这 2 颗"。—— 计划 §16.3）
	///
	/// 存档：本行为自带一条 <c>lwn_spell_loadout</c>（<c>Dictionary&lt;string,string&gt;</c>，引擎原生类型，
	/// **零新增存档结构**）。键 = 英雄 StringId（铁律 20），值 = <c>|</c> 分隔的槽位内容（空槽 = 空串）。
	///
	/// 槽位数（计划 §16.3 / §24 第 3 条）：**手环品质（数据 &lt;Loadout slots="…"&gt;）+ 忍术等级（0~4）**。
	///   ⚠️ 忍术等级的**真值**要等阶段 9（太阁5 忍术卡 → 归属映射）；现在读英雄扩展属性，读不到就是 0。
	/// </summary>
	public class SpellLoadoutBehavior : CampaignBehaviorBase
	{
		/// <summary>英雄 StringId → "gem_a||gem_c"（槽位按 <c>|</c> 切，空槽是空串）。</summary>
		private Dictionary<string, string> _loadouts = new Dictionary<string, string>(StringComparer.Ordinal);

		/// <summary>存档键名（**唯一权威 = 本处**；改键名 = 改这里 + 计划文档同步，禁止单边）。</summary>
		private const string SaveKey = "lwn_spell_loadout";

		public static SpellLoadoutBehavior Instance
		{
			get { return Campaign.Current != null ? Campaign.Current.GetCampaignBehavior<SpellLoadoutBehavior>() : null; }
		}

		public override void RegisterEvents()
		{
			// 只做存储，不需要事件
		}

		public override void SyncData(IDataStore dataStore)
		{
			try
			{
				dataStore.SyncData(SaveKey, ref _loadouts);
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Spell] 配装存档同步失败：{ex.GetType().Name} {ex.Message}");
			}
			if (_loadouts == null)
			{
				_loadouts = new Dictionary<string, string>(StringComparer.Ordinal);
			}
		}

		/// <summary>读原始串（没配过 = null）。</summary>
		public string GetRaw(string heroId)
		{
			if (string.IsNullOrEmpty(heroId))
			{
				return null;
			}
			string raw;
			return _loadouts.TryGetValue(heroId, out raw) ? raw : null;
		}

		/// <summary>写原始串（空串 = 清掉这条，别在存档里攒垃圾键）。</summary>
		public void SetRaw(string heroId, string value)
		{
			if (string.IsNullOrEmpty(heroId))
			{
				return;
			}
			if (string.IsNullOrEmpty(value))
			{
				_loadouts.Remove(heroId);
				return;
			}
			_loadouts[heroId] = value;
		}

		/// <summary>已配装的英雄数（诊断用）。</summary>
		public int Count
		{
			get { return _loadouts.Count; }
		}

		/// <summary>清空全部（新档清理用；与 <see cref="ResetAllCampaignState"/> 一起调）。</summary>
		public void ClearAll()
		{
			_loadouts.Clear();
		}
	}

	/// <summary>配装仓的门面（业务代码只认它 —— 不感知行为生命周期）。</summary>
	public static class SpellLoadout
	{
		/// <summary>
		/// 忍术等级的**英雄扩展属性键**（阶段 9 接线：太阁5 忍术卡 → 归属映射 → 写进这里）。
		/// 现在读不到 = 0 ⇒ 槽位只由手环给（不挡任何测试）。
		/// </summary>
		public const string NinjutsuPropertyKey = "忍术等级";

		/// <summary>忍术等级硬上限（计划 §17：0~4）。</summary>
		public const int NinjutsuMax = 4;

		// ─────────────────────────── 读 ───────────────────────────

		/// <summary>这个英雄配了哪几颗宝石（按槽位顺序，空槽已剔除）。没配过 = 空表。</summary>
		public static List<string> GetGems(Hero hero)
		{
			List<string> gems = new List<string>();
			string raw = GetRaw(hero);
			if (string.IsNullOrEmpty(raw))
			{
				return gems;
			}
			foreach (string piece in raw.Split('|'))
			{
				string gem = piece.Trim();
				if (gem.Length > 0)
				{
					gems.Add(gem);
				}
			}
			return gems;
		}

		/// <summary>原始串（诊断打印用）。</summary>
		public static string GetRaw(Hero hero)
		{
			if (hero == null)
			{
				return null;
			}
			try
			{
				SpellLoadoutBehavior store = SpellLoadoutBehavior.Instance;
				return store != null ? store.GetRaw(hero.StringId) : null;
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Spell] 读配装失败：{ex.GetType().Name} {ex.Message}");
				return null;
			}
		}

		/// <summary>某个槽里是哪颗宝石（空 = null）。</summary>
		public static string GetSlot(Hero hero, int slot)
		{
			string raw = GetRaw(hero);
			if (string.IsNullOrEmpty(raw) || slot < 0)
			{
				return null;
			}
			string[] parts = raw.Split('|');
			if (slot >= parts.Length)
			{
				return null;
			}
			string gem = parts[slot].Trim();
			return gem.Length > 0 ? gem : null;
		}

		/// <summary>槽位数 = 手环品质 + 忍术等级（计划 §16.3）。</summary>
		/// <param name="sealItemId">手环（施法印记）物品 id；空 = 用宝石表里登记的第一只手环。</param>
		public static int SlotCount(Hero hero, string sealItemId = null)
		{
			int slots = ModifierRegistry.SlotsForSeal(string.IsNullOrEmpty(sealItemId) ? DefaultSealId() : sealItemId);
			return slots + NinjutsuLevel(hero);
		}

		/// <summary>忍术等级（0~4）。阶段 9 之前恒 0（读不到扩展属性）。</summary>
		public static int NinjutsuLevel(Hero hero)
		{
			if (hero == null)
			{
				return 0;
			}
			try
			{
				string raw = GlobalVariableBehavior.Instance != null
					? GlobalVariableBehavior.Instance.GetExtendedProperty(hero.StringId, NinjutsuPropertyKey)
					: null;
				int level;
				if (!string.IsNullOrEmpty(raw)
					&& int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out level))
				{
					return Math.Max(0, Math.Min(NinjutsuMax, level));
				}
			}
			catch (Exception)
			{
				// 读不到就是 0（铁律 1）
			}
			return 0;
		}

		/// <summary>宝石表里登记的第一只手环（"槽位数"那句的默认来源；没有登记 = 空串）。</summary>
		public static string DefaultSealId()
		{
			string best = null;
			foreach (KeyValuePair<string, int> kv in ModifierRegistry.SealSlots)
			{
				if (best == null || string.CompareOrdinal(kv.Key, best) < 0)
				{
					best = kv.Key;
				}
			}
			return best;
		}

		// ─────────────────────────── 写 ───────────────────────────

		/// <summary>
		/// 把一颗宝石插进某个槽（<paramref name="gemId"/> 空 / <c>none</c> = 清空该槽）。
		/// 返回 false = 没插上（超出槽位数 / 宝石不存在 / 没有解锁凭证），<paramref name="error"/> 说明原因。
		/// </summary>
		public static bool SetSlot(Hero hero, int slot, string gemId, string sealItemId, out string error)
		{
			error = null;
			if (hero == null)
			{
				error = "no hero (not in campaign?)";
				return false;
			}
			if (slot < 0)
			{
				error = "slot must be >= 0";
				return false;
			}
			int slots = SlotCount(hero, sealItemId);
			if (slot >= slots)
			{
				error = $"slot {slot} out of range (this hero has {slots})";
				return false;
			}
			bool clear = string.IsNullOrEmpty(gemId) || gemId.Equals("none", StringComparison.OrdinalIgnoreCase);
			if (!clear)
			{
				ModifierDef gem = ModifierRegistry.Find(gemId);
				if (gem == null)
				{
					error = $"gem '{gemId}' not in table (see 'custom.spell mods')";
					return false;
				}
				string reason;
				if (!HasCredential(hero, gem, out reason))
				{
					error = reason;
					return false;
				}
			}

			try
			{
				SpellLoadoutBehavior store = SpellLoadoutBehavior.Instance;
				if (store == null)
				{
					error = "loadout store not registered (see MySubModule.OnGameStart)";
					return false;
				}
				string[] parts = SplitSlots(store.GetRaw(hero.StringId), slot + 1);
				parts[slot] = clear ? "" : gemId;
				store.SetRaw(hero.StringId, string.Join("|", parts));
				return true;
			}
			catch (Exception ex)
			{
				error = $"{ex.GetType().Name} (see log)";
				DebugLogger.Log($"[Spell] 写配装失败：{ex.GetType().Name} {ex.Message}");
				return false;
			}
		}

		/// <summary>清掉这个英雄的全部配装。</summary>
		public static void Clear(Hero hero)
		{
			if (hero == null)
			{
				return;
			}
			try
			{
				SpellLoadoutBehavior store = SpellLoadoutBehavior.Instance;
				if (store != null)
				{
					store.SetRaw(hero.StringId, null);
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Spell] 清配装失败：{ex.GetType().Name} {ex.Message}");
			}
		}

		private static string[] SplitSlots(string raw, int atLeast)
		{
			string[] parts = string.IsNullOrEmpty(raw) ? new string[0] : raw.Split('|');
			if (parts.Length >= atLeast)
			{
				return parts;
			}
			string[] grown = new string[atLeast];
			for (int i = 0; i < atLeast; i++)
			{
				grown[i] = i < parts.Length ? parts[i] : "";
			}
			return grown;
		}

		/// <summary>
		/// 解锁凭证检查（计划 §16.3：宝石物品只当解锁凭证）。
		/// 🔴 **数据没铺开时放行**：宝石写了 <c>item="…"</c> 但那个物品在游戏里根本不存在
		///   （内容包还没做这颗宝石的物品）= 机制测试期，放行 + 记一条日志；
		///   物品存在才真的查背包 —— 这样阶段 9 把物品补上之后，这道闸门**自动开始生效**。
		/// </summary>
		public static bool HasCredential(Hero hero, ModifierDef gem, out string reason)
		{
			reason = null;
			if (gem == null || string.IsNullOrEmpty(gem.ItemId))
			{
				return true;   // 没写凭证 = 不做检查
			}
			ItemObject item = null;
			try
			{
				item = MBObjectManager.Instance.GetObject<ItemObject>(gem.ItemId);
			}
			catch (Exception)
			{
				item = null;
			}
			if (item == null)
			{
				if (_loggedCredential.Add(gem.ItemId))
				{
					DebugLogger.Log($"[Spell] 宝石 '{gem.Id}' 的解锁凭证物品 '{gem.ItemId}' 在游戏里不存在"
						+ " → 暂时放行（阶段 9 补上物品后自动开始生效）");
				}
				return true;
			}
			if (hero.PartyBelongedTo != null && hero.PartyBelongedTo.ItemRoster != null
				&& hero.PartyBelongedTo.ItemRoster.GetItemNumber(item) > 0)
			{
				return true;
			}
			reason = $"no '{gem.ItemId}' in inventory (gem '{gem.Id}' needs its unlock item)";
			return false;
		}

		private static readonly HashSet<string> _loggedCredential = new HashSet<string>(StringComparer.Ordinal);

		// ─────────────────────────── 解算入口 ───────────────────────────

		/// <summary>
		/// **修正解算的唯一入口**（玩家与 NPC 共用，铁律 18）：拿这个施法者的配装去解算有效定义。
		/// 🔴 没配装 = **原样返回**（连复制都不做）= 旧法术零开销、行为零变化。
		/// </summary>
		public static SpellDef ResolveFor(Agent caster, SpellDef spell)
		{
			if (caster == null || spell == null)
			{
				return spell;
			}
			if (spell.Resolved)
			{
				return spell;   // 已经解算过（触发子法术 / 上层已解算）—— 别再叠一遍
			}
			try
			{
				Hero hero = HeroOf(caster);
				if (hero == null)
				{
					return spell;
				}
				string raw = GetRaw(hero);
				if (string.IsNullOrEmpty(raw))
				{
					return spell;
				}
				return SpellResolver.ResolveCsv(spell, raw, 0, null);
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Spell] 解算有效定义失败（退回基础术）：{ex.GetType().Name} {ex.Message}");
				return spell;
			}
		}

		/// <summary>施法者对应的英雄（模板 NPC 没有 Hero ⇒ null ⇒ 无配装）。</summary>
		public static Hero HeroOf(Agent agent)
		{
			if (agent == null)
			{
				return null;
			}
			try
			{
				CharacterObject character = agent.Character as CharacterObject;
				return character != null ? character.HeroObject : null;
			}
			catch (Exception)
			{
				return null;
			}
		}
	}
}
