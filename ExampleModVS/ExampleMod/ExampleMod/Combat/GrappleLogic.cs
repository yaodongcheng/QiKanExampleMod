using System;
using LivingWorldNpcs.Animation;
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
			Returning,  // 🔴 打空返程（2026-10-08）：钩头追着手飞回，到了交回手里那套
		}

		/// <summary>钩索正在忙（钩头在飞 / 已挂住 / 拉拽中）—— 瞄准相机用它决定
		/// "瞄准结束但流程未完（已开火、拉拽相机还没接手）时先别还相机"（见 GrappleAimCamera.Tick）。</summary>
		public bool IsBusy => _hookPhase != HookPhase.Idle;

		/// <summary>
		/// **根实体**（钩 / 绳 / 左手环 同属它的子级 —— 用户 2026-10-08 要求"严格的父子关系"）。
		/// 🔴 必须先于 <see cref="_rope"/> / <see cref="_hook"/> 初始化（字段初始化按声明顺序跑，构造里要用它）。
		/// </summary>
		private readonly GrappleRig _rig = new GrappleRig();

		private readonly GrappleRope _rope;
		private bool _anchored;
		private Vec3 _anchor;

		/// <summary>
		/// **左手那截绳（A：左手环 → 右手）** —— 用户 2026-10-08 定的拓扑：
		/// **左手环 + 绳A一端 · 右手（不可见节点）+ 绳A另一端 + 绳B一端 · 钩 = 绳B另一端**；
		/// 发射时飞出去的是 **B**（= <see cref="_rope"/>，与手里甩的那条**同一条**——用户要求"射出去的必须是手里那条"）。
		/// 🔴 从 2026-10-07 起绳的近端一直在**右手**（`GetRopeAnchor`）——那是因为当时裸读左手骨崩过一次而回退；
		///    现在左手环已由我们自绘（<see cref="GrappleRig.RingPosition"/> 每帧可读）⇒ A 可以真的系在环上。
		/// </summary>
		private readonly GrappleRope _ropeA;
		private bool _ropeAShown;

		// ── 钩头模式（步骤 2）──
		private readonly GrappleHook _hook;

		public GrappleLogic()
		{
			_rope = new GrappleRope(_rig, "B");
			_ropeA = new GrappleRope(_rig, "A");
			_hook = new GrappleHook(_rig);

			// A 的配置：短、自动跟长（两只手分开就拉长）、不甩 —— 它就是"握在手里的那一截"
			_ropeA.AutoLength = true;
			_ropeA.FreeEnd = false;
			_ropeA.MinLength = 0.15f;

			// 🔴 **2026-10-08 用户要求：手里甩的那截绳再细一半** ——
			//    A、B 都减半（B 是同一条，所以飞行时也细了；要飞行时恢复粗的就改这一行）。
			//    `RadiusScale` 是实例级、初始化自静态默认（`radius` 命令的那个）⇒ 这里做乘法，不动静态值。
			_rope.RadiusScale *= 0.5f;
			_ropeA.RadiusScale *= 0.5f;
		}
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

		// ── 姿态动画（2026-10-04；定义 = `ModuleData/statemachines/grapple.xml`）──

		/// <summary>动画总开关（命令 `custom.grapple anim`）：**关 = 回"复用弓动画"的旧行为**（对照用）。</summary>
		public static bool AnimEnabled = true;

		/// <summary>
		/// 位移起点钉在**开火后多少秒**（fire = 松手那一刻 ⇒ pull 段内"源帧 34"）——
		/// 🔴 **2026-10-07 晚重切后 = 1.133**（= 新 release 全长 **0.566** + pull 段内偏移 0.567；
		/// pull clip 只做了镜像、时长与内部节拍未变 ⇒ 偏移不变）。
		/// 蓄势 = 它 − 本钩飞行耗时（见 <see cref="TickHook"/> 的自动拉拽段）。
		/// </summary>
		public static float PullStartSeconds = 1.133f;

		/// <summary>
		/// **release 动作的长度**（秒，= 内容包 clip `grapple_ground_release` 的时长；**2026-10-07 晚重切后 = 0.566 s**）：
		/// 开火后过这么久，引擎的甩出动作演完 ⇒ C# 把状态机送进"过程"（pull 段无缝接上）。
		/// ⚠️ **用户每重切一次动画，这个数就要跟着改**（历次：0.333 → 1.167 → **0.566**）。
		/// </summary>
		public static float ReleaseSeconds = 0.566f;

		// ── 手里待命（设计 B，2026-10-07）：钩索拿在手上 = 钩头实体停在**右手**绕圈、绳连到钩的尾环 ──

		/// <summary>**待机档**转速（转/分）—— 拿着钩索站着/走动时，手里那枚钩转多快（用户 2026-10-08 定：**135**）。
		/// 0 = 不绕、停在手上；负值 = 反向。</summary>
		public static float HandHookRpmIdle = 135f;

		/// <summary>**瞄准 / 蓄力档**转速（转/分）—— 按住左键瞄准期间甩得更急（用户 2026-10-08 定：**270**）。
		/// 判据见 <see cref="IsAimingGrapple"/>（= 我们的 ready / hold 正在播，**不是**"拿着钩索"就算）。</summary>
		public static float HandHookRpmAim = 270f;

		/// <summary>此刻生效的转速（转/分）—— 上一帧 <see cref="TickHandHook"/> 用的那个值（给命令回执看的）。</summary>
		public static float HandHookRpmNow = 135f;

		/// <summary>此刻在瞄准/蓄力吗（上一帧的值；`custom.grapple spin` 无参时回显用）。</summary>
		public static bool HandHookAimingNow;

		/// <summary>绕圈半径（米）= 手里那截绳的长度（绳走 verlet，甩起来自然有弧线与拖尾）。
		/// 🔴 2026-10-07 晚用户口径：**半径 ≈ 小臂长度（0.25 m）** ⇒ 默认 0.25
		///    （早前是 0.20，"手里大约 20 cm 绳"）。现调：`custom.grapple spin &lt;rpm&gt; [半径]`。</summary>
		public static float HandHookRadius = 0.25f;

		/// <summary>
		/// 绕转的**轴**（`custom.grapple armaxis &lt;0|1|2&gt;`）—— 圆所在的平面 ⊥ 这个轴：
		/// · **0（默认）世界竖直** —— 手垂在体侧时小臂≈竖直 ⇒ 圆是**水平的、绕着胳膊转**
		///   （用户 2026-10-07 口径："以手臂为轴做圆周运动，半径差不多是小臂长度"）
		/// · 1 = **体侧前后**（轴 = 角色右方向）⇒ 圆在竖直面里、前后甩
		/// · 2 = **左右横扫**（轴 = 角色朝向）⇒ 圆在正面（左右）面里
		/// · 3 = **上臂弦（肩 → 手）** ⇒ 圆 ⊥ 这条弦；⚠️ 手臂一折它就不是小臂了（用户当场指出）⇒ 一般用 4
		/// · 4 = 🔴 **小臂轴（肘 → 手）**（用户 2026-10-07 晚最终口径）⇒ 圆 ⊥ 小臂，真像"绕着胳膊甩"；
		///   骨索引走官方语义接口 `GetRealBoneIndex(HumanBone.ForearmR / HandR)`；读不到自动退回 0。
		/// ⚠️ 真"小臂骨方向"要读 `Monster.RightUpperArmBoneIndex`（本项目**从没读过这条**，且有
		///    裸读左手骨当场 AccessViolation 的前科）—— 先不碰；竖直轴在手垂下时就是小臂方向。
		/// </summary>
		/// 🔴 **默认值已改为 4（小臂轴）**（2026-10-07 晚用户裁定："别让我多打一个指令了"）——
		///    上面 · 里写的"0（默认）"作废，0/1/2/3 现在都只是备选对照档。
		public static int HandHookAxis = 4;

		/// <summary>
		/// **手部挂点沿小臂外移**（米，`custom.grapple palm &lt;米&gt;`；默认 **0.08**）——
		/// 🔴 骨点 = **腕关节**，不是掌心（骨骼原点是起点关节）。这个偏移把"手"这一个点整体挪到**掌心**，
		///    于是**三处一起**跟着走：① 绳的近端锚点（`GetRopeAnchor`）② 手里那枚钩的圆心 ③ 开火起点。
		///    （2026-10-08 用户实机："绳的挂点仍然在手腕，不在手心" —— 当时只有圆心挪了、绳没挪，故统一到这里。）
		/// 方向 = **小臂方向**（肘→手，`TryGetForearmAxis`，与绕转轴同一套语义骨接口）；读不到就不移。
		/// 设 0 = 正好压在腕关节上。
		/// </summary>
		/// 🔴 2026-10-08 用户实机定稿：**默认 0.25**（腕关节沿小臂外移 25 cm）。
		///    ⚠️ 参考量级：腕→掌心 ≈ 0.08、腕→指尖 ≈ 0.18~0.20 ⇒ 0.25 已经**在指尖之外**，
		///    绳的近端与钩的圆心都会浮在手前方一点（用户明确要这个数）。要贴回手心就 `palm 0.08`。
		/// </summary>
		public static float HandPalmOffset = 0.25f;

		/// <summary>
		/// **圆心再沿轴外移**（米，`custom.grapple armaxis &lt;档&gt; [米]`；**默认 0**）——
		/// 圆所在的平面整体沿"绕转轴"方向挪一点（在 <see cref="HandPalmOffset"/> 之上再叠）。
		/// 🔴 默认改成 0（2026-10-08）：原来那 0.08 是为了把圆心挪进掌心，现在这件事由 `palm` 统一负责，
		///    这里再叠就会**double**（掌心里又往外挪 8 cm）。留作微调旋钮。
		/// </summary>
		public static float HandHookAxisOffset = 0f;

		/// <summary>
		/// **飞行期间**的绳长跟随速度（米/秒；`GrappleRope.LengthFollowSpeed` 的临时值，收钩时恢复原值）。
		/// 🔴 2026-10-08 加：默认的 8 m/s 追不上 42 m/s 的钩头 ⇒ 绳永远比跨度短 ⇒ **绷直成一条直线**
		/// （用户实机："看不到末端从右手边飞到目标点的曲线过程，只有一条笔直的直线"）。拉到 60 之后
		/// 绳长≈跨度×1.05（那份"永远留的余量"）⇒ 甩出去带弧 ✓。
		/// </summary>
		public static float FlightLengthFollowSpeed = 60f;

		/// <summary>手里那档的绳长跟随速度（= 绳自己的默认值 8 m/s；收钩时恢复成它）。</summary>
		public static float HandLengthFollowSpeed = 8f;

		/// <summary>
		/// 🔴 **手里那段绳要绷直**（用户 2026-10-08）—— 手→钩那一截是"甩着的绳"，必须是紧的。
		/// `SlackRatio = 1.0` ⇒ 目标绳长 = 跨度（不留余量）⇒ 引擎走**绷紧的解析解** = 一条直线 ✓
		/// （绳自己的默认 1.05 是给飞行用的：甩出去要带弧）。
		/// </summary>
		public static float HandRopeSlackRatio = 1.0f;

		/// <summary>手里那档的绳长**下限**（米）—— 绳的默认 `MinLength` 是 0.3，
		/// 而手里跨度只有 ~0.25 m ⇒ 下限比跨度还长 ⇒ 必然松 ✗。手里这档压到 0.1。</summary>
		public static float HandRopeMinLength = 0.1f;

		/// <summary>飞行那档的余量比例（默认 1.05 = 比跨度长 5% ⇒ 带弧；`custom.grapple slack` 改的就是它）。</summary>
		public static float FlightRopeSlackRatio = 1.05f;

		/// <summary>飞行那档的绳长下限（米，绳的默认值）。</summary>
		public static float FlightRopeMinLength = 0.3f;

		/// <summary>返程**到位判定**（米）：钩头到手挂点的距离小于它就认为"收回来了"，交回手里那套。
		/// 别设太小 —— 手在动画里一直在动，可能永远追不到 0。</summary>
		public static float ReturnArriveDistance = 0.35f;

		/// <summary>
		/// 🔴 **A 段（左手环 → 右手）要松**（用户 2026-10-08）—— 那是"握在两只手之间的一段绳"，
		/// 自然垂一点才像话；B 段（手→钩）才是必须绷直的那截。
		/// 1.3 = 比跨度长 30% ⇒ 明显的一段垂弧（绳的 verlet 自带重力 ⇒ 自己会垂 ✓）；嫌垂多/垂少改这个数。
		/// </summary>
		public static float RopeASlackRatio = 1.3f;

		/// <summary>A 段的绳长下限（米）。</summary>
		public static float RopeAMinLength = 0.12f;

		/// <summary>
		/// **圆心横向远离身体**的偏移（米，`custom.grapple armout &lt;米&gt;`；默认 0.10）——
		/// 治"转一圈有一小段看不见"：半径 0.25 m 的圆，若圆心就压在手腕骨点上，**圆内侧那一段正好扫进躯干里**
		/// （实机现象：转一圈就短暂消失一下）。往外挪一点，整圈都露在体外。
		/// </summary>
		public static float HandHookOutShift = 0.10f;

		/// <summary>**每帧诊断**（`custom.grapple spinlog 1 [秒]`，默认抓 2 秒）：开着时**每帧打一行**
		/// `[Grapple] spinlog #帧号 角度 pos 径向 手源 横偏 内缘 高 | 实体帧 | 两条绳状态`，到点**自动关**（免得刷屏）。
		/// 🔴 用它抓"钩在圆周运动里瞬间不见"：那一帧的 `actual`/`shown` 直接说明是"我们藏了"还是"位置跳了"还是"都好（= 渲染/遮挡）"。
		/// 🔴 **2026-10-08 补的三个数**（回答"走动时看不见、站住才出现"）：`手源`（换路 / 退盆骨兜底一眼可见）·
		///    `横偏`（圆心离身体轴的水平距离）· `内缘` = 横偏 − 半径（**< 0 = 圆扫进躯干/腿里** = 遮挡）· `高`（脚底以上）。
		/// </summary>
		public static bool HandHookLog = false;

		/// <summary>抓帧时长（秒；`spinlog 1 &lt;秒&gt;` 可改）。</summary>
		public static float HandHookLogSeconds = 2f;

		private float _handHookLogLeft;      // 剩余抓帧时长（>0 = 正在抓）
		private int _handHookLogFrame;       // 本次抓帧的帧号（看跳号/丢帧用）
		private bool _handHookLogArmed;      // 已经开抓了吗（命令置 HandHookLog=true 后，首次 tick 才真正开始计时）
		private string _lastHandSrcTag;      // 上一帧的手源**路径标签**（换路就落一条日志；null = 手里那套还没起来）
		private float _handSrcLogCool;       // 「手源换路」日志的节流（秒；防抖时刷屏）
		/// <summary>上一次读到的**好手点**（世界坐标）与当时的 agent 位置 —— 手读数失败时拿它顶着（见 <see cref="GetHand"/>）。
		/// 🔴 2026-10-08：以前读失败直接退盆骨/脚底，钩与绳会一起"甩到脚踝消失"（实机日志实锤）。</summary>
		private Vec3 _lastGoodHand;
		private Vec3 _lastGoodAgentPos;
		private float _lastGoodHandAge = float.MaxValue;   // 秒；超过 <see cref="HandHoldSeconds"/> 就作废
		/// <summary>手读数失败时，"上一次的好手点"还能顶多久（秒）。</summary>
		private const float HandHoldSeconds = 1.0f;
		private Vec3 _hookPlaneS;             // 圆平面内的参考方向（状态：逐帧最小旋转，保证平面不跳）

		/// <summary>
		/// 圆平面的**连续**基（给出 s / f ⊥ 轴，且 s × f = u = 轴）。
		///
		/// 🔴 **为什么不能直接用 `GrappleRope.BasisWithLocalZ`**（2026-10-08 用户报告"钩的旋转面在扭"）：
		/// 那个函数在 **|轴.z| 跨越 0.99** 时会**换一根参考轴**去叉乘（(0,0,1) ↔ (0,1,0)）⇒ 平面基**突然翻 90°**，
		/// 表现出来就是钩在圆上的**相位**与自身**滚转**跳一下（用户看到的"扭"/"忽然变个样"）。
		/// ⇒ 改成把**上一帧的 s 投影到新平面再归一**（最小旋转 / 平行移动）：平面跟着轴平滑转，永不跳。
		/// 顺带：`hookroll` 因此变成"相对一个稳定参考"的滚转，调出来的角度才可复现。
		/// </summary>
		private Mat3 ContinuousPlane(Vec3 axis)
		{
			Vec3 s = _hookPlaneS - axis * Vec3.DotProduct(_hookPlaneS, axis);   // 投影到 ⊥ 轴的平面
			if (s.LengthSquared < 1e-8f)
			{
				Mat3 fresh = GrappleRope.BasisWithLocalZ(axis);                 // 首帧 / s 与轴几乎平行 → 重建一个
				_hookPlaneS = fresh.s;
				return fresh;
			}
			s.Normalize();
			_hookPlaneS = s;
			Mat3 m = Mat3.Identity;
			m.u = axis;
			m.s = s;
			m.f = Vec3.CrossProduct(axis, s);       // 与 BasisWithLocalZ 同款手性：s × f = u
			return m;
		}

		/// <summary>
		/// 手源字符串的**路径标签**（去掉每帧都在变的距离数字）—— 用来判"这一帧换路了吗"。
		/// 例：`bone/local d=1.26` → `bone/local` · `item-entity Weapon3 d=1.2` → `item-entity` ·
		/// `bone: both far (world 575.7 local 1.7)` → `bone:far`（= 骨读数掉线，调用方会退盆骨兜底位）。
		/// 🔴 直接比整串是不行的：距离每帧都在变 ⇒ 每帧都算"换路"⇒ 刷屏。
		/// </summary>
		private static string HandSourceTag(string src)
		{
			if (string.IsNullOrEmpty(src))
			{
				return "?";
			}
			if (src.StartsWith("hold ", StringComparison.Ordinal))
			{
				return "hold";          // 读失败、正拿上一次的好手点顶着（见 GetHand）
			}
			if (src.StartsWith("bone/", StringComparison.Ordinal))
			{
				int sp = src.IndexOf(' ');
				return sp > 0 ? src.Substring(0, sp) : src;
			}
			if (src.StartsWith("item-entity", StringComparison.Ordinal))
			{
				return "item-entity";
			}
			if (src.StartsWith("bone:", StringComparison.Ordinal))
			{
				return "bone:far";
			}
			return src;
		}

		/// <summary>手里待命总开关（`custom.grapple spin off`）。</summary>
		public static bool HandHookEnabled = true;

		/// <summary>手里那条**绳**的开关（`custom.grapple spinrope 1`）。
		/// 🔴 **默认关**（2026-10-07 事故后分两步走）：待机路径里钩实体与绳是一起上的，一起崩时说不清是谁；
		///    先只开钩（实体 + 自造网格），确认不崩再单独打开绳 ⇒ 一步就能定死病根。
		/// 待机绳是**全新工况**（绳长只有 0.3~0.5 m，飞行时是 20 m），最可疑的就是它。</summary>
		/// 🔴 **2026-10-08 用户裁定：默认开**（实机开 `spinrope ab` 没崩）。早前的 AV 嫌疑仍在案，
		///    但我们现在有**事件级日志**（`[Rope]` 建/拆/显隐 + 根子树计数）⇒ 真崩了也能一次定位。
		///    出事立刻 `custom.grapple spinrope off` 当场关掉（即时生效）。
		public static bool HandRopeEnabled = true;

		/// <summary>**左手那截绳（A：环→右手）**的独立开关（`custom.grapple spinrope a`）—— 用来二分"崩在哪条绳"。
		/// 🔴 2026-10-08 用户裁定：**默认开**（与 B 一起）。分开的开关保留着，随时能单独关。</summary>
		public static bool HandRopeAEnabled = true;

		/// <summary>
		/// 🔴 **手上那枚钩的分档诊断**（`custom.grapple hand &lt;0..4&gt;`，2026-10-07 晚）——
		/// 回答"它为什么不显示"。**每一档只比上一档多一个变量**，所以：
		/// **第一档开始看不见 = 元凶就是那一档新加的那个变量。**
		///
		/// | 档 | 位置 | 朝向 | 摆位频率 | 比上一档多出来的变量 |
		/// |---|---|---|---|---|
		/// | 1 | 身前 2 m / 高 1.2（= `custom.spawn_mesh` 逐字同款，那件**已知可见**） | Identity | **只摆一次** | 起点（= 对照组） |
		/// | 2 | 同档 1（跟着人走） | Identity | **每帧** | 每帧 `SetGlobalFrame` |
		/// | 3 | **右手骨上方 0.18 m**（<see cref="GetHand"/>，= 阴魔斩蓄力球同一读法） | Identity | 每帧 | 位置换成手骨 |
		/// | 4 | 手骨 + 离心 0.20 m | **径向朝向** | 每帧 | 径向基底（**= 现行为**） |
		///
		/// **判读**：4 看不见而 3 看得见 ⇒ 元凶 = 径向基底（`BasisWithLocalZ`）·
		/// 3 看不见而 2 看得见 ⇒ 元凶 = 手骨位置 · 2 看不见而 1 看得见 ⇒ 元凶 = 每帧摆位 ·
		/// **1 就看不见 ⇒ 元凶在"实体 + 网格这条路"本身**（那时与 `custom.spawn_mesh` 的差异只剩
		/// 「缓存过的 MetaMesh」与「建实体的写法」，再往下切）。
		///
		/// 🔴 全程用**同一枚实体**（`_hook`）—— 换档不重建，免得"重建"本身混进来当变量。
		/// 🔴 档 4 不是"另写一份摆位"，而是**直接落进正常那条路**（只跳过判据）—— 保证它就是现行为。
		/// </summary>
		public static int HandProbeStage = 0;

		private bool _handProbePlaced;             // 档 1（只摆一次）是否已摆过
		private int _handProbeLoggedStage = -1;    // 诊断日志闸门：每档只打一条

		private float _handHookAngle;        // 绕圈相位（弧度）
		private bool _handHookRopeShown;     // 手里那条绳是否 Show 过（Show 会把点链拉直 ⇒ 只在进入时调一次）
		private bool _handHookLogged;        // 诊断：开摆只打一条日志

		private GrappleAnimContext _animCtx;
		private AgentAnimStateMachine _anim;
		private float _animPendingPullTimer;  // >0 = 开火后计时中，到点把状态机送进"过程"（release 演完那一刻）
		private float _flightSeconds;         // 本钩飞行耗时（蓄势 = PullStartSeconds − 它）
		private string _lockedAnimState;      // 命令 `animlock`：非空 = 强锁该状态 + Hold（静观用）

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
			// 姿态动画状态机实例（从注册表按名字取；定义没注册 = 一台空机器 —— 动不了，但一切照常，铁律 1）
			_animCtx = new GrappleAnimContext();
			_anim = AnimMachineRegistry.Create(GrappleAnimMachine.Name, _animCtx);
			_pull.WaitAnimExit = AnimExited;      // 落地动作没演完就先别拆板（GrapplePull.TickSettling 的等待门）
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
				_ropeA.Teardown();          // 手里那截（环 → 右手）
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
				_hook.DestroyEntity();      // 先拆子件（钩），再拆根 —— 顺序反了就是"销毁父级后还动子级"
				_rig.Teardown();            // 根实体 + 左手环（绳由下面的 Teardown 自己收）
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
			_rope.Show(GetRopeAnchor(), _anchor);
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
					// 🔴🔴 **2026-10-08 用户裁定：没目标也必须能打** ——
					//    "朝着天射绳索的时候绳索射不出来…这是不应该的，绳索应该正常打，
					//      只不过没目标的话会随着 recover 飞回手里继续圆周。"
					//    ⇒ 打空（朝天/开阔地）时**不再拒发**：按"朝视线方向打满射程"合成一个瞄准点，
					//      钩头飞满射程 → 命中检查一无所获 → `Missed` → 状态机走"打空亮相 → recover"，
					//      钩随即回到手里继续绕圈 ✓（以前这里是 `return Error` = 干脆不出钩 ✗）。
					// 🔴🔴 **方向口径（2026-10-08 实机抓出来的坑）**：**以相机视线为准**，弹道方向只当兜底。
					//    起因：我原来优先用引擎给的 `shotDirection`，而实机日志显示那个方向是**水平的** ——
					//    玩家准星明明看着天（相机方向 z=+0.60），合成的瞄准点却和手同高 ⇒ 钩**平着飞** ✗。
					//    这也符合既有口径：瞄准一律"以玩家看到的『指哪』为准"，弹道只是相机打空后的退路。
					Vec3 dirFallback;
					if (CameraLook.TryGet(out dirFallback) && dirFallback.LengthSquared > 1e-6f)
					{
						dirFallback = dirFallback.NormalizedCopy();
					}
					else if (hasShot && shotDirection.LengthSquared > 1e-6f)
					{
						dirFallback = shotDirection.NormalizedCopy();
					}
					else
					{
						DebugLogger.Log($"[Grapple] 瞄准失败且拿不到方向（相机：{why}）");
						return "Error: " + why;
					}

					hand = GetHand();
					aim = hand + dirFallback * AimRange;
					aimAgent = false;
					DebugLogger.Log($"[Grapple] 瞄准打空（{why}）→ 按「朝视线打满 {AimRange:F0}m」发射"
						+ $" 方向=({dirFallback.x:F2},{dirFallback.y:F2},{dirFallback.z:F2})"
						+ $"（飞满后打空回收，钩回手里继续转）");
				}
			}

			// 🔴 **飞行起点 = 钩此刻待的地方**（用户 2026-10-08 要求："钩锁的飞行起点位置就是之前做圆周运动
			//    的那个位置，不准强行设置到相机中心处，这样才真实"）—— 手里那枚停在圆周上，就从那儿飞出去。
			//    没在待命（命令 `throw` / 没拿在手上）才退回"手"。
			Vec3 launchFrom = _hook.IsParked ? _hook.ParkedPosition : hand;
			Vec3 toAim = aim - launchFrom;
			float dist = toAim.Length;
			if (dist < 0.4f)
			{
				return "Error: target too close.";
			}
			Vec3 dir = toAim * (1f / dist);

			// 绳先摆出来（手 → 钩头方向的一小段），此后每帧跟着钩头 —— 顺序照 Anchor()：先定长度再 Build
			_rope.AutoLength = true;
			_rope.FreeEnd = false;
			// 切回**飞行那一档**（手里那档是"绷直"：余量 1.0 / 下限 0.1；飞行要带弧：1.05 / 0.3）
			_rope.SlackRatio = FlightRopeSlackRatio;
			_rope.MinLength = FlightRopeMinLength;
			if (_rope.Length < _rope.MinLength)
			{
				_rope.Length = _rope.MinLength;
			}
			if (!_rope.Build(scene))
			{
				return "Error: rope build failed (" + _rope.LastError + ")";
			}
			// 🔴🔴 **2026-10-08 修：开火时不再 `Show`（= 不再把绳拉直）** ——
			//    `Show()` 的语义就是"把点链**重新排成一条直线**"（见 GrappleRope.Show 的注释）；在开火瞬间调它，
			//    手里那条甩着的绳会**立刻变成一条笔直的线** ⇒ 用户实机："看不到末端从右手边飞到目标点的曲线过程"。
			//    而这里**根本不需要 Show**：钩的起点 = 它刚才待的**圆周位置** = 绳的远端**本来就在那儿**，
			//    接着每帧 `Tick` 就是完全连续的（绳自己会随钩头拉长、甩出去 ✓）。
			//    只有"手里压根没绳"时（命令 `throw` / 手绳关着）才需要 Show 出一条来。
			if (!_rope.IsVisible || _rope.PointCount < 2)
			{
				_rope.Show(GetRopeAnchor(), launchFrom);
			}

			// 🔴🔴 **2026-10-08 修（二）：飞行时把"绳长跟随速度"拉高** —— 否则绳永远比跨度短 ⇒ 走**解析解 = 一条直线**：
			//    钩以 42 m/s 飞出去，而 `LengthFollowSpeed` 默认只有 8 m/s ⇒ 绳长追不上跨度 ⇒ `[TAUT]` ⇒ 看不到弧。
			//    拉高之后绳长≈跨度×1.05（那份"永远留的余量"）⇒ 甩出去时自然**带弧**、远端像被拽着走 ✓。
			//    收钩时（<see cref="Release"/>）恢复原值。
			_rope.LengthFollowSpeed = FlightLengthFollowSpeed;

			if (!_hook.Launch(scene, launchFrom, dir, dist + 0.6f))
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
			DebugLogger.Log($"[Grapple] 发射：起点={Fmt(launchFrom)}{(_hook.IsParked ? "(= 圆周上那枚此刻的位置)" : "(手)")}"
				+ $" 手={Fmt(hand)} 瞄准={Fmt(aim)} 距离={dist:F1}m 目标={(aimAgent ? "人" : "地形")}"
				+ $" | 起手={(autoPull ? "武器开火(命中后自动拉)" : "命令(命中即停)")} 空中起钩={_attachFromAir}");

			// ── 姿态动画：开火（2026-10-04 二稿）──
			//    前半段（ready/hold/release）归**武器 usage**，引擎自己在松手那一拍播 release；
			//    本机只在 release 演完（+ReleaseSeconds）那一刻接管"过程"。这里只记计时 + 重置上下文。
			_flightSeconds = (dist + 0.6f) / MathF.Max(1f, GrappleHook.Speed);   // 蓄势用它（见 TickHook）
			_animPendingPullTimer = ReleaseSeconds;
			if (_animCtx != null)
			{
				_animCtx.LandingFound = false;    // 上一钩的平台别带进这一钩（这一钩解算了才置真）
				_animCtx.Cancelled = !autoPull;   // 命令 throw（不自动拉）= 没人续这一钩 ⇒ 到时走"收手"出口
			}

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
			bool wasPulling = _pull.IsActive;    // 动画收摊的判据要在 Abort 之前取（Abort 会把它收掉）
			// 正在拉拽 ⇒ 先中止（拆板 + 解冻 —— 绝不留冻结状态）
			if (wasPulling)
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
			_rope.LengthFollowSpeed = HandLengthFollowSpeed;   // 恢复手里那档（飞行期的临时值到此为止）
			_landingNote = "released";

			// ── 姿态动画收摊（2026-10-04 二稿）──
			//    · **人已空中**（拉拽中掉板 / 空中收摊）：立即收摊（按 fall-trigger 送 自由落体）；
			//    · **其余**（打空 / 命令 / 正常收尾）：只打"已放弃"标志 —— 状态机自己走：
			//        过程段 → recover-due 边 → 收手（打空亮相）；落地/收手段 → rem 15% 自己出机。
			if (_animCtx != null)
			{
				_animCtx.Cancelled = true;
			}
			if (IsMainAirborne())
			{
				ExitAnim();
			}
			return "grapple: hook released, rope hidden";
		}

		// ─────────────────────────────── 姿态动画（2026-10-04） ───────────────────────────────

		/// <summary>命令层读机械态用（`custom.grapple anim` 打印当前状态 / animlock 用）。</summary>
		internal AgentAnimStateMachine Anim => _anim;

		/// <summary>每帧推进状态机：喂事实 → Tick（照飞行，"填完才 Tick"）。</summary>
		private void TickAnim(float dt)
		{
			if (_anim == null || _animCtx == null || Agent.Main == null)
			{
				return;
			}

			if (!AnimEnabled)
			{
				// 总开关关掉：机器若还在机内就送出去（回引擎动画），之后不再动它
				if (IsAnimInside())
				{
					ExitAnim();
				}
				return;
			}

			if (!string.IsNullOrEmpty(_lockedAnimState))
			{
				// 命令 animlock：强锁某状态 + Hold（静观用）；无参 = 解锁（见 SetAnimLock）
				_anim.Hold = true;
				if (_anim.Current != _lockedAnimState)
				{
					_anim.Force(Agent.Main, _lockedAnimState, GrappleAnimMachine.AnimBlendIn);
				}
				return;
			}
			_anim.Hold = false;

			// 🔴 开火计时：release 播完（+ReleaseSeconds）那一刻把状态机送进"过程"
			//    （合并件从源帧 17 起，无缝接住引擎播的 release 尾帧）。命令路径同此。
			if (_animPendingPullTimer > 0f)
			{
				_animPendingPullTimer -= dt;
				if (_animPendingPullTimer <= 0f)
				{
					_animPendingPullTimer = 0f;
					ForceAnim(GrappleAnimConditions.PullTrigger);
				}
			}

			// 喂事实（填完才 Tick）：段剩余 = 当前状态的剩余（所以 landing-due 拿到的就是"过程段"的）
			_animCtx.AnimRemainFrac = _anim.CurrentRemainFrac;
			_animCtx.LandingFound = _landing.Found;

			_anim.Tick(Agent.Main, dt);

			if (_anim.Current == AgentAnimStateMachine.OutsideState)
			{
				_animCtx.Cancelled = false;      // 收摊 = 标志清干净，别带进下一钩
			}
		}

		/// <summary>机器"在机内"吗（接管着 0 号通道）。</summary>
		private bool IsAnimInside()
		{
			string cur = _anim?.Current;
			return cur != null && cur != AgentAnimStateMachine.OutsideState;
		}

		/// <summary>按 XML 里的"时刻名"Force 进对应状态（C# 里没有状态名；改名/换状态只改 XML）。</summary>
		private void ForceAnim(string whenToken)
		{
			if (_anim == null || Agent.Main == null || !AnimEnabled)
			{
				return;
			}
			if (_anim.TryEventTarget(whenToken, out string state))
			{
				_anim.Force(Agent.Main, state, GrappleAnimMachine.AnimBlendIn);
				// 🔴 常开一行/次（2026-10-04 深夜：用户问"状态机有没有 setactionchannel 的记录"——
				//    以前成功的 Force 是静默的，日志里查不到"到底进没进"，只有缺边才报错）。
				DebugLogger.Log($"[Grapple] 姿态动画 Force → {state}（时刻名 {whenToken}）");
			}
			else
			{
				DebugLogger.Log($"[Grapple] 状态机里没有 '{whenToken}' 这条事件边 —— 动画不起"
					+ "（检查 ModuleData/statemachines/grapple.xml）");
			}
		}

		/// <summary>动画收摊：在机内才动；人在空中 ⇒ 按 `fall-trigger` 送 自由落体，否则送机外。</summary>
		private void ExitAnim()
		{
			_animPendingPullTimer = 0f;      // 收摊 = 作废"待进过程"的计时
			if (!IsAnimInside() || Agent.Main == null || !AnimEnabled)
			{
				return;
			}
			string cur = _anim.Current;
			if (IsMainAirborne() && _anim.TryEventTarget(GrappleAnimConditions.FallTrigger, out string fallState))
			{
				_anim.Force(Agent.Main, fallState, GrappleAnimMachine.AnimBlendIn);
				DebugLogger.Log($"[Grapple] 姿态动画 → 自由落体（人还在空中；从 {cur}）");
				return;
			}
			_anim.Force(Agent.Main, AgentAnimStateMachine.OutsideState, GrappleAnimMachine.AnimBlendIn);
			DebugLogger.Log($"[Grapple] 姿态动画收摊（0 号通道还引擎）| 从 {cur}");
		}

		/// <summary>
		/// `GrapplePull` 的落地等待门（`WaitAnimExit`）：**落地动作没演完就返回 false** —— 拆板/解冻再等等。
		/// 机器没接管 / 总开关关了 ⇒ 恒 true（不等，回旧行为）。
		/// </summary>
		private bool AnimExited()
		{
			if (_anim == null || _animCtx == null || !AnimEnabled)
			{
				return true;
			}
			return !IsAnimInside();
		}

		/// <summary>命令 `animlock`：锁死播某个状态（静观）· 空参 = 解锁。返回英文回执（控制台纪律）。</summary>
		internal string SetAnimLock(string stateName)
		{
			if (_anim == null || Agent.Main == null)
			{
				return "Error: no anim machine (in mission?)";
			}
			if (string.IsNullOrEmpty(stateName) || stateName == "clear")
			{
				_lockedAnimState = null;
				_anim.Hold = false;
				return "OK: animlock cleared.";
			}
			_lockedAnimState = stateName;
			_anim.Hold = true;
			_anim.Force(Agent.Main, stateName, GrappleAnimMachine.AnimBlendIn);
			return _anim.Current == stateName
				? $"OK: locked '{stateName}' (hold=true; 'animlock clear' to release)."
				: $"FAILED: could not enter '{stateName}' —— 名字不在定义里，或这条动作的 clip 还没导入（act_none，看日志）"
					+ "  usage: animlock <投掷|过程|落地|自由落体|clear>";
		}

		/// <summary>主角在空中吗（口径同 <see cref="ThrowInternal"/> 的 `_attachFromAir`）。</summary>
		private static bool IsMainAirborne()
		{
			return Agent.Main != null && !Agent.Main.IsOnLand();
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

		// ─────────────────────────────── 每帧面包屑（查栈丢失的 AV 用） ───────────────────────────────

		/// <summary>
		/// **每帧面包屑**（`custom.grapple tracelog &lt;0|1&gt;`，**默认关**）——开着时**每一步都往运行日志写一行**
		/// `[GTrace] f&lt;帧号&gt; &lt;阶段&gt;`。
		///
		/// 🔴 **为什么要有它**：钩索这一段的 AccessViolation 是**栈丢失**的那种（崩在 native、托管栈没有、
		///    连 dump 都没落盘 —— 见 plan §B4）。那种崩法**唯一**能取证的东西就是"**最后写进磁盘的那一行**"：
		///    `DebugLogger` 每次调用都 `AppendAllText`（逐行落盘、不缓存）⇒ 硬崩也留得住。
		///    ⇒ 崩在哪一行，那一行前面的**阶段名**就是死掉的那一步（阶段名紧挨着真正的调用写）。
		///
		/// **代价**：每帧十几行（30 秒 ≈ 2 万行、约 2 MB）。**只在复现崩溃的那一次开**，验完就关。
		/// 用法：`custom.grapple tracelog 1` → 复现 → 把运行日志**最后 30 行**发出来。
		/// </summary>
		public static bool TraceTick = false;

		private int _traceFrame;

		/// <summary>写一条面包屑（关着时零成本：一次 bool 判断）。**紧挨着真正的调用写**，不合并。</summary>
		private void Trace(string stage)
		{
			if (TraceTick)
			{
				DebugLogger.Log($"[GTrace] f{_traceFrame} {stage}");
			}
		}

		// ─────────────────────────────── 每帧 ───────────────────────────────

		public override void OnMissionTick(float dt)
		{
			base.OnMissionTick(dt);
			_traceFrame++;
			Trace("tick:begin");

			// ⓪ 瞄准相机（2026-10-04）：按住左键瞄准期间接管相机（鼠标驱动；见 GrappleAimCamera）。
			//    放在最前 —— 它的生命周期与钩头相位无关（瞄准时钩头通常还是 Idle）。
			GrappleAimCamera.Tick(dt);
			Trace("cam");

			// ⓪′ 姿态动画状态机（2026-10-04）：喂事实 → Tick（照飞行："填完才 Tick"）
			TickAnim(dt);
			Trace("anim");

			// ⓪ 开火拦截的排队执行（2026-10-03）：拦截补丁只"记一笔"，真正的发射与退弹在这一帧做 ——
			//    这样武器开火与命令 `throw` 走的是**同一条链路**（都在常规 tick 里）。
			GrappleFirePatch.ProcessPending();
			Trace("fire");

			// ⓪″ **手里那套常驻件**（左手环 + "环→右手"那截绳 A，2026-10-08）：与钩头相位**无关** ——
			//     握着钩索就该在（待命 / 飞行 / 拉拽都跟着两只手），所以放在相位分派**之前**。
			TickHandKit(dt);
			Trace("handkit");

			// ① 钩头模式（步骤 2 起）：它接管绳子；手动锚定让位
			if (_hookPhase != HookPhase.Idle)
			{
				TickHook(dt);
				Trace("hook");
				return;
			}

			// ①′ 手里待命（设计 B，2026-10-07）：钩索在手上 = 钩头停在右手绕圈、绳连到钩的尾环。
			//     顺序：飞行/钉住归 ①；命令锚定归 ②（那条也要用绳，所以这里先让开）。
			if (!_anchored)
			{
				Trace("handhook:enter");
				TickHandHook(dt);
				Trace("handhook");
				return;
			}

			// ② 手动锚定模式（步骤 1 的命令）
			if (!_anchored) return;
			try
			{
				_rope.Tick(dt, GetRopeAnchor(), _anchor);
				Trace("anchor.rope");
			}
			catch (Exception ex)
			{
				// 单帧出错就收绳 —— 绝不让它每帧刷异常
				_anchored = false;
				_rope.Hide();
				DebugLogger.Log($"[Grapple] tick disabled after exception: {ex.GetType().Name} {ex.Message}");
			}
			Trace("tick:end");
		}

		/// <summary>钩头模式每帧：推进钩头 → 处理命中/打空 → 绳跟着（手 → 远端）。</summary>
		private void TickHook(float dt)
		{
			// 🔴 **返程相位**（2026-10-08）：钩头追着手飞回，绳同步收短（远端 = 尾环 = `_hook.Position` ✓）。
			//    到了（≤ <see cref="ReturnArriveDistance"/>）就收场 → 相位回 Idle → 手里那套接管（绕圈恢复 ✓）。
			if (_hookPhase == HookPhase.Returning)
			{
				Agent main = Agent.Main;
				if (main == null)
				{
					Release("return: no player agent");
					return;
				}
				bool arrived;
				try
				{
					arrived = _hook.TickReturn(dt, GetHand(), ReturnArriveDistance);
					_rope.Tick(dt, GetRopeAnchor(), _hook.Position);
				}
				catch (Exception ex)
				{
					DebugLogger.Log($"[Grapple] 返程 tick 异常，直接收：{ex.GetType().Name} {ex.Message}");
					Release("return tick exception");
					return;
				}
				if (arrived)
				{
					DebugLogger.Log("[Grapple] 返程到位 → 交回手里（接着绕圈）");
					Release("returned (返程到位)");
				}
				return;
			}

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
				Trace("hk:step");

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
					// 🔴 **2026-10-08 用户要求：打空要"看得见地飞回来"**（"相当于飞出去的逆向"）——
					//    不再直接隐藏收场，切进**返程相位**：钩头追着手飞回、绳同步收短，到了交回手里继续绕圈 ✓。
					if (_hook.StartReturn())
					{
						_hookPhase = HookPhase.Returning;
						DebugLogger.Log("[Grapple] 打空 → 开始返程（追着手飞回，绳同步收短；到 ArriveDistance 内交回手里）");
					}
					else
					{
						Release("missed (打空，实体不在→直接收)");
					}
					return;
				}
			}
			else if (_hookPhase == HookPhase.Attached)
			{
				// 钉住之后：蓄势（UE 参考工程的那半秒）→ 自动拉（只有武器开火那一钩会）
				_attachTimer += dt;
				if (_attachAutoPull && AutoPull && _hook.AttachedAgent == null)
				{
					// 🔴 蓄势 = **拉拽起点钉在开火后 PullStartSeconds** − 本钩飞行耗时（2026-10-04 时间轴对齐）：
					//    钩到得早就不多等、飞得远就少等 —— 位移起点始终 ≈ 动画"过程"段的起点（用户定的 1.13 s）。
					//    （空中起钩的时间轴还没做，先照旧用固定值。）
					float delay = _attachFromAir
						? PullDelayAir
						: MathF.Max(0f, PullStartSeconds - _flightSeconds);
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
					// 拉拽整段完成：落地那条在 WaitAnimExit 的等待门里已自己出机（此处空操作）；
					// 板载自由落体落地的那条还留在 自由落体 状态 ⇒ 送它出机。
					ExitAnim();
					Release("pull finished（到位拆板）");
					return;
				}
				if (ev == GrapplePull.PullEvent.Aborted)
				{
					Release("pull aborted（拉拽中止）");
					return;
				}
			}

			try
			{
				_rope.Tick(dt, GetRopeAnchor(), FarEnd());
				Trace("hk:rope");
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
				// 姿态动画：位移真的开始 = 板动起来（状态机早在开火 +1.167 s 就进了"过程"，这里不 Force）
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

		// ─────────────────── 手里那套常驻件（左手环 + 环→右手那截绳 A，2026-10-08） ───────────────────

		/// <summary>
		/// **左手环 + "环 → 右手"那截绳（A）** —— 用户 2026-10-08 定的拓扑：
		/// **左手环 + A 一端 · 右手（不可见节点）+ A 另一端 + B 一端 · 钩 = B 另一端**。
		/// 与钩头相位**无关**（握着钩索就该在：待命 / 飞行 / 拉拽期间都跟着两只手），没拿钩索或手动锚定时才收。
		/// 判据与钩一致（<see cref="HandHookEnabled"/> + <see cref="IsHoldingGrapple"/>）—— 三件总是一起出现、一起消失。
		/// </summary>
		private void TickHandKit(float dt)
		{
			Agent player = Agent.Main;
			Scene scene = Mission != null ? Mission.Scene : null;
			bool want = HandHookEnabled && !_anchored && player != null && scene != null && IsHoldingGrapple();

			_lastGoodHandAge += dt;      // 「上一次好手点」的保鲜计时（每帧一次；见 GetHand）

			// 🔴 **先摆根实体**（钩索这件"东西"本身 = 挂在手上）—— 必须在本帧所有子件之前，
			//    因为子件写的是"相对 root 的局部帧"（用到的 root 帧要是本帧的，否则差一帧 ⇒ 抖）。
			if (want)
			{
				_rig.PlaceRoot(GetHand(), scene);      // 传 scene：根实体在这一步就按"手的位置"建出来（见 PlaceRoot 注释）
			}
			Trace("kit:root");

			// 🔴 **左手环默认关**（2026-10-08）：左手那件现在由**物品网格**（绳，`lwn_grapple_rope`，引擎挂在左手骨上）
			//    负责 —— 环是它上一版的做法（运行时实体、每帧跟左手骨），两个一起上 = 同一只手上叠着环 + 绳。
			//    要用回来：`custom.grapple handring 1`（即时生效）。绳的近端锚点在**右手**，与环无关，关掉不丢功能。
			_rig.TickRing(scene, player, want && GrappleRig.HandRingEnabled);   // 左手环（根实体下的第三件）
			Trace("kit:ring");

			if (!want)
			{
				if (_ropeAShown)
				{
					_ropeAShown = false;
					_ropeA.Hide();
				}
				return;
			}

			// A（环 → 右手）：独立开关 —— 默认关（与 B 一样是 AV 嫌疑，见 HandRopeAEnabled）
			if (!HandRopeAEnabled)
			{
				if (_ropeAShown)
				{
					_ropeAShown = false;
					_ropeA.Hide();
				}
				return;
			}

			// A 段**要松**（用户 2026-10-08）：那是"握在两只手之间的一段绳"，垂一点才自然；
			//   B 段（手→钩）才绷直（见 TickHandHook 里那两行）。两段的档各自独立、互不影响。
			_ropeA.SlackRatio = RopeASlackRatio;
			_ropeA.MinLength = RopeAMinLength;

			// 🔴 **环没摆上 = A 干脆不建**（2026-10-08 修）：A 的两端是"左手环 → 右手"，环关着时环的点**退回手点**
			//    ⇒ 两端是同一个点（实机日志 `A Show：… → … 跨度=0.00m`）—— 那是 **48 个恒退化的实体**
			//    （摆不出任何形状、还占着根实体的子树）。环要用了（`handring 1`）A 自然就回来。
			if (!_rig.RingPlaced)
			{
				if (_ropeAShown)
				{
					_ropeAShown = false;
					_ropeA.Hide();
				}
				Trace("kit:A:off(no ring)");
				return;
			}

			// A：左手环 → 右手（环还没摆上时退化成"手的点"，不至于拉出一条飞线）
			Vec3 hand = GetHand();
			Vec3 ring = _rig.RingPosition;
			if (!_ropeAShown || !_ropeA.IsVisible)      // 同上：以绳自己的状态为准，别只信标志
			{
				Trace("kit:A:build");
				if (!_ropeA.Build(scene))
				{
					return;
				}
				_ropeA.Show(ring, hand);
				_ropeAShown = true;
				Trace("kit:A:show");
			}
			_ropeA.Tick(dt, ring, hand);
			Trace("kit:A:tick");
		}

		// ─────────────────── 手里待命（设计 B，2026-10-07）：钩在右手上绕圈 ───────────────────

		/// <summary>
		/// 钩索拿在手上时每帧：钩头实体停在**右手**上、绕着手指定的圈转，绳走"手 → 钩（尾环）"那条。
		/// 判据 = **此刻握着的是钩索本体**（`Agent.WieldedWeapon`）—— 换武器 / 收起来就收掉钩与绳。
		/// 参数：<see cref="HandHookRpmIdle"/> / <see cref="HandHookRpmAim"/>（两档转速，判据 <see cref="IsAimingGrapple"/>）·
		/// <see cref="HandHookRadius"/> · <see cref="HandHookEnabled"/>。
		/// 🔴 绳**只在进入时 Show 一次**（Show 会把点链拉直成一条线）；之后每帧只 Tick —— 让它自己甩。
		/// 🔴 **诊断阶梯**（<see cref="HandProbeStage"/>）：档 1~3 由 <see cref="TickHandProbe"/> 独占；
		///    档 4 = 落进下面这条正常路（只跳过判据）；档 0 = 正常（判据照旧）。
		/// </summary>
		private void TickHandHook(float dt)
		{
			if (HandProbeStage >= 1 && HandProbeStage <= 3)
			{
				TickHandProbe(dt);
				return;
			}

			// 档 0 = 正常；**档 4 = 强制走下面那条**（不看总开关、不看握着谁 —— 它就是"现行为"的对照）
			if (HandProbeStage == 0 && (!HandHookEnabled || !IsHoldingGrapple()))
			{
				_hook.Unpark();
				_lastHandSrcTag = null;      // 收了就复位 ⇒ 下次拿起来，"手源换路"会重新落一条（带当前几何）
				if (_handHookRopeShown)
				{
					_handHookRopeShown = false;
					_rope.Hide();
				}
				if (_handHookLogged)
				{
					_handHookLogged = false;      // 下次再拿起来会重新打"开摆"
					string wieldedId = "(?)";
					try
					{
						Agent m = Agent.Main;
						wieldedId = (m != null && m.WieldedWeapon.Item != null) ? m.WieldedWeapon.Item.StringId : "(空手)";
					}
					catch (Exception)
					{
					}
					DebugLogger.Log($"[Grapple] 手里的钩：停（此刻握着 {wieldedId}，开开关={HandHookEnabled}）");
				}
				return;
			}

			Agent player = Agent.Main;
			Scene scene = Mission != null ? Mission.Scene : null;
			if (player == null || scene == null)
			{
				return;
			}

			// 🔴 **支点 = 手的世界位置，走已验证的那条读法**（`GetHand` → `SpellCastInput.TryGetRightHandAnchor`）：
			//    它内部带"**骨帧离角色 > 3 m 就判不可信、退回身体近似位**"的兜底。
			//    2026-10-07 实机栽过：我裸用 `GetBoneEntitialFrame`（没兜底），那一帧读到的是**角色局部**坐标
			//    (0.28,-0.16,0.85) ⇒ 钩被摆到世界原点附近 ⇒ 手里什么都看不见（不崩，就是找不到）。
			//    🔴 当晚最终修法 = `TryReadBoneWorld` 的**双解释**（原生给的就是角色局部坐标，实测 d=575 m vs 1.4 m）。
			Vec3 pivot = GetHand();
			Trace("hh:hand");

			// 圆心**横向远离身体**（默认 0.10 m）——"转一圈消失一下"的常见原因 =
			// 半径 0.25 的圆内侧那一段扫进了躯干里（整段埋在身体内部 ⇒ 看不见）。
			if (HandHookOutShift > 0.001f)
			{
				Vec3 lateral = new Vec3(pivot.x - player.Position.x, pivot.y - player.Position.y, 0f);
				if (lateral.LengthSquared > 1e-6f)
				{
					pivot += lateral.NormalizedCopy() * HandHookOutShift;
				}
			}

			// 绕圈平面：**圆 ⊥ 轴**（轴由 <see cref="HandHookAxis"/> 选，`armaxis` 现场调）。
			// 用 `BasisWithLocalZ(轴)` 的 s / f 当圆平面的基（都是单位长、正交、且都 ⊥ 轴）——
			// 复用同一个已验证的基底构造，不再自己搭三角函数（2026-10-07 早前的"竖直圆"就是它的 s/f 特例）。
			Vec3 axis = HandHookAxisDir(player);
			Trace("hh:axis");
			pivot += axis * HandHookAxisOffset;      // 圆心沿轴外移（"再向外探一点点"）
			Mat3 plane = ContinuousPlane(axis);      // 🔴 连续基（别再换回 BasisWithLocalZ，见方法注释）
			Trace("hh:plane");
			// 🔴 **两档转速**（用户 2026-10-08 定：待机 135 / 瞄准蓄力 270）—— 瞄准判据见 IsAimingGrapple。
			//    角度是**连续累加**的 ⇒ 切档只改"转多快"，不会跳一下（相位不重置）。
			bool aiming = IsAimingGrapple(player);
			float rpm = aiming ? HandHookRpmAim : HandHookRpmIdle;
			HandHookAimingNow = aiming;
			HandHookRpmNow = rpm;
			_handHookAngle += dt * rpm / 60f * 6.2831855f;
			float c = (float)Math.Cos(_handHookAngle);
			float s = (float)Math.Sin(_handHookAngle);

			Vec3 radial = (plane.s * c + plane.f * s);
			if (radial.LengthSquared < 1e-8f)
			{
				return;
			}
			radial = radial.NormalizedCopy();
			Vec3 hookPos = pivot + radial * HandHookRadius;

			// 🔴 **圆心与身体的关系**（2026-10-08 加；回答"移动时 / 转到某角度看不见"）：
			//    `横偏` = 圆心离**身体轴**（脚底那点）的水平距离 · `内缘` = 横偏 − 半径 ·
			//    `高` = 圆心在脚底以上多高（配合横偏，一眼看出这个圆是绕胸、绕膝还是绕地）。
			//    ⇒ **内缘 < 0 = 这个圆有一大半扫进躯干/腿里** —— 那不是渲染问题，是几何问题：
			//      把 `palm` 调小（圆心别顺着小臂探出去）或 `armout` 调大（整圆离身）。
			//    🔴 **兜底位签名 = 横偏≈0.00 且 高≈1.25** —— 手挂点退成了 `player.Position + 1.25 m`（盆骨）；
			//       这种时候 `pos`/`actual`/`shown` 三个自证**全部"正常"**，只有这两个数 + 手源能看出来
			//       （见下面那条「手源换路」日志 —— 它不用你守着 spinlog 抓，换路那一帧自动落盘）。
			float lat = 0f, hgt = 0f;
			try
			{
				float ddx = pivot.x - player.Position.x, ddy = pivot.y - player.Position.y;
				lat = MathF.Sqrt(ddx * ddx + ddy * ddy);
				hgt = pivot.z - player.Position.z;
			}
			catch (Exception)
			{
			}

			// **手源换路日志**：路径标签变了才打（0.5 s 节流 = 抖动时最多 2 条/秒，不会刷屏）。
			//   放在 `Park` 之后 ⇒ 行里的 `pos=` 就是本帧摆的位（与「开摆」那条同口径）。
			_hook.Park(scene, hookPos, radial);
			Trace("hh:park");
			string srcTag = HandSourceTag(SpellCastInput.LastHandSource);
			if (srcTag != _lastHandSrcTag && _handSrcLogCool <= 0f)
			{
				_handSrcLogCool = 0.5f;
				DebugLogger.Log($"[Grapple] 手源换路 → [{SpellCastInput.LastHandSource}]"
					+ $" 手=({pivot.x:F2},{pivot.y:F2},{pivot.z:F2}) 横偏={lat:F2} 内缘={lat - HandHookRadius:F2} 高={hgt:F2}"
					+ $" | {_hook.ParkState()}");
			}
			_lastHandSrcTag = srcTag;
			if (_handSrcLogCool > 0f)
			{
				_handSrcLogCool -= dt;
			}

			if (!_handHookLogged)
			{
				_handHookLogged = true;
				// 🔴 把**径向基底**逐向量打出来 —— 万一元凶是"基底退化 ⇒ 网格被压成一条线/零体积"，
				//    这一行是唯一能看出来的地方（三个向量应当都是单位长、两两垂直）。
				Mat3 basis = GrappleRope.BasisWithLocalZ(radial);
				DebugLogger.Log($"[Grapple] 手里的钩：开摆 手={pivot.x:F2},{pivot.y:F2},{pivot.z:F2}"
					+ $" 手源=[{SpellCastInput.LastHandSource}] 横偏={lat:F2} 内缘={lat - HandHookRadius:F2} 高={hgt:F2}"
					+ $" 钩={hookPos.x:F2},{hookPos.y:F2},{hookPos.z:F2} 半径={HandHookRadius:F2} rpm={rpm:F0}（待机{HandHookRpmIdle:F0}/瞄准{HandHookRpmAim:F0} 现={ (aiming ? "瞄准" : "待机") }）"
					+ $" 轴模式={HandHookAxis} 轴=({axis.x:F3},{axis.y:F3},{axis.z:F3}) 轴外移={HandHookAxisOffset:F2}"
					+ $" 臂骨=[{SpellCastInput.LastArmBones}]"
					+ $" 径向=({radial.x:F3},{radial.y:F3},{radial.z:F3})"
					+ $" 基底 s=({basis.s.x:F3},{basis.s.y:F3},{basis.s.z:F3})"
					+ $" f=({basis.f.x:F3},{basis.f.y:F3},{basis.f.z:F3})"
					+ $" u=({basis.u.x:F3},{basis.u.y:F3},{basis.u.z:F3})"
					+ $" | {_hook.ParkState()}");
			}

			// 限频帧日志（默认关）：判"某个角度看不见"是**被身体/手臂遮挡**还是**摆位失效**
			// —— actual 跟着角度正常画圆 = 遮挡（把 armout / armaxis 的轴外移调大）；
			//    actual 在某角度跳走 = 摆位真的坏了（把这一行发我）。
			// 每帧抓帧（`spinlog 1 [秒]`）：**命令置位后由这里真正开抓**（计数清零），到点自动关 ——
			// 抓的正是"钩在圆周上瞬间不见"那一帧。
			if (!HandHookLog)
			{
				_handHookLogArmed = false;      // 关掉即复位 ⇒ 可以**反复武装**（连敲 spinlog 1 = 重新抓一轮）
			}
			if (HandHookLog && !_handHookLogArmed)
			{
				_handHookLogArmed = true;
				_handHookLogFrame = 0;
				_handHookLogLeft = MathF.Max(0.2f, HandHookLogSeconds);
				DebugLogger.Log($"[Grapple] spinlog 开抓（{_handHookLogLeft:F1}s，每帧一行）");
			}
			if (HandHookLog && _handHookLogArmed)
			{
				_handHookLogLeft -= dt;
				_handHookLogFrame++;
				DebugLogger.Log($"[Grapple] spinlog #{_handHookLogFrame} 角度={((_handHookAngle * 57.29578f) % 360f + 360f) % 360f:F0}°"
					+ $" rpm={rpm:F0}({(aiming ? "aim" : "idle")}) pos={Fmt(hookPos)} 径向=({radial.x:F2},{radial.y:F2},{radial.z:F2})"
					+ $" 手源=[{SpellCastInput.LastHandSource}] 横偏={lat:F2} 内缘={lat - HandHookRadius:F2} 高={hgt:F2}"
					+ $" | {_hook.SpinFrameInfo()}"
					+ $" | rig[kids={_rig.ChildCount}]"
					+ $" | ropeB[{_rope.SpinFrameInfo()}]"
					+ $" | ropeA[{_ropeA.SpinFrameInfo()}]");
				if (_handHookLogLeft <= 0f)
				{
					HandHookLog = false;
					_handHookLogArmed = false;
					DebugLogger.Log($"[Grapple] spinlog 抓帧结束（共 {_handHookLogFrame} 帧 / {HandHookLogSeconds:F1}s）");
				}
			}

			// 绳的**近端 = 右手**（见 GetRopeAnchor 的事故记录）；绳默认不开，见 HandRopeEnabled。
			if (!HandRopeEnabled)
			{
				if (_handHookRopeShown)
				{
					_handHookRopeShown = false;
					_rope.Hide();
				}
				return;
			}

			// 🔴 **手里那截必须绷直**（用户 2026-10-08）：余量压到 1.0（目标绳长 = 跨度）+ 下限压到 0.1
			//    （绳默认下限 0.3 > 手里跨度 0.25 ⇒ 不改的话必然松）。发射时（ThrowInternal）切回飞行那档。
			_rope.SlackRatio = HandRopeSlackRatio;
			_rope.MinLength = HandRopeMinLength;

			Vec3 ropeAnchor = GetRopeAnchor();
			// 🔴 `|| !_rope.IsVisible` 是**必需**的（2026-10-08 实机：发射落地后绳再也不出现）：
			//    `Release()` / `Anchor()` 会绕过 `_handHookRopeShown` 直接 Hide() ⇒ 标志卡在 true、
			//    这里只 Tick 不 Show（而 Tick 在隐藏时直接 return）⇒ 绳永久看不见。以**绳自己的状态**为准。
			if (!_handHookRopeShown || !_rope.IsVisible)
			{
				Trace("hh:B:build");
				if (!_rope.Build(scene))
				{
					return;
				}
				_rope.Show(ropeAnchor, hookPos);
				_handHookRopeShown = true;
				Trace("hh:B:show");
			}
			_rope.Tick(dt, ropeAnchor, hookPos);
			Trace("hh:B:tick");
		}

		/// <summary>绕转轴（世界坐标、单位向量），见 <see cref="HandHookAxis"/>。
		/// 三种都只用**已验证过的读法**（`LookDirection` / 世界轴），不碰新骨骼（避免再撞 AV）。</summary>
		private static Vec3 HandHookAxisDir(Agent player)
		{
			Vec3 fwd = player.LookDirection;
			if (fwd.LengthSquared < 1e-6f)
			{
				fwd = Vec3.Forward;
			}
			fwd = fwd.NormalizedCopy();

			switch (HandHookAxis)
			{
				case 1:
				{
					// 体侧前后甩：轴 = 角色右方向（= f × u，与 BasisWithLocalZ 同款约定）
					Vec3 r = Vec3.CrossProduct(fwd, Vec3.Up);
					return r.LengthSquared < 1e-6f ? Vec3.Forward : r.NormalizedCopy();
				}
				case 2:
					return fwd;                      // 左右横扫：轴 = 朝向 ⇒ 圆在正面（左右）面里
				case 3:
				{
					// 🔴 **上臂弦（肩 → 手）** —— ⚠️ 手臂一折这条弦就不是小臂了（用户当场指出）⇒ 优先用 4
					if (SpellCastInput.TryGetArmAxis(player, out Vec3 armAxis))
					{
						return armAxis;
					}
					return Vec3.Up;
				}
				case 4:
				{
					// 🔴🔴 **小臂轴（肘 → 手）** —— 用户 2026-10-07 晚的最终口径：
					// "(手臂)可能是折的，要以**手肘到手**的连线为轴，再向外探一点点"。
					// 骨索引走官方语义接口 `GetRealBoneIndex(HumanBone.ForearmR / HandR)`。
					if (SpellCastInput.TryGetForearmAxis(player, out Vec3 foreAxis))
					{
						return foreAxis;
					}
					return Vec3.Up;                  // 读不到就退回竖直（不让圆乱翻）
				}
				default:
					return Vec3.Up;                  // 世界竖直：手垂在体侧时 = 绕小臂转
			}
		}

		/// <summary>命令用（`custom.grapple spin` 无参）：把"手里那枚实体"的状态读成一行 ——
		/// 回答"到底召唤出实体没有 / 网格挂上没"（别靠猜）。</summary>
		public string ParkStateLine()
		{
			return _hook.ParkState() + " | " + _rig.Describe()
				+ " | ropeA[ring->hand]: " + SafeStatus(_ropeA)
				+ " | ropeB[hand->hook]: " + SafeStatus(_rope);
		}

		/// <summary>绳状态的安全读取（命令回执用；单行，出错不抛）。</summary>
		private static string SafeStatus(GrappleRope rope)
		{
			try
			{
				return rope == null ? "(null)" : rope.Status();
			}
			catch (Exception ex)
			{
				return "(status failed: " + ex.GetType().Name + ")";
			}
		}

		/// <summary>
		/// 命令 `custom.grapple hand &lt;0..4&gt;` 的落点：换档。
		/// 🔴 必须走这里而不是直接写 <see cref="HandProbeStage"/> —— 要顺带重置
		/// 「档 1 只摆一次」的闸门与日志闸门，否则换回档 1 时它不会重新摆位（看着像"没反应"）。
		/// </summary>
		public void SetHandProbe(int stage)
		{
			HandProbeStage = stage;
			_handProbePlaced = false;
			_handProbeLoggedStage = -1;
		}

		/// <summary>
		/// 诊断阶梯的档 1~3（表见 <see cref="HandProbeStage"/>）—— **只摆位**：
		/// 不判"握着没握着"、不看总开关、不碰绳（绳开着就先收掉，免得它的弧线把判读搅浑）。
		/// 档 4 不在这里 —— 它落进 <see cref="TickHandHook"/> 那条正常路，保证"就是现行为"。
		/// </summary>
		private void TickHandProbe(float dt)
		{
			Agent player = Agent.Main;
			Scene scene = Mission != null ? Mission.Scene : null;
			if (player == null || scene == null)
			{
				return;
			}

			if (_handHookRopeShown)
			{
				_handHookRopeShown = false;
				_rope.Hide();
			}

			if (HandProbeStage == 1 || HandProbeStage == 2)
			{
				// 身前 2 m / 高 1.2 —— **与 `custom.spawn_mesh` 逐字同款**（那件已知可见 ⇒ 这档就是对照组）
				Vec3 fwd = player.LookDirection;
				float hl = MathF.Sqrt(fwd.x * fwd.x + fwd.y * fwd.y);
				Vec3 flat = hl > 1e-3f ? new Vec3(fwd.x / hl, fwd.y / hl, 0f) : new Vec3(0f, 1f, 0f);
				Vec3 pos = player.Position + flat * 2f + Vec3.Up * 1.2f;

				if (HandProbeStage == 1 && _handProbePlaced)
				{
					return;      // 档 1 = **只摆一次**（spawn_mesh 的语义：摆完就不管了，人走开它就留在原地）
				}
				_handProbePlaced = true;
				_hook.Park(scene, pos, Vec3.Up, applyFrameEveryTick: true, identityRotation: true);
				LogHandProbe($"pos=({pos.x:F2},{pos.y:F2},{pos.z:F2}) 身前 2m/高 1.2m"
					+ (HandProbeStage == 1 ? " · 只摆一次" : " · 每帧摆"));
				return;
			}

			// 档 3：**右手骨上方** —— 与阴魔斩蓄力球同一读法（GetHand → TryGetRightHandAnchor，带 3 m 兜底）
			Vec3 hand = GetHand();
			_hook.Park(scene, hand, Vec3.Up, applyFrameEveryTick: true, identityRotation: true);
			LogHandProbe($"pos=({hand.x:F2},{hand.y:F2},{hand.z:F2}) 右手骨上方 · 每帧摆");
		}

		/// <summary>每档只打一条（带数字，供事后比对）；换档时由 <see cref="SetHandProbe"/> 重开闸门。</summary>
		private void LogHandProbe(string what)
		{
			if (_handProbeLoggedStage == HandProbeStage)
			{
				return;
			}
			_handProbeLoggedStage = HandProbeStage;
			DebugLogger.Log($"[Grapple] hand probe {HandProbeStage} → {what} | {_hook.ParkState()}");
		}

		/// <summary>瞄准/蓄力那两条动作的索引（懒解析一次；<see cref="ActionIndexCache.act_none"/> = 没注册）。</summary>
		private static ActionIndexCache _idxAimReady;
		private static ActionIndexCache _idxAimHold;
		private static bool _idxAimTried;

		/// <summary>
		/// 玩家此刻在**瞄准 / 蓄力**吗 —— 决定手里那枚钩用哪档转速（<see cref="HandHookRpmAim"/> / <see cref="HandHookRpmIdle"/>）。
		///
		/// 判据 = **当前动作索引命中我们的 `act_grapple_ground_ready` 或 `_hold`**（ch0 / ch1 都查 ——
		/// 弓系动作跑在通道 1，见方案 §D-0「通道真相」；两条都查 = 通道安排变了也不会失效）。
		/// 🔴 **为什么不用 `GetCurrentActionStage == AttackReady`**：stage 是引擎按 `action_types.xml` 分类出来的，
		///    **别的远程武器（真弓）也会是 AttackReady**；而索引是"我们自己在播什么"，一一对应、不会误判。
		/// ⚠️ 前提 = 这两条动作确实被引擎播着（瞄准时 usage 链会自动播 ready→hold；`custom.do_anim` 手动播的
		///    那种也算"在瞄"——那是调试动作，不影响）。
		/// </summary>
		private static bool IsAimingGrapple(Agent player)
		{
			if (player == null)
			{
				return false;
			}
			if (!_idxAimTried)
			{
				_idxAimTried = true;
				try
				{
					_idxAimReady = ActionIndexCache.Create("act_grapple_ground_ready");
					_idxAimHold = ActionIndexCache.Create("act_grapple_ground_hold");
				}
				catch (Exception)
				{
				}
				if (_idxAimReady == ActionIndexCache.act_none || _idxAimHold == ActionIndexCache.act_none)
				{
					DebugLogger.Log("[Grapple] 瞄准档转速：动作名解析失败（ready/hold 没注册？）—— 一直用待机档");
				}
			}
			bool hasReady = _idxAimReady != ActionIndexCache.act_none;
			bool hasHold = _idxAimHold != ActionIndexCache.act_none;
			if (!hasReady && !hasHold)
			{
				return false;
			}
			try
			{
				for (int ch = 0; ch <= 1; ch++)
				{
					ActionIndexCache cur = player.GetCurrentAction(ch);
					if ((hasReady && cur == _idxAimReady) || (hasHold && cur == _idxAimHold))
					{
						return true;
					}
				}
			}
			catch (Exception)
			{
			}
			return false;
		}

		/// <summary>此刻"手上拿着钩索"吗？判据 = `Agent.WieldedWeapon` 是**绳**（`taikou_grapple_rope`）
		/// **或钩**（`taikou_grapple_hook`，= 弹药那件）。
		/// 🔴 2026-10-07 实机（"瞄准时钩看不见"）：**瞄准期间引擎把 `WieldedWeapon` 报成那支**（弹药），
		///   只判本体 ⇒ 一瞄准钩就被收掉（拔刀那一瞬还在，日志可证）。两件都认即可。
		/// 换到别的武器（刀/弓）时两件都不匹配 ⇒ 正常收掉 ✓。</summary>
		private static bool IsHoldingGrapple()
		{
			try
			{
				Agent main = Agent.Main;
				if (main == null)
				{
					return false;
				}
				MissionWeapon wielded = main.WieldedWeapon;
				string id = wielded.Item != null ? wielded.Item.StringId : null;
				return id == GrappleFirePatch.RopeItemId || id == GrappleFirePatch.HookItemId;
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>绳的**近端**锚点 = **右手**（= 开火/出手点，与 <see cref="GetHand"/> 同一套**已被实机验证**的读法）。
		/// 🔴 2026-10-07 事故记录（别再照抄那条路）：我一度把它改成"左手的环"（读 `Monster.OffHandItemBoneIndex`
		///    + `GetBoneEntitialFrame`）—— 那是**本项目第一次**读左手骨，结果**拔刀后第一帧就 AccessViolation**
		///    （栈顶 = 托管→本机转换、崩在 `MissionState.TickMission` 里；引擎日志最后一行 = `Render Requested: taikou_grapple_hook`）。
		///    ⇒ 恢复成右手（已验证）；"绳系在左手环上"那条观感**等有稳妥的左手骨读法再说**（要动就得先单独验证那个 API）。
		/// 画面口径：绳从**右手**（捏绳那只手）连到钩 —— 左手的环是独立道具，不参与绳的锚点。</summary>
		private Vec3 GetRopeAnchor()
		{
			return GetHand();
		}

		/// <summary>手的世界位置（复用蓄力球那套挂点读取；取不到退回"身体坐标 + 抬一点"）。
		/// 🔴 **2026-10-08：把"蓄力球口径"的上抬减掉**，再**沿小臂外移到掌心**（<see cref="HandPalmOffset"/>）——
		///    三处（绳的近端 / 钩的圆心 / 开火起点）共用这一个点，所以只需调一个旋钮。
		///    （原 0.18 上抬是给阴魔斩球的：球挂手上方好看；钩/绳拿它当支点就会比掌心靠内靠上。）
		/// ⚠️ 球那边不受影响（它自己走 <see cref="SpellCastInput.TryGetRightHandAnchor"/>）。</summary>
		private Vec3 GetHand()
		{
			Agent player = Agent.Main;
			if (player == null) return _anchor;
			Vec3 hand;
			if (!SpellCastInput.TryGetRightHandAnchor(player, out hand))
			{
				// 🔴 **读失败时先拿"上一次读到的好手点"顶着**（窗口 = <see cref="HandHoldSeconds"/>）——
				//    2026-10-08 实机（日志实锤）：骨读数一旦被闸门拒，这里就直掉**脚底/盆骨** ⇒
				//    钩与绳双双甩到脚踝（用户症状："偶尔消失" / "走动时看不见"）。
				//    顶着的点**按 agent 的位移平移**（人走它也走）；超窗口还读不到才认输、退老兜底位。
				if (_lastGoodHandAge <= HandHoldSeconds)
				{
					SpellCastInput.LastHandSource = "hold " + SpellCastInput.LastHandSource;
					return _lastGoodHand + (player.Position - _lastGoodAgentPos);
				}
				return player.Position + Vec3.Up * FallbackHandLift;
			}
			hand -= Vec3.Up * SpellCastInput.HandAnchorUpOffset;      // 去掉蓄力球口径的上抬 → 回到手骨原点（腕关节）

			// 沿**小臂方向**外移到掌心（读不到就保持腕关节，不乱挪）
			if (Math.Abs(HandPalmOffset) > 0.0005f
				&& SpellCastInput.TryGetForearmAxis(player, out Vec3 foreArm))
			{
				hand += foreArm * HandPalmOffset;
			}
			_lastGoodHand = hand;                    // 记下这次的好点（读失败时靠它顶）
			_lastGoodAgentPos = player.Position;
			_lastGoodHandAge = 0f;
			return hand;
		}
	}
}
