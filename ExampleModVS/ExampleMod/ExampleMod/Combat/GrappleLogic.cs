using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 钩索的运行时宿主（MissionLogic，2026-10-01 立；方案 = plans\钩索-实施计划.md）。
	///
	/// 两条并行的工作模式（互斥 —— 钩头模式接管绳子）：
	///   · **手动锚定**（步骤 1）：`custom.grapple anchor` 把绳一端钉在世界某点，纯看绳的运动；
	///   · **钩头模式**（步骤 2 起）：`throw`（或武器开火）→ <see cref="GrappleHook"/> 飞出去 →
	///     命中后停在命中点、绳钉住；同时解算**落点平台**（<see cref="GrappleLanding"/>，方案 §3.7）。
	///
	/// 还没做的（后面几步）：武器接线（步骤 3）、拉自己（步骤 4）、拉目标（步骤 5）、收线（步骤 6）。
	/// </summary>
	public class GrappleLogic : MissionLogic
	{
		/// <summary>当前任务的实例（命令入口用）。</summary>
		public static GrappleLogic Current { get; private set; }

		private enum HookPhase
		{
			Idle,
			Flying,     // 钩头在飞
			Attached,   // 钩头钉住（墙/地 = AttachedPoint；人 = 挂在身上）
		}

		private readonly GrappleRope _rope = new GrappleRope();
		private bool _anchored;
		private Vec3 _anchor;

		// ── 钩头模式（步骤 2）──
		private readonly GrappleHook _hook = new GrappleHook();
		private HookPhase _hookPhase = HookPhase.Idle;
		private Vec3 _aimPoint;                       // 本次发射的瞄准点（命中点）
		private bool _aimHitAgent;                    // 瞄准点是不是人（是人就不解算落点平台）
		private GrappleLanding.Result _landing;       // 命中后解算的落点（飞行终点）
		private string _landingNote = "-";            // 落点解算的一行摘要（dump 用）

		/// <summary>瞄准射线的最大长度（米）。UE 参考工程是 12 米；骑砍地图更大，默认 20（命令 `range` 可调）。</summary>
		public static float AimRange = 20f;

		/// <summary>瞄准射线找人时的粗细（米）。</summary>
		public static float AimAgentRadius = 0.3f;

		/// <summary>绳本体（命令层读当前参数用；改参数一律走 <see cref="Configure"/>）。</summary>
		public GrappleRope Rope => _rope;

		/// <summary>钩头（命令层读状态用）。</summary>
		internal GrappleHook Hook => _hook;

		/// <summary>手取不到时的兜底高度（米，身体坐标往上抬一点 ≈ 手位）。</summary>
		private const float FallbackHandLift = 1.25f;

		/// <summary>没在手边时的兜底绳子长度（米）。</summary>
		private const float DefaultRopeLength = 3.6f;

		/// <summary>
		/// 拿当前 Mission 上的实例，没有就挂一个（命令入口调它）。
		/// 🔴 追加行为**拿不到 `OnBehaviorInitialize`**（引擎只调 `OnCreated`，反编译实锤，见
		/// `AI/NpcSightSystem.cs` 的注释）⇒ 这里自己把 <see cref="Current"/> 设上。
		/// </summary>
		public static GrappleLogic Ensure()
		{
			Mission mission = Mission.Current;
			if (mission == null) return null;
			if (Current != null && Current.Mission == mission) return Current;
			GrappleLogic c = new GrappleLogic();
			mission.AddMissionBehavior(c);
			Current = c;
			return c;
		}

		public override void OnBehaviorInitialize()
		{
			base.OnBehaviorInitialize();
			Current = this;
		}

		protected override void OnEndMission()
		{
			base.OnEndMission();
			try
			{
				_rope.Teardown();
			}
			catch (Exception)
			{
				// 离场清理失败无所谓，实体随场景走
			}
			try
			{
				_hook.Clear();
			}
			catch (Exception)
			{
			}
			_anchored = false;
			_hookPhase = HookPhase.Idle;
			if (Current == this) Current = null;
		}

		// ─────────────────────────────── 对外操作（命令调） ───────────────────────────────

		/// <summary>
		/// 把绳的另一端钉在世界点 <paramref name="point"/> 上并显示。
		/// <paramref name="ropeLength"/>：绳自身总长（米）；**&lt;=0 = 自动**（跨度 +30%，保证看得见垂度）。
		/// <paramref name="segments"/>：段数；**&lt;=0 = 自动**（按绳长算，每段 ≈18cm）。每段多长由两者相除得来，不用管。
		/// </summary>
		public string Anchor(Vec3 point, float ropeLength = -1f, int segments = -1)
		{
			Scene scene = Mission?.Scene;
			if (scene == null) return "Error: no scene.";

			float span = (point - GetHand()).Length;

			// 绳长：给了就用给的（**并关掉橡皮筋** —— 想验"固定长度"就用它）；
			//      没给 = 橡皮筋模式（绳长自己跟着跨度走，永远留余量）
			if (ropeLength > 0f)
			{
				_rope.Length = MathF.Min(ropeLength, _rope.MaxLength);
				_rope.AutoLength = false;
			}
			else
			{
				_rope.AutoLength = true;
				if (_rope.Length < _rope.MinLength) _rope.Length = MathF.Max(span, _rope.MinLength);
			}

			// 段数：给了就锁死；没给 = 用当前分辨率（默认 48）
			if (segments > 0)
			{
				_rope.Segments = segments;
			}

			// 🔴 **先定长度/段数再 Build**：顺序反了会出现"点数按旧值、静止长度按新值"的错配。
			if (!_rope.Build(scene))
			{
				return "Error: rope build failed (" + _rope.LastError + ")";
			}

			_anchor = point;
			_anchored = true;
			_rope.FreeEnd = false;          // 重新锚定 = 把远端钉回去（`release` 的反动作）
			_rope.Show(GetHand(), _anchor);
			return $"grapple: anchored at ({point.x:F2},{point.y:F2},{point.z:F2}) | {_rope.Status()}";
		}

		/// <summary>收绳（藏起来，实体留着复用）。</summary>
		public string Clear()
		{
			_anchored = false;
			_rope.Hide();
			return "grapple: rope hidden (entities kept for reuse)";
		}

		/// <summary>
		/// 改完参数后调它：重建（段数/网格变了才真重建）+ 重新铺一遍 + 回状态。
		/// 参数一律直接写 <see cref="Rope"/> 上的公开字段（命令层就是这么用的）。
		/// </summary>
		public string Refresh()
		{
			if (!_rope.Build(Mission?.Scene))
			{
				return "grapple: rebuild failed (" + _rope.LastError + ")";
			}
			if (_anchored) _rope.Show(GetHand(), _anchor);
			return "grapple: " + _rope.Status();
		}

		/// <summary>状态（命令 `dump` 用；**单行** —— 控制台里换行会串版）。</summary>
		public string Status()
		{
			string head = _anchored ? "anchored" : "idle";
			return "grapple: " + head + " | rope: " + _rope.Status()
				+ " | hook: " + _hook.Describe() + " | landing: " + _landingNote;
		}

		// ─────────────────────────────── 钩头模式（步骤 2） ───────────────────────────────

		/// <summary>
		/// 朝准星发一根钩头（命令 `throw`；步骤 3 的武器开火将走同一条链路）。
		/// 返回一句英文回执（控制台纪律）。
		/// </summary>
		public string Throw()
		{
			Mission mission = Mission;
			Scene scene = mission?.Scene;
			if (scene == null)
			{
				return "Error: no scene.";
			}

			Vec3 hand, aim;
			bool aimAgent;
			string why;
			if (!TryAim(out hand, out aim, out aimAgent, out why))
			{
				return "Error: " + why;
			}

			Vec3 toAim = aim - hand;
			float dist = toAim.Length;
			if (dist < 0.4f)
			{
				return "Error: target too close.";
			}
			Vec3 dir = toAim * (1f / dist);

			// 绳先摆出来（手 → 钩头方向的一小段），此后每帧跟着钩头 —— 顺序照 Anchor()：先定长度再 Build
			_rope.AutoLength = true;
			_rope.FreeEnd = false;
			if (_rope.Length < _rope.MinLength)
			{
				_rope.Length = _rope.MinLength;
			}
			if (!_rope.Build(scene))
			{
				return "Error: rope build failed (" + _rope.LastError + ")";
			}
			_rope.Show(hand, hand + dir * 0.5f);

			if (!_hook.Launch(scene, hand, dir, dist + 0.6f))
			{
				return "Error: hook launch failed.";
			}

			_hookPhase = HookPhase.Flying;
			_aimPoint = aim;
			_aimHitAgent = aimAgent;
			_landingNote = "-";
			DebugLogger.Log($"[Grapple] 发射：手={Fmt(hand)} 瞄准={Fmt(aim)} 距离={dist:F1}m 目标={(aimAgent ? "人" : "地形")}");

			return string.Format("OK: hook thrown | dist={0:F1}m target={1} aim=({2:F2},{3:F2},{4:F2})",
				dist, aimAgent ? "agent" : "terrain", aim.x, aim.y, aim.z);
		}

		/// <summary>
		/// 只解算、不发射（命令 `probe`）：打印瞄准点 + 落点平台的逐环明细。
		/// **调参/排查"为什么这次落在那里"全靠它** —— 不用真的飞一次（方案 §3.7）。
		/// </summary>
		public string Probe()
		{
			Mission mission = Mission;
			if (mission?.Scene == null)
			{
				return "Error: no scene.";
			}

			Vec3 hand, aim;
			bool aimAgent;
			string why;
			if (!TryAim(out hand, out aim, out aimAgent, out why))
			{
				return "Error: " + why;
			}

			if (aimAgent)
			{
				return string.Format("probe: aim=({0:F2},{1:F2},{2:F2}) target=agent (no landing solve; pull-target path = step 5)",
					aim.x, aim.y, aim.z);
			}

			Vec3 playerPos = Agent.Main != null ? Agent.Main.Position : hand;
			GrappleLanding.Result r = GrappleLanding.Solve(mission.Scene, aim, playerPos);
			return string.Format("probe: aim=({0:F2},{1:F2},{2:F2}) dist={3:F1}m | {4}",
				aim.x, aim.y, aim.z, (aim - playerPos).Length, GrappleLanding.Describe(r));
		}

		/// <summary>收钩（命令 `retract`；也是异常兜底）。</summary>
		public string Release()
		{
			_hook.Clear();
			_hookPhase = HookPhase.Idle;
			_rope.Hide();
			_landingNote = "released";
			return "grapple: hook released, rope hidden";
		}

		/// <summary>
		/// 准星瞄准：从**相机**沿视线射一条 ≤ <see cref="AimRange"/> 米的射线，取"人 / 场景"里近的那个。
		/// 返回 false = 拿不到方向（接管中没人注册）或打空（天上）。
		/// 🔴 方向来源 = <see cref="CameraLook.TryGet"/>（铁律 35）；位置可以照读（相机 frame 的 origin）。
		/// </summary>
		private bool TryAim(out Vec3 hand, out Vec3 aim, out bool hitAgent, out string why)
		{
			hand = GetHand();
			aim = hand;
			hitAgent = false;
			why = "";

			Mission mission = Mission;
			if (mission?.Scene == null)
			{
				why = "no scene.";
				return false;
			}
			Agent main = Agent.Main;
			if (main == null)
			{
				why = "no player agent.";
				return false;
			}

			Vec3 forward;
			if (!CameraLook.TryGet(out forward) || forward.LengthSquared < 1e-6f)
			{
				why = "no camera look this frame.";
				return false;
			}

			Vec3 origin;
			try
			{
				origin = mission.GetCameraFrame().origin;
			}
			catch (Exception)
			{
				origin = main.GetEyeGlobalPosition();
			}
			Vec3 end = origin + forward.NormalizedCopy() * AimRange;

			try
			{
				Agent victim;
				Vec3 point;
				if (!SpellSweep.FindNearestHit(mission, origin, end, AimAgentRadius, main.Index, null,
					out victim, out point))
				{
					why = string.Format("nothing within {0:F0}m under the crosshair.", AimRange);
					return false;
				}
				aim = point;
				hitAgent = victim != null;
				return true;
			}
			catch (Exception ex)
			{
				why = "aim ray failed (" + ex.GetType().Name + ").";
				return false;
			}
		}

		/// <summary>命中地形后解算落点平台（§3.7）；结果暂存，步骤 4 的拉拽直接消费。</summary>
		private void ResolveLanding()
		{
			Vec3 playerPos = Agent.Main != null ? Agent.Main.Position : GetHand();
			_landing = GrappleLanding.Solve(Mission?.Scene, _hook.AttachedPoint, playerPos);
			_landingNote = GrappleLanding.Describe(_landing);
			DebugLogger.Log($"[Grapple] 落点解算 @ 钩点 {Fmt(_hook.AttachedPoint)}：{_landingNote}");
		}

		/// <summary>绳的远端：勾住人 = 目标身上；其余 = 钩头位置。</summary>
		private Vec3 FarEnd()
		{
			Agent attached = _hook.AttachedAgent;
			if (attached != null)
			{
				try
				{
					return attached.Position + Vec3.Up * 1.2f;
				}
				catch (Exception)
				{
					// agent 没了就用最后位置
				}
			}
			return _hook.Position;
		}

		private static string Fmt(Vec3 v)
		{
			return string.Format("({0:F2},{1:F2},{2:F2})", v.x, v.y, v.z);
		}

		// ─────────────────────────────── 每帧 ───────────────────────────────

		public override void OnMissionTick(float dt)
		{
			base.OnMissionTick(dt);

			// ① 钩头模式（步骤 2 起）：它接管绳子；手动锚定让位
			if (_hookPhase != HookPhase.Idle)
			{
				TickHook(dt);
				return;
			}

			// ② 手动锚定模式（步骤 1 的命令）
			if (!_anchored) return;
			try
			{
				_rope.Tick(dt, GetHand(), _anchor);
			}
			catch (Exception ex)
			{
				// 单帧出错就收绳 —— 绝不让它每帧刷异常
				_anchored = false;
				_rope.Hide();
				DebugLogger.Log($"[Grapple] tick disabled after exception: {ex.GetType().Name} {ex.Message}");
			}
		}

		/// <summary>钩头模式每帧：推进钩头 → 处理命中/打空 → 绳跟着（手 → 远端）。</summary>
		private void TickHook(float dt)
		{
			if (_hookPhase == HookPhase.Flying)
			{
				GrappleHook.StepResult step;
				try
				{
					step = _hook.Tick(Mission, dt, Agent.Main != null ? Agent.Main.Index : -1);
				}
				catch (Exception ex)
				{
					DebugLogger.Log($"[Grapple] 钩头 tick 异常，收钩：{ex.GetType().Name} {ex.Message}");
					Release();
					return;
				}

				if (step == GrappleHook.StepResult.HitWorld)
				{
					_hookPhase = HookPhase.Attached;
					ResolveLanding();
				}
				else if (step == GrappleHook.StepResult.HitAgent)
				{
					_hookPhase = HookPhase.Attached;
					_landingNote = "attached to agent (pull-target path = step 5)";
				}
				else if (step == GrappleHook.StepResult.Missed)
				{
					Release();
					return;
				}
			}

			try
			{
				_rope.Tick(dt, GetHand(), FarEnd());
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 绳 tick 异常，收钩：{ex.GetType().Name} {ex.Message}");
				Release();
			}
		}

		/// <summary>手的世界位置（复用蓄力球那套挂点读取；取不到退回"身体坐标 + 抬一点"）。</summary>
		private Vec3 GetHand()
		{
			Agent player = Agent.Main;
			if (player == null) return _anchor;
			Vec3 hand;
			if (SpellCastInput.TryGetRightHandAnchor(player, out hand))
			{
				return hand;
			}
			return player.Position + Vec3.Up * FallbackHandLift;
		}
	}
}
