using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 钩索的运行时宿主（MissionLogic，2026-10-01 立；方案 = plans\钩索-实施计划.md）。
	///
	/// 现阶段（**第 1 步：绳子能画出来**）只做一件事：
	///   **把一根绳的一端拴在主角手上、另一端钉在世界某点**，然后每帧喂给
	///   <see cref="GrappleRope"/> 去模拟 —— 用它来判断绳子的运动与观感。
	///
	/// 还没做的（后面几步）：钩头飞行、命中判定、拉自己 / 拉目标、弓形武器接线。
	/// </summary>
	public class GrappleLogic : MissionLogic
	{
		/// <summary>当前任务的实例（命令入口用）。</summary>
		public static GrappleLogic Current { get; private set; }

		private readonly GrappleRope _rope = new GrappleRope();
		private bool _anchored;
		private Vec3 _anchor;

		/// <summary>绳本体（命令层读当前参数用；改参数一律走 <see cref="Configure"/>）。</summary>
		public GrappleRope Rope => _rope;

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
			_anchored = false;
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
			return "grapple: " + head + " | rope: " + _rope.Status();
		}

		// ─────────────────────────────── 每帧 ───────────────────────────────

		public override void OnMissionTick(float dt)
		{
			base.OnMissionTick(dt);
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
