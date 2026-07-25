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
    /// <summary>
    /// 保管箱所在环境类型，用于决定锚点 NPC 和 UI 文字。
    /// </summary>
    public enum ChestContext
    {
        Village,        // 村庄外景
        TownTavern,     // 城镇酒馆
        TownCenter,     // 城镇中心/市场
        LordsHall,      // 领主大厅
        Alley,          // 城镇小巷
        Arena,          // 竞技场 — 不生成保管箱
        Castle,         // 城堡室内
        Unknown         // 无法识别
    }

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

            return itemName;
        }

        /// <summary>目标身上是否还有任何可偷之物（任一装备槽或钱袋）。扒窃开条前预检用。</summary>
        public static bool HasAnythingToSteal(Agent agent)
        {
            if (agent == null) return false;
            if (HasPurseGold(agent)) return true;
            for (int i = 0; i < 12; i++)
            {
                EquipmentElement element = agent.SpawnEquipment[(EquipmentIndex)i];
                if (!element.IsEmpty && element.Item != null) return true;
            }
            return false;
        }

        /// <summary>目标身上是否有可偷的钱袋（分配金 > 0）。扒窃盲盒候选判定用。</summary>
        public static bool HasPurseGold(Agent agent)
        {
            if (agent == null) return false;
            return GetAgentGold(agent) > 0;
        }

        /// <summary>
        /// 「偷钱袋」独立路径：扒窃盲盒摸到钱袋且时机判定命中时调用（金钱=特殊物品，独立偷窃）。
        /// 钱袋 = NPC 身上的全部分配金，命中一次整袋端走。
        /// 族长家族金库（Hero.Gold）不在钱袋里——那是全族资金，必须战场上击败其部队才能获得。
        /// 返回实际偷到的面额；0 = 摸空（钱已被先摸走/分配池耗尽）。
        /// </summary>
        public static int StealPurseGold(Agent agent)
        {
            if (agent == null) return 0;

            int agentGold = GetAgentGold(agent);
            if (agentGold <= 0) return 0;

            int actual = ConsumeAgentGold(agent, agentGold, Settlement.CurrentSettlement);
            if (actual > 0)
                RecordStolenGold(agent, actual);
            return actual;
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

                    // 抓现行围堵：victim=null（牲畜没有具体受害者 Agent），与保管箱偷窃对齐
                    AgentAIController.Instance?.BroadcastEventInRange(
                        Agent.Main.Position, 25f, "WitnessCrime",
                        exclude: null, requireSight: true,
                        Agent.Main, null);
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
        // 3c. 大动物挣扎：抓动物偷窃条（StealBar Animal 模式）手滑 → 惊叫逃跑。
        //     难度由判定区宽度表达（大动物右扣 40%），不再用隐藏掷骰。
        // ----------------------------------------------------------------
        /// <summary>大型牲畜：猪/羊/牛（判定区右扣 40%）。鸡/鹅小动物全宽。</summary>
        public static bool IsLargeAnimal(string monsterId)
            => monsterId == "hog" || monsterId == "sheep" || monsterId == "cow";

        /// <summary>
        /// 动物挣脱的后果：惊叫惊动 20m 内目击者（警戒脉冲 + WitnessCrime 立即围堵，与保管箱对齐），
        /// 然后朝远离玩家的方向逃跑 8~14m。动物无 AgentBrain（IsHuman=false 不注册脑），
        /// 逃跑不走 Brain 事件体系，直接一次性脚本化移动。
        /// </summary>
        public static void OnAnimalStruggleFlee(Agent animal, string animalName)
        {
            if (animal == null || !animal.IsActive() || Agent.Main == null) return;

            try
            {
                // ① 惊叫：目击者警戒脉冲（复用撬锁噪音模式）
                var witnesses = GetWitnesses(Agent.Main, animal, maxDistance: 20f);
                foreach (var w in witnesses)
                    AgentAIController.GetBrainForAgent(w)?.AddAlert(PlayerActionType.Steal, 0.5f);

                // ② 有人看见 → 立即围堵（victim=null，同保管箱抓现行）
                if (witnesses.Count > 0)
                {
                    DebugLogger.Log($"[AnimalTheft] {animalName} 挣脱惊叫，{witnesses.Count} 名目击者被惊动");
                    AgentAIController.Instance?.BroadcastEventInRange(
                        Agent.Main.Position, 25f, "WitnessCrime",
                        exclude: null, requireSight: true,
                        Agent.Main, null);
                }
                else
                {
                    DebugLogger.Log($"[AnimalTheft] {animalName} 挣脱，无人目击");
                }

                // ③ 逃跑：远离玩家方向 ±45° 抖动，8~14m，取第一个 navmesh 有效点
                Vec3 away = animal.Position - Agent.Main.Position;
                away.z = 0f;
                if (away.LengthSquared < 0.001f) away = new Vec3(1f, 0f, 0f);
                away = away.NormalizedCopy();

                for (int i = 0; i < 6; i++)
                {
                    float angle = (MBRandom.RandomFloat - 0.5f) * MathF.PI * 0.5f; // ±45°
                    float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
                    Vec3 dir = new Vec3(away.x * cos - away.y * sin, away.x * sin + away.y * cos, 0f);
                    Vec3 fleePos = animal.Position + dir * (8f + MBRandom.RandomFloat * 6f);
                    if (Mission.Current?.Scene != null && V.NavMesh(Mission.Current.Scene, fleePos, out _))
                    {
                        AgentControlHelper.ScriptedMoveToPoint(animal, fleePos, isRun: true);
                        return;
                    }
                }
                // 兜底：找不到可寻路点 → 呆在原地（惊叫已发生）
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[AnimalTheft] OnAnimalStruggleFlee error: {ex.Message}");
            }
        }

        // ----------------------------------------------------------------
        // 3b. 保管箱偷窃犯罪统一接线（照 RecordAnimalTheft 模板）：
        //     目击检测 → RegisterTheftWitnesses → WitnessCrime 广播（victim=null 抓现行围堵）。
        //     由 InteractionMissionView 两条 loot 路径在收尾时调用一次。
        //     TheftLedger 的物品记账已在 LootChestItem/DeductSettlementItemsOnly 内完成；
        //     金钱非物品、无法标赃，金的失窃由此处的证词 ActionRecord 承载。
        // ----------------------------------------------------------------
        /// <summary>
        /// 保管箱 loot 收尾时记录目击证词并广播犯罪。无目击者 → 仅日志（偷干净了，没人知道）。
        /// </summary>
        /// <param name="items">拿走的物品清单（纯金时为空表）</param>
        /// <param name="gold">拿走的金币（无则为 0）</param>
        public static void RecordChestTheft(Settlement settlement, List<(string itemId, string itemName, int count)> items, int gold)
        {
            try
            {
                if (settlement == null || Agent.Main == null) return;

                if (!Settings.Instance.WitnessSystemEnabled)
                {
                    DebugLogger.Log("[ChestTheft] Witness system DISABLED — treating as no witnesses.");
                    return;
                }

                var witnesses = GetWitnesses(Agent.Main, null, maxDistance: 15f);
                var witnessHeroIds = witnesses
                    .Where(a => (a.Character as CharacterObject)?.HeroObject != null)
                    .Select(a => (a.Character as CharacterObject).HeroObject.StringId)
                    .ToList();
                var templateWitness = witnesses
                    .Where(a => (a.Character as CharacterObject)?.HeroObject == null && a.Character != null)
                    .GroupBy(a => a.Character.StringId)
                    .ToDictionary(g => g.Key, g => g.Count());

                bool wasWitnessed = witnessHeroIds.Count > 0 || templateWitness.Count > 0;
                if (!wasWitnessed)
                {
                    DebugLogger.Log("[ChestTheft] No witnesses — clean getaway.");
                    return;
                }

                DebugLogger.Log($"[ChestTheft] Witnessed! {witnessHeroIds.Count} hero(es) + {templateWitness.Sum(kv => kv.Value)} template(s) saw the chest looting. items={items?.Count ?? 0}, gold={gold}");

                // 证词：每件物品一条；金钱一条（targetName=保管箱，victim 无法具体到人）
                if (items != null)
                {
                    foreach (var (itemId, itemName, _) in items)
                    {
                        AgentAIController.Instance?.RegisterTheftWitnesses(
                            witnessHeroIds, templateWitness,
                            itemId, itemName ?? itemId, targetName: "保管箱");
                    }
                }
                if (gold > 0)
                {
                    AgentAIController.Instance?.RegisterTheftWitnesses(
                        witnessHeroIds, templateWitness,
                        "gold", $"{gold} 第纳尔", targetName: "保管箱");
                }

                // 抓现行围堵：victim=null（保管箱没有具体受害者 Agent）
                AgentAIController.Instance?.BroadcastEventInRange(
                    Agent.Main.Position, 25f, "WitnessCrime",
                    exclude: null, requireSight: true,
                    Agent.Main, null);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ChestTheft] RecordChestTheft error: {ex.Message}");
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

        /// <summary>防重复分配：已分配过的"定居点|场景"复合键</summary>
        private static string _lastDistributedSettlementId = null;

        /// <summary>
        /// Town 子场景的金库权重。村庄/城堡只有单一场景，100% 不变。
        /// Town 内部按场景类型拆分金库，不同场景的 NPC 和保管箱各自独立分配。
        /// </summary>
        private static float GetChestContextGoldWeight(ChestContext ctx)
        {
            return ctx switch
            {
                ChestContext.Village => 1.0f,       // 村庄全拿
                ChestContext.Castle => 1.0f,         // 城堡全拿
                ChestContext.TownCenter => 0.40f,    // 城镇中心 40%
                ChestContext.LordsHall => 0.30f,     // 领主大厅 30%
                ChestContext.TownTavern => 0.15f,    // 酒馆 15%
                ChestContext.Alley => 0.10f,         // 暗巷 10%
                ChestContext.Arena => 0f,            // 竞技场 — 不生成保管箱
                ChestContext.Unknown => 0.20f,       // 未知场景保守 20%
                _ => 0.20f
            };
        }

        /// <summary>
        /// 按场景类型过滤物品。确保酒馆里不会出现军马和盔甲，领主大厅里不会出现啤酒桶。
        /// </summary>
        private static bool IsItemAllowedInContext(ItemObject item, ChestContext ctx)
        {
            if (item == null) return false;
            if (item.Type == ItemObject.ItemTypeEnum.Animal) return false; // 动物场景里直接偷

            var type = item.Type;

            return ctx switch
            {
                // 村庄/城堡：单一场景，全部物资
                ChestContext.Village => true,
                ChestContext.Castle => true,

                // 城镇中心：民用物资（商品+食物），武器盔甲归领主大厅
                ChestContext.TownCenter => type == ItemObject.ItemTypeEnum.Goods
                    || item.IsFood,

                // 酒馆：食物 + 消耗品
                ChestContext.TownTavern => type == ItemObject.ItemTypeEnum.Goods
                    || item.IsFood,

                // 领主大厅：武器 + 防具 + 盾牌 + 马匹 + 书籍（领主的军械库）
                ChestContext.LordsHall => type == ItemObject.ItemTypeEnum.OneHandedWeapon
                    || type == ItemObject.ItemTypeEnum.TwoHandedWeapon
                    || type == ItemObject.ItemTypeEnum.Polearm
                    || type == ItemObject.ItemTypeEnum.Bow
                    || type == ItemObject.ItemTypeEnum.Crossbow
                    || type == ItemObject.ItemTypeEnum.Arrows
                    || type == ItemObject.ItemTypeEnum.Bolts
                    || type == ItemObject.ItemTypeEnum.Thrown
                    || type == ItemObject.ItemTypeEnum.Shield
                    || type == ItemObject.ItemTypeEnum.HeadArmor
                    || type == ItemObject.ItemTypeEnum.BodyArmor
                    || type == ItemObject.ItemTypeEnum.LegArmor
                    || type == ItemObject.ItemTypeEnum.HandArmor
                    || type == ItemObject.ItemTypeEnum.Cape
                    || type == ItemObject.ItemTypeEnum.Horse
                    || type == ItemObject.ItemTypeEnum.HorseHarness
                    || type == ItemObject.ItemTypeEnum.Book
                    || type == ItemObject.ItemTypeEnum.Goods,

                // 暗巷：投掷武器 + 单手 + 轻甲 + 商品（违禁品/赃物）
                ChestContext.Alley => type == ItemObject.ItemTypeEnum.Thrown
                    || type == ItemObject.ItemTypeEnum.OneHandedWeapon
                    || type == ItemObject.ItemTypeEnum.Goods
                    || item.IsFood
                    || type == ItemObject.ItemTypeEnum.HandArmor
                    || type == ItemObject.ItemTypeEnum.Cape,

                // 竞技场：不生成保管箱
                ChestContext.Arena => false,

                // 未知：保守开放 Goods + Food
                _ => type == ItemObject.ItemTypeEnum.Goods
                    || item.IsFood
            };
        }

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
        /// 进场景时调用：把定居点金库按场景权重分配到 NPC 身上 + 公共箱子。
        /// 同一"定居点+场景"只分配一次（防重复）。
        /// Town 内部按场景拆分：领主大厅 30%、城镇中心 40%、酒馆 15%、暗巷 10%。
        /// 村庄/城堡只有单一场景 → 100% 全拿。
        /// </summary>
        public static void DistributeSettlementWealth(Settlement settlement)
        {
            if (settlement == null) return;

            // 复合键：定居点 + 场景（Town 的内部场景各自独立分配）
            var ctx = GetCurrentChestContext();
            string locationId = CampaignMission.Current?.Location?.StringId ?? "__unknown__";
            string compoundKey = $"{settlement.StringId}|{locationId}";
            if (_lastDistributedSettlementId == compoundKey) return;

            _lastDistributedSettlementId = compoundKey;
            _agentGold = new Dictionary<int, int>();
            _stashGold = 0;
            ChestItemRoster = new ItemRoster();
            ChestEntity = null;

            try
            {
                // ── 金库计算（按场景权重缩放）──
                int treasury = GetGoldComponent(settlement)?.Gold ?? 0;
                if (treasury <= 0) return;

                float weight = GetChestContextGoldWeight(ctx);
                int effectiveTreasury = (int)(treasury * weight);
                int pool = (int)(effectiveTreasury * CirculatingRatio);
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

                    int w;
                    if (hero != null)
                    {
                        if (hero.Occupation == Occupation.Headman)
                            { w = 10; headmanCount++; }
                        else if (hero.Occupation == Occupation.RuralNotable)
                            { w = 7; notableCount++; }
                        else
                            { w = 4; heroCount++; }
                    }
                    else
                    {
                        w = 1; templateCount++;
                    }

                    weightedAgents.Add((agent, w));
                    totalWeight += w;
                }

                int distributedCount = 0;
                if (totalWeight > 0 && npcPool > 0)
                {
                    foreach (var (agent, weight2) in weightedAgents)
                    {
                        int share = (int)((float)weight2 / totalWeight * npcPool);
                        if (share > 0)
                        {
                            _agentGold[agent.Index] = share;
                            distributedCount++;

                            var ch = (agent.Character as CharacterObject)?.HeroObject;
                            if (ch?.Occupation == Occupation.Headman) headmanGold += share;
                            else if (ch?.Occupation == Occupation.RuralNotable) notableGold += share;
                            else if (ch != null) heroGold += share;
                            else templateGold += share;
                        }
                    }
                }

                // ── 箱子物品（按场景类型过滤，不动物资——懒扣除）──
                var settlementRoster = settlement.ItemRoster;
                int itemTypesInChest = 0;
                for (int i = 0; i < settlementRoster.Count; i++)
                {
                    var item = settlementRoster.GetItemAtIndex(i);
                    if (item == null) continue;
                    if (!IsItemAllowedInContext(item, ctx)) continue;
                    int have = settlementRoster.GetElementNumber(i);
                    if (have > 0)
                    {
                        ChestItemRoster.AddToCounts(item, have);
                        itemTypesInChest++;
                    }
                }

                // ── 汇总日志 ──
                var sb = new System.Text.StringBuilder();
                string tag = settlement.IsTown ? $" ({ctx}, ×{weight:F2})" : "";
                sb.Append($"[Wealth] {settlement.Name}{tag}: 金库{treasury}→场景{effectiveTreasury}→流通池{pool}, NPC池{npcPool}(");
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
                sb.Append($"), 箱子{_stashGold}第纳尔, 物资{itemTypesInChest}种(过滤后)");
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
                locationName: $"在{settlement.Name}的保管箱",
                worldEventId: AgentAIController.Instance?.PendingWorldEvent?.EventId);
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
                locationName: $"在{settlement.Name}的保管箱",
                worldEventId: AgentAIController.Instance?.PendingWorldEvent?.EventId);
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
        /// 根据当前场景类型推断保管箱所在环境。
        /// </summary>
        public static ChestContext GetCurrentChestContext()
        {
            // Location StringId 优先（精确到子场景）
            string locId = CampaignMission.Current?.Location?.StringId ?? "";
            if (!string.IsNullOrEmpty(locId))
            {
                if (locId.Contains("tavern")) return ChestContext.TownTavern;
                if (locId.Contains("lordshall")) return ChestContext.LordsHall;
                if (locId.Contains("alley")) return ChestContext.Alley;
                if (locId.Contains("arena")) return ChestContext.Arena;
                if (locId == "center" || locId.Contains("village"))
                {
                    var s = Settlement.CurrentSettlement;
                    if (s != null && s.IsVillage) return ChestContext.Village;
                    return ChestContext.TownCenter;
                }
                if (locId.Contains("castle")) return ChestContext.Castle;
            }

            // 回退：按定居点类型
            var settlement = Settlement.CurrentSettlement;
            if (settlement == null) return ChestContext.Unknown;
            if (settlement.IsVillage) return ChestContext.Village;
            if (settlement.IsTown) return ChestContext.TownCenter;
            if (settlement.IsCastle) return ChestContext.Castle;
            return ChestContext.Unknown;
        }

        /// <summary>
        /// 按场景类型决定保管箱锁的簧片数（锁难度 = 世界规则，与场景分发同住）。
        /// 村庄 2 / 城镇 3 / 城堡·领主大厅 4。
        /// </summary>
        public static int GetLockpickPinCount(ChestContext ctx)
        {
            return ctx switch
            {
                ChestContext.Village => 2,
                ChestContext.TownCenter => 3,
                ChestContext.TownTavern => 3,
                ChestContext.Alley => 3,
                ChestContext.Castle => 4,
                ChestContext.LordsHall => 4,
                _ => 2
            };
        }

        /// <summary>
        /// 场景感知的保管箱锚点：根据不同场景类型选择最合适的 NPC。
        /// 村庄→村长/乡绅，酒馆→酒馆老板，领主大厅→领主，其余→任意有名 NPC。
        /// 优先级数组有序：逐档搜索，高档命中即返回（保证酒馆老板优先于工匠）。
        /// </summary>
        internal static Agent FindChestAnchorAgent()
        {
            var agents = Mission.Current?.Agents;
            if (agents == null) return null;

            ChestContext ctx = GetCurrentChestContext();

            // 按场景类型确定搜索优先级（数组顺序 = 优先顺序）
            Occupation[] priorities = ctx switch
            {
                ChestContext.TownTavern => new[] { Occupation.Tavernkeeper,
                    Occupation.Artisan, Occupation.Merchant, Occupation.Wanderer },
                ChestContext.LordsHall => null, // 特殊处理：IsLord
                ChestContext.Alley => new[] { Occupation.GangLeader, Occupation.Wanderer },
                ChestContext.TownCenter => new[] { Occupation.Merchant,
                    Occupation.Artisan, Occupation.GangLeader },
                ChestContext.Village => new[] { Occupation.Headman, Occupation.RuralNotable },
                ChestContext.Castle => null, // 特殊处理：IsLord
                _ => Array.Empty<Occupation>()
            };

            // 按优先级逐档搜索 occupation
            // 注意（铁律 8）：酒馆老板/村长等是模板 NPC（is_hero=false，无 HeroObject），
            // Occupation 挂在 CharacterObject 模板上——HeroObject?.Occupation 只认 Hero，会永远漏掉他们。
            if (priorities != null)
            {
                foreach (var occ in priorities)
                {
                    foreach (Agent agent in agents)
                    {
                        if (!agent.IsHuman || !agent.IsActive()) continue;
                        var co = agent.Character as CharacterObject;
                        var agentOcc = co?.HeroObject?.Occupation ?? co?.Occupation;
                        if (agentOcc.HasValue && agentOcc.Value == occ)
                            return agent;
                    }
                }
            }
            else
            {
                // 领主大厅/城堡：找 IsLord
                foreach (Agent agent in agents)
                {
                    if (!agent.IsHuman || !agent.IsActive()) continue;
                    var hero = (agent.Character as CharacterObject)?.HeroObject;
                    if (hero != null && hero.IsLord)
                        return agent;
                }
            }

            // 兜底：任意活跃 Hero
            foreach (Agent agent in agents)
            {
                if (!agent.IsHuman || !agent.IsActive()) continue;
                if ((agent.Character as CharacterObject)?.HeroObject != null)
                    return agent;
            }

            // 最终兜底：任意活跃人类
            foreach (Agent agent in agents)
            {
                if (agent.IsHuman && agent.IsActive())
                    return agent;
            }

            return null;
        }

        /// <summary>保管箱放在锚点 NPC 正后方的候选距离（按数组顺序逐级尝试，取第一个可站立点）。</summary>
        private static readonly float[] ChestBehindDistances = { 0.7f, 1.2f, 2.0f };

        /// <summary>
        /// 决定保管箱的世界坐标：优先锚点 NPC 正后方（酒馆老板身后的柜台内侧、村长背后的墙根），
        /// 按 <see cref="ChestBehindDistances"/> 数组顺序逐级尝试并验证 navmesh 可站立；
        /// 全部失败回退旧行为（锚点 +X 偏移 2m）。
        /// </summary>
        internal static Vec3 ResolveChestSpawnPosition(Scene scene, Agent anchor)
        {
            Vec3 anchorPos = anchor?.Position ?? Agent.Main?.Position ?? Vec3.Zero;

            if (anchor != null && scene != null)
            {
                Vec3 back = -anchor.LookDirection;
                back.z = 0f;
                if (back.LengthSquared > 0.0001f)
                {
                    back = back.NormalizedCopy();
                    foreach (float dist in ChestBehindDistances)
                    {
                        Vec3 candidate = anchorPos + back * dist;
                        float ground = scene.GetGroundHeightAtPosition(candidate);
                        if (ground != 0f) candidate.z = ground;
                        if (V.NavMesh(scene, candidate, out _))
                        {
                            DebugLogger.Log($"[Chest] 锚点 '{anchor.Name}' at {anchorPos} → 正后方 {dist:F1}m 命中，" +
                                $"相对偏移 ({candidate.x - anchorPos.x:F2}, {candidate.y - anchorPos.y:F2}, {candidate.z - anchorPos.z:F2})");
                            return candidate;
                        }
                        DebugLogger.Log($"[Chest] 锚点 '{anchor.Name}' 正后方 {dist:F1}m navmesh 无效，尝试下一档");
                    }
                }
            }

            // 兜底：旧行为（锚点 +X 偏移 2m）
            Vec3 fallback = anchorPos + new Vec3(2f, 0f, 0f);
            if (scene != null)
            {
                float h = scene.GetGroundHeightAtPosition(fallback);
                if (h != 0f) fallback.z = h;
            }
            DebugLogger.Log($"[Chest] 锚点 '{anchor?.Name ?? "null"}' at {anchorPos} → 正后方全档位失败，兜底 +X 2m，" +
                $"相对偏移 ({fallback.x - anchorPos.x:F2}, {fallback.y - anchorPos.y:F2}, {fallback.z - anchorPos.z:F2})");
            return fallback;
        }

        /// <summary>
        /// 保管箱外观 prefab。实机对比选定：原版 271 个场景 XML 中验证在用的真箱子模型（bd_chest_* 家族）。
        /// </summary>
        internal const string ChestPrefabName = "bd_chest_c";

        /// <summary>保管箱模型缩放系数（实机反馈原尺寸过大，减半）。prefab 与克隆两条路径统一生效。</summary>
        internal const float ChestScale = 0.5f;

        /// <summary>
        /// 生成保管箱可见实体。两轮策略（铁律 5）：
        /// ① 固定 prefab Instantiate —— 外观正确性优先：场景克隆靠实体名猜，曾把椅子（bd_chair_c）
        ///    纯靠贴脸距离选成"宝箱"，实体名与外观无必然联系；
        /// ② 场景扫描克隆兜底 —— prefab 未加载时（如内容包场景），从场景已有储物道具克隆。
        /// </summary>
        /// <param name="faceToward">箱体正面朝向点（通常传锚点 NPC 位置——老板开柜的自然朝向）；null 保持默认朝向。</param>
        internal static GameEntity SpawnStorageChestProp(Scene scene, Vec3 targetPos, Vec3? faceToward = null)
        {
            if (GameEntity.PrefabExists(ChestPrefabName))
            {
                // 朝向：正面朝向锚点 NPC；无锚点则保持 prefab 默认朝向
                Mat3 rotation = Mat3.Identity;
                if (faceToward.HasValue)
                {
                    Vec3 dir = faceToward.Value - targetPos;
                    dir.z = 0f;
                    if (dir.LengthSquared > 0.0001f)
                        rotation = Mat3.CreateMat3WithForward(dir.NormalizedCopy());
                }
                MatrixFrame frame = new MatrixFrame(rotation * ChestScale, targetPos);
                var chest = GameEntity.Instantiate(scene, ChestPrefabName, frame);
                if (chest != null)
                {
                    DebugLogger.Log($"[Chest] Spawned prefab '{ChestPrefabName}' at {targetPos} (scale={ChestScale})");
                    return chest;
                }
                DebugLogger.Log($"[Chest] Instantiate '{ChestPrefabName}' failed — fallback to scene scan");
            }
            else
            {
                DebugLogger.Log($"[Chest] Prefab '{ChestPrefabName}' not loaded — fallback to scene scan");
            }

            return FindAndCloneStorageProp(scene, targetPos);
        }

        /// <summary>
        /// 递归扫描场景中所有 GameEntity，找到最合适的储物类道具，克隆并放到 targetPos。
        /// 评分策略：名字含 barrel/sack/crate/chest/basket 等关键词才入选（通用道具禁止靠距离夺冠）；离目标越近 → 加分。
        /// </summary>
        private static GameEntity FindAndCloneStorageProp(Scene scene, Vec3 targetPos)
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
            DebugLogger.Log($"[Chest] Scan: context={GetCurrentChestContext()}, best candidate '{best.entity.Name}' (score={best.score:F0}), " +
                $"candidates={candidates.Count}, names=[{string.Join(", ", candidates.Take(5).Select(c => c.entity.Name))}]");

            // 克隆并移动到目标位置（缩放减半，与 prefab 路径一致）
            var clone = GameEntity.CopyFrom(scene, best.entity);
            if (clone != null)
            {
                MatrixFrame frame = clone.GetGlobalFrame();
                frame.rotation = frame.rotation * ChestScale;
                frame.origin = targetPos;
                clone.SetGlobalFrame(frame);
                DebugLogger.Log($"[Chest] Cloned '{best.entity.Name}' → moved to {targetPos} (scale={ChestScale})");
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

                // 黑名单：跳过引擎内部实体（__skybox__ 含 "box" 命中 nameScore=85 是已知误伤）
                if (IsBlacklistedEntityName(name))
                    goto RecurseChildren;

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
                // 必须命中储物关键词才入选：通用道具（nameScore=5）曾纯靠贴脸距离夺冠，
                // 把 bd_chair_c 椅子克隆成保管箱。距离只在真储物道具之间做 tie-break。
                if (nameScore >= 70f)
                    candidates.Add((entity, total));
            }

        RecurseChildren:
            // 递归子 entity
            for (int i = 0; i < entity.ChildCount; i++)
            {
                var child = entity.GetChild(i);
                if (child != null)
                    CollectPropsRecursive(child, targetPos, candidates);
            }
        }

        /// <summary>
        /// 实体名黑名单检查。引擎内部实体（skybox / 光源 / 粒子 / 碰撞体等）
        /// 虽然可能有 mesh，但绝不是储物道具，禁止进入候选池。
        /// </summary>
        private static bool IsBlacklistedEntityName(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            // 下划线开头 = 引擎/场景内部实体（__skybox__、_negative_light_10 等）
            if (name.StartsWith("_")) return true;
            // 精确匹配
            if (name == "__skybox__") return true;
            // 前缀匹配
            if (name.StartsWith("torch_") || name.StartsWith("flame_") ||
                name.StartsWith("light_") || name.StartsWith("smoke_") ||
                name.StartsWith("sound_") || name.StartsWith("fire_") ||
                name.StartsWith("particle_") || name.StartsWith("vfx_"))
                return true;
            // 内部/碰撞/水面实体
            if (name.Contains("_collision_") || name.Contains("_hitbox_") ||
                name.Contains("_water_") || name.Contains("_trigger_"))
                return true;
            return false;
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
