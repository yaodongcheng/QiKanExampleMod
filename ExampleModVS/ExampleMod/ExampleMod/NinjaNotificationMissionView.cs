using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.Data;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace ExampleMod
{
    public static class NinjaNotificationManager
    {
        private static GauntletLayer _layer;
        private static NinjaNotificationVM _vm;
        private static IGauntletMovie _movie;

        public static void Show(string text, Action onConfirm)
        {
            // 1. 如果当前已有显示的 UI，先关闭，避免重叠
            Close();

            try
            {
                // 2. 初始化 ViewModel，传入 Close 方法作为关闭回调
                _vm = new NinjaNotificationVM(text, onConfirm, Close);

                // 3. 创建图层，优先级 100 保证在大多数UI之上
                _layer = new GauntletLayer(100, "NinjaNotificationLayer");

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
