using HarmonyLib;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Harmony Prefix on InformationManager.ShowInquiry().
    /// Logs all inquiry dialogs shown to the player so we can verify UI text from logs.
    /// Search [ShowInquiry] to see all inquiry popups.
    /// </summary>
    [HarmonyPatch(typeof(InformationManager), nameof(InformationManager.ShowInquiry))]
    public static class ShowInquiryLoggerPatch
    {
        [HarmonyPrefix]
        public static void Prefix(InquiryData data, bool pauseGameActiveState, bool prioritize)
        {
            try
            {
                if (data == null) return;

                string title = data.TitleText ?? "(no title)";
                string body = data.Text ?? "(no text)";
                string btnOk = data.IsAffirmativeOptionShown
                    ? (string.IsNullOrEmpty(data.AffirmativeText) ? "OK" : data.AffirmativeText)
                    : "(hidden)";
                string btnCancel = data.IsNegativeOptionShown
                    ? (string.IsNullOrEmpty(data.NegativeText) ? "Cancel" : data.NegativeText)
                    : "(hidden)";

                DebugLogger.Log($"[ShowInquiry] \"{title}\" | \"{body}\" | Btn1=\"{btnOk}\" Btn2=\"{btnCancel}\"");
            }
            catch
            {
                // 日志系统绝不能影响游戏正常运行
            }
        }
    }
}
