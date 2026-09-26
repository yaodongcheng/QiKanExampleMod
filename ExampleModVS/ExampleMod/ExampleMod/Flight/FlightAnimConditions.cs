using System;
using LivingWorldNpcs.Animation;

namespace LivingWorldNpcs.Flight
{
    /// <summary>
    /// 飞行状态机用到的**条件谓词 + 命名标量**（2026-09-25 立）—— 注册给 XML 用。
    ///
    /// 🔴 **这里是这批名字的唯一真身**。`ModuleData/statemachine_flight.xml` 里只写名字
    /// （`when="sprinting"`、`duration="boostStartSeconds"`），名字写错**装载期就报错**、不会静默。
    ///
    /// 🔴 **为什么判据不写进 XML**：写成字符串表达式就得自己写解析器，也失去编译期检查 ——
    /// 那等于给自己再造一种"打错了不报错、只是那条边永远不生效"的静默失败。
    /// 所以边界划在这里：**结构进 XML（改它不用重编译），判据留 C#（改它受编译器保护）**。
    ///
    /// 🔴 **命名纪律（2026-09-25 用户裁定）：条件要读得出"玩家在按什么"** ——
    /// 所以名字一律用**输入语言**（`hold-left` = 按着 A、`look-up` = 镜头朝上、`shift-held` = 按着 Shift），
    /// **不要**用内部量名（曾经叫 `bank-left` / `BankBand &lt; 0`，图上看不出那是什么操作）。
    /// 判据内部**带迟滞**（<see cref="AnimLatch"/>）是为了防抖，不是判据本身 —— 见各条的注释。
    ///
    /// 命名约定：谓词 kebab-case、标量 camelCase。
    /// </summary>
    public static class FlightAnimConditions
    {
        /// <summary>
        /// **相位"时刻"名 —— 起飞（进机入姿）**。
        /// 🔴 相位边的 `when=` 写它；C# 也用它去找"起飞该进哪个状态"
        /// （<c>AgentAnimStateMachine.TryPhaseTarget(TakeoffTrigger)</c>）——
        /// **名字只有这一份**，改这里两边一起改。
        /// ⚠️ 它的真身是 `c => false`：**它不代表时刻**，只代表"起飞那一刻"这个标签；
        ///    真正的触发在 C#（空中按空格）。
        /// </summary>
        public const string TakeoffTrigger = "takeoff-trigger";

        /// <summary>**相位"时刻"名 —— 落地（落地动作）**。真身同样是 `c => false`；触发在 C#（板顶触地）。</summary>
        public const string LandTrigger = "land-trigger";

        /// <summary>把本机用到的名字全部登记进去（`FlightAnimMachine.Register` 里调一次）。</summary>
        public static void RegisterAll()
        {
            // 🔴 **登记表 = 编辑器里"条件"下拉的词汇表**（2026-09-26 用户裁定：只留真用得上的）。
            //
            // ── 输入（键 / 鼠标 ⇒ 用**键原语**写在边上，不用谓词）────────────────────
            //    XML 里直接写 `keys="W+Shift"` / `keys="A" not="true"` —— 组合与取反都能写，
            //    而且每个键装载期就校验（写错 = 整台不注册）。所以这一档只留**键表达不了**的两个：
            AnimConditions.Register("move-input", c => C(c).MoveInput);        // 这一帧按着方向键（含小键盘/死区，不含惯性）

            // ── 镜头俯仰（**键原语表达不了**：它是"相机看向哪"，不是按键）──────────────
            //    判据 = 相机前向的**竖直分量**（带迟滞：进 0.42 ≈ 25°、退 0.30 ≈ 17°，防抖）。
            //    ⚠️ 与机身朝向无关 —— 机身只有水平朝向（引擎转不了俯仰），所以这里就是"相机仰角"。
            //    🔴 **两个家族共用这两个**（2026-09-26）：直立家 / 趴姿家各用**边的来源**区分
            //       （`from="hovermove"` vs `from="fastmove"`），不再需要 `sprint-look-*` 那对
            //       —— 它们已删（同一语义只留一处）。
            AnimConditions.Register("look-up", c => C(c).PitchBand > 0);       // 镜头抬起（爬升姿势用）
            AnimConditions.Register("look-down", c => C(c).PitchBand < 0);     // 镜头压下（俯冲姿势用）

            // 🪦 2026-09-26 删除的谓词（用户裁定：**没用到、且用键原语 / 边来源就能表达**的，一律不留 ——
            //    下拉里混着重复名字容易选错，而且行为有细微差别）：
            //      · `dodge-left/right/up/down` —— 闪避姿态改由 `keys="Space+A"` 这类直接触发
            //      · `shift-held`（= `keys="Shift"`）/ `hold-left`·`hold-right`（= `keys="A"`·`keys="D"`）
            //      · `moving`·`coasting`·`at-rest`（≈ `move-input` / `move-input not="true"`，
            //        差别只在"松手后还在滑行"那一小段）
            //      · `sprinting`（≈ `keys="W+Shift"`）/ `sprint-lean-left`·`sprint-lean-right`
            //        （= `keys="W+A"`·`keys="W+D"`）
            //      · `sprint-look-up`·`sprint-look-down` —— "在冲刺"由**边的来源**表达（`from="fastmove"`）⇒ 冗余
            //      · `casting-fallback` —— 死代码（它读的 magic 状态早已删除）
            //    连带留下但**当前没人读**的事实：`FlightAnimContext.BankBand`（压弯档，喂 `_bankLatch`）
            //    与 `SpellCharging`；`custom.flight tune bank / bankout` 也跟着成了空转。
            //    要恢复某个谓词 = 在这里加一行 + 边上写它的名字。

            // ── 相位触发的边专用的两名（**只是"时刻"标签**）────────────────────────
            // 🔴 起飞/落地那条边由飞行相位（C#）在特定时刻 `Force` 进，**状态机不求值** ——
            //    所以这两个谓词永远返回 false，登记它们只是为了让 XML 里的名字也能过校验。
            //    🔴 **但名字本身是有用的**：C# 按它去找"这个时刻该进哪个状态"（见上面两个常量）。
            //    真值时刻在 PlayerFlightBehavior：起飞 = 空中按空格 / 长按空格；
            //    落地 = **板顶触地**（硬着陆才播落地动作；轻放不播）。
            AnimConditions.Register(TakeoffTrigger, c => false);
            AnimConditions.Register(LandTrigger, c => false);

            // ── 命名标量（一次性动作的时长；重导 clip 换了帧数就改 FlightTuning 那边的值）──
            AnimConditions.RegisterParam("boostStartSeconds", () => FlightTuning.BoostStartSeconds);
            AnimConditions.RegisterParam("dodgeClipSeconds", () => FlightTuning.DodgeClipSeconds);
            AnimConditions.RegisterParam("castReleaseSeconds", () => FlightTuning.CastReleaseSeconds);
        }

        /// <summary>把上下文收窄成飞行那份（定义与上下文同命名空间，收窄在这里是安全的）。</summary>
        private static FlightAnimContext C(AnimContext c) => (FlightAnimContext)c;
    }
}
