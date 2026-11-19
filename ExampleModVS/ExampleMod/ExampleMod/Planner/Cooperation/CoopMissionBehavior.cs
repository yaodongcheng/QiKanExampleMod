using System;
using System.Collections.Generic;
using System.IO;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.Planner.Cooperation
{
    /// <summary>
    /// Mission 侧宿主：装载协作内容、驱动活动执行与运行时，并在离场时清理。
    /// 规划层与正文数据全部走 JSON；这里只提供通用机制。
    /// </summary>
    public sealed class CoopMissionBehavior : MissionLogic
    {
        public static CoopMissionBehavior Current { get; private set; }

        private double _simTime;
        private bool _installed;
        private readonly HashSet<string> _modalPaused = new HashSet<string>(StringComparer.Ordinal);

        public CoopMissionRuntime Runtime { get; private set; }
        public string LoadError { get; private set; }

        public double SimTime { get { return _simTime; } }

        public static string ContentPath
        {
            get
            {
                var folder = StoryProbeDataFiles.DesignDataFolder;
                return string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, "Cooperation", "cooperation.json");
            }
        }

        public override void OnCreated()
        {
            base.OnCreated();
            // 子模块追加行为时引擎只调用 OnCreated，不会补调 OnBehaviorInitialize。
            // 此时只登记宿主；内容和演员适配仍等 AfterStart 再安装。
            Current = this;
            DebugLogger.Log("[Coop] mission host registered in OnCreated");
        }

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            Current = this;
            Install();
        }

        public override void AfterStart()
        {
            base.AfterStart();
            if (!_installed)
            {
                Install();
            }
        }

        private void Install()
        {
            if (_installed)
            {
                return;
            }
            _installed = true;
            var path = ContentPath;
            var content = CoopContentData.Load(path);
            ValidateTextKeys(content);
            if (!content.IsValid)
            {
                LoadError = string.Join("; ", content.Errors.ToArray());
                DebugLogger.Log("[Coop] content rejected: " + LoadError);
                CoopLog.Event("config.rejected")
                    .Value("path", path).Detail(LoadError)
                    .Write();
                return;
            }
            CoopLog.Sink = DebugLogger.Log;
            var director = CoopDirector.Install(content, Availability, Mission.Current != null ? Mission.Current.GetHashCode() : 0);
            if (!Settings.Instance.IsLLMConfigured)
            {
                CoopLog.Event("polish.discarded").Reason("llm_not_configured").Write();
            }
            Runtime = new CoopMissionRuntime(StoryProbeMissionBehavior.Current);
            director.Runtime = Runtime;
            CoopRequestRouter.Reset();
            CoopLog.Event("config.validated")
                .Value("version", content.ContentVersion).Value("hash", content.ConfigHash)
                .Raw("capabilities", content.Domain.Actions.Count).Raw("goals", content.Goals.Count)
                .Write();
            CoopLog.Event("config.published")
                .Value("path", path).Value("version", content.ContentVersion)
                .Value("hash", content.ConfigHash)
                .Write();
        }

        /// <summary>目录可用性：没有运行时适配器的能力如实标成不可规划。</summary>
        internal static string Availability(CoopActionDef action)
        {
            if (action == null)
            {
                return "null_action";
            }
            return CoopContentData.HasRuntimeContract(action.CapabilityId) ? null : "no_runtime_adapter";
        }

        private static void ValidateTextKeys(CoopContentData content)
        {
            var keys = new HashSet<string>(content.StageTextKeys.Values);
            foreach (var goal in content.Goals) keys.Add(goal.DisplayKey);
            foreach (var action in content.Domain.Actions.Values)
            {
                if (!string.IsNullOrEmpty(action.DisplayKey)) keys.Add(action.DisplayKey);
                if (!string.IsNullOrEmpty(action.DescriptionKey)) keys.Add(action.DescriptionKey);
                if (action.Args.TryGetValue("line", out var key)) keys.Add(key);
                if (action.Args.TryGetValue("replyLine", out var replyKey)) keys.Add(replyKey);
            }
            foreach (var role in content.Roles)
            {
                foreach (var key in role.Presentation.Values) keys.Add(key);
                foreach (var policy in role.Requests.Values)
                {
                    keys.Add(policy.AcceptLine); keys.Add(policy.RefuseLine); keys.Add(policy.IneligibleLine);
                    foreach (var lines in policy.ActorLines.Values) foreach (var key in lines.Values) keys.Add(key);
                }
            }
            foreach (var key in keys) if (!string.IsNullOrEmpty(key) && !LWNTextHelper.HasEnglishKey(key))
                content.Errors.Add("unknown text key '" + key + "'");
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            var director = CoopDirector.Current;
            if (director == null || Runtime == null)
            {
                return;
            }
            bool modal = ConversationEntryPatch.IsConversationActive();
            foreach (var activity in new List<CoopActivity>(director.Activities))
            {
                string detail;
                if (modal && activity.IsActive && activity.State != CoopActivityState.Paused)
                {
                    director.Pause(activity.Id, "conversation", _simTime, out detail);
                    _modalPaused.Add(activity.Id);
                }
                else if (!modal && _modalPaused.Remove(activity.Id) && activity.State == CoopActivityState.Paused)
                    director.Resume(activity.Id, "conversation_closed", _simTime, out detail);
            }
            if (modal) return;
            _simTime += dt;
            CoopRequestRouter.Tick(_simTime);
            Runtime.Tick(dt, _simTime);
            // 全场新规划共享每帧 CPU 预算：本版按固定上限分帧推进，
            // 活动层只在状态变化时推进（不在每帧重算）。
            director.Tick(dt, _simTime);
            CoopView.Tick(director, _simTime);
        }

        public override void OnRemoveBehavior()
        {
            try
            {
                var director = CoopDirector.Current;
                if (director != null)
                {
                    director.StopAll("scene_exit", _simTime);
                }
                CoopRequestRouter.Reset();
                CoopTestSession.End();
                CoopView.Reset();
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[Coop] teardown failed: " + ex.Message);
            }
            CoopDirector.Uninstall(0);
            if (Current == this)
            {
                Current = null;
            }
            base.OnRemoveBehavior();
        }

        // ---- 命令入口 -------------------------------------------------

        public string DefaultActorId()
        {
            var host = StoryProbeMissionBehavior.Current;
            string saved = StoryProbeState.Data.CompanionHeroId;
            if (!string.IsNullOrEmpty(saved) && host?.GetAgent(saved) != null
                && CoopRecruitment.CanCooperate(saved, null, out var reason)) return saved;
            return CoopDirector.Current?.Content?.Recruitment.Candidate;
        }

        public string ReloadContent()
        {
            var next = CoopContentData.Load(ContentPath);
            ValidateTextKeys(next);
            if (!next.IsValid)
            {
                DebugLogger.Log("[Coop] reload rejected: " + string.Join("; ", next.Errors));
                return "FAIL: cooperation content invalid; previous version retained; inspect runtime log";
            }
            var director = CoopDirector.Current;
            if (director == null) { _installed = false; LoadError = null; Install(); director = CoopDirector.Current; }
            else director.Reload(next, Availability);
            if (director == null) return "FAIL: cooperation host could not install";
            LoadError = null;
            CoopLog.Event("config.published").Value("version", next.ContentVersion).Value("hash", next.ConfigHash).Write();
            return "OK: cooperation version=" + next.ContentVersion + " hash=" + next.ConfigHash
                + "; new activities use it; active activities retain their version";
        }

        public CoopActivity StartCooperation(string actorId, string roleId, out string reason, CoopAlgorithm? overrideAlgorithm = null, bool preview = false)
        {
            return StartCooperationCore(actorId, roleId, out reason, overrideAlgorithm, preview, false);
        }

