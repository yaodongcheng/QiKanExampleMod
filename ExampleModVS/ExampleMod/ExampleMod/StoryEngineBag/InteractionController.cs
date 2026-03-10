using ExampleMod;
using ExampleMod.AI;
using Microsoft.VisualBasic.Devices;
using Microsoft.VisualBasic.FileIO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Library.NewsManager;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ViewModelCollection.GameOptions;
using TaleWorlds.TwoDimension;
using static ExampleMod.PromptBuilder;
namespace ExampleMod.StoryEngineBag
{

    public static class ActionHandler
    {
        // 定义一个动作的结构，包含：代码、描述、判断条件、执行逻辑
        private class ActionDefinition
        {
            public string Code { get; set; }
            public string Description { get; set; }
            // [修改] 条件委托：传入NPC(可能为null), Player, Agent。增加Agent是为了辅助判断
            public Func<Hero, Hero, Agent, bool> IsValid { get; set; }
            // 执行委托：传入 actionCode, NPC, Player, Agent
            public Action<Hero, Hero, Agent> Execute { get; set; }
        }

        // 静态列表存储所有可能的动作
        private static readonly List<ActionDefinition> _actions = new List<ActionDefinition>();

        // 静态构造函数，程序启动时初始化动作列表
        static ActionHandler()
        {
            InitializeActions();
        }

        private static void InitializeActions()
        {
            // 1. 默认动作 NONE
            _actions.Add(new ActionDefinition
            {
                Code = "NONE",
                Description = "默认无动作，仅进行对话。",
                IsValid = (npc, player, agent) => true,
                Execute = (n, p, a) => { /* Do nothing */ }
            });

            // 1. 默认动作 NONE
            _actions.Add(new ActionDefinition
            {
                Code = "INCREASE_RELATION",
                Description = "好感度小幅上升。",
                IsValid = (npc, player, agent) => npc != null,
                Execute = (n, p, a) => {

                    if (n != null)
                    {
                        ChangeRelationAction.ApplyPlayerRelation(n, 5, true, true);
                    }
                }
            });

            // 1. 默认动作 NONE
            _actions.Add(new ActionDefinition
            {
                Code = "DECREASE_RELATION",
                Description = "好感度小幅下降。",
                IsValid = (npc, player, agent) => npc != null,
                Execute = (n, p, a) => {
                    if (n != null)
                    {
                        ChangeRelationAction.ApplyPlayerRelation(n, -5, true, true);
                    }
                }
            });

            // 2. 攻击动作 ATTACK
            _actions.Add(new ActionDefinition
            {
                Code = "ATTACK",
                Description = "恼羞成怒，发起攻击（进入战斗）。",
                IsValid = (npc, player, agent) => agent != null,
                Execute = (npc, player, agent) =>
                {
                    // 获取名字：如果有Hero用Hero名，否则用Agent名
                    string targetName = npc != null ? npc.Name.ToString() : agent.Name.ToString();

                    Action confirmFight = () => {
                        AgentAIController.Instance.SendEventToAgent(agent, "order_attack", Agent.Main);
                        // 尝试关闭对话UI (根据你的Mod实现可能需要调整)
                        if (InteractionController.Instance != null && InteractionController.Instance._vm != null)
                        {
                            InteractionController.Instance._vm.Close();
                        }

                    };

                    InformationManager.ShowInquiry(new InquiryData("危险", $"{targetName} 即将对你发起攻击", true, false, "来战！", null, confirmFight, null));
                }
            });
            _actions.Add(new ActionDefinition
            {
                Code = "DUEL",
                Description = "和平的交手切磋（进入不致命的战斗）。",
                IsValid = (npc, player, agent) => agent != null,
                Execute = (npc, player, agent) =>
                {
                    // 获取名字：如果有Hero用Hero名，否则用Agent名
                    string targetName = npc != null ? npc.Name.ToString() : agent.Name.ToString();

                    Action confirmFight = () => {
                        // 尝试关闭对话UI (根据你的Mod实现可能需要调整)
                        if (InteractionController.Instance != null && InteractionController.Instance._vm != null)
                        {
                            InteractionController.Instance._vm.Close();
                        }
                        AgentAIController.Instance.SendEventToAgent(agent, "order_attack", Agent.Main);
                        // 开启战斗
                    };

                    InformationManager.ShowInquiry(new InquiryData("提示", $"{targetName} 想与你切磋一场，你准备好了吗？", true, false, "来战！", null, confirmFight, null));
                }
            });

            // 3. 结婚动作 MARRY_SUCCESS
            _actions.Add(new ActionDefinition
            {
                Code = "MARRY_SUCCESS",
                Description = "同意对方的求婚（建立婚姻关系）。",
                // 条件：异性 + 双方单身 + 年龄合适(可选)
                IsValid = (npc, player, agent) =>
                {
                    // [关键修改] 守卫不能结婚，必须是 Hero
                    if (npc == null) return false;

                    bool differentGender = npc.IsFemale != player.IsFemale;
                    // 注意：这里移除了临时的 true 赋值，恢复逻辑
                    bool npcSingle = npc.Spouse == null;
                    bool playerSingle = player.Spouse == null;

                    return differentGender && npcSingle && playerSingle;
                },
                Execute = (npc, player, agent) =>
                {
                    if (npc != null)
                    {
                        MarriageAction.Apply(player, npc);
                        InformationManager.DisplayMessage(new InformationMessage($"{npc.Name} 接受了你的求婚！", Colors.Green));
                    }
                }
            });

            // 4. 招募动作 JOIN_CLAN
            _actions.Add(new ActionDefinition
            {
                Code = "JOIN_CLAN",
                Description = "接受招募，加入玩家的家族。",
                // 条件：NPC是流浪者(Wanderer) 或者 没有家族
                IsValid = (npc, player, agent) =>
                {
                    if (npc == null) return false; // 守卫通常不能直接变为家族成员
                    return npc.Clan == null || npc.IsWanderer;
                },
                Execute = (npc, player, agent) =>
                {
                    if (npc != null)
                    {
                        // 具体的招募逻辑 (示例)                        
                        InformationManager.DisplayMessage(new InformationMessage($"{npc.Name} 加入了你的家族。", Colors.Blue));
                    }
                }
            });

            // ... 在这里扩展更多动作
        }

