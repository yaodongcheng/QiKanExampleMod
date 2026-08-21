using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs
{
    public static class PromptBuilder
    {
        private static Settings S => Settings.Instance;

        public static string BuildOpeningPrompt(SingNpcMemorySystem memory,Agent targetAgent)
        {
            StringBuilder sb = new StringBuilder();
            string npcName = memory._profile.Name;
            string playerName = Hero.MainHero != null ? Hero.MainHero.Name.ToString() : "玩家";
            var initiative = memory.CurrentInitiative;
            // 语言规则（prompt 顶部强指令，双桶 XML LWN_prompt_lang_rule；2026-08-20 双语化迁移）
            sb.AppendLine(LWNTextHelper.GetLanguageRule());
            sb.AppendLine();
            // 1. 场景设定
            // 本地化：LWN_prompt_opening_role（角色设定，双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_opening_role", ("NPC_NAME", npcName)));
            // 本地化：LWN_prompt_opening_accost（拦路场景，双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_opening_accost", ("PLAYER_NAME", playerName)));
            sb.AppendLine();

            // 本地化：LWN_prompt_section_conflict（当前冲突情境段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_conflict"));
            // 本地化：LWN_prompt_opening_conflict_body（事件起因，双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_opening_conflict_body", ("CONFLICT", initiative.ContextDescription)));
            // 本地化：LWN_prompt_opening_goal（目标，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_opening_goal"));

            // 本地化：LWN_prompt_section_self_info（自我信息段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_self_info"));
            sb.AppendLine(memory.GetPersonaPrompt());

            // [新增] B. 玩家(对话对象) 信息
            // 本地化：LWN_prompt_section_player_info（玩家信息段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_player_info"));
            string playerContext = AllNpcMemoryManager.GetPlayerDescription(memory._profile);
            sb.AppendLine(playerContext);
            //拼入Npc人设、玩家人设、对话历史、记忆、事件、动作空间等
            sb.AppendLine(GetPrompt_History_Memory_Events(memory));

            sb.AppendLine();
            // 本地化：LWN_prompt_section_options（选项卡段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_options"));
            // 本地化：LWN_prompt_option_intro（选项说明，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_option_intro"));
            // 本地化：LWN_prompt_option_tactics（tactic 范围，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_option_tactics"));
            // 本地化：LWN_prompt_option_emotion（情绪匹配，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_option_emotion"));
            // 本地化：LWN_prompt_option_impact（后果预测，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_option_impact"));

            // 2. 冲突上下文
            // 本地化：LWN_prompt_opening_json（JSON 输出模板，双桶；含 JSON 走 ResolvePrompt 绕 TextObject 截断）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_opening_json"));

            // 本地化：LWN_prompt_section_notes（交谈注意事项段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_notes"));
            // 本地化：LWN_prompt_note_fact（绝对事实防御，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_note_fact"));
            // 本地化：LWN_prompt_note_repeat（拒绝复读，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_note_repeat"));
            // 本地化：LWN_prompt_note_rank（身份位阶，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_note_rank"));
            // 本地化：LWN_prompt_note_style（风格行，双桶；LANG/STYLE/ADDR 运行时解析）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_note_style",
                ("LANG", LWNTextHelper.GetReplyLanguageInstruction()),
                ("STYLE", S.SpeechStyle),
                ("ADDR", S.FemaleSelfAddress)));
            // 本地化：LWN_prompt_note_bracket（括号引导，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_note_bracket"));

            // 本地化：LWN_prompt_section_req_other（其他回复要求段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_req_other"));
            // 本地化：LWN_prompt_req_json（JSON 纯净输出，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_req_json"));
            // 本地化：LWN_prompt_req_options（选项数量，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_req_options"));
            // 本地化：LWN_prompt_req_len_15（长度限制，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_req_len_15"));
            // 本地化：LWN_prompt_req_emotion（情绪枚举，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_req_emotion"));
            // 本地化：LWN_prompt_req_action（动作空间，双桶；ACTION_SPACE 运行时解析）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_req_action",
                ("ACTION_SPACE", ActionHandler.GetActionSpacePrompt(memory._profile.BaseHero, Hero.MainHero, targetAgent))));

            // 本地化：LWN_prompt_lang_rule_final（最终输出语言强制令，双桶；2026-08-20 输出纪律处强指定语言）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_lang_rule_final"));
            return sb.ToString();
        }

        public static string BuildSkillCheckResponsePrompt(SingNpcMemorySystem memory, SkillCheckOption option, bool IsSkillCheckSuccess, Agent targetAgent)
        {

            StringBuilder sb = new StringBuilder();
            string npcName = memory._profile.Name;
            string playerName = Hero.MainHero != null ? Hero.MainHero.Name.ToString() : "玩家";
            // 语言规则（prompt 顶部强指令，双桶 XML LWN_prompt_lang_rule；2026-08-20 双语化迁移）
            sb.AppendLine(LWNTextHelper.GetLanguageRule());
            sb.AppendLine();
            // 2. 构建 Prompt：只索取剧情反馈
            string conflictInfo = memory.CurrentInitiative.ContextDescription;
            // 本地化：LWN_prompt_skillcheck_bg（当前背景段，双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_skillcheck_bg", ("CONFLICT", conflictInfo)));

            if(IsSkillCheckSuccess)
            {
                // 本地化：LWN_prompt_skillcheck_success（检定成功段，双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_skillcheck_success",
                    ("PLAYER_NAME", playerName), ("OPTION_TEXT", option.Text), ("NPC_NAME", npcName), ("CONFLICT", conflictInfo)));
            }
            else
            {
                // 本地化：LWN_prompt_skillcheck_fail（检定失败段，双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_skillcheck_fail",
                    ("PLAYER_NAME", playerName), ("OPTION_TEXT", option.Text), ("NPC_NAME", npcName), ("CONFLICT", conflictInfo)));
            }
            // 本地化：LWN_prompt_skillcheck_json（JSON 输出格式，双桶；含 JSON 走 ResolvePrompt）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_skillcheck_json"));

            // 本地化：LWN_prompt_section_notes（交谈注意事项段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_notes"));
            // 本地化：LWN_prompt_note_rank（身份位阶，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_note_rank"));
            // 本地化：LWN_prompt_note_style（风格行，双桶；LANG/STYLE/ADDR 运行时解析）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_note_style",
                ("LANG", LWNTextHelper.GetReplyLanguageInstruction()),
                ("STYLE", S.SpeechStyle),
                ("ADDR", S.FemaleSelfAddress)));
            // 本地化：LWN_prompt_skillcheck_note3（三字段输出说明，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_skillcheck_note3"));

            // 本地化：LWN_prompt_section_req_other（其他回复要求段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_req_other"));
            // 本地化：LWN_prompt_req_json（JSON 纯净输出，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_req_json"));
            // 本地化：LWN_prompt_skillcheck_req_len（长度限制，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_skillcheck_req_len"));
            // 本地化：LWN_prompt_req_emotion（情绪枚举，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_req_emotion"));

            // 本地化：LWN_prompt_lang_rule_final（最终输出语言强制令，双桶；2026-08-20 输出纪律处强指定语言）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_lang_rule_final"));
            return sb.ToString();

        }
        public static string BuildPromptForNegoAndChat(SingNpcMemorySystem memory, string playerInput, PlayerResources playerRes, NegotiationCard selectedOption,Agent  targetAgent)
        {
            if (memory == null) return "";

            // 1. 判断当前模式
            bool isInNegotiation = (memory.CurrentNegotiationState != null);
            // --- 模式分流 ---
            if (!isInNegotiation)
            {
                return BuildCasualChatPrompt(memory,playerInput, targetAgent);
            }
            else
            {
                return BuildNegotiationPrompt_New(memory, playerRes, playerInput, selectedOption, targetAgent);
            }
        }
        public static string GetPrompt_History_Memory_Events(SingNpcMemorySystem memory)
        {
            var sb = new StringBuilder();
            string playerName = Hero.MainHero != null ? Hero.MainHero.Name.ToString() : "玩家";
            string npcName = memory._profile.Name;

           
            // B. 远期记忆
            if (memory.PermanentMemory.Length > 0)
            {
                // 本地化：LWN_prompt_section_perm_memory（远期记忆段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_perm_memory"));
                sb.AppendLine($"{memory.PermanentMemory.ToString()}");
            }

            // C. 动态记忆
            if (memory.DynamicMemories.Count > 0)
            {
                // 本地化：LWN_plan_respond_section_recall（近期回忆段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_respond_section_recall"));
                foreach (var mem in memory.DynamicMemories)
                {
                    if (!string.IsNullOrEmpty(mem.Content))
                        sb.AppendLine($"- {mem}");
                }
            }

            // D. 委托记录（结构化历史，比传闻可靠）
            if (memory.QuestHistory.Count > 0)
            {
                // 本地化：LWN_prompt_section_quest_history（委托记录段标题，双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_quest_history"));
                for (int i = memory.QuestHistory.Count - 1; i >= 0; i--)
                {
                    sb.AppendLine($"- {memory.QuestHistory[i].GetDisplaySummary()}");
                }
                sb.AppendLine();
            }

            // E. 重大新闻
            if (!string.IsNullOrEmpty(memory.GlobalNews))
            {
                // 本地化：LWN_prompt_section_news（重大新闻段标题，双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_news"));
                sb.AppendLine($"{memory.GlobalNews}");
            }

            // E. 近期对话历史
            if (memory.RecentHistory.Count > 0)
            {
                // 本地化：LWN_prompt_section_chat_history（近期对话段标题，双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_chat_history"));


                foreach (var msg in memory.RecentHistory)
                {
                    sb.AppendLine($"-{msg.Content}");
                }
            }

            // 相关传闻
            if (memory.KnownEvents.Count > 0)
            {
                // 本地化：LWN_prompt_section_rumors（相关传闻段标题，双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_rumors"));
                //先倒序排序，然后取第一个肇事者或者受害者是玩家的
                var events = memory.KnownEvents.OrderByDescending(e => e.PerceivedSeverity).ToList();


                foreach (var evt in events)
                {
                    float severity = evt.PerceivedSeverity;
                    string eventId = evt.EventId;
                    SocialEvent sevt = NewsSpreadSystem.Instance.GetEventById(eventId);
                    if (sevt != null)
                    {
                        if (sevt.VictimId == Hero.MainHero.StringId || sevt.InitiatorId == Hero.MainHero.StringId)
                        {
                            // 本地化：LWN_prompt_rumor_line（传闻行，双桶）
                            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_rumor_line", ("DESC", sevt.Description), ("NPC", npcName), ("SEV", severity.ToString())));
                            sb.AppendLine(GetRelationPrompt(memory, eventId));
                            break;
                        }
                    }
                }
            }


            return sb.ToString();
        }

        /// <summary>respond 实时回应的裁剪版记忆上下文（BC-006 v2 / plan D3）：
        /// 永久记忆（截断 200 字）+ 动态记忆最新 2 条 + 与 otherId 相关的近期对话（最多 6 句，不足补最近行）。
        /// 不复用 GetPrompt_History_Memory_Events（玩家对话全量版，对 2s 预算太重）。
        /// otherId = 当前搭话方标识（Hero StringId / TEMP_AGENT 键），null = 不过滤。</summary>
        public static string GetPrompt_RespondContext(SingNpcMemorySystem memory, string otherId)
        {
            if (memory == null) return "";
            var sb = new StringBuilder();
            // 🔴 2026-08-16（方案 N1）：大事记段——写入时 C# 白名单分级锚定的重要事件（建国/获封/
            // 大婚/添丁/被俘/获释/大捷），平行于 LLM 淘汰晋升；L1 全量注入（2~6 行展示，条目 = desc 原文）
            if (memory.ImportantEvents != null && memory.ImportantEvents.Count > 0)
            {
                // 本地化：plan_respond_section_important（玩家可见文本）
                sb.AppendLine(LWNTextHelper.ResolveText("LWN_plan_respond_section_important"));
                int shown = 0;
                for (int i = memory.ImportantEvents.Count - 1; i >= 0 && shown < 6; i--, shown++)
                {
                    if (!string.IsNullOrEmpty(memory.ImportantEvents[i]))
                        sb.AppendLine("- " + memory.ImportantEvents[i]);
                }
                sb.AppendLine();
            }

            // 1. 永久记忆（截断，防 token 膨胀拖慢 2s 预算）——旧事段天然陈旧，不加时间戳（I5）
            if (memory.PermanentMemory.Length > 0)
            {
                string perm = memory.PermanentMemory.ToString();
                if (perm.Length > 200) perm = perm.Substring(0, 200) + "…";
                // LWN_plan_respond_section_perm：旧事段标题
                sb.AppendLine(LWNTextHelper.ResolveText("LWN_plan_respond_section_perm"));
                sb.AppendLine(perm);
            }

            // 1.5 近期经历（旁白，2026-08-11）：AgentBrain 事件决策点写入的第一人称经历
            // （"我遭到X的攻击"/"我看见X偷窃"/"我奉命攻击X"），最新 3 条，新→旧。
            // 比【近期回忆】更即时——它是原始事件，回忆是总结产物。
            // 🔴 2026-08-16（I5）：每行带 [相对词] 时间戳前缀（游戏内日 → 相对词；旧存档 CampaignDay==0
            // 不标——契约兜底，宁模糊不编数）
            var narration = memory.SnapshotNarrationLog();
            if (narration.Count > 0)
            {
                // LWN_plan_respond_section_experience：近期经历段标题
                sb.AppendLine(LWNTextHelper.ResolveText("LWN_plan_respond_section_experience"));
                for (int i = narration.Count - 1; i >= 0 && i >= narration.Count - 3; i--)
                {
                    if (!string.IsNullOrEmpty(narration[i].Content))
                        sb.AppendLine("- " + RelativeDayPrefix(narration[i].CampaignDay) + narration[i].Content);
                }
            }

            // 2. 动态记忆最新 2 条（LinkedList 正序 = 旧→新）
            if (memory.DynamicMemories.Count > 0)
            {
                // LWN_plan_respond_section_recall：回忆段标题
                sb.AppendLine(LWNTextHelper.ResolveText("LWN_plan_respond_section_recall"));
                var recent = memory.DynamicMemories.Last;
                int shown = 0;
                while (recent != null && shown < 2)
                {
                    if (!string.IsNullOrEmpty(recent.Value.Content))
                    {
                        sb.AppendLine("- " + RelativeDayPrefix(recent.Value.CampaignDay) + recent.Value.Content);
                        shown++;
                    }
                    recent = recent.Previous;
                }
            }

            // 3. 近期对话：优先取与 otherId 相关的行（最多 6 句）；无 SpeakerId 的旧行（玩家对话）也保留；
            //    不足 6 句 → 从最近行补足（保持上下文连续）
            // 🔴 2026-08-16（prompt 精简）：跳过 channel_ 角色（群聊公区消息）——它们已由
            //    【频道近期消息】段（BuildChannelRecentSection）全量承担，再进【对话历史】= 同一批对话打印两遍。
            //    私聊线（im_user/im_npc）不受影响（群聊回复本就走 channelRecent 段）。
            if (memory.RecentHistory.Count > 0)
            {
                var selected = new List<ChatMessage>();
                for (int i = memory.RecentHistory.Count - 1; i >= 0 && selected.Count < 6; i--)
                {
                    var msg = memory.RecentHistory[i];
                    if (msg == null || string.IsNullOrEmpty(msg.Content)) continue;
                    if (msg.Role != null && msg.Role.StartsWith("channel_", StringComparison.Ordinal)) continue;
                    if (string.IsNullOrEmpty(msg.SpeakerId) || msg.SpeakerId == otherId)
                        selected.Insert(0, msg);
                }
                if (selected.Count < 6)
                {
                    for (int i = memory.RecentHistory.Count - 1; i >= 0 && selected.Count < 6; i--)
                    {
                        var msg = memory.RecentHistory[i];
                        if (msg == null || string.IsNullOrEmpty(msg.Content)) continue;
                        if (msg.Role != null && msg.Role.StartsWith("channel_", StringComparison.Ordinal)) continue;
                        if (!selected.Contains(msg))
                            selected.Insert(0, msg);
                    }
                }
                if (selected.Count > 0)
                {
                    // 修复历史乱序（日志暴露）：过滤 + 补足收集后按时间戳排序（related 行与补足行交错）
                    selected.Sort((a, b) => a.TimeStamp.CompareTo(b.TimeStamp));
                    // LWN_plan_respond_section_history：对话历史段标题
                    sb.AppendLine(LWNTextHelper.ResolveText("LWN_plan_respond_section_history"));
                    foreach (var msg in selected)
                        sb.AppendLine("- " + RelativeDayPrefix(msg.CampaignDay) + msg.Content);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 🔴 2026-08-16（方案 I5）：游戏内日 → 相对词前缀（契约的证据——「[3天前] 钱袋 5000」让模型
        /// 真正区分新旧；回应"那是几天前的账了"有据可依）。词表 8 档（差 = 当前 ToDays − 条目 CampaignDay）：
        /// 刚才(&lt;0.25 天) / 今天(&lt;1) / 昨天(&lt;2) / 几天前(&lt;4) / 上周(&lt;8) / 上个月(&lt;30) /
        /// 几个月前(&lt;90) / 很久以前(其余)。CampaignDay==0（旧存档）→ 空串不标（契约兜底，宁模糊不编数）。
        /// </summary>
        private static string RelativeDayPrefix(float campaignDay)
        {
            if (campaignDay <= 0f) return "";
            try
            {
                float now = (float)CampaignTime.Now.ToDays;
                float diff = now - campaignDay;
                if (diff < 0f) diff = 0f;
                string word;
                // 本地化：LWN_word_time_just（双桶）
                if (diff < 0.25f) word = LWNTextHelper.ResolvePrompt("LWN_word_time_just"); // lwn-ignore: A
                // 本地化：LWN_word_time_today（双桶）
                else if (diff < 1f) word = LWNTextHelper.ResolvePrompt("LWN_word_time_today");
                // 本地化：LWN_word_time_yesterday（双桶）
                else if (diff < 2f) word = LWNTextHelper.ResolvePrompt("LWN_word_time_yesterday");
                // 本地化：LWN_word_time_days_ago（双桶）
                else if (diff < 4f) word = LWNTextHelper.ResolvePrompt("LWN_word_time_days_ago");
                // 本地化：LWN_word_time_last_week（双桶）
                else if (diff < 8f) word = LWNTextHelper.ResolvePrompt("LWN_word_time_last_week");
                // 本地化：LWN_word_time_last_month（双桶）
                else if (diff < 30f) word = LWNTextHelper.ResolvePrompt("LWN_word_time_last_month");
                // 本地化：LWN_word_time_months_ago（双桶）
                else if (diff < 90f) word = LWNTextHelper.ResolvePrompt("LWN_word_time_months_ago");
                // 本地化：LWN_word_time_long_ago（双桶）
                else word = LWNTextHelper.ResolvePrompt("LWN_word_time_long_ago");
                return $"[{word}] ";
            }
            catch { return ""; }
        }

        /// <summary>
        /// IM 闲聊回复 prompt（ImReplyService 用）：世界观 + 身份（人设聚合）+ 记忆裁剪段 + 对方刚说
        /// + 动态知识注入段（<see cref="WorldFactProvider"/> 命中才有；平时为空串零注入）。
        /// 叙事铁律：NPC 只见自己的记忆（GetPrompt_RespondContext 按对方过滤），无上帝视角；
        /// 知识注入段同样按可见性裁剪（队伍成员才见队伍现状）。
        /// 🔴 2026-08-10（im-command-action-upgrade.md §5.1）：actionSpace 段 = 按空间裁剪的动作空间
        /// （ActionHandler.GetActionSpacePrompt），输出格式为 JSON（npc_reply/npc_action/action_target/action_level）。
        /// 🔴 2026-08-12（合并闲聊/计划模式）：executionContext 段 = 计划执行期间玩家传讯 → 注入【当前计划执行中】
        /// （PlanSummary + CurrentStep，C# 快照），LLM 判定 adjust_plan（问进度=false，明确改做法=true）；
        /// isCampaign = 大地图能力提示段（只建议行军类计划，防「我去暗杀谁」）。
        /// </summary>
        public static string BuildPrompt_ImReply(SingNpcMemorySystem memory, string otherId, string speakerName, string lastPlayerText, string worldFacts = null, string channelRecent = null, string peerInteraction = null, string actionSpace = null, ImCommandFlow.ImExecutionContext executionContext = null, bool isCampaign = false, string sceneAwareness = null, string riskScene = null, string npcHeroId = null, string campaignAwareness = null, string selfAwareness = null, string splitPartyAwareness = null, string stayedAwareness = null, string currentStatusLine = null, string playerRelationSection = null, string partyRelationSection = null, bool isPartyMember = true)
        {
            if (memory == null) return "";
            var sb = new StringBuilder();
            // 语言规则（prompt 顶部强指令，双桶 XML LWN_prompt_lang_rule；2026-08-20 双语化迁移）
            sb.AppendLine(LWNTextHelper.GetLanguageRule());
            sb.AppendLine();
            // 世界观段（blob 单段注入，2026-08-17：静态 flavor 退场，LLM 自动生成；blob 空 =
            // 未配置 LLM/生成失败/未就绪 → 标题+内容整段省略，防标题残留）
            string worldSection = WorldBackgroundProvider.GetWorldSection(npcHeroId);
            if (!string.IsNullOrWhiteSpace(worldSection))
            {
                // 本地化：LWN_plan_section_world（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_section_world") + worldSection); // lwn-ignore: B
                sb.AppendLine();
            }
            sb.AppendLine(Settings.Instance.SpeechStyle);
            sb.AppendLine();
            // 🔴 2026-08-17（称呼纪律，用户裁定三版定稿）：称呼 = LLM 现场发挥（无预生成矩阵）——
            // 双方性别年龄现取 + 对方（玩家）族长/队长身份；亲缘 NPC 附加第一人称亲缘段（那塔诺斯案）
            string addressSection = BuildAddressAndKinshipSections(npcHeroId);
            if (!string.IsNullOrWhiteSpace(addressSection))
            {
                sb.AppendLine(addressSection);
                sb.AppendLine();
            }
            // 🔴 2026-08-16（方案 F2）：自我认知段（【我的状态】+【主公的行头】+【队伍物资】）——
            // 独立段插在 sceneAwareness 前（第一人称亲见：谁都知道自己穿什么、几斤几两）
            if (!string.IsNullOrWhiteSpace(selfAwareness))
            {
                sb.AppendLine(selfAwareness);
                sb.AppendLine();
            }
            // 🔴 2026-08-16（方案 J3 补漏）：分兵随从自己的队伍状态段（【分兵近况】——自己的
            // party 位置/AI 行为/兵力，亲历级；补【此刻处境（大地图）】被裁后的自我定位空白）
            if (!string.IsNullOrWhiteSpace(splitPartyAwareness))
            {
                sb.AppendLine(splitPartyAwareness);
                sb.AppendLine();
            }
            // 🔴 2026-08-16（留守处境）：主队随从留守城外时的自我定位段（【留守处境】——亲历级；
            // 防 LLM 把主公的位置当自己的，实机 21:06 百草"我在吕卡隆城里"）
            if (!string.IsNullOrWhiteSpace(stayedAwareness))
            {
                sb.AppendLine(stayedAwareness);
                sb.AppendLine();
            }
            // 🔴 2026-08-16（方案 E2）：campaign 版【目之所及】（大地图环境视野，队伍成员才注入）
            if (!string.IsNullOrWhiteSpace(campaignAwareness))
            {
                sb.AppendLine(campaignAwareness);
                sb.AppendLine();
            }
            // 🔴 2026-08-16（方案 G10/T3a）：L1 常态段（主公的人缘 + 咱们人的关系）——队伍成员才注入
            if (!string.IsNullOrWhiteSpace(playerRelationSection))
            {
                sb.AppendLine(playerRelationSection);
                sb.AppendLine();
            }
            if (!string.IsNullOrWhiteSpace(partyRelationSection))
            {
                sb.AppendLine(partyRelationSection);
                sb.AppendLine();
            }
            // 🔴 2026-08-15（好感影响语气，用户需求）：与主公的关系段——NPC 对玩家的好感数值注入，
            // LLM 按数值定语气基调（亲近/友善/客气/冷淡/敌意）。仅 Hero 私聊/群聊注入（模板 NPC 无 Hero）。
            string relationSection = BuildRelationToPlayerSection(npcHeroId);
            if (!string.IsNullOrWhiteSpace(relationSection))
            {
                sb.AppendLine(relationSection);
                sb.AppendLine();
            }
            // 身份段：人设聚合（NPCProfile.GetPersonaPrompt：性格/动机/关系网）
            string persona = memory.GetPersonaPrompt();
            if (!string.IsNullOrWhiteSpace(persona))
            {
                sb.AppendLine(persona);
                sb.AppendLine();
            }
            // 记忆裁剪段（永久记忆 + 动态回忆 + 与对方相关的近期对话）
            string ctx = GetPrompt_RespondContext(memory, otherId);
            if (!string.IsNullOrWhiteSpace(ctx))
            {
                sb.AppendLine(ctx);
                sb.AppendLine();
            }
            // 频道公区近期消息（群聊回复注入；旁观者没参与也能接住"刚才聊了什么"——方案 B 即时层）
            if (!string.IsNullOrWhiteSpace(channelRecent))
            {
                // 本地化：prompt_section_channel（玩家可见文本）
                sb.AppendLine(LWNTextHelper.ResolveText("LWN_prompt_section_channel", "## Recent Channel Messages (public talk you witnessed)")); // lwn-ignore: B
                sb.AppendLine(channelRecent);
                sb.AppendLine();
            }
            // 🔴 群聊活力·拌嘴（2026-08-10）：跟随回复者的同僚互动段（关系档位 → 捧/呛/打岔）
            if (!string.IsNullOrWhiteSpace(peerInteraction))
            {
                sb.AppendLine(peerInteraction);
                sb.AppendLine();
            }
            // 🔴 动态知识注入段（WorldFactProvider：命中「队伍/位置/时间」主题才拼，平时零开销）
            if (!string.IsNullOrWhiteSpace(worldFacts))
            {
                sb.AppendLine(worldFacts);
                sb.AppendLine();
            }
            // 🔴 2026-08-13（场景认知注入）：场景内回复者的处境段（在哪 + 主公相对方位）——
            // 根治 IM 闲聊无场景认知（实锤：药僧在玩家 4 米外答"波罗斯城四五日脚程"）。
            // 主线程构建（WorldFactProvider.BuildSceneAwareness），后台生成直接拼字符串。
            if (!string.IsNullOrWhiteSpace(sceneAwareness))
            {
                sb.AppendLine(sceneAwareness);
                sb.AppendLine();
            }
            // 🔴 2026-08-14（M3/M4 风险审视）：命令注入场景感知——【目之所及】段（动作命令才注入，
            // 闲聊零开销）+ 【风险审视纪律】段（XML LWN_plan_risk_rules 单一事实源）。
            // 该 NPC 亲见的场面 = think-aloud 的事实来源：拒绝/计划时理由有事实依据。
            if (!string.IsNullOrWhiteSpace(riskScene))
            {
                sb.AppendLine(riskScene);
                sb.AppendLine();
                // 本地化：风险审视纪律段（XML LWN_plan_risk_rules，py/C# 同源单一事实源）
                string riskRules = LWNTextHelper.ResolvePrompt("LWN_plan_risk_rules");
                if (!string.IsNullOrWhiteSpace(riskRules))
                {
                    sb.AppendLine(riskRules);
                    sb.AppendLine();
                }
            }
            // 家族/国家全量背景：玩家提到相关话题才拼入（平时人设只有一句自我认知，见 NPCProfile）
            string mentioned = memory?._profile?.GetMentionedBackgroundPrompt(lastPlayerText);
            if (!string.IsNullOrWhiteSpace(mentioned))
            {
                sb.AppendLine(mentioned);
                sb.AppendLine();
            }
            // 🔴 2026-08-10 修复措辞：send 方恒为玩家（SendPlayerMessage 单入口）；
            // NPC 是主公麾下 → "你的主公 X 传讯给你"，否则 "对方 X 传讯给你"。
            // （旧 bug：ImReplyService 误传 NPC 自己的名字 → "对方 阿速甘 传讯给你" 自我传讯出戏）
            string senderPrefix = memory?._profile?.IsPlayerSubordinate() == true
                // 本地化：prompt_im_sender_lord（玩家可见文本）——2026-08-17 称呼纪律：抬头不再带"主公"
                ? LWNTextHelper.ResolveCompound("LWN_prompt_im_sender_lord", "{NAME} just sent you a secret letter:", ("NAME", speakerName)) // lwn-ignore: B
                // 本地化：prompt_im_sender_other（玩家可见文本）
                : LWNTextHelper.ResolveCompound("LWN_prompt_im_sender_other", "{NAME} just sent you a secret letter:", ("NAME", speakerName)); // lwn-ignore: B
            if (!string.IsNullOrWhiteSpace(peerInteraction))
            {
                // 🔴 v3.2（2026-08-10 用户反馈"两个NPC都在回我"）：跟随者改成**对主回复者说话**的对话流——
                // 主公是话题发起者，跟随者的重点是接主回复者的茬（风格见【同僚互动】段），不是再回一遍主公
                // 本地化：LWN_prompt_im_dialog_flow_1/2/3（对话流三段，双桶；2026-08-20 双语化迁移）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_im_dialog_flow_1",
                    ("SPEAKER", speakerName), ("TEXT", lastPlayerText)));
                // 本地化：LWN_prompt_im_dialog_flow_2（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_im_dialog_flow_2"));
                // 🔴 2026-08-13（实机：主公"你们俩都来我这"，跟随者只接话没动，主公再点名一次才动）：
                // 接话归接话、办事归办事——主公的话点名了你/你们（"你们俩都来""百草过来"这类），
                // 你在接完同僚的茬之后，照常执行对应的动作（move_to/follow/stop_following 等），
                // npc_action 不许因为"正在回应同僚"就填 NONE；主公的话与你无关时才只接话不动手。
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_im_dialog_flow_3"));
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine(senderPrefix);
                sb.AppendLine(lastPlayerText);
                sb.AppendLine();
            }
            // 🔴 2026-08-10 动作空间段（§5.1/§5.2）：按空间裁剪的合法动作列表——LLM 只看到
            // 当前空间的动作，无空间概念；输出格式 JSON 见 LWN_plan_im_reply_rule（XML 单一事实源）
            if (!string.IsNullOrWhiteSpace(actionSpace))
            {
                sb.AppendLine(actionSpace);
                sb.AppendLine();
            }
            // 🔴 2026-08-12（合并闲聊/计划模式）：执行期说话 → 计划调整（方案 A）——注入【当前计划执行中】段。
            // 文案单一事实源（strings XML LWN_prompt_im_execution_ctx，{SUMMARY}/{STEP} 变量）；
            // 纪律：问进度/闲聊/催促 → adjust_plan=false；玩家明确要求改变做法 → adjust_plan=true
            //（且 need_plan 必须 false）。无执行上下文 → 段缺省（普通闲聊，无调整判定）。
            if (executionContext != null)
            {
                // 执行上下文段（文案单一事实源：LWN_prompt_im_execution_ctx，{SUMMARY}/{STEP} 变量）
                string exec = LWNTextHelper.ResolveCompound("LWN_prompt_im_execution_ctx",
                    "## Current Plan in Progress\nYou are currently carrying out your lord's order: {SUMMARY}\nCurrent progress: {STEP}\nThe lord just sent you another message. If it only asks for progress or chats, answer normally (adjust_plan=false). Only if the lord explicitly wants you to change what you are doing (different target, order, or approach) set adjust_plan=true. When adjust_plan=true, need_plan must be false.",
                    ("SUMMARY", executionContext.PlanSummary),
                    ("STEP", executionContext.CurrentStep));
                if (!string.IsNullOrWhiteSpace(exec))
                {
                    sb.AppendLine(exec);
                    sb.AppendLine();
                }
            }
            // 🔴 2026-08-12（合并闲聊/计划模式）：Campaign 大地图能力提示段——无战场场景，NPC 只能建议
            // 行军类计划（跟随/待命/前往定居点）；防「我去暗杀谁」式无法执行的建议（出戏）。
            if (isCampaign)
            {
                // 🔴 2026-08-16（用户裁定：不在队伍就老实说不清楚）：能力段按回复者身份分流——
                // 队伍成员/分兵随从用现文案（跟随主公/原地待命/前往定居点）；家族离队成员（不在队伍）
                // 用 away 版：明确"主公队伍动向不知情，问位置/账目如实说不知道，禁止自称咱们/编地点"。
                // 现文案假设回应者是主公军队一员，对离队成员是误导（实机 2026-08-16：阿速甘答
                // "咱们正在卡拉迪亚大道上行进"——人设 away 文案单打独斗压不住能力段的"咱们"暗示）。
                string capKey;
                // 本地化：LWN_prompt_im_capability_campaign（队伍成员能力段）
                if (isPartyMember) capKey = "LWN_prompt_im_capability_campaign";
                // 本地化：LWN_prompt_im_capability_campaign_away（离队成员能力段）
                else capKey = "LWN_prompt_im_capability_campaign_away";
                string cap = LWNTextHelper.ResolvePrompt(capKey);
                if (!string.IsNullOrWhiteSpace(cap))
                {
                    sb.AppendLine(cap);
                    sb.AppendLine();
                }
            }
            // 🔴 2026-08-16（方案 I1）：触发式现状行【此刻现状】——聊过数值才注入（历史提及检测），
            // 未命中零注入（prompt 不膨胀）。【此刻现状】是当前值的唯一权威来源（与 I2 时效契约配合）
            if (!string.IsNullOrWhiteSpace(currentStatusLine))
            {
                sb.AppendLine(currentStatusLine);
                sb.AppendLine();
            }
            // 🔴 2026-08-16（方案 I2）：prompt 时效契约（主解，零 token，永远生效）——
            // 凡数值一律以【此刻现状】段为准；【近期回忆】与【对话历史】中的数值都是过去的快照；
            // 想引用的数值不在【此刻现状】段 → 宁可模糊化，禁止编具体数。
            // 不注入时 LLM 自然模糊化（随从记不清旧账反而真实）；注入时用当前值。
            string freshness = LWNTextHelper.ResolvePrompt("LWN_plan_im_freshness_rule");
            if (!string.IsNullOrWhiteSpace(freshness))
            {
                sb.AppendLine(freshness);
                sb.AppendLine();
            }
            // IM 回复纪律（XML 单一事实源：LWN_plan_im_reply_rule，EN/CN 同源）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_im_reply_rule"));
            // 本地化：LWN_prompt_lang_rule_history（历史语言强化，双桶；2026-08-20 防历史中文带偏输出）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_lang_rule_history"));
            // 本地化：LWN_prompt_lang_rule_final（最终输出语言强制令，双桶；2026-08-20 输出纪律处强指定语言）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_lang_rule_final"));
            // 🔴 2026-08-16（prompt 精简）：命令纪律/目标名纪律只在命令语境注入——riskScene（【目之所及】
            // + 风险审视纪律）本来就是"动作命令才注入"的代理；闲聊/问句不背命令规则（原全量注入 ~430 字）
            if (riskScene != null)
            {
                // 本地化：LWN_plan_im_command_rule（命令纪律段——动作命令语境才注入）
                string commandRule = LWNTextHelper.ResolvePrompt("LWN_plan_im_command_rule");
                if (!string.IsNullOrWhiteSpace(commandRule))
                    sb.AppendLine(commandRule);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 🔴 2026-08-15（好感影响语气，用户需求）：NPC 对玩家的好感段——把好感数值注入 IM 回复 prompt，
        /// LLM 按数值拿捏语气基调（亲近/友善/客气/冷淡/敌意），好感低时不再无差别热情。方向 = NPC 对玩家
        /// （npc.GetRelation(player)——态度由"他怎么看你"决定）。Hero 缺失/异常/key 缺失 → null（零注入，铁律 2）。
        /// 文案单一事实源：LWN_prompt_im_relation_section（{REL} 变量；EN/CN 同源）。
        /// </summary>
        private static string BuildRelationToPlayerSection(string npcHeroId)
        {
            if (string.IsNullOrEmpty(npcHeroId) || Hero.MainHero == null) return null;
            try
            {
                var npc = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == npcHeroId);
                if (npc == null) return null;
                int rel = npc.GetRelation(Hero.MainHero);
                // LLM prompt 材料（豁免铁律 13；单一事实源 = XML，缺失 → 零注入降级）
                string section = LWNTextHelper.ResolvePrompt("LWN_prompt_im_relation_section");
                if (string.IsNullOrWhiteSpace(section)) return null;
                return section.Replace("{REL}", rel.ToString());
            }
            catch { return null; }
        }
        /// <summary>
        /// 🔴 2026-08-17（称呼纪律，用户裁定三版迭代定稿）：【称呼纪律】段 + 【亲缘与身份认知】段。
        /// 称呼 = LLM 每次生成回复时按双方身份/阵营/阶级/性别/年龄**现场发挥**（生成产物，非配置参数）——
        /// 不写死"主公"，称呼随世界观与关系自然呈现；【称呼纪律】段 = 双方性别年龄现取 + 对方（玩家）
        /// 族长/队长身份（其余复用现有注入：persona 我方身份、与对方的关系段、对话历史、百科对方身份）。
        /// 亲缘与身份认知独立保留：NPC 与对方（玩家）有亲缘时注入第一人称亲缘段（亲缘关系重点说明 +
        /// 对方族长/队长身份），根治"否认兄弟关系"（2026-08-17 那塔诺斯案）。
        /// IM/群聊入口：npcHeroId = 说话 NPC 的 StringId；对方恒为玩家。
        /// </summary>
        public static string BuildAddressAndKinshipSections(string npcHeroId)
        {
            Hero npc = null;
            if (!string.IsNullOrEmpty(npcHeroId))
            {
                try { npc = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == npcHeroId); } catch { }
            }
            if (npc == null) return "";
            return BuildAddressAndKinshipSections(
                npc.IsFemale, npc.Age, npc,
                Hero.MainHero?.IsFemale == true, Hero.MainHero?.Age ?? 0f, Hero.MainHero,
                otherIsPlayer: true);
        }

        /// <summary>respond 链（当面对话/附近喊话/劝说/旁观插嘴）入口：self = 说话 NPC，other = 对话对象。
        /// 模板 NPC（无 Hero）→ 亲缘段跳过（亲缘需要 Hero），称呼纪律用 CharacterObject 性别/年龄兜底。</summary>
        public static string BuildAddressAndKinshipSections(Agent self, Agent other)
        {
            if (self == null) return "";
            var selfChar = self.Character;
            var otherChar = other?.Character;
            Hero selfHero = (selfChar as CharacterObject)?.HeroObject;
            Hero otherHero = (otherChar as CharacterObject)?.HeroObject;
            return BuildAddressAndKinshipSections(
                selfChar?.IsFemale == true, selfChar?.Age ?? 0f, selfHero,
                otherChar?.IsFemale == true, otherChar?.Age ?? 0f, otherHero,
                otherIsPlayer: other == Agent.Main);
        }

        /// <summary>核心构建：称呼纪律（双方性别年龄 + 对方族长/队长身份）+ 亲缘认知（有亲缘才注入）。
        /// 空串 = 不注入（调用方判断 IsNullOrWhiteSpace 跳过）。</summary>
        private static string BuildAddressAndKinshipSections(
            bool npcIsFemale, float npcAge, Hero npcHero,
            bool otherIsFemale, float otherAge, Hero otherHero, bool otherIsPlayer)
        {
            try
            {
                var sb = new StringBuilder();
                // ── 【称呼纪律】段（普世：任何对话双方都注入；亲缘称呼优先，对方是族长/队长按职位敬称）──
                // 本地化：LWN_prompt_address_head（称呼纪律抬头，双桶）
                sb.Append(LWNTextHelper.ResolvePrompt("LWN_prompt_address_head"));
                // 本地化：LWN_word_gender_male/female（性别词，双桶）
                sb.Append(npcIsFemale ? LWNTextHelper.ResolvePrompt("LWN_word_gender_female") : LWNTextHelper.ResolvePrompt("LWN_word_gender_male"));
                if (npcAge > 0)
                {
                    // 本地化：LWN_prompt_address_age（年龄拼接，双桶）
                    sb.Append(LWNTextHelper.ResolveCompound("LWN_prompt_address_age", ("AGE", ((int)npcAge).ToString())));
                }
                // 本地化：LWN_prompt_address_you（对方分隔，双桶）
                sb.Append(LWNTextHelper.ResolvePrompt("LWN_prompt_address_you"));
                // 本地化：LWN_word_gender_female（双桶）
                sb.Append(otherIsFemale ? LWNTextHelper.ResolvePrompt("LWN_word_gender_female") : LWNTextHelper.ResolvePrompt("LWN_word_gender_male"));
                if (otherAge > 0)
                {
                    // 本地化：LWN_prompt_address_age（年龄拼接，双桶）
                    sb.Append(LWNTextHelper.ResolveCompound("LWN_prompt_address_age", ("AGE", ((int)otherAge).ToString())));
                }
                // 本地化：LWN_prompt_address_close（括号闭合，双桶）
                sb.Append(LWNTextHelper.ResolvePrompt("LWN_prompt_address_close"));
                // 对方（玩家）身份：族长/无家族 + 队长（随从语境恒真，见 NpcTierHelper）
                if (otherIsPlayer && otherHero != null)
                {
                    // 本地化：LWN_prompt_address_player（玩家身份拼接，双桶）
                    sb.Append(LWNTextHelper.ResolveCompound("LWN_prompt_address_player", ("IDENTITY", BuildPlayerIdentityClause(otherHero))));
                }
                // 本地化：LWN_prompt_address_end（句号，双桶）
                sb.Append(LWNTextHelper.ResolvePrompt("LWN_prompt_address_end"));
                sb.AppendLine();
                // ── 【亲缘与身份认知】段（有亲缘才注入；关系×性别×年龄封闭集规则生成）──
                string kinship = BuildKinshipSection(npcHero, otherHero, otherIsPlayer);
                if (!string.IsNullOrWhiteSpace(kinship))
                {
                    sb.AppendLine(kinship);
                    sb.AppendLine();
                }
                return sb.ToString();
            }
            catch
            {
                return "";
            }
        }

        /// <summary>对方（玩家）身份句：族长判定 = Clan.Leader == MainHero；无家族变体「尚未建立自己的家族」；
        /// 队长 = 随从语境恒真（NPC 在玩家队伍/家族体系内，玩家必然是其队长）。</summary>
        private static string BuildPlayerIdentityClause(Hero player)
        {
            // 本地化：LWN_prompt_player_clan_leader/clanless（族长身份句，双桶）
            string family = (player != null && player.Clan != null && player.Clan.Leader == player)
                // 本地化：LWN_prompt_player_clan_leader（双桶）
                ? LWNTextHelper.ResolvePrompt("LWN_prompt_player_clan_leader")
                // 本地化：LWN_prompt_player_clanless（双桶）
                : LWNTextHelper.ResolvePrompt("LWN_prompt_player_clanless");
            // 本地化：LWN_prompt_player_captain（队长身份句，双桶）
            return family + LWNTextHelper.ResolvePrompt("LWN_prompt_player_captain");
        }

        /// <summary>亲缘认知段（第一人称亲缘关系 + 对方身份）；无亲缘返回 null（零注入）。</summary>
        private static string BuildKinshipSection(Hero npc, Hero other, bool otherIsPlayer)
        {
            if (npc == null || other == null || npc == other) return null;
            string relation = DescribeKinship(npc, other);
            if (relation == null) return null;
            string playerName = other.Name?.ToString() ?? "the other";
            // 本地化：LWN_prompt_kinship_head（亲缘段头，双桶）
            string section = LWNTextHelper.ResolveCompound("LWN_prompt_kinship_head", ("NAME", playerName), ("REL", relation));
            if (otherIsPlayer)
                section += BuildPlayerIdentityClause(other) + "。";
            return section;
        }

        /// <summary>亲缘关系描述（封闭集）：配偶 → 父母/子女 → 同胞（兄弟/姐妹/兄妹/姐弟 + 谁年长）。
        /// 代词他/她按对方性别；称谓按本 NPC 性别与年长。</summary>
        private static string DescribeKinship(Hero npc, Hero other)
        {
            // 本地化：LWN_word_pronoun_he/she（代词，双桶）
            string pronoun = other.IsFemale ? LWNTextHelper.ResolvePrompt("LWN_word_pronoun_she") : LWNTextHelper.ResolvePrompt("LWN_word_pronoun_he");
            // 配偶
            if (npc.Spouse == other)
            {
                // 本地化：LWN_prompt_kinship_spouse（夫妻关系，双桶）
                return LWNTextHelper.ResolveCompound("LWN_prompt_kinship_spouse",
                    ("PRONOUN", pronoun),
                    // 本地化：LWN_word_kin_wife（双桶）
                    ("ROLE", npc.IsFemale ? LWNTextHelper.ResolvePrompt("LWN_word_kin_wife") : LWNTextHelper.ResolvePrompt("LWN_word_kin_husband")));
            }
            // 父母（对方是 NPC 的父母）
            if (npc.Father == other || npc.Mother == other)
            {
                // 本地化：LWN_word_kin_father/mother/son/daughter（亲属词，双桶）
                string pair = (other.IsFemale ? LWNTextHelper.ResolvePrompt("LWN_word_kin_mother") : LWNTextHelper.ResolvePrompt("LWN_word_kin_father"))
                    // 本地化：LWN_word_kin_daughter（双桶）
                    + (npc.IsFemale ? LWNTextHelper.ResolvePrompt("LWN_word_kin_daughter") : LWNTextHelper.ResolvePrompt("LWN_word_kin_son"));
                // 本地化：LWN_prompt_kinship_parent（父母关系，双桶）
                return LWNTextHelper.ResolveCompound("LWN_prompt_kinship_parent",
                    ("PARENT", pair), ("PRONOUN", pronoun),
                    // 本地化：LWN_word_kin_role_daughter（双桶）
                    ("ROLE", npc.IsFemale ? LWNTextHelper.ResolvePrompt("LWN_word_kin_role_daughter") : LWNTextHelper.ResolvePrompt("LWN_word_kin_role_son")));
            }
            // 子女（对方是 NPC 的子女）
            if (npc.Children.Contains(other))
            {
                // 本地化：LWN_word_kin_father/mother/son/daughter（亲属词，双桶）
                string pair = (npc.IsFemale ? LWNTextHelper.ResolvePrompt("LWN_word_kin_mother") : LWNTextHelper.ResolvePrompt("LWN_word_kin_father"))
                    // 本地化：LWN_word_kin_daughter（双桶）
                    + (other.IsFemale ? LWNTextHelper.ResolvePrompt("LWN_word_kin_daughter") : LWNTextHelper.ResolvePrompt("LWN_word_kin_son"));
                // 本地化：LWN_prompt_kinship_child（子女关系，双桶）
                return LWNTextHelper.ResolveCompound("LWN_prompt_kinship_child",
                    ("PARENT", pair), ("PRONOUN", pronoun),
                    // 本地化：LWN_word_kin_role_mother（双桶）
                    ("ROLE", npc.IsFemale ? LWNTextHelper.ResolvePrompt("LWN_word_kin_role_mother") : LWNTextHelper.ResolvePrompt("LWN_word_kin_role_father")));
            }
            // 同胞
            if (npc.Siblings.Contains(other))
            {
                bool sameFather = npc.Father != null && npc.Father == other.Father;
                bool sameMother = npc.Mother != null && npc.Mother == other.Mother;
                // 本地化：LWN_word_kin_blood_full/father/mother/generic（血亲词，双桶）
                string blood = sameFather && sameMother ? LWNTextHelper.ResolvePrompt("LWN_word_kin_blood_full")
                    // 本地化：LWN_word_kin_blood_father（双桶）
                    : (sameFather ? LWNTextHelper.ResolvePrompt("LWN_word_kin_blood_father")
                        // 本地化：LWN_word_kin_blood_mother（双桶）
                        : (sameMother ? LWNTextHelper.ResolvePrompt("LWN_word_kin_blood_mother") : LWNTextHelper.ResolvePrompt("LWN_word_kin_blood_generic")));
                // 本地化：LWN_word_kin_sis_sis/sis_bro/bro_sis/bro_bro（同胞类型，双桶）
                string type = npc.IsFemale
                    // 本地化：LWN_word_kin_sis_sis（双桶）
                    ? (other.IsFemale ? LWNTextHelper.ResolvePrompt("LWN_word_kin_sis_sis") : LWNTextHelper.ResolvePrompt("LWN_word_kin_sis_bro"))
                    // 本地化：LWN_word_kin_bro_sis（双桶）
                    : (other.IsFemale ? LWNTextHelper.ResolvePrompt("LWN_word_kin_bro_sis") : LWNTextHelper.ResolvePrompt("LWN_word_kin_bro_bro"));
                bool npcElder = npc.Age >= other.Age;
                // 本地化：LWN_word_kin_elder_sis/younger_sis/elder_bro/younger_bro（自称词，双桶）
                string selfTitle = npc.IsFemale
                    // 本地化：LWN_word_kin_elder_sis（双桶）
                    ? (npcElder ? LWNTextHelper.ResolvePrompt("LWN_word_kin_elder_sis") : LWNTextHelper.ResolvePrompt("LWN_word_kin_younger_sis"))
                    // 本地化：LWN_word_kin_elder_bro（双桶）
                    : (npcElder ? LWNTextHelper.ResolvePrompt("LWN_word_kin_elder_bro") : LWNTextHelper.ResolvePrompt("LWN_word_kin_younger_bro"));
                // 本地化：LWN_prompt_kinship_sibling（同胞关系，双桶）
                return LWNTextHelper.ResolveCompound("LWN_prompt_kinship_sibling",
                    ("BLOOD", blood), ("TYPE", type), ("PRONOUN", pronoun), ("SELFTITLE", selfTitle));
            }
            return null;
        }

        /// <summary>
        /// 委托记录 Tab 的文本。从 QuestHistory 读取，按时间倒序展示。
        /// </summary>
        public static string GetPrompt_QuestHistory(SingNpcMemorySystem memory)
        {
            if (memory == null || memory.QuestHistory.Count == 0)
                // 本地化：LWN_ui_questhistory_empty（无委托记录兜底，双桶）
                return LWNTextHelper.ResolveText("LWN_ui_questhistory_empty");
            var sb = new StringBuilder();
            // 本地化：LWN_ui_questhistory_header（记录标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_ui_questhistory_header", ("COUNT", memory.QuestHistory.Count.ToString())));
            // 倒序：最新的在前
            for (int i = memory.QuestHistory.Count - 1; i >= 0; i--)
            {
                var r = memory.QuestHistory[i];
                sb.AppendLine(r.GetDisplaySummary());
            }
            return sb.ToString();
        }
        // --- A. 闲聊模式 (简化的 Prompt) ---
        private static string BuildCasualChatPrompt(SingNpcMemorySystem memory,string playerInput,Agent targetAgent)
        {
            StringBuilder sb = new StringBuilder();
            string npcName = memory._profile.Name;
            string playerName = Hero.MainHero != null ? Hero.MainHero.Name.ToString() : "玩家";
            // 语言规则（prompt 顶部强指令，双桶 XML LWN_prompt_lang_rule；2026-08-20 双语化迁移）
            sb.AppendLine(LWNTextHelper.GetLanguageRule());
            sb.AppendLine();
            // 本地化：LWN_prompt_section_task_chat（任务标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_task_chat"));
            // 🔴 2026-08-17：静态时代描述退场——改拼 blob 世界观段（闲聊是玩家问"这世界什么样"的
            // 高频链路，必须有 grounding；GetWorldSection(null) 全民同段纯字符串、无身份裁剪）
            string worldSection = WorldBackgroundProvider.GetWorldSection((string)null);
            if (!string.IsNullOrWhiteSpace(worldSection))
                sb.AppendLine(worldSection);
            // 本地化：LWN_prompt_chat_role（角色目标，双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_chat_role", ("NPC_NAME", npcName)));
            // 本地化：LWN_prompt_chat_main（输出内容说明，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_chat_main"));
            // 本地化：LWN_prompt_chat_nego_intro/1/2/3（谈判意图检测，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_chat_nego_intro"));
            // 本地化：LWN_prompt_chat_nego_1（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_chat_nego_1"));
            // 本地化：LWN_prompt_chat_nego_2（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_chat_nego_2"));
            // 本地化：LWN_prompt_chat_nego_3（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_chat_nego_3"));
            // 本地化：LWN_prompt_chat_nego_list（目的代码列表说明，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_chat_nego_list"));
            foreach (var kvp in NegotiationRegistry.Goal2Info)
            {
                NegotiationGoalTemplate tmpl = kvp.Value;
                if(tmpl.Type != NegotiationGoalType.None)
                    // 本地化：LWN_prompt_chat_nego_line（目的代码动态行，双桶）
                    sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_chat_nego_line",
                        ("TACTIC", tmpl.Type.ToString()), ("NAME", tmpl.Name), ("DESC", tmpl.Description)));
            }
            // 本地化：LWN_prompt_chat_nego_goal（目标描述生成，双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_chat_nego_goal", ("PLAYER_NAME", playerName)));
            // Npc人设
            // 本地化：LWN_prompt_section_self_info（自我信息段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_self_info"));
            sb.AppendLine(memory.GetPersonaPrompt());
            // 家族/国家全量背景：玩家提到相关话题才拼入（平时人设只有一句自我认知）
            string mentionedBg = memory._profile?.GetMentionedBackgroundPrompt(playerInput);
            if (!string.IsNullOrWhiteSpace(mentionedBg))
                sb.AppendLine(mentionedBg);
            // [新增] B. 玩家(对话对象) 信息
            // 本地化：LWN_prompt_section_player_info（玩家信息段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_player_info"));
            string playerContext = AllNpcMemoryManager.GetPlayerDescription(memory._profile);
            sb.AppendLine(playerContext);
            //拼入Npc人设、玩家人设、对话历史、记忆、事件、动作空间等
            sb.AppendLine(GetPrompt_History_Memory_Events(memory));
            sb.AppendLine();
            // 本地化：LWN_prompt_section_options（选项卡段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_options"));
            // 本地化：LWN_prompt_option_intro/tactics/emotion/impact（选项生成说明，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_option_intro"));
            // 本地化：LWN_prompt_option_tactics（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_option_tactics"));
            // 本地化：LWN_prompt_option_emotion（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_option_emotion"));
            // 本地化：LWN_prompt_option_impact（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_option_impact"));
            // 本地化：LWN_prompt_chat_json（JSON 输出格式，双桶；含 JSON 走 ResolvePrompt）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_chat_json"));
            // 本地化：LWN_prompt_section_notes（交谈注意事项段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_notes"));
            // 本地化：LWN_prompt_note_fact/repeat/rank/style/bracket（注意事项，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_note_fact"));
            // 本地化：LWN_prompt_note_repeat（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_note_repeat"));
            // 本地化：LWN_prompt_note_rank（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_note_rank"));
            // 本地化：LWN_prompt_note_style（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_note_style",
                ("LANG", LWNTextHelper.GetReplyLanguageInstruction()),
                ("STYLE", S.SpeechStyle),
                ("ADDR", S.FemaleSelfAddress)));
            // 本地化：LWN_prompt_note_bracket（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_note_bracket"));
            // 本地化：LWN_prompt_section_req_other（其他回复要求段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_req_other"));
            // 本地化：LWN_prompt_req_json/options/len_15/emotion/action（回复要求，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_req_json"));
            // 本地化：LWN_prompt_req_options（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_req_options"));
            // 本地化：LWN_prompt_req_len_15（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_req_len_15"));
            // 本地化：LWN_prompt_req_emotion（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_req_emotion"));
            // 本地化：LWN_prompt_req_action（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_req_action",
                ("ACTION_SPACE", ActionHandler.GetActionSpacePrompt(memory._profile.BaseHero, Hero.MainHero, targetAgent))));
            // 本地化：LWN_prompt_chat_backdoor（后门指令，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_chat_backdoor"));
            // 本地化：LWN_prompt_chat_input_header（玩家输入段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_chat_input_header"));
            sb.AppendLine(playerInput);
            // 本地化：LWN_prompt_lang_rule_final（最终输出语言强制令，双桶；2026-08-20 输出纪律处强指定语言）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_lang_rule_final"));
            return sb.ToString();
        }
        // --- B. 谈判模式 (核心逻辑) ---
       
       private static string GetCurrentNegotiationSituation(SingNpcMemorySystem memory, NegotiationCard selectedOption)
        {
            StringBuilder sb = new StringBuilder();
            var state = memory.CurrentNegotiationState;
            string npcName = memory._profile.Name;
            string playerName = Hero.MainHero != null ? Hero.MainHero.Name.ToString() : "玩家";
            float predictedTotal = state.CurrentProgress + (selectedOption.GetEstimatedValue() * 0.7f);
            float predictedRatio = predictedTotal / state.TargetThreshold;
            // 确保比率不超过 200% 以免数值崩坏，但保留溢出感
            if (predictedRatio > 2.0f) predictedRatio = 2.0f;
            string currentConflictStateStr = "";
            // --- 状态分层逻辑 ---
            if (predictedRatio >= 1.0f)
            {
                // === 阶段 5：达成共识 (Success) ===
                // 本地化：LWN_prompt_nego_reaction_high/ok（反应词，双桶）
                string reaction = predictedRatio > 1.5f
                    // 本地化：LWN_prompt_nego_reaction_high（双桶）
                    ? LWNTextHelper.ResolvePrompt("LWN_prompt_nego_reaction_high")
                    // 本地化：LWN_prompt_nego_reaction_ok（双桶）
                    : LWNTextHelper.ResolvePrompt("LWN_prompt_nego_reaction_ok");
                // 本地化：LWN_prompt_nego_situation_success（成功段，双桶）
                currentConflictStateStr = LWNTextHelper.ResolveCompound("LWN_prompt_nego_situation_success",
                    ("TOTAL", predictedTotal.ToString()), ("NPC_NAME", npcName), ("REACTION", reaction));
            }
            else if (predictedRatio >= 0.85f)
            {
                // === 阶段 4：动摇与最后的矜持 (Hesitation) ===
                // 本地化：LWN_prompt_nego_situation_hesitation（动摇段，双桶）
                currentConflictStateStr = LWNTextHelper.ResolveCompound("LWN_prompt_nego_situation_hesitation", ("NPC_NAME", npcName));
            }
            else if (predictedRatio >= 0.5f)
            {
                // === 阶段 3：博弈与贪婪 (Greed / Interest) ===
                // 本地化：LWN_prompt_nego_situation_greed（博弈段，双桶）
                currentConflictStateStr = LWNTextHelper.ResolveCompound("LWN_prompt_nego_situation_greed", ("NPC_NAME", npcName));
            }
            else if (predictedRatio >= 0.2f)
            {
                // === 阶段 2：冷漠与试探 (Indifference) ===
                // 本地化：LWN_prompt_nego_situation_indiff（冷漠段，双桶）
                currentConflictStateStr = LWNTextHelper.ResolveCompound("LWN_prompt_nego_situation_indiff", ("NPC_NAME", npcName));
            }
            else
            {
                // === 阶段 1：蔑视 (Contempt) ===
                // 本地化：LWN_prompt_nego_situation_contempt（蔑视段，双桶）
                currentConflictStateStr = LWNTextHelper.ResolveCompound("LWN_prompt_nego_situation_contempt", ("NPC_NAME", npcName));
            }
            // 本地化：LWN_prompt_nego_situation_pred（进度判定，双桶；RATIO 预格式化）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_situation_pred", ("RATIO", predictedRatio.ToString("P0"))));
            sb.AppendLine(currentConflictStateStr);
            string currentPatienceStr = "";
            if (state.MaxTurns - state.TurnCount <= 2)
                // 本地化：LWN_prompt_nego_patience_low（耐心耗尽，双桶）
                currentPatienceStr = LWNTextHelper.ResolveCompound("LWN_prompt_nego_patience_low", ("NPC_NAME", npcName), ("PLAYER_NAME", playerName));
            else if (state.TurnCount <= 2)
                // 本地化：LWN_prompt_nego_patience_ok（耐心尚可，双桶）
                currentPatienceStr = LWNTextHelper.ResolveCompound("LWN_prompt_nego_patience_ok", ("NPC_NAME", npcName), ("PLAYER_NAME", playerName));
            else
                // 本地化：LWN_prompt_nego_patience_normal（耐心一般，双桶）
                currentPatienceStr = LWNTextHelper.ResolveCompound("LWN_prompt_nego_patience_normal", ("NPC_NAME", npcName), ("PLAYER_NAME", playerName));
            // 本地化：LWN_prompt_nego_turns_left（剩余回合，双桶；PATIENCE 已解析）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_turns_left",
                ("TURNS", (state.MaxTurns - state.TurnCount - 1).ToString()), ("PATIENCE", currentPatienceStr)));
            return sb.ToString();
        }
        private static string BuildFailureAnalysisPrompt(SingNpcMemorySystem memory,Agent targetAgent)
        {
            var state = memory.CurrentNegotiationState;
            StringBuilder sb = new StringBuilder();
            string npcName = memory._profile.Name;
            float targetThreshold = state.TargetThreshold;
            float finalProgressRatio = 100 * state.CurrentProgress / state.TargetThreshold;
            var lastTwoTurns = state.TurnHistory.Take(2).ToList();
            float recentMoodScore = lastTwoTurns.Any() ? lastTwoTurns.Average(x => x.FeedbackMultiplier) : 1.0f;
            // --- 动态定调逻辑 (这是修正体验的关键) ---
            string emotionalGuidance = "";
            string summaryTitle = "";
            if (finalProgressRatio >= 0.90f)
            {
                // 情况A：虽败犹荣 (90%+)；本地化：LWN_prompt_nego_fail_title_a/guide_a（双桶）
                summaryTitle = LWNTextHelper.ResolvePrompt("LWN_prompt_nego_fail_title_a");
                // 本地化：LWN_prompt_nego_fail_guide_a（双桶）
                emotionalGuidance = LWNTextHelper.ResolvePrompt("LWN_prompt_nego_fail_guide_a");
            }
            else if (finalProgressRatio >= 0.60f && recentMoodScore < 0.8f)
            {
                // 情况B：前功尽弃 (60%+ 但最后时刻搞砸了)；本地化：LWN_prompt_nego_fail_title_b/guide_b（双桶）
                summaryTitle = LWNTextHelper.ResolvePrompt("LWN_prompt_nego_fail_title_b");
                // 本地化：LWN_prompt_nego_fail_guide_b（双桶）
                emotionalGuidance = LWNTextHelper.ResolvePrompt("LWN_prompt_nego_fail_guide_b");
            }
            else if (finalProgressRatio >= 0.50f)
            {
                // 情况C：实力不足；本地化：LWN_prompt_nego_fail_title_c/guide_c（双桶）
                summaryTitle = LWNTextHelper.ResolvePrompt("LWN_prompt_nego_fail_title_c");
                // 本地化：LWN_prompt_nego_fail_guide_c（双桶）
                emotionalGuidance = LWNTextHelper.ResolvePrompt("LWN_prompt_nego_fail_guide_c");
            }
            else
            {
                // 情况D：毫无希望；本地化：LWN_prompt_nego_fail_title_d/guide_d（双桶）
                summaryTitle = LWNTextHelper.ResolvePrompt("LWN_prompt_nego_fail_title_d");
                // 本地化：LWN_prompt_nego_fail_guide_d（双桶）
                emotionalGuidance = LWNTextHelper.ResolvePrompt("LWN_prompt_nego_fail_guide_d");
            }
            // 本地化：LWN_prompt_nego_fail_head/task/core（复盘头部，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_fail_head"));
            // 本地化：LWN_prompt_nego_fail_task（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_fail_task", ("NPC_NAME", npcName)));
            // 本地化：LWN_prompt_nego_fail_core（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_fail_core"));
            // 本地化：LWN_prompt_nego_fail_log_title（复盘数据标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_fail_log_title"));
            sb.AppendLine(summaryTitle);
            sb.AppendLine(emotionalGuidance); // <--- 强制注入情绪基调
            // 本地化：LWN_prompt_nego_fail_data/ratio/score（数据透视，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_fail_data"));
            // 本地化：LWN_prompt_nego_fail_ratio（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_fail_ratio", ("RATIO", finalProgressRatio.ToString("P1"))));
            // 本地化：LWN_prompt_nego_fail_score（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_fail_score", ("SCORE", recentMoodScore.ToString("F1"))));
            // 本地化：LWN_prompt_nego_fail_detail_title（详细记录标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_fail_detail_title"));
            // 预计算统计数据，用于后续生成精准指令
            float totalChipValue = 0f;
            int highSkillTurns = 0; // 高倍率回合数
            int lowSkillTurns = 0;  // 低倍率回合数
            int highValueTurns = 0; // 高筹码回合数
            // 遍历历史记录
            foreach (var log in state.TurnHistory)
            {
                totalChipValue += log.ChipValue;
                // 1. 定性分析：技巧 (Multiplier)；本地化：LWN_prompt_nego_mood_bad/good/normal（双桶）
                string moodDesc;
                if (log.FeedbackMultiplier <= 0.7f)
                {
                    // 本地化：LWN_prompt_nego_mood_bad（双桶）
                    moodDesc = LWNTextHelper.ResolvePrompt("LWN_prompt_nego_mood_bad");
                    lowSkillTurns++;
                }
                else if (log.FeedbackMultiplier >= 1.3f)
                {
                    // 本地化：LWN_prompt_nego_mood_good（双桶）
                    moodDesc = LWNTextHelper.ResolvePrompt("LWN_prompt_nego_mood_good");
                    highSkillTurns++;
                }
                else
                {
                    // 本地化：LWN_prompt_nego_mood_normal（双桶）
                    moodDesc = LWNTextHelper.ResolvePrompt("LWN_prompt_nego_mood_normal");
                }
                // 2. 定性分析：筹码分量 (ChipValue)；本地化：LWN_prompt_nego_chip_heavy/poor/normal（双桶）
                string chipDesc;
                float chipRatio = log.ChipValue / targetThreshold;
                if (chipRatio > 0.25f)
                {
                    // 本地化：LWN_prompt_nego_chip_heavy（双桶）
                    chipDesc = LWNTextHelper.ResolveCompound("LWN_prompt_nego_chip_heavy", ("RATIO", chipRatio.ToString("P0")));
                    highValueTurns++;
                }
                else if (chipRatio < 0.05f)
                {
                    // 本地化：LWN_prompt_nego_chip_poor（双桶）
                    chipDesc = LWNTextHelper.ResolveCompound("LWN_prompt_nego_chip_poor", ("RATIO", chipRatio.ToString("P0")));
                }
                else
                {
                    // 本地化：LWN_prompt_nego_chip_normal（双桶）
                    chipDesc = LWNTextHelper.ResolvePrompt("LWN_prompt_nego_chip_normal");
                }
                // 本地化：LWN_prompt_nego_no_input（玩家未说话兜底，双桶）
                string inputContent = string.IsNullOrEmpty(log.PlayerInput) ? LWNTextHelper.ResolvePrompt("LWN_prompt_nego_no_input") : "“" + log.PlayerInput + "”";
                // 生成单行日志；本地化：LWN_prompt_nego_turn_line/action_line/chip_line/progress_line/reply_line/separator（双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_turn_line", ("TURN", (log.TurnIndex + 1).ToString())));
                // 本地化：LWN_prompt_nego_action_line（双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_action_line",
                    ("TACTIC", log.PlayerTactic), ("INPUT", inputContent), ("MOOD", moodDesc), ("MULT", log.FeedbackMultiplier.ToString("F1"))));
                // 本地化：LWN_prompt_nego_chip_line（双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_chip_line",
                    ("CHIP", log.ChipValue.ToString("F0")), ("DESC", chipDesc)));
                // 本地化：LWN_prompt_nego_progress_line（双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_progress_line", ("DELTA", log.ProgressDelta.ToString("F0"))));
                // 本地化：LWN_prompt_nego_reply_line（双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_reply_line", ("REPLY", log.NpcReply)));
                // 本地化：LWN_prompt_nego_separator（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_separator"));
            }
            // 本地化：LWN_prompt_nego_global_title/progress/value（全局统计，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_global_title"));
            // 本地化：LWN_prompt_nego_global_progress（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_global_progress", ("RATIO", (state.CurrentProgress / targetThreshold).ToString("P1"))));
            // 本地化：LWN_prompt_nego_global_value（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_global_value",
                ("TOTAL", totalChipValue.ToString("F0")), ("TARGET", targetThreshold.ToString("F0"))));
            // === 逻辑分支 A：没钱装大款 (技巧好，但钱太少) ===
            // 判定条件：有高光时刻(高倍率)，但总投入极低(小于目标的40%)；本地化：LWN_prompt_nego_verdict_a_*（双桶）
            if (highSkillTurns > 0 && (totalChipValue / targetThreshold < 0.4f))
            {
                // 本地化：LWN_prompt_nego_verdict_a_title（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_verdict_a_title"));
                // 本地化：LWN_prompt_nego_verdict_a_analysis（双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_verdict_a_analysis", ("COUNT", highSkillTurns.ToString())));
                // 本地化：LWN_prompt_nego_verdict_a_advice（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_verdict_a_advice"));
            }
            // === 逻辑分支 B：土豪但嘴臭 (钱给够了，但把人得罪了) ===
            // 判定条件：有重金投入，但有低倍率回合导致系数崩盘，或者最后没成；本地化：LWN_prompt_nego_verdict_b_*（双桶）
            else if (highValueTurns > 0 && lowSkillTurns > 0)
            {
                // 本地化：LWN_prompt_nego_verdict_b_title（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_verdict_b_title"));
                // 本地化：LWN_prompt_nego_verdict_b_analysis（双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_verdict_b_analysis",
                    ("COUNT_H", highValueTurns.ToString()), ("COUNT_L", lowSkillTurns.ToString())));
                // 本地化：LWN_prompt_nego_verdict_b_advice（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_verdict_b_advice"));
            }
            // === 逻辑分支 C：纯粹的穷鬼/白嫖 (没钱也没技巧) ===
            // 本地化：LWN_prompt_nego_verdict_c_*（双桶）
            else if (totalChipValue / targetThreshold < 0.2f)
            {
                // 本地化：LWN_prompt_nego_verdict_c_title（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_verdict_c_title"));
                // 本地化：LWN_prompt_nego_verdict_c_analysis（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_verdict_c_analysis"));
                // 本地化：LWN_prompt_nego_verdict_c_advice（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_verdict_c_advice"));
            }
            // === 逻辑分支 D：功亏一篑 (各项都还行，就是差一点点) ===
            // 本地化：LWN_prompt_nego_verdict_d_*（双桶）
            else if (state.CurrentProgress / targetThreshold > 0.85f)
            {
                // 本地化：LWN_prompt_nego_verdict_d_title（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_verdict_d_title"));
                // 本地化：LWN_prompt_nego_verdict_d_analysis（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_verdict_d_analysis"));
                // 本地化：LWN_prompt_nego_verdict_d_advice（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_verdict_d_advice"));
            }
            // === 逻辑分支 E：平庸 ===
            // 本地化：LWN_prompt_nego_verdict_e_title/advice（双桶）
            else
            {
                // 本地化：LWN_prompt_nego_verdict_e_title（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_verdict_e_title"));
                // 本地化：LWN_prompt_nego_verdict_e_advice（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_verdict_e_advice"));
            }
            // 本地化：LWN_prompt_nego_fail_note（字数注意，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_fail_note"));
            // 本地化：LWN_prompt_section_req_other（其他回复要求段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_req_other"));
            // 本地化：LWN_prompt_req_json（JSON 纯净输出，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_req_json"));
            // 本地化：LWN_prompt_nego_fail_req_len（复盘长度限制，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_fail_req_len"));
            // 本地化：LWN_prompt_req_emotion（情绪枚举，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_req_emotion"));
            // 本地化：LWN_prompt_req_action（动作空间，双桶；ACTION_SPACE 运行时解析）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_req_action",
                ("ACTION_SPACE", ActionHandler.GetActionSpacePrompt(memory._profile.BaseHero, Hero.MainHero, targetAgent))));
            // 4. JSON 约束；本地化：LWN_prompt_nego_fail_json（JSON 示例，双桶；含 JSON 走 ResolvePrompt）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_fail_json"));
            return sb.ToString();
        }
        public static string GetCurrentTraitsPrompt(SingNpcMemorySystem memory)
        {
            var state = memory.CurrentNegotiationState;
            StringBuilder sb = new StringBuilder();
            if (state.ActiveTraits != null && state.ActiveTraits.Count > 0)
            {
                // 1. 根据极性对特征进行分类
                var resistances = state.ActiveTraits.Where(t => t.Polarity == TraitPolarity.Resistance).ToList();
                var weaknesses = state.ActiveTraits.Where(t => t.Polarity == TraitPolarity.Weakness).ToList();
                var immunities = state.ActiveTraits.Where(t => t.Polarity == TraitPolarity.Immunity).ToList();
                var neutrals = state.ActiveTraits.Where(t => t.Polarity == TraitPolarity.Neutral).ToList();
                // 本地化：LWN_prompt_nego_traits_title/intro（决策心理模型段，双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_traits_title"));
                // 本地化：LWN_prompt_nego_traits_intro（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_traits_intro"));
                // --- A. 免疫/绝对底线 (灰) ---
                if (immunities.Count > 0)
                {
                    // 本地化：LWN_prompt_nego_traits_immune/immune_desc（免疫段，双桶）
                    sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_traits_immune"));
                    // 本地化：LWN_prompt_nego_traits_immune_desc（双桶）
                    sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_traits_immune_desc"));
                    foreach (var t in immunities)
                    {
                        // 提示：如果是隐藏特征(IsSecret)且尚未被玩家发现，可以在这里决定是否暴露给LLM，
                        // 通常为了扮演真实，建议暴露给LLM但要求它"不要直接说破"，或者根据你的游戏设计决定。
                        sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_traits_cond", ("NAME", t.Name), ("DESC", t.Description)));
                    }
                }
                // --- B. 阻力/顾虑 (红) ---
                if (resistances.Count > 0)
                {
                    // 本地化：LWN_prompt_nego_traits_resist/resist_desc（阻力段，双桶）
                    sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_traits_resist"));
                    // 本地化：LWN_prompt_nego_traits_resist_desc（双桶）
                    sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_traits_resist_desc"));
                    foreach (var t in resistances)
                    {
                        // 本地化：LWN_prompt_nego_traits_cond（双桶）
                        sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_traits_cond", ("NAME", t.Name), ("DESC", t.Description)));
                    }
                }
                // --- C. 弱点/突破口 (绿) ---
                if (weaknesses.Count > 0)
                {
                    // 本地化：LWN_prompt_nego_traits_weak/weak_desc（弱点段，双桶）
                    sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_traits_weak"));
                    // 本地化：LWN_prompt_nego_traits_weak_desc（双桶）
                    sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_traits_weak_desc"));
                    foreach (var t in weaknesses)
                    {
                        // 本地化：LWN_prompt_nego_traits_cond（双桶）
                        sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_traits_cond", ("NAME", t.Name), ("DESC", t.Description)));
                    }
                }
                // --- D. 其他状态 (中性) ---
                if (neutrals.Count > 0)
                {
                    // 本地化：LWN_prompt_nego_traits_neutral（中性段标题，双桶）
                    sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_traits_neutral"));
                    foreach (var t in neutrals)
                    {
                        // 本地化：LWN_prompt_nego_traits_cond（双桶）
                        sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_traits_cond", ("NAME", t.Name), ("DESC", t.Description)));
                    }
                }
                sb.AppendLine(""); // 空行分隔
            }
            return sb.ToString();
        }
        private static string BuildNegotiationPrompt_New(SingNpcMemorySystem memory, PlayerResources playerRes, string playerInput, NegotiationCard selectedOption,Agent targetAgent)
        {
            StringBuilder sb = new StringBuilder();
            NegotiationState state = memory.CurrentNegotiationState;
            string npcName = memory._profile.Name;
            string playerName = Hero.MainHero != null ? Hero.MainHero.Name.ToString() : "玩家";
            if (selectedOption == null)
            {
                throw new Exception();
            }
            // 语言规则（prompt 顶部强指令，双桶 XML LWN_prompt_lang_rule；2026-08-20 双语化迁移）
            sb.AppendLine(LWNTextHelper.GetLanguageRule());
            sb.AppendLine();
            // 本地化：LWN_prompt_section_task_nego（任务标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_task_nego"));
            // 🔴 2026-08-10 修 bug：原为 "你是一个高自由度{S.WorldDescription}..." 漏 $ 插值，原样字符串打给 LLM
            // 🔴 2026-08-17：WorldDescription 退场，删字段引用（上帝裁判无需世界观 grounding）
            // 本地化：LWN_prompt_nego_role/tasks/task1/2/3（角色与任务，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_role"));
            // 本地化：LWN_prompt_nego_tasks（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_tasks"));
            // 本地化：LWN_prompt_nego_task1（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_task1", ("NPC_NAME", npcName)));
            // 本地化：LWN_prompt_nego_task2（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_task2"));
            // 本地化：LWN_prompt_nego_task3（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_task3"));
            // 本地化：LWN_prompt_nego_bg_title/bg/goal（谈判背景，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_bg_title"));
            // 本地化：LWN_prompt_nego_bg（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_bg",
                ("NPC_NAME", npcName), ("PLAYER_NAME", playerName), ("EVENT", state.Name)));
            // 本地化：LWN_prompt_nego_goal（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_goal",
                ("PLAYER_NAME", playerName), ("GOAL", state.PlayerGoalDescription)));
            // 本地化：LWN_prompt_nego_files_title（人物档案段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_files_title"));
            // 1. NPC 自我信息
            // 本地化：LWN_prompt_section_nego_npc_file（NPC 档案标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_nego_npc_file"));
            sb.AppendLine(memory.GetPersonaPrompt());
            // 家族/国家全量背景：玩家提到相关话题才拼入（平时人设只有一句自我认知）
            string mentionedBg = memory._profile?.GetMentionedBackgroundPrompt(playerInput);
            if (!string.IsNullOrWhiteSpace(mentionedBg))
                sb.AppendLine(mentionedBg);
            // 2. 玩家信息
            // 本地化：LWN_prompt_section_nego_player_file（玩家档案标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_nego_player_file"));
            sb.AppendLine(AllNpcMemoryManager.GetPlayerDescription(memory._profile));
            //拼入Npc人设、玩家人设、对话历史、记忆、事件、动作空间等
            sb.AppendLine(GetPrompt_History_Memory_Events(memory));
            //谈判开场
            if (state.TurnCount == -1)
            {
                // 本地化：LWN_prompt_section_nego_state（当前局势状态段标题，双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_nego_state"));
                sb.AppendLine(state.InitialImpressionContext);
            }
            //谈判过程
            else
            {
                float predictedTotal = state.CurrentProgress + (selectedOption.GetEstimatedValue());
                float predictedRatio = predictedTotal / state.TargetThreshold;
                //最后一回合检查，如果给的不够多直接失败
                if (state.TurnCount >= state.MaxTurns - 1 && predictedRatio < 1)
                {
                    // 本地化：LWN_prompt_section_nego_state（当前局势状态段标题，双桶）
                    sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_nego_state"));
                    sb.AppendLine(BuildFailureAnalysisPrompt(memory,targetAgent));
                    return sb.ToString();
                }
                else
                {
                    // 4. 当前局势判定 (根据你提供的逻辑)
                    // 本地化：LWN_prompt_section_nego_state（当前局势状态段标题，双桶）
                    sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_nego_state"));
                    sb.AppendLine(GetCurrentNegotiationSituation(memory, selectedOption));
                    sb.AppendLine(GetCurrentTraitsPrompt(memory));
                }
            }
            // 本地化：LWN_prompt_nego_chips_title/intro/chip_*（剩余筹码段，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_chips_title"));
            // 本地化：LWN_prompt_nego_chips_intro（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_chips_intro"));
            // 本地化：LWN_prompt_nego_chip_gold（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_chip_gold", ("GOLD", playerRes.PersonalGold.ToString())));
            // 本地化：LWN_prompt_nego_chip_reputation（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_chip_reputation", ("REP", playerRes.Reputation.ToString())));
            // 本地化：LWN_prompt_nego_chip_notoriety（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_chip_notoriety", ("NOTO", playerRes.Notoriety.ToString())));
            // 本地化：LWN_prompt_nego_chip_relation（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_chip_relation", ("REL", playerRes.SocialRelation.ToString())));
            // 本地化：LWN_prompt_section_options（选项卡段标题，双桶）
            sb.AppendLine();
            // 本地化：LWN_prompt_section_options（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_options"));
            // 本地化：LWN_prompt_nego_option_intro/cost/rule/impact（选项规则，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_option_intro"));
            // 本地化：LWN_prompt_nego_option_cost（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_option_cost"));
            // 本地化：LWN_prompt_nego_option_rule（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_option_rule"));
            // 本地化：LWN_prompt_option_impact（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_option_impact"));
            foreach (var kvp in NegotiationRegistry.Tactic2Info)
            {
                NegotiationMoveTemplate tmpl = kvp.Value;
                // 本地化：LWN_prompt_nego_option_list（可用 tactic 动态行，双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_option_list",
                    ("TACTIC", tmpl.Tactic.ToString()), ("COST_TYPE", tmpl.CostType.ToString()), ("DESC", tmpl.DescriptionPrompt)));
            }
            // 本地化：LWN_prompt_nego_cards_title/step6/diversity/budget（选项卡生成规则，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_cards_title"));
            // 本地化：LWN_prompt_nego_cards_step6（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_cards_step6"));
            // 本地化：LWN_prompt_nego_cards_diversity（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_cards_diversity"));
            // 本地化：LWN_prompt_nego_cards_budget（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_cards_budget"));
            // ==========================================
            // 第五部分：核心指令与思维链 (判定逻辑)
            // ==========================================
            // 本地化：LWN_prompt_section_nego_judge（判定逻辑步骤段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_nego_judge"));
            if (state.TurnCount == -1)
            {
                // 本地化：LWN_prompt_nego_judge_open/open_reply/open_thinking（开场判定，双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_judge_open"));
                // 本地化：LWN_prompt_nego_judge_open_reply（双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_judge_open_reply", ("NPC_NAME", npcName)));
                // 本地化：LWN_prompt_nego_judge_open_thinking（双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_judge_open_thinking", ("NPC_NAME", npcName)));
            }
            else
            {
                // 本地化：LWN_prompt_nego_judge_step1/step2/range1-4/step3/step4/step5（判定步骤，双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_judge_step1",
                    ("NPC_NAME", npcName), ("PLAYER_NAME", playerName)));
                // 本地化：LWN_prompt_nego_judge_step2（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_judge_step2"));
                // 本地化：LWN_prompt_nego_judge_range1（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_judge_range1"));
                // 本地化：LWN_prompt_nego_judge_range2（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_judge_range2"));
                // 本地化：LWN_prompt_nego_judge_range3（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_judge_range3"));
                // 本地化：LWN_prompt_nego_judge_range4（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_judge_range4"));
                // 本地化：LWN_prompt_nego_judge_step3（双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_judge_step3", ("NPC_NAME", npcName)));
                // 本地化：LWN_prompt_nego_judge_step4（双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_judge_step4", ("NPC_NAME", npcName)));
                // 本地化：LWN_prompt_nego_judge_step5（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_judge_step5"));
            }
            if(state.TurnCount == -1)
            // 本地化：LWN_prompt_section_nego_turn（玩家本回合行动段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_nego_turn"));
            if (state.TurnCount == -1)
            {
                // 本地化：LWN_prompt_nego_turn_start（开局行动，双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_turn_start"));
            }
            else
            {
                if (selectedOption != null && selectedOption.CostAmount > 0)
                {
                    // 假设 selectedOption 中包含了 CostType 和 CostAmount
                    // 你可能需要根据你的类结构调整这里，比如 selectedOption.Template.CostType
                    // 本地化：LWN_prompt_nego_turn_chips/strategy/value/judge/mult1-3（筹码行动，双桶）
                    sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_turn_chips"));
                    foreach (Chip oneChip in selectedOption.Chips)
                    {
                        sb.Append($"{oneChip.Amount}份{oneChip.Type}");
                    }
                    // 本地化：LWN_prompt_nego_turn_strategy（双桶）
                    sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_turn_strategy", ("TEXT", selectedOption.Text)));
                    float estimatedDelta = selectedOption.GetEstimatedValue();
                    // 本地化：LWN_prompt_nego_turn_value（双桶）
                    sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_turn_value", ("VALUE", estimatedDelta.ToString("F0"))));
                    // 本地化：LWN_prompt_nego_turn_judge（双桶）
                    sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_turn_judge", ("INPUT", playerInput)));
                    // 本地化：LWN_prompt_nego_turn_mult1（双桶）
                    sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_turn_mult1", ("VALUE", estimatedDelta.ToString("F0"))));
                    // 本地化：LWN_prompt_nego_turn_mult2（双桶）
                    sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_turn_mult2", ("VALUE", (estimatedDelta * 2.0f).ToString("F0"))));
                    // 本地化：LWN_prompt_nego_turn_mult3（双桶）
                    sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_nego_turn_mult3", ("VALUE", (estimatedDelta * 0.5f).ToString("F0"))));
                }
                else
                {
                    // 本地化：LWN_prompt_nego_turn_none（无筹码行动，双桶）
                    sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_turn_none"));
                }
            }
            // 本地化：LWN_prompt_section_notes（交谈注意事项段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_notes"));
            // 本地化：LWN_prompt_nego_note_fact（谈判版事实防御，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_note_fact"));
            // 本地化：LWN_prompt_note_repeat/rank/style/bracket（通用注意事项，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_note_repeat"));
            // 本地化：LWN_prompt_note_rank（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_note_rank"));
            // 本地化：LWN_prompt_note_style（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_note_style",
                ("LANG", LWNTextHelper.GetReplyLanguageInstruction()),
                ("STYLE", S.SpeechStyle),
                ("ADDR", S.FemaleSelfAddress)));
            // 本地化：LWN_prompt_note_bracket（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_note_bracket"));
            // 本地化：LWN_prompt_nego_note_fake（人情式虚伪，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_note_fake"));
            // 本地化：LWN_prompt_section_req_other（其他回复要求段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_req_other"));
            // 本地化：LWN_prompt_req_json/nego_req_len/req_emotion/req_action（回复要求，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_req_json"));
            // 本地化：LWN_prompt_nego_req_len（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_req_len"));
            // 本地化：LWN_prompt_req_emotion（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_req_emotion"));
            // 本地化：LWN_prompt_req_action（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_req_action",
                ("ACTION_SPACE", ActionHandler.GetActionSpacePrompt(memory._profile.BaseHero, Hero.MainHero, targetAgent))));
            // 4. JSON 约束；本地化：LWN_prompt_nego_json（JSON 示例，双桶；含 JSON 走 ResolvePrompt）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_nego_json"));
            // 本地化：LWN_prompt_lang_rule_final（最终输出语言强制令，双桶；2026-08-20 输出纪律处强指定语言）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_lang_rule_final"));
            return sb.ToString();
        }
        public static string GetRelationPrompt(SingNpcMemorySystem memory,string eventId)
        {
            Hero _hero = memory._profile.BaseHero;
            SocialEvent evt = NewsSpreadSystem.Instance.GetEventById(eventId);
            StringBuilder sb = new StringBuilder();
            // ... (之前的自我信息和事件描述) ...
            // ================== 新增：通用关系映射逻辑 ==================
            // 本地化：LWN_prompt_section_event_relations（人际关系提示段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_event_relations"));
            // 1. 获取事件中的关键人物对象
            Hero victim = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == evt.VictimId);
            Hero initiator = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == evt.InitiatorId);
            // 2. 动态判断受害者与"我"的关系 (通用逻辑)；本地化：LWN_prompt_relation_victim_*（双桶）
            if (victim != null)
            {
                if (victim == _hero)
                {
                    // 本地化：LWN_prompt_relation_victim_self（双桶）
                    sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_relation_victim_self"));
                }
                else if (_hero.Spouse == victim)
                {
                    // 本地化：LWN_prompt_relation_victim_spouse（双桶）
                    sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_relation_victim_spouse", ("NAME", victim.Name.ToString())));
                }
                else if (_hero.Children.Contains(victim))
                {
                    // 本地化：LWN_prompt_relation_victim_child（双桶）
                    sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_relation_victim_child", ("NAME", victim.Name.ToString())));
                }
                else if (_hero.Siblings.Contains(victim))
                {
                    // 本地化：LWN_prompt_relation_victim_sibling（双桶）
                    sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_relation_victim_sibling", ("NAME", victim.Name.ToString())));
                }
                else if (_hero.Clan?.Leader == victim && _hero.Clan?.Leader != _hero)
                {
                    // 本地化：LWN_prompt_relation_victim_leader（双桶）
                    sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_relation_victim_leader", ("NAME", victim.Name.ToString())));
                }
                else if(victim.Clan?.Leader == _hero && victim.Clan?.Leader != victim)
                {
                    // 本地化：LWN_prompt_relation_victim_retainer（双桶）
                    sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_relation_victim_retainer", ("NAME", victim.Name.ToString())));
                }
                else
                {
                    // 甚至可以判断朋友/敌人关系
                    int relation = _hero.GetRelation(victim);
                    // 本地化：LWN_prompt_relation_victim_friend（双桶）
                    if (relation > 20) sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_relation_victim_friend", ("NAME", victim.Name.ToString()), ("REL", relation.ToString())));
                    // 本地化：LWN_prompt_relation_victim_foe（双桶）
                    if (relation < -20) sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_relation_victim_foe", ("NAME", victim.Name.ToString()), ("REL", relation.ToString())));
                }
            }
            // 3. 动态判断肇事者与"我"的关系 (防止肇事者是自家人产生逻辑BUG)
            if (initiator != null)
            {
                // 本地化：LWN_prompt_relation_initiator_spouse（肇事者配偶行，双桶）
                if (initiator == _hero.Spouse)
                {
                    // 本地化：LWN_prompt_relation_initiator_spouse（双桶）
                    sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_relation_initiator_spouse", ("NAME", initiator.Name.ToString())));
                }
                // ... 同上，可以扩展 ...
            }
            // ==========================================================
            // 本地化：LWN_prompt_relation_tail（称呼调整说明，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_relation_tail"));
            return sb.ToString();
        }
        public static string BuildPromptForSocialEvent(SingNpcMemorySystem memory,string historyStr, string memoryStr)
        {
            // 使用 StringBuilder 构建清晰的 Prompt
            string npcName = memory._profile.Name;
            string playerName = Hero.MainHero.Name.ToString();
            StringBuilder sb = new StringBuilder();
            // 本地化：LWN_prompt_section_task_desc（任务描述段标题，双桶）
            // 语言规则（prompt 顶部强指令，双桶 XML LWN_prompt_lang_rule；2026-08-20 双语化迁移）
            sb.AppendLine(LWNTextHelper.GetLanguageRule());
            sb.AppendLine();
// 本地化：LWN_prompt_section_task_desc（双桶）
sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_task_desc"));
            // 本地化：LWN_prompt_memory_social_task1/2/3/4（任务说明，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_social_task1"));
            // 本地化：LWN_prompt_memory_social_task2（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_social_task2", ("NPC_NAME", npcName), ("PLAYER_NAME", playerName)));
            // 本地化：LWN_prompt_memory_social_task3（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_social_task3"));
            // 本地化：LWN_prompt_memory_social_task4（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_social_task4"));
            sb.AppendLine();
            // 本地化：LWN_prompt_memory_social_core1/2（通用处理规则，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_social_core1"));
            // 本地化：LWN_prompt_memory_social_core2（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_social_core2"));
            // 本地化：LWN_prompt_memory_social_ctx/ctx_mem/ctx_hist（上下文输入，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_social_ctx"));
            // 本地化：LWN_prompt_memory_social_ctx_mem（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_social_ctx_mem", ("MEMORY", memoryStr)));
            // 本地化：LWN_prompt_memory_social_ctx_hist（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_social_ctx_hist", ("HISTORY", historyStr)));
            sb.AppendLine();
            // 本地化：LWN_prompt_section_out_req（输出要求段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_out_req"));
            // 本地化：LWN_prompt_memory_social_req1-7（输出要求，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_social_req1"));
            // 本地化：LWN_prompt_memory_social_req2（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_social_req2"));
            // 本地化：LWN_prompt_memory_social_req3（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_social_req3"));
            // 本地化：LWN_prompt_memory_social_req4（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_social_req4"));
            // 本地化：LWN_prompt_memory_social_req5（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_social_req5"));
            // 本地化：LWN_prompt_memory_social_req6（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_social_req6"));
            // 本地化：LWN_prompt_memory_social_req7（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_social_req7"));
            sb.AppendLine();
            // 本地化：LWN_prompt_memory_social_json（JSON 模板，双桶；含 JSON 走 ResolvePrompt）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_social_json"));
            // 本地化：LWN_prompt_lang_rule_final（最终输出语言强制令，双桶；2026-08-20 输出纪律处强指定语言）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_lang_rule_final"));
            return sb.ToString();
        }
        public static string BuildPromptForPermanentMemory(SingNpcMemorySystem memory, string fadingMemory, string currentPermanentMemory)
        {
            string npcName = memory._profile.Name;
            StringBuilder sb = new StringBuilder();
            // 本地化：LWN_prompt_section_task_desc（任务描述段标题，双桶）
            // 语言规则（prompt 顶部强指令，双桶 XML LWN_prompt_lang_rule；2026-08-20 双语化迁移）
            sb.AppendLine(LWNTextHelper.GetLanguageRule());
            sb.AppendLine();
// 本地化：LWN_prompt_section_task_desc（双桶）
sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_task_desc"));
            // 🔴 2026-08-17：WorldDescription 退场，删字段引用（记忆判定无需世界观 grounding）
            // 本地化：LWN_prompt_memory_perm_task（任务说明，双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_perm_task", ("NPC_NAME", npcName)));
            // 本地化：LWN_prompt_memory_perm_content/fading/current（记忆内容，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_perm_content"));
            // 本地化：LWN_prompt_memory_perm_fading（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_perm_fading", ("FADING", fadingMemory)));
            // 本地化：LWN_prompt_memory_perm_current（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_perm_current", ("CURRENT", currentPermanentMemory)));
            // 本地化：LWN_prompt_section_out_format（输出格式段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_out_format"));
            // 本地化：LWN_prompt_memory_perm_json（JSON 模板，双桶；含 JSON 走 ResolvePrompt）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_perm_json"));
            // 本地化：LWN_prompt_lang_rule_final（最终输出语言强制令，双桶；2026-08-20 输出纪律处强指定语言）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_lang_rule_final"));
            return sb.ToString();
        }
        public static string BuildPromptForSummary(SingNpcMemorySystem memory, List<ChatMessage> messagesToSummarize)
        {
            StringBuilder sb = new StringBuilder();
            string npcName = memory._profile.Name;
            // （§八 任意人对话泛化的对方名提取不再需要：2026-08-10 起总结输入可能混合私聊与频道公开对话，
            //  任务描述改为通用表述，不再假定"和某一个人的对话"）
            // 本地化：LWN_prompt_section_task_desc（任务描述段标题，双桶）
            // 语言规则（prompt 顶部强指令，双桶 XML LWN_prompt_lang_rule；2026-08-20 双语化迁移）
            sb.AppendLine(LWNTextHelper.GetLanguageRule());
            sb.AppendLine();
// 本地化：LWN_prompt_section_task_desc（双桶）
sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_task_desc"));
            // 本地化：LWN_prompt_memory_summary_task/goal（任务说明，双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_summary_task", ("NPC_NAME", npcName)));
            // 本地化：LWN_prompt_memory_summary_goal（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_summary_goal", ("NPC_NAME", npcName)));
            // 本地化：LWN_prompt_section_chat_log（对话记录段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_chat_log"));
            foreach (var msg in messagesToSummarize)
            {
                // 频道来源标注：频道行带「（频道）」前缀，防把公区对话记成与某人的私聊（方案 B）
                string prefix = msg?.Role != null && msg.Role.StartsWith("channel_") ? LWNTextHelper.ResolveText("LWN_prompt_summary_channel_mark") : "";
                sb.AppendLine($"- {prefix}{msg.Content}");
            }
            // 频道行补充说明：只记梗概，别记成私聊细节
            // 本地化：LWN_prompt_memory_summary_channel_note（频道行补充说明，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_summary_channel_note"));
            // 本地化：LWN_prompt_section_out_format（输出格式段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_out_format"));
            // 本地化：LWN_prompt_memory_summary_json（JSON 模板，双桶；含 JSON 走 ResolvePrompt）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_summary_json"));
            // G. 回复要求
            // 本地化：LWN_prompt_section_req（回复要求段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_req"));
            // 本地化：LWN_prompt_memory_summary_req1/2/3/4/5（回复要求，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_summary_req1"));
            // 本地化：LWN_prompt_memory_summary_req2（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_summary_req2", ("NPC_NAME", npcName))); //再次强调
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_summary_req3", ("NPC_NAME", npcName))); // 防呆指令
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_summary_req4", ("STYLE", S.SpeechStyle)));
            // 本地化：LWN_prompt_memory_summary_req5（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_summary_req5"));
            // 本地化：LWN_prompt_lang_rule_final（最终输出语言强制令，双桶；2026-08-20 输出纪律处强指定语言）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_lang_rule_final"));
            return sb.ToString();
        }
        /// <summary>
        /// 旁白总结 prompt（SingNpcMemorySystem.MaintainNarrationAsync 用，2026-08-11）：
        /// 第一人称经历记录（被攻击/目击/奉命/认输）→ 一句 30 字以内记忆总结，进 DynamicMemories。
        /// 与 BuildPromptForSummary 同构（输出格式/防呆指令一致），输入换成旁白行。
        /// </summary>
        public static string BuildPromptForNarrationSummary(SingNpcMemorySystem memory, List<RecentMemory> narrationsToSummarize)
        {
            StringBuilder sb = new StringBuilder();
            string npcName = memory._profile.Name;
            // 本地化：LWN_prompt_section_task_desc（任务描述段标题，双桶）
            // 语言规则（prompt 顶部强指令，双桶 XML LWN_prompt_lang_rule；2026-08-20 双语化迁移）
            sb.AppendLine(LWNTextHelper.GetLanguageRule());
            sb.AppendLine();
// 本地化：LWN_prompt_section_task_desc（双桶）
sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_task_desc"));
            // 本地化：LWN_prompt_memory_narr_task/goal（任务说明，双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_narr_task", ("NPC_NAME", npcName)));
            // 本地化：LWN_prompt_memory_summary_goal（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_summary_goal", ("NPC_NAME", npcName)));
            // 本地化：LWN_prompt_section_experience_log（经历记录段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_experience_log"));
            foreach (var n in narrationsToSummarize)
            {
                if (n != null && !string.IsNullOrEmpty(n.Content))
                    sb.AppendLine($"- {n.Content}");
            }
            // 本地化：LWN_prompt_section_out_format（输出格式段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_out_format"));
            // 本地化：LWN_prompt_memory_summary_json（JSON 模板，双桶；含 JSON 走 ResolvePrompt）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_summary_json"));
            // G. 回复要求
            // 本地化：LWN_prompt_section_req（回复要求段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_req"));
            // 本地化：LWN_prompt_memory_summary_req1/2 + narr_req3 + req4/5（回复要求，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_summary_req1"));
            // 本地化：LWN_prompt_memory_summary_req2（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_summary_req2", ("NPC_NAME", npcName))); //再次强调
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_narr_req3", ("NPC_NAME", npcName))); // 防呆指令
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_summary_req4", ("STYLE", S.SpeechStyle)));
            // 本地化：LWN_prompt_memory_summary_req5（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_summary_req5"));
            // 本地化：LWN_prompt_lang_rule_final（最终输出语言强制令，双桶；2026-08-20 输出纪律处强指定语言）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_lang_rule_final"));
            return sb.ToString();
        }
        /// <summary>
        /// 人设精炼 prompt（SingNpcMemorySystem.EnsureProfileSummary 用，2026-08-10）：
        /// 第一次有对话素材时，把身份信息 + 性格数值 + 武艺技能 + 对话记录一次 LLM 调用精炼成
        /// 三字段第一人称常驻人设（BackgroundStory 身世 / Personality 性格 / Specialty 本事）。
        /// 输出：纯 JSON。LLM 失败/未配置 → 不生成，下次再试（铁律 1）。
        /// </summary>
        public static string BuildPromptForProfileSummary(SingNpcMemorySystem memory)
        {
            if (memory == null || memory._profile == null) return "";
            var profile = memory._profile;
            var hero = profile.BaseHero;
            var sb = new StringBuilder();
            // 本地化：LWN_prompt_section_task_desc（任务段标题，双桶）
            // 语言规则（prompt 顶部强指令，双桶 XML LWN_prompt_lang_rule；2026-08-20 双语化迁移）
            sb.AppendLine(LWNTextHelper.GetLanguageRule());
            sb.AppendLine();
// 本地化：LWN_prompt_section_task_desc（双桶）
sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_task_desc"));
            // 本地化：LWN_prompt_memory_profile_task（任务说明，双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_profile_task", ("NAME", profile.Name)));
            // 本地化：LWN_prompt_memory_profile_req_head/req1-5（要求，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_profile_req_head"));
            // 本地化：LWN_prompt_memory_profile_req1（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_profile_req1"));
            // 本地化：LWN_prompt_memory_profile_req2（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_profile_req2"));
            // 本地化：LWN_prompt_memory_profile_req3（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_profile_req3"));
            // 本地化：LWN_prompt_memory_profile_req4（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_profile_req4"));
            // 本地化：LWN_prompt_memory_profile_req5（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_profile_req5"));
            // 本地化：LWN_prompt_section_identity_info（身份信息段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_identity_info"));
            // 本地化：LWN_prompt_memory_profile_name/occ（姓名职业行，双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_profile_name", ("NAME", profile.Name)));
            // 本地化：LWN_prompt_memory_profile_occ（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_profile_occ", ("OCC", profile.Occupation)));
            sb.AppendLine(profile.GetStandingSummary());
            if (hero != null)
            {
                // 性格数值（引擎真实 trait，LLM 需翻译成人话）
                int honor = profile.CoreValues.ContainsKey("Honor") ? profile.CoreValues["Honor"] : 0;
                int mercy = profile.CoreValues.ContainsKey("Mercy") ? profile.CoreValues["Mercy"] : 0;
                int valor = profile.CoreValues.ContainsKey("Valor") ? profile.CoreValues["Valor"] : 0;
                int calc = profile.CoreValues.ContainsKey("Calculating") ? profile.CoreValues["Calculating"] : 0;
                // 本地化：LWN_prompt_section_traits（性格数值段标题，双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_traits"));
                // 本地化：LWN_prompt_memory_profile_traits（性格数值行，双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_profile_traits",
                    ("HONOR", honor.ToString()), ("MERCY", mercy.ToString()), ("VALOR", valor.ToString()), ("CALC", calc.ToString())));
                // 武艺技能（动态遍历 MBObjectManager，铁律 5；前 6 项）
                // 本地化：LWN_prompt_section_skills（武艺技能段标题，双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_skills"));
                try
                {
                    var skills = MBObjectManager.Instance.GetObjectTypeList<SkillObject>()
                        .Where(s => s != null && !string.IsNullOrEmpty(s.Name?.ToString()) && hero.GetSkillValue(s) > 0)
                        .OrderByDescending(s => hero.GetSkillValue(s))
                        .Take(6)
                        .Select(s => $"{s.Name} {hero.GetSkillValue(s)}")
                        .ToList();
                    if (skills.Count == 0)
                        // 本地化：LWN_prompt_profile_no_skill（无技能行，双桶）
                        sb.AppendLine(LWNTextHelper.ResolveText("LWN_prompt_profile_no_skill"));
                    else
                    {
                        // 本地化：LWN_prompt_memory_profile_skill_join（技能分隔符，双桶）
                        sb.AppendLine("- " + string.Join(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_profile_skill_join"), skills));
                    }
                }
                catch
                {
                    // 本地化：LWN_prompt_profile_skill_unavailable（技能不可用行，双桶）
                    sb.AppendLine(LWNTextHelper.ResolveText("LWN_prompt_profile_skill_unavailable"));
                }
            }
            if (!string.IsNullOrEmpty(memory.BackgroundStory))
            {
                // 旧存档升级场景：已有身世保持原文不重写（只补性格/本事）
                // 本地化：LWN_prompt_section_bg_keep（已有身世段标题，双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_bg_keep"));
                sb.AppendLine(memory.BackgroundStory);
            }
            // 本地化：LWN_prompt_section_chat_log（对话记录段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_chat_log"));
            foreach (var msg in memory.SnapshotRecentHistory())
            {
                if (msg == null || string.IsNullOrEmpty(msg.Content)) continue;
                sb.AppendLine($"- {msg.Content}");
            }
            // 本地化：LWN_prompt_section_out_format（输出格式段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_out_format"));
            // 本地化：LWN_prompt_memory_profile_json（JSON 模板，双桶；含 JSON 走 ResolvePrompt）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_profile_json"));
            // 本地化：LWN_prompt_lang_rule_final（最终输出语言强制令，双桶；2026-08-20 输出纪律处强指定语言）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_lang_rule_final"));
            return sb.ToString();
        }
        /// <summary>
        /// 事件主动话题评论 prompt（ImEventBroadcaster 用，2026-08-10）：
        /// NPC 听说玩家经历的大事件（战斗/坐牢/任务/新人/洗劫/王国兴灭）→ 在队伍频道说一句
        /// 自己的看法。输出：一句台词（非 JSON）。LLM 失败/未配置 → 模板兜底（铁律 1）。
        /// </summary>
        public static string BuildPromptForEventComment(Hero speaker, string eventKey, string description)
        {
            if (speaker == null) return "";
            var sb = new StringBuilder();
            // 本地化：LWN_prompt_memory_comment_task（任务说明，双桶）
            // 语言规则（prompt 顶部强指令，双桶 XML LWN_prompt_lang_rule；2026-08-20 双语化迁移）
            sb.AppendLine(LWNTextHelper.GetLanguageRule());
            sb.AppendLine();
// 本地化：LWN_prompt_memory_comment_task（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_comment_task", ("SPEAKER", speaker.Name.ToString())));
            // 本地化：LWN_prompt_memory_comment_event（事件行，双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_memory_comment_event", ("DESC", description)));
            // 本地化：LWN_prompt_memory_comment_req_head/req1-3（要求，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_comment_req_head"));
            // 本地化：LWN_prompt_memory_comment_req1（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_comment_req1"));
            // 本地化：LWN_prompt_memory_comment_req2（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_comment_req2"));
            // 本地化：LWN_prompt_memory_comment_req3（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_memory_comment_req3"));
            // 本地化：LWN_prompt_lang_rule_final（最终输出语言强制令，双桶；2026-08-20 输出纪律处强指定语言）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_lang_rule_final"));
            return sb.ToString();
        }
        /// <summary>从对话记录提取对方说话人名字（Content 惯例"名字: 台词"；排除本 NPC 自己）。
        /// 玩家对话 = 玩家名；NPC-NPC 对话 = 随从名——总结归因泛化（§八）。</summary>
        private static string ExtractOtherSpeakerName(SingNpcMemorySystem memory, List<ChatMessage> messages)
        {
            string npcName = memory._profile.Name;
            if (messages == null) return "someone";
            foreach (var msg in messages)
            {
                if (msg == null || string.IsNullOrEmpty(msg.Content)) continue;
                string name = msg.Content;
                int colon = name.IndexOf(':');
                if (colon <= 0) colon = name.IndexOf('：');
                if (colon > 0)
                {
                    string candidate = name.Substring(0, colon).Trim();
                    if (!string.IsNullOrEmpty(candidate) && !candidate.Equals(npcName, StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
            }
            return "someone";
        }
        public static string BuildDirectorPrompt(ScreenPlayOutline outline)
        {
            if (outline == null || outline.Accused == null || outline.Accuser == null)
                // 本地化：LWN_prompt_director_error（角色缺失错误，双桶）
                return LWNTextHelper.ResolvePrompt("LWN_prompt_director_error");
            StringBuilder sb = new StringBuilder();
            // 本地化：LWN_prompt_section_task（任务段标题，双桶）
            // 语言规则（prompt 顶部强指令，双桶 XML LWN_prompt_lang_rule；2026-08-20 双语化迁移）
            sb.AppendLine(LWNTextHelper.GetLanguageRule());
            sb.AppendLine();
// 本地化：LWN_prompt_section_task（双桶）
sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_task"));
            // 本地化：LWN_prompt_director_task（任务说明，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_director_task"));
            SocialEvent evt = outline.SourceEvent;
            // 本地化：LWN_prompt_section_rumor_info（传闻信息段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_rumor_info"));
            // 本地化：LWN_prompt_director_loc/conflict/evidence（传闻细节，双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_director_loc", ("LOC", evt.Location)));
            // 本地化：LWN_prompt_director_conflict（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_director_conflict", ("DESC", evt.Description)));
            // 本地化：LWN_prompt_director_evidence（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_director_evidence",
                ("SPEAKER", evt.KeyQuoteSpeakerName), ("QUOTE", evt.KeyQuoteText)));
            // 本地化：LWN_prompt_section_cast（演员表段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_cast"));
            // 本地化：LWN_prompt_director_cast_intro/accused/accuser/authority/gallery（角色行，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_director_cast_intro"));
            // 本地化：LWN_prompt_director_accused（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_director_accused",
                ("NAME", outline.Accused.Name.ToString()),
                ("PERSONA", AllNpcMemoryManager.GenerateHeroProfile(outline.Accused).GetPersonaPrompt())));
            // 原告行（代理注单独拼，防空值占位残留）
            string accuserLine = LWNTextHelper.ResolveCompound("LWN_prompt_director_accuser",
                ("NAME", outline.Accuser.Name.ToString()),
                ("PERSONA", AllNpcMemoryManager.GenerateHeroProfile(outline.Accuser).GetPersonaPrompt()));
            if (outline.Accuser.StringId != outline.SourceEvent.VictimId)
                // 本地化：LWN_prompt_director_accuser_note（代理人注释，双桶）
                accuserLine += LWNTextHelper.ResolvePrompt("LWN_prompt_director_accuser_note");
            sb.AppendLine(accuserLine);
            if (outline.Authority != null)
            {
                // 本地化：LWN_prompt_director_authority（双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_director_authority",
                    ("NAME", outline.Authority.Name.ToString()),
                    ("PERSONA", AllNpcMemoryManager.GenerateHeroProfile(outline.Authority).GetPersonaPrompt())));
            }
            string GalleryNames = "";
            if (outline.Gallery.Count > 0)
            {
                GalleryNames = string.Join(", ", outline.Gallery.Select(h => h.Name));
                // 本地化：LWN_prompt_director_gallery（围观者行，双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_director_gallery", ("NAMES", GalleryNames)));
            }
            // 关键：告诉 AI 玩家扮演了哪个角色；本地化：LWN_prompt_director_role_*（双桶）
            string playerRoleDesc = "";
            // 本地化：LWN_prompt_director_role_accused（双桶）
            if (outline.Accused == Hero.MainHero) playerRoleDesc = LWNTextHelper.ResolvePrompt("LWN_prompt_director_role_accused");
            // 本地化：LWN_prompt_director_role_accuser（双桶）
            else if (outline.Accuser == Hero.MainHero) playerRoleDesc = LWNTextHelper.ResolvePrompt("LWN_prompt_director_role_accuser");
            // 本地化：LWN_prompt_director_role_authority（双桶）
            else if (outline.Authority == Hero.MainHero) playerRoleDesc = LWNTextHelper.ResolvePrompt("LWN_prompt_director_role_authority");
            // 本地化：LWN_prompt_director_role_bystander（双桶）
            else playerRoleDesc = LWNTextHelper.ResolvePrompt("LWN_prompt_director_role_bystander");
            // 本地化：LWN_prompt_section_player_role（玩家定位段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_director_player_line", ("ROLE", playerRoleDesc)));
            sb.AppendLine();
            // 本地化：LWN_prompt_section_director_flow（剧本流向要求段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_director_flow"));
            // 本地化：LWN_prompt_director_flow_intro/flow1（开场演出，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_director_flow_intro"));
            // 本地化：LWN_prompt_director_flow1（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_director_flow1",
                ("ACCUSER", outline.Accuser.Name.ToString()), ("ACCUSED", outline.Accused.Name.ToString())));
            if (outline.Authority != null)
            {
                // 本地化：LWN_prompt_director_flow2_authority（仲裁者介入，双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_director_flow2_authority", ("NAME", outline.Authority.Name.ToString())));
            }
            else
            {
                // 本地化：LWN_prompt_director_flow2_crowd（人群起哄，双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_director_flow2_crowd"));
            }
            // 根据玩家是不是被告，生成的危机感不同；本地化：LWN_prompt_director_flow3/4_*（双桶）
            if (outline.Accused == Hero.MainHero)
            {
                // 本地化：LWN_prompt_director_flow3_accused（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_director_flow3_accused"));
                // 本地化：LWN_prompt_director_flow4_accused（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_director_flow4_accused"));
            }
            else if (outline.Authority == Hero.MainHero)
            {
                // 本地化：LWN_prompt_director_flow3_authority（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_director_flow3_authority"));
                // 本地化：LWN_prompt_director_flow4_authority（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_director_flow4_authority"));
            }
            else
            {
                // 玩家是原告或旁观
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_director_flow3_bystander"));
                // 本地化：LWN_prompt_director_flow4_bystander（双桶）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_director_flow4_bystander"));
            }
            // 本地化：LWN_prompt_section_director_example（大纲示例段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_director_example"));
            // 本地化：LWN_prompt_director_example（大纲示例正文，双桶；2026-08-20 清战国残留）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_director_example"));
            // 本地化：LWN_prompt_section_out_req（输出要求段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_out_req"));
            // 本地化：LWN_prompt_director_req1/2/3/4（输出要求，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_director_req1"));
            // 本地化：LWN_prompt_director_req2（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_director_req2"));
            // 本地化：LWN_prompt_director_req3（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_director_req3"));
            // 本地化：LWN_prompt_director_req4（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_director_req4"));
            // 本地化：LWN_prompt_lang_rule_final（最终输出语言强制令，双桶；2026-08-20 输出纪律处强指定语言）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_lang_rule_final"));
            return sb.ToString();
        }
        public static string BuildShowPrompt(ScreenPlayOutline outline,string directorBook)
        {
            StringBuilder sb = new StringBuilder();
            // 本地化：LWN_prompt_section_task（任务段标题，双桶）
            // 语言规则（prompt 顶部强指令，双桶 XML LWN_prompt_lang_rule；2026-08-20 双语化迁移）
            sb.AppendLine(LWNTextHelper.GetLanguageRule());
            sb.AppendLine();
// 本地化：LWN_prompt_section_task（双桶）
sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_task"));
            // 本地化：LWN_prompt_show_task/role（任务说明，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_show_task"));
            // 本地化：LWN_prompt_show_role（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_show_role", ("NAME", Hero.MainHero.Name.ToString())));
            // 本地化：LWN_prompt_section_show_outline（剧本梗概段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_show_outline"));
            SocialEvent evt = outline.SourceEvent;
            string GalleryNames = "";
            if (outline.Gallery.Count > 0)
            {
                GalleryNames = string.Join(", ", outline.Gallery.Select(h => h.Name));
            }
            // 本地化：LWN_prompt_show_info/evidence/cast/flow（梗概细节，双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_show_info", ("DESC", evt.Description)));
            // 本地化：LWN_prompt_show_evidence（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_show_evidence",
                ("SPEAKER", evt.KeyQuoteSpeakerName), ("QUOTE", evt.KeyQuoteText)));
            // 本地化：LWN_prompt_show_cast（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_show_cast",
                ("ACCUSED", outline.Accused.Name.ToString()), ("ACCUSER", outline.Accuser.Name.ToString()),
                ("AUTHORITY", outline.Authority?.Name.ToString() ?? ""), ("GALLERY", GalleryNames)));
            // 本地化：LWN_prompt_show_flow（双桶）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_show_flow", ("BOOK", directorBook)));
            // 本地化：LWN_prompt_section_show_format（输出格式段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_show_format"));
            // 本地化：LWN_prompt_show_format1/2/3（格式要求，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_show_format1"));
            // 本地化：LWN_prompt_show_format2（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_show_format2"));
            // 本地化：LWN_prompt_show_format3（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_show_format3"));
            // 本地化：LWN_prompt_section_show_commands（脚本指令段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_show_commands"));
            // 本地化：LWN_prompt_show_commands（脚本指令模板，双桶；含 JSON 走 ResolvePrompt；2026-08-20 清战国残留）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_show_commands"));
            // 本地化：LWN_prompt_section_out_req（输出要求段标题，双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_section_out_req"));
            // 本地化：LWN_prompt_show_req1-7（输出要求，双桶；req1 的 STYLE 运行时解析——
            // 🔴 2026-08-20 修复：原 C# 漏 $ 前缀导致 {S.WarriorTerms} 以字面量发给 LLM）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_show_req1", ("STYLE", S.WarriorTerms)));
            // 本地化：LWN_prompt_show_req2（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_show_req2"));
            // 本地化：LWN_prompt_show_req3（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_show_req3"));
            // 本地化：LWN_prompt_show_req4（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_show_req4"));
            // 本地化：LWN_prompt_show_req5（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_show_req5"));
            // 本地化：LWN_prompt_show_req6（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_show_req6"));
            // 本地化：LWN_prompt_show_req7（双桶）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_show_req7"));
            // 本地化：LWN_prompt_lang_rule_final（最终输出语言强制令，双桶；2026-08-20 输出纪律处强指定语言）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_prompt_lang_rule_final"));
            return sb.ToString();
        }
        /// <summary>密谋命令系统：意图分类 + 计划生成 prompt（§9）。
        /// 意图词表全表 + 动作/谓词封闭词表 + 角色表 + 人设 + 命令 + 澄清历史 + 完整 JSON 模板
        /// + 跳转双向校验纪律（§5.1 铁律）。
        /// 🔴 2026-08-15（目标唯一标记）：resolvedTargetText = 回复轮已解析目标（含 #N）——【目标指认】段
        /// 直接引用（玩家说的「酒馆老板」= 场景里的「酒馆店主#3」已固定，不再二次解析）。
        /// 🔴 2026-08-19（目标纪律）：targetCandidatesText = 命令对应多人的候选清单/目标类型无匹配的
        /// 诚实声明——注入计划轮（LLM 必须 questions 让主公挑，禁止自行指定一个；无匹配不得偷换目标）。</summary>
        public static string BuildPlanPrompt(string snapshotText, string command, string persona,
            string history, string intentTable, string grammarRules, string companionIntention = null,
            string resolvedTargetText = null, string worldSection = null, string targetCandidatesText = null,
            string detentionNote = null)
        {
            var sb = new StringBuilder();
            // 世界观段（blob 单段注入，2026-08-17：静态 flavor 退场；调用点传切片结果，null = 省略——
            // 标题+内容一起条件化，blob 空 → 整段省略防标题残留）
            if (!string.IsNullOrWhiteSpace(worldSection))
            {
                // 世界观段标题（XML LWN_plan_section_world，双语）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_section_world") + worldSection);
                sb.AppendLine();
            }
            // 当前场景段标题（XML LWN_plan_section_scene）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_section_scene"));
            // 场景快照为空时的兜底文案（XML LWN_plan_section_scene_empty）
            sb.AppendLine(string.IsNullOrEmpty(snapshotText) ? LWNTextHelper.ResolvePrompt("LWN_plan_section_scene_empty") : snapshotText);
            sb.AppendLine();
            // 身份段标题（XML LWN_plan_section_identity）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_section_identity"));
            // 默认随从人设（XML LWN_plan_identity_default）
            sb.AppendLine(string.IsNullOrEmpty(persona) ? LWNTextHelper.ResolvePrompt("LWN_plan_identity_default") : persona);
            sb.AppendLine();
            // 玩家命令段标题（XML LWN_plan_section_command）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_section_command"));
            // 命令为空时的兜底文案（XML LWN_plan_section_command_empty）
            sb.AppendLine(string.IsNullOrEmpty(command) ? LWNTextHelper.ResolvePrompt("LWN_plan_section_command_empty") : command);
            sb.AppendLine();
            // 🔴 2026-08-14（M4 风险审视 plan_needed）：【随从的打算】段——随从战术方向（risk_analysis
            // 第一人称）独立成段，不混入【命令】段（防"谁的命令"语义混淆）；标题 XML 单一事实源
            //（LWN_plan_section_companion_intention）。计划由计划轮 LLM 决定——随从的打算只是参考。
            if (!string.IsNullOrWhiteSpace(companionIntention))
            {
                // 本地化：【随从的打算】段标题（XML LWN_plan_section_companion_intention）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_section_companion_intention"));
                sb.AppendLine(companionIntention);
                sb.AppendLine();
            }
            // 🔴 2026-08-21（用户裁定：在押随从无法执行移动类操作）：【在押纪律】段——执行者在押时
            // 注入（ImCommandFlow 判定）。C# 执行器会中止移动步骤（move_to/follow/lead），prompt 先
            // 告知 LLM 别生成注定中止的计划：要么做不依赖移动的事（传话/望风/原地动作），要么诚实
            // 收尾 fail + report「我在牢里出不去」。
            if (!string.IsNullOrWhiteSpace(detentionNote))
            {
                sb.AppendLine(detentionNote);
                sb.AppendLine();
            }
            // 🔴 2026-08-15（目标唯一标记，用户裁定）：【目标指认】段——回复轮已把玩家的模糊说法
            //（"酒馆老板"）解析固定为场景实体的唯一标记（"酒馆店主#3"），计划 target 直接引用该标记，
            // 禁止再按玩家原话自行解析（失配风险归零）。非空才注入（普通密令无此段，零开销）。
            if (!string.IsNullOrWhiteSpace(resolvedTargetText))
            {
                // 本地化：【目标指认】段标题（XML LWN_plan_section_target_id）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_section_target_id"));
                sb.AppendLine($"玩家说的目标 = 场景里的 {resolvedTargetText}（计划里 target 直接写 {resolvedTargetText}，不要按玩家原话另找）"); // lwn-ignore: A
                sb.AppendLine();
            }
            // 🔴 2026-08-19（目标纪律）：命令对应多人/目标类型在场景无匹配 → 候选段注入
            //（LLM 必须 questions 让主公挑，禁止自行指定；无匹配不得偷换目标，诚实澄清或按物件计划）
            if (!string.IsNullOrWhiteSpace(targetCandidatesText))
            {
                // 本地化：目标候选段标题（XML LWN_plan_section_target_candidates，纪律文本随标题注入）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_section_target_candidates"));
                sb.AppendLine(targetCandidatesText);
                sb.AppendLine();
            }
            if (!string.IsNullOrEmpty(history))
            {
                // 澄清历史段标题（XML LWN_plan_section_history）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_section_history"));
                sb.AppendLine(history);
                sb.AppendLine();
            }
            // 意图分类段标题（XML LWN_plan_section_intent；意图列表为动态拼接段，不进 XML）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_section_intent"));
            sb.AppendLine(string.IsNullOrEmpty(intentTable) ? "DISTRACT/BRING/LOOKOUT/DELIVER/STEAL/ATTACK" : intentTable);
            sb.AppendLine();
            // 计划语法纪律 19 条（文本在 XML LWN_plan_rules，py/C# 同源——改 prompt 只改 XML）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_rules"));
            sb.AppendLine();
            if (!string.IsNullOrEmpty(grammarRules))
            {
                // 动作/谓词词表段标题（XML LWN_plan_section_grammar；词表内容为动态拼接段）
                sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_section_grammar"));
                sb.AppendLine(grammarRules);
                sb.AppendLine();
            }
            // 输出格式 BRING 完整模板（XML LWN_plan_template_bring）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_template_bring"));
            sb.AppendLine();
            // GUIDE 带路示范（XML LWN_plan_template_guide；lead 用法：先 move_to 目标，再 lead 等主公跟上）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_template_guide"));
            sb.AppendLine();
            // 批量目标 loop 段示范（XML LWN_plan_template_loop）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_template_loop"));
            sb.AppendLine();
            // 失败路径示范（XML LWN_plan_example_fail）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_example_fail"));
            sb.AppendLine();
            // 等对方回应示范（XML LWN_plan_example_respond）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_example_respond"));
            sb.AppendLine();
            // 保持型示范（XML LWN_plan_example_keep）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_example_keep"));
            sb.AppendLine();
            // 分头配合示范（XML LWN_plan_example_assist；2026-08-14 M6/M7：ask_help + steal_equipment 战术）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_example_assist"));
            sb.AppendLine();
            // 等机会询问主公示范（XML LWN_plan_example_ask；2026-08-15：ask_player 密信决策卡，
            // 等没人看超时 → 问主公撤还是硬来，禁止直接撤退）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_example_ask"));
            sb.AppendLine();
            // 判定型步骤示范（XML LWN_plan_example_result）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_example_result"));
            sb.AppendLine();
            // 输出质量要求 10 条（XML LWN_plan_quality）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_quality"));
            sb.AppendLine();
            // 执行要求 4 条（XML LWN_plan_exec）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_exec"));
            sb.AppendLine();
            // 计划陈述纪律（XML LWN_plan_narration_rule，双语；2026-08-10 im-command-action-upgrade.md §3.1）
            sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_narration_rule"));
            return sb.ToString();
        }
    }
}