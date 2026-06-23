using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace LivingWorldNpcs
{
    /// <summary>
    /// JSON 驱动的任务因果后果解析器。
    /// 所有原版 Quest 完成后统一走这里查因果表，生成后续 WorldEvent。
    ///
    /// C# 侧只负责：加载 JSON → 查表 → 调 WorldEventSimulator/WorldEventDatabase 执行。
    /// 因果逻辑全部在 JSON 中定义。
    /// </summary>
    public static class QuestConsequenceResolver
    {
        public enum QuestCompletionOutcome
        {
            Success,
            Fail,
            Betrayal,
            Timeout,
            Cancel
        }

        /// <summary>
        /// JSON 中定义的后续行动类型。
        /// </summary>
        public enum FollowUpAction
        {
            Suppress,
            BoostWeight,
            ScheduleIssue,
            ScheduleWorldEvent,
        }

        /// <summary>
        /// 后续委托的发布者与源 Quest 委托人的关系。
        /// </summary>
        public enum GiverRelation
        {
            /// <summary>同一个 NPC</summary>
            SameNpc,
            /// <summary>同定居点的另一个名人（村长→商人）</summary>
            SameSettlement,
            /// <summary>同家族/同阵营成员</summary>
            SameClan,
            /// <summary>对立面（目标英雄 / 敌对帮派）</summary>
            Enemy,
            /// <summary>目标英雄本人</summary>
            Target,
        }

        /// <summary>
        /// JSON 反序列化用的后续条目。
        /// </summary>
        [Serializable]
        public class FollowUpEntry
        {
            public string quest;
            public string action;
            public int? delayMin;
            public int? delayMax;
            public int? durationDays;
            public double? multiplier;
            public double? probability;
            public string condition;
            public string target;
            /// <summary>后续委托的发布者与源 Quest 委托人的关系。不填默认 SameNpc。</summary>
            public string giverRelation;
        }

        [Serializable]
        public class CausalityRule
        {
            public string sourceQuest;
            public string outcome;
            public List<FollowUpEntry> followUps;
        }

        /// <summary>
        /// 因果表：源Quest ID → (完成方式 → 后续列表)
        /// </summary>
        private static Dictionary<string, Dictionary<QuestCompletionOutcome, List<FollowUpEntry>>> _causalityTable
            = new Dictionary<string, Dictionary<QuestCompletionOutcome, List<FollowUpEntry>>>();

        private static bool _loaded = false;

        /// <summary>
        /// 调试开关：true = 因果链后续立即出现（delay 归零，跳过所有概率检查）。
        /// 仅用于测试，发布前改回 false。
        /// </summary>
        public static bool DebugInstantFollowUps = true;

        /// <summary>
        /// 从 JSON 文件加载因果表。
        /// </summary>
        public static void LoadFromJson(string jsonPath)
        {
            try
            {
                if (!File.Exists(jsonPath))
                {
                    DebugLogger.Log($"[QuestConsequence] 因果表 JSON 不存在: {jsonPath}");
                    return;
                }

                string json = File.ReadAllText(jsonPath);
                var rules = JsonConvert.DeserializeObject<List<CausalityRule>>(json);
                if (rules == null) return;

                _causalityTable.Clear();
                foreach (var rule in rules)
                {
                    if (!Enum.TryParse<QuestCompletionOutcome>(rule.outcome, out var outcome))
                        continue;

                    if (!_causalityTable.ContainsKey(rule.sourceQuest))
                        _causalityTable[rule.sourceQuest] = new Dictionary<QuestCompletionOutcome, List<FollowUpEntry>>();

                    if (!_causalityTable[rule.sourceQuest].ContainsKey(outcome))
                        _causalityTable[rule.sourceQuest][outcome] = new List<FollowUpEntry>();

                    if (rule.followUps != null)
                        _causalityTable[rule.sourceQuest][outcome].AddRange(rule.followUps);
                }

                _loaded = true;
                DebugLogger.Log($"[QuestConsequence] 因果表已加载: {rules.Count} 条规则，{_causalityTable.Count} 个源Quest");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[QuestConsequence] 因果表加载失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 尝试从默认路径加载因果表。
        /// </summary>
        public static void TryLoadDefault()
        {
            if (_loaded) return;

            // 默认路径：模块根目录/ModuleData/DesignData/causality_chains.json
            string modulePath = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            // Assembly 在 bin/Win64_Shipping_Client/ 下，往上走 2 层到模块根
            string defaultPath = Path.Combine(modulePath, "..", "..", "ModuleData", "DesignData", "causality_chains.json");
            defaultPath = Path.GetFullPath(defaultPath);

            LoadFromJson(defaultPath);
        }

        /// <summary>
        /// 主入口：Quest 完成时调用。
        /// </summary>
        /// <param name="questId">VANILLA_* 或 LWNPCS_* Quest ID</param>
        /// <param name="outcome">完成方式</param>
        /// <param name="outcomeDetail">"Capture"/"Kill"/"Persuade"/"" 等细分</param>
        /// <param name="questGiver">源 Quest 的委托人</param>
        /// <param name="targetHero">源 Quest 的目标英雄（可能 null）</param>
        /// <param name="targetSettlement">源 Quest 的目标定居点（可能 null）</param>
        public static void ResolveConsequences(
            string questId,
            QuestCompletionOutcome outcome,
            string outcomeDetail,
            Hero questGiver,
            Hero targetHero,
            Settlement targetSettlement)
        {
            TryLoadDefault();

            if (string.IsNullOrEmpty(questId)) return;

            // 1. 查因果表
            if (!_causalityTable.TryGetValue(questId, out var outcomeDict))
            {
                // 无后续条目 → 自然断链（不记录日志，这在因果链末端是正常的）
                return;
            }

            if (!outcomeDict.TryGetValue(outcome, out var followUps))
            {
                // 此完成方式无后续定义
                return;
            }

            // 2. 逐个检查条件 + 执行
            int scheduled = 0;
            foreach (var fu in followUps)
            {
                if (!CheckConditions(fu, targetHero, questGiver)) continue;
                if (!DebugInstantFollowUps && fu.probability.HasValue && MBRandom.RandomFloat > (float)fu.probability.Value) continue;

                ExecuteFollowUp(fu, questId, questGiver, targetHero, targetSettlement);
                scheduled++;
            }

            if (scheduled > 0)
                DebugLogger.Log($"[QuestConsequence] {questId} + {outcome} → 排入 {scheduled} 个后续");
            else
                DebugLogger.Log($"[QuestConsequence] {questId} + {outcome} → 无后续条目（断链）");
        }

        /// <summary>
        /// 检查后续条目的条件。
        /// </summary>
        private static bool CheckConditions(FollowUpEntry fu, Hero targetHero, Hero questGiver)
        {
            if (string.IsNullOrEmpty(fu.condition)) return true;

            switch (fu.condition)
            {
                case "RequireTargetAlive":
                    return targetHero != null && targetHero.IsAlive;
                case "RequireTargetDead":
                    return targetHero != null && !targetHero.IsAlive;
                case "RequireGiverAlive":
                    return questGiver != null && questGiver.IsAlive;
                default:
                    return true;
            }
        }

        /// <summary>
        /// 执行单个后续条目。
        /// </summary>
        private static void ExecuteFollowUp(
            FollowUpEntry fu,
            string sourceQuestId,
            Hero questGiver,
            Hero targetHero,
            Settlement targetSettlement)
        {
            if (string.IsNullOrEmpty(fu.action)) return;

            switch (fu.action)
            {
                case "Suppress":
                    ExecuteSuppress(fu, questGiver, targetSettlement);
                    break;
                case "BoostWeight":
                    ExecuteBoostWeight(fu, questGiver, targetSettlement);
                    break;
                case "ScheduleIssue":
                    ExecuteScheduleIssue(fu, sourceQuestId, questGiver, targetHero, targetSettlement);
                    break;
                case "ScheduleWorldEvent":
                    ExecuteScheduleWorldEvent(fu, sourceQuestId, questGiver, targetHero, targetSettlement);
                    break;
            }
        }

        /// <summary>
        /// 抑制某类 Issue：在目标区域的 NPC 记忆上标记抑制天数。
        /// </summary>
        private static void ExecuteSuppress(FollowUpEntry fu, Hero questGiver, Settlement targetSettlement)
        {
            int durationDays = fu.durationDays ?? 30;
            // 对 questGiver 所在定居点附近的所有 Hero 设置 Issue 抑制
            var settlement = questGiver?.CurrentSettlement ?? targetSettlement;
            if (settlement == null) return;

            // 简单实现：在 questGiver 的记忆中记录抑制
            var mem = AllNpcMemoryManager.GetMemory(questGiver.StringId);
            if (mem != null)
            {
                mem.AddHistory("system",
                    $"CausalitySuppress|{fu.quest}|{durationDays}|{CampaignTime.Now.ToDays}");
            }
        }

        /// <summary>
        /// 提升某类 Issue 权重：记录到 questGiver 的记忆中，供后续 Issue 生成时查询。
        /// </summary>
        private static void ExecuteBoostWeight(FollowUpEntry fu, Hero questGiver, Settlement targetSettlement)
        {
            int durationDays = fu.durationDays ?? 30;
            double multiplier = fu.multiplier ?? 2.0;

            var settlement = questGiver?.CurrentSettlement ?? targetSettlement;
            if (settlement == null) return;

            var mem = questGiver != null ? AllNpcMemoryManager.GetMemory(questGiver.StringId) : null;
            if (mem != null)
            {
                mem.AddHistory("system",
                    $"CausalityBoost|{fu.quest}|{multiplier}|{durationDays}|{CampaignTime.Now.ToDays}");
            }
        }

        /// <summary>
        /// 排期生成 Issue：解析 giverRelation 找到正确的后续委托人，
        /// 生成 WorldEvent 标记，因果记忆只写入该委托人。
        /// </summary>
        private static void ExecuteScheduleIssue(
            FollowUpEntry fu,
            string sourceQuestId,
            Hero questGiver,
            Hero targetHero,
            Settlement targetSettlement)
        {
            int delayDays;
            if (DebugInstantFollowUps)
            {
                delayDays = 0;
            }
            else
            {
                delayDays = fu.delayMin.HasValue
                    ? (fu.delayMax.HasValue
                        ? MBRandom.RandomInt(fu.delayMin.Value, fu.delayMax.Value + 1)
                        : fu.delayMin.Value)
                    : 7;
            }

            // 按 giverRelation 找到正确的后续委托人
            var nextGiver = ResolveGiver(fu.giverRelation, questGiver, targetHero, targetSettlement);
            var nextSettlement = nextGiver?.CurrentSettlement ?? targetSettlement ?? questGiver?.CurrentSettlement;

            // 生成 WorldEvent 标记（NoParty 类型），TargetHeroId 指向后续委托人
            var worldEvent = new WorldEventData
            {
                EventId = $"causality_{sourceQuestId}_{fu.quest}_{System.DateTime.Now.Ticks}",
                EventType = ResolveEventTypeForQuest(fu.quest),
                Status = WorldEventStatus.Active,
                TargetHeroId = nextGiver?.StringId ?? questGiver?.StringId,
                InstigatorHeroId = targetHero?.StringId,
                TargetSettlementId = nextSettlement?.StringId,
                IsGenericInstigator = true,
                CreatedDay = (float)CampaignTime.Now.ToDays,
                DayLimit = delayDays + (fu.durationDays ?? 14),
                Severity = 3,
            };

            WorldEventDatabase.AddEvent(worldEvent);

            // 因果记忆写入后续委托人（只写一个人，不广播）
            WriteCausalityMemory(sourceQuestId, fu.quest, nextGiver, questGiver, targetHero);

            DebugLogger.Log($"[QuestConsequence] ScheduleIssue: {fu.quest} → giver={nextGiver?.Name} (relation={fu.giverRelation ?? "SameNpc"}) @ {delayDays}d delay, eventId={worldEvent.EventId}");
        }

        /// <summary>
        /// 排期生成 WorldEvent（物理层：spawn party 等）。
        /// </summary>
        private static void ExecuteScheduleWorldEvent(
            FollowUpEntry fu,
            string sourceQuestId,
            Hero questGiver,
            Hero targetHero,
            Settlement targetSettlement)
        {
            if (targetSettlement == null)
                targetSettlement = questGiver?.CurrentSettlement ?? questGiver?.HomeSettlement;

            if (targetSettlement == null) return;

            var nextGiver = ResolveGiver(fu.giverRelation, questGiver, targetHero, targetSettlement);

            WorldEventSimulator.ForceGenerateEvent(
                ResolveEventTypeForQuest(fu.quest),
                severity: 3);

            WriteCausalityMemory(sourceQuestId, fu.quest, nextGiver, questGiver, targetHero);

            DebugLogger.Log($"[QuestConsequence] ScheduleWorldEvent: {fu.quest} → giver={nextGiver?.Name} (relation={fu.giverRelation ?? "SameNpc"})");
        }

        /// <summary>
        /// 根据 Quest ID 推断合适的 WorldEventType。
        /// 这个映射是启发式的——因果引擎用 WorldEvent 作为"标记"来影响后续 Issue 的生成条件。
        /// </summary>
        private static WorldEventType ResolveEventTypeForQuest(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return WorldEventType.BanditRaid;

            if (questId.Contains("Bandit") || questId.Contains("Deserters") || questId.Contains("Poachers"))
                return WorldEventType.BanditRaid;
            if (questId.Contains("Gang") || questId.Contains("RivalGang") || questId.Contains("Snare"))
                return WorldEventType.BanditRaid; // 帮派活动用匪患类型
            if (questId.Contains("Caravan") || questId.Contains("Trade") || questId.Contains("Artisan")
                || questId.Contains("Revenue") || questId.Contains("Betting"))
                return WorldEventType.TradeDispute;
            if (questId.Contains("Noble") || questId.Contains("Revolt") || questId.Contains("Conquest")
                || questId.Contains("Lord") || questId.Contains("Lady") || questId.Contains("Prodigal"))
                return WorldEventType.NobleConflict;
            if (questId.Contains("Famine") || questId.Contains("Grain") || questId.Contains("Herd"))
                return WorldEventType.Famine;
            if (questId.Contains("Spy") || questId.Contains("Scout") || questId.Contains("Raid"))
                return WorldEventType.Assassination;
            if (questId.Contains("Family") || questId.Contains("Daughter") || questId.Contains("Inn"))
                return WorldEventType.RomanticConflict;
            if (questId.Contains("Prison") || questId.Contains("Captured"))
                return WorldEventType.Kidnapping;

            return WorldEventType.BanditRaid; // 默认
        }

        /// <summary>
        /// 按 giverRelation 找到后续委托的正确发布者。
        /// </summary>
        private static Hero ResolveGiver(string giverRelation, Hero questGiver, Hero targetHero, Settlement targetSettlement)
        {
            if (!Enum.TryParse<GiverRelation>(giverRelation, out var relation))
                relation = GiverRelation.SameNpc;

            var settlement = questGiver?.CurrentSettlement ?? targetSettlement;

            switch (relation)
            {
                case GiverRelation.SameNpc:
                    return questGiver;

                case GiverRelation.SameSettlement:
                    if (settlement != null)
                    {
                        // 找同定居点中非 questGiver 也非 targetHero 的第一个存活名人
                        foreach (var notable in settlement.Notables)
                        {
                            if (notable == null || !notable.IsAlive) continue;
                            if (notable == questGiver) continue;
                            if (notable == targetHero) continue;
                            return notable;
                        }
                    }
                    return questGiver; // 没有其他人，回退

                case GiverRelation.SameClan:
                    if (questGiver?.Clan != null)
                    {
                        foreach (var member in questGiver.Clan.Heroes)
                        {
                            if (member == questGiver) continue;
                            if (!member.IsAlive || member.IsPrisoner) continue;
                            return member;
                        }
                    }
                    return questGiver; // 没有合适的 clan 成员，回退

                case GiverRelation.Enemy:
                    return targetHero ?? questGiver;

                case GiverRelation.Target:
                    return targetHero ?? questGiver;

                default:
                    return questGiver;
            }
        }

        /// <summary>
        /// 将因果上下文写入后续委托人的 QuestHistory（而非 RecentHistory）。
        /// 只写一个人，生成 RecordType="Causality" 的 QuestRecord。
        /// </summary>
        private static void WriteCausalityMemory(
            string sourceQuestId,
            string followUpQuestId,
            Hero nextGiver,
            Hero questGiver,
            Hero targetHero)
        {
            if (nextGiver == null) return;

            string context;
            if (nextGiver == questGiver)
            {
                context = $"玩家完成了「{sourceQuestId}」，现在需要处理「{followUpQuestId}」";
            }
            else if (nextGiver == targetHero)
            {
                context = $"玩家完成了{questGiver?.Name}委托的「{sourceQuestId}」，「{followUpQuestId}」的事波及到了你";
            }
            else
            {
                context = $"玩家帮{questGiver?.Name}完成了「{sourceQuestId}」，影响到这里——需要处理「{followUpQuestId}」";
            }

            var record = new QuestRecord
            {
                QuestId = followUpQuestId,
                QuestName = followUpQuestId, // 后续会用实际名称覆盖
                RecordType = "Causality",
                GiverName = nextGiver.Name?.ToString() ?? "",
                SettlementName = nextGiver.CurrentSettlement?.Name?.ToString() ?? "",
                Summary = context,
                PreviousQuestId = sourceQuestId,
                CauseHeroName = questGiver?.Name?.ToString() ?? "",
                ChainDepth = 1,
            };

            var mem = AllNpcMemoryManager.GetMemory(nextGiver.StringId);
            if (mem != null)
            {
                mem.AddQuestRecord(record);
            }
        }

        /// <summary>
        /// 从 NPC 的 QuestHistory 中提取最近的因果上下文，供 CSV/LLM 叙事变量注入。
        /// </summary>
        public static CausalityContext ExtractCausalityContext(Hero hero)
        {
            if (hero == null) return null;

            var mem = AllNpcMemoryManager.GetMemory(hero.StringId);
            if (mem == null) return null;

            var record = mem.FindLatestQuestRecord("Causality");
            if (record == null || !record.HasCausalityContext) return null;

            return new CausalityContext
            {
                PreviousQuestId = record.PreviousQuestId,
                FollowUpQuestId = record.QuestId,
                CauseHeroName = record.CauseHeroName,
                ChainDepth = record.ChainDepth,
                Summary = record.Summary,
            };
        }
    }

    /// <summary>
    /// 因果上下文：从 NPC 记忆提取，供叙事变量填充。
    /// </summary>
    public class CausalityContext
    {
        /// <summary>上一个完成的委托 ID（VANILLA_* / LWNPCS_*）</summary>
        public string PreviousQuestId;
        /// <summary>后续的委托 ID</summary>
        public string FollowUpQuestId;
        /// <summary>引发当前局面的关键人物名</summary>
        public string CauseHeroName;
        /// <summary>因果链深度（第几步）</summary>
        public int ChainDepth;
        /// <summary>一句话摘要</summary>
        public string Summary;

        /// <summary>是否有因果上下文（深度 > 0）</summary>
        public bool HasContext => ChainDepth > 0 || !string.IsNullOrEmpty(PreviousQuestId);
    }
}
