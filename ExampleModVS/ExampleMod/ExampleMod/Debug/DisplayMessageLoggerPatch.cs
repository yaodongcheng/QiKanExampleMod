using HarmonyLib;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Harmony Postfix on InformationManager.DisplayMessage().
    /// 将所有玩家可见的 HUD 消息自动写入 DebugLogger，方便从日志还原玩家体验。
    /// 搜 [DisplayMessage] 即可看到全部 HUD 消息。
    /// </summary>
    [HarmonyPatch(typeof(InformationManager), nameof(InformationManager.DisplayMessage))]
    public static class DisplayMessageLoggerPatch
    {
        [HarmonyPostfix]
        public static void Postfix(InformationMessage message)
        {
            try
            {
                if (message == null || string.IsNullOrEmpty(message.Information))
                    return;

                string text = message.Information;

                // 颜色附注：非默认白色时标注颜色，方便判断消息性质（红=警告/坏消息，绿=好消息等）
                string colorNote = "";
                if (message.Color != Color.White)
                {
                    // 用 RGB 分量近似判断常见颜色名
                    if (message.Color.Red > 0.9f && message.Color.Green < 0.3f && message.Color.Blue < 0.3f)
                        colorNote = " [Red]";
                    else if (message.Color.Green > 0.9f && message.Color.Red < 0.3f && message.Color.Blue < 0.3f)
                        colorNote = " [Green]";
                    else if (message.Color.Red > 0.9f && message.Color.Green > 0.7f && message.Color.Blue < 0.3f)
                        colorNote = " [Yellow]";
                    else if (message.Color.Blue > 0.9f && message.Color.Red < 0.3f && message.Color.Green < 0.3f)
                        colorNote = " [Blue]";
                    else if (message.Color.Red < 0.4f && message.Color.Green < 0.4f && message.Color.Blue < 0.4f)
                        colorNote = " [Gray]";
                    else
                        colorNote = $" [RGB({message.Color.Red:F1},{message.Color.Green:F1},{message.Color.Blue:F1})]";
                }

                string categoryNote = !string.IsNullOrEmpty(message.Category)
                    ? $" | category={message.Category}"
                    : "";

                DebugLogger.Log($"[DisplayMessage]{colorNote} \"{text}\"{categoryNote}");
            }
            catch
            {
                // 日志系统绝不能影响游戏正常运行
            }
        }
    }
}
