using System;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.Animation
{
    /// <summary>
    /// **带迟滞的开关**（同一根轴上"进"和"出"用不同阈值）。
    ///
    /// 为什么需要：单阈值下玩家把某个量停在阈值附近，姿态会**来回翻**（飞行实测教训）。
    /// 用法：
    /// <code>
    /// _band = new AnimLatch(enter: 0.42f, exit: 0.30f);   // 进用大阈值、出用小阈值
    /// _band.SetThresholds(FlightTuning.PitchThreshold, FlightTuning.PitchExitThreshold);  // 支持热调
    /// _band.Update(cameraForwardZ);                       // 每帧喂一次
    /// if (_band.Value > 0) ...                            // 读档位：+1 / 0 / −1
    /// </code>
    /// </summary>
    public sealed class AnimLatch
    {
        private float _enter;
        private float _exit;

        /// <summary>当前档位：+1 / 0 / −1。</summary>
        public int Value { get; private set; }

        public AnimLatch(float enter, float exit)
        {
            SetThresholds(enter, exit);
        }

        /// <summary>改阈值（支持运行期热调 —— 每帧喂之前调一次即可）。</summary>
        public void SetThresholds(float enter, float exit)
        {
            _enter = Math.Abs(enter);
            _exit = Math.Abs(exit);
        }

        /// <summary>喂一个采样值（比如相机前方向的竖直分量），返回档位。</summary>
        public int Update(float v)
        {
            if (Value == 0)
            {
                if (v > _enter) Value = 1;
                else if (v < -_enter) Value = -1;
            }
            else if (Value > 0)
            {
                if (v < _exit) Value = 0;
            }
            else
            {
                if (v > -_exit) Value = 0;
            }
            return Value;
        }

        /// <summary>强制归零（比如"完全没输入"时重新判）。</summary>
        public void Reset() => Value = 0;
    }

    /// <summary>
    /// **通用代理动画状态机（运行时）** —— 骑砍只给了"播哪条动画"（<see cref="Agent.SetActionChannel"/>），
    /// 没有状态机；这个类把"什么时候播什么、怎么切、切多久"收成**注册在案的转移表**并按表流转。
    ///
    /// 分工（读代码先认这两件东西）：
    /// · **定义** = <see cref="AnimMachineDef"/>（状态表 + 转移表），注册进 <see cref="AnimMachineRegistry"/>，
    ///   写在各自的 `XxxAnimMachine.cs` 里（范本 `Flight/FlightAnimMachine.cs`）。
    /// · **运行时** = 本类：每帧喂一次上下文，它按表决定要不要切状态、并负责把动作写进 0 号通道。
    ///
    /// 几个已经踩过坑、所以内建进来的东西
    /// · **防被引擎抢**：0 号通道引擎的走跑系统也有权写 ⇒ 每 <see cref="AnimMachineDef.RecheckSeconds"/>
    ///   核对一次，发现不是我们的就重设（实测不核对会被抢回去）。
    /// · **动作名写错是静默失败**（引擎不报错、只是不播）⇒ 这里替它报一次日志。
    /// · **未接的状态自动跳过**（动作名空串）⇒ 行为等价于"没有这条转移"，条件里不用写例外。
    /// · **一次性动作（<see cref="AnimState.Once"/>）播完前不被打断** —— `"*"` 出发的兜底边
    ///   在它播完之前一律不参与求值（否则下一帧就被踢走，一帧都播不出来）；
    ///   要打断就写**指名**它的边。见 <see cref="Tick"/> 里那条注释。
    /// · **相位自己掌控动画时**用 <see cref="Hold"/>，此时只认 <see cref="Force"/>。
    ///
    /// 用在**很多个 agent** 上时（将来别的运动系统）
    /// · 把 <see cref="Verbose"/> 关掉（否则每次转移打一行日志）；
    /// · 动作索引已缓存在**定义**上（<see cref="AnimState.Index"/>），与 agent 数量无关；
    /// · 每帧成本 = 几条边的 lambda 求值（读几个字段）+ 一次计时累加，**无堆分配**；
    ///   真正花钱的"0 号通道写入"只在**状态变化时**发生。
    /// · ⚠️ 但**能不能用**不取决于本类：0 号通道的归属得先解决 ——
    ///   飞行能拿到是因为"冻结了玩家 + 暂停了 AI"；NPC 要用得同样先让它别被走跑系统写。
    /// </summary>
    public sealed class AgentAnimStateMachine
    {
        private readonly AnimMachineDef _def;
        private readonly AnimContext _ctx;

        private Agent _agent;              // 最近一次拿到的 agent（Force 可能早于首次 Tick）
        private AnimState _current;
        private float _elapsed;
        private float _sinceRecheck;
        private bool _warnedBadAction;
        private Func<float> _progressFn;

        // 抖动自检（2026-09-22）：条件振荡会让状态在帧级来回切，动画永远播不起来
        // （实机踩过：boost↔cruise 每 5ms 互踢，看着像"前倾的巡航"）。这里只是**报出来**，不改行为。
        private int _switchCount;
        private float _switchWindow;
        private string _lastSwitchFrom;

        /// <summary>
        /// 切换时是否打一行日志（排查"为什么播的不是我以为的那条"第一站）。
        /// 🔴 **默认 false**（2026-09-22 用户要求：通用件出厂安静，别替使用方决定刷不刷屏）——
        /// 由使用方按自己的总闸打开，范本 = `PlayerFlightBehavior` 每帧写 `_anim.Verbose = FlightTuning.DebugLog`。
        /// </summary>
        public bool Verbose = false;

        /// <summary>
        /// **暂停自动转移**：true 时只维持当前状态、不判转移表（<see cref="Force"/> 仍然有效）。
        /// 用途：相位自己掌控动画的那两段（起飞入姿 / 落地）。
        /// </summary>
        public bool Hold;

        internal AgentAnimStateMachine(AnimMachineDef def, AnimContext context)
        {
            _def = def;
            _ctx = context ?? new AnimContextImpl();
        }

        /// <summary>没给上下文时的兜底（条件读不到东西而已，不该崩）。</summary>
        private sealed class AnimContextImpl : AnimContext { }

        /// <summary>定义名（日志用）。</summary>
        public string MachineName => _def.Name;

        /// <summary>当前状态名（null = 还没接管）。</summary>
        public string Current => _current?.Name;

        /// <summary>当前引擎动作名（没接管时 null）。</summary>
        public string CurrentAction => _current?.Action;

        /// <summary>当前状态已经播了多久（秒）。</summary>
        public float CurrentElapsed => _elapsed;

        /// <summary>当前状态是不是"一次性动作且已播完"。</summary>
        public bool CurrentFinished
        {
            get
            {
                if (_current == null || !_current.OneShot)
                    return false;
                if (_current.Duration > 0.01f)
                    return _elapsed >= _current.Duration;
                return _progressFn != null && _progressFn() >= 0.999f;
            }
        }

        /// <summary>
        /// **强制进入某状态**（不看条件）—— 用于相位驱动的时刻（起飞 / 落地 / 收摊）。
        /// </summary>
        /// <param name="agent">要驱动哪个 agent（**必须给** —— 起飞/落地都在"首次 Tick 之前"发生）。</param>
        /// <param name="startProgress">从动作的哪个进度开始播（0~1；跳过 clip 开头用）。</param>
        public void Force(Agent agent, string stateName, float blend = -1f, float startProgress = 0f)
        {
            Enter(agent, stateName, blend, startProgress, forced: true);
        }

        /// <summary>
        /// **把当前状态的动作重新写一遍**（不清状态、不重播计时）—— 用于"通道 0 被别人挤掉之后补回来"。
        /// 典型场景：上层（通道 1）播施法动作时，引擎可能把通道 0 的全身动作取消掉 ⇒ 腿失去姿态；
        /// 调用方每帧查一次"通道 0 是不是空了"，空了就调这里补回去（见 <c>PlayerFlightBehavior</c>）。
        /// 返回 false = 当前没有状态可补（还没进过任何状态）。
        /// </summary>
        public bool Reassert(Agent agent, float blend = 0.1f)
        {
            if (agent == null || _current == null)
            {
                return false;
            }
            Enter(agent, _current.Name, blend, 0f, forced: true);
            return true;
        }

        /// <summary>每帧调一次（喂之前把上下文里的量更新好）。</summary>
        public void Tick(Agent agent, float dt)
        {
            if (agent == null)
                return;
            _agent = agent;

            _elapsed += dt;
            TickSwitchWindow(dt);       // 抖动自检的窗口按**真实时间**推进（不是按切换次数）

            if (!Hold && _current != null)
            {
                // ① 一次性动作播完 → 去它声明的下一个状态
                if (CurrentFinished && !string.IsNullOrEmpty(_current.Next))
                {
                    Enter(agent, _current.Next, _current.NextBlend, 0f, forced: false);
                }
                else
                {
                    // ② 转移表：按注册顺序，第一条命中的生效
                    var edges = _def.Edges;
                    // 🔴 **命中的第一条边定输赢**（这张表是"优先级阶梯"，不是"找一条能切的"）。
                    //    命中边的目标就是当前状态 ⇒ **留在原地，且不能继续往下找** ——
                    //    否则低优先级的兜底边会把它踢走。2026-09-22 实机踩过：写成了"跳过自己那条继续找"，
                    //    结果 boost↔cruise 每 5ms 互踢一次，动画永远停在淡化开头（看着像"前倾的巡航"）。
                    for (int i = 0; i < edges.Count; i++)
                    {
                        AnimEdgeDef e = edges[i];
                        if (!e.Matches(_current.Name))
                            continue;

                        // 🔴 **一次性动作（冲刺入姿 / 闪避）播放期间不被打断**：
                        //    `"*"` 出发的兜底边在它播完前一律不参与 —— 否则下一帧就被 `* → 巡航` 踢走，
                        //    一次性动作一帧都播不出来（这是它没被接上的原因，2026-09-22）。
                        //    要打断就写**指名**它的边（`Edge("dodge_l", "cruise", …)`）。
                        if (_current.OneShot && !CurrentFinished && !e.MatchesExplicit(_current.Name))
                            continue;

                        bool ok;
                        try { ok = e.When == null || e.When(_ctx); }
                        catch (Exception ex)
                        {
                            DebugLogger.Log($"[Anim:{_def.Name}] 转移条件异常（{_current.Name}→{e.To}）: {ex.Message}");
                            continue;
                        }
                        if (!ok)
                            continue;

                        if (e.To != _current.Name)
                            Enter(agent, e.To, e.Blend, 0f, forced: false);
                        break;      // ← 命中即定（目标是自己 = 留在原地，也在这里收手）
                    }
                }
            }

            RecheckStolen(agent, dt);
        }

        /// <summary>把 0 号通道还给引擎（收摊 / 落地时调；之后 <see cref="Current"/> 变 null）。</summary>
        public void Release(Agent agent)
        {
            _current = null;
            _elapsed = 0f;
            if (agent == null)
                return;
            try
            {
                agent.SetActionChannel(0, ActionIndexCache.act_none, ignorePriority: false, blendInPeriod: 0.3f);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Anim:{_def.Name}] 归还通道异常（通常无害）: {ex.Message}");
            }
        }

        // ─────────────────────────────── 内部 ───────────────────────────────

        private void Enter(Agent agent, string stateName, float blend, float startProgress, bool forced)
        {
            if (string.IsNullOrEmpty(stateName))
                return;

            // 🔴 Force 可能在**首次 Tick 之前**被调（起飞就发生在 TickGrounded 里）⇒ 这里兜底取记住的那个；
            //    两个都没有（真没 agent）= 不接管，**不崩**（宁可不播动画，也不能让游戏挂）。
            if (agent == null)
                agent = _agent;
            if (agent == null)
                return;
            _agent = agent;

            AnimState state;
            if (!_def.TryGetState(stateName, out state))
            {
                DebugLogger.Log($"[Anim:{_def.Name}] 转移目标 '{stateName}' 没在定义里声明过");
                return;
            }
            if (!state.IsWired)
                return;      // 动作名空串 = 这条还没接 ⇒ 静默跳过（等价于没有这条转移）

            ActionIndexCache idx = state.Index();      // 缓存在定义上，整个进程只解析一次
            if (idx.Index < 0)
            {
                if (!_warnedBadAction)
                {
                    _warnedBadAction = true;
                    DebugLogger.Log($"[Anim:{_def.Name}] 动作 '{state.Action}' 解析为 act_none —— " +
                                    "检查内容包 action_types.xml / action_sets.xml（写错不报错，只是不播）");
                }
                return;
            }

            float useBlend = blend >= 0f ? blend : _def.DefaultBlend();
            string from = _current?.Name ?? "-";
            CountSwitch(from, state.Name);
            _current = state;
            _elapsed = 0f;
            _sinceRecheck = 0f;
            _progressFn = () =>
            {
                try { return agent != null ? agent.GetCurrentActionProgress(0) : 0f; }
                catch { return 0f; }
            };

            try
            {
                // 🔴 `blendOutPeriodToNoAnim: 0` —— **必须显式传 0，不能用默认值**（默认 0.4 秒）。
                //    引擎语义 = 「这条动作播完时用多久淡出到【无动画】」；不传 = 每条动作走到尾声
                //    都会被拉回静止姿势再弹回来。实机取证（2026-09-22，custom.anim_trace 逐帧骨骼）：
                //    一圈里只有中段 ~0.25~0.75 是真正的动画，头尾分别被 blendIn / blendOutToNoAnim
                //    拖走 —— 膝角从数据里的 8~11° 被拉到精确 0.00（= 子骨 == 父骨 == 静止）再弹回，
                //    观感就是"腿摆得很硬"。飞行这条链上全部是循环姿态，**永远不需要"淡出到无"**。
                agent.SetActionChannel(0, idx, ignorePriority: true,
                                       blendInPeriod: useBlend, blendOutPeriodToNoAnim: 0f,
                                       startProgress: startProgress);
                if (Verbose)
                    DebugLogger.Log($"[Anim:{_def.Name}] {from} → {state.Name}（{state.Action}）" +
                                    $" blend={useBlend:F2}{(startProgress > 0f ? $" start={startProgress:F2}" : "")}" +
                                    (forced ? " [强制]" : ""));
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Anim:{_def.Name}] 切动作 '{state.Action}' 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 抖动自检：**1 秒（真实时间）内切换超过 10 次** ⇒ 打一行警告（含"谁在跟谁互踢"）。
        /// 判据不是"切得多"，而是**同一个来回反复**（A→B→A→B）—— 那一定是条件振荡，不是玩家在操作。
        ///
        /// 🔴 窗口必须按**真实时间**推进（`Tick` 每帧喂 dt），不能按"切换次数"近似
        ///   （2026-09-22 实机踩到：原来写 `_switchWindow += 1/60` 只在**切换时**累加 ⇒
        ///   实际报的是"累计切了 61 次"，不是"1 秒内 61 次"，36 秒的正常操作被误报成抖动）。
        /// </summary>
        private void CountSwitch(string from, string to)
        {
            _switchCount++;
            _lastSwitchFrom = from;
        }

        /// <summary>窗口推进（每帧调，见 <see cref="Tick"/>）。</summary>
        private void TickSwitchWindow(float dt)
        {
            _switchWindow += dt;
            if (_switchWindow < 1f)
                return;

            if (_switchCount > 10)
                DebugLogger.Log($"[Anim:{_def.Name}] ⚠️ 抖动：1 秒内切了 {_switchCount} 次" +
                                $"（最近 {_lastSwitchFrom}↔{_current?.Name}）—— 转移条件可能在振荡，动画会一直停在淡化开头");

            _switchCount = 0;
            _switchWindow = 0f;
        }

        /// <summary>0 号通道引擎也有权写 —— 定期核对有没有被抢走，被抢了重设。</summary>
        private void RecheckStolen(Agent agent, float dt)
        {
            if (_current == null)
                return;

            _sinceRecheck += dt;
            if (_sinceRecheck < _def.RecheckSeconds())
                return;
            _sinceRecheck = 0f;

            try
            {
                ActionIndexCache idx = _current.Index();
                if (idx.Index < 0)
                    return;
                if (agent.GetCurrentAction(0) == idx)
                    return;                       // 还是我们的，没事
                if (Verbose)
                    DebugLogger.Log($"[Anim:{_def.Name}] '{_current.Name}' 被引擎抢走了，重设");
                agent.SetActionChannel(0, idx, ignorePriority: true,
                                       blendInPeriod: _def.DefaultBlend(), blendOutPeriodToNoAnim: 0f);
            }
            catch
            {
                // 核对失败不该影响玩法
            }
        }
    }
}
