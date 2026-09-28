using System;
using LivingWorldNpcs.Animation;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.Flight
{
    /// <summary>
    /// 飞行状态机用到的**条件谓词 + 命名标量**（2026-09-25 立）—— 注册给 XML 用。
    ///
    /// 🔴 **这里是这批名字的唯一真身**。`ModuleData/statemachines/flight.xml` 里只写名字
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
        /// （<c>AgentAnimStateMachine.TryEventTarget(TakeoffTrigger)</c>）——
        /// **名字只有这一份**，改这里两边一起改。
        /// ⚠️ 它的真身是 `c => false`：**它不代表时刻**，只代表"起飞那一刻"这个标签；
        ///    真正的触发在 C#（空中按空格）。
        /// </summary>
        public const string TakeoffTrigger = "takeoff-trigger";

        /// <summary>**相位"时刻"名 —— 落地（落地动作）**。真身同样是 `c => false`；触发在 C#（板顶触地）。</summary>
        public const string LandTrigger = "land-trigger";

        /// <summary>
        /// **相位"时刻"名 —— 坠落（出机后的下坠姿势）**。真身同样是 `c => false`；触发在 C#（状态机出机那一刻）。
        /// 🔴 它的作用是**让 C# 知道"那一段该演哪个状态"**（`TryEventTarget(FallTrigger)`），
        /// 于是 `flight.xml` 里那个状态改名 / 换 clip，C# 一行都不用动（同起飞 / 落地两条接缝）。
        /// </summary>
        public const string FallTrigger = "fall-trigger";

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
            //    🪦 **2026-09-28 起没有边再用它们**：姿态改走下面的 `climbing`/`diving`（**竖直运动**驱动）——
            //       用户实机反馈"朝天/朝地飞身体没跟着运动走" ⇒ 姿势要跟**实际怎么动**，不跟镜头朝哪
            //       （航向惯性之后两者不再是一回事）。留着是因为"相机朝哪"这个量本身还有用
            //       （`[Flight-Diag]` 仍在打 `pitchBand`），要恢复"镜头驱动"只需把边上 when 换回来。
            AnimConditions.Register("look-up", c => C(c).PitchBand > 0);       // 镜头抬起
            AnimConditions.Register("look-down", c => C(c).PitchBand < 0);     // 镜头压下

            // ── 升降（**键原语也表达不了**：它是"实际在往哪飞"）──────────────────────
            //    判据 = 木板竖直速度的方向分量（`velocity.z ÷ |velocity|`，带迟滞）。
            //    🔴 爬升 / 俯冲的姿态（内容包里的 pitchu / pitchd）**由这两个进**（2026-09-28 接回，
            //       照抄 UE："抬头/低头看竖直速度分量"）。两个家族各用**边的来源**区分，同 look-* 的写法。
            AnimConditions.Register("climbing", c => C(c).ClimbBand > 0);      // 在爬升（姿态 = 抬头）
            AnimConditions.Register("diving", c => C(c).ClimbBand < 0);        // 在俯冲（姿态 = 低头）

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
            AnimConditions.Register(FallTrigger, c => false);   // 坠落（出机后那段）—— 同上是"时刻"标签

            // ── 命名标量（一次性动作的时长；重导 clip 换了帧数就改 FlightTuning 那边的值）──
            AnimConditions.RegisterParam("boostStartSeconds", () => FlightTuning.BoostStartSeconds);
            AnimConditions.RegisterParam("dodgeClipSeconds", () => FlightTuning.DodgeClipSeconds);
            AnimConditions.RegisterParam("castReleaseSeconds", () => FlightTuning.CastReleaseSeconds);
            // 悬停 ⇄ 巡航 那两条边的过渡时长（边上写 `blend="hoverBlendSeconds"`）——
            // 单独一个名字是为了"只调这两条、不动别的转移"，热调键 `custom.flight tune hoverblend`。
            AnimConditions.RegisterParam("hoverBlendSeconds", () => FlightTuning.HoverBlendSeconds);
            // 升降姿态（升 ⇄ 平 ⇄ 降）那几条边的过渡时长（2026-09-28）——
            // 同 hoverBlend 的理由：它是"整段姿态差"，用全局 0.3 秒会啪地翻过去；热调 `tune pitchblend`。
            AnimConditions.RegisterParam("pitchBlendSeconds", () => FlightTuning.PitchBlendSeconds);

            // ── 状态**进出动作**（2026-09-28 立；登记表在 `AnimActions`，写法同上面的条件谓词）──────
            // 🔴 触发点由状态机保证：**enter 在状态真正确立之后 / leave 在离开之前**，
            //    且两端都包了 try/catch（动作炸了不会带崩状态机，见 `AgentAnimStateMachine.RunAction`）。
            // 用在哪儿：`flight.xml` 的 `<state name="超人落地" … enter="land-fx"/>`。
            AnimActions.Register(LandFxAction, c => PlayFxAtFeet(LandingFxName, out _));
        }

        /// <summary>
        /// **落地特效的名字**（内容包发布包里注册的那个粒子系统名）。
        /// 🔴 换名字 / 改名 = 只改这一行（XML 里写的是动作名 `land-fx`，不是粒子名）。
        /// </summary>
        public const string LandingFxName = "lwn_manual_fly_land";

        /// <summary>`flight.xml` 里 <c>&lt;state … enter="land-fx"/&gt;</c> 的那个动作名。</summary>
        public const string LandFxAction = "land-fx";

        /// <summary>已经报过一次"粒子没注册"了（避免每帧刷屏）。</summary>
        private static bool _warnedFxMissing;

        /// <summary>
        /// **在主角脚下的地面上放一次粒子**（世界固定的一次性 burst）。
        ///
        /// 供两处用：① 状态机的 `enter="land-fx"`（落地那一刻）② 验收命令 `custom.flight fx`。
        ///
        /// 🔴 **位置口径**：水平用主角当前位置、**竖直用地面高度**（`Scene.GetTerrainHeight`）——
        ///    撞地判定是「板顶触地」，直接拿人物 z 会浮空或埋地（见 `PlayerFlightBehavior` 的撞地段）。
        /// 🔴 **不抛异常**：调用方可能是每帧都在跑的状态机。失败一律"记一行 + 返回 false"。
        /// </summary>
        internal static bool PlayFxAtFeet(string particleName, out string error)
        {
            error = null;
            try
            {
                if (string.IsNullOrEmpty(particleName))
                {
                    error = "no particle name";
                    return false;
                }
                Mission mission = Mission.Current;
                if (mission == null)
                {
                    error = "only works inside a mission (battle / arena / town scene)";
                    return false;
                }
                Agent main = mission.MainAgent;
                if (main == null)
                {
                    error = "no main agent in this mission";
                    return false;
                }
                Scene scene = mission.Scene;
                if (scene == null)
                {
                    error = "no scene";
                    return false;
                }

                int id = ParticleSystemManager.GetRuntimeIdByName(particleName);
                if (id == -1)
                {
                    error = "'" + particleName + "' NOT REGISTERED (id -1) - publish the particle pack "
                          + "into the module the game loads (see docs)";
                    if (!_warnedFxMissing)
                    {
                        _warnedFxMissing = true;
                        DebugLogger.Log("[Flight-Fx] " + error);
                    }
                    return false;
                }

                Vec3 p = main.Position;
                float z = p.z;
                try { z = scene.GetTerrainHeight(new Vec2(p.x, p.y), true); }
                catch { /* 拿不到地形就用人物高度兜底 */ }
                scene.CreateBurstParticle(id, new MatrixFrame(Mat3.Identity, new Vec3(p.x, p.y, z)));
                DebugLogger.Log($"[Flight-Fx] 播放 '{particleName}'（id {id}）于 ({p.x:F2}, {p.y:F2}, {z:F2})");
                return true;
            }
            catch (Exception ex)
            {
                error = "exception: " + ex.Message;
                DebugLogger.Log("[Flight-Fx] " + error);
                return false;
            }
        }

        /// <summary>把上下文收窄成飞行那份（定义与上下文同命名空间，收窄在这里是安全的）。</summary>
        private static FlightAnimContext C(AnimContext c) => (FlightAnimContext)c;
    }
}
