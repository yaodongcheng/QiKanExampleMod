using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.Library.VirtualFolders.Win64_Shipping_Client;
using static TaleWorlds.MountAndBlade.Agent;

namespace ExampleMod
{
    public static class AgentControlHelper
    {
        public static void SetPose(Agent agent, string actionId)
        {
            if (agent == null || string.IsNullOrEmpty(actionId))
                return;
            if (GetPose(agent) == actionId)
                return;
            ActionIndexCache actionCache = ActionIndexCache.Create(actionId);
            if (actionCache != ActionIndexCache.act_none)
            {
                agent.SetActionChannel(0, actionCache, ignorePriority: false, blendInPeriod: 0.2f);
            }
        }
        public static string GetPose(Agent agent)
        {
            if (agent == null) return "";
            ActionIndexValueCache currentAction = agent.GetCurrentActionValue(0);
            return currentAction.Name;
        }
        public static bool IsPlayingPose(Agent agent,string actionId)
        {
            if (agent == null || string.IsNullOrEmpty(actionId))
                return false;
            return GetPose(agent) == actionId;
        }

        // 【修改后】只负责发号施令，不负责等待

       
        public static void ScriptedMoveToPoint(Agent agent, Vec3 targetVec, bool isRun = false,bool hasNav = false)
        {
            if (agent == null || !agent.IsActive()) return;

            // 1. 清理状态 (原 MoveTo 的前置逻辑)
            if (agent.IsUsingGameObject)
            {
                agent.StopUsingGameObject(true, Agent.StopUsingGameObjectFlags.None);
            }
            agent.ClearTargetFrame();
            agent.Controller = Agent.ControllerType.AI;

            // 2. 修正导航网格
            WorldPosition targetPos = new WorldPosition(agent.Mission.Scene, UIntPtr.Zero, targetVec, false);
            if (!hasNav)
            {
                if (targetPos.GetNavMesh() == UIntPtr.Zero)
                {
                    agent.Mission.Scene.GetNavigationMeshForPosition(ref targetVec);
                    targetPos = new WorldPosition(agent.Mission.Scene, targetVec);
                }
            }
            // 3. 设置移动参数
            var moveFlags = Agent.AIScriptedFrameFlags.GoToPosition |
                            Agent.AIScriptedFrameFlags.NoAttack |
                            Agent.AIScriptedFrameFlags.NeverSlowDown;

            if (!isRun)
                moveFlags |= Agent.AIScriptedFrameFlags.DoNotRun;
            // 4. 下达指令 (只执行一次)
            agent.SetScriptedPosition(ref targetPos, false, moveFlags);
        }
        public static void ScriptedMoveToAgent(Agent agent, Agent targetAgent, bool isRun)
        {
            if (agent == null || targetAgent == null) return;

            // 复用上面的逻辑，只是目标点变成了动态获取
            // 注意：这里不需要每帧做 NavMesh 修正，因为 GetWorldPosition 通常是合法的，
            // 但如果目标跳崖了，这里可能需要额外判断，暂时保持简单。
            ScriptedMoveToPoint(agent, targetAgent.Position, isRun);
        }

        private static string GetAmmoIdForWeaponClass(TaleWorlds.Core.WeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case TaleWorlds.Core.WeaponClass.Bow:
                    return "sho_practice_arrow";
                case TaleWorlds.Core.WeaponClass.Crossbow:
                    return "teppo_ammo";
                case TaleWorlds.Core.WeaponClass.Musket:
                case TaleWorlds.Core.WeaponClass.Pistol:
                    return "teppo_ammo";
                default:
                    return null;
            }
        }
        private static string GiveWeaponToAgent(Agent agent, string itemId)
        {
            var itemObject = TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<TaleWorlds.Core.ItemObject>(itemId);
            if (itemObject == null)
            {
                return $"can not find Item :{itemId}";
            }
            // === 新增：先把熟练度拉满，防止 AI 判定自己太菜只肯肉搏 ===
            if (agent.Character != null)
            {

                //    agent.SetSkillValue(TaleWorlds.Core.DefaultSkills.Crossbow, 300); // 火枪通常吃 Crossbow 技能
            }
            var missionWeapon = new TaleWorlds.MountAndBlade.MissionWeapon(itemObject, null, agent.Origin?.Banner);
            agent.EquipWeaponWithNewEntity(TaleWorlds.Core.EquipmentIndex.Weapon0, ref missionWeapon);
            //检查是否需要弹药
            if (itemObject.PrimaryWeapon != null)
            {
                string ammoId = GetAmmoIdForWeaponClass(itemObject.PrimaryWeapon.WeaponClass);
                if (!string.IsNullOrEmpty(ammoId))
                {
                    var ammoObject = TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<TaleWorlds.Core.ItemObject>(ammoId);
                    if (ammoObject != null)
                    {
                        var ammoWeapon = new TaleWorlds.MountAndBlade.MissionWeapon(ammoObject, null, agent.Origin?.Banner);
                        agent.EquipWeaponWithNewEntity(TaleWorlds.Core.EquipmentIndex.Weapon1, ref ammoWeapon);
                    }
                }
            }
            agent.UpdateAgentStats();

            return "";
        }
      

