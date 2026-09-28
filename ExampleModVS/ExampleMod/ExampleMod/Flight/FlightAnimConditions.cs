using System;
using System.Collections.Generic;
using LivingWorldNpcs.Animation;
using TaleWorlds.Core;
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
            // 用在哪儿：`flight.xml` 的 `<state name="超人落地" …>` 里的 `<track at="0.067" action="land-fx"/>`。
            AnimActions.Register(LandFxAction, c => PlayFxAtFeet(LandingFxName, out _));
            // 冲刺起步 / 起飞（`fastmoveStart` 与 `进入飞行` 的 `<track at="0.1" action="boost-fx"/>`）——
            // 🔴 **在"身上"，不是脚下**（2026-09-28 修）：UE 那边这几个发射器都是 `ParticleOwnerPosition`
            //    = 主人身上；我第一版误用了 `PlayFxAtFeet`（那是给"贴地炸开"的落地用的），位置低了一米。
            AnimActions.Register(BoostStartFxAction, c => PlayFxAtBody(BoostStartFxName, 1.0f, out _));
            // 🔴 **持续特效**（2026-09-28）：冲刺期间在**身上**按间隔刷 `lwn_manual_fly_boosting`
            //    （`fastmove` 的 `<track every="…" action="boost-loop"/>`）。
            //    为什么这么设计：UE 那边的"持续云迹"本来就是**短命粒子不停刷**出来的
            //    （云 0.2 s @10/秒）—— 所以**密度由 XML 的 `every` 控制**，改密度不用重新发布粒子；
            //    也绕开了"骨挂粒子摘不掉"那个死结（不挂，只是刷）。
            // ⚠️ `every` 该填多少，取决于资产自己的 `Emitter life`（见 `BoostLoopFxName` 的注释）。
            AnimActions.Register(BoostLoopFxAction, c => PlayFxAtBody(BoostLoopFxName, 1.0f, out _));
            // 🔴 **手部尾迹**（2026-09-28）：**两只手各刷一颗** —— 走"读手骨世界坐标"那条路，
            //    **不是骨挂**（骨挂摘不掉，冲刺一停尾迹还留在手上，见 `PlayFxAtBothHands` 的说明）。
            //    对应 UE `NS_Flight_Trail` 里的 `HandTrail_L` / `HandTrail_R`（两条 8 cm 的细带子）。
            AnimActions.Register(BoostHandFxAction, c => PlayFxAtBothHands(BoostHandFxName, out _));
        }

        /// <summary>**手部尾迹的粒子名**（双手各刷一颗，2026-09-28）。</summary>
        public const string BoostHandFxName = "lwn_manual_fly_handtrail";

        /// <summary>`fastmove` 上那条手部尾迹轨道点的动作名。</summary>
        public const string BoostHandFxAction = "boost-hand";

        /// <summary>**持续云迹的粒子名**（冲刺期间刷的那个，2026-09-28）。</summary>
        public const string BoostLoopFxName = "lwn_manual_fly_boosting";

        /// <summary>
        /// `fastmove` 那条**周期**轨道点的动作名。
        /// ⚠️ **`every` 的取值取决于这个资产的 `Emitter life`**：
        /// · `Emitter life` &gt; 0（自己会停）⇒ `every` 取它的**一半左右**，两批首尾叠上 = 看着连续；
        /// · `Emitter life` = 0（永不自己停）⇒ **不能周期刷**（会越堆越多），得改成"进状态刷一次"。
        /// </summary>
        public const string BoostLoopFxAction = "boost-loop";

        /// <summary>
        /// **落地特效的名字**（内容包发布包里注册的那个粒子系统名）。
        /// 🔴 换名字 / 改名 = 只改这一行（XML 里写的是动作名 `land-fx`，不是粒子名）。
        /// </summary>
        public const string LandingFxName = "lwn_manual_fly_land";

        /// <summary>`flight.xml` 里 <c>&lt;state … &gt;&lt;track … action="land-fx"/&gt;</c> 的那个动作名。</summary>
        public const string LandFxAction = "land-fx";

        /// <summary>**冲刺起步特效的名字**（2026-09-28）。同 <see cref="LandingFxName"/> 的口径。</summary>
        public const string BoostStartFxName = "lwn_manual_fly_booststart";

        /// <summary>`fastmoveStart` 那条轨道点的动作名。</summary>
        public const string BoostStartFxAction = "boost-fx";

        /// <summary>已经报过"某个粒子没注册"的名字（**按名字记** —— 免得第二个特效缺失时一句话都不打）。</summary>
        private static readonly HashSet<string> _warnedFxMissing = new HashSet<string>(StringComparer.Ordinal);

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
            return PlayFx(particleName, atFeet: true, upOffset: 0f, out error);
        }

        /// <summary>
        /// **在主角身上（可带抬高）放一次粒子** —— 给**持续刷**的破空云迹 / 尾迹用（2026-09-28）。
        ///
        /// 与 <see cref="PlayFxAtFeet"/> 的区别只有一个 —— **取哪个 z**：
        /// 脚下那个取**地面高度**（落地要贴地），这个取**人自己的位置**（云要裹在人身上，不贴地）。
        ///
        /// 🔴 **帧里带上人的朝向**：UE 那边粒子是"往身后甩"的（`AddVelocity (-7500,0,0)`，局部空间）——
        ///    我们的粒子若也做局部空间，帧的旋转分量就得是人的朝向，否则甩的方向是错的。
        /// </summary>
        internal static bool PlayFxAtBody(string particleName, float upOffset, out string error)
        {
            return PlayFx(particleName, atFeet: false, upOffset: upOffset, out error);
        }

        /// <summary>
        /// **在主角两只手的骨骼位置各放一次粒子** —— 给**手部尾迹**用（2026-09-28）。
        ///
        /// 🔴 **为什么不是"骨挂"**：引擎的 `CreateParticleSystemAttachedToBone` **没有摘除句柄**
        ///    ⇒ 挂上去就摘不掉，冲刺一停尾迹还留在手上。所以走**周期刷点**：
        ///    每 N 毫秒读一次手骨的世界坐标、在那儿炸一颗**短命**的 —— 停手 = 不再刷 = 自然消失。
        ///
        /// 🔴 **骨骼世界坐标的算法**（抄自 `custom.psys_head` 那段可用的先例）：
        ///    `GetBoneEntitialFrameWithIndex` 给的是**实体局部**（动画算完、还没乘实体全局变换的那一层）
        ///    ⇒ 要再乘一次 `visuals.GetGlobalFrame()` 才是世界坐标。
        ///    ⚠️ 骑砍**骨轴不沿肢体**，所以"往哪个方向甩"不能用骨轴推，得用世界方向（或用人的朝向）。
        /// </summary>
        internal static bool PlayFxAtBothHands(string particleName, out string error)
        {
            error = null;
            try
            {
                if (!TryResolveFxId(particleName, out int id, out error))
                {
                    return false;
                }
                if (!TryGetMainAgent(out Mission mission, out Agent main, out Scene scene, out error))
                {
                    return false;
                }
                MBAgentVisuals visuals = main.AgentVisuals;
                Skeleton skel = visuals != null ? visuals.GetSkeleton() : null;
                if (visuals == null || skel == null)
                {
                    error = "main agent has no visuals/skeleton";
                    return false;
                }

                MatrixFrame entFrame = visuals.GetGlobalFrame();
                Mat3 rot = entFrame.rotation.TransformToParent(skel.GetBoneEntitialFrameWithIndex(0).rotation);
                int played = 0;
                for (int i = 0; i < 2; i++)
                {
                    sbyte bone = visuals.GetRealBoneIndex(i == 0 ? HumanBone.HandL : HumanBone.HandR);
                    if (bone < 0)
                    {
                        continue;
                    }
                    Vec3 world = entFrame.TransformToParent(skel.GetBoneEntitialFrameWithIndex(bone).origin);
                    scene.CreateBurstParticle(id, new MatrixFrame(rot, world));
                    played++;
                }
                if (played == 0)
                {
                    error = "no hand bones resolved";
                    return false;
                }
                DebugLogger.Log($"[Flight-Fx] 播放 '{particleName}'（id {id}）于 {played} 只手");
                return true;
            }
            catch (Exception ex)
            {
                error = "exception: " + ex.Message;
                DebugLogger.Log("[Flight-Fx] " + error);
                return false;
            }
        }

        /// <summary>
        /// 两个入口共用的执行体（2026-09-28 合并 —— 铁律 18：同一语义只留一份）。
        ///
        /// 🔴 **不抛异常**：调用方可能是**每帧都在跑的状态机**。失败一律"记一行 + 返回 false"。
        /// </summary>
        private static bool PlayFx(string particleName, bool atFeet, float upOffset, out string error)
        {
            error = null;
            try
            {
                if (!TryResolveFxId(particleName, out int id, out error))
                {
                    return false;
                }
                if (!TryGetMainAgent(out Mission mission, out Agent main, out Scene scene, out error))
                {
                    return false;
                }

                Vec3 p = main.Position;
                float z;
                if (atFeet)
                {
                    // 落地：水平用人的位置、**竖直用地面**（撞地判定是"板顶触地"，直接用人的 z 会浮空/埋地）
                    z = p.z;
                    try { z = scene.GetTerrainHeight(new Vec2(p.x, p.y), true); }
                    catch { /* 拿不到地形就用人物高度兜底 */ }
                }
                else
                {
                    z = p.z + upOffset;      // 身上：云裹在人身上
                }

                Mat3 rot = Mat3.Identity;    // 朝向（局部空间速度靠它）
                try { rot = main.Frame.rotation; } catch { }

                scene.CreateBurstParticle(id, new MatrixFrame(rot, new Vec3(p.x, p.y, z)));
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

        /// <summary>把粒子名解析成引擎 id（**按名字去重**：每个缺失的粒子各报一次，不刷屏）。</summary>
        private static bool TryResolveFxId(string particleName, out int id, out string error)
        {
            id = -1;
            error = null;
            if (string.IsNullOrEmpty(particleName))
            {
                error = "no particle name";
                return false;
            }
            id = ParticleSystemManager.GetRuntimeIdByName(particleName);
            if (id != -1)
            {
                return true;
            }
            error = "'" + particleName + "' NOT REGISTERED (id -1) - publish the particle pack "
                  + "into the module the game loads (see docs)";
            if (_warnedFxMissing.Add(particleName))
            {
                DebugLogger.Log("[Flight-Fx] " + error);
            }
            return false;
        }

        /// <summary>取出「在一个 Mission 里 + 有主角 + 有场景」这三个前提（各入口共用）。</summary>
        private static bool TryGetMainAgent(out Mission mission, out Agent main, out Scene scene, out string error)
        {
            mission = Mission.Current;
            main = null;
            scene = null;
            error = null;
            if (mission == null)
            {
                error = "only works inside a mission (battle / arena / town scene)";
                return false;
            }
            main = mission.MainAgent;
            if (main == null)
            {
                error = "no main agent in this mission";
                return false;
            }
            scene = mission.Scene;
            if (scene == null)
            {
                error = "no scene";
                return false;
            }
            return true;
        }

        /// <summary>把上下文收窄成飞行那份（定义与上下文同命名空间，收窄在这里是安全的）。</summary>
        private static FlightAnimContext C(AnimContext c) => (FlightAnimContext)c;
    }
}
