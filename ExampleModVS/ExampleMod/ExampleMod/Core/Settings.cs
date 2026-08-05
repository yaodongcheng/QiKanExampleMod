using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
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

        // ── 玩家 LLM 配置（唯一来源 = MCM Mod 选项 UI，Core/MCMSettings.cs 写入）──
        // [JsonIgnore]：config.json 不再读取这些字段（删掉旧兜底，避免玩家在两个地方配置 LLM 产生误会）。
        // 修改流程：选项 → Mod 选项 → Living World NPCs → LLM 配置 → 即时生效（IsLLMReady 调用点实时计算）。
        [Newtonsoft.Json.JsonIgnore]
        public string LLMBaseUrl { get; set; } = "";
        [Newtonsoft.Json.JsonIgnore]
        public string LLMApiKey { get; set; } = "";
        [Newtonsoft.Json.JsonIgnore]
        public string LLMModel { get; set; } = "";
        public bool IsLLMReady => !string.IsNullOrWhiteSpace(LLMBaseUrl)
                               && !string.IsNullOrWhiteSpace(LLMApiKey)
                               && !string.IsNullOrWhiteSpace(LLMModel);

        // ── 世界观 flavor（硬编码卡拉迪亚默认，供 Mod B 代码覆盖）──
        // 世界观描述默认值：通用卡拉迪亚中世纪世界
        public string WorldDescription { get; set; } = LWNTextHelper.ResolveText("LWN_config_world_description", "Mount & Blade II: Calradia medieval world");
        // 时代描述默认值：中世纪卡拉迪亚大陆
        public string EraDescription { get; set; } = LWNTextHelper.ResolveText("LWN_config_era_description", "Medieval Calradia");
        // 说话风格默认值：口语化、贴合中世纪背景，禁用现代网络用语
        public string SpeechStyle { get; set; } = LWNTextHelper.ResolveText("LWN_config_speech_style", "Speak in a colloquial style fitting the medieval setting. Do not use modern internet slang.");
        // 称谓用语默认值：使用"大人"、"爵士"等中世纪语境词汇
        public string WarriorTerms { get; set; } = LWNTextHelper.ResolveText("LWN_config_warrior_terms", "Use terms like \"my lord\" and \"sir\" appropriate to the medieval setting.");
        public string FemaleSelfAddress { get; set; } = "";

        // ── 调试消息全局开关（工作时打开，发布前关掉）──
        public bool ShowDebugMessages { get; set; } = true;

        // ── 目击系统开关（默认开启，关掉后偷窃/犯罪不会被目击）──
        public bool WitnessSystemEnabled { get; set; } = true;

        // ── 复仇队开关（默认开启，关掉后事件方不会派出复仇队/打手队追击嫌犯）──
        // 唯一来源 = MCM Mod 选项 UI（Core/MCMSettings.cs 写入），config.json 不读取
        // （[JsonIgnore] 双配置体系纪律：玩家高频调整的开关只在 MCM 一侧存在）。
        [Newtonsoft.Json.JsonIgnore]
        public bool EnableRevengeParty { get; set; } = true;

        // ── L3 警戒质问对话模式 ──
        public AlertDialogueMode AlertDialogueMode { get; set; } = AlertDialogueMode.StoryVM;


        //这里的值会被 config.json覆盖 只作为默认值
        public List<string> DisabledInteractionMissionModes { get; set; } = new List<string>
        {
            "Conversation", // 对话（ConversationMission）
            "Battle",       // 野战/攻城
            "Duel",         // 竞技场决斗
            "Tournament",   // 竞技大赛
            "Stealth",      // 藏身处潜入阶段（HideoutAmbushMission）
            "Barter",       // 讨价还价（BarterMission）
            "Deployment",   // 战前部署阶段
            "Replay",       // 战斗回放
            "CutScene",     // 剧情对话（CutsceneMission）
            "Benchmark"     // 性能测试场景
        };

        /// <summary>当前 Mission 是否应关闭非战斗互动（视野感知/警戒/击晕/偷窃/对话）</summary>
        public bool IsInteractionDisabled()
        {
            if (Mission.Current == null) return true;
            if (DisabledInteractionMissionModes.Contains(Mission.Current.Mode.ToString())) return true;
            // 新手训练场（tutorial_training_field）不适用 MissionMode 过滤（其 Mode 为 StartUp 与城镇相同），
            // 但训练场是教程关，不应出现交互功能干扰教学流程，按 Settlement ID 直判
            if (Settlement.CurrentSettlement?.StringId == "tutorial_training_field") return true;
            // 竞技场练习（arena_* 场景）Mode 同为 StartUp，不在上面的 Mode 列表中。
            // 按 SceneName 前缀或玩法级 MissionLogic（ArenaPracticeFightMissionController）直判——
            // Behavior 标志语义最准；前缀兜底低版本（1.2.12 无 SandBox 引用）
            if (Mission.Current.SceneName?.StartsWith("arena_") == true) return true;
#if !MB2_V1212
            if (Mission.Current.HasMissionBehavior<SandBox.Missions.MissionLogics.Arena.ArenaPracticeFightMissionController>()) return true;
#endif
            //DebugLogger.Log($"[Interaction] Mission mode 通过检查: {Mission.Current.Mode}");
            
            return false;
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
