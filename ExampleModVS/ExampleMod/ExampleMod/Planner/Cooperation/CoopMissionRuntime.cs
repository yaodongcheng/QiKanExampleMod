using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.Planner.Cooperation
{
    /// <summary>只经本人 Brain 租约执行原子能力；完成事实来自走位、展示和感知回执。</summary>
    public sealed class CoopMissionRuntime : ICoopStepRuntime, ICoopRuntimeLifecycle
    {
        private sealed class RunningStep
        {
            public CoopActivity Activity;
            public CoopActionDef Action;
            public Action<CoopStepOutcome> Callback;
            public Agent Actor, Target;
            public PerformanceAction Lease;
            public string Kind;
            public double Timeout, PausedAt, SpeechEndsAt;
            public bool Done, Begun, SpeechShown, Resuming;
            public int SpeechToken;
            public Vec3 Point;
            public void Finish(CoopStepOutcome outcome)
            {
                if (Done) return;
                Done = true;
                var callback = Callback;
                Callback = null;
                callback?.Invoke(outcome);
            }
        }
        private readonly StoryProbeMissionBehavior _host;
        private readonly Dictionary<string, RunningStep> _running = new Dictionary<string, RunningStep>(StringComparer.Ordinal);
        private readonly Dictionary<string, PerformanceAction> _leases = new Dictionary<string, PerformanceAction>(StringComparer.Ordinal);
        public double SimTime { get; private set; }
        internal StoryProbeMissionBehavior Host => _host;
        public CoopMissionRuntime(StoryProbeMissionBehavior host) { _host = host; }
        public bool IsBusy(CoopActivity activity) => activity != null && _running.TryGetValue(activity.Id, out var step) && !step.Done;
        internal bool HasControl(CoopActivity activity) => _leases.TryGetValue(activity.Id, out var lease) && lease.CanControl;
        internal Agent ResolveAgent(string id) => id == "player" ? Agent.Main : _host?.GetAgent(id);
        internal Agent ResolveTargetAgent(CoopActionDef action) => ResolveAgent(Arg(action, "target", null));
        internal string LineOf(CoopActionDef action) => Arg(action, "line", null);

        /// <summary>答复读完后延续本人已获准的租约；不释放身体，也不重新抢占 Brain。</summary>
        internal bool AdoptReplyLease(CoopActivity activity, PerformanceAction lease, string senderActivityId)
        {
            if (activity == null || !activity.IsActive || _leases.ContainsKey(activity.Id)
                || lease == null || !lease.Controls(ResolveAgent(activity.ActorId))
                || !BodyAvailable(ResolveAgent(activity.ActorId), out var reason)
                || _leases.ContainsValue(lease)) return false;
            _leases.Add(activity.Id, lease);
            lease.PauseOnConversation = true;
            CoopLog.Event("brain.handoff").Actor(activity.ActorId).Activity(activity.Id).Owner(activity.OwnerToken)
                .Value("fromActivity", senderActivityId).Value("leaseOwner", lease.OwnerToken).Write();
            return true;
        }

        public static bool BodyAvailable(Agent actor, out string reason)
        {
            reason = null;
            if (!AgentControlHelper.SafeIsActive(actor) || actor.Health <= 0f) reason = "body_unavailable";
            else if (actor.IsMainAgent) reason = "player_body_forbidden";
            else
            {
                var brain = AgentAIController.GetBrainForAgent(actor);
                if (brain == null) reason = "no_brain";
                else if (brain.IsBound || AgentBrain.IsRoped(actor)) reason = "bound";
                else if (brain.IsStatusHeld || brain.IsStunned || AgentBrain.IsKnockedOut(actor)) reason = "incapacitated";
                else if (brain.IsInCombat) reason = "in_combat";
                else if (CompanionDetentionBehavior.IsDetained(actor)) reason = "detained";
            }
            return reason == null;
        }
        public bool CanRun(CoopActivity activity, CoopActionDef action, out string reason)
        {
            if (!BodyAvailable(ResolveAgent(activity.ActorId), out reason)) return false;
            if (NeedsContract(activity) && !CoopRecruitment.CanCooperate(activity.ActorId, activity, out reason)) return false;
            if (NeedsContract(activity) && action?.CapabilityId == "interact_request"
                && !CoopRecruitment.CanRequestPurpose(activity.ActorId, activity, AuthorizationPurpose(activity, action), out reason)) return false;
            return true;
        }
        private static string AuthorizationPurpose(CoopActivity activity, CoopActionDef action)
        {
            string purpose = Arg(action, "purpose", null);
            foreach (var role in activity.Content.Roles)
                if (role.Requests.TryGetValue(purpose ?? "", out var policy))
                    return policy.AuthorizationPurpose ?? purpose;
            return purpose;
        }
        public void Refresh(CoopActivity activity, double simTime)
        {
            SimTime = simTime;
            var actor = ResolveAgent(activity.ActorId);
            string why;
            bool available = BodyAvailable(actor, out why);
            Set(activity, "alive", available, "body", simTime);
            if (NeedsContract(activity))
            {
                var participantRole = activity.Content.RoleOrNull(activity.Content.RoleOrNull(activity.RoleId)?.ParticipantRole);
                if (participantRole != null) foreach (var id in participantRole.Actors)
                {
                    var participant = ResolveAgent(id);
                    if (!AgentControlHelper.SafeIsActive(participant)) continue;
                    Set(activity, "near_" + id, actor != null && actor.Position.Distance(participant.Position) <= 4f, "position", simTime);
                    if (!NpcSightSystem.CanAgentSeeTarget(actor, participant, 30f)) continue;
                    bool onDuty = AgentAIController.GetBrainForAgent(participant)?.HasDutyPoint == true;
                    // 新观察到的看守必须重新交涉；视野外不得据名单判定缺席。
                    if (onDuty && !activity.Facts.IsTrue("present_" + id))
                    { Set(activity, "handled_" + id, false, "new_guard_observed", simTime); Set(activity, "guided", false, "new_guard_observed", simTime); }
                    Set(activity, "present_" + id, onDuty, "guard_observed", simTime);
                }
                Set(activity, "companion_ready", CoopRecruitment.CanCooperate(activity.ActorId, activity, out why), "contract", simTime);
                Set(activity, "talk_authorized", CoopRecruitment.CanRequestPurpose(activity.ActorId, activity, "talk", out why), "contract", simTime);
                Set(activity, "investigation_authorized", CoopRecruitment.CanRequestPurpose(activity.ActorId, activity, "investigate", out why), "contract", simTime);
                var target = ResolveAgent(activity.FocusTargetId);
                if (target == null) target = ResolveTargetAgent(activity.CurrentAction(activity.Domain));
                if (target != null)
                {
                    Set(activity, "near_guard", actor != null && actor.Position.Distance(target.Position) <= 4f, "position", simTime);
                    bool gap = available && _host != null && _host.HasGateOpportunity(actor, target);
                    if (gap && !string.IsNullOrEmpty(activity.ObservationPointQuery) && participantRole != null)
                    {
                        gap = ResolveScenePoint(activity.ObservationPointQuery, out var destination);
                        if (gap) foreach (var id in participantRole.Actors)
                        {
                            if (!activity.Facts.IsTrue("present_" + id)) continue;
                            var guard = ResolveAgent(id);
                            if (!BodyAvailable(guard, out var guardReason) || guard.Position.Distance(destination) > activity.ObservationDistance
                                || !NpcSightSystem.CanAgentSeeTarget(actor, guard, 20f)) { gap = false; break; }
                        }
                    }
                    if (gap) Set(activity, "other_watcher", false, "observed_clear_gate", simTime);
                    else activity.Facts.Set("other_watcher", CoopTruth.Unknown, "opportunity_unconfirmed", simTime);
                    // 未接触前保持未知；观察后才持续维护机会，旧信号随机会消失失效。
                    if (activity.Facts.IsTrue("engaged") && !activity.Facts.IsTrue("player_crossed"))
                    {
                        Set(activity, "gap", gap, "sight", simTime);
                        if (!gap) Set(activity, "signal", false, "opportunity_lost", simTime);
                    }
                }
                if (available && AgentControlHelper.SafeIsActive(Agent.Main)
                    && _host != null && _host.PlayerHasCrossed && NpcSightSystem.CanAgentSeeTarget(actor, Agent.Main, 30f))
                    Set(activity, "player_crossed", true, "witnessed_crossing", simTime);
            }
            else if (actor != null)
            {
                var post = CoopRequestRouter.GuardPost(actor.Index);
                Set(activity, "at_post", post != Vec3.Zero && actor.Position.Distance(post) <= 1.7f, "position", simTime);
                Set(activity, "relief_confirmed", CoopRequestRouter.HasRelief(actor), "replacement_arrived", simTime);
                var partner = ResolveAgent(activity.InteractionPartnerId);
                var current = activity.CurrentAction(activity.Domain);
                if (activity.Facts.IsTrue("lure_heard") && CanHear(actor, partner))
                    Set(activity, "guide_departed", partner.Position.Distance(post) >= ReadFloat(current, "Distance", 4f), "witnessed_guide_departure", simTime);
            }
        }
        private static void Set(CoopActivity activity, string fact, bool value, string source, double time)
        {
            bool wasTrue = activity.Facts.IsTrue(fact);
            activity.Facts.Set(fact, value ? CoopTruth.True : CoopTruth.False, source, time);
            if (fact == "gap" && wasTrue != value)
                CoopLog.Event(value ? "opportunity.observed" : "opportunity.expired")
                    .Actor(activity.ActorId).Activity(activity.Id).Source(source).SimTime(time).Write();
        }
        public void Start(CoopActivity activity, CoopActionDef action, double simTime, Action<CoopStepOutcome> onDone)
        {
            SimTime = simTime;
            var step = new RunningStep { Activity = activity, Action = action, Callback = onDone,
                Actor = ResolveAgent(activity.ActorId), Kind = action.CapabilityId,
                Timeout = simTime + (action.TimeoutGameSeconds > 0 ? action.TimeoutGameSeconds : 30) };
            _running[activity.Id] = step;
            if (!_leases.TryGetValue(activity.Id, out var lease) || lease.Ended)
            {
                CoopLog.Event("brain.requested").Actor(activity.ActorId).Activity(activity.Id).Owner(activity.OwnerToken).Write();
                lease = PerformanceAction.TryAcquire(step.Actor, "coop:" + activity.RunId + ":" + activity.OwnerToken, replaceAmbient: true);
                if (lease == null) { step.Finish(CoopStepOutcome.Interrupted("brain_refused")); return; }
                _leases[activity.Id] = lease;
                lease.PauseOnConversation = true;
            }
            step.Lease = lease;
        }
        public void Tick(float dt, double simTime)
        {
            SimTime = simTime;
            // 回调可增删活动，使用稳定快照。
            foreach (var step in new List<RunningStep>(_running.Values))
            {
                if (step.Done || step.Activity.IsTerminal || step.Activity.State == CoopActivityState.Paused) continue;
                try { TickStep(step, simTime); }
                catch (Exception ex)
                {
                    DebugLogger.Log("[Coop] runtime failed: " + ex.GetType().Name + " " + ex.Message);
                    step.Finish(CoopStepOutcome.Failed("runtime_exception"));
                }
            }
        }
        private void TickStep(RunningStep step, double time)
        {
            var activity = step.Activity;
            Refresh(activity, time);
            if (!CanRun(activity, step.Action, out var why)) { step.Finish(CoopStepOutcome.Interrupted(why)); return; }
            if (step.Lease == null || step.Lease.Ended) { step.Finish(CoopStepOutcome.Interrupted("lease_lost")); return; }
            if (time >= step.Timeout && step.Kind != "hold" && step.Kind != "face_target")
            { step.Finish(CoopStepOutcome.Failed("step_timeout")); return; }
            if (!step.Lease.CanControl) return;
            if (step.Begun && step.Kind == "wait_fact" && activity.Facts.IsTrue(Arg(step.Action, "fact", "")))
            { step.Finish(CoopStepOutcome.Completed()); return; }
            if (!step.Action.Persistent.IsSatisfiedBy(activity.Facts.Snapshot()))
            { step.Finish(CoopStepOutcome.Failed("persistent_condition_lost")); return; }
            if (!step.Begun)
            {
                if (!step.Action.Preconditions.IsSatisfiedBy(activity.Facts.Snapshot()))
                { step.Finish(CoopStepOutcome.Failed("conditions_invalid")); return; }
                step.Begun = true;
                CoopLog.Event("brain.granted").Actor(activity.ActorId).Activity(activity.Id).Owner(activity.OwnerToken).Write();
                CoopLog.Event("step.started").Actor(activity.ActorId).Activity(activity.Id).Step(step.Action.Id).Write();
                Begin(step, time);
                if (step.Done) return;
            }
            if (step.Resuming)
            {
                step.Resuming = false;
                if (step.Kind == "move_to_point") BeginMove(step);
                else if (step.Kind == "follow_to_point")
                {
                    if (!BodyAvailable(step.Target, out var guideReason) || !step.Lease.BeginFollow(step.Target, StopDistance(step), keepFollowing: true))
                        step.Finish(CoopStepOutcome.Failed("guide_lost"));
                }
                else if (!step.SpeechShown && (step.Kind == "signal_player" || step.Kind == "speak_line" || step.Kind == "interact_request"))
                {
                    if (IsVisualSignal(step.Action) && !step.Lease.PlayAction(Arg(step.Action, "action", null)))
                    { step.Finish(CoopStepOutcome.Failed("signal_action_unavailable")); return; }
                    QueueSpeech(step);
                }
            }
            switch (step.Kind)
            {
                case "move_to_point":
                    if (step.Lease.IsWalking) return;
                    if (step.Lease.WalkResult.HasValue)
                    {
                        bool arrived = step.Lease.WalkResult.Value && (step.Target != null
                            ? AgentControlHelper.SafeIsActive(step.Target) && step.Actor.Position.Distance(step.Target.Position) <= StopDistance(step) + 0.5f
                            : step.Actor.Position.Distance(step.Point) <= StopDistance(step) + 0.5f);
                        step.Finish(arrived ? CoopStepOutcome.Completed() : CoopStepOutcome.Failed("move_incomplete"));
                    }
                    break;
                case "follow_to_point":
                    if (!BodyAvailable(step.Target, out var guideReason)) { step.Finish(CoopStepOutcome.Failed("guide_lost")); break; }
                    if (step.Actor.Position.Distance(step.Point) <= StopDistance(step) + 1f
                        && step.Target.Position.Distance(step.Point) <= StopDistance(step) + 1f)
                    {
                        CoopLog.Event("movement.observed").Actor(activity.ActorId).Activity(activity.Id).Step(step.Action.Id)
                            .Raw("distance", step.Actor.Position.Distance(step.Point)).Raw("guideDistance", step.Target.Position.Distance(step.Point))
                            .Result("arrived").Write();
                        step.Finish(CoopStepOutcome.Completed()); break;
                    }
                    if (!step.Lease.IsWalking)
                    {
                        if (step.Lease.WalkResult == false) { step.Finish(CoopStepOutcome.Failed("follow_incomplete")); break; }
                        if (step.Actor.Position.Distance(step.Target.Position) > StopDistance(step)
                            && !step.Lease.BeginFollow(step.Target, StopDistance(step), keepFollowing: true)) step.Finish(CoopStepOutcome.Failed("follow_refused"));
                    }
                    break;
                case "face_target":
                    if (!AgentControlHelper.SafeIsActive(step.Target)) { step.Finish(CoopStepOutcome.Failed("target_unavailable")); break; }
                    if (time >= step.Timeout && NpcSightSystem.IsTargetInViewCone(step.Actor, step.Target, 30f, useBodyFacing: true))
                        step.Finish(CoopStepOutcome.Completed());
                    else if (time >= step.Timeout + 4) step.Finish(CoopStepOutcome.Failed("face_timeout"));
                    break;
                case "hold":
                    if (time >= step.Timeout) step.Finish(CoopStepOutcome.Completed());
                    break;
                case "speak_line": case "signal_player":
                    if (step.SpeechShown && time >= step.SpeechEndsAt) step.Finish(CoopStepOutcome.Completed());
                    break;
                case "wait_fact":
                    if (activity.Facts.IsTrue(Arg(step.Action, "fact", ""))) step.Finish(CoopStepOutcome.Completed());
                    break;
                case "observe_post":
                    if (activity.Facts.IsTrue("gap")) step.Finish(CoopStepOutcome.Completed());
                    break;
                case "interact_request":
                    if (step.SpeechShown && time >= step.SpeechEndsAt)
                    {
                        step.Kind = "interaction_pending";
                        CoopRequestRouter.Submit(this, activity, step.Action, step.Actor, step.Target, step.Finish);
                    }
                    break;
            }
        }
        private void Begin(RunningStep step, double time)
        {
            step.Target = ResolveAgent(Arg(step.Action, "target", null));
            if (Arg(step.Action, "target", null) == "requester") step.Target = ResolveAgent(step.Activity.InteractionPartnerId);
            if (Arg(step.Action, "target", null) == "focus") step.Target = ResolveAgent(step.Activity.FocusTargetId);
            if (step.Target != null) step.Activity.FocusTargetId = IdOf(step.Target);
            switch (step.Kind)
            {
                case "move_to_point": BeginMove(step); break;
                case "follow_to_point":
                    if (!BodyAvailable(step.Target, out var guideReason))
                    { step.Finish(CoopStepOutcome.Failed("guide_lost")); break; }
                    if (!ResolveScenePoint(Arg(step.Action, "point", null), out step.Point))
                    { step.Finish(CoopStepOutcome.Failed("point_unresolved")); break; }
                    if (!step.Lease.BeginFollow(step.Target, StopDistance(step), keepFollowing: true)) step.Finish(CoopStepOutcome.Failed("follow_refused"));
                    break;
                case "face_target":
                    if (!step.Lease.FaceTarget(step.Target)) { step.Finish(CoopStepOutcome.Failed("target_unavailable")); break; }
                    step.Timeout = time + ReadFloat(step.Action, "FaceSeconds", 0.5f); break;
                case "hold":
                    if (step.Target != null) step.Lease.FaceTarget(step.Target);
                    step.Timeout = time + ReadFloat(step.Action, "HoldSeconds", 2f); break;
                case "speak_line": case "signal_player": case "interact_request":
                    if (step.Kind == "signal_player") step.Target = Agent.Main;
                    if (step.Target != null && !IsVisualSignal(step.Action)) step.Lease.FaceTarget(step.Target);
                    if (IsVisualSignal(step.Action) && !step.Lease.PlayAction(Arg(step.Action, "action", null)))
                    { step.Finish(CoopStepOutcome.Failed("signal_action_unavailable")); break; }
                    QueueSpeech(step); break;
                case "observe_post":
                    step.Activity.ObservationPointQuery = Arg(step.Action, "point", null);
                    step.Activity.ObservationDistance = ReadFloat(step.Action, "Distance", 3f);
                    if (step.Target != null) step.Lease.FaceTarget(step.Target);
                    Refresh(step.Activity, time);
                    break;
                case "wait_fact": break;
                default: step.Finish(CoopStepOutcome.Failed("no_runtime_adapter")); break;
            }
        }
        private float StopDistance(RunningStep step) => Math.Max(0.6f,
            Math.Max(ReadFloat(step.Action, "StopDistance", 1.2f), ReadFloat(step.Action, "Offset", 0f)));
        private void BeginMove(RunningStep step)
        {
            var point = Arg(step.Action, "point", "self");
            if (point == "player") step.Target = Agent.Main;
            if (point == "target" || point == "player")
            {
                if (!step.Lease.BeginFollow(step.Target, Math.Max(StopDistance(step), ReadFloat(step.Action, "Offset", 0f))))
                    step.Finish(CoopStepOutcome.Failed("follow_refused"));
                return;
            }
            if (point == "post")
            {
                step.Point = CoopRequestRouter.GuardPost(step.Actor.Index);
                if (step.Point == Vec3.Zero) { step.Finish(CoopStepOutcome.Failed("post_unresolved")); return; }
            }
            else if (point == "relief_post")
            {
                var relieved = ResolveAgent(step.Activity.InteractionPartnerId);
                if (relieved == null) { step.Finish(CoopStepOutcome.Failed("relief_post_unresolved")); return; }
                step.Point = CoopRequestRouter.GuardPost(relieved.Index);
            }
            else if (point == "self")
            {
                var facing = step.Actor.Frame.rotation.f;
                facing.z = 0; facing.Normalize();
                step.Point = step.Actor.Position + facing * ReadFloat(step.Action, "Offset", 0f);
            }
            else if (!ResolveScenePoint(point, out step.Point)) { step.Finish(CoopStepOutcome.Failed("point_unresolved")); return; }
            if (!step.Lease.BeginWalk(step.Point, StopDistance(step), ReadFloat(step.Action, "MoveTimeout", 20f)))
                step.Finish(CoopStepOutcome.Failed("move_refused"));
        }
        private bool ResolveScenePoint(string query, out Vec3 point)
        {
            point = Vec3.Zero;
            return query != null && query.StartsWith("scene:", StringComparison.Ordinal)
                && _host?.Prep != null && _host.Prep.TryResolvePoint(query.Substring(6), out point);
        }
        private void QueueSpeech(RunningStep step)
        {
            string key = Arg(step.Action, "line", null);
            if (string.IsNullOrEmpty(key)) { step.Finish(CoopStepOutcome.Failed("line_missing")); return; }
            int revision = step.Activity.StepRevision;
            int speechToken = ++step.SpeechToken;
            string text = LWNTextHelper.ResolveText(key, key);
            float duration = IsVisualSignal(step.Action) ? 0f : Math.Max(3f, SpeechChannel.EstimateDuration(text));
            SpeechChannel.Say(step.Actor, text, SpeechPriority.Dialogue,
                SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(step.Actor), listener:
                    IsVisualSignal(step.Action) ? ResolveAgent(step.Activity.FocusTargetId) : step.Target),
                duration: duration,
                onShown: () =>
                {
                    if (step.SpeechToken != speechToken || !Current(step, revision) || step.SpeechShown) return;
                    if (step.Target != null && !CanReceive(step.Action, step.Target, step.Actor)) return;
                    step.SpeechShown = true;
                    step.SpeechEndsAt = SimTime + duration;
                    if (step.Kind == "signal_player")
                    {
                        step.Activity.PlayerInformed = true;
                        CoopView.NotifySignal(step.Activity);
                    }
                    CoopLog.Event("speech.shown").Actor(step.Activity.ActorId).Activity(step.Activity.Id).Step(step.Action.Id)
                        .Value("textKey", key).Value("delivery", IsVisualSignal(step.Action) ? "visual" : "voice")
                        .Raw("duration", duration).Raw("endsAt", step.SpeechEndsAt).Write();
                }, canPublish: () =>
                {
                    if (step.SpeechToken != speechToken || !Current(step, revision) || !step.Lease.CanControl) return false;
                    Refresh(step.Activity, SimTime);
                    bool valid = CanRun(step.Activity, step.Action, out var why)
                        && step.Action.Persistent.IsSatisfiedBy(step.Activity.Facts.Snapshot())
                        && (step.Target == null || CanReceive(step.Action, step.Target, step.Actor));
                    if (!valid) CoopLog.Event("speech.discarded").Activity(step.Activity.Id).Step(step.Action.Id)
                        .Reason(why ?? "delivery_conditions_lost").Write();
                    return valid;
                }, canForwardToNearby: () => CanReceive(step.Action, Agent.Main, step.Actor));
            CoopLog.Event("speech.queued").Actor(step.Activity.ActorId).Activity(step.Activity.Id).Value("textKey", key).Write();
        }
        private static bool Current(RunningStep step, int revision) => !step.Done && step.Activity.IsActive
            && step.Activity.State != CoopActivityState.Paused && step.Activity.StepRevision == revision;
        internal static bool CanHear(Agent listener, Agent speaker) => AgentControlHelper.SafeIsActive(listener)
            && AgentControlHelper.SafeIsActive(speaker) && listener.Position.Distance(speaker.Position) <= 12f
            && Math.Abs(listener.Position.z - speaker.Position.z) <= 3f;
        internal static bool CanSeeWithin(Agent observer, Agent target, float distance) => AgentControlHelper.SafeIsActive(observer)
            && AgentControlHelper.SafeIsActive(target) && observer.Position.Distance(target.Position) <= distance
            && NpcSightSystem.CanAgentSeeTarget(observer, target, distance);
        private static bool IsVisualSignal(CoopActionDef action) => action?.CapabilityId == "signal_player"
            && Arg(action, "delivery", "voice") == "visual";
        private static bool CanReceive(CoopActionDef action, Agent listener, Agent actor) => IsVisualSignal(action)
            ? CanSeeWithin(listener, actor, 30f)
            : CanHear(listener, actor);
        internal string IdOf(Agent agent)
        {
            if (agent == Agent.Main) return "player";
            if (_host != null) foreach (var npc in StoryProbeDefs.Npcs)
                if (npc != null && _host.GetAgent(npc.Id) == agent) return npc.Id;
            return null;
        }
        public void Pause(CoopActivity activity, bool paused, double time)
        {
            CoopRequestRouter.Pause(activity.Id, paused);
            if (_leases.TryGetValue(activity.Id, out var lease)) lease.Pause(paused);
            if (_running.TryGetValue(activity.Id, out var step))
            {
                if (paused) { step.PausedAt = time; step.SpeechToken++; }
                else
                {
                    step.Timeout += Math.Max(0, time - step.PausedAt);
                    if (step.SpeechShown) step.SpeechEndsAt += Math.Max(0, time - step.PausedAt);
                    step.Resuming = step.Begun;
                }
            }
        }
        public void Stop(CoopActivity activity, string reason)
        {
            if (activity == null) return;
            if (_running.TryGetValue(activity.Id, out var step)) { step.Done = true; step.Callback = null; _running.Remove(activity.Id); }
            if (_leases.TryGetValue(activity.Id, out var lease))
            {
                _leases.Remove(activity.Id);
                lease.Release(reason);
                CoopLog.Event("brain.released").Actor(activity.ActorId).Activity(activity.Id).Owner(activity.OwnerToken).Reason(reason).Write();
            }
            CoopRequestRouter.Cancel(activity.Id, reason);
            // 撤销本次诱离邀请，不取消无关守卫活动；岗位系统按真实职责安排回岗。
            if (NeedsContract(activity) && activity.IsTerminal)
                foreach (var receiver in new List<CoopActivity>(CoopDirector.Current.Activities))
                    if (receiver.IsActive && receiver.RequestActivityId == activity.Id
                        && receiver.InteractionPartnerId == activity.ActorId && receiver.Facts.IsTrue("lure_heard"))
                        CoopDirector.Current.StopActivity(receiver, "requester_stopped", SimTime);
            if (NeedsContract(activity) && reason != "scene_exit" && reason != "host_replaced"
                && (activity.State == CoopActivityState.Failed || activity.State == CoopActivityState.Interrupted)
                && BodyAvailable(ResolveAgent(activity.ActorId), out var bodyReason))
            {
                var recoveryId = activity.Content.RoleOrNull(activity.RoleId)?.RecoveryRole;
                var role = activity.Content.RoleOrNull(recoveryId);
                var facts = new Dictionary<string, CoopTruth> { { "alive", CoopTruth.True }, { "cancelled", CoopTruth.False } };
                var goal = role != null ? CoopMissionBehavior.SelectRoleGoal(role, activity.Content, facts) : null;
                if (goal != null) CoopMissionBehavior.Current?.StartRoleActivity(activity.ActorId, role, goal, facts,
                    "player", "failure_report", out var recoveryReason, activity.Content, activity.Domain);
            }
        }
        internal static string Arg(CoopActionDef action, string key, string fallback) => action != null
            && action.Args.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : fallback;
        private static bool NeedsContract(CoopActivity activity) => activity.Content?.RoleOrNull(activity.RoleId)?.RequiresContract == true;
        internal static float ReadFloat(CoopActionDef action, string key, float fallback) => float.TryParse(Arg(action, key, null),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }
}
