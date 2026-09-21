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
        public static string CarrierPrefab = "wooden_platform_a";

        /// <summary>
        /// 是否隐藏载具网格。
        ///
        /// 🔴 默认 **false = 显示**。理由：板要是隐形的，**就没法用肉眼确认它真的生成了、真的在托人** ——
        ///    "飞起来了"和"看见一块板抬着自己飞"是两回事，前者可能是别的原因造成的错觉。
        ///    阶段 1 一律让它显示；等法阵进了包、要出货了，再用 <c>custom.flight hide on</c> 关掉。
        /// </summary>
        public static bool HideCarrier = false;

        /// <summary>wooden_platform_a 的顶面在局部坐标里的高度（实测 0.37 米）。</summary>
        public static float CarrierTopLocalZ = 0.37f;

        /// <summary>
        /// 生成时把板往下压多少，让顶面正好落在脚底。
        /// 多压一点（而不是刚好）是留冗余：板略低于脚底照样托得住，略高则会把人顶起来。
        /// </summary>
        public static float CarrierFeetOffset = 0.45f;

        // ───────────────────────── 法阵（纯视觉）─────────────────────────

        /// <summary>
        /// 脚下法阵的预制体名（内容包提供）。
        /// 🔴 资产还没做好时这里查不到 —— 代码会静默跳过（只飞、无法阵），不影响飞行本身。
        /// </summary>
        public static string SigilPrefab = "lwn_flight_sigil";

        /// <summary>
        /// 预制体里那个 <c>&lt;meta_mesh_component name="…"&gt;</c> 引用的**网格名**。
        /// 代码在实例化预制体之前会先用它做存在性探针 ——
        /// 🔴 网格不存在却去 Instantiate = native 访问违例，游戏当场崩，try/catch 拦不住（2026-09-21 实测）。
        /// 导完法阵后如果 ModKit 给的名字不是这个，改这里（或 custom.flight tune 里加）。
        /// </summary>
        public static string SigilMeshName = "lwn_flight_sigil";

        /// <summary>
        /// 法阵相对「板原点」抬高多少。
        /// 🔴 必须**高于板顶面**（<see cref="CarrierTopLocalZ"/> ≈ 0.37）—— 板原点在板**底面**，
        ///    抬 0.02 的话法阵会埋在板体内部，等于看不见。抬到板顶面之上 = 正好在玩家脚下。
        /// </summary>
        public static float SigilLiftZ = 0.40f;

        // ───────────────────────── 高度 ─────────────────────────

        /// <summary>起飞后相对「起飞点地面」的悬停高度。</summary>
        public static float HoverAltitude = 6f;

        /// <summary>飞行中离地形的最小间隙。低于它就把板顶回去，防止钻进山体。</summary>
        public static float MinClearance = 1.2f;

        /// <summary>高度上限（防越界；实测从 169 米跳下后坐标会归零）。</summary>
        public static float MaxAltitude = 160f;

        /// <summary>抬升 / 下降速率（米/秒），只在起飞与降落阶段用。</summary>
        public static float VerticalRate = 7f;

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

        /// <summary>停在最低点多久自动落地（秒）。玩家一直往下压 = 想下来。</summary>
        public static float AutoLandSeconds = 0.45f;

        // ───────────────────────── 长按 ─────────────────────────

        /// <summary>长按空格多久算触发（秒）。</summary>
        public static float LongPressSeconds = 0.65f;

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

        // ───────────────────────── 动画 ─────────────────────────

        /// <summary>
        /// 动作切换的交叉淡化时长（秒）。这就是骑砍2 里能做的"混合空间"。
        /// 取 0.3 而不是 0.2：实测姿态之间是 **120°~180° 的大翻转**（参见巡航↔趴姿↔头朝下），
        /// 0.2 秒翻 180° 看着像猛地翻跟头。
        /// </summary>
        public static float AnimBlendIn = 0.3f;

        /// <summary>
        /// 每隔多久**核对一次**动画有没有被引擎抢回去（秒）。
        /// 为什么需要：0 号动作通道是引擎 locomotion 系统也有权写的，我们设完不保证一直有效。
        /// 为什么不能每帧核对：每帧重设 <c>SetActionChannel</c> 会把动画**卡在第 0 帧**。
        /// </summary>
        public static float ActionRecheckSeconds = 0.5f;

        // ───────────────────────── 动作名 ─────────────────────────
        // 🔴 这些是「动作名」，不是 clip 名 —— clip 名在内容包的 action_sets.xml 里绑，两边别混。

        public static string ActTakeoff = "act_fly_start";
        public static string ActIdle = "act_fly_idle";
        public static string ActCruise = "act_fly_cruise";
        public static string ActBoost = "act_fly_boost";
        public static string ActLand = "act_fly_land";

        /// <summary>抬头爬升时播的姿态（upright 上升）。</summary>
        public static string ActClimb = "act_fly_climb";

        /// <summary>低头俯冲时播的姿态（头朝下）。</summary>
        public static string ActDive = "act_fly_dive";

        // ───────────────────────── 调试 ─────────────────────────

        /// <summary>开一次性的逐帧诊断日志（默认关，免得刷屏）。</summary>
        public static bool VerboseLog = false;

        /// <summary>把参数恢复到出厂值。</summary>
        public static void ResetToDefaults()
        {
            CarrierPrefab = "wooden_platform_a";
            CarrierTopLocalZ = 0.37f;
            CarrierFeetOffset = 0.45f;
            SigilPrefab = "lwn_flight_sigil";
            SigilMeshName = "lwn_flight_sigil";
            SigilLiftZ = 0.40f;
            HoverAltitude = 6f;
            MinClearance = 1.2f;
            MaxAltitude = 160f;
            VerticalRate = 7f;
            Freeze = FlightFreezeMode.AiPaused;
            MaxStepPerFrame = 1.5f;
            CruiseSpeed = 9f;
            BoostSpeed = 26f;
            Accel = 20f;
            LandRate = 5f;
            AutoLandSeconds = 0.45f;
            LongPressSeconds = 0.65f;
            AnimBlendIn = 0.3f;
            PitchExitThreshold = 0.30f;
            ActionRecheckSeconds = 0.5f;
            ActTakeoff = "act_fly_start";
            ActIdle = "act_fly_idle";
            ActCruise = "act_fly_cruise";
            ActBoost = "act_fly_boost";
            ActLand = "act_fly_land";
            ActClimb = "act_fly_climb";
            ActDive = "act_fly_dive";
            PitchThreshold = 0.42f;
        }

        /// <summary>给控制台 custom.flight status 用的一行摘要。</summary>
        public static string Describe()
        {
            return string.Format(
                "carrier={0} sigil={1} | cruise={2} boost={3} accel={4} | hover={5} clearance={6} maxAlt={7} vrate={8} | longPress={9} | freeze={10}",
                CarrierPrefab, SigilPrefab, CruiseSpeed, BoostSpeed, Accel,
                HoverAltitude, MinClearance, MaxAltitude, VerticalRate, LongPressSeconds,
                Freeze);
        }
    }
}
