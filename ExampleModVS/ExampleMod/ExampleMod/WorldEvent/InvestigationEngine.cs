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

namespace LivingWorldNpcs
{
    /// <summary>
    /// 调查引擎：AdvanceInvestigation + TryLockSuspect + ProcessAuthorityAction
    /// 从 WorldEventStore.ProcessDaily 中调用。
    /// </summary>
    public static class InvestigationEngine
    {
        /// <summary>每日认知传播速率</summary>
        public static float GetDailySpreadRate(WorldEvent evt)
        {
            var cfg = evt.Config;
            float baseRate = cfg?.BaseSpreadRate ?? 0.1f;
            float severityBonus = (evt.Severity / 100f) * 0.15f;
            float witnessBonus = evt.WitnessCount * 0.05f;
            return Math.Min(0.5f, baseRate + severityBonus + witnessBonus);
        }

        /// <summary>每日调查推进</summary>
        public static void AdvanceInvestigation(WorldEvent evt)
        {
            var cfg = evt.Config;
            float baseRate = cfg?.BaseInvestigationRate ?? 0.25f;

            // 目击修正
            float witnessBonus = evt.WitnessCount * 0.15f;

            // 证据修正
            float evidenceBonus = (evt.EvidenceList?.Sum(e => e.Strength) ?? 0f) * 0.2f;

            // 证据指向本地熟人 → 更快被认出
            float suspectCloseness = 0f;
            var topEvidence = evt.EvidenceList?.OrderByDescending(e => e.Strength).FirstOrDefault();
            if (topEvidence?.TargetId != null)
            {
                var lead = Hero.FindFirst(h => h.StringId == topEvidence.TargetId);
                var authority = WorldEventStore.GetAuthorityNpc(evt);
                float relation = authority?.GetRelation(lead) ?? 0f;
                suspectCloseness = Math.Abs(relation) > 10 ? 0.1f : 0f;
            }

            // 真凶反侦察（仅当玩家是真凶）
            float counterForensics = 0f;
            if (evt.InitiatorId == Hero.MainHero?.StringId)
                counterForensics = Math.Min(0.5f, Hero.MainHero.GetSkillValue(DefaultSkills.Roguery) / 300f * 0.5f);

            float dailyAdvance = baseRate + witnessBonus + evidenceBonus + suspectCloseness - counterForensics;
            evt.InvestigationProgress = Math.Min(1.0f, evt.InvestigationProgress + dailyAdvance);

            DebugLogger.Log($"[Investigation] {evt.EventId} DailyTick: progress={evt.InvestigationProgress:F2} (+{dailyAdvance:F2}) "
                + $"base={baseRate:F2} witness={witnessBonus:F2} evidence={evidenceBonus:F2} "
                + $"closeness={suspectCloseness:F2} counter={counterForensics:F2} suspect={evt.SuspectHeroId ?? "null"}");
        }

        /// <summary>嫌犯锁定</summary>
        public static void TryLockSuspect(WorldEvent evt)
        {
            // 最高 Strength 证据 → 确认为嫌犯
            var topEvidence = evt.EvidenceList?.OrderByDescending(e => e.Strength).FirstOrDefault();
            if (topEvidence?.TargetId != null)
            {
                evt.SuspectHeroId = topEvidence.TargetId;
            }
            else if (evt.WitnessCount > 0)
            {
                // 目击者描述匹配
                evt.SuspectHeroId = TryMatchSuspectFromWitnesses(evt);
            }

            if (evt.SuspectHeroId != null)
            {
                WorldEventStore.TransitionStage(evt, EventStage.Active);
                DebugLogger.Log($"[Investigation] {evt.EventId} Suspect locked: {evt.SuspectHeroId}");
            }
            else
            {
                WorldEventStore.TransitionStage(evt, EventStage.Unsolved);
                DebugLogger.Log($"[Investigation] {evt.EventId} Cold case — no suspect identified");
            }
        }

        /// <summary>从目击者描述匹配嫌犯</summary>
        private static string TryMatchSuspectFromWitnesses(WorldEvent evt)
        {
            // 有 notable 目击者且目击了真凶 → 直接指向真凶
            if (evt.WitnessHeroIds?.Count > 0 && !string.IsNullOrEmpty(evt.InitiatorId))
            {
                // 目击者看到了真凶
                return evt.InitiatorId;
            }

            // 有模板村民目击 → 匹配条件：在附近、有 Roguery 技能、关系差
            if (evt.TemplateWitness?.Count > 0 && !string.IsNullOrEmpty(evt.InitiatorId))
            {
                return evt.InitiatorId;
            }

            return null;
        }

