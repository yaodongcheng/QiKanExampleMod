using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.Settlements;

namespace LivingWorldNpcs
{
    /// <summary>
    /// CampaignBehavior 监听 CampaignEvents.QuestCompletedEvent，
    /// 将原版 Quest 完成事件路由到 QuestConsequenceResolver。
    ///
    /// 注册到 MySubModule.OnGameStart。
    /// </summary>
    public class QuestConsequenceBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.OnQuestCompletedEvent.AddNonSerializedListener(this, OnQuestCompleted);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // 无状态——因果表在 QuestConsequenceResolver 中管理
        }

        private void OnQuestCompleted(QuestBase quest, QuestBase.QuestCompleteDetails detail)
        {
            if (quest == null) return;

            try
            {
                // 1. 映射原版 Quest → VANILLA_* ID
                string questId = VanillaQuestMapping.MapQuestToId(quest);
                if (questId == null) return; // 不是我们关注的 40 种 Quest 类型

                // 2. 映射完成方式
                var outcome = VanillaQuestMapping.MapCompletionDetail(detail);

                // 3. 检测活捉 vs 击杀（从 Quest 的 TargetHero 状态推断）
                string outcomeDetail = "";
                Hero targetHero = null;

                // TargetHero 通常通过 IssueBase 关联
                var issue = quest.QuestGiver?.Issue;
                if (issue != null)
                {
                    // 尝试通过反射或已知 Issue 类型获取 target
                    targetHero = GetTargetHeroFromIssue(issue);
                }

                if (targetHero != null)
                {
                    if (targetHero.IsPrisoner) outcomeDetail = "Capture";
                    else if (!targetHero.IsAlive) outcomeDetail = "Kill";
                }

                // 4. 调因果引擎
                Hero questGiver = quest.QuestGiver;
                Settlement targetSettlement = issue?.IssueSettlement ?? questGiver?.CurrentSettlement;

                QuestConsequenceResolver.ResolveConsequences(
                    questId, outcome, outcomeDetail,
                    questGiver, targetHero, targetSettlement);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[QuestConsequence] 处理 QuestCompleted 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 尝试从 IssueBase 实例获取 TargetHero。
        /// 不同 Issue 类型的目标存储在不同属性中，这里做最佳尝试。
        /// </summary>
        private static Hero GetTargetHeroFromIssue(IssueBase issue)
        {
            if (issue == null) return null;

            try
            {
                // 使用反射尝试获取常见的 target hero 属性
                var issueType = issue.GetType();

                // 尝试常见属性名
                foreach (var propName in new[] { "TargetHero", "RivalHero", "Son", "Daughter",
                    "CompanionHero", "EnemyHero", "KidnappedHero", "Antagonist" })
                {
                    var prop = issueType.GetProperty(propName);
                    if (prop != null)
                    {
                        var val = prop.GetValue(issue);
                        if (val is Hero hero && hero != null)
                            return hero;
                    }
                }

                // 尝试字段（备用）
                foreach (var fieldName in new[] { "_targetHero", "_rivalHero", "_enemyHero" })
                {
                    var field = issueType.GetField(fieldName,
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null)
                    {
                        var val = field.GetValue(issue);
                        if (val is Hero hero && hero != null)
                            return hero;
                    }
                }
            }
            catch
            {
                // 反射失败不影响因果引擎主流程
            }

            return null;
        }
    }
}
