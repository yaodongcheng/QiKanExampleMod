using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 极简信号 Issue：不生成 Quest，不走 Issue 对话流，仅用做触发 NPC 头顶 ! 标记。
    /// 当玩家与该 NPC 对话时，走 Intent 系统的 CommissionIntent（不是 Issue 对话流）。
    /// </summary>
    public class CommissionHubIssue : IssueBase
    {
        public CommissionHubIssue(Hero issueOwner)
            : base(issueOwner, CampaignTime.DaysFromNow(30f))
        {
        }

        // ── 抽象属性实现 ──
        public override TextObject IssueBriefByIssueGiver =>
            new TextObject("有委托任务可接");
        public override TextObject IssueAcceptByPlayer =>
            new TextObject("让我看看有什么委托。");
        public override TextObject IssueQuestSolutionExplanationByIssueGiver =>
            new TextObject("这位人物有委托任务需要帮手。上前对话可了解详情。");
        public override TextObject IssueQuestSolutionAcceptByPlayer =>
            new TextObject("接取委托");
        public override bool IsThereAlternativeSolution => false;
        public override bool IsThereLordSolution => false;
        public override TextObject Title =>
            new TextObject("委托任务");
        public override TextObject Description =>
            new TextObject("这位人物有委托任务需要帮手。上前对话可了解详情。");

        protected override void OnGameLoad()
        {
        }

        protected override void HourlyTick()
        {
        }

        protected override QuestBase GenerateIssueQuest(string questId)
        {
            // 信号 Issue 不生成 Quest——委托由 CommissionIntent 创建
            return null;
        }

        public override IssueFrequency GetFrequency()
        {
            return IssueFrequency.Common;
        }

        protected override bool CanPlayerTakeQuestConditions(Hero issueGiver,
            out PreconditionFlags flag, out Hero relationHero, out SkillObject skill)
        {
            flag = PreconditionFlags.None;
            relationHero = issueGiver;
            skill = null;

            if (issueGiver == null)
            {
                flag = PreconditionFlags.Relation;
                return false;
            }
            return true;
        }

        public override bool IssueStayAliveConditions()
        {
            return IssueOwner != null && IssueOwner.IsAlive;
        }

        protected override void CompleteIssueWithTimedOutConsequences()
        {
            // 信号 Issue 过期的清理已在 CommissionIssueBehavior 的 DailyTick 中处理
        }
    }

    /// <summary>
    /// CommissionIssueBehavior：监听 OnCheckForIssueEvent，
    /// 当 NPC 有可接委托时通过 CommissionHubIssue 触发原生 ! 标记。
    /// </summary>
    public class CommissionIssueBehavior : CampaignBehaviorBase
    {
        // 缓存：记录哪些 Hero 已被注册了 Issue（避免重复注册）
        private Dictionary<string, CommissionHubIssue> _activeIssues = new Dictionary<string, CommissionHubIssue>();

        public override void RegisterEvents()
        {
            CampaignEvents.OnCheckForIssueEvent.AddNonSerializedListener(this, OnCheckForIssue);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        private void OnCheckForIssue(Hero hero)
        {
            if (hero == null) return;
            if (hero == Hero.MainHero) return;
            if (hero.Issue != null) return; // 已有其他 Issue

            // 检查是否已达到并发上限
            int maxQuests = TrustSystem.GetMaxConcurrentQuests(TrustSystem.GetTrust(hero));
            int activeCount = CommissionQuest.GetActiveCommissionCount();
            if (activeCount >= maxQuests) return;

            // 检查该 NPC 是否有可接委托
            if (CommissionGenerator.HasCommissionsFor(hero, out int count) && count > 0)
            {
                // 注册信号 Issue
                Campaign.Current.IssueManager.AddPotentialIssueData(hero,
                    new PotentialIssueData(
                        (in PotentialIssueData pid, Hero issueOwner) =>
                        {
                            var issue = new CommissionHubIssue(issueOwner);
                            _activeIssues[issueOwner.StringId] = issue;
                            return issue;
                        },
                        typeof(CommissionHubIssue),
                        IssueBase.IssueFrequency.Common
                    ));
            }
        }

        private void OnDailyTick()
        {
            var toRemove = new List<string>();
            foreach (var kvp in _activeIssues)
            {
                var hero = Hero.FindFirst(h => h.StringId == kvp.Key);
                if (hero == null || !hero.IsAlive || !CommissionGenerator.HasCommissionsFor(hero, out _))
                {
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var id in toRemove)
            {
                _activeIssues.Remove(id);
            }

            // 定期全量清理已死亡/不存在的 Hero 条目
            if (_activeIssues.Count > 100)
            {
                var allDead = new List<string>();
                foreach (var kvp in _activeIssues)
                {
                    var h = Hero.FindFirst(x => x.StringId == kvp.Key);
                    if (h == null || !h.IsAlive)
                        allDead.Add(kvp.Key);
                }
                foreach (var id in allDead)
                    _activeIssues.Remove(id);
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            // CommissionIssueBehavior 不需要持久化——委托状态由 CommissionQuest 管理
        }
    }
}
