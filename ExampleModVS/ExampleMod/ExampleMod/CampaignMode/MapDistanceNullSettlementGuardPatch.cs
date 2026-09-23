using HarmonyLib;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;

namespace LivingWorldNpcs.CampaignMode
{
#if MB2_V1212
	/// <summary>
	/// 引擎裸解引用守卫：<c>DefaultMapDistanceModel.GetDistance(Settlement, Settlement)</c> 全程不对入参判空——
	/// 第一个分支就写 <c>fromSettlement.Id</c>，传入 null 必 NRE。
	/// 🔴 触发条件（2026-09-12 实机，建世界第二个崩）：vanilla
	///   <c>CharacterRelationCampaignBehavior.UpdateFriendshipAndEnemies</c> 按「势力中点」给领主对播种初始关系：
	///   <c>GetDistance(hero.MapFaction.FactionMidSettlement, hero2.MapFaction.FactionMidSettlement)</c>，
	///   而**无地势力的 <c>FactionMidSettlement</c> 恒为 null**（<c>FactionHelper</c>：只有 `Settlements.Count > 0`
	///   才算中点，否则 return null，无兜底）→ <c>null.Id</c> → NullReferenceException。
	/// 🔴 为什么本世界「无地家族 + 领主成员」是**要保留**的状态（用户 2026-09-12 裁定）：
	///   ① 商家 / 浪人（19 家 + 1 个收容家族 = 122 个领主）本来就没有城；
	///   ② **灭族后领主带野队继续在图上跑**是刻意保留的体验——引擎自己也到得了这个状态
	///      （家族丢光领地后领主照样带部队），只是那段行为**只在建世界跑一次**才没暴露这个裸解引用。
	/// 处置：在距离模型入口补 null 守卫，返回引擎自己的「够不着」值 <c>float.MaxValue</c>
	///   （与 <c>GetDistanceWithDistanceLimit</c> 越限时写的值同源）。调用方据此算出「距离极远」→
	///   不给无地势力的领主播种关系（语义正确），两家都没地时同样不崩。
	///   同族另一处没判 null 的调用点（家族灭亡路径 <c>Clan.All.Where(...)</c> 里的
	///   <c>GetDistance(item.FactionMidSettlement, oldClan.FactionMidSettlement)</c>）被本守卫一并覆盖。
	/// 版本：1.2.12 反编译实证该方法存在（`public override float GetDistance(Settlement, Settlement)`）。
	///   🔴 1.3.0 起**两参重载不存在**（1.3.15 / 1.4.6 实测：Settlement 对只剩
	///   `GetDistance(Settlement, Settlement, bool, bool, NavigationType)` 等 5 参 / 6 参形态）
	///   → 本补丁整类只在 1.2.12 编译（见上方 #if）。
	///   ⚠️ 将来若要让内容包跑在 1.3+，本守卫得对着那一版的重载重做（触发条件与 mod 代码无关，同上）。
	/// 🔴 为什么必须 #if 而不是"找不到就算了"：Harmony 属性式目标解析不到 = **抛异常掐断整个 PatchAll**
	///   （不是静默跳过）——完整证据与连带损失见 KingdomOnNewGameCreatedGuardPatch 的类注释。
	/// </summary>
	[HarmonyPatch(typeof(DefaultMapDistanceModel), "GetDistance", new[] { typeof(Settlement), typeof(Settlement) })]
	public static class MapDistanceNullSettlementGuardPatch
	{
		private static bool _loggedOnce;

		[HarmonyPrefix]
		public static bool Prefix(Settlement fromSettlement, Settlement toSettlement, ref float __result)
		{
			if (fromSettlement != null && toSettlement != null)
			{
				return true;
			}
			if (!_loggedOnce)
			{
				_loggedOnce = true;
				DebugLogger.Log("[MapDistanceNullGuard] 命中：势力中点为 null（无地势力）→ 返回 float.MaxValue 跳过距离计算"
					+ $"（from={(fromSettlement == null ? "null" : fromSettlement.StringId)}"
					+ $", to={(toSettlement == null ? "null" : toSettlement.StringId)}；本条只记一次）");
			}
			__result = float.MaxValue;
			return false;
		}
	}
#endif
}
