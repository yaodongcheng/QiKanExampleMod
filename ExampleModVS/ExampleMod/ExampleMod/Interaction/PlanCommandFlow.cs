using System;
using System.Collections.Generic;
using System.Linq;
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
    // PlanCommandFlow.cs — 密谋对话壳（§8 计划阶段 UX）
    //
    // 流程：Plot 交互（G 长按）→ 对话壳开场 → 自由文本输入命令
    //     → 快照构建 + LLM 调用（意图分类 + 计划 + 可含 questions）
    //     → 澄清轮（≤2 轮，追加上下文再调）→ 批准轮（同意/再想想/算了）
    //     → order_execute_plan 下发（ReactiveAgent 反应计划一并应用）→ 确定性执行
    //
    // 复用：StoryDialogVM（对话壳）+ ShowTextInquiry（自由输入，既有先例）
    //       + LLMService（支持 temperature/max_tokens 4000）。
    // LLM 结果回主线程消费（Tick 轮询），避免后台线程动 UI。
    //
    // 铁律 1：IsLLMReady 总闸——不可用 → Plot 行不出现/点开提示"随从想不出主意"。
    // ═══════════════════════════════════════════════════════════════

    public static class PlanCommandFlow
    {
        private static Agent _companion;
        private static GauntletLayer _layer;
        private static StoryDialogVM _vm;
        private static bool _isActive;
        private static bool _processing;
        private static string _command;
        private static readonly List<string> _history = new List<string>();
        private static int _clarifyRound;
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
            // 铁律 1：LLM 总闸
            if (!Settings.Instance.IsLLMReady)
            {
                // 本地化：LLM 未配置提示（随从想不出主意）
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText("LWN_plan_no_llm", "The companion cannot think of a plan right now."), Colors.Red));
                return;
            }
            var brain = AgentAIController.GetBrainForAgent(companion);
            if (brain == null) return;

            _companion = companion;
            _isActive = true;
            _processing = false;
            _clarifyRound = 0;
            _command = null;
            _history.Clear();

            OpenDialog();
            // 本地化：密谋开场白（随从小声示意）
            ShowCompanionLine(LWNTextHelper.ResolveText("LWN_plan_opening", "Quiet... tell me what you need."));
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
        // 对话壳
        // ═══════════════════════════════════════════════════════════

        private static void OpenDialog()
        {
            var screen = ScreenManager.TopScreen as MissionScreen;
            if (screen == null) return;
            if (_layer == null)
            {
                _vm = new StoryDialogVM();
                _layer = V.NewLayer(1000);
                _layer.LoadMovie("DialogChoice", _vm);
                screen.AddLayer(_layer);
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
            }
        }

        private static void ShowCompanionLine(string text)
        {
            if (_vm == null) return;
            try
            {
                _vm.Show(_companion?.Name?.ToString() ?? "", text);
            }
            catch { }
        }

        private static void ShowOptions(params StoryOptionVM[] options)
        {
            if (_vm == null) return;
            try
            {
                _vm.ShowOptions(options);
            }
            catch { }
        }

        private static void CloseDialog()
        {
            var screen = ScreenManager.TopScreen as MissionScreen;
            if (_layer != null && screen != null)
            {
                try { screen.RemoveLayer(_layer); } catch { }
                _layer = null;
            }
            _vm = null;
            _isActive = false;
            _companion = null;
            _pendingResult = null;
            _resultReady = false;
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
                    _command = text;
                    _processing = true;
                    _ = CallPlanAsync(text);
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
                // 本地化：重试选项
                var retry = new StoryOptionVM(LWNTextHelper.ResolveText("LWN_plan_btn_retry", "Try again"), () => PromptForCommand());
                // 本地化：放弃选项
                var giveUp = new StoryOptionVM(LWNTextHelper.ResolveText("LWN_plan_btn_giveup", "Never mind"), () => CloseDialog());
                ShowOptions(retry, giveUp);
                return;
            }

            // 澄清轮（意图歧义优先澄清，最多 2 轮）
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
                ShowCompanionLine(qText);
                var opts = new List<StoryOptionVM>();
                if (q != null && q.Options != null)
                {
                    foreach (var opt in q.Options)
                    {
                        string optCopy = opt;
                        opts.Add(new StoryOptionVM(opt, () =>
                        {
                            _history.Add($"随从（澄清）：{qText}\n玩家：{optCopy}");
                            _command = $"{_command}（{optCopy}）";
                            _processing = true;
                            _ = CallPlanAsync(_command);
                        }));
                    }
                }
                // 本地化：放弃选项（澄清轮）
                opts.Add(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_plan_btn_giveup", "Never mind"), () => CloseDialog()));
                ShowOptions(opts.ToArray());
                return;
            }
            if (needsClarify && _clarifyRound >= 2)
            {
                // 澄清超轮 → 诚实放弃
                _processing = false;
                // 本地化：澄清超轮放弃
                ShowCompanionLine(LWNTextHelper.ResolveText("LWN_plan_clarify_exhausted", "I still do not understand. Perhaps another time."));
                // 本地化：放弃选项（超轮）
                ShowOptions(new StoryOptionVM(LWNTextHelper.ResolveText("LWN_plan_btn_giveup", "Never mind"), () => CloseDialog()));
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
                // 本地化：重试选项（词表外）
                var retry = new StoryOptionVM(LWNTextHelper.ResolveText("LWN_plan_btn_retry", "Try again"), () => PromptForCommand());
                // 本地化：放弃选项（词表外）
                var giveUp = new StoryOptionVM(LWNTextHelper.ResolveText("LWN_plan_btn_giveup", "Never mind"), () => CloseDialog());
                ShowOptions(retry, giveUp);
                return;
            }

            // 批准轮
            _processing = false;
            // 本地化：计划摘要缺省文案
            string summary = !string.IsNullOrEmpty(response.Plan?.Summary) ? response.Plan.Summary
                // 本地化：计划摘要缺省文案（缺省）
                : LWNTextHelper.ResolveText("LWN_plan_default_summary", "I have a plan. Shall I go?");
            string reply = !string.IsNullOrEmpty(response.Reply) ? response.Reply + "\n" : "";
            ShowCompanionLine(reply + summary);            // 本地化：批准执行选项
            var approve = new StoryOptionVM(LWNTextHelper.ResolveText("LWN_plan_btn_approve", "Go ahead"), () => ApplyPlan(response));
            // 本地化：再想想选项（回到输入框重说/追加修改意见）
            var rethink = new StoryOptionVM(LWNTextHelper.ResolveText("LWN_plan_btn_rethink", "Think again"), () =>
            {
                _history.Add($"玩家：{_command}");
                PromptForCommand();
            });
            // 本地化：放弃选项（批准轮）
            var giveUp2 = new StoryOptionVM(LWNTextHelper.ResolveText("LWN_plan_btn_giveup", "Never mind"), () => CloseDialog());
            ShowOptions(approve, rethink, giveUp2);
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
                var hero = (companion?.Character as CharacterObject)?.HeroObject;
                if (hero != null && !string.IsNullOrWhiteSpace(hero.Name?.ToString()))
                    return $"你是 {hero.Name}，{companion.Name} 的随从。说话简短、恭敬、务实，像游戏里的随从。";
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
                            ? $"{t.ToString().ToUpperInvariant()} {phrase}"
                            : t.ToString()));
            // few-shot 判定基准（分类示范知识，手写维护；注册新意图时在此补判定基准）
            return table
                + "\n【意图判定基准（few-shot）】"
                + "\n\"干掉他/杀了他/解决他/做了他\" → ATTACK（要动手见血）"
                + "\n\"引开/骗走/调虎离山/把某人支开\" → DISTRACT（不交手，只转移注意力）"
                + "\n\"缠住/拖住/别让他走/稳住他\" → ENGAGE（对话/周旋，不让对方脱身）"
                + "\n\"偷/摸/拿那东西\" → STEAL；\"请/叫某人过来\" → BRING；\"望风/盯梢/来人了叫我\" → LOOKOUT"
                + "\n\"带我去/领我去\" → GUIDE；\"赶走/轰走/撵走\" → DRIVE_AWAY；\"传话/告诉他\" → DELIVER"
                + "\n\"去和X切磋/比试，试他深浅\" → DUEL（随从与第三方比武，非致死，回报评估）；\"和我切磋/和我比划\" → SPAR（玩家是互动对象）"
                + "\n\"订房/安排事务/订酒菜\" → TALK_TO（交涉安排）；\"买/购买某物\" → PURCHASE（随从花钱买货带回来）；\"讨债/要钱/收账\" → COLLECT（把钱要回来）"
                + "\n【复合命令判定（重要：按最终目的分类，不是第一个动作）】"
                + "\n\"引开/骗走 X 打晕/干掉/放倒\" → KNOCKOUT/ATTACK（引开只是手段，最终目的是击晕/击杀）"
                + "\n\"我引开/缠住/望风，你去偷/翻/动手\" → STEAL 等（\"我…你…\" = 角色分工，随从执行的是后半句的主动作）"
                + "\n\"X 敢还手/动手/攻击，你就上/参战\" → GUARD（条件参战：平时压阵，对方动手才打）"
                + "\n\"先…然后…/顺便…/同时…\" → 按最终目的分类"
                + "\n\"在这等我，去那边看看/打听…\" → SCOUT（后半句的任务才是命令主体）"
                + "\n【指代纪律】命令里的\"他/她/它/那东西/那个人\"若场景存在多个候选或指代不明 → 必须 questions 澄清（列候选位置让玩家选），禁止自行挑一个；\"跟他走\"无明确指代也须澄清（除非场景只有唯一可跟随者）。";
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
