using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace LivingWorldNpcs
{
       public class PlayerResources
    {
        public int PersonalGold; //个人金钱
        public int FactionGold; //势力资金
        public float Reputation; //善名
        public float Notoriety; //恶名
        public float SocialRelation; //好感度，但是还得看和谁的？
                                     // ... 其他资源
        Hero targetHero;
        // --- 复杂对象资源 (列表) ---
        // 保存引用，以便在UI中显示名字和做逻辑处理
        public List<ItemRosterElement> InventoryItems = new List<ItemRosterElement>();
        public List<Settlement> OwnedSettlements = new List<Settlement>();
        public List<Hero> Prisoners = new List<Hero>();

        public PlayerResources(Hero hero)
        {
            targetHero = hero;
            //玩家在谈判开始时候的资源，不严格等于当前的资源，因为可能会被修改
            PersonalGold = Hero.MainHero.Gold;
            Reputation = 50;
            Notoriety = 20;
            SocialRelation = (float)targetHero?.GetRelationWithPlayer();   // CharacterRelation 目前就取和当前正在互动的Npc的好感吧
        }
        public float GetResourceAmount(NegotiationCostType type)
        {
            switch (type)
            {
                case NegotiationCostType.PersonalGold: return PersonalGold;
                case NegotiationCostType.FactionGold: return FactionGold;
                case NegotiationCostType.Reputation: return Reputation;
                case NegotiationCostType.Notoriety: return Notoriety; // 恶名通常是消耗品吗？暂定是可以利用的资源
                case NegotiationCostType.SocialRelation: return SocialRelation;
                case NegotiationCostType.CityProsperity: return 0; // 暂不支持直接交易繁荣度
                default: return 0;
            }
        }
    }
}
