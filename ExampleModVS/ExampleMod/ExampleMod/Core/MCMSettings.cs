using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using System;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace LivingWorldNpcs
{
    /// <summary>
    /// MCM（Bannerlord.MBOptionScreen）Mod 选项设置页 —— 玩家可见设置的 UI 层。
    ///
    /// 接入方式（参考 Bannerlord.Diplomacy 的 Diplomacy.Settings）：继承
    /// AttributeGlobalSettings&lt;T&gt; 即被 MCM 启动时自动扫描注册（AppDomain 全程序集
    /// 扫描 BaseSettings 子类 + AttributeSettingsPropertyDiscoverer 读特性），
    /// **无需任何手动注册代码**，以子菜单形式出现在游戏「Mod 选项」界面。
    ///
    /// 数据流（facade 模式）：getter/setter 透传核心 Settings。
    ///   玩家在 UI 修改 → setter 立即写入核心 Settings（调用点实时生效，无需重启）
    ///                    → MCM 自动持久化到 json2 文件（改动即存，无需手动保存）：
    ///       {USERPROFILE}\Documents\Mount and Blade II Bannerlord\Configs\ModSettings\Global\LivingWorldNpcs\LivingWorldNpcsSettings_v1.json
    ///       （文件名 = Id + ".json"；路径 = 平台用户目录 + Configs\ModSettings\Global\{FolderName}\）
    ///   LLM 三字段是唯一来源（核心 Settings 侧已 [JsonIgnore]，config.json 不再读它们）；
    ///   其他玩家隐藏变量（世界观/互动开关等）仍走 config.json（Modules\LivingWorldNpcs\config.json），MCM UI 不显示。
    ///   铁律 1 不受影响：核心 Settings.Instance 永不因 MCM 未就绪而变 null。
    ///
    /// 本地化：显示名/HintText 用 {=LWN_KEY}fallback 格式（Diplomacy 同款），
    /// 引擎查 Languages/ 语言表，命中用翻译、未命中用 fallback —— 满足铁律 13。
    /// </summary>
    public sealed class MCMSettings : AttributeGlobalSettings<MCMSettings>
    {
        public override string Id => "LivingWorldNpcsSettings_v1";
        public override string DisplayName => new TextObject("{=LWN_mcm_display_name}Living World NPCs").ToString();
        public override string FolderName => "LivingWorldNpcs";
        public override string FormatType => "json2";

        // ── 分组显示说明（踩坑记录，MCM v5.11.4 + 游戏 v1.4.7 实测）──
        // MCM 设置页所有列表都是 VerticalBottomToTop（从底部往上排）：显示顺序 =
        // Order 排序结果的倒序；组标题渲染在分隔行下方（footer 式），标题永远紧贴
        // 下一组的正文。多组 + 单设置组会让标题看起来属于别的组 —— 所以这里全部
        // 设置归入一个组（LWN_mcm_grp_main），彻底消除标题错位观感。
        // ⚠️ Order 按「显示倒序」赋值：期望显示 地址→密钥→模型→复仇队→回血，
        //    Order 就必须反过来（回血=0 … 地址=4），显示时才能恢复正序。
        // ── 密信玩法总闸（默认关闭，透传核心 Settings）──
        // Order = 5：显示倒序 → Order 最大 = 列表最顶部（玩法总闸置顶）
        // 🔴 2026-08-12（合并闲聊/计划模式）：入口文案随交互行统一为「密信」——
        // 打开私聊后默认闲聊，NPC 判 need_plan 才进计划管线
        [SettingPropertyBool("{=LWN_mcm_plot_enabled}Messaging", Order = 5, RequireRestart = false,
            HintText = "{=LWN_mcm_plot_enabled_hint}When enabled, you can write natural-language secret letters to companions (press the letter key near a companion). Disabled by default; when off, the letter entry is hidden but already-running plans can still be stopped.")]
        [SettingPropertyGroup("{=LWN_mcm_grp_main}Settings")]
        public bool PlotEnabled
        {
            get => Settings.Instance.PlotEnabled;
            set => Settings.Instance.PlotEnabled = value;
        }

        // ── LLM 配置（透传核心 Settings；IsLLMConfigured 在调用点实时计算 → 无需重启）──
        [SettingPropertyText("{=LWN_mcm_llm_base_url}LLM API Base URL", Order = 4, RequireRestart = false,
            HintText = "{=LWN_mcm_llm_base_url_hint}The LLM API endpoint base URL, e.g. https://api.example.com/v1")]
        [SettingPropertyGroup("{=LWN_mcm_grp_main}Settings")]
        public string LLMBaseUrl
        {
            get => Settings.Instance.LLMBaseUrl;
            set
            {
                // 自动纠错：玩家误填完整端点时剥掉后缀（防 ApiUrl 拼出双后缀 404）——
                // ① OpenAI 方言：…/chat/completions（本 mod 客户端格式）
                // ② Anthropic 方言：…/messages（照网关 anthropic 说明复制来的）
                // 🔴 只剥后缀、禁止 TrimEnd('/') 落盘：MCM 文本框实时提交（每按键调 setter），
                //    输入 "https:/" 时剥尾部斜杠会立即变回 "https:"——斜杠永远打不进去（2026-08-08 实测）。
                //    尾部斜杠交给 ApiUrl 请求时处理（LLMService 侧本就有 TrimEnd('/')）。
                var cleaned = value;
                if (cleaned != null)
                {
                    // 后缀匹配用临时剥斜杠的值判断，剥出的结果落盘（防 ".../messages/" 漏剥）
                    var trimmed = cleaned.TrimEnd('/');
                    foreach (var suffix in new[] { "/chat/completions", "/messages" })
                    {
                        if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        {
                            cleaned = trimmed.Substring(0, trimmed.Length - suffix.Length);
                            break;
                        }
                    }
                }
                Settings.Instance.LLMBaseUrl = cleaned;
            }
        }

        [SettingPropertyText("{=LWN_mcm_llm_api_key}LLM API Key", Order = 3, RequireRestart = false,
            HintText = "{=LWN_mcm_llm_api_key_hint}Your LLM API key. Saved in the MCM settings file.")]
        [SettingPropertyGroup("{=LWN_mcm_grp_main}Settings")]
        public string LLMApiKey
        {
            get => Settings.Instance.LLMApiKey;
            set { Settings.Instance.LLMApiKey = value; }
        }

        [SettingPropertyText("{=LWN_mcm_llm_model}LLM Model", Order = 2, RequireRestart = false,
            HintText = "{=LWN_mcm_llm_model_hint}The model name to use, e.g. gpt-4o-mini")]
        [SettingPropertyGroup("{=LWN_mcm_grp_main}Settings")]
        public string LLMModel
        {
            get => Settings.Instance.LLMModel;
            set { Settings.Instance.LLMModel = value; }
        }

        // ── LLM 连接测试按钮（交付：验证 BaseUrl 可达 + key 有效；结果飘字提示）──
        // MCM 按钮 = Action 类型属性（PropertyReference.Type == typeof(Action) → SettingType.Button）；
        // 回调同步等待测试（HttpClient 30s 超时兜底，UI 冻结最长 30s 可接受）。
        [SettingPropertyButton("{=LWN_mcm_llm_test}Test LLM Connection", Order = 1, RequireRestart = false,
            HintText = "{=LWN_mcm_llm_test_hint}Send a minimal request to verify the LLM service is reachable and the API key is valid.")]
        [SettingPropertyGroup("{=LWN_mcm_grp_main}Settings")]
        public Action TestLLMConnection
        {
            // MCM CheckIsValid 要求非 Dropdown 属性必须 CanWrite（有 setter）——按钮动作在 getter，setter 留空
            // 同步调用 LLMService.TestConnection（HttpWebRequest 10s 超时）：无 async 死锁问题，UI 冻结最长 10s
            // 展示统一走 LLMService.ShowConnectionMessage：按 5 种失败原因分别提示（未配置/地址错/模型不存在/密钥错/余额不足）
            get => () =>
            {
                var result = LLMService.TestConnection();
                LLMService.ShowConnectionMessage(result, showSuccess: true);
            };
            set { }
        }

        // ── 世界事件（透传核心 Settings）──
        [SettingPropertyBool("{=LWN_mcm_revenge_party}Send Revenge Party", Order = 1, RequireRestart = false,
            HintText = "{=LWN_mcm_revenge_party_hint}When enabled, the victim party may dispatch revenge parties (and thugs) to hunt down the suspect on the campaign map. Disable to keep the world from sending pursuers.")]
        [SettingPropertyGroup("{=LWN_mcm_grp_main}Settings")]
        public bool EnableRevengeParty
        {
            get => Settings.Instance.EnableRevengeParty;
            set => Settings.Instance.EnableRevengeParty = value;
        }

        // ── 战斗（透传核心 Settings）──
        [SettingPropertyBool("{=LWN_mcm_heal_on_kill}Heal on Kill", Order = 0, RequireRestart = false,
            HintText = "{=LWN_mcm_heal_on_kill_hint}When enabled, the player restores health after killing an enemy on the battlefield. Disable to remove this bonus.")]
        [SettingPropertyGroup("{=LWN_mcm_grp_main}Settings")]
        public bool HealOnKill
        {
            get => Settings.Instance.HealOnKill;
            set => Settings.Instance.HealOnKill = value;
        }

        // ── 警戒行为（透传核心 Settings）──
        // Order = 0 与击杀回血并列（Order 无空档整数位；平局按名称稳定排序，两者显示位置紧邻）
        [SettingPropertyBool("{=LWN_mcm_alarmed_direct_combat}Fight on Full Alarm", Order = 0, RequireRestart = false,
            HintText = "{=LWN_mcm_alarmed_direct_combat_hint}When enabled, NPCs whose alarm reaches maximum attack you directly instead of confronting you in dialogue. The alarm escalation skips the interrogation and goes straight to combat.")]
        [SettingPropertyGroup("{=LWN_mcm_grp_main}Settings")]
        public bool AlarmedDirectCombat
        {
            get => Settings.Instance.AlarmedDirectCombat;
            set => Settings.Instance.AlarmedDirectCombat = value;
        }

        // ── 战斗（透传核心 Settings）──
        // Order = -1：显示顺序为 Order 升序的倒序（见类头部注释），-1 使其排在列表最底部
        [SettingPropertyBool("{=LWN_mcm_show_health_bar}Show NPC Health Bars", Order = -1, RequireRestart = false,
            HintText = "{=LWN_mcm_show_health_bar_hint}When enabled, health bars appear above NPCs (in combat, when damaged, or when alerted). Disable to reduce on-screen clutter; NPC names then only appear while they speak or show an alert.")]
        [SettingPropertyGroup("{=LWN_mcm_grp_main}Settings")]
        public bool ShowAgentHealthBar
        {
            get => Settings.Instance.ShowAgentHealthBar;
            set => Settings.Instance.ShowAgentHealthBar = value;
        }

        // ── 友方敌意互动保护（透传核心 Settings）──
        // Order = -3：显示在 ShowNpcIntent(-2) 之后，列表最底部
        [SettingPropertyBool("{=LWN_mcm_hostile_on_allies}Allow Hostile Interactions on Allies", Order = -3, RequireRestart = false,
            HintText = "{=LWN_mcm_hostile_on_allies_hint}When disabled, knockout/pickpocket/loot/attack are blocked on allies — friendly definition: config.json `FriendlyRelationCriteria` (default: same party + same clan) and `FriendlyRelationThreshold` (relation value). Enabled by default (hostile interactions require a long press). Allies always ignore your crimes against strangers, regardless of this setting.")]
        [SettingPropertyGroup("{=LWN_mcm_grp_main}Settings")]
        public bool AllowHostileOnAllies
        {
            get => Settings.Instance.AllowHostileOnAllies;
            set => Settings.Instance.AllowHostileOnAllies = value;
        }

        // ── 战斗（透传核心 Settings）──
        // Order = -2：显示在血条开关（-1）之后，列表最底部
        [SettingPropertyBool("{=LWN_mcm_show_npc_intent}Show NPC Intent", Order = -2, RequireRestart = false,
            HintText = "{=LWN_mcm_show_npc_intent_hint}When enabled, the current AI intent is displayed above NPCs (in towns and other interactive scenes, not on the battlefield). Disable to hide this text.")]
        [SettingPropertyGroup("{=LWN_mcm_grp_main}Settings")]
        public bool ShowNpcIntent
        {
            get => Settings.Instance.ShowNpcIntent;
            set => Settings.Instance.ShowNpcIntent = value;
        }

        // ── 顶部罗盘（透传核心 Settings）──
        // Order = -4：显示在意图开关（-2）与友方保护（-3）之后，列表最底部
        // 🔴 2026-08-20（用户裁定）：罗盘任务图标不再提供 MCM 开关（跟随罗盘总开关），
        // 高级玩家可改 config.json `ShowCompassIcons`（默认 true）。
        [SettingPropertyBool("{=LWN_mcm_show_compass}Show Compass", Order = -4, RequireRestart = false,
            HintText = "{=LWN_mcm_show_compass_hint}When enabled, a Skyrim-style compass band appears at the top of the screen in missions, showing direction letters (N/E/S/W), tick marks, and icons for important people with quests. Hidden while the full messaging panel or the system menu is open.")]
        [SettingPropertyGroup("{=LWN_mcm_grp_main}Settings")]
        public bool ShowCompass
        {
            get => Settings.Instance.ShowCompass;
            set => Settings.Instance.ShowCompass = value;
        }
    }
}
