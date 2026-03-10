using ExampleMod.AI;
using ExampleMod.AI.Actions;
using ExampleMod.StoryEngineBag;
using HarmonyLib;
using SandBox.Conversation.MissionLogics;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.MountAndBlade.ViewModelCollection;
using TaleWorlds.ScreenSystem;
using static TaleWorlds.MountAndBlade.Agent;

namespace ExampleMod
{
    public class InteractionMissionView : MissionView
    {
        private InteractionVM _interactVM;
        private GauntletLayer _interact_layer;

        // --- 新增：对话系统变量 ---
        private StoryDialogVM _dialogueVM;
        private GauntletLayer _dialogueLayer;
        private InteractionController _interactionController;


        private NPCInfoVM _npcInfoVM;
        private GauntletLayer _npcInfoLayer;

        // --- 偷窃界面变量 ---
        private StealVM _stealVM;
        private GauntletLayer _stealLayer;


        private int _tickCounter = 0;


        // 缓存变量，用于去重，避免每帧刷新UI
        private Agent _lastFocusedAgent = null;
        private bool _lastAgentWasAlive = false;
        private bool _lastCanSteal = false;


        public MissionScreen thisMissionScreen;

        // 标记是否是我们自己在处理交互，用于通知 Harmony 补丁
        public static bool IsHandlingInteraction { get; private set; } = false;
        //聊天锁，防止其他人也想和玩家说话
        public static bool IsChatting { get; private set; } = false;

        public static InteractionMissionView Instance { get; private set; }

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();
            thisMissionScreen = ScreenManager.TopScreen as MissionScreen;

            //右下角交互UI
            _interactVM = new InteractionVM();            
            _interact_layer = new GauntletLayer(100); 
            _interact_layer.LoadMovie("InteractArea", _interactVM);
            thisMissionScreen.AddLayer(_interact_layer);

            //对话UI
            _dialogueVM = new StoryDialogVM();
            _dialogueLayer = new GauntletLayer(101);
            _dialogueLayer.LoadMovie("DialogChoice", _dialogueVM);
            thisMissionScreen.AddLayer(_dialogueLayer);
            // 初始化控制器
            _interactionController = new InteractionController(_dialogueVM);

            // 订阅关闭事件，处理收尾工作
            _dialogueVM.OnDialogClosed += OnDialogueEnded;

            Instance = this;

        }


