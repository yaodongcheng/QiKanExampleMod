using System;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace LivingWorldNpcs
{
	/// <summary>
	/// Issues（问题委托）系统启动空列表兜底（通用基座，2026-09-09 雷 39 —— 新开档 OnSessionStart 崩）。
	///
	/// 反编译实锤（1.2.12 IssuesCampaignBehavior.OnSessionLaunched）：
	///   list = Settlement.All.Where(x => x.IsTown || x.IsVillage)；
	///   DeterministicShuffle(list) → `random.Next() % settlements.Count` ——
	///   Count == 0 时 `% 0` = DivideByZero（415 行，用户划的现场正确）；随后 `Days(1)/_settlements.Length` 同炸。
	/// 成因观察：自定义最小世界时点差异（OnSessionStart 时刻 Town/Village 结算为空的情况）
	///   —— 织丰 300 据点不触发；本补丁 = 通用兜底：空列表 → 跳过 Issues 行为初始化（v0 最小世界可接受）
	///   + 日志记录时点数据（Settlement 总数/town/village）供根源确认。
	/// 版本：类型+方法名字符串运行期解析（改名 = 静默跳过）。
	/// </summary>
	[HarmonyPatch("TaleWorlds.CampaignSystem.CampaignBehaviors.IssuesCampaignBehavior", "OnSessionLaunched")]
	public static class IssuesSettlementGuardPatch
	{
		[HarmonyPrefix]
		private static bool Prefix()
		{
			try
			{
				var settlements = Settlement.All;
				int townCount = settlements.Count(s => s.IsTown);
				int villageCount = settlements.Count(s => s.IsVillage);
				int pairCount = townCount + villageCount;

				if (pairCount == 0)
				{
					DebugLogger.Log($"[IssuesGuard] Settlement 为空集（town/village=0/0, 总 {settlements.Count}）→ " +
						$"跳过 IssuesCampaignBehavior 初始化（防 % 0 崩溃；最小世界 v0 可接受），请数据侧核对 OnSessionStart 时点据点加载");
					return false; // 跳过整个 OnSessionLaunched
				}
				return true;
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[IssuesGuard] 诊断段异常: {ex.Message}");
				return true; // 异常放行（宁可原版行为）
			}
		}
	}
}
