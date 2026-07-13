using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
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

        // ── Transitions 工厂 ──


        /// <summary>单选项"…（继续）"→ next。用于 ack Node。</summary>
        static List<DialogueInjector.DialogueTransition> SingleContinue(string next)
            => new() { new() { PlayerLine = "…", NextNodeOnSuccess = next } };

        /// <summary>terminal 选项（关窗）。</summary>
        static List<DialogueInjector.DialogueTransition> CloseOptions(string line = "…")
            => new() { new() { PlayerLine = line, NextNodeOnSuccess = "" } };

        // ── 原子 Transition 工厂 ──


        /// <summary>共享的"离开"选项。替代原来的 BuildContinueChatNode 中的走人选项。</summary>
        static List<DialogueInjector.DialogueTransition> ContinueOptions(string walkAwayLine = "我得走了。")
            => new() { WalkAway(walkAwayLine) };

        static DialogueInjector.DialogueTransition WalkAway(string playerLine = "（转身就走）")
            => new() { PlayerLine = playerLine, Action = "INTENT:WalkAway", NextNodeOnSuccess = "" };


        /// <summary>
        /// 玩家对 NPC 点"交谈"时调用。</summary>
        public static DialogueInjector.DialogueInjectScript BuildScript(Hero speaker, Hero listener)
        {
            Settlement settlement = speaker.CurrentSettlement;
            if (settlement == null) return null;
            WorldEvent evt = WorldEventStore.FindActive(settlement.StringId);
            if (evt == null) return null;

            PlaceholderResolver r = new PlaceholderResolver(evt, speaker, listener);
            Agent speakerAgent = TaleWorlds.CampaignSystem.Campaign.Current?.ConversationManager?.OneToOneConversationAgent as Agent;
            IntentContext ctx = new IntentContext(speakerAgent, speaker: speaker, worldEvent: evt);

            // 按说话者身份分派
            DialogueInjector.DialogueInjectScript script;
            if (IsAuthority(speaker, evt))
                script = BuildAuthorityScript(r, ctx);
            else if (evt.WitnessHeroIds?.Contains(speaker.StringId) == true)
                script = BuildWitnessScript(r, ctx);
            else if (evt.SuspectHeroId == speaker.StringId)
                script = BuildSuspectScript(r, ctx);
            else
                script = BuildBystanderScript(r, ctx);

            // 日志：打印每个 node 的最终填充文本，方便排查占位符遗漏
            DialogueInjector.LogScript(script, $"[CrimeDialog] speaker={speaker.Name} stage={evt.Stage}");

            return script;
        }

        private static bool IsAuthority(Hero npc, WorldEvent evt)
        {
            Hero authority = WorldEventStore.GetAuthorityNpc(evt);
            return npc == authority || (npc?.Occupation == Occupation.Headman || npc?.Occupation == Occupation.RuralNotable);
        }

        private static DialogueInjector.DialogueInjectScript BuildAuthorityScript(
            PlaceholderResolver r, IntentContext ctx)
        {
            List<DialogueInjector.DialogueNode> nodes = new List<DialogueInjector.DialogueNode>();
            WorldEvent evt = ctx.ActiveEvent;
            string entryOption = r.Resolve("{SpeakerRole}，听说{TargetSettlementName}出了点事？", "EntryOption");
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
                        entryOption = r.Resolve("关于{TargetSettlementName}那个案子……", "EntryOption");
                        BuildReportNode(nodes, r, ctx);
                    }
                    else
                    {
                        //玩家没有接调查任务,请求玩家调查
                        entryOption = r.Resolve("{SpeakerRole}，听说{TargetSettlementName}出了点事？", "EntryOption");
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
                    else
                    {
                        //是别的人干的，请求玩家去帮忙
                        entryOption = r.Resolve("{SpeakerRole}，关于那桩悬赏……", "EntryOption");
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
            bool skipOpening = (evt.Stage == EventStage.Active && evt.SuspectIsPlayer)
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
                NpcLine = r.Resolve("（{SpeakerEmotion}地）{TimeWord}{TargetSettlementName}的{CrimeScene}{CrimeVerbPast}{StolenItemClause}。{InvestigationProgressWord}。{WitnessCountWord}，{SuspectDescription}。{SpeakerPlayerAddr}能帮忙查查吗？", "NpcLine"),
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    new DialogueInjector.DialogueTransition
                    {
                        PlayerLine = "我可以帮忙查查是谁干的。",
                        Action = "INTENT:Investigate",
                        NextNodeOnSuccess = "discovery_accept_ack"
                    },
                    new DialogueInjector.DialogueTransition
                    {
                        PlayerLine = "我还有事。",
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
                    PlayerLine = "是我干的。",
                    Action = "INTENT:Confess",
                    NextNodeOnSuccess = "confess"
                });
                BuildConfessSubtree(nodes, r, ctx);
            }

            nodes.Add(node);
            nodes.Add(Node("discovery_accept_ack", r.Resolve("拜托了！查出来了{SpeakerSelfRef}必有重谢。"), "continue_chat"));
            nodes.Add(Node("discovery_decline_ack", r.Resolve("那{SpeakerPlayerAddr}忙吧……{SpeakerSelfRef}们自己想办法。"), "continue_chat"));
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
                NpcLine = r.Resolve("{SpeakerPlayerAddr}？！……好，既然自己认了，咱们可以商量。有什么要说的？"),
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    new()
                    {
                        PlayerLine = r.Resolve("我愿意赔。你说个数。"),
                        Action = "NONE",
                        NextNodeOnSuccess = "restitution_demand"
                    },
                    new()
                    {
                        PlayerLine = "（讪笑）开个玩笑……刚才是我胡说的",
                        CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                        Action = "INTENT:CharmDefense",
                        NextNodeOnSuccess = "charm_ok",
                        NextNodeOnFail = "charm_fail"
                    },
                    new() { PlayerLine = "（转身就走）", Action = "INTENT:WalkAway", NextNodeOnSuccess = "" },
                }
            });
            nodes.Add(Node("charm_ok", r.Resolve("……说清楚？好，{SpeakerSelfRef}倒要听听。"), "continue_chat"));
            nodes.Add(Node("charm_fail", r.Resolve("说清楚？证据确凿，没什么好说的。"), "continue_chat"));
            BuildRestitutionSubtree(nodes, r, ctx);
        }

        /// <summary>赔偿子树：NPC 开价 → 全价付 / 砍价 / 放弃 → 付款确认。依赖调用方已添加 continue_chat / farewell。</summary>
        private static void BuildRestitutionSubtree(List<DialogueInjector.DialogueNode> nodes, PlaceholderResolver r, IntentContext ctx)
        {
            WorldEvent evt = r.Event;
            int cost = CrimePenaltyCalculator.ComputeCost(evt, CostType.Restitution);
            int haggleCost = CrimePenaltyCalculator.ComputeHaggleAmount(cost, 0.5f);

            // NPC 开价台词：优先用 RestitutionBreakdown，否则兜底
            string demandNpcLine = r.Resolve("{RestitutionBreakdown}", "NpcLine");
            if (string.IsNullOrEmpty(demandNpcLine) || demandNpcLine == "{RestitutionBreakdown}")
                demandNpcLine = r.Resolve($"罚款{cost}第纳尔。你认不认？", "NpcLine");

            // demand 节点：NPC 开价
            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "restitution_demand",
                NpcLine = demandNpcLine,
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    new() { PlayerLine = r.Resolve($"好，我赔（{cost} 第纳尔）", "PlayerLine"), Action = "INTENT:PayRestitution", ActionParam = null, NextNodeOnSuccess = "restitution_pay_ack" },
                    new() { PlayerLine = "太贵了，能便宜点吗？", CheckType = DialogueInjector.TransitionCheckType.SkillCheck, Action = "INTENT:Settle", NextNodeOnSuccess = "restitution_haggle_ok", NextNodeOnFail = "restitution_haggle_fail" },
                    new() { PlayerLine = "太贵了，不赔。", Action = "NONE", NextNodeOnSuccess = "continue_chat" },
                }
            });

            // haggle_ok：砍价成功
            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "restitution_haggle_ok",
                NpcLine = r.Resolve($"……行，算你{haggleCost}，不能再少了。", "NpcLine"),
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    new() { PlayerLine = r.Resolve($"行，就这个数。（{haggleCost} 第纳尔）", "PlayerLine"), Action = "INTENT:PayRestitution", ActionParam = "haggle", NextNodeOnSuccess = "restitution_pay_ack" },
                    new() { PlayerLine = "还是太贵，不赔了。", Action = "NONE", NextNodeOnSuccess = "continue_chat" },
                }
            });

            // haggle_fail → 回到 demand
            nodes.Add(Node("restitution_haggle_fail", r.Resolve("不行，一文都不能少。", "NpcLine"), "restitution_demand"));

            // pay_ack：付款确认
            nodes.Add(Node("restitution_pay_ack", r.Resolve("好，钱留下，这事就算了。", "NpcLine"), "continue_chat"));
        }

        private static void BuildReportNode(List<DialogueInjector.DialogueNode> nodes, PlaceholderResolver r, IntentContext ctx)
        {

            WorldEvent evt = r.Event;
            DialogueInjector.DialogueNode node = new DialogueInjector.DialogueNode
            {
                Id = "injectedStart",
                NpcLine = "怎么样，查到什么了吗？",
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    new DialogueInjector.DialogueTransition
                    {
                        PlayerLine = "是附近藏身处的强盗干的！",
                        CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                        Action = "INTENT:FrameSuspect",
                        ActionParam = "bandit",
                        NextNodeOnSuccess = "frame_bandit_ok",
                        NextNodeOnFail = "frame_bandit_fail"
                    },
                    new DialogueInjector.DialogueTransition { PlayerLine = "还没查到什么。", Action = "NONE", NextNodeOnSuccess = "report_nothing_ack" },
                    new DialogueInjector.DialogueTransition
                    {
                        PlayerLine = "我还有事。",
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
                            PlayerLine = $"是 {target.DisplayName} 干的——[出示{evItem.ItemName}]",
                            CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                            Action = "INTENT:FrameSuspect",
                            ActionParam = target.TargetId,
                            NextNodeOnSuccess = okId,
                            NextNodeOnFail = failId
                        });
                        nodes.Add(Node(okId, $"（仔细看了看{evItem.ItemName}）……这确实是他的东西。好，那就是他了！", "continue_chat"));
                        nodes.Add(Node(failId, $"（仔细看了看{evItem.ItemName}）……这东西说明不了什么。{r.Resolve("{SpeakerPlayerAddr}")}再去查查。", "continue_chat"));
                        frameIdx++;
                    }
                }
                else
                {
                    string okId = $"frame_{frameIdx}_ok";
                    string failId = $"frame_{frameIdx}_fail";
                    node.Transitions.Insert(node.Transitions.Count - 1, new DialogueInjector.DialogueTransition
                    {
                        PlayerLine = $"是 {target.DisplayName} 干的。",
                        CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                        Action = "INTENT:FrameSuspect",
                        ActionParam = target.TargetId,
                        NextNodeOnSuccess = okId,
                        NextNodeOnFail = failId
                    });
                    nodes.Add(Node(okId, $"是{target.DisplayName}干的？……好，{r.Resolve("{SpeakerSelfRef}")}信你。", "continue_chat"));
                    nodes.Add(Node(failId, $"就凭一句话？{r.Resolve("{SpeakerPlayerAddr}")}再去查查。", "continue_chat"));
                    frameIdx++;
                }
            }

            // 如果玩家是贼 → 加"主动认栽"
            if (evt.InitiatorIsPlayer)
            {
                node.Transitions.Add(new DialogueInjector.DialogueTransition
                {
                    PlayerLine = "（低头）……是我干的。",
                    Action = "INTENT:Confess",
                    NextNodeOnSuccess = "confess"
                });
                BuildConfessSubtree(nodes, r, ctx);
            }

            nodes.Add(node);
            nodes.Add(Node("frame_bandit_ok", r.Resolve("藏身处的强盗？好，那就是他们了！{SpeakerSelfRef}这就张罗悬赏。"), "continue_chat"));
            nodes.Add(Node("frame_bandit_fail", r.Resolve("强盗？光凭{SpeakerPlayerAddr}一句话可不行……再去查查。"), "continue_chat"));
            nodes.Add(Node("report_nothing_ack", r.Resolve("那你再去看看。{InvestigationProgressWord}。"), "continue_chat"));
            nodes.Add(Node("report_leave_ack", r.Resolve("快去查，有消息了来告诉{SpeakerSelfRef}。"), "continue_chat"));
            AddContinueChatWithFarewell(nodes, r);
        }

        private static void BuildConfrontPlayerNode(List<DialogueInjector.DialogueNode> nodes, PlaceholderResolver r, IntentContext ctx)
        {
            WorldEvent evt = r.Event;
            // 大地图对话无法叫守卫/触发战斗 → 威胁失败的 NPC 回应降级为口头警告
            string threatFailLine = ctx.IsInMission
                ? r.Resolve("威胁{SpeakerSelfRef}？来人！")
                : r.Resolve("威胁{SpeakerSelfRef}？{SpeakerPlayerAddr}等着，{SpeakerSelfRef}会告到上面去。");
            DialogueInjector.DialogueNode node = new DialogueInjector.DialogueNode
            {
                Id = "injectedStart",
                NpcLine = r.Resolve("（{SpeakerEmotion}地）{SpeakerPlayerAddr}还敢来？{PrimaryWitnessDesc}{TimeWord}就来找{SpeakerSelfRef}，说亲眼瞧见是{SpeakerPlayerAddr}{CrimeVerb}。有什么要说的？", "NpcLine"),
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    new DialogueInjector.DialogueTransition
                    {
                        PlayerLine = "你们搞错了。给我个机会说清楚。",
                        CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                        Action = "INTENT:CharmDefense",
                        NextNodeOnSuccess = "confront_charm_ok",
                        NextNodeOnFail = "confront_charm_fail"
                    },
                    new DialogueInjector.DialogueTransition
                    {
                        PlayerLine = "赔偿的事……你要多少？",
                        Action = "NONE",
                        NextNodeOnSuccess = "restitution_demand"
                    },
                    new DialogueInjector.DialogueTransition
                    {
                        PlayerLine = "你再说一遍？（手按在剑柄上）",
                        CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                        Action = "INTENT:Threat",
                        NextNodeOnSuccess = "confront_threat_ok",
                        NextNodeOnFail = "confront_threat_fail"
                    },
                    new DialogueInjector.DialogueTransition { PlayerLine = "（转身就走）", Action = "INTENT:WalkAway", NextNodeOnSuccess = "" },
                }
            };
            nodes.Add(node);

            // ack nodes
            nodes.Add(Node("confront_charm_ok", r.Resolve("……{SpeakerPlayerAddr}说的也有道理。那{SpeakerSelfRef}再查查。"), "continue_chat"));
            nodes.Add(Node("confront_charm_fail", r.Resolve("说清楚？证据确凿，没什么好说的。"), "continue_chat"));
            nodes.Add(Node("confront_threat_ok", r.Resolve("……{SpeakerSelfRef}不说了。{SpeakerPlayerAddr}走吧。"), "continue_chat"));
            nodes.Add(Node("confront_threat_fail", threatFailLine, "continue_chat"));

            BuildRestitutionSubtree(nodes, r, ctx);
            AddContinueChatWithFarewell(nodes, r);
        }

        private static void BuildBountyOfferNode(List<DialogueInjector.DialogueNode> nodes, PlaceholderResolver r, IntentContext ctx)
        {
            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "injectedStart",
                NpcLine = r.Resolve("还记得{TimeWord}{CrimeVerbPast}的事吗？查清楚了——是{SuspectDescription}干的。村上凑了{BountyAmount}第纳尔悬赏，谁把他抓回来就给谁。{SpeakerPlayerAddr}接不接？", "NpcLine"),
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    new DialogueInjector.DialogueTransition { PlayerLine = "我接这个悬赏！", Action = "INTENT:AcceptBountyQuest", NextNodeOnSuccess = "bounty_accept_ack" },
                    new DialogueInjector.DialogueTransition { PlayerLine = "我先想想。", Action = "NONE", NextNodeOnSuccess = "continue_chat" },
                }
            });
            nodes.Add(Node("bounty_accept_ack", r.Resolve("好！人就交给{SpeakerPlayerAddr}了。"), "continue_chat"));
            AddContinueChatWithFarewell(nodes, r);
        }

        private static void BuildRetaliationNode(List<DialogueInjector.DialogueNode> nodes, PlaceholderResolver r, IntentContext ctx)
        {
            WorldEvent evt = r.Event;

            if (evt.SuspectIsPlayer)
            {
                string npcLine = r.Resolve("（{SpeakerEmotion}地）好话说尽，{SpeakerPlayerAddr}非要走到这一步。{SpeakerSelfRef}也不想多费口舌——今天不给{TargetSettlementName}一个交代，别想走着出去。", "NpcLine");
                var transitions = new List<DialogueInjector.DialogueTransition>
                {
                    new DialogueInjector.DialogueTransition { PlayerLine = r.Resolve("我赔钱！你说个数。"), Action = "NONE", NextNodeOnSuccess = "restitution_detail" },
                    new DialogueInjector.DialogueTransition { PlayerLine = "我走了。", Action = "INTENT:WalkAway", NextNodeOnSuccess = "" },
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
                string npcLine = r.Resolve("（{SpeakerEmotion}地）客客气气说话不管用，那就只能动手了。我们已经雇了人去抓{SuspectDescription}。{SpeakerPlayerAddr}要是站在我们这边的，可以带他们去。", "NpcLine");
                var transitions = new List<DialogueInjector.DialogueTransition>
                {
                    new DialogueInjector.DialogueTransition { PlayerLine = "我带人去！", Action = "INTENT:LeadRetaliation", NextNodeOnSuccess = "retaliate_lead_ack" },
                    new DialogueInjector.DialogueTransition { PlayerLine = "我没空。", Action = "NONE", NextNodeOnSuccess = "" },
                };
                nodes.Add(new DialogueInjector.DialogueNode
                {
                    Id = "injectedStart",
                    NpcLine = npcLine,
                    Transitions = transitions
                });
                nodes.Add(Node("retaliate_lead_ack", r.Resolve("好！有{SpeakerPlayerAddr}带队，那{SuspectDescription}跑不了。"), "continue_chat"));
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

            string witnessedDesc = BuildWitnessedActionDescription(testimony);

            DialogueInjector.DialogueNode node = new DialogueInjector.DialogueNode
            {
                Id = "injectedStart",
                NpcLine = evt.InitiatorIsPlayer
                    ? r.Resolve($"（{{SpeakerEmotion}}地）{{SpeakerPlayerAddr}}是来问{{CrimeScene}}的事？{{SpeakerSelfRef}}看见了——{witnessedDesc}。")
                    : r.Resolve($"（{{SpeakerEmotion}}地）{{SpeakerSelfRef}}{{TimeWord}}在{{CrimeScene}}附近看见了——{witnessedDesc}"),
                Transitions = new List<DialogueInjector.DialogueTransition>()
            };

            if (evt.InitiatorIsPlayer && !evt.WitnessesSilenced)
            {
                node.Transitions.Add(new DialogueInjector.DialogueTransition
                {
                    PlayerLine = "（给些钱）这事你别往外说……",
                    CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                    Action = "INTENT:SilenceWitness",
                    ActionParam = "bribe",
                    NextNodeOnSuccess = "witness_silence_ack",
                    NextNodeOnFail = "witness_silence_fail"
                });
                node.Transitions.Add(new DialogueInjector.DialogueTransition
                {
                    PlayerLine = "（威胁）你什么也没看见，明白吗？",
                    CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                    Action = "INTENT:SilenceWitness",
                    ActionParam = "threat",
                    NextNodeOnSuccess = "witness_threat_ack",
                    NextNodeOnFail = "witness_threat_fail"
                });
                node.Transitions.Add(new DialogueInjector.DialogueTransition { PlayerLine = "当我没来过。", Action = "INTENT:WalkAway", NextNodeOnSuccess = "" });
            }
            else if (evt.WitnessesSilenced)
            {
                node.NpcLine = r.Resolve("（紧张地看了看四周）{SpeakerPlayerAddr}找错人了。{SpeakerSelfRef}什么也不知道。", "NpcLine");
                node.Transitions.Add(new DialogueInjector.DialogueTransition { PlayerLine = "……好吧。", Action = "NONE", NextNodeOnSuccess = "continue_chat" });
            }
            else
            {
                node.Transitions.Add(new DialogueInjector.DialogueTransition { PlayerLine = "能说说那人的特征吗？", Action = "NONE", NextNodeOnSuccess = "witness_desc_ack" });
                node.Transitions.Add(new DialogueInjector.DialogueTransition { PlayerLine = "谢谢，我知道了。", Action = "NONE", NextNodeOnSuccess = "continue_chat" });
            }

            nodes.Add(node);
            nodes.Add(Node("witness_silence_ack", r.Resolve("……好吧，{SpeakerSelfRef}什么也没看见。"), "continue_chat"));
            nodes.Add(Node("witness_silence_fail", r.Resolve("（提高嗓门）你当{SpeakerSelfRef}是什么人？！{SpeakerSelfRef}这就去告诉村长！"), "continue_chat"));
            nodes.Add(Node("witness_threat_ack", r.Resolve("明白、明白……{SpeakerSelfRef}一个字也不说。"), "continue_chat"));
            nodes.Add(Node("witness_threat_fail", r.Resolve("（后退一步，手按在腰间）你敢威胁{SpeakerSelfRef}？！来人——！"), "continue_chat"));
            nodes.Add(Node("witness_desc_ack", r.Resolve("那人……{SuspectDescription}。"), "continue_chat"));
            AddContinueChatWithFarewell(nodes, r);
            return new DialogueInjector.DialogueInjectScript { EntryOption = "听说你看到了……？", EntryNode = "injectedStart", Nodes = nodes };
        }

        private static DialogueInjector.DialogueInjectScript BuildSuspectScript(
            PlaceholderResolver r, IntentContext ctx)
        {
            List<DialogueInjector.DialogueNode> nodes = new List<DialogueInjector.DialogueNode>
            {
                new DialogueInjector.DialogueNode
                {
                    Id = "injectedStart",
                    NpcLine = r.Resolve("（警惕地）{SpeakerPlayerAddr}盯着{SpeakerSelfRef}看什么？", "NpcLine"),
                    Transitions = new List<DialogueInjector.DialogueTransition>
                    {
                        new DialogueInjector.DialogueTransition
                        {
                            PlayerLine = "跟我走一趟，村长找你有事。",
                            CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                            Action = "INTENT:LureArrest",
                            NextNodeOnSuccess = "suspect_lure_ack",
                            NextNodeOnFail = "suspect_lure_fail"
                        },
                        new DialogueInjector.DialogueTransition { PlayerLine = "快跑！村里人在抓你。", Action = "INTENT:BetrayQuest", NextNodeOnSuccess = "suspect_betray_ack" },
                        new DialogueInjector.DialogueTransition { PlayerLine = "没什么。", Action = "INTENT:WalkAway", NextNodeOnSuccess = "" },
                    }
                }
            };
            nodes.Add(Node("suspect_lure_ack", r.Resolve("什么？！{SpeakerSelfRef}什么也没干……"), "continue_chat"));
            nodes.Add(Node("suspect_lure_fail", r.Resolve("村长找我？他自己怎么不来？{SpeakerPlayerAddr}少在这骗人。"), "continue_chat"));
            nodes.Add(Node("suspect_betray_ack", r.Resolve("什么？！……谢了！"), "continue_chat"));
            AddContinueChatWithFarewell(nodes, r);
            return new DialogueInjector.DialogueInjectScript { EntryOption = "（打量了一下）……", EntryNode = "injectedStart", Nodes = nodes };
        }

        private static DialogueInjector.DialogueInjectScript BuildBystanderScript(
            PlaceholderResolver r, IntentContext ctx)
        {
            List<DialogueInjector.DialogueNode> nodes = new List<DialogueInjector.DialogueNode>();
            WorldEvent evt = ctx.ActiveEvent;

            string npcLine = evt.Stage switch
            {
                EventStage.Emerging => r.Resolve("（压低声音）{SpeakerPlayerAddr}听说了吗？{TargetSettlementName}的{CrimeScene}{CrimeVerbPast}！谁干的还不知道。", "NpcLine"),
                EventStage.Active => r.Resolve("听说了吗？是{SuspectDescription}干的！村里悬赏{BountyAmount}第纳尔抓他呢。", "NpcLine"),
                EventStage.Confrontation => r.Resolve("（紧张地）{TargetSettlementName}的人真动手了——雇了打手满世界找人。这事闹大了……", "NpcLine"),
                _ => r.Resolve("这事好像已经过去了……", "NpcLine"),
            };

            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "injectedStart",
                NpcLine = npcLine,
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    new DialogueInjector.DialogueTransition { PlayerLine = "详细说说？", Action = "NONE", NextNodeOnSuccess = "bystander_detail_ack" },
                    new DialogueInjector.DialogueTransition { PlayerLine = "哦。", Action = "NONE", NextNodeOnSuccess = "continue_chat" },
                }
            });
            nodes.Add(Node("bystander_detail_ack", r.Resolve("我就知道这么多……"), "continue_chat"));
            AddContinueChatWithFarewell(nodes, r);

            return new DialogueInjector.DialogueInjectScript { EntryOption = "最近村里有什么新鲜事？", EntryNode = "injectedStart", Nodes = nodes };
        }

        /// <summary>继续聊 node：NPC 说完事后 → 玩家走人。告别语按阶段动态切换，引擎展示前才求值。</summary>
        private static DialogueInjector.DialogueNode BuildContinueChatNode(PlaceholderResolver r)
        {
            WorldEvent evt = r.Event;
            return new DialogueInjector.DialogueNode
            {
                Id = "continue_chat",
                NpcLine = "还有什么别的想说的吗?",
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    new DialogueInjector.DialogueTransition
                    {
                        PlayerLine = "我得走了。",
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
                    if (evt.PlayerTookInvestigationQuest)
                        return r.Resolve("快去查，有消息了来告诉{SpeakerSelfRef}。");
                    if (evt.SuspectIsPlayer && evt.Stage == EventStage.Active)
                        return r.Resolve("（冷冷地）这事不算完。");
                    if (evt.Stage == EventStage.Confrontation)
                        return r.Resolve("这事没完。");
                    return r.Resolve("嗯，{SpeakerPlayerAddr}去吧。");
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
        public static string BuildWitnessedActionDescription(WitnessTestimony testimony)
        {
            if (testimony?.Actions == null || testimony.Actions.Count == 0)
                return "有人在闹事";

            List<string> parts = new List<string>();
            foreach (ActionRecord a in testimony.Actions.OrderByDescending(a => a.AlertValue))
            {
                string desc = a.ActionType switch
                {
                    "Crouching" => "鬼鬼祟祟蹲了半天",
                    "WeaponDrawn" => "在村里拔刀",
                    "StealUIOpen" => "翻箱倒柜",
                    "Steal" when a.ItemName != null =>
                        a.TargetName != null
                            ? $"偷了{a.TargetName}的{a.ItemName}"
                            : $"偷了{a.ItemName}",
                    "Steal" => "偷了东西",
                    "AttackAlly" when a.TargetName != null => $"动手打了{a.TargetName}",
                    "AttackAlly" => "动手打人",
                    "Knockout" when a.TargetName != null => $"把{a.TargetName}打晕了",
                    "Knockout" => "把人打晕了",
                    _ => null
                };
                if (desc != null) parts.Add(desc);
            }
            return parts.Count switch
            {
                0 => "有人在闹事",
                1 => parts[0],
                2 => $"{parts[0]}，还{parts[1]}",
                _ => $"{parts[0]}、{parts[1]}，还{parts[2]}"
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

            // 两阶段回落：① NpcSpeech.csv（含 Narrative 过渡）→ ② 硬编码
            string npcOpening =
                NpcSpeechResolver.Resolve($"L3_{npcIntent}_{primaryAction}", speaker, Hero.MainHero,
                    narrativeFallback: new NarrativeFilters
                    {
                        EventName = "L3AlertIntercept",
                        GoalType = npcIntent.ToString(),
                        Outcome = primaryAction.ToString(),
                    })
                ?? HardcodedAlertLine(r, npcIntent, primaryAction);

            BuildAlertTransitionsSubtree(nodes, r, ctx, npcOpening);

            // continue_chat — 阶段 >= Active 时无退路
            bool escalated = worldEvt != null && worldEvt.Stage >= EventStage.Active;
            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "continue_chat",
                NpcLine = escalated ? "最后一次警告——别逼我叫人！" : "还有什么想说的？",
                Transitions = escalated
                    ? new List<DialogueInjector.DialogueTransition>
                    {
                        new() { PlayerLine = "（拔剑）谁敢拦我！", Action = "INTENT:FightVillagers", NextNodeOnSuccess = "alert_esc_fight_ack" },
                        new() { PlayerLine = r.Resolve("我认罚。（{AlertFineCost} 第纳尔）"), Action = "INTENT:PayRestitution", ActionParam = "alert_fine", NextNodeOnSuccess = "alert_esc_fine_ack" },
                        new() { PlayerLine = "我没钱。要抓就抓吧。", Action = "INTENT:SurrenderJail", ActionParam = "surrender_jail", NextNodeOnSuccess = "alert_esc_jail_ack" },
                    }
                    : new List<DialogueInjector.DialogueTransition>
                    {
                        new() { PlayerLine = "我走了。", Action = "INTENT:WalkAway", NextNodeOnSuccess = "" }
                    }
            });
            // Escalated ack nodes
            nodes.Add(Node("alert_esc_fight_ack", r.Resolve("{SPEAKER_PLAYER_ADDR}疯了！快叫人！")));
            nodes.Add(Node("alert_esc_fine_ack", r.Resolve("扰乱治安，罚款{AlertFineCost}第纳尔。算你识相。别再来了。")));
            nodes.Add(Node("alert_esc_jail_ack", r.Resolve("没钱还敢闹事？！来人，把他关进地牢！")));

            return new DialogueInjector.DialogueInjectScript { SkipVanillaOpening = true, EntryNode = "injectedStart", Nodes = nodes };
        }

        /// <summary>L3 警戒质问的硬编码兜底台词（CSV 和 Narrative 均未命中时）。</summary>
        static string HardcodedAlertLine(PlaceholderResolver r, ConfrontationType npcIntent, PlayerActionType primaryAction)
        {
            return npcIntent switch
            {
                ConfrontationType.Deter => primaryAction switch
                {
                    PlayerActionType.WeaponDrawn =>
                        r.Resolve("（{SPEAKER_EMOTION}地）把{ITEM}收起来！{SPEAKER_PLAYER_ADDR}！这是村子，不是战场！"),
                    _ => // Crouching
                        r.Resolve("（{SPEAKER_EMOTION}地）喂！{SPEAKER_PLAYER_ADDR}！蹲在那鬼鬼祟祟干什么？"),
                },

                ConfrontationType.Search =>
                    r.Resolve("（{SPEAKER_EMOTION}地）{SPEAKER_PLAYER_ADDR}在翻什么？把手拿开，让{SPEAKER_SELF}看看你的包。"),

                ConfrontationType.Recover =>
                    r.Resolve("（{SPEAKER_EMOTION}地）{SPEAKER_SELF}看见了！{SPEAKER_PLAYER_ADDR}偷了{StolenItemName}！交出来！"),

                ConfrontationType.Stop => primaryAction switch
                {
                    PlayerActionType.AttackAlly =>
                        r.Resolve("（{SPEAKER_EMOTION}地）{SPEAKER_PLAYER_ADDR}竟敢动手打人？！住手！"),
                    PlayerActionType.Knockout =>
                        r.Resolve("（{SPEAKER_EMOTION}地）{SPEAKER_PLAYER_ADDR}把{TARGET}打晕了！来人！"),
                    _ => r.Resolve("（{SPEAKER_EMOTION}地）住手！")
                },
                _ => r.Resolve("（{SPEAKER_EMOTION}地）{SPEAKER_PLAYER_ADDR}！你在干什么？")
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
                    BuildRecoverSubtree(nodes, r, npcOpening);
                    break;
                //制止
                case ConfrontationType.Stop:
                    BuildStopSubtree(nodes, r, npcOpening);
                    break;
            }
        }

        /// <summary>Deter 质问子树：驱离警告。transitions + ack nodes 自包含。</summary>
        static void BuildDeterSubtree(
            List<DialogueInjector.DialogueNode> nodes,
            PlaceholderResolver r,
            IntentContext ctx,
            string npcOpening)
        {
            var action = ctx.TriggerAction;
            var worldEvt = ctx.ActiveEvent;
            string complyLine = action == PlayerActionType.WeaponDrawn
                ? "好，我收起来。"
                : "没什么，我这就走。";
            var transitions = new List<DialogueInjector.DialogueTransition>
            {
                new() { PlayerLine = complyLine, Action = "INTENT:Comply", ActionParam = "comply", NextNodeOnSuccess = "alert_comply_ack" },
                new() { PlayerLine = "关你什么事？（挑衅）", CheckType = DialogueInjector.TransitionCheckType.SkillCheck, Action = "INTENT:Threat", NextNodeOnSuccess = "alert_deter_threat_ok", NextNodeOnFail = "alert_deter_threat_fail" },
            };
            if (worldEvt != null && worldEvt.Stage >= EventStage.Active)
            {
                transitions.Add(new() { PlayerLine = r.Resolve("我认罚。（{AlertFineCost} 第纳尔）"), Action = "INTENT:PayRestitution", ActionParam = "alert_fine", NextNodeOnSuccess = "alert_deter_fine_ack" });
                transitions.Add(new() { PlayerLine = "我没钱。要抓就抓吧。", Action = "INTENT:SurrenderJail", ActionParam = "surrender_jail", NextNodeOnSuccess = "alert_deter_jail_ack" });
            }

            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "injectedStart",
                NpcLine = npcOpening,
                Transitions = transitions
            });

            nodes.Add(Node("alert_comply_ack", r.Resolve(action == PlayerActionType.WeaponDrawn
                ? "……别再让{SPEAKER_SELF}看见你在这拔{ITEM}。"
                : "……别再让{SPEAKER_SELF}看见你鬼鬼祟祟的。")));
            nodes.Add(Node("alert_deter_threat_ok", r.Resolve("……算了。")));
            nodes.Add(Node("alert_deter_threat_fail", r.Resolve("来人！这有个闹事的！")));
            nodes.Add(Node("alert_deter_fine_ack", r.Resolve("扰乱治安，罚款{AlertFineCost}第纳尔。算你识相。别再来了。")));
            nodes.Add(Node("alert_deter_jail_ack", r.Resolve("没钱还敢闹事？！来人，把他关进地牢！")));
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
                    new() { PlayerLine = "……行，你看吧。", Action = "INTENT:SubmitToSearch", NextNodeOnSuccess = "search_result" },
                    new() { PlayerLine = "凭什么翻我东西？（拒绝）", Action = "INTENT:RefuseSearch", NextNodeOnSuccess = "recover_confront" },
                    new() { PlayerLine = "别查了，我赔你点钱。", CheckType = DialogueInjector.TransitionCheckType.SkillCheck, Action = "INTENT:PayRestitution", ActionParam = "bribe", NextNodeOnSuccess = "alert_search_bribe_ack", NextNodeOnFail = "alert_search_bribe_fail" },
                    new() { PlayerLine = "（转身就走）", Action = "INTENT:WalkAway", NextNodeOnSuccess = "alert_search_walk_ack" },
                }
            });

            nodes.Add(Node("alert_search_bribe_ack", r.Resolve("……做贼心虚。拿了钱滚。")));
            nodes.Add(Node("alert_search_bribe_fail", r.Resolve("少来这套。把包打开，{SPEAKER_SELF}自己看！"), "continue_chat"));
            nodes.Add(Node("alert_search_walk_ack", r.Resolve("站住！")));
            nodes.Add(Node("alert_search_deny_ack", r.Resolve("你的？上面还写着{TARGET}的名字呢！"), "continue_chat"));

            // recover_confront（refuse search → recover mode）
            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "recover_confront",
                NpcLine = r.Resolve("不敢让人看？那就是有鬼了！（{SPEAKER_EMOTION}地）{SPEAKER_SELF}看见了！{SPEAKER_PLAYER_ADDR}偷了{StolenItemName}！交出来！"),
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    new() { PlayerLine = r.Resolve("好，还给你。（{RestitutionCost} 第纳尔）"), Action = "INTENT:PayRestitution", NextNodeOnSuccess = "alert_recover_pay_ack" },
                    new() { PlayerLine = r.Resolve("你哪只眼睛看见的？"), CheckType = DialogueInjector.TransitionCheckType.SkillCheck, Action = "INTENT:CharmDefense", NextNodeOnSuccess = "alert_recover_charm_ok", NextNodeOnFail = "alert_recover_charm_fail" },
                    new() { PlayerLine = "（推开就跑）", Action = "INTENT:WalkAway", NextNodeOnSuccess = "" },
                }
            });

            AddRecoverAckNodes(nodes, r);

            // search_result（submit search → 判定赃物）
            bool hasStolen = PlayerHasStolenItems();
            nodes.Add(BuildSearchResultNode(r, hasStolen));
        }

        /// <summary>Recover 质问子树：人赃并获，交出赃物。</summary>
        static void BuildRecoverSubtree(
            List<DialogueInjector.DialogueNode> nodes,
            PlaceholderResolver r,
            string npcOpening)
        {
            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "injectedStart",
                NpcLine = npcOpening,
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    new() { PlayerLine = r.Resolve("好，还给你。（{RestitutionCost} 第纳尔）"), Action = "INTENT:PayRestitution", NextNodeOnSuccess = "alert_recover_pay_ack" },
                    new() { PlayerLine = r.Resolve("你哪只眼睛看见的？"), CheckType = DialogueInjector.TransitionCheckType.SkillCheck, Action = "INTENT:CharmDefense", NextNodeOnSuccess = "alert_recover_charm_ok", NextNodeOnFail = "alert_recover_charm_fail" },
                    new() { PlayerLine = "（推开就跑）", Action = "INTENT:WalkAway", NextNodeOnSuccess = "" },
                }
            });

            AddRecoverAckNodes(nodes, r);
        }

        /// <summary>Stop 质问子树：当场制止暴力行为。</summary>
        static void BuildStopSubtree(
            List<DialogueInjector.DialogueNode> nodes,
            PlaceholderResolver r,
            string npcOpening)
        {
            nodes.Add(new DialogueInjector.DialogueNode
            {
                Id = "injectedStart",
                NpcLine = npcOpening,
                Transitions = new List<DialogueInjector.DialogueTransition>
                {
                    new() { PlayerLine = r.Resolve("我愿意赔钱。（{RestitutionCost} 第纳尔）"), Action = "INTENT:PayRestitution", NextNodeOnSuccess = "alert_stop_pay_ack" },
                    new() { PlayerLine = "他先惹我的。", CheckType = DialogueInjector.TransitionCheckType.SkillCheck, Action = "INTENT:CharmDefense", NextNodeOnSuccess = "alert_stop_charm_ok", NextNodeOnFail = "alert_stop_charm_fail" },
                    new() { PlayerLine = "（拔剑）谁敢拦我！", Action = "INTENT:FightVillagers", NextNodeOnSuccess = "alert_stop_fight_ack" },
                }
            });

            nodes.Add(Node("alert_stop_pay_ack", r.Resolve("光赔钱就完了？拿了钱快滚。")));
            nodes.Add(Node("alert_stop_charm_ok", r.Resolve("……下次再动手没这么好说话。"), "continue_chat"));
            nodes.Add(Node("alert_stop_charm_fail", r.Resolve("在{SPEAKER_SELF}眼皮底下动手，就得有个说法！"), "continue_chat"));
            nodes.Add(Node("alert_stop_fight_ack", r.Resolve("{SPEAKER_PLAYER_ADDR}疯了！快叫人！")));
        }

        /// <summary>Recover ack nodes：被 BuildRecoverSubtree 和 BuildSearchSubtree（via recover_confront）共享。</summary>
        static void AddRecoverAckNodes(List<DialogueInjector.DialogueNode> nodes, PlaceholderResolver r)
        {
            nodes.Add(Node("alert_recover_pay_ack", r.Resolve("算你识相。别再来了。")));
            nodes.Add(Node("alert_recover_charm_ok", r.Resolve("……{SPEAKER_SELF}可能看错了。"), "continue_chat"));
            nodes.Add(Node("alert_recover_charm_fail", r.Resolve("{SPEAKER_SELF}两只眼睛都看见了！"), "continue_chat"));
        }

        /// <summary>搜查结果 node：接受搜查后，系统查 TheftLedger 判定玩家背包是否有赃物。依赖调用方已添加 alert_search_deny_ack。</summary>
        static DialogueInjector.DialogueNode BuildSearchResultNode(PlaceholderResolver r, bool hasStolenItems)
        {
            return new DialogueInjector.DialogueNode
            {
                Id = "search_result",
                NpcLine = hasStolenItems
                    ? r.Resolve("（{SPEAKER_EMOTION}地）这是什么？！还说没偷！")
                    : r.Resolve("（{SPEAKER_EMOTION}地）……行吧。是{SPEAKER_SELF}多心了。"),
                Transitions = hasStolenItems
                    ? new List<DialogueInjector.DialogueTransition>
                    {
                        new() { PlayerLine = "……（无言以对）", Action = "INTENT:Confess", NextNodeOnSuccess = "continue_chat" },
                        new() { PlayerLine = "那是我的东西！", Action = "NONE", NextNodeOnSuccess = "alert_search_deny_ack" },
                    }
                    : new List<DialogueInjector.DialogueTransition>
                    {
                        new() { PlayerLine = "我说了没拿吧。", Action = "NONE", NextNodeOnSuccess = "" },
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
                        NpcLine = $"（喘着粗气，收起武器）哼，知道打不过了吧？把钱袋交出来——{baseRansom}第纳尔，饶你一命。",
                        Transitions = new List<DialogueInjector.DialogueTransition>
                        {
                            new DialogueInjector.DialogueTransition
                            {
                                PlayerLine = $"……（交出 {baseRansom} 第纳尔）",
                                Action = "INTENT:PlayerSurrenderPay",
                                ActionParam = "pay",
                                NextNodeOnSuccess = "surrender_pay_ack"
                            },
                            new DialogueInjector.DialogueTransition
                            {
                                PlayerLine = "求你放过我，我只是路过……",
                                CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                                Action = "INTENT:PlayerSurrenderBeg",
                                ActionParam = "beg",
                                NextNodeOnSuccess = "surrender_beg_ok",
                                NextNodeOnFail = "player_lose_counteroffer"
                            },
                            new DialogueInjector.DialogueTransition
                            {
                                PlayerLine = "你这条狗！杀了我你也别想好过！",
                                CheckType = DialogueInjector.TransitionCheckType.SkillCheck,
                                Action = "INTENT:PlayerSurrenderThreaten",
                                ActionParam = "threaten",
                                NextNodeOnSuccess = "surrender_threaten_ok",
                                NextNodeOnFail = "surrender_threaten_fail"
                            }
                        }
                    },
                    // Ack nodes for player_lose
                    new DialogueInjector.DialogueNode { Id = "surrender_pay_ack", NpcLine = $"算你识相。{baseRansom}第纳尔，下次长点眼力见，滚吧！", Transitions = new List<DialogueInjector.DialogueTransition>() },
                    new DialogueInjector.DialogueNode { Id = "surrender_beg_ok", NpcLine = "……啧，算你运气好。滚，别让我再看见你。", Transitions = new List<DialogueInjector.DialogueTransition>() },
                    new DialogueInjector.DialogueNode { Id = "surrender_threaten_ok", NpcLine = "……疯子。滚，别让我再看见你。", Transitions = new List<DialogueInjector.DialogueTransition>() },
                    new DialogueInjector.DialogueNode { Id = "surrender_threaten_fail", NpcLine = "找死！！（暴怒地扑了上来）", Transitions = new List<DialogueInjector.DialogueTransition>() },
                    new DialogueInjector.DialogueNode
                    {
                        Id = "player_lose_counteroffer",
                        NpcLine = $"（冷笑）最后一次机会——{counterRansom} 第纳尔，或者咱们接着打。你选。",
                        Transitions = new List<DialogueInjector.DialogueTransition>
                        {
                            new DialogueInjector.DialogueTransition
                            {
                                PlayerLine = $"……（交出 {counterRansom} 第纳尔）",
                                Action = "INTENT:PlayerSurrenderPay",
                                ActionParam = "counteroffer_beg",
                                NextNodeOnSuccess = "surrender_counter_ack"
                            },
                            new DialogueInjector.DialogueTransition
                            {
                                PlayerLine = "（拼死一战）",
                                Action = "NONE",
                                NextNodeOnSuccess = "surrender_fight_ack"
                            }
                        }
                    },
                    new DialogueInjector.DialogueNode { Id = "surrender_counter_ack", NpcLine = $"算你识相。{counterRansom}第纳尔，滚吧！", Transitions = new List<DialogueInjector.DialogueTransition>() },
                    new DialogueInjector.DialogueNode { Id = "surrender_fight_ack", NpcLine = "好！那就打到你爬不起来！", Transitions = new List<DialogueInjector.DialogueTransition>() },
                }
            };
        }

        /// <summary>
        /// 构建 NPC 向玩家认输的对话脚本。
        /// 调用方：CombatManager.AcceptAgentSurrender。
        /// </summary>
        /// <param name="npcName">认输 NPC 的显示名称（用于 NPC 回应文本插值）</param>
        public static DialogueInjector.DialogueInjectScript BuildNpcSurrenderScript(string npcName)
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
                        NpcLine = "（丢下武器，踉跄后退，举起双手）别、别打了……我认输！",
                        Transitions = new List<DialogueInjector.DialogueTransition>
                        {
                            new DialogueInjector.DialogueTransition
                            {
                                PlayerLine = "你走吧。",
                                Action = "INTENT:ResolveNpcSurrender",
                                ActionParam = "accept",
                                NextNodeOnSuccess = "npc_surrender_accept_ack"
                            },
                            new DialogueInjector.DialogueTransition
                            {
                                PlayerLine = "给我跪下磕头认错！",
                                Action = "INTENT:ResolveNpcSurrender",
                                ActionParam = "humiliate",
                                NextNodeOnSuccess = "npc_surrender_humiliate_ack"
                            },
                            new DialogueInjector.DialogueTransition
                            {
                                PlayerLine = "把钱交出来，饶你一命。",
                                Action = "INTENT:ResolveNpcSurrender",
                                ActionParam = "ransom",
                                NextNodeOnSuccess = "npc_surrender_ransom_ack"
                            },
                            new DialogueInjector.DialogueTransition
                            {
                                PlayerLine = "太迟了。继续打！",
                                Action = "INTENT:ResolveNpcSurrender",
                                ActionParam = "refuse",
                                NextNodeOnSuccess = "npc_surrender_refuse_ack"
                            }
                        }
                    },
                    new DialogueInjector.DialogueNode { Id = "npc_surrender_accept_ack", NpcLine = "多、多谢！我这就走……", Transitions = new List<DialogueInjector.DialogueTransition>() },
                    new DialogueInjector.DialogueNode { Id = "npc_surrender_humiliate_ack", NpcLine = $"（{npcName}屈辱地跪倒在地，额头重重磕在地上……）", Transitions = new List<DialogueInjector.DialogueTransition>() },
                    new DialogueInjector.DialogueNode { Id = "npc_surrender_ransom_ack", NpcLine = "好、好……都给你！求你放过我……", Transitions = new List<DialogueInjector.DialogueTransition>() },
                    new DialogueInjector.DialogueNode { Id = "npc_surrender_refuse_ack", NpcLine = $"不——！（{npcName}绝望地重新抓起武器）", Transitions = new List<DialogueInjector.DialogueTransition>() },
                }
            };
        }
    }
}
