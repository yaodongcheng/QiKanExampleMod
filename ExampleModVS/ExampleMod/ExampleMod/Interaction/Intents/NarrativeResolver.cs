using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 叙事文本过滤器。所有维度列可选；null/空 = 不筛选；"Any" = 匹配任意值。
    /// </summary>
    public class NarrativeFilters
    {
        /// <summary>事件基础键（对话用：ProposeMarriage/Chat_Greeting 等）</summary>
        public string EventName;

        /// <summary>目标类型（ProposeMarriage/RecruitHero/DefectFaction 等）</summary>
        public string GoalType;

        /// <summary>成败（Success/Fail/Neutral/High/Low/Already/TooYoung/Female）</summary>
        public string Outcome;

        /// <summary>委托类型（BountyHunt/VillageDefense 等）</summary>
        public string Category;

        /// <summary>阶段（Opening/Closure）</summary>
        public string Phase;

        /// <summary>性格标签（Greedy/Brave/Desperate/Cautious/Any）</summary>
        public string PersonalityTrait;

        /// <summary>信任下限（含）</summary>
        public int? TrustMin;

        /// <summary>信任上限（含）</summary>
        public int? TrustMax;

        /// <summary>评级（Perfect/Good/Passable/Failed，仅 Closure）</summary>
        public string Grade;

        /// <summary>荣誉段（High/Neutral/Low/Any）</summary>
        public string Honor;

        /// <summary>NPC 性别（Male/Female/Any）</summary>
        public string Gender;

        /// <summary>NPC 身份（Lord/Soldier/Civilian/Any）</summary>
        public string Identity;

        /// <summary>关系（预留）</summary>
        public string Relation;

        /// <summary>严重度 1-10（预留）</summary>
        public int? Severity;

        /// <summary>是否宿敌（预留）</summary>
        public bool? IsNemesis;

        /// <summary>宿敌等级（预留）</summary>
        public int? NemesisLevel;

        /// <summary>是否有伤疤（预留）</summary>
        public bool? HasScar;

        /// <summary>交手次数（预留）</summary>
        public int? TimesEncountered;

        /// <summary>上次结果（预留）</summary>
        public string LastOutcome;

        /// <summary>从 DialogueFactors 构建。</summary>
        public static NarrativeFilters FromDialogueFactors(string eventKey, DialogueFactors factors)
        {
            return new NarrativeFilters
            {
                EventName = eventKey,
                Honor = factors.Honor.ToString(),
                Gender = factors.Gender.ToString(),
                Identity = factors.Identity.ToString(),
            };
        }

        /// <summary>从旧 dialogueKey + success 构建（兼容旧 API）。</summary>
        public static NarrativeFilters FromDialogueKey(string dialogueKey, bool success)
        {
            string fullKey = dialogueKey + (success ? "_Success" : "_Fail");
            // 尝试解析旧 ID 中的多因素维度（如 Chat_Greeting_High_Any_Any）
            return ParseCompositeId(fullKey);
        }

        /// <summary>按 ID 查询（兼容旧 API）。</summary>
        public static NarrativeFilters FromId(string id)
        {
            return ParseCompositeId(id);
        }

        /// <summary>
        /// 解析复合 ID（如 Chat_Greeting_High_Any_Any → EventName=Chat, GoalType=Greeting, Honor=High, ...）
        /// 同时处理简单 ID（如 ProposeMarriage_Success → EventName=ProposeMarriage, Outcome=Success）
        /// </summary>
        private static NarrativeFilters ParseCompositeId(string id)
        {
            var filters = new NarrativeFilters();
            if (string.IsNullOrEmpty(id)) return filters;

            string[] parts = id.Split('_');

            // 尝试作为多因素 ID 解析（末尾三段可能是 Honor/Gender/Identity）
            if (parts.Length >= 4)
            {
                string last3 = parts[parts.Length - 3];
                string last2 = parts[parts.Length - 2];
                string last1 = parts[parts.Length - 1];

                // 检查末尾三段是否是 Honor_Gender_Identity 模式
                bool last3IsHonor = last3 == "High" || last3 == "Neutral" || last3 == "Low" || last3 == "Any";
                bool last2IsGender = last2 == "Male" || last2 == "Female" || last2 == "Any";
                bool last1IsIdentity = last1 == "Lord" || last1 == "Soldier" || last1 == "Civilian" || last1 == "Any";

                if (last3IsHonor && last2IsGender && last1IsIdentity)
                {
                    filters.Honor = last3;
                    filters.Gender = last2;
                    filters.Identity = last1;

                    // 剩余部分是 EventName + 可能的中间段
                    string[] remaining = parts.Take(parts.Length - 3).ToArray();

                    // 检查剩余部分的倒数第一个是否是 GoalType/Outcome
                    if (remaining.Length >= 2)
                    {
                        string maybeOutcome = remaining[remaining.Length - 1];
                        if (maybeOutcome == "Success" || maybeOutcome == "Fail" || maybeOutcome == "Neutral"
                            || maybeOutcome == "High" || maybeOutcome == "Low" || maybeOutcome == "Already"
                            || maybeOutcome == "TooYoung" || maybeOutcome == "Female")
                        {
                            filters.Outcome = maybeOutcome;
                            string maybeGoal = remaining[remaining.Length - 2];
                            // GoalType 从已知值判断
                            if (IsKnownGoalType(maybeGoal))
                            {
                                filters.GoalType = maybeGoal;
                                filters.EventName = string.Join("_", remaining.Take(remaining.Length - 2));
                            }
                            else
                            {
                                filters.EventName = string.Join("_", remaining.Take(remaining.Length - 1));
                            }
                        }
                        else
                        {
                            filters.EventName = string.Join("_", remaining);
                        }
                    }
                    else if (remaining.Length == 1)
                    {
                        filters.EventName = remaining[0];
                    }

                    return filters;
                }
            }

            // 简单 ID 解析：最后一段是 Outcome
            if (parts.Length >= 2)
            {
                string last = parts[parts.Length - 1];
                if (last == "Success" || last == "Fail" || last == "Neutral"
                    || last == "High" || last == "Low" || last == "Already"
                    || last == "TooYoung" || last == "Female")
                {
                    filters.Outcome = last;
                    string remaining = string.Join("_", parts.Take(parts.Length - 1));

                    // 检查倒数第二段是不是 GoalType
                    string[] remParts = remaining.Split('_');
                    if (remParts.Length >= 2)
                    {
                        string maybeGoal = remParts[remParts.Length - 1];
                        if (IsKnownGoalType(maybeGoal))
                        {
                            filters.GoalType = maybeGoal;
                            filters.EventName = string.Join("_", remParts.Take(remParts.Length - 1));
                        }
                        else
                        {
                            filters.EventName = remaining;
                        }
                    }
                    else
                    {
                        filters.EventName = remaining;
                    }
                    return filters;
                }
            }

            // 裸 ID：就是 EventName
            filters.EventName = id;
            return filters;
        }

        private static bool IsKnownGoalType(string s)
        {
            return s == "ProposeMarriage" || s == "RecruitHero" || s == "DefectFaction"
                || s == "Exaction" || s == "JoinInFaction" || s == "Greeting"
                || s == "Weather" || s == "Gossip" || s == "Praise"
                || s == "BubbleGreet" || s == "RecruitSoldier" || s == "Order";
        }

        /// <summary>检查某行的某列是否匹配过滤器值（Any = 匹配任意，空 = 不筛选）。</summary>
        public bool ColumnMatches(string filterValue, string rowValue)
        {
            if (string.IsNullOrEmpty(filterValue)) return true;       // 未提供过滤条件
            if (filterValue == "Any") return true;                   // 显式 Any
            if (string.IsNullOrEmpty(rowValue)) return true;          // 行未填此维度，视为 Any
            if (rowValue == "Any") return true;
            return string.Equals(filterValue, rowValue, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>NarrativeResolver 的查询结果。</summary>
    public class NarrativeResult
    {
        public string Text;
        public string Emotion;

        public NarrativeResult(string text, string emotion)
        {
            Text = text ?? "";
            Emotion = emotion ?? "normal";
        }
    }

    /// <summary>
    /// 统一叙事文本查询引擎。
    ///
    /// 从 Narrative.csv 查询所有面向玩家的文本，所有维度列可选，支持渐进式 fallback。
    /// Dialogue 和 CommissionNarrative 视图由 GameDatabase 从 Narrative 表自动筛选生成。
    ///
    /// 使用方式：
    ///   NarrativeResolver.Resolve(filters) → NarrativeResult
    ///   NarrativeResolver.GetDialogue(key, success, out emotion, target, agent)  // 兼容旧 API
    ///   NarrativeResolver.GetCommissionOpening(data, profile)                     // 委托开场
    ///   NarrativeResolver.GetCommissionClosure(data, profile, payer, grade)       // 委托结局
    /// </summary>
    public static class NarrativeResolver
    {
        #region 主查询

        /// <summary>
        /// 主查询入口：按 filters 构造 XML key，直接查本地化表。
        /// 返回匹配的文本和情绪，保证不返回 null。
        /// </summary>
        public static NarrativeResult Resolve(NarrativeFilters filters)
        {
            if (filters == null)
                // 兜底省略号：查询条件为空时的默认叙事文本
                return new NarrativeResult(LWNTextHelper.ResolveText("LWN_ph_ellipsis", "..."), "normal");

            string text = TryResolveByKey(filters);
            if (text != null)
                return new NarrativeResult(text, "normal");

            return GetCodeFallback(filters);
        }

        /// <summary>从 filters 构造候选 XML key，逐个尝试，命中返回文本，未命中返回 null。</summary>
        private static string TryResolveByKey(NarrativeFilters filters)
        {
            bool isCommission = !string.IsNullOrEmpty(filters.Category);
            bool isDialogue = !string.IsNullOrEmpty(filters.EventName);

            var keys = new List<string>();

            if (isCommission)
            {
                BuildCommissionKeys(keys, filters);
            }
            else if (isDialogue)
            {
                BuildDialogueKeys(keys, filters);
            }
            else
            {
                return null;
            }

            foreach (var key in keys)
            {
                string result = LWNTextHelper.TryResolveText(key);
                if (result != null) return result;
            }
            return null;
        }

        /// <summary>委托叙事 key：LWN_narr_{category}_{phase}_{trait}_{trustMin}_{trustMax}_{grade?}</summary>
        private static void BuildCommissionKeys(List<string> keys, NarrativeFilters filters)
        {
            string cat = filters.Category.ToLower();
            string phase = filters.Phase.ToLower();
            string trait = string.IsNullOrEmpty(filters.PersonalityTrait) || filters.PersonalityTrait == "Any"
                ? "any" : filters.PersonalityTrait.ToLower();
            int tMin = filters.TrustMin ?? 0;
            int tMax = filters.TrustMax ?? 100;
            string grade = filters.Grade?.ToLower();

            // 精确 → 泛化
            if (!string.IsNullOrEmpty(grade))
            {
                keys.Add($"LWN_narr_{cat}_{phase}_{trait}_{tMin}_{tMax}_{grade}");
                keys.Add($"LWN_narr_{cat}_{phase}_{trait}_0_100_{grade}");
                keys.Add($"LWN_narr_{cat}_{phase}_any_0_100_{grade}");
            }
            keys.Add($"LWN_narr_{cat}_{phase}_{trait}_{tMin}_{tMax}");
            keys.Add($"LWN_narr_{cat}_{phase}_{trait}_0_100");
            keys.Add($"LWN_narr_{cat}_{phase}_any_{tMin}_{tMax}");
            keys.Add($"LWN_narr_{cat}_{phase}_any_0_100");
        }

        /// <summary>对话叙事 key：LWN_narr_{eventName}_{outcome}_{honor?}_{gender?}_{identity?}</summary>
        private static void BuildDialogueKeys(List<string> keys, NarrativeFilters filters)
        {
            string evt = filters.EventName.ToLower();
            string outcome = filters.Outcome?.ToLower();
            string honor = filters.Honor?.ToLower();
            string gender = filters.Gender?.ToLower();
            string identity = filters.Identity?.ToLower();

            // 先试原始 EventName（兼容 WorldEvent_xxx 格式的 CSV ID）
            keys.Add($"LWN_narr_{evt}");

            if (!string.IsNullOrEmpty(outcome))
            {
                keys.Add($"LWN_narr_{evt}_{outcome}");

                if (!string.IsNullOrEmpty(honor))
                {
                    string g = gender ?? "any";
                    string i = identity ?? "any";
                    // 精确 → 逐维泛化
                    keys.Add($"LWN_narr_{evt}_{outcome}_{honor}_{g}_{i}");
                    keys.Add($"LWN_narr_{evt}_{outcome}_{honor}_{g}_any");
                    keys.Add($"LWN_narr_{evt}_{outcome}_{honor}_any_any");
                }
                keys.Add($"LWN_narr_{evt}_{outcome}_any_any_any");
            }
        }

        #endregion

        #region 兼容旧 API

        /// <summary>对抗类：按成败取台词（兼容旧 DialogueTemplateHelper.Get(key, success, ...)）。</summary>
        public static string GetDialogue(string dialogueKey, bool success, out string emotion,
            Hero target = null, Agent agent = null)
        {
            var filters = NarrativeFilters.FromDialogueKey(dialogueKey, success);
            var result = Resolve(filters);
            emotion = result.Emotion;
            return ApplyPlaceholders(result.Text, target, agent);
        }

        /// <summary>即时类/话题：按完整 ID 取台词（兼容旧 DialogueTemplateHelper.Get(id, ...)）。</summary>
        public static string GetDialogue(string id, out string emotion,
            Hero target = null, Agent agent = null)
        {
            var filters = NarrativeFilters.FromId(id);
            var result = Resolve(filters);
            emotion = result.Emotion;
            return ApplyPlaceholders(result.Text, target, agent);
        }

        /// <summary>多因素版：按 EventKey + Factors 查表（兼容旧 Get 多因素重载）。</summary>
        public static string GetDialogue(string eventKey, DialogueFactors factors, out string emotion,
            Hero target = null, Agent agent = null)
        {
            var filters = NarrativeFilters.FromDialogueFactors(eventKey, factors);
            var result = Resolve(filters);
            emotion = result.Emotion;
            return ApplyPlaceholders(result.Text, target, agent);
        }

        /// <summary>委托开场叙事。</summary>
        public static string GetCommissionOpening(CommissionData data, NPCProfile giverProfile)
        {
            // 委托数据为空时的开场兜底台词（本地化 key，缺 XML 时回退英文）
            if (data == null) return LWNTextHelper.ResolveText("LWN_narr_fallback_commission_opening_null", "I need someone to handle a matter for me.");

            // 如果有 WorldEvent 关联，优先使用事件背景叙事
            if (!string.IsNullOrEmpty(data.WorldEventId))
            {
                var worldEvent = WorldEventStore.FindEvent(data.WorldEventId);
                if (worldEvent != null)
                {
                    string eventNarrative = BuildWorldEventCommissionOpening(worldEvent, data);
                    if (!string.IsNullOrEmpty(eventNarrative))
                        return eventNarrative;
                }
            }

            // 回退 CSV 查询
            var filters = new NarrativeFilters
            {
                Category = data.Category.ToString(),
                Phase = "Opening",
                PersonalityTrait = giverProfile?.PersonalityTraits ?? "Any",
                TrustMin = TrustSystem.GetTrust(data.QuestGiver),
            };

            var result = Resolve(filters);
            if (IsFallbackText(result.Text))
                return GetCommissionFallback(data, "Opening", CommissionGrade.Passable);

            return SubstituteCommissionPlaceholders(result.Text, data);
        }

        /// <summary>基于 WorldEvent 背景生成委托开场叙事。优先从 CSV 读取，兜底硬编码。</summary>
        private static string BuildWorldEventCommissionOpening(WorldEvent evt, CommissionData data)
        {
            // 推导说话人的事件角色
            string role = DeriveEventRole(evt, data.QuestGiver);

            // 优先从 Narrative.csv 查表（按角色 + 类别逐级 fallback）
            string csvText = TryGetWorldEventNarrative(evt, data, "Opening", null, role);
            if (!string.IsNullOrEmpty(csvText))
                return csvText;

            // 兜底硬编码（角色 + 类别感知）
            return BuildHardcodedEventOpening(evt, data, role);
        }

        /// <summary>推导 NPC 在世界事件中的角色。</summary>
        private static string DeriveEventRole(WorldEvent evt, Hero speaker)
        {
            if (speaker == null || evt == null) return "Victim";
            string speakerId = speaker.StringId;
            if (speakerId == evt.InitiatorId) return "Instigator";
            if (speakerId == evt.TargetHeroId) return "Victim";
            // 代理人（村长替不在场的受害者发委托）
            return "Victim";
        }

        /// <summary>硬编码兜底：按事件类型 × 角色 × 委托类别 生成开场叙事。</summary>
        private static string BuildHardcodedEventOpening(WorldEvent evt, CommissionData data, string role)
        {
            // 地点名兜底：村庄名缺失时用本地化文本
            string loc = evt.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_narr_fallback_nearby", "around here");
            // 受害者名兜底：受害者缺失时用本地化称呼
            string victim = evt.TargetHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_narr_fallback_villager", "a villager");
            // 加害者名兜底：无名团伙用泛指称呼（本地化文本）
            string instigator = evt.IsGenericInstigator ? LWNTextHelper.ResolveText("LWN_narr_fallback_gang", "a gang of men") : (evt.InstigatorHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_narr_fallback_they", "they"));
            string reward = data.NegotiatedReward.ToString();
            bool isVictim = role == "Victim";

            // ── NobleConflict：贵族冲突（双方都可雇人，对立叙事）──
            if (evt.Type == EventType.NobleConflict)
            {
                if (isVictim)
                {
                    return data.Category switch
                    {
                        CommissionCategory.VillageDefense =>
                            // 贵族冲突·受害方·守村委托开场：敌军逼近，雇玩家守村
                            LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_nobleconflict_victim_villagedefense",
                                "The army of {INSTIGATOR} is closing in on {LOCATION}. Help us hold the village — our people must not suffer. {REWARD} denars.",
                                ("INSTIGATOR", instigator), ("LOCATION", loc), ("REWARD", reward)),
                        CommissionCategory.CaravanEscort =>
                            // 贵族冲突·受害方·护送家眷委托开场：战前撤离家眷细软
                            LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_nobleconflict_victim_caravanescort",
                                "{INSTIGATOR} could strike at any moment. Help me move my family and valuables out of {LOCATION} to a safe place — {REWARD} denars.",
                                ("INSTIGATOR", instigator), ("LOCATION", loc), ("REWARD", reward)),
                        CommissionCategory.SupplyEmergency =>
                            // 贵族冲突·受害方·囤积物资委托开场：围城前抢运物资
                            LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_nobleconflict_victim_supplyemergency",
                                "{INSTIGATOR} is about to besiege us. Before the roads are cut off, stock {LOCATION} with supplies — {REWARD} denars, the sooner the better.",
                                ("INSTIGATOR", instigator), ("LOCATION", loc), ("REWARD", reward)),
                        // 贵族冲突·受害方·其他委托开场：边境集结，雇佣兵打前哨
                        _ => LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_nobleconflict_victim_default",
                            "{INSTIGATOR} has massed troops at {LOCATION}'s border. {TARGET} needs a capable mercenary to scout ahead — {REWARD} denars, and you answer for your own life.",
                            ("INSTIGATOR", instigator), ("LOCATION", loc), ("TARGET", victim), ("REWARD", reward))
                    };
                }
                else // Instigator
                {
                    return data.Category switch
                    {
                        CommissionCategory.SupplyIntercept =>
                            // 贵族冲突·加害方·截断补给委托开场：拦截运往目标的补给
                            LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_nobleconflict_instigator_supplyintercept",
                                "I'm moving against {TARGET}. A shipment of supplies is heading to {LOCATION} — intercept it. The goods are yours, or hand them to me for {REWARD} denars.",
                                ("TARGET", victim), ("LOCATION", loc), ("REWARD", reward)),
                        CommissionCategory.DecoyMission =>
                            // 贵族冲突·加害方·诱敌委托开场：制造动静引开敌方斥候
                            LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_nobleconflict_instigator_decoymission",
                                "Before the attack, I need {TARGET}'s scouts drawn away. Take a small force to the other side of {LOCATION} and make a racket to draw their attention — {REWARD} denars.",
                                ("TARGET", victim), ("LOCATION", loc), ("REWARD", reward)),
                        // 贵族冲突·加害方·其他委托开场：目标部署拖太久，需人办杂活
                        _ => LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_nobleconflict_instigator_default",
                            "{TARGET}'s position at {LOCATION} has dragged on too long. I have some work that needs doing — {REWARD} denars, take your pick.",
                            ("TARGET", victim), ("LOCATION", loc), ("REWARD", reward))
                    };
                }
            }

            // ── TradeDispute：贸易争端（双方对立）──
            if (evt.Type == EventType.TradeDispute)
            {
                if (isVictim)
                {
                    return data.Category switch
                    {
                        CommissionCategory.SupplyEmergency =>
                            // 贸易争端·受害方·囤货委托开场：垄断者抬价，雇玩家破局
                            LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_tradedispute_victim_supplyemergency",
                                "{INSTIGATOR} has monopolized {LOCATION}'s market — grain prices have tripled. {TARGET}'s business can't hold out much longer — {REWARD} denars to help break the stranglehold.",
                                ("INSTIGATOR", instigator), ("LOCATION", loc), ("TARGET", victim), ("REWARD", reward)),
                        CommissionCategory.ProcurementAgent =>
                            // 贸易争端·受害方·代购委托开场：跨城代购绕开垄断
                            LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_tradedispute_victim_procurementagent",
                                "{INSTIGATOR} controls every source of goods in {LOCATION}. I need you to buy supplies from another city, bypassing his monopoly — {REWARD} denars.",
                                ("INSTIGATOR", instigator), ("LOCATION", loc), ("REWARD", reward)),
                        // 贸易争端·受害方·其他委托开场：粮价暴涨，雇玩家打破垄断困局
                        _ => LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_tradedispute_victim_default",
                            "Grain prices in {LOCATION} suddenly tripled — {INSTIGATOR} is monopolizing the market behind it. {TARGET}'s business is about to collapse; {REWARD} denars for your help in breaking this deadlock.",
                            ("INSTIGATOR", instigator), ("LOCATION", loc), ("TARGET", victim), ("REWARD", reward))
                    };
                }
                else // Instigator（垄断商）
                {
                    return data.Category switch
                    {
                        CommissionCategory.SupplyIntercept =>
                            // 贸易争端·加害方·截断货源委托开场：拦截绕过自己生意网的货源
                            LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_tradedispute_instigator_supplyintercept",
                                "Someone is trying to bypass my trade network in {LOCATION} and bring goods in from outside. Intercept that shipment — {REWARD} denars, and don't let it reach the city.",
                                ("LOCATION", loc), ("REWARD", reward)),
                        CommissionCategory.DecoyMission =>
                            // 贸易争端·加害方·诱敌委托开场：制造混乱转移对手注意
                            LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_tradedispute_instigator_decoymission",
                                "{TARGET}'s people are asking around in {LOCATION}, trying to poach my business. Stir up some confusion to divert their attention — {REWARD} denars.",
                                ("TARGET", victim), ("LOCATION", loc), ("REWARD", reward)),
                        // 贸易争端·加害方·其他委托开场：压住不服气的对手
                        _ => LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_tradedispute_instigator_default",
                            "Business in {LOCATION} answers to me now. {TARGET} won't accept it and wants to turn the tables — help me hold the ground — {REWARD} denars.",
                            ("TARGET", victim), ("LOCATION", loc), ("REWARD", reward))
                    };
                }
            }

            // ── Fugitive：逃犯（双方对立）──
            if (evt.Type == EventType.Fugitive)
            {
                if (isVictim)
                {
                    // 逃犯·受害方开场：真假难辨，先找到藏身处护送离开
                    return LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_fugitive_victim",
                        "{TARGET}'s case is not so simple — his pursuers call him a traitor, while he claims he was framed. First, help me find where he is hiding and escort him to safety — {REWARD} denars.",
                        ("TARGET", victim), ("REWARD", reward));
                }
                else
                {
                    // 逃犯·加害方开场：把叛徒揪出来，死活不论
                    return LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_fugitive_instigator",
                        "{TARGET} is a traitor — at least that's what his pursuers say. Whatever the truth, drag him out — dead or alive — for {REWARD} denars.",
                        ("TARGET", victim), ("REWARD", reward));
                }
            }

            // ── 以下事件只有受害方发委托（加害方是匪徒/刺客/天灾，不雇人）──
            if (!isVictim)
                return null; // 加害方没有委托叙事 → 返回 null，让调用方处理

            // ── 受害方通用叙事（按事件类型）──
            return evt.Type switch
            {
                EventType.BanditRaid =>
                    // 匪徒劫掠开场：受害者逃出报信，雇玩家打退匪徒
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_banditraid",
                        "{TARGET} fled {LOCATION} to raise the alarm — {INSTIGATOR} is raiding the village right now! The villagers scraped together {REWARD} denars to hire someone to drive them off. Will you help?",
                        ("TARGET", victim), ("LOCATION", loc), ("INSTIGATOR", instigator), ("REWARD", reward)),
                EventType.Kidnapping =>
                    // 绑架开场：家人急疯，雇玩家把人救回
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_kidnapping",
                        "{TARGET}'s family is frantic — {INSTIGATOR} has taken someone and set a ransom and a meeting point. We don't have {REWARD} denars to pay the ransom, but we can afford to hire you to bring them back.",
                        ("TARGET", victim), ("INSTIGATOR", instigator), ("REWARD", reward)),
                EventType.Famine =>
                    // 饥荒开场：村长求助，雇玩家买粮救命
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_famine",
                        "This is {TARGET}, the village elder of {LOCATION} — the village has run out of grain, and the old and young have eaten wild greens for three days. These {REWARD} denars are the last the villagers could scrape together; take them and buy grain to save lives.",
                        ("TARGET", victim), ("LOCATION", loc), ("REWARD", reward)),
                EventType.Betrayal =>
                    // 背叛开场：最信任的人卷款逃跑，雇玩家追回人财
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_betrayal",
                        "{TARGET}'s voice trembles — {INSTIGATOR}, his most trusted man, made off with the accounts and half the caravan. {REWARD} denars to bring back both the man and the money.",
                        ("TARGET", victim), ("INSTIGATOR", instigator), ("REWARD", reward)),
                EventType.DebtTrap =>
                    // 债务陷阱开场：高利贷滚雪球，雇玩家渡难关
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_debttrap",
                        "{TARGET} hangs his head — {INSTIGATOR}'s usury has grown to a sum he can never repay. If he doesn't, the land will be seized. {REWARD} denars — help my family through this crisis...",
                        ("TARGET", victim), ("INSTIGATOR", instigator), ("REWARD", reward)),
                EventType.RomanticConflict =>
                    // 情场冲突开场：替人出面解决决斗
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_romanticconflict",
                        "{TARGET} sighs — it's a long story. In short, someone needs to stand in for a duel on his behalf — {REWARD} denars for the task. The details can wait until you get there.",
                        ("TARGET", victim), ("REWARD", reward)),
                EventType.FalseAccusation =>
                    // 冤案开场：找证据救替罪羊
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_falseaccusation",
                        "The lord wants to make an example of someone, and {TARGET} became the scapegoat. I know who the real culprit is — but we need proof. {REWARD} denars to find that evidence and save a life.",
                        ("TARGET", victim), ("REWARD", reward)),
                EventType.InheritanceDispute =>
                    // 遗产纠纷开场：找回信物证明继承权
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_inheritancedispute",
                        "The old clan chief is gone, and the will has vanished. {TARGET} says his father entrusted a token to someone before his death — find it and the inheritance can be proven. {REWARD} denars.",
                        ("TARGET", victim), ("REWARD", reward)),
                EventType.SacredTheft =>
                    // 圣物失窃开场：从祠堂追回祖传圣物
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_sacredtheft",
                        "This is the heirloom of {LOCATION}'s clan — {INSTIGATOR} stole it from the shrine. Without it, the new chief cannot convene the clan council. {REWARD} denars to recover it.",
                        ("LOCATION", loc), ("INSTIGATOR", instigator), ("REWARD", reward)),
                EventType.Assassination =>
                    // 暗杀悬案开场：悬赏追查真凶
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_opening_assassination",
                        "{TARGET} is dead. {LOCATION} is restless — subordinates suspect each other. Someone has offered {REWARD} denars to find the true killer — will you take it?",
                        ("TARGET", victim), ("LOCATION", loc), ("REWARD", reward)),
                _ => null
            };
        }

        /// <summary>从 XML 直接查 WorldEvent 叙事文本（原 CSV 路径已移除）。
        /// Key 格式逐级 fallback：
        ///   1. LWN_narr_worldevent_{eventType}_{role}_{category}_{phase}_{grade}
        ///   2. LWN_narr_worldevent_{eventType}_{role}_{phase}_{grade}
        ///   3. LWN_narr_worldevent_{eventType}_{phase}_{grade}
        ///   4. LWN_narr_worldevent_{eventType}_{phase}（无 grade）</summary>
        private static string TryGetWorldEventNarrative(WorldEvent evt, CommissionData data, string phase, string grade, string role = null)
        {
            try
            {
                string et = evt.Type.ToString().ToLower();
                string gradeSuffix = !string.IsNullOrEmpty(grade) ? $"_{grade.ToLower()}" : "";
                string cat = data?.Category.ToString()?.ToLower();

                var keys = new List<string>();

                if (!string.IsNullOrEmpty(role) && !string.IsNullOrEmpty(cat))
                    keys.Add($"LWN_narr_worldevent_{et}_{role.ToLower()}_{cat}_{phase.ToLower()}{gradeSuffix}");

                if (!string.IsNullOrEmpty(role))
                    keys.Add($"LWN_narr_worldevent_{et}_{role.ToLower()}_{phase.ToLower()}{gradeSuffix}");

                keys.Add($"LWN_narr_worldevent_{et}_{phase.ToLower()}{gradeSuffix}");
                keys.Add($"LWN_narr_worldevent_{et}_{phase.ToLower()}");

                foreach (var key in keys)
                {
                    string result = LWNTextHelper.TryResolveText(key);
                    if (result != null)
                        return SubstituteCommissionPlaceholders(result, data);
                }
            }
            catch { }
            return null;
        }

        /// <summary>委托结账结局叙事。</summary>
        public static string GetCommissionClosure(CommissionData data, NPCProfile giverProfile,
            NPCProfile payerProfile, CommissionGrade grade)
        {
            // 委托数据为空时的结账兜底台词（本地化 key，缺 XML 时回退英文）
            if (data == null) return LWNTextHelper.ResolveText("LWN_narr_fallback_closure_payment_null", "Here is your payment.");

            // 如果有 WorldEvent 关联，优先使用事件背景结局
            string text;
            if (!string.IsNullOrEmpty(data.WorldEventId))
            {
                var worldEvent = WorldEventStore.FindEvent(data.WorldEventId);
                if (worldEvent != null)
                {
                    text = BuildWorldEventCommissionClosure(worldEvent, data, grade);
                    if (!string.IsNullOrEmpty(text))
                        goto appendPayer;
                }
            }

            int trust = TrustSystem.GetTrust(data.QuestGiver);
            var filters = new NarrativeFilters
            {
                Category = data.Category.ToString(),
                Phase = "Closure",
                PersonalityTrait = giverProfile?.PersonalityTraits ?? "Any",
                TrustMin = trust,
                TrustMax = 100,
                Grade = grade.ToString(),
            };

            var result = Resolve(filters);
            if (IsFallbackText(result.Text))
                text = GetCommissionFallback(data, "Closure", grade);
            else
                text = SubstituteCommissionPlaceholders(result.Text, data);

        appendPayer:
            // 如果结账人 ≠ 委托人，追加 payer 角度的台词
            if (payerProfile != null && giverProfile != null &&
                payerProfile.BaseHero != giverProfile.BaseHero)
            {
                // 结账人名兜底：结账人姓名缺失时用本地化称呼
                string payerName = payerProfile.BaseHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_narr_fallback_payer", "the payer");
                // 追加结账人转交台词：结账人 ≠ 委托人时说明钱的来源（{PAYER} 由 ResolveCompound 填充）
                text += LWNTextHelper.ResolveCompound("LWN_narr_fallback_payer_transfer",
                    "This money was entrusted to me by {PAYER} to pass on to you.", ("PAYER", payerName));
            }

            return text;
        }

        /// <summary>基于 WorldEvent 背景生成委托结账结局。优先从 CSV 读取。</summary>
        private static string BuildWorldEventCommissionClosure(WorldEvent evt, CommissionData data, CommissionGrade grade)
        {
            string role = DeriveEventRole(evt, data.QuestGiver);

            // 优先从 Narrative.csv 查表（角色感知）
            string csvText = TryGetWorldEventNarrative(evt, data, "Closure", grade.ToString(), role);
            if (!string.IsNullOrEmpty(csvText))
                return csvText;

            // 兜底硬编码
            // 地点名兜底：村庄名缺失时用本地化文本
            string loc = evt.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_narr_fallback_over_there", "over there");
            // 受害者名兜底：受害者缺失时用本地化称呼
            string victim = evt.TargetHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_narr_fallback_client", "the client");
            // 加害者名兜底：无名团伙用泛指称呼（本地化文本）
            string instigator = evt.IsGenericInstigator ? LWNTextHelper.ResolveText("LWN_narr_fallback_those_men", "those men") : (evt.InstigatorHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_narr_fallback_they", "they"));
            string reward = data.NegotiatedReward.ToString();

            return (evt.Type, grade) switch
            {
                (EventType.BanditRaid, CommissionGrade.Perfect) =>
                    // 结账·匪徒劫掠·完美完成：匪徒全灭，乡亲安睡
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_closure_banditraid_perfect",
                        "{TARGET} is in tears — {INSTIGATOR} was driven off for good, and the folk of {LOCATION} can finally sleep soundly. {REWARD} denars — it's all we can afford.",
                        ("TARGET", victim), ("INSTIGATOR", instigator), ("LOCATION", loc), ("REWARD", reward)),
                (EventType.BanditRaid, CommissionGrade.Good) =>
                    // 结账·匪徒劫掠·顺利完成：匪帮撤退，暂得安宁
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_closure_banditraid_good",
                        "The raiders have fled! {LOCATION} is safe for now. {REWARD} denars — take them.",
                        ("LOCATION", loc), ("REWARD", reward)),
                (EventType.BanditRaid, _) =>
                    // 结账·匪徒劫掠·勉强完成：总算有了结果
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_closure_banditraid_other",
                        "Well, there's a result at last. {REWARD} denars for your trouble.",
                        ("REWARD", reward)),

                (EventType.Kidnapping, CommissionGrade.Perfect) =>
                    // 结账·绑架·完美完成：人质毫发无伤回家
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_closure_kidnapping_perfect",
                        "{TARGET} throws his arms around the rescued one and breaks into sobs. {REWARD} denars... our whole family will remember this debt for a lifetime.",
                        ("TARGET", victim), ("REWARD", reward)),
                (EventType.Kidnapping, CommissionGrade.Good) =>
                    // 结账·绑架·顺利完成：人质平安归来
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_closure_kidnapping_good",
                        "They're back. {TARGET} grips your hand, unable to speak. {REWARD} denars — thank you.",
                        ("TARGET", victim), ("REWARD", reward)),
                (EventType.Kidnapping, _) =>
                    // 结账·绑架·勉强完成：人救回但过程波折
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_closure_kidnapping_other",
                        "The person is rescued, though the process was far from perfect... {REWARD} denars for your trouble.",
                        ("REWARD", reward)),

                (EventType.Famine, CommissionGrade.Perfect) =>
                    // 结账·饥荒·完美完成：粮食及时送到救急
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_closure_famine_perfect",
                        "The grain arrived just in time! The old and young of {LOCATION} finally have food. {TARGET} thanks you on behalf of the whole village — {REWARD} denars.",
                        ("LOCATION", loc), ("TARGET", victim), ("REWARD", reward)),
                (EventType.Famine, _) =>
                    // 结账·饥荒·勉强完成：粮食迟到但救急
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_closure_famine_other",
                        "The grain has arrived. It was late, but it still saved the day. {REWARD} denars.",
                        ("REWARD", reward)),

                (EventType.Betrayal, CommissionGrade.Perfect) =>
                    // 结账·背叛·完美完成：追回人财，讨回公道
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_closure_betrayal_perfect",
                        "{TARGET} looks at the recovered goods and is silent for a long time. 'He was once my most trusted man...' {REWARD} denars — thank you for setting things right.",
                        ("TARGET", victim), ("REWARD", reward)),
                (EventType.Betrayal, _) =>
                    // 结账·背叛·勉强完成：事情了结，伤痕难愈
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_closure_betrayal_other",
                        "It's over. {TARGET} sighs — some wounds cannot be mended with money. {REWARD} denars.",
                        ("TARGET", victim), ("REWARD", reward)),

                (EventType.DebtTrap, CommissionGrade.Perfect) =>
                    // 结账·债务陷阱·完美完成：债主退散，如释重负
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_closure_debttrap_perfect",
                        "{TARGET} kneels — 'I no longer have to hide from them.' {REWARD} denars — I will repay this kindness even if I must work like a beast of burden.",
                        ("TARGET", victim), ("REWARD", reward)),
                (EventType.DebtTrap, _) =>
                    // 结账·债务陷阱·勉强完成：债主暂缓骚扰
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_closure_debttrap_other",
                        "The creditors won't be coming around for a while. {TARGET} can finally breathe. {REWARD} denars.",
                        ("TARGET", victim), ("REWARD", reward)),

                (EventType.SacredTheft, CommissionGrade.Perfect) =>
                    // 结账·圣物失窃·完美完成：圣物完璧归祠
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_closure_sacredtheft_perfect",
                        "The relic returned to the shrine undamaged. The elders of {LOCATION} pay their respects with tears in their eyes — 'The ancestors' spirit has finally come home.' {REWARD} denars.",
                        ("LOCATION", loc), ("REWARD", reward)),
                (EventType.SacredTheft, _) =>
                    // 结账·圣物失窃·勉强完成：圣物追回但有磕碰
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_closure_sacredtheft_other",
                        "The relic is recovered, though a bit battered... {REWARD} denars.",
                        ("REWARD", reward)),

                (EventType.Assassination, CommissionGrade.Perfect) =>
                    // 结账·暗杀悬案·完美完成：真凶伏法，正义伸张
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_closure_assassination_perfect",
                        "The true killer has been brought to justice. {LOCATION} has returned to order — at least on the surface. {REWARD} denars — you made justice prevail.",
                        ("LOCATION", loc), ("REWARD", reward)),
                (EventType.Assassination, _) =>
                    // 结账·暗杀悬案·勉强完成：凶手已除但伤痕难愈
                    LWNTextHelper.ResolveCompound("LWN_narr_worldevent_closure_assassination_other",
                        "The killer has been dealt with. But {LOCATION}'s wounds will not heal quickly. {REWARD} denars.",
                        ("LOCATION", loc), ("REWARD", reward)),

                _ => null
            };
        }

        #endregion

        #region 占位符替换

        /// <summary>通用占位符替换（对话用）。</summary>
        public static string ApplyPlaceholders(string raw, Hero target, Agent agent)
        {
            if (string.IsNullOrEmpty(raw)) return raw;

            // 玩家名兜底：主英雄名缺失时用本地化文本（复用称呼兜底 key）
            string playerName = Hero.MainHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_ph_pronoun_you", "you");
            string npcName = target?.Name?.ToString()
                ?? agent?.Name?.ToString()
                // NPC 名兜底：目标与 Agent 名都缺失时用本地化文本
                ?? LWNTextHelper.ResolveText("LWN_ph_fallback_npc", "the other person");
            string world = Settings.Instance?.WorldDescription ?? "";

            return raw
                .Replace("{PLAYER}", playerName)
                .Replace("{NPC}", npcName)
                .Replace("{WORLD}", world ?? "")
                // {TERM_LORD} 占位符：贵族称呼词，由本地化文本控制
                .Replace("{TERM_LORD}", LWNTextHelper.ResolveText("LWN_ph_term_lord", "Lord"));
        }

        /// <summary>委托占位符替换。</summary>
        public static string SubstituteCommissionPlaceholders(string template, CommissionData data)
        {
            if (string.IsNullOrEmpty(template)) return template;

            if (data.TargetHero != null)
                // {TARGET} 占位符：目标人物名缺失时用本地化文本兜底
                template = template.Replace("{TARGET}", data.TargetHero.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_ph_fallback_target", "target"));
            else
                // {TARGET} 占位符：无目标人物时用本地化文本兜底
                template = template.Replace("{TARGET}", LWNTextHelper.ResolveText("LWN_ph_fallback_target", "target"));

            if (!string.IsNullOrEmpty(data.TargetSettlementId))
            {
                var s = Settlement.Find(data.TargetSettlementId);
                template = template.Replace("{LOCATION}", s?.Name?.ToString() ?? data.TargetSettlementId);
            }
            else
                // {LOCATION} 占位符：无目标城镇时用本地化文本兜底
                template = template.Replace("{LOCATION}", LWNTextHelper.ResolveText("LWN_ph_fallback_destination", "destination"));

            if (!string.IsNullOrEmpty(data.TargetItemId))
            {
                var item = MBObjectManager.Instance.GetObject<ItemObject>(data.TargetItemId);
                template = template.Replace("{ITEM}", item?.Name?.ToString() ?? data.TargetItemId);
            }
            else
                // {ITEM} 占位符：无目标物品时用本地化文本兜底
                template = template.Replace("{ITEM}", LWNTextHelper.ResolveText("LWN_ph_fallback_item", "something"));

            template = template.Replace("{REWARD}", data.NegotiatedReward.ToString());
            template = template.Replace("{DEPOSIT}", data.DepositAmount.ToString());
            // {GIVER} 占位符：委托人名缺失时用本地化文本兜底
            template = template.Replace("{GIVER}", data.QuestGiver?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_ph_fallback_giver", "client"));
            template = template.Replace("{COUNT}", data.TargetItemCount.ToString());
            template = template.Replace("{DAYS}", ((int)(data.TimeRemainingHours / 24f) + 1).ToString());
            // {PAYER} 占位符：结算人名字缺失时用本地化文本兜底
            template = template.Replace("{PAYER}", data.RewardPayer?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_ph_fallback_payer", "payer"));

            return template;
        }

        #endregion

        #region 兜底

        /// <summary>
        /// 尝试从 XML 解析叙事文本，未命中返回 null。
        /// 与 Resolve 的区别：Resolve 保证不返回 null（兜底 GetCodeFallback），此方法 null = 没查到。
        /// </summary>
        public static string TryResolveText(NarrativeFilters filters)
        {
            if (filters == null) return null;
            return TryResolveByKey(filters);
        }

        /// <summary>判断 Resolve 返回的文本是否是兜底文本。</summary>
        public static bool IsFallbackText(string text)
        {
            return string.IsNullOrEmpty(text);
        }

        /// <summary>查询无结果时的代码级硬编码兜底。</summary>
        private static NarrativeResult GetCodeFallback(NarrativeFilters filters)
        {
            // 委托 Opening 兜底：委托人开场白（含 {REWARD} 占位符）
            if (filters.Category != null && filters.Phase == "Opening")
                return new NarrativeResult(
                    // 委托开场兜底 key：本地化查不到时回退英文
                    LWNTextHelper.ResolveText("LWN_narr_fallback_commission_opening", "I need someone to handle something. {REWARD} denars. Will you take it?"), "normal");

            // 委托 Closure 兜底：按评级取结账台词（含 {REWARD} 占位符）
            if (filters.Category != null && filters.Phase == "Closure")
            {
                string gradeText = filters.Grade switch
                {
                    // 委托完美完成时的结账台词
                    "Perfect" => LWNTextHelper.ResolveText("LWN_narr_fallback_closure_perfect", "Well done! {REWARD} denars — you're more reliable than I thought."),
                    // 委托顺利完成时的结账台词
                    "Good" => LWNTextHelper.ResolveText("LWN_narr_fallback_closure_good", "It's done. {REWARD} denars, take it."),
                    // 委托勉强完成时的结账台词
                    "Passable" => LWNTextHelper.ResolveText("LWN_narr_fallback_closure_passable", "Finally done. {REWARD}, as agreed."),
                    // 委托失败时的结账台词
                    "Failed" => LWNTextHelper.ResolveText("LWN_narr_fallback_closure_failed", "Let's forget this one. Hope next time is better."),
                    // 未知评级时的结账兜底台词
                    _ => LWNTextHelper.ResolveText("LWN_narr_fallback_closure_default", "Here's {REWARD} denars for your trouble.")
                };
                return new NarrativeResult(gradeText, "normal");
            }

            // 对话 Success 兜底：成功台词的通用兜底
            if (filters.Outcome == "Success")
                // 对话成功兜底 key：本地化查不到时回退英文
                return new NarrativeResult(LWNTextHelper.ResolveText("LWN_narr_fallback_success", "...Fine, alright."), "positive");

            // 对话 Fail 兜底：失败台词的通用兜底
            if (filters.Outcome == "Fail")
                // 对话失败兜底 key：本地化查不到时回退英文
                return new NarrativeResult(LWNTextHelper.ResolveText("LWN_narr_fallback_fail", "...No."), "negative");

            // 通用兜底 key：任何查询都无结果时的最后兜底
            return new NarrativeResult(LWNTextHelper.ResolveText("LWN_narr_fallback_generic", "...Mm."), "normal");
        }

        /// <summary>委托叙事 CSV 查不到时的兜底。</summary>
        private static string GetCommissionFallback(CommissionData data, string phase, CommissionGrade grade)
        {
            if (phase == "Opening")
            {
                // 委托开场兜底（含目标）：TARGET/REWARD 由 ResolveCompound 显式填充
                string target = data.TargetHero?.Name?.ToString()
                    ?? (data.TargetSettlementId != null
                        // 目标地点名缺失时的本地化兜底
                        ? Settlement.Find(data.TargetSettlementId)?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_ph_fallback_place", "some place")
                        // 无目标地点时的本地化兜底
                        : LWNTextHelper.ResolveText("LWN_ph_fallback_target", "target"));
                // 委托开场兜底 key：含目标场景，TARGET/REWARD 由 ResolveCompound 显式填充
                return LWNTextHelper.ResolveCompound("LWN_narr_fallback_commission_opening_target",
                    ("TARGET", target), ("REWARD", data.NegotiatedReward.ToString()));
            }
            else
            {
                // 委托结账兜底：按评级取台词，复用 GetCodeFallback 的 Closure key
                return grade switch
                {
                    // 委托完美完成时的结账台词（{REWARD} 由 ResolveCompound 填充）
                    CommissionGrade.Perfect => LWNTextHelper.ResolveCompound("LWN_narr_fallback_closure_perfect", ("REWARD", data.NegotiatedReward.ToString())),
                    // 委托顺利完成时的结账台词（{REWARD} 由 ResolveCompound 填充）
                    CommissionGrade.Good => LWNTextHelper.ResolveCompound("LWN_narr_fallback_closure_good", ("REWARD", data.NegotiatedReward.ToString())),
                    // 委托勉强完成时的结账台词（{REWARD} 由 ResolveCompound 填充）
                    CommissionGrade.Passable => LWNTextHelper.ResolveCompound("LWN_narr_fallback_closure_passable", ("REWARD", data.NegotiatedReward.ToString())),
                    // 委托失败时的结账台词
                    CommissionGrade.Failed => LWNTextHelper.ResolveCompound("LWN_narr_fallback_closure_failed"),
                    // 未知评级时的结账兜底台词（{REWARD} 由 ResolveCompound 填充）
                    _ => LWNTextHelper.ResolveCompound("LWN_narr_fallback_closure_default", ("REWARD", data.NegotiatedReward.ToString()))
                };
            }
        }

        #endregion
    }
}
