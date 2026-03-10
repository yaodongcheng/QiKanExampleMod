using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.Engine;
using TaleWorlds.ScreenSystem;
using NetworkMessages.FromServer;
using TaleWorlds.Core;
using psai.net;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem;
using SandBox.Objects.Usables;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Party;
using Helpers;
using System.Reflection;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameState;
using ExampleMod.StoryEngineBag;
using System.IO;

namespace ExampleMod
{
    public class MyCommands
    {

      

        [CommandLineFunctionality.CommandLineArgumentFunction("summon_npc", "custom")]
        public static string ExecuteSummonPureNpc(List<string> args)
        {
            // 1. 检查是否在场景中且主角存在
            if (Mission.Current == null || Agent.Main == null)
            {
                return "error: must in mission";
            }
            // 2. 检查是否有参数输入
            if (args.Count == 0)
            {
                return "ERROR：PLEASE INPUT hero id";
            }
            

            for (int i = 0; i < args.Count; i++)
            {
                string heroId = args[i];
                HeroSpawnerMissionBehavior.TeleportExistingHeroById(heroId,2.0f);
            }

            return "spawn suscess";
        }


        //让两个人决斗
        [CommandLineFunctionality.CommandLineArgumentFunction("duel_npc", "custom")]
        public static string ExecuteDuel(List<string> args)
        {


            if (Mission.Current == null || Agent.Main == null)
            {
                return "error: not in misson";
            }

            if (args.Count != 2)
            {
                string received = string.Join(", ", args);
                return $"error: params num is not 2 :{args.Count},received:[{received}]";
            }
            //基于ID搜索当前场景已经召唤的agent
            string targetId1 = args[0];
            string targetId2 = args[1];
            Agent agent1 = null;
            Agent agent2 = null;
            foreach (Agent agent in Mission.Current.Agents)
            {
                if (agent.Character != null && agent.IsHuman)
                {
                    if (targetId1 == "player")
                        agent1 = Agent.Main;
                    if (targetId2 == "player")
                        agent2 = Agent.Main;
                    if (agent.Character.StringId == targetId1 && agent1 == null)
                        agent1 = agent;
                    if (agent.Character.StringId == targetId2 && agent2 == null)
                        agent2 = agent;
                    if (agent1 != null && agent2 != null)
                        break;
                }
            }
            if (agent1 == null || agent2 == null)
            {
                ExecuteSummonPureNpc(args);
                foreach (Agent agent in Mission.Current.Agents)
                {
                    if (agent.Character != null && agent.IsHuman)
                    {
                        if (agent.Character.StringId == targetId1 && agent1 == null)
                            agent1 = agent;
                        if (agent.Character.StringId == targetId2 && agent2 == null)
                            agent2 = agent;
                        if (agent1 != null && agent2 != null)
                            break;
                    }
                }
            }
            if (agent1 == null) return $"{targetId1} not find";
            if (agent2 == null) return $"{targetId2} not find";
            if(agent1 == agent2) return "can not duel with self";

            //AgentControlHelper.StartFight(agent1, agent2);
            CombatManager.StartFight(agent1, agent2, -1, -1, Peace: true);
            return "find success";
        }
       
        
        
       

