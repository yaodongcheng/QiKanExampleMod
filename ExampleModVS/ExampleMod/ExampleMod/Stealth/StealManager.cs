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
using TaleWorlds.DotNet;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    public class StealManager
    {
        // ── 🆕 偷窃 UI 状态（Phase 1：AgentBrain.UpdateAlertCognition 中检测 StealUIOpen）──
        /// <summary>偷窃/物品 UI 是否当前打开。由 StealVM / InteractionMissionView 设置。</summary>
        public static bool IsUIOpen { get; set; }

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
                TheftLedger.Record(
                    initiatorId: Hero.MainHero.StringId,
                    victimHeroId: victimHero.StringId,
                    settlementId: Settlement.CurrentSettlement.StringId,
                    itemId: itemToSteal.Item.StringId,
                    count: 1,
                    locationName: $"在{Settlement.CurrentSettlement.Name}",
                    worldEventId: AgentAIController.Instance?.PendingWorldEvent?.EventId
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

            // 6. 顺手牵羊：偷装备的同时摸走一些零钱（来自村庄财富分配）
            TryStealPocketGold(agent);

            return itemName;
        }

        /// <summary>偷装备时顺手摸走 NPC 身上的零钱（村庄分配金 + 族长家族金库）。</summary>
        private static void TryStealPocketGold(Agent agent)
        {
            int totalStolen = 0;

            // 来源 1：村庄分配金（所有 NPC 通用）
            int agentGold = GetAgentGold(agent);
            if (agentGold > 0)
            {
                int goldToSteal = Math.Min(agentGold, MBRandom.RandomInt(1, 15));
                if (goldToSteal > 0)
                {
                    int actual = ConsumeAgentGold(agent, goldToSteal, Settlement.CurrentSettlement);
                    if (actual > 0)
                    {
                        RecordStolenGold(agent, actual);
                        totalStolen += actual;
                    }
                }
            }

            // 来源 2：族长家族金库（Hero.Gold = 全族资金，非族长不碰）
            var hero = (agent.Character as CharacterObject)?.HeroObject;
            bool isClanLeader = hero != null && hero.Clan?.Leader == hero;
            if (isClanLeader && hero.Gold > 0)
            {
                int heroSteal = Math.Min(hero.Gold, MBRandom.RandomInt(1, 50));
                if (heroSteal > 0)
                {
                    int actual = AgentControlHelper.TransferGold(hero, Hero.MainHero, heroSteal, notify: false);
                    if (actual > 0)
                    {
                        RecordStolenGold(agent, actual);
                        totalStolen += actual;
                    }
                }
            }

            if (totalStolen > 0)
                InformationManager.DisplayMessage(new InformationMessage($"顺手摸到了 {totalStolen} 第纳尔。", Colors.Yellow));
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
        // 3. 动物偷窃：WorldEvent 创建 + TheftLedger 记账 + 目击者记录
        //    从 InteractionMissionView 迁入，与 StealSpecificItem 的 TheftLedger
        //    记账保持在同一处，统一 Stealth 子系统的犯罪记录入口。
        // ----------------------------------------------------------------
        /// <summary>
        /// 偷动物成功后记录目击证词到 PendingWorldEvent + TheftLedger 记账。
        /// 目击者检测走统一的 <see cref="GetWitnesses"/>。
        /// WorldEvent 的持久化延迟到离开场景时 FinalizePendingWorldEvent。
        /// </summary>
        public static void RecordAnimalTheft(Settlement settlement, ItemObject livestockItem, string monsterId, Agent animal)
        {
            try
            {
                // 目击系统开关：关闭时跳过目击检测
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

                // 有目击者 → 写入 PendingWorldEvent（离开场景时统一持久化）
                if (wasWitnessed)
                {
                    AgentAIController.Instance?.RegisterTheftWitnesses(
                        witnessHeroIds, templateWitness,
                        livestockItem.StringId, livestockItem.Name?.ToString() ?? livestockItem.StringId);
                }

                // 统一偷窃账本记账（赃物标注、栽赃系统依赖它）
                TheftLedger.Record(
                    initiatorId: Hero.MainHero.StringId,
                    victimHeroId: null,
                    settlementId: settlement.StringId,
                    itemId: livestockItem.StringId,
                    count: 1,
                    locationName: $"在{settlement.Name}",
                    worldEventId: AgentAIController.Instance?.PendingWorldEvent?.EventId
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

            // 步骤 4：WorldEvent 创建 + TheftLedger 记账 + 目击者记录
            RecordAnimalTheft(settlement, livestockItem, monsterId, animal);
        }


        // ================================================================
        // 5. 村庄财富分配系统 — 进村时把定居点金库的一部分分配到 NPC 身上+公共箱子
        // ================================================================

        /// <summary>每个 Agent 身上携带的金钱（来自财富分配），key = Agent.Index</summary>
        private static Dictionary<int, int> _agentGold = new Dictionary<int, int>();

        /// <summary>公共箱子里的金钱（还没被偷走的 20% 流通池份额）</summary>
        private static int _stashGold = 0;

        /// <summary>防重复分配：已分配过的定居点 StringId</summary>
        private static string _lastDistributedSettlementId = null;

        /// <summary>金库取多少比例出来流通（默认 100% = 全部流通）</summary>
        public static float CirculatingRatio { get; set; } = 1.0f;

        /// <summary>流通池里多少比例给 NPC，剩下的进公共箱子（默认 80%）</summary>
        public static float NpcShareRatio { get; set; } = 0.80f;

        /// <summary>箱子里的物品（仅元数据，实际扣除在偷窃时发生——懒扣除）</summary>
        public static ItemRoster ChestItemRoster { get; set; } = new ItemRoster();

        /// <summary>箱子对应的实体 GameEntity（InteractionMissionView 创建后回填）</summary>
        public static GameEntity ChestEntity { get; set; } = null;

        /// <summary>公共箱子金币（只读）</summary>
        public static int StashGold => _stashGold;

        /// <summary>获取定居点的 SettlementComponent（村庄→Village, 城镇→Town），取其 Gold。</summary>
        private static SettlementComponent GetGoldComponent(Settlement settlement)
        {
            if (settlement == null) return null;
            return (SettlementComponent)settlement.Town ?? settlement.Village;
        }

        /// <summary>从定居点金库扣钱并给玩家（守恒）。金库不足时自动截断。</summary>
        private static int DeductSettlementGold(Settlement settlement, int amount)
        {
            if (settlement == null || amount <= 0) return 0;
            return AgentControlHelper.TransferGold(settlement, Hero.MainHero, amount, notify: false);
        }

        /// <summary>
        /// 进村时调用：把定居点金库的一部分分配到 NPC 身上 + 公共箱子。
        /// 同一定居点只分配一次（防重复）。
        /// </summary>
        public static void DistributeSettlementWealth(Settlement settlement)
        {
            if (settlement == null) return;
            if (_lastDistributedSettlementId == settlement.StringId) return;

            _lastDistributedSettlementId = settlement.StringId;
            _agentGold = new Dictionary<int, int>();
            _stashGold = 0;
            ChestItemRoster = new ItemRoster();
            ChestEntity = null;

            try
            {
                // ── 财富计算 ──
                int treasury = GetGoldComponent(settlement)?.Gold ?? 0;
                if (treasury <= 0) return;

                int pool = (int)(treasury * CirculatingRatio);
                if (pool <= 0) return;

                int npcPool = (int)(pool * NpcShareRatio);
                _stashGold = pool - npcPool;

                // ── NPC 分配：五档身份权重 ──
                var agents = Mission.Current?.Agents;
                if (agents == null) return;

                var weightedAgents = new List<(Agent agent, int weight)>();
                int totalWeight = 0;
                int headmanCount = 0, notableCount = 0, heroCount = 0, templateCount = 0;
                int headmanGold = 0, notableGold = 0, heroGold = 0, templateGold = 0;

                foreach (Agent agent in agents)
                {
                    if (!agent.IsHuman || !agent.IsActive()) continue;
                    var character = agent.Character as CharacterObject;
                    var hero = character?.HeroObject;

                    int weight;
                    if (hero != null)
                    {
                        if (hero.Occupation == Occupation.Headman)
                            { weight = 10; headmanCount++; }
                        else if (hero.Occupation == Occupation.RuralNotable)
                            { weight = 7; notableCount++; }
                        else
                            { weight = 4; heroCount++; }
                    }
                    else
                    {
                        weight = 1; templateCount++;
                    }

                    weightedAgents.Add((agent, weight));
                    totalWeight += weight;
                }

                int distributedCount = 0;
                if (totalWeight > 0 && npcPool > 0)
                {
                    foreach (var (agent, weight) in weightedAgents)
                    {
                        int share = (int)((float)weight / totalWeight * npcPool);
                        if (share > 0)
                        {
                            _agentGold[agent.Index] = share;
                            distributedCount++;

                            // 按档位归账
                            var ch = (agent.Character as CharacterObject)?.HeroObject;
                            if (ch?.Occupation == Occupation.Headman) headmanGold += share;
                            else if (ch?.Occupation == Occupation.RuralNotable) notableGold += share;
                            else if (ch != null) heroGold += share;
                            else templateGold += share;
                        }
                    }
                }

                // ── 箱子物品（仅元数据，不动物资——懒扣除）──
                var settlementRoster = settlement.ItemRoster;
                for (int i = 0; i < settlementRoster.Count; i++)
                {
                    var item = settlementRoster.GetItemAtIndex(i);
                    if (item == null) continue;
                    if (item.Type == ItemObject.ItemTypeEnum.Animal) continue; // 动物场景里直接偷
                    int have = settlementRoster.GetElementNumber(i);
                    if (have > 0)
                        ChestItemRoster.AddToCounts(item, have);
                }

                // ── 汇总日志 ──
                var sb = new System.Text.StringBuilder();
                sb.Append($"[Wealth] {settlement.Name}: 金库{treasury} → 流通池{pool}, NPC池{npcPool}(");
                if (distributedCount > 0)
                {
                    var parts = new List<string>();
                    if (headmanCount > 0) parts.Add($"村长{headmanCount}人={headmanGold}");
                    if (notableCount > 0) parts.Add($"乡绅{notableCount}人={notableGold}");
                    if (heroCount > 0) parts.Add($"英雄{heroCount}人={heroGold}");
                    if (templateCount > 0) parts.Add($"村民{templateCount}人={templateGold}");
                    sb.Append(string.Join(", ", parts));
                }
                else
                {
                    sb.Append("无人分得");
                }
                sb.Append($"), 箱子{_stashGold}第纳尔, 物资{ChestItemRoster.Count}种");
                DebugLogger.Log(sb.ToString());
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Wealth] DistributeSettlementWealth error: {ex.Message}");
            }
        }

        /// <summary>查询 Agent 身上携带的分配金额</summary>
        public static int GetAgentGold(Agent agent)
        {
            if (agent == null) return 0;
            _agentGold.TryGetValue(agent.Index, out int gold);
            return gold;
        }

        /// <summary>
        /// 消费 Agent 身上的分配金额（懒扣除——真偷才扣定居点金库）。
        /// </summary>
        /// <returns>实际获得的金额（可能小于 requested）</returns>
        public static int ConsumeAgentGold(Agent agent, int requested, Settlement settlement)
        {
            if (agent == null || requested <= 0) return 0;
            if (!_agentGold.TryGetValue(agent.Index, out int have)) return 0;

            int actual = Math.Min(requested, have);
            _agentGold[agent.Index] = have - actual;

            // 从定居点金库真实扣除并给玩家
            DeductSettlementGold(settlement, actual);
            return actual;
        }

        /// <summary>
        /// 偷公共箱子里的金币（懒扣除——真偷才扣定居点金库）。
        /// </summary>
        /// <returns>实际获得的金额</returns>
        public static int LootStash(int requested, Settlement settlement)
        {
            if (requested <= 0) return 0;
            int actual = Math.Min(requested, _stashGold);
            if (actual <= 0) return 0;

            _stashGold -= actual;

            // 从定居点金库真实扣除并给玩家
            DeductSettlementGold(settlement, actual);
            return actual;
        }

        /// <summary>
        /// 偷箱子物品——真实扣除定居点 ItemRoster + 同步减少箱子显示。
        /// </summary>
        /// <returns>实际拿走的数量</returns>
        public static int LootChestItem(ItemObject item, int count, Settlement settlement)
        {
            if (item == null || count <= 0 || settlement?.ItemRoster == null) return 0;

            int actual = Math.Min(count, settlement.ItemRoster.GetItemNumber(item));
            if (actual <= 0) return 0;

            // 1. 从定居点 ItemRoster 真实扣除
            settlement.ItemRoster.AddToCounts(item, -actual);
            // 2. 箱子显示同步减少
            ChestItemRoster.AddToCounts(item, -actual);
            // 3. 给玩家
            AgentControlHelper.TransferItems(null, Hero.MainHero, item, actual);
            // 4. 犯罪记账
            TheftLedger.Record(
                initiatorId: Hero.MainHero.StringId,
                victimHeroId: null, settlementId: settlement.StringId,
                itemId: item.StringId, count: actual,
                locationName: $"在{settlement.Name}的保管箱");
            return actual;
        }

        /// <summary>
        /// 仅扣除定居点 ItemRoster（不修改 ChestItemRoster——已由 InventoryManager 原地修改）。
        /// 用于"自己挑选"路径：OpenScreenAsLoot 已把玩家拿走的物品从 roster 移除并给了玩家，
        /// 这里只需同步扣除定居点库存。
        /// </summary>
        public static void DeductSettlementItemsOnly(Settlement settlement, ItemObject item, int count)
        {
            if (settlement?.ItemRoster == null || item == null || count <= 0) return;
            int actual = Math.Min(count, settlement.ItemRoster.GetItemNumber(item));
            if (actual <= 0) return;
            settlement.ItemRoster.AddToCounts(item, -actual);
            TheftLedger.Record(
                initiatorId: Hero.MainHero.StringId,
                victimHeroId: null, settlementId: settlement.StringId,
                itemId: item.StringId, count: actual,
                locationName: $"在{settlement.Name}的保管箱");
        }

        /// <summary>克隆 ItemRoster（用于比较 OpenScreenAsLoot 前后差异）</summary>
        public static ItemRoster CloneItemRoster(ItemRoster source)
        {
            var copy = new ItemRoster();
            if (source == null) return copy;
            for (int i = 0; i < source.Count; i++)
            {
                var item = source.GetItemAtIndex(i);
                if (item != null)
                    copy.AddToCounts(item, source.GetElementNumber(i));
            }
            return copy;
        }

        /// <summary>清除财富分配数据（离开场景时调用）</summary>
        public static void ClearWealthDistribution()
        {
            _agentGold = new Dictionary<int, int>();
            _stashGold = 0;
            _lastDistributedSettlementId = null;
            ChestItemRoster = new ItemRoster();
            ChestEntity = null;
        }

        // ────────────────────────────────────────────────────────────────
        // 保管箱实体生成（从 InteractionMissionView 迁移至此，瘦身 View）
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 找到村长（Headman）或乡绅（RuralNotable）的 Agent 位置。
        /// </summary>
        internal static Vec3 FindHeadmanPosition()
        {
            var agents = Mission.Current?.Agents;
            if (agents == null) return Vec3.Zero;

            foreach (Agent agent in agents)
            {
                if (!agent.IsHuman || !agent.IsActive()) continue;
                var co = agent.Character as CharacterObject;
                var occ = co?.HeroObject?.Occupation;
                if (occ == Occupation.Headman || occ == Occupation.RuralNotable)
                    return agent.Position;
            }
            return Agent.Main?.Position ?? Vec3.Zero;
        }

        /// <summary>
        /// 递归扫描场景中所有 GameEntity，找到最合适的储物类道具，克隆并放到 targetPos。
        /// 评分策略：名字含 barrel/sack/crate/chest/basket 等关键词 → 高分；离目标越近 → 加分。
        /// </summary>
        internal static GameEntity FindAndCloneStorageProp(Scene scene, Vec3 targetPos)
        {
            var candidates = new List<(GameEntity entity, float score)>();
            var rootEntities = NativeObjectArray.Create();
            scene.GetRootEntities(rootEntities);

            foreach (NativeObject obj in rootEntities)
            {
                var entity = obj as GameEntity;
                if (entity != null)
                    CollectPropsRecursive(entity, targetPos, candidates);
            }

            if (candidates.Count == 0)
            {
                DebugLogger.Log("[Chest] Scan: no suitable storage props found in scene");
                return null;
            }

            // 按评分降序
            candidates.Sort((a, b) => b.score.CompareTo(a.score));
            var best = candidates[0];
            DebugLogger.Log($"[Chest] Scan: best candidate '{best.entity.Name}' (score={best.score:F0}), " +
                $"candidates={candidates.Count}, names=[{string.Join(", ", candidates.Take(5).Select(c => c.entity.Name))}]");

            // 克隆并移动到目标位置
            var clone = GameEntity.CopyFrom(scene, best.entity);
            if (clone != null)
            {
                MatrixFrame frame = clone.GetGlobalFrame();
                frame.origin = targetPos;
                clone.SetGlobalFrame(frame);
                DebugLogger.Log($"[Chest] Cloned '{best.entity.Name}' → moved to {targetPos}");
            }
            return clone;
        }

        /// <summary>递归遍历 entity 树，收集有可见 mesh 的储物道具。</summary>
        private static void CollectPropsRecursive(GameEntity entity, Vec3 targetPos,
            List<(GameEntity entity, float score)> candidates)
        {
            if (entity == null) return;

            if (entity.MultiMeshComponentCount > 0)
            {
                string name = (entity.Name ?? "").ToLower();
                float dist = entity.GlobalPosition.Distance(targetPos);

                // 名字评分：储物关键词
                float nameScore = 0;
                if (name.Contains("chest") || name.Contains("coffer") || name.Contains("strongbox"))
                    nameScore = 110;
                else if (name.Contains("barrel") || name.Contains("cask"))
                    nameScore = 100;
                else if (name.Contains("sack") || name.Contains("bag") || name.Contains("pouch"))
                    nameScore = 90;
                else if (name.Contains("crate") || name.Contains("box"))
                    nameScore = 85;
                else if (name.Contains("basket"))
                    nameScore = 80;
                else if (name.Contains("pot") || name.Contains("jar") || name.Contains("urn"))
                    nameScore = 70;
                else if (name.Contains("trunk"))
                    nameScore = 75;
                else
                    nameScore = 5; // 通用道具

                // 距离评分（越近越好，50m 范围衰减）
                float distScore = Math.Max(0, 50f - dist) * 2f;

                float total = nameScore + distScore;
                if (total > 55f) // 最低门槛：过滤完全不相关的
                    candidates.Add((entity, total));
            }

            // 递归子 entity
            for (int i = 0; i < entity.ChildCount; i++)
            {
                var child = entity.GetChild(i);
                if (child != null)
                    CollectPropsRecursive(child, targetPos, candidates);
            }
        }

        /// <summary>
        /// KCD2 风格沉浸引导：给保管箱实体加微暖金色调，让它在视觉上与普通场景道具区分。
        /// 玩家会注意到"这个桶/箱子和周围不太一样"，自然产生好奇心走过去查看。
        /// </summary>
        internal static void ApplyChestHighlight(GameEntity entity)
        {
            try
            {
                // 暖金微色调（ARGB），仅提示"特殊"而非霓虹灯式的显眼
                uint color1 = 0xD0FFF0D0; // 淡暖金
                uint color2 = 0xD0FFE8C0; // 微深暖金
                entity.SetColor(color1, color2, null);
                DebugLogger.Log("[Chest] Visual highlight applied (warm gold tint)");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Chest] SetColor failed (non-critical): {ex.Message}");
            }
        }
    }
}
