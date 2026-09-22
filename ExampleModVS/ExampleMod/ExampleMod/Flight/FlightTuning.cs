using TaleWorlds.Library;

namespace LivingWorldNpcs.Flight
{
    /// <summary>
    /// 「飞行时怎么让主角别自己走」的手法（2026-09-21 T1 二次改）。
    ///
    /// 🔴 **为什么要有这个枚举**：第一版只做了 <see cref="Flags"/>，实机证明**无效** ——
    ///    按着 W 时日志里 `engineMove` 恒为 `0x0`、人却比板快 2.25 m/s 自己在走。
    ///    也就是说**玩家走路根本不看 `MovementFlags` / `MovementInputVector`**（那两个量对玩家是被动镜像）。
    ///    真正的开关应该是 `Controller`，但项目里对"切控制权"的既有观察互相矛盾（城镇里主角待机 ✓ /
    ///    野外被 AI 带走 ✗），而那是 native 行为、反编译看不到。**所以不再猜，做成开关一轮试出来。**
    ///
    /// 用法：<c>custom.flight freeze &lt;模式&gt;</c>（飞行中也能热切，立刻生效）。
    /// </summary>
    public enum FlightFreezeMode
    {
        /// <summary>不冻结。基线对照 —— 按 WASD 角色会自己走下板。</summary>
        Off,

        /// <summary>
        /// 保留 <c>Controller = Player</c>，每帧把引擎写进去的移动输入清零。
        /// 🔴 **2026-09-21 实机证明无效**（按 W 时 `engineMove=0x0`、人照样走）—— 保留仅作对照。
        /// </summary>
        Flags,

        /// <summary>
        /// <c>Controller = AI</c>。项目偷窃条用的就是这个，城镇场景**实机验证过：主角待机**。
        /// ⚠️ 风险：野外/战场里主角在编队里，AI 会把他往编队位置带（2026-09-18 实测跑去了别处）。
        /// </summary>
        Ai,

        /// <summary>
        /// ✅🔴🔴 **已实机验证通过（2026-09-21），现行做法**：<c>Controller = AI</c> + <c>SetIsAIPaused(true)</c>
        /// —— **AI 接管但被暂停**。
        ///
        /// **它为什么是答案**：飞行要同时满足三件事，只有这一档全中 ——
        ///   ① 玩家 WASD 被拿掉（切 AI 就成立，用户在地上实测过）
        ///   ② 板照样托人（切 AI 不影响碰撞承载，实机日志 `z` 差恒为 0.37）
        ///   ③ **不让"脚下平台在动"这件事干扰 AI** —— 这正是 `ai` 档失败的原因：
        ///      AI 有自己的平台自适应，板一动它就跟着调整，表现为在板上乱走/滑移。
        ///      **暂停 AI = 那套自适应整个不跑** ⇒ 人老实待在板上。实测：板停人也停、偏移恒定不漂。
        ///
        /// 🔴 已试过的三条死路（别重走）：
        ///   · 保留 Player + 每帧清零移动输入 → 无效（玩家走路不看那两个量）
        ///   · <c>MissionMainAgentController.IsDisabled</c> → 无效（<c>MissionScreen.UpdateCamera</c> 每帧冲它）
        ///   · 只切 AI 不暂停 → 冻得住输入，但 AI 跟移动中的板较劲
        /// </summary>
        AiPaused,

        /// <summary>
        /// 🔴🔴 **首选档（2026-09-21 二次反编译找到）**：<c>Controller</c> 保持 <c>Player</c> 不动，
        /// 只把**引擎的玩家控制器**关掉 —— <c>MissionMainAgentController.IsDisabled = true</c>。
        ///
        /// 反编译依据（<c>TaleWorlds.MountAndBlade.View.MissionViews.MissionMainAgentController</c>）：
        /// <code>
        /// public bool IsDisabled { get; set; }        // ← 公开可写
        ///
        /// OnPreMissionTick:
        ///   if (... &amp;&amp; !Mission.MainAgent.IsAIControlled &amp;&amp; !IsDisabled &amp;&amp; _activated)
        ///   { ...; ControlTick(); LookTick(dt); }     // ← 玩家输入就写在这两行里
        ///   else { LockedAgent = null; }
        /// </code>
        ///
        /// **为什么它正好是甜点区**：
        ///   · <c>ControlTick</c> 不跑 ⇒ 引擎**不写**移动输入（WASD 归零）——这是我第一版想达到但没达到的效果；
        ///   · <c>IsAIControlled</c> 仍是 false ⇒ <c>TickAsAI</c> 不跑，**AI 没有机会跟木板较劲**；
        ///   · <c>Controller</c> 仍是 <c>Player</c> ⇒ **板托人这套机制原封不动**（那是唯一已实机验证过的组合）。
        ///
        /// ⚠️ 代价：<c>LookTick</c> 也一起停了（角色不再自动转向镜头方向）——
        ///    对飞行无碍，机身朝向本来就由我们每帧写（<c>TurnBody</c>）。
        ///    **相机不受影响**：相机走 <c>Mission.OnTick</c> 的 <c>_missionState.Handler.UpdateCamera</c>，是另一条路。
        ///
        /// ⚠️ 会连 <c>InteractionComponent.FocusTick</c>（互动提示）一起停 —— 飞行中不需要。
        /// </summary>
        CtrlOff,

