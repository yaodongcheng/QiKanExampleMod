using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ExampleMod.StoryEngineBag
{
    public static class LogicCommands
    {

        
        public static bool HandleUpdate(ScriptNode node, StoryEngine engine)
        {
            

            string keyStr = "";
            string valueStr = "";
            if(node.Params.Count>0)
                keyStr = node.Params[0];
            if(node.Params.Count > 1)
                valueStr = node.Params[1];
            
            /*
            string finalValue = VariableManager.GetValue(valueStr);
            if (finalValue == "0" && valueStr != "0")
            {
                finalValue = valueStr;
            }
            */

            // 2. 执行赋值
            bool setValueSuccess = VariableManager.SetValue(keyStr, valueStr);
            if(setValueSuccess)
            {
                return false;
            }
            else
            {
                engine.ViewModel.Show(node.Cmd, $"未找到 {keyStr}，无法赋值为 {valueStr}");
                return true;
            }  
        }

        public static bool HandleSubstitute(ScriptNode node, StoryEngine engine)
        {
            string param1 = "";
            string param2 = "";
            if(node.Params.Count == 2)
            {
                param1 = node.Params[0];
                param2 = node.Params[1];
            }

            if(node.Cmd == "代入ａ")
            {
                int value1 = 0;
                int value2 = 0;
                int.TryParse( VariableManager.GetValue(param1), out value1);
                int.TryParse(param2, out value2);
                string finalValue = (value1 + value2).ToString();
                VariableManager.SetValue("ａ", finalValue);

                return false;
            }

            engine.ViewModel.Show(node.Cmd, $"{param1} + {param2}");
            return true;
        }

        public static bool Ispronoun(string str)
        {
            if(str == "人物Ｂ")
                return true;
            if (str == "主人公")
                return true;

            return false;
        }
        


        public static bool HandleAbdicate(ScriptNode node, StoryEngine engine)
        {
            string twoHero = node.Params[0];
            string[] parts = twoHero.Split(new char[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries);
            if(parts.Length< 2) return false;
            string oldLeader = parts[0];
            string newLeader = parts[1];
            //如果是人物A、人物B、主人公等代词，还需要统一替换

            if (Ispronoun(oldLeader))
                oldLeader = VariableManager.GetValue(oldLeader);
            if (Ispronoun(newLeader))
                newLeader = VariableManager.GetValue(newLeader);

            Action onConfirm = () => { StoryEngine.Instance.Resume(); };

            InformationManager.ShowInquiry(new InquiryData(node.Cmd, $"{oldLeader} 即将让位于 {newLeader}", true, false, "确定", "", onConfirm, null));
            return true;
        }
        public static bool HandleSwitch(ScriptNode node, StoryEngine engine)
        {
            
            engine.ViewModel.Show(node.Cmd, $"{node.Value}");


            // 假设 engine 有一个变量存储系统 (StoryContext)
            string lastChoice = $"{engine.LastChoiceIndex}";

            // 检查 node.Value 是否匹配玩家的选择
            if (lastChoice == node.Value)
            {
                // 关键：将子节点推入引擎的执行栈

                InformationManager.DisplayMessage(new InformationMessage($"当前选择值{lastChoice}推入了新的一层", Colors.Red));
                engine.PushSubScript(node.Children);
                // 分歧本身不是阻塞的（瞬间完成进入子集），所以返回 false
                return false;
            }

            return false; // 条件不满足，不执行子集，继续下一行          

        }

        public static bool HandleSetVar(ScriptNode node, StoryEngine engine)
        {
            string keyStr = ""; // "人物B"
            string valueStr = "";
            if (node.Params.Count > 0)
                valueStr = node.Params[0];

            engine.ViewModel.Show(node.Cmd, $"{valueStr}");
            if (node.Cmd == "代入人物Ｂ") 
                keyStr = "人物Ｂ";
            else if (node.Cmd == "代入人物Ａ" || node.Cmd == "代入ａ") 
                keyStr = "人物Ａ";
            if (keyStr != "")
            { 
                VariableManager.SetValue(keyStr, valueStr);
            //先不用本地局部变量
           // StoryContext.Instance.SetLocalVariable(keyStr, valueStr);
                return false;
            }         
            
            return true;


        }

       
    }
}
