using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View;
using TaleWorlds.MountAndBlade.View.MissionViews;

namespace ExampleMod
{
    public  class DuelMissionView: MissionView
    {
        private DuelVM _dataSource;
        private GauntletLayer _layer;

        // 记录正在打架的两个人
        private Agent _currentFighter1;
        private Agent _currentFighter2;

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();

            _dataSource = new DuelVM(); // 确保你定义了这个类，且构造函数里 IsDuelActive = false

            // 3. 创建图层
            _layer = new TaleWorlds.Engine.GauntletUI.GauntletLayer(100);
            _layer.LoadMovie("DuelUI", _dataSource);


            var missionScreen = TaleWorlds.ScreenSystem.ScreenManager.TopScreen as TaleWorlds.MountAndBlade.View.Screens.MissionScreen;

            if (missionScreen != null)
            {
                missionScreen.AddLayer(_layer);
            }
            else
            {
                TaleWorlds.Library.Debug.Print("[Error] DuelUI: MissionScreen not found!");
            }

        }
        public override void OnMissionScreenTick(float dt)
        {
            base.OnMissionScreenTick(dt);

            // 只有当开启显示，且有人的时候才刷新数据
            if (_dataSource !=null && _dataSource.IsDuelActive && _currentFighter1 != null && _currentFighter2 != null)
            {
                _dataSource.UpdateStats(_currentFighter1, _currentFighter2);

                // 如果某人死了，可以自动关闭 UI (可选)
                if (!_currentFighter1.IsActive() || !_currentFighter2.IsActive())
                {
                    // _dataSource.IsDuelActive = false; // 取消注释这行可以实现死人后自动隐藏 UI
                }
            }
        }
        // === 公开方法：供你的控制台命令调用 ===
        public void StartDuelUI(Agent a1, Agent a2)
        {
            // 1. 如果 DataSource 还没创建，现在创建 (懒加载)
            if (_dataSource == null)
            {
                _dataSource = new DuelVM();
            }

            // 2. 如果 Layer 还没创建，现在创建 (懒加载)
            if (_layer == null)
            {
                _layer = new GauntletLayer(100);
                _layer.LoadMovie("DuelUI", _dataSource);

                // 使用我们要教你的通用获取屏幕方法
                var missionScreen = TaleWorlds.ScreenSystem.ScreenManager.TopScreen as TaleWorlds.MountAndBlade.View.Screens.MissionScreen;
                if (missionScreen != null)
                {
                    missionScreen.AddLayer(_layer);
                }
            }

            // 3. 设置人员并开启显示
            _currentFighter1 = a1;
            _currentFighter2 = a2;
            _dataSource.IsDuelActive = true;
        }

        public override void OnMissionScreenFinalize()
        {
            base.OnMissionScreenFinalize();
            MissionScreen.RemoveLayer(_layer);
            _dataSource = null;
            _layer = null;
        }
    }


}
