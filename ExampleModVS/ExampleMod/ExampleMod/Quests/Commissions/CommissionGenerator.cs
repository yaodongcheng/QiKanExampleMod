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

            // 世界事件驱动：NPC 是附近活跃事件的受害者 → 强制显示 !
            if (IsHeroInNearbyWorldEvent(hero))
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
                float distance = brokerSettlement.Position2D.Distance(s.Position2D);
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

        private static List<CommissionDef> GetAvailableDefsForHero(Hero hero)
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
                float dist = giverSettlement.Position2D.Distance(heroSettlement.Position2D);
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
            return WorldEventDatabase.ActiveEvents.Any(e =>
                e.InstigatorHeroId == hero.StringId || e.TargetHeroId == hero.StringId);
        }

        /// <summary>检查 Hero 是否在附近活跃世界事件中作为受害者（用于 ! 标记显示）。</summary>
        private static bool IsHeroInNearbyWorldEvent(Hero hero)
        {
            if (hero == null || string.IsNullOrEmpty(hero.StringId)) return false;
            Settlement heroSettlement = hero.CurrentSettlement ?? hero.HomeSettlement;
            if (heroSettlement == null) return false;
            return WorldEventDatabase.ActiveEvents.Any(e =>
                e.TargetHeroId == hero.StringId
                && e.TargetSettlement != null
                && e.TargetSettlement.Position2D.Distance(heroSettlement.Position2D) < 80f);
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
            var nearbyEvents = WorldEventDatabase.GetActiveEventsNear(giverSettlement, maxDistance: 80f);
            if (nearbyEvents.Count == 0) return false;

            // 按委托类别筛选匹配的事件
            foreach (var worldEvent in nearbyEvents)
            {
                if (!IsWorldEventMatchForCategory(worldEvent.EventType, def.Category))
                    continue;

                // 匹配！填充 CommissionData
                data.WorldEventId = worldEvent.EventId;
                data.IsGenericInstigator = worldEvent.IsGenericInstigator;

                switch (def.TargetType)
                {
                    case CommissionTargetType.NamedHero:
                        // 目标 = 加害方（匪首/绑匪/背叛者…）
                        var instigator = worldEvent.InstigatorHero;
                        if (instigator != null && instigator.IsAlive)
                        {
                            data.TargetHero = instigator;
                        }
                        else if (worldEvent.IsGenericInstigator)
                        {
                            // 🐛 修复：通用匪帮没有真实 Hero，但仍应匹配事件。
                            // 将目标落脚点设为目标定居点（匪帮最后出现在受害人所在地附近），
                            // 委托叙事层通过 WorldEventId 输出事件专属文本。
                            data.TargetSettlementId = worldEvent.TargetSettlementId;
                            DebugLogger.Log($"[CommissionGen] Matched WorldEvent with generic instigator: category={def.Category} event={worldEvent.EventType} settlement={data.TargetSettlementId}");
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

                DebugLogger.Log($"[CommissionGen] Matched WorldEvent! category={def.Category} event={worldEvent.EventType} eventId={worldEvent.EventId} target={data.TargetHero?.Name?.ToString() ?? data.TargetSettlementId}");
                return true;
            }

            return false;
        }

        /// <summary>检查 WorldEventType 是否匹配 CommissionCategory。</summary>
        private static bool IsWorldEventMatchForCategory(WorldEventType eventType, CommissionCategory category)
        {
            var config = WorldEventConfig.Get(eventType);
            if (config?.MatchingCommissions == null) return false;

            foreach (var c in config.MatchingCommissions)
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
                        .OrderBy(s => giverSettlement.Position2D.Distance(s.Position2D)
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
                        .OrderBy(s => giverSettlement.Position2D.Distance(s.Position2D)
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
                        .OrderBy(s => giverSettlement.Position2D.Distance(s.Position2D)
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
                        .OrderBy(s => giverSettlement.Position2D.Distance(s.Position2D)
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
    }
}
