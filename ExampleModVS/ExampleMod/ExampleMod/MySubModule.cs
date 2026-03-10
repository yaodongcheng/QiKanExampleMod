using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using System.IO;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.ObjectSystem;
using System.Reflection;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.Data;
using TaleWorlds.ScreenSystem;
using TaleWorlds.InputSystem;
using HarmonyLib;
using psai;
using psai.net;
using ExampleMod.StoryEngineBag;
using ExampleMod.AI;

namespace ExampleMod
{
    public class MySubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            TaleWorlds.MountAndBlade.Module.CurrentModule.AddInitialStateOption(new InitialStateOption("LoadTaikouEvents",
                new TextObject("LoadTaikouEvents", null),
                9990,
                () => { LoadEvent(); InformationManager.DisplayMessage(new InformationMessage("click message Success!")); },
                () => { return (false, null); }));


            LoadEvent();

            //harmony测试，屏蔽掉F的交互
            var harmony = new Harmony("com.ydc.ExampleMod");
            harmony.PatchAll( );

            //实例化大模型
            LLMService.Initialize("sk-db03887a984d43caaaf2d30767e81bcd");

            //加载策划表数据
            try
            {
                GameDatabase.Initialize();

                //   StoryEngine.ChangeNameBasedOnHistory();
               // SystemCommands.ChangeBgm("上呀");
            }
            catch ( Exception ex )
            {
                string errorMsg = $"[ShokuhoMod] Failed to load CSV Data!\nError: {ex.Message}";
                Debug.PrintError(errorMsg);
            }


            TaleWorlds.MountAndBlade.Module.CurrentModule.AddInitialStateOption(new InitialStateOption("TestDesignData",
               new TextObject("TestDesignData", null),
               9990,
               () => {

                   var tagPointData = GameDatabase.TagPoint.GetByID("lordshall");
                   string ChairTrans = tagPointData.GetString("ChairTrans");
                   SimpleTrans trans = new SimpleTrans();
                   StageConfig.TryParseSimpleTrans(ChairTrans,out trans);
                   InformationManager.DisplayMessage(new InformationMessage($"check csv success {ChairTrans} prase {trans.Offset.x}!")); },
               () => { return (false, null); }));

