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
}
