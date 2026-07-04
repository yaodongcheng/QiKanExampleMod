using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs
{
    public static class CommissionGenerator
    {
        private const int MaxCommissionsPerNpc = 1;
        // 简单缓存：避免每次 OnCheckForIssue 都重新生成
        private static Dictionary<string, List<CommissionData>> _cache = new Dictionary<string, List<CommissionData>>();
        private static float _lastCacheClearDay = -1;

        public static bool HasCommissionsFor(Hero hero, out int count)
        {
            count = 0;
            if (hero == null) return false;
            if (hero == Hero.MainHero) return false;
            if (!hero.IsAlive) return false;

            if (CommissionQuest.IsHeroInvolvedInActiveCommission(hero, out _, out bool isGiver) && isGiver)
            {
                int existingCount = Campaign.Current.QuestManager.Quests
                    .Count(q => q is CommissionQuest cq && cq.CommissionGiver == hero);
                if (existingCount >= MaxCommissionsPerNpc) return false;
            }

            // 事件优先（新架构）：NPC 自身有紧迫事件缠身（作为加害方或受害者） → 强制显示 !
            if (AllNpcMemoryManager.GetMemory(hero.StringId)?.CurrentUrgentEvent != null)
            {
                count = 1;
                return true;
            }

            // 世界事件驱动：NPC 是附近活跃事件的受害者 → 强制显示 !
            if (IsHeroInNearbyWorldEvent(hero))
            {
                count = 1;
                return true;
            }

            // 世界事件代理：NPC 所在定居点是活跃事件的目标，但受害者（lord）不在场 →
            // 此 NPC 作为代理人提供委托（头人/村长替不在场的领主发布任务）
            if (IsHeroProxyForWorldEvent(hero))
            {
                count = 1;
                return true;
            }

            // 犯罪追责：NPC 是对应定居点犯罪事件的权威人物（村长/族长/领主）
            if (IsAuthorityForActiveCrimeEvent(hero))
            {
                count = 1;
                return true;
            }

            // 每日清缓存（委托列表每天刷新）
            float currentDay = (float)CampaignTime.Now.ToDays;
            if (Math.Abs(currentDay - _lastCacheClearDay) > 0.5f)
            {
                _cache.Clear();
                _lastCacheClearDay = currentDay;
            }

            if (_cache.TryGetValue(hero.StringId, out var cached))
            {
                count = Math.Min(cached.Count, MaxCommissionsPerNpc);
                return count > 0;
            }

            var availableDefs = GetAvailableDefsForHero(hero);
            count = Math.Min(availableDefs.Count, MaxCommissionsPerNpc);
            return count > 0;
        }

        public static List<CommissionData> GenerateCommissions(Hero hero, int maxCount = 3)
        {
            var results = new List<CommissionData>();
            if (hero == null || hero == Hero.MainHero || !hero.IsAlive) return results;

            float currentDay = (float)CampaignTime.Now.ToDays;
            if (Math.Abs(currentDay - _lastCacheClearDay) > 0.5f)
            { _cache.Clear(); _lastCacheClearDay = currentDay; }
            if (_cache.TryGetValue(hero.StringId, out var cached))
                return cached.Take(maxCount).ToList();

            // 判断此 NPC 是告示板（中转人）还是直接委托人
            bool isBroker = IsBrokerType(hero);

            if (isBroker)
            {
                // 告示板模式：从此 NPC 周边的真实委托人收集委托
                var nearbyGivers = FindNearbyQuestGivers(hero);
                foreach (var giver in nearbyGivers.Take(maxCount))
                {
                    var defs = GetAvailableDefsForHero(giver);
                    if (defs.Count == 0) continue;
                    var def = defs.OrderBy(_ => MBRandom.RandomFloat).First();
                    var data = GenerateCommissionData(def, giver, hero); // QuestGiver=giver, BrokerHero=hero
                    if (data != null) results.Add(data);
                }
            }
            else
            {
                // 直接模式：此 NPC 就是委托人
                var availableDefs = GetAvailableDefsForHero(hero);

                // ── 事件直接匹配：当事人用自己的 CurrentUrgentEvent，不走地理邻近 ──
                var urgentEvent = AllNpcMemoryManager.GetMemory(hero.StringId)?.CurrentUrgentEvent;
                if (urgentEvent != null)
                {
                    bool isVictim = urgentEvent.TargetHeroId == hero.StringId;
                    bool isInstigator = urgentEvent.InitiatorId == hero.StringId;
                    var eventConfig = WorldEventConfig.Get(urgentEvent.Type);

                    // 按角色取分侧委托列表（优先 InstigatorCommissions/VictimCommissions，回退 MatchingCommissions）
                    var roleCommissions = eventConfig?.GetCommissionsForRole(isVictim);
                    if (roleCommissions != null && roleCommissions.Length > 0)
                    {
                        // 按角色筛选合适的委托类别
                        var roleDefs = new List<CommissionDef>();
                        foreach (var def in availableDefs)
                        {
                            if (!roleCommissions.Contains(def.Category)) continue;

                            // 加害方：过滤掉自己在做的活（赏金/刺杀目标不需要雇人重复干）
                            if (isInstigator)
                            {
                                if (def.Category == CommissionCategory.BountyHunt) continue;
                                if (def.Category == CommissionCategory.PrisonBreak) continue;
                            }

                            roleDefs.Add(def);
                        }

                        // 每个 category 最多一条，事件 NPC 最多 2 个委托（KCD2：少而精）
                        var distinctDefs = roleDefs
                            .GroupBy(d => d.Category)
                            .Select(g => g.OrderBy(_ => MBRandom.RandomFloat).First())
                            .Take(Math.Min(maxCount, 2))
                            .ToList();

                        // 用事件数据直接生成委托：受害者 → target=instigator，加害方 → target=victim
                        foreach (var def in distinctDefs)
                        {
                            var data = GenerateCommissionDataForEvent(def, hero, urgentEvent, isVictim);
                            if (data != null)
                                results.Add(data);
                        }

                        DebugLogger.Log($"[CommissionGen] Event-direct generation: hero={hero.Name} event={urgentEvent.Type} role={(isVictim ? "Victim" : isInstigator ? "Instigator" : "Unknown")} candidates={roleDefs.Count} result={results.Count}");
                        _cache[hero.StringId] = results;
                        return results;
                    }

                    // 事件存在但无匹配委托配置 → 不生成委托，让事件对话主导
                    DebugLogger.Log($"[CommissionGen] Event-active NPC {hero.Name} ({urgentEvent.Type}) has no matching commission defs — skipping");
                    _cache[hero.StringId] = results;
                    return results;
                }

                // ── 犯罪追责：权威 NPC 所在定居点有活跃 WorldEvent 犯罪 → 生成追责 Quest ──
                var accountabilityQuest = TryGenerateAccountabilityQuest(hero);
                if (accountabilityQuest != null)
                {
                    results.Add(accountabilityQuest);
                    _cache[hero.StringId] = results;
                    return results;
                }

                // 无事件：保持现有随机逻辑
                int count = Math.Min(maxCount, availableDefs.Count);
                var shuffled = availableDefs.OrderBy(_ => MBRandom.RandomFloat).ToList();
                for (int i = 0; i < count; i++)
                {
                    var data = GenerateCommissionData(shuffled[i], hero, null); // QuestGiver=hero, BrokerHero=null
                    if (data != null) results.Add(data);
                }
            }

            _cache[hero.StringId] = results;
            return results;
        }

        /// <summary>此 NPC 是否是告示板类型（酒馆老板 / 村长 / 浪人情报贩子）</summary>
        private static bool IsBrokerType(Hero hero)
        {
            if (hero == null) return false;
            return hero.Occupation == Occupation.Tavernkeeper  // 酒馆老板
                || hero.Occupation == Occupation.Headman        // 村庄村长
                || hero.IsWanderer;                              // 浪人情报贩子
        }

        /// <summary>为告示板 NPC 寻找周边有委托需求的真实委托人</summary>
        private static List<Hero> FindNearbyQuestGivers(Hero broker)
        {
            var results = new List<Hero>();
            Settlement brokerSettlement = broker.CurrentSettlement ?? broker.HomeSettlement;
            if (brokerSettlement == null) return results;

            // 收集同城 + 附近定居点的有 HeroId 的 NPC
            var nearbySettlements = new HashSet<Settlement> { brokerSettlement };
            foreach (var s in Settlement.All)
            {
                if (s == brokerSettlement) continue;
                float distance = V.Pos(brokerSettlement).Distance(V.Pos(s));
                if (distance < 50f) // 大约 2 天路程
                    nearbySettlements.Add(s);
            }

            foreach (var settlement in nearbySettlements)
            {
                foreach (var hero in settlement.Notables)
                {
                    if (hero == null || hero == broker || hero == Hero.MainHero || !hero.IsAlive) continue;
                    if (CommissionQuest.IsHeroInvolvedInActiveCommission(hero, out _, out bool isGiver) && isGiver) continue;
                    if (GetAvailableDefsForHero(hero).Count > 0)
                        results.Add(hero);
                }
                // 也检查该定居点所属领主
                if (settlement.OwnerClan?.Leader != null)
                {
                    var lord = settlement.OwnerClan.Leader;
                    if (lord != broker && lord.IsAlive && lord != Hero.MainHero
                        && GetAvailableDefsForHero(lord).Count > 0
                        && !CommissionQuest.IsHeroInvolvedInActiveCommission(lord, out _, out _))
                        results.Add(lord);
                }
            }

            return results.Distinct().OrderBy(_ => MBRandom.RandomFloat).ToList();
        }

        internal static List<CommissionDef> GetAvailableDefsForHero(Hero hero)
        {
            var results = new List<CommissionDef>();
            foreach (var def in CommissionDef.AllDefs)
            {
                if (def.ValidGiverOccupations == null || def.ValidGiverOccupations.Length == 0) continue;

                bool occupationMatch = false;
                if (hero.Occupation != Occupation.NotAssigned)
                    occupationMatch = def.ValidGiverOccupations.Contains(hero.Occupation);
                if (!occupationMatch && hero.IsWanderer)
                    occupationMatch = def.ValidGiverOccupations.Contains(Occupation.Wanderer);
                if (!occupationMatch && hero.IsNotable)
                    occupationMatch = def.ValidGiverOccupations.Contains(hero.Occupation);
                if (!occupationMatch) continue;

                if (!IsVenueMatch(def.Category, hero)) continue;

                if (InfamySystem.IsBlockedByInfamy(def.Category, (int)hero.GetRelationWithPlayer())) continue;

                float relation = hero.GetRelationWithPlayer();
                int trust = TrustSystem.GetTrust(hero);
                if (relation < -30 && trust < 50) continue;

                results.Add(def);
            }
            return results;
        }

        private static bool IsVenueMatch(CommissionCategory category, Hero hero)
        {
            if (MBRandom.RandomFloat < 0.3f) return true;
            Occupation occ = hero.Occupation;

            switch (category)
            {
                case CommissionCategory.BountyHunt:
                case CommissionCategory.LegendaryHunt:
                case CommissionCategory.HideoutClear:
                    return occ == Occupation.GangLeader || occ == Occupation.Headman ||
                           occ == Occupation.Lord || occ == Occupation.Wanderer || occ == Occupation.RuralNotable;

                case CommissionCategory.CaravanEscort:
                case CommissionCategory.EmergencyDelivery:
                    return occ == Occupation.Merchant || occ == Occupation.Artisan ||
                           occ == Occupation.Headman || occ == Occupation.RuralNotable || occ == Occupation.Lord;

                case CommissionCategory.SupplyEmergency:
                case CommissionCategory.ProcurementAgent:
                case CommissionCategory.LostItem:
                case CommissionCategory.TreasureHunt:
                case CommissionCategory.HorseAcquisition:
                    return occ == Occupation.Merchant || occ == Occupation.Artisan ||
                           occ == Occupation.Headman || occ == Occupation.Wanderer;

                case CommissionCategory.UndergroundFight:
                case CommissionCategory.ArenaSpecial:
                    return occ == Occupation.GangLeader || occ == Occupation.Wanderer;

                case CommissionCategory.VillageDefense:
                    return occ == Occupation.Headman || occ == Occupation.RuralNotable || occ == Occupation.Lord;

                case CommissionCategory.PrisonBreak:
                case CommissionCategory.SupplyIntercept:
                case CommissionCategory.DecoyMission:
                    return occ == Occupation.GangLeader || occ == Occupation.Lord || occ == Occupation.Wanderer;

                default:
                    return true;
            }
        }

        /// <summary>
        /// 生成委托数据但跳过世界事件匹配——直接走 FillTargetXxx 回退逻辑。
        /// 用于 instigator 的委托生成在过滤掉受害者目标后需要补充非事件委托时。
        /// </summary>
        private static CommissionData GenerateCommissionDataWithoutEvent(CommissionDef def, Hero questGiver)
        {
            CommissionTier tier = CommissionTierProgression.GetAvailableTier(def.Category);
            if (tier > CommissionTier.Basic && MBRandom.RandomFloat < 0.5f)
                tier = (CommissionTier)((int)tier - 1);

            var data = new CommissionData
            {
                DefId = def.Id,
                Category = def.Category,
                QuestGiver = questGiver,
                BrokerHero = null,
                IsNarrativePhase = false,
                TimeRemainingHours = def.TimeLimitDays * 24f,
                ChosenPath = PickBestPath(def.AvailablePaths),
                Tier = tier,
            };

            bool filled;
            switch (def.TargetType)
            {
                case CommissionTargetType.NamedHero:
                    filled = FillTargetHero(data, def);
                    break;
                case CommissionTargetType.Settlement:
                    filled = FillTargetSettlement(data, def, questGiver);
                    break;
                case CommissionTargetType.Item:
                    filled = FillTargetItem(data, def) && FillTargetSettlement(data, def, questGiver);
                    break;
                case CommissionTargetType.Region:
                case CommissionTargetType.Any:
                    filled = FillTargetSettlement(data, def, questGiver);
                    break;
                default:
                    filled = false;
                    break;
            }
            if (!filled) return null;

            float tierMultiplier = tier switch
            {
                CommissionTier.Basic => 1.0f,
                CommissionTier.Skilled => 2.0f,
                CommissionTier.Expert => 4.0f,
                CommissionTier.Legendary => 8.0f,
                _ => 1.0f
            };

            int trust = TrustSystem.GetTrust(questGiver);
            float depositRatio = TrustSystem.GetDepositRatio(trust);
            data.NegotiatedReward = (int)(def.BaseRewardGold * tierMultiplier);
            data.DepositAmount = (int)(data.NegotiatedReward * depositRatio);
            data.RewardPayer = MBRandom.RandomFloat < 0.9f ? questGiver : PickAlternatePayer(questGiver);

            return data;
        }

        /// <summary>
        /// 事件直接匹配生成委托 — 当事人用自己的 CurrentUrgentEvent。
        /// 不走地理邻近搜索，受害者 target=instigator，加害方 target=victim。
        /// </summary>
        private static CommissionData GenerateCommissionDataForEvent(CommissionDef def, Hero questGiver, WorldEvent worldEvent, bool isVictim)
        {
            CommissionTier tier = CommissionTierProgression.GetAvailableTier(def.Category);
            if (tier > CommissionTier.Basic && MBRandom.RandomFloat < 0.5f)
                tier = (CommissionTier)((int)tier - 1);

            var data = new CommissionData
            {
                DefId = def.Id,
                Category = def.Category,
                QuestGiver = questGiver,
                BrokerHero = null,
                IsNarrativePhase = false,
                TimeRemainingHours = def.TimeLimitDays * 24f,
                ChosenPath = PickBestPath(def.AvailablePaths),
                Tier = tier,
                WorldEventId = worldEvent.EventId,
                IsGenericInstigator = worldEvent.IsGenericInstigator,
            };

            // 目标设置：受害者 → instigator，加害方 → victim
            switch (def.TargetType)
            {
                case CommissionTargetType.NamedHero:
                    if (isVictim)
                    {
                        // 受害者：委托目标 = 加害方（赏金缉拿刺客 / 引开追兵）
                        var instigator = worldEvent.InstigatorHero;
                        if (instigator != null && instigator.IsAlive && instigator != questGiver)
                            data.TargetHero = instigator;
                        else
                            return null;
                    }
                    else
                    {
                        // 加害方：委托目标 = 受害者（但 BountyHunt 已在入口过滤，这里不走）
                        var target = worldEvent.TargetHero;
                        if (target != null && target.IsAlive && target != questGiver)
                            data.TargetHero = target;
                        else
                            return null;
                    }
                    break;

                case CommissionTargetType.Settlement:
                    // 目标定居点 = 事件发生地
                    data.TargetSettlementId = worldEvent.TargetSettlementId
                        ?? questGiver?.CurrentSettlement?.StringId
                        ?? questGiver?.HomeSettlement?.StringId;
                    if (string.IsNullOrEmpty(data.TargetSettlementId)) return null;
                    break;

                case CommissionTargetType.Item:
                    if (!FillTargetItem(data, def)) return null;
                    data.TargetSettlementId = worldEvent.TargetSettlementId
                        ?? questGiver?.CurrentSettlement?.StringId;
                    break;

                default:
                    data.TargetSettlementId = worldEvent.TargetSettlementId;
                    break;
            }

            float tierMultiplier = tier switch
            {
                CommissionTier.Basic => 1.0f,
                CommissionTier.Skilled => 2.0f,
                CommissionTier.Expert => 4.0f,
                CommissionTier.Legendary => 8.0f,
                _ => 1.0f
            };

            int trust = TrustSystem.GetTrust(questGiver);
            float depositRatio = TrustSystem.GetDepositRatio(trust);
            data.NegotiatedReward = (int)(def.BaseRewardGold * tierMultiplier);
            data.DepositAmount = (int)(data.NegotiatedReward * depositRatio);
            data.RewardPayer = MBRandom.RandomFloat < 0.9f ? questGiver : PickAlternatePayer(questGiver);

            DebugLogger.Log($"[CommissionGen] Event-direct: def={def.Id} tier={tier} giver={questGiver?.Name} role={(isVictim ? "Victim" : "Instigator")} target={data.TargetHero?.Name?.ToString() ?? data.TargetSettlementId} payer={data.RewardPayer?.Name?.ToString() ?? questGiver?.Name?.ToString()}");

            return data;
        }

        private static CommissionData GenerateCommissionData(CommissionDef def, Hero questGiver, Hero brokerHero)
        {
            CommissionTier tier = CommissionTierProgression.GetAvailableTier(def.Category);
            if (tier > CommissionTier.Basic && MBRandom.RandomFloat < 0.5f)
                tier = (CommissionTier)((int)tier - 1);

            var data = new CommissionData
            {
                DefId = def.Id,
                Category = def.Category,
                QuestGiver = questGiver,       // 真正的委托人
                BrokerHero = brokerHero,        // 告示板中转人（null = 直接委托）
                IsNarrativePhase = brokerHero != null, // 通过告示板接的 → 需要先去见委托人
                TimeRemainingHours = def.TimeLimitDays * 24f,
                ChosenPath = PickBestPath(def.AvailablePaths),
                Tier = tier,
            };

            switch (def.TargetType)
            {
                case CommissionTargetType.NamedHero:
                    // 第一优先：尝试匹配真实世界事件
                    if (!TryMatchWorldEvent(data, def, questGiver))
                    {
                        if (!FillTargetHero(data, def)) return null;
                    }
                    break;
                case CommissionTargetType.Settlement:
                    if (!TryMatchWorldEvent(data, def, questGiver))
                    {
                        if (!FillTargetSettlement(data, def, questGiver)) return null;
                    }
                    break;
                case CommissionTargetType.Item:
                    if (!FillTargetItem(data, def)) return null;
                    FillTargetSettlement(data, def, questGiver);
                    break;
                case CommissionTargetType.Region:
                case CommissionTargetType.Any:
                    FillTargetSettlement(data, def, questGiver);
                    break;
            }

            float tierMultiplier = tier switch
            {
                CommissionTier.Basic => 1.0f,
                CommissionTier.Skilled => 2.0f,
                CommissionTier.Expert => 4.0f,
                CommissionTier.Legendary => 8.0f,
                _ => 1.0f
            };

            int trust = TrustSystem.GetTrust(questGiver);
            float depositRatio = TrustSystem.GetDepositRatio(trust);
            data.NegotiatedReward = (int)(def.BaseRewardGold * tierMultiplier);
            data.DepositAmount = (int)(data.NegotiatedReward * depositRatio);

            // 90% 概率结账人 = 委托人，10% 概率随机选同城另一个 NPC
            data.RewardPayer = MBRandom.RandomFloat < 0.9f ? questGiver : PickAlternatePayer(questGiver);

            DebugLogger.Log($"[CommissionGen] Generated commission: def={def.Id} tier={tier} giver={questGiver?.Name} broker={(brokerHero != null ? brokerHero.Name?.ToString() : "none")} reward={data.NegotiatedReward} target={data.TargetHero?.Name?.ToString() ?? data.TargetSettlementId ?? "any"} payer={data.RewardPayer?.Name?.ToString() ?? questGiver?.Name?.ToString()}");

            return data;
        }

        private static ResolutionPath PickBestPath(ResolutionPath[] availablePaths)
        {
            if (availablePaths == null || availablePaths.Length == 0) return ResolutionPath.Combat;
            if (availablePaths.Length == 1) return availablePaths[0];

            float combat = Hero.MainHero.GetSkillValue(DefaultSkills.OneHanded) + Hero.MainHero.GetSkillValue(DefaultSkills.TwoHanded);
            float stealth = Hero.MainHero.GetSkillValue(DefaultSkills.Roguery);
            float wealth = Hero.MainHero.Gold;
            float technical = Hero.MainHero.GetSkillValue(DefaultSkills.Engineering) + Hero.MainHero.GetSkillValue(DefaultSkills.Trade);
            float social = Hero.MainHero.GetSkillValue(DefaultSkills.Charm);

            var scores = new Dictionary<ResolutionPath, float>
            {
                [ResolutionPath.Combat] = combat,
                [ResolutionPath.Stealth] = stealth,
                [ResolutionPath.Wealth] = wealth / 1000f,
                [ResolutionPath.Technical] = technical,
                [ResolutionPath.Social] = social,
            };

            ResolutionPath best = ResolutionPath.Combat;
            float bestScore = 0;
            foreach (var path in availablePaths)
            {
                if (scores.ContainsKey(path) && scores[path] > bestScore)
                {
                    bestScore = scores[path];
                    best = path;
                }
            }
            return best;
        }

        private static bool FillTargetHero(CommissionData data, CommissionDef def)
        {
            // 计算目标与委托人之间的距离权重
            Settlement giverSettlement = data.QuestGiver?.CurrentSettlement
                ?? data.QuestGiver?.HomeSettlement;
            float GetProximityScore(Hero h)
            {
                if (giverSettlement == null) return MBRandom.RandomFloat;
                Settlement heroSettlement = h.CurrentSettlement ?? h.HomeSettlement;
                if (heroSettlement == null) return MBRandom.RandomFloat * 100f;
                float dist = V.Pos(giverSettlement).Distance(V.Pos(heroSettlement));
                return dist * (0.5f + MBRandom.RandomFloat * 1.5f);
            }

            switch (def.Category)
            {
                case CommissionCategory.BountyHunt:
                case CommissionCategory.LegendaryHunt:
                    var banditHeroes = Hero.AllAliveHeroes
                        .Where(h => h != Hero.MainHero && h != data.QuestGiver && h.IsAlive
                            && h.PartyBelongedTo == null
                            && (h.Occupation == Occupation.Bandit || h.MapFaction != Hero.MainHero.MapFaction)
                            && !CommissionQuest.IsHeroInvolvedInActiveCommission(h, out _, out _)
                            && !IsHeroBusyInWorldEvent(h))  // 排除已被世界事件占用的 hero
                        .OrderBy(h => GetProximityScore(h))
                        .ToList();
                    if (banditHeroes.Count == 0) return false;
                    data.TargetHero = banditHeroes.First();
                    break;

                case CommissionCategory.PrisonBreak:
                    var prisoners = Hero.AllAliveHeroes
                        .Where(h => h != Hero.MainHero && h != data.QuestGiver && h.IsPrisoner
                            && !CommissionQuest.IsHeroInvolvedInActiveCommission(h, out _, out _))
                        .OrderBy(h => GetProximityScore(h))
                        .ToList();
                    if (prisoners.Count == 0) return false;
                    data.TargetHero = prisoners.First();
                    break;

                default:
                    var candidates = Hero.AllAliveHeroes
                        .Where(h => h != Hero.MainHero && h != data.QuestGiver && h.IsAlive
                            && !CommissionQuest.IsHeroInvolvedInActiveCommission(h, out _, out _))
                        .OrderBy(h => GetProximityScore(h))
                        .ToList();
                    if (candidates.Count == 0) return false;
                    data.TargetHero = candidates.First();
                    break;
            }
            return data.TargetHero != null;
        }

        /// <summary>检查 Hero 是否已被世界事件占用（作为目标或加害方）。</summary>
        private static bool IsHeroBusyInWorldEvent(Hero hero)
        {
            if (hero == null || string.IsNullOrEmpty(hero.StringId)) return false;
            return WorldEventStore.ActiveEvents.Any(e =>
                e.InitiatorId == hero.StringId || e.TargetHeroId == hero.StringId);
        }

        /// <summary>检查 Hero 是否在附近活跃世界事件中作为受害者（用于 ! 标记显示）。</summary>
        private static bool IsHeroInNearbyWorldEvent(Hero hero)
        {
            if (hero == null || string.IsNullOrEmpty(hero.StringId)) return false;
            Settlement heroSettlement = hero.CurrentSettlement ?? hero.HomeSettlement;
            if (heroSettlement == null) return false;
            return WorldEventStore.ActiveEvents.Any(e =>
                e.TargetHeroId == hero.StringId
                && e.TargetSettlement != null
                && V.Pos(e.TargetSettlement).Distance(V.Pos(heroSettlement)) < 80f);
        }

        /// <summary>
        /// 检查 NPC 是否可作为世界事件的代理人。
        /// 当定居点是活跃事件的目标但受害者（lord）不在场时，
        /// 定居点内的 Notable 可作为代理人发布委托——玩家到地方不会找不到人。
        /// </summary>
        private static bool IsHeroProxyForWorldEvent(Hero hero)
        {
            if (hero == null || string.IsNullOrEmpty(hero.StringId)) return false;
            Settlement heroSettlement = hero.CurrentSettlement;
            if (heroSettlement == null) return false;

            // 只对定居点内的 Notable 生效（头人、商人、工匠等），lord 不需要代理
            if (!hero.IsNotable && hero.Occupation != Occupation.Headman) return false;

            // 检查是否有活跃事件以此定居点为目标，且受害者不在此定居点
            // 注意：必须存在具体的受害 Hero（TargetHeroId != null）才启用代理——
            // 没有具体受害者的事件（如偷动物，受害方是整个村子）走 IsAuthorityForActiveCrimeEvent 独占路径
            return WorldEventStore.ActiveEvents.Any(e =>
                e.TargetSettlementId == heroSettlement.StringId
                && !string.IsNullOrEmpty(e.TargetHeroId)  // 有具体受害者才需要代理
                && e.TargetHeroId != hero.StringId  // 不是受害者本人
                && !IsHeroPresentInSettlement(e.TargetHeroId, heroSettlement)); // 受害者不在场
        }

        /// <summary>检查指定 Hero 是否在某个定居点中。</summary>
        private static bool IsHeroPresentInSettlement(string heroStringId, Settlement settlement)
        {
            if (string.IsNullOrEmpty(heroStringId) || settlement == null) return false;
            foreach (var h in settlement.HeroesWithoutParty)
                if (h.StringId == heroStringId) return true;
            foreach (var h in settlement.Notables)
                if (h.StringId == heroStringId) return true;
            return false;
        }

        /// <summary>
        /// 第一优先路径：尝试匹配附近的真实 WorldEvent。
        /// 匹配成功 → 使用事件的目标/加害方/定居点，设置 WorldEventId。
        /// 匹配失败 → 返回 false，调用方回退旧的随机 Fill 逻辑。
        ///
        /// 事件类型 → 委托类别映射（阶段 3 MVP：只有 BanditRaid → BountyHunt）：
        ///   BanditRaid → BountyHunt, VillageDefense
        ///   后续扩展：Kidnapping → BountyHunt, DecoyMission 等
        /// </summary>
        private static bool TryMatchWorldEvent(CommissionData data, CommissionDef def, Hero questGiver)
        {
            // 委托人所在地点
            Settlement giverSettlement = questGiver?.CurrentSettlement
                ?? questGiver?.HomeSettlement;
            if (giverSettlement == null) return false;

            // 查附近活跃事件
            var nearbyEvents = WorldEventStore.GetActiveEventsNear(giverSettlement, maxDistance: 80f);
            if (nearbyEvents.Count == 0) return false;

            // 按委托类别筛选匹配的事件
            foreach (var worldEvent in nearbyEvents)
            {
                if (!IsWorldEventMatchForCategory(worldEvent.Type, def.Category))
                    continue;

                // 匹配！填充 CommissionData
                data.WorldEventId = worldEvent.EventId;
                data.IsGenericInstigator = worldEvent.IsGenericInstigator;

                switch (def.TargetType)
                {
                    case CommissionTargetType.NamedHero:
                        // 目标 = 加害方（匪首/绑匪/背叛者…）
                        // 🛡 守卫：不要匹配 quest giver 自己的事件（instigator 不能悬赏自己）
                        var instigator = worldEvent.InstigatorHero;
                        if (instigator != null && instigator.IsAlive && instigator != questGiver)
                        {
                            data.TargetHero = instigator;
                        }
                        else if (worldEvent.IsGenericInstigator)
                        {
                            data.TargetSettlementId = worldEvent.TargetSettlementId;
                            DebugLogger.Log($"[CommissionGen] Matched WorldEvent with generic instigator: category={def.Category} event={worldEvent.Type} settlement={data.TargetSettlementId}");
                        }
                        else
                        {
                            return false;
                        }
                        break;

                    case CommissionTargetType.Settlement:
                        // 目标 = 受害定居点
                        data.TargetSettlementId = worldEvent.TargetSettlementId;
                        break;

                    default:
                        // 其他目标类型暂不匹配 WorldEvent
                        return false;
                }

                DebugLogger.Log($"[CommissionGen] Matched WorldEvent! category={def.Category} event={worldEvent.Type} eventId={worldEvent.EventId} target={data.TargetHero?.Name?.ToString() ?? data.TargetSettlementId}");
                return true;
            }

            return false;
        }

        /// <summary>检查 EventType 是否匹配 CommissionCategory（检查全部分侧列表）。</summary>
        private static bool IsWorldEventMatchForCategory(EventType eventType, CommissionCategory category)
        {
            var config = WorldEventConfig.Get(eventType);
            if (config == null) return false;

            // 检查所有三个列表
            if (config.MatchingCommissions != null)
                foreach (var c in config.MatchingCommissions)
                    if (c == category) return true;

            if (config.InstigatorCommissions != null)
                foreach (var c in config.InstigatorCommissions)
                    if (c == category) return true;

            if (config.VictimCommissions != null)
                foreach (var c in config.VictimCommissions)
                    if (c == category) return true;

            return false;
        }

        private static bool FillTargetSettlement(CommissionData data, CommissionDef def, Hero questGiver)
        {
            Settlement giverSettlement = questGiver.CurrentSettlement
                ?? questGiver.HomeSettlement
                ?? Settlement.All.FirstOrDefault();
            if (giverSettlement == null) return false;

            switch (def.Category)
            {
                case CommissionCategory.CaravanEscort:
                case CommissionCategory.EmergencyDelivery:
                case CommissionCategory.SupplyEmergency:
                case CommissionCategory.ProcurementAgent:
                {
                    var towns = Settlement.All
                        .Where(s => (s.IsTown || s.IsCastle) && s != giverSettlement)
                        .OrderBy(s => V.Pos(giverSettlement).Distance(V.Pos(s))
                                      * (0.5f + MBRandom.RandomFloat * 1.5f))
                        .ToList();
                    if (towns.Count == 0) return false;
                    // 60% 概率从最近的 3 个中选
                    int pickIndex;
                    float roll = MBRandom.RandomFloat;
                    if (towns.Count > 3 && roll < 0.6f)
                        pickIndex = MBRandom.RandomInt(0, Math.Min(3, towns.Count));
                    else if (roll < 0.85f && towns.Count > 6)
                        pickIndex = MBRandom.RandomInt(3, Math.Min(6, towns.Count));
                    else
                        pickIndex = MBRandom.RandomInt(0, towns.Count);
                    data.TargetSettlementId = towns[pickIndex].StringId;
                    break;
                }

                case CommissionCategory.VillageDefense:
                {
                    // 村防应援：严格限制为附近村庄
                    var villages = Settlement.All
                        .Where(s => s.IsVillage && s != giverSettlement)
                        .OrderBy(s => V.Pos(giverSettlement).Distance(V.Pos(s))
                                      * (0.3f + MBRandom.RandomFloat * 1.2f))
                        .ToList();
                    if (villages.Count == 0) return false;
                    // 优选取最近 2 个村庄
                    int vIdx = villages.Count > 2 && MBRandom.RandomFloat < 0.7f
                        ? MBRandom.RandomInt(0, 2) : MBRandom.RandomInt(0, villages.Count);
                    data.TargetSettlementId = villages[vIdx].StringId;
                    break;
                }

                case CommissionCategory.HideoutClear:
                {
                    var hideouts = Settlement.All
                        .Where(s => s.IsHideout)
                        .OrderBy(s => V.Pos(giverSettlement).Distance(V.Pos(s))
                                      * (0.5f + MBRandom.RandomFloat * 1.5f))
                        .ToList();
                    if (hideouts.Count == 0) return false;
                    data.TargetSettlementId = hideouts.First().StringId;
                    break;
                }

                default:
                {
                    var other = Settlement.All
                        .Where(s => s != giverSettlement)
                        .OrderBy(s => V.Pos(giverSettlement).Distance(V.Pos(s))
                                      * (0.5f + MBRandom.RandomFloat * 1.5f))
                        .FirstOrDefault();
                    data.TargetSettlementId = other?.StringId ?? giverSettlement.StringId;
                    break;
                }
            }
            return !string.IsNullOrEmpty(data.TargetSettlementId);
        }

        private static bool FillTargetItem(CommissionData data, CommissionDef def)
        {
            switch (def.Category)
            {
                case CommissionCategory.SupplyEmergency:
                    var tradeGoods = MBObjectManager.Instance.GetObjectTypeList<ItemObject>()
                        .Where(item => item.IsTradeGood)
                        .OrderBy(_ => MBRandom.RandomFloat).ToList();
                    if (tradeGoods.Count == 0)
                    {
                        tradeGoods = MBObjectManager.Instance.GetObjectTypeList<ItemObject>()
                            .Where(item => item.IsFood).OrderBy(_ => MBRandom.RandomFloat).ToList();
                    }
                    if (tradeGoods.Count == 0) return false;
                    data.TargetItemId = tradeGoods.First().StringId;
                    data.TargetItemCount = 4 + MBRandom.RandomInt(12);
                    break;

                case CommissionCategory.EmergencyDelivery:
                    var foods = MBObjectManager.Instance.GetObjectTypeList<ItemObject>()
                        .Where(item => item.IsFood).OrderBy(_ => MBRandom.RandomFloat).ToList();
                    if (foods.Count == 0) return false;
                    data.TargetItemId = foods.First().StringId;
                    data.TargetItemCount = 8 + MBRandom.RandomInt(20);
                    break;

                case CommissionCategory.HorseAcquisition:
                    var horses = MBObjectManager.Instance.GetObjectTypeList<ItemObject>()
                        .Where(item => item.IsMountable && item.HorseComponent != null)
                        .Where(item => item.Tier >= ItemObject.ItemTiers.Tier2)
                        .OrderBy(_ => MBRandom.RandomFloat).ToList();
                    if (horses.Count == 0) return false;
                    data.TargetItemId = horses.First().StringId;
                    data.TargetItemCount = 1;
                    break;

                case CommissionCategory.ProcurementAgent:
                    var equipment = MBObjectManager.Instance.GetObjectTypeList<ItemObject>()
                        .Where(item => item.IsMountable || item.WeaponComponent != null)
                        .Where(item => item.Tier >= ItemObject.ItemTiers.Tier2)
                        .OrderBy(_ => MBRandom.RandomFloat).ToList();
                    if (equipment.Count == 0) return false;
                    data.TargetItemId = equipment.First().StringId;
                    data.TargetItemCount = 1;
                    break;

                default: return false;
            }
            return !string.IsNullOrEmpty(data.TargetItemId);
        }

        public static int NegotiateReward(int baseReward, Hero questGiver)
        {
            float charmSkill = Hero.MainHero.GetSkillValue(DefaultSkills.Charm);
            float roll = MBRandom.RandomFloat;
            float negotiateFactor = 0.8f + (charmSkill / 300f) * 0.4f + roll * 0.2f;
            int finalReward = (int)(baseReward * MathF.Clamp(negotiateFactor, 0.8f, 1.4f));
            finalReward = Math.Max((int)(baseReward * 0.8f), Math.Min((int)(baseReward * 1.2f), finalReward));
            return finalReward;
        }

        /// <summary>从委托人所在定居点随机选另一个 NPC 作为结账人。</summary>
        private static Hero PickAlternatePayer(Hero questGiver)
        {
            Settlement giverSettlement = questGiver?.CurrentSettlement
                ?? questGiver?.HomeSettlement;
            if (giverSettlement == null) return questGiver;

            var candidates = giverSettlement.Notables
                .Where(n => n != null && n != questGiver && n.IsAlive)
                .ToList();
            if (candidates.Count == 0) return questGiver;

            return candidates[MBRandom.RandomInt(0, candidates.Count)];
        }

        /// <summary>
        /// 检查此 NPC 是否是对应定居点犯罪事件的权威人物（村长/族长/领主），
        /// 用于 ! 标记显示。
        /// </summary>
        private static bool IsAuthorityForActiveCrimeEvent(Hero hero)
        {
            if (hero == null || string.IsNullOrEmpty(hero.StringId)) return false;
            var settlement = hero.CurrentSettlement ?? hero.HomeSettlement;
            if (settlement == null) return false;

            var evt = WorldEventStore.FindActive(settlement.StringId);
            if (evt == null) return false;
            if (evt.Stage == EventStage.Dormant) return false;  // 还没被发现

            var authority = WorldEventStore.GetAuthorityNpc(evt);
            return authority == hero;
        }

        /// <summary>
        /// 尝试为犯罪事件的权威 NPC 生成追责 Quest 数据（不创建 Quest，仅返回 CommissionData）。
        /// 返回 null = 无需生成或不符合条件。
        /// </summary>
        internal static CommissionData TryGenerateAccountabilityQuest(Hero hero)
        {
            var settlement = hero.CurrentSettlement ?? hero.HomeSettlement;
            if (settlement == null) return null;

            var evt = WorldEventStore.FindActive(settlement.StringId);
            if (evt == null) return null;
            if (evt.Stage == EventStage.Dormant || evt.Stage == EventStage.Resolved || evt.Stage == EventStage.Unsolved)
                return null;

            // 确认此 NPC 是权威人物
            var authority = WorldEventStore.GetAuthorityNpc(evt);
            if (authority != hero) return null;

            // 已生成过则不重复
            if (Campaign.Current.QuestManager.Quests.Any(q =>
                q is CommissionQuest cq && cq.CommissionGiver == hero
                && cq.GetType().GetField("_data", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.GetValue(cq) is CommissionData cd && cd.WorldEventId == evt.EventId))
                return null;

            CommissionData data = null;

            switch (evt.Stage)
            {
                case EventStage.Emerging:
                    // 调查 Quest（蓝色 !）—— 阶段 1: 还没查出是谁
                    data = new CommissionData
                    {
                        DefId = "investigation",
                        Category = CommissionCategory.Investigation,
                        QuestGiver = hero,
                        TargetHero = null,  // 还不知道嫌犯是谁
                        TargetSettlementId = evt.TargetSettlementId,
                        NegotiatedReward = 150,
                        DepositAmount = 30,
                        TimeRemainingHours = (evt.Config?.InvestigationWindowDays ?? 7) * 24f,
                        Tier = CommissionTier.Basic,
                        WorldEventId = evt.EventId,
                    };
                    DebugLogger.Log($"[CommissionGen] Accountability Investigation quest: hero={hero.Name} event={evt.EventId} stage=Emerging reward={data.NegotiatedReward}");
                    break;

                case EventStage.Active:
                    // 悬赏 Quest（黄色 !）—— 阶段 2: 嫌犯已确定
                    if (evt.SuspectIsPlayer)
                        return null;  // 玩家是嫌犯 → 不生成悬赏 Quest（用对话替代）

                    var suspect = Hero.FindFirst(h => h.StringId == evt.SuspectHeroId);
                    if (suspect == null) return null;

                    data = new CommissionData
                    {
                        DefId = "bounty_hunt",
                        Category = CommissionCategory.BountyHunt,
                        QuestGiver = hero,
                        TargetHero = suspect,
                        TargetSettlementId = evt.TargetSettlementId,
                        NegotiatedReward = evt.ComputeBountyAmount(),
                        TimeRemainingHours = 15 * 24f,
                        WorldEventId = evt.EventId,
                    };
                    DebugLogger.Log($"[CommissionGen] Accountability Bounty quest: hero={hero.Name} event={evt.EventId} suspect={suspect.Name} reward={data.NegotiatedReward}");
                    break;

                case EventStage.Confrontation:
                    // 报复 Quest（红色 !）—— 阶段 3
                    if (evt.SuspectIsPlayer)
                        return null;  // 玩家是嫌犯 → 报复部队自动追玩家

                    data = new CommissionData
                    {
                        DefId = "village_defense",
                        Category = CommissionCategory.VillageDefense,
                        QuestGiver = hero,
                        TargetHero = Hero.FindFirst(h => h.StringId == evt.SuspectHeroId),
                        TargetSettlementId = evt.TargetSettlementId,
                        NegotiatedReward = evt.ComputeBountyAmount() / 2,  // 带队报复酬劳
                        TimeRemainingHours = 10 * 24f,
                        WorldEventId = evt.EventId,
                    };
                    DebugLogger.Log($"[CommissionGen] Accountability Retaliation quest: hero={hero.Name} event={evt.EventId} stage=Confrontation");
                    break;
            }

            return data;
        }
    }
}
