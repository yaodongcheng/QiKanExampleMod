using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════
    // PlanCommandFlow.cs — 密谋命令流程入口（IM 版，2026-08-10 升级）
    //
    // 流程：Plot 交互（G 长按）→ 随从冒泡开场（仪式感保留）→ 直接呼出 IM 面板定位该随从
    //     → 私聊会话并切「密令」模式 → 玩家在 IM 输入框下达命令 → ImCommandFlow 现有管线
    //     （LLM 计划 / PlanCard 三态 / 执行 / 回报）全复用。
    //
    // 🔴 2026-08-10 IM 化改造（plans/im-command-action-upgrade.md Q1）：退役 vanilla 三通道
    //    （ShowTextInquiry 命令输入框 / 澄清轮输入框 / 批准 ShowInquiry 弹窗）。本类只剩：
    //    Plot 入口门控 + 开场冒泡 + IM 定位 + _isActive 互斥语义（Talk 行互斥移除）+ 停止键。
    //    LLM 计划生成与批准流程全部由 ImCommandFlow 承担。
    //
    // 🔴 禁止 DialogChoice/StoryDialogVM——废弃对话系统；随从台词一律 AgentSay 头顶冒泡。
    //
    // 铁律 1：IsLLMConfigured 总闸——不可用 → Plot 行不出现/点开提示"随从想不出主意"。
    // ═══════════════════════════════════════════════════════════════

    public static class PlanCommandFlow
    {
        private static Agent _companion;
        private static bool _isActive;
        private static string _activeConvId;   // 密谋定位的会话 Id（互斥检查按会话放行）

        public static bool IsActiveFor(Agent agent)
        {
            return _isActive && agent != null && agent == _companion;
        }

        /// <summary>是否正在密谋（Talk/Pickpocket 行互斥移除用）。</summary>
        public static bool IsActive => _isActive;

        /// <summary>是否正在密谋且不是本会话（IM 命令入口放行自己的会话，拦截其他会话的并发密谋）。</summary>
        public static bool IsActiveForOtherConv(ImConversation conv)
        {
            return _isActive && (conv == null || conv.Id != _activeConvId);
        }

        /// <summary>结束密谋输入阶段（IM 关闭 / 切回闲聊 / 卡片批准或拒绝后调用，幂等）。</summary>
        public static void End()
        {
            _isActive = false;
            _companion = null;
            _activeConvId = null;
        }

        // ═══════════════════════════════════════════════════════════
        // 入口（InteractionMissionView ExecuteInteraction 分发）
        // ═══════════════════════════════════════════════════════════

        /// <summary>Plot 交互入口（对随从下达自然语言命令，§8.1；IM 化后 = 呼出 IM 定位私聊切密令模式）。</summary>
        public static void Start(Agent companion)
        {
            if (companion == null || _isActive) return;
            // 密令玩法总闸（MCM 开关，默认关闭；显示层已门控，此处兜底触发路径）
            if (!Settings.Instance.PlotEnabled)
            {
                DebugLogger.Log($"[PlanCommandFlow] 密令玩法未开启（PlotEnabled=false），拒绝启动");
                return;
            }
            // 铁律 1：LLM 总闸（配置齐全——显示层已门控，此处兜底触发路径）
            if (!Settings.Instance.IsLLMConfigured)
            {
                // 本地化：LLM 未配置提示（随从想不出主意）
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText("LWN_plan_no_llm", "The companion cannot think of a plan right now."), Colors.Red));
                return;
            }
            var brain = AgentAIController.GetBrainForAgent(companion);
            if (brain == null) return;
            // 战斗/模态门控：IM 面板打开条件（IsInteractionDisabled 等；Plot 行显示层已门控，此处兜底）
            if (!ImChatView.CanOpen())
            {
                // 本地化：当前状态无法呼出传讯面板
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText("LWN_im_cmd_unavailable_here", "You cannot send messages right now."), Colors.Red));
                return;
            }

            _companion = companion;
            _isActive = true;

            // 本地化：密谋开场白（随从头顶冒泡示意，仪式感保留）
            AgentHudMissionView.AgentSay(companion, LWNTextHelper.ResolveText("LWN_plan_opening", "Quiet... tell me what you need."));

            // 私聊会话定位：随从必须有 Hero（direct 私聊按 Hero StringId 索引；模板 NPC 无法建私聊 → 降级）
            var hero = (companion.Character as CharacterObject)?.HeroObject;
            if (hero == null || string.IsNullOrEmpty(hero.StringId))
            {
                DebugLogger.Log($"[PlanCommandFlow] 随从无 Hero，无法建立 IM 私聊，降级退出");
                End();
                // 本地化：模板随从无法传讯联系
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText("LWN_im_cmd_no_direct", "This companion cannot be reached by message."), Colors.Red));
                return;
            }
            ImChatStore.TouchDirectChat(hero.StringId, ImChatManager.NowUnixMs());
            var conv = ImChatManager.GetDirectConversation(hero.StringId);
            if (conv == null)
            {
                DebugLogger.Log($"[PlanCommandFlow] 私聊会话构建失败，降级退出");
                End();
                return;
            }
            _activeConvId = conv.Id;
            // 呼出 IM 定位该随从私聊 + 切「密令」模式（vanilla 输入框/批准弹窗已退役）
            ImChatView.Open(conv, ImMode.Command);
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
        // prompt 辅助（ImCommandFlow / PlanReplan 复用）
        // ═══════════════════════════════════════════════════════════

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
                "字段纪律：say_to 台词写 text（不是 content）；wait 退出条件写 until（必须是对象 {\"type\":...}，禁止字符串）；ask 只允许 \"follow\"；**对话任务（TALK_TO/DELIVER 闲聊）用 say_to 带 topic + outline（2-5 段走向数组）表达多轮对话，不写多句预写台词**",
                "end_plan 的 result 只能是 \"success\" 或 \"fail\"；report 可选（当面报告文本）",
                "【reactions 封闭词表（事件/动作严禁自创）】",
                "事件：" + string.Join(" / ", ReactiveAgent.TriggerEventsInPromptOrder)
                    + "（注意是 approach_by，不是 approached_by）",
                "动作：" + string.Join(" / ", ReactiveAgent.ReactionActionsInPromptOrder)
                    + "（flee = 看到同伴被杀等恐慌情境下跑离现场；respond = 被搭话时开口回应，台词实时生成）");
            // 质量要求/纪律不在此重复——BuildPlanPrompt 已有完整版（2026-08-08 消除重复，防语义过载）
        }
    }
}