        private void ProcessAgentCandidate(Agent agent, Vec3 eyePos, Vec3 lookDir, float maxDistanceSq, ref float bestDot, ref Agent bestAgent)
        {
            // 1. 快速排除
            if (agent == null || agent == Agent.Main || !agent.IsHuman) return;

            // 2. 距离剔除 (Distance Squared)
            float distSq = agent.Position.DistanceSquared(eyePos);
            if (distSq > maxDistanceSq) return;

            // 3. 目标中心点修正 (关键优化)
            // agent.Position 是脚底板。如果尸体躺着，或者你看着人的头，脚底板的角度偏差会很大。
            // 我们取 Position 向上 0.5 - 1.0 米的位置作为“躯干中心”
            Vec3 targetCenter = agent.Position + new Vec3(0, 0, 0.8f);

            // 4. 计算向量与点积
            Vec3 toTarget = targetCenter - eyePos;
            toTarget.Normalize();

            float dot = Vec3.DotProduct(lookDir, toTarget);

            // 5. 擂台法：谁更接近 1.0 (正中心)，谁就胜出
            // 这里不仅跟阈值比，还要跟当前最好的比
            if (dot > bestDot)
            {
                bestDot = dot;
                bestAgent = agent;
            }
        }
        private Agent GetFocusdAgent()
        {
            // 如果玩家自己都死了，就不探测了
            if (Agent.Main == null || !Agent.Main.IsActive()) return null;

            Camera cam = thisMissionScreen.CombatCamera;
            if (cam == null) return null;
            Vec3 rayStart = cam.Position;
            Vec3 rayDir = cam.Direction;
            float maxDistance = 7.0f;
            Vec3 rayEnd = rayStart + rayDir * maxDistance;
            float dist = 0;
            Agent raycastedAgent = Mission.Current.RayCastForClosestAgent(rayStart, rayEnd, out  dist, Agent.Main.Index, 0.1f);
            //去掉IsActive 不然人死了就拿不到了
            if(raycastedAgent != null && raycastedAgent.IsHuman && raycastedAgent.Character != null && raycastedAgent.Character.Name.ToString() != "")
            {
               //  InformationManager.DisplayMessage(new InformationMessage($"射线检测 找到了{dist}米的 {raycastedAgent.Character.Name}"));
                return raycastedAgent;

            }
            // =================================================================
            // 第二阶段：广域模糊搜索 (Cone/DotProduct Search)
            // =================================================================
            // 如果射线没打中，开始从周围的对象里找一个“准星最对得准”的

            float interactDist = 3.0f; // 模糊搜索只搜身边3米
            float maxDistanceSq = interactDist * interactDist;
            float bestDotProduct = 0.85f; // 阈值：约30度角，越接近1越准
            Agent bestAgent = null;

            Vec3 eyePos = cam.Position; // 使用相机位置作为视点
            Vec3 lookDir = cam.Direction;

            // -------------------------------------------------------
            // 来源 A：附近的尸体 (Dead Agents)
            // -------------------------------------------------------
            var corpses = AttackTriggerMissionLogic.Instance.GetDeadAgentsRaw();
            foreach (Agent agent in corpses)
            {
                ProcessAgentCandidate(agent, eyePos, lookDir, maxDistanceSq, ref bestDotProduct, ref bestAgent);
            }

            // -------------------------------------------------------
            // 来源 B：附近的活人 (Nearby Living Agents)
            // -------------------------------------------------------
            // 使用 MBList 以避免 GC (Garbage Collection)
            MBList<Agent> nearbyLiving = new MBList<Agent>();
            Mission.Current.GetNearbyAgents(Agent.Main.Position.AsVec2, interactDist, nearbyLiving);

            foreach (Agent agent in nearbyLiving)
            {
                ProcessAgentCandidate(agent, eyePos, lookDir, maxDistanceSq, ref bestDotProduct, ref bestAgent);
            }
            if (bestAgent != null)
            {
                InformationManager.DisplayMessage(new InformationMessage($"吸附检测 找到了{dist}米的 {bestAgent.Character.Name}"));
            }
            return bestAgent;


        }
        private bool IsMainAgentCrouching()
        {
            if (Agent.Main == null) return false;
            // 检查 Crouch 标志位
            return Agent.Main.CrouchMode;
        }
        private bool IsBehindTarget(Agent target)
        {
            if (Agent.Main == null || target == null) return false;

            // 获取从目标指向玩家的向量
            Vec3 directionToPlayer = (Agent.Main.Position - target.Position).NormalizedCopy();
            // 获取目标的朝向向量
            Vec3 targetLookDirection = target.GetMovementDirection().ToVec3().NormalizedCopy();

            float distance = target.Position.Distance(Agent.Main.Position);

            // 计算点积。如果小于 -0.5 (约60度角)，说明玩家在目标背后盲区
            // Dot Product: 1.0 = 同向, 0 = 垂直, -1.0 = 面对面
            return (Vec3.DotProduct(targetLookDirection, directionToPlayer) < -0.3f) && distance < 2.5f;
        }

