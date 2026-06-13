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
            if (IsBrokerType(hero))
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
                           occ == Occupation.Lord || occ == Occupation.Wanderer;

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
                    if (!FillTargetHero(data, def)) return null;
                    break;
                case CommissionTargetType.Settlement:
                    if (!FillTargetSettlement(data, def, questGiver)) return null;
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
            switch (def.Category)
            {
                case CommissionCategory.BountyHunt:
                case CommissionCategory.LegendaryHunt:
                    var banditHeroes = Hero.AllAliveHeroes
                        .Where(h => h != Hero.MainHero && h != data.QuestGiver && h.IsAlive
                            && h.PartyBelongedTo == null
                            && (h.Occupation == Occupation.Bandit || h.MapFaction != Hero.MainHero.MapFaction)
                            && !CommissionQuest.IsHeroInvolvedInActiveCommission(h, out _, out _))
                        .OrderBy(_ => MBRandom.RandomFloat).ToList();
                    if (banditHeroes.Count == 0) return false;
                    data.TargetHero = banditHeroes.First();
                    break;

                case CommissionCategory.PrisonBreak:
                    var prisoners = Hero.AllAliveHeroes
                        .Where(h => h != Hero.MainHero && h != data.QuestGiver && h.IsPrisoner
                            && !CommissionQuest.IsHeroInvolvedInActiveCommission(h, out _, out _))
                        .OrderBy(_ => MBRandom.RandomFloat).ToList();
                    if (prisoners.Count == 0) return false;
                    data.TargetHero = prisoners.First();
                    break;

                default:
                    var candidates = Hero.AllAliveHeroes
                        .Where(h => h != Hero.MainHero && h != data.QuestGiver && h.IsAlive
                            && !CommissionQuest.IsHeroInvolvedInActiveCommission(h, out _, out _))
                        .OrderBy(_ => MBRandom.RandomFloat).ToList();
                    if (candidates.Count == 0) return false;
                    data.TargetHero = candidates.First();
                    break;
            }
            return data.TargetHero != null;
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
                    var towns = Settlement.All
                        .Where(s => (s.IsTown || s.IsCastle) && s != giverSettlement)
                        .OrderBy(_ => MBRandom.RandomFloat).ToList();
                    if (towns.Count == 0) return false;
                    data.TargetSettlementId = towns.First().StringId;
                    break;

                case CommissionCategory.HideoutClear:
                    var hideouts = Settlement.All
                        .Where(s => s.IsHideout)
                        .OrderBy(_ => MBRandom.RandomFloat).ToList();
                    if (hideouts.Count == 0) return false;
                    data.TargetSettlementId = hideouts.First().StringId;
                    break;

                default:
                    var other = Settlement.All
                        .Where(s => s != giverSettlement)
                        .OrderBy(_ => MBRandom.RandomFloat).FirstOrDefault();
                    data.TargetSettlementId = other?.StringId ?? giverSettlement.StringId;
                    break;
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
    }
}
