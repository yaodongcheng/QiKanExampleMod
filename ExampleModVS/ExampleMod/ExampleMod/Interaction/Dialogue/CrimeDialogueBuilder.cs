using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

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
            var settlement = speaker.CurrentSettlement;
            if (settlement == null) return null;
            var evt = WorldEventStore.FindActive(settlement.StringId);
            if (evt == null) return null;

            var r = new PlaceholderResolver(evt, speaker, listener);
            var ctx = BuildIntentContext(evt, speaker);

            // 按说话者身份分派
            DialogueInjector.DialogueInjectScript script;
            if (IsAuthority(speaker, evt))
                script = BuildAuthorityScript(evt, speaker, listener, r, ctx);
            else if (evt.WitnessHeroIds?.Contains(speaker.StringId) == true)
                script = BuildWitnessScript(evt, speaker, listener, r, ctx);
            else if (evt.SuspectHeroId == speaker.StringId)
                script = BuildSuspectScript(evt, speaker, listener, r, ctx);
            else
                script = BuildBystanderScript(evt, speaker, listener, r, ctx);

            // 日志：打印每个 turn 的最终填充文本，方便排查占位符遗漏
            if (script?.Turns != null)
            {
                foreach (var t in script.Turns)
                {
                    DebugLogger.Log($"[CrimeDialog] Turn[{t.Id}] speaker={speaker.Name} stage={evt.Stage}");
                    if (!string.IsNullOrEmpty(t.NpcLine))
                        DebugLogger.Log($"[CrimeDialog]   NPC: {t.NpcLine}");
                    if (t.Options != null)
                    {
                        foreach (var opt in t.Options)
                        {
                            string action = opt.Action ?? "NONE";
                            DebugLogger.Log($"[CrimeDialog]   Option: \"{opt.PlayerLine}\" → {action}");
                        }
                    }
                }
            }

            return script;
        }

        private static bool IsAuthority(Hero npc, WorldEvent evt)
        {
            var authority = WorldEventStore.GetAuthorityNpc(evt);
            return npc == authority || (npc?.Occupation == Occupation.Headman || npc?.Occupation == Occupation.RuralNotable);
        }

        private static IntentContext BuildIntentContext(WorldEvent evt, Hero speaker)
        {
            // 检测是否在 Mission 内（村庄/酒馆等3D场景）。大地图对话无法触发战斗。
            bool isInMission = TaleWorlds.MountAndBlade.Mission.Current != null;
            return new IntentContext
            {
                ActiveEvent = evt,
                Hero = speaker,
                Player = Hero.MainHero,
                IsInMission = isInMission
            };
        }

        private static DialogueInjector.DialogueInjectScript BuildAuthorityScript(
            WorldEvent evt, Hero speaker, Hero listener, PlaceholderResolver r, IntentContext ctx)
        {
            var turns = new List<DialogueInjector.DialogueInjectTurn>();

            switch (evt.Stage)
            {
                case EventStage.Emerging:
                    if (evt.PlayerTookInvestigationQuest)
                        BuildReportTurn(turns, r, ctx);
                    else
                        BuildDiscoveryTurn(turns, r, ctx);
                    break;
                case EventStage.Active:
                    if (evt.SuspectIsPlayer)
                        BuildConfrontPlayerTurn(turns, r, ctx);
                    else
                        BuildBountyOfferTurn(turns, r, ctx);
                    break;
                case EventStage.Confrontation:
                    BuildRetaliationTurn(turns, r, ctx);
                    break;
            }

            // EntryOption 按阶段选不同语义，避免"接完任务还在问听说出事了"
            string entryOption = evt.Stage switch
            {
                EventStage.Emerging when evt.PlayerTookInvestigationQuest =>
                    r.Resolve("关于{TargetSettlementName}那个案子……", "EntryOption"),
                EventStage.Emerging =>
                    r.Resolve("{SpeakerRole}，听说{TargetSettlementName}出了点事？", "EntryOption"),
                EventStage.Active when evt.SuspectIsPlayer =>
                    r.Resolve("{SpeakerRole}，{SpeakerSelfRef}有话跟你说。", "EntryOption"),
                EventStage.Active =>
                    r.Resolve("{SpeakerRole}，关于那桩悬赏……", "EntryOption"),
                EventStage.Confrontation =>
                    r.Resolve("{SpeakerRole}……", "EntryOption"),
                _ => r.Resolve("{SpeakerRole}，听说{TargetSettlementName}出了点事？", "EntryOption"),
            };

            return new DialogueInjector.DialogueInjectScript
            {
                EntryOption = entryOption,
                EntryTurn = "start",
                Turns = turns
            };
        }

        private static void BuildDiscoveryTurn(List<DialogueInjector.DialogueInjectTurn> turns, PlaceholderResolver r, IntentContext ctx)
        {
            var evt = r.Event;
            var turn = new DialogueInjector.DialogueInjectTurn
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
                    PlayerLine = "（低头）是我干的。",
                    NpcResponse = r.Resolve("{SpeakerPlayerAddr}？！……好，既然自己认了，咱们可以商量。"),
                    Action = "INTENT:Confess",
                    NextTurn = "confess"
                });
                turns.Add(BuildConfessTurn(r, ctx));
                turns.Add(BuildRestitutionDetailTurn(r, ctx, "restitution_detail", "continue_chat"));
            }

            turns.Add(turn);
            turns.Add(BuildContinueChatTurn());
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
            var evt = r.Event;
            var turn = new DialogueInjector.DialogueInjectTurn
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
                }
            };

            // 动态生成栽赃候选
            var frameTargets = TheftLedger.GetFrameableTargets();
            foreach (var target in frameTargets.Skip(1)) // Skip "bandit" (already above)
            {
                if (target.CanShowEvidence)
                {
                    // 有证物 → 展开每一件赃物为独立选项
                    var evidenceItems = TheftLedger.GetEvidenceItems(target.TargetId);
                    foreach (var evItem in evidenceItems)
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
            turns.Add(BuildContinueChatTurn());
        }

        private static void BuildConfrontPlayerTurn(List<DialogueInjector.DialogueInjectTurn> turns, PlaceholderResolver r, IntentContext ctx)
        {
            var evt = r.Event;
            // 大地图对话无法叫守卫/触发战斗 → 威胁失败的 NPC 回应降级为口头警告
            string threatFailLine = ctx.IsInMission
                ? r.Resolve("威胁{SpeakerSelfRef}？来人！")
                : r.Resolve("威胁{SpeakerSelfRef}？{SpeakerPlayerAddr}等着，{SpeakerSelfRef}会告到上面去。");
            var turn = new DialogueInjector.DialogueInjectTurn
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
            turns.Add(BuildContinueChatTurn());
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
            turns.Add(BuildContinueChatTurn());
        }

        private static void BuildRetaliationTurn(List<DialogueInjector.DialogueInjectTurn> turns, PlaceholderResolver r, IntentContext ctx)
        {
            var evt = r.Event;
            string npcLine;
            var options = new List<DialogueInjector.DialogueInjectOption>();

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
            turns.Add(BuildContinueChatTurn());
        }

        private static DialogueInjector.DialogueInjectScript BuildWitnessScript(
            WorldEvent evt, Hero speaker, Hero listener, PlaceholderResolver r, IntentContext ctx)
        {
            var turns = new List<DialogueInjector.DialogueInjectTurn>();

            var turn = new DialogueInjector.DialogueInjectTurn
            {
                Id = "start",
                SpeakerIndex = 0,
                NpcLine = evt.InitiatorIsPlayer
                    ? r.Resolve("（{SpeakerEmotion}地）{SpeakerPlayerAddr}是来问{CrimeScene}的事？{SpeakerSelfRef}……确实看见了。")
                    : r.Resolve("（{SpeakerEmotion}地）{SpeakerSelfRef}{TimeWord}在{CrimeScene}附近看见了一个人……"),
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
            turns.Add(BuildContinueChatTurn());
            return new DialogueInjector.DialogueInjectScript { EntryOption = "听说你看到了……？", EntryTurn = "start", Turns = turns };
        }

        private static DialogueInjector.DialogueInjectScript BuildSuspectScript(
            WorldEvent evt, Hero speaker, Hero listener, PlaceholderResolver r, IntentContext ctx)
        {
            var turns = new List<DialogueInjector.DialogueInjectTurn>
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
            turns.Add(BuildContinueChatTurn());
            return new DialogueInjector.DialogueInjectScript { EntryOption = "（打量了一下）……", EntryTurn = "start", Turns = turns };
        }

        private static DialogueInjector.DialogueInjectScript BuildBystanderScript(
            WorldEvent evt, Hero speaker, Hero listener, PlaceholderResolver r, IntentContext ctx)
        {
            var turns = new List<DialogueInjector.DialogueInjectTurn>();

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
            turns.Add(BuildContinueChatTurn());

            return new DialogueInjector.DialogueInjectScript { EntryOption = "最近村里有什么新鲜事？", EntryTurn = "start", Turns = turns };
        }

        /// <summary>继续聊 turn：NPC 说完事后 → 玩家可选回到犯罪对话或走人</summary>
        private static DialogueInjector.DialogueInjectTurn BuildContinueChatTurn()
        {
            return new DialogueInjector.DialogueInjectTurn
            {
                Id = "continue_chat",
                SpeakerIndex = 0,
                NpcLine = "…",
                Options = new List<DialogueInjector.DialogueInjectOption>
                {
                    new DialogueInjector.DialogueInjectOption { PlayerLine = "说点别的……", Action = "NONE", NextTurn = "start" },
                    new DialogueInjector.DialogueInjectOption { PlayerLine = "我得走了。", Action = "INTENT:WalkAway", NextTurn = "" },
                }
            };
        }

        /// <summary>收尾 turn：NPC 最后一句台词 + 玩家"……"→关闭窗口（保留供未来特定场景使用）</summary>
        private static DialogueInjector.DialogueInjectTurn BuildClosingTurn(PlaceholderResolver r, string turnId)
        {
            return new DialogueInjector.DialogueInjectTurn
            {
                Id = turnId,
                SpeakerIndex = 0,
                NpcLine = r.Resolve("{ConfrontClosingLine}"),
                Options = new List<DialogueInjector.DialogueInjectOption>
                {
                    new DialogueInjector.DialogueInjectOption { PlayerLine = "……", Action = "NONE", NextTurn = "close_window" },
                }
            };
        }
    }
}
