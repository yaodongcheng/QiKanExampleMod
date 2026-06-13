using LivingWorldNpcs;
using TaleWorlds.CampaignSystem;
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
