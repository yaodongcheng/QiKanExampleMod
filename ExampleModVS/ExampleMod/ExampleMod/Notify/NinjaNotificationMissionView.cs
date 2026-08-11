using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.Data;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    public static class NinjaNotificationManager
    {
        private static GauntletLayer _layer;
        private static NinjaNotificationVM _vm;
#if !MB2_V1212
        private static GauntletMovieIdentifier _movie;
#else
        private static IGauntletMovie _movie;
#endif

        public static void Show(string text, Action onConfirm)
        {
            // 🔴 Mission 内一律不弹通知圆环（2026-08-11 致命 bug 修复）：
            // 本层 SetInputRestrictions(true, InputUsageMask.Mouse) 拦截鼠标（左键攻击/右键格挡/滚轮），
            // 键盘（移动）不拦——玩家攻击 NPC → NPC 台词 → AgentSay→NearbyFeed→OnMessageArrived→
            // NotifyIncoming→Show 弹出通知 → 玩家攻击/格挡全被吞、移动正常（实机 16:15 复现，见 pitfalls.md）。
            // Mission 内消息仍在频道里（IM 面板可看），只是无圆环提醒；大地图保留原功能。
            if (Mission.Current != null) return;

            // 1. 如果当前已有显示的 UI，先关闭，避免重叠
            Close();

            try
            {
                // 2. 初始化 ViewModel，传入 Close 方法作为关闭回调
                _vm = new NinjaNotificationVM(text, onConfirm, Close);

                // 3. 创建图层，优先级 100 保证在大多数UI之上
                _layer = V.NewLayer(100, "NinjaNotificationLayer");

                // 4. 加载 XML (注意：LoadMovie 的字符串必须和 XML 文件名一致，不带 .xml 后缀)
                _movie = _layer.LoadMovie("CustomNotify", _vm);

                // 5. 设置输入限制
                // true = 接收输入, InputUsageMask.Mouse = 仅处理鼠标，不拦截键盘移动
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.Mouse);

                // 6. 添加到当前屏幕 (无论是战场还是大地图)
                if (ScreenManager.TopScreen != null)
                {
                    ScreenManager.TopScreen.AddLayer(_layer);
                }
            }
            catch (Exception ex)
            {
                // 简单的错误捕获，防止因为 UI 问题导致游戏崩溃
                Debug.Print($"[NinjaMod] Error showing notification: {ex.Message}");
                Close();
            }
        }

        /// <summary>
        /// 关闭并清理 UI
        /// </summary>
        public static void Close()
        {
            if (_layer != null)
            {
                // 移除电影和图层
                if (_movie != null)
                {
                    _layer.ReleaseMovie(_movie);
                    _movie = null;
                }

                if (ScreenManager.TopScreen != null)
                {
                    ScreenManager.TopScreen.RemoveLayer(_layer);
                }

                // 清理输入限制
                _layer.InputRestrictions.ResetInputRestrictions();
                _layer = null;
            }

            _vm = null;
        }
    }
}
