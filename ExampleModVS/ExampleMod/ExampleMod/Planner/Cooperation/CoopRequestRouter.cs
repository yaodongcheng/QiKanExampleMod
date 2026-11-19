using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.Planner.Cooperation
{
    /// <summary>请求只携带目的和筹码；受理规则、答复和职责目标来自接受者自己的角色数据。</summary>
    public static class CoopRequestRouter
    {
        private sealed class Pending
        {
            public CoopMissionRuntime Runtime;
            public CoopActivity Activity;
            public CoopActionDef Action;
            public Agent Actor, Target;
            public CoopRoleDef Role;
            public CoopResponseDef Policy;
            public string TargetId;
            public Action<CoopStepOutcome> Finish;
            public PerformanceAction Lease;
            public bool Resolved, Done, Paused, ReliefArrived, LineQueued, LineShown, ReplyShown, ReplyHeard;
            public int DialoguePhase;
            public double SpeechEndsAt;
            public int SpeechToken;
            public CoopAttemptRecord Response;
            public string Refusal;
            public double Started;
        }
        private static readonly Dictionary<string, Pending> _pending = new Dictionary<string, Pending>(StringComparer.Ordinal);
        private static readonly Dictionary<int, Vec3> _posts = new Dictionary<int, Vec3>();
        public static Vec3 GuardPost(int index) => _posts.TryGetValue(index, out var point) ? point : Vec3.Zero;
        public static void RememberPost(int index, Vec3 point, Vec3 facing = default(Vec3)) { _posts[index] = point; }
        public static void Reset()
        {
            foreach (var item in new List<Pending>(_pending.Values)) Cancel(item.Activity.Id, "scene_exit");
            _posts.Clear();
        }
        public static void Submit(CoopMissionRuntime runtime, CoopActivity activity, CoopActionDef action,
            Agent actor, Agent target, Action<CoopStepOutcome> finish)
        {
            var targetId = runtime.IdOf(target);
            var content = activity.Content;
            var role = content?.Roles.Find(r => r.Actors.Contains(targetId));
            string purpose = CoopMissionRuntime.Arg(action, "purpose", "talk");
            if (role == null || !role.Requests.TryGetValue(purpose, out var policy))
            { finish(CoopStepOutcome.Failed("request_not_supported")); return; }
            if (!CoopMissionRuntime.BodyAvailable(target, out var reason) || !CoopMissionRuntime.CanHear(target, actor))
            { finish(CoopStepOutcome.Failed(reason ?? "request_not_heard")); return; }
            var brain = AgentAIController.GetBrainForAgent(target);
            if (!brain.HasDutyPoint) { finish(CoopStepOutcome.Failed("duty_unavailable")); return; }
            RememberPost(target.Index, brain.DutyPoint);
            var existing = CoopDirector.Current?.ForActor(targetId);
            if (existing != null && existing.IsActive) { finish(CoopStepOutcome.Failed("receiver_busy")); return; }
            var pending = new Pending { Runtime = runtime, Activity = activity, Action = action, Actor = actor,
                Target = target, TargetId = targetId, Role = role, Policy = policy, Finish = finish, Started = runtime.SimTime };
            pending.Lease = PerformanceAction.TryAcquire(target, "coop-reply:" + activity.RunId + ":" + activity.OwnerToken,
                replaceAmbient: true);
            if (pending.Lease == null) { finish(CoopStepOutcome.Failed("receiver_brain_refused")); return; }
            pending.Lease.PauseOnConversation = true;
            _pending[activity.Id] = pending;
            CoopLog.Event("interaction.received").Actor(targetId).Activity(activity.Id).Value("purpose", purpose).Write();
        }
        public static void Tick(double time)
        {
            foreach (var pending in new List<Pending>(_pending.Values))
            {
                if (pending.Done) continue;
                if (pending.Activity.IsTerminal) { Cancel(pending.Activity.Id, "sender_terminal"); continue; }
                bool paused = pending.Activity.State == CoopActivityState.Paused;
                Pause(pending.Activity.Id, paused);
                if (paused) continue;
                if (!CoopMissionRuntime.BodyAvailable(pending.Target, out var reason) || pending.Lease.Ended)
                { Complete(pending, CoopStepOutcome.Failed(reason ?? "receiver_interrupted")); continue; }
                if (!pending.Lease.CanControl) continue;
                if (!CoopMissionRuntime.CanHear(pending.Actor, pending.Target))
                { Complete(pending, CoopStepOutcome.Failed(pending.Resolved ? "reply_not_heard" : "request_not_heard")); continue; }
                pending.Lease.FaceTarget(pending.Actor);
                if (pending.Resolved)
                {
                    if (pending.ReplyShown && time >= pending.SpeechEndsAt) FinishReply(pending);
                    continue;
                }
                if (pending.DialoguePhase < 2) { AdvanceDialogue(pending, time); continue; }
                if (pending.Policy.RequiresRelief && !HasRelief(pending.Target))
                {
                    if (!RequestRelief(pending)) { Answer(pending, null, "last_sentry_cannot_leave"); continue; }
                    // 接替在独立Brain内走位；必须收到实际到岗证据才继续。
                    continue;
                }
                if (pending.Policy.RequiresRelief && !pending.ReliefArrived)
                {
                    pending.ReliefArrived = true;
                    CoopLog.Event("post.replacement_arrived").Actor(pending.TargetId).Activity(pending.Activity.Id).Write();
                }
                Resolve(pending, time);
            }
        }
        /// <summary>先质疑、再解释；每句真实上屏且读完后才轮到另一人，途中不掷骰或提前派守卫。</summary>
        private static void AdvanceDialogue(Pending pending, double time)
        {
            if (pending.LineShown)
            {
                if (time < pending.SpeechEndsAt) return;
                pending.DialoguePhase++;
                pending.LineShown = pending.LineQueued = false;
                return;
            }
            if (pending.LineQueued) return;
            string key = pending.DialoguePhase == 0 ? pending.Policy.LineFor(pending.TargetId, "challenge")
                : CoopMissionRuntime.Arg(pending.Action, "replyLine", null);
            if (string.IsNullOrEmpty(key)) { pending.DialoguePhase++; return; }
            pending.LineQueued = true;
            int phase = pending.DialoguePhase, token = ++pending.SpeechToken;
            var speaker = phase == 0 ? pending.Target : pending.Actor;
            var listener = phase == 0 ? pending.Actor : pending.Target;
            string text = LWNTextHelper.ResolveText(key, key);
            float duration = Math.Max(3f, SpeechChannel.EstimateDuration(text));
            SpeechChannel.Say(speaker, text, SpeechPriority.Dialogue,
                SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(speaker), listener: listener), duration: duration,
                onShown: () =>
                {
                    if (pending.SpeechToken != token || !CanPublishAnswer(pending, token)) return;
                    pending.LineShown = true;
                    pending.SpeechEndsAt = pending.Runtime.SimTime + duration;
                    CoopLog.Event("dialogue.turn_shown").Actor(pending.Runtime.IdOf(speaker)).Activity(pending.Activity.Id)
                        .Value("textKey", key).Raw("duration", duration).Raw("endsAt", pending.SpeechEndsAt).Write();
                }, canPublish: () => CanPublishAnswer(pending, token),
                canForwardToNearby: () => CoopMissionRuntime.CanHear(Agent.Main, speaker));
        }
        private static void Resolve(Pending pending, double time)
        {
            string purpose = CoopMissionRuntime.Arg(pending.Action, "purpose", "talk");
            var hero = pending.Runtime.Host?.GetHero(pending.Activity.ActorId);
            if (hero != null && CoopRecruitment.GetMode(hero) == CoopContractMode.Temporary && !pending.Activity.FavorCommitted)
            {
                if (!CoopRecruitment.ConsumeFavor(hero, purpose, time, pending.Activity.Id))
                { Complete(pending, CoopStepOutcome.Failed("favor_insufficient")); return; }
                pending.Activity.FavorCommitted = true;
            }
            var offer = CoopMissionRuntime.ReadFloat(pending.Action, "offer", 0f);
            var key = new CoopAttemptKey { ActorId = pending.Activity.ActorId, TargetId = pending.TargetId,
                Purpose = purpose, Method = CoopMissionRuntime.Arg(pending.Action, "method", "talk"),
                Offer = offer.ToString("0.###", CultureInfo.InvariantCulture),
                Period = CoopMissionRuntime.Arg(pending.Action, "period", "coop") };
            var skill = GetSkill(pending.Policy.Skill);
            if (skill == null) { Complete(pending, CoopStepOutcome.Failed("unmodeled_skill")); return; }
            var result = CoopInteraction.Resolve(CoopDirector.Current.Ledger, key, true, null,
                () => SingleRollResolver.SimpleChance(pending.Actor.Character.GetSkillValue(skill), pending.Target.Character.GetSkillValue(skill), offer),
                () => CoopTestSession.NextRoll(pending.Activity.Id), time, "coop:" + purpose,
                CoopMissionRuntime.Arg(pending.Action, "evidence", null), SingleRollResolver.Roll);
            CoopLog.Event(result.Reused ? "attempt.reused" : "roll.committed").Actor(pending.Activity.ActorId)
                .Activity(pending.Activity.Id).Attempt(result.Attempt.AttemptId).Raw("roll", result.Attempt.Roll)
                .Raw("threshold", result.Attempt.Threshold).Raw("actorSkill", pending.Actor.Character.GetSkillValue(skill))
                .Raw("targetSkill", pending.Target.Character.GetSkillValue(skill)).Value("skill", skill.StringId)
                .Value("rng", CoopTestSession.Owns(pending.Activity.Id) ? "test_session" : "game")
                .Result(result.Attempt.Success ? "success" : "failed").Write();
            Answer(pending, result.Attempt, null);
        }
        private static SkillObject GetSkill(string id)
        {
            // 技能注册表与正式交互同源，未知标识显式拒绝。
            if (string.IsNullOrEmpty(id)) return null;
            return TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<SkillObject>(id.ToLowerInvariant())
                ?? TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<SkillObject>(s => s.StringId.Equals(id, StringComparison.OrdinalIgnoreCase));
        }
        private static void Answer(Pending pending, CoopAttemptRecord record, string refusal)
        {
            pending.Resolved = true;
            pending.Response = record;
            pending.Refusal = refusal;
            pending.ReplyShown = false;
            int speechToken = ++pending.SpeechToken;
            bool success = record != null && record.Success;
            string key = pending.Policy.LineFor(pending.TargetId, refusal != null ? "ineligible" : success ? "accept" : "refuse");
            if (string.IsNullOrEmpty(key)) { Complete(pending, CoopStepOutcome.Failed("reply_missing")); return; }
            string text = LWNTextHelper.ResolveText(key, key);
            float duration = Math.Max(3f, SpeechChannel.EstimateDuration(text));
            SpeechChannel.Say(pending.Target, text, SpeechPriority.Dialogue,
                SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(pending.Target), pending.Actor, listener: pending.Actor),
                duration: duration,
                onShown: () =>
                {
                    if (pending.SpeechToken != speechToken || pending.Done || !pending.Activity.IsActive || pending.Paused) return;
                    pending.ReplyHeard = CoopMissionRuntime.CanHear(pending.Actor, pending.Target);
                    pending.ReplyShown = true;
                    pending.SpeechEndsAt = pending.Runtime.SimTime + duration;
                    CoopLog.Event("answer.published").Actor(pending.TargetId).Activity(pending.Activity.Id).Value("textKey", key)
                        .Raw("duration", duration).Raw("endsAt", pending.SpeechEndsAt).Write();
                    CoopLog.Event(pending.ReplyHeard ? "answer.observed" : "answer.discarded").Actor(pending.Activity.ActorId).Activity(pending.Activity.Id)
                        .Target(pending.TargetId).Result(success ? "accepted" : "refused").Write();
                }, canPublish: () => CanPublishAnswer(pending, speechToken),
                canForwardToNearby: () => CoopMissionRuntime.CanHear(Agent.Main, pending.Target));
            CoopLog.Event("answer.queued").Actor(pending.TargetId).Activity(pending.Activity.Id).Value("textKey", key).Write();
        }
        private static void FinishReply(Pending pending)
        {
            if (!CanPublishAnswer(pending, pending.SpeechToken))
            { Complete(pending, CoopStepOutcome.Failed("reply_conditions_expired")); return; }
            bool success = pending.Response?.Success == true;
            if (success && pending.ReplyHeard)
            {
                var seed = OwnFacts(pending.Target);
                if (!string.IsNullOrEmpty(pending.Policy.HeardFact)) seed[pending.Policy.HeardFact] = CoopTruth.True;
                var goal = CoopMissionBehavior.SelectRoleGoal(pending.Role, pending.Activity.Content, seed);
                if (goal == null || !goal.Constraints.IsSatisfiedBy(seed))
                { Complete(pending, CoopStepOutcome.Failed("receiver_goal_ineligible")); return; }
                string why;
                var started = CoopMissionBehavior.Current?.StartRoleActivity(pending.TargetId, pending.Role, goal, seed,
                    pending.Activity.ActorId, "received_request", out why, pending.Activity.Content, pending.Activity.Domain);
                if (started == null) { Complete(pending, CoopStepOutcome.Failed("receiver_plan_rejected")); return; }
                if (!pending.Runtime.AdoptReplyLease(started, pending.Lease, pending.Activity.Id))
                {
                    CoopDirector.Current.StopActivity(started, "reply_handoff_rejected", pending.Runtime.SimTime);
                    Complete(pending, CoopStepOutcome.Failed("receiver_handoff_rejected"));
                    return;
                }
                // 资源已归接受者活动；旧请求及迟到的对白回调不能再释放它。
                pending.Lease = null;
                started.RequestActivityId = pending.Activity.Id;
                CoopLog.Event("guard.choice").Actor(pending.TargetId).Activity(started.Id).Value("goal", goal.Id).Write();
            }
            Complete(pending, !pending.ReplyHeard ? CoopStepOutcome.Failed("reply_not_heard")
                : success ? CoopStepOutcome.Completed() : CoopStepOutcome.Failed(pending.Refusal ?? "refused"));
        }
        private static bool CanPublishAnswer(Pending pending, int speechToken)
        {
            if (pending.SpeechToken != speechToken || pending.Done || !pending.Activity.IsActive
                || pending.Paused || !pending.Lease.CanControl) return false;
            pending.Runtime.Refresh(pending.Activity, pending.Runtime.SimTime);
            string reason;
            if (!CoopMissionRuntime.BodyAvailable(pending.Target, out reason)
                || !pending.Runtime.CanRun(pending.Activity, pending.Action, out reason)
                || !pending.Action.Persistent.IsSatisfiedBy(pending.Activity.Facts.Snapshot())
                || (pending.Response?.Success == true && pending.Policy.RequiresRelief && !HasRelief(pending.Target)))
            {
                CoopLog.Event("answer.discarded").Actor(pending.TargetId).Activity(pending.Activity.Id)
                    .Reason(reason ?? "reply_conditions_expired").Write();
                return false;
            }
            return true;
        }
        internal static Dictionary<string, CoopTruth> OwnFacts(Agent guard)
        {
            return new Dictionary<string, CoopTruth>(StringComparer.Ordinal) {
                {"alive", CoopTruth.True}, {"cancelled", CoopTruth.False},
                {"at_post", guard.Position.Distance(GuardPost(guard.Index)) <= 1.7f ? CoopTruth.True : CoopTruth.False},
                {"watching", CoopTruth.False}, {"report_checked", CoopTruth.False}, {"report_heard", CoopTruth.False},
                {"talk_heard", CoopTruth.False}, {"relief_confirmed", HasRelief(guard) ? CoopTruth.True : CoopTruth.False},
                {"cover_requested", CoopTruth.False}, {"covering", CoopTruth.False}, {"coverage_done", CoopTruth.False},
                {"conversation_held", CoopTruth.False}
                , {"lure_heard", CoopTruth.False}, {"guide_departed", CoopTruth.False}
            };
        }
        public static bool HasRelief(Agent guard)
        {
            var director = CoopDirector.Current;
            var runtime = CoopMissionBehavior.Current?.Runtime;
            if (director == null || runtime == null || guard == null) return false;
            foreach (var activity in director.Activities)
            {
                if (!activity.IsActive || !activity.Facts.IsTrue("covering") || runtime.ResolveAgent(activity.InteractionPartnerId) != guard) continue;
                var covering = runtime.ResolveAgent(activity.ActorId);
                if (CoopMissionRuntime.BodyAvailable(covering, out var reason) && runtime.HasControl(activity)
                    && covering.Position.Distance(GuardPost(guard.Index)) <= 1.7f) return true;
            }
            return false;
        }
        private static bool RequestRelief(Pending pending)
        {
            var goal = pending.Activity.Content.GoalOrNull(pending.Policy.ReliefGoal);
            var host = pending.Runtime.Host;
            var behavior = CoopMissionBehavior.Current;
            if (goal == null || host == null || behavior == null) return false;
            foreach (var id in pending.Role.Actors)
            {
                var agent = host.GetAgent(id);
                if (agent == pending.Target || !CoopMissionRuntime.BodyAvailable(agent, out var reason)
                    || !CoopMissionRuntime.CanHear(agent, pending.Target)) continue;
                var existing = CoopDirector.Current.ForActor(id);
                if (existing != null && existing.IsActive && existing.InteractionPartnerId == pending.TargetId) return true;
                if (existing != null && existing.IsActive) continue;
                var brain = AgentAIController.GetBrainForAgent(agent);
                if (!brain.HasDutyPoint) continue;
                RememberPost(agent.Index, brain.DutyPoint);
                var facts = OwnFacts(agent);
                facts["cover_requested"] = CoopTruth.True;
                var started = behavior.StartRoleActivity(id, pending.Role, goal, facts, pending.TargetId, "relief_request", out reason,
                    pending.Activity.Content, pending.Activity.Domain);
                if (started == null) continue;
                CoopLog.Event("post.replacement_requested").Actor(id).Activity(started.Id).Target(pending.TargetId).Write();
                return true;
            }
            return false;
        }
        private static void Complete(Pending pending, CoopStepOutcome outcome)
        {
            if (pending.Done) return;
            pending.Done = true;
            _pending.Remove(pending.Activity.Id);
            pending.Lease?.Release("request_finished");
            pending.Finish?.Invoke(outcome);
        }
        public static void Cancel(string activityId, string reason)
        {
            if (!_pending.TryGetValue(activityId, out var pending)) return;
            pending.Done = true;
            _pending.Remove(activityId);
            pending.Lease?.Release(reason);
        }
        public static void Pause(string activityId, bool paused)
        {
            if (!_pending.TryGetValue(activityId, out var pending) || pending.Done || pending.Paused == paused) return;
            pending.Paused = paused;
            pending.Lease.Pause(paused);
            if (paused) { pending.SpeechToken++; pending.LineQueued = pending.LineShown = false; }
            else if (pending.Resolved) Answer(pending, pending.Response, pending.Refusal);
        }
    }
}
