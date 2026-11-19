using System;
using System.Collections.Generic;
using System.Text;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Library;

namespace LivingWorldNpcs.Planner.Cooperation
{
    /// <summary>
    /// 玩家可读的协作说明：基础功能版始终可查看；LLM 只能润色同一份说明，
    /// 关键事实（对象、分工、条件、代价）由本地字段固定显示（§5.5）。
    /// </summary>
    public static class CoopView
    {
        private sealed class Card
        {
            public ImMessage Message;
            public string Signature, Basic, Polished, Progress, Deferred;
            public bool ShowPolished;
        }
        private static readonly Dictionary<string, Card> _cards = new Dictionary<string, Card>();
        public static bool HasLocalCard => _cards.Count > 0;
        private static readonly ConcurrentQueue<(string id, string signature, string language, string text)> _polished = new ConcurrentQueue<(string, string, string, string)>();

        public static void Reset()
        {
            _cards.Clear();
            while (_polished.TryDequeue(out var unused)) { }
        }
        public static string KnownProgress(string activityId) => activityId != null && _cards.TryGetValue(activityId, out var card)
            ? card.Progress ?? string.Empty : string.Empty;

        /// <summary>玩家实际看见手势后单独留消息并即时提示；不替代视线，也不开窗打断走位。</summary>
        internal static void NotifySignal(CoopActivity activity)
        {
            var actor = CoopMissionBehavior.Current?.Runtime?.ResolveAgent(activity.ActorId);
            string text = ResolveCompound("LWN_coop_signal_received", ("ACTOR", actor?.Name?.ToString() ?? activity.ActorId));
            NearbyFeed.AppendCard(new ImMessage {
                SenderHeroId = StoryProbeMissionBehavior.Current?.GetHero(activity.ActorId)?.StringId,
                SenderName = actor?.Name?.ToString(), Content = text, TimeStamp = ImChatManager.NowUnixMs()
            });
            InformationManager.DisplayMessage(new InformationMessage(text));
            CoopLog.Event("signal.received").Actor(activity.ActorId).Activity(activity.Id)
                .Value("source", "witnessed_gesture").Value("channel", "nearby_and_notice").Write();
        }

