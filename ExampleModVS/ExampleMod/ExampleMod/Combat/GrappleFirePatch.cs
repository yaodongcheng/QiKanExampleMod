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
	/// 没装内容包时**不挂载**（MySubModule 的 contentPackOnly 清单）：绳弹物品在内容包里，
	/// 纯功能包模式下拉不到这件弹，挂上去只是白跑一次字符串比较（铁律 5 推论）。
	/// </summary>
	[HarmonyPatch(typeof(Mission), "OnAgentShootMissile")]
	public static class GrappleFirePatch
	{
		/// <summary>绳弹的物品 StringId（内容包 `taikou_items/grapple.xml`）。改了那边这里要跟着改。</summary>
		public const string DartItemId = "taikou_grapple_dart";

		/// <summary>绳弹"应该有"的数量（玩家身上永远是 1 个 —— 打完立刻退还）。</summary>
		private const short DartExpectedAmount = 1;

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
				if (ammoItem == null || ammoItem.StringId != DartItemId)
				{
					return true;             // 不是绳弹（普通弓箭/弩矢/投掷物）→ 一行都不多做
				}

				// 🔴 是绳弹：不发引擎导弹，改发我们自己的钩头。
				GrappleLogic logic = GrappleLogic.Ensure();
				string note = "-";
				if (logic == null)
				{
					note = "no GrappleLogic";
				}
				else
				{
					note = logic.Throw();
				}

				// 🔴 退弹（幂等：补到"应该有 1 个"而不是 +1）—— 拦掉导弹后引擎到底扣不扣弹药，
				//    这里把**退还前后**的余量都记进日志（连打两发对比就知道；扣不扣都对）。
				RefundDart(shooterAgent);

				// 🔴 补引擎跳过的记账（规矩 5）：原方法末尾那句"刚射击过"的计时是 AI 用的，
				//    我们提前 return 就跳过了它 —— 自己补一次，免得 AI 的远程决策时序错乱。
				shooterAgent.UpdateLastRangedAttackTimeDueToAnAttack(MBCommon.GetTotalMissionTime());

				DebugLogger.Log($"[Grapple] 拦截开火 → 钩索：{note}");
				return false;
			}
			catch (Exception ex)
			{
				// 出错就放行 —— 引擎照常发它那颗弹，顶多退回"射了支箭"，绝不掐断游戏
				LogOnce("exception", $"拦截判定异常（已放行引擎导弹）：{ex.GetType().Name} {ex.Message}");
				return true;
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

		/// <summary>把绳弹补回"应该有的数量"（幂等）。找不到槽位/出异常 = 静默跳过。</summary>
		private static void RefundDart(Agent agent)
		{
			try
			{
				for (EquipmentIndex slot = EquipmentIndex.WeaponItemBeginSlot;
					slot < EquipmentIndex.NumAllWeaponSlots; slot++)
				{
					MissionWeapon weapon = agent.Equipment[slot];
					if (weapon.Item == null || weapon.Item.StringId != DartItemId)
					{
						continue;
					}
					short before = weapon.Amount;
					if (before != DartExpectedAmount)
					{
						agent.SetWeaponAmountInSlot(slot, DartExpectedAmount, false);
						DebugLogger.Log($"[Grapple] 绳弹退还：槽 {slot} {before} → {DartExpectedAmount}");
					}
					else
					{
						DebugLogger.Log($"[Grapple] 绳弹退还：槽 {slot} 余量已是 {before}（引擎没扣）");
					}
					return;
				}
			}
			catch (Exception ex)
			{
				LogOnce("refund", $"绳弹退还失败（忽略）：{ex.GetType().Name} {ex.Message}");
			}
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
