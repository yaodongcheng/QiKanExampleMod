using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 对话触发来源。BuildScript 根据此枚举统一分派到对应的子树构建方法。
    /// </summary>
    public enum DialogueTrigger
    {
        Normal,           // 玩家主动交谈 → 按 speaker 身份分派（Authority/Witness/Suspect/Bystander）
        Alert,            // NPC 主动质问 → BuildAlertInterceptScript
        PlayerSurrender,  // 玩家认输 → BuildPlayerSurrenderScript
        NpcSurrender      // NPC 投降 → BuildNpcSurrenderScript
    }

    /// <summary>
    /// 犯罪对话构建器：运行时从游戏状态动态构建 DialogueInjectScript，
    /// 经 DialogueInjector.InjectScript 注入 ConversationManager。
    ///
    /// 替代手写 JSON 穷举——游戏状态组合爆炸。
    /// 三条路径同一出口：
    ///   路径 A（静态调试）: 手写 JSON → DialogueInjectScript
    ///   路径 B（生产）:  游戏状态 → CrimeDialogueBuilder.BuildScript → DialogueInjectScript
    ///   路径 C（LLM增强）: 游戏状态 → LLM生成 JSON → DialogueInjectScript
    /// </summary>
    public static class CrimeDialogueBuilder
    {
        // ═══════════════════════════════════════════════════════════════
        // P1: 共享 Node/Transition 工厂方法
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Node：NPC 说一句话。next 为 null 则关窗（terminal），非 null 则跳到 next</summary>
        static DialogueInjector.DialogueNode Node(string id, string npcLine, string next = null)
            => new()
            {
                Id = id,
                NpcLine = npcLine,
                Transitions = next != null ? SingleContinue(next) : new List<DialogueInjector.DialogueTransition>()
            };

        /// <summary>Lazy Node：NPC 说一句话（惰性求值）。next 为 null 则关窗（terminal），非 null 则跳到 next</summary>
        static DialogueInjector.DialogueNode LazyNode(string id, Func<string> lazyNpcLine, string next = null)
            => new()
            {
                Id = id,
                LazyNpcLine = lazyNpcLine,
                Transitions = next != null ? SingleContinue(next) : new List<DialogueInjector.DialogueTransition>()
            };

        static List<DialogueInjector.DialogueTransition> SingleContinue(string next)
        {
            // 玩家继续话题的通用确认语："嗯…"
            return new() { new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_continue", "Mm-hm."), NextNodeOnSuccess = next } };
        }

        /// <summary>
        /// 注入时机：玩家对 NPC 点"交谈"时调用。MissionConversationLogic.StartConversation、CampaignMapConversation.OpenConversation调用TryInjectCrimeDialogue
        /// 基于当前的settlement有没有相关活跃事件
        ///
        /// BuildScript 是犯罪对话的唯一分派点。四种触发场景在内部统一 switch：
        ///   Normal → 按 speaker 身份分派（Authority/Witness/Suspect/Bystander）
        ///   Alert → BuildAlertInterceptScript（NPC 主动质问）
        ///   PlayerSurrender → BuildPlayerSurrenderScript
        ///   NpcSurrender → BuildNpcSurrenderScript
        /// </summary>
        public static DialogueInjector.DialogueInjectScript BuildScript(
            Hero speaker, Hero listener, WorldEvent evt,
            DialogueTrigger trigger = DialogueTrigger.Normal,
            ConfrontationType? alertConfrontation = null,
            PlayerActionType? alertTriggerAction = null,
            CharacterObject speakerCharacter = null,
            Agent speakerAgent = null)
        {
            // ── 预填充阶段标记：本方法内所有 Resolve 都是对话开启时的整树预构建（非运行时播放），
            // 日志统一带 [对话预填充] 前缀；finally 保证任意 return 路径都还原 ──
            string prevLogPhaseTag = PlaceholderResolver.LogPhaseTag;
            PlaceholderResolver.LogPhaseTag = "对话预填充"; // lwn-ignore: A (debug tag)
            try
            {
                return BuildScriptInternal(speaker, listener, evt, trigger, alertConfrontation, alertTriggerAction, speakerCharacter, speakerAgent);
            }
            finally
            {
                PlaceholderResolver.LogPhaseTag = prevLogPhaseTag;
            }
        }

        /// <summary>BuildScript 的实际构建逻辑（trigger 分派 + 子树构建）。</summary>
        private static DialogueInjector.DialogueInjectScript BuildScriptInternal(
            Hero speaker, Hero listener, WorldEvent evt,
            DialogueTrigger trigger,
            ConfrontationType? alertConfrontation,
            PlayerActionType? alertTriggerAction,
            CharacterObject speakerCharacter = null,
            Agent speakerAgent = null)
        {
            // evt 为 null 时仅 Alert trigger 放行（纯警戒质问，无关联犯罪事件）
            if (evt == null && trigger != DialogueTrigger.Alert) return null;

            // ── trigger 优先分派 ──
            DialogueInjector.DialogueInjectScript result;
            switch (trigger)
            {
                case DialogueTrigger.Alert:
                    result = BuildAlertInterceptScriptInternal(speaker, listener, evt,
                        alertConfrontation, alertTriggerAction, speakerCharacter, speakerAgent);
                    break;

                case DialogueTrigger.PlayerSurrender:
                    result = BuildPlayerSurrenderScript();
                    break;

                case DialogueTrigger.NpcSurrender:
                    result = BuildNpcSurrenderScript();
                    break;

                default:
                    result = null;
                    break;
            }

            if (result != null)
            {
                DialogueInjector.LogScript(result, $"[CrimeDialog] trigger={trigger} speaker={speaker?.Name?.ToString() ?? "(template)"} stage={evt?.Stage.ToString() ?? "none"}");
                return result;
            }

            // ── Normal：按 speaker 身份分派 ──
            PlaceholderResolver r = new PlaceholderResolver(evt, speaker, listener, speakerCharacter);
            Agent conversationAgent = TaleWorlds.CampaignSystem.Campaign.Current?.ConversationManager?.OneToOneConversationAgent as Agent;
            IntentContext ctx = new IntentContext(conversationAgent, speaker: speaker, worldEvent: evt);

            // ── 模板 NPC（speaker==null）兼容性审计 ──
            //   IsAuthority → null-safe（npc?.Occupation），模板 NPC 永远不命中 ✅
            //   Witness    → speaker.StringId 匹配证词 + SilenceWitness 记录身份
            //                模板 NPC 可当目击者（RegisterWitness 已支持 TemplateId），
            //                但 BuildWitnessScript 未适配 → 暂落 Bystander ⚠️ TODO
            //   Suspect    → SuspectHeroId 是 Hero StringId，模板 NPC 当嫌疑人需改
            //                数据模型 → 暂落 Bystander ⚠️
            //   Bystander  → 全程 PlaceholderResolver，完全兼容 ✅
            //   扩展方式：加 TemplateId 匹配的 else if 即可，不需改结构。
            if (IsAuthority(speaker, evt))                             // null-safe: npc?.Occupation
                result = BuildAuthorityScript(r, ctx);
            else if (evt.WitnessHeroIds?.Contains(speaker?.StringId) == true)  // 🆕 speaker?
                result = BuildWitnessScript(r, ctx);                   // ⚠️ 仅 Hero 目击者
            else if (evt.SuspectHeroId == speaker?.StringId)                  // 🆕 speaker?
                result = BuildSuspectScript(r, ctx);                   // ⚠️ 仅 Hero 嫌疑人
            else
                result = BuildBystanderScript(r, ctx);                 // 自然兜底（模板 NPC ✅）

            DialogueInjector.LogScript(result, $"[CrimeDialog] speaker={speaker?.Name?.ToString() ?? "(template)"} stage={evt.Stage}");
            return result;
        }

        /// <summary>Alert 路径的内部适配：从原始参数构建 PlaceholderResolver + IntentContext，调 BuildAlertInterceptScript。
        /// evt 可为 null（纯警戒质问，无关联犯罪事件）。</summary>
        private static DialogueInjector.DialogueInjectScript BuildAlertInterceptScriptInternal(
            Hero speaker, Hero listener, WorldEvent evt,
            ConfrontationType? confrontation, PlayerActionType? triggerAction,
            CharacterObject speakerCharacter = null, Agent speakerAgent = null)
        {
            // evt 为 null 时用无 WorldEvent 的 PlaceholderResolver 构造器，{CRIME} 等占位符回落空串
            var r = evt != null
                ? new PlaceholderResolver(evt, speaker, listener, speakerCharacter)
                : new PlaceholderResolver(speaker, listener, targetName: null, itemName: null, speakerCharacter);

            var agent = speakerAgent
                ?? TaleWorlds.CampaignSystem.Campaign.Current?.ConversationManager?.OneToOneConversationAgent as Agent
                ?? AlertForceConversationAction.ActiveConversationAgent;  // Prefix 阶段 ConversationManager 尚未就绪，兜底用我们自己的引用

            // 目击者匹配：Hero 按 StringId；模板 NPC（speaker==null）按 CharacterObject.StringId ↔ TemplateId，
            // 不能再 t.WitnessHeroId == null 乱匹配（会错拿别的模板 NPC 的证词）
            string speakerTemplateId = speaker == null ? agent?.Character?.StringId : null;
            var allTestimonies = AgentAIController.Instance?.PendingWorldEvent?.WitnessTestimonies;
            DebugLogger.Log($"[Placeholder] SpeakingWitness match: speaker={speaker?.StringId ?? "null"} speakerTemplateId={speakerTemplateId} totalTestimonies={allTestimonies?.Count ?? 0}" +
                (allTestimonies != null ? $" ids=[{string.Join(", ", allTestimonies.Select(t => $"{t.WitnessHeroId ?? t.TemplateId ?? "dark"}:actions={t.Actions?.Count ?? 0}:first={t.Actions?.FirstOrDefault()?.ActionType}={t.Actions?.FirstOrDefault()?.TargetName}"))}]" : ""));
            r.SpeakingWitness = null;
            if (allTestimonies != null)
            {
                string spId = speaker?.StringId;
                foreach (var t in allTestimonies)
                {
                    bool matchHero = spId != null && t.WitnessHeroId == spId;
                    bool matchTpl = speakerTemplateId != null && t.TemplateId == speakerTemplateId;
                    DebugLogger.Log($"[Placeholder] SpeakingWitness try: t.WitnessHeroId={t.WitnessHeroId ?? "null"} t.TemplateId={t.TemplateId ?? "null"} spId={spId ?? "null"} spTplId={speakerTemplateId ?? "null"} matchHero={matchHero} matchTpl={matchTpl}");
                    if (matchHero || matchTpl)
                    {
                        r.SpeakingWitness = t;
                        break;
                    }
                }
            }

            // 脉冲上下文回填：{TARGET}/{ITEM}（击晕受害者名、被盗物品名）。
            // 质问者自己 Brain 的警戒明细最精准，填补 PlaceholderResolver 拿不到 pulse 的缺口
            var action = triggerAction ?? PlayerActionType.Crouching;
            var brain = agent != null ? AgentAIController.GetBrainForAgent(agent) : null;
            if (brain?.AlertBreakdown != null && brain.AlertBreakdown.TryGetValue(action, out var pulse))
            {
                r.TargetName = r.TargetName ?? pulse.TargetName;
                r.ItemName = r.ItemName ?? pulse.ItemName;
            }

            var ctx = new IntentContext(agent, speaker: speaker, worldEvent: evt);
            ctx.Confrontation = confrontation ?? ConfrontationType.Deter;
            ctx.TriggerAction = action;

            return BuildAlertInterceptScript(r, ctx);
        }

        private static bool IsAuthority(Hero npc, WorldEvent evt)
        {
            Hero authority = WorldEventStore.GetAuthorityNpc(evt);
            return npc == authority || (npc?.Occupation == Occupation.Headman || npc?.Occupation == Occupation.RuralNotable);
        }

        /// <summary>嫌疑犯是玩家队伍随从（Phase F 大义灭亲对话触发判定）。</summary>
        static bool IsCompanionSuspect(WorldEvent evt)
        {
            if (evt == null || string.IsNullOrEmpty(evt.SuspectHeroId)) return false;
            var suspect = Hero.FindFirst(h => h.StringId == evt.SuspectHeroId);
            return suspect != null && FriendlinessHelper.IsPlayerPartyMember(suspect);
        }

        /// <summary>嫌疑=随从的 Hero（大义灭亲对话结算用；未知 → null）。</summary>
        static Hero CompanionSuspectHero(WorldEvent evt)
        {
            if (evt == null || string.IsNullOrEmpty(evt.SuspectHeroId)) return null;
            return Hero.FindFirst(h => h.StringId == evt.SuspectHeroId);
        }

        /// <summary>
        /// 该对话是否会生成 SkipVanillaOpening 脚本（= 必须在 StartConversation 评估 start token 之前注入）。
        /// 与 BuildAuthorityScript 的 skipOpening 条件同源——权威 NPC 在 Active+嫌犯=玩家 / Confrontation
        /// 阶段会把开场白直挂 start token（优先级 200）。Postfix 注入时 start 已被原版评估完毕，
        /// 注入的开场白永远不会播放、hero_main_options 也无入口，整场对话退化为纯原版，
        /// 玩家可经原版"我现在得走了"零后果离开。此谓词供 MissionConversationStartPatch.Prefix 提前注入用。
        /// 🔴 2026-08-13 Phase F：嫌疑=玩家队伍随从（大义灭亲对话）同样 SkipVanillaOpening。
        /// </summary>
        public static bool NeedsEarlyInjection(Hero speaker, WorldEvent evt)
        {
            if (evt == null) return false;
            if (!IsAuthority(speaker, evt)) return false;
            return (evt.Stage == EventStage.Active && (evt.SuspectIsPlayer || IsCompanionSuspect(evt)))
                || evt.Stage == EventStage.Confrontation;
        }

        private static DialogueInjector.DialogueInjectScript BuildAuthorityScript(
            PlaceholderResolver r, IntentContext ctx)
        {
            List<DialogueInjector.DialogueNode> nodes = new List<DialogueInjector.DialogueNode>();
            WorldEvent evt = ctx.ActiveEvent;
            // 权威NPC开场白（默认/沉睡期）：听说出事，引出案件话题
            string entryOption = LWNTextHelper.Resolve("LWN_crime_authority_entry_dormant", r,
                "{SpeakerRole}, I hear something happened in {TargetSettlementName}?");
            switch (evt.Stage)
            {
                case EventStage.Dormant:
                    //如果案件本身还没被发现，那么也就没有特殊的案件对话
                    break;
                case EventStage.Emerging:
                    //案件已经被发现了，但是还不知道谁干的
                    if (evt.PlayerTookInvestigationQuest)
                    {
                        //玩家接了调查任务，那么对话就是关于任务情况的报告
                        // 权威NPC开场白（Emerging+玩家已接调查任务）：直奔案件话题
                        entryOption = LWNTextHelper.Resolve("LWN_crime_authority_entry_report", r,
                            "About that case in {TargetSettlementName}...");
                        BuildReportNode(nodes, r, ctx);
                    }
                    else
                    {
                        //玩家没有接调查任务,请求玩家调查 — entryOption 沿用第 199 行的默认值（同一模板，无需重复 Resolve）
                        BuildDiscoveryNode(nodes, r, ctx);
                    }
                    break;
                case EventStage.Active:
                    //案件已经知道是谁干的了（怀疑）
                    if (evt.SuspectIsPlayer)
                    {
                        //怀疑是玩家干的 — NPC 锁定玩家身份，不设 EntryOption，SkipVanillaOpening=true
                        BuildConfrontPlayerNode(nodes, r, ctx);
                    }
                    else if (IsCompanionSuspect(evt))
                    {
                        // 🔴 Phase F（2026-08-13）：嫌疑=玩家队伍随从 → 大义灭亲对话
                        //（交出随从 / 替随从赔钱 / 拒不认账，铁律 12 每个出口有代价）
                        BuildCompanionCrimeNode(nodes, r, ctx);
                    }
                    else
                    {
                        //是别的人干的，请求玩家去帮忙
                        // 权威NPC开场白（Active+非玩家作案）：提起悬赏
                        entryOption = LWNTextHelper.Resolve("LWN_crime_authority_entry_bounty", r,
                            "{SpeakerRole}, about that bounty...");
                        BuildBountyOfferNode(nodes, r, ctx);
                    }
                    break;
                case EventStage.Confrontation:
                    //是玩家干的，和玩家对峙 — NPC 已动员武力，不设 EntryOption，SkipVanillaOpening=true
                    {
                        BuildRetaliationNode(nodes, r, ctx);
                    }
                    break;
                case EventStage.Resolved:
                case EventStage.Unsolved:
                    //解决了，或者没解决，都没有对话
                    break;

            }           

            // NPC 锁定玩家身份 → SkipVanillaOpening=true，直接说正事；否则保留原版开场白
            // 🔴 Phase F：嫌疑=玩家随从（大义灭亲对话）同样跳过原版开场白
            bool skipOpening = (evt.Stage == EventStage.Active && (evt.SuspectIsPlayer || IsCompanionSuspect(evt)))
                            || evt.Stage == EventStage.Confrontation;

            return new DialogueInjector.DialogueInjectScript
            {
                SkipVanillaOpening = skipOpening,
                EntryOption = skipOpening ? null : entryOption,
                EntryNode = "injectedStart",
                Nodes = nodes
            };
        }

        private static void BuildDiscoveryNode(List<DialogueInjector.DialogueNode> nodes, PlaceholderResolver r, IntentContext ctx)
        {
            WorldEvent evt = r.Event;
            DialogueInjector.DialogueNode node = new DialogueInjector.DialogueNode
            {
                Id = "injectedStart",
                // 权威NPC委托调查开场：陈述案件事实，请玩家帮忙查
                NpcLine = LWNTextHelper.Resolve("LWN_crime_authority_discovery_opening", r,
                    "{TimeWord} something happened in {TargetSettlementName} — {DiscoveryFacts}. {InvestigationProgressWord}. {WitnessCountWord}, {SuspectDescription}. Can {SpeakerPlayerAddr} help look into it?"),
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    new DialogueInjector.DialogueTransition
                    {
                        // 玩家接受调查委托
                        PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_discovery_accept", "I can help find out who did it."),
                        Action = "INTENT:Investigate",
                        NextNodeOnSuccess = "discovery_accept_ack"
                    },
                    new DialogueInjector.DialogueTransition
                    {
                        // 玩家婉拒调查委托（走人）
                        PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_busy", "I have other matters."),
                        Action = "INTENT:WalkAway",
                        NextNodeOnSuccess = "discovery_decline_ack"
                    }
                }
            };

            // 如果玩家是贼 → 加"主动认栽"选项
            if (evt.InitiatorIsPlayer)
            {
                node.Transitions.Insert(0, new DialogueInjector.DialogueTransition
                {
                    // 玩家主动认罪（调查阶段）
                    PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_confess", "It was me."),
                    Action = "INTENT:Confess",
                    NextNodeOnSuccess = "confess"
                });
                BuildConfessSubtree(nodes, r, ctx);
            }

            nodes.Add(node);
            // 权威NPC：接受调查的回应（承诺重谢）
            nodes.Add(Node("discovery_accept_ack", LWNTextHelper.Resolve("LWN_crime_authority_discovery_accept_ack", r,
                "Please! Find the culprit and {SpeakerSelfRef} will reward you well."), "continue_chat"));
            // 权威NPC：拒绝调查的回应（放行）
            nodes.Add(Node("discovery_decline_ack", LWNTextHelper.Resolve("LWN_crime_authority_discovery_decline_ack", r,
                "Then go about {SpeakerPlayerAddr}'s business... {SpeakerSelfRef} will manage on their own."), "continue_chat"));
            AddContinueChatWithFarewell(nodes, r);
        }

        /// <summary>
        /// 构建"认栽"对话子树：confess → 赔钱/狡辩/走人 → charm 回应 / restitution_demand / restitution_pay_ack。
        /// 自包含所有下游节点，调用方只需一行 BuildConfessSubtree(nodes, r, ctx)。
        /// </summary>
        private static void BuildConfessSubtree(List<DialogueInjector.DialogueNode> nodes, PlaceholderResolver r, IntentContext ctx)
        {
            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "confess",
                // 权威NPC：玩家主动认罪后的回应（可以商量）
                NpcLine = LWNTextHelper.Resolve("LWN_crime_authority_confess_opening", r,
                    "{SpeakerPlayerAddr}?! ...Very well, since you admit it, we can talk. What do you have to say?"),
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    new()
                    {
                        // 玩家认赔（不标价，由 NPC 在 restitution_demand 开价）
                        PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_confess_pay", "I'll pay. Name your price."),
                        Action = "NONE",
                        NextNodeOnSuccess = "restitution_demand"
                    },
                    new()
                    {
                        // 玩家试图狡辩（魅力检定）
                        PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_confess_joke", "Just kidding... I was talking nonsense."),
                        CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                        Action = "INTENT:CharmDefense",
                        NextNodeOnSuccess = "charm_ok",
                        NextNodeOnFail = "charm_fail"
                    },
                    // 玩家转身就走（承担关系后果）
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_turn_and_leave", "Turn and leave"), Action = "INTENT:WalkAway", NextNodeOnSuccess = "" },
                }
            });
            // 权威NPC：狡辩成功（愿意听解释）
            nodes.Add(Node("charm_ok", LWNTextHelper.Resolve("LWN_crime_authority_confess_charm_ok", r,
                "...Explain yourself? Very well, {SpeakerSelfRef} will listen."), "continue_chat"));
            // 权威NPC：狡辩失败（证据确凿）
            nodes.Add(Node("charm_fail", LWNTextHelper.ResolveText("LWN_crime_authority_charm_fail", "Explain? The evidence is conclusive. Nothing to say."), "continue_chat"));
            BuildRestitutionSubtree(nodes, r, ctx);
        }

        /// <summary>
        /// 🔴【赔偿对话唯一入口】NPC 开价 → 全价付 / 砍价 / 放弃 → 付款确认。
        ///
        /// 所有赔钱路径必须路由到这里——玩家先说"我愿意赔偿"（不标价）→ 跳转 restitution_demand
        /// → NPC 算账开价（明细+倍率+总价）→ 玩家接受/砍价/拒绝。
        ///
        /// ⚠️ 禁止在任何新对话子树中：
        ///   - 让玩家台词说出具体金额（"我赔500第纳尔"❌）
        ///   - 直接用 INTENT:PayRestitution 跳过 NPC 开价环节
        ///   - 在 NPC 没开价前就扣钱
        ///
        /// 正确写法（新子树）：
        ///   1. 玩家选项：PlayerLine="我愿意赔偿。" Action="NONE" NextNodeOnSuccess="restitution_demand"
        ///   2. 子树末尾调：BuildRestitutionSubtree(nodes, r, ctx)
        ///
        /// 依赖调用方已添加 continue_chat / farewell。
        /// </summary>
        private static void BuildRestitutionSubtree(List<DialogueInjector.DialogueNode> nodes, PlaceholderResolver r, IntentContext ctx,
            string afterPayNodeId = "continue_chat")
        {
            WorldEvent evt = r.Event;
            int cost = CrimePenaltyCalculator.ComputeCost(evt, CostType.Restitution);
            int haggleCost = CrimePenaltyCalculator.ComputeHaggleAmount(cost, 0.5f);

            // NPC 开价台词：优先用 RestitutionBreakdown，否则兜底
            string demandNpcLine = r.Resolve("{RestitutionBreakdown}", "NpcLine");
            if (string.IsNullOrEmpty(demandNpcLine) || demandNpcLine == "{RestitutionBreakdown}")
                // 兜底开价台词：报出罚款金额，问玩家认不认
                demandNpcLine = LWNTextHelper.ResolveCompound("LWN_crime_common_demand_fallback",
                    "The fine is {COST} denars. Do you accept?",
                    ("COST", cost.ToString()));

            // demand 节点：NPC 开价
            // 🆕 惰性求值：首次进入用原价 cost，砍价成功后再进来 → 沿用砍后价 _hagglePrice，NPC 交代"刚才砍过一轮"
            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "restitution_demand",
                LazyNpcLine = () =>
                {
                    int currentCost = evt?._hagglePrice > 0 ? evt._hagglePrice : cost;
                    evt?.RecordQuote(currentCost);
                    if (evt?._hagglePrice > 0)
                    {
                        // 砍过价后再来：NPC 重申砍后价（强调价格不变）
                        return LWNTextHelper.ResolveCompound("LWN_crime_common_demand_after_haggle",
                            "We bargained down to {COST} denars and you refused. Same price — {COST} denars. Do you accept?",
                            ("COST", currentCost.ToString()));
                    }
                    return demandNpcLine;
                },
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    // 玩家全价接受
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_demand_accept", "Fine, deal."), Action = "INTENT:PayRestitution", ActionParam = null, NextNodeOnSuccess = "restitution_pay_ack" },
                    // 玩家砍价（说服检定）
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_demand_haggle", "Too expensive. Can you lower it?"), CheckType = DialogueInjector.TransitionCheckType.SkillCheck, Action = "INTENT:Settle", NextNodeOnSuccess = "restitution_haggle_ok", NextNodeOnFail = "restitution_haggle_fail" },
                    // 玩家拒赔
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_demand_refuse", "Too expensive. I won't pay."), Action = "NONE", NextNodeOnSuccess = "restitution_refuse_warn" },
                }
            });

            // refuse_warn：拒赔的当场就把代价说清楚 —— 这个价不是永远有效的
            // （赔款随案件阶段上浮：Emerging ×0.7 → Active ×1.0 → Confrontation ×1.7，
            //   玩家事后在地牢里看到翻倍的数字，得是"我知道会这样"而不是"系统坑我"）
            // 🆕 惰性求值：如果前面砍过价（LastQuotedAmount 已更新为砍后价），用砍后价；
            //   如果直接从 demand 来的，用原价 cost。避免"砍到1680→不赔→NPC又说3360"的割裂。
            nodes.Add(LazyNode("restitution_refuse_warn",
                () =>
                {
                    int warnCost = evt?.LastQuotedAmount > 0 ? evt.LastQuotedAmount : cost;
                    // 拒赔警告：强调现在给是这个价，往后只会翻倍
                    return LWNTextHelper.ResolveCompound("LWN_crime_common_refuse_warn",
                        "Refuse? {SELF} will say this plainly: pay {COST} now. Once word spreads, once we come to blows, the price will only climb. Think carefully.",
                        ("SELF", r.ResolveOne("SpeakerSelfRef")), ("COST", warnCost.ToString()));
                },
                "continue_chat"));

            // haggle_ok：砍价成功 — 明盘：原价多少、怎么砍的、砍到多少
            string haggleNpcLine = LWNTextHelper.ResolveCompound("LWN_crime_common_haggle_ok",
                "Hmph... original price {COST}. I'll cut it in half for you — {HAGGLECOST}. Final price, no more discounts.",
                ("COST", cost.ToString()), ("HAGGLECOST", haggleCost.ToString()));
            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "restitution_haggle_ok",
                NpcLine = haggleNpcLine,
                LazyNpcLine = () => { evt!.RecordQuote(haggleCost); evt._hagglePrice = haggleCost; return haggleNpcLine; },
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    // 玩家接受砍后价
                    new() { PlayerLine = LWNTextHelper.ResolveCompound("LWN_crime_player_haggle_accept", "Fine — {COST} denars it is.", ("COST", haggleCost.ToString())), Action = "INTENT:PayRestitution", ActionParam = "haggle", NextNodeOnSuccess = "restitution_pay_ack" },
                    // 玩家仍嫌贵，不赔
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_haggle_refuse", "Still too expensive. I'm not paying."), Action = "NONE", NextNodeOnSuccess = "restitution_refuse_warn" },
                }
            });

            // haggle_fail → 回到 demand
            // 砍价失败：一分不让
            nodes.Add(Node("restitution_haggle_fail", LWNTextHelper.ResolveText("LWN_crime_common_haggle_fail", "No. Not a single coin less."), "restitution_demand"));

            // pay_ack：付款确认
            // 付款确认：钱留下，事两清
            nodes.Add(Node("restitution_pay_ack", LWNTextHelper.ResolveText("LWN_crime_common_pay_ack", "Good. Leave the money, and this matter is settled."), afterPayNodeId));
        }

        private static void BuildReportNode(List<DialogueInjector.DialogueNode> nodes, PlaceholderResolver r, IntentContext ctx)
        {

            WorldEvent evt = r.Event;
            DialogueInjector.DialogueNode node = new DialogueInjector.DialogueNode
            {
                Id = "injectedStart",
                // 权威NPC：问玩家调查进度
                NpcLine = LWNTextHelper.ResolveText("LWN_crime_authority_report_opening", "Well? Found anything?"),
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    new DialogueInjector.DialogueTransition
                    {
                        // 玩家栽赃藏身处强盗（说服检定）
                        PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_report_frame_bandit", "It was the bandits from the nearby hideout!"),
                        CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                        Action = "INTENT:FrameSuspect",
                        ActionParam = "bandit",
                        NextNodeOnSuccess = "frame_bandit_ok",
                        NextNodeOnFail = "frame_bandit_fail"
                    },
                    // 玩家暂无进展
                    new DialogueInjector.DialogueTransition { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_report_nothing", "Nothing yet."), Action = "NONE", NextNodeOnSuccess = "report_nothing_ack" },
                    new DialogueInjector.DialogueTransition
                    {
                        // 玩家告辞
                        PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_busy", "I have other matters."),
                        Action = "INTENT:WalkAway",
                        NextNodeOnSuccess = "report_leave_ack"
                    },
                }
            };

            // 动态生成栽赃候选
            List<FrameSubOption> frameTargets = TheftLedger.GetFrameableTargets();
            int frameIdx = 0;
            foreach (FrameSubOption target in frameTargets.Skip(1)) // Skip "bandit" (already above)
            {
                if (target.CanShowEvidence)
                {
                    List<EvidenceItem> evidenceItems = TheftLedger.GetEvidenceItems(target.TargetId);
                    foreach (EvidenceItem evItem in evidenceItems)
                    {
                        string okId = $"frame_{frameIdx}_ok";
                        string failId = $"frame_{frameIdx}_fail";
                        node.Transitions.Insert(node.Transitions.Count - 1, new DialogueInjector.DialogueTransition
                        {
                            // 玩家拿证据栽赃目标（[出示{ITEM}] 是证据展示框）
                            PlayerLine = LWNTextHelper.ResolveCompound("LWN_crime_player_frame_evidence",
                                "It was {NAME} — [shows {ITEM}]",
                                ("NAME", target.DisplayName), ("ITEM", evItem.ItemName)),
                            CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                            Action = "INTENT:FrameSuspect",
                            ActionParam = target.TargetId,
                            NextNodeOnSuccess = okId,
                            NextNodeOnFail = failId
                        });
                        // 权威NPC：证据坐实目标
                        nodes.Add(Node(okId, LWNTextHelper.ResolveText("LWN_crime_authority_frame_ok", "...This is indeed his. Alright, he's the one!"), "continue_chat"));
                        // 权威NPC：证据不足，继续查
                        nodes.Add(Node(failId, LWNTextHelper.ResolveCompound("LWN_crime_authority_frame_fail_evidence",
                            "This proves nothing. {ADDR}, keep investigating.",
                            ("ADDR", r.ResolveOne("SpeakerPlayerAddr"))), "continue_chat"));
                        frameIdx++;
                    }
                }
                else
                {
                    string okId = $"frame_{frameIdx}_ok";
                    string failId = $"frame_{frameIdx}_fail";
                    node.Transitions.Insert(node.Transitions.Count - 1, new DialogueInjector.DialogueTransition
                    {
                        // 玩家口头指认目标（无证据）
                        PlayerLine = LWNTextHelper.ResolveCompound("LWN_crime_player_frame_simple",
                            "It was {NAME}.",
                            ("NAME", target.DisplayName)),
                        CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                        Action = "INTENT:FrameSuspect",
                        ActionParam = target.TargetId,
                        NextNodeOnSuccess = okId,
                        NextNodeOnFail = failId
                    });
                    // 权威NPC：采信口头指认
                    nodes.Add(Node(okId, LWNTextHelper.ResolveCompound("LWN_crime_authority_frame_ok_simple",
                        "It was {NAME}? ...Very well, {SELF} believes you.",
                        ("NAME", target.DisplayName), ("SELF", r.ResolveOne("SpeakerSelfRef"))), "continue_chat"));
                    // 权威NPC：单凭一句话不予采信
                    nodes.Add(Node(failId, LWNTextHelper.ResolveCompound("LWN_crime_authority_frame_fail_simple",
                        "Just a word? {ADDR}, keep investigating.",
                        ("ADDR", r.ResolveOne("SpeakerPlayerAddr"))), "continue_chat"));
                    frameIdx++;
                }
            }

            // 如果玩家是贼 → 加"主动认栽"
            if (evt.InitiatorIsPlayer)
            {
                node.Transitions.Add(new DialogueInjector.DialogueTransition
                {
                    // 玩家低头认罪（报告阶段）
                    PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_report_confess", "(head down) ...It was me."),
                    Action = "INTENT:Confess",
                    NextNodeOnSuccess = "confess"
                });
                BuildConfessSubtree(nodes, r, ctx);
            }

            nodes.Add(node);
            // 权威NPC：采信强盗说法，张罗悬赏
            nodes.Add(Node("frame_bandit_ok", LWNTextHelper.Resolve("LWN_crime_authority_frame_bandit_ok", r,
                "The hideout bandits? Alright, it's them then! {SpeakerSelfRef} will set up a bounty right away."), "continue_chat"));
            // 权威NPC：强盗说法缺证据
            nodes.Add(Node("frame_bandit_fail", LWNTextHelper.Resolve("LWN_crime_authority_frame_bandit_fail", r,
                "Bandits? Just {SpeakerPlayerAddr}'s word is not enough... go investigate again."), "continue_chat"));
            // 权威NPC：没查到就继续查
            nodes.Add(Node("report_nothing_ack", LWNTextHelper.Resolve("LWN_crime_authority_report_nothing_ack", r,
                "Then take another look. {InvestigationProgressWord}."), "continue_chat"));
            // 权威NPC：催促玩家快查
            nodes.Add(Node("report_leave_ack", LWNTextHelper.Resolve("LWN_crime_common_go_investigate", r,
                "Go investigate, and report back to {SpeakerSelfRef} when you have news."), "continue_chat"));
            AddContinueChatWithFarewell(nodes, r);
        }

        private static void BuildConfrontPlayerNode(List<DialogueInjector.DialogueNode> nodes, PlaceholderResolver r, IntentContext ctx)
        {
            WorldEvent evt = r.Event;
            // 真场景才能叫守卫/当场开打；大地图（含临时对话 Mission）威胁失败的 NPC 回应降级为口头警告
            // 权威NPC：真场景威胁失败（叫守卫）
            string threatFailLine = ctx.InRealScene
                // 威胁{SpeakerSelfRef}？来人！
                ? LWNTextHelper.Resolve("LWN_crime_authority_confront_threat_fail_scene", r,
                    "Threaten {SpeakerSelfRef}? Guards!")
                // 权威NPC：大地图威胁失败（口头警告，告到上面去）
                : LWNTextHelper.Resolve("LWN_crime_authority_confront_threat_fail_map", r,
                    "Threaten {SpeakerSelfRef}? {SpeakerPlayerAddr} just wait — {SpeakerSelfRef} will report this to the higher-ups.");
            DialogueInjector.DialogueNode node = new DialogueInjector.DialogueNode
            {
                Id = "injectedStart",
                // 权威NPC对峙开场：证人指认玩家作案
                NpcLine = LWNTextHelper.Resolve("LWN_crime_authority_confront_opening", r,
                    "{SpeakerPlayerAddr} dares to show up? {PrimaryWitnessDesc} came to {SpeakerSelfRef} {TimeWord}, saying they saw with their own eyes that {SpeakerPlayerAddr} {ActionDescription}. Anything to say?"),
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    new DialogueInjector.DialogueTransition
                    {
                        // 玩家辩解（魅力检定）
                        PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_confront_charm", "You've got it wrong. Give me a chance to explain."),
                        CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                        Action = "INTENT:CharmDefense",
                        NextNodeOnSuccess = "confront_charm_ok",
                        NextNodeOnFail = "confront_charm_fail"
                    },
                    new DialogueInjector.DialogueTransition
                    {
                        // 玩家问赔偿金额（不标价，由 NPC 开价）
                        PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_confront_pay", "About restitution... how much?"),
                        Action = "NONE",
                        NextNodeOnSuccess = "restitution_demand"
                    },
                    new DialogueInjector.DialogueTransition
                    {
                        // 玩家反威胁（威胁检定）
                        PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_confront_threat", "Say that again?"),
                        CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                        Action = "INTENT:Threat",
                        NextNodeOnSuccess = "confront_threat_ok",
                        NextNodeOnFail = "confront_threat_fail"
                    },
                    // 玩家转身就走（承担后果）
                    new DialogueInjector.DialogueTransition { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_turn_and_leave", "Turn and leave"), Action = "INTENT:WalkAway", NextNodeOnSuccess = "" },
                }
            };
            nodes.Add(node);

            // ack nodes
            // 权威NPC：辩解成功，愿意再查
            nodes.Add(Node("confront_charm_ok", LWNTextHelper.Resolve("LWN_crime_authority_confront_charm_ok", r,
                "...{SpeakerPlayerAddr} makes some sense. {SpeakerSelfRef} will investigate further."), "continue_chat"));
            // 权威NPC：辩解失败，证据确凿
            nodes.Add(Node("confront_charm_fail", LWNTextHelper.ResolveText("LWN_crime_authority_charm_fail", "Explain? The evidence is conclusive. Nothing to say."), "continue_chat"));
            // 权威NPC：威胁成功，放玩家走
            nodes.Add(Node("confront_threat_ok", LWNTextHelper.Resolve("LWN_crime_authority_confront_threat_ok", r,
                "...{SpeakerSelfRef} will say no more. {SpeakerPlayerAddr}, go."), "continue_chat"));
            nodes.Add(Node("confront_threat_fail", threatFailLine, "continue_chat"));

            BuildRestitutionSubtree(nodes, r, ctx);
            AddContinueChatWithFarewell(nodes, r);
        }

        private static void BuildBountyOfferNode(List<DialogueInjector.DialogueNode> nodes, PlaceholderResolver r, IntentContext ctx)
        {
            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "injectedStart",
                // 权威NPC：说明案件已查清，提出悬赏邀请
                NpcLine = LWNTextHelper.Resolve("LWN_crime_authority_bounty_opening", r,
                    "Remember {TimeWord} {DiscoveryFacts}? We've got it figured out — it was {SuspectDescription}. The village pooled {BountyAmount} denars as a bounty; whoever brings them back gets it. Will {SpeakerPlayerAddr} take it?"),
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    // 玩家接下悬赏
                    new DialogueInjector.DialogueTransition { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_bounty_accept", "I'll take the bounty!"), Action = "INTENT:AcceptBountyQuest", NextNodeOnSuccess = "bounty_accept_ack" },
                    // 玩家暂不表态
                    new DialogueInjector.DialogueTransition { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_bounty_decline", "Let me think about it."), Action = "NONE", NextNodeOnSuccess = "continue_chat" },
                }
            });
            // 权威NPC：接受悬赏的回应（人交给你了）
            nodes.Add(Node("bounty_accept_ack", LWNTextHelper.Resolve("LWN_crime_authority_bounty_accept_ack", r,
                "Good! The matter is in {SpeakerPlayerAddr}'s hands."), "continue_chat"));
            AddContinueChatWithFarewell(nodes, r);
        }

        /// <summary>
        /// 大义灭亲对话（Phase F，2026-08-13，hud-intent-unify-alert-suspect.md §2.9）：
        /// 嫌疑=玩家队伍随从的犯罪事件（随从犯法后跟玩家跑了，Mission 内未被抓）→ 玩家与权威 NPC 对话时
        /// 出现「随从犯法」话题。权威 NPC 开场「你的随从 {NAME} 偷了我的东西！」→ 三出口（铁律 12）：
        ///   A 交出随从（大义灭亲）：TakePrisonerAction.Apply(settlement.Party, companion) + 事件 Resolved + 好感影响
        ///   B 替随从赔钱：BuildRestitutionSubtree 复用（赔款从玩家金库扣，AgentControlHelper 归口）
        ///   C 拒不认账：关系惩罚（权威对玩家好感下降）
        /// </summary>
        static void BuildCompanionCrimeNode(List<DialogueInjector.DialogueNode> nodes, PlaceholderResolver r, IntentContext ctx)
        {
            WorldEvent evt = r.Event;
            Hero companion = CompanionSuspectHero(evt);
            string companionName = companion?.Name?.ToString()
                ?? LWNTextHelper.ResolveText("LWN_crime_suspect_unknown", "that companion of yours");

            DialogueInjector.DialogueNode node = new DialogueInjector.DialogueNode
            {
                Id = "injectedStart",
                // 权威NPC开场（随从犯案）：你的随从 {NAME} 偷了我的东西！你怎么说？
                NpcLine = LWNTextHelper.ResolveCompound("LWN_dialogue_companion_crime_opening",
                    "Your companion {NAME} stole my things! What do you have to say for yourself?",
                    ("NAME", companionName)),
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    // A 交出随从（大义灭亲）——代价 = 失去随从（被关进牢房）
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_dialogue_companion_crime_handover", "Take him. He is yours."), Action = "NONE", NextNodeOnSuccess = "companion_handover_ack" },
                    // B 替随从赔钱（不标价，由 NPC 在 restitution_demand 开价——赔偿对话纪律）
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_dialogue_companion_crime_pay", "I'll make it right with gold."), Action = "NONE", NextNodeOnSuccess = "restitution_demand" },
                    // C 拒不认账——代价 = 关系惩罚
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_dialogue_companion_crime_deny", "He is not mine. I do not know him."), Action = "NONE", NextNodeOnSuccess = "companion_deny_ack" },
                }
            };
            nodes.Add(node);

            // A ack：交人成功——关进牢房 + 事件 Resolved（副作用在首次求值时执行一次）
            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "companion_handover_ack",
                LazyNpcLine = () =>
                {
                    HandoverCompanion(evt, companion);
                    // 权威NPC：人归我们，这事就算了
                    return LWNTextHelper.ResolveText("LWN_dialogue_companion_crime_handover_ack", "Good. Justice is served, and this matter is settled.");
                },
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_leave", "I'm leaving."), Action = "NONE", NextNodeOnSuccess = "" },
                }
            });

            // C ack：拒不认账——NPC 不信（当众看见随从跟着玩家）
            nodes.Add(Node("companion_deny_ack", LWNTextHelper.ResolveText("LWN_dialogue_companion_crime_deny_ack", "Liar! The whole village saw him following you!"), "companion_deny_result"));
            // C 结果：关系惩罚 + 放行（代价已付）
            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "companion_deny_result",
                LazyNpcLine = () =>
                {
                    ApplyDenyConsequence(evt);
                    // 权威NPC：……行。那你走吧。让他别再踏进这里。
                    return LWNTextHelper.ResolveText("LWN_dialogue_companion_crime_deny_ok", "...Very well. Leave, then. But keep him away from here.");
                },
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_leave", "I'm leaving."), Action = "NONE", NextNodeOnSuccess = "" },
                }
            });

            BuildRestitutionSubtree(nodes, r, ctx);
            AddContinueChatWithFarewell(nodes, r);
        }

        /// <summary>交出随从（大义灭亲结算）：关进事件定居点牢房（原版 hero 俘虏机制）+ 事件 Resolved + 提示。</summary>
        static void HandoverCompanion(WorldEvent evt, Hero companion)
        {
            try
            {
                if (companion == null || evt?.TargetSettlement == null) return;
                if (evt.Stage != EventStage.Resolved)
                {
                    TakePrisonerAction.Apply(evt.TargetSettlement.Party, companion);
                    WorldEventStore.TransitionStage(evt, EventStage.Resolved, null, "companion_handed_over");
                    // 提示消息（铁律 13）：你把 {NAME} 交给了守卫。
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveCompound("LWN_dialogue_companion_crime_handover_msg",
                            "You hand over {NAME} to the guards.",
                            ("NAME", companion.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_ui_name_target", "target"))),
                        Colors.Yellow));
                    DebugLogger.Log($"[CompanionCrime] 大义灭亲：{companion.Name} 被关进 {evt.TargetSettlement.Name}，事件 {evt.EventId} Resolved");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[CompanionCrime] 交人失败: {ex.Message}");
            }
        }

        /// <summary>拒不认账结算：权威对玩家好感下降（包庇随从的代价，铁律 12）。</summary>
        static void ApplyDenyConsequence(WorldEvent evt)
        {
            try
            {
                var authority = WorldEventStore.GetAuthorityNpc(evt);
                if (authority != null)
                {
                    ChangeRelationAction.ApplyPlayerRelation(authority, -10);
                    DebugLogger.Log($"[CompanionCrime] 拒认代价：{authority.Name} 对玩家好感 -10");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[CompanionCrime] 拒认结算失败: {ex.Message}");
            }
        }

        private static void BuildRetaliationNode(List<DialogueInjector.DialogueNode> nodes, PlaceholderResolver r, IntentContext ctx)
        {
            WorldEvent evt = r.Event;

            if (evt.SuspectIsPlayer)
            {
                // 权威NPC（玩家作案）：最后通牒，今天必须给个交代
                string npcLine = LWNTextHelper.Resolve("LWN_crime_authority_retaliation_opening_player", r,
                    "Kind words are spent, {SpeakerPlayerAddr} insists on going this far. {SpeakerSelfRef} will not waste more breath — without settling for {TargetSettlementName} today, you won't walk out of here.");
                var transitions = new List<DialogueInjector.DialogueTransition>
                {
                    // 玩家答应赔钱（不标价，由 NPC 开价）
                    new DialogueInjector.DialogueTransition { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_retaliation_pay", "I'll pay! Name your price."), Action = "NONE", NextNodeOnSuccess = "restitution_detail" },
                    // 玩家硬走（承担后果）
                    new DialogueInjector.DialogueTransition { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_leave", "I'm leaving."), Action = "INTENT:WalkAway", NextNodeOnSuccess = "" },
                };
                nodes.Add(new DialogueInjector.DialogueNode
                {
                    Id = "injectedStart",
                    NpcLine = npcLine,
                    Transitions = transitions
                });
                BuildRestitutionSubtree(nodes, r, ctx);
            }
            else
            {
                // 权威NPC（他人作案）：已雇人去抓，邀玩家带队
                string npcLine = LWNTextHelper.Resolve("LWN_crime_authority_retaliation_opening_other", r,
                    "Polite words don't work, so it comes to action. We've already hired men to hunt {SuspectDescription}. If {SpeakerPlayerAddr} stands with us, you can lead them.");
                var transitions = new List<DialogueInjector.DialogueTransition>
                {
                    // 玩家带队抓人
                    new DialogueInjector.DialogueTransition { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_retaliation_lead", "I'll lead the men!"), Action = "INTENT:LeadRetaliation", NextNodeOnSuccess = "retaliate_lead_ack" },
                    // 玩家拒绝带队
                    new DialogueInjector.DialogueTransition { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_retaliation_busy", "I'm busy."), Action = "NONE", NextNodeOnSuccess = "" },
                };
                nodes.Add(new DialogueInjector.DialogueNode
                {
                    Id = "injectedStart",
                    NpcLine = npcLine,
                    Transitions = transitions
                });
                // 权威NPC：同意玩家带队
                nodes.Add(Node("retaliate_lead_ack", LWNTextHelper.Resolve("LWN_crime_authority_retaliation_lead_ack", r,
                    "Good! With {SpeakerPlayerAddr} leading, {SuspectDescription} won't get away."), "continue_chat"));
            }

            AddContinueChatWithFarewell(nodes, r);
        }

        private static DialogueInjector.DialogueInjectScript BuildWitnessScript(
            PlaceholderResolver r, IntentContext ctx)
        {
            List<DialogueInjector.DialogueNode> nodes = new List<DialogueInjector.DialogueNode>();
            WorldEvent evt = ctx.ActiveEvent;
            Hero speaker = ctx.Speaker;

            // 从 WitnessTestimonies 匹配当前 NPC 的证词
            WitnessTestimony testimony = evt.WitnessTestimonies?
                .FirstOrDefault(t => t.WitnessHeroId == speaker.StringId);
            r.SpeakingWitness = testimony;

            string witnessedDesc = BuildWitnessedActionDescription(testimony, evt.SettlementLocationWord);

            DialogueInjector.DialogueNode node = new DialogueInjector.DialogueNode
            {
                Id = "injectedStart",
                // 目击者开场（玩家是作案者）：看见玩家作案的证词
                NpcLine = evt.InitiatorIsPlayer
                    // {ADDR}是来问{SCENE}的事？{SELF}看见了——{DESC}。
                    ? LWNTextHelper.ResolveCompound("LWN_crime_witness_opening_player",
                        "{ADDR} is here to ask about {SCENE}? {SELF} saw it — {DESC}.",
                        ("ADDR", r.ResolveOne("SpeakerPlayerAddr")), ("SCENE", r.ResolveOne("CrimeScene")),
                        ("SELF", r.ResolveOne("SpeakerSelfRef")), ("DESC", witnessedDesc))
                    // 目击者开场（他人作案）：陈述所见
                    : LWNTextHelper.ResolveCompound("LWN_crime_witness_opening_other",
                        "{SELF} saw it near {SCENE} {TIME} — {DESC}",
                        ("SELF", r.ResolveOne("SpeakerSelfRef")), ("TIME", r.ResolveOne("TimeWord")),
                        ("SCENE", r.ResolveOne("CrimeScene")), ("DESC", witnessedDesc)),
                Transitions = new List<DialogueInjector.DialogueTransition>()
            };

            if (evt.InitiatorIsPlayer && !evt.WitnessesSilenced)
            {
                node.Transitions.Add(new DialogueInjector.DialogueTransition
                {
                    // 玩家行贿封口（说服检定）
                    PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_witness_bribe", "(offers money) Don't tell anyone about this..."),
                    CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                    Action = "INTENT:SilenceWitness",
                    ActionParam = "bribe",
                    NextNodeOnSuccess = "witness_silence_ack",
                    NextNodeOnFail = "witness_silence_fail"
                });
                node.Transitions.Add(new DialogueInjector.DialogueTransition
                {
                    // 玩家威胁封口（威胁检定）
                    PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_witness_threat", "(threatens) You saw nothing, understand?"),
                    CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                    Action = "INTENT:SilenceWitness",
                    ActionParam = "threat",
                    NextNodeOnSuccess = "witness_threat_ack",
                    NextNodeOnFail = "witness_threat_fail"
                });
                // 玩家装作没来过（走人）
                node.Transitions.Add(new DialogueInjector.DialogueTransition { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_witness_leave", "Pretend I was never here."), Action = "INTENT:WalkAway", NextNodeOnSuccess = "" });
            }
            else if (evt.WitnessesSilenced)
            {
                // 目击者已被封口：矢口否认
                node.NpcLine = LWNTextHelper.Resolve("LWN_crime_witness_silenced", r,
                    "{SpeakerPlayerAddr} has the wrong person. {SpeakerSelfRef} knows nothing.");
                // 玩家接受封口结果
                node.Transitions.Add(new DialogueInjector.DialogueTransition { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_witness_silenced_accept", "...Alright."), Action = "NONE", NextNodeOnSuccess = "continue_chat" });
            }
            else
            {
                // 玩家打听嫌疑人特征
                node.Transitions.Add(new DialogueInjector.DialogueTransition { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_witness_ask_desc", "Can you describe the person?"), Action = "NONE", NextNodeOnSuccess = "witness_desc_ack" });
                // 玩家道谢走人
                node.Transitions.Add(new DialogueInjector.DialogueTransition { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_witness_thanks", "Thanks, that's all."), Action = "NONE", NextNodeOnSuccess = "continue_chat" });
            }

            nodes.Add(node);
            // 目击者：收钱封口
            nodes.Add(Node("witness_silence_ack", LWNTextHelper.Resolve("LWN_crime_witness_bribe_ack", r,
                "...Alright, {SpeakerSelfRef} saw nothing."), "continue_chat"));
            // 目击者：拒收贿赂，要去告发
            nodes.Add(Node("witness_silence_fail", LWNTextHelper.Resolve("LWN_crime_witness_bribe_fail", r,
                "What do you take {SpeakerSelfRef} for?! {SpeakerSelfRef} will go tell {AuthorityRole}!"), "continue_chat"));
            // 目击者：被威胁服软
            nodes.Add(Node("witness_threat_ack", LWNTextHelper.Resolve("LWN_crime_witness_threat_ack", r,
                "Understood, understood... {SpeakerSelfRef} won't say a word."), "continue_chat"));
            // 目击者：被威胁激怒，喊人
            nodes.Add(Node("witness_threat_fail", LWNTextHelper.Resolve("LWN_crime_witness_threat_fail", r,
                "You dare threaten {SpeakerSelfRef}?! Guards —!"), "continue_chat"));
            // 目击者：描述嫌疑人特征
            nodes.Add(Node("witness_desc_ack", LWNTextHelper.Resolve("LWN_crime_witness_desc_ack", r,
                "That person... {SuspectDescription}."), "continue_chat"));
            AddContinueChatWithFarewell(nodes, r);
            // 玩家主动提起目击话题的入口选项
            return new DialogueInjector.DialogueInjectScript { EntryOption = LWNTextHelper.ResolveText("LWN_crime_player_witness_entry", "I heard you saw something...?"), EntryNode = "injectedStart", Nodes = nodes };
        }

        private static DialogueInjector.DialogueInjectScript BuildSuspectScript(
            PlaceholderResolver r, IntentContext ctx)
        {
            List<DialogueInjector.DialogueNode> nodes = new List<DialogueInjector.DialogueNode>
            {
                new DialogueInjector.DialogueNode
                {
                    Id = "injectedStart",
                    // 嫌疑人：被盯得心里发毛
                    NpcLine = LWNTextHelper.Resolve("LWN_crime_suspect_opening", r,
                        "Why is {SpeakerPlayerAddr} staring at {SpeakerSelfRef}?"),
                    Transitions = new List<DialogueInjector.DialogueTransition>
                    {
                        new DialogueInjector.DialogueTransition
                        {
                            // 玩家诱骗嫌疑人去见权威（说服检定）
                            PlayerLine = LWNTextHelper.Resolve("LWN_crime_player_suspect_lure", r,
                                "Come with me. {AuthorityRole} wants to see you."),
                            CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                            Action = "INTENT:LureArrest",
                            NextNodeOnSuccess = "suspect_lure_ack",
                            NextNodeOnFail = "suspect_lure_fail"
                        },
                        // 玩家出卖嫌疑人（通风报信）
                        new DialogueInjector.DialogueTransition { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_suspect_betray", "Run! The locals are after you."), Action = "INTENT:BetrayQuest", NextNodeOnSuccess = "suspect_betray_ack" },
                        // 玩家装作无事走人
                        new DialogueInjector.DialogueTransition { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_suspect_nothing", "Nothing."), Action = "INTENT:WalkAway", NextNodeOnSuccess = "" },
                    }
                }
            };
            // 嫌疑人：被诱骗成功（惊愕否认）
            nodes.Add(Node("suspect_lure_ack", LWNTextHelper.Resolve("LWN_crime_suspect_lure_ack", r,
                "What?! {SpeakerSelfRef} hasn't done anything..."), "continue_chat"));
            // 嫌疑人：识破诱骗（反问玩家）
            nodes.Add(Node("suspect_lure_fail", LWNTextHelper.Resolve("LWN_crime_suspect_lure_fail", r,
                "{AuthorityRole} wants {SpeakerSelfRef}? Why doesn't he come himself? {SpeakerPlayerAddr}, stop lying."), "continue_chat"));
            // 嫌疑人：感谢玩家通风报信
            nodes.Add(Node("suspect_betray_ack", LWNTextHelper.ResolveText("LWN_crime_suspect_betray_ack", "What?! ...Thanks!"), "continue_chat"));
            AddContinueChatWithFarewell(nodes, r);
            // 玩家沉默搭话的入口选项
            return new DialogueInjector.DialogueInjectScript { EntryOption = LWNTextHelper.ResolveText("LWN_ph_ellipsis", "..."), EntryNode = "injectedStart", Nodes = nodes };
        }

        private static DialogueInjector.DialogueInjectScript BuildBystanderScript(
            PlaceholderResolver r, IntentContext ctx)
        {
            List<DialogueInjector.DialogueNode> nodes = new List<DialogueInjector.DialogueNode>();
            WorldEvent evt = ctx.ActiveEvent;

            // 路人NPC台词按案件阶段切换：Emerging（案子刚发现）/ Active（已锁定嫌疑人）/ Confrontation（闹大了）/ 已了结
            string npcLine = evt.Stage switch
            {
                // {SpeakerPlayerAddr}听说了吗？{TargetSettle...
                EventStage.Emerging => LWNTextHelper.Resolve("LWN_crime_bystander_emerging", r,
                    "Have {SpeakerPlayerAddr} heard? Something happened in {TargetSettlementName} — {DiscoveryFacts}! Nobody knows who did it yet."),
                // 听说了吗？是{SuspectDescription}干的！{TargetS...
                EventStage.Active => LWNTextHelper.Resolve("LWN_crime_bystander_active", r,
                    "Heard the news? It was {SuspectDescription}! {TargetSettlementName} put {BountyAmount} denars on their head."),
                // {TargetSettlementName}的人真动手了——雇了打手满世界...
                EventStage.Confrontation => LWNTextHelper.Resolve("LWN_crime_bystander_confrontation", r,
                    "The people of {TargetSettlementName} really moved — hired thugs searching everywhere. This is getting big..."),
                // 这事好像已经过去了……
                _ => LWNTextHelper.ResolveText("LWN_crime_bystander_resolved", "Looks like that matter is over..."),
            };

            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "injectedStart",
                NpcLine = npcLine,
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    // 玩家打听细节
                    new DialogueInjector.DialogueTransition { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_bystander_detail", "Tell me more?"), Action = "NONE", NextNodeOnSuccess = "bystander_detail_ack" },
                    // 玩家随口应和
                    new DialogueInjector.DialogueTransition { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_bystander_ack", "Oh."), Action = "NONE", NextNodeOnSuccess = "continue_chat" },
                }
            });
            // 路人NPC：知道的就这么多
            nodes.Add(Node("bystander_detail_ack", LWNTextHelper.ResolveText("LWN_crime_bystander_detail_ack", "That's all I know..."), "continue_chat"));
            AddContinueChatWithFarewell(nodes, r);

            // 玩家打听新鲜事的入口选项
            return new DialogueInjector.DialogueInjectScript { EntryOption = LWNTextHelper.Resolve("LWN_crime_player_bystander_entry", r, "Anything new in {TargetSettlementName} lately?"), EntryNode = "injectedStart", Nodes = nodes };
        }

        /// <summary>继续聊 node：NPC 说完事后 → 玩家走人。告别语按阶段动态切换，引擎展示前才求值。</summary>
        private static DialogueInjector.DialogueNode BuildContinueChatNode(PlaceholderResolver r)
        {
            WorldEvent evt = r.Event;
            return new DialogueInjector.DialogueNode
            {
                Id = "continue_chat",
                // 通用继续聊：NPC 问玩家还有什么想说的
                NpcLine = LWNTextHelper.ResolveText("LWN_crime_common_continue_chat", "Anything else you want to say?"),
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    new DialogueInjector.DialogueTransition
                    {
                        // 玩家告辞
                        PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_leave_now", "I have to go."),
                        Action = "INTENT:WalkAway",
                        NextNodeOnSuccess = "farewell"
                    },
                }
            };
        }

        /// <summary>告别 node：惰性求值阶段相关的告别语。始终跟在 continue_chat 后面。</summary>
        private static DialogueInjector.DialogueNode BuildFarewellNode(PlaceholderResolver r)
        {
            WorldEvent evt = r.Event;
            return new DialogueInjector.DialogueNode
            {
                Id = "farewell",
                LazyNpcLine = () =>
                {
                    // 告别语（接了调查任务）：催促快去查
                    if (evt.PlayerTookInvestigationQuest)
                        // 快去查，有消息了来告诉{SpeakerSelfRef}。
                        return LWNTextHelper.Resolve("LWN_crime_common_go_investigate", r,
                            "Go investigate, and report back to {SpeakerSelfRef} when you have news.");
                    // 告别语（玩家是嫌疑人且案件 Active）：这事不算完
                    if (evt.SuspectIsPlayer && evt.Stage == EventStage.Active)
                        // 这事不算完。
                        return LWNTextHelper.ResolveText("LWN_crime_common_farewell_unfinished_active", "This isn't over.");
                    // 告别语（对峙阶段）：这事没完
                    if (evt.Stage == EventStage.Confrontation)
                        // 这事没完。
                        return LWNTextHelper.ResolveText("LWN_crime_common_farewell_unfinished_confrontation", "This isn't over yet.");
                    // 告别语（默认）：放玩家走
                    return LWNTextHelper.Resolve("LWN_crime_common_farewell_ok", r, "Alright, {SpeakerPlayerAddr} may go.");
                },
                Transitions = new List<DialogueInjector.DialogueTransition>()  // terminal
            };
        }

        /// <summary>同时添加 continue_chat + farewell 两个 node。</summary>
        private static void AddContinueChatWithFarewell(List<DialogueInjector.DialogueNode> nodes, PlaceholderResolver r)
        {
            nodes.Add(BuildContinueChatNode(r));
            nodes.Add(BuildFarewellNode(r));
        }


        // ═══════════════════════════════════════════════════════════════
        // 🆕 L3 警戒质问对话构建（Phase 4）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>从单条 WitnessTestimony 构建中文描述（如"偷了村民甲的鸡，还把人打晕了"）</summary>
        public static string BuildWitnessedActionDescription(WitnessTestimony testimony, string locationWord = null)
        {
            // 无有效证词时的兜底描述："有人在闹事"
            if (testimony?.Actions == null || testimony.Actions.Count == 0)
                // 有人在闹事
                return LWNTextHelper.ResolveText("LWN_crime_witness_act_someone_stirring", "someone was making trouble");

            // 地点缺省时的兜底："当地"
            string loc = locationWord ?? LWNTextHelper.ResolveText("LWN_crime_witness_act_loc_fallback", "around here");

            List<string> parts = new List<string>();
            foreach (ActionRecord a in testimony.Actions.OrderByDescending(a => a.AlertValue))
            {
                // 按动作类型生成目击描述片段（偷窃/拔刀/打人/击晕等）
                string desc = a.ActionType switch
                {
                    // 鬼鬼祟祟蹲了半天
                    "Crouching" => LWNTextHelper.ResolveText("LWN_crime_witness_act_crouching", "crouched around suspiciously for a while"),
                    // 在{LOC}拔刀
                    "WeaponDrawn" => LWNTextHelper.ResolveCompound("LWN_crime_witness_act_weapondrawn", "drew a blade in {LOC}", ("LOC", loc)),
                    // 翻箱倒柜
                    "StealUIOpen" => LWNTextHelper.ResolveText("LWN_crime_witness_act_steal_ui", "rummaging through things"),
                    "Steal" when a.ItemName != null =>
                        a.TargetName != null
                            // 偷了{TARGET}的{ITEM}
                            ? LWNTextHelper.ResolveCompound("LWN_crime_witness_act_steal_target_item", "stole {ITEM} from {TARGET}", ("TARGET", a.TargetName), ("ITEM", a.ItemName))
                            // 偷了{ITEM}
                            : LWNTextHelper.ResolveCompound("LWN_crime_witness_act_steal_item", "stole {ITEM}", ("ITEM", a.ItemName)),
                    // 偷了东西
                    "Steal" => LWNTextHelper.ResolveText("LWN_crime_witness_act_steal", "stole something"),
                    // 动手打了{TARGET}
                    "AttackAlly" when a.TargetName != null => LWNTextHelper.ResolveCompound("LWN_crime_witness_act_attack_target", "attacked {TARGET}", ("TARGET", a.TargetName)),
                    // 动手打人
                    "AttackAlly" => LWNTextHelper.ResolveText("LWN_crime_witness_act_attack", "started a fight"),
                    // 把{TARGET}打晕了
                    "Knockout" when a.TargetName != null => LWNTextHelper.ResolveCompound("LWN_crime_witness_act_knockout_target", "knocked out {TARGET}", ("TARGET", a.TargetName)),
                    // 把人打晕了
                    "Knockout" => LWNTextHelper.ResolveText("LWN_crime_witness_act_knockout", "knocked someone out"),
                    _ => null
                };
                if (desc != null) parts.Add(desc);
            }
            // 多段描述拼接：0 段兜底 / 1 段原样 / 2 段"…，还…" / 3 段"…、…，还…"
            return parts.Count switch
            {
                // 有人在闹事
                0 => LWNTextHelper.ResolveText("LWN_crime_witness_act_someone_stirring", "someone was making trouble"),
                1 => parts[0],
                // {ACT1}，还{ACT2}
                2 => LWNTextHelper.ResolveCompound("LWN_crime_witness_act_join_two", "{ACT1}, and also {ACT2}", ("ACT1", parts[0]), ("ACT2", parts[1])),
                // {ACT1}、{ACT2}，还{ACT3}
                _ => LWNTextHelper.ResolveCompound("LWN_crime_witness_act_join_three", "{ACT1}, {ACT2}, and also {ACT3}", ("ACT1", parts[0]), ("ACT2", parts[1]), ("ACT3", parts[2]))
            };
        }

        /// <summary>
        /// 构建 L3 警戒质问的 DialogueInjectScript。
        /// 台词通过 NpcSpeechResolver → NarrativeResolver → 硬编码 三阶段 ?? 回落解析。
        /// 统一签名：PlaceholderResolver + IntentContext 即全部对话所需信息。
        /// ctx.Confrontation / ctx.TriggerAction 由调用方从 AgentBrain 或目击证词设定；
        /// r.SpeakingWitness 由调用方从 PendingWorldEvent 设定。
        /// </summary>
        public static DialogueInjector.DialogueInjectScript BuildAlertInterceptScript(
            PlaceholderResolver r, IntentContext ctx)
        {
            List<DialogueInjector.DialogueNode> nodes = new List<DialogueInjector.DialogueNode>();

            var npcIntent = ctx.Confrontation;
            var primaryAction = ctx.TriggerAction;
            var worldEvt = ctx.ActiveEvent;
            var speaker = ctx.Speaker;

            // 脉冲上下文：证词主行为优先，回落 r 的 TargetName/ItemName（Brain 警戒明细回填）。
            // 必须显式传给 NpcSpeechResolver——它内部自建 PlaceholderResolver，
            // 不传的话 CSV 模板里的 {TARGET}/{ITEM} 会解析成空串（"你把打晕了"缺主语的 bug）
            var primaryPulse = r.SpeakingWitness?.Actions?
                .OrderByDescending(a => a.AlertValue).FirstOrDefault();
            string pulseTarget = primaryPulse?.TargetName ?? r.TargetName;
            string pulseItem = primaryPulse?.ItemName ?? r.ItemName;

            // 两阶段回落：① NpcSpeech.csv（含 Narrative 过渡）→ ② 硬编码
            string npcOpening =
                NpcSpeechResolver.Resolve($"L3_{npcIntent}_{primaryAction}", speaker, Hero.MainHero,
                    evt: worldEvt, targetName: pulseTarget, itemName: pulseItem,
                    narrativeFallback: new NarrativeFilters
                    {
                        EventName = "L3AlertIntercept",
                        GoalType = npcIntent.ToString(),
                        Outcome = primaryAction.ToString(),
                    }, speakerCharacter: r.SpeakerCharacter)
                ?? HardcodedAlertLine(r, npcIntent, primaryAction);

            BuildAlertTransitionsSubtree(nodes, r, ctx, npcOpening);

            // continue_chat（冲突中）：赔钱/打架/坐牢，没有"离开"。
            // 只有被原谅后（→ continue_chat_safe）才能走人。
            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "continue_chat",
                LazyNpcLine = () =>
                {
                    var st = Settlement.CurrentSettlement;
                    var evt = st != null ? WorldEventStore.FindOnGoing(st.StringId) : null;
                    bool escalated = evt != null && evt.Stage >= EventStage.Active;
                    // 警戒冲突继续聊：已升级 → 最后一次警告；未升级 → 还有什么想说的
                    return escalated
                        // 最后一次警告——别逼我叫人！
                        ? LWNTextHelper.ResolveText("LWN_crime_alert_continue_escalated", "Final warning — don't make me call the men!")
                        // 还有什么想说的？
                        : LWNTextHelper.ResolveText("LWN_crime_alert_continue_peaceful", "Anything else you want to say?");
                },
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    // 冲突未解决 → 只有对抗性出口，没有"离开"
                    // 玩家开打（对抗出口）
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_stand_in_my_way", "Stand in my way and I'll kill you!"), Action = "INTENT:FightVillagers", NextNodeOnSuccess = "alert_esc_fight_ack" },
                    // 玩家认赔（由 NPC 开价）
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_willing_to_pay", "I'm willing to pay."), Action = "NONE", NextNodeOnSuccess = "restitution_demand" },
                    // 玩家放弃抵抗坐牢
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_no_money_arrest", "I have no money. Arrest me if you must."), Action = "INTENT:SurrenderJail", ActionParam = "surrender_jail", NextNodeOnSuccess = "alert_esc_jail_ack" },
                }
            });

            // continue_chat_safe（已原谅）：只有"离开"
            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "continue_chat_safe",
                // 警戒冲突已和解：还有什么想说的
                NpcLine = LWNTextHelper.ResolveText("LWN_crime_alert_continue_peaceful", "Anything else you want to say?"),
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    // 玩家安全离开
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_leave", "I'm leaving."), Action = "INTENT:WalkAway", ActionParam = "safe", NextNodeOnSuccess = "" },
                }
            });
            // Escalated ack nodes
            // 警戒NPC：玩家动武 → 喊人
            nodes.Add(Node("alert_esc_fight_ack", LWNTextHelper.Resolve("LWN_crime_alert_fight_ack", r, "{SPEAKER_PLAYER_ADDR} has gone mad! Call for help!")));
            // 警戒NPC：玩家没钱还闹事 → 关地牢
            nodes.Add(Node("alert_esc_jail_ack", LWNTextHelper.Resolve("LWN_crime_alert_jail_ack", r, "No money and still causing trouble?! Guards, throw him in the dungeon!")));

            BuildRestitutionSubtree(nodes, r, ctx, "continue_chat_safe");

            return new DialogueInjector.DialogueInjectScript { SkipVanillaOpening = true, EntryNode = "injectedStart", Nodes = nodes };
        }

        /// <summary>L3 警戒质问的硬编码兜底台词（CSV 和 Narrative 均未命中时）。</summary>
        static string HardcodedAlertLine(PlaceholderResolver r, ConfrontationType npcIntent, PlayerActionType primaryAction)
        {
            return npcIntent switch
            {
                // 驱离质问：拔刀 → 叫收刀
                ConfrontationType.Deter => primaryAction switch
                {
                    PlayerActionType.WeaponDrawn =>
                        // 把{ITEM}收起来！{SPEAKER_PLAYER_ADDR}！这是村子...
                        LWNTextHelper.Resolve("LWN_crime_alert_deter_weapondrawn", r,
                            "Put away {ITEM}! {SPEAKER_PLAYER_ADDR}! This is a village, not a battlefield!"),
                    _ => // Crouching
                        // 喂！{SPEAKER_PLAYER_ADDR}！蹲在那鬼鬼祟祟干什么？
                        LWNTextHelper.Resolve("LWN_crime_alert_deter_crouching", r,
                            "Hey! {SPEAKER_PLAYER_ADDR}! What are you doing crouching around so furtively?"),
                },

                // 搜身质问：命令玩家打开背包
                ConfrontationType.Search =>
                    // {SPEAKER_PLAYER_ADDR}在翻什么？把手拿开，让{SPEA...
                    LWNTextHelper.Resolve("LWN_crime_alert_search", r,
                        "What is {SPEAKER_PLAYER_ADDR} rummaging through? Move your hands, let {SPEAKER_SELF} see your bag."),

                // 追回质问：人赃并获
                ConfrontationType.Recover =>
                    // {SPEAKER_SELF}看见了！{SPEAKER_PLAYER_ADD...
                    LWNTextHelper.Resolve("LWN_crime_alert_recover", r,
                        "{SPEAKER_SELF} saw it! {SPEAKER_PLAYER_ADDR} stole {StolenItemName}! Hand it over!"),

                // 制止质问：按具体行为分派
                ConfrontationType.Stop => primaryAction switch
                {
                    PlayerActionType.AttackAlly =>
                        // {SPEAKER_PLAYER_ADDR}竟敢动手打人？！住手！
                        LWNTextHelper.Resolve("LWN_crime_alert_stop_attack", r,
                            "{SPEAKER_PLAYER_ADDR} dares to strike people?! Stop!"),
                    PlayerActionType.Knockout =>
                        // {SPEAKER_PLAYER_ADDR}把{TARGET}打晕了！来人！
                        LWNTextHelper.Resolve("LWN_crime_alert_stop_knockout", r,
                            "{SPEAKER_PLAYER_ADDR} knocked out {TARGET}! Guards!"),
                    PlayerActionType.SuspectFlee =>
                        // 站住！这事没了结，{SPEAKER_PLAYER_ADDR}哪儿也别想去！
                        LWNTextHelper.Resolve("LWN_crime_alert_stop_flee", r,
                            "Stop! This isn't over — {SPEAKER_PLAYER_ADDR} isn't going anywhere!"),
                    // 住手！
                    _ => LWNTextHelper.ResolveText("LWN_crime_alert_stop_generic", "Stop!")
                },
                // {SPEAKER_PLAYER_ADDR}！你在干什么？
                _ => LWNTextHelper.Resolve("LWN_crime_alert_generic", r, "{SPEAKER_PLAYER_ADDR}! What are you doing?")
            };
        }

        /// <summary>
        /// L3 警戒质问对话子树：按 ctx.Confrontation 分派到四个自包含的子函数。
        /// 依赖调用方已添加 continue_chat（含 escalated ack nodes）。
        /// </summary>
        static void BuildAlertTransitionsSubtree(
            List<DialogueInjector.DialogueNode> nodes,
            PlaceholderResolver r,
            IntentContext ctx,
            string npcOpening)
        {
            switch (ctx.Confrontation)
            {
                //驱离
                case ConfrontationType.Deter:
                    BuildDeterSubtree(nodes, r, ctx, npcOpening);
                    break;
                //搜身
                case ConfrontationType.Search:
                    BuildSearchSubtree(nodes, r, ctx, npcOpening);
                    break;
                //追回
                case ConfrontationType.Recover:
                    BuildRecoverSubtree(nodes, r, ctx, npcOpening);
                    break;
                //制止
                case ConfrontationType.Stop:
                    BuildStopSubtree(nodes, r, ctx, npcOpening);
                    break;
            }
        }

        /// <summary>Deter 质问子树：驱离警告。两层递进设计。
        /// Layer 1: 威胁 + 道歉（两个检定出口，没有免费离开）
        /// Layer 2（道歉失败后）: 认罚 + 拔剑 + 坐牢
        /// 对标 KCD2：被抓住后不能无代价脱身——要么检定过，要么付代价。</summary>
        static void BuildDeterSubtree(
            List<DialogueInjector.DialogueNode> nodes,
            PlaceholderResolver r,
            IntentContext ctx,
            string npcOpening)
        {
            // ══ Layer 1: NPC 质问 → 威胁（高风险高回报）或 道歉（检定门控） ══
            var layer1Transitions = new List<DialogueInjector.DialogueTransition>
            {
                // 玩家反呛（威胁检定）
                new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_deter_threat", "None of your business."), CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                        Action = "INTENT:Threat", NextNodeOnSuccess = "alert_deter_threat_ok", NextNodeOnFail = "alert_deter_threat_fail" },
                // 玩家道歉求放行（魅力检定）
                new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_deter_apologize", "Sorry, can you let me go?"), CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                        Action = "INTENT:Apologize", NextNodeOnSuccess = "alert_deter_apologize_ok", NextNodeOnFail = "alert_deter_apologize_fail" },
            };

            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "injectedStart",
                NpcLine = npcOpening,
                Transitions = layer1Transitions
            });

            // Layer 1 ack nodes
            // 警戒NPC：威胁成功（算了）
            nodes.Add(Node("alert_deter_threat_ok", LWNTextHelper.ResolveText("LWN_crime_alert_deter_threat_ok", "...Forget it.")));
            // 警戒NPC：威胁失败（喊人）
            nodes.Add(Node("alert_deter_threat_fail", LWNTextHelper.ResolveText("LWN_crime_alert_deter_threat_fail", "Guards! We've got a troublemaker here!")));
            // 警戒NPC：道歉成功（警告别再鬼鬼祟祟）
            nodes.Add(Node("alert_deter_apologize_ok", LWNTextHelper.Resolve("LWN_crime_alert_deter_apologize_ok", r,
                "...Sensible of you. Don't let {SPEAKER_SELF} catch you skulking around again.")));

            // ══ Layer 2: 道歉失败 → NPC 拒绝，升级选项 ══
            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "alert_deter_apologize_fail",
                // 警戒NPC：道歉无用，给玩家两条路（认罚或开打）
                NpcLine = LWNTextHelper.Resolve("LWN_crime_alert_deter_apologize_fail", r,
                    "If apologies worked, what would we be here for? Two ways — pay the fine, or we settle this another way. Your choice."),
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    // 玩家认罚（由 NPC 开价）
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_willing_to_pay", "I'm willing to pay."), Action = "NONE", NextNodeOnSuccess = "restitution_demand" },
                    // 玩家开打
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_stand_in_my_way", "Stand in my way and I'll kill you!"), Action = "INTENT:FightVillagers",
                            NextNodeOnSuccess = "alert_deter_fight_ack" },
                    // 玩家放弃抵抗坐牢
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_no_money_arrest", "I have no money. Arrest me if you must."), Action = "INTENT:SurrenderJail",
                            ActionParam = "surrender_jail", NextNodeOnSuccess = "alert_deter_jail_ack" },
                }
            });

            // Layer 2 ack nodes
            // 警戒NPC：玩家动武 → 喊人
            nodes.Add(Node("alert_deter_fight_ack", LWNTextHelper.Resolve("LWN_crime_alert_fight_ack", r, "{SPEAKER_PLAYER_ADDR} has gone mad! Call for help!")));
            // 警戒NPC：玩家没钱还闹事 → 关地牢
            nodes.Add(Node("alert_deter_jail_ack", LWNTextHelper.Resolve("LWN_crime_alert_jail_ack", r, "No money and still causing trouble?! Guards, throw him in the dungeon!")));

            BuildRestitutionSubtree(nodes, r, ctx, "continue_chat_safe");
        }

        /// <summary>Search 质问子树：搜查包裹。含 recover_confront（拒绝搜查→对峙）和 search_result（接受搜查→判定赃物）。</summary>
        static void BuildSearchSubtree(
            List<DialogueInjector.DialogueNode> nodes,
            PlaceholderResolver r,
            IntentContext ctx,
            string npcOpening)
        {
            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "injectedStart",
                NpcLine = npcOpening,
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    // 玩家同意搜查
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_search_submit", "...Fine, take a look."), Action = "INTENT:SubmitToSearch", NextNodeOnSuccess = "search_result" },
                    // 玩家拒绝搜查（升级为追回对峙）
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_search_refuse", "Why should you search my things?"), Action = "INTENT:RefuseSearch", NextNodeOnSuccess = "recover_confront" },
                    // 玩家行贿免查（说服检定）
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_search_bribe", "Skip the search, I'll pay you off."), CheckType = DialogueInjector.TransitionCheckType.SkillCheck, Action = "INTENT:PayRestitution", ActionParam = "bribe", NextNodeOnSuccess = "alert_search_bribe_ack", NextNodeOnFail = "alert_search_bribe_fail" },
                }
            });

            // 警戒NPC：收下贿赂（做贼心虚）
            nodes.Add(Node("alert_search_bribe_ack", LWNTextHelper.ResolveText("LWN_crime_alert_search_bribe_ack", "...Guilty conscience, eh? Take your money and go.")));
            // 警戒NPC：拒收贿赂（强行搜包）
            nodes.Add(Node("alert_search_bribe_fail", LWNTextHelper.Resolve("LWN_crime_alert_search_bribe_fail", r,
                "None of that. Open the bag, {SPEAKER_SELF} will see for himself!"), "continue_chat"));
            // 警戒NPC：赃物上有失主名字
            nodes.Add(Node("alert_search_deny_ack", LWNTextHelper.Resolve("LWN_crime_alert_search_deny_ack", r,
                "Yours? It even has {TARGET}'s name on it!"), "continue_chat"));

            // recover_confront（refuse search → recover mode）
            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "recover_confront",
                // 警戒NPC：拒绝搜查 = 心里有鬼，人赃并获
                NpcLine = LWNTextHelper.Resolve("LWN_crime_alert_recover_confront", r,
                    "Afraid to be seen? Then you're hiding something! {SPEAKER_SELF} saw it! {SPEAKER_PLAYER_ADDR} stole {StolenItemName}! Hand it over!"),
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    // 玩家归还赃物两清
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_return_items", "Here, take your things back. We're even."), Action = "INTENT:ReturnStolenItems", NextNodeOnSuccess = "alert_recover_return_ack" },
                    // 玩家认赔（由 NPC 开价）
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_willing_to_pay", "I'm willing to pay."), Action = "NONE", NextNodeOnSuccess = "restitution_demand" },
                    // 玩家狡辩否认（魅力检定）
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_deny_eyes", "What did you see with your own eyes?"), CheckType = DialogueInjector.TransitionCheckType.SkillCheck, Action = "INTENT:CharmDefense", NextNodeOnSuccess = "alert_recover_charm_ok", NextNodeOnFail = "alert_recover_charm_fail" },
                    // 玩家推开就跑
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_shove_and_run", "Shove past and run"), Action = "INTENT:FightVillagers", NextNodeOnSuccess = "alert_esc_fight_ack" },
                }
            });

            AddRecoverAckNodes(nodes, r);

            // search_result（submit search → 判定赃物）
            bool hasStolen = PlayerHasStolenItems();
            nodes.Add(BuildSearchResultNode(r, hasStolen));

            BuildRestitutionSubtree(nodes, r, ctx, "continue_chat_safe");
        }

        /// <summary>Recover 质问子树：人赃并获，交出赃物。</summary>
        static void BuildRecoverSubtree(
            List<DialogueInjector.DialogueNode> nodes,
            PlaceholderResolver r,
            IntentContext ctx,
            string npcOpening)
        {
            // 开场就把赔款数字摆在玩家眼前（选项里的 {RestitutionCost}）→ 这就是一次报价，记台账。
            // 与选项同源：都取构树时这一刻的 Restitution。
            // 🆕 赔钱入口已统一路由到 restitution_demand，NPC 在 restitution_demand 统一算账开价。
            // 报价台账由 BuildRestitutionSubtree 的 LazyNpcLine 统一记录，这里不再提前记账。
            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "injectedStart",
                NpcLine = npcOpening,
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    // 玩家归还赃物两清
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_return_items", "Here, take your things back. We're even."), Action = "INTENT:ReturnStolenItems", NextNodeOnSuccess = "alert_recover_return_ack" },
                    // 玩家认赔（由 NPC 开价）
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_willing_to_pay", "I'm willing to pay."), Action = "NONE", NextNodeOnSuccess = "restitution_demand" },
                    // 玩家狡辩否认（魅力检定）
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_deny_eyes", "What did you see with your own eyes?"), CheckType = DialogueInjector.TransitionCheckType.SkillCheck, Action = "INTENT:CharmDefense", NextNodeOnSuccess = "alert_recover_charm_ok", NextNodeOnFail = "alert_recover_charm_fail" },
                    // 玩家推开就跑
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_shove_and_run", "Shove past and run"), Action = "INTENT:FightVillagers", NextNodeOnSuccess = "alert_esc_fight_ack" },
                }
            });

            AddRecoverAckNodes(nodes, r);

            BuildRestitutionSubtree(nodes, r, ctx, "continue_chat_safe");
        }

        /// <summary>Stop 质问子树：当场制止暴力行为。</summary>
        static void BuildStopSubtree(
            List<DialogueInjector.DialogueNode> nodes,
            PlaceholderResolver r,
            IntentContext ctx,
            string npcOpening)
        {
            // 🆕 赔钱入口已统一路由到 restitution_demand，NPC 在 restitution_demand 统一算账开价。
            // 报价台账由 BuildRestitutionSubtree 的 LazyNpcLine 统一记录，这里不再提前记账。
            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "injectedStart",
                NpcLine = npcOpening,
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    // 玩家认赔（由 NPC 开价）
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_willing_to_pay", "I'm willing to pay."), Action = "NONE", NextNodeOnSuccess = "restitution_demand" },
                    // 玩家辩解对方先动手（魅力检定）
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_stop_charm", "He started it."), CheckType = DialogueInjector.TransitionCheckType.SkillCheck, Action = "INTENT:CharmDefense", NextNodeOnSuccess = "alert_stop_charm_ok", NextNodeOnFail = "alert_stop_charm_fail" },
                    // 玩家开打
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_stand_in_my_way", "Stand in my way and I'll kill you!"), Action = "INTENT:FightVillagers", NextNodeOnSuccess = "alert_stop_fight_ack" },
                    // 玩家放弃抵抗坐牢
                    new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_no_money_arrest", "I have no money. Arrest me if you must."), Action = "INTENT:SurrenderJail", ActionParam = "surrender_jail", NextNodeOnSuccess = "alert_esc_jail_ack" },
                }
            });

            // 警戒NPC：辩解成功（下次没这么好说话）
            nodes.Add(Node("alert_stop_charm_ok", LWNTextHelper.ResolveText("LWN_crime_alert_stop_charm_ok", "...Next time it won't be so easy."), "continue_chat_safe"));
            // 警戒NPC：辩解失败（眼皮底下动手要有说法）
            nodes.Add(Node("alert_stop_charm_fail", LWNTextHelper.Resolve("LWN_crime_alert_stop_charm_fail", r,
                "Striking someone under {SPEAKER_SELF}'s very eyes demands an answer!"), "continue_chat"));
            // 警戒NPC：玩家动武 → 喊人
            nodes.Add(Node("alert_stop_fight_ack", LWNTextHelper.Resolve("LWN_crime_alert_fight_ack", r, "{SPEAKER_PLAYER_ADDR} has gone mad! Call for help!")));

            BuildRestitutionSubtree(nodes, r, ctx, "continue_chat_safe");
        }

        /// <summary>Recover ack nodes：被 BuildRecoverSubtree 和 BuildSearchSubtree（via recover_confront）共享。</summary>
        static void AddRecoverAckNodes(List<DialogueInjector.DialogueNode> nodes, PlaceholderResolver r)
        {
            // 警戒NPC：赃物归还确认（算你老实）
            nodes.Add(Node("alert_recover_return_ack", LWNTextHelper.ResolveText("LWN_crime_alert_recover_return_ack", "It's all here. ...You're being honest. Don't come back.")));
            // 警戒NPC：狡辩成功（可能看错了）
            nodes.Add(Node("alert_recover_charm_ok", LWNTextHelper.Resolve("LWN_crime_alert_recover_charm_ok", r,
                "...{SPEAKER_SELF} might have been mistaken."), "continue_chat_safe"));
            // 警戒NPC：狡辩失败（两只眼睛都看见）
            nodes.Add(Node("alert_recover_charm_fail", LWNTextHelper.Resolve("LWN_crime_alert_recover_charm_fail", r,
                "{SPEAKER_SELF} saw it with both eyes!"), "continue_chat"));
        }

        /// <summary>搜查结果 node：接受搜查后，系统查 TheftLedger 判定玩家背包是否有赃物。依赖调用方已添加 alert_search_deny_ack。</summary>
        static DialogueInjector.DialogueNode BuildSearchResultNode(PlaceholderResolver r, bool hasStolenItems)
        {
            return new DialogueInjector.DialogueNode
            {
                Id = "search_result",
                // 搜查结果：搜出赃物（人赃并获）/ 没搜到（多心了）
                NpcLine = hasStolenItems
                    // 这是什么？！还说没偷！
                    ? LWNTextHelper.ResolveText("LWN_crime_alert_search_result_stolen", "What's this?! And you said you didn't steal!")
                    // ……行吧。是{SPEAKER_SELF}多心了。
                    : LWNTextHelper.Resolve("LWN_crime_alert_search_result_clean", r,
                        "...Fine then. {SPEAKER_SELF} was being paranoid."),
                Transitions = hasStolenItems
                    ? new List<DialogueInjector.DialogueTransition>
                    {
                        // 玩家沉默认罪
                        new() { PlayerLine = LWNTextHelper.ResolveText("LWN_ph_ellipsis", "..."), Action = "INTENT:Confess", NextNodeOnSuccess = "continue_chat" },
                        // 玩家死不承认（赃物上有名字）
                        new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_search_deny", "That's mine!"), Action = "NONE", NextNodeOnSuccess = "alert_search_deny_ack" },
                    }
                    : new List<DialogueInjector.DialogueTransition>
                    {
                        // 玩家自证清白
                        new() { PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_search_clean", "I told you I didn't take anything."), Action = "NONE", NextNodeOnSuccess = "" },
                    }
            };
        }

        /// <summary>查 TheftLedger 判定玩家背包是否有赃物</summary>
        static bool PlayerHasStolenItems()
        {
            MobileParty party = Hero.MainHero?.PartyBelongedTo;
            if (party?.ItemRoster == null) return false;
            foreach (ItemRosterElement item in party.ItemRoster)
            {
                if (item.EquipmentElement.Item == null) continue;
                string tag = TheftLedger.GetSourceTag(
                    item.EquipmentElement.Item.StringId, Hero.MainHero.StringId);
                if (!string.IsNullOrEmpty(tag)) return true;
            }
            return false;
        }

        // ═══════════════════════════════════════════════════════════════
        // 🆕 战斗认输对话构建（从 CombatManager 手写脚本抽取）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 构建玩家向 NPC 认输的对话脚本。
        /// 调用方：CombatManager.PlayerSurrenderToAgent。
        /// 赎金 = CrinePenaltyCalculator.ComputeSurrenderRansom()（玩家金币15%或200取大值）。
        /// </summary>
        public static DialogueInjector.DialogueInjectScript BuildPlayerSurrenderScript()
        {
            int baseRansom = CrimePenaltyCalculator.ComputeSurrenderRansom();
            int counterRansom = baseRansom * 2;

            return new DialogueInjector.DialogueInjectScript
            {
                SkipVanillaOpening = true,
                EntryNode = "player_lose",
                Nodes = new List<DialogueInjector.DialogueNode>
                {
                    new DialogueInjector.DialogueNode
                    {
                        Id = "player_lose",
                        // NPC：玩家认输后的勒索开场（报赎金）
                        NpcLine = LWNTextHelper.ResolveCompound("LWN_crime_surrender_npc_player_lose",
                            "Hmph, know you can't win? Hand over your purse — {RANSOM} denars and I'll spare you.",
                            ("RANSOM", baseRansom.ToString())),
                        Transitions = new List<DialogueInjector.DialogueTransition>
                        {
                            new DialogueInjector.DialogueTransition
                            {
                                // 玩家乖乖交出赎金
                                PlayerLine = LWNTextHelper.ResolveCompound("LWN_crime_player_surrender_pay",
                                    "...Here's {RANSOM} denars.",
                                    ("RANSOM", baseRansom.ToString())),
                                Action = "INTENT:PlayerSurrenderPay",
                                ActionParam = "pay",
                                NextNodeOnSuccess = "surrender_pay_ack"
                            },
                            new DialogueInjector.DialogueTransition
                            {
                                // 玩家求饶（魅力检定）
                                PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_surrender_beg", "Please let me go, I was just passing through..."),
                                CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                                Action = "INTENT:PlayerSurrenderBeg",
                                ActionParam = "beg",
                                NextNodeOnSuccess = "surrender_beg_ok",
                                NextNodeOnFail = "player_lose_counteroffer"
                            },
                            new DialogueInjector.DialogueTransition
                            {
                                // 玩家放狠话（威胁检定）
                                PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_surrender_threaten", "You dog! Kill me and you'll regret it!"),
                                CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                                Action = "INTENT:PlayerSurrenderThreaten",
                                ActionParam = "threaten",
                                NextNodeOnSuccess = "surrender_threaten_ok",
                                NextNodeOnFail = "surrender_threaten_fail"
                            }
                        }
                    },
                    // Ack nodes for player_lose
                    // NPC：收下赎金放人
                    new DialogueInjector.DialogueNode { Id = "surrender_pay_ack", NpcLine = LWNTextHelper.ResolveCompound("LWN_crime_surrender_npc_pay_ack", "Sensible choice. {RANSOM} denars. Watch your step next time — now get out!", ("RANSOM", baseRansom.ToString())), Transitions = new List<DialogueInjector.DialogueTransition>() },
                    // NPC：求饶成功，放玩家走
                    new DialogueInjector.DialogueNode { Id = "surrender_beg_ok", NpcLine = LWNTextHelper.ResolveText("LWN_crime_surrender_npc_beg_ok", "...Tch, lucky you. Get out of my sight."), Transitions = new List<DialogueInjector.DialogueTransition>() },
                    // NPC：威胁成功，放玩家走
                    new DialogueInjector.DialogueNode { Id = "surrender_threaten_ok", NpcLine = LWNTextHelper.ResolveText("LWN_crime_surrender_npc_threaten_ok", "...You're insane. Get out of my sight."), Transitions = new List<DialogueInjector.DialogueTransition>() },
                    // NPC：威胁失败，直接动手
                    new DialogueInjector.DialogueNode { Id = "surrender_threaten_fail", NpcLine = LWNTextHelper.ResolveText("LWN_crime_surrender_npc_threaten_fail", "You're asking for death!!"), Transitions = new List<DialogueInjector.DialogueTransition>() },
                    new DialogueInjector.DialogueNode
                    {
                        Id = "player_lose_counteroffer",
                        // NPC：求饶失败后的最后通牒（赎金翻倍）
                        NpcLine = LWNTextHelper.ResolveCompound("LWN_crime_surrender_npc_counteroffer",
                            "Last chance — {RANSOM} denars, or we keep fighting. Your choice.",
                            ("RANSOM", counterRansom.ToString())),
                        Transitions = new List<DialogueInjector.DialogueTransition>
                        {
                            new DialogueInjector.DialogueTransition
                            {
                                // 玩家接受翻倍赎金
                                PlayerLine = LWNTextHelper.ResolveCompound("LWN_crime_player_surrender_pay_counter",
                                    "...Here's {RANSOM} denars.",
                                    ("RANSOM", counterRansom.ToString())),
                                Action = "INTENT:PlayerSurrenderPay",
                                ActionParam = "counteroffer_beg",
                                NextNodeOnSuccess = "surrender_counter_ack"
                            },
                            new DialogueInjector.DialogueTransition
                            {
                                // 玩家拼死一战
                                PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_surrender_fight", "Fight to the death"),
                                Action = "INTENT:FightOn",
                                NextNodeOnSuccess = "surrender_fight_ack"
                            }
                        }
                    },
                    // NPC：收下翻倍赎金放人
                    new DialogueInjector.DialogueNode { Id = "surrender_counter_ack", NpcLine = LWNTextHelper.ResolveCompound("LWN_crime_surrender_npc_counter_ack", "Sensible choice. {RANSOM} denars. Now get out!", ("RANSOM", counterRansom.ToString())), Transitions = new List<DialogueInjector.DialogueTransition>() },
                    // NPC：应战（打到玩家爬不起来）
                    new DialogueInjector.DialogueNode { Id = "surrender_fight_ack", NpcLine = LWNTextHelper.ResolveText("LWN_crime_surrender_npc_fight_ack", "Fine! I'll beat you until you can't get up!"), Transitions = new List<DialogueInjector.DialogueTransition>() },
                }
            };
        }

        /// <summary>
        /// 构建 NPC 向玩家认输的对话脚本。
        /// 调用方：CombatManager.AcceptAgentSurrender。
        /// </summary>
        /// <param name="npcName">认输 NPC 的显示名称（用于 NPC 回应文本插值）</param>
        public static DialogueInjector.DialogueInjectScript BuildNpcSurrenderScript()
        {
            return new DialogueInjector.DialogueInjectScript
            {
                SkipVanillaOpening = true,
                EntryNode = "npc_beg",
                Nodes = new List<DialogueInjector.DialogueNode>
                {
                    new DialogueInjector.DialogueNode
                    {
                        Id = "npc_beg",
                        // NPC：被打服认输
                        NpcLine = LWNTextHelper.ResolveText("LWN_crime_surrender_npc_beg", "S-stop! I surrender!"),
                        Transitions = new List<DialogueInjector.DialogueTransition>
                        {
                            new DialogueInjector.DialogueTransition
                            {
                                // 玩家放走 NPC
                                PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_surrender_accept", "You may go."),
                                Action = "INTENT:ResolveNpcSurrender",
                                ActionParam = "accept",
                                NextNodeOnSuccess = "npc_surrender_accept_ack"
                            },
                            new DialogueInjector.DialogueTransition
                            {
                                // 玩家羞辱 NPC（跪下认错）
                                PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_surrender_humiliate", "Kneel and beg for forgiveness!"),
                                Action = "INTENT:ResolveNpcSurrender",
                                ActionParam = "humiliate",
                                NextNodeOnSuccess = "npc_surrender_humiliate_ack"
                            },
                            new DialogueInjector.DialogueTransition
                            {
                                // 玩家勒索 NPC 财物
                                PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_surrender_ransom", "Hand over your money and I'll spare you."),
                                Action = "INTENT:ResolveNpcSurrender",
                                ActionParam = "ransom",
                                NextNodeOnSuccess = "npc_surrender_ransom_ack"
                            },
                            new DialogueInjector.DialogueTransition
                            {
                                // 玩家不接受投降，继续打
                                PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_surrender_refuse", "Too late. Keep fighting!"),
                                Action = "INTENT:ResolveNpcSurrender",
                                ActionParam = "refuse",
                                NextNodeOnSuccess = "npc_surrender_refuse_ack"
                            }
                        }
                    },
                    // NPC：被放走的回应
                    new DialogueInjector.DialogueNode { Id = "npc_surrender_accept_ack", NpcLine = LWNTextHelper.ResolveText("LWN_crime_surrender_npc_accept_ack", "Th-thanks! I'll be on my way..."), Transitions = new List<DialogueInjector.DialogueTransition>() },
                    // NPC：被羞辱后的服软
                    new DialogueInjector.DialogueNode { Id = "npc_surrender_humiliate_ack", NpcLine = LWNTextHelper.ResolveText("LWN_crime_surrender_npc_humiliate_ack", "...I've learned my lesson."), Transitions = new List<DialogueInjector.DialogueTransition>() },
                    // NPC：交钱求饶
                    new DialogueInjector.DialogueNode { Id = "npc_surrender_ransom_ack", NpcLine = LWNTextHelper.ResolveText("LWN_crime_surrender_npc_ransom_ack", "O-okay... take it all! Please spare me..."), Transitions = new List<DialogueInjector.DialogueTransition>() },
                    // NPC：拒绝投降后的拼死反扑
                    new DialogueInjector.DialogueNode { Id = "npc_surrender_refuse_ack", NpcLine = LWNTextHelper.ResolveText("LWN_crime_surrender_npc_refuse_ack", "No —! I'll fight you to the end!"), Transitions = new List<DialogueInjector.DialogueTransition>() },
                }
            };
        }

        /// <summary>
        /// 防御兜底：Alert 质问但案件已结案（Resolved）时注入的简短"已了结"对话。
        /// 玩家已赔钱/坐牢/自首 → NPC 不再质问要账，只说一句"事已了结"放玩家走。
        /// 正常路径结案广播（WorldEventStore.TransitionStage → AgentAIController.ClearAlertsForEvent）
        /// 已清掉目击者警戒不会走到这，此处兜底时序竞争（如质问已入队后结案）。
        /// </summary>
        public static DialogueInjector.DialogueInjectScript BuildResolvedAlertScript()
        {
            return new DialogueInjector.DialogueInjectScript
            {
                SkipVanillaOpening = true,
                EntryNode = "injectedStart",
                Nodes = new List<DialogueInjector.DialogueNode>
                {
                    new DialogueInjector.DialogueNode
                    {
                        Id = "injectedStart",
                        // NPC：事已了结，放人走（不标价、不追责——案件已经结清）
                        NpcLine = LWNTextHelper.ResolveText("LWN_crime_alert_already_settled", "...Weren't you already dealt with? Be on your way."),
                        Transitions = new List<DialogueInjector.DialogueTransition>
                        {
                            new DialogueInjector.DialogueTransition
                            {
                                // 玩家离开（安全离开，无后果）
                                PlayerLine = LWNTextHelper.ResolveText("LWN_crime_player_leave", "I'm leaving."),
                                Action = "INTENT:WalkAway",
                                ActionParam = "safe",
                                NextNodeOnSuccess = ""
                            }
                        }
                    }
                }
            };
        }
    }
}