        public static void Tick(CoopDirector director, double time)
        {
            while (_polished.TryDequeue(out var result))
            {
                var activity = director.Find(result.id);
                if (activity == null || !activity.IsActive || activity.State == CoopActivityState.Paused
                    || !_cards.TryGetValue(result.id, out var card) || card.Signature != result.signature
                    || result.language != LWNTextHelper.GetReplyLanguageInstruction())
                { CoopLog.Event("polish.discarded").Reason("stale_result").Write(); continue; }
                card.Polished = result.text;
                if (card.ShowPolished) UpdateText(card);
            }
            foreach (var activity in director.Activities)
            {
                var content = activity.Content ?? director.Content;
                if (content.RoleOrNull(activity.RoleId)?.RequiresContract != true || activity.Plan == null) continue;
                var actor = CoopMissionBehavior.Current?.Runtime?.ResolveAgent(activity.ActorId);
                if (!_cards.TryGetValue(activity.Id, out var card))
                {
                    if (!activity.IsActive || !CoopMissionRuntime.CanHear(Agent.Main, actor)) continue;
                    card = new Card { Message = new ImMessage {
                        Kind = ImMessageKind.Cooperation, CooperationActivityId = activity.Id,
                        SenderHeroId = StoryProbeMissionBehavior.Current?.GetHero(activity.ActorId)?.StringId,
                        SenderName = actor?.Name?.ToString(), TimeStamp = ImChatManager.NowUnixMs()
                    }};
                    _cards[activity.Id] = card;
                    CoopLog.Event("preview.queued").Actor(activity.ActorId).Activity(activity.Id).Value("channel", "im_card").Write();
                }
                string signature = activity.PlanRevision + ":" + activity.StepIndex + ":" + activity.State
                    + ":" + activity.LastReason + ":" + activity.AwaitingApproval + ":" + LWNTextHelper.GetReplyLanguageInstruction();
                if (!activity.PlayerInformed && card.Basic != null && ImChatView.ShowCooperationCard())
                {
                    activity.PlayerInformed = true;
                    CoopLog.Event("preview.shown").Actor(activity.ActorId).Activity(activity.Id).Value("channel", "im_card").Write();
                }
                if (signature == card.Signature) continue;
                bool canReceive = CoopMissionRuntime.CanHear(Agent.Main, actor)
                    || activity.Facts.IsTrue("signal") && CoopMissionRuntime.CanSeeWithin(Agent.Main, actor, 30f);
                if (!canReceive && activity.StopReason != "player_cancelled")
                {
                    if (card.Deferred != signature)
                    { card.Deferred = signature; CoopLog.Event("view.discarded").Activity(activity.Id).Reason("report_not_received").Write(); }
                    continue;
                }
                card.Signature = signature;
                card.Polished = null;
                card.Basic = string.Join("\n", BuildBasic(activity, content)) + "\n" + BuildDetail(activity, content);
                card.Progress = BuildProgress(activity, content);
                UpdateText(card);
                if (string.IsNullOrEmpty(card.Message.ConvId)) NearbyFeed.AppendCard(card.Message);
                if (!activity.PlayerInformed && ImChatView.ShowCooperationCard())
                {
                    activity.PlayerInformed = true;
                    CoopLog.Event("preview.shown").Actor(activity.ActorId).Activity(activity.Id).Value("channel", "im_card").Write();
                }
                CoopLog.Event("view.updated").Actor(activity.ActorId).Activity(activity.Id).Value("channel", "im_card").Write();
            }
            if (_cards.Count > 64)
            {
                foreach (var id in new List<string>(_cards.Keys)) if (director.Find(id) == null) _cards.Remove(id);
            }
        }
        private static void UpdateText(Card card)
        {
            card.Message.Content = card.ShowPolished && !string.IsNullOrWhiteSpace(card.Polished)
                ? card.Polished + "\n\n" + card.Basic : card.Basic;
            ImChatView.RebuildCardMessages();
        }
        public static IEnumerable<ImButtonVM> Buttons(ImMessage message)
        {
            yield return new ImButtonVM(Resolve("LWN_coop_btn_basic", "Facts"), () => SetMode(message, false));
            yield return new ImButtonVM(Resolve("LWN_coop_btn_polish", "In my words"), () => SetMode(message, true), Settings.Instance.IsLLMConfigured);
            var activity = CoopDirector.Current?.Find(message.CooperationActivityId);
            if (activity == null || !activity.IsActive) yield break;
            if (activity.AwaitingApproval)
            {
                int revision = activity.PlanRevision;
                yield return new ImButtonVM(Resolve("LWN_coop_btn_start", "Start"), () =>
                {
                    if (CoopDirector.Current?.Approve(activity.Id, revision, CoopMissionBehavior.Current?.SimTime ?? 0) == true)
                        ImChatView.Close();
                });
            }
            yield return new ImButtonVM(Resolve("LWN_coop_btn_stop", "Stop"), () =>
            {
                string detail;
                CoopDirector.Current?.Stop(activity.Id, "player_cancelled", CoopMissionBehavior.Current?.SimTime ?? 0, out detail);
            });
        }
        private static void SetMode(ImMessage message, bool polished)
        {
            if (!_cards.TryGetValue(message.CooperationActivityId, out var card)) return;
            card.ShowPolished = polished;
            UpdateText(card);
            if (!polished || !Settings.Instance.IsLLMConfigured || card.Polished != null) return;
            var activity = CoopDirector.Current?.Find(message.CooperationActivityId);
            if (activity == null || !activity.IsActive) return;
            string id = activity.Id, signature = card.Signature, language = LWNTextHelper.GetReplyLanguageInstruction();
            string basic = card.Basic;
            string instruction = LWNTextHelper.ResolvePrompt("LWN_coop_prompt_polish") + "\n" + LWNTextHelper.GetLanguageRule();
            CoopLog.Event("polish.requested").Actor(activity.ActorId).Activity(id).PlanRevision(activity.PlanRevision).Write();
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!Settings.Instance.IsLLMConfigured) return;
                    var json = await LLMService.Instance.ChatOnceAsync(instruction + "\n" + basic,
                        maxTokens: 240, timeoutMs: 1500, needJson: true);
                    if (string.IsNullOrWhiteSpace(json)) return;
                    var obj = JObject.Parse(LLMService.CleanJson(json));
                    if (obj.Count != 1 || obj["text"]?.Type != JTokenType.String) return;
                    string text = obj.Value<string>("text");
                    if (!string.IsNullOrWhiteSpace(text) && text.Length <= 480
                        && text.IndexOf("<", StringComparison.Ordinal) < 0 && text.IndexOf("{", StringComparison.Ordinal) < 0
                        && text.IndexOf("guarantee", StringComparison.OrdinalIgnoreCase) < 0
                        && text.IndexOf("必定", StringComparison.Ordinal) < 0 && text.IndexOf("一定成功", StringComparison.Ordinal) < 0)
                        _polished.Enqueue((id, signature, language, text));
                }
                catch (Exception ex) { DebugLogger.Log("[Coop] polish fallback: " + ex.GetType().Name); }
            });
        }
        public static string GoalText(CoopGoalDef goal, CoopContentData content)
        {
            if (goal == null)
            {
                return string.Empty;
            }
            return Resolve(goal.DisplayKey, goal.Id);
        }

        /// <summary>基础功能描述版：目标、本人分步、等待条件、失败打算。</summary>
        public static List<string> BuildBasic(CoopActivity activity, CoopContentData content)
        {
            var lines = new List<string>();
            if (activity == null || content == null)
            {
                return lines;
            }
            var goal = content.GoalOrNull(activity.Plan != null ? activity.Plan.GoalId : null);
            lines.Add(Resolve("LWN_coop_plan_title", "Plan"));
            if (activity.AwaitingApproval) lines.Add(Resolve("LWN_coop_plan_preview", "These are the proposed steps. I will wait for your word."));
            var runtime = CoopMissionBehavior.Current?.Runtime;
            var actor = runtime?.ResolveAgent(activity.ActorId);
            lines.Add(ResolveCompound("LWN_coop_plan_actor", ("ACTOR", actor?.Name?.ToString() ?? activity.ActorId)));
            var targets = new List<string>();
            string target = activity.FocusTargetId;
            var participantRole = content.RoleOrNull(content.RoleOrNull(activity.RoleId)?.ParticipantRole);
            if (participantRole != null) foreach (var id in participantRole.Actors)
                if (activity.Facts.IsTrue("present_" + id)) targets.Add(runtime?.ResolveAgent(id)?.Name?.ToString() ?? id);
            if (string.IsNullOrEmpty(target) && activity.Plan != null)
                foreach (var id in activity.Plan.Steps)
                    if (content.Domain.Actions.TryGetValue(id, out var step) && step.Args.TryGetValue("target", out target)) break;
            lines.Add(ResolveCompound("LWN_coop_plan_target", ("TARGET", targets.Count > 0 ? string.Join(", ", targets)
                : runtime?.ResolveAgent(target)?.Name?.ToString() ?? Resolve("LWN_coop_unknown_person", "the person at the gate"))));
            var role = content.RoleOrNull(activity.RoleId);
            var goalText = GoalText(goal, content);
            if (!string.IsNullOrEmpty(goalText)) lines.Add(ResolveCompound("LWN_coop_plan_goal", ("GOAL", goalText)));
            var descriptions = new HashSet<string>(StringComparer.Ordinal);
            if (activity.Plan != null) foreach (var stepId in activity.Plan.Steps)
                if (content.Domain.Actions.TryGetValue(stepId, out var step)
                    && !string.IsNullOrEmpty(step.DescriptionKey) && descriptions.Add(step.DescriptionKey))
                    lines.Add(Resolve(step.DescriptionKey, step.DescriptionKey));
            if (role != null) foreach (var key in role.Presentation.Values) lines.Add(Resolve(key, key));
            lines.Add(Resolve("LWN_coop_plan_failure", "I will stop if I cannot continue safely; I will not add money or start a fight."));

            if (activity.State == CoopActivityState.Waiting)
            {
                lines.Add(Resolve("LWN_coop_waiting", "Waiting for a real reply; nothing is promised yet."));
            }
            lines.Add(Resolve("LWN_coop_plan_stop", "You can stop this at any time; it will not undo what already happened."));
            return lines;
        }

        /// <summary>一行进展：当前阶段 + 玩家此刻可做的事。</summary>
        public static string BuildProgress(CoopActivity activity, CoopContentData content)
        {
            if (activity == null || content == null)
            {
                return string.Empty;
            }
            if (activity.IsTerminal) return BuildDetail(activity, content);
            if (activity.AwaitingApproval) return Resolve("LWN_coop_plan_preview", "These are the proposed steps. I will wait for your word.");
            if (activity.State == CoopActivityState.Waiting)
            {
                var waiting = activity.CurrentAction(content.Domain);
                return ResolveCompound("LWN_coop_progress_waiting",
                    ("STAGE", waiting != null ? StageText(waiting, content) : Resolve("LWN_coop_stage_unknown", "the next step")));
            }
            var action = activity.CurrentAction(content.Domain);
            if (action != null)
            {
                if (action.CapabilityId == "wait_fact" && activity.Facts.IsTrue("signal"))
                    return Resolve("LWN_coop_plan_waiting_signal", "You saw my gesture. Watch the gate and cross while the opening lasts.");
                return ResolveCompound("LWN_coop_progress_active", ("STAGE", StageText(action, content)));
            }
            return Resolve("LWN_coop_progress_idle", "No active step.");
        }

        /// <summary>按需详情：已知进展、等待原因、最近一次变化。</summary>
        public static string BuildDetail(CoopActivity activity, CoopContentData content)
        {
            if (activity == null || content == null)
            {
                return string.Empty;
            }
            if (activity.State == CoopActivityState.Success) return Resolve("LWN_coop_result_success", "You are through the gate. My part is done.");
            if (activity.State == CoopActivityState.Cancelled) return Resolve("LWN_coop_result_cancelled", "I have stopped. What already happened still stands.");
            if (activity.IsTerminal)
            {
                string reason = string.IsNullOrEmpty(activity.LastFailureReason) ? activity.LastReason : activity.LastFailureReason;
                string key;
                switch (reason)
                {
                    case "refused": case "last_sentry_cannot_leave": key = "LWN_coop_reason_refused"; break;
                    case "move_incomplete": case "move_refused": case "follow_incomplete": case "follow_refused":
                    case "guide_lost": case "point_unresolved": case "step_timeout": case "wait_timeout": key = "LWN_coop_reason_timeout"; break;
                    case "persistent_condition_lost": case "conditions_invalid": key = "LWN_coop_reason_gap_lost"; break;
                    case "plan_budgetexhausted": case "retry_budget": key = "LWN_coop_reason_budget"; break;
                    default: key = "LWN_coop_result_failed"; break;
                }
                return Resolve(key, "I could not continue. Stay clear of the gate; we can talk through another way.");
            }
            if (activity.State == CoopActivityState.Paused) return Resolve("LWN_coop_result_paused", "I will wait while we talk.");
            return BuildProgress(activity, content);
        }

        public static string StageText(CoopActionDef action, CoopContentData content)
        {
            if (action == null)
            {
                return string.Empty;
            }
            string key;
            if (content != null && !string.IsNullOrEmpty(action.StageKey) && content.StageTextKeys.TryGetValue(action.StageKey, out key))
            {
                return Resolve(key, action.StageKey);
            }
            if (!string.IsNullOrEmpty(action.DisplayKey))
            {
                return Resolve(action.DisplayKey, action.Id);
            }
            return action.Id;
        }

        private static string Resolve(string key, string fallback)
        {
            if (string.IsNullOrEmpty(key))
            {
                return fallback ?? string.Empty;
            }
            try
            {
                return LWNTextHelper.ResolveText(key, fallback);
            }
            catch (Exception)
            {
                return fallback ?? key;
            }
        }

        private static string ResolveCompound(string key, params (string var, string value)[] variables)
        {
            try
            {
                return LWNTextHelper.ResolveCompound(key, variables);
            }
            catch (Exception)
            {
                var builder = new StringBuilder(key);
                foreach (var pair in variables)
                {
                    builder.Append(' ').Append(pair.var).Append('=').Append(pair.value);
                }
                return builder.ToString();
            }
        }
    }
}