        /// <summary>
        /// <c>Controller = AI</c> + <b>把玩家从编队里摘出来</b>（<c>Formation = null</c>，落地还回去）。
        /// 设计意图：AI 会「跟移动中的木板较劲」多半是因为它要把 agent 带回**编队给它的位置** ——
        /// 掐掉这个目标来源，AI 就没有走路的理由了。
        /// 🔴 未验证。
        /// </summary>
        AiDetach,

        /// <summary>
        /// <c>Controller = None</c> —— 引擎 AI 完全退场、没人驱动位置（<c>VersionCompat</c> 的原注释）。
        /// 🔴 未验证：需要确认「没人驱动」时板**还托不托得住**人。
        /// </summary>
        None
    }

    /// <summary>
    /// 飞行参数集中地（2026-09-21）。
    /// 改手感只动这里；控制台 <c>custom.flight</c> 可热调其中几项，不必重编译。
    ///
    /// 🔴 LWN 是通用基座 —— 这里不许出现任何世界观词（铁律 3）。
    /// </summary>
    public static class FlightTuning
    {
        // ───────────────────────── 载具（隐形实心板）─────────────────────────
        // 机制依据：Knowledge/骑砍2Agent运动与位置机制.md §6.6 ——
        // 引擎写不进 agent 的 Z，只能靠"脚下一块实心道具 + 每帧瞬移它"把人托起来。

        /// <summary>承载用的实心预制体。网格会被隐藏，留下物理体当"地面"。</summary>
        /// <summary>
        /// 飞行载具的预制体 —— 🔴 **2026-09-21 起就是法阵自己**
        /// （`Taikou/Prefabs/lwn_flight_sigil.xml`：根带 `bo_wooden_platform_a` 碰撞 + 子带法阵网格）。
        /// 旧值 `wooden_platform_a` 已废 —— 那个方案要另挂一张法阵并"把木板藏起来"，
        /// 而**隐藏会把碰撞一起干掉**（实机摔死过主角）。
        /// </summary>
        public static string CarrierPrefab = "lwn_flight_sigil";

        /// <summary>
        /// 载具网格名（**生成前的存在性探针用**）。
        /// 🔴 网格不在任何包里却去 `Instantiate` = native 访问违例，**游戏当场崩、try/catch 拦不住**。
        /// </summary>
        public static string CarrierMeshName = "lwn_flight_sigil";

        /// <summary>wooden_platform_a 的顶面在局部坐标里的高度（实测 0.37 米）。</summary>
        public static float CarrierTopLocalZ = 0.37f;

        /// <summary>
        /// 生成时板面比**玩家真实碰撞体的底面**再低多少（米）。
        ///
        /// 🔴 **口径改过一次（2026-09-21 用户实机反馈"卡上木板"）**：
        ///    旧口径 = "拿 `agent.Position` 当脚底，往下压 0.45"。两处都不对：
        ///    ① `Position` 只是**假设**等于脚底，二段跳时人还在空中，它和真实碰撞体不一定同高；
        ///    ② 下压 0.45 而板面只在原点上方 0.37 ⇒ **板面其实在脚底下方 8cm**，
        ///       于是人先自由落体 8cm"砸"在板上，同时板正以 7 m/s 往上冲 = 那一下顿挫。
        ///    现口径 = `板面 = 碰撞体底面 − 本值`（碰撞体底面走
        ///    <see cref="TaleWorlds.MountAndBlade.Agent.CollisionCapsule"/> 真值，见 CarrierBoard）。
        ///    留间隙是为了**避免插进碰撞体**（5.3 米宽的大盒子，一旦重叠，解算会往最短方向推人）。
        ///    🔴 2026-09-21 用户："平面再稍微往下一点点" —— 0.03 → **0.06**（视觉上让脚下留出空隙）。
        /// </summary>
        public static float CarrierSpawnGap = 0.06f;

        /// <summary>
        /// 起飞时**等玩家真的踩到板上**再开始抬升（最多等这么久，秒）。0 = 不等。
        ///
        /// 为什么需要：触发方式是**二段跳**，按空格那一刻人还在空中（可能还在上升）。
        /// 板生成在脚下 3cm 且**保持不动**，等人落回来（`IsOnLand` 为真）再抬升 ——
        /// 否则"人的抛物线"和"板的匀速上升"两套运动打架 = 起飞那下不平滑。
        /// 取值口径：一次跳跃在空中总共约 1 秒，**取 1.0 秒足够等到落回**（正常 0.2~0.5 秒就等到）；
        /// 超时兜底照常抬升（不会卡住）。日志会打实际等了多久。
        /// </summary>
        public static float TakeoffSettleSeconds = 1.0f;

