using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    // ── 情报：打开百科（即时类）──
    public class InfoIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.Info; } }
        // 情报意图名：打开百科查看对方信息
        public override string DisplayName { get { return LWNTextHelper.ResolveText("LWN_intent_general_info_name", "Intel: View information"); } }
        // 情报意图提示：查看人物属性与关系
        public override string ToolTip { get { return LWNTextHelper.ResolveText("LWN_intent_general_info_tooltip", "View the character's attributes and relations"); } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.HasUrgentWorldEvent && !ctx.ExpandedOptions) return Eligibility.Hide();
            return ctx.IsHero ? Eligibility.Show() : Eligibility.Hide();
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (ctx.Speaker != null && Campaign.Current != null)
                Campaign.Current.EncyclopediaManager.GoToLink(ctx.Speaker.EncyclopediaLink);
        }
    }

    // ── 命令士兵：询问状况（即时类，无 LLM 时给固定台词）──
    public class OrderSoldierIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.Order; } }
        // 命令意图名：询问士兵状况
        public override string DisplayName { get { return LWNTextHelper.ResolveText("LWN_intent_general_order_name", "Order: Report your status"); } }
        // 命令意图提示：询问士兵当前状态
        public override string ToolTip { get { return LWNTextHelper.ResolveText("LWN_intent_general_order_tooltip", "Ask the soldier about their current status"); } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.HasUrgentWorldEvent && !ctx.ExpandedOptions) return Eligibility.Hide();
            return ctx.IsMySoldier ? Eligibility.Show() : Eligibility.Hide();
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (Settings.Instance.IsLLMReady)
            {
                // LLM 命令开场白：让士兵汇报当前情况
                ctx.Controller.SendIntent("Order", LWNTextHelper.ResolveText("LWN_intent_general_order_prompt", "Report your situation, soldier!"));
            }
            else
            {
                string line = DialogueTemplateHelper.Get("Order", out string emotion, ctx.Speaker, ctx.Agent);
                ctx.Controller.ShowNpcLineKeepMenu(ctx.Agent, line, emotion);
            }
        }
    }

    // ── 跟随：让对方跟随玩家（即时类）──
    public class FollowIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.Order_Follow; } }
        // 跟随意图名：让对方跟随行动
        public override string DisplayName { get { return LWNTextHelper.ResolveText("LWN_intent_general_follow_name", "Follow: Follow me"); } }
        // 跟随意图提示：让对方跟随你行动
        public override string ToolTip { get { return LWNTextHelper.ResolveText("LWN_intent_general_follow_tooltip", "Ask the other to follow you"); } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.HasUrgentWorldEvent && !ctx.ExpandedOptions) return Eligibility.Hide();
            return (ctx.IsHero || ctx.IsMySoldier) ? Eligibility.Show() : Eligibility.Hide();
        }

        public override void OnInstant(IntentContext ctx)
        {
            ctx.Controller._vm.Close();
            var brain = AgentAIController.GetBrainForAgent(ctx.Agent);
            if (brain == null) return;
            brain.SetLeader(Agent.Main);
            brain.SetGuardMode(true);
            AgentAIController.Instance.SendEventToAgent(ctx.Agent, "order_follow", Agent.Main);
        }
    }

    // ── 寒暄：有 LLM 走自由聊天；无 LLM 走话题菜单（即时类）──
    public class ChatIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.Chat; } }
        // 寒暄意图名：随便聊聊
        public override string DisplayName { get { return LWNTextHelper.ResolveText("LWN_intent_general_chat_name", "Chat: Just talk..."); } }
        // 寒暄意图提示：与对方闲聊
        public override string ToolTip { get { return LWNTextHelper.ResolveText("LWN_intent_general_chat_tooltip", "Have a chat with the other"); } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            // 危机时折叠——开场白已传达事件信息，寒暄不提供增量价值
            if (ctx.HasUrgentWorldEvent && !ctx.ExpandedOptions) return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (Settings.Instance.IsLLMReady)
                ctx.Controller.OpenFreeChatInput(ctx.Agent);
            else
                ctx.Controller.OpenChatTopicMenu(ctx);
        }
    }

    // ── 展开折叠选项：事件 NPC 初始只显示核心选项，点击后展开全部。
    //     BugFix: ExpandedOptions 原来在 IntentContext 上每次重建丢失，现已移到 Controller。 ──
    public class ExpandOptionsIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.Chat; } }
        // 展开折叠选项名：还有其他事情要说
        public override string DisplayName { get { return LWNTextHelper.ResolveText("LWN_intent_general_expand_name", "Other matters: Something else..."); } }
        // 展开折叠选项提示：展开更多选项
        public override string ToolTip { get { return LWNTextHelper.ResolveText("LWN_intent_general_expand_tooltip", "Expand more options"); } }
        public override NegotiationGoalType? Goal => null;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (!ctx.HasUrgentWorldEvent || ctx.Controller.OptionsExpanded) return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            ctx.Controller.OptionsExpanded = true;
            ctx.Controller.RefreshInitialOptions();
        }
    }

    // ── 劝降：威吓敌方士兵投降（即时类）──
    public class PersuadeSurrenderIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.PersuadeSurrender; } }
        // 劝降意图名：威吓敌方士兵投降
        public override string DisplayName { get { return LWNTextHelper.ResolveText("LWN_intent_general_surrender_name", "Surrender: Lay down your arms and live"); } }
        // 劝降意图提示：兵力悬殊时成功率更高
        public override string ToolTip { get { return LWNTextHelper.ResolveText("LWN_intent_general_surrender_tooltip", "Intimidate enemy soldiers into surrendering - better odds when outnumbered"); } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.HasUrgentWorldEvent && !ctx.ExpandedOptions) return Eligibility.Hide();
            if (!ctx.IsEnemyAgent) return Eligibility.Hide();
            if (ctx.IsHero) return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            try
            {
                var enemyParty = MapEncounterDialogState.PartnerParty?.MobileParty;
                int playerTroops = MobileParty.MainParty?.MemberRoster.TotalHealthyCount ?? 1;
                int enemyTroops = enemyParty?.MemberRoster.TotalHealthyCount ?? 10;

                float ratio = (float)playerTroops / Math.Max(enemyTroops, 1);
                float baseChance = Math.Max(0.05f, Math.Min(0.9f, ratio * 0.7f));
                float charmBonus = (Hero.MainHero.GetSkillValue(DefaultSkills.Charm) / 300f) * 0.3f;
                float rogueryBonus = (Hero.MainHero.GetSkillValue(DefaultSkills.Roguery) / 300f) * 0.2f;
                float finalChance = baseChance + charmBonus + rogueryBonus;
                bool success = MBRandom.RandomFloat < finalChance;

                if (success)
                {
                    int captured = 1 + MBRandom.RandomInt(Math.Min(5, enemyTroops));
                    if (enemyParty != null && MobileParty.MainParty != null)
                    {
                        for (int i = 0; i < captured && enemyParty.MemberRoster.TotalRegulars > 0; i++)
                        {
                            CharacterObject firstNonHero = null;
                            foreach (var t in enemyParty.MemberRoster.GetTroopRoster())
                            {
                                if (!t.Character.IsHero) { firstNonHero = t.Character; break; }
                            }
                            if (firstNonHero == null) break;
                            // 从敌方移除
                            enemyParty.MemberRoster.AddToCounts(firstNonHero, -1);
                            // 加入玩家俘虏
                            MobileParty.MainParty.PrisonRoster.AddToCounts(firstNonHero, 1);
                        }
                    }

                    // 投降比例高 → 敌军溃散，遭遇战结束
                    float surrenderRatio = (float)captured / Math.Max(enemyTroops, 1);
                    if (surrenderRatio > 0.3f || enemyParty == null || enemyParty.MemberRoster.TotalRegulars <= 1)
                    {
                        string line;
                        if (captured > 1)
                        {
                            // 劝降成功台词：对方多人集体投降
                            line = LWNTextHelper.ResolveCompound("LWN_intent_general_surrender_success_multi",
                                ("CAPTURED", captured.ToString()));
                        }
                        else
                        {
                            // 劝降成功台词：对方单人投降
                            line = LWNTextHelper.ResolveText("LWN_intent_general_surrender_success_single", "...I surrender! They threw down their weapons, shaking.");
                        }
                        ctx.Controller.SceneSay(line,
                            // 劝降成功后的离开选项：玩家告辞离开
                            new StoryOptionVM(LWNTextHelper.ResolveText("LWN_intent_general_leave_option", "(Leave)"), () => ctx.Controller._vm.Close()));
                    }
                    else
                    {
                        string line;
                        if (captured > 1)
                        {
                            // 劝降部分成功台词：多人扔下武器被押入俘虏队
                            line = LWNTextHelper.ResolveCompound("LWN_intent_general_surrender_partial_multi",
                                ("CAPTURED", captured.ToString()));
                        }
                        else
                        {
                            // 劝降部分成功台词：单人跪地求饶成为俘虏
                            line = LWNTextHelper.ResolveText("LWN_intent_general_surrender_partial_single", "...Mercy! He knelt and begged, taken as your prisoner.");
                        }
                        ctx.Controller.SceneSay(line);
                    }
                    DebugLogger.Log($"[PersuadeSurrender] SUCCESS: captured {captured}/{enemyTroops} from {enemyParty?.Name}, chance={finalChance:P0}");
                }
                else
                {
                    // 失败：对方被激怒，对话强制结束
                    string line;
                    if (enemyTroops > 20)
                    {
                        // 劝降失败台词：对方人多势众，嘲笑并拒绝
                        line = LWNTextHelper.ResolveText("LWN_intent_general_surrender_fail_confident", "You? They laughed, their fighting spirit even fiercer. Negotiations broke down.");
                    }
                    else
                    {
                        // 劝降失败台词：对方愤怒拒绝再谈
                        line = LWNTextHelper.ResolveText("LWN_intent_general_surrender_fail_angry", "...Get lost. They gripped their weapon and refused to talk.");
                    }
                    ctx.Controller.SceneSay(line,
                        // 劝降失败后的离开选项：玩家告辞离开
                        new StoryOptionVM(LWNTextHelper.ResolveText("LWN_intent_general_leave_option", "(Leave)"), () => ctx.Controller._vm.Close()));
                    DebugLogger.Log($"[PersuadeSurrender] FAIL: chance={finalChance:P0}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PersuadeSurrender] error: {ex.Message}");
            }
        }
    }

    // ── 离开：结束对话（即时类，永远最后）──
    public class LeaveIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.Leave; } }
        // 离开意图名：结束对话告辞
        public override string DisplayName { get { return LWNTextHelper.ResolveText("LWN_intent_general_leave_name", "Leave: Take my leave"); } }
        // 离开意图提示：结束对话
        public override string ToolTip { get { return LWNTextHelper.ResolveText("LWN_intent_general_leave_tooltip", "End the conversation"); } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (Agent.Main != null)
                AgentAIController.Instance.BroadcastEventInRange(Agent.Main.Position, 15.0f, "EndInteraction", false, Agent.Main);
            GroupStageManager.Reset(Agent.Main);
            ctx.Controller._vm.Close();
        }
    }
}
