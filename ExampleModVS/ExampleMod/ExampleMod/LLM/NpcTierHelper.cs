using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// NPC 身份/位阶共享判定（2026-08-17 计划 world-background-auto-summary.md §6，消除重复，铁律 18）：
    /// 玩家与 NPC 平权、判定公式共享单管线——IsNobleTier 从 NPCProfile 私有方法抽成共享静态。
    /// </summary>
    public static class NpcTierHelper
    {
        /// <summary>领主级判定（身份深度用）：领主 / 阵营领袖 / 家族族长。null 安全（模板 NPC 无 Hero → false）。</summary>
        public static bool IsNoble(Hero hero)
        {
            if (hero == null) return false;
            if (hero.IsLord || hero.IsFactionLeader) return true;
            var clan = hero.Clan;
            return clan != null && clan.Leader == hero;
        }

        /// <summary>对方（玩家）是否家族族长：Clan.Leader == MainHero。</summary>
        public static bool IsPlayerClanLeader()
        {
            var player = Hero.MainHero;
            if (player == null) return false;
            return player.Clan != null && player.Clan.Leader == player;
        }

        /// <summary>对方（玩家）是否本 NPC 的队伍队长（随从语境恒真——NPC 在玩家队伍/家族体系内，
        /// 玩家必然是其队长，无需再查 party 归属；队伍体系判定 = IsPlayerPartyMember（含分兵随从））。
        /// 路人/场外 NPC（不在玩家队伍）→ false（不说「你的队长」）。</summary>
        public static bool IsPlayerPartyCaptainFor(Hero npc)
        {
            if (npc == null || Hero.MainHero == null) return false;
            if (npc == Hero.MainHero) return false;
            if (FriendlinessHelper.IsPlayerPartyMember(npc)) return true;
            return npc.Clan != null && Hero.MainHero.Clan != null && npc.Clan == Hero.MainHero.Clan;
        }
    }
}