        // ───────────────────────── 法阵（纯视觉）─────────────────────────

        // ── 🪦 已退役（2026-09-21 合一版）：法阵不再是"另挂的一个实体"，它就是载具本身 ──
        //    SigilPrefab / SigilMeshName / SigilLiftZ / HideCarrier / CarrierHideMode
        //    这几个字段连同"隐藏载具"整层逻辑一起废了。保留注释只为记录来龙去脉，
        //    **实机验证合一版没问题后可以整段删掉**（见 plans/玩家飞行-实施方案.md）。

        // ───────────────────────── 起飞节拍 ─────────────────────────
        // 🪦 2026-09-22 删掉了四个退役字段（hover / clearance / maxalt / vrate）——
        //    它们服务的是"板自动抬升 + 高度夹取"，那套早已删掉（板只按 WASD 动）。
        //    来龙去脉见 plans/玩家飞行-实施方案.md，不留在代码里当死重量。

        /// <summary>
        /// 起飞姿态（`act_fly_start`）播多久 —— 登板之后**板不动**，只是把这 1.5 秒的入姿动画演完；
        /// 玩家中途给任何方向输入就立刻交给飞行控制。
        /// 取值 = 该 clip 的真实时长：(46 帧 − 1) ÷ 30 = **1.5 秒**。
        /// </summary>
        public static float TakeoffAnimSeconds = 1.5f;

        /// <summary>
        /// 起飞时**先切动作，板晚这么久才召唤**（秒）。0 = 同一帧出板。
        ///
        /// 为什么要延迟（2026-09-22 用户裁定）：动作是输入的即时反馈（按空格立刻起势），
        /// 而"人落到板上"那一拍如果和动作同时发生，会读成一个独立的**落地**阶段。
        /// 让板晚 0.2 秒出现 ⇒ 下落最后一段落在"起飞动作已经播起来之后"，观感连贯。
        /// </summary>
        public static float TakeoffSpawnDelay = 0.2f;

        /// <summary>
        /// 🔴 起飞动作的**淡入时长**（秒），单独一个值，不吃全局的 <see cref="AnimBlendIn"/>。
        ///
        /// 为什么（2026-09-21 用户两次反馈"hoverstart 播晚了 / 前面多一段脚踩平面"）：
        ///    动作其实在**按下空格那一帧**就设了（见 BeginTakeoff），但淡入吃的是全局 0.3 秒 ——
        ///    那 0.3 秒里人还是**跳跃/下落的姿势**在淡出，看起来就像"先踩一下平面才开始起飞"。
        ///    ⇒ **用户裁定：进飞行模式就秒播**（跳跃中一按空格，姿势立刻换）⇒ 本值 = **0（不淡化）**。
        ///    觉得突兀就往上调（0.05 / 0.1）。
        /// </summary>
        public static float TakeoffBlendIn = 0f;

        /// <summary>
        /// 起飞动作从**第几秒开始播**（跳过 clip 开头，秒）。0 = 从头播。
        ///
        /// 用途：如果那段"脚踩平面"是 **clip 自身开头**带的（不是淡入造成的），就从这里跳过它。
        /// 会按 clip 时长换算成引擎要的 startProgress。热调：`custom.flight tune takeoffskip 0.3`
        /// </summary>
        public static float TakeoffSkipSeconds = 0f;

        /// <summary>
        /// 进入空中态后**多久内不判"撞地"**（秒）。
        ///
        /// 为什么需要：**板现在不自动抬升了** —— 二段跳按得早时，板就停在离地十几厘米处，
        /// 撞地检测（板顶 ≤ 地面 + 0.25）会当场把人判成落地，刚起飞就结束。
        /// 给一段宽限：这段时间里玩家抬头 + W 自己就升上去了。
        /// 0 = 不宽限（会回到"贴着地起飞立刻判落地"）。
        /// </summary>
        public static float LandTouchGraceSeconds = 1.0f;

        // ───────────────────────── 冻结（T1，2026-09-21）─────────────────────────

        /// <summary>
        /// 「飞行时怎么让主角别自己走」—— 见 <see cref="FlightFreezeMode"/> 每个档位的说明与验证状态。
        ///
        /// 🔴 **默认 <see cref="FlightFreezeMode.AiPaused"/>**（2026-09-21 `aipause` 实机验证通过后定为现行做法）：
        ///    起飞自动冻、落地自动还，有安全网。
        ///    想临时关掉：<c>custom.flight freeze off</c>（飞行中热切，不用重编译）。
        ///    **注意 `Off` 档 = 不冻，按 WASD 角色会自己走下板。**
        ///
        /// 🔴 为什么不再用"每帧清零移动输入"那条路：实机日志证明**玩家走路不看那两个量**
        ///    （按 W 时 `engineMove` 恒为 `0x0`，人却比板快 2.25 m/s）。详见 <see cref="FlightFreezeMode.Flags"/>。
        /// </summary>
        public static FlightFreezeMode Freeze = FlightFreezeMode.AiPaused;