        /// <summary>权威 NPC 自主行动（AI 不等玩家）</summary>
        public static void ProcessAuthorityAction(WorldEvent evt)
        {
            var authority = WorldEventStore.GetAuthorityNpc(evt);
            if (authority == null) return;

            var stance = AttitudeSystem.ComputeStance(authority, evt);
            var responses = ResponseGenerator.GenerateResponses(authority, evt);

            foreach (var pattern in responses)
            {
                switch (pattern)
                {
                    case ResponsePattern.IssueBounty:
                        // 权威 NPC 掏钱悬赏 — Bounty Quest 注册由 CommissionGenerator 处理
                        break;
                    case ResponsePattern.LeadRetaliation:
                        if (!evt.RetaliationSpawned)
                            SpawnRetaliationParty(evt);
                        break;
                    case ResponsePattern.ReportToLord:
                        EscalateToLord(evt, authority);
                        break;
                    case ResponsePattern.SendThugs:
                        SpawnThugParty(evt, authority);
                        break;
                    case ResponsePattern.Indifferent:
                        break;
                    // DemandRestitution / ExtortBribe / GoEasy — 等玩家来找，不主动推
                }
            }
        }

        /// <summary>Spawn 报复部队</summary>
        /// <param name="playerInitiated">true = 玩家主动带队报复（LeadRetaliationIntent，SuspectIsPlayer==false）。
        /// 那是玩家自己的选择，豁免复仇队开关；世界机制派队（权威自主行动/拖延超时/补波次/走人·开打）均走默认 false。</param>
        public static void SpawnRetaliationParty(WorldEvent evt, bool playerInitiated = false)
        {
            try
            {
                if (evt.RetaliationSpawned) return;
                // 🔴 复仇队开关（MCM Mod 选项）：关闭则不派复仇队——玩家选择不会被追击的世界。
                // 覆盖全部 NPC 派队入口（权威自主行动/拖延超时/打赢补波次/走人·开打），
                // 单点守卫，防止任何调用路径绕过；唯一豁免 = 玩家主动带队（playerInitiated）。
                if (!Settings.Instance.EnableRevengeParty && !playerInitiated) return;
                // 🔴 必须已锁定嫌犯才派队：Emerging/Dormant 期 SuspectHeroId 被阶段不变式强制清空，
                // 此时派队无追击目标（旧逻辑 fallback 追玩家 = 上帝视角 + 可能冤枉无辜）。
                // 所有合法调用点（Active 拖延超时/干活违约/Confrontation 走人·开打·补波次）到达时嫌犯均已锁定。
                if (string.IsNullOrEmpty(evt.SuspectHeroId)) return;
                var settlement = evt.TargetSettlement;
                if (settlement == null) return;

                // 播种经费（首次）
                if (evt.RetaliationBudget <= 0)
                    evt.RetaliationBudget = WorldEventStore.SeedRetaliationBudget(evt);

                // 波次成本
                int waveCost = GetWaveCost(evt.RetaliationWaveCount + 1);
                if (evt.RetaliationBudget < waveCost)
                {
                    evt.PermanentEnemy = true;
                    DebugLogger.Log($"[Retaliation] {evt.EventId} Budget exhausted ({evt.RetaliationBudget} < {waveCost})");
                    return;
                }

                // 扣减经费
                var authority = WorldEventStore.GetAuthorityNpc(evt);
                if (authority != null)
                    AgentControlHelper.TransferGold(authority, null, waveCost, notify: false);
                evt.RetaliationBudget -= waveCost;
                evt.RetaliationWaveCount++;

                // 创建报复部队
                var partyId = $"retaliation_{evt.EventId}_w{evt.RetaliationWaveCount}";
                var partyComponent = new SafeLordPartyComponent(authority ?? Hero.MainHero);
                var party = V.MakeParty(partyId, partyComponent);
                if (party == null) return;

                evt.RetaliationPartyId = partyId;
                evt.RetaliationSpawnDay = (float)CampaignTime.Now.ToDays;
                evt.RetaliationSpawned = true;

                // 命名：带队人 = 挂此事的权威 NPC（村长/头人/总督）本人带队——
                // 队名挂带队人名字，玩家一眼看出"谁在追我"，不是泛泛的"村名复仇队"
                // 带队人兜底：村名 → "Village"
                string partyName = authority?.Name?.ToString()
                    ?? evt.TargetSettlement?.Name?.ToString()
                    // 本地化：LWN_investigation_village（玩家可见文本）
                    ?? LWNTextHelper.ResolveText("LWN_investigation_village", "Village");
                // 报复部队名：{带队人名}'s Revenge Party
                V.SetPartyName(party, new TaleWorlds.Localization.TextObject(LWNTextHelper.ResolveCompound("LWN_investigation_revenge_party", ("NAME", partyName))));

                // 位置
                Vec2 basePos = V.Pos(settlement);
                Vec2 spawnPos = basePos + new Vec2(MBRandom.RandomFloatRanged(-5f, 5f), MBRandom.RandomFloatRanged(-5f, 5f));
                V.SetPos(party, spawnPos);

                // 部队规模（每波更强）
                int partySize = 5 + evt.RetaliationWaveCount * 3;
                PartyTemplateObject template = settlement.Culture?.DefaultPartyTemplate;
                if (template != null)
                {
                    V.InitPartyPos(party, template, spawnPos);
                    party.MemberRoster.Clear();
                }

                // 填充部队：只放村民——村长名义带队（leader 是村长，队名/归属/对话都算他），
                // 但本人不加入成员列表：名义带队不会战死/被俘，村庄 Notable 永续。
                var basicTroop = settlement.Culture?.BasicTroop;
                if (basicTroop != null)
                    party.MemberRoster.AddToCounts(basicTroop, partySize);

                // AI：追击嫌犯（SuspectHeroId 已由入口守卫保证非空；
                // 目标判定按 SuspectIsPlayer，不再"null=玩家"——嫌犯为其他英雄但暂无部队时，队原地待命等目标入网）
                if (evt.SuspectIsPlayer)
                {
                    V.EngageParty(party, MobileParty.MainParty);
                    party.Ai.SetDoNotMakeNewDecisions(true);
                }
                else
                {
                    var suspect = Hero.FindFirst(h => h.StringId == evt.SuspectHeroId);
                    var suspectParty = suspect?.PartyBelongedTo;
                    if (suspectParty != null)
                    {
                        V.EngageParty(party, suspectParty);
                        party.Ai.SetDoNotMakeNewDecisions(true);
                    }
                }

                party.SetPartyUsedByQuest(true);
                party.Party.SetVisualAsDirty();
                DebugLogger.Log($"[Retaliation] {evt.EventId} Wave {evt.RetaliationWaveCount}: {partySize} men, cost={waveCost}, budget={evt.RetaliationBudget}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Retaliation] SpawnRetaliationParty error: {ex.Message}");
            }
        }

