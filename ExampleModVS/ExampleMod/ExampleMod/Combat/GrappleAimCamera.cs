using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **钩索相机（政策层）** —— 2026-10-04 立、2026-10-05 改口径（**开火之后**接管）、
	/// 2026-10-05 阶段 2 缩成现在这样：**只管玩法政策**，不摆相机、不读鼠标、不算锚点。
	///
	/// 它只回答三个问题：
	///   ① **什么时候接管**：钩索流程在跑（开火后：钩头在飞 / 已挂住 / 拉拽中）⇒ `Play("grapple_shot")`；
	///   ② **什么时候归还**：流程回到 Idle ⇒ 渐变交还引擎；
	///   ③ **身体朝哪**（玩法表现，不是相机）：玩家没按移动键时，身体转向镜头前方（走的时候朝向归引擎）。
	///
	/// 其余全在相机模块里按 `Camera.csv` 的 `grapple_shot` 行自动生效：
	/// 臂长 2.8 · 鼠标驱动（`MouseLook=1`）· 锚点跟头骨（`AnchorFollowHead=1`）· 相机朝向同步给
	/// `CameraLook`（服务的唯一提供者转给机器）。
	///
	/// 🔴 **为什么是"开火之后"而不是"瞄准期间"**（2026-10-05 用户实机裁定）：
	///    做成"按住左键瞄准就接管"会撞两个硬伤 —— ① 我们一接管，引擎的**准星/整套瞄准表现全没了**；
	///    ② 开火取方向那条链（`CameraLook`）在开火瞬间出问题（钩头没发出去）。
	///    ⇒ 瞄准阶段相机**完全归引擎**（准星/开火全原生），**开火之后**（钩索流程开始跑）才接入。
	///
	/// 验收命令：`custom.cam play grapple_shot [秒]`（接管看机位）· `custom.grapple cam`（总开关/臂长）。
	/// </summary>
	internal static class GrappleAimCamera
	{
		/// <summary>持有者标签（钩索这一整段 = 同一个 owner：瞄准相机与拉拽共享，所以拉拽能"收编"它）。</summary>
		public const string Owner = "grapple";

		/// <summary>开火后接管用的机位行。</summary>
		public const string CaseName = "grapple_shot";

		/// <summary>拉拽用的机位行（`GrapplePull` 收编/起播时用同一条名字）。</summary>
		public const string PullCaseName = "grapple_pull";

		/// <summary>**钩索相机总开关**（默认开；`custom.grapple cam off|on`）—— 关掉 = 瞄准与拉拽都用引擎相机。</summary>
		public static bool Enabled = true;

		/// <summary>本段是否正持有相机（`GrapplePull` 据此决定"收编"还是"新起播"）。</summary>
		public static bool IsActive { get; private set; }

		private static bool _errorLogged;
		private static bool _caseMissingLogged;

		// ─────────────────────────────── 每帧（政策） ───────────────────────────────

		public static void Tick(float dt)
		{
			try
			{
				Mission mission = Mission.Current;
				Agent main = mission?.MainAgent;
				if (mission == null || main == null)
				{
					IsActive = false;
					return;
				}

				// 被别人顶掉（演出/对话的一次性机位抢占、或别人起播）⇒ 让位，别继续写方向
				if (IsActive && !CameraService.IsHeldBy(Owner))
				{
					IsActive = false;
					DebugLogger.Log("[GrappleAim] 相机已被别人接手 —— 钩索相机让位");
				}

				GrappleLogic logic = GrappleLogic.Current;
				bool grappleBusy = logic != null && logic.IsBusy;

				if (!IsActive)
				{
					// 进：**开火之后**（钩索流程已在跑）才接入；已经有人拿着相机时不抢。
					// 🔴 **骑马期间不接管**（2026-10-09 用户拍板，方案 §13.11）：我们的锚点公式
					//    （SpringArmMath.ResolveEngineEyeHeight）**只有站/蹲/倒地三支**，引擎骑马那一支
					//    在站姿眼高之外**另有一项坐骑项**、我们没抄过 ⇒ 接管 = 取景系统性不对；
					//    而引擎自己的骑马相机本来就是对的 —— 骑马时把它留着。
					//    （将来做"马上演出机位"时：先把那一支反编译抄进 SpringArmMath，再放开这条守卫。）
					if (Enabled && grappleBusy && !CameraService.IsHeld && !main.HasMount)
					{
						Enter(main);
					}
					return;
				}

				if (grappleBusy)
				{
					DriveBodyTowardCamera(main);
				}
				else
				{
					// 打空 / 收钩回到 Idle ⇒ 渐变交还引擎
					Exit(handBack: true);
				}
			}
			catch (Exception ex)
			{
				if (!_errorLogged)
				{
					_errorLogged = true;
					DebugLogger.Log($"[GrappleAim] tick 异常（已让位相机）：{ex.GetType().Name} {ex.Message}");
				}
				if (IsActive)
				{
					IsActive = false;
					if (CameraService.IsHeldBy(Owner))
						CameraService.Stop();
				}
			}
		}

		// ─────────────────────────────── 进出场 ───────────────────────────────

		private static void Enter(Agent main)
		{
			if (!CameraCase.TryGet(CaseName, out _))
			{
				if (!_caseMissingLogged)
				{
					_caseMissingLogged = true;
					DebugLogger.Log($"[GrappleAim] 🔴 Camera.csv 缺 `{CaseName}` 行 —— 不接管相机"
									+ "（请检查 ModuleData/DesignData/Camera.csv；不设代码兜底）");
				}
				return;
			}

			// 一次调用接管：方向照抄引擎那一刻的机位（Seed=Engine，不硬切）+ 臂长 2.8（行里）+
			// 鼠标驱动 + 锚点跟头骨（都在行里）—— 由相机机器按 case 自动生效。
			// `WriteBackLook = true`：这一路没有冻结/解冻，引擎不会重置相机 ⇒ 交还时把我们的朝向写回引擎
			// = 引擎从我们停的地方接着看（零旋转）。
			if (!CameraService.Play(CaseName, main, 0f, Owner,
					new CameraReturnPolicy { WriteBackLook = true }))
			{
				return;   // 起播失败 / 被别人持有 —— 下帧再试，静默（日志在服务里）
			}

			IsActive = true;
			_errorLogged = false;
			DebugLogger.Log($"[GrappleAim] 钩索相机接管（case={CaseName}，开火后接入）");
		}

		/// <summary>
		/// **拉拽相机收编**（`GrapplePull` 调）：交还"看"的控制权（鼠标/provider）但**跟随本身不断** ——
		/// 现在由 `CameraService.Adopt("grapple_pull", …)` 一次完成（不重播种），这里只标记本段退场。
		/// </summary>
		public static void OnPullAdopt()
		{
			if (!IsActive)
				return;
			IsActive = false;
			DebugLogger.Log("[GrappleAim] 拉拽相机接手 —— 钩索相机交出方向控制（跟随不断）");
		}

		/// <summary>出场：渐变交还引擎（handBack=true）或已让别人接手时只标记退场。</summary>
		private static void Exit(bool handBack)
		{
			if (!IsActive)
				return;
			IsActive = false;
			if (handBack && CameraService.IsHeldBy(Owner))
			{
				CameraService.RequestHandBack(0.35f);
				DebugLogger.Log("[GrappleAim] 钩索流程结束 —— 交还引擎相机（渐变 0.35s）");
			}
			else
			{
				DebugLogger.Log("[GrappleAim] 钩索相机让位");
			}
		}

		// ─────────────────────────────── 身体朝向（玩法表现） ───────────────────────────────

		/// <summary>
		/// 玩家没按移动键时把身体转向**镜头前方**（走着的时候朝向归引擎，与原生一致；两边同时写会互搏）。
		/// 用**向量**算移动方向（前向 → `atan2(y,x)`），避开「`ArmYaw`(RotationZ) 与移动方向口径差 90°」那个坑。
		/// </summary>
		private static void DriveBodyTowardCamera(Agent main)
		{
			bool moving = false;
			try { moving = main.MovementInputVector.LengthSquared > 0.01f; } catch { }
			if (moving)
				return;

			try
			{
				if (!CameraService.TryGetBasis(out Vec3 f, out _) || f.LengthSquared < 1e-4f)
					return;
				float bodyYaw = MathF.Atan2(f.y, f.x);
				main.SetMovementDirection(new Vec2(MathF.Cos(bodyYaw), MathF.Sin(bodyYaw)));
			}
			catch
			{
				// 朝向只是观感，失败不拦
			}
		}

		/// <summary>状态一行（`custom.grapple cam` 无参时打）。</summary>
		public static string StatusLine()
		{
			return $"aimCam={(Enabled ? "ON" : "OFF")} active={(IsActive ? 1 : 0)} case={CaseName}"
				 + (IsActive ? $" heldBy={CameraService.Holder ?? "(none)"}" : "");
		}
	}
}