        /// <summary>
        /// 载具**单帧位移上限**（米）。正常一帧最快也就 26 m/s × dt ≈ 0.5 米。
        /// 设这条纯粹是数值兜底：出正反馈时最坏也只是"飞得慢"，不会窜出去或炸数值。
        /// 🔴 它**不是**玩法修正，触发时一定有别的 bug —— 所以触发会打日志。
        /// </summary>
        public static float MaxStepPerFrame = 1.5f;

        // ───────────────────────── 速度 ─────────────────────────

        /// <summary>巡航速度（米/秒）。</summary>
        public static float CruiseSpeed = 9f;

        /// <summary>冲刺速度（按住左 Shift）。</summary>
        public static float BoostSpeed = 26f;

        /// <summary>速度趋近速率（米/秒²）—— 手感上的"惯性"。</summary>
        public static float Accel = 20f;

        /// <summary>落地阶段把板降到地面的速率。</summary>
        public static float LandRate = 5f;

        /// <summary>
        /// 🔴 **落地动画的时长（秒）** —— 触地之后**至少等这么久**才收摊（还相机 / 清动作通道）。
        ///
        /// 为什么要等（2026-09-21 用户反馈"好几次看不到 landing 动画"）：
        ///    原来一触地就 <c>FinishFlight</c>，而它**同一帧**干三件事 —— 拆载具 / **还相机** / 清动作。
        ///    从贴地短按落地时，板从 1 米降到地面只要 **0.2 秒** ⇒ 动画被"咔嚓"掉，
        ///    同时画面还从自定义相机跳回引擎相机 ⇒ 观感上就是"没有落地动作"。
        ///    实测这条 clip（`flight_superland_a`）的真实时长 = (61 帧 − 1) ÷ 30 = **2.0 秒**。
        /// </summary>
        public static float LandAnimSeconds = 2.0f;
        /// <summary>落地阶段的硬上限（秒）—— 防止"动画时长"配错时把人卡在落地态出不来。</summary>
        public static float LandMaxSeconds = 5f;

        /// <summary>
        /// 🔴 **空格落地的两种终点不一样**（2026-09-21 用户裁定）：
        ///    · **空格导致的接地**（短按贴地 / 长按下降）⇒ **不播落地动画**、保持悬停待机姿势下降，
        ///      触地**当场**收摊 → 引擎走跑接管。理由：这是玩家主动的"放下"，本来就轻，落地动画反而像摔了一跤。
        ///    · **撞地**（飞着撞上地形）⇒ 照旧播落地动画 + 等它演完（破坏性的动作需要个交代）。
        /// 本开关只影响第一种（默认 `false` = 不播）；调成 `true` 就回到"两种都播"的老行为。
        /// </summary>
        public static bool LandAnimOnGentle = false;

        // ───────────── 落地手势（🔴 2026-09-21 用户重新定义，与起飞不对称）─────────────

        /// <summary>
        /// **离板多远算"掉下去了"**（米）—— 超过就强制收摊（拆板 + 还相机 + 解冻）。
        ///
        /// 为什么需要（2026-09-22 用户实机）：飞行撞墙时，**板会瞬移穿墙**（它是逐帧 SetFrame），
        /// 而人会被墙挡住 ⇒ 人从板上掉下来。那时若不收摊，就留下"人在半空/地上 + 被冻结 + 板还在天上"
        /// 的坏状态（走不动、也飞不了）。判据用"玩家碰撞体底面 vs 板面"的**三维距离**，
        /// 站着时只有 0.37 米，阈值留到 1.2 米足够宽容。
        /// </summary>
        public static float FallOffDistance = 1.2f;

        /// <summary>
        /// 触地瞬间下降速度 ≥ 它 ⇒ 算**硬着陆**（演落地动画）。
        ///
        /// 两种落地的分界（2026-09-22 用户定义）：**自然慢速落地不播动画**、**快速俯冲撞地播动画**。
        /// 取 7.5：长按下降的速率是 <see cref="DescendRate"/> = 6 ⇒ 稳定落在"轻放"那侧；
        /// 巡航俯冲 45° ≈ 6.4（仍算轻）、60° ≈ 7.8（算硬），冲刺俯冲必是硬着陆。
        /// </summary>
        public static float HardLandingSpeed = 7.5f;

        /// <summary>短按空格是否可用于落地（总开关）。关掉 = 只能靠长按下降撞地落。</summary>
        public static bool LandByTap = true;

        /// <summary>
        /// 短按落地的高度闸（米）：板顶离地面在这个高度以内，短按空格才落地。
        /// 默认 8 —— 取"刚二段跳进浮空（悬停高度 6m）"再多留一点余量，
        /// 语义就是"反悔刚才那一跳"。**高空平飞时短按不落地**（防误触）。
        /// </summary>
        public static float LandTapMaxHeight = 8f;