        [CommandLineFunctionality.CommandLineArgumentFunction("do_anim", "custom")]
        public static string ExecuteDoAnim(List<string> args)
        {
            // 1. 检查是否在场景中且主角存在
            if (Mission.Current == null || Agent.Main == null)
            {
                return "错误：必须进入战场或场景后才能使用此命令。";
            }

            // 2. 检查是否有参数输入
            if (args.Count == 0)
            {
                return "Please Enter AnimType.For example : custom.do_anim act_sit_down_on_floor_2 or act_stand_up_to_front";
            }

            string actionName = args[0];
            Agent executeAgent = Agent.Main;
            if (args.Count == 2)
            {
                string agentId = args[1];
                var targetAgent = Mission.Current.Agents.FirstOrDefault(a => a.Character?.StringId == agentId);
                if (targetAgent != null)
                {
                    executeAgent = targetAgent;
                }
            }


            try
            {
                // 3. 将字符串ID转换为游戏引擎可识别的 Index
                // 这里的 actionName 就是你在 XML 文件里找到的那个 id="xxx"
                ActionIndexCache actionIndex = ActionIndexCache.Create(actionName);

                // 4. 执行动作
                // 参数说明: 
                // channel 0 = 全身/下半身 (通常用于移动或全身动作)
                // channel 1 = 上半身 (通常用于攻击、格挡)
                // 这里我们使用 Channel 0 以获得最高优先级
                executeAgent.SetActionChannel(0, actionIndex, false, 0UL, 0f, 1f, -0.2f, 0.4f, 0f, false, -0.2f, 0, true);

                return $"Success：{executeAgent.Name} is trying to do anim {actionName} ";
            }
            catch (System.Exception e)
            {
                return "Error：" + e.Message;
            }
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("print_npcs", "custom")]
        public static string ExecutePrintNpc(List<string> args)
        {
            if (Mission.Current == null) return "Please Enter the mission First.";
            int radius = 0;
            if(args.Count >0)
            {
                string radiusStr = args[0];
                radius = int.Parse(radiusStr);
            }
            var allAgents = Mission.Current.Agents;
            MBList<Agent> nearbyAgents = new MBList<Agent>();
            if (radius > 0)
            {
                 Mission.Current.GetNearbyAgents(Agent.Main.Position.AsVec2, radius, nearbyAgents);
            }

            // 使用 StringBuilder 提高性能
            StringBuilder sb = new StringBuilder();
            int count = 0;
            int totalCount = Mission.Current.AllAgents.Count;

            Vec3 eyePos = Agent.Main.GetEyeGlobalPosition();
            Vec3 lookDir = Agent.Main.LookFrame.rotation.f.NormalizedCopy();
            Agent deadAgent = Mission.Current.AllAgents.FirstOrDefault(a => !Mission.Current.Agents.Contains(a));
            if(deadAgent != null)
            {
                sb.AppendLine($"--- Detected Dead Agent: {deadAgent.Name} (ID: {deadAgent.Character.StringId}) ---");
                sb.AppendLine($"IsHuman? {deadAgent.IsHuman} IsActive? {deadAgent.IsActive()} HasCharacter? {deadAgent.Character}");
                Vec3 targetPos = deadAgent.Position;
                targetPos.z += 0.5f;

                // 计算从眼睛指向尸体的向量
                Vec3 dirToTarget = (targetPos - eyePos).NormalizedCopy();

                // 计算点积 (1.0 = 正中心, 0 = 垂直, -1 = 背后)
                float dot = Vec3.DotProduct(lookDir, dirToTarget);
                sb.AppendLine($"Dot Product with Look Direction: {dot}");
            }



            if (radius == 0)
            {
                foreach (Agent agent in Mission.Current.Agents)
                {
                    // 1. 过滤掉非人类（马匹、攻城器械等）
                    //if (!agent.IsHuman) continue;

                    // 2. 过滤掉死人（如果你只想看活着的）
                    //if (!agent.IsActive()) continue;

                    // 3. 安全获取名字
                    // 有些 Agent 可能没有 Name 属性，或者名字是空的
                    string name = agent.Name;
                    string id = agent.Character.StringId;
                    if (string.IsNullOrWhiteSpace(name) && agent.Character != null)
                    {
                        // 如果 Name 属性为空，尝试获取 Character 定义的名字
                        name = agent.Character.Name.ToString();
                        id = agent.Character.StringId;
                    }

                    if(!string.IsNullOrWhiteSpace(name))
                    {
                        name = "empty name";
                    }

                        sb.Append($"{name}:{id}");
                        sb.Append(" | ");
                        if (count % 3 == 0)
                            sb.Append(" \n ");
                        count++;
                }
            }
            else
            {
                foreach (Agent agent in nearbyAgents)
                {
                    // 1. 过滤掉非人类（马匹、攻城器械等）
                    //if (!agent.IsHuman) continue;

                    // 2. 过滤掉死人（如果你只想看活着的）
                    //if (!agent.IsActive()) continue;

                    // 3. 安全获取名字
                    // 有些 Agent 可能没有 Name 属性，或者名字是空的
                    string name = agent.Name;
                    string id = agent.Character.StringId;
                    if (string.IsNullOrWhiteSpace(name) && agent.Character != null)
                    {
                        // 如果 Name 属性为空，尝试获取 Character 定义的名字
                        name = agent.Character.Name.ToString();
                        id = agent.Character.StringId;
                    }
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        name = "empty name";
                    }
                        sb.Append($"{name}:{id}");
                        sb.Append(" | ");
                        if (count % 3 == 0)
                            sb.Append(" \n ");
                        count++;
                }
            }
            if (count == 0) return "There is no visiable Npc in this mission.";
            // 4. 打印总数，防止看起来像没输出
            return $"Find {count}/{totalCount}  NPC: \n" + sb.ToString();
        }