        /// <summary>波次成本</summary>
        public static int GetWaveCost(int wave)
        {
            return 500 + (wave - 1) * 300;  // Wave 1: 500, Wave 2: 800, Wave 3: 1100...
        }

        /// <summary>打赢后检查是否再派</summary>
        public static void CheckBudgetAndRespawn(WorldEvent evt)
        {
            if (evt.RetaliationSpawned) return;
            if (evt.RetaliationBudget <= 0)
            {
                evt.PermanentEnemy = true;
                WorldEventStore.TransitionStage(evt, EventStage.Resolved);
                evt.ResolvedBy = "budget_depleted";
                return;
            }
            int nextCost = GetWaveCost(evt.RetaliationWaveCount + 1);
            if (evt.RetaliationBudget >= nextCost)
            {
                SpawnRetaliationParty(evt);
            }
            else
            {
                evt.PermanentEnemy = true;
                WorldEventStore.TransitionStage(evt, EventStage.Resolved);
                evt.ResolvedBy = "budget_depleted";
            }
        }

        /// <summary>Spawn 打手小队（小型，3-5人）— SendThugs 响应</summary>
        public static void SpawnThugParty(WorldEvent evt, Hero authority)
        {
            try
            {
                if (evt.RetaliationSpawned) return;
                // 🔴 复仇队开关（MCM Mod 选项）：打手队与复仇队同属"派人追击"通道，
                // 开关关闭时一并禁止，避免玩家关了开关仍被打手队骚扰。
                if (!Settings.Instance.EnableRevengeParty) return;
                // 🔴 同 SpawnRetaliationParty：未锁定嫌犯不派（Emerging 期 SuspectHeroId 为 null）
                if (string.IsNullOrEmpty(evt.SuspectHeroId)) return;
                var settlement = evt.TargetSettlement;
                if (settlement == null) return;

                // 小型打手队经费
                int cost = 200;
                if (authority != null)
                    AgentControlHelper.TransferGold(authority, null, cost, notify: false);

                var partyId = $"thugs_{evt.EventId}_{(float)CampaignTime.Now.ToDays}";
                var partyComponent = new SafeLordPartyComponent(authority ?? Hero.MainHero);
                var party = V.MakeParty(partyId, partyComponent);
                if (party == null) return;

                evt.RetaliationPartyId = partyId;
                evt.RetaliationSpawnDay = (float)CampaignTime.Now.ToDays;
                evt.RetaliationSpawned = true;

                // 打手队名：挂权威 NPC 名字（他派来的打手，主使可见）；带队人兜底：村名 → "Village"
                string partyName = authority?.Name?.ToString()
                    ?? evt.TargetSettlement?.Name?.ToString()
                    // 本地化：LWN_investigation_village（玩家可见文本）
                    ?? LWNTextHelper.ResolveText("LWN_investigation_village", "Village");
                // 打手队名：{权威 NPC 名}'s Thugs
                V.SetPartyName(party, new TaleWorlds.Localization.TextObject(LWNTextHelper.ResolveCompound("LWN_investigation_thug_party", ("NAME", partyName))));

                Vec2 basePos = V.Pos(settlement);
                Vec2 spawnPos = basePos + new Vec2(MBRandom.RandomFloatRanged(-5f, 5f), MBRandom.RandomFloatRanged(-5f, 5f));
                V.SetPos(party, spawnPos);

                // 小型打手队：3-5 人
                int partySize = 3 + MBRandom.RandomInt(0, 3);
                var basicTroop = settlement.Culture?.BasicTroop;
                if (basicTroop != null)
                    party.MemberRoster.AddToCounts(basicTroop, partySize);

                // AI：追击嫌犯（同 SpawnRetaliationParty：按 SuspectIsPlayer 判定，不再"null=玩家"）
                if (evt.SuspectIsPlayer)
                {
                    V.EngageParty(party, MobileParty.MainParty);
                }
                else
                {
                    var suspect = Hero.FindFirst(h => h.StringId == evt.SuspectHeroId);
                    var suspectParty = suspect?.PartyBelongedTo;
                    if (suspectParty != null)
                        V.EngageParty(party, suspectParty);
                }

                party.Ai.SetDoNotMakeNewDecisions(true);
                party.SetPartyUsedByQuest(true);
                party.Party.SetVisualAsDirty();
                DebugLogger.Log($"[Thugs] {evt.EventId} Spawned thug party: {partySize} men, cost={cost}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Thugs] SpawnThugParty error: {ex.Message}");
            }
        }

