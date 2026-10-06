using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **过渡期接口（阶段 4 合并完成后退役）**：还没接进服务的"外部持有者"（当前唯一实现 = 飞行相机
	/// <c>Flight/FlightCameraRig</c>）向服务登记，服务就能：
	/// ① 在 <see cref="CameraService.ApplyPose"/> 抢占时叫它**立刻放手**；② 把它的"看"转手给
	/// <see cref="CameraLook"/>（这样 `CameraLook.Provider` 永远只有服务一个写者）。
	/// </summary>
	public interface ICameraExternalHolder
	{
		/// <summary>被抢占：**立刻放手**（不渐变）—— 由服务在抢占路径上调用。</summary>
		void OnCameraPreempted();

		/// <summary>把"相机看向哪"给出去（没接管 = false）。</summary>
		bool TryGetLook(out Vec3 forward);
	}

	/// <summary>
	/// **相机服务 —— 全项目唯一入口**（2026-10-05 立，方案 = plans/自定义相机整合-实施方案.md）。
	///
	/// | 维度 | 规则 |
	/// |---|---|
	/// | 数据 | 一切机位 = `ModuleData/DesignData/Camera.csv` 的一行（<see cref="CameraCase"/>） |
	/// | 谁摆相机 | **只有本类**写 `MissionScreen.CustomCamera` 与 `CameraLook.Provider`（机器 = <see cref="SpringArmRig"/>） |
	/// | 持有者 | 每次接管带一个 **owner 标签**（`"flight"` / `"grapple"` / `"perf:exec_pair"` / `"pose"`） |
	///
	/// **仲裁规则**（2026-10-05 用户审定的口径）：
	/// · 常规起播（<see cref="Play"/> / <see cref="PlayEnginePose"/> / <see cref="Adopt"/>）**被占用 = 拒绝** + 记日志；
	/// · 一次性机位 <see cref="ApplyPose"/> = **抢占**（先停旧持有者再摆机位）——
	///   演出/对话是"剧情要用的镜头"，优先级高于在跑的自定义机位；
	/// · 静态机位（pose）**可以被新 Play 顶掉**（它本来就是个一次算好的取景，不是"在跟"）；
	/// · 只有持有者能置 `CustomCamera = null`（<see cref="Stop"/> 内部也因此先比对象再撒手）。
	///
	/// 用法：业务侧只写"**用哪一行 case**"——`CameraService.Play("grapple_shot", agent, 5f, "grapple")`。
	/// </summary>
	public static class CameraService
	{
		// ═════════════════════════════ 状态 ═════════════════════════════

		/// <summary>相机机器（唯一一份）。</summary>
		private static readonly SpringArmRig Rig = new SpringArmRig();

		/// <summary>当前持有者标签（null = 没人持有 = 引擎相机）。</summary>
		private static string _holder;

		/// <summary>当前持有的是不是"一次性静态机位"（可以被新 Play 顶掉）。</summary>
		private static bool _holderIsPose;

		/// <summary>外部持有者（过渡期 = 飞行；它自己摆相机，但"谁持有"由服务记账）。</summary>
		private static ICameraExternalHolder _external;

		private static readonly RigLookProvider LookProviderInstance = new RigLookProvider();
		private static bool _lookProviderRegistered;

		// ═════════════════════════════ 查询 ═════════════════════════════

		/// <summary>相机是否被自定义机位持有（false = 引擎相机在跑）。</summary>
		public static bool IsHeld => _holder != null;

		/// <summary>当前持有者标签（null = 没人）。</summary>
		public static string Holder => _holder;

		/// <summary>当前 case 名（诊断/回显；没接管 = 空）。</summary>
		public static string CurrentCase => _holder == null ? "" : Rig.CaseName;

		/// <summary>是不是某个 owner 在持有。</summary>
		public static bool IsHeldBy(string owner) => _holder != null && _holder == owner;

		/// <summary>相机机器的只读视图（诊断台用）。</summary>
		public static SpringArmRig Machine => Rig;

		/// <summary>
		/// **相机前向 / 右向**（"看向哪"的消费者用它：飞行方向、施法方向、身体朝向…）。
		/// 优先级 = 外部持有者（飞行）→ 内部机器；都没接管 = false（调用方回退，别猜）。
		/// </summary>
		public static bool TryGetBasis(out Vec3 forward, out Vec3 right)
		{
			forward = Vec3.Zero;
			right = Vec3.Zero;

			ICameraExternalHolder ext = _external;
			if (ext != null)
			{
				try
				{
					if (ext.TryGetLook(out forward) && forward.LengthSquared > 1e-6f)
					{
						// 右向 = 水平面内的右手侧（口径同 `Mat3.RotateAboutUp`：f=(−sin a, cos a, 0) ⇒ s=(−f.y, f.x, 0)）
						Vec3 flat = new Vec3(-forward.y, forward.x, 0f);
						right = flat.LengthSquared > 1e-6f ? flat.NormalizedCopy() : Vec3.Zero;
						return true;
					}
				}
				catch { /* 外部这一帧拿不到 —— 往下走 */ }
			}
			return Rig.TryGetBasis(out forward, out right);
		}

		// ═════════════════════════════ 起播 ═════════════════════════════

		/// <summary>起播一个机位 case（无特殊归还策略）。被占用 → 拒绝（返回 false + 日志）。</summary>
		public static bool Play(string caseName, Agent agent, float seconds, string owner)
			=> Play(caseName, agent, seconds, owner, default);

		/// <summary>
		/// **起播一个机位 case**（方向/臂长的来源由 case 的 `Seed` 列决定，见 <see cref="SpringArmRig.Start"/>）。
		/// <paramref name="owner"/> = 持有者标签。**已被别人持有 → 拒绝**（静态机位除外，它会被顶掉）。
		/// </summary>
		public static bool Play(string caseName, Agent agent, float seconds, string owner, CameraReturnPolicy policy)
		{
			if (!CameraCase.TryGet(caseName, out CameraCase kase))
			{
				DebugLogger.Log($"[CamSvc] 起播失败：Camera.csv 里没有 case '{caseName}'（不做代码兜底）");
				return false;
			}
			if (!CanTake(owner, out string why))
			{
				DebugLogger.Log($"[CamSvc] 拒绝起播 case={caseName} owner={owner}：{why}");
				return false;
			}
			if (!Rig.Start(kase, agent, seconds, policy, owner))
			{
				DebugLogger.Log($"[CamSvc] 起播失败：case={caseName} owner={owner}（引擎机位读不到 / 没有 mission screen）");
				return false;
			}
			_holder = owner;
			_holderIsPose = false;
			_external = null;
			EnsureLookProvider();
			return true;
		}

		/// <summary>
		/// **用"引擎此刻的机位"起播**（= 原 `ApplyFollowFromEngineCamera`）——
		/// 方向逐度照抄引擎相机、只跟位置，是**表演镜头的默认做法**（"视角还是你原来的视角，只是跟着人平移"）。
		/// 等价于 `Play("follow_engine", …)`；留成本方法是为了调用点读起来直白。
		/// </summary>
		public static bool PlayEnginePose(Agent agent, float seconds, string owner, CameraReturnPolicy policy = default)
			=> Play("follow_engine", agent, seconds, owner, policy);

		/// <summary>
		/// **换阶段**（同一位持有者继续用，**不重播种**）—— 钩索"瞄准 → 拉拽"走这条。
		/// 只叠加"看得见的量"（臂长 / FOV / 鼠标接管 / 归还策略 / 时长），方向与其余偏移保持现状。
		/// </summary>
		public static bool Adopt(string caseName, string owner, in CameraStage stage)
		{
			if (_holder != owner)
			{
				DebugLogger.Log($"[CamSvc] 换阶段失败：case={caseName} owner={owner} 但当前持有者={_holder ?? "(空)"}");
				return false;
			}
			if (!CameraCase.TryGet(caseName, out CameraCase kase))
			{
				DebugLogger.Log($"[CamSvc] 换阶段失败：Camera.csv 里没有 case '{caseName}'");
				return false;
			}
			return Rig.Adopt(kase, in stage);
		}

		/// <summary>**同持有者换 case**（飞行机位切换）：臂长/FOV/Socket 从当前值渐变到新 case。</summary>
		public static bool Switch(string caseName, float blendSeconds)
		{
			if (_holder == null || _holderIsPose)
				return false;
			if (!CameraCase.TryGet(caseName, out CameraCase kase))
			{
				DebugLogger.Log($"[CamSvc] 换 case 失败：Camera.csv 里没有 case '{caseName}'");
				return false;
			}
			return Rig.Switch(kase, blendSeconds);
		}

		/// <summary>
		/// **一次性静态机位**（演出/对话取景）—— = 原 `SpringArmCameraView.UseCameraTemlate`，签名语义不变。
		/// 🔴 **抢占**：先停掉在跑的持有者（内部机器 → 立刻还；外部持有者 → 叫它放手）再摆机位。
		/// 摆完的机位是"静态"的（角色一动就出画），**会被新的 Play 顶掉**。
		/// </summary>
		public static bool ApplyPose(string caseName, Agent speaker, Agent listener, Vec3 anchorWorldPos)
		{
			if (!CameraCase.TryGet(caseName, out CameraCase kase))
			{
				DebugLogger.Log($"[CamSvc] 摆机位失败：Camera.csv 里没有 case '{caseName}'");
				return false;
			}
			if (_holder != null)
			{
				DebugLogger.Log($"[CamSvc] 一次性机位抢占：case={caseName} 顶掉当前持有者 {_holder}");
				Stop();
			}
			if (!Rig.ApplyStaticPose(kase, speaker, listener, anchorWorldPos))
				return false;

			_holder = "pose";
			_holderIsPose = true;
			_external = null;
			EnsureLookProvider();
			return true;
		}

		/// <summary>一次性机位（**强制锚到指定角色**）—— `custom.useSpringArmCamera &lt;模板&gt; &lt;agentId&gt;` 用。
		/// 同样走抢占。</summary>
		public static bool ApplyPoseTo(string caseName, Agent target)
		{
			if (!CameraCase.TryGet(caseName, out CameraCase kase))
			{
				DebugLogger.Log($"[CamSvc] 摆机位失败：Camera.csv 里没有 case '{caseName}'");
				return false;
			}
			if (_holder != null)
			{
				DebugLogger.Log($"[CamSvc] 一次性机位抢占：case={caseName} 顶掉当前持有者 {_holder}");
				Stop();
			}
			if (!Rig.ApplyStaticFrame(target, in kase.Param, kase.Id))
				return false;

			_holder = "pose";
			_holderIsPose = true;
			_external = null;
			EnsureLookProvider();
			return true;
		}

		/// <summary>
		/// **调试滑杆 UI 专用**：每帧喂一组参数（第一次喂时抢占持有权）。业务代码**不要**用它 ——
		/// 它绕过 case（参数直接来自滑杆），只服务 `custom.openSpringArmCamDebugger`。
		/// </summary>
		public static bool SetLivePose(string owner, Agent target, in SpringArmCameraParam p)
		{
			if (_holder != owner)
			{
				if (_holder != null)
				{
					DebugLogger.Log($"[CamSvc] 调试机位抢占：顶掉当前持有者 {_holder}");
					Stop();
				}
				_holder = owner;
				_holderIsPose = true;
				_external = null;
				EnsureLookProvider();
			}
			return Rig.ApplyStaticFrame(target, in p, owner);
		}

		// ═════════════════════════════ 现场改（仅当前持有者） ═════════════════════════════

		/// <summary>改臂长（米）。</summary>
		public static void SetArmLength(float meters) => Rig.SetArmLength(meters);

		/// <summary>改"环绕点"偏移 Z（米；角色系）。</summary>
		public static void SetPivotZ(float meters) => Rig.SetPivotZ(meters);

		/// <summary>改"相机"偏移 Z（米；相机系）。</summary>
		public static void SetSocketZ(float meters) => Rig.SetSocketZ(meters);

		/// <summary>外部朝向驱动（每帧调；停止用 <see cref="ReleaseLook"/>）。</summary>
		public static void SetLook(float yawDeg, float pitchDeg) => Rig.SetLook(yawDeg, pitchDeg);

		/// <summary>停用外部朝向驱动（方向回到 case 的鼠标驱动 / 渐变 / 归还 chase）。</summary>
		public static void ReleaseLook() => Rig.ReleaseLook();

		/// <summary>喂本帧运动量（飞行每帧调；不喂 = 全零 = 无影响）。</summary>
		public static void SetMotion(in SpringArmMotion motion) => Rig.SetMotion(in motion);

		/// <summary>改超时（秒；≤0 = 不限时）。</summary>
		public static void SetTimeout(float seconds) => Rig.SetTimeout(seconds);

		/// <summary>改归还策略三件套。</summary>
		public static void SetReturnPolicy(in CameraReturnPolicy policy) => Rig.SetReturnPolicy(in policy);

		/// <summary>
		/// **表行被热改了**（`custom.cam set` 改完调一下）—— 改的若正是当前在用的那一行，
		/// 立刻同步进 live 目标（"实时可见"）；不是当前那行 = 空操作（下次接管自然读到新值）。
		/// </summary>
		public static void NotifyCaseEdited(CameraCase kase) => Rig.RefreshFromCase(kase);

		// ═════════════════════════════ 归还 / 收场 ═════════════════════════════

		/// <summary>**只启动"方向归还"**（臂长/FOV 仍按自己的时机）—— 钩索从拉拽一开始就调它。</summary>
		public static void BeginLookReturn(float seconds) => Rig.BeginLookReturn(seconds);

		/// <summary>**请求渐变归还**（臂长/FOV 滑回接管时引擎相机的值，滑完自动撒手）。</summary>
		public static void RequestHandBack(float glideSeconds = 0f) => Rig.RequestHandBack(glideSeconds);

		/// <summary>
		/// **立刻归还**（幂等）：内部机器 → 撒手 + 释放；外部持有者（飞行）→ 叫它放手。
		/// 这是唯一会把 `CustomCamera` 置 null 的地方（且只置"还是我们那台"的情况）。
		/// </summary>
		public static void Stop()
		{
			ICameraExternalHolder ext = _external;
			if (ext != null)
			{
				_holder = null;
				_external = null;
				DebugLogger.Log("[CamSvc] 请外部持有者放手（被抢占 / 收场）");
				try { ext.OnCameraPreempted(); }
				catch (Exception ex) { DebugLogger.Log($"[CamSvc] 外部持有者放手异常：{ex.GetType().Name} {ex.Message}"); }
				return;
			}

			if (_holder == null)
				return;

			_holder = null;
			_holderIsPose = false;
			Rig.Stop();
			ReleasePresentedCamera();
			// 🔴 **不要清 `CameraLook` 登记**：那是个"路由器"（按谁在管相机分发），不是持有权 ——
			//    清掉它就得有人再注册回来，而注册是一次性的 ⇒ 飞行接管后施法方向会拿不到视线（真踩过）。
			//    没接管时它返回 false，`CameraLook.TryGet` 自己回落到引擎分支 ✓。
		}

		/// <summary>相机是否还在归还渐变中（诊断 / 调用方决定还要不要喂）。</summary>
		public static bool IsReturning => Rig.HandingBack || Rig.LookReturning;

		/// <summary>
		/// **只收"一次性机位"**（对话/剧情演完时调）：当前持有的是 pose 才放手，**别的持有者不碰** ——
		/// 老写法（直接 `CustomCamera = null`）会把正在飞的相机一起踩掉。
		/// </summary>
		public static void StopPose()
		{
			if (_holder != null && _holderIsPose)
				Stop();
		}

		/// <summary>换场景 / 收场：把状态清干净（<see cref="SpringArmCameraView"/> 生命周期里调）。</summary>
		public static void ResetForMission()
		{
			_holder = null;
			_holderIsPose = false;
			_external = null;
			Rig.Reset();
			// `CameraLook` 的登记是路由器（见 Stop 的注释）—— 这里也不清，它自己会按"没接管"回落引擎分支。
		}

		// ═════════════════════════════ 每帧 ═════════════════════════════

		/// <summary>每帧推进（由 <see cref="SpringArmCameraView.OnMissionTick"/> 无条件调用）。</summary>
		public static void Tick(float dt)
		{
			EnsureLookProvider();
			Rig.Tick(dt);
		}

		// ═════════════════════════════ 外部持有者（过渡期：飞行） ═════════════════════════════

		/// <summary>
		/// 外部持有者登记（飞行接管相机时调）：从此"谁持有"由服务记账 —— 别人 Play 会被拒绝，
		/// 演出的 ApplyPose 会叫它放手。**它自己摆相机**（阶段 4 合并后这条会退役）。
		/// </summary>
		public static void TakeExternal(string owner, ICameraExternalHolder holder)
		{
			_holder = owner;
			_holderIsPose = false;
			_external = holder;
			EnsureLookProvider();
		}

		/// <summary>外部持有者退场（飞行归还相机时调；幂等）。</summary>
		public static void ReleaseExternal(string owner)
		{
			if (_external != null && _holder == owner)
			{
				_external = null;
				_holder = null;
			}
		}

		// ═════════════════════════════ 呈现（唯一写 CustomCamera 的地方） ═════════════════════════════

		/// <summary>把一台相机交给引擎（**全项目唯一写 `CustomCamera` 的地方**）。</summary>
		public static void Present(Camera camera)
		{
			if (camera == null)
				return;
			try
			{
				if (ScreenManager.TopScreen is MissionScreen screen)
					screen.CustomCamera = camera;
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[CamSvc] 呈现相机异常：{ex.GetType().Name} {ex.Message}");
			}
		}

		/// <summary>撒手：**只有引擎手里还是这台相机时**才置 null（防"踩掉别人"）。</summary>
		public static void ReleasePresentedIf(Camera camera)
		{
			try
			{
				if (ScreenManager.TopScreen is MissionScreen screen
					&& camera != null && ReferenceEquals(screen.CustomCamera, camera))
				{
					screen.CustomCamera = null;
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[CamSvc] 撒手相机异常（重进场景可恢复）：{ex.GetType().Name} {ex.Message}");
			}
		}

		/// <summary>撒手我们这台（= 视图里那台相机）。</summary>
		private static void ReleasePresentedCamera()
		{
			try
			{
				SpringArmCameraView view = Mission.Current?.GetMissionBehavior<SpringArmCameraView>();
				if (view != null)
					ReleasePresentedIf(view.Camera);
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[CamSvc] 撒手相机异常（重进场景可恢复）：{ex.GetType().Name} {ex.Message}");
			}
			DebugLogger.Log("[CamSvc] 已归还相机");
		}

		// ═════════════════════════════ 仲裁 ═════════════════════════════

		private static bool CanTake(string owner, out string why)
		{
			why = null;
			if (_holder == null || _holder == owner)
				return true;
			if (_holderIsPose)
				return true;                       // 静态机位可被顶掉
			why = $"相机已被 {_holder} 持有（只有它自己能换/还）";
			return false;
		}

		// ═════════════════════════════ "看"的提供者 ═════════════════════════════

		private const string LookOwner = "camera";

		/// <summary>`CameraLook` 的唯一写者（注册一次，永久有效）—— 按"谁在管相机"分发：
		/// 外部持有者（飞行）→ 问它；内部机器接管中 → 问机器；都没接管 → 交给引擎分支。</summary>
		private sealed class RigLookProvider : ICameraLookProvider
		{
			public bool TryGetLook(out Vec3 forward)
			{
				forward = Vec3.Zero;
				ICameraExternalHolder ext = _external;
				if (ext != null)
				{
					try { return ext.TryGetLook(out forward) && forward.LengthSquared > 1e-6f; }
					catch { return false; }
				}
				return Rig.TryGetBasis(out forward, out _);
			}
		}

		private static void EnsureLookProvider()
		{
			if (_lookProviderRegistered)
				return;
			CameraLook.Set(LookProviderInstance, LookOwner);
			_lookProviderRegistered = true;
		}

		// ═════════════════════════════ 引擎互操作 / 诊断 ═════════════════════════════

		/// <summary>控制台诊断入口（`custom.cam stat`）：把**引擎默认相机**此刻的机位/Δ眼/角度打一行到日志。</summary>
		public static void LogEngineCameraNow(string tag) => LogEngineCamera(tag);

		/// <summary>控制台诊断入口（`custom.cam info`）：一行报告我们的相机现状。</summary>
		public static string DumpCameraNow(string tag) => Rig.DumpState(tag);

		/// <summary>引擎默认相机此刻的四个数 + 世界位置（日志用；取不到的项打 `?`）。</summary>
		public static void LogEngineCamera(string tag)
		{
			bool hasDist = SpringArmRig.TryGetEngineCameraDistanceFov(out float dist, out float fov);
			bool hasLook = CameraLook.TryGetEngineAnglesRaw(out float yaw, out float pitch);
			Vec3 pos = Vec3.Zero;
			float viewPitchDeg = float.NaN;
			try
			{
				Mission m = Mission.Current;
				if (m != null)
				{
					MatrixFrame camFrame = m.GetCameraFrame();
					pos = camFrame.origin;
					viewPitchDeg = (-camFrame.rotation.u).RotationX * (180f / MathF.PI);
				}
			}
			catch { /* 取不到就留零 */ }

			Vec3 playerPos = Vec3.Zero;
			float playerDist = -1f;
			Agent main = null;
			try
			{
				main = Mission.Current?.MainAgent;
				if (main != null)
				{
					playerPos = main.Position;
					playerDist = pos.Distance(playerPos);
				}
			}
			catch { /* 忽略 */ }

			DebugLogger.Log($"[FollowCam] {tag}（引擎默认相机）："
				+ (hasDist ? $"臂长 {dist:F2} fov {fov:F1} " : "臂长=? fov=? ")
				+ (hasLook ? $"俯仰 {pitch:F1}° yaw {yaw:F1}° " : "俯仰=? yaw=? ")
				+ $"相机=({pos.x:F2},{pos.y:F2},{pos.z:F2}) Δ眼={SpringArmRig.FmtCameraVsEye(pos, main, true)}m"
				+ (float.IsNaN(viewPitchDeg) ? "" : $" 视角俯仰={viewPitchDeg:F1}°")
				+ (main != null ? $" 骑马={(main.HasMount ? 1 : 0)}" : "")
				+ $" 瞄准修正={(BannerlordConfig.EnableVerticalAimCorrection ? 1 : 0)}"
				+ " " + EngineInternalsSuffix(main)
				+ (playerDist >= 0f ? $" 玩家=({playerPos.x:F1},{playerPos.y:F1},{playerPos.z:F1}) 距={playerDist:F1}m" : ""));
		}

		/// <summary>引擎内部状态快照（诊断）：附加仰角 / 锚点平滑高度 / 角色"视觉位置 vs 逻辑位置"的差。</summary>
		private static string EngineInternalsSuffix(Agent agent)
		{
			float addedElevRad = ReadScreenFloatField("_cameraAddedElevation");
			float anchorH = ReadScreenFloatField("_cameraTargetAddedHeight");
			string visual = "?";
			try
			{
				if (agent != null)
					visual = (agent.VisualPosition.z - agent.Position.z).ToString("F2");
			}
			catch { }
			return "| 引擎内参[附加仰角="
				+ (float.IsNaN(addedElevRad) ? "?" : (addedElevRad * (180f / MathF.PI)).ToString("F1") + "°")
				+ " 锚高=" + (float.IsNaN(anchorH) ? "?" : anchorH.ToString("F2"))
				+ " 视觉Δz=" + visual + "] ";
		}

		/// <summary>读引擎 `MissionScreen` 的私有 float 字段（诊断用；读不到返回 NaN）。</summary>
		private static float ReadScreenFloatField(string name)
		{
			try
			{
				if (!(ScreenManager.TopScreen is MissionScreen screen))
					return float.NaN;
				System.Reflection.FieldInfo f = typeof(MissionScreen).GetField(name,
					System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
					| System.Reflection.BindingFlags.NonPublic);
				return f != null && f.FieldType == typeof(float) ? (float)f.GetValue(screen) : float.NaN;
			}
			catch
			{
				return float.NaN;
			}
		}
	}
}
