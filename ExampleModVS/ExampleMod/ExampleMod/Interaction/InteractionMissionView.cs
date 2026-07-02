using LivingWorldNpcs.Story;
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
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.MountAndBlade.ViewModelCollection;
using TaleWorlds.ObjectSystem;
using TaleWorlds.ScreenSystem;
using static TaleWorlds.MountAndBlade.Agent;

namespace LivingWorldNpcs
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
        private bool _lastIsBehind = false;
        private bool _lastWasCrouching = false;
        private bool _lastWasAnimal = false;

        // 偷动物并发守卫：防止动画期间重复触发
        private bool _isStealingAnimal = false;

        // 场景动物同步：首帧只执行一次
        private bool _animalSyncDone = false;

        // 击晕追踪：记录被玩家从背后击晕的Agent
        private HashSet<Agent> _knockedOutAgents = new HashSet<Agent>();


        public MissionScreen thisMissionScreen;

        // 标记是否是我们自己在处理交互，用于通知 Harmony 补丁
        public static bool IsHandlingInteraction { get; private set; } = false;
        //聊天锁，防止其他人也想和玩家说话
        public static bool IsChatting { get; private set; } = false;

        // SightBubbleConsumer：已订阅 NpcSightSystem 事件
        private bool _sightBubbleSubscribed = false;

        // Map encounter dialog auto-trigger gate — 防止 OnMissionTick 重复拉起
        private bool _encounterDialogStarted = false;
        private int _encounterPartnerSearchFrames = 0;
        private const int MaxEncounterPartnerSearchFrames = 300; // ~5s at 60fps

        public static InteractionMissionView Instance { get; private set; }

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();
            thisMissionScreen = ScreenManager.TopScreen as MissionScreen;

            //右下角交互UI
            _interactVM = new InteractionVM();            
            _interact_layer = V.NewLayer(100); 
            _interact_layer.LoadMovie("InteractArea", _interactVM);
            thisMissionScreen.AddLayer(_interact_layer);

            //对话UI
            _dialogueVM = new StoryDialogVM();
            _dialogueLayer = V.NewLayer(101);
            _dialogueLayer.LoadMovie("DialogChoice", _dialogueVM);
            thisMissionScreen.AddLayer(_dialogueLayer);
            // 初始化控制器
            _interactionController = new InteractionController(_dialogueVM);

            // 订阅关闭事件，处理收尾工作
            _dialogueVM.OnDialogClosed += OnDialogueEnded;

            Instance = this;

            // SightBubbleConsumer：订阅 NpcSightSystem，NPC 看到玩家时概率冒泡
            SubscribeSightBubble();

        }

        private void SubscribeSightBubble()
        {
            if (_sightBubbleSubscribed) return;
            var sight = Mission.Current?.GetMissionBehavior<NpcSightSystem>();
            if (sight == null) return;
            _sightBubbleSubscribed = true;
            sight.OnAgentStartObserving += OnNpcStartObservingPlayer;
        }

        private void OnNpcStartObservingPlayer(Agent observer, Agent target)
        {
            // 只处理 NPC 看到玩家
            if (target != Agent.Main) return;
            if (observer == null || !observer.IsActive()) return;
            if (IsChatting) return; // 正在对话中不冒泡

            // 查据点荣誉
            int honor = 0;
            if (Hero.MainHero.CurrentSettlement != null)
                honor = SettlementHonorStore.Get(Hero.MainHero.CurrentSettlement);

            // 概率 = min(0.10 + honor * 0.01, 0.25)
            float prob = MathF.Clamp(0.10f + honor * 0.01f, 0.02f, 0.25f);
            float roll = MBRandom.RandomFloat;
            bool hit = roll < prob;

            if (!hit) return;

            // 构建因素
            var factors = new DialogueFactors
            {
                Honor = honor >= 5 ? HonorLevel.High : (honor <= -5 ? HonorLevel.Low : HonorLevel.Neutral),
                Gender = (observer.Character != null && observer.Character.IsFemale) ? NpcGender.Female : NpcGender.Male,
                Identity = NpcIdentity.Civilian
            };

            string emotion;
            string line = DialogueTemplateHelper.Get("BubbleGreet", factors, out emotion, null, observer);
            if (!string.IsNullOrEmpty(line))
                BubbleSayMissionView.AgentBubbleSay(observer, line);
        }


        // ── 动物 Agent 识别 ──
        // 村庄场景中的牲畜（羊/牛/猪/鹅/鸡）是 IsHuman=false 的 Agent，
        // Monster.StringId 区分种类，Character 为 null
        internal static readonly HashSet<string> AnimalMonsters = new HashSet<string>
        {
            "sheep", "cow", "hog", "goose", "chicken"
        };

        private static bool IsAnimalAgent(Agent agent)
        {
            if (agent == null || agent.IsHuman) return false;
            string monster = agent.Monster?.StringId;
            return monster != null && AnimalMonsters.Contains(monster);
        }

        // ── 动物 Monster.StringId → 牲畜 ItemObject 静态缓存 ──
        // 惰性初始化，避免每次偷动物都遍历全部物品（铁律 5 两轮策略）
        private static readonly Dictionary<string, ItemObject> _monsterToLivestockItem = new Dictionary<string, ItemObject>();

        internal static ItemObject GetLivestockItemForAnimal(string monsterId, string animalName)
        {
            if (string.IsNullOrEmpty(monsterId)) return null;

            // 缓存命中
            if (_monsterToLivestockItem.TryGetValue(monsterId, out ItemObject cached))
                return cached;

            ItemObject item = null;

            // 第一轮：精确 ID 匹配
            item = MBObjectManager.Instance.GetObject<ItemObject>(monsterId);

            // 第二轮：遍历所有 Animal 类型物品，按 monster ID 模糊匹配
            if (item == null)
            {
                item = MBObjectManager.Instance.GetObject<ItemObject>(i =>
                    i.Type == ItemObject.ItemTypeEnum.Animal &&
                    i.Name?.ToString().IndexOf(monsterId, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            // 兜底：按动物显示名匹配（处理 goose→Goose 大小写差异）
            if (item == null && !string.IsNullOrEmpty(animalName))
            {
                item = MBObjectManager.Instance.GetObject<ItemObject>(i =>
                    i.Type == ItemObject.ItemTypeEnum.Animal &&
                    i.Name?.ToString().IndexOf(animalName, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (item != null)
                _monsterToLivestockItem[monsterId] = item;
            return item;
        }

        public void ProcessAgentCandidate(Agent agent, Vec3 eyePos, Vec3 lookDir, float maxDistanceSq, float minDot, ref float bestDot, ref Agent bestAgent)
        {
            // 1. 快速排除
            if (agent == null || agent == Agent.Main) return;
            if (!agent.IsHuman && !IsAnimalAgent(agent)) return;

            // 2. 视野缓存预检查：不在玩家视野内直接跳过（NpcSightSystem ~1s 更新一次）
            //    注意：NpcSightSystem.TickTrackedTarget 过滤了非人类 Agent，导致
            //    IsPlayerSeeing 对动物永远返回 false。动物只依赖后续距离+点积判定。
            var sight = NpcSightSystem.Instance;
            if (sight != null && !sight.IsPlayerSeeing(agent))
            {
                if (!IsAnimalAgent(agent)) return; // 人类视野不够→跳过
                // 动物：跳过视野预检，继续进入距离+点积判定
            }

            // 3. 距离剔除 (Distance Squared)
            float distSq = agent.Position.DistanceSquared(eyePos);
            if (distSq > maxDistanceSq) return;

            // 3. 目标中心点修正 (关键优化)
            // agent.Position 是脚底板。如果尸体躺着，或者你看着人的头，脚底板的角度偏差会很大。
            // 我们取 Position 向上 0.5 - 1.0 米的位置作为"躯干中心"
            Vec3 targetCenter = agent.Position + new Vec3(0, 0, 0.8f);

            // 4. 计算向量与点积
            Vec3 toTarget = targetCenter - eyePos;
            toTarget.Normalize();

            float dot = Vec3.DotProduct(lookDir, toTarget);

            // 5. 必须先过本类目标的最小角度阈值（活人严、尸体松）
            if (dot < minDot) return;

            // 6. 擂台法：谁更接近 1.0 (正中心)，谁就胜出
            if (dot > bestDot)
            {
                bestDot = dot;
                bestAgent = agent;
            }
        }
        public Agent GetFocusdAgent()
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
            Agent raycastedAgent = V.RayCastForClosestAgent(rayStart, rayEnd, Agent.Main.Index, out dist, 0.1f);
            //去掉IsActive 不然人死了就拿不到了
            // 人类：需要 Character 有名字；动物：IsHuman=false，靠 Monster 识别
            if (raycastedAgent != null)
            {
                bool isHumanTarget = raycastedAgent.IsHuman
                    && raycastedAgent.Character != null
                    && !string.IsNullOrWhiteSpace(raycastedAgent.Character.Name?.ToString());
                bool isAnimalTarget = IsAnimalAgent(raycastedAgent);

                if (isHumanTarget || isAnimalTarget)
                {
                    return raycastedAgent;
                }
            }
            // =================================================================
            // 第二阶段：广域模糊搜索 (Cone/DotProduct Search)
            // =================================================================
            // 如果射线没打中，开始从周围的对象里找一个"准星最对得准"的

            float interactDist = 4.0f; // 模糊搜索只搜身边4米
            float maxDistanceSq = interactDist * interactDist;
            // 活人要求准星较准(约31°)；尸体躺在地上、低头去看角度偏差大，放宽到约53°，否则脚边的尸体永远对不准
            const float livingMinDot = 0.85f;
            const float corpseMinDot = 0.3f;
            float bestDotProduct = -1f; // 擂台初值给最低，让候选各自过阈值后再比谁更正
            Agent bestAgent = null;

            Vec3 eyePos = cam.Position; // 使用相机位置作为视点
            Vec3 lookDir = cam.Direction;

            // -------------------------------------------------------
            // 来源 A：附近的尸体 (Dead Agents)
            // -------------------------------------------------------
            var corpses = AttackTriggerMissionLogic.Instance.GetDeadAgentsRaw();
            foreach (Agent agent in corpses)
            {
                ProcessAgentCandidate(agent, eyePos, lookDir, maxDistanceSq, corpseMinDot, ref bestDotProduct, ref bestAgent);
            }

            // -------------------------------------------------------
            // 来源 B：附近的活人 (Nearby Living Agents)
            // -------------------------------------------------------
            // 使用 MBList 以避免 GC (Garbage Collection)
            MBList<Agent> nearbyLiving = new MBList<Agent>();
            Mission.Current.GetNearbyAgents(Agent.Main.Position.AsVec2, interactDist, nearbyLiving);

            foreach (Agent agent in nearbyLiving)
            {
                ProcessAgentCandidate(agent, eyePos, lookDir, maxDistanceSq, livingMinDot, ref bestDotProduct, ref bestAgent);
            }
            if (bestAgent != null)
            {
                //InformationManager.DisplayMessage(new InformationMessage($"吸附检测 找到了{dist}米的 {bestAgent.Character.Name}"));
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
                // 动物：活的偷，死的搜刮
                if (_lastWasAnimal)
                {
                    if (_lastAgentWasAlive)
                        TryStealAnimal(_lastFocusedAgent);
                    else
                        LootAgent(_lastFocusedAgent, isStealing: false);
                }
                else if (_lastAgentWasAlive)
                {
                    if (_lastIsBehind)
                    {
                        // 蹲伏=偷窃，站立=击晕
                        if (IsMainAgentCrouching())
                        {
                            TryStealFromAgent(_lastFocusedAgent);
                        }
                        else
                        {
                            TryKnockoutAgent(_lastFocusedAgent);
                        }
                    }
                    else
                    {
                        StartVanillaConversation(_lastFocusedAgent);
                    }
                }
                else
                {
                    // 尸体/昏迷直接搜刮
                    LootAgent(_lastFocusedAgent, isStealing: false);
                }
            }
            else if (TaleWorlds.InputSystem.Input.IsKeyReleased(InputKey.G))
            {
                if (_lastAgentWasAlive)
                {
                    // 无 LLM 也能进：菜单里的对抗意图走 C# 单次检定，闲聊走话题菜单
                    _ = StartFreeConversationFlow(_lastFocusedAgent);
                }
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
            bool isAnimal = IsAnimalAgent(currentAgent);
            bool isAlive = currentAgent.IsActive();
            bool isKnockedOut = _knockedOutAgents.Contains(currentAgent);

            // 已被击晕的Agent视为失去行动能力（引擎可能未立即转为Unconscious时兜底）
            if (isKnockedOut)
            {
                isAlive = false;
            }

            bool isBehind = !isAnimal && isAlive && IsBehindTarget(currentAgent);
            bool isCrouching = !isAnimal && IsMainAgentCrouching();


            // E. 判断是否需要刷新 UI (对比上一状态)
            bool targetChanged = (currentAgent != _lastFocusedAgent);
            bool lifeStateChanged = (isAlive != _lastAgentWasAlive);
            bool behindStateChanged = (isBehind != _lastIsBehind);
            bool crouchStateChanged = (isCrouching != _lastWasCrouching);
            bool animalStateChanged = (isAnimal != _lastWasAnimal);

            if (targetChanged || lifeStateChanged || behindStateChanged || crouchStateChanged || animalStateChanged || !_interactVM.IsVisible)
            {
                _interactVM.IsVisible = true;
                IsHandlingInteraction = true;

                var actions = new List<(string, string)>();

                if (isAnimal)
                {
                    // 动物：活的可偷，死的搜刮
                    if (isAlive)
                    {
                        actions.Add(("偷", "F"));
                    }
                    else
                    {
                        actions.Add(("搜刮", "F"));
                    }
                }
                else if (isAlive)
                {
                    if (isBehind)
                    {
                        // 蹲伏=偷窃，站立=击晕
                        if (isCrouching)
                        {
                            actions.Add(("偷窃", "F"));
                        }
                        else
                        {
                            actions.Add(("击晕", "F"));
                        }
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
                string name;
                if (isAnimal)
                {
                    // 动物：用 agent.Name（"鹅"/"羊" 等），没有 Character
                    name = !string.IsNullOrWhiteSpace(currentAgent.Name) ? currentAgent.Name.Trim() : "动物";
                }
                else
                {
                    name = currentAgent.Name != null ? currentAgent.Name.ToString().Trim() : "未知";
                }
                if (!currentAgent.IsActive())
                {
                    name += isAnimal ? "(死亡)" : (isKnockedOut ? "(昏迷)" : "(重伤)");
                }
                _interactVM.UpdateTarget(name, actions);

                // 更新对比缓存
                _lastFocusedAgent = currentAgent;
                _lastAgentWasAlive = isAlive;
                _lastIsBehind = isBehind;
                _lastWasCrouching = isCrouching;
                _lastWasAnimal = isAnimal;
            }
        }


        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            // ── 大世界遭遇对话：自动触发自定义对话 ──
            if (MapEncounterDialogState.Active && !_encounterDialogStarted)
            {
                _encounterPartnerSearchFrames++;
                if (_encounterPartnerSearchFrames > MaxEncounterPartnerSearchFrames)
                {
                    DebugLogger.Log("[MapConv] Partner agent not found within timeout, ending mission");
                    MapEncounterDialogState.Clear();
                    Mission.Current?.EndMission();
                    return;
                }

                Agent partnerAgent = null;
                CharacterObject partnerChar = MapEncounterDialogState.Partner;
                if (partnerChar != null && Mission.Current != null)
                {
                    foreach (Agent a in Mission.Current.Agents)
                    {
                        if (a.Character == partnerChar && a.IsActive())
                        {
                            partnerAgent = a;
                            break;
                        }
                    }
                }

                if (partnerAgent != null)
                {
                    _encounterDialogStarted = true;
                    DebugLogger.Log($"[MapConv] Partner agent found: {partnerAgent.Name}, starting custom dialog flow");
                    _ = StartFreeConversationFlow(partnerAgent);
                }
                // 未找到则等下一帧
                return;
            }

            // ── 场景动物同步：首帧按 ItemRoster 裁剪多余动物（铁律 5 动态物品查找 + 村庄库存真相源）──
            if (!_animalSyncDone)
            {
                _animalSyncDone = true;
                SyncSceneAnimalsWithInventory();
            }


            // ----------------- 0. 库存界面关闭后的搜刮收尾 -----------------
            if (_pendingLootCorpse != null)
            {
                ProcessPendingLoot();
                return;
            }

            if (thisMissionScreen == null) return;

            // ----------------- 1. 基础拦截条件 -----------------
            if (Mission.Current.Mode == MissionMode.Conversation || Mission.Current.Mode == MissionMode.Barter)
            {
                if (_interactVM.IsVisible) _interactVM.IsVisible = false;
                return;
            }

            var storyengine = Story.StoryEngine.Instance;
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
            // 1. 隐藏"按G交互"的提示
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
            }

            if (playerActive)
            {
                await PrepareAgentForConversation(agent);
            }
            Agent.Main.SetMovementDirection(Vec2.Zero);
            if (V.IsAgentPlayer(Agent.Main))
            {
                V.SetAgentAI(Agent.Main);
                AgentControlHelper.FaceToActor(Agent.Main, agent);
            }
            Agent.Main.SetLookAgent(agent);    // 玩家持续注视 NPC，防止 AI 控头乱转

            // 检查 NPC 是否还在 (防止移动过程中被杀或消失)
            if (agent == null || !agent.IsActive())
            {
                IsHandlingInteraction = false;
                return;
            }

            // 3. 同帧修正 NPC 到精确位置 + 切镜头
            //    沿玩家→NPC 连线方向放在 2m 外，玩家随后转身面对 NPC，
            //    切镜头同一帧完成，跳变不可见。
            Vec3 toNpc = agent.Position - Agent.Main.Position;
            toNpc.z = 0;
            toNpc.Normalize();
            Vec3 idealPos = Agent.Main.Position + toNpc * 2.0f;
            agent.TeleportToPosition(idealPos);
            agent.SetMovementDirection(-toNpc.AsVec2);
            agent.SetLookAgent(Agent.Main);
            AgentControlHelper.MoveEndAndInteractPrepare(agent, idealPos);

            // ── 大世界遭遇对话：瞬移后 spawn 护卫（位置正确）──
            if (MapEncounterDialogState.Active)
            {
                SpawnEncounterBodyguards(agent);
                LogAllAgentPositions();
                LogCurrentCamera("EncounterEnter");
            }

            SetupCameraForDialogue(agent);

            // 4. 激活鼠标 (对话时需要鼠标点击选项)
            _dialogueLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);

            // 5. 启动对话控制器 (这会设置 VM.IsVisible = true)
            _interactionController.StartInteraction(agent);
        }

        /// <summary>
        /// 地图遭遇对话专用：跳过所有走位/瞬移/镜头逻辑，直接进对话。
        /// 前提：ConversationMissionLogic.AfterStart() 已把双方在对话场景里摆好位。
        /// </summary>
        private void StartEncounterConversationFlow(Agent agent)
        {
            _interactVM.IsVisible = false;
            IsHandlingInteraction = true;
            IsChatting = true;

            // 收刀
            Agent mainAgent = Agent.Main;
            if (mainAgent != null)
            {
                mainAgent.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);
            }

            // 激活鼠标
            _dialogueLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);

            // 直接启动对话控制器（对话场景已摆好位，无需走位/瞬移/镜头）
            _interactionController.StartInteraction(agent);
        }

        /// <summary>提取自 StartFreeConversationFlow 的 NPC 走位准备逻辑</summary>
        private async Task PrepareAgentForConversation(Agent agent)
        {
            bool usingObj = agent.IsUsingGameObject;
            bool crouching = agent.CrouchMode;
            string pose = AgentControlHelper.GetPose(agent) ?? "";
            bool isDefaultPose = string.IsNullOrEmpty(pose)
                || pose == "act_none"
                || (pose.StartsWith("act_stand_") && !pose.StartsWith("act_stand_up_"))
                || (pose.StartsWith("act_idle_") && !pose.StartsWith("act_idle_to_") && !pose.StartsWith("act_idle_from_"))
                || pose.StartsWith("act_conversation_");    // 对话场景预设动画，等同于自然站立
            bool isStandingNaturally = !usingObj && !crouching && isDefaultPose;
            InformationManager.DisplayMessage(new InformationMessage(
                $"[闲聊快速路径] {agent.Name}: obj={usingObj} crouch={crouching} pose=\"{pose}\" isDefault={isDefaultPose} → 快速={(isStandingNaturally?"YES":"NO")}",
                isStandingNaturally ? Colors.Green : Colors.Yellow));
            //强制走comehere流程
            isStandingNaturally = false; 
            if (isStandingNaturally)
            {
                var brain = AgentAIController.GetBrainForAgent(agent);
                if (brain != null)
                {
                    brain.InteractedAgent = Agent.Main;
                    brain.ClearAllActions();
                    // 自然站立也要暂停原版 AI — 防止 NPC 在对话期间被 AgentNavigator 带走
                    // SuspendVanillaAI 内部幂等，重复调用安全
                    AgentControlHelper.SuspendVanillaAI(agent);
                }
            }
            else
            {
                AgentAIController.Instance.SendEventToAgent(agent, "ComeHere", Agent.Main);
                await WaitForAgentToSettle(agent);
            }
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
            _npcInfoLayer = V.NewLayer(200); // 这里的 200 是层级优先级，需比普通 HUD 高
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
            _stealLayer = V.NewLayer(201); // 优先级比对话(101)更高，覆盖在上面
            _stealLayer.LoadMovie("Steal", _stealVM); // 

            // 4. 设置输入限制 (释放鼠标，冻结镜头，或者保持镜头可动但显示鼠标)
            _stealLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);

            // 5. 添加到屏幕
            thisMissionScreen.AddLayer(_stealLayer);

            // 6. 标记状态，防止其他交互干扰
            IsHandlingInteraction = true;

            // 7. 隐藏原本的"按F交互"小黑条
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

            // ── 大世界遭遇对话安全网：防玩家 ESC 直接退 mission 时标志泄漏 ──
            if (MapEncounterDialogState.Active)
            {
                DebugLogger.Log("[MapConv] Finalize with Active=true — clearing state (ESC exit?)");
                MapEncounterDialogState.Clear();
            }

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

            // 清除击晕记录
            _knockedOutAgents?.Clear();

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
                V.SetAgentPlayer(Agent.Main);
            }
            SocialEvent evt = null;
            if (Settings.Instance.IsLLMReady)
                evt = await _interactionController.GenerateEventAsync();
            if (evt != null)
                NewsSpreadSystem.Instance.BroadcastEvent(evt);

            // ── 大世界遭遇对话收尾：结束 encounter + 关 mission 回大地图 ──
            if (MapEncounterDialogState.Active)
            {
                try
                {
                    Helpers.MapEventHelper.OnConversationEnd();
                    DebugLogger.Log($"[MapConv] end: enc={PlayerEncounter.Current != null} leave={PlayerEncounter.LeaveEncounter}");
                }
                catch (Exception ex) { DebugLogger.Log($"[MapConv] teardown error: {ex}"); }
                finally { MapEncounterDialogState.Clear(); }
                Mission.Current?.EndMission();
            }

        }

        /// <summary>
        /// 场景进入时按 VillageAnimalTracker 持久化记录裁剪被偷过的动物。
        /// 只移除该村庄有偷窃记录的类型，不会误删装饰性动物。
        /// 自然恢复由 MyBehavior.DailyTick → VillageAnimalTracker.DecayDaily 驱动。
        /// </summary>
        private void SyncSceneAnimalsWithInventory()
        {
            Settlement settlement = Settlement.CurrentSettlement;
            if (settlement == null || !settlement.IsVillage) return;
            if (Mission.Current == null) return;

            string settlementId = settlement.StringId;
            if (string.IsNullOrEmpty(settlementId)) return;

            // ── 第一步：收集数据 + 缓存自然数 ──
            // 场景动物（按 monsterId 分组）
            var sceneAnimalsByMonster = new Dictionary<string, List<Agent>>();
            foreach (Agent agent in Mission.Current.Agents)
            {
                if (!IsAnimalAgent(agent) || !agent.IsActive()) continue;
                string monsterId = agent.Monster?.StringId;
                if (string.IsNullOrEmpty(monsterId)) continue;

                if (!sceneAnimalsByMonster.ContainsKey(monsterId))
                    sceneAnimalsByMonster[monsterId] = new List<Agent>();
                sceneAnimalsByMonster[monsterId].Add(agent);
            }

            // 缓存自然生成数（第一次进场景时记录，后续不变）
            foreach (var kvp in sceneAnimalsByMonster)
                VillageAnimalTracker.SetNaturalCount(settlementId, kvp.Key, kvp.Value.Count);

            // ItemRoster 中的牲畜物品
            var rosterAnimals = new Dictionary<string, int>(); // monsterId → count
            foreach (var monsterId in AnimalMonsters)
            {
                ItemObject item = GetLivestockItemForAnimal(monsterId, null);
                if (item == null) continue;
                int count = settlement.ItemRoster.GetItemNumber(item);
                if (count > 0)
                    rosterAnimals[monsterId] = count;
            }

            // ── 第二步：打印对比日志 ──
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== [AnimalSync] {settlement.Name} ({settlementId}) ===");
            sb.AppendLine($"  {"Type",-10} {"Scene",-8} {"Roster",-8} {"Stolen",-8} Action");
            sb.AppendLine($"  {"----",-10} {"-----",-8} {"------",-8} {"------",-8} ------");

            foreach (var monsterId in AnimalMonsters)
            {
                int sceneCount = sceneAnimalsByMonster.TryGetValue(monsterId, out var list) ? list.Count : 0;
                int rosterCount = rosterAnimals.TryGetValue(monsterId, out int rc) ? rc : 0;
                int stolenCount = VillageAnimalTracker.GetStolenCount(settlementId, monsterId);
                string action = "";
                if (stolenCount > 0 && sceneCount > 0)
                {
                    int toRemove = Math.Min(stolenCount, sceneCount);
                    action = $"remove {toRemove}";
                }
                else if (sceneCount > 0 && rosterCount == 0)
                {
                    action = "decorative (not in roster)";
                }
                else if (rosterCount > 0 && sceneCount == 0)
                {
                    action = "roster-only (no scene spawn)";
                }

                if (sceneCount > 0 || rosterCount > 0 || stolenCount > 0)
                    sb.AppendLine($"  {monsterId,-10} {sceneCount,-8} {rosterCount,-8} {stolenCount,-8} {action}");
            }
            sb.AppendLine($"  =========================================");
            DebugLogger.Log(sb.ToString());

            // ── 第三步：按偷窃记录裁剪场景动物 ──
            if (sceneAnimalsByMonster.Count == 0) return;

            int totalRemoved = 0;
            Vec3 playerPos = Agent.Main?.Position ?? Vec3.Zero;

            foreach (var kvp in sceneAnimalsByMonster)
            {
                string monsterId = kvp.Key;
                int stolenCount = VillageAnimalTracker.GetStolenCount(settlementId, monsterId);
                if (stolenCount <= 0) continue;

                List<Agent> agents = kvp.Value;
                int sceneCount = agents.Count;
                int toRemove = Math.Min(stolenCount, sceneCount);

                var sorted = agents.OrderByDescending(a => a.Position.DistanceSquared(playerPos)).ToList();

                for (int i = 0; i < toRemove; i++)
                {
                    try
                    {
                        sorted[i].FadeOut(false, true);
                        totalRemoved++;
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"[AnimalSync] FadeOut error for {sorted[i].Name}: {ex.Message}");
                    }
                }
            }

            if (totalRemoved > 0)
                DebugLogger.Log($"[AnimalSync] {settlement.Name}: removed {totalRemoved} stolen animal(s) across {sceneAnimalsByMonster.Count} type(s)");

            // ── 第四步：ItemRoster 补足（用缓存自然数 - 被偷数）──
            TopUpRosterToNaturalCounts(settlement);
        }

        /// <summary>
        /// 按缓存自然数补足 ItemRoster：自然数 - 被偷数 = 应存数量，只补不删。
        /// 可从场景进入或村庄菜单调用（无场景依赖）。
        /// </summary>
        internal static void TopUpRosterToNaturalCounts(Settlement settlement)
        {
            if (settlement == null || !settlement.IsVillage) return;
            string settlementId = settlement.StringId;
            if (string.IsNullOrEmpty(settlementId)) return;

            int totalToppedUp = 0;
            foreach (var monsterId in AnimalMonsters)
            {
                int natural = VillageAnimalTracker.GetNaturalCount(settlementId, monsterId);
                if (natural <= 0) continue; // 未缓存

                ItemObject item = GetLivestockItemForAnimal(monsterId, null);
                if (item == null) continue;

                int stolen = VillageAnimalTracker.GetStolenCount(settlementId, monsterId);
                int expected = natural - stolen;
                if (expected < 0) expected = 0;

                int current = settlement.ItemRoster.GetItemNumber(item);
                int deficit = expected - current;
                if (deficit > 0)
                {
                    settlement.ItemRoster.AddToCounts(item, deficit);
                    totalToppedUp += deficit;
#if DEBUG
                    DebugLogger.Log($"[AnimalSync] Topped up {item.Name}: roster {current} → {expected} (natural={natural} stolen={stolen})");
#endif
                }
            }

            if (totalToppedUp > 0)
                DebugLogger.Log($"[AnimalSync] {settlement.Name}: topped up {totalToppedUp} animal(s) in ItemRoster");
        }

        /// <summary>
        /// 偷牲畜：将动物 Agent 转化为玩家库存中的牲畜物品（ItemType.Animal）。
        /// 异步播放蹲下采集动画 → 查找物品 → 加入背包 + 扣村庄库存 → 消除动物 → 站起。
        /// </summary>
        private async void TryStealAnimal(Agent animal)
        {
            if (animal == null || !animal.IsActive()) return;

            // ── 并发守卫：防止动画期间重复触发 ──
            if (_isStealingAnimal) return;
            _isStealingAnimal = true;

            // 立即隐藏交互 UI，防止动画期间被重新聚焦
            _interactVM.IsVisible = false;
            IsHandlingInteraction = false;
            _lastFocusedAgent = null;

            Agent mainAgent = Agent.Main;
            if (mainAgent == null || !mainAgent.IsActive())
            {
                _isStealingAnimal = false;
                return;
            }

            string animalName = animal.Name ?? "动物";
            string monsterId = animal.Monster?.StringId;

            try
            {
                // ── 步骤 1：面向动物 ──
                AgentControlHelper.FaceToActor(mainAgent, animal);

                // ── 步骤 2：播放蹲下拾取动画 ──
                AgentControlHelper.ForcePlayAction(mainAgent, "act_pickup_down_begin");
                await Task.Delay(400);

                // ── 步骤 3：再次确认动物存活 ──
                if (animal == null || !animal.IsActive())
                {
                    InformationManager.DisplayMessage(
                        new InformationMessage("动物跑掉了...", Colors.Gray));
                    AgentControlHelper.ForcePlayAction(mainAgent, "act_pickup_down_end");
                    return;
                }

                // ── 步骤 4：查找对应的牲畜物品（静态缓存，惰性初始化）──
                ItemObject livestockItem = GetLivestockItemForAnimal(monsterId, animalName);

                if (livestockItem == null)
                {
                    string errMsg = $"无法将 {animalName}（monster={monsterId}）转化为库存物品——未找到匹配的 Animal 类型物品";
                    DebugLogger.Log($"[TryStealAnimal] {errMsg}");
                    InformationManager.DisplayMessage(new InformationMessage(errMsg, Colors.Red));
                    AgentControlHelper.ForcePlayAction(mainAgent, "act_pickup_down_end");
                    return;
                }

                // ── 步骤 5：核心业务（库存转移 + 追踪 + 犯罪记账）──
                StealManager.StealAnimal(Settlement.CurrentSettlement, livestockItem, monsterId, animal);

                // ── 步骤 6：消除场景中的动物 Agent ──
                try
                {
                    animal.FadeOut(false, true);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[TryStealAnimal] FadeOut error: {ex.Message}");
                }

                // ── 步骤 7：播放站起来动画 ──
                AgentControlHelper.ForcePlayAction(mainAgent, "act_pickup_down_end");

                // ── 步骤 8：UI 反馈 ──
                string msg = $"获得了 {livestockItem.Name}！";
                InformationManager.DisplayMessage(new InformationMessage(msg, Colors.Green));
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[TryStealAnimal] Error: {ex.Message}");
                InformationManager.DisplayMessage(
                    new InformationMessage("偷动物失败", Colors.Red));

                // 出错了也尝试站起来
                if (mainAgent != null && mainAgent.IsActive())
                    AgentControlHelper.ForcePlayAction(mainAgent, "act_pickup_down_end");
            }
            finally
            {
                _isStealingAnimal = false;
            }
        }

        private void TryStealFromAgent(Agent target)
        {
                InformationManager.DisplayMessage(new InformationMessage("你屏住呼吸，悄悄伸出了手...", Colors.Green));

                // 【核心修改】：打开你的 Gauntlet UI
                OpenStealInterface(target);
        }

        /// <summary>
        /// 从背后击晕目标Agent。复用偷窃的蹲伏+背后判定。
        /// 引擎 Immortal + Health=0 → AgentState.Unconscious（等同 ragdoll 倒地）。
        /// 若引擎未自动处理，强制播放击倒动画兜底。
        /// </summary>
        private async void TryKnockoutAgent(Agent target)
        {
            if (target == null || !target.IsActive()) return;

            string targetName = target.Name?.ToString() ?? "目标";

            // human_child monster 的骨骼比例（臂长 0.6、眼高 1.2）与 adult 不同，
            // death_fall_front 动画无法在其骨架上播放，直接拒绝
            string monsterId = target.Monster?.StringId;
            if (monsterId == "human_child")
            {
                DebugLogger.Log($"[Knockout] Skipped {targetName}: monster={monsterId} — child skeleton incompatible");
                InformationManager.DisplayMessage(
                    new InformationMessage($"{targetName} 年纪太小了，下不了手。", Colors.Gray));
                return;
            }

            string attackAnim = "act_1h_bash";

            try
            {
                // 1. ★ 玩家攻击动作：朝向目标 + 根据武器选择打击动画
                Agent mainAgent = Agent.Main;
                if (mainAgent != null && mainAgent.IsActive())
                {
                    
                    AgentControlHelper.FaceToActor(mainAgent, target);    

                    EquipmentIndex mainWpn = V.MainWpn(mainAgent);
                    attackAnim = mainWpn != EquipmentIndex.None ? "act_1h_bash" : "act_shield_bash";
                    AgentControlHelper.ForcePlayAction(mainAgent, attackAnim);
                    await Task.Delay(600);
              
                }

                // 2. 强制播放击倒动画（ForcePlayAction 会临时切到 as_human_warrior
                //    以绕过村民/平民 action_set 缺乏战斗动作的问题）
                if (target.IsActive())
                {
                    // act_death_fall_front: monster_usage_fall direction="back" death_type="knock_back"
                    // 背后打击 → 受害者面朝下扑倒，表现最接近背后击晕
                    AgentControlHelper.ForcePlayAction(target, "act_death_fall_front");

                    // 走事件系统投递击晕事件 → Brain.ReceiveEvent 自动 Suspend + StayAction 占位
                    AgentAIController.Instance?.SendEventToAgent(target, "event_agent_knocked_out");

                    target.SetScriptedFlags(AIScriptedFrameFlags.DoNotRun | AIScriptedFrameFlags.NoAttack);
                }

                // 3. 记录击晕
                _knockedOutAgents.Add(target);

                // 4. UI 反馈
                InformationManager.DisplayMessage(
                    new InformationMessage($"从背后击晕了 {targetName}！", Colors.Green));

                // 5. 隐藏交互 UI，重置状态
                _interactVM.IsVisible = false;
                IsHandlingInteraction = false;
                _lastFocusedAgent = null;

                DebugLogger.Log($"[Knockout] {Agent.Main.Name} knocked out {targetName} from behind (anim: {attackAnim})");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Knockout] Error: {ex.Message}");
                InformationManager.DisplayMessage(
                    new InformationMessage("击晕失败", Colors.Red));
            }
        }

        private List<Agent> _lootedCorpses = new List<Agent>(); // 用于记录已经搜刮过的尸体，避免重复搜刮

        // "自己挑选"库存界面关闭后的待处理状态
        private Agent _pendingLootCorpse;
        private ItemRoster _pendingLootRoster;
        private bool _pendingIsStealing;

        /// <summary>
        /// "自己挑选"库存界面关闭后的收尾：标记已搜刮 + 精准扒掉被玩家拿走的装备。
        /// </summary>
        private void ProcessPendingLoot()
        {
            Agent corpse = _pendingLootCorpse;
            ItemRoster remainingRoster = _pendingLootRoster;
            bool isStealing = _pendingIsStealing;

            _pendingLootCorpse = null;
            _pendingLootRoster = null;

            // 尸体可能已被清理（换场景等）
            if (corpse == null) return;

            if (!isStealing)
            {
                _lootedCorpses.Add(corpse);
                // remainingRoster 已被 OpenScreenAsLoot 原地修改：玩家拿走的已被移除
                // 传进去 → 只扒掉不在 roster 中的槽
                StealManager.StripAgentEquipment(corpse, true, true, remainingRoster);
            }
        }

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
            // 这里我们做"顺手牵羊"：你拿到了装备，但NPC还没发现自己丢了东西。
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
            if(targetHero != null)
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
                        AgentControlHelper.TransferGold(null, Hero.MainHero, lootedGold, notify: false);
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
                        AgentControlHelper.TransferGold(null, Hero.MainHero, lootedGold);
                        InformationManager.DisplayMessage(new InformationMessage($"获得了 {lootedGold} 两钱。", Colors.Yellow));
                    }

                    if (!lootRoster.IsEmpty())
                    {
                        // 推迟到库存界面关闭后再处理：标记已搜刮 + 精准扒掉被拿走的装备
                        _pendingLootCorpse = targetAgent;
                        _pendingLootRoster = lootRoster;
                        _pendingIsStealing = isStealing;
                        var rosterDictionary = new Dictionary<PartyBase, ItemRoster>();
                        rosterDictionary.Add(PartyBase.MainParty, lootRoster);
#if !MB2_V1212
                        // InventoryManager not available in Latest; skip loot screen for now
                        DebugLogger.Log("[InteractionMissionView] InventoryManager not available in this version, skipping loot screen");
#else
                        InventoryManager.OpenScreenAsLoot(rosterDictionary);
#endif
                    }
                    else if (!isStealing)
                    {
                        // 没装备可挑，直接标记搜刮即可（钱已在上面转走）
                        _lootedCorpses.Add(targetAgent);
                    }
                },
                "", 0f
            ), true);
        }

        // ── 大世界遭遇对话：氛围护卫 spawn ──

        private void SpawnEncounterBodyguards(Agent partnerAgent)
        {
            try
            {
                var playerParty = PartyBase.MainParty;
                var npcParty = MapEncounterDialogState.PartnerParty;
                if (playerParty == null && npcParty == null) return;

                var playerTroops = PickGuardTroops(playerParty, CharacterObject.PlayerCharacter, out int playerTotal);
                var npcTroops = PickGuardTroops(npcParty, MapEncounterDialogState.Partner, out int npcTotal);
                if (playerTroops.Count == 0 && npcTroops.Count == 0) return;

                Vec3 playerPos = Agent.Main.Position;
                Vec3 npcPos = partnerAgent.Position;
                Vec3 toNpc = (npcPos - playerPos).NormalizedCopy();
                Vec3 toPlayer = -toNpc;
                Team playerTeam = Mission.Current.PlayerTeam;
                Team npcTeam = partnerAgent.Team;

                const int maxPerRow = 4;

                // 玩家护卫：站在玩家身后，面朝 NPC + 持续注视 NPC，超过 5 人则多排
                for (int i = 0; i < playerTroops.Count; i++)
                {
                    int row = i / maxPerRow;
                    int col = i % maxPerRow;
                    int inRow = Math.Min(maxPerRow, playerTroops.Count - row * maxPerRow);
                    float offset = (col - (inRow - 1) * 0.5f) * 1.8f;
                    float depth = 1.0f + row * 1.5f;
                    Vec3 pos = playerPos + toPlayer * depth + LateralOffset(toNpc, offset);
                    SpawnGuardAgent(playerTroops[i], pos, toNpc.AsVec2, playerTeam, partnerAgent);
                }

                // NPC 护卫：站在 NPC 身后，面朝玩家 + 持续注视玩家
                for (int i = 0; i < npcTroops.Count; i++)
                {
                    int row = i / maxPerRow;
                    int col = i % maxPerRow;
                    int inRow = Math.Min(maxPerRow, npcTroops.Count - row * maxPerRow);
                    float offset = (col - (inRow - 1) * 0.5f) * 1.8f;
                    float depth = 1.0f + row * 1.5f;
                    Vec3 pos = npcPos + toNpc * depth + LateralOffset(toNpc, offset);
                    SpawnGuardAgent(npcTroops[i], pos, toPlayer.AsVec2, npcTeam, Agent.Main);
                }

                DebugLogger.Log($"[MapConv] Guards: player={playerTroops.Count}(/{playerTotal}) npc={npcTroops.Count}(/{npcTotal})");
            }
            catch (Exception ex) { DebugLogger.Log($"[MapConv] Guard spawn error: {ex}"); }
        }

        /// <summary>从 party roster 取等级最高的非 Hero 兵种，数量按部队规模 clamp [2,5]</summary>
        private static List<CharacterObject> PickGuardTroops(PartyBase party, CharacterObject excludeLeader, out int totalManCount)
        {
            totalManCount = 0;
            var result = new List<CharacterObject>();
            if (party?.MemberRoster == null) return result;

            totalManCount = party.MemberRoster.TotalManCount;
            int guardCount = Math.Min(10, Math.Max(2, totalManCount / 10));

            foreach (TroopRosterElement element in party.MemberRoster.GetTroopRoster())
            {
                if (element.Character == null) continue;
                if (element.Character == excludeLeader) continue;
                if (element.Character.IsHero) continue;
                if (element.Character.IsPlayerCharacter) continue;
                result.Add(element.Character);
            }

            return result.OrderByDescending(c => c.Level).Take(guardCount).ToList();
        }

        /// <summary>用 HeroSpawnerMissionBehavior 同款 API 生成一个护卫 Agent，并持续注视目标</summary>
        private static void SpawnGuardAgent(CharacterObject character, Vec3 position, Vec2 direction, Team team, Agent lookTarget = null)
        {
            var origin = new SimpleAgentOrigin(character, -1, null);
            var buildData = new AgentBuildData(origin)
                .InitialPosition(position)
                .InitialDirection(direction)
                .Team(team)
                .NoHorses(true)
                .CivilianEquipment(character.IsFemale);
            Agent guard = Mission.Current.SpawnAgent(buildData);
            if (guard != null)
            {
                guard.SetActionChannel(0, ActionIndexCache.Create("act_conversation_normal_loop"), false, 0UL, 0f, 1f, 0f, 0.4f, MBRandom.RandomFloat, false, -0.2f, 0, true);
                if (lookTarget != null)
                    guard.SetLookAgent(lookTarget);
            }
        }

        private static Vec3 LateralOffset(Vec3 forward, float offset)
        {
            return new Vec3(-forward.y * offset, forward.x * offset, 0);
        }

        /// <summary>打印当前 mission 所有 Agent 的坐标到日志，方便分析站位</summary>
        private static void LogAllAgentPositions()
        {
            try
            {
                if (Mission.Current == null) return;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== [MapConv] All Agent Positions ===");
                int idx = 0;
                foreach (Agent a in Mission.Current.Agents)
                {
                    if (a == null) continue;
                    idx++;
                    string name = a.Name?.ToString() ?? "?";
                    string charId = a.Character?.StringId ?? "?";
                    Vec3 p = a.Position;
                    float distToPlayer = Agent.Main != null ? a.Position.Distance(Agent.Main.Position) : 0f;
                    float distToPartner = 0f;
                    if (MapEncounterDialogState.Partner != null)
                    {
                        foreach (Agent pa in Mission.Current.Agents)
                        {
                            if (pa.Character == MapEncounterDialogState.Partner && pa.IsActive())
                            {
                                distToPartner = a.Position.Distance(pa.Position);
                                break;
                            }
                        }
                    }
                    sb.AppendLine($"[{idx}] {name} ({charId}) pos=({p.x:F2},{p.y:F2},{p.z:F2}) distToPlayer={distToPlayer:F1}m distToPartner={distToPartner:F1}m team={a.Team?.Side}");
                }
                sb.AppendLine("=====================================");
                DebugLogger.Log(sb.ToString());
            }
            catch (Exception ex) { DebugLogger.Log($"[MapConv] LogAgents error: {ex}"); }
        }

        /// <summary>打印当前相机参数到日志（优先读 CustomCamera，fallback 到默认相机）</summary>
        private static void LogCurrentCamera(string label)
        {
            try
            {
                if (Mission.Current == null) return;
                var ms = ScreenManager.TopScreen as MissionScreen;
                Camera cam = ms?.CustomCamera;
                MatrixFrame frame = (cam != null) ? cam.Frame : Mission.Current.GetCameraFrame();
                Vec3 pos = frame.origin;
                Vec3 fwd = frame.rotation.f;
                Vec3 up = frame.rotation.u;
                string extra = cam != null ? " [CustomCamera]" : " [DefaultCam]";
                DebugLogger.Log($"[Cam] {label} pos=({pos.x:F2},{pos.y:F2},{pos.z:F2}) fwd=({fwd.x:F3},{fwd.y:F3},{fwd.z:F3}) up=({up.x:F3},{up.y:F3},{up.z:F3}){extra}");
            }
            catch (Exception) { }
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

    /// <summary>
    /// Hide default "Press F to talk" / NPC name on focused agents.
    /// v1.2.12: AgentInteractionInterfaceVM.SetAgent, string properties
    /// v1.4.6+: AgentInteractionInterfaceVM.SetHumanAgent, MBBindingList properties
    /// </summary>
#if MB2_V1212
    [HarmonyPatch(typeof(TaleWorlds.MountAndBlade.ViewModelCollection.AgentInteractionInterfaceVM), "SetAgent")]
    public static class ChangeInteractionTextPatch
    {
        public static void Postfix(TaleWorlds.MountAndBlade.ViewModelCollection.AgentInteractionInterfaceVM __instance, Agent focusedAgent)
        {
            if (focusedAgent != null)
            {
                __instance.SecondaryInteractionMessage = "";
                __instance.PrimaryInteractionMessage = "";
            }
        }
    }
#else
    [HarmonyPatch(typeof(TaleWorlds.MountAndBlade.ViewModelCollection.Missions.Interaction.AgentInteractionInterfaceVM), "SetHumanAgent")]
    public static class ChangeInteractionTextPatch
    {
        public static void Postfix(TaleWorlds.MountAndBlade.ViewModelCollection.Missions.Interaction.AgentInteractionInterfaceVM __instance, Agent focusedAgent)
        {
            if (focusedAgent != null)
            {
                // Reset content without removing items — ResetFocus() accesses [0]/[1] by index
                __instance.PrimaryInteractionMessages?.ApplyActionOnAllItems(x => x.ResetData());
                // SecondaryMessages safe to clear — only checked via .Count, never indexed
                __instance.SecondaryInteractionMessages?.Clear();
            }
        }
    }
#endif

    /// <summary>
    /// 村庄交易界面打开时打印 ItemRoster 中的牲畜物品，
    /// 方便对比"商人卖什么"vs"场景里有什么"。
    /// </summary>
    [HarmonyPatch(typeof(TaleWorlds.CampaignSystem.Inventory.InventoryManager), "OpenScreenAsTrade")]
    public static class TradeScreenAnimalLoggerPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ItemRoster leftRoster)
        {
            try
            {
                var settlement = Settlement.CurrentSettlement;
                if (settlement == null || !settlement.IsVillage) return;
                string name = settlement.Name?.ToString() ?? "?";
                string id = settlement.StringId ?? "?";

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"=== [TradeScreen] {name} ({id}) ItemRoster animals ===");

                foreach (var monsterId in InteractionMissionView.AnimalMonsters)
                {
                    ItemObject item = InteractionMissionView.GetLivestockItemForAnimal(monsterId, null);
                    if (item == null) continue;
                    int count = leftRoster.GetItemNumber(item);
                    if (count > 0)
                        sb.AppendLine($"  {monsterId,-10} x{count}");
                }
                if (sb.Length == 0)
                    sb.AppendLine("  (no animal items in roster)");
                sb.AppendLine($"  ================================================");
                DebugLogger.Log(sb.ToString());
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[TradeScreen] Log error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 村庄非本地动物价格修正：本地不产的动物买入 5 倍、卖出 0.3 倍。
    /// 只对玩家交易生效。
    /// </summary>
    [HarmonyPatch(typeof(VillageMarketData), "GetPrice",
        new Type[] { typeof(EquipmentElement), typeof(MobileParty), typeof(bool), typeof(PartyBase) })]
    public static class VillageAnimalPricePatch
    {
        private const float NonNativeBuyMultiplier = 5f;
        private const float NonNativeSellMultiplier = 0.3f;

        [HarmonyPostfix]
        public static void Postfix(ref int __result, EquipmentElement itemRosterElement,
            MobileParty tradingParty, bool isSelling)
        {
            try
            {
                if (tradingParty != MobileParty.MainParty) return;
                if (itemRosterElement.IsEmpty || itemRosterElement.Item == null) return;
                if (itemRosterElement.Item.Type != ItemObject.ItemTypeEnum.Animal) return;

                var settlement = Settlement.CurrentSettlement;
                if (settlement == null || !settlement.IsVillage) return;

                // 检查该动物是否为村庄特产
                ItemObject animalItem = itemRosterElement.Item;
                bool isNative = false;
                foreach (var prod in settlement.Village.VillageType.Productions)
                {
                    if (prod.Item1 == animalItem)
                    {
                        isNative = true;
                        break;
                    }
                }

                if (!isNative)
                {
                    float multiplier = isSelling ? NonNativeSellMultiplier : NonNativeBuyMultiplier;
                    int original = __result;
                    __result = (int)(__result * multiplier);
                    if (__result < 1) __result = 1;
#if DEBUG
                    DebugLogger.Log($"[AnimalPrice] {settlement.Name}: {animalItem.Name} non-native, {original} → {__result} ({(isSelling ? "sell" : "buy")} x{multiplier})");
#endif
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[AnimalPrice] Error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 村庄菜单打开时补足 ItemRoster（无需进场景）。
    /// 依赖 VillageAnimalTracker 缓存自然数（首次进场景时记录）。
    /// </summary>
    [HarmonyPatch(typeof(TaleWorlds.CampaignSystem.GameMenus.GameMenu), "SwitchToMenu")]
    public static class VillageMenuAnimalPatch
    {
        [HarmonyPostfix]
        public static void Postfix(string menuId)
        {
            try
            {
                if (menuId != "village") return;
                var settlement = Settlement.CurrentSettlement;
                if (settlement == null || !settlement.IsVillage) return;
                InteractionMissionView.TopUpRosterToNaturalCounts(settlement);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[VillageMenu] Error: {ex.Message}");
            }
        }
    }

}
