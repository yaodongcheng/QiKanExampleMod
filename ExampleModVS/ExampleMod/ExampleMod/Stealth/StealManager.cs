using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    public class StealManager
    {
        // ----------------------------------------------------------------
        // 0. 失窃记录：本场 Mission 内玩家从某 victim 身上偷走的物品，用于「归还」。
        //    用 ConditionalWeakTable（弱引用键），Mission 结束 Agent 被 GC 后自动清，无泄漏。
        // ----------------------------------------------------------------
        private struct StolenEntry
        {
            public EquipmentIndex Slot;      // 物品来源槽位（金钱条目无意义）
            public EquipmentElement Element; // 偷走的物品（带品质）；金钱条目时为空
            public int StashTaken;           // 从受害者辎重实扣的物品数（金钱条目无意义）
            public int Gold;                 // 偷走的金钱面额（物品条目时为 0）
        }
        private static readonly ConditionalWeakTable<Agent, List<StolenEntry>> _stolenLog
            = new ConditionalWeakTable<Agent, List<StolenEntry>>();

        /// <summary>本场是否从该 victim 身上偷过东西（还没归还）。</summary>
        public static bool HasStolenItemsFrom(Agent victim)
            => victim != null && _stolenLog.TryGetValue(victim, out var list) && list.Count > 0;

        /// <summary>
        /// 本场从该 victim 身上偷走的赃物总价值（市场基准价之和）。
        /// 用于「破财消灾」赔偿额——偷得越贵赔得越多，而不是写死。
        /// 注：金钱(Item==null)按面额计；物品按 ItemObject.Value（不含品质加成）。
        /// </summary>
        public static int GetStolenValue(Agent victim)
        {
            if (victim == null || !_stolenLog.TryGetValue(victim, out var list)) return 0;
            int total = 0;
            foreach (var e in list)
                total += (e.Element.Item != null ? e.Element.Item.Value : e.Gold);
            return total;
        }

        /// <summary>
        /// 归还：把本场从 <paramref name="victim"/> 偷走的赃物从玩家背包交出，对称复原——
        /// 复原其穿戴外观，并把当初从其 party 辎重实扣的库存还回辎重。玩家已卖/丢的跳过。
        /// 返回实际归还件数。
        /// </summary>
        public static int ReturnStolenItems(Agent victim)
        {
            if (victim == null || !_stolenLog.TryGetValue(victim, out var list) || list.Count == 0)
                return 0;

            Hero victimHero = (victim.Character as CharacterObject)?.HeroObject;
            Equipment newEquipment = victim.IsActive() ? victim.SpawnEquipment.Clone() : null;
            int returned = 0;

            foreach (var entry in list)
            {
                // 金钱条目：等额还给受害者（守恒转移）
                if (entry.Element.Item == null && entry.Gold > 0)
                {
                    if (AgentControlHelper.TransferGold(Hero.MainHero, victimHero, entry.Gold, notify: false) > 0)
                        returned++;
                    continue;
                }

                // 1. 玩家交出赃物（穿戴件，可能带品质）：玩家背包 → 世界
                int removed = AgentControlHelper.TransferItems(Hero.MainHero, null, entry.Element, 1);
                if (removed <= 0) continue; // 背包里已经没有了（卖了/丢了），还不出

                // 2. 复原受害者穿戴外观（仅当原槽现在为空，避免覆盖他后换上的东西）
                if (newEquipment != null && newEquipment[entry.Slot].IsEmpty)
                    newEquipment[entry.Slot] = entry.Element;

                // 3. 把当初从其 party 辎重实扣的真实库存还回去：世界 → 受害者辎重
                if (entry.StashTaken > 0 && victimHero?.PartyBelongedTo != null)
                    AgentControlHelper.TransferItems(null, victimHero, entry.Element.Item, entry.StashTaken);

                returned++;
            }

            // 4. 刷新受害者视觉 / 战斗属性
            if (newEquipment != null && returned > 0)
            {
                victim.UpdateSpawnEquipmentAndRefreshVisuals(newEquipment);
                victim.UpdateAgentStats();
            }

            _stolenLog.Remove(victim);
            return returned;
        }

        private static void RecordStolen(Agent victim, EquipmentIndex slot, EquipmentElement element, int stashTaken)
        {
            if (victim == null || element.IsEmpty) return;
            _stolenLog.GetOrCreateValue(victim).Add(new StolenEntry { Slot = slot, Element = element, StashTaken = stashTaken });
        }

        /// <summary>
        /// 「偷钱」路径的失窃登记入口。当前扒窃流程只偷装备(StealSpecificItem)，
        /// 此方法留给未来的金钱被窃路径调用——记下后 GetStolenValue 计入面额、ReturnStolenItems 等额返还。
        /// </summary>
        public static void RecordStolenGold(Agent victim, int amount)
        {
            if (victim == null || amount <= 0) return;
            _stolenLog.GetOrCreateValue(victim).Add(new StolenEntry { Gold = amount });
        }

        // ----------------------------------------------------------------
        // 1. 辅助方法：从 NPC 身上随机找一件装备的槽位 (用于“摸索”阶段)
        // ----------------------------------------------------------------
        public static EquipmentIndex? GetRandomStealableItemIndex(Agent agent)
        {
            if (agent == null) return null;

            List<EquipmentIndex> validIndices = new List<EquipmentIndex>();

            // 遍历所有可能的装备槽位 (头、身、手、腿、4个武器槽、马)
            for (int i = 0; i < 12; i++)
            {
                EquipmentIndex index = (EquipmentIndex)i;
                EquipmentElement element = agent.SpawnEquipment[index];

                // 只有当这个槽位有东西，且不是不可拾取的隐藏物品时
                if (!element.IsEmpty && element.Item != null)
                {
                    validIndices.Add(index);
                }
            }

            if (validIndices.Count == 0) return null;

            // 随机返回一个槽位
            Random rand = new Random();
            return validIndices[rand.Next(validIndices.Count)];
        }
        // ----------------------------------------------------------------
        // 2. 核心业务：执行偷取单件物品 (用于“拿走”阶段)
        // ----------------------------------------------------------------
        /// <summary>
        /// 从 NPC 身上移除指定槽位的物品，并放入玩家背包
        /// </summary>
        /// <returns>返回偷到的物品名称，如果没有则返回 null</returns>
        public static string StealSpecificItem(Agent agent, EquipmentIndex index)
        {
            if (agent == null || agent.SpawnEquipment[index].IsEmpty) return null;

            // 1. 获取物品数据
            EquipmentElement itemToSteal = agent.SpawnEquipment[index];
            string itemName = itemToSteal.Item.Name.ToString();

            // 2. 玩家获得赃物（穿戴件，带品质）：从「身上(世界)」grant 到玩家队伍背包
            if (PartyBase.MainParty != null)
            {
                AgentControlHelper.TransferItems(null, Hero.MainHero, itemToSteal, 1);
            }

            // 2b. 真实损失：受害者若有 party，从其辎重(ItemRoster)扣一件同款——保证不是「假偷」。
            //     辎重里恰好没有同款则不扣(返回0)；记下实扣数，供归还对称还回。
            int stashTaken = 0;
            Hero victimHero = (agent.Character as CharacterObject)?.HeroObject;
            if (victimHero?.PartyBelongedTo != null)
                stashTaken = AgentControlHelper.TransferItems(victimHero, null, itemToSteal.Item, 1);

            // 记录失窃（槽位 + 带品质元素 + 辎重实扣数），供「归还」对称复原
            RecordStolen(agent, index, itemToSteal, stashTaken);

            // 偷窃账本记账（犯罪后果系统用）
            if (victimHero != null && Settlement.CurrentSettlement != null)
            {
                PlayerTheftLedger.Record(
                    victimHeroId: victimHero.StringId,
                    settlementId: Settlement.CurrentSettlement.StringId,
                    itemId: itemToSteal.Item.StringId,
                    count: 1,
                    locationName: $"在{Settlement.CurrentSettlement.Name}"
                );
            }

            // 3. 从 NPC 身上移除该物品 (修改视觉)
            Equipment newEquipment = agent.SpawnEquipment.Clone();
            newEquipment[index] = EquipmentElement.Invalid; // 设置为空

            // 4. 刷新 NPC 模型
            agent.UpdateSpawnEquipmentAndRefreshVisuals(newEquipment);

            // 5. 如果偷的是手中的武器，强制收起或丢弃
            if (index >= EquipmentIndex.WeaponItemBeginSlot && index <= EquipmentIndex.Weapon3)
            {
                agent.UpdateAgentStats(); // 刷新战斗属性
            }

            return itemName;
        }
        /// <summary>
        /// 扒掉 Agent 的装备。
        /// remainingRoster = null → 无条件扒光所有武器/防具（"全部拿走"）。
        /// remainingRoster 非 null → 只扒槽内物品不在 roster 中的（"自己挑选"后被拿走的）。
        /// </summary>
        public static void StripAgentEquipment(Agent agent, bool stripWeapons, bool stripArmor, ItemRoster remainingRoster = null)
        {
            if (agent == null) return;

            Equipment newEquipment = agent.SpawnEquipment.Clone();
            bool anyChange = false;

            // 尸体（ragdoll）不能重新 wield 武器：UpdateSpawnEquipmentAndRefreshVisuals 内部会对
            // SpawnEquipment 里残留的武器执行 WieldInitialWeapons → native TryToWieldWeaponInSlot，
            // 而死人的骨骼已交给物理系统，再去握武器会操作失效内存 → AccessViolation。
            // 因此对尸体一律清空所有武器槽（与"全部拿走"路径等价：无武器可 wield 即安全）。
            // 昏迷(Unconscious)同样是 ragdoll，IsActive()=false 一并覆盖。
            bool isCorpse = !agent.IsActive();

            // 防具槽位
            var armorSlots = new[] { EquipmentIndex.Head, EquipmentIndex.Body, EquipmentIndex.Leg, EquipmentIndex.Gloves, EquipmentIndex.Cape };

            if (stripArmor)
            {
                foreach (var slot in armorSlots)
                {
                    if (TryStripSlot(agent.SpawnEquipment[slot], slot, remainingRoster, ref newEquipment))
                        anyChange = true;
                }
            }

            if (stripWeapons)
            {
                for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i <= EquipmentIndex.Weapon3; i++)
                {
                    // 尸体：传 null → 无条件清空（绝不能给 ragdoll 留武器去 wield）；
                    // 活人：按 remainingRoster 精准扒（玩家拿走的才扒，活人可正常重新 wield 剩下的）。
                    ItemRoster slotFilter = isCorpse ? null : remainingRoster;
                    if (TryStripSlot(agent.SpawnEquipment[i], i, slotFilter, ref newEquipment))
                        anyChange = true;
                }
            }

            // remainingRoster 为 null 时始终刷新（保持原行为）；非 null 时只有变化才刷新
            if (remainingRoster == null || anyChange)
            {
                agent.UpdateSpawnEquipmentAndRefreshVisuals(newEquipment);

                if (stripWeapons)
                {
                    if (agent.Equipment[EquipmentIndex.WeaponItemBeginSlot].IsEmpty)
                    {
                        agent.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);
                        agent.TryToSheathWeaponInHand(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.Instant);
                    }
                    agent.UpdateAgentStats();
                }
            }
        }

        private static bool TryStripSlot(EquipmentElement element, EquipmentIndex slot, ItemRoster remainingRoster, ref Equipment newEquipment)
        {
            if (element.IsEmpty || element.Item == null) return false;
            // remainingRoster 为 null → 无条件扒；非 null → 只在玩家拿走后不在 roster 中时扒
            if (remainingRoster != null && remainingRoster.GetItemNumber(element.Item) > 0) return false;

            newEquipment[slot] = EquipmentElement.Invalid;
            return true;
        }


        public static List<Agent> GetWitnesses(Agent thief, Agent victim, float maxDistance = 15f, float fovDegrees = 120f)
        {
            // 统一走 NpcSightSystem 的视线检测（FOV+RayCast），只额外过滤 victim
            List<Agent> witnesses = NpcSightSystem.GetObserversOf(thief, maxDistance, fovDegrees);
            witnesses.RemoveAll(a => a == victim);
            return witnesses;
        }

        // ----------------------------------------------------------------
        // 3. 动物偷窃：WorldEvent 创建 + PlayerTheftLedger 记账 + 目击者记录
        //    从 InteractionMissionView 迁入，与 StealSpecificItem 的 PlayerTheftLedger
        //    记账保持在同一处，统一 Stealth 子系统的犯罪记录入口。
        // ----------------------------------------------------------------
        /// <summary>
        /// 偷动物成功后创建 WorldEvent + PlayerTheftLedger 记账 + 目击者记录。
        /// 目击者检测走统一的 <see cref="GetWitnesses"/>。
        /// </summary>
        public static void RecordAnimalTheft(Settlement settlement, ItemObject livestockItem, string monsterId, Agent animal)
        {
            try
            {
                // 目击系统开关：关闭时跳过目击检测，始终走 Dormant（无目击）路径
                bool witnessSystemOn = Settings.Instance.WitnessSystemEnabled;
                List<string> witnessHeroIds;
                Dictionary<string, int> templateWitness;
                bool wasWitnessed;

                if (witnessSystemOn)
                {
                    var witnesses = GetWitnesses(Agent.Main, animal, maxDistance: 20f);
                    witnessHeroIds = witnesses
                        .Where(a => (a.Character as CharacterObject)?.HeroObject != null)
                        .Select(a => (a.Character as CharacterObject).HeroObject.StringId)
                        .ToList();
                    templateWitness = witnesses
                        .Where(a => (a.Character as CharacterObject)?.HeroObject == null && a.Character != null)
                        .GroupBy(a => a.Character.StringId)
                        .ToDictionary(g => g.Key, g => g.Count());
                    wasWitnessed = witnessHeroIds.Count > 0 || templateWitness.Count > 0;

                    if (wasWitnessed)
                        DebugLogger.Log($"[AnimalTheft] Witnessed! {witnessHeroIds.Count} hero(es) + {templateWitness.Sum(kv => kv.Value)} villagers saw the theft. Suspect = Player.");
                    else
                        DebugLogger.Log($"[AnimalTheft] No witnesses.");
                }
                else
                {
                    witnessHeroIds = new List<string>();
                    templateWitness = new Dictionary<string, int>();
                    wasWitnessed = false;
                    DebugLogger.Log($"[AnimalTheft] Witness system DISABLED — treating as no witnesses.");
                }

                var evt = new WorldEvent
                {
                    EventId = $"theft_{settlement.StringId}_{(float)CampaignTime.Now.ToDays}_{WorldEventStore.AllEvents.Count(e => e.TargetSettlementId == settlement.StringId) + 1}",
                    Category = EventCategory.Crime,
                    Type = EventType.Theft_Animal,
                    Severity = 30,
                    InitiatorId = Hero.MainHero.StringId,
                    TargetSettlementId = settlement.StringId,
                    TargetItemId = livestockItem.StringId,
                    Quantity = 1,
                    OccurredDay = (float)CampaignTime.Now.ToDays,
                    DayLimit = 14f,
                    LocationName = settlement.Name?.ToString() ?? "村庄",
                    WitnessHeroIds = witnessHeroIds,
                    TemplateWitness = templateWitness,
                    Stage = wasWitnessed ? EventStage.Active : EventStage.Dormant,
                    SuspectHeroId = wasWitnessed ? Hero.MainHero.StringId : null,
                    InvestigationProgress = wasWitnessed ? 1.0f : 0f,
                    PublicAwareness = wasWitnessed ? 0.5f : 0f,
                    EvidenceList = witnessHeroIds.Count > 0
                        ? new List<EvidencePointer>
                        {
                            new EvidencePointer
                            {
                                EvidenceId = $"theft_{settlement.StringId}_witness",
                                TargetId = Hero.MainHero.StringId,
                                Kind = EvidenceKind.Witness,
                                Strength = 0.7f,
                                SourceDescription = $"目击者称看到有人在牲口圈附近鬼鬼祟祟",
                                DiscoveredDay = (float)CampaignTime.Now.ToDays
                            }
                        }
                        : new List<EvidencePointer>(),
                };
                WorldEventStore.AddOrMerge(evt);

                // 偷窃账本记账
                PlayerTheftLedger.Record(
                    victimHeroId: null,
                    settlementId: settlement.StringId,
                    itemId: livestockItem.StringId,
                    count: 1,
                    locationName: $"在{settlement.Name}"
                );
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[AnimalTheft] RecordAnimalTheft error: {ex.Message}");
            }
        }

        // ----------------------------------------------------------------
        // 4. 动物偷窃核心业务：库存转移 + 追踪 + 犯罪记账
        //    对应 TryStealFromAgent → StealSpecificItem 的分层模式。
        //    View 层（InteractionMissionView.TryStealAnimal）负责动画/UI/FadeOut。
        // ----------------------------------------------------------------
        /// <summary>
        /// 偷动物核心事务：物品授予玩家 → 定居点库存扣除 → 偷窃追踪 → 犯罪记账。
        /// 参数 <paramref name="livestockItem"/> 应由调用方通过 ItemObject 查找提前解析。
        /// </summary>
        /// <param name="settlement">当前村庄（null 时仅授予物品，不扣库存不记账）</param>
        /// <param name="livestockItem">已解析的牲畜 ItemObject</param>
        /// <param name="monsterId">动物 monster ID（用于 VillageAnimalTracker 追踪）</param>
        /// <param name="animal">被偷的动物 Agent（用于目击者检测）</param>
        public static void StealAnimal(Settlement settlement, ItemObject livestockItem, string monsterId, Agent animal)
        {
            // 步骤 1：物品授予玩家（Grant from world，铁律 4.②）
            AgentControlHelper.TransferItems(null, Hero.MainHero, new EquipmentElement(livestockItem, null), 1);

            if (settlement == null || !settlement.IsVillage) return;

            // 步骤 2：从定居点库存扣除（Sink to world，铁律 4.②）
            int currentStock = settlement.ItemRoster.GetItemNumber(livestockItem);
            if (currentStock > 0)
            {
                settlement.ItemRoster.AddToCounts(livestockItem, -1);
                DebugLogger.Log($"[StealAnimal] {animal.Name} (monster={monsterId}) → {livestockItem.StringId}, {settlement.Name} stock: {currentStock}→{currentStock - 1}");
            }
            else
            {
                DebugLogger.Log($"[StealAnimal] {animal.Name} (monster={monsterId}) → {livestockItem.StringId}, {settlement.Name} stock: 0 — skip deduction");
            }

            // 步骤 3：偷窃追踪（持久化，自然恢复：每天每种恢复 1 只）
            VillageAnimalTracker.RecordTheft(settlement.StringId, monsterId);

            // 步骤 4：WorldEvent 创建 + PlayerTheftLedger 记账 + 目击者记录
            RecordAnimalTheft(settlement, livestockItem, monsterId, animal);
        }
    }
}
