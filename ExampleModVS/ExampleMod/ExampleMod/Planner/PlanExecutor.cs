using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════
    // PlanTexts — 执行器/安全网玩家可见文本（铁律 13：一律走 LWNTextHelper）
    // ═══════════════════════════════════════════════════════════════

    internal static class PlanTexts
    {
        // 本地化：R2 执行者倒下中止
        public static string CompanionDown => LWNTextHelper.ResolveText("LWN_plan_abort_down", "The companion has fallen!");
        // 本地化：R5 目标敌对中止（可 replan）
        public static string FightBrokeOut => LWNTextHelper.ResolveText("LWN_plan_abort_fight", "A fight broke out. Falling back.");
        // 本地化：R6 总时长超限中止
        public static string TooLong => LWNTextHelper.ResolveText("LWN_plan_abort_toolong", "This is taking too long. Falling back.");
        // 本地化：外部打断中止
        public static string Interrupted => LWNTextHelper.ResolveText("LWN_plan_abort_interrupted", "The plan was interrupted.");
        // 本地化：@abort_gracefully 中止
        public static string Aborted => LWNTextHelper.ResolveText("LWN_plan_abort_generic", "The plan is called off.");
        // 本地化：步骤超时中止
        public static string StepTimeout => LWNTextHelper.ResolveText("LWN_plan_abort_timeout", "A step took too long. Falling back.");
        // 本地化：跳转目标缺失中止
        public static string BadJump => LWNTextHelper.ResolveText("LWN_plan_abort_badjump", "The plan went wrong. Falling back.");
        // 本地化：主链走完但 goal 未达成
        public static string GoalNotMet => LWNTextHelper.ResolveText("LWN_plan_goal_notmet", "It did not work out.");
        // 本地化：lead 等待超时（当面报告）
        public static string LeadWaiting => LWNTextHelper.ResolveText("LWN_plan_lead_waiting", "Are you coming or not?");
        // 本地化：偷窃所得物品名（箱子记账）
        public static string Loot => LWNTextHelper.ResolveText("LWN_plan_loot", "loot");
        // 本地化：金币物品名（目击问责/暗账）
        public static string Gold => LWNTextHelper.ResolveText("LWN_plan_gold", "gold");
    }

    // ═══════════════════════════════════════════════════════════════
    // PlanExecutor.cs — 计划解释器 + 状态机（§5.4）
    //
    // 执行模型：计划 = steps[]（每步带 actor）→ 按 actor 分组 → 每个 actor
    // 一个独立游标（actor 内串行、actor 间并行、跨 actor 用 when 谓词
    // 经世界状态隐式同步）。单随从 = 全部步骤 actor 缺省 self。
    // loop 段先于 steps 主链执行（case N/P：循环清剿 → 报告收尾）。
    //
    // 双通道：事件通道（决策结果广播 refused/followed → 步骤 on_event 即时跳转）
    //        状态通道（谓词轮询，100ms 节流 → until/when 推进，sustained 积分）
    //
    // 收尾三路一函数：成功/失败/中断 → 清队列 → 释放控制 → 恢复默认行为；
    //   report 可选 = 当面报告（恢复默认跟随走回玩家 ~3m 冒泡转述后再彻底收尾）。
    //
    // 驱动：AgentAIController.OnMissionTick → PlanExecutor.TickAll(dt)；
    //       brain 队列持有 ExecutePlanAction（IsFinished = 执行器完成）。
    // ═══════════════════════════════════════════════════════════════

    public class PlanExecutor
    {
        public enum ExecutorState { Executing, Paused, Succeeded, Failed, Aborted }

        // ── 静态注册表（执行器由 AgentAIController 统一驱动；HUD/调试查询）──
        public static PlanExecutor Instance { get; private set; }
        public static readonly Dictionary<Agent, PlanExecutor> ActiveExecutors = new Dictionary<Agent, PlanExecutor>();
        public static PlanExecutor GetExecutorFor(Agent agent)
        {
            if (agent == null) return null;
            return ActiveExecutors.TryGetValue(agent, out var e) ? e : null;
        }

        /// <summary>统一驱动所有活动执行器（AgentAIController.OnMissionTick 调用）。</summary>
        public static void TickAll(float dt)
        {
            if (ActiveExecutors.Count == 0) return;
            foreach (var e in ActiveExecutors.Values.ToList())
                e.Tick(dt);
        }

        /// <summary>Mission 结束统一收尾（OnMissionScreenFinalize 兜底纪律）。</summary>
        public static void ShutdownAll()
        {
            foreach (var e in ActiveExecutors.Values.ToList())
                e.FinalizeExecutor("计划随场景结束而中止");
            ActiveExecutors.Clear();
            Instance = null;
        }

        // ── 主执行者信息 ──
        public Agent OwnerAgent { get; private set; }
        public Plan Plan { get; private set; }
        public CommandIntentType IntentType { get; private set; }
        public string Summary { get; private set; }

        public ExecutorState State = ExecutorState.Executing;
        public string PauseReason;
        public string EndMessage;       // 收尾消息（报告文本）
        public string CurrentSummary;   // 执行摘要（HUD 状态行）
        public float Elapsed;
        public bool IsFinished { get; private set; }        // ExecutePlanAction 轮询
        public bool IsPlayerInModalUi { get; private set; } // R7

        public event Action<PlanExecutor> OnFinished;       // 收尾通知（brain 恢复 Following）
        public event Action<PlanExecutor, string> OnAborted; // 中止通知（Replan 低频重入监听，§7.2）

        /// <summary>原命令文本（Replan 上下文；PlanCommandFlow 批准时传入）。</summary>
        public string OriginalCommand;
        /// <summary>同计划的 replan 次数（节流 ≤ 2）。</summary>
        public int ReplanCount;
        /// <summary>意外事件日志（Replan prompt 上下文："守卫与玩家发生战斗，s3 未能完成"）。</summary>
        public readonly List<string> EventLog = new List<string>();

        // ── 内部 ──
        private readonly RuntimeWorldState _world = new RuntimeWorldState();
        private readonly List<ActorCursor> _cursors = new List<ActorCursor>();
        private ActorCursor _selfCursor;                    // 主执行者游标（contingency/trigger 上下文）
        private readonly List<(float Time, string Type)> _eventQueue = new List<(float, string)>();
        private float _stepStartTime;                       // 当前步骤开始时刻（事件过滤基准）
        private readonly Dictionary<Contingency, bool> _contingencyPrev = new Dictionary<Contingency, bool>();
        private readonly HashSet<Contingency> _oneShotFired = new HashSet<Contingency>();
        private readonly Dictionary<Trigger, bool> _triggerPrev = new Dictionary<Trigger, bool>();
        private float _tickAccum;
        private bool _goalMet;
        private bool _reportPending;
        private string _pendingReport;
        private float _reportTimer;
        private bool _reportSpoken;
        private bool _finalized;
        private string _stepResultKey;                      // 判定型原子结果（result 路由）
        private float _stolenGold;                          // steal_attempt 成功所得（give_gold "stolen"）

        public RuntimeWorldState World => _world;

        // ═══════════════════════════════════════════════════════════
        // 生命周期
        // ═══════════════════════════════════════════════════════════

        private PlanExecutor() { }

        /// <summary>从 LLM/示例 JSON 构建执行器（校验通过才返回；null = 拒收）。</summary>
        public static PlanExecutor Create(Agent ownerAgent, Plan plan, string intentType, Dictionary<string, Agent> roleAgents = null)
        {
            if (ownerAgent == null || plan == null) return null;

            string intentStr = intentType ?? plan.Intent?.IntentType;
            CommandIntentType parsed = ParseIntentType(intentStr);
            var validation = PlanValidator.Validate(plan, intentStr ?? "");
            if (!validation.Ok)
            {
                foreach (var w in validation.Warnings)
                    DebugLogger.Log($"[PlanExecutor] 计划校验未通过: {w}");
                return null;
            }
            if (validation.Warnings.Count > 0)
            {
                foreach (var w in validation.Warnings)
                    DebugLogger.Log($"[PlanExecutor] 计划校验警告: {w}");
            }

            // 质量诊断（纯报告，不拒收不改动）：对照输出质量要求逐条打分，
            // 抓结构校验覆盖不到的质量项（步数/预案数/contingencies/combat 与 SPAR 矛盾/goal 纪律）
            foreach (var d in PlanValidator.Diagnose(plan, parsed))
                DebugLogger.Log($"[PlanQuality] {d}");

            var ex = new PlanExecutor
            {
                OwnerAgent = ownerAgent,
                Plan = plan,
                IntentType = parsed,
                Summary = plan.Summary,
            };
            ex._world.OwnerAgent = ownerAgent;
            ex._world.Owner = ex;

            // 角色表：快照自动打标 + 显式注入
            var snap = SceneSnapshot.Build(Mission.Current);
            ex._world.Snapshot = snap;
            foreach (var info in snap.Agents)
            {
                if (info.Role != null && info.Agent != null)
                    ex._world.RoleAgents[info.Role] = info.Agent;
            }
            if (roleAgents != null)
            {
                foreach (var kv in roleAgents)
                    if (kv.Value != null)
                        ex._world.RoleAgents[kv.Key] = kv.Value;
            }
            ex._world.RoleAgents["self"] = ownerAgent;
            if (Agent.Main != null) ex._world.RoleAgents["player"] = Agent.Main;

            // 区域注册（§5.0）：intent.zone/target 区域名 → 物件/区域解析注册（LOOKOUT 望风区等）
            ex.RegisterIntentZones(plan.Intent);

            ex.BuildCursors(ownerAgent);
            return ex;
        }

        /// <summary>启动执行（brain order_execute_plan 分支调用）。</summary>
        public void Start(Agent agent)
        {
            OwnerAgent = agent;
            Instance = this;
            ActiveExecutors[agent] = this;
            _tickAccum = 0f;
            Elapsed = 0f;
            _stepStartTime = 0f;
            CurrentSummary = Summary ?? "";
            DebugLogger.Log($"[PlanExecutor] 开始执行计划（{IntentType}）: {Summary}");
        }

        public void Tick(float dt)
        {
            // 当面报告流程（收尾后置阶段：走回玩家旁冒泡转述）
            if (_reportPending)
            {
                TickReport(dt);
                return;
            }
            if (IsFinished) return;

            Elapsed += dt;
            _tickAccum += dt;
            if (_tickAccum < 0.1f) return;      // 100ms 节流
            _tickAccum = 0f;

            _world.Tick(dt);

            // R7 玩家模态（偷窃条/对话/剧情演出）→ Pause；模态结束 → Resume
            bool modal = DetectPlayerModalUi();
            IsPlayerInModalUi = modal;
            if (modal && State == ExecutorState.Executing) { Pause("玩家在忙别的"); return; }
            if (!modal && State == ExecutorState.Paused && PauseReason == "玩家在忙别的") { Resume(); return; }

            // R1 玩家战斗 → Pause（随从护主由既有 event_agent_damaged 链处理）；战斗结束 → Resume
            bool playerCombat = IsPlayerInCombat();
            if (playerCombat && State == ExecutorState.Executing) { Pause("玩家战斗中"); return; }
            if (!playerCombat && State == ExecutorState.Paused && PauseReason == "玩家战斗中") { Resume(); return; }

            // R4 玩家走远（>30m）→ Pause 追回；豁免：当前步骤是远离玩家的独行任务
            if (State == ExecutorState.Paused && PauseReason == "玩家走远了")
            {
                TickChaseBack(dt);
                return;
            }
            if (State == ExecutorState.Executing && !IsCurrentStepRemote())
            {
                var player = Agent.Main;
                if (player != null && player.IsActive() && OwnerAgent.IsActive()
                    && OwnerAgent.Position.Distance(player.Position) > 30f)
                {
                    Pause("玩家走远了");
                    return;
                }
            }

            if (State != ExecutorState.Executing) return;

            // Guardrails R2/R5/R6
            TickGuardrails(dt);
            if (State != ExecutorState.Executing) return;

            // contingencies（EDGE 上升沿）
            TickContingencies();
            if (State != ExecutorState.Executing) return;

            // triggers（TRIGGER 上升沿 → signal_player，计划不结束）
            TickTriggers();

            // 游标推进（actor 间并行）
            bool anyActive = false;
            foreach (var cursor in _cursors)
            {
                if (!cursor.Done && cursor.Agent != null && cursor.Agent.IsActive())
                {
                    anyActive = true;
                    TickCursor(cursor, dt);
                    if (IsFinished || State != ExecutorState.Executing) return;
                }
            }
            if (!anyActive) FinishMainChain();
        }

        /// <summary>区域注册（§5.0）：intent.zone / watch_point 等区域名 → 物件/区域解析后注册为具名 zone
        /// （LOOKOUT 望风区、ANNIHILATE 清剿区等；解析不到 → 由运行时 query 兜底或诚实失败）。</summary>
        private void RegisterIntentZones(PlanIntent intent)
        {
            if (intent == null) return;
            var candidates = new List<string>();
            string z = PlanRefUtil.Normalize(intent.Zone, out string zq);
            if (zq != null) z = zq;
            if (!string.IsNullOrEmpty(z)) candidates.Add(z);
            string wp = PlanRefUtil.Normalize(intent.WatchPoint, out string wpq);
            if (wpq != null) wp = wpq;
            if (!string.IsNullOrEmpty(wp) && !PlanVocab.EntityKeywords.Contains(wp)) candidates.Add(wp);
            foreach (var name in candidates)
            {
                if (_world.NamedZoneRadii.ContainsKey(name)) continue;
                if (Mission.Current == null) continue;
                // 物件匹配 → 注册为 zone（半径 8m）
                var obj = _world.Snapshot.FindObject(name);
                if (obj != null)
                {
                    _world.NamedPositions[name] = SceneSnapshot.GetMissionObjectPosition(obj.MissionObject);
                    _world.NamedZoneRadii[name] = 8f;
                    continue;
                }
                var zone = _world.Snapshot.FindZone(name);
                if (zone != null)
                {
                    _world.NamedPositions[name] = zone.Position;
                    _world.NamedZoneRadii[name] = zone.Radius;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════
        // 游标
        // ═══════════════════════════════════════════════════════════

        private void BuildCursors(Agent owner)
        {
            var mainSteps = CollectStepsForActor(Plan, "self", owner);
            var cursor = new ActorCursor
            {
                ActorId = "self",
                Agent = owner,
                Sequence = mainSteps,
                LoopEntryPending = Plan.Loop != null,   // 有循环段 → 先跑循环
            };
            _cursors.Add(cursor);
            _selfCursor = cursor;

            // subjects 多 actor（一带多）：subjects 列表角色 → 快照解析
            var subjects = Plan.Intent?.Subjects;
            if (subjects != null)
            {
                foreach (var s in subjects)
                {
                    if (string.IsNullOrEmpty(s)) continue;
                    if (string.Equals(s, "self", StringComparison.OrdinalIgnoreCase)) continue;
                    Agent actor = null;
                    if (_world.RoleAgents.TryGetValue(s, out actor) && actor != null) { }
                    else if (Agent.Main != null && string.Equals(s, "player", StringComparison.OrdinalIgnoreCase)) actor = Agent.Main;
                    else if (Agent.Main != null)
                    {
                        var info = _world.Snapshot.FindAgent(s);
                        actor = info?.Agent;
                    }
                    if (actor == null || actor == owner) continue;
                    _world.RoleAgents[s] = actor;
                    var actorSteps = CollectStepsForActor(Plan, s, actor);
                    if (actorSteps.Count > 0)
                        _cursors.Add(new ActorCursor { ActorId = s, Agent = actor, Sequence = actorSteps });
                }
            }
        }

        /// <summary>收集该 actor 的主链步骤（缺省 self；"all" 广播 = 所有 actor）。</summary>
        private static List<PlanStep> CollectStepsForActor(Plan plan, string actorId, Agent actor)
        {
            var result = new List<PlanStep>();
            if (plan.Steps == null) return result;
            foreach (var s in plan.Steps)
            {
                if (s == null) continue;
                string a = string.IsNullOrEmpty(s.Actor) ? "self" : s.Actor;
                if (a == actorId || (a == "all" && actor != null)) result.Add(s);
            }
            return result;
        }

        /// <summary>主链走完收尾判定：goal 达成 → 成功；无 goal（计划以步骤链定义成功，§2.3 回落）→ 主链走完即成功。</summary>
        private void FinishMainChain()
        {
            if (_goalMet || Plan?.Goal == null)
                Finish(ExecutorState.Succeeded, null);
            else
                Finish(ExecutorState.Failed, PlanTexts.GoalNotMet);
        }

        // ═══════════════════════════════════════════════════════════
        // 步骤执行
        // ═══════════════════════════════════════════════════════════

        /// <summary>步骤目标的人类可读描述（日志用）：" → player" / " → query:nearest_enemy(self)" / 空。</summary>
        private static string RenderStepTarget(PlanStep step)
        {
            if (step?.Target == null) return "";
            if (step.Target.Type == JTokenType.String) return $" → {step.Target.ToString()}";
            if (step.Target.Type == JTokenType.Object && step.Target["query"] != null)
                return $" → query:{step.Target["query"]}";
            return "";
        }

        private void TickCursor(ActorCursor cursor, float dt)
        {
            // 循环段入口（loop 先于 steps 主链执行；§5.0 循环段）
            if (cursor.LoopEntryPending)
            {
                cursor.LoopEntryPending = false;
                cursor.LoopMode = true;
                cursor.Sequence = PlanExecutorHelpers.CollectLoopSteps(Plan, cursor.ActorId);
                cursor.Index = 0;
                cursor.StepElapsed = 0f;
                cursor.ClearSubAction();
                return;
            }

            var step = cursor.Current;
            if (step == null)
            {
                cursor.Done = true;
                cursor.ClearSubAction();
                return;
            }

            // when 前置门控（GATE）：不成立 = 等待（超时照常累计——门控步永不挂死，§5.4）
            if (step.When != null && !_world.Evaluate(step.When, cursor.Agent))
            {
                cursor.StepElapsed += dt;
                if (step.TimeoutS > 0f && cursor.StepElapsed > step.TimeoutS && !PlanStep.IsUnboundedStep(step))
                {
                    HandleStepTimeout(cursor, step);
                }
                return;
            }

            // 事件通道消费（本步执行期间收到决策结果事件 → 即时跳转；步骤开始前的事件 = 过期丢弃）
            if (_eventQueue.Count > 0)
            {
                var stepStart = _stepStartTime;
                for (int i = _eventQueue.Count - 1; i >= 0; i--)
                {
                    var ev = _eventQueue[i];
                    if (ev.Time < stepStart)
                    {
                        _eventQueue.RemoveAt(i);   // 过期事件
                        continue;
                    }
                    if (step.OnEvent != null)
                    {
                        var match = step.OnEvent.FirstOrDefault(e => e != null && string.Equals(e.Type, ev.Type, StringComparison.OrdinalIgnoreCase));
                        if (match != null && !string.IsNullOrEmpty(match.Then))
                        {
                            CurrentSummary = $"{step.Id} 收到决策结果({ev.Type})";
                            DebugLogger.Log($"[PlanExecutor] {OwnerAgent?.Name}: {step.Id} on_event {ev.Type} → {match.Then}");
                            _eventQueue.Clear();
                            Jump(cursor, match.Then);
                            return;
                        }
                    }
                }
            }

            cursor.StepElapsed += dt;
            CurrentSummary = BuildStepSummary(step);

            // 子动作/内联步骤创建（每步仅创建一次 → 恰好每步打一条开始日志）
            if (cursor.SubAction == null && cursor.Inline == null)
            {
                DebugLogger.Log($"[PlanExecutor] {OwnerAgent?.Name}: ▶ 步骤 {step.Id} 开始（{step.Action}{RenderStepTarget(step)}）");
                if (!TryCreateSubAction(cursor, step))
                {
                    // 创建失败（不可解析目标/未实现动作）→ 按超时处理（不静默）
                    HandleStepTimeout(cursor, step);
                    return;
                }
                if (cursor.SubAction != null)
                    cursor.SubAction.OnStart(cursor.Agent);
            }

            // until 提前完成（动作步骤）或退出条件（wait 步骤）
            if (step.Until != null && _world.Evaluate(step.Until, cursor.Agent))
            {
                CompleteStep(cursor, step);
                return;
            }

            // 内联步骤驱动
            if (cursor.Inline != null)
            {
                cursor.Inline.OnTick(dt);
                if (cursor.Inline.Finished)
                {
                    CompleteStep(cursor, step);
                }
                return;
            }

            // 子动作驱动
            if (cursor.SubAction != null)
            {
                cursor.SubAction.OnTick(cursor.Agent, dt);
                if (cursor.SubAction.IsFinished(cursor.Agent))
                {
                    CompleteStep(cursor, step);
                    return;
                }
            }

            // 超时（保持型/无限等待步骤豁免）
            if (step.TimeoutS > 0f && cursor.StepElapsed > step.TimeoutS && !PlanStep.IsUnboundedStep(step))
            {
                HandleStepTimeout(cursor, step);
            }
        }

        private void CompleteStep(ActorCursor cursor, PlanStep step)
        {
            cursor.ClearSubAction();
            cursor.StepElapsed = 0f;
            _stepStartTime = Elapsed;
            _world.MarkStepComplete(step.Id, Elapsed);
            DebugLogger.Log($"[PlanExecutor] {OwnerAgent?.Name}: 步骤 {step.Id} 完成（{step.Action}）");
            // 事件队列不整体清空：步骤切换后，本步期间到达的决策事件（say_to 广播 → 守卫演算）留给下一步 on_event 消费
            // （消费逻辑按 _stepStartTime 过滤过期事件）

            // 判定型原子结果路由（steal_attempt/negotiate/duel 的 result{} 路由，§5.0 缺口 2）
            if (!string.IsNullOrEmpty(_stepResultKey))
            {
                var route = step.ResultRoute(_stepResultKey);
                _stepResultKey = null;
                if (!string.IsNullOrEmpty(route))
                {
                    Jump(cursor, route);
                    return;
                }
            }

            // 循环段内：步骤完成 → 检查循环退出或回顶
            if (cursor.LoopMode)
            {
                if (cursor.Index >= cursor.Sequence.Count - 1)
                {
                    // 循环段走完 → 求值 loop.until（达成 = 正常退出；未达成 = 回顶）
                    var loop = Plan.Loop;
                    if (loop?.Until == null || _world.Evaluate(loop.Until, cursor.Agent))
                    {
                        ExitLoop(cursor);
                    }
                    else
                    {
                        cursor.Index = 0;
                    }
                    return;
                }
                cursor.Index++;
                return;
            }

            // 步骤完成 → goal 检查（收尾检查放在"步骤完成时"）
            CheckGoal();

            // on_success 显式跳转 / 缺省顺序下一歩
            if (!string.IsNullOrEmpty(step.OnSuccess))
            {
                Jump(cursor, step.OnSuccess);
                return;
            }

            cursor.Index++;
            if (cursor.Index >= cursor.Sequence.Count)
            {
                if (cursor.InFallback)
                {
                    // 预案尾完成 → 回收尾判定（不溢出到下一个预案）
                    cursor.Done = true;
                    if (_goalMet) Finish(ExecutorState.Succeeded, null);
                    else Finish(ExecutorState.Failed, "事情没办成");
                    return;
                }
                cursor.Done = true;
            }
        }

        private void HandleStepTimeout(ActorCursor cursor, PlanStep step)
        {
            cursor.ClearSubAction();
            cursor.StepElapsed = 0f;
            DebugLogger.Log($"[PlanExecutor] {OwnerAgent?.Name}: 步骤 {step.Id} 超时");

            // 循环段内：超时 = 本步失败 → 回循环顶重新求值 loop.until（§5.0 四层退出②）
            if (cursor.LoopMode && string.IsNullOrEmpty(step.OnTimeout))
            {
                var loop = Plan.Loop;
                if (loop?.Until != null && _world.Evaluate(loop.Until, cursor.Agent))
                {
                    ExitLoop(cursor);
                    return;
                }
                cursor.Index = 0;
                return;
            }

            if (!string.IsNullOrEmpty(step.OnTimeout))
            {
                Jump(cursor, step.OnTimeout);
                return;
            }
            // 缺省 → @abort_gracefully
            Abort(PlanTexts.StepTimeout);
        }

        private void ExitLoop(ActorCursor cursor)
        {
            cursor.LoopMode = false;
            cursor.Sequence = CollectStepsForActor(Plan, cursor.ActorId, cursor.Agent);
            cursor.Index = 0;
            cursor.Done = cursor.Sequence.Count == 0;
            cursor.ClearSubAction();
            // 循环正常退出 → goal 检查（N/P 的 goal = count 归零）
            CheckGoal();
        }

        /// <summary>跳转（on_timeout/on_success/on_event/contingency/result 路由共用）。</summary>
        private void Jump(ActorCursor cursor, string target)
        {
            if (string.IsNullOrEmpty(target)) return;
            if (target.StartsWith("@"))
            {
                if (target == "@abort_gracefully") Abort(PlanTexts.Aborted);
                return;
            }
            DebugLogger.Log($"[PlanExecutor] {OwnerAgent?.Name}: 跳转 → {target}");

            // 优先找 fallback 入口（只允许跳入口步，S3）
            if (Plan.Fallbacks != null)
            {
                for (int i = 0; i < Plan.Fallbacks.Count; i++)
                {
                    var entry = Plan.Fallbacks[i];
                    if (entry == null || entry.Count == 0) continue;
                    if (entry[0].Id == target)
                    {
                        cursor.InFallback = true;
                        cursor.LoopMode = false;
                        cursor.Sequence = entry;
                        cursor.Index = 0;
                        cursor.StepElapsed = 0f;
                        cursor.ClearSubAction();
                        return;
                    }
                }
            }
            // 循环段内跳转
            if (cursor.LoopMode && Plan.Loop?.Steps != null)
            {
                for (int i = 0; i < Plan.Loop.Steps.Count; i++)
                {
                    if (Plan.Loop.Steps[i]?.Id == target)
                    {
                        cursor.Index = i;
                        cursor.StepElapsed = 0f;
                        cursor.ClearSubAction();
                        return;
                    }
                }
            }
            // 主链步骤（含跳回主链：预案 → 主链）
            cursor.InFallback = false;
            var mainSeq = CollectStepsForActor(Plan, cursor.ActorId, cursor.Agent);
            for (int i = 0; i < mainSeq.Count; i++)
            {
                if (mainSeq[i]?.Id == target)
                {
                    cursor.Sequence = mainSeq;
                    cursor.Index = i;
                    cursor.StepElapsed = 0f;
                    cursor.ClearSubAction();
                    return;
                }
            }
            // 目标不存在 → 计划跳转目标缺失（LLM 幻觉 ID 漏网）→ 中止报告，不静默继续（§5.1 铁律）
            DebugLogger.Log($"[PlanExecutor] 跳转目标不存在: {target} → 计划中止");
            Abort(PlanTexts.BadJump);
        }

        // ═══════════════════════════════════════════════════════════
        // 子动作（原子行为 / 内联步骤）
        // ═══════════════════════════════════════════════════════════

        private bool TryCreateSubAction(ActorCursor cursor, PlanStep step)
        {
            var agent = cursor.Agent;
            if (agent == null) return false;

            // ── 执行器内联步骤（编排逻辑属于执行器）──
            switch (step.Action)
            {
                case "say_to":
                    cursor.Inline = new SayInlineState(this, cursor, step);
                    return cursor.Inline.Ok;
                case "wait":
                    cursor.Inline = new WaitInlineState(step);
                    return true;
                case "signal_player":
                    cursor.Inline = new SignalInlineState(this, step);
                    return true;
                case "end_plan":
                    cursor.Inline = new EndPlanInlineState(this, step);
                    return true;
                case "emote":
                    cursor.Inline = new EmoteInlineState(agent, step);
                    return true;
                case "make_noise":
                    cursor.Inline = new MakeNoiseInlineState(this, cursor);
                    return true;
                case "lead":
                    cursor.Inline = new LeadInlineState(this, cursor, step);
                    return cursor.Inline.Ok;
                case "steal_attempt":
                    cursor.Inline = new StealAttemptInlineState(this, cursor, step);
                    return cursor.Inline.Ok;
                case "give_item":
                case "give_gold":
                    cursor.Inline = new GiveInlineState(this, cursor, step);
                    return cursor.Inline.Ok;
                case "deliver_item":
                    cursor.Inline = new DeliverInlineState(this, cursor, step);
                    return cursor.Inline.Ok;
                case "knockout":
                    cursor.Inline = new KnockoutInlineState(this, cursor, step);
                    return cursor.Inline.Ok;
            }

            // ── IAtomicAction 子动作（复用引擎级原子行为）──
            switch (step.Action)
            {
                case "move_to":
                    {
                        if (!ResolveStepTarget(step, cursor, out Vec3 pos, out Vec2 dir)) return false;
                        float within = step.Within > 0f ? step.Within : 2.0f;
                        cursor.SubAction = new MoveToPositionAction(pos, dir, false, within);
                        return true;
                    }
                case "follow":
                    {
                        if (!ResolveStepAgent(step, cursor, out Agent target)) return false;
                        float radius = 2.0f;
                        float angleOffset = 0f;
                        float stopDistance = 3.5f;
                        if (!string.IsNullOrEmpty(step.RelPos))
                        {
                            switch (step.RelPos.ToLowerInvariant())
                            {
                                case "behind": angleOffset = 180f; radius = 1.5f; break;
                                case "left": angleOffset = 90f; break;
                                case "right": angleOffset = -90f; break;
                                case "line": angleOffset = 0f; radius = 2.5f; break;
                            }
                        }
                        bool keep = step.TimeoutS <= 0f;   // follow 省略 timeout = 无限保持（O1）
                        cursor.SubAction = new FollowAgentAction(target, false, radius, angleOffset, stopDistance, keepFollow: keep);
                        return true;
                    }
                case "stop_following":
                    {
                        // 执行器接管期间默认跟随不会启动；此步 = 语义化清理（原地等待）
                        cursor.SubAction = new StayAction(null, false);
                        return true;
                    }
                case "order_attack":
                    {
                        if (!ResolveStepAgent(step, cursor, out Agent target)) return false;
                        cursor.SubAction = new FightEnemyAction(target);
                        return true;
                    }
                case "face":
                    {
                        if (!ResolveStepAgent(step, cursor, out Agent target)) return false;
                        if (agent == target) return false;
                        Vec2 dir = (target.Position.AsVec2 - agent.Position.AsVec2).Normalized();
                        if (dir.LengthSquared < 0.01f) dir = target.LookDirection.AsVec2.Normalized();
                        cursor.SubAction = new TurnToDirectionAction(dir);
                        return true;
                    }
                case "look_at":
                    {
                        if (!ResolveStepAgent(step, cursor, out Agent target)) return false;
                        float seconds = step.Seconds > 0f ? step.Seconds : 2.0f;
                        cursor.SubAction = new LookAtAction(target, seconds);
                        return true;
                    }
                case "shadow":
                case "negotiate":
                case "duel":
                    // v2 扩展：未实现前明确失败（走 on_timeout/失败路径，不静默）
                    DebugLogger.Log($"[PlanExecutor] 动作 {step.Action}（{step.Id}）尚未实现 → 步骤失败");
                    return false;
                default:
                    DebugLogger.Log($"[PlanExecutor] 未知动作 {step.Action}（{step.Id}）→ 步骤失败");
                    return false;
            }
        }

        private bool ResolveStepTarget(PlanStep step, ActorCursor cursor, out Vec3 pos, out Vec2 dir)
        {
            pos = Vec3.Zero;
            dir = Vec2.Zero;
            if (step.Target == null) return false;
            string refName = PlanRefUtil.Normalize(step.Target, out string query);
            if (query != null) refName = query;
            if (string.IsNullOrEmpty(refName)) return false;
            if (!_world.TryResolvePosition(refName, cursor.Agent, out pos)) return false;
            // 目标为 agent 时朝向它
            if (_world.TryResolveAgent(refName, cursor.Agent, out Agent target))
            {
                if (target != cursor.Agent)
                    dir = (target.Position.AsVec2 - cursor.Agent.Position.AsVec2).Normalized();
            }
            return true;
        }

        private bool ResolveStepAgent(PlanStep step, ActorCursor cursor, out Agent target)
        {
            target = null;
            if (step.Target == null) return false;
            string refName = PlanRefUtil.Normalize(step.Target, out string query);
            if (query != null) refName = query;
            if (string.IsNullOrEmpty(refName)) return false;
            return _world.TryResolveAgent(refName, cursor.Agent, out target);
        }

        // ═══════════════════════════════════════════════════════════
        // contingencies / triggers / guardrails
        // ═══════════════════════════════════════════════════════════

        private void TickContingencies()
        {
            if (Plan.Contingencies == null) return;
            foreach (var c in Plan.Contingencies)
            {
                if (c?.When == null || string.IsNullOrEmpty(c.Then)) continue;
                bool now = _world.Evaluate(c.When, OwnerAgent);
                bool prev = _contingencyPrev.TryGetValue(c, out bool p) && p;
                _contingencyPrev[c] = now;
                if (!now || prev) continue;
                if (c.OneShot && _oneShotFired.Contains(c)) continue;
                if (c.OneShot) _oneShotFired.Add(c);
                DebugLogger.Log($"[PlanExecutor] contingency 触发: {c.Then}");
                // was 修饰的条件触发后清除其"曾成立"记录（§5.2）：
                // 掉线类条件（E/F 的 seeing was:true）触发预案后，恢复 → 再掉线 → 可再次触发（one_shot: false 语义）
                _world.ForgetWasEver(RuntimeWorldState.ConditionKey(c.When));
                Jump(_selfCursor, c.Then);
                if (State != ExecutorState.Executing) return;
            }
        }

        private void TickTriggers()
        {
            if (Plan.Triggers == null) return;
            foreach (var t in Plan.Triggers)
            {
                if (t?.When == null || t.Then == null) continue;
                bool now = _world.Evaluate(t.When, OwnerAgent);
                bool prev = _triggerPrev.TryGetValue(t, out bool p) && p;
                _triggerPrev[t] = now;
                if (!now || prev) continue;   // 上升沿
                // TRIGGER：signal_player 报告，计划不结束（可重复触发）
                if (t.Then.Action == "signal_player" && !string.IsNullOrEmpty(t.Then.Text))
                {
                    SignalPlayer(t.Then.Text);
                    DebugLogger.Log($"[PlanExecutor] TRIGGER 触发: {t.Then.Text}");
                }
            }
        }

        private void TickGuardrails(float dt)
        {
            // R2: 执行者死亡/离场 → Abort（战斗意图：目标死亡 = GOAL 达成，不触发本规则）
            if (!OwnerAgent.IsActive())
            {
                Abort(PlanTexts.CompanionDown);
                return;
            }

            // R5: 计划目标变为敌对 → Abort + 报告；豁免：战斗意图 / contingencies 已声明 combat
            if (!GoalTemplates.IsCombatIntent(IntentType) && !PlanDeclaresCombat())
            {
                foreach (var kv in _world.RoleAgents)
                {
                    var a = kv.Value;
                    if (a == null || a == OwnerAgent || a == Agent.Main) continue;
                    if (!a.IsActive()) continue;
                    var brain = AgentAIController.GetBrainForAgent(a);
                    if (brain == null) continue;
                    try
                    {
                        // 与玩家开战 或 与随从本人开战（knockout 失败反被攻击等）→ 均可 replan
                        bool hostile = brain.IsInCombat
                            && (a.GetTargetAgent() == Agent.Main || a.GetTargetAgent() == OwnerAgent);
                        if (hostile)
                        {
                            // 守卫和玩家/随从打起来 → Abort + 报告（可 replan，§7.2）
                            Abort(PlanTexts.FightBrokeOut, allowReplan: true);
                            return;
                        }
                    }
                    catch { }
                }
            }

            // R6: 总时长 > 5 分钟 → Abort（事件驱动计划豁免：LOOKOUT/SHADOW 无限期待命）
            if (Elapsed > 300f && !GoalTemplates.IsEventDriven(IntentType))
            {
                Abort(PlanTexts.TooLong);
            }
        }

        private bool PlanDeclaresCombat()
        {
            if (Plan.Contingencies == null) return false;
            foreach (var c in Plan.Contingencies)
            {
                if (c?.When != null && ContainsPredicate(c.When, "combat")) return true;
            }
            return false;
        }

        private static bool ContainsPredicate(Condition c, string type)
        {
            if (c == null) return false;
            if (string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase)) return true;
            if (c.Conditions != null)
                foreach (var sub in c.Conditions)
                    if (ContainsPredicate(sub, type)) return true;
            return false;
        }

        private static bool IsPlayerInCombat()
        {
            try
            {
                var brain = AgentAIController.GetBrainForAgent(Agent.Main);
                return brain != null && brain.IsInCombat;
            }
            catch { return false; }
        }

        private static bool DetectPlayerModalUi()
        {
            try
            {
                var mission = Mission.Current;
                if (mission == null) return false;
                if (mission.Mode == MissionMode.Conversation
                    || mission.Mode == MissionMode.Barter) return true;
                if (AlertForceConversationAction.ActiveConversationAgent != null) return true;
                // 偷窃条子弹时间（玩家控制冻结）
                if (InteractionMissionView.Instance != null && InteractionMissionView.Instance.IsPlayerControlFrozen)
                    return true;
                return false;
            }
            catch { return false; }
        }

        // ═══════════════════════════════════════════════════════════
        // 暂停 / 恢复 / 中止 / 收尾
        // ═══════════════════════════════════════════════════════════

        public void Pause(string reason)
        {
            if (State == ExecutorState.Paused) return;
            State = ExecutorState.Paused;
            PauseReason = reason;
            // 只清原子动作（SubAction），保留内联步骤状态（Inline）——恢复后 say_to 不重播、wait 不重计时（§5.4 Paused 可恢复）
            foreach (var c in _cursors)
            {
                if (c.SubAction != null)
                {
                    try { c.SubAction.OnEnd(c.Agent); } catch { }
                    c.SubAction = null;
                }
            }
            CurrentSummary = reason;
            DebugLogger.Log($"[PlanExecutor] 暂停: {reason}");
        }

        public void Resume()
        {
            if (State != ExecutorState.Paused) return;
            State = ExecutorState.Executing;
            PauseReason = null;
            CurrentSummary = Summary ?? "";
            DebugLogger.Log($"[PlanExecutor] 恢复执行");
        }

        /// <summary>玩家停止键/新命令（R3）：旧计划作废，收尾为中断。</summary>
        public void CancelByPlayer(string reason = "玩家叫停")
        {
            if (IsFinished) return;
            Finish(ExecutorState.Aborted, reason, needFaceReport: false, silent: true);
        }

        /// <summary>外部打断（brain 队列中断）。</summary>
        public void RequestInterrupt()
        {
            if (IsFinished) return;
            Finish(ExecutorState.Aborted, PlanTexts.Interrupted, needFaceReport: false, silent: true);
        }

        private void Abort(string message, bool allowReplan = false)
        {
            if (IsFinished) return;
            DebugLogger.Log($"[PlanExecutor] 中止: {message}");
            if (allowReplan)
                EventLog.Add($"{Elapsed:F0}s: {message}（步骤 {_selfCursor?.Current?.Id} 未能完成）");
            Finish(ExecutorState.Aborted, message, needFaceReport: false);
            if (allowReplan)
                OnAborted?.Invoke(this, message);
        }

        /// <summary>lead 等内联步骤的"当面报告后中止"路径。</summary>
        internal void AbortWithReport(string message)
        {
            if (IsFinished) return;
            Finish(ExecutorState.Failed, message, needFaceReport: true);
        }

        /// <summary>收尾三路一函数（成功/失败/中断统一收口）。</summary>
        private void Finish(ExecutorState state, string message, bool needFaceReport = false, bool silent = false)
        {
            if (IsFinished) return;
            State = state;
            EndMessage = message;
            IsFinished = true;
            DebugLogger.Log($"[PlanExecutor] {OwnerAgent?.Name}: 计划结束（{state}）: {message}");
            foreach (var c in _cursors) c.ClearSubAction();

            if (silent || string.IsNullOrEmpty(message))
            {
                FinalizeExecutor(message);
                return;
            }

            if (needFaceReport)
            {
                // 当面报告：随从恢复默认跟随走回玩家 ~3m 冒泡转述再彻底收尾（§5.4）
                _pendingReport = message;
                _reportPending = true;
                _reportTimer = 0f;
                _reportSpoken = false;
                AgentControlHelper.ForceUnlockAgent(OwnerAgent);
                // brain 队列已出队 → DecideDefaultBehavior 自动恢复跟随
            }
            else
            {
                // 密信报告（脱不开身 / 紧急中断）
                SignalPlayer(message);
                FinalizeExecutor(message);
            }
        }

        /// <summary>end_plan 步骤（result + report）。</summary>
        internal void ApplyEndPlan(PlanStep step, string result)
        {
            bool success = result == "success";
            string report = step.Report ?? step.TextOrContent;
            if (!success && string.IsNullOrEmpty(report))
            {
                // 失败且无报告 → 仅收尾不冒泡
                Finish(ExecutorState.Failed, null);
                return;
            }
            Finish(success ? ExecutorState.Succeeded : ExecutorState.Failed,
                report ?? "完成了", needFaceReport: !string.IsNullOrEmpty(report));
        }

        private void TickReport(float dt)
        {
            _reportTimer += dt;
            var player = Agent.Main;
            if (player != null && player.IsActive() && OwnerAgent.IsActive()
                && OwnerAgent.Position.Distance(player.Position) < 3.0f)
            {
                if (!_reportSpoken)
                {
                    _reportSpoken = true;
                    try
                    {
                        AgentControlHelper.FaceToActor(OwnerAgent, player);
                        AgentHudMissionView.AgentSay(OwnerAgent, _pendingReport);
                        DebugLogger.Log($"[PlanExecutor] 当面报告: {_pendingReport}");
                    }
                    catch { }
                }
                if (_reportTimer > 3f)
                {
                    FinalizeExecutor(_pendingReport);
                }
            }
            else if (_reportTimer > 60f)
            {
                // 超时兜底：密信
                SignalPlayer(_pendingReport);
                FinalizeExecutor(_pendingReport);
            }
        }

        private void FinalizeExecutor(string message)
        {
            if (_finalized) return;
            _finalized = true;
            var owner = OwnerAgent;
            // 收尾统一释放：清脚本锁（DecideDefaultBehavior 恢复跟随/原版 AI 不被残留锁卡住）
            try
            {
                if (owner != null && owner.IsActive())
                    AgentControlHelper.ForceUnlockAgent(owner);
            }
            catch { }
            var evt = OnFinished;
            ActiveExecutors.Remove(owner);
            if (Instance == this) Instance = null;
            evt?.Invoke(this);
        }

        // ═══════════════════════════════════════════════════════════
        // 工具
        // ═══════════════════════════════════════════════════════════

        internal void SignalPlayer(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                // 密信 = 非模态即时信号（NinjaNotification 是模态且锁鼠标，会挡玩家操作）
                InformationManager.DisplayMessage(new InformationMessage(text, Colors.Yellow));
                DebugLogger.Log($"[PlanExecutor] 密信: {text}");
            }
            catch { }
        }

        private void CheckGoal()
        {
            if (_goalMet) return;
            if (Plan?.Goal != null && _world.Evaluate(Plan.Goal, OwnerAgent))
            {
                _goalMet = true;
                CurrentSummary = "目标达成";
                DebugLogger.Log($"[PlanExecutor] GOAL 达成");
            }
        }

        private string BuildStepSummary(PlanStep step)
        {
            if (step == null) return Summary ?? "";
            switch (step.Action)
            {
                case "move_to": return "正在前往目标地点";
                case "say_to": return step.Text != null ? $"去说：{step.Text}" : "正在搭话";
                case "wait": return "等待时机";
                case "order_attack": return "正在交手";
                case "signal_player": return "准备报告";
                case "steal_attempt": return "准备下手";
                default: return $"执行中（{step.Action}）";
            }
        }

        internal static CommandIntentType ParseIntentType(string s)
        {
            if (string.IsNullOrEmpty(s)) return CommandIntentType.Custom;
            // 下划线别名兼容：few-shot 里模型可能抄 TALK_TO/DRIVE_AWAY（下划线格式），
            // 词表输出与解析都归一化到去掉下划线再匹配，两种写法都能解析。
            string norm = s.Replace("_", "");
            foreach (CommandIntentType t in Enum.GetValues(typeof(CommandIntentType)))
            {
                if (string.Equals(t.ToString(), norm, StringComparison.OrdinalIgnoreCase)) return t;
            }
            return CommandIntentType.Custom;
        }

        internal void NotifyDecisionEvent(string eventType)
        {
            _eventQueue.Add((Elapsed, eventType));
        }

        internal void NotifySayDone(PlanStep step, Agent target)
        {
            // 占位：say_to 完成钩子（M3 ReactiveAgent 演算后的后续钩子）
        }

        internal void SetStepResultKey(string key) => _stepResultKey = key;
        internal void RecordStolenGold(float amount) => _stolenGold = amount;
        internal float StolenGold => _stolenGold;

        // 批量击晕计数（收尾报告用）
        private int _knockoutCount;
        internal void IncrementKnockoutCount() => _knockoutCount++;
        internal int KnockoutCount => _knockoutCount;

        // 扒窃源（守恒转移用：目标 Hero 钱包 → 玩家）
        private TaleWorlds.CampaignSystem.Hero _stolenSource;
        internal void RecordStolenSource(TaleWorlds.CampaignSystem.Hero source) => _stolenSource = source;
        internal TaleWorlds.CampaignSystem.Hero StolenSource => _stolenSource;

        // 物变体赃物（箱子记账语义）
        private string _stolenItem;
        internal void RecordStolenItem(string id, string name) => _stolenItem = name;
        internal string StolenItem => _stolenItem;

        /// <summary>内联步骤显式失败（knockout 失败/目标离场等）：走步骤失败路径（on_timeout 或 abort）。</summary>
        internal void FailStep(ActorCursor cursor, PlanStep step)
        {
            HandleStepTimeout(cursor, step);
        }

        /// <summary>主执行者游标当前步骤序号（调试/step 指令用）。</summary>
        public int SelfCursorIndex => _selfCursor?.Index ?? 0;

        /// <summary>调试强制 replan（plan_debug replan 指令）：以 R5 语义中止并触发 Replan 链路。</summary>
        internal void AbortForReplanDebug(string message)
        {
            if (IsFinished) return;
            Abort(message, allowReplan: true);
        }

        /// <summary>R4 豁免：当前步骤 target/zone 远离玩家 > 30m（独行任务不叫回）。</summary>
        private bool IsCurrentStepRemote()
        {
            var step = _selfCursor?.Current;
            if (step == null || step.Target == null) return false;
            var player = Agent.Main;
            if (player == null) return false;
            string refName = PlanRefUtil.Normalize(step.Target, out string query);
            if (query != null) refName = query;
            if (string.IsNullOrEmpty(refName)) return false;
            if (_world.TryResolvePosition(refName, OwnerAgent, out Vec3 pos))
                return pos.Distance(player.Position) > 30f;
            return false;
        }

        private void TickChaseBack(float dt)
        {
            var player = Agent.Main;
            if (player == null || !player.IsActive()) return;
            if (!OwnerAgent.IsActive()) { Abort("随从倒下了"); return; }
            float dist = OwnerAgent.Position.Distance(player.Position);
            if (dist < 20f)
            {
                Resume();
                return;
            }
            // 追回玩家身边
            AgentControlHelper.ScriptedMoveToPoint(OwnerAgent, player.Position, true);
            CurrentSummary = "追上玩家";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ActorCursor — 每 actor 一个游标（并行推进；actor 内串行）
    // ═══════════════════════════════════════════════════════════════

    public class ActorCursor
    {
        public string ActorId;
        public Agent Agent;
        public List<PlanStep> Sequence;
        public int Index;
        public bool InFallback;
        public bool LoopMode;
        public bool LoopEntryPending;       // 主链有循环段（先跑循环）
        public float StepElapsed;
        public bool Done;
        public IAtomicAction SubAction;
        public IInlineStep Inline;

        public PlanStep Current => Index >= 0 && Index < Sequence.Count ? Sequence[Index] : null;

        public void ClearSubAction()
        {
            if (SubAction != null)
            {
                try { SubAction.OnEnd(Agent); } catch { }
                SubAction = null;
            }
            Inline = null;
        }
    }

    /// <summary>循环段步骤收集（loop.steps 按 actor 过滤）。</summary>
    public static class PlanExecutorHelpers
    {
        internal static List<PlanStep> CollectLoopSteps(Plan plan, string actorId)
        {
            var result = new List<PlanStep>();
            if (plan?.Loop?.Steps == null) return result;
            foreach (var s in plan.Loop.Steps)
            {
                if (s == null) continue;
                string a = string.IsNullOrEmpty(s.Actor) ? "self" : s.Actor;
                if (a == actorId || a == "all") result.Add(s);
            }
            return result;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ExecutePlanAction — brain 队列挂载（IAtomicAction）
    // ═══════════════════════════════════════════════════════════════

    public class ExecutePlanAction : IAtomicAction
    {
        private readonly PlanExecutor _executor;
        public PlanExecutor Executor => _executor;

        public ExecutePlanAction(PlanExecutor executor)
        {
            _executor = executor;
        }

        public void OnStart(Agent agent)
        {
            _executor?.Start(agent);
        }

        public void OnTick(Agent agent, float dt)
        {
            // 执行器由 AgentAIController.TickAll 统一驱动（与队列解耦，报告流程也能跑）
        }

        public bool IsFinished(Agent agent)
        {
            return _executor == null || _executor.IsFinished;
        }

        public void OnEnd(Agent agent)
        {
            // brain 标准清理后：恢复默认行为由 DecideDefaultBehavior 自动处理
        }

        public void RequestInterrupt()
        {
            _executor?.RequestInterrupt();
        }
    }
}
