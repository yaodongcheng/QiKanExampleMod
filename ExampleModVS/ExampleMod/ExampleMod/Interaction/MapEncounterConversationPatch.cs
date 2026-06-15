using HarmonyLib;
using SandBox.Conversation.MissionLogics;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 大世界遇敌 → 自定义对话分流。
    /// ① 在 CampaignMapConversation.OpenConversation 咽喉处弹 inquiry 让玩家选「原版/新版」；
    /// ② 选新版则启用静态标志、开真对话 Mission（ConversationMissionLogic），并抑制其原版自动对话。
    /// </summary>
    [HarmonyPatch(typeof(CampaignMapConversation), nameof(CampaignMapConversation.OpenConversation))]
    public static class MapEncounterConversationPatch
    {
        private static bool _reentry = false;

        [HarmonyPrefix]
        public static bool Prefix(ConversationCharacterData playerCharacterData,
                                  ConversationCharacterData conversationPartnerData)
        {
            try
            {
                if (_reentry) return true; // 放行重入（原版分支），别再拦

                CharacterObject partnerChar = conversationPartnerData.Character;
                if (partnerChar == null) return true; // 无角色数据则放行原版

                string npcName = partnerChar.Name?.ToString() ?? "对方";

                // 结构体按值捕获入闭包
                var p = playerCharacterData;
                var q = conversationPartnerData;

                InformationManager.ShowInquiry(new InquiryData(
                    $"你和{npcName}相遇了",
                    "你想怎么和对方说话？",
                    true, true,
                    "闲聊", "对话",
                    affirmativeAction: () =>
                    {
                        MapEncounterDialogState.Active = true;
                        MapEncounterDialogState.Partner = q.Character;
                        MapEncounterDialogState.PartnerParty = q.Party;
                        // 掐掉引擎自带的 2 个护卫（位置不对），全用我们的
                        var p2 = new ConversationCharacterData(p.Character, p.Party, p.NoHorse, p.NoWeapon, p.SpawnedAfterFight, p.IsCivilianEquipmentRequiredForLeader, p.IsCivilianEquipmentRequiredForBodyGuardCharacters, noBodyguards: true);
                        var q2 = new ConversationCharacterData(q.Character, q.Party, q.NoHorse, q.NoWeapon, q.SpawnedAfterFight, q.IsCivilianEquipmentRequiredForLeader, q.IsCivilianEquipmentRequiredForBodyGuardCharacters, noBodyguards: true);
                        CampaignMission.OpenConversationMission(p2, q2);
                    },
                    negativeAction: () =>
                    {
                        // 原版分支：直调底层绕开本补丁的 Prefix
                        _reentry = true;
                        try { Campaign.Current.ConversationManager.OpenMapConversation(p, q); }
                        finally { _reentry = false; }
                    }));
                return false; // inquiry 异步，统一同步拦掉
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[MapConvPatch] {ex}");
                return true; // 出错放行原版
            }
        }
    }

    /// <summary>
    /// 抑制「我们的遭遇 mission」中原版 ConversationMissionLogic 的自动对话与自动结束。
    /// gate 全靠静态 Active 标志；仅对我们的 mission 生效，不误伤城镇会面等其它 OpenConversationMission。
    /// </summary>
    [HarmonyPatch(typeof(ConversationMissionLogic), "OnMissionTick")]
    public static class SuppressVanillaConversationMissionPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            // Active → return false 跳过原版 tick（自动对话 + 自动结束一并掐掉）
            return !MapEncounterDialogState.Active;
        }
    }
}
