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
        private static Settings _instance = null;
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
        // 修改流程：选项 → Mod 选项 → Living World NPCs → LLM 配置 → 即时生效（IsLLMConfigured 调用点实时计算）。
        [Newtonsoft.Json.JsonIgnore]
        public string LLMBaseUrl { get; set; } = "";
        [Newtonsoft.Json.JsonIgnore]
        public string LLMApiKey { get; set; } = "";
        [Newtonsoft.Json.JsonIgnore]
        public string LLMModel { get; set; } = "";
        public bool IsLLMConfigured => !string.IsNullOrWhiteSpace(LLMBaseUrl)
                               && !string.IsNullOrWhiteSpace(LLMApiKey)
                               && !string.IsNullOrWhiteSpace(LLMModel);

        // ── 密令玩法总闸（行动密令，默认关闭）──
        // [JsonIgnore]：唯一来源 = MCM Mod 选项 UI（Core/MCMSettings.cs 写入），config.json 不读。
        // 关闭 = 密令入口（Plot）隐藏；已执行中的计划仍可用 StopPlan 停止（自由感：不被旧命令困住）。
        [Newtonsoft.Json.JsonIgnore]
        public bool PlotEnabled { get; set; } = false;

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
        // 货币单位（世界观参数化，config.json 侧）：默认解析原版本地化串 {=hYgmzZJX}
        // （CN="第纳尔" / EN="Denar"，随游戏语言自动变化，避免硬编码）；Mod B（如 TaikouContent）
        // 可在 MySubModule 注入"两"等自世界观货币名。
        // 🔴 禁止在 prompt 模板里硬编码货币单位（不同 mod 世界观、不同语言都不一致）。
        public string CurrencyName { get; set; } = LWNTextHelper.ResolveText("hYgmzZJX", "Denar");

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

        // ── 警戒拉满直接战斗开关（默认关闭，玩家在 MCM 里主动开启后生效）──
        // true = NPC 警戒值达到 Alarmed 时直接动手攻击（复用 StartL3CombatJoin 战斗加入路径，
        // 推进 WorldEvent 到 Confrontation 后入队 FightEnemyAction），跳过质问对话；
        // false（默认）= 维持原流程：警戒拉满后进入 L3 质问对话。
        // 唯一来源 = MCM Mod 选项 UI（Core/MCMSettings.cs 写入），config.json 不读取
        // （[JsonIgnore] 双配置体系纪律：玩家高频调整的开关只在 MCM 一侧存在）。
        [Newtonsoft.Json.JsonIgnore]
        public bool AlarmedDirectCombat { get; set; } = false;

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

        // ── 友方敌意互动保护开关（唯一来源 = MCM Mod 选项 UI，Core/MCMSettings.cs 写入）──
        // true（默认）= 允许对友方使用敌意互动（保持原行为——敌意互动均为长按蓄力触发，
        // 误触概率低，不需要默认禁止）；false = 禁止（防误伤保护：击晕/偷窃/搜刮/主动攻击
        // 不能作用于友方）。
        // [JsonIgnore] 双配置体系纪律：玩家高频调整的开关只在 MCM 一侧存在。
        [Newtonsoft.Json.JsonIgnore]
        public bool AllowHostileOnAllies { get; set; } = true;

        // ── 友方定义（config.json 侧，玩家可改；不进 MCM——列表型配置，注释写清楚即可）──
        // 取值："Party" 同队伍（随从/玩家部队成员）/ "Clan" 同家族 / "Kingdom" 同王国。
        // 默认 = 同队伍 + 同家族。判定入口 FriendlinessHelper.IsFriendlyToPlayer。
        public List<string> FriendlyRelationCriteria { get; set; } = new List<string> { "Party", "Clan" };

        // ── 好感度友方阈值（config.json 侧，数值玩家自己设）──
        // 对玩家好感 >= 此值的角色也视为友方（仅对 Hero 生效；模板 NPC 无个人好感值）。
        // 默认 50；好感上限 100，不需要此规则时设 101（永远达不到）。
        public int FriendlyRelationThreshold { get; set; } = 50;

        // ── L3 警戒质问对话模式 ──
        public AlertDialogueMode AlertDialogueMode { get; set; } = AlertDialogueMode.StoryVM;

        // ── IM 即时传讯系统（config.json 侧，不进 MCM：数值/概率类高级配置）──
        // 群聊跟随回复概率（用户决策 1：主回复者之外的第二人跟一句的概率，0~1）。
        public float ImGroupFollowUpChance { get; set; } = 0.1f;
        // 单 NPC 回复冷却（墙钟秒）：防玩家连发刷爆 LLM 限流。
        public float ImReplyCooldownSeconds { get; set; } = 5f;
        // 互动热度分档阈值（决定 NPC 记忆容量，Phase 5 生效）：heat >= Hot → 大容量；>= Normal → 现状；否则冷门小容量。
        public int ImHeatHotThreshold { get; set; } = 10;
        public int ImHeatNormalThreshold { get; set; } = 3;
        // 每日热度衰减（每游戏日扣减，下限 0）。
        public float ImHeatDecayPerDay { get; set; } = 1f;

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
            // 密谋/停止同键（G/LB 长按）：执行中显示 StopPlan、空闲显示 Plot，互斥保证不同时 available（LogBindingConflicts 零冲突）
            [InteractionIds.Plot] = new InteractionBindingConfig { Keyboard = "G", Gamepad = "LB", PressMode = "Long" },
            [InteractionIds.StopPlan] = new InteractionBindingConfig { Keyboard = "G", Gamepad = "LB", PressMode = "Long" },
            // 传讯（IM 面板开关）：键盘 O 短按；手柄不占键（面板右下角通知点击打开）
            [InteractionIds.IM] = new InteractionBindingConfig { Keyboard = "O", Gamepad = "", PressMode = "Short" },
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
            // 🔴 new Settings() 必须在 try 内：属性初始化器（WorldDescription 等调 LWNTextHelper.ResolveText）
            // 可能抛引擎本地化层异常——在 try 外 = Settings.Instance 抛 = 调用点"未初始化"假象。
            Settings settings = null;
            try
            {
                settings = new Settings();
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
                // 加载失败时使用全部默认值（初始化器异常时二次尝试，仍失败则跳过初始化器兜底）
                if (settings == null)
                {
                    try { settings = new Settings(); }
                    catch { settings = (Settings)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Settings)); }
                }
            }
            return settings;
        }

        public static void Reload()
        {
            _instance = Load();
        }
    }
}
