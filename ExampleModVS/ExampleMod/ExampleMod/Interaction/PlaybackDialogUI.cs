using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// PlaybackDialog 面板挂载器（T7，2026-08-30）——
    /// 先例 = StoryEngine.SetupUI（V.NewLayer(1000) + LoadMovie + ScreenBase.AddLayer + SetInputRestrictions）；
    /// 通用化：TopScreen（Mission/Map/Menu 三形态都能挂——演绎三段统一，00 v3 §2.3）；
    /// 🔴 摘层守卫（wheels.d/ui.md）：HasLayer + try/catch 全程兜底——引擎无 IsFinalized API（1.2.12 HeldFinalizeNRE 即此），二重摘层 = 二重 OnFinalize NRE
    /// 🔴 过场式全控（05:89 输入掩码）：打开 SetInputRestrictions(true, All)，关闭必须复位（_lastCompactMask 教训——W5 播放器负责复位）
    /// </summary>
    public static class PlaybackDialogUI
    {
        private static GauntletLayer _layer;
        private static ScreenBase _parentScreen;

        public static PlaybackDialogVM VM { get; } = new PlaybackDialogVM();

        public static bool IsOpen => _layer != null;

        /// <summary>打开面板（幂等：已开 = no-op）。TopScreen 为 null = 日志不崩（铁律 1）。</summary>
        public static void Open()
        {
            try
            {
                if (_layer != null) return;
                var top = ScreenManager.TopScreen as ScreenBase;
                if (top == null) { DebugLogger.Log("[PlaybackDialog] 无顶层屏幕（不显示）"); return; }

                _parentScreen = top;
                _layer = V.NewLayer(1000);                       // 高优先级（StoryEngine 先例）
                _layer.LoadMovie("PlaybackDialog", VM);
                _parentScreen.AddLayer(_layer);
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                DebugLogger.Log($"[PlaybackDialog] 打开（挂在 {top.GetType().Name}）");
            }
            catch (Exception e)
            {
                DebugLogger.Log($"[PlaybackDialog] 打开失败（不崩）: {e.Message}");
                _layer = null;
            }
        }

        /// <summary>关闭面板（摘层守卫 = HasLayer 判空 + 全段 try/catch；引擎无 IsFinalized——二重摘层防护靠判空）</summary>
        public static void Close()
        {
            try
            {
                if (_layer == null) return;
                var layer = _layer;
                _layer = null;
                var parent = _parentScreen;
                _parentScreen = null;

                try { layer.InputRestrictions.SetInputRestrictions(false, InputUsageMask.All); } catch (Exception e) { DebugLogger.Log($"[PlaybackDialog] 输入限制复位失败: {e.Message}"); }
                if (parent != null)
                {
                    try { parent.RemoveLayer(layer); } catch (Exception e) { DebugLogger.Log($"[PlaybackDialog] 摘层失败: {e.Message}"); }
                }
            }
            catch (Exception e)
            {
                DebugLogger.Log($"[PlaybackDialog] 关闭异常（不崩）: {e.Message}");
            }
        }

        /// <summary>清理（游戏结束/新档/切换场景时需要：只清引用防跨场景残留）</summary>
        public static void Reset()
        {
            Close();
        }
    }
}