        /// <summary>
        /// 获取当前可用的动作空间 Prompt
        /// </summary>
        public static string GetActionSpacePrompt(Hero npcHero, Hero playerHero, Agent npcAgent)
        {
            StringBuilder sb = new StringBuilder();

            // [修改] 传入 agent 进行判断
            var availableActions = _actions.Where(a => a.IsValid(npcHero, playerHero, npcAgent));

            foreach (var action in availableActions)
            {
                sb.AppendLine($"- \"{action.Code}\": {action.Description}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 执行动作
        /// </summary>
        public static void HandleAction(string actionCode, Hero npcHero, Hero playerHero, Agent npcAgent)
        {
            if (string.IsNullOrEmpty(actionCode)) return;

            // 查找对应的动作定义
            var actionDef = _actions.FirstOrDefault(a => a.Code.ToUpper() == actionCode.ToUpper());

            if (actionDef != null)
            {
                // [修改] 再次校验时传入 Agent
                if (actionDef.IsValid(npcHero, playerHero, npcAgent))
                {
                    // 即使 npcHero 为 null，Execute 方法内部现在也能处理了
                    actionDef.Execute(npcHero, playerHero, npcAgent);
                }
                else
                {
                    InformationManager.DisplayMessage(new InformationMessage($"无法执行动作: {actionCode} (条件不满足)", Colors.Red));
                }
            }
            else
            {
                // 处理未知的动作代码
            }
        }
    }

    public class InteractionController
    {
        public StoryDialogVM _vm;
        private InteractionOptionManager _optionManager;
        private Agent _targetAgent;
        private Hero _targetHero;
        public SingNpcMemorySystem _memory;
        public  DialogueActionMatcher _matcher;
        public static InteractionController Instance ;
        public double InteractBeginTimeStamp = 0;
        // 标记是否正在等待 AI 回复，防止连点
        private bool _isProcessing = false;

        private DraftProposal _draftProposal = new DraftProposal();

        private List<NegotiationCard> _lastRoundCards;
        // 标记当前是否处于“读心”状态
        private bool _isReadingMind = false;
        // [新增] 缓存当前回合的文本，用于读心切换
        private string _cachedCurrentReply = "";
        private string _cachedCurrentThinking = "";
        public LLMResponse_Casual currentCasualResponse = null;

        // [新增] 为了支持层级菜单，我们需要一个状态来标记当前是在哪一层
        private enum MenuState { Root, AddWager, RemoveWager, CategorySelect, ItemSelect }



        public InteractionController(StoryDialogVM vm)
        {
            _vm = vm;
            _matcher = new DialogueActionMatcher();
            InteractBeginTimeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _optionManager = new InteractionOptionManager(this);
            Instance = this;
        }
        

        // 1. 开始对话：传入 Agent
        public void StartInteraction(Agent target)
        {
            //没有对应hero的不能对话，交互键拦截

            if (target == null) return;





            _targetAgent = target;
            _targetHero = (_targetAgent.Character as CharacterObject)?.HeroObject;

            _memory = AllNpcMemoryManager.GetMemoryForAgent(target);

            _memory.CurrentNegotiationState = null;
            // 2. [关键] 在对话开始前，检查是否有由于新闻传播导致的潜在冲突
            bool hasActiveConflict = _memory.CheckAndGeneratePersuadeInfo();

            string displayName = _targetAgent.Name.ToString();
            string initialText = "";

            var initiative = _memory.CurrentInitiative;
            if (initiative != null && initiative.IsReady)
            {
                ProcessOpeningResponse();
            }
            else
            {
                // === 正常模式 ===
                initialText = "（看着你，似乎在揣测你的想法...）";
                _vm.Show(displayName, initialText);

                // 生成通用选项
                /*
                var options = GenerateInitialIntents(target);
                _vm.ShowOptions(options.ToArray());
                */
                RefreshInitialOptions();
            }

            

        }
       

        // 生成初始意图逻辑
        private void RefreshInitialOptions()
        {
            // 1. 获取数据定义
            var defs = _optionManager.GetAvailableOptions(_targetHero, _targetAgent);
            var vmOptions = new List<StoryOptionVM>();

            // 2. 转换为 VM
            foreach (InteractionDefinition def in defs)
            {
                vmOptions.Add(new StoryOptionVM(
                    def.DisplayName,
                    () => def.OnAction(_targetHero, _targetAgent), // 传入自身(this)
                    def.ToolTip
                ));
            }

            // 这里的 Reverse 取决于 UI 是从下往上排还是从上往下，保持原样
            vmOptions.Reverse();
            _vm.ShowOptions(vmOptions.ToArray());
        }


        public void SendIntent(string intentType, string playerInput)
        {
            if (_isProcessing) return; // 防止重复点击

            NegotiationTactic ThisIntent;
            if (Enum.TryParse<NegotiationTactic>(intentType, true, out var result))
            {
                ThisIntent = result;
            }
            else
            {
                ThisIntent = NegotiationTactic.Flatter;
            }



            var virtualCard = new NegotiationCard(intentType, playerInput);                    

            _ = Task.Run(() => HandlePlayerInputAsync(playerInput, virtualCard));

          
        }
       
        private async Task<string> HandlePlayerSkillCheck(SkillCheckOption skillCheckOption)
        {
            string jsonResponse = "";
            if (skillCheckOption != null)
            {

                // A2. 核心机制：C# 掷骰子
                float roll = MBRandom.RandomFloat;
                bool isSuccess = roll < skillCheckOption.SuccessChance;
                lock (_memory) { _memory.AddHistory("user", $"(尝试检定: {skillCheckOption.Text}) 结果: {(isSuccess?"成功":"失败")}"); }
                // A3. 奖励经验值
                if (isSuccess && Hero.MainHero != null && skillCheckOption.RelatedSkill != null)
                {
                    Hero.MainHero.AddSkillXp(skillCheckOption.RelatedSkill, 50);
                }

                // A4. 构建 Prompt (告诉 LLM 刚才发生了什么，是成功还是失败)
                string skillPrompt = PromptBuilder.BuildSkillCheckResponsePrompt(_memory, skillCheckOption, isSuccess, _targetAgent);

                try
                {
                    jsonResponse = await LLMService.Instance.ChatAsync(skillPrompt, 500, true);
                    jsonResponse = LLMService.CleanJson(jsonResponse);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"LLM SkillCheck Error: {ex.Message}");
                    // 错误处理...
                    _isProcessing = false;
                    return "";
                }

                // A6. 专门的处理函数：只用 LLM 的文本，选项由 C# 硬编码
                ProcessSkillCheckResponse(jsonResponse, isSuccess, skillCheckOption);

                _isProcessing = false;
            }
            return jsonResponse;
        }

        //新的处理玩家输入，玩家输入本质上是和Npc进行协商博弈，利益交换
        public async Task<string> HandlePlayerInputAsync(string playerInput, NegotiationCard selectedOption = null, SkillCheckOption skillCheckOption = null)
        {
            // 1. 基础 UI 锁定与镜头处理
            if (_isProcessing) return "";
            _isProcessing = true;
            _vm.AreOptionsVisible = false; // 隐藏选项防止重复点击
            _vm.LockPrediction();
            VisualCommands.SmartCamera(Agent.Main, _targetAgent); // 镜头给玩家
            _vm.Show(Agent.Main.Name.ToString(), playerInput);
            NegotiationState state = _memory.CurrentNegotiationState;
            string npcName = _memory._profile.Name;
            string playerEmotion = "Neutral";
            if (selectedOption != null)            
                playerEmotion = selectedOption.Emotion;
            else if(skillCheckOption != null)
                playerEmotion = skillCheckOption.Emotion;
            AgentControlHelper.SetPose(Agent.Main, _matcher.GetAnimByEmotion(playerEmotion)); //玩家基于情绪做动作

            //分支A，技能鉴定
            if (skillCheckOption != null)
            {
                return await HandlePlayerSkillCheck(skillCheckOption);
            }
            //分支B，谈判模式           
            if (state != null)
            {
                //比较复制前后的chip的价值
                float beforeValue = selectedOption.Chips.Sum(x => x.EstimatedValue);
                state.LastTurnAddedChips = new List<Chip>(selectedOption.Chips);
                float afterValue = state.LastTurnAddedChips.Sum(x => x.EstimatedValue);
                DebugLogger.Log($"筹码价值变化：{beforeValue} -> {afterValue}");
                // 加入到累积池
                state.CommittedChips.AddRange(selectedOption.Chips);
                _draftProposal.Clear();
            }            

            if (selectedOption != null) 
            {
                AgentControlHelper.SetPose(Agent.Main, _matcher.GetAnimByEmotion(selectedOption.Emotion)); 
            }
            // 1. 记录玩家发言到历史
            lock (_memory)
            {
                _memory.AddHistory("user", $"{Hero.MainHero.Name}: {playerInput}");
                if (selectedOption != null && selectedOption.CostAmount != 0)
                {
                    string his = ($"玩家投入筹码：");
                    foreach (Chip oneChip in selectedOption.Chips)
                    {
                        his += ($"{oneChip.Amount}份{oneChip.Type}");
                    }
                    _memory.AddHistory("system", his);
                }
            }

            // 1. 执行代价扣除 (如果是谈判模式，且玩家选了牌)
            if (_memory.CurrentNegotiationState != null && selectedOption != null)
            {
            }            
            PlayerResources playerRes = _memory.CurrentNegotiationState?.playerResources ?? null;
            // 3. 构建 Prompt
            string prompt = PromptBuilder.BuildPromptForNegoAndChat(_memory, playerInput, playerRes, selectedOption,_targetAgent);
            string jsonResponse = "";
            try
            {
                jsonResponse = await LLMService.Instance.ChatAsync(prompt, 500, true);
                jsonResponse = LLMService.CleanJson(jsonResponse); // 清洗可能存在的 markdown 符号
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"LLM Error: {ex.Message}");
                _vm.DialogueContent = $"（{npcName}似乎走神了...）";
                _vm.ShowOptions(new[] { new StoryOptionVM("离开", () => { _vm.Close(); 
                //    AgentAIController.Instance.SendEventToAgent(_targetAgent, "EndInteraction", Agent.Main);

                AgentAIController.Instance.BroadcastEventInRange(Agent.Main.Position,15.0f,"EndInteraction",Agent.Main);
                GroupStageManager.Reset(Agent.Main);
                }) });
                _isProcessing = false; // 解锁
                return "";
            }


            string finalResult = "";
            // 5. 解析并更新状态
            if (_memory.CurrentNegotiationState != null)
            {
                finalResult=  ProcessNegotiationResponse(jsonResponse,selectedOption);
            }
            else
            {
                finalResult =  ProcessCasualResponse(jsonResponse);
            }
            _isProcessing = false; // 解锁
            return finalResult;

        }
        private string GetIconByTraitId(string id, bool isRevealed)
        {
            if (!isRevealed) return "StdAssets\\lock_closed";
            // 可以在这里根据 trait ID 返回具体的图标，比如贪婪是金币图标
            if (id.Contains("Greedy")) return "StdAssets\\icon_gold";
            return "StdAssets\\lock_opened";
        }
        private string GetColorByPolarity(TraitPolarity polarity)
        {
            switch (polarity)
            {
                case TraitPolarity.Weakness: return "#44FF44FF"; // 绿色 (弱点)
                case TraitPolarity.Resistance: return "#FF4444FF"; // 红色 (阻力)
                case TraitPolarity.Immunity: return "#888888FF"; // 灰色 (无效)
                default: return "#FFFFFF"; // 白色 (中性)
            }
        }
        private void RefreshTraitUI(NegotiationState state)
        {
            if (state == null) return;

            // 清空旧列表
            _vm.TraitList.Clear();

            foreach (var trait in state.ActiveTraits)
            {
                // 判断是否解锁 (IsSecret). 
                // 逻辑：如果 IsSecret 为 true，则 UI 显示为未解锁状态
                // 你可以在 NegotiationState 里维护一个 "已发现的Trait ID列表" 来动态解锁
                // 这里暂时简单处理：默认都显示，或者根据 trait.IsSecret 判断

                bool isRevealed = !trait.IsSecret;
                // 创建 VM
                var traitVM = _vm.GenerateTrait(trait.Name,  trait.Description, isRevealed);             

                // 设置颜色 (根据 Polarity)
                traitVM.TraitColor = GetColorByPolarity(trait.Polarity);

                // 设置图标 (可以根据 ID 映射不同的 Sprite，或者用通用的)
                traitVM.IconSprite = GetIconByTraitId(trait.ID, isRevealed);

                _vm.TraitList.Add(traitVM);
            }
        }


        private void ToggleReadThinking(NegotiationState state)
        {

            var mindBtn = _vm.OptionList.FirstOrDefault(opt => opt.Identifier == "MIND_READING");
            if (state == null)
            { 
                if(currentCasualResponse != null)
                {
                    if (!_isReadingMind && !string.IsNullOrEmpty(currentCasualResponse.NpcThinking))
                    {
                        // 读心模式：显示内心独白
                        // 加一些特殊的格式让玩家一眼看出这是心里话
                        _vm.DialogueContent = currentCasualResponse.NpcThinking;
                        mindBtn.OptionText = "【读心】查看明面回复";
                        _isReadingMind = true;
                    }
                    else
                    {
                        // 正常模式：显示明面回复
                        _vm.DialogueContent = currentCasualResponse.NpcReply;
                        _isReadingMind = false;
                        mindBtn.OptionText = "【读心】查看内心独白";
                    }
                }
                
                return; 
            
            
            
            }
            NegotiationTurnLog _currentLog = state.TurnHistory.LastOrDefault();
            if (_currentLog == null) return;

            if (!_isReadingMind && !string.IsNullOrEmpty(_currentLog.NpcThinking))
            {
                // 读心模式：显示内心独白
                // 加一些特殊的格式让玩家一眼看出这是心里话
                _vm.DialogueContent = _currentLog.NpcThinking;
                mindBtn.OptionText = "【读心】查看明面回复";
                _isReadingMind = true;
            }
            else
            {
                // 正常模式：显示明面回复
                _vm.DialogueContent = _currentLog.NpcReply;
                _isReadingMind=false;
                mindBtn.OptionText = "【读心】查看内心独白";
            }



        }
        private string ProcessNegotiationResponse(string json, NegotiationCard selectedOption=null)
        {
            LLMResponse_Negotiation result;
            try
            {
                result = JsonConvert.DeserializeObject<LLMResponse_Negotiation>(json);
            }
            catch
            {
                // 如果解析失败，回退到基础处理
                _vm.DialogueContent = "（对方的话语有些含糊不清...）";
                UpdateUiForNextTurn(new List<NegotiationCard>(), false);
                return json;
            }


            NegotiationState state = _memory.CurrentNegotiationState;
            //这个下面处理了显示字幕的逻辑
            UpdateNpcVisuals(result.NpcReply, result.NpcEmotion, result.NpcAction,result.NpcThinking);
            float calculatedDelta = 0f;
            float finalMultiplier = 1.0f; // 默认为 1
            string tacticDesc= "付出了";
            float chipsValue = 0f;



            if (selectedOption != null)
            {
                // 限制 Multiplier 范围，防止 LLM 发疯
                finalMultiplier = Mathf.Clamp(result.DeltaMultiplier, 0.5f, 2.0f);
                float tacticBaseScore = state.TargetThreshold*0.02f;//比如嘴炮基础分
                chipsValue = selectedOption.Chips.Sum(x => x.EstimatedValue);
                calculatedDelta = (tacticBaseScore + chipsValue) * finalMultiplier;

                
                foreach (var oneChip in selectedOption.Chips)
                {
                    tacticDesc += $"{oneChip.Amount}个{oneChip.Type}";
                }
                
                InformationManager.DisplayMessage(new InformationMessage($"【谈判计算】\n牌面效果：{tacticBaseScore}\n筹码加成：{chipsValue}\nLLM 乘数：{finalMultiplier} 最终得分：{calculatedDelta}"));
                DebugLogger.Log($"【谈判计算】牌面效果：{tacticBaseScore} 筹码加成：{chipsValue} LLM 乘数：{finalMultiplier} 最终得分：{calculatedDelta}");
            }
            else
            {
                // 玩家可能没选牌（比如刚进谈判的过渡回合），不增加进度
                calculatedDelta = 0;
            }
            float oldProgress;
            if (state.TurnCount == -1)
            {
                oldProgress = 0;
                calculatedDelta = state.CurrentProgress;
                RefreshTraitUI(state);
            }
            else
            {
                oldProgress = state.CurrentProgress;
                state.CurrentProgress = oldProgress + calculatedDelta;
            }
            DebugLogger.Log($"进度条发生变化，{oldProgress} + {calculatedDelta}-> {state.CurrentProgress}");
            var turnLog = new NegotiationTurnLog
            {
                TurnIndex = state.TurnCount,
                PlayerInput = selectedOption.Text,
                PlayerTactic = tacticDesc,
                ChipValue = chipsValue,
                ProgressDelta = calculatedDelta,
                NpcReply = result.NpcReply,
                NpcThinking = result.NpcThinking,
                FeedbackMultiplier = finalMultiplier, // 记录 NPC 的真实态度反馈
                ResultingProgressRatio = 100 * state.CurrentProgress / state.TargetThreshold
            };
            state.TurnHistory.Add(turnLog);
            state.TurnCount++;
            // 2. 处理阻力 Tag 变更


            float predictedGain = selectedOption.Chips.Sum(x => x.EstimatedValue);
            float predictedTotalOnUI = oldProgress + predictedGain;
            
            /*
            _=  _vm.AnimateProgressTo(
                oldProgress,
                state.CurrentProgress,
                predictedTotalOnUI,
                state.TargetThreshold
            );
            */
            _vm.UpdateConflictStatus(state, predictedTotalOnUI, true);
            

            bool isWin = state.CurrentProgress >= state.TargetThreshold;
            bool isLoss = state.TurnCount >= state.MaxTurns && !isWin;

            if (isWin || isLoss )
            {
                _memory.CurrentNegotiationState = null; // 清除状态

                string endText = isWin ? "【谈判达成】" : "【谈判破裂】";

                // 结束后的选项
                var endOptions = new List<StoryOptionVM>();
                if (isWin)
                {
                    endOptions.Add(new StoryOptionVM($"{_targetHero.Name}被你说服了", () =>
                    {
                        ExecuteTransaction(_draftProposal);
                       // AgentAIController.Instance.SendEventToAgent(_targetAgent, "EndInteraction", Agent.Main);

                        AgentAIController.Instance.BroadcastEventInRange(Agent.Main.Position, 15.0f, "EndInteraction", Agent.Main);
                        GroupStageManager.Reset(Agent.Main);
                        _vm.Close();
                    }));
                }
                else
                {
                    endOptions.Add(new StoryOptionVM($"{_targetHero.Name}耐心已耗尽，遗憾离开", () => { _vm.Close(); AgentAIController.Instance.SendEventToAgent(_targetAgent, "EndInteraction", Agent.Main); }));
                }
                if (_memory.ActiveConflict != null)
                    _memory.ActiveConflict = null; // [关键] 清除标记，避免死循环
                _vm.ShowOptions(endOptions.ToArray());
                _memory.CurrentNegotiationState = null; // 清除状态
            }
            else
            {
                // 谈判继续，生成下一轮卡牌
                UpdateUiForNextTurn(result.NextRoundCards, true);
            }

            return JsonConvert.SerializeObject(result);


           
        }
        public void ProcessOpeningResponse()
        {
            var initiative = _memory.CurrentInitiative;

            // 1. 安全检查
            if (initiative == null || !initiative.IsReady || initiative.CachedOpening == null)
                return;

            var openingData = initiative.CachedOpening;
            UpdateNpcVisuals(openingData.NpcReply, openingData.NpcEmotion, openingData.NpcAction, openingData.NpcThinking);
            // 3. 构建开场专属的选项列表
            var options = new List<StoryOptionVM>();

            // 遍历 LLM 生成的 3 个选项
            foreach (SkillCheckOption checkOpt in openingData.PlayerNextOptions)
            {
                string skillName = checkOpt.RelatedSkill.Name.ToString();
                string chanceText = $"{(checkOpt.SuccessChance * 100):0}%";
                string btnTitle = $"[{skillName} {chanceText}] {checkOpt.Text} ({checkOpt.TacticRaw})";
                string tooltip = $"预期后果: {checkOpt.Prediction}";
                var vmOption = new StoryOptionVM(btnTitle, async () =>
                {
                    await HandlePlayerInputAsync(checkOpt.Text, null, checkOpt);
                }, tooltip);

                options.Add(vmOption);               
            }

            // 4. 添加一个兜底的战斗选项 (防止卡死)
            options.Add(new StoryOptionVM("【拔刀】多说无益", () =>
            {
                _vm.Close();
                AgentAIController.Instance.SendEventToAgent(_targetAgent, "order_attack", Agent.Main);
                Agent.Main.TryToWieldWeaponInSlot(EquipmentIndex.WeaponItemBeginSlot, Agent.WeaponWieldActionType.WithAnimation, false);
            }, "放弃交涉，直接动手"));

            options.Add(new StoryOptionVM("【读心】查看内心独白", () => {

                ToggleReadThinking(_memory.CurrentNegotiationState);
            }, "尝试查看对方的真实想法", 0, null, null, "MIND_READING"));


            options.Reverse();
            // 5. 显示选项
            _vm.ShowOptions(options.ToArray());

        }      
        private void ProcessSkillCheckResponse(string json, bool isSuccess, SkillCheckOption originalOption)
        {
            LLMResponse_Casual result;
            try
            {
                result = JsonConvert.DeserializeObject<LLMResponse_Casual>(json);
            }
            catch
            {
                // 容错处理
                result = new LLMResponse_Casual
                {
                    NpcReply = "（对方看起来有点疑惑...）",
                    NpcEmotion = "Neutral",
                    NpcAction = "NONE"
                };
            }
            UpdateNpcVisuals(result.NpcReply, result.NpcEmotion, result.NpcAction, result.NpcThinking);
            var fixedOptions = new List<StoryOptionVM>();
            if (isSuccess)
            {
                fixedOptions.Add(new StoryOptionVM("【离开】顺利解决", () =>
                {
                    _vm.Close();
                    _memory.CurrentInitiative = null; // 清除开场状态，避免重复触发
                                                      //AgentAIController.Instance.SendEventToAgent(_targetAgent, "EndInteraction", Agent.Main);

                    AgentAIController.Instance.BroadcastEventInRange(Agent.Main.Position, 15.0f, "EndInteraction", Agent.Main);
                }, "结束对话"));
            }
            else
            {
                var conflict = _memory.CurrentInitiative.ConflictData;
                var newState = new NegotiationState(_targetAgent, conflict);
                //这里加入谈判分析
                InformationManager.ShowInquiry(new InquiryData(
        $"谈判分析：{newState.Name}",
        newState.CalculationLog.ToString(),
        true, false, "我心里有数了", null,
        () => { }, null));


                fixedOptions.Add(new StoryOptionVM("【尝试补救】进入谈判", async () =>
                {
                    _memory.CurrentNegotiationState = newState;
                    // 2. 构造第一张虚拟卡牌，刷新界面
                    var startCard = new NegotiationCard("Plead", "（无论如何，请听我解释...）");

                    // 3. 进入谈判循环
                    await HandlePlayerInputAsync("（试图挽回局面，开始解释）", startCard);

                }, "进入谈判模式"));

                // 选项 2: 战斗 (谈崩了)
                fixedOptions.Add(new StoryOptionVM("【拔刀】 多说无益", () =>
                {
                    _vm.Close();
                    AgentAIController.Instance.SendEventToAgent(_targetAgent, "order_attack", Agent.Main);
                    Agent.Main.TryToWieldWeaponInSlot(EquipmentIndex.WeaponItemBeginSlot, Agent.WeaponWieldActionType.WithAnimation, false);

                }, "放弃交涉，直接动手"));

                fixedOptions.Add(new StoryOptionVM("【投降】 (任由处置)", () =>
                {
                    // 让自己成为对方的俘虏

                    _vm.Close();
                    _memory.CurrentInitiative = null; // 清除开场状态，避免重复触发
                }));
                
            }
            fixedOptions.Add(new StoryOptionVM("【读心】查看内心独白", () => {

                ToggleReadThinking(_memory.CurrentNegotiationState);
            }, "尝试查看对方的真实想法", 0, null, null, "MIND_READING"));


            fixedOptions.Reverse(); 
            _vm.ShowOptions(fixedOptions.ToArray());
        }


        private string ProcessCasualResponse(string json)
        {

            LLMResponse_Casual result;
            try
            {
                result = JsonConvert.DeserializeObject<LLMResponse_Casual>(json);
                currentCasualResponse = result;
            }
            catch
            {
                // 如果解析失败，回退到基础处理
                _vm.DialogueContent = "（对方的话语有些含糊不清...）";
                UpdateUiForNextTurn(new List<NegotiationCard>(), false);
                return json;
            }
            UpdateNpcVisuals(result.NpcReply, result.NpcEmotion, result.NpcAction, result.NpcThinking);
            //检测是否触发谈判
            if (result.SuggestNegotiationStart)
            {
                // 初始化谈判状态
                NegotiationState newState = new NegotiationState(_targetAgent,result.DetectedNegotiationGoal,result.DetectedPlayerGoalDesc);               

                _memory.CurrentNegotiationState = newState;
                // 【修改点】：弹窗展示计算来源
                InformationManager.ShowInquiry(new InquiryData(
                    $"谈判分析：{newState.Name}",
                    newState.CalculationLog.ToString(), // 这里显示刚才记录的日志
                    true, false, "我心里有数了", null,
                    () => { }, null));

            }

            // 3. 生成下一轮按钮
            //这里考虑看看之后要不要预先生成好谈判初始选项
            if (result.SuggestNegotiationStart)
            {
              


                // 强制给一个“开始谈判”的按钮，点击后再次调用 HandlePlayerInputAsync 刷新出真正的谈判卡牌
                var startOpt = new StoryOptionVM("【开始协商】 (进入博弈)", async () =>
                {
                    _vm.LockPrediction();
                    //需要构造一个Card，用于刷新谈判界面
                    var startCard = new NegotiationCard("Flatter", "（开始谈判）");                  

                    // 传入空输入，旨在刷新谈判界面的第一轮 Prompt
                    await HandlePlayerInputAsync("（眼神坚定，准备开始谈判）", startCard);
                }, "进入博弈");
                //补充一个，放弃谈判，回归闲聊
                var cancelOpt = new StoryOptionVM("【误会我了】 (返回闲聊)", () =>
                {
                    _memory.CurrentNegotiationState = null ;
                    // 正常闲聊选项
                    UpdateUiForNextTurn(result.PlayerNextOptions, false);
                }, "返回闲聊");
                _vm.ShowOptions(new[] { startOpt, cancelOpt });
            }
            else
            {


                // 正常闲聊选项
                UpdateUiForNextTurn(result.PlayerNextOptions, false);
            }
            return json;
        }
        // ========================================================================
        // 辅助方法：更新 UI 和 按钮绑定 (打通链路的关键)
        // ========================================================================

        private void UpdateUiForNextTurn(List<NegotiationCard> cards, bool isNegotiationMode)
        {
            var options = new List<StoryOptionVM>();
            
            string npcName = _targetAgent.Name;
            _lastRoundCards = cards;

            //谈判模式下，增加 条件入口
            if (isNegotiationMode)
            {

                //区分当前回合新增筹码，以及从谈判到现在桌上已经放的筹码

                float currentDraftValue = _draftProposal.GetTotalEstimatedValue();
                string currentOfferStr = _draftProposal.chips.Count > 0   ? $"{_draftProposal.GetDescription()}"  : "当前未开出条件";            

                var customProposalOpt = new StoryOptionVM(
                    $"【自定义提案】{currentOfferStr}", // 按钮文本
                    () => OpenCustomProposalMenu(),      // 点击进入子菜单
                    "调整提案内容或者提交本轮提案",                      // Tooltip
                    currentDraftValue                    // [修改点 2]：传入价值用于显示数字
                );

                customProposalOpt._onHoverBeginAction = () => ShowPredictionBar(_draftProposal.GetTotalEstimatedValue());
                customProposalOpt._onHoverEndAction = () => HidePredictionBar();

                options.Add(customProposalOpt);

            }
           

            if (cards != null)
            {
                foreach (NegotiationCard card in cards)
                {
                    // 构造按钮文本
                    string btnText = "";
                    string costStr = "";

                    string TacticStr = NegotiationRegistry.GetTacticInfo(card.Tactic).Name;
                    string CostTypeStr = NegotiationRegistry.GetCostName(card.CostType);
                    if (isNegotiationMode)
                    {
                        // 谈判模式显示代价
                        costStr = card.CostAmount > 0 ? $"[{card.CostAmount} {CostTypeStr}]" : "";
                        btnText = $"[{TacticStr}]{card.Text}";
                    }
                    else
                    {
                        // 闲聊模式显示意图
                        btnText = $"[{TacticStr}] {card.Text}";
                    }
                    string PredictText = $"付出：{costStr}";
                    if(!string.IsNullOrEmpty(card.PredictedImpact))
                        PredictText += $"\n预测：{card.PredictedImpact}";
                    float estimatedValue = NegotiationRegistry.CalculateCardValue(card);
                    var opt = new StoryOptionVM(btnText, async () =>
                    {
                        // 玩家点击了这个选项，视为玩家说了 card.Text，并打出了 card
                        _vm.LockPrediction();
                        DebugLogger.Log($"【出牌】{card.Text} 基础值：{estimatedValue}");
                        await HandlePlayerInputAsync(card.Text, card);
                    }, PredictText, estimatedValue, () => ShowPredictionBar(estimatedValue), () => HidePredictionBar());
                      


                    // 绑定点击事件 -> HandlePlayerInputAsync
                    // 这里的 Lambda 表达式捕获了 card 变量
                    options.Add(opt);
                }
            }

            options.Add(new StoryOptionVM("【读心】查看内心独白", () =>            {

                ToggleReadThinking(_memory.CurrentNegotiationState);                
            }, "尝试查看对方的真实想法",0,null,null, "MIND_READING"));


            //自由聊天卡
            NegotiationCard freeTalkCard = new NegotiationCard("Flatter", "自由对话");


            options.Add(new StoryOptionVM("【寒暄】", () =>
            {



                InformationManager.ShowTextInquiry(new TextInquiryData(
                  "寒暄", $"你想对{npcName}说什么?：", true, true, "发送", "取消",
                  async (text) =>
                  {
                      _vm.LockPrediction();
                      freeTalkCard.Text = text;
                      await HandlePlayerInputAsync(freeTalkCard.Text, freeTalkCard);
                  }, null));
            }, "自由输入你想说的内容"));

            options.Add(new StoryOptionVM("【离开】告辞", () => { 
                //AgentAIController.Instance.SendEventToAgent(_targetAgent, "EndInteraction", Agent.Main);

                AgentAIController.Instance.BroadcastEventInRange(Agent.Main.Position, 15.0f, "EndInteraction", Agent.Main);
                GroupStageManager.Reset(Agent.Main);
                _vm.Close();
            }, "退出对话"));
            options.Reverse();
            _vm.ShowOptions(options.ToArray());
        }

        private void OpenCustomProposalMenu()
        {
            var options = new List<StoryOptionVM>();
            float currentTotalValue = _draftProposal.GetTotalEstimatedValue();
            bool hasChips = _draftProposal.chips.Count > 0;
            // 1. 提交按钮 (只有当有筹码时才显示，或者总是显示但没筹码时提示)
            if (hasChips)
            {
                var submitOpt = new StoryOptionVM(
                    "【确认提交并发言】",
                    () => {
                        // 弹出输入框
                        InformationManager.ShowTextInquiry(new TextInquiryData(
                         "确认条件", $"当前条件：{_draftProposal.GetDescription()}。\n附加一句什么话？",
                         true, true, "发送", "取消",
                         async (text) =>
                         {
                             _vm.LockPrediction();
                             var state = _memory.CurrentNegotiationState;

                             string dominantType = _draftProposal.chips.OrderByDescending(c => c.EstimatedValue).First().Type.ToString();

                             var proposalCard = new NegotiationCard("Bribe", text);
                             proposalCard.Chips = new List<Chip>(_draftProposal.chips);
                             //将草稿箱的筹码正式转为卡牌
                             _draftProposal.chips.Clear();
                             proposalCard.EffectBaseValue = (int)currentTotalValue; // 基础攻击力 = 价值
                             proposalCard.CostAmount = (int)currentTotalValue;
                             //_draftProposal.Clear();
                             await HandlePlayerInputAsync(text, proposalCard);
                         }, null));
                    },
                    "将当前选定的筹码发送给对方",
                    currentTotalValue // 显示价值
                );

                // 在提交按钮上也绑定预测条，方便玩家最后确认一眼
                submitOpt._onHoverBeginAction = () => ShowPredictionBar(currentTotalValue);
                submitOpt._onHoverEndAction = () => HidePredictionBar();

                options.Add(submitOpt);
            }
            else
            {
                // 如果没筹码，给一个不可点击或者提示性的选项
                //options.Add(new StoryOptionVM("（请先添加筹码）", () => { }, "你需要先添加一些条件"));
            }

            // 2. 加注
            options.Add(new StoryOptionVM("【加注】 增加筹码", () => OpenCategorySelectMenu_Refactored()));

            // 3. 减注
            if (_draftProposal.chips.Count > 0)
            {
                options.Add(new StoryOptionVM("【减注】 移除筹码", () => OpenRemoveMenu()));
            }

            // 4. 返回上一级
            options.Add(new StoryOptionVM("【返回】", () =>
            {
                // 返回上一级，实际上就是刷新回 UpdateUiForNextTurn
                // 需要把上一轮的大模型建议卡牌传回去
                UpdateUiForNextTurn(_lastRoundCards, true);
            }));

            _vm.ShowOptions(options.ToArray());
        }


        // [新增] UI 预测条控制
        private void ShowPredictionBar(float gainValue)
        {

            var state = _memory.CurrentNegotiationState;
            if (state == null) return;
            float gainPercent = _vm.GetMaxProgressValue()* gainValue / state.TargetThreshold;

            _vm.PreviewPrediction(gainValue, state.TargetThreshold);



            InformationManager.DisplayMessage(new InformationMessage($"当前进度：{state.CurrentProgress}/{state.TargetThreshold} 增加值{gainValue} 增加百分比{gainPercent:F1}，", Colors.Green));
         
        }

        private void HidePredictionBar()
        {
            var state = _memory.CurrentNegotiationState;
            if (state == null) return;
            _vm.HidePrediction();
        }
        private void OpenProposalRootMenu()
        {
            var options = new List<StoryOptionVM>();

            options.Add(new StoryOptionVM("【加注】 增加条件", () => OpenCategorySelectMenu_Refactored()));

            if (_draftProposal.chips.Count > 0)
            {
                options.Add(new StoryOptionVM("【减注】 撤回条件", () => OpenRemoveMenu()));
            }

            options.Add(new StoryOptionVM("【返回】", () =>
            {
                // 重新刷新主界面 (需要缓存上一次的 LLM 建议卡牌，这里简化处理，假设 memory 里有)
                // 实际操作中，最好把 UpdateUiForNextTurn 的参数存起来
                UpdateUiForNextTurn(_lastRoundCards, true);
            }));

            _vm.ShowOptions(options.ToArray());
        }
     

        private void OpenCategorySelectMenu_Refactored()
        {
            var options = new List<StoryOptionVM>();
            PlayerResources playerRes = _memory.CurrentNegotiationState.playerResources; // 获取资源快照

            // ================= Group 1: 财富与资产 =================

            // 1.1 个人金钱 (PersonalGold)
            AddNumericResourceOption(options, "个人金钱", NegotiationCostType.PersonalGold, playerRes.PersonalGold);

            // 1.2 势力资金 (FactionGold)
            // 只有当玩家是国王或有权限时显示
            if (playerRes.FactionGold > 0)
            {
                AddNumericResourceOption(options, "势力公款", NegotiationCostType.FactionGold, playerRes.FactionGold);
            }

            // 1.3 物品 (Item) - 打开物品选择器
            options.Add(new StoryOptionVM("【物品】 (纳贡)", () => OpenItemSelectMenu()));

            // 1.4 城池 (Settlement) - 打开城池选择器
            if (playerRes.OwnedSettlements.Count > 0)
            {
                options.Add(new StoryOptionVM("【城池】 (割地)", () => OpenFiefSelectMenu()));
            }

            // ================= Group 2: 社会资本 (抽象资源) =================

            // 2.1 善名 (Reputation) - "我用我的名誉担保"
            // 逻辑：玩家消耗声望值，一旦违约或失败，声望暴跌
            if (playerRes.Reputation > 10) // 门槛
            {
                AddNumericResourceOption(options, "名誉担保", NegotiationCostType.Reputation, (int)playerRes.Reputation);
            }

            // 2.2 人情 (SocialRelation) - "看在我们的交情上"
            if (playerRes.SocialRelation > 5)
            {
                // 这里可以直接最大值梭哈，或者输入数值
                AddNumericResourceOption(options, "动用人情", NegotiationCostType.SocialRelation, (int)playerRes.SocialRelation);
            }

            // 2.3 恶名 (Notoriety) - "你也不想把事情闹大吧" (威慑)
            // 这是一个特殊的反向资源，通常作为 Threat Tactic 的加成，但这里作为筹码可能意味着 "我承诺不使用暴力/不散布谣言"
            // 或者理解为：投入恶名 = 进行恐吓操作
            if (playerRes.Notoriety > 0)
            {
                // 恐吓不需要输入数量，通常是一次性行为
                options.Add(new StoryOptionVM("【恶名】 施加恐吓", () => {
                    AddWagerItem(new Chip(NegotiationCostType.Notoriety, "暴力威胁", "让对方感到恐惧", 100)); // 这里的Value 100是估值
                    OpenCustomProposalMenu();
                }));
            }

            // ================= Group 3: 期货与承诺 =================

            // 3.1 承诺 (Promise)
            options.Add(new StoryOptionVM("【空头支票】 (许诺)", () => OpenPromiseSubMenu()));


            options.Add(new StoryOptionVM("【返回】", () => OpenCustomProposalMenu()));
            _vm.ShowOptions(options.ToArray());
        }

        // --- 辅助方法：添加数值型资源的选项 ---
        private void AddNumericResourceOption(List<StoryOptionVM> options, string label, NegotiationCostType type, int maxAvailable)
        {
            options.Add(new StoryOptionVM($"[{label}]", () =>
            {
                // 1. 计算这一类资源已经被草稿箱，以及本次的累计资源池占用了多少
                int currentWagered = _draftProposal.chips
                    .Where(c => c.Type == type)
                    .Sum(c => (int)c.Amount); 

                int realAvailable = maxAvailable - currentWagered;

                if (realAvailable <= 0)
                {
                    InformationManager.DisplayMessage(new InformationMessage($"你没有足够的{label}了！"));
                    return;
                }

                InformationManager.ShowTextInquiry(new TextInquiryData(
                   $"投入{label}",
                   $"背包总量: {maxAvailable}\n已加注: {currentWagered}\n当前剩余可用: {realAvailable}",
                   true, true, "确认", "取消",
                   (text) => {
                       if (int.TryParse(text, out int amount))
                       {
                           amount = (int)MathF.Clamp((float)amount, 0, realAvailable);
                           if (amount > 0)
                           {
                               // 【关键】创建 Chip 时严格传入 Type
                               AddWagerItem(new Chip(type, $"{amount} {label}", label, amount));
                           }
                       }
                   }, null));
            }));
        }


        private void OpenPromiseSubMenu()
        {
            var options = new List<StoryOptionVM>();
            // 选项 B: 预设 - 联姻
            options.Add(new StoryOptionVM("【承诺分期】", () => {
                AddWagerItem(new Chip(NegotiationCostType.Promise, "分期支付", "Promise", 500));
            }));
            // 选项 B: 预设 - 联姻
            options.Add(new StoryOptionVM("【承诺联姻】", () => {
                AddWagerItem(new Chip(NegotiationCostType.Promise, "家族联姻", "Promise", 500));
            }));

            // 选项 C: 预设 - 晋升
            options.Add(new StoryOptionVM("【承诺晋升】", () => {
                AddWagerItem(new Chip(NegotiationCostType.Promise, "推荐晋升", "Promise", 200));
            }));

            options.Add(new StoryOptionVM("【返回】", () => OpenCategorySelectMenu_Refactored()));
            _vm.ShowOptions(options.ToArray());
        }
        private void AddWagerItem(Chip item)
        {
            _draftProposal.AddOrUpdate(item);
             OpenCustomProposalMenu();
        }
        private void OpenItemSelectMenu(int page = 0)
        {
            var options = new List<StoryOptionVM>();
            const int ItemsPerPage = 8; // 每页显示的物品数量，防止UI溢出

            // 1. 获取玩家背包数据
            // GetElementCopyAtIndex 用于遍历，但直接转成 List 更方便操作分页
            // 这里我们将 Roster 转换为 List，并过滤掉空数据
            var rosterElements = MobileParty.MainParty.ItemRoster
                .Where(item => !item.IsEmpty && item.EquipmentElement.Item != null)
                // 可选：按单个物品价值排序，把贵的放前面
                .OrderByDescending(item => item.EquipmentElement.Item.Value)
                .ToList();

            // 2. 计算分页数据
            int totalItems = rosterElements.Count;
            int totalPages = (totalItems + ItemsPerPage - 1) / ItemsPerPage;
            
            // 防止页码越界
            if (page < 0) page = 0;
            if (page >= totalPages && totalPages > 0) page = totalPages - 1;

            int startIndex = page * ItemsPerPage;
            int endIndex = System.Math.Min(startIndex + ItemsPerPage, totalItems);

            // 3. 遍历当前页的物品
            for (int i = startIndex; i < endIndex; i++)
            {
                var element = rosterElements[i];
                var itemObject = element.EquipmentElement.Item;

                // 创建筹码对象
                // 注意：这里默认将背包里该物品的“堆叠数量”全部作为筹码
                // 如果想让玩家选数量，需要额外的 UI 逻辑，这里简化为 All-in
                var chip = new Chip(NegotiationCostType.Item, itemObject.Name.ToString(), itemObject.StringId, element.Amount);
                float estimatedVal = chip.EstimatedValue;
                // 4. 调用上一轮定义的计算器进行估值
                // 这会根据 Amount * 物品单价 计算总价

                // 5. 构建菜单选项
                // 显示文本示例: "西方良马 (x5)"
                string optionText = $"{chip.Name} (x{chip.Amount})";

                var opt = new StoryOptionVM(
                    optionText,
                    () => AddWagerItem(chip), // 点击动作：添加筹码
                    "", // 可以在这里加 tooltip id
                    chip.EstimatedValue // 传入数值用于 UI 显示数字
                );

                // Hover 预览进度条增长 (与 OpenFiefSelectMenu 逻辑一致)
                opt._onHoverBeginAction = () => ShowPredictionBar(chip.EstimatedValue);
                opt._onHoverEndAction = () => HidePredictionBar();

                options.Add(opt);
            }

            // 6. 添加翻页按钮
            if (page > 0)
            {
                options.Add(new StoryOptionVM("【上一页】", () => OpenItemSelectMenu(page - 1)));
            }

            if (page < totalPages - 1)
            {
                options.Add(new StoryOptionVM("【下一页】", () => OpenItemSelectMenu(page + 1)));
            }

            // 7. 返回按钮
            options.Add(new StoryOptionVM("【返回】", () => OpenCategorySelectMenu_Refactored()));

            options.Reverse();
            // 显示选项
            _vm.ShowOptions(options.ToArray());
        }
        private void OpenFiefSelectMenu()
        {
            var options = new List<StoryOptionVM>();

            // 获取玩家拥有的城池
            foreach (Settlement settlement in Hero.MainHero.Clan.Settlements.Where(s => s.IsFortification))
            {
                // 简单估值：繁荣度 * 系数
                float estimatedVal;

                var item = new Chip(NegotiationCostType.SettlementOwnership, settlement.Name.ToString(), settlement.StringId, 1);
                estimatedVal = item.EstimatedValue;
                // 选项
                var opt = new StoryOptionVM(settlement.Name.ToString(), () => AddWagerItem(item),"",estimatedVal);

                // Hover 预览该城池带来的进度条增长
                opt._onHoverBeginAction = () => ShowPredictionBar(estimatedVal);
                opt._onHoverEndAction = () => HidePredictionBar();

                options.Add(opt);
            }

            options.Add(new StoryOptionVM("【返回】", () => OpenCategorySelectMenu_Refactored()));

            // 支持翻页逻辑 (如果城池太多)可以在这里加分页

            _vm.ShowOptions(options.ToArray());
        }

        // [新增] 移除菜单
        private void OpenRemoveMenu()
        {
            var options = new List<StoryOptionVM>();
            foreach (var item in _draftProposal.chips)
            {
                options.Add(new StoryOptionVM($"移除: {item.Name}", () =>
                {
                    _draftProposal.Remove(item);
                    OpenProposalRootMenu();
                }));
            }
            options.Add(new StoryOptionVM("【返回】", () => OpenProposalRootMenu()));
            _vm.ShowOptions(options.ToArray());
        }

        private void ToggleReadThinking()
        {
            var mindBtn = _vm.OptionList.FirstOrDefault(opt => opt.Identifier == "MIND_READING");
            if (mindBtn == null) return;

            if (!_isReadingMind)
            {
                // -> 切换到读心模式
                _vm.DialogueContent = _cachedCurrentThinking;
                mindBtn.OptionText = "【读心】查看明面回复";
                _isReadingMind = true;
            }
            else
            {
                // -> 切换回正常模式
                _vm.DialogueContent = _cachedCurrentReply;
                mindBtn.OptionText = "【读心】查看内心独白";
                _isReadingMind = false;
            }
        }

        //更新引擎表现，镜头、说话、动作、执行操作等
        private void UpdateNpcVisuals(string reply, string emotion, string action,string thoughts )
        {
            // 1. 缓存数据（核心修改）
            _cachedCurrentReply = reply;
            // 如果没有传 thoughts (比如旧代码或某些特殊情况)，给个默认值防止报错
            _cachedCurrentThinking = string.IsNullOrEmpty(thoughts) ? "（看不出在想什么...）" : thoughts;

            // 重置读心状态为 false，因为 NPC 说了新话，默认显示明面回复
            _isReadingMind = false;
            // 更新文本
            lock (_memory)
            {
                _memory.AddHistory("assistant", $"{_targetAgent.Name}: {reply}");
            }
            _vm.Show(_targetAgent.Name.ToString(), reply);
            InformationManager.DisplayMessage(new InformationMessage($"{_targetAgent.Name}回复: {reply}", Colors.Red));
            // 更新动画/表情动作
            if (!string.IsNullOrEmpty(emotion))
            {
                AgentControlHelper.SetPose(_targetAgent, _matcher.GetAnimByEmotion(emotion));
            }

            // 触发执行Action
            if (!string.IsNullOrEmpty(action) && _targetHero != null)
            {
                ActionHandler.HandleAction(action, _targetHero, Hero.MainHero, _targetAgent);
            }

            // 镜头打向NPC
            VisualCommands.SmartCamera(_targetAgent, Agent.Main);
        }

        private void ExecuteTransaction(DraftProposal proposal)
        {
            foreach (var item in proposal.chips)
            {
                switch (item.Type)
                {
                    case NegotiationCostType.PersonalGold:
                        GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, _targetHero, item.Amount);
                        break;
                    case NegotiationCostType.SettlementOwnership:
                        // 查找 settlement 并转移
                        var settlement = Settlement.Find(item.StringId);
                        ChangeOwnerOfSettlementAction.ApplyByBarter(_targetHero, settlement);
                        break;
                        // ... 处理其他类型 ...
                }
            }
            // 清空草稿
            proposal.Clear();
        }
        // 以前的玩家输入处理逻辑
      
     

        

