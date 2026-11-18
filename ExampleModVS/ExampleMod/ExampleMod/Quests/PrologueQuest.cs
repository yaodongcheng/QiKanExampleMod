using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 序章委托的 Issue（2026-11-13）。
    /// 🔴 复用项目既有管道（范本 CommissionHubIssue）：`new XxxIssue(...)` → `PotentialIssueData` →
    ///    `IssueManager.CreateNewIssue` → `StartIssueWithQuest()`。**不复用 Commission 业务类**
    ///    （押金/备案/信任/大地图时限与序章"场景内打倒某人"不匹配）。
    /// 🔴 Quest 只能从 Issue 生：读档时 QuestManager 会校验活动 Quest 必须有关联 Issue，凭空 new 会被取消。
    /// </summary>
    public class PrologueIssue : IssueBase
    {
        [SaveableField(1)] private string _npcId;        // 委托人（pawnbroker/widow/son/butcher）
        [SaveableField(2)] private string _targetNpcId;  // 要打倒的人（debtor/rival/outlaw/thug）
        [SaveableField(3)] private string _titleKey;     // 任务名（StoryNpcDef.QuestLogKey）

        public PrologueIssue(Hero issueOwner, string npcId, string targetNpcId, string titleKey)
            : base(issueOwner, CampaignTime.DaysFromNow(60f))
        {
            _npcId = npcId;
            _targetNpcId = targetNpcId;
            _titleKey = titleKey;
        }

        private string TitleText()
            => LWNTextHelper.ResolveText(string.IsNullOrEmpty(_titleKey) ? "LWN_story_issue_title_fallback" : _titleKey,
                                         string.IsNullOrEmpty(_titleKey) ? "Job" : _npcId);

        private string TargetText()
            => string.IsNullOrEmpty(_targetNpcId) ? "" : StoryProbeMissionBehavior.NpcName(_targetNpcId);

        private TextObject ObjectiveText()
            => new TextObject(LWNTextHelper.ResolveCompound("LWN_story_goal_quest_named",
                ("TASK", TitleText()), ("TARGET", TargetText())));

        // ── IssueBase 抽象成员 ──
        public override TextObject Title => new TextObject(TitleText());
        public override TextObject Description => ObjectiveText();
        public override TextObject IssueBriefByIssueGiver
            => new TextObject(LWNTextHelper.ResolveText("LWN_story_issue_brief", "There is a thing to be done."));
        public override TextObject IssueAcceptByPlayer
            => new TextObject(LWNTextHelper.ResolveText("LWN_story_issue_accept", "Tell me."));
        public override TextObject IssueQuestSolutionExplanationByIssueGiver => ObjectiveText();
        public override TextObject IssueQuestSolutionAcceptByPlayer
            => new TextObject(LWNTextHelper.ResolveText("LWN_story_opt_take_job", "[Take] I'll do it."));
        public override bool IsThereAlternativeSolution => false;
        public override bool IsThereLordSolution => false;

        public override IssueFrequency GetFrequency() => IssueFrequency.Common;

        protected override QuestBase GenerateIssueQuest(string questId)
        {
            var quest = new PrologueQuest(questId, IssueOwner, _npcId, _targetNpcId, _titleKey);
            quest.StartQuest();
            DebugLogger.Log($"[PrologueIssue] GenerateIssueQuest: {questId} giver={IssueOwner?.Name} target={_targetNpcId}");
            return quest;
        }

        /// <summary>公开入口：对话里选「这事我接了」时调用。只走官方 StartIssueWithQuest 建立关联。</summary>
        public PrologueQuest AcceptPrologueQuest()
        {
            if (!StartIssueWithQuest()) return null;
            return IssueQuest as PrologueQuest;
        }

#if MB2_GE_140
        protected override bool CanPlayerTakeQuestConditions(Hero issueGiver,
            out PreconditionFlags flag, out Hero relationHero, out SkillObject skill, out int requiredGold)
#else
        protected override bool CanPlayerTakeQuestConditions(Hero issueGiver,
            out PreconditionFlags flag, out Hero relationHero, out SkillObject skill)
#endif
        {
            flag = PreconditionFlags.None;
            relationHero = issueGiver;
            skill = null;
#if MB2_GE_140
            requiredGold = 0;
#endif
            return issueGiver != null;
        }

        public override bool IssueStayAliveConditions() => IssueOwner != null && IssueOwner.IsAlive;

        protected override void CompleteIssueWithTimedOutConsequences() { }
        protected override void OnGameLoad() { }
        protected override void HourlyTick() { }

        public string NpcId => _npcId;
        public string TargetNpcId => _targetNpcId;
    }

    /// <summary>
    /// 序章委托的 Quest（2026-11-13）。生命周期骨架照 CommissionQuest，业务只有三件事：
    /// ① 接受 → 记一条日志；② 目标倒下 → 再记一条"回去交差"；③ 回去交差 → 成功结束。
    /// </summary>
    public class PrologueQuest : QuestBase
    {
        [SaveableField(1)] private string _npcId;
        [SaveableField(2)] private string _targetNpcId;
        [SaveableField(3)] private string _titleKey;
        [SaveableField(4)] private bool _objectivesDone;

        public PrologueQuest(string questId, Hero questGiver, string npcId, string targetNpcId, string titleKey)
            : base(questId, questGiver, CampaignTime.Now + CampaignTime.Days(365f), 0)
        {
            _npcId = npcId;
            _targetNpcId = targetNpcId;
            _titleKey = titleKey;
        }

        public override bool IsRemainingTimeHidden => true;

        public override TextObject Title
            => new TextObject(LWNTextHelper.ResolveText(string.IsNullOrEmpty(_titleKey) ? "LWN_story_issue_title_fallback" : _titleKey,
                                                       string.IsNullOrEmpty(_titleKey) ? "Job" : _npcId));

        private string TargetText()
            => string.IsNullOrEmpty(_targetNpcId) ? "" : StoryProbeMissionBehavior.NpcName(_targetNpcId);

        private string GiverText()
            => string.IsNullOrEmpty(_npcId) ? "" : StoryProbeMissionBehavior.NpcName(_npcId);

        public string NpcId => _npcId;
        public bool ObjectivesDone => _objectivesDone;

        protected override void SetDialogs() { }
        protected override void InitializeQuestOnGameLoad() { }
        protected override void HourlyTick() { }
        protected override void RegisterEvents() { }

        protected override void OnStartQuest()
        {
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_story_goal_quest_named",
                ("TASK", Title.ToString()), ("TARGET", TargetText()))));
            DebugLogger.Log($"[PrologueQuest] start {StringId} giver={_npcId} target={_targetNpcId}");
        }

        /// <summary>目标倒下时调用（StoryProbeMissionBehavior.TickQuests）。只记一次。</summary>
        public void MarkObjectivesDone()
        {
            if (_objectivesDone || IsFinalized) return;
            _objectivesDone = true;
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_story_quest_ready_turnin",
                "Done. Go back and tell {NAME}.", ("NAME", GiverText()))));
            DebugLogger.Log($"[PrologueQuest] objectives done {StringId}");
        }

        /// <summary>回委托人处交差时调用。</summary>
        public void Finish()
        {
            if (IsFinalized) return;
            CompleteQuestWithSuccess();
        }

        public void CancelForPrologueRestart()
        {
            if (IsFinalized) return;
            CompleteQuestWithCancel();
        }

        protected override void OnCompleteWithSuccess()
        {
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_story_quest_finished",
                "Settled with {NAME}.", ("NAME", GiverText()))));
            DebugLogger.Log($"[PrologueQuest] success {StringId}");
        }

        protected override void OnTimedOut() { }
        public override void OnFailed() { }
    }

    /// <summary>
    /// 序章委托的创建/查询入口。
    /// 创建照 CommissionHubIssue.TryAddIssue 的 20 行：`new` → `PotentialIssueData` → `IssueManager.CreateNewIssue`。
    /// </summary>
    public static class PrologueQuestLauncher
    {
        /// <summary>只读对照剧情接单标志与原版Quest登记，不补建/接单/完成任务。</summary>
        public static string Describe()
        {
            var quests = Campaign.Current?.QuestManager?.Quests;
            if (quests == null) return "NO: native quest manager unavailable.";
            var native = quests.OfType<PrologueQuest>().ToList();
            var lines = new System.Collections.Generic.List<string>
            {
                "OK: native prologue commissions=" + native.Count(q => !q.IsFinalized)
                + "; main=" + (PrologueJournalQuest.Active?.StringId ?? (StoryProbeState.Data.JournalCompleted ? "completed" : "none"))
                + "; journal=" + (PrologueJournalQuest.Active?.Describe() ?? (StoryProbeState.Data.JournalCompleted ? "native completed journal" : "inactive"))
            };
            foreach (var def in StoryProbeDefs.Representatives)
            {
                if (string.IsNullOrEmpty(def.QuestTargetNpcId)) continue;
                var quest = native.FirstOrDefault(q => q.NpcId == def.Id && !q.IsFinalized);
                lines.Add("giver=" + def.Id + " accepted=" + StoryProbeState.IsQuestAccepted(def.Id)
                    + " done=" + StoryProbeState.IsQuestDone(def.Id)
                    + " turnedIn=" + StoryProbeState.IsQuestTurnedIn(def.Id)
                    + " native=" + (quest?.StringId ?? "none")
                    + " nativeObjectivesDone=" + (quest?.ObjectivesDone == true));
            }
            return string.Join("\n", lines);
        }

        public static bool CancelAll()
        {
            StoryPracticeQuest.CancelAll();
            try
            {
                var quests = Campaign.Current?.QuestManager?.Quests;
                if (quests == null) return StoryProbeState.Data.QuestAccepted.Count == 0;
                foreach (var quest in quests.OfType<PrologueQuest>().Where(q => !q.IsFinalized).ToList())
                    quest.CancelForPrologueRestart();
                return !quests.OfType<PrologueQuest>().Any(q => !q.IsFinalized);
            }
            catch (System.Exception ex)
            {
                DebugLogger.Log($"[PrologueQuest] cancellation failed: {ex.Message}");
                return false;
            }
        }

        public static PrologueQuest Start(string npcId)
        {
            try
            {
                if (string.IsNullOrEmpty(npcId)) return null;
                if (Campaign.Current?.IssueManager == null) return null;

                var def = StoryProbeDefs.GetNpc(npcId);
                if (def == null || string.IsNullOrEmpty(def.QuestTargetNpcId)) return null;

                var hero = StoryProbeMissionBehavior.Current?.GetHero(npcId);
                if (hero == null)
                {
                    DebugLogger.Log($"[PrologueQuest] no hero for '{npcId}' (scene not ready)");
                    return null;
                }

                var existing = FindActive(npcId);
                if (existing != null) return existing;

                var issue = new PrologueIssue(hero, npcId, def.QuestTargetNpcId, def.QuestLogKey);
                IssueBase Factory(in PotentialIssueData pid, Hero owner) => issue;
                var data = new PotentialIssueData(Factory, typeof(PrologueIssue), IssueBase.IssueFrequency.Common);

                if (hero.Issue != null && !(hero.Issue is PrologueIssue))
                {
                    Campaign.Current.IssueManager.DeactivateIssue(hero.Issue);
                }

                if (!Campaign.Current.IssueManager.CreateNewIssue(data, hero))
                {
                    DebugLogger.Log($"[PrologueQuest] CreateNewIssue rejected for {hero.Name}");
                    return null;
                }

                var quest = issue.AcceptPrologueQuest();
                DebugLogger.Log($"[PrologueQuest] started for '{npcId}' -> {quest?.StringId ?? "NULL"}");
                return quest;
            }
            catch (System.Exception ex)
            {
                DebugLogger.Log($"[PrologueQuest] start '{npcId}' failed: {ex.Message}");
                return null;
            }
        }

        public static PrologueQuest FindActive(string npcId)
        {
            try
            {
                return Campaign.Current?.QuestManager?.Quests?
                    .OfType<PrologueQuest>()
                    .FirstOrDefault(q => q.NpcId == npcId && !q.IsFinalized);
            }
            catch { return null; }
        }

        public static PrologueQuest FindActiveForTarget(string npcId)
        {
            try
            {
                return Campaign.Current?.QuestManager?.Quests?
                    .OfType<PrologueQuest>()
                    .FirstOrDefault(q => q.NpcId == npcId && !q.IsFinalized);
            }
            catch { return null; }
        }
    }

    /// <summary>主线日记：原版特殊Quest无需Issue；内容和里程碑由StoryJournal.json定义。</summary>
    public sealed class PrologueJournalQuest : QuestBase
    {
        [SaveableField(1)] private string _recorded = "|";
        [SaveableField(2)] private string _townId;

        private sealed class Entry
        {
            public string Id { get; set; }
            public string TextKey { get; set; }
            public string[] Conditions { get; set; }
            public string[] Actors { get; set; }
            public string SourceTextKey { get; set; }
        }
        private sealed class Definition
        {
            public Entry[] Entries { get; set; }
        }
        private Definition _definition;

        public PrologueJournalQuest(string townId)
            : base("lwn_prologue_journal", null, CampaignTime.Never, 0)
        { _townId = townId; }

        public static PrologueJournalQuest Active => Campaign.Current?.QuestManager?.Quests
            .OfType<PrologueJournalQuest>().FirstOrDefault(q => !q.IsFinalized);
        // 原版1.3起由非空SpecialQuestType派生IsSpecialQuest；override签名差异必须在类型内分支。
#if MB2_GE_130
        public override string SpecialQuestType => "lwn_prologue";
#else
        public override bool IsSpecialQuest => true;
#endif
        public override bool IsRemainingTimeHidden => true;
        public override TextObject Title => new TextObject(LWNTextHelper.ResolveText("LWN_story_journal_title", "A way into town"));
        protected override void SetDialogs() { }
        protected override void InitializeQuestOnGameLoad() { }
        protected override void HourlyTick() { }
        protected override void RegisterEvents() { }
        protected override void OnTimedOut() { }
        public override void OnFailed() { }

        /// <summary>只在自然入场或已开始主线恢复时建任务；不会从查询命令接任务。</summary>
        public static bool EnsureStarted(string townId)
        {
            try
            {
                if (Campaign.Current?.QuestManager == null || string.IsNullOrEmpty(townId)) return false;
                if (StoryProbeState.Data.JournalCompleted || StoryProbeState.Data.CancellationPending || StoryProbeState.Data.Suspended) return false;
                var quest = Active;
                if (quest == null)
                {
                    quest = new PrologueJournalQuest(townId);
                    quest.EnsureDefinition();
                    quest.StartQuest();
                    DebugLogger.Log("[StoryJournal] started town=" + townId);
                }
                quest.EnsureDefinition();
                var d = StoryProbeState.Data;
                d.JournalShownTextKeys ??= new System.Collections.Generic.List<string>();
                if (d.JournalKnowledgeVersion == 0 && quest._definition != null)
                {
                    // 旧档没有展示句收据：只按已有线索事实恢复对应条目，不推断未知线索。
                    foreach (var entry in quest._definition.Entries.Where(e => !string.IsNullOrEmpty(e.SourceTextKey)
                        && e.Conditions.Any(c => c.StartsWith("clue:", System.StringComparison.Ordinal))
                        && Matches(e.Conditions, e.Actors?.FirstOrDefault())))
                        if (!d.JournalShownTextKeys.Contains(entry.SourceTextKey)) d.JournalShownTextKeys.Add(entry.SourceTextKey);
                    d.JournalKnowledgeVersion = 1;
                }
                quest.Synchronize();
                return true;
            }
            catch (System.Exception ex)
            { DebugLogger.Log("[StoryJournal] start failed: " + ex.Message); return false; }
        }

        public static void Tick()
        {
            var d = StoryProbeState.Data;
            if (d.CancellationPending || d.Suspended) return;
            if (Active == null && StoryProbeState.Phase != StoryProbePhase.Done
                && !string.IsNullOrEmpty(d.StorySettlementId)
                && (d.Stripped || d.TutorialLost || d.IntroPlayed || d.NpcStep.Count > 0)) EnsureStarted(d.StorySettlementId);
            Active?.Synchronize();
        }

        public string Describe() => "town=" + _townId + " recorded=" + (_recorded ?? "|");
        public void CancelForRestart()
        {
            if (!IsFinalized) CompleteQuestWithCancel(new TextObject(LWNTextHelper.ResolveText("LWN_story_journal_cancel", "I have put this search aside.")));
        }

        public void Synchronize()
        {
            if (IsFinalized) return;
            try
            {
                EnsureDefinition();
                var def = _definition;
                if (def?.Entries == null) return;
                _recorded ??= "|";
                foreach (var entry in def.Entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Id) || string.IsNullOrEmpty(entry.TextKey)
                        || _recorded.Contains("|" + entry.Id + "|")) continue;
                    if (!string.IsNullOrEmpty(entry.SourceTextKey)
                        && StoryProbeState.Data.JournalShownTextKeys?.Contains(entry.SourceTextKey) != true) continue;
                    if (entry.Actors?.Length > 0
                        ? !entry.Actors.Any(actor => Matches(entry.Conditions, actor))
                        : !Matches(entry.Conditions, null)) continue;
                    AddLog(new TextObject(LWNTextHelper.ResolveText(entry.TextKey)), hideInformation: true);
                    _recorded += entry.Id + "|";
                    DebugLogger.Log("[StoryJournal] recorded " + entry.Id + " town=" + _townId);
                }
                if (StoryProbeState.Phase == StoryProbePhase.Done)
                {
                    CompleteQuestWithSuccess();
                    // 原版JournalLogsCampaignBehavior已归档全文；活动Quest列表移除是正常完成。
                    StoryProbeState.Data.JournalCompleted = true;
                }
            }
            catch (System.Exception ex) { DebugLogger.Log("[StoryJournal] sync failed: " + ex.Message); }
        }

        private void EnsureDefinition()
        {
            if (_definition == null) _definition = LoadDefinition(System.IO.Path.Combine(BasePath.Name, "Modules", "LivingWorldNpcs",
                "ModuleData", "DesignData", "StoryJournal.json"));
        }

        public static void NoteShown(string textKey)
        {
            var quest = Active;
            if (quest == null || string.IsNullOrEmpty(textKey)) return;
            try
            {
                quest.EnsureDefinition();
                if (!quest._definition.Entries.Any(e => e.SourceTextKey == textKey)) return;
                var keys = StoryProbeState.Data.JournalShownTextKeys ??= new System.Collections.Generic.List<string>();
                if (!keys.Contains(textKey)) keys.Add(textKey);
            }
            catch (System.Exception ex) { DebugLogger.Log("[StoryJournal] shown receipt failed: " + ex.Message); }
        }

        public static string Reload()
        {
            try
            {
                var def = LoadDefinition(System.IO.Path.Combine(BasePath.Name, "Modules", "LivingWorldNpcs",
                    "ModuleData", "DesignData", "StoryJournal.json"));
                if (Active != null) Active._definition = def;
                return "journal entries=" + def.Entries.Length + "; applies on next story tick; existing receipts retained";
            }
            catch (System.Exception ex) { return "journal rejected; previous definition retained: " + ex.Message; }
        }

        private static Definition LoadDefinition(string path)
        {
            StoryDialogueLoader.EnsureLoaded();
            var shownKeys = new System.Collections.Generic.HashSet<string>(StoryDialogueRegistry.All
                .SelectMany(d => d.Nodes).Select(n => n.TextKey), System.StringComparer.Ordinal);
            var root = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path),
                new Newtonsoft.Json.Linq.JsonLoadSettings { DuplicatePropertyNameHandling = Newtonsoft.Json.Linq.DuplicatePropertyNameHandling.Error });
            if (root.Properties().Any(p => p.Name != "Entries") || !(root["Entries"] is Newtonsoft.Json.Linq.JArray entries)
                || entries.Count == 0 || entries.Count > 64) throw new System.InvalidOperationException("invalid journal Entries");
            var ids = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            foreach (var token in entries)
            {
                if (!(token is Newtonsoft.Json.Linq.JObject item)
                    || item.Properties().Any(p => p.Name != "Id" && p.Name != "TextKey" && p.Name != "Conditions" && p.Name != "Actors" && p.Name != "SourceTextKey"))
                    throw new System.InvalidOperationException("unknown journal field");
                var entry = item.ToObject<Entry>();
                if (item["Id"]?.Type != Newtonsoft.Json.Linq.JTokenType.String
                    || item["TextKey"]?.Type != Newtonsoft.Json.Linq.JTokenType.String
                    || item["SourceTextKey"] != null && item["SourceTextKey"].Type != Newtonsoft.Json.Linq.JTokenType.String
                    || item["Actors"] != null && (!(item["Actors"] is Newtonsoft.Json.Linq.JArray actors)
                        || actors.Any(a => a.Type != Newtonsoft.Json.Linq.JTokenType.String)))
                    throw new System.InvalidOperationException("invalid journal value types");
                if (entry == null || string.IsNullOrEmpty(entry.Id) || entry.Id.Length > 32
                    || entry.Id.Any(c => !(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_'))
                    || !ids.Add(entry.Id) || string.IsNullOrEmpty(entry.TextKey) || !entry.TextKey.StartsWith("LWN_", System.StringComparison.Ordinal)
                    || !(item["Conditions"] is Newtonsoft.Json.Linq.JArray conditions)
                    || conditions.Any(c => c.Type != Newtonsoft.Json.Linq.JTokenType.String || string.IsNullOrWhiteSpace(c.ToString())))
                    throw new System.InvalidOperationException("invalid journal entry " + entry?.Id);
                if (entry.Actors != null && entry.Actors.Any(a => StoryProbeDefs.GetNpc(a) == null))
                    throw new System.InvalidOperationException("unknown journal actor");
                if (entry.SourceTextKey != null && !shownKeys.Contains(entry.SourceTextKey))
                    throw new System.InvalidOperationException("journal source is not a published sentence: " + entry.SourceTextKey);
                // 逐个校验，不让首个false掩盖后面的未知词。
                foreach (string condition in entry.Conditions) Matches(new[] { condition }, entry.Actors?.FirstOrDefault());
            }
            return root.ToObject<Definition>();
        }

        private static bool Matches(string[] conditions, string actorId)
        {
            if (conditions == null) return false;
            var d = StoryProbeState.Data;
            foreach (string condition in conditions)
            {
                bool value;
                // 持久事实是查询能力；哪条事实写什么日志由JSON选定。
                switch (condition)
                {
                    case "tutorial_lost": value = d.TutorialLost; break;
                    case "sentence_started": value = d.JailDone || PlayerDetentionBehavior.IsSentenceRunning(d.DetentionOwnerId)
                        || PlayerDetentionBehavior.IsSentenceCompleted(d.DetentionOwnerId); break;
                    case "jail_done": value = d.JailDone; break;
                    case "gate_opened": value = d.GateOpened; break;
                    case "goal_reached": value = d.GoalReached; break;
                    case "has_commission": value = d.QuestAccepted.Count > 0; break;
                    default:
                        if (condition != null && condition.StartsWith("goal:", System.StringComparison.Ordinal))
                        {
                            if (!new[] { "enter", "asked", "quest", "tool", "done" }.Contains(condition.Substring(5)))
                                throw new System.InvalidOperationException("unknown journal goal " + condition);
                            value = StoryProbeState.Goal == condition.Substring(5)
                                || d.GoalHistory?.Contains(condition.Substring(5)) == true;
                        }
                        else if (!StoryDialogueFacts.TryEvaluateAll(new[] { condition }, new StoryDialogueContext { ActorId = actorId },
                            out value, out string error, out var trail))
                            throw new System.InvalidOperationException(error);
                        break;
                }
                if (!value) return false;
            }
            return true;
        }
    }
}
