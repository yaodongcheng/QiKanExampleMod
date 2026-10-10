using System;
using LivingWorldNpcs.Flight;      // CarrierBoard / FlightTuning（与飞行工程共用同一套载具与常量）
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **拉自己**（步骤 4；方案 = plans\钩索-实施计划.md §3.5）—— 勾住之后把玩家拽过去，落点 = 解算出的平台。
	///
	/// 两件事分开看：
	///   · **怎么动** = 曲线驱动（照 UE 参考工程）：`位置 = lerp(起点, 终点, 速度曲线(u)) + (0,0,弧线(u))`，
	///     速度曲线是三次 Hermite（起点斜率 2.2、终点 0.29 = 先加速后收），弧线 `sin(πu) × 高度`（**结束归零**）。
	///   · **谁来动** = **脚下木板**（骑砍写不进 agent 的 Z，见 Knowledge/骑砍2Agent运动与位置机制.md §6）。
	///     逐帧 `MoveTo` 瞬移板，人站在板上被一起带走 —— 与飞行工程同一套 `CarrierBoard`。
	///
	/// 🔴 三条从飞行工程继承的硬纪律（都是实机撞出来的，别改）：
	///   ① **冻结 = `V.SetPlayerControlFrozen(main, true)` + `main.SetIsAIPaused(true)`**
	///      （只切 AI 不暂停 = AI 跟板较劲、人自己走回来）；
	///   ② **拆板的时机 = 板面已经贴到地面之后**——「悬空变被托住」那一瞬引擎会按地形重算位置
	///      （实测瞬移 63 米），所以**没平台那种"拉完自由落体"也不能中途拆板**：板载着人一起往下掉，
	///      落地了才拆；
	///   ③ **脱离载具要收摊**（撞墙时板穿墙、人被挡住 ⇒ 人掉下板）：判据 = 碰撞体底面与板面的三维距离。
	///
	/// 本类**自己管冻结与板**，`GrappleLogic` 只负责"什么时候开始 / 结束时收钩"。
	/// </summary>
	internal sealed class GrapplePull
	{
		public enum Phase
		{
			Idle,
			WaitingOnBoard,   // 板已放到脚下，等人站上去（或超时）
			Pulling,          // 曲线驱动中
			Settling,         // 到位后的停顿（拆板前的安全窗）
			Falling,          // 没平台：板载着人一起下落（支撑不断开）
			Done,
		}

		/// <summary>Tick 的返回值：Finished / Aborted 只在发生的那一帧各返回一次。</summary>
		public enum PullEvent
		{
			None,
			Finished,
			Aborted,
		}

		// ───────────────────────────── 可调参数（命令改） ─────────────────────────────

		/// <summary>弧线最高抬多少米（0 = 直线）。照 UE 的高度曲线取的形（中途抬升、结束归零）。</summary>
		public static float ArcHeight = 1.5f;

		/// <summary>拉升时长（秒）——地面起钩 / 空中起钩两个基准（UE 参考工程 = 1.2 / 0.95）。</summary>
		/// <remarks>
		/// 🔴 2026-10-04 **地面档改成 1.17**（= 动画时间轴的位移窗口 1.13→2.30 = 源 GrappleEnd）：
		///    位移时长与"过程"段对齐后，"切落地点 = 位移 ~63%"（谓词里的剩 10%）才与物理动作严丝合缝。
		///    空中档还没做时间轴（等空中起钩那版），先留旧值。
		/// </remarks>
		public static float DurationGround = 1.17f;
		public static float DurationAir = 0.95f;

		/// <summary>时长按距离缩放的参照距离（米）：UE 那次是 12 米射程；我们射程 20 米，按比例放大但钳住。</summary>
		public static float RefDistance = 12f;
		/// <summary>
		/// 距离缩放区间（2026-10-04 起默认 **1.0~1.0 = 关掉缩放**）：动画时间轴是固定的，
		/// 位移时长必须钉死才对齐（用户 2026-10-04 裁定："怎么动"走曲线、"动多久"由动画节点说了算）。
		/// 想找回"远拉快、近拉慢"的手感 ⇒ 把这两个值改回 0.6 / 1.8（代价：节点只在 12 米附近对得上）。
		/// </summary>
		public static float DurationScaleMin = 1.0f;
		public static float DurationScaleMax = 1.0f;

		/// <summary>&gt;0 = 固定时长（命令 `pulltime`）；0 = 按上面那套自动算。</summary>
		public static float DurationOverride = 0f;

		/// <summary>到位后的停顿（秒）——留一点时间让人"落稳"，然后才拆板。</summary>
		public static float SettleSeconds = 0.25f;

		/// <summary>
		/// 动画收摊的等待门（2026-10-04）：**返回 false = 落地动作还没演完**，拆板/解冻再等等。
		/// 由 <see cref="GrappleLogic"/> 挂上（`AnimExited`）；没挂 = 不等（回旧行为）。
		/// </summary>
		public Func<bool> WaitAnimExit;

		/// <summary>等动画的上限（秒）——超了就强制拆板（别为了等动画把人锁住）。</summary>
		public static float MaxAnimWaitSeconds = 1.2f;

		/// <summary>等玩家站上板的超时（秒）。</summary>
		public static float OnBoardTimeout = 0.5f;

		/// <summary>自由落体（板载坠落）：加速度与限速（m/s²、m/s）。</summary>
		public static float FallGravity = 20f;
		public static float FallMaxSpeed = 45f;

		/// <summary>脱离载具的判据（米，同飞行工程 `FallOffDistance` 的口径）。</summary>
		public static float FallOffLimit = 1.2f;

		/// <summary>本相机在 Camera.csv 里的行名（🔴 2026-10-05 阶段 1 起，拉拽机位参数也住在表里）。</summary>
		public const string CaseName = "grapple_pull";

		/// <summary>取表行（缺行 = false ⇒ 本次拉拽**不接管相机**并打日志；拉拽本身照常跑）。</summary>
		public static bool TryGetCase(out CameraCase kase) => CameraCase.TryGet(CaseName, out kase);

		/// <summary>
		/// 拉拽期间的机位（2026-10-03 加；同日第二版改成"引擎机位 + 拉远"）：
		/// **方向 = 接管那一刻的引擎相机机位（世界锚定、不硬切、不跟角色转）**，只把臂长拉远 ——
		/// 上一版用模板（方向相对角色、且接管瞬间硬切到侧后 30°）实机症状：**镜头猛转**。
		/// 世界锚定 = 相机保持发射时的视角、只是跟着人平移 ⇒ 不会甩、也不会跟丢。
		/// 臂长（米）：表列 `ArmLength`（默认 8，能看清全身与弧线）；**≤0 = 不接管相机**（回引擎相机，对照用）。
		/// 🔴 2026-10-05 阶段 1：数值搬到 `Camera.csv` 的 `grapple_pull` 行；本属性读写的就是那一行（内存态）。
		/// </summary>
		public static float CameraArmLength
		{
			get => CameraCase.TryGet(CaseName, out CameraCase c) ? c.Param.ArmLength : 0f;
			set { if (CameraCase.TryGet(CaseName, out CameraCase c)) { c.Param.ArmLength = value; } }
		}

		// 🪦 2026-10-05 阶段 3：`CameraTemplate`（`custom.grapple cam t:<模板>` 的调试口子）**已退役** ——
		//    想用别的机位看拉拽：`custom.cam play <case> [秒]`（任何 Camera.csv 行都能起播）。

		/// <summary>接管时比拉拽本身多留的时间（秒）：覆盖登板等待 + 停稳 + 归还渐变。</summary>
		public static float CameraExtraSeconds = 1.5f;

		/// <summary>
		/// 相机归还的**起点**（拉拽进度 u，0~1）：u 到这个比例，镜头就**开始往回滑**
		/// （臂长从 8 米收回引擎视距），而不是等落地那一帧才动。
		/// 🔴 2026-10-03 用户要求："快到终点的时候就开始过渡，不然时间太短了看不出来，好像是硬切"
		///    —— 旧行为 = 到位那一帧才开始（= 1.0）、只滑 0.35s ⇒ 8m→引擎视距那一下看着像硬切。
		///
		/// 🔴 **这里刻意和飞行工程不一样**（用户 2026-10-03 明确裁定，别照抄飞行那套）：
		///    飞行 = 玩家自己高速撞地收场，落点是玩家冲出来的、收势只能留在落地那一下；
		///    钩索 = **脚本算好的定点落台**（§3.5 的曲线 + 末段本来就减速），终点在拉拽开始前就已知 ⇒
		///    镜头**还没落地就该开始收**：落地时已经滑了大半，剩下的一小截在"停稳 0.25 秒"里滑完，
		///    玩家恢复控制的那一帧镜头是连续的（全程一段运镜，没有"最后抖一下"）。
		/// 所以默认值给"过半就开始"，而不是"到位才开始"。
		/// 1 = 回到旧时机（到位才开始；只做对照实验用）。
		/// </summary>
		public static float CameraReturnStart = 0.5f;

		/// <summary>相机归还的**滑行时长**（秒）：从我们的机位滑回引擎视距/FOV 用多久。
		/// 拉长到 1 秒出头才看得出是渐变（旧值 = 相机系统的默认 0.35s）。</summary>
		public static float CameraReturnGlideSeconds = 1.2f;

		// 🪦 **2026-10-10 退役：「方向归还」（`LookReturnStart` / `LookReturnSeconds` / `StartLookReturn`）
		//    连同 `lookret` 命令一起废了。** 它当年解决的是"撒手瞬间镜头跳一下"（引擎解冻会把
		//    CameraBearing 重置成角色移动方向、俯仰清零），代价却是**把玩家自己转的镜头扳回去**。
		//    现在改成"撒手那一刻把玩家真实朝向写回引擎"（`CameraReturnPolicy.WriteBackLookOnRelease`）
		//    —— 既没有跳变，也不动玩家的镜头。旋钮没了：想改方向就自己转鼠标。


		/// <summary>拉拽期间把身体**转向钩点**（保住发射时的朝向；不写的话朝向会飘）。</summary>
		public static bool FaceHook = true;

		/// <summary>转身速率（度/秒）—— 只用于纠正小偏差，不需要快。</summary>
		public static float TurnRateDegPerSec = 240f;

		// ───────────────────────────── 状态 ─────────────────────────────

		private readonly CarrierBoard _board = new CarrierBoard();
		private Agent _main;
		private Phase _phase = Phase.Idle;
		private Vec3 _startTop;        // 起点：**板面**（= 脚底）世界坐标
		private Vec3 _endTop;          // 终点：板面世界坐标（平台面；没平台时是空中那个点）
		private bool _landingFound;    // 终点是不是落在平台上
		private float _t;              // 已拉时长
		private float _duration;
		private float _phaseTimer;
		private float _fallSpeed;
		private bool _frozen;
		private bool _cameraHeld;      // 本次拉拽有没有接管相机（收摊时只还我们接的）
		private bool _cameraReturning; // 相机是否已"提前归还"（防重复触发/重复日志；归还滑完前 _cameraHeld 保持 true = 中途 Abort 还能硬还）
		private Vec3 _faceTarget;      // 拉拽期间身体朝向的目标（= 钩点，保住"发射时的朝向"）
		private float _bodyYawDeg = float.NaN;
		private float _logTimer;       // 拉拽诊断日志节流（每 0.25s 一行）
		private Vec3 _windPrevPos;     // 风噪用：上一帧玩家位置（位移速度 = 拉拽的真实速度）
		private bool _windHasPrev;
		private float _windSpeedNow;   // 本帧喂给风噪的速度（诊断日志用）

		public Phase CurrentPhase => _phase;
		public bool IsActive => _phase == Phase.WaitingOnBoard || _phase == Phase.Pulling
			|| _phase == Phase.Settling || _phase == Phase.Falling;

		// ───────────────────────────── 曲线 ─────────────────────────────

		/// <summary>
		/// 速度曲线（三次 Hermite，u∈[0,1] → 0..1）：起点斜率 **2.204**、终点斜率 **0.286** ——
		/// 这两个数是从 UE 参考工程的 `Curve_GrappleSpeedAir` 关键帧切线**实测抄下来的**（不是调的）。
		/// 形状 = 起步就快、中段平推、**末段自己收**（收势是硬需求：见类注释 ②）。
		/// </summary>
		public static float SpeedCurve(float u)
		{
			u = u < 0f ? 0f : (u > 1f ? 1f : u);
			const float m0 = 2.204f;
			const float m1 = 0.286f;
			float u2 = u * u;
			float u3 = u2 * u;
			return m0 * (u3 - 2f * u2 + u) + (-2f * u3 + 3f * u2) + m1 * (u3 - u2);
		}

		/// <summary>弧线（米）：`sin(πu) × ArcHeight`，**u=1 时归零**（我们与 UE 的唯一刻意差异，见 §3.5）。</summary>
		public static float ArcCurve(float u)
		{
			u = u < 0f ? 0f : (u > 1f ? 1f : u);
			return ArcHeight * MathF.Sin(MathF.PI * u);
		}

		// ───────────────────────────── 开始 / 每帧 / 收摊 ─────────────────────────────

		/// <summary>
		/// 开始拉拽。<paramref name="landingFound"/> = 终点是不是解算出的平台（false ⇒ 拉完自由落体）。
		/// 返回一句英文回执（控制台纪律）；失败 = 什么都没动（不会留下冻结/板）。
		/// </summary>
		public string Start(Agent main, Vec3 endTop, bool landingFound, Scene scene, bool fromAir, Vec3 faceTarget)
		{
			if (IsActive)
			{
				return "Error: pull already in progress.";
			}
			if (main == null || scene == null)
			{
				return "Error: no player/scene.";
			}
			if (main.HasMount)
			{
				// 骑马时控制权被切走会乱坐骑状态机（飞行工程的实测结论）——第一版直接不可用
				return "Error: not usable while mounted.";
			}

			_main = main;
			_landingFound = landingFound;
			_endTop = endTop;
			_faceTarget = faceTarget;
			_bodyYawDeg = float.NaN;       // 首次由"当前朝向"播种（见 FaceToward）
			_logTimer = 0f;
			_startTop = new Vec3(main.Position.x, main.Position.y, CarrierBottomZ(main));

			float dist = (_endTop - _startTop).Length;
			_duration = DurationOverride > 0f
				? DurationOverride
				: (fromAir ? DurationAir : DurationGround)
					* Clamp(dist / MathF.Max(1f, RefDistance), DurationScaleMin, DurationScaleMax);
			_t = 0f;
			_phaseTimer = 0f;
			_fallSpeed = 0f;

			// ① 冻结（照抄飞行工程：两条一起上，缺一不可）
			try
			{
				V.SetPlayerControlFrozen(main, true);
				main.SetIsAIPaused(true);
				_frozen = true;
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 拉拽冻结失败：{ex.GetType().Name} {ex.Message}");
				_frozen = false;
				_main = null;
				return "Error: freeze failed.";
			}

			// ② 板放到脚下（板面 = 碰撞体底面往下让一点，人自然落上去）
			float boardTopZ = _startTop.z - FlightTuning.CarrierSpawnGap;
			if (!_board.Spawn(scene, new Vec3(_startTop.x, _startTop.y, boardTopZ)))
			{
				ExitFreeze();
				_main = null;
				return "Error: carrier board spawn failed (mesh missing?)";
			}

			_phase = Phase.WaitingOnBoard;
			EnterCamera();
			DebugLogger.Log($"[Grapple] 拉拽开始：起点板面={Fmt(_startTop)} 终点板面={Fmt(_endTop)} 距离={dist:F1}m "
				+ $"时长={_duration:F2}s 弧线={ArcHeight:F1}m 平台={(_landingFound ? "有" : "无(拉完自由落体)")} 空中起钩={fromAir}");

			return string.Format("OK: pulling | dist={0:F1}m dur={1:F2}s arc={2:F1}m landing={3}",
				dist, _duration, ArcHeight, _landingFound ? "platform" : "free-fall");
		}

		/// <summary>每帧推进。返回 Finished / Aborted 的那一帧之后 <see cref="IsActive"/> 变 false。</summary>
		public PullEvent Tick(float dt)
		{
			if (!IsActive)
			{
				return PullEvent.None;
			}
			if (dt <= 0f)
			{
				return PullEvent.None;
			}
			if (_main == null || !AgentControlHelper.SafeIsActive(_main) || !_board.IsSpawned)
			{
				Abort("player or board gone");
				return PullEvent.Aborted;
			}

			// 🌬️ 风噪（2026-10-10 用户要求：被绳拽着飞也要有风）——用**玩家实际位移速度**喂调度器。
			//    飞行那边每帧也在喂，但它拉拽期间喂的是 0（它的相位是 Grounded），而"喂 0"只解除待发标记、
			//    不推进计时器 ⇒ 两边不打架、不会双倍出风（机制见 FlightWindFx.Tick 的注释）。
			FeedWind(dt);

			try
			{
				switch (_phase)
				{
					case Phase.WaitingOnBoard:
						return TickWaitingOnBoard(dt);
					case Phase.Pulling:
						return TickPulling(dt);
					case Phase.Settling:
						return TickSettling(dt);
					case Phase.Falling:
						return TickFalling(dt);
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 拉拽 tick 异常，收摊：{ex.GetType().Name} {ex.Message}");
				Abort("tick exception: " + ex.Message);
				return PullEvent.Aborted;
			}
			return PullEvent.None;
		}

		/// <summary>
		/// 把"玩家这一帧的实际位移速度"喂给风噪调度器（`Flight/FlightWindFx.cs`）。
		/// 为什么用位移而不是对速度曲线求导：板载着人走 ⇒ 玩家位置差就是真速度，一段除法就够；
		/// 等待/收势/坠落几个相位也顺带被喂到（速度≈0 时调度器自己不出声）。
		/// </summary>
		private void FeedWind(float dt)
		{
			if (_main == null)
			{
				_windHasPrev = false;
				return;
			}
			Vec3 pos = _main.Position;
			float speed = 0f;
			if (_windHasPrev && dt > 0f)
			{
				speed = (pos - _windPrevPos).Length / dt;
			}
			_windPrevPos = pos;
			_windHasPrev = true;
			_windSpeedNow = speed;
			FlightWindFx.Tick(speed, dt);
		}

		private PullEvent TickWaitingOnBoard(float dt)
		{
			_phaseTimer += dt;
			// 人站上板（或超时兜底）才开始拉 —— 照飞行工程的登板闸
			bool onboard = _main.IsOnLand();
			if (!onboard && _phaseTimer < OnBoardTimeout)
			{
				return PullEvent.None;
			}

			// 板面此刻在哪就按哪算起点（人可能已经站上去，起点以**板面**为准，避免起手一帧的拉扯）
			_startTop = new Vec3(_startTop.x, _startTop.y,
				_board.Origin.z + FlightTuning.CarrierTopLocalZ);
			_phase = Phase.Pulling;
			_t = 0f;

			// 🌬️ **被拽走的那一刻强制起一阵风**（2026-10-10 实机：用户"被钩锁拉起来时没听到风声"）。
			//    拉拽全程只有 ~1 秒、速度曲线末段还掉到门槛以下 ⇒ 光靠"按速度密度"可能一阵都赶不上。
			//    Kick 不受门槛管；之后仍由 FeedWind 按真实速度接节奏。
			if (FlightWindFx.Kick())
			{
				DebugLogger.Log("[Grapple] 拉拽起风（Kick，起拽瞬间强制一阵）");
			}
			else
			{
				// 留痕：开关关着 / 事件名空 / 不在场景 —— 否则"没风"分不清是没触发还是被关掉
				DebugLogger.Log("[Grapple] 拉拽未起风（Kick 被拒：风噪开关关着或不在场景）");
			}

			DebugLogger.Log($"[Grapple] 拉拽：登板完成（等待={_phaseTimer:F2}s 站住={onboard}）");
			return PullEvent.None;
		}

		private PullEvent TickPulling(float dt)
		{
			_t += dt;
			float u = _t / _duration;
			Vec3 top = Vec3.Lerp(_startTop, _endTop, SpeedCurve(u));
			top.z += ArcCurve(u);
			MoveBoardTopTo(top);

			// 🔴 **起飞头几帧：把"搭便车"的人请下板**（2026-10-10 用户报告：
			//    "钩锁拉人起飞的时候，如果身边有其他 agent，会把其他人也带飞"）。
			//    只在这个高度之下做（`FlightTuning.CarrierEvictMaxLift`）—— 板还低的时候把人挪到板外，
			//    他最多掉一两米；飞高了再挪 = 从空中丢人。判据与挪法在 `CarrierBoard.EvictRiders`。
			if (FlightTuning.CarrierEvictOthers)
			{
				float lift = top.z - GroundZ(_board.Origin);
				if (lift <= FlightTuning.CarrierEvictMaxLift)
				{
					int evicted = _board.EvictRiders(_main, FlightTuning.CarrierEvictMargin);
					if (evicted > 0)
					{
						DebugLogger.Log($"[Grapple] 载具搭便车：请下板 {evicted} 人（板面离地 {lift:F2}m）");
					}
				}
			}

			// 🔴 **每帧把身体按住**（2026-10-03 用户实测"落地后脸不对/镜头猛转"）：
			//    拉拽期间我们一行朝向代码都没写 ⇒ 朝向由引擎/惯性决定，不可控（还会带着相机一起甩）。
			//    这里明确写：身体朝**钩点**（≈ 发射方向）平滑转过去，帧帧覆盖 ⇒ 朝向可预期。
			FaceToward(dt);

			if (FellOffBoard())
			{
				Abort("fell off board");
				return PullEvent.Aborted;
			}

			// 诊断：每 0.25s 一行（位置 + 身体实际朝向）——"镜头/身体怎么动的"全靠它
			_logTimer += dt;
			if (_logTimer >= 0.25f)
			{
				_logTimer = 0f;
				Vec3 travel = _endTop - _startTop; travel.z = 0f;
				float travelYaw = MathF.Atan2(travel.y, travel.x) * (180f / MathF.PI);
				DebugLogger.Log($"[Grapple] 拉拽中 u={u:F2} 玩家={Fmt(_main.Position)} 板面={Fmt(top)} "
					+ $"速度={_windSpeedNow:F1}m/s（风噪门槛 {FlightTuning.WindSpeedMin:F0}）"
					+ $" 身体yaw={LookYawDeg(_main):F0}° 行进yaw={travelYaw:F0}°（常量目标）");
			}

			// 🔴 **相机提前归还**（2026-10-03 用户要求"快到终点的时候就开始过渡"）：
			//    旧行为 = 到位那一帧才开始滑、只滑 0.35s ⇒ 太快，看着像硬切。
			//    现在 u 到 CameraReturnStart 镜头就开始往回滑、滑 CameraReturnGlideSeconds 秒 ——
			//    落地/拆板时早已在滑路上，玩家恢复控制的那一帧镜头是连续的。
			if (u >= CameraReturnStart)
			{
				StartCameraReturn();
			}

			if (u >= 1f)
			{
				if (_landingFound)
				{
					_phase = Phase.Settling;
					_phaseTimer = 0f;
					// 兜底：`camret` 设成 1（= 旧时机）时这里才开始收；默认早已在归还中，重复请求是空操作。
					StartCameraReturn();
					DebugLogger.Log($"[Grapple] 拉拽到位（{_duration:F2}s）→ 停稳 {SettleSeconds:F2}s 后拆板 | {_board.Describe()}");
				}
				else
				{
					_phase = Phase.Falling;
					_fallSpeed = 0f;
					DebugLogger.Log($"[Grapple] 拉拽到位（{_duration:F2}s）**没有平台** → 板载着人自由落体 | {_board.Describe()}");
				}
			}
			return PullEvent.None;
		}

		private PullEvent TickSettling(float dt)
		{
			_phaseTimer += dt;
			if (_phaseTimer < SettleSeconds)
			{
				return PullEvent.None;
			}
			// 🔴 等"落地"动作演完再拆板（2026-10-04）：状态机那台在 落地段 剩 15% 才出机；
			//    解冻（交还控制权）跟着它走 = 落地收势完整播完。上限兜底，别把人锁住太久。
			if (WaitAnimExit != null && !WaitAnimExit() && _phaseTimer < SettleSeconds + MaxAnimWaitSeconds)
			{
				return PullEvent.None;
			}
			// 板面已经贴在地面/平台上 ⇒ 拆板、交还控制（照飞行工程的落地：先拆板再交还）
			DebugLogger.Log($"[Grapple] 拉拽完成：拆板交还控制 | {_board.Describe()}");
			Finish();
			return PullEvent.Finished;
		}

		private PullEvent TickFalling(float dt)
		{
			_fallSpeed = MathF.Min(FallMaxSpeed, _fallSpeed + FallGravity * dt);
			float top = _board.Origin.z + FlightTuning.CarrierTopLocalZ;
			float newTop = top - _fallSpeed * dt;

			// 地面高度用 §22.4 的口径（**不是** GetTerrainHeight —— 那是高度图，城镇里在铺装之下）
			float groundZ = GroundZ(_board.Origin);
			if (newTop <= groundZ + 0.03f)
			{
				DebugLogger.Log($"[Grapple] 板载坠落着地（板面={newTop:F2} 地面={groundZ:F2} 落速={_fallSpeed:F1}m/s）→ 拆板交还控制");
				MoveBoardTopTo(new Vec3(_board.Origin.x, _board.Origin.y, groundZ));
				Finish();
				return PullEvent.Finished;
			}

			MoveBoardTopTo(new Vec3(_board.Origin.x, _board.Origin.y, newTop));
			return PullEvent.None;
		}

		/// <summary>外部中止（受击 / 命令 / 换场景）：拆板 + 解冻，绝不留冻结状态。</summary>
		public void Abort(string reason)
		{
			if (_phase == Phase.Idle)
			{
				return;
			}
			DebugLogger.Log($"[Grapple] 拉拽中止（{reason}）| {_board.Describe()}");
			ExitCamera(immediate: true);       // 出错路径：立刻还相机，别留接管态
			Cleanup();
		}

		private void Finish()
		{
			ExitCamera(immediate: false);      // 正常结束：渐变滑回引擎相机（硬切会"跳"一下）
			Cleanup();
		}

		private void Cleanup()
		{
			try
			{
				_board.Remove();
			}
			catch (Exception)
			{
			}
			ExitFreeze();
			_phase = Phase.Idle;
			_main = null;
			_fallSpeed = 0f;
		}

		private void ExitFreeze()
		{
			if (!_frozen)
			{
				return;
			}
			_frozen = false;
			Agent main = _main ?? Agent.Main;
			if (main == null || !AgentControlHelper.SafeIsActive(main))
			{
				DebugLogger.Log("[Grapple] 解冻：玩家 agent 已失效，无需归还");
				return;
			}
			try
			{
				main.SetIsAIPaused(false);
				V.SetPlayerControlFrozen(main, false);
				DebugLogger.Log($"[Grapple] 解冻：Controller={main.Controller} paused={main.IsPaused}");
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 🔴 解冻失败，玩家可能失去控制权（重启场景可恢复）: {ex.Message}");
			}
		}

		// ───────────────────────────── 朝向 ─────────────────────────────

		/// <summary>
		/// 把身体平滑转向**行进方向**（起点 → 终点，一个**常量**；与飞行工程"朝实际航迹"同源）。
		///
		/// 🔴🔴 **别改成"朝钩点"**（2026-10-03 实机定死，日志证据）：
		///    钩点是**玩家收敛的目标** ⇒ `钩点 − 玩家` 越飞越短、方向在末段**乱转**
		///    （实测最后 0.25 秒里目标角从 −107° 甩到 −46°，身体跟着甩 ≈ 90° ——
		///     用户症状"落地瞬间角色向右转 90°"，且 `cam off` 关掉相机后照样出现）。
		///    行进方向是常量、余量永远够，落地朝向 ≈ 发射方向（落点就在钩点旁边）。
		///
		/// 写入走 <c>Agent.SetMovementDirection</c>（只给方向、不给速度；范本 = 飞行工程 `TurnBody`）——
		/// 玩家此时被冻结（Controller=AI + AI 暂停），写进去只影响朝向，不会让人真的走。
		/// 首次用**当前实际朝向**播种，避免起手一帧忽然掰头。
		/// </summary>
		private void FaceToward(float dt)
		{
			if (!FaceHook || _main == null)
			{
				return;
			}
			Vec3 d = _endTop - _startTop;          // 行进方向（常量）
			d.z = 0f;
			if (d.LengthSquared < 0.25f)
			{
				d = _faceTarget - _startTop;       // 极短拉拽兜底（起点→钩点，同样是常量）
				d.z = 0f;
			}
			if (d.LengthSquared < 1e-4f)
			{
				return;
			}
			float targetDeg = MathF.Atan2(d.y, d.x) * (180f / MathF.PI);
			if (float.IsNaN(_bodyYawDeg))
			{
				_bodyYawDeg = LookYawDeg(_main);      // 播种 = 现在实际朝哪
			}
			float delta = Normalize180(targetDeg - _bodyYawDeg);
			float step = TurnRateDegPerSec * dt;
			_bodyYawDeg += MathF.Abs(delta) <= step ? delta : MathF.Sign(delta) * step;

			float rad = _bodyYawDeg * (MathF.PI / 180f);
			try
			{
				_main.SetMovementDirection(new Vec2(MathF.Cos(rad), MathF.Sin(rad)));
			}
			catch (Exception)
			{
				// 朝向只是观感，失败不拦拉拽
			}
		}

		/// <summary>角色当前朝向（度，atan2(y,x) 口径 —— 与相机/模板那套同一口径）。</summary>
		private static float LookYawDeg(Agent agent)
		{
			try
			{
				Vec3 look = agent.LookDirection;
				return MathF.Atan2(look.y, look.x) * (180f / MathF.PI);
			}
			catch (Exception)
			{
				return 0f;
			}
		}

		/// <summary>从角色位置看 <paramref name="target"/> 的水平方位角（度）。</summary>
		private float YawTo(Vec3 target)
		{
			if (_main == null)
			{
				return 0f;
			}
			Vec3 d = target - _main.Position;
			return MathF.Atan2(d.y, d.x) * (180f / MathF.PI);
		}

		private static float Normalize180(float deg)
		{
			while (deg > 180f) deg -= 360f;
			while (deg <= -180f) deg += 360f;
			return deg;
		}

		// ───────────────────────────── 相机 ─────────────────────────────

		/// <summary>
		/// 接管相机（脚本驱动的短期跟随）。失败不影响拉拽本身 —— 顶多回到引擎相机（跟丢就认了）。
		/// 时长给足：登板等待 + 拉拽 + 停稳 + 归还渐变的余量，**到点会自动滑回**（双保险）。
		///
		/// 🔴 **2026-10-05 阶段 2：整段收敛成"一次调用"**（原来是 5 步 setter 舞）——
		///    · 瞄准相机在手上 ⇒ `CameraService.Adopt(grapple_pull, …)`（**收编、不重播种**）；
		///    · 否则 ⇒ `CameraService.Play(grapple_pull, …)`（Seed=Engine = 照抄接管那一刻的引擎机位 + 行里的臂长 8）。
		///    归还策略（**撒手那一刻写回玩家朝向** / 撒手前清引擎特殊相机修正 / 不预置俯仰）作**调用参数**传进去 ——
		///    它们描述的是"引擎接下来会干什么"（收尾会解冻玩家 ⇒ 引擎必然重置相机），不进表。
		/// </summary>
		private void EnterCamera()
		{
			if (_main == null)
			{
				return;
			}
			_cameraReturning = false;
			if (!GrappleAimCamera.Enabled)      // 钩索相机总开关（`custom.grapple cam off`）
			{
				return;
			}
			if (CameraArmLength <= 0f)          // `cam off` = 不接管（对照用）
			{
				return;
			}
			// 🔴 机位参数来自 Camera.csv 的 `grapple_pull` 行（2026-10-05 阶段 1）——
			//    缺行 = **本次不接管相机**（拉拽本身照常跑，顶多回到引擎相机）；不做代码兜底。
			if (!TryGetCase(out CameraCase pullCase))
			{
				DebugLogger.Log("[Grapple] 🔴 Camera.csv 缺 `" + CaseName + "` 行 —— 本次拉拽不接管相机"
								+ "（拉拽照常；请检查 ModuleData/DesignData/Camera.csv）");
				return;
			}
			try
			{
				float seconds = OnBoardTimeout + _duration + SettleSeconds + CameraExtraSeconds;
				var policy = new CameraReturnPolicy
				{
					// 🔴🔴 **2026-10-10 改口径：方向不再"自己回正"，完全听玩家的**（用户裁定二选一）。
					//    旧行为 = 拉拽一开始就把镜头朝"引擎解冻后会重置成的值"转（yaw 保持、俯仰拉到 0），
					//    好处是撒手零跳变；坏处 = **玩家自己在飞行途中转的镜头会被扳回去**
					//    （用户原话："如果玩家转了镜头，最后我们自己还有一个重设旋转镜头的操作"）。
					//    新行为 = 拉拽全程鼠标可转（`Camera.csv` 的 `grapple_pull` 行 MouseLook=1），
					//    **撒手那一刻**才把玩家真实的朝向写回引擎（`WriteBackLookOnRelease`）——
					//    写回必须晚于"引擎解冻时的重置"（那条重置在归还滑行途中发生，早写会被打掉，
					//    2026-10-03 踩过），所以由机器在撒手那一刻执行，见 SpringArmRig.Stop。
					WriteBackLook = true,
					WriteBackLookOnRelease = true,
					ClearSpecial = true,       // 撒手前清引擎"特殊相机"冻结修正（落地高度台阶的根治）
					PredictReset = false,      // 不再预置俯仰：那正是"把玩家的镜头扳回去"的来源
				};
				bool adopt = GrappleAimCamera.IsActive && CameraService.IsHeldBy(GrappleAimCamera.Owner);
				if (adopt)
				{
					// 🔴 **瞄准相机已在接管**（开火 → 钩头在飞这段归它）—— **就地收编，不重新快照**：
					//    接管期间引擎相机是冻的，重抄它 = 镜头跳回瞄准起手那一刻。跟随本身不断
					//    （方向 = 玩家最后瞄的地方），只换臂长（行里的 8m）+ 归还三件套 + 时长。
					var stage = new CameraStage { Seconds = seconds, Policy = policy, HasPolicy = true };
					_cameraHeld = CameraService.Adopt(GrappleAimCamera.PullCaseName, GrappleAimCamera.Owner, in stage);
					if (_cameraHeld)
					{
						GrappleAimCamera.OnPullAdopt();
						DebugLogger.Log($"[Grapple] 相机接管：从瞄准相机无缝接手（case={pullCase.Id}，臂长 {pullCase.Param.ArmLength:F1}m，{seconds:F1}s 后自动归还）");
					}
					return;
				}

				_cameraHeld = CameraService.Play(pullCase.Id, _main, seconds,
					GrappleAimCamera.Owner, policy);
				DebugLogger.Log(_cameraHeld
					? $"[Grapple] 相机接管：引擎机位 + 臂长 {pullCase.Param.ArmLength:F1}m（{seconds:F1}s 后自动归还）"
					: "[Grapple] 相机接管失败 —— 用引擎相机继续");
			}
			catch (Exception ex)
			{
				_cameraHeld = false;
				DebugLogger.Log($"[Grapple] 相机接管异常（忽略）：{ex.GetType().Name} {ex.Message}");
			}
		}

		/// <summary>
		/// **提前开始归还相机**（拉拽尾段每帧调，幂等）：让机器把臂长/FOV 滑回接管时的引擎机位，
		/// 滑完自己撒手。**归还期间 `_cameraHeld` 保持 true** —— 这样中途 Abort 仍能
		/// <see cref="ExitCamera"/> 走"立刻还"（滑行中被打断不该留着镜头慢慢飘）。
		///
		/// 🔴 **方向不在这里还**（2026-10-10 改）：玩家全程能转镜头（`grapple_pull` 行的 MouseLook=1），
		///    真正的"朝向交还"发生在**撒手那一刻**（把玩家的角度写回引擎，见 `WriteBackLookOnRelease`）。
		/// </summary>
		private void StartCameraReturn()
		{
			if (!_cameraHeld || _cameraReturning)
			{
				return;
			}
			_cameraReturning = true;
			try
			{
				CameraService.RequestHandBack(CameraReturnGlideSeconds);
				DebugLogger.Log($"[Grapple] 相机提前归还：镜头开始往回滑，{CameraReturnGlideSeconds:F2}s 滑回引擎机位（还没落地就开始收）");
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 相机归还触发异常（忽略）：{ex.GetType().Name} {ex.Message}");
			}
		}

		/// <summary>归还相机：正常结束走渐变（不跳），异常路径立刻还。</summary>
		private void ExitCamera(bool immediate)
		{
			if (!_cameraHeld)
			{
				return;
			}
			_cameraHeld = false;
			_cameraReturning = false;
			try
			{
				// 🔴 **只有还归我们持有才动它**（2026-10-05 阶段 2：相机被演出/对话抢占过的话，
				//    这里再 Stop 就是在踩别人的相机 —— 服务按 owner 判断，不是我们的就不动）。
				if (!CameraService.IsHeldBy(GrappleAimCamera.Owner))
				{
					return;
				}
				if (immediate)
				{
					CameraService.Stop();
				}
				else
				{
					CameraService.RequestHandBack();
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 归还相机异常（忽略）：{ex.GetType().Name} {ex.Message}");
			}
		}

		// ───────────────────────────── 小工具 ─────────────────────────────

		/// <summary>把板挪到"板面 = <paramref name="top"/> 的世界坐标"（`MoveTo` 收的是原点，要减掉板厚）。</summary>
		private void MoveBoardTopTo(Vec3 top)
		{
			_board.MoveTo(new Vec3(top.x, top.y, top.z - FlightTuning.CarrierTopLocalZ));
		}

		/// <summary>人还在板上吗（判据同飞行工程：碰撞体底面与板面的三维距离）。</summary>
		private bool FellOffBoard()
		{
			float boardTop = _board.Origin.z + FlightTuning.CarrierTopLocalZ;
			float feet = CarrierBoard.CollisionCapsuleBottomZ(_main);
			float dz = MathF.Abs(feet - boardTop);
			float dx = new Vec2(_main.Position.x - _board.Origin.x, _main.Position.y - _board.Origin.y).Length;
			return dz > FallOffLimit || dx > FallOffLimit;
		}

		private static float CarrierBottomZ(Agent agent)
		{
			return CarrierBoard.CollisionCapsuleBottomZ(agent);
		}

		/// <summary>板下方的地面高度（用 §22.4 的口径；拿不到就用当前板面，宁可不动）。</summary>
		private static float GroundZ(Vec3 boardOrigin)
		{
			try
			{
				Scene scene = Mission.Current?.Scene;
				if (scene == null)
				{
					return boardOrigin.z;
				}
				float z = scene.GetGroundHeightAtPositionMT(
					new Vec3(boardOrigin.x, boardOrigin.y, boardOrigin.z + 2f),
					BodyFlags.CommonCollisionExcludeFlags);
				if (float.IsNaN(z) || z > 1e5f || z < -1e5f)
				{
					return boardOrigin.z;      // 悬空：当作"还没到地面"
				}
				return z;
			}
			catch (Exception)
			{
				return boardOrigin.z;
			}
		}

		private static float Clamp(float v, float lo, float hi)
		{
			return v < lo ? lo : (v > hi ? hi : v);
		}

		private static string Fmt(Vec3 v)
		{
			return string.Format("({0:F2},{1:F2},{2:F2})", v.x, v.y, v.z);
		}

		public string Describe()
		{
			return string.Format("pull={0} t={1:F2}/{2:F2} board[{3}]",
				_phase, _t, _duration, _board.Describe());
		}

		/// <summary>
		/// 体检口（`custom.grapple carrier`）：本轮载具的**物理足迹**（世界，半宽/半深/顶面高）
		/// + 板上"别人"的人数。没在拉拽（或板已拆）= false。
		/// </summary>
		public bool TryDescribeBoard(out Vec3 center, out float halfX, out float halfY, out float topZ, out int others)
		{
			others = 0;
			if (!_board.TryGetFootprint(out center, out halfX, out halfY, out topZ))
			{
				return false;
			}
			others = _board.CountRiders(Agent.Main, 0.15f);
			return true;
		}
	}
}