        /// <summary>正在俯冲（冲向地面）时短按空格是否可落地。</summary>
        public static bool LandTapWhileDiving = true;

        /// <summary>
        /// "冲向地面"的判据：镜头前方向的竖直分量 ≤ −这个值。
        /// 默认 0.42（≈ 低头 25°），与 <see cref="PitchThreshold"/> 同一量级但**故意分开** ——
        /// 姿态动画的阈值和落地判定是两件事，将来调一个不该牵动另一个。
        /// </summary>
        public static float LandTapDivePitch = 0.42f;

        /// <summary>长按空格是否进入持续下降。关掉 = 长按空格无作用（只剩短按落地）。</summary>
        public static bool LandByLongPressDescend = true;

        /// <summary>持续下降的速率（米/秒）。长按空格时垂直分量整个被它接管。</summary>
        public static float DescendRate = 6f;

        // ───────────────────────── 撞地自动落地（N4，2026-09-21）─────────────────────────

        /// <summary>飞行中板顶触地是否自动进落地。关掉 = 退回"只能手动长按空格落地"。</summary>
        public static bool LandOnGroundTouch = true;

        /// <summary>
        /// 触地判定的容差（米）。板顶离地面小于它才算"撞上"。
        /// 给容差是为了让**贴地掠过**不被误判成落地 —— 真撞上去才落。
        /// </summary>
        public static float LandTouchEps = 0.25f;

        /// <summary>停在最低点多久自动落地（秒）。玩家一直往下压 = 想下来。</summary>
        public static float AutoLandSeconds = 0.45f;

        // ───────────────────────── 长按 ─────────────────────────

        /// <summary>长按空格多久算触发（秒）。</summary>
        public static float LongPressSeconds = 0.65f;

        /// <summary>
        /// 🔴 **起飞主路（2026-09-21 N2 用户要求）**：**跳跃中按空格**（二段跳）。
        /// 判定 = `!Agent.IsOnLand()`（离地即为真）+ 空格按下沿。
        /// 因为不要求"刚跳过"，**从高处坠落时按空格同样起飞** —— 这是想要的（摔下来能自救）。
        /// </summary>
        public static bool TakeoffByDoubleJump = true;

        /// <summary>
        /// 后备路：站着**长按空格**起飞。🔴 默认保留 —— 长按同时还是**落地**的触发，
        /// 关掉它只影响"起飞"这一侧，落地手势不受影响。
        /// </summary>
        public static bool TakeoffByLongPress = true;

        // ───────────────────────── 俯仰姿态 ─────────────────────────

        /// <summary>
        /// 镜头前方向量的**竖直分量**超过多少算「在爬升 / 在俯冲」。
        /// 0.42 ≈ 抬头 25°。朝向的俯仰由动画表现（引擎的 agent 转不了俯仰，见 §三）。
        /// </summary>
        public static float PitchThreshold = 0.42f;

        /// <summary>
        /// 退出俯仰姿态的阈值（**迟滞**，比进阈值小）。
        /// 为什么需要：姿态之间是 120°~180° 的大翻转（实测），单一阈值下玩家把镜头停在
        /// 阈值附近会让动画来回翻。进出用两个阈值就不会抖。
        /// </summary>
        public static float PitchExitThreshold = 0.30f;

        // ── 压弯（倾斜）触发（2026-09-22 用户裁定后接）─────────────────────────
        // 用**横移输入**当压弯：|MoveAxis.x| 超过进阈值算在压弯，低于出阈值退出（迟滞）。
        // 理由：飞行里"A/D 侧移"就是压弯的语义；而且不用再引入"转向角速度"这种要额外平滑的量。
        // 0 = 关掉压弯（两个阈值都填 0 就永远不进压弯状态）。
        /// <summary>进压弯的横移阈值（|A/D 输入|）。</summary>
        public static float BankThreshold = 0.35f;

        /// <summary>退出压弯的阈值（迟滞，比进阈值小）。</summary>
        public static float BankExitThreshold = 0.20f;

        /// <summary>
        /// 姿态变化时在屏幕上弹一条提示（默认开，调姿态时用；`custom.flight tune statemsg 0` 关）。
        /// 只显示**状态名**（idle / cruise / leanL / boostLeanR …），跟代码和日志里的名字一致。
        /// </summary>
        public static bool ShowStateMessages = true;

        // ───────────────────────── 机身转向 ─────────────────────────

        /// <summary>
        /// 机身水平的**转向角速度**（度/秒）。0 = 关掉平滑（瞬时转向，回到旧行为）。
        ///
        /// 为什么要它（2026-09-21 用户实机反馈「起步时角色朝向会抖一下」）：`SetMovementDirection`
        /// 写下去是**立刻生效**的，转镜头时人「啪」地跟过去。加上限速之后，机身以固定角速度追
        /// 目标方向（最短弧），观感像真的在转体。
        ///
        /// 取值口径：540°/s ⇒ 转 180°（按 S 转身）要 0.33 秒，转 90° 要 0.17 秒 ——
        /// 既看得出在转、又不拖沓。**嫌慢调大，想回到瞬时转向填 0。**
        /// 热调：`custom.flight tune turnrate 540`
        /// </summary>
        public static float TurnRateDegPerSec = 540f;

