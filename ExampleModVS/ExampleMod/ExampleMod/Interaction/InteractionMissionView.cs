using HarmonyLib;
using SandBox.Conversation.MissionLogics;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
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
using TaleWorlds.Localization;
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

        // --- 偷窃界面变量（进度条小游戏，双模式：扒窃/撬锁） ---
        private StealBarVM _stealBarVM;
        private GauntletLayer _stealLayer;

        // 偷窃子弹时间：请求队列式减速（击杀镜头同款），关闭路径必须全部回收
        private const int StealSlowmoRequestId = 731007;
        private bool _stealSlowmoActive = false;

        // 偷窃期间冻结玩家控制（Controller→AI：输入移交 AI 组件，主角待机；v1.2.12/1.4.x 均支持）
        private bool _playerControlFrozen = false;

        // 冻结前的蹲姿（切 AI 后原生姿态被重置为站立，需用 scripted flag 保持）
        private bool _frozenWasCrouching = false;

        // 箱子"自己挑选"路径：金币先落袋暂存，物品在 ProcessPendingChestLoot 一并记账
        private int _pendingChestGold = 0;


        private int _tickCounter = 0;

        // 输入设备追踪（键盘↔手柄切换时刷新全部按键提示，ModInput 统一管理映射）
        private bool _lastUsingGamepad = false;


        // 缓存变量，用于去重，避免每帧刷新UI
        private Agent _lastFocusedAgent = null;
        private bool _lastAgentWasAlive = false;
        private bool _lastIsBehind = false;
        private bool _lastWasCrouching = false;
        private bool _lastWasAnimal = false;
        private NpcIntentType _lastNpcIntentType = NpcIntentType.None;

        // 偷动物并发守卫：防止偷窃条/拾取动画期间重复触发
        private bool _isStealingAnimal = false;

        // 抓动物偷窃条的目标（命中后 CompleteAnimalSteal 消费；关层统一清空）
        private Agent _stealAnimalTarget = null;

        // 场景动物同步：首帧只执行一次
        private bool _animalSyncDone = false;

        // 财富分配：首帧只执行一次
        private bool _wealthDistributed = false;

        // 箱子实体 + 生成标记
        private GameEntity _chestEntity = null;
        private bool _chestSpawned = false;

        // 箱子"自己挑选"待处理状态（InventoryManager 关闭后处理）
        private bool _chestLootPending = false;
        private ItemRoster _pendingChestSnapshot = null;

        // 玩家是否在箱子交互范围内（PerformPerformanceHeavyLogic 更新）
        private bool _nearChest = false;

        // 首次靠近箱子时给出提示（KCD2 风格沉浸引导）
        private bool _chestHintShown = false;


        public MissionScreen thisMissionScreen;

        // 标记是否是我们自己在处理交互，用于通知 Harmony 补丁
        public static bool IsHandlingInteraction { get; private set; } = false;
        //聊天锁，防止其他人也想和玩家说话
        public static bool IsChatting { get; private set; } = false;

        /// <summary>
        /// 闲聊（G键自由对话）总开关。false 时 UI 不显示"闲聊"入口、G 键不触发自由对话。
        /// 临时屏蔽用，改回 true 即可恢复。
        /// </summary>
        public static bool EnableSmallTalk { get; set; } = false;

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
            _interact_layer = V.NewLayer(10);
            _interact_layer.LoadMovie("InteractArea", _interactVM);
            thisMissionScreen.AddLayer(_interact_layer);

            //对话UI
            _dialogueVM = new StoryDialogVM();
            _dialogueLayer = V.NewLayer(11);
            _dialogueLayer.LoadMovie("DialogChoice", _dialogueVM);
            thisMissionScreen.AddLayer(_dialogueLayer);
            // 初始化控制器
            _interactionController = new InteractionController(_dialogueVM);

            // 订阅关闭事件，处理收尾工作
            _dialogueVM.OnDialogClosed += OnDialogueEnded;

            Instance = this;

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
            // 2. 类型排除：非人类（含儿童）且非动物 → 排除
            if (!AgentControlHelper.IsHumanOrChild(agent) && !IsAnimalAgent(agent)) return;

  

            // 3. 距离剔除 (Distance Squared)
            float distSq = agent.Position.DistanceSquared(eyePos);
            if (distSq > maxDistanceSq) return;

            // 活着且不再玩家屏幕里的人，不参与搜索
            if (agent.IsActive() && !NpcSightSystem.IsPlayerSeeing(agent) && AgentControlHelper.IsHumanOrChild(agent))
            {
                return;
            }

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
                bool isHumanTarget = AgentControlHelper.IsHumanOrChild(raycastedAgent)
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

            // -------------------------------------------------------
            // 来源 C：附近的动物 (Nearby Animals)
            // -------------------------------------------------------
            // GetNearbyAgents 的 native C++ 实现不返回动物 Agent（只返回人类战斗单位），
            // 需要手动从 Mission.Current.Agents 遍历捞动物。
            foreach (Agent agent in Mission.Current.Agents)
            {
                if (IsAnimalAgent(agent))
                    ProcessAgentCandidate(agent, eyePos, lookDir, maxDistanceSq, livingMinDot, ref bestDotProduct, ref bestAgent);
            }
            if (bestAgent != null)
            {
                /*
                float actualDist = bestAgent.Position.Distance(eyePos);
                if (IsAnimalAgent(bestAgent))
                {
                    string animalName = bestAgent.Name?.ToString() ?? "unknown";
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"吸附检测 找到了{actualDist:F1}米的 动物({animalName})")); // lwn-ignore: A (debug)
                }
                else
                {
                    string name = bestAgent.Character?.Name?.ToString() ?? "???";
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"吸附检测 找到了{actualDist:F1}米的 {name}")); // lwn-ignore: A (debug)
                }
                */
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
            // 战斗模式下不处理任何交互输入（击晕/偷窃/对话均不可用）
            if (Settings.Instance.IsInteractionDisabled())
                return;

            // 箱子互动优先：靠近箱子时按互动键 → 打开保管箱
            if (_nearChest && _chestEntity != null && ModInput.Pressed(ModInputAction.Interact))
            {
                OpenChest();
                return;
            }

            // 如果没有缓存的目标，直接返回，防止空引用
            if (_lastFocusedAgent == null) return;

            if (ModInput.Pressed(ModInputAction.Interact))
            {
                // 动物：活的蹲下偷，死的搜刮
                if (_lastWasAnimal)
                {
                    if (_lastAgentWasAlive)
                    {
                        if (IsMainAgentCrouching())
                            TryStealAnimal(_lastFocusedAgent);
                    }
                    else
                        LootAgent(_lastFocusedAgent, isStealing: false);
                }
                else if (_lastAgentWasAlive)
                {
                    if (_lastNpcIntentType == NpcIntentType.Fighting || _lastNpcIntentType == NpcIntentType.Surrendering)
                    {
                        CombatManager.PlayerSurrenderToAgent(_lastFocusedAgent);
                    }
                    else if (_lastIsBehind)
                    {
                        if (IsMainAgentCrouching())
                            TryStealFromAgent(_lastFocusedAgent);
                        else
                            TryKnockoutAgent(_lastFocusedAgent);
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
            else if (ModInput.Released(ModInputAction.AltInteract))
            {
                if (_lastAgentWasAlive)
                {
                    if (_lastNpcIntentType == NpcIntentType.Surrendering)
                        CombatManager.AcceptAgentSurrender(_lastFocusedAgent);
                    else if (EnableSmallTalk && _lastNpcIntentType != NpcIntentType.Fighting)
                        _ = StartFreeConversationFlow(_lastFocusedAgent);
                }
            }
        }

        private void PerformPerformanceHeavyLogic()
        {
            // A. 获取目标
            Agent currentAgent = GetFocusdAgent();

            // A2. 箱子接近度检测（每 3 帧更新，用于 HUD 提示 + HandleInput 缓存）
            bool nearChest = _chestEntity != null && Agent.Main != null
                && Agent.Main.Position.Distance(_chestEntity.GetGlobalFrame().origin) < 3f;

            // B. 排除空目标或玩家自己 → 显示箱子提示或隐藏 UI
            if (currentAgent == null || currentAgent == Mission.Current.MainAgent)
            {
                if (nearChest)
                {
                    // 首次靠近 → KCD2 风格沉浸提示（仅一次）
                    if (!_chestHintShown)
                    {
                        _chestHintShown = true;
                        var chestCtx = StealManager.GetCurrentChestContext();
                        var (hint, _, _) = GetChestTexts(chestCtx);
                        InformationManager.DisplayMessage(new InformationMessage(hint, Colors.Yellow));
                    }

                    // 没在看人但在箱子旁边 → 显示箱子提示
                    _interactVM.IsVisible = true;
                    IsHandlingInteraction = true;
                    var actions = new List<(string, ModInputAction?)>();
                    // 本地化：撬锁交互按钮
                    actions.Add((LWNTextHelper.ResolveText("LWN_ui_interact_lockpick", "Pick Lock"), ModInputAction.Interact));
                    var chestCtx2 = StealManager.GetCurrentChestContext();
                    var (_, title2, _) = GetChestTexts(chestCtx2);
                    _interactVM.UpdateTarget(title2, actions);
                    _nearChest = true;
                    _lastFocusedAgent = null;
                }
                else
                {
                    _nearChest = false;
                    if (_interactVM.IsVisible)
                    {
                        _interactVM.IsVisible = false;
                        IsHandlingInteraction = false;
                        _lastFocusedAgent = null;
                    }
                }
                return;
            }

            // 有聚焦目标时也更新箱子接近状态（供 HandleInput 判断优先级）
            _nearChest = nearChest;

            // C. 计算状态
            bool isAnimal = IsAnimalAgent(currentAgent);
            bool isAlive = currentAgent.IsActive();
            bool isKnockedOut = AgentBrain.IsKnockedOut(currentAgent);

            // 已被击晕的Agent视为失去行动能力（引擎可能未立即转为Unconscious时兜底）
            if (isKnockedOut)
            {
                isAlive = false;
            }

            bool isBehind = !isAnimal && isAlive && IsBehindTarget(currentAgent);
            // 蹲姿对动物也要真实追踪：蹲下才能偷动物，蹲下/站起需触发 UI 刷新
            bool isCrouching = IsMainAgentCrouching();


            // E. 判断是否需要刷新 UI (对比上一状态)
            bool targetChanged = (currentAgent != _lastFocusedAgent);
            bool lifeStateChanged = (isAlive != _lastAgentWasAlive);
            bool behindStateChanged = (isBehind != _lastIsBehind);
            bool crouchStateChanged = (isCrouching != _lastWasCrouching);
            bool animalStateChanged = (isAnimal != _lastWasAnimal);

            // NpcIntent: 从 Brain 读取 NPC 当前意图
            var brain = AgentAIController.GetBrainForAgent(currentAgent);
            var currentNpcIntentType = brain?.CurrentIntent?.Type ?? NpcIntentType.None;
            var prevNpcIntentType = brain?.PreviousIntent?.Type ?? NpcIntentType.None;
            bool intentChanged = (currentNpcIntentType != prevNpcIntentType);

            if (targetChanged || lifeStateChanged || behindStateChanged || crouchStateChanged || animalStateChanged || intentChanged || !_interactVM.IsVisible)
            {
                _interactVM.IsVisible = true;
                IsHandlingInteraction = true;

                var actions = new List<(string, ModInputAction?)>();

                if (isAnimal)
                {
                    // 动物：活的蹲下可偷（站立时给提示不给按键），死的搜刮
                    if (isAlive)
                    {
                        if (isCrouching)
                            // 本地化：偷动物交互按钮
                            actions.Add((LWNTextHelper.ResolveText("LWN_ui_interact_steal_animal", "Steal"), ModInputAction.Interact));
                       
                    }
                    else
                    {
                        // 本地化：搜刮交互按钮
                        actions.Add((LWNTextHelper.ResolveText("LWN_ui_interact_loot", "Loot"), ModInputAction.Interact));
                    }
                }
                else if (isAlive)
                {
                    // 战斗意图优先（正面背后都显示）
                    if (currentNpcIntentType == NpcIntentType.Fighting || currentNpcIntentType == NpcIntentType.Surrendering)
                    {
                        // 本地化：认输交互按钮
                        actions.Add((LWNTextHelper.ResolveText("LWN_ui_interact_surrender", "Surrender"), ModInputAction.Interact));
                        if (currentNpcIntentType == NpcIntentType.Surrendering)
                            // 本地化：接受认输交互按钮
                            actions.Add((LWNTextHelper.ResolveText("LWN_ui_interact_accept_surrender", "Accept Surrender"), ModInputAction.AltInteract));
                    }
                    else if (isBehind)
                    {
                        if (isCrouching)
                            // 本地化：偷窃交互按钮
                            actions.Add((LWNTextHelper.ResolveText("LWN_ui_interact_pickpocket", "Pickpocket"), ModInputAction.Interact));
                        else
                            // 本地化：击晕交互按钮（附难度预览）
                            actions.Add((LWNTextHelper.ResolveCompound("LWN_ui_interact_knockout", ("DIFFICULTY", ComputeKnockoutChance(currentAgent).difficulty)), ModInputAction.Interact));
                        if (EnableSmallTalk)
                            // 本地化：闲聊交互按钮
                            actions.Add((LWNTextHelper.ResolveText("LWN_ui_interact_smalltalk", "Small Talk"), ModInputAction.AltInteract));
                        // 本地化：探查交互按钮
                        actions.Add((LWNTextHelper.ResolveText("LWN_ui_interact_inspect", "Inspect"), ModInputAction.Inspect));
                    }
                    else
                    {
                        // 本地化：对话交互按钮
                        actions.Add((LWNTextHelper.ResolveText("LWN_ui_interact_talk", "Talk"), ModInputAction.Interact));
                        if (EnableSmallTalk)
                            // 本地化：闲聊交互按钮
                            actions.Add((LWNTextHelper.ResolveText("LWN_ui_interact_smalltalk", "Small Talk"), ModInputAction.AltInteract));
                        // 本地化：探查交互按钮
                        actions.Add((LWNTextHelper.ResolveText("LWN_ui_interact_inspect", "Inspect"), ModInputAction.Inspect));
                    }

                }
                else
                {
                    // 本地化：搜刮交互按钮
                    actions.Add((LWNTextHelper.ResolveText("LWN_ui_interact_loot", "Loot"), ModInputAction.Interact));
                }

                // 只有名字不为空才显示，避免报错
                string name;
                if (isAnimal)
                {
                    // 动物：用 agent.Name（"鹅"/"羊" 等），没有 Character
                    // 本地化：动物名兜底
                    name = !string.IsNullOrWhiteSpace(currentAgent.Name) ? currentAgent.Name.Trim() : LWNTextHelper.ResolveText("LWN_ui_name_animal", "animal");
                }
                else
                {
                    // 本地化：未知目标名兜底
                    name = currentAgent.Name != null ? currentAgent.Name.ToString().Trim() : LWNTextHelper.ResolveText("LWN_ui_name_unknown", "Unknown");
                }
                if (!currentAgent.IsActive())
                {
                    // 本地化：目标死亡/昏迷/重伤状态后缀
                    name += isAnimal ? LWNTextHelper.ResolveText("LWN_ui_state_dead", "(dead)") : (isKnockedOut ? LWNTextHelper.ResolveText("LWN_ui_state_unconscious", "(unconscious)") : LWNTextHelper.ResolveText("LWN_ui_state_injured", "(badly injured)"));
                }
                _interactVM.UpdateTarget(name, actions);

                // 更新对比缓存
                _lastFocusedAgent = currentAgent;
                _lastAgentWasAlive = isAlive;
                _lastIsBehind = isBehind;
                _lastWasCrouching = isCrouching;
                _lastWasAnimal = isAnimal;
                _lastNpcIntentType = currentNpcIntentType;
            }
        }


        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            // ── 输入设备切换追踪：键盘↔手柄 → 刷新全部按键提示字形 ──
            bool usingGamepad = ModInput.UsingGamepad;
            if (usingGamepad != _lastUsingGamepad)
            {
                _lastUsingGamepad = usingGamepad;
                _interactVM?.RefreshGlyphs();
                _stealBarVM?.RefreshButtonTexts();
            }

            // 战斗模式下跳过交互 UI 全部逻辑：大世界遭遇/箱子/射线检测/交互选项构建
            if (Settings.Instance.IsInteractionDisabled())
                return;

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

            // ── 财富分配：首帧把定居点金库的钱分配到 NPC 身上 + 公共箱子 ──
            if (!_wealthDistributed)
            {
                _wealthDistributed = true;
                var settlement = Settlement.CurrentSettlement;
                if (settlement != null)
                    StealManager.DistributeSettlementWealth(settlement);
            }

            // ── 箱子生成：财富分配后有 stash 就生成箱子实体 ──
            if (!_chestSpawned && StealManager.StashGold > 0)
            {
                _chestSpawned = true;
                SpawnSettlementChest();
            }

            // ── 偷窃条（扒窃/撬锁）：打开期间独占交互，屏蔽普通 F/G 射线检测 ──
            if (_stealBarVM != null)
            {
                TickStealBar(dt);
                return;
            }


            // ----------------- 0. 库存界面关闭后的搜刮收尾 -----------------
            if (_pendingLootCorpse != null)
            {
                ProcessPendingLoot();
                return;
            }

            // ----------------- 0b. 箱子挑选界面关闭后的收尾 -----------------
            if (_chestLootPending)
            {
                ProcessPendingChestLoot();
                return;
            }

            if (thisMissionScreen == null) return;

            // ----------------- 1. 基础拦截条件 -----------------
            if (Mission.Current.Mode == MissionMode.Conversation || Mission.Current.Mode == MissionMode.Barter)
            {
                if (_interactVM.IsVisible) _interactVM.IsVisible = false;
                return;
            }

            var storyengine = StoryEngine.Instance;
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

            // ----------------- 3. 探查键全局输入：有focus看NPC，无focus看自己 -----------------
            if (ModInput.Released(ModInputAction.Inspect))
            {
                if (_lastFocusedAgent != null)
                    OpenNPCInfoBoard(_lastFocusedAgent);
                else
                    OpenNPCInfoBoard(Agent.Main);
            }

            // ----------------- 4. NPC信息面板关闭：ESC / 手柄B -----------------
            if (_npcInfoLayer != null)
            {
                if (TaleWorlds.InputSystem.Input.IsKeyReleased(InputKey.Escape) ||
                    TaleWorlds.InputSystem.Input.IsKeyReleased(InputKey.ControllerRRight))
                {
                    CloseNPCInfoBoard();
                }
            }

            // ----------------- 4. 高频逻辑：F/G输入监听 (每帧必须执行) -----------------
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
            // 本地化：NPC 就位超时提示
            InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText("LWN_ui_steal_msg_npc_timeout", "The NPC took too long to settle — starting the conversation anyway."), Colors.Red));

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

        private async Task PrepareAgentForConversation(Agent agent)
        {
            // 统一走事件驱动：AgentBrain 内部自行管理 Suspend/Resume/ClearAllActions/EnqueueAction
            AgentAIController.Instance.SendEventToAgent(agent, "ComeHere", Agent.Main);
            await WaitForAgentToSettle(agent);
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

            // 1. 获取数据 (记忆系统可能为 null——模板 NPC 无记忆)
            var memory = AllNpcMemoryManager.GetMemoryForAgent(agent);

            // 2. 创建 VM，传入关闭回调 + Agent（模板 NPC 也能看基本信息和身上的钱）
            _npcInfoVM = new NPCInfoVM(memory, agent, CloseNPCInfoBoard);

            // 3. 创建 Layer 并加载 Movie
            _npcInfoLayer = V.NewLayer(15); // 低层级，比 AgentHUD(5) 和交互提示(10) 高，系统菜单自然覆盖
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

            // 2. 初始化 VM（扒窃模式：光标-子横条时机判定）
            _stealBarVM = new StealBarVM(StealBarMode.Pickpocket, targetAgent, () => CloseStealInterface());

            // 3. 创建 Layer
            _stealLayer = V.NewLayer(16); // 优先级比对话(101)更高，覆盖在上面
            V.LoadMov(_stealLayer, "StealBar", _stealBarVM);

            // 4. 设置输入限制（鼠标可见+按钮可点；键盘不路由给本层，游戏侧按键由 TickStealBar 在 Agent 层剥离）
            _stealLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.Mouse);

            // 5. 添加到屏幕
            thisMissionScreen.AddLayer(_stealLayer);

            // 6. 标记状态，防止其他交互干扰
            IsHandlingInteraction = true;

            // 7. 隐藏原本的"按F交互"小黑条
            _interactVM.IsVisible = false;

            // 🆕 标记偷窃 UI 已打开（供 AgentBrain 警戒值系统检测）
            StealManager.IsUIOpen = true;

            // 8. 子弹时间：世界慢下来，给玩家操作窗口（浮标走缩放 dt 同步变慢，难度不被白嫖）
            StartStealSlowmo();

            // 9. 冻结玩家控制：切 ControllerType.AI → 输入处理权移交 AI 组件（跳/走/攻击全死；键盘 mask 拦不住只能切控制器）
            FreezePlayerControl();
        }

        // 关闭偷窃界面（所有路径统一收口：收手Tab/强制/质问接管/Finalize；ESC 让给游戏菜单，不再收条）
        private void CloseStealInterface()
        {
            // 子弹时间先收（幂等）
            StopStealSlowmo();
            // 玩家控制同步恢复（幂等）
            UnfreezePlayerControl();

            if (_stealLayer != null)
            {
                // 1. 移除 Layer
                thisMissionScreen.RemoveLayer(_stealLayer);
                _stealLayer.InputRestrictions.ResetInputRestrictions();

                // 2. 清理变量
                _stealLayer = null;
                _stealBarVM = null;

                // 3. 恢复状态
                IsHandlingInteraction = false;
                StealManager.IsUIOpen = false;
                _isStealingAnimal = false;
                _stealAnimalTarget = null;
            }
        }

        /// <summary>偷窃条每帧驱动：动画 + 出手/收手键输入 + 关闭原因消费。返回 true 表示本条仍在处理（调用方应 return）。</summary>
        private void TickStealBar(float dt)
        {
            var vm = _stealBarVM;
            if (vm == null || _stealLayer == null) return;

            vm.UpdateFrame(dt);

            if (ModInput.Pressed(ModInputAction.StealAttempt))
                vm.ExecuteAttempt();
            if (ModInput.Pressed(ModInputAction.StealLeave))
            {
                CloseStealInterface();
                return;
            }

            var reason = vm.CloseReason;
            if (reason == StealBarCloseReason.None) return;

            // 抓动物命中：关层前抢出目标（CloseStealInterface 会清空 _stealAnimalTarget）
            Agent caughtAnimal = reason == StealBarCloseReason.AnimalCaught ? _stealAnimalTarget : null;

            // 目标走开/死亡 → 强制收手，不算被发现（无脉冲无广播）
            if (reason == StealBarCloseReason.TargetGone)
                InformationManager.DisplayMessage(new InformationMessage(
                    // 本地化：偷窃条自动收手提示（动物溜走/目标走远）
                    _stealAnimalTarget != null ? LWNTextHelper.ResolveText("LWN_ui_steal_msg_animal_fled", "It slipped away...") : LWNTextHelper.ResolveText("LWN_ui_steal_msg_target_gone", "Your target moved away — you can't continue stealing."), Colors.Gray));
            // 摸空了 → 自动收手提示（无装备也无钱袋）
            else if (reason == StealBarCloseReason.NothingLeft)
                // 本地化：目标被摸空提示
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText("LWN_ui_steal_msg_nothing_left", "There's nothing left to steal from them."), Colors.Gray));
            // AnimalFled：VM 内已完成惊叫/逃跑/围堵广播，此处只收口

            bool lockpickDone = reason == StealBarCloseReason.Completed;
            CloseStealInterface();

            // 撬锁全部 pin 解开 → 进入开箱 Inquiry（IsUIOpen 在 Inquiry 内重新拉起，贯穿 loot 全程）
            if (lockpickDone)
                ShowChestInquiry();
            // 抓动物命中 → 继续实际偷窃流程（动画→物品→消除）
            if (caughtAnimal != null)
                CompleteAnimalSteal(caughtAnimal);
        }

        // ── 偷窃子弹时间（AddTimeSpeedRequest 队列取最小值；Remove 对未知 ID 会 RemoveAt(-1) 抛异常，必须先查）──
        private void StartStealSlowmo()
        {
            if (_stealSlowmoActive) return;
            var mission = Mission.Current;
            if (mission == null) return;
            mission.AddTimeSpeedRequest(new Mission.TimeSpeedRequest(0.35f, StealSlowmoRequestId));
            _stealSlowmoActive = true;
        }

        private void StopStealSlowmo()
        {
            if (!_stealSlowmoActive) return;
            _stealSlowmoActive = false;
            var mission = Mission.Current;
            if (mission != null && mission.GetRequestedTimeSpeed(StealSlowmoRequestId, out _))
                mission.RemoveTimeSpeedRequest(StealSlowmoRequestId);
        }

        // ── 偷窃输入隔离：冻结/恢复玩家控制（冻结期间跳/走/攻击全死，幂等）──
        private void FreezePlayerControl()
        {
            if (_playerControlFrozen) return;
            _playerControlFrozen = true;
            var main = Agent.Main;
            _frozenWasCrouching = main != null && main.CrouchMode;
            V.SetPlayerControlFrozen(main, true);
            // 切 AI 后 AI 移动组件把姿态重置为站立 → 用 scripted flag 恢复蹲姿（AI 蹲姿的官方通道）
            if (_frozenWasCrouching)
                main.SetCrouchMode(true);
        }

        private void UnfreezePlayerControl()
        {
            if (!_playerControlFrozen) return;
            _playerControlFrozen = false;
            var main = Agent.Main;
            // 先解除脚本蹲姿（防止 scripted flag 压住还控制后的玩家输入），再还控制
            if (_frozenWasCrouching)
            {
                main?.SetCrouchMode(false);
                _frozenWasCrouching = false;
            }
            V.SetPlayerControlFrozen(main, false);
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
            _stealBarVM = null;
            // 子弹时间兜底回收（防 ESC 直接退 mission 泄漏）
            StopStealSlowmo();
            // 玩家控制兜底恢复（同上）
            UnfreezePlayerControl();
            StealManager.IsUIOpen = false;

            //清除场景里临时Agent的临时记忆
            AllNpcMemoryManager.ClearTemporaryMemories();

            // 清理箱子实体
            if (_chestEntity != null)
            {
                _chestEntity.Remove(0);
                _chestEntity = null;
            }
            StealManager.ChestEntity = null;
            StealManager.ChestItemRoster = new ItemRoster();
            _chestSpawned = false;
            _chestLootPending = false;
            _pendingChestSnapshot = null;
            _pendingChestGold = 0;
            _chestHintShown = false;

            // 清理财富分配
            StealManager.ClearWealthDistribution();
            _wealthDistributed = false;
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
        /// 偷牲畜入口：守卫 → 开抓动物偷窃条（StealBar Animal 模式，命中/手滑由时机判定决定）。
        /// 命中后的实际偷窃（动画→物品→消除）由 TickStealBar 接 CompleteAnimalSteal 继续。
        /// </summary>
        private void TryStealAnimal(Agent animal)
        {
            // 战斗模式下禁止偷窃动物
            if (Settings.Instance.IsInteractionDisabled()) return;
            if (animal == null || !animal.IsActive()) return;

            // 蹲下才能偷（UI 层已拦，此处防御兜底）
            if (!IsMainAgentCrouching()) return;

            // ── 并发守卫：防止条打开期间重复触发 ──
            if (_isStealingAnimal) return;
            if (_stealLayer != null) return;
            _isStealingAnimal = true;
            _stealAnimalTarget = animal;

            // 立即隐藏交互 UI，防止条打开期间被重新聚焦
            _interactVM.IsVisible = false;
            IsHandlingInteraction = true;
            _lastFocusedAgent = null;

            // 抓动物偷窃条：大动物判定区窄 40%；命中 → CompleteAnimalSteal，手滑 → VM 内惊叫逃跑
            // 本地化：动物名兜底
            string animalName = !string.IsNullOrWhiteSpace(animal.Name) ? animal.Name.Trim() : LWNTextHelper.ResolveText("LWN_ui_name_animal", "animal");
            bool isLarge = StealManager.IsLargeAnimal(animal.Monster?.StringId);
            _stealBarVM = new StealBarVM(StealBarMode.Animal, animal, animalName, isLarge, () => CloseStealInterface());
            _stealLayer = V.NewLayer(16);
            V.LoadMov(_stealLayer, "StealBar", _stealBarVM);
            _stealLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.Mouse);
            thisMissionScreen.AddLayer(_stealLayer);

            StealManager.IsUIOpen = true;
            StartStealSlowmo();
            FreezePlayerControl(); // 同扒窃：冻结玩家输入（跳/走/攻击）
        }

        /// <summary>
        /// 抓动物命中后的实际偷窃：面向 → 蹲下拾取动画 → 库存转移 + 追踪 + 犯罪记账 → 消除动物 → 站起。
        /// </summary>
        private async void CompleteAnimalSteal(Agent animal)
        {
            _isStealingAnimal = true;   // CloseStealInterface 已复位并发守卫，这里重新拉起直到流程结束

            Agent mainAgent = Agent.Main;
            // 本地化：动物名兜底
            string animalName = !string.IsNullOrWhiteSpace(animal?.Name) ? animal.Name.Trim() : LWNTextHelper.ResolveText("LWN_ui_name_animal", "animal");
            string monsterId = animal?.Monster?.StringId;

            try
            {
                if (mainAgent == null || !mainAgent.IsActive() || animal == null) return;

                // ── 步骤 1：面向动物 + 蹲下拾取动画 ──
                AgentControlHelper.FaceToActor(mainAgent, animal);
                AgentControlHelper.ForcePlayAction(mainAgent, "act_pickup_down_begin");
                await Task.Delay(400);

                // ── 步骤 2：再次确认动物存活且没溜远 ──
                if (!animal.IsActive() || animal.Position.Distance(mainAgent.Position) > 5f)
                {
                    InformationManager.DisplayMessage(
                        // 本地化：动物趁机溜走提示
                        new InformationMessage(LWNTextHelper.ResolveText("LWN_ui_steal_msg_animal_escaped", "It took the chance and ran..."), Colors.Gray));
                    AgentControlHelper.ForcePlayAction(mainAgent, "act_pickup_down_end");
                    return;
                }

                // ── 步骤 3：查找对应的牲畜物品（静态缓存，惰性初始化）──
                ItemObject livestockItem = GetLivestockItemForAnimal(monsterId, animalName);

                if (livestockItem == null)
                {
                    // 本地化：动物无法转化为库存物品错误提示
                    string errMsg = LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_animal_convert_fail", ("ANIMAL", animalName), ("MONSTER", monsterId));
                    DebugLogger.Log($"[StealAnimal] {errMsg}");
                    if (Settings.Instance.ShowDebugMessages)
                        InformationManager.DisplayMessage(new InformationMessage(errMsg, Colors.Red));
                    AgentControlHelper.ForcePlayAction(mainAgent, "act_pickup_down_end");
                    return;
                }

                // ── 步骤 4：核心业务（库存转移 + 追踪 + 犯罪记账）──
                StealManager.StealAnimal(Settlement.CurrentSettlement, livestockItem, monsterId, animal);

                // ── 步骤 5：消除场景中的动物 Agent ──
                try
                {
                    animal.FadeOut(false, true);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[StealAnimal] FadeOut error: {ex.Message}");
                }

                // ── 步骤 6：播放站起来动画 ──
                AgentControlHelper.ForcePlayAction(mainAgent, "act_pickup_down_end");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StealAnimal] Error: {ex.Message}");
                InformationManager.DisplayMessage(
                    // 本地化：偷动物失败提示
                    new InformationMessage(LWNTextHelper.ResolveText("LWN_ui_steal_msg_animal_fail", "Failed to steal the animal"), Colors.Red));

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
            // 战斗模式下禁止偷窃
            if (Settings.Instance.IsInteractionDisabled()) return;

            // 没东西可偷（无装备也无钱袋）→ 直接提示，不开条
            if (!StealManager.HasAnythingToSteal(target))
            {
                // 本地化：目标无可偷之物提示
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText("LWN_ui_steal_msg_nothing_to_steal", "There's nothing to steal from them."), Colors.Gray));
                return;
            }

            // 本地化：偷窃出手前屏息提示
            InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText("LWN_ui_steal_msg_hold_breath", "You hold your breath and reach out quietly..."), Colors.Green));

                // 【核心修改】：打开你的 Gauntlet UI
                OpenStealInterface(target);
        }

        /// <summary>
        /// 获取Agent的 Vigor+Control 合计值（Bannerlord 版"力量+敏捷"）。
        /// Hero 有属性记录 → 直接读；模板 NPC 无 Hero → 按 Level 估算。
        /// </summary>
        private static int GetAgentStatTotal(Agent agent)
        {
            var (v, c) = GetAgentStats(agent);
            return v + c;
        }

        /// <summary>
        /// 获取Agent的 Vigor 和 Control 各自的值。
        /// Hero → 直接读属性；模板 NPC → 按 Level 估算（均分到两个属性）。
        /// </summary>
        private static (int vigor, int control) GetAgentStats(Agent agent)
        {
            if (agent == null) return (5, 5);

            var character = agent.Character as CharacterObject;
            var hero = character?.HeroObject;

            if (hero != null)
            {
                return (hero.GetAttributeValue(DefaultCharacterAttributes.Vigor),
                        hero.GetAttributeValue(DefaultCharacterAttributes.Control));
            }

            if (character != null)
            {
                // 模板 NPC 无 Hero → 按 Level 均分估算
                int half = (3 + character.Level / 3) / 2;
                return (half, half);
            }

            return (5, 5);
        }

        /// <summary>
        /// 计算击晕成功率 + 难度文本（易/中/难）。
        /// 基于玩家与目标的 Vigor+Control 比值：ratio = playerStats / targetStats，
        /// successRate = clamp(0.05, 0.95, 0.5 * ratio)。
        /// 小孩(human_child) difficulty 永远返回 "难"（实际在 TryKnockoutAgent 里 100% 免疫）。
        /// </summary>
        private static (float successRate, string difficulty) ComputeKnockoutChance(Agent target)
        {
            string monsterId = target?.Monster?.StringId;

            int playerStats = GetAgentStatTotal(Agent.Main);
            int targetStats = GetAgentStatTotal(target);

            float ratio = targetStats > 0 ? (float)playerStats / targetStats : 3.0f;
            float successRate = Math.Max(0.05f, Math.Min(0.95f, 0.5f * ratio));

            string difficulty;
            // 本地化：击晕难度标签（易/中/难）
            if (monsterId?.Contains("child") == true)
                // 难
                difficulty = LWNTextHelper.ResolveText("LWN_ui_difficulty_hard", "Hard");
            else if (successRate >= 0.70f)
                // 易
                difficulty = LWNTextHelper.ResolveText("LWN_ui_difficulty_easy", "Easy");
            else if (successRate >= 0.40f)
                // 中
                difficulty = LWNTextHelper.ResolveText("LWN_ui_difficulty_medium", "Medium");
            else
                // 难
                difficulty = LWNTextHelper.ResolveText("LWN_ui_difficulty_hard", "Hard");

            return (successRate, difficulty);
        }

        /// <summary>
        /// 从背后击晕目标Agent。复用偷窃的蹲伏+背后判定。
        /// 引擎 Immortal + Health=0 → AgentState.Unconscious（等同 ragdoll 倒地）。
        /// 若引擎未自动处理，强制播放击倒动画兜底。
        /// </summary>
        [HandleProcessCorruptedStateExceptions]
        private async void TryKnockoutAgent(Agent target)
        {
            if (target == null || !target.IsActive()) return;

            // 战斗模式下禁止击晕
            if (Settings.Instance.IsInteractionDisabled()) return;

            // 本地化：击晕目标名兜底
            string targetName = target.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_ui_name_target", "target");

            // 儿童（monster StringId 含 "child"，如 human_child）骨骼比例（臂长 0.6、眼高 1.2）与 adult 不同，
            // death_fall_front 动画无法在其骨架上播放，成功率强制 0（100% 免疫）
            string monsterId = target.Monster?.StringId;
            bool isChild = monsterId?.Contains("child") == true;

            // ── 击晕成功率判定：玩家 Vigor+Control vs 目标 Vigor+Control ──
            var (knockSuccessRate, _) = ComputeKnockoutChance(target);
            float knockRoll = MBRandom.RandomFloat;
            bool knockSuccess = !isChild && knockRoll <= knockSuccessRate;
            DebugLogger.Log($"[Knockout] {targetName}: isChild={isChild}, successRate={knockSuccessRate:F2}, roll={knockRoll:F2}, success={knockSuccess}");

            string attackAnim = "act_1h_bash";

            try
            {
                // 1. ★ 玩家攻击动作：无论成败，先播攻击动画
                Agent mainAgent = Agent.Main;
                if (mainAgent != null && mainAgent.IsActive())
                {
                    AgentControlHelper.FaceToActor(mainAgent, target);

                    EquipmentIndex mainWpn = V.MainWpn(mainAgent);
                    attackAnim = mainWpn != EquipmentIndex.None ? "act_1h_bash" : "act_shield_bash";
                    // 玩家永远是 as_human_warrior，直接用 SetPose 跳过 SetActionSet，
                    // 避免不必要的 native AnimationSystemData 替换触发异步 AI tick 竞态
                    AgentControlHelper.SetPose(mainAgent, attackAnim);
                    await Task.Delay(400);
                }

                // ── 延迟后重新验证目标：400ms 内 target 可能已被引擎回收 ──
                if (target == null || !target.IsActive())
                {
                    DebugLogger.Log($"[Knockout] Target became invalid after delay: {targetName}");
                    return;
                }

                // ── 公共：无论成败，出手即是袭击，记账 ──
                AgentAIController.Instance?.RecordAssaultVictim(target);

                // ★ 击晕成功时，必须先标记受害者状态，再广播第三方目击事件。
                // 否则证人 AgentBrain 处理 WitnessCrime_GatherOnLook 时调用 IsKnockedOut(victim)
                // 会返回 false（event_agent_knocked_out 尚未入队），罪行被错误归类为 Steal。
                if (knockSuccess)
                {
                    // ── 成功：目标倒地 + 击晕事件 ──
                    if (target.IsActive())
                    {
                        AgentControlHelper.ForcePlayAction(target, "act_death_fall_front");
                        target.SetScriptedFlags(AIScriptedFrameFlags.DoNotRun | AIScriptedFrameFlags.NoAttack);
                    }

                    AgentAIController.Instance?.SendEventToAgent(target, "event_agent_knocked_out");
                }

                // 第三方目击广播：受害者始终 exclude（击晕场景玩家在背后，sight check 必然 false，
                // 受害者通过直接事件 event_agent_knocked_out 反应）
                AgentAIController.Instance?.BroadcastEventInRange(
                    target.Position, 20f, "WitnessCrime",
                    exclude: new HashSet<Agent> { target },
                    requireSight: true,
                    Agent.Main, target);

                if (knockSuccess)
                {

                    InformationManager.DisplayMessage(
                        // 本地化：击晕成功消息
                        new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_knockout_success", ("NAME", targetName), ("ROLL", $"{knockRoll * 100:F0}%"), ("CHANCE", $"{knockSuccessRate:P0}")), Colors.Green));

                    DebugLogger.Log($"[Knockout] {Agent.Main.Name} knocked out {targetName} (anim: {attackAnim})");
                }
                else if (isChild)
                {
                    // ── 小孩：100% 躲开，不反击（目击广播已发，周围成人会反应）──
                    DebugLogger.Log($"[Knockout] Child dodged: {targetName}");
                    InformationManager.DisplayMessage(
                        // 本地化：小孩躲开击晕提示
                        new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_target_dodged", ("NAME", targetName)), Colors.Gray));
                }
                else
                {
                    // ── 失败：目标察觉并反击 ──
                    DebugLogger.Log($"[Knockout] Failed: {Agent.Main.Name} vs {targetName}");
                    int pVigor = Hero.MainHero.GetAttributeValue(DefaultCharacterAttributes.Vigor);
                    int pControl = Hero.MainHero.GetAttributeValue(DefaultCharacterAttributes.Control);
                    var (tVigor, tControl) = GetAgentStats(target);
                    MBInformationManager.AddQuickInformation(
                        // 本地化：背后偷袭失败快速提示（属性对比）
                        new TextObject(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_knockout_fail_quick", ("VIGOR", pVigor.ToString()), ("CONTROL", pControl.ToString()), ("NAME", targetName), ("TVIGOR", tVigor.ToString()), ("TCONTROL", tControl.ToString()), ("CHANCE", $"{knockSuccessRate:P0}"))));
                    InformationManager.DisplayMessage(
                        // 本地化：目标察觉反击消息
                        new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_target_retaliates", ("NAME", targetName), ("ROLL", $"{knockRoll * 100:F0}%"), ("CHANCE", $"{knockSuccessRate:P0}")), Colors.Red));

                    // 受害者直接进战斗（sight check 拦不住直接事件）
                    AgentAIController.Instance?.SendEventToAgent(target, "event_agent_damaged", Agent.Main, target);
                }

                // 隐藏交互 UI，重置状态
                _interactVM.IsVisible = false;
                IsHandlingInteraction = false;
                _lastFocusedAgent = null;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Knockout] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                InformationManager.DisplayMessage(
                    // 本地化：击晕失败提示
                    new InformationMessage(LWNTextHelper.ResolveText("LWN_ui_steal_msg_knockout_fail", "Failed to knock out"), Colors.Red));
            }
        }

        private List<Agent> _lootedCorpses = new List<Agent>(); // 用于记录已经搜刮过的尸体，避免重复搜刮

        // "自己挑选"库存界面关闭后的待处理状态
        private Agent _pendingLootCorpse;
        private ItemRoster _pendingLootRoster;
        private bool _pendingIsStealing;
        // "自己挑选"开库存界面时的完整物品快照（搜刮昏迷者记账用：快照 − 界面关闭后的剩余 = 实际拿走的）
        private List<(string itemId, string itemName, int count)> _pendingLootAllItems;

        /// <summary>
        /// 目标是否为"昏迷未死"的活人（被击晕/失去意识）。搜刮此类目标 = 偷窃（走 RecordUnconsciousLootTheft）；
        /// 尸体（Killed）和战场搜刮不算偷窃，不触发。
        /// </summary>
        private static bool IsUnconsciousAlive(Agent agent)
        {
            if (agent == null || !AgentControlHelper.IsHumanOrChild(agent)) return false;
            return AgentBrain.IsKnockedOut(agent) || agent.State == AgentState.Unconscious;
        }

        /// <summary>收集目标当前全部装备槽物品（每槽一条，count=1），供搜刮记账用。</summary>
        private static List<(string itemId, string itemName, int count)> CollectEquipmentItems(Agent agent)
        {
            var items = new List<(string, string, int)>();
            if (agent == null) return items;
            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumEquipmentSetSlots; i++)
            {
                var el = agent.SpawnEquipment[i];
                if (!el.IsEmpty && el.Item != null)
                    items.Add((el.Item.StringId, el.Item.Name?.ToString(), 1));
            }
            return items;
        }

        /// <summary>
        /// "自己挑选"库存界面关闭后的收尾：标记已搜刮 + 精准扒掉被玩家拿走的装备。
        /// </summary>
        private void ProcessPendingLoot()
        {
            Agent corpse = _pendingLootCorpse;
            ItemRoster remainingRoster = _pendingLootRoster;
            bool isStealing = _pendingIsStealing;
            var allItems = _pendingLootAllItems;

            _pendingLootCorpse = null;
            _pendingLootRoster = null;
            _pendingLootAllItems = null;

            // 尸体可能已被清理（换场景等）
            if (corpse == null) return;

            if (!isStealing)
            {
                // 搜刮 = 偷窃（发布前统一：死/活均走犯罪记账）
                if (allItems != null && allItems.Count > 0)
                {
                    var taken = new List<(string, string, int)>();
                    foreach (var grp in allItems.GroupBy(t => t.itemId))
                    {
                        int remaining = remainingRoster?.GetItemNumber(
                            TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<ItemObject>(grp.Key)) ?? 0;
                        int takenCount = grp.Count() - remaining;
                        for (int k = 0; k < takenCount; k++)
                            taken.Add((grp.Key, grp.First().itemName, 1));
                    }
                    if (taken.Count > 0)
                        StealManager.RecordUnconsciousLootTheft(corpse, taken, 0);
                }

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
                // 本地化：目标已被搜刮提示
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_already_looted", ("NAME", targetAgent.Name.ToString())), Colors.Red));
                return;
            }

            // --- 步骤一：计算产出 ---

            int villageGold = 0;   // 村庄分配金
            int clanGold = 0;      // 族长家族金库（非族长不算）
            CharacterObject character = targetAgent.Character as CharacterObject;
            bool isClanLeader = targetHero != null && targetHero.Clan?.Leader == targetHero;

            // 来源 1：村庄财富分配（所有 NPC 通用，全额）
            int allocatedGold = StealManager.GetAgentGold(targetAgent);
            if (allocatedGold > 0)
                villageGold = allocatedGold;

            // 来源 2：族长家族金库（上限 5000，防一次掏空全族资金——发布前平衡）
            if (isClanLeader && targetHero.Gold > 0)
                clanGold = Math.Min(targetHero.Gold, 5000);

            // 来源 3：回落随机（模板 NPC 无分配金且非族长时）
            if (villageGold == 0 && clanGold == 0 && character != null)
            {
                if (isStealing)
                    villageGold = MBRandom.RandomInt(1, 20);
                else
                    villageGold = character.IsHero ? (100 + character.Level * 50) : (character.Level * 5);
            }

            int lootedGold = villageGold + clanGold;

            // B. 构建物品列表 (ItemRoster) —— 分武器/防具
            ItemRoster weaponRoster = new ItemRoster();
            ItemRoster armorRoster = new ItemRoster();

            var equipmentToInspect = targetAgent.SpawnEquipment;
            string itemsName = "";
            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumEquipmentSetSlots; i++)
            {
                EquipmentElement element = equipmentToInspect[i];
                if (!element.IsEmpty && element.Item != null)
                {
                    bool isWeapon = i <= EquipmentIndex.Weapon3;
                    if (isWeapon)
                        weaponRoster.AddToCounts(element.Item, 1);
                    else
                        armorRoster.AddToCounts(element.Item, 1);
                    itemsName += element.Item.Name.ToString() + " ";
                }
            }
            ItemRoster lootRoster = new ItemRoster();
            lootRoster.Add(weaponRoster);
            lootRoster.Add(armorRoster);
            string partyItems = "";
            if(targetHero != null)
            {
                MobileParty party = targetHero.PartyBelongedTo;
                if(party!=null)
                {
                    var rst = party.ItemRoster;
                    if(rst!=null && rst.Count > 0)
                    {
                        // 本地化：目标还有东西在队伍里（战利品询问内容）
                        partyItems = LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_party_items", ("NAME", targetAgent.Name.ToString()), ("COUNT", rst.Count.ToString()));
                    }
                }
            }
            // 空空如也检查
            if (lootedGold == 0 && lootRoster.IsEmpty())
            {
                // 本地化：目标身上空空如也提示
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_nothing_on_target", ("NAME", targetAgent.Name.ToString())), Colors.Gray));
                return;
            }



            // --- 步骤二：构建 Inquiry (复用原有逻辑) ---
            // 本地化：战利品询问框动作名（偷窃/搜刮）
            string actionName = isStealing ? LWNTextHelper.ResolveText("LWN_ui_interact_loot_action_steal", "Steal") : LWNTextHelper.ResolveText("LWN_ui_interact_loot_action_loot", "Loot");
            // 本地化：战利品询问框标题
            string titleText = LWNTextHelper.ResolveCompound("LWN_ui_interact_loot_title", ("ACTION", actionName), ("NAME", targetAgent.Name.ToString()));
            // 本地化：战利品金币预览行
            string goldPreview = lootedGold > 0 ? LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_gold_line", ("GOLD", lootedGold.ToString())) : "";
            // 本地化：战利品询问框内容
            string contentText = LWNTextHelper.ResolveCompound("LWN_ui_interact_loot_content", ("NAME", targetAgent.Name.ToString()), ("ITEMS", itemsName), ("GOLD", goldPreview), ("PARTY", partyItems));

#if MB2_V1212
            bool showPickButton = targetAgent.IsActive();  // 只有活人偷窃时才有挑选界面；死人/昏迷不给
#else
            // Latest: InventoryManager.OpenScreenAsLoot 不可用，不显示"自己挑选"
            bool showPickButton = false;
#endif

            InformationManager.ShowInquiry(new InquiryData(
                titleText,
                contentText,
                true,
                showPickButton,
                // 本地化：战利品询问框按钮（全部拿走/自己挑选）
                LWNTextHelper.ResolveText("LWN_ui_interact_btn_take_all", "Take All"),
                // 自己挑选
                LWNTextHelper.ResolveText("LWN_ui_interact_btn_pick_yourself", "Pick Yourself"),
                () =>
                {
                    // 全部拿走回调
                    if (villageGold > 0)
                    {
                        int actual = StealManager.ConsumeAgentGold(targetAgent, villageGold, Settlement.CurrentSettlement);
                        if (actual > 0)
                            // 本地化：获得金币消息
                            InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_got_gold", ("GOLD", actual.ToString())), Colors.Yellow));
                    }
                    if (clanGold > 0 && targetHero != null)
                    {
                        int actual = AgentControlHelper.TransferGold(targetHero, Hero.MainHero, clanGold, notify: false);
                        if (actual > 0)
                            // 本地化：获得家族金库金币消息
                            InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_got_clan_gold", ("NAME", targetHero.Name.ToString()), ("GOLD", actual.ToString())), Colors.Yellow));
                    }
                    if (!lootRoster.IsEmpty())
                    {
                        MobileParty.MainParty.ItemRoster.Add(lootRoster);
                        // 本地化：获得物品数量消息
                        InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_got_items", ("COUNT", lootRoster.Count.ToString())), Colors.Green));
                    }
                    // 搜刮 = 偷窃：物品+金钱一次性记账（发布前统一：死/活均走犯罪记账）
                    if (!isStealing)
                        StealManager.RecordUnconsciousLootTheft(targetAgent, CollectEquipmentItems(targetAgent), lootedGold);
                    if (!isStealing) _lootedCorpses.Add(targetAgent); // 倒地目标搜空标记（死/昏迷均防重复搜刮）
                    StealManager.StripAgentEquipment(targetAgent, true, true);
                },
                () =>
                {
                    // 自己挑选回调
                    if (villageGold > 0)
                    {
                        int actual = StealManager.ConsumeAgentGold(targetAgent, villageGold, Settlement.CurrentSettlement);
                        if (actual > 0)
                            // 本地化：获得金币消息
                            InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_got_gold", ("GOLD", actual.ToString())), Colors.Yellow));
                    }
                    if (clanGold > 0 && targetHero != null)
                    {
                        int actual = AgentControlHelper.TransferGold(targetHero, Hero.MainHero, clanGold, notify: false);
                        if (actual > 0)
                            // 本地化：获得家族金库金币消息
                            InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_got_clan_gold", ("NAME", targetHero.Name.ToString()), ("GOLD", actual.ToString())), Colors.Yellow));
                    }

                    // 搜刮 = 偷窃：金钱在此刻已实际易手，立即记账（发布前统一：死/活均走犯罪记账）
                    if (!isStealing && lootedGold > 0)
                        StealManager.RecordUnconsciousLootTheft(targetAgent, null, lootedGold);

                    if (!lootRoster.IsEmpty())
                    {
                        // 死人/昏迷者（!IsActive）：武器已由引擎掉在地上，只放防具进库存。
                        // StripAgentEquipment 内置 agent.IsActive() 守卫，死人跳过
                        // UpdateSpawnEquipmentAndRefreshVisuals → 不会 AccessViolation。
                        bool isDead = !targetAgent.IsActive();
#if MB2_V1212
                        if (isDead)
                        {
                            // 死人：武器引擎已掉在地上，不管；只把防具进库存让玩家挑
                            if (!armorRoster.IsEmpty())
                            {
                                _pendingLootCorpse = targetAgent;
                                _pendingLootRoster = armorRoster;        // 只放防具
                                _pendingIsStealing = isStealing;
                                _pendingLootAllItems = CollectEquipmentItems(targetAgent);
                                var rosterDictionary = new Dictionary<PartyBase, ItemRoster>();
                                rosterDictionary.Add(PartyBase.MainParty, armorRoster);
                                InventoryManager.OpenScreenAsLoot(rosterDictionary);
                            }
                            else
                            {
                                // 没有防具，扒装备收尾（武器视觉清理）
                                if (!isStealing)
                                {
                                    _lootedCorpses.Add(targetAgent);
                                    StealManager.StripAgentEquipment(targetAgent, true, true);
                                }
                            }
                        }
                        else
                        {
                            // 活人：正常库存界面（偷窃场景）
                            _pendingLootCorpse = targetAgent;
                            _pendingLootRoster = lootRoster;
                            _pendingIsStealing = isStealing;
                            _pendingLootAllItems = CollectEquipmentItems(targetAgent);
                            var rosterDictionary = new Dictionary<PartyBase, ItemRoster>();
                            rosterDictionary.Add(PartyBase.MainParty, lootRoster);
                            InventoryManager.OpenScreenAsLoot(rosterDictionary);
                        }
#else
                        // Latest: InventoryManager.OpenScreenAsLoot 不可用，全部拿走
                        MobileParty.MainParty.ItemRoster.Add(lootRoster);
                        // 本地化：获得物品数量消息
                        InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_got_items", ("COUNT", lootRoster.Count.ToString())), Colors.Green));
                        if (!isStealing)
                            StealManager.RecordUnconsciousLootTheft(targetAgent, CollectEquipmentItems(targetAgent), lootedGold);
                        if (!isStealing) _lootedCorpses.Add(targetAgent);
                        StealManager.StripAgentEquipment(targetAgent, true, true);
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

        // ================================================================
        // 村庄保管箱 — 生成 + 互动 + 收尾
        // ================================================================

        /// <summary>
        /// 根据定居点类型返回保管箱相关文字（提示/标题/内容）。
        /// </summary>
        private static (string hintText, string title, string contentPrefix) GetChestTexts(ChestContext ctx)
        {
            return ctx switch
            {
                ChestContext.TownTavern => (
                    // 本地化：酒馆保管箱提示/标题/内容
                    LWNTextHelper.ResolveText("LWN_ui_chest_hint_tavern", "You notice a faintly golden storage barrel in the corner of the tavern, fitted with an old lock..."),
                    // 酒馆保管箱
                    LWNTextHelper.ResolveText("LWN_ui_chest_title_tavern", "Tavern Storage Chest"),
                    // 你找到了酒馆的保管箱。
                    LWNTextHelper.ResolveText("LWN_ui_chest_content_tavern", "You found the tavern's storage chest.")
                ),
                ChestContext.LordsHall => (
                    // 本地化：领主大厅保管箱提示/标题/内容
                    LWNTextHelper.ResolveText("LWN_ui_chest_hint_lordshall", "You notice a faintly golden storage chest in the corner of the hall, fitted with an old lock..."),
                    // 领主保管箱
                    LWNTextHelper.ResolveText("LWN_ui_chest_title_lordshall", "Lord's Storage Chest"),
                    // 你找到了领主的保管箱。
                    LWNTextHelper.ResolveText("LWN_ui_chest_content_lordshall", "You found the lord's storage chest.")
                ),
                ChestContext.TownCenter => (
                    // 本地化：城镇中心保管箱提示/标题/内容
                    LWNTextHelper.ResolveText("LWN_ui_chest_hint_towncenter", "You notice a faintly golden storage chest by the shops, fitted with an old lock..."),
                    // 城镇保管箱
                    LWNTextHelper.ResolveText("LWN_ui_chest_title_towncenter", "Town Storage Chest"),
                    // 你找到了城镇的保管箱。
                    LWNTextHelper.ResolveText("LWN_ui_chest_content_towncenter", "You found the town's storage chest.")
                ),
                ChestContext.Alley => (
                    // 本地化：暗巷保管箱提示/标题/内容
                    LWNTextHelper.ResolveText("LWN_ui_chest_hint_alley", "You notice a faintly golden storage barrel in the corner of the dark alley, fitted with an old lock..."),
                    // 暗巷保管箱
                    LWNTextHelper.ResolveText("LWN_ui_chest_title_alley", "Alley Storage Chest"),
                    // 你找到了暗巷的保管箱。
                    LWNTextHelper.ResolveText("LWN_ui_chest_content_alley", "You found the alley's storage chest.")
                ),
                ChestContext.Arena => (
                    "",
                    "",
                    ""
                ),
                ChestContext.Dungeon => (
                    "",
                    "",
                    ""
                ),
                ChestContext.Castle => (
                    // 本地化：城堡保管箱提示/标题/内容
                    LWNTextHelper.ResolveText("LWN_ui_chest_hint_castle", "You notice a faintly golden storage chest in the castle storeroom, fitted with an old lock..."),
                    // 城堡保管箱
                    LWNTextHelper.ResolveText("LWN_ui_chest_title_castle", "Castle Storage Chest"),
                    // 你找到了城堡的保管箱。
                    LWNTextHelper.ResolveText("LWN_ui_chest_content_castle", "You found the castle's storage chest.")
                ),
                ChestContext.Village => (
                    // 本地化：村庄保管箱提示/标题/内容
                    LWNTextHelper.ResolveText("LWN_ui_chest_hint_village", "You notice a faintly golden storage barrel by the headman's house, fitted with an old lock..."),
                    // 村庄保管箱
                    LWNTextHelper.ResolveText("LWN_ui_chest_title_village", "Village Storage Chest"),
                    // 你找到了村庄的保管箱。
                    LWNTextHelper.ResolveText("LWN_ui_chest_content_village", "You found the village's storage chest.")
                ),
                _ => (
                    // 本地化：默认保管箱提示/标题/内容
                    LWNTextHelper.ResolveText("LWN_ui_chest_hint_default", "You notice a faintly golden storage chest nearby, fitted with an old lock..."),
                    // 保管箱
                    LWNTextHelper.ResolveText("LWN_ui_chest_title_default", "Storage Chest"),
                    // 你找到了保管箱。
                    LWNTextHelper.ResolveText("LWN_ui_chest_content_default", "You found a storage chest.")
                )
            };
        }

        /// <summary>
        /// 在锚点 NPC 身后生成一个保管箱实体（酒馆→酒馆老板背后，村庄→村长背后，领主大厅→领主背后）。
        /// 策略：场景感知锚点 → 正后方 navmesh 验证取点 → 固定 prefab（bd_chest_c，0.5× 缩放）→ 高亮标记。
        /// 若扫描失败则回退到场景克隆；再失败则 CreateEmpty（仅功能，不可见）。
        /// </summary>
        private void SpawnSettlementChest()
        {
            var scene = Mission.Current?.Scene;
            if (scene == null) return;

            try
            {
                // 1. 找场景感知的 NPC 锚点，箱位优先取锚点正后方（柜台内侧/墙根）
                Agent anchor = StealManager.FindChestAnchorAgent();
                Vec3 chestPos = StealManager.ResolveChestSpawnPosition(scene, anchor);

                // 2. 生成保管箱可见实体：固定 prefab（bd_chest_c，实机选定）优先，场景克隆兜底
                _chestEntity = StealManager.SpawnStorageChestProp(scene, chestPos, anchor?.Position);

                // 3. 最终兜底：不可见标记点
                if (_chestEntity == null)
                {
                    MatrixFrame frame = new MatrixFrame(Mat3.Identity, chestPos);
                    _chestEntity = GameEntity.CreateEmpty(scene);
                    _chestEntity.SetGlobalFrame(frame);
                    DebugLogger.Log("[Chest] WARNING: No visible entity available — invisible marker");
                }
                else
                {
                    // 4. KCD2 风格视觉提示：给箱体加暖金微色调，让玩家能注意到
                    StealManager.ApplyChestHighlight(_chestEntity);
                }

                // 回填给 StealManager
                StealManager.ChestEntity = _chestEntity;
                DebugLogger.Log($"[Chest] Spawned at {chestPos}, context={StealManager.GetCurrentChestContext()}, gold={StealManager.StashGold}, items={StealManager.ChestItemRoster?.Count ?? 0}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Chest] SpawnSettlementChest error: {ex.Message}");
            }
        }

        /// <summary>玩家按 F 互动箱子时调用：空箱早退，否则先撬锁（StealBar Lockpick 模式）。</summary>
        private void OpenChest()
        {
            int gold = StealManager.StashGold;
            var roster = StealManager.ChestItemRoster;

            if (gold == 0 && (roster == null || roster.IsEmpty()))
            {
                // 本地化：空箱子提示
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText("LWN_ui_steal_msg_chest_empty", "The chest is empty."), Colors.Gray));
                return;
            }
            if (_stealLayer != null) return;

            var chestCtx = StealManager.GetCurrentChestContext();
            int pins = StealManager.GetLockpickPinCount(chestCtx);
            var (_, title, _) = GetChestTexts(chestCtx);

            // 撬锁条：pin 全开后由 TickStealBar 接 ShowChestInquiry
            // 本地化：撬锁条标题
            _stealBarVM = new StealBarVM(StealBarMode.Lockpick, pins, LWNTextHelper.ResolveCompound("LWN_ui_steal_lockpick_title", ("TITLE", title)), () => CloseStealInterface());
            _stealLayer = V.NewLayer(16);
            V.LoadMov(_stealLayer, "StealBar", _stealBarVM);
            _stealLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.Mouse);
            thisMissionScreen.AddLayer(_stealLayer);

            IsHandlingInteraction = true;
            _interactVM.IsVisible = false;
            StealManager.IsUIOpen = true;
            StartStealSlowmo();
            FreezePlayerControl(); // 同扒窃：冻结玩家输入（跳/走/攻击）
        }

        /// <summary>撬锁成功后弹出：全部拿走 / 自己挑选。IsUIOpen 贯穿 Inquiry + 战利品界面全程，loot 收尾才复位。</summary>
        private void ShowChestInquiry()
        {
            int gold = StealManager.StashGold;
            var roster = StealManager.ChestItemRoster;
            var settlement = Settlement.CurrentSettlement;

            // IsUIOpen 在 CloseStealInterface 中被复位，这里重新拉起，贯穿 loot 全程
            StealManager.IsUIOpen = true;

            // 本地化：开箱询问金币预览行
            string goldLine = gold > 0 ? LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_gold_line", ("GOLD", gold.ToString())) : "";
            string itemsPreview = "";
            if (roster != null)
            {
                for (int i = 0; i < Math.Min(roster.Count, 5); i++)
                {
                    var item = roster.GetItemAtIndex(i);
                    if (item != null)
                        itemsPreview += $"\n  {item.Name} x{roster.GetElementNumber(i)}";
                }
                // 本地化：开箱物品预览省略行
                if (roster.Count > 5) itemsPreview += LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_chest_more_items", ("COUNT", (roster.Count - 5).ToString()));
            }

            var chestCtx = StealManager.GetCurrentChestContext();
            var (_, title, contentPrefix) = GetChestTexts(chestCtx);
            // 本地化：开箱询问框内容（物品标签）
            string content = LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_chest_items_label", ("PREFIX", contentPrefix), ("GOLD", goldLine), ("ITEMS", itemsPreview));

            InformationManager.ShowInquiry(new InquiryData(
                title, content,
                true, true,
                // 本地化：开箱询问框按钮（全部拿走/自己挑选）
                LWNTextHelper.ResolveText("LWN_ui_interact_btn_take_all", "Take All"), LWNTextHelper.ResolveText("LWN_ui_interact_btn_pick_yourself", "Pick Yourself"),
                () =>
                {
                    // ── 全部拿走 ──
                    int takenGold = 0;
                    if (gold > 0)
                    {
                        takenGold = StealManager.LootStash(gold, settlement);
                        if (takenGold > 0)
                            // 本地化：开箱获得金币消息
                            InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_got_gold", ("GOLD", takenGold.ToString())), Colors.Yellow));
                    }

                    var takenItems = new List<(string itemId, string itemName, int count)>();
                    if (roster != null && !roster.IsEmpty())
                    {
                        int totalItems = 0;
                        for (int i = roster.Count - 1; i >= 0; i--)
                        {
                            var item = roster.GetItemAtIndex(i);
                            int count = roster.GetElementNumber(i);
                            if (item != null && count > 0)
                            {
                                int taken = StealManager.LootChestItem(item, count, settlement);
                                totalItems += taken;
                                if (taken > 0)
                                    takenItems.Add((item.StringId, item.Name?.ToString() ?? item.StringId, taken));
                            }
                        }
                        if (totalItems > 0)
                            // 本地化：开箱获得物品数量消息
                            InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_got_items", ("COUNT", totalItems.ToString())), Colors.Green));
                    }

                    // 犯罪统一接线：目击检测 → 证词 → 围堵质问
                    if (settlement != null && (takenGold > 0 || takenItems.Count > 0))
                        StealManager.RecordChestTheft(settlement, takenItems, takenGold);

                    RemoveChestEntityIfEmpty();
                    StealManager.IsUIOpen = false; // loot 收尾完成
                },
                () =>
                {
                    // ── 自己挑选 ──
                    // 金币先入待记账（与物品一起在 ProcessPendingChestLoot 记录一次）
                    _pendingChestGold = 0;
                    if (gold > 0)
                    {
                        _pendingChestGold = StealManager.LootStash(gold, settlement);
                        if (_pendingChestGold > 0)
                            // 本地化：开箱挑选金币入账消息
                            InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_got_gold", ("GOLD", _pendingChestGold.ToString())), Colors.Yellow));
                    }

                    if (roster != null && !roster.IsEmpty())
                    {
#if !MB2_V1212
                        DebugLogger.Log("[Chest] InventoryManager not available in this version, taking all items instead");
                        // Fallback: take everything
                        var takenItems = new List<(string itemId, string itemName, int count)>();
                        int totalItems = 0;
                        for (int i = roster.Count - 1; i >= 0; i--)
                        {
                            var item = roster.GetItemAtIndex(i);
                            int count = roster.GetElementNumber(i);
                            if (item != null && count > 0)
                            {
                                int taken = StealManager.LootChestItem(item, count, settlement);
                                totalItems += taken;
                                if (taken > 0)
                                    takenItems.Add((item.StringId, item.Name?.ToString() ?? item.StringId, taken));
                            }
                        }
                        if (totalItems > 0)
                            // 本地化：开箱获得物品数量消息
                            InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_steal_msg_got_items", ("COUNT", totalItems.ToString())), Colors.Green));
                        if (settlement != null && (_pendingChestGold > 0 || takenItems.Count > 0))
                            StealManager.RecordChestTheft(settlement, takenItems, _pendingChestGold);
                        _pendingChestGold = 0;
                        RemoveChestEntityIfEmpty();
                        StealManager.IsUIOpen = false;
#else
                        // 保存快照用于比较
                        _pendingChestSnapshot = StealManager.CloneItemRoster(roster);

                        // 打开战利品界面（roster 会被原地修改）
                        var dict = new Dictionary<PartyBase, ItemRoster>();
                        dict[PartyBase.MainParty] = roster;
                        InventoryManager.OpenScreenAsLoot(dict);

                        // 标记待处理（ProcessPendingChestLoot 收尾 + 记账 + IsUIOpen 复位）
                        _chestLootPending = true;
#endif
                    }
                    else
                    {
                        // 纯金无物品：不开战利品界面，立即记账收尾
                        if (settlement != null && _pendingChestGold > 0)
                            StealManager.RecordChestTheft(settlement, new List<(string, string, int)>(), _pendingChestGold);
                        _pendingChestGold = 0;
                        RemoveChestEntityIfEmpty();
                        StealManager.IsUIOpen = false;
                    }
                }));
        }

        /// <summary>箱子空了就移除实体（两条 loot 路径共用）。</summary>
        private void RemoveChestEntityIfEmpty()
        {
            if (_chestEntity != null && StealManager.StashGold == 0
                && (StealManager.ChestItemRoster == null || StealManager.ChestItemRoster.IsEmpty()))
            {
                _chestEntity.Remove(0);
                _chestEntity = null;
                StealManager.ChestEntity = null;
            }
        }

        /// <summary>
        /// 箱子"自己挑选"战利品界面关闭后的收尾：
        /// 比较快照找出玩家拿走的物品，同步扣除定居点 ItemRoster，
        /// 物品 + 暂存金币一次记账（RecordChestTheft），IsUIOpen 复位。
        /// </summary>
        private void ProcessPendingChestLoot()
        {
            _chestLootPending = false;
            var snapshot = _pendingChestSnapshot;
            _pendingChestSnapshot = null;

            var settlement = Settlement.CurrentSettlement;
            int takenGold = _pendingChestGold;
            _pendingChestGold = 0;
            if (settlement == null || snapshot == null)
            {
                StealManager.IsUIOpen = false;
                return;
            }

            var remaining = StealManager.ChestItemRoster;
            var takenItems = new List<(string itemId, string itemName, int count)>();

            try
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var item = snapshot.GetItemAtIndex(i);
                    if (item == null) continue;
                    int before = snapshot.GetElementNumber(i);
                    int after = remaining?.GetItemNumber(item) ?? 0;
                    int taken = before - after;
                    if (taken > 0)
                    {
                        StealManager.DeductSettlementItemsOnly(settlement, item, taken);
                        takenItems.Add((item.StringId, item.Name?.ToString() ?? item.StringId, taken));
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Chest] ProcessPendingChestLoot error: {ex.Message}");
            }

            // 犯罪统一接线：目击检测 → 证词 → 围堵质问
            if (takenGold > 0 || takenItems.Count > 0)
                StealManager.RecordChestTheft(settlement, takenItems, takenGold);

            RemoveChestEntityIfEmpty();
            StealManager.IsUIOpen = false; // loot 收尾完成
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
                // 0. 战斗/训练场/竞技场等本 mod 交互整体关闭的场景：放行原版 F 对话
                if (Settings.Instance.IsInteractionDisabled())
                    return true;

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
                if (Settings.Instance.ShowDebugMessages)
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
            // 战斗/教程/竞技场等本 mod 交互关闭的场景：保留原版"按F对话"提示
            if (Settings.Instance.IsInteractionDisabled()) return;
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
            // 战斗/教程/竞技场等本 mod 交互关闭的场景：保留原版"按F对话"提示
            if (Settings.Instance.IsInteractionDisabled()) return;
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

#if MB2_V1212
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
#endif

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
