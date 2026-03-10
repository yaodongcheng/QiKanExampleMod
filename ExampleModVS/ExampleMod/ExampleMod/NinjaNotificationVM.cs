using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Library;

namespace ExampleMod
{
    public class NinjaNotificationVM : ViewModel
    {
        private bool _isHovered;
        private bool _isCloseHovered;
        private string _reportText;
        private readonly Action _onConfirmAction;
        private readonly Action _onCloseAction;
        

        public NinjaNotificationVM(string text, Action onConfirm, Action onClose)
        {
            _reportText = text;
            _onConfirmAction = onConfirm;
            _onCloseAction = onClose;
            _isHovered = false;
        }

        // --- 数据绑定属性 ---
        [DataSourceProperty] 
        public bool ShouldExpand
        {
            get => _isHovered || _isCloseHovered;
        }

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

        // 鼠标移入圆圈
        public void ExecuteOnHoverBegin()
        {
            IsHovered = true;
        }

        // 鼠标移出圆圈
        public void ExecuteOnHoverEnd()
        {
            // 这里可以加一点延迟，防止鼠标滑向文本框时瞬间消失
            // 但为了简单，目前设为直接消失
            IsHovered = false;
        }
        public void ExecuteOnCloseHoverBegin()
        {
            IsCloseHovered = true;
        }

        // 鼠标移出圆圈
        public void ExecuteOnCloseHoverEnd()
        {
            // 这里可以加一点延迟，防止鼠标滑向文本框时瞬间消失
            // 但为了简单，目前设为直接消失
            IsCloseHovered = false;
        }

        // 点击圆圈（确认/下一步）
        public void ExecuteSelect()
        {
            _onConfirmAction?.Invoke(); // 执行你的“召唤忍者”逻辑
            _onCloseAction?.Invoke();   // 关闭 UI
        }

        // 点击右上角 X（忽略）
        public void ExecuteClose()
        {
            _onCloseAction?.Invoke();   // 仅关闭 UI
        }
    }
}