        // ───────────────────────── 动画 ─────────────────────────
        // 🔴 **"什么时候播哪条动画"的规则不在这里** —— 在注册制的定义 `Flight/FlightAnimMachine.cs`
        //    （状态表 + 转移表）。本节这些值是**定义要用的参数**（过渡时长 / 动作名 / 核对周期）。

        /// <summary>
        /// 动作切换的交叉淡化时长（秒）。这就是骑砍2 里能做的"混合空间"。
        /// 取 0.3 而不是 0.2：实测姿态之间是 **120°~180° 的大翻转**（参见巡航↔趴姿↔头朝下），
        /// 0.2 秒翻 180° 看着像猛地翻跟头。
        /// </summary>
        public static float AnimBlendIn = 0.3f;

        // ───────────────────────── 运动相机（N5，2026-09-21）─────────────────────────

        /// <summary>飞行期间是否接管相机（运动机位）。关掉 = 用引擎默认跟随相机。</summary>
        public static bool UseFlightCamera = true;

        /// <summary>
        /// 相机**机位之间**的渐变时长（秒）。
        ///
        /// 🔴 **2026-09-21 起与 <see cref="AnimBlendIn"/> 解绑**：原先是两个字段取同一个值
        /// （用户当时要求"运动动画渐变和相机渐变一起做"），后经实机试用后**用户裁定机位过渡要更长**（0.3 → 0.6）：
        /// 机位是"镜头自己滑过去"，慢一点更像运镜；而动画交叉淡化翻的是 120°~180° 的大姿势，
        /// 拖到 0.6 秒会显得黏。⇒ 现在**各管各的**。
        /// 想再同步回去：`custom.flight tune camblend &lt;秒&gt;`（两个一起改）。
        /// </summary>
        public static float CamBlendIn = 0.45f;

        /// <summary>右键是否可以进瞄准机位（只在悬停 / 巡航生效，加速时不给进）。</summary>
        public static bool AimOnRightClick = true;

        /// <summary>
        /// **接管/归还相机时做"交接"**（2026-09-21 用户提问后补，默认开）。
        ///
        /// 关掉 = 旧行为：接管瞬间**硬切**到运动机位、归还瞬间**硬切**回引擎相机。
        /// 开着 = 两件事：
        ///  ① **进**：从"引擎相机交班那一帧"（位置 + 朝向 + FOV）插值到我们的机位，用时 <see cref="CamBlendIn"/>；
        ///  ② **出**：归还前把**我们当前的朝向写回引擎相机**（角度是私有 setter，走反射）——
        ///     否则引擎相机从**接管那一刻**的冻结朝向恢复，飞行中转过视角的话落地会甩回去。
        /// </summary>
        public static bool UseCamHandover = true;

        /// <summary>
        /// 归还相机时是否把当前朝向写回引擎（<see cref="UseCamHandover"/> 的一部分，单独一个开关便于排除故障）。
        /// 写回失败（反射拿不到 setter，换版本/换引擎）只打一行日志，不影响飞行。
        /// </summary>
        public static bool CamHandBackLook = true;

        // ── 相机接管后的"看"（🔴 接管相机就必须接管看，见 FlightCameraRig 的类型注释）──

        /// <summary>
        /// 鼠标灵敏度：**每像素转多少度**。最终值 = 本值 × 引擎的 <c>Input.MouseSensitivity</c>
        /// （跟随玩家在选项里的设置），所以这里只给基准。
        /// </summary>
        public static float CamLookSensitivity = 0.12f;

        /// <summary>上下视角钳制（度）。别让它翻过头 —— 抬头到 89 度以上画面会翻。</summary>
        public static float CamPitchMin = -80f;
        public static float CamPitchMax = 75f;

        /// <summary>鼠标左右是否反向（实机觉得转反了就翻这个）。</summary>
        public static bool InvertCamX = false;

        /// <summary>鼠标上下是否反向。</summary>
        public static bool InvertCamY = false;

        /// <summary>
        /// 每隔多久**核对一次**动画有没有被引擎抢回去（秒）。
        /// 为什么需要：0 号动作通道是引擎 locomotion 系统也有权写的，我们设完不保证一直有效。
        /// 为什么不能每帧核对：每帧重设 <c>SetActionChannel</c> 会把动画**卡在第 0 帧**。
        /// 现在由通用状态机消费（<c>AnimMachineDef.RecheckSeconds</c> ← 这里）。
        /// </summary>
        public static float ActionRecheckSeconds = 0.5f;

        // ───────────────────────── 动作名 ─────────────────────────
        // 🔴 这些是「动作名」，不是 clip 名 —— clip 名在内容包的 action_sets.xml 里绑，两边别混。

