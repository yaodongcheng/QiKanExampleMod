using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **步骤 5「勾人」的目标侧控制器**（2026-10-09 立；方案 = plans\钩索-实施计划.md §十三）。
	///
	/// 四拍（钩头命中人之后）：
	///   ① **缠 Wrapping** —— 钩绕目标躯干一圈（半径越绕越紧、高度胸口→腰），绳尾贴身体绕
	///      （<see cref="GrappleRope.SetTailPin"/>，否则绳是两点直线、会从人身上穿过去）；
	///   ② **倒 Yanking** —— 目标被拽向玩家（原地倒下、拖 <see cref="DragDistance"/> 米），
	///      同时起播"躺下去"动画（<see cref="LayAction"/>，从 <see cref="LayStartProgress"/> 起播）；
	///   ③ **缚 Bound** —— 躺地**循环**动画（<see cref="CycleAction"/>，原版地牢囚犯那三条之一）；
	///      AI 被压制（brain.IsBound + 脚本旗标），超时 <see cref="BoundSeconds"/> 自己挣脱；
	///   ④ **解 Releasing** —— 起身动画（<see cref="StandupAction"/>）→ 播完再把 AI 还回去。
	///
	/// 🔴 **平权（铁律 18）**：判定与结算全在本文件（<see cref="Begin"/> / <see cref="Tick"/> /
	///    <see cref="Release"/> / <see cref="Abort"/>），玩家侧只留"输入 + 相机"壳
	///    （<see cref="GrappleLogic"/> 负责把钩的相位与绳的远端接到这里）⇒ 将来 NPC 用钩索直接复用。
	///
	/// 🔴 **依赖的两条已验证先例**（别另创）：
	///    · NPC 动画接管 = `AgentControlHelper.ForcePlayAction`（先例 KnockoutFlow：切 as_human_warrior + 通道 0）；
	///      但**循环件必须显式 `blendOutPeriodToNoAnim = 0`**（默认 0.4 会把躺地循环淡出）。
	///    · "让人一直躺着手脚不动" = 击晕那套（brain 事件 → 清行为 + 永不结束的 StayAction 占位）。
	/// </summary>
	internal sealed class GrappleBind
	{
		public enum Phase { None, Wrapping, Yanking, Bound }

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

		/// <summary>"躺下去"动画从哪个进度起播（0~1）。0.35 = 跳过"慢慢蹲下"的开头，直接进"倒下去"。</summary>
		public static float LayStartProgress = 0.35f;

		/// <summary>
		/// 捆缚**自动挣脱**的超时（秒）。🔴 **默认 0 = 永不挣脱**（2026-10-09 用户裁定："不要让 NPC 自己挣脱"）——
		/// 解开只由玩家主动做：敲 `custom.grapple bind release`，或（待做）走到身边用**交互面板的"松绳"**。
		/// 非 0 时按超时自动放（调试试用）。命令 `custom.grapple bind time &lt;秒&gt;`（0 = 永不）。
		/// </summary>
		public static float BoundSeconds = 0f;

		/// <summary>起身动画时长（秒）—— 起身队列用它决定"多久之后把 AI 还回去"。</summary>
		public static float StandupSeconds = 3.6f;

		/// <summary>拽倒时先把目标转向玩家（`SetMovementDirection`，与 GrapplePull.FaceToward 同一套已验证写法）。</summary>
		public static bool FacePlayerOnYank = true;

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

		/// <summary>姿势被抢回了几次（诊断用：正常应恒为 0；`bind state` 会打出来）。</summary>
		private int _poseReasserts;
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

		// 三条动作名（本模块 action_types.xml 声明 + action_sets.xml 映射到**原版**地牢囚犯躺地 clip；
		// 写错 = 静默 act_none、播不出来 ⇒ `bind state` 会把当前动作名打出来核对）。
		public const string LayAction = "act_grapple_bound_lay";
		public const string CycleAction = "act_grapple_bound_cycle";
		public const string StandupAction = "act_grapple_bound_standup";

		// ───────────────────────────── 运行时状态 ─────────────────────────────

		private Agent _attacker;
		private Agent _target;
		private Phase _phase = Phase.None;
		private float _t;                    // 本相位计时
		private float _boundTimer;
		private string _note = "-";

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

		/// <summary>观感验收：强制目标播某一段（`custom.grapple bind anim lay|cycle|standup`）；null = 交回流程。</summary>
		private string _forcedAnim;

		/// <summary>这一绑有没有真的把人放倒（进过 <see cref="Phase.Bound"/>）——
		/// 决定"放人"时要不要播起身动画（见 <see cref="BeginStandup"/>）。</summary>
		private bool _wentDown;

		/// <summary>本轮拽倒是否已经起播"倒下"动画（三段的第③段只触发一次）。</summary>
		private bool _fallStarted;

		// ───────────────────────────── 只读面（GrappleLogic / 命令用）─────────────────────────────

		public bool IsActive => _phase != Phase.None;
		public Phase Current => _phase;
		public Agent Target => _target;
		public string Note => _note;

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
		/// </summary>
		public bool WantsTautRope => _phase == Phase.Wrapping || _phase == Phase.Yanking;

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
			if (target.MountAgent != null)
			{
				// 骑马的目标 = 另一小步（"拽下马"，方案 §13.5）—— 本版先不受理，钩照旧挂住。
				why = "target mounted (pull-down path = 5B)";
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
			_forcedAnim = null;
			_wentDown = false;
			_fallStarted = false;
			_center = ChestOf(target);
			_startAngle = MathF.Atan2(hitPoint.y - _center.y, hitPoint.x - _center.x);
			_wrapSign = WrapTurns >= 0f ? 1f : -1f;
			_note = "wrapping";
			_hookPos = hitPoint;
			_hookDir = (target.Position - hitPoint).NormalizedCopy();
			// 🔴 **冻住他的原版 AI —— 就在命中的这一刻**（不是等到"缚"那拍）：缠 0.55s + 拽 1.1s
			//    这 1.65 秒里若放任原版 AI 指挥，战斗场景中他会一边被我们拽一边打架/走位
			//    （城镇里看不出来，因为那套原生 AI 本来就不打架）。
			//    注：钩头飞行那 0.5 秒还没冻（那时还没"命中"，不冻是对的 —— 打空了不该动人家）。
			SetTargetAIPaused(target, true);
			DebugLogger.Log($"[Grapple] 勾人：开始缠（{target.Name}）入口={Fmt(hitPoint)} 圈数={WrapTurns:F2} 半径={WrapRadiusStart:F2}→{WrapRadiusEnd:F2} 时长={WrapSeconds:F2}s"
				+ (PauseVanillaAI ? " · 已冻结其原版 AI" : " · （原版 AI 未冻：bind aipause 0）"));
			return true;
		}

		/// <summary>每帧推进（<see cref="GrappleLogic"/> 在钩头相位里调）。</summary>
		public BindEvent Tick(float dt)
		{
			if (_phase == Phase.None) return BindEvent.None;

			// 目标没了 / 死了 / 失效 ⇒ 直接收
			if (_target == null || !AgentControlHelper.SafeIsActive(_target))
			{
				DebugLogger.Log($"[Grapple] 勾人：目标失效 ⇒ 放（{_note}）");
				Release("target gone");
				return BindEvent.Finished;
			}

			// 🔴 **压制复核**（2026-10-09，TODO 2「AI 接管」）：**所有相位都跑** ——
			//    旗标与 AI 冻结都可能被抢/被清（引擎的部署收尾会把全场解冻、别的系统也可能清旗标）；
			//    姿势那一项只在"缚"那拍抢（见 VerifySuppression 里的相位判据）。
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

			switch (_phase)
			{
				case Phase.Wrapping: return TickWrapping(dt);
				case Phase.Yanking: return TickYanking(dt);
				case Phase.Bound: return TickBound(dt);
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

			if (u >= 1f) EnterYank();
			return BindEvent.None;
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
					EnterBound();
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

			// ③ 拽到位那一刻**才**起播"倒下"（顺序：先位移、后倒地 —— 反过来位移会被姿势变化吃掉）
			if (!_fallStarted && _t >= pullEnd && Yank == YankMode.Curve)
			{
				_fallStarted = true;
				PlayOnce(_target, LayAction, LayStartProgress, 0.2f, "lay");
			}

			_hookPos = ChestOf(_target);
			_hookDir = AwayFromPlayerDir();

			if (_t >= fallEnd) EnterBound();
			return BindEvent.None;
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

		/// <summary>Blow 模式的结果读数：引擎到底推没推、推了多远、往哪边 —— 这是这条 A/B 的判据。</summary>
		private void LogBlowResult()
		{
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

		private void EnterBound()
		{
			_phase = Phase.Bound;
			_boundTimer = 0f;
			_note = "bound";
			_wentDown = true;      // 到这一步才是真"放倒" ⇒ 放人时才播起身动画（见 BeginStandup）

			// AI 压制：脚本旗标 + 脑事件（同击晕那套；先把旗标打上，免得 AI 在事件到达前抢动画通道）
			try
			{
				_target.SetScriptedFlags(Agent.AIScriptedFrameFlags.DoNotRun | Agent.AIScriptedFrameFlags.NoAttack);
			}
			catch (Exception) { }

			// 🔴 **标记直接置一份**（2026-10-09 TODO 2 加固；事件只当"叙述 + 占位"用）：
			//    战斗场景里 `SendEventToAgent` 会**整体早退**（IsInteractionDisabled）⇒ 只靠事件的话，
			//    "被捆"这件事在战斗里对脑完全不可见（`bind state` 也会显示 bound=False，误导排查）。
			//    直接置 = 任何场景下脑都看得见这件事（战斗里脑不 Tick，但标记与状态读数是对的）。
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

			PlayLoop(_target, CycleAction, "cycle");
			DebugLogger.Log($"[Grapple] 勾人：捆缚成立（{_target.Name}）—— 躺地循环 {CycleAction}，"
				+ "AI 接管 = 脑标记 IsBound + BoundAction 占位（不接命令 / 不参战 / 不围观；解绑后立刻恢复），"
				+ (BoundSeconds > 0f ? $"{BoundSeconds:F0} 秒后自动挣脱（调试档）" : "**永不自动挣脱**")
				+ " —— 解开：敲 bind release，或走到身边用交互面板的【松绳】（待做，见 §13.14 TODO 4）");
		}

		private BindEvent TickBound(float dt)
		{
			_boundTimer += dt;
			_hookPos = ChestOf(_target);
			_hookDir = AwayFromPlayerDir();

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
		/// 复核"压制还在不在"（**所有相位都跑**，见 <see cref="Tick"/>）：
		/// ① 脚本旗标（`DoNotRun | NoAttack`）② **原版 AI 冻结**（<see cref="SetTargetAIPaused"/>）
		/// ③ **只在"缚"那拍**：躺地循环还挂在通道 0 上吗。
		/// 被抢 = 抢回来并计数（`bind state` 打出来；正常路径恒为 0 条日志）。
		/// ⚠️ 观感验收模式（`bind anim lay|cycle|standup` 锁了某一段）**不抢姿势** —— 那是用户正在看的东西。
		/// </summary>
		private void VerifySuppression()
		{
			if (_target == null) return;

			// ① 旗标：幂等重压（被别的系统清掉 = 躺着的人立刻恢复原生 AI）。
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
			SetTargetAIPaused(_target, true);

			// ③ 姿势：只在"缚"那拍抢；锁了观感段就不动
			if (_phase != Phase.Bound || _forcedAnim != null) return;
			try
			{
				if (AgentControlHelper.IsPlayingPose(_target, CycleAction)) return;
				_poseReasserts++;
				PlayLoop(_target, CycleAction, "cycle");
				// 日志限频：头 3 次 + 每 20 次一条（否则被抢成常态时会刷屏）
				if (_poseReasserts <= 3 || _poseReasserts % 20 == 0)
				{
					DebugLogger.Log($"[Grapple] 勾人：躺地循环被抢走（通道 0 = {AgentControlHelper.GetPose(_target)}）"
						+ $" ⇒ 抢回 {CycleAction}（第 {_poseReasserts} 次）");
				}
			}
			catch (Exception) { }
		}

		/// <summary>命令入口：立刻放开当前目标（`custom.grapple bind release`）。</summary>
		public bool ReleaseNow()
		{
			if (!IsActive) return false;
			Release("command");
			return true;
		}

		/// <summary>
		/// **放人**：起身动画 + 排队"等动画播完再还 AI"。
		/// 注意**不是立刻**清旗标 —— 起身动画播到一半被原生 AI 抢走通道就白演了
		/// （先例：`KnockoutFlow.StandUp` 那条"等起身演完"纪律）。
		/// </summary>
		public void Release(string reason)
		{
			Agent t = _target;
			_phase = Phase.None;
			_forcedAnim = null;
			_attacker = null;
			_target = null;
			_note = "released (" + reason + ")";

			BeginStandup(t, reason);
			DebugLogger.Log($"[Grapple] 勾人：放人（{reason}）—— 起身 {StandupSeconds:F1}s 后把 AI 还回去");
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

		private void BeginStandup(Agent t, string why)
		{
			if (t == null) return;
			// 🔴 **入队必须无条件发生**（2026-10-09 TODO 2 加固）：`RestoreAgent` 是"把 AI 还回去 +
			//    清掉 brain.IsBound"的**唯一**出口 —— 上面任何一步抛异常而漏掉入队，被捆标记就永久留着
			//    （那个 agent 从此不接任何事件、拒清队列 = 僵尸）。所以播动画放 try 里，入队放 try 外。
			float wait = 0.05f;
			bool wentDown = _wentDown;
			try
			{
				if (!AgentControlHelper.SafeIsActive(t))
				{
					// agent 已经没了（死了/离场）：没什么可还的 —— 但**标记得清掉**，
					// 否则那个脑（若还挂着）从此不接任何事件（本方法入队被跳过 = 永远没人来清）。
					try
					{
						var dead = AgentAIController.GetBrainForAgent(t);
						if (dead != null) dead.IsBound = false;
					}
					catch (Exception) { }
					return;
				}

				if (wentDown)
				{
					PlayOnce(t, StandupAction, 0f, 0.2f, "standup");
					wait = StandupSeconds;
				}
				else
				{
					// 🔴 **没到"躺下"那一步就别播起身动画**（2026-10-09 实机事故，症状 = "倒下又立刻起来"）：
					//    起身动画的**头几帧就是躺姿**（源帧 111→1，从躺到站）⇒ 给一个还站着的人播它，
					//    效果是**先趴下去再站起来**。实测：`bind test` 被出口踢掉那两次，每分钟都演这一下。
					//    ⇒ 没躺下 = 直接还 AI（下一帧就走 TickPending 的恢复路）。
					DebugLogger.Log($"[Grapple] 勾人：没到躺下那步（{why}）⇒ 不播起身动画，直接还 AI");
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 勾人：起身动画播放异常（{ex.GetType().Name}）—— 仍照常排队还 AI");
			}
			_pending.Add(new Pending { Agent = t, Timer = wait, Why = why + (wentDown ? "" : " (never went down)") });
		}

		/// <summary>每帧推进"起身队列"（<see cref="GrappleLogic.OnMissionTick"/> **无条件**每帧调一次）。</summary>
		public void TickPending(float dt)
		{
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

			DebugLogger.Log($"[Grapple] 勾人：AI 已还给 {SafeName(a)}（被捆标记已清、占位已拆、旗标已解锁、原版 AI 已解冻）");
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

		// ───────────────────────────── 动画 / 记账 ─────────────────────────────

		private void PlayOnce(Agent a, string action, float startProgress, float blendIn, string tag)
		{
			if (a == null) return;
			string forced = _forcedAnim;
			if (forced != null && forced != tag)
			{
				// 观感验收模式：锁在某一段 ⇒ 不再被流程改动（`bind anim auto` 解除）
				return;
			}
			WarnIfUnresolved(action);
			AgentControlHelper.ForcePlayAction(a, action, startProgress: startProgress, blendIn: blendIn,
				blendOutPeriodToNoAnim: 0.4f);
		}

		private void PlayLoop(Agent a, string action, string tag)
		{
			if (a == null) return;
			string forced = _forcedAnim;
			if (forced != null && forced != tag) return;
			WarnIfUnresolved(action);
			// 🔴 循环件：blendOutPeriodToNoAnim 必须显式 0（默认 0.4 会把躺地循环淡出）
			AgentControlHelper.ForcePlayAction(a, action, startProgress: 0f, blendIn: 0.25f,
				blendOutPeriodToNoAnim: 0f);
		}

		/// <summary>
		/// 动作名解析不到 ⇒ 打一条**显眼的**日志（只打一次/名字）。
		/// 这是第 0 步（跨模块引原版 clip 名）失败时的唯一症状 —— 引擎自己**静默** `act_none`、不报错，
		/// 不主动报的话表现只是"人站着不倒"，会被当成别的 bug 排查半天。
		/// </summary>
		private static readonly HashSet<string> s_warnedActions = new HashSet<string>();
		private static void WarnIfUnresolved(string action)
		{
			try
			{
				if (ActionIndexCache.Create(action) != ActionIndexCache.act_none) return;
				if (!s_warnedActions.Add(action)) return;
				DebugLogger.Log($"[Grapple] 勾人：🔴 动作名解析不到 —— '{action}' 是 act_none！"
					+ " 检查内容包 action_types.xml（声明）+ action_sets.xml（映射到 anim_dungeon_prisoner_lay* 三条原版 clip）。"
					+ " 症状 = 目标不会躺下/起身（引擎静默跳过，无报错）。");
			}
			catch (Exception) { }
		}

		/// <summary>观感验收：强制目标播某一段（lay / cycle / standup）；<paramref name="which"/> = null/auto = 交回流程。</summary>
		public string ForceAnim(string which)
		{
			if (!IsActive || _target == null) return "Error: no bound target.";
			if (string.IsNullOrEmpty(which) || which == "auto")
			{
				_forcedAnim = null;
				// 交回流程：按当前相位重播该播的那条
				if (_phase == Phase.Bound) PlayLoop(_target, CycleAction, "cycle");
				else if (_phase == Phase.Yanking) PlayOnce(_target, LayAction, LayStartProgress, 0.2f, "lay");
				return "OK: bind anim = auto (流程接管)";
			}
			switch (which)
			{
				case "lay":
					_forcedAnim = "lay";
					PlayOnce(_target, LayAction, LayStartProgress, 0.2f, "lay");
					return "OK: forced lay";
				case "cycle":
					_forcedAnim = "cycle";
					PlayLoop(_target, CycleAction, "cycle");
					return "OK: forced cycle";
				case "standup":
					_forcedAnim = "standup";
					PlayOnce(_target, StandupAction, 0f, 0.2f, "standup");
					return "OK: forced standup";
			}
			return "Error: bind anim expects lay|cycle|standup|auto";
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
				string pend = _pending.Count > 0 ? $" | 起身队列={_pending.Count}" : "";
				return $"bind: idle（总开关 {(Enabled ? "on" : "off")}）{pend} | 最近一次：{_note}";
			}
			string act = "-";
			try { act = V.ActName(_target, 0); } catch (Exception) { }
			string tgt = SafeName(_target);
			float timer = _phase == Phase.Bound ? _boundTimer : _t;
			float limit = _phase == Phase.Bound ? BoundSeconds
				: (_phase == Phase.Wrapping ? WrapSeconds
				: (Yank == YankMode.Blow ? BlowYankSeconds : YankTotalSeconds));
			// 🔴 接管状态（TODO 2 的验收判据，2026-10-09）：脑标记 = 闸门基准（false 时下面几道全不生效）；
			//    事件忽略 = 被挡下的定向事件条数（该涨就涨 = 调度真的被挡住了）；姿势抢回 = 正常恒为 0。
			//    ⚠️ 控制台返回文本一律英文（CLAUDE.md 控制台纪律）。
			string takeover = "-";
			try
			{
				var b = AgentAIController.GetBrainForAgent(_target);
				// aiPaused 读的是**引擎自己**那份状态（`Agent.IsPaused` ← `AIStateFlag.Paused`），
				// 不是我们的旋钮 —— 这样"旋钮开了但冻结没生效"也能一眼看出来。
				string paused = "?";
				try { paused = _target.IsPaused ? "True" : "False"; } catch (Exception) { }
				if (b != null)
					takeover = $"bound={b.IsBound} ignored={b.BoundEventsDropped} poseReassert={_poseReasserts}";
				else
					takeover = $"bound=(no brain) poseReassert={_poseReasserts}";
				takeover += $" aiPaused={paused}";
			}
			catch (Exception) { }
			return $"bind: {_phase} | 目标={tgt} | {timer:F2}/{limit:F2}s | 动作={act}"
				+ $" | 钩={Fmt(_hookPos)} | 拖距={DragDistance:F1}m 超时={BoundSeconds:F0}s"
				+ $" | takeover[{takeover}]"
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
