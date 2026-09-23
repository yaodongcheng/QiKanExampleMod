using System;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 🔴 **拦下引擎导弹，改发我们自己的实体** —— 阶段 1 的"起手壳"（脚手架，阶段 3 甩掉）。
	///
	/// 为什么必须拦：不拦 = 有一颗**看不见的弩矢**跟月牙一起飞出去，它会撞到人、触发格挡与受击反应，
	///   玩家看到的是"法术把人打退了两步"这种出戏现象（计划 §一 第 5 条）。
	///
	/// 补丁点：<c>Mission.OnAgentShootMissile</c>（native 回调 → C# 建的导弹就在这个方法里，
	///   前缀返回 false 就**真的没有导弹被建出来**，不是"建了再藏起来"）。
	///   签名在 1.2.12 / 1.3.15 / 1.4.8 / 1.5.2 四个锚点反编译**逐字相同**（8 参，含 isPrimaryWeaponShot）。
	///
	/// 🔴 守卫要窄（计划 §5.4 规矩 2）：只在「这一发用的弹藥物品在我们的法术表里」时才拦截。
	///   别的 mod 射箭、原版射弩、攻城器械的炮弹**一律不管**（`AddCustomMissile` 那条路根本不经过本方法）。
	/// 🔴 前缀内**吞异常并放行**（规矩 3）：出错就 `return true` 让引擎照常发导弹 ——
	///   顶多退回旧表现（月牙当 flying_mesh 飞，只是 110 米外会隐形），绝不掐断游戏。
	/// 🔴 可单独关掉（规矩 4）：config.json 的 `DisabledPatchClasses` 写 `SpellSealFirePatch` 即可。
	/// 🔴 兜底天然优雅（规矩 6）：补丁关掉 / 弹药不在表里 / 认不出 → 引擎照常发导弹，不会崩。
	///
	/// 没装内容包时**不挂载**（MySubModule 的 contentPackOnly 清单）：那时表里一条法术都没有，
	///   挂上去只是白跑一次查表（铁律 5 推论 —— 内容包专属补丁在纯功能包模式下一律不挂）。
	/// </summary>
	[HarmonyPatch(typeof(Mission), "OnAgentShootMissile")]
	public static class SpellSealFirePatch
	{
		/// <summary>已记过日志的键（防刷屏）。</summary>
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
				// 脚本/native 强制的弹（forcedMissileIndex 有值）不是"谁开了一枪"，一律不碰
				if (forcedMissileIndex != -1)
				{
					return true;
				}

				ItemObject ammoItem = ResolveAmmoItem(shooterAgent, weaponIndex);
				if (ammoItem == null)
				{
					return true;
				}
				SpellDef spell = SpellRegistry.FindByAmmo(ammoItem.StringId);
				if (spell == null)
				{
					// 不是法术弹（普通弩矢/弓箭/投掷物）→ 一行都不多做
					return true;
				}

				// 🔴 是法术弹：不发引擎导弹，改发我们自己的实体。
				//    但**玩家相位机正在施法中**（蓄力/引导）时只拦不放 —— 那一发由相位机按自己的节奏出去，
				//    两条起手并存（引擎开火 / 施法键），谁也不会把同一发法术放两遍（阶段 3 起手轴）。
				Vec3 direction = velocity.LengthSquared < 1e-8f
					? shooterAgent.LookDirection
					: velocity.NormalizedCopy();
				SpellProjectileLogic host = __instance.GetMissionBehavior<SpellProjectileLogic>();
				if (host != null && host.IsPlayerCasting)
				{
					shooterAgent.UpdateLastRangedAttackTimeDueToAnAttack(MBCommon.GetTotalMissionTime());
					return false;
				}
				bool cast = SpellCastFlow.Cast(shooterAgent, spell, position, direction);
				if (!cast)
				{
					LogOnce("castfail:" + spell.Id,
						$"法术 '{spell.Id}' 没起来（数据缺件 / 在飞已满）—— 这一发**什么都不飞**（不退回引擎导弹）");
				}

				// 🔴 补引擎跳过的记账（规矩 5）：原方法末尾那句"刚射击过"的计时是 AI 用的，
				//    我们提前 return 就跳过了它 —— 自己补一次，免得 AI 的远程决策时序错乱。
				shooterAgent.UpdateLastRangedAttackTimeDueToAnAttack(MBCommon.GetTotalMissionTime());

				// 🔴 §5.4 实测三问之一：拦掉导弹后**弹药还照扣吗**？把余量记在日志里，
				//    连打两发对比这个数字就知道（不扣 → 要自己扣，走 AgentControlHelper，铁律 4）。
				MissionWeapon ammo = shooterAgent.Equipment[weaponIndex].AmmoWeapon;
				DebugLogger.Log($"[Spell] 拦截引擎导弹：弹药={ammoItem.StringId} 余量≈{ammo.Amount}"
					+ $" 施法者={shooterAgent.Name} 法术={spell.Id}");
				return false;
			}
			catch (Exception ex)
			{
				// 规矩 3：出错就放行 —— 引擎照常发它那颗导弹，顶多退回旧表现，绝不掐断游戏
				LogOnce("exception", $"拦截判定异常（已放行引擎导弹）：{ex.GetType().Name} {ex.Message}");
				return true;
			}
		}

		/// <summary>
		/// 取"这一发打的是哪个弹藥物品" —— 与引擎在 <c>Mission.OnAgentShootMissile</c> 里的取法**逐字同源**：
		/// 手持件自己是消耗品（投掷类）就用它，否则用它挂在下面的弹药（弩/弓类）。
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
				DebugLogger.Log($"[Spell] {message}");
			}
		}
	}
}
