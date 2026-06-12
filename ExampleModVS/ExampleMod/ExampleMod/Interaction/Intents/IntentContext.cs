using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.Story
{
    /// <summary>选项资格三态：隐藏 / 置灰(带原因) / 可用。</summary>
    public enum EligState { Hidden, Disabled, Enabled }

    public struct Eligibility
    {
        public EligState State;
        public string Reason;   // Disabled 时的置灰原因

        public static Eligibility Show() { return new Eligibility { State = EligState.Enabled, Reason = "" }; }
        public static Eligibility Hide() { return new Eligibility { State = EligState.Hidden, Reason = "" }; }
        public static Eligibility Grey(string reason) { return new Eligibility { State = EligState.Disabled, Reason = reason }; }
    }

    /// <summary>
    /// 交互上下文：开对话时一次性算好身份/关系，传给资格判定和结算，避免每个意图重复取数。
    /// 各意图的 Evaluate 直接读这里的计算属性（IsHero / SameFaction / Relation ...），声明式书写。
    /// </summary>
    public class IntentContext
    {
        public Agent Agent;
        public Hero Target;             // 对方是 Hero 时非 null；批量小兵为 null
        public Hero Player;
        public SingNpcMemorySystem Memory;
        public NPCProfile Profile;      // Memory?._profile
        public InteractionController Controller;

        // ── 计算属性（构造时一次性算好）──
        public bool IsHero;
        public int Relation;
        public bool SameFaction;        // 同一 MapFaction
        public bool EnemyFaction;       // 与玩家阵营交战
        public bool IsLiege;            // 同阵营且对方是阵营领袖（玩家的主君）
        public bool IsClanLeader;       // 对方是自己家族族长
        public bool IsWanderer;
        public bool IsMarried;
        public bool OppositeSex;
        public bool PlayerHasNoKingdom; // 玩家自由身（未加入王国）
        public bool IsMySoldier;        // 非 Hero、且在玩家队伍(同 Team)的士兵
        public bool IsEnemyAgent;       // 战场上与玩家敌对的 agent（含非 Hero）
        public bool IsRecruitableCivilian; // 非 Hero 平民（可花钱招募为兵）
        public bool IsChild;              // 未成年（Hero: Age<16，非Hero: Character.IsChild）

        public bool RelationAtLeast(int v) { return Relation >= v; }
        public bool OnCooldown(NegotiationGoalType goal) { return Target != null && IntentCooldownStore.IsOnCooldown(Target, goal); }
        public int CooldownDaysLeft(NegotiationGoalType goal) { return Target != null ? IntentCooldownStore.DaysLeft(Target, goal) : 0; }

        public static IntentContext Build(Agent agent, InteractionController controller)
        {
            var ctx = new IntentContext();
            ctx.Agent = agent;
            ctx.Player = Hero.MainHero;
            ctx.Controller = controller;
            ctx.Target = (agent != null ? agent.Character as CharacterObject : null)?.HeroObject;
            ctx.IsHero = ctx.Target != null;

            if (ctx.Target != null)
            {
                ctx.Memory = AllNpcMemoryManager.GetMemoryForAgent(agent);
                ctx.Profile = ctx.Memory != null ? ctx.Memory._profile : null;

                ctx.Relation = ctx.Target.GetRelation(Hero.MainHero);
                IFaction myFaction = Hero.MainHero.MapFaction;
                IFaction theirFaction = ctx.Target.MapFaction;
                ctx.SameFaction = myFaction != null && theirFaction != null && myFaction == theirFaction;
                ctx.EnemyFaction = myFaction != null && theirFaction != null && theirFaction.IsAtWarWith(myFaction);
                ctx.IsLiege = ctx.SameFaction && ctx.Target.IsFactionLeader && ctx.Target != Hero.MainHero;
                ctx.IsClanLeader = ctx.Target.Clan != null && ctx.Target.Clan.Leader == ctx.Target;
                ctx.IsWanderer = ctx.Target.IsWanderer;
                ctx.IsMarried = ctx.Target.Spouse != null;
                ctx.OppositeSex = ctx.Target.IsFemale != Hero.MainHero.IsFemale;
                ctx.PlayerHasNoKingdom = Clan.PlayerClan == null || Clan.PlayerClan.Kingdom == null;
                ctx.IsChild = ctx.Target.Age < 16f;
            }
            else
            {
                // 非 Hero：判断是不是自己人 / 战场敌人
                CharacterObject co = agent != null ? agent.Character as CharacterObject : null;
                bool isSoldier = co != null && co.IsSoldier;
                if (isSoldier && agent != null && agent.Team != null && Agent.Main != null && Agent.Main.Team != null)
                {
                    ctx.IsMySoldier = agent.Team == Agent.Main.Team;
                    ctx.IsEnemyAgent = agent.Team.IsEnemyOf(Agent.Main.Team);
                }
                // 可招募平民：非士兵、非敌对、其文化有基础兵
                if (co != null && !isSoldier && !ctx.IsEnemyAgent)
                {
                    CultureObject culture = co.Culture as CultureObject;
                    ctx.IsRecruitableCivilian = culture != null && culture.BasicTroop != null;
                }
                // 小孩判定（非 Hero）：CharacterObject 无 IsChild API，降级用 Agent.Age
                ctx.IsChild = agent != null && agent.Age < 16f;
            }
            return ctx;
        }
    }
}
