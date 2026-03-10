using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ExampleMod
{
    public class StealManager
    {
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

            // 2. 添加到玩家队伍背包 (Party Stash)
            if (PartyBase.MainParty != null)
            {
                PartyBase.MainParty.ItemRoster.AddToCounts(itemToSteal, 1);
            }
            else
            {
                // 如果是在村庄/城镇场景且没有队伍，也可以直接加给主角 (这种情况较少)
                // CharacterObject.PlayerCharacter.Equipment... (通常加到队伍背包更通用)
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
        public static void StripAgentEquipment(Agent agent, bool stripWeapons, bool stripArmor)
        {
            if (agent == null) return;

            // 1. 获取当前装备的深拷贝 (必须拷贝，否则修改引用可能出错)
            Equipment newEquipment = agent.SpawnEquipment.Clone();

            // 2. 根据需求清空槽位
            if (stripArmor)
            {
                // 清空防具：头、身、腿、手、马匹
                newEquipment[EquipmentIndex.Head] = EquipmentElement.Invalid;
                newEquipment[EquipmentIndex.Body] = EquipmentElement.Invalid;
                newEquipment[EquipmentIndex.Leg] = EquipmentElement.Invalid;
                newEquipment[EquipmentIndex.Gloves] = EquipmentElement.Invalid;
                newEquipment[EquipmentIndex.Cape] = EquipmentElement.Invalid;

                // 甚至可以把马偷了
                // newEquipment[EquipmentIndex.Horse] = EquipmentElement.Invalid;
                // newEquipment[EquipmentIndex.HorseHarness] = EquipmentElement.Invalid;
            }

            if (stripWeapons)
            {
                // 清空武器槽位 (0-3)
                for (int i = 0; i < 4; i++)
                {
                    newEquipment[i] = EquipmentElement.Invalid;
                }
            }

            // 3. 【核心】应用装备变更并刷新视觉模型
            // 这会让 NPC 瞬间变装（变裸或者手里没武器）
            agent.UpdateSpawnEquipmentAndRefreshVisuals(newEquipment);

            // 4. 如果 Agent 正在手持武器，强制他丢掉或收起
            // 如果不加这一步，有时候模型刷新了，但 AI 逻辑里还认为手里拿着剑
            if (stripWeapons)
            {
                // 强制主手和副手丢弃物品
                if (agent.Equipment[EquipmentIndex.WeaponItemBeginSlot].IsEmpty)
                {
                    // 尝试让他收刀，防止动作鬼畜
                    agent.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);
                    agent.TryToSheathWeaponInHand(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.Instant);
                }

                // 强制更新 AI 的武器状态
                agent.UpdateAgentStats();
            }
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
