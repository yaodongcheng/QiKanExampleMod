using System;
using System.Linq;
using LivingWorldNpcs;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.Story
{
    // ── 情报：打开百科（即时类）──
    public class InfoIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.Info; } }
        public override string DisplayName { get { return "【情报】 查看信息"; } }
        public override string ToolTip { get { return "查看对方的人物属性和关系"; } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.HasUrgentWorldEvent && !ctx.ExpandedOptions) return Eligibility.Hide();
            return ctx.IsHero ? Eligibility.Show() : Eligibility.Hide();
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (ctx.Hero != null && Campaign.Current != null)
                Campaign.Current.EncyclopediaManager.GoToLink(ctx.Hero.EncyclopediaLink);
        }
    }

    // ── 命令士兵：询问状况（即时类，无 LLM 时给固定台词）──
    public class OrderSoldierIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.Order; } }
        public override string DisplayName { get { return "【命令】 询问状况"; } }
        public override string ToolTip { get { return "询问士兵当前的状态"; } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.HasUrgentWorldEvent && !ctx.ExpandedOptions) return Eligibility.Hide();
            return ctx.IsMySoldier ? Eligibility.Show() : Eligibility.Hide();
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (Settings.Instance.IsLLMReady)
            {
                ctx.Controller.SendIntent("Order", "汇报你的情况，士兵！");
            }
            else
            {
                string line = DialogueTemplateHelper.Get("Order", out string emotion, ctx.Hero, ctx.Agent);
                ctx.Controller.ShowNpcLineKeepMenu(ctx.Agent, line, emotion);
            }
        }
    }

    // ── 跟随：让对方跟随玩家（即时类）──
    public class FollowIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.Order_Follow; } }
        public override string DisplayName { get { return "【跟随】 跟随我"; } }
        public override string ToolTip { get { return "让对方跟随你行动"; } }

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
        public override string DisplayName { get { return "【寒暄】 随便说两句..."; } }
        public override string ToolTip { get { return "与对方闲聊"; } }

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

    // ── 展开折叠选项：危机时显示，点击后展开"有别的事找你" ──
    public class ExpandOptionsIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.Chat; } }
        public override string DisplayName { get { return "【其他事情】 有别的事找你..."; } }
        public override string ToolTip { get { return "展开更多选项"; } }
        public override NegotiationGoalType? Goal => null;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            // 仅当 NPC 有紧迫事件且尚未展开时显示
            if (!ctx.HasUrgentWorldEvent || ctx.ExpandedOptions) return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            ctx.ExpandedOptions = true;
            ctx.Controller.RefreshInitialOptions();
        }
    }

    // ── 劝降：威吓敌方士兵投降（即时类）──
    public class PersuadeSurrenderIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.PersuadeSurrender; } }
        public override string DisplayName { get { return "【劝降】 放下武器，饶你不死"; } }
        public override string ToolTip { get { return "威吓敌方士兵投降——兵力悬殊时成功率更高"; } }

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
                        string line = captured > 1
                            ? $"「……我们投降！别杀我们——」对方战意全失，{captured} 人放下武器束手就擒。"
                            : $"「……我投降！」对方颤抖着扔下武器，束手就擒。";
                        ctx.Controller.SceneSay(line,
                            new StoryOptionVM("（离开）", () => ctx.Controller._vm.Close()));
                    }
                    else
                    {
                        string line = captured > 1
                            ? $"「……我们投降！」有 {captured} 人扔下武器，被押入你的俘虏队。"
                            : $"「……饶命！」对方跪地求饶，成了你的俘虏。";
                        ctx.Controller.SceneSay(line);
                    }
                    DebugLogger.Log($"[PersuadeSurrender] SUCCESS: captured {captured}/{enemyTroops} from {enemyParty?.Name}, chance={finalChance:P0}");
                }
                else
                {
                    // 失败：对方被激怒，对话强制结束
                    string line = enemyTroops > 20
                        ? $"「就凭你？」对方大笑，战意反而更盛。交涉破裂。"
                        : $"「……滚。」对方握紧了武器，拒绝再谈。";
                    ctx.Controller.SceneSay(line,
                        new StoryOptionVM("（离开）", () => ctx.Controller._vm.Close()));
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
        public override string DisplayName { get { return "【离开】 告辞"; } }
        public override string ToolTip { get { return "结束对话"; } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (Agent.Main != null)
                AgentAIController.Instance.BroadcastEventInRange(Agent.Main.Position, 15.0f, "EndInteraction", Agent.Main);
            GroupStageManager.Reset(Agent.Main);
            ctx.Controller._vm.Close();
        }
    }
}