        public static string ActTakeoff = "act_fly_start";
        public static string ActIdle = "act_fly_idle";
        public static string ActCruise = "act_fly_cruise";
        public static string ActBoost = "act_fly_boost";
        public static string ActLand = "act_fly_land";

        /// <summary>
        /// 抬头爬升时播的姿态。✅ **2026-09-22 已接**：`act_fly_climb` → clip `flight_hovermove_a_pitchu`
        /// （A 套合成件，TRF `fly_A_Flight_HoverMove_A_PitchU`：巡航基准 + 抬头增量，合成时根骨那道量已按 0 缩放）。
        /// ⚠️ 现在接的是**巡航家（直立）**的姿态 —— 冲刺（趴姿）时抬头会从趴姿切到直立，观感是翻一下；
        /// 要"冲刺家也各有一套"得给状态机再拆状态（见 plans/玩家飞行-实施方案.md §3.8.1）。
        /// 留空的后果（旧行为）= 抬头时**保持上一条姿态**，观感是"平板上升"。
        /// </summary>
        public static string ActClimb = "act_fly_climb";

        /// <summary>低头俯冲时播的姿态。✅ **2026-09-22 已接**：`act_fly_dive` → clip `flight_hovermove_a_pitchd`，说明见 <see cref="ActClimb"/>。</summary>
        public static string ActDive = "act_fly_dive";

        /// <summary>进冲刺的**入姿**（一次性，治"按 Shift 硬切到趴姿"）。</summary>
        public static string ActBoostStart = "act_fly_dash_start";

        /// <summary>闪避四方向（一次性；只在冲刺态里播，理由见 <see cref="FlightAnimMachine"/>）。</summary>
        public static string ActDodgeL = "act_fly_dodge_l";
        public static string ActDodgeR = "act_fly_dodge_r";
        public static string ActDodgeU = "act_fly_dodge_u";
        public static string ActDodgeD = "act_fly_dodge_d";

        // ── 压弯 / 俯仰的合成件（2026-09-22：A 套合成件进包后接线）────────────────
        // 这批 clip 都是【合成】出来的（基础动画 ∘ 增量，根骨那道量已按 0 缩放，见方案 §3.8.1），
        // 所以它们跟基础动画同帧号、同循环属性，直接当普通姿态播。

        /// <summary>巡航（直立）压弯左 / 右。触发见 <see cref="BankThreshold"/>。</summary>
        public static string ActLeanL = "act_fly_lean_l";
        public static string ActLeanR = "act_fly_lean_r";

        /// <summary>冲刺（趴姿）压弯左 / 右。</summary>
        public static string ActBoostLeanL = "act_fly_boost_lean_l";
        public static string ActBoostLeanR = "act_fly_boost_lean_r";

        /// <summary>冲刺（趴姿）抬头 / 低头 —— 与巡航家的 <see cref="ActClimb"/> / <see cref="ActDive"/> 分开，
        /// 免得冲刺时从趴姿硬切到直立姿态（那两个家族各有一套抬头/低头）。</summary>
        public static string ActBoostClimb = "act_fly_boost_climb";
        public static string ActBoostDive = "act_fly_boost_dive";

        // ───────────────────── 冲刺入姿 / 闪避（2026-09-22）─────────────────────
        // 🔴 这两组都是**一次性动作**：状态机里 `AnimState.Once(..., next: null, duration: …)`，
        //    时长 = clip 真实长度（帧数 ÷ 30，实测值）。**重导 clip 换了帧数就改这里**。

        /// <summary>冲刺入姿时长（秒）= 31 帧 ÷ 30，实测。</summary>
        public static float BoostStartSeconds = 1.033f;

        /// <summary>闪避动画时长（秒）= 56 帧 ÷ 30，实测。四条一样长。</summary>
        public static float DodgeClipSeconds = 1.867f;

        /// <summary>
        /// 🔴 **闪避的触发方式（2026-09-22 用户裁定）**：**冲刺（按住 Shift）中短按空格 = 闪避**。
        /// 与"悬停 / 巡航中长按空格 = 持续下降"是两套手势，靠**状态 + 长 / 短按**区分。
        /// 关掉本开关 = 回到旧行为（冲刺中短按空格仍走"贴地 / 俯冲落地"那套判定）。
        /// </summary>
        public static bool DodgeOnSpaceTapInBoost = true;

        /// <summary>闪避的位移距离（米）。</summary>
        public static float DodgeDistance = 8f;

        /// <summary>闪避位移走完用多久（秒）—— 之后速度交还普通飞行，姿态动画继续演完。</summary>
        public static float DodgeDisplaceSeconds = 0.4f;

        /// <summary>两次闪避之间的最短间隔（秒）。默认 ≈ 一条闪避动画的长度（演完才能再闪）。</summary>
        public static float DodgeCooldownSeconds = 1.9f;

        // ───────────────────────── 调试 ─────────────────────────

