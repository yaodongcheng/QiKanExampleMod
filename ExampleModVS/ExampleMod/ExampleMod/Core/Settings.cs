using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 一个玩法交互行的键位配置（config.json "Interactions" 条目）。
    /// 同一物理键可挂多个玩法行（默认 F 挂 7 行），同一次按下各按各自阈值与按法触发，短/长互斥。
    /// 解析与校验在 ModInput.RebuildBindings（空值 = 内置默认；非法值 = 回落默认 + 日志警告）。
    /// </summary>
    public class InteractionBindingConfig
    {
        public string Keyboard { get; set; } = "";   // 键盘：InputKey 枚举名（"F"/"Q"/"Space"/"Tab"…）
        public string Gamepad { get; set; } = "";    // 手柄：人类可读逻辑键（"Y"/"LB"/"R3"…；PS 名等价，见 config.json 注释）
        public string PressMode { get; set; } = "";  // Short / Long（空 = 内置默认）
        public int HoldMs { get; set; } = 0;         // 可选：覆盖全局 LongPressDurationMs（0 = 用全局）
    }

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

        // ── 击杀回血开关（默认关闭，玩家在 MCM 里主动开启后生效）──
        // 唯一来源 = MCM Mod 选项 UI（Core/MCMSettings.cs 写入），config.json 不读取
        // （[JsonIgnore] 双配置体系纪律：玩家高频调整的开关只在 MCM 一侧存在）。
        [Newtonsoft.Json.JsonIgnore]
        public bool HealOnKill { get; set; } = false;

        // ── NPC 血条显示开关（默认开启，玩家在 MCM 里关闭后 NPC 头顶血条/伤害数字隐藏）──
        // 唯一来源 = MCM Mod 选项 UI（Core/MCMSettings.cs 写入），config.json 不读取
        // （[JsonIgnore] 双配置体系纪律：玩家高频调整的开关只在 MCM 一侧存在）。
        [Newtonsoft.Json.JsonIgnore]
        public bool ShowAgentHealthBar { get; set; } = true;

        // ── NPC 意图文本显示开关（默认开启，NPC 头顶显示当前 AI 意图的调试文本）──
        // 唯一来源 = MCM Mod 选项 UI（Core/MCMSettings.cs 写入），config.json 不读取
        // （[JsonIgnore] 双配置体系纪律：玩家高频调整的开关只在 MCM 一侧存在）。
        [Newtonsoft.Json.JsonIgnore]
        public bool ShowNpcIntent { get; set; } = true;

        // ── L3 警戒质问对话模式 ──
        public AlertDialogueMode AlertDialogueMode { get; set; } = AlertDialogueMode.StoryVM;

        // ── 玩法键位配置（config.json 侧，不进 MCM：小白玩家不改，资深玩家可编辑热重载）──
        // 一个玩法行 = 一个玩法交互的 (键盘键, 手柄键, 按法)。同一物理键可挂多个玩法行
        // （默认 F 挂 7 行），同一次按下各按各自阈值与按法触发，短/长互斥。
        // 解析与校验在 ModInput.RebuildBindings（空值 = 内置默认；非法值 = 回落默认 + 日志警告）。
        // 条目类型 InteractionBindingConfig 见本文件顶级类。

        /// <summary>内置默认玩法行表（config.json 缺失/删除/非法时回落；示例即真实默认值）。</summary>
        public static readonly Dictionary<string, InteractionBindingConfig> DefaultInteractions = new Dictionary<string, InteractionBindingConfig>
        {
            [InteractionIds.Talk] = new InteractionBindingConfig { Keyboard = "F", Gamepad = "Y", PressMode = "Short" },
            [InteractionIds.Loot] = new InteractionBindingConfig { Keyboard = "F", Gamepad = "Y", PressMode = "Long" },
            [InteractionIds.Knockout] = new InteractionBindingConfig { Keyboard = "F", Gamepad = "Y", PressMode = "Long" },
            [InteractionIds.Pickpocket] = new InteractionBindingConfig { Keyboard = "F", Gamepad = "Y", PressMode = "Long" },
            [InteractionIds.StealAnimal] = new InteractionBindingConfig { Keyboard = "F", Gamepad = "Y", PressMode = "Long" },
            [InteractionIds.Lockpick] = new InteractionBindingConfig { Keyboard = "F", Gamepad = "Y", PressMode = "Long" },
            [InteractionIds.PlayerSurrender] = new InteractionBindingConfig { Keyboard = "F", Gamepad = "Y", PressMode = "Long" },
            [InteractionIds.AcceptSurrender] = new InteractionBindingConfig { Keyboard = "Q", Gamepad = "LB", PressMode = "Long" },
            [InteractionIds.Inspect] = new InteractionBindingConfig { Keyboard = "H", Gamepad = "R3", PressMode = "Short" },
            [InteractionIds.StealAttempt] = new InteractionBindingConfig { Keyboard = "Space", Gamepad = "A", PressMode = "Short" },
            [InteractionIds.StealLeave] = new InteractionBindingConfig { Keyboard = "Tab", Gamepad = "B", PressMode = "Short" },
        };

        /// <summary>玩法行配置（玩家在 config.json 覆盖/增删；PopulateObject 合并，删行 = 回落内置默认）。</summary>
        public Dictionary<string, InteractionBindingConfig> Interactions { get; set; } =
            new Dictionary<string, InteractionBindingConfig>(DefaultInteractions);

        /// <summary>长按全局默认阈值（毫秒）；玩法级 HoldMs 可单独覆盖。450ms = KCD 手感。</summary>
        public int LongPressDurationMs { get; set; } = 450;


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
