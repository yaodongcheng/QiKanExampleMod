using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;

namespace LivingWorldNpcs.Planner.Cooperation
{
    /// <summary>候选人与玩家之间的协作关系。</summary>
    public enum CoopContractMode
    {
        None = 0,
        /// <summary>限当前场景、约定目的与风险的一次协作。</summary>
        Temporary = 1,
        /// <summary>正式同伴（家族／主队成员）。</summary>
        Companion = 2
    }

    /// <summary>
    /// 招募与临时协作的业务合同。候选人、价格、前置全部来自内容 JSON，
    /// C# 不按角色 ID 写专用招募分支（实施计划 §4.3.3）。
    /// </summary>
    public static class CoopRecruitment
    {
        public sealed class Eligibility
        {
            public bool Ok;
            public string Reason = string.Empty;
            public Hero Hero;
            public CoopContractMode Mode;
            public int Price;
            public bool FavorAvailable;
        }

        private const string ModeKey = "contract.mode";
        private const string FavorKey = "contract.favor";

        public static CoopRecruitmentConfig Config
        {
            get
            {
                var director = CoopDirector.Current;
                return director != null && director.Content != null ? director.Content.Recruitment : null;
            }
        }

        public static CoopContractMode GetMode(Hero hero)
        {
            if (hero != null && hero.IsPlayerCompanion && hero.CompanionOf == Clan.PlayerClan)
                return CoopContractMode.Companion;
            var director = CoopDirector.Current;
            if (hero == null || director == null || director.Ledger == null)
            {
                return CoopContractMode.None;
            }
            var value = director.Ledger.GetRelation(hero.StringId, ModeKey);
            CoopContractMode mode;
            // 正式同伴身份以原生家族关系为准；旧账本不能恢复已被解除的权限。
            return !string.IsNullOrEmpty(value) && Enum.TryParse(value, true, out mode)
                && mode == CoopContractMode.Temporary ? mode : CoopContractMode.None;
        }

        public static bool HasFavor(Hero hero)
        {
            var director = CoopDirector.Current;
            if (hero == null || director == null || director.Ledger == null)
            {
                return false;
            }
            return string.IsNullOrEmpty(director.Ledger.GetRelation(hero.StringId, FavorKey));
        }

        /// <summary>资格复核：任何一项不满足都直接报告原因，不消耗随机输入。</summary>
        public static Eligibility Evaluate(string candidateId, bool diagnostic = false)
        {
            var result = new Eligibility { Mode = CoopContractMode.None };
            var config = Config;
            if (config == null)
            {
                result.Reason = "recruitment_not_configured";
                return result;
            }
            if (!string.IsNullOrEmpty(config.Candidate) && !string.Equals(config.Candidate, candidateId, StringComparison.Ordinal))
            {
                result.Reason = "not_a_candidate";
                return result;
            }
            var host = StoryProbeMissionBehavior.Current;
            if (host == null)
            {
                result.Reason = "not_in_story_scene";
                return result;
            }
            var hero = host.GetHero(candidateId);
            if (hero == null)
            {
                result.Reason = "hero_missing";
                return result;
            }
            result.Hero = hero;
            if (hero.CompanionOf != null && hero.CompanionOf != Clan.PlayerClan)
            { result.Reason = "belongs_to_other_clan"; return result; }
            if (!hero.IsAlive)
            {
                result.Reason = "dead";
                return result;
            }
            if (hero.IsPrisoner)
            {
                result.Reason = "detained";
                return result;
            }
            if (hero.PartyBelongedTo != null && hero.PartyBelongedTo != MobileParty.MainParty)
            {
                result.Reason = "elsewhere";
                return result;
            }
            bool preparedDiagnostic = diagnostic && CoopTestSession.AllowsPreparedActor(candidateId, null);
            if (!preparedDiagnostic && config.RequireQuestTurnedIn && !StoryProbeState.IsQuestTurnedIn(candidateId))
            {
                result.Reason = "prerequisite_not_met";
                return result;
            }
            if (!preparedDiagnostic && config.RequireEventSettled && !EventSettled(candidateId))
            {
                result.Reason = "event_not_settled";
                return result;
            }
            var agent = host.GetAgent(candidateId);
            if (agent == null || !agent.IsActive())
            {
                result.Reason = "body_unavailable";
                return result;
            }
            if (!CoopMissionRuntime.BodyAvailable(agent, out var bodyReason))
            { result.Reason = bodyReason; return result; }

            result.Mode = GetMode(hero);
            result.Price = Math.Max(0, config.PriceGold);
            result.FavorAvailable = HasFavor(hero) && result.Mode == CoopContractMode.None;
            if (result.Mode == CoopContractMode.Companion)
            {
                result.Reason = "already_companion";
                return result;
            }
            result.Ok = true;
            return result;
        }

