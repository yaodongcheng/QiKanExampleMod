using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 世界事件模拟器 — CampaignBehaviorBase。
    ///
    /// 每游戏日：
    ///   1. 清理已被 AI 击败的事件 party
    ///   2. 检查到期事件
    ///   3. ~10% 概率生成新的 BanditRaid 事件
    ///
    /// 生成逻辑：
    ///   选定居点 → 选受害者 → 选加害方 → 设等级 → 生成 MobileParty → 存入数据库
    ///   原版 AI 立即开始反应（领主巡逻遇敌 party → 交战）。
    /// </summary>
    public class WorldEventSimulator : CampaignBehaviorBase
    {
        private const float BASE_DAILY_PROBABILITY = 0.10f;
        private const float MIN_SEVERITY = 1f;
        private const float MAX_SEVERITY = 10f;
        private const float MIN_DAY_LIMIT = 3f;
        private const float MAX_DAY_LIMIT = 15f;
        private const int MAX_ACTIVE_EVENTS = 15;

        /// <summary>
        /// 总闸：true = 停止世界事件模拟器所有被动逻辑（自动生成、过期处理、巡逻、摘要等）。
        /// ForceGenerateEvent（控制台 / 因果引擎）不受影响。
        /// </summary>
        public static bool SuppressAutoGeneration = true;

        // ── 首次体验保障 ──
        /// <summary>自游戏开始经过的游戏日（跨存档）。</summary>
        private static float _daysSinceGameStart = 0f;
        /// <summary>已强制生成的新手引导事件数。</summary>
        private static int _tutorialEventsGenerated = 0;
        private const int MAX_TUTORIAL_EVENTS = 2;
        private const float TUTORIAL_WINDOW_DAYS = 5f; // 前 5 天为新手窗口

        #region CampaignBehaviorBase

        private float _roadInterceptAccumDt;
        private const float ROAD_INTERCEPT_INTERVAL_SEC = 2f; // 每2秒 1 次
        private float _lastPeriodicDigestDay = -1f;
        private const float PERIODIC_DIGEST_INTERVAL = 3f; // 每 3 天推送一次世界摘要
        private float _aiMonitorAccumDt;
        private const float AI_MONITOR_INTERVAL_SEC = 5f; // 每 5 秒扫一次事件 party AI
        private static readonly Dictionary<string, string> _lastPartyAiDesc = new Dictionary<string, string>(); // partyId → last known AI description
        private static readonly Dictionary<string, float> _arrivedParties = new Dictionary<string, float>(); // partyId → arrival day (patrol phase)
        private const float PATROL_DAYS_BEFORE_ATTACK = 0f; // 🐛 调试：设为 0 强制到达后立即攻城

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            // 高频路途拦截检查：每 ~1 游戏小时检查一次，而非一天一次
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnCampaignTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // 新手引导/首次体验状态（简单管道分隔序列化，跨存档）
            string simState = $"{_daysSinceGameStart}|{_tutorialEventsGenerated}";
            dataStore.SyncData("lwn_sim_state", ref simState);
            if (dataStore.IsLoading && !string.IsNullOrEmpty(simState))
            {
                try
                {
                    var parts = simState.Split('|');
                    if (parts.Length >= 2)
                    {
                        float.TryParse(parts[0], out _daysSinceGameStart);
                        int.TryParse(parts[1], out _tutorialEventsGenerated);
                    }
                }
                catch { }
            }
        }

        #endregion

        #region Tick

        /// <summary>路途拦截 + 酒馆传闻 + 事件 party AI 监控：dt 累积到指定间隔执行（不再依赖游戏时间）。</summary>
        private void OnCampaignTick(float dt)
        {
            if (SuppressAutoGeneration) return;
            try
            {
                _roadInterceptAccumDt += dt;
                if (_roadInterceptAccumDt >= ROAD_INTERCEPT_INTERVAL_SEC)
                {
                    _roadInterceptAccumDt = 0f;
                    WorldEventDirector.CheckRoadIntercept();
                    WorldEventDirector.CheckTavernAmbientTrigger();
                }

                _aiMonitorAccumDt += dt;
                if (_aiMonitorAccumDt >= AI_MONITOR_INTERVAL_SEC)
                {
                    _aiMonitorAccumDt = 0f;
                    MonitorEventPartyAiStates();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEventSimulator] OnCampaignTick error: {ex.Message}");
            }
        }

        /// <summary>每 ~5 秒扫描所有活跃事件 party 的 AI 状态，仅行为/目标变化时打印日志。</summary>
        private static void MonitorEventPartyAiStates()
        {
            try
            {
                var activeEvents = WorldEventStore.ActiveEvents;
                if (activeEvents.Count == 0) return;

                foreach (var evt in activeEvents)
                {
                    var party = evt.GeneratedParty;
                    if (party == null || !party.IsActive)
                    {
                        if (_lastPartyAiDesc.Remove(evt.GeneratedPartyId ?? evt.EventId))
                            DebugLogger.Log($"[WorldEvent AI] Party gone: eventId={evt.EventId} partyId={evt.GeneratedPartyId}");
                        continue;
                    }

                    // 🔑 变化检测只用行为+目标，不含坐标/兵力（坐标每帧在变）
                    string changeKey = BuildPartyAiChangeKey(party);
                    string key = party.StringId;
                    if (_lastPartyAiDesc.TryGetValue(key, out string lastKey) && lastKey == changeKey)
                        continue;

                    _lastPartyAiDesc[key] = changeKey;
                    string desc = BuildPartyAiDisplay(party);
                    string eventLabel = $"{evt.Type} instigator={evt.InstigatorHero?.Name?.ToString() ?? "?"} target={evt.TargetSettlement?.Name?.ToString() ?? evt.TargetHero?.Name?.ToString() ?? "?"}";
                    DebugLogger.Log($"[WorldEvent AI] {eventLabel} | {desc}");
                }

                // 定期清理已不存在的 party 记录
                var activeIds = new HashSet<string>(activeEvents.Select(e => e.GeneratedParty?.StringId).Where(s => s != null));
                var stale = _lastPartyAiDesc.Keys.Where(k => !activeIds.Contains(k)).ToList();
                foreach (var k in stale) _lastPartyAiDesc.Remove(k);

                // ── 高频到达检测：不等 daily tick，每 AI_MONITOR_INTERVAL_SEC 秒扫一次 ──
                CheckArrivalsHighFreq(activeEvents);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEventSimulator] MonitorEventPartyAiStates error: {ex.Message}");
            }
        }

        /// <summary>构建 AI 行为变化检测 key：只有行为类型和目标，不含坐标。</summary>
        private static string BuildPartyAiChangeKey(MobileParty party)
        {
            string st = party.ShortTermBehavior.ToString();
            string stTarget = party.ShortTermTargetParty?.StringId ?? "";
            string def = party.DefaultBehavior.ToString();
            string defTarget = party.TargetSettlement?.StringId
                ?? V.MoveTarget(party)?.StringId
                ?? "";
            return $"{st}|{stTarget}|{def}|{defTarget}";
        }

        /// <summary>构建 AI 行为展示描述（含坐标/兵力，仅用于日志阅读）。</summary>
        private static string BuildPartyAiDisplay(MobileParty party)
        {
            var shortTerm = party.ShortTermBehavior;
            var defaultBehavior = party.DefaultBehavior;
            string pos = $"({V.Pos(party).X:F0},{V.Pos(party).Y:F0})";
            int troops = party.MemberRoster?.TotalManCount ?? 0;

            string instant = shortTerm.ToString();
            if (shortTerm != AiBehavior.Hold && shortTerm != AiBehavior.None)
            {
                string stName = party.ShortTermTargetParty?.Name?.ToString()
                    ?? party.ShortTermTargetParty?.StringId ?? "";
                if (!string.IsNullOrEmpty(stName)) instant = $"{shortTerm}→{stName}";
            }

            string goal = defaultBehavior.ToString();
            string defTarget = party.TargetSettlement?.Name?.ToString()
                ?? V.MoveTarget(party)?.Name?.ToString() ?? "";
            if (!string.IsNullOrEmpty(defTarget)) goal = $"{defaultBehavior}→{defTarget}";

            return $"shortTerm={instant} default={goal} pos={pos} troops={troops}";
        }

        #endregion

        #region Daily Tick

        private void OnDailyTick()
        {
            if (SuppressAutoGeneration) return;
            try
            {
                _daysSinceGameStart += 1f;

                // 1. 清理已被 AI 击败的事件 party
                WorldEventStore.CleanupDefeatedParties();

                // 1.5. 检查事件 party 是否已到达目标 → 进入巡逻阶段
                CheckEventPartyArrivals();

                // 1.6. 巡逻结束 → 释放 AI，真正开打
                CheckPatrolCompleteAndLaunchAttack();

                // 2. 检查事件升级（每 7 天未解决 → severity+1）
                CheckEventEscalation();

                // 3. 检查到期事件并施加后果
                CheckExpiredEventsWithConsequences();

                // 4. 检查宿敌复仇
                HeroNemesisTracker.CheckAndTriggerRevenge();

                // 5. 区域稳定性衰减
                StabilityDailyDecay();

                // 6. 尝试生成新事件（临时开关控制）
                if (!SuppressAutoGeneration)
                    TryGenerateNewEvent();

                // ── 7. 新手窗口强制事件保障 ──
                if (!SuppressAutoGeneration
                    && _tutorialEventsGenerated < MAX_TUTORIAL_EVENTS
                    && _daysSinceGameStart <= TUTORIAL_WINDOW_DAYS
                    && WorldEventStore.ActiveEvents.Count < 3)
                {
                    ForceGenerateTutorialEvent();
                }

                // ── 8. 定期世界摘要推送 ──
                if (_daysSinceGameStart - _lastPeriodicDigestDay >= PERIODIC_DIGEST_INTERVAL
                    && WorldEventStore.ActiveEvents.Count > 0)
                {
                    _lastPeriodicDigestDay = _daysSinceGameStart;
                    WorldEventDirector.ShowPeriodicDigest();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEventSimulator] DailyTick error: {ex.Message}");
            }
        }

        /// <summary>每 7 天未解决 → 事件升级（severity+1，party 扩军，导演推送）。</summary>
        private void CheckEventEscalation()
        {
            float currentDay = (float)CampaignTime.Now.ToDays;
            var toEscalate = WorldEventStore.ActiveEvents
                .Where(e =>
                {
                    float daysSinceCreation = currentDay - e.OccurredDay;
                    int expectedEscalations = (int)(daysSinceCreation / 7f);
                    return expectedEscalations > e.EscalationCount && e.Severity < 10;
                })
                .ToList();

            foreach (var evt in toEscalate)
            {
                evt.EscalationCount++;
                evt.Severity = Math.Min(100, evt.Severity + 10);

                // 事件升级仅提高严重度（影响后果严重性 + 通知频率），
                // 不再凭空添加兵力——party 的部队来自领主驻军抽调，真实性优先。
                // 若 party 已在途中被 AI 打残，那是世界自然演化的结果。

                WorldEventStore.EscalateEvent(evt.EventId);
                DebugLogger.Log($"[WorldEventSimulator] Escalated {evt.Type} id={evt.EventId} to severity {evt.Severity}");
            }
        }

        /// <summary>
        /// 检查事件 party 是否已到达目标定居点。
        /// 到达 → 进入巡逻阶段（围城），1 天后释放 AI 真正开打。
        /// </summary>
        private void CheckEventPartyArrivals()
        {
            float currentDay = (float)CampaignTime.Now.ToDays;
            var arrived = WorldEventStore.ActiveEvents
                .Where(e => e.GeneratedParty != null
                    && e.GeneratedParty.IsActive
                    && e.TargetSettlement != null
                    && V.Pos(e.GeneratedParty).Distance(V.Pos(e.TargetSettlement)) < 3f) // 3 单位 ≈ 已在门口
                .ToList();

            foreach (var evt in arrived)
            {
                var party = evt.GeneratedParty;
                string partyId = party.StringId;

                // 已在巡逻阶段 → 跳过
                if (_arrivedParties.ContainsKey(partyId)) continue;

                // 刚到达：记录时间，切换为巡逻 Action（原生 API + true 防拐跑）
                _arrivedParties[partyId] = currentDay;
                V.PatrolAround(party, evt.TargetSettlement);
                party.Ai.SetDoNotMakeNewDecisions(true);

                string loc = evt.TargetSettlement?.Name?.ToString() ?? "目标";
                DebugLogger.Log($"[WorldEventSimulator] Party arrived at {loc}, entering patrol phase: {evt.Type} partyId={partyId} — will attack in ~{PATROL_DAYS_BEFORE_ATTACK} day(s)");

                // ── 通知玩家：部队已到达 ──
                float dist = V.Pos(MobileParty.MainParty).Distance(V.Pos(evt.TargetSettlement));
                if (dist < 100f)
                {
                    string arrivalSummary = BuildArrivalSummary(evt);
                    string fullNarrative = NotificationPipeline.BuildEventNarrativePublic(evt);
                    DebugLogger.Log($"[Player] NinjaReport(arrival): {arrivalSummary}");
                    NinjaNotificationManager.Show(arrivalSummary, () =>
                    {
                        WorldEventNotificationController.ShowEventInquiry(evt,
                            // 部队到达弹窗正文：⚔ 部队已到达！
                            LWNTextHelper.ResolveCompound("LWN_simulator_arrival_inquiry", ("NARRATIVE", fullNarrative)));
                    });
                }
            }
        }

        /// <summary>
        /// 高频到达检测（每 AI_MONITOR_INTERVAL_SEC 秒，不等 daily tick）。
        /// 仅记录到达状态 + 切换巡逻 AI，不发玩家通知（通知在 daily tick 的 CheckEventPartyArrivals 里统一发）。
        /// </summary>
        private static void CheckArrivalsHighFreq(IReadOnlyList<WorldEvent> activeEvents)
        {
            try
            {
                float currentDay = (float)CampaignTime.Now.ToDays;
                foreach (var evt in activeEvents)
                {
                    var party = evt.GeneratedParty;
                    if (party == null || !party.IsActive) continue;
                    if (evt.TargetSettlement == null) continue;
                    if (V.Pos(party).Distance(V.Pos(evt.TargetSettlement)) >= 3f) continue;

                    string partyId = party.StringId;
                    if (_arrivedParties.ContainsKey(partyId)) continue; // 已在巡逻

                    _arrivedParties[partyId] = currentDay;
                    V.PatrolAround(party, evt.TargetSettlement);
                    party.Ai.SetDoNotMakeNewDecisions(true);

                    DebugLogger.Log($"[WorldEventSimulator] High-freq arrival: {evt.Type} partyId={partyId} at {evt.TargetSettlement.Name} — patrol phase, attack in ~{PATROL_DAYS_BEFORE_ATTACK} day(s)");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEventSimulator] CheckArrivalsHighFreq error: {ex.Message}");
            }
        }

        /// <summary>巡逻阶段结束 → 调用原生 SetPartyAiAction 发动真正的劫掠/攻城。</summary>
        private void CheckPatrolCompleteAndLaunchAttack()
        {
            float currentDay = (float)CampaignTime.Now.ToDays;
            var readyToAttack = new List<string>();

            foreach (var kvp in _arrivedParties)
            {
                if (currentDay >= kvp.Value + PATROL_DAYS_BEFORE_ATTACK)
                    readyToAttack.Add(kvp.Key);
            }

            foreach (var partyId in readyToAttack)
            {
                _arrivedParties.Remove(partyId);

                var evt = WorldEventStore.ActiveEvents.FirstOrDefault(e => e.GeneratedParty?.StringId == partyId);
                var party = evt?.GeneratedParty;
                var target = evt?.TargetSettlement;
                if (party == null || !party.IsActive || target == null) continue;

                // ── 🐛 调试：兵力阈值临时放宽至 5%，强制允许攻城测试 ──
                int myTroops = party.MemberRoster?.TotalManCount ?? 0;
                int targetDefense = GetSettlementDefenseStrength(target);
                if (targetDefense > 0 && myTroops < targetDefense * 0.05f)
                {
                    _arrivedParties[partyId] = currentDay; // 重置巡逻计时器
                    DebugLogger.Log($"[WorldEventSimulator] Attack delayed — {evt.Type} partyId={partyId} has {myTroops} troops vs {targetDefense} defense (<30%), extending patrol");
                    continue;
                }

                // 目标名兜底：目标
                string loc = target.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_simulator_fallback_target", "the target");
                string actionName;

                // 按定居点类型选择原生 AI Action（全部搭配 true 防拐跑）
                if (target.IsVillage)
                {
                    V.RaidSettlement(party, target);
                    actionName = "RaidSettlement";
                }
                else if (target.IsFortification)
                {
                    V.BesiegeSettlement(party, target);
                    actionName = "BesiegeSettlement";
                }
                else
                {
                    V.RaidSettlement(party, target);
                    actionName = "RaidSettlement(fallback)";
                }
                party.Ai.SetDoNotMakeNewDecisions(true);

                DebugLogger.Log($"[WorldEventSimulator] Patrol complete — launched {actionName}: {evt.Type} partyId={partyId} → {loc}");

                // 通知玩家：进攻开始
                float dist = V.Pos(MobileParty.MainParty).Distance(V.Pos(target));
                if (dist < 100f)
                {
                    string attackMsg = evt.Type switch
                    {
                        // 进攻通知：贵族冲突
                        EventType.NobleConflict => LWNTextHelper.ResolveCompound("LWN_simulator_attack_nobleconflict",
                            // 加害方名兜底：一伙人
                            ("NAME", evt.InstigatorHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_simulator_fallback_army", "an army")), ("LOC", loc)),
                        // 进攻通知：匪患
                        EventType.BanditRaid => LWNTextHelper.ResolveCompound("LWN_simulator_attack_banditraid", ("LOC", loc)),
                        // 进攻通知兜底
                        _ => LWNTextHelper.ResolveCompound("LWN_simulator_attack_default", ("LOC", loc))
                    };
                    NinjaNotificationManager.Show(attackMsg, () =>
                    {
                        WorldEventNotificationController.ShowEventInquiry(evt,
                            // 进攻开始弹窗正文：⚔ 进攻开始！
                            LWNTextHelper.ResolveCompound("LWN_simulator_attack_inquiry", ("NARRATIVE", NotificationPipeline.BuildEventNarrativePublic(evt))));
                    });
                }
            }
        }

        /// <summary>构建到达通知摘要（TK5 忍者通报风格）。</summary>
        private static string BuildArrivalSummary(WorldEvent e)
        {
            // 地点名兜底：某地
            string loc = e.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_simulator_fallback_place", "Somewhere");
            // 加害方名兜底：一伙歹徒（通用）/ 加害方
            string instigator = e.IsGenericInstigator ? LWNTextHelper.ResolveText("LWN_simulator_generic_gang", "a gang of ruffians")
                // 加害方名兜底：加害方
                : (e.InstigatorHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_simulator_fallback_instigator", "the instigator"));
            string victim = e.TargetHero?.Name?.ToString() ?? loc;

            return e.Type switch
            {
                // 到达摘要：匪患
                EventType.BanditRaid => LWNTextHelper.ResolveCompound("LWN_simulator_arrival_banditraid", ("INSTIGATOR", instigator), ("LOC", loc)),
                // 到达摘要：绑架
                EventType.Kidnapping => LWNTextHelper.ResolveCompound("LWN_simulator_arrival_kidnapping", ("INSTIGATOR", instigator), ("VICTIM", victim)),
                // 到达摘要：贵族冲突
                EventType.NobleConflict => LWNTextHelper.ResolveCompound("LWN_simulator_arrival_nobleconflict", ("INSTIGATOR", instigator), ("LOC", loc), ("VICTIM", victim)),
                // 到达摘要：暗杀
                EventType.Assassination => LWNTextHelper.ResolveCompound("LWN_simulator_arrival_assassination", ("VICTIM", victim), ("INSTIGATOR", instigator), ("LOC", loc)),
                // 到达摘要：圣物失窃
                EventType.SacredTheft => LWNTextHelper.ResolveCompound("LWN_simulator_arrival_sacredtheft", ("INSTIGATOR", instigator), ("LOC", loc)),
                // 到达摘要：背叛
                EventType.Betrayal => LWNTextHelper.ResolveCompound("LWN_simulator_arrival_betrayal", ("INSTIGATOR", instigator), ("VICTIM", victim)),
                // 到达摘要：饥荒
                EventType.Famine => LWNTextHelper.ResolveCompound("LWN_simulator_arrival_famine", ("LOC", loc)),
                // 到达摘要兜底
                _ => LWNTextHelper.ResolveCompound("LWN_simulator_arrival_default", ("INSTIGATOR", instigator), ("LOC", loc))
            };
        }

        /// <summary>检查到期事件，施加各类型的物理后果。</summary>
        private void CheckExpiredEventsWithConsequences()
        {
            var expired = WorldEventStore.ActiveEvents
                .Where(e => e.IsExpired)
                .ToList();

            foreach (var evt in expired)
            {
                ApplyExpiryConsequences(evt);
                WorldEventStore.ExpireEvent(evt.EventId);
            }
        }

        /// <summary>到期事件的物理后果（玩家未解决 = 加害方达成目标）+ 玩家通知。</summary>
        private void ApplyExpiryConsequences(WorldEvent evt, bool isArrival = false)
        {
            if (evt == null) return;

            Settlement settlement = evt.TargetSettlement;
            Hero targetHero = evt.TargetHero;
            // 地点名兜底：某地
            string loc = settlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_simulator_fallback_place", "Somewhere");
            // 受害者名兜底：村民
            string victim = targetHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_simulator_fallback_villager", "a villager");
            // 加害方名兜底：一伙歹徒（通用）/ 加害方
            string instigator = evt.IsGenericInstigator ? LWNTextHelper.ResolveText("LWN_simulator_generic_gang", "a gang of ruffians")
                // 加害方名兜底：加害方
                : (evt.InstigatorHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_simulator_fallback_instigator", "the instigator"));

            string playerMsg = null;

            switch (evt.Type)
            {
                case EventType.BanditRaid:
                    if (settlement?.Village != null)
                    {
                        settlement.Village.Hearth = Math.Max(0, settlement.Village.Hearth - 30);
                        playerMsg = isArrival
                            // 过期后果：匪患（部队抵达时）
                            ? LWNTextHelper.ResolveCompound("LWN_simulator_expiry_banditraid_arrival", ("INSTIGATOR", instigator), ("LOC", loc))
                            // 过期后果：匪患（劫掠已成）
                            : LWNTextHelper.ResolveCompound("LWN_simulator_expiry_banditraid", ("INSTIGATOR", instigator), ("LOC", loc));
                    }
                    break;

                case EventType.Kidnapping:
                    if (targetHero != null && targetHero.IsAlive && !targetHero.IsLord)
                    {
                        // 人质名兜底：人质
                        string name = targetHero.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_simulator_fallback_hostage", "the hostage");
                        KillCharacterAction.ApplyByMurder(null, targetHero, true);
                        playerMsg = isArrival
                            // 过期后果：绑架（绑走时）
                            ? LWNTextHelper.ResolveCompound("LWN_simulator_expiry_kidnapping_arrival", ("INSTIGATOR", instigator), ("NAME", name), ("VICTIM", victim))
                            // 过期后果：绑架（撕票）
                            : LWNTextHelper.ResolveCompound("LWN_simulator_expiry_kidnapping", ("NAME", name), ("LOC", loc));
                    }
                    break;

                case EventType.Betrayal:
                    if (evt.InstigatorHero != null && targetHero != null)
                    {
                        int stolen = targetHero.Gold / 2;
                        AgentControlHelper.TransferGold(targetHero, evt.InstigatorHero, stolen);
                        // 过期后果：背叛
                        playerMsg = LWNTextHelper.ResolveCompound("LWN_simulator_expiry_betrayal", ("VICTIM", victim), ("INSTIGATOR", instigator));
                    }
                    break;

                case EventType.DebtTrap:
                    if (targetHero != null)
                    {
                        AgentControlHelper.TransferGold(targetHero, null, targetHero.Gold / 3);
                        // 过期后果：债务陷阱
                        playerMsg = LWNTextHelper.ResolveCompound("LWN_simulator_expiry_debttrap", ("VICTIM", victim), ("INSTIGATOR", instigator));
                    }
                    break;

                case EventType.Famine:
                    if (settlement?.Village != null)
                    {
                        settlement.Village.Hearth = Math.Max(0, settlement.Village.Hearth - 50);
                        // 过期后果：饥荒
                        playerMsg = LWNTextHelper.ResolveCompound("LWN_simulator_expiry_famine", ("LOC", loc));
                    }
                    break;

                case EventType.Assassination:
                    if (targetHero != null && targetHero.IsAlive)
                    {
                        // 重要人物名兜底：重要人物
                        string name = targetHero.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_simulator_fallback_vip", "an important person");
                        KillCharacterAction.ApplyByMurder(null, targetHero, true);
                        playerMsg = isArrival
                            // 过期后果：暗杀（刺客得手时）
                            ? LWNTextHelper.ResolveCompound("LWN_simulator_expiry_assassination_arrival", ("NAME", name), ("INSTIGATOR", instigator), ("LOC", loc))
                            // 过期后果：暗杀（已成定局）
                            : LWNTextHelper.ResolveCompound("LWN_simulator_expiry_assassination", ("NAME", name), ("LOC", loc));
                    }
                    break;

                case EventType.SacredTheft:
                    if (settlement != null)
                    {
                        SettlementHonorStore.Modify(settlement, -5);
                        playerMsg = isArrival
                            // 过期后果：圣物失窃（被带走时）
                            ? LWNTextHelper.ResolveCompound("LWN_simulator_expiry_sacredtheft_arrival", ("INSTIGATOR", instigator), ("LOC", loc))
                            // 过期后果：圣物失窃（未能追回）
                            : LWNTextHelper.ResolveCompound("LWN_simulator_expiry_sacredtheft", ("LOC", loc));
                    }
                    break;

                case EventType.NobleConflict:
                    if (evt.InstigatorHero != null && targetHero != null)
                    {
                        ChangeRelationAction.ApplyPlayerRelation(evt.InstigatorHero, -10);
                        ChangeRelationAction.ApplyPlayerRelation(targetHero, -10);
                        playerMsg = isArrival
                            // 过期后果：贵族冲突（军队开到时）
                            ? LWNTextHelper.ResolveCompound("LWN_simulator_expiry_nobleconflict_arrival", ("INSTIGATOR", instigator), ("LOC", loc), ("VICTIM", victim))
                            // 过期后果：贵族冲突（兵戎相见）
                            : LWNTextHelper.ResolveCompound("LWN_simulator_expiry_nobleconflict", ("INSTIGATOR", instigator), ("VICTIM", victim), ("LOC", loc));
                    }
                    break;

                case EventType.Fugitive:
                    if (targetHero != null && targetHero.IsAlive)
                    {
                        // 逃犯名兜底：逃犯
                        string name = targetHero.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_simulator_fallback_fugitive", "the fugitive");
                        // 过期后果：逃犯下落不明
                        playerMsg = LWNTextHelper.ResolveCompound("LWN_simulator_expiry_fugitive", ("NAME", name), ("LOC", loc));
                    }
                    break;

                case EventType.RomanticConflict:
                    if (targetHero != null)
                    {
                        // 过期后果：情仇无赢家
                        playerMsg = LWNTextHelper.ResolveCompound("LWN_simulator_expiry_romantic", ("VICTIM", victim), ("LOC", loc));
                    }
                    break;

                case EventType.FalseAccusation:
                    if (targetHero != null)
                    {
                        // 过期后果：冤案成定局
                        playerMsg = LWNTextHelper.ResolveCompound("LWN_simulator_expiry_falseaccusation", ("VICTIM", victim), ("LOC", loc));
                    }
                    break;

                case EventType.InheritanceDispute:
                    // 过期后果：继承之争靠拳头解决
                    playerMsg = LWNTextHelper.ResolveCompound("LWN_simulator_expiry_inheritance", ("LOC", loc));
                    break;

                case EventType.TradeDispute:
                    // 过期后果：市场被垄断
                    playerMsg = LWNTextHelper.ResolveCompound("LWN_simulator_expiry_tradedispute", ("LOC", loc), ("INSTIGATOR", instigator));
                    break;

                default:
                    // 过期后果兜底
                    playerMsg = LWNTextHelper.ResolveCompound("LWN_simulator_expiry_default", ("LOC", loc));
                    break;
            }

            if (!string.IsNullOrEmpty(playerMsg))
            {
                InformationManager.DisplayMessage(new InformationMessage(playerMsg));
                DebugLogger.Log($"[WorldEvent] {playerMsg}");
            }

            // 事件过期后可能触发连锁反应
            TryTriggerCascade(evt);
        }

        private void CheckExpiredEvents()
        {
            // 保留旧方法作为兼容（CheckExpiredEventsWithConsequences 已替代）
            CheckExpiredEventsWithConsequences();
        }

        private void TryGenerateNewEvent()
        {
            if (WorldEventStore.ActiveEvents.Count >= MAX_ACTIVE_EVENTS)
                return;

            // ── 第一优先：真人动机驱动的事件 ──
            if (TryGenerateMotivatedEvent())
                return; // 已生成真人冲突事件，本日不再额外 roll

            // ── 第二优先：随机事件（含通用模板）──
            float roll = MBRandom.RandomFloat;
            float probability = BASE_DAILY_PROBABILITY * GetRegionWeight();

            bool inTutorial = _daysSinceGameStart <= TUTORIAL_WINDOW_DAYS && WorldEventStore.ActiveEvents.Count < 3;
            if (inTutorial)
                probability = Math.Max(probability, 0.50f);

            if (roll > probability)
                return;

            var eligibleConfigs = WorldEventConfig.AllConfigs
                .Where(c => c.WeightMultiplier > 0)
                .ToList();

            if (eligibleConfigs.Count == 0) return;

            var selectedConfig = WeightedRandomSelect(eligibleConfigs, c => c.WeightMultiplier);
            if (selectedConfig == null) return;

            TryGenerateEvent(selectedConfig);
        }

        /// <summary>
        /// 动机驱动的真人冲突生成（TK5 风格）。
        ///
        /// 扫描真实 Hero 之间的关系/仇恨/野心/资源，让有动机的角色之间自然产生冲突。
        /// 事件不是骰子的结果，而是 AI 角色策略决策的投射。
        ///
        /// 扫描顺序（按动机强度降序）：
        ///   1. 跨 clan 仇恨 → NobleConflict / Assassination
        ///   2. 同 clan 内部矛盾 → Betrayal
        ///   3. 地方名人经济冲突 → DebtTrap / TradeDispute
        ///   4. 野心领主对外扩张 → SacredTheft / NobleConflict
        ///
        /// 找到即生成，不当天额外 roll 随机事件。
        /// 返回 true 表示已生成真人冲突事件。
        /// </summary>
        private bool TryGenerateMotivatedEvent()
        {
            // 收集候选 instigator（有动机、有资源的真人）
            var candidates = Hero.AllAliveHeroes
                .Where(h => h != null && h.IsAlive && h != Hero.MainHero
                    && !IsHeroBusyInEvent(h.StringId)
                    && h.StringId != null)
                .ToList();

            if (candidates.Count < 2) return false;

            // 随机打乱避免每次都从同一批人开始
            candidates = candidates.OrderBy(_ => MBRandom.RandomFloat).ToList();

            // ── 1. 跨 clan 深仇：NobleConflict / Assassination ──
            foreach (var instigator in candidates.Take(60)) // 只扫前 60 个，性能
            {
                if (instigator.Clan == null || instigator.Clan == Clan.PlayerClan) continue;
                if (!instigator.IsLord && instigator.Occupation != Occupation.GangLeader) continue;

                // 找这个 instigator 最恨的人
                var hated = candidates
                    .Where(t => t != instigator
                        && t.Clan != null
                        && t.Clan != instigator.Clan
                        && !IsHeroBusyInEvent(t.StringId))
                    .OrderBy(t => instigator.GetRelation(t)) // 关系越差越靠前
                    .FirstOrDefault();

                if (hated == null) continue;
                float relation = instigator.GetRelation(hated);
                if (relation > -25) continue; // 不够恨

                // 有动机！根据性格和资源选择事件类型
                int valor = instigator.GetTraitLevel(DefaultTraits.Valor);
                int mercy = instigator.GetTraitLevel(DefaultTraits.Mercy);
                int calculating = instigator.GetTraitLevel(DefaultTraits.Calculating);
                bool isRuthless = mercy <= -1;
                bool isAggressive = valor >= 1;
                bool isSchemer = calculating >= 1;

                EventType eventType;
                if (isRuthless && relation <= -40 && MBRandom.RandomFloat < 0.4f)
                    eventType = EventType.Assassination; // 冷酷 + 深仇 → 刺杀
                else if (isSchemer && hated.Clan?.Tier >= 3)
                    eventType = EventType.SacredTheft;    // 精明 + 对方是大族 → 偷圣物
                else
                    eventType = EventType.NobleConflict;  // 常规冲突

                var config = WorldEventConfig.Get(eventType);
                if (config == null || config.WeightMultiplier <= 0) continue;

                // 目标定居点：受害方的领地
                Settlement targetSettlement = hated.CurrentSettlement
                    ?? hated.HomeSettlement
                    ?? Settlement.All
                        .Where(s => s.IsVillage && s.OwnerClan == hated.Clan)
                        .OrderBy(_ => MBRandom.RandomFloat)
                        .FirstOrDefault();
                if (targetSettlement == null) continue;

                Hero targetHero = hated; // 动机系统已知恨的对象 → 无论 config 是否要求，都写入 WorldEvent 供叙事使用
                if (config.TargetsHero && targetHero == null) continue; // 仅当配置强制要求时才因缺目标跳过

                int severity = ClampInt((3 + (int)(Math.Abs(relation) / 10f)) * 10, config.MinSeverity, config.MaxSeverity);
                float dayLimit = config.MinDayLimit + MBRandom.RandomFloat * (config.MaxDayLimit - config.MinDayLimit);

                MobileParty eventParty = SpawnEventParty(config, targetSettlement, targetHero, instigator, isGeneric: false, severity, ref dayLimit);
                if (eventParty == null && config.PartyBehavior != EventPartyBehavior.NoParty) continue;

                string eventId = $"evt_motiv_{eventType.ToString().ToLower()}_{DateTime.UtcNow.Ticks:X8}_{MBRandom.RandomInt(10000)}";

                var worldEvent = new WorldEvent
                {
                    EventId = eventId,
                    Type = eventType,
                    Stage = EventStage.Active,
                    TargetHeroId = targetHero?.StringId,
                    TargetSettlementId = targetSettlement.StringId,
                    InitiatorId = instigator.StringId,
                    IsGenericInstigator = false,
                    GeneratedPartyId = eventParty?.StringId,
                    OccurredDay = (float)CampaignTime.Now.ToDays,
                    DayLimit = dayLimit,
                    Severity = severity,
                };
                worldEvent.IsRedirectedExistingParty = eventParty != null && instigator.PartyBelongedTo == eventParty;

                WorldEventStore.AddEvent(worldEvent);
                DebugLogger.Log($"[WorldEvent] Motivated conflict: {instigator.Name} → {eventType} → {hated.Name} (relation={relation}) at {targetSettlement.Name}");
                return true;
            }

            // ── 2. 同 clan 内部矛盾：Betrayal ──
            foreach (var instigator in candidates.Take(40))
            {
                if (instigator.Clan == null || instigator.Clan == Clan.PlayerClan) continue;
                if (instigator.Clan.Heroes.Count < 3) continue; // 小家族内斗没意思

                var betrayed = instigator.Clan.Heroes
                    .Where(h => h != null && h.IsAlive && h != instigator
                        && !IsHeroBusyInEvent(h.StringId)
                        && instigator.GetRelation(h) <= -15)
                    .OrderBy(h => instigator.GetRelation(h))
                    .FirstOrDefault();

                if (betrayed == null) continue;

                var config = WorldEventConfig.Get(EventType.Betrayal);
                if (config == null) continue;

                Settlement targetSettlement = betrayed.CurrentSettlement ?? betrayed.HomeSettlement;
                if (targetSettlement == null) continue;

                int severity = (5 + MBRandom.RandomInt(0, 3)) * 10;
                float dayLimit = config.MinDayLimit + MBRandom.RandomFloat * (config.MaxDayLimit - config.MinDayLimit);

                MobileParty eventParty = SpawnEventParty(config, targetSettlement, betrayed, instigator, isGeneric: false, severity, ref dayLimit);
                if (eventParty == null) continue;

                string eventId = $"evt_motiv_betrayal_{DateTime.UtcNow.Ticks:X8}_{MBRandom.RandomInt(10000)}";

                var worldEvent = new WorldEvent
                {
                    EventId = eventId,
                    Type = EventType.Betrayal,
                    Stage = EventStage.Active,
                    TargetHeroId = betrayed.StringId,
                    TargetSettlementId = targetSettlement.StringId,
                    InitiatorId = instigator.StringId,
                    IsGenericInstigator = false,
                    GeneratedPartyId = eventParty.StringId,
                    OccurredDay = (float)CampaignTime.Now.ToDays,
                    DayLimit = dayLimit,
                    Severity = severity,
                };
                worldEvent.IsRedirectedExistingParty = eventParty != null && instigator.PartyBelongedTo == eventParty;

                WorldEventStore.AddEvent(worldEvent);
                DebugLogger.Log($"[WorldEvent] Motivated betrayal: {instigator.Name} betrays {betrayed.Name} (relation={instigator.GetRelation(betrayed)}) in clan {instigator.Clan.Name}");
                return true;
            }

            // ── 3. 地方经济冲突：DebtTrap / TradeDispute ──
            foreach (var instigator in candidates.Take(40))
            {
                if (instigator.Occupation != Occupation.GangLeader
                    && instigator.Occupation != Occupation.Merchant)
                    continue;

                Settlement home = instigator.CurrentSettlement ?? instigator.HomeSettlement;
                if (home == null || home.Notables == null || home.Notables.Count < 2) continue;

                // 找同一/邻近定居点的一个弱势名人
                var victim = home.Notables
                    .Where(n => n != null && n.IsAlive && n != instigator
                        && !IsHeroBusyInEvent(n.StringId)
                        && n.Gold < instigator.Gold / 3) // 经济上明显弱势
                    .OrderBy(_ => MBRandom.RandomFloat)
                    .FirstOrDefault();

                if (victim == null) continue;

                EventType econType = instigator.Occupation == Occupation.GangLeader
                    ? EventType.DebtTrap
                    : EventType.TradeDispute;

                var config = WorldEventConfig.Get(econType);
                if (config == null) continue;

                int severity = MBRandom.RandomInt(2, 5) * 10;
                float dayLimit = config.MinDayLimit + MBRandom.RandomFloat * (config.MaxDayLimit - config.MinDayLimit);

                MobileParty eventParty = null;
                if (config.PartyBehavior != EventPartyBehavior.NoParty)
                    eventParty = SpawnEventParty(config, home, victim, instigator, isGeneric: false, severity, ref dayLimit);

                string eventId = $"evt_motiv_{econType.ToString().ToLower()}_{DateTime.UtcNow.Ticks:X8}_{MBRandom.RandomInt(10000)}";

                var worldEvent = new WorldEvent
                {
                    EventId = eventId,
                    Type = econType,
                    Stage = EventStage.Active,
                    TargetHeroId = victim.StringId,
                    TargetSettlementId = home.StringId,
                    InitiatorId = instigator.StringId,
                    IsGenericInstigator = false,
                    GeneratedPartyId = eventParty?.StringId,
                    OccurredDay = (float)CampaignTime.Now.ToDays,
                    DayLimit = dayLimit,
                    Severity = severity,
                };
                worldEvent.IsRedirectedExistingParty = eventParty != null && instigator.PartyBelongedTo == eventParty;

                WorldEventStore.AddEvent(worldEvent);
                DebugLogger.Log($"[WorldEvent] Motivated economic: {instigator.Name} ({instigator.Occupation}) targets {victim.Name} → {econType} at {home.Name}");
                return true;
            }

            // ── 4. 野心领主对外扩张：SacredTheft / NobleConflict（对无直接仇恨但有利益的目标）──
            if (MBRandom.RandomFloat < 0.15f) // 不每天都扫，降低性能压力
            {
                foreach (var instigator in candidates.Take(30))
                {
                    if (!instigator.IsLord || instigator.Clan == null || instigator.Clan == Clan.PlayerClan) continue;

                    int calculating = instigator.GetTraitLevel(DefaultTraits.Calculating);
                    int valor = instigator.GetTraitLevel(DefaultTraits.Valor);
                    if (calculating < 1 && valor < 1) continue; // 既不精明也不勇敢，没野心

                    // 找一个邻近的、不属于同一 clan 的定居点
                    Settlement home = instigator.CurrentSettlement ?? instigator.HomeSettlement;
                    if (home == null) continue;

                    var targetSettlement = Settlement.All
                        .Where(s => s.IsVillage
                            && s.OwnerClan != null
                            && s.OwnerClan != instigator.Clan
                            && s.OwnerClan != Clan.PlayerClan
                            && V.Pos(s).Distance(V.Pos(home)) < 80f)
                        .OrderBy(_ => MBRandom.RandomFloat)
                        .FirstOrDefault();

                    if (targetSettlement == null) continue;

                    EventType expType = calculating >= 1
                        ? EventType.SacredTheft  // 精明 → 偷圣物打击对方文化
                        : EventType.NobleConflict; // 勇敢 → 正面冲突

                    var config = WorldEventConfig.Get(expType);
                    if (config == null) continue;

                    Hero targetHero = SelectTargetHero(targetSettlement); // 即使 config 不要求，也尝试找 → 供叙事使用
                    if (config.TargetsHero && targetHero == null) continue; // 仅当配置强制要求时才因缺目标跳过

                    int severity = MBRandom.RandomInt(3, 7) * 10;
                    float dayLimit = config.MinDayLimit + MBRandom.RandomFloat * (config.MaxDayLimit - config.MinDayLimit);

                    MobileParty eventParty = SpawnEventParty(config, targetSettlement, targetHero, instigator, isGeneric: false, severity, ref dayLimit);
                    if (eventParty == null && config.PartyBehavior != EventPartyBehavior.NoParty) continue;

                    string eventId = $"evt_motiv_exp_{expType.ToString().ToLower()}_{DateTime.UtcNow.Ticks:X8}_{MBRandom.RandomInt(10000)}";

                    var worldEvent = new WorldEvent
                    {
                        EventId = eventId,
                        Type = expType,
                        Stage = EventStage.Active,
                        TargetHeroId = targetHero?.StringId,
                        TargetSettlementId = targetSettlement.StringId,
                        InitiatorId = instigator.StringId,
                        IsGenericInstigator = false,
                        GeneratedPartyId = eventParty?.StringId,
                        OccurredDay = (float)CampaignTime.Now.ToDays,
                        DayLimit = dayLimit,
                        Severity = severity,
                    };
                    worldEvent.IsRedirectedExistingParty = eventParty != null && instigator.PartyBelongedTo == eventParty;

                    WorldEventStore.AddEvent(worldEvent);
                    DebugLogger.Log($"[WorldEvent] Motivated expansion: {instigator.Name} ({instigator.Clan.Name}) → {expType} at {targetSettlement.Name} (trait: valor={valor} calc={calculating})");
                    return true;
                }
            }

            return false; // 没找到动机冲突，回落随机事件
        }

        /// <summary>
        /// 新手窗口强制事件保障：如果前 N 天自然概率没触发事件，
        /// 强制在玩家附近生成一个 BanditRaid（最常见、最直观的事件类型）。
        /// </summary>
        private void ForceGenerateTutorialEvent()
        {
            // 优先尝试生成真人动机冲突
            if (TryGenerateMotivatedEvent())
            {
                _tutorialEventsGenerated++;
                return;
            }

            // 回落：附近生成一个简单的 BanditRaid
            var config = WorldEventConfig.Get(EventType.BanditRaid);
            if (config == null) return;

            // 优先选玩家附近的定居点
            Settlement targetSettlement = SelectSettlementNearPlayer(40f)
                ?? SelectTargetSettlement();
            if (targetSettlement == null) return;

            Hero targetHero = SelectTargetHero(targetSettlement);
            Hero instigatorHero = null;
            bool isGeneric = false;
            SelectInstigatorBySource(config, targetSettlement, targetHero, out instigatorHero, out isGeneric);

            int severity = MBRandom.RandomInt(2, 4) * 10; // 新手友好的低严重度
            float dayLimit = 5f + MBRandom.RandomFloat * 5f; // 5-10 天，足够宽裕

            MobileParty eventParty = SpawnEventParty(config, targetSettlement, targetHero, instigatorHero, isGeneric, severity, ref dayLimit);
            if (eventParty == null) return;

            string eventId = $"evt_tutorial_{config.EventType.ToString().ToLower()}_{DateTime.UtcNow.Ticks:X8}_{MBRandom.RandomInt(10000)}";

            var worldEvent = new WorldEvent
            {
                EventId = eventId,
                Type = config.EventType,
                Stage = EventStage.Active,
                TargetHeroId = targetHero?.StringId,
                TargetSettlementId = targetSettlement.StringId,
                InitiatorId = instigatorHero?.StringId,
                IsGenericInstigator = isGeneric,
                GeneratedPartyId = eventParty.StringId,
                OccurredDay = (float)CampaignTime.Now.ToDays,
                DayLimit = dayLimit,
                Severity = severity,
            };

            WorldEventStore.AddEvent(worldEvent);
            _tutorialEventsGenerated++;
        }

        /// <summary>选玩家附近指定距离内的定居点（新手引导用）。</summary>
        private Settlement SelectSettlementNearPlayer(float maxDistance)
        {
            var playerPos = V.Pos(MobileParty.MainParty);
            return Settlement.All
                .Where(s => s.IsVillage && s.Notables != null && s.Notables.Count > 0
                    && V.Pos(s).Distance(playerPos) < maxDistance)
                .OrderBy(s => V.Pos(s).Distance(playerPos))
                .FirstOrDefault();
        }

        /// <summary>
        /// 【控制台调试】强制在玩家附近生成一个指定类型的世界事件。
        /// 调用方：MyCommands.worldevent_force
        /// </summary>
        /// <param name="eventType">事件类型，默认 BanditRaid</param>
        /// <param name="severity">严重度，-1=随机(2-5)</param>
        /// <returns>生成结果描述</returns>
        public static string ForceGenerateEvent(EventType eventType = EventType.BanditRaid, int severity = -1)
        {
            if (Campaign.Current == null || MobileParty.MainParty == null)
                return "Error: Campaign not ready.";

            var config = WorldEventConfig.Get(eventType);
            if (config == null)
                return $"Error: No config for {eventType}.";

            // 优先玩家附近，否则全局
            var simulator = Campaign.Current.GetCampaignBehavior<WorldEventSimulator>();
            Settlement targetSettlement = null;
            if (simulator != null)
            {
                targetSettlement = simulator.SelectSettlementNearPlayer(60f)
                    ?? simulator.SelectTargetSettlement();
            }
            if (targetSettlement == null)
            {
                var playerPos = V.Pos(MobileParty.MainParty);
                targetSettlement = Settlement.All
                    .Where(s => s.IsVillage && s.Notables?.Count > 0)
                    .OrderBy(s => V.Pos(s).Distance(playerPos))
                    .FirstOrDefault();
            }
            if (targetSettlement == null)
                return "Error: No suitable settlement found.";

            Hero targetHero = simulator?.SelectTargetHero(targetSettlement); // 即使 config 不要求，也尝试找 → 供叙事使用
            if (config.TargetsHero && targetHero == null)
                return "Error: No suitable target hero found.";

            Hero instigatorHero = null;
            bool isGeneric = false;
            if (simulator != null)
                simulator.SelectInstigatorBySource(config, targetSettlement, targetHero, out instigatorHero, out isGeneric);

            int sev = severity > 0 ? Math.Min(severity, 100) : MBRandom.RandomInt(2, 6) * 10;
            float dayLimit = config.MinDayLimit + MBRandom.RandomFloat * (config.MaxDayLimit - config.MinDayLimit);

            MobileParty eventParty = null;
            if (simulator != null && config.PartyBehavior != EventPartyBehavior.NoParty)
                eventParty = simulator.SpawnEventParty(config, targetSettlement, targetHero, instigatorHero, isGeneric, sev, ref dayLimit);

            string eventId = $"evt_cmd_{eventType.ToString().ToLower()}_{DateTime.UtcNow.Ticks:X8}_{MBRandom.RandomInt(10000)}";

            var worldEvent = new WorldEvent
            {
                EventId = eventId,
                Type = config.EventType,
                Stage = EventStage.Active,
                TargetHeroId = targetHero?.StringId,
                TargetSettlementId = targetSettlement.StringId,
                InitiatorId = instigatorHero?.StringId,
                IsGenericInstigator = isGeneric,
                GeneratedPartyId = eventParty?.StringId,
                OccurredDay = (float)CampaignTime.Now.ToDays,
                DayLimit = dayLimit,
                Severity = sev,
            };

            WorldEventStore.AddEvent(worldEvent);
            return $"OK: {eventType} at {targetSettlement.Name} sev={sev} party={eventParty != null} hero={instigatorHero?.Name?.ToString() ?? "generic"}";
        }

        /// <summary>数据驱动的通用事件生成。</summary>
        private void TryGenerateEvent(WorldEventConfig config)
        {
            if (config == null) return;

            // 1. 选定居点
            Settlement targetSettlement = SelectTargetSettlement();
            if (targetSettlement == null) return;

            // 2. 选受害者（即使 config 不要求，也尝试找 → 供叙事使用）
            Hero targetHero = SelectTargetHero(targetSettlement);
            if (config.TargetsHero && targetHero == null) return; // 配置强制要求但找不到真人 → 不生成

            // 3. 选加害方
            Hero instigatorHero = null;
            bool isGeneric = false;
            SelectInstigatorBySource(config, targetSettlement, targetHero, out instigatorHero, out isGeneric);

            if (!isGeneric && instigatorHero == null && !config.AllowGeneric)
                return; // 必须有真人但找不到 → 不生成

            // 4. 设严重度和时限
            int severity = MBRandom.RandomInt(config.MinSeverity, config.MaxSeverity + 1);
            float dayLimit = config.MinDayLimit + MBRandom.RandomFloat * (config.MaxDayLimit - config.MinDayLimit);

            // 5. 生成 MobileParty（如果需要）
            MobileParty eventParty = null;
            if (config.PartyBehavior != EventPartyBehavior.NoParty)
            {
                eventParty = SpawnEventParty(config, targetSettlement, targetHero, instigatorHero, isGeneric, severity, ref dayLimit);
                if (eventParty == null && config.PartyBehavior != EventPartyBehavior.NoParty)
                {
                    DebugLogger.Log($"[WorldEventSimulator] Failed to spawn party for {config.EventType}");
                    // 无 party 的非必需类型仍然可以继续（如 TradeDispute/Famine 本身就没有 party）
                    if (config.PartyBehavior == EventPartyBehavior.RaidSettlement ||
                        config.PartyBehavior == EventPartyBehavior.EngageTarget)
                        return; // 必需 party 的类型失败了就放弃
                }
            }

            // 6. 生成唯一 EventId
            string eventId = $"evt_{config.EventType.ToString().ToLower()}_{DateTime.UtcNow.Ticks:X8}_{MBRandom.RandomInt(10000)}";

            // 7. 创建 WorldEvent
            var worldEvent = new WorldEvent
            {
                EventId = eventId,
                Type = config.EventType,
                Stage = EventStage.Active,
                TargetHeroId = targetHero?.StringId,
                TargetSettlementId = targetSettlement.StringId,
                InitiatorId = instigatorHero?.StringId,
                IsGenericInstigator = isGeneric,
                GeneratedPartyId = eventParty?.StringId,
                OccurredDay = (float)CampaignTime.Now.ToDays,
                DayLimit = dayLimit,
                Severity = severity,
            };

            // 8. 存入数据库
            WorldEventStore.AddEvent(worldEvent);

            // 9. 尝试分配幕后黑手（~5% 概率）
            ConspiracyManager.TryAssignMastermind(worldEvent);
        }

        /// <summary>
        /// 区域状态加权：有 Hideout 附近概率翻倍，战争地区概率提高。
        /// </summary>
        private float GetRegionWeight()
        {
            float weight = 1.0f;

            // 玩家附近有 Hideout → 概率翻倍
            var nearbyHideout = Settlement.All
                .FirstOrDefault(s => s.IsHideout
                    && V.Pos(s).Distance(V.Pos(MobileParty.MainParty)) < 100f);
            if (nearbyHideout != null)
                weight *= 2.0f;

            // 正在战争的 faction 地区概率提高
            if (Clan.PlayerClan?.Kingdom != null)
            {
                // 检查玩家王国是否与其他王国处于战争状态
                bool atWar = false;
                foreach (var otherKingdom in Kingdom.All)
                {
                    if (otherKingdom != Clan.PlayerClan.Kingdom
                        && Clan.PlayerClan.Kingdom.IsAtWarWith(otherKingdom))
                    {
                        atWar = true;
                        break;
                    }
                }
                if (atWar) weight *= 1.5f;
            }

            return Math.Min(weight, 3.0f); // 上限 3x
        }

        #endregion

        #region Config-Driven Instigator Selection

        private void SelectInstigatorBySource(WorldEventConfig config, Settlement targetSettlement,
            Hero targetHero, out Hero instigator, out bool isGeneric)
        {
            instigator = null;
            isGeneric = false;

            switch (config.InstigatorSource)
            {
                case InstigatorSource.BanditHideout:
                    instigator = FindBanditNearHideout(targetSettlement) ?? FindFreeBanditHero();
                    if (instigator == null && config.AllowGeneric) isGeneric = true;
                    break;
                case InstigatorSource.AnyBandit:
                    instigator = FindFreeBanditHero();
                    if (instigator == null && config.AllowGeneric) isGeneric = true;
                    break;
                case InstigatorSource.EnemyLord:
                    instigator = FindEnemyLord(targetSettlement);
                    if (instigator == null && !config.AllowGeneric) return;
                    if (instigator == null && config.AllowGeneric) isGeneric = true;
                    break;
                case InstigatorSource.RelatedHero:
                    instigator = FindRelatedHero(targetHero, targetSettlement);
                    break;
                case InstigatorSource.TownNotable:
                    instigator = FindTownNotable(targetSettlement);
                    if (instigator == null && !config.AllowGeneric) return;
                    break;
                case InstigatorSource.None:
                case InstigatorSource.Nemesis:
                    isGeneric = true;
                    break;
            }
        }

        private Hero FindBanditNearHideout(Settlement target)
        {
            foreach (var hideout in Settlement.All.Where(s => s.IsHideout
                && V.Pos(s).Distance(V.Pos(target)) < 150f)
                .OrderBy(s => V.Pos(s).Distance(V.Pos(target))))
            {
                foreach (var clan in Clan.BanditFactions)
                {
                    if (clan == null) continue;
                    foreach (var hero in clan.Heroes)
                    {
                        if (hero == null || !hero.IsAlive || IsHeroBusyInEvent(hero.StringId)) continue;
                        if (hero.CurrentSettlement == hideout
                            || (hero.PartyBelongedTo != null
                                && V.Pos(hero.PartyBelongedTo).Distance(V.Pos(hideout)) < 80f))
                            return hero;
                    }
                }
            }
            return null;
        }

        private Hero FindFreeBanditHero()
        {
            var candidates = new List<Hero>();
            foreach (var clan in Clan.BanditFactions)
            {
                if (clan == null) continue;
                foreach (var hero in clan.Heroes)
                    if (hero != null && hero.IsAlive && !IsHeroBusyInEvent(hero.StringId))
                        candidates.Add(hero);
            }
            return candidates.Count > 0 ? candidates[MBRandom.RandomInt(0, candidates.Count)] : null;
        }

        private Hero FindEnemyLord(Settlement target)
        {
            var enemies = new List<Hero>();
            foreach (var clan in Clan.All)
            {
                if (clan == null || clan == Clan.PlayerClan) continue;
                foreach (var hero in clan.Heroes)
                    if (hero != null && hero.IsAlive && hero.IsLord && !IsHeroBusyInEvent(hero.StringId))
                        enemies.Add(hero);
            }
            if (enemies.Count == 0) return null;
            return enemies.OrderBy(h => V.Pos(target).Distance(
                V.Pos(h.CurrentSettlement ?? (Settlement)h.HomeSettlement ?? target))).FirstOrDefault();
        }

        private Hero FindRelatedHero(Hero target, Settlement settlement)
        {
            if (target == null) return null;
            if (target.Clan != null)
            {
                var member = target.Clan.Heroes
                    .FirstOrDefault(h => h != null && h.IsAlive && h != target && !IsHeroBusyInEvent(h.StringId));
                if (member != null) return member;
            }
            if (settlement?.Notables != null)
            {
                var local = settlement.Notables
                    .FirstOrDefault(n => n != null && n.IsAlive && n != target && !IsHeroBusyInEvent(n.StringId));
                if (local != null) return local;
            }
            return null;
        }

        private Hero FindTownNotable(Settlement settlement)
        {
            if (settlement?.Notables == null) return null;
            return settlement.Notables.FirstOrDefault(n => n != null && n.IsAlive
                && (n.Occupation == Occupation.GangLeader || n.Occupation == Occupation.Merchant)
                && !IsHeroBusyInEvent(n.StringId))
                ?? settlement.Notables.FirstOrDefault(n => n != null && n.IsAlive
                    && !n.IsLord && !IsHeroBusyInEvent(n.StringId));
        }

        private bool IsHeroBusyInEvent(string heroId)
        {
            return WorldEventStore.ActiveEvents.Any(e =>
                e.InitiatorId == heroId || e.TargetHeroId == heroId);
        }

        #endregion

        #region Config-Driven Party Spawning

        /// <summary>
        /// 🐛 调试开关：临时禁用 WorldEvent party 生成，排查 0xc00000ff 栈溢出。
        /// 设为 true 后所有事件只记录不生成 party。排查完恢复 false。
        /// </summary>
        private const bool DEBUG_DISABLE_PARTY_SPAWN = false;

        private MobileParty SpawnEventParty(WorldEventConfig config, Settlement targetSettlement,
            Hero targetHero, Hero instigatorHero, bool isGeneric, int severity, ref float dayLimit)
        {

            try
            {
                string prefix = config.EventType.ToString().ToLower();
                string partyId = $"lwn_{prefix}_{targetSettlement.StringId}_{MBRandom.RandomInt(10000)}";
                MobileParty party;

                // ── 真人 instigator：优先征用其现有部队，而非另建幽灵 party ──
                if (!isGeneric && instigatorHero != null)
                {
                    MobileParty existingParty = instigatorHero.PartyBelongedTo;

                    if (existingParty != null && existingParty.IsActive && existingParty.LeaderHero == instigatorHero)
                    {
                        // instigator 本人正带队 → 直接调遣他的真实部队去打目标！
                        party = existingParty;
                        V.SetPartyName(party,new TextObject(
                            GetPartyNameTemplate(config, instigatorHero, targetSettlement, targetHero)));
                        party.Ai.SetDoNotMakeNewDecisions(false); // 解锁 AI，允许我们下达新指令

                        // 根据行军距离自动延长事件过期时间（防止 lord 还没走到就过期了）
                        float distToTarget = V.Pos(party).Distance(V.Pos(targetSettlement));
                        float speedEstimate = party.Speed > 0.1f ? party.Speed : 2.5f;
                        float travelDays = distToTarget / (speedEstimate * 24f);
                        float minTotalDays = travelDays + 4f;
                        if (minTotalDays > dayLimit)
                            dayLimit = Math.Min(minTotalDays, 30f);
                        DebugLogger.Log($"[WorldEventSimulator] Redirecting party of {instigatorHero.Name} → {targetSettlement.Name} (dist={distToTarget:F0}, boosted)");
                    }
                    else
                    {
                        // instigator 没有自己的队伍（在定居点 / 在别人军队里 / 被俘）→ 尝试拉出来新建
                        string unavailReason = GetHeroUnavailabilityReason(instigatorHero);
                        if (!string.IsNullOrEmpty(unavailReason))
                        {
                            DebugLogger.Log($"[WorldEventSimulator] WARNING: instigator {instigatorHero.Name} not available for party creation: {unavailReason}. Attempting extraction...");
                        }

                        // 尝试将 hero 从当前占用中解放
                        if (!TryExtractHeroForParty(instigatorHero))
                        {
                            DebugLogger.Log($"[WorldEventSimulator] ERROR: Failed to extract {instigatorHero.Name} for event party. Hero is {unavailReason}. Falling back to generic party.");
                            party = CreateGenericEventParty(partyId, config, targetSettlement, targetHero, severity);
                            if (party != null)
                            {
                                party.SetPartyUsedByQuest(true);
                                Campaign.Current?.VisualTrackerManager?.RegisterObject(party);
                            }
                            return party;
                        }

                        if (instigatorHero.Clan == null)
                            instigatorHero.Clan = Clan.BanditFactions.FirstOrDefault() ?? Clan.PlayerClan;

                        var component = new SafeLordPartyComponent(instigatorHero);
                        string nameTemplate = GetPartyNameTemplate(config, instigatorHero, targetSettlement, targetHero);
                        party = V.MakeParty(partyId, component);
                        if (party != null) V.SetPartyName(party,new TextObject(nameTemplate));
                        if (party == null)
                        {
                            DebugLogger.Log($"[WorldEventSimulator] ERROR: MobileParty.CreateParty returned null for {instigatorHero.Name}");
                            return null;
                        }
                        party.ActualClan = instigatorHero.Clan;
                        MobilizePartyTroops(party, instigatorHero, targetSettlement, targetHero, config, severity, dayLimit);

                        // 验证 hero 确实进入了 party
                        if (party.LeaderHero != instigatorHero || party.MemberRoster.GetTroopCount(instigatorHero.CharacterObject) == 0)
                        {
                            DebugLogger.Log($"[WorldEventSimulator] ERROR: {instigatorHero.Name} failed to join created party. LeaderHero={party.LeaderHero?.Name?.ToString() ?? "null"}, inRoster={party.MemberRoster.GetTroopCount(instigatorHero.CharacterObject)}. Reason: {unavailReason}");
                            V.DelParty(party);
                            party = CreateGenericEventParty(partyId, config, targetSettlement, targetHero, severity);
                            if (party != null)
                            {
                                party.SetPartyUsedByQuest(true);
                                Campaign.Current?.VisualTrackerManager?.RegisterObject(party);
                            }
                            return party;
                        }

                        DebugLogger.Log($"[WorldEventSimulator] Mobilized party for {instigatorHero.Name} ({party.MemberRoster.TotalManCount} troops, from garrisons) → {targetSettlement.Name}");

                        // 定位：在目标周围找可通行位置，验证岛屿连通性 + 寻路距离
                        V.SetPos(party, FindReachableSpawnPosition(targetSettlement));;
                    }
                }
                else
                {
                    party = CreateGenericEventParty(partyId, config, targetSettlement, targetHero, severity);
                    if (party == null) return null;
                }

                // AI 行为
                switch (config.PartyBehavior)
                {
                    case EventPartyBehavior.RaidSettlement:
                    case EventPartyBehavior.GoToSettlement:
                        V.SetMoveToTown(party,targetSettlement);
                        break;
                    case EventPartyBehavior.EngageTarget:
                        if (targetHero?.PartyBelongedTo != null)
                            V.SetMoveEngage(party,targetHero.PartyBelongedTo);
                        else
                            V.SetMoveToTown(party,targetSettlement);
                        break;
                    case EventPartyBehavior.PatrolNearTarget:
                        V.SetMovePatrol(party,V.Pos(party));
                        break;
                    case EventPartyBehavior.ChasePlayer:
                        V.SetMoveEngage(party,MobileParty.MainParty);
                        break;
                }
                party.Ai.SetDoNotMakeNewDecisions(true); // 锁定目标，不被原生 AI 拐跑；到达由 CheckEventPartyArrivals 检测并触发后果
                party.SetPartyUsedByQuest(true);
                Campaign.Current?.VisualTrackerManager?.RegisterObject(party); // 注册地图追踪 → 无视战争迷雾 + 显示任务光圈
                party.Party.SetVisualAsDirty();

                return party;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEventSimulator] SpawnEventParty error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 为世界事件 spawn 一个辅助部队。CommissionQuest 接取时通过 RoleTag 查找复用。
        /// 返回创建的 party（失败返回 null）。由 WorldEventStore.AddEvent 集中调用。
        /// </summary>
        public static MobileParty SpawnAuxiliaryParty(AuxiliaryPartyConfig auxConfig, WorldEvent evt)
        {
            if (auxConfig == null || evt == null) return null;

            try
            {
                Settlement targetSettlement = evt.TargetSettlement;
                Hero instigator = evt.InstigatorHero;
                Hero victim = evt.TargetHero;
                if (targetSettlement == null) return null;

                string safeRole = auxConfig.RoleTag ?? "aux";
                string partyId = $"lwn_aux_{safeRole}_{evt.EventId}_{MBRandom.RandomInt(1000)}";

                // 确定 faction 和 culture
                Clan factionClan;
                CultureObject culture;

                switch (auxConfig.Faction)
                {
                    case AuxiliaryFaction.Instigator:
                        factionClan = instigator?.Clan ?? Clan.PlayerClan;
                        break;
                    case AuxiliaryFaction.Victim:
                        factionClan = victim?.Clan ?? targetSettlement.OwnerClan ?? Clan.PlayerClan;
                        break;
                    case AuxiliaryFaction.Bandit:
                        factionClan = Clan.BanditFactions.FirstOrDefault(c => c.StringId == "looters")
                            ?? Clan.BanditFactions.FirstOrDefault()
                            ?? Clan.PlayerClan;
                        break;
                    default:
                        factionClan = Clan.PlayerClan;
                        break;
                }

                switch (auxConfig.CultureSource)
                {
                    case AuxiliaryCultureSource.Instigator:
                        culture = instigator?.Culture ?? Hero.MainHero.Culture;
                        break;
                    case AuxiliaryCultureSource.Victim:
                        culture = victim?.Culture ?? targetSettlement.Culture ?? Hero.MainHero.Culture;
                        break;
                    case AuxiliaryCultureSource.Bandit:
                        culture = factionClan?.Culture ?? Hero.MainHero.Culture;
                        break;
                    default:
                        culture = Hero.MainHero.Culture;
                        break;
                }

                // 确定生成位置（所有路径均经过导航网格验证）
                Vec2 spawnPos;
                switch (auxConfig.SpawnPosition)
                {
                    case AuxiliarySpawnPosition.BetweenParties:
                        Vec2 instPos = instigator?.PartyBelongedTo != null ? V.Pos(instigator.PartyBelongedTo)
                            : evt.GeneratedParty != null ? V.Pos(evt.GeneratedParty)
                            : V.Pos(targetSettlement);
                        Vec2 victimPos = victim?.PartyBelongedTo != null ? V.Pos(victim.PartyBelongedTo)
                            : V.Pos(targetSettlement);
                        Vec2 mid = (instPos + victimPos) * 0.5f;
                        spawnPos = V.AccessiblePointNear(Campaign.Current?.MapSceneWrapper, mid, 15f);
                        break;
                    case AuxiliarySpawnPosition.NearInstigator:
                        Vec2 instBase = instigator?.PartyBelongedTo != null ? V.Pos(instigator.PartyBelongedTo)
                            : evt.GeneratedParty != null ? V.Pos(evt.GeneratedParty)
                            : V.Pos(targetSettlement);
                        spawnPos = V.AccessiblePointNear(Campaign.Current?.MapSceneWrapper, instBase, 10f);
                        break;
                    case AuxiliarySpawnPosition.NearTarget:
                    default:
                        spawnPos = FindReachableSpawnPosition(targetSettlement);
                        break;
                }

                // 创建 party
                // 目标名兜底：目的地
                string displayName = auxConfig.NameTemplate.Replace("{TARGET}", targetSettlement.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_simulator_fallback_destination", "destination"));
                var component = new CustomPartyComponent(targetSettlement, displayName);
                MobileParty party = V.MakeParty(partyId, component);
                if (party != null) V.SetPartyName(party,new TextObject(displayName));
                if (party == null) return null;

                party.ActualClan = factionClan;
                V.SetPos(party, spawnPos);;

                // 填充兵力
                int troopCount = auxConfig.MinTroops + MBRandom.RandomInt(Math.Max(1, auxConfig.MaxTroops - auxConfig.MinTroops + 1));
                var template = culture?.DefaultPartyTemplate ?? factionClan?.DefaultPartyTemplate;
                if (template != null)
                    V.InitPartyPos(party, template, spawnPos);
                party.MemberRoster.Clear();
                var basicTroop = culture?.BasicTroop ?? factionClan?.Culture?.BasicTroop;
                if (basicTroop != null)
                    party.MemberRoster.AddToCounts(basicTroop, troopCount);

                // AI 行为
                switch (auxConfig.Behavior)
                {
                    case EventPartyBehavior.GoToSettlement:
                    case EventPartyBehavior.RaidSettlement:
                        V.SetMoveToTown(party,targetSettlement);
                        break;
                    case EventPartyBehavior.PatrolNearTarget:
                        V.SetMovePatrol(party,spawnPos);
                        break;
                    case EventPartyBehavior.EngageTarget:
                        if (victim?.PartyBelongedTo != null)
                            V.SetMoveEngage(party,victim.PartyBelongedTo);
                        else
                            V.SetMovePatrol(party,spawnPos);
                        break;
                    case EventPartyBehavior.ChasePlayer:
                        V.SetMoveEngage(party,MobileParty.MainParty);
                        break;
                    default:
                        V.SetMovePatrol(party,spawnPos);
                        break;
                }
                party.Ai.SetDoNotMakeNewDecisions(true); // 辅助部队行为固定，不自行满世界乱跑
                party.SetPartyUsedByQuest(true);
                party.Party.SetVisualAsDirty();

                DebugLogger.Log($"[WorldEventSimulator] Spawned auxiliary party '{auxConfig.RoleTag}' id={partyId} at ({spawnPos.X:F0},{spawnPos.Y:F0}) troops={troopCount}");
                return party;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEventSimulator] SpawnAuxiliaryParty '{auxConfig?.RoleTag}' error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 在目标定居点周围找到一个真实可达的生成位置。
        ///
        /// 分两层验证：
        ///   1. AreFacesOnSameIsland — 候选面和定居点面是否在同一连通岛上（排除隔山/隔水）
        ///   2. GetPathDistanceBetweenAIFaces — 实际寻路距离是否在合理范围内
        ///
        /// 候选策略：在定居点周围 15~45 单位半径上以 8 个方向尝试，
        /// 每次尝试先做 navmesh 投影，再验证岛屿连通性和寻路距离。
        /// 全部失败则用引擎原生的 GetAccessiblePointNearPosition 兜底。
        ///
        /// 鼠标变禁用图标也是基于同一套 AreFacesOnSameIsland 底层判断。
        /// </summary>
        private static Vec2 FindReachableSpawnPosition(Settlement targetSettlement)
        {
            var wrapper = Campaign.Current?.MapSceneWrapper;
            if (wrapper == null || targetSettlement == null)
                return targetSettlement != null ? V.Pos(targetSettlement) : Vec2.Zero;

            Vec2 settlementPos = V.Pos(targetSettlement);
            PathFaceRecord settlementFace = V.FaceIndex(wrapper, settlementPos);
            if (!settlementFace.IsValid())
            {
                DebugLogger.Log($"[WorldEventSimulator] FindReachableSpawnPosition: settlement face invalid for {targetSettlement.Name}! Using GetAccessiblePointNearPosition fallback.");
                return V.AccessiblePointNear(wrapper, settlementPos, 30f);
            }

            const int MAX_ATTEMPTS = 24; // 3 圈 × 8 个方向
            int attempt = 0;

            // 三圈：不同距离
            foreach (float radius in new[] { 18f, 30f, 42f })
            {
                // 随机起始角度避免总是尝试同一个方向
                float baseAngle = MBRandom.RandomFloat * 2f * (float)Math.PI;
                for (int dir = 0; dir < 8; dir++)
                {
                    float angle = baseAngle + dir * (float)Math.PI / 4f;
                    Vec2 candidate = settlementPos + new Vec2(
                        (float)Math.Cos(angle) * radius,
                        (float)Math.Sin(angle) * radius);

                    // 投影到 navmesh
                    Vec2 projected = wrapper.GetLastPointOnNavigationMeshFromPositionToDestination(
                        settlementFace, candidate, settlementPos);

                    PathFaceRecord projectedFace = V.FaceIndex(wrapper, projected);
                    if (!projectedFace.IsValid())
                        continue;

#if !MB2_V1212
                    // 🔑 验证：寻路距离是否合理（Latest API：额外参数 excludedFaceIds/regionSwitchCost）
                    if (!wrapper.GetPathDistanceBetweenAIFaces(
                        projectedFace, settlementFace, projected, settlementPos,
                        0.1f, 100f, out float pathDist, null, 0, 0))
                        continue;
#else
                    // 🔑 验证 1：同一岛屿？
                    if (!wrapper.AreFacesOnSameIsland(projectedFace, settlementFace, ignoreDisabled: false))
                        continue;

                    // 🔑 验证 2：寻路距离是否合理（距离 < 100 单位，排除绕远路的孤立路径）
                    if (!wrapper.GetPathDistanceBetweenAIFaces(
                        projectedFace, settlementFace, projected, settlementPos,
                        0.1f, 100f, out float pathDist))
                        continue;
#endif

                    attempt++;
                    // 寻路距离不应超过直线距离的 3 倍（否则地形严重阻挡）
                    float straightDist = projected.Distance(settlementPos);
                    if (pathDist > straightDist * 3f && pathDist > 10f)
                        continue;

                    DebugLogger.Log(attempt > 1
                        ? $"[WorldEventSimulator] FindReachableSpawnPosition: found valid pos at ({projected.X:F1},{projected.Y:F1}) after {attempt} attempts (straight={straightDist:F1}m, path={pathDist:F1}m)"
                        : $"[WorldEventSimulator] FindReachableSpawnPosition: ({projected.X:F1},{projected.Y:F1}) straight={straightDist:F1}m path={pathDist:F1}m");
                    return projected;
                }
            }

            // 全部候选失败 → 宽松 fallback：投影到定居点所在岛的面，取距离定居点最远的可达点。
            // 不依赖 GetAccessiblePointNearPosition（会 snap 回定居点中心导致不可选中）。
            // 只要求同岛 + face 有效，不要求 path distance（岛太小的时候 path dist 本质上就是直线距离）。
            DebugLogger.Log($"[WorldEventSimulator] FindReachableSpawnPosition: all {MAX_ATTEMPTS} candidates failed for {targetSettlement.Name} — relaxed projection fallback");
            Vec2 bestFallback = settlementPos;
            float bestDist = 0f;

            foreach (float radius in new[] { 5f, 8f, 12f, 18f, 25f, 35f, 50f, 70f })
            {
                float baseAngle = MBRandom.RandomFloat * 2f * (float)Math.PI;
                for (int dir = 0; dir < 12; dir++)
                {
                    float angle = baseAngle + dir * (float)Math.PI * 2f / 12f;
                    Vec2 candidate = settlementPos + new Vec2(
                        (float)Math.Cos(angle) * radius,
                        (float)Math.Sin(angle) * radius);
                    Vec2 projected = wrapper.GetLastPointOnNavigationMeshFromPositionToDestination(
                        settlementFace, candidate, settlementPos);
                    PathFaceRecord projFace = V.FaceIndex(wrapper, projected);
                    if (!projFace.IsValid()) continue;
#if !MB2_V1212
                    // AreFacesOnSameIsland removed in Latest; use GetPathDistanceBetweenAIFaces directly
                    if (!wrapper.GetPathDistanceBetweenAIFaces(
                        projFace, settlementFace, projected, settlementPos,
                        0.1f, 100f, out float pfDist2, null, 0, 0)) continue;
#else
                    if (!wrapper.AreFacesOnSameIsland(projFace, settlementFace, ignoreDisabled: false)) continue;

                    // 🔑 验证：寻路距离是否合理
                    if (!wrapper.GetPathDistanceBetweenAIFaces(
                        projFace, settlementFace, projected, settlementPos,
                        0.1f, 100f, out float pfDist2)) continue;
#endif

                    float dist = projected.Distance(settlementPos);
                    if (dist > bestDist) { bestDist = dist; bestFallback = projected; }
                }

                if (bestDist > 5f) break; // 已经够远，停止搜索
            }

            if (bestDist < 3f)
                DebugLogger.Log($"[WorldEventSimulator] WARNING: best spawn for {targetSettlement.Name} only {bestDist:F1} units from settlement center — party may overlap settlement UI");
            else
                DebugLogger.Log($"[WorldEventSimulator] FindReachableSpawnPosition: relaxed fallback at dist={bestDist:F1} from {targetSettlement.Name} pos=({bestFallback.X:F1},{bestFallback.Y:F1})");
            return bestFallback;
        }

        private string GetPartyNameTemplate(WorldEventConfig config, Hero instigator, Settlement settlement, Hero target)
        {
            // 加害方名兜底：某人
            string name = instigator?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_simulator_fallback_someone", "someone");
            // 地点名兜底：某地
            string loc = settlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_simulator_fallback_place", "Somewhere");
            // 目标名兜底：目标
            string tgt = target?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_simulator_fallback_target", "the target");

            switch (config.EventType)
            {
                // 事件部队名：劫掠队
                case EventType.BanditRaid: return LWNTextHelper.ResolveCompound("LWN_simulator_party_banditraid", ("NAME", name));
                // 事件部队名：绑匪帮
                case EventType.Kidnapping: return LWNTextHelper.ResolveCompound("LWN_simulator_party_kidnapping", ("NAME", name));
                // 事件部队名：叛军
                case EventType.Betrayal: return LWNTextHelper.ResolveCompound("LWN_simulator_party_betrayal", ("NAME", name));
                // 事件部队名：讨债队
                case EventType.DebtTrap: return LWNTextHelper.ResolveCompound("LWN_simulator_party_debttrap", ("NAME", name));
                // 事件部队名：征讨军
                case EventType.NobleConflict: return LWNTextHelper.ResolveCompound("LWN_simulator_party_nobleconflict", ("NAME", name));
                // 事件部队名：盗贼团
                case EventType.SacredTheft: return LWNTextHelper.ResolveCompound("LWN_simulator_party_sacredtheft", ("NAME", name));
                // 事件部队名：刺客
                case EventType.Assassination: return LWNTextHelper.ResolveCompound("LWN_simulator_party_assassination", ("NAME", name));
                // 事件部队名：追捕部队
                case EventType.Fugitive: return LWNTextHelper.ResolveCompound("LWN_simulator_party_fugitive", ("TGT", tgt), ("NAME", name));
                // 事件部队名兜底
                default: return LWNTextHelper.ResolveCompound("LWN_simulator_party_default", ("NAME", name));
            }
        }

        private string GetGenericPartyName(WorldEventConfig config, Settlement settlement, Hero target)
        {
            // 地点名兜底：某地
            string loc = settlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_simulator_fallback_place", "Somewhere");
            // 目标名兜底：目标
            string tgt = target?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_simulator_fallback_target", "the target");

            switch (config.EventType)
            {
                // 通用部队名：劫掠匪帮
                case EventType.BanditRaid: return LWNTextHelper.ResolveCompound("LWN_simulator_generic_banditraid", ("LOC", loc));
                // 通用部队名：绑匪
                case EventType.Kidnapping: return LWNTextHelper.ResolveCompound("LWN_simulator_generic_kidnapping", ("TGT", tgt));
                // 通用部队名：叛变者
                case EventType.Betrayal: return LWNTextHelper.ResolveCompound("LWN_simulator_generic_betrayal", ("LOC", loc));
                // 通用部队名：催债人
                case EventType.DebtTrap: return LWNTextHelper.ResolveCompound("LWN_simulator_generic_debttrap", ("LOC", loc));
                // 通用部队名：决斗者
                case EventType.RomanticConflict: return LWNTextHelper.ResolveCompound("LWN_simulator_generic_romantic", ("LOC", loc));
                // 通用部队名：真凶
                case EventType.FalseAccusation: return LWNTextHelper.ResolveCompound("LWN_simulator_generic_falseacc", ("LOC", loc));
                // 通用部队名：赏金猎人
                case EventType.Fugitive: return LWNTextHelper.ResolveCompound("LWN_simulator_generic_fugitive", ("TGT", tgt));
                // 通用部队名：盗圣物的贼
                case EventType.SacredTheft: return LWNTextHelper.ResolveCompound("LWN_simulator_generic_sacredtheft", ("LOC", loc));
                // 通用部队名：不知名刺客
                case EventType.Assassination: return LWNTextHelper.ResolveCompound("LWN_simulator_generic_assassination", ("TGT", tgt));
                // 通用部队名：宿敌
                case EventType.NemesisRevenge: return LWNTextHelper.ResolveCompound("LWN_simulator_generic_nemesis", ("TGT", tgt));
                // 通用部队名兜底
                default: return LWNTextHelper.ResolveCompound("LWN_simulator_generic_default", ("LOC", loc));
            }
        }

        #endregion

        #region Settlement / Hero Selection

        /// <summary>
        /// 选一个定居点。村庄优先，按 prosperity 反向 + 距玩家距离加权。
        /// 距离越近、越穷越容易被选。
        /// </summary>
        private Settlement SelectTargetSettlement()
        {
            var playerPos = V.Pos(MobileParty.MainParty);

            // 候选：所有村庄（Village），排除已被同一类型事件盯上的
            var candidates = Settlement.All
                .Where(s => s.IsVillage && s.Notables != null && s.Notables.Count > 0)
                .Where(s => !WorldEventStore.GetActiveEventsNear(s, 10f)
                    .Any(e => e.Type == EventType.BanditRaid
                           && e.TargetSettlementId == s.StringId))
                .ToList();

            if (candidates.Count == 0) return null;

            // 加权：距离近 + prosperity 低 + 稳定性低 → 权重高
            return WeightedRandomSelect(candidates, s =>
            {
                float dist = playerPos.Distance(V.Pos(s));
                float distWeight = Math.Max(0.1f, 80f / Math.Max(dist, 1f));
                float prosperityWeight = Math.Max(0.2f, 5000f / Math.Max(GetSettlementProsperity(s), 100f));
                float stabilityWeight = Math.Max(0.3f, 10f - GetRegionalStability(s)); // 越低越危险
                return distWeight * 0.4f + prosperityWeight * 0.3f + stabilityWeight * 0.3f;
            });
        }

        /// <summary>从定居点名人中选受害者（非玩家、非领主、存活）。</summary>
        private Hero SelectTargetHero(Settlement settlement)
        {
            if (settlement?.Notables == null) return null;

            var candidates = settlement.Notables
                .Where(n => n != null && n.IsAlive && n != Hero.MainHero)
                .Where(n => !n.IsLord) // 不是领主
                .ToList();

            if (candidates.Count == 0)
            {
                // 如果没名人，用定居点拥有者（领主）
                if (settlement.Owner != null && settlement.Owner.IsAlive && settlement.Owner != Hero.MainHero)
                    return settlement.Owner;
                return null;
            }

            return candidates[MBRandom.RandomInt(0, candidates.Count)];
        }

        #endregion

        #region Troop Filling

        /// <summary>
        /// 从领主真实领地中抽调兵力，组建事件部队。
        ///
        /// 兵力规模按事件类型分级，绝不凭空造兵：
        ///   - 攻城级（NobleConflict）：按目标城防 × 1.5~3.0x 抽调，必须够打
        ///   - 劫掠级（BanditRaid/DebtTrap）：按目标防御 × 0.6~1.5x
        ///   - 交战级（Betrayal/Kidnapping）：按目标 Hero 部队规模匹配
        ///   - 隐蔽级（Assassination/SacredTheft/Fugitive）：小股精锐 5~25 人
        ///   - 其他：最小规模
        ///
        /// 抽不够就抽多少用多少——不造兵，世界自然演化。
        /// </summary>
        private void MobilizePartyTroops(MobileParty party, Hero leader, Settlement targetSettlement,
            Hero targetHero, WorldEventConfig config, int severity, float dayLimit = 8f)
        {
            try
            {
                party.MemberRoster.Clear();
                party.PrisonRoster.Clear();
                party.MemberRoster.AddToCounts(leader.CharacterObject, 1);

                int neededTroops;
                string scaleDesc;

                switch (config.EventType)
                {
                    // ── 攻城级：必须能威胁城池 ──
                    case EventType.NobleConflict:
                    {
                        int targetDefense = GetSettlementDefenseStrength(targetSettlement);
                        float ratio = 1.2f + severity * 0.018f; // 1.5x ~ 3.0x
                        neededTroops = Math.Max((int)(targetDefense * ratio), 20);
                        scaleDesc = $"siege: target defense={targetDefense}, ratio={ratio:F1}x → need {neededTroops}";
                        break;
                    }

                    // ── 劫掠级：能打村子但不需要攻城 ──
                    case EventType.BanditRaid:
                    case EventType.DebtTrap:
                    {
                        int targetDefense = GetSettlementDefenseStrength(targetSettlement);
                        float ratio = 0.5f + severity * 0.010f; // 0.6x ~ 1.5x
                        neededTroops = Math.Max((int)(targetDefense * ratio), 10);
                        scaleDesc = $"raid: target defense={targetDefense}, ratio={ratio:F1}x → need {neededTroops}";
                        break;
                    }

                    // ── 交战级：匹配目标 Hero 的兵力 ──
                    case EventType.Betrayal:
                    case EventType.Kidnapping:
                    {
                        int targetPartySize = targetHero?.PartyBelongedTo?.MemberRoster?.TotalManCount ?? 0;
                        if (targetPartySize > 0)
                        {
                            neededTroops = Math.Max(targetPartySize * 2 / 3, 15); // 至少对方 2/3 的兵力
                        }
                        else
                        {
                            neededTroops = (int)(15 + severity * 0.2f); // 对方没部队，少量即可
                        }
                        scaleDesc = $"engagement: target hero party size={targetPartySize} → need {neededTroops}";
                        break;
                    }

                    // ── 隐蔽级：小股精锐，隐蔽行动 ──
                    case EventType.Assassination:
                    case EventType.SacredTheft:
                    case EventType.Fugitive:
                    case EventType.RomanticConflict:
                    case EventType.InheritanceDispute:
                    {
                        neededTroops = (int)(5 + severity * 0.1f); // 5~15 人，轻装简行
                        scaleDesc = $"stealth: small elite squad → need {neededTroops}";
                        break;
                    }

                    // ── 默认：最小规模 ──
                    default:
                    {
                        neededTroops = (int)(8 + severity * 0.1f);
                        scaleDesc = $"default: minimal → need {neededTroops}";
                        break;
                    }
                }

                int mobilized = DraftTroopsFromSettlements(party, leader, neededTroops);

                if (mobilized < neededTroops / 2)
                {
                    DebugLogger.Log($"[WorldEventSimulator] WARNING: {leader.Name} only mobilized {mobilized}/{neededTroops} troops ({scaleDesc}). Party may be too weak for {config.EventType}.");
                }
                else
                {
                    DebugLogger.Log($"[WorldEventSimulator] Mobilized {mobilized} troops from {leader.Name}'s garrisons for {config.EventType} → {targetSettlement?.Name} ({scaleDesc})");
                }

                // ── 补给：事件部队从 garrison 抽调时不带食物，需手动补充以防饥饿减员 ──
                int finalTroopCount = party.MemberRoster.TotalManCount;
                if (finalTroopCount > 0)
                {
                    var foodItem = GetFoodItem();
                    int foodAmount = Math.Max(50, (int)(finalTroopCount * Math.Max(dayLimit, 3f)));
                    if (foodItem != null)
                    {
                        party.ItemRoster.AddToCounts(foodItem, foodAmount);
                        DebugLogger.Log($"[WorldEventSimulator] Supplied {foodAmount} {foodItem.Name} to {leader.Name}'s party ({finalTroopCount} troops × {dayLimit:F1} days)");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEventSimulator] MobilizePartyTroops error: {ex.Message}");
            }
        }

        /// <summary>计算定居点的防御兵力（驻军 + 城镇民兵）。</summary>
        private static int GetSettlementDefenseStrength(Settlement settlement)
        {
            if (settlement == null) return 0;
            int total = 0;
            try
            {
                var garrison = settlement.Town?.GarrisonParty;
                if (garrison != null)
                    total += garrison.MemberRoster.TotalManCount;
            }
            catch { }
            return Math.Max(total, 0);
        }

        /// <summary>
        /// 从 leader 族内各定居点驻军中抽调兵力填充 party。
        /// 优先从 leader 当前所在城抽，其次自家领地，最后族内其他城。
        /// 每城最多抽走 80% 驻军，留下至少 20% 守城。
        /// 返回实际抽调的总人数。
        /// </summary>
        private static int DraftTroopsFromSettlements(MobileParty party, Hero leader, int neededCount)
        {
            if (leader?.Clan == null || party == null) return 0;

            int drafted = 0;
            var clan = leader.Clan;

            // 收集族内所有有驻军的城镇/城堡
            var candidates = new List<Settlement>();
            foreach (var s in clan.Settlements)
            {
                if (s == null || !s.IsTown && !s.IsCastle) continue;
                if (s.Town?.GarrisonParty?.MemberRoster == null) continue;
                if (s.Town.GarrisonParty.MemberRoster.TotalManCount <= 0) continue;
                candidates.Add(s);
            }

            if (candidates.Count == 0)
            {
                DebugLogger.Log($"[WorldEventSimulator] DraftTroops: {leader.Name}'s clan '{clan.Name}' has no garrisoned settlements to draw from.");
                return 0;
            }

            // 优先排序：leader 当前所在城 > 家乡 > 驻军最多的城
            var ordered = candidates
                .OrderBy(s => s == leader.CurrentSettlement ? 0 : 1)
                .ThenBy(s => s == leader.HomeSettlement ? 0 : 1)
                .ThenByDescending(s => s.Town?.GarrisonParty?.MemberRoster?.TotalManCount ?? 0)
                .ToList();

            foreach (var settlement in ordered)
            {
                if (drafted >= neededCount) break;

                var garrisonParty = settlement.Town?.GarrisonParty;
                if (garrisonParty == null) continue;

                int totalGarrison = garrisonParty.MemberRoster.TotalManCount;
                int maxDraft = Math.Max(0, totalGarrison * 8 / 10); // 最多抽 80%
                if (maxDraft <= 0) continue;

                int toTake = Math.Min(neededCount - drafted, maxDraft);
                if (toTake <= 0) continue;

                // 逐类转移部队（从驻军 → 事件 party）
                int taken = 0;
                for (int i = 0; i < garrisonParty.MemberRoster.Count && taken < toTake; i++)
                {
                    var elem = garrisonParty.MemberRoster.GetElementCopyAtIndex(i);
                    if (elem.Character == null || elem.Number <= 0) continue;

                    int take = Math.Min(toTake - taken, elem.Number);
                    if (take <= 0) continue;

                    garrisonParty.MemberRoster.AddToCounts(elem.Character, -take);
                    party.MemberRoster.AddToCounts(elem.Character, take);
                    taken += take;
                    drafted += take;
                }

                DebugLogger.Log($"[WorldEventSimulator] Drafted {taken} troops from {settlement.Name} garrison (remaining: {garrisonParty.MemberRoster.TotalManCount})");
            }

            return drafted;
        }

        /// <summary>给通用 party（无 leader）填充部队。</summary>
        private void FillGenericPartyTroops(MobileParty party, int severity)
        {
            try
            {
                // 🐛 修复：同 FillPartyTroops，删除 InitializeMobilePartyAtPosition + Clear() 模式。
                // party 已由 CreateParty 创建为合法空 party，直接填充部队即可。
                party.MemberRoster.Clear();
                party.PrisonRoster.Clear();

                int troopCount = 5 + severity / 10 * 3;
                var looterTier1 = GetLooterTroop();
                if (looterTier1 != null)
                    party.MemberRoster.AddToCounts(looterTier1, troopCount);

                // ── 补给：通用 party 同样需要食物防止饥饿减员 ──
                var foodItem = GetFoodItem();
                int foodAmount = Math.Max(30, troopCount * 3);
                if (foodItem != null)
                    party.ItemRoster.AddToCounts(foodItem, foodAmount);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEventSimulator] FillGenericPartyTroops error: {ex.Message}");
            }
        }

        /// <summary>获取劫匪兵种（两轮策略：已知 ID → 遍历搜索）。</summary>
        private CharacterObject GetLooterTroop()
        {
            var looter = MBObjectManager.Instance.GetObject<CharacterObject>("looter");
            if (looter != null) return looter;

            return MBObjectManager.Instance.GetObject<CharacterObject>(
                co => co != null && co.IsBasicTroop
                    && co.Name?.ToString()?.IndexOf("looter", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>获取食物物品（两轮策略：已知 ID → 遍历搜索 IsFood）。</summary>
        private static ItemObject GetFoodItem()
        {
            var grain = MBObjectManager.Instance.GetObject<ItemObject>("grain");
            if (grain != null) return grain;

            return MBObjectManager.Instance.GetObject<ItemObject>(
                item => item != null && item.IsFood);
        }

        #endregion

        /// <summary>
        /// 创建通用模板 party（无 hero leader），用于 hero 无法带队时的降级方案。
        /// </summary>
        private MobileParty CreateGenericEventParty(string partyId, WorldEventConfig config,
            Settlement targetSettlement, Hero targetHero, int severity)
        {
            string nameTemplate = GetGenericPartyName(config, targetSettlement, targetHero);
            var component = new CustomPartyComponent(targetSettlement, nameTemplate);
            var party = V.MakeParty(partyId, component);
            if (party != null) V.SetPartyName(party,new TextObject(nameTemplate));
            if (party == null) return null;
            var banditClan = Clan.BanditFactions.FirstOrDefault();
            if (banditClan != null) party.ActualClan = banditClan;
            FillGenericPartyTroops(party, severity);

            V.SetPos(party, FindReachableSpawnPosition(targetSettlement));;
            return party;
        }

        #region Utility

        /// <summary>
        /// 诊断 hero 为何无法带队：返回可读原因，若 hero 可自由行动则返回 null。
        /// </summary>
        private static string GetHeroUnavailabilityReason(Hero hero)
        {
            if (hero == null) return "hero is null";
            if (!hero.IsAlive) return "hero is dead";
            if (hero.IsPrisoner) return $"hero is prisoner at {(hero.PartyBelongedToAsPrisoner?.Name?.ToString() ?? "unknown")}";
            if (hero.IsFugitive) return "hero is fugitive";
            if (hero.PartyBelongedTo != null)
            {
                if (hero.PartyBelongedTo.LeaderHero != hero)
                    return $"hero is guest in {hero.PartyBelongedTo.LeaderHero?.Name?.ToString() ?? "unknown"}'s party '{hero.PartyBelongedTo.Name}'";
                // LeaderHero == hero but we're in the fallback path — should not happen, but log
                return $"hero leads existing party '{hero.PartyBelongedTo.Name}' but LeaderHero check failed";
            }
            if (hero.CurrentSettlement != null)
                return $"hero is staying at settlement '{hero.CurrentSettlement.Name}'";
            return null; // free to create party
        }

        /// <summary>
        /// 尝试将 hero 从当前占用中解放（离开定居点/离开别人的军队），
        /// 使其可以被创建为新 party 的 leader。返回是否成功。
        /// </summary>
        private static bool TryExtractHeroForParty(Hero hero)
        {
            try
            {
                // 被俘 → 无法解放
                if (hero.IsPrisoner) return false;
                // 逃亡中 → 等待状态自然恢复
                if (hero.IsFugitive) return false;
                // 已死亡 → 不可能
                if (!hero.IsAlive) return false;

                // 在定居点中 → 移除
                if (hero.CurrentSettlement != null)
                {
                    hero.StayingInSettlement = null;
                    DebugLogger.Log($"[WorldEventSimulator] Extracted {hero.Name} from settlement for event party");
                }

                // 在别人的队伍里（非 leader）→ 从原队伍移除
                if (hero.PartyBelongedTo != null && hero.PartyBelongedTo.LeaderHero != hero)
                {
                    hero.PartyBelongedTo.MemberRoster.RemoveTroop(hero.CharacterObject);
                    DebugLogger.Log($"[WorldEventSimulator] Extracted {hero.Name} from guest role in '{hero.PartyBelongedTo.Name}' for event party");
                }

                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEventSimulator] TryExtractHeroForParty({hero?.Name}) error: {ex.Message}");
                return false;
            }
        }

        /// <summary>获取定居点繁荣度（兼容 Village 和 Town）。</summary>
        private static float GetSettlementProsperity(Settlement s)
        {
            if (s.IsVillage && s.Village != null) return s.Village.Hearth;
            if (s.IsTown && s.Town != null) return s.Town.Prosperity;
            if (s.IsCastle && s.Town != null) return s.Town.Prosperity;
            return 1000f;
        }

        /// <summary>加权随机选择。</summary>
        private T WeightedRandomSelect<T>(List<T> items, Func<T, float> weightFunc)
        {
            if (items == null || items.Count == 0) return default;

            var weights = items.Select(w => Math.Max(0.001f, weightFunc(w))).ToArray();
            float totalWeight = weights.Sum();

            float roll = MBRandom.RandomFloat * totalWeight;
            float cumulative = 0f;
            for (int i = 0; i < items.Count; i++)
            {
                cumulative += weights[i];
                if (roll <= cumulative)
                    return items[i];
            }

            return items[items.Count - 1];
        }

        /// <summary>.NET Framework 兼容的整数值夹取（Math.Clamp 不存在于 .NET 4.x）。</summary>
        private static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        #endregion

        #region Regional Stability + Event Cascading

        /// <summary>区域稳定性：按 Settlement.StringId 记录（-10 极其动荡 ~ +10 非常稳定）。</summary>
        private static Dictionary<string, int> _regionalStability = new Dictionary<string, int>();

        /// <summary>获取区域稳定性值（0 = 中性）。</summary>
        public static int GetRegionalStability(Settlement settlement)
        {
            if (settlement == null) return 0;
            _regionalStability.TryGetValue(settlement.StringId, out int v);
            return v;
        }

        /// <summary>修改区域稳定性。</summary>
        public static void ModifyStability(Settlement settlement, int delta)
        {
            if (settlement == null) return;
            int cur = GetRegionalStability(settlement);
            _regionalStability[settlement.StringId] = ClampInt(cur + delta, -10, 10);
        }

        /// <summary>
        /// 事件过期后的连锁反应：某些事件的后果会触发新事件。
        /// 例：BanditRaid 过期 → Famine；Assassination 过期 → Betrayal 或 NobleConflict。
        /// </summary>
        private void TryTriggerCascade(WorldEvent expiredEvent)
        {
            if (expiredEvent == null) return;
            Settlement settlement = expiredEvent.TargetSettlement;

            // 区域稳定性因为事件过期而恶化
            if (settlement != null)
                ModifyStability(settlement, -2);

            // 连锁规则：某些事件过期后可能触发新的相关事件
            EventType? cascadeType = null;
            float cascadeChance = 0f;

            switch (expiredEvent.Type)
            {
                case EventType.BanditRaid:
                    // 匪患摧毁了村子 → 可能引发饥荒或更多人逃亡
                    cascadeType = MBRandom.RandomFloat < 0.5f ? EventType.Famine : EventType.Fugitive;
                    cascadeChance = 0.4f;
                    break;
                case EventType.Assassination:
                    // 关键人物死亡 → 内部混乱 → 背叛或贵族冲突
                    cascadeType = MBRandom.RandomFloat < 0.5f ? EventType.Betrayal : EventType.NobleConflict;
                    cascadeChance = 0.5f;
                    break;
                case EventType.Famine:
                    // 饥荒 → 匪患（绝望的人铤而走险）或债务陷阱
                    cascadeType = EventType.BanditRaid;
                    cascadeChance = 0.35f;
                    break;
                case EventType.Kidnapping:
                    // 绑架撕票 → 冤案（家属被冤枉）或背叛（内部怀疑）
                    cascadeType = EventType.FalseAccusation;
                    cascadeChance = 0.25f;
                    break;
                case EventType.Betrayal:
                    // 背叛 → 组织分裂 → 继承争端
                    cascadeType = EventType.InheritanceDispute;
                    cascadeChance = 0.3f;
                    break;
                case EventType.NobleConflict:
                    // 贵族冲突 → 匪患（边境失控）或行刺（升级为暗杀）
                    cascadeType = MBRandom.RandomFloat < 0.6f ? EventType.BanditRaid : EventType.Assassination;
                    cascadeChance = 0.35f;
                    break;
                case EventType.SacredTheft:
                    // 圣物流失 → 内部互相指责 → 背叛或冤案
                    cascadeType = EventType.Betrayal;
                    cascadeChance = 0.3f;
                    break;
            }

            if (cascadeType.HasValue && MBRandom.RandomFloat < cascadeChance && settlement != null)
            {
                var config = WorldEventConfig.Get(cascadeType.Value);
                if (config != null && config.WeightMultiplier > 0)
                {
                    // 连锁事件：与原始事件同一定居点，较低延迟感到时间压力
                    float delay = 1f + MBRandom.RandomFloat * 2f; // 1-3 天后连锁触发
                    ScheduleCascadeEvent(config, settlement, expiredEvent, delay);
                }
            }
        }

        /// <summary>安排一个连锁事件在指定延迟后生成。</summary>
        private void ScheduleCascadeEvent(WorldEventConfig config, Settlement settlement, WorldEvent parentEvent, float delayDays)
        {
            // 序列化到 WorldEventDatabase 的 pending 列表（简单的延迟生成机制）
            // 通过检查 CreatedDay + delay 来实现：立即创建但设为未来激活
            var cascadeEvent = new WorldEvent
            {
                EventId = $"evt_cascade_{config.EventType.ToString().ToLower()}_{DateTime.UtcNow.Ticks:X8}_{MBRandom.RandomInt(10000)}",
                Type = config.EventType,
                Stage = EventStage.Active,
                TargetSettlementId = settlement.StringId,
                TargetHeroId = SelectTargetHero(settlement)?.StringId,
                OccurredDay = (float)CampaignTime.Now.ToDays + delayDays, // 未来创建日
                DayLimit = config.MinDayLimit + MBRandom.RandomFloat * (config.MaxDayLimit - config.MinDayLimit),
                Severity = Math.Max(config.MinSeverity, parentEvent.Severity - 20), // 略低于原始事件
            };

            // 选加害方
            bool isGeneric = false;
            Hero instigatorHero = null;
            SelectInstigatorBySource(config, settlement, cascadeEvent.TargetHero, out instigatorHero, out isGeneric);
            cascadeEvent.InitiatorId = instigatorHero?.StringId;
            cascadeEvent.IsGenericInstigator = isGeneric;

            if (!isGeneric && instigatorHero == null && !config.AllowGeneric)
                return; // 找不到真人就放弃连锁

            // 生成 party
            if (config.PartyBehavior != EventPartyBehavior.NoParty && config.PartyBehavior != EventPartyBehavior.ChasePlayer)
            {
                float cascadeDayLimit = cascadeEvent.DayLimit ?? 0f;
                var party = SpawnEventParty(config, settlement, cascadeEvent.TargetHero, instigatorHero, isGeneric, cascadeEvent.Severity, ref cascadeDayLimit);
                cascadeEvent.DayLimit = cascadeDayLimit;
                cascadeEvent.GeneratedPartyId = party?.StringId;
            }

            WorldEventStore.AddEvent(cascadeEvent);
            DebugLogger.Log($"[WorldEvent] Cascade: {parentEvent.Type} expired → {cascadeEvent.Type} at {settlement.Name} in {delayDays:F1} days");
        }

        /// <summary>每日稳定性自然回归（缓慢趋向中性）。</summary>
        private void StabilityDailyDecay()
        {
            var keys = _regionalStability.Keys.ToList();
            foreach (var key in keys)
            {
                int val = _regionalStability[key];
                if (val > 0)
                    _regionalStability[key] = val - 1;
                else if (val < 0)
                    _regionalStability[key] = val + 1;
                if (_regionalStability[key] == 0)
                    _regionalStability.Remove(key);
            }
        }

        /// <summary>🔴 2026-08-23（跨档残留修复）：新档创建时清空区域稳定性（static 字典，
        /// 同进程主菜单直接开新档会残留旧档数值；其余模拟状态是 behavior 实例字段，新档自动为空）。</summary>
        public static void ResetStability()
        {
            _regionalStability.Clear();
        }

        /// <summary>序列化区域稳定性为 JSON。</summary>
        public static string SerializeStability()
        {
            try { return Newtonsoft.Json.JsonConvert.SerializeObject(_regionalStability, Newtonsoft.Json.Formatting.None); }
            catch { return "{}"; }
        }

        /// <summary>从 JSON 反序列化区域稳定性。</summary>
        public static void DeserializeStability(string json)
        {
            _regionalStability.Clear();
            if (string.IsNullOrEmpty(json) || json == "{}") return;
            try
            {
                var dict = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, int>>(json);
                if (dict != null)
                    foreach (var kv in dict)
                        _regionalStability[kv.Key] = ClampInt(kv.Value, -10, 10);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEvent] DeserializeStability error: {ex.Message}");
            }
        }

        #endregion
    }
}
