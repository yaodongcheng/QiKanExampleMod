using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Library;

namespace ExampleMod
{
    public class BubbleSayNeaybyVM : ViewModel
    {

        private MBBindingList<BubbleSayVM> _bubbles;
        public BubbleSayNeaybyVM()
        {
            Bubbles = new MBBindingList<BubbleSayVM>();
        }
        [DataSourceProperty]
        public MBBindingList<BubbleSayVM> Bubbles
        {
            get => _bubbles;
            set
            {
                if (value != _bubbles)
                {
                    _bubbles = value;
                    OnPropertyChangedWithValue(value, "Bubbles");
                }
            }
        }
        public void AddBubble(BubbleSayVM bubble)
        {
            if (Bubbles == null)
            {
                return;
            }
            if (!Bubbles.Contains(bubble))
            {
                Bubbles.Add(bubble);
            }
        }

        // 移除气泡的方法
        public void RemoveBubble(BubbleSayVM bubble)
        {
            if (Bubbles == null)
            {
                return;
            }
            if (Bubbles.Contains(bubble))
            {
                Bubbles.Remove(bubble);
            }
        }
    }

}
