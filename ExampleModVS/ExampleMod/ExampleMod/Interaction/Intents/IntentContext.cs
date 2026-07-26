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
    /// 交互上下文：开对话时一次性算好身份/关系，传给资格判定和结算，避免每个意图重复取数。
    /// 各意图的 Evaluate 直接读这里的计算属性（IsHero / SameFaction / Relation ...），声明式书写。
    ///
    /// 唯一入口：public 构造函数，所有参数可选，内部自动推导。
    /// Speaker 推导优先级：显式 speaker 参数 > agent 的 HeroObject。
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

        // ═══ L3 警戒质问扩展 ═══
        /// <summary>NPC 的质问意图（驱离/搜查/追回/制止）。L3 警戒质问场景专用。</summary>
        public ConfrontationType Confrontation = ConfrontationType.Deter;
        /// <summary>触发质问的玩家行为类型。L3 警戒质问场景专用。</summary>
        public PlayerActionType TriggerAction = PlayerActionType.Crouching;

        // ═══ 场景上下文 ═══
        /// <summary>当前对话是否发生在 Mission 内（村庄/酒馆等 3D 场景）。
        /// false = 大地图对话（CampaignMapConversation），无法触发战斗/叫守卫。</summary>
        public bool IsInMission;

        // ═══ 唯一构造入口 ═══
        /// <summary>
        /// 构建交互上下文。所有参数可选。
        /// Speaker 推导优先级：显式 speaker > agent 的 HeroObject。
        /// </summary>
        /// <param name="agent">Speaker 对应的 Agent（Mission 内非 null；大地图对话为 null）</param>
        /// <param name="controller">InteractionController，有则用于读取 ExpandedOptions</param>
        /// <param name="speaker">显式指定 Speaker Hero，覆盖 agent 推导结果。大地图对话 agent 为 null 时用此参数</param>
        /// <param name="worldEvent">犯罪追责等世界事件场景时传入</param>
        /// <param name="actionParam">DialogueInjector 注入的附加参数</param>
        public IntentContext(
            Agent agent = null,
            InteractionController controller = null,
            Hero speaker = null,
            WorldEvent worldEvent = null,
            string actionParam = null)
        {
            Agent = agent;
            Controller = controller;
            Listener = Hero.MainHero;
            IsInMission = Mission.Current != null;
            ActiveEvent = worldEvent;
            ActionParam = actionParam;

            // Speaker 推导：显式 speaker > agent 的 HeroObject
            Hero resolvedSpeaker = speaker ?? (agent?.Character as CharacterObject)?.HeroObject;
            Speaker = resolvedSpeaker;
            IsHero = resolvedSpeaker != null;

            if (resolvedSpeaker != null)
            {
                PopulateHeroFields(resolvedSpeaker);
                ExpandedOptions = controller?.OptionsExpanded ?? false;
            }
            else
            {
                // 非 Hero：判断是不是自己人 / 战场敌人 / 可招募平民 / 小孩
                CharacterObject co = agent?.Character as CharacterObject;
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

                // ⚠️ Team.Invalid 是 non-null 单例（MBTeam._mission=null），!= null 挡不住，
                // 必须同时查 IsValid——地牢等场景守卫的 Team 就是 Invalid，IsEnemyOf 内部解引用 null mission 必 NRE
                if (isSoldier && agent != null && agent.Team != null && agent.Team.IsValid
                    && Agent.Main != null && Agent.Main.Team != null && Agent.Main.Team.IsValid)
                {
                    // 对话场景中所有人同队，必须以战役层面敌对关系为准
                    IsMySoldier = !isHostileParty && agent.Team == Agent.Main.Team;
                    IsEnemyAgent = isHostileParty || agent.Team.IsEnemyOf(Agent.Main.Team);
                }
                // 可招募平民：非士兵、非敌对（战役层面）、其文化有基础兵
                if (co != null && !isSoldier && !IsEnemyAgent && !isHostileParty)
                {
                    CultureObject culture = co.Culture as CultureObject;
                    IsRecruitableCivilian = culture != null && culture.BasicTroop != null;
                }
                // 小孩判定（非 Hero）：CharacterObject 无 IsChild API，降级用 Agent.Age
                IsChild = agent != null && agent.Age < 16f;
            }
        }

        public bool RelationAtLeast(int v) { return Relation >= v; }
        public bool OnCooldown(NegotiationGoalType goal) { return Speaker != null && IntentCooldownStore.IsOnCooldown(Speaker, goal); }
        public int CooldownDaysLeft(NegotiationGoalType goal) { return Speaker != null ? IntentCooldownStore.DaysLeft(Speaker, goal) : 0; }

        /// <summary>
        /// 根据 Hero 填充关系/阵营/身份等计算字段。
        /// 调用前需已设置 Speaker = hero, IsHero = true。
        /// </summary>
        private void PopulateHeroFields(Hero hero)
        {
            if (hero == null || Hero.MainHero == null) return;

            Memory = AllNpcMemoryManager.GetMemory(hero.StringId);
            Profile = Memory?._profile;
            Relation = hero.GetRelation(Hero.MainHero);
            IFaction myFaction = Hero.MainHero.MapFaction;
            IFaction theirFaction = hero.MapFaction;
            SameFaction = myFaction != null && theirFaction != null && myFaction == theirFaction;
            EnemyFaction = myFaction != null && theirFaction != null && theirFaction.IsAtWarWith(myFaction);
            IsLiege = SameFaction && hero.IsFactionLeader && hero != Hero.MainHero;
            IsClanLeader = hero.Clan != null && hero.Clan.Leader == hero;
            IsWanderer = hero.IsWanderer;
            IsMarried = hero.Spouse != null;
            OppositeSex = hero.IsFemale != Hero.MainHero.IsFemale;
            PlayerHasNoKingdom = Clan.PlayerClan == null || Clan.PlayerClan.Kingdom == null;
            IsChild = hero.Age < 16f;
            HasUrgentWorldEvent = Memory?.CurrentUrgentEvent != null;
        }
    }
}
