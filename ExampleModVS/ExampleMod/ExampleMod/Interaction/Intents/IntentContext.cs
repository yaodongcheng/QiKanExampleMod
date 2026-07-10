using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
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
    /// NPC 意图发起方的身份等级。
    /// </summary>
    public enum NpcIdentityLevel
    {
        None,       // 既无 Agent 也无 Hero — 不触发意图
        AgentOnly,  // 仅有 Agent（模板士兵/农民）— 只能做 Mission 行为
        Full,       // 有 Hero（可能有也可能没有 Agent）— 完整功能
    }

    /// <summary>
    /// 交互上下文：开对话时一次性算好身份/关系，传给资格判定和结算，避免每个意图重复取数。
    /// 各意图的 Evaluate 直接读这里的计算属性（IsHero / SameFaction / Relation ...），声明式书写。
    ///
    /// 支持两种构建方式：
    /// - Build(agent, controller): 玩家视角（现有，不变）
    /// - BuildForNpc(npcAgent, npcHero): NPC 视角（新增，NPC 发起意图时使用）
    ///
    /// 语义约定：Speaker = 当前说话的 NPC，Listener = 玩家（Hero.MainHero）。
    /// </summary>
    public class IntentContext
    {
        public Agent Agent;                // Speaker 对应的 Agent（Mission 内非 null；大地图对话为 null）
        public Hero Speaker;               // 当前说话的 NPC（对方是 Hero 时非 null；批量小兵为 null）
        public Hero Listener;              // 始终为 Hero.MainHero（玩家）
        public SingNpcMemorySystem Memory;
        public NPCProfile Profile;         // Memory?._profile
        public InteractionController Controller;

        // ── 计算属性（构造时一次性算好）──
        public bool IsHero;
        public int Relation;
        public bool SameFaction;           // 同一 MapFaction
        public bool EnemyFaction;          // 与玩家阵营交战
        public bool IsLiege;               // 同阵营且对方是阵营领袖（玩家的主君）
        public bool IsClanLeader;          // 对方是自己家族族长
        public bool IsWanderer;
        public bool IsMarried;
        public bool OppositeSex;
        public bool PlayerHasNoKingdom;    // 玩家自由身（未加入王国）
        public bool IsMySoldier;           // 非 Hero、且在玩家队伍(同 Team)的士兵
        public bool IsEnemyAgent;          // 战场上与玩家敌对的 agent（含非 Hero）
        public bool IsRecruitableCivilian; // 非 Hero 平民（可花钱招募为兵）
        public bool IsChild;               // 未成年（Hero: Age<16，非Hero: Character.IsChild）

        /// <summary>NPC 当前是否被世界事件缠身（作为加害方或受害者）。从 NPC memory 的 CurrentUrgentEvent 读取。</summary>
        public bool HasUrgentWorldEvent;
        /// <summary>用户是否已点击"有别的事找你"展开全部选项。仅当 HasUrgentWorldEvent 时有效。</summary>
        public bool ExpandedOptions;

        // ═══ 犯罪追责扩展 ═══
        /// <summary>当前关联的犯罪事件（null = 非追责场景）</summary>
        public WorldEvent ActiveEvent;
        /// <summary>DialogueInjector 注入的附加参数，由各 Intent 自行解析（如栽赃目标 ID / 证物 ID）。</summary>
        public string ActionParam;

        // ═══ 场景上下文 ═══
        /// <summary>当前对话是否发生在 Mission 内（村庄/酒馆等 3D 场景）。
        /// false = 大地图对话（CampaignMapConversation），无法触发战斗/叫守卫。</summary>
        public bool IsInMission;

        // ═══ 新增：NPC 视角字段 ═══
        /// <summary>NPC 发起方身份等级。非 null 表示这是 NPC 视角的上下文。</summary>
        public NpcIdentityLevel? NpcLevel;
        /// <summary>NPC 发起方的 Agent（模板士兵有，大地图对话为 null）</summary>
        public Agent NpcAgent;
        /// <summary>NPC 发起方的 Hero（模板士兵为 null）</summary>
        public Hero NpcHero;

        // ═══ Mission 内警戒上下文（L3 警戒质问） ═══
        /// <summary>
        /// 玩家在 Mission 内的不法行为明细（拔刀/蹲伏/偷窃/攻击盟友/击晕 等 → 警戒值）。
        /// 来自 AgentBrain._alertBreakdown。L3 警戒质问对话构建时读取。
        /// null = 非 Mission 场景或无警戒数据。
        /// </summary>
        public IReadOnlyDictionary<PlayerActionType, AlertEntry> AlertBreakdown;
        /// <summary>当前最高警戒值对应的行为类型（快捷读取，避免每次遍历字典）。</summary>
        public PlayerActionType? PrimaryAlertAction;
        /// <summary>总警戒值（快捷读取）。</summary>
        public float AlertValue;

        public bool RelationAtLeast(int v) { return Relation >= v; }
        public bool OnCooldown(NegotiationGoalType goal) { return Speaker != null && IntentCooldownStore.IsOnCooldown(Speaker, goal); }
        public int CooldownDaysLeft(NegotiationGoalType goal) { return Speaker != null ? IntentCooldownStore.DaysLeft(Speaker, goal) : 0; }

        /// <summary>玩家视角构建（现有，不变）</summary>
        public static IntentContext Build(Agent agent, InteractionController controller)
        {
            var ctx = new IntentContext();
            ctx.Agent = agent;
            ctx.Listener = Hero.MainHero;
            ctx.Controller = controller;
            ctx.IsInMission = agent != null || Mission.Current != null;
            ctx.Speaker = (agent != null ? agent.Character as CharacterObject : null)?.HeroObject;
            ctx.IsHero = ctx.Speaker != null;

            if (ctx.Speaker != null)
            {
                ctx.Memory = AllNpcMemoryManager.GetMemoryForAgent(agent);
                ctx.Profile = ctx.Memory != null ? ctx.Memory._profile : null;

                ctx.Relation = ctx.Speaker.GetRelation(Hero.MainHero);
                IFaction myFaction = Hero.MainHero.MapFaction;
                IFaction theirFaction = ctx.Speaker.MapFaction;
                ctx.SameFaction = myFaction != null && theirFaction != null && myFaction == theirFaction;
                ctx.EnemyFaction = myFaction != null && theirFaction != null && theirFaction.IsAtWarWith(myFaction);
                ctx.IsLiege = ctx.SameFaction && ctx.Speaker.IsFactionLeader && ctx.Speaker != Hero.MainHero;
                ctx.IsClanLeader = ctx.Speaker.Clan != null && ctx.Speaker.Clan.Leader == ctx.Speaker;
                ctx.IsWanderer = ctx.Speaker.IsWanderer;
                ctx.IsMarried = ctx.Speaker.Spouse != null;
                ctx.OppositeSex = ctx.Speaker.IsFemale != Hero.MainHero.IsFemale;
                ctx.PlayerHasNoKingdom = Clan.PlayerClan == null || Clan.PlayerClan.Kingdom == null;
                ctx.IsChild = ctx.Speaker.Age < 16f;
                ctx.HasUrgentWorldEvent = ctx.Memory?.CurrentUrgentEvent != null;
                ctx.ExpandedOptions = ctx.Controller?.OptionsExpanded ?? false;
            }
            else
            {
                // 非 Hero：判断是不是自己人 / 战场敌人
                CharacterObject co = agent != null ? agent.Character as CharacterObject : null;
                bool isSoldier = co != null && co.IsSoldier;

                // 在对话场景中，所有 agent 同队 → IsEnemyAgent 永远 false。
                // 用战役层面的 party 敌对关系纠正，防止敌方部队被误判为可招募平民。
                bool isHostileParty = false;
                if (co != null && !isSoldier && MapEncounterDialogState.Active && MapEncounterDialogState.PartnerParty != null)
                {
                    var partyFaction = MapEncounterDialogState.PartnerParty.MapFaction;
                    var playerFaction = Hero.MainHero.MapFaction;
                    isHostileParty = partyFaction != null && playerFaction != null
                        && partyFaction.IsAtWarWith(playerFaction);
                }

                if (isSoldier && agent != null && agent.Team != null && Agent.Main != null && Agent.Main.Team != null)
                {
                    // 对话场景中所有人同队，必须以战役层面敌对关系为准
                    ctx.IsMySoldier = !isHostileParty && agent.Team == Agent.Main.Team;
                    ctx.IsEnemyAgent = isHostileParty || agent.Team.IsEnemyOf(Agent.Main.Team);
                }
                // 可招募平民：非士兵、非敌对（战役层面）、其文化有基础兵
                if (co != null && !isSoldier && !ctx.IsEnemyAgent && !isHostileParty)
                {
                    CultureObject culture = co.Culture as CultureObject;
                    ctx.IsRecruitableCivilian = culture != null && culture.BasicTroop != null;
                }
                // 小孩判定（非 Hero）：CharacterObject 无 IsChild API，降级用 Agent.Age
                ctx.IsChild = agent != null && agent.Age < 16f;
            }
            return ctx;
        }

        /// <summary>
        /// NPC 视角构建：NPC 发起意图时的上下文。
        /// npcAgent 和 npcHero 至少需要一个非 null，否则返回 null（无法发起意图）。
        /// Speaker = 发起意图的 NPC，Listener = 玩家（Hero.MainHero）。
        /// </summary>
        public static IntentContext BuildForNpc(Agent npcAgent = null, Hero npcHero = null)
        {
            // 1. 确定 NPC 身份等级
            NpcIdentityLevel level;
            Hero resolvedHero = npcHero ?? (npcAgent?.Character as CharacterObject)?.HeroObject;

            if (resolvedHero != null)
                level = NpcIdentityLevel.Full;
            else if (npcAgent != null)
                level = NpcIdentityLevel.AgentOnly;
            else
                return null; // 什么都没有，不构建上下文

            var ctx = new IntentContext
            {
                NpcLevel = level,
                NpcAgent = npcAgent,
                NpcHero = resolvedHero,

                // Speaker = 发起方 NPC，Agent = NPC 的 Agent
                Speaker = resolvedHero,
                Agent = npcAgent,
                IsHero = resolvedHero != null,

                // Listener = 玩家（交互目标）
                Listener = Hero.MainHero,
                IsInMission = Mission.Current != null,
            };

            // 3. 关系相关字段（仅当 NPC 有 Hero 时有意义）
            if (resolvedHero != null)
            {
                ctx.Memory = AllNpcMemoryManager.GetMemory(resolvedHero.StringId);
                ctx.Profile = ctx.Memory?._profile;
                ctx.Relation = resolvedHero.GetRelation(Hero.MainHero);
                IFaction myFaction = Hero.MainHero.MapFaction;
                IFaction theirFaction = resolvedHero.MapFaction;
                ctx.SameFaction = myFaction != null && theirFaction != null && myFaction == theirFaction;
                ctx.EnemyFaction = myFaction != null && theirFaction != null && theirFaction.IsAtWarWith(myFaction);
                ctx.IsLiege = ctx.SameFaction && resolvedHero.IsFactionLeader && resolvedHero != Hero.MainHero;
                ctx.IsClanLeader = resolvedHero.Clan != null && resolvedHero.Clan.Leader == resolvedHero;
                ctx.IsWanderer = resolvedHero.IsWanderer;
                ctx.IsMarried = resolvedHero.Spouse != null;
                ctx.OppositeSex = resolvedHero.IsFemale != Hero.MainHero.IsFemale;
                ctx.PlayerHasNoKingdom = Clan.PlayerClan == null || Clan.PlayerClan.Kingdom == null;
                ctx.IsChild = resolvedHero.Age < 16f;
                ctx.HasUrgentWorldEvent = ctx.Memory?.CurrentUrgentEvent != null;
            }
            else
            {
                // 模板 NPC：关系默认 0，无阵营概念
                ctx.Relation = 0;
                ctx.SameFaction = false;
                ctx.EnemyFaction = false;
            }

            return ctx;
        }
    }
}
