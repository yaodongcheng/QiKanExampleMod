using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 阶段 4：**NPC 施法者** —— 让 NPC 自己会放法术，而且判定、飞行、命中、结算与玩家**走同一套代码**
	/// （铁律 18）。它与 <see cref="SpellCastInput"/> 的差别**只有触发源**：
	///   玩家 = 按键 + 相位机；NPC = 这里（"脑驱动"的最简形态）。
	///
	/// 谁算施法者：**手里拿着"法印 + 法术弹"的 NPC** —— 不需要另立名单，法印就是它的法术书
	/// （与玩家同一条判据：弹药 = 法术）。
	///
	/// 四条纪律（计划 §八 阶段 4）：
	///   ① **只开框架认的族**（投射/引导/天降/放置/增益/治疗/传送）—— 位移、召唤、变形这些要设计的东西先不给 NPC；
	///   ② **带权随机选法术**：NPC 手上只有一个法术时就是那一个；有了法术书（多法术）之后按 `ai_weight` 抽；
	///   ③ **防重入**：每个 NPC 一个冷却（数据 `ai_cooldown`），放完不会立刻再放；
	///   ④ **施法期间照常走位**：本类**从不碰移动/行为**，只管调 <see cref="SpellCastFlow"/>。
	///
	/// 性能：扫描用 <c>Mission.GetNearbyAgents</c> 粗筛（1 次原生调用 / 秒），**不遍历全场 agent**。
	/// </summary>
	public sealed class SpellNpcCaster
	{
		/// <summary>扫描间隔（秒）—— 每秒一次足够，NPC 不是帧级反应的东西。</summary>
		private const float ScanInterval = 1.0f;

		/// <summary>只扫玩家附近这个半径内的 NPC（远处的 NPC 不施法，省开销也省得玩家莫名其妙挨打）。</summary>
		private const float ScanRadius = 45f;

		/// <summary>NPC 找目标的最远距离（米）。</summary>
		private const float TargetRange = 40f;

		/// <summary>
		/// **允许 NPC 用的族**（计划 §八 阶段 4 的"只开 7 类"）。
		/// 移动/符文/召唤/变形这四类要么改变 NPC 自己的位置、要么在世界里留东西 —— 都要单独设计，
		/// 先不开。数据侧还能用 <c>ai="false"</c> 单独否掉某一条法术。
		/// </summary>
		private static readonly HashSet<string> AiFamilies = new HashSet<string>(StringComparer.Ordinal)
		{
			"projectile",   // 投射
			"channel",      // 引导
			"skyfall",      // 天降
			"place",        // 放置
			"buff",         // 增益 / 屏障
			"heal",         // 治疗
			"teleport",     // 传送
		};

		private static readonly MBList<Agent> _nearby = new MBList<Agent>();
		private static readonly MBList<Agent> _enemies = new MBList<Agent>();

		/// <summary>每个 NPC 的下次可施法时刻（按 Agent.Index）。</summary>
		private readonly Dictionary<int, float> _cooldownUntil = new Dictionary<int, float>();

		private float _timer;

		public void Tick(float dt)
		{
			_timer += dt;
			if (_timer < ScanInterval)
			{
				return;
			}
			_timer = 0f;

			Mission mission = Mission.Current;
			Agent player = mission != null ? mission.MainAgent : null;
			if (mission == null || player == null || SpellRegistry.Count == 0)
			{
				return;
			}
			try
			{
				_nearby.Clear();
				mission.GetNearbyAgents(new Vec2(player.Position.x, player.Position.y), ScanRadius, _nearby);
				for (int i = 0; i < _nearby.Count; i++)
				{
					TryCast(mission, _nearby[i], player);
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Spell] NPC 施法扫描异常：{ex.GetType().Name} {ex.Message}");
			}
		}

		/// <summary>换场景 / 卸载时清冷却表。</summary>
		public void Reset()
		{
			_cooldownUntil.Clear();
			_timer = 0f;
		}

		private void TryCast(Mission mission, Agent npc, Agent player)
		{
			if (npc == null || npc == player || !AgentControlHelper.SafeIsActive(npc) || npc.Health <= 0f)
			{
				return;
			}
			// ① 手里是不是法印 + 法术弹（与玩家同一条判据）
			SpellDef spell = ResolveWieldedSpell(npc);
			if (spell == null || !spell.AiEnabled || !AiFamilies.Contains(spell.Family))
			{
				return;
			}
			// ② 防重入：冷却没到就不放
			float now = MBCommon.GetTotalMissionTime();
			float until;
			if (_cooldownUntil.TryGetValue(npc.Index, out until) && now < until)
			{
				return;
			}
			// ③ 找个目标（附近敌人里最近的一个）
			Agent target = FindTarget(mission, npc);
			if (target == null)
			{
				return;
			}

			Vec3 origin = npc.Position;
			origin.z += npc.GetEyeGlobalHeight();
			Vec3 aim = target.Position;
			aim.z += target.GetEyeGlobalHeight() * 0.5f;
			Vec3 direction = aim - origin;
			direction = direction.LengthSquared < 1e-6f ? npc.LookDirection : direction.NormalizedCopy();

			if (SpellCastFlow.Cast(npc, spell, origin, direction))
			{
				_cooldownUntil[npc.Index] = now + MathF.Max(1f, spell.AiCooldown);
				DebugLogger.Log($"[Spell] NPC 施法：{npc.Name}(Idx={npc.Index}) 放 '{spell.Id}' → {target.Name}"
					+ $"（下次可放 {spell.AiCooldown:F1}s 后）");
			}
		}

		/// <summary>最近的一个敌人（粗筛 1 次原生调用）。</summary>
		private static Agent FindTarget(Mission mission, Agent npc)
		{
			if (npc.Team == null)
			{
				return null;
			}
			_enemies.Clear();
			mission.GetNearbyEnemyAgents(new Vec2(npc.Position.x, npc.Position.y), TargetRange, npc.Team, _enemies);
			Agent best = null;
			float bestSq = float.MaxValue;
			for (int i = 0; i < _enemies.Count; i++)
			{
				Agent agent = _enemies[i];
				if (agent == null || !AgentControlHelper.SafeIsActive(agent) || agent.Health <= 0f)
				{
					continue;
				}
				float d = agent.Position.DistanceSquared(npc.Position);
				if (d < bestSq)
				{
					bestSq = d;
					best = agent;
				}
			}
			return best;
		}

		private static SpellDef ResolveWieldedSpell(Agent agent)
		{
			MissionWeapon wielded = agent.WieldedWeapon;
			if (wielded.IsEqualTo(MissionWeapon.Invalid) || wielded.Item == null)
			{
				return null;
			}
			WeaponComponentData usage = wielded.CurrentUsageItem;
			if (usage == null)
			{
				return null;
			}
			ItemObject ammo = usage.IsRangedWeapon && usage.IsConsumable ? wielded.Item : wielded.AmmoWeapon.Item;
			return ammo != null ? SpellRegistry.FindByAmmo(ammo.StringId) : null;
		}

		// ─────────────────────────── 装配与抽取（给测试命令用）───────────────────────────

		/// <summary>
		/// 给一个 NPC 装上"法印 + 法术弹"（**测试用**：让 NPC 变成施法者，本类的扫描下一拍就会认它）。
		/// 法印 id 来自**数据**（法术行的 <c>seal</c> 字段）—— LWN 里不出现任何物品 id（铁律 5）。
		/// </summary>
		public static string EquipFor(Agent npc, SpellDef spell)
		{
			if (npc == null || spell == null)
			{
				return "no agent/spell";
			}
			if (string.IsNullOrEmpty(spell.SealItem))
			{
				return $"spell '{spell.Id}' has no seal= in the data (nothing to equip)";
			}
			ItemObject seal = MBObjectManager.Instance.GetObject<ItemObject>(spell.SealItem);
			ItemObject ammo = MBObjectManager.Instance.GetObject<ItemObject>(spell.AmmoId);
			if (seal == null || ammo == null)
			{
				return $"item lookup failed (seal='{spell.SealItem}' ammo='{spell.AmmoId}')";
			}
			MissionWeapon sealWeapon = new MissionWeapon(seal, null, npc.Origin != null ? npc.Origin.Banner : null);
			MissionWeapon ammoWeapon = new MissionWeapon(ammo, null, npc.Origin != null ? npc.Origin.Banner : null);
			npc.EquipWeaponWithNewEntity(EquipmentIndex.Weapon0, ref sealWeapon);
			npc.EquipWeaponWithNewEntity(EquipmentIndex.Weapon1, ref ammoWeapon);
			npc.UpdateAgentStats();
			return "";
		}

		/// <summary>
		/// **带权随机**抽一个"允许 NPC 用"的法术（计划 §八 阶段 4 的原话）。
		/// 现在 NPC 的法术书 = 它手上那一个弹药，所以这里主要给"发法印"这类场景用；
		/// 以后有真正的法术书（多法术槽）时，同一个函数就是选法术的入口。
		/// </summary>
		public static SpellDef PickWeightedForAi()
		{
			float total = 0f;
			foreach (SpellDef def in SpellRegistry.All)
			{
				if (def.AiEnabled && AiFamilies.Contains(def.Family) && !string.IsNullOrEmpty(def.SealItem))
				{
					total += MathF.Max(0.01f, def.AiWeight);
				}
			}
			if (total <= 0f)
			{
				return null;
			}
			float roll = MBRandom.RandomFloat * total;
			foreach (SpellDef def in SpellRegistry.All)
			{
				if (!def.AiEnabled || !AiFamilies.Contains(def.Family) || string.IsNullOrEmpty(def.SealItem))
				{
					continue;
				}
				roll -= MathF.Max(0.01f, def.AiWeight);
				if (roll <= 0f)
				{
					return def;
				}
			}
			return null;
		}
	}
}
