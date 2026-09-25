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

        /// <summary>一次性动作的时长（秒）。≤0 = 改用引擎的播放进度判完成。</summary>
        public float Duration;

        /// <summary>一次性动作播完后去哪个状态（null = 留在原地，让普通转移接管）。</summary>
        public string Next;

        /// <summary>播完转移用的过渡时长（秒）。</summary>
        public float NextBlend = 0.25f;

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

        /// <summary>一次性状态：播完自动去 <paramref name="next"/>。</summary>
        public static AnimState Once(string name, string action, string next, float duration = 0f, float nextBlend = 0.25f)
            => new AnimState { Name = name, Action = action, OneShot = true, Next = next, Duration = duration, NextBlend = nextBlend };
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
        /// **相位驱动**：这条边**不由状态机求值** —— 它由飞行相位（C#）在特定时刻
        /// `Force` 进目标状态（起飞入姿 / 落地）。写进定义只是为了让**表与图完整**：
        /// 让"从哪进、从哪出"在定义里一眼可见，而不是散在 C# 里。
        /// 状态机求值时直接跳过（见 <see cref="AgentAnimStateMachine.Tick"/>）。
        /// </summary>
        public bool PhaseForced;

        public AnimEdgeDef(string[] from, string to, Func<AnimContext, bool> when,
                           float blend = -1f, bool onlyAfterFinish = false, bool phaseForced = false)
        {
            From = from; To = to; When = when; Blend = blend;
            OnlyAfterFinish = onlyAfterFinish; PhaseForced = phaseForced;
        }

        public AnimEdgeDef(string from, string to, Func<AnimContext, bool> when,
                           float blend = -1f, bool onlyAfterFinish = false, bool phaseForced = false)
            : this(new[] { from }, to, when, blend, onlyAfterFinish, phaseForced)
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

        public readonly string Name;

        /// <summary>没写 <see cref="AnimEdgeDef.Blend"/> 的转移用它。用委托是为了**热调生效**
        /// （飞行那边指向 `FlightTuning.AnimBlendIn`，调参立刻反映到状态机）。</summary>
        public Func<float> DefaultBlend = () => 0.3f;

        /// <summary>"引擎把 0 号通道抢走"的核对周期（秒）。</summary>
        public Func<float> RecheckSeconds = () => 0.5f;

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
            _edges.Add(new AnimEdgeDef(from, to, when, blend, onlyAfterFinish));
            return this;
        }

        /// <summary>同上，来源多个。</summary>
        public AnimMachineDef Edge(string[] from, string to, Func<AnimContext, bool> when,
                                   float blend = -1f, bool onlyAfterFinish = false, bool phaseForced = false)
        {
            _edges.Add(new AnimEdgeDef(from, to, when, blend, onlyAfterFinish, phaseForced));
            return this;
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
            return this;
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
