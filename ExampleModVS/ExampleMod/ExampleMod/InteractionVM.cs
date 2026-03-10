using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ExampleMod
{
    public class InteractionItemVM : ViewModel
    {

        private string _actionText;
        private string _keyText;
        

        // 构造函数
        public InteractionItemVM(string actionText, string keyText)
        {
            _actionText = actionText;
            _keyText = keyText;
            
        }

        // 对应 XML 中的 Text="@ActionText"
        [DataSourceProperty]
        public string ActionText
        {
            get => _actionText;
            set
            {
                if (value != _actionText)
                {
                    _actionText = value;
                    OnPropertyChangedWithValue(value, nameof(ActionText));
                }
            }
        }

        // 对应 XML 中的 Text="@KeyText"
        [DataSourceProperty]
        public string KeyText
        {
            get => _keyText;
            set
            {
                if (value != _keyText)
                {
                    _keyText = value;
                    OnPropertyChangedWithValue(value, nameof(KeyText));
                }
            }
        }

        // 你可以在这里添加 Execute 方法，用于处理玩家按下按键后的逻辑
        public void Execute()
        {
            // 处理点击或按键逻辑
        }
    }

    public class InteractionVM : ViewModel
    {
        private bool _isVisible;
        private string _targetName;
        private MBBindingList<InteractionItemVM> _interactionList;

        public InteractionVM()
        {
            // 初始化列表，必须实例化，否则报错
            _interactionList = new MBBindingList<InteractionItemVM>();
            _targetName = "";
            _isVisible = true;
        }

        // 对应 XML 中的 Text="@TargetName"
        [DataSourceProperty]
        public string TargetName
        {
            get => _targetName;
            set
            {
                if (value != _targetName)
                {
                    _targetName = value;
                    OnPropertyChangedWithValue(value, nameof(TargetName));
                }
            }
        }

        [DataSourceProperty]
        public bool IsVisible
        {
            get => _isVisible;
            set { if (value != _isVisible) { _isVisible = value; OnPropertyChangedWithValue(value, "IsVisible"); } }
        }



        // 对应 XML 中的 DataSource="{InteractionList}"
        [DataSourceProperty]
        public MBBindingList<InteractionItemVM> InteractionList
        {
            get => _interactionList;
            set
            {
                if (value != _interactionList)
                {
                    _interactionList = value;
                    OnPropertyChangedWithValue(value, nameof(InteractionList));
                }
            }
        }

        // === 辅助方法：用于游戏逻辑调用 ===

        // 更新目标名字 (例如：玩家看向了一个守卫)
        public void UpdateTarget(string name)
        {
            TargetName = name;
            //可能不同状态下，InteractionList 也要变化，暂时不变。
        }
        // 用于外部刷新数据的方法
        public void UpdateTarget(string name, List<(string action, string key)> actions)
        {
            TargetName = name;
            InteractionList.Clear();
            foreach (var act in actions)
            {
                InteractionList.Add(new InteractionItemVM(act.action, act.key));
            }
            IsVisible = true;
        }

        public void ChangeInteractionName(string oldName,string newName)
        {
            var interaction = InteractionList.FirstOrDefault(h => h.ActionText == oldName);
            if(interaction!=null)
                interaction.ActionText = newName;
        }

        public void Hide()
        {
            IsVisible = false;
        }

        // 添加一个交互选项 (例如：添加 "F - 交谈")
        public void AddInteraction(string action, string key)
        {
            InteractionList.Add(new InteractionItemVM(action, key));
        }

        // 清空所有交互
        public void ClearInteractions()
        {
            InteractionList.Clear();
        }
    }
}
