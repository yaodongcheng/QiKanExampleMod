using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.Data;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 2026-08-15（密信通知）：私聊（密信）新消息的 ninjareport 形式通知。
    /// 与旧 NinjaNotificationManager 的关键差异：
    /// ① Mission 内可弹（旧的在 Mission 被 guard——2026-08-11 全屏拦鼠标教训，见 pitfalls.md）；
    /// ② 输入 mask = Mouse 依赖引擎 hit-test 门控（ScreenManager.EarlyUpdate：层只在鼠标位于层内
    ///    DoNotAcceptEvents=false 的 widget 矩形时获得鼠标输入）→ 圆环小矩形外场景输入不被吞；
    ///    ⚠️ 此为未验证假设（审查 P0-1）：实机若复现拦鼠标，降级 = Mission 内不弹 / mask 降 Invalid；
    /// ③ 自动消失（10s）兜底，防「环永久挂着」复现 2026-08-11 局面；
    /// ④ 点击回调先 CanOpen() 再开面板（审查 P2-3：战斗中点击通知不丢密信——失败保持通知不关）。
    /// 驱动：ImChatView.Tick 每帧调用 <see cref="Tick"/>（自动消失计时；无独立 MissionView）。
    /// </summary>
    public static class ImSecretNotifyManager
    {
        private const float AutoDismissSeconds = 10f;

        private static GauntletLayer _layer;
        private static ImSecretNotifyVM _vm;
#if !MB2_V1212
        private static GauntletMovieIdentifier _movie;
#else
        private static IGauntletMovie _movie;
#endif
        private static float _lifeTimer;

        public static bool IsShown => _layer != null;

        /// <summary>显示密信通知圆环（ninjareport 形式：右缘圆环 + hover 展开摘要 + 关闭 X）。
        /// onConfirm = 点击圆环回调（内部已处理 CanOpen 失败不关通知的纪律，见 ImSecretNotifyVM）。</summary>
        public static void Show(string text, Action onConfirm)
        {
            // 已显示的旧通知先关（防重叠）
            Close();

            try
            {
                _vm = new ImSecretNotifyVM(text, onConfirm, Close);
                _layer = V.NewLayer(400, "ImSecretNotifyLayer");
                _movie = _layer.LoadMovie("ImSecretNotify", _vm);
                // 🔴 mask = Mouse（按钮+滚轮）——引擎 hit-test 门控下只吞圆环矩形内的点击；
                // 键盘不拦（无文本输入），玩家移动/攻击在圆环外照常
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.Mouse);
                if (ScreenManager.TopScreen != null)
                    ScreenManager.TopScreen.AddLayer(_layer);
                _lifeTimer = 0f;
                DebugLogger.Log($"[ImSecretNotify] 显示密信通知: {text}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImSecretNotify] 显示失败: {ex.Message}");
                Close();
            }
        }

        /// <summary>每帧驱动（ImChatView.Tick 调用）：自动消失兜底（审查 P0-1/P2-1）。</summary>
        public static void Tick(float dt)
        {
            if (_layer == null) return;
            _lifeTimer += dt;
            if (_lifeTimer >= AutoDismissSeconds)
            {
                DebugLogger.Log("[ImSecretNotify] 超时自动消失");
                Close();
            }
        }

        public static void Close()
        {
            if (_layer != null)
            {
                try
                {
                    if (_movie != null)
                    {
                        _layer.ReleaseMovie(_movie);
                        _movie = null;
                    }
                    if (ScreenManager.TopScreen != null)
                        ScreenManager.TopScreen.RemoveLayer(_layer);
                    _layer.InputRestrictions.ResetInputRestrictions();
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[ImSecretNotify] 关闭失败: {ex.Message}");
                }
                _layer = null;
            }
            _vm = null;
        }
    }
}