            //   List<string> moduleNames = new List<string>() { "ExampleMod" };
            //   PsaiCore.Instance.LoadSoundtrackFromProjectFile("ExampleMod");


        }

 

        private void LoadEvent()
        {
            StoryDataLoader.LoadEvents();
            var eventsMap = StoryDataLoader.eventsMap;
            InformationManager.DisplayMessage(new InformationMessage("Story Load to check!"));
            //StoryEvent testEvent = StoryDataLoader.FindEvent("EP120500","0");
            //StoryDebugHelper.PrintEventInfo(testEvent);            
        }

        protected override void OnSubModuleUnloaded()
        {
            base.OnSubModuleUnloaded();
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);
            //喝酒功能
            mission.AddMissionBehavior(new ArtisanBeerMissionView());
            //吸血功能
            mission.AddMissionBehavior(new KillMissionLogic());
            //召唤某个英雄并且和他对话功能
            mission.AddMissionBehavior(new HeroSpawnerMissionBehavior());



            //决斗UI
              mission.AddMissionBehavior(new DuelMissionView());
            //相机调试UI
            mission.AddMissionBehavior(new CameraDebuggerView());
            //相机调试UI
            mission.AddMissionBehavior(new SpringArmCameraView());
            //冒泡UI
            mission.AddMissionBehavior(new BubbleSayMissionView());
            //右下角交互区
            mission.AddMissionBehavior(new InteractionMissionView());
            //攻击触发
            mission.AddMissionBehavior(new AttackTriggerMissionLogic());
            //AI
            mission.AddMissionBehavior(new AgentAIController());

        }
        protected override void InitializeGameStarter(Game game, IGameStarter starterObject)
        {
            if (starterObject is CampaignGameStarter starter)
            {
                starter.AddBehavior(new MyBehavior());
            }
        }
        protected override void OnGameStart(Game game, IGameStarter gameStarter)
        {
            base.OnGameStart(game, gameStarter);
            if (game.GameType is Campaign)
            {
                var campaignGameStarter = (CampaignGameStarter)gameStarter;
                campaignGameStarter.AddBehavior(new DialogBehavior());
                //  campaignGameStarter.AddBehavior(new DebugBehavior());
                campaignGameStarter.AddBehavior(new GlobalVariableBehavior());

                campaignGameStarter.AddBehavior(new AIStoryGeneratorBehavior());

                if (!_hasDumped)
                {
                    //DumpAllActions();
                  //  _hasDumped = true;
                }

            }      

          


        }

        // 定义一个简单的日志路径，直接放在桌面，方便你查找
        private static string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "MyMod_Debug_Log.txt");
        private bool _hasDumped = false;


        // 1. 在主菜单加载前执行：检查 NPCCharacters (模板)
        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();

            InformationManager.DisplayMessage(new InformationMessage($"成功进入游戏主菜单界面", Color.FromUint(0xFF0000)));
        }

        private void DumpAllActions()
        {
            string outputPath = Path.Combine(BasePath.Name, "Shokuho_Actions_Dump.txt");
            List<string> outputLines = new List<string>();

            outputLines.Add($"--- 导出时间: {DateTime.Now} ---");

            // 方法 A: 尝试通过 ActionIndexCache 遍历 (适用于逻辑层动作代码)
            // Bannerlord 的 ActionIndexCache 通常是静态定义的，但我们可以尝试遍历 Monster 的 ActionSet

            // 获取人类的 Monster 定义 (Humanoid)
            // 注意: 在不同的游戏版本中，获取 Monster 的方式可能微调，通常通过 MBObjectManager
         
            
                outputLines.Add("\n--- Human Action Set (人类动作集) ---");
                // ActionSet 是一个包含动作索引的集合
                // 这里我们利用 MBActionSet 的相关功能。
                // 由于 API 限制，无法直接 foreach 所有的 Action，
                // 但我们可以通过遍历一个较大的索引范围来暴力匹配引擎内的动作名。

                outputLines.Add("正在从引擎遍历动作索引 (Index 0 - 10000)...");
                var num = MBActionSet.GetNumberOfActionSets();
                
                int foundCount = 0;
                // 假设动作索引上限为 10000 (通常只有几千个)
                for (int i = 0; i < num; i++)
                {
                    try
                    {
                        // 这是一个关键的 Engine 层函数，用于从索引反查动作名
                        // 注意：如果函数名在最新版本变动，请检查 Utilities 类
                        var action_set = MBActionSet.GetActionSetWithIndex(i);
                        string actionName = action_set.GetName();

                        if (!string.IsNullOrEmpty(actionName) && actionName != "invalid")
                        {
                            outputLines.Add($"Index {i}: {actionName}");
                            foundCount++;
                        }
                    }
                    catch
                    {
                        // 忽略越界异常
                    }
                }
                outputLines.Add($"总共找到 {foundCount} 个动作。");
        

            // 写入文件
            try
            {
                File.WriteAllLines(outputPath, outputLines);

                // 提示用户 (Message)
                InformationManager.DisplayMessage(new InformationMessage($"[Shokuho] 动作已导出至: {outputPath}", Color.FromUint(0x00FF00)));
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage($"[Shokuho] 导出失败: {ex.Message}", Color.FromUint(0xFF0000)));
            }
        }




        // 这个函数游戏运行时的每一帧都会调用
        private bool _hasPatched = false;
        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);


            // 只有当 StoryEngine 实例存在时才尝试更新
            if (StoryEngineBag.StoryEngine.Instance != null)
            {
                StoryEngineBag.StoryEngine.Instance.OnTick();
            }
            //自定义UI部分
            if (Input.IsKeyPressed(InputKey.F9))
            {
                if (myLayer == null)
                {                    
                    
                   InformationManager.DisplayMessage(new InformationMessage($"尝试开启自定义UI", Color.FromUint(0xFF0000)));
                   OpenMyScreen();
                   InformationManager.DisplayMessage(new InformationMessage($"成功开启自定义UI", Color.FromUint(0xFF0000)));
                                 
                }
                else
                {
                    InformationManager.DisplayMessage(new InformationMessage($"尝试关闭自定义UI", Color.FromUint(0xFF0000)));
                    CloseMyScreen();
                    InformationManager.DisplayMessage(new InformationMessage($"成功关闭自定义UI", Color.FromUint(0xFF0000)));
                }
            }

            //给头盔打贴头皮patch
            PatchXml();
           

           
        }

        private void PatchXml()
        {
            //给头盔打patch
            // 1. 如果已经修改过了，直接退出，节省性能


            if (_hasPatched)
                return;

            // 2. 核心判断：如果 MBObjectManager 还没准备好，这一帧先跳过，等下一帧
            if (MBObjectManager.Instance == null)
                return;

            // 3. 到了这里说明 MBObjectManager 已经活了！
            // 执行我们的修改逻辑
            try
            {
                ChangeShoHelmet();
                // 4. 重要：标记为已完成，防止下一帧重复执行
                _hasPatched = true;
            }
            catch (Exception e)
            {
                // 如果出错了，打印出来，防止游戏卡死
                // 此时 UI 可能还没好，用 Debug 输出
                System.Diagnostics.Debug.WriteLine($">> 修改头盔失败: {e.Message}");
                // 即使失败了也要标记为 true，不然每帧都报错会把游戏卡死
                _hasPatched = true;
            }

        }
        private void ChangeShoHelmet()
        {

            var objectManager = MBObjectManager.Instance;

            // 获取物品列表
            var allItems = objectManager.GetObjectTypeList<ItemObject>();
            if (allItems == null) return;

            // 获取反射字段（复用之前的逻辑）
            var allFields = typeof(ArmorComponent).GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo targetField = allFields.FirstOrDefault(f => f.FieldType == typeof(ArmorComponent.HairCoverTypes));

            if (targetField == null) return;

            int count = 0;
            foreach (var item in allItems)
            {
                if (item.Type != ItemObject.ItemTypeEnum.HeadArmor) continue;
                if (item.ArmorComponent == null) continue;

                // 改为“全部覆盖”或者你需要的值
                targetField.SetValue(item.ArmorComponent, ArmorComponent.HairCoverTypes.None);
                count++;
            }

        }

        private GauntletLayer myLayer;
        private MyCustomUIVM myVM;
        private IGauntletMovie myMovie;
        
        private void OpenMyScreen()
        {
            myVM = new MyCustomUIVM();
            myVM.OnClose += CloseMyScreen;
            myLayer = new GauntletLayer(100);
            myMovie = myLayer.LoadMovie("MyCustomPopup",myVM);
            ScreenManager.TopScreen.AddLayer(myLayer);
            myLayer.InputRestrictions.SetInputRestrictions(true,InputUsageMask.All);
            ScreenManager.TrySetFocus(myLayer);
        }
        private void CloseMyScreen()
        {
            InformationManager.DisplayMessage(new InformationMessage($"监听到关闭UI事件", Color.FromUint(0xFF0000)));
            if (myLayer != null) {

                myLayer.ReleaseMovie(myMovie);
                ScreenManager.TopScreen.RemoveLayer(myLayer);
                myLayer = null;
                myMovie = null;
                myVM = null;
            
            }
        }


        
    }
}