        public static async Task MoveToActor(Agent npcAgent, Agent actor, float stopDistance = 0.5f)
        {
            if (npcAgent == null || !npcAgent.IsActive() || actor == null) return;
            Mat3 playerRot = actor.LookFrame.rotation;
            //npcAgent的目标rotation需要和actor的rot相反

          //  Vec3 direction = npcAgent.Position - turnAgent.Position;

            Vec2 targetDir = (-playerRot.f).AsVec2;

            npcAgent.SetLookAgent(actor); // 走路时盯着actor
            await MoveTo(npcAgent, actor.Position + (playerRot.f * 2.0f), targetDir, stopDistance);
            npcAgent.SetLookAgent(actor); // 到终点仍盯着actor
        }

        public static void StopAndReset(Agent agent)
        {
            if (agent == null || !agent.IsActive()) return;
            // agent.ClearTargetFrame();
            ForceUnlockAgent(agent);
            // 可以根据需要决定是否要重置 Flags
            //agent.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);
            // agent.SetScriptedFlags(agent.GetScriptedFlags() & ~Agent.AIScriptedFrameFlags.DoNotRun);
        }
        public static void ForceUnlockAgent(Agent agent)
        {
            if (agent == null || !agent.IsActive()) return;

            // 1. 解除速度限制 (-1f 表示恢复默认最大速度)
            agent.SetMaximumSpeedLimit(-1f, false);
            agent.SetScriptedFlags(Agent.AIScriptedFrameFlags.None);
            // 2. 禁用之前 MoveTo 设置的脚本化移动 (解除 SetScriptedPosition 的锁定)
            agent.DisableScriptedMovement();

            // 3. 清除强制盯人 (解除 SetLookAgent)
            agent.SetLookAgent(null);
            agent.ClearTargetFrame();

            // 4. 确保控制器回归 AI
            if (agent.Controller != Agent.ControllerType.Player)
            {
                agent.Controller = Agent.ControllerType.AI;
            }

            // 5. 如果之前在使用物体（比如椅子），强制停止
            if (agent.IsUsingGameObject)
            {
                agent.StopUsingGameObject(true, Agent.StopUsingGameObjectFlags.None);
            }
        }
        public static async Task MovePrepare(Agent npcAgent)
        {
            if (npcAgent == null || !npcAgent.IsActive()) return;

            // 1. 停止当前交互 (如坐在椅子上)
            if (npcAgent.IsUsingGameObject)
            {
                npcAgent.StopUsingGameObject(false, Agent.StopUsingGameObjectFlags.None);
                npcAgent.SetInteractionAgent(null);
                await Task.Delay(2000); // 给动画一点混合时间
                if (!npcAgent.IsActive()) return; // 检查存活
                npcAgent.StopUsingGameObject(true);
            }

            // 2. 清除 AI 状态，准备接管
            npcAgent.Formation = null;
            npcAgent.ClearTargetFrame();
            npcAgent.SetTargetAgent(null);
            npcAgent.Controller = Agent.ControllerType.AI;
            npcAgent.SetActionChannel(0, ActionIndexCache.act_none, true, 0UL, 0f, 1f, 0.5f);
            npcAgent.SetActionChannel(1, ActionIndexCache.act_none, true, 0UL, 0f, 1f, 0.5f);
        }
        public static void MoveEndAndInteractPrepare(Agent npcAgent)
        {
            // 7. 锁定状态 (进入对话模式)
            WorldPosition currentPos = npcAgent.GetWorldPosition();
            var lockFlags = AIScriptedFrameFlags.DoNotRun |
                            AIScriptedFrameFlags.NoAttack |
                            AIScriptedFrameFlags.InConversation; // 关键 Flag

            npcAgent.SetScriptedPosition(ref currentPos, false, lockFlags);
            npcAgent.SetMaximumSpeedLimit(0f, false); // 锁死速度

           // InformationManager.DisplayMessage(new InformationMessage($"NPC {npcAgent.Character.Name} 已锁定位置"));
        }
        public static void MoveEndAndInteractPrepare(Agent npcAgent,Vec3 initPos)
        {
            // 7. 锁定状态 (进入对话模式)
            WorldPosition currentPos = new WorldPosition(npcAgent.Mission.Scene,  initPos);
            var lockFlags = AIScriptedFrameFlags.DoNotRun |
                            AIScriptedFrameFlags.NoAttack |
                            AIScriptedFrameFlags.InConversation; // 关键 Flag

            npcAgent.SetScriptedPosition(ref currentPos, false, lockFlags);
            npcAgent.SetMaximumSpeedLimit(0f, false); // 锁死速度

            //InformationManager.DisplayMessage(new InformationMessage($"NPC {npcAgent.Character.Name} 已锁定位置"));
        }
        public static async Task MoveTo(Agent npcAgent, Vec3 targetVec, Vec2  targetDir, float stopDistance = 0.5f)
        {

            await MovePrepare(npcAgent);

            // 修正到导航网格上 (防止点在墙里)
            WorldPosition targetPos = new WorldPosition(npcAgent.Mission.Scene, UIntPtr.Zero, targetVec, false);
            // 如果点无效，尝试获取最近的导航网格
            if (targetPos.GetNavMesh() == UIntPtr.Zero)
            {
                npcAgent.Mission.Scene.GetNavigationMeshForPosition(ref targetVec);
                targetPos = new WorldPosition(npcAgent.Mission.Scene, targetVec);
            }

            // 4. 设置移动参数
            var moveFlags = AIScriptedFrameFlags.GoToPosition |
                            AIScriptedFrameFlags.DoNotRun |     // 走路，显着优雅
                            AIScriptedFrameFlags.NoAttack |
                            AIScriptedFrameFlags.NeverSlowDown;


            // 5. 循环检查距离 (移动过程)
            float timeElapsed = 0f;
            float timeout = 8f; // 8秒超时

            while (npcAgent.Position.Distance(targetVec) > stopDistance && timeElapsed < timeout)
            {
                // 持续更新目标点 (防止玩家移动后NPC去错地方，或者每帧刷新确保不掉队)
                npcAgent.SetScriptedPosition(ref targetPos, false, moveFlags);

                await Task.Delay(200); // 没必要太频繁
                timeElapsed += 0.2f;

                if (!npcAgent.IsActive()) return;
            }

            // 6. 超时处理 (如果卡住了，瞬移最后一段距离)

            // 保持朝向瞬移
            Vec3 finalPos = targetVec;
            npcAgent.TeleportToPosition(finalPos);
            npcAgent.SetMovementDirection(targetDir);


            MoveEndAndInteractPrepare(npcAgent);
        }

