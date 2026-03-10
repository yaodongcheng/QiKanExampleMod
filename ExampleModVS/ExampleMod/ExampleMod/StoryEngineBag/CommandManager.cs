using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace ExampleMod.StoryEngineBag
{
    public delegate bool CommandHandler(ScriptNode node, StoryEngine engine);
    public class CommandManager
    {
        // 核心字典：指令名 -> 处理函数
        private static Dictionary<string, CommandHandler> _handlers = new Dictionary<string, CommandHandler>();

        // 初始化：在 Mod 加载时调用一次，注册所有指令
        public static void RegisterAll()
        {
            

            _handlers.Clear();

            // 注册逻辑指令 (实现在 LogicCommands.cs)
            Register("更新", LogicCommands.HandleUpdate);
            Register("代入ａ", LogicCommands.HandleSubstitute);

            Register("變量賦值", LogicCommands.HandleSetVar);
            Register("代入人物Ｂ", LogicCommands.HandleSetVar);
            Register("家督讓位", LogicCommands.HandleAbdicate);
            Register("分歧", LogicCommands.HandleSwitch);
            

            // 注册表现指令 (实现在 VisualCommands.cs)
            Register("對話", VisualCommands.HandleDialog); // 注意：太阁里通常是用 params 里的字串判断说话人和听话人
            Register("自語", VisualCommands.HandleDialog);
            Register("旁白", VisualCommands.HandleDialog);
            Register("對話選擇", VisualCommands.HandleDialog);
            Register("人物別", VisualCommands.HandlePortraitChange);
            Register("人物登场", VisualCommands.HandleAppear);
            Register("人物退场", VisualCommands.HandleExit);

            Register("選擇", VisualCommands.HandleChoice);

            // 注册系统指令
            Register("ＢＧＭ變更", SystemCommands.HandleBgm);
            Register("ＳＥ開始", SystemCommands.HandleSEBegin);
            Register("關閉消息", SystemCommands.HandleCloseMessage);
            Register("進入設施", SystemCommands.HandleEnterScene);
            Register("離開設施", SystemCommands.HandleEnterScene);
        }

        public static void Register(string cmdName, CommandHandler handler)
        {
            if (!_handlers.ContainsKey(cmdName))
            {
                _handlers.Add(cmdName, handler);
            }
        }
        public static bool Execute(ScriptNode node, StoryEngine engine,int index)
        {
            if (node == null || string.IsNullOrEmpty(node.Cmd))
            {
             //   InformationManager.DisplayMessage(new InformationMessage($"{index }指令为空."));
                return false;
            }
            if (_handlers.TryGetValue(node.Cmd, out var handler))
            {
            //    InformationManager.DisplayMessage(new InformationMessage($"找到第{index}个指令 {node.Cmd}. 尝试执行"));
                return handler.Invoke(node, engine);
            }

            // 如果找不到指令，默认跳过，防止报错卡死
            // TaleWorlds.Library.Debug.Print($"未知的指令: {node.Cmd}");
      //      InformationManager.DisplayMessage(new InformationMessage($"没找到第{index}指令{node.Cmd}对应的函数."));
            return false;
        }


      
    }
    
}