        /// <summary>
        /// 🔴 **飞行 tick 日志总闸（默认关，2026-09-22 用户要求）** —— 管住所有**高频**日志：
        /// `[Flight-Diag]` 三行（每 0.5 秒）、`[Flight] 姿态 →` + 屏幕姿态提示（每次切换）、
        /// `[Flight] air v=…`（每帧，还要再叠 <see cref="VerboseLog"/>）、
        /// 以及共享状态机的 `[Anim:flight] xx → yy`（每次切换）。
        ///
        /// **默认 false**：平时飞完只留每次飞行几行的低频日志（起飞/落地/相机交接，见方案 §3.9 的 B 类），
        /// 出问题要查时再开：<c>custom.flight log on</c>（再加 `full` 连每帧那行也开）。
        ///
        /// ⚠️ **异常路径的日志不受本开关管**（冻结失败 / 载具召唤失败 / 网格不在包里 / tick 异常 /
        /// 状态机抖动自检……）—— 那些是"坏了要能查"的唯一线索，按 §3.9 的 C 类**永远别删、也别关**。
        /// </summary>
        public static bool DebugLog = false;

        /// <summary>
        /// 在总闸之上**再加一档逐帧**日志（默认关，免得刷屏）。
        /// 单开它无效 —— 要配合 <see cref="DebugLog"/>（<c>custom.flight log on full</c>）。
        /// </summary>
        public static bool VerboseLog = false;

        /// <summary>把参数恢复到出厂值。</summary>
        public static void ResetToDefaults()
        {
            CarrierPrefab = "lwn_flight_sigil";
            CarrierTopLocalZ = 0.37f;
            CarrierSpawnGap = 0.06f;
            TakeoffSettleSeconds = 1.0f;
            TakeoffAnimSeconds = 1.5f;
            TakeoffSpawnDelay = 0.2f;
            TakeoffBlendIn = 0f;
            TakeoffSkipSeconds = 0f;
            LandTouchGraceSeconds = 1.0f;
            Freeze = FlightFreezeMode.AiPaused;
            MaxStepPerFrame = 1.5f;
            CruiseSpeed = 9f;
            BoostSpeed = 26f;
            Accel = 20f;
            LandRate = 5f;
            LandAnimSeconds = 2.0f;
            LandMaxSeconds = 5f;
            LandAnimOnGentle = false;
            FallOffDistance = 1.2f;
            HardLandingSpeed = 7.5f;
            LandByTap = true;
            LandTapMaxHeight = 8f;
            LandTapWhileDiving = true;
            LandTapDivePitch = 0.42f;
            LandByLongPressDescend = true;
            DescendRate = 6f;
            LandOnGroundTouch = true;
            LandTouchEps = 0.25f;
            AutoLandSeconds = 0.45f;
            LongPressSeconds = 0.65f;
            TakeoffByDoubleJump = true;
            TakeoffByLongPress = true;
            AnimBlendIn = 0.3f;
            UseFlightCamera = true;
            CamBlendIn = 0.45f;
            UseCamHandover = true;
            CamHandBackLook = true;
            AimOnRightClick = true;
            CamLookSensitivity = 0.12f;
            CamPitchMin = -80f;
            CamPitchMax = 75f;
            InvertCamX = false;
            InvertCamY = false;
            PitchExitThreshold = 0.30f;
            ActionRecheckSeconds = 0.5f;
            ActTakeoff = "act_fly_start";
            ActIdle = "act_fly_idle";
            ActCruise = "act_fly_cruise";
            ActBoost = "act_fly_boost";
            ActLand = "act_fly_land";
            ActClimb = "act_fly_climb";
            ActDive = "act_fly_dive";
            ActBoostStart = "act_fly_dash_start";
            ActDodgeL = "act_fly_dodge_l";
            ActDodgeR = "act_fly_dodge_r";
            ActDodgeU = "act_fly_dodge_u";
            ActDodgeD = "act_fly_dodge_d";
            ActLeanL = "act_fly_lean_l";
            ActLeanR = "act_fly_lean_r";
            ActBoostLeanL = "act_fly_boost_lean_l";
            ActBoostLeanR = "act_fly_boost_lean_r";
            ActBoostClimb = "act_fly_boost_climb";
            ActBoostDive = "act_fly_boost_dive";
            BankThreshold = 0.35f;
            BankExitThreshold = 0.20f;
            ShowStateMessages = true;
            DebugLog = false;
            VerboseLog = false;
            BoostStartSeconds = 1.033f;
            DodgeClipSeconds = 1.867f;
            DodgeOnSpaceTapInBoost = true;
            DodgeDistance = 8f;
            DodgeDisplaceSeconds = 0.4f;
            DodgeCooldownSeconds = 1.9f;
            PitchThreshold = 0.42f;
        }

        /// <summary>给控制台 custom.flight status 用的一行摘要。</summary>
        public static string Describe()
        {
            return string.Format(
                "carrier={0} | cruise={1} boost={2} accel={3} | longPress={4} | freeze={5}",
                CarrierPrefab, CruiseSpeed, BoostSpeed, Accel, LongPressSeconds, Freeze);
        }
    }
}