#if DEBUG
        /// <summary>仅未绑定的quick诊断准备真实参与者；不把场景准备当本人目击，也不提前执行。</summary>
        internal CoopActivity StartPreparedDiagnostic(string actorId, string roleId, out string reason, CoopAlgorithm algorithm)
        {
            if (!CoopTestSession.AllowsParticipantPreparation(actorId))
            { reason = "no_prepared_diagnostic"; return null; }
            return StartCooperationCore(actorId, roleId, out reason, algorithm, true, true);
        }
#endif

        private CoopActivity StartCooperationCore(string actorId, string roleId, out string reason,
            CoopAlgorithm? overrideAlgorithm, bool preview, bool preparedDiagnostic)
        {
            reason = null;
            var director = CoopDirector.Current;
            if (director == null || director.Content == null)
            {
                reason = "content_not_loaded";
                return null;
            }
            if (!director.Content.IsValid)
            {
                reason = "content_invalid";
                return null;
            }
            var role = FindRole(roleId);
            if (role == null)
            {
                reason = "unknown_role";
                return null;
            }
            if (role.RequiresContract && !CoopRecruitment.CanCooperate(actorId, null, out reason)) return null;
            var seed = BuildInitialFacts(actorId);
            var participantRole = director.Content.RoleOrNull(role.ParticipantRole);
            if (participantRole != null)
            {
                int present = 0;
                bool allKnown = true;
                var observer = Runtime.ResolveAgent(actorId);
                foreach (var id in participantRole.Actors)
                {
                    var target = Runtime.ResolveAgent(id);
                    bool known;
                    bool exists;
                    if (preparedDiagnostic)
                    {
                        if (!CoopMissionRuntime.BodyAvailable(target, out reason))
                        { reason = "participant_unavailable:" + id + ":" + reason; return null; }
                        if (AgentAIController.GetBrainForAgent(target)?.HasDutyPoint != true)
                        { reason = "participant_without_duty:" + id; return null; }
                        known = exists = true;
                        CoopLog.Event("test.participant_prepared").Actor(id).Value("source", "scene_setup")
                            .Detail("real body and duty; startup observation only bypassed; execution checks retained").Write();
                    }
                    else
                    {
                        known = CoopMissionRuntime.CanSeeWithin(observer, target, 30f)
                            || CoopMissionRuntime.CanSeeWithin(Agent.Main, target, 30f);
                        exists = known && AgentAIController.GetBrainForAgent(target)?.HasDutyPoint == true;
                    }
                    seed["present_" + id] = known ? (exists ? CoopTruth.True : CoopTruth.False) : CoopTruth.Unknown;
                    seed["handled_" + id] = known ? (exists ? CoopTruth.False : CoopTruth.True) : CoopTruth.Unknown;
                    seed["near_" + id] = CoopTruth.False;
                    allKnown &= known;
                    if (exists) present++;
                }
                if (!allKnown) { reason = "participants_not_observed; approach the gate and look at the guards before discussing the plan"; return null; }
                seed["single_guard"] = present == 1 ? CoopTruth.True : CoopTruth.False;
                seed["guided"] = CoopTruth.False;
            }
            var goal = SelectRoleGoal(role, director.Content, seed);
            if (goal == null)
            {
                reason = "no_eligible_goal";
                return null;
            }
            var algorithm = overrideAlgorithm ?? ParseAlgorithm(role.Algorithm);
            var activity = director.StartSeeded(actorId, role.Id, goal, algorithm,
                preparedDiagnostic ? "test_scene_setup" : "player_order", _simTime, seed, out reason);
            if (activity != null) activity.AwaitingApproval = preview;
            return activity;
        }

        public CoopActivity StartRoleActivity(string actorId, CoopRoleDef role, CoopGoalDef requestedGoal,
            IDictionary<string, CoopTruth> facts, string partner, string source, out string reason,
            CoopContentData pinnedContent = null, CoopDomain pinnedDomain = null)
        {
            reason = null;
            if (role == null || requestedGoal == null || !role.GoalIds.Contains(requestedGoal.Id))
            { reason = "goal_not_in_own_role"; return null; }
            var activity = CoopDirector.Current.StartSeeded(actorId, role.Id, requestedGoal, ParseAlgorithm(role.Algorithm),
                source, _simTime, facts, out reason, pinnedContent, pinnedDomain);
            if (activity != null) activity.InteractionPartnerId = partner;
            return activity;
        }

        /// <summary>
        /// 发起时本人已知道的事实：约定已成立、门口情况尚未查明。
        /// 未知不写成真；这里只写“确实知道为假”的初始状态与他人数量这类可观察事实。
        /// </summary>
        private static Dictionary<string, CoopTruth> BuildInitialFacts(string actorId)
        {
            var host = StoryProbeMissionBehavior.Current;
            var agent = host?.GetAgent(actorId);
            string reason;
            var seed = new Dictionary<string, CoopTruth>(StringComparer.Ordinal)
            {
                { "alive", CoopMissionRuntime.BodyAvailable(agent, out reason) ? CoopTruth.True : CoopTruth.False },
                { "cancelled", CoopTruth.False },
                { "companion_ready", CoopRecruitment.CanCooperate(actorId, null, out reason) ? CoopTruth.True : CoopTruth.False },
                { "talk_authorized", CoopRecruitment.CanRequestPurpose(actorId, null, "talk", out reason) ? CoopTruth.True : CoopTruth.False },
                { "investigation_authorized", CoopRecruitment.CanRequestPurpose(actorId, null, "investigate", out reason) ? CoopTruth.True : CoopTruth.False },
                { "briefed", CoopTruth.True },
                { "near_guard", CoopTruth.False },
                { "engaged", CoopTruth.False },
                { "off_post", CoopTruth.False },
                { "gap", CoopTruth.False },
                { "signal", CoopTruth.False },
                { "passed", CoopTruth.False },
                // 尚未去现场观察，人数不等于“此刻还在看守”；真实适配器会更新。
                { "other_watcher", CoopTruth.Unknown }
            };
            return seed;
        }

        public static int CountGateGuards()
        {
            var host = StoryProbeMissionBehavior.Current;
            if (host == null)
            {
                return 0;
            }
            int count = 0;
            foreach (var def in StoryProbeDefs.Npcs)
            {
                if (def == null || !def.FirstMeetGuard)
                {
                    continue;
                }
                var agent = host.GetAgent(def.Id);
                if (agent != null && agent.IsActive())
                {
                    count++;
                }
            }
            return count;
        }

        private CoopRoleDef FindRole(string roleId)
        {
            var content = CoopDirector.Current != null ? CoopDirector.Current.Content : null;
            if (content == null || string.IsNullOrEmpty(roleId))
            {
                return null;
            }
            foreach (var role in content.Roles)
            {
                if (string.Equals(role.Id, roleId, StringComparison.OrdinalIgnoreCase))
                {
                    return role;
                }
            }
            return null;
        }

        internal static CoopGoalDef SelectRoleGoal(CoopRoleDef role, CoopContentData content, IDictionary<string, CoopTruth> facts)
        {
            CoopGoalDef selected = null;
            foreach (var id in role.GoalIds)
            {
                var goal = content.GoalOrNull(id);
                if (goal != null && goal.Constraints.IsSatisfiedBy(facts)
                    && (selected == null || goal.Priority > selected.Priority))
                {
                    selected = goal;
                }
            }
            return selected;
        }

        public static CoopAlgorithm ParseAlgorithm(string text)
        {
            CoopAlgorithm algorithm;
            return !string.IsNullOrEmpty(text) && Enum.TryParse(text, true, out algorithm)
                ? algorithm
                : CoopAlgorithm.Hybrid;
        }

        /// <summary>玩家真实越过门线：把结果写给在场、并且能知道这件事的本人活动。</summary>
        public void NotifyPlayerCrossed()
        {
            var director = CoopDirector.Current;
            if (director == null)
            {
                return;
            }
            foreach (var activity in director.Activities)
            {
                var actor = Runtime.ResolveAgent(activity.ActorId);
                if (activity.IsActive && actor != null && NpcSightSystem.CanAgentSeeTarget(actor, Agent.Main, 30f))
                {
                    activity.Facts.Set("player_crossed", CoopTruth.True, "witnessed_crossing", _simTime);
                    CoopLog.Event("fact.observed").Actor(activity.ActorId).Activity(activity.Id).Value("fact", "player_crossed").Write();
                }
            }
            CoopLog.Event("player.crossed").SimTime(_simTime).Write();
        }

        public string StatusText()
        {
            var director = CoopDirector.Current;
            if (director == null)
            {
                return "NO: no cooperation session (content not loaded)";
            }
            var activities = new List<CoopActivity>(director.Activities);
            if (activities.Count == 0)
            {
                return "NO: no cooperation candidate or activity";
            }
            var builder = new System.Text.StringBuilder();
            builder.Append("OK: run=").Append(director.RunId)
                .Append(" sceneEpoch=").Append(director.SceneEpoch)
                .Append(" content=").Append(director.Content != null ? director.Content.ContentVersion : "-")
                .Append(" hash=").Append(director.Content != null && director.Content.ConfigHash.Length > 8
                    ? director.Content.ConfigHash.Substring(0, 8) : "-")
                .Append(" budgetNodes=").Append(director.Content != null ? director.Content.Budget.MaxNodes : 0)
                .Append(" catalogPlannable=").Append(director.Catalog != null ? director.Catalog.PlannableCount : 0);
            foreach (var activity in activities)
            {
                builder.Append("\n  ").Append(activity.Id)
                    .Append(" actor=").Append(activity.ActorId)
                    .Append(" role=").Append(activity.RoleId)
                    .Append(" state=").Append(activity.State)
                    .Append(" algo=").Append(activity.Algorithm)
                    .Append(" revision=").Append(activity.PlanRevision)
                    .Append(" step=").Append(activity.StepIndex)
                    .Append('/').Append(activity.Plan != null ? activity.Plan.Steps.Count : 0)
                    .Append(" owner=").Append(activity.OwnerToken)
                    .Append(" result=").Append(string.IsNullOrEmpty(activity.LastResult) ? "-" : activity.LastResult)
                    .Append(" opportunity=").Append(activity.Facts.IsTrue("gap") ? "yes" : "no")
                    .Append(" crossed=").Append(activity.Facts.IsTrue("player_crossed") ? "yes" : "no")
                    .Append(" signal=").Append(activity.Facts.IsTrue("signal") ? "yes" : "no");
                if (!string.IsNullOrEmpty(activity.LastReason))
                {
                    builder.Append(" reason=").Append(activity.LastReason);
                }
            }
            return builder.ToString();
        }

        private static string TraceRecent(int count)
        {
            var recent = CoopLog.Recent();
            var builder = new System.Text.StringBuilder();
            int from = Math.Max(0, recent.Count - count);
            for (int i = from; i < recent.Count; i++)
            {
                builder.Append("\n  ").Append(recent[i]);
            }
            return builder.ToString();
        }

        public string PlanText()
        {
            var director = CoopDirector.Current;
            if (director == null || director.Content == null)
            {
                return "NO: no cooperation content";
            }
            var builder = new System.Text.StringBuilder("OK: plan");
            bool any = false;
            foreach (var activity in director.Activities)
            {
                if (!activity.IsActive)
                {
                    continue;
                }
                any = true;
                builder.Append("\n[").Append(activity.ActorId).Append(" / ").Append(activity.Id).Append(']');
                builder.Append("\n  goal=").Append(activity.Plan?.GoalId ?? "pending")
                    .Append(" algorithm=").Append(activity.Algorithm).Append(" revision=").Append(activity.PlanRevision)
                    .Append(" steps=").Append(activity.Plan != null ? string.Join(" -> ", activity.Plan.Steps) : "pending")
                    .Append(" state=").Append(activity.State);
            }
            return any ? builder.ToString() : "NO: no active cooperation plan";
        }

        public string TraceText(string activityId)
        {
            var director = CoopDirector.Current;
            if (director == null)
            {
                return "NO: no cooperation session";
            }
            var activity = string.IsNullOrEmpty(activityId) ? null : director.Find(activityId);
            if (activity == null && !string.IsNullOrEmpty(activityId))
            {
                activity = director.FindByRun(activityId);
                if (activity == null)
                {
                    return "NO: run '" + activityId + "' not found (kept records limited); last 20 in-memory records"
                        + TraceRecent(20) + "\n  full log: <module>\\Debug\\StoryEngine_RuntimeLog.txt";
                }
            }
            var builder = new System.Text.StringBuilder("OK: trace");
            if (activity == null)
            {
                builder.Append("\n  recent log records:");
                var recent = CoopLog.Recent();
                int from = Math.Max(0, recent.Count - 20);
                for (int i = from; i < recent.Count; i++)
                {
                    builder.Append("\n  ").Append(recent[i]);
                }
                return builder.ToString();
            }
            builder.Append("\n  ").Append(activity.Id).Append(" actor=").Append(activity.ActorId)
                .Append(" state=").Append(activity.State);
            foreach (var line in activity.History)
            {
                builder.Append("\n  ").Append(line);
            }
            var facts = activity.Facts.Entries();
            builder.Append("\n  facts:");
            foreach (var pair in facts)
            {
                builder.Append(' ').Append(pair.Key).Append('=').Append(pair.Value.Truth);
            }
            return builder.ToString();
        }
    }
}
