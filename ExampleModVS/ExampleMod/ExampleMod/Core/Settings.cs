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

        // ── 密信玩法总闸（🔴 2026-08-12 改名：行动密令 → 密信，入口语义 = 打开私聊默认闲聊，
        //    玩家说话后 NPC 判 need_plan 才进计划管线；默认关闭）──
        // [JsonIgnore]：唯一来源 = MCM Mod 选项 UI（Core/MCMSettings.cs 写入），config.json 不读。
        // 关闭 = 密信入口（Plot）隐藏；已执行中的计划仍可用 StopPlan 停止（自由感：不被旧命令困住）。
        [Newtonsoft.Json.JsonIgnore]
        public bool PlotEnabled { get; set; } = false;

        // ── 口吻参数（config.json 侧；世界观 flavor 已退场，2026-08-17：WorldDescription/EraDescription
        // 删除——世界观改由 LLM 自动生成（WorldBackgroundBehavior），见 plans/world-background-auto-summary.md）──
        // 说话风格默认值：口语化、贴合中世纪背景，禁用现代网络用语
        public string SpeechStyle { get; set; } = LWNTextHelper.ResolveText("LWN_config_speech_style", "Speak in a colloquial style fitting the medieval setting. Do not use modern internet slang.");
        // 称谓用语默认值：使用"大人"、"爵士"等中世纪语境词汇
        public string WarriorTerms { get; set; } = LWNTextHelper.ResolveText("LWN_config_warrior_terms", "Use terms like \"my lord\" and \"sir\" appropriate to the medieval setting.");
        public string FemaleSelfAddress { get; set; } = "";
        // IM 新消息系统通知（NinjaReport 顶部横幅）开关（config.json 侧）：面板打开且当前会话时不弹
        public bool ImNotifyEnabled { get; set; } = true;

        // ── NPC 自主行动提议开关（默认关闭；config.json 侧，2026-08-13 用户裁定）──
        // 玩家对 NPC 说话（私聊/群聊/附近喊话）后，NPC 可能主动提议「我想去…」（巡逻/望风/讨账等），
        // 卡片批准后走计划管线。实机体验易出戏（玩家下令时小模型常把命令当话题顺着提，如「下令击晕 →
        // 提议去望风」双卡），默认关闭；开启后仍有纯寒暄门控（命令/计划消息不提议）。关 = 完全静默。
        public bool AutonomyProposalEnabled { get; set; } = false;
        // 货币单位（世界观参数化，config.json 侧）：默认解析原版本地化串 {=hYgmzZJX}
        // （CN="第纳尔" / EN="Denar"，随游戏语言自动变化，避免硬编码）；Mod B（如 TaikouContent）
        // 可在 MySubModule 注入"两"等自世界观货币名。
        // 🔴 禁止在 prompt 模板里硬编码货币单位（不同 mod 世界观、不同语言都不一致）。
        public string CurrencyName { get; set; } = LWNTextHelper.ResolveText("hYgmzZJX", "Denar");

        // ── 调试消息全局开关（工作时打开，发布前关掉）──
        public bool ShowDebugMessages { get; set; } = true;

        // ── 🔴 偷窃/击晕成功率强制覆盖（config.json 侧调试项；默认 -1 = 关闭）──
        // 本地调试用：NPC 偷窃/击晕老失败时直接锁成功率，不用反复改公式重编译。
        // 取值 0.05~0.95 = 强制成功率（所有判定都按这个概率掷点）；-1 = 走原公式。
        // 随从偷窃（InlineSteps）与 KnockoutFlow 共享管线（玩家击晕/NPC 击晕）都吃。
        // 运行时热改指令：custom.plan_debug steal_rate <0.05~0.95|off>
        public float StealSuccessRateOverride { get; set; } = -1f;

        // ── 手柄导航按键级诊断（config.json 侧调试项；默认关——排查按键/焦点/设备判定时开）──
        // 盖住 [Pad]/[NavCursor]/[Input 设备沿] 按键级日志 + 🎮/➤/🅰 屏显黄字
        // （每按一次键好几行，平时刷屏；关闭不影响功能与异常日志）。
        public bool GamepadNavDebugLog { get; set; } = false;

        // ── 🔴 [KbDiag] 软键盘链路诊断总闸（config.json 侧调试项；2026-08-23 新增，08-24 默认关）──
        // Deck 软键盘「聚焦→请求→弹窗→回填」全链路日志（输入框聚焦行/软键盘激活状态转换/
        // Done·Cancel 回调链/回填结果/请求判定）。排查软键盘呼不出、打字不回填时置 true，查完置 false
        // （聚焦行每点一次输入框打一行，平时没必要常开）。关闭不影响功能与异常日志。
        // 🔴 2026-08-24 结论：Deck 桌面模式 ShowGamepadTextInput 恒 false = Steam 客户端限制（mod 无解），
        // 本开关保留供游戏模式及未来任何软键盘问题排查用。
        public bool KbDiagEnabled { get; set; } = false;

        // ── 说话 LLM 润色开关（默认开启；config.json 侧调试项）──
        // 能力已全量升级（2026-08-12）：战斗喊话/质问/拒绝/警告等所有说话调用点都有 LLM 实时润色
        // 路径（fire-and-forget，预算超时/失败/无配置 → 原模板立即兜底，铁律 1）。此开关 = 是否启用
        // 润色；关掉后所有台词走离线模板（= 升级前的行为）。总闸另有 IsLLMConfigured（LLM 未配置 =
        // 天然不润色，无需额外处理）。
        public bool PolishSpeechEnabled { get; set; } = true;

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

        // ── 顶部罗盘开关（默认开启，Mission 内显示老滚5 风格方位罗盘）──
        // 唯一来源 = MCM Mod 选项 UI（Core/MCMSettings.cs 写入），config.json 不读取
        // （[JsonIgnore] 双配置体系纪律：玩家高频调整的开关只在 MCM 一侧存在）。
        [Newtonsoft.Json.JsonIgnore]
        public bool ShowCompass { get; set; } = true;

        // ── 性能诊断面板二开关（默认全关，诊断工具按需开；唯一来源 = MCM）──
        // ShowPerfHud    = 左上角帧率行（FPS / 帧时间 / 场景）
        // ShowPerfDetails = 追加模块耗时 TOP（本 mod 子系统 + 其他 mod DLL，合并一个开关）
        // 🔴 插桩/卡顿捕获本身常开（~2μs/帧，不可感知；卡顿行永远带 mod 数据）——
        //    二开关只控制「面板显示 + 是否包裹其他 mod + 卡顿行是否带 [Wrap] 段」。
        [Newtonsoft.Json.JsonIgnore]
        public bool ShowPerfHud { get; set; } = false;

        // ── 启动 Logo 视频替换（config.json 侧内容包注入；默认空 = 不替换，播放原生 Taleworlds 标志）──
        // 两个字段成对启用："SplashVideoModuleId" 指定视频所在模块 Id，"SplashVideoFileName" 为
        // Videos/ 下文件名（不含扩展名；同名 .ivf + .ogg 必须齐全）。替换链路见 Core/SplashVideoReplacePatch.cs。
        public string SplashVideoModuleId { get; set; } = "";
        public string SplashVideoFileName { get; set; } = "";

        // ── 设计数据内容包注入（config.json 侧；默认空 = 不注入，剧本表（Hero/Music/TagPoint 等）保持空表）──
        // "DesignDataModuleId" 指定内容包模块 Id（如 Taikou）；非空则启动时注入该模块
        // ModuleData/DesignData 下的 CSV。注入链路见 Data/DesignDataLoad.cs Initialize()。
        public string DesignDataModuleId { get; set; } = "";

        // ── 主菜单 BGM 内容包注入（config.json 侧；默认空 = 不接管，播放原生 Maintheme）──
        // "MenuSoundtrackModuleId" 指定音乐工程所在内容包模块 Id（如 Taikou）——该模块
        // music/soundtrack.xml（Psai 工程）+ music/PC/*.ogg 即主菜单 BGM 源。
        // 链路见 Core/MenuSoundtrackPatch.cs（仅 1.2.12 生效；1.5.x 原生支持模块工程）。
        public string MenuSoundtrackModuleId { get; set; } = "";

        // ── loading 背景图内容包注入（config.json 侧；默认空 = 原生 loading 图）──
        // "LoadingImageCategory" 指定内容包 SpriteCategory 名（如 Taikou 的 "taikou_loading" 79 张池）。
        // 内容包契约：SpriteData 定义 N 个 sheet 的类目 + 同名 GenericSprite 条目 {category}_{001..NNN}；
        // 启用后每次场景切换 loading 随机抽一张，纹理按 PartialLoad 单张进显存（无 AlwaysLoad 大驻流）。
        // 链路见 GUI/LoadingRandomPatch.cs。
        public string LoadingImageCategory { get; set; } = "";
        [Newtonsoft.Json.JsonIgnore]
        public bool ShowPerfDetails { get; set; } = false;

        // ── 罗盘重要人物图标开关（默认开启；关闭 = 只留刻度带与方向字母，不带任务图标）──
        // 🔴 2026-08-20（用户裁定）：MCM 侧开关已移除（跟随罗盘总开关），此字段归入
        // config.json 高级配置（小白玩家不需要分开控制；想只留刻度带的改 config.json 此项）。
        public bool ShowCompassIcons { get; set; } = true;

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
        // 🔴 2026-08-12 25%→15%：25% + 满4条保底叠加实测偏密（用户反馈"跟随说话频率有点高"）
        // 🔴 2026-08-13 15%→6%：玩家一条消息仍常带出「主回复+跟随+bounce」三条连发（实机日志），继续下调
        public float ImGroupFollowUpChance { get; set; } = 0.06f;
        // 斗嘴往返概率（2026-08-10 v4）：跟随者回应后，主回复者再回一句的概率（0~1；0 = 关闭）
        // 🔴 2026-08-13 50%→25%：bounce 是"一条消息三条回复"链的放大器（主回复+跟随+bounce），减半
        public float ImBounceChance { get; set; } = 0.25f;
        // 单 NPC 回复冷却（墙钟秒）：防玩家连发刷爆 LLM 限流。
        public float ImReplyCooldownSeconds { get; set; } = 5f;
        // 互动热度分档阈值（决定 NPC 记忆容量，Phase 5 生效）：heat >= Hot → 大容量；>= Normal → 现状；否则冷门小容量。
        public int ImHeatHotThreshold { get; set; } = 10;
        public int ImHeatNormalThreshold { get; set; } = 3;
        // 每日热度衰减（每游戏日扣减，下限 0）。
        public float ImHeatDecayPerDay { get; set; } = 1f;

        // ── 闲聊行动系统（im-command-action-upgrade.md §5.2/§5.4，config.json 侧：数值类高级配置）──
        // 关系/声望/party 类 action 冷却（墙钟秒）：同 attacker→同 defender 每 60s 最多 1 次，
        // 超频 → 该次降级 NONE + DebugLogger（防 LLM 滥用掉好感贬值；演出类/高风险类不参与）。
        public float ChatActionCooldownSeconds { get; set; } = 60f;
        // FollowAgentAction 动态重算间隔上下限（秒，§5.4）：跟随目标时按「目标速度 + 距离」双因子
        // 动态决定重发寻路的间隔；上限 = 心跳（目标不可达时周期性自愈纠偏），下限 = 跟上冲刺目标。
        public float FollowRepathMin { get; set; } = 0.15f;
        public float FollowRepathMax { get; set; } = 3.0f;
        // 附近频道（§5.7）：玩家喊话的响应半径（米）——范围内最近 NPC 才可能应声
        public float NearbyRespondRadius { get; set; } = 6f;
        // 附近频道可听半径（米）：场景冒泡距玩家超过此距离不进 nearby（与 AgentHudMissionView.FarHearDistance
        // 30m 的"远处听到"语义一致——玩家听不到的话就不该在频道里）
        public float NearbyHearRadius { get; set; } = 30f;

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
            // 传讯（IM 面板开关）：键盘 M / 手柄 ↑ 十字短按（面板外唯一呼出键，2026-08-17）。
            // 🔴 2026-08-23（用户裁定：按键重选）：O 换 M——O=原版 Cheer（口哨），M 全分类零绑定
            //（BannerlordGameKeys.xml 逐分类核查实锤，唯一空闲字母键）；长按方案废弃（O/↑ 长按 =
            // 原版 CheerBark 表情菜单按住弹出，物理轮询无法拦截必双触发，im-layer-and-input-design.md §4.6）。
            // 定居点菜单（GameMenu）内：手柄 ↑ 屏蔽（菜单导航键冲突，CanOpen+键帽双屏蔽），键盘 M 照常
            [InteractionIds.IM] = new InteractionBindingConfig { Keyboard = "M", Gamepad = "LUp", PressMode = "Short" },
            // 调停（随从犯法被执法时面向守卫按 F）：与 Talk 同键——上下文互斥替换（守卫警戒非玩家时
            // Intervene 行替换 Talk 行，永不共存，无冲突警告）
            [InteractionIds.Intervene] = new InteractionBindingConfig { Keyboard = "F", Gamepad = "Y", PressMode = "Short" },
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
            // 非战役模式（自定义战斗等）无 Campaign：Settlement.CurrentSettlement getter 内部
            // 访问 MobileParty.MainParty 会 NRE（getter 无 null 保护，?. 救不了），直接禁用互动
            if (Campaign.Current == null) return true;
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
