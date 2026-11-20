using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 511 自建对话面板打开时，把**原版血条 HUD** 收起来（2026-11-20 用户裁定方案 A）。
    ///
    /// 为什么不改 `Mission.Mode`：
    ///   原版是靠 `Mission.Mode == Conversation` 让引擎自己收起整组 HUD 的，但 `Mission.Mode` 是只读的；
    ///   而反射硬写它等于让引擎"以为在对话"—— 那正是问题 49（谈完话 NPC 定身）的来源地，绝对不能碰。
    ///
    /// 做法：找到 `MainAgentHUD` 这个 movie 的根控件，把 `IsVisible` 关掉；对话结束再打开。
    /// 只动 Gauntlet 的显示层，不碰引擎状态；任何一步失败都静默跳过（血条照旧显示，绝不崩）。
    /// </summary>
    internal static class MissionHudVisibility
    {
        public static void SetVisible(bool visible)
        {
            try
            {
                var layers = SafeLayers();
                if (layers == null) return;

                int changed = 0;
                for (int i = 0; i < layers.Count; i++)
                {
                    var layer = layers[i];
                    if (layer == null) continue;
                    foreach (var (movie, vm) in GauntletUiIntrospect.GetMovies(layer))
                    {
                        // 血条 HUD 的数据源类型是 MissionAgentStatusVM —— 按类型名认，比猜 movie 名可靠。
                        string vmType = vm?.GetType().Name ?? "";
                        if (vmType.IndexOf("AgentStatus", StringComparison.Ordinal) < 0) continue;
                        var root = GauntletUiIntrospect.GetRootWidget(movie);
                        if (root == null) continue;
                        if (root.IsVisible != visible) root.IsVisible = visible;
                        changed++;
                    }
                }
                DebugLogger.Log($"[StoryDialogueUI] hud visible={visible} roots={changed}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[StoryDialogueUI] hud visibility failed: " + ex.Message);
            }
        }

        private static List<ScreenLayer> SafeLayers()
        {
            try { return ScreenManager.SortedLayers; }
            catch { return null; }
        }

    }
}