using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs.CampaignMode
{
#if MB2_V1212
	/// <summary>
	/// 引擎裸解引用守卫：<c>Kingdom.OnNewGameCreated</c> 无条件写
	/// <c>InitialHomeLand = Leader.HomeSettlement;</c>，而 <c>Leader => _rulingClan?.Leader</c>。
	/// 🔴 触发条件（2026-09-10 实机）：同一会话里**第二次开局**时报
	///   NullReferenceException @ Kingdom.OnNewGameCreated（选 1582 转变之卷）。
	/// 根因链：`RulingClan` 全程只有一条赋值路径——**读 Kingdom 节点时**
	///   `RulingClan = (ReadObjectReferenceFromXml("owner", typeof(Hero)) as Hero)?.Clan`；
	///   而 SubModule 的段顺序是 Kingdoms(7) → Factions(9) → Heroes(19)，
	///   读王国时英雄还没读 → 该表达式的 `?.Clan` 落空 → RulingClan 留着 null → Leader null → 崩。
	/// 处置：本补丁在 Leader 为 null 时不写 InitialHomeLand（**不做任何猜测性兜底**），
	///   留给 LivingWorldCampaign.OnNewGameCreatedPartialFollowUp 的既有家宅置位段处理
	///   （那里按「家族第一个据点」置位，与引擎打分同解）。
	/// 版本：1.2.12 反编译实证该方法存在（TaleWorlds.CampaignSystem.Kingdom.OnNewGameCreated(CampaignGameStarter)）。
	///   🔴 1.3.0 起**整个方法不存在**（1.3.15 / 1.4.6 实测：Kingdom 上再无任何 NewGame/Created 成员，
	///   连 `InitialHomeLand` 字段都换成了 `InitialHomeSettlement`）→ 本补丁整类只在 1.2.12 编译（见下方 #if）。
	///
	/// 🔴 为什么必须 #if，而不是"目标找不到就让它找不到"（2026-09-23 实机崩溃教训）：
	///   Harmony 对**属性式目标**解析不到时是 **throw ArgumentException("Undefined target method ...")**，
	///   **不是静默跳过**——异常一路冒到 `OnSubModuleLoad`，把整个 `PatchAll` 掐断：
	///   排在本类之后的补丁全没打上，且 `OnSubModuleLoad` 里 PatchAll 之后的代码
	///   （伤害模型补丁 / SwordBeam 补丁 / 崩溃钩子 / 动画状态机注册 / GameDatabase.Initialize）**一行都没执行**。
	///   证据 = 反编译 0Harmony `PatchClassProcessor.PatchWithAttributes`（抛点原文可见）。
	///   同族两个坑（同一处反编译实锤）：`TargetMethod()` 返回 null **也抛**（"returned an unexpected result: null"）；
	///   只有 `TargetMethods()` 返回**空集合**才是真·静默跳过。→ **新增/改动任何补丁目标必须先核目标在不在。**
	/// </summary>
	[HarmonyPatch(typeof(Kingdom), "OnNewGameCreated")]
	public static class KingdomOnNewGameCreatedGuardPatch
	{
		[HarmonyPrefix]
		public static bool Prefix(Kingdom __instance)
		{
			if (__instance == null)
			{
				return false;
			}
			Hero leader = __instance.Leader;
			if (leader == null)
			{
				DebugLogger.Log($"[KingdomHomeGuard] Kingdom {__instance.StringId} 的 Leader 为 null"
					+ "（RulingClan 未由 XML 读取链赋值）→ 跳过引擎 InitialHomeLand 置位，交由内容包置位段处理");
				return false;
			}
			return true;
		}
	}
#endif
}
