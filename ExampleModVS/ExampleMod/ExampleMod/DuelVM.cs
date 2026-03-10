using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ExampleMod
{
    public class DuelVM: TaleWorlds.Library.ViewModel
    {
        private bool _isDuelActive;
        private string _fighter1Name;
        private string _fighter2Name;
        private float _fighter1HpWidth;
        private float _fighter2HpWidth;
        private string _fighter1HpText;
        private string _fighter2HpText;

        // 绑定 XML 的 IsVisible
        [DataSourceProperty]
        public bool IsDuelActive
        {
            get => _isDuelActive;
            set
            {
                if (_isDuelActive != value)
                {
                    _isDuelActive = value;
                    OnPropertyChanged("IsDuelActive");
                }
            }
        }

        [DataSourceProperty] public string Fighter1Name { get => _fighter1Name; set { _fighter1Name = value; OnPropertyChanged(); } }
        [DataSourceProperty] public string Fighter2Name { get => _fighter2Name; set { _fighter2Name = value; OnPropertyChanged(); } }

        // 我们用 float 也就是像素宽度来控制血条
        [DataSourceProperty] public float Fighter1HpWidth { get => _fighter1HpWidth; set { _fighter1HpWidth = value; OnPropertyChanged(); } }
        [DataSourceProperty] public float Fighter2HpWidth { get => _fighter2HpWidth; set { _fighter2HpWidth = value; OnPropertyChanged(); } }

        [DataSourceProperty] public string Fighter1HpText { get => _fighter1HpText; set { _fighter1HpText = value; OnPropertyChanged(); } }
        [DataSourceProperty] public string Fighter2HpText { get => _fighter2HpText; set { _fighter2HpText = value; OnPropertyChanged(); } }

        // 构造函数，默认隐藏
        public DuelVM()
        {
            IsDuelActive = false;
        }

        // 刷新数据的辅助方法，每一帧都会被 View 调用
        public void UpdateStats(Agent a1, Agent a2)
        {
            if (a1 == null || a2 == null) return;

            // 更新名字
            Fighter1Name = a1.Name;
            Fighter2Name = a2.Name;

            // 计算血条宽度 (假设满血是 300 像素宽)
            float maxWidth = 300f;
            Fighter1HpWidth = (a1.Health / a1.HealthLimit) * maxWidth;
            Fighter2HpWidth = (a2.Health / a2.HealthLimit) * maxWidth;

            // 更新文本
            Fighter1HpText = $"{a1.Health:F0}/{a1.HealthLimit:F0}";
            Fighter2HpText = $"{a2.Health:F0}/{a2.HealthLimit:F0}";
        }
    }
}
