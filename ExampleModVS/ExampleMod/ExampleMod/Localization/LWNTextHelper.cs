using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using TaleWorlds.Localization;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 本地化文本统一入口：封装 TextObject + PlaceholderResolver。
    /// TextObject("{=KEY}fallback") 自带 XML 查找：查到用翻译，查不到用 fallback。
    ///
    /// 🔴 English 特殊处理：引擎对 English 直接使用 C# fallback，不查 XML。
    ///    因此 InitializeEnglishFallback() 必须在启动时调用，从 English XML 构建 fallback 字典。
    ///
    /// 使用方式：
    ///   // 带 PlaceholderResolver 的叙事文本
    ///   string text = LWNTextHelper.Resolve("LWN_narr_supply_emergency_opening_any", resolver, "fallback text");
    ///
    ///   // 纯文本（无占位符）— 无显式 fallback 时自动从 English XML 取
    ///   string text = LWNTextHelper.ResolveText("LWN_ui_detention_pay_fine");
    ///
    ///   // 拼接场景：key + 显式键值对（语序由 XML 控制）
    ///   string text = LWNTextHelper.ResolveCompound("LWN_ph_suspect_description",
    ///       ("IDENTITY", identity), ("NAME", name));
    /// </summary>
    public static class LWNTextHelper
    {
        /// <summary>
        /// 按语言分桶的 fallback 字典：语言 id → (key → text)。
        /// 语言 id 来源：Languages/ 根目录 = "English"（LanguageData id 惯例）；子目录（CNs 等）
        /// 读各自 language_data.xml 的 id（如 "简体中文"）。
        /// 引擎对 English 语言直接使用 C# fallback 文本，不查 XML 翻译表，
        /// 因此必须在启动时加载此字典，为所有无显式 fallback 的调用提供英文兜底。
        /// </summary>
        private static Dictionary<string, Dictionary<string, string>> _langFallbacks =
            new Dictionary<string, Dictionary<string, string>>();

        /// <summary>
        /// 扫描 Languages/ 全部语言文件（根目录 = English + 各语言子目录），按语言分桶加载。
        /// 必须在 OnSubModuleLoad 中调用。
        /// </summary>
        /// <param name="modulePath">模块根目录路径（ModuleHelper.GetModuleFullPath("LivingWorldNpcs")）</param>
        public static void InitializeEnglishFallback(string modulePath)
        {
            _langFallbacks.Clear();
            string langDir = Path.Combine(modulePath, "ModuleData", "Languages");

            if (!Directory.Exists(langDir))
            {
                DebugLogger.Log($"LWNTextHelper: Languages dir not found at {langDir}");
                return;
            }

            // 根目录 = English 语言（LanguageData id="English" 惯例，见 Languages/language_data.xml）
            LoadXmlsIntoLang(langDir, "English");
            // 各语言子目录（CNs 等）→ 读各自 language_data.xml 的 id 作桶名
            foreach (string subDir in Directory.GetDirectories(langDir))
            {
                string langId = ReadLanguageDataId(subDir);
                if (!string.IsNullOrEmpty(langId))
                {
                    LoadXmlsIntoLang(subDir, langId);
                }
            }
        }

        /// <summary>读取语言子目录 language_data.xml 的 LanguageData id（如 "简体中文"）。</summary>
        private static string ReadLanguageDataId(string subDir)
        {
            string ldPath = Path.Combine(subDir, "language_data.xml");
            if (!File.Exists(ldPath)) return null;
            try
            {
                var doc = new XmlDocument();
                doc.Load(ldPath);
                return doc.DocumentElement?.GetAttribute("id");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"LWNTextHelper: Failed to read language id from {ldPath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>加载一个语言目录的全部 std_*.xml 进对应桶。</summary>
        private static void LoadXmlsIntoLang(string dir, string langId)
        {
            var dict = new Dictionary<string, string>();
            foreach (string xmlPath in Directory.GetFiles(dir, "std_*.xml", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var doc = new XmlDocument();
                    doc.Load(xmlPath);

                    var stringsNode = doc.SelectSingleNode("base/strings") ?? doc.SelectSingleNode("//strings");
                    if (stringsNode == null) continue;

                    foreach (XmlNode node in stringsNode.ChildNodes)
                    {
                        if (node.Name == "string" && node.NodeType != XmlNodeType.Comment && node.Attributes != null)
                        {
                            string id = node.Attributes["id"]?.Value;
                            string text = node.Attributes["text"]?.Value;
                            if (!string.IsNullOrEmpty(id) && text != null)
                            {
                                dict[id] = text;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"LWNTextHelper: Failed to load {Path.GetFileName(xmlPath)}: {ex.Message}");
                }
            }
            _langFallbacks[langId] = dict;
            DebugLogger.Log($"LWNTextHelper: Loaded {dict.Count} entries for language {langId}");
        }

        /// <summary>
        /// 从 English 语言桶取 key 对应的英文文本。未加载或 key 不存在返回 null。
        /// </summary>
        private static string GetEnglishFallback(string key)
        {
            if (_langFallbacks.TryGetValue("English", out var dict) && dict.TryGetValue(key, out string text))
                return text;
            return null;
        }

        /// <summary>
        /// 用 localization key 取文本 + 用 PlaceholderResolver 填占位符。
        /// TextObject("{=KEY}fallback") 自带 XML 查找：查到用翻译，查不到用 fallback。
        /// </summary>
        public static string Resolve(string key, PlaceholderResolver resolver, string fallback = null)
        {
            string fallbackText = fallback ?? GetEnglishFallback(key) ?? key;
            TextObject text = new TextObject($"{{={key}}}{fallbackText}", null);
            ApplyAllVariables(text, resolver);
            return text.ToString();
        }

        /// <summary>
        /// 不带 PlaceholderResolver 的纯文本解析。
        /// 无显式 fallback 时自动从 English XML 字典取英文兜底。
        /// </summary>
        public static string ResolveText(string key, string fallback = null)
        {
            string fallbackText = fallback ?? GetEnglishFallback(key) ?? key;
            TextObject text = new TextObject($"{{={key}}}{fallbackText}", null);
            return text.ToString();
        }

        /// <summary>
        /// LLM prompt 原始文本解析（🔴 不走 TextObject）。
        /// 为什么必须绕过 TextObject：prompt 静态块含大量 JSON 大括号（{"type": ...}），
        /// TextObject 的 Tokenizer 会把 {…} 当变量表达式解析，而 JSON 引号没有对应 token
        /// 定义 → FindTokenMatches 失败 → 字符串从第一个 { 起被整体截断（TaleWorlds.Localization
        /// Tokenizer 实测，见 plans/rules/pitfalls.md）。
        /// prompt 是 LLM 输入（铁律 13 豁免项），无需本地化渲染，直接从语言分桶字典取原文。
        /// 语言链：当前语言（MBTextManager.ActiveTextLanguage，如 "简体中文"/"English"）→ English 桶 → 空。
        /// 缺 key → 日志警告 + 返回空串（铁律 1：不崩，prompt 缺段降级，日志可查）。
        /// 与 py 测试脚本（Scripts/test_llm_plan.py _load_plan_prompts，读 CNs 中文）同源同语义。
        /// </summary>
        public static string ResolvePrompt(string key)
        {
            string raw = null;
            string langId = MBTextManager.ActiveTextLanguage;
            if (!string.IsNullOrEmpty(langId)
                && _langFallbacks.TryGetValue(langId, out var dict)
                && dict.TryGetValue(key, out raw))
            {
                // 当前语言命中
            }
            else
            {
                raw = GetEnglishFallback(key);
            }
            if (raw == null)
            {
                DebugLogger.Log($"LWNTextHelper: prompt key 缺失: {key}（该 prompt 段将缺失）");
                return string.Empty;
            }
            // XML 中 \n 字面量（两字符：反斜杠+n）→ 真实换行（与 py 侧 replace 语义一致）
            return raw.Replace((char)92 + "n", ((char)10).ToString());
        }

        /// <summary>
        /// 尝试取文本，key 不存在时返回 null（不返回 fallback/key 名）。
        /// 与 ResolveText 的区别：ResolveText 查不到返回 fallback；此方法查不到返回 null。
        /// </summary>
        public static string TryResolveText(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var text = new TextObject($"{{={key}}}{SentinelValue}", null);
            string result = text.ToString();
            return result == SentinelValue ? null : result;
        }
        private const string SentinelValue = "\0LWN_NOKEY\0";

        /// <summary>
        /// 拼接场景专用：key + 显式指定的键值对（不走 PlaceholderResolver 全局扫）。
        /// 用于语序敏感拼接——每个变量由调用方显式传递，不由 ResolveOne 全局匹配。
        /// 无 fallback 参数时自动从 English XML 字典取英文兜底。
        /// </summary>
        public static string ResolveCompound(string key, params (string var, string value)[] variables)
        {
            string fallbackText = GetEnglishFallback(key) ?? key;
            TextObject text = new TextObject($"{{={key}}}{fallbackText}", null);
            foreach (var (var, value) in variables)
            {
                if (!string.IsNullOrEmpty(value))
                    text.SetTextVariable(var, value);
            }
            return text.ToString();
        }

        /// <summary>
        /// 拼接场景专用（NAME 变量为 TextObject，保留 CJK 编码上下文）。
        /// 解决 agent.Name 等 TextObject 被 .ToString() 降级后，英文模式下 CJK 字符显示为 ? 的问题。
        /// </summary>
        public static string ResolveCompoundWithNameObject(string key, string fallback, TaleWorlds.MountAndBlade.Agent agent, string nameFallback)
        {
            TextObject text = new TextObject($"{{={key}}}{fallback}", null);
            var nameObj = agent?.Name;
            if (nameObj != null)
                TaleWorlds.Localization.MBTextManager.SetTextVariable("NAME", nameObj);
            else
                text.SetTextVariable("NAME", nameFallback);
            return text.ToString();
        }

        /// <summary>
        /// 拼接场景：混用 TextObject 和 string 变量。
        /// TextObject 变量保留原始编码（CJK 名在英文下不会变 ?），string 走标准路径。
        /// </summary>
        public static string ResolveCompoundMixed(string key, string fallback, params (string var, object value)[] variables)
        {
            TextObject text = new TextObject($"{{={key}}}{fallback}", null);
            foreach (var (var, value) in variables)
            {
                if (value == null) continue;
                if (value is TextObject to)
                    TaleWorlds.Localization.MBTextManager.SetTextVariable(var, to);
                else
                {
                    string s = value.ToString();
                    if (!string.IsNullOrEmpty(s))
                        text.SetTextVariable(var, s);
                }
            }
            return text.ToString();
        }

        /// <summary>
        /// 返回 TextObject（不调用 .ToString()），GauntletUI 原生渲染保留语言/字体上下文。
        /// 用于标题等需要嵌入 CJK 名字的场景。
        /// </summary>
        public static TextObject BuildCompoundTextObject(string key, string fallback, params (string var, object value)[] variables)
        {
            TextObject text = new TextObject($"{{={key}}}{fallback}", null);
            foreach (var (var, value) in variables)
            {
                if (value == null) continue;
                if (value is TextObject to)
                    TaleWorlds.Localization.MBTextManager.SetTextVariable(var, to);
                else
                {
                    string s = value.ToString();
                    if (!string.IsNullOrEmpty(s))
                        text.SetTextVariable(var, s);
                }
            }
            return text;
        }

        /// <summary>
        /// 拼接场景（无显式 fallback，自动从 English XML 取）：混用 TextObject 和 string 变量。
        /// </summary>
        public static string ResolveCompoundMixed(string key, params (string var, object value)[] variables)
        {
            string fallback = GetEnglishFallback(key) ?? key;
            return ResolveCompoundMixed(key, fallback, variables);
        }

        /// <summary>
        /// 拼接场景专用（带 fallback）：key + 英文兜底 + 显式键值对。
        /// XML 缺失时展示 fallback（其中的 {VAR} 仍会被显式变量替换）。
        /// </summary>
        public static string ResolveCompound(string key, string fallback, params (string var, string value)[] variables)
        {
            TextObject text = new TextObject($"{{={key}}}{fallback}", null);
            foreach (var (var, value) in variables)
            {
                if (!string.IsNullOrEmpty(value))
                    text.SetTextVariable(var, value);
            }
            return text.ToString();
        }

        /// <summary>从 PlaceholderResolver 提取全部占位符值，批量 SetTextVariable。</summary>
        private static void ApplyAllVariables(TextObject text, PlaceholderResolver r)
        {
            if (r == null) return;
            foreach (var key in AllKnownPlaceholders)
            {
                string value = r.ResolveOne(key);
                if (!string.IsNullOrEmpty(value))
                    text.SetTextVariable(key, value);
            }
        }

        /// <summary>
        /// PlaceholderResolver.ResolveOne 支持的全部占位符 key。
        /// 新增占位符时必须同步更新此列表。
        /// </summary>
        private static readonly string[] AllKnownPlaceholders =
        {
            // NpcSpeech 别名
            "PLAYER", "SPEAKER", "SPEAKER_SELF", "SPEAKER_PLAYER_ADDR", "SPEAKER_EMOTION",
            "TARGET", "ITEM", "StolenItemName", "LOCATION",

            // A. EventConfig
            "EventTypeName", "CrimeVerb", "CrimeVerbPast", "CrimeVerbGerund", "CrimeScene",
            "VictimLabel", "AuthorityRole", "SeverityWord", "DefaultPenalty",

            // B. WorldEvent
            "EventId", "StolenCount", "StolenItemDesc", "DiscoveryFacts", "StolenItemClause",
            "ActionDescription", "TargetHeroName", "TargetHeroIdentity", "TargetSettlementName",
            "LocationDetail",

            // C. Time
            "DaysSinceEvent", "TimeWord", "DaysSinceDiscovery", "DaysRemaining",
            "InvestigationDuration",

            // D. Cognition
            "PublicAwarenessWord", "InvestigationProgressWord",
            "SuspectName", "SuspectIdentity", "SuspectDescription",
            "SuspectIsPlayer", "SuspectIsUnknown", "InitiatorIsPlayer",
            "PlayerIsAccused", "PlayerIsNotAccused",

            // E. Witness/Evidence
            "WitnessExist", "WitnessCount", "WitnessCountWord",
            "PrimaryWitnessName", "PrimaryWitnessIdentity", "PrimaryWitnessDesc",
            "WitnessesSilenced", "EvidenceExist", "EvidenceCount", "TopEvidenceDesc",

            // F. Speaker
            "SpeakerName", "SpeakerIdentity", "SpeakerRole",
            "SpeakerSelfRef", "SpeakerPlayerAddr", "SpeakerEmotion",
            "SpeakerAttitudeWord", "SpeakerIsAuthority",

            // G. Listener
            "ListenerName", "ListenerIdentity",
            "ListenerIsThief", "ListenerIsSuspect", "ListenerIsDetective",

            // G2. Closing
            "ConfrontClosingLine",

            // H. Options
            "RestitutionCost", "RestitutionCostOnSpot", "RestitutionCostHaggle",
            "RestitutionBreakdown", "AlertFineCost", "BountyAmount",
            "CharmReprieveUsed", "FailCount", "FailCountRemaining",
        };
    }
}
