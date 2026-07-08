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

        public NpcStance Stance => _stance ??= AttitudeSystem.ComputeStance(Speaker, Event);

        /// <summary>WorldEvent 语境构造（现有调用路径，不变）</summary>
        public PlaceholderResolver(WorldEvent evt, Hero speaker, Hero listener = null)
        {
            Event = evt;
            Speaker = speaker;
            Listener = listener ?? Hero.MainHero;
        }

        /// <summary>
        /// 🆕 Mission 层构造：无 WorldEvent 语境，用于警戒 BubbleSay / L3 质问台词。
        /// targetName / itemName 为脉冲上下文，传 null 时对应占位符解析为空字符串。
        /// </summary>
        public PlaceholderResolver(Hero speaker, Hero listener = null, string targetName = null, string itemName = null)
            : this(null, speaker, listener)
        {
            TargetName = targetName;
            ItemName = itemName;
        }

        /// <summary>
        /// 🆕 完整构造：WorldEvent + Mission 层脉冲上下文（L3 质问台词用）。
        /// </summary>
        public PlaceholderResolver(WorldEvent evt, Hero speaker, Hero listener, string targetName, string itemName)
            : this(evt, speaker, listener)
        {
            TargetName = targetName;
            ItemName = itemName;
        }

        /// <summary>解析模板中的所有占位符。未解析的保留原样并记日志。传 context 时同时打印模板→结果。</summary>
        public string Resolve(string template, string context = "")
        {
            if (string.IsNullOrEmpty(template)) return template;
            var unresolved = new List<string>();
            var result = System.Text.RegularExpressions.Regex.Replace(template, @"\{(\w+)\}", match =>
            {
                var key = match.Groups[1].Value;
                var value = ResolveOne(key);
                if (value == null) unresolved.Add(key);
                return value ?? match.Value;
            });
            if (!string.IsNullOrEmpty(context))
            {
                int maxLen = 120;
                string tpl = template.Length > maxLen ? template.Substring(0, maxLen) + "…" : template;
                string res = result.Length > maxLen ? result.Substring(0, maxLen) + "…" : result;
                DebugLogger.Log($"[Placeholder] {context}: \"{tpl}\" → \"{res}\"");
            }
            if (unresolved.Count > 0)
                DebugLogger.Log($"[Placeholder] UNRESOLVED in '{context}': {string.Join(", ", unresolved)}");
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

        private string ResolveOne(string key)
        {
            var evt = Event;
            var cfg = evt?.Config;
            var speaker = Speaker;
            var stance = Stance;

            switch (key)
            {
                // ── 🆕 NpcSpeech.csv 占位符别名（模板简写 → 标准 key）──
                case "PLAYER": return Listener?.Name?.ToString() ?? "你";
                case "SPEAKER": return speaker?.Name?.ToString() ?? "我";
                case "SPEAKER_SELF": return ResolveOne("SpeakerSelfRef");
                case "SPEAKER_PLAYER_ADDR": return ResolveOne("SpeakerPlayerAddr");
                case "SPEAKER_EMOTION": return ResolveOne("SpeakerEmotion");
                case "TARGET": return TargetName ?? "";
                case "ITEM": return ItemName ?? AgentControlHelper.GetWieldedWeaponName(Agent.Main) ?? "";
                case "StolenItemName": return ItemName ?? "";
                case "LOCATION":
                    return Settlement.CurrentSettlement?.Name?.ToString() ?? "";

                // A. 事件事实（EventConfig 级）
                case "EventTypeName": return cfg?.DisplayName ?? "犯罪";
                case "CrimeVerb": return cfg?.CrimeVerb ?? "做了";
                case "CrimeVerbPast": return cfg?.CrimeVerbPast ?? "出了事";
                case "CrimeVerbGerund": return cfg?.CrimeVerbGerund ?? "作案";
                case "CrimeScene": return cfg?.CrimeScene ?? "现场";
                case "VictimLabel": return cfg?.VictimLabel ?? "受害者";
                case "AuthorityRole": return cfg?.AuthorityRole ?? "村长";
                case "SeverityWord": return EventConfig.GetSeverityWord(evt?.Severity ?? 0);
                case "DefaultPenalty": return (cfg?.BaseRestitutionMultiplier ?? 1).ToString();

                // B. 事件实例（WorldEvent 级）
                case "EventId": return evt?.EventId ?? "";
                case "StolenCount": return (evt?.TotalStolenCount ?? 0).ToString();
                case "StolenItemDesc":
                {
                    if (evt == null) return "";
                    var items = evt.StolenItemsSnapshot;
                    if (items.Count == 0) return "";

                    var parts = new List<string>();
                    foreach (var kv in items)
                    {
                        var name = MBObjectManager.Instance.GetObject<ItemObject>(kv.Key)?.Name?.ToString() ?? kv.Key;
                        parts.Add(kv.Value == 1 ? $"一只{name}" : $"{kv.Value}只{name}");
                    }

                    if (parts.Count == 1) return parts[0];
                    if (parts.Count == 2) return $"{parts[0]}和{parts[1]}";
                    // 3+ 种不同物品：列举前两种 + 泛称总量
                    var total = items.Values.Sum();
                    return $"{parts[0]}、{parts[1]}等{total}只牲口";
                }
                case "StolenItemClause":  // 通用被盗物品从句：有物品→"，三只羊不见了"；暗杀等无物品犯罪→""
                {
                    var desc = ResolveOne("StolenItemDesc");
                    return string.IsNullOrEmpty(desc) ? "" : $"，{desc}不见了";
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
                    if (evt == null) return "最近";
                    float diff = (float)CampaignTime.Now.ToDays - evt.OccurredDay;
                    return diff < 0.5f ? "刚才" : diff < 1.5f ? "昨儿" : diff < 2.5f ? "前天"
                         : diff < 4f ? "前几天" : diff < 7f ? "上周" : diff < 14f ? "前阵子"
                         : diff < 30f ? "上个月" : "很久以前";
                case "DaysSinceDiscovery":
                    return evt != null ? ((int)((float)CampaignTime.Now.ToDays - evt._stageEnteredDay)).ToString() : "0";
                case "DaysRemaining":
                    if (evt == null) return "0";
                    return ((cfg?.InvestigationWindowDays ?? 7) - (int)((float)CampaignTime.Now.ToDays - evt.OccurredDay)).ToString();
                case "InvestigationDuration":
                    return $"查了{(evt != null ? (int)((float)CampaignTime.Now.ToDays - evt._stageEnteredDay) : 0)}天了";

                // D. 公共认知
                case "PublicAwarenessWord":
                    return (evt?.PublicAwareness ?? 0) switch
                    {
                        < 0.1f => "还没人知道", < 0.2f => "私下在议论",
                        < 0.5f => "很多人都知道了", < 0.8f => "传开了",
                        _ => "全社会都知道了"
                    };
                case "InvestigationProgressWord":
                    return (evt?.InvestigationProgress ?? 0) switch
                    {
                        < 0.3f => "刚开始查", < 0.6f => "正在查",
                        < 0.9f => "快查出来了", _ => "查清楚了"
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
                    if (sn == null) return "不知道是谁";
                    return $"{si}{sn}";
                case "SuspectIsPlayer": return (evt?.SuspectIsPlayer == true).ToString().ToLower();
                case "SuspectIsUnknown": return (evt == null || evt.SuspectHeroId == null).ToString().ToLower();
                case "InitiatorIsPlayer": return (evt?.InitiatorIsPlayer == true).ToString().ToLower();
                case "PlayerIsAccused": return (evt?.SuspectIsPlayer == true).ToString().ToLower();
                case "PlayerIsNotAccused": return (evt?.SuspectIsPlayer != true).ToString().ToLower();

                // E. 目击与证据
                case "WitnessExist": return ((evt?.WitnessCount ?? 0) > 0).ToString().ToLower();
                case "WitnessCount": return (evt?.WitnessCount ?? 0).ToString();
                case "WitnessCountWord":
                    return (evt?.WitnessCount ?? 0) switch
                    {
                        0 => "没人看见", 1 => "有一个人看见了",
                        _ => $"有{evt.WitnessCount}个人看见了"
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
                    return string.IsNullOrEmpty(pwn) ? "" : $"{pwi}{pwn}";
                case "WitnessesSilenced": return (evt?.WitnessesSilenced == true).ToString().ToLower();
                case "EvidenceExist": return ((evt?.EvidenceList?.Count ?? 0) > 0).ToString().ToLower();
                case "EvidenceCount": return (evt?.EvidenceList?.Count ?? 0).ToString();
                case "TopEvidenceDesc":
                    return evt?.EvidenceList?.OrderByDescending(e => e.Strength).FirstOrDefault()?.SourceDescription ?? "";

                // F. 说话者
                case "SpeakerName": return speaker?.Name?.ToString() ?? "";
                case "SpeakerIdentity": return AttitudeSystem.GetSocialIdentity(speaker);
                case "SpeakerRole":
                    return speaker != null && AttitudeSystem.ComputeStance(speaker, evt).WillAct > -1
                        ? (cfg?.AuthorityRole ?? "村民") : "村民";
                case "SpeakerSelfRef": return AttitudeSystem.GetSelfReference(speaker);
                case "SpeakerPlayerAddr": return AttitudeSystem.GetPlayerAddress(speaker);
                case "SpeakerEmotion":
                    return stance.Outrage > 0.7f ? "愤怒" : stance.Outrage > 0.3f ? "焦虑"
                         : stance.Fear > 0.5f ? "畏惧" : stance.SelfInterest > 0.4f ? "意味深长"
                         : stance.Sympathy < -0.3f ? "温和" : "冷淡";
                case "SpeakerAttitudeWord":
                    return stance.TowardActor switch
                    {
                        Attitude.Sympathetic => "同情", Attitude.Understanding => "理解",
                        Attitude.Neutral => "无所谓", Attitude.Disapproving => "不赞同",
                        Attitude.Angry => "愤怒", Attitude.Vengeful => "仇恨", _ => "平静"
                    };
                case "SpeakerIsAuthority":
                    return WorldEventStore.GetAuthorityNpc(evt) == speaker ? "true" : "false";

                // G. 听者
                case "ListenerName": return Listener?.Name?.ToString() ?? "你";
                case "ListenerIdentity": return AttitudeSystem.GetSocialIdentity(Listener);
                case "ListenerIsThief": return (evt?.InitiatorId == Hero.MainHero?.StringId).ToString().ToLower();
                case "ListenerIsSuspect": return (evt?.SuspectHeroId == Hero.MainHero?.StringId).ToString().ToLower();
                case "ListenerIsDetective": return (evt?.InitiatorId != Hero.MainHero?.StringId).ToString().ToLower();

                // G2. 对峙收尾（按 NPC 当前态度选不同的最后一句）
                case "ConfrontClosingLine":
                    string selfRef = AttitudeSystem.GetSelfReference(speaker);
                    return stance.Outrage > 0.7f ? $"（{speaker?.Name}盯着你的背影，一言不发。）"
                         : stance.Outrage > 0.3f ? "这事没完。你好自为之。"
                         : stance.Fear > 0.5f ? "（后退一步）……你走吧。别再来了。"
                         : $"{selfRef}话说到了。你自己掂量吧。";

                // H. 选项参数
                case "RestitutionCost": return (evt?.ComputeRestitutionCost() ?? 0).ToString();
                case "RestitutionCostOnSpot": return (evt?.ComputeOnSpotCost() ?? 0).ToString();
                case "RestitutionBreakdown": return evt?.GetRestitutionBreakdown() ?? "";
                case "BountyAmount": return (evt?.ComputeBountyAmount() ?? 0).ToString();
                case "CharmReprieveUsed": return (evt?.CharmReprieveUsed == true).ToString().ToLower();
                case "FailCount": return (evt?.FailCount ?? 0).ToString();
                case "FailCountRemaining": return (2 - (evt?.FailCount ?? 0)).ToString();

                default: return null;
            }
        }
    }
}
