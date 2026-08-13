using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 闲聊行动执行器（im-command-action-upgrade.md §5.2/§5.3/§六）：当面对话与 IM 共用一张动作注册表。
    /// 2026-08-10 升级：
    /// ① defender 双向化：核心效果 = attacker 表达的态度（NPC↔NPC 走官方 ApplyRelationChangeBetweenHeroes，
    ///    反编译确认；玩家侧走 ApplyPlayerRelation）；
    /// ② Spaces 位掩码按空间裁剪动作空间（LLM 只看到当前空间的合法动作，无空间概念）；
    /// ③ 物理行为类（ATTACK/EMOTE/FOLLOW/MOVE_TO/...）走单步 Plan 通道（ChatActionFlow →
    ///    PlanExecutor.TryCreateSubAction 既有分支），执行层零新代码；
    /// ④ 频率纪律：关系/声望/party 类每 60s 冷却（演出类/高风险类不参与）。
    /// 2026-08-13 重构：动作定义迁出至 ActionRegistry 主表（单一事实源），本类只保留
    /// 执行入口（HandleAction/HandleImAction）、运行时状态（冷却表）、空间裁决（ResolveSpace）、
    /// prompt 拼接（GetActionSpacePrompt）、决策播报（AnnounceDecision）。自 InteractionController.cs 拆出独立文件。
    /// </summary>
    public static class ActionHandler
    {
        // 动作定义单一事实源 = ActionRegistry 主表（34 行，action-registry-refactor.md）。
        // 本类只保留：执行入口（HandleAction/HandleImAction）、运行时状态（冷却表）、
        // 空间裁决（ResolveSpace）、prompt 拼接（GetActionSpacePrompt）、决策播报（AnnounceDecision）。

        // 频率纪律冷却表（§5.2）："attackerId→defenderId" → 上次执行墙钟秒
        private static readonly Dictionary<string, double> _actionCooldown = new Dictionary<string, double>();

        // ── 档位映射（LLM 只给档位，数值 C# 定——铁律 2）──

        /// <summary>关系档位 → 变化量（small=±3 / medium=±5 / large=±10）。</summary>
        internal static int LevelDelta(string level, int sign)
        {
            string lv = level?.ToLowerInvariant();
            int mag = lv == "small" ? 3 : (lv == "large" ? 10 : 5);
            return sign * mag;
        }

        // ── 频率纪律（§5.2）：同 attacker→同 defender 的关系/声望/party 类 action 冷却 ──

        private static bool IsCooledDown(string attackerId, string defenderId)
        {
            if (string.IsNullOrEmpty(attackerId) || string.IsNullOrEmpty(defenderId)) return true;
            double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            float cd = Settings.Instance.ChatActionCooldownSeconds;
            string key = attackerId + "→" + defenderId;
            bool cooled = _actionCooldown.TryGetValue(key, out double last) && now - last < cd;
            if (!cooled) _actionCooldown[key] = now;
            return !cooled;
        }

        /// <summary>
        /// 空间裁决（§5.2，C# 确定性）：由 defender 的空间关系决定动作空间。
        /// 复用既有轮子 ImChatManager.IsPresentInMission（Q3 已建）。
        /// </summary>
        public static ActionSpace ResolveSpace(Hero defender)
        {
            if (Mission.Current == null) return ActionSpace.Party;   // 玩家在 Campaign：部队动作为主
            if (defender != null && ImChatManager.IsPresentInMission(defender.StringId))
                return ActionSpace.InScene;                          // 随从/在场 NPC：场景内可执行
            return ActionSpace.ImRemote;                             // 不在场：远距语义
        }

        /// <summary>
        /// 获取当前空间可用的动作空间 Prompt（当面对话 LLM 与 IM 回复共用；按空间裁剪，LLM 无空间概念）。
        /// </summary>
        public static string GetActionSpacePrompt(Hero attacker, Hero defender, Agent agent)
        {
            var space = ResolveSpace(defender);
            StringBuilder sb = new StringBuilder();
            // 标题/纪律段是增强（缺 key 返回空串不崩，铁律 1）
            string title = LWNTextHelper.ResolvePrompt("LWN_im_action_space_title");
            if (!string.IsNullOrEmpty(title)) sb.AppendLine(title);
            foreach (var action in ActionRegistry.ChatActions)
            {
                if (action == null || action.Code == "NONE") continue;
                if ((action.Spaces & space) == 0) continue;   // 空间裁剪
                // 🔴 资格裁剪（2026-08-11 修）：party 动作（部队巡逻/集结）注入前过 IsValid——
                // 招募同伴 Clan==PlayerClan 但 PartyBelongedTo==玩家 party（无独立部队），旧注入直接给 LLM
                //（实机日志 09:58:55 游民同伴被注入 PARTY_PATROL/GATHER_TO_PLAYER）；资格不符不再进 prompt。
                if ((action.Spaces & ActionSpace.Party) != 0 && !action.IsValid(attacker, defender, agent)) continue;
                sb.AppendLine($"- \"{action.Code}\": {action.Description}");
            }
            // 动作空间纪律段（LLM 输入）
            string rule = LWNTextHelper.ResolvePrompt("LWN_im_action_rule");
            if (!string.IsNullOrEmpty(rule)) sb.AppendLine(rule);
            return sb.ToString();
        }

        /// <summary>
        /// 🔴 2026-08-13：LLM 决策播报——动作真正落地前（IsValid 通过）向玩家 DisplayMessage：
        /// 「谁决定要干嘛 + 参数」（目标/档位/金额/表情 key）。铁律 13：全部走 LWN_* 本地化。
        /// 纪律：① 只播实际执行的动作（IsValid 之后调用，降级 NONE 不播）；② NONE 不播；
        /// ③ alreadyConfirmed（IM 卡片已批准）不播——卡片本身就是决策展示，不双报。
        /// 动作标签复用 ImCommandFlow.PlanActionLabel（LWN_plan_action_* 同表，防两份标签漂移）。
        /// </summary>
        private static void AnnounceDecision(ActionRegistry.ActionSpec actionDef, Hero attacker, Hero defender,
            Agent agent, string level, string targetText)
        {
            try
            {
                if (actionDef == null || string.IsNullOrEmpty(actionDef.Code) || actionDef.Code == "NONE") return;
                string name = attacker?.Name?.ToString() ?? agent?.Name?.ToString() ?? "";
                if (string.IsNullOrEmpty(name)) return;
                // 标签取统一小写码（Code 已小写，无需 ToLowerInvariant）
                string actionLabel = ImCommandFlow.PlanActionLabel(actionDef.Code);
                // 目标：LLM 目标文本优先，否则用解析出的 defender（私聊语境下 LLM 常省略目标）
                string target = !string.IsNullOrWhiteSpace(targetText) ? targetText
                    : (defender != null ? defender.Name?.ToString() : null);
                // 参数：金额（give_gold）/ 档位词（关系类）/ 动画 key（emote），C# 确定（铁律 2）
                string param = actionDef.AnnounceParam?.Invoke(level);

                string msg;
                if (!string.IsNullOrEmpty(target) && !string.IsNullOrEmpty(param))
                    // 播报：谁决定要干嘛 + 目标 + 参数
                    msg = LWNTextHelper.ResolveCompound("LWN_action_decide_target_param",
                        ("NAME", name), ("ACTION", actionLabel), ("TARGET", target), ("PARAM", param));
                else if (!string.IsNullOrEmpty(target))
                    // 播报：谁决定要干嘛 + 目标
                    msg = LWNTextHelper.ResolveCompound("LWN_action_decide_target",
                        ("NAME", name), ("ACTION", actionLabel), ("TARGET", target));
                else if (!string.IsNullOrEmpty(param))
                    // 播报：谁决定要干嘛 + 参数
                    msg = LWNTextHelper.ResolveCompound("LWN_action_decide_param",
                        ("NAME", name), ("ACTION", actionLabel), ("PARAM", param));
                else
                    // 播报：谁决定要干嘛（无目标无参数）
                    msg = LWNTextHelper.ResolveCompound("LWN_action_decide",
                        ("NAME", name), ("ACTION", actionLabel));

                InformationManager.DisplayMessage(new InformationMessage(msg));
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ActionHandler] 决策播报失败: {ex.Message}");
            }
        }

        /// <summary>档位词（small/medium/large）→ 本地化词（LWN_action_level_*）。</summary>
        internal static string LevelWord(string level)
        {
            string lv = level?.ToLowerInvariant();
            if (lv == "small") return LWNTextHelper.ResolveText("LWN_action_level_small", "small");
            if (lv == "large") return LWNTextHelper.ResolveText("LWN_action_level_large", "large");
            return LWNTextHelper.ResolveText("LWN_action_level_medium", "medium");
        }

        /// <summary>
        /// 执行动作（当面对话 + IM 共用入口）。
        /// 流程：空间裁剪（Spaces 位掩码）→ 频率冷却（关系/声望/party 类）→ IsValid → Execute。
        /// 🔴 2026-08-11：alreadyConfirmed=true（IM 卡片批准后的再执行）→ 直接跑 ExecuteCore
        /// （RequiresConfirm 动作的核心逻辑），不再弹原生确认窗（确认已在卡片上完成）。
        /// </summary>
        public static void HandleAction(string actionCode, Hero attacker, Hero defender, Agent agent,
            string level = null, string targetText = null, string sayText = null, bool alreadyConfirmed = false)
        {
            if (string.IsNullOrEmpty(actionCode)) return;
            // FindByCode（OrdinalIgnoreCase：兼容旧存档/旧词表大写码 "ATTACK"/"MOVE_TO" 等）
            var actionDef = ActionRegistry.FindByCode(actionCode);
            if (actionDef == null)
            {
                DebugLogger.Log($"[ActionHandler] 未知动作代码: {actionCode} → 降级 NONE");
                return;
            }
            // 空间裁剪（§5.2）：动作空间不含当前空间 → 降级 NONE（LLM 硬选场景外动作，IsValid 兜底前再拦一层）
            var space = ResolveSpace(defender);
            if ((actionDef.Spaces & space) == 0)
            {
                DebugLogger.Log($"[ActionHandler] 动作 {actionCode} 不适用于空间 {space} → 降级 NONE");
                return;
            }
            // 频率纪律（§5.2）：关系/声望/party 类同对冷却；演出类/高风险类不参与
            if (actionDef.NeedsCooldown)
            {
                string ak = attacker?.StringId ?? "";
                string dk = defender?.StringId ?? "";
                if (!IsCooledDown(ak, dk))
                {
                    DebugLogger.Log($"[ActionHandler] 动作 {actionCode} 冷却中（{ak}→{dk}）→ 降级 NONE");
                    return;
                }
            }
            if (actionDef.IsValid(attacker, defender, agent))
            {
                // 🔴 2026-08-13：决策播报（谁决定要干嘛+参数）；已确认卡片路径不重复播（卡片即决策展示）
                if (!alreadyConfirmed)
                    AnnounceDecision(actionDef, attacker, defender, agent, level, targetText);
                // 已确认路径（IM 卡片批准）：直接执行核心逻辑；缺 ExecuteCore（普通动作）→ 回退 Execute
                if (alreadyConfirmed && actionDef.ExecuteCore != null)
                    actionDef.ExecuteCore(attacker, defender, agent, level, targetText, sayText);
                else
                    actionDef.Execute(attacker, defender, agent, level, targetText, sayText);
            }
            else
            {
                DebugLogger.Log($"[ActionHandler] 动作 {actionCode} 条件不满足 → 降级 NONE");
            }
        }

        /// <summary>
        /// 🔴 2026-08-11：按动作码执行核心逻辑（ExecuteCore，缺省回退 Execute）——
        /// 当面对话弹窗的确认回调复用（Execute 包装弹窗，回调时跑核心，避免两份逻辑漂移）。
        /// 仅在弹窗回调运行期调用（此时 ActionRegistry 已完整构建），IM 卡片批准路径走
        /// HandleAction(alreadyConfirmed:true) 直接 ExecuteCore，不经本方法。
        /// </summary>
        internal static void RunActionCore(string code, Hero attacker, Hero defender, Agent agent,
            string level, string targetText, string sayText)
        {
            // FindByCode（OrdinalIgnoreCase：兼容旧大写码）
            var def = ActionRegistry.FindByCode(code);
            (def?.ExecuteCore ?? def?.Execute)?.Invoke(attacker, defender, agent, level, targetText, sayText);
        }

        /// <summary>RequiresConfirm 动作的当面对话确认弹窗（title=InquiryTitleKey，msg=InquiryMsgKey，按钮恒 fight）。
        /// 2026-08-13 抽自主表四行 Execute 的重复弹窗构造（order_attack/duel/knockout/steal_attempt）。</summary>
        internal static void ConfirmDialog(string titleKey, string titleFallback, string msgKey, string targetName, Action confirm)
        {
            InformationManager.ShowInquiry(new InquiryData(
                LWNTextHelper.ResolveText("LWN_ui_interact_inquiry_" + titleKey, titleFallback),
                LWNTextHelper.ResolveCompound("LWN_ui_interact_inquiry_" + msgKey + "_msg", ("NAME", targetName)),
                true, false, LWNTextHelper.ResolveText("LWN_ui_interact_btn_fight", "Come and fight!"), null, confirm, null));
        }

        /// <summary>记忆写入（C2 记忆类动作用）：defender/attacker 的恩怨记录（后续对话 LLM 上下文接得住）。
        /// role = "user"（对方言行）；模板 NPC 走 TEMP 记忆兜底（GetMemoryForAgent）。</summary>
        internal static void WriteMemory(Hero hero, string role, string content, string speakerId)
        {
            if (hero == null || string.IsNullOrWhiteSpace(content)) return;
            try
            {
                var memory = AllNpcMemoryManager.GetMemory(hero.StringId)
                    ?? AllNpcMemoryManager.GetMemoryForAgent(null);
                if (memory != null) memory.AddHistory(role, content, string.IsNullOrEmpty(speakerId) ? hero.StringId : speakerId);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ActionHandler] 记忆写入失败 {hero.Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// IM 闲聊回复动作投递（§5.1/§5.2）：attacker = 回复的 NPC（IM 中说话者），
        /// defender 解析（§四优先级：名字文本 → 群聊成员/私聊对方/世界 Hero → 兜底玩家）。
        /// agent = attacker 的物理载体（InScene 动作执行者；不在场 = null → 物理动作 IsValid 拦截）。
        /// sayText = IM 回复正文（记忆类动作的台词来源：威胁/承诺的具体内容）。
        /// 🔴 2026-08-11：RequiresConfirm 动作 → 拦截为 Proposal 卡片（PostActionProposal）；
        /// bypassConfirm=true = 玩家已批准卡片的再执行（HandleProposal 调用），直接执行不再拦截。
        /// </summary>
        public static void HandleImAction(string actionCode, string attackerHeroId, string attackerName,
            string targetText, string level, ImConversation conv, string sayText = null, bool bypassConfirm = false)
        {
            if (string.IsNullOrEmpty(actionCode) || actionCode == "NONE") return;

            // attacker 解析（IM 回复者必须有 Hero——IM 侧天然全 Hero，模板 NPC 不进 IM）
            Hero attacker = null;
            try { attacker = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == attackerHeroId); } catch { }
            if (attacker == null)
            {
                DebugLogger.Log($"[ActionHandler] IM 动作 {actionCode} 的 attacker 无 Hero（{attackerHeroId}）→ 降级 NONE");
                return;
            }

            // defender 解析：目标名字文本（长度≥2 防单字误伤）→ 群聊成员/私聊对象/世界 Hero → 兜底玩家
            Hero defender = ResolveImDefender(attacker, targetText, conv);
            // agent = attacker 的物理载体
            Agent agent = FindAgentByHeroId(attackerHeroId);

            // 🔴 2026-08-11（IM 闲聊动作 → 提议卡片）：高风险动作（RequiresConfirm）不弹原生确认窗，
            // 改为在当前会话投递 Proposal 卡片（同意/拒绝）——与密令/NPC 主动提议同一套确认 UI。
            // 当面对话路径（ReactiveAgent → HandleAction 直接）不受影响，仍走原生弹窗。
            // bypassConfirm=true（玩家已批准卡片的再执行）→ 跳过拦截直接执行，防死循环。
            // FindByCode（OrdinalIgnoreCase：兼容旧存档大写码）
            var actionDef = ActionRegistry.FindByCode(actionCode);
            if (actionDef != null && actionDef.RequiresConfirm && !bypassConfirm)
            {
                PostActionProposal(conv, attacker, attackerName, defender, actionDef, actionCode, targetText, level, agent);
                return;
            }

            HandleAction(actionCode, attacker, defender, agent, level, targetText, sayText, alreadyConfirmed: bypassConfirm);
        }

        /// <summary>
        /// 🔴 2026-08-11：IM 高风险动作 → 提议卡片（RequiresConfirm 动作专用）。
        /// 卡片文案复用各动作的确认弹窗本地化 key（零新增）；玩家同意 → ImChatView.HandleProposal
        /// 调回 HandleImAction 重新执行（空间/冷却/IsValid 全保留，NPC 已离场自然降级）；拒绝 → 卡片了结。
        /// 投递前预检（空间裁剪 + IsValid）：当前不可用则不发卡（避免"同意后无法执行"的死卡），
        /// 与 HandleAction 的降级 NONE 同语义，玩家在频道里只看到台词、看不到无效动作。
        /// </summary>
        private static void PostActionProposal(ImConversation conv, Hero attacker, string attackerName, Hero defender,
            ActionRegistry.ActionSpec actionDef, string actionCode, string targetText, string level, Agent agent)
        {
            try
            {
                if (conv == null || attacker == null) return;
                var space = ResolveSpace(defender);
                if ((actionDef.Spaces & space) == 0 || !actionDef.IsValid(attacker, defender, agent))
                {
                    DebugLogger.Log($"[ActionProposal] 动作 {actionCode} 当前不可用（空间/条件不满足）→ 不发卡");
                    return;
                }
                string targetName = defender?.Name?.ToString() ?? targetText;
                // 🔴 2026-08-13：卡片文案按目标分流——原 key（"{NAME} 想与你…"）是当面对话
                // 弹窗文案，玩家视角"你"= 玩家；群聊里 defender 是另一个 NPC 时语义全错
                //（日志实锤：斯唐纳夫要对阿速甘出手，卡片却显示"阿速甘 想与你切磋"，
                // 玩家以为冲自己来的）。规则：
                //   defender=玩家 → 原 key，NAME=attacker 名（"「斯唐纳夫」想与你切磋"）
                //   defender≠玩家 → *_npc 变体，NAME=attacker + TARGET=defender（"斯唐纳夫想与阿速甘切磋"）
                // 文案变量顺序：先 NAME（谁要做）后 TARGET（对谁做）。
                string content = targetText;
                if (actionDef.InquiryMsgKey != null)
                {
                    bool targetIsPlayer = defender == null || defender == Hero.MainHero;
                    string key = "LWN_ui_interact_inquiry_" + actionDef.InquiryMsgKey
                        + (targetIsPlayer ? "" : "_npc") + "_msg";
                    content = targetIsPlayer
                        ? LWNTextHelper.ResolveCompound(key, ("NAME", attackerName))
                        : LWNTextHelper.ResolveCompound(key, ("NAME", attackerName), ("TARGET", targetName));
                }
                // 🔴 2026-08-13：同会话去重——主回复者与跟随者常对同一对双方各发一张同动作卡
                //（互为镜像：A→B 与 B→A），玩家看到两张卡、点两次 → StartFight 二次触发
                //（侧模型 _sideFightCount 叠加，EndFight 一次不归零，队伍还原泄漏）。
                // 规则：已有未决同动作卡且其发送者 == 本次 defender（镜像）→ 丢弃本卡，
                // 玩家拒绝一张 = 整场切磋取消，语义一致。
                if (HasPendingMirrorProposal(conv, attacker, defender, actionCode))
                {
                    DebugLogger.Log($"[ActionProposal] 丢弃重复提议: {attackerName} {actionCode}→{targetName}（已有未决同动作镜像卡）");
                    return;
                }
                var msg = new ImMessage(attacker.StringId, attackerName, content, ImMessageKind.Proposal)
                {
                    ConvId = conv.Id,
                    ActionCode = actionCode,
                    ActionTarget = targetText,
                    ActionLevel = level,
                };
                ImChatStore.AppendGroupMessage(conv.Id, msg);
                ImChatStore.IncUnread(conv.Id);
                ImChatManager.BroadcastMessageArrived(conv);
                DebugLogger.Log($"[ActionProposal] {attackerName} 提议 {actionCode}（目标 {targetName}）→ 玩家确认");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ActionProposal] 提议卡片投递失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 同会话镜像卡去重（2026-08-13）：检查本 conv 是否已有「未决 + 同动作码 + 发送者 == 本次目标」
        /// 的提议卡。A→B 发卡后 B 回包同动作 → 镜像 → 丢弃；同一 NPC 连发同动作卡也命中。
        /// 已了结（批准/拒绝）的卡不算——玩家拒绝后再提同动作允许重发。
        /// </summary>
        private static bool HasPendingMirrorProposal(ImConversation conv, Hero attacker, Hero defender, string actionCode)
        {
            if (conv == null || defender == null) return false;
            if (conv.Type == ImConversationType.Direct) return false;   // 私聊单方，无镜像可能
            List<ImMessage> msgs;
            try { msgs = ImChatManager.GetMessages(conv); } catch { return false; }
            if (msgs == null) return false;
            foreach (var m in msgs)
            {
                if (m == null || !m.IsProposal || m.IsProposalResolved) continue;
                if (m.ActionCode != actionCode) continue;
                if (m.SenderHeroId == defender.StringId) return true;
            }
            return false;
        }

        /// <summary>IM 动作 defender 解析（§四优先级：名字文本 → 群聊成员候选匹配 → 私聊对象 → 世界 Hero；兜底玩家）。
        /// 排除说话者自己（attacker 不能对自己用动作）。</summary>
        private static Hero ResolveImDefender(Hero attacker, string targetText, ImConversation conv)
        {
            string t = targetText?.Trim();
            if (!string.IsNullOrEmpty(t) && t.Length >= 2)
            {
                try
                {
                    // 1) 玩家自己
                    if (Hero.MainHero != null && NameMatchesHero(Hero.MainHero, t)) return Hero.MainHero;
                    // 2) 群聊成员候选匹配（@提及语义：成员名/称号/FirstName 优先命中）
                    if (conv != null && conv.Type != ImConversationType.Direct)
                    {
                        var member = FindChannelMemberMatching(conv, t);
                        if (member != null) return member;
                    }
                    // 3) 私聊对象（私聊里说对方名字 → 对方）
                    if (conv != null && conv.Type == ImConversationType.Direct)
                    {
                        var partner = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == conv.PartnerHeroId);
                        if (partner != null && NameMatchesHero(partner, t)) return partner;
                    }
                    // 4) 世界 Hero（名字/FirstName 匹配，排除说话者自己）
                    foreach (var h in Hero.AllAliveHeroes)
                    {
                        if (h == attacker) continue;
                        if (NameMatchesHero(h, t)) return h;
                    }
                }
                catch { }
            }
            // 兜底：默认玩家（消息接收者）
            return Hero.MainHero;
        }

        /// <summary>群聊成员名字匹配（@提及候选：全名/去引号全名/引号内称号/FirstName——ImTopicMatcher 同款候选集）。</summary>
        private static Hero FindChannelMemberMatching(ImConversation conv, string text)
        {
            var members = ImChatManager.GetChannelMembers(conv.Type);
            foreach (var h in members)
            {
                if (h == null) continue;
                if (NameMatchesHero(h, text)) return h;
                // 引号内称号（「百草药僧」斯唐纳夫 → 称号匹配）
                string name = h.Name?.ToString() ?? "";
                int q1 = name.IndexOf('“'); int q2 = name.IndexOf('”');
                if (q1 >= 0 && q2 > q1)
                {
                    string title = name.Substring(q1 + 1, q2 - q1 - 1);
                    if (title.Equals(text, StringComparison.OrdinalIgnoreCase) || title.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                        return h;
                }
                if (h.Name != null && h.Name.ToString() == text) return h;
            }
            return null;
        }

        private static bool NameMatchesHero(Hero hero, string text)
        {
            if (hero == null || string.IsNullOrEmpty(text)) return false;
            try
            {
                string name = hero.Name?.ToString() ?? "";
                if (name.Equals(text, StringComparison.OrdinalIgnoreCase)) return true;
                if (name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                // 🔴 2026-08-13：去引号全名匹配——LLM 回包 action_target 常省略引号
                //（"求知客阿速甘" vs 全名"“求知客”阿速甘"）：全名含引号字符，子串 IndexOf 失败
                // → defender 解析兜底到玩家（实机：切磋目标变努勒丹，斯唐纳夫拔刀砍玩家）。
                // ImTopicMatcher.GetMentionCandidates 2026-08-10 已修同款问题，此处补上。
                string clean = name.Replace("“", "").Replace("”", "").Replace("\"", "");
                if (clean.Length >= 2 && clean.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                string first = hero.FirstName?.ToString() ?? "";
                return !string.IsNullOrEmpty(first) && first.Equals(text, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        internal static Agent FindAgentByHeroId(string heroId)
        {
            if (string.IsNullOrEmpty(heroId) || Mission.Current == null) return null;
            foreach (var a in Mission.Current.Agents)
            {
                var hero = (a.Character as CharacterObject)?.HeroObject;
                if (hero != null && hero.StringId == heroId) return a;
            }
            return null;
        }
    }
}
