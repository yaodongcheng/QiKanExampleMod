using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 定居点遭遇链诊断（2026-09-09，京点不开面板排查——用户判定为遭遇检测问题）。
	///
	/// 反编译基准（1.2.12 TaleWorlds.CampaignSystem.dll）：
	///   EncounterManager.Tick → HandleEncounterForMobileParty（静态，每 tick/每部队）：
	///     一长串状态条件 + GetEncounterTargetPoint（= GatePosition 距离判定）→ 命中 →
	///     mobileParty.Ai.AiBehaviorMapEntity.OnPartyInteraction(mobileParty)
	///        → Settlement.OnPartyInteraction → 玩家 → MapState.OnMainPartyEncounter +
	///           EncounterManager.StartSettlementEncounter（开面板）。
	///   🔴 单漏一环 = 部队到位但面板不开（无任何提示）。
	/// 本补丁两枚：①HandleEncounter 前缀——玩家部队+定居点目标时逐值记录（行为/目标/卡点状态/
	///   距离 vs 触发半径）②Settlement.OnPartyInteraction 前缀——触发时记录（触发成功/失败一眼分界）。
	/// 版本：类型+方法名双字符串运行期解析；改名 = 静默跳过，以 [EncounterDiag] 缺失即可察觉。
	/// </summary>
	public class EncounterDiagPatches
	{
		private static float _lastLogTime;

		[HarmonyPatch("TaleWorlds.CampaignSystem.EncounterManager", "HandleEncounterForMobileParty")]
		public static class HandleEncounterDiagPatch
		{
			[HarmonyPrefix]
			private static void Prefix(MobileParty mobileParty)
			{
				try
				{
					if (mobileParty != MobileParty.MainParty || Campaign.Current == null)
					{
						return;
					}
					// 只对「以定居点为目标/正与定居点互动」的玩家部队记录
					bool interesting = mobileParty.ShortTermTargetSettlement != null ||
						(mobileParty.Ai?.AiBehaviorMapEntity is Settlement);
					if (!interesting)
					{
						return;
					}

					// 限频：2 秒一条（这条链每 tick 跑，全量为刷屏）
					if (TaleWorlds.Engine.Time.ApplicationTime - _lastLogTime < 2f)
					{
						return;
					}
					_lastLogTime = TaleWorlds.Engine.Time.ApplicationTime;

					Settlement target = mobileParty.ShortTermTargetSettlement ?? (mobileParty.Ai?.AiBehaviorMapEntity as Settlement);
					Vec2 targetPoint = target?.GatePosition ?? mobileParty.Position2D;
					float distance = (mobileParty.Position2D - targetPoint).Length;
					string blockInfo = "";
					blockInfo += $"attachedTo={mobileParty.AttachedTo?.ToString() ?? "null"} ";
					blockInfo += $"mapEventSide={mobileParty.MapEventSide?.ToString() ?? "null"} ";
					blockInfo += $"currentSettlement={mobileParty.CurrentSettlement?.StringId ?? "null"} ";
					blockInfo += $"besieged={mobileParty.BesiegedSettlement?.StringId ?? "null"} ";
					blockInfo += $"targetSett=({(mobileParty.ShortTermTargetSettlement != null ? "yes" : "null")}) ";
					blockInfo += $"playerEncounter={((PlayerEncounter.Current != null) ? "active" : "null")} ";
					blockInfo += $"timeMode={(int)Campaign.Current.TimeControlMode} ";
					blockInfo += $"aiEntity={(mobileParty.Ai?.AiBehaviorMapEntity?.GetType().Name ?? "null")} ";

					DebugLogger.Log($"[EncounterDiag] MainParty 遭遇检测: behavior={mobileParty.ShortTermBehavior} " +
						$"targetSettle={(target?.StringId ?? "-")} | 状态: {blockInfo}" +
						$"| 距离={distance:F1} 需要<{Campaign.Current.Models.EncounterModel.NeededMaximumDistanceForEncounteringTown:F1}");
				}
				catch (Exception ex)
				{
					DebugLogger.Log($"[EncounterDiag] 诊断段异常: {ex.Message}");
				}
			}
		}

		[HarmonyPatch("TaleWorlds.CampaignSystem.Settlements.Settlement", "OnPartyInteraction")]
		public static class OnPartyInteractionDiagPatch
		{
			[HarmonyPrefix]
			private static void Prefix(MobileParty mobileParty)
			{
				try
				{
					if (mobileParty != MobileParty.MainParty)
					{
						return;
					}
					DebugLogger.Log($"[EncounterDiag] 触发成功：Settlement.OnPartyInteraction fired → 后续应开面板（此前日志若显示到位而无此行 = 遭遇链被状态条件拦截）。");
				}
				catch (Exception ex)
				{
					DebugLogger.Log($"[EncounterDiag] 诊断段异常: {ex.Message}");
				}
			}
		}
	}
}