        public static void LookAtAgent(Agent agent, Agent target)
        {
            if (agent == null) return;
            agent.SetLookAgent(target);
        }
        public static void StopLooking(Agent agent)
        {
            if (agent == null) return;
            agent.SetLookAgent(null);
        }
        public static void FaceToActor(Agent turnAgent, Agent targetAgent)
        {
            //计算两个人的位置差向量
            Vec3 direction = targetAgent.Position - turnAgent.Position;
            direction.Normalize();
            turnAgent.SetMovementDirection(direction.AsVec2);

        }
        public static string GetPartyInfo(Hero targetHero)
        {
            StringBuilder sb = new StringBuilder();

            MobileParty party = targetHero.PartyBelongedTo;
            if (party != null)
            {
                sb.AppendLine($"所属部队: {party.Name}");
                sb.AppendLine("\n--- 部队成员 (士兵) ---");

                TroopRoster memberRoster = party.MemberRoster;
                if (memberRoster != null && memberRoster.Count > 0)
                {
                    int totalMen = memberRoster.TotalManCount;
                    int totalWounded = memberRoster.TotalWounded;
                    int totalHeroes = memberRoster.TotalHeroes;

                    sb.AppendLine($"  [概览] 总人数: {totalMen} (英雄: {totalHeroes}, 受伤: {totalWounded})");

                    // 获取具体的部队列表
                    var troops = memberRoster.GetTroopRoster();

                    foreach (var element in troops)
                    {
                        if (element.Character == null) continue;

                        string charName = element.Character.Name.ToString();
                        int count = element.Number; // 健康数量 + 受伤数量
                        int wounded = element.WoundedNumber;
                        string tier = element.Character.IsHero ? "Hero" : $"T{element.Character.Tier}";
                        string id = element.Character.StringId;

                        string woundInfo = wounded > 0 ? $"(含伤员: {wounded})" : "";

                        sb.AppendLine($"  - [{tier}] {charName} : {count} 人 {woundInfo} [ID:{id}]");
                    }
                }
                else
                {
                    sb.AppendLine("  [结果] 光杆司令，没有士兵。");
                }
            }
            else
            {
                sb.AppendLine($" {targetHero.Name}目前没有所属部队。");
            }
            return sb.ToString();
        }
        public static string GetBagInfo(Hero targetHero,bool IsPrompt= false)
        {
            StringBuilder sb = new StringBuilder();

            if (targetHero == null)
            {
                sb.AppendLine("错误：目标 Hero 为空！");
                return sb.ToString();
            }

            sb.AppendLine($"========== 【{targetHero.Name}】 背包信息报告 ==========");

            // ---------------------------------------------------------
            // 第一部分：装备检查 (优先检查 Agent 实体，否则检查 Hero 配置)
            // ---------------------------------------------------------
            sb.AppendLine("--- [1] 贴身装备情况 ---");

            Equipment equipmentToInspect = null;

            // 1. 尝试获取场景内的 Agent
            Agent agent = null;
            if (Mission.Current != null && Mission.Current.Agents != null)
            {
                agent = Mission.Current.Agents.FirstOrDefault(a => a.Character == targetHero.CharacterObject);
            }

            // 2. 决定查看哪套装备
            if (agent != null)
            {
                // 如果人在场景里，看他实际身上穿的（包含临时捡起的武器等）
                equipmentToInspect = agent.SpawnEquipment;
            }
            else
            {
                // 如果人不在场景里，看他数据层面的战斗装备
                equipmentToInspect = targetHero.BattleEquipment;
            }
            //sb.AppendLine($"状态: {(agent != null ? "在场景中" : "未在场景中/大地图模式")} {sourceInfo}");

            sb.AppendLine($"-持有金钱: {targetHero.Gold}");

            // 3. 遍历打印装备
            if (equipmentToInspect != null)
            {
                bool hasItem = false;
                // EquipmentIndex 0-3 是武器，4-9 是防具，10-11 是马匹/马具

                if(!equipmentToInspect.Horse.IsEmpty)
                    sb.AppendLine($"-所骑马匹: {equipmentToInspect.Horse.Item.Name}");

                for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumEquipmentSetSlots; i++)
                {
                    EquipmentIndex slotIndex = i;
                    EquipmentElement element = equipmentToInspect[slotIndex];

                    if (!element.IsEmpty && element.Item != null)
                    {
                        hasItem = true;
                        string slotName = slotIndex.ToString(); // Get Enum name (e.g., Head, Body, Leg)
                        string itemId = element.Item.StringId; // The ID you need
                        string itemName = element.Item.Name.ToString();
                        string modifyName = element.ItemModifier?.Name.ToString();
                        modifyName = string.IsNullOrEmpty(modifyName) ? "" : $"[{modifyName}的]";
                        // Format: [SlotName] ItemID
                        if (!IsPrompt)
                            sb.AppendLine($"  - 槽位[{slotName,-12}]: {modifyName}{itemName} (ID: {itemId})");
                        else
                            sb.Append($"{modifyName}{itemName} ");
                    }
                }
                
                if (!hasItem) sb.AppendLine("  [结果] 身上光溜溜的，没有任何装备。");
            }
            else
            {
                sb.AppendLine("  [异常] 无法获取装备数据。");
            }


