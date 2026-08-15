using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 闲聊动作空间位掩码（im-command-action-upgrade.md §5.2）：
    /// 动作空间不由「IM 还是当面对话」决定，而由 attacker 与 defender 的空间关系决定（C# 确定性裁决，
    /// 不交 LLM）。同一句 IM 消息，对方在不在场、玩家在不在大地图，LLM 能选的动作完全不同。
    /// 2026-08-13 重构：自 InteractionController.cs 随动作系统团聚迁入本文件（ActionSpace = 动作域概念）。
    /// 🔴 2026-08-13（用户裁定：空间 = 执行人与目标双方在不在 Mission 内）：
    ///   InScene = 双方都在 Mission 内（场景动作：走位/物理/当面仪式——目标在不在跟前由动作
    ///             自身语义/IsValid 判断，如 move_to 走过去即可，不进空间位掩码）
    ///   Remote  = 一人在 Mission 内、一人在 Mission 外（跨场景远程语义：关系/声望/记忆/传话——
    ///             物理动作对"不在场景的目标"无法执行，天然降级）
    ///   Party   = 双方都在 Mission 外（Campaign 大地图：部队动作）
    /// 裁决输入是执行人 attacker 与目标 defender 双方，不是玩家（Mission.Current 只代表玩家在不在场景，
    /// 执行人/目标可能没进场景——如玩家在场景里 IM 一个没进场景的随从）。
    /// </summary>
    [Flags]
    public enum ActionSpace
    {
        InScene = 1,    // 执行人 + 目标 都在 Mission 内：场景动作
        Remote = 2,     // 一人在 Mission 内、一人在 Mission 外：远程语义（关系/声望/记忆/传话）
        Party = 4,      // 执行人 + 目标 都在 Mission 外（Campaign 大地图）：部队动作为主
    }

    /// <summary>
    /// 🔴 动作注册单一事实源（action-registry-refactor.md，2026-08-13）：
    /// 策划只维护这一张主表（36 行），其余全部派生——计划词表（PlanVocab）、闲聊动作空间
    /// （ActionHandler）、标签表（PlanActionLabel）、单步参数填充（ChatActionFlow）、
    /// 校验脚本（check_vocab_sync.py）都只读本表。
    ///
    /// 单码统一：Code = 统一小写码（计划词表与闲聊动作空间共用）。旧存档的大写码
    /// （ImMessage.ActionCode="MOVE_TO" 等）由 ByCode 的 OrdinalIgnoreCase 查询天然兼容。
    /// "NONE" 是空操作哨兵（大写保留，不进任何 prompt，三处跳过判据原样有效）。
    ///
    /// 行语义：InPlanVocab = 进计划词表（23 行，序 = 原 ActionsInPromptOrder 手写序，
    /// 82% LLM 回归基线依赖此顺序；2026-08-14 末尾追加 ask_help/steal_equipment）；InChatSpace = 进闲聊动作空间
    ///（29 行，ChatOrder 钉死 1..29 = 闲聊 prompt 展示序）。16 个交集动作双 true；
    /// 7 个仅计划（lead/wait/give_item/deliver_item/shadow/negotiate/end_plan）+ 2 个战术新动作
    ///（ask_help/steal_equipment）；15 个仅闲聊（含 crouch/stand 瞬时姿态动作）。
    ///
    /// 执行职责边界：Execute 委托对 agent 载体动作 = 闲聊侧点火（包装单步 Plan 走
    /// ChatActionFlow → PlanExecutor 既有分支），行为语义仍归执行器；对 hero/party 载体
    /// 动作 = 行为实现。RequiresConfirm 动作的 ExecuteCore = 卡片批准后的核心执行。
    /// </summary>
    public static class ActionRegistry
    {
        /// <summary>动作主表行。</summary>
        public sealed class ActionSpec
        {
            public string Code;                          // 统一小写码（计划词表与闲聊动作空间共用）
            public string Description;                   // 闲聊 prompt 描述（LLM 决定用）
            public string LabelKey;                      // 标签本地化 key 后缀（LWN_plan_action_<LabelKey>）
            public string LabelFallback;                 // 标签英文 fallback
            public bool InPlanVocab;                     // 进计划词表（ActionsInPromptOrder 派生源）
            public bool InChatSpace;                     // 进闲聊动作空间（GetActionSpacePrompt）
            public int ChatOrder;                        // 闲聊 prompt 展示序（1..27；0 = 不进闲聊空间）
            public ActionSpace Spaces = ActionSpace.InScene | ActionSpace.Remote | ActionSpace.Party;   // 空间位掩码（§5.2；默认全空间，仅计划动作不参与闲聊裁剪）
            public bool NeedsCooldown;                   // 频率纪律：关系/声望/party 类 60s 冷却
            public bool RequiresConfirm;                 // 高风险物理动作：IM 路径拦截为提议卡片
            public string InquiryTitleKey;               // 确认弹窗/卡片标题 key（LWN_ui_interact_inquiry_<key>）
            public string InquiryMsgKey;                 // 确认弹窗/卡片内容 key（LWN_ui_interact_inquiry_<key>_msg）
            public string[] Aliases;                     // 计划侧 LLM 容错别名（attack→order_attack 等，校验时规范为正码）
            public HashSet<string> ResultKeys;           // 判定型/结算型动作的合法 result 键
            public bool IsTerminal;                      // end_plan（收尾，无跳转消费）
            public bool SelfTargeted;                    // 自身状态切换：无 defender 目标语义（蹲下/站起）——跳过目标解析与播报，空间只看执行人
            // 🔴 默认 true：绝大多数动作执行器已实现，仅 shadow/negotiate/duel 显式设 false。
            // 勿改回默认 false——bool 默认值是 false，34 行未显式赋值的行会全部变成"未实现"，
            // 静态构造自检直接炸（实机 2026-08-13 崩溃实录）。
            public bool ExecutorImplemented = true;      // 计划侧执行器已实现（shadow/negotiate/duel=false → 步骤失败）
            public Func<Hero, Hero, Agent, bool> IsValid;      // 前置条件：(attacker, defender, agent)
            public Action<Hero, Hero, Agent, string, string, string> Execute;       // 闲聊入口点火 / hero/party 行为实现
            // 🔴 2026-08-13：7 参（尾加 Agent explicitTarget）——模板 NPC 目标（无 Hero）由玩家选定后
            // 直达执行器（RoleAgents["target"]），不靠名字模糊匹配。旧调用点（当面对话弹窗）传 null。
            public Action<Hero, Hero, Agent, string, string, string, Agent> ExecuteCore;   // RequiresConfirm 动作卡片批准后的核心执行
            public Action<PlanStep, string, string> FillParams;    // 单步 Plan 参数填充（ChatActionFlow，C# 确定）
            public Func<string, string> AnnounceParam;            // 决策播报参数（AnnounceDecision）
        }
        // ─────────────────────────────────────────────────────────────
        // 主表 34 行：前 21 行 = 计划词表原序（严格按原 ActionsInPromptOrder 抄，82% 基线）；
        // 后 13 行 = 闲聊-only。14 个交集动作（计划码 + 旧闲聊大写码双身份）已合并为一行。
        // Execute/IsValid/ExecuteCore 正文自 InteractionController.cs InitializeActions 逐字搬运
        //（lambda 形参名保留原样防手滑）。行为语义归执行器（PlanExecutor），本表只接线。
        // ─────────────────────────────────────────────────────────────
        public static readonly ActionSpec[] All =
        {
            // ── 交集 14 行（计划序 1..20，闲聊 ChatOrder 钉死）──
            // 1. move_to（原 MOVE_TO；闲聊 ChatOrder=23）
            // 🔴 2026-08-13（空间修复 + 模型重构）：Mission 内一律 InScene 可执行——move_to 核心语义
            // 就是「走到目标身边」，远处目标走过去即可（实机日志：LLM 回 move_to 去找 67 米外的那弥斯
            // → 旧 ImRemote 空间拦截「不适用于空间 ImRemote → 降级 NONE」→ NPC 口头答应但不动）。
            new ActionSpec
            {
                Code = "move_to",
                Description = "走到对方身边/某个地方（当面或远处目标均可）。",
                InPlanVocab = true, InChatSpace = true, ChatOrder = 23,
                Spaces = ActionSpace.InScene,
                Aliases = new[] { "move" },
                LabelKey = "move_to", LabelFallback = "move to",
                IsValid = (npc, player, agent) => agent != null,
                Execute = (attacker, defender, agent, l, t, s) =>
                {
                    // target 文本 → C# 解析（TryResolvePosition 链：agent 名 → 语义 tag zone）
                    string name = !string.IsNullOrWhiteSpace(t) ? t
                        : (defender != null ? defender.Name.ToString() : null);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        DebugLogger.Log($"[ActionHandler] MOVE_TO 无目标文本 → 降级 NONE");
                        return;
                    }
                    ChatActionFlow.TryExecute(agent, "move_to", name, null, null);
                }
            },
            // 2. follow（原 FOLLOW；闲聊 ChatOrder=19；无限保持）
            // 🔴 2026-08-13（空间修复 + 模型重构）：与 move_to 同——目标不在跟前时先走过去再保持跟随
            new ActionSpec
            {
                Code = "follow",
                Description = "跟到对方身边（保持跟随，直到对方离开；当面或远处目标均可）。",
                InPlanVocab = true, InChatSpace = true, ChatOrder = 19,
                Spaces = ActionSpace.InScene,
                LabelKey = "follow", LabelFallback = "follow",
                IsValid = (npc, player, agent) => agent != null,
                FillParams = (step, level, sayText) => step.TimeoutS = 0f,   // 无限保持（与密令 follow 省略 timeout 同语义）
                Execute = (attacker, defender, agent, l, t, s) =>
                {
                    string name = defender != null ? defender.Name.ToString() : t;
                    ChatActionFlow.TryExecute(agent, "follow", name, null, null);
                }
            },
            // 3. stop_following（原 STOP_FOLLOWING；闲聊 ChatOrder=20）
            new ActionSpec
            {
                Code = "stop_following",
                Description = "停止跟随对方（仅当面）。",
                InPlanVocab = true, InChatSpace = true, ChatOrder = 20,
                Spaces = ActionSpace.InScene,
                Aliases = new[] { "stop" },
                LabelKey = "stop_following", LabelFallback = "stop following",
                IsValid = (npc, player, agent) => agent != null,
                Execute = (attacker, defender, agent, l, t, s) => ChatActionFlow.TryExecute(agent, "stop_following", null, null, null)
            },
            // 4. order_attack（原 ATTACK；闲聊 ChatOrder=12；别名 attack 承载旧计划缩写）
            new ActionSpec
            {
                Code = "order_attack",
                Description = "要求对方战斗，发起攻击（进入战斗；仅当面）。",
                InPlanVocab = true, InChatSpace = true, ChatOrder = 12,
                Spaces = ActionSpace.InScene,
                RequiresConfirm = true,   // 高风险：IM 路径走提议卡片
                InquiryTitleKey = "danger", InquiryMsgKey = "attack",
                Aliases = new[] { "attack" },
                LabelKey = "order_attack", LabelFallback = "attack",
                IsValid = (npc, player, agent) => agent != null,
                // 核心执行（IM 卡片批准后直接跑；当面对话走 Execute 的弹窗包装）。
                // 🔴 2026-08-11 用户裁定：发 order_attack 事件 → AgentBrain 既有战斗链
                //（ClearAllActions → FightEnemyAction，儿童逃离，CombatManager 队伍管理），
                // 与当面对话拔刀（SendEventToAgent(NPC, order_attack, 玩家)）同构；
                // 不走单步 Plan——战斗是持续行为，由 Brain 管理生命周期，执行器不该介入。
                // ⚠️ AIEvent 事件名 "order_attack" 是 Brain 层协议（AgentBrain.cs:387 白名单），
                // 不等于本行动作码，此处只是桥接注释，不注册。
                ExecuteCore = (attacker, defender, agent, l, t, s, explicitTarget) =>
                {
                    // 🔴 2026-08-13：explicitTarget = 模板 NPC 目标（玩家选定后直达，不走 Hero 解析）
                    Agent target = explicitTarget;
                    if (target == null && defender != null && defender != Hero.MainHero)
                        target = ActionHandler.FindAgentByHeroId(defender.StringId);
                    if (target == null) target = Agent.Main;   // 兜底（InScene 空间前提 = defender 在场）
                    if (agent != null && target != null && agent.IsActive() && target.IsActive())
                        AgentAIController.Instance?.SendEventToAgent(agent, "order_attack", target);
                },
                Execute = (attacker, defender, agent, l, t, s) =>
                {
                    string targetName = defender != null ? defender.Name.ToString() : (agent != null ? agent.Name.ToString() : "");
                    Action confirmFight = () =>
                    {
                        // 核心执行已抽到 ExecuteCore（IM 卡片路径复用，避免二次确认弹窗）
                        ActionHandler.RunActionCore("order_attack", attacker, defender, agent, l, t, s);
                    };
                    // 本地化：攻击确认弹窗（标题/内容/按钮）
                    InformationManager.ShowInquiry(new InquiryData(LWNTextHelper.ResolveText("LWN_ui_interact_inquiry_danger", "Danger"), LWNTextHelper.ResolveCompound("LWN_ui_interact_inquiry_attack_msg", ("NAME", targetName)), true, false, LWNTextHelper.ResolveText("LWN_ui_interact_btn_fight", "Come and fight!"), null, confirmFight, null));
                }
            },
            // 5. knockout（原 KNOCKOUT；闲聊 ChatOrder=14）
            new ActionSpec
            {
                Code = "knockout",
                Description = "背后击晕对方（仅当面；高风险）。",
                InPlanVocab = true, InChatSpace = true, ChatOrder = 14,
                Spaces = ActionSpace.InScene,
                RequiresConfirm = true,   // 高风险：IM 路径走提议卡片
                InquiryTitleKey = "danger", InquiryMsgKey = "knockout",
                LabelKey = "knockout", LabelFallback = "knock out",
                IsValid = (npc, player, agent) => agent != null,
                // 核心执行（IM 卡片批准后直接跑；当面对话走 Execute 的弹窗包装）
                ExecuteCore = (attacker, defender, agent, l, t, s, explicitTarget) =>
                {
                    // 🔴 2026-08-13：目标文本优先（模板 NPC 名/玩家选定候选），缺省回退 defender 名——
                    // 修复 LLM action_target=帝国新兵 但 defender 兜底成玩家 → 执行打玩家的丢失（实机 16:49）。
                    string targetName = explicitTarget != null ? "target"
                        : (!string.IsNullOrWhiteSpace(t) ? t
                            : (defender != null ? defender.Name.ToString() : (agent != null ? agent.Name.ToString() : "")));
                    ChatActionFlow.TryExecute(agent, "knockout", targetName, null, null, explicitTarget);
                },
                Execute = (attacker, defender, agent, l, t, s) =>
                {
                    string targetName = defender != null ? defender.Name.ToString() : (agent != null ? agent.Name.ToString() : "");
                    Action confirm = () => ActionHandler.RunActionCore("knockout", attacker, defender, agent, l, t, s);
                    // 本地化：击晕确认弹窗（标题/内容/按钮）
                    InformationManager.ShowInquiry(new InquiryData(LWNTextHelper.ResolveText("LWN_ui_interact_inquiry_danger", "Danger"), LWNTextHelper.ResolveCompound("LWN_ui_interact_inquiry_knockout_msg", ("NAME", targetName)), true, false, LWNTextHelper.ResolveText("LWN_ui_interact_btn_fight", "Come and fight!"), null, confirm, null));
                }
            },
            // 6. lead（仅计划：执行器内联状态机）
            new ActionSpec
            {
                Code = "lead",
                InPlanVocab = true,
                LabelKey = "lead", LabelFallback = "lead the way",
                IsValid = (a, d, ag) => false,   // 计划语义（执行器），无闲聊入口 → 永不调用
            },
            // 7. face（原 FACE；闲聊 ChatOrder=17）
            new ActionSpec
            {
                Code = "face",
                Description = "转身面向对方（仅当面）。",
                InPlanVocab = true, InChatSpace = true, ChatOrder = 17,
                Spaces = ActionSpace.InScene,
                LabelKey = "face", LabelFallback = "face",
                IsValid = (npc, player, agent) => agent != null,
                Execute = (attacker, defender, agent, l, t, s) =>
                {
                    string name = defender != null ? defender.Name.ToString() : t;
                    ChatActionFlow.TryExecute(agent, "face", name, null, null);
                }
            },
            // 8. look_at（原 LOOK_AT；闲聊 ChatOrder=18；时长默认 2s）
            new ActionSpec
            {
                Code = "look_at",
                Description = "注视对方片刻（仅当面）。",
                InPlanVocab = true, InChatSpace = true, ChatOrder = 18,
                Spaces = ActionSpace.InScene,
                LabelKey = "look_at", LabelFallback = "look at",
                IsValid = (npc, player, agent) => agent != null,
                FillParams = (step, level, sayText) => step.Seconds = 2f,   // 时长默认 2s（§5.3）
                Execute = (attacker, defender, agent, l, t, s) =>
                {
                    string name = defender != null ? defender.Name.ToString() : t;
                    ChatActionFlow.TryExecute(agent, "look_at", name, null, null);
                }
            },
            // 9. say_to（原 SAY_TO；闲聊 ChatOrder=24；v1 = IM 回复正文复述，一句话两用）
            new ActionSpec
            {
                Code = "say_to",
                Description = "转头对目标当面说这句话（仅当面）。",
                InPlanVocab = true, InChatSpace = true, ChatOrder = 24,
                Spaces = ActionSpace.InScene,
                Aliases = new[] { "speak" },
                LabelKey = "say_to", LabelFallback = "speak to",
                IsValid = (npc, player, agent) => agent != null,
                FillParams = (step, level, sayText) => step.Text = sayText,   // v1：IM 回复正文复述（prompt 约束正文须可转述）
                Execute = (attacker, defender, agent, l, t, s) =>
                {
                    string name = defender != null ? defender.Name.ToString() : t;
                    ChatActionFlow.TryExecute(agent, "say_to", name, null, s);
                }
            },
            // 10. wait（仅计划：执行器内联状态机）
            new ActionSpec
            {
                Code = "wait",
                InPlanVocab = true,
                LabelKey = "wait", LabelFallback = "wait",
                IsValid = (a, d, ag) => false,   // 计划语义（执行器），无闲聊入口 → 永不调用
            },
            // 11. emote（原 EMOTE；闲聊 ChatOrder=16；level = 动画 key，白名单 9 动画在 EmoteInlineState）
            new ActionSpec
            {
                Code = "emote",
                Description = "做出一个手势/动作（nod 点头/shake 摇头/wave 招手/cheer 欢呼/bow 鞠躬/shrug 耸肩/point 指路/threaten 威胁手势/disappointed 沮丧；仅当面）。",
                InPlanVocab = true, InChatSpace = true, ChatOrder = 16,
                Spaces = ActionSpace.InScene,
                LabelKey = "emote", LabelFallback = "gesture",
                IsValid = (npc, player, agent) => agent != null,
                FillParams = (step, level, sayText) => step.Text = level,   // EmoteInlineState 白名单校验（9 动画），非法 → 降级无动作
                AnnounceParam = (level) => level,
                Execute = (attacker, defender, agent, l, t, s) => ChatActionFlow.TryExecute(agent, "emote", null, l, null)
            },
            // 12. make_noise（原 MAKE_NOISE；闲聊 ChatOrder=25）
            new ActionSpec
            {
                Code = "make_noise",
                Description = "大喊一声引人注意（仅当面）。",
                InPlanVocab = true, InChatSpace = true, ChatOrder = 25,
                Spaces = ActionSpace.InScene,
                LabelKey = "make_noise", LabelFallback = "shout",
                IsValid = (npc, player, agent) => agent != null,
                Execute = (attacker, defender, agent, l, t, s) => ChatActionFlow.TryExecute(agent, "make_noise", null, null, null)
            },
            // 13. signal_player（原 SIGNAL_PLAYER；闲聊 ChatOrder=21；无参）
            new ActionSpec
            {
                Code = "signal_player",
                Description = "向玩家发出一个信号（仅当面）。",
                InPlanVocab = true, InChatSpace = true, ChatOrder = 21,
                Spaces = ActionSpace.InScene,
                LabelKey = "signal_player", LabelFallback = "signal",
                IsValid = (npc, player, agent) => agent != null,
                Execute = (attacker, defender, agent, l, t, s) => ChatActionFlow.TryExecute(agent, "signal_player", null, null, null)
            },
            // 14. steal_attempt（原 STEAL_ATTEMPT；闲聊 ChatOrder=15；人变体扒窃）
            new ActionSpec
            {
                Code = "steal_attempt",
                Description = "偷走对方身上的钱（仅当面；高风险）。",
                InPlanVocab = true, InChatSpace = true, ChatOrder = 15,
                Spaces = ActionSpace.InScene,
                RequiresConfirm = true,   // 高风险：IM 路径走提议卡片
                InquiryTitleKey = "danger", InquiryMsgKey = "steal",
                Aliases = new[] { "steal" },
                LabelKey = "steal_attempt", LabelFallback = "steal from",
                ResultKeys = new HashSet<string> { "success", "empty", "impossible", "interrupted" },
                IsValid = (npc, player, agent) => agent != null,
                FillParams = (step, level, sayText) => step.Variant = "pickpocket",   // 人变体（扒窃 defender；result 路由既有）
                // 核心执行（IM 卡片批准后直接跑；当面对话走 Execute 的弹窗包装）
                ExecuteCore = (attacker, defender, agent, l, t, s, explicitTarget) =>
                {
                    // 🔴 2026-08-13：与 knockout 同款——目标文本优先（模板 NPC 名/玩家选定候选），缺省回退 defender 名
                    string targetName = explicitTarget != null ? "target"
                        : (!string.IsNullOrWhiteSpace(t) ? t
                            : (defender != null ? defender.Name.ToString() : (agent != null ? agent.Name.ToString() : "")));
                    ChatActionFlow.TryExecute(agent, "steal_attempt", targetName, null, null, explicitTarget);
                },
                Execute = (attacker, defender, agent, l, t, s) =>
                {
                    string targetName = defender != null ? defender.Name.ToString() : (agent != null ? agent.Name.ToString() : "");
                    Action confirm = () => ActionHandler.RunActionCore("steal_attempt", attacker, defender, agent, l, t, s);
                    // 本地化：扒窃确认弹窗（标题/内容/按钮）
                    InformationManager.ShowInquiry(new InquiryData(LWNTextHelper.ResolveText("LWN_ui_interact_inquiry_danger", "Danger"), LWNTextHelper.ResolveCompound("LWN_ui_interact_inquiry_steal_msg", ("NAME", targetName)), true, false, LWNTextHelper.ResolveText("LWN_ui_interact_btn_fight", "Come and fight!"), null, confirm, null));
                }
            },
            // 15. give_item（仅计划：执行器内联状态机）
            new ActionSpec
            {
                Code = "give_item",
                InPlanVocab = true,
                Aliases = new[] { "give" },
                LabelKey = "give_item", LabelFallback = "hand over",
                IsValid = (a, d, ag) => false,   // 计划语义（执行器），无闲聊入口 → 永不调用
            },
            // 16. give_gold（原 GIVE_GOLD；闲聊 ChatOrder=22；守恒：attacker 钱包 → 玩家）
            new ActionSpec
            {
                Code = "give_gold",
                Description = "掏出自己的钱给玩家（档位 small=50 / medium=150 / large=500 金币；仅当面）。",
                InPlanVocab = true, InChatSpace = true, ChatOrder = 22,
                Spaces = ActionSpace.InScene,
                NeedsCooldown = true,
                LabelKey = "give_gold", LabelFallback = "give gold",
                IsValid = (npc, player, agent) => agent != null && npc != null,
                FillParams = (step, level, sayText) => step.Amount = new JValue(ChatActionFlow.GoldLevelAmount(level)),
                // 本地化：LWN_action_gold_unit（玩家可见文本）
                AnnounceParam = (level) => $"{ChatActionFlow.GoldLevelAmount(level)} {LWNTextHelper.ResolveText("LWN_action_gold_unit", "gold")}",
                Execute = (attacker, defender, agent, l, t, s) =>
                {
                    if (attacker == null || Hero.MainHero == null) return;
                    int amount = ChatActionFlow.GoldLevelAmount(l);
                    if (attacker.Gold < amount)
                    {
                        DebugLogger.Log($"[ActionHandler] GIVE_GOLD {attacker.Name} 钱不够（{attacker.Gold}/{amount}）→ 降级 NONE");
                        return;
                    }
                    // 守恒转移（铁律 4：一方扣一方加；与计划模式 GiveInlineState 的赃物移交语义区分——闲聊 = 随从个人钱包直付）
                    AgentControlHelper.TransferGold(attacker, Hero.MainHero, amount);
                    DebugLogger.Log($"[ActionHandler] GIVE_GOLD {attacker.Name} → 玩家 {amount} 金币");
                }
            },
            // 17. deliver_item（仅计划：执行器内联状态机）
            new ActionSpec
            {
                Code = "deliver_item",
                InPlanVocab = true,
                LabelKey = "deliver_item", LabelFallback = "deliver",
                IsValid = (a, d, ag) => false,   // 计划语义（执行器），无闲聊入口 → 永不调用
            },
            // 18. shadow（仅计划；未实现 → 执行器步骤失败）
            new ActionSpec
            {
                Code = "shadow",
                InPlanVocab = true,
                ExecutorImplemented = false,
                LabelKey = "shadow", LabelFallback = "shadow",
                IsValid = (a, d, ag) => false,   // 未实现：永不通过
            },
            // 19. negotiate（仅计划；未实现 → 执行器步骤失败）
            new ActionSpec
            {
                Code = "negotiate",
                InPlanVocab = true,
                ExecutorImplemented = false,
                LabelKey = "negotiate", LabelFallback = "negotiate",
                ResultKeys = new HashSet<string> { "success", "partial", "fail" },
                IsValid = (a, d, ag) => false,   // 未实现：永不通过
            },
            // 20. duel（原 DUEL；闲聊 ChatOrder=13；⚠️ 双语义一行承载：计划侧=判定型未实现；
            // 闲聊侧=切磋开打经 ExecuteCore 发 duel 事件，互不干扰）
            new ActionSpec
            {
                Code = "duel",
                Description = "和平的交手切磋（进入不致命的战斗；仅当面）。",
                InPlanVocab = true, InChatSpace = true, ChatOrder = 13,
                Spaces = ActionSpace.InScene,
                RequiresConfirm = true,   // 高风险：IM 路径走提议卡片
                InquiryTitleKey = "hint", InquiryMsgKey = "duel",
                ExecutorImplemented = false,   // 计划侧判定型未实现（闲聊侧 ExecuteCore 已实现）
                LabelKey = "duel", LabelFallback = "duel",
                ResultKeys = new HashSet<string> { "win", "draw", "lose" },
                IsValid = (npc, player, agent) => agent != null,
                // 核心执行（IM 卡片批准后直接跑；当面对话走 Execute 的弹窗包装）。
                // 🔴 2026-08-13：发 "duel" 事件（与 order_attack 区分）→ AgentBrain 切磋分支
                // → FightEnemyAction(IsDuel) → StartFight(Peace:true) → StartDuel（Invulnerable
                // 底层无敌，点到为止）。旧实现发 order_attack 走真打链（无无敌，会打死人）。
                ExecuteCore = (attacker, defender, agent, l, t, s, explicitTarget) =>
                {
                    // 🔴 2026-08-13：explicitTarget = 模板 NPC 目标（玩家选定后直达，不走 Hero 解析）
                    Agent target = explicitTarget;
                    if (target == null && defender != null && defender != Hero.MainHero)
                        target = ActionHandler.FindAgentByHeroId(defender.StringId);
                    if (target == null) target = Agent.Main;
                    if (agent != null && target != null && agent.IsActive() && target.IsActive())
                        AgentAIController.Instance?.SendEventToAgent(agent, "duel", target);
                },
                Execute = (attacker, defender, agent, l, t, s) =>
                {
                    string targetName = defender != null ? defender.Name.ToString() : (agent != null ? agent.Name.ToString() : "");
                    Action confirmFight = () =>
                    {
                        // 🔴 2026-08-13：同上——duel 事件 → AgentBrain 切磋分支 → StartDuel 无敌仲裁
                        ActionHandler.RunActionCore("duel", attacker, defender, agent, l, t, s);
                    };
                    // 本地化：切磋确认弹窗（标题/内容/按钮）
                    InformationManager.ShowInquiry(new InquiryData(LWNTextHelper.ResolveText("LWN_ui_interact_inquiry_hint", "Notice"), LWNTextHelper.ResolveCompound("LWN_ui_interact_inquiry_duel_msg", ("NAME", targetName)), true, false, LWNTextHelper.ResolveText("LWN_ui_interact_btn_fight", "Come and fight!"), null, confirmFight, null));
                }
            },
            // 21. end_plan（仅计划；IsTerminal 收尾）
            new ActionSpec
            {
                Code = "end_plan",
                InPlanVocab = true,
                IsTerminal = true,
                LabelKey = "end_plan", LabelFallback = "finish",
                IsValid = (a, d, ag) => false,   // 计划语义（执行器），无闲聊入口 → 永不调用
            },
            // ── 仅闲聊 13 行（无计划词表；ChatOrder 2..27 衔接）──
            // 22. NONE 空操作哨兵（大写保留：三处跳过判据 actionCode == "NONE" 原样有效）
            new ActionSpec
            {
                Code = "NONE",
                Description = "默认无动作，仅进行对话（普通寒暄必选）。",
                InChatSpace = true, ChatOrder = 1,
                IsValid = (npc, player, agent) => true,
                Execute = (n, p, a, l, t, s) => { /* Do nothing */ }
            },
            // 23. relation_up（原 RELATION_UP；好感上升，attacker 对 defender；NPC↔NPC 官方 API）
            new ActionSpec
            {
                Code = "relation_up",
                Description = "好感上升：你对对方印象变好（档位 small=+3 / medium=+5 / large=+10）。",
                InChatSpace = true, ChatOrder = 2,
                Spaces = ActionSpace.InScene | ActionSpace.Remote | ActionSpace.Party,
                NeedsCooldown = true,
                LabelKey = "relation_up", LabelFallback = "raise opinion of",
                IsValid = (a, d, ag) => a != null && d != null,
                AnnounceParam = (level) => ActionHandler.LevelWord(level),
                Execute = (a, d, ag, l, t, s) =>
                {
                    if (a == null || d == null) return;
                    int delta = ActionHandler.LevelDelta(l, +1);
                    if (d == Hero.MainHero)
                    {
                        // 玩家侧：官方玩家关系 API（showQuickNotification：玩家可见反馈，§5.2 裁定例外）
                        ChangeRelationAction.ApplyPlayerRelation(d, delta, true, true);
                    }
                    else
                    {
                        // NPC↔NPC：静默执行（不刷系统行，§5.2 反馈裁定）；后续言行体现
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(a, d, delta, false);
                    }
                    DebugLogger.Log($"[ActionHandler] RELATION_UP {a.Name}→{d.Name} {delta:+0;-0}");
                }
            },
            // 24. relation_down（原 RELATION_DOWN；好感下降）
            new ActionSpec
            {
                Code = "relation_down",
                Description = "好感下降：你对对方印象变差（档位 small=-3 / medium=-5 / large=-10）。",
                InChatSpace = true, ChatOrder = 3,
                Spaces = ActionSpace.InScene | ActionSpace.Remote | ActionSpace.Party,
                NeedsCooldown = true,
                LabelKey = "relation_down", LabelFallback = "lower opinion of",
                IsValid = (a, d, ag) => a != null && d != null,
                AnnounceParam = (level) => ActionHandler.LevelWord(level),
                Execute = (a, d, ag, l, t, s) =>
                {
                    if (a == null || d == null) return;
                    int delta = ActionHandler.LevelDelta(l, -1);
                    if (d == Hero.MainHero)
                        ChangeRelationAction.ApplyPlayerRelation(d, delta, true, true);
                    else
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(a, d, delta, false);
                    DebugLogger.Log($"[ActionHandler] RELATION_DOWN {a.Name}→{d.Name} {delta:+0;-0}");
                }
            },
            // 25. increase_relation（兼容旧词表：RELATION 语义同款，缺省档位 medium=±5；
            // LabelKey 承载别名 → relation_up 同款标签）
            new ActionSpec
            {
                Code = "increase_relation",
                Description = "好感度小幅上升（兼容旧词表）。",
                InChatSpace = true, ChatOrder = 4,
                Spaces = ActionSpace.InScene | ActionSpace.Remote | ActionSpace.Party,
                NeedsCooldown = true,
                LabelKey = "relation_up", LabelFallback = "raise opinion of",
                IsValid = (a, d, ag) => a != null && d != null,
                AnnounceParam = (level) => ActionHandler.LevelWord(level),
                Execute = (a, d, ag, l, t, s) =>
                {
                    if (a == null || d == null) return;
                    int delta = ActionHandler.LevelDelta(null, +1);
                    if (d == Hero.MainHero) ChangeRelationAction.ApplyPlayerRelation(d, delta, true, true);
                    else ChangeRelationAction.ApplyRelationChangeBetweenHeroes(a, d, delta, false);
                }
            },
            // 26. decrease_relation（兼容旧词表；LabelKey 承载别名 → relation_down 同款标签）
            new ActionSpec
            {
                Code = "decrease_relation",
                Description = "好感度小幅下降（兼容旧词表）。",
                InChatSpace = true, ChatOrder = 5,
                Spaces = ActionSpace.InScene | ActionSpace.Remote | ActionSpace.Party,
                NeedsCooldown = true,
                LabelKey = "relation_down", LabelFallback = "lower opinion of",
                IsValid = (a, d, ag) => a != null && d != null,
                AnnounceParam = (level) => ActionHandler.LevelWord(level),
                Execute = (a, d, ag, l, t, s) =>
                {
                    if (a == null || d == null) return;
                    int delta = ActionHandler.LevelDelta(null, -1);
                    if (d == Hero.MainHero) ChangeRelationAction.ApplyPlayerRelation(d, delta, true, true);
                    else ChangeRelationAction.ApplyRelationChangeBetweenHeroes(a, d, delta, false);
                }
            },
            // 27. praise（夸赞：defender 本地声望小升；在场=当众夸 → 说话广播链）
            new ActionSpec
            {
                Code = "praise",
                Description = "夸赞对方：对方在当地声望小升（当众夸赞/背后说好话）。",
                InChatSpace = true, ChatOrder = 6,
                Spaces = ActionSpace.InScene | ActionSpace.Remote | ActionSpace.Party,
                NeedsCooldown = true,
                LabelKey = "praise", LabelFallback = "praise",
                IsValid = (a, d, ag) => d != null,
                Execute = (a, d, ag, l, t, s) =>
                {
                    if (d == null) return;
                    var settlement = d.CurrentSettlement;
                    if (settlement == null)
                    {
                        DebugLogger.Log($"[ActionHandler] PRAISE {d.Name} 不在定居点 → 降级 NONE");
                        return;
                    }
                    SettlementHonorStore.Modify(settlement, 2);
                    // InScene 当众夸 → 广播（line = 夸赞的话；无话可说不广播）
                    if (Mission.Current != null && !string.IsNullOrWhiteSpace(s) && ImChatManager.IsPresentInMission(d.StringId))
                    {
                        Agent defenderAgent = ActionHandler.FindAgentByHeroId(d.StringId);
                        Agent attackerAgent = ActionHandler.FindAgentByHeroId(a?.StringId);
                        if (defenderAgent != null && attackerAgent != null)
                            DialogueComponent.HandleDialogue(attackerAgent, defenderAgent, "praise", s);
                    }
                    DebugLogger.Log($"[ActionHandler] PRAISE {d.Name} 本地声望 +2");
                }
            },
            // 28. spread_rumor（造谣：defender 本地声望小降 + 写双方记忆——恩怨后续对话接得住）
            new ActionSpec
            {
                Code = "spread_rumor",
                Description = "散布关于对方的谣言：对方当地声望小降（背后说坏话）。",
                InChatSpace = true, ChatOrder = 7,
                Spaces = ActionSpace.InScene | ActionSpace.Remote | ActionSpace.Party,
                NeedsCooldown = true,
                LabelKey = "spread_rumor", LabelFallback = "spread rumors about",
                IsValid = (a, d, ag) => d != null,
                Execute = (a, d, ag, l, t, s) =>
                {
                    if (d == null) return;
                    var settlement = d.CurrentSettlement;
                    if (settlement == null)
                    {
                        DebugLogger.Log($"[ActionHandler] SPREAD_RUMOR {d.Name} 不在定居点 → 声望部分降级");
                    }
                    else
                    {
                        SettlementHonorStore.Modify(settlement, -2);
                    }
                    // 双方记忆（defender 记「被造谣」→ 后续对峙；attacker 记「我造了谣」→ 可自首）
                    string rumorLine = LWNTextHelper.ResolveCompound("LWN_plan_action_rumor",
                        "有人在背后说我的坏话", ("NAME", a?.Name?.ToString() ?? ""));
                    ActionHandler.WriteMemory(d, "user", rumorLine, a?.StringId ?? "");
                    if (a != null)
                        // 记忆：我造了谣（attacker 侧）
                        ActionHandler.WriteMemory(a, "user", LWNTextHelper.ResolveCompound("LWN_plan_action_rumor_self",
                            "我在背后说了 {NAME} 的坏话", ("NAME", d.Name?.ToString() ?? "")), a.StringId);
                    // InScene 当众造谣 → 广播 spoken_to（被造谣者 respond——撕破脸，§5.5 说话类对抗广播链）
                    if (Mission.Current != null && !string.IsNullOrWhiteSpace(s) && ImChatManager.IsPresentInMission(d.StringId))
                    {
                        Agent defenderAgent = ActionHandler.FindAgentByHeroId(d.StringId);
                        Agent attackerAgent = ActionHandler.FindAgentByHeroId(a?.StringId);
                        if (defenderAgent != null && attackerAgent != null)
                            DialogueComponent.HandleDialogue(attackerAgent, defenderAgent, "rumor", s);
                    }
                    DebugLogger.Log($"[ActionHandler] SPREAD_RUMOR {a?.Name} 造谣 {d.Name}");
                }
            },
            // 29. threaten_verbal（威胁：写 defender 记忆；InScene 版广播 → defender 人格演算反应）
            new ActionSpec
            {
                Code = "threaten_verbal",
                Description = "出言威胁对方（对方会记住这次威胁；当面威胁对方可能当场翻脸）。",
                InChatSpace = true, ChatOrder = 8,
                Spaces = ActionSpace.InScene | ActionSpace.Remote | ActionSpace.Party,
                NeedsCooldown = true,
                LabelKey = "threaten_verbal", LabelFallback = "threaten",
                IsValid = (a, d, ag) => d != null,
                Execute = (a, d, ag, l, t, s) =>
                {
                    if (d == null) return;
                    ActionHandler.WriteMemory(d, "user",
            // 记忆：被威胁（defender 侧）
                        LWNTextHelper.ResolveCompound("LWN_plan_action_threatened",
                            "{NAME} 威胁过我：{TEXT}", ("NAME", a?.Name?.ToString() ?? ""), ("TEXT", s ?? "")),
                        a?.StringId ?? "");
                    // InScene 版：威胁 = 搭话 → 广播 spoken_to（speaker=attacker，line=威胁台词）→ defender 人格演算（愤怒/畏惧/叫守卫/记仇）
                    // 收敛到 DialogueComponent.HandleDialogue（§5.6 统一入口，含旁观者 seen_speaking）
                    if (Mission.Current != null && ImChatManager.IsPresentInMission(d.StringId))
                    {
                        Agent defenderAgent = ActionHandler.FindAgentByHeroId(d.StringId);
                        Agent attackerAgent = ActionHandler.FindAgentByHeroId(a?.StringId);
                        if (defenderAgent != null && attackerAgent != null)
                        {
                            // 威胁缺省台词（广播用）
                            string line = !string.IsNullOrWhiteSpace(s) ? s : LWNTextHelper.ResolveText("LWN_plan_action_threaten_default", "You had better watch yourself.");
                            DialogueComponent.HandleDialogue(attackerAgent, defenderAgent, "threat", line);
                            // 🔴 2026-08-11 架构收敛：注册 SocialSlot 续话——defender respond 后 attacker 跟进（威胁对峙吵起来）
                            DialogueComponent.RegisterSession(attackerAgent, defenderAgent, "threat", new SocialSlot());
                            DebugLogger.Log($"[ActionHandler] THREATEN_VERBAL 广播 spoken_to: {a.Name} → {d.Name}");
                        }
                    }
                }
            },
            // 30. promise（承诺：写 defender 记忆）
            new ActionSpec
            {
                Code = "promise",
                Description = "向对方作出承诺（对方会记住这次承诺）。",
                InChatSpace = true, ChatOrder = 9,
                Spaces = ActionSpace.InScene | ActionSpace.Remote | ActionSpace.Party,
                NeedsCooldown = true,
                LabelKey = "promise", LabelFallback = "make a promise to",
                IsValid = (a, d, ag) => d != null,
                Execute = (a, d, ag, l, t, s) =>
                {
                    if (d == null) return;
                    ActionHandler.WriteMemory(d, "user",
            // 记忆：被承诺（defender 侧）
                        LWNTextHelper.ResolveCompound("LWN_plan_action_promised",
                            "{NAME} 答应过我：{TEXT}", ("NAME", a?.Name?.ToString() ?? ""), ("TEXT", s ?? "")),
                        a?.StringId ?? "");
                }
            },
            // 31. marry_success（结婚：defender = 求婚对象；仅当面）
            new ActionSpec
            {
                Code = "marry_success",
                Description = "同意对方的求婚（建立婚姻关系；仅当面）。",
                InChatSpace = true, ChatOrder = 10,
                Spaces = ActionSpace.InScene,
                LabelKey = "marry_success", LabelFallback = "agree to marry",
                IsValid = (npc, player, agent) =>
                {
                    if (npc == null || player == null) return false;
                    bool differentGender = npc.IsFemale != player.IsFemale;
                    bool npcSingle = npc.Spouse == null;
                    bool playerSingle = player.Spouse == null;
                    return differentGender && npcSingle && playerSingle;
                },
                Execute = (npc, player, agent, l, t, s) =>
                {
                    if (npc != null && player != null)
                    {
                        MarriageAction.Apply(player, npc);
                        // 本地化：求婚成功消息
                        InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_interact_msg_marry_accept", ("NAME", npc.Name.ToString())), Colors.Green));
                    }
                }
            },
            // 32. join_clan（招募：defender = 招募对象；仅当面）
            new ActionSpec
            {
                Code = "join_clan",
                Description = "接受招募，加入玩家的家族（仅当面）。",
                InChatSpace = true, ChatOrder = 11,
                Spaces = ActionSpace.InScene,
                LabelKey = "join_clan", LabelFallback = "join the clan",
                IsValid = (npc, player, agent) =>
                {
                    if (npc == null) return false;
                    return npc.Clan == null || npc.IsWanderer;
                },
                Execute = (npc, player, agent, l, t, s) =>
                {
                    if (npc != null)
                    {
                        // 具体的招募逻辑 (示例)
                        // 本地化：招募加入家族消息
                        InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ui_interact_msg_join_clan", ("NAME", npc.Name.ToString())), Colors.Blue));
                    }
                }
            },
            // 33. party_patrol（部队巡逻：defender party 巡逻其所在 settlement；🔴 资格守卫查 defender）
            new ActionSpec
            {
                Code = "party_patrol",
                Description = "率部在所在城镇周边巡逻（大地图）。",
                InChatSpace = true, ChatOrder = 26,
                Spaces = ActionSpace.Party,
                NeedsCooldown = true,
                LabelKey = "party_patrol", LabelFallback = "start patrolling",
                IsValid = (attacker, defender, agent) => defender != null
                    && defender != Hero.MainHero
                    && defender.Clan == Clan.PlayerClan
                    && defender.PartyBelongedTo != null
                    && defender.PartyBelongedTo != MobileParty.MainParty,
                Execute = (attacker, defender, agent, l, t, s) =>
                {
                    if (defender == null || defender == Hero.MainHero
                        || defender.Clan != Clan.PlayerClan || defender.PartyBelongedTo == null
                        || defender.PartyBelongedTo == MobileParty.MainParty)
                    {
                        DebugLogger.Log($"[ActionHandler] PARTY_PATROL {defender?.Name} 资格不符（非玩家家族独立部队）→ 降级 NONE");
                        return;
                    }
                    var settlement = defender.PartyBelongedTo.CurrentSettlement;
                    if (settlement == null)
                    {
                        DebugLogger.Log($"[ActionHandler] PARTY_PATROL {defender.Name} 无当前定居点 → 降级 NONE");
                        return;
                    }
                    // 巡逻目标 C# 确定（不解析 LLM 文本）
                    V.PatrolAround(defender.PartyBelongedTo, settlement);
                    DebugLogger.Log($"[ActionHandler] PARTY_PATROL {defender.Name} 巡逻 {settlement.Name}");
                }
            },
            // 34. gather_to_player（部队集结：defender party 移向玩家 party；资格守卫同 party_patrol）
            new ActionSpec
            {
                Code = "gather_to_player",
                Description = "率部集结到玩家身边（大地图）。",
                InChatSpace = true, ChatOrder = 27,
                Spaces = ActionSpace.Party,
                NeedsCooldown = true,
                LabelKey = "gather_to_player", LabelFallback = "march to assemble",
                IsValid = (attacker, defender, agent) => defender != null
                    && defender != Hero.MainHero
                    && defender.Clan == Clan.PlayerClan
                    && defender.PartyBelongedTo != null
                    && defender.PartyBelongedTo != MobileParty.MainParty,
                Execute = (attacker, defender, agent, l, t, s) =>
                {
                    if (defender == null || defender == Hero.MainHero
                        || defender.Clan != Clan.PlayerClan || defender.PartyBelongedTo == null
                        || defender.PartyBelongedTo == MobileParty.MainParty)
                    {
                        DebugLogger.Log($"[ActionHandler] GATHER_TO_PLAYER {defender?.Name} 资格不符（非玩家家族独立部队）→ 降级 NONE");
                        return;
                    }
                    // 集结 = 护送玩家部队（SetPartyAiAction.EscortParty：跟随玩家 party 移动，反编译确认语义）
                    V.GatherToPlayer(defender.PartyBelongedTo);
                    DebugLogger.Log($"[ActionHandler] GATHER_TO_PLAYER {defender.Name} 集结到玩家部队");
                }
            },
            // 35. crouch（引擎下蹲：玩家 Z 键同机制 SetCrouchMode = AIScriptedFrameFlags.Crouch；
            // 瞬时 flag 操作，零风险可逆 → 免确认直接执行，与 emote 同级。蹲姿保持到「站起」/脑接管自动清除）
            new ActionSpec
            {
                Code = "crouch",
                Description = "蹲下（保持蹲姿，直到命令站起；仅当面）。",
                InChatSpace = true, ChatOrder = 28,
                Spaces = ActionSpace.InScene,
                SelfTargeted = true,
                LabelKey = "crouch", LabelFallback = "crouch",
                IsValid = (npc, player, agent) => agent != null,
                Execute = (attacker, defender, agent, l, t, s) => ChatActionFlow.TryExecute(agent, "crouch", null, null, null)
            },
            // 36. stand（站起：解除引擎蹲姿，SetCrouchMode(false)；对称的瞬时免确认动作）
            new ActionSpec
            {
                Code = "stand",
                Description = "站起（从蹲姿恢复站立；仅当面）。",
                InChatSpace = true, ChatOrder = 29,
                Spaces = ActionSpace.InScene,
                SelfTargeted = true,
                LabelKey = "stand", LabelFallback = "stand up",
                IsValid = (npc, player, agent) => agent != null,
                Execute = (attacker, defender, agent, l, t, s) => ChatActionFlow.TryExecute(agent, "stand", null, null, null)
            },
            // 37. ask_help（🔴 2026-08-14 M6，npc-risk-aware-planning.md：多随从分头配合）——
            // 计划侧配合动作：执行人请求同袍执行单个低危动作（引开/望风/手势示意），自己继续主任务。
            // v1 白名单 = make_noise/follow/emote（配合者不生成计划、不风险审视）；配合者忙碌 → on_timeout 兜底。
            // InChatSpace=false：闲聊一句话不直接调多随从（v1 只允许计划语法出现）。
            new ActionSpec
            {
                Code = "ask_help",
                InPlanVocab = true,
                LabelKey = "ask_help", LabelFallback = "ask a companion for help",
                IsValid = (a, d, ag) => false,   // 计划语义（执行器），无闲聊入口 → 永不调用
            },
            // 38. steal_equipment（🔴 2026-08-14 M7，npc-risk-aware-planning.md：先削弱再打）——
            // 计划侧战术动作：卸目标装备（武器槽优先，削攻最直观）→ 目标徒手 → 战力真实下降。
            // 执行层复用扒窃判定管线（StealAttemptInlineState variant="equipment"）+ StealEquipmentForNpc 共享结算。
            // RequiresConfirm=true 语义登记（扒窃目标本人 = 高危，同 steal_attempt）；
            // InChatSpace=false：v1 只计划语法（闲聊一句话不直接触发）。
            new ActionSpec
            {
                Code = "steal_equipment",
                InPlanVocab = true,
                Spaces = ActionSpace.InScene,
                RequiresConfirm = true,
                InquiryTitleKey = "danger", InquiryMsgKey = "steal",
                LabelKey = "steal_equipment", LabelFallback = "disarm",
                ResultKeys = new HashSet<string> { "success", "empty", "impossible", "interrupted" },
                IsValid = (a, d, ag) => false,   // 计划语义（执行器），无闲聊入口 → 永不调用
            },
            // 39. ask_player（🔴 2026-08-15，等机会/抉择点询问主公）——
            // 计划侧抉择原语：执行人向玩家投递密信决策卡（撤退/强制执行），玩家点击 →
            // 事件回投 → 本步骤 on_event 路由（type 仅 retreat/force）；超时未答 → 默认撤退
            //（on_timeout 或 @abort_gracefully）。典型用法：击晕/扒窃"等没人看"的 wait 步骤超时后，
            // 不直接撤退，先问主公（宁可问也不擅自放弃主公的命令）。
            // InChatSpace=false：仅计划语法（闲聊对话轮不出现）。
            new ActionSpec
            {
                Code = "ask_player",
                InPlanVocab = true,
                LabelKey = "ask_player", LabelFallback = "ask the lord",
                IsValid = (a, d, ag) => false,   // 计划语义（执行器内联），无闲聊入口 → 永不调用
            },
        };
        /// <summary>计划词表动作（InPlanVocab，按主表序 = 原 ActionsInPromptOrder 手写序）。</summary>
        public static IEnumerable<ActionSpec> PlanActions => All.Where(s => s.InPlanVocab);
        /// <summary>闲聊空间动作（InChatSpace，按 ChatOrder 展示序；NONE 不进 prompt 由调用方跳过）。</summary>
        public static IEnumerable<ActionSpec> ChatActions => All.Where(s => s.InChatSpace).OrderBy(s => s.ChatOrder);
        static ActionRegistry()
        {
            // 🔴 自检五连：失败只写日志不弹窗（Debug.Assert 在实机 Debug 构建会弹断言框崩游戏——
            // 铁律 1；自检失败 = 表结构错误，降级继续跑，问题在 StoryEngine_RuntimeLog.txt 可见）
            // 计划 24 码字面量序（82% LLM 回归基线依赖——派生数组必须逐字节一致；
            // 2026-08-14 追加 ask_help/steal_equipment 于末尾，既有 21 码顺序不动；
            // 2026-08-15 追加 ask_player 于末尾）
            string[] expectedPlanOrder =
            {
                "move_to", "follow", "stop_following", "order_attack", "knockout", "lead",
                "face", "look_at", "say_to", "wait", "emote", "make_noise", "signal_player",
                "steal_attempt", "give_item", "give_gold", "deliver_item", "shadow",
                "negotiate", "duel", "end_plan",
                "ask_help", "steal_equipment", "ask_player",
            };
            Check(PlanActions.Select(s => s.Code).SequenceEqual(expectedPlanOrder),
                "[ActionRegistry] 计划 24 码顺序与基线不符（82% LLM 回归基线依赖此顺序）");
            // ChatOrder 1..29 连续（闲聊 prompt 展示序钉死）
            var chatOrders = ChatActions.Select(s => s.ChatOrder).ToArray();
            Check(chatOrders.SequenceEqual(Enumerable.Range(1, 29)),
                "[ActionRegistry] ChatOrder 必须为 1..29 连续序列");
            // 未实现集合（计划侧执行器）
            var unimplemented = All.Where(s => !s.ExecutorImplemented).Select(s => s.Code).OrderBy(c => c).ToArray();
            Check(unimplemented.SequenceEqual(new[] { "duel", "negotiate", "shadow" }),
                "[ActionRegistry] ExecutorImplemented=false 必须是 {shadow, negotiate, duel}");
            // 别名无重复（含与 Code 冲突）
            var aliases = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in All)
            {
                Check(s.IsValid != null, $"[ActionRegistry] {s.Code} IsValid 为空");
                if (s.InChatSpace)
                    Check(s.Execute != null || s.ExecuteCore != null,
                        $"[ActionRegistry] {s.Code} 在闲聊空间但 Execute/ExecuteCore 均为空");
                if (s.Aliases != null)
                {
                    foreach (var al in s.Aliases)
                    {
                        Check(!string.IsNullOrEmpty(al) && al != s.Code
                            && aliases.Add(al), $"[ActionRegistry] 别名重复: {al}（{s.Code}）");
                    }
                }
            }
        }
        /// <summary>自检失败 → 写运行日志（不抛异常不弹窗，铁律 1：游戏不能崩）。</summary>
        private static void Check(bool ok, string msg)
        {
            if (!ok) DebugLogger.Log(msg);
        }
        /// <summary>按码查动作（OrdinalIgnoreCase——兼容旧存档大写码 "ATTACK"/"MOVE_TO" 等）。</summary>
        public static ActionSpec FindByCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            foreach (var s in All)
                if (s.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }
        /// <summary>按标签码查动作（ByCode 优先，回落别名——PlanActionLabel 用；别名不区分大小写）。</summary>
        public static ActionSpec FindByLabelCode(string code)
        {
            var spec = FindByCode(code);
            if (spec != null) return spec;
            if (string.IsNullOrEmpty(code)) return null;
            foreach (var s in All)
            {
                if (s.Aliases == null) continue;
                foreach (var al in s.Aliases)
                    if (al.Equals(code, StringComparison.OrdinalIgnoreCase)) return s;
            }
            return null;
        }
        /// <summary>计划侧 LLM 容错别名表（attack→order_attack 等；PlanVocab.ActionAliases 派生源）。</summary>
        public static Dictionary<string, string> BuildAliases()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var s in All)
            {
                if (s.Aliases == null) continue;
                foreach (var al in s.Aliases)
                    map[al] = s.Code;
            }
            return map;
        }
        /// <summary>判定型/结算型动作的 result 允许集（PlanVocab.AllowedResultKeys 派生源）。</summary>
        public static Dictionary<string, HashSet<string>> BuildResultKeys()
        {
            var map = new Dictionary<string, HashSet<string>>();
            foreach (var s in All)
            {
                if (s.ResultKeys == null) continue;
                map[s.Code] = s.ResultKeys;
            }
            return map;
        }
        /// <summary>终态动作码集（end_plan；PlanVocab.TerminalActions 派生源）。</summary>
        public static HashSet<string> BuildTerminalCodes()
        {
            return new HashSet<string>(All.Where(s => s.IsTerminal).Select(s => s.Code), StringComparer.Ordinal);
        }
    }
}