        //基于近期记忆和对话，生成一个事件
        public async Task<SocialEvent> GenerateEventAsync()
        {
            StringBuilder sbHistory = new StringBuilder();
            StringBuilder sbMemory = new StringBuilder();
            int validHistoryNum = 0;
            int validMemoryNum = 0;

            // 遍历记忆 (假设 DynamicMemories 是按时间排序的，或者这里逻辑是提取最近的)
            foreach (var memory in _memory.DynamicMemories)
            {
                if (memory.TimeStamp_Start > InteractBeginTimeStamp)
                {
                    validMemoryNum++;
                    sbMemory.AppendLine($"[Memory] {memory.Content}");
                }
            }

            // 遍历对话历史
            foreach (var chat_history in _memory.RecentHistory)
            {
                if (chat_history.TimeStamp > InteractBeginTimeStamp)
                {
                    validHistoryNum++;
                    // 假设 chat_history.Content 包含了 "SpeakerName: Content" 的格式，如果没有，建议在这里拼接名字
                    sbHistory.AppendLine($"[Chat] {chat_history.Content}");
                }
            }

            // 简单过滤：如果信息太少，不足以构成事件，返回 null
            // 阈值设为 5 可能有点高，如果是一句恶毒的辱骂可能只有 1 条记录，建议根据实际测试调整
            if (validHistoryNum + validMemoryNum < 2) return null;

            // 构建 Prompt
            string prompt = PromptBuilder.BuildPromptForSocialEvent(_memory,sbHistory.ToString(), sbMemory.ToString());

            // 请求 LLM (建议温度设低一点，比如 0.1 或 0.2，以保证 JSON 格式稳定)
            string jsonResponse = await LLMService.Instance.ChatAsync(prompt, 500); // token 300 可能不够，稍微增加到 500

            // 解析 JSON
            SocialEvent evt = _memory.ParseSocialEventJson(jsonResponse);

            return evt;
        }

    }
}