            // ---------------------------------------------------------
            // 第二部分：打印 Party 里的物品 (背包里的)
            // ---------------------------------------------------------
            sb.AppendLine("\n--- [2] 部队辎重 (物品栏) ---");

            MobileParty party = targetHero.PartyBelongedTo;

            if (party != null)
            {
                sb.AppendLine($"所属部队: {party.Name}");

                // 检查物品
                var itemRoster = party.ItemRoster;
                if (itemRoster != null && itemRoster.Count > 0)
                {
                    int totalValue = 0;

                    // 为了美观，可以按价值排序
                  
                    var sortedItems = itemRoster
            .Where(x => !x.IsEmpty && x.EquipmentElement.Item != null)
            .OrderByDescending(x => x.EquipmentElement.Item.Value * x.Amount)
            .ToList(); // 转换为 List 方便处理
                    //三个一行
                    int colIndex = 0;
                    int columnWidth = 300; // 设定每列的固定宽度（根据实际显示区域调整）
                    foreach (var element in sortedItems)
                    {
                        var item = element.EquipmentElement.Item;
                        string itemName = item.Name.ToString();
                        int amount = element.Amount;
                        int valuePerItem = item.Value;
                        int subTotal = valuePerItem * amount;

                        totalValue += subTotal;
                        string displayName = itemName.Length > 10 ? itemName.Substring(0, 9) + ".." : itemName;
                        string itemDesc = $"- {displayName} x{amount} (单:{valuePerItem}|总:{subTotal})";
                        if (itemDesc.Length < columnWidth)
                        {
                            itemDesc = itemDesc.PadRight(columnWidth);
                        }
                        sb.Append(itemDesc);
                        colIndex++;
                        colIndex = colIndex % 3;
                        if (colIndex % 3 == 0)
                        {
                            sb.AppendLine();
                        }
                    }
                    if (colIndex % 3 != 0)
                    {
                        sb.AppendLine();
                    }
                    sb.AppendLine($"  [统计] 物品种类: {itemRoster.Count} | 总估值: {totalValue}");
                }
                else
                {
                    sb.AppendLine($"{targetHero.Name}辎重中空无一物。");
                }               
            }
            else
            {
                sb.AppendLine($"{targetHero.Name}当前没有部队辎重。");
            }

