using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 友方判定轮子（全项目统一入口）。
    ///
    /// 「友方」定义 = <see cref="Settings.FriendlyRelationCriteria"/>（config.json 可改，
    /// 默认 Party + Clan；可选 Kingdom）+ 好感度（独立字段 <see cref="Settings.FriendlyRelationThreshold"/>，
    /// 数值玩家自设，默认 50）。判据：
    ///   Party    — 同队伍：玩家同伴（IsPlayerCompanion）/ 玩家主队成员 / 玩家部队模板士兵
    ///   Clan     — 同家族：Hero.Clan == Clan.PlayerClan；模板 NPC 走归属链（见下）
    ///   Kingdom  — 同王国：Clan.Kingdom 相等（双方都非 null 才算）
    ///   Relation — 好感度：该 Hero 对玩家好感 ≥ FriendlyRelationThreshold（独立生效，
    ///              不进 criteria 列表；仅 Hero 有个人好感值；设 101 可关闭）
    ///
    /// 🔴 禁止用 Mission Team 相等判「同队伍」——和平场景所有 NPC 同属玩家队
    /// （AgentBrain.IsPlayerTeammate 旧注释踩坑记录），会把全村人当队友。
    ///
    /// 模板 NPC 归属链（复刻 NPCProfile.cs 范式）：
    ///   PartyAgentOrigin.Party.Owner?.Clan → Party.IsSettlement → Settlement.OwnerClan
    /// （站岗士兵多属于 GarrisonParty；生成的守卫可退化为当前场景归属）。
    /// </summary>
    public static class FriendlinessHelper
    {
        /// <summary>按 Settings.FriendlyRelationCriteria 判定 target 是否被玩家视为友方（任一条命中即 true；玩家自己 false）。</summary>
        public static bool IsFriendlyToPlayer(Agent target)
        {
            if (target == null || target == Agent.Main) return false;
            var hero = (target.Character as CharacterObject)?.HeroObject;
            if (hero != null) return IsFriendlyToPlayer(hero);
            return IsTemplateFriendly(target);
        }

        /// <summary>Hero 版本（Agent.Character.HeroObject 提取后复用）。
        /// 或关系：列表维度（Party/Clan/Kingdom）与好感维度（FriendlyRelationThreshold）任意一条
        /// 满足即视为友方——比如别的王国的英雄但与玩家好感 ≥ 阈值，也算友方。</summary>
        public static bool IsFriendlyToPlayer(Hero hero)
        {
            if (hero == null || hero == Hero.MainHero) return false;
            var criteria = Settings.Instance?.FriendlyRelationCriteria;
            if (criteria != null && criteria.Count > 0)
            {
                if (HasCriteria(criteria, "Party") && IsPlayerPartyMember(hero)) return true;
                if (HasCriteria(criteria, "Clan") && hero.Clan != null && Clan.PlayerClan != null && hero.Clan == Clan.PlayerClan) return true;
                if (HasCriteria(criteria, "Kingdom") && SameKingdom(hero.Clan?.Kingdom)) return true;
            }
            // 好感维度独立生效（不依赖 criteria 列表；空列表只关闭列表维度，不关闭好感）
            if (MeetsRelationThreshold(hero)) return true;
            return false;
        }

        /// <summary>
        /// 🔴 2026-08-14（npc-risk-aware-planning.md M3.5）：任意两方（Agent）的通用友方判定。
        /// 解决：旧 IsFriendlyToPlayer 只支持「→ 玩家」方向；风险审视需要随从 vs 在场者、目标 vs 在场者
        /// 的任意方向判定。四维判据与 IsFriendlyToPlayer 同语义（Party 同队伍 / Clan 同家族 /
        /// Kingdom 同王国 / Relation 好感阈值），对称调用：
        ///   ① Party — 同队伍（含模板士兵同属一 party）
        ///   ② Clan — 双方 Hero.Clan 相等（模板 NPC 走归属链，与 IsTemplateFriendly 同口径）
        ///   ③ Kingdom — Clan.Kingdom 相等（双方都非 null 才算）
        ///   ④ Relation — 任一方向好感 ≥ 阈值（关系方向性问题：b.GetRelation(a) 与 a.GetRelation(b)
        ///      数值不同——取「任一方向达标即视为友方」的宽松语义，实现期记录于本注释）
        /// </summary>
        public static bool IsFriendlyBetween(Agent a, Agent b)
        {
            if (a == null || b == null || a == b) return false;
            // 玩家特判：任何一方是玩家 → 回落既有 IsFriendlyToPlayer（原语义零变化）
            if (a == Agent.Main) return IsFriendlyToPlayer(b);
            if (b == Agent.Main) return IsFriendlyToPlayer(a);
            var heroA = (a.Character as CharacterObject)?.HeroObject;
            var heroB = (b.Character as CharacterObject)?.HeroObject;
            if (heroA != null && heroB != null) return IsFriendlyBetween(heroA, heroB);
            return IsTemplateFriendlyBetween(a, b, heroA, heroB);
        }
        /// <summary>Hero 版本通用友方判定（对称语义，四维判据同 IsFriendlyToPlayer）。</summary>
        public static bool IsFriendlyBetween(Hero a, Hero b)
        {
            if (a == null || b == null || a == b) return false;
            if (a == Hero.MainHero) return IsFriendlyToPlayer(b);
            if (b == Hero.MainHero) return IsFriendlyToPlayer(a);
            var criteria = Settings.Instance?.FriendlyRelationCriteria;
            if (criteria != null && criteria.Count > 0)
            {
                if (HasCriteria(criteria, "Party") && SameParty(a, b)) return true;
                if (HasCriteria(criteria, "Clan") && a.Clan != null && b.Clan != null && a.Clan == b.Clan) return true;
                if (HasCriteria(criteria, "Kingdom") && SameKingdom(a.Clan?.Kingdom, b.Clan?.Kingdom)) return true;
            }
            // 好感维度：任一方向 ≥ 阈值即视为友方（宽松语义）
            try
            {
                if (a.GetRelation(b) >= Settings.Instance.FriendlyRelationThreshold) return true;
                if (b.GetRelation(a) >= Settings.Instance.FriendlyRelationThreshold) return true;
            }
            catch { }
            return false;
        }
        /// <summary>Hero 双方同队伍判定（同 party 或同为玩家同伴）。</summary>
        private static bool SameParty(Hero a, Hero b)
        {
            if (a.PartyBelongedTo != null && a.PartyBelongedTo == b.PartyBelongedTo) return true;
            return a.IsPlayerCompanion && b.IsPlayerCompanion;
        }
        /// <summary>同王国判定（双 null-guard）。</summary>
        private static bool SameKingdom(Kingdom other, Kingdom other2)
        {
            if (other == null || other2 == null) return false;
            return other == other2;
        }
        /// <summary>模板 NPC 参与的两方判定：Hero↔模板 或 模板↔模板——按归属链 clan/kingdom 维度。
        /// 模板 NPC 无好感值（关系维度天然不命中）；同属一 party（PartyAgentOrigin）也算同队。</summary>
        private static bool IsTemplateFriendlyBetween(Agent a, Agent b, Hero heroA, Hero heroB)
        {
            var criteria = Settings.Instance?.FriendlyRelationCriteria;
            if (criteria == null || criteria.Count == 0) return false;
            // 同 party（PartyAgentOrigin 同源 = 同队）——Hero 侧同队判定由 SameParty 已覆盖，此处只补模板
            try
            {
                if (a.Origin is PartyAgentOrigin pa && b.Origin is PartyAgentOrigin pb
                    && pa.Party != null && pa.Party == pb.Party)
                    return true;
            }
            catch { }
            Clan clanA = heroA?.Clan ?? ClanOf(heroA, a);
            Clan clanB = heroB?.Clan ?? ClanOf(heroB, b);
            if (clanA == null || clanB == null) return false;
            if (HasCriteria(criteria, "Clan") && clanA == clanB) return true;
            if (HasCriteria(criteria, "Kingdom") && SameKingdom(clanA.Kingdom, clanB.Kingdom)) return true;
            return false;
        }
        /// <summary>Agent → 归属 Clan（Hero 直接读；模板走 PartyAgentOrigin 链，与 IsTemplateFriendly 同口径）。</summary>
        private static Clan ClanOf(Hero hero, Agent agent)
        {
            if (hero?.Clan != null) return hero.Clan;
            try
            {
                if (agent.Origin is PartyAgentOrigin partyOrigin && partyOrigin.Party != null)
                {
                    if (partyOrigin.Party.Owner?.Clan != null) return partyOrigin.Party.Owner.Clan;
                    if (partyOrigin.Party.IsSettlement) return partyOrigin.Party.Settlement.OwnerClan;
                }
            }
            catch { }
            return null;
        }
        /// <summary>
        /// 好感度友方判定（独立于 criteria 列表，数值玩家在 config.json 自设）：
        /// 该英雄对玩家的好感（GetRelation，NPC→玩家单向）≥ Settings.FriendlyRelationThreshold 即视为友方。
        /// 仅对 Hero 生效——模板 NPC 无个人好感值，天然不命中。
        /// 好感上限 100；不需要此规则时把 FriendlyRelationThreshold 设为 101（永远达不到）。
        /// </summary>
        private static bool MeetsRelationThreshold(Hero hero)
            => hero.GetRelation(Hero.MainHero) >= Settings.Instance.FriendlyRelationThreshold;

        /// <summary>
        /// 严格同队伍（随从关系专用，语义 = 原 AgentBrain.IsPlayerTeammate）：
        /// ① 招募入队的同伴 Hero（IsPlayerCompanion）；② 当前正在玩家主队的任何 Hero
        /// （PartyBelongedTo == 玩家主队，含随队家族成员）；③ 模板士兵走
        /// PartyAgentOrigin.Party == MobileParty.MainParty。
        /// </summary>
        public static bool IsPlayerPartyMember(Agent agent)
        {
            if (agent == null || agent == Agent.Main) return false;
            var hero = (agent.Character as CharacterObject)?.HeroObject;
            if (hero != null) return IsPlayerPartyMember(hero);
            return agent.Origin is PartyAgentOrigin po
                && po.Party != null
                && MobileParty.MainParty != null
                && po.Party == MobileParty.MainParty.Party;
        }

        /// <summary>距 from 最近的在场景内队伍成员 agent（排除 from 本身；无 → null）。
        /// 🔴 2026-08-16（K1/K2 共享单管线，平权纪律）：原 PlayerMissionEventLogic 实例私有版本
        /// 提升为公共 helper——K1 血线关切（AttackTriggerMissionLogic 受击事件驱动）与 K2 犯罪关切
        /// （PlayerMissionEventLogic）同源调用，禁止两侧各抄一份。</summary>
        public static Agent FindNearestPartyMemberAgent(Agent from)
        {
            try
            {
                if (from == null || Mission.Current == null) return null;
                Agent nearest = null;
                float bestSq = float.MaxValue;
                foreach (var a in Mission.Current.Agents)
                {
                    if (a == null || !a.IsActive() || a == from) continue;
                    if (!IsPlayerPartyMember(a)) continue;
                    float d = a.Position.DistanceSquared(from.Position);
                    if (d < bestSq) { bestSq = d; nearest = a; }
                }
                return nearest;
            }
            catch { return null; }
        }

        /// <summary>严格同队伍（Hero 版本）。</summary>
        public static bool IsPlayerPartyMember(Hero hero)
        {
            if (hero == null || hero == Hero.MainHero) return false;
            if (hero.IsPlayerCompanion) return true;
            var mainParty = Hero.MainHero?.PartyBelongedTo;
            return mainParty != null && hero.PartyBelongedTo == mainParty;
        }

        /// <summary>严格同行判定（2026-08-16 用户裁定：注入看说话人身份，频道只管理回复人群）：
        /// 此刻真正在主队（PartyBelongedTo == MainParty）。
        /// 与 <see cref="IsPlayerPartyMember"/> 的区别：后者有 IsPlayerCompanion 捷径——留守家族随从
        /// （不在队伍）也算队伍成员；本方法只认实际同行，用于注入层级判定——家族频道里的队伍成员
        /// 同样 L1 全量；家族但不在队伍的成员 L4 遥距（位置/账目等队伍亲历级不注入，答"不清楚"
        /// 是正确表现，实机 2026-08-16 阿速甘案）。分兵随从走 PartySplitFlow.IsSplitPartyLeader 另行判断。
        /// 🔴 在押守卫（2026-08-16）：被俘随从 PartyBelongedToAsPrisoner 非空 = 关在牢里，
        /// 不可能同行——即使引擎残留 PartyBelongedTo 也不当队伍成员（被俘认知由
        /// BuildSelfAwareness 在押行注入，见 CompanionDetentionBehavior.GetDetentionSettlement）。</summary>
        public static bool IsInMainParty(Hero hero)
        {
            if (hero == null || hero == Hero.MainHero || !hero.IsAlive) return false;
            if (hero.PartyBelongedToAsPrisoner != null) return false;
            var mainParty = Hero.MainHero?.PartyBelongedTo;
            return mainParty != null && hero.PartyBelongedTo == mainParty;
        }

        /// <summary>同王国判定（双 null-guard：玩家无王国或对方无王国 → false）。</summary>
        private static bool SameKingdom(Kingdom other)
        {
            if (other == null) return false;
            var playerKingdom = Clan.PlayerClan?.Kingdom;
            return playerKingdom != null && other == playerKingdom;
        }

        /// <summary>criteria 列表是否含该键（大小写不敏感，玩家 config 可能写小写）。</summary>
        private static bool HasCriteria(List<string> criteria, string key)
        {
            return criteria.Any(c => string.Equals(c, key, System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>模板 NPC（无 Hero）归属链：Party.Owner?.Clan → Party.IsSettlement → Settlement.OwnerClan（NPCProfile 范式）。</summary>
        private static bool IsTemplateFriendly(Agent agent)
        {
            var criteria = Settings.Instance?.FriendlyRelationCriteria;
            if (criteria == null || criteria.Count == 0) return false;
            if (!HasCriteria(criteria, "Clan") && !HasCriteria(criteria, "Kingdom")) return false;

            Clan clan = null;
            if (agent.Origin is PartyAgentOrigin partyOrigin && partyOrigin.Party != null)
            {
                clan = partyOrigin.Party.Owner?.Clan;
                if (clan == null && partyOrigin.Party.IsSettlement)
                    clan = partyOrigin.Party.Settlement.OwnerClan;
            }
            if (clan == null) return false;

            if (HasCriteria(criteria, "Clan") && Clan.PlayerClan != null && clan == Clan.PlayerClan) return true;
            if (HasCriteria(criteria, "Kingdom") && SameKingdom(clan.Kingdom)) return true;
            return false;
        }
    }
}
