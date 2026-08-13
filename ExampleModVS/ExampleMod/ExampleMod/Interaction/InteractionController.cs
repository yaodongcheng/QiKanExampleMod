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
using static LivingWorldNpcs.PromptBuilder;
namespace LivingWorldNpcs
{
    /// <summary>
    /// 当面对话控制器（StoryDialogVM 驱动）。
    /// 🔴 2026-08-13 重构：闲聊动作执行器（ActionHandler，原同居本文件）与动作空间位掩码
    /// （ActionSpace）已随动作系统迁出——主表 ActionRegistry + 执行入口 ActionHandler +
    /// 单步执行 ChatActionFlow 团聚于 Planner/（action-registry-refactor.md）。
    /// </summary>
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
        // 标记当前是否处于"读心"状态
        private bool _isReadingMind = false;
        // [新增] 缓存当前回合的文本，用于读心切换
        private string _cachedCurrentReply = "";
        private string _cachedCurrentThinking = "";
        public LLMResponse_Casual currentCasualResponse = null;

        /// <summary>是否已展开"其他事情"（持久化在 Controller 上，避免 IntentContext 重建丢失）。</summary>
        public bool OptionsExpanded = false;

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
            OptionsExpanded = false;

            DebugLogger.Log($"[Player] Talk to: {_targetAgent.Name} (hero={_targetHero?.Name?.ToString() ?? "none"})");

            // IM 互动热度：面对面对话开始 +2（用户决策 3：互动多 → 记忆容量大）
            if (_targetHero != null && !string.IsNullOrEmpty(_targetHero.StringId))
                ImHeatTracker.Add(_targetHero.StringId, 2f);

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
                // === 统一轮次制（KCD2 式）：NPC 先说开场白 → 玩家点"继续" → 选项出现 ===

                // 第一优先：从 NPC 自身记忆读取最紧迫的世界事件
                var urgentEvent = _memory?.CurrentUrgentEvent;

                // 🛡 兜底：如果记忆里没有（惰性创建 / key 不匹配等），直接查事件数据库
                if (urgentEvent == null && _targetHero != null && !string.IsNullOrEmpty(_targetHero.StringId))
                {
                    urgentEvent = WorldEventStore.ActiveEvents
                        .Where(e => e.TargetHeroId == _targetHero.StringId || e.InitiatorId == _targetHero.StringId)
                        .OrderByDescending(e => e.Severity)
                        .FirstOrDefault();
                    if (urgentEvent != null && _memory != null)
                    {
                        _memory.CurrentUrgentEvent = urgentEvent;
                        DebugLogger.Log($"[EventAware] Backfilled memory for {_targetHero.Name} ← {urgentEvent.Type}");
                    }
                }

                if (urgentEvent != null)
                {
                    initialText = WorldEventDirector.BuildEventOpeningLine(urgentEvent, _targetHero, "Greeting");
                }

                // 如果事件驱动未产出有效文本（兜底），尝试 Party 级别匹配
                if (string.IsNullOrEmpty(initialText))
                {
                    MobileParty npcParty = _targetHero?.PartyBelongedTo;
                    if (npcParty == null && MapEncounterDialogState.Active)
                        npcParty = MapEncounterDialogState.PartnerParty?.MobileParty;

                    string partyLine = WorldEventDirector.GetEventAwareDialogueForParty(npcParty, "Greeting");
                    if (!string.IsNullOrEmpty(partyLine))
                        initialText = partyLine;
                }

                // 如果事件文本生成失败但有事件缠身 → 保底事件句，绝不回退到普通寒暄
                if (string.IsNullOrEmpty(initialText) && urgentEvent != null)
                {
                    // 本地化：事件开场白兜底句
                    initialText = LWNTextHelper.ResolveText("LWN_ui_interact_opening_fallback_worry", "...What am I to do...");
                    DebugLogger.Log($"[EventAware] WARNING: NPC={_targetHero?.Name} has urgent event {urgentEvent.Type} but text generation returned empty, using fallback");
                }

                // 无事件 → 普通上下文感知开场白
                if (string.IsNullOrEmpty(initialText))
                {
                    string contextual = WorldEventDirector.GetContextualOpening(_targetHero);
                    initialText = !string.IsNullOrEmpty(contextual)
                        ? contextual
                        // 本地化：普通开场白兜底句
                        : LWNTextHelper.ResolveText("LWN_ui_interact_opening_fallback", "...What do you want to say?");
                }

                _vm.Show(displayName, initialText);
                DebugLogger.Log($"[Dialog] NPC says (opening): \"{initialText}\"");
                _vm.AreOptionsVisible = false;

