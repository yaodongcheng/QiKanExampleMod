using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════
    // PlanCommandFlow.cs — 密谋命令流程（§8 计划阶段 UX）
    //
    // 流程：Plot 交互（G 长按）→ 随从冒泡开场 → 系统输入框（ShowTextInquiry）输入命令
    //     → 快照构建 + LLM 调用（意图分类 + 计划 + 可含 questions）
    //     → 澄清轮（≤2 轮，冒泡问句 + 输入框回答追加上下文）→ 批准轮（系统弹窗 同意/算了）
    //     → order_execute_plan 下发（ReactiveAgent 反应计划一并应用）→ 确定性执行
    //
    // 复用：ShowTextInquiry（自由输入）/ ShowInquiry（确认弹窗，既有先例）
    //       + LLMService（支持 temperature/max_tokens 4000）。
    // 🔴 禁止 DialogChoice/StoryDialogVM——废弃对话系统；随从台词一律 AgentSay 头顶冒泡。
    // LLM 结果回主线程消费（Tick 轮询），避免后台线程动 UI。
    //
    // 铁律 1：IsLLMReady 总闸——不可用 → Plot 行不出现/点开提示"随从想不出主意"。
    // ═══════════════════════════════════════════════════════════════

    public static class PlanCommandFlow
    {
        private static Agent _companion;
        private static bool _isActive;
        private static bool _processing;
        private static string _command;
        private static readonly List<string> _history = new List<string>();
        private static int _clarifyRound;
        private static bool _awaitingClarifyAnswer;   // 澄清轮：输入框回答要追加上下文
        private static string _lastClarifyQ;           // 澄清轮：冒泡问句（历史记录用）
        private static PlanResponse _pendingResult;
        private static bool _resultReady;

        public static bool IsActiveFor(Agent agent)
        {
            return _isActive && agent != null && agent == _companion;
        }

        /// <summary>是否正在密谋（Talk/Pickpocket 行互斥移除用）。</summary>
        public static bool IsActive => _isActive;

        // ═══════════════════════════════════════════════════════════
        // 入口（InteractionMissionView ExecuteInteraction 分发）
        // ═══════════════════════════════════════════════════════════

        /// <summary>Plot 交互入口（对随从下达自然语言命令，§8.1）。</summary>
        public static void Start(Agent companion)
        {
            if (companion == null || _isActive) return;
            // 铁律 1：LLM 总闸（配置齐全 + 连接未失败——显示层已门控，此处兜底触发路径）
            if (!Settings.Instance.IsLLMReady)
            {
                // 本地化：LLM 未配置提示（随从想不出主意）
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText("LWN_plan_no_llm", "The companion cannot think of a plan right now."), Colors.Red));
                return;
            }
            if (!LLMService.IsConnectionOk())
            {
                // 本地化：LLM 连接失败提示（配置在但服务不可达/key 无效——MCM 可测试连接）
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText("LWN_plan_no_conn", "LLM connection failed. Test it in Mod Options."), Colors.Red));
                return;
            }
            var brain = AgentAIController.GetBrainForAgent(companion);
            if (brain == null) return;

            _companion = companion;
            _isActive = true;
            _processing = false;
            _clarifyRound = 0;
            _awaitingClarifyAnswer = false;
            _command = null;
            _history.Clear();

            EnableInputBlock();   // 密谋全程屏蔽 Input 快捷键，防输入框打字误触发（如 H 开面板）
            // 本地化：密谋开场白（随从头顶冒泡示意）
            AgentHudMissionView.AgentSay(companion, LWNTextHelper.ResolveText("LWN_plan_opening", "Quiet... tell me what you need."));
            PromptForCommand();
        }

        /// <summary>停止键（§8.1）：对执行中的随从喊停（当面/密信双通道）。</summary>
        public static void StopPlan(Agent companion)
        {
            if (companion == null) return;
            var executor = PlanExecutor.GetExecutorFor(companion);
            if (executor == null) return;

            float dist = companion.Position.Distance(Agent.Main?.Position ?? companion.Position);
            if (dist < 6f)
            {
                // 本地化：停止键当面喊停（冒泡）
                AgentHudMissionView.AgentSay(companion, LWNTextHelper.ResolveText("LWN_plan_stop_face", "As you say. Stopping."));
            }
            else
            {
                // 本地化：停止键远距离密信中止
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText("LWN_plan_stop_far", "You signal the companion from afar: stop what you are doing."), Colors.Yellow));
            }
            executor.CancelByPlayer();
        }

        // ═══════════════════════════════════════════════════════════
        // 说话/弹窗（🔴 禁止 DialogChoice：台词一律 AgentSay 冒泡，选择一律系统弹窗）
        // ═══════════════════════════════════════════════════════════

        private static GauntletLayer _inputBlocker;   // 纯输入屏蔽层（无 UI）：密谋期间挡住 Input 系统快捷键误触

        /// <summary>密谋全程屏蔽底层输入：inquiry 输入框打字时 H 等快捷键会穿透到 Input 系统
        /// （误开面板/误触发监听）——旧 DialogChoice 层靠 InputRestrictions 屏蔽，拆掉后补回等效机制。
        /// 不加载任何 movie（纯屏蔽层，不算废弃对话系统 UI）。</summary>
        private static void EnableInputBlock()
        {
            var screen = ScreenManager.TopScreen as MissionScreen;
            if (screen == null || _inputBlocker != null) return;
            _inputBlocker = V.NewLayer(1000);
            screen.AddLayer(_inputBlocker);
            _inputBlocker.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
        }

        private static void DisableInputBlock()
        {
            var screen = ScreenManager.TopScreen as MissionScreen;
            if (_inputBlocker != null && screen != null)
            {
                try { screen.RemoveLayer(_inputBlocker); } catch { }
            }
            _inputBlocker = null;
        }

        private static void ShowCompanionLine(string text)
        {
            if (_companion == null) return;
            try
            {
                AgentHudMissionView.AgentSay(_companion, text);
            }
            catch { }
        }

        /// <summary>通用确认弹窗（是/否两按钮）。标题带随从名。</summary>
        private static void ShowConfirm(string body, string okBtn, string cancelBtn,
            Action onOk, Action onCancel)
        {
            // 本地化：通用确认弹窗标题（含随从名）
            string title = LWNTextHelper.ResolveCompound("LWN_plan_msg_title", ("NAME", _companion?.Name?.ToString() ?? ""));
            InformationManager.ShowInquiry(new InquiryData(
                title, body, true, true, okBtn, cancelBtn, onOk, onCancel));
        }

        private static void CloseDialog()
        {
            _isActive = false;
            _companion = null;
            _pendingResult = null;
            _resultReady = false;
            _awaitingClarifyAnswer = false;
            _lastClarifyQ = null;
            DisableInputBlock();   // 解除输入屏蔽，恢复正常快捷键
        }

        // ═══════════════════════════════════════════════════════════
        // 流程
        // ═══════════════════════════════════════════════════════════

        private static void PromptForCommand()
        {
            if (!_isActive || _processing) return;
            string name = _companion?.Name?.ToString() ?? "";
            // 本地化：密谋输入框（标题）
            string title = LWNTextHelper.ResolveText("LWN_plan_input_title", "Plot an Order");
            // 本地化：密谋输入框（提示语，含目标名）
            string prompt = LWNTextHelper.ResolveCompound("LWN_plan_input_prompt", ("NAME", name));
            // 本地化：密谋输入框（发送按钮）
            string send = LWNTextHelper.ResolveText("LWN_ui_interact_btn_send", "Send");
            // 本地化：密谋输入框（取消按钮）
            string cancel = LWNTextHelper.ResolveText("LWN_ui_interact_btn_cancel", "Cancel");
            InformationManager.ShowTextInquiry(new TextInquiryData(
                title, prompt, true, true, send, cancel,
                (text) =>
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        // 空输入 → 放弃本次密谋
                        CloseDialog();
                        return;
                    }
                    if (_awaitingClarifyAnswer)
                    {
                        // 澄清轮：回答追加到原命令再调 LLM
                        _awaitingClarifyAnswer = false;
                        _history.Add($"随从（澄清）：{_lastClarifyQ}\n玩家：{text}");
                        _command = $"{_command}（{text}）";
                    }
                    else
                    {
                        _command = text;
                    }
                    _processing = true;
                    _ = CallPlanAsync(_command);
                },
                () =>
                {
                    // "算了"：放弃本次密谋、随从回默认行为
                    CloseDialog();
                }));
        }

        /// <summary>LLM 一次调用：快照 + 意图分类 + 计划 + 反应计划（§2.2 意图与计划一次调用）。</summary>
        private static async Task CallPlanAsync(string command)
        {
            PlanResponse response = null;
            try
            {
                if (!Settings.Instance.IsLLMReady) { FinishWith(null); return; }
                var snapshot = SceneSnapshot.Build(Mission.Current, agentLimit: 30);
                string persona = BuildPersona(_companion);
                string history = string.Join("\n", _history);
                string intentTable = BuildIntentTable();
                string grammar = BuildGrammar();
                string prompt = PromptBuilder.BuildPlanPrompt(
                    snapshot.ToPromptText(), command, persona, history, intentTable, grammar);
                string json = await LLMService.Instance.ChatAsync(prompt, 4000, true, 0.4f, disableReasoning: true);
                string cleaned = LLMService.CleanJson(json);
                try
                {
                    response = JsonConvert.DeserializeObject<PlanResponse>(cleaned);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[PlanCommandFlow] 计划 JSON 解析失败: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PlanCommandFlow] LLM 调用失败: {ex.Message}");
            }
            FinishWith(response);
        }

        private static void FinishWith(PlanResponse response)
        {
            // 回主线程消费（Tick 轮询）
            _pendingResult = response;
            _resultReady = true;
        }

        /// <summary>主线程消费（InteractionMissionView.OnMissionTick 调用）。</summary>
        public static void Tick()
        {
            if (!_isActive) return;
            if (!_resultReady) return;
            _resultReady = false;
            var response = _pendingResult;
            _pendingResult = null;
            if (response == null)
            {
                // 失败降级（§2.2）：告知玩家 + 释放控制
                _processing = false;
                // 本地化：LLM 失败降级（建议改述重试）
                ShowCompanionLine(LWNTextHelper.ResolveText("LWN_plan_fail_llm", "I could not think of a plan. Tell me again in different words."));
                // 本地化：重试按钮
                string retryBtn = LWNTextHelper.ResolveText("LWN_plan_btn_retry", "Try again");
                // 本地化：放弃按钮
                string giveUpBtn = LWNTextHelper.ResolveText("LWN_plan_btn_giveup", "Never mind");
                ShowConfirm(LWNTextHelper.ResolveText("LWN_plan_fail_llm", "I could not think of a plan. Tell me again in different words."),
                    retryBtn, giveUpBtn, () => PromptForCommand(), () => CloseDialog());
                return;
            }

            // 澄清轮（意图歧义优先澄清，最多 2 轮）：冒泡问句 + 候选，输入框回答追加
            bool needsClarify = response.NeedsClarification
                || (response.Questions != null && response.Questions.Count > 0);
            if (needsClarify && _clarifyRound < 2)
            {
                _clarifyRound++;
                _processing = false;
                var q = response.Questions?[0];
                // 本地化：澄清轮默认问句
                string qText = q?.Q ?? LWNTextHelper.ResolveText("LWN_plan_clarify_default", "What do you mean exactly?");
                _history.Add($"玩家：{_command}");
                // 冒泡：问句 + 候选列表（提示玩家怎么答）
                string bubble = qText;
                if (q != null && q.Options != null && q.Options.Count > 0)
                    bubble += "\n" + string.Join(" / ", q.Options.Select(o => $"「{o}」"));
                ShowCompanionLine(bubble);
                _awaitingClarifyAnswer = true;
                _lastClarifyQ = qText;
                PromptForCommand();
                return;
            }
            if (needsClarify && _clarifyRound >= 2)
            {
                // 澄清超轮 → 诚实放弃
                _processing = false;
                // 本地化：澄清超轮放弃
                ShowCompanionLine(LWNTextHelper.ResolveText("LWN_plan_clarify_exhausted", "I still do not understand. Perhaps another time."));
                // 本地化：重试按钮
                string retryBtn = LWNTextHelper.ResolveText("LWN_plan_btn_retry", "Try again");
                // 本地化：放弃按钮
                string giveUpBtn = LWNTextHelper.ResolveText("LWN_plan_btn_giveup", "Never mind");
                ShowConfirm(LWNTextHelper.ResolveText("LWN_plan_clarify_exhausted", "I still do not understand. Perhaps another time."),
                    retryBtn, giveUpBtn, () => PromptForCommand(), () => CloseDialog());
                return;
            }

            // 词表外命令（CUSTOM）→ 诚实拒绝
            var intentType = response.Intent?.IntentType;
            if (string.Equals(intentType, "CUSTOM", StringComparison.OrdinalIgnoreCase)
                || response.Plan == null)
            {
                _processing = false;
                // 本地化：词表外命令诚实拒绝
                string rejectText = !string.IsNullOrEmpty(response.Reply) ? response.Reply
                    // 本地化：词表外命令诚实拒绝（缺省文案）
                    : LWNTextHelper.ResolveText("LWN_plan_custom_reject", "I cannot do this. Try something else.");
                ShowCompanionLine(rejectText);
                // 本地化：重试按钮
                string retryBtn = LWNTextHelper.ResolveText("LWN_plan_btn_retry", "Try again");
                // 本地化：放弃按钮
                string giveUpBtn = LWNTextHelper.ResolveText("LWN_plan_btn_giveup", "Never mind");
                ShowConfirm(rejectText, retryBtn, giveUpBtn, () => PromptForCommand(), () => CloseDialog());
                return;
            }

            // 批准轮：系统确认弹窗（同意/算了，无第三选项）——不再用 DialogChoice 自绘选项列表。
            // 玩家输入命令的 inquiry 发送后系统自动关闭，这里新开一个纯按钮 inquiry 承载确认。
            _processing = false;
            // 本地化：计划摘要缺省文案
            string summary = !string.IsNullOrEmpty(response.Plan?.Summary) ? response.Plan.Summary
                // 本地化：计划摘要缺省文案（缺省）
                : LWNTextHelper.ResolveText("LWN_plan_default_summary", "I have a plan. Shall I go?");
            string reply = !string.IsNullOrEmpty(response.Reply) ? response.Reply + "\n" : "";
            // 本地化：批准弹窗标题（含随从名）
            string approveTitle = LWNTextHelper.ResolveCompound("LWN_plan_approve_title", ("NAME", _companion?.Name?.ToString() ?? ""));
            // 本地化：同意按钮
            string approveOkBtn = LWNTextHelper.ResolveText("LWN_plan_btn_approve", "Go ahead");
            // 本地化：放弃按钮
            string giveUpOkBtn = LWNTextHelper.ResolveText("LWN_plan_btn_giveup", "Never mind");
            InformationManager.ShowInquiry(new InquiryData(
                approveTitle, reply + summary, true, true, approveOkBtn, giveUpOkBtn,
                () => ApplyPlan(response), () => CloseDialog()));
        }

        /// <summary>批准：应用反应计划 + 下发执行（§10 执行通道）。</summary>
        private static void ApplyPlan(PlanResponse response)
        {
            if (!_isActive || _companion == null) return;
            var companion = _companion;
            try
            {
                // 反应计划应用（ReactiveAgent 覆盖默认模板）
                if (response.Reactions != null)
                {
                    foreach (var rp in response.Reactions)
                    {
                        if (rp == null || string.IsNullOrEmpty(rp.Role)) continue;
                        var info = SceneSnapshot.Build(Mission.Current).FindAgent(rp.Role);
                        if (info?.Agent != null)
                            ReactiveAgent.ApplyPlan(info.Agent, rp);
                    }
                }

                // 意图 target 解析（角色表注入）
                Agent target = null;
                var intent = response.Intent;
                if (intent != null)
                {
                    string t = PlanRefUtil.Normalize(intent.Target, out string _);
                    if (!string.IsNullOrEmpty(t) && t != "player" && t != "self")
                    {
                        var info = SceneSnapshot.Build(Mission.Current).FindAgent(t);
                        target = info?.Agent;
                    }
                }

                string planJson = JsonConvert.SerializeObject(response.Plan,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                string intentType = intent?.IntentType;
                string originalCommand = _command;
                CloseDialog();
                AgentAIController.Instance?.SendEventToAgent(companion, "order_execute_plan", planJson, intentType, target, originalCommand);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PlanCommandFlow] 计划下发失败: {ex.Message}");
                CloseDialog();
            }
        }

        // ═══════════════════════════════════════════════════════════
        // prompt 辅助
        // ═══════════════════════════════════════════════════════════

        private static string BuildPersona(Agent companion)
        {
            try
            {
                // 主人 = 玩家（命令永远来自玩家）；companion 是随从自己——之前错把 companion.Name（随从名）当主人名
                string masterName = Agent.Main?.Name ?? "";
                string heroName = (companion?.Character as CharacterObject)?.HeroObject?.Name?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(heroName))
                {
                    if (string.IsNullOrWhiteSpace(masterName))
                        return $"你是 {heroName}。说话简短、恭敬、务实，像游戏里的随从。";
                    return $"你是 {heroName}，{masterName} 的随从。说话简短、恭敬、务实，像游戏里的随从。";
                }
                // 模板 NPC 无名 → 至少让模型知道主人是谁
                if (!string.IsNullOrWhiteSpace(masterName))
                    return $"你是 {masterName} 的随从。说话简短、务实，像游戏里的随从。";
            }
            catch { }
            return "你是一名随从。说话简短、务实，像游戏里的随从。";
        }

        /// <summary>意图词表（Replan 复用）。</summary>
        internal static string IntentTableForPrompt()
        {
            return BuildIntentTable();
        }

        /// <summary>语法词表（Replan 复用）。</summary>
        internal static string GrammarForPrompt()
        {
            return BuildGrammar();
        }

        /// <summary>意图注册表（单一事实源）：注册新意图 = 枚举加一行（GoalTemplates.cs）+ 此处加一行话术，
        /// prompt 意图词表自动读到；few-shot 判定基准需在 BuildIntentTable 手写（分类示范知识）。</summary>
        private static readonly Dictionary<CommandIntentType, string> IntentPhrases =
            new Dictionary<CommandIntentType, string>
            {
                { CommandIntentType.Follow, "跟我走" },
                { CommandIntentType.Wait, "在这等我" },
                { CommandIntentType.Stop, "住手" },
                { CommandIntentType.Attack, "干掉他" },
                { CommandIntentType.Guard, "护住他/条件参战" },
                { CommandIntentType.Bring, "请人到面前" },
                { CommandIntentType.Distract, "引开某人" },
                { CommandIntentType.Lookout, "望风" },
                { CommandIntentType.Deliver, "传话/送物" },
                { CommandIntentType.Engage, "缠住/拖住" },
                { CommandIntentType.DriveAway, "赶走" },
                { CommandIntentType.Steal, "偷物/扒窃" },
                { CommandIntentType.Formation, "站位" },
                { CommandIntentType.Spar, "切磋" },
                { CommandIntentType.Fetch, "取物" },
                { CommandIntentType.Purchase, "购买" },
                { CommandIntentType.Knockout, "打晕" },
                { CommandIntentType.Guide, "带路" },
                { CommandIntentType.Scout, "侦察" },
                { CommandIntentType.TalkTo, "交涉" },
                { CommandIntentType.Find, "找人" },
                { CommandIntentType.Shadow, "跟踪" },
                { CommandIntentType.Collect, "讨债" },
                { CommandIntentType.Duel, "比武" },
                { CommandIntentType.Annihilate, "清剿（把某个区域的所有人杀掉/打晕，批量战斗）" },
                { CommandIntentType.Commotion, "闹出动静" },
                { CommandIntentType.Interact, "实体互动（把门打开/把灯吹灭，能力待验证）" },
                { CommandIntentType.Discreet, "低调/别惹事（行为参数）" },
            };

        private static string BuildIntentTable()
        {
            // 意图词表行动态拼接（单一事实源 = IntentPhrases + CommandIntentType 枚举）：
            // 注册新意图 → 两处加行，prompt 自动读到。
            var table = string.Join(" / ",
                Enum.GetValues(typeof(CommandIntentType)).Cast<CommandIntentType>()
                    .Where(t => t != CommandIntentType.None)
                    .Select(t => t == CommandIntentType.Custom
                        ? "CUSTOM 词表外（现实做不到的动作：翻译/施法/修装备等 → 诚实拒绝）"
                        : IntentPhrases.TryGetValue(t, out var phrase)
                            ? $"{IntentCode(t)} {phrase}"
                            : IntentCode(t)));
            // few-shot 判定基准（分类示范知识）——静态文本在 XML（LWN_plan_intent_fewshot，py/C# 同源）：
            // 注册新意图时除 IntentPhrases 加行外，还须在 XML 的 LWN_plan_intent_fewshot 补判定基准行。
            // 词表输出用 IntentCode（驼峰拆下划线大写，与 few-shot 的 TALK_TO/DRIVE_AWAY 写法一致）；
            // 解析侧 ParseIntentType 兼容两者（见 PlanExecutor）。
            return table + "\n" + LWNTextHelper.ResolvePrompt("LWN_plan_intent_fewshot");
        }

        /// <summary>意图代码的规范写法：驼峰拆成下划线大写（TalkTo→TALK_TO、DriveAway→DRIVE_AWAY），
        /// 与 few-shot 判定基准（XML）写法一致，避免模型抄到两种写法导致解析降级 CUSTOM。</summary>
        private static string IntentCode(CommandIntentType t)
        {
            var name = t.ToString();
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i])) sb.Append('_');
                sb.Append(char.ToUpperInvariant(name[i]));
            }
            return sb.ToString();
        }

        private static string BuildGrammar()
        {
            // 词表动态拼接（单一事实源 = PlanVocab / ReactiveAgent 的 *InPromptOrder 数组）：
            // 注册新动作/谓词/查询/触发词/反应动作 → 词表数组加一行，prompt 自动读到。
            // 顺序 = 数组声明顺序（保持手写序，防 prompt 漂移——82% 回归基线是此顺序跑出来的）。
            return string.Join("\n",
                "动作（action）：" + string.Join(" / ", PlanVocab.ActionsInPromptOrder)
                    + "（攻击动作必须写 order_attack，禁止缩写 attack）",
                "谓词（type）：" + string.Join(" / ", PlanVocab.PredicatesInPromptOrder),
                "谓词修饰：sustained_s（连续成立 N 秒）、was（曾成立过）",
                "实体：self（执行者）/ player / 场景角色 / 场景物件 / 区域",
                "字段纪律：say_to 台词写 text（不是 content）；wait 退出条件写 until（必须是对象 {\"type\":...}，禁止字符串）；ask 只允许 \"follow\"",
                "end_plan 的 result 只能是 \"success\" 或 \"fail\"；report 可选（当面报告文本）",
                "【reactions 封闭词表（事件/动作严禁自创）】",
                "事件：" + string.Join(" / ", ReactiveAgent.TriggerEventsInPromptOrder)
                    + "（注意是 approach_by，不是 approached_by）",
                "动作：" + string.Join(" / ", ReactiveAgent.ReactionActionsInPromptOrder)
                    + "（flee = 看到同伴被杀等恐慌情境下跑离现场）");
            // 质量要求/纪律不在此重复——BuildPlanPrompt 已有完整版（2026-08-08 消除重复，防语义过载）
        }
    }
}
