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
        /// Mapping: EventType → Issue type NAMES that are BLOCKED during this event.
        /// </summary>
        private static readonly Dictionary<EventType, HashSet<string>> _blockedTypeNames =
            new Dictionary<EventType, HashSet<string>>();

        /// <summary>
        /// 犯罪事件（Theft_Animal/Theft_Pickpocket/Murder/Poaching/Smuggling/Arson）共用的拦截表。
        /// 村庄出了案子 → 村长 / 权威 NPC 不应再发日常经营委托。
        /// </summary>
        private static HashSet<string> _crimeBlockedIssueTypes;

        // ── Per-DailyTick 统计 ──
        private static readonly Dictionary<string, int> _blockedCounts = new Dictionary<string, int>();
        private static readonly Dictionary<string, List<string>> _blockedExamples = new Dictionary<string, List<string>>();
        private static readonly Dictionary<string, List<string>> _passedExamples = new Dictionary<string, List<string>>();
        private static float _lastLogDay = -1f;

        // ── 结构化 Issue 抑制表（因果链 Suppress action 写入，IssueFilterPatch 读取）──
        // Key: "{hero.StringId}|{issueTypeName}"  Value: 过期日期（CampaignTime.ToDays）
        private static readonly Dictionary<string, float> _activeSuppressions = new Dictionary<string, float>();

        static IssueFilterBehavior()
        {
            LoadBlockingTable();
        }

        private static void LoadBlockingTable()
        {
            // ── BanditRaid（匪患劫掠）：村庄正被劫掠，不应发布日常经营类委托 ──
            _blockedTypeNames[EventType.BanditRaid] = new HashSet<string>
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
            _blockedTypeNames[EventType.NobleConflict] = new HashSet<string>
            {
                "LordNeedsHorsesIssue",
                "LordsNeedsTutorIssue",
                "LadysKnightOutIssue",
                "ProdigalSonIssue",
            };

            // ── Famine（饥荒）：村庄缺粮 ──
            _blockedTypeNames[EventType.Famine] = new HashSet<string>
            {
                "HeadmanNeedsToDeliverAHerdIssue",
                "LandlordTrainingForRetainersIssue",
                "BettingFraudIssue",
            };

            // ── Crime（所有犯罪事件通用）：村庄有案件时不应发布日常经营委托 ──
            _crimeBlockedIssueTypes = new HashSet<string>
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
        }

        /// <summary>
        /// 查询某事件类型是否阻止某类 Issue（按 Type 对象匹配其 Name）。
        /// </summary>
        public static bool TryGetBlockedTypes(EventType eventType, out HashSet<string> blockedTypeNames)
        {
            return _blockedTypeNames.TryGetValue(eventType, out blockedTypeNames);
        }

        /// <summary>
        /// 检查给定的 issueType 是否在阻止列表中。
        /// </summary>
        public static bool IsIssueTypeBlocked(EventType eventType, Type issueType)
        {
            if (issueType == null) return false;
            if (_blockedTypeNames.TryGetValue(eventType, out var blockedNames))
            {
                return blockedNames.Contains(issueType.Name);
            }
            return false;
        }

        /// <summary>
        /// 检查给定的 issueType 是否属于犯罪事件应拦截的日常经营类 Issue。
        /// 由 IssueFilterPatch 在 WorldEventStore 有活跃犯罪事件时调用。
        /// </summary>
        public static bool IsBlockedForCrimeEvent(Type issueType)
        {
            if (issueType == null || _crimeBlockedIssueTypes == null) return false;
            return _crimeBlockedIssueTypes.Contains(issueType.Name);
        }

        public static void RecordBlockedIssue(EventType eventType, Hero hero, Type issueType)
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

        public static void RecordPassedIssue(EventType eventType, Hero hero, Type issueType)
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

        // ── 结构化 Issue 抑制（因果链 Suppress action 使用）──

        /// <summary>
        /// 注册一个 Issue 类型抑制。由 QuestConsequenceResolver.ExecuteSuppress 调用。
        /// </summary>
        /// <param name="hero">要抑制的 NPC</param>
        /// <param name="issueTypeName">Issue 类型名（如 "ExtortionByDesertersIssue"）</param>
        /// <param name="durationDays">抑制持续天数</param>
        public static void RegisterSuppression(Hero hero, string issueTypeName, int durationDays)
        {
            if (hero == null || string.IsNullOrEmpty(issueTypeName)) return;

            string key = MakeSuppressionKey(hero, issueTypeName);
            float expiryDay = (float)CampaignTime.Now.ToDays + durationDays;

            lock (_activeSuppressions)
            {
                _activeSuppressions[key] = expiryDay;
            }
        }

        /// <summary>
        /// 检查某个 Issue 类型是否被 Suppress 抑制。
        /// 由 IssueFilterPatch.Prefix 在 AddPotentialIssueData 前调用。
        /// </summary>
        /// <param name="hero">被检查的 NPC</param>
        /// <param name="issueType">Issue 类型</param>
        /// <returns>true = 该 Issue 被抑制，应拦截</returns>
        public static bool IsIssueSuppressed(Hero hero, Type issueType)
        {
            if (hero == null || issueType == null) return false;

            string key = MakeSuppressionKey(hero, issueType.Name);
            float nowDay = (float)CampaignTime.Now.ToDays;

            lock (_activeSuppressions)
            {
                if (_activeSuppressions.TryGetValue(key, out float expiry))
                {
                    if (nowDay < expiry) return true;    // 仍在抑制期内
                    _activeSuppressions.Remove(key);      // 已过期，清理
                }
            }
            return false;
        }

        private static string MakeSuppressionKey(Hero hero, string issueTypeName)
            => $"{hero.StringId}|{issueTypeName}";

        // ── Persistence ──

        /// <summary>序列化活跃的 Issue 抑制表供存档。</summary>
        public static string Serialize()
        {
            try
            {
                var data = new Dictionary<string, object>
                {
                    { "suppressions", _activeSuppressions },
                    { "lastLogDay", _lastLogDay }
                };
                return Newtonsoft.Json.JsonConvert.SerializeObject(data);
            }
            catch { return "{}"; }
        }

        /// <summary>从存档恢复 Issue 抑制表。</summary>
        public static void Deserialize(string json)
        {
            lock (_activeSuppressions)
            {
                _activeSuppressions.Clear();
                _lastLogDay = -1f;

                if (string.IsNullOrEmpty(json) || json == "{}") return;

                try
                {
                    var dict = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    if (dict == null) return;

                    if (dict.TryGetValue("suppressions", out var supp) && supp != null)
                    {
                        var restored = Newtonsoft.Json.JsonConvert
                            .DeserializeObject<Dictionary<string, float>>(supp.ToString());
                        if (restored != null)
                        {
                            foreach (var kvp in restored)
                                _activeSuppressions[kvp.Key] = kvp.Value;
                        }
                    }

                    if (dict.TryGetValue("lastLogDay", out var lld) && lld != null)
                        _lastLogDay = Convert.ToSingle(lld);
                }
                catch { /* keep clean state */ }
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

            // 清理过期的 Suppress 条目
            CleanExpiredSuppressions(currentDay);

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

        /// <summary>
        /// DailyTick 时清理已过期的 Suppress 条目。
        /// </summary>
        private static void CleanExpiredSuppressions(float currentDay)
        {
            lock (_activeSuppressions)
            {
                var expired = new List<string>();
                foreach (var kvp in _activeSuppressions)
                {
                    if (currentDay >= kvp.Value)
                        expired.Add(kvp.Key);
                }
                foreach (var key in expired)
                    _activeSuppressions.Remove(key);
            }
        }
    }
}
