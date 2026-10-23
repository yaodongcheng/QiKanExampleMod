using System;
using System.Collections.Generic;
using LivingWorldNpcs.Animation;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
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
			Binding,    // 🔴 勾人（步骤 5，2026-10-09）：缠 → 倒 → 缚 → 解，全交给 GrappleBind
			Pulling,    // 拉自己中（步骤 4：冻结 + 木板 + 曲线）
			Returning,  // 🔴 打空返程（2026-10-08）：钩头追着手飞回，到了交回手里那套
		}

		/// <summary>钩索正在忙（钩头在飞 / 已挂住 / 拉拽中）—— 瞄准相机用它决定
		/// "瞄准结束但流程未完（已开火、拉拽相机还没接手）时先别还相机"（见 GrappleAimCamera.Tick）。</summary>
		public bool IsBusy => _hookPhase != HookPhase.Idle;

		/// <summary>
		/// **瞄准相机该不该继续拿着镜头**（2026-10-10 加）。
		///
		/// 🔴 与 <see cref="IsBusy"/> 只差一条：**钩子挂在"绳另一头那个人"身上时（<see cref="HookPhase.Binding"/>）不算忙**。
		///    理由（实机事故）：那一拍是**长期状态** —— NPC 永不自己挣脱（2026-10-09 裁定）＋ 扛着人走路都在这一相里。
		///    镜头一直被 <see cref="GrappleAimCamera"/> 拿着 ⇒ 按 CLAUDE.md 铁律 35，
		///    **`CustomCamera != null` 时引擎整段跳过鼠标 look** ⇒ 玩家**转不了视角**，而移动是**相对相机**的
		///    ⇒ 按 W 会朝"那个被锁住的相机前方"走（用户报的原话："扛起人之后按 W 前进，却在朝着左方向走"）。
		///    其余相位（飞 / 钉住 / 拉自己 / 打空返程）都是秒级的，镜头该拿着 —— 保持原行为。
		/// </summary>
		public bool IsAimCameraBusy => _hookPhase != HookPhase.Idle && _hookPhase != HookPhase.Binding;

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

		/// <summary>
		/// **这一套钩索的使用者**（2026-10-23 立，NPC 平权）：玩家那套 = null（运行时取 `Agent.Main`）；
		/// NPC 那套 = 固定的 agent。每个使用者各有一套绳/钩/状态/动画，互不共用。
		/// </summary>
		private readonly Agent _npcOwner;

		/// <summary>本套钩索所属的 Mission —— NPC 那套不挂在引擎的行为列表里，拿不到 `Mission` 属性。</summary>
		private readonly Mission _sessionMission;

		/// <summary>NPC 那套的静态登记表（按 `Agent.Index`；用 Agent 引用复核，防编号复用）。</summary>
		private static readonly Dictionary<int, GrappleLogic> _sessions = new Dictionary<int, GrappleLogic>();

		/// <summary>每帧推进用的临时表（避免在遍历时改字典）。</summary>
		private static readonly List<GrappleLogic> _sessionScratch = new List<GrappleLogic>();

		/// <summary>玩家那套（引擎每帧调的那一个）。</summary>
		public bool IsPlayerOwner => _npcOwner == null;

		/// <summary>NPC 那套已经收摊了吗（防重复拆实体）。</summary>
		private bool _terminated;

		/// <summary>这一套的使用者（玩家那套动态取 `Agent.Main`）。</summary>
		public Agent Owner
		{
			get
			{
				try { return _npcOwner ?? Agent.Main; }
				catch (Exception) { return _npcOwner; }
			}
		}

		/// <summary>本套所属 Mission（替代基类的 `Mission` 属性，供不登记为引擎行为的 NPC 那套使用）。</summary>
		private Mission MissionOf => _sessionMission ?? Mission.Current;

		/// <summary>这一套正绑着人吗 / 正拉着自己吗 / 绑的是谁（AI 判据用）。</summary>
		public bool HasBoundTarget => _bind.IsActive;
		public bool IsPullingSelf => _pull.IsActive;
		public Agent BoundTarget => _bind.Target;

		/// <summary>这一套是否"正在干活"（钩头忙 / 绑人 / 拉自己 / 扛人 / 出手租约）。</summary>
		public bool IsSessionBusy => IsBusy || _bind.IsActive || _bind.HasPendingRelease
			|| _pull.IsActive || _carry.IsActive || _npcKit.Active;

		// ── NPC 视觉降级（2026-10-23，用户裁定：远处可以不渲染或简化）──
		/// <summary>≤ 这个距离：全渲染（手环 + 绳 A + 绳 B + 手里的钩）。</summary>
		public static float NpcFullVisualDistance = 25f;
		/// <summary>> 这个距离：不渲染（隐藏绳/手部道具；机制照常跑）。</summary>
		public static float NpcVisualDistance = 80f;

		private float _npcLodTimer;
		private int _npcVisualLevel;

		public GrappleLogic() : this(Mission.Current, null)
		{
		}

		public GrappleLogic(Mission mission, Agent npcOwner)
		{
			_sessionMission = mission;
			_npcOwner = npcOwner;

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

			InitSession();
		}

		/// <summary>姿态动画状态机实例（幂等；玩家那套在构造时建，`OnCreated` 再调一次也无害）。</summary>
		private void InitSession()
		{
			if (_anim != null) return;
			_animCtx = new GrappleAnimContext();
			_anim = AnimMachineRegistry.Create(GrappleAnimMachine.Name, _animCtx);
			_pull.WaitAnimExit = AnimExited;      // 落地动作没演完就先别拆板（GrapplePull.TickSettling 的等待门）
		}
		private HookPhase _hookPhase = HookPhase.Idle;
		private Vec3 _aimPoint;                       // 本次发射的瞄准点（命中点）
		private bool _aimHitAgent;                    // 瞄准点是不是人（是人就不解算落点平台）

		/// <summary>NPC 发射时跳过相机瞄准：由 <see cref="ThrowAtForOwner"/> 填这三个字段。</summary>
		private bool _forcedAimActive;
		private Vec3 _forcedAim;
		private bool _forcedAimAgent;
		private string _launchSource = "command";

		/// <summary>下一次"钩中人"时给 <see cref="GrappleBind.AiBindHoldSeconds"/> 的值（AI 绑人自动放开）。</summary>
		private float _pendingAiBindHoldSeconds;
		private GrappleLanding.Result _landing;       // 命中后解算的落点（飞行终点）
		private string _landingNote = "-";            // 落点解算的一行摘要（dump 用）

		// ── 拉自己（步骤 4）──
		private readonly GrapplePull _pull = new GrapplePull();

		/// <summary>**勾人**（步骤 5，2026-10-09）：目标侧状态机（缠 → 倒 → 缚 → 解）。
		/// 判定与结算全在它里面（铁律 18 平权），这里只负责"把钩的相位与绳的远端接过去"。</summary>
		private readonly GrappleBind _bind = new GrappleBind();

		/// <summary>扛人配对（步骤 6，2026-10-10 立）：扛人者 + 被扛者两台状态机的对齐 + 每帧摆位。</summary>
		private readonly GrappleCarry _carry = new GrappleCarry();

		/// <summary>放人时如果正扛着：先演"放下"，演完再松绳（2026-10-23 用户："release 要播被放下动作"）。</summary>
		private bool _releaseAfterCarry;
		private string _releaseAfterCarryReason;

		/// <summary>NPC 出手的一次租约（2026-10-23）：装备 / 动作集 / ready-hold-release。</summary>
		private readonly GrappleNpcKit _npcKit = new GrappleNpcKit();

		/// <summary>NPC 那套手里那盘道具要不要摆（由 <see cref="GrappleNpcKit"/> 的租约开关）。</summary>
		public bool NpcHandKitEnabled { get; set; }

		/// <summary>这一套的手部道具总闸：玩家看全局开关，NPC 看自己的租约。</summary>
		private bool HandKitWanted => IsPlayerOwner ? HandHookEnabled : NpcHandKitEnabled;

		/// <summary>这一次捆绑是不是 `custom.grapple bind test`（调试路径）开的 ——
		/// 那条路径玩家没拿钩索、目标还可能在 30 米外 ⇒ "收起钩索/绳被拉断"两条出口对它无意义（会当场把自己踢掉）。</summary>
		private bool _bindFromTest;
		private float _attachTimer;                   // 钩头钉住后过了多久（蓄势计时）
		private bool _attachFromAir;                  // 这一钩是不是"空中起钩"（决定蓄势/拉升时长的基准）
		private bool _attachAutoPull;                 // 这一钩钉住后要不要自动拉（武器开火 = 要；命令 throw = 不要）
		private float _mountWait;                     // "骑马勾到地形 ⇒ 自动下马"等了多久（秒；0 = 没在等）
		private bool _mountedBlowFired;               // 这一钩的"催下马 blow"打过了吗（只打一次，见调用点）

		/// <summary>
		/// **骑马勾到地形时：自动下马，然后把下马藏进"蓄势"里直接起飞**（2026-10-13 用户拍板，取代 §13.11 的"直接收钩"）。
		///
		/// 🔴 **为什么必须先下马**（硬约束，不是设计选择）："拉自己"要**冻结玩家 + 用底板托起来**
		///    （见 <see cref="GrapplePull.Start"/>），而骑在马上时**骑手的位置由马决定**
		///    （`Agent.TeleportToPosition` 会把马一起搬走）、切控制权会乱坐骑状态机 ⇒ 不下马就拉不动。
		///
		/// 🔴 **下马动画藏在哪里**：钩一咬住地形就点引擎的"下马"事件，人演完翻身上/下马的那一拍
		///    正好落在本来就要等的"蓄势"窗口里；**脚一落地（`HasMount` 变 false）立刻起飞** ——
		///    玩家不用自己按 E、也不用多等一秒。
		/// 关掉 = 回到 2026-10-09 的旧行为（直接收钩、什么都不发生）。
		/// </summary>
		public static bool MountedPull = true;

		/// <summary>等他下马的超时（秒）——超时 = 这一钩作废（收钩返程），别把人晾在马上。</summary>
		public static float MountedDismountTimeoutSeconds = 2.5f;

		/// <summary>
		/// **玩家自己骑马钩到地形时，要不要额外打一发 `CanDismount` blow 催下马**（默认**开**，2026-10-19）。
		///
		/// 🔴 为什么加它（实机，日志 `09:47:23.776` / `09:47:31.058`）：光点引擎的"下马"旗标
		///    （`EventControlFlag.Dismount`，每帧重发）在**玩家**身上被顶着不放行 —— 连打两枪都是
		///    "2.5 s 内没能下马 ⇒ 这一钩作废"。而 blow 是 **native 直接执行、不看马速**
		///    （目标侧实测 0.20 s 落地，2026-10-13）。
		/// 代价（已知并接受）：玩家身上走一次真实打击管线 —— 血不掉（`FireDismountBlow` 内部临时 Immortal），
		///    但会播"被击落"的受击反应（正好读作"绳一绷把人扯下马"），`OnAgentHit` 会广播一发（攻击者传 −1）。
		/// 关掉 = 回到"只点旗标"的老路（`custom.grapple mountedselfblow 0`，现场 A/B 用）。
		/// </summary>
		public static bool MountedSelfBlow = true;

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

		// ── 音效（2026-10-10）──

		/// <summary>抛出瞬间的甩绳声开关（命令 `custom.grapple sound off|on` 热切）。</summary>
		public static bool RopeSoundEnabled = true;

		/// <summary>绳声事件名（`ModuleData/module_sounds.xml` 里声明的名字；空 = 不播）。</summary>
		public static string RopeSoundName = "lwn_grapple_rope";

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
		/// **过程段（`act_grapple_ground_pull`）的总长**（秒）= `lwn_core.tpac` 里那条 clip 的实测时长
		/// （2026-10-17 `tpaccli clipinfo` 量的：**1.30 s**，源帧 2→40 @30fps）。
		/// 用途 = 把"过程段内第几秒"换算成 `Force(..., startProgress)` 要的 0~1 比例（见 <see cref="PullAnchorIntoClip"/>）。
		/// ⚠️ 用户每重切一次这条动画，它要跟着改（与 <see cref="ReleaseSeconds"/> 同款纪律）。
		/// </summary>
		public static float PullClipSeconds = 1.30f;

		/// <summary>
		/// **过程段里"人开始被带走"的那一拍，距这段动画开头多少秒** = <see cref="PullStartSeconds"/> − <see cref="ReleaseSeconds"/>
		/// = 1.133 − 0.566 = **0.567 s**（0~1 比例 ≈ 0.436）。
		///
		/// 🔴 为什么需要它（2026-10-17，用户口径 A「落地即飞」）：地面钩锁里过程段是"开火后 0.566 进、
		///    位移在 0.567 处发生"—— 也就是说**位移永远落在过程段播到 0.567 秒的那一刻**。
		///    骑马钩地形时位移被"下马"推迟（T = max(开火+1.133, 下马完成)），若过程段仍按开火时刻进场，
		///    等能飞的时候它已经播过头了 ⇒ 观感错位。
		///    ⇒ 做法 = **等到起飞那一刻再进场，并直接从这一拍开始播**（`startProgress = 0.567 / 1.30`），
		///    这样"位移 ↔ 姿态"的关系与地面**逐帧一致**，且不额外等那 0.567 秒"使劲"（用户选的 A 口径）。
		/// </summary>
		public static float PullAnchorIntoClip =>
			MathF.Max(0f, MathF.Min(PullClipSeconds, PullStartSeconds - ReleaseSeconds));

		/// <summary>
		/// **release 动作的长度**（秒，= 本模块资产包 `lwn_core.tpac` 里 clip `grapple_ground_release` 的时长；**2026-10-07 晚重切后 = 0.566 s**）：
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

		/// <summary>
		/// **骑马待机档**转速（转/分）—— 骑在马上、**没在瞄准**时手里那枚钩转多快。
		/// 🔴 **默认 0 = 不转**（2026-10-23 用户裁定）：
		///    · 站地上维持原样（仍然用 <see cref="HandHookRpmIdle"/> 135）；
		///    · 骑马时手臂姿势与站地上完全不同（小臂方向变了）⇒ 钩绕那个平面转观感不对 ⇒ **停住**；
		///    · **瞄准时照旧转**（<see cref="HandHookRpmAim"/> 270），两个状态都要有"甩起来"的读感。
		/// 判据 = `Agent.HasMount` 且 `!IsAimingGrapple`（见 <see cref="TickHandHook"/>）。
		/// 现场调：`custom.grapple spin mounted &lt;rpm&gt;`（0 = 停 · 135 = 跟站地上一样 · 负值 = 反向）。
		/// </summary>
		public static float HandHookRpmMountedIdle = 0f;

		/// <summary>此刻玩家骑着马吗（上一帧 <see cref="TickHandHook"/> 读到的值；命令回显用）。</summary>
		public static bool HandHookMountedNow;

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
		/// 🔴 **默认值 = 4（小臂轴）**（2026-10-07 晚用户裁定，2026-10-23 复核保留）：
		///    **小臂读得到就永远用小臂**（读法见 <see cref="SpellCastInput.TryGetForearmAxis"/>）；
		///    **只有读不到时**才退回"体侧前后甩"（见 <see cref="HandHookAxisDir"/> —— 旧兜底是世界竖直，
		///    轴从朝下变朝上会让转向整个翻过来，2026-10-23 改掉）。
		///    上面 · 里写的"0（默认）"作废，0/1/2/3 都只是备选对照档（`custom.grapple armaxis <n>` 现场切）。
		public static int HandHookAxis = 4;

		/// <summary>「小臂读不到 ⇒ 已退回前后甩」的状态（只在**翻转那一刻**打一行日志，不刷屏）。</summary>
		private static bool _armAxisFallbackOn;

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
		public static float HandPalmOffset = 0.20f;

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

		/// <summary>
		/// **过程姿态被"压住"了吗**（2026-10-17，骑马口径 A）：release 演完时如果玩家还在马上、
		/// 这一钩又勾到了地形（= 正在等引擎放行下马），就**先不进过程段** —— 理由两条：
		/// ① 此刻引擎的下马动作（Priority 64）正在占 0 号通道，我们过程段（30）写了也会被压 / 互相闪；
		/// ② 真正起飞还要等马下来，过程段早播的时间全是浪费（起飞时已越过"被拽走"那一拍）。
		/// ⇒ 压到"已下马"那一帧，用 <see cref="PullAnchorIntoClip"/> 换算的 `startProgress` **从中段进**。
		/// 地面钩锁 / 打空 / 勾到人 / 命令路径**都不受影响**（那些情况不会置位）。
		/// </summary>
		private bool _animPullHeld;
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
		/// **拿某个 agent 的那一套钩索**（没有就现建一套，2026-10-23 立）。
		/// 玩家 ⇒ <see cref="Ensure()"/>（引擎每帧会调它）；NPC ⇒ 建一个**不登记为 MissionBehavior** 的普通对象，
		/// 由 <see cref="NpcGrappleDirector"/> 每帧调 <see cref="TickSession"/>。
		/// </summary>
		public static GrappleLogic EnsureFor(Agent owner)
		{
			if (owner == null) return null;
			if (ReferenceEquals(owner, Agent.Main)) return Ensure();
			Mission mission = Mission.Current;
			if (mission == null) return null;
			if (_sessions.TryGetValue(owner.Index, out GrappleLogic existing)
				&& existing != null && !existing._terminated && ReferenceEquals(existing.Owner, owner))
			{
				return existing;
			}
			GrappleLogic session = new GrappleLogic(mission, owner);
			_sessions[owner.Index] = session;
			return session;
		}

		/// <summary>查某个 agent 的那一套钩索（不新建；玩家 = <see cref="Current"/>）。</summary>
		public static GrappleLogic GetSession(Agent owner)
		{
			if (owner == null) return null;
			if (ReferenceEquals(owner, Agent.Main)) return Current;
			if (_sessions.TryGetValue(owner.Index, out GrappleLogic s)
				&& s != null && !s._terminated && ReferenceEquals(s.Owner, owner))
			{
				return s;
			}
			return null;
		}

		/// <summary>这个 agent 此刻是不是正用着钩索（钩头在飞/挂着/绑着人）。</summary>
		public static bool IsAgentInGrapple(Agent agent)
		{
			GrappleLogic s = GetSession(agent);
			return s != null && (s.IsBusy || s._bind.IsActive);
		}

		/// <summary>玩家此刻是不是正被别的一根绳绑着（NPC 绑玩家时，玩家自己的钩索要让位）。</summary>
		public static bool IsPlayerBoundByOtherRope()
		{
			Agent player = Agent.Main;
			return player != null && GrappleBind.IsTargetClaimed(player);
		}

		/// <summary>场景结束/收摊：把所有 NPC 那套清掉（玩家那套走 <see cref="OnEndMission"/>）。</summary>
		public static void TerminateAllNpcSessions(string reason)
		{
			if (_sessions.Count == 0) return;
			GrappleLogic[] list = new GrappleLogic[_sessions.Count];
			_sessions.Values.CopyTo(list, 0);
			for (int i = 0; i < list.Length; i++)
			{
				try { list[i].TerminateSession(reason); } catch (Exception) { }
			}
			_sessions.Clear();
			GrappleBind.ClearAllClaims();
		}

		/// <summary>命令/诊断用：当前所有 NPC 那套（拷贝一份，遍历安全）。</summary>
		public static List<GrappleLogic> GetNpcSessions()
		{
			return new List<GrappleLogic>(_sessions.Values);
		}

		/// <summary>命令/诊断用：一句话状态（谁 / 钩头相位 / 绑着谁）。</summary>
		public string SessionStateLine()
		{
			Agent o = Owner;
			string owner = o != null ? $"{o.Name}#{o.Index}" : "(gone)";
			string target = _bind.Target != null ? _bind.Target.Name?.ToString() : "-";
			return $"[{owner}] phase={_hookPhase} bind={_bind.Current} target={target} busy={IsBusy}";
		}

		/// <summary>放掉这一套正绑着的人（命令用；不终止整套）。</summary>
		public bool ReleaseBindNow(string reason)
		{
			if (!_bind.IsActive) return false;

			// 🔴 2026-10-23：正扛着人 ⇒ 先演"放下/被放下"（beikangqi_reverse），演完再松绳。
			//    直接 Release 会走 `_carry.HardStop`，把放下动画当场切掉（用户实机："没有播放下的动作"）。
			if (_carry.IsActive)
			{
				if (_carry.Phase == GrappleCarry.CarryPhase.PuttingDown
					|| _carry.PutDown(_bind, reason))
				{
					_releaseAfterCarry = true;
					_releaseAfterCarryReason = reason;
					DebugLogger.Log($"[Grapple] 放人：正扛着 ⇒ 先演放下（{reason}），演完再松绳");
					return true;
				}
			}

			Release(reason);
			return true;
		}

		/// <summary>每帧：放下动画演完（扛人关系结束）⇒ 真正松绳。</summary>
		private void TickDeferredRelease()
		{
			if (!_releaseAfterCarry) return;
			if (_carry.IsActive) return;
			string reason = _releaseAfterCarryReason ?? "release";
			_releaseAfterCarry = false;
			_releaseAfterCarryReason = null;
			Release(reason);
		}

		/// <summary>每帧推进所有 NPC 那套（由 <see cref="NpcGrappleDirector"/> 调）。</summary>
		public static void TickAllNpcSessions(float dt)
		{
			if (_sessions.Count == 0) return;
			_sessionScratch.Clear();
			foreach (GrappleLogic s in _sessions.Values) _sessionScratch.Add(s);
			for (int i = 0; i < _sessionScratch.Count; i++)
			{
				GrappleLogic s = _sessionScratch[i];
				if (s == null) continue;
				Agent owner = s.Owner;
				if (owner == null || !AgentControlHelper.SafeIsActive(owner))
				{
					s.TerminateSession("owner gone");
					continue;
				}
				try { s.TickSession(dt); }
				catch (Exception ex)
				{
					DebugLogger.Log($"[Grapple] NPC 那套 tick 异常（{owner.Name}）：{ex.GetType().Name} {ex.Message} ⇒ 终止");
					s.TerminateSession("tick exception");
				}
			}
			_sessionScratch.Clear();
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
			if (IsPlayerOwner) Current = this;
			InitSession();
		}

		public override void OnBehaviorInitialize()
		{
			base.OnBehaviorInitialize();
			if (IsPlayerOwner) Current = this;
		}

		protected override void OnEndMission()
		{
			base.OnEndMission();

			// 🔴 **反向绑的兜底**（2026-10-13）：玩家被绑着时**场景结束**（战斗结束 / 退出场景）——
			//    控制器必须还回玩家，否则"人已经离场了控制器还在 AI 手里"（下个场景能不能自愈不做赌注）。
			//    只做这一件事：实体 / 状态机 / 旗标都随场景一起走，不用在这里逐个收。
			try
			{
				if (_bind.Target != null && ReferenceEquals(_bind.Target, Agent.Main))
				{
					V.SetPlayerControlFrozen(Agent.Main, false);
				}
			}
			catch (Exception)
			{
			}

			// 玩家那套收摊；顺手把 NPC 那套也清掉（正常由 NpcGrappleDirector 收，这里只是兜底）
			TeardownSession("mission end");
			try { TerminateAllNpcSessions("mission end"); } catch (Exception) { }
			if (IsPlayerOwner && Current == this) Current = null;
		}

		/// <summary>收掉**NPC 那套**（玩家那套只由 <see cref="OnEndMission"/> 收）。</summary>
		public void TerminateSession(string reason)
		{
			if (IsPlayerOwner || _terminated) return;
			_terminated = true;
			TeardownSession(reason);
			if (_npcOwner != null && _sessions.TryGetValue(_npcOwner.Index, out GrappleLogic s) && ReferenceEquals(s, this))
			{
				_sessions.Remove(_npcOwner.Index);
			}
		}

		/// <summary>实体 / 绳 / 钩 / 拉拽 / 绑人 / 扛人的统一收摊（幂等）。</summary>
		private void TeardownSession(string reason)
		{
			try { _rope.ClearTailPin(); _ropeA.ClearTailPin(); } catch (Exception) { }
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
				_pull.Abort(reason);
			}
			catch (Exception)
			{
			}
			try
			{
				_carry.HardStop(reason);
			}
			catch (Exception)
			{
			}
			try
			{
				_bind.Abort(reason);
			}
			catch (Exception)
			{
			}
			try { _bind.RestoreAllActionSets(); } catch (Exception) { }
			// 🔴 租约必须排在 `_pull.Abort` **之后**：板子解冻会把控制器恢复成"它接手时看到的那个"
			//    （而那时已经被租约设成 None）—— 租约最后再还，才能把真正的原控制者还回去。
			try { _npcKit.EndLease(reason); } catch (Exception) { }
			_releaseAfterCarry = false;
			_releaseAfterCarryReason = null;
			_anchored = false;
			_hookPhase = HookPhase.Idle;
		}

		// ─────────────────────────────── 对外操作（命令调） ───────────────────────────────

		/// <summary>
		/// 把绳的另一端钉在世界点 <paramref name="point"/> 上并显示。
		/// <paramref name="ropeLength"/>：绳自身总长（米）；**&lt;=0 = 自动**（跨度 +30%，保证看得见垂度）。
		/// <paramref name="segments"/>：段数；**&lt;=0 = 自动**（按绳长算，每段 ≈18cm）。每段多长由两者相除得来，不用管。
		/// </summary>
		public string Anchor(Vec3 point, float ropeLength = -1f, int segments = -1)
		{
			Scene scene = MissionOf?.Scene;
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
			if (!_rope.Build(MissionOf?.Scene))
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

		/// <summary>
		/// **NPC 的发射入口**（2026-10-23）：瞄准点由调用方给（目标胸口 / 墙上的钩点），不走相机。
		/// 手部动作由 <see cref="GrappleNpcKit"/> 播；本方法只负责"从这只手把钩放出去"。
		/// </summary>
		public string ThrowAtForOwner(Vec3 aim, bool aimAgent, bool autoPull)
		{
			if (Owner == null) return "Error: no owner agent.";
			_forcedAim = aim;
			_forcedAimAgent = aimAgent;
			_forcedAimActive = true;
			try
			{
				return ThrowInternal(false, Vec3.Zero, Vec3.Zero, autoPull);
			}
			finally
			{
				_forcedAimActive = false;
			}
		}

		/// <summary>
		/// **NPC 出手的完整入口**（动作 + 道具 + 到时放钩）：命令/AI 调这个，不是直接调 <see cref="ThrowAtForOwner"/>。
		/// 返回 false + why = 缺件 / 改不了动作集（什么都没动）。
		/// </summary>
		public bool BeginNpcThrow(Vec3 aim, bool aimAgent, bool autoPull, out string why,
			float aiBindHoldSeconds = 0f, Agent aimTarget = null)
		{
			if (IsPlayerOwner)
			{
				why = "this is the player session (use the normal throw path)";
				return false;
			}
			if (_bind.IsActive)
			{
				why = "rope is tied to a bound target - release it first";
				return false;
			}
			// 🔴 起手前先清掉上一钩：否则等下 `ThrowInternal` 里的"清场 Release"会把**新租约**一起收掉
			//    （人已经握好绳、切好动作集，结果钩飞出去时手上的东西没了）。
			if (_hookPhase != HookPhase.Idle) Release("npc throw（起手前清场）");
			_pendingAiBindHoldSeconds = aiBindHoldSeconds;
			return _npcKit.BeginThrow(Owner, this, aim, aimAgent, autoPull, aimTarget, out why);
		}

		/// <summary>收掉手部道具（租约结束时由 <see cref="GrappleNpcKit"/> 调）。</summary>
		internal void HideNpcHandKitVisuals()
		{
			try { _hook.Unpark(); } catch (Exception) { }
			try { _ropeA.Hide(); } catch (Exception) { }
			_handHookRopeShown = false;
			// 🔴 不在这里 `_rig.Teardown()`：整套收摊要走 TeardownSession 的顺序
			//    （先拆钩/绳子件，再拆根）—— 先拆根会让随后的 Clear 摸到已销毁的子件。
		}

		private string ThrowInternal(bool hasShot, Vec3 shotOrigin, Vec3 shotDirection, bool autoPull)
		{
			Mission mission = MissionOf;
			Scene scene = mission?.Scene;
			if (scene == null)
			{
				return "Error: no scene.";
			}
			_launchSource = hasShot ? "weapon" : (_forcedAimActive ? "npc" : "command");

			// 🔴 **绳上还绑着人 ⇒ 真开火一律不许**（2026-10-18 用户裁定，安全网）。
			//    以前这里是"发射即替换"（下面那句"先清场"会把人悄悄放掉）。一根绳只能系一头 ⇒
			//    "再钩一次"必然 = 放掉上一个人；玩家看不到提示 ⇒ 实机反馈"我没松绳怎么还能钩人"。
			//    正门拦截在 <see cref="GrappleAimGatePatch"/>（进瞄准之前就抹掉攻击输入）；
			//    这条是它没拦住（补丁被关 / 漏网）时的兜底 —— **绝不许悄悄放人**。
			//    ⚠️ 只管**真开火**（hasShot）；命令 `custom.grapple throw` 不受限（调试要能强发）。
			if (hasShot && _bind != null && _bind.IsActive)
			{
				ShowBoundNoFireHint();
				return "Error: rope is tied to a bound target - release it first "
					+ "(panel: Slack Rope / 'custom.grapple bind release').";
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
			Vec3 aimNormal;
			if (_forcedAimActive)
			{
				// NPC：瞄准点由调用方给（目标胸口 / 墙上的钩点），不碰相机。
				hand = GetHand(Owner);
				aim = _forcedAim;
				aimAgent = _forcedAimAgent;
				aimNormal = Vec3.Up;
				why = "forced";
			}
			else if (!TryAim(out hand, out aim, out aimAgent, out why, out aimNormal))
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
			_mountWait = 0f;                      // 新的一钩：清掉上一钩"等骑马玩家下马"的计时
			_aimPoint = aim;
			_aimHitAgent = aimAgent;
			_landingNote = "-";
			// 🔴 新的一钩：**把上一钩的落点结果清掉**（2026-10-23）—— 打空那一钩从不跑 ResolveLanding，
			//    留着上一钩的 `Found=true` 会让状态机以为"有平台"而演「过程 → 落地」。
			_landing = default(GrappleLanding.Result);
			// 这一钩的两个属性：从空中起的吗（决定蓄势/拉升时长）、要不要自动拉（武器开火 = 要）
			_attachFromAir = Owner != null && !Owner.IsOnLand();
			_attachAutoPull = autoPull;
			_attachTimer = 0f;
			DebugLogger.Log($"[Grapple] 发射：起点={Fmt(launchFrom)}{(_hook.IsParked ? "(= 圆周上那枚此刻的位置)" : "(手)")}"
				+ $" 手={Fmt(hand)} 瞄准={Fmt(aim)} 瞄准面法线={Fmt(aimNormal)} 距离={dist:F1}m 目标={(aimAgent ? "人" : "地形")}"
				+ $" | 起手={_launchSource}({(autoPull ? "命中后自动拉" : "命中即停")}) 空中起钩={_attachFromAir}");

			// 🔊 抛出瞬间的甩绳声（2026-10-10）—— **整条钩索流程只响这一下**
			//    （钉住 / 拉拽不另配音；用户 2026-10-10 裁定）。名字查不到 = 静默跳过（不崩）。
			if (RopeSoundEnabled)
			{
				SoundFx.Play3D(RopeSoundName, launchFrom);
			}

			// ── 姿态动画：开火（2026-10-04 二稿）──
			//    前半段（ready/hold/release）归**武器 usage**，引擎自己在松手那一拍播 release；
			//    本机只在 release 演完（+ReleaseSeconds）那一刻接管"过程"。这里只记计时 + 重置上下文。
			_flightSeconds = (dist + 0.6f) / MathF.Max(1f, GrappleHook.Speed);   // 蓄势用它（见 TickHook）
			_animPendingPullTimer = ReleaseSeconds;
			_animPullHeld = false;              // 新的一钩：清掉上一钩"骑马等马"压住过程姿态的标志
			_mountedBlowFired = false;          // 同上：新的一钩可以再打一发"催下马 blow"
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
			Mission mission = MissionOf;
			if (mission?.Scene == null)
			{
				return "Error: no scene.";
			}

			Vec3 hand, aim;
			bool aimAgent;
			string why;
			Vec3 aimNormal;
			if (!TryAim(out hand, out aim, out aimAgent, out why, out aimNormal))
			{
				return "Error: " + why;
			}

			if (aimAgent)
			{
				return string.Format("probe: aim=({0:F2},{1:F2},{2:F2}) target=agent (no landing solve; pull-target path = step 5)",
					aim.x, aim.y, aim.z);
			}

			Vec3 playerPos = Owner != null ? Owner.Position : hand;
			GrappleLanding.Result r = GrappleLanding.Solve(mission.Scene, aim, playerPos, aimNormal);
			return string.Format("probe: aim=({0:F2},{1:F2},{2:F2}) n=({3:F2},{4:F2},{5:F2}) dist={6:F1}m | {7}",
				aim.x, aim.y, aim.z, aimNormal.x, aimNormal.y, aimNormal.z,
				(aim - playerPos).Length, GrappleLanding.Describe(r));
		}

		/// <summary>
		/// 收钩（命令 `retract`；也是打空与异常的兜底）。
		/// <paramref name="reason"/> **只进日志**（2026-10-03 加：实机出现"绳突然消失"，没有原因日志没法定位）。
		/// <paramref name="landed"/> = **这次收摊是"已经落地"**（拉拽到位 / 板载坠落着地）⇒
		/// 不许再问 <see cref="IsMainAirborne"/>：板刚拆、人刚解冻，引擎还没把 `IsOnLand` 翻成 true，
		/// 问出来是"还在空中"⇒ 动画被推回 自由落体后**再没有出口**（2026-10-15 实机，详见 <see cref="ExitAnim"/>）。
		/// </summary>
		public string Release(string reason = "command", bool landed = false)
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
			// 2026-10-23：NPC 那套的出手租约先还（武器 / 动作集 / AI），再收实体。
			try { _npcKit.EndLease("hook released: " + reason); } catch (Exception) { }
			// 勾人中 ⇒ **放人**（起身动画 + 排队"等演完再把 AI 还回去"；绳这一收不影响起身动画继续演）。
			// 若本次 Release 正是"绑自己走完了"那条路（GrappleBind 已把相位收成 None），Abort 是空操作。
			// 🔴 **扛着人的话先散伙**（2026-10-10）：正常放下走交互面板的【放下】（那条会演完动画才松手）；
			//    走到这里说明是**异常收摊**（收钩 / 换武器 / 命令）⇒ 直接松手，不演放下动画
			//    （被扛者会立刻回到自己脚下那一点 —— 这是接受的代价，见 §15.8）。
			_carry.HardStop("hook released: " + reason);
			_bind.Abort(reason);
			_bindFromTest = false;
			_pendingAiBindHoldSeconds = 0f;
			_releaseAfterCarry = false;
			_releaseAfterCarryReason = null;
			_hook.Clear();
			_hookPhase = HookPhase.Idle;
			// 🔴 2026-10-23：解绳必须清掉"绕在身上的绳尾钉"，否则 hide 之后手里那截再显示时
			//    会把上一圈绳圈画回来（用户实机："解开绳子之后对方身上的圈还在"）。
			try { _rope.ClearTailPin(); } catch (Exception) { }
			_rope.Hide();
			_rope.LengthFollowSpeed = HandLengthFollowSpeed;   // 恢复手里那档（飞行期的临时值到此为止）
			_rope.SlackRatio = FlightRopeSlackRatio;           // 绑定期可能被压成绷直（见 Binding 分支）—— 收钩恢复默认档
			_rope.AutoLength = true;                           // 绳长上限那条可能把它钉住了（见 Binding 分支）—— 收钩必须解开
			_landingNote = "released";

			// ── 姿态动画收摊（2026-10-04 二稿）──
			//    · **人已空中**（拉拽中掉板 / 空中收摊）：立即收摊（按 fall-trigger 送 自由落体）；
			//    · **已落地**（`landed`）：跳过空中判据 —— 这一帧 `IsOnLand` 还是假（板刚拆、人刚解冻），
			//        按它判会把动画又推回 自由落体（2026-10-15 实机）；调用方已在上一步收过摊。
			//    · **其余**（打空 / 命令 / 正常收尾）：只打"已放弃"标志 —— 状态机自己走：
			//        过程段 → recover-due 边 → 收手（打空亮相）；落地/收手段 → rem 15% 自己出机。
			if (_animCtx != null)
			{
				_animCtx.Cancelled = true;
			}
			if (!landed && IsMainAirborne())
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
			if (_anim == null || _animCtx == null || Owner == null)
			{
				return;
			}

			// 🔴 **玩家自己被绑着的时候，这台武器机整体让位**（2026-10-13 反向绑）：
			//    0 号通道同一时刻只能有一套全身姿态 —— 被绑那台机（bind.xml）正在写，
			//    这台再写就是**两台机抢同一个通道**（每帧互踢 = 人抖成筛子）。
			//    让位方式 = 把自己送出机（回引擎待机），等放人之后自然接回来。
			if (GrappleBind.IsTargetClaimed(Owner)
				|| (_bind.Target != null && ReferenceEquals(_bind.Target, Owner)))
			{
				if (IsAnimInside()) ExitAnim();
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
					_anim.Force(Owner, _lockedAnimState, GrappleAnimMachine.AnimBlendIn);
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
					// 🔴 整条"过程"段（pull）是**拉自己**的姿势（人被绳拽着走）——命中**人**的那一钩
					//    套到"拉别人"身上是错的（方案 §13.6）⇒ 这一钩跳过 Force，玩家自然回 idle。
					//    判据用相位（Binding = 真的在捆人）；命中马/儿童被拒的照旧走老路（钩挂在人身上）。
					// 🔴 **已放弃的钩也不进**（2026-10-23 加）：打空 / 钩到捆不了的目标时
					//    <see cref="CancelPullAnim"/> 会打 `Cancelled` 标志 —— 这一钩根本不会拉人，
					//    再进"过程"就是"人站着被绳拽一遍"（实机：钩空了却演完整套 pull→落地）。
					if (_hookPhase != HookPhase.Binding && !(_animCtx != null && _animCtx.Cancelled))
					{
						// 🔴 2026-10-17（口径 A「落地即飞」）：骑马勾到地形时，此刻人还在马上 ⇒
						//    **压住不进过程段**（下马动作正占着 0 号通道；真正起飞还要等马下来）。
						//    等"已下马"那一帧再从中段进 —— 见 TickHook 自动拉拽段里的 _animPullHeld 分支。
						if (IsWaitingForDismount())
						{
							_animPullHeld = true;
						}
						else
						{
							ForceAnim(GrappleAnimConditions.PullTrigger);
						}
					}
				}
			}

			// 喂事实（填完才 Tick）：段剩余 = 当前状态的剩余（所以 landing-due 拿到的就是"过程段"的）
			_animCtx.AnimRemainFrac = _anim.CurrentRemainFrac;
			// 🔴🔴 **"有落点平台"只在"钩真的钉在地形上 / 正在被拉过去"时才算数**（2026-10-23 实机修）：
			//    `_landing` 是**上一钩**留下的字段 —— 打空那一钩**从没跑过** <see cref="ResolveLanding"/>，
			//    这里照抄 `_landing.Found` 就等于把上一钩的"有平台"带进来 ⇒ 状态机走 `landing-due`
			//    ⇒ 人站着把「过程 → 落地」整套演一遍（用户报的现象："钩空了，角色没飞，却播了 pull 到落地"）。
			_animCtx.LandingFound = (_hookPhase == HookPhase.Attached || _hookPhase == HookPhase.Pulling)
				&& _landing.Found;

			_anim.Tick(Owner, dt);

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
		/// <param name="startProgress">0~1：从动作的哪个进度开始播（只给"骑马：落地即飞、从中段进"这一条用）。</param>
		private void ForceAnim(string whenToken, float startProgress = 0f)
		{
			if (_anim == null || Owner == null || !AnimEnabled)
			{
				return;
			}
			if (_anim.TryEventTarget(whenToken, out string state))
			{
				_anim.Force(Owner, state, GrappleAnimMachine.AnimBlendIn, startProgress);
				// 🔴 常开一行/次（2026-10-04 深夜：用户问"状态机有没有 setactionchannel 的记录"——
				//    以前成功的 Force 是静默的，日志里查不到"到底进没进"，只有缺边才报错）。
				DebugLogger.Log($"[Grapple] 姿态动画 Force → {state}（时刻名 {whenToken}"
					+ (startProgress > 0f ? $"，从中段 {startProgress:P0} 进（骑马：落地即飞）" : "")
					+ "）");
			}
			else
			{
				DebugLogger.Log($"[Grapple] 状态机里没有 '{whenToken}' 这条事件边 —— 动画不起"
					+ "（检查 ModuleData/statemachines/grapple.xml）");
			}
		}

		/// <summary>
		/// 这一钩是不是**"骑马勾到地形、正在等引擎放行下马"**（= 过程姿态要压着等的那一刻）。
		/// 判据与 TickHook 自动拉拽段里那段"骑马"分支**同一组条件**（保持同源，改一处要改两处）：
		/// 已钉住地形（不是人/马）+ 这一钩会自动拉 + 玩家还骑着。
		/// 任何一条不成立（打空 / 勾到人 / 命令路径 / 已经下马）⇒ false = 过程姿态照旧按开火时刻进。
		/// </summary>
		private bool IsWaitingForDismount()
		{
			try
			{
				return _hookPhase == HookPhase.Attached
					&& _hook != null && _hook.AttachedAgent == null
					&& _attachAutoPull && (AutoPull || !IsPlayerOwner)
					&& Owner != null && Owner.HasMount;
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>
		/// 🔴 **这一钩不会拉人了**（打空 / 钩到捆不了的目标）—— 姿态立刻作废，别把"被绳拽走"演一遍：
		/// ① **还没进"过程"** ⇒ 把待进计时清零，干脆不进（正常打空就是这一条：漏的时刻 = 开火后 0.566 s）；
		/// ② **已经在"过程"里**（远距离飞满才判打空）⇒ 打 `Cancelled` 标志，`recover-due` 边会把它切到 **收手**
		///    （= XML 里写的"打空/中止的标准出口"）。
		/// 不做的后果（2026-10-23 实机）：人站着把「过程 → 落地」整套播完，而钩其实一直在往回飞 —— 角色没被拉，
		/// 动作却像被拽了一趟。（"落地"那一支还有第二个帮凶，见 <see cref="TickAnim"/> 里 `LandingFound` 的注释。）
		/// </summary>
		private void CancelPullAnim(string why)
		{
			bool wasPending = _animPendingPullTimer > 0f;
			_animPendingPullTimer = 0f;
			_animPullHeld = false;
			if (_animCtx != null)
			{
				_animCtx.Cancelled = true;
			}
			DebugLogger.Log($"[Grapple] 姿态：这一钩不拉了（{why}）⇒ "
				+ (wasPending ? "还没进「过程」，不再进" : "已在「过程」，走 recover 边切「收手」"));
		}

		/// <summary>
		/// 动画收摊：在机内才动。
		/// <paramref name="landed"/> = **这次收摊是"已经落地"**（拉拽到位 / 板载坠落着地）——
		/// 此时**不许再问** <see cref="IsMainAirborne"/>：板刚拆、人刚解冻，引擎还没把 `IsOnLand` 翻成 true，
		/// 问出来是"还在空中"⇒ 动画被推回 自由落体，而那个状态**除这条边以外没有别的出口** ⇒
		/// 人站在地上一直播下落动画（2026-10-15 实机，日志 `姿态动画 → 自由落体（人还在空中；从 自由落体）`）。
		/// 人还在空中（拉拽途中掉板 / 空中收摊）⇒ 按 `fall-trigger` 送 自由落体，否则送机外。
		/// </summary>
		private void ExitAnim(bool landed = false)
		{
			_animPendingPullTimer = 0f;      // 收摊 = 作废"待进过程"的计时
			_animPullHeld = false;           // 同理：被压住的"过程进场"也一并作废（下马超时/收钩那条路）
			if (!IsAnimInside() || Owner == null || !AnimEnabled)
			{
				return;
			}
			string cur = _anim.Current;
			if (landed)
			{
				// 已落地 ⇒ 送机外（0 号通道还引擎）。
				// 🔴 定义里那条出边就是 `自由落体 → outside`，而 `TryEventTarget` **刻意不认"指向 outside 的边"**
				//    （见 `AgentAnimStateMachine.TryEventTarget` 的 `e.To != "outside"`）⇒ 这里读不出目标、
				//    只能直接送机外。日志带"已落地"四个字，用来和"打空/命令收摊"那条区分（验收就看这一行）。
				_anim.Force(Owner, AgentAnimStateMachine.OutsideState, GrappleAnimMachine.AnimBlendIn);
				DebugLogger.Log($"[Grapple] 姿态动画收摊（已落地；0 号通道还引擎）| 从 {cur}");
				return;
			}
			if (IsMainAirborne() && _anim.TryEventTarget(GrappleAnimConditions.FallTrigger, out string fallState))
			{
				_anim.Force(Owner, fallState, GrappleAnimMachine.AnimBlendIn);
				DebugLogger.Log($"[Grapple] 姿态动画 → 自由落体（人还在空中；从 {cur}）");
				return;
			}
			_anim.Force(Owner, AgentAnimStateMachine.OutsideState, GrappleAnimMachine.AnimBlendIn);
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
			if (_anim == null || Owner == null)
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
			_anim.Force(Owner, stateName, GrappleAnimMachine.AnimBlendIn);
			return _anim.Current == stateName
				? $"OK: locked '{stateName}' (hold=true; 'animlock clear' to release)."
				: $"FAILED: could not enter '{stateName}' —— 名字不在定义里，或这条动作的 clip 还没导入（act_none，看日志）"
					+ "  usage: animlock <投掷|过程|落地|自由落体|clear>";
		}

		/// <summary>主角在空中吗（口径同 <see cref="ThrowInternal"/> 的 `_attachFromAir`）。</summary>
		private bool IsMainAirborne()
		{
			return Owner != null && !Owner.IsOnLand();
		}

		/// <summary>
		/// 准星瞄准：从**相机**沿视线射一条 ≤ <see cref="AimRange"/> 米的射线，取"人 / 场景"里近的那个。
		/// 返回 false = 拿不到方向（接管中没人注册）或打空（天上）。
		/// 🔴 方向来源 = <see cref="CameraLook.TryGet"/>（铁律 35）；位置可以照读（相机 frame 的 origin）。
		/// </summary>
		private bool TryAim(out Vec3 hand, out Vec3 aim, out bool hitAgent, out string why, out Vec3 normal)
		{
			hand = GetHand();
			aim = hand;
			hitAgent = false;
			why = "";
			normal = Vec3.Up;                       // 没命中 = 当作"朝上的面"（不受盖板判据管）

			Mission mission = MissionOf;
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
				Vec3 hitNormal;
				if (!SpellSweep.FindNearestHit(mission, origin, end, AimAgentRadius, main.Index, null, null, null,
					out victim, out point, out hitNormal))
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
				normal = hitNormal;                     // 命中面朝向（只进日志/回执 —— 盖板判据锚在钩点上方，不看法线）
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
			Vec3 playerPos = Owner != null ? Owner.Position : GetHand();
			_landing = GrappleLanding.Solve(MissionOf?.Scene, _hook.AttachedPoint, playerPos, _hook.AttachedNormal);
			_landingNote = GrappleLanding.Describe(_landing);
			DebugLogger.Log($"[Grapple] 落点解算 @ 钩点 {Fmt(_hook.AttachedPoint)} 法线={Fmt(_hook.AttachedNormal)}：{_landingNote}");
			// 🔴 **盖板**（2026-10-15/16 室内勾天花板事故；方案 §二十一）——命中了就多打一行，验收看这一行：
			//    盖板 = **钩点正上方**最近的那块东西（天花板 / 檐口 / 楼板 / 桥面…；量法见 `GrappleLanding.FindLid`）。
			//    `否掉 N 个候选` = 那些"算在盖板上面"的落点被判死；
			//    `压到盖板下面` = 没有可站的平台 ⇒ 兜底落点被压到盖板下方（表现 = 拉上去→再掉回地面），
			//    而不是旧行为那样把脚直接放到天花板那一层（会穿出房顶）。
			if (GrappleLanding.LidCheck && (_landing.Blocked > 0 || _landing.LidClamped))
			{
				DebugLogger.Log($"[Grapple] 盖板 z={_landing.LidZ:F2}（钩点正上方那块）：否掉 {_landing.Blocked} 个候选"
					+ (_landing.LidClamped
						? $"；兜底落点压到盖板下面 ({_landing.Endpoint.z:F2}) ⇒ 拉上去再掉回地面"
						: "；落点仍在盖板下面，照常拉"));
			}
		}

		/// <summary>绳的远端：勾住人 = 目标身上；其余 = 钩头位置。</summary>
		private Vec3 FarEnd()
		{
			// 勾人中（步骤 5）：远端 = **钩**（GrappleBind 每帧算好的位置：缠 = 圆上一点 · 倒/缚 = 目标躯干）。
			// 注：绳永远系在钩的尾环上，而钩的位置在勾人时由我们驱动（不再冻在命中点）。
			if (_hookPhase == HookPhase.Binding && _bind.IsActive)
			{
				return _bind.RopeEnd;
			}
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

		/// <summary>
		/// **NPC 那套的每帧**（由 <see cref="NpcGrappleDirector"/> 调，2026-10-23）。
		/// 只跑"这一套钩索自己的"东西：起身队列 / 扛人 / 姿态动画 / 钩头相位。
		/// **不碰**玩家独占的：瞄准相机、开火队列、弹药顺序、缺件提示、骑马夹具命令。
		/// </summary>
		public void TickSession(float dt)
		{
			if (_terminated) return;
			if (IsPlayerOwner)
			{
				OnMissionTick(dt);
				return;
			}

			try { _bind.TickPending(dt); }
			catch (Exception ex) { DebugLogger.Log($"[Grapple] NPC 起身队列异常（忽略）：{ex.GetType().Name} {ex.Message}"); }

			try { _carry.Tick(dt, _bind); }
			catch (Exception ex) { DebugLogger.Log($"[Grapple] NPC 扛人 tick 异常（忽略）：{ex.GetType().Name} {ex.Message}"); }
			TickDeferredRelease();

			TickAnim(dt);
			_npcKit.Tick(dt);
			TickNpcVisualLod(dt);
			if (NpcHandKitEnabled && _npcVisualLevel == 0)
			{
				TickHandKit(dt);
				TickHandHook(dt);
			}

			if (_hookPhase != HookPhase.Idle)
			{
				TickHook(dt);
				ApplyNpcVisualLodAfterTick();
				return;
			}

			if (!_anchored) return;
			try
			{
				_rope.Tick(dt, GetRopeAnchor(), _anchor);
			}
			catch (Exception ex)
			{
				_anchored = false;
				_rope.Hide();
				DebugLogger.Log($"[Grapple] NPC 锚定 tick 异常，收绳：{ex.GetType().Name} {ex.Message}");
			}
			ApplyNpcVisualLodAfterTick();
		}

		/// <summary>按与玩家的距离算 NPC 那套的渲染档（0 全 / 1 简化 / 2 不渲染）。</summary>
		private void TickNpcVisualLod(float dt)
		{
			_npcLodTimer += dt;
			if (_npcLodTimer < 0.5f) return;
			_npcLodTimer = 0f;

			Agent player = Agent.Main;
			Agent owner = Owner;
			int level = 0;
			if (player != null && owner != null)
			{
				float d = owner.Position.Distance(player.Position);
				level = d > NpcVisualDistance ? 2 : (d > NpcFullVisualDistance ? 1 : 0);
			}
			if (level == _npcVisualLevel) return;
			_npcVisualLevel = level;
			ApplyNpcVisualLod();
		}

		private void ApplyNpcVisualLod()
		{
			if (_npcVisualLevel >= 1)
			{
				// 简化：手部那盘（环 / 绳 A / 手里转的钩）不画
				try { _ropeA.Hide(); } catch (Exception) { }
				try { _hook.Unpark(); } catch (Exception) { }
			}
			if (_npcVisualLevel >= 2)
			{
				// 不渲染：连绳 B 也收掉（机制照常跑）
				try { _rope.Hide(); } catch (Exception) { }
			}
			else if (_hookPhase != HookPhase.Idle)
			{
				// 回到近处：把绳重新摆出来（Show 会把点链拉直，绑定期/飞行期都可以）
				try
				{
					if (!_rope.IsVisible)
					{
						_rope.Show(GetRopeAnchor(), _bind.IsActive ? _bind.RopeEnd : _hook.Position);
					}
				}
				catch (Exception) { }
			}
		}

		private void ApplyNpcVisualLodAfterTick()
		{
			if (_npcVisualLevel >= 2)
			{
				try { _rope.Hide(); } catch (Exception) { }
			}
		}

		public override void OnMissionTick(float dt)
		{
			base.OnMissionTick(dt);
			if (!IsPlayerOwner)
			{
				TickSession(dt);
				return;
			}
			_traceFrame++;
			Trace("tick:begin");

			// ⓪ 瞄准相机（2026-10-04）：按住左键瞄准期间接管相机（鼠标驱动；见 GrappleAimCamera）。
			//    放在最前 —— 它的生命周期与钩头相位无关（瞄准时钩头通常还是 Idle）。
			GrappleAimCamera.Tick(dt);
			Trace("cam");

			// ⓪′ 姿态动画状态机（2026-10-04）：喂事实 → Tick（照飞行："填完才 Tick"）
			TickAnim(dt);
			Trace("anim");

			// ⓪″ 弹药顺序归一化（2026-10-12 晚）：**进战场一次性**把"钩爪 vs 其它箭类"的槽位顺序排对
			//     （每个 Mission 只做一次；**不能**在装备界面做 —— 那样会坏物品栏，见方法注释）
			NormalizeAmmoOrderOncePerMission();
			Trace("ammoorder");

			// ⓪ 开火拦截的排队执行（2026-10-03）：拦截补丁只"记一笔"，真正的发射与退弹在这一帧做 ——
			//    这样武器开火与命令 `throw` 走的是**同一条链路**（都在常规 tick 里）。
			GrappleFirePatch.ProcessPending();
			Trace("fire");

			// ⓪‴ 勾人的「起身队列」（步骤 5，2026-10-09）：与钩头相位**无关** ——
			//      绑结束了还得等起身动画演完再把 AI 还给目标，所以它必须每帧都跑。
			_bind.TickPending(dt);

			// ⓪‴′ 骑马夹具的"上马请求"保持（2026-10-13）：见 TickMountRequest 的注释
			TickMountRequest(dt);

			// ⓪⁗ **扛人**（步骤 6，2026-10-10）：与钩头相位**无关** —— 扛着人走路时钩头是 Idle，
			//      但被扛者每帧都得跟着摆位、两台状态机都得推进；【放下】之后还要等放下动画演完才松手。
			_carry.Tick(dt, _bind);
			Trace("carry");
			TickDeferredRelease();

			// ⓪″ **手里那套常驻件**（左手环 + "环→右手"那截绳 A，2026-10-08）：与钩头相位**无关** ——
			//     握着钩索就该在（待命 / 飞行 / 拉拽都跟着两只手），所以放在相位分派**之前**。
			TickHandKit(dt);
			Trace("handkit");

			// ⓪‴ **缺件提示**（2026-10-12）：握着绳、按了开火键、可装备里没有钩爪 ⇒ 弹一句。
			//     与钩头相位无关（缺件时相位恒为 Idle），同样放在相位分派**之前**。
			TickNoHookHint();
			Trace("nohookhint");

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
				Agent main = Owner;
				if (main == null)
				{
					Release("return: no owner agent");
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
					int ownerIndex = Owner != null ? Owner.Index : -1;
					int ownerMount = -1;
					try { ownerMount = Owner?.MountAgent != null ? Owner.MountAgent.Index : -1; } catch (Exception) { }
					step = _hook.Tick(MissionOf, dt, ownerIndex, ownerMount);
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
					_landingNote = "attached to agent";
					// 🔴 勾人（步骤 5，2026-10-09）：命中人 ⇒ 进"缠 → 倒 → 缚"那条。
					//    不接受（儿童 / 坐骑 / 骑在马上的人…）= 停在 Attached（旧行为：钩挂人身上不动）。
					string why = "disabled";
					if (GrappleBind.Enabled && _hook.AttachedAgent != null
						&& _bind.Begin(Owner, _hook.AttachedAgent, _hook.AttachedPoint, out why))
					{
						_bind.AiBindHoldSeconds = _pendingAiBindHoldSeconds;
						_pendingAiBindHoldSeconds = 0f;
						_hookPhase = HookPhase.Binding;
						_bindFromTest = false;              // 真开火那条路：出口②③ 照常生效
						_landingNote = "binding target";
						// 🔴 **骑马钩人 = 玩家不下马**（2026-10-17 核；方案 §13.11 的口径：
						//    "骑马勾人是本玩法最顺的用法"—— 玩家在马上不动、只有目标被拽倒/拖行）。
						//    这行是那条口径的**判据日志**：谁想确认"骑马钩人会不会把玩家弄下马"，
						//    看它 + 之后的拖行行（`骑着马`）就够，不用去猜。
						//    （对照：**骑马钩到地形**才会自动下马 —— 那是"拉自己"的硬约束，见 _mountWait 那一段。）
						try
						{
							if (Owner != null && Owner.HasMount)
							{
								DebugLogger.Log("[Grapple] 骑马钩人：绳主仍在马上 —— **不下马**（设计口径 §13.11）；"
									+ "目标照旧被拽倒，绳主骑马移动（>1 m/s）时他被拖在地上");
							}
						}
						catch (Exception) { }
					}
					else if (GrappleBind.Enabled)
					{
						_landingNote = "attached to agent (bind refused: " + why + ")";
						// 🔴 2026-10-22 用户裁定：**捆不了的目标直接收钩，不提示**。
						//    原来停在 Attached：钩子钉在人身上、玩家的拉拽姿态已经起了、绳一直抖 ——
						//    观感像"勾住了却抽风"，而且玩家什么也做不了、只能自己收钩。
						//    现在走"打空"同一条返程（看得见地飞回来），零提示（技术日志照留）。
						//    典型 = 儿童（child immune）/ 坐骑 / 骑在马上的人（下拉关闭时）/ 失效目标。
						DebugLogger.Log($"[Grapple] 钩到无法捆的目标（{why}）⇒ 直接收钩返程");
						// 同上：这一钩不会拉人 ⇒ 姿态作废（别演"被绳拽走"）
						CancelPullAnim("钩到捆不了的目标");
						if (_hook.StartReturn())
						{
							_hookPhase = HookPhase.Returning;
							return;
						}
						Release("bind refused（实体不在→直接收）");
						return;
					}
				}
				else if (step == GrappleHook.StepResult.Missed)
				{
					// 🔴 **2026-10-08 用户要求：打空要"看得见地飞回来"**（"相当于飞出去的逆向"）——
					//    不再直接隐藏收场，切进**返程相位**：钩头追着手飞回、绳同步收短，到了交回手里继续绕圈 ✓。
					if (_hook.StartReturn())
					{
						_hookPhase = HookPhase.Returning;
						// 🔴 打空 = 这一钩**不会拉人** ⇒ 姿态立刻作废（否则人站着把 过程→落地 演一遍）
						CancelPullAnim("打空");
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
				if (_attachAutoPull && (AutoPull || !IsPlayerOwner) && _hook.AttachedAgent == null)
				{
					// 🔴 骑马勾到地形（2026-10-13 改口径，用户拍板 —— 取代 §13.11 的"直接收钩"）：
					//    **自动下马，然后把下马那一拍藏进"蓄势"里直接起飞**（见 MountedPull 的注释：
					//    拉自己必须先下马 —— 骑在马上时骑手位置由马决定，冻不住也托不起来）。
					if (Owner != null && Owner.HasMount)
					{
						if (!MountedPull)
						{
							// 旋钮关掉 = 旧行为（2026-10-09 拍板那条）：直接收钩、不提示
							_attachAutoPull = false;
							if (_hook.StartReturn())
							{
								_hookPhase = HookPhase.Returning;
								_landingNote = "mounted -> auto retract (MountedPull off)";
								DebugLogger.Log("[Grapple] 骑马勾到地形 ⇒ 直接收钩（MountedPull 关着）");
							}
							else
							{
								Release("mounted（实体不在→直接收）");
							}
							return;
						}

						// ① 点"下马"（每帧一次；引擎按马速可能先拒后收 —— 重发是幂等的，标志位而已）
						try { Owner.EventControlFlags |= Agent.EventControlFlag.Dismount; }
						catch (Exception) { }

						// ② 🔴 2026-10-19 实机（日志 09:47:23.776 / 09:47:31.058）：**旗标这条路在玩家身上被引擎顶着不放行**
						//    ——两枪都是"2.5 s 内没能下马 ⇒ 作废"。⇒ **同时打一发 `CanDismount` blow** 催下马：
						//    native 直接执行、**不看马速**（目标侧实测 0.20 s 落地，见 §13.5）。
						//    · 只打一次：blow 会播"被击落"的受击反应，重复打 = 反复踉跄；
						//    · 临时无敌在 `FireDismountBlow` 内部做（血一滴不掉）；
						//    · **攻击者传 −1**：不归属于任何人（否则会被记成"玩家攻击玩家"）。
						//    · 方向 = 朝钩点（绳绷紧把人往墙那边扯，和接下来起飞的方向一致）。
						//    旗标那条**保留**：万一 blow 被引擎拒（Immortal 之外的情况），还有老路兜底。
						// 🔴 2026-10-19（用户："木板带着马也飞起来了" / "我不希望我的坐骑被挪走"）：
						//    **不挪坐骑** —— 改用"承载板物理体本身变小"来根治（预制体换 shape，见
						//    `Prefabs/lwn_prefabs.xml` 的 `lwn_flight_sigil_body` + `FlightTuning.CarrierTopLocalZ`）。
						//    旧板物理盒 5.1 m 宽、马站在旁边 2 m 内就被抬走；新板 1.25 × 1.33 m，马本来就在板外。
						if (MountedSelfBlow && !_mountedBlowFired)
						{
							_mountedBlowFired = true;
							try
							{
								Vec3 toHook = _hook.AttachedPoint - Owner.Position;
								toHook.z = 0f;
								string err = GrappleBind.FireDismountBlow(Owner, rear: false, damage: 60f,
									dirToward: toHook, attackerIndex: -1);
								DebugLogger.Log(err == null
									? "[Grapple] 骑马勾到地形 ⇒ 已打 CanDismount blow 催下马（native、不看马速；免疫掉血）"
									: $"[Grapple] ⚠ 骑马下马 blow 出手失败（{err}）—— 只靠旗标（引擎可能不放行）");
							}
							catch (Exception ex)
							{
								DebugLogger.Log($"[Grapple] ⚠ 骑马下马 blow 异常（{ex.GetType().Name}）—— 只靠旗标");
							}
						}

						_mountWait += dt;
						if (_mountWait >= MountedDismountTimeoutSeconds)
						{
							_attachAutoPull = false;
							_landingNote = "mounted -> dismount timeout";
							DebugLogger.Log($"[Grapple] ⚠ 骑马勾到地形：{MountedDismountTimeoutSeconds:F1}s 内没能下马"
								+ "（马一直在跑？）⇒ 这一钩作废、收钩返程");
							if (!_hook.StartReturn())
							{
								Release("mounted（下马超时，实体不在→直接收）");
							}
							else
							{
								_hookPhase = HookPhase.Returning;
							}
							return;
						}

						_landingNote = $"waiting for dismount ({_mountWait:F1}s)";
						return;                       // 还骑在马上 —— 落地那一帧自然走下面那条
					}
					if (_mountWait > 0f)
					{
						// 刚从马上下来：起飞（蓄势那一拍正好把下马动作吃掉了）
						DebugLogger.Log($"[Grapple] 骑马勾到地形 ⇒ 已下马（{_mountWait:F2}s）⇒ 起飞");
						_mountWait = 0f;
					}

					// 🔴 蓄势 = **拉拽起点钉在开火后 PullStartSeconds** − 本钩飞行耗时（2026-10-04 时间轴对齐）：
					//    钩到得早就不多等、飞得远就少等 —— 位移起点始终 ≈ 动画"过程"段的起点（用户定的 1.13 s）。
					//    （空中起钩的时间轴还没做，先照旧用固定值。）
					float delay = _attachFromAir
						? PullDelayAir
						: MathF.Max(0f, PullStartSeconds - _flightSeconds);
					if (_attachTimer >= delay)
					{
						_attachAutoPull = false;              // 只自动拉一次

						// 🔴 2026-10-17（口径 A「落地即飞」）：骑马钩地形时过程姿态被压到**这一刻**才进 ——
						//    并且**从中段进**（<see cref="PullAnchorIntoClip"/> = 0.567 s = "被拽走"那一拍），
						//    这样"位移 ↔ 姿态"的关系与地面钩锁**逐帧一致**，也不额外等那 0.567 秒"使劲"。
						//    · `_animPullHeld` = 按预想的"等马"压住了（正常骑马路径）；
						//    · 另一条兜底：**进场过晚**（姿态早就进了、但位移被各种原因推迟 —— 例如把
						//      `custom.grapple range` 调大到钩子飞过 0.566 s 才咬住）⇒ 同样从锚点重进。
						//      地面钩锁这里恒等于 anchor（按构造：delay = 1.133 − 飞行 ⇒ elapsed ≡ 0.567），
						//      **不会**误触发；打空 / 勾到人 / 命令路径同样不受影响。
						//      ⚠️ 容差必须跟**帧长**挂钩：`_attachTimer` 是逐帧累加的，触发那一帧最多超出一个 dt
						//      （慢机器 dt 0.067 ⇒ elapsed 0.634）—— 固定 0.05 会在 15fps 上把地面误判成"过晚"。
						float poseElapsed = _attachTimer + _flightSeconds - ReleaseSeconds;
						float lateTolerance = MathF.Max(0.10f, dt * 1.5f);
						if (_animPullHeld || poseElapsed > PullAnchorIntoClip + lateTolerance)
						{
							_animPullHeld = false;
							ForceAnim(GrappleAnimConditions.PullTrigger,
								PullClipSeconds > 0.01f ? PullAnchorIntoClip / PullClipSeconds : 0f);
						}

						string r = StartPull("auto（武器开火）");
						DebugLogger.Log($"[Grapple] 自动拉拽（蓄势 {_attachTimer:F2}s ≥ {delay:F2}s）→ {r}");
						if (!r.StartsWith("OK"))
						{
							_landingNote = "auto-pull refused: " + r;
						}
					}
				}
			}
			else if (_hookPhase == HookPhase.Binding)
			{
				// 🔴 勾人（步骤 5，2026-10-09）：目标侧的一切（位移 / 动画 / AI 压制 / 犯罪 / 解除）
				//    都在 GrappleBind 里；这里只做三件事 —— 判出口、推进它、把钩摆到它说的地方。
				// 🔴 **`bind test`（调试路径）不受这两条出口管**（2026-10-09 实机事故）：
				//    测试路径玩家**没拿钩索**、目标还可能在 30 米外（> 绳长上限）⇒ 这两条一判就中，
				//    实测 15 毫秒就把自己踢掉（日志：`收钩（player stowed the grapple）`），
				//    而"放人"会播起身动画 ⇒ 头几帧正是躺姿 ⇒ 看着像"倒下又立刻起来"。
				if (!_bindFromTest)
				{
					// 出口②：玩家换武器 / 把钩索收起来 ⇒ 视为"松手放人"；
					// 🔴 **扛着人时豁免**（2026-10-13）：扛起那一刻是**我们**把武器收进鞘的
					//    （不要准星/端弓姿势），那不是"玩家收起了钩索" ⇒ 由 `_carry.SuppressStowCheck`
					//    在"扛着期间 + 还武器后的一小段"里把这条出口挡住（见 GrappleCarry.StowGraceSeconds）。
					if (!IsHoldingGrapple() && !_carry.SuppressStowCheck)
					{
						Release("player stowed the grapple（换武器/收起）");
						return;
					}
					// 出口③：绳被拉断（玩家走出绳长上限）⇒ 放人（否则绳会无限拉长、目标被拖着走）。
					try
					{
						if ((GetRopeAnchor() - _bind.RopeEnd).Length > _rope.MaxLength)
						{
							Release("rope snapped（走出绳长上限）");
							return;
						}
					}
					catch (Exception) { }
				}

				// 顺序：先 Tick（本帧的位置才算得出来）再 Park（用本帧的值摆，不差帧）。
				// 🔴 锚点要喂进去：**绳长上限**（"牵着走 / 拖在地上"）要拿"绳主的手 → 目标胸口"量距离，
				//    见 GrappleBind 顶部那段；反向绑（NPC 牵玩家）时这个锚点自动是**那个 NPC 的手**。
				GrappleBind.BindEvent bindEv = _bind.Tick(dt, GetRopeAnchor());

				// 🔴 绳的**可视长度**跟着"绳长上限"钉住（2026-10-13）：锁了长就**不再伸缩**
				//    （否则玩家走远，绳先自己变长、人再被拽 —— 读感是"橡皮筋"，不是"绷紧的绳"）。
				//    没锁（还没到缚那一拍 / 开关关掉 / 被扛着）⇒ 回到"每帧按跨度推"的旧行为。
				if (_bind.LeashActive)
				{
					_rope.AutoLength = false;
					_rope.Length = _bind.LeashRopeLength;   // 可视长度（被封顶过）≠ 绷紧判据（LeashLength），见那条注释
				}
				else
				{
					_rope.AutoLength = true;
				}

				// 🔴 绳的松紧跟着相位走（2026-10-09 用户反馈"看不出被拉"后加）：
				//    缠/拽 那两拍**压成绷直**（松绳没有"在拉"的读感）；缚那拍恢复松量（绳在地上拖着才自然）。
				//    2026-10-13：牵着人走那一拍也压成绷直（绳正吃着力；松着读不出"在牵"）。
				_rope.SlackRatio = (_bind.WantsTautRope || _bind.LeashPulling) ? 1.0f : FlightRopeSlackRatio;

				Scene bindScene = MissionOf != null ? MissionOf.Scene : null;
				if (bindScene != null && _bind.IsActive)
				{
					_hook.Park(bindScene, _bind.HookPos, _bind.HookDir);
				}
				if (bindEv == GrappleBind.BindEvent.Finished)
				{
					Release("bind finished（放人）");
					return;
				}
			}
			else if (_hookPhase == HookPhase.Pulling)
			{
				GrapplePull.PullEvent ev = _pull.Tick(dt);
				if (ev == GrapplePull.PullEvent.Finished)
				{
					// 拉拽整段完成 = **已经落地**（平台那条：落地动作早已自己出机，此处空操作；
					// 板载自由落体那条：板顶刚贴地）⇒ 收摊必须**带上"已落地"这个事实**：
					// 这一帧 `IsOnLand()` 还是假（板刚拆、人刚解冻），按它判会把动画又推回 自由落体，
					// 而那个状态除触地边以外没有别的出口 ⇒ 人站在地上一直播下落（2026-10-15 实机）。
					ExitAnim(landed: true);
					Release("pull finished（到位拆板）", landed: true);
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
				_bind.ApplyRopeTailPin(_rope);   // 缠绕态：绳尾贴身体绕（非缠绕相位=自动解除；见 GrappleRope.SetTailPin）
				_rope.Tick(dt, GetRopeAnchor(), FarEnd());
				Trace("hk:rope");
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 绳 tick 异常，收钩：{ex.GetType().Name} {ex.Message}");
				Release("rope tick exception: " + ex.Message);
			}
		}

		// ─────────────────────────────── 勾人（步骤 5，2026-10-09）───────────────────────────────

		/// <summary>命令层：勾人状态一行（`custom.grapple bind state`）。</summary>
		public string BindStateLine() => _bind.StatusLine();

		/// <summary>命令层：立刻放开当前目标（`custom.grapple bind release`）。</summary>
		public string BindRelease() => _bind.ReleaseNow()
			? "OK: target released (standup plays, AI returns after it finishes)"
			: "Error: no bound target.";

		/// <summary>
		/// **玩家此刻手里牵着人吗**（= 钩索捆着某个目标）—— 交互面板【松绳】行的判据（§13.14 TODO 4）。
		/// 🔴 **跟玩家走、不跟 focus 走**（用户 2026-10-09 拍板）：绳在玩家手上 ⇒ 这是"我"的状态，
		/// 不能要求玩家把准星精确怼到一具趴着的身体上才给这个键。
		/// 缠/拽阶段也算（绳已经挂在人身上了）；只有钩头在飞 / 已收回 = false。
		/// </summary>
		public bool IsBindingSomeone()
		{
			try { return _bind.IsActive && _bind.Target != null; }
			catch (Exception) { return false; }
		}

		/// <summary>
		/// **此刻被绑者的位置/移动归 `GrappleBind` 写**（缠 / 拽倒 / 牵绳拖动中）。
		/// 这些时候 `BoundAction` 与压制复核**不许再碰他的移动**（见 `GrappleBind.IsMovingBody`）。
		/// </summary>
		public bool IsMovingBoundBody
		{
			get { try { return _bind.IsActive && _bind.IsMovingBody; } catch (Exception) { return false; } }
		}

		/// <summary>当前被捆的那一个（没有则 null；面板标题用 —— 见 `IsBindingSomeone` 的说明）。</summary>
		public Agent BoundTargetAgent
		{
			get { try { return _bind.IsActive ? _bind.Target : null; } catch (Exception) { return null; } }
		}

		/// <summary>
		/// 这个 agent 是不是**当前被捆的那一个**（面板名字后缀用）。
		/// 面板每帧都要问，所以做成最便宜的引用比较 + 空安全。
		/// </summary>
		public bool IsBindingTarget(Agent a)
		{
			if (a == null) return false;
			try { return _bind.IsActive && ReferenceEquals(_bind.Target, a); }
			catch (Exception) { return false; }
		}

		/// <summary>
		/// **交互面板【松绳】**（§13.14 TODO 4）：放开当前被捆的那一个 ——
		/// 走既有的 `Release` 那条路：**收钩 + 绳收回 + 放人**（起身动画 → 3.6 秒后把 AI 还回去）。
		/// **不要目标参数**：判据是"我牵着人吗"（<see cref="IsBindingSomeone"/>），绳子系在谁身上由我们自己知道。
		/// 返回 false + <paramref name="message"/> = 手里没牵着人。
		/// </summary>
		public bool CutRopeNow(out string message)
		{
			Agent target = _bind.Target;
			if (!IsBindingSomeone())
			{
				message = "no bound target";
				return false;
			}
			Release("cut rope (interaction panel)");
			message = target?.Name?.ToString() ?? "the target";
			DebugLogger.Log($"[CutRope] 玩家松开 {message} 身上的绳（收钩 + 起身动画）");
			return true;
		}

		/// <summary>
		/// **命令层：验收"被捆的人不接命令"**（`custom.grapple bind probe`，TODO 2）——
		/// 给当前被捆的目标**真发一条命令**（`ComeHere` 目标 = 玩家），对比"事件忽略"计数：
		/// 计数 +1 = 命令被闸门挡住 ✓；没涨 = 闸门漏了（那时人当场会站起来走过来，肉眼也看得见）。
		/// 🔴 只在 `Bound` 相位可用 —— 缠/拽还在演，此刻标记尚未立起（放行会把人从半路拽起来）。
		/// </summary>
		public string BindProbe()
		{
			Agent t = _bind.Target;
			if (!_bind.IsActive || t == null) return "Error: no bound target.";
			if (_bind.Current != GrappleBind.Phase.Bound)
				return $"Error: target not fully bound yet (phase = {_bind.Current}; wait for Bound).";

			var brain = AgentAIController.GetBrainForAgent(t);
			if (brain == null) return $"Error: target '{t.Name}' has no brain (no AI to gate).";

			int before = brain.BoundEventsDropped;
			try
			{
				AgentAIController.Instance?.SendEventToAgent(t, "ComeHere", Agent.Main);
			}
			catch (Exception ex)
			{
				return $"Error: sending the probe command failed ({ex.GetType().Name}).";
			}
			int after = brain.BoundEventsDropped;
			string act = "-";
			try { act = V.ActName(t, 0); } catch (Exception) { }

			return $"OK: sent 'ComeHere' to '{t.Name}' | bound={brain.IsBound} | events ignored {before}->{after}"
				+ (after > before
					? " (+1 = the command was blocked by the gate)"
					: " (NOT blocked -- the gate leaked; the target should be walking over right now)")
				+ $" | action={act}";
		}

		/// <summary>命令层：观感验收 —— 强制目标播"躺下 / 躺地循环 / 起身"某一段（`bind anim`）。</summary>
		public string BindForceAnim(string which) => _bind.ForceAnim(which);

		/// <summary>
		/// **命令 / 面板入口：【拉紧】—— 把当前被捆的人拽倒**（`custom.grapple bind tug`）。
		/// 🔴 2026-10-10 新口径：**命中不再自动拽倒**（第一拍是 crash，稳态是站着），
		///    拽倒改成玩家主动做；拉倒后 <see cref="GrappleBind.LaySeconds"/> 秒自动起身。
		/// </summary>
		public string BindTug()
		{
			if (!_bind.IsActive) return "Error: no bound target.";
			return _bind.StartYank(out string why)
				? $"OK: tugging the bound target down (stand-up in {GrappleBind.LaySeconds:F1}s; bind laytime 0 = never)"
				: $"Error: cannot tug ({why}).";
		}

		/// <summary>命令层：在闹开关（`站缚 ⇄ 挣扎`）。</summary>
		public bool BindStruggleState
		{
			get { try { return _bind.StruggleState; } catch (Exception) { return false; } }
		}

		/// <summary>命令层：翻一下在闹开关。</summary>
		public bool BindToggleStruggle()
		{
			try { return _bind.ToggleStruggle(); }
			catch (Exception) { return false; }
		}

		// ───────────────────────────── 扛人（步骤 6，2026-10-10）─────────────────────────────

		/// <summary>手里扛着人吗（交互面板与命令用；判据 = 扛人控制器在跑）。</summary>
		public bool IsCarryingSomeone
		{
			// 🔴 **2026-10-14 收窄**：只算"**扛在肩上**"（含正在放下）。
			//    人被搁到**马背上**之后，扛人关系已经结束（玩家双手空着、能开火/骑马）⇒ 不算"扛着人"。
			//    消费者按这个口径走：① 面板"扛着就不给松绳" ② 开火拦截"手占着不许开火"。
			get
			{
				try
				{
					return _carry.Phase == GrappleCarry.CarryPhase.Riding
						|| _carry.Phase == GrappleCarry.CarryPhase.PuttingDown;
				}
				catch (Exception) { return false; }
			}
		}

		/// <summary>
		/// **这个 agent 是不是"正被我们扛在身上的那个"**（准星/面板的焦点要把他过滤掉）。
		///
		/// 🔴 为什么必须过滤（2026-10-14 实机，用户："你没有过滤你当前扛着的这个 NPC，所以你永远无法对准马"）：
		///    扛着人时那个人**被摆在玩家身上** ⇒ 从相机打出去的射线**第一个命中的就是他** ⇒
		///    准星永远锁在他身上、面板永远在讲他，**任何"瞄准别人/别的活物"的交互都用不了**
		///    （不只是放上马背 —— 牵着人时想瞄旁边的马/人/箱子，全都被他挡住）。
		/// </summary>
		public bool IsBodyCarried(Agent a)
		{
			try { return a != null && _carry.Carried != null && ReferenceEquals(_carry.Carried, a); }
			catch (Exception) { return false; }
		}

		/// <summary>人被搁在**马背上**（扛人关系已结束，但位置还由我们每帧贴马 —— 见 `GrappleCarry`）。</summary>
		public bool IsBodyOnHorse
		{
			get { try { return _carry.Horse != null; } catch (Exception) { return false; } }
		}

		/// <summary>正被扛着的那一个（没有则 null）。</summary>
		public Agent CarriedAgent
		{
			get { try { return _carry.Carried; } catch (Exception) { return null; } }
		}

		/// <summary>这个 agent 是不是**正被扛着的那一个**（面板名字后缀用）。</summary>
		public bool IsCarriedTarget(Agent a)
		{
			try { return a != null && _carry.IsCarrying(a); }
			catch (Exception) { return false; }
		}

		/// <summary>
		/// **命令 / 面板入口：把当前被捆的那个扛起来**（`custom.grapple carry start`）。
		/// 默认扛人者 = `Agent.Main`（玩家）；两边都是 Agent，将来 NPC 扛人直接传别的。
		/// 返回 false + <paramref name="why"/> = 扛不了（没捆着 / 趴着 / 太远 / 已在扛）。
		/// </summary>
		public bool CarryPickUp(out string why, Agent carrier = null)
		{
			why = "-";
			Agent t = _bind.IsActive ? _bind.Target : null;
			if (t == null) { why = "no bound target (lasso someone first)"; return false; }
			return _carry.Begin(carrier ?? Owner, t, _bind, out why);
		}

		/// <summary>命令 / 面板入口：把扛着的人放下（演完放下动画才真松手）。</summary>
		public bool CarryPutDown(out string why)
		{
			if (!_carry.IsActive) { why = "not carrying anyone"; return false; }
			why = "-";
			return _carry.PutDown(_bind, "command");
		}

		/// <summary>命令层：扛人状态一行（`custom.grapple carry state`）。**纯英文**。</summary>
		public string CarryStateLine() => _carry.StatusLine();

		/// <summary>命令层：扛人总开关。</summary>
		public string CarrySetEnabled(bool on)
		{
			GrappleCarry.Enabled = on;
			if (!on && _carry.IsActive) _carry.HardStop("carry disabled");
			return $"OK: carry {(on ? "on" : "off")}";
		}

		// ── 交互面板用的两个判据（2026-10-10 用户要的两件；都是"壳"，判定本体在上面）──

		/// <summary>
		/// **【拉紧】能不能用**（面板行判据）：牵着绳 **且** 离绳另一端那个人 **超过
		/// <see cref="GrappleBind.TugMinDistance"/> 米**、而且他已经捆稳了（不再是缠/拽中）。
		/// 🔴 距离**从脚底量**（`Agent.Position`）—— 与"手挂点闸门"那条先例同口径，
		///    别拿手/相机去量（抬手就多 1.5 米）。
		/// </summary>
		public bool CanTugRope(out float distance)
		{
			distance = -1f;
			try
			{
				if (!IsBindingSomeone()) return false;
				Agent t = _bind.Target;
				Agent me = Owner;
				if (t == null || me == null) return false;
				// 🔴 **含高度**（2026-10-20）：与绳长的判据同一把尺子 —— 目标在墙头/台阶上时，
				//    水平距离可能很近，但绳是真的斜着绷的，那时该出的是【拉紧】不是【扛起】。
				distance = (t.Position - me.Position).Length;
				return distance > GrappleBind.TugMinDistance && _bind.Current == GrappleBind.Phase.Bound;
			}
			catch (Exception) { return false; }
		}

		/// <summary>
		/// **【扛起】能不能用**（面板行判据）：牵着绳、瞄着的**就是被绳捆着的那个人**、
		/// 他站着（不是趴着）、而且够得着（≤ <see cref="GrappleCarry.MaxLiftDistance"/> 米）。
		/// 与 <see cref="CanTugRope"/> **天然互斥**：扛起要"够得着"、拉紧要"够不着" ⇒
		/// 同一时刻只可能出一条 ⇒ 两条共用 G 长按不会打架。
		/// </summary>
		/// <summary>
		/// **能不能把扛着的人放到这匹马的背上**（交互面板那条的判据；命令路径走 <see cref="CarryPlaceOnHorse"/>）。
		/// 条件：正扛着人（Riding）+ 目标是**能骑的马**（`HorseComponent.IsMount`）+ 够得着 + 那匹马上还没搁着人。
		/// </summary>
		public bool CanPlaceOnHorse(Agent focus, out string why)
			=> ResolveHorseForPlacement(focus, out _, out why);

		/// <summary>
		/// **要放上去的那匹马 = 面板焦点那一匹**（用户 2026-10-14 裁定："不是和检测身边有什么尸体或者
		/// NPC 一样的办法吗"）—— **就用同一套焦点检测**（射线 + 身边 4 米锥形搜索，
		/// 见 `InteractionMissionView.GetFocusdAgent`），**不再自己另写"就近找马"**。
		///
		/// 🔴 为什么把"就近找马"删了（我自己造的两轮 bug 的根源）：那是我为了绕开"人被扛在肩上挡住准星"
		///    加的旁路，结果它和焦点**口径不一致** —— 面板说"有马"、按下时又按另一套算，两个方向来回翻
		///    （隔老远也显示 / 走近了却不显示 / 按下去变成放下）。真正该修的是**焦点本身**：
		///    扛着的那个人已经从焦点里过滤掉了（<see cref="IsBodyCarried"/>）⇒ 准星能落到马上 ✓。
		/// </summary>
		public bool ResolveHorseForPlacement(Agent focus, out Agent horse, out string why)
		{
			horse = null;
			why = "-";
			try
			{
				if (!GrappleCarry.Enabled) { why = "carry off"; return false; }
				if (_carry.Phase != GrappleCarry.CarryPhase.Riding) { why = "not carrying anyone"; return false; }
				if (_carry.Carried == null) { why = "no carried agent"; return false; }
				// 焦点可能是 null（没看任何人）⇒ 命令路径会自己去取面板焦点，取不到就是"没对着马"
				Agent f = focus ?? InteractionMissionView.Instance?.GetFocusdAgent();
				if (f == null) { why = "look at the horse (nothing in focus)"; return false; }
				if (!AgentControlHelper.SafeIsActive(f)) { why = "that agent is gone"; return false; }
				if (!AgentControlHelper.IsRideableMount(f)) { why = "the focused thing is not a rideable horse"; return false; }
				if (_carry.IsOnHorse(f)) { why = "already on that horse"; return false; }
				horse = f;
				return true;
			}
			catch (Exception) { return false; }
		}

		/// <summary>命令层：**把扛着的人放上马背**（`custom.grapple carry horse`）。</summary>
		public string CarryPlaceOnHorse(Agent focus)
		{
			if (!ResolveHorseForPlacement(focus, out Agent horse, out string whyPick))
				return $"Error: cannot put them on a horse ({whyPick})"
					+ " -- point your crosshair at the horse (same detection as any nearby corpse/NPC);"
					+ " 'custom.grapple horse spawn' spawns one if you have none.";
			if (!_carry.PlaceOnHorse(horse, _bind, out string whyPut))
				return "Error: cannot put them on that horse (" + whyPut + ")";
			return $"OK: {horse.Name} now carries them on its back (walk/ride away and they follow;"
				+ " 'custom.grapple carry horseoff' to take them off;"
				+ " tune with 'custom.grapple carry horseoffset <back> [side]' / 'carry horseface along|across|reverse')";
		}

		/// <summary>**马背上是不是搁着我们的人**（交互面板"从马背拿下"那条的判据）。</summary>
		public bool CanTakeOffHorse(Agent horse, out string why)
		{
			why = "-";
			try
			{
				if (horse == null || !_carry.IsOnHorse(horse)) { why = "no one on that horse"; return false; }
				Agent me = Owner;
				Agent carried = _carry.Carried;
				if (me == null || carried == null) { why = "no agent"; return false; }
				Vec3 d = carried.Position - me.Position;
				d.z = 0f;
				if (d.Length > GrappleCarry.MaxHorseDistance)
				{
					why = $"too far ({d.Length:F1}m)";
					return false;
				}
				return true;
			}
			catch (Exception) { return false; }
		}

		/// <summary>命令层：**从马背拿下**（`custom.grapple carry horseoff`）：放回地面、回站缚。</summary>
		public string CarryTakeOffHorse()
		{
			if (_carry.Horse == null) return "Error: no one is on a horse back (carry horse first).";
			return _carry.TakeOffHorse(_bind, "command")
				? "OK: taken off the horse and set on the ground (back to standing bound); carry is over."
				: "Error: take-off failed.";
		}

		public bool CanLiftCarried(Agent a, out string why)
		{
			why = "-";
			try
			{
				if (!GrappleCarry.Enabled) { why = "carry off"; return false; }
				if (!IsBindingSomeone() || _bind.Target == null) { why = "no bound target"; return false; }
				if (a == null || !ReferenceEquals(_bind.Target, a)) { why = "not the one on the rope"; return false; }
				if (_carry.IsActive) { why = "already carrying"; return false; }
				if (_bind.IsLying) { why = "target is lying down"; return false; }
				Agent me = Owner;
				if (me == null) { why = "no main agent"; return false; }
				Vec3 d = a.Position - me.Position;
				d.z = 0f;
				if (d.Length > GrappleCarry.MaxLiftDistance) { why = $"too far ({d.Length:F1}m)"; return false; }
				return true;
			}
			catch (Exception) { return false; }
		}

		/// <summary>**【放下】能不能用**：正扛着（还没在放下的过程中）。</summary>
		public bool CanPutDownCarried
		{
			get { try { return _carry.Phase == GrappleCarry.CarryPhase.Riding; } catch (Exception) { return false; } }
		}

		/// <summary>命令层：摆位微调（米；默认 0 —— 对齐量本来就烘在被扛者的动画里）。</summary>
		public string CarrySetOffset(float forward, float right)
		{
			_carry.SetRuntimeOffset(forward, right);
			return $"OK: carry offset forward={forward:F2} right={right:F2}"
				+ " (runtime offset on top of PairActions.csv stance)";
		}

		/// <summary>
		/// **命令入口：对指定目标直接走"缠 → 倒 → 缚"**（`custom.grapple bind test`）——
		/// 不走飞行、不用开火，用来单独验机制（方案 §13.9 第 1 步）。
		/// </summary>
		public string BindTest(Agent target)
		{
			if (!GrappleBind.Enabled) return "Error: bind is OFF (custom.grapple bind on)";
			if (target == null) return "Error: no target agent found nearby (need one within 30 m, not a child; 'bind test mounted' picks a rider instead).";
			if (IsBusy) Release("bind test（清场重来）");

			Vec3 hit = GrappleBind.ChestOf(target);
			if (!_bind.Begin(Agent.Main, target, hit, out string why))
			{
				return "Error: cannot bind this target (" + why + ")";
			}
			_hook.Clear();                     // 清掉上一发的残留（Bind 自己拿着目标，不受影响）
			_hookPhase = HookPhase.Binding;
			_bindFromTest = true;              // 调试路径：不判"收起钩索/绳被拉断"（见字段注释）
			_landingNote = "binding (test path)";
			ShowBindRope();
			return $"OK: binding target '{target.Name}' (test path; watch 缠→倒→缚, then '{GrappleBind.BoundSeconds:F0}s' auto-release or 'bind release')";
		}

		/// <summary>
		/// **反向绑（调试路径，2026-10-13 第 3 条）**：**最近的 NPC 把玩家绑了**，
		/// 之后 `bind lead &lt;米&gt;` 让他牵着玩家走（绳长上限那条拽着玩家跟上）。
		///
		/// 与 <see cref="BindTest"/> 是**同一套机制、角色对调**（铁律 18 平权）：
		///   绳主 = 那个 NPC（他的右手是绳的近端）· 目标 = 玩家（被冻控制器、被拽着走、姿态归 bind.xml）。
		/// 玩家的控制器冻结/归还在 <see cref="GrappleBind.HoldTarget"/> / `RestoreAgent` 里，与 NPC 目标共用一条路。
		/// </summary>
		public string BindReverse(Agent attacker)
		{
			if (!GrappleBind.Enabled) return "Error: bind is OFF (custom.grapple bind on)";
			if (attacker == null) return "Error: no NPC found nearby (need one within 30 m, not a child, on foot).";
			if (Agent.Main == null) return "Error: no player agent.";
			if (IsBusy) Release("bind reverse（清场重来）");

			Vec3 hit = GrappleBind.ChestOf(Agent.Main);
			if (!_bind.Begin(attacker, Agent.Main, hit, out string why))
			{
				return "Error: cannot bind the player (" + why + ")";
			}
			_hook.Clear();
			_hookPhase = HookPhase.Binding;
			_bindFromTest = true;
			_landingNote = "binding (reverse test path)";
			ShowBindRope();
			return $"OK: '{attacker.Name}' binds YOU (test path). 'custom.grapple bind lead 6' = they walk 6 m and drag you along; "
				+ "'custom.grapple bind leadstop' = stop; 'custom.grapple bind release' = let go (you get your controls back).";
		}

		/// <summary>
		/// **任意两个人直接绑**（不飞钩；2026-10-23，NPC 平权入口）。
		/// `testPath` = true 时不判"收起钩索/绳被拉断"（调试命令）；AI 用 false。
		/// </summary>
		public string BindFromTo(Agent attacker, Agent target, bool testPath = true)
		{
			if (!GrappleBind.Enabled) return "Error: bind is OFF (custom.grapple bind on)";
			if (attacker == null || target == null) return "Error: no attacker/target.";
			if (!ReferenceEquals(attacker, Owner))
			{
				GrappleLogic other = EnsureFor(attacker);
				if (other == null) return "Error: no grapple session for the attacker.";
				return other.BindFromTo(attacker, target, testPath);
			}
			if (IsBusy) Release("bind from-to（清场重来）");

			Vec3 hit = GrappleBind.ChestOf(target);
			if (!_bind.Begin(attacker, target, hit, out string why))
			{
				return "Error: cannot bind (" + why + ")";
			}
			_bind.AiBindHoldSeconds = _pendingAiBindHoldSeconds;
			_pendingAiBindHoldSeconds = 0f;
			_hook.Clear();
			_hookPhase = HookPhase.Binding;
			_bindFromTest = testPath;
			_landingNote = "binding (from-to)";
			ShowBindRope();
			return $"OK: '{attacker.Name}' binds '{target.Name}'"
				+ (testPath ? " (test path)." : ".") + " Watch 缠→受击→站缚; release with 'custom.grapple npc release' or the panel.";
		}

		/// <summary>把"勾人那条绳"显示出来（测试路径没经过"飞行"，绳可能还藏着 / 没建）。
		/// 失败只记日志 —— 机制不受影响（绳是纯观感）。</summary>
		private void ShowBindRope()
		{
			try
			{
				Scene scene = MissionOf != null ? MissionOf.Scene : null;
				if (scene != null && !_rope.IsVisible)
				{
					if (_rope.Build(scene))
					{
						_rope.Show(GetRopeAnchor(), _bind.RopeEnd);
					}
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 绑人：绳显示失败（{ex.GetType().Name}）—— 机制不受影响");
			}
		}

		/// <summary>命令层：**绳主牵着走**（反向绑那条：NPC 走，玩家被拽着跟）—— 判定与执行在 GrappleBind。</summary>
		public string BindLead(float meters, bool run) => _bind.LeadForward(meters, run);

		/// <summary>命令层：绳主停下。</summary>
		public string BindLeadStop() => _bind.LeadStop();

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
			Agent main = Owner;
			Scene scene = MissionOf?.Scene;
			if (main == null || scene == null)
			{
				return "Error: no owner/scene.";
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

		// ─────────────────── 骑马夹具：上/下马请求（2026-10-13）───────────────────
		//
		// 🔴🔴 **为什么必须先把 AI 摘掉**（用户给的线索 + 反编译印证）：
		//    `Agent.EventControlFlags` 这个旗标字**每帧由 AI 重写**（`Agent.Tick`：
		//    `if (Mission.AllowAiTicking && IsAIControlled) TickAsAI();`，AI 走 `OnAIInputSet(ref eventFlag…)`
		//    把整字算出来再写回去）⇒ 我们外部 `|= Mount` 的位**活不过一帧**，native 根本看不到。
		//    ⇒ 正规顺序（CSDN 教程同款）：
		//        ① `agent.Controller = AgentControllerType.None` —— 摘掉 AI 控制器，旗标就没人覆盖了
		//        ② `agent.Mount(horse)` —— 内部 = `EventControlFlags |= Mount` + `SetInteractionAgent(horse)`
		//        ③ native 接手：播 `act_mount_horse_from_*`、把人挂到马上、回调 `Mission.OnAgentMount`
		//        ④ 上完把 Controller 还回去（引擎自己按 Controller 增减 AI 组件，见 `AgentCommonAILogic
		//           .OnAgentControllerChanged`：离 AI 就 `RemoveComponent`，回 AI 就 `AddComponent`）
		//    顺带说明：`Agent.Mount()` 里那三道 managed 检查（CanRide 旗标 / 骑术≥难度 / 当前动作 act_none）
		//    我们已经在命令里全部满足了（回执里那三个 True 就是它们）。
		//
		// ⚠️ 这是**测试夹具**，不是正式玩法逻辑：默认没有任何请求，等于什么都不做。

		private enum HorseReqKind { None, Mount, Dismount, DownWatch }

		private HorseReqKind _reqKind;
		private Agent _reqNpc;
		private Agent _reqHorse;
		private float _reqTimer;
		private float _reqLogTimer;
		private bool _reqWasAI;          // 请求前他是不是 AI 控制（收尾时按这个还回去）

		/// <summary>请引擎把 <paramref name="npc"/> 弄上 <paramref name="horse"/>（保持 <paramref name="seconds"/> 秒）。
		/// 调用方**负责先把 Controller 摘成 None**（<see cref="V.SetAgentControllerNone"/>，见上面那段"为什么"），
		/// 并把摘之前的状态用 <paramref name="wasAI"/> 传进来 —— 收尾时按它还原。</summary>
		public void RequestMount(Agent npc, Agent horse, float seconds, bool wasAI)
		{
			_reqKind = HorseReqKind.Mount;
			_reqNpc = npc;
			_reqHorse = horse;
			_reqTimer = seconds;
			_reqLogTimer = 0f;
			_reqWasAI = wasAI;
		}

		/// <summary>请引擎把 <paramref name="npc"/> 弄下马（同样要先摘 Controller）。</summary>
		public void RequestDismount(Agent npc, float seconds, bool wasAI)
		{
			_reqKind = HorseReqKind.Dismount;
			_reqNpc = npc;
			_reqHorse = null;
			_reqTimer = seconds;
			_reqLogTimer = 0f;
			_reqWasAI = wasAI;
		}

		/// <summary>落马 blow 出手之后的**看结果窗口**（只读数、不下手）：每秒打一行骑手/马的动作，
		/// 到点给判据行（还在马上 / 已落马 + 引擎选的受击动画）。</summary>
		public void RequestDownWatch(Agent rider, Agent horse, float seconds)
		{
			_reqKind = HorseReqKind.DownWatch;
			_reqNpc = rider;
			_reqHorse = horse;
			_reqTimer = seconds;
			_reqLogTimer = 0f;
			_reqWasAI = false;
		}

		private void TickMountRequest(float dt)
		{
			if (_reqKind == HorseReqKind.None || _reqNpc == null)
				return;

			_reqTimer -= dt;
			try
			{
				if (!AgentControlHelper.SafeIsActive(_reqNpc))
				{
					FinishHorseRequest("npc gone");
					return;
				}

				bool mounted = false;
				try { mounted = _reqNpc.MountAgent != null; } catch (Exception) { }

				if (_reqKind == HorseReqKind.Mount)
				{
					if (mounted) { FinishHorseRequest("MOUNTED", ok: true); return; }

					// 🔴 **马被别人骑走了 ⇒ 立刻中止并说清**（2026-10-13 实机）：那天连试三次都"没反应"，
					//    日志里 `horseHasRider` 从 0 变 1、我们却干等到超时才回一句 "timeout" —— 用户以为是命令失效。
					Agent horseRider = null;
					try { horseRider = _reqHorse != null ? _reqHorse.RiderAgent : null; } catch (Exception) { }
					if (horseRider != null && horseRider != _reqNpc)
					{
						string who = "someone else";
						try { who = horseRider.Name != null ? horseRider.Name.ToString() : who; } catch (Exception) { }
						FinishHorseRequest($"the horse was taken by {who} -- spawn another one and retry");
						return;
					}

					if (_reqTimer <= 0f) { FinishHorseRequest("timeout (still on foot)"); return; }
					ResendHorseFlag();
				}
				else if (_reqKind == HorseReqKind.Dismount)
				{
					if (!mounted) { FinishHorseRequest("DISMOUNTED"); return; }
					if (_reqTimer <= 0f) { FinishHorseRequest("timeout (still mounted)"); return; }
					ResendHorseFlag();
				}
				else // DownWatch：只读数不下手，到点给判据
				{
					if (_reqTimer <= 0f)
					{
						FinishHorseRequest("watch done: " + DownWatchVerdict(mounted));
						return;
					}
				}

				// 🔴 **每秒回读**：用来分清"native 把我们设的值清掉了"与"native 收下但不动手"
				_reqLogTimer += dt;
				if (_reqLogTimer >= 1f)
				{
					_reqLogTimer = 0f;
					string extra = "";
					try
					{
						extra = $" ridingOrder={_reqNpc.GetRidingOrder()} selMount={_reqNpc.GetSelectedMountIndex()}";
						if (_reqHorse != null)
						{
							extra += $" horseHasRider={(_reqHorse.RiderAgent != null ? 1 : 0)}"
								+ $" horseReserved={(_reqHorse.CommonAIComponent != null ? _reqHorse.CommonAIComponent.ReservedRiderAgentIndex : -1)}";
						}
					}
					catch (Exception) { }
					DebugLogger.Log($"[GrappleHorse] {_reqKind} request held: mounted={(mounted ? 1 : 0)}"
						+ $" controller={SafeController(_reqNpc)}{extra}"
						+ (_reqKind == HorseReqKind.DownWatch
							? $" clip='{ClipOf(_reqNpc)}' horseClip='{ClipOf(_reqHorse)}'"
							: "")
						+ $" left={_reqTimer:F1}s");
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[GrappleHorse] horse request tick exception (dropped): {ex.GetType().Name} {ex.Message}");
				FinishHorseRequest("exception");
			}
		}

		/// <summary>🔴 **每帧重发上下马旗标**（2026-10-13 调研结论，修"玩家点了没反应"）：
		/// 玩家侧的一切 `EventControlFlags` **会被输入层每帧清零重建**
		/// （`MissionMainAgentController` 先 `EventControlFlags = 0` 再按输入重建）⇒ 只点一次 = 下一帧就没了；
		/// NPC 侧虽然摘了控制器不会被覆写，重发同样幂等无害。
		/// 重发走 `Agent.Mount(...)` 而不是裸写旗标 —— 它顺手把 `SetInteractionAgent` 也补上（上马那一半需要）。
		/// ⚠️ 引擎放行下马还有一道"前进速度 &lt; 0.5 m/s 且马不在扬蹄"的门槛（native）—— 重发会一直等到它满足。</summary>
		private void ResendHorseFlag()
		{
			try
			{
				if (_reqKind == HorseReqKind.Mount && _reqHorse != null)
				{
					_reqNpc.Mount(_reqHorse);
				}
				else if (_reqKind == HorseReqKind.Dismount)
				{
					// 🔴 必须传"他正骑着的那匹"——`Agent.Mount()` 的 else 分支（= 下马）只在 `MountAgent == 传入者` 时才点旗标
					Agent m = _reqNpc.MountAgent;
					if (m != null) _reqNpc.Mount(m);
				}
			}
			catch (Exception) { }
		}

		/// <summary>落马 blow 的判据行：他还在马上吗 + 引擎给他选了哪条受击/摔落动画 + 马在做什么。</summary>
		private string DownWatchVerdict(bool mounted)
		{
			string riderClip = ClipOf(_reqNpc), horseClip = ClipOf(_reqHorse), horseAct = "-";
			try { if (_reqHorse != null) horseAct = _reqHorse.GetCurrentActionType(0).ToString(); } catch (Exception) { }
			return $"{(mounted ? "STILL MOUNTED (the flag did not take him off)" : "DISMOUNTED")}"
				+ $" rider-clip='{riderClip}' horse-clip='{horseClip}' horse-action={horseAct}"
				+ " (expect rider-clip in act_rider_only_fall_* / act_fall_rider_* for a real fall)";
		}

		/// <summary>读 agent 当前 0 号通道的 clip 名（失败 = "-"）。</summary>
		private static string ClipOf(Agent a)
		{
			try { return a != null ? V.ActName(a, 0) : "-"; } catch (Exception) { return "-"; }
		}

		/// <summary>收尾：**把控制权还回去**（引擎会按 Controller 重新挂上 AI 组件）+ 落一行日志。
		/// <paramref name="ok"/> = 这一趟是不是真成了（只有上马用得上：**没成就把我们设的坐骑令撤回**）。</summary>
		private void FinishHorseRequest(string why, bool ok = false)
		{
			Agent npc = _reqNpc;
			HorseReqKind kind = _reqKind;
			try
			{
				// 🔴 上马没成 ⇒ **撤回我们设的坐骑令**（`horse mount` 给他的 `SetRidingOrder(Mount)` + 选中的马）。
				//    留着会一直挂在他身上（2026-10-13 实机：那几次失败之后他的 `ridingOrder` 一直是 1）。
				if (kind == HorseReqKind.Mount && !ok && npc != null && AgentControlHelper.SafeIsActive(npc))
				{
					try
					{
						npc.SetRidingOrder(RidingOrder.RidingOrderEnum.Free);
						npc.SetSelectedMountIndex(-1);
					}
					catch (Exception) { }
				}

				if (_reqWasAI && npc != null && AgentControlHelper.SafeIsActive(npc) && !npc.IsAIControlled)
				{
					V.SetAgentAI(npc);
					DebugLogger.Log($"[GrappleHorse] controller restored to AI on '{why}'");
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[GrappleHorse] 🔴 restoring the controller failed ({ex.GetType().Name}) "
					+ "—— 那个人会一直没 AI（重进场景可恢复）");
			}
			DebugLogger.Log($"[GrappleHorse] {kind} request finished: {why}");
			_reqKind = HorseReqKind.None;
			_reqNpc = null;
			_reqHorse = null;
			_reqTimer = 0f;
			_reqLogTimer = 0f;
			_reqWasAI = false;
		}

		private static string SafeController(Agent a)
		{
			try { return a != null ? a.Controller.ToString() : "?"; } catch (Exception) { return "?"; }
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
			Agent player = Owner;
			Scene scene = MissionOf != null ? MissionOf.Scene : null;
			bool want = HandKitWanted && !_anchored && player != null && scene != null && IsHoldingGrapple();

			// 🔴 **反向绑（NPC 把我们绑了，2026-10-13）**：那一拍玩家手是被捆着的（状态机在演站缚/被拖行），
			//    手里再挂着"环 + 两截绳 + 一枚转着的钩"是错位的（而且钩已经挂在**我们胸口**了）。
			//    ⇒ 玩家是绳另一头那个人时，手里那套整体收掉（钩头那套由 Binding 分支自己摆）。
			if (want && (GrappleBind.IsTargetClaimed(player)
				|| (_bind.Target != null && ReferenceEquals(_bind.Target, player))))
			{
				want = false;
			}

			_lastGoodHandAge += dt;      // 「上一次好手点」的保鲜计时（每帧一次；见 GetHand）

			// 🔴 **先摆根实体**（钩索这件"东西"本身 = 挂在手上）—— 必须在本帧所有子件之前，
			//    因为子件写的是"相对 root 的局部帧"（用到的 root 帧要是本帧的，否则差一帧 ⇒ 抖）。
			if (want)
			{
				_rig.PlaceRoot(GetHand(), scene);      // 传 scene：根实体在这一步就按"手的位置"建出来（见 PlaceRoot 注释）
			}
			Trace("kit:root");

			// **左手环**（`custom.grapple handring 0|1`，**默认显示**）—— 环与"环→右手"那截绳（A）是一套：
			// 环没摆上时 A 也会被下面 `RingPlaced` 那道闸收掉 ⇒ **"看不到两手之间那截绳"先看这个开关**。
			// （2026-10-12：此前这里写着"默认关（2026-10-08 用户口径）"，用户明确否认 ⇒ 那是错归属，已改回默认开。）
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

			// A（环 → 右手）：独立开关（**默认开**，见 HandRopeAEnabled）；⚠️ 它还要过下面 `RingPlaced` 那道闸
			// —— 环没摆上就不建 A（那时 A 的两端会退化成同一个点，2026-10-08 修过）
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
		/// 参数：<see cref="HandHookRpmIdle"/> / <see cref="HandHookRpmAim"/> / <see cref="HandHookRpmMountedIdle"/>
		/// （三档转速：站地待机 / 瞄准 / **骑马待机**，判据 <see cref="IsAimingGrapple"/> 与 `Agent.HasMount`）·
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

			// 🔴 2026-10-23：自己正被另一根绳绑着 ⇒ 手里那套整体收掉（与 TickHandKit 同一条口径）。
			if (Owner == null || GrappleBind.IsTargetClaimed(Owner))
			{
				_hook.Unpark();
				if (_handHookRopeShown)
				{
					_handHookRopeShown = false;
					_rope.Hide();
				}
				return;
			}

			// 档 0 = 正常；**档 4 = 强制走下面那条**（不看总开关、不看握着谁 —— 它就是"现行为"的对照）
			// 🔴 **"装备里没有钩"也要收**（2026-10-12 用户裁定：视觉一致 —— "看得见的钩" = "能用的钩"）：
			//    手里那枚钩是**运行时实体**，判据原先只有"握着的是绳/钩" ⇒ 一个没带钩爪的玩家手里**照样**
			//    有一枚转着的钩，"看着一切正常、按开火键却打不出去"（缺件时引擎连拉弓都不进）。
			//    加上这条之后：缺钩爪 ⇒ 手里只剩左手那盘绳（物品网格），缺口一眼可见。
			bool hasHook = GrappleFirePatch.HasHookEquipped(Owner);
			if (HandProbeStage == 0 && (!HandKitWanted || !IsHoldingGrapple() || !hasHook))
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
						Agent m = Owner;
						wieldedId = (m != null && m.WieldedWeapon.Item != null) ? m.WieldedWeapon.Item.StringId : "(空手)";
					}
					catch (Exception)
					{
					}
					// 停摆原因三分（2026-10-12）：没握绳 / **缺钩爪** / 总开关关 —— 日志里一眼可辨
					string stopWhy = !IsHoldingGrapple()
						? $"此刻握着 {wieldedId}"
						: (!hasHook ? "装备里没有钩爪（缺件 ⇒ 手里的钩收掉、只剩那盘绳）" : $"总开关关着，握着 {wieldedId}");
					DebugLogger.Log($"[Grapple] 手里的钩：停（{stopWhy}，开开关={HandKitWanted}）");
				}
				return;
			}

			Agent player = Owner;
			Scene scene = MissionOf != null ? MissionOf.Scene : null;
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
			// 🔴 **骑马待机不转**（2026-10-23 用户裁定，见 HandHookRpmMountedIdle）：
			//    骑马时**没瞄准**用"骑马待机档"（默认 0 = 停住），站地上仍用 135；
			//    瞄准两档都照旧 270。停住 = 角度不再累加 ⇒ 钩停在圆周上这一刻的位置，瞄准时从这一刻接着转（不跳）。
			bool mounted = false;
			try { mounted = player.HasMount; } catch (Exception) { }
			float rpm = aiming ? HandHookRpmAim : (mounted ? HandHookRpmMountedIdle : HandHookRpmIdle);
			// 这三个是**玩家诊断**用的静态读数（`custom.grapple hand`）；NPC 那套不要覆盖它们。
			if (IsPlayerOwner)
			{
				HandHookMountedNow = mounted;
				HandHookAimingNow = aiming;
				HandHookRpmNow = rpm;
			}
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

		// ─────────────────── 缺件提示（2026-10-12）：握着绳、按开火键、装备里没有钩 ───────────────────

		/// <summary>缺件提示的冷却（秒）—— 连点开火键时最多每这么久一句（防刷屏）。</summary>
		public static float NoHookHintCooldown = 1.5f;

		private float _noHookHintLastAt = float.MinValue;

		/// <summary>「钩不能当箭用」提示的冷却（同上一档；两个提示各自计时）。</summary>
		private float _wrongWeaponHintLastAt = float.MinValue;

		/// <summary>"绑着人时不能开火"提示的冷却时间戳（2026-10-18；同上，连点不刷屏）。</summary>
		private float _boundHintLastAt = float.MinValue;

		/// <summary>
		/// **缺件提示**（2026-10-12 用户裁定）：握着钩索绳、按了开火键、可装备里**没有钩爪** ⇒ 弹一句红字。
		///
		/// **为什么挂在"按开火键"上、而不是"握上绳就提示"**：
		///   · 缺件时手里那枚钩**已经收掉了**（见 <see cref="TickHandHook"/> 的闸门）⇒ 不按开火键，玩家只是
		///     "拿着一盘绳"，此时提示 = 背景噪音；按下去打不响才是"我遇到问题了"的那一刻（设计哲学①反馈明确）。
		///     而且缺件时引擎**连拉弓都不进**（弓没有匹配弹药上不了弦），不按就完全没有别的信号。
		///   · 判据与"能不能开火"完全同源：`IsHoldingGrapple()` + `GrappleFirePatch.HasHookEquipped()`。
		///
		/// 触发 = **按下沿**（松手再按才再弹；按住不放不重复）+ <see cref="NoHookHintCooldown"/> 冷却。
		/// 装备好钩爪后判据自然为假 ⇒ 下次缺件立刻能再提示（不需要额外的"重新武装"状态）。
		/// 🔴 **UI 模态门控**：IM 面板里点卡片用的也是左键 ⇒ 面板 / 全屏界面 / ESC 菜单开着时不弹。
		/// </summary>
		private void TickNoHookHint()
		{
			// ① 开火键按下沿（左键 = 弓系的瞄准/放箭键；手柄 = 右扳机）。读输入失败 = 当作没按。
			bool pressed;
			try
			{
				pressed = Input.IsKeyPressed(InputKey.LeftMouseButton)
					|| Input.IsKeyPressed(InputKey.ControllerRTrigger);
			}
			catch (Exception)
			{
				return;
			}
			if (!pressed)
			{
				return;
			}

			// ② 缺件吗：握着绳（或瞄准时引擎报的钩）而装备里没有钩
			Agent main = Agent.Main;
			if (main == null || !IsHoldingGrapple() || GrappleFirePatch.HasHookEquipped(main))
			{
				return;
			}

			// ③ UI 模态门控（在面板里点左键不该弹这条）
			try
			{
				if (ImChatView.IsOpen || UiFullScreenHelper.IsFullScreenUiOpen() || UiFullScreenHelper.IsEscapeMenuOpen())
				{
					return;
				}
			}
			catch (Exception)
			{
			}

			// ④ 冷却（连点不刷屏）
			float now = Mission != null ? Mission.CurrentTime : 0f;
			if (now - _noHookHintLastAt < NoHookHintCooldown)
			{
				return;
			}
			_noHookHintLastAt = now;
			ShowNoHookHint();
		}

		/// <summary>
		/// 弹一次"缺钩爪"提示（命令 `custom.grapple hint` 也调它 —— 不按键也能验文案 / 本地化 / 颜色）。
		/// 玩家可见文本一律走 <see cref="LWNTextHelper"/>（铁律 13），key = `LWN_ui_grapple_no_hook`。
		/// </summary>
		public void ShowNoHookHint()
		{
			try
			{
				// 本地化：LWN_ui_grapple_no_hook（玩家可见文本）
				string text = LWNTextHelper.ResolveText("LWN_ui_grapple_no_hook",
					"No hook equipped - the grappling rope needs a Grapple Hook in an ammo slot to fire.");
				InformationManager.DisplayMessage(new InformationMessage(text, Colors.Red));
				DebugLogger.Log("[Grapple] 缺件提示：握着钩索绳、装备里没有钩爪（已提示玩家）");
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 缺件提示弹出失败（忽略）：{ex.GetType().Name} {ex.Message}");
			}
		}

		/// <summary>
		/// **「钩不能当箭用」的提示**（2026-10-12 用户裁定："普通弓应该射不出钩，不准把钩作为普通弓的箭"）。
		///
		/// **主调用方 = <see cref="GrappleAimGatePatch"/>**（每帧、在"进入瞄准之前"就把攻击输入抹掉，
		/// 并在按下沿调这里给玩家一句话）；<see cref="GrappleFirePatch"/> 的 refused 分支保留为**安全网**。
		///
		/// 🔴 为什么必须给字：拦下 = 按下攻击键什么都不发生，不解释就是看着像 bug。
		/// 冷却照 <see cref="NoHookHintCooldown"/>（连点不刷屏）。
		/// </summary>
		public void ShowWrongWeaponHint()
		{
			float now = Mission != null ? Mission.CurrentTime : 0f;
			if (now - _wrongWeaponHintLastAt < NoHookHintCooldown)
			{
				return;
			}
			_wrongWeaponHintLastAt = now;
			try
			{
				// 本地化：LWN_ui_grapple_hook_needs_rope（玩家可见文本）
				string text = LWNTextHelper.ResolveText("LWN_ui_grapple_hook_needs_rope",
					"The Grapple Hook only fires from the Grapple Rope - a normal bow cannot shoot it.");
				InformationManager.DisplayMessage(new InformationMessage(text, Colors.Red));
				DebugLogger.Log("[Grapple] 钩被别的武器开火 ⇒ 已拦下并提示玩家（钩只能通过钩索绳发射）");
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 拒发提示弹出失败（忽略）：{ex.GetType().Name} {ex.Message}");
			}
		}

		/// <summary>
		/// **「绳上还绑着人，不能开火」的提示**（2026-10-18 用户裁定，主调用方 = <see cref="GrappleAimGatePatch"/>）。
		///
		/// 背景：以前是"发射即替换"—— 绳上绑着人时再开一枪，会**把人悄悄放掉**再钩新的
		/// （`ThrowInternal` 的"先清场"）。一根绳只能系一头，"再钩一次"必然放掉上一个人，而玩家看不到任何提示
		/// ⇒ 实机反馈就是"我没松绳，怎么又能钩人？有 bug 吧"。
		/// ⇒ 现在**绑着人时禁止开火**（`GrappleAimGatePatch` 在"进瞄准之前"就抹掉攻击输入），
		///    并在这里给一句话：先松绳（交互面板【松绳】/ `custom.grapple bind release`）。
		/// 冷却照 <see cref="NoHookHintCooldown"/>（连点不刷屏）。
		/// </summary>
		public void ShowBoundNoFireHint()
		{
			float now = Mission != null ? Mission.CurrentTime : 0f;
			if (now - _boundHintLastAt < NoHookHintCooldown)
			{
				return;
			}
			_boundHintLastAt = now;
			try
			{
				// 本地化：LWN_ui_grapple_bound_no_fire（玩家可见文本）
				string text = LWNTextHelper.ResolveText("LWN_ui_grapple_bound_no_fire",
					"The rope is still tied to someone - slack the rope first (panel: Slack Rope). The grapple cannot fire while it holds a person.");
				InformationManager.DisplayMessage(new InformationMessage(text, Colors.Red));
				DebugLogger.Log("[Grapple] 绑着人时开火 ⇒ 已拦下并提示玩家（先松绳；不再「发射即替换」）");
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 绑人拒发提示弹出失败（忽略）：{ex.GetType().Name} {ex.Message}");
			}
		}

		// ─────────────── 弹药顺序归一化（2026-10-12 晚：从装备界面搬到"进战场一次性"） ───────────────

		private bool _ammoOrderNormalized;

		/// <summary>
		/// **进战场后一次性把"钩爪 vs 其它箭类"的槽位顺序排对**（每个 Mission 只做一次）。
		///
		/// **为什么要排**（用户实测 2026-10-12）：弓的弹药是**开火那一刻由引擎现挑**的，规则 = **按槽位顺序
		/// 取第一个同类弹药** ⇒ 弓(W1)+箭(W2)+钩(W3) 会先把箭射完、轮到钩时被瞄准闸拦下；箭/钩对调则一上来就打不出去。
		/// **方向由装备决定**：装备里有**绳**且**没有别的远程武器** ⇒ **钩在前**（绳要用钩）；
		/// 否则 ⇒ **钩在后**（让别的弓/弩优先用箭）。
		///
		/// 🔴 **为什么不在装备界面做**（原来那样做**会坏物品栏**，2026-10-12 实机事故）：直接写战役侧
		///    `Equipment` 的槽位会跟 `InventoryLogic` 的内部状态**脱钩**（界面上的槽位 VM 还指着原来的物品）
		///    ⇒ 症状 = **箭和钩爪再也脱不下来**。⇒ 改到"进战场那一刻"排：没有任何界面在看着这份装备，
		///    零 UI 风险，效果一样（弓按顺序挑弹药）。原实现留在 `Combat/GrappleAmmoOrderPatch.cs`（已停用备查）。
		///
		/// 🔴 只动**弹药槽之间**的顺序（钩 + 其它箭类，限 0-3 号主武器槽）：**绳、刀、盾一概不挪**。
		/// </summary>
		private void NormalizeAmmoOrderOncePerMission()
		{
			if (_ammoOrderNormalized)
			{
				return;
			}
			Agent main = Agent.Main;
			if (main == null)
			{
				return;          // 还没出生 ⇒ 下一帧再看
			}
			_ammoOrderNormalized = true;
			try
			{
				int first = (int)EquipmentIndex.WeaponItemBeginSlot;
				int last = (int)EquipmentIndex.NumPrimaryWeaponSlots;
				List<int> slots = new List<int>();          // 钩 + 其它箭 所在槽位
				int hookSlot = -1;
				bool ropePresent = false;
				bool otherRangedPresent = false;

				for (int i = first; i < last; i++)
				{
					MissionWeapon w = main.Equipment[(EquipmentIndex)i];
					if (w.Item == null)
					{
						continue;
					}
					bool isHook = w.Item.StringId == GrappleFirePatch.HookItemId;
					if (w.Item.StringId == GrappleFirePatch.RopeItemId)
					{
						ropePresent = true;
					}
					else if (!isHook
						&& (w.Item.Type == ItemObject.ItemTypeEnum.Bow || w.Item.Type == ItemObject.ItemTypeEnum.Crossbow))
					{
						otherRangedPresent = true;
					}

					if (isHook)
					{
						hookSlot = i;
						slots.Add(i);
					}
					else if (w.Item.Type == ItemObject.ItemTypeEnum.Arrows)
					{
						slots.Add(i);
					}
				}

				if (hookSlot < 0 || slots.Count < 2)
				{
					return;      // 没有"钩 + 至少一个其它箭" ⇒ 不动
				}

				slots.Sort();
				bool hookFirst = ropePresent && !otherRangedPresent;
				int hookIdx = slots.IndexOf(hookSlot);
				int wantIdx = hookFirst ? 0 : slots.Count - 1;
				if (hookIdx == wantIdx)
				{
					return;      // 顺序本来就对 ⇒ 一行都不动
				}

				int guard = 0;
				while (hookIdx != wantIdx && guard++ < 8)      // 只在**这批弹药槽之间**挪，不碰别的物品
				{
					int nextIdx = hookIdx + (wantIdx > hookIdx ? 1 : -1);
					SwapAgentSlots(main, (EquipmentIndex)slots[hookIdx], (EquipmentIndex)slots[nextIdx]);
					int tmp = slots[hookIdx];
					slots[hookIdx] = slots[nextIdx];
					slots[nextIdx] = tmp;
					hookIdx = nextIdx;
				}

				DebugLogger.Log($"[Grapple] 弹药顺序归一化（进战场一次性）：钩爪挪到 {(hookFirst ? "最前" : "最后")}"
					+ $"（{(hookFirst ? "装备里有钩索绳、且没有别的远程武器 ⇒ 钩优先" : "有别的弓/弩 ⇒ 让箭优先")}）");
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 弹药顺序归一化失败（忽略，不影响玩）：{ex.GetType().Name} {ex.Message}");
			}
		}

		/// <summary>把两个装备槽的内容**互换**（物品 + 品质修正 + 数量；空槽也支持）。</summary>
		private static void SwapAgentSlots(Agent a, EquipmentIndex x, EquipmentIndex y)
		{
			MissionWeapon wx = a.Equipment[x];
			MissionWeapon wy = a.Equipment[y];
			ItemObject ix = wx.Item;
			ItemObject iy = wy.Item;
			ItemModifier mx = wx.ItemModifier;
			ItemModifier my = wy.ItemModifier;
			short ax = wx.Amount;
			short ay = wy.Amount;
			Banner banner = a.Origin?.Banner;

			if (iy != null)
			{
				MissionWeapon w = new MissionWeapon(iy, my, banner);
				a.EquipWeaponWithNewEntity(x, ref w);
				a.SetWeaponAmountInSlot(x, ay, false);
			}
			else
			{
				a.RemoveEquippedWeapon(x);
			}

			if (ix != null)
			{
				MissionWeapon w = new MissionWeapon(ix, mx, banner);
				a.EquipWeaponWithNewEntity(y, ref w);
				a.SetWeaponAmountInSlot(y, ax, false);
			}
			else
			{
				a.RemoveEquippedWeapon(y);
			}
			a.UpdateAgentStats();
		}

		/// <summary>
		/// **体侧前后甩的轴** = 角色的**右方向**（水平、单位长，= `fwd × Up`，与 `BasisWithLocalZ` 同款手性）。
		/// 圆 ⊥ 它 ⇒ 圆落在**竖直面**里、在体侧前后甩。
		/// 只在"面朝哪"上依赖角色（`LookDirection`，竖直分量被叉乘自然消掉）；**完全不读骨骼** ⇒ 站地上/骑马一致。
		/// 用途 = ① `armaxis 1` 档 ② 小臂读不到时的兜底（2026-10-23 起，取代原来的"世界竖直"）。
		/// ⚠️ 它和"小臂垂下时的轴"（近似 -Up）是**两根不同的线**：不会出现"圆看着一样、转向反了"的错觉。
		/// </summary>
		private static Vec3 SideAxis(Agent player)
		{
			Vec3 fwd = player.LookDirection;
			if (fwd.LengthSquared < 1e-6f)
			{
				fwd = Vec3.Forward;
			}
			Vec3 r = Vec3.CrossProduct(fwd.NormalizedCopy(), Vec3.Up);
			return r.LengthSquared < 1e-6f ? Vec3.Forward : r.NormalizedCopy();
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
					return SideAxis(player);         // 体侧前后甩：轴 = 角色右方向 ⇒ 圆在竖直面里
				case 2:
					return fwd;                      // 左右横扫：轴 = 朝向 ⇒ 圆在正面（左右）面里
				case 3:
				{
					// 🔴 **上臂弦（肩 → 手）** —— ⚠️ 手臂一折这条弦就不是小臂了（用户当场指出）⇒ 优先用 4
					if (SpellCastInput.TryGetArmAxis(player, out Vec3 armAxis))
					{
						return armAxis;
					}
					return SideAxis(player);
				}
				case 4:
				{
					// 🔴🔴 **小臂轴（肘 → 手）** —— 用户 2026-10-07 晚的最终口径：
					// "(手臂)可能是折的，要以**手肘到手**的连线为轴，再向外探一点点"。
					// 骨索引走官方语义接口 `GetRealBoneIndex(HumanBone.ForearmR / HandR)`。
					// 🔴 **读得到就永远用它**（用户 2026-10-23 复核："能找到小臂数据还是小臂"）。
					// **读不到**才兜底 —— 兜底 = **体侧前后甩**（不是世界竖直！）：
					//    世界竖直与"小臂垂下"是同一根线、方向相反 ⇒ 转向整个翻过来
					//    （实机嫌疑：站地上对、骑马上反）。改成前后甩 = 换一个**明显不同**的平面，
					//    不会再和"对的形态"长得像却转反。
					if (SpellCastInput.TryGetForearmAxis(player, out Vec3 foreAxis))
					{
						if (_armAxisFallbackOn)
						{
							_armAxisFallbackOn = false;
							DebugLogger.Log("[Grapple] 小臂读数恢复 ⇒ 绕转轴回到小臂");
						}
						return foreAxis;
					}
					if (!_armAxisFallbackOn)
					{
						_armAxisFallbackOn = true;
						DebugLogger.Log("[Grapple] ⚠ 小臂读不到 ⇒ 绕转轴退回「体侧前后甩」"
							+ $"（臂骨=[{SpellCastInput.LastArmBones}]；骑马时出现这条 = 马上读不到小臂）");
					}
					return SideAxis(player);
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
			Agent player = Owner;
			Scene scene = MissionOf != null ? MissionOf.Scene : null;
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

		/// <summary>此刻"手上拿着钩索"吗？判据 = `Agent.WieldedWeapon` 是**绳**（`lwn_grapple_rope`）
		/// **或钩**（`lwn_grapple_hook`，= 弹药那件）。
		/// 🔴 2026-10-07 实机（"瞄准时钩看不见"）：**瞄准期间引擎把 `WieldedWeapon` 报成那支**（弹药），
		///   只判本体 ⇒ 一瞄准钩就被收掉（拔刀那一瞬还在，日志可证）。两件都认即可。
		/// 换到别的武器（刀/弓）时两件都不匹配 ⇒ 正常收掉 ✓。</summary>
		private bool IsHoldingGrapple()
		{
			try
			{
				Agent main = Owner;
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
		///    （栈顶 = 托管→本机转换、崩在 `MissionState.TickMission` 里；引擎日志最后一行 = `Render Requested: lwn_grapple_hook`）。
		///    ⇒ 恢复成右手（已验证）；"绳系在左手环上"那条观感**等有稳妥的左手骨读法再说**（要动就得先单独验证那个 API）。
		/// 画面口径：绳从**右手**（捏绳那只手）连到钩 —— 左手的环是独立道具，不参与绳的锚点。</summary>
		private Vec3 GetRopeAnchor()
		{
			// 🔴 **反向绑（NPC 牵玩家，2026-10-13 第 3 条）**：绳的近端永远在**绳主**那只手上 ——
			//    绳主是玩家 = 右手（下面那套，实机验证过）；绳主是 NPC = 读**那个 NPC**的右手，
			//    用的是同一个骨读数（`SpellCastInput.TryGetRightHandAnchor` 本来就收任意 agent）。
			//    读不到就退回"他身上抬一点"，**绝不退回玩家的手**（那会让绳从玩家身上长出来，一眼假）。
			//    2026-10-23：NPC 那套的 Owner 就是绳主；这里的"不同人"只剩反向绑那条调试路。
			try
			{
				Agent holder = _bind.Attacker;
				if (_bind.IsActive && holder != null && !ReferenceEquals(holder, Owner))
				{
					if (SpellCastInput.TryGetRightHandAnchor(holder, out Vec3 npcHand))
						return npcHand - Vec3.Up * SpellCastInput.HandAnchorUpOffset;
					return holder.Position + Vec3.Up * FallbackHandLift;
				}
			}
			catch (Exception) { }
			return GetHand();
		}

		/// <summary>手的世界位置（复用蓄力球那套挂点读取；取不到退回"身体坐标 + 抬一点"）。
		/// 🔴 **2026-10-08：把"蓄力球口径"的上抬减掉**，再**沿小臂外移到掌心**（<see cref="HandPalmOffset"/>）——
		///    三处（绳的近端 / 钩的圆心 / 开火起点）共用这一个点，所以只需调一个旋钮。
		///    （原 0.18 上抬是给阴魔斩球的：球挂手上方好看；钩/绳拿它当支点就会比掌心靠内靠上。）
		/// ⚠️ 球那边不受影响（它自己走 <see cref="SpellCastInput.TryGetRightHandAnchor"/>）。
		/// 2026-10-23：按**这一套的使用者**读手（NPC 那套就是 NPC 的右手）。</summary>
		private Vec3 GetHand()
		{
			return GetHand(Owner);
		}

		private Vec3 GetHand(Agent agent)
		{
			if (agent == null) return _anchor;
			Vec3 hand;
			if (!SpellCastInput.TryGetRightHandAnchor(agent, out hand))
			{
				// 🔴 **读失败时先拿"上一次读到的好手点"顶着**（窗口 = <see cref="HandHoldSeconds"/>）——
				//    2026-10-08 实机（日志实锤）：骨读数一旦被闸门拒，这里就直掉**脚底/盆骨** ⇒
				//    钩与绳双双甩到脚踝（用户症状："偶尔消失" / "走动时看不见"）。
				//    顶着的点**按 agent 的位移平移**（人走它也走）；超窗口还读不到才认输、退老兜底位。
				if (_lastGoodHandAge <= HandHoldSeconds)
				{
					SpellCastInput.LastHandSource = "hold " + SpellCastInput.LastHandSource;
					return _lastGoodHand + (agent.Position - _lastGoodAgentPos);
				}
				return agent.Position + Vec3.Up * FallbackHandLift;
			}
			hand -= Vec3.Up * SpellCastInput.HandAnchorUpOffset;      // 去掉蓄力球口径的上抬 → 回到手骨原点（腕关节）

			// 沿**小臂方向**外移到掌心（读不到就保持腕关节，不乱挪）
			if (Math.Abs(HandPalmOffset) > 0.0005f
				&& SpellCastInput.TryGetForearmAxis(agent, out Vec3 foreArm))
			{
				hand += foreArm * HandPalmOffset;
			}
			_lastGoodHand = hand;                    // 记下这次的好点（读失败时靠它顶）
			_lastGoodAgentPos = agent.Position;
			_lastGoodHandAge = 0f;
			return hand;
		}
	}
}
