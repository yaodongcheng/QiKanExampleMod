using System;
using System.IO;
using System.Reflection;
using LivingWorldNpcs.Animation;

namespace LivingWorldNpcs.Flight
{
    /// <summary>闪避方向（一次请求 = 一个方向；<see cref="None"/> = 没请求）。</summary>
    public enum FlightDodgeDir
    {
        None,
        Left,
        Right,
        Up,
        Down,
    }

    /// <summary>
    /// 飞行动画状态机要读的**事实**（转移条件只看这里）。
    ///
    /// 由 <see cref="PlayerFlightBehavior"/> 每帧填、填完才 Tick 状态机。
    /// 🔴 **每个字段的算法见 `PlayerFlightBehavior.UpdateAnimContext`**；条件那边只读、不算。
    /// </summary>
    public sealed class FlightAnimContext : AnimContext, IAnimInputFacts
    {
        // ── IAnimInputFacts 的载体（每帧由 PlayerFlightBehavior 填；下标顺序 = AnimPrimitives.Keys）──
        private readonly bool[] _keyHeld = new bool[AnimPrimitives.KeyCount];

        /// <summary>当前动画还剩多少（**占 clip 的比例** 0~1；循环状态 = +∞ ⇒ "剩余 &lt; X%" 永不成立）。</summary>
        public float AnimRemainFrac = float.PositiveInfinity;

        /// <summary>填一个键的**电平**（按住 / 没按住；"沿"已删，见 <see cref="IAnimInputFacts"/>）。</summary>
        internal void SetKey(int i, bool held) => _keyHeld[i] = held;

        bool IAnimInputFacts.KeyHeld(int i) => _keyHeld[i];
        float IAnimInputFacts.AnimRemainFrac => AnimRemainFrac;

        /// <summary>有方向输入（或还在惯性滑行）—— 决定"待机 ↔ 悬停移动"。= <see cref="MoveInput"/> || 速度&gt;1。</summary>
        public bool Moving;

        /// <summary>**这一帧真的按着方向键**（原始输入，不含惯性滑行）—— 想让条件只读输入时用它。</summary>
        public bool MoveInput;

        /// <summary>按着冲刺键（左 Shift）。</summary>
        public bool Boost;

        /// <summary>这一帧请求的闪避方向（<see cref="FlightDodgeDir.None"/> = 没请求）。</summary>
        public FlightDodgeDir DodgeRequest;

        /// <summary>俯仰档（带迟滞）：+1 抬头 / 0 水平 / −1 低头。</summary>
        public int PitchBand;

        /// <summary>压弯档（带迟滞）：**+1 = 按 D（右移）→ 右压 / −1 = 按 A（左移）→ 左压** / 0 = 不压。</summary>
        public int BankBand;

        /// <summary>
        /// **正在蓄力施法**（按住右键）—— 决定"进施法手势状态"。
        /// 🔴 来源 = <c>SpellCastInput.IsPlayerAiming</c>（施法相位机是不是非 Idle）。
        /// 释放那一下**不走这条**（一次性动作由行为 <c>Force</c> 进，与起飞/落地同一套路）。
        /// </summary>
        public bool SpellCharging;
    }

    /// <summary>
    /// **飞行动画状态机**（2026-09-22 立，2026-09-25 定义迁到 XML）。
    ///
    /// 🔴🔴 **"什么时候播哪条动画"的规则不在代码里了** —— 在
    /// [`ModuleData/statemachine_flight.xml`](../../../ModuleData/statemachine_flight.xml)：
    /// 状态表（状态名 / 动作名 / 循环性 / 时长）+ 族名单 + 转移表（来源 / 目标 / 条件名 / 优先级）。
    /// **改那个文件不用重编译**，重启游戏即生效。
    ///
    /// 本文件只剩三件事：
    ///   ① 注册**条件谓词与命名标量**（`FlightAnimConditions.RegisterAll()`）—— 判据本体留 C#，
    ///      XML 里只写谓词名（理由见 <see cref="AnimConditions"/> 的说明）；
    ///   ② 从 XML **装载 + 校验**（<see cref="AnimMachineLoader"/>），校验不过**不注册**；
    ///   ③ 挂上**运行期旋钮**（默认过渡时长 / 核对周期 / 动作优先级 → 仍指向 `FlightTuning` 的字段，
    ///      所以 `custom.flight tune blend` 那些热调键照旧有效）。
    ///
    /// 命名规则（**贯穿三层，改哪层都照这个来**）：**状态名 = 动作名 = clip 名**，去掉 `act_fly_` /
    /// `flight_` 前缀与 `_a` 后缀就是同一个词：
    ///   `hovermove` ↔ `act_fly_hovermove` ↔ `flight_hovermove_a`（悬停移动 = 直立巡航）
    ///   `fastmove`  ↔ `act_fly_fastmove`  ↔ `flight_fastmove_a` （快移 = 趴姿冲刺）
    ///
    /// 注册时机：`MySubModule.OnSubModuleLoad`。
    /// </summary>
    public static class FlightAnimMachine
    {
        /// <summary>注册名（<see cref="AnimMachineRegistry.Create"/> 用它取）。</summary>
        public const string Name = "flight";

        /// <summary>定义文件名（在模块的 `ModuleData/` 下）。</summary>
        public const string FileName = "statemachine_flight.xml";

        /// <summary>注册进注册表（幂等：重名会覆盖，方便热改）。</summary>
        public static void Register()
        {
            // ① 判据（C#）：XML 里的 `when=` / `duration=` 只能引用这里登记过的名字
            FlightAnimConditions.RegisterAll();

            // ② 结构（XML）：装载 + 校验；校验不过就不注册（状态机退回"空机器"：
            //    动画不播、飞行照常 —— 而不是带着半张错表跑）
            string path = Path.Combine(ModuleRoot, "ModuleData", FileName);
            AnimMachineDef def = AnimMachineLoader.Load(path, out string error);
            if (def == null)
            {
                DebugLogger.Log("[Flight] 状态机未注册（定义有问题，见上面的 [Anim] 报错）—— "
                                + "飞行本身照常，只是没有姿态动画");
                return;
            }

            // ③ 运行期旋钮（不属结构，故不进 XML）
            def.DefaultBlend = () => FlightTuning.AnimBlendIn;
            def.RecheckSeconds = () => FlightTuning.ActionRecheckSeconds;
            def.ActionPriority = () => FlightTuning.FlightActionPriority;

            AnimMachineRegistry.Register(def);
        }

        /// <summary>模块根目录（从 DLL 位置反推：`bin/Win64_Shipping_Client/xxx.dll` → 上两级）。</summary>
        private static string ModuleRoot
        {
            get
            {
                try
                {
                    string dll = Assembly.GetExecutingAssembly().Location;
                    return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(dll), "..", ".."));
                }
                catch
                {
                    return ".";
                }
            }
        }
    }
}