        /// <summary>
        /// 临时人情的提交点：首个合法交涉真正开始时消费一次；预览、早停、走位受阻不消费。
        /// </summary>
        public static bool ConsumeFavor(Hero hero, string purpose, double simTime)
        {
            var director = CoopDirector.Current;
            if (hero == null || director == null || director.Ledger == null || !HasFavor(hero))
            {
                return false;
            }
            director.Ledger.SetRelation(hero.StringId, FavorKey, "used:" + purpose);
            director.Ledger.SetRelation(hero.StringId, ModeKey, CoopContractMode.Temporary.ToString());
            CoopLog.Event("favor.used")
                .Actor(hero.StringId).ContractMode("temporary")
                .Operation(CoopInteraction.OperationId("favor", hero.StringId, null, purpose))
                .SimTime(simTime)
                .Write();
            return true;
        }

        /// <summary>正式付费招募：报价 → 付款 → 入队，每步核真，收据只提交一次。</summary>
        public static bool TryCommitPaid(string candidateId, double simTime, out string reason)
        {
            reason = null;
            var config = Config;
            if (config == null)
            {
                reason = "recruitment_not_configured";
                return false;
            }
            var operationId = CoopInteraction.OperationId("recruit.paid", Hero.MainHero != null ? Hero.MainHero.StringId : "player", candidateId, config.PriceGold.ToString());
            var director = CoopDirector.Current;
            if (director != null && director.Ledger != null && director.Ledger.HasReceipt(operationId))
            {
                CoopLog.Event("receipt.reused").Operation(operationId).Write();
                reason = "already_committed";
                return false;
            }

            var eligibility = Evaluate(candidateId);
            if (!eligibility.Ok)
            {
                reason = eligibility.Reason;
                return false;
            }
            var hero = eligibility.Hero;
            int price = eligibility.Price;
            if (Hero.MainHero.Gold < price)
            {
                reason = "gold_insufficient";
                return false;
            }
            if (!HasCapacity(out reason))
            {
                return false;
            }

            int paid = AgentControlHelper.TransferGold(Hero.MainHero, hero, price);
            if (paid < price)
            {
                // 转移不完整：按实际账目恢复，不盲目再扣。
                if (paid > 0)
                {
                    AgentControlHelper.TransferGold(hero, Hero.MainHero, paid, notify: false);
                }
                reason = "transfer_incomplete";
                return false;
            }
            if (!ApplyCompanion(hero, candidateId, out reason))
            {
                AgentControlHelper.TransferGold(hero, Hero.MainHero, paid);
                return false;
            }
            Commit(operationId, hero, CoopContractMode.Companion, "paid=" + paid, simTime);
            return true;
        }

