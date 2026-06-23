using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Issues;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 原版 IssueBase 子类 Type Name → VANILLA_* ID 映射表。
    /// 使用 type name 字符串（而非 typeof）因为原版 Issue 类均嵌套于父 Behavior 类中。
    ///
    /// 覆盖全部 40 种原版 Issue 类型。
    /// 运行时通过 issue.GetType().Name 进行匹配。
    /// </summary>
    public static class VanillaQuestMapping
    {
        /// <summary>
        /// Issue 类型名（不含命名空间）→ VANILLA_* ID。
        /// </summary>
        private static readonly Dictionary<string, string> IssueNameToId = new Dictionary<string, string>
        {
            // ── 村庄要人（13 种）──
            { "HeadmanNeedsGrainIssue",                    "VANILLA_HeadmanNeedsGrain" },
            { "HeadmanNeedsToDeliverAHerdIssue",            "VANILLA_HeadmanDeliverHerd" },
            { "HeadmanVillageNeedsDraughtAnimalsIssue",     "VANILLA_VillageDraughtAnimals" },
            { "VillageNeedsToolsIssue",                     "VANILLA_VillageNeedsTools" },
            { "VillageNeedsCraftingMaterialsIssue",         "VANILLA_VillageCraftingMaterials" },
            { "LandlordNeedsAccessToVillageCommonsIssue",   "VANILLA_LandlordVillageCommons" },
            { "LandLordNeedsManualLaborersIssue",           "VANILLA_LandlordManualLaborers" },
            { "LandLordTheArtOfTheTradeIssue",              "VANILLA_LandlordTradeArt" },
            { "LandlordTrainingForRetainersIssue",          "VANILLA_LandlordTraining" },
            { "ExtortionByDesertersIssue",                  "VANILLA_ExtortionByDeserters" },
            { "FamilyFeudIssue",                            "VANILLA_FamilyFeud" },
            { "NotableWantsDaughterFoundIssue",             "VANILLA_NotableDaughterFound" },
            { "RuralNotableInnAndOutIssue",                 "VANILLA_RuralNotableInnOut" },

            // ── 城镇工匠/商人（6 种）──
            { "ArtisanCantSellProductsAtAFairPriceIssue",   "VANILLA_ArtisanCantSell" },
            { "ArtisanOverpricedGoodsIssue",                "VANILLA_ArtisanOverpricedGoods" },
            { "EscortMerchantCaravanIssue",                 "VANILLA_EscortMerchantCaravan" },
            { "CaravanAmbushIssue",                         "VANILLA_CaravanAmbush" },
            { "BettingFraudIssue",                          "VANILLA_BettingFraud" },
            { "RevenueFarmingIssue",                        "VANILLA_RevenueFarming" },

            // ── 帮派头目（5 种）──
            { "GangLeaderNeedsRecruitsIssue",               "VANILLA_GangNeedsRecruits" },
            { "GangLeaderNeedsSpecialWeaponsIssue",         "VANILLA_GangSpecialWeapons" },
            { "GangLeaderNeedsWeaponsIssue",                "VANILLA_GangNeedsWeapons" },
            { "GangLeaderNeedsToOffloadStolenGoodsIssue",   "VANILLA_GangOffloadStolenGoods" },
            { "RivalGangMovingInIssue",                     "VANILLA_RivalGangMovingIn" },
            { "SnareTheWealthyIssue",                       "VANILLA_SnareTheWealthy" },

            // ── 领主/贵族（10 种）──
            { "LordNeedsHorsesIssue",                       "VANILLA_LordNeedsHorses" },
            { "LordNeedsGarrisonTroopsIssue",               "VANILLA_LordNeedsGarrisonTroops" },
            { "LordsNeedsTutorIssue",                       "VANILLA_LordsNeedsTutor" },
            { "LordWantsRivalCapturedIssue",                "VANILLA_LordWantsRivalCaptured" },
            { "LadysKnightOutIssue",                        "VANILLA_LadysKnightOut" },
            { "ProdigalSonIssue",                           "VANILLA_ProdigalSon" },
            { "TheSpyPartyIssue",                           "VANILLA_TheSpyParty" },
            { "ArmyNeedsSuppliesIssue",                     "VANILLA_ArmyNeedsSupplies" },
            { "ScoutEnemyGarrisonsIssue",                   "VANILLA_ScoutEnemyGarrisons" },
            { "RaidAnEnemyTerritoryIssue",                  "VANILLA_RaidEnemyTerritory" },
            { "TheConquestOfSettlementIssue",               "VANILLA_ConquestOfSettlement" },

            // ── 通用/全局（6 种）──
            { "NearbyBanditBaseIssue",                      "VANILLA_NearbyBanditBase" },
            { "MerchantArmyOfPoachersIssue",                "VANILLA_ArmyOfPoachers" },
            { "MerchantNeedsHelpWithOutlawsIssue",          "VANILLA_MerchantNeedsHelpWithOutlaws" },
            { "SmugglersIssue",                             "VANILLA_Smugglers" },
            { "CapturedByBountyHuntersIssue",               "VANILLA_CapturedByBountyHunters" },
            { "LandLordCompanyOfTroubleIssue",              "VANILLA_CompanyOfTrouble" },
            { "LesserNobleRevoltIssue",                     "VANILLA_LesserNobleRevolt" },
        };

        // 反向索引：VANILLA_* ID → Issue 类型名
        private static readonly Dictionary<string, string> IdToIssueName;

        static VanillaQuestMapping()
        {
            IdToIssueName = new Dictionary<string, string>();
            foreach (var kvp in IssueNameToId)
            {
                IdToIssueName[kvp.Value] = kvp.Key;
            }
        }

        /// <summary>
        /// 从原版 QuestBase 推导 VANILLA_* ID。
        /// 查 QuestGiver 的 QuestHistory，取最近一条 "Issued" 或 "Causality" 记录。
        /// </summary>
        public static string MapQuestToId(QuestBase quest)
        {
            if (quest?.QuestGiver == null) return null;

            var mem = AllNpcMemoryManager.GetMemory(quest.QuestGiver.StringId);
            if (mem != null)
            {
                var record = mem.FindLatestQuestIssued();
                if (record != null && !string.IsNullOrEmpty(record.QuestId))
                    return record.QuestId;
            }

            // Fallback：QuestGiver.Issue 还在
            var issue = quest.QuestGiver.Issue;
            if (issue != null)
            {
                string typeName = issue.GetType().Name;
                if (IssueNameToId.TryGetValue(typeName, out string id))
                    return id;
            }

            return null;
        }

        /// <summary>
        /// 原版 QuestCompleteDetails → QuestCompletionOutcome。
        /// </summary>
        public static QuestConsequenceResolver.QuestCompletionOutcome MapCompletionDetail(
            QuestBase.QuestCompleteDetails detail)
        {
            switch (detail)
            {
                case QuestBase.QuestCompleteDetails.Success:
                    return QuestConsequenceResolver.QuestCompletionOutcome.Success;
                case QuestBase.QuestCompleteDetails.Fail:
                    return QuestConsequenceResolver.QuestCompletionOutcome.Fail;
                case QuestBase.QuestCompleteDetails.FailWithBetrayal:
                    return QuestConsequenceResolver.QuestCompletionOutcome.Betrayal;
                case QuestBase.QuestCompleteDetails.Timeout:
                    return QuestConsequenceResolver.QuestCompletionOutcome.Timeout;
                case QuestBase.QuestCompleteDetails.Cancel:
                    return QuestConsequenceResolver.QuestCompletionOutcome.Cancel;
                default:
                    return QuestConsequenceResolver.QuestCompletionOutcome.Cancel;
            }
        }

        /// <summary>
        /// 从 Issue 类型名获取 VANILLA_* ID。
        /// </summary>
        public static string GetIdForIssueTypeName(string typeName)
        {
            if (IssueNameToId.TryGetValue(typeName, out string id))
                return id;
            return null;
        }

        /// <summary>
        /// 从 VANILLA_* ID 获取 Issue 类型名。
        /// </summary>
        public static string GetIssueTypeNameForId(string id)
        {
            if (IdToIssueName.TryGetValue(id, out string name))
                return name;
            return null;
        }
    }
}
