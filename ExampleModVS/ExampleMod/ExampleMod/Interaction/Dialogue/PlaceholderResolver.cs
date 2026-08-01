using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 运行时占位符解析器：将 {PlaceholderKey} 替换为真实游戏数据。
    /// 约 80 个占位符，分九类：EventConfig/WorldEvent/Time/Cognition/Evidence/Speaker/Listener/Options/Result。
    /// 无 LLM 时直接拼接；有 LLM 时 ExportContext() 导出字典作为 prompt context。
    /// </summary>
    public class PlaceholderResolver
    {
        public WorldEvent Event;
        public Hero Speaker;
        public Hero Listener;
        private NpcStance? _stance;

        // 🆕 Mission 层脉冲上下文（警戒 BubbleSay / L3 质问台词用）
        public string TargetName;   // 脉冲事件受害者名
        public string ItemName;     // 脉冲事件被盗物品名

        /// <summary>当前对话 NPC 的目击证词。null = 不是目击者。从 WitnessTestimonies 匹配。</summary>
        public WitnessTestimony SpeakingWitness;

        /// <summary>
        /// 🆕 模板 NPC 的 CharacterObject（Hero 为 null 时的身份回退）。
        /// 用于 {SPEAKER}/{SpeakerName} 等占位符解析。
        /// </summary>
        public CharacterObject SpeakerCharacter;

        public NpcStance Stance => _stance ??= AttitudeSystem.ComputeStance(Speaker, Event);

        /// <summary>WorldEvent 语境构造（现有调用路径，不变）</summary>
        public PlaceholderResolver(WorldEvent evt, Hero speaker, Hero listener = null, CharacterObject speakerCharacter = null)
        {
            Event = evt;
            Speaker = speaker;
            Listener = listener ?? Hero.MainHero;
            SpeakerCharacter = speakerCharacter;
        }

        /// <summary>
        /// 🆕 Mission 层构造：无 WorldEvent 语境，用于警戒 BubbleSay / L3 质问台词。
        /// targetName / itemName 为脉冲上下文，传 null 时对应占位符解析为空字符串。
        /// </summary>
        public PlaceholderResolver(Hero speaker, Hero listener = null, string targetName = null, string itemName = null, CharacterObject speakerCharacter = null)
            : this(null, speaker, listener, speakerCharacter)
        {
            TargetName = targetName;
            ItemName = itemName;
        }

        /// <summary>
        /// 🆕 完整构造：WorldEvent + Mission 层脉冲上下文（L3 质问台词用）。
        /// </summary>
        public PlaceholderResolver(WorldEvent evt, Hero speaker, Hero listener, string targetName, string itemName, CharacterObject speakerCharacter = null)
            : this(evt, speaker, listener, speakerCharacter)
        {
            TargetName = targetName;
            ItemName = itemName;
        }

        /// <summary>
        /// 日志阶段标签（静态环境量）。非 null 时 [Placeholder] 日志会带上此前缀，
        /// 用于区分「对话预填充」（BuildScript 在对话开启时一次性构建整棵对话树、批量解析所有分支）
        /// 与运行时的零星解析。由 CrimeDialogueBuilder.BuildScript 进入时设置、退出时还原（try/finally）。
        /// </summary>
        public static string LogPhaseTag;

        /// <summary>解析模板中的所有占位符。未解析的保留原样并记日志。传 context 时同时打印模板→结果。</summary>
        public string Resolve(string template, string context = "")
        {
            //context的作用：单纯打日志调试用，不影响实际逻辑
            if (string.IsNullOrEmpty(template)) return template;
            var unresolved = new List<string>();
            var result = System.Text.RegularExpressions.Regex.Replace(template, @"\{(\w+)\}", match =>
            {
                var key = match.Groups[1].Value;
                var value = ResolveOne(key);
                if (string.IsNullOrEmpty(value)) unresolved.Add(key);
                return !string.IsNullOrEmpty(value) ? value : match.Value;
            });
            string phase = LogPhaseTag != null ? $"[{LogPhaseTag}]" : "";
            if (!string.IsNullOrEmpty(context))
            {
                int maxLen = 120;
                string tpl = template.Length > maxLen ? template.Substring(0, maxLen) + "…" : template;
                string res = result.Length > maxLen ? result.Substring(0, maxLen) + "…" : result;
                DebugLogger.Log($"[Placeholder]{phase} {context}: \"{tpl}\" → \"{res}\"");
            }
            if (unresolved.Count > 0)
                DebugLogger.Log($"[Placeholder]{phase} UNRESOLVED in '{context}': {string.Join(", ", unresolved)}");
            return result;
        }

        /// <summary>导出全部占位符为字典（供 LLM 使用）</summary>
        public Dictionary<string, string> ExportContext()
        {
            var dict = new Dictionary<string, string>();
            foreach (var prop in GetType().GetFields())
            {
                if (prop.Name.StartsWith("_")) continue;
                dict[prop.Name] = prop.GetValue(this)?.ToString() ?? "";
            }
            return dict;
        }

        internal string ResolveOne(string key)
        {
            var evt = Event;
            var cfg = evt?.Config;
            var speaker = Speaker;
            var stance = Stance;

            switch (key)
            {
                // ── 🆕 NpcSpeech.csv 占位符别名（模板简写 → 标准 key）──
                case "PLAYER":
                    // 玩家称呼占位符：指名道姓，无名时兜底"你"
                    return Listener?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_ph_pronoun_you", "you");
                case "SPEAKER":
                    // NPC 自称占位符：指名道姓，无名时兜底"我"
                    return speaker?.Name?.ToString() ?? SpeakerCharacter?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_ph_pronoun_me", "me");
                case "SPEAKER_SELF": return ResolveOne("SpeakerSelfRef");
                case "SPEAKER_PLAYER_ADDR": return ResolveOne("SpeakerPlayerAddr");
                case "SPEAKER_EMOTION": return ResolveOne("SpeakerEmotion");
                case "TARGET":
                {
                    // 优先从目击证词取（精准），回落旧 TargetName 字段
                    var primaryAction = SpeakingWitness?.Actions
                        ?.OrderByDescending(a => a.AlertValue).FirstOrDefault();
                    var result = primaryAction?.TargetName ?? TargetName ?? "";
                    DebugLogger.Log($"[Placeholder] {{TARGET}} resolve: SpeakingWitness={SpeakingWitness?.WitnessHeroId ?? SpeakingWitness?.TemplateId ?? "null"} primaryAction={primaryAction?.ActionType}:{primaryAction?.TargetName} r.TargetName={TargetName} → \"{result}\"");
                    return result;
                }
                case "ITEM":
                {
                    var primaryAction = SpeakingWitness?.Actions
                        ?.OrderByDescending(a => a.AlertValue).FirstOrDefault();
                    return primaryAction?.ItemName ?? ItemName ?? AgentControlHelper.GetWieldedWeaponName(Agent.Main) ?? "";
                }
                case "StolenItemName":
                {
                    var primaryAction = SpeakingWitness?.Actions
                        ?.OrderByDescending(a => a.AlertValue).FirstOrDefault();
                    return primaryAction?.ItemName ?? ItemName ?? "";
                }
                case "LOCATION":
                    return Settlement.CurrentSettlement?.Name?.ToString() ?? "";

                // A. 事件事实（EventConfig 级）
                case "EventTypeName":
                    // 事件类型名兜底：Config 未配置时默认显示"犯罪"
                    return cfg?.DisplayName ?? LWNTextHelper.ResolveText("LWN_ph_event_type_default", "crime");
                case "CrimeVerb":
                    // 罪行动词兜底（如"偷了"）
                    return cfg?.CrimeVerb ?? LWNTextHelper.ResolveText("LWN_ph_crime_verb", "did it");
                case "CrimeVerbPast":
                    // 罪行动词过去式兜底（如"出了事"）
                    return cfg?.CrimeVerbPast ?? LWNTextHelper.ResolveText("LWN_ph_crime_verb_past", "something happened");
                case "CrimeVerbGerund":
                    // 罪行动词动名词兜底（如"作案"）
                    return cfg?.CrimeVerbGerund ?? LWNTextHelper.ResolveText("LWN_ph_crime_verb_gerund", "committing a crime");
                case "CrimeScene":
                    // 案发现场名词兜底（如"现场"）
                    return cfg?.CrimeScene ?? LWNTextHelper.ResolveText("LWN_ph_crime_scene", "crime scene");
                case "VictimLabel":
                    // 受害者称谓兜底（如"受害者"）
                    return cfg?.VictimLabel ?? LWNTextHelper.ResolveText("LWN_ph_victim_label", "victim");
                case "AuthorityRole": return WorldEventStore.GetAuthorityRoleDisplayName(evt);
                case "SeverityWord": return EventConfig.GetSeverityWord(evt?.Severity ?? 0);
                case "DefaultPenalty": return (cfg?.BaseRestitutionMultiplier ?? 1).ToString();

                // B. 事件实例（WorldEvent 级）
                case "EventId": return evt?.EventId ?? "";
                case "StolenCount": return (evt?.TotalStolenCount ?? 0).ToString();
                case "StolenItemDesc":
                {
                    // 统一收口到 WorldEvent.BuildStolenItemsDescription（量词分类/金面额），禁止本地重复实现
                    if (evt == null || evt.TotalStolenCount == 0) return "";
                    return evt.BuildStolenItemsDescription();
                }
                case "DiscoveryFacts":  // 案情事实句（袭击+失窃如实还原）：发现通知/对话模板共用
                    return evt?.BuildDiscoveryFacts() ?? "";
                // 通用被盗物品从句：有物品→"，三只羊不见了"；暗杀等无物品犯罪→""
                case "StolenItemClause":
                {
                    var desc = ResolveOne("StolenItemDesc");
                    // 被盗物品从句：物品描述+“不见了”（语序由 XML 控制）
                    return string.IsNullOrEmpty(desc) ? "" : LWNTextHelper.ResolveCompound("LWN_ph_stolen_clause", ("DESC", desc));
                }
                case "ActionDescription":
                    return evt?.ActionDescription ?? "";
                case "TargetHeroName":
                    return (evt?.TargetHero?.Name?.ToString()) ?? "";
                case "TargetHeroIdentity":
                    return AttitudeSystem.GetSocialIdentity(evt?.TargetHero);
                case "TargetSettlementName":
                    return evt?.TargetSettlement?.Name?.ToString() ?? "";
                case "LocationDetail": return evt?.LocationName ?? "";

                // C. 时间
                case "DaysSinceEvent":
                    return evt != null ? ((int)((float)CampaignTime.Now.ToDays - evt.OccurredDay)).ToString() : "0";
                case "TimeWord":
                    // 事件距今时间词：无事件时兜底"最近"
                    if (evt == null) return LWNTextHelper.ResolveText("LWN_ph_time_recent", "recently");
                    float diff = (float)CampaignTime.Now.ToDays - evt.OccurredDay;
                    // 半天内：刚才
                    return diff < 0.5f ? LWNTextHelper.ResolveText("LWN_ph_time_just_now", "just now")
                        // 一天左右：昨儿
                         : diff < 1.5f ? LWNTextHelper.ResolveText("LWN_ph_time_yesterday", "yesterday")
                        // 两天左右：前天
                         : diff < 2.5f ? LWNTextHelper.ResolveText("LWN_ph_time_day_before", "day before yesterday")
                        // 三四天：前几天
                         : diff < 4f ? LWNTextHelper.ResolveText("LWN_ph_time_few_days_ago", "a few days ago")
                        // 一周内：上周
                         : diff < 7f ? LWNTextHelper.ResolveText("LWN_ph_time_last_week", "last week")
                        // 两周内：前阵子
                         : diff < 14f ? LWNTextHelper.ResolveText("LWN_ph_time_recently", "a while ago")
                        // 一月内：上个月
                         : diff < 30f ? LWNTextHelper.ResolveText("LWN_ph_time_last_month", "last month")
                        // 更早：很久以前
                         : LWNTextHelper.ResolveText("LWN_ph_time_long_ago", "long ago");
                case "DaysSinceDiscovery":
                    return evt != null ? ((int)((float)CampaignTime.Now.ToDays - evt._stageEnteredDay)).ToString() : "0";
                case "DaysRemaining":
                    if (evt == null) return "0";
                    return ((cfg?.InvestigationWindowDays ?? 7) - (int)((float)CampaignTime.Now.ToDays - evt.OccurredDay)).ToString();
                case "InvestigationDuration":
                    // 自进入调查阶段起经过的天数
                    int invDays = evt != null ? (int)((float)CampaignTime.Now.ToDays - evt._stageEnteredDay) : 0;
                    // 调查时长占位符：查了 N 天了（天数由 XML 变量注入）
                    return LWNTextHelper.ResolveCompound("LWN_ph_investigation_duration",
                        ("DAYS", invDays.ToString()));

                // D. 公共认知
                case "PublicAwarenessWord":
                    // 公共认知度词：按认知度分五档
                    return (evt?.PublicAwareness ?? 0) switch
                    {
                        // 认知度最低档：还没人知道
                        < 0.1f => LWNTextHelper.ResolveText("LWN_ph_awareness_none", "nobody knows yet"),
                        // 认知度低档：私下在议论
                        < 0.2f => LWNTextHelper.ResolveText("LWN_ph_awareness_rumors", "people are whispering"),
                        // 认知度中档：很多人都知道了
                        < 0.5f => LWNTextHelper.ResolveText("LWN_ph_awareness_many_know", "many people know"),
                        // 认知度高档：传开了
                        < 0.8f => LWNTextHelper.ResolveText("LWN_ph_awareness_spread", "news has spread"),
                        // 认知度最高档：全社会都知道了
                        _ => LWNTextHelper.ResolveText("LWN_ph_awareness_everyone", "everyone knows")
                    };
                case "InvestigationProgressWord":
                    // 调查进度词：按进度分四档
                    return (evt?.InvestigationProgress ?? 0) switch
                    {
                        // 进度低档：刚开始查
                        < 0.3f => LWNTextHelper.ResolveText("LWN_ph_investigation_started", "just started investigating"),
                        // 进度中低档：正在查
                        < 0.6f => LWNTextHelper.ResolveText("LWN_ph_investigation_ongoing", "investigating"),
                        // 进度中高档：快查出来了
                        < 0.9f => LWNTextHelper.ResolveText("LWN_ph_investigation_close", "close to finding out"),
                        // 进度最高档：查清楚了
                        _ => LWNTextHelper.ResolveText("LWN_ph_investigation_clear", "investigation complete")
                    };
                case "SuspectName":
                    if (evt == null || string.IsNullOrEmpty(evt.SuspectHeroId)) return null;
                    return Hero.FindFirst(h => h.StringId == evt.SuspectHeroId)?.Name?.ToString();
                case "SuspectIdentity":
                    if (evt == null || string.IsNullOrEmpty(evt.SuspectHeroId)) return null;
                    return AttitudeSystem.GetSocialIdentity(Hero.FindFirst(h => h.StringId == evt.SuspectHeroId));
                case "SuspectDescription":
                    var sn = ResolveOne("SuspectName");
                    var si = ResolveOne("SuspectIdentity");
                    // 嫌疑人身份未知时的兜底描述
                    if (sn == null) return LWNTextHelper.ResolveText("LWN_ph_suspect_unknown", "unknown who");
                    // 嫌疑人身份+姓名的完整描述（语序由 XML 控制）
                    return LWNTextHelper.ResolveCompound("LWN_ph_suspect_description",
                        ("IDENTITY", si ?? ""), ("NAME", sn ?? ""));
                case "SuspectIsPlayer": return (evt?.SuspectIsPlayer == true).ToString().ToLower();
                case "SuspectIsUnknown": return (evt == null || evt.SuspectHeroId == null).ToString().ToLower();
                case "InitiatorIsPlayer": return (evt?.InitiatorIsPlayer == true).ToString().ToLower();
                case "PlayerIsAccused": return (evt?.SuspectIsPlayer == true).ToString().ToLower();
                case "PlayerIsNotAccused": return (evt?.SuspectIsPlayer != true).ToString().ToLower();

                // E. 目击与证据
                case "WitnessExist": return ((evt?.WitnessCount ?? 0) > 0).ToString().ToLower();
                case "WitnessCount": return (evt?.WitnessCount ?? 0).ToString();
                case "WitnessCountWord":
                    // 目击人数词：0/1/多 三档
                    return (evt?.WitnessCount ?? 0) switch
                    {
                        // 无目击者
                        0 => LWNTextHelper.ResolveText("LWN_ph_witness_none", "nobody saw"),
                        // 一名目击者
                        1 => LWNTextHelper.ResolveText("LWN_ph_witness_one", "one person saw it"),
                        // 多名目击者（人数由 XML 变量控制）
                        _ => LWNTextHelper.ResolveCompound("LWN_ph_witness_multi", ("COUNT", evt.WitnessCount.ToString()))
                    };
                case "PrimaryWitnessName":
                    var firstW = evt?.WitnessHeroIds?.FirstOrDefault();
                    return firstW != null ? Hero.FindFirst(h => h.StringId == firstW)?.Name?.ToString() : "";
                case "PrimaryWitnessIdentity":
                    var fwh = evt?.WitnessHeroIds?.FirstOrDefault();
                    return fwh != null ? AttitudeSystem.GetSocialIdentity(Hero.FindFirst(h => h.StringId == fwh)) : "";
                case "PrimaryWitnessDesc":
                    var pwn = ResolveOne("PrimaryWitnessName");
                    var pwi = ResolveOne("PrimaryWitnessIdentity");
                    // 无名目击者不输出描述
                    if (string.IsNullOrEmpty(pwn)) return "";
                    // 目击者身份+姓名的完整描述（语序由 XML 控制）
                    return LWNTextHelper.ResolveCompound("LWN_ph_witness_description",
                        ("IDENTITY", pwi ?? ""), ("NAME", pwn));
                case "WitnessesSilenced": return (evt?.WitnessesSilenced == true).ToString().ToLower();
                case "EvidenceExist": return ((evt?.EvidenceList?.Count ?? 0) > 0).ToString().ToLower();
                case "EvidenceCount": return (evt?.EvidenceList?.Count ?? 0).ToString();
                case "TopEvidenceDesc":
                    return evt?.EvidenceList?.OrderByDescending(e => e.Strength).FirstOrDefault()?.SourceDescription ?? "";

                // F. 说话者
                case "SpeakerName": return speaker?.Name?.ToString() ?? SpeakerCharacter?.Name?.ToString() ?? "";
                case "SpeakerIdentity": return AttitudeSystem.GetSocialIdentity(speaker);
                case "SpeakerRole":
                    // 说话者身份角色：有行动意愿的权威显示职权名，否则兜底"村民"
                    if (speaker != null && AttitudeSystem.ComputeStance(speaker, evt).WillAct > -1)
                        return WorldEventStore.GetAuthorityRoleDisplayName(evt);
                    // 非权威 NPC 的兜底身份：村民
                    return LWNTextHelper.ResolveText("LWN_ph_role_villager", "villager");
                case "SpeakerSelfRef": return AttitudeSystem.GetSelfReference(speaker);
                case "SpeakerPlayerAddr": return AttitudeSystem.GetPlayerAddress(speaker);
                case "SpeakerEmotion":
                    // 说话者情绪词：按愤怒/恐惧/利益/同情四维分档
                    return stance.Outrage > 0.7f ? LWNTextHelper.ResolveText("LWN_ph_emotion_angry", "angry")
                        // 愤怒中等：焦虑
                         : stance.Outrage > 0.3f ? LWNTextHelper.ResolveText("LWN_ph_emotion_anxious", "anxious")
                        // 恐惧高：畏惧
                         : stance.Fear > 0.5f ? LWNTextHelper.ResolveText("LWN_ph_emotion_fearful", "fearful")
                        // 有利益诉求：意味深长
                         : stance.SelfInterest > 0.4f ? LWNTextHelper.ResolveText("LWN_ph_emotion_meaningful", "meaningful")
                        // 同情高：温和
                         : stance.Sympathy < -0.3f ? LWNTextHelper.ResolveText("LWN_ph_emotion_gentle", "gentle")
                        // 默认：冷淡
                         : LWNTextHelper.ResolveText("LWN_ph_emotion_cold", "cold");
                case "SpeakerAttitudeWord":
                    // 说话者态度词：按对玩家的态度枚举分档
                    return stance.TowardActor switch
                    {
                        // 态度：同情
                        Attitude.Sympathetic => LWNTextHelper.ResolveText("LWN_ph_attitude_sympathetic", "sympathetic"),
                        // 态度：理解
                        Attitude.Understanding => LWNTextHelper.ResolveText("LWN_ph_attitude_understanding", "understanding"),
                        // 态度：无所谓
                        Attitude.Neutral => LWNTextHelper.ResolveText("LWN_ph_attitude_neutral", "indifferent"),
                        // 态度：不赞同
                        Attitude.Disapproving => LWNTextHelper.ResolveText("LWN_ph_attitude_disapproving", "disapproving"),
                        // 态度：愤怒（复用情绪词 key）
                        Attitude.Angry => LWNTextHelper.ResolveText("LWN_ph_emotion_angry", "angry"),
                        // 态度：仇恨
                        Attitude.Vengeful => LWNTextHelper.ResolveText("LWN_ph_attitude_vengeful", "vengeful"),
                        // 默认态度：平静
                        _ => LWNTextHelper.ResolveText("LWN_ph_attitude_calm", "calm")
                    };
                case "SpeakerIsAuthority":
                    return WorldEventStore.GetAuthorityNpc(evt) == speaker ? "true" : "false";

                // G. 听者
                case "ListenerName":
                    // 听者称呼占位符：指名道姓，无名时兜底"你"
                    return Listener?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_ph_pronoun_you", "you");
                case "ListenerIdentity": return AttitudeSystem.GetSocialIdentity(Listener);
                case "ListenerIsThief": return (evt?.InitiatorId == Hero.MainHero?.StringId).ToString().ToLower();
                case "ListenerIsSuspect": return (evt?.SuspectHeroId == Hero.MainHero?.StringId).ToString().ToLower();
                case "ListenerIsDetective": return (evt?.InitiatorId != Hero.MainHero?.StringId).ToString().ToLower();

                // G2. 对峙收尾（按 NPC 当前态度选不同的最后一句）
                case "ConfrontClosingLine":
                    string selfRef = AttitudeSystem.GetSelfReference(speaker);
                    // 对峙收尾句：按 NPC 态度分四档（沉默/未了/驱赶/警告）
                    return stance.Outrage > 0.7f ? LWNTextHelper.ResolveText("LWN_ph_closing_silence", "...")
                        // 中度愤怒：事情没完
                         : stance.Outrage > 0.3f ? LWNTextHelper.ResolveText("LWN_ph_closing_unfinished", "This isn't over. Watch yourself.")
                        // 高度恐惧：驱赶玩家
                         : stance.Fear > 0.5f ? LWNTextHelper.ResolveText("LWN_ph_closing_leave", "...Just go. Don't come back.")
                        // 默认：警告式收尾（自称由 XML 变量控制）
                         : LWNTextHelper.ResolveCompound("LWN_ph_closing_warned", ("SELF_REF", selfRef ?? ""));

                // H. 选项参数
                case "RestitutionCost": return (evt != null ? CrimePenaltyCalculator.ComputeCost(evt, CostType.Restitution) : 0).ToString();
                case "RestitutionCostOnSpot": return (evt != null ? CrimePenaltyCalculator.ComputeCost(evt, CostType.OnSpot) : 0).ToString();
                case "RestitutionCostHaggle": return (int)((evt != null ? CrimePenaltyCalculator.ComputeCost(evt, CostType.Restitution) : 0) * 0.5f) + "";
                case "RestitutionBreakdown": return evt?.GetRestitutionBreakdown() ?? "";
                case "AlertFineCost": return (evt != null ? CrimePenaltyCalculator.ComputeCost(evt, CostType.Restitution) : 0).ToString();
                case "BountyAmount": return (evt != null ? CrimePenaltyCalculator.ComputeCost(evt, CostType.Bounty) : 0).ToString();
                case "CharmReprieveUsed": return (evt?.CharmReprieveUsed == true).ToString().ToLower();
                case "FailCount": return (evt?.FailCount ?? 0).ToString();
                case "FailCountRemaining": return (2 - (evt?.FailCount ?? 0)).ToString();

                default: return null;
            }
        }
    }
}
