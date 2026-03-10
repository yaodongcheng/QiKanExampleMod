using psai.net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ExampleMod.StoryEngineBag
{
    public static class SystemCommands
    {
        public static bool HandleBgm(ScriptNode node, StoryEngine engine)
        {
            string bgmName = "";

            if (node.Cmd == "ＢＧＭ變更")
            {
                if (node.Params.Count > 0)
                {
                    bgmName = node.Params[0];
                    bool result =  ChangeBgm(bgmName);
                    if(result == false)
                        return false;
                    
                }
            }
             engine.ViewModel.Show("System", $"{node.Cmd}：{node.Params[0]}");
             return true;
            

        }
        public static bool ChangeBgm(string bgmName)
        {
            if (PsaiCore.Instance != null)
            {
                PsaiCore.Instance.StopMusic(true, 2.0f);
            }
            if (StoryEngine.currentBgm != null && StoryEngine.currentBgm.IsPlaying())
            {
                StoryEngine.currentBgm.Stop();
            }
            //SoundManager.SetListenerFrame(listenderFrame);
            string soundStr = bgmName;
            string soundId = GameDatabase.Music.GetIdByName(soundStr);
            int soundIndex = SoundEvent.GetEventIdFromString(soundId);
            if (soundIndex >= 0)
            {
                StoryEngine.currentBgm = SoundEvent.CreateEvent(soundIndex, Mission.Current.Scene);
                StoryEngine.currentBgm.Play();
                return false;
            }
            return true;
        }


        public static bool HandleSEBegin(ScriptNode node, StoryEngine engine)
        {
            // 处理 "關閉消息" 指令
            // 关闭当前对话窗口或消息框
            
            string param = "";
            if (node.Params.Count > 0)
            {
                param = node.Params[0];
            }

            if (param == "忍者報告")
            {
                engine.ViewModel.Close();
                NinjaNotificationManager.Show(param, () => {
                    //还是在这里让忍者烟雾弹出现吧
                    StageDirector.Instance.NinjaShow();
                   // SystemCommands.ExecuteWait(2.0f);
                    StoryEngine.Instance.Resume();
                });
                return true;
            }
            else
            { 
                engine.ViewModel.Show(node.Cmd, $"{node.Cmd}");
                return true;
            }



        }


        public static bool HandleCloseMessage(ScriptNode node, StoryEngine engine)
        {
            // 处理 "關閉消息" 指令
            // 关闭当前对话窗口或消息框
            engine.ViewModel.IsVisible = false;

            return false; // 
        }
        public static bool HandleEnterScene(ScriptNode node, StoryEngine engine)
        {

            

            string sceneName = "";
            string sceneId = "center";
            if(node.Cmd == "離開設施")
            {
                sceneName = "镇中心";
                sceneId = "center";
            }
            if (node.Params.Count > 0)
            {
                sceneName = node.Params[0];
                switch(sceneName)
                {
                    case "城主間":
                        sceneId = "lordshall";
                        break;
                    default:
                        return true;
                }
                

            }

            //希望玩家确认之后才切换场景
            Action onConfirm = () =>
            {
                LocationComplex locationComplex = LocationComplex.Current;
                if (locationComplex != null)
                {
                    // 获取目标位置
                    Location targetLocation = locationComplex.GetLocationWithId(sceneId);
                    Location previousLocation = locationComplex.GetLocationWithId("center"); // 或者当前位置

                    // 增加空值检查，防止 ID 写错导致崩溃
                    if (targetLocation != null)
                    {
                        Campaign.Current.GameMenuManager.NextLocation = targetLocation;
                        Campaign.Current.GameMenuManager.PreviousLocation = previousLocation;

                        // 结束当前任务（场景），系统会自动根据 NextLocation 加载新场景
                        Mission.Current.EndMission();

                        // 通知 StoryEngine 状态变更
                        StoryEngine.Instance.TriggerSceneChange(sceneId);
                    }
                    else
                    {
                        // 可选：提示找不到场景 ID
                        InformationManager.DisplayMessage(new InformationMessage($"找不到场景 ID: {sceneId}"));
                    }
                }
            };
            
            InformationManager.ShowInquiry(new InquiryData(node.Cmd, $"即将前往{sceneName}", true, false, "确定", "", onConfirm, null));

           
    
            return true;
        }
        public static bool ExecuteWait(float seconds)
        {

            StoryEngine.Instance.StartWait(seconds);
            return false;
        }

    }
}
