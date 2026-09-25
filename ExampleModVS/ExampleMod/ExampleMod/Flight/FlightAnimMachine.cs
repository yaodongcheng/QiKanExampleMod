using System;
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
        /// <summary>有方向输入（或还在惯性滑行）—— 决定"待机 ↔ 悬停移动"。</summary>
        public bool Moving;

        /// <summary>按着冲刺键（左 Shift）。</summary>
        public bool Boost;

        // 🪦 2026-09-25 删除 `BoostJustPressed`（冲刺键按下沿）—— 入姿边不再用按下沿了。
        //    原因见下面 Build() 里"③ 进快移家一律先播入姿"那段：按下沿只活一帧，
        //    会在"没推方向键 / 起飞落地 Hold 期间 / 施法让位期间"被吞掉 ⇒ 从直立硬翻到趴姿。
        //    现在入姿的判据 = **来源是直立家 + Shift 按着**，不需要按下沿这个事实了。

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
    /// **飞行动画状态机的定义**（状态表 + 转移表）—— 注册制：这里是唯一事实源，
    /// 运行时由 <see cref="AnimMachineRegistry.Create"/> 按名字建实例。
    ///
    /// 🔴 改飞行"什么时候播哪条动画"**只改这个文件**。加一个新姿态 =
    ///    ① 这里加一行状态；② 需要的话加一行边；③ 内容包接线（`action_types.xml` / `action_sets.xml`）。
    ///
    /// 🔴🔴 **命名规则（2026-09-25 用户裁定）：状态名一律用「动画自己的词」** ——
    ///    一眼就能对上 ModKit 里的 clip，不用在脑子里做翻译。全链路同一个词：
    ///
    ///    | 状态名 | 动作名 | 装机包里的 clip |
    ///    |---|---|---|
    ///    | `idle` | `act_fly_idle` | `flight_idle_a` |
    ///    | `hovermove` | `act_fly_hovermove` | `flight_hovermove_a` |
    ///    | `fastmove` | `act_fly_fastmove` | `flight_fastmove_a` |
    ///    | `hovermoveLeanL` / `hovermoveLeanR` | `act_fly_hovermove_lean_l/_r` | `flight_hovermove_a_leanl/_leanr` |
    ///    | `fastmoveLeanL` / `fastmoveLeanR` | `act_fly_fastmove_lean_l/_r` | `flight_fastmove_a_leanl/_leanr` |
    ///    | `hovermovePitchU` / `hovermovePitchD` | `act_fly_hovermove_pitchu/_pitchd` | `flight_hovermove_a_pitchu/_pitchd` |
    ///    | `fastmovePitchU` / `fastmovePitchD` | `act_fly_fastmove_pitchu/_pitchd` | `flight_fastmove_a_pitchu/_pitchd` |
    ///    | `fastmoveStart` | `act_fly_fastmove_start` | `flight_fastmove_start_a` |
    ///    | `hoverstart` | `act_fly_hoverstart` | `flight_hoverstart_a` |
    ///    | `superland` | `act_fly_superland` | `flight_superland_a` |
    ///    | `dodgeL/R/U/D` | `act_fly_dodge_l/r/u/d` | `flight_dodge_a_l/_r/_u/_d` |
    ///    | `magicIdle` / `magicProjectile` | `act_magic_idle` / `act_magic_projectile` | `magic_idle` / `magic_projectile_spell` |
    ///
    ///    两个词的含义：**`hovermove` = 悬停移动**（直立姿态的巡航，旧名 `cruise`）：
    ///    **`fastmove` = 快移**（超人趴姿的冲刺，旧名 `boost`）。
    ///    2026-09-25 之前用的是 `idle`/`cruise`/`boost`/`dashStart`/`climb`/`leanL` 那套自造词，已全部改掉。
    ///
    ///    ⚠️ 输入侧的词**没改**（`FlightInput.BoostHeld` / `FlightTuning.BoostSpeed` / 上下文里的 `Boost` 等）——
    ///    那些说的是「哪个键、多快」，不是「哪条动画」；改它们会牵动 `custom.flight tune` 的热调键名。
    ///
    /// 注册时机：`MySubModule.OnSubModuleLoad`（模块加载时一次）。
    /// </summary>
    public static class FlightAnimMachine
    {
        /// <summary>注册名（<see cref="AnimMachineRegistry.Create"/> 用它取）。</summary>
        public const string Name = "flight";

        // ─────────────────────── 两族名单（2026-09-25 立）───────────────────────
        //
        // 🔴🔴 **状态分成两族，进趴姿只有一道门**（用户裁定："fastmove 的来源只能是 fastmoveStart 演完"）：
        //    · **直立家**（下面 UprightFamily）—— 进趴姿**必须先播入姿**（`fastmoveStart`）；
        //    · **快移家 / 趴姿族**（下面 ProneFamily）—— 族内互转自由；从族外进来**只能经入姿**。
        //
        // 🔴 这条不变式现在是**结构性的**：趴姿那几个状态的来源表**只列族内状态**，
        //    所以**没有任何 `"*"` 边能进趴姿** —— 想进趴姿就必须先命中入姿边（来源 = 直立家）。
        //    改表的人只要不动这两张名单，就不可能把入姿绕过去（曾经靠"入姿边写在前面 + 条件恰好相同"保证，
        //    那种隐式保证改一行就破）。

        /// <summary>**直立家**（非趴姿的全部姿态）—— 冲刺中在飞时，只能从这里经入姿进趴姿。</summary>
        private static readonly string[] UprightFamily =
        {
            "idle", "hovermove", "hovermoveLeanL", "hovermoveLeanR",
            "hovermovePitchU", "hovermovePitchD", "hoverstart",
            "magicIdle",     // 施法蓄力（回退档）
            // ⚠️ **不含 `magicProjectile`**（释放手势是一次性动作）：列进来就会被入姿边**切断**，
            //    它演完后由 `"*"` 那几条（idle / hovermove / 压弯 / 俯仰）接手，不会卡住。
        };

        /// <summary>**趴姿本体的 6 个状态**（不含闪避）—— 闪避只能从这几个状态发起。</summary>
        private static readonly string[] PronePoses =
        {
            "fastmove", "fastmoveStart", "fastmoveLeanL", "fastmoveLeanR",
            "fastmovePitchU", "fastmovePitchD",
        };

        /// <summary>**快移家（趴姿族）= 趴姿 6 个 + 闪避 4 个**（闪避动画的基准姿势也是趴姿）。</summary>
        private static readonly string[] ProneFamily =
        {
            "fastmove", "fastmoveStart", "fastmoveLeanL", "fastmoveLeanR",
            "fastmovePitchU", "fastmovePitchD",
            "dodgeL", "dodgeR", "dodgeU", "dodgeD",
        };

        /// <summary>
        /// **"冲刺中且在飞"** —— 入姿边与趴姿边**共用同一个委托**（这个语义只允许有一处定义）。
        /// 两族的来源表已经互斥，所以不靠"谁写在前面"来分胜负了；共用委托是为了防止
        /// 有人只改一边的条件、让两边语义悄悄分叉。
        /// </summary>
        private static readonly Func<AnimContext, bool> Sprinting = c => C(c).Boost && C(c).Moving;

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
                // 🔴 **飞行姿势的优先级**（2026-09-25）：不设 = 0（= clip 自带值），会被挥手(2)/挥刀(10~15)
                //    抢走腿 —— 飞行中人被冻住、没有走路动画给它分腿，谁的优先级高谁当主角。
                ActionPriority = () => FlightTuning.FlightActionPriority,
            };

            // ── 状态：一条 = 引擎里的一条动作（`act_xxx`）────────────────────────────
            def.Add(AnimState.Loop("idle", FlightTuning.ActIdle));                       // 悬停待机
            def.Add(AnimState.Loop("hovermove", FlightTuning.ActHoverMove));             // 悬停移动（直立·巡航）
            def.Add(AnimState.Loop("fastmove", FlightTuning.ActFastMove));               // 快移（趴姿·冲刺）
            def.Add(AnimState.Loop("hovermovePitchU", FlightTuning.ActHoverMovePitchU)); // 抬头（悬停移动家·直立姿态）
            def.Add(AnimState.Loop("hovermovePitchD", FlightTuning.ActHoverMovePitchD)); // 低头（同上）
            def.Add(AnimState.Loop("hoverstart", FlightTuning.ActHoverStart));           // 起飞入姿（只由相位 Force 进）
            def.Add(AnimState.Loop("superland", FlightTuning.ActSuperLand));             // 落地（只由相位 Force 进）

            // ── 压弯 / 俯仰（2026-09-22 接 A 套合成件）─────────────────────────
            // 🔴 两个家族各一套：**悬停移动家是直立姿势、快移家是趴姿**，混用 = 硬翻 90°（实测过）。
            def.Add(AnimState.Loop("hovermoveLeanL", FlightTuning.ActHoverMoveLeanL));   // 悬停移动·左压
            def.Add(AnimState.Loop("hovermoveLeanR", FlightTuning.ActHoverMoveLeanR));   // 悬停移动·右压
            def.Add(AnimState.Loop("fastmoveLeanL", FlightTuning.ActFastMoveLeanL));     // 快移·左压
            def.Add(AnimState.Loop("fastmoveLeanR", FlightTuning.ActFastMoveLeanR));     // 快移·右压
            def.Add(AnimState.Loop("fastmovePitchU", FlightTuning.ActFastMovePitchU));   // 快移·抬头
            def.Add(AnimState.Loop("fastmovePitchD", FlightTuning.ActFastMovePitchD));   // 快移·低头

            // ── 一次性动作（`next: null` = 播完留在原地，让普通转移接管）─────────────
            //
            // 🔴 为什么 `next` 填 null 而不是"回到 fastmove"：演完那一刻玩家可能早松开 Shift 了，
            //    写死回 fastmove 会先切过去、下一帧又被普通转移踢走 = 连着两次交叉淡化（看着闪一下）。
            //    留 null 就是"演完让表来判"，一步到位。
            def.Add(AnimState.Once("fastmoveStart", FlightTuning.ActFastMoveStart, next: null,
                                   duration: FlightTuning.BoostStartSeconds));   // 进快移的入姿（治硬切）
            def.Add(AnimState.Once("dodgeL", FlightTuning.ActDodgeL, next: null,
                                   duration: FlightTuning.DodgeClipSeconds));    // 闪避（四方向）
            def.Add(AnimState.Once("dodgeR", FlightTuning.ActDodgeR, next: null,
                                   duration: FlightTuning.DodgeClipSeconds));
            def.Add(AnimState.Once("dodgeU", FlightTuning.ActDodgeU, next: null,
                                   duration: FlightTuning.DodgeClipSeconds));
            def.Add(AnimState.Once("dodgeD", FlightTuning.ActDodgeD, next: null,
                                   duration: FlightTuning.DodgeClipSeconds));

            // ── 施法手势（2026-09-24；FCS 素材，换动作只改 FlightTuning + 内容包两行）──────────────
            //
            // 🔴 **默认不由这里播**（`FlightTuning.CastOnUpperChannel = true`）：
            //    用户要「只动上半身」⇒ 手势由 `SpellCastInput` 播在**通道 1（上身层）**，
            //    通道 0 留给飞行姿势；而**通道 1 一动会把通道 0 的动作挤掉**，
            //    所以 `PlayerFlightBehavior` 第 ⑦′ 条每帧守通道 0 —— 空了就把当前飞行姿势补回去
            //    （`AgentAnimStateMachine.Reassert`）。这样腿是飞行姿、上身是施法姿势。
            //
            //    下面这两个状态是**回退档**（`CastOnUpperChannel = false` 时才由边进）：
            //    走通道 0 播全身施法姿势 —— 通道 1 那条路若在实机上不成立，把开关关掉即可退回。
            //
            // ⚠️ 通道 1 的"收下了不播"根因是 **clip 元数据**（Priority / Right hand pose / Blend out period /
            //    Flags.allow_head_movement），已在编辑器修好并发布 —— 见计划 §A4，别再往引擎上赖。
            def.Add(AnimState.Loop("magicIdle", FlightTuning.ActMagicIdle));             // 蓄力循环（回退档）
            def.Add(AnimState.Once("magicProjectile", FlightTuning.ActMagicProjectile, next: null,
                                   duration: FlightTuning.CastReleaseSeconds));         // 释放（回退档）

            // ── 转移：**顺序 = 优先级**（写在上面先判；第一条命中的生效）─────────────
            //
            //   ① 闪避 ② 施法手势 ③ 快移入姿 ④ 快移家（趴姿）的压弯/俯仰 ⑤ 悬停移动家的压弯
            //   ⑥ 待机 ⑦ 快移 ⑧ 悬停移动家俯仰 ⑨ 悬停移动
            //
            // 🔴 闪避的边**只从快移态出发**，不是 `"*"` —— 实测那 4 条闪避动画
            //    的基准姿势就是**趴姿**（身体轴 前−0.98/上+0.22，与 `flight_fastmove_a` 一致），
            //    从悬停/悬停移动（直立，上+1.00）切过去会硬翻 ~90°。这也正是"闪避只在冲刺中用"的由来。
            //    （2026-09-22 起来源扩到快移家的全部状态：压弯/抬头/低头都算快移态。）
            //
            // 🔴 起飞/落地那两段靠行为里的 `Hold = true` 挂起自动转移（相位自己 Force），
            //    所以这里不用为它们写例外。
            def.Edge(PronePoses, "dodgeL", c => C(c).DodgeRequest == FlightDodgeDir.Left, blend: 0.12f);
            def.Edge(PronePoses, "dodgeR", c => C(c).DodgeRequest == FlightDodgeDir.Right, blend: 0.12f);
            def.Edge(PronePoses, "dodgeU", c => C(c).DodgeRequest == FlightDodgeDir.Up, blend: 0.12f);
            def.Edge(PronePoses, "dodgeD", c => C(c).DodgeRequest == FlightDodgeDir.Down, blend: 0.12f);
            // 🔴 **施法压过一切**（第一条命中的边生效 ⇒ 写在最前面）：蓄力期间身体换成施法姿势，
            //    压弯/俯仰/悬停移动全让位；蓄力一结束（松右键取消 / 放手）这条边自然不成立，回到原姿态。
            def.Edge("*", "magicIdle", c => C(c).SpellCharging && !FlightTuning.CastOnUpperChannel, blend: 0.15f);
            //    ⚠️ `CastOnUpperChannel = true`（默认）时这条边不成立 —— 手势由 `SpellCastInput` 播在**通道 1**，
            //       通道 0 留给飞行姿势（飞行侧每帧守通道 0，见 PlayerFlightBehavior 第 ⑦′ 条）。
            //
            // ③ 🔴🔴 **进快移家（趴姿）一律先播入姿**（2026-09-25 改，实机日志抓到的缺陷）——
            //    来源 = **直立家的全部状态**，条件 = **按着 Shift**（不再用"按下沿"）。
            //
            //    为什么不用按下沿：它只活一帧，下面三种情况**都会把它吞掉** ——
            //      ① 按 Shift 那一刻没推方向键（旧条件还额外要求 `Moving` ⇒ 更早丢）
            //      ② 起飞 / 落地那两段状态机被 `Hold` 住，不判转移表
            //      ③ 施法手势期间让位给施法姿势（上面那条边优先）
            //    丢了入姿 ⇒ 从直立**硬翻 90° 到趴姿**（用户实机原话："某一帧角色会突然抬高"）。
            //
            //    改成"来源是直立家 + Shift 按着"之后是**幂等**的：进了快移家（`fastmove` 等）就不在
            //    来源表里 ⇒ 不会重复播入姿；Shift 一松这条自然不成立，交给 ⑥⑦⑨ 的阶梯。
            //    ⚠️ 来源里**不放 `magicProjectile`**（释放手势是一次性动作，放进来会被这条边打断）。
            def.Edge(UprightFamily, "fastmoveStart", Sprinting);
            // ③ 快移家（趴姿）：压弯优先于俯仰（两个同时成立时先出压弯）
            //    ⚠️ 符号口径：BankBand = −1 是**按 A（左移）** ⇒ 出 leanL；+1 是按 D ⇒ 出 leanR。
            //       左右接反了就交换下面两行（真机一眼能看出来）。
            //    🔴 `blend: 0.15` 与全局默认 0.3 不同 —— 压弯/俯仰是**小姿态**（实测颈/肩 ~20~40°），
            //       0.3 秒的交叉淡化会吃掉大半停留时间（日志实测：一次压弯只停 0.6~1.1 秒），
            //       姿势刚到极值就切回去了。家族之间的大翻转（悬停移动↔快移 120°+）仍走默认 0.3，别一起改。
            def.Edge(ProneFamily, "fastmoveLeanL", c => C(c).Boost && C(c).BankBand < 0,
                     blend: 0.15f, onlyAfterFinish: true);
            def.Edge(ProneFamily, "fastmoveLeanR", c => C(c).Boost && C(c).BankBand > 0,
                     blend: 0.15f, onlyAfterFinish: true);
            def.Edge(ProneFamily, "fastmovePitchU", c => C(c).Boost && C(c).PitchBand > 0,
                     blend: 0.15f, onlyAfterFinish: true);
            def.Edge(ProneFamily, "fastmovePitchD", c => C(c).Boost && C(c).PitchBand < 0,
                     blend: 0.15f, onlyAfterFinish: true);
            // ④ 悬停移动家（直立）：压弯
            def.Edge("*", "hovermoveLeanL", c => C(c).BankBand < 0, blend: 0.15f);
            def.Edge("*", "hovermoveLeanR", c => C(c).BankBand > 0, blend: 0.15f);
            // ⑥~⑨ 原有阶梯
            // 🔴 **`idle` 只判 `!Moving`，不看 Boost**（2026-09-25 用户裁定，我改过一次又退回来了）：
            //    **Shift 是"冲刺速度"，不是"姿态开关"** —— 松掉方向键就是没在飞，
            //    这时回直立悬停（idle）是**正确行为**，不是 bug。
            //    ⚠️ 但"按住 Shift 却不动"要停在 idle 而不是趴姿，靠的是下面 `fastmove` 那条边
            //       把条件收紧成 `Boost && Moving`（曾经只判 Boost ⇒ 不动也摆趴姿，
            //       与 idle 互抢，日志里表现为 `fastmove → idle`）。
            def.Edge("*", "idle", c => !C(c).Moving);
            // 🔴 **趴姿 ⟺ "冲刺中**且**在飞"**（2026-09-25 收紧）：只判 Boost 的话，
            //    "按着 Shift 站着不动"也会摆趴姿 —— 而那时正确的姿态是直立悬停（idle）。
            // 🔴 来源 = **趴姿族**（不是 `"*"`）⇒ **直立家进不来**，只能经上面那条入姿边；
            //    `onlyAfterFinish` ⇒ 入姿 / 闪避还在演的时候不许抢（抢了就是把动画切一半）。
            def.Edge(ProneFamily, "fastmove", Sprinting, onlyAfterFinish: true);
            def.Edge("*", "hovermovePitchU", c => C(c).PitchBand > 0);
            def.Edge("*", "hovermovePitchD", c => C(c).PitchBand < 0);
            def.Edge("*", "hovermove", c => C(c).Moving);

            return def;
        }

        /// <summary>把上下文收窄成飞行那份（定义与上下文同文件，收窄在这里是安全的）。</summary>
        private static FlightAnimContext C(AnimContext c) => (FlightAnimContext)c;
    }
}
