using System;
using System.Collections.Generic;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.Animation
{
    /// <summary>
    /// **动画状态机的"事实"基类** —— 每个用状态机的系统自己派生一个，装"转移条件要读的那些量"。
    ///
    /// 为什么要有它：状态与转移的**定义**是注册在案、与具体系统解耦的（见 <see cref="AnimMachineRegistry"/>），
    /// 所以条件不能直接去读某个行为类的私有字段 —— 一律通过**上下文对象**读。
    ///
    /// 范本见 `Flight/FlightAnimMachine.cs`：`FlightAnimContext { Moving / Boost / PitchBand }`。
    /// </summary>
    public abstract class AnimContext
    {
    }

    /// <summary>一个**动画状态**的定义（注册进 <see cref="AnimMachineDef"/>）。</summary>
    public sealed class AnimState
    {
        /// <summary>状态名（转移表与日志里用它，**不是**引擎动作名）。</summary>
        public string Name;

        /// <summary>
        /// 引擎动作名（写在内容包的 `action_types.xml` / `action_sets.xml` 里）。
        /// **空串 = 这个状态还没接**：状态机会跳过它（等价于"没有这条转移"），
        /// 所以条件里不用写"如果导了就……"。
        /// </summary>
        public string Action;

        /// <summary>一次性动作（播完自动去 <see cref="Next"/>）？false = 循环。</summary>
        public bool OneShot;

        /// <summary>
        /// 一次性动作的**可选时长覆盖**（秒）。**默认 0 = 不用** —— 长度由 clip 自己带（引擎给播放进度）。
        /// >0 = 显式覆盖（要主动截短 clip 时才写 `duration="…"`）。
        /// </summary>
        public float Duration;

        /// <summary>已接？（动作名非空）</summary>
        public bool IsWired => !string.IsNullOrEmpty(Action);

        // 引擎动作名 → 索引的解析是**懒的**（`ActionIndexCache.Create` 每次给一个新实例，首次取 Index 才去查表），
        // 所以缓存必须在**定义**这一层做：一份定义整个进程只解析一次，与"多少个 agent 在跑"无关。
        private ActionIndexCache _index;

        /// <summary>取引擎动作索引（首次调用解析，之后直接命中）。</summary>
        internal ActionIndexCache Index()
        {
            if (_index == null)
                _index = ActionIndexCache.Create(Action);
            return _index;
        }

        /// <summary>循环状态。</summary>
        public static AnimState Loop(string name, string action)
            => new AnimState { Name = name, Action = action };

        /// <summary>一次性动作播完后去哪个状态（null = 留在原地，让普通转移接管）。</summary>
        public string Next;

        /// <summary>播完转移用的过渡时长（秒）。</summary>
        public float NextBlend = 0.25f;

        /// <summary>
        /// **进入本状态时执行的动作**（2026-09-28 立）。null = 没有。
        ///
        /// 与 <see cref="When"/> 同一个套路：**XML 里只写名字**（`enter="land-fx"`），
        /// 真身在 <see cref="AnimActions"/> 注册。主要用于**"这一刻播个特效"**这类事。
        ///
        /// 🔴 触发点 = <see cref="AgentAnimStateMachine"/> 里**状态真正确立之后**
        ///    （切换前的校验没通过 = 根本没进这个状态 ⇒ 不会误触发）。
        /// </summary>
        public Action<AnimContext> EnterAction;

        /// <summary>
        /// **离开本状态时执行的动作**。null = 没有。
        /// 🔴 触发点 = **离开之前**（含"出机"那条路 —— 交还 0 号通道时也调）。
        /// </summary>
        public Action<AnimContext> LeaveAction;

        /// <summary>
        /// **状态轨道上的时间点**（2026-09-28 立，照 UE 的 AnimNotify 做的）。
        ///
        /// 与 <see cref="EnterAction"/> 的区别：enter 是"进状态**立刻**"，
        /// 这里是"**演到 clip 的百分之几**才触发"——
        /// 落地特效就该这么挂：进落地状态那一刻人**还在空中一点点**（撞地判定带 0.25 m 容差），
        /// 立刻炸 = 看着早；UE 那边也是把它挂在落地蒙太奇的 **6.7%** 处。
        ///
        /// 每个点只触发一次（进状态时清零），见 <see cref="AnimTrackPoint.Fired"/>。
        /// </summary>
        public List<AnimTrackPoint> Track;

        /// <summary>一次性状态：播完自动去 <paramref name="next"/>。</summary>
        public static AnimState Once(string name, string action, string next, float duration = 0f, float nextBlend = 0.25f)
            => new AnimState { Name = name, Action = action, OneShot = true, Next = next, Duration = duration, NextBlend = nextBlend };
    }

    /// <summary>
    /// **状态轨道上的一个时间点**（2026-09-28 立）—— 对应 UE 的 **AnimNotify**。
    ///
    /// **两种写法**：
    /// <code>
    /// &lt;!-- ① 一次性：演到 clip 的 6.7% 时响一次（落地特效用这个） --&gt;
    /// &lt;state name="超人落地" act="act_fly_superland" once="true"&gt;
    ///     &lt;track at="0.067" action="land-fx" /&gt;
    /// &lt;/state&gt;
    ///
    /// &lt;!-- ② 周期：状态期间**每 0.1 秒刷一次**（持续特效用这个：破空云迹 / 尾迹） --&gt;
    /// &lt;state name="fastmove" act="act_fly_fastmove"&gt;
    ///     &lt;track every="0.1" action="boost-cloud" /&gt;
    /// &lt;/state&gt;
    /// </code>
    /// 一次性点的 `at` = **占整条 clip 的比例 0~1**（不是秒）—— 与项目其它地方一个口径：
    /// 长度由 clip 自己带，**重导 clip 换了帧数不用改这里**。
    /// 周期点的 `every` = **秒**（循环状态没有"百分之几"可言，见 <see cref="AnimTrackPoint.Every"/>）。
    ///
    /// 🔴 **求值不受 `Hold` 影响** —— 落地/起飞这些相位状态正是被 `Hold` 住的（动画归相位管），
    ///    把求值放进 Hold 门控里 = 永远不触发（2026-09-28 实现时特意避开的坑）。
    /// </summary>
    public sealed class AnimTrackPoint
    {
        /// <summary>触发位置：占整条 clip 的比例（0~1）。**周期点（<see cref="Every"/>&gt;0）不看它。**</summary>
        public float At;

        /// <summary>
        /// **周期**（秒）：&gt;0 = **每隔这么久触发一次**（到状态结束为止），0 = 只触发一次。
        ///
        /// 🔴 **为什么周期用秒、而 `at` 用比例**（2026-09-28 定）：
        /// `at` 是"这条 clip 演到百分之几"（跟着 clip 走，重导换帧数不用改）；
        /// 而周期点用在**循环状态**上（冲刺本体 `fastmove`）—— 循环状态没有"百分之几"可言，
        /// 它要的是"**每秒刷几次**"，所以是秒。
        ///
        /// 用途 = **持续特效**（破空云迹 / 尾迹）：UE 那边也是靠"短命粒子不停刷"做的
        /// （云 0.2 s @10/秒、烟 0.15 s @75/秒），骑砍侧同理 ——
        /// **粒子资产做"一次性 1 颗"，密度全由这里的秒数控制**（改密度不用重发粒子）。
        /// </summary>
        public float Every;

        /// <summary>到点执行的动作（<see cref="AnimActions"/> 里注册的真身）。</summary>
        public Action<AnimContext> Act;

        /// <summary>一次性点：本次进入状态内是否已触发过（进状态时清零）。</summary>
        internal bool Fired;

        /// <summary>周期点：下一次该响的时刻（秒，相对进入状态那一刻）。进状态时清零。</summary>
        internal float NextAt;
    }

    /// <summary>
    /// **一条转移（边）的定义**：`从哪些状态 → 到哪个状态，当什么条件成立`，过渡多久。
    ///
    /// 🔴 条件是**普通委托**（定义处一行 lambda，读代码时就在眼前），不是字符串表达式 ——
    ///    不引入"表达式引擎"这种东西要学。
    /// </summary>
    public sealed class AnimEdgeDef
    {
        /// <summary>来源状态名；<c>"*"</c> = 任意状态。</summary>
        public string[] From;

        /// <summary>目标状态名。</summary>
        public string To;

        /// <summary>成立就切。读 <see cref="AnimContext"/>（各系统自己派生的那个）。</summary>
        public Func<AnimContext, bool> When;

        /// <summary>过渡时长（秒）。&lt;0 = 用机器定义的默认值。</summary>
        public float Blend = -1f;

        /// <summary>
        /// **只在该状态"演完之后"生效**（正在播的一次性动作**不参与**求值）。
        ///
        /// 🔴 **它补的是缺的第三档**（原来只有两档）：
        ///    · `From = "*"`      —— 谁都能进，**但不打断**一次性动作；
        ///    · `From = 指名状态` —— 只能从这些状态进，**且可以打断**（用于"闪避可以打断入姿"这类）；
        ///    · **本开关**：`From = 指名状态` + **不打断**。
        ///
        /// 典型场景就是它诞生的原因（2026-09-25）：**"一次性动作演完回主状态"这条边**
        /// 必须**指名**那几个一次性状态（不指名的话别的状态也能进来），
        /// 但**绝不能打断它们**（不开关的话 `dodge → fastmove` 会把闪避动画切一半。
        /// 实测：玩家闪避时一定按着 W，`Boost &amp;&amp; Moving` 恒成立 ⇒ 必被切）。
        /// </summary>
        public bool OnlyAfterFinish;

        /// <summary>
        /// **事件驱动**：这条边**不由状态机求值** —— 它由飞行相位（C#）在特定时刻
        /// `Force` 进目标状态（起飞入姿 / 落地）。写进定义只是为了让**表与图完整**：
        /// 让"从哪进、从哪出"在定义里一眼可见，而不是散在 C# 里。
        /// 状态机求值时直接跳过（见 <see cref="AgentAnimStateMachine.Tick"/>）。
        /// </summary>
        public bool EventDriven;

        /// <summary>
        /// 事件边上写的**"时刻"名**（`when="takeoff-trigger"` / `when="land-trigger"`）。
        ///
        /// 🔴 普通边的条件是**委托**、用不着名字；事件边单独留下名字，是因为**相位（C#）要按时刻找状态**：
        ///    "落地这个时刻该 Force 进哪个状态" = 读 `when="land-trigger"` 那条边的 `to`
        ///     （<see cref="AgentAnimStateMachine.TryEventTarget"/>）。
        /// ⚠️ 名字本身**不代表时刻**（那两个谓词的真身是 `c => false`）——
        ///    真正的触发时刻在 C#（起飞 = 空中按空格；落地 = 板顶触地），XML 只说"进哪个状态"。
        /// </summary>
        public string WhenName;

        /// <summary>
        /// **事件边声明的"剩余百分比"**（`anim="remaining" anim-rem-pct="10"` 里那个 10）。
        /// &lt;0 = 没声明 ⇒ 调用方（相位）回退到自己的默认判据。
        ///
        /// 🔴 **为什么事件边要带这个数**：事件边**不由状态机求值**（Tick 里整条跳过），
        ///    但"什么时候出机"仍然需要判据 —— 以前那个判据**硬编码在 C# 里**（手填的 2.0 秒），
        ///    和"长度问 clip"的原则冲突。现在改成**相位去读这条边在 XML 里写的百分比**：
        ///    图上写的 = 实际生效的，换 clip 不用改代码。
        /// </summary>
        public float RemainPct = -1f;

        public AnimEdgeDef(string[] from, string to, Func<AnimContext, bool> when,
                           float blend = -1f, bool onlyAfterFinish = false, bool eventDriven = false)
        {
            From = from; To = to; When = when; Blend = blend;
            OnlyAfterFinish = onlyAfterFinish; EventDriven = eventDriven;
        }

        public AnimEdgeDef(string from, string to, Func<AnimContext, bool> when,
                           float blend = -1f, bool onlyAfterFinish = false, bool eventDriven = false)
            : this(new[] { from }, to, when, blend, onlyAfterFinish, eventDriven)
        {
        }

        internal bool Matches(string current)
        {
            for (int i = 0; i < From.Length; i++)
                if (From[i] == "*" || From[i] == current)
                    return true;
            return false;
        }

        /// <summary>
        /// 是否**指名**了这个来源（<c>"*"</c> 不算）。一次性动作播放期间靠它区分
        /// "兜底边"（不许打断）与"专门为打断写的边"（可以打断）。
        /// </summary>
        internal bool MatchesExplicit(string current)
        {
            for (int i = 0; i < From.Length; i++)
                if (From[i] == current)
                    return true;
            return false;
        }
    }

    /// <summary>
    /// **一台状态机的完整定义**（状态表 + 转移表）—— 这就是"注册"的东西。
    ///
    /// 写法（照 `Flight/FlightAnimMachine.cs`）：
    /// <code>
    /// var def = new AnimMachineDef("flight");
    /// def.Add(AnimState.Loop("idle", "act_fly_idle"));
    /// def.Add(AnimState.Once("fastmoveStart", "act_fly_fastmove_start", next: "fastmove", duration: 1.0f));
    /// def.Edge("*", "idle", ctx => !((MyCtx)ctx).Moving, blend: 0.3f);
    /// AnimMachineRegistry.Register(def);
    /// </code>
    /// **转移按加入顺序求值**，第一条命中的生效 ⇒ 顺序 = 优先级。
    /// </summary>
    public sealed class AnimMachineDef
    {
        private readonly Dictionary<string, AnimState> _states = new Dictionary<string, AnimState>();
        private readonly List<AnimEdgeDef> _edges = new List<AnimEdgeDef>();

        /// <summary>"按状态取边"的缓存（见 <see cref="EdgesFrom"/>）—— <see cref="AddEdge"/> 时清空。</summary>
        private readonly Dictionary<string, List<AnimEdgeDef>> _fromCache =
            new Dictionary<string, List<AnimEdgeDef>>(StringComparer.Ordinal);

        public readonly string Name;

        /// <summary>没写 <see cref="AnimEdgeDef.Blend"/> 的转移用它。用委托是为了**热调生效**
        /// （飞行那边指向 `FlightTuning.AnimBlendIn`，调参立刻反映到状态机）。</summary>
        public Func<float> DefaultBlend = () => 0.3f;

        /// <summary>"引擎把 0 号通道抢走"的核对周期（秒）。</summary>
        public Func<float> RecheckSeconds = () => 0.5f;

        /// <summary>没写 `finish-margin` 时的默认"完成余量"（见 <see cref="FinishMarginFrac"/>）。</summary>
        public const float DefaultFinishMargin = 0.15f;

        /// <summary>
        /// **"一次性动作算演完"的余量**（占整条 clip 的比例；`<state_machine finish-margin="0.15">` 可调，0~1）。
        ///
        /// 🔴 **为什么要留余量、不等真播完**（2026-09-27 用户裁定，UE 习惯）：
        ///    `SetActionChannel` 用的是 `blendOutPeriodToNoAnim: 0` ⇒ **clip 一播完，0 号通道当场空掉**
        ///    （引擎回到走跑 / 默认姿势）。而我们只能**下一个 tick** 才发现"播完了" ⇒ 中间那 1~几帧：
        ///    ① 看得见地掉回默认姿势；② **更要命 —— 随后的交叉淡化是从"默认姿势"开始的**（不是上一段的收尾），
        ///    整段过渡都变形。留一点余量在"通道还有动作"的时候切过去，淡化才从**真实姿势**接得上。
        ///
        /// 语义：`演完才进`（`after-finish`）/ `演完兜底`（`&lt;state next="…"&gt;`）/ 原语 `anim="finished"`
        /// 全部按**剩余 ≤ 这个余量**判定。想要"真播完"就写 `anim="remaining" anim-rem-pct="0"`。
        /// </summary>
        public float FinishMarginFrac = DefaultFinishMargin;

        /// <summary>
        /// 播动作时带上的**优先级**（写进 `additionalFlags` 的低字节，引擎的 `amf_priority_mask = 0xFF`）。
        /// 0 = 不设（引擎按 clip 自带的 `Priority` 字段走）。
        ///
        /// 🔴 **为什么需要它**（2026-09-25 实机）：飞行姿势的 clip `Priority = 0`，而挥手动作是 2、
        /// 挥刀 10~15 ⇒ **在飞行中（人被冻住、没有走路动画）挥一条通道 1 的动作，腿会被它抢走**
        /// （地面不会 —— 腿归移动层）。给飞行姿势带上一个更高的优先级，腿才保得住。
        /// 用委托 = 热调生效。
        /// </summary>
        public Func<int> ActionPriority = () => 0;

        public AnimMachineDef(string name)
        {
            Name = name;
        }

        /// <summary>声明一个状态。</summary>
        public AnimMachineDef Add(AnimState state)
        {
            if (state == null || string.IsNullOrEmpty(state.Name))
                throw new ArgumentException("AnimState 必须有 Name");
            _states[state.Name] = state;
            return this;
        }

        /// <summary>声明一条转移（**按调用顺序求值**；<paramref name="from"/> 写 <c>"*"</c> = 任意状态）。
        /// <paramref name="onlyAfterFinish"/> 见 <see cref="AnimEdgeDef.OnlyAfterFinish"/>。</summary>
        public AnimMachineDef Edge(string from, string to, Func<AnimContext, bool> when,
                                   float blend = -1f, bool onlyAfterFinish = false)
        {
            return AddEdge(new AnimEdgeDef(from, to, when, blend, onlyAfterFinish));
        }

        /// <summary>同上，来源多个。</summary>
        public AnimMachineDef Edge(string[] from, string to, Func<AnimContext, bool> when,
                                   float blend = -1f, bool onlyAfterFinish = false, bool eventDriven = false)
        {
            return AddEdge(new AnimEdgeDef(from, to, when, blend, onlyAfterFinish, eventDriven));
        }

        /// <summary>
        /// 直接加一条**已经构造好的**边（给 XML 装载器用：那边是逐条校验后才拼出来的
        /// <see cref="AnimEdgeDef"/>，不该再拆成参数重来一遍）。
        /// **顺序 = 优先级**，和 <see cref="Edge(string,string,Func{AnimContext,bool},float,bool)"/> 一条规则。
        /// </summary>
        public AnimMachineDef AddEdge(AnimEdgeDef edge)
        {
            if (edge == null)
            {
                throw new ArgumentException("AddEdge 不接受 null");
            }
            _edges.Add(edge);
            _fromCache.Clear();          // 表变了 ⇒ "按状态取边"的缓存失效（见 EdgesFrom）
            return this;
        }

        /// <summary>
        /// **从某个状态出发的边**（**顺序 = 全局优先级**，含 `from="*"` 的兜底边）。
        ///
        /// 🔴 **状态机任何时刻只在一个状态里**（2026-09-26 用户指出）⇒ 每帧只有这些边**可能**命中，
        ///    全量扫整张表既是浪费、也会让人误读成"顺序是全局的事"。这里按状态**预先分好并缓存**：
        ///    · 容器来源在**装载期**就展开成叶子状态了（每个成员各进一份）⇒ 这里天然覆盖"父容器的边"
        ///    · 只在**首次进入该状态**时算一次（25 条边 × 十几个状态 = 一次性的几微秒），之后直接命中缓存
        ///    · 调用方**不要再自己判 `Matches`**（已经筛过了），但 `EventDriven` 仍要自己跳过
        ///
        /// <see cref="AddEdge"/> 会清缓存 ⇒ 装载期边还没加完就调用也是对的。
        /// </summary>
        public IReadOnlyList<AnimEdgeDef> EdgesFrom(string state)
        {
            List<AnimEdgeDef> list;
            if (_fromCache.TryGetValue(state, out list))
            {
                return list;
            }
            list = new List<AnimEdgeDef>();
            for (int i = 0; i < _edges.Count; i++)
            {
                if (_edges[i].Matches(state))
                {
                    list.Add(_edges[i]);      // 按 _edges 的先后追加 = 全局优先级顺序
                }
            }
            _fromCache[state] = list;
            return list;
        }

        public IReadOnlyList<AnimEdgeDef> Edges => _edges;

        public bool TryGetState(string name, out AnimState state) => _states.TryGetValue(name, out state);

        /// <summary>这个状态名声明过吗（供运行时校验/日志）。</summary>
        public bool HasState(string name) => _states.ContainsKey(name);
    }

    /// <summary>
    /// **状态机注册表**（2026-09-22）—— 与项目里 `ActionRegistry` 同一种思路：
    /// **定义集中注册在一处，运行时按名字取用**，而不是散在各自的行为类里硬编码。
    ///
    /// 谁用谁注册（在模块加载时调一次）：
    /// <code>
    /// MySubModule.OnSubModuleLoad:  FlightAnimMachine.Register();
    /// </code>
    /// 取用：
    /// <code>
    /// var ctx  = new FlightAnimContext();
    /// var anim = AnimMachineRegistry.Create("flight", ctx);   // 定义不在 = 直接报错（不静默）
    /// </code>
    /// </summary>
    public static class AnimMachineRegistry
    {
        private static readonly Dictionary<string, AnimMachineDef> _defs =
            new Dictionary<string, AnimMachineDef>(StringComparer.Ordinal);

        /// <summary>注册一台状态机的定义（重名 = 覆盖，方便热改；日志里会提醒）。</summary>
        public static void Register(AnimMachineDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.Name))
                throw new ArgumentException("AnimMachineDef 必须有 Name");

            if (_defs.ContainsKey(def.Name))
                DebugLogger.Log($"[Anim] 状态机定义 '{def.Name}' 被重复注册（覆盖旧的）");
            _defs[def.Name] = def;
        }

        /// <summary>
        /// 按名字建一台状态机实例。
        /// **定义没注册不会崩**：报一行日志 + 返回一台"空机器"（没有状态）= 动画不播、其它照常。
        /// </summary>
        public static AgentAnimStateMachine Create(string name, AnimContext context)
        {
            AnimMachineDef def;
            if (!_defs.TryGetValue(name, out def))
            {
                DebugLogger.Log($"[Anim] 状态机 '{name}' 没有注册 —— 忘了调 XxxAnimMachine.Register()？（本次降级为空机器）");
                def = new AnimMachineDef(name);
            }
            return new AgentAnimStateMachine(def, context);
        }

        /// <summary>注册过吗（体检/日志用）。</summary>
        public static bool IsRegistered(string name) => _defs.ContainsKey(name);
    }
}