        /// <summary>上报领主</summary>
        private static void EscalateToLord(WorldEvent evt, Hero authority)
        {
            try
            {
                var settlement = evt.TargetSettlement;
                if (settlement?.OwnerClan?.Leader == null) return;
                var lord = settlement.OwnerClan.Leader;
                if (lord == authority) return;

                var escalated = evt.ShallowCopy($"escalated_{evt.EventId}");
                escalated.Type = EventType.EscalatedCrime;
                escalated.Severity = Math.Min(100, evt.Severity + 20);
                escalated.OccurredDay = (float)CampaignTime.Now.ToDays;
                escalated.DayLimit = 14f;
                escalated.Stage = EventStage.Active;
                escalated.PublicAwareness = evt.PublicAwareness + 0.2f;

                WorldEventStore.AddOrMerge(escalated);
                DebugLogger.Log($"[Escalate] {evt.EventId} escalated to lord {lord.Name}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Escalate] Error: {ex.Message}");
            }
        }

        /// <summary>冷案尾巴：15% 概率村民迁怒打错人（由 WorldEventStore.TransitionStage 进入 Unsolved 时一次性判定调用）</summary>
        public static void TriggerVigilanteJustice(WorldEvent evt)
        {
            try
            {
                var settlement = evt.TargetSettlement;
                if (settlement == null) return;

                var candidates = Hero.AllAliveHeroes
                    .Where(h => h != Hero.MainHero && !h.IsLord && !h.IsMerchant)
                    .Where(h => h.GetSkillValue(DefaultSkills.Roguery) > 50 || h.GetTraitLevel(DefaultTraits.Honor) < 0)
                    .OrderByDescending(h => h.GetSkillValue(DefaultSkills.Roguery))
                    .Take(3)
                    .ToList();

                if (candidates.Count == 0) return;
                var scapegoat = candidates[new Random().Next(candidates.Count)];

                var newEvt = new WorldEvent
                {
                    EventId = $"vigilante_{evt.EventId}_{(float)CampaignTime.Now.ToDays}",
                    Category = EventCategory.Crime,
                    Type = EventType.VigilanteJustice,
                    Severity = 20,
                    InitiatorId = null,
                    TargetHeroId = scapegoat.StringId,
                    TargetSettlementId = evt.TargetSettlementId,
                    OccurredDay = (float)CampaignTime.Now.ToDays,
                    DayLimit = 10f,
                    Stage = EventStage.Active,
                    SuspectHeroId = scapegoat.StringId,
                    InvestigationProgress = 1.0f,
                    PublicAwareness = 0.5f,
                };
                WorldEventStore.AddOrMerge(newEvt);
                DebugLogger.Log($"[WorldEvent] Vigilante justice spawned: {scapegoat.Name} blamed for cold case {evt.EventId}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEvent] TriggerVigilanteJustice error: {ex.Message}");
            }
        }
    }
}
