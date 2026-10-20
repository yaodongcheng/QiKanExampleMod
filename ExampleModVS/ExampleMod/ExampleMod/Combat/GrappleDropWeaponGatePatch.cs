using System;
using HarmonyLib;
using TaleWorlds.MountAndBlade.GauntletUI.Mission;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **绳上有人时，把原版 G 键（丢弃武器）整个让给我们**（2026-10-20 立）。
	///
	/// **为什么需要它**：交互面板的【拉紧】= **G 短按**、【扛起/放下/放上马背】= **G 长按**，
	/// 而 G 在原版是 `CombatHotKeyCategory.DropWeapon` —— 我们的输入是**轮询、不吞键** ⇒ 按 G 一定会
	/// 同时撞上原版那条：
	///   · 短按 = 把手里这件（绳/钩）**扔在地上** ⇒ `WieldedWeapon` 变空 ⇒ `[PlayerWeapon] 玩家收刀`
	///     ⇒ 命中 `GrappleLogic` 的出口②"玩家收起钩索 = 松手放人" ⇒ **绳子凭空消失**
	///     （2026-10-19 实机原话："我拉紧之后绳子突然没了"）；
	///   · 长按 0.5s = 弹出原版的"丢弃武器"菜单（`HandleOpeningHoldDrop`）⇒ 扛人按到一半被菜单打断。
	///
	/// **下手点**（1.2.12 / 1.3.15 / 1.4.8 / 1.5.x 四份 DLL 逐个核对过，签名一致）：
	/// `MissionGauntletMainAgentEquipmentControllerView.DropWeaponTick(float)`，**private**，
	/// 由 `OnMissionScreenTick` 每帧调；短按走 `HandleQuickReleaseDrop → Agent.Main.HandleDropWeapon`，
	/// 长按走 `HandleOpeningHoldDrop`。**Prefix 返回 false = 整个方法这一帧不跑** ⇒ 两条都不发生，
	/// 且它内部的 `_prevDropKeyDown / _dropHoldTime` 也不会被推进（我们跳过的是入口，不是里面的分支）。
	///
	/// **闸门 = 我们真的牵着人**（<see cref="GrappleLogic.IsBindingSomeone"/>，扛着人也算）：
	/// 绳上没人时这条补丁**放行**（原版 G 该丢什么丢什么，零体验变化）。
	/// 每帧只多一次 null 判断 + 一个 bool，代价可忽略。
	///
	/// ⚠️ 与 `GrappleAimGatePatch` 的分工：那条管**左键开火**，这条管 **G 键丢弃** —— 同一个思路
	/// （我们不吞键，就在原版的处理入口把它让位），别把两者合并。
	/// </summary>
	[HarmonyPatch(typeof(MissionGauntletMainAgentEquipmentControllerView), "DropWeaponTick")]
	internal static class GrappleDropWeaponGatePatch
	{
		/// <summary>每次"绑上 → 放人"只记一行日志（验收判据：搜 `[Grapple] G 键让位`）。</summary>
		private static bool _logged;

		[HarmonyPrefix]
		private static bool Prefix()
		{
			try
			{
				GrappleLogic logic = GrappleLogic.Current;
				bool ropeOnSomeone = logic != null && logic.IsBindingSomeone();
				if (!ropeOnSomeone)
				{
					_logged = false;     // 放下一个人之后，下一次绑上要能再记一行
					return true;         // 绳上没人 ⇒ 原版丢弃武器照旧
				}

				if (!_logged)
				{
					_logged = true;
					DebugLogger.Log("[Grapple] G 键让位：绳上有人 ⇒ 跳过原版 DropWeaponTick"
						+ "（短按=【拉紧】/ 长按=【扛起·放下】，不再把绳扔在地上）");
				}
				return false;
			}
			catch (Exception)
			{
				// 每帧路径：出错就放行（宁可这一次让原版丢掉武器，也不能让输入出问题）
				return true;
			}
		}
	}
}
