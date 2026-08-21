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
        // 本地化：判定型动作结局默认成功出口（M5：有结局必有出口）
        public static string Done => LWNTextHelper.ResolveText("LWN_plan_done", "It is done.");
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
    // 🔴 单脑化重构（D1/D4b）：执行器降级为纯排序器——一切表现层动作（移动/朝向/视线/
    // 战斗/姿势）入队由 brain 驱动（OnStart/OnTick/OnEnd/IsFinished 生命周期归脑），
    // 执行器只做 100ms 轮询完成检测 + 三路径判定（IsFinished 正常完成 / IsActionAlive
    // 外部清除 / RequestInterrupt 主动中断）。占位动作 ExecutePlanAction 已删，
    // "计划执行中"哨兵 = ExecutingCommand 意图（AgentBrain D2 空窗守卫）。
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
            // 心跳日志（1s 一次，只在计划活跃时打——空闲时每秒刷屏无意义）：
            // 区分"OnMissionTick 没调 TickAll"（整链死）和"foreach 没驱动到执行器"（注册表/循环死）。
            if (ActiveExecutors.Count > 0)
            {
                _tickAllHeartbeatAccum += dt;
                if (_tickAllHeartbeatAccum >= 1f)
                {
                    _tickAllHeartbeatAccum = 0f;
                    //DebugLogger.Log($"[PlanExecutor] TickAll 心跳: {ActiveExecutors.Count} 个执行器活跃");
                }
            }
            else
            {
                _tickAllHeartbeatAccum = 0f;
            }

            if (ActiveExecutors.Count == 0) return;
            foreach (var e in ActiveExecutors.Values.ToList())
                e.Tick(dt);
        }

        /// <summary>Mission 结束统一收尾（OnMissionScreenFinalize 兜底纪律）。</summary>
        public static void ShutdownAll()
        {
            foreach (var e in ActiveExecutors.Values.ToList())
                // 本地化：场景强制收尾消息（铁律 13 走 LWN_plan_abort_scene_end）
                e.FinalizeExecutor(LWNTextHelper.ResolveText("LWN_plan_abort_scene_end", "The plan ended as the scene closed."));
            ActiveExecutors.Clear();
            Instance = null;
        }
        // ── 主执行者信息 ──
        public Agent OwnerAgent { get; private set; }
        public Plan Plan { get; private set; }
        public CommandIntentType IntentType { get; private set; }
        public string Summary { get; private set; }
        public ExecutorState State = ExecutorState.Executing;
        // 🔴 暂停原因是状态标识符（Resume/追回匹配用），禁止换本地化文本；玩家可见文本在 Pause() 内按常量映射本地化
        public const string PauseReasonModal = "player_modal";
        public const string PauseReasonFight = "player_fight";
        public const string PauseReasonFar = "player_far";
        public string PauseReason;
        public string EndMessage;       // 收尾消息（报告文本）
        public string CurrentSummary;   // 执行摘要（HUD 状态行）
        public float Elapsed;
        public bool IsFinished { get; private set; }        // 收尾标记（Finish 置位；TickInner 据此停摆）
        public bool IsPlayerInModalUi { get; private set; } // R7
        public event Action<PlanExecutor> OnFinished;       // 收尾通知（brain 意图复位 None）
        public event Action<PlanExecutor, string> OnAborted; // 中止通知（Replan 低频重入监听，§7.2）
        // 🔴 2026-08-10（im-command-action-upgrade.md §2.1）：步骤执行完成事件——全部步骤完成路径的
        // 唯一汇合点（CompleteStep）。IM 侧挂接写执行者记忆（plan_step 单向链条），零侵入既有执行器逻辑。
        public event Action<PlanExecutor, Agent, PlanStep> OnStepCompleted;   // (executor, 完成该步的执行者 agent, step)
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
        private float _heartbeatAccum;
        private static float _tickAllHeartbeatAccum;
        private bool _goalMet;
        private bool _reportPending;
        private string _pendingReport;
        private float _reportTimer;
        private bool _reportSpoken;
        private bool _finalized;
        private string _stepResultKey;                      // 判定型原子结果（result 路由）
        private float _stolenGold;                          // steal_attempt 成功所得（give_gold "stolen"）
        // 🔴 2026-08-14（npc-risk-aware-planning.md M2d/M5）：钱袋路径当场移交标记 + 结局已播标记
        private bool _goldHanded;                           // 模板 NPC 钱袋路径已守恒移交（give_gold 防双移交）
        private bool _resultBroadcast;                      // 判定型结局已由 InlineSteps 播报（Finish 不重复播）
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
            // 🔴 2026-08-13：Custom 意图（闲聊动作单步包装，ChatActionFlow）跳过——任务型质量检查
            // 对它无意义（按构造即 1 步无 goal），实机日志把「主链仅1步/缺goal」警告打在闲聊动作上，
            // 误导排查误以为生成了需要批准的任务计划。LLM 计划路径在 ImCommandFlow 已拒收 CUSTOM 意图。
            if (parsed != CommandIntentType.Custom)
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
            // 🔴 2026-08-13：Custom 意图 = 闲聊动作的单步机械包裹（ChatActionFlow）——日志显式标注，
            // 与 LLM 生成的任务计划区分（实机日志曾误读为"模型生成了计划"；模型只给动作码，计划壳是 C# 包的）
            string wrapperNote = IntentType == CommandIntentType.Custom
                ? "（闲聊单动作包裹：非 LLM 计划，直接执行无需玩家批准）" : ""; // lwn-ignore: A 日志内容（249 行 DebugLogger 使用）
            DebugLogger.Log($"[PlanExecutor] 开始执行计划（{IntentType}）: {Summary}{wrapperNote}");
        }
        public void Tick(float dt)
        {
            // 心跳日志（1s 一次，放最开头）：诊断"原地不动"——Tick 只要被调用就会打，
            // 与节流/暂停/异常路径无关，直接区分"执行器没被驱动"和"驱动了但卡住"。
            _heartbeatAccum += dt;
            if (_heartbeatAccum >= 1f)
            {
                _heartbeatAccum = 0f;
                LogHeartbeat();
            }
            try
            {
                TickInner(dt);
            }
            catch (Exception ex)
            {
                // 诊断：Tick 链静默死亡 → NPC 被钉在原地（latch 永不完成）。
                // 抓到异常立即记日志 + 中止计划，不让 NPC 永久卡死。
                DebugLogger.Log($"[PlanExecutor] {OwnerAgent?.Name}: Tick 异常 → 计划中止: {ex}");
                Finish(ExecutorState.Aborted, PlanTexts.Interrupted);
            }
        }
        private void LogHeartbeat()
        {
            var step = _selfCursor?.Current;
            string stepInfo = step != null ? $"{step.Id}({step.Action}{RenderStepTarget(step)})" : "-";
            string subInfo = _selfCursor?.SubAction != null ? _selfCursor.SubAction.GetType().Name
                : (_selfCursor?.Inline != null ? _selfCursor.Inline.GetType().Name : "-");
            //DebugLogger.Log($"[PlanExecutor] 心跳 {Elapsed:F0}s | {State}{(PauseReason != null ? "(" + PauseReason + ")" : "")} | 步骤={stepInfo} | 子={subInfo} | 距目标={GetStepTargetDistance():F1}m");
        }
        private void TickInner(float dt)
        {
            // 🔴 全局战斗模式门控（D6 v3 修正）：IsInteractionDisabled 期间脑不 tick、队列动作不被驱动
            // → 执行器也必须整体冻结（含 StepElapsed 冻结——否则 bounded step 会被超时中止而非暂停，
            // 与"脑恢复 tick 后继续"的门控语义矛盾）。脑恢复 tick 后计划自然继续（特性非 bug）。
            if (Settings.Instance.IsInteractionDisabled())
                return;
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
            // 🔴 时间基准：节流通过后必须把"真实经过时间"（约 0.1s+）传给下游——
            // 传帧 dt（~16ms）会让子动作计时/步骤超时/sustained_s 全部 ~6 倍饿死
            // （起身 2s→12.5s、_maxTime 8s→50s、timeout 20s→125s，实机表现 = NPC 原地发呆）。
            float tickDt = _tickAccum;
            _tickAccum = 0f;
            _world.Tick(tickDt);
            // R7 玩家模态（偷窃条/对话/剧情演出）→ Pause；模态结束 → Resume
            bool modal = DetectPlayerModalUi();
            IsPlayerInModalUi = modal;
            if (modal && State == ExecutorState.Executing) { Pause(PauseReasonModal); return; }
            if (!modal && State == ExecutorState.Paused && PauseReason == PauseReasonModal) { Resume(); return; }
            // R1 玩家战斗 → Pause（随从护主由既有 event_agent_damaged 链处理）；战斗结束 → Resume
            bool playerCombat = IsPlayerInCombat();
            if (playerCombat && State == ExecutorState.Executing) { Pause(PauseReasonFight); return; }
            if (!playerCombat && State == ExecutorState.Paused && PauseReason == PauseReasonFight) { Resume(); return; }
            // R4 玩家走远（>30m）→ Pause 追回；豁免：当前步骤是远离玩家的独行任务
            // 🔴 2026-08-13 追加豁免：move_to/follow 目标 = 任意 agent（"过来/跟着某人"）——
            // 走 FollowAgentAction 追踪式跟随，目标在动也兼容；执行者离玩家 >30m 时暂停 +
            // chaseback 是双重走路（先走回 30m 再开始正式跟随），暂停毫无意义。
            // 实机（2026-08-13）：161m 外随从响应"过来"，计划一启动就被暂停 60s 追回，
            // 玩家以为"第二次命令才响应"。
            if (State == ExecutorState.Paused && PauseReason == PauseReasonFar)
            {
                if (IsFollowAgentStep()) Resume();
                else TickChaseBack(dt);
                return;
            }
            if (State == ExecutorState.Executing && !IsCurrentStepRemote())
            {
                var player = Agent.Main;
                if (player != null && player.IsActive() && OwnerAgent.IsActive()
                    && OwnerAgent.Position.Distance(player.Position) > 30f
                    && !IsFollowAgentStep())
                {
                    Pause(PauseReasonFar);
                    return;
                }
            }
            if (State != ExecutorState.Executing) return;
            // Guardrails R2/R5/R6
            TickGuardrails(tickDt);
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
                    TickCursor(cursor, tickDt);
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
        /// <summary>当前步骤目标与执行者的距离（心跳日志用；无目标/未解析 → -1）。</summary>
        private float GetStepTargetDistance()
        {
            var step = _selfCursor?.Current;
            if (step == null || step.Target == null || OwnerAgent == null || !OwnerAgent.IsActive()) return -1f;
            string refName = PlanRefUtil.Normalize(step.Target, out string query);
            if (query != null) refName = query;
            if (string.IsNullOrEmpty(refName)) return -1f;
            if (!_world.TryResolvePosition(refName, OwnerAgent, out Vec3 pos)) return -1f;
            return OwnerAgent.Position.Distance(pos);
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
                cursor.DetachSubAction();   // 循环入口：尚无已入队动作（防御性摘引用）
                return;
            }
            var step = cursor.Current;
            if (step == null)
            {
                cursor.Done = true;
                cursor.DetachSubAction();
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
                            // 🔴 2026-08-19（force 语义硬保障，实机：玩家选「强制执行」后 40s 超时再问，
                            // 两次 force 白选）：强制执行 = 直接动手不再等——清掉跳转目标步骤的 when 门控
                            //（等没人看正是 ask_player 的成因；带门控跳回去 = 门控永不成立 → 超时 → 再问
                            // = 死循环）。C# 侧硬处理，不依赖 LLM 生成无门控步骤。
                            if (string.Equals(ev.Type, "force", StringComparison.OrdinalIgnoreCase))
                            {
                                // 🔴 2026-08-20（实机：LLM 计划 force → s5 但 s5 不存在 → 计划中止，强制白选）：
                                // LLM 常照抄 prompt 示范的 force→sN 结构却忘定义步骤（幻觉 ID，计划校验
                                // 只警告不删除）。force 跳转目标缺失 → 兜底跳到主链中最近的偷窃/击晕步骤
                                //（ask_player 之前必然刚执行过它）——「强制执行」不因计划缺陷落空，
                                // force 语义（无视目击者硬偷）照常生效。
                                string forceTarget = match.Then;
                                if (FindStepById(forceTarget) == null && !forceTarget.StartsWith("@"))
                                {
                                    // 🔴 2026-08-20（force 兜底通用化，用户裁定）：不再硬编码动作名列表
                                    //（steal/knockout…）——回跳「最近执行的真实动作步」（TickCursor 跟踪，
                                    // ask_player 之前必然刚执行过它），对偷窃/击晕/撬锁等任何动作同样适用。
                                    if (!string.IsNullOrEmpty(_lastActionStepId) && FindStepById(_lastActionStepId) != null)
                                    {
                                        DebugLogger.Log($"[PlanExecutor] {OwnerAgent?.Name}: force 跳转目标 {forceTarget} 不存在 → 兜底跳主链步骤 {_lastActionStepId}（强制执行）");
                                        forceTarget = _lastActionStepId;
                                        match.Then = forceTarget;
                                    }
                                }
                                ClearWhenGateOfJumpTarget(forceTarget);
                                // 🔴 2026-08-20（force 语义升级，实机：连点 7 次强制全被目击中断）：
                                // 原 force 只清 when 门控重试——steal 步骤 Rolling 的目击检查无条件，
                                // 被看到照样收手 → 玩家白选。force 同时标记目标步骤「强制执行」：
                                // 偷窃步骤据此跳过目击中断，后果由 roll 后的 WitnessCrime 广播承担。
                                MarkForcedStep(forceTarget);
                            }
                            // 本地化：LWN_plan_step_decision（玩家可见文本）
                            CurrentSummary = LWNTextHelper.ResolveCompound("LWN_plan_step_decision",
                                "Step {STEP}: decision received ({TYPE})",
                                ("STEP", step.Id), ("TYPE", ev.Type));
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
                // 🔴 2026-08-21（用户裁定：在押随从无法执行任何移动类操作）：计划轮 LLM 可能绕过
                // 动作空间生成移动步骤——步骤启动处守卫：移动门控动作（move_to/follow/lead/
                // party_patrol/gather_to_player/engage）+ 执行者在押 → 计划中止（诚实报告，不瞬移不越狱）
                if (ActionRegistry.FindByCode(step.Action)?.DetentionGated == true
                    && CompanionDetentionBehavior.IsDetained(cursor.Agent))
                {
                    DebugLogger.Log($"[PlanExecutor] {OwnerAgent?.Name}: 在押无法执行移动步骤 {step.Id}（{step.Action}）→ 计划中止");
                    Abort(PlanTexts.Aborted);
                    return;
                }
                DebugLogger.Log($"[PlanExecutor] {OwnerAgent?.Name}: ▶ 步骤 {step.Id} 开始（{step.Action}{RenderStepTarget(step)}）");
                if (!TryCreateSubAction(cursor, step))
                {
                    // 创建失败（不可解析目标/未实现动作）→ 按超时处理（不静默）
                    HandleStepTimeout(cursor, step);
                    return;
                }
                // 🔴 2026-08-20（force 语义通用化，用户裁定）：任何实现 IForceable 的内联动作
                // 统一消费强制标记（玩家 ask_player 选「强制执行」→ force → 跳回本步骤）。
                // 动作各自 ApplyForce 定义「强制 = 跳过什么安全检查」（偷窃 = Rolling 目击检查；
                // 击晕/撬锁/搜刮未来引入「等没人看」类阻塞时实现 IForceable 即自动生效，零改动）。
                if (cursor.Inline is IForceable forceable && ConsumeForcedStep(step.Id))
                    forceable.ApplyForce();
                // force 兜底跟踪：最近执行的真实动作步（行为性内联/原子动作 = 动手步骤；
                // ask_player/wait 等通信步不跟踪）——force 跳转目标缺失（LLM 幻觉 ID）时回跳它。
                if (cursor.SubAction != null || (cursor.Inline != null && cursor.Inline.IsBehavioral))
                    _lastActionStepId = step.Id;
                if (cursor.SubAction != null)
                {
                    // M2（D4b）：不再 OnStart——动作生命周期归脑。入队后由脑驱动
                    // （OnStart/OnTick/OnEnd/IsFinished 全部脑侧；经历旁白在脑出队点统一记录）。
                    var brain = AgentAIController.GetBrainForAgent(cursor.Agent);
                    if (brain == null)
                    {
                        // 无脑（异常路径：如玩家被指定为 actor，玩家永不注册 brain）→
                        // 动作从未入队 → TeardownSubAction 兜底补 OnEnd + 步骤失败
                        DebugLogger.Log($"[PlanExecutor] {OwnerAgent?.Name}: {cursor.ActorId}（{cursor.Agent?.Name}）无脑 → 步骤 {step.Id} 无法入队，按失败处理");
                        cursor.TeardownSubAction();
                        HandleStepTimeout(cursor, step);
                        return;
                    }
                    brain.EnqueuePlanAction(cursor.SubAction);
                }
            }
            // until 提前完成（动作步骤）或退出条件（wait 步骤）
            if (step.Until != null && _world.Evaluate(step.Until, cursor.Agent))
            {
                CompleteStep(cursor, step);
                return;
            }
            // 🔴 超时检查必须在子动作/内联驱动之前（保持型/无限等待豁免）：
            // 内联分支（wait 等）的 return 曾短路此检查 → wait 步骤条件不成立时永不超时（实机 b5 卡死，BC-006）
            if (step.TimeoutS > 0f && cursor.StepElapsed > step.TimeoutS && !PlanStep.IsUnboundedStep(step))
            {
                HandleStepTimeout(cursor, step);
                return;
            }
            // 非行为性内联驱动（排序器侧直接驱动：纯逻辑/通信——计时/冒泡台词/事件广播/音效/跳转，不写表现层）
            if (cursor.Inline != null && !cursor.Inline.IsBehavioral)
            {
                cursor.Inline.OnTick(dt);
                // 内联步骤的 OnTick 可能同步完成计划（end_plan → Finish → DetachSubAction 清空 Inline），
                // 此时 cursor.Inline 已为 null——直接返回，由 IsFinished 收尾，禁止二次解引用（NRE 修复）。
                if (cursor.Inline == null) return;
                if (cursor.Inline.Finished)
                {
                    CompleteStep(cursor, step);
                }
                return;
            }
            // 入队动作完成检测（100ms 轮询节奏；三路径判定 D4——🔴 先 IsFinished 再 IsActionAlive：
            // 动作被脑完成后同样不在队列，必须先查 IsFinished 才能区分"完成了"与"被清了"）
            if (cursor.SubAction != null)
            {
                // ① 正常完成（脑已 OnEnd 出队）→ CompleteStep（只摘引用，不再 OnEnd）
                if (cursor.SubAction.IsFinished(cursor.Agent))
                {
                    CompleteStep(cursor, step);
                    return;
                }
                // ② 外部清除（被 ClearAllActions 清掉 = 计划中止 + 收尾报告）
                if (!IsCursorActionAlive(cursor))
                {
                    OnExternalClear(cursor, step);
                    return;
                }
            }
        }
        /// <summary>D4 路径 ②：动作被脑 ClearAllActions 清掉（战斗/护主/击晕/ReactiveAgent 搭话/目击围观）
        /// → 计划中止（graceful，走既有 @abort_gracefully 词汇）+ 收尾报告立即发。
        /// 玩家在场且脱得开身 → 当面报告（needFaceReport：玩家就在旁边却收密信出戏）；
        /// 战斗中/击晕（脱不开身）→ 密信通道。</summary>
        private void OnExternalClear(ActorCursor cursor, PlanStep step)
        {
            DebugLogger.Log($"[PlanExecutor] {OwnerAgent?.Name}: 步骤 {step.Id}（{step.Action}）动作被外部清除 → 计划中止");
            bool canFaceReport = true;
            var brain = AgentAIController.GetBrainForAgent(cursor.Agent);
            if (brain != null && (brain.IsInCombat || AgentBrain.IsKnockedOut(cursor.Agent)))
                canFaceReport = false;   // 脱不开身 → 密信（不打断战斗/不叫晕着的人起来转述）
            Finish(ExecutorState.Aborted, PlanTexts.Aborted, needFaceReport: canFaceReport);
        }
        /// <summary>动作是否仍由 actor 的脑持有（D4 外部清除判定）。</summary>
        private static bool IsCursorActionAlive(ActorCursor cursor)
        {
            var brain = AgentAIController.GetBrainForAgent(cursor.Agent);
            return brain != null && brain.IsActionAlive(cursor.SubAction);
        }
        /// <summary>D4b 统一迁移收口：迁移/终止当前步骤（跳转/超时/循环退出）→
        /// 对当前动作 RequestInterrupt（脑下一帧见 IsFinished 自清出队；中断标记使动作 OnTick
        /// 直接结束、不会真执行——无僵尸动作）+ 摘引用（不调 OnEnd——teardown 归脑）。
        /// 已由脑完成/尚未入队的动作：interrupt 为空操作，摘引用即可。</summary>
        private static void InterruptAndDetach(ActorCursor cursor)
        {
            if (cursor.SubAction != null)
            {
                try { cursor.SubAction.RequestInterrupt(); } catch { }
            }
            cursor.DetachSubAction();
        }
        private void CompleteStep(ActorCursor cursor, PlanStep step)
        {
            // D4b 生命周期收口：脑已完成的动作（IsFinished 路径）只摘引用、不再 OnEnd
            // （脑已调过 OnEnd——再调 = 双 OnEnd，MoveEndAndInteractPrepare/CombatManager 清理双触发）；
            // until/on_event 提前完成 → 动作仍存活在脑队列 → RequestInterrupt（脑下一帧见
            // IsFinished 自清出队；中断标记使动作 OnTick 直接结束、不会真执行——无僵尸动作）。
            if (cursor.SubAction != null)
            {
                var brain = AgentAIController.GetBrainForAgent(cursor.Agent);
                if (brain != null && brain.IsActionAlive(cursor.SubAction))
                {
                    try { cursor.SubAction.RequestInterrupt(); } catch { }
                }
                cursor.DetachSubAction();
            }
            else if (cursor.Inline != null)
            {
                // 非行为性内联完成：排序器侧状态直接丢弃
                cursor.Inline = null;
            }
            cursor.StepElapsed = 0f;
            _stepStartTime = Elapsed;
            _world.MarkStepComplete(step.Id, Elapsed);
            DebugLogger.Log($"[PlanExecutor] {OwnerAgent?.Name}: 步骤 {step.Id} 完成（{step.Action}）");
            // 🔴 2026-08-10（§2.1）：步骤完成事件（实际发生的事实，按执行顺序逐条追加 = 单向链条；
            // IM 侧挂接写执行者记忆 plan_step 行，与密令聊天流不写记忆的偏差②两不相扰）
            try { OnStepCompleted?.Invoke(this, cursor.Agent, step); } catch (Exception ex) { DebugLogger.Log($"[PlanExecutor] OnStepCompleted 异常: {ex.Message}"); }
            // 事件队列不整体清空：步骤切换后，本步期间到达的决策事件（say_to 广播 → 守卫演算）留给下一步 on_event 消费
            // （消费逻辑按 _stepStartTime 过滤过期事件）
            // 判定型原子结果路由（steal_attempt/negotiate/duel 的 result{} 路由，§5.0 缺口 2）
            if (!string.IsNullOrEmpty(_stepResultKey))
            {
                var outcome = _stepResultKey;
                var route = step.ResultRoute(outcome);
                _stepResultKey = null;
                if (!string.IsNullOrEmpty(route))
                {
                    Jump(cursor, route);
                    return;
                }
                // 🔴 2026-08-20（实机：随从偷帝国资深步兵绕不到背后 → 结局 impossible 未被 LLM 的
                // result{} 收录 → 漏路由 → 掉进快乐路径 end_plan success 报「偷着了，快走」）：
                // 判定型步骤产出负面结局（empty/impossible/interrupted）但计划没给路由 → 禁止顺着
                // 主链谎报成功——与「跳转目标缺失」同级处理：计划中止（graceful，@abort_gracefully
                // 同词）。success 未路由 = 步骤本身成功了 → 照旧掉进主链（语义正确）。
                if (outcome != "success")
                {
                    DebugLogger.Log($"[PlanExecutor] {OwnerAgent?.Name}: 步骤 {step.Id}（{step.Action}）结局 {outcome} 无路由 → 计划中止（防谎报成功）");
                    Abort(PlanTexts.Aborted);
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
                    else Finish(ExecutorState.Failed, PlanTexts.GoalNotMet);
                    return;
                }
                cursor.Done = true;
            }
        }
        private void HandleStepTimeout(ActorCursor cursor, PlanStep step)
        {
            // D4b：超时迁移 = 对当前动作 RequestInterrupt（脑下一帧自清出队，中断标记使动作不真执行）
            // + 摘引用（不调 OnEnd——teardown 归脑）
            InterruptAndDetach(cursor);
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
            // 🔴 2026-08-13：消息带"谁 + 在干什么"（玩家看不懂"有一步拖太久了，先撤"——
            // 不写谁、不写什么事；实机 2026-08-13：随从追不上走动的玩家，30s 后冒这句密信）。
            // 中文走 LWN_plan_abort_timeout 的 CN 翻译（带 {OWNER}/{STEP} 变量）。
            string stepDesc = BuildStepSummary(step);
            // 本地化：LWN_plan_abort_timeout（玩家可见文本）
            Abort(LWNTextHelper.ResolveCompound("LWN_plan_abort_timeout",
                "{OWNER} {STEP} — taking too long, calling it off.",
                ("OWNER", OwnerAgent?.Name?.ToString() ?? ""),
                ("STEP", stepDesc)));
        }
        private void ExitLoop(ActorCursor cursor)
        {
            cursor.LoopMode = false;
            cursor.Sequence = CollectStepsForActor(Plan, cursor.ActorId, cursor.Agent);
            cursor.Index = 0;
            cursor.Done = cursor.Sequence.Count == 0;
            InterruptAndDetach(cursor);
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
            // 🔴 2026-08-20（实机：ask_player 的 retreat → 同预案第二步 fail 收尾，原只认预案
            // 入口步 entry[0] → 「跳转目标不存在」→ 计划中止，收尾台词没播）：同预案内跳转放行——
            // 当前游标所在 fallback entry 内的步骤（如 ask_player → 自己的 end_plan 收尾）允许直达；
            // 跨预案仍只允许入口步（S3），防止跳过预案首步的进入条件。
            if (cursor.InFallback && cursor.Sequence != null)
            {
                for (int i = 0; i < cursor.Sequence.Count; i++)
                {
                    if (cursor.Sequence[i]?.Id == target)
                    {
                        cursor.Index = i;
                        cursor.StepElapsed = 0f;
                        InterruptAndDetach(cursor);
                        return;
                    }
                }
            }
            // 优先找 fallback 入口（S3 只跳入口步）
            // 🔴 2026-08-20（实机：contingency 直指预案中间步 q2 → 「跳转目标不存在」→ 计划中止）：
            // LLM 计划的 contingency/on_event 常直指预案内的 end_plan 收尾（如 alert 过高 → q2 撤，
            // 「您发话了，我先撤」），原实现只认入口步 → 跨上下文（主链→预案中间）被拒 → 计划暴毙
            // 「战术出了岔子」。放宽为预案任意步可跳：预案中间步均为 end_plan 终态（无副作用），
            // 直达 = 尊重 LLM 意图（contingency 明写 q2 = 明确跳过 ask_player 直接收尾）。
            if (Plan.Fallbacks != null)
            {
                for (int i = 0; i < Plan.Fallbacks.Count; i++)
                {
                    var entry = Plan.Fallbacks[i];
                    if (entry == null || entry.Count == 0) continue;
                    for (int j = 0; j < entry.Count; j++)
                    {
                        if (entry[j]?.Id != target) continue;
                        cursor.InFallback = true;
                        cursor.LoopMode = false;
                        cursor.Sequence = entry;
                        cursor.Index = j;
                        cursor.StepElapsed = 0f;
                        InterruptAndDetach(cursor);
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
                        InterruptAndDetach(cursor);
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
                    InterruptAndDetach(cursor);
                    return;
                }
            }
            // 目标不存在 → 计划跳转目标缺失（LLM 幻觉 ID 漏网）→ 中止报告，不静默继续（§5.1 铁律）
            DebugLogger.Log($"[PlanExecutor] 跳转目标不存在: {target} → 计划中止");
            Abort(PlanTexts.BadJump);
        }
        /// <summary>force 语义（玩家选「强制执行」= 直接动手不再等）：清掉跳转目标步骤的 when 门控。
        /// 门控（等没人看/等落单）正是 ask_player 的原因——带门控跳回去 = 门控永不成立 → 超时 → 再问
        /// （实机 2026-08-19：force → s3，s3 带 when → 40s 超时 → 回 q1 再问，两次 force 白选）。
        /// 清门控后步骤立即启动，由动作自身判定（StealAttemptInlineState 目击检查等）承担后果。</summary>
        private void ClearWhenGateOfJumpTarget(string target)
        {
            if (string.IsNullOrEmpty(target)) return;
            var step = FindStepById(target);
            if (step?.When == null) return;
            DebugLogger.Log($"[PlanExecutor] {OwnerAgent?.Name}: force 语义 → 清除 {step.Id} 的 when 门控");
            step.When = null;
        }
        /// <summary>🔴 2026-08-20（force 语义升级）：玩家选「强制执行」标记的步骤 id 集合。
        /// 一次性消费：目标步骤创建内联状态时 <see cref="ConsumeForcedStep"/> 读取并移除；计划结束清空。
        /// 语义 = 无视目击者直接动手（偷窃跳过 Rolling 目击中断；后果由 roll 后的 WitnessCrime
        /// 广播承担——目击者警戒/呼叫守卫/可能动手，正是「强制」的代价，铁律 12）。</summary>
        private readonly HashSet<string> _forcedStepIds = new HashSet<string>();

        /// <summary>最近执行的真实动作步 id（force 兜底：跳转目标缺失时回跳它）。
        /// TickCursor 在创建行为性内联/原子动作时更新；ask_player/wait 等通信步不更新。</summary>
        private string _lastActionStepId;

        internal void MarkForcedStep(string stepId)
        {
            if (!string.IsNullOrEmpty(stepId)) _forcedStepIds.Add(stepId);
        }

        /// <summary>一次性消费步骤的强制标记（StealAttemptInlineState 构造时调用）。</summary>
        internal bool ConsumeForcedStep(string stepId)
        {
            if (string.IsNullOrEmpty(stepId)) return false;
            bool forced = _forcedStepIds.Remove(stepId);
            if (forced)
                DebugLogger.Log($"[PlanExecutor] {OwnerAgent?.Name}: 步骤 {stepId} 强制执行（无视目击者）");
            return forced;
        }
        /// <summary>按步骤 id 全计划查找（fallbacks → loop → 主链，与 Jump 同口径）。</summary>
        private PlanStep FindStepById(string id)
        {            if (string.IsNullOrEmpty(id)) return null;
            if (Plan?.Fallbacks != null)
                foreach (var fb in Plan.Fallbacks)
                    if (fb != null)
                        foreach (var s in fb)
                            if (s != null && s.Id == id) return s;
            if (Plan?.Loop?.Steps != null)
                foreach (var s in Plan.Loop.Steps)
                    if (s != null && s.Id == id) return s;
            if (Plan?.Steps != null)
                foreach (var s in Plan.Steps)
                    if (s != null && s.Id == id) return s;
            return null;
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
                    cursor.Inline = new WaitInlineState(cursor.Agent, step);
                    return true;
                case "signal_player":
                    cursor.Inline = new SignalInlineState(this, step);
                    return true;
                case "end_plan":
                    cursor.Inline = new EndPlanInlineState(this, step);
                    return true;
                case "emote":
                    cursor.Inline = new EmoteInlineState(agent, step);
                    cursor.SubAction = new InlinePlanAction(cursor.Inline);
                    return true;
                case "make_noise":
                    cursor.Inline = new MakeNoiseInlineState(this, cursor);
                    return true;
                case "lead":
                    cursor.Inline = new LeadInlineState(this, cursor, step);
                    if (!cursor.Inline.Ok) return false;
                    // M2（D3）：行为性内联 → InlinePlanAction 适配器入队由脑驱动
                    cursor.SubAction = new InlinePlanAction(cursor.Inline);
                    return true;
                case "steal_attempt":
                    cursor.Inline = new StealAttemptInlineState(this, cursor, step);
                    if (!cursor.Inline.Ok) return false;
                    cursor.SubAction = new InlinePlanAction(cursor.Inline);
                    return true;
                case "give_item":
                case "give_gold":
                    cursor.Inline = new GiveInlineState(this, cursor, step);
                    return cursor.Inline.Ok;
                case "deliver_item":
                    cursor.Inline = new DeliverInlineState(this, cursor, step);
                    return cursor.Inline.Ok;
                case "knockout":
                    cursor.Inline = new KnockoutInlineState(this, cursor, step);
                    if (!cursor.Inline.Ok) return false;
                    cursor.SubAction = new InlinePlanAction(cursor.Inline);
                    return true;
                case "crouch":
                case "stand":
                    // 引擎下蹲/站起（2026-08-14）：瞬时 flag 动作，经脑入队（行为性内联，与 emote 同级）
                    cursor.Inline = new CrouchInlineState(agent, step);
                    if (!cursor.Inline.Ok) return false;
                    cursor.SubAction = new InlinePlanAction(cursor.Inline);
                    return true;
                case "ask_help":
                    // 🔴 2026-08-14（M6 多随从分头配合）：通信类内联（留排序器侧）
                    cursor.Inline = new AskHelpInlineState(this, cursor, step);
                    return cursor.Inline.Ok;
                case "ask_player":
                    // 🔴 2026-08-15（等机会/抉择点询问主公）：密信决策卡内联（通信类，排序器侧）。
                    // 投递决策卡（撤退/强制执行）→ 步骤级 on_event 消费玩家点击（事件回投）→ 跳转；
                    // 超时未答 → on_timeout 或 @abort_gracefully（默认撤退语义）。
                    cursor.Inline = new AskPlayerInlineState(this, cursor, step);
                    return cursor.Inline.Ok;
                case "steal_equipment":
                    // 🔴 2026-08-14（M7 偷装备）：复用扒窃判定管线（variant="equipment" 走共享结算）
                    cursor.Inline = new StealAttemptInlineState(this, cursor, step);
                    if (!cursor.Inline.Ok) return false;
                    cursor.SubAction = new InlinePlanAction(cursor.Inline);
                    return true;
            }
            // ── IAtomicAction 子动作（复用引擎级原子行为）──
            switch (step.Action)
            {
                case "move_to":
                    {
                        float within = step.Within > 0f ? step.Within : 2.0f;
                        // 🔴 2026-08-11 目标类型分派（用户裁定，修正"位置快照"错误设计）：
                        // 目标 = agent（找人/找玩家）→ FollowAgentAction(keepFollow:false)——追踪式追到 within 内，
                        // 目标在动也不走空点（对齐 im-command-action-upgrade.md §5.4 契约：会动的 agent → FollowAgentAction）；
                        // 目标 = 确定坐标点（逃跑点/物件/区域/query）→ MoveToPositionAction（快照寻路到点）。
                        // self 无移动意义 → 落 MoveToPositionAction 原位（dist=0 瞬完）。
                        if (ResolveStepAgent(step, cursor, out Agent target) && target != cursor.Agent)
                        {
                            // 🔴 2026-08-13 用户裁定：move_to 目标=玩家 → 挂原版持续跟随
                            // （VanillaFollowAction → FollowAgentBehavior 三连），Brain 队列清空后
                            // 依然跟随；目标=其他 agent → 自研单次追踪（下方 FollowAgentAction）。
                            if (target == Agent.Main)
                            {
                                cursor.SubAction = new VanillaFollowAction(Agent.Main);
                                return true;
                            }
                            // 原 BRING withinMove 特例（2026-08-12）已删：目标=玩家的 BRING 走
                            // 原版跟随，贴身距离天然满足"被请者距玩家 < 3m"目标圈。
                            // 🔴 2026-08-13（走改跑）：原 false（DoNotRun 走速）——找人目标几十米外时
                            // 走速撞步骤 timeout（实机：71 米目标 + k1 timeout 30s → 超时中止「拖太久没成」）。
                            // move_to = 赶路语义，跑过去（到位后 stopDistance 内自停）。
                            cursor.SubAction = new FollowAgentAction(target, true, stopDistance: within, keepFollow: false);
                            return true;
                        }
                        if (!ResolveStepTarget(step, cursor, out Vec3 pos, out Vec2 dir)) return false;
                        cursor.SubAction = new MoveToPositionAction(pos, dir, false, within);
                        return true;
                    }
                case "follow":
                    {
                        if (!ResolveStepAgent(step, cursor, out Agent target)) return false;
                        // 🔴 2026-08-13 用户裁定：follow 目标=玩家 → 原版持续跟随
                        // （relPos behind/left/right/line 在原生跟随下丢弃——跟随语义本来就是贴人走，接受）
                        if (target == Agent.Main)
                        {
                            cursor.SubAction = new VanillaFollowAction(Agent.Main);
                            return true;
                        }
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
                        // 🔴 2026-08-13 语义升级：真解挂原版跟随（RemoveBehavior<FollowAgentBehavior>
                        // + 恢复回岗行为）。旧实现 StayAction 语义清理已不适用——原版跟随期间
                        // Brain 是空脑（不 Suspend），NPC 处于原版接管状态，必须主动解挂。
                        cursor.SubAction = new VanillaUnfollowAction();
                        return true;
                    }
                case "order_attack":
                    {
                        if (!ResolveStepAgent(step, cursor, out Agent target)) return false;
                        cursor.SubAction = new FightEnemyAction(target);
                        // 旁白在子动作 OnStart 处统一记录（RecordActionNarration 分发，见本类 Tick 执行点）
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
            // 🔴 2026-08-21（实机：偷窃被中断 → ask_player 决策卡等待期，LLM 自带的
            // seeing(self,player)=false 持续 10s contingency 触发 → 计划 fail「您发话了，我先撤」
            // → 玩家后点「强制执行」被丢弃）：ask_player 是玩家回合——玩家正在决策，
            // 意外检测不该把计划带走（玩家答完前 contingencies 冻结；卡死由步骤 timeout 兜底）。
            // 同类先例：CurrentStepIsOrderAttack 豁免（2026-08-19）。
            if (IsAskPlayerPending()) return;
            foreach (var c in Plan.Contingencies)
            {
                if (c?.When == null || string.IsNullOrEmpty(c.Then)) continue;
                // 🔴 2026-08-12（实机误报：BRING 计划「seeing(self, 被请者, false) 掉线检测」）：
                // 被请者同意后跟在执行者身后（跟随关系成立），但执行者走路时对方落在身后视野锥外
                // （120° FOV + 视线遮挡）→ "看不见他"持续 5s → contingency 触发 → 报告"他不肯来"，
                // 而对方实际一直在跟（玩家实机：守卫跟到 0.5m 贴身，随从却报"没来"）。
                // 语义：正在跟随我们 = "跟着走"，不是"丢了"——此类掉线 contingency 直接跳过。
                if (IsLostTargetContingency(c.When, OwnerAgent)) continue;
                // 🔴 2026-08-19（实机：目标 93 米外，「seeing(self, 目标)=false」掉线检测秒触发，
                // 把赶路中的计划拽去 ask_player——目标在视野半径外「看不见」是恒真，不是丢失）：
                // seeing(A,B)=false 型掉线检测只在 B 处于 A 的视野半径内才有意义——超距直接跳过
                //（执行者接近进入视野范围后检测恢复语义；等机会类场景仍由 wait 步骤的 until 表达）。
                if (IsBeyondSightRangeContingency(c.When)) continue;
                // 🔴 2026-08-19（实机：ask_player force → order_attack 开战瞬间被计划自带的
                // combat contingency 中止——「战术作罢了」；计划主动发起的战斗不是意外）：
                // 当前步骤 = order_attack（计划主动开战）时，跳过针对执行者自身的 combat 型
                // contingency（combat(self)）；意外战斗检测（他人攻击 / combat(a,b) 双实体）不受影响。
                if (CurrentStepIsOrderAttack() && IsSelfCombatContingency(c.When)) continue;
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
        /// <summary>ask_player 决策卡等待中（玩家回合）：当前游标步骤是 ask_player 且内联状态存活——
        /// 玩家正在做决策，contingency 意外检测冻结（🔴 2026-08-21，见 TickContingencies 注释）。
        /// 卡死由步骤 timeout_s 兜底（LLM 给的 ask_player 通常 60s）。</summary>
        private bool IsAskPlayerPending()
        {
            var c = _selfCursor;
            return c != null && !c.Done && !IsFinished && c.Inline is AskPlayerInlineState;
        }

        /// <summary>掉线检测距离闸（2026-08-19）：seeing(A,B,op≠true) 且 A/B 距离超过视野半径
        /// （NpcSightSystem.CanAgentSeeTarget 默认半径 15m 同款）→ 超距「看不见」恒真，掉线检测无意义
        /// （目标还没走进视野，属于赶路期）。跳过，等执行者接近后检测恢复语义。
        /// any/all watcher 语义是目击判定（内部自带 15m），不适用本闸。</summary>
        private bool IsBeyondSightRangeContingency(Condition cond)
        {
            if (cond == null || !string.Equals(cond.Type, "seeing", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(cond.Op, "true", StringComparison.OrdinalIgnoreCase)) return false;
            // was 修饰 = 目标曾被看见、现在不可见（真实丢失语义，目标可能已逃出视野半径）——距离闸不适用
            if (cond.Was) return false;
            string watcher = cond.A ?? "";
            if (string.Equals(watcher, "any", StringComparison.OrdinalIgnoreCase)
                || string.Equals(watcher, "all", StringComparison.OrdinalIgnoreCase))
                return false;
            if (string.IsNullOrWhiteSpace(watcher) || string.IsNullOrWhiteSpace(cond.B)) return false;
            try
            {
                if (!_world.TryResolveAgent(watcher, OwnerAgent, out Agent watcherAgent)) return false;
                if (!_world.TryResolveAgent(cond.B, OwnerAgent, out Agent subject)) return false;
                return watcherAgent.IsActive() && subject.IsActive()
                    && watcherAgent.Position.Distance(subject.Position) > SightRangeForLossDetection;
            }
            catch { return false; }
        }
        private const float SightRangeForLossDetection = 15f;   // NpcSightSystem.CanAgentSeeTarget 默认半径同款
        /// <summary>当前步骤是否为 order_attack（计划主动开战）。主动战斗期间跳过战斗型 contingency，
        /// 防「force 路径开战瞬间被计划自带的 combat contingency 中止」（实机 2026-08-19：
        /// ask_player force → order_attack 开战 0.1s 后「战术作罢了」）。</summary>
        private bool CurrentStepIsOrderAttack()
        {
            var cur = _selfCursor;
            if (cur == null || cur.Sequence == null || cur.Index < 0 || cur.Index >= cur.Sequence.Count) return false;
            return string.Equals(cur.Sequence[cur.Index]?.Action, "order_attack", StringComparison.OrdinalIgnoreCase);
        }
        /// <summary>combat(self/entity=self) 型 contingency（裸 combat 谓词，无双实体 B 参数）——
        /// 只豁免执行者自己主动开战的场景；combat(a, b) 双实体型（他人战斗）不受影响。</summary>
        private bool IsSelfCombatContingency(Condition cond)
        {
            if (cond == null || !string.Equals(cond.Type, "combat", StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(cond.B)) return false;
            string who = cond.Entity ?? cond.A;
            if (string.IsNullOrEmpty(who)) return false;
            if (string.Equals(who, "self", StringComparison.OrdinalIgnoreCase)) return true;
            try { return _world.TryResolveAgent(who, OwnerAgent, out Agent a) && a == OwnerAgent; }
            catch { return false; }
        }
        /// <summary>
        /// 掉线误报防御（2026-08-12）：结构 = seeing(self, X, op≠true) 且 X 正跟随 self → 目标没丢。
        /// LLM 会给 BRING/带路类计划写"掉线检测"（目标消失就失败），但被请者跟在执行者身后时
        /// 往往在视野锥外（120° FOV + 视线遮挡）——"看不见" ≠ "丢了"。跟随关系成立期间抑制，
        /// 一旦对方停止跟随（如折返岗点）→ 恢复检测。
        /// </summary>
        private bool IsLostTargetContingency(Condition cond, Agent owner)
        {
            if (cond == null) return false;
            // 只识别裸 seeing 条件（and/or 包裹的复杂形态不处理——保持最小侵入）
            if (!string.Equals(cond.Type, "seeing", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(cond.Op, "true", StringComparison.OrdinalIgnoreCase)) return false;
            string watcher = cond.A ?? "";
            if (!string.Equals(watcher, "self", StringComparison.OrdinalIgnoreCase)) return false;
            if (!_world.TryResolveAgent(cond.B, owner, out Agent subject)) return false;
            return _world.IsFollowing(subject, owner);
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
            // D5（v3 修正）：对当前已入队动作 RequestInterrupt（脑下一帧 OnEnd 清出队）+ 摘除引用——
            // SubAction（含行为性内联的 InlinePlanAction 适配器）与行为性内联的 cursor.Inline 都要摘；
            // 非行为性内联（say_to/wait/end_plan）保留状态跨 Pause（恢复后不重播、不重计时，与现状一致）。
            // 🔴 v3 实锤：不摘引用 → Resume 后首轮询见 IsFinished==true → 误判"正常完成"→ 跳步
            // （前进到下一步而非重跑本步）。摘除后 Resume 的创建块会为同一步骤重建（行为性内联 =
            // 全新状态机——Interrupt 不可逆，复用旧实例 = 立即 IsFinished 死路）。
            foreach (var c in _cursors)
            {
                if (c.SubAction != null)
                {
                    try { c.SubAction.RequestInterrupt(); } catch { }
                    c.SubAction = null;
                }
                if (c.Inline != null && c.Inline.IsBehavioral)
                    c.Inline = null;
            }
            CurrentSummary = PauseReason switch
            {
                // 本地化：LWN_plan_pause_modal（玩家可见文本）
                PauseReasonModal => LWNTextHelper.ResolveText("LWN_plan_pause_modal", "The player is busy"),
                // 本地化：LWN_plan_pause_fight（玩家可见文本）
                PauseReasonFight => LWNTextHelper.ResolveText("LWN_plan_pause_fight", "The player is in combat"),
                // 本地化：LWN_plan_pause_far（玩家可见文本）
                PauseReasonFar => LWNTextHelper.ResolveText("LWN_plan_pause_far", "The player is too far away"),
                _ => reason,
            };
            DebugLogger.Log($"[PlanExecutor] 暂停: {reason}");
        }
        public void Resume()
        {
            if (State != ExecutorState.Paused) return;
            State = ExecutorState.Executing;
            PauseReason = null;
            CurrentSummary = Summary ?? "";
            // 同一步骤的重新入队由 TickCursor 创建块自动处理（SubAction/行为性 Inline 已摘 →
            // 重建全新动作/状态机，步骤重跑——与现状「Pause 清 SubAction、Resume 重创建」等价）
            DebugLogger.Log($"[PlanExecutor] 恢复执行");
        }
        /// <summary>玩家停止键/新命令（R3）：旧计划作废，收尾为中断。</summary>
        public void CancelByPlayer(string reason = null)
        {
            if (IsFinished) return;
            Finish(ExecutorState.Aborted,
                // 本地化：LWN_plan_cancel_player（玩家可见文本）
                reason ?? LWNTextHelper.ResolveText("LWN_plan_cancel_player", "Called off by the player"),
                needFaceReport: false, silent: true);
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
                EventLog.Add($"{Elapsed:F0}s: {message}（步骤 {_selfCursor?.Current?.Id} 未能完成）"); // lwn-ignore: A EventLog 调试日志
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
            _forcedStepIds.Clear();   // 强制标记随计划结束清空（防残留污染后续步骤）
            // 🔴 2026-08-14（M5，npc-risk-aware-planning.md）：判定型动作（steal/knockout 有 _stepResultKey）
            // Succeeded 收尾不允许静默——偷窃/击晕是有结局的动作，玩家必须看到结果；
            // InlineSteps 已播报（_resultBroadcast）时视为已有出口，不重复播（聊天单步路径 M2a 已播）。
            bool hasOutcome = state == ExecutorState.Succeeded && !string.IsNullOrEmpty(_stepResultKey);
            if (hasOutcome && !_resultBroadcast && string.IsNullOrEmpty(message))
                message = PlanTexts.Done;   // 有结局但未播报 → 补默认成功出口（收尾报告/密信）
            // 🔴 2026-08-12（用户裁定：BRING 成功 → 被请者开口，不能无声离开）：
            // 成功收尾时"正跟随执行者的人" = 被带来的那个人（BRING 目标跟在随从身后到达），
            // 冒泡问玩家一句（尾巴对话）——人带到了总得有个交代；玩家可当面接话。
            if (state == ExecutorState.Succeeded && IntentType == CommandIntentType.Bring)
                SpeakBringTail();
            foreach (var c in _cursors) FinalizeCursor(c);
            // 🔴 2026-08-14（M5）：判定型动作有结局且未播报 → 不允许静默（走报告出口）
            if ((silent || string.IsNullOrEmpty(message)) && !(hasOutcome && !_resultBroadcast))
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
                // 🔴 2026-08-17（end_plan 60s 罚站修复，实机 20:24:06→20:25:06）：报告期必须释放
                // 「计划执行中」哨兵——意图不复位 → D2 空窗守卫拦截 DecideDefaultBehavior →
                // 随从不走回玩家 → 当面报告条件（<3m）永不满足 → 干等 60s 密信兜底，期间
                // 卡片一直显示执行中、prompt 一直注入「执行中（end_plan）」。
                // 守卫同 OnPlanExecutorFinished：新命令已覆盖意图时不抢。
                var brain = AgentAIController.GetBrainForAgent(OwnerAgent);
                if (brain != null && brain.CurrentIntent?.Type == NpcIntentType.ExecutingCommand)
                    brain.SetNpcIntent(NpcIntentType.None);
            }
            else
            {
                // 密信报告（脱不开身 / 紧急中断）
                SignalPlayer(message);
                FinalizeExecutor(message);
            }
        }
        /// <summary>BRING 成功尾巴（2026-08-12 用户裁定）：被请者冒泡问玩家"召我来有何事"——
        /// 对第一个正跟随执行者的人说（= 被带到面前的那个人）；说话并联框架（plan_report 刺激），
        /// 不占队列不接管 brain；找不到跟随者（防御）→ 静默跳过。</summary>
        private void SpeakBringTail()
        {
            if (OwnerAgent == null || Mission.Current == null) return;
            try
            {
                foreach (var a in Mission.Current.Agents)
                {
                    if (a == null || !a.IsActive() || a == OwnerAgent || a == Agent.Main) continue;
                    if (!_world.IsFollowing(a, OwnerAgent)) continue;
                    SpeechChannel.Say(a,
                        // 本地化：LWN_plan_bring_tail（玩家可见文本）
                        LWNTextHelper.ResolveText("LWN_plan_bring_tail",
                            "You summoned me, my lord. How may I serve?"),
                        SpeechPriority.Dialogue,
                        SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(a), Agent.Main, "plan_report",
                            // 本地化：LWN_plan_bring_tail_topic（玩家可见文本）
                            LWNTextHelper.ResolveText("LWN_plan_bring_tail_topic", "Brought before the master")));
                    return;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PlanExecutor] BRING 尾巴对话失败: {ex.Message}");
            }
        }
        /// <summary>D4b：收尾时各游标的动作收口——生命周期归脑：
        /// 脑仍会 tick（Agent 活跃）→ RequestInterrupt + 摘引用（脑下一帧见 IsFinished 自清出队，
        /// 中断标记使动作不真执行——无僵尸动作；teardown 归脑不调 OnEnd）；
        /// 脑已死（Agent 不活跃，brain 不再 tick，OnEnd 永远不会被调）→ TeardownSubAction 补 OnEnd
        /// （异常兜底，不会双跑——脑不会再调）。</summary>
        private static void FinalizeCursor(ActorCursor c)
        {
            if (c.SubAction != null)
            {
                if (c.Agent != null && c.Agent.IsActive())
                {
                    try { c.SubAction.RequestInterrupt(); } catch { }
                    c.DetachSubAction();
                }
                else
                {
                    c.TeardownSubAction();
                }
            }
            else
            {
                c.Inline = null;
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
                // 本地化：LWN_plan_done（玩家可见文本）
                report ?? LWNTextHelper.ResolveText("LWN_plan_done", "Done"), needFaceReport: !string.IsNullOrEmpty(report));
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
                        // 🔴 统一说话框架：当面报告（前因=plan_report）。
                        // 文本 _pendingReport 已是 LLM 生成 → 不再 SayPolished（避免嵌套 LLM 请求）
                        SpeechChannel.Say(OwnerAgent, _pendingReport, SpeechPriority.Dialogue,
                            SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(OwnerAgent), player, "plan_report", null));
                        DebugLogger.Log($"[PlanExecutor] 当面报告: {_pendingReport}");
                    }
                    catch { }
                }
                if (_reportTimer > 3f)
                {
                    FinalizeExecutor(_pendingReport);
                }
            }
            else if (_reportTimer > 30f)
            {
                // 超时兜底：密信（🔴 2026-08-17：60f→30f——报告期哨兵已释放、随从会走回玩家，
                // 兜底只需覆盖「玩家走远/跨区/跟丢」场景；60s 罚站期间卡片执行态/意图全悬挂）
                SignalPlayer(_pendingReport);
                FinalizeExecutor(_pendingReport);
            }
        }
        private void FinalizeExecutor(string message)
        {
            if (_finalized) return;
            _finalized = true;
            // 🔴 2026-08-19（实机：场景结束强制收尾播「命令已经办妥」）：ShutdownAll 直调本方法时
            // Finish 从未执行 → EndMessage 为 null → OnFinished 兜底文案 LWN_im_cmd_done 误导玩家
            //（实际战术被场景结束强制丢弃，什么都没办成）。message 参数落进 EndMessage——
            // Finish/TickReport 路径已先置值，此处不覆盖。
            if (string.IsNullOrEmpty(EndMessage)) EndMessage = message;
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
                // 本地化：LWN_plan_goal_done（玩家可见文本）
                CurrentSummary = LWNTextHelper.ResolveText("LWN_plan_goal_done", "Goal achieved");
                DebugLogger.Log($"[PlanExecutor] GOAL 达成");
            }
        }
        private string BuildStepSummary(PlanStep step)
        {
            if (step == null) return Summary ?? "";
            // 🔴 2026-08-13 本地化（原硬编码中文；现随超时消息进入玩家可见文本，铁律 13）
            switch (step.Action)
            {
                // 本地化：LWN_plan_step_move_to（玩家可见文本）
                case "move_to": return LWNTextHelper.ResolveText("LWN_plan_step_move_to", "Heading to target");
                case "say_to":
                    // 🔴 2026-08-13 文案去 {TEXT}（步骤摘要只报状态，不说台词内容）
                    return LWNTextHelper.ResolveText("LWN_plan_step_talk", "Talking");
                // 本地化：LWN_plan_step_wait（玩家可见文本）
                case "wait": return LWNTextHelper.ResolveText("LWN_plan_step_wait", "Waiting");
                // 本地化：LWN_plan_step_fight（玩家可见文本）
                case "order_attack": return LWNTextHelper.ResolveText("LWN_plan_step_fight", "Fighting");
                // 本地化：LWN_plan_step_report（玩家可见文本）
                case "signal_player": return LWNTextHelper.ResolveText("LWN_plan_step_report", "Preparing to report");
                // 本地化：LWN_plan_step_ask_player（玩家可见文本）
                case "ask_player": return LWNTextHelper.ResolveText("LWN_plan_step_ask_player", "Asking the lord for a decision");
                // 🔴 2026-08-17（end_plan 技术代号泄漏修复）：原 default 兜底「执行中（{ACTION}）」
                // 把 end_plan 原样漏进 prompt 注入段（【当前计划执行中】当前进度）与 HUD——
                // LLM 不知道 end_plan 是什么，实机回复「plan 已定，只等您一声令下」。专属人性化摘要。
                // 本地化：LWN_plan_step_reporting（玩家可见文本）
                case "end_plan": return LWNTextHelper.ResolveText("LWN_plan_step_reporting", "Reporting the result to you");
                // 本地化：LWN_plan_step_steal（玩家可见文本）
                case "steal_attempt": return LWNTextHelper.ResolveText("LWN_plan_step_steal", "Preparing to steal");
                // 本地化：LWN_plan_step_doing（玩家可见文本）
                default: return LWNTextHelper.ResolveCompound("LWN_plan_step_doing", "Carrying out ({ACTION})", ("ACTION", step.Action));
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
        /// <summary>
        /// 🔴 2026-08-15（ask_player 询问步骤）：向玩家投递密信决策卡（主线程，AskPlayerInlineState 调用）。
        /// 卡片 = 执行人私聊会话的 Text 消息 + 按钮行（撤退/强制执行，文案本地化；事件码固定 retreat/force，
        /// 与 LLM 计划 on_event 的 type 逐字匹配）。玩家点击 → ImChatView.HandleAskPlayerOption →
        /// 本方法所在执行器的 NotifyDecisionEvent(eventType) → 步骤 on_event 路由（TickCursor 事件通道消费）。
        /// 执行人无 Hero（模板 NPC 无私聊会话）/会话不可达 → 静默跳过（计划继续走超时/失败路径）。
        /// </summary>
        internal void AskPlayer(string question)
        {
            try
            {
                if (OwnerAgent == null || Mission.Current == null) return;
                var hero = (OwnerAgent.Character as TaleWorlds.CampaignSystem.CharacterObject)?.HeroObject;
                if (hero == null || string.IsNullOrEmpty(hero.StringId)) return;
                string heroId = hero.StringId;
                string name = OwnerAgent.Name?.ToString() ?? heroId;
                ImChatStore.TouchDirectChat(heroId, ImChatManager.NowUnixMs());
                var conv = ImChatManager.GetDirectConversation(heroId);
                if (conv == null) return;
                var msg = new ImMessage(heroId, name,
                    string.IsNullOrWhiteSpace(question)
                        // 本地化：ask_player 默认提问文案（LWN_im_ask_player_default）
                        ? LWNTextHelper.ResolveText("LWN_im_ask_player_default", "My lord, I need your decision.")
                        : question,
                    ImMessageKind.Text)
                {
                    ConvId = conv.Id,
                    IsAskPlayer = true,
                    // 按钮文案本地化（主线程可调 LWNTextHelper）；事件码固定白名单（on_event 匹配）
                    AskPlayerOptions = new System.Collections.Generic.List<AskPlayerOption>
                    {
                        // 本地化：ask_player 撤退按钮（LWN_im_ask_player_retreat）
                        new AskPlayerOption(LWNTextHelper.ResolveText("LWN_im_ask_player_retreat", "Fall back"), "retreat"),
                        // 本地化：ask_player 强制执行按钮（LWN_im_ask_player_force）
                        new AskPlayerOption(LWNTextHelper.ResolveText("LWN_im_ask_player_force", "Force it"), "force"),
                    },
                };
                ImChatStore.AppendGroupMessage(conv.Id, msg);
                ImChatStore.IncUnread(conv.Id);
                ImChatManager.BroadcastMessageArrived(conv);
                DebugLogger.Log($"[PlanExecutor] {name} ask_player → 密信决策卡已投递: {msg.Content}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PlanExecutor] ask_player 投递失败: {ex.Message}");
            }
        }
        internal void NotifySayDone(PlanStep step, Agent target)
        {
            // 占位：say_to 完成钩子（M3 ReactiveAgent 演算后的后续钩子）
        }
        internal void SetStepResultKey(string key) => _stepResultKey = key;
        /// <summary>当前步骤结果 key（success/empty/interrupted/impossible；null = 普通完成）。
        /// 🔴 2026-08-20：OnStepCompleted 回调在 CompleteStep 清空 _stepResultKey 之前触发，
        /// 记忆写入可读此值区分「办成了」与「没办成」（实机：偷窃被目击中断却记成「已完成」）。</summary>
        internal string StepResultKey => _stepResultKey;
        internal void RecordStolenGold(float amount) => _stolenGold = amount;
        internal float StolenGold => _stolenGold;
        // 🔴 2026-08-14（M2d/M5）：钱袋路径当场移交标记（give_gold 防双移交）+ 判定型结局已播标记（Finish 防重复播）
        internal void MarkGoldHanded() => _goldHanded = true;
        internal bool GoldHanded => _goldHanded;
        internal void MarkResultBroadcast() => _resultBroadcast = true;
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
        /// <summary>🔴 2026-08-13：当前步骤是"朝 agent 走/跟着 agent"（move_to/follow 且目标解析为
        /// 任意 agent，含玩家）→ 豁免「玩家走远了」暂停（R4）。这类步骤走 FollowAgentAction 追踪式
        /// 跟随，目标在动也兼容（重算间隔随目标速度自适应），执行者离玩家 >30m 时暂停 + chaseback
        /// 是双重走路，暂停毫无意义。R4 该防的只有"往固定坐标点傻走"（MoveToPositionAction 快照
        /// 寻路，玩家走了它仍走旧点）。判定与执行器 move_to/follow 分派完全对齐：
        /// ResolveStepAgent 成功 = agent 目标（豁免）；失败 = 坐标点（不豁免，走既有 R4）。</summary>
        private bool IsFollowAgentStep()
        {
            var step = _selfCursor?.Current;
            if (step == null) return false;
            if (step.Action != "move_to" && step.Action != "follow") return false;
            return ResolveStepAgent(step, _selfCursor, out _);
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
            if (!OwnerAgent.IsActive()) { Abort(PlanTexts.CompanionDown); return; }
            float dist = OwnerAgent.Position.Distance(player.Position);
            if (dist < 20f)
            {
                Resume();
                return;
            }
            // 追回玩家身边
            AgentControlHelper.ScriptedMoveToPoint(OwnerAgent, player.Position, true);
            // 本地化：LWN_plan_chaseback（玩家可见文本）
            CurrentSummary = LWNTextHelper.ResolveText("LWN_plan_chaseback", "Catching up to the player");
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
        /// <summary>正常迁移（D4b）：只摘引用，teardown（OnEnd）归脑——动作生命周期已被脑接管。
        /// 脑侧完成出队 / 迁移终止时一律走此路径，禁止再调 OnEnd（双 OnEnd 会让
        /// MoveEndAndInteractPrepare / CombatManager 清理双触发）。</summary>
        public void DetachSubAction()
        {
            SubAction = null;
            Inline = null;
        }
        /// <summary>异常兜底（D4b）：动作从未入队/脑已死时补 OnEnd，防资源泄漏。
        /// 只用于脑永远不会再驱动该动作的路径（无脑入队失败 / Agent 已不活跃），
        /// 不会与脑的 OnEnd 双跑——脑不会再调。</summary>
        public void TeardownSubAction()
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
    // 原 ExecutePlanAction（brain 队列占位挂载）已随单脑化重构 D1 删除（2026-08-11）：
    // 执行器不再挂脑队列占位——order_execute_plan / plan_debug 直接 executor.Start +
    // ExecutingCommand 意图哨兵（AgentBrain D2 空窗守卫），行为步骤由执行器逐个入队。
    // 行为性内联入队适配器 InlinePlanAction（D3）定义于 AI/Actions/AtomicAction.cs。
    // ═══════════════════════════════════════════════════════════════
}