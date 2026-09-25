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
        /// <summary>把本机用到的名字全部登记进去（`FlightAnimMachine.Register` 里调一次）。</summary>
        public static void RegisterAll()
        {
            // ── 输入（按键 / 鼠标）──────────────────────────────────────────────
            AnimConditions.Register("move-input", c => C(c).MoveInput);        // 这一帧按着方向键（不含惯性）
            AnimConditions.Register("shift-held", c => C(c).Boost);            // 按着左 Shift
            AnimConditions.Register("hold-left", c => C(c).BankBand < 0);      // 按着 A（带迟滞 0.35 / 0.20，防抖）
            AnimConditions.Register("hold-right", c => C(c).BankBand > 0);     // 按着 D（同上）
            AnimConditions.Register("look-up", c => C(c).PitchBand > 0);       // 鼠标把镜头抬起来（迟滞 0.42 / 0.30）
            AnimConditions.Register("look-down", c => C(c).PitchBand < 0);     // 鼠标把镜头压下去（同上）

            // ── 运动（输入 + 惯性）──────────────────────────────────────────────
            AnimConditions.Register("moving", c => C(c).Moving);               // = 按着方向键 或 还在滑行
            AnimConditions.Register("coasting", c => C(c).Moving && !C(c).MoveInput);  // 松了键但还没停（惯性）
            AnimConditions.Register("at-rest", c => !C(c).Moving);             // 停住了
            AnimConditions.Register("sprinting", c => C(c).Boost && C(c).Moving);      // 按着 Shift 且在飞

            // ── 冲刺中的姿态（快移家那四条）──────────────────────────────────────
            // 🔴 **必须带 Boost**：不带的话"按着 Shift 站着不动"也会摆趴姿，与 at-rest（直立待机）互抢 ——
            //    实机踩过（日志 09:52:12.185 的 `fastmove → idle`）。
            AnimConditions.Register("sprint-lean-left", c => C(c).Boost && C(c).BankBand < 0);
            AnimConditions.Register("sprint-lean-right", c => C(c).Boost && C(c).BankBand > 0);
            AnimConditions.Register("sprint-look-up", c => C(c).Boost && C(c).PitchBand > 0);
            AnimConditions.Register("sprint-look-down", c => C(c).Boost && C(c).PitchBand < 0);

            // ── 施法与闪避 ────────────────────────────────────────────────────
            AnimConditions.Register("casting-fallback",                          // 回退档专用：默认走通道 1 手势，这条不成立
                c => C(c).SpellCharging && !FlightTuning.CastOnUpperChannel);
            AnimConditions.Register("dodge-left", c => C(c).DodgeRequest == FlightDodgeDir.Left);    // 冲刺中短按空格
            AnimConditions.Register("dodge-right", c => C(c).DodgeRequest == FlightDodgeDir.Right);
            AnimConditions.Register("dodge-up", c => C(c).DodgeRequest == FlightDodgeDir.Up);
            AnimConditions.Register("dodge-down", c => C(c).DodgeRequest == FlightDodgeDir.Down);

            // ── 相位触发的边专用的两名（**只是文档**）────────────────────────────
            // 🔴 起飞/落地那条边由飞行相位（C#）在特定时刻 `Force` 进，**状态机不求值** ——
            //    所以这两个谓词永远返回 false，登记它们只是为了让 XML 里的名字也能过校验。
            //    真身在 PlayerFlightBehavior：起飞 = 跳跃中按空格 / 长按空格；落地 = 短按贴地·俯冲 / 长按下降 / 撞地。
            AnimConditions.Register("takeoff-trigger", c => false);
            AnimConditions.Register("land-trigger", c => false);

            // ── 命名标量（一次性动作的时长；重导 clip 换了帧数就改 FlightTuning 那边的值）──
            AnimConditions.RegisterParam("boostStartSeconds", () => FlightTuning.BoostStartSeconds);
            AnimConditions.RegisterParam("dodgeClipSeconds", () => FlightTuning.DodgeClipSeconds);
            AnimConditions.RegisterParam("castReleaseSeconds", () => FlightTuning.CastReleaseSeconds);
        }

        /// <summary>把上下文收窄成飞行那份（定义与上下文同命名空间，收窄在这里是安全的）。</summary>
        private static FlightAnimContext C(AnimContext c) => (FlightAnimContext)c;
    }
}
