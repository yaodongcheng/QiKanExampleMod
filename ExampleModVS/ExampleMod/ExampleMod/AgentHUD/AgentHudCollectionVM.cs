using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    public class AgentHudCollectionVM : ViewModel
    {
        private MBBindingList<AgentHudVM> _huds;

        public AgentHudCollectionVM()
        {
            Huds = new MBBindingList<AgentHudVM>();
        }

        [DataSourceProperty]
        public MBBindingList<AgentHudVM> Huds
        {
            get => _huds;
            set
            {
                if (value != _huds)
                {
                    _huds = value;
                    OnPropertyChangedWithValue(value, "Huds");
                }
            }
        }

        public void AddHud(AgentHudVM hud)
        {
            if (Huds == null) return;
            if (!Huds.Contains(hud))
            {
                Huds.Add(hud);
            }
        }

        public void RemoveHud(AgentHudVM hud)
        {
            if (Huds == null) return;
            if (Huds.Contains(hud))
            {
                Huds.Remove(hud);
            }
        }
    }
}
