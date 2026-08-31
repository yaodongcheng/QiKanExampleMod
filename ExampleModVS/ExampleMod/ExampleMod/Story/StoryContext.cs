using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.SaveSystem;

namespace LivingWorldNpcs
{
    public class StoryContext
    {
        private static StoryContext _instance;
        public static StoryContext Instance => _instance ?? (_instance = new StoryContext());

        /// <summary>🔴 2026-08-23（跨档残留修复）：新档创建时丢弃旧单例（static 单例跨档残留旧 LocalVariables；
        /// GlobalVariableBehavior 是 behavior 实例字段，新档自动清空，不需处理）。</summary>
        public static void ResetAll()
        {
            _instance = null;
        }

        public Dictionary<string, string> LocalVariables { get; private set; } = new Dictionary<string, string>();

        public void SetLocalVariable(string key, string value)
        {
            if (LocalVariables.ContainsKey(key))
                LocalVariables[key] = value;
            else
                LocalVariables.Add(key, value);
        }

        public string GetLocalVariable(string key)
        {
            return LocalVariables.ContainsKey(key) ? LocalVariables[key] : null;
        }

        // 每次脚本开始前最好清理一下
        public void ClearContext()
        {
            LocalVariables.Clear();
        }


  
        public Hero FindHeroById(string stringId)
        {

            
            return Campaign.Current.CampaignObjectManager.Find<Hero>(stringId); ;
        }

        public string ParseText(string rawText)
        {
            if (string.IsNullOrEmpty(rawText)) return "";

            string result = rawText;

            // 替换常见占位符 目前这里就换了主角的，但是其他角色其实也都有的
            if (result.Contains("二人稱"))
                result = result.Replace("{二人稱}", Hero.MainHero.Name.ToString());

            if (result.Contains("一人稱"))
                result = result.Replace("{一人稱}", "在下"); // 也可以根据主角性格动态变化

            // 支持读取变量：例如文本里写了 {人物::マリア.所在}
            // 这里可以用正则搞定，暂时简单处理

            return result;
        }

    }
}