            return sb.ToString();
        }

        public static void ApplyDivorceMarriage(Hero targetHero)
        {
            Hero targetHeroSpouse = targetHero.Spouse;
            if(targetHeroSpouse!= null)
            {
                targetHero.Spouse = null;
                targetHeroSpouse.Spouse = null;
                //好感变化
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(targetHero, targetHeroSpouse, -30);
                //可能要做一个事件新闻
                ActionTransactionSystem.RecordAction(SocialEventType.Divorce, targetHero, targetHeroSpouse,"强制离婚");
                InformationManager.DisplayMessage(new InformationMessage($"{targetHero.Name} 与 {targetHeroSpouse.Name} 离婚了。"));

            }
        }
        public static void ApplyMarriageLogic(Hero hero1, Hero hero2)
        {
            // ... 执行原版 MarriageAction.Apply ...
            MarriageAction.Apply(hero1, hero2);

            // 记录到事务系统

            ActionTransactionSystem.RecordAction(SocialEventType.Marriage, hero1, hero2,"结婚");
        }

        public static void OnPlayerSelect_MarryNewLover(Hero newLover)
        {
            Hero player = Hero.MainHero;
            Hero exSpouse = player.Spouse;
            Hero loverSpouse = newLover.Spouse;

            // 1. 开启事务：告诉系统接下来的一系列操作是一个整体
            ActionTransactionSystem.BeginTransaction();

            try
            {
                // 2. 如果玩家已婚，先离
                if (exSpouse != null)
                {
                    ApplyDivorceMarriage(player);
                }

                // 3. 如果对方已婚，让对方离 (甚至可能触发决斗事件，这里简化为离婚)
                if (loverSpouse != null)
                {
                    // 这里也可以记录一个离婚事件，如果系统足够复杂，
                    // 事务管理器可以把 "玩家离婚" 和 "新欢离婚" 合并成 "双重出轨"
                    ApplyDivorceMarriage(newLover);
                }

                // 4. 结婚
                ApplyMarriageLogic(player, newLover);
            }
            catch (Exception e)
            {
                // 错误处理
            }
            finally
            {
                // 5. 提交事务：系统分析刚才发生的 3 件事，发现符合 "ScandalousRemarriage" 模式
                // 于是只广播一条 "玩家为了新欢抛弃发妻" 的重磅新闻，而不是三条零散新闻
                ActionTransactionSystem.CommitTransaction();
            }
        }
    }
}