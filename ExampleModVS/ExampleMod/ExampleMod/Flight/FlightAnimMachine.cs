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
    /// </summary>
    public sealed class FlightAnimContext : AnimContext
    {
        /// <summary>有方向输入（或还在惯性滑行）—— 决定"待机 ↔ 巡航"。</summary>
        public bool Moving;

        /// <summary>按着冲刺（左 Shift）。</summary>
        public bool Boost;

        /// <summary>刚按下冲刺键 —— **按下沿，只活一帧**，用来进"冲刺入姿"那一段一次性动画。</summary>
        public bool BoostJustPressed;

        /// <summary>这一帧请求的闪避方向（<see cref="FlightDodgeDir.None"/> = 没请求）。</summary>
        public FlightDodgeDir DodgeRequest;

        /// <summary>俯仰档（带迟滞）：+1 抬头 / 0 水平 / −1 低头。</summary>
        public int PitchBand;

        /// <summary>压弯档（带迟滞）：**+1 = 按 D（右移）→ 右压 / −1 = 按 A（左移）→ 左压** / 0 = 不压。</summary>
        public int BankBand;
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
            def.Add(AnimState.Loop("climb", FlightTuning.ActClimb));     // 抬头（巡航家·直立姿态）
            def.Add(AnimState.Loop("dive", FlightTuning.ActDive));       // 低头（同上）
            def.Add(AnimState.Loop("takeoff", FlightTuning.ActTakeoff)); // 起飞入姿（只由相位 Force 进）
            def.Add(AnimState.Loop("land", FlightTuning.ActLand));       // 落地（只由相位 Force 进）

            // ── 压弯 / 俯仰（2026-09-22 接 A 套合成件）─────────────────────────
            // 🔴 两个家族各一套：**巡航家是直立姿势、冲刺家是趴姿**，混用 = 硬翻 90°（实测过）。
            def.Add(AnimState.Loop("leanL", FlightTuning.ActLeanL));                 // 巡航·左压
            def.Add(AnimState.Loop("leanR", FlightTuning.ActLeanR));                 // 巡航·右压
            def.Add(AnimState.Loop("boostLeanL", FlightTuning.ActBoostLeanL));       // 冲刺·左压
            def.Add(AnimState.Loop("boostLeanR", FlightTuning.ActBoostLeanR));       // 冲刺·右压
            def.Add(AnimState.Loop("boostClimb", FlightTuning.ActBoostClimb));       // 冲刺·抬头
            def.Add(AnimState.Loop("boostDive", FlightTuning.ActBoostDive));         // 冲刺·低头

            // ── 一次性动作（`next: null` = 播完留在原地，让普通转移接管）─────────────
            //
            // 🔴 为什么 `next` 填 null 而不是"回到 boost"：演完那一刻玩家可能早松开 Shift 了，
            //    写死回 boost 会先切过去、下一帧又被普通转移踢走 = 连着两次交叉淡化（看着闪一下）。
            //    留 null 就是"演完让表来判"，一步到位。
            def.Add(AnimState.Once("dashStart", FlightTuning.ActBoostStart, next: null,
                                   duration: FlightTuning.BoostStartSeconds));   // 进冲刺的入姿（治硬切）
            def.Add(AnimState.Once("dodgeL", FlightTuning.ActDodgeL, next: null,
                                   duration: FlightTuning.DodgeClipSeconds));    // 闪避（四方向）
            def.Add(AnimState.Once("dodgeR", FlightTuning.ActDodgeR, next: null,
                                   duration: FlightTuning.DodgeClipSeconds));
            def.Add(AnimState.Once("dodgeU", FlightTuning.ActDodgeU, next: null,
                                   duration: FlightTuning.DodgeClipSeconds));
            def.Add(AnimState.Once("dodgeD", FlightTuning.ActDodgeD, next: null,
                                   duration: FlightTuning.DodgeClipSeconds));

            // ── 转移：**顺序 = 优先级**（写在上面先判；第一条命中的生效）─────────────
            //
            //   ① 闪避 ② 冲刺入姿 ③ 冲刺家（趴姿）的压弯/俯仰 ④ 巡航家的压弯
            //   ⑤ 待机 ⑥ 冲刺 ⑦ 巡航家俯仰 ⑧ 巡航
            //
            // 🔴 闪避的边**只从冲刺态出发**，不是 `"*"` —— 实测那 4 条闪避动画
            //    的基准姿势就是**趴姿**（身体轴 前−0.98/上+0.22，与 `FastMove_A` 一致），
            //    从悬停/巡航（直立，上+1.00）切过去会硬翻 ~90°。这也正是"闪避只在冲刺中用"的由来。
            //    （2026-09-22 起来源扩到冲刺家的全部状态：压弯/抬头/低头都算冲刺态。）
            //
            // 🔴 起飞/落地那两段靠行为里的 `Hold = true` 挂起自动转移（相位自己 Force），
            //    所以这里不用为它们写例外。
            def.Edge(new[] { "boost", "dashStart", "boostLeanL", "boostLeanR", "boostClimb", "boostDive" },
                     "dodgeL", c => C(c).DodgeRequest == FlightDodgeDir.Left, blend: 0.12f);
            def.Edge(new[] { "boost", "dashStart", "boostLeanL", "boostLeanR", "boostClimb", "boostDive" },
                     "dodgeR", c => C(c).DodgeRequest == FlightDodgeDir.Right, blend: 0.12f);
            def.Edge(new[] { "boost", "dashStart", "boostLeanL", "boostLeanR", "boostClimb", "boostDive" },
                     "dodgeU", c => C(c).DodgeRequest == FlightDodgeDir.Up, blend: 0.12f);
            def.Edge(new[] { "boost", "dashStart", "boostLeanL", "boostLeanR", "boostClimb", "boostDive" },
                     "dodgeD", c => C(c).DodgeRequest == FlightDodgeDir.Down, blend: 0.12f);
            def.Edge("*", "dashStart", c => C(c).BoostJustPressed && C(c).Moving);
            // ③ 冲刺家（趴姿）：压弯优先于俯仰（两个同时成立时先出压弯）
            //    ⚠️ 符号口径：BankBand = −1 是**按 A（左移）** ⇒ 出 leanL；+1 是按 D ⇒ 出 leanR。
            //       左右接反了就交换下面两行（真机一眼能看出来）。
            //    🔴 `blend: 0.15` 与全局默认 0.3 不同 —— 压弯/俯仰是**小姿态**（实测颈/肩 ~20~40°），
            //       0.3 秒的交叉淡化会吃掉大半停留时间（日志实测：一次压弯只停 0.6~1.1 秒），
            //       姿势刚到极值就切回去了。家族之间的大翻转（巡航↔冲刺 120°+）仍走默认 0.3，别一起改。
            def.Edge("*", "boostLeanL", c => C(c).Boost && C(c).BankBand < 0, blend: 0.15f);
            def.Edge("*", "boostLeanR", c => C(c).Boost && C(c).BankBand > 0, blend: 0.15f);
            def.Edge("*", "boostClimb", c => C(c).Boost && C(c).PitchBand > 0, blend: 0.15f);
            def.Edge("*", "boostDive", c => C(c).Boost && C(c).PitchBand < 0, blend: 0.15f);
            // ④ 巡航家（直立）：压弯
            def.Edge("*", "leanL", c => C(c).BankBand < 0, blend: 0.15f);
            def.Edge("*", "leanR", c => C(c).BankBand > 0, blend: 0.15f);
            // ⑤~⑧ 原有阶梯
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