        private void HandleInput()
        {
            // 如果没有缓存的目标，直接返回，防止空引用
            if (_lastFocusedAgent == null) return;

            if (TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.F))
            {
                if (_lastAgentWasAlive)
                {
                    if (_lastCanSteal)
                    {
                        TryStealFromAgent(_lastFocusedAgent);
                    }
                    else
                    {
                        StartVanillaConversation(_lastFocusedAgent);
                    }
                }
                else
                {
                    // 尸体直接搜刮
                    LootAgent(_lastFocusedAgent, isStealing: false);
                }
            }
            else if (TaleWorlds.InputSystem.Input.IsKeyReleased(InputKey.G))
            {
                if (_lastAgentWasAlive) _ = StartFreeConversationFlow(_lastFocusedAgent);
            }
            else if (TaleWorlds.InputSystem.Input.IsKeyReleased(InputKey.H))
            {
                OpenNPCInfoBoard(_lastFocusedAgent);
            }
        }

        private void PerformPerformanceHeavyLogic()
        {
            // A. 获取目标
            Agent currentAgent = GetFocusdAgent();

            // B. 排除空目标或玩家自己
            if (currentAgent == null || currentAgent == Mission.Current.MainAgent)
            {
                if (_interactVM.IsVisible)
                {
                    _interactVM.IsVisible = false;
                    IsHandlingInteraction = false;
                    _lastFocusedAgent = null;

                }
                return;
            }

            // C. 计算状态
            bool isAlive = currentAgent.IsActive();
            bool isStealingConditionMet = isAlive && IsMainAgentCrouching() && IsBehindTarget(currentAgent);


            // E. 判断是否需要刷新 UI (对比上一状态)
            bool targetChanged = (currentAgent != _lastFocusedAgent);
            bool lifeStateChanged = (isAlive != _lastAgentWasAlive);
            bool stealStateChanged = (isStealingConditionMet != _lastCanSteal);

            if (targetChanged || lifeStateChanged || stealStateChanged || !_interactVM.IsVisible)
            {
                _interactVM.IsVisible = true;
                IsHandlingInteraction = true;

                var actions = new List<(string, string)>();

                if (isAlive)
                {
                    if (isStealingConditionMet)
                    {
                        actions.Add(("偷窃", "F"));
                    }
                    else
                    {
                        actions.Add(("对话", "F"));
                    }

                    
                     actions.Add(("闲聊", "G"));
                     actions.Add(("探查", "H"));
                    
                }
                else
                {
                    actions.Add(("搜刮", "F"));
                }

                // 只有名字不为空才显示，避免报错
                string name = currentAgent.Name != null ? currentAgent.Name.ToString().Trim() : "未知";
                if(!currentAgent.IsActive())
                {
                    name += "(重伤)";
                }
                _interactVM.UpdateTarget(name, actions);

                // 更新对比缓存
                _lastFocusedAgent = currentAgent;
                _lastAgentWasAlive = isAlive;
                _lastCanSteal = isStealingConditionMet;
            }
        }


        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);


            if (thisMissionScreen == null) return;

            // ----------------- 1. 基础拦截条件 -----------------
            if (Mission.Current.Mode == MissionMode.Conversation || Mission.Current.Mode == MissionMode.Barter)
            {
                if (_interactVM.IsVisible) _interactVM.IsVisible = false;
                return;
            }

            var storyengine = StoryEngineBag.StoryEngine.Instance;
            if (storyengine != null && storyengine.GetIsRunning()) { return; }

            if (_dialogueVM.IsVisible)
            {
                _interactVM.IsVisible = false;
                return;
            }

            _tickCounter++;

            // 只有在第 3 帧时，才去执行射线检测和 UI 刷新
            if (_tickCounter % 3 == 0)
            {
                PerformPerformanceHeavyLogic();
            }

            // ----------------- 3. 高频逻辑：输入监听 (每帧必须执行) -----------------
            // 只有当 UI 显示时，才允许输入
            if (_interactVM.IsVisible)
            {
                HandleInput();
            }

        }
        private void StartVanillaConversation(Agent agent)
        {
            // 获取任务中的对话逻辑控制器
            var conversationLogic = Mission.Current.GetMissionBehavior<MissionConversationLogic>();

            if (conversationLogic != null)
            {
                // 暂时禁用我们的UI，防止重叠
                _interactVM.IsVisible = false;

                // 手动触发对话，绕过 Harmony 的屏蔽（如果你的 Prefix return false 的话，需要注意这里）
                // 通常直接调用 StartConversation 还是会进入原版逻辑
                conversationLogic.StartConversation(agent, true, false);
            }
        }
        private async Task WaitForAgentToSettle(Agent agent, float timeout = 10f)
        {
            float timer = 0f;

            var brain = AgentAIController.GetBrainForAgent(agent);
            if (brain == null)
                return;
            while (timer < timeout)
            {
                // 1. 安全检查
                if (agent == null || !agent.IsActive()) return;

                // 2. 核心判断：如果当前的动作已经是 StayAction，说明前面的 Move/Look 都跑完了
                if (brain.CurrentAction is StayAction)
                {
                    return; // 到位了，退出等待
                }

                // 3. 稍微等待一下再检查 (每 0.1 秒检查一次)
                await Task.Delay(100);
                timer += 0.1f;
            }

            // 超时处理：如果走到这里说明 NPC 卡住了或者路太远
            // 可以强制瞬移，或者直接开始对话，视需求而定
            InformationManager.DisplayMessage(new InformationMessage("NPC 移动超时，强制进入对话。", Colors.Red));

            // 可选：强制瞬移过去，保证对话镜头正常
            // agent.TeleportToPosition(Agent.Main.Position + Agent.Main.LookDirection * 1.5f);
        }
        public async Task StartFreeConversationFlow(Agent agent,bool playerActive = true)
        {
            // 1. 隐藏“按G交互”的提示
            _interactVM.IsVisible = false;

            // 标记正在交互，防止 Harmony 补丁或其他逻辑干扰
            IsHandlingInteraction = true;
            IsChatting = true;
            // 1. 获取主玩家 Agent
            Agent mainAgent = Agent.Main;

            if (mainAgent != null)
            {
                // 【关键代码】强制把主手（右手）的武器收回刀鞘
                // 使用 Instant 表示瞬间完成，不需要播放收刀动作，防止动作打断
                mainAgent.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);

                // 【关键代码】如果有副手武器（比如盾牌），也瞬间收起来
                //mainAgent.TryToSheathWeaponInHand(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.Instant);

                // 【补充】有时候即便收起来了，之前的姿态可能还会残留，可以强制重置一下动作通道（可选，视情况而定）
                // mainAgent.SetActionChannel(0, ActionIndexCache.act_none, true, 0, 0, 1f);
            }




            //禁用主角移动


            if (playerActive)
            {
                //await AgentControlHelper.MoveToActor(agent, Agent.Main);

                AgentAIController.Instance.SendEventToAgent(agent, "ComeHere", Agent.Main);
                await WaitForAgentToSettle(agent);
            }
            Agent.Main.SetMovementDirection(Vec2.Zero);
            if (Agent.Main.Controller == Agent.ControllerType.Player)
            {
                Agent.Main.Controller = Agent.ControllerType.AI;
                AgentControlHelper.FaceToActor(Agent.Main, agent);
            }


            // 检查 NPC 是否还在 (防止移动过程中被杀或消失)
            if (agent == null || !agent.IsActive())
            {
                IsHandlingInteraction = false;
                return;
            }

            // 3. 设置镜头 (这里填入你的自定义镜头逻辑)
            SetupCameraForDialogue(agent);

            // 4. 激活鼠标 (对话时需要鼠标点击选项)
            _dialogueLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);

            // 5. 启动对话控制器 (这会设置 VM.IsVisible = true)
            _interactionController.StartInteraction(agent);

        }

        // 你的自定义镜头逻辑占位符
        private void SetupCameraForDialogue(Agent targetAgent)
        {
            string firstDialog_Far = "2m_Npc_Shoulder_Mid_R";


            SpringArmCameraView.UseCameraTemlate(firstDialog_Far, targetAgent, Agent.Main, Vec3.Zero);

        }
        private void OpenNPCInfoBoard(Agent agent)
        {
            // 防止重复打开
            if (_npcInfoLayer != null) return;

            // 1. 获取数据 (假设你有办法从 Agent 获取 NPCProfile)
            // 这里你需要根据你的 Mod 逻辑，从 Agent 找到对应的 Hero 或自定义数据
            var memory = AllNpcMemoryManager.GetMemoryForAgent(agent);
            if (memory == null) return;

            // 2. 创建 VM，传入关闭回调
            _npcInfoVM = new NPCInfoVM(memory, CloseNPCInfoBoard);

            // 3. 创建 Layer 并加载 Movie
            _npcInfoLayer = new GauntletLayer(200); // 这里的 200 是层级优先级，需比普通 HUD 高
            _npcInfoLayer.LoadMovie("NPCInfoBoard", _npcInfoVM);

            // 4. 设置输入限制 (让鼠标可见，并不允许移动视角)
            _npcInfoLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);

            // 5. 添加到屏幕
            thisMissionScreen.AddLayer(_npcInfoLayer);

            // 标记状态（可选，配合 Harmony 暂停游戏或禁止其他交互）
            IsHandlingInteraction = true;
        }
        private void CloseNPCInfoBoard()
        {
            if (_npcInfoLayer != null)
            {
                thisMissionScreen.RemoveLayer(_npcInfoLayer);
                _npcInfoLayer = null;
                _npcInfoVM = null;
                IsHandlingInteraction = false;
            }
        }

        private void OpenStealInterface(Agent targetAgent)
        {
            // 1. 如果已经打开了，先不要重开
            if (_stealLayer != null) return;

            // 2. 初始化 VM
            _stealVM = new StealVM(targetAgent, () => CloseStealInterface());

            // 3. 创建 Layer
            _stealLayer = new GauntletLayer(201); // 优先级比对话(101)更高，覆盖在上面
            _stealLayer.LoadMovie("Steal", _stealVM); // 

            // 4. 设置输入限制 (释放鼠标，冻结镜头，或者保持镜头可动但显示鼠标)
            _stealLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);

            // 5. 添加到屏幕
            thisMissionScreen.AddLayer(_stealLayer);

            // 6. 标记状态，防止其他交互干扰
            IsHandlingInteraction = true;

            // 7. 隐藏原本的“按F交互”小黑条
            _interactVM.IsVisible = false;

        }

        // 关闭偷窃界面
        private void CloseStealInterface()
        {
            if (_stealLayer != null)
            {
                // 1. 移除 Layer
                thisMissionScreen.RemoveLayer(_stealLayer);
                _stealLayer.InputRestrictions.ResetInputRestrictions();

                // 2. 清理变量
                _stealLayer = null;
                _stealVM = null;

                // 3. 恢复状态
                IsHandlingInteraction = false;
            }
        }
        public override void OnMissionScreenFinalize()
        {
            base.OnMissionScreenFinalize();
            if (_interact_layer != null)
            {
                thisMissionScreen.RemoveLayer(_interact_layer);
                _interact_layer = null;
            }

            _interactVM = null;
            if (_dialogueLayer != null) { thisMissionScreen.RemoveLayer(_dialogueLayer); _dialogueLayer = null; }            
            _dialogueVM = null;

            if (_npcInfoLayer != null)
            {
                thisMissionScreen.RemoveLayer(_npcInfoLayer);
                _npcInfoLayer = null;
            }
            _npcInfoVM = null;

            if(_stealLayer != null)
            {
                thisMissionScreen.RemoveLayer(_stealLayer);
                _stealLayer = null;
            }
            _stealVM = null;

            //清除场景里临时Agent的临时记忆
            AllNpcMemoryManager.ClearTemporaryMemories();
        }


        private async void OnDialogueEnded()
        {
            // 1. 释放鼠标，恢复游戏控制
            _dialogueLayer.InputRestrictions.ResetInputRestrictions();

                   

            // 3. 恢复镜头 (如果之前设置了 CustomCamera)
            thisMissionScreen.CustomCamera = null;
            IsHandlingInteraction = false;
            IsChatting = false;

            //恢复主角移动
            if (Agent.Main != null && Agent.Main.IsActive())
            {
                // 切回玩家控制
                Agent.Main.Controller = Agent.ControllerType.Player;
            }
            SocialEvent evt = await _interactionController.GenerateEventAsync();
            NewsSpreadSystem.Instance.BroadcastEvent(evt);

        }

        private void TryStealFromAgent(Agent target)
        {

         
                InformationManager.DisplayMessage(new InformationMessage("你屏住呼吸，悄悄伸出了手...", Colors.Green));

                // 【核心修改】：打开你的 Gauntlet UI
                OpenStealInterface(target);
           

           
        }

        private List<Agent> _lootedCorpses = new List<Agent>(); // 用于记录已经搜刮过的尸体，避免重复搜刮
        private void LootAgent(Agent targetAgent, bool isStealing)
        {
            Hero targetHero = (targetAgent.Character as CharacterObject)?.HeroObject;
            // 1. 去重检查 (只针对尸体，活人可以反复偷，或者你可以加冷却)
            if (!isStealing && _lootedCorpses.Contains(targetAgent))
            {
                InformationManager.DisplayMessage(new InformationMessage($"{targetAgent.Name} 已经被搜刮过了。", Colors.Red));
                return;
            }

            // --- 步骤一：计算产出 ---

            // 活人的钱通常在家族里，身上一般没现金，这里简单处理：偷活人只偷装备，或者是偷少量零钱
            int lootedGold = 0;
            CharacterObject character = targetAgent.Character as CharacterObject;

            if (character != null)
            {
                if (isStealing)
                {
                    // 偷窃获得的金钱较少
                    lootedGold = MBRandom.RandomInt(1, 20);
                }
                else
                {
                    // 搜刮尸体逻辑
                    lootedGold = character.IsHero ? (100 + character.Level * 50) : (character.Level * 5);
                }
            }

            // B. 构建物品列表 (ItemRoster)
            ItemRoster lootRoster = new ItemRoster();

            // 活人使用的是 SpawnEquipment 或者 Equipment，尸体也是。
            // 注意：偷活人时，我们是在生成副本。如果你拿走了，NPC身上的视觉模型不会消失（除非写非常复杂的逻辑去剥离装备）
            // 这里我们做“顺手牵羊”：你拿到了装备，但NPC还没发现自己丢了东西。
            var equipmentToInspect = targetAgent.SpawnEquipment;
            string itemsName = "";
            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumEquipmentSetSlots; i++)
            {
                EquipmentElement element = equipmentToInspect[i];
                if (!element.IsEmpty && element.Item != null)
                {
                    lootRoster.AddToCounts(element.Item, 1);
                    itemsName += element.Item.Name.ToString() + " ";
                }
            }
            string partyItems = "";
            if(targetAgent!= null)
            {
                MobileParty party = targetHero.PartyBelongedTo;
                if(party!=null)
                {
                    var rst = party.ItemRoster;
                    if(rst!=null && rst.Count > 0)
                    {
                        partyItems = $"似乎{targetAgent.Name}还有{rst.Count}件东西在队伍里没带身上";
                    }
                }
            }
            // 空空如也检查
            if (lootedGold == 0 && lootRoster.IsEmpty())
            {
                InformationManager.DisplayMessage(new InformationMessage($"{targetAgent.Name} 身上啥也没有。", Colors.Gray));
                return;
            }



            // --- 步骤二：构建 Inquiry (复用原有逻辑) ---
            string actionName = isStealing ? "偷窃" : "搜刮";
            string titleText = $"{actionName} {targetAgent.Name}";
            string contentText = $"你在 {targetAgent.Name} 身上发现了些东西:{itemsName} \n{partyItems}";

            InformationManager.ShowInquiry(new InquiryData(
                titleText,
                contentText,
                true,
                true,
                "全部拿走",
                "自己挑选",
                () =>
                {
                    // 全部拿走回调
                    if (lootedGold > 0)
                    {
                        GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, lootedGold, disableNotification: true);
                        InformationManager.DisplayMessage(new InformationMessage($"获得了 {lootedGold} 两钱。", Colors.Yellow));
                    }
                    if (!lootRoster.IsEmpty())
                    {
                        MobileParty.MainParty.ItemRoster.Add(lootRoster);
                        InformationManager.DisplayMessage(new InformationMessage($"获得了 {lootRoster.Count} 件物品。", Colors.Green));
                    }
                    if (!isStealing) _lootedCorpses.Add(targetAgent); // 只有尸体才标记为彻底搜空
                    StealManager.StripAgentEquipment(targetAgent, true, true);
                },
                () =>
                {
                    // 自己挑选回调
                    if (lootedGold > 0)
                    {
                        GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, lootedGold, disableNotification: false);
                    }

                    if (!lootRoster.IsEmpty())
                    {
                        var rosterDictionary = new Dictionary<PartyBase, ItemRoster>();
                        rosterDictionary.Add(PartyBase.MainParty, lootRoster);
                        InventoryManager.OpenScreenAsLoot(rosterDictionary);

                        if (!isStealing) _lootedCorpses.Add(targetAgent);
                    }
                },
                "", 0f
            ), true);
        }
        

    }
    

    [HarmonyPatch(typeof(MissionConversationLogic), "OnAgentInteraction")]
    public class AgentInteractPatch
    {
        // Prefix 返回 bool：
        // 返回 true  => 继续执行原版代码（进入原版对话）
        // 返回 false => 阻止执行原版代码（拦截成功，原版对话不会触发）

        [HarmonyPrefix]
        public static bool Prefix(Agent userAgent, Agent agent)
        {
            // userAgent = 发起交互的人 (玩家)
            // agent     = 被交互的人 (NPC)

            try
            {
                // 1. 基础校验
                if (userAgent != Agent.Main || agent == null)
                {
                    return true; // 放行
                }

                // 2. 你的判断逻辑
                // 比如根据 agent.Name 或者 agent.Character 等判断
                    // 🚀 在这里写你的 UI 打开代码
                    //InformationManager.DisplayMessage(new InformationMessage("拦截成功！打开自定义UI..."));
                    return false;
                
            }
            catch (Exception ex)
            {
                // 防止你的代码报错导致游戏崩溃
                InformationManager.DisplayMessage(new InformationMessage("Patch Error: " + ex.Message));
            }

            // 默认放行
            return true;
        }
    

        
    }

    [HarmonyPatch(typeof(AgentInteractionInterfaceVM), "SetAgent")]
    public static class ChangeInteractionTextPatch
    {
        public static void Postfix(AgentInteractionInterfaceVM __instance, Agent focusedAgent)
        {
            // 1. 此时原版代码已经跑完了，__instance.SecondaryInteractionMessage 已经是 "按 F 交谈"

            // 2. 检查是不是我们要改的人
            if (focusedAgent != null)
            {
                // 3. 我们不管之前的变量设置了什么，直接覆盖最终结果
                // 这里我们甚至手动保留了 "按 F" 这个部分，只改后面的字

                // 获取 "按 F" 的部分 (原版逻辑里它是通过 str_ui_agent_interaction_use 获取的)
                string keyText = GameTexts.FindText("str_ui_agent_interaction_use").ToString();

                // 把按F交互隐藏
                __instance.SecondaryInteractionMessage = "";
                //把交互对象的名字隐藏
                __instance.PrimaryInteractionMessage = "";
            }
        }
    }

}
