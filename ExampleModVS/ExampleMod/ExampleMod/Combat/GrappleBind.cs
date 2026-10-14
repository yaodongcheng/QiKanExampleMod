using System;
using System.Collections.Generic;
using LivingWorldNpcs.Animation;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **步骤 5/6「勾人 → 捆住 → 拉倒 / 被扛」的目标侧控制器**（2026-10-09 立；2026-10-10 改口径）。
	/// 方案 = plans\钩索-实施计划.md §十三（初版）+ §十五（现行口径）。
	///
	/// 🔴 **2026-10-10 起：姿态不再由本文件直接 ForcePlayAction，改由状态机管**
	///    （定义在 `ModuleData/statemachines/bind.xml`，一台机走到底：受击 → 站缚 → 拉倒 → 趴缚
	///     → 起身 → 被扛起 → 被扛行 → 被放下）。本文件只做三件事：
	///      ① 填上下文（<see cref="BindAnimContext"/>）；
	///      ② 在**相位切换**时按"时刻名"把机器 Force 进对应状态（`bind-hit` / `yank-trigger` / …）；
	///      ③ AI 压制（旗标 + 原版 AI 冻结 + 脑标记）与收尾（还 AI）。
	///    **本文件里一个状态名都没有** —— 改图（bind.xml）即可改行为，不用重编译。
	///
	/// 相位（本文件的粗粒度）：
	///   ① **缠 Wrapping** —— 钩绕目标躯干一圈（半径越绕越紧、高度胸口→腰），绳尾贴身体绕；
	///   ② **受击** —— 缠完那一刻 Force `bind-hit` ⇒ 播 crash（1.7 s 一次性，**不倒地**）→ 自动接站缚；
	///   ③ **站缚（稳态）** —— 用户 2026-10-10 拍板：**被捆住的默认姿势是站着**；
	///   ④ **【拉紧】拉倒** —— 三段位移（绷住 → 猛拽 → 倒下）+ 起播"拉倒" ⇒ 趴缚循环；
	///      **定时自动起身**（<see cref="LaySeconds"/>），不必玩家做什么；
	///   ⑤ **【扛起】/【放下】** —— 由 <see cref="GrappleCarry"/> 驱动（同一台机里的"被扛起/被扛行/被放下"）；
	///   ⑥ **解 Releasing** —— 置 `Released` ⇒ **走哪条路由 bind.xml 决定**（站着直接出机、
	///      趴着先演起身再出机），本文件只负责"等机器出机之后把 AI 还回去"。
	///
	/// 🔴 **平权（铁律 18）**：判定与结算全在本文件（<see cref="Begin"/> / <see cref="Tick"/> /
	///    <see cref="Release"/> / <see cref="Abort"/>），玩家侧只留"输入 + 相机"壳
	///    （<see cref="GrappleLogic"/> 负责把钩的相位与绳的远端接到这里）⇒ 将来 NPC 用钩索直接复用。
	///
	/// 🔴 **依赖的两条已验证先例**（别另创）：
	///    · "让人一直站着/躺着不动" = 击晕那套（brain 事件 → 清行为 + 永不结束的 BoundAction 占位）
	///      + **引擎层的原版 AI 冻结**（`SetIsAIPaused`，见 <see cref="PauseVanillaAI"/>）；
	///    · 姿态被抢回来 = 状态机自带的 `RecheckStolen`（每 0.5 s 核对 0 号通道），本文件不再自己抢。
	/// </summary>
	internal sealed class GrappleBind
	{
		public enum Phase { None, Dismounting, Wrapping, Yanking, Bound }

		/// <summary>Tick 的返回：这一拍发生了什么（与 <see cref="GrappleHook.StepResult"/> 同风格）。</summary>
		public enum BindEvent
		{
			None,       // 还在进行
			Finished,   // 整条流程结束（绳该收了）
		}

		// ───────────────────────────── 可调参数（命令热调；重启复位）─────────────────────────────

		/// <summary>总开关（`custom.grapple bind on|off`）。关 = 回到"勾住人什么都不发生"的旧行为。</summary>
		public static bool Enabled = true;

		/// <summary>缠的时长（秒）。甩出去的东西是有速度的 —— 0.55 够看清"绕了一圈"。</summary>
		public static float WrapSeconds = 0.55f;

		/// <summary>缠几圈（1.25 = 一整圈 + 0.25 收口）。</summary>
		public static float WrapTurns = 1.25f;

		/// <summary>缠的半径：入口 → 收紧（米）。目标躯干半径大约 0.3 m。</summary>
		public static float WrapRadiusStart = 0.45f;
		public static float WrapRadiusEnd = 0.28f;

		/// <summary>绳尾贴身体绕的那一段有多长（米）—— 按当前绳的点距折算成"钉几个点"。</summary>
		public static float WrapTailSpan = 1.8f;

		/// <summary>拽倒时朝玩家拖多远（米）。用户 2026-10-08 裁定："原地倒下（拖 2 米）"。</summary>
		public static float DragDistance = 2.0f;

		/// <summary>拽倒（秒）—— 🔴 **2026-10-09 拆成三段**（用户要求"拉倒 + 视觉可见的短暂位移"）。
		/// 三段必须**分开**，因为"位移 + 倒地"同时发生的话，眼睛只看见"姿势从站变躺"，位移被吃掉：
		///   ① <see cref="YankHoldSeconds"/>  **绷住** —— 目标不动，绳吃上力（读作"被拽住了"）；
		///   ② <see cref="YankPullSeconds"/>  **猛拽** —— **身体还站着**快速位移（这一段才是"看得见的被拽"）；
		///   ③ <see cref="YankFallSeconds"/>  **倒下** —— 到位那一刻才起播躺下动画，演一段再接管躺地循环。</summary>
		public static float YankHoldSeconds = 0.15f;
		public static float YankPullSeconds = 0.25f;
		public static float YankFallSeconds = 0.70f;

		/// <summary>拽倒总时长（三段相加；日志/状态行用）。</summary>
		public static float YankTotalSeconds => YankHoldSeconds + YankPullSeconds + YankFallSeconds;

		/// <summary>终点离玩家至少留多远（米）—— 别拉到贴脸。</summary>
		public static float MinPlayerDistance = 3.0f;

		/// <summary>"拉倒"动画从哪个进度起播（0~1）。
		/// 🔴 **2026-10-10 起默认 0**：新的自家 clip `binded_lay_start`（0.733 s / 22 帧）本身就是
		/// 用户专门抽帧做的"拉倒"，**不需要再跳过开头**（旧的 0.35 是给原版 3.6 s 的地牢囚犯躺下用的）。
		/// 旋钮留着：观感不对时可以往后跳。命令 `custom.grapple bind laystart &lt;0~1&gt;`。 </summary>
		public static float LayStartProgress = 0f;

		/// <summary>
		/// **【拉紧】把人拉倒之后，多久自动起身**（秒；用户 2026-10-10 拍板："过一会也会起来"）。
		///
		/// 计时从"拽倒三段走完、拉倒动画起播"那一刻算起；到点 Force `rise-trigger`
		/// ⇒ 播 `binded_lay_end`（2.33 s）→ 自动落回站缚。趴着期间玩家【松绳】/ 收摊 = 立即起身（走 released 那条）。
		/// 命令 `custom.grapple bind laytime &lt;秒&gt;`（0 = 拉倒后永不自动起身，调试档）。
		/// </summary>
		public static float LaySeconds = 6.0f;

		/// <summary>
		/// 捆缚**自动挣脱**的超时（秒）。🔴 **默认 0 = 永不挣脱**（2026-10-09 用户裁定："不要让 NPC 自己挣脱"）——
		/// 解开只由玩家主动做：敲 `custom.grapple bind release`，或（待做）走到身边用**交互面板的"松绳"**。
		/// 非 0 时按超时自动放（调试试用）。命令 `custom.grapple bind time &lt;秒&gt;`（0 = 永不）。
		/// </summary>
		public static float BoundSeconds = 0f;

		/// <summary>
		/// **"解"的兜底超时**（秒）—— 放人之后等状态机自己出机（站着 = 立刻出；趴着 = 先演起身 2.33 s）。
		/// 正常路径由状态机走到机外触发（见 <see cref="TickPending"/>）；这个数只是**防呆**：
		/// 机器因故没出机时也必须把 AI 还回去，否则那个人被永久冻住（比"多站两秒"严重得多）。
		/// </summary>
		public static float StandupSeconds = 3.0f;

		/// <summary>拽倒时先把目标转向玩家（`SetMovementDirection`，与 GrapplePull.FaceToward 同一套已验证写法）。</summary>
		public static bool FacePlayerOnYank = true;

		/// <summary>
		/// **【拉紧】的距离门槛**（米，2026-10-10 用户裁定："绳子系上之后**并且我离绳子另一端的 agent
		/// 超过 3 米**，就可以拉紧把对方拉倒在地"）。**从脚底量**（`Agent.Position`）。
		/// 面板那一行由 <see cref="GrappleLogic.CanTugRope"/> 管；命令 `custom.grapple bind tugmin &lt;米&gt;`。
		/// </summary>
		public static float TugMinDistance = 3.0f;

		// ═════════════════ 绳长上限："牵着走 / 拖在地上"（2026-10-13 立，用户口述）═════════════════
		//
		// **要解决什么**：绳绑上人之后，玩家走远绳就无限变长（`GrappleRope.AutoLength` 那条橡皮筋），
		//   被绑的人杵在原地不动 —— 读感是"一根会伸缩的绳"，不是"我牵着他"。
		//
		// **怎么做的**（用户 2026-10-13 原话拆成三条）：
		//   ① 绑上那一刻把**绳长锁死**（= 当时"手 → 胸口"的距离），之后**不再延长**；
		//   ② 玩家走进绳长以内 ⇒ 绳松着、人不动；**走出绳长 ⇒ 绳绷紧，人被拽着跟上**（`binded_walk`）；
		//   ③ 玩家**骑马 / 跑得快** ⇒ 人跟不上 ⇒ **直接拖在地上**（趴缚循环，脚在前头在后）。
		//
		// 🔴 **挪人是"我们自己写位置"，不走引擎寻路**。三条理由：
		//   · 被捆期间他的**原版 AI 是冻着的**（`SetIsAIPaused`）⇒ 引擎寻路那套（`SetScriptedPosition`）
		//     在他身上到底还动不动**我们没验过**，不赌；
		//   · "拖在地上"这条**只能**自己写位置（引擎不会导航一个趴着的人）；
		//   · 一条路管两种形态（走 / 拖），行为一致、没有两套代码。
		//   **代价**（接受，写进方案已知边界）：脚会滑（动画速率不跟速度）、会被拖过墙角与台阶。
		//   要"不滑不穿"就得改走引擎寻路（方案 §15.9 路线 B/C，那是另一档工作量）。

		/// <summary>总开关（`custom.grapple bind leash 0|1`）。关 = 回到"绳随跨度自由伸缩"的旧行为。</summary>
		public static bool LeashEnabled = true;

		/// <summary>锁定绳长的下限（米）—— 贴脸绑上时别锁成 0.5 米，那样一动就拽。</summary>
		public static float LeashMinLength = 2.5f;

		/// <summary>
		/// **绳的"可视长度"上限**（米）—— 只影响**画出来的那根绳**，不影响"多远才算绷紧"。
		///
		/// 🔴 为什么两件事要分开（2026-10-13 自查抓到的一个坑）：锁定量**不能**被上限压小 ——
		///    在 25 米外绑上一个人（`bind test` 的调试路径能到 30 米）时，若把锁定量压成 18，
		///    那"超出量"一上来就是 7 米 ⇒ **人会当场被 12 m/s 拽飞**（玩家啥都没干）。
		///    所以：锁定量按**真实距离**算（只受 <see cref="LeashMinLength"/> 托底），
		///    上限只管"绳子画多长"（超了就绷成一条直线，链节被拉稀 —— 只在调试路径上见得到，
		///    正常钩索射程 ≤20 米，锁定量本来就在这之下）。
		/// </summary>
		public static float LeashMaxLength = 18.0f;

		/// <summary>锁定绳长时乘的余量（1.05 = 与绳"自动长度"平时的口径一致：永远比跨度长 5%）。
		/// 乘上它，**锁定那一帧画面不变**；不乘 = 绳当场被拉成直线、跳一下。</summary>
		public static float LeashSlackAtLock = 1.05f;

		/// <summary>拽人的**前馈**：速度里"直接跟上绳主"那一份（1 = 完全跟上，不掉队）。</summary>
		public static float LeashFeedForward = 1.0f;

		/// <summary>拽人的**弹簧**：每超出 1 米给多少速度（把多出来的那点收回去）。</summary>
		public static float LeashStiffness = 3.0f;

		/// <summary>拽人速度上限（米/秒）。</summary>
		public static float LeashMaxSpeed = 12f;

		/// <summary>拽的**死区**（米）—— 手在走路时前后摆，别让几厘米的抖动每帧都拽人一下。
		/// 🔴 进用死区、**出用它乘 <see cref="LeashReleaseRatio"/>**（迟滞，防每帧翻）。</summary>
		public static float LeashDeadzone = 0.12f;

		/// <summary>退出"在拽"的阈值比例（默认 0.5 ⇒ 超出量掉到死区一半以下才算松）。</summary>
		public static float LeashReleaseRatio = 0.5f;

		/// <summary>
		/// **`binded_walk` 这条动画"原速时看着像每秒走几米"**（米/秒，默认 1.1）。
		///
		/// 🔴 为什么要有这个数（2026-10-14）：这条 clip **原地无位移轨**（`tpaccli animpos` 实测 X/Y 恒 0），
		///    管线里量不出"它每秒走几米"，只能实机看着调。它决定两件事：
		///    ① **播放倍率**：倍率 = 实际拽速 ÷ 这个数（1.1 m/s 时倍率刚好 1）；
		///    ② **什么时候改拖在地上**（<see cref="WalkMaxMult"/> × 这个数 = 以 m/s 表示的门槛）。
		/// 调法：`custom.grapple bind walkrate &lt;米/秒&gt;`（看着脚不滑就是对的）。
		/// </summary>
		public static float WalkNaturalSpeed = 1.1f;

		/// <summary>
		/// **倍率上限**：需要的倍率超过它 ⇒ **直接改"拖在地上"**（用户 2026-10-14 裁定：
		/// "倍率太高就自动进入拉倒"）。默认 2.0 —— 再快腿就是快放，不如趴着被拖自然。
		/// 命令 `custom.grapple bind walkmax &lt;倍率&gt;` / `dragthr &lt;米/秒&gt;`（同一个门槛，两种写法）。
		/// </summary>
		public static float WalkMaxMult = 2.0f;

		/// <summary>倍率下限：拽得比动画还慢时最多慢放到它（默认 0.55；再慢就像在放幻灯片）。</summary>
		public static float WalkMinMult = 0.55f;

		/// <summary>
		/// **退出"拖在地上"的倍率比例**（默认 0.7）—— 进用 <see cref="WalkMaxMult"/>、出用它的 0.7 倍。
		/// 单阈值会在门槛附近来回翻（人一会儿站着一会儿趴着），这是迟滞（照 <see cref="AnimLatch"/> 那套）。
		/// </summary>
		public static float WalkDragExitRatio = 0.7f;

		/// <summary>形态判据的迟滞开关（进 <see cref="WalkMaxMult"/>、出 ×0.7）。</summary>
		private readonly AnimLatch _dragLatch = new AnimLatch(2f, 1.4f);

		/// <summary>手动锁定倍率（0 = 自适应，默认）。命令 `custom.grapple bind walkspeed &lt;倍率&gt;`。</summary>
		public static float WalkSpeedOverride = 0f;

		/// <summary>
		/// **连续调速那条路开不开**（默认开；命令 `custom.grapple bind walkspeedauto 0|1`）。
		/// 开 = 每帧把倍率推到骨架（`Skeleton.SetAnimationSpeedAtChannel`）；
		/// 关 = 只在**切进"被拖行"那一刻**用 `actionSpeed` 写一次（速率就不再跟着速度变）。
		/// 🔴 这是给"倍率到底吃没吃上"做 A/B 用的：两条路互为对照（见 `AgentAnimStateMachine.SpeedMultiplier`）。
		/// </summary>
		public static bool WalkSpeedAuto = true;

		/// <summary>绳主速度超过这个（米/秒）= "跑起来了" ⇒ 人被**拖在地上**（趴缚）而不是跟着走。
		/// 🔴 2026-10-14 起它是**派生值**（= <see cref="WalkMaxMult"/> × <see cref="WalkNaturalSpeed"/>），
		///    不再是独立旋钮 —— 唯一的门槛是"倍率"，这样"腿演不演得过来"只有一处口径。</summary>
		public static float DragSpeedThreshold => WalkMaxMult * WalkNaturalSpeed;

		/// <summary>绳主**骑着马**且在动 ⇒ 一律拖在地上（用户 2026-10-13："玩家骑马速度快起来"）。</summary>
		public static bool DragWhenMounted = true;

		/// <summary>骑马判定用的最小速度（米/秒）—— 骑着但原地不动不算"拖"。</summary>
		public static float DragMountedMinSpeed = 0.4f;

		/// <summary>
		/// 拖在地上时**背朝行进方向**（= 脚在前、头在后）—— 默认 **false**。
		///
		/// 🔴 2026-10-14 用户裁定改成 **false = 头朝着绳主**：绳系在胸口，人被往绳主那边拽 ⇒
		///    **头朝绳主**才读得通（"他被我拽着，脸朝着我"）；脚在前那种（拖麻袋）留成开关做 A/B。
		///    `custom.grapple bind dragface 0|1` 现改现看。
		/// </summary>
		public static bool DragFaceAway = false;

		/// <summary>拽人的日志间隔（秒；0 = 关）。只在"开始拽 / 停 / 换形态"时各打一行。</summary>
		private bool _leashPullingPrev;
		private bool _leashDraggedPrev;

		/// <summary>拽人的诊断行间隔（秒；0 = 关）—— 每这么多秒打一行距离/速度（看"是不是真绷住了"）。</summary>
		public static float LeashProbeSeconds = 0f;
		private float _leashProbeTimer;

		/// <summary>
		/// **骑马的目标照收 —— 钩住骑手 = 把他拉下马**（`custom.grapple bind mounted 0|1`，默认开）。
		///
		/// 手法 = **两条一起跑**（2026-10-13 改口径，加第 ① 条）：
		///   ① **`CanDismount` blow**（见 <see cref="MountedBlow"/>）：native 直接执行、**不看马速** ——
		///      观感是"绳一绷被扯下来"（走 `act_rider_only_fall_*` 那族），这是"拉下马"想要的那版；
		///   ② 引擎自己的**下马事件**（`Agent.EventControlFlags |= EventControlFlag.Dismount`）兜底 ——
		///      它按马速可能拒收（`DismountVelocityLimit = 0.5f`），但目标慢下来时一定收。
		/// **脚一落地**才接"缠 → 倒 → 缚"（骑在马上时那一套位移/姿势全对不上，必须先下来）。
		/// 关掉 = 回到旧行为（钩挂在他身上、什么都不发生）。
		/// </summary>
		public static bool PullDownMounted = true;

		/// <summary>
		/// 拉下马时**要不要同时打一发 `CanDismount` blow**（`custom.grapple bind mountedblow 0|1`，默认开）。
		/// 关掉 = 只走下马事件那条（老行为，A/B 对照用）。
		/// 🔴 代价（与勾人的击退 blow 同款，已知并接受）：走真实打击管线 ⇒ `OnAgentHit` 会广播
		/// （犯罪/警戒可能记账）、目标会演一下受击反应；血不掉（临时 Immortal）。
		/// </summary>
		public static bool MountedBlow = true;

		/// <summary>**等他下马的超时**（秒）—— 超时就不捆了（收钩收绳），别把人晾在那儿。</summary>
		public static float DismountTimeoutSeconds = 3.0f;

		/// <summary>下马事件的重发间隔（秒）—— 引擎按马速（`DismountVelocityLimit`）可能先拒后收，隔一会儿再点一次。</summary>
		public static float DismountRetrySeconds = 0.25f;

		/// <summary>
		/// 捆缚期**压制复核间隔**（秒；0 = 关）。每这么多个秒复核一次"旗标 + AI 冻结 + 躺地循环还在不在"，
		/// 被抢就补回来（有日志、有计数，见 <see cref="VerifySuppression"/>）。
		/// 🔴 **为什么复核放在本类、而不是放进 <see cref="BoundAction"/>**：本类在**任何场景**每帧都跑
		/// （挂在 <see cref="GrappleLogic"/> 的 mission tick 上），而 `AgentBrain` 在战斗场景里**整套是关的**
		/// （<see cref="Settings.IsInteractionDisabled"/> ⇒ Tick 早退）—— 战斗里就只剩这一层在管。
		/// </summary>
		public static float VerifySeconds = 0.5f;

		/// <summary>
		/// 🔴 **冻结目标的原版 AI**（2026-10-09 加；战斗场景里唯一真正"按住"他的手段）。
		///
		/// **为什么不是"两行旗标就够"**（我先前判断错过一次，依据在此）：`AIScriptedFrameFlags` 一共只有 9 个值
		/// （`GoToPosition` / `NoAttack` / `ConsiderRotation` / `NeverSlowDown` / `DoNotRun` / `GoWithoutMount` /
		/// `RangerCanMoveForClearTarget` / `InConversation` / `Crouch`）—— **没有一个语义是"不许动"**。
		/// 只压 `DoNotRun | NoAttack` = 禁跑、禁攻击，**但没禁"走"、没禁"选目标 / 换武器 / 移动"**
		/// ⇒ 战斗场景里他照旧归原版战斗 AI 指挥（城镇里之所以看着没事，是因为城镇那套原版 AI 本来就不打架）。
		///
		/// **正解 = `Agent.AIStateFlag.Paused`** —— 引擎原生状态，公开 API `Agent.SetIsAIPaused(bool)`
		/// （走 native 桥 `IMBAgent.SetAIStateFlags` → 原生 `Ai_state_flag::Paused`）。三条实证：
		///   · **原版自己就在用**：战前部署阶段（OrderOfBattle）拿它把**全场 AI 冻住**；
		///   · **织丰 `Shokuho.dll` 逐字抄了这套**（反编译实读）——冻结 `SetIsAIPaused(true)`；
		///     解冻 `SetIsAIPaused(false)` + `ResetEnemyCaches()` + `HumanAIComponent.SyncBehaviorParamsIfNecessary()`；
		///   · **我们自己的飞行工程**也用它（`Controller = AI` + `SetIsAIPaused(true)` 冻玩家）。
		/// 1.2.12 / 1.3.15 / 1.4.6 / 1.5.1 四个锚点 DLL 全有 ⇒ **不需要版本分叉**。
		/// 旋钮 `custom.grapple bind aipause 0|1`（默认开；关掉 = 回到"只压旗标"的旧行为，做 A/B 用）。
		/// </summary>
		public static bool PauseVanillaAI = true;

		/// <summary>压制复核的计时器（<see cref="VerifySuppression"/>）。</summary>
		private float _verifyTimer;

		/// <summary>冻 / 解冻目标的原版 AI（幂等；失败只记日志不抛）。
		/// 🔴 **解冻永远执行**（不受旋钮管）—— 否则旋钮中途被关掉就会留一个永久冻住的 agent。</summary>
		private static void SetTargetAIPaused(Agent a, bool paused)
		{
			if (a == null) return;
			if (paused && !PauseVanillaAI) return;
			try { a.SetIsAIPaused(paused); }
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 勾人：{(paused ? "冻结" : "解冻")}原版 AI 失败（{ex.GetType().Name}）"
					+ (paused ? " —— 战斗场景里他可能仍归原版 AI 指挥" : " 🔴 目标可能被留成「冻住」状态"));
			}
		}

		/// <summary>
		/// **把目标"按住"**（进绑那一刻调）：① 冻住他的原版 AI（<see cref="SetTargetAIPaused"/>）；
		///   ② **目标是玩家时还要切他的控制器**（`Controller = AI`，见
		///   <see cref="V.SetPlayerControlFrozen"/>）—— 不切的话玩家照旧能自己走，
		///   绳就成了摆设，"**带着玩家走**"（用户 2026-10-13 第 3 条）根本不成立。
		///
		/// 🔴 **对称的"松开"在 <see cref="RestoreAgent"/>，而且必须排在那里的 `ForceUnlockAgent` 之后** ——
		///    `ForceUnlockAgent` 内部有一步"确保控制器回归 AI"，会把我们先还回去的 Player 控制器再抢走。
		/// </summary>
		private static void HoldTarget(Agent a)
		{
			SetTargetAIPaused(a, true);
			if (a != null && ReferenceEquals(a, Agent.Main))
			{
				try { V.SetPlayerControlFrozen(a, true); }
				catch (Exception ex)
				{
					DebugLogger.Log($"[Grapple] 勾人：切玩家控制器失败（{ex.GetType().Name}: {ex.Message}）"
						+ " —— 玩家还能自己走，绳拉不动他");
				}
			}
		}

		// ───────────────────── 拽倒方式：曲线 vs 引擎冲量（A/B，2026-10-09 用户提出）─────────────────────
		//
		// 用户口径："击退的 blow 来一个冲量就行 —— 我们只是要把他拉倒，又不是拉到身前。"
		// 对：**一次性冲量正是 blow 的语义**，而且引擎自己算位移、自己演受击反应。
		// 唯一要先解决的是"伤害"：`Agent.HandleBlow` 里 `if (b.InflictedDamage <= 0) return;`
		//   ⇒ 零伤害 = 整条打击管线不跑（native 的击退也不会发生）。
		// 解法（反编译实证）：`Agent.SetMortalityState(MortalityState.Immortal)` 是公开 API，
		//   而同一函数里扣血那行是 `if (CurrentMortalityState != MortalityState.Immortal && …) Health = …;`
		//   ⇒ **临时设成 Immortal，伤害数值就变成纯"力度旋钮"，血一点不掉**，而 native 的击退照跑。
		//
		// 命令：`custom.grapple bind yank curve|blow`（默认 curve）· `bind blowforce <数值>` · `bind blowalt 0|1`

		/// <summary>拽倒用哪套：<see cref="YankMode.Curve"/> = 我们自己写位移曲线（精确可控）；
		/// <see cref="YankMode.Blow"/> = 手搓一条击退 blow 交给引擎（它算位移 + 它演受击反应）。</summary>
		public enum YankMode { Curve, Blow }
		public static YankMode Yank = YankMode.Curve;

		/// <summary>Blow 模式的**力度旋钮**（= 写进 blow 的伤害数值）。
		/// 🔴 这个数**不扣血**（blew 期间目标被临时设成 Immortal）—— 它只喂给引擎的
		///    `伤害 ≥ 血量上限 × (抗性 − 穿透)` 判定，决定"推不推得动、推多远"。</summary>
		public static float BlowDamage = 60f;

		/// <summary>Blow 模式：带上 `BlowFlags.KnockDown`（击倒，不只是击退）。</summary>
		public static bool BlowKnockDownFlag = true;

		/// <summary>Blow 模式：把碰撞数据标成"替代攻击"。
		/// 🔴 这是**确定性路径** —— `DecideAgentKnockedBackByBlow` 里 `IsAlternativeAttack ⇒ 直接 true`，
		///    不依赖武器数据（`CanWeaponKnockback` 要一件真武器的记录，native 从哪取我们读不到）。
		///    关掉它就走"自然路径"（靠 WeaponRecord + 伤害比例判定），两条都留着做 A/B。</summary>
		public static bool BlowAlternativeAttack = true;

		/// <summary>Blow 模式等多久（秒）—— 引擎演完它的受击/倒地反应，我们再接管成躺地循环。</summary>
		public static float BlowYankSeconds = 1.2f;

		/// <summary>Blow 模式的诊断锚点：出手那一刻目标在哪（1 秒后打位移差，判断"引擎到底推没推、往哪推"）。</summary>
		private Vec3 _blowOrigin;

		// 🔴 **动作名已经搬进状态机定义**（`ModuleData/statemachines/bind.xml`）——
		//    本文件不再直接播任何一段姿态，所以这里没有 `LayAction` 之类的常量了
		//    （动作名 ↔ clip 名的接线在 `ModuleData/action_types.xml` / `action_sets.xml`）。

		// ───────────────────────────── 运行时状态 ─────────────────────────────

		private Agent _attacker;
		private Agent _target;
		private Phase _phase = Phase.None;
		private float _t;                    // 本相位计时
		private float _boundTimer;
		private float _dismountTimer;        // "拉下马"那一相的计时（超时用）
		private float _dismountRetry;        // 下马事件的重发节拍
		private bool _dismountBlowFired;     // 这一相里那发 CanDismount blow 有没有真的出手（判据行用）
		private string _note = "-";

		// ── 状态机（2026-10-10）：一台机管"被绑者的一生"，本文件只填上下文 + 按时刻 Force ──
		private AgentAnimStateMachine _machine;
		private BindAnimContext _ctx;

		/// <summary>正在演"解"那一段的人（机器还在跑起身动作）—— 出机之后才还 AI，见 <see cref="TickPending"/>。
		/// 🔴 **机器与上下文也一起搬过来**（不是共用 <see cref="_machine"/>）：否则"旧目标还在起身、
		///    新目标已经被勾中"时两台机会互相踩（<see cref="Begin"/> 会重置那两组字段）。</summary>
		private Agent _releasing;
		private AgentAnimStateMachine _releaseMachine;
		private BindAnimContext _releaseCtx;
		private float _releaseTimer;

		/// <summary>【拉紧】拉倒之后"多久自动起身"的倒计时（&lt;= 0 = 没在倒计时）。</summary>
		private float _layTimer;

		/// <summary>扛人者（谁把目标扛在肩上；没在扛 = null）。**只用来读它的速度** —— 被扛行/被扛走的判据。</summary>
		private Agent _carrier;

		// ── 绳长上限（牵着走 / 拖在地上；见上面那一大段"绳长上限"注释）──
		private float _leashLength;          // 锁定的绳长（米）
		private bool _leashLocked;           // 锁过没有（绑上之后第一帧锁）
		private Vec3 _leashAnchor;           // 绳主那只手这一帧在哪（GrappleLogic 每帧喂进来）
		private bool _leashHadAnchor;        // 这一绑里收到过锚点没有
		private bool _leashPulling;          // 本帧绳绷着、人正被拽
		private bool _leashEngaged;          // "在拽"这个状态本身（带迟滞：进用死区、出用一半）
		private bool _leashDragged;          // 本帧是"拖在地上"那种拽（否则 = 被拖着走）
		private float _leashSpeed;           // 本帧拽人速度（诊断）
		private float _leashMult = 1f;       // 本帧给"被拖行"那个动画的播放倍率（1 = 原速）
		private float _leashDist;            // 本帧手→胸口距离（诊断）

		/// <summary>在挣扎（`站缚 ⇄ 挣扎`）。🔴 本轮**没有自动判据**，由 `custom.grapple bind struggle 0|1` 手动置 ——
		/// 什么情况下算"在闹"还没定，先把线接齐、行为留空（免得凭空发明一套规则）。</summary>
		private bool _struggling;

		/// <summary>
		/// 目标**现在是趴着的**（被拉倒、还没起身）。
		///
		/// 为什么要有这个标志（而不是去问状态机"你在哪个状态"）：**C# 里不许出现状态名**
		/// （状态名可以在编辑器里随手改，写死 = 改完静默失效）。趴/站这件事**只有两个写入者**
		/// （<see cref="EnterLaidOut"/> 置 true、起身那一刻置 false），所以本文件自己记最省事。
		/// 消费者 = <see cref="GrappleCarry.Begin"/>（"扛起"那条动画是**从站着的人身上**开始的）。
		/// </summary>
		private bool _lying;

		// 缠：圆心/起始角/当前角度与半径（绳尾钉要用同一组数）
		private Vec3 _center;
		private float _startAngle;
		private float _wrapAngle;
		private float _wrapRadius;
		private float _wrapZ;
		private float _wrapSign = 1f;
		private Vec3[] _tailBuf;

		// 倒
		private Vec3 _yankFrom;
		private Vec3 _yankTo;

		// 钩这一帧该在哪 / 朝哪（GrappleLogic 每帧来取，喂给 GrappleHook.Park）
		private Vec3 _hookPos;
		private Vec3 _hookDir = Vec3.Forward;

		/// <summary>起身等待队列：绑结束之后还要"等起身动画播完再还 AI"（独立于当前有没有绑）。</summary>
		private readonly List<Pending> _pending = new List<Pending>();
		private sealed class Pending
		{
			public Agent Agent;
			public float Timer;
			public string Why;
		}

		/// <summary>观感验收：锁在某一段（`custom.grapple bind anim …`）；null = 交回流程。
		/// 现在它只做一件事：**压住"拉倒后自动起身"的倒计时**（否则看到一半人就自己爬起来了）。</summary>
		private string _forcedAnim;

		/// <summary>本轮拽倒是否已经起播"倒下"动画（三段的第③段只触发一次）。</summary>
		private bool _fallStarted;

		// ───────────────────────────── 只读面（GrappleLogic / 命令用）─────────────────────────────

		public bool IsActive => _phase != Phase.None;
		public Phase Current => _phase;
		public Agent Target => _target;
		public string Note => _note;

		/// <summary>绳主（牵绳那一头的人）。**可能是玩家、也可能是 NPC**（反向绑 = NPC 牵玩家）——
		/// 消费者的判据一律写 <c>ReferenceEquals(Attacker, Agent.Main)</c>，不要假设是玩家。</summary>
		public Agent Attacker => _attacker;

		/// <summary>锁定的绳长（米；0 = 还没锁）。消费者 = <see cref="GrappleLogic"/>（把绳的可视长度也钉住）。</summary>
		public float LeashLength => _leashLocked ? _leashLength : 0f;

		/// <summary>**画出来的那根绳该多长**（= 锁定量，再被 <see cref="LeashMaxLength"/> 封顶）——
		/// 与"多远才算绷紧"（<see cref="LeashLength"/>）是两件事，理由见 <see cref="LeashMaxLength"/> 注释。</summary>
		public float LeashRopeLength => _leashLocked ? MathF.Min(_leashLength, LeashMaxLength) : 0f;

		/// <summary>绳长上限**此刻生效吗**（开关开 + 锁过 + 在缚那一相 + 没被扛着）。</summary>
		public bool LeashActive => LeashEnabled && _leashLocked && _phase == Phase.Bound && _carrier == null;

		/// <summary>本帧绳绷着、人正被拽（诊断 + 消费者判"在拖行"用）。</summary>
		public bool LeashPulling => _leashPulling;

		/// <summary>本帧是"拖在地上"那种拽（true = 趴着被拖 · false = 站着被牵着走）。</summary>
		public bool LeashDragged => _leashDragged;

		/// <summary>目标侧那台状态机（扛人时 <see cref="GrappleCarry"/> 要按时刻 Force 它）。没进机 = null。</summary>
		internal AgentAnimStateMachine Machine => _machine;

		/// <summary>目标侧状态机的上下文（扛人要写 <see cref="BindAnimContext.CarrierMoving"/>）。</summary>
		internal BindAnimContext Context => _ctx;

		/// <summary>目标当前处在状态机的哪个状态（诊断用；没进机 = null）。</summary>
		public string AnimState => _machine != null ? _machine.Current : null;

		/// <summary>目标现在是趴着的吗（见 <see cref="_lying"/>）。消费者 = <see cref="GrappleCarry.Begin"/>。</summary>
		public bool IsLying => _lying;

		/// <summary>扛人者在不在（诊断用）。</summary>
		public bool HasCarrier => _carrier != null;

		/// <summary>钩头这一帧该待的世界位置（缠 = 圆上的点；倒/缚 = 目标躯干）。</summary>
		public Vec3 HookPos => _hookPos;

		/// <summary>钩头这一帧的朝向（局部 +Z）：缠 = 圆的切线；倒/缚 = 背向玩家（像钩挂在身上）。</summary>
		public Vec3 HookDir => _hookDir;

		/// <summary>绳的远端（= 钩的位置；绳永远系在钩的尾环上）。</summary>
		public Vec3 RopeEnd => _hookPos;

		/// <summary>
		/// 这一段该不该把绳**压成绷直**（2026-10-09 用户反馈"看不出被拉"后加）：
		/// 缠 + 拽倒 = 绷直（读感是"在拉"，而不是飘着一根松绳）；缚 = 恢复松量（绳在地上拖着才自然）。
		/// 消费者 = <see cref="GrappleLogic"/> 的 Binding 分支（每帧喂给 <see cref="GrappleRope.SlackRatio"/>）。
		/// 2026-10-13 起"拉下马"那一相也算（绳正吃着力把他从马上拽下来）。
		/// </summary>
		public bool WantsTautRope => _phase == Phase.Dismounting || _phase == Phase.Wrapping || _phase == Phase.Yanking;

		// ───────────────────────────── 进入 / 推进 / 结束 ─────────────────────────────

		/// <summary>
		/// **开始捆一个人**（钩头命中人时由 <see cref="GrappleLogic"/> 调）。
		/// 返回 false + <paramref name="why"/> = 这个目标不能捆（自己 / 儿童 / 坐骑 / 骑在马上的人…），
		/// 调用方照旧"钩挂在人身上不动"。
		/// </summary>
		public bool Begin(Agent attacker, Agent target, Vec3 hitPoint, out string why)
		{
			why = "-";
			if (attacker == null || target == null) { why = "no agent"; return false; }
			if (ReferenceEquals(attacker, target)) { why = "self"; return false; }
			if (!AgentControlHelper.SafeIsActive(target)) { why = "target inactive"; return false; }
			if (target.IsMount || target.RiderAgent != null) { why = "target is a mount"; return false; }
			// 骑马的目标 ⇒ **拉下马**（2026-10-13 新口径）：先触发他的下马事件，落地后才进"缠"。
			// 开关关掉 = 回到旧行为（不接受，钩照旧挂住）。
			bool mounted = false;
			try { mounted = target.MountAgent != null; } catch (Exception) { }
			if (mounted && !PullDownMounted)
			{
				why = "target mounted (pull-down off: 'custom.grapple bind mounted 1' to enable)";
				return false;
			}
			string monsterId = null;
			try { monsterId = target.Monster?.StringId; } catch { }
			if (monsterId != null && monsterId.IndexOf("child", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				// 与击晕同一条规矩：儿童骨架播不了这些动作（成功率也就是 0）
				why = "child immune";
				return false;
			}

			// 前一个还绑着 ⇒ 先放掉（同一时刻只有一根绳，与"发射即替换"的既有口径一致）
			if (IsActive) Release("replaced by a new target");

			// 🔴 **这个 agent 可能还挂着一条没走完的"起身队列"**（上一次绑完的尾巴）——
			//    不清掉的话，它到点会调 `RestoreAgent` 把**这一次**新绑的占位一起清掉
			//    （ClearAllActions 抹掉 StayAction + 解锁旗标 ⇒ 人自己爬起来）。
			//    （2026-10-09 立：`bind test` 连测同一个 NPC 时就是这条潜伏 bug 的现场。）
			_pending.RemoveAll(p => p.Agent == target);

			_attacker = attacker;
			_target = target;
			_phase = Phase.Wrapping;
			_t = 0f;
			_boundTimer = 0f;
			_layTimer = 0f;
			_fallStarted = false;
			_lying = false;
			// 绳长上限：新的一绑 = 重新锁（长度按"绑上那一刻的手→胸口"，见 TickLeash）
			_leashLength = 0f;
			_leashLocked = false;
			_leashHadAnchor = false;
			_leashPulling = false;
			_leashEngaged = false;
			_leashDragged = false;
			_leashSpeed = 0f;
			_leashMult = 1f;
			_leashPullingPrev = false;
			_leashDraggedPrev = false;
			_leashProbeTimer = 0f;
			// 🔴 形态迟滞也要归零：它是**跨"绑"存活**的实例字段，上一绑拖着人时的 +1 会留到这一绑，
			//    第一帧就把刚绑上的人判成"该拖在地上"（凭空放倒）。见 _dragLatch。
			_dragLatch.Reset();
			// 新的一绑 = 新的一台机（上下文全新：Released / Struggling 都得是 false）
			_machine = null;
			_ctx = null;
			_center = ChestOf(target);
			_startAngle = MathF.Atan2(hitPoint.y - _center.y, hitPoint.x - _center.x);
			_wrapSign = WrapTurns >= 0f ? 1f : -1f;
			_note = "wrapping";
			_hookPos = hitPoint;
			_hookDir = (target.Position - hitPoint).NormalizedCopy();

			// 🔴 **骑马的目标：先拉下马**（2026-10-13）。这一相里我们只做两件事 ——
			//    ① **先打一发 `CanDismount` blow**（`MountedBlow` 开着时）：native 直接执行、**不看马速**，
			//       观感就是"被绳一绷扯下来"（走 `act_rider_only_fall_*` 那族）；
			//    ② 同时照旧**反复点他的"下马"事件**兜底（引擎那条按马速可能拒收：`DismountVelocityLimit = 0.5f`）
			//       —— 两条都在跑，谁先把他弄下来算谁的（见 TickDismounting）。
			//    ⚠️ **不冻他的 AI**：下马事件是引擎处理的，先不动 AI 少一层不确定；
			//       进入"缠"那一刻再冻（BeginWrapping 里那行）。
			if (mounted)
			{
				_phase = Phase.Dismounting;
				_dismountTimer = 0f;
				_dismountRetry = 0f;
				_note = "pulling the rider off the horse";
				TriggerDismount(target);
				string blowNote = "";
				_dismountBlowFired = false;
				if (MountedBlow)
				{
					// 方向 = 从目标指向我们（绳在往这边扯）；失败不影响流程，下马事件那条还在兜底
					Vec3 towardAttacker = Vec3.Zero;
					try
					{
						if (attacker != null) towardAttacker = (attacker.Position - target.Position);
					}
					catch (Exception) { }
					string blowErr = FireDismountBlow(target, rear: false, damage: BlowDamage, dirToward: towardAttacker);
					_dismountBlowFired = blowErr == null;
					blowNote = blowErr == null
						? $"，并已发一发 CanDismount blow（力度 {BlowDamage:F0}，目标临时无敌不掉血）"
						: $"，但 blow 出手失败（{blowErr}，只走下马事件）";
				}
				DebugLogger.Log($"[Grapple] 拉下马：命中骑马目标（{SafeName(target)}）⇒ 已点「下马」{blowNote}"
					+ $"，等他落地再接「缠 → 倒 → 缚」（超时 {DismountTimeoutSeconds:F1}s）");
				return true;
			}

			BeginWrapping();
			return true;
		}

		/// <summary>
		/// **进入"缠"那一拍**（原来是 <see cref="Begin"/> 的尾巴，2026-10-13 拆出来 —— 现在有两条入口：
		/// 步行目标 = 直接进；骑马目标 = 下了马才进）。
		/// 🔴 **冻住他的原版 AI 就在这一刻**（不是等到"缚"那拍）：缠 0.55s + 拽 1.1s 这 1.65 秒里
		///    若放任原版 AI 指挥，战斗场景中他会一边被我们拽一边打架/走位
		///    （城镇里看不出来，因为那套原生 AI 本来就不打架）。
		/// </summary>
		private void BeginWrapping()
		{
			_phase = Phase.Wrapping;
			_note = "wrapping";
			HoldTarget(_target);
			DebugLogger.Log($"[Grapple] 勾人：开始缠（{_target.Name}）入口={Fmt(_hookPos)} 圈数={WrapTurns:F2} 半径={WrapRadiusStart:F2}→{WrapRadiusEnd:F2} 时长={WrapSeconds:F2}s"
				+ (PauseVanillaAI ? " · 已冻结其原版 AI" : " · （原版 AI 未冻：bind aipause 0）"));
		}

		/// <summary>
		/// **给目标点一次"下马"**（引擎的 <see cref="Agent.EventControlFlag.Dismount"/>）。
		/// 反编译依据：`Agent.Mount(mount)` 在"已骑着这匹马"那一支里就是 `EventControlFlags |= Dismount`
		/// ⇒ 这是引擎自己的下马入口，不是我们造的。马在狂飙（>`Agent.DismountVelocityLimit`）时
		/// 引擎可能先不收，所以调用方每 <see cref="DismountRetrySeconds"/> 重发一次。
		/// </summary>
		private static void TriggerDismount(Agent a)
		{
			if (a == null) return;
			try { a.EventControlFlags |= Agent.EventControlFlag.Dismount; }
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 拉下马：点下马事件失败（{ex.GetType().Name}: {ex.Message}）");
			}
		}

		/// <summary>
		/// 每帧推进（<see cref="GrappleLogic"/> 在钩头相位里调）。
		/// <paramref name="ropeAnchor"/> = **绳主那只手这一帧在哪**（绳长上限要用它量"手 → 胸口"，
		/// 见本文件顶部"绳长上限"那段）；取不到就传 <c>Vec3.Zero</c>，那一帧不拽人。
		/// </summary>
		public BindEvent Tick(float dt, Vec3 ropeAnchor)
		{
			if (_phase == Phase.None) return BindEvent.None;

			_leashAnchor = ropeAnchor;
			_leashHadAnchor = ropeAnchor.LengthSquared > 0.01f;

			// 目标没了 / 死了 / 失效 ⇒ 直接收
			if (_target == null || !AgentControlHelper.SafeIsActive(_target))
			{
				DebugLogger.Log($"[Grapple] 勾人：目标失效 ⇒ 放（{_note}）");
				Release("target gone");
				return BindEvent.Finished;
			}

			// 🔴 **压制复核**（2026-10-09，TODO 2「AI 接管」）：**所有相位都跑** ——
			//    旗标与 AI 冻结都可能被抢/被清（引擎的部署收尾会把全场解冻、别的系统也可能清旗标）。
			//    ⚠️ **姿势那一项不在这里了**（2026-10-10）：姿态归状态机，它自带 `RecheckStolen`
			//       每 0.5 s 核对 0 号通道、被抢就补回来 ⇒ 本文件不再自己抢姿势。
			//    每 <see cref="VerifySeconds"/> 秒一次，被抢就补回来（有日志、有计数，肉眼可查）。
			if (VerifySeconds > 0f)
			{
				_verifyTimer += dt;
				if (_verifyTimer >= VerifySeconds)
				{
					_verifyTimer = 0f;
					VerifySuppression();
				}
			}

			BindEvent phaseEvent = BindEvent.None;
			switch (_phase)
			{
				case Phase.Dismounting: phaseEvent = TickDismounting(dt); break;
				case Phase.Wrapping: phaseEvent = TickWrapping(dt); break;
				case Phase.Yanking: phaseEvent = TickYanking(dt); break;
				case Phase.Bound: phaseEvent = TickBound(dt); break;
			}

			// 姿态：填上下文 + 推进状态机（**放在相位之后** —— 相位这一帧可能刚 Force 过状态）
			TickTargetMachine(dt);

			return phaseEvent;
		}

		/// <summary>
		/// 把"事实"喂进上下文 + 推进目标侧状态机（每帧在相位之后调）。
		/// 🔴 转移条件**只读上下文**（不读本类的私有字段）—— 这是状态机定义能与系统解耦的前提。
		/// </summary>
		private void TickTargetMachine(float dt)
		{
			if (_machine == null || _ctx == null || _target == null) return;
			_ctx.Struggling = _struggling;
			_ctx.CarrierMoving = IsCarrierMoving();
			_ctx.AnimRemainFrac = _machine.CurrentRemainFrac;
			// 🔴 **播放倍率**（2026-10-14）："被拖行"那一拍按实际拽速自适应 —— 拽得快动画就播快，
			//    脚才不滑。倍率的值由牵绳那一拍算（<see cref="TickLeash"/>），这里只负责喂进去
			//    （**每帧都喂**，所以相位离开缚那拍时会自动回到 1，见 StopLeash）。
			_machine.SpeedMultiplier = _leashMult;
			_machine.PushSpeedEveryFrame = WalkSpeedAuto;
			_machine.Tick(_target, dt);
		}

		/// <summary>
		/// **扛人者在不在动**（`被扛行 ⇄ 被扛走` 的判据）。
		/// 🔴 读的是**扛人者**的速度，不是被扛者的 —— 被扛者是每帧被我们摆位的，它自己的速度没有意义。
		/// 用 `AverageVelocity`（native 平均窗口）不用瞬时速度：防抖动让两个状态每帧互踢
		/// （同 `FollowAgentAction` 那条先例）。
		/// </summary>
		private bool IsCarrierMoving()
		{
			Agent c = _carrier;
			if (c == null) return false;
			try
			{
				Vec3 v = c.AverageVelocity;
				return new Vec2(v.x, v.y).Length > BindAnimConditions.CarrierMovingSpeed;
			}
			catch (Exception) { return false; }
		}

		/// <summary>
		/// **按"时刻名"把状态机 Force 进对应状态**（C# 里一个状态名都没有 —— 改图即可改行为，照飞行那条规矩）。
		/// 定义里没这条边 = 报一行日志（改 bind.xml 改坏了要能一眼看见），返回 false。
		/// </summary>
		internal bool ForceEvent(string whenToken, float startProgress = 0f)
		{
			if (_machine == null || _target == null) return false;
			if (_machine.TryEventTarget(whenToken, out string state))
			{
				_machine.Force(_target, state, BindAnimMachine.AnimBlendIn, startProgress);
				return true;
			}
			DebugLogger.Log($"[Grapple] 勾人：状态机里没有时刻 '{whenToken}' 的边"
				+ "（bind.xml 改坏了？）—— 这一拍不会有姿态变化");
			return false;
		}

		/// <summary>
		/// **拉下马那一相**（2026-10-13 立）：钩已经挂住骑手，我们只做三件事 ——
		///   ① 反复点他的"下马"（引擎按马速可能先拒后收，所以隔 <see cref="DismountRetrySeconds"/> 再点）；
		///   ② 钩头停在**他的胸口**（绳绷着、人还挂在马上，读感就是"被绳子拽住"）；
		///   ③ 他脚一落地 ⇒ 交给 <see cref="BeginWrapping"/> 走原来那条"缠 → 倒 → 缚"。
		/// 超时（<see cref="DismountTimeoutSeconds"/>）= 这钩不成了 ⇒ <see cref="BindEvent.Finished"/>，
		/// 调用方照"整条流程结束"收钩收绳（不把人晾在半空）。
		/// </summary>
		private BindEvent TickDismounting(float dt)
		{
			_t += dt;
			_dismountTimer += dt;
			_dismountRetry += dt;

			bool mounted = false;
			try { mounted = _target.MountAgent != null; } catch (Exception) { }

			if (!mounted)
			{
				// 判据行：耗时 + **这一相里哪条路在场**（blow 已发 / 只走下马事件）——
				// 实机 A/B（`bind mountedblow 0|1`）就看这两行。
				DebugLogger.Log($"[Grapple] 拉下马：'{SafeName(_target)}' 已落地（耗时 {_dismountTimer:F2}s"
					+ (_dismountBlowFired ? "，本相已发 CanDismount blow" : MountedBlow ? "，blow 未出手（只走下马事件）" : "，只走下马事件（mountedblow 关）")
					+ (_dismountBlowFired
						? "）⇒ 他是被绳扯下来的 ⇒ 跳过「缠/受击/站缚」，直接进「拉倒」"
						: "）⇒ 接「缠 → 倒 → 缚」"));
				if (_dismountBlowFired) BeginYankFromFall();
				else BeginWrapping();
				return BindEvent.None;
			}

			// 钩头 / 绳的远端停在胸口（他还在马上，位置由马带着走 —— 每帧重取）。
			_center = ChestOf(_target);
			_hookPos = _center;
			try { _hookDir = (_target.Position - _attacker.Position).NormalizedCopy(); } catch (Exception) { }

			if (_dismountRetry >= DismountRetrySeconds)
			{
				_dismountRetry = 0f;
				TriggerDismount(_target);
			}

			if (_dismountTimer >= DismountTimeoutSeconds)
			{
				DebugLogger.Log($"[Grapple] 拉下马：⚠ '{SafeName(_target)}' {DismountTimeoutSeconds:F1}s 内没下来"
					+ "（马速可能一直超引擎的下马门槛）⇒ 这一钩作废、收钩收绳");
				Release("dismount timeout");
				return BindEvent.Finished;
			}
			return BindEvent.None;
		}

		private BindEvent TickWrapping(float dt)
		{
			_t += dt;
			float u = MathF.Min(1f, _t / MathF.Max(0.05f, WrapSeconds));

			// 圆心每帧跟躯干（目标在走/在转也跟得上）；起始角按命中那一刻的世界方位角定死
			_center = ChestOf(_target);
			_wrapAngle = _startAngle + WrapTurns * MathF.PI * 2f * u;
			_wrapRadius = WrapRadiusStart + (WrapRadiusEnd - WrapRadiusStart) * u;
			_wrapZ = _center.z + 0.10f - 0.30f * u;          // 胸口 → 腰
			_hookPos = CirclePoint(_wrapAngle, _wrapRadius, _wrapZ);
			_hookDir = TangentAt(_wrapAngle, _wrapRadius, _wrapZ);

			// 缠完 ⇒ **受击**（2026-10-10 新口径：命中第一拍 = crash，1.7 s 一次性、不倒地）
			if (u >= 1f) EnterCrash();
			return BindEvent.None;
		}

		/// <summary>
		/// **把"被捆"这件事写进他的旗标与脑**（<see cref="EnterCrash"/> 与"被扯下马直接拉倒"两条路共用）：
		/// ① 脚本旗标压制（`DoNotRun | NoAttack`）② `IsBound` 脑标记 ③ 脑事件 `event_agent_bound`。
		/// 🔴 **标记必须直接置一份**（2026-10-09 加固；事件只当"叙述 + 占位"用）：战斗场景里
		/// `SendEventToAgent` 会**整体早退**（IsInteractionDisabled）⇒ 只靠事件的话，"被捆"在战斗里
		/// 对脑完全不可见（`bind state` 也会显示 bound=False，误导排查）。
		/// </summary>
		private void MarkTargetBound()
		{
			try
			{
				_target.SetScriptedFlags(Agent.AIScriptedFrameFlags.DoNotRun | Agent.AIScriptedFrameFlags.NoAttack);
			}
			catch (Exception) { }

			try
			{
				var markBrain = AgentAIController.GetBrainForAgent(_target);
				if (markBrain != null) markBrain.IsBound = true;
			}
			catch (Exception) { }

			try
			{
				AgentAIController.Instance?.SendEventToAgent(_target, "event_agent_bound", _attacker);
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 勾人：脑事件发送失败（{ex.GetType().Name}）—— 继续（旗标已压制）");
			}
		}

		/// <summary>
		/// 🔴 **被绳扯下马 ⇒ 直接进「拉倒」**（2026-10-13 实机反馈，用户口径）：
		/// 骑马目标是被 `CanDismount` blow **掀下来**的 —— 再走"缠 0.55s → 受击 1.7s → 站缚"是多余的，
		/// 他本来就是**被绳拽下来的**：直接接"绷住 → 猛拽 → 倒"三段（<see cref="EnterYank"/>），落点 = 趴缚循环。
		/// ⚠️ 依赖状态机里那条 `outside → 拉倒` 的边（`ModuleData/statemachines/bind.xml`）。
		/// </summary>
		private void BeginYankFromFall()
		{
			HoldTarget(_target);
			MarkTargetBound();
			if (_machine == null)
			{
				_ctx = new BindAnimContext();
				_machine = AnimMachineRegistry.Create(BindAnimMachine.Name, _ctx);
			}
			_ctx.Released = false;
			DebugLogger.Log($"[Grapple] 拉下马：'{SafeName(_target)}' 是被 blow 掀下来的 ⇒ 跳过「缠/受击/站缚」，直接进「拉倒」");
			EnterYank();
		}

		/// <summary>
		/// **缠完那一刻**：从"过程"进"已捆"。
		///
		/// 🔴 **2026-10-10 改口径**（用户拍板）：命中**不再自动把人拽倒**，改成播 `binded_crash`
		///    （1.7 s 一次性、站姿基准 — 实测根骨 z 恒 −0.02 ⇒ 不倒地）→ 状态机自动接**站缚**。
		///    拽倒改成玩家主动【拉紧】（<see cref="StartYank"/>）。
		/// </summary>
		private void EnterCrash()
		{
			_phase = Phase.Bound;
			_boundTimer = 0f;
			_layTimer = 0f;
			_note = "bound (hit)";

			// 状态机进场：按时刻名 Force（定义里 `outside → 受击` 那条边）
			if (_machine == null)
			{
				_ctx = new BindAnimContext();
				_machine = AnimMachineRegistry.Create(BindAnimMachine.Name, _ctx);
			}
			_ctx.Released = false;
			ForceEvent(BindAnimConditions.HitTrigger);

			// AI 压制：脚本旗标 + 脑事件（同击晕那套；先把旗标打上，免得 AI 在事件到达前抢动画通道）
			MarkTargetBound();

			DebugLogger.Log($"[Grapple] 勾人：捆缚成立（{_target.Name}）—— 受击 {BindAnimConditions.HitTrigger}"
				+ " → 站缚（稳态）。AI 接管 = 脑标记 IsBound + BoundAction 占位（不接命令 / 不参战 / 不围观；解绑后立刻恢复），"
				+ (BoundSeconds > 0f ? $"{BoundSeconds:F0} 秒后自动挣脱（调试档）" : "**永不自动挣脱**")
				+ " —— 解开：敲 bind release，或走到身边用交互面板的【松绳】"
				+ "；拉倒：【拉紧】；扛走：【扛起】（见 §十五）");
		}

		/// <summary>
		/// **【拉紧】—— 把被捆的人拽倒**（玩家主动触发；命令 `custom.grapple bind yank` / 交互面板那一行）。
		///
		/// 复用 §13.12 那三段（绷住 → 猛拽 → 倒下）：位移 + 朝向都在 <see cref="TickYanking"/> 里，
		/// "拉倒"动画在**第三段起点**才起播（先位移、后姿势 —— 反过来位移会被姿势变化吃掉）。
		/// 到位之后 = 趴缚循环，<see cref="LaySeconds"/> 秒后自动起身（用户 2026-10-10 拍板）。
		/// </summary>
		public bool StartYank(out string why)
		{
			why = "-";
			if (_phase != Phase.Bound) { why = "not bound yet (phase = " + _phase + ")"; return false; }
			if (_target == null || !AgentControlHelper.SafeIsActive(_target)) { why = "no live target"; return false; }
			EnterYank();
			return true;
		}

		private void EnterYank()
		{
			_phase = Phase.Yanking;
			_t = 0f;
			_note = "yanking";

			// 起点 = 目标此刻的位置；终点 = 朝玩家方向拖 DragDistance，且离玩家不少于 MinPlayerDistance
			_yankFrom = _target.Position;
			Vec3 toPlayer = _attacker != null ? _attacker.Position - _yankFrom : Vec3.Zero;
			toPlayer.z = 0f;
			float dist = toPlayer.Length;
			Vec3 dir = dist > 0.05f ? toPlayer * (1f / dist) : Vec3.Forward;
			float want = MathF.Min(DragDistance, MathF.Max(0f, dist - MinPlayerDistance));
			_yankTo = _yankFrom + dir * want;
			_yankTo.z = _yankFrom.z;

			// 转向玩家（竖直分量清零；`SetMovementDirection` 是已验证写法，LookDirection 赋值转不动）
			if (FacePlayerOnYank && dir.LengthSquared > 0.01f)
			{
				try { _target.SetMovementDirection(dir.AsVec2); } catch { }
			}

			// 🔴 **不下倒**（2026-10-09 改）：躺下动画挪到"拽到位那一刻"才起播（见 TickYanking 第③段）——
			//    位移与倒地**同时**发生 = 眼睛只看见姿势变化、位移被吃掉。Blow 模式仍不播我们的动画
			//    （让引擎演它自己的受击/倒地反应，那正是选它的理由）。
			if (Yank == YankMode.Blow)
			{
				FireYankBlow(dir);
			}

			// 犯罪记账：**倒下的这一刻**才算"撂倒"（缠只是过程）
			ReportBind(_attacker, _target);

			DebugLogger.Log(Yank == YankMode.Blow
				? $"[Grapple] 勾人：拽倒（引擎冲量）@{Fmt(_yankFrom)} 力度={BlowDamage:F0} 方向→玩家 {Fmt(dir)}"
				: $"[Grapple] 勾人：拽倒 {Fmt(_yankFrom)} → {Fmt(_yankTo)}（拖 {want:F2}m；绷 {YankHoldSeconds:F2}s → 拽 {YankPullSeconds:F2}s → 倒 {YankFallSeconds:F2}s）");
		}

		private BindEvent TickYanking(float dt)
		{
			_t += dt;

			// ── Blow 模式：**引擎在搬人**，我们一个字都不写，只等它演完 ──
			if (Yank == YankMode.Blow)
			{
				_hookPos = ChestOf(_target);
				_hookDir = AwayFromPlayerDir();
				if (_t >= BlowYankSeconds)
				{
					LogBlowResult();
					EnterLaidOut("blow");
				}
				return BindEvent.None;
			}

			// ── 三段：绷住 → 猛拽（人还站着）→ 倒下（到位才起播躺下）──
			float holdEnd = MathF.Max(0.01f, YankHoldSeconds);
			float pullEnd = holdEnd + MathF.Max(0.01f, YankPullSeconds);
			float fallEnd = pullEnd + MathF.Max(0.01f, YankFallSeconds);

			float k = 0f;                                   // ① 绷住：位移恒为 0（绳吃上力、人还站着）
			if (_t > holdEnd)
			{
				// ② 猛拽：`1−(1−u)³` = 起步极快、末段收（冲量的顿挫感）
				float u = MathF.Min(1f, (_t - holdEnd) / MathF.Max(0.01f, YankPullSeconds));
				k = 1f - (1f - u) * (1f - u) * (1f - u);
			}

			Vec3 p = _yankFrom + (_yankTo - _yankFrom) * k;
			try
			{
				// 🔴 每帧瞬移 = 飞行工程 CarrierBoard 同款手法；Z 由引擎按地形重算
				//    （agent 的 Z 写不进去 —— 骑砍底层行为，见 Knowledge/骑砍Agent运动与位置机制.md）
				_target.TeleportToPosition(p);
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 勾人：拖拽瞬移失败（{ex.GetType().Name}）—— 停在原地继续流程");
			}

			// ③ 拽到位那一刻**才**起播"拉倒"（顺序：先位移、后倒地 —— 反过来位移会被姿势变化吃掉）
			if (!_fallStarted && _t >= pullEnd && Yank == YankMode.Curve)
			{
				_fallStarted = true;
				ForceEvent(BindAnimConditions.YankTrigger, LayStartProgress);
			}

			_hookPos = ChestOf(_target);
			_hookDir = AwayFromPlayerDir();

			if (_t >= fallEnd) EnterLaidOut("curve");
			return BindEvent.None;
		}

		/// <summary>
		/// **到位：进"趴缚"**（三段走完 / Blow 模式演完都从这里进）。
		/// 旧版这里是 <c>EnterBound()</c>（进"缚"那拍）；2026-10-10 起"缚"的稳态改成**站着**，
		/// 所以这个入口的语义变成"**被拉倒了**"。
		///
		/// 🔴 顺带起 **"定时自动起身"倒计时**（<see cref="LaySeconds"/>，用户 2026-10-10 拍板：
		///    "拉倒进入趴地状态，但是过一会也会起来"）。趴着期间【松绳】/ 收摊 = 立即起身（走 released 那条边）。
		/// </summary>
		private void EnterLaidOut(string how)
		{
			_phase = Phase.Bound;
			_note = "bound (laid down by " + how + ")";
			_lying = true;
			_layTimer = LaySeconds;      // 0 = 拉倒后永不自动起身（调试档）
			if (!_fallStarted)
			{
				// Blow 模式：引擎演完了它的受击/倒地反应 ⇒ 再用我们的"拉倒"接上趴地循环
				_fallStarted = true;
				ForceEvent(BindAnimConditions.YankTrigger, LayStartProgress);
			}
			DebugLogger.Log($"[Grapple] 勾人：已拉倒（{how}）⇒ 趴缚循环"
				+ (LaySeconds > 0f ? $"，{LaySeconds:F1} 秒后自动起身" : "（laytime 0 = 不自动起身）"));
		}

		/// <summary>
		/// **引擎冲量式拽倒**（A/B 里 "blow" 那条）：手搓一条击退 blow 交给引擎 ——
		/// 位移、受击反应、倒地全由 native 算，我们只给"方向 + 力度"。
		///
		/// 🔴 **不掉血的做法**：`Agent.SetMortalityState(Agent.MortalityState.Immortal)` 是公开 API，
		///   而 `Agent.HandleBlow` 里扣血那行是
		///   `if (CurrentMortalityState != MortalityState.Immortal &amp;&amp; !Mission.DisableDying) Health = …;`
		///   ⇒ 出手前设 Immortal、出手后还原：**伤害数值纯当"力度参数"用，血一点不掉**（反编译实证）。
		///
		/// 🔴 **为什么必须伤害 &gt; 0**：同一函数开头 `if (b.InflictedDamage &lt;= 0) return;` 在**扣血之前**
		///   —— 零伤害 = 整条打击管线（含 native 的击退/击倒）根本不跑。
		///   ⇒ "零伤害击退"不存在；只能靠 Immortal 把伤害的代价去掉、把数值留下当力度。
		///
		/// （手搓 blow 的字段口径照抄 <see cref="SpellPieces"/> 的落地伤害那条已验证配方。）
		/// </summary>
		private void FireYankBlow(Vec3 dirToPlayer)
		{
			Agent victim = _target;
			if (victim == null) return;
			_blowOrigin = victim.Position;
			Agent.MortalityState prev = Agent.MortalityState.Mortal;
			try { prev = victim.CurrentMortalityState; } catch (Exception) { }
			try
			{
				victim.SetMortalityState(Agent.MortalityState.Immortal);   // ← 血不掉（见方法注释）

				Vec3 dir = dirToPlayer.LengthSquared < 1e-6f ? Vec3.Forward : dirToPlayer.NormalizedCopy();
				Blow blow = new Blow(_attacker != null ? _attacker.Index : -1);
				blow.DamageType = DamageTypes.Blunt;
				blow.BoneIndex = victim.Monster != null ? victim.Monster.HeadLookDirectionBoneIndex : (sbyte)0;
				blow.VictimBodyPart = BoneBodyPartType.Chest;
				blow.GlobalPosition = victim.Position + Vec3.Up * 1.0f;
				blow.BaseMagnitude = BlowDamage;
				blow.WeaponRecord.FillAsMeleeBlow(null, null, -1, -1);
				blow.InflictedDamage = (int)BlowDamage;
				blow.SwingDirection = dir;
				blow.Direction = dir;                                  // 方向 = 朝玩家（"往回拽"）
				blow.BlowFlag = BlowFlags.KnockBack
					| (BlowKnockDownFlag ? BlowFlags.KnockDown : BlowFlags.None)
					| BlowFlags.NoSound;                               // 音效在伤害检查之前就播了 ⇒ 必须自己抑制，不然来一声"被打中"
				blow.DamageCalculated = true;

				AttackCollisionData collision = AttackCollisionData.GetAttackCollisionDataForDebugPurpose(
					_attackBlockedWithShield: false, _correctSideShieldBlock: false,
					_isAlternativeAttack: BlowAlternativeAttack,            // true = 确定性击退路径（见字段注释）
					_isColliderAgent: true, _collidedWithShieldOnBack: false, _isMissile: false,
					_isMissileBlockedWithWeapon: false, _missileHasPhysics: false, _entityExists: false,
					_thrustTipHit: false, _missileGoneUnderWater: false, _missileGoneOutOfBorder: false,
					CombatCollisionResult.StrikeAgent, -1, 0, (int)DamageTypes.Blunt, blow.BoneIndex,
					BoneBodyPartType.Chest, -1, Agent.UsageDirection.AttackLeft, -1,
					CombatHitResultFlags.NormalHit, 0.5f, 1f, 0f, 0f, 0f, 0f, 0f, 0f,
					Vec3.Up, dir, blow.GlobalPosition, Vec3.Zero, Vec3.Zero, victim.Velocity, Vec3.Up);

				victim.RegisterBlow(blow, in collision);
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 勾人：引擎冲量出手异常（{ex.GetType().Name}：{ex.Message}）");
			}
			finally
			{
				try { victim.SetMortalityState(prev); } catch (Exception) { }   // 还原 —— 绝不留无敌状态
			}
		}

		/// <summary>
		/// **手搓一发"掀下马 / 让马扬蹄"的 blow**（`custom.grapple horse down blow|rear` 的引擎侧，2026-10-13）。
		///
		/// 为什么要手搓：引擎的**自然路径很窄** —— `CanWeaponDismount` 只认「**步下**攻击者 + 挥砍 + `WeaponFlags.CanHook`」
		/// 或「戳刺 + `WeaponFlags.CanDismount`」，再过 `DecideAgentDismountedByBlow`（不能打死 + 没被 ShrugOff + 穿透 vs 抗性）。
		/// 而 `BlowFlags.CanDismount` / `BlowFlags.MakesRear` 在**托管层只写不读**（唯二写点都在 `Mission` 构造 blow 处）
		/// ⇒ 我们自己往 blow 上置这两位，就等于**替引擎下"把他掀下去 / 让马扬蹄"这道指令**，上面那整套判定全部绕开。
		///
		/// 门槛链（反编译实锤）：`Agent.RegisterBlow` → `HandleBlow`，**唯一硬门槛 = `InflictedDamage &gt; 0`**
		/// （所以照 <see cref="FireYankBlow"/> 的老办法：临时 Immortal 顶着，伤害数只当力度、血一滴不掉）→
		/// `Mission.OnAgentHit` 广播 → `HandleBlowAux(ref b)`（**native 在这里读 flag**）。
		/// ⚠️ 两个已知代价：① 走 `Agent.RegisterBlow` **不触发** `MissionBehavior.OnRegisterBlow`（那个只在原生管线里调），
		/// 但**会**触发 `OnAgentHit`（犯罪/警戒体系可能记账）；② native 内部还会不会有别的条件 —— **读不到，只能实机验**
		/// （判据 = 引擎选的受击动画落在 `act_rider_only_fall_*` / `act_fall_rider_*`）。
		/// </summary>
		/// <param name="victim">挨打的那个：掀人 = **骑手**；扬蹄 = **马**。</param>
		/// <param name="rear">true = 给**马**发 `MakesRear`（扬蹄）；false = 给**骑手**发 `CanDismount`（掀下马）。</param>
		/// <param name="damage">力度（写进 blow 的伤害数；Immortal 顶着所以不扣血）。</param>
		/// <param name="dirToward">推他的方向（世界系水平向量）；留空（`default`）= 自动 ——
		/// 掀人用"从他右手侧"，扬蹄用"正面顶"（照自然路径的条件）。拉下马时传"目标 → 我们"，读感是"被绳扯下来"。</param>
		/// <returns>null = 已出手；非 null = 出错原因（命令直接回给用户）。</returns>
		public static string FireDismountBlow(Agent victim, bool rear, float damage, Vec3 dirToward = default(Vec3))
		{
			if (victim == null) return "no victim";
			Agent.MortalityState prev = Agent.MortalityState.Mortal;
			bool hadPrev = false;
			try { prev = victim.CurrentMortalityState; hadPrev = true; } catch (Exception) { }
			try
			{
				victim.SetMortalityState(Agent.MortalityState.Immortal);   // 血不掉（同上）

				Vec3 dir;
				bool given = false;
				try { dir = dirToward; dir.z = 0f; given = dir.LengthSquared > 1e-6f; }
				catch (Exception) { dir = Vec3.Zero; }
				if (!given)
				{
					try { dir = victim.LookDirection; } catch (Exception) { dir = Vec3.Forward; }
					dir.z = 0f;
					if (dir.LengthSquared < 1e-4f) dir = Vec3.Forward;
					dir = dir.NormalizedCopy();
					// 马：**正面顶**（照自然路径 `DecideMountRearedByBlow` 的条件 `Dot(blow.Direction, 马朝向) < −0.35`）；
					// 骑手：**从右手侧掀**（"被撞下马"的自然方向）。
					dir = rear ? -dir : new Vec3(dir.y, -dir.x, 0f);
				}
				else
				{
					dir = dir.NormalizedCopy();
				}

				Agent attacker = Agent.Main;
				Blow blow = new Blow(attacker != null ? attacker.Index : -1);
				blow.DamageType = DamageTypes.Blunt;
				blow.BoneIndex = victim.Monster != null ? victim.Monster.HeadLookDirectionBoneIndex : (sbyte)0;
				blow.VictimBodyPart = BoneBodyPartType.Chest;
				blow.GlobalPosition = victim.Position + Vec3.Up * 1.0f;
				blow.BaseMagnitude = damage;
				blow.WeaponRecord.FillAsMeleeBlow(null, null, -1, -1);
				blow.InflictedDamage = (int)damage;
				blow.SwingDirection = dir;
				blow.Direction = dir;
				blow.BlowFlag = BlowFlags.NoSound                     // 音效在伤害检查之前播 ⇒ 必须自己抑制
					| (rear ? BlowFlags.MakesRear : BlowFlags.CanDismount);
				blow.DamageCalculated = true;

				AttackCollisionData collision = AttackCollisionData.GetAttackCollisionDataForDebugPurpose(
					_attackBlockedWithShield: false, _correctSideShieldBlock: false,
					_isAlternativeAttack: true,                        // 确定性路径（同 FireYankBlow 的口径）
					_isColliderAgent: true, _collidedWithShieldOnBack: false, _isMissile: false,
					_isMissileBlockedWithWeapon: false, _missileHasPhysics: false, _entityExists: false,
					_thrustTipHit: false, _missileGoneUnderWater: false, _missileGoneOutOfBorder: false,
					CombatCollisionResult.StrikeAgent, -1, 0, (int)DamageTypes.Blunt, blow.BoneIndex,
					BoneBodyPartType.Chest, -1, Agent.UsageDirection.AttackLeft, -1,
					CombatHitResultFlags.NormalHit, 0.5f, 1f, 0f, 0f, 0f, 0f, 0f, 0f,
					Vec3.Up, dir, blow.GlobalPosition, Vec3.Zero, Vec3.Zero, victim.Velocity, Vec3.Up);

				victim.RegisterBlow(blow, in collision);
				return null;
			}
			catch (Exception ex)
			{
				return $"{ex.GetType().Name}: {ex.Message}";
			}
			finally
			{
				if (hadPrev) { try { victim.SetMortalityState(prev); } catch (Exception) { } }
			}
		}

		/// <summary>Blow 模式的结果读数：引擎到底推没推、推了多远、往哪边 —— 这是这条 A/B 的判据。</summary>
		private void LogBlowResult()		{
			try
			{
				Vec3 d = _target.Position - _blowOrigin;
				Vec3 toPlayer = _attacker != null ? _attacker.Position - _blowOrigin : Vec3.Zero;
				d.z = 0f; toPlayer.z = 0f;
				float toward = (d.Length > 0.01f && toPlayer.Length > 0.01f)
					? Vec3.DotProduct(d.NormalizedCopy(), toPlayer.NormalizedCopy())
					: 0f;
				// 🔴 顺便打出**引擎选中了哪条受击动画** —— 这是"blowforce 到底改了什么"的唯一可见证据：
				//    原版那 322 条受击动画是按 (is_heavy, direction, body_part, impact 0~5) 查表选的
				//    （见 Knowledge/csdn_column_articles/骑砍Ⅱ霸主MOD开发(29)-Blow伤害反馈.md 的管线图）。
				//    力度旋钮若能改变"选中的那一档"，这里就能看到动画名换家族；换不了 = 这个数只管判定过不过。
				string act = "-";
				try { act = V.ActName(_target, 0); } catch (Exception) { }
				DebugLogger.Log($"[Grapple] 勾人：引擎冲量结果 —— 位移 {d.Length:F2}m"
					+ $"（朝玩家分量 {toward:+0.00;-0.00}：+1 = 正朝玩家 · −1 = 被推远离）"
					+ $" · 目标血 {_target.Health:F0}（Immortal 已还原 ⇒ 应该一滴没掉）"
					+ $" · 力度={BlowDamage:F0} · 引擎选的受击动画={act}");
			}
			catch (Exception) { }
		}

		private BindEvent TickBound(float dt)
		{
			_boundTimer += dt;

			// 🔴 **绳长上限（牵着走 / 拖在地上）走在最前**：它可能在这一帧把人挪走 / 把他放倒
			//    ⇒ 下面的钩位置、朝向都得按"挪完之后"的算（不然绳与钩差一帧，看着像甩）。
			TickLeash(dt);

			_hookPos = ChestOf(_target);
			_hookDir = AwayFromPlayerDir();

			// 【拉紧】拉倒之后的**定时自动起身**（用户 2026-10-10 拍板："过一会也会起来"）。
			// 到点 Force `rise-trigger` ⇒ 播 `binded_lay_end`（2.33 s）→ 状态机自动落回站缚。
			// 观感验收模式（`bind anim …` 锁了某一段）不倒计时 —— 那是用户正在看的东西。
			if (_layTimer > 0f && _forcedAnim == null)
			{
				_layTimer -= dt;
				if (_layTimer <= 0f)
				{
					_layTimer = 0f;
					_lying = false;
					ForceEvent(BindAnimConditions.RiseTrigger);
					DebugLogger.Log($"[Grapple] 勾人：拉倒计时到（{LaySeconds:F1}s）⇒ 自动起身");
				}
			}

			// 超时挣脱：**默认关**（BoundSeconds = 0 = 永不）—— 解开只由玩家主动做（用户 2026-10-09 裁定）
			if (BoundSeconds > 0f && _boundTimer >= BoundSeconds)
			{
				DebugLogger.Log($"[Grapple] 勾人：捆缚超时（{BoundSeconds:F0}s）⇒ 目标挣脱");
				Release("timeout");
				return BindEvent.Finished;
			}
			return BindEvent.None;
		}

		/// <summary>
		/// **绳长上限 + 拽着跟上 / 拖在地上**（每帧，缚那一相跑）。机制说明见本文件顶部那段。
		///
		/// 四拍：
		///   ① **锁长**：第一帧把绳长锁在"此刻手 → 胸口"的距离上（下限/上限 <see cref="LeashMinLength"/> /
		///      <see cref="LeashMaxLength"/>）—— 之后**不再延长**（用户 2026-10-13："以当前长度为上限"）；
		///   ② **量差**：只有**超出**锁定量（且超出 &gt; <see cref="LeashDeadzone"/>）才拽；
		///   ③ **定形态**：绳主骑马在动 / 速度 &gt; <see cref="DragSpeedThreshold"/> ⇒ **拖在地上**（趴缚），
		///      否则 = **牵着走**（`binded_walk`）；
		///   ④ **挪人**：速度 = 绳主水平速度（前馈，跟上就不掉队）+ 超出量 × 弹簧系数，封顶
		///      <see cref="LeashMaxSpeed"/>；水平直写位置（竖直由引擎贴地），朝向按形态给。
		///
		/// 🔴 **被扛着的时候一律不拽**（`_carrier != null`）：那一拍由 <see cref="GrappleCarry"/> 每帧摆位，
		///    两边同时写同一个人的位置 = 打架（人会在两个点之间抖）。
		/// </summary>
		private void TickLeash(float dt)
		{
			if (!LeashEnabled || _target == null || _attacker == null || !_leashHadAnchor || _carrier != null)
			{
				StopLeash("off / no anchor / being carried");
				return;
			}

			Vec3 chest = ChestOf(_target);
			Vec3 d = chest - _leashAnchor;
			d.z = 0f;
			float dist = d.Length;
			_leashDist = dist;

			// ① 锁长：绑上那一刻的跨度 = 上限
			//    🔴 乘 <see cref="LeashSlackAtLock"/>（默认 1.05 = 与绳平时的"自动长度留 5% 余量"同一个口径）：
			//       锁在**绳此刻真实的那点长度**上，锁定那一帧看不出任何变化（不乘 = 绳当场被拉直、跳一下）。
			//    🔴 **不设上限**（只托底）：上限压小 = 一上来就有"超出量" = 人当场被拽飞，见 LeashMaxLength 注释。
			if (!_leashLocked)
			{
				_leashLength = MathF.Max(LeashMinLength, dist * LeashSlackAtLock);
				_leashLocked = true;
				DebugLogger.Log($"[Grapple] 牵绳：绳长锁定 = {_leashLength:F2} m（手 → 胸口 {dist:F2} m × {LeashSlackAtLock:F2}）"
					+ " —— 之后走远不再延长，超出就拽着他跟上（要松手才解）");
			}

			// ② 量差
			//    🔴 **进出用两个阈值**（2026-10-14 实机抓到）：单阈值下"超出量"正好卡在死区边上时，
			//       `松了 ⇄ 拖在地上` 会**每帧翻一次**（实测日志里 200 行里 93 次翻转）——
			//       既刷屏，又让状态机的"被拖行 ⇄ 站缚"跟着抖。进用死区、出用死区的一半。
			float excess = dist - _leashLength;
			if (!_leashEngaged)
			{
				if (excess > LeashDeadzone && dist > 1e-3f) _leashEngaged = true;
			}
			else if (excess < LeashDeadzone * LeashReleaseRatio)
			{
				_leashEngaged = false;
			}
			if (!_leashEngaged || dist < 1e-3f)
			{
				StopLeash(excess > 0f ? $"slack ({excess:F2} m < {LeashDeadzone:F2} m)" : "slack");
				return;
			}

			// ③ 形态：拖在地上 / 牵着走
			//    🔴 判据 = **这条走路动画演不演得过来**（2026-10-14 用户裁定："倍率太高就自动进入拉倒"）：
			//       需要的倍率 = **绳主的持续速度** ÷ 动画自然速率；超过 <see cref="WalkMaxMult"/> ⇒ 不再硬撑走着跟，
			//       直接改"拖在地上"（趴着滑行不需要跟速率，也没有腿在滑）。
			//    🔴 判据用**绳主速度**、不用本帧拽速：拽速里有"追回超出量"那一份，会在起步/加速的瞬间冲高，
			//       拿它判 = 一加速就翻形态、下一帧又翻回来（来回切）。绳主速度经 `AverageVelocity` 本身就是平滑量。
			//    🔴 进出用**两个阈值**（<see cref="AnimLatch"/>）：进 2.0x / 出 1.4x —— 单阈值会在门槛附近来回翻。
			float leadSpeed = HorizontalSpeed(_attacker);
			bool mountedLead = DragWhenMounted && IsRidingSomething(_attacker) && leadSpeed > DragMountedMinSpeed;
			float leadMult = WalkNaturalSpeed > 0.05f ? leadSpeed / WalkNaturalSpeed : 1f;
			_dragLatch.SetThresholds(WalkMaxMult, WalkMaxMult * WalkDragExitRatio);
			bool dragged = mountedLead || _dragLatch.Update(leadMult) > 0;

			// ④ 挪人 + ⑤ 定倍率
			//    🔴 牵着走时速度**封顶在"动画演得过来"那一档**（倍率上限 × 自然速率）—— 多出来的不硬追
			//       （追了就是滑步）：绳会稍微绷过一点，等绳主慢下来再自己收回去。
			float wantSpeed = MathF.Min(LeashMaxSpeed, leadSpeed * LeashFeedForward + excess * LeashStiffness);
			float speed = dragged ? wantSpeed : MathF.Min(wantSpeed, WalkMaxMult * WalkNaturalSpeed);
			_leashSpeed = speed;
			_leashMult = dragged
				? 1f                                                            // 趴着被拖：不需要跟速率
				: MathF.Max(WalkMinMult, MathF.Min(WalkMaxMult,
					WalkSpeedOverride > 0f
						? WalkSpeedOverride
						: speed / MathF.Max(0.05f, WalkNaturalSpeed)));         // 强制档 / 自适应档
			try
			{
				Vec3 dir = d * (-1f / dist);                         // 从胸口指向手 = 被拽的方向
				_target.TeleportToPosition(_target.Position + dir * (speed * dt));
				// 朝向：牵着走 = 朝行进方向（他自己在走）；拖在地上 = 背朝行进方向（脚在前头在后）
				Vec3 face = dragged && DragFaceAway ? -dir : dir;
				_target.SetMovementDirection(face.AsVec2);
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 牵绳：挪人失败（{ex.GetType().Name}: {ex.Message}）—— 这一帧不拽");
				return;
			}

			// ⑤ 形态落到状态机（**只有它变的那一刻**做事，其余每帧只填上下文）
			if (dragged)
			{
				_layTimer = 0f;                                      // 拖着期间不倒计时起身（松了才起）
				if (!_lying) EnterDraggedLay(leadSpeed);             // 站着/走着 ⇒ 先放倒，之后一直趴着被拖
			}

			_leashPulling = true;
			_leashDragged = dragged;
			if (_ctx != null)
			{
				_ctx.BeingPulled = !dragged;    // 🔴 两个标志互斥（拖在地上那条不走"被拖行"）
				_ctx.BeingDragged = dragged;
			}

			// 只在"开始拽 / 换形态"时各打一行（每帧打会淹掉日志）
			if (!_leashPullingPrev || _leashDraggedPrev != dragged)
			{
				DebugLogger.Log($"[Grapple] 牵绳：{(dragged ? "拖在地上" : "牵着走")} —— 距离 {dist:F2} m > 绳长 {_leashLength:F2} m"
					+ $"（超出 {excess:F2} m）· 绳主速度 {leadSpeed:F1} m/s ⇒ 拽速 {speed:F1} m/s"
					+ (dragged
						? $"（绳主需要的倍率 {leadMult:F2} > {WalkMaxMult:F2} ⇒ 趴缚循环：脚在前头在后）"
						: $"（binded_walk 倍率 {_leashMult:F2} = 拽速 {speed:F2} ÷ 自然速率 {WalkNaturalSpeed:F2}）"));
			}
			_leashPullingPrev = true;
			_leashDraggedPrev = dragged;

			if (LeashProbeSeconds > 0f)
			{
				_leashProbeTimer += dt;
				if (_leashProbeTimer >= LeashProbeSeconds)
				{
					_leashProbeTimer = 0f;
					DebugLogger.Log($"[Grapple] 牵绳探针：手→胸口 {dist:F2} m · 绳长 {_leashLength:F2} m · 超出 {excess:F2} m"
						+ $" · 绳主 {leadSpeed:F1} m/s · 拽速 {speed:F1} m/s · 形态={(dragged ? "拖在地上" : "牵着走")}"
						+ $" · 倍率 {_leashMult:F2}");
				}
			}
		}

		/// <summary>这一帧没在拽（绳松着 / 关掉了 / 被扛着）—— 清标志 + 把"拖完该起身"的倒计时接上。</summary>
		private void StopLeash(string why)
		{
			if (_leashPullingPrev)
			{
				DebugLogger.Log($"[Grapple] 牵绳：松了（{why}）—— 人停下"
					+ (_leashDragged ? $"；拖着的那位按 laytime（{LaySeconds:F1}s）起身" : ""));
			}
			// 拖行结束 ⇒ 趴着的人重新开始"自动起身"倒计时（拖着的时候这个计时是清零的）
			if (_leashDragged && _lying && _layTimer <= 0f && LaySeconds > 0f)
			{
				_layTimer = LaySeconds;
			}
			_leashPulling = false;
			_leashEngaged = false;           // 迟滞态一起清（下次要重新"进"）
			_leashDragged = false;
			_leashSpeed = 0f;
			_leashMult = 1f;                 // 不拽了 ⇒ 动画回原速（TickTargetMachine 每帧把它推给状态机）
			_leashPullingPrev = false;
			_leashDraggedPrev = false;
			if (_ctx != null)
			{
				_ctx.BeingPulled = false;
				_ctx.BeingDragged = false;
			}
		}

		/// <summary>
		/// **把被拽的人放倒**（"拖在地上"那条的入口）—— 只做两件事：记 `_lying` + Force "拉倒"那一拍。
		/// 🔴 **不走 <see cref="EnterYank"/>**：那套自带"朝玩家拖 2 米"的位移曲线，与绳的拽人**两处写位置**；
		///    拖在地上时位移**全交给绳**（绳才是那个"在拉"的东西）。
		/// </summary>
		private void EnterDraggedLay(float leadSpeed)
		{
			_lying = true;
			_layTimer = 0f;
			ForceEvent(BindAnimConditions.YankTrigger);
			DebugLogger.Log($"[Grapple] 牵绳：'{SafeName(_target)}' 被拖倒（绳主 {leadSpeed:F1} m/s"
				+ (IsRidingSomething(_attacker) ? "，骑着马" : "") + $"）⇒ 拖在地上（趴缚循环），"
				+ (LaySeconds > 0f ? $"停下后 {LaySeconds:F1}s 自己起身" : "停下也不会自己起身（laytime=0）"));
		}

		/// <summary>某个 agent 的**水平**速度（米/秒）。用 `AverageVelocity`（native 平均窗口）防瞬时抖动。</summary>
		private static float HorizontalSpeed(Agent a)
		{
			if (a == null) return 0f;
			try
			{
				Vec3 v = a.AverageVelocity;
				return new Vec2(v.x, v.y).Length;
			}
			catch (Exception) { return 0f; }
		}

		// ───────────────────── 牵着走：给绳主下"走到哪"的指令（2026-10-13）─────────────────────
		//
		// 🔴 **谁需要它**：`bind lead` 那条**反向绑**（NPC 牵着玩家走）。
		//    玩家牵 NPC 时不需要 —— 玩家自己会走，绳长上限那条（TickLeash）负责把 NPC 拽着跟上。
		// 🔴 **给绳主下的是引擎的脚本移动**（`SetScriptedPosition`，走引擎寻路：绕障碍、上下台阶都归它），
		//    **不是**我们自己写位置 —— 绳主是自由的（没被冻），就该让引擎好好走。

		/// <summary>`bind lead` 不写米数时的默认距离。</summary>
		public static float LeadDefaultMeters = 6f;

		/// <summary>这一绑里给绳主下过"往前走"的指令没有（诊断用）。</summary>
		private bool _leadActive;
		private Vec3 _leadTarget;

		/// <summary>
		/// **牵着人往前走**（"带着玩家走"那条腿）：给**绳主**下一个"走到前方 N 米"的脚本移动指令。
		/// 被牵的人由绳长上限那条拽着跟上 —— 两者天然配合：绳主走 → 绳绷紧 → 被牵的人被拽着走。
		/// </summary>
		public string LeadForward(float meters, bool run)
		{
			if (_phase == Phase.None || _attacker == null) return "Error: no active bind (nothing to lead).";
			if (ReferenceEquals(_attacker, Agent.Main)) return "Error: the rope holder is the player - just walk (the bound one follows by the rope).";
			if (meters <= 0f) meters = LeadDefaultMeters;
			try
			{
				Vec3 fwd = _attacker.LookFrame.rotation.f;
				fwd.z = 0f;
				fwd = fwd.LengthSquared > 1e-4f ? fwd.NormalizedCopy() : Vec3.Forward;
				Vec3 to = _attacker.Position + fwd * meters;
				AgentControlHelper.ScriptedMoveToPoint(_attacker, to, run);
				_leadTarget = to;
				_leadActive = true;
				DebugLogger.Log($"[Grapple] 牵人：绳主 {SafeName(_attacker)} 出发 → {Fmt(to)}（{meters:F1} m{(run ? "，跑" : "")}）"
					+ " —— 绳一绷，被牵的就被拽着跟上来");
				return $"OK: {SafeName(_attacker)} walks {meters:F1} m{(run ? " (running)" : "")}; the bound one is dragged along by the rope.";
			}
			catch (Exception ex)
			{
				return $"Error: lead failed ({ex.GetType().Name}: {ex.Message})";
			}
		}

		/// <summary>绳主停下（解除上一条 <see cref="LeadForward"/> 的脚本移动）。</summary>
		public string LeadStop()
		{
			if (_attacker == null) return "Error: no rope holder.";
			try { _attacker.DisableScriptedMovement(); }
			catch (Exception ex) { return $"Error: stop failed ({ex.GetType().Name}: {ex.Message})"; }
			_leadActive = false;
			DebugLogger.Log($"[Grapple] 牵人：绳主 {SafeName(_attacker)} 停下");
			return $"OK: {SafeName(_attacker)} stops walking.";
		}

		/// <summary>他骑着东西吗（骑马；`MountAgent` 就是引擎的"我骑在谁身上"）。</summary>
		private static bool IsRidingSomething(Agent a)
		{
			if (a == null) return false;
			try { return a.MountAgent != null; }
			catch (Exception) { return false; }
		}

		/// <summary>
		/// 复核"压制还在不在"（**所有相位都跑**，见 <see cref="Tick"/>）：
		/// ① 脚本旗标（`DoNotRun | NoAttack`）② **原版 AI 冻结**（<see cref="SetTargetAIPaused"/>）。
		///
		/// 🔴 **姿势那一项 2026-10-10 已从这里删除** —— 姿态归状态机，它自带 `RecheckStolen`
		///    （每 0.5 s 核对 0 号通道，被抢就补写回来）。两处都抢 = 同一个语义两处实现，
		///    迟早有一处忘改；而且状态机补写用的是它自己的状态定义，比这里写死动作名更对。
		/// </summary>
		private void VerifySuppression()
		{
			if (_target == null) return;

			// 🔴 **"拉下马"那一相不压**（2026-10-13）：那一段靠的是**引擎自己的下马事件**，
			//    而我们**没验过"AI 冻结着还能不能吃下马事件"**（`SetIsAIPaused` 改的是
			//    `AIStateFlag.Paused`，与 native 处理 EventControlFlags 是两回事，但没实测）
			//    ⇒ 不去冒这个险：等他落地、进"缠"那一刻才冻（见 BeginWrapping）。
			if (_phase == Phase.Dismounting)
				return;

			// ① 旗标：幂等重压（被别的系统清掉 = 站着的人立刻恢复原生 AI）。
			//    ⚠️ 用"或"叠加、**不覆盖** —— 直接赋值会把 BoundAction 每 0.2s 设的 `InConversation`
			//    抹掉，两边一帧一变地互相打脸（那一位语义是"别动，你正在交互中"）。
			try
			{
				_target.SetScriptedFlags(_target.GetScriptedFlags()
					| Agent.AIScriptedFrameFlags.DoNotRun
					| Agent.AIScriptedFrameFlags.NoAttack);
			}
			catch (Exception) { }

			// ② 原版 AI 冻结：幂等重压（引擎的部署收尾会把**全场**解冻，战斗里尤其要盯）
			//    🔴 走 HoldTarget 而不是只 SetTargetAIPaused：**目标是玩家**时它还会把"控制器归 AI"
			//       一起补回来（不然玩家中途被别的系统还回控制器，就能自己跑掉了）。
			HoldTarget(_target);
		}

		/// <summary>命令入口：立刻放开当前目标（`custom.grapple bind release`）。</summary>
		public bool ReleaseNow()
		{
			if (!IsActive) return false;
			Release("command");
			return true;
		}

		/// <summary>
		/// **放人**（松绳 / 收摊 / 被新的钩替换）。
		///
		/// 🔴 **2026-10-10 起"要不要演起身"不在这里**：本方法只做三件事 ——
		///    ① 置上下文的 `Released`（**出机正门**，见 <see cref="BindAnimContext.Released"/>）；
		///    ② 把状态机**连同上下文一起搬进"收尾槽"**（<see cref="_releasing"/>）；
		///    ③ 剩下的交给 <see cref="TickPending"/>：机器自己走到机外之后再还 AI。
		///    走哪条路出机（站着直接出 / 趴着先演 2.33 s 起身）由 `bind.xml` 里那两条 `bind-released` 边决定。
		/// </summary>
		public void Release(string reason)
		{
			Agent t = _target;
			_phase = Phase.None;
			_attacker = null;
			_target = null;
			_carrier = null;
			_layTimer = 0f;
			_struggling = false;
			_note = "released (" + reason + ")";

			// 绳长上限：这一绑结束 ⇒ 锁解掉（下一绑重新锁）
			_leashLocked = false;
			_leashLength = 0f;
			_leashPulling = false;
			_leashDragged = false;
			_leashSpeed = 0f;
			_leashPullingPrev = false;
			_leashDraggedPrev = false;

			if (_ctx != null)
			{
				_ctx.Released = true;
				// 🔴 **牵绳那两个标志必须一起清掉**（2026-10-13）：它们在图里是**普通谓词边**
				//    （`站缚 ⇄ 被拖行`），而"放人"那条边（`bind-released`）排在同一条状态轨道上 ——
				//    标志留着为真 = **放人那条边永远轮不到**（人被放开却还演着"被牵着走"，直到别的边把它踢走）。
				//    释放之后牵绳那一拍也不会再跑（相位已 None）⇒ 不在这里清就没人清了。
				_ctx.BeingPulled = false;
				_ctx.BeingDragged = false;
			}

			// 机器搬进收尾槽（与"下一绑"互不干扰：Begin 会重置 _machine/_ctx 那两组）
			if (t != null && _machine != null)
			{
				// 前一个还没还完 AI（极端情况：连着两次替换）⇒ 立刻把它还了，别丢
				if (_releasing != null) FinishRelease("superseded");
				_releasing = t;
				_releaseMachine = _machine;
				_releaseCtx = _ctx;
				_releaseTimer = 0f;
			}
			else if (t != null)
			{
				// 还没进过状态机（缠都没缠完就被放）⇒ 没什么动画可等，直接还 AI
				_pending.Add(new Pending { Agent = t, Timer = 0.05f, Why = reason + " (never entered machine)" });
			}
			_machine = null;
			_ctx = null;

			DebugLogger.Log($"[Grapple] 勾人：放人（{reason}）—— 交状态机出机（站着=直接出；趴着=先演起身），"
				+ "走到机外之后把 AI 还回去");
		}

		/// <summary>收钩/异常时的强制解除（<see cref="GrappleLogic.Release"/> 会调）—— 同样走起身队列。</summary>
		public void Abort(string reason)
		{
			if (_phase == Phase.None)
			{
				return;
			}
			Release("abort: " + reason);
		}

		/// <summary>
		/// **"解"的收尾：推进还在演起身的那台机**（每帧，由 <see cref="TickPending"/> 调）。
		/// 上下文里 `Released` 已经是 true ⇒ 机器自己会 趴缚 → 起身 → 站缚 → 机外 走完。
		/// </summary>
		private void TickReleaseMachine(float dt)
		{
			if (_releaseMachine == null || _releasing == null) return;
			if (!AgentControlHelper.SafeIsActive(_releasing)) return;
			_releaseCtx.Struggling = false;
			_releaseCtx.CarrierMoving = false;
			_releaseCtx.AnimRemainFrac = _releaseMachine.CurrentRemainFrac;
			_releaseMachine.Tick(_releasing, dt);
		}

		/// <summary>收尾结束：把 AI 还回去 + 清空收尾槽。</summary>
		private void FinishRelease(string why)
		{
			Agent a = _releasing;
			_releasing = null;
			_releaseMachine = null;
			_releaseCtx = null;
			_releaseTimer = 0f;
			if (a == null) return;
			RestoreAgent(a);
			DebugLogger.Log($"[Grapple] 勾人：起身收尾完成（{why}）—— AI 已归还");
		}

		/// <summary>
		/// 每帧推进**收尾**（<see cref="GrappleLogic.OnMissionTick"/> **无条件**每帧调一次）：
		/// ① 「解」的机器跑到机外 / 超时 ⇒ 还 AI；
		/// ② 老的"没进过状态机就被放"的短队列。
		///
		/// 🔴 **为什么收尾必须挂在"无条件每帧"这条路上**：放人之后钩已经收了，
		///    <see cref="Tick"/> 那条（钩的相位分支）根本不会再被调 —— 挂那儿 = 起身动画演完没人还 AI。
		/// </summary>
		public void TickPending(float dt)
		{
			// ① 起身/出机
			if (_releasing != null)
			{
				TickReleaseMachine(dt);
				_releaseTimer += dt;
				bool left = _releaseMachine == null
							|| _releaseMachine.Current == AgentAnimStateMachine.OutsideState
							|| !AgentControlHelper.SafeIsActive(_releasing);
				if (left)
				{
					FinishRelease("machine left");
					return;
				}
				if (_releaseTimer >= StandupSeconds + 6f)
				{
					// 防呆：机器没走到机外也得还 AI（漏了 = 那个人被永久冻住，比"多站两秒"严重得多）
					DebugLogger.Log($"[Grapple] 勾人：🔴 起身收尾超时（{_releaseTimer:F1}s，状态={_releaseMachine?.Current}）"
						+ " ⇒ 强制还 AI");
					FinishRelease("timeout");
					return;
				}
			}

			// ② 老队列（"没进过状态机就被放"的短延迟）
			if (_pending.Count == 0) return;
			for (int i = _pending.Count - 1; i >= 0; i--)
			{
				Pending p = _pending[i];
				p.Timer -= dt;
				if (p.Timer > 0f) continue;
				RestoreAgent(p.Agent);
				_pending.RemoveAt(i);
			}
		}

		/// <summary>把 AI 还给目标：清脑标记 + 清行为 + 解锁旗标（照 KnockoutFlow 唤醒那条顺序）。</summary>
		private static void RestoreAgent(Agent a)
		{
			if (a == null) return;

			AgentBrain brain = null;
			try { brain = AgentAIController.GetBrainForAgent(a); }
			catch (Exception ex) { DebugLogger.Log($"[Grapple] 勾人：取脑异常（{ex.GetType().Name}）"); }

			// 🔴 **顺序要紧**（2026-10-09 TODO 2）：先把标记清掉，`ClearAllActions` 才肯动手
			//    （被捆者拒清 —— 那条守卫见 AgentBrain.ClearAllActions）。反过来 = 占位永远拆不掉。
			try { if (brain != null) brain.IsBound = false; }
			catch (Exception ex) { DebugLogger.Log($"[Grapple] 勾人：清被捆标记异常（{ex.GetType().Name}）"); }

			try { brain?.ClearAllActions(); }       // 清掉那条 BoundAction 占位
			catch (Exception ex) { DebugLogger.Log($"[Grapple] 勾人：清脑状态异常（{ex.GetType().Name}）"); }

			try { AgentControlHelper.ForceUnlockAgent(a); }   // 清 scripted 旗标 + 速度限制 + 交还 AI 控制器
			catch (Exception ex) { DebugLogger.Log($"[Grapple] 勾人：解锁目标异常（{ex.GetType().Name}）"); }

			// 🔴 **解冻原版 AI —— 与 <see cref="Begin"/> 里的 SetIsAIPaused(true) 配对，缺一不可**：
			//    漏了这一步，目标会被**永久冻住**（站着不动、不打架、不逃）。
			//    后两步照原版部署收尾 / 织丰那套解冻配方抄（`Shokuho.dll` 反编译实读）——
			//    只解冻不清缓存的话，AI 恢复后可能还攥着"冻住期间"那份过期的敌人/行为参数。
			SetTargetAIPaused(a, false);
			try { a.ResetEnemyCaches(); } catch (Exception) { }
			try { a.HumanAIComponent?.SyncBehaviorParamsIfNecessary(); } catch (Exception) { }

			// 🔴 **玩家当目标时**：把控制器还给玩家 —— **必须排在 `ForceUnlockAgent` 之后**
			//    （它内部"确保控制器回归 AI"那一步会把它抢成 AI，先还 = 白还，见 HoldTarget 注释）。
			if (ReferenceEquals(a, Agent.Main))
			{
				try { V.SetPlayerControlFrozen(a, false); }
				catch (Exception ex) { DebugLogger.Log($"[Grapple] 勾人：还玩家控制器失败（{ex.GetType().Name}: {ex.Message}）—— 用控制台/重进场景恢复"); }
			}

			DebugLogger.Log($"[Grapple] 勾人：AI 已还给 {SafeName(a)}（被捆标记已清、占位已拆、旗标已解锁、原版 AI 已解冻）"
				+ $"；0 号通道此刻 = {SafeOrNone(a)}（应是 act_none / 引擎自己的动作 —— 若还挂着 act_grapple_bound_* 就是没还干净）");
		}

		/// <summary>目标 0 号通道现在是什么动作（还 AI 时打一行做判据：应该是 act_none）。</summary>
		private static string SafeOrNone(Agent a)
		{
			try { return V.ActName(a, 0) ?? "?"; }
			catch (Exception) { return "(read failed)"; }
		}

		// ───────────────────────────── 绳尾钉（缠绕态）─────────────────────────────

		/// <summary>
		/// 把"绳尾该在哪"喂给绳（每帧、在 <see cref="GrappleRope.Tick"/> 之前调）。
		/// 缠绕时 = 沿同一个圆铺、**相位落后钩头一点**（绳跟着钩走，不是绳头领先），半径比钩大 2 厘米；
		/// 其余相位 = 解除（绳自己按"手 → 钩"拉直）。
		/// </summary>
		public void ApplyRopeTailPin(GrappleRope rope)
		{
			if (rope == null) return;
			if (_phase != Phase.Wrapping)
			{
				rope.ClearTailPin();
				return;
			}
			// 每点弧长 ≈ 绳长 ÷ 段数（绳长每帧在变，所以每帧重算点数）
			float rest = rope.Length / Math.Max(1, rope.Segments);
			if (rest <= 0.001f) rest = 0.2f;
			int count = (int)MathF.Ceiling(WrapTailSpan / rest);
			if (count < 2) count = 2;
			if (count > 24) count = 24;
			if (_tailBuf == null || _tailBuf.Length < count) _tailBuf = new Vec3[24];

			float dAng = rest / MathF.Max(0.05f, _wrapRadius) * _wrapSign;
			for (int k = 0; k < count; k++)
			{
				_tailBuf[k] = CirclePoint(_wrapAngle - dAng * (k + 1), _wrapRadius + 0.02f, _wrapZ);
			}
			rope.SetTailPin(count, _tailBuf);
		}

		// ───────────────────────────── 几何小工具 ─────────────────────────────

		private Vec3 CirclePoint(float ang, float r, float z)
		{
			return new Vec3(_center.x + MathF.Cos(ang) * r, _center.y + MathF.Sin(ang) * r, z);
		}

		/// <summary>圆周切线（钩头绕行时局部 +Z 朝这个方向 —— 看着像"钩在绕着走"）。</summary>
		private Vec3 TangentAt(float ang, float r, float z)
		{
			Vec3 ahead = CirclePoint(ang + 0.05f * _wrapSign, r, z);
			Vec3 d = ahead - CirclePoint(ang, r, z);
			return d.LengthSquared > 1e-8f ? d.NormalizedCopy() : Vec3.Forward;
		}

		/// <summary>倒/缚阶段的钩朝向：背向玩家（钩挂在人身上那个方向）。</summary>
		private Vec3 AwayFromPlayerDir()
		{
			try
			{
				if (_attacker != null && _target != null)
				{
					Vec3 d = _target.Position - _attacker.Position;
					d.z = 0f;
					if (d.LengthSquared > 0.01f) return d.NormalizedCopy();
				}
			}
			catch (Exception) { }
			return Vec3.Forward;
		}

		/// <summary>
		/// 目标**躯干**的世界位置：优先读 `HumanBone.Spine2` 的骨（复用已验证的双解释读法
		/// <see cref="SpellCastInput.TryReadBoneWorld"/>），读不到退回"脚底 + 1.15 米"。
		/// </summary>
		internal static Vec3 ChestOf(Agent a)
		{
			if (a == null) return Vec3.Zero;
			try
			{
				var v = a.AgentVisuals;
				if (v != null)
				{
					sbyte bone = v.GetRealBoneIndex(HumanBone.Spine2);
					if (bone >= 0 && SpellCastInput.TryReadBoneWorld(a, bone, 2.0f, out Vec3 w, out _))
					{
						return w;
					}
				}
			}
			catch (Exception) { }
			try { return a.Position + Vec3.Up * 1.15f; }
			catch (Exception) { return Vec3.Zero; }
		}

		// ───────────────────────────── 姿态 / 记账 ─────────────────────────────
		//
		// 🔴 **本文件不再自己播姿态**（2026-10-10）：动作名与"什么时候播哪条"全在
		//    `ModuleData/statemachines/bind.xml`，本文件只按"时刻名"Force（见 <see cref="ForceEvent"/>）。
		//    动作名解析不到时的诊断由状态机负责（`[Anim:bind] 动作 '…' 解析为 act_none`）。

		/// <summary>
		/// **观感验收**：按"时刻名"把目标推到他一生里的某一段（`lay|cycle|standup|hit|carry|put|auto`）。
		/// <paramref name="which"/> = null/auto = 交回流程。
		///
		/// 🔴 **只有时刻名，没有状态名**（照飞行那条规矩）—— 状态名可以在编辑器里随手改，
		///    写死在 C# 里 = 改完静默失效。别名：`lay`/`cycle` 都是"拉倒"那一刻（拉倒演完自然接趴缚）。
		/// </summary>
		public string ForceAnim(string which)
		{
			if (!IsActive || _target == null) return "Error: no bound target.";
			if (string.IsNullOrEmpty(which) || which == "auto")
			{
				_forcedAnim = null;
				return "OK: bind anim = auto (flow takes over)";
			}
			string token;
			switch (which)
			{
				case "hit": token = BindAnimConditions.HitTrigger; break;
				case "lay":
				case "cycle": token = BindAnimConditions.YankTrigger; break;
				case "standup": token = BindAnimConditions.RiseTrigger; break;
				case "carry": token = BindAnimConditions.CarryTrigger; break;
				case "put": token = BindAnimConditions.PutTrigger; break;
				default:
					return "Error: bind anim expects hit|lay|cycle|standup|carry|put|auto";
			}
			_forcedAnim = which;      // 压住"拉倒后自动起身"的倒计时（否则看一半人自己爬起来）
			_layTimer = 0f;
			if (which == "standup") _lying = false;
			return ForceEvent(token, which == "lay" || which == "cycle" ? LayStartProgress : 0f)
				? $"OK: forced {which}"
				: $"Error: no '{token}' seam in bind.xml";
		}

		/// <summary>观感/调试旋钮：挣扎开关（`站缚 ⇄ 挣扎`）。返回新值。</summary>
		public bool ToggleStruggle()
		{
			_struggling = !_struggling;
			return _struggling;
		}

		/// <summary>在不在闹（诊断用）。</summary>
		public bool StruggleState => _struggling;

		/// <summary>扛人接线（<see cref="GrappleCarry"/> 用）：记下扛人者（只用来读它的速度）。</summary>
		internal void SetCarrier(Agent carrier)
		{
			_carrier = carrier;
		}

		/// <summary>犯罪记账（与击晕同源）：袭击记账 + 犯罪感知 + 目击广播。</summary>
		private static void ReportBind(Agent attacker, Agent target)
		{
			try
			{
				AgentAIController.Instance?.RecordAssaultVictim(target);
				// 罪名词 "Bind"（新增；描述文本 = LWN_crime_witness_act_bind）。
				// 🔴 不用 "Knockout" —— 那把"用绳撂倒"播报成"把人打晕了"，与事实不符。
				AttackTriggerMissionLogic.ReportPlayerMisconduct("Bind", attacker);
				AgentAIController.Instance?.BroadcastEventInRange(
					target.Position, 20f, "WitnessCrime",
					exclude: new HashSet<Agent> { target },
					requireSight: true,
					attacker, target);
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 勾人：犯罪记账异常（{ex.GetType().Name}：{ex.Message}）");
			}
		}

		// ───────────────────────────── 诊断 ─────────────────────────────

		/// <summary>一行状态（命令 `custom.grapple bind state`）。</summary>
		public string StatusLine()
		{
			if (!IsActive)
			{
				string rel = _releasing != null
					? $" | releasing={SafeName(_releasing)} state={_releaseMachine?.Current ?? "-"} t={_releaseTimer:F1}s"
					: "";
				string pend = _pending.Count > 0 ? $" | 起身队列={_pending.Count}" : "";
				return $"bind: idle（总开关 {(Enabled ? "on" : "off")}）{rel}{pend} | 最近一次：{_note}";
			}
			string act = "-";
			try { act = V.ActName(_target, 0); } catch (Exception) { }
			string tgt = SafeName(_target);
			float timer = _phase == Phase.Bound ? _boundTimer : _t;
			float limit = _phase == Phase.Bound ? BoundSeconds
				: (_phase == Phase.Dismounting ? DismountTimeoutSeconds
				: (_phase == Phase.Wrapping ? WrapSeconds
				: (Yank == YankMode.Blow ? BlowYankSeconds : YankTotalSeconds)));
			// 🔴 接管状态（TODO 2 的验收判据，2026-10-09）：脑标记 = 闸门基准（false 时下面几道全不生效）；
			//    事件忽略 = 被挡下的定向事件条数（该涨就涨 = 调度真的被挡住了）。
			//    🔴 2026-10-10：`poseReassert` 那项**删了** —— 姿态抢回改由状态机自带的 `RecheckStolen` 管，
			//       它的证据在 `[Anim:bind] 'X' 被引擎抢走了，重设` 那行（要把 Verbose 打开）。
			//    ⚠️ 控制台返回文本一律英文（CLAUDE.md 控制台纪律）。
			string takeover = "-";
			try
			{
				var b = AgentAIController.GetBrainForAgent(_target);
				// aiPaused 读的是**引擎自己**那份状态（`Agent.IsPaused` ← `AIStateFlag.Paused`），
				// 不是我们的旋钮 —— 这样"旋钮开了但冻结没生效"也能一眼看出来。
				string paused = "?";
				try { paused = _target.IsPaused ? "True" : "False"; } catch (Exception) { }
				takeover = b != null
					? $"bound={b.IsBound} ignored={b.BoundEventsDropped}"
					: "bound=(no brain)";
				takeover += $" aiPaused={paused}";
			}
			catch (Exception) { }
			// 🔴 状态机那一格是**这一版的主判据**：`anim=` 打的是状态机的当前状态 +
			//    它解析到的引擎动作名（对不上就是接线断了）。
			string anim = _machine != null
				? $"{_machine.Current ?? "-"}/{_machine.CurrentAction ?? "-"}"
				: "(no machine)";
			string carry = _carrier != null ? $" carrier={SafeName(_carrier)}" : "";
			// 🔴 牵绳那一格（2026-10-13）：绳长锁没锁 / 手→胸口多远 / 在不在拽 / 走还是拖 ——
			//    "走远了人没跟上来"这类问题的第一判据就是这一格（`leashprobe` 打开还能看连续值）。
			string leash = !LeashEnabled ? "off"
				: !_leashLocked ? "not locked yet"
				: $"{_leashLength:F2}m dist={_leashDist:F2}m pull={(_leashPulling ? (_leashDragged ? "DRAGGED" : "walking") : "slack")}"
					+ (_leashPulling ? $" v={_leashSpeed:F1}m/s x{_leashMult:F2}" : "");
			string lead = _leadActive ? $" | lead→{Fmt(_leadTarget)}" : "";
			return $"bind: {_phase} | 目标={tgt} | {timer:F2}/{limit:F2}s | anim={anim} | 动作={act}"
				+ $" | 钩={Fmt(_hookPos)} | 拖距={DragDistance:F1}m 超时={BoundSeconds:F0}s"
				+ $" 拉倒起身={LaySeconds:F1}s{carry}"
				+ $" | leash[{leash}]{lead}"
				+ $" | takeover[{takeover}]"
				+ (_layTimer > 0f ? $" | 起身倒计时={_layTimer:F1}s" : "")
				+ (_forcedAnim != null ? $" | 🔒强制={_forcedAnim}" : "");
		}

		private static string SafeName(Agent a)
		{
			try { return a?.Name?.ToString() ?? "?"; }
			catch (Exception) { return "?"; }
		}

		private static string Fmt(Vec3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";
	}
}
