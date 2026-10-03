using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

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
			Pulling,    // 拉自己中（步骤 4：冻结 + 木板 + 曲线）
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

		// ── 拉自己（步骤 4）──
		private readonly GrapplePull _pull = new GrapplePull();
		private float _attachTimer;                   // 钩头钉住后过了多久（蓄势计时）
		private bool _attachFromAir;                  // 这一钩是不是"空中起钩"（决定蓄势/拉升时长的基准）
		private bool _attachAutoPull;                 // 这一钩钉住后要不要自动拉（武器开火 = 要；命令 throw = 不要）
		private int _postPullWatch;                   // 拉拽结束后再盯 N 帧（诊断"落地瞬间镜头/角色是否被转"）

		/// <summary>瞄准射线的最大长度（米）。UE 参考工程是 12 米；骑砍地图更大，默认 20（命令 `range` 可调）。</summary>
		public static float AimRange = 20f;

		/// <summary>瞄准射线找人时的粗细（米）。</summary>
		public static float AimAgentRadius = 0.3f;

		/// <summary>退化瞄准闸：地形命中点离相机小于这个距离（米）= 判为"相机贴进几何体"，拒发（见 <see cref="TryAim"/>）。</summary>
		public static float MinAimCameraDistance = 1.0f;

		/// <summary>蓄势时长（秒）：钩头钉住之后、开始拉之前的那一小段（UE 参考工程：地面 0.65 / 空中 0.35）。</summary>
		public static float PullDelayGround = 0.65f;
		public static float PullDelayAir = 0.35f;

		/// <summary>武器开火命中地形后**自动拉过去**（命令 `autopull` 可关；关掉用于"只看勾住"的调试）。</summary>
		public static bool AutoPull = true;

		/// <summary>拉拽控制器（命令层读状态用）。</summary>
		internal GrapplePull Pull => _pull;

		/// <summary>绳本体（命令层读当前参数用；改参数一律走 <see cref="Configure"/>）。</summary>
		public GrappleRope Rope => _rope;

		/// <summary>钩头（命令层读状态用）。</summary>
		internal GrappleHook Hook => _hook;

		/// <summary>手取不到时的兜底高度（米，身体坐标往上抬一点 ≈ 手位）。</summary>
		private const float FallbackHandLift = 1.25f;

		/// <summary>没在手边时的兜底绳子长度（米）。</summary>
		private const float DefaultRopeLength = 3.6f;

		/// <summary>
		/// 拿当前 Mission 上的实例，没有就挂一个（命令入口 + 开火排队入口调它）。
		/// 🔴 追加行为**拿不到 `OnBehaviorInitialize`**（引擎只调 `OnCreated`，反编译实锤，见
		/// <see cref="OnCreated"/> 的注释）⇒ 本类的 `Current` 在 **`OnCreated`** 里设；
		/// 这里的赋值是"显式起见"的冗余（`AddMissionBehavior` 内部会先调 `OnCreated`，值其实已经设上）。
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

		/// <summary>
		/// 🔴🔴 **`Current` 在这里设，不能只在 <see cref="OnBehaviorInitialize"/> 里设**（2026-10-03 实机踩到）：
		/// 引擎的顺序是「先给已在列表里的行为调 `OnBehaviorInitialize`，**之后**才调各 SubModule 的
		/// `OnMissionBehaviorInitialize`」（反编译 `Mission.Initialize` 实锤）—— 本行为是在后者里
		/// `AddMissionBehavior` 挂上的，而 `AddMissionBehavior` **只调 `OnCreated`**（同一份反编译实锤）
		/// ⇒ 它**永远拿不到 `OnBehaviorInitialize`**。此前只在那里赋值 ⇒ 玩家"只 equip 没敲过命令"
		/// 的那个场景里 `Current` 恒为 null（症状：开火被"没有 GrappleLogic"挡掉，什么都没有飞出来）。
		/// </summary>
		public override void OnCreated()
		{
			base.OnCreated();
			Current = this;
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
			try
			{
				_pull.Abort("mission end");
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
				+ " | hook: " + _hook.Describe() + " | landing: " + _landingNote
				+ " | " + _pull.Describe();
		}

		// ─────────────────────────────── 钩头模式（步骤 2） ───────────────────────────────

		/// <summary>
		/// 朝准星发一根钩头（命令 `throw`；武器开火走 <see cref="ThrowFromShot"/> → 同一条内部链路）。
		/// 返回一句英文回执（控制台纪律）。
		/// </summary>
		public string Throw()
		{
			return ThrowInternal(false, Vec3.Zero, Vec3.Zero, autoPull: false);
		}

		/// <summary>
		/// **武器开火路径**（2026-10-03 加）：带上引擎给的那一枪的**起点与方向**做兜底瞄准。
		/// 为什么需要：实机出现过"第一发正常、之后每一发都 `nothing within 20m`" —— 相机射线
		/// （第三人称瞄准机位）**自己泡在几何体里**时，射线可能一路穿出去什么都打不到；
		/// 而**这一枪的起点在弓上、方向是引擎算好的箭道**，没有这个毛病。
		/// 优先级：仍以**相机准星**为准（那是玩家看到的"指哪"）；相机打空才退回弹道。
		/// </summary>
		public string ThrowFromShot(Vec3 shotOrigin, Vec3 shotDirection)
		{
			return ThrowInternal(true, shotOrigin, shotDirection, autoPull: true);
		}

		private string ThrowInternal(bool hasShot, Vec3 shotOrigin, Vec3 shotDirection, bool autoPull)
		{
			Mission mission = Mission;
			Scene scene = mission?.Scene;
			if (scene == null)
			{
				return "Error: no scene.";
			}

			// 🔴 **先清场再瞄准**（2026-10-03 用户要求）：上一根钩头/绳**不该留着** ——
			//    以前是"瞄失败直接 return"，于是旧绳旧钩挂在原地（实机症状："第二次射的时候没销毁前一次"）。
			if (_hookPhase != HookPhase.Idle)
			{
				Release("re-throw（发射前清场）");
			}

			Vec3 hand, aim;
			bool aimAgent;
			string why;
			if (!TryAim(out hand, out aim, out aimAgent, out why))
			{
				// 相机准星打空 → 退回"这一枪自己的弹道"（见 ThrowFromShot 注释）
				if (!hasShot || !TryAimAlongShot(mission, shotOrigin, shotDirection, out hand, out aim, out aimAgent))
				{
					DebugLogger.Log($"[Grapple] 瞄准失败（相机：{why}；弹道兜底也未命中）");
					return "Error: " + why;
				}
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
			// 这一钩的两个属性：从空中起的吗（决定蓄势/拉升时长）、要不要自动拉（武器开火 = 要）
			_attachFromAir = Agent.Main != null && !Agent.Main.IsOnLand();
			_attachAutoPull = autoPull;
			_attachTimer = 0f;
			DebugLogger.Log($"[Grapple] 发射：手={Fmt(hand)} 瞄准={Fmt(aim)} 距离={dist:F1}m 目标={(aimAgent ? "人" : "地形")}"
				+ $" | 起手={(autoPull ? "武器开火(命中后自动拉)" : "命令(命中即停)")} 空中起钩={_attachFromAir}");

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

		/// <summary>
		/// 收钩（命令 `retract`；也是打空与异常的兜底）。
		/// <paramref name="reason"/> **只进日志**（2026-10-03 加：实机出现"绳突然消失"，没有原因日志没法定位）。
		/// </summary>
		public string Release(string reason = "command")
		{
			// 正在拉拽 ⇒ 先中止（拆板 + 解冻 —— 绝不留冻结状态）
			if (_pull.IsActive)
			{
				try
				{
					_pull.Abort("released (" + reason + ")");
				}
				catch (Exception ex)
				{
					DebugLogger.Log($"[Grapple] 中止拉拽异常（继续收钩）：{ex.GetType().Name} {ex.Message}");
				}
			}
			if (_hookPhase != HookPhase.Idle)
			{
				DebugLogger.Log($"[Grapple] 收钩（{reason}）| 钩头末位置 {Fmt(_hook.Position)} 状态 {_hook.State}");
			}
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
					// 打空时把射线的三个关键量打进日志 —— "相机泡在几何体里"这类问题一看就知道
					DebugLogger.Log(string.Format(
						"[Grapple] 相机瞄准打空：起点=({0:F2},{1:F2},{2:F2}) 方向=({3:F2},{4:F2},{5:F2}) 终点=({6:F2},{7:F2},{8:F2})",
						origin.x, origin.y, origin.z, forward.x, forward.y, forward.z, end.x, end.y, end.z));
					why = string.Format("nothing within {0:F0}m under the crosshair.", AimRange);
					return false;
				}

				// 🔴 退化瞄准闸（2026-10-03 加）：命中点**离相机太近** ⇒ 多半是第三人称相机贴进了墙里/地形里，
				//    射线一出来就打在自己的"内侧"上。这种点当目标 = 钩头朝**反方向**飞一小段就打空、
				//    绳随即收掉（实机症状正是"飞了一下、绳没了"）。宁可拒发（回执说明原因），别发一钩废的。
				float cameraDist = (point - origin).Length;
				if (victim == null && cameraDist < MinAimCameraDistance)
				{
					why = string.Format("aim point too close ({0:F2}m from camera) - camera clipped into geometry?", cameraDist);
					return false;
				}

				aim = point;
				hitAgent = victim != null;
				DebugLogger.Log(string.Format(
					"[Grapple] 瞄准：相机→命中 {0:F2}m · 手→命中 {1:F2}m · 目标={2} 命中点=({3:F2},{4:F2},{5:F2})",
					cameraDist, (point - hand).Length, hitAgent ? "人" : "地形",
					point.x, point.y, point.z));
				return true;
			}
			catch (Exception ex)
			{
				why = "aim ray failed (" + ex.GetType().Name + ").";
				return false;
			}
		}

		/// <summary>
		/// **弹道兜底瞄准**（武器开火路径专用）：从**这一枪的起点**（弓上）沿**引擎给的箭道方向**打射线。
		/// 相机射线打空时用它 —— 起点在弓上、方向是引擎算的，不受"相机泡在几何体里"影响。
		/// 返回 false = 这条也没打到（真的是朝天上放的）。
		/// </summary>
		private bool TryAimAlongShot(Mission mission, Vec3 shotOrigin, Vec3 shotDirection,
			out Vec3 hand, out Vec3 aim, out bool hitAgent)
		{
			hand = GetHand();
			aim = hand;
			hitAgent = false;
			if (mission?.Scene == null || shotDirection.LengthSquared < 1e-8f)
			{
				return false;
			}
			try
			{
				Vec3 dir = shotDirection.NormalizedCopy();
				Vec3 end = shotOrigin + dir * AimRange;
				Agent victim;
				Vec3 point;
				int exclude = Agent.Main != null ? Agent.Main.Index : -1;
				if (!SpellSweep.FindNearestHit(mission, shotOrigin, end, AimAgentRadius, exclude, null,
					out victim, out point))
				{
					return false;
				}
				aim = point;
				hitAgent = victim != null;
				DebugLogger.Log(string.Format(
					"[Grapple] 瞄准源=**弹道兜底**（相机打空）| 起点={0} 命中距={1:F1}m 目标={2} 命中点=({3:F2},{4:F2},{5:F2})",
					Fmt(shotOrigin), (point - shotOrigin).Length, hitAgent ? "人" : "地形",
					point.x, point.y, point.z));
				return true;
			}
			catch (Exception)
			{
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

			// ⓪ 开火拦截的排队执行（2026-10-03）：拦截补丁只"记一笔"，真正的发射与退弹在这一帧做 ——
			//    这样武器开火与命令 `throw` 走的是**同一条链路**（都在常规 tick 里）。
			GrappleFirePatch.ProcessPending();

			// ⓪′ 拉拽结束后的落地观察窗（诊断）：每帧记"玩家朝向 / 引擎 bearing / 相机位置"，
			//     看落地瞬间到底是谁在转（镜头归还？角色被引擎转向？还是位置跳）。
			if (_postPullWatch > 0)
			{
				_postPullWatch--;
				try
				{
					Agent main = Agent.Main;
					float playerYaw = 0f;
					Vec3 pos = Vec3.Zero;
					if (main != null)
					{
						pos = main.Position;
						// 🔴 用 `LookDirection.RotationZ`（= atan2(−x, y)）——**与引擎 bearing 同一约定**；
						//    别用 atan2(y, x)：那是"角色移动方向"的约定（SetMovementDirection 那一族），
						//    两个口径差 90°，混着打日志会让人白判"差了 90°"（2026-10-03 自查抓到）。
						playerYaw = main.LookDirection.RotationZ * (180f / MathF.PI);
					}
					CameraLook.TryGetEngineAnglesRaw(out float engYawDeg, out float engPitchDeg);
					Vec3 camPos = Vec3.Zero;
					try { camPos = Mission.GetCameraFrame().origin; } catch { }
					// 引擎自己的"移动方向"（我们写的朝向最后有没有被它吃进去/它有没有另写一个）
					float moveYaw = float.NaN;
					try
					{
						Vec2 md = main != null ? main.GetMovementDirection() : Vec2.Zero;
						if (md.LengthSquared > 1e-6f)
						{
							moveYaw = MathF.Atan2(md.y, md.x) * (180f / MathF.PI);
						}
					}
					catch { }
					DebugLogger.Log($"[Grapple] 落地后第 {24 - _postPullWatch} 帧：玩家yaw={playerYaw:F0}° "
						+ $"移动方向yaw={(float.IsNaN(moveYaw) ? "无" : moveYaw.ToString("F0") + "°")} "
						+ $"引擎bearing={engYawDeg:F0}/{engPitchDeg:F0}° 玩家={Fmt(pos)} 相机={Fmt(camPos)} "
						+ $"customCam={(MissionScreenHasCustomCamera() ? 1 : 0)}");
				}
				catch (Exception)
				{
					_postPullWatch = 0;
				}
			}

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
					Release("hook tick exception: " + ex.Message);
					return;
				}

				if (step == GrappleHook.StepResult.HitWorld)
				{
					_hookPhase = HookPhase.Attached;
					_attachTimer = 0f;
					ResolveLanding();
				}
				else if (step == GrappleHook.StepResult.HitAgent)
				{
					_hookPhase = HookPhase.Attached;
					_attachTimer = 0f;
					_landingNote = "attached to agent (pull-target path = step 5)";
				}
				else if (step == GrappleHook.StepResult.Missed)
				{
					Release("missed (打空)");
					return;
				}
			}
			else if (_hookPhase == HookPhase.Attached)
			{
				// 钉住之后：蓄势（UE 参考工程的那半秒）→ 自动拉（只有武器开火那一钩会）
				_attachTimer += dt;
				if (_attachAutoPull && AutoPull && _hook.AttachedAgent == null)
				{
					float delay = _attachFromAir ? PullDelayAir : PullDelayGround;
					if (_attachTimer >= delay)
					{
						_attachAutoPull = false;              // 只自动拉一次
						string r = StartPull("auto（武器开火）");
						DebugLogger.Log($"[Grapple] 自动拉拽（蓄势 {_attachTimer:F2}s ≥ {delay:F2}s）→ {r}");
						if (!r.StartsWith("OK"))
						{
							_landingNote = "auto-pull refused: " + r;
						}
					}
				}
			}
			else if (_hookPhase == HookPhase.Pulling)
			{
				GrapplePull.PullEvent ev = _pull.Tick(dt);
				if (ev == GrapplePull.PullEvent.Finished)
				{
					_postPullWatch = 24;                 // 落地后盯 8 帧（每帧一行，看镜头/角色有没有被转）
					Release("pull finished（到位拆板）");
					return;
				}
				if (ev == GrapplePull.PullEvent.Aborted)
				{
					_postPullWatch = 24;
					Release("pull aborted（拉拽中止）");
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
				Release("rope tick exception: " + ex.Message);
			}
		}

		// ─────────────────────────────── 拉自己（步骤 4） ───────────────────────────────

		/// <summary>
		/// **命令入口：拉自己**（`custom.grapple pull self`）。要求钩头已经钉在地形上（先 `throw` 或开一枪）。
		/// 真正的活由 <see cref="GrapplePull"/> 干（冻结 + 木板 + 曲线）。
		/// </summary>
		public string PullSelf()
		{
			if (_hookPhase == HookPhase.Pulling)
			{
				return "Error: already pulling.";
			}
			if (_hookPhase != HookPhase.Attached)
			{
				return "Error: no hook attached (throw first, or fire with the grapple)";
			}
			if (_hook.AttachedAgent != null)
			{
				return "Error: hook is on a person (pull-target path = step 5)";
			}
			return StartPull("command");
		}

		/// <summary>起一次拉拽（命令与自动两条路共用）。失败时什么都不动。</summary>
		private string StartPull(string why)
		{
			Agent main = Agent.Main;
			Scene scene = Mission?.Scene;
			if (main == null || scene == null)
			{
				return "Error: no player/scene.";
			}
			bool fromAir = !main.IsOnLand();
			string r = _pull.Start(main, _landing.Endpoint, _landing.Found, scene, fromAir, _hook.AttachedPoint);
			if (r.StartsWith("OK"))
			{
				_hookPhase = HookPhase.Pulling;
				DebugLogger.Log($"[Grapple] 拉拽开始（{why}）| 落点={Fmt(_landing.Endpoint)} 平台={(_landing.Found ? "有" : "无")} → {r}");
			}
			return r;
		}

		/// <summary>诊断用：当前是不是我们/别人在接管相机（铁律 35 的判据）。</summary>
		private static bool MissionScreenHasCustomCamera()
		{
			try
			{
				return (ScreenManager.TopScreen as MissionScreen)?.CustomCamera != null;
			}
			catch (Exception)
			{
				return false;
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
