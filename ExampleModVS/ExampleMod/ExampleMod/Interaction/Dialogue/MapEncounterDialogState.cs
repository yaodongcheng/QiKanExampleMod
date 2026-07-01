using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 静态协调标志：在咽喉补丁与 InteractionMissionView 之间传递
    /// 「这是我们的遭遇对话 mission」信号。
    /// </summary>
    public static class MapEncounterDialogState
    {
        /// <summary>我们的遭遇 mission 生命周期内为 true</summary>
        public static bool Active;

        /// <summary>对方角色，用于在 mission 里精确定位 partner Agent</summary>
        public static CharacterObject Partner;

        /// <summary>对方 Party，用于取 MemberRoster 生成护卫</summary>
        public static PartyBase PartnerParty;

        public static void Clear()
        {
            Active = false;
            Partner = null;
            PartnerParty = null;
        }
    }
}
