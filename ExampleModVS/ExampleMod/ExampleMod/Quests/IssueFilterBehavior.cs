using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Issues;

namespace LivingWorldNpcs
{
    /// <summary>
    /// CampaignBehavior that loads the Issue-type-to-WorldEvent-type blocking table
    /// and tracks per-DailyTick statistics for aggregated logging.
    ///
    /// Uses type name strings (not typeof) because vanilla Issue classes are nested
    /// inside their parent Behavior classes (e.g. HeadmanNeedsGrainIssueBehavior.HeadmanNeedsGrainIssue).
    /// </summary>
    public class IssueFilterBehavior : CampaignBehaviorBase
    {
        /// <summary>
        /// Mapping: WorldEventType → Issue type NAMES that are BLOCKED during this event.
        /// </summary>
        private static readonly Dictionary<WorldEventType, HashSet<string>> _blockedTypeNames =
            new Dictionary<WorldEventType, HashSet<string>>();

        // ── Per-DailyTick 统计 ──
        private static readonly Dictionary<string, int> _blockedCounts = new Dictionary<string, int>();
        private static readonly Dictionary<string, List<string>> _blockedExamples = new Dictionary<string, List<string>>();
        private static readonly Dictionary<string, List<string>> _passedExamples = new Dictionary<string, List<string>>();
        private static float _lastLogDay = -1f;

        static IssueFilterBehavior()
        {
            LoadBlockingTable();
        }

        private static void LoadBlockingTable()
        {
            // ── BanditRaid（匪患劫掠）：村庄正被劫掠，不应发布日常经营类委托 ──
            _blockedTypeNames[WorldEventType.BanditRaid] = new HashSet<string>
            {
                "HeadmanNeedsGrainIssue",
                "VillageNeedsToolsIssue",
                "HeadmanNeedsToDeliverAHerdIssue",
                "HeadmanVillageNeedsDraughtAnimalsIssue",
                "VillageNeedsCraftingMaterialsIssue",
                "LandlordNeedsAccessToVillageCommonsIssue",
                "LandLordNeedsManualLaborersIssue",
                "LandLordTheArtOfTheTradeIssue",
                "LandlordTrainingForRetainersIssue",
                "ArtisanCantSellProductsAtAFairPriceIssue",
                "ArtisanOverpricedGoodsIssue",
            };

            // ── NobleConflict（贵族冲突）：领主正集结军队 ──
            _blockedTypeNames[WorldEventType.NobleConflict] = new HashSet<string>
            {
                "LordNeedsHorsesIssue",
                "LordsNeedsTutorIssue",
                "LadysKnightOutIssue",
                "ProdigalSonIssue",
            };

            // ── Famine（饥荒）：村庄缺粮 ──
            _blockedTypeNames[WorldEventType.Famine] = new HashSet<string>
            {
                "HeadmanNeedsToDeliverAHerdIssue",
                "LandlordTrainingForRetainersIssue",
                "BettingFraudIssue",
            };
        }

        /// <summary>
        /// 查询某事件类型是否阻止某类 Issue（按 Type 对象匹配其 Name）。
        /// </summary>
        public static bool TryGetBlockedTypes(WorldEventType eventType, out HashSet<string> blockedTypeNames)
        {
            return _blockedTypeNames.TryGetValue(eventType, out blockedTypeNames);
        }

        /// <summary>
        /// 检查给定的 issueType 是否在阻止列表中。
        /// </summary>
        public static bool IsIssueTypeBlocked(WorldEventType eventType, Type issueType)
        {
            if (issueType == null) return false;
            if (_blockedTypeNames.TryGetValue(eventType, out var blockedNames))
            {
                return blockedNames.Contains(issueType.Name);
            }
            return false;
        }

        public static void RecordBlockedIssue(WorldEventType eventType, Hero hero, Type issueType)
        {
            string key = $"{eventType}@{hero?.CurrentSettlement?.Name?.ToString() ?? hero?.HomeSettlement?.Name?.ToString() ?? "?"}";
            lock (_blockedCounts)
            {
                if (!_blockedCounts.ContainsKey(key)) _blockedCounts[key] = 0;
                _blockedCounts[key]++;

                if (!_blockedExamples.ContainsKey(key))
                    _blockedExamples[key] = new List<string>();
                if (_blockedExamples[key].Count < 5)
                    _blockedExamples[key].Add(issueType?.Name ?? "?");
            }
        }

        public static void RecordPassedIssue(WorldEventType eventType, Hero hero, Type issueType)
        {
            string key = $"{eventType}@{hero?.CurrentSettlement?.Name?.ToString() ?? hero?.HomeSettlement?.Name?.ToString() ?? "?"}";
            lock (_blockedCounts)
            {
                if (!_passedExamples.ContainsKey(key))
                    _passedExamples[key] = new List<string>();
                if (_passedExamples[key].Count < 3)
                    _passedExamples[key].Add(issueType?.Name ?? "?");
            }
        }

        // ── CampaignBehaviorBase ──

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnDailyTick()
        {
            float currentDay = (float)CampaignTime.Now.ToDays;
            if (_lastLogDay < 0) _lastLogDay = currentDay;

            lock (_blockedCounts)
            {
                if (_blockedCounts.Count == 0 && _passedExamples.Count == 0) return;

                var allKeys = new HashSet<string>(_blockedCounts.Keys);
                foreach (var key in _passedExamples.Keys) allKeys.Add(key);

                foreach (var key in allKeys)
                {
                    _blockedCounts.TryGetValue(key, out int blocked);
                    _blockedExamples.TryGetValue(key, out var blockedList);
                    _passedExamples.TryGetValue(key, out var passedList);

                    if (blocked > 0)
                    {
                        string blockedStr = blockedList != null && blockedList.Count > 0
                            ? string.Join(", ", blockedList.Take(3).Select(n => n.Replace("Issue", "")))
                            : "?";
                        string passedStr = passedList != null && passedList.Count > 0
                            ? string.Join(", ", passedList.Take(3).Select(n => n.Replace("Issue", "")))
                            : "无";
                        DebugLogger.Log($"[QuestFilter] {key}: 阻止了 {blocked} 种日常Issue（{blockedStr}），保留了 {passedStr}");
                    }
                }

                _blockedCounts.Clear();
                _blockedExamples.Clear();
                _passedExamples.Clear();
            }

            _lastLogDay = currentDay;
        }
    }
}