        /// <summary>正式说服招募：免本次招募费，入队核验同付费路径。</summary>
        public static bool TryCommitCharm(string candidateId, double simTime, out string reason)
        {
            reason = null;
            var config = Config;
            if (config == null)
            {
                reason = "recruitment_not_configured";
                return false;
            }
            var operationId = CoopInteraction.OperationId("recruit.charm", Hero.MainHero != null ? Hero.MainHero.StringId : "player", candidateId, "0");
            var director = CoopDirector.Current;
            if (director != null && director.Ledger != null && director.Ledger.HasReceipt(operationId))
            {
                reason = "already_committed";
                return false;
            }
            var eligibility = Evaluate(candidateId);
            if (!eligibility.Ok)
            {
                reason = eligibility.Reason;
                return false;
            }
            if (!HasCapacity(out reason))
            {
                return false;
            }
            if (!ApplyCompanion(eligibility.Hero, candidateId, out reason))
            {
                return false;
            }
            Commit(operationId, eligibility.Hero, CoopContractMode.Companion, "free", simTime);
            return true;
        }

        /// <summary>临时协作约定（不占队伍名额、不扣招募费）。</summary>
        public static bool TrySetTemporary(string candidateId, string purpose, double simTime, out string reason, bool diagnostic = false)
        {
            reason = null;
            var eligibility = Evaluate(candidateId, diagnostic);
            if (!eligibility.Ok)
            {
                reason = eligibility.Reason;
                return false;
            }
            if (!HasFavor(eligibility.Hero)) { reason = "favor_used"; return false; }
            if (eligibility.Mode == CoopContractMode.Companion)
            {
                reason = "already_companion";
                return false;
            }
            var director = CoopDirector.Current;
            director.Ledger.SetRelation(eligibility.Hero.StringId, ModeKey, CoopContractMode.Temporary.ToString());
            director.Ledger.SetRelation(eligibility.Hero.StringId, "contract.purpose", purpose ?? string.Empty);
            director.Ledger.SetRelation(eligibility.Hero.StringId, "contract.scene", director.RunId);
            CoopLog.Event("agreement.committed")
                .Actor(eligibility.Hero.StringId).ContractMode("temporary")
                .Value("purpose", purpose)
                .SimTime(simTime)
                .Write();
            return true;
        }

        private static bool EventSettled(string actorId)
        {
            foreach (var evt in StoryProbeDefs.Events)
                if (evt != null && (evt.ActorA == actorId || evt.ActorB == actorId
                    || evt.HostNpcId == actorId || evt.Members.Contains(actorId))
                    && !StoryProbeState.IsStreetEventResolved(evt.Index)) return false;
            return true;
        }

        public static bool CanCooperate(string actorId, CoopActivity activity, out string reason)
        {
            reason = null;
            var host = StoryProbeMissionBehavior.Current;
            var hero = host?.GetHero(actorId);
            var director = CoopDirector.Current;
            if (hero == null || !hero.IsAlive || hero.IsPrisoner) { reason = "hero_unavailable"; return false; }
            if (hero.PartyBelongedTo != null && hero.PartyBelongedTo != MobileParty.MainParty)
            { reason = "elsewhere"; return false; }
            var mode = GetMode(hero);
            if (mode == CoopContractMode.Companion)
            {
                if (MobileParty.MainParty?.MemberRoster == null
                    || !MobileParty.MainParty.MemberRoster.Contains(hero.CharacterObject)) { reason = "not_in_main_party"; return false; }
                return true;
            }
            if (mode != CoopContractMode.Temporary || director == null
                || director.Ledger.GetRelation(hero.StringId, "contract.scene") != director.RunId)
            { reason = "no_current_agreement"; return false; }
            if (!HasFavor(hero) && (activity == null || !activity.FavorCommitted))
            { reason = "favor_used"; return false; }
            if (!EventSettled(actorId) && !CoopTestSession.AllowsPreparedActor(actorId, activity))
            { reason = "event_not_settled"; return false; }
            return true;
        }

