using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace LivingWorldNpcs
{
    /// <summary>世界事件类型。</summary>
    public enum WorldEventType
    {
        BanditRaid,         // 匪患 — 匪帮向村庄移动，集体恐惧
        Kidnapping,         // 绑架 — 一人被绑，个体焦急
        Famine,             // 饥荒 — 粮食耗尽，集体绝望（天灾）
        Betrayal,           // 背叛 — 被自己人捅刀
        DebtTrap,           // 债务陷阱 — 被制度压榨
        RomanticConflict,   // 情仇 — 爱恨交织
        FalseAccusation,    // 冤案 — 正义感驱动
        InheritanceDispute, // 继承争端 — 合法性之争
        Fugitive,           // 逃犯/隐士 — 道德灰色带
        TradeDispute,       // 贸易争端 — 商人的战争
        NobleConflict,      // 贵族冲突 — 领主之间的尊严游戏
        SacredTheft,        // 圣物失窃 — 身份/传承被盗
        Assassination,      // 行刺 — 关键人物被清除
        NemesisRevenge,     // 宿敌复仇 — 私人恩怨
    }

    /// <summary>世界事件状态。</summary>
    public enum WorldEventStatus
    {
        Active,
        Resolved,   // 玩家或 AI 解决了事件
        Expired,    // 到期未解决，加害方达成目标
        Escalated   // 严重度升级
    }

    /// <summary>
    /// 世界事件叙事阶段 — 决定文案该用"正在发生"还是"已经发生"的时态。
    /// </summary>
    public enum WorldEventPhase
    {
        /// <summary>事件仍在进行中，后果尚未发生（party 行军 / instigator 准备中）。文案应用将来/进行时。</summary>
        Impending,
        /// <summary>事件后果已施加（到期未解决 / 部队已到达 / 已解决）。文案可用过去时。</summary>
        Consummated
    }

    /// <summary>
    /// 世界事件数据模型。所有字段可 null 兼容旧档。
    /// JSON 序列化走 Newtonsoft.Json。
    /// </summary>
    [Serializable]
    public class WorldEventData
    {
        /// <summary>唯一事件 ID（GUID 短串）。</summary>
        public string EventId;

        /// <summary>事件类型。</summary>
        public WorldEventType EventType;

        /// <summary>当前状态。</summary>
        public WorldEventStatus Status;

        // ── 角色 ──

        /// <summary>受害者 Hero.StringId（受影响定居点的名人/头人）。</summary>
        public string TargetHeroId;

        /// <summary>受影响定居点 Settlement.StringId。</summary>
        public string TargetSettlementId;

        /// <summary>加害方 Hero.StringId；可为 null（通用匪帮）。</summary>
        public string InstigatorHeroId;

        /// <summary>加害方是否为通用模板（无真实 Hero）。</summary>
        public bool IsGenericInstigator;

        // ── 大地图 ──

        /// <summary>生成的事件 MobileParty.StringId。</summary>
        public string GeneratedPartyId;

        // ── 时间与强度 ──

        /// <summary>创建时的 CampaignTime.Days。</summary>
        public float CreatedDay;

        /// <summary>距创建日多少天后到期（3-15）。</summary>
        public float DayLimit;

        /// <summary>严重度 1-10。</summary>
        public int Severity;

        /// <summary>已升级次数（用于计算下次升级时间）。</summary>
        public int EscalationCount;

        // ── 跨事件机制（后续阶段）──

        /// <summary>背后是否有人操纵。</summary>
        public bool HasHiddenMastermind;

        /// <summary>幕后黑手 Hero.StringId（可选）。</summary>
        public string HiddenMastermindId;

        /// <summary>同一阴谋的多个事件共享此 ID。</summary>
        public string ConspiracyId;

        /// <summary>生成的 party 是否为征用的真人现有部队（而非我们新建的）。征用部队在事件结束时不能删除。</summary>
        public bool IsRedirectedExistingParty;

        // ── 辅助方法 ──

        /// <summary>到期日（创建日 + 时限）。</summary>
        [JsonIgnore]
        public float ExpiryDay => CreatedDay + DayLimit;

        /// <summary>是否已过期。</summary>
        [JsonIgnore]
        public bool IsExpired => Campaign.Current != null &&
            (float)CampaignTime.Now.ToDays > ExpiryDay;

        /// <summary>获取事件叙事阶段 — 决定文案该用"正在/即将"还是"已经"的时态。</summary>
        [JsonIgnore]
        public WorldEventPhase Phase
        {
            get
            {
                // 已过期 / 已解决 → 已成事实
                if (Status == WorldEventStatus.Expired || Status == WorldEventStatus.Resolved)
                    return WorldEventPhase.Consummated;

                // Active / Escalated：检查 party 是否已到达目标
                var party = GeneratedParty;
                var settlement = TargetSettlement;
                if (party != null && settlement != null && party.IsActive
                    && party.Position2D.Distance(settlement.Position2D) < 3f)
                    return WorldEventPhase.Consummated;

                // 仍在行军 / 准备中
                return WorldEventPhase.Impending;
            }
        }

        /// <summary>获取受害者 Hero（可能为 null）。</summary>
        [JsonIgnore]
        public Hero TargetHero => string.IsNullOrEmpty(TargetHeroId)
            ? null : Hero.FindFirst(h => h.StringId == TargetHeroId);

        /// <summary>获取加害方 Hero（可能为 null）。</summary>
        [JsonIgnore]
        public Hero InstigatorHero => string.IsNullOrEmpty(InstigatorHeroId)
            ? null : Hero.FindFirst(h => h.StringId == InstigatorHeroId);

        /// <summary>获取目标定居点。</summary>
        [JsonIgnore]
        public Settlement TargetSettlement => string.IsNullOrEmpty(TargetSettlementId)
            ? null : Settlement.Find(TargetSettlementId);

        /// <summary>获取生成的 MobileParty。</summary>
        [JsonIgnore]
        public MobileParty GeneratedParty
        {
            get
            {
                if (string.IsNullOrEmpty(GeneratedPartyId)) return null;
                foreach (var mp in Campaign.Current?.MobileParties ?? Enumerable.Empty<MobileParty>())
                {
                    if (mp.StringId == GeneratedPartyId) return mp;
                }
                return null;
            }
        }
    }

    /// <summary>
    /// 世界事件数据库。单例静态类，存储所有 WorldEvent，支持 JSON 持久化。
    /// 持久化走 MyBehavior.SyncData（与 TrustSystem / InfamySystem 模式一致）。
    /// </summary>
    public static class WorldEventDatabase
    {
        private static List<WorldEventData> _activeEvents = new List<WorldEventData>();
        private static List<WorldEventData> _resolvedEvents = new List<WorldEventData>();
        private static List<WorldEventData> _expiredEvents = new List<WorldEventData>();

        /// <summary>所有活跃事件。</summary>
        public static IReadOnlyList<WorldEventData> ActiveEvents => _activeEvents.AsReadOnly();

        /// <summary>已解决事件（用于叙事线程）。</summary>
        public static IReadOnlyList<WorldEventData> ResolvedEvents => _resolvedEvents.AsReadOnly();

        /// <summary>所有事件总数。</summary>
        public static int TotalEventCount => _activeEvents.Count + _resolvedEvents.Count + _expiredEvents.Count;

        #region CRUD

        /// <summary>添加一个新事件。</summary>
        public static void AddEvent(WorldEventData evt)
        {
            if (evt == null || string.IsNullOrEmpty(evt.EventId)) return;
            evt.Status = WorldEventStatus.Active;

            // 去重检查
            if (_activeEvents.Any(e => e.EventId == evt.EventId)) return;

            _activeEvents.Add(evt);
            DebugLogger.Log($"[WorldEvent] New event: {evt.EventType} id={evt.EventId} settlement={evt.TargetSettlementId}");

            // 通知系统
            WorldEventNotificationController.OnEventCreated(evt);

            // 推送到涉事 NPC 的记忆系统
            SyncEventToNpcMemory(evt);
        }

        /// <summary>将事件标记为已解决。</summary>
        public static void ResolveEvent(string eventId)
        {
            var evt = _activeEvents.FirstOrDefault(e => e.EventId == eventId);
            if (evt == null) return;

            _activeEvents.Remove(evt);
            evt.Status = WorldEventStatus.Resolved;
            _resolvedEvents.Add(evt);

            // 清理关联的 party
            RemoveEventParty(evt);
            DebugLogger.Log($"[WorldEvent] Resolved: {evt.EventType} id={eventId}");

            // 事件被解决 → 区域稳定性提升
            if (evt.TargetSettlement != null)
                WorldEventSimulator.ModifyStability(evt.TargetSettlement, +1);

            // 通知系统
            WorldEventNotificationController.OnEventResolved(evt);

            // 清除涉事 NPC 的记忆
            ClearEventFromNpcMemory(evt);
        }
        public static void ExpireEvent(string eventId)
        {
            var evt = _activeEvents.FirstOrDefault(e => e.EventId == eventId);
            if (evt == null) return;

            _activeEvents.Remove(evt);
            evt.Status = WorldEventStatus.Expired;
            _expiredEvents.Add(evt);

            // 清理关联的 party
            RemoveEventParty(evt);
            DebugLogger.Log($"[WorldEvent] Expired: {evt.EventType} id={eventId}");

            // 清除涉事 NPC 的记忆
            ClearEventFromNpcMemory(evt);
        }

        /// <summary>将事件标记为升级。</summary>
        public static void EscalateEvent(string eventId)
        {
            var evt = _activeEvents.FirstOrDefault(e => e.EventId == eventId);
            if (evt == null) return;

            evt.Status = WorldEventStatus.Escalated;
            evt.Severity = Math.Min(10, evt.Severity + 1);
            DebugLogger.Log($"[WorldEvent] Escalated to severity {evt.Severity}: {evt.EventType} id={eventId}");

            // 通知系统
            WorldEventNotificationController.OnEventEscalated(evt);
        }

        /// <summary>清理活跃事件中已不存在的 party（被 AI 击败）或已死亡的 instigator。</summary>
        public static void CleanupDefeatedParties()
        {
            var defeated = _activeEvents
                .Where(e =>
                {
                    // party 已消失
                    if (!string.IsNullOrEmpty(e.GeneratedPartyId) && e.GeneratedParty == null)
                        return true;
                    // instigator hero 已死亡（但事件还在活跃）
                    if (!string.IsNullOrEmpty(e.InstigatorHeroId) && !e.IsGenericInstigator)
                    {
                        var hero = e.InstigatorHero;
                        if (hero != null && !hero.IsAlive) return true;
                    }
                    return false;
                })
                .ToList();

            foreach (var evt in defeated)
                ResolveEvent(evt.EventId);

            if (defeated.Count > 0)
                DebugLogger.Log($"[WorldEvent] Auto-resolved {defeated.Count} events (party defeated or instigator died)");

            // 防止 JSON 膨胀：只保留最近 50 条已解决事件
            while (_resolvedEvents.Count > 50)
                _resolvedEvents.RemoveAt(0);
            while (_expiredEvents.Count > 50)
                _expiredEvents.RemoveAt(0);
        }

        private static void RemoveEventParty(WorldEventData evt)
        {
            try
            {
                if (evt.IsRedirectedExistingParty)
                {
                    // 征用的真人部队不能删——恢复 AI 自主决策即可
                    var party = evt.GeneratedParty;
                    if (party != null)
                    {
                        party.Ai.SetDoNotMakeNewDecisions(false);
                        party.SetPartyUsedByQuest(false);
                        Campaign.Current?.VisualTrackerManager?.RemoveTrackedObject(party, forceRemove: true);
                    }
                    DebugLogger.Log($"[WorldEvent] Unlocked redirected party {evt.GeneratedPartyId} (event resolved)");
                }
                else
                {
                    var party = evt.GeneratedParty;
                    if (party != null)
                    {
                        Campaign.Current?.VisualTrackerManager?.RemoveTrackedObject(party, forceRemove: true);
                    }
                    party?.RemoveParty();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEvent] Failed to remove party {evt.GeneratedPartyId}: {ex.Message}");
            }
        }

        #endregion

        #region Query

        /// <summary>获取指定定居点附近（距离 &lt; maxDistance）的活跃事件。</summary>
        public static List<WorldEventData> GetActiveEventsNear(Settlement settlement, float maxDistance = 80f)
        {
            if (settlement == null) return new List<WorldEventData>();

            return _activeEvents
                .Where(e =>
                {
                    var target = e.TargetSettlement;
                    if (target == null) return false;
                    return target.Position2D.Distance(settlement.Position2D) < maxDistance;
                })
                .ToList();
        }

        /// <summary>按事件类型筛选活跃事件。</summary>
        public static List<WorldEventData> GetActiveEventsOfType(WorldEventType type)
        {
            return _activeEvents.Where(e => e.EventType == type).ToList();
        }

        /// <summary>按 EventId 查找事件（不限状态）。</summary>
        public static WorldEventData FindEvent(string eventId)
        {
            return _activeEvents.FirstOrDefault(e => e.EventId == eventId)
                ?? _resolvedEvents.FirstOrDefault(e => e.EventId == eventId)
                ?? _expiredEvents.FirstOrDefault(e => e.EventId == eventId);
        }

        /// <summary>获取某个 Hero 作为目标（受害者）的所有活跃事件。</summary>
        public static List<WorldEventData> GetActiveEventsForTarget(string heroStringId)
        {
            if (string.IsNullOrEmpty(heroStringId)) return new List<WorldEventData>();
            return _activeEvents.Where(e => e.TargetHeroId == heroStringId).ToList();
        }

        #endregion

        #region NPC Memory Sync

        /// <summary>
        /// 将事件推送到涉事 NPC 的 SingNpcMemorySystem.CurrentUrgentEvent。
        /// 如果 NPC 已有更严重的事件则不覆盖；否则写入。
        /// 通过 AllNpcMemoryManager.GetMemory 惰性创建记忆（若玩家从未与此 NPC 对话）。
        /// </summary>
        private static void SyncEventToNpcMemory(WorldEventData evt)
        {
            try
            {
                var heroes = new List<Hero>();
                if (evt.InstigatorHero != null) heroes.Add(evt.InstigatorHero);
                if (evt.TargetHero != null && evt.TargetHero != evt.InstigatorHero) heroes.Add(evt.TargetHero);

                foreach (var hero in heroes)
                {
                    if (string.IsNullOrEmpty(hero.StringId)) continue;
                    var mem = AllNpcMemoryManager.GetMemory(hero.StringId);
                    if (mem == null) continue;

                    // 如果 NPC 当前无事件，或新事件严重度 ≥ 当前事件 → 覆盖
                    if (mem.CurrentUrgentEvent == null || evt.Severity >= mem.CurrentUrgentEvent.Severity)
                    {
                        mem.CurrentUrgentEvent = evt;
                        DebugLogger.Log($"[WorldEvent] Synced to NPC memory: {hero.Name} ← {evt.EventType} (severity={evt.Severity}, role={(hero == evt.InstigatorHero ? "Instigator" : "Victim")})");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEvent] SyncEventToNpcMemory error: {ex.Message}");
            }
        }

        /// <summary>
        /// 从涉事 NPC 的记忆中清除指定事件。
        /// 如果 NPC 还涉及其它活跃事件，取最严重者作为新的 CurrentUrgentEvent；
        /// 否则置 null。
        /// </summary>
        private static void ClearEventFromNpcMemory(WorldEventData evt)
        {
            try
            {
                var heroes = new List<Hero>();
                if (evt.InstigatorHero != null) heroes.Add(evt.InstigatorHero);
                if (evt.TargetHero != null && evt.TargetHero != evt.InstigatorHero) heroes.Add(evt.TargetHero);

                foreach (var hero in heroes)
                {
                    if (string.IsNullOrEmpty(hero.StringId)) continue;
                    var mem = AllNpcMemoryManager.GetMemory(hero.StringId);
                    if (mem == null || mem.CurrentUrgentEvent == null) continue;

                    // 只有当 NPC 当前记忆的就是这个事件时才清理
                    if (mem.CurrentUrgentEvent.EventId == evt.EventId)
                    {
                        // 查找此 NPC 是否还涉及其它活跃事件，取最严重的
                        var remaining = _activeEvents
                            .Where(e => e.EventId != evt.EventId
                                && (e.InstigatorHeroId == hero.StringId || e.TargetHeroId == hero.StringId))
                            .OrderByDescending(e => e.Severity)
                            .FirstOrDefault();

                        mem.CurrentUrgentEvent = remaining;
                        DebugLogger.Log($"[WorldEvent] Cleared from NPC memory: {hero.Name} ← {evt.EventType} (replaced with {(remaining != null ? remaining.EventType.ToString() : "none")})");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEvent] ClearEventFromNpcMemory error: {ex.Message}");
            }
        }

        #endregion

        #region JSON Persistence

        /// <summary>序列化所有事件为 JSON（持久化到存档）。</summary>
        public static string Serialize()
        {
            try
            {
                var allEvents = new List<WorldEventData>();
                allEvents.AddRange(_activeEvents);
                allEvents.AddRange(_resolvedEvents);
                allEvents.AddRange(_expiredEvents);
                return JsonConvert.SerializeObject(allEvents, Formatting.None);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEvent] Serialize error: {ex.Message}");
                return "[]";
            }
        }

        /// <summary>从 JSON 反序列化恢复所有事件（读档时调用）。</summary>
        public static void Deserialize(string json)
        {
            _activeEvents.Clear();
            _resolvedEvents.Clear();
            _expiredEvents.Clear();

            if (string.IsNullOrEmpty(json) || json == "[]") return;

            try
            {
                var allEvents = JsonConvert.DeserializeObject<List<WorldEventData>>(json);
                if (allEvents == null) return;

                foreach (var evt in allEvents)
                {
                    if (evt == null) continue;
                    switch (evt.Status)
                    {
                        case WorldEventStatus.Active:
                            _activeEvents.Add(evt);
                            break;
                        case WorldEventStatus.Resolved:
                            _resolvedEvents.Add(evt);
                            break;
                        case WorldEventStatus.Expired:
                        case WorldEventStatus.Escalated:
                            _expiredEvents.Add(evt);
                            break;
                    }
                }

                DebugLogger.Log($"[WorldEvent] Deserialized {allEvents.Count} events (active={_activeEvents.Count}, resolved={_resolvedEvents.Count}, expired={_expiredEvents.Count})");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEvent] Deserialize error: {ex.Message}");
            }
        }

        #endregion
    }
}