        [CommandLineFunctionality.CommandLineArgumentFunction("weapon_sheath", "custom")]
        public static string ExecuteTeleportToNpc(List<string> args)
        {
            if (Mission.Current == null) return "Please Enter the mission First.";

            Agent.Main.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);
            return "success";
        }


      


        [CommandLineFunctionality.CommandLineArgumentFunction("look", "custom")]
        public static string ExecuteLook(List<string> args)
        {
            if (Mission.Current == null || Agent.Main == null)
            {
                return "错误：请先进入战场。";
            }

            if (args.Count == 0)
            {
                return "请输入参数: mount, npc [名字], camera, 或 reset";
            }

            string targetType = args[0].ToLower();

            // --- 1. 看向坐骑 ---
            if (targetType == "mount")
            {
                if (Agent.Main.MountAgent != null)
                {
                    // 让主角的头锁定坐骑
                    Agent.Main.SetLookAgent(Agent.Main.MountAgent);
                    return "表演：正在深情地注视着爱马。";
                }
                return "错误：你当前没有骑马，或者马不在身边。";
            }

            // --- 2. 看向特定 NPC ---
            else if (targetType == "npc")
            {
                if (args.Count < 2) return "错误：请输入NPC名字的一部分。例如: custom.look npc 织田";

                string searchName = args[1];

                // 在当前战场的所有单位中查找名字匹配的人（排除自己）
                Agent targetAgent = Mission.Current.Agents
                    .FirstOrDefault(a => a != Agent.Main && a.Name.Contains(searchName) && a.IsActive());

                if (targetAgent != null)
                {
                    Agent.Main.SetLookAgent(targetAgent);
                    return $"表演：正在注视 NPC '{targetAgent.Name}'。";
                }
                else
                {
                    return $"未找到名字包含 '{searchName}' 的NPC。";
                }
            }

            // --- 3. 看向镜头 (自拍/说话视角) ---
            else if (targetType == "camera")
            {
                // 获取当前摄像机的位置
                // 注意：这里我们只取一次位置，如果移动镜头，角色会盯着刚才那个点
                // 想要实时盯着镜头比较复杂，需要每帧更新，这里做静态摆拍
                if (true)
                {
                    Vec3 cameraPos = Mission.Current.GetCameraFrame().origin;

                    Agent.Main.SetLookToPointOfInterest(cameraPos);
                    return "表演：正在注视着镜头（观众）。";
                }
            }

            // --- 4. 重置视线 ---
            else if (targetType == "reset")
            {
                // 清除锁定，恢复鼠标控制视线
                Agent.Main.ResetLookAgent();
                return "表演：视线已重置，恢复自由控制。";
            }

            return "未知指令。可用参数: mount, npc [名字], camera, reset";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("cam_face", "custom")]
        public static string ExecuteCamFace(List<string> args)
        {
            if (Mission.Current == null || Agent.Main == null) return "error:no mission";

            // 【关键修正】: 不通过 Behavior 获取，而是直接通过 ScreenManager 获取当前屏幕
            MissionScreen missionScreen = ScreenManager.TopScreen as MissionScreen;

            if (missionScreen == null) return "error: no mission screen";

            // 1. 获取主角眼睛的位置
            // 1. 确定目标点（玩家的眼睛）
            Vec3 targetPos = Agent.Main.LookFrame.origin;

            // 2. 确定摄像机的位置（玩家正前方 2.5 米，高度稍微抬高一点）
            // Agent.Main.LookDirection 是玩家脸朝向的方向
            Vec3 forwardDir = Agent.Main.LookDirection;
            forwardDir.Normalize(); // 标准化向量，确保长度为1

            // 位置计算：玩家位置 + (朝向向量 * 距离)
            Vec3 cameraPos = targetPos + (forwardDir * 2.5f);

            // 稍微把摄像机抬高一点 (0.5米)，形成一点点俯视感，这样更有电影感
            cameraPos.z += 0.5f;

            // 3. 【核心修正】手动计算“从摄像机看向玩家”的方向向量
            // 向量减法：终点 - 起点 = 指向终点的向量
            Vec3 directionFromCamToPlayer = targetPos - cameraPos;
            directionFromCamToPlayer.Normalize();

            // 4. 【核心修正】强制构建旋转矩阵
            // Mat3.CreateMat3WithForward(前向向量, 上方向量)
            // 我们明确告诉引擎：摄像机的“正前方(Y轴)”必须等于 directionFromCamToPlayer
            Mat3 rotation = Mat3.CreateMat3WithForward(in directionFromCamToPlayer);

            // 5. 组合成最终的坐标帧 (MatrixFrame = 旋转 + 位置)
            MatrixFrame camFrame = new MatrixFrame(rotation, cameraPos);

            // 6. 创建相机并赋值
            Camera customCam = Camera.CreateCamera();
            customCam.Frame = camFrame;
            missionScreen.CustomCamera = customCam;

            return "Camera see you now.";
        }

        // 恢复正常镜头
        // 命令: custom.cam_reset
        [CommandLineFunctionality.CommandLineArgumentFunction("cam_reset", "custom")]
        public static string ExecuteCamReset(List<string> args)
        {
            MissionScreen missionScreen = ScreenManager.TopScreen as MissionScreen;
            if (missionScreen != null)
            {
                // 设为 null 就会自动切回游戏默认视角
                missionScreen.CustomCamera = null;
                return "screen reset success。";
            }
            return "error:no screen";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("print_npc_move_info", "custom")]
        public static string ExecutePrintMoveInfo(List<string> args)
        {
            if(args.Count == 0)
            {
                return "error:no npc";
            }
            string stringId = args[0];
            //获取当前场景的id为stringId的agent

            Agent agent = null;
            foreach (Agent a in Mission.Current.Agents)
            {
                if (a.IsHuman && a.Character.StringId == stringId)
                    { agent = a; break; }
            }
            if (agent == null)
            {
                return $"error:no agent id = {stringId}";
            }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"--- 诊断 Agent: {agent.Name} ---");

            // 1. 基础控制状态
            sb.AppendLine($"IsActive: {agent.IsActive()}");
            sb.AppendLine($"Controller: {agent.Controller}"); // 应该是 AI
            sb.AppendLine($"State: {agent.State}"); // 应该是 Active

            // 2. 关键：是否正在使用物体（这是店主不动的最大嫌疑）
            sb.AppendLine($"IsUsingGameObject: {agent.IsUsingGameObject}");
            if (agent.IsUsingGameObject)
            {
                sb.AppendLine($"TargetObject: {agent.CurrentlyUsedGameObject?.GameEntity?.Name ?? "Unknown"}");
            }

            // 3. 关键：AI 脚本标志位（检查是否有 DisableMove 之类的标志）
            sb.AppendLine($"ScriptedFlags: {agent.GetScriptedFlags()}");

            // 4. 关键：当前的动作动画（检查是否卡在特殊动画里，如站岗、擦桌子）
            // 通道 0 是基础动作（站立/走/跑），通道 1 是上半身动作
            var action0 = agent.GetCurrentAction(0);
            var action1 = agent.GetCurrentAction(1);
            sb.AppendLine($"Action Ch0: {action0.Name}");
            sb.AppendLine($"Action Ch1: {action1.Name}");

            // 5. 移动能力检查
            sb.AppendLine($"MovementLockedState: {agent.MovementLockedState}"); // 这是一个属性，如果为 false，肯定动不了
            sb.AppendLine($"MovementFlags: {agent.MovementFlags}");

           

            return sb.ToString();
        }





        [CommandLineFunctionality.CommandLineArgumentFunction("playsound", "custom")]
        public static string ExecutePlaySound(List<string> args)
        {
            if (PsaiCore.Instance != null)
            {
                PsaiCore.Instance.StopMusic(true, 2.0f);
            }
            //SoundManager.SetListenerFrame(listenderFrame);
            string soundStr = "14_HYOUJOU";
            int soundIndex = SoundEvent.GetEventIdFromString(soundStr);
            SoundEvent sound = SoundEvent.CreateEvent(soundIndex, Mission.Current.Scene);
            sound.Play();
            //var result =PsaiCore.Instance.TriggerMusicTheme(1001, 10);

            return "psaicore success";


        }

        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("check_agent_equip", "custom")]
        public static string CheckAgentEquip(List<string> args)
        {
            if (TaleWorlds.MountAndBlade.Mission.Current == null)
                return "Error: Mission is not running.";

            if (args.Count == 0)
                return "Usage: custom.check_agent_equip [AgentName]";

            string targetId = args[0];

            foreach (Agent agent in Mission.Current.Agents)
            {
                if (agent.Character != null && agent.Character.StringId == targetId)
                {
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    sb.AppendLine($"--- Checking Agent: {agent.Name} ---");
                    sb.AppendLine($"State: {agent.State}, AI State Flag: {agent.AIStateFlags}");
                    // 检查 4 个武器槽位
                    for (int i = 0; i < 4; i++)
                    {
                        var eqIndex = (TaleWorlds.Core.EquipmentIndex)i;
                        var weapon = agent.Equipment[eqIndex];

                        if (!weapon.IsEmpty)
                        {
                            sb.AppendLine($"[Slot {i}] {weapon.Item.StringId} ({weapon.Item.Name})");
                            sb.AppendLine($"    - Type: {weapon.Item.PrimaryWeapon?.WeaponClass}");
                            sb.AppendLine($"    - Ammo Count: {weapon.Amount}/{weapon.ModifiedMaxAmount}"); // 关键检查点
                            sb.AppendLine($"      -> Weapon Class: {weapon.Item.PrimaryWeapon.WeaponClass}");
                            sb.AppendLine($"    - Requires Ammo Class: {weapon.Item.PrimaryWeapon.AmmoClass}");

                        }
                        else
                        {
                            sb.AppendLine($"[Slot {i}] Empty");
                        }
                    }
                    // 检查手里真正拿着啥
                    var wieldedMain = agent.GetWieldedItemIndex(Agent.HandIndex.MainHand);
                    sb.AppendLine($"\nCurrently Wielding MainHand Index: {wieldedMain}");
                    return sb.ToString();
                }
            }
            return "Agent not found.";

        }


        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("current_location", "custom")]
        public static string ListCurrentLocations(List<string> args)
        {
            // 1. 安全检查
            if (Campaign.Current == null || Hero.MainHero == null)
            {
                return "Error: Campaign or MainHero is null.";
            }

            if (Hero.MainHero.CurrentSettlement == null)
            {
                InformationManager.DisplayMessage(new InformationMessage("错误：你当前不在定居点内。", Colors.Red));
                return "Error: Not in a settlement.";
            }

            Settlement settlement = Hero.MainHero.CurrentSettlement;
            LocationComplex complex = LocationComplex.Current;

            if (complex == null)
            {
                return "Error: LocationComplex is null.";
            }

            // 2. 准备输出内容
            // sbDisplay 用于左下角中文显示
            StringBuilder sbDisplay = new StringBuilder();
            sbDisplay.AppendLine($"\n=== [{settlement.Name}] 可用场景 ID ===");

            // sbConsole 用于控制台英文返回
            StringBuilder sbConsole = new StringBuilder();
            sbConsole.AppendLine($"\n=== Location List for {settlement.Name} ===");

            // 3. 遍历所有地点
            foreach (Location loc in complex.GetListOfLocations())
            {
                // loc.StringId 就是你在代码里需要用的 ID (例如 "lordshall")
                string id = loc.StringId;

                // 获取场景文件名 (0, 1, 2, 3 代表不同等级，通常取 1 或当前等级)
                string sceneName = loc.GetSceneName(settlement.Town != null ? settlement.Town.GetWallLevel() : 1);

                string infoLine = $"ID: [{id}]  --->  Scene: {sceneName}";

                sbDisplay.AppendLine(infoLine);
                sbConsole.AppendLine(infoLine);
            }

            sbDisplay.AppendLine("=================================");

            // 4. 分别输出
            // 中文输出到屏幕左下角
            InformationManager.DisplayMessage(new InformationMessage(sbDisplay.ToString(), Colors.Green));

            // 英文输出到控制台窗口
            return sbConsole.ToString() + "\nDone. Check the game log for details.";
        }


        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("enter_lordshall", "custom")]
        public static string ExecuteEnterLordsHall(List<string> args)
        {
            // 获取当前定居点的 LocationComplex
            LocationComplex locationComplex = LocationComplex.Current;
            if (locationComplex != null)
            {
                // "lordshall" 是骑砍2中城主大厅的标准 ID
                Location lordsHall = locationComplex.GetLocationWithId("lordshall");
                Campaign.Current.GameMenuManager.NextLocation = lordsHall;
                Campaign.Current.GameMenuManager.PreviousLocation = locationComplex.GetLocationWithId("center");
                Mission.Current.EndMission();
                // 这会触发加载画面，并自动结束当前的 Mission
                ///      Campaign.Current.GameMenuManager.SetNextMenu("town_keep");
                return "success";

            }
            return "failure";
        }

        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("find_chairs", "custom")]
        public static string FindAllChairs(List<string> strings)
        {
            if (Mission.Current == null)
                return "Error: You must be in a mission to use this command.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\n=== Searching for Chairs (Sittable Objects) ===");

            int count = 0;

            // iterate through interactive objects only (MissionObjects)
            // This avoids printing static meshes like walls or ground.
            foreach (MissionObject obj in Mission.Current.MissionObjects)
            {
                // Check if the object is actually a Chair
                if (obj is Chair)
                {
                    count++;
                    GameEntity entity = obj.GameEntity;
                    Vec3 pos = entity.GlobalPosition;
                    var tags = entity.Tags;


                    sb.AppendLine($"[Found Chair #{count}]");
                    sb.AppendLine($"  Name: {entity.Name}");
                    sb.Append("  Tags: ");
                    foreach (var tag in tags)
                    {
                        sb.Append($" {tag} ");
                    }

                    sb.Append("\n"); // Important: Use these tags to find it in code!
                    sb.AppendLine($"  Pos : {pos.x:F2}, {pos.y:F2}, {pos.z:F2}");
                    sb.AppendLine("--------------------------------");
                }
            }

            if (count == 0)
            {
                return "Result: No chairs found in this scene.";
            }

            // Output to game log (left bottom) as well, but keep it English as requested
            string resultMsg = $"Result: Found {count} chairs. Check console for details.";
            InformationManager.DisplayMessage(new InformationMessage(resultMsg, Colors.Green));

            return sb.ToString();
        }
        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("set_npc_to_hero", "custom")]
        public static string ExecuteSetNpcToHero(List<string> args)
        {
            if (Campaign.Current == null) return "Error: Campaign not loaded.";
            if (args.Count < 1) return "Usage: custom.set_npc_to_hero [HeroStringId]";

            string targetStringId = args[0];
            Hero targetHero = Campaign.Current.CampaignObjectManager.Find<Hero>(targetStringId);

            if (targetHero == null) return $"Error: Hero '{targetStringId}' not found.";
            if (!targetHero.IsAlive) return "Error: Hero is dead.";
            if (targetHero == Hero.MainHero) return "Error: Already this hero.";

            // =================================================================
            // 步骤 1：确保目标有队伍 (你的核心诉求)
            // =================================================================
            // 如果他在城里或者流浪，没有 MobileParty，夺舍后会变成幽灵。
            // 这里我们用 MobilePartyHelper 强行给他造一个队。
            if (targetHero.PartyBelongedTo == null)
            {
                // 注意：这里调用你提到的 MobilePartyHelper
                // 确保该函数可用，且参数正确
                MobilePartyHelper.SpawnLordParty(targetHero, targetHero.HomeSettlement ?? Settlement.All.FirstOrDefault());
            }

            // =================================================================
            // 步骤 2：政治身份修正 (防止 Kingdom 界面崩溃)
            // =================================================================
            // 原版逻辑规定：只有家族族长能打开王国界面。
            if (targetHero.Clan != null && targetHero.Clan.Leader != targetHero)
            {
                targetHero.Clan.SetLeader(targetHero);
            }

            // =================================================================
            // 步骤 3：官方夺舍 (参考 StartAsAnyone)
            // =================================================================
            // 这会自动切换控制权、UI、主队伍指针
            ChangePlayerCharacterAction.Apply(targetHero);

            // =================================================================
            // 步骤 4：后勤补给 (完全复刻 StartAsAnyone 的逻辑)
            // =================================================================
            // 夺舍后，现在的 MobileParty.MainParty 就是织田信长的队伍了
            MobileParty mainParty = MobileParty.MainParty;

            if (mainParty != null)
            {
                // 4.1 刷新相机
                MapState mapState = GameStateManager.Current.ActiveState as MapState;
                if (mapState != null)
                {
                    mapState.Handler.ResetCamera(true, true);
                    mapState.Handler.TeleportCameraToMainParty();
                }

                // 4.2 发放口粮 (参考代码逻辑：根据部队阶级发粮食，防止饿死)
                ItemObject grain = DefaultItems.Grain;
                ItemObject meat = DefaultItems.Meat;

                // 清空旧杂物 (可选，StartAsAnyone 做了这步)
                // mainParty.ItemRoster.Clear(); 

                foreach (var element in mainParty.MemberRoster.GetTroopRoster())
                {
                    int number = element.Number;
                    // StartAsAnyone 的算法：部队等级越高，发的粮食越多
                    int grainCount = (int)Math.Sqrt((double)element.Character.Tier) * (number / 2);
                    int meatCount = number / 3;

                    if (grainCount > 0) mainParty.ItemRoster.AddToCounts(grain, grainCount);
                    if (meatCount > 0) mainParty.ItemRoster.AddToCounts(meat, meatCount);
                }

                // 简单补底：如果算出来还是没吃的，强制给100个面包
                if (mainParty.ItemRoster.TotalFood == 0)
                {
                    mainParty.ItemRoster.AddToCounts(DefaultItems.Grain, 100);
                }
            }

            return $"Success: You are now {targetHero.Name}!";
        }


        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("teleport", "custom")]
        public static string TeleportAgent(List<string> args)
        {
            if (Mission.Current == null || Mission.Current.MainAgent == null)
                return "Error: Mission or MainAgent is null.";

            if (args.Count < 4)
            {
                return "Error: Usage format is 'drama.teleport x y z id' (4 arguments required).";
            }

            try
            {
                // Parse arguments
                float x = float.Parse(args[0]);
                float y = float.Parse(args[1]);
                float z = float.Parse(args[2]);
                string agentId = args[3];

                Agent teleAgent = Mission.Current.MainAgent;
                if (agentId != "player")
                    teleAgent = Mission.Current.Agents.FirstOrDefault(a => a.Character.StringId == agentId);
                if (teleAgent == null) { teleAgent = Mission.Current.MainAgent; }

                // Create target vector
                Vec3 targetPos = new Vec3(x, y, z);

                // Teleport
                teleAgent.TeleportToPosition(targetPos);

                return $"Success:{teleAgent.Name} Teleported to {x:F2}, {y:F2}, {z:F2}";
            }
            catch (FormatException)
            {
                return "Error: Invalid number format. Please use numbers (e.g., 100.5).";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }


        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("print_pos_dir", "custom")]
        public static string ExecutePrintPosDir(List<string> strings)
        {
            //参数一是stringId，没有的话默认用玩家 Pos是vec3，dir是vec2
            // 1. 检查是否在场景（Mission）中
            if (Mission.Current == null)
            {
                return "Error: You must be in a Mission (Scene) to use this command.";
            }

            Agent targetAgent = null;

            // 2. 确定目标 Agent
            if (strings == null || strings.Count == 0)
            {
                // --- 情况 A: 没有参数，默认获取玩家 ---
                targetAgent = Mission.Current.MainAgent;
                if (targetAgent == null)
                {
                    // 有时候玩家可能是自由摄像机模式，尝试获取由玩家控制的 Agent
                    if (Agent.Main != null) targetAgent = Agent.Main;
                    else return "Error: Player Agent not found (Are you in free camera mode?).";
                }
            }
            else
            {
                // --- 情况 B: 有参数，根据 StringId 查找 NPC ---
                string targetId = strings[0];

                // 遍历场景中所有 Agent 寻找匹配的 StringId
                foreach (Agent agent in Mission.Current.Agents)
                {
                    // 必须是人类，且拥有 Character 对象
                    if (agent.IsHuman && agent.Character != null)
                    {
                        // 比较 StringId (Hero 的 ID 或者兵种 ID)
                        if (agent.Character.StringId == targetId)
                        {
                            targetAgent = agent;
                            break;
                        }
                    }
                }

                if (targetAgent == null)
                {
                    targetAgent = Agent.Main;
                }
            }

            // 3. 获取坐标和朝向
            Vec3 pos = targetAgent.Position;

            // 获取面朝方向 (LookDirection 是 Vec3，转为 Vec2 通常用于 SetMovePos 或 Teleport)
            Vec2 dir = targetAgent.GetMovementDirection();
            dir.Normalize(); // 归一化，保证向量长度为 1

            // 4. 格式化输出 (保留3位小数，直接生成可用的 C# 代码字符串)
            string posStr = $"new Vec3({pos.x:F3}f, {pos.y:F3}f, {pos.z:F3}f)";
            string dirStr = $"new Vec2({dir.x:F3}f, {dir.y:F3}f)";

            // 5. 返回结果
            return $"\n--- Agent Data: {targetAgent.Name} ---\n" +
                   $"Position Code: {posStr}\n" +
                   $"Direction Code: {dirStr}\n" +
                   $"Raw Pos: {pos}\n" +
                   $"Raw Dir: {dir}\n" +
                   $"Location: {VisualCommands.GetCurrentLocationId()}\n" +
                   $"-----------------------------";
        }










        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("print_agent_anims", "custom")]
        public static string ListAgentActions(List<string> strings)
        {
            if (Mission.Current == null)
                return "Error: Mission is null.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\n=== Agent Action Report ===");

            int count = 0;

            // Loop through all agents in the scene
            foreach (Agent agent in Mission.Current.Agents)
            {
                // Filter: Only show active humans (ignore horses and dead bodies to reduce spam)
                if (agent.IsHuman && agent.IsActive())
                {
                    count++;

                    // Channel 0: Usually Base movement (Stand, Walk, Run, Sit)
                    ActionIndexValueCache action0 = agent.GetCurrentActionValue(0);

                    // Channel 1: Usually Upper body actions (Cheer, Attack, Drink)
                    ActionIndexValueCache action1 = agent.GetCurrentActionValue(1);

                    // Monster / ActionSet: Defines the "class" of animations (e.g., human_warrior, human_lord)
                    string actionSetId = agent.Monster != null ? agent.Monster.StringId : "Unknown";

                    float distToPlayer = 0f;
                    if (Mission.Current.MainAgent != null)
                    {
                        distToPlayer = agent.Position.Distance(Mission.Current.MainAgent.Position);
                    }

                    sb.AppendLine($"[Agent #{agent.Index}] Name: {agent.Name}");
                    sb.AppendLine($"  Dist to Player: {distToPlayer:F1}m");
                    sb.AppendLine($"  ActionSet (Monster): {actionSetId}");
                    sb.AppendLine($"  Current Action Ch0 (Base): {action0.Name}");

                    // Only print Channel 1 if it is actually playing something different
                    if (action1 != ActionIndexValueCache.act_none)
                    {
                        sb.AppendLine($"  Current Action Ch1 (Upper): {action1.Name}");
                    }

                    // Check if they are using a GameObject (like a chair)
                    if (agent.CurrentlyUsedGameObject != null)
                    {
                        sb.AppendLine($"  Using Object: {agent.CurrentlyUsedGameObject.GameEntity.Name} (Type: {agent.CurrentlyUsedGameObject.GetType().Name})");
                    }

                    sb.AppendLine("---------------------------");
                }
            }

            if (count == 0) return "No active human agents found.";

            return sb.ToString();
        }


        [CommandLineFunctionality.CommandLineArgumentFunction("check_all_variable_in_file", "custom")]
        public static string DumpVariables(List<string> strings)
        {
            if (GlobalVariableBehavior.Instance == null)
            {
                return "Error: GlobalVariableBehavior Instance null.";
            }

            try
            {
                // 1. 获取所有变量的字符串快照
                string dumpContent = GlobalVariableBehavior.Instance.DumpAllVariables();

                // 2. 确定保存路径
                // 通常保存在: 文档/Mount and Blade II Bannerlord/Configs/StoryEngine_Dump.txt
                string documentsPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal);
                string savePath = System.IO.Path.Combine(documentsPath, "Mount and Blade II Bannerlord", "Configs", "StoryEngine_Dump.txt");

                // 确保目录存在
                string dir = System.IO.Path.GetDirectoryName(savePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // 3. 写入文件 (强制使用 UTF-8 编码以支持中文)
                File.WriteAllText(savePath, dumpContent, Encoding.UTF8);

                // 4. 在游戏左下角提示 (这里支持中文)
                InformationManager.DisplayMessage(new InformationMessage($"[剧本] 变量已导出至: {savePath}", Color.FromUint(0x00FF00)));

                return "Success";
            }
            catch (Exception ex)
            {
                // 如果出错，打印错误信息
                return $"Error: {ex.Message}";
            }
        }



        [CommandLineFunctionality.CommandLineArgumentFunction("print_equip", "custom")]
        public static string PrintEquip(List<string> strings)
        {
            StringBuilder sb = new StringBuilder();
            CharacterObject targetChar = null;
            string stringId = "";
            if (Campaign.Current != null)
            {
                if (strings.Count >= 1)
                {
                    stringId = strings[0];
                    //基于stringId获取当前场景的Agent
                   
                    Agent agent = Mission.Current.Agents.FirstOrDefault(a => a.Character.StringId == stringId);
                    if (agent != null)
                    {
                        sb.AppendLine($">> Print {stringId} 's equipment:");
                        targetChar = agent.Character as CharacterObject;
                        AppendEquipmentToLog(sb, agent.SpawnEquipment, "Spawn Loadout");
                    }
                    else
                    {
                        sb.AppendLine($">> Can Not Find {stringId}  Print Player's equipment:");
                        AppendEquipmentToLog(sb, Agent.Main.SpawnEquipment, "Spawn Loadout");
                    }
                    
                }
                else
                {
                    sb.AppendLine($">> Can Not Find {stringId}  Print Player's equipment:");
                    AppendEquipmentToLog(sb, Agent.Main.SpawnEquipment, "Spawn Loadout");
                }
                return sb.ToString();
            }
            else
            {
                //大地图，就默认打主角吧
                targetChar = Hero.MainHero.CharacterObject;
                AppendEquipmentToLog(sb, targetChar.Equipment, "Battle Loadout");
                AppendEquipmentToLog(sb, targetChar.FirstCivilianEquipment, "Civilian Loadout");
                return sb.ToString();
            }
            
        }

        // Helper method to iterate through slots and format the string (English Only)
        private static void AppendEquipmentToLog(StringBuilder sb, Equipment equipment, string loadoutName)
        {
            sb.AppendLine($"\n--- {loadoutName} ---");

            if (equipment == null)
            {
                sb.AppendLine("Error: Equipment data is null.");
                return;
            }
            //Equipment battleEquip = Hero.MainHero.BattleEquipment;
            // Iterate through all equipment slots (Weapons, Head, Body, Leg, Gloves, Cape, Horse, Harness)
            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumEquipmentSetSlots; i++)
            {
                EquipmentIndex slotIndex = i;
                EquipmentElement element = equipment[slotIndex];

                if (!element.IsEmpty && element.Item != null)
                {
                    string slotName = slotIndex.ToString(); // Get Enum name (e.g., Head, Body, Leg)
                    string itemId = element.Item.StringId; // The ID you need

                    // Format: [SlotName] ItemID
                    sb.AppendLine($"[{slotName}] {itemId}");
                }
            }
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("update_historyname", "custom")]
        public static string UpdateHistoryName(List<string> strings)
        {
            StoryEngine.ChangeNameBasedOnHistory();
            return "success";
        }
        [CommandLineFunctionality.CommandLineArgumentFunction("divorce", "custom")]
        public static string ExecuteDivorce(List<string> strings)
        {
            string stringId = "";
            if (strings.Count > 0)
            {
                stringId = strings[0];
                Hero targetHero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == stringId);
                if (targetHero != null)
                {
                    targetHero.Spouse = null;
                    return "stringId divorce success";
                }

            }
            
            

                Hero.MainHero.Spouse = null;
                return "player divorce success";
            
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("ninja_report", "custom")]
        public static string ShowNinjaReport(List<string> strings)
        {

            string particleName = "psys_game_boulder_stone_coll";
            if (strings.Count ==2 )
            {
                particleName = strings[1];
            }

            

            // 【关键步骤】将字符串名字转换为 int ID
            int particleId = ParticleSystemManager.GetRuntimeIdByName(particleName);

            // 调用你在代码中提供的 CreateBurstParticle 方法
            // 注意：Mission.Current.Scene 就是你提供的 Scene 类的实例

            Agent mainAgent = Mission.Current.MainAgent;
            if (particleId != -1)
            {
             //   InformationManager.DisplayMessage(new InformationMessage($"忍者smoke已召唤!{particleId}"));
                Mission.Current.Scene.CreateBurstParticle(particleId, mainAgent.Frame);
            }
            




            NinjaNotificationManager.Show("忍者报告!", () => {
                // 这里写点击圆圈后要发生的事情
                // 比如：Mission.Current.SpawnNinja(...);
        //        InformationManager.DisplayMessage(new InformationMessage("忍者已召唤!"));
            });

            return "success";
        }


        [CommandLineFunctionality.CommandLineArgumentFunction("change_leader_by_id", "custom")]
        public static string ChangeLeaderById(List<string> strings)
        {
            // Check if Campaign is active
            if (Campaign.Current == null)
            {
                return "Error: Campaign system is not loaded.";
            }

            // Check argument count
            if (strings == null || strings.Count != 2)
            {
                return "Usage: campaign.change_leader_by_id [CurrentLeaderStringId] [NewLeaderStringId]";
            }

            string currentLeaderId = strings[0];
            string newLeaderId = strings[1];

            // Find the heroes by StringId
            Hero currentLeader = Campaign.Current.CampaignObjectManager.Find<Hero>(currentLeaderId);
            Hero newLeader = Campaign.Current.CampaignObjectManager.Find<Hero>(newLeaderId);

            // Validate Heroes
            if (currentLeader == null)
            {
                return "Error: Could not find a hero with StringId: " + currentLeaderId;
            }

            if (newLeader == null)
            {
                return "Error: Could not find a hero with StringId: " + newLeaderId;
            }

            // Validate Clan status
            Clan targetClan = currentLeader.Clan;

            if (targetClan == null)
            {
                return "Error: The hero '" + currentLeader.Name + "' does not belong to any clan.";
            }

            if (targetClan.Leader != currentLeader)
            {
                return "Error: The hero '" + currentLeader.Name + "' is not the leader of the clan '" + targetClan.Name + "'.";
            }

            if (!newLeader.IsAlive)
            {
                return "Error: The new leader candidate is dead.";
            }

            // Optional: Check if new leader is in the same clan (Safety check, though Action might handle it)
            if (newLeader.Clan != targetClan)
            {
                // You can decide to return an error or force move them. 
                // Standard logic implies leader should be in the clan.
                return "Error: The new leader '" + newLeader.Name + "' is not in the same clan (" + targetClan.Name + ").";
            }

            try
            {
                // Apply the action
                ChangeClanLeaderAction.ApplyWithSelectedNewLeader(targetClan, newLeader);
                return "Success: Clan '" + targetClan.Name + "' leader changed from '" + currentLeader.Name + "' to '" + newLeader.Name + "'.";
            }
            catch (System.Exception ex)
            {
                return "Exception occurred: " + ex.Message;
            }
        }
    }



}

