using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
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
                    if (TryStripSlot(agent.SpawnEquipment[i], i, remainingRoster, ref newEquipment))
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
            List<Agent> witnesses = new List<Agent>();
            MBList<Agent> nearbyAgents = new MBList<Agent>(); // 创建一个复用的列表容器

            // 1. 利用引擎的空间查询，只获取水平半径内的 Agent
            // 注意：这步极大地减少了我们要处理的数量
            Mission.Current.GetNearbyAgents(thief.Position.AsVec2, maxDistance, nearbyAgents);

            float fovDotThreshold = MathF.Cos(MathF.DegToRad*(fovDegrees / 2f));
            // 定义一个楼层高度阈值，一般 3米 左右算作一层楼
            float heightThreshold = 3.0f;

            foreach (var agent in nearbyAgents)
            {
                // 基础排除：排除自己、排除非人类、排除尸体
                if (agent == thief || !agent.IsHuman || !agent.IsActive()) continue;

                // 排除受害者（受害者逻辑通常单独处理，如果你希望受害者也算目击者，去掉这行）
                if (agent == victim) continue;

                // 2. [关键] 高度检查 (Z轴)
                // 如果高度差超过 3米，说明不在同一楼层，不管是楼上还是楼下，通常看不见（除非在楼梯上）
                float heightDiff = MathF.Abs(agent.Position.z - thief.Position.z);
                if (heightDiff > heightThreshold) continue;

                // 3. 角度检测 (他在看我吗？)
                // 计算向量
                Vec3 dirToThief3D = thief.Position - agent.Position;
                Vec2 dirToThief2D = dirToThief3D.AsVec2.Normalized();
                Vec2 agentLookDir = agent.LookDirection.AsVec2.Normalized();

                float dot = Vec2.DotProduct(agentLookDir, dirToThief2D);

                // 如果点积小于阈值，说明玩家在背身盲区，没看见
                if (dot < fovDotThreshold) continue;

                // 4. [进阶] 视线阻挡检测 (RayCast)
                // 即使距离近、在同一层、且面向你，中间可能隔着一堵墙。
                // 我们可以发射一条从 目击者眼睛 到 小偷身体 的射线。
                // 如果射线碰到了静态物体（墙、箱子），说明被遮挡了。

                float distance = dirToThief3D.Length;
                Vec3 eyePos = agent.LookFrame.origin; // 目击者眼睛位置
                Vec3 targetPos = thief.GetChestGlobalPosition(); // 小偷胸口位置

                // RayCast 返回 true 表示击中了物体（有遮挡），返回 false 表示通畅

                // --- 4. 射线检测 (RayCast) - 核心修正部分 ---
                if (!CanSeeTarget(agent, thief)) continue;


                // 通过所有测试，这是个目击者！
                witnesses.Add(agent);
            }

            return witnesses;
        }
        private static bool CanSeeTarget(Agent observer, Agent target)
        {
            // [修正1]：获取眼睛的位置，而不是脚底
            Vec3 eyePos = observer.GetEyeGlobalPosition();

            // 目标位置取胸口或略低于头部的位置 (取脚底容易被路沿挡住，取头顶有时判定奇怪)
            // GetChestGlobalPosition() 是个好选择，如果没有，可以用 Position + Z轴偏移
            Vec3 targetChestPos = target.AgentVisuals != null
                ? target.AgentVisuals.GetGlobalFrame().origin + new Vec3(0, 0, 1.2f) // 假设胸口高度 1.5米
                : target.Position + new Vec3(0, 0, 1.5f);

            float distanceToTarget = eyePos.Distance(targetChestPos);

            // [修正3]：使用射线检测
            float collisionDistance;
            Vec3 closestPoint;
            GameEntity collidedEntity;

            // 调用场景的 RayCast
            // 注意：BodyFlags.CommonCollisionExcludeFlags 通常用于物理碰撞，包含墙壁和地形
            // 我们不希望射线被地上的小碎石挡住，所以射线厚度给 0 或很小
            bool hasHitObstacle = Mission.Current.Scene.RayCastForClosestEntityOrTerrain(
                eyePos,
                targetChestPos,
                out collisionDistance,
                out closestPoint,
                out collidedEntity,
                0.01f, // 射线厚度
                BodyFlags.CommonCollisionExcludeFlags // 排除一些非阻挡性物体
            );

            // [修正2]：判断逻辑
            if (hasHitObstacle)
            {
                // 如果撞击点距离 比 目标距离 明显短 (留0.2f的容错空间)，说明中间有墙
                if (collisionDistance < distanceToTarget - 0.2f)
                {
                    return false; // 视线被遮挡
                }
            }

            // 没撞到任何东西，或者撞到的东西在目标身后（虽然理论上RayCast到目标点就停了，但安全起见）
            return true;
        }
    }
}
