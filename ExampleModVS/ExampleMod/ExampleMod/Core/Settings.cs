using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    public class Settings
    {
        private static Settings _instance;
        public static Settings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Load();
                }
                return _instance;
            }
        }

        // ── 玩家 LLM 配置（从 config.json 读取）──
        public string LLMBaseUrl { get; set; } = "";
        public string LLMApiKey { get; set; } = "";
        public string LLMModel { get; set; } = "";
        public bool IsLLMReady => !string.IsNullOrWhiteSpace(LLMBaseUrl)
                               && !string.IsNullOrWhiteSpace(LLMApiKey)
                               && !string.IsNullOrWhiteSpace(LLMModel);

        // ── 世界观 flavor（硬编码卡拉迪亚默认，供 Mod B 代码覆盖）──
        public string WorldDescription { get; set; } = "骑马与砍杀2 卡拉迪亚中世纪世界";
        public string EraDescription { get; set; } = "中世纪卡拉迪亚大陆";
        public string SpeechStyle { get; set; } = "风格口语化、符合中世纪背景。不要使用现代网络用语。";
        public string WarriorTerms { get; set; } = "使用\"大人\"、\"爵士\"等符合中世纪语境的词汇。";
        public string FemaleSelfAddress { get; set; } = "";

        // ── 目击系统开关（默认开启，关掉后偷窃/犯罪不会被目击）──
        public bool WitnessSystemEnabled { get; set; } = false;

        // ── L3 警戒质问对话模式 ──
        public AlertDialogueMode AlertDialogueMode { get; set; } = AlertDialogueMode.StoryVM;


        //这里的值会被 config.json覆盖 只作为默认值
        public List<string> DisabledInteractionMissionModes { get; set; } = new List<string>
        {
            "Battle",       // 野战/攻城/藏身处
            "Deployment",   // 战前部署阶段
            "Duel",         // 竞技场决斗
        };

        /// <summary>当前 Mission 是否应关闭非战斗互动（视野感知/警戒/击晕/偷窃/对话）</summary>
        public bool IsInteractionDisabled()
        {
            if (Mission.Current == null) return false;
            return DisabledInteractionMissionModes.Contains(Mission.Current.Mode.ToString());
        }

        private static Settings Load()
        {
            Settings settings = new Settings();
            try
            {
                string configPath = Path.Combine(
                    System.AppDomain.CurrentDomain.BaseDirectory,
                    "..", "..", "Modules", "LivingWorldNpcs", "config.json");
                // Fallback: try old directory name during transition
                if (!File.Exists(configPath))
                {
                    configPath = Path.Combine(
                        System.AppDomain.CurrentDomain.BaseDirectory,
                        "..", "..", "Modules", "ExampleMod", "config.json");
                }
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    JsonConvert.PopulateObject(json, settings);
                }
            }
            catch
            {
                // 加载失败时使用全部默认值
            }
            return settings;
        }

        public static void Reload()
        {
            _instance = Load();
        }
    }
}