                _vm.OnClickContinue = () =>
                {
                    RefreshInitialOptions();
                };
            }

            

        }
       

        // 生成初始意图逻辑（薄壳：全部交给意图注册表 + 资格层）
        public void RefreshInitialOptions()
        {
            if (_targetAgent == null) return;
            var opts = _optionManager.BuildOptionVMs(_targetAgent);
            _vm.ShowOptions(opts);
            DebugLogger.Log($"[Dialog] Options shown ({opts.Length}): {string.Join(" | ", opts.Select(o => o.OptionText))}");
        }

        /// <summary>
        /// 让当前对话的 NPC 自然说一句话，并附带选项。
        /// 用于替代 InquiryData 弹窗——除非内容确实需要弹窗（如委托书信），否则一律走此函数让 NPC 自然说话。
        /// </summary>
        public void SceneSay(string npcLine, params StoryOptionVM[] options)
        {
            if (_vm == null) return;
            string name = _targetAgent?.Name?.ToString() ?? "";
            _vm.Show(name, npcLine);
            DebugLogger.Log($"[Dialog] NPC says: \"{npcLine}\"");
            if (options != null && options.Length > 0)
            {
                _vm.ShowOptions(options);
                DebugLogger.Log($"[Dialog] Options ({options.Length}): {string.Join(" | ", options.Select(o => o.OptionText))}");
            }
            else
                _vm.AreOptionsVisible = false;
        }

        /// <summary>关闭当前对话 UI。</summary>
        public void CloseDialogue()
        {
            _vm?.Close();
        }

        /// <summary>当前对话对象（供意图回调读取）。</summary>
        public Agent CurrentAgent => _targetAgent;


        // ============================================================
        // 无 LLM 单次检定：意图分发 + 结算 + 子菜单（主线程同步，见坑 P2）
        // ============================================================

        /// <summary>选项点击入口：即时类直接结算；对抗类按 LLM 是否就绪分流。</summary>
        public void DispatchIntent(IntentBase intent, IntentContext ctx)
        {
            if (intent == null || ctx == null) return;
            DebugLogger.Log($"[Dialog] Player clicked: \"{intent.DisplayName}\" (goal={intent.Goal}, category={intent.Category})");

            if (!intent.Goal.HasValue)
            {
                intent.OnInstant(ctx);
                return;
            }

            if (Settings.Instance.IsLLMConfigured)
                StartLLMNegotiation(intent, ctx);   // 有 LLM：走谈判博弈盘（目标已知，无需 LLM 猜）
            else
                ResolveAdversarialIntent(intent, ctx); // 无 LLM：单次检定
        }

        /// <summary>有 LLM：用意图已知的目标直接开一场谈判。</summary>
        private void StartLLMNegotiation(IntentBase intent, IntentContext ctx)
        {
            if (ctx.Speaker == null) { ResolveAdversarialIntent(intent, ctx); return; }
            _memory.CurrentNegotiationState = new NegotiationState(ctx.Agent, intent.Goal.Value.ToString(), intent.DisplayName);
            // 谈判开局玩家话语：以意图名作为开场（语序由 XML 控制）
            var startCard = new NegotiationCard(intent.Tactic.ToString(),
                // 谈判开场白（{NAME}）
                LWNTextHelper.ResolveCompound("LWN_ui_interact_nego_opening", "(about {NAME})", ("NAME", intent.DisplayName)));
            _vm.LockPrediction();
            // 谈判开局玩家发言文本
            _ = Task.Run(() => HandlePlayerInputAsync(
                // 谈判开场白（{NAME}）
                LWNTextHelper.ResolveCompound("LWN_ui_interact_nego_opening", "(about {NAME})", ("NAME", intent.DisplayName)), startCard));
        }

        /// <summary>无 LLM：一次掷骰决定成败，模板台词 + 直接结算。</summary>
        public void ResolveAdversarialIntent(IntentBase intent, IntentContext ctx)
        {
            if (ctx == null || !intent.Goal.HasValue) return;
            // P3：守卫/无人设的 agent 不能进对抗结算（对抗意图本就只对 Hero 开放，这里二次兜底）
            if (ctx.Speaker == null || ctx.Profile == null)
            {
                // 本地化：无法深谈提示
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText("LWN_ui_interact_msg_no_deep_talk", "You can't have a deep talk with this person.")));
                return;
            }

            RollResult rr = SingleRollResolver.Compute(ctx, intent.Goal.Value, intent.Tactic, intent.GetOfferValue(ctx));
            DebugLogger.Log(rr.Log);
            bool success = SingleRollResolver.Roll(rr.Chance);
            DebugLogger.Log($"[单次检定] {intent.DisplayName} 掷骰结果：{(success ? "成功" : "失败")}");

            if (success) intent.OnSuccess(ctx);
            else intent.OnFail(ctx);

            // 模板台词 + 表情动作
            string emotion;
            string line = DialogueTemplateHelper.Get(intent.DialogueKey, success, out emotion, ctx.Speaker, ctx.Agent);
            UpdateNpcVisuals(line, emotion, "NONE", "");

            // ── 🆕 ReofferOnFail：失败后重新渲染选项 ──
            if (!success && intent.ReofferOnFail)
            {
                // OnFail 已修改 ctx.ActionParam → BuildOptionVMs 重新求值
                RefreshInitialOptions();
                return;
            }

            // ── 默认收尾：【离开】/【继续】──
            var opts = new List<StoryOptionVM>();
            // 本地化：对话收尾选项（离开）
            opts.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_leave", "[Leave] Farewell"), () =>
            {
                AgentAIController.Instance.BroadcastEventInRange(Agent.Main.Position, 15.0f, "EndInteraction", false, Agent.Main);
                GroupStageManager.Reset(Agent.Main);
                _vm.Close();
            }));
            // 本地化：对话收尾选项（继续）
            opts.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_continue", "[Continue] Something else"), () => RefreshInitialOptions()));
            opts.Reverse();
            _vm.ShowOptions(opts.ToArray());
        }

        /// <summary>展示一句 NPC 台词后回到主菜单（即时类用，如命令士兵）。</summary>
        public void ShowNpcLineKeepMenu(Agent agent, string line, string emotion)
        {
            UpdateNpcVisuals(line, emotion, "NONE", "");
            RefreshInitialOptions();
        }

        /// <summary>有 LLM 的自由聊天输入（从原寒暄逻辑迁出）。</summary>
        public void OpenFreeChatInput(Agent agent)
        {
            string name = agent.Name.ToString();
            InformationManager.ShowTextInquiry(new TextInquiryData(
              // 本地化：自由聊天输入框（标题/提示/按钮）
              LWNTextHelper.ResolveText("LWN_ui_interact_smalltalk_title", "Small Talk"), LWNTextHelper.ResolveCompound("LWN_ui_interact_smalltalk_prompt", ("NAME", name)), true, true, LWNTextHelper.ResolveText("LWN_ui_interact_btn_send", "Send"), LWNTextHelper.ResolveText("LWN_ui_interact_btn_cancel", "Cancel"),
              async (text) =>
              {
                  _vm.LockPrediction();
                  await HandlePlayerInputAsync(text, null);
              }, null));
        }

        /// <summary>无 LLM 的话题菜单（太阁5 式预设话题，内容由多因素框架按荣誉/性别/身份选词）。</summary>
        public void OpenChatTopicMenu(IntentContext ctx)
        {
            var factors = DialogueFactors.FromContext(ctx);
            var topics = new[]
            {
                // 本地化：闲聊话题选项（问候/近况/消息/恭维）
                new KeyValuePair<string,string>("Greeting", LWNTextHelper.ResolveText("LWN_ui_interact_topic_greeting", "Greetings")),
                // 聊聊近况
                new KeyValuePair<string,string>("Weather",  LWNTextHelper.ResolveText("LWN_ui_interact_topic_weather", "Talk about recent days")),
                // 打听消息
                new KeyValuePair<string,string>("Gossip",   LWNTextHelper.ResolveText("LWN_ui_interact_topic_gossip", "Ask for news")),
                // 恭维几句
                new KeyValuePair<string,string>("Praise",   LWNTextHelper.ResolveText("LWN_ui_interact_topic_praise", "Pay compliments")),
            };
            var options = new List<StoryOptionVM>();
            foreach (var t in topics)
            {
                string key = t.Key;
                options.Add(new StoryOptionVM(t.Value, () =>
                {
                    string emotion;
                    string line;

                    // ── 世界事件上下文：NPC 是事件当事人 → 对话反映其处境 ──
                    string eventLine = null;
                    if (key == "Greeting" || key == "Weather")
                    {
                        eventLine = WorldEventDirector.GetEventAwareDialogue(ctx.Speaker, key);
                    }

                    // 打听消息：优先用 WorldEvent 真实传闻，查不到再回退 CSV 通用台词
                    if (key == "Gossip")
                    {
                        string rumor = WorldEventDirector.GetTavernRumor(ctx.Speaker);
                        if (!string.IsNullOrEmpty(rumor))
                        {
                            line = rumor;
                            emotion = "normal";
                        }
                        else
                        {
                            line = DialogueTemplateHelper.Get("Chat_" + key, factors, out emotion, ctx.Speaker, ctx.Agent);
                        }
                    }
                    else if (!string.IsNullOrEmpty(eventLine))
                    {
                        // NPC 涉及世界事件 → 用事件上下文对话
                        line = eventLine;
                        emotion = key == "Greeting" ? "urgent" : "sad";
                    }
                    else
                    {
                        line = DialogueTemplateHelper.Get("Chat_" + key, factors, out emotion, ctx.Speaker, ctx.Agent);
                    }
                    int delta = key == "Praise" ? 2 : 1;
                    if (factors.Honor == HonorLevel.High) delta += 1;
                    else if (factors.Honor == HonorLevel.Low) delta = Math.Max(delta - 1, 0);
                    if (ctx.Speaker != null) ChangeRelationAction.ApplyPlayerRelation(ctx.Speaker, delta);
                    DebugLogger.Log($"[Dialog] Player chose chat topic: \"{t.Value}\" → NPC reply: \"{line}\"");
                    UpdateNpcVisuals(line, emotion, "NONE", "");
                    OpenChatTopicMenu(ctx);
                }));
            }

            // 打听声望：动态构建回复（含数值 + 解释）
            // 本地化：打听声望选项
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_ask_reputation", "Ask about reputation"), () =>
            {
                int honor = 0;
                if (Hero.MainHero.CurrentSettlement != null)
                    honor = SettlementHonorStore.Get(Hero.MainHero.CurrentSettlement);
                string npcName = ctx.Speaker != null && ctx.Speaker.Name != null
                    ? ctx.Speaker.Name.ToString()
                    // 本地化：对话对象名兜底
                    : (ctx.Agent != null ? ctx.Agent.Name.ToString() : LWNTextHelper.ResolveText("LWN_ui_interact_name_other", "the other person"));

                string desc;
                // 本地化：本地声望描述（分五档）
                if (honor >= 10)
                    // 你在本地的声望极高（{HONOR}），乡亲们都把你当自家人，征兵能便宜一半...
                    desc = LWNTextHelper.ResolveCompound("LWN_ui_interact_rep_high", ("HONOR", honor.ToString()));
                else if (honor >= 5)
                    // 你在本地声望不错（{HONOR}），大家见了你都愿意招呼一声，征兵也有折扣。
                    desc = LWNTextHelper.ResolveCompound("LWN_ui_interact_rep_good", ("HONOR", honor.ToString()));
                else if (honor >= 0)
                    // 你在本地声望一般（{HONOR}），就是普通路人，没什么特别的。
                    desc = LWNTextHelper.ResolveCompound("LWN_ui_interact_rep_neutral", ("HONOR", honor.ToString()));
                else if (honor >= -3)
                    // 你在本地风评不太好（{HONOR}），大家见你来了都不怎么搭理。
                    desc = LWNTextHelper.ResolveCompound("LWN_ui_interact_rep_bad", ("HONOR", honor.ToString()));
                else
                    // 你在本地的名声很差（{HONOR}），乡亲们避之不及，征兵价格也更贵。
                    desc = LWNTextHelper.ResolveCompound("LWN_ui_interact_rep_terrible", ("HONOR", honor.ToString()));

                // 本地化：声望回答引语
                string line = LWNTextHelper.ResolveCompound("LWN_ui_interact_rep_says", ("NAME", npcName), ("DESC", desc));
                UpdateNpcVisuals(line, "normal", "NONE", "");
                OpenChatTopicMenu(ctx);
            }));
            // 本地化：返回选项
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_back", "[Back]"), () => RefreshInitialOptions()));
            options.Reverse();
            _vm.ShowOptions(options.ToArray());
        }

        /// <summary>送礼菜单：从背包挑物品，按价值×NPC喜好算好感增量。</summary>
        public void OpenGiftMenu(Hero target, int page = 0)
        {
            if (target == null) return;
            var options = new List<StoryOptionVM>();
            const int per = 8;
            var items = MobileParty.MainParty.ItemRoster
                .Where(e => !e.IsEmpty && e.EquipmentElement.Item != null)
                .OrderByDescending(e => e.EquipmentElement.Item.Value)
                .ToList();
            int total = items.Count;
            int pages = (total + per - 1) / per;
            if (page < 0) page = 0;
            if (page >= pages && pages > 0) page = pages - 1;
            int start = page * per;
            int end = System.Math.Min(start + per, total);

            for (int i = start; i < end; i++)
            {
                var item = items[i].EquipmentElement.Item;
                int delta = GiftRelationDelta(target, item);
                var captured = item;
                // 本地化：送礼选项文本（物品名+好感增量）
                options.Add(new StoryOptionVM(LWNTextHelper.ResolveCompound("LWN_ui_interact_gift_option", ("ITEM", item.Name.ToString()), ("DELTA", delta.ToString())), () =>
                {
                    AgentControlHelper.TransferItems(Hero.MainHero, target, captured, 1);
                    ChangeRelationAction.ApplyPlayerRelation(target, delta);
                    // 本地化：送礼成功消息
                    InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_interact_gift_give_msg", ("ITEM", captured.Name.ToString()), ("NAME", target.Name.ToString()), ("DELTA", delta.ToString())), Colors.Green));
                    RefreshInitialOptions();
                // 本地化：送礼选项提示
                }, LWNTextHelper.ResolveCompound("LWN_ui_interact_gift_tooltip", ("DELTA", delta.ToString()))));
            }
            // 本地化：送礼分页按钮（上一页/下一页）
            if (page > 0) options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_prev_page", "[Previous Page]"), () => OpenGiftMenu(target, page - 1)));
            // 本地化：送礼分页按钮（下一页）
            if (page < pages - 1) options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_next_page", "[Next Page]"), () => OpenGiftMenu(target, page + 1)));
            // 本地化：返回选项
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_back", "[Back]"), () => RefreshInitialOptions()));
            options.Reverse();
            _vm.ShowOptions(options.ToArray());
        }

        private int GiftRelationDelta(Hero target, ItemObject item)
        {
            float baseGain = item.Value / 100f;
            float typeMult = 1.0f;
            var mem = AllNpcMemoryManager.GetMemory(target.StringId);
            var profile = mem != null ? mem._profile : null;
            if (profile != null)
            {
                if (profile.Desire == NPCProfile.DesireEnum.Greedy) typeMult *= 1.5f;
                if (MatchDesireType(profile.DesireType, item)) typeMult *= 1.5f;
            }
            return (int)MathF.Clamp(baseGain * typeMult, 1f, 30f);
        }

        private bool MatchDesireType(NPCProfile.DesireTypeEnum desire, ItemObject item)
        {
            switch (desire)
            {
                case NPCProfile.DesireTypeEnum.Weapon: return item.HasWeaponComponent;
                case NPCProfile.DesireTypeEnum.Book: return item.ItemType == ItemObject.ItemTypeEnum.Book;
                case NPCProfile.DesireTypeEnum.Money: return item.Value >= 1000; // 爱财者看重高价物
                default: return false;
            }
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

                // A2. 核心机制：C# 掷骰子（🔴 2026-08-13 d20 风格：掷点 ≥ 目标阈值成功——目标 = 1 − 成功率）
                float roll = MBRandom.RandomFloat;
                bool isSuccess = roll >= (1f - skillCheckOption.SuccessChance);
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
            if (!MapEncounterDialogState.Active)
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
                // 本地化：LLM 失败时兜底台词
                _vm.DialogueContent = LWNTextHelper.ResolveText("LWN_ui_interact_err_reply", "...Huh? What?");
                // 本地化：LLM 失败时的离开选项
                _vm.ShowOptions(new[] { new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_leave_short", "Leave"), () => { _vm.Close();
                //    AgentAIController.Instance.SendEventToAgent(_targetAgent, "EndInteraction", Agent.Main);

                AgentAIController.Instance.BroadcastEventInRange(Agent.Main.Position,15.0f,"EndInteraction", false, Agent.Main);
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
                        // 本地化：读心切换按钮（查看明面回复）
                mindBtn.OptionText = LWNTextHelper.ResolveText("LWN_ui_interact_mind_show_reply", "[Read Mind] Show the surface reply");
                        _isReadingMind = true;
                    }
                    else
                    {
                        // 正常模式：显示明面回复
                        _vm.DialogueContent = currentCasualResponse.NpcReply;
                        _isReadingMind = false;
                        // 本地化：读心切换按钮（查看内心独白）
                mindBtn.OptionText = LWNTextHelper.ResolveText("LWN_ui_interact_mind_show_thoughts", "[Read Mind] Show inner thoughts");
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
                // 本地化：读心切换按钮（查看明面回复）
                mindBtn.OptionText = LWNTextHelper.ResolveText("LWN_ui_interact_mind_show_reply", "[Read Mind] Show the surface reply");
                _isReadingMind = true;
            }
            else
            {
                // 正常模式：显示明面回复
                _vm.DialogueContent = _currentLog.NpcReply;
                _isReadingMind=false;
                // 本地化：读心切换按钮（查看内心独白）
                mindBtn.OptionText = LWNTextHelper.ResolveText("LWN_ui_interact_mind_show_thoughts", "[Read Mind] Show inner thoughts");
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
                // 本地化：LLM 响应解析失败兜底
                _vm.DialogueContent = LWNTextHelper.ResolveText("LWN_ui_interact_msg_unclear", "(Their words are hard to make out...)");
                UpdateUiForNextTurn(new List<NegotiationCard>(), false);
                return json;
            }


            NegotiationState state = _memory.CurrentNegotiationState;
            //这个下面处理了显示字幕的逻辑
            UpdateNpcVisuals(result.NpcReply, result.NpcEmotion, result.NpcAction,result.NpcThinking);
            float calculatedDelta = 0f;
            float finalMultiplier = 1.0f; // 默认为 1
            // 本地化：谈判代价描述前缀
            string tacticDesc= LWNTextHelper.ResolveText("LWN_ui_interact_tactic_paid", "paid ");
            float chipsValue = 0f;



            if (selectedOption != null)
            {
                // 性格倍率（纯 C#，命中 NPC 性格弱点/抗性）——以前写好却没人用，现在接回结算路径。
                float traitMult = NegotiationRegistry.CalculateMultiplier(selectedOption, state);
                // LLM 只做「润色微调」：配了才用它的 delta，没配就 1.0（纯性格驱动）。
                float llmMult = Settings.Instance.IsLLMConfigured ? Mathf.Clamp(result.DeltaMultiplier, 0.5f, 2.0f) : 1.0f;
                finalMultiplier = Mathf.Clamp(traitMult * llmMult, 0.1f, 5.0f);
                float tacticBaseScore = state.TargetThreshold*0.02f;//比如嘴炮基础分
                chipsValue = selectedOption.Chips.Sum(x => x.EstimatedValue);
                calculatedDelta = (tacticBaseScore + chipsValue) * finalMultiplier;

                
                foreach (var oneChip in selectedOption.Chips)
                {
                    // 本地化：谈判代价筹码描述
                    tacticDesc += LWNTextHelper.ResolveCompound("LWN_ui_interact_tactic_chips", ("AMOUNT", oneChip.Amount.ToString()), ("TYPE", oneChip.Type.ToString()));
                }
                
                if (Settings.Instance.ShowDebugMessages)
                    InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_interact_msg_negotiation_calc", ("BASE", tacticBaseScore.ToString()), ("CHIPS", chipsValue.ToString()), ("MULT", finalMultiplier.ToString()), ("TOTAL", calculatedDelta.ToString()))));
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

                // 谈判结束标题（成功/破裂）——调试遗留变量，未在 UI 展示
                string endText = isWin
                    // 【谈判达成】
                    ? LWNTextHelper.ResolveText("LWN_ui_interact_nego_win", "Negotiation succeeded")
                    // 【谈判破裂】
                    : LWNTextHelper.ResolveText("LWN_ui_interact_nego_fail", "Negotiation broke down");

                // 结束后的选项
                var endOptions = new List<StoryOptionVM>();
                if (isWin)
                {
                    // 本地化：谈判成功选项
                    endOptions.Add(new StoryOptionVM(LWNTextHelper.ResolveCompound("LWN_ui_interact_win_option", ("NAME", _targetHero.Name.ToString())), () =>
                    {
                        ExecuteTransaction(_draftProposal);
                       // AgentAIController.Instance.SendEventToAgent(_targetAgent, "EndInteraction", Agent.Main);

                        AgentAIController.Instance.BroadcastEventInRange(Agent.Main.Position, 15.0f, "EndInteraction", false, Agent.Main);
                        GroupStageManager.Reset(Agent.Main);
                        _vm.Close();
                    }));
                }
                else
                {
                    // 本地化：谈判破裂选项
                    endOptions.Add(new StoryOptionVM(LWNTextHelper.ResolveCompound("LWN_ui_interact_loss_option", ("NAME", _targetHero.Name.ToString())), () => { _vm.Close(); AgentAIController.Instance.SendEventToAgent(_targetAgent, "EndInteraction", Agent.Main); }));
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
            // P8：无 LLM 时开场被填成空选项数组（非 null），原判空失效会让玩家只剩「拔刀/读心」。
            // 这里回退到本地意图菜单（仍先放出开场台词）。
            if (!Settings.Instance.IsLLMConfigured &&
                (openingData.PlayerNextOptions == null || openingData.PlayerNextOptions.Count == 0))
            {
                if (!string.IsNullOrEmpty(openingData.NpcReply))
                    UpdateNpcVisuals(openingData.NpcReply, openingData.NpcEmotion, openingData.NpcAction, openingData.NpcThinking);
                RefreshInitialOptions();
                return;
            }
            // LLM 响应可能缺少 player_next_options（JSON 不完整 / LLM 未配置）
            if (openingData.PlayerNextOptions == null)
                return;

            UpdateNpcVisuals(openingData.NpcReply, openingData.NpcEmotion, openingData.NpcAction, openingData.NpcThinking);
            // 3. 构建开场专属的选项列表
            var options = new List<StoryOptionVM>();

            // 遍历 LLM 生成的 3 个选项
            foreach (SkillCheckOption checkOpt in openingData.PlayerNextOptions)
            {
                string skillName = checkOpt.RelatedSkill.Name.ToString();
                string chanceText = $"{(checkOpt.SuccessChance * 100):0}%";
                string btnTitle = $"[{skillName} {chanceText}] {checkOpt.Text} ({checkOpt.TacticRaw})";
                // 本地化：开场选项后果提示
                string tooltip = LWNTextHelper.ResolveCompound("LWN_ui_interact_tooltip_consequences", ("PREDICTION", checkOpt.Prediction));
                var vmOption = new StoryOptionVM(btnTitle, async () =>
                {
                    await HandlePlayerInputAsync(checkOpt.Text, null, checkOpt);
                }, tooltip);

                options.Add(vmOption);               
            }

            // 4. 添加一个兜底的战斗选项 (防止卡死)
            // 本地化：拔刀选项
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_draw_sword", "[Draw] Words are wasted"), () =>
            {
                _vm.Close();
                AgentAIController.Instance.SendEventToAgent(_targetAgent, "order_attack", Agent.Main);
                Agent.Main.TryToWieldWeaponInSlot(EquipmentIndex.WeaponItemBeginSlot, Agent.WeaponWieldActionType.WithAnimation, false);
            // 本地化：拔刀选项提示（放弃交涉，直接动手）
            }, LWNTextHelper.ResolveText("LWN_ui_interact_tooltip_draw_sword", "Abandon negotiation and fight")));

            // 本地化：读心选项（查看内心独白）
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_mind_show_thoughts", "[Read Mind] Show inner thoughts"), () => {

                ToggleReadThinking(_memory.CurrentNegotiationState);
            // 本地化：读心选项提示（尝试查看对方的真实想法）
            }, LWNTextHelper.ResolveText("LWN_ui_interact_tooltip_mind", "Try to glimpse their true thoughts"), 0, null, null, "MIND_READING"));


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
                    // 本地化：技能检定失败兜底台词
                    NpcReply = LWNTextHelper.ResolveText("LWN_ui_interact_msg_dont_understand", "...I don't quite understand you."),
                    NpcEmotion = "Neutral",
                    NpcAction = "NONE"
                };
            }
            UpdateNpcVisuals(result.NpcReply, result.NpcEmotion, result.NpcAction, result.NpcThinking);
            var fixedOptions = new List<StoryOptionVM>();
            if (isSuccess)
            {
                // 本地化：检定成功选项
                fixedOptions.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_solved", "[Leave] Resolved"), () =>
                {
                    _vm.Close();
                    _memory.CurrentInitiative = null; // 清除开场状态，避免重复触发
                                                      //AgentAIController.Instance.SendEventToAgent(_targetAgent, "EndInteraction", Agent.Main);

                    AgentAIController.Instance.BroadcastEventInRange(Agent.Main.Position, 15.0f, "EndInteraction", false, Agent.Main);
                // 本地化：检定成功选项提示
                }, LWNTextHelper.ResolveText("LWN_ui_interact_tooltip_end", "End the conversation")));
            }
            else
            {
                var conflict = _memory.CurrentInitiative.ConflictData;
                var newState = new NegotiationState(_targetAgent, conflict);
                //这里加入谈判分析
                // 本地化：谈判分析弹窗（标题/按钮）
                InformationManager.ShowInquiry(new InquiryData(
        // 谈判分析：{NAME}
        LWNTextHelper.ResolveCompound("LWN_ui_interact_inquiry_analysis_title", ("NAME", newState.Name)),
        newState.CalculationLog.ToString(),
        // 我心里有数了
        true, false, LWNTextHelper.ResolveText("LWN_ui_interact_btn_understood", "I understand"), null,
        () => { }, null));


                // 本地化：检定失败补救选项
                fixedOptions.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_try_recover", "[Try to Recover] Enter negotiation"), async () =>
                {
                    _memory.CurrentNegotiationState = newState;
                    // 2. 构造第一张虚拟卡牌，刷新界面
                    // 本地化：补救谈判起始卡牌文本
                    var startCard = new NegotiationCard("Plead", LWNTextHelper.ResolveText("LWN_ui_interact_card_please_listen", "(Please, just hear me out...)"));

                    // 3. 进入谈判循环
                    // 本地化：补救谈判玩家发言文本
                    await HandlePlayerInputAsync(LWNTextHelper.ResolveText("LWN_ui_interact_input_recover", "(Trying to recover the situation and explain)"), startCard);

                // 本地化：补救选项提示
                }, LWNTextHelper.ResolveText("LWN_ui_interact_tooltip_enter_nego", "Enter negotiation mode")));

                // 选项 2: 战斗 (谈崩了)
                // 本地化：拔刀选项（谈崩）
                fixedOptions.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_draw_sword", "[Draw] Words are wasted"), () =>
                {
                    _vm.Close();
                    AgentAIController.Instance.SendEventToAgent(_targetAgent, "order_attack", Agent.Main);
                    Agent.Main.TryToWieldWeaponInSlot(EquipmentIndex.WeaponItemBeginSlot, Agent.WeaponWieldActionType.WithAnimation, false);

                // 本地化：拔刀选项提示
                }, LWNTextHelper.ResolveText("LWN_ui_interact_tooltip_draw_sword", "Abandon negotiation and fight")));

                // 本地化：投降选项
                fixedOptions.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_surrender_option", "[Surrender] (accept any judgment)"), () =>
                {
                    // 让自己成为对方的俘虏

                    _vm.Close();
                    _memory.CurrentInitiative = null; // 清除开场状态，避免重复触发
                }));
                
            }
            // 本地化：读心选项（技能检定流程）
            fixedOptions.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_mind_show_thoughts", "[Read Mind] Show inner thoughts"), () => {

                ToggleReadThinking(_memory.CurrentNegotiationState);
            // 本地化：读心选项提示（尝试查看对方的真实想法）
            }, LWNTextHelper.ResolveText("LWN_ui_interact_tooltip_mind", "Try to glimpse their true thoughts"), 0, null, null, "MIND_READING"));


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
                // 本地化：LLM 响应解析失败兜底
                _vm.DialogueContent = LWNTextHelper.ResolveText("LWN_ui_interact_msg_unclear", "(Their words are hard to make out...)");
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
                // 本地化：谈判分析弹窗（标题/按钮）
                InformationManager.ShowInquiry(new InquiryData(
                    // 谈判分析：{NAME}
                    LWNTextHelper.ResolveCompound("LWN_ui_interact_inquiry_analysis_title", ("NAME", newState.Name)),
                    newState.CalculationLog.ToString(), // 这里显示刚才记录的日志
                    true, false, LWNTextHelper.ResolveText("LWN_ui_interact_btn_understood", "I understand"), null,
                    () => { }, null));

            }

            // 3. 生成下一轮按钮
            //这里考虑看看之后要不要预先生成好谈判初始选项
            if (result.SuggestNegotiationStart)
            {
              


                // 强制给一个"开始谈判"的按钮，点击后再次调用 HandlePlayerInputAsync 刷新出真正的谈判卡牌
                // 本地化：开始协商选项
                var startOpt = new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_start_nego", "[Start Negotiating] (enter the game)"), async () =>
                {
                    _vm.LockPrediction();
                    //需要构造一个Card，用于刷新谈判界面
                    // 本地化：协商起始卡牌文本
                    var startCard = new NegotiationCard("Flatter", LWNTextHelper.ResolveText("LWN_ui_interact_card_start_nego", "(Start negotiating)"));                  

                    // 传入空输入，旨在刷新谈判界面的第一轮 Prompt
                    // 本地化：开始协商玩家发言文本
                    await HandlePlayerInputAsync(LWNTextHelper.ResolveText("LWN_ui_interact_input_start_nego", "(With a determined look, ready to negotiate)"), startCard);
                // 本地化：开始协商选项提示
                }, LWNTextHelper.ResolveText("LWN_ui_interact_tooltip_enter_game", "Enter the game of wits"));
                //补充一个，放弃谈判，回归闲聊
                // 本地化：取消协商选项
                var cancelOpt = new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_misunderstood", "[You Misunderstand] (Return to small talk)"), () =>
                {
                    _memory.CurrentNegotiationState = null ;
                    // 正常闲聊选项
                    UpdateUiForNextTurn(result.PlayerNextOptions, false);
                // 本地化：取消协商选项提示
                }, LWNTextHelper.ResolveText("LWN_ui_interact_tooltip_back_chat", "Return to small talk"));
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
                // 本地化：当前提案为空时的占位文本
                string currentOfferStr = _draftProposal.chips.Count > 0   ? $"{_draftProposal.GetDescription()}"  : LWNTextHelper.ResolveText("LWN_ui_interact_no_offer_yet", "No conditions proposed yet");            

                var customProposalOpt = new StoryOptionVM(
                    // 本地化：自定义提案选项文本
                    LWNTextHelper.ResolveCompound("LWN_ui_interact_custom_proposal", ("OFFER", currentOfferStr)), // 按钮文本
                    () => OpenCustomProposalMenu(),      // 点击进入子菜单
                    // 本地化：自定义提案选项提示
                    LWNTextHelper.ResolveText("LWN_ui_interact_tooltip_custom_proposal", "Adjust the proposal or submit it"),                      // Tooltip
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
                    // 本地化：卡牌代价提示前缀
                    string PredictText = LWNTextHelper.ResolveCompound("LWN_ui_interact_cost_prefix", ("COST", costStr));
                    if(!string.IsNullOrEmpty(card.PredictedImpact))
                        // 本地化：卡牌预测提示行
                        PredictText += LWNTextHelper.ResolveCompound("LWN_ui_interact_predict_line", ("PREDICTION", card.PredictedImpact));
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

            // 本地化：读心选项（下一轮卡牌）
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_mind_show_thoughts", "[Read Mind] Show inner thoughts"), () =>            {

                ToggleReadThinking(_memory.CurrentNegotiationState);
            // 本地化：读心选项提示（尝试查看对方的真实想法）
            }, LWNTextHelper.ResolveText("LWN_ui_interact_tooltip_mind", "Try to glimpse their true thoughts"),0,null,null, "MIND_READING"));


            //自由聊天卡
            // 本地化：自由对话卡牌文本
            NegotiationCard freeTalkCard = new NegotiationCard("Flatter", LWNTextHelper.ResolveText("LWN_ui_interact_card_free_talk", "Free talk"));


            // 本地化：寒暄选项
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_smalltalk_opt", "[Small Talk]"), () =>
            {



                InformationManager.ShowTextInquiry(new TextInquiryData(
                  // 本地化：自由聊天输入框（标题/提示/按钮）
                  LWNTextHelper.ResolveText("LWN_ui_interact_smalltalk_title", "Small Talk"), LWNTextHelper.ResolveCompound("LWN_ui_interact_smalltalk_prompt", ("NAME", npcName)), true, true, LWNTextHelper.ResolveText("LWN_ui_interact_btn_send", "Send"), LWNTextHelper.ResolveText("LWN_ui_interact_btn_cancel", "Cancel"),
                  async (text) =>
                  {
                      _vm.LockPrediction();
                      freeTalkCard.Text = text;
                      await HandlePlayerInputAsync(freeTalkCard.Text, freeTalkCard);
                  }, null));
            // 本地化：寒暄选项提示
            }, LWNTextHelper.ResolveText("LWN_ui_interact_tooltip_free_input", "Type whatever you want to say")));

            // 本地化：离开对话选项
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_leave_farewell", "[Leave] Farewell"), () => {
                //AgentAIController.Instance.SendEventToAgent(_targetAgent, "EndInteraction", Agent.Main);

                AgentAIController.Instance.BroadcastEventInRange(Agent.Main.Position, 15.0f, "EndInteraction", false, Agent.Main);
                GroupStageManager.Reset(Agent.Main);
                _vm.Close();
            // 本地化：离开对话选项提示
            }, LWNTextHelper.ResolveText("LWN_ui_interact_tooltip_exit", "Exit the conversation")));
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
                    // 本地化：提交提案选项
                    LWNTextHelper.ResolveText("LWN_ui_interact_confirm_submit", "[Confirm and Speak]"),
                    () => {
                        // 弹出输入框
                        InformationManager.ShowTextInquiry(new TextInquiryData(
                         // 本地化：提交提案输入框（标题/内容/按钮）
                         LWNTextHelper.ResolveText("LWN_ui_interact_inquiry_confirm_title", "Confirm the terms"), LWNTextHelper.ResolveCompound("LWN_ui_interact_inquiry_confirm_prompt", ("TERMS", _draftProposal.GetDescription())),
                         // 发送
                         true, true, LWNTextHelper.ResolveText("LWN_ui_interact_btn_send", "Send"), LWNTextHelper.ResolveText("LWN_ui_interact_btn_cancel", "Cancel"),
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
                    // 本地化：提交提案选项提示
                    LWNTextHelper.ResolveText("LWN_ui_interact_tooltip_send_terms", "Send the selected chips to the other party"),
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
            // 本地化：加注选项
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_add_wager", "[Raise] Add chips"), () => OpenCategorySelectMenu_Refactored()));

            // 3. 减注
            if (_draftProposal.chips.Count > 0)
            {
                // 本地化：减注选项
                options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_remove_wager", "[Fold] Remove chips"), () => OpenRemoveMenu()));
            }

            // 4. 返回上一级
            // 本地化：返回选项
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_back", "[Back]"), () =>
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



            if (Settings.Instance.ShowDebugMessages)
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_interact_msg_progress", ("CURRENT", state.CurrentProgress.ToString()), ("TARGET", state.TargetThreshold.ToString()), ("GAIN", gainValue.ToString()), ("PCT", $"{gainPercent:F1}")), Colors.Green));
         
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

            // 本地化：加注选项（增加条件）
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_add_condition", "[Raise] Add conditions"), () => OpenCategorySelectMenu_Refactored()));

            if (_draftProposal.chips.Count > 0)
            {
                // 本地化：减注选项（撤回条件）
                options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_remove_condition", "[Fold] Withdraw conditions"), () => OpenRemoveMenu()));
            }

            // 本地化：返回选项
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_back", "[Back]"), () =>
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
            // 本地化：资源类别选项（个人金钱）
            AddNumericResourceOption(options, LWNTextHelper.ResolveText("LWN_ui_interact_res_personal_gold", "Personal Gold"), NegotiationCostType.PersonalGold, playerRes.PersonalGold);

            // 1.2 势力资金 (FactionGold)
            // 只有当玩家是国王或有权限时显示
            if (playerRes.FactionGold > 0)
            {
                // 本地化：资源类别选项（势力公款）
                AddNumericResourceOption(options, LWNTextHelper.ResolveText("LWN_ui_interact_res_faction_gold", "Faction Funds"), NegotiationCostType.FactionGold, playerRes.FactionGold);
            }

            // 1.3 物品 (Item) - 打开物品选择器
            // 本地化：物品纳贡选项
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_res_item", "[Items] (tribute)"), () => OpenItemSelectMenu()));

            // 1.4 城池 (Settlement) - 打开城池选择器
            if (playerRes.OwnedSettlements.Count > 0)
            {
                // 本地化：城池割地选项
                options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_res_settlement", "[Fiefs] (cede land)"), () => OpenFiefSelectMenu()));
            }

            // ================= Group 2: 社会资本 (抽象资源) =================

            // 2.1 善名 (Reputation) - "我用我的名誉担保"
            // 逻辑：玩家消耗声望值，一旦违约或失败，声望暴跌
            if (playerRes.Reputation > 10) // 门槛
            {
                // 本地化：资源类别选项（名誉担保）
                AddNumericResourceOption(options, LWNTextHelper.ResolveText("LWN_ui_interact_res_reputation", "Reputation"), NegotiationCostType.Reputation, (int)playerRes.Reputation);
            }

            // 2.2 人情 (SocialRelation) - "看在我们的交情上"
            if (playerRes.SocialRelation > 5)
            {
                // 这里可以直接最大值梭哈，或者输入数值
                // 本地化：资源类别选项（动用人情）
                AddNumericResourceOption(options, LWNTextHelper.ResolveText("LWN_ui_interact_res_social", "Social Favor"), NegotiationCostType.SocialRelation, (int)playerRes.SocialRelation);
            }

            // 2.3 恶名 (Notoriety) - "你也不想把事情闹大吧" (威慑)
            // 这是一个特殊的反向资源，通常作为 Threat Tactic 的加成，但这里作为筹码可能意味着 "我承诺不使用暴力/不散布谣言"
            // 或者理解为：投入恶名 = 进行恐吓操作
            if (playerRes.Notoriety > 0)
            {
                // 恐吓不需要输入数量，通常是一次性行为
                // 本地化：恶名恐吓选项
                options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_res_notoriety", "[Notoriety] Intimidate"), () => {
                    // 本地化：恶名筹码名称与描述
                    AddWagerItem(new Chip(NegotiationCostType.Notoriety, LWNTextHelper.ResolveText("LWN_ui_interact_chip_violence", "violent threat"), LWNTextHelper.ResolveText("LWN_ui_interact_chip_violence_desc", "fills them with fear"), 100)); // 这里的Value 100是估值
                    OpenCustomProposalMenu();
                }));
            }

            // ================= Group 3: 期货与承诺 =================

            // 3.1 承诺 (Promise)
            // 本地化：空头支票选项
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_res_promise", "[Empty Promise] (pledge)"), () => OpenPromiseSubMenu()));


            // 本地化：返回选项
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_back", "[Back]"), () => OpenCustomProposalMenu()));
            _vm.ShowOptions(options.ToArray());
        }

        // --- 辅助方法：添加数值型资源的选项 ---
        private void AddNumericResourceOption(List<StoryOptionVM> options, string label, NegotiationCostType type, int maxAvailable)
        {
            // 本地化：数值资源选项文本
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveCompound("LWN_ui_interact_res_label", ("LABEL", label)), () =>
            {
                // 1. 计算这一类资源已经被草稿箱，以及本次的累计资源池占用了多少
                int currentWagered = _draftProposal.chips
                    .Where(c => c.Type == type)
                    .Sum(c => (int)c.Amount); 

                int realAvailable = maxAvailable - currentWagered;

                if (realAvailable <= 0)
                {
                    // 本地化：资源不足消息
                    InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_interact_msg_not_enough", ("LABEL", label))));
                    return;
                }

                // 本地化：投入资源输入框（标题/内容/按钮）
                InformationManager.ShowTextInquiry(new TextInquiryData(
                   // 投入{LABEL}
                   LWNTextHelper.ResolveCompound("LWN_ui_interact_inquiry_put_title", ("LABEL", label)),
                   // 背包总量: {MAX} 已加注: {WAGERED} 当前剩余可用: {A...
                   LWNTextHelper.ResolveCompound("LWN_ui_interact_inquiry_put_prompt", ("MAX", maxAvailable.ToString()), ("WAGERED", currentWagered.ToString()), ("AVAILABLE", realAvailable.ToString())),
                   // 确认
                   true, true, LWNTextHelper.ResolveText("LWN_ui_interact_btn_confirm", "Confirm"), LWNTextHelper.ResolveText("LWN_ui_interact_btn_cancel", "Cancel"),
                   (text) => {
                       if (int.TryParse(text, out int amount))
                       {
                           amount = (int)MathF.Clamp((float)amount, 0, realAvailable);
                           if (amount > 0)
                           {
                               // 【关键】创建 Chip 时严格传入 Type
                               // 本地化：筹码名称（数量+资源标签）
                               AddWagerItem(new Chip(type, LWNTextHelper.ResolveCompound("LWN_ui_interact_chip_amount", ("AMOUNT", amount.ToString()), ("LABEL", label)), label, amount));
                           }
                       }
                   }, null));
            }));
        }


        private void OpenPromiseSubMenu()
        {
            var options = new List<StoryOptionVM>();
            // 选项 B: 预设 - 联姻
            // 本地化：承诺分期选项
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_promise_installments", "[Promise: Installments]"), () => {
                // 本地化：分期支付筹码名
                AddWagerItem(new Chip(NegotiationCostType.Promise, LWNTextHelper.ResolveText("LWN_ui_interact_chip_installments", "installment payments"), "Promise", 500));
            }));
            // 选项 B: 预设 - 联姻
            // 本地化：承诺联姻选项
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_promise_marriage", "[Promise: Marriage]"), () => {
                // 本地化：家族联姻筹码名
                AddWagerItem(new Chip(NegotiationCostType.Promise, LWNTextHelper.ResolveText("LWN_ui_interact_chip_marriage", "family marriage"), "Promise", 500));
            }));

            // 选项 C: 预设 - 晋升
            // 本地化：承诺晋升选项
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_promise_promotion", "[Promise: Promotion]"), () => {
                // 本地化：推荐晋升筹码名
                AddWagerItem(new Chip(NegotiationCostType.Promise, LWNTextHelper.ResolveText("LWN_ui_interact_chip_promotion", "recommended promotion"), "Promise", 200));
            }));

            // 本地化：返回选项
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_back", "[Back]"), () => OpenCategorySelectMenu_Refactored()));
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
                // 注意：这里默认将背包里该物品的"堆叠数量"全部作为筹码
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
                // 本地化：物品选择翻页按钮（上一页）
                options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_prev_page", "[Previous Page]"), () => OpenItemSelectMenu(page - 1)));
            }

            if (page < totalPages - 1)
            {
                // 本地化：物品选择翻页按钮（下一页）
                options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_next_page", "[Next Page]"), () => OpenItemSelectMenu(page + 1)));
            }

            // 7. 返回按钮
            // 本地化：返回选项
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_back", "[Back]"), () => OpenCategorySelectMenu_Refactored()));

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

            // 本地化：返回选项
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_back", "[Back]"), () => OpenCategorySelectMenu_Refactored()));

            // 支持翻页逻辑 (如果城池太多)可以在这里加分页

            _vm.ShowOptions(options.ToArray());
        }

        // [新增] 移除菜单
        private void OpenRemoveMenu()
        {
            var options = new List<StoryOptionVM>();
            foreach (var item in _draftProposal.chips)
            {
                // 本地化：移除筹码选项
                options.Add(new StoryOptionVM(LWNTextHelper.ResolveCompound("LWN_ui_interact_remove_item", ("NAME", item.Name)), () =>
                {
                    _draftProposal.Remove(item);
                    OpenProposalRootMenu();
                }));
            }
            // 本地化：返回选项
            options.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_ui_interact_back", "[Back]"), () => OpenProposalRootMenu()));
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
                // 本地化：读心切换按钮（查看明面回复）
                mindBtn.OptionText = LWNTextHelper.ResolveText("LWN_ui_interact_mind_show_reply", "[Read Mind] Show the surface reply");
                _isReadingMind = true;
            }
            else
            {
                // -> 切换回正常模式
                _vm.DialogueContent = _cachedCurrentReply;
                // 本地化：读心切换按钮（查看内心独白）
                mindBtn.OptionText = LWNTextHelper.ResolveText("LWN_ui_interact_mind_show_thoughts", "[Read Mind] Show inner thoughts");
                _isReadingMind = false;
            }
        }

        //更新引擎表现，镜头、说话、动作、执行操作等
        private void UpdateNpcVisuals(string reply, string emotion, string action,string thoughts )
        {
            // 1. 缓存数据（核心修改）
            _cachedCurrentReply = reply;
            // 如果没有传 thoughts (比如旧代码或某些特殊情况)，给个默认值防止报错
            // 本地化：读心内容兜底
            _cachedCurrentThinking = string.IsNullOrEmpty(thoughts) ? LWNTextHelper.ResolveText("LWN_ui_interact_thoughts_default", "(Can't tell what they're thinking...)") : thoughts;

            // 重置读心状态为 false，因为 NPC 说了新话，默认显示明面回复
            _isReadingMind = false;
            // 更新文本
            lock (_memory)
            {
                _memory.AddHistory("assistant", $"{_targetAgent.Name}: {reply}");
            }
            _vm.Show(_targetAgent.Name.ToString(), reply);
            if (Settings.Instance.ShowDebugMessages)
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_interact_msg_reply", ("NAME", _targetAgent.Name.ToString()), ("REPLY", reply)), Colors.Red));
            // 更新动画/表情动作
            if (!string.IsNullOrEmpty(emotion))
            {
                AgentControlHelper.SetPose(_targetAgent, _matcher.GetAnimByEmotion(emotion));
            }

            // 触发执行Action（2026-08-10 升级：defender 双向化——attacker=说话 NPC，defender=玩家；空间由内部裁决）
            if (!string.IsNullOrEmpty(action) && _targetHero != null)
            {
                ActionHandler.HandleAction(action, _targetHero, Hero.MainHero, _targetAgent);
            }

            // 镜头打向NPC（地图遭遇 mission 已有固定镜头，跳过）
            if (!MapEncounterDialogState.Active)
                VisualCommands.SmartCamera(_targetAgent, Agent.Main);
        }

        private void ExecuteTransaction(DraftProposal proposal)
        {
            foreach (var item in proposal.chips)
            {
                switch (item.Type)
                {
                    case NegotiationCostType.PersonalGold:
                        AgentControlHelper.TransferGold(Hero.MainHero, _targetHero, item.Amount);
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
            // 🔴 2026-08-11 修复：对话未开始过（_memory 未赋值，如 IM 弹窗确认路径误触发 OnDialogClosed）
            // → 无对话可收尾，直接返回（铁律 2 null-guard；实机日志 11:13:37 NullReferenceException）
            if (_memory == null) return null;
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
