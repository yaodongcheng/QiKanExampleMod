using TaleWorlds.Library;

namespace LivingWorldNpcs.Flight
{
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
            SigilLiftZ = 0.40f;
            HoverAltitude = 6f;
            MinClearance = 1.2f;
            MaxAltitude = 160f;
            VerticalRate = 7f;
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
                "carrier={0} sigil={1} | cruise={2} boost={3} accel={4} | hover={5} clearance={6} maxAlt={7} vrate={8} | longPress={9}",
                CarrierPrefab, SigilPrefab, CruiseSpeed, BoostSpeed, Accel,
                HoverAltitude, MinClearance, MaxAltitude, VerticalRate, LongPressSeconds);
        }
    }
}
