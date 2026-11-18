using HarmonyLib;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 恢复原有屏幕消息日志镜像；保留本地化后的原文，不改消息和显示行为。
    /// 使用Harmony而非订阅DisplayMessageInternal，避免InformationManager.Clear清掉订阅。
    /// </summary>
    [HarmonyPatch(typeof(InformationManager), nameof(InformationManager.DisplayMessage), new[] { typeof(InformationMessage) })]
    public static class DisplayMessageLoggerPatch
    {
        [HarmonyPostfix]
        public static void Postfix(InformationMessage message)
        {
            try
            {
                if (message == null || string.IsNullOrEmpty(message.Information)) return;

                string colorNote = "";
                if (message.Color != Color.White)
                {
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

                string categoryNote = !string.IsNullOrEmpty(message.Category) ? $" | category={message.Category}" : "";
                DebugLogger.Log($"[DisplayMessage]{colorNote} \"{message.Information}\"{categoryNote}");
            }
            catch
            {
                // 日志镜像失败不能影响原消息显示；DebugLogger自身已有IO容错，不递归发屏幕消息。
            }
        }
    }
}
