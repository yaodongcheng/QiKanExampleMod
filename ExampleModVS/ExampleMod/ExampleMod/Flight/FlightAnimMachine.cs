using LivingWorldNpcs.Animation;

namespace LivingWorldNpcs.Flight
{
    /// <summary>
    /// 飞行动画状态机要读的**事实**（转移条件只看这里）。
    ///
    /// 由 <see cref="PlayerFlightBehavior"/> 每帧填、填完才 Tick 状态机。
    /// </summary>
    public sealed class FlightAnimContext : AnimContext
    {
        /// <summary>有方向输入（或还在惯性滑行）—— 决定"待机 ↔ 巡航"。</summary>
        public bool Moving;

        /// <summary>按着冲刺（左 Shift）。</summary>
        public bool Boost;

        /// <summary>俯仰档（带迟滞）：+1 抬头 / 0 水平 / −1 低头。</summary>
        public int PitchBand;
    }

    /// <summary>
    /// **飞行动画状态机的定义**（状态表 + 转移表）—— 注册制：这里是唯一事实源，
    /// 运行时由 <see cref="AnimMachineRegistry.Create"/> 按名字建实例。
    ///
    /// 🔴 改飞行"什么时候播哪条动画"**只改这个文件**。加一个新姿态 =
    ///    ① 这里加一行状态；② 需要的话加一行边；③ 内容包接线（`action_types.xml` / `action_sets.xml`）。
    ///
    /// 注册时机：`MySubModule.OnSubModuleLoad`（模块加载时一次）。
    /// </summary>
    public static class FlightAnimMachine
    {
        /// <summary>注册名（<see cref="AnimMachineRegistry.Create"/> 用它取）。</summary>
        public const string Name = "flight";

        /// <summary>注册进注册表（幂等：重名会覆盖，方便热改）。</summary>
        public static void Register()
        {
            AnimMachineRegistry.Register(Build());
        }

        private static AnimMachineDef Build()
        {
            var def = new AnimMachineDef(Name)
            {
                // 过渡与核对周期**指向 FlightTuning** ⇒ 现有热调键（blend / ActionRecheckSeconds）继续生效
                DefaultBlend = () => FlightTuning.AnimBlendIn,
                RecheckSeconds = () => FlightTuning.ActionRecheckSeconds,
            };

            // ── 状态：一条 = 引擎里的一条动作（`act_xxx`）────────────────────────────
            def.Add(AnimState.Loop("idle", FlightTuning.ActIdle));       // 悬停待机
            def.Add(AnimState.Loop("cruise", FlightTuning.ActCruise));   // 巡航
            def.Add(AnimState.Loop("boost", FlightTuning.ActBoost));     // 冲刺（趴姿）
            def.Add(AnimState.Loop("climb", FlightTuning.ActClimb));     // 抬头 —— 动作名现在是空串 ⇒ 未接，自动跳过
            def.Add(AnimState.Loop("dive", FlightTuning.ActDive));       // 低头 —— 同上
            def.Add(AnimState.Loop("takeoff", FlightTuning.ActTakeoff)); // 起飞入姿（只由相位 Force 进）
            def.Add(AnimState.Loop("land", FlightTuning.ActLand));       // 落地（只由相位 Force 进）

            // 以后接一次性动作就照这个写（状态带 next + 时长，播完自动去 next）：
            //   def.Add(AnimState.Once("dashStart", FlightTuning.ActBoostStart, next: "boost", duration: 1.0f));
            //   def.Edge("boost", "dashStart", c => ((FlightAnimContext)c).BoostJustPressed, blend: 0.15f);

            // ── 转移：**顺序 = 优先级**（写在上面先判；第一条命中的生效）─────────────
            //
            // 与早先 PickAirAction 的分支顺序逐条对应：
            //   ① 没输入 → 待机   ② 冲刺优先   ③ 俯仰档（带迟滞）→ 爬升/俯冲   ④ 其余 → 巡航
            //
            // 🔴 `"*"` = 任意状态。起飞/落地那两段靠行为里的 `Hold = true` 挂起自动转移
            //    （相位自己 Force），所以这里不用为它们写例外。
            def.Edge("*", "idle", c => !C(c).Moving);
            def.Edge("*", "boost", c => C(c).Boost);
            def.Edge("*", "climb", c => C(c).PitchBand > 0);
            def.Edge("*", "dive", c => C(c).PitchBand < 0);
            def.Edge("*", "cruise", c => C(c).Moving);

            return def;
        }

        /// <summary>把上下文收窄成飞行那份（定义与上下文同文件，收窄在这里是安全的）。</summary>
        private static FlightAnimContext C(AnimContext c) => (FlightAnimContext)c;
    }
}
