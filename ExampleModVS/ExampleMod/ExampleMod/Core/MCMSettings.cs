using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
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
        // ── LLM 配置（透传核心 Settings；IsLLMReady 在调用点实时计算 → 无需重启）──
        [SettingPropertyText("{=LWN_mcm_llm_base_url}LLM API Base URL", Order = 4, RequireRestart = false,
            HintText = "{=LWN_mcm_llm_base_url_hint}The LLM API endpoint base URL, e.g. https://api.example.com/v1")]
        [SettingPropertyGroup("{=LWN_mcm_grp_main}Settings")]
        public string LLMBaseUrl
        {
            get => Settings.Instance.LLMBaseUrl;
            set => Settings.Instance.LLMBaseUrl = value;
        }

        [SettingPropertyText("{=LWN_mcm_llm_api_key}LLM API Key", Order = 3, RequireRestart = false,
            HintText = "{=LWN_mcm_llm_api_key_hint}Your LLM API key. Saved in the MCM settings file.")]
        [SettingPropertyGroup("{=LWN_mcm_grp_main}Settings")]
        public string LLMApiKey
        {
            get => Settings.Instance.LLMApiKey;
            set => Settings.Instance.LLMApiKey = value;
        }

        [SettingPropertyText("{=LWN_mcm_llm_model}LLM Model", Order = 2, RequireRestart = false,
            HintText = "{=LWN_mcm_llm_model_hint}The model name to use, e.g. gpt-4o-mini")]
        [SettingPropertyGroup("{=LWN_mcm_grp_main}Settings")]
        public string LLMModel
        {
            get => Settings.Instance.LLMModel;
            set => Settings.Instance.LLMModel = value;
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
    }
}
