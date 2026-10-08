using System;
using System.Collections.Generic;
using System.Diagnostics;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 钩索的「绳」—— 点链模拟 + 两种画法（2026-10-01 立，方案 = plans\钩索-实施计划.md §3.3）。
	///
	/// **为什么不直接用引擎的绳子**：
	///   · 缰绳是引擎给**坐骑**开的专用管线（两端锚点骨写死在 `monsters.xml` + 专用骨架），只做得了
	///     "焊在马身上"，做不了任意两点之间的自由绳；
	///   · 1.2.12 的实体组件表里**没有绳索**；战帆 DLC 的 `rope_physics_body` 是 1.5.x 的。
	///
	/// **模拟**：绳 = `段数+1` 个点，verlet 积分 + 距离约束；两端钉死（手 / 锚点），中间自由 ——
	///   绷直 / 下垂 / 甩尾全都是这一套自然长出来的。
	///
	/// **两种画法**：
	///   · **分段管**（<see cref="ChainMode"/> = false，默认）：N 个实体，每个挂同一份直管网格
	///     （运行时 `Mesh.CreateMesh` 造的，只造一次）。摆放 = **中点法**：段 i 从"点 i-1 与点 i 的中点"
	///     画到"点 i 与点 i+1 的中点" ⇒ 相邻段**共享端点、方向连续**，拐弯处不会交叉（2026-10-01 实机教训）。
	///   · **铁链**（<see cref="ChainMode"/> = true，**推荐**）：一串竖长铁环，相邻环绕链轴转 90°。
	///     环网格同样运行时造、所有环共用；沿**光滑曲线**（Catmull-Rom 加密）按固定节距铺 ⇒ 观感最好。
	///
	/// 🔴 运行时造网格（`Mesh.CreateMesh` + `AddTriangle`）**2026-10-01 实机验证可上屏**。
	///   但"**每帧 ClearMesh 重建**"那条路**不通**（试过整条弯曲管，完全画不出来，已删除 —— 见下方注释）。
	/// </summary>
	public class GrappleRope
	{
		// ─────────────────────── 可调参数（命令改，都是"看着调"的量） ───────────────────────

		/// <summary>**模拟分辨率**：把绳切成几段点（点 = 它 + 1）。
		/// 🔴 它是**固定的分辨率**，不跟绳长联动 —— 绳长每帧在变，段数一变就要重建实体（灾难）。
		///   视觉上的粗细密度由"铺件间距"决定（链节间距 / 管件长度），跟这个无关。</summary>
		public int Segments = 48;

		/// <summary>绳长（米）。<see cref="AutoLength"/> 开着时它每帧由跨度推出来，这里的值是当前值。</summary>
		public float Length = 3.6f;

		/// <summary>
		/// **橡皮筋模式**（2026-10-01 用户裁定，默认开）：绳长不再固定，每帧按"手到锚点的跨度"推：
		/// 走远 = 被拽出来（变长）· 走近 = 自动收短，**但永远留一份余量**（<see cref="SlackRatio"/>），
		/// 所以链子始终有自然的弧度与垂感，不会一直紧绷。
		/// 视觉件（链节/管件）的**尺寸不随长度变**，只有**件数**变 —— 否则看起来像被拉变形。
		/// </summary>
		public bool AutoLength = true;

		/// <summary>自动长度留的余量比例（1.05 = 永远比跨度长 5%，微微有弧；1 = 绷直）。</summary>
		public float SlackRatio = 1.05f;

		/// <summary>自动长度的跟随速度（每秒收敛比例；越大越"硬"，越小越有橡皮筋的黏滞感）。</summary>
		public float LengthFollowSpeed = 8f;

		/// <summary>自动长度的上下限（米）。上限也决定实体池的大小。
		/// 下限只是防退化（跨度极小时别把绳压成零长），别指望它"兜住余量"。
		/// 🔴 上限 24 = 覆盖钩索的瞄准射程（`GrappleLogic.AimRange` 默认 20 米 + 余量）——
		///   绳长被夹到比跨度短时走"绷直解析解"，绳仍会连到钩头，**但链节会被拉稀**（节距 &gt; 链环长）⇒
		///   射程内别让它发生。第 1 步的 16 米是当时"手到锚点"的合理上限，随射程一起抬了。</summary>
		public float MinLength = 0.3f;
		public float MaxLength = 24f;

		/// <summary>段的粗细倍率（1.0 = 管网格原直径 5cm）。</summary>
		public float RadiusScale = s_radiusScale;

		/// <summary>段长倍率：1 = 首尾相接（**默认**）· &gt;1 = 相邻段互相重叠。
		/// 🔴 2026-10-01 实机：配合"中点法"摆放，1.0 就够；调大可遮接缝，但**拐弯处两根直管会交叉**
		///   （每段比该占的位置长，方向一变就斜插过去）。</summary>
		public float Overlap = 1.0f;

		// 🔴 「整条弯曲管」（一个网格每帧 ClearMesh 重建顶点）2026-10-01 试过并**已删除**：
		//    实机完全画不出来（不报错、tick 正常、`EditDataFaceCornerCount` 也不为 0 却没画面），
		//    排掉过三个嫌疑（ClearMesh 与上锁的顺序 / HintVerticesDynamic / 挂载时机）仍不显示。
		//    结论：**运行时造网格可以用（管件就是这么造的），但"每帧清空重建"这条路不通**。
		//    改走铁链（离散刚体件），见 <see cref="ChainMode"/>。经过记在 plans\钩索-实施计划.md。

		// ─────────────── 会话级"记住"的外观设置（2026-10-01）───────────────
		// 每次进场景都会 new 一个 GrappleLogic + GrappleRope，玩家调好的外观不该每场重打一遍。
		// 只记"观感类"的开关（画法 / 粗细 / 材质 / 加密步长），不记跟具体绳子绑定的量（长度、锚点）。

		private static bool s_chainMode;
		private static float s_radiusScale = 0.6f;
		private static string s_matSource = "";
		private static float s_fineStep = 0.03f;

		/// <summary>记住"用铁链画法"（下一个场景自动生效）。</summary>
		public static void RememberChain(bool on) { s_chainMode = on; }

		/// <summary>记住粗细倍率。</summary>
		public static void RememberRadius(float v) { s_radiusScale = v; }

		/// <summary>记住材质借谁。</summary>
		public static void RememberMatSource(string mesh) { s_matSource = mesh ?? ""; }

		/// <summary>记住铺链加密步长。</summary>
		public static void RememberFineStep(float v) { s_fineStep = v; }

		/// <summary>
		/// **铁链模式**（2026-10-01 用户裁定，取代"整条弯曲管"）：绳 = 一串**竖长铁环**，
		/// 相邻环绕链轴**转 90°**（真实链条就是这样咬合的）。每个环是一个固定网格（运行时造一次、所有环共用），
		/// 按链长定节数 ⇒ 不存在"弯不弯"的问题，拐弯处也不会交叉。
		/// </summary>
		public bool ChainMode = s_chainMode;

		/// <summary>链节间距 = 链节长 × 它（越小咬得越紧；0.62 ≈ 真实链条的咬合量）。</summary>
		public float ChainPitchFactor = 0.62f;

		/// <summary>链节比例：半宽 / 半长（相对铁丝半径 r 的倍数）。竖长 = 半长 &gt; 半宽。</summary>
		public float ChainHalfWidthPerR = 1.6f;
		public float ChainHalfLengthPerR = 4.0f;

		/// <summary>铺链前把点链加密成光滑曲线的步长（米）。越小越顺、越费；0.03 ≈ 每 3cm 一个采样点。</summary>
		public float ChainFineStep = s_fineStep;

		/// <summary>速度阻尼（0.9~0.99：越小越快停，甩尾越短）。</summary>
		public float Damping = 0.94f;

		/// <summary>重力加速度（m/s²）。</summary>
		public float Gravity = 9.8f;

		/// <summary>距离约束的迭代次数（越多越"紧"，8 次已看不见拉伸）。</summary>
		public int Iterations = 8;

		/// <summary>贴地：把点压在地面之上（绳躺在地上的观感靠它）。</summary>
		public bool GroundClamp = true;

		/// <summary>贴地检测的间隔（秒）—— 地面查询有成本，别每帧每个点都问（默认 0.05s = 20Hz）。</summary>
		public float GroundInterval = 0.05f;

		/// <summary>定格：冻住点链不再模拟（只为**静止看形状/接缝**用，命令 `custom.grapple freeze 1`）。</summary>
		public bool Frozen;

		/// <summary>远端松手：不再钉住末端（绳自己掉下去）—— 验重力 / 后面收线要用。</summary>
		public bool FreeEnd;

		/// <summary>自造管造失败时，退回用原版元网格的第几件。</summary>
		public int MeshPart = 1;

		/// <summary>原版元网格名（仅供退回路径 + 借材质）。</summary>
		public string MeshName = DefaultMeshName;

		/// <summary>
		/// 材质从哪份网格借（空 = 借 <see cref="MeshName"/> 那件）。
		/// 🔴 我们自造的网格**形状是算出来的，质感是借来的** —— 想让铁环像铁，
		///   就指一件金属件（比如盔甲/武器网格名），不用开 ModKit。
		/// </summary>
		public string MaterialSourceMesh = s_matSource;

		public const string DefaultMeshName = "rope_stealth_mission_a";

		/// <summary>自造管的参数：直径（米）/ 沿轴纹理重复数。</summary>
		public float TubeDiameter = 0.05f;
		public float TubeUvRepeat = 3.6f;

		// ─────────────────────────────── 运行时状态 ───────────────────────────────

		private Scene _scene;
		private MetaMesh _mesh;
		private Mesh _tubeMesh;          // 分段管本体（所有段共用一份）
		private bool _tubeTried;
		private Mesh _linkMesh;          // 铁环网格（造一次，所有环共用）
		private GameEntity[] _linkEntities;
		private readonly List<Vec3> _finePts = new List<Vec3>(4096);   // 铺链用的光滑曲线采样（复用）
		private GameEntity[] _seg;
		private readonly GrappleRig _rig;    // 根实体（绳/钩/环 同属一个根，2026-10-08）
		private readonly string _tag;        // 这条绳叫什么（"A"/"B"）—— 日志里靠它区分

		/// <summary>
		/// **绳的事件日志开关**（建/拆实体、Show/Hide 各打一行 `[Rope] &lt;标签&gt; …`）。
		/// 🔴 2026-10-08 加：查 AccessViolation 时"崩在哪个事件之后"是唯一线索，而此前这些事件**一行都不打**。
		/// 事件本身稀疏（重建/显隐），默认开着不刷屏；嫌吵就 `custom.grapple ropelog 0`。
		/// </summary>
		public static bool LogEvents = true;

		/// <summary>构造：把"根实体"与**名字**传进来 —— 绳建的每个实体都会被收进根实体下面。</summary>
		internal GrappleRope(GrappleRig rig = null, string tag = "?")
		{
			_rig = rig;
			_tag = tag;
		}
		private string _boundSig;        // 实体当前绑的是哪份网格 / 哪种画法（变了就重建）
		private Vec3[] _pts;             // 长度 = 段数 + 1
		private Vec3[] _prev;            // 上一帧位置（verlet 用）
		private bool _visible;

		private readonly float[] _floorZ = new float[128];   // 每个点的地面高度缓存
		private float _groundTimer;
		private readonly Stopwatch _tickWatch = new Stopwatch();
		private float _tickMsAvg;        // 指数滑动平均（每帧模拟+画完的耗时）

		/// <summary>绳当前是否显示。</summary>
		public bool IsVisible => _visible;

		/// <summary>
		/// **每帧抓帧用的紧凑一行**（`custom.grapple spinlog`）：只放判"是不是绳这边藏了/没跟上"必需的几个数。
		/// `vis=` 是绳自己的显示标志（`Tick` 一开头就 `if (!_visible) return;` ⇒ 它一变 NO，整条绳当帧就不画了）；
		/// `n=` = 已建件数（链环/管段）· `len` / `span` = 绳长与两端跨度（`len ≈ span` = 绷直 · `len < span` = 会被拉直）。
		/// </summary>
		public string SpinFrameInfo()
		{
			int n = 0;
			try { n = (_linkEntities?.Length ?? _seg?.Length ?? 0); } catch (Exception) { }
			float span = 0f;
			try
			{
				if (_pts != null && _pts.Length >= 2)
				{
					span = (_pts[_pts.Length - 1] - _pts[0]).Length;
				}
			}
			catch (Exception)
			{
			}
			return $"vis={(_visible ? "yes" : "NO")} {(ChainMode ? "chain" : "seg")} n={n} len={Length:F2} span={span:F2}";
		}

		/// <summary>点数（= 段数 + 1）；还没建好时为 0。</summary>
		public int PointCount => _pts?.Length ?? 0;

		/// <summary>本帧实际建出来的段实体数（= 开销的直接来源）。</summary>
		public int LiveSegments => _seg?.Length ?? 0;

		/// <summary>最近每帧模拟 + 画完的平均耗时（毫秒）。</summary>
		public float TickMs => _tickMsAvg;

		/// <summary>网格没解析出来时的原因（给命令显示用）。</summary>
		public string LastError { get; private set; } = "";

		/// <summary>绳的总长永远等于 <see cref="Length"/>（跟有几段无关）。</summary>
		private float SegmentRestLength => Length / Math.Max(1, (_pts?.Length ?? 2) - 1);

		/// <summary>本帧实际用几段点：**固定分辨率**（4~120 封顶），不跟绳长联动。</summary>
		public int EffectiveSegments() => Math.Max(4, Math.Min(120, Segments));

		/// <summary>实体"绑的是哪份网格 + 哪种画法 + 什么材质"的签名（变了就必须重建）。</summary>
		private string BindSignature()
			=> (ChainMode ? "chain" : UseTubeSegment ? "tube" : "part" + MeshPart)
				+ "|" + MeshName + "|" + MaterialSourceMesh + "|" + RadiusScale.ToString("F3");

		/// <summary>分段模式是否用自造直管（造不出来会退回元网格）。</summary>
		private bool UseTubeSegment = true;

		// ─────────────────────────────── 建造 / 拆除 ───────────────────────────────

		/// <summary>建（或重建）绳本体。段数 / 画法 / 网格名任一变化都会重建。</summary>
		public bool Build(Scene scene)
		{
			if (scene == null)
			{
				LastError = "scene is null";
				return false;
			}

			int want = EffectiveSegments();
			string sig = BindSignature();
			bool sameShape = _scene == scene && _pts != null && _pts.Length == want + 1
				&& ((ChainMode && _linkEntities != null)
					|| (!ChainMode && _seg != null && _seg.Length == want))
				&& _boundSig == sig;
			if (sameShape)
			{
				LastError = "";
				return true;
			}

			Teardown();
			_scene = scene;
			_pts = new Vec3[want + 1];
			_prev = new Vec3[want + 1];

			if (LogEvents)
			{
				GameEntity[] arr = _linkEntities ?? _seg;
				string who;
				if (arr == null || arr.Length == 0)
				{
					who = "-";
				}
				else if (arr.Length <= 12)
				{
					who = string.Join(" ", Array.ConvertAll(arr, GrappleRig.Describe));   // 少就逐个列
				}
				else
				{
					who = $"{GrappleRig.Describe(arr[0])} … {GrappleRig.Describe(arr[arr.Length - 1])}（共 {arr.Length}）";
				}
				DebugLogger.Log($"[Rope] {_tag} 重建实体：段数={want} 模式={(ChainMode ? "chain" : "segments")}"
					+ $" 长度={Length:F2}m 半径={RadiusScale:F2} | {who}");
			}

			if (ChainMode)
			{
				if (!BuildChain(scene))
				{
					Teardown();
					return false;
				}
			}
			else if (!BuildSegments(scene, want))
			{
				Teardown();
				return false;
			}

			_boundSig = sig;
			LastError = "";
			return true;
		}

		/// <summary>分段模式：建 want 个实体。</summary>
		private bool BuildSegments(Scene scene, int want)
		{
			_seg = new GameEntity[want];
			for (int i = 0; i < want; i++)
			{
				GameEntity e = GameEntity.CreateEmpty(scene, true);
				if (e == null)
				{
					LastError = $"CreateEmpty failed at segment {i}";
					return false;
				}
				AttachSegmentMesh(e);
				e.SetVisibilityExcludeParents(false);
				GrappleRig.Name(e, $"lwn_{_tag}_seg{i:D2}");     // 自己的编号（日志/反查用）
				_rig?.Adopt(scene, e);
				_seg[i] = e;
			}
			return true;
		}

		/// <summary>铁环的铁丝半径（米）。</summary>
		private float LinkWireRadius => MathF.Max(0.002f, TubeDiameter * 0.5f * RadiusScale);

		/// <summary>链节间距（米）= 链节长 × <see cref="ChainPitchFactor"/>。</summary>
		private float ChainPitch()
			=> MathF.Max(0.01f, 2f * LinkWireRadius * ChainHalfLengthPerR * ChainPitchFactor);

		/// <summary>实体池要备几个环：按**最大长度**备（绳长每帧在变，池子不能跟着变）。
		/// 真正显示几个由每帧铺到哪算 —— 多的藏起来，不重建。</summary>
		private int ChainLinkCount()
			=> Math.Max(2, Math.Min(400, (int)MathF.Ceiling((AutoLength ? MaxLength : Length) / ChainPitch()) + 2));

		/// <summary>铁链模式：建 <see cref="ChainLinkCount"/> 个环实体（环网格共用一份）。</summary>
		private bool BuildChain(Scene scene)
		{
			_linkMesh = EnsureLinkMesh();
			if (_linkMesh == null)
			{
				if (string.IsNullOrEmpty(LastError)) LastError = "link mesh build failed";
				return false;
			}
			int count = ChainLinkCount();
			_linkEntities = new GameEntity[count];
			for (int i = 0; i < count; i++)
			{
				GameEntity e = GameEntity.CreateEmpty(scene, true);
				if (e == null)
				{
					LastError = $"CreateEmpty failed at link {i}";
					return false;
				}
				e.AddMesh(_linkMesh);
				e.SetVisibilityExcludeParents(false);
				GrappleRig.Name(e, $"lwn_{_tag}_link{i:D3}");    // 自己的编号（日志/反查用）
				_rig?.Adopt(scene, e);
				_linkEntities[i] = e;
			}
			return true;
		}

		/// <summary>
		/// 造一个**竖长铁环**（只造一次，所有环共用）：
		/// 中线 = 体育场形（两条直边 + 两个半圆），躺在**局部 XZ 平面**、长轴 = **局部 Z**；
		/// 铁丝是半径 r 的圆截面，绕中线一圈闭合。尺寸全部由 <see cref="TubeDiameter"/> × <see cref="RadiusScale"/> 派生。
		/// </summary>
		private Mesh EnsureLinkMesh()
		{
			if (_linkMesh != null) return _linkMesh;
			try
			{
				Mesh m = CreateEmptyEditableMesh();
				if (m == null)
				{
					LastError = "CreateMesh returned null (link)";
					return null;
				}

				float r = LinkWireRadius;
				float b = r * ChainHalfWidthPerR;      // 半宽（局部 X）
				float a = r * ChainHalfLengthPerR;     // 半长（局部 Z）；a > b = 竖长
				const int steps = 14;                  // 沿中线取几个截面
				const int sides = 6;                   // 绕铁丝几边
				float straight = 2f * (a - b);
				float arc = MathF.PI * b;
				float total = 2f * straight + 2f * arc;

				var centers = new Vec3[steps];
				var tangents = new Vec3[steps];
				for (int k = 0; k < steps; k++)
				{
					float d = total * k / steps;
					if (d < straight)
					{
						centers[k] = new Vec3(b, 0f, -(a - b) + d);
						tangents[k] = new Vec3(0f, 0f, 1f);
					}
					else if (d < straight + arc)
					{
						float phi = (d - straight) / b;                 // 0..π
						centers[k] = new Vec3(b * MathF.Cos(phi), 0f, (a - b) + b * MathF.Sin(phi));
						tangents[k] = new Vec3(-MathF.Sin(phi), 0f, MathF.Cos(phi));
					}
					else if (d < 2f * straight + arc)
					{
						float s = d - straight - arc;
						centers[k] = new Vec3(-b, 0f, (a - b) - s);
						tangents[k] = new Vec3(0f, 0f, -1f);
					}
					else
					{
						float phi = MathF.PI + (d - 2f * straight - arc) / b;   // π..2π
						centers[k] = new Vec3(b * MathF.Cos(phi), 0f, -(a - b) + b * MathF.Sin(phi));
						tangents[k] = new Vec3(-MathF.Sin(phi), 0f, MathF.Cos(phi));
					}
				}

				// 每个截面的铁丝圆环（在垂直于中线的平面内）
				var rings = new Vec3[steps][];
				var uvRings = new Vec2[steps][];
				Vec3 planeNormal = new Vec3(0f, 1f, 0f);   // 中线躺在 XZ 面 ⇒ 面法线 = ±Y
				for (int k = 0; k < steps; k++)
				{
					Vec3 t = tangents[k];
					if (t.LengthSquared < 1e-8f) t = new Vec3(0f, 0f, 1f);
					t.Normalize();
					Vec3 n2 = Vec3.CrossProduct(t, planeNormal);
					n2.Normalize();
					rings[k] = new Vec3[sides + 1];
					uvRings[k] = new Vec2[sides + 1];
					for (int s = 0; s <= sides; s++)
					{
						float th = s / (float)sides * MathF.PI * 2f;
						rings[k][s] = centers[k] + (planeNormal * MathF.Cos(th) + n2 * MathF.Sin(th)) * r;
						uvRings[k][s] = new Vec2(s / (float)sides, k / (float)steps);
					}
				}

				uint white = 0xFFFFFFFFu;
				UIntPtr h = m.LockEditDataWrite();
				for (int k = 0; k < steps; k++)
				{
					int k2 = (k + 1) % steps;   // 闭合：最后一圈接回第 0 圈
					for (int s = 0; s < sides; s++)
					{
						Vec3 A = rings[k][s], B = rings[k][s + 1], C = rings[k2][s + 1], D = rings[k2][s];
						Vec2 ua = uvRings[k][s], ub = uvRings[k][s + 1], uc = uvRings[k2][s + 1], ud = uvRings[k2][s];
						m.AddTriangle(A, B, C, ua, ub, uc, white, h);
						m.AddTriangle(A, C, D, ua, uc, ud, white, h);
					}
				}
				m.UnlockEditDataWrite(h);
				m.ComputeNormals();
				m.UpdateBoundingBox();
				Material mat = BorrowRopeMaterial();
				if (mat != null) m.SetMaterial(mat);
				m.SetVisibilityMask(VisibilityMaskFlags.Final);
				_linkMesh = m;
				return _linkMesh;
			}
			catch (Exception ex)
			{
				LastError = "link mesh build failed: " + ex.GetType().Name + " " + ex.Message;
				_linkMesh = null;
				return null;
			}
		}

		/// <summary>
		/// 沿**光滑曲线**按弧长铺环：第 i 个环摆在"弧长 i × 间距"处，相邻环转 90°。
		/// 🔴 必须先把点链**加密成光滑曲线**（<see cref="BuildSmoothPath"/>）再铺 ——
		///   直接铺在模拟折线上的话，一个折线区间内会塞 3~4 个环并严格共线，走到顶点方向突变，
		///   看起来就是"4 个环一组不会折、第 5 个突然折"（2026-10-01 实机反馈）。
		/// </summary>
		private void ApplyChain()
		{
			if (_linkEntities == null || _pts == null) return;
			BuildSmoothPath();
			int m = _finePts.Count;
			if (m < 2) return;

			float pitch = ChainPitch();
			int placed = 0;
			float acc = 0f;
			float target = 0f;
			for (int i = 0; i < m - 1 && placed < _linkEntities.Length; i++)
			{
				Vec3 a = _finePts[i];
				Vec3 b = _finePts[i + 1];
				Vec3 d = b - a;
				float segLen = d.Length;
				if (segLen < 1e-5f) continue;
				while (placed < _linkEntities.Length && target <= acc + segLen)
				{
					float t = (target - acc) / segLen;
					PlaceLink(placed, a + d * t, d * (1f / segLen));
					placed++;
					target += pitch;
				}
				acc += segLen;
			}
			for (int k = placed; k < _linkEntities.Length; k++)
			{
				try { _linkEntities[k]?.SetVisibilityExcludeParents(false); } catch (Exception) { }
			}
		}

		/// <summary>把模拟点链用 Catmull-Rom 加密成一条光滑折线（每个模拟区间切 <see cref="ChainFineStep"/> 米一小段）。
		/// 缓冲区复用，不每帧新建。</summary>
		private void BuildSmoothPath()
		{
			_finePts.Clear();
			int n = _pts.Length;
			if (n < 2) return;
			float step = MathF.Max(0.01f, ChainFineStep);
			for (int i = 0; i < n - 1; i++)
			{
				Vec3 p0 = _pts[Math.Max(0, i - 1)];
				Vec3 p1 = _pts[i];
				Vec3 p2 = _pts[i + 1];
				Vec3 p3 = _pts[Math.Min(n - 1, i + 2)];
				float segLen = (p2 - p1).Length;
				int k = Math.Max(1, Math.Min(64, (int)MathF.Ceiling(segLen / step)));
				for (int j = 0; j < k; j++)
				{
					_finePts.Add(CatmullRom(p0, p1, p2, p3, j / (float)k));
				}
			}
			_finePts.Add(_pts[n - 1]);
		}

		/// <summary>标准 Catmull-Rom：过 p1 → p2，p0/p3 只提供切线。</summary>
		private static Vec3 CatmullRom(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3, float t)
		{
			float t2 = t * t;
			float t3 = t2 * t;
			return (p1 * 2f
				+ (p2 - p0) * t
				+ (p0 * 2f - p1 * 5f + p2 * 4f - p3) * t2
				+ (p1 * 3f - p0 - p2 * 3f + p3) * t3) * 0.5f;
		}

		/// <summary>摆一个环：长轴对准链方向；奇数号绕长轴转 90°（真实链条的咬合方式）。</summary>
		private void PlaceLink(int i, Vec3 p, Vec3 dir)
		{
			GameEntity e = _linkEntities[i];
			if (e == null) return;
			try
			{
				Mat3 basis = BasisWithLocalZ(dir);
				if ((i & 1) == 1)
				{
					// 绕局部 Z 转 90°：交换 s / f，并翻转一个以保持右手系（s×f = u）
					Vec3 s = basis.s;
					basis.s = basis.f;
					basis.f = -s;
				}
				_rig?.Place(e, new MatrixFrame(basis, p));
				if (_rig == null) { e.SetGlobalFrame(new MatrixFrame(basis, p)); }
				e.SetVisibilityExcludeParents(true);
			}
			catch (Exception)
			{
				// 单个环摆失败不拖垮其余环
			}
		}

		/// <summary>给一个段实体挂网格：优先自造直管，失败退回原版元网格的第 <see cref="MeshPart"/> 件。</summary>
		private void AttachSegmentMesh(GameEntity e)
		{
			Mesh tube = EnsureTubeMesh();
			if (tube != null)
			{
				UseTubeSegment = true;
				e.AddMesh(tube);
				return;
			}

			UseTubeSegment = false;
			try
			{
				if (_mesh == null) _mesh = SpellWorld.ResolveMesh(MeshName);
				if (_mesh != null)
				{
					int part = Math.Max(0, Math.Min(_mesh.MeshCount - 1, MeshPart));
					Mesh sub = _mesh.GetMeshAtIndex(part);
					if (sub != null)
					{
						e.AddMesh(sub);
						return;
					}
				}
			}
			catch (Exception ex)
			{
				LastError = "attach failed: " + ex.GetType().Name;
				return;
			}
			LastError = _mesh == null ? $"mesh '{MeshName}' not found" : $"mesh part {MeshPart} not found";
		}

		/// <summary>
		/// 借一份现成的材质（共享，不改它）：来源 = <see cref="MaterialSourceMesh"/>（空则用 <see cref="MeshName"/>）。
		/// 拿不到就返回 null —— 引擎会用默认材质（白板），但形状照画。
		/// </summary>
		private Material BorrowRopeMaterial()
		{
			try
			{
				string src = string.IsNullOrEmpty(MaterialSourceMesh) ? MeshName : MaterialSourceMesh;
				MetaMesh mm = SpellWorld.ResolveMesh(src);
				if (mm != null && mm.MeshCount > 0)
				{
					int part = Math.Max(0, Math.Min(mm.MeshCount - 1, MeshPart));
					return mm.GetMeshAtIndex(part)?.GetMaterial();
				}
			}
			catch (Exception)
			{
				// 材质借不到不算失败
			}
			return null;
		}

		/// <summary>材质来源变了 ⇒ 已造好的网格必须重造（材质是造网格那一刻设上去的）。</summary>
		public void InvalidateBuiltMeshes()
		{
			_tubeMesh = null;
			_tubeTried = false;
			_linkMesh = null;
		}

		/// <summary>
		/// 自造一根"单位直管"（只造一次，所有段共用）：沿局部 **+Z 从 0 长到 1**、
		/// 直径 <see cref="TubeDiameter"/>、8 边、带 UV 和端盖。
		/// </summary>
		private Mesh EnsureTubeMesh()
		{
			if (_tubeTried) return _tubeMesh;
			_tubeTried = true;
			try
			{
				Mesh m = CreateEmptyEditableMesh();
				if (m == null) return null;

				const int sides = 8;
				float r = MathF.Max(0.002f, TubeDiameter * 0.5f);
				uint white = 0xFFFFFFFFu;
				UIntPtr h = m.LockEditDataWrite();

				Vec3 RingPoint(int s, float z, out Vec2 uv)
				{
					float t = s / (float)sides;
					float ang = t * MathF.PI * 2f;
					uv = new Vec2(t, z * TubeUvRepeat);
					return new Vec3(MathF.Cos(ang) * r, MathF.Sin(ang) * r, z);
				}

				Vec2 uvA, uvB, uvC, uvD;
				Vec3 c0a = new Vec3(0f, 0f, 0f), c0b = new Vec3(0f, 0f, 1f);
				Vec2 uvCa = new Vec2(0.5f, 0f), uvCb = new Vec2(0.5f, TubeUvRepeat);

				for (int s = 0; s < sides; s++)
				{
					Vec3 a = RingPoint(s, 0f, out uvA);
					Vec3 b = RingPoint((s + 1) % sides, 0f, out uvB);
					Vec3 c = RingPoint((s + 1) % sides, 1f, out uvC);
					Vec3 d = RingPoint(s, 1f, out uvD);
					m.AddTriangle(a, b, c, uvA, uvB, uvC, white, h);   // 侧面（绕序朝外）
					m.AddTriangle(a, c, d, uvA, uvC, uvD, white, h);
					m.AddTriangle(c0a, b, a, uvCa, uvB, uvA, white, h);   // 尾盖
					m.AddTriangle(c0b, d, c, uvCb, uvD, uvC, white, h);   // 头盖
				}

				m.UnlockEditDataWrite(h);
				m.ComputeNormals();
				m.UpdateBoundingBox();
				Material mat = BorrowRopeMaterial();
				if (mat != null) m.SetMaterial(mat);
				m.SetVisibilityMask(VisibilityMaskFlags.Final);
				_tubeMesh = m;
				return _tubeMesh;
			}
			catch (Exception ex)
			{
				_lastTubeError = "tube build failed: " + ex.GetType().Name + " " + ex.Message;
				_tubeMesh = null;
				return null;
			}
		}

		private string _lastTubeError = "";

		private static Mesh CreateEmptyEditableMesh()
		{
			return Mesh.CreateMesh(true);
		}

		/// <summary>拆掉全部实体（自造网格留着复用）。</summary>
		public void Teardown()
		{
			// 🔴 事件日志（2026-10-08）：拆实体是 AV 的头号嫌疑（挂进根实体后销毁 = 可能的悬空子件），
			//    所以"拆了几个"必须留下痕迹。放在真正动手之前打 —— 崩了也能看到"它正准备拆"。
			if (LogEvents && (_seg != null || _linkEntities != null))
			{
				GameEntity[] arr = _linkEntities ?? _seg;
				string who = (arr == null || arr.Length == 0) ? "-"
					: (arr.Length <= 12 ? string.Join(" ", Array.ConvertAll(arr, GrappleRig.Describe))
						: $"{GrappleRig.Describe(arr[0])} … {GrappleRig.Describe(arr[arr.Length - 1])}（共 {arr.Length}）");
				DebugLogger.Log($"[Rope] {_tag} 销毁实体：段={_seg?.Length ?? 0} 环={_linkEntities?.Length ?? 0}"
					+ $" 根子树={(_rig != null ? _rig.ChildCount.ToString() : "n/a")} | {who}");
			}
			_visible = false;
			if (_seg != null)
			{
				for (int i = 0; i < _seg.Length; i++)
				{
					try { _seg[i]?.Remove(0); } catch (Exception) { }
				}
			}
			if (_linkEntities != null)
			{
				for (int i = 0; i < _linkEntities.Length; i++)
				{
					try { _linkEntities[i]?.Remove(0); } catch (Exception) { }
				}
			}
			_seg = null;
			_linkEntities = null;
			_pts = null;
			_prev = null;
			_scene = null;
			_visible = false;

			// 🔴 拆完再打一次根子树数量 —— 与上面那条"拆前"的数一比，就是"引擎会不会解链"的判据：
			//    变少 = 会解链 ✓（悬挂嫌疑排除）· 没变 = **悬空子件坐实** ✗（必须补显式摘链或改池子）。
			if (LogEvents && _rig != null)
			{
				DebugLogger.Log($"[Rope] {_tag} 销毁完成：根子树={_rig.ChildCount}（与上面那条「拆前」对比）");
			}
		}

		// ─────────────────────────────── 显示 / 隐藏 ───────────────────────────────

		/// <summary>激活绳子并把点沿「起点 → 终点」铺开（不摆动，干净的初始态）。</summary>
		public void Show(Vec3 from, Vec3 to)
		{
			if (_pts == null) return;
			int n = _pts.Length;
			for (int i = 0; i < n; i++)
			{
				float t = i / (float)(n - 1);
				_pts[i] = from + (to - from) * t;
				_prev[i] = _pts[i];
			}
			_pts[0] = from;
			_pts[n - 1] = to;
			_visible = true;
			_groundTimer = 0f;
			if (LogEvents)
			{
				DebugLogger.Log($"[Rope] {_tag} Show：{from.x:F2},{from.y:F2},{from.z:F2} → {to.x:F2},{to.y:F2},{to.z:F2}"
					+ $" 跨度={(to - from).Length:F2}m 段数={n - 1}");
			}
			Draw();
		}

		/// <summary>藏起来（保留实体，下次 Show 复用）。</summary>
		/// <summary>🔴 调用方要判"绳现在显示着吗"一律用**这个**（第 184 行那个属性的权威说明）：
		/// 别只信自己记的"我 Show 过了"标志 —— `Release()`/`Anchor()` 等路径会绕过标志直接 `Hide()`，
		/// 标志卡在 true ⇒ 之后只 Tick 不 Show（Tick 在隐藏时直接 return）⇒ **绳永久不出现**（2026-10-08 实机）。
		/// </summary>
		public void Hide()
		{
			if (!_visible) return;
			if (LogEvents)
			{
				DebugLogger.Log($"[Rope] {_tag} Hide（隐藏实体，不销毁）");
			}
			_visible = false;
			SetEntitiesVisible(false);
		}

		private void SetEntitiesVisible(bool on)
		{
			if (_seg != null)
			{
				for (int i = 0; i < _seg.Length; i++)
				{
					try { _seg[i]?.SetVisibilityExcludeParents(on); } catch (Exception) { }
				}
			}
			if (_linkEntities != null)
			{
				for (int i = 0; i < _linkEntities.Length; i++)
				{
					try { _linkEntities[i]?.SetVisibilityExcludeParents(on); } catch (Exception) { }
				}
			}
		}

		// ─────────────────────────────── 每帧 ───────────────────────────────

		/// <summary>每帧：verlet 积分 → 距离约束 → 贴地 → 画。</summary>
		public void Tick(float dt, Vec3 from, Vec3 to)
		{
			if (!_visible || _pts == null) return;
			int n = _pts.Length;
			if (n < 2) return;

			_tickWatch.Restart();

			if (Frozen)
			{
				Draw();
				_tickWatch.Stop();
				return;
			}

			if (dt <= 0f) dt = 1f / 60f;
			if (dt > 1f / 30f) dt = 1f / 30f;   // 卡帧时钳住，防止一步炸开

			// ⓪ 橡皮筋：绳长跟着"手到锚点的跨度"走 —— 走远被拽长、走近自动收短，**但永远留一份余量**
			//    （不照实缩短，否则会一直是紧绷状态）。视觉件尺寸不变、**件数**随之增减。
			// 🔴 口径就是 `跨度 × SlackRatio`，**别再偷偷加固定余量**（2026-10-01 用户实测抓到：
			//   原来写的是 max(span×slack, span+0.25) ⇒ `slack 1` 也绷不直，玩家一眼看出不对）。
			//   想绷直就把 slack 调到 1（或者干脆用 `len` 固定长度）。
			if (AutoLength && !FreeEnd)
			{
				float spanNow = (to - from).Length;
				float want = MathF.Max(MinLength, MathF.Min(MaxLength, spanNow * SlackRatio));
				Length += (want - Length) * MathF.Min(1f, MathF.Max(0.05f, LengthFollowSpeed) * dt);
			}

			float dt2 = dt * dt;
			float g = -Gravity * dt2;           // 引擎 Z 轴朝上

			// 🔴 **绷直态走解析解**（2026-10-01 用户实测抓到"slack 1 还是有弧"）：
			//   绳长 ≤ 跨度时，物理上绳子**只能是直线**（没有多余长度可垂）；但 verlet 在
			//   "约束刚好拉满"的情况下永远解不干净（两端钉死 + 重力下拉，8 次迭代后必然剩一点弧）。
			//   所以这种状态直接把点均匀铺在两端连线上 —— 既正确又是解析解，零残余。
			float spanStraight = (to - from).Length;
			if (!FreeEnd && spanStraight >= Length * 0.995f)
			{
				float inv = 1f / (n - 1);
				Vec3 delta = to - from;
				for (int i = 0; i < n; i++)
				{
					_pts[i] = from + delta * (i * inv);
					_prev[i] = _pts[i];      // 清速度：免得离开绷直态时甩一下
				}
				if (GroundClamp)
				{
					_groundTimer -= dt;
					if (_groundTimer <= 0f)
					{
						_groundTimer = MathF.Max(0.01f, GroundInterval);
						SampleFloors();
					}
					ApplyFloors();
				}
				Draw();
				_tickWatch.Stop();
				float msTaut = (float)_tickWatch.Elapsed.TotalMilliseconds;
				_tickMsAvg = _tickMsAvg <= 0f ? msTaut : _tickMsAvg * 0.95f + msTaut * 0.05f;
				return;
			}

			// ① 惯性积分（被钉住的端点跳过；FreeEnd 时末端也参与）
			int lastFree = FreeEnd ? n : n - 1;
			for (int i = 1; i < lastFree; i++)
			{
				Vec3 vel = (_pts[i] - _prev[i]) * Damping;
				_prev[i] = _pts[i];
				_pts[i] = _pts[i] + vel + new Vec3(0f, 0f, g);
			}

			// ② 端点是硬约束（FreeEnd 时不钉远端）
			_pts[0] = from;
			if (!FreeEnd) _pts[n - 1] = to;

			// ③ 距离约束迭代
			float rest = SegmentRestLength;
			for (int it = 0; it < Iterations; it++)
			{
				for (int i = 0; i < n - 1; i++)
				{
					SolveDistance(i, i + 1, rest);
				}
				_pts[0] = from;
				if (!FreeEnd) _pts[n - 1] = to;
			}

			// ④ 贴地（限频）
			if (GroundClamp)
			{
				_groundTimer -= dt;
				if (_groundTimer <= 0f)
				{
					_groundTimer = MathF.Max(0.01f, GroundInterval);
					SampleFloors();
				}
				ApplyFloors();
			}

			Draw();

			_tickWatch.Stop();
			float ms = (float)_tickWatch.Elapsed.TotalMilliseconds;
			_tickMsAvg = _tickMsAvg <= 0f ? ms : _tickMsAvg * 0.95f + ms * 0.05f;
		}

		/// <summary>把 i、j 两点的距离拉回 rest（经典 verlet 距离约束，各分摊一半）。</summary>
		private void SolveDistance(int i, int j, float rest)
		{
			Vec3 d = _pts[j] - _pts[i];
			float dist = d.Length;
			if (dist < 1e-5f) return;
			float diff = (dist - rest) / dist * 0.5f;
			Vec3 corr = d * diff;
			_pts[i] = _pts[i] + corr;
			_pts[j] = _pts[j] - corr;
		}

		/// <summary>
		/// 逐点问"这个位置的地面高度"（限频调用）。
		/// 🔴 **用 `Scene.GetGroundHeightAtPositionMT`，不是 `GetTerrainHeight`**：
		///   后者是**高度图**——城镇/城堡里脚下是**网格铺装**，高度图在铺装之下（实测：绳原地陷进石板路）。
		///   前者是引擎的"地面查询"（地表系统 `SurfaceDecalFx.GroundZ` 同款：**带上探针高度、从上方往下找**），
		///   铺装/地形/物件都能落上去；返回值可能是 NaN 或 ±1e5 这种哨兵 ⇒ 一律当"悬空"处理。
		/// </summary>
		private void SampleFloors()
		{
			if (_scene == null || _pts == null) return;
			int n = Math.Min(_pts.Length, _floorZ.Length);
			for (int i = 0; i < n; i++)
			{
				Vec3 p = _pts[i];
				try
				{
					float z = _scene.GetGroundHeightAtPositionMT(new Vec3(p.x, p.y, p.z + 2f),
						BodyFlags.CommonCollisionExcludeFlags);
					_floorZ[i] = (float.IsNaN(z) || z > 1e5f || z < -1e5f) ? float.NegativeInfinity : z;
				}
				catch (Exception)
				{
					_floorZ[i] = float.NegativeInfinity;
				}
			}
		}

		/// <summary>把点压到"上一次采样到的地面"之上（两次采样之间沿用旧值）。</summary>
		private void ApplyFloors()
		{
			if (_pts == null) return;
			float minGap = 0.015f + TubeDiameter * 0.5f * RadiusScale;
			int n = Math.Min(_pts.Length, _floorZ.Length);
			for (int i = 0; i < n; i++)
			{
				float floor = _floorZ[i];
				if (float.IsNegativeInfinity(floor)) continue;
				float want = floor + minGap;
				if (_pts[i].z < want)
				{
					_pts[i] = new Vec3(_pts[i].x, _pts[i].y, want);
				}
			}
		}

		/// <summary>按当前画法把绳画出来。</summary>
		private void Draw()
		{
			if (ChainMode) ApplyChain();
			else ApplyFrames();
		}

		/// <summary>
		/// 分段画法（**中点法**）：段 i 从「点 i-1 与点 i 的中点」画到「点 i 与点 i+1 的中点」，
		/// 朝向取这两中点的连线 ⇒ 相邻段共享端点、方向连续，拐弯处不会交叉。
		/// </summary>
		private void ApplyFrames()
		{
			if (_seg == null || _pts == null) return;
			int segCount = Math.Min(_seg.Length, _pts.Length - 1);
			if (segCount <= 0) return;
			int n = _pts.Length;

			float meshLen = UseTubeSegment ? 1f : 18.412f;    // 自造管 1 单位长 / 原版绳 18.41 米
			float zScaleBase = Overlap / MathF.Max(0.001f, meshLen);
			float backOff = (Overlap - 1f) * 0.5f;
			float dirSign = UseTubeSegment ? 1f : -1f;         // 自造管朝 +Z 长、原版绳朝 -Z 长

			for (int i = 0; i < segCount; i++)
			{
				GameEntity e = _seg[i];
				if (e == null) continue;

				Vec3 a = SegmentEdge(i);        // 左端（点 i-1 与 i 的中点）
				Vec3 b = SegmentEdge(i + 1);    // 右端（点 i 与 i+1 的中点）
				Vec3 d = b - a;
				float len = d.Length;
				if (len < 1e-4f)
				{
					try { e.SetVisibilityExcludeParents(false); } catch (Exception) { }
					continue;
				}
				try
				{
					Vec3 dir = d * (1f / len);
					MatrixFrame frame = new MatrixFrame(
						BasisWithLocalZ(dir * dirSign),
						a - dir * (len * backOff));
					frame.Scale(new Vec3(RadiusScale, RadiusScale, len * zScaleBase));
					_rig?.Place(e, frame);
					if (_rig == null) { e.SetGlobalFrame(frame); }
					e.SetVisibilityExcludeParents(true);
				}
				catch (Exception)
				{
					// 单个段摆失败不拖垮其余段
				}
			}
			_ = n;
		}

		/// <summary>段端点：<paramref name="edge"/> = 0 返回首点，= n 返回末点，其余返回「点 edge-1 与点 edge 的中点」。</summary>
		private Vec3 SegmentEdge(int edge)
		{
			int n = _pts.Length;
			if (edge <= 0) return _pts[0];
			if (edge >= n) return _pts[n - 1];
			return (_pts[edge - 1] + _pts[edge]) * 0.5f;
		}

		/// <summary>
		/// 造一个"**局部 Z 轴对准 <paramref name="dir"/>**"的正交基底。
		///
		/// 🔴 为什么不用 <c>Mat3.CreateMat3WithForward</c>（2026-10-01 实机栽过）：那个函数把方向写进
		///   **`f`（局部 Y 轴）**，而我们的管网格是沿**局部 Z** 长的 ⇒ 长度轴被甩到别处，管子全立起来。
		///   Mat3 的轴对应关系（`MatrixFrame.Scale` 的 `s.x/f.y/u.z` 实锤）：**s = 局部 X · f = 局部 Y · u = 局部 Z**。
		/// （`internal`：钩头实体摆位也用它 —— 唯一实现，别再抄一份。）
		/// </summary>
		internal static Mat3 BasisWithLocalZ(Vec3 dir)
		{
			Mat3 m = Mat3.Identity;
			m.u = dir;
			m.f = MathF.Abs(dir.z) < 0.99f ? new Vec3(0f, 0f, 1f) : new Vec3(0f, 1f, 0f);
			m.s = Vec3.CrossProduct(m.f, m.u);
			m.s.Normalize();
			m.f = Vec3.CrossProduct(m.u, m.s);
			m.f.Normalize();
			return m;
		}

		// ─────────────────────────────── 诊断 ───────────────────────────────

		/// <summary>
		/// 一行状态（`custom.grapple dump`）。**单行** —— 控制台里换行会串版。
		/// 开头四个数是给"判断绷紧还是松弛"用的：
		///   · <c>length</c> = 绳自身的长度（米）· <c>span</c> = 手到锚点的直线距离
		///   · <c>slack</c> = length − span（&gt;0 = 有余量、该垂下来；≈0 = 正好拉满）
		///   · <c>sag</c> = 绳中点比"两端直线"低了多少（**正数 = 真被重力压弯了**）
		/// </summary>
		public string Status()
		{
			string vis = _visible ? "" : "HIDDEN ";     // 🔴 诊断：绳是不是根本没被 Show 出来（用户 2026-10-08 "看不见了"）
			string mode = ChainMode
				? (_linkMesh != null ? $"chain(links={_linkEntities?.Length ?? 0},pitch={ChainPitch():F3}m,wire={LinkWireRadius * 100f:F1}cm)" : "chain(FAILED)")
				: (UseTubeSegment ? "tube-segments" : $"part{MeshPart}");
			if (_pts == null)
			{
				return $"not built | mode={mode} mesh={MeshName} err={(string.IsNullOrEmpty(LastError) ? _lastTubeError : LastError)}";
			}			Vec3 a = _pts[0];
			Vec3 b = _pts[_pts.Length - 1];
			float span = (b - a).Length;
			float slack = Length - span;
			int mid = _pts.Length / 2;
			float sag = (a + b).z * 0.5f - _pts[mid].z;
			string taut = FreeEnd ? "FREE" : (span >= Length * 0.98f ? "TAUT" : "slack");
			return $"{vis}length={Length:F2}m{(AutoLength ? "(auto)" : "")} span={span:F2}m slack={slack:F2}m sag={sag:F2}m [{taut}]"
				+ $" | segments={_pts.Length - 1} radius={RadiusScale:F2} "
				+ $"overlap={Overlap:F2} shape={mode}{(Frozen ? " FROZEN" : "")} ground={GroundClamp} tick={_tickMsAvg:F2}ms"
				+ $" | hand=({a.x:F1},{a.y:F1},{a.z:F1}) anchor=({b.x:F1},{b.y:F1},{b.z:F1})";
		}
	}
}
