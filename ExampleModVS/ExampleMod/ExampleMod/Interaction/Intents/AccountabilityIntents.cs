using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace LivingWorldNpcs.Story
{
    /// <summary>
    /// 追责相关 Intent（14 个）。
    /// 逻辑层：每个 Intent 声明 Evaluate/OnInstant/OnSuccess/OnFail。
    /// 表现层：DialogueInjector JSON 通过 "INTENT:xxx" 委托到这些 Intent。
    ///
    /// 🛞 复用 IntentBase 标准模式：Evaluate (三态) / OnSuccess / OnFail / OnInstant
    /// 🛞 复用 SingleRollResolver 检定公式
    /// 🛞 复用 AgentControlHelper.TransferGold 资源操作
    /// 🛞 复用 IntentCooldownStore 冷却
    /// </summary>

    #region InteractionOptionType Extension

    // 追责 Intent 使用自定义 Type（不冲突现有枚举）
    public enum AccountabilityOptionType
    {
        // 核心追责
        PayRestitution,        // 赔钱消灾
        CharmDefense,          // Charm 辩护
        FrameSuspect,          // 栽赃嫁祸
        Threat,                // 威胁
        Investigate,           // 接调查 Quest
        Confess,               // 自首——低头认罪，跳转讨价还价 turn
        SilenceWitness,        // 收买/吓唬目击者
        LeadRetaliation,       // 带队报复

        // 当面对峙（Mission 内）
        PayOnTheSpot,          // 当场赔钱 ×2
        WorkOffDebt,           // 干活抵债
        FleeFromConfrontation, // 推开逃跑
        FightVillagers,        // 拔剑

        // 追捕
        BetrayQuest,           // 背叛 Quest
        InnocenceProof,        // 被冤枉时证明清白
        Arrest,                // 直接抓捕
        LureArrest,            // 诱捕
        AcceptBountyQuest,     // 接悬赏 Quest
        Settle,                // 和解劝说（报复阶段）
    }

    #endregion

    #region PayRestitutionIntent

    public class PayRestitutionIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Chat; // 不通过主菜单显示,由DialogueInjector调用
        public override string DisplayName => "【赔钱消灾】我愿意赔偿损失";
        public override NegotiationTactic Tactic => NegotiationTactic.Flatter;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.ActiveEvent == null) return Eligibility.Hide();
            if (ctx.ActiveEvent.InitiatorId != Hero.MainHero.StringId) return Eligibility.Hide();
            // 赔钱在 Active/Confrontation 阶段始终可用；Emerging 阶段只有自首后（SuspectHeroId=玩家）才可用
            bool stageOk = ctx.ActiveEvent.Stage == EventStage.Active
                        || ctx.ActiveEvent.Stage == EventStage.Confrontation
                        || (ctx.ActiveEvent.Stage == EventStage.Emerging && ctx.ActiveEvent.SuspectHeroId == Hero.MainHero.StringId);
            if (!stageOk) return Eligibility.Hide();

            int cost = ctx.ActiveEvent.ComputeRestitutionCost();
            if (Hero.MainHero.Gold < cost)
                return Eligibility.Grey($"钱不够（需要 {cost} 第纳尔）");
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt == null) return;

            int cost = evt.ComputeRestitutionCost();
            var authority = WorldEventStore.GetAuthorityNpc(evt);
            if (authority != null)
                AgentControlHelper.TransferGold(Hero.MainHero, authority, cost);
            else
                AgentControlHelper.TransferGold(Hero.MainHero, null, cost);

            WorldEventStore.OnPlayerPaidRestitution(evt);
            PlayerTheftLedger.MarkCleared(evt.TargetSettlementId);
            DebugLogger.Log($"[Accountability] Player paid restitution {cost} gold for {evt.EventId}");
        }
    }

    #endregion

    #region CharmDefenseIntent

    public class CharmDefenseIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Chat; // 不通过主菜单显示,由DialogueInjector调用
        public override string DisplayName => "【Charm 辩护】你们搞错了，给我个机会说清楚";
        public override NegotiationGoalType? Goal => NegotiationGoalType.ResolveConflict_Explain;
        public override NegotiationTactic Tactic => NegotiationTactic.Flatter;
        public override float CooldownDays => 0f; // 每案仅一次，靠 CharmReprieveUsed 守卫

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.ActiveEvent == null) return Eligibility.Hide();
            if (ctx.ActiveEvent.SuspectHeroId != Hero.MainHero.StringId) return Eligibility.Hide();
            if (ctx.ActiveEvent.CharmReprieveUsed) return Eligibility.Grey("已经用过一次了（村长不会再信）");
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt == null) return;
            WorldEventStore.OnCharmReprieve(evt);
            DebugLogger.Log($"[Accountability] Charm defense succeeded for {evt.EventId} — suspect downgraded");
        }

        public override void OnFail(IntentContext ctx)
        {
            base.OnFail(ctx);
            var evt = ctx.ActiveEvent;
            if (evt == null) return;
            ChangeRelationAction.ApplyPlayerRelation(ctx.Hero, -10, false, true);
            WorldEventStore.TransitionStage(evt, EventStage.Confrontation);
            DebugLogger.Log($"[Accountability] Charm defense failed for {evt.EventId} — → Confrontation");
        }
    }

    #endregion

    #region FrameSuspectIntent

    public class FrameSuspectIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Chat; // 不通过主菜单显示,由DialogueInjector调用
        public override string DisplayName => "【栽赃】是别人干的！";
        public override NegotiationGoalType? Goal => NegotiationGoalType.ResolveConflict_Explain;
        public override NegotiationTactic Tactic => NegotiationTactic.Flatter;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.ActiveEvent == null) return Eligibility.Hide();
            var evt = ctx.ActiveEvent;
            if (evt.Stage != EventStage.Emerging && evt.Stage != EventStage.Active)
                return Eligibility.Hide();
            return Eligibility.Show();
        }

        /// <summary>有效证据时提升成功率：+20 相当于出示物证的说服力加成</summary>
        public override float GetOfferValue(IntentContext ctx)
        {
            if (!string.IsNullOrEmpty(ctx.FrameTargetId) && ctx.FrameTargetId != "bandit")
            {
                if (PlayerTheftLedger.HasRecordFor(ctx.FrameTargetId))
                    return 0.6f;  // 有证物 → 高说服力
            }
            return 0.2f;  // 无证物 → 裸过
        }

        public override void OnSuccess(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt == null) return;

            // 从 IntentContext 获取当前选定的栽赃目标
            string targetId = ctx.FrameTargetId ?? "bandit";
            evt.SuspectHeroId = targetId == "bandit" ? GetBanditLeaderId() : targetId;
            evt.InvestigationProgress = 1.0f;
            WorldEventStore.TransitionStage(evt, EventStage.Active);
            DebugLogger.Log($"[Accountability] Frame suspicion: {targetId} blamed for {evt.EventId}");
        }

        public override void OnFail(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt == null) return;

            evt.FailCount++;
            if (evt.FailCount >= 2 && evt.InitiatorId == Hero.MainHero.StringId)
            {
                // Fail forward: 嫌疑转回玩家
                evt.SuspectHeroId = Hero.MainHero.StringId;
                evt.InvestigationProgress = 1.0f;
                WorldEventStore.TransitionStage(evt, EventStage.Active);
                DebugLogger.Log($"[Accountability] Frame suspicion failed twice — suspect reverts to player");
            }
        }

        private static string GetBanditLeaderId()
        {
            // 找附近藏身处的强盗头子
            var hideout = Settlement.All?.FirstOrDefault(s =>
                s.IsHideout && s.Position2D.Distance(
                    Hero.MainHero.CurrentSettlement?.Position2D ?? s.Position2D) < 80f);
            if (hideout?.Notables?.FirstOrDefault() != null)
                return hideout.Notables.First().StringId;

            // 兜底：找任意一个活着的强盗 notable（属土匪阵营）
            var anyBandit = Hero.AllAliveHeroes.FirstOrDefault(h =>
                h.Clan?.IsBanditFaction == true || h.Occupation == TaleWorlds.CampaignSystem.Occupation.Bandit);
            if (anyBandit != null) return anyBandit.StringId;

            // 最终兜底：找任意 wanderer（最像坏人的人）
            var fallback = Hero.AllAliveHeroes
                .Where(h => h.IsWanderer && h != Hero.MainHero)
                .OrderByDescending(h => h.GetSkillValue(TaleWorlds.Core.DefaultSkills.Roguery))
                .FirstOrDefault();
            if (fallback != null) return fallback.StringId;

            return "bandit"; // 终极兜底——系统会将其视为无 Hero 的抽象目标
        }
    }

    #endregion

    #region ThreatIntent

    public class ThreatIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Chat; // 不通过主菜单显示,由DialogueInjector调用
        public override string DisplayName => "【威胁】你再说一遍？（手按在剑柄上）";
        public override NegotiationGoalType? Goal => NegotiationGoalType.ResolveConflict_Intimidate;
        public override NegotiationTactic Tactic => NegotiationTactic.Flatter;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.ActiveEvent == null) return Eligibility.Hide();
            if (ctx.ActiveEvent.SuspectHeroId != Hero.MainHero.StringId) return Eligibility.Hide();
            if (Hero.MainHero.GetSkillValue(DefaultSkills.Roguery) < 50)
                return Eligibility.Grey($"Roguery 技能不足（需要 50，当前 {Hero.MainHero.GetSkillValue(DefaultSkills.Roguery):0}）");
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt == null) return;
            WorldEventStore.OnIntimidated(evt);
            // 恶名+1
            InfamySystem.AddInfamy(1);
            DebugLogger.Log($"[Accountability] Threat succeeded for {evt.EventId}");
        }

        public override void OnFail(IntentContext ctx)
        {
            base.OnFail(ctx);
            var evt = ctx.ActiveEvent;
            if (evt == null) return;
            WorldEventStore.TransitionStage(evt, EventStage.Confrontation);
            DebugLogger.Log($"[Accountability] Threat failed — → Confrontation");
        }
    }

    #endregion

    #region InvestigateIntent

    public class InvestigateIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Chat; // 不通过主菜单显示,由DialogueInjector调用
        public override string DisplayName => "【接调查任务】我可以帮忙查查是谁干的";
        public override NegotiationGoalType? Goal => null; // 即时类

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.ActiveEvent == null) return Eligibility.Hide();
            var evt = ctx.ActiveEvent;
            if (evt.Stage != EventStage.Emerging) return Eligibility.Hide();
            if (evt.PlayerTookInvestigationQuest) return Eligibility.Grey("你已经在调查这个案子了");
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt == null) return;
            evt.PlayerTookInvestigationQuest = true;
            DebugLogger.Log($"[Accountability] Player accepted investigation quest for {evt.EventId}");

            // 创建实际的 CommissionQuest 并加入 Journal
            var authority = WorldEventStore.GetAuthorityNpc(evt);
            if (authority != null)
            {
                var data = CommissionGenerator.TryGenerateAccountabilityQuest(authority);
                if (data != null)
                {
                    string questId = $"investigate_{evt.EventId}";
                    var quest = new CommissionQuest(questId, data);
                    quest.StartQuest();
                    DebugLogger.Log($"[Accountability] Investigation quest STARTED: {questId} giver={authority.Name}");
                }
                else
                {
                    DebugLogger.Log($"[Accountability] TryGenerateAccountabilityQuest returned null for {evt.EventId}");
                }
            }
            else
            {
                DebugLogger.Log($"[Accountability] No authority NPC found for {evt.EventId}");
            }
        }
    }

    #endregion

    #region ConfessIntent

    /// <summary>
    /// 自首——"是我干的"。不改变结算状态，但立即将 SuspectHeroId 设为玩家，
    /// 并跳转对话到 confess turn（赔钱/Charm辩护/走人）。
    /// 对标 KCD2：先认罪再讨价还价——认罪本身不是终结。
    /// </summary>
    public class ConfessIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Chat;
        public override string DisplayName => "【自首】（低头）是我干的";
        public override NegotiationGoalType? Goal => null;  // 即时类——不检定，直接跳转

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.ActiveEvent == null) return Eligibility.Hide();
            if (ctx.ActiveEvent.Stage != EventStage.Emerging && ctx.ActiveEvent.Stage != EventStage.Active)
                return Eligibility.Hide();
            // 只有真凶才能自首（被冤枉的人不需要"自首"，他们有 InnocenceProofIntent）
            if (ctx.ActiveEvent.InitiatorId != Hero.MainHero.StringId) return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt == null) return;

            // 自首 → 嫌犯立即确认（但事件不结算——留给后续 PayRestitution/CharmDefense）
            evt.SuspectHeroId = Hero.MainHero.StringId;
            evt.InvestigationProgress = 1.0f;
            DebugLogger.Log($"[Accountability] Player confessed for {evt.EventId} — suspect=self, awaiting resolution");
        }
    }

    #endregion

    #region SilenceWitnessIntent

    public class SilenceWitnessIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Chat; // 不通过主菜单显示,由DialogueInjector调用
        public override string DisplayName => "【封口】这事你别往外说……";
        public override NegotiationGoalType? Goal => NegotiationGoalType.ResolveConflict_Intimidate;
        public override NegotiationTactic Tactic => NegotiationTactic.Flatter;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.ActiveEvent == null) return Eligibility.Hide();
            var evt = ctx.ActiveEvent;
            if (evt.WitnessesSilenced) return Eligibility.Hide();
            if (evt.WitnessHeroIds == null || evt.WitnessHeroIds.Count == 0) return Eligibility.Hide();
            if (evt.InitiatorId != Hero.MainHero.StringId) return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt == null) return;
            evt.WitnessesSilenced = true;
            DebugLogger.Log($"[Accountability] Witnesses silenced for {evt.EventId}");
        }

        public override void OnFail(IntentContext ctx)
        {
            base.OnFail(ctx);
            var evt = ctx.ActiveEvent;
            if (evt == null) return;
            // 封口失败 → 目击者马上去报告
            evt.InvestigationProgress += 0.2f;
            DebugLogger.Log($"[Accountability] Witness silencing failed — investigation +0.2");
        }
    }

    #endregion

    #region PayOnTheSpotIntent

    public class PayOnTheSpotIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Chat; // 不通过主菜单显示,由DialogueInjector调用
        public override string DisplayName => "【当场赔钱】这是赔偿，够不够？";
        public override NegotiationGoalType? Goal => null;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.ActiveEvent == null) return Eligibility.Hide();
            int cost = ctx.ActiveEvent.ComputeOnSpotCost();
            if (Hero.MainHero.Gold < cost)
                return Eligibility.Grey($"钱不够（需要 {cost} 第纳尔）");
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt == null) return;
            int cost = evt.ComputeOnSpotCost();
            var authority = WorldEventStore.GetAuthorityNpc(evt);
            if (authority != null)
                AgentControlHelper.TransferGold(Hero.MainHero, authority, cost);
            else
                AgentControlHelper.TransferGold(Hero.MainHero, null, cost);
            WorldEventStore.OnPlayerPaidRestitution(evt);
            PlayerTheftLedger.MarkCleared(evt.TargetSettlementId);
        }
    }

    #endregion

    #region WorkOffDebtIntent

    public class WorkOffDebtIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Chat; // 不通过主菜单显示,由DialogueInjector调用
        public override string DisplayName => "【干活抵债】我没钱，但我可以帮村里干活";
        public override NegotiationGoalType? Goal => null;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.ActiveEvent == null) return Eligibility.Hide();
            if (ctx.ActiveEvent.Stage != EventStage.Emerging && ctx.ActiveEvent.Stage != EventStage.Active)
                return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt == null) return;
            // 3 天软约束：标记为干活抵债模式，不立即结案
            // 每天 DailyTick 检查玩家是否在村庄附近（≤1天距离），未到→违约→Confrontation
            evt._workOffDebtDay = (float)CampaignTime.Now.ToDays;
            evt._workOffDebtAccepted = true;
            evt._workOffDaysDone = 0;
            var settlementName = evt.TargetSettlement?.Name?.ToString() ?? "村里";
            TaleWorlds.Library.InformationManager.DisplayMessage(
                new TaleWorlds.Library.InformationMessage($"[干活抵债] 3天内每天回 {settlementName} 干活，违约后果自负。"));
            DebugLogger.Log($"[Accountability] Work-off-debt accepted for {evt.EventId}, due at day {evt._workOffDebtDay + 3}");
        }
    }

    #endregion

    #region FleeFromConfrontationIntent

    public class FleeFromConfrontationIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Chat; // 不通过主菜单显示,由DialogueInjector调用
        public override string DisplayName => "【推开逃跑】（推开身边的人就跑）";
        public override NegotiationGoalType? Goal => NegotiationGoalType.ResolveConflict_Intimidate;
        public override NegotiationTactic Tactic => NegotiationTactic.Flatter;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.ActiveEvent == null) return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt == null) return;
            evt.SuspectHeroId = Hero.MainHero.StringId;
            evt.InvestigationProgress = 1.0f;
            WorldEventStore.TransitionStage(evt, EventStage.Active);
            DebugLogger.Log($"[Accountability] Player fled confrontation for {evt.EventId}");
        }

        public override void OnFail(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt == null) return;
            ChangeRelationAction.ApplyPlayerRelation(ctx.Hero, -15, false, true);
            WorldEventStore.TransitionStage(evt, EventStage.Confrontation);
        }
    }

    #endregion

    #region FightVillagersIntent

    public class FightVillagersIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Chat; // 不通过主菜单显示,由DialogueInjector调用
        public override string DisplayName => "【拔剑】（拔出武器）谁敢拦我！";
        public override NegotiationGoalType? Goal => null;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.ActiveEvent == null) return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt == null) return;
            WorldEventStore.TransitionStage(evt, EventStage.Confrontation);
            InfamySystem.AddInfamy(5);
            // 立即 spawn 报复部队（村民当场组织）
            InvestigationEngine.SpawnRetaliationParty(evt);
            // 标记村庄警觉
            if (!string.IsNullOrEmpty(evt.TargetSettlementId))
                evt.PermanentEnemy = true;
            TaleWorlds.Library.InformationManager.DisplayMessage(
                new TaleWorlds.Library.InformationMessage("村民愤怒了！有人抄起家伙围了过来……快离开这里！",
                    Colors.Red));
            DebugLogger.Log($"[Accountability] Player fought villagers for {evt.EventId} — retaliation spawned");
        }
    }

    #endregion

    #region BetrayQuestIntent

    public class BetrayQuestIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Chat; // 不通过主菜单显示,由DialogueInjector调用
        public override string DisplayName => "【背叛】快跑！村里人在抓你。";
        public override NegotiationGoalType? Goal => null;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.ActiveEvent == null) return Eligibility.Hide();
            if (ctx.ActiveEvent.SuspectHeroId == Hero.MainHero.StringId) return Eligibility.Hide();
            if (!ctx.ActiveEvent.PlayerTookBountyQuest) return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt == null) return;

            // Fail the active bounty quest if one exists
            try
            {
                var activeQuest = Campaign.Current?.QuestManager?.Quests
                    .FirstOrDefault(q => q is CommissionQuest cq
                        && cq.Data?.WorldEventId == evt.EventId
                        && q.IsOngoing);
                if (activeQuest != null)
                {
                    activeQuest.CompleteQuestWithCancel();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[BetrayQuest] Failed to cancel quest: {ex.Message}");
            }

            // Trust -15
            var authority = WorldEventStore.GetAuthorityNpc(evt);
            if (authority != null)
                ChangeRelationAction.ApplyPlayerRelation(authority, -15, false, true);

            // 若玩家是 Initiator（栽赃了别人） → 自曝 → 嫌疑转回玩家
            if (evt.InitiatorIsPlayer && evt.SuspectHeroId != Hero.MainHero?.StringId)
            {
                evt.SuspectHeroId = Hero.MainHero?.StringId;
                WorldEventStore.TransitionStage(evt, EventStage.Confrontation);
                DebugLogger.Log($"[Accountability] Player betrayed + confessed framing → suspect reverts to player");
            }

            DebugLogger.Log($"[Accountability] Player betrayed bounty quest for {evt.EventId}");
        }
    }

    #endregion

    #region InnocenceProofIntent

    public class InnocenceProofIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Chat; // 不通过主菜单显示,由DialogueInjector调用
        public override string DisplayName => "【自证清白】不是我干的！查清楚就知道。";
        public override NegotiationGoalType? Goal => null;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.ActiveEvent == null) return Eligibility.Hide();
            var evt = ctx.ActiveEvent;
            // 被冤枉: SuspectHeroId=Player 但 InitiatorId≠Player
            if (evt.SuspectHeroId != Hero.MainHero.StringId) return Eligibility.Hide();
            if (evt.InitiatorId == Hero.MainHero.StringId) return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt == null) return;
            // 系统验证：InitiatorId ≠ Player → 自动洗清
            evt.SuspectHeroId = evt.InitiatorId; // 指向真凶
            if (ctx.Hero != null)
                ChangeRelationAction.ApplyPlayerRelation(ctx.Hero, 5, false, true);
            DebugLogger.Log($"[Accountability] Innocence proof: player cleared for {evt.EventId}");
        }
    }

    #endregion

    #region SettleIntent

    public class SettleIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Chat; // 不通过主菜单显示,由DialogueInjector调用
        public override string DisplayName => "【和解劝说】这事可以商量……";
        public override NegotiationGoalType? Goal => NegotiationGoalType.ResolveConflict_Explain;
        public override NegotiationTactic Tactic => NegotiationTactic.Flatter;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.ActiveEvent == null) return Eligibility.Hide();
            if (ctx.ActiveEvent.Stage != EventStage.Confrontation) return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt == null) return;
            if (ctx.Hero != null)
                ChangeRelationAction.ApplyPlayerRelation(ctx.Hero, -15, false, true);
            WorldEventStore.TransitionStage(evt, EventStage.Resolved);
            evt.ResolvedBy = "settled";
            DebugLogger.Log($"[Accountability] Settlement reached for {evt.EventId}");
        }

        public override void OnFail(IntentContext ctx)
        {
            base.OnFail(ctx);
        }
    }

    #endregion

    #region AcceptBountyQuestIntent

    public class AcceptBountyQuestIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Chat; // 不通过主菜单显示,由DialogueInjector调用
        public override string DisplayName => "【接悬赏】我接这个悬赏！";
        public override NegotiationGoalType? Goal => null;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.ActiveEvent == null) return Eligibility.Hide();
            var evt = ctx.ActiveEvent;
            if (evt.Stage != EventStage.Active) return Eligibility.Hide();
            if (evt.SuspectIsPlayer) return Eligibility.Hide();
            if (evt.PlayerTookBountyQuest) return Eligibility.Grey("你已经接了悬赏");
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt == null) return;
            evt.PlayerTookBountyQuest = true;
            DebugLogger.Log($"[Accountability] Player accepted bounty quest for {evt.EventId}");
        }
    }

    #endregion

    #region LeadRetaliationIntent

    public class LeadRetaliationIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Chat; // 不通过主菜单显示,由DialogueInjector调用
        public override string DisplayName => "【带队报复】我带人去！";
        public override NegotiationGoalType? Goal => null;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.ActiveEvent == null) return Eligibility.Hide();
            var evt = ctx.ActiveEvent;
            if (evt.Stage != EventStage.Confrontation) return Eligibility.Hide();
            if (evt.SuspectIsPlayer) return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt == null) return;
            InvestigationEngine.SpawnRetaliationParty(evt);
            DebugLogger.Log($"[Accountability] Player leading retaliation for {evt.EventId}");
        }
    }

    #endregion

    #region LureArrestIntent

    public class LureArrestIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Chat;
        public override string DisplayName => "【诱捕】跟我走一趟，村长找你有事";
        public override NegotiationGoalType? Goal => NegotiationGoalType.ResolveConflict_Explain;
        public override NegotiationTactic Tactic => NegotiationTactic.Flatter;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.ActiveEvent == null) return Eligibility.Hide();
            if (ctx.ActiveEvent.SuspectIsPlayer) return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt == null) return;
            // NPC 信了 → 进玩家俘虏栏（无物理出手）
            var suspect = Hero.FindFirst(h => h.StringId == evt.SuspectHeroId);
            if (suspect != null && suspect.PartyBelongedTo != null)
            {
                try
                {
                    // ⚠️ TakePrisonerAction.Apply(PartyBase, Hero) 签名待验证
                    TaleWorlds.CampaignSystem.Actions.TakePrisonerAction.Apply(
                        MobileParty.MainParty.Party, suspect);
                    DebugLogger.Log($"[Accountability] LureArrest succeeded: {evt.SuspectHeroId}");
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[Accountability] LureArrest TakePrisonerAction failed: {ex.Message} — suspect not captured, quest continues");
                }
            }
        }
    }

    #endregion

    #region ArrestIntent

    /// <summary>
    /// 直接抓捕——公开动手，不对话。
    /// 靠近嫌犯后触发战斗，活捉或击杀嫌犯。
    /// </summary>
    public class ArrestIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Chat;
        public override string DisplayName => "【抓捕】束手就擒！";
        public override NegotiationGoalType? Goal => null;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.ActiveEvent == null) return Eligibility.Hide();
            var evt = ctx.ActiveEvent;
            if (evt.SuspectIsPlayer) return Eligibility.Hide();
            if (evt.SuspectHeroId == null) return Eligibility.Hide();
            if (evt.Stage != EventStage.Active && evt.Stage != EventStage.Confrontation)
                return Eligibility.Hide();
            // 嫌犯必须在附近（同一定居点或有可见 party）
            var suspect = Hero.FindFirst(h => h.StringId == evt.SuspectHeroId);
            if (suspect == null) return Eligibility.Hide();
            if (suspect.CurrentSettlement?.StringId != evt.TargetSettlementId
                && suspect.PartyBelongedTo?.CurrentSettlement?.StringId != evt.TargetSettlementId)
                return Eligibility.Grey($"{suspect.Name} 不在这里（无法直接抓捕）");
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt == null) return;

            var suspect = Hero.FindFirst(h => h.StringId == evt.SuspectHeroId);
            if (suspect == null) return;

            if (suspect.PartyBelongedTo != null && suspect.PartyBelongedTo != MobileParty.MainParty)
            {
                // 嫌犯有 party → 发起战斗
                TaleWorlds.CampaignSystem.Actions.SetPartyAiAction.GetActionForEngagingParty(
                    MobileParty.MainParty, suspect.PartyBelongedTo);
                DebugLogger.Log($"[Arrest] Engaging suspect party: {suspect.Name}");
            }
            else if (suspect.CurrentSettlement != null && suspect.CurrentSettlement == Settlement.CurrentSettlement)
            {
                // 同定居点 → 直接俘虏
                try
                {
                    // ⚠️ TakePrisonerAction.Apply(PartyBase, Hero) 签名待验证
                    TaleWorlds.CampaignSystem.Actions.TakePrisonerAction.Apply(
                        Settlement.CurrentSettlement.Party, suspect);
                    WorldEventStore.OnSuspectDelivered(evt);
                    DebugLogger.Log($"[Arrest] Arrested {suspect.Name} in settlement");
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[Arrest] TakePrisonerAction failed: {ex.Message} — engaging party instead");
                    // 降级：发起战斗
                    if (suspect.PartyBelongedTo != null)
                        TaleWorlds.CampaignSystem.Actions.SetPartyAiAction.GetActionForEngagingParty(
                            MobileParty.MainParty, suspect.PartyBelongedTo);
                }
            }

            // 标记玩家已接追捕
            evt.PlayerTookBountyQuest = true;
        }
    }

    #endregion
}
