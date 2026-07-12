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
        /// <summary>
        /// 玩家对 NPC 点"交谈"时调用。
        /// 返回 null = 该 NPC 不需要注入犯罪对话。
        /// </summary>
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

            // 日志：打印每个 turn 的最终填充文本，方便排查占位符遗漏
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
            List<DialogueInjector.DialogueInjectTurn> turns = new List<DialogueInjector.DialogueInjectTurn>();
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
                        BuildReportTurn(turns, r, ctx);
                    }
                    else
                    {
                        //玩家没有接调查任务,请求玩家调查
                        entryOption = r.Resolve("{SpeakerRole}，听说{TargetSettlementName}出了点事？", "EntryOption");
                        BuildDiscoveryTurn(turns, r, ctx);
                    }
                    break;
                case EventStage.Active:
                    //案件已经知道是谁干的了（怀疑）
                    if (evt.SuspectIsPlayer)
                    {
                        //怀疑是玩家干的
                        entryOption = r.Resolve("{SpeakerRole}，{SpeakerSelfRef}有话跟你说。", "EntryOption");
                        BuildConfrontPlayerTurn(turns, r, ctx);
                    }
                    else
                    {
                        //是别的人干的，请求玩家去帮忙
                        entryOption = r.Resolve("{SpeakerRole}，关于那桩悬赏……", "EntryOption");
                        BuildBountyOfferTurn(turns, r, ctx);
                    }
                    break;
                case EventStage.Confrontation:
                    //是玩家干的，和玩家对峙
                    {
                        entryOption = r.Resolve("{SpeakerRole}……", "EntryOption");
                        BuildRetaliationTurn(turns, r, ctx);
                    }
                    break;
                case EventStage.Resolved:
                case EventStage.Unsolved:
                    //解决了，或者没解决，都没有对话
                    break;

            }           

            return new DialogueInjector.DialogueInjectScript
            {
                EntryOption = entryOption,
                EntryTurn = "start",
                Turns = turns
            };
        }

        private static void BuildDiscoveryTurn(List<DialogueInjector.DialogueInjectTurn> turns, PlaceholderResolver r, IntentContext ctx)
        {
            WorldEvent evt = r.Event;
            DialogueInjector.DialogueInjectTurn turn = new DialogueInjector.DialogueInjectTurn
            {
                Id = "start",
                SpeakerIndex = 0,
                NpcLine = r.Resolve("（{SpeakerEmotion}地）{TimeWord}{TargetSettlementName}的{CrimeScene}{CrimeVerbPast}{StolenItemClause}。{InvestigationProgressWord}。{WitnessCountWord}，{SuspectDescription}。{SpeakerPlayerAddr}能帮忙查查吗？", "NpcLine"),
                Options = new List<DialogueInjector.DialogueInjectOption>
                {
                    new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = "我可以帮忙查查是谁干的。",
                        NpcResponse = r.Resolve("拜托了！查出来了{SpeakerSelfRef}必有重谢。"),
                        Action = "INTENT:Investigate",
                        NextTurn = "continue_chat"
                    },
                    new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = "我还有事。",
                        NpcResponse = r.Resolve("那{SpeakerPlayerAddr}忙吧……{SpeakerSelfRef}们自己想办法。"),
                        Action = "INTENT:WalkAway",
                        NextTurn = ""
                    }
                }
            };

            // 如果玩家是贼 → 加"主动认栽"选项
            if (evt.InitiatorIsPlayer)
            {
                turn.Options.Insert(0, new DialogueInjector.DialogueInjectOption
                {
                    PlayerLine = "是我干的。",
                    NpcResponse = r.Resolve("{SpeakerPlayerAddr}？！……好，既然自己认了，咱们可以商量。"),
                    Action = "INTENT:Confess",
                    NextTurn = "confess"
                });
                turns.Add(BuildConfessTurn(r, ctx));
                turns.Add(BuildRestitutionDetailTurn(r, ctx, "restitution_detail", "continue_chat"));
            }

            turns.Add(turn);
            turns.Add(BuildContinueChatTurn(r));
        }

        private static DialogueInjector.DialogueInjectTurn BuildConfessTurn(PlaceholderResolver r, IntentContext ctx)
        {
            return new DialogueInjector.DialogueInjectTurn
            {
                Id = "confess",
                SpeakerIndex = 0,
                NpcLine = r.Resolve("有什么要说的？"),
                Options = new List<DialogueInjector.DialogueInjectOption>
                {
                    new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = r.Resolve("我愿意赔。你说个数。"),
                        Action = "NONE",
                        NextTurn = "restitution_detail"
                    },
                    new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = "（讪笑）开个玩笑……刚才是我胡说的",
                        NpcResponseOnSuccess = r.Resolve("……说清楚？好，{SpeakerSelfRef}倒要听听。"),
                        NpcResponseOnFail = r.Resolve("说清楚？证据确凿，没什么好说的。"),
                        Action = "INTENT:CharmDefense",
                        NextTurn = "continue_chat"
                    },
                    new DialogueInjector.DialogueInjectOption { PlayerLine = "（转身就走）", Action = "INTENT:WalkAway", NextTurn = "" },
                }
            };
        }

        /// <summary>赔偿明细 turn：NPC 解释赔偿金额怎么算的 → 玩家选接受/不接受</summary>
        private static DialogueInjector.DialogueInjectTurn BuildRestitutionDetailTurn(PlaceholderResolver r, IntentContext ctx, string turnId, string declineTurn)
        {
            return new DialogueInjector.DialogueInjectTurn
            {
                Id = turnId,
                SpeakerIndex = 0,
                NpcLine = r.Resolve("{RestitutionBreakdown}", "NpcLine"),
                Options = new List<DialogueInjector.DialogueInjectOption>
                {
                    new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = r.Resolve("好，我赔（{RestitutionCost} 第纳尔）"),
                        NpcResponse = r.Resolve("好，钱留下，这事就算了。"),
                        Action = "INTENT:PayRestitution",
                        NextTurn = "continue_chat"
                    },
                    new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = "太贵了，不赔。",
                        Action = "NONE",
                        NextTurn = declineTurn
                    },
                }
            };
        }

        private static void BuildReportTurn(List<DialogueInjector.DialogueInjectTurn> turns, PlaceholderResolver r, IntentContext ctx)
        {
            
            WorldEvent evt = r.Event;
            DialogueInjector.DialogueInjectTurn turn = new DialogueInjector.DialogueInjectTurn
            {
                Id = "start",
                SpeakerIndex = 0,
                NpcLine = "怎么样，查到什么了吗？",
                Options = new List<DialogueInjector.DialogueInjectOption>
                {
                    new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = "是附近藏身处的强盗干的！",
                        NpcResponseOnSuccess = r.Resolve("藏身处的强盗？好，那就是他们了！{SpeakerSelfRef}这就张罗悬赏。"),
                        NpcResponseOnFail = r.Resolve("强盗？光凭{SpeakerPlayerAddr}一句话可不行……再去查查。"),
                        Action = "INTENT:FrameSuspect",
                        ActionParam = "bandit",
                        NextTurn = "continue_chat"
                    },
                    new DialogueInjector.DialogueInjectOption { PlayerLine = "还没查到什么。", NpcResponse = r.Resolve("那你再去看看。{InvestigationProgressWord}。"), Action = "NONE", NextTurn = "continue_chat" },
                    new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = "我还有事。",
                        NpcResponse = r.Resolve("快去查，有消息了来告诉{SpeakerSelfRef}。"),
                        Action = "INTENT:WalkAway",
                        NextTurn = ""
                    },
                }
            };

            // 动态生成栽赃候选
            List<FrameSubOption> frameTargets = TheftLedger.GetFrameableTargets();
            foreach (FrameSubOption target in frameTargets.Skip(1)) // Skip "bandit" (already above)
            {
                if (target.CanShowEvidence)
                {
                    // 有证物 → 展开每一件赃物为独立选项
                    List<EvidenceItem> evidenceItems = TheftLedger.GetEvidenceItems(target.TargetId);
                    foreach (EvidenceItem evItem in evidenceItems)
                    {
                        turn.Options.Insert(turn.Options.Count - 1, new DialogueInjector.DialogueInjectOption
                        {
                            PlayerLine = $"是 {target.DisplayName} 干的——[出示{evItem.ItemName}]",
                            NpcResponseOnSuccess = $"（仔细看了看{evItem.ItemName}）……这确实是他的东西。好，那就是他了！",
                            NpcResponseOnFail = $"（仔细看了看{evItem.ItemName}）……这东西说明不了什么。{r.Resolve("{SpeakerPlayerAddr}")}再去查查。",
                            Action = "INTENT:FrameSuspect",
                            ActionParam = target.TargetId,
                            NextTurn = "continue_chat"
                        });
                    }
                }
                else
                {
                    // 无证物 → 裸指控
                    turn.Options.Insert(turn.Options.Count - 1, new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = $"是 {target.DisplayName} 干的。",
                        NpcResponseOnSuccess = $"是{target.DisplayName}干的？……好，{r.Resolve("{SpeakerSelfRef}")}信你。",
                        NpcResponseOnFail = $"就凭一句话？{r.Resolve("{SpeakerPlayerAddr}")}再去查查。",
                        Action = "INTENT:FrameSuspect",
                        ActionParam = target.TargetId,
                        NextTurn = "continue_chat"
                    });
                }
            }

            // 如果玩家是贼 → 加"主动认栽"
            if (evt.InitiatorIsPlayer)
            {
                turn.Options.Add(new DialogueInjector.DialogueInjectOption
                {
                    PlayerLine = "（低头）……是我干的。",
                    NpcResponse = r.Resolve("{SpeakerPlayerAddr}？！……好，既然自己认了，咱们可以商量。"),
                    Action = "INTENT:Confess",
                    NextTurn = "confess"
                });
                turns.Add(BuildConfessTurn(r, ctx));
                turns.Add(BuildRestitutionDetailTurn(r, ctx, "restitution_detail", "continue_chat"));
            }

            turns.Add(turn);
            turns.Add(BuildContinueChatTurn(r));
        }

        private static void BuildConfrontPlayerTurn(List<DialogueInjector.DialogueInjectTurn> turns, PlaceholderResolver r, IntentContext ctx)
        {
            WorldEvent evt = r.Event;
            // 大地图对话无法叫守卫/触发战斗 → 威胁失败的 NPC 回应降级为口头警告
            string threatFailLine = ctx.IsInMission
                ? r.Resolve("威胁{SpeakerSelfRef}？来人！")
                : r.Resolve("威胁{SpeakerSelfRef}？{SpeakerPlayerAddr}等着，{SpeakerSelfRef}会告到上面去。");
            DialogueInjector.DialogueInjectTurn turn = new DialogueInjector.DialogueInjectTurn
            {
                Id = "start",
                SpeakerIndex = 0,
                NpcLine = r.Resolve("（{SpeakerEmotion}地）{SpeakerPlayerAddr}还敢来？{PrimaryWitnessDesc}{TimeWord}就来找{SpeakerSelfRef}，说亲眼瞧见是{SpeakerPlayerAddr}{CrimeVerb}。有什么要说的？", "NpcLine"),
                Options = new List<DialogueInjector.DialogueInjectOption>
                {
                    new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = "你们搞错了。给我个机会说清楚。",
                        NpcResponseOnSuccess = r.Resolve("……{SpeakerPlayerAddr}说的也有道理。那{SpeakerSelfRef}再查查。"),
                        NpcResponseOnFail = r.Resolve("说清楚？证据确凿，没什么好说的。"),
                        Action = "INTENT:CharmDefense",
                        NextTurn = "continue_chat"
                    },
                    new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = "赔偿的事……你要多少？",
                        Action = "NONE",
                        NextTurn = "restitution_detail"
                    },
                    new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = "你再说一遍？（手按在剑柄上）",
                        NpcResponseOnSuccess = r.Resolve("……{SpeakerSelfRef}不说了。{SpeakerPlayerAddr}走吧。"),
                        NpcResponseOnFail = threatFailLine,
                        Action = "INTENT:Threat",
                        NextTurn = "continue_chat"
                    },
                    new DialogueInjector.DialogueInjectOption { PlayerLine = "（转身就走）", Action = "INTENT:WalkAway", NextTurn = "" },
                }
            };
            turns.Add(turn);

            // 赔偿明细 turn + 继续聊 turn
            turns.Add(BuildRestitutionDetailTurn(r, ctx, "restitution_detail", "continue_chat"));
            turns.Add(BuildContinueChatTurn(r));
        }

        private static void BuildBountyOfferTurn(List<DialogueInjector.DialogueInjectTurn> turns, PlaceholderResolver r, IntentContext ctx)
        {
            turns.Add(new DialogueInjector.DialogueInjectTurn
            {
                Id = "start",
                SpeakerIndex = 0,
                NpcLine = r.Resolve("还记得{TimeWord}{CrimeVerbPast}的事吗？查清楚了——是{SuspectDescription}干的。村上凑了{BountyAmount}第纳尔悬赏，谁把他抓回来就给谁。{SpeakerPlayerAddr}接不接？", "NpcLine"),
                Options = new List<DialogueInjector.DialogueInjectOption>
                {
                    new DialogueInjector.DialogueInjectOption { PlayerLine = "我接这个悬赏！", NpcResponse = r.Resolve("好！人就交给{SpeakerPlayerAddr}了。"), Action = "INTENT:AcceptBountyQuest", NextTurn = "continue_chat" },
                    new DialogueInjector.DialogueInjectOption { PlayerLine = "我先想想。", Action = "NONE", NextTurn = "continue_chat" },
                }
            });
            turns.Add(BuildContinueChatTurn(r));
        }

        private static void BuildRetaliationTurn(List<DialogueInjector.DialogueInjectTurn> turns, PlaceholderResolver r, IntentContext ctx)
        {
            WorldEvent evt = r.Event;
            string npcLine;
            List<DialogueInjector.DialogueInjectOption> options = new List<DialogueInjector.DialogueInjectOption>();

            if (evt.SuspectIsPlayer)
            {
                npcLine = r.Resolve("（{SpeakerEmotion}地）客客气气说话不管用，那就只能动手了。村里凑了钱，已经雇了人。{SpeakerPlayerAddr}躲得过初一躲不过十五。", "NpcLine");
                options.Add(new DialogueInjector.DialogueInjectOption { PlayerLine = r.Resolve("我赔钱！你说个数。"), Action = "NONE", NextTurn = "restitution_detail" });
                options.Add(new DialogueInjector.DialogueInjectOption { PlayerLine = "这事可以商量……", NpcResponse = r.Resolve("商量？有什么好商量的？"), Action = "INTENT:Settle", NextTurn = "continue_chat" });
                options.Add(new DialogueInjector.DialogueInjectOption { PlayerLine = "我走了。", Action = "INTENT:WalkAway", NextTurn = "" });
            }
            else
            {
                npcLine = r.Resolve("（{SpeakerEmotion}地）客客气气说话不管用，那就只能动手了。我们已经雇了人去抓{SuspectDescription}。{SpeakerPlayerAddr}要是站在我们这边的，可以带他们去。", "NpcLine");
                options.Add(new DialogueInjector.DialogueInjectOption { PlayerLine = "我带人去！", NpcResponse = r.Resolve("好！有{SpeakerPlayerAddr}带队，那{SuspectDescription}跑不了。"), Action = "INTENT:LeadRetaliation", NextTurn = "close_window" });
                options.Add(new DialogueInjector.DialogueInjectOption { PlayerLine = "我没空。", Action = "NONE", NextTurn = "close_window" });
            }

            turns.Add(new DialogueInjector.DialogueInjectTurn
            {
                Id = "start",
                SpeakerIndex = 0,
                NpcLine = npcLine,
                Options = options
            });

            // 赔钱明细 turn：报复阶段也需要解释金额构成（与 confess/confront 一致）
            if (evt.SuspectIsPlayer)
            {
                turns.Add(BuildRestitutionDetailTurn(r, ctx, "restitution_detail", "continue_chat"));
            }
            turns.Add(BuildContinueChatTurn(r));
        }

        private static DialogueInjector.DialogueInjectScript BuildWitnessScript(
            PlaceholderResolver r, IntentContext ctx)
        {
            List<DialogueInjector.DialogueInjectTurn> turns = new List<DialogueInjector.DialogueInjectTurn>();
            WorldEvent evt = ctx.ActiveEvent;
            Hero speaker = ctx.Speaker;

            // 从 WitnessTestimonies 匹配当前 NPC 的证词
            WitnessTestimony testimony = evt.WitnessTestimonies?
                .FirstOrDefault(t => t.WitnessHeroId == speaker.StringId);
            r.SpeakingWitness = testimony;

            string witnessedDesc = BuildWitnessedActionDescription(testimony);

            DialogueInjector.DialogueInjectTurn turn = new DialogueInjector.DialogueInjectTurn
            {
                Id = "start",
                SpeakerIndex = 0,
                NpcLine = evt.InitiatorIsPlayer
                    ? r.Resolve($"（{{SpeakerEmotion}}地）{{SpeakerPlayerAddr}}是来问{{CrimeScene}}的事？{{SpeakerSelfRef}}看见了——{witnessedDesc}。")
                    : r.Resolve($"（{{SpeakerEmotion}}地）{{SpeakerSelfRef}}{{TimeWord}}在{{CrimeScene}}附近看见了——{witnessedDesc}"),
                Options = new List<DialogueInjector.DialogueInjectOption>()
            };

            if (evt.InitiatorIsPlayer && !evt.WitnessesSilenced)
            {
                turn.Options.Add(new DialogueInjector.DialogueInjectOption { PlayerLine = "（给些钱）这事你别往外说……", NpcResponse = r.Resolve("……好吧，{SpeakerSelfRef}什么也没看见。"), Action = "INTENT:SilenceWitness", NextTurn = "continue_chat" });
                turn.Options.Add(new DialogueInjector.DialogueInjectOption { PlayerLine = "（威胁）你什么也没看见，明白吗？", NpcResponse = r.Resolve("明白、明白……{SpeakerSelfRef}一个字也不说。"), Action = "INTENT:SilenceWitness", NextTurn = "continue_chat" });
                turn.Options.Add(new DialogueInjector.DialogueInjectOption { PlayerLine = "当我没来过。", Action = "INTENT:WalkAway", NextTurn = "" });
            }
            else if (evt.WitnessesSilenced)
            {
                turn.NpcLine = r.Resolve("（紧张地看了看四周）{SpeakerPlayerAddr}找错人了。{SpeakerSelfRef}什么也不知道。", "NpcLine");
                turn.Options.Add(new DialogueInjector.DialogueInjectOption { PlayerLine = "……好吧。", Action = "NONE", NextTurn = "continue_chat" });
            }
            else
            {
                turn.Options.Add(new DialogueInjector.DialogueInjectOption { PlayerLine = "能说说那人的特征吗？", NpcResponse = r.Resolve("那人……{SuspectDescription}。"), Action = "NONE", NextTurn = "continue_chat" });
                turn.Options.Add(new DialogueInjector.DialogueInjectOption { PlayerLine = "谢谢，我知道了。", Action = "NONE", NextTurn = "continue_chat" });
            }

            turns.Add(turn);
            turns.Add(BuildContinueChatTurn(r));
            return new DialogueInjector.DialogueInjectScript { EntryOption = "听说你看到了……？", EntryTurn = "start", Turns = turns };
        }

        private static DialogueInjector.DialogueInjectScript BuildSuspectScript(
            PlaceholderResolver r, IntentContext ctx)
        {
            List<DialogueInjector.DialogueInjectTurn> turns = new List<DialogueInjector.DialogueInjectTurn>
            {
                new DialogueInjector.DialogueInjectTurn
                {
                    Id = "start",
                    SpeakerIndex = 0,
                    NpcLine = r.Resolve("（警惕地）{SpeakerPlayerAddr}盯着{SpeakerSelfRef}看什么？", "NpcLine"),
                    Options = new List<DialogueInjector.DialogueInjectOption>
                    {
                        new DialogueInjector.DialogueInjectOption { PlayerLine = "跟我走一趟，村长找你有事。", NpcResponse = r.Resolve("什么？！{SpeakerSelfRef}什么也没干……"), Action = "INTENT:LureArrest", NextTurn = "continue_chat" },
                        new DialogueInjector.DialogueInjectOption { PlayerLine = "快跑！村里人在抓你。", NpcResponse = r.Resolve("什么？！……谢了！"), Action = "INTENT:BetrayQuest", NextTurn = "continue_chat" },
                        new DialogueInjector.DialogueInjectOption { PlayerLine = "没什么。", Action = "INTENT:WalkAway", NextTurn = "" },
                    }
                }
            };
            turns.Add(BuildContinueChatTurn(r));
            return new DialogueInjector.DialogueInjectScript { EntryOption = "（打量了一下）……", EntryTurn = "start", Turns = turns };
        }

        private static DialogueInjector.DialogueInjectScript BuildBystanderScript(
            PlaceholderResolver r, IntentContext ctx)
        {
            List<DialogueInjector.DialogueInjectTurn> turns = new List<DialogueInjector.DialogueInjectTurn>();
            WorldEvent evt = ctx.ActiveEvent;

            string npcLine = evt.Stage switch
            {
                EventStage.Emerging => r.Resolve("（压低声音）{SpeakerPlayerAddr}听说了吗？{TargetSettlementName}的{CrimeScene}{CrimeVerbPast}！谁干的还不知道。", "NpcLine"),
                EventStage.Active => r.Resolve("听说了吗？是{SuspectDescription}干的！村里悬赏{BountyAmount}第纳尔抓他呢。", "NpcLine"),
                EventStage.Confrontation => r.Resolve("（紧张地）{TargetSettlementName}的人真动手了——雇了打手满世界找人。这事闹大了……", "NpcLine"),
                _ => r.Resolve("这事好像已经过去了……", "NpcLine"),
            };

            turns.Add(new DialogueInjector.DialogueInjectTurn
            {
                Id = "start",
                SpeakerIndex = 0,
                NpcLine = npcLine,
                Options = new List<DialogueInjector.DialogueInjectOption>
                {
                    new DialogueInjector.DialogueInjectOption { PlayerLine = "详细说说？", NpcResponse = r.Resolve("我就知道这么多……"), Action = "NONE", NextTurn = "continue_chat" },
                    new DialogueInjector.DialogueInjectOption { PlayerLine = "哦。", Action = "NONE", NextTurn = "continue_chat" },
                }
            });
            turns.Add(BuildContinueChatTurn(r));

            return new DialogueInjector.DialogueInjectScript { EntryOption = "最近村里有什么新鲜事？", EntryTurn = "start", Turns = turns };
        }

        /// <summary>继续聊 turn：NPC 说完事后 → 玩家走人。告别语按阶段动态切换，引擎展示前才求值。</summary>
        private static DialogueInjector.DialogueInjectTurn BuildContinueChatTurn(PlaceholderResolver r)
        {
            WorldEvent evt = r.Event;
            return new DialogueInjector.DialogueInjectTurn
            {
                Id = "continue_chat",
                SpeakerIndex = 0,
                NpcLine = "还有什么别的想说的吗?",
                Options = new List<DialogueInjector.DialogueInjectOption>
                {
                    new DialogueInjector.DialogueInjectOption
                    {
                        PlayerLine = "我得走了。",
                        LazyNpcResponse = () =>
                        {
                            if (evt.PlayerTookInvestigationQuest)
                                return r.Resolve("快去查，有消息了来告诉{SpeakerSelfRef}。");
                            if (evt.SuspectIsPlayer && evt.Stage == EventStage.Active)
                                return r.Resolve("（冷冷地）这事不算完。");
                            if (evt.Stage == EventStage.Confrontation)
                                return r.Resolve("这事没完。");
                            return r.Resolve("嗯，{SpeakerPlayerAddr}去吧。");
                        },
                        Action = "INTENT:WalkAway",
                        NextTurn = ""
                    },
                }
            };
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
        /// 台词查找顺序：① NpcSpeech.csv → ② NarrativeResolver（过渡）→ ③ PlaceholderResolver 硬编码兜底。
        /// </summary>
        public static DialogueInjector.DialogueInjectScript BuildAlertInterceptScript(
            Hero speaker, ConfrontationType npcIntent, PlayerActionType primaryAction,
            WorldEvent worldEvt = null, Agent speakerAgent = null)
        {
            PlaceholderResolver r = new PlaceholderResolver(speaker, Hero.MainHero);

            // 🔑 从 PendingWorldEvent 取刚写入的证词（RegisterWitness 已在 CheckPhaseTransition 前一步执行）
            WorldEvent pending = AgentAIController.Instance?.PendingWorldEvent;
            r.SpeakingWitness = pending?.WitnessTestimonies?
                .FirstOrDefault(t => t.WitnessHeroId == speaker.StringId);

            IntentContext ctx = new IntentContext(speakerAgent, speaker: speaker, worldEvent: worldEvt);
            List<DialogueInjector.DialogueInjectTurn> turns = new List<DialogueInjector.DialogueInjectTurn>();

            // ① 优先查 NpcSpeech.csv
            string csvTemplateId = $"L3_{npcIntent}_{primaryAction}";
            string npcOpening = NpcSpeechResolver.Resolve(csvTemplateId, speaker, Hero.MainHero);

            // ② CSV 未命中 → 回落 NarrativeResolver（过渡）
            if (string.IsNullOrEmpty(npcOpening))
            {
                NarrativeResult narrResult = NarrativeResolver.Resolve(new NarrativeFilters
                {
                    EventName = "L3AlertIntercept",
                    GoalType = npcIntent.ToString(),
                    Outcome = primaryAction.ToString(),
                });
                if (narrResult != null && !NarrativeResolver.IsFallbackText(narrResult.Text))
                    npcOpening = narrResult.Text;
            }

            // ③ 最终兜底：PlaceholderResolver 直接解析硬编码模板
            if (string.IsNullOrEmpty(npcOpening))
            {
                npcOpening = npcIntent switch
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

            turns.Add(new DialogueInjector.DialogueInjectTurn
            {
                Id = "start", SpeakerIndex = 0, NpcLine = npcOpening,
                Options = BuildOptionsByIntent(r, npcIntent, primaryAction, worldEvt)
            });

            // Search 成功后如果搜到赃物 → 插入一个额外 turn 把意图切换为 Recover
            if (npcIntent == ConfrontationType.Search)
            {
                bool hasStolen = PlayerHasStolenItems();
                turns.Add(BuildSearchResultTurn(r, hasStolen));
            }

            // continue_chat — 阶段 >= Active 时无退路
            bool escalated = worldEvt != null && worldEvt.Stage >= EventStage.Active;
            turns.Add(new DialogueInjector.DialogueInjectTurn
            {
                Id = "continue_chat", SpeakerIndex = 0,
                NpcLine = escalated ? "最后一次警告——别逼我叫人！" : "还有什么想说的？",
                Options = escalated
                    ? new List<DialogueInjector.DialogueInjectOption>
                    {
                        new() { PlayerLine = "（拔剑）谁敢拦我！", NpcResponse = r.Resolve("{SPEAKER_PLAYER_ADDR}疯了！快叫人！"), Action = "INTENT:FightVillagers", NextTurn = "" },
                        new() { PlayerLine = "我认罚。（100 第纳尔）", NpcResponse = r.Resolve("扰乱治安，罚款100第纳尔。算你识相。别再来了。"), Action = "INTENT:PayRestitution", ActionParam = "alert_fine", NextTurn = "" },
                        new() { PlayerLine = "我没钱。要抓就抓吧。", NpcResponse = r.Resolve("没钱还敢闹事？！来人，把他关进地牢！"), Action = "INTENT:SurrenderJail", ActionParam = "surrender_jail", NextTurn = "" },
                    }
                    : new List<DialogueInjector.DialogueInjectOption>
                    {
                        new() { PlayerLine = "我走了。", Action = "INTENT:WalkAway", NextTurn = "" }
                    }
            });

            return new DialogueInjector.DialogueInjectScript { EntryTurn = "start", Turns = turns };
        }

        static List<DialogueInjector.DialogueInjectOption> BuildOptionsByIntent(
            PlaceholderResolver r, ConfrontationType intent, PlayerActionType action, WorldEvent worldEvt = null)
        {
            List<DialogueInjector.DialogueInjectOption> opts = new List<DialogueInjector.DialogueInjectOption>();

            switch (intent)
            {
                case ConfrontationType.Deter:
                    string complyLine = action == PlayerActionType.WeaponDrawn
                        ? "好，我收起来。"
                        : "没什么，我这就走。";
                    string complyResp = action == PlayerActionType.WeaponDrawn
                        ? "……别再让{SPEAKER_SELF}看见你在这拔{ITEM}。"
                        : "……别再让{SPEAKER_SELF}看见你鬼鬼祟祟的。";
                    opts.Add(new() { PlayerLine = complyLine, NpcResponse = r.Resolve(complyResp), Action = "INTENT:Comply", ActionParam = "comply", NextTurn = "" });
                    opts.Add(new() { PlayerLine = "关你什么事？（挑衅）", NpcResponseOnSuccess = r.Resolve("……算了。"), NpcResponseOnFail = r.Resolve("来人！这有个闹事的！"), Action = "INTENT:Threat", NextTurn = "" });
                    // WorldEvent Stage >= Active（玩家上次走了，这次升级围堵），加入投降选项
                    if (worldEvt != null && worldEvt.Stage >= EventStage.Active)
                    {
                        opts.Add(new() { PlayerLine = "我认罚。（100 第纳尔）", NpcResponse = r.Resolve("扰乱治安，罚款100第纳尔。算你识相。别再来了。"), Action = "INTENT:PayRestitution", ActionParam = "alert_fine", NextTurn = "" });
                        opts.Add(new() { PlayerLine = "我没钱。要抓就抓吧。", NpcResponse = r.Resolve("没钱还敢闹事？！来人，把他关进地牢！"), Action = "INTENT:SurrenderJail", ActionParam = "surrender_jail", NextTurn = "" });
                    }
                    break;

                case ConfrontationType.Search:
                    opts.Add(new() { PlayerLine = "……行，你看吧。", Action = "INTENT:SubmitToSearch", NextTurn = "search_result" });
                    opts.Add(new() { PlayerLine = "凭什么翻我东西？（拒绝）", NpcResponse = r.Resolve("不敢让人看？那就是有鬼了！"), Action = "INTENT:RefuseSearch", NextTurn = "recover_confront" });
                    opts.Add(new() { PlayerLine = "别查了，我赔你点钱。", NpcResponse = r.Resolve("……做贼心虚。拿了钱滚。"), Action = "INTENT:PayRestitution", NextTurn = "" });
                    opts.Add(new() { PlayerLine = "（转身就走）", NpcResponse = r.Resolve("站住！"), Action = "INTENT:WalkAway", NextTurn = "" });
                    break;

                case ConfrontationType.Recover:
                    opts.Add(new() { PlayerLine = r.Resolve("好，还给你。（{RestitutionCost} 第纳尔）"), NpcResponse = r.Resolve("算你识相。别再来了。"), Action = "INTENT:PayRestitution", NextTurn = "" });
                    opts.Add(new() { PlayerLine = r.Resolve("你哪只眼睛看见的？"), NpcResponseOnSuccess = r.Resolve("……{SPEAKER_SELF}可能看错了。"), NpcResponseOnFail = r.Resolve("{SPEAKER_SELF}两只眼睛都看见了！"), Action = "INTENT:CharmDefense", NextTurn = "continue_chat" });
                    opts.Add(new() { PlayerLine = "（推开就跑）", Action = "INTENT:WalkAway", NextTurn = "" });
                    break;

                case ConfrontationType.Stop:
                    opts.Add(new() { PlayerLine = r.Resolve("我愿意赔钱。（{RestitutionCost} 第纳尔）"), NpcResponse = r.Resolve("光赔钱就完了？拿了钱快滚。"), Action = "INTENT:PayRestitution", NextTurn = "" });
                    opts.Add(new() { PlayerLine = "他先惹我的。", NpcResponseOnSuccess = r.Resolve("……下次再动手没这么好说话。"), NpcResponseOnFail = r.Resolve("在{SPEAKER_SELF}眼皮底下动手，就得有个说法！"), Action = "INTENT:CharmDefense", NextTurn = "continue_chat" });
                    opts.Add(new() { PlayerLine = "（拔剑）谁敢拦我！", NpcResponse = r.Resolve("{SPEAKER_PLAYER_ADDR}疯了！快叫人！"), Action = "INTENT:FightVillagers", NextTurn = "" });
                    break;
            }

            return opts;
        }

        /// <summary>搜查结果 turn：接受搜查后，系统查 TheftLedger 判定玩家背包是否有赃物。</summary>
        static DialogueInjector.DialogueInjectTurn BuildSearchResultTurn(PlaceholderResolver r, bool hasStolenItems)
        {
            return new DialogueInjector.DialogueInjectTurn
            {
                Id = "search_result",
                SpeakerIndex = 0,
                NpcLine = hasStolenItems
                    ? r.Resolve("（{SPEAKER_EMOTION}地）这是什么？！还说没偷！")
                    : r.Resolve("（{SPEAKER_EMOTION}地）……行吧。是{SPEAKER_SELF}多心了。"),
                Options = hasStolenItems
                    ? new List<DialogueInjector.DialogueInjectOption>
                    {
                        new() { PlayerLine = "……（无言以对）", Action = "INTENT:Confess", NextTurn = "continue_chat" },
                        new() { PlayerLine = "那是我的东西！", NpcResponse = r.Resolve("你的？上面还写着{TARGET}的名字呢！"), Action = "NONE", NextTurn = "continue_chat" },
                    }
                    : new List<DialogueInjector.DialogueInjectOption>
                    {
                        new() { PlayerLine = "我说了没拿吧。", Action = "NONE", NextTurn = "" },
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
        /// </summary>
        public static DialogueInjector.DialogueInjectScript BuildPlayerSurrenderScript()
        {
            return new DialogueInjector.DialogueInjectScript
            {
                EntryTurn = "player_lose",
                Turns = new List<DialogueInjector.DialogueInjectTurn>
                {
                    new DialogueInjector.DialogueInjectTurn
                    {
                        Id = "player_lose",
                        SpeakerIndex = 0,
                        NpcLine = "（喘着粗气，收起武器）哼，知道打不过了吧？把钱袋交出来，饶你一命。",
                        Options = new List<DialogueInjector.DialogueInjectOption>
                        {
                            new DialogueInjector.DialogueInjectOption
                            {
                                PlayerLine = "……（交出钱袋）",
                                NpcResponse = "算你识相。下次长点眼力见，滚吧！",
                                Action = "INTENT:PlayerSurrenderPay",
                                ActionParam = "pay"
                            },
                            new DialogueInjector.DialogueInjectOption
                            {
                                PlayerLine = "求你放过我，我只是路过……",
                                NpcResponseOnSuccess = "……啧，算你运气好。滚，别让我再看见你。",
                                NpcResponseOnFail = "废话少说！求饶？现在翻倍——400 第纳尔，一个子儿不能少！",
                                Action = "INTENT:PlayerSurrenderBeg",
                                ActionParam = "beg",
                                NextTurn = "",
                                NextTurnOnFail = "player_lose_counteroffer"
                            },
                            new DialogueInjector.DialogueInjectOption
                            {
                                PlayerLine = "你这条狗！杀了我你也别想好过！",
                                NpcResponseOnSuccess = "……疯子。滚，别让我再看见你。",
                                NpcResponseOnFail = "找死！！（暴怒地扑了上来）",
                                Action = "INTENT:PlayerSurrenderThreaten",
                                ActionParam = "threaten"
                            }
                        }
                    },
                    new DialogueInjector.DialogueInjectTurn
                    {
                        Id = "player_lose_counteroffer",
                        SpeakerIndex = 0,
                        NpcLine = "（冷笑）最后一次机会——400 第纳尔，或者咱们接着打。你选。",
                        Options = new List<DialogueInjector.DialogueInjectOption>
                        {
                            new DialogueInjector.DialogueInjectOption
                            {
                                PlayerLine = "……（交出 400 第纳尔）",
                                NpcResponse = "算你识相。滚吧！",
                                Action = "INTENT:PlayerSurrenderPay",
                                ActionParam = "counteroffer_beg"
                            },
                            new DialogueInjector.DialogueInjectOption
                            {
                                PlayerLine = "（拼死一战）",
                                NpcResponse = "好！那就打到你爬不起来！",
                                Action = "NONE",
                                NextTurn = ""
                            }
                        }
                    }
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
                EntryTurn = "npc_beg",
                Turns = new List<DialogueInjector.DialogueInjectTurn>
                {
                    new DialogueInjector.DialogueInjectTurn
                    {
                        Id = "npc_beg",
                        SpeakerIndex = 0,
                        NpcLine = "（丢下武器，踉跄后退，举起双手）别、别打了……我认输！",
                        Options = new List<DialogueInjector.DialogueInjectOption>
                        {
                            new DialogueInjector.DialogueInjectOption
                            {
                                PlayerLine = "你走吧。",
                                NpcResponse = "多、多谢！我这就走……",
                                Action = "INTENT:ResolveNpcSurrender",
                                ActionParam = "accept"
                            },
                            new DialogueInjector.DialogueInjectOption
                            {
                                PlayerLine = "给我跪下磕头认错！",
                                NpcResponse = $"（{npcName}屈辱地跪倒在地，额头重重磕在地上……）",
                                Action = "INTENT:ResolveNpcSurrender",
                                ActionParam = "humiliate"
                            },
                            new DialogueInjector.DialogueInjectOption
                            {
                                PlayerLine = "把钱交出来，饶你一命。",
                                NpcResponse = "好、好……都给你！求你放过我……",
                                Action = "INTENT:ResolveNpcSurrender",
                                ActionParam = "ransom"
                            },
                            new DialogueInjector.DialogueInjectOption
                            {
                                PlayerLine = "太迟了。继续打！",
                                NpcResponse = $"不——！（{npcName}绝望地重新抓起武器）",
                                Action = "INTENT:ResolveNpcSurrender",
                                ActionParam = "refuse"
                            }
                        }
                    }
                }
            };
        }
    }
}
