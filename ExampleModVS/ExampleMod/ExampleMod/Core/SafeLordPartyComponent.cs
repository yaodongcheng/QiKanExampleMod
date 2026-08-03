using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace LivingWorldNpcs
{
    public class SafeLordPartyComponent : PartyComponent
    {
        // ⚠️ 必须存档：类型注册只解决"读档能解析类型"，字段不标 [SaveableField] 读档后为 null
        //（曾实测：坐牢存档→读档→get_HomeSettlement NRE，_leader 为 null。原版 LordPartyComponent 同款 [SaveableField] Hero 字段）
        [SaveableField(1)]
        private Hero _leader;

        public SafeLordPartyComponent(Hero leader)
        {
            _leader = leader;
        }


        // 【关键修复2】必须返回一个名称，否则UI显示会报错
        // 读档兜底：旧档无字段数据时 _leader==null，返回空名（引擎可容忍，返回 null TextObject 会崩）
        public override TextObject Name => _leader != null ? _leader.Name : new TextObject("");

        // 【关键修复3】必须返回一个家乡定居点。
        // 如果英雄没有家乡，就默认给全图第一个定居点，防止引擎读取null崩溃
        public override Settlement HomeSettlement
        {
            get
            {
                if (_leader == null) return Settlement.All.FirstOrDefault();
                return _leader.HomeSettlement ?? _leader.Clan?.HomeSettlement ?? Settlement.All.FirstOrDefault();
            }
        }

        public override Hero PartyOwner => _leader;
        public override Hero Leader => _leader;  // MobileParty.LeaderHero 读的是 Leader，不是 PartyOwner

#if !MB2_V1212
        public override Banner GetDefaultComponentBanner()
        {
            return _leader?.ClanBanner;
        }
#endif

        // 可选：如果你希望这个部队算作玩家家族的部队
        // 这个属性通常由 Base 类处理，但为了保险起见，我们不做额外修改，保持默认即可
    }
}
