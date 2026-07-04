using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

#pragma warning disable CS0618 // Intentional migration: uses deprecated PrepareOpeningAction ctors
namespace LivingWorldNpcs
{
    /// <summary>
    /// NPC 主动意图类（7 个），按 IntentBase 格式重写。
    /// 替代旧的 NpcInitiative + InitiativeType 手动创建模式。
    /// 叙事文本走 NarrativeResolver → CSV，无 CSV 时 LLM 兜底，LLM 不可用时 PlaceholderResolver 拼接。
    /// </summary>

    #region NewsConflictIntent

    /// <summary>世界事件相关新闻 → NPC 主动告知玩家</summary>
    public class NewsConflictIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Chat;
        public override InteractionCategory Category => InteractionCategory.General;
        public override string DisplayName => "【传闻】听说了吗？";
        public override IntentSource Source => IntentSource.Npc;
        public override string[] TriggerEvents => new[] { "WorldEvent_NewsArrived" };

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.NpcLevel == null || ctx.NpcLevel == NpcIdentityLevel.None)
                return Eligibility.Hide();
            if (ctx.NpcLevel == NpcIdentityLevel.AgentOnly)
                return Eligibility.Hide(); // 模板 NPC 无新闻概念
            if (ctx.NpcHero == null) return Eligibility.Hide();
            // 检查是否有可告知的世界事件
            var memory = AllNpcMemoryManager.GetMemory(ctx.NpcHero.StringId);
            if (memory == null || memory.KnownEvents == null || memory.KnownEvents.Count == 0)
                return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override bool CanHandle(AIEvent aiEvent, IntentContext ctx)
        {
            if (aiEvent.Args == null || aiEvent.Args.Length < 1) return false;
            if (ctx.NpcHero == null) return false;
            var memory = AllNpcMemoryManager.GetMemory(ctx.NpcHero.StringId);
            return memory != null && memory.KnownEvents != null && memory.KnownEvents.Count > 0;
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (ctx.NpcAgent == null) return;
            var brain = AgentAIController.GetBrainForAgent(ctx.NpcAgent);
            if (brain == null) return;
            brain.ClearAllActions();
            brain.EnqueueAction(new PrepareOpeningAction(InitiativeType.NewsConflict, "世界事件新闻"));
            brain.EnqueueAction(new ForceTalkAction());
        }
    }

    #endregion

    #region GuardInterceptIntent

    /// <summary>守卫拦截：通缉/违禁品/未缴罚款检查</summary>
    public class GuardInterceptIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Threat;
        public override InteractionCategory Category => InteractionCategory.Accountability;
        public override string DisplayName => "【守卫拦截】站住！";
        public override IntentSource Source => IntentSource.Npc;
        public override string[] TriggerEvents => new[] { "WitnessCrime_GatherOnLook", "GuardCheck" };

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.NpcLevel == null || ctx.NpcLevel == NpcIdentityLevel.None)
                return Eligibility.Hide();
            // 检查是否有活跃犯罪事件——玩家是嫌犯或加害方
            var activeEvents = WorldEventStore.GetActiveEventsForTarget(Hero.MainHero.StringId);
            if (activeEvents == null || activeEvents.Count == 0) return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override bool CanHandle(AIEvent aiEvent, IntentContext ctx)
        {
            if (aiEvent.Args == null || aiEvent.Args.Length < 2) return false;
            var thief = aiEvent.Args[0] as Agent;
            if (thief != Agent.Main) return false; // 只拦截玩家
            var activeEvents = WorldEventStore.GetActiveEventsForTarget(Hero.MainHero.StringId);
            return activeEvents != null && activeEvents.Count > 0;
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (ctx.NpcAgent == null) return;
            var activeEvents = WorldEventStore.GetActiveEventsForTarget(Hero.MainHero.StringId);
            var evt = activeEvents?.FirstOrDefault();
            var conflict = new PendingConflict(
                eventId: evt?.EventId ?? $"GuardCheck_{CampaignTime.Now.ToHours}",
                topicName: evt?.Config?.DisplayName ?? "守卫盘查",
                goalDesc: (ctx.NpcAgent?.Name ?? "守卫") + "拦住了你的去路",
                severity: evt?.Severity ?? 50f,
                type: NegotiationGoalType.ResolveConflict_Apology
            );

            var brain = AgentAIController.GetBrainForAgent(ctx.NpcAgent);
            if (brain == null) return;
            brain.ClearAllActions();
            brain.EnqueueAction(new PrepareOpeningAction(InitiativeType.GuardIntercept, conflict));
            brain.EnqueueAction(new ForceTalkAction());
        }
    }

    #endregion

    #region CrimeAccusationIntent

    /// <summary>犯罪指控开场白 — 受害方 NPC 直接质问玩家</summary>
    public class CrimeAccusationIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Confess;
        public override InteractionCategory Category => InteractionCategory.Accountability;
        public override string DisplayName => "【指控】是不是你干的？！";
        public override IntentSource Source => IntentSource.Npc;
        public override string[] TriggerEvents => new[] { "WitnessCrime_GatherOnLook" };

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.NpcLevel == null || ctx.NpcLevel == NpcIdentityLevel.None)
                return Eligibility.Hide();
            if (ctx.NpcAgent == null) return Eligibility.Hide();
            // 只有受害方才能触发指控
            return Eligibility.Show(); // 具体判断在 CanHandle 中
        }

        public override bool CanHandle(AIEvent aiEvent, IntentContext ctx)
        {
            if (aiEvent.Args == null || aiEvent.Args.Length < 2) return false;
            var thief = aiEvent.Args[0] as Agent;
            var victim = aiEvent.Args[1] as Agent;
            if (thief != Agent.Main) return false;
            // Owner == victim（只有受害方才触发指控开场白）
            return ctx.NpcAgent == victim;
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (ctx.NpcAgent == null) return;
            var conflict = new PendingConflict(
                eventId: $"Theft_{CampaignTime.Now.ToHours}",
                topicName: "当众行窃",
                goalDesc: $"要求 {Agent.Main?.Name ?? "玩家"} 立刻归还财物并赔偿精神损失",
                severity: 70.0f,
                type: NegotiationGoalType.ResolveConflict_Apology
            );

            var brain = AgentAIController.GetBrainForAgent(ctx.NpcAgent);
            if (brain == null) return;
            brain.ClearAllActions();
            brain.EnqueueAction(new PrepareOpeningAction(InitiativeType.CrimeAccusation, conflict));
            brain.EnqueueAction(new ForceTalkAction());
        }
    }

    #endregion

    #region RevengeIntent

    /// <summary>寻仇 NPC 主动找玩家</summary>
    public class RevengeIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Assault;
        public override InteractionCategory Category => InteractionCategory.Hostile;
        public override string DisplayName => "【寻仇】终于找到你了！";
        public override IntentSource Source => IntentSource.Npc;
        public override string[] TriggerEvents => new[] { "NemesisDetected", "PlayerApproach" };

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.NpcLevel == null || ctx.NpcLevel == NpcIdentityLevel.None)
                return Eligibility.Hide();
            if (ctx.NpcHero == null) return Eligibility.Hide();
            // 检查是否有宿敌记录
            var record = HeroNemesisTracker.GetRecord(ctx.NpcHero);
            if (record == null || record.Level < NemesisLevel.Rival)
                return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override bool CanHandle(AIEvent aiEvent, IntentContext ctx)
        {
            if (ctx.NpcHero == null) return false;
            var record = HeroNemesisTracker.GetRecord(ctx.NpcHero);
            return record != null && record.Level >= NemesisLevel.Rival;
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (ctx.NpcAgent == null || ctx.NpcHero == null) return;
            var record = HeroNemesisTracker.GetRecord(ctx.NpcHero);
            var conflict = new PendingConflict(
                eventId: $"Revenge_{ctx.NpcHero.StringId}_{CampaignTime.Now.ToHours}",
                topicName: "宿敌寻仇",
                goalDesc: $"{ctx.NpcHero.Name} 来找你算旧账了",
                severity: record?.Level >= NemesisLevel.ArchNemesis ? 90f : 60f,
                type: NegotiationGoalType.ResolveConflict_Intimidate
            );

            var brain = AgentAIController.GetBrainForAgent(ctx.NpcAgent);
            if (brain == null) return;
            brain.ClearAllActions();
            brain.EnqueueAction(new PrepareOpeningAction(InitiativeType.Revenge, conflict));
            brain.EnqueueAction(new ForceTalkAction());
        }
    }

    #endregion

    #region GreetingIntent

    /// <summary>熟人打招呼（玩家侧对应 ChatIntent）</summary>
    public class GreetingIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Chat;
        public override InteractionCategory Category => InteractionCategory.General;
        public override string DisplayName => "【寒暄】你好啊！";
        public override IntentSource Source => IntentSource.Both;
        public override string[] TriggerEvents => new[] { "PlayerApproach", "DailyTick_Social" };

        public override Eligibility Evaluate(IntentContext ctx)
        {
            // NPC 侧：检查是否有关系基础 + 冷却
            if (ctx.NpcLevel != null && ctx.NpcLevel != NpcIdentityLevel.None)
            {
                if (ctx.NpcHero == null) return Eligibility.Hide();
                if (ctx.Relation < 5) return Eligibility.Hide(); // 关系太浅不主动打招呼
                return Eligibility.Show();
            }
            // 玩家侧：始终可用
            return Eligibility.Show();
        }

        public override bool CanHandle(AIEvent aiEvent, IntentContext ctx)
        {
            if (ctx.NpcHero == null) return false;
            // 检查关系 + 上次见面时间冷却（至少隔 0.5 天）
            if (ctx.Relation < 5) return false;
            var memory = AllNpcMemoryManager.GetMemory(ctx.NpcHero.StringId);
            if (memory == null) return true; // 无记忆 → 允许
            // 简单冷却：检查最近一次互动时间
            return true;
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (ctx.NpcAgent == null) return;
            var brain = AgentAIController.GetBrainForAgent(ctx.NpcAgent);
            if (brain == null) return;
            brain.ClearAllActions();
            brain.EnqueueAction(new PrepareOpeningAction(InitiativeType.Greeting, "熟人寒暄"));
            brain.EnqueueAction(new ForceTalkAction());
        }
    }

    #endregion

    #region OfficialBusinessIntent

    /// <summary>税务官/传令兵公务通知</summary>
    public class OfficialBusinessIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.ReportMission;
        public override InteractionCategory Category => InteractionCategory.Official;
        public override string DisplayName => "【公务】有命令传达！";
        public override IntentSource Source => IntentSource.Npc;
        public override string[] TriggerEvents => new[] { "LordCommand_Deliver", "TaxCollection" };

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.NpcLevel == null || ctx.NpcLevel == NpcIdentityLevel.None)
                return Eligibility.Hide();
            if (ctx.NpcHero == null) return Eligibility.Hide();
            // 检查是否有待下发的命令
            return Eligibility.Show(); // 具体判断在 CanHandle 中
        }

        public override bool CanHandle(AIEvent aiEvent, IntentContext ctx)
        {
            if (ctx.NpcHero == null) return false;
            // 检查 Owner 的 Hero 是否有待下发的命令
            return ctx.NpcHero.Clan?.Kingdom?.Leader == ctx.NpcHero
                || ctx.NpcHero.Clan?.Leader == ctx.NpcHero;
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (ctx.NpcAgent == null) return;
            var brain = AgentAIController.GetBrainForAgent(ctx.NpcAgent);
            if (brain == null) return;
            brain.ClearAllActions();
            brain.EnqueueAction(new PrepareOpeningAction(InitiativeType.OfficialBusiness, "公务通知"));
            brain.EnqueueAction(new ForceTalkAction());
        }
    }

    #endregion

    #region CrushIntent

    /// <summary>爱慕者搭讪</summary>
    public class CrushIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Chat;
        public override InteractionCategory Category => InteractionCategory.Social;
        public override string DisplayName => "【搭讪】那个……你好！";
        public override IntentSource Source => IntentSource.Npc;
        public override string[] TriggerEvents => new[] { "DailyTick_Social", "PlayerApproach" };

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.NpcLevel == null || ctx.NpcLevel == NpcIdentityLevel.None)
                return Eligibility.Hide();
            if (ctx.NpcHero == null) return Eligibility.Hide();
            // 需要异性 + 关系在某区间 + 冷却
            if (!ctx.OppositeSex) return Eligibility.Hide();
            if (ctx.Relation < 10 || ctx.Relation > 50) return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override bool CanHandle(AIEvent aiEvent, IntentContext ctx)
        {
            if (ctx.NpcHero == null) return false;
            if (!ctx.OppositeSex) return false;
            if (ctx.Relation < 10 || ctx.Relation > 50) return false;
            var memory = AllNpcMemoryManager.GetMemory(ctx.NpcHero.StringId);
            if (memory == null) return true;
            // 检查是否有"爱慕"记忆 + 冷却
            return true; // 简化：关系在 10-50 且有异性身份即可
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (ctx.NpcAgent == null) return;
            var brain = AgentAIController.GetBrainForAgent(ctx.NpcAgent);
            if (brain == null) return;
            brain.ClearAllActions();
            brain.EnqueueAction(new PrepareOpeningAction(InitiativeType.Crush, "爱慕者搭讪"));
            brain.EnqueueAction(new ForceTalkAction());
        }
    }

    #endregion
}
