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

        /// <summary>严格同队伍（Hero 版本）。</summary>
        public static bool IsPlayerPartyMember(Hero hero)
        {
            if (hero == null || hero == Hero.MainHero) return false;
            if (hero.IsPlayerCompanion) return true;
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
