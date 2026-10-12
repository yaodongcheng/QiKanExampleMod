using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 战场侧的 <c>ModInput</c> 驱动（2026-10-12）—— 从法术宿主手里**接过来**的职责。
	///
	/// 🔴 为什么需要它：<c>ModInput</c>（项目的统一键位系统）每帧必须有人 `Tick`，而 Mission 侧
	///   原先只有两个驱动点：
	///     · `Interaction/InteractionMissionView` —— 它挂在「战场不跑玩法逻辑」那道闸门**之后**，
	///       战场 / 竞技场 / 自定义战斗里**不在场**；
	///     · `Combat/SpellProjectileLogic` —— 它在闸门之前，正好补上那些场景
	///       （原注释：「那些场景里 InteractionMissionView 不在场，它才是平时的推进入口」）。
	///   ⇒ 法术体系加总闸之后（见 <see cref="SpellSystemGate"/>，默认关 = 法术宿主不挂），
	///     那个补位的驱动点就没了 ⇒ 战场里 `ModInput` 再没人推进。
	///     今天那些场景里**恰好没有 ModInput 的消费者**，所以不会立刻出症状 ——
	///     但那是**刻意装上去的基础设施**，默默拿掉就是埋雷：将来谁在战场加个热键，
	///     都会遇到"键按了没反应"且没人想得到原因。
	///   ⇒ 于是把这件事搬到这个与法术再无关系的常驻小件里。
	///
	/// ⚠️ **只能有一个驱动者**：两个都推 = 按住时长按按两倍速走、长按阈值实际变快，
	///   会悄悄改掉别的玩法行的手感（原注释里记着的教训）⇒ 法术宿主里那两行**已删**。
	///
	/// 覆盖面与原状**逐字一致**：挂载点仍在战役闸门之前（与 `GrappleLogic` 同一批），
	/// 暂停判定与闸门判据都照抄原处 ⇒ 战场 / 竞技场 / 自定义战斗里照旧推进，不多不少。
	/// 没在推进时每帧只做一次置位很轻的判断，零开销。
	/// </summary>
	public class ModInputMissionDriver : MissionLogic
	{
		public override void OnMissionTick(float dt)
		{
			base.OnMissionTick(dt);

			// 暂停（ESC 菜单等）期间不推进输入 —— 与搬家前那一处逐字一致。
			if (MBCommon.IsPaused)
			{
				return;
			}

			// 🔴 判据与原来一字不差：只有「玩法闸门关着」的场景（战场 / 竞技场 / 潜入 / 自定义战斗…）
			//    才由我们推进（那些场景里 InteractionMissionView 不在场）；闸门开着的场景
			//    （城镇 / 村庄 / 对话）由它推 —— 两边都推就会变成两倍速。
			if (Settings.Instance.IsInteractionDisabled())
			{
				ModInput.Tick(dt);
			}
		}
	}
}
