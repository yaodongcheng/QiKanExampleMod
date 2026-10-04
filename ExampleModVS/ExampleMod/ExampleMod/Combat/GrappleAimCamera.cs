using System;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **钩索相机**（2026-10-04 立；2026-10-05 改口径）—— **开火之后**接管相机（弹簧臂系统，`SpringArmCameraView`），
	/// 覆盖"钩头在飞 / 蓄势 / 拉拽"整段；拉拽开始时由 `GrapplePull` 就地收编继续用。
	///
	/// 🔴 **为什么是"开火之后"而不是"瞄准期间"**（2026-10-05 用户实机裁定）：
	///    一开始做成"按住左键瞄准就接管"，实机撞出两个硬伤 —— ① 我们一接管，引擎的**准星/整套瞄准表现全没了**；
	///    ② 开火取方向那条链（`CameraLook`）在开火瞬间出问题（实测两次 `no camera look this frame.`，钩头没发出去）。
	///    ⇒ 瞄准阶段相机**完全归引擎**（准星/开火全原生），**开火之后**（钩索流程开始跑）才接入。
	///    代价 = 瞄准期间的蹲姿取景回到引擎口径（原来的动机），用拉拽窗口这段的取景补回来。
	///    （瞄准期的取景若还想调，用 `aimcam lock / test` 手动接管看 —— 那是调试工具。）
	///
	/// 🔴 **接管相机 = 接管"看"**（铁律：引擎在 `CustomCamera != null` 时**完全不处理鼠标 look**）——
	///    所以本类必须自己喂鼠标：做法与飞行相机 `FlightCameraRig.ApplyLook` 同源
	///    （`Input.MouseMoveX/Y` × `Input.MouseSensitivity`，跟随玩家设置里的灵敏度）。
	///
	/// **生命周期**（每帧由 `GrappleLogic.OnMissionTick` 调 <see cref="Tick"/>）：
	///   · 进：`GrappleLogic.IsBusy`（开火后：钩头在飞 / 已挂住 / 拉拽中）⇒ 接管
	///     （方向 = 接管那一刻的引擎机位 ⇒ **不硬切**；立即改为鼠标驱动）。
	///   · 出：
	///       ① **拉拽开始** ⇒ `GrapplePull.EnterCamera` 调 <see cref="OnPullAdopt"/> **就地收编**
	///          （跟随不断、只换臂长与归还三件套 —— **不能重新快照引擎机位**：接管期间引擎相机是冻的，重抄 = 跳）。
	///       ② **打空 / 收钩**（流程回到 Idle）⇒ 渐变交还引擎。
	///       ③ 被别的系统顶掉跟随（演出等）⇒ 立即让位。
	///
	/// 🔴 **相机朝向同步给 `CameraLook`**：接管期间注册为 <see cref="ICameraLookProvider"/>（这一段里
	///    任何"要看哪"的调用方拿到的是我们镜头的朝向）。
	///
	/// 验收命令：`custom.grapple aimcam <off|on|lock|unlock|test|臂长米>` · `aimanchor` · `aimlift <米>` · `aimsens`。
	/// </summary>
	public static class GrappleAimCamera
	{
		// ── 旋钮（`custom.grapple` 命令改，即时生效）──

		/// <summary>总开关（默认开）。`aimcam off` 关掉 = 瞄准期间用引擎原相机。</summary>
		public static bool Enabled = true;

		/// <summary>**调试锁定**（`aimcam lock`）：不按左键也让相机接管 —— 这样调参时能边看边敲命令
		/// （按住左键根本没法开控制台；而松手就开火）。锁定期间鼠标照常转视角；拉拽接管相机时本相机先让开，
		/// 拉完自动回到锁定。`aimcam unlock` / `aimcam off` 退出。</summary>
		public static bool Forced = false;

		/// <summary>**试看模式剩余时长**（秒；`aimcam test [秒]`，默认 3）—— &gt;0 时每帧倒计时，
		/// 到点自动解除 <see cref="Forced"/>（= 自动切回引擎相机，不用记着 unlock）。</summary>
		public static float TestSeconds = 0f;

		/// <summary>接管后的臂长（米）。默认 2.8 = 近景过肩感；比拉拽的 8 米近得多。</summary>
		public static float ArmLength = 2.8f;

		/// <summary>相机**画面上下**微调（米，负 = 画面里人往下挪）—— 写进跟随相机的
		/// **SocketOffset.Z**（`SpringArmCameraView.SetFollowSocketZ`；= UE 弹簧臂的 SocketOffset.Z，
		/// 相机系 —— 不管俯仰多少，"人往画面哪边挪"的含义一致）。默认 0。</summary>
		public static float LiftMeters = 0f;

		/// <summary>鼠标灵敏度（**每像素多少度**）；最终值 = 本值 × 引擎的 `Input.MouseSensitivity`。
		/// 基准照飞行相机的 0.12（`FlightTuning.CamLookSensitivity`）。</summary>
		public static float Sensitivity = 0.12f;

		/// <summary>俯仰钳制（度）——照飞行那组（±85 会翻画面）。</summary>
		public static float PitchMin = -80f;
		public static float PitchMax = 75f;

		/// <summary>锚点**自动跟头骨**（默认开）：环绕点贴住角色**动画里的真实头部高度** ——
		/// 蹲姿瞄准就跟着压低。为什么需要：引擎的眼高公式只认引擎自己的蹲姿状态（`CrouchMode`），
		/// **认不出纯动画的蹲伏**（我们的 ready/hold = 动画蹲姿）⇒ 不跟 = 环绕点悬在 1.90 m 站姿高度上、取景不对。
		/// 关掉后用 <see cref="AnchorHeight"/>。</summary>
		public static bool AnchorFollowHead = true;

		/// <summary>锚点**固定高度**（米，从脚底算；≤ 0 = 引擎口径）。仅在 <see cref="AnchorFollowHead"/> = false 时生效。</summary>
		public static float AnchorHeight = 0f;

		/// <summary>自动跟头骨时的"头上抬量"（米）—— 环绕点取在头骨原点之上一点点。</summary>
		public const float HeadAnchorUpOffset = 0.10f;

		/// <summary>锚点平滑速度（每秒收敛比例）—— 照引擎自己那套 6/s 的锚点平滑，防取景随动画抖。</summary>
		public const float AnchorSmoothSpeed = 6f;

		/// <summary>是否正在接管。</summary>
		public static bool IsActive { get; private set; }

		// ── 运行状态 ──
		private static float _yaw;                       // spring-arm 世界口径（与 ArmYaw 同源）
		private static float _pitch;
		private static ICameraLookProvider _prevProvider;
		private static bool _errorLogged;
		private static float _pivotZ;                    // 平滑后的环绕点偏移 Z（= UE TargetOffset.Z，见 UpdateAnchor）
		private static bool _pivotZInit;                 // 首次直接到位（不要从 0 慢慢滑进来）


		/// <summary>`CameraLook` 的接管方（引擎等一切"要看哪"的调用方都读它）。</summary>
		private sealed class AimLookProvider : ICameraLookProvider
		{
			public bool TryGetLook(out Vec3 forward) => TryGetLookInternal(out forward);
		}
		private static readonly AimLookProvider ProviderInstance = new AimLookProvider();

		// ─────────────────────────────── 每帧 ───────────────────────────────

		public static void Tick(float dt)
		{
			try
			{
				Mission mission = Mission.Current;
				Agent main = mission?.MainAgent;
				if (mission == null || main == null)
				{
					if (IsActive) ExitInternal(handBack: false);
					return;
				}

				// 跟随被别的系统顶掉（演出接管 / 强停）⇒ 本相机立即让位，别继续写方向
				if (IsActive && !SpringArmCameraView.IsFollowing)
				{
					ExitInternal(handBack: false);
				}

				// 试看模式倒计时（`aimcam test [秒]`）：到点自动解除锁定 ⇒ 走下面的正常出口自动切回引擎相机。
				if (Forced && TestSeconds > 0f)
				{
					TestSeconds -= dt;
					if (TestSeconds <= 0f)
					{
						TestSeconds = 0f;
						Forced = false;
						DebugLogger.Log("[GrappleAim] 试看结束 —— 自动切回引擎相机");
					}
				}

				GrappleLogic logic = GrappleLogic.Current;
				bool grappleBusy = logic != null && logic.IsBusy;

				if (!IsActive)
				{
					// 进（2026-10-05 用户裁定：**开火之后才接入**，瞄准阶段相机归引擎）——
					//   理由（用户实机）：我们一接管，引擎的**准星/整套瞄准表现就没了**，开火取方向那条链也受影响。
					//   · 钩索流程已在跑 = 开火后（钩头在飞 / 蓄势 / 拉拽中）；
					//   · 拉拽已接管相机时不抢（`IsFollowing` = 有人拿着）；
					//   · `aimcam lock / test` 调试锁定照旧随时可接管。
					if (Enabled && !SpringArmCameraView.IsFollowing && (grappleBusy || Forced))
					{
						Enter(main, Forced ? "lock" : "post-shot");
					}
					return;
				}

				// 已接管：钩索流程还在跑 **或** 调试锁定 ⇒ 继续跟随 + 鼠标驱动；
				// 打空 / 收钩回到 Idle ⇒ 走"取消"出口交还引擎。
				if (grappleBusy || Forced)
				{
					DriveLook(main);
					UpdateAnchor(main, dt);
				}
				else
				{
					ExitInternal(handBack: true);
				}
			}
			catch (Exception ex)
			{
				if (!_errorLogged)
				{
					_errorLogged = true;
					DebugLogger.Log($"[GrappleAim] tick 异常（已让位相机）：{ex.GetType().Name} {ex.Message}");
				}
				if (IsActive) ExitInternal(handBack: false);
			}
		}

		// ─────────────────────────────── 进出场 ───────────────────────────────

		private static void Enter(Agent main, string why)
		{
			// 方向 = 接管那一刻的引擎机位（不硬切）；`writeBackLookOnReturn` = 交还时把我们的朝向写回引擎
			// （这一路没有冻结/解冻，引擎不会重置相机 ⇒ 写回 = 引擎从我们停的地方接着看，零旋转）。
			if (!SpringArmCameraView.ApplyFollowFromEngineCamera(main, 0f, writeBackLookOnReturn: true))
			{
				return;   // 接管失败（不在 mission screen / 相机实体拿不到）—— 下帧再试，静默
			}

			SpringArmCameraView.TryGetFollowLook(out _yaw, out _pitch);
			SpringArmCameraView.SetFollowArmLength(ArmLength);
			// 「相机自己的上下」= UE 弹簧臂的 SocketOffset.Z（画面微调那个 aimlift）；环绕点偏移走 SetFollowPivotZ。
			SpringArmCameraView.SetFollowSocketZ(LiftMeters);

			_prevProvider = CameraLook.Provider;
			CameraLook.Provider = ProviderInstance;

			IsActive = true;
			_errorLogged = false;
			_pivotZInit = false;      // 锚点首帧直接到位（别从 0 慢慢滑进来）
			DebugLogger.Log($"[GrappleAim] 钩索相机接管：臂长={ArmLength:F1}m lift={LiftMeters:F2} "
							+ $"yaw={_yaw:F0} pitch={_pitch:F0} sens={Sensitivity:F3} "
							+ $"anchor={(AnchorFollowHead ? "head-bone" : (AnchorHeight > 0f ? AnchorHeight.ToString("F2") + "m" : "engine"))} "
							+ $"signal={why}");
		}

		/// <summary>**拉拽相机收编**（`GrapplePull.EnterCamera` 调）：交还"看"的控制权（鼠标/provider），
		/// **但跟随本身不断** —— 相机继续以接管时的方向跟随（环绕点偏移保留，归还渐变会滑回 0），
		/// 拉拽侧接着改臂长/归还三件套。</summary>
		public static void OnPullAdopt()
		{
			if (!IsActive)
			{
				return;
			}
			IsActive = false;
			SpringArmCameraView.ClearFollowLook();
			if (ReferenceEquals(CameraLook.Provider, ProviderInstance))
			{
				CameraLook.Provider = _prevProvider;
			}
			DebugLogger.Log("[GrappleAim] 拉拽相机接手 —— 钩索相机交出方向控制（跟随不断）");
		}

		/// <summary>出场：让位（handBack=false，别的系统接手/场景收摊）或渐变交还引擎（handBack=true）。</summary>
		private static void ExitInternal(bool handBack)
		{
			if (!IsActive)
			{
				return;
			}
			IsActive = false;
			SpringArmCameraView.ClearFollowLook();
			if (ReferenceEquals(CameraLook.Provider, ProviderInstance))
			{
				CameraLook.Provider = _prevProvider;
			}
			if (handBack)
			{
				SpringArmCameraView.RequestHandBack(0.35f);
				DebugLogger.Log($"[GrappleAim] 钩索流程结束 —— 交还引擎相机（渐变 0.35s，朝向已写回）");
			}
			else
			{
				DebugLogger.Log("[GrappleAim] 钩索相机让位");
			}
		}

		// ─────────────────────────────── 鼠标驱动 ───────────────────────────────

		private static void DriveLook(Agent main)
		{
			float s = Sensitivity * Math.Max(0.05f, Input.MouseSensitivity);

			// 符号与飞行相机逐字相同（`FlightCameraRig.ApplyLook`）：dx 向右 → yaw 减、dy 向下 → pitch 减。
			_yaw += Input.MouseMoveX * s * -1f;
			_pitch += Input.MouseMoveY * s * -1f;

			while (_yaw > 180f) _yaw -= 360f;
			while (_yaw < -180f) _yaw += 360f;
			_pitch = MBMath.ClampFloat(_pitch, PitchMin, PitchMax);

			// ① 相机朝向（本帧）
			SpringArmCameraView.SetFollowLook(_yaw, _pitch);

			// ② 机身转向：朝相机前向。用**向量**算移动方向（前向 → atan2(y,x)），
			//    避开「ArmYaw(RotationZ) 与移动方向口径差 90°」那个坑（CLAUDE.md 铁律 35 旁注）。
			//    🔴 只在**玩家没按移动键**时转：走着的时候朝向归引擎（与原生"走动朝移动方向"一致），
			//    两边同时写会互搏。
			bool moving = false;
			try { moving = main.MovementInputVector.LengthSquared > 0.01f; } catch { }
			if (!moving)
			{
				try
				{
					Mat3 m = Mat3.Identity;
					m.RotateAboutUp(_yaw * (MathF.PI / 180f));
					m.RotateAboutSide(_pitch * (MathF.PI / 180f));
					Vec3 f = m.f;
					if (f.LengthSquared > 1e-4f)
					{
						float bodyYaw = MathF.Atan2(f.y, f.x);
						main.SetMovementDirection(new Vec2(MathF.Cos(bodyYaw), MathF.Sin(bodyYaw)));
					}
				}
				catch { /* 朝向只是观感，失败不拦 */ }
			}
		}

		// ─────────────────────────────── 锚点高度（贴蹲姿） ───────────────────────────────

		/// <summary>
		/// 每帧算**环绕点偏移 Z**喂给跟随相机（`SpringArmCameraView.SetFollowPivotZ`）——
		/// 这就是 **UE 弹簧臂的 `TargetOffset.Z`**（我们 `SpringArmCameraParam.PivotZ` 的同义词；
		/// 世界 = `VisualPosition + 引擎眼高 + PivotZ`，滑回引擎时由归还渐变自动归零）。
		///
		/// 🔴 为什么需要（用户 2026-10-04 问："锚点是什么、有没有考虑 ready/hold 角色高度比较低"）：
		///    跟随相机的锚点 = `VisualPosition + 引擎眼高公式`——而公式只认**引擎自己的蹲姿状态**
		///    (`CrouchMode`)，我们的蹲是**动画**蹲，它看不出来 ⇒ 环绕点留在站姿的 1.90 m，
		///    镜头悬在蹲下的人头上、取景不对。这里用**头骨**（`Monster.HeadLookDirectionBoneIndex`，
		///    手骨锚点同款读法 `GetBoneEntitialFrame`，读的就是当前动画帧）拿**动画里真实的头高**。
		/// </summary>
		private static void UpdateAnchor(Agent main, float dt)
		{
			float engineEye = SpringArmMath.ResolveEyeHeightOffset(main, useEngineFormula: true);
			float baseZ = BaseZ(main);
			float headZ = 0f;
			bool headOk = AnchorFollowHead && TryGetHeadHeight(main, out headZ);

			float targetPivotZ;
			if (headOk)
			{
				targetPivotZ = (headZ + HeadAnchorUpOffset) - (baseZ + engineEye);
			}
			else if (!AnchorFollowHead && AnchorHeight > 0f)
			{
				targetPivotZ = AnchorHeight - (baseZ + engineEye);
			}
			else
			{
				targetPivotZ = 0f;      // 引擎口径（不偏）
			}

			if (!_pivotZInit)
			{
				_pivotZInit = true;
				_pivotZ = targetPivotZ;      // 首帧直接到位：接管那一刻取景就是对的位置
				// 🔴 常开一行/次（2026-10-04 深夜，用户实机"高度还不够低、看不到角色"）——
				//    "锚点为什么没压在蹲姿上"必须一眼可查：模式 + 头骨采样成没成 + 三个高度 + 最终环绕点。
				string mode = headOk
					? "head-bone"
					: (AnchorFollowHead ? "head-bone(采样失败→引擎口径)" : (AnchorHeight > 0f ? AnchorHeight.ToString("F2") + "m" : "engine"));
				DebugLogger.Log($"[GrappleAim] 锚点解析：mode={mode} baseZ={baseZ:F2} engineEye={engineEye:F2} "
								+ (headOk ? $"headZ={headZ:F2} " : "")
								+ $"pivotZ={targetPivotZ:F2} => 环绕点 z={baseZ + engineEye + targetPivotZ:F2}");
			}
			else
			{
				float k = MathF.Min(1f, dt * AnchorSmoothSpeed);
				_pivotZ += (targetPivotZ - _pivotZ) * k;
			}
			SpringArmCameraView.SetFollowPivotZ(_pivotZ);
		}

		/// <summary>锚点基准 Z（与 `SpringArmMath.ComputeFrame` 同口径：优先 VisualPosition，取不到回落逻辑位置）。</summary>
		private static float BaseZ(Agent main)
		{
			try
			{
				Vec3 vp = main.VisualPosition;
				if (vp.LengthSquared > 0.01f)
				{
					return vp.z;
				}
			}
			catch { }
			try { return main.Position.z; } catch { return 0f; }
		}

		/// <summary>头骨世界高度（顶/颈关节，当前动画帧）—— 读法照 `SpellCastInput.TryGetRightHandAnchor`：
		/// `Monster.HeadLookDirectionBoneIndex`（"head" 骨）+ `AgentVisuals.GetBoneEntitialFrame`；
		/// 离角色太远（>3 m）= 这个骨帧不可信，返回 false（调用方回落引擎口径）。</summary>
		private static bool TryGetHeadHeight(Agent agent, out float headZ)
		{
			headZ = 0f;
			try
			{
				if (agent?.Monster == null)
				{
					return false;
				}
				sbyte bone = agent.Monster.HeadLookDirectionBoneIndex;
				if (bone < 0)
				{
					return false;
				}
				MBAgentVisuals visuals = agent.AgentVisuals;
				if (visuals == null || !visuals.IsValid())
				{
					return false;
				}
				Vec3 head = visuals.GetBoneEntitialFrame(bone, useBoneMapping: false).origin;
				if (head.LengthSquared < 1e-6f)
				{
					return false;
				}
				Vec3 d = head - agent.Position;
				if (d.LengthSquared > 9f)
				{
					return false;
				}
				headZ = head.z;
				return true;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>相机前向（`CameraLook` 接口实现 + 日志用）。</summary>
		private static bool TryGetLookInternal(out Vec3 forward)
		{
			forward = Vec3.Zero;
			if (!IsActive)
			{
				return false;
			}
			try
			{
				Mat3 m = Mat3.Identity;
				m.RotateAboutUp(_yaw * (MathF.PI / 180f));
				m.RotateAboutSide(_pitch * (MathF.PI / 180f));
				if (m.f.LengthSquared < 1e-4f)
				{
					return false;
				}
				forward = m.f.NormalizedCopy();
				return true;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>状态一行（`custom.grapple aimcam` 无参时打）。</summary>
		public static string StatusLine()
		{
			string anchor = AnchorFollowHead
				? "head-bone"
				: (AnchorHeight > 0f ? AnchorHeight.ToString("F2") + "m" : "engine");
			return $"aimcam={(Enabled ? "ON" : "OFF")} lock={(Forced ? 1 : 0)} active={(IsActive ? 1 : 0)} arm={ArmLength:F1}m "
				 + $"lift={LiftMeters:F2} sens={Sensitivity:F3} anchor={anchor}"
				 + (TestSeconds > 0f ? $" test={TestSeconds:F1}s" : "")
				 + (IsActive ? $" pivotZ={_pivotZ:F2} yaw={_yaw:F0} pitch={_pitch:F0}" : "");
		}
	}
}
