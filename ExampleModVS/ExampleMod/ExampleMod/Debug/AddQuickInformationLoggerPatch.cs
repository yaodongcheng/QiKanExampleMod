using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Harmony Prefix on MBInformationManager.AddQuickInformation().
    /// 将所有屏幕上方快速提示自动写入 DebugLogger，方便从日志还原玩家体验。
    /// 搜 [AddQuickInformation] 即可看到全部 toast 通知。
    /// </summary>
    [HarmonyPatch(typeof(MBInformationManager), "AddQuickInformation")]
    public static class AddQuickInformationLoggerPatch
    {
        [HarmonyPrefix]
        public static void Prefix(TextObject message)
        {
            try
            {
                if (message == null)
                    return;

                string textStr = message.ToString();
                if (string.IsNullOrEmpty(textStr))
                    return;

                DebugLogger.Log($"[AddQuickInformation] \"{textStr}\"");
            }
            catch
            {
                // 日志系统绝不能影响游戏正常运行
            }
        }
    }
}
