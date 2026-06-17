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

        /// <summary>路途拦截 + 酒馆传闻：dt 累积到 1 秒执行一次（不再依赖游戏时间）。</summary>
        private void OnCampaignTick(float dt)
        {
            try
            {
                _roadInterceptAccumDt += dt;
                if (_roadInterceptAccumDt >= ROAD_INTERCEPT_INTERVAL_SEC)
                {
                    _roadInterceptAccumDt = 0f;
                    WorldEventDirector.CheckRoadIntercept();
                    WorldEventDirector.CheckTavernAmbientTrigger();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEventSimulator] OnCampaignTick error: {ex.Message}");
            }
        }

        #endregion

        #region Daily Tick

        private void OnDailyTick()
        {
            try
            {
                _daysSinceGameStart += 1f;

                // 1. 清理已被 AI 击败的事件 party
                WorldEventDatabase.CleanupDefeatedParties();

                // 1.5. 检查事件 party 是否已到达目标 → 到场即触发后果，不等倒计时
                CheckEventPartyArrivals();

                // 2. 检查事件升级（每 7 天未解决 → severity+1）
                CheckEventEscalation();

                // 3. 检查到期事件并施加后果
                CheckExpiredEventsWithConsequences();

                // 4. 检查宿敌复仇
                HeroNemesisTracker.CheckAndTriggerRevenge();

                // 5. 区域稳定性衰减
                StabilityDailyDecay();

                // 6. 尝试生成新事件（首次体验保障）
                TryGenerateNewEvent();

                // ── 7. 新手窗口强制事件保障 ──
                if (_tutorialEventsGenerated < MAX_TUTORIAL_EVENTS
                    && _daysSinceGameStart <= TUTORIAL_WINDOW_DAYS
                    && WorldEventDatabase.ActiveEvents.Count < 3)
                {
                    ForceGenerateTutorialEvent();
                }

                // ── 8. 定期世界摘要推送 ──
                if (_daysSinceGameStart - _lastPeriodicDigestDay >= PERIODIC_DIGEST_INTERVAL
                    && WorldEventDatabase.ActiveEvents.Count > 0)
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
            var toEscalate = WorldEventDatabase.ActiveEvents
                .Where(e =>
                {
                    float daysSinceCreation = currentDay - e.CreatedDay;
                    int expectedEscalations = (int)(daysSinceCreation / 7f);
                    return expectedEscalations > e.EscalationCount && e.Severity < 10;
                })
                .ToList();

            foreach (var evt in toEscalate)
            {
                evt.EscalationCount++;
                evt.Severity = Math.Min(10, evt.Severity + 1);

                // 扩展现有 party 兵力
                var party = evt.GeneratedParty;
                if (party != null && party.MemberRoster.TotalManCount > 0)
                {
                    var looter = GetLooterTroop();
                    if (looter != null)
                        party.MemberRoster.AddToCounts(looter, 3 + evt.Severity);
                }

                WorldEventDatabase.EscalateEvent(evt.EventId);
                DebugLogger.Log($"[WorldEventSimulator] Escalated {evt.EventType} id={evt.EventId} to severity {evt.Severity}");
            }
        }

        /// <summary>
        /// 检查事件 party 是否已到达目标定居点。
        /// 到场即触发后果——不等计时器倒计时。这才是真实的劫掠/围攻逻辑。
        /// </summary>
        private void CheckEventPartyArrivals()
        {
            var arrived = WorldEventDatabase.ActiveEvents
                .Where(e => e.GeneratedParty != null
                    && e.GeneratedParty.IsActive
                    && e.TargetSettlement != null
                    && e.GeneratedParty.Position2D.Distance(e.TargetSettlement.Position2D) < 3f) // 3 单位 ≈ 已在门口
                .ToList();

            foreach (var evt in arrived)
            {
                DebugLogger.Log($"[WorldEventSimulator] Party arrived! {evt.EventType} at {evt.TargetSettlement?.Name} — triggering consequences");
                ApplyExpiryConsequences(evt, isArrival: true);
                WorldEventDatabase.ExpireEvent(evt.EventId);

                // ── 忍者报告：部队到达！──
                float dist = MobileParty.MainParty?.Position2D.Distance(evt.TargetSettlement.Position2D) ?? float.MaxValue;
                if (dist < 100f) // 近处才有 NinjaReport，远处走 DisplayMessage（已在 ApplyExpiryConsequences 中）
                {
                    string arrivalSummary = BuildArrivalSummary(evt);
                    string fullNarrative = NotificationPipeline.BuildEventNarrativePublic(evt);
                    DebugLogger.Log($"[Player] NinjaReport(arrival): {arrivalSummary}");
                    NinjaNotificationManager.Show(arrivalSummary, () =>
                    {
                        WorldEventNotificationController.ShowEventInquiry(evt,
                            $"⚔ 部队已到达！\n\n{fullNarrative}\n\n——\n后果已经发生。");
                    });
                }
            }
        }

        /// <summary>构建到达通知摘要（TK5 忍者通报风格）。</summary>
        private static string BuildArrivalSummary(WorldEventData e)
        {
            string loc = e.TargetSettlement?.Name?.ToString() ?? "某地";
            string instigator = e.IsGenericInstigator ? "一伙歹徒" : (e.InstigatorHero?.Name?.ToString() ?? "加害方");
            string victim = e.TargetHero?.Name?.ToString() ?? loc;

            return e.EventType switch
            {
                WorldEventType.BanditRaid => $"⚔ {instigator} 已抵达{loc}——劫掠开始！",
                WorldEventType.Kidnapping => $"⚔ {instigator} 带走了{victim}——绑匪已经得手！",
                WorldEventType.NobleConflict => $"⚔ {instigator} 的军队已开进{loc}——与{victim}短兵相接！",
                WorldEventType.Assassination => $"🗡 {victim}遇刺——{instigator}的刺客在{loc}得手了……",
                WorldEventType.SacredTheft => $"🔮 {instigator} 已从{loc}带走圣物——传承断绝。",
                WorldEventType.Betrayal => $"💔 {instigator} 背叛了{victim}——事成定局。",
                WorldEventType.Famine => $"⚠ {loc}粮食耗尽——饥荒已至。",
                _ => $"⚔ {instigator} 的行动已在{loc}得手。"
            };
        }

        /// <summary>检查到期事件，施加各类型的物理后果。</summary>
        private void CheckExpiredEventsWithConsequences()
        {
            var expired = WorldEventDatabase.ActiveEvents
                .Where(e => e.IsExpired)
                .ToList();

            foreach (var evt in expired)
            {
                ApplyExpiryConsequences(evt);
                WorldEventDatabase.ExpireEvent(evt.EventId);
            }
        }

        /// <summary>到期事件的物理后果（玩家未解决 = 加害方达成目标）+ 玩家通知。</summary>
        private void ApplyExpiryConsequences(WorldEventData evt, bool isArrival = false)
        {
            if (evt == null) return;

            Settlement settlement = evt.TargetSettlement;
            Hero targetHero = evt.TargetHero;
            string loc = settlement?.Name?.ToString() ?? "某地";
            string victim = targetHero?.Name?.ToString() ?? "村民";
            string instigator = evt.IsGenericInstigator ? "一伙歹徒" : (evt.InstigatorHero?.Name?.ToString() ?? "加害方");

            string playerMsg = null;

            switch (evt.EventType)
            {
                case WorldEventType.BanditRaid:
                    if (settlement?.Village != null)
                    {
                        settlement.Village.Hearth = Math.Max(0, settlement.Village.Hearth - 30);
                        playerMsg = isArrival
                            ? $"⚔ {instigator}的部队已经抵达{loc}——劫掠开始了！村民们四散奔逃……"
                            : $"噩耗传来——{instigator}劫掠了{loc}！村子损失惨重，百姓流离失所。";
                    }
                    break;

                case WorldEventType.Kidnapping:
                    if (targetHero != null && targetHero.IsAlive && !targetHero.IsLord)
                    {
                        string name = targetHero.Name?.ToString() ?? "人质";
                        KillCharacterAction.ApplyByMurder(null, targetHero, true);
                        playerMsg = isArrival
                            ? $"⚔ {instigator}已经绑走了{name}——{victim}的家人绝望地看着匪徒扬长而去……"
                            : $"噩耗——{name}被绑匪撕票了。赎金没来得及送到……{loc}的百姓悲愤交加。";
                    }
                    break;

                case WorldEventType.Betrayal:
                    if (evt.InstigatorHero != null && targetHero != null)
                    {
                        int stolen = targetHero.Gold / 2;
                        AgentControlHelper.TransferGold(targetHero, evt.InstigatorHero, stolen);
                        playerMsg = $"消息传来——{victim}被{instigator}背叛了！多年积蓄被卷走，信任化为乌有。";
                    }
                    break;

                case WorldEventType.DebtTrap:
                    if (targetHero != null)
                    {
                        AgentControlHelper.TransferGold(targetHero, null, targetHero.Gold / 3);
                        playerMsg = $"{victim}的地契被{instigator}收走了——一家人失去了安身之所。债主们如愿以偿。";
                    }
                    break;

                case WorldEventType.Famine:
                    if (settlement?.Village != null)
                    {
                        settlement.Village.Hearth = Math.Max(0, settlement.Village.Hearth - 50);
                        playerMsg = $"{loc}的饥荒已经到了极限——粮食耗尽，饿殍遍野。没人能救得了他们了。";
                    }
                    break;

                case WorldEventType.Assassination:
                    if (targetHero != null && targetHero.IsAlive)
                    {
                        string name = targetHero.Name?.ToString() ?? "重要人物";
                        KillCharacterAction.ApplyByMurder(null, targetHero, true);
                        playerMsg = isArrival
                            ? $"⚔ {name}被刺杀了——{instigator}的刺客成功潜入{loc}！人们震惊地盯着尸体，不敢出声。"
                            : $"震惊——{name}被刺杀了！{loc}陷入混乱，人人自危。刺客已经不见踪影。";
                    }
                    break;

                case WorldEventType.SacredTheft:
                    if (settlement != null)
                    {
                        SettlementHonorStore.Modify(settlement, -5);
                        playerMsg = isArrival
                            ? $"⚔ {instigator}的人摸进了{loc}的祠堂——圣物被带走了！族老们跪在地上，耻辱刻进了族谱。"
                            : $"{loc}的圣物没能追回来——祖宗的传承断了。族老们低下了头，耻辱刻进了族谱。";
                    }
                    break;

                case WorldEventType.NobleConflict:
                    if (evt.InstigatorHero != null && targetHero != null)
                    {
                        ChangeRelationAction.ApplyPlayerRelation(evt.InstigatorHero, -10);
                        ChangeRelationAction.ApplyPlayerRelation(targetHero, -10);
                        playerMsg = isArrival
                            ? $"⚔ {instigator}的军队开到了{loc}——与{victim}的部队短兵相接！边境烽火已经点燃。"
                            : $"{instigator}与{victim}的矛盾彻底爆发了！双方在{loc}边境兵戎相见，血流成河。";
                    }
                    break;

                case WorldEventType.Fugitive:
                    if (targetHero != null && targetHero.IsAlive)
                    {
                        string name = targetHero.Name?.ToString() ?? "逃犯";
                        playerMsg = $"{name}的踪迹彻底断了——也许是逃走了，也许是被人抓回去了。{loc}又恢复了表面的平静。";
                    }
                    break;

                case WorldEventType.RomanticConflict:
                    if (targetHero != null)
                    {
                        playerMsg = $"{victim}的心被伤透了——那场决斗没有赢家。{loc}的人茶余饭后又多了一段谈资。";
                    }
                    break;

                case WorldEventType.FalseAccusation:
                    if (targetHero != null)
                    {
                        playerMsg = $"{victim}被定罪了——证据始终没能找到。{loc}少了一个清白的人，多了一个冤魂。";
                    }
                    break;

                case WorldEventType.InheritanceDispute:
                    playerMsg = $"{loc}的继承之争尘埃落定——但不是通过法理，而是通过拳头。家族的裂痕怕是永远无法弥合了。";
                    break;

                case WorldEventType.TradeDispute:
                    playerMsg = $"{loc}的市场被{instigator}垄断了——小商人们破产的破产，远走他乡的远走他乡。";
                    break;

                default:
                    playerMsg = $"{loc}的危机没能得到解决——事情正在向最坏的方向发展。";
                    break;
            }

            if (!string.IsNullOrEmpty(playerMsg))
                InformationManager.DisplayMessage(new InformationMessage(playerMsg));

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
            if (WorldEventDatabase.ActiveEvents.Count >= MAX_ACTIVE_EVENTS)
                return;

            // ── 第一优先：真人动机驱动的事件 ──
            if (TryGenerateMotivatedEvent())
                return; // 已生成真人冲突事件，本日不再额外 roll

            // ── 第二优先：随机事件（含通用模板）──
            float roll = MBRandom.RandomFloat;
            float probability = BASE_DAILY_PROBABILITY * GetRegionWeight();

            bool inTutorial = _daysSinceGameStart <= TUTORIAL_WINDOW_DAYS && WorldEventDatabase.ActiveEvents.Count < 3;
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

                WorldEventType eventType;
                if (isRuthless && relation <= -40 && MBRandom.RandomFloat < 0.4f)
                    eventType = WorldEventType.Assassination; // 冷酷 + 深仇 → 刺杀
                else if (isSchemer && hated.Clan?.Tier >= 3)
                    eventType = WorldEventType.SacredTheft;    // 精明 + 对方是大族 → 偷圣物
                else
                    eventType = WorldEventType.NobleConflict;  // 常规冲突

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

                Hero targetHero = null;
                if (config.TargetsHero)
                {
                    targetHero = hated; // 直接用恨的对象
                    if (targetHero == null) continue;
                }

                int severity = ClampInt(3 + (int)(Math.Abs(relation) / 10f), config.MinSeverity, config.MaxSeverity);
                float dayLimit = config.MinDayLimit + MBRandom.RandomFloat * (config.MaxDayLimit - config.MinDayLimit);

                MobileParty eventParty = SpawnEventParty(config, targetSettlement, targetHero, instigator, isGeneric: false, severity, ref dayLimit);
                if (eventParty == null && config.PartyBehavior != EventPartyBehavior.NoParty) continue;

                string eventId = $"evt_motiv_{eventType.ToString().ToLower()}_{DateTime.UtcNow.Ticks:X8}_{MBRandom.RandomInt(10000)}";

                var worldEvent = new WorldEventData
                {
                    EventId = eventId,
                    EventType = eventType,
                    Status = WorldEventStatus.Active,
                    TargetHeroId = targetHero?.StringId,
                    TargetSettlementId = targetSettlement.StringId,
                    InstigatorHeroId = instigator.StringId,
                    IsGenericInstigator = false,
                    GeneratedPartyId = eventParty?.StringId,
                    CreatedDay = (float)CampaignTime.Now.ToDays,
                    DayLimit = dayLimit,
                    Severity = severity,
                };
                worldEvent.IsRedirectedExistingParty = eventParty != null && instigator.PartyBelongedTo == eventParty;

                WorldEventDatabase.AddEvent(worldEvent);
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

                var config = WorldEventConfig.Get(WorldEventType.Betrayal);
                if (config == null) continue;

                Settlement targetSettlement = betrayed.CurrentSettlement ?? betrayed.HomeSettlement;
                if (targetSettlement == null) continue;

                int severity = 5 + MBRandom.RandomInt(0, 3);
                float dayLimit = config.MinDayLimit + MBRandom.RandomFloat * (config.MaxDayLimit - config.MinDayLimit);

                MobileParty eventParty = SpawnEventParty(config, targetSettlement, betrayed, instigator, isGeneric: false, severity, ref dayLimit);
                if (eventParty == null) continue;

                string eventId = $"evt_motiv_betrayal_{DateTime.UtcNow.Ticks:X8}_{MBRandom.RandomInt(10000)}";

                var worldEvent = new WorldEventData
                {
                    EventId = eventId,
                    EventType = WorldEventType.Betrayal,
                    Status = WorldEventStatus.Active,
                    TargetHeroId = betrayed.StringId,
                    TargetSettlementId = targetSettlement.StringId,
                    InstigatorHeroId = instigator.StringId,
                    IsGenericInstigator = false,
                    GeneratedPartyId = eventParty.StringId,
                    CreatedDay = (float)CampaignTime.Now.ToDays,
                    DayLimit = dayLimit,
                    Severity = severity,
                };
                worldEvent.IsRedirectedExistingParty = eventParty != null && instigator.PartyBelongedTo == eventParty;

                WorldEventDatabase.AddEvent(worldEvent);
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

                WorldEventType econType = instigator.Occupation == Occupation.GangLeader
                    ? WorldEventType.DebtTrap
                    : WorldEventType.TradeDispute;

                var config = WorldEventConfig.Get(econType);
                if (config == null) continue;

                int severity = MBRandom.RandomInt(2, 5);
                float dayLimit = config.MinDayLimit + MBRandom.RandomFloat * (config.MaxDayLimit - config.MinDayLimit);

                MobileParty eventParty = null;
                if (config.PartyBehavior != EventPartyBehavior.NoParty)
                    eventParty = SpawnEventParty(config, home, victim, instigator, isGeneric: false, severity, ref dayLimit);

                string eventId = $"evt_motiv_{econType.ToString().ToLower()}_{DateTime.UtcNow.Ticks:X8}_{MBRandom.RandomInt(10000)}";

                var worldEvent = new WorldEventData
                {
                    EventId = eventId,
                    EventType = econType,
                    Status = WorldEventStatus.Active,
                    TargetHeroId = victim.StringId,
                    TargetSettlementId = home.StringId,
                    InstigatorHeroId = instigator.StringId,
                    IsGenericInstigator = false,
                    GeneratedPartyId = eventParty?.StringId,
                    CreatedDay = (float)CampaignTime.Now.ToDays,
                    DayLimit = dayLimit,
                    Severity = severity,
                };
                worldEvent.IsRedirectedExistingParty = eventParty != null && instigator.PartyBelongedTo == eventParty;

                WorldEventDatabase.AddEvent(worldEvent);
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
                            && s.Position2D.Distance(home.Position2D) < 80f)
                        .OrderBy(_ => MBRandom.RandomFloat)
                        .FirstOrDefault();

                    if (targetSettlement == null) continue;

                    WorldEventType expType = calculating >= 1
                        ? WorldEventType.SacredTheft  // 精明 → 偷圣物打击对方文化
                        : WorldEventType.NobleConflict; // 勇敢 → 正面冲突

                    var config = WorldEventConfig.Get(expType);
                    if (config == null) continue;

                    Hero targetHero = null;
                    if (config.TargetsHero)
                    {
                        targetHero = SelectTargetHero(targetSettlement);
                        if (targetHero == null) continue;
                    }

                    int severity = MBRandom.RandomInt(3, 7);
                    float dayLimit = config.MinDayLimit + MBRandom.RandomFloat * (config.MaxDayLimit - config.MinDayLimit);

                    MobileParty eventParty = SpawnEventParty(config, targetSettlement, targetHero, instigator, isGeneric: false, severity, ref dayLimit);
                    if (eventParty == null && config.PartyBehavior != EventPartyBehavior.NoParty) continue;

                    string eventId = $"evt_motiv_exp_{expType.ToString().ToLower()}_{DateTime.UtcNow.Ticks:X8}_{MBRandom.RandomInt(10000)}";

                    var worldEvent = new WorldEventData
                    {
                        EventId = eventId,
                        EventType = expType,
                        Status = WorldEventStatus.Active,
                        TargetHeroId = targetHero?.StringId,
                        TargetSettlementId = targetSettlement.StringId,
                        InstigatorHeroId = instigator.StringId,
                        IsGenericInstigator = false,
                        GeneratedPartyId = eventParty?.StringId,
                        CreatedDay = (float)CampaignTime.Now.ToDays,
                        DayLimit = dayLimit,
                        Severity = severity,
                    };
                    worldEvent.IsRedirectedExistingParty = eventParty != null && instigator.PartyBelongedTo == eventParty;

                    WorldEventDatabase.AddEvent(worldEvent);
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
            var config = WorldEventConfig.Get(WorldEventType.BanditRaid);
            if (config == null) return;

            // 优先选玩家附近的定居点
            Settlement targetSettlement = SelectSettlementNearPlayer(40f)
                ?? SelectTargetSettlement();
            if (targetSettlement == null) return;

            Hero targetHero = SelectTargetHero(targetSettlement);
            Hero instigatorHero = null;
            bool isGeneric = false;
            SelectInstigatorBySource(config, targetSettlement, targetHero, out instigatorHero, out isGeneric);

            int severity = MBRandom.RandomInt(2, 4); // 新手友好的低严重度
            float dayLimit = 5f + MBRandom.RandomFloat * 5f; // 5-10 天，足够宽裕

            MobileParty eventParty = SpawnEventParty(config, targetSettlement, targetHero, instigatorHero, isGeneric, severity, ref dayLimit);
            if (eventParty == null) return;

            string eventId = $"evt_tutorial_{config.EventType.ToString().ToLower()}_{DateTime.UtcNow.Ticks:X8}_{MBRandom.RandomInt(10000)}";

            var worldEvent = new WorldEventData
            {
                EventId = eventId,
                EventType = config.EventType,
                Status = WorldEventStatus.Active,
                TargetHeroId = targetHero?.StringId,
                TargetSettlementId = targetSettlement.StringId,
                InstigatorHeroId = instigatorHero?.StringId,
                IsGenericInstigator = isGeneric,
                GeneratedPartyId = eventParty.StringId,
                CreatedDay = (float)CampaignTime.Now.ToDays,
                DayLimit = dayLimit,
                Severity = severity,
            };

            WorldEventDatabase.AddEvent(worldEvent);
            _tutorialEventsGenerated++;
        }

        /// <summary>选玩家附近指定距离内的定居点（新手引导用）。</summary>
        private Settlement SelectSettlementNearPlayer(float maxDistance)
        {
            var playerPos = MobileParty.MainParty.Position2D;
            return Settlement.All
                .Where(s => s.IsVillage && s.Notables != null && s.Notables.Count > 0
                    && s.Position2D.Distance(playerPos) < maxDistance)
                .OrderBy(s => s.Position2D.Distance(playerPos))
                .FirstOrDefault();
        }

        /// <summary>
        /// 【控制台调试】强制在玩家附近生成一个指定类型的世界事件。
        /// 调用方：MyCommands.worldevent_force
        /// </summary>
        /// <param name="eventType">事件类型，默认 BanditRaid</param>
        /// <param name="severity">严重度，-1=随机(2-5)</param>
        /// <returns>生成结果描述</returns>
        public static string ForceGenerateEvent(WorldEventType eventType = WorldEventType.BanditRaid, int severity = -1)
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
                var playerPos = MobileParty.MainParty.Position2D;
                targetSettlement = Settlement.All
                    .Where(s => s.IsVillage && s.Notables?.Count > 0)
                    .OrderBy(s => s.Position2D.Distance(playerPos))
                    .FirstOrDefault();
            }
            if (targetSettlement == null)
                return "Error: No suitable settlement found.";

            Hero targetHero = null;
            if (config.TargetsHero && simulator != null)
                targetHero = simulator.SelectTargetHero(targetSettlement);

            Hero instigatorHero = null;
            bool isGeneric = false;
            if (simulator != null)
                simulator.SelectInstigatorBySource(config, targetSettlement, targetHero, out instigatorHero, out isGeneric);

            int sev = severity > 0 ? Math.Min(severity, 10) : MBRandom.RandomInt(2, 6);
            float dayLimit = config.MinDayLimit + MBRandom.RandomFloat * (config.MaxDayLimit - config.MinDayLimit);

            MobileParty eventParty = null;
            if (simulator != null && config.PartyBehavior != EventPartyBehavior.NoParty)
                eventParty = simulator.SpawnEventParty(config, targetSettlement, targetHero, instigatorHero, isGeneric, sev, ref dayLimit);

            string eventId = $"evt_cmd_{eventType.ToString().ToLower()}_{DateTime.UtcNow.Ticks:X8}_{MBRandom.RandomInt(10000)}";

            var worldEvent = new WorldEventData
            {
                EventId = eventId,
                EventType = config.EventType,
                Status = WorldEventStatus.Active,
                TargetHeroId = targetHero?.StringId,
                TargetSettlementId = targetSettlement.StringId,
                InstigatorHeroId = instigatorHero?.StringId,
                IsGenericInstigator = isGeneric,
                GeneratedPartyId = eventParty?.StringId,
                CreatedDay = (float)CampaignTime.Now.ToDays,
                DayLimit = dayLimit,
                Severity = sev,
            };

            WorldEventDatabase.AddEvent(worldEvent);
            return $"OK: {eventType} at {targetSettlement.Name} sev={sev} party={eventParty != null} hero={instigatorHero?.Name?.ToString() ?? "generic"}";
        }

        /// <summary>数据驱动的通用事件生成。</summary>
        private void TryGenerateEvent(WorldEventConfig config)
        {
            if (config == null) return;

            // 1. 选定居点
            Settlement targetSettlement = SelectTargetSettlement();
            if (targetSettlement == null) return;

            // 2. 选受害者（根据配置决定是 Hero 还是 Settlement 本身）
            Hero targetHero = null;
            if (config.TargetsHero)
            {
                targetHero = SelectTargetHero(targetSettlement);
                if (targetHero == null) return; // 找不到真人 → 不生成
            }

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
            var worldEvent = new WorldEventData
            {
                EventId = eventId,
                EventType = config.EventType,
                Status = WorldEventStatus.Active,
                TargetHeroId = targetHero?.StringId,
                TargetSettlementId = targetSettlement.StringId,
                InstigatorHeroId = instigatorHero?.StringId,
                IsGenericInstigator = isGeneric,
                GeneratedPartyId = eventParty?.StringId,
                CreatedDay = (float)CampaignTime.Now.ToDays,
                DayLimit = dayLimit,
                Severity = severity,
            };

            // 8. 存入数据库
            WorldEventDatabase.AddEvent(worldEvent);

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
                    && s.Position2D.Distance(MobileParty.MainParty.Position2D) < 100f);
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
                && s.Position2D.Distance(target.Position2D) < 150f)
                .OrderBy(s => s.Position2D.Distance(target.Position2D)))
            {
                foreach (var clan in Clan.BanditFactions)
                {
                    if (clan == null) continue;
                    foreach (var hero in clan.Heroes)
                    {
                        if (hero == null || !hero.IsAlive || IsHeroBusyInEvent(hero.StringId)) continue;
                        if (hero.CurrentSettlement == hideout
                            || (hero.PartyBelongedTo != null
                                && hero.PartyBelongedTo.Position2D.Distance(hideout.Position2D) < 80f))
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
            return enemies.OrderBy(h => target.Position2D.Distance(
                h.CurrentSettlement?.Position2D ?? h.HomeSettlement?.Position2D ?? target.Position2D)).FirstOrDefault();
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
            return WorldEventDatabase.ActiveEvents.Any(e =>
                e.InstigatorHeroId == heroId || e.TargetHeroId == heroId);
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
            if (DEBUG_DISABLE_PARTY_SPAWN)
            {
                DebugLogger.Log($"[WorldEventSimulator] DEBUG: party spawn SKIPPED for {config.EventType} (DEBUG_DISABLE_PARTY_SPAWN=true)");
                return null;
            }

            try
            {
                string prefix = config.EventType.ToString().ToLower();
                string partyId = $"lwn_{prefix}_{targetSettlement.StringId}_{MBRandom.RandomInt(10000)}";
                MobileParty party;
                bool isRedirected = false; // 征用真人部队时不锁 AI

                // ── 真人 instigator：优先征用其现有部队，而非另建幽灵 party ──
                if (!isGeneric && instigatorHero != null)
                {
                    MobileParty existingParty = instigatorHero.PartyBelongedTo;

                    if (existingParty != null && existingParty.IsActive && existingParty.LeaderHero == instigatorHero)
                    {
                        // instigator 本人正带队 → 直接调遣他的真实部队去打目标！
                        party = existingParty;
                        isRedirected = true;
                        party.SetCustomName(new TextObject(
                            GetPartyNameTemplate(config, instigatorHero, targetSettlement, targetHero)));
                        party.Ai.SetDoNotMakeNewDecisions(false); // 解锁 AI，允许我们下达新指令

                        // 加速行军：临时大幅提升 Scouting 技能，让部队快速赶到目标
                        instigatorHero.SetSkillValue(DefaultSkills.Scouting, 300);

                        // 根据行军距离自动延长事件过期时间（防止 lord 还没走到就过期了）
                        float distToTarget = party.Position2D.Distance(targetSettlement.Position2D);
                        float speedEstimate = party.Speed > 0.1f ? party.Speed : 2.5f;
                        float travelDays = distToTarget / (speedEstimate * 24f);
                        float minTotalDays = travelDays + 4f;
                        if (minTotalDays > dayLimit)
                            dayLimit = Math.Min(minTotalDays, 30f);
                        DebugLogger.Log($"[WorldEventSimulator] Redirecting party of {instigatorHero.Name} → {targetSettlement.Name} (dist={distToTarget:F0}, boosted)");
                    }
                    else
                    {
                        // instigator 没有自己的队伍（在定居点 / 在别人军队里）→ 新建
                        if (instigatorHero.Clan == null)
                            instigatorHero.Clan = Clan.BanditFactions.FirstOrDefault() ?? Clan.PlayerClan;

                        var component = new SafeLordPartyComponent(instigatorHero);
                        string nameTemplate = GetPartyNameTemplate(config, instigatorHero, targetSettlement, targetHero);
                        party = MobileParty.CreateParty(partyId, component,
                            delegate (MobileParty p) { p.SetCustomName(new TextObject(nameTemplate)); });
                        if (party == null) return null;
                        party.ActualClan = instigatorHero.Clan;
                        FillPartyTroops(party, instigatorHero, severity);

                        // 定位在目标附近
                        Vec2 offset = new Vec2((MBRandom.RandomFloat - 0.5f) * 20f, (MBRandom.RandomFloat - 0.5f) * 20f);
                        party.Position2D = targetSettlement.Position2D + offset;
                    }
                }
                else
                {
                    string nameTemplate = GetGenericPartyName(config, targetSettlement, targetHero);
                    var component = new CustomPartyComponent(targetSettlement, nameTemplate);
                    party = MobileParty.CreateParty(partyId, component,
                        delegate (MobileParty p) { p.SetCustomName(new TextObject(nameTemplate)); });
                    if (party == null) return null;
                    var banditClan = Clan.BanditFactions.FirstOrDefault();
                    if (banditClan != null) party.ActualClan = banditClan;
                    FillGenericPartyTroops(party, severity);

                    // 定位
                    Vec2 offset = new Vec2((MBRandom.RandomFloat - 0.5f) * 20f, (MBRandom.RandomFloat - 0.5f) * 20f);
                    party.Position2D = targetSettlement.Position2D + offset;
                }

                // AI 行为
                switch (config.PartyBehavior)
                {
                    case EventPartyBehavior.RaidSettlement:
                        party.Ai.SetMoveGoToSettlement(targetSettlement);
                        break;
                    case EventPartyBehavior.EngageTarget:
                        if (targetHero?.PartyBelongedTo != null)
                            party.Ai.SetMoveEngageParty(targetHero.PartyBelongedTo);
                        else
                            party.Ai.SetMoveGoToSettlement(targetSettlement);
                        break;
                    case EventPartyBehavior.PatrolNearTarget:
                        party.Ai.SetMovePatrolAroundPoint(party.Position2D);
                        break;
                    case EventPartyBehavior.ChasePlayer:
                        party.Ai.SetMoveEngageParty(MobileParty.MainParty);
                        break;
                }
                party.Ai.SetDoNotMakeNewDecisions(!isRedirected); // 征用部队不锁 AI，让它们自主战斗
                party.SetPartyUsedByQuest(true);
                party.Party.SetVisualAsDirty();

                return party;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEventSimulator] SpawnEventParty error: {ex.Message}");
                return null;
            }
        }

        private string GetPartyNameTemplate(WorldEventConfig config, Hero instigator, Settlement settlement, Hero target)
        {
            string name = instigator?.Name?.ToString() ?? "某人";
            string loc = settlement?.Name?.ToString() ?? "某地";
            string tgt = target?.Name?.ToString() ?? "目标";

            switch (config.EventType)
            {
                case WorldEventType.BanditRaid: return $"{name}的劫掠队";
                case WorldEventType.Kidnapping: return $"{name}的绑匪帮";
                case WorldEventType.Betrayal: return $"{name}的叛军";
                case WorldEventType.DebtTrap: return $"{name}的讨债队";
                case WorldEventType.NobleConflict: return $"{name}的征讨军";
                case WorldEventType.SacredTheft: return $"{name}的盗贼团";
                case WorldEventType.Assassination: return $"{name}的刺客";
                case WorldEventType.Fugitive: return $"追捕{tgt}的{name}部队";
                default: return $"{name}的部队";
            }
        }

        private string GetGenericPartyName(WorldEventConfig config, Settlement settlement, Hero target)
        {
            string loc = settlement?.Name?.ToString() ?? "某地";
            string tgt = target?.Name?.ToString() ?? "目标";

            switch (config.EventType)
            {
                case WorldEventType.BanditRaid: return $"劫掠{loc}的匪帮";
                case WorldEventType.Kidnapping: return $"绑走{tgt}的匪徒";
                case WorldEventType.Betrayal: return $"{loc}的叛变者";
                case WorldEventType.DebtTrap: return $"{loc}的催债人";
                case WorldEventType.RomanticConflict: return $"{loc}的决斗者";
                case WorldEventType.FalseAccusation: return $"{loc}的真凶";
                case WorldEventType.Fugitive: return $"追捕{tgt}的赏金猎人";
                case WorldEventType.SacredTheft: return $"偷走{loc}圣物的盗贼";
                case WorldEventType.Assassination: return $"刺杀{tgt}的不知名刺客";
                case WorldEventType.NemesisRevenge: return $"{tgt}的宿敌";
                default: return $"{loc}的事件部队";
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
            var playerPos = MobileParty.MainParty.Position2D;

            // 候选：所有村庄（Village），排除已被同一类型事件盯上的
            var candidates = Settlement.All
                .Where(s => s.IsVillage && s.Notables != null && s.Notables.Count > 0)
                .Where(s => !WorldEventDatabase.GetActiveEventsNear(s, 10f)
                    .Any(e => e.EventType == WorldEventType.BanditRaid
                           && e.TargetSettlementId == s.StringId))
                .ToList();

            if (candidates.Count == 0) return null;

            // 加权：距离近 + prosperity 低 + 稳定性低 → 权重高
            return WeightedRandomSelect(candidates, s =>
            {
                float dist = playerPos.Distance(s.Position2D);
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

        /// <summary>给有 leader 的 party 填充部队。</summary>
        private void FillPartyTroops(MobileParty party, Hero leader, int severity)
        {
            try
            {
                // 🐛 修复：删除 InitializeMobilePartyAtPosition + Clear() 模式。
                // InitializeMobilePartyAtPosition 是 native 方法，按模板在本地分配 roster 内存；
                // 紧接着 Clear() 只清空管理侧列表，本地内存大小不变；
                // 后续 AddToCounts 写入不同数量 → 本地 buffer 大小不匹配 → 引擎更新时栈溢出 (0xc00000ff)。
                // 正确做法：party 已由 CreateParty 创建，直接 Clear + AddToCounts 即可。
                party.MemberRoster.Clear();
                party.PrisonRoster.Clear();
                party.MemberRoster.AddToCounts(leader.CharacterObject, 1);

                int troopCount = 3 + severity * 2;
                var basicTroop = leader.Culture?.BasicTroop;
                if (basicTroop != null)
                    party.MemberRoster.AddToCounts(basicTroop, troopCount);

                if (severity >= 5)
                {
                    var eliteTroop = leader.Culture?.EliteBasicTroop ?? basicTroop;
                    if (eliteTroop != null)
                        party.MemberRoster.AddToCounts(eliteTroop, severity - 4);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEventSimulator] FillPartyTroops error: {ex.Message}");
            }
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

                int troopCount = 5 + severity * 3;
                var looterTier1 = GetLooterTroop();
                if (looterTier1 != null)
                    party.MemberRoster.AddToCounts(looterTier1, troopCount);
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

        #endregion

        #region Utility

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
        private void TryTriggerCascade(WorldEventData expiredEvent)
        {
            if (expiredEvent == null) return;
            Settlement settlement = expiredEvent.TargetSettlement;

            // 区域稳定性因为事件过期而恶化
            if (settlement != null)
                ModifyStability(settlement, -2);

            // 连锁规则：某些事件过期后可能触发新的相关事件
            WorldEventType? cascadeType = null;
            float cascadeChance = 0f;

            switch (expiredEvent.EventType)
            {
                case WorldEventType.BanditRaid:
                    // 匪患摧毁了村子 → 可能引发饥荒或更多人逃亡
                    cascadeType = MBRandom.RandomFloat < 0.5f ? WorldEventType.Famine : WorldEventType.Fugitive;
                    cascadeChance = 0.4f;
                    break;
                case WorldEventType.Assassination:
                    // 关键人物死亡 → 内部混乱 → 背叛或贵族冲突
                    cascadeType = MBRandom.RandomFloat < 0.5f ? WorldEventType.Betrayal : WorldEventType.NobleConflict;
                    cascadeChance = 0.5f;
                    break;
                case WorldEventType.Famine:
                    // 饥荒 → 匪患（绝望的人铤而走险）或债务陷阱
                    cascadeType = WorldEventType.BanditRaid;
                    cascadeChance = 0.35f;
                    break;
                case WorldEventType.Kidnapping:
                    // 绑架撕票 → 冤案（家属被冤枉）或背叛（内部怀疑）
                    cascadeType = WorldEventType.FalseAccusation;
                    cascadeChance = 0.25f;
                    break;
                case WorldEventType.Betrayal:
                    // 背叛 → 组织分裂 → 继承争端
                    cascadeType = WorldEventType.InheritanceDispute;
                    cascadeChance = 0.3f;
                    break;
                case WorldEventType.NobleConflict:
                    // 贵族冲突 → 匪患（边境失控）或行刺（升级为暗杀）
                    cascadeType = MBRandom.RandomFloat < 0.6f ? WorldEventType.BanditRaid : WorldEventType.Assassination;
                    cascadeChance = 0.35f;
                    break;
                case WorldEventType.SacredTheft:
                    // 圣物流失 → 内部互相指责 → 背叛或冤案
                    cascadeType = WorldEventType.Betrayal;
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
        private void ScheduleCascadeEvent(WorldEventConfig config, Settlement settlement, WorldEventData parentEvent, float delayDays)
        {
            // 序列化到 WorldEventDatabase 的 pending 列表（简单的延迟生成机制）
            // 通过检查 CreatedDay + delay 来实现：立即创建但设为未来激活
            var cascadeEvent = new WorldEventData
            {
                EventId = $"evt_cascade_{config.EventType.ToString().ToLower()}_{DateTime.UtcNow.Ticks:X8}_{MBRandom.RandomInt(10000)}",
                EventType = config.EventType,
                Status = WorldEventStatus.Active,
                TargetSettlementId = settlement.StringId,
                TargetHeroId = SelectTargetHero(settlement)?.StringId,
                CreatedDay = (float)CampaignTime.Now.ToDays + delayDays, // 未来创建日
                DayLimit = config.MinDayLimit + MBRandom.RandomFloat * (config.MaxDayLimit - config.MinDayLimit),
                Severity = Math.Max(config.MinSeverity, parentEvent.Severity - 2), // 略低于原始事件
            };

            // 选加害方
            bool isGeneric = false;
            Hero instigatorHero = null;
            SelectInstigatorBySource(config, settlement, cascadeEvent.TargetHero, out instigatorHero, out isGeneric);
            cascadeEvent.InstigatorHeroId = instigatorHero?.StringId;
            cascadeEvent.IsGenericInstigator = isGeneric;

            if (!isGeneric && instigatorHero == null && !config.AllowGeneric)
                return; // 找不到真人就放弃连锁

            // 生成 party
            if (config.PartyBehavior != EventPartyBehavior.NoParty && config.PartyBehavior != EventPartyBehavior.ChasePlayer)
            {
                float cascadeDayLimit = cascadeEvent.DayLimit;
                var party = SpawnEventParty(config, settlement, cascadeEvent.TargetHero, instigatorHero, isGeneric, cascadeEvent.Severity, ref cascadeDayLimit);
                cascadeEvent.DayLimit = cascadeDayLimit;
                cascadeEvent.GeneratedPartyId = party?.StringId;
            }

            WorldEventDatabase.AddEvent(cascadeEvent);
            DebugLogger.Log($"[WorldEvent] Cascade: {parentEvent.EventType} expired → {cascadeEvent.EventType} at {settlement.Name} in {delayDays:F1} days");
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