        public static bool CanRequestPurpose(string actorId, CoopActivity activity, string purpose, out string reason)
        {
            if (!CanCooperate(actorId, activity, out reason)) return false;
            var hero = StoryProbeMissionBehavior.Current?.GetHero(actorId);
            if (GetMode(hero) == CoopContractMode.Companion) return true;
            var agreed = CoopDirector.Current?.Ledger.GetRelation(hero.StringId, "contract.purpose");
            if (!string.IsNullOrEmpty(purpose) && string.Equals(agreed, purpose, StringComparison.Ordinal)) return true;
            reason = "purpose_not_agreed";
            return false;
        }

        private static bool HasCapacity(out string reason)
        {
            reason = null;
            var clan = Clan.PlayerClan;
            if (clan == null)
            {
                reason = "no_clan";
                return false;
            }
            if (clan.Companions != null && clan.CompanionLimit > 0 && clan.Companions.Count >= clan.CompanionLimit)
            {
                reason = "clan_full";
                return false;
            }
            var party = PartyBase.MainParty;
            if (party != null && party.MemberRoster.TotalManCount >= party.PartySizeLimit)
            {
                reason = "party_full";
                return false;
            }
            return true;
        }

        private static bool ApplyCompanion(Hero hero, string actorId, out string reason)
        {
            reason = null;
            var party = MobileParty.MainParty;
            if (party?.MemberRoster == null || Clan.PlayerClan == null)
            { reason = "main_party_unavailable"; return false; }
            bool wasInParty = party.MemberRoster.Contains(hero.CharacterObject);
            var oldActivity = CoopDirector.Current?.ForActor(actorId);
            if (oldActivity?.IsActive == true)
                CoopDirector.Current.StopActivity(oldActivity, "contract_changed", CoopMissionBehavior.Current?.SimTime ?? 0);
            try
            {
                AddCompanionAction.Apply(Clan.PlayerClan, hero);
                if (!hero.IsPlayerCompanion || hero.CompanionOf != Clan.PlayerClan)
                    throw new InvalidOperationException("companion_not_applied");
                if (!party.MemberRoster.Contains(hero.CharacterObject)) AddHeroToPartyAction.Apply(hero, party);
                if (hero.PartyBelongedTo != party || !party.MemberRoster.Contains(hero.CharacterObject))
                    throw new InvalidOperationException("main_party_not_applied");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[Coop] recruit exception " + ex.GetType().Name + " " + ex.Message);
                // 事件消费者可能在已经完成原生入队后抛错；核实最终归属，不能把成功退款。
                if (hero.IsPlayerCompanion && hero.CompanionOf == Clan.PlayerClan && hero.PartyBelongedTo == party
                    && party.MemberRoster.Contains(hero.CharacterObject)) return true;
                try
                {
                    if (hero.CompanionOf == Clan.PlayerClan)
                    {
                        if (!wasInParty && hero.PartyBelongedTo == party && party.MemberRoster.Contains(hero.CharacterObject))
                            RemoveCompanionAction.ApplyAfterQuest(Clan.PlayerClan, hero);
                        else RemoveCompanionAction.ApplyByByTurningToLord(Clan.PlayerClan, hero);
                    }
                    CoopLog.Event("receipt.compensated").Actor(hero.StringId).Reason("recruitment_rejected").Write();
                }
                catch (Exception rollback)
                { DebugLogger.Log("[Coop] recruit rollback failed: " + rollback.GetType().Name); }
                reason = "add_companion_failed";
                return false;
            }
        }

        private static void Commit(string operationId, Hero hero, CoopContractMode mode, string detail, double simTime)
        {
            var director = CoopDirector.Current;
            if (director != null && director.Ledger != null)
            {
                if (director.Ledger.TryCommitReceipt(operationId, detail))
                {
                    CoopLog.Event("receipt.committed").Operation(operationId).Detail(detail).SimTime(simTime).Write();
                }
                director.Ledger.SetRelation(hero.StringId, ModeKey, mode.ToString());
            }
            CoopLog.Event("recruit.committed")
                .Actor(hero.StringId).ContractMode(mode.ToString().ToLowerInvariant())
                .Operation(operationId).Detail(detail)
                .SimTime(simTime)
                .Write();
        }
    }
}
