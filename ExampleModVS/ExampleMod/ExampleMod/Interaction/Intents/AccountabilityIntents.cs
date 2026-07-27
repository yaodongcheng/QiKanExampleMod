using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
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

    /// <summary>追责 Intent 的共享工具方法</summary>
    internal static class AccountabilityHelper
    {
        /// <summary>从 Agent 获取当前 Misconduct WorldEvent（用于 L3 质问后同步阶段）</summary>
        public static WorldEvent GetMisconductEvent(Agent agent)
        {
            if (agent == null) return null;
            var pending = AgentAIController.Instance?.PendingWorldEvent;
            if (pending == null) return null;
            // 已在 WorldEventStore 中（续档）→ 取持久化版本；否则取 Pending
            return WorldEventStore.Find(pending.EventId) ?? pending;
        }

        /// <summary>解析 Misconduct WorldEvent 并推进阶段</summary>
        public static void ResolveMisconduct(Agent agent, string resolvedBy)
        {
            var evt = GetMisconductEvent(agent);
            if (evt == null) return;
            evt.ResolvedBy = resolvedBy;
            WorldEventStore.TransitionStage(evt, EventStage.Resolved);
        }
    }

    #region InteractionOptionType Extension

    // AccountabilityOptionType 已删除 — 追责 Intent 现在使用 InteractionOptionType 枚举新增值。
    // 这些值在 InteractionOptionManager.cs 中定义：PayRestitution, CharmDefense, FrameSuspect, Threat,
    // Investigate, Confess, SilenceWitness, LeadRetaliation, WorkOffDebt,
    // BetrayQuest, InnocenceProof, Settle, AcceptBountyQuest, LureArrest, Arrest

    #endregion

    #region PayRestitutionIntent

    /// <summary>
    /// 赔钱消灾 — 合并原 PayRestitutionIntent 和 PayOnTheSpotIntent。
    /// Mission 场景内当场被抓 → ComputeOnSpotCost（2倍私了价）；正式对话协商 → ComputeRestitutionCost（标准赔偿）。
    /// </summary>
    public class PayRestitutionIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.PayRestitution;
        public override string DisplayName => "【赔钱消灾】我愿意赔偿损失";

        // 动态 Tactic/Goal/OfferValue —— Evaluate 中按 ActionParam 设置，
        // ExecuteIntentAction 保证调用顺序：Evaluate → Goal/Tactic/GetOfferValue → SimpleCompute
        private NegotiationGoalType? _goal = null;
        private NegotiationTactic _tactic = NegotiationTactic.Flatter;
        private float _offerValue = 0f;

        public override NegotiationGoalType? Goal => _goal;
        public override NegotiationTactic Tactic => _tactic;
        public override float GetOfferValue(IntentContext ctx) => _offerValue;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            // Alert 场景：NPC 找上门质问，无犯罪事件也允许赔钱消灾
            if (ctx.ActiveEvent == null)
            {
                if (ctx.IsInMission)
                {
                    switch (ctx.ActionParam)
                    {
                        case "alert_fine":
                            _goal = null;  // 认罚不掷骰——NPC 已经决定了，玩家只是接受
                            _tactic = NegotiationTactic.Flatter;
                            _offerValue = 0f;
                            return Eligibility.Show();

                        case "bribe":
                            _goal = NegotiationGoalType.ResolveConflict_Apology;
                            _tactic = NegotiationTactic.Bribe;
                            _offerValue = 0.3f;
                            var misEvtBribe = AccountabilityHelper.GetMisconductEvent(ctx.Agent);
                            int bribeCost = CrimePenaltyCalculator.ComputePenalty(misEvtBribe);
                            if (Hero.MainHero.Gold < bribeCost)
                                return Eligibility.Grey($"钱不够（需要 {bribeCost} 第纳尔）");
                            return Eligibility.Show();
                    }
                }
                return Eligibility.Hide();
            }
            if (ctx.ActiveEvent.InitiatorId != Hero.MainHero.StringId) return Eligibility.Hide();

            // 正式对话：赔钱在 Active/Confrontation 阶段始终可用；Emerging 阶段只有自首后（SuspectHeroId=玩家）才可用
            bool stageOk = ctx.ActiveEvent.Stage == EventStage.Active
                        || ctx.ActiveEvent.Stage == EventStage.Confrontation
                        || (ctx.ActiveEvent.Stage == EventStage.Emerging && ctx.ActiveEvent.SuspectHeroId == Hero.MainHero.StringId);
            if (!stageOk) return Eligibility.Hide();

            int cost = ctx.IsInMission
                ? CrimePenaltyCalculator.ComputeCost(ctx.ActiveEvent, CostType.OnSpot)
                : CrimePenaltyCalculator.ComputeCost(ctx.ActiveEvent, CostType.Restitution);
            if (Hero.MainHero.Gold < cost)
                return Eligibility.Grey($"钱不够（需要 {cost} 第纳尔）");
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            // Alert 场景：NPC 质问中玩家选认罚 → 当场扣钱，清警戒，释放质问锁
            if (ctx.ActionParam == "alert_fine")
            {
                var misEvt = AccountabilityHelper.GetMisconductEvent(ctx.Agent);
                int fine = CrimePenaltyCalculator.ComputePenalty(misEvt);
                AgentControlHelper.TransferGold(Hero.MainHero, null, fine);
                var npc = ctx.Speaker ?? Campaign.Current?.ConversationManager?.OneToOneConversationHero;
                if (npc is Hero n)
                    ChangeRelationAction.ApplyPlayerRelation(n, -3, false, true);

                // 结案 Misconduct WorldEvent
                AccountabilityHelper.ResolveMisconduct(ctx.Agent, "payment");

                var brain = AgentAIController.GetBrainForAgent(ctx.Agent);
                brain?.ClearAllAlerts();
                // ConfrontingBrain 不在这里释放 — 由 EndConversation 统一解锁
                return;
            }

            // 标准事件赔偿路径
            var evt = ctx.ActiveEvent;
            if (evt == null) return;

            bool isOnSpot = ctx.IsInMission;
            int cost = isOnSpot ? CrimePenaltyCalculator.ComputeCost(evt, CostType.OnSpot) : CrimePenaltyCalculator.ComputeCost(evt, CostType.Restitution);
            if (ctx.ActionParam == "haggle")
                cost = (int)(cost * 0.5f);
            var authority = WorldEventStore.GetAuthorityNpc(evt);
            if (authority != null)
                AgentControlHelper.TransferGold(Hero.MainHero, authority, cost);
            else
                AgentControlHelper.TransferGold(Hero.MainHero, null, cost);

            WorldEventStore.OnPlayerPaidRestitution(evt);
            TheftLedger.MarkCleared(evt.TargetSettlementId);

            // 解决了 → 清除 WalkAway Inquiry
            WalkAwayIntent.PendingInquiryTitle = null;
            WalkAwayIntent.PendingInquiryBody = null;

            string sceneTag = isOnSpot ? "[OnSpot]" : "";
            DebugLogger.Log($"[Accountability]{sceneTag} Player paid restitution {cost} gold for {evt.EventId}");
            CommissionQuest.AddNarrativeLogForEvent(evt, $"赔了{cost}第纳尔。{authority?.Name?.ToString() ?? "村长"}收了钱，这事总算翻篇了。");
        }

        /// <summary>贿赂检定通过：扣钱、清警戒、结案。</summary>
        public override void OnSuccess(IntentContext ctx)
        {
            if (ctx.ActionParam == "bribe")
            {
                var misEvt = AccountabilityHelper.GetMisconductEvent(ctx.Agent);
                int bribe = CrimePenaltyCalculator.ComputePenalty(misEvt);
                AgentControlHelper.TransferGold(Hero.MainHero, null, bribe);
                var brain = AgentAIController.GetBrainForAgent(ctx.Agent);
                brain?.ClearAllAlerts();
                // ConfrontingBrain 不在这里释放 — 由 EndConversation 统一解锁
                AccountabilityHelper.ResolveMisconduct(ctx.Agent, "bribe");
                DebugLogger.Log($"[Accountability] Bribe accepted: paid {bribe} gold");
                return;
            }
            base.OnSuccess(ctx);
        }

        /// <summary>贿赂检定失败：不掉钱，基类处理掉好感和冷却。</summary>
        public override void OnFail(IntentContext ctx)
        {
            if (ctx.ActionParam == "bribe")
            {
                DebugLogger.Log($"[Accountability] Bribe rejected by {ctx.Speaker?.Name}");
            }
            base.OnFail(ctx);
        }
    }

    #endregion

    #region CharmDefenseIntent

    public class CharmDefenseIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.CharmDefense;
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

            // 魅力辩护成功 → 清除 WalkAway Inquiry
            WalkAwayIntent.PendingInquiryTitle = null;
            WalkAwayIntent.PendingInquiryBody = null;

            DebugLogger.Log($"[Accountability] Charm defense succeeded for {evt.EventId} — suspect downgraded");
            var giverName = WorldEventStore.GetAuthorityNpc(evt)?.Name?.ToString() ?? "村长";
            CommissionQuest.AddNarrativeLogForEvent(evt, $"我设法说服了{giverName}，暂时洗脱了嫌疑。但他看我的眼神还是不太对……");
        }

        public override void OnFail(IntentContext ctx)
        {
            base.OnFail(ctx);
            var evt = ctx.ActiveEvent;
            if (evt == null) return;
            ChangeRelationAction.ApplyPlayerRelation(ctx.Speaker, -10, false, true);
            WorldEventStore.TransitionStage(evt, EventStage.Confrontation);
            DebugLogger.Log($"[Accountability] Charm defense failed for {evt.EventId} — → Confrontation");
            var giverName = WorldEventStore.GetAuthorityNpc(evt)?.Name?.ToString() ?? "村长";
            CommissionQuest.AddNarrativeLogForEvent(evt, $"辩解没用。{giverName}根本不买账，事态反而更严重了。");
        }
    }

    #endregion

    #region FrameSuspectIntent

    public class FrameSuspectIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.FrameSuspect;
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
            var target = ctx.ActionParam;
            if (!string.IsNullOrEmpty(target) && target != "bandit")
            {
                if (TheftLedger.HasRecordFor(target))
                    return 0.6f;
            }
            return 0.2f;
        }

        public override void OnSuccess(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt == null) return;

            string targetId = ctx.ActionParam ?? "bandit";
            evt.SuspectHeroId = targetId == "bandit" ? GetBanditLeaderId() : targetId;
            evt.InvestigationProgress = 1.0f;
            WorldEventStore.TransitionStage(evt, EventStage.Active);
            DebugLogger.Log($"[Accountability] Frame suspicion: {targetId} blamed for {evt.EventId}");

            // Intent 驱动：通知调查 Quest "嫌犯已锁定"
            var suspectHero = Hero.FindFirst(h => h.StringId == evt.SuspectHeroId);
            string suspectName = suspectHero?.Name?.ToString() ?? "某人";
            var giverName = WorldEventStore.GetAuthorityNpc(evt)?.Name?.ToString() ?? "村长";
            CommissionQuest.AddNarrativeLogForEvent(evt, $"我成功把嫌疑推给了{suspectName}。{giverName}信了。");
            foreach (var q in Campaign.Current.QuestManager.Quests)
            {
                if (q is CommissionQuest cq
                    && cq.Data?.WorldEventId == evt.EventId
                    && cq.Data?.Category == CommissionCategory.Investigation)
                {
                    cq.NotifySuspectIdentified(suspectName);
                    break;
                }
            }
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
                var giverName = WorldEventStore.GetAuthorityNpc(evt)?.Name?.ToString() ?? "村长";
                CommissionQuest.AddNarrativeLogForEvent(evt, $"栽赃再次被识破——{giverName}已经不再相信我。嫌疑转回了我的头上。");
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
        public override InteractionOptionType Type => InteractionOptionType.Threat;
        public override string DisplayName => "【威胁】你再说一遍？（手按在剑柄上）";
        public override NegotiationGoalType? Goal => NegotiationGoalType.ResolveConflict_Intimidate;
        public override NegotiationTactic Tactic => NegotiationTactic.Flatter;

        /// <summary>威胁失败后延迟进入战斗的 Agent（对话关闭后由 Patch 消费）</summary>
        internal static Agent PendingCombatAgent;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            // Alert 场景：NPC 主动找上门质问（蹲下/偷窃/攻击），无犯罪事件或仅 Misconduct 也允许威胁
            if (ctx.ActiveEvent == null || ctx.ActiveEvent.Type == EventType.Misconduct)
            {
                if (ctx.IsInMission)
                    return Eligibility.Show();
                return Eligibility.Hide();
            }
            if (ctx.ActiveEvent.SuspectHeroId != Hero.MainHero.StringId) return Eligibility.Hide();
            if (Hero.MainHero.GetSkillValue(DefaultSkills.Roguery) < 50)
                return Eligibility.Grey($"Roguery 技能不足（需要 50，当前 {Hero.MainHero.GetSkillValue(DefaultSkills.Roguery):0}）");
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            var evt = ctx.ActiveEvent;
            if (evt != null)
            {
                WorldEventStore.OnIntimidated(evt);
                InfamySystem.AddInfamy(1);
                DebugLogger.Log($"[Accountability] Threat succeeded for {evt.EventId}");
                var giverName = WorldEventStore.GetAuthorityNpc(evt)?.Name?.ToString() ?? "村长";
                CommissionQuest.AddNarrativeLogForEvent(evt, $"我放了狠话。{giverName}退缩了，不敢再追究。但我在这地方的名声怕是完了。");
            }
            else
            {
                // Alert 场景：玩家威胁成功 → NPC 退缩，关系 -2（震慑留下芥蒂）
                var npc = ctx.Speaker ?? Campaign.Current?.ConversationManager?.OneToOneConversationHero;
                if (npc is Hero n)
                    ChangeRelationAction.ApplyPlayerRelation(n, -2, false, true);
                DebugLogger.Log($"[Accountability] Threat succeeded (Alert context, no event) — relation -2");
            }
        }

        public override void OnFail(IntentContext ctx)
        {
            base.OnFail(ctx);
            var evt = ctx.ActiveEvent;
            if (evt != null)
            {
                WorldEventStore.TransitionStage(evt, EventStage.Confrontation);
                DebugLogger.Log($"[Accountability] Threat failed — → Confrontation");
                var giverName = WorldEventStore.GetAuthorityNpc(evt)?.Name?.ToString() ?? "村长";
                CommissionQuest.AddNarrativeLogForEvent(evt, $"威胁没吓住{giverName}——他叫人了。事情彻底闹大了。");
            }
            else
            {
                // Alert 场景：威胁失败 → 设 PendingCombatAgent，推进 WorldEvent 到 Confrontation
                PendingCombatAgent = ctx.Agent;
                var npc = ctx.Speaker ?? Campaign.Current?.ConversationManager?.OneToOneConversationHero;
                if (npc is Hero n)
                    ChangeRelationAction.ApplyPlayerRelation(n, -5, false, true);

                var misconduct = AccountabilityHelper.GetMisconductEvent(ctx.Agent);
                if (misconduct != null)
                    WorldEventStore.TransitionStage(misconduct, EventStage.Confrontation);

                DebugLogger.Log($"[Accountability] Threat failed (Alert) — PendingCombatAgent={ctx.Agent?.Name}, relation -5, WorldEvent→Confrontation");
            }
        }
    }

    #endregion

    #region InvestigateIntent

    public class InvestigateIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Investigate;
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

            // Quest 只能从 Issue 生——走统一入口
            var authority = WorldEventStore.GetAuthorityNpc(evt);
            if (authority != null)
            {
                if (authority.Issue is CommissionHubIssue issue)
                {
                    var quest = issue.AcceptQuest();
                    if (quest == null)
                        DebugLogger.Log($"[Accountability] AcceptQuest returned null for {evt.EventId} (no WorldEvent data on {authority.Name}?)");
                }
                else
                    DebugLogger.Log($"[Accountability] No CommissionHubIssue on {authority.Name} for {evt.EventId}");
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
        public override InteractionOptionType Type => InteractionOptionType.Confess;
        public override string DisplayName => "【自首】是我干的";
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
            var giverName = WorldEventStore.GetAuthorityNpc(evt)?.Name?.ToString() ?? "村长";
            // 自首叙事如实还原罪行（袭击+失窃），不只说"拿了东西"
            var facts = evt.BuildDiscoveryFacts();
            CommissionQuest.AddNarrativeLogForEvent(evt, $"我向{giverName}坦白了——{facts}，都是我干的。");
        }
    }

    #endregion

    #region WalkAwayIntent

    /// <summary>
    /// 通用"离开"——合并原 WalkAwayIntent 和 FleeFromConfrontationIntent。
    /// OnInstant 按四层判定逐层收窄，每层不满足即"纯自然离开"返回：
    /// ① 有无活跃犯罪事件 —— 无 → 仅 Alert 场景（NPC 警戒质问找上门）需要处理
    /// ② 有无定居点 —— 无（大地图偶遇）→ 后果无法落地，暂放行（TODO）
    /// ③ 玩家是否嫌犯 —— 否（路人闲聊/受托查案者）→ 无任何后果
    /// ④ 事件阶段 × 是否在 Mission —— 仅 Active + Mission 内 + 嫌犯=玩家 才掷武力逃跑检定；
    ///    其余对话内离开走 NPC 警告（延迟 Inquiry）+ 关系惩罚。
    /// </summary>
    public class WalkAwayIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Leave;
        public override string DisplayName => "【离开】";
        public override NegotiationGoalType? Goal => null;  // 即时——不检定（Mission 逃脱在 OnInstant 内部掷骰）

        /// <summary>延迟到对话结束后弹出的 Inquiry 数据</summary>
        internal static string PendingInquiryTitle;
        internal static string PendingInquiryBody;

        /// <summary>Alert 场景玩家转身就走 → 对话关闭后由 Patch 消费，触发呼救围堵 + 重新质问</summary>
        internal static Agent PendingEscalationAgent;

        /// <summary>🆕 围堵升级的显式质问上下文（嫌犯转身就走 → Stop + SuspectFlee）。null = 按 NPC 自身警戒明细推导（Alert 路径）。</summary>
        internal static ConfrontationType? PendingEscalationDetail;
        internal static PlayerActionType? PendingEscalationAction;

        /// <summary>🆕 true = 只广播围观，不追上重新质问（武力逃脱成功——人跑了，村民只能目送）。</summary>
        internal static bool PendingEscalationGatherOnly;

        /// <summary>统一设置围堵升级标记（四字段一体，防止上一轮的残留泄漏到下一轮）。</summary>
        private static void SetPendingEscalation(Agent agent, ConfrontationType? detail, PlayerActionType? action, bool gatherOnly = false)
        {
            PendingEscalationAgent = agent;
            PendingEscalationDetail = detail;
            PendingEscalationAction = action;
            PendingEscalationGatherOnly = gatherOnly;
        }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            // 始终可见——任何对话都可以选择离开
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            // ══ 第一层：是否有活跃犯罪事件 ══
            // ctx.ActiveEvent = 对话上下文已锁定的事件；为 null 时按玩家所在定居点查一次兜底
            // （StoryDialogVM 互动菜单路径构建 ctx 时不带 worldEvent，必须靠兜底）。
            var settlement = Settlement.CurrentSettlement ?? Hero.MainHero?.CurrentSettlement;
            var evt = ctx.ActiveEvent ?? (settlement != null ? WorldEventStore.FindActive(settlement.StringId) : null);

            if (evt == null)
            {
                // 无活跃事件 —— 唯一需要处理的是 Alert 场景：NPC 因警戒质问找上门，玩家直接走人
                // 警戒质问比较特殊 玩家可能只是做出了不合适的行为，或者在做坏事过程中，并没有真的执行完成做坏事的结果
                // → 关系小降 + 设 EscalationAgent（对话关闭后由 Patch 消费：呼救围堵 + 重新质问）
                if (ctx.IsInMission)
                {
                    SetPendingEscalation(ctx.Agent, null, null);
                    var npc = ctx.Speaker ?? Campaign.Current?.ConversationManager?.OneToOneConversationHero;
                    if (npc is Hero n)
                        ChangeRelationAction.ApplyPlayerRelation(n, -5, false, true);

                    var misconduct = AccountabilityHelper.GetMisconductEvent(ctx.Agent);
                    if (misconduct != null && misconduct.Stage < EventStage.Active)
                        WorldEventStore.TransitionStage(misconduct, EventStage.Active);

                    DebugLogger.Log($"[WalkAway] Alert context — PendingEscalationAgent={ctx.Agent?.Name}, relation -5, WorldEvent→Active");
                }
                return;  // 其余情况（大地图/菜单闲聊）→ 纯自然离开
            }

            // ══ 第二层：是否有定居点 ══
            // 有事件但玩家不在定居点（大地图偶遇对话）——目前后果都依赖定居点场景，无法落地。
            // TODO: 未来支持"大地图上 NPC 找上门追责"时在这里接。
            if (settlement == null) return;

            // ══ 第三层：玩家是否是嫌犯 ══
            // 不是嫌犯（路人闲聊 / 受托查案的调查者）→ 纯自然离开，任何阶段都无后果。
            if (!evt.SuspectIsPlayer) return;

            // ══ 第四层：玩家是嫌犯 → 按事件阶段决定后果；最内层按是否在 Mission 区分处理方式 ══
            var authority = WorldEventStore.GetAuthorityNpc(evt);
            string npcName = authority?.Name?.ToString() ?? "村长";
            string villageName = authority?.CurrentSettlement?.Name?.ToString()
                ?? settlement.Name?.ToString() ?? "村子";

            switch (evt.Stage)
            {
                case EventStage.Emerging:
                    // 已被怀疑（自首后）转身就走 → 村民确信是你干的，事件升级 Active
                    WorldEventStore.TransitionStage(evt, EventStage.Active);
                    if (ctx.IsInMission && ctx.Agent != null)
                    {
                        // 🆕 村内当场走人 → 物理围堵升级：村民围观 + NPC 追上重新质问（无"我走了"退路）
                        SetPendingEscalation(ctx.Agent, ConfrontationType.Stop, PlayerActionType.SuspectFlee);
                        PendingInquiryTitle = "“站住！”";
                        PendingInquiryBody =
                            $"你转身离开，身后传来{npcName}愤怒的吼声——\n\n" +
                            $"\"你以为认了就完了？！来人——拦住他！\"\n\n" +
                            $"{villageName}的村民们闻声围拢过来……";
                    }
                    else
                    {
                        PendingInquiryTitle = "“站住！”";
                        PendingInquiryBody =
                            $"你转身离开，身后传来{npcName}愤怒的吼声——\n\n" +
                            $"\"你以为认了就完了？！这事没完！\"\n\n" +
                            $"{villageName}的村民们纷纷侧目，你在此地的名声已经坏了。" +
                            $"下次再见到{npcName}，可就不是商量那么简单了。";
                    }
                    NotifyInvestigationQuest(evt);
                    DebugLogger.Log($"[Accountability] WalkAway (Emerging suspect): {evt.EventId} → Active");
                    CommissionQuest.AddNarrativeLogForEvent(evt, $"我转身走了。身后传来{npcName}的怒吼——这事没完。");
                    break;

                case EventStage.Active:
                    if (ctx.IsInMission)
                    {
                        // ── 第五层：Mission 内 —— 玩家正被围堵缉拿，"离开" = 武力推开逃跑 → Intimidate 检定
                        // 成功：挣脱但身份彻底暴露；失败：被拦下，关系大降 + 事件升级 Confrontation
                        var roll = SingleRollResolver.SimpleCompute(ctx, NegotiationTactic.Flatter, 0f);
                        bool success = SingleRollResolver.Roll(roll.Chance);
                        DebugLogger.Log($"[WalkAway] Mission flee: chance={roll.Chance:P0} success={success}");
                        if (success)
                            OnFleeSuccess(ctx, evt);
                        else
                            OnFleeFail(ctx, evt);
                    }
                    else
                    {
                        // ── 第五层：对话/菜单内 —— 转身就走，NPC 放话 + 关系惩罚
                        PendingInquiryTitle = "“站住！”";
                        PendingInquiryBody =
                            $"你转身离开，身后传来{npcName}的怒吼——\n\n" +
                            $"\"跑了？！好，{villageName}的人不会放过你！\"\n\n" +
                            $"下次见面，就不会再跟你废话了。";
                        if (authority != null)
                            ChangeRelationAction.ApplyPlayerRelation(authority, -10, false, true);
                        DebugLogger.Log($"[Accountability] WalkAway (Active suspect): {evt.EventId} — rep -10");
                        CommissionQuest.AddNarrativeLogForEvent(evt, $"我转身走了。{npcName}气得发抖——下次见面不会跟我客气了。");
                    }
                    break;

                case EventStage.Confrontation:
                    // 不死不休阶段 —— 无论是否在 Mission，都是放狠话 + 关系重罚 + 报复部队
                    PendingInquiryTitle = "“你跑不掉的！”";
                    PendingInquiryBody =
                        $"你转身就跑。身后{npcName}的吼声回荡——\n\n" +
                        $"\"躲得过初一躲不过十五！{villageName}跟你不死不休！\"\n\n" +
                        $"你在此地已是死敌。小心——他们雇的人随时可能出现。";
                    if (authority != null)
                        ChangeRelationAction.ApplyPlayerRelation(authority, -20, false, true);
                    if (!evt.RetaliationSpawned)
                        InvestigationEngine.SpawnRetaliationParty(evt);
                    DebugLogger.Log($"[Accountability] WalkAway (Confrontation): {evt.EventId} — retaliation + rep -20");
                    CommissionQuest.AddNarrativeLogForEvent(evt, $"我跑了。{npcName}追了出来——{villageName}跟我不死不休。");
                    break;
            }
        }

        /// <summary>Mission 内武力逃脱成功：挣脱围堵，但身份彻底暴露（嫌犯锁定 + 调查进度拉满）。已在 Active → TransitionStage 幂等早退。</summary>
        private void OnFleeSuccess(IntentContext ctx, WorldEvent evt)
        {
            evt.SuspectHeroId = Hero.MainHero.StringId;
            evt.InvestigationProgress = 1.0f;
            WorldEventStore.TransitionStage(evt, EventStage.Active);
            // 🆕 挣脱跑了 → 村民围观目送（只广播，不追上——人已经跑了）
            if (ctx.Agent != null)
                SetPendingEscalation(ctx.Agent, null, null, gatherOnly: true);
            DebugLogger.Log($"[Accountability] Player fled confrontation for {evt.EventId}");
        }

        /// <summary>Mission 内武力逃脱失败：被拦下，关系大降 + 事件升级 Confrontation + NPC 追上重新质问（无退路）</summary>
        private void OnFleeFail(IntentContext ctx, WorldEvent evt)
        {
            if (ctx.Speaker != null)
                ChangeRelationAction.ApplyPlayerRelation(ctx.Speaker, -15, false, true);
            WorldEventStore.TransitionStage(evt, EventStage.Confrontation);
            // 🆕 "被拦下"物理化：村民围观 + NPC 追上重新质问（拔剑/认罚/坐牢，没有"我走了"）
            if (ctx.Agent != null)
                SetPendingEscalation(ctx.Agent, ConfrontationType.Stop, PlayerActionType.SuspectFlee);
        }

        private static void NotifyInvestigationQuest(WorldEvent evt)
        {
            foreach (var q in Campaign.Current.QuestManager.Quests)
            {
                if (q is CommissionQuest cq
                    && cq.Data?.WorldEventId == evt.EventId
                    && cq.Data?.Category == CommissionCategory.Investigation)
                {
                    cq.NotifySuspectIdentified(Hero.MainHero.Name?.ToString() ?? "你");
                    break;
                }
            }
        }
    }

    #endregion

    #region SilenceWitnessIntent

    public class SilenceWitnessIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.SilenceWitness;
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

    #region WorkOffDebtIntent

    public class WorkOffDebtIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.WorkOffDebt;
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

    #region FightVillagersIntent

    public class FightVillagersIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Assault;
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

            // 🆕 场景内开战：玩家在 Mission 中 → 在场村民立即敌对，而不是只等大地图复仇队。
            // 战斗一旦打响，后续 BecomeAlarmed 会被 IsPlayerInCombat / IsCurrentOrPending<FightEnemyAction>
            // 守卫拦截（AgentBrain.ReceiveEvent），L3 强制质问对话循环自然中断。
            if (ctx.IsInMission && Agent.Main != null)
            {
                // ① 对话对象 → 两阶段延迟战斗（复用 ThreatIntent.PendingCombatAgent 轮子）：
                //    对话关闭后 ConversationEntryPatch 发 DeferredCombat，
                //    避免对话进行中 ClearAllActions 把对话本身打断。
                if (ctx.Agent != null && ctx.Agent != Agent.Main && ctx.Agent.IsActive())
                    ThreatIntent.PendingCombatAgent = ctx.Agent;

                // ② 围观村民 → 立即广播 order_attack（排除对话对象）。
                //    空手村民由 CombatManager.StartFight → TryGiveAnyMeleeWeapon 现场发武器——"抄起家伙"。
                var exclude = ctx.Agent != null
                    ? new System.Collections.Generic.HashSet<Agent> { ctx.Agent }
                    : null;
                AgentAIController.Instance?.BroadcastEventInRange(
                    Agent.Main.Position, 30f, "order_attack", exclude, false, Agent.Main);
                DebugLogger.Log($"[Accountability] FightVillagers in-mission: order_attack broadcast, deferred={ctx.Agent?.Name ?? "none"}");
            }

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
        public override InteractionOptionType Type => InteractionOptionType.BetrayQuest;
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
        public override InteractionOptionType Type => InteractionOptionType.InnocenceProof;
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
            if (ctx.Speaker != null)
                ChangeRelationAction.ApplyPlayerRelation(ctx.Speaker, 5, false, true);
            DebugLogger.Log($"[Accountability] Innocence proof: player cleared for {evt.EventId}");
        }
    }

    #endregion

    #region SettleIntent

    public class SettleIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Settle;
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
            if (ctx.Speaker != null)
                ChangeRelationAction.ApplyPlayerRelation(ctx.Speaker, -15, false, true);
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
        public override InteractionOptionType Type => InteractionOptionType.AcceptBountyQuest;
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

            // 1. 完成调查 Quest（如果存在且未完成）
            foreach (var q in Campaign.Current.QuestManager.Quests)
            {
                if (q is CommissionQuest cq
                    && cq.Data?.WorldEventId == evt.EventId
                    && cq.Data?.Category == CommissionCategory.Investigation
                    && !cq.Data.IsObjectivesComplete)
                {
                    cq.CompleteObjectivesFromExternal();
                    DebugLogger.Log($"[Accountability] Investigation quest completed via bounty acceptance: {cq.StringId}");
                    break;
                }
            }

            // 2. Quest 只能从 Issue 生——走统一入口
            var authority = WorldEventStore.GetAuthorityNpc(evt);
            if (authority != null)
            {
                if (authority.Issue is CommissionHubIssue issue)
                {
                    var quest = issue.AcceptQuest();
                    if (quest == null)
                        DebugLogger.Log($"[Accountability] AcceptQuest returned null for {evt.EventId} (no WorldEvent data on {authority.Name}?)");
                }
                else
                    DebugLogger.Log($"[Accountability] No CommissionHubIssue on {authority.Name} for {evt.EventId}");
            }
        }
    }

    #endregion

    #region LeadRetaliationIntent

    public class LeadRetaliationIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.LeadRetaliation;
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
        public override InteractionOptionType Type => InteractionOptionType.LureArrest;
        public override string DisplayName => "【诱捕】跟我走一趟，村长找你有事";
        public override NegotiationGoalType? Goal => NegotiationGoalType.ResolveConflict_Explain;
        public override NegotiationTactic Tactic => NegotiationTactic.Flatter;

        /// <summary>Mission 内诱捕成功后待淡出的 Agent。EndConversation 时消费。</summary>
        public static Agent PendingFadeAgent;

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
                    TaleWorlds.CampaignSystem.Actions.TakePrisonerAction.Apply(
                        MobileParty.MainParty.Party, suspect);
                    DebugLogger.Log($"[Accountability] LureArrest succeeded: {evt.SuspectHeroId}");

                    // 反馈：提示玩家俘虏成功
                    InformationManager.DisplayMessage(
                        new InformationMessage($"{suspect.Name} 被你诱捕，关进了俘虏栏。"));
                    DebugLogger.Log($"[Accountability] LureArrest InformationMessage displayed for {suspect.Name}");

                    // 延迟 FadeOut：Mission 内 Agent 需等对话结束后再消失，避免 NPC 一边说话一边淡出
                    if (ctx.IsInMission && ctx.Agent != null)
                    {
                        PendingFadeAgent = ctx.Agent;
                        DebugLogger.Log($"[Accountability] LureArrest Agent {ctx.Agent.Name}(Idx={ctx.Agent.Index}) deferred for post-dialogue FadeOut");
                    }
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
        public override InteractionOptionType Type => InteractionOptionType.Arrest;
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

    #region SurrenderJailIntent

    /// <summary>
    /// 束手就擒——没钱赔就坐牢。扣钱、扣关系、清警戒、时间快进、传送出村。
    /// 对标 KCD2：被抓住后要么交罚款走人，要么蹲几天地牢。
    /// </summary>
    public class SurrenderJailIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.SurrenderJail;
        public override string DisplayName => "【坐牢】我没钱。要抓就抓吧。";
        public override NegotiationGoalType? Goal => null; // 即时类

        /// <summary>坐牢后延迟踢出村庄的标记（对话关闭后由 ConversationEntryPatch 消费）</summary>
        internal static bool PendingJailExit;

        /// <summary>坐牢的村庄（用于 DailyTick 自动释放）</summary>
        internal static Settlement JailSettlement;
        /// <summary>被俘日期（用于 DailyTick 判断时间）</summary>
        internal static float JailCaptureDay;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            // Alert 场景：NPC 质问中玩家选坐牢
            if (ctx.ActiveEvent == null && ctx.IsInMission && ctx.ActionParam == "surrender_jail")
                return Eligibility.Show();
            return Eligibility.Hide();
        }

        public override void OnInstant(IntentContext ctx)
        {
            var misEvt = AccountabilityHelper.GetMisconductEvent(ctx.Agent);
            int maxConfiscation = CrimePenaltyCalculator.ComputePenalty(misEvt);
            int confiscation = Math.Min(Hero.MainHero.Gold, maxConfiscation);
            if (confiscation > 0)
                AgentControlHelper.TransferGold(Hero.MainHero, null, confiscation);

            var npc = ctx.Speaker ?? Campaign.Current?.ConversationManager?.OneToOneConversationHero;
            if (npc is Hero n)
                ChangeRelationAction.ApplyPlayerRelation(n, -10, false, true);

            // 结案 Misconduct WorldEvent
            AccountabilityHelper.ResolveMisconduct(ctx.Agent, "jail");

            var brain = AgentAIController.GetBrainForAgent(ctx.Agent);
            brain?.ClearAllAlerts();
            // ConfrontingBrain 不在这里释放 — 由 EndConversation 统一解锁

            // 标记延迟踢出村庄（对话关闭后由 Patch 执行 EndMission + 传送）
            PendingJailExit = true;

            DebugLogger.Log($"[Accountability] SurrenderJail: confiscated {confiscation} gold, pending jail exit");
        }
    }

    #endregion

    #region ComplyIntent

    /// <summary>
    /// 服从 — 玩家收武器/停止可疑行为。
    /// Alert 场景 Deter 对话框的"好，我收起来"/"没什么，我这就走"选项。
    /// 收武器、清 NPC 警戒值、释放质问锁。
    /// </summary>
    public class ComplyIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.Comply;
        public override string DisplayName => "【服从】";
        public override NegotiationGoalType? Goal => null; // 即时类

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if(Mission.Current != null)
                return Eligibility.Show();
            return Eligibility.Hide();
        }

        public override void OnInstant(IntentContext ctx)
        {
            // 收武器（双手都收）
            Agent.Main?.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);
            Agent.Main?.TryToSheathWeaponInHand(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.Instant);

            // 清 NPC 警戒值 + 释放质问锁 + 结案 Misconduct WorldEvent
            AccountabilityHelper.ResolveMisconduct(ctx.Agent, "comply");

            var brain = AgentAIController.GetBrainForAgent(ctx.Agent);
            brain?.ClearAllAlerts();
            // ConfrontingBrain 不在这里释放 — 由 EndConversation 统一解锁

            var npc = ctx.Speaker ?? Campaign.Current?.ConversationManager?.OneToOneConversationHero;
            if (npc is Hero n)
                ChangeRelationAction.ApplyPlayerRelation(n, -1, false, true);

            DebugLogger.Log($"[Accountability] Comply: weapon sheathed, alerts cleared");
        }
    }

    #endregion

    #region ContinueChatIntent

    /// <summary>
    /// 对话内导航 Intent："说点别的……" —— 跳回 start turn 继续讨论。
    /// 纯导航，不检定。事件 Resolved/Unsolved 后自动 Hide。
    /// </summary>
    public class ContinueChatIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.ContinueChat;
        public override string DisplayName => "说点别的……";
        public override NegotiationGoalType? Goal => null;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.ActiveEvent == null) return Eligibility.Hide();
            if (ctx.ActiveEvent.Stage == EventStage.Resolved) return Eligibility.Hide();
            if (ctx.ActiveEvent.Stage == EventStage.Unsolved) return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx) { }
    }

    #endregion
}
