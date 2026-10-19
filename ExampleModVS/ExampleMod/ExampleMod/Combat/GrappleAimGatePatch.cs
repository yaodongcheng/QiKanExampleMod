using System;
using HarmonyLib;
using TaleWorlds.InputSystem;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **「钩不能当箭用」的提前拦 —— 抢在"进入瞄准状态"之前**（2026-10-12 用户要求，同日落地）。
	///
	/// **为什么需要它**（<see cref="GrappleFirePatch"/> 那道拦截晚了半步）：那边拦在
	/// `Mission.OnAgentShootMissile`，是**开火之后** —— 玩家已经看到**拉弓动作**了，而且那套动作还是钩索的
	/// （弓系开火动作挂在**弹药**的用法组上 ⇒ 任何弓装上我们的钩，播的都是 `act_grapple_*`）。
	///
	/// **为什么不在数据层解决**（试过、当天已回退）：曾把绳的 `ammo_class` / 钩的 `weapon_class` 换成
	/// 没人用的类（`Javelin`），想让普通弓**装不进**这发弹（那样就是引擎自己拦在进瞄准之前）；但 1.2.12 的
	/// `ItemObject.Deserialize` 会用 `weapon_class` **反推 ItemType 并强制覆盖 XML 里写的 `Type`**
	/// （`GetItemTypeFromWeaponClass`：只有 `Arrow` 推出 `Arrows`，`Javelin` 等一律推出 `Thrown`）
	/// ⇒ 换类 = 钩不再是「箭矢」类 = 整套"弓 + 弹药"塌掉。完整记录见 `ModuleData/items/grapple.xml` 文件头。
	///
	/// **下手点**（1.2.12 / 1.3.15 / 1.5.x 三锚点反编译实证）：玩家攻击的托管入口 =
	/// <c>MissionMainAgentController.ControlTick()</c>（`TaleWorlds.MountAndBlade.View.dll`，`private void`，
	/// 由 `OnPreMissionTick` **每帧**调用）。1.2.12 里那句原文（View.dll L642-644）：
	/// <code>
	/// if (base.Input.IsGameKeyDown(9))
	///     mainAgent.MovementFlags |= mainAgent.AttackDirectionToMovementFlag(mainAgent.GetAttackDirection());
	/// </code>
	/// ⇒ 我们在这句**之后**（Postfix）**把这一帧的攻击 flag 抹掉**：native 收到的是"这一帧没按攻击"，
	/// **不会进入 ready/瞄准** —— 连拉弓动作都没有，动作串味一并消失。
	///
	/// 🔴 **掩码不硬编码**：用引擎自己的 `AttackDirectionToMovementFlag(GetAttackDirection())` 现算
	/// （`MovementFlags` 本来就会在 `ControlTick` 里每帧清空重建，抹掉这一帧的即可）。
	/// 🔴 **影响面 = 只有那一条组合**（手里握着别的远程武器本体、而它装填的正是我们的钩）——
	/// 绳 + 钩（正主）、弓 + 普通箭、近战、投掷，一律不碰（判据见
	/// <see cref="GrappleFirePatch.IsHoldingHookInForeignWeapon"/>）。
	/// 🔴 **与 <see cref="GrappleFirePatch"/> 的关系**：这里是**前面那道闸**；那边"拦下 + 退弹 + 提示"
	/// 保留为**安全网**（这里没拦住、或本补丁被 `DisabledPatchClasses` 关掉时兜底）。
	/// 🔴 每帧路径：任何异常都**静默吞掉并放行**（绝不影响玩家操作）。
	/// </summary>
	[HarmonyPatch(typeof(MissionMainAgentController), "ControlTick")]
	internal static class GrappleAimGatePatch
	{
		/// <summary>每条"外来武器"只记一次日志（诊断用，不刷屏）——验收时搜 `[Grapple] 瞄准闸`。</summary>
		private static readonly System.Collections.Generic.HashSet<string> _logged =
			new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

		[HarmonyPostfix]
		private static void Postfix()
		{
			try
			{
				Mission mission = Mission.Current;
				Agent agent = mission != null ? mission.MainAgent : null;
				if (agent == null || !AgentControlHelper.SafeIsActive(agent))
				{
					return;
				}

				// 🔴🔴 **绳上还绑着人 ⇒ 禁止开火**（2026-10-18 用户裁定）。
				//    以前是"发射即替换"：再开一枪会先"清场" ⇒ **把绑着的人悄悄放掉**再钩新的
				//    （一根绳只能系一头，物理上必然如此）⇒ 玩家看到的是"我没松绳，怎么又能钩人"。
				//    这里与"钩不能当箭"同一套手法：抹掉这一帧攻击输入（连瞄准都不让进）+ 按下沿给一句话。
				//    要再钩东西 = 先松绳（交互面板【松绳】/ `custom.grapple bind release`）。
				GrappleLogic boundLogic = GrappleLogic.Current;
				if (boundLogic != null && boundLogic.IsBindingSomeone())
				{
					agent.MovementFlags &= ~agent.AttackDirectionToMovementFlag(agent.GetAttackDirection());
					if (Input.IsKeyPressed(InputKey.LeftMouseButton)
						|| Input.IsKeyPressed(InputKey.ControllerRTrigger))
					{
						boundLogic.ShowBoundNoFireHint();
					}
					return;
				}

				if (!GrappleFirePatch.IsHoldingHookInForeignWeapon(agent))
				{
					return;          // 绝大多数情况：一行都不多做
				}

				// ① 抹掉这一帧的攻击输入（掩码现算，见类注释）
				agent.MovementFlags &= ~agent.AttackDirectionToMovementFlag(agent.GetAttackDirection());

				// ② 按下沿给玩家一句话（冷却在 ShowWrongWeaponHint 内部；reason 变化时它自己会立刻再说）
				if (Input.IsKeyPressed(InputKey.LeftMouseButton)
					|| Input.IsKeyPressed(InputKey.ControllerRTrigger))
				{
					GrappleLogic.Ensure()?.ShowWrongWeaponHint();
				}

				// ③ 诊断：每条外来武器只记一次（证明这道闸真的在动手）
				try
				{
					string weaponId = agent.WieldedWeapon.Item != null ? agent.WieldedWeapon.Item.StringId : "?";
					if (_logged.Add(weaponId))
					{
						DebugLogger.Log($"[Grapple] 瞄准闸：{weaponId} 装填着钩 ⇒ 已抹掉攻击输入（拦在进瞄准之前）");
					}
				}
				catch (Exception)
				{
				}
			}
			catch (Exception)
			{
				// 每帧路径：出错就静默（宁可这一次没拦住，也不能让输入出问题）
			}
		}
	}
}
