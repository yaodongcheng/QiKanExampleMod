using System;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 2026-08-15（密信通知）：ImSecretNotify.xml 的 VM（ninjareport 形态，照 NinjaNotificationVM 抄）。
    /// 差异：点击确认回调内先查 ImChatView.CanOpen()——失败（战斗中/系统弹窗）**保持通知不关**
    /// （审查 P2-3：通知消失而面板没开 = 丢密信）；成功才开面板并关闭通知。
    /// hover 展开照旧：Command.HoverBegin/End 绑定（hit-test 门控下鼠标在圆环上时层有输入）。
    /// </summary>
    public class ImSecretNotifyVM : ViewModel
    {
        private bool _isHovered;
        private bool _isCloseHovered;
        private string _reportText;
        private readonly Action _onConfirmAction;
        private readonly Action _onCloseAction;

        public ImSecretNotifyVM(string text, Action onConfirm, Action onClose)
        {
            _reportText = text;
            _onConfirmAction = onConfirm;
            _onCloseAction = onClose;
            _isHovered = false;
        }

        // --- 数据绑定属性 ---
        [DataSourceProperty]
        public bool ShouldExpand => _isHovered || _isCloseHovered;

        [DataSourceProperty]
        public bool IsHovered
        {
            get => _isHovered;
            set
            {
                if (value != _isHovered)
                {
                    _isHovered = value;
                    OnPropertyChangedWithValue(value, nameof(IsHovered));
                    OnPropertyChanged(nameof(ShouldExpand));
                }
            }
        }

        public bool IsCloseHovered
        {
            get => _isCloseHovered;
            set
            {
                if (value != _isCloseHovered)
                {
                    _isCloseHovered = value;
                    OnPropertyChangedWithValue(value, nameof(IsCloseHovered));
                    OnPropertyChanged(nameof(ShouldExpand));
                }
            }
        }

        [DataSourceProperty]
        public string ReportText
        {
            get => _reportText;
            set
            {
                if (value != _reportText)
                {
                    _reportText = value;
                    OnPropertyChangedWithValue(value, nameof(ReportText));
                }
            }
        }

        // --- 命令绑定 (XML Command.*) ---

        // 鼠标移入圆环
        public void ExecuteOnHoverBegin()
        {
            IsHovered = true;
        }

        // 鼠标移出圆环
        public void ExecuteOnHoverEnd()
        {
            IsHovered = false;
        }

        public void ExecuteOnCloseHoverBegin()
        {
            IsCloseHovered = true;
        }

        // 鼠标移出关闭按钮
        public void ExecuteOnCloseHoverEnd()
        {
            IsCloseHovered = false;
        }

        /// <summary>点击圆环（确认）：🔴 先 CanOpen() 再开面板——失败保持通知不关（P2-3），成功才关。</summary>
        public void ExecuteSelect()
        {
            try
            {
                if (ImChatView.CanOpen())
                {
                    _onConfirmAction?.Invoke();
                    _onCloseAction?.Invoke();
                }
                else
                {
                    DebugLogger.Log("[ImSecretNotify] 点击时 CanOpen=false（战斗中/弹窗），通知保持");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImSecretNotify] 点击处理异常: {ex.Message}");
            }
        }

        // 点击右上角 X（忽略）
        public void ExecuteClose()
        {
            _onCloseAction?.Invoke();
        }
    }
}
