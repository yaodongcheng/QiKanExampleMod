using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace LivingWorldNpcs.Planner.Cooperation
{
    public enum CoopActivityState
    {
        Candidate = 0,
        /// <summary>已向本人 Brain 申请身体，尚未获准。</summary>
        Requested = 1,
        Executing = 2,
        /// <summary>已推进到未知结果处，等对方公开答复／真实到达。</summary>
        Waiting = 3,
        Paused = 4,
        Success = 5,
        Failed = 6,
        Cancelled = 7,
        Interrupted = 8
    }

    public enum CoopStepOutcomeKind
    {
        Completed = 0,
        Failed = 1,
        /// <summary>结果未知，等真实回执。</summary>
        Waiting = 2,
        Interrupted = 3
    }

    public struct CoopStepOutcome
    {
        public CoopStepOutcomeKind Kind;
        public string Reason;
        public bool ApplyEffects;

        public static CoopStepOutcome Completed(bool applyEffects = true)
        {
            return new CoopStepOutcome { Kind = CoopStepOutcomeKind.Completed, ApplyEffects = applyEffects, Reason = "completed" };
        }

        public static CoopStepOutcome Failed(string reason)
        {
            return new CoopStepOutcome { Kind = CoopStepOutcomeKind.Failed, Reason = reason };
        }

        public static CoopStepOutcome Waiting(string reason)
        {
            return new CoopStepOutcome { Kind = CoopStepOutcomeKind.Waiting, Reason = reason };
        }

        public static CoopStepOutcome Interrupted(string reason)
        {
            return new CoopStepOutcome { Kind = CoopStepOutcomeKind.Interrupted, Reason = reason };
        }
    }

    /// <summary>
    /// 一名角色的一次协作活动。活动、原子步骤、双方交互、玩家任务四种状态分开记录（§5.6）。
    /// 终态不可复活；重新启动产生新活动，但复用业务收据。
    /// </summary>
    public sealed class CoopActivity
    {
        public string Id = string.Empty;
        public string RunId = string.Empty;
        public string ActorId = string.Empty;
        public string RoleId = string.Empty;
        public string Source = "coop";
        public int OwnerToken;
        public CoopAlgorithm Algorithm = CoopAlgorithm.Hybrid;

        public CoopActivityState State = CoopActivityState.Candidate;
        public CoopPlan Plan;
        public CoopContentData Content;
        public CoopDomain Domain;
        internal Task<CoopSearchResult> PendingSolve;
        internal CoopGoalDef PendingGoal;
        internal CoopActivityState BeforePause;
        internal double PausedAt;
        public int StepRevision;
        public bool PlayerInformed;
        public string ObservationPointQuery;
        public float ObservationDistance = 3f;
        public bool AwaitingApproval;
        internal CoopStepOutcome? PausedOutcome;
        internal CoopActionDef PausedAction;
        public string FocusTargetId;
        public string InteractionPartnerId;
        public string RequestActivityId;
        public bool FavorCommitted;
        public int PlanRevision;
        public int StepIndex;
        public readonly CoopFactStore Facts = new CoopFactStore();

        public double StartedSim;
        public double StepStartedSim;
        public double WaitDeadlineSim;
        public double LastProgressSim;

        public string LastResult = string.Empty;
        public string LastReason = string.Empty;
        public string LastFailureReason = string.Empty;
        public string StopReason = string.Empty;
        public int AttemptsUsed;
        public int ReplansUsed;
        public readonly HashSet<string> FailedActions = new HashSet<string>(StringComparer.Ordinal);
        public readonly List<string> History = new List<string>();

        public bool IsActive
        {
            get
            {
                return State == CoopActivityState.Requested
                    || State == CoopActivityState.Executing
                    || State == CoopActivityState.Waiting
                    || State == CoopActivityState.Paused;
            }
        }

        public bool IsTerminal
        {
            get
            {
                return State == CoopActivityState.Success
                    || State == CoopActivityState.Failed
                    || State == CoopActivityState.Cancelled
                    || State == CoopActivityState.Interrupted;
            }
        }

        public string CurrentStepId
        {
            get
            {
                if (Plan == null || StepIndex < 0 || StepIndex >= Plan.Steps.Count)
                {
                    return null;
                }
                return Plan.Steps[StepIndex];
            }
        }

        public CoopActionDef CurrentAction(CoopDomain domain)
        {
            var id = CurrentStepId;
            CoopActionDef action;
            return id != null && domain != null && domain.Actions.TryGetValue(id, out action) ? action : null;
        }

        public void Note(string text, double simTime)
        {
            if (History.Count >= 200)
            {
                History.RemoveAt(0);
            }
            History.Add(simTime.ToString("0.###") + " " + text);
            LastProgressSim = simTime;
        }

        public void End(CoopActivityState terminal, string reason, double simTime)
        {
            if (IsTerminal)
            {
                return;
            }
            State = terminal;
            LastReason = reason ?? string.Empty;
            LastResult = terminal.ToString().ToLowerInvariant();
            Note("end:" + LastResult + " " + LastReason, simTime);
        }
    }

    /// <summary>
    /// 实际执行一步的运行时（引擎适配器实现）。活动层不直接操纵身体；
    /// 它只申请本人 Brain，并把真实结果回执转成 CoopStepOutcome。
    /// </summary>
    public interface ICoopStepRuntime
    {
        /// <summary>该活动是否已有一步在跑（避免每帧重复 Start）。</summary>
        bool IsBusy(CoopActivity activity);

        /// <summary>资格复核：不满足时返回原因，不消耗随机输入。</summary>
        bool CanRun(CoopActivity activity, CoopActionDef action, out string reason);

        /// <summary>请求本人 Brain 执行；完成、失败或进入等待时回调一次。</summary>
        void Start(CoopActivity activity, CoopActionDef action, double simTime, Action<CoopStepOutcome> onDone);

        /// <summary>本次活动收尾：停信号、释放身体、撤回合法动作。</summary>
        void Stop(CoopActivity activity, string reason);
    }

    /// <summary>真实世界适配：更新本人能知道的事实、按持有编号暂停身体。</summary>
    public interface ICoopRuntimeLifecycle
    {
        double SimTime { get; }
        void Refresh(CoopActivity activity, double simTime);
        void Pause(CoopActivity activity, bool paused, double simTime);
    }

    /// <summary>
    /// 活动执行层：管步骤、依赖、等待、预算、一次结算与版本。
    /// 每人的活动独立；不调用原子的 OnStart/OnEnd，不重复结算。
    /// </summary>
    public sealed class CoopDirector
    {
        public static CoopDirector Current { get; private set; }

        private readonly Dictionary<string, CoopActivity> _byId = new Dictionary<string, CoopActivity>(StringComparer.Ordinal);
        private readonly Dictionary<string, CoopActivity> _byActor = new Dictionary<string, CoopActivity>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>();
        private int _nextToken = 1;
        private int _tickCursor;

        public CoopContentData Content { get; private set; }
        public CoopCatalog Catalog { get; private set; }
        public CoopDomain Domain { get; private set; }
        public CoopLedger Ledger { get; private set; }
        internal CoopLedger SwapLedger(CoopLedger ledger)
        {
            var previous = Ledger;
            Ledger = ledger;
            return previous;
        }
        public ICoopStepRuntime Runtime { get; set; }
        public int SceneEpoch { get; private set; }
        public string RunId { get; private set; }

        public IEnumerable<CoopActivity> Activities { get { return _order.Count == 0 ? (IEnumerable<CoopActivity>)new CoopActivity[0] : Enumerate(); } }

        private IEnumerable<CoopActivity> Enumerate()
        {
            for (int i = 0; i < _order.Count; i++)
            {
                CoopActivity activity;
                if (_byId.TryGetValue(_order[i], out activity))
                {
                    yield return activity;
                }
            }
        }

        public static CoopDirector Install(CoopContentData content, Func<CoopActionDef, string> availability, int sceneEpoch)
        {
            var previous = Current;
            previous?.StopAll("host_replaced", 0);
            var director = new CoopDirector
            {
                Content = content,
                Catalog = CoopCatalog.Build(content, availability, sceneEpoch),
                Ledger = CoopState.Ledger,
                SceneEpoch = sceneEpoch,
                RunId = "run-" + Guid.NewGuid().ToString("N")
            };
            CoopLog.SetSceneEpoch(sceneEpoch);
            director.Domain = director.Catalog.PlannableDomain(content.Domain);
            CoopLog.SetRun(director.RunId);
            Current = director;
            CoopLog.Event("catalog.built")
                .Detail(director.Catalog.ContentVersion)
                .Value("hash", director.Catalog.ContentHash)
                .Raw("plannable", director.Catalog.PlannableCount)
                .Raw("entries", director.Catalog.Count)
                .Write();
            return director;
        }

        public bool Reload(CoopContentData content, Func<CoopActionDef, string> availability)
        {
            if (content == null || !content.IsValid) return false;
            var catalog = CoopCatalog.Build(content, availability, SceneEpoch);
            Content = content;
            Catalog = catalog;
            Domain = catalog.PlannableDomain(content.Domain);
            // 现有活动持有原内容对象和能力目录；新活动使用新版本。
            return true;
        }

        public static void Uninstall(int sceneEpoch)
        {
            var director = Current;
            if (director != null)
            {
                director.StopAll("scene_exit", 0);
            }
            Current = null;
            CoopLog.SetSceneEpoch(sceneEpoch);
        }

        public int NextOwnerToken()
        {
            return _nextToken++;
        }

        public CoopActivity Find(string id)
        {
            CoopActivity activity;
            return id != null && _byId.TryGetValue(id, out activity) ? activity : null;
        }

        public CoopActivity FindByRun(string runId)
        {
            if (string.IsNullOrEmpty(runId)) return null;
            foreach (var activity in Activities)
            {
                if (string.Equals(activity.RunId, runId, StringComparison.Ordinal)) return activity;
            }
            return null;
        }

        public CoopActivity ForActor(string actorId)
        {
            CoopActivity activity;
            return actorId != null && _byActor.TryGetValue(actorId, out activity) ? activity : null;
        }

        /// <summary>启动一次活动：先校验内容、再固定计划版本，最后才申请身体。</summary>
        public CoopActivity Start(string actorId, string roleId, CoopGoalDef goal, CoopAlgorithm algorithm, string source, double simTime, out string reason)
        {
            return StartInternal(actorId, roleId, goal, algorithm, source, simTime, null, out reason);
        }

        /// <summary>带初始事实启动（接受者用自己的感知开始自己的活动，不读对方计划）。</summary>
        public CoopActivity StartSeeded(string actorId, string roleId, CoopGoalDef goal, CoopAlgorithm algorithm, string source, double simTime, IDictionary<string, CoopTruth> seed, out string reason,
            CoopContentData pinnedContent = null, CoopDomain pinnedDomain = null)
        {
            return StartInternal(actorId, roleId, goal, algorithm, source, simTime, seed, out reason, pinnedContent, pinnedDomain);
        }

        private CoopActivity StartInternal(string actorId, string roleId, CoopGoalDef goal, CoopAlgorithm algorithm, string source, double simTime, IDictionary<string, CoopTruth> seed, out string reason,
            CoopContentData pinnedContent = null, CoopDomain pinnedDomain = null)
        {
            reason = null;
            var content = pinnedContent ?? Content;
            var domain = pinnedDomain ?? Domain;
            if (content == null || !content.IsValid || domain == null)
            {
                reason = "content_invalid";
                return null;
            }
            if (string.IsNullOrEmpty(actorId) || goal == null)
            {
                reason = "bad_arguments";
                return null;
            }
            var existing = ForActor(actorId);
            if (existing != null && existing.IsActive)
            {
                reason = "activity_already_active";
                return null;
            }

            var activity = new CoopActivity
            {
                Id = "act-" + RunId.Substring(4, 8) + "-" + _nextToken,
                RunId = RunId,
                ActorId = actorId,
                RoleId = roleId ?? string.Empty,
                Source = source ?? "coop",
                Algorithm = algorithm,
                OwnerToken = _nextToken++,
                StartedSim = simTime,
                PlanRevision = 1
            };
            activity.Facts.OwnerId = actorId;
            CoopLog.Event("run.begin")
                .Actor(actorId).Activity(activity.Id).Owner(activity.OwnerToken)
                .Source(source).Value("role", roleId).Value("goal", goal.Id)
                .Value("algorithm", algorithm.ToString())
                .Write();
            if (seed != null)
            {
                foreach (var pair in seed)
                {
                    activity.Facts.Set(pair.Key, pair.Value, "seed", simTime);
                }
            }
            activity.State = CoopActivityState.Requested;

            CoopLog.Event("goal.selected")
                .Actor(actorId).Activity(activity.Id).Value("goal", goal.Id)
                .Raw("priority", goal.Priority)
                .Write();
            var snapshot = activity.Facts.Snapshot();
            activity.Content = content;
            activity.Domain = domain;
            activity.PendingGoal = goal;
            if (!goal.Constraints.IsSatisfiedBy(snapshot))
            {
                reason = "goal_constraints_invalid";
                return null;
            }
            if (Runtime is ICoopRuntimeLifecycle)
            {
                Register(activity);
                QueueSolve(activity, goal, domain);
                return activity;
            }
            var result = Solve(activity, snapshot, domain, goal);
            CoopLog.Event("plan.requested")
                .Actor(actorId).Activity(activity.Id).Owner(activity.OwnerToken)
                .Value("goal", goal.Id).Value("algorithm", algorithm.ToString())
                .Value("configVersion", content.ContentVersion).Value("configHash", content.ConfigHash)
                .FactsVersion(activity.Facts.Version)
                .Write();
            if (!result.Found)
            {
                activity.End(CoopActivityState.Failed, "plan_" + result.Status.ToString().ToLowerInvariant(), simTime);
                reason = "plan_" + result.Status.ToString().ToLowerInvariant();
                CoopLog.Event("plan.rejected")
                    .Actor(actorId).Activity(activity.Id)
                    .Value("goal", goal.Id).Value("reason", result.Reason)
                    .Raw("nodes", result.Expanded).Raw("ms", result.ElapsedMs)
                    .Write();
                Register(activity);
                return null;
            }

            activity.Plan = CoopPlanner.ToPlan(result, algorithm, goal, 1, content.ContentVersion + "#" + Hash8(content.ConfigHash));
            activity.State = CoopActivityState.Executing;
            Register(activity);
            CoopLog.Event("plan.ready")
                .Actor(actorId).Activity(activity.Id).PlanRevision(activity.PlanRevision)
                .Value("goal", goal.Id).Value("algorithm", algorithm.ToString())
                .Raw("cost", result.Cost).Raw("nodes", result.Expanded).Raw("ms", result.ElapsedMs)
                .Detail(string.Join(" -> ", activity.Plan.Steps))
                .Write();
            return activity;
        }

        private void Register(CoopActivity activity)
        {
            _byId[activity.Id] = activity;
            _order.Add(activity.Id);
            if (!string.IsNullOrEmpty(activity.ActorId))
            {
                _byActor[activity.ActorId] = activity;
            }
            if (_order.Count > 64)
            {
                Prune();
            }
        }

        private void Prune()
        {
            for (int i = 0; i < _order.Count && _order.Count > 32; i++)
            {
                CoopActivity activity;
                if (_byId.TryGetValue(_order[i], out activity) && activity.IsTerminal)
                {
                    _byId.Remove(_order[i]);
                    _order.RemoveAt(i);
                    i--;
                }
            }
        }

        public void Tick(double dt, double simTime)
        {
            if (Runtime == null || Content == null)
            {
                return;
            }
            var frame = Stopwatch.StartNew();
            int count = _order.Count;
            for (int i = 0; i < count; i++)
            {
                if (_tickCursor >= _order.Count) _tickCursor = 0;
                string id = _order[_tickCursor++];
                CoopActivity activity;
                if (!_byId.TryGetValue(id, out activity) || !activity.IsActive)
                {
                    continue;
                }
                TickActivity(activity, simTime);
                if (frame.Elapsed.TotalMilliseconds >= Content.FrameCpuMs) break;
            }
        }

        private static CoopSearchResult Solve(CoopActivity activity, IDictionary<string, CoopTruth> facts, CoopDomain domain, CoopGoalDef goal)
        {
            var watch = Stopwatch.StartNew();
            var result = CoopPlanner.Solve(facts, domain, goal, activity.Algorithm, activity.Content.Budget,
                () => activity.IsTerminal || watch.Elapsed.TotalMilliseconds >= activity.Content.SolveCpuMs);
            if (!activity.IsTerminal && watch.Elapsed.TotalMilliseconds >= activity.Content.SolveCpuMs)
            {
                result.Status = CoopSolveStatus.BudgetExhausted;
                result.Reason = "cpu_budget";
            }
            return result;
        }

        private static void QueueSolve(CoopActivity activity, CoopGoalDef goal, CoopDomain domain)
        {
            var facts = activity.Facts.Snapshot();
            activity.State = CoopActivityState.Requested;
            activity.PendingGoal = goal;
            activity.PendingSolve = Task.Run(() =>
            {
                try { return Solve(activity, facts, domain, goal); }
                catch (Exception) { return CoopSearchResult.Failure(CoopSolveStatus.InvalidInput, "solve_exception", 0); }
            });
            CoopLog.Event("plan.requested").Actor(activity.ActorId).Activity(activity.Id)
                .Value("goal", goal.Id).Value("configVersion", activity.Content.ContentVersion).Write();
        }

        private void TickActivity(CoopActivity activity, double simTime)
        {
            if (activity.State == CoopActivityState.Paused) return;
            var lifecycle = Runtime as ICoopRuntimeLifecycle;
            if (lifecycle != null) lifecycle.Refresh(activity, simTime);
            var content = activity.Content ?? Content;
            var domain = activity.Domain ?? Domain;
            var goal = content.GoalOrNull(activity.Plan != null ? activity.Plan.GoalId : activity.PendingGoal?.Id);
            if (goal == null || !goal.Constraints.IsSatisfiedBy(activity.Facts.Snapshot()))
            {
                EndActivity(activity, CoopActivityState.Interrupted, "goal_constraints_invalid", simTime);
                return;
            }
            if (activity.PausedOutcome.HasValue)
            {
                var outcome = activity.PausedOutcome.Value;
                var pausedAction = activity.PausedAction;
                activity.PausedOutcome = null;
                activity.PausedAction = null;
                OnStepDone(activity, pausedAction, outcome, simTime);
                return;
            }
            if (activity.PendingSolve != null)
            {
                if (!activity.PendingSolve.IsCompleted) return;
                var pending = activity.PendingSolve;
                activity.PendingSolve = null;
                if (pending.IsFaulted || pending.IsCanceled)
                {
                    DebugLoggerException(pending);
                    EndActivity(activity, CoopActivityState.Failed, "solve_failed", simTime);
                    return;
                }
                var solved = pending.Result;
                if (!solved.Found)
                {
                    CoopLog.Event("plan.rejected").Actor(activity.ActorId).Activity(activity.Id)
                        .Value("algorithm", activity.Algorithm.ToString()).Reason(solved.Reason)
                        .Raw("nodes", solved.Expanded).Raw("ms", solved.ElapsedMs).Write();
                    EndActivity(activity, CoopActivityState.Failed, "plan_" + solved.Status.ToString().ToLowerInvariant(), simTime);
                    return;
                }
                activity.Plan = CoopPlanner.ToPlan(solved, activity.Algorithm, goal, activity.PlanRevision,
                    content.ContentVersion + "#" + Hash8(content.ConfigHash));
                activity.StepIndex = 0;
                activity.State = CoopActivityState.Executing;
                CoopLog.Event("plan.ready").Actor(activity.ActorId).Activity(activity.Id)
                    .PlanRevision(activity.PlanRevision).Value("algorithm", activity.Algorithm.ToString())
                    .Raw("nodes", solved.Expanded).Raw("ms", solved.ElapsedMs).Raw("cost", solved.Cost)
                    .Detail(string.Join(" -> ", solved.Steps)).Write();
                return; // 主线程先发布分工说明；下一拍才申请步骤。
            }
            if (activity.Facts.Expire(simTime))
            {
                CoopLog.Event("fact.expired")
                    .Actor(activity.ActorId).Activity(activity.Id)
                    .SimTime(simTime)
                    .Write();
            }
            if (activity.State == CoopActivityState.Paused)
            {
                return;
            }
            if (activity.State == CoopActivityState.Waiting)
            {
                if (activity.WaitDeadlineSim > 0 && simTime >= activity.WaitDeadlineSim)
                {
                    FailStep(activity, "wait_timeout", simTime);
                }
                return;
            }
            if (activity.State != CoopActivityState.Executing)
            {
                return;
            }
            if (activity.AwaitingApproval) return;
            if (lifecycle != null && content.RoleOrNull(activity.RoleId)?.RequiresContract == true && !activity.PlayerInformed) return;
            var activeAction = activity.CurrentAction(domain);
            bool witnessedWait = activeAction?.CapabilityId == "wait_fact" && Runtime.IsBusy(activity)
                && activeAction.Args.TryGetValue("fact", out var observedFact) && activity.Facts.IsTrue(observedFact);
            if (activeAction != null && !witnessedWait && !activeAction.Persistent.IsSatisfiedBy(activity.Facts.Snapshot()))
            {
                FailStep(activity, "persistent_condition_lost", simTime);
                return;
            }
            if (Runtime.IsBusy(activity))
            {
                return;
            }

            var action = activity.CurrentAction(domain);
            if (action == null)
            {
                // 计划走完：核目标是否真的达成。
                bool done = goal != null && goal.Conditions.IsSatisfiedBy(activity.Facts.Snapshot());
                EndActivity(activity, done ? CoopActivityState.Success : CoopActivityState.Failed,
                    done ? "goal_observed" : "plan_exhausted_without_goal", simTime);
                CoopLog.Event("run.end")
                    .Actor(activity.ActorId).Activity(activity.Id)
                    .Result(activity.LastResult).Reason(activity.LastReason)
                    .Detail("steps=" + (activity.Plan == null ? 0 : activity.Plan.Steps.Count))
                    .Write();
                return;
            }

            string reason;
            if (!action.Preconditions.IsSatisfiedBy(activity.Facts.Snapshot())
                || !action.Persistent.IsSatisfiedBy(activity.Facts.Snapshot()))
            {
                FailStep(activity, "conditions_invalid", simTime);
                return;
            }
            if (!Runtime.CanRun(activity, action, out reason))
            {
                FailStep(activity, reason ?? "not_eligible", simTime);
                return;
            }

            CoopLog.Event("step.requested")
                .Actor(activity.ActorId).Activity(activity.Id).PlanRevision(activity.PlanRevision)
                .Stage(action.StageKey).Step(action.Id).Owner(activity.OwnerToken)
                .Write();
            CoopLog.Event("step.accepted")
                .Actor(activity.ActorId).Activity(activity.Id).Step(action.Id)
                .Stage(action.StageKey)
                .Write();
            activity.StepStartedSim = simTime;
            int revision = ++activity.StepRevision;
            var captured = action;
            Runtime.Start(activity, captured, simTime, outcome =>
            {
                if (revision != activity.StepRevision || activity.IsTerminal) return;
                OnStepDone(activity, captured, outcome, (Runtime as ICoopRuntimeLifecycle)?.SimTime ?? simTime);
            });
        }

        private static void DebugLoggerException(Task<CoopSearchResult> pending)
        {
            // 观察后台异常，不能让它成为未处理的任务异常。
            if (pending.Exception != null) CoopLog.Event("plan.rejected").Reason("solve_exception").Write();
        }

        private void EndActivity(CoopActivity activity, CoopActivityState state, string reason, double simTime)
        {
            activity.StepRevision++;
            activity.End(state, reason, simTime);
            Runtime?.Stop(activity, reason);
        }

        private void OnStepDone(CoopActivity activity, CoopActionDef action, CoopStepOutcome outcome, double simTime)
        {
            if (!activity.IsActive)
            {
                CoopLog.Event("stale.discarded")
                    .Actor(activity.ActorId).Activity(activity.Id).Step(action.Id)
                    .Result(outcome.Kind.ToString()).Reason("activity_terminal")
                    .Write();
                return;
            }
            if (activity.State == CoopActivityState.Paused)
            {
                activity.PausedOutcome = outcome;
                activity.PausedAction = action;
                return;
            }
            if (outcome.Kind == CoopStepOutcomeKind.Completed)
            {
                (Runtime as ICoopRuntimeLifecycle)?.Refresh(activity, simTime);
                var goal = activity.Content.GoalOrNull(activity.Plan?.GoalId);
                string reason;
                if (goal == null || !goal.Constraints.IsSatisfiedBy(activity.Facts.Snapshot())
                    || !Runtime.CanRun(activity, action, out reason))
                {
                    EndActivity(activity, CoopActivityState.Interrupted, "commit_conditions_invalid", simTime);
                    return;
                }
                // 已目击等候的真实结果后，不再要求结果发生前的临时机会仍存在。
                bool observedWait = action.CapabilityId == "wait_fact" && action.Args.TryGetValue("fact", out var fact)
                    && activity.Facts.IsTrue(fact);
                if (!observedWait && !action.Persistent.IsSatisfiedBy(activity.Facts.Snapshot()))
                {
                    FailStep(activity, "persistent_condition_lost", simTime);
                    return;
                }
            }
            switch (outcome.Kind)
            {
                case CoopStepOutcomeKind.Completed:
                    if (outcome.ApplyEffects)
                    {
                        foreach (var condition in action.Effects.Items)
                        {
                            activity.Facts.Set(condition.Fact, condition.Value ? CoopTruth.True : CoopTruth.False,
                                "step:" + action.Id, simTime);
                        }
                    }
                    activity.StepIndex++;
                    CoopLog.Event("step.ended")
                        .Actor(activity.ActorId).Activity(activity.Id).Step(action.Id)
                        .Result("completed").Stage(action.StageKey)
                        .FactsVersion(activity.Facts.Version)
                        .Write();
                    break;
                case CoopStepOutcomeKind.Waiting:
                    activity.State = CoopActivityState.Waiting;
                    activity.WaitDeadlineSim = action.TimeoutGameSeconds > 0 ? simTime + action.TimeoutGameSeconds : 0;
                    CoopLog.Event("step.ended")
                        .Actor(activity.ActorId).Activity(activity.Id).Step(action.Id)
                        .Result("waiting").Reason(outcome.Reason)
                        .Write();
                    break;
                case CoopStepOutcomeKind.Failed:
                    FailStep(activity, outcome.Reason ?? "step_failed", simTime);
                    break;
                case CoopStepOutcomeKind.Interrupted:
                    EndActivity(activity, CoopActivityState.Interrupted, outcome.Reason ?? "interrupted", simTime);
                    CoopLog.Event("interrupted")
                        .Actor(activity.ActorId).Activity(activity.Id).Step(action.Id)
                        .Reason(outcome.Reason)
                        .Write();
                    break;
            }
        }

        /// <summary>等待中的活动收到真实结果：更新事实并按需重规划。</summary>
        public void ResolveWaiting(CoopActivity activity, CoopStepOutcome outcome, double simTime)
        {
            if (activity == null || activity.State != CoopActivityState.Waiting)
            {
                return;
            }
            var action = activity.CurrentAction(activity.Domain ?? Domain);
            if (action == null)
            {
                EndActivity(activity, CoopActivityState.Failed, "waiting_without_step", simTime);
                return;
            }
            activity.State = CoopActivityState.Executing;
            activity.WaitDeadlineSim = 0;
            OnStepDone(activity, action, outcome, simTime);
        }

        /// <summary>失败：记本次手段已失败，有界重规划；已提交的业务记录作硬边界保留。</summary>
        private void FailStep(CoopActivity activity, string reason, double simTime)
        {
            var action = activity.CurrentAction(activity.Domain ?? Domain);
            var content = activity.Content ?? Content;
            activity.StepRevision++;
            Runtime?.Stop(activity, reason);
            if (action != null)
            {
                activity.FailedActions.Add(action.Id);
            }
            activity.AttemptsUsed++;
            activity.LastFailureReason = reason;
            CoopLog.Event("step.ended")
                .Actor(activity.ActorId).Activity(activity.Id)
                .Step(action != null ? action.Id : "-")
                .Result("failed").Reason(reason)
                .Raw("attempts", activity.AttemptsUsed)
                .Write();

            if (activity.AttemptsUsed > content.MaxAttempts || activity.ReplansUsed >= content.MaxReplans)
            {
                EndActivity(activity, CoopActivityState.Failed, "retry_budget", simTime);
                return;
            }

            var goal = content.GoalOrNull(activity.Plan != null ? activity.Plan.GoalId : null);
            if (goal == null)
            {
                EndActivity(activity, CoopActivityState.Failed, reason, simTime);
                return;
            }
            var working = WithoutActions(activity.Domain ?? Domain, activity.FailedActions);
            activity.ReplansUsed++;
            if (Runtime is ICoopRuntimeLifecycle)
            {
                activity.PlanRevision++;
                QueueSolve(activity, goal, working);
                return;
            }
            var result = Solve(activity, activity.Facts.Snapshot(), working, goal);
            CoopLog.Event("replan.completed")
                .Actor(activity.ActorId).Activity(activity.Id)
                .Result(result.Found ? "found" : result.Status.ToString().ToLowerInvariant())
                .Reason("failed:" + reason).Raw("revision", activity.PlanRevision + 1)
                .Detail(string.Join(" -> ", result.Steps))
                .Write();
            if (!result.Found)
            {
                EndActivity(activity, CoopActivityState.Failed, "no_alternative", simTime);
                return;
            }
            activity.PlanRevision++;
            activity.Plan = CoopPlanner.ToPlan(result, activity.Algorithm, goal, activity.PlanRevision, Content.ContentVersion);
            activity.StepIndex = 0;
            activity.State = CoopActivityState.Executing;
        }

        private static CoopDomain WithoutActions(CoopDomain domain, HashSet<string> excluded)
        {
            var copy = new CoopDomain();
            foreach (var pair in domain.Actions)
            {
                if (!excluded.Contains(pair.Key))
                {
                    copy.Actions[pair.Key] = pair.Value;
                }
            }
            foreach (var pair in domain.Methods)
            {
                copy.Methods[pair.Key] = new List<CoopMethodDef>(pair.Value);
            }
            return copy;
        }

        public bool Stop(string activityId, string reason, double simTime, out string detail)
        {
            detail = null;
            var activity = Find(activityId);
            if (activity == null)
            {
                detail = "activity not found";
                return false;
            }
            if (activity.IsTerminal)
            {
                detail = "activity already " + activity.State;
                return false;
            }
            StopActivity(activity, reason ?? "player_cancelled", simTime);
            detail = "stopped " + activity.Id;
            return true;
        }

        public bool Approve(string activityId, int revision, double simTime)
        {
            var activity = Find(activityId);
            if (activity == null || !activity.IsActive || !activity.AwaitingApproval
                || activity.Plan == null || activity.PlanRevision != revision || !activity.PlayerInformed) return false;
            (Runtime as ICoopRuntimeLifecycle)?.Refresh(activity, simTime);
            var goal = activity.Content.GoalOrNull(activity.Plan.GoalId);
            if (goal == null || !goal.Constraints.IsSatisfiedBy(activity.Facts.Snapshot())) return false;
            activity.AwaitingApproval = false;
            CoopLog.Event("agreement.executed").Actor(activity.ActorId).Activity(activity.Id).PlanRevision(revision).Write();
            return true;
        }

        public void StopActivity(CoopActivity activity, string reason, double simTime)
        {
            if (activity == null || activity.IsTerminal)
            {
                return;
            }
            CoopLog.Event("cancel")
                .Actor(activity.ActorId).Activity(activity.Id)
                .Reason(reason).Owner(activity.OwnerToken)
                .Write();
            activity.StopReason = reason ?? string.Empty;
            EndActivity(activity, CoopActivityState.Cancelled, reason ?? "cancelled", simTime);
        }

        public void StopAll(string reason, double simTime)
        {
            for (int i = 0; i < _order.Count; i++)
            {
                CoopActivity activity;
                if (_byId.TryGetValue(_order[i], out activity) && activity.IsActive)
                {
                    StopActivity(activity, reason, simTime);
                }
            }
        }

        /// <summary>暂停：游戏内模态界面／对白期间不推进，恢复前重核世界状态。</summary>
        public bool Pause(string activityId, string reason, double simTime, out string detail)
        {
            detail = null;
            var activity = Find(activityId);
            if (activity == null || !activity.IsActive || activity.State == CoopActivityState.Paused)
            {
                detail = activity == null ? "activity not found" : "not pausable";
                return false;
            }
            activity.BeforePause = activity.State;
            activity.PausedAt = simTime;
            activity.State = CoopActivityState.Paused;
            (Runtime as ICoopRuntimeLifecycle)?.Pause(activity, true, simTime);
            CoopLog.Event("pause").Actor(activity.ActorId).Activity(activity.Id).Reason(reason).SimTime(simTime).Write();
            detail = "paused " + activity.Id;
            return true;
        }

        /// <summary>恢复：先核真实状态再推进；等待期限按暂停时长顺延。</summary>
        public bool Resume(string activityId, string reason, double simTime, out string detail)
        {
            detail = null;
            var activity = Find(activityId);
            if (activity == null || activity.State != CoopActivityState.Paused)
            {
                detail = activity == null ? "activity not found" : "not paused";
                return false;
            }
            activity.State = activity.BeforePause;
            if (activity.WaitDeadlineSim > 0)
            {
                activity.WaitDeadlineSim += Math.Max(0, simTime - activity.PausedAt);
            }
            (Runtime as ICoopRuntimeLifecycle)?.Pause(activity, false, simTime);
            CoopLog.Event("resume").Actor(activity.ActorId).Activity(activity.Id).Reason(reason).SimTime(simTime).Write();
            detail = "resumed " + activity.Id;
            return true;
        }

        /// <summary>
        /// 真实世界事件（玩家过门、目标到岗等）按“谁能看见／收到”写入各自事实。
        /// 这不是广播全知：调用方只在本人合法感知时调用。
        /// </summary>
        public void BroadcastFact(string fact, CoopTruth value, double simTime, string source, Func<CoopActivity, bool> canKnow)
        {
            if (string.IsNullOrEmpty(fact))
            {
                return;
            }
            for (int i = 0; i < _order.Count; i++)
            {
                CoopActivity activity;
                if (_byId.TryGetValue(_order[i], out activity) && activity.IsActive && canKnow != null && canKnow(activity))
                {
                    activity.Facts.Set(fact, value, source, simTime);
                    CoopLog.Event("fact.observed")
                        .Actor(activity.ActorId).Activity(activity.Id)
                        .Value("fact", fact).Value("value", value.ToString()).Value("source", source)
                        .SimTime(simTime)
                        .Write();
                }
            }
        }

        private static string Hash8(string hash)
        {
            if (string.IsNullOrEmpty(hash))
            {
                return "nohash";
            }
            return hash.Substring(0, Math.Min(8, hash.Length));
        }
    }
}
