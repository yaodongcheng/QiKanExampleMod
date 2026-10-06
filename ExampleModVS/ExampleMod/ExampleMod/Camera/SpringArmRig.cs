using System;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **归还策略三件套**（描述"引擎接下来会干什么"，不是"镜头长什么样"）——
	/// 由调用方按玩法进程决定，**不进表**（换玩法就要加一行 = 表会被时序污染）。
	/// </summary>
	public struct CameraReturnPolicy
	{
		/// <summary>归还时把我们的朝向**写回引擎**（`CameraBearing/Elevation`，反射写私有 setter）。</summary>
		public bool WriteBackLook;

		/// <summary>撒手前**清掉引擎"特殊相机"的冻结修正**（`_cameraSpecial*`）—— 钩索用（落地高度台阶的根治）。</summary>
		public bool ClearSpecial;

		/// <summary>归还时**预置俯仰为"引擎重置后的值"**（=0）—— 只有**确定引擎会重置**的调用方才能开（收尾会解冻玩家）。</summary>
		public bool PredictReset;
	}

	/// <summary>
	/// **换阶段的调用参数**（时序 / 玩法状态）—— 同样**不进表**：
	/// 依赖玩法进程，进表 = 每换玩法就要加一行。语义 = "在 case 的基础上再叠加什么"。
	/// </summary>
	public struct CameraStage
	{
		/// <summary>接管时长（秒；≤0 = 不限时，要手动还）。</summary>
		public float Seconds;

		/// <summary>臂长覆盖（米；≤0 = 用 case 行里的值）。</summary>
		public float ArmLength;

		/// <summary>归还策略（<see cref="HasPolicy"/> = false 时不动，沿用当前）。</summary>
		public CameraReturnPolicy Policy;

		/// <summary>本次是否要改归还策略。</summary>
		public bool HasPolicy;
	}

	/// <summary>
	/// **弹簧臂相机机器**（2026-10-05 立）—— **全项目唯一一份"摆相机"的状态机**。
	///
	/// 它从 <see cref="SpringArmCameraView"/> 的跟随机器原样搬出来（那部分是 2026-09-23 ~ 10-05 四轮实机
	/// 调出来的，**搬运时逐字保留**），再加上飞行相机那套能力（鼠标驱动 / 弹簧滞后 / 运动驱动 /
	/// case 间渐变 / `TryGetBasis`）—— 于是"跟随"与"飞行"不再是两台机器。
	///
	/// **谁在用**：<see cref="CameraService"/>（唯一入口，持有本类的一个实例）；
	/// 业务侧一律只说"用哪一行 case"（`Camera.csv`），**不直接碰本类**。
	///
	/// 🔴 **两条硬纪律**（都是实机撞出来的，别改）：
	///   ① **接管相机 = 必须接管"看"**：`MissionScreen.CustomCamera != null` 时引擎整段跳过鼠标 look
	///      ⇒ 谁接管相机，谁就得自己读鼠标（`ApplyMouseLook`）或由外部每帧喂（<see cref="SetLook"/>）。
	///   ② **方向归还的终点是"引擎即将重置成的值"**，不是"引擎此刻的值"（<see cref="ChaseEngineLook"/> 的注释里有完整链路）。
	/// </summary>
	public sealed class SpringArmRig
	{
		// ═════════════════════════════ 全局旋钮（会话级 / 标定） ═════════════════════════════

		/// <summary>
		/// **相机诊断日志总开关（默认关）** —— `custom.cam log 1` 打开（会话级）。
		/// 关着时只留少量生命周期行（接管 / 已归还 / 强制撒手 / 异常），逐帧 tick、交班明细、
		/// 撒手后采样、引擎内参等**诊断一律不打**。
		/// </summary>
		public static bool DebugLogging;

		/// <summary>跟随期间的固定高度修正（米）—— `custom.cam lift` 调它（标定/应急用；正常应为 0，
		/// 因为引擎那条 +0.484 米抬高已按公式补进 <see cref="SpringArmMath.ComputeEngineLift"/>）。</summary>
		public static float CameraLiftMeters;

		// ═════════════════════════════ 状态 ═════════════════════════════

		private Agent _agent;
		private CameraCase _case;                  // 当前 case（行为开关：MouseLook / LookSens / 锚点口径…）
		private string _caseName = "";
		private string _desc = "";                 // 日志用（谁、怎么起的）
		private CameraReturnPolicy _policy;

		// 机位参数：target = 本 case 的目标；current = 本帧实际；from/handFrom/handTo = 渐变两端
		private SpringArmCameraParam _target, _current, _from, _handFrom, _handTo;
		/// <summary>**接管那一刻的引擎机位**（归还渐变的终点：臂长 / FOV 要滑回它）。
		/// 🔴 单独存一份 —— `_from` 会被 `Switch`（飞行换机位）改成"上一个机位的值"，
		///    拿它当归还终点 = 撒手瞬间引擎从自己的视距接管 = 跳一下（原实现靠"只改 target、绝不碰 from"守住）。</summary>
		private SpringArmCameraParam _engineAtTakeover;
		private float _blendT = 1f, _blendDur = 0.35f;
		private float _handT = 1f, _handDur = 0.35f;
		private float _remain;
		private bool _useTimeout;

		private bool _active;
		private bool _handingBack;

		// ── "看"（世界口径的 yaw/pitch，度）──
		private float _lookYaw, _lookPitch;
		private bool _lookMouse;                   // case 自己驱动鼠标（Camera.csv 的 MouseLook 列）
		private bool _lookExternal;                // 外部每帧喂（SetLook）
		private bool _lookReturning;               // 方向归还中（chase 引擎）
		private float _lookReturnTau = 0.35f;
		private float _lookReturnSeconds;
		private float _lookLiveYaw0 = float.NaN, _lookLivePitch0;   // 起跑那刻引擎冻着的值（判断"重置发生了没"）
		private float _handHoldT;                                   // 方向没跟完时的等待计时
		private float _lastEngineYaw = float.NaN, _lastEnginePitch;  // 日志节流

		// ── 锚点（环绕点上下）──
		private float _pivotZ;
		private bool _pivotZInit;
		private bool _offsetsDirty;                // 写过 Pivot/Socket 偏移 ⇒ 归还时要滑回 0

		// ── 弹簧跟随 + 运动驱动（飞行能力，2026-10-05 阶段 4 并入）──
		private readonly SpringArmLagState _lag = new SpringArmLagState();
		private SpringArmMotion _motion;

		// ── 诊断 ──
		private float _elapsed, _lastDt;
		private Vec3 _lastAnchor, _lastCam;
		private bool _hasLast;
		private bool _errorLogged;
		private bool _anchorLogged;                // 锚点解析行（每次接管一行）
		private float _postReleaseTimer = -1f;     // 撒手后采样（<0 = 没在等）
		private bool _postReleaseFirst;
		private float _postReleaseWatchTimer = -1f;
		private int _postReleaseWatchLeft;

		private const float FollowBlendSeconds = 0.35f;      // 进出场渐变时长（臂长/FOV）
		private const float HandBackLookChaseTau = 0.35f;    // 方向追赶的时间常数
		private const float HandBackLookHoldMaxSeconds = 1.2f;
		private const float HandBackLookConvergedDeg = 2.0f;
		private const float PostReleaseLogDelaySeconds = 0.5f;
		private const float PostReleaseWatchSeconds = 3f;
		private const float RadToDeg = 180f / MathF.PI;
		private const float DegToRad = MathF.PI / 180f;
		private const float HeadAnchorUpOffset = 0.10f;
		private const float AnchorSmoothSpeed = 6f;

		// ═════════════════════════════ 查询 ═════════════════════════════

		/// <summary>在跟（含归还渐变中）。</summary>
		public bool Active => _active;

		/// <summary>归还渐变是否在走（调用方据此决定还要不要继续喂）。</summary>
		public bool HandingBack => _handingBack;

		/// <summary>方向归还是否已起跑。</summary>
		public bool LookReturning => _lookReturning;

		/// <summary>当前 case 名（诊断/回显）。</summary>
		public string CaseName => _caseName;

		/// <summary>本帧实际生效的机位参数（诊断）。</summary>
		public SpringArmCameraParam Current => _current;

		/// <summary>当前朝向前向（度，供诊断）。</summary>
		public float LookYaw => _lookYaw;
		public float LookPitch => _lookPitch;

		/// <summary>渐变进度（诊断）。</summary>
		public float BlendProgress => _blendT;

		// ═════════════════════════════ 起播 ═════════════════════════════

		/// <summary>
		/// **起播一个机位 case**（方向/臂长从哪来由 case 的 `Seed` 列决定）——
		/// 这一条把原来两条入口合并了：
		/// <list type="bullet">
		///   <item><c>Seed=Engine</c>（`follow_engine` / `grapple_shot` / 飞行四档）= 原 `ApplyFollowFromEngineCamera`
		///         ——方向逐度照抄引擎**此刻**的机位（不甩不抖）、臂长/FOV 用行里非零的值（0 = 保留引擎当时的值）；</item>
		///   <item><c>Seed=Row</c>（全部演出模板）= 原 `ApplyFollowTemplate`
		///         ——方向按本行 ArmYaw/ArmPitch，臂长/FOV **从引擎此刻的值渐变过去**（进场不硬切）。</item>
		/// </list>
		/// 返回 false = 起不来（没 mission screen / 相机拿不到 / 引擎机位读不出）。
		/// </summary>
		public bool Start(CameraCase kase, Agent agent, float seconds, CameraReturnPolicy policy, string desc)
		{
			if (kase == null || agent == null)
				return false;
			if (ScreenManager.TopScreen as MissionScreen == null)
				return false;
			if (!TryBuildEngineCameraParam(agent, out SpringArmCameraParam engine))
				return false;

			_agent = agent;
			_case = kase;
			_caseName = kase.Id;
			_desc = desc;
			_policy = policy;
			_offsetsDirty = false;
			_anchorLogged = false;
			_pivotZInit = false;
			_engineAtTakeover = engine;      // 归还渐变的终点（见字段注释）

			_target = kase.Param;
			if (kase.Seed == CameraSeed.Engine)
			{
				// 方向照抄引擎（**世界锚定必须为真**：抄来的角是世界角）；行里填 0 的量保留引擎当时的值。
				// ⚠️ 其余字段（`UseEngineEyeHeight` 锚点口径 / `LagSpeed` / 锚点开关…）**一律以行里的值为准**，
				//    机器不替它做决定（那正是"数值住表里"的意义）。
				_target.IsAnchorWorld = true;
				_target.ArmYaw = engine.ArmYaw;
				_target.ArmPitch = engine.ArmPitch;
				if (_target.ArmLength <= 0f)
					_target.ArmLength = engine.ArmLength;
				if (_target.Fov <= 0f)
					_target.Fov = engine.Fov;

				// 🔴 **起点 = 引擎那一份**（不是 target！）—— 它是**归还渐变的终点**：
				//    行里写了臂长（如 `grapple_shot` 的 2.8）时，归还仍要滑回**引擎当时的视距**，
				//    否则撒手瞬间引擎从它自己的视距接管 = 跳一下（原实现靠"`SetFollowArmLength` 只改 target、
				//    绝不碰 `_followFrom`"这条纪律保住，搬进机器时必须原样保住）。
				_from = engine;
				// 无进场渐变：方向本来就逐度一致，臂长/FOV 的差在这一支里是一次到位（= 原实现的行为）
				_blendT = 1f;
			}
			else
			{
				// Seed=Row：方向按本行；臂长/FOV 从引擎此刻的值渐变过去
				_from = _target;
				_from.ArmLength = engine.ArmLength;
				_from.Fov = engine.Fov;
				_blendT = 0f;
			}

			_blendDur = Math.Max(0.01f, FollowBlendSeconds);
			_current = _from;
			_handingBack = false;
			_handT = 1f;
			_handHoldT = 0f;
			_lookMouse = kase.MouseLook;
			_lookExternal = false;
			_lookReturning = false;
			_lookYaw = _target.ArmYaw;
			// 播种的俯仰也按本 case 的钳位走（老 `SeedLookFromEngineCamera` 就是这个口径）——
			// 只有"要自己驱动鼠标"的 case 才钳（其余 case 的方向来自行/渐变，不该被动）。
			_lookPitch = _lookMouse
				? MBMath.ClampFloat(_target.ArmPitch, kase.PitchMin, kase.PitchMax)
				: _target.ArmPitch;
			_useTimeout = seconds > 0f;
			_remain = seconds;
			_elapsed = 0f;
			_hasLast = false;
			_errorLogged = false;
			_lag.Reset();
			_motion = default;
			_active = true;

			DebugLogger.Log($"[FollowCam] 接管相机（{desc}｜case={kase.Id}）锚={agent.Name} "
							+ $"时长={(_useTimeout ? seconds.ToString("0.0") + "s" : "不限")} "
							+ $"臂长 {_from.ArmLength:F1}->{_target.ArmLength:F1} fov {_from.Fov:F0}->{_target.Fov:F0} "
							+ $"方向={(_target.IsAnchorWorld ? "世界锚定" : "角色相对")} 鼠标={(_lookMouse ? 1 : 0)}");
			if (DebugLogging)
			{
				DebugLogger.Log($"[FollowCam] 接管明细（{desc}）：我们 yaw={_target.ArmYaw:F0} pitch={_target.ArmPitch:F0} "
								+ EngineLookSuffix() + PreTakeoverSuffix());
			}
			return true;
		}

		/// <summary>
		/// **换阶段**（同一位持有者继续用，**不重播种**）—— 钩索"瞄准 → 拉拽"走这条。
		/// 语义 = 在当前状态上**叠加**：只改"看得见的量"（臂长 / FOV / 鼠标接管 / 锚点口径 / 归还策略 / 时长），
		/// **方向与其余偏移一律保持现状**（重新播种 = 镜头跳回引擎那一刻，实机踩过）。
		/// </summary>
		public bool Adopt(CameraCase kase, in CameraStage stage)
		{
			if (!_active || kase == null)
				return false;

			_case = kase;
			_caseName = kase.Id;
			if (kase.Param.ArmLength > 0f)
				_target.ArmLength = kase.Param.ArmLength;
			if (kase.Param.Fov > 0f)
				_target.Fov = kase.Param.Fov;
			if (stage.ArmLength > 0f)
				_target.ArmLength = stage.ArmLength;

			_lookMouse = kase.MouseLook;
			_lookExternal = false;
			// 🔴 收编时方向**回到 target 那一份**（= 原 `ClearFollowLook` 的行为：交还方向控制、跟随不断）——
			//    除非方向归还已经在跑（那由 chase 状态接着走，不许重置）。
			if (!_lookReturning)
			{
				_lookYaw = _target.ArmYaw;
				_lookPitch = _target.ArmPitch;
			}
			if (stage.HasPolicy)
				_policy = stage.Policy;
			if (stage.Seconds > 0f)
				SetTimeout(stage.Seconds);

			DebugLogger.Log($"[FollowCam] 换阶段 → case={kase.Id}（不重播种）臂长={_target.ArmLength:F1} "
							+ $"fov={_target.Fov:F0} 鼠标={(_lookMouse ? 1 : 0)} "
							+ (stage.Seconds > 0f ? $"时长={stage.Seconds:F1}s " : "")
							+ $"| 方向 yaw={_lookYaw:F0} pitch={_lookPitch:F0}");
			return true;
		}

		/// <summary>**同持有者换 case**（飞行机位切换走这条）：臂长/FOV/Socket 从当前值渐变到新 case。</summary>
		public bool Switch(CameraCase kase, float blendSeconds)
		{
			if (!_active || kase == null)
				return false;

			_case = kase;
			_caseName = kase.Id;
			_target = kase.Param;
			// 世界锚定是**机器驱动鼠标的前提**（`ArmYaw` 是世界角）—— 这一条由机器保证；
			// 其余字段（含锚点口径 `UseEngineEyeHeight`）一律以行里的值为准。飞行四档在表里就是 1/0 这么写的。
			_target.IsAnchorWorld = true;
			_from = _current;
			_blendT = 0f;
			_blendDur = Math.Max(0.01f, blendSeconds);
			return true;
		}

		/// <summary>
		/// **一次性静态机位**（对话/剧情取景，= 原 `UseCameraTemlate`）：只按"那一刻"的角色帧算一次，
		/// **不进状态机**（角色一动就出画，这是它一直以来的语义）。
		/// 🔴 行为逐字冻结：`AttachType` 选锚点（Player/Speaker/Listener；AnchorWorld 实际也落到主角 ——
		/// 老实现里那句"targetAgent = null"紧接着就被 MainAgent 兜底顶掉），且**强制 IsAnchorWorld = false**
		/// （原实现就是这么写的，改了会影响所有相机模板）。
		/// </summary>
		public bool ApplyStaticPose(CameraCase kase, Agent speaker, Agent listener, Vec3 anchorWorldPos)
		{
			if (kase == null || Mission.Current == null)
				return false;

			Agent target;
			switch (kase.AttachType)
			{
				case "Speaker": target = speaker; break;
				case "Listener": target = listener; break;
				case "AnchorWorld": target = null; break;
				default: target = Agent.Main; break;
			}

			SpringArmCameraParam p = kase.Param;
			return ApplyStaticFrame(target, in p, kase.Id);
		}

		/// <summary>
		/// 一次性机位（**显式指定锚点角色**）—— 调试滑杆 UI 与 `custom.useSpringArmCamera &lt;模板&gt; &lt;agentId&gt;`
		/// 走这条。同样**强制 IsAnchorWorld = false**（一次性机位路径行为冻结）。
		/// </summary>
		public bool ApplyStaticFrame(Agent target, in SpringArmCameraParam param, string tag)
		{
			if (Mission.Current == null)
				return false;
			if (target == null || target.Mission == null)
				target = Mission.Current.MainAgent;
			if (target == null)
				return false;

			SpringArmCameraParam p = param;
			p.IsAnchorWorld = false;      // ← 一次性机位路径行为冻结（见 ApplyStaticPose 注释）
			SpringArmMath.ComputeFrame(target, in p, out MatrixFrame frame, out float fovDeg);

			_agent = target;
			_current = p;
			_caseName = tag ?? "";
			_active = false;              // 静态机位不进状态机（持有权由 service 记着）
			_handingBack = false;

			DebugLogger.Log($"[FollowCam] 一次性机位 case={tag} 锚={target.Name}");
			WriteCamera(frame, fovDeg);
			return true;
		}

		// ═════════════════════════════ 每帧 ═════════════════════════════

		/// <summary>
		/// 每帧推进（**由 <see cref="CameraService"/> 无条件调用**：没接管时也要跑，因为撒手后的
		/// 诊断采样挂在这里）。相机出错**绝不能拖垮任务** —— 出错就立刻还给引擎。
		/// </summary>
		public void Tick(float dt)
		{
			TickPostRelease(dt);
			if (!_active || dt <= 0f)
				return;

			// 锚点没了（移除 / 换场景）就收摊。native 属性可能抛，按项目惯例全包起来。
			bool anchorAlive;
			try { anchorAlive = _agent != null && _agent.Mission != null && Mission.Current?.MainAgent != null; }
			catch { anchorAlive = false; }
			if (!anchorAlive)
			{
				DebugLogger.Log("[FollowCam] 锚点没了（换场景/agent 移除）—— 归还相机");
				CameraService.Stop();
				return;
			}

			try
			{
				// ① 推进渐变（臂长 / FOV / Socket …）
				if (_handingBack)
				{
					_handT = Math.Min(1f, _handT + dt / _handDur);
					_current = SpringArmMath.Lerp(in _handFrom, in _handTo, SpringArmMath.Ease(_handT));
				}
				else
				{
					if (_blendT < 1f)
						_blendT = Math.Min(1f, _blendT + dt / _blendDur);
					_current = _blendT >= 1f
						? _target
						: SpringArmMath.Lerp(in _from, in _target, SpringArmMath.Ease(_blendT));

					if (_useTimeout)
					{
						_remain -= dt;
						if (_remain <= 0f)
						{
							BeginHandBack(0f);
							return;                    // 本帧不写相机，下一帧开始渐变
						}
					}
				}

				// ② 方向（优先级从低到高：渐变值 → 鼠标/外部 → 归还 chase）
				if (_lookReturning)
				{
					ChaseEngineLook(ref _lookYaw, ref _lookPitch, dt);
				}
				else if (!_handingBack && _lookMouse && !_lookExternal)
				{
					ApplyMouseLook(Input.MouseMoveX, Input.MouseMoveY);
				}
				if (_lookReturning || _lookMouse || _lookExternal)
				{
					_current.ArmYaw = _lookYaw;
					_current.ArmPitch = _lookPitch;
				}

				// ③ 锚点（跟头骨 / 定高）
				if (!_handingBack)
					UpdateAnchor(dt);

				// ④ 归还：臂长走完 + 方向跟平 ⇒ 撒手
				if (_handingBack && _handT >= 1f)
				{
					if (EngineLookConverged(_current))
					{
						CameraService.Stop();
						return;
					}
					_handHoldT += dt;
					if (_handHoldT >= HandBackLookHoldMaxSeconds)
					{
						DebugLogger.Log($"[FollowCam] 方向没在等待上限内跟完（还差 {EngineLookDeltaDeg(_current):F1}°）—— 强制撒手");
						CameraService.Stop();
						return;
					}
				}

				// ⑤ 摆相机
				WriteCurrentFrame(dt);
			}
			catch (Exception ex)
			{
				if (!_errorLogged)
				{
					_errorLogged = true;
					DebugLogger.Log($"[FollowCam] 每帧写相机异常，已归还引擎相机: {ex.GetType().Name}: {ex.Message}");
				}
				CameraService.Stop();
			}
		}

		/// <summary>
		/// 算本帧机位并写进相机（**每帧**）。
		///
		/// 🔴 **三处都要写**（2026-09-23 实机教训 + 2026-10-05 反编译复核）：
		///   ① `相机实体.SetGlobalFrame(frame)` —— 引擎真正读的是这个；
		///   ② `相机.Frame = frame` —— 相机自己那份也保持一致；
		///   ③ `screen.CustomCamera = 相机` —— 由 <see cref="CameraService"/> 统一呈现（唯一写者）。
		/// </summary>
		private void WriteCurrentFrame(float dt)
		{
			try
			{
				SpringArmCameraView view = Mission.Current?.GetMissionBehavior<SpringArmCameraView>();
				if (view == null)
					return;

				SpringArmCameraParam p = _current;
				// 🔴 运动驱动（飞行能力）：竖直速率 → FOV/臂长、航向角速度 → 侧倾。
				//    演出/钩索不喂运动量（`_motion` 全零）⇒ 与没这一行逐字节一致。
				p = SpringArmMath.WithMotion(in p, in _motion);
				SpringArmMath.ComputeFrame(_agent, in p, out MatrixFrame frame, out float fovDeg);

				// 弹簧跟随（相机**位置**滞后）：滞后的是"角色锚点"，相机跟着挪。渐隐见 SpringArmLagState。
				float lagSpeed = _handingBack ? 0f : p.LagSpeed;
				float lagFade = _handingBack ? (1f - _handT) : 1f;
				frame.origin += _lag.Update(_agent.LookFrame.origin, lagSpeed, p.LagMaxDistance, dt, lagFade);

				// 固定高度修正（`custom.cam lift` 标定用；0 = 不修）
				if (CameraLiftMeters != 0f)
					frame.origin.z += CameraLiftMeters;

				_lastDt = dt;
				LogFollowTick(_agent, frame, view);
				WriteCamera(frame, fovDeg);
			}
			catch (Exception ex)
			{
				if (!_errorLogged)
				{
					_errorLogged = true;
					DebugLogger.Log($"[FollowCam] 每帧写相机异常，已归还引擎相机: {ex.GetType().Name}: {ex.Message}");
				}
				CameraService.Stop();
			}
		}

		/// <summary>把算好的帧写进相机（+ 实体）并交给 service 呈现。<see cref="ApplyStaticPose"/> 也用这条。</summary>
		private void WriteCamera(in MatrixFrame frame, float fovDeg)
		{
			SpringArmCameraView view = Mission.Current?.GetMissionBehavior<SpringArmCameraView>();
			if (view == null || !view.EnsureCameraEntity())
				return;

			Camera cam = view.Camera;
			if (cam == null)
				return;

			MatrixFrame f = frame;
			view.CamEntity.SetGlobalFrame(in f);                       // ① 引擎读的那份
			cam.SetFovVertical(fovDeg * DegToRad, Screen.AspectRatio, 0.1f, 1000f);
			cam.Frame = frame;                                          // ② 相机自己那份
			CameraService.Present(cam);                                 // ③ 唯一入口（service）
		}

		// ═════════════════════════════ "看" ═════════════════════════════

		/// <summary>
		/// **鼠标驱动**（本 case 的 `MouseLook=1` 时每帧自动跑）—— 接管相机就必须接管"看"，
		/// 这是玩家在接管期间唯一能转视角的地方。灵敏度 = case 的 `LookSens` × 引擎的
		/// `Input.MouseSensitivity`（跟随玩家设置）；左右/上下反向 = 全局设备偏好。
		/// </summary>
		public void ApplyMouseLook(float mouseDx, float mouseDy)
		{
			if (!_active || _case == null || _handingBack)
				return;

			float s = _case.LookSens * Math.Max(0.05f, Input.MouseSensitivity);
			float sx = Flight.FlightTuning.InvertCamX ? 1f : -1f;
			float sy = Flight.FlightTuning.InvertCamY ? 1f : -1f;

			_lookYaw += mouseDx * s * sx;
			_lookPitch += mouseDy * s * sy;

			while (_lookYaw > 180f) _lookYaw -= 360f;
			while (_lookYaw < -180f) _lookYaw += 360f;
			_lookPitch = MBMath.ClampFloat(_lookPitch, _case.PitchMin, _case.PitchMax);
		}

		/// <summary>**外部朝向驱动**：每帧调用则本帧方向 = 给定角（停止用 <see cref="ReleaseLook"/>）。</summary>
		public void SetLook(float yawDeg, float pitchDeg)
		{
			_lookExternal = true;
			_lookYaw = yawDeg;
			_lookPitch = pitchDeg;
		}

		/// <summary>停用外部朝向驱动（方向回到鼠标驱动 / 渐变 / 归还 chase 决定）。</summary>
		public void ReleaseLook()
		{
			_lookExternal = false;
		}

		/// <summary>相机前向 / 右向（飞行方向、施法方向等"看向哪"的消费者用它）。没接管 = false。</summary>
		public bool TryGetBasis(out Vec3 forward, out Vec3 right)
		{
			forward = Vec3.Zero;
			right = Vec3.Zero;
			if (!_active)
				return false;

			try
			{
				Mat3 m = Mat3.Identity;
				m.RotateAboutUp(_current.ArmYaw * DegToRad);
				m.RotateAboutSide(_current.ArmPitch * DegToRad);
				if (m.f.LengthSquared < 0.0001f)
					return false;
				forward = m.f.NormalizedCopy();
				right = m.s.NormalizedCopy();
				return true;
			}
			catch
			{
				return false;
			}
		}

		// ═════════════════════════════ 锚点（环绕点上下） ═════════════════════════════

		/// <summary>
		/// 每帧算**环绕点偏移 Z**（= UE 弹簧臂的 `TargetOffset.Z`）——
		/// `AnchorFollowHead=1` 的 case（钩索瞄准）在这里贴**动画头骨**：
		/// 引擎的眼高公式只认引擎自己的蹲姿（`CrouchMode`），**认不出纯动画的蹲伏**（ready/hold）
		/// ⇒ 不跟的话环绕点悬在站姿 1.90 m 上、蹲着瞄时取景不对。
		/// </summary>
		private void UpdateAnchor(float dt)
		{
			if (_case == null || _agent == null)
				return;

			// 🔴 **本 case 没开锚点口径 = 一个字节都不碰**（保留现状）——
			//    钩索拉拽收编瞄准相机就是靠这条：瞄准期压下去的环绕点**留在原地**（归还渐变再滑回 0），
			//    改成"没开就归零" = 拉拽一开始镜头自己往上飘一下（与 2026-10-05 验收过的行为不符）。
			bool headMode = _case.AnchorFollowHead;
			bool fixedMode = !headMode && _case.AnchorHeight > 0f;
			if (!headMode && !fixedMode)
				return;

			float targetPivotZ;
			string mode;
			float engineEye = SpringArmMath.ResolveEyeHeightOffset(_agent, useEngineFormula: true);
			float baseZ = BaseZ(_agent);
			if (headMode && TryGetHeadHeight(_agent, out float headZ))
			{
				targetPivotZ = (headZ + HeadAnchorUpOffset) - (baseZ + engineEye);
				mode = "head-bone";
			}
			else if (fixedMode)
			{
				targetPivotZ = _case.AnchorHeight - (baseZ + engineEye);
				mode = _case.AnchorHeight.ToString("F2") + "m";
			}
			else
			{
				targetPivotZ = 0f;      // 引擎口径（不偏）
				mode = "head-bone(采样失败→引擎口径)";
			}

			if (!_pivotZInit)
			{
				_pivotZInit = true;
				_pivotZ = targetPivotZ;      // 首帧直接到位：接管那一刻取景就是对的位置
				if (!_anchorLogged)
				{
					_anchorLogged = true;
					DebugLogger.Log($"[FollowCam] 锚点解析：mode={mode} baseZ={baseZ:F2} engineEye={engineEye:F2} "
									+ (_case.AnchorFollowHead && mode.StartsWith("head") ? $"headZ={HeadZ(_agent):F2} " : "")
									+ $"pivotZ={targetPivotZ:F2} => 环绕点 z={baseZ + engineEye + targetPivotZ:F2}");
				}
			}
			else
			{
				float k = MathF.Min(1f, dt * AnchorSmoothSpeed);
				_pivotZ += (targetPivotZ - _pivotZ) * k;
			}

			_target.PivotZ = _pivotZ;
			_offsetsDirty = _offsetsDirty || Math.Abs(_pivotZ) > 0.001f;
		}

		/// <summary>锚点基准 Z（与 `SpringArmMath.ComputeFrame` 同口径：优先 VisualPosition，取不到回落逻辑位置）。</summary>
		private static float BaseZ(Agent agent)
		{
			try
			{
				Vec3 vp = agent.VisualPosition;
				if (vp.LengthSquared > 0.01f)
					return vp.z;
			}
			catch { }
			try { return agent.Position.z; } catch { return 0f; }
		}

		private static float HeadZ(Agent agent) => TryGetHeadHeight(agent, out float z) ? z : 0f;

		/// <summary>头骨世界高度（读法照 `SpellCastInput.TryGetRightHandAnchor`：`Monster.HeadLookDirectionBoneIndex`
		/// + `AgentVisuals.GetBoneEntitialFrame`，读的就是当前动画帧）。离角色太远（&gt;3 m）= 骨帧不可信 → false。</summary>
		private static bool TryGetHeadHeight(Agent agent, out float headZ)
		{
			headZ = 0f;
			try
			{
				if (agent?.Monster == null)
					return false;
				sbyte bone = agent.Monster.HeadLookDirectionBoneIndex;
				if (bone < 0)
					return false;
				MBAgentVisuals visuals = agent.AgentVisuals;
				if (visuals == null || !visuals.IsValid())
					return false;
				Vec3 head = visuals.GetBoneEntitialFrame(bone, useBoneMapping: false).origin;
				if (head.LengthSquared < 1e-6f)
					return false;
				Vec3 d = head - agent.Position;
				if (d.LengthSquared > 9f)
					return false;
				headZ = head.z;
				return true;
			}
			catch
			{
				return false;
			}
		}

		// ═════════════════════════════ 现场改 ═════════════════════════════

		/// <summary>改臂长（米）—— **只改 target**，进场/归还的渐变端点不受影响（2026-10-03 实机教训）。</summary>
		public void SetArmLength(float meters)
		{
			if (_active && meters > 0f)
				_target.ArmLength = meters;
		}

		/// <summary>改"环绕点"偏移 Z（米；角色系）＝ UE `TargetOffset.Z`。归还时滑回 0（引擎口径）。</summary>
		public void SetPivotZ(float meters)
		{
			if (!_active)
				return;
			_target.PivotZ = meters;
			_offsetsDirty = true;
		}

		/// <summary>改"相机"偏移 Z（米；相机系）＝ UE `SocketOffset.Z`。归还时滑回 0。</summary>
		public void SetSocketZ(float meters)
		{
			if (!_active)
				return;
			_target.SocketZ = meters;
			_offsetsDirty = true;
		}

		/// <summary>喂本帧的运动量（飞行行为层每帧调；不喂 = 全零 = 无影响）。</summary>
		public void SetMotion(in SpringArmMotion m)
		{
			float g = Flight.FlightTuning.CamMotionGain;
			_motion.Vz = m.Vz * g;
			_motion.YawRate = m.YawRate * g;
			_motion.Speed = m.Speed;
		}

		/// <summary>改超时（秒；≤0 = 不限时）。</summary>
		public void SetTimeout(float seconds)
		{
			_useTimeout = seconds > 0f;
			_remain = seconds;
			_elapsed = 0f;
		}

		/// <summary>改归还策略三件套（收编时把拉拽侧的旗标接过来）。</summary>
		public void SetReturnPolicy(in CameraReturnPolicy policy)
		{
			_policy = policy;
		}

		/// <summary>
		/// **表行被热改了**（`custom.cam set`）—— 若改的正是当前在用的那一行，把"看得见的量"同步进 live 目标
		/// （臂长 / FOV / 相机的 Pivot-XY·Socket / 自转 / 滞后 / 运动驱动系数），**方向与环绕点 Z 不动**
		/// （那两个是运行期状态：方向由鼠标/chase 管，环绕点 Z 由锚点逻辑管）。
		/// 不这样"同步一下"的话，`set` 的效果要等下次接管才看得到（验收要求"实时可见"）。
		/// </summary>
		public void RefreshFromCase(CameraCase kase)
		{
			if (!_active || kase == null || !ReferenceEquals(_case, kase))
				return;

			SpringArmCameraParam p = kase.Param;
			_target.ArmLength = p.ArmLength;
			_target.Fov = p.Fov;
			_target.PivotX = p.PivotX;
			_target.PivotY = p.PivotY;
			_target.SocketX = p.SocketX;
			_target.SocketY = p.SocketY;
			_target.SocketZ = p.SocketZ;
			_target.SelfYaw = p.SelfYaw;
			_target.SelfPitch = p.SelfPitch;
			_target.SelfRoll = p.SelfRoll;
			_target.LagSpeed = p.LagSpeed;
			_target.LagMaxDistance = p.LagMaxDistance;
			_target.FovPerVz = p.FovPerVz;
			_target.ArmPerVz = p.ArmPerVz;
			_target.RollPerYawRate = p.RollPerYawRate;
			_target.UseEngineEyeHeight = p.UseEngineEyeHeight;
		}

		/// <summary>
		/// **只启动"方向归还"**（臂长/FOV 的归还仍按自己的时机起）—— 给"想从更早开始把镜头转过去"的调用方用
		/// （钩索：**拉拽一开始**就调它，方向在整个拉拽里平顺走完；臂长仍保持宽镜到后半程）。
		/// 幂等：已经在归还中就不重复初始化（重复 = 每帧重置 = 增量不累积）。
		/// </summary>
		public void BeginLookReturn(float seconds)
		{
			if (!_active || _lookReturning)
				return;
			_lookReturning = true;
			_lookReturnSeconds = seconds;
			_lookReturnTau = MBMath.ClampFloat((seconds > 0.1f ? seconds : 1.2f) / 3.5f, 0.2f, 1.5f);
			SampleEngineLookBaseline();
			if (!_handingBack)
			{
				_lookYaw = _current.ArmYaw;
				_lookPitch = _current.ArmPitch;
			}
			DebugLogger.Log($"[FollowCam] 方向归还开始：{seconds:F2}s 内平顺转到预测重置值（τ={_lookReturnTau:F2}s）");
		}

		// ═════════════════════════════ 归还 / 收场 ═════════════════════════════

		/// <summary>
		/// **请求渐变归还**（= 超时那条路，只是由调用方主动触发）：把臂长/FOV 滑回接管时引擎相机的值，
		/// 滑完（且方向与引擎收敛）才撒手。已经在归还中 / 没接管 = 什么都不做。
		/// <paramref name="blendSeconds"/> &gt; 0 = 指定滑行时长。
		/// </summary>
		public void RequestHandBack(float blendSeconds = 0f)
		{
			if (_active && !_handingBack)
				BeginHandBack(blendSeconds);
		}

		/// <summary>
		/// 归还前渐变：把**臂长 / FOV** 滑回接管时引擎相机的值，滑完才真撒手（硬切会"跳"一下）。
		/// 🔴 **方向也渐**（世界锚定 case）—— 但目标不是"引擎此刻的值"，是**引擎即将重置成的值**
		/// （见 <see cref="ChaseEngineLook"/> 的完整链路）。非世界锚定（演出模板）= 方向不渐。
		/// </summary>
		private void BeginHandBack(float blendSeconds)
		{
			_handingBack = true;
			_handT = 0f;
			_handHoldT = 0f;
			_handDur = blendSeconds > 0.05f ? blendSeconds : FollowBlendSeconds;
			_handFrom = _current;
			_handTo = _target;
			_handTo.ArmLength = _engineAtTakeover.ArmLength;   // 终点 = 接管那一刻的引擎视距（见字段注释）
			_handTo.Fov = _engineAtTakeover.Fov;
			// 写过 Pivot/Socket 偏移的（瞄准相机 / 拉拽收编它）⇒ 归还时滑回 0（引擎口径），
			// 不归零 = 撒手瞬间"环绕点/机位跳一下"（2026-10-04 落地高度台阶的同族问题）。
			if (_offsetsDirty)
			{
				_handTo.PivotZ = 0f;
				_handTo.SocketZ = 0f;
			}
			else
			{
				_handTo.PivotZ = _handFrom.PivotZ;
				_handTo.SocketZ = _handFrom.SocketZ;
			}

			// 方向 = 独立状态、逐帧累积（**已经在归还中就不要重置**）。
			if (!_lookReturning)
			{
				_lookYaw = _handFrom.ArmYaw;
				_lookPitch = _handFrom.ArmPitch;
				_lookReturnTau = HandBackLookChaseTau;
				SampleEngineLookBaseline();
			}
			_lookReturning = true;

			bool wroteBack = false;
			float yawDelta = 0f, pitchDelta = 0f;
			bool chaseLook = false;
			if (_current.IsAnchorWorld)
			{
				_handFrom.IsAnchorWorld = true;                 // 两端口径统一（世界锚定）
				_handTo.IsAnchorWorld = true;
				_handTo.ArmYaw = _handFrom.ArmYaw;              // 方向交给 ChaseEngineLook，不走 Lerp
				_handTo.ArmPitch = _handFrom.ArmPitch;
				if (_policy.WriteBackLook)
				{
					wroteBack = WriteBackLookToEngine(_handFrom.ArmYaw, _handFrom.ArmPitch);
				}
				if (!wroteBack)
				{
					chaseLook = true;
					if (CameraLook.TryGetEngineAnglesRaw(out float engYawDeg, out float engPitchDeg))
					{
						yawDelta = Normalize180(_handFrom.ArmYaw - engYawDeg);
						pitchDelta = _handFrom.ArmPitch - engPitchDeg;
					}
					_lastEngineYaw = float.NaN;                 // 让滑行第一帧把"引擎当前角度"记进日志
				}
			}

			DebugLogger.Log($"[FollowCam] 归还渐变开始：臂长 {_handFrom.ArmLength:F1}→{_handTo.ArmLength:F1} "
				+ $"fov {_handFrom.Fov:F0}→{_handTo.Fov:F0} "
				+ $"用时 {_handDur:F2}s "
				+ (wroteBack
					? $"方向：已写回引擎（{_handFrom.ArmYaw:F0}/{_handFrom.ArmPitch:F0}°）—— 撒手零旋转"
					: (chaseLook
						? $"方向：朝预测重置值走（我们 {_handFrom.ArmYaw:F0}/{_handFrom.ArmPitch:F0}°，此刻引擎差 {yawDelta:F0}/{pitchDelta:F0}°）"
						: "方向：不渐（非世界锚定）")));
		}

		/// <summary>
		/// **立刻撒手**（幂等；任何异常都要保证还）。**不渐变**（异常/收摊路径用）。
		/// 🔴 撒手动作本身（`CustomCamera = null`）由 <see cref="CameraService.Stop"/> 执行 —— 唯一写者。
		/// </summary>
		public void Stop()
		{
			if (!_active && !_handingBack && !_lookReturning)
			{
				// 一次性静态机位（_active=false）也要能收
				_handingBack = false;
				_lookReturning = false;
				return;
			}

			_active = false;
			_handingBack = false;
			_lookReturning = false;
			_lookExternal = false;
			_offsetsDirty = false;
			_lag.Reset();
			_motion = default;

			if (DebugLogging)
				LogHandOff(_current);

			// 撒手前清掉引擎冻结的"特殊相机"修正（瞄准态残留 = 落地高度台阶的来源）
			if (_policy.ClearSpecial)
				ClearEngineSpecialCameraAdds();

			// 撒手后的连续采样（诊断；`custom.cam log 1` 才跑）
			_postReleaseFirst = true;
			_postReleaseTimer = PostReleaseLogDelaySeconds;
			_postReleaseWatchLeft = (int)(PostReleaseWatchSeconds / 0.5f);
			_postReleaseWatchTimer = 0.5f;
		}

		/// <summary>
		/// **换场景 / 收场：状态清零**（<see cref="CameraService.ResetForMission"/> 调）——
		/// 与 <see cref="Stop"/> 的区别 = 这里**不碰相机**（场景已经没了），只把状态清干净，防跨场景残留。
		/// </summary>
		public void Reset()
		{
			_active = false;
			_handingBack = false;
			_lookReturning = false;
			_lookExternal = false;
			_lookMouse = false;
			_offsetsDirty = false;
			_pivotZInit = false;
			_pivotZ = 0f;
			_agent = null;
			_case = null;
			_caseName = "";
			_desc = "";
			_policy = default;
			_target = default;
			_current = default;
			_from = default;
			_handFrom = default;
			_handTo = default;
			_engineAtTakeover = default;
			_blendT = 1f;
			_handT = 1f;
			_useTimeout = false;
			_remain = 0f;
			_postReleaseTimer = -1f;
			_postReleaseWatchLeft = 0;
			_lag.Reset();
			_motion = default;
		}

		// ═════════════════════════════ 引擎互操作 ═════════════════════════════

		/// <summary>
		/// 把我们的朝向**写回引擎**（`CameraBearing` / `CameraElevation`，私有 setter 走反射）。
		/// 角度口径实测对称：引擎 look = `RotateAboutUp(bearing)` 再 `RotateAboutSide(elevation)`，
		/// 而 `Vec3.RotationZ/RotationX` 正好是它的可逆分解 ⇒ 直接对写，无需符号校准。
		/// </summary>
		public static bool WriteBackLookToEngine(float yawDeg, float pitchDeg)
		{
			try
			{
				if (!(ScreenManager.TopScreen is MissionScreen screen))
					return false;
				const System.Reflection.BindingFlags F =
					System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
					| System.Reflection.BindingFlags.NonPublic;
				var bearingSet = typeof(MissionScreen).GetProperty("CameraBearing", F)?.GetSetMethod(true);
				var elevSet = typeof(MissionScreen).GetProperty("CameraElevation", F)?.GetSetMethod(true);
				if (bearingSet == null || elevSet == null)
					return false;
				bearingSet.Invoke(screen, new object[] { yawDeg * DegToRad });
				elevSet.Invoke(screen, new object[] { pitchDeg * DegToRad });
				DebugLogger.Log($"[FollowCam] 朝向已写回引擎：bearing={yawDeg:F0}° elev={pitchDeg:F0}°（撒手零旋转）");
				return true;
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[FollowCam] 写回引擎朝向异常（退回渐变）: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// **撒手前清掉引擎"特殊相机"的冻结修正**（"落地瞬间相机高度台阶"的根治）。
		/// `_cameraSpecial*` 只在 View 装配的 `UpdateCamera` 里更新，我们接管期间整段被跳过 ⇒ 冻在接管那一刻
		/// （开火时的瞄准态）。撒手后引擎恢复的第一帧先带上这份冻值、再按 4/s 平滑抹掉 = 高度台阶（0.09~0.51 m）。
		/// 归零 ⇒ 引擎恢复的第一帧就是纯几何机位 = 我们的机位。
		/// </summary>
		public static void ClearEngineSpecialCameraAdds()
		{
			try
			{
				if (!(ScreenManager.TopScreen is MissionScreen screen))
					return;
				const System.Reflection.BindingFlags F =
					System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
					| System.Reflection.BindingFlags.NonPublic;
				Type t = typeof(MissionScreen);
				var sb = new System.Text.StringBuilder();
				int cleared = 0;
				bool anyNonZero = false;

				string[] floatFields =
				{
					"_cameraSpecialCurrentAddedBearing", "_cameraSpecialTargetAddedBearing",
					"_cameraSpecialCurrentAddedElevation", "_cameraSpecialTargetAddedElevation",
					"_cameraSpecialCurrentDistanceToAdd", "_cameraSpecialTargetDistanceToAdd",
				};
				foreach (string name in floatFields)
				{
					System.Reflection.FieldInfo f = t.GetField(name, F);
					if (f == null || f.FieldType != typeof(float))
						continue;
					float old = (float)f.GetValue(screen);
					if (Math.Abs(old) > 1e-6f)
						anyNonZero = true;
					sb.Append(name.Replace("_cameraSpecial", "")).Append('=').Append(old).Append(' ');
					f.SetValue(screen, 0f);
					cleared++;
				}
				string[] vecFields = { "_cameraSpecialCurrentPositionToAdd", "_cameraSpecialTargetPositionToAdd" };
				foreach (string name in vecFields)
				{
					System.Reflection.FieldInfo f = t.GetField(name, F);
					if (f == null || f.FieldType != typeof(Vec3))
						continue;
					Vec3 old = (Vec3)f.GetValue(screen);
					if (old.LengthSquared > 1e-6f)
						anyNonZero = true;
					sb.Append(name.Replace("_cameraSpecial", "")).Append('=').Append(old).Append(' ');
					f.SetValue(screen, Vec3.Zero);
					cleared++;
				}

				if (anyNonZero)
					DebugLogger.Log($"[FollowCam] 撒手前清掉引擎特殊相机修正（{cleared} 项，**有非零**）：{sb}");
				else if (DebugLogging)
					DebugLogger.Log($"[FollowCam] 撒手前引擎特殊相机修正 = 全零（{cleared} 项，无需清）");
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[FollowCam] 清引擎特殊相机修正异常（忽略，退回旧行为）：{ex.GetType().Name} {ex.Message}");
			}
		}

		/// <summary>
		/// **滑行期间：把方向转向"引擎重置后的值"**。
		///
		/// 🔴 目标不是"引擎此刻的值"，是"引擎**即将**重置成的值"（重置的时机与公式都是确定的，反编译实证：
		/// `Agent.Controller` setter → `Mission.MainAgent = this` → 下一帧 `HandleUserInput`（每帧跑、**不受
		/// CustomCamera 挡板保护**）执行 `CameraBearing = MainAgent.MovementDirectionAsAngle; CameraElevation = 0`）。
		/// ⚠️ 但**只预置俯仰**：预测的 yaw = 角色**移动方向**，拉拽刚开始时它还是上一个走路方向
		/// （实测 253° vs 瞄准 129°，差 124°）—— 预置 yaw = 一开火镜头猛甩。yaw 停在引擎当前值上。
		/// 追不上/预测不了都不怕：撒手与否由"与引擎**实时值**收敛"把关。
		/// 🔴 yaw 必须走最短弧（引擎可能给 −240° 这种等价角）。
		/// </summary>
		private void ChaseEngineLook(ref float yaw, ref float pitch, float dt)
		{
			bool hasLive = CameraLook.TryGetEngineAnglesRaw(out float liveYaw, out float livePitch);

			bool liveMoved = false;
			if (hasLive && !float.IsNaN(_lookLiveYaw0))
			{
				liveMoved = MathF.Abs(Normalize180(liveYaw - _lookLiveYaw0)) > 2f
						 || MathF.Abs(livePitch - _lookLivePitch0) > 2f;
			}

			float targetYaw, targetPitch;
			string src;
			if (_policy.PredictReset && !liveMoved && TryPredictEngineResetLook(out float predYaw, out float predPitch))
			{
				targetYaw = hasLive ? liveYaw : predYaw;
				targetPitch = predPitch;
				src = "预测俯仰(yaw 保持)";
			}
			else if (hasLive)
			{
				targetYaw = liveYaw;
				targetPitch = livePitch;
				src = "引擎实时值";
			}
			else
			{
				return;
			}

			if (DebugLogging
				&& (float.IsNaN(_lastEngineYaw)
					|| MathF.Abs(Normalize180(targetYaw - _lastEngineYaw)) > 1f
					|| MathF.Abs(targetPitch - _lastEnginePitch) > 1f))
			{
				_lastEngineYaw = targetYaw;
				_lastEnginePitch = targetPitch;
				DebugLogger.Log($"[FollowCam] 滑行中：目标（{src}）= {targetYaw:F1}/{targetPitch:F1}°"
					+ (hasLive ? $"，引擎实时 = {liveYaw:F1}/{livePitch:F1}°" : "，引擎实时=读不到")
					+ $"（我们 {yaw:F1}/{pitch:F1}° → 跟随中）");
			}

			// τ **收尾收紧**：越接近撒手跟得越紧（引擎解冻后还会继续转 ~40°/s）。
			float tau = _lookReturnTau;
			if (_handingBack)
				tau = MathF.Max(0.06f, _lookReturnTau * (1f - _handT));
			float k = 1f - (float)Math.Exp(-dt / tau);   // .NET 4.7.2 没有 MathF.Exp
			yaw += Normalize180(targetYaw - yaw) * k;
			pitch += (targetPitch - pitch) * k;
		}

		/// <summary>采样"引擎此刻冻着的那组值"作为基准：之后偏离 &gt;2° 就说明引擎的重置已发生。</summary>
		private void SampleEngineLookBaseline()
		{
			if (CameraLook.TryGetEngineAnglesRaw(out float yaw, out float pitch))
			{
				_lookLiveYaw0 = yaw;
				_lookLivePitch0 = pitch;
			}
			else
			{
				_lookLiveYaw0 = float.NaN;
			}
		}

		/// <summary>**预测"引擎重置后"的相机朝向** —— 照抄引擎自己的初始化公式：
		/// 第三人称 = (`MainAgent.MovementDirectionAsAngle`, 俯仰 0)；第一人称 = `MainAgent.LookDirection` 的两个角。</summary>
		private static bool TryPredictEngineResetLook(out float yawDeg, out float pitchDeg)
		{
			yawDeg = 0f;
			pitchDeg = 0f;
			try
			{
				Mission mission = Mission.Current;
				Agent main = mission?.MainAgent;
				if (mission == null || main == null)
					return false;
				if (mission.CameraIsFirstPerson)
				{
					Vec3 look = main.LookDirection;
					yawDeg = look.RotationZ * RadToDeg;
					pitchDeg = look.RotationX * RadToDeg;
				}
				else
				{
					yawDeg = main.MovementDirectionAsAngle * RadToDeg;
					pitchDeg = 0f;                                        // ← 引擎写死的常量
				}
				return true;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// **把引擎相机此刻的机位抄成弹簧臂参数**（方向 + 臂长 + FOV，世界锚定）。
		/// 🔴 方向换算走 `Vec3.RotationZ` / `RotationX`（**不是** `atan2(y, x)`）—— 角约定差 90°。
		/// </summary>
		public static bool TryBuildEngineCameraParam(Agent agent, out SpringArmCameraParam param)
		{
			param = default;

			Vec3 look = Vec3.Zero;
			try
			{
				Mission mission = Mission.Current;
				if (mission != null)
				{
					MatrixFrame camFrame = mission.GetCameraFrame();
					look = -camFrame.rotation.u;      // 引擎相机帧约定：视线 = −u
				}
			}
			catch { /* 相机帧取不到 → 回落角色朝向 */ }

			if (look.LengthSquared < 0.0001f)
			{
				try { look = agent?.LookDirection ?? Vec3.Zero; } catch { look = Vec3.Zero; }
			}
			if (look.LengthSquared < 0.0001f)
				return false;

			if (!TryGetEngineCameraDistanceFov(out float dist, out float fov))
			{
				dist = 4f;                            // 兜底：引擎值取不到时的常用第三人称视距
				fov = 65f;
			}

			param = new SpringArmCameraParam
			{
				ArmLength = dist,
				ArmYaw = look.RotationZ * RadToDeg,
				// 🔴 **别收窄这个 pitch 钳位**（2026-10-03 实机教训）：原来 [−75,45]，
				//    而钩索常常要仰头瞄屋顶/崖顶（pitch 超出 45）⇒ 抓到的机位与引擎实际角度系统性对不上，
				//    归还渐变时镜头就得转回那个差值。钳到 ±85 只防"臂翻转"。
				ArmPitch = MBMath.ClampFloat(look.RotationX * RadToDeg, -85f, 85f),
				Fov = fov,
				IsAnchorWorld = true,                 // ← 方向冻在世界里（不跟角色转身）
				UseEngineEyeHeight = true,            // ← 锚点高度用引擎自己的公式（撒手那一刻两边同高）
			};
			return true;
		}

		/// <summary>引擎相机此刻的视距 / FOV（取不到或明显不合理 → false）。</summary>
		public static bool TryGetEngineCameraDistanceFov(out float distance, out float fov)
		{
			distance = 0f;
			fov = 0f;
			try
			{
				MissionScreen screen = ScreenManager.TopScreen as MissionScreen;
				if (screen == null)
					return false;

				float d = screen.CameraResultDistanceToTarget;
				float f = screen.CameraViewAngle;
				if (d <= 0.5f || d >= 30f || f <= 20f || f >= 130f)
					return false;

				distance = MBMath.ClampFloat(d, 1f, 15f);
				fov = f;
				return true;
			}
			catch
			{
				return false;
			}
		}

		private static float Normalize180(float deg)
		{
			while (deg > 180f) deg -= 360f;
			while (deg <= -180f) deg += 360f;
			return deg;
		}

		/// <summary>我们与引擎方向的当前最大差（度；读不到引擎角度 = 0 = 视为已收敛，不拦撒手）。</summary>
		private static float EngineLookDeltaDeg(in SpringArmCameraParam p)
		{
			if (!CameraLook.TryGetEngineAnglesRaw(out float engYaw, out float engPitch))
				return 0f;
			float dy = MathF.Abs(Normalize180(engYaw - p.ArmYaw));
			float dp = MathF.Abs(engPitch - p.ArmPitch);
			return MathF.Max(dy, dp);
		}

		private static bool EngineLookConverged(in SpringArmCameraParam p)
			=> EngineLookDeltaDeg(p) <= HandBackLookConvergedDeg;

		/// <summary>日志片段：引擎**原始**朝向（未钳位、未换算）—— 与"我们照抄的那份"并排，钳位差异一眼可见。</summary>
		private static string EngineLookSuffix()
		{
			return CameraLook.TryGetEngineAnglesRaw(out float yaw, out float pitch)
				? $"| 引擎原始 yaw={yaw:F0}° pitch={pitch:F0}° "
				: "| 引擎原始角度=读不到 ";
		}

		/// <summary>起飞前引擎机位（接管**前**采样，和接管后的每帧行并排 = "那一瞬间高度跳没跳"）。</summary>
		private static string PreTakeoverSuffix()
		{
			try
			{
				Mission mission = Mission.Current;
				if (mission == null)
					return string.Empty;
				Vec3 pos = mission.GetCameraFrame().origin;
				return $"| 起飞前引擎机位：相机=({pos.x:F2},{pos.y:F2},{pos.z:F2}) "
					 + $"Δ眼={FmtCameraVsEye(pos, mission.MainAgent, true)}m ";
			}
			catch
			{
				return string.Empty;
			}
		}

		// ═════════════════════════════ 诊断 ═════════════════════════════

		/// <summary>相机相对**角色眼睛**的高度差（米，两位；&gt;0 = 相机在眼睛之上）。
		/// 眼睛 = 字面眼高（`monster.StandingEyeHeight × 缩放`）—— 引擎锚点比它高 0.2 米，所以稳态俯仰 0 时 ≈ +0.2。</summary>
		public static string FmtCameraVsEye(Vec3 camPos, Agent agent, bool engineFormula)
		{
			try
			{
				if (agent == null)
					return "?";
				float eye = engineFormula
					? SpringArmMath.ResolveLiteralEyeHeight(agent)
					: SpringArmMath.ResolveEyeHeightOffset(agent, false);
				return (camPos.z - (agent.Position.z + eye)).ToString("F2");
			}
			catch
			{
				return "?";
			}
		}

		/// <summary>**交班行**（撒手那一刻）：我们最后一帧 vs 引擎此刻，四项并排 —— 数字越接近 = 撒手越无缝。</summary>
		private void LogHandOff(in SpringArmCameraParam ours)
		{
			bool hasDist = TryGetEngineCameraDistanceFov(out float dist, out float fov);
			bool hasLook = CameraLook.TryGetEngineAnglesRaw(out float yaw, out float pitch);
			Vec3 ourPos = _lastCam;     // 别读 GetCameraFrame()：我们持有期间它冻在接管前的机位
			Agent mainAgent = Mission.Current?.MainAgent;
			DebugLogger.Log("[FollowCam] 交班（撒手那一刻）我们："
				+ $"相机=({ourPos.x:F2},{ourPos.y:F2},{ourPos.z:F2}) "
				+ $"Δ眼={FmtCameraVsEye(ourPos, mainAgent, ours.UseEngineEyeHeight)}m "
				+ $"臂长 {ours.ArmLength:F2} 俯仰 {ours.ArmPitch:F1}° yaw {ours.ArmYaw:F1}° fov {ours.Fov:F1} "
				+ "| 引擎："
				+ (hasDist ? $"臂长 {dist:F2} fov {fov:F1} " : "臂长=? fov=? ")
				+ (hasLook ? $"俯仰 {pitch:F1}° yaw {yaw:F1}°" : "俯仰=? yaw=?"));
		}

		/// <summary>
		/// 跟随期间的日志（**每帧一行**，受 `custom.cam log 1` 管）—— 排查"镜头跟没跟"的现场证据。
		/// `回读Δ` = 写完再从**实体**读回来的位置差：≈0 = 实体确实收下了（引擎读的是同一份）。
		/// </summary>
		private void LogFollowTick(Agent agent, in MatrixFrame frame, SpringArmCameraView view)
		{
			_elapsed += _lastDt;

			Vec3 a = Vec3.Zero;
			try { a = agent.Position; } catch { /* 取不到就留零 */ }

			float anchorMoved = _hasLast ? a.Distance(_lastAnchor) : 0f;
			float camMoved = _hasLast ? frame.origin.Distance(_lastCam) : 0f;
			_lastAnchor = a;
			_lastCam = frame.origin;
			_hasLast = true;

			if (!DebugLogging)
				return;

			float readBack = -1f;
			try { readBack = view.CamEntity.GetGlobalFrame().origin.Distance(frame.origin); }
			catch { readBack = -1f; }

			DebugLogger.Log($"[FollowCam] t={_elapsed:F2} "
							+ $"锚=({a.x:F2},{a.y:F2},{a.z:F2})Δ{anchorMoved:F3} "
							+ $"相机=({frame.origin.x:F2},{frame.origin.y:F2},{frame.origin.z:F2})Δ{camMoved:F3} "
							+ $"Δ眼={FmtCameraVsEye(frame.origin, agent, _current.UseEngineEyeHeight)}m "
							+ $"回读Δ={(readBack < 0f ? "ERR" : readBack.ToString("F4"))} "
							+ $"yaw={_current.ArmYaw:F1} pitch={_current.ArmPitch:F1} "
							+ $"臂长={_current.ArmLength:F2} fov={_current.Fov:F0} "
							+ $"lag={(_lag.Offset.LengthSquared > 1e-6f ? _lag.Offset.Length.ToString("F2") : "0")}");
		}

		/// <summary>撒手后的采样（第 1 帧 + 0.5 秒 + 之后每 0.5 秒共 3 秒，**仅 DebugLogging**）——
		/// 把"引擎相机撒手后到底停在哪、有没有在动"记录成时间序列。</summary>
		private void TickPostRelease(float dt)
		{
			if (!DebugLogging)
				return;
			if (_postReleaseFirst)
			{
				_postReleaseFirst = false;
				CameraService.LogEngineCamera("撒手后第1帧");
			}
			if (_postReleaseTimer >= 0f)
			{
				_postReleaseTimer -= dt;
				if (_postReleaseTimer <= 0f)
				{
					_postReleaseTimer = -1f;
					CameraService.LogEngineCamera("交班后稳定值");
				}
			}
			if (_postReleaseWatchLeft > 0)
			{
				_postReleaseWatchTimer -= dt;
				if (_postReleaseWatchTimer <= 0f)
				{
					_postReleaseWatchTimer = 0.5f;
					_postReleaseWatchLeft--;
					CameraService.LogEngineCamera($"撒手后+{(PostReleaseWatchSeconds - _postReleaseWatchLeft * 0.5f):F1}s");
				}
			}
		}

		/// <summary>一行报告**我们的跟随相机**现状（`custom.cam info`）。</summary>
		public string DumpState(string tag)
		{
			try
			{
				Agent agent = _agent ?? Mission.Current?.MainAgent;
				string eye = "?";
				string lift = "?";
				if (agent != null)
				{
					eye = SpringArmMath.ResolveEyeHeightOffset(agent, _current.UseEngineEyeHeight).ToString("F2");
					lift = SpringArmMath.ComputeEngineLift(agent, _current.ArmLength).ToString("F3");
				}
				string line = $"[FollowCam] {tag}：在跟={(_active ? 1 : 0)} 归还中={(_handingBack ? 1 : 0)} "
					+ $"方向归还中={(_lookReturning ? 1 : 0)} case={_caseName} 臂长={_current.ArmLength:F2} "
					+ $"俯仰={_current.ArmPitch:F1}° yaw={_current.ArmYaw:F1}° fov={_current.Fov:F0} "
					+ $"眼高={eye} 引擎抬升项={lift} 手动 lift={CameraLiftMeters:F2} "
					+ $"鼠标驱动={(_lookMouse ? 1 : 0)} 外部喂={(_lookExternal ? 1 : 0)} 日志开关={(DebugLogging ? 1 : 0)}";
				DebugLogger.Log(line);
				return line;
			}
			catch (Exception ex)
			{
				return $"Error: {ex.GetType().Name} {ex.Message}";
			}
		}
	}
}
