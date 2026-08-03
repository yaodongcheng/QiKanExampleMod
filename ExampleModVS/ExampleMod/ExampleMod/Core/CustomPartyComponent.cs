using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 泛型 party 组件 — 用于无真实 Hero leader 的事件 party（通用匪帮、不知名刺客等）。
    /// 引擎要求 PartyComponent 必须返回非 null 的 Name 和 HomeSettlement。
    /// ⚠️ 字段必须存档（同 SafeLordPartyComponent）：读档后字段缺失为 null，
    /// Name/HomeSettlement 各自带兜底防引擎读取 null 崩溃。
    /// </summary>
    public class CustomPartyComponent : PartyComponent
    {
        [SaveableField(1)]
        private Settlement _homeSettlement;
        [SaveableField(2)]
        private string _displayName;

        public CustomPartyComponent(Settlement homeSettlement, string displayName)
        {
            _homeSettlement = homeSettlement;
            _displayName = displayName ?? "不明部队";
        }

        /// <summary>引擎要求必须返回非 null 名称，否则 UI 崩溃。</summary>
        public override TextObject Name => new TextObject(_displayName ?? "");

        /// <summary>返回事件发生的定居点作为家乡，保证引擎不读 null。</summary>
        public override Settlement HomeSettlement
        {
            get
            {
                return _homeSettlement
                    ?? Settlement.All.FirstOrDefault();
            }
        }

        /// <summary>泛型 party 无 Owner。</summary>
        public override Hero PartyOwner => null;

#if !MB2_V1212
        public override Banner GetDefaultComponentBanner()
        {
            return null;
        }
#endif
    }
}
