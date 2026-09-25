namespace LivingWorldNpcs.Animation
{
    /// <summary>
    /// **动画状态机的调试开关**（2026-09-25 从 `FlightTuning` 搬进来 —— 用户指出：它属于通用件）。
    ///
    /// 🔴 **为什么放在 `Animation/` 而不是各系统的 Tuning 里**：状态机是**通用件**，
    ///    将来 NPC 飞行 / 坐骑 / 别的运动系统都会用它 —— 开关挂在某个使用方身上，
    ///    等于"只有那一个系统能调"，换个系统就得再抄一份、还会出现两套互相打架的开关。
    ///
    /// 三档（控制台 <c>custom.anim_log off|on|full</c>，**默认 off**）：
    ///   · **off（默认）** = 全关。平时飞完日志里只有起飞/落地那几行（见方案 §3.9 的 B 类）。
    ///   · **on** = **拐点日志** —— 状态切换一行 + blend 走完回读确认一行。
    ///     只打"事件"不打"采样"（沿触发 / 状态真变了 / 每状态确认一次）⇒ 一次运行几十行，开了可以一直留着。
    ///   · **full** = 再加**高频诊断**（"被抢走重设"、逐帧那些）—— 排查疑难时才开。
    ///   🔴 **`✗ 被抢走` 那条无论如何都打** —— 异常证据不受任何开关管。
    ///
    /// 🔴 **默认为什么是关**（2026-09-25 用户裁定，当天从 on 翻过来）：这套日志是**排查工具**，
    ///    平时不需要占日志行；要看的时候 `custom.anim_log on` 敲一下就有（不带参数 = 查当前档位）。
    ///
    /// 🔴 **批量使用（几十上百个 agent 都跑状态机）时别关这里** —— 见
    ///    <see cref="AgentAnimStateMachine.TraceEnabled"/>：全局开关是"我要查"，
    ///    实例开关是"这个 agent 值不值得记"，两个问题分开回答。
    /// </summary>
    public static class AnimDebug
    {
        /// <summary>拐点日志（状态切换 / blend 后生效确认 / 输入沿）。**默认关**，调试时 `custom.anim_log on`。</summary>
        public static bool Trace = false;

        /// <summary>高频诊断日志（被抢走重设、逐帧）。**默认关** —— 那个量级会刷屏。</summary>
        public static bool Verbose = false;

        /// <summary>一行摘要（控制台回显用；**纯英文** —— 游戏内控制台的返回文本纪律）。</summary>
        public static string Describe()
        {
            return "trace=" + (Trace ? "on" : "off") + " verbose=" + (Verbose ? "on" : "off");
        }
    }
}
