using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace ExampleMod
{
    public class SafeLordPartyComponent : PartyComponent
    {
        private readonly Hero _leader;

        public SafeLordPartyComponent(Hero leader)
        {
            _leader = leader;
        }


        // 【关键修复2】必须返回一个名称，否则UI显示会报错
        public override TextObject Name => _leader.Name;

        // 【关键修复3】必须返回一个家乡定居点。
        // 如果英雄没有家乡，就默认给全图第一个定居点，防止引擎读取null崩溃
        public override Settlement HomeSettlement
        {
            get
            {
                return _leader.HomeSettlement ?? _leader.Clan?.HomeSettlement ?? Settlement.All.FirstOrDefault();
            }
        }

        public override Hero PartyOwner => _leader;

        // 可选：如果你希望这个部队算作玩家家族的部队
        // 这个属性通常由 Base 类处理，但为了保险起见，我们不做额外修改，保持默认即可
    }
}
