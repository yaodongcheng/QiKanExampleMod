using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using LivingWorldNpcs.Story;

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
        /// 主查询入口：按 filters 查询 Narrative.csv，渐进式 fallback。
        /// 返回匹配的文本和情绪，保证不返回 null。
        /// </summary>
        public static NarrativeResult Resolve(NarrativeFilters filters)
        {
            if (filters == null)
                return new NarrativeResult("……", "normal");

            var table = GameDatabase.Narrative;
            if (table == null)
                return GetCodeFallback(filters);

            var allRows = table.GetAll().ToList();
            if (allRows.Count == 0)
                return GetCodeFallback(filters);

            // 决定使用哪种 fallback 策略
            bool isCommission = !string.IsNullOrEmpty(filters.Category);
            bool isDialogue = !string.IsNullOrEmpty(filters.EventName);

            DynamicRecord match = null;

            if (isCommission)
                match = ResolveCommission(allRows, filters);
            else if (isDialogue)
                match = ResolveDialogue(allRows, filters);
            else
                match = ResolveSimple(allRows, filters);

            if (match != null)
            {
                var lines = match.GetList("Text");
                string text = "";
                if (lines != null && lines.Count > 0)
                    text = lines[MBRandom.RandomInt(lines.Count)];
                if (string.IsNullOrEmpty(text))
                    text = match.GetString("Text"); // fallback to string read

                string emotion = match.GetString("Emotion", "normal");
                if (string.IsNullOrEmpty(emotion)) emotion = "normal";

                return new NarrativeResult(text, emotion);
            }

            return GetCodeFallback(filters);
        }

        /// <summary>委托叙事查询：Category + Phase 为主键。</summary>
        private static DynamicRecord ResolveCommission(List<DynamicRecord> rows, NarrativeFilters filters)
        {
            // 1. Category + Phase 精确
            var candidates = rows.Where(r =>
                r.GetString("Category") == filters.Category &&
                r.GetString("Phase") == filters.Phase
            ).ToList();

            if (candidates.Count == 0)
                return null;

            // 2. Grade（仅 Closure 阶段）
            if (filters.Phase == "Closure" && !string.IsNullOrEmpty(filters.Grade))
            {
                var gradeMatch = candidates.Where(r =>
                    r.GetString("Grade") == filters.Grade).ToList();
                if (gradeMatch.Count > 0)
                    candidates = gradeMatch;
                // 精确 Grade 不匹配 → 保留全部 candidates 兜底
            }

            // 3. PersonalityTrait：精确 > Any
            var traitFiltered = FilterByTrait(candidates, filters.PersonalityTrait);
            if (traitFiltered.Count > 0)
                candidates = traitFiltered;

            // 4. Trust 区间
            if (filters.TrustMin.HasValue || filters.TrustMax.HasValue)
            {
                int trust = filters.TrustMin ?? 0;
                int trustMax = filters.TrustMax ?? 100;
                var trustMatch = candidates.Where(r =>
                    trust >= r.GetInt("TrustMin", 0) &&
                    trust <= r.GetInt("TrustMax", 100)
                ).ToList();
                if (trustMatch.Count > 0)
                    candidates = trustMatch;
            }

            // 5. 随机取一条
            return candidates.Count > 0
                ? candidates[MBRandom.RandomInt(0, candidates.Count)]
                : null;
        }

        /// <summary>对话查询：EventName 为主键，Honor/Gender/Identity 渐进 fallback。</summary>
        private static DynamicRecord ResolveDialogue(List<DynamicRecord> rows, NarrativeFilters filters)
        {
            // 1. EventName 精确
            var byEvent = rows.Where(r =>
                r.GetString("EventName") == filters.EventName
            ).ToList();

            // 如果 EventName 无匹配，尝试直接用 ID 搜索（兼容裸 ID 查询）
            if (byEvent.Count == 0)
            {
                byEvent = rows.Where(r =>
                    r.GetString("ID") == filters.EventName ||
                    r.GetString("EventName") == filters.EventName
                ).ToList();
            }

            if (byEvent.Count == 0)
            {
                // 尝试匹配 ID 前缀
                byEvent = rows.Where(r =>
                    r.GetString("ID").StartsWith(filters.EventName, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            if (byEvent.Count == 0)
                return null;

            // 2. Outcome 筛选
            if (!string.IsNullOrEmpty(filters.Outcome))
            {
                var outcomeMatch = byEvent.Where(r =>
                    filters.ColumnMatches(filters.Outcome, r.GetString("Outcome"))
                ).ToList();
                if (outcomeMatch.Count > 0)
                    byEvent = outcomeMatch;
            }

            // 3. GoalType 筛选
            if (!string.IsNullOrEmpty(filters.GoalType))
            {
                var goalMatch = byEvent.Where(r =>
                    filters.ColumnMatches(filters.GoalType, r.GetString("GoalType"))
                ).ToList();
                if (goalMatch.Count > 0)
                    byEvent = goalMatch;
            }

            // 4. Honor + Gender + Identity 渐进 fallback
            var result = ResolveWithHonorGenderIdentity(byEvent, filters);
            if (result != null) return result;

            // 5. 最宽泛：Any/Any/Any
            var anyMatch = byEvent.FirstOrDefault(r =>
                (r.GetString("Honor") == "Any" || string.IsNullOrEmpty(r.GetString("Honor"))) &&
                (r.GetString("Gender") == "Any" || string.IsNullOrEmpty(r.GetString("Gender"))) &&
                (r.GetString("Identity") == "Any" || string.IsNullOrEmpty(r.GetString("Identity")))
            );
            if (anyMatch != null) return anyMatch;

            // 6. 随便返回一条匹配 EventName 的
            return byEvent.Count > 0
                ? byEvent[MBRandom.RandomInt(0, byEvent.Count)]
                : null;
        }

        /// <summary>Honor/Gender/Identity 渐进 fallback，优先级与旧 BuildFallbackIds 一致。</summary>
        private static DynamicRecord ResolveWithHonorGenderIdentity(
            List<DynamicRecord> candidates, NarrativeFilters filters)
        {
            string h = filters.Honor ?? "Any";
            string g = filters.Gender ?? "Any";
            string i = filters.Identity ?? "Any";

            // 优先级：exact → 逐维改 Any
            var fallbackOrders = new List<(string honor, string gender, string identity)>
            {
                (h, g, i),           // exact
                (h, g, "Any"),       // wildcard identity
                ("Any", g, i),       // wildcard honor, keep gender+identity
                ("Any", g, "Any"),   // wildcard honor+identity, keep gender
                (h, "Any", "Any"),   // wildcard gender+identity, keep honor
                ("Any", "Any", "Any"), // 最宽泛
            };

            foreach (var (fh, fg, fi) in fallbackOrders)
            {
                var match = candidates.FirstOrDefault(r =>
                    DimensionMatches(fh, r.GetString("Honor")) &&
                    DimensionMatches(fg, r.GetString("Gender")) &&
                    DimensionMatches(fi, r.GetString("Identity"))
                );
                if (match != null) return match;
            }

            return null;
        }

        private static bool DimensionMatches(string filter, string rowValue)
        {
            if (filter == "Any" || string.IsNullOrEmpty(filter)) return true;
            if (string.IsNullOrEmpty(rowValue)) return true;  // 行未填视为 Any
            if (rowValue == "Any") return true;
            return string.Equals(filter, rowValue, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>简单查询：不区分对话/委托模式，按所有非空过滤列精确匹配。</summary>
        private static DynamicRecord ResolveSimple(List<DynamicRecord> rows, NarrativeFilters filters)
        {
            var candidates = rows.Where(r =>
            {
                if (!string.IsNullOrEmpty(filters.GoalType) &&
                    !filters.ColumnMatches(filters.GoalType, r.GetString("GoalType")))
                    return false;
                if (!string.IsNullOrEmpty(filters.Outcome) &&
                    !filters.ColumnMatches(filters.Outcome, r.GetString("Outcome")))
                    return false;
                return true;
            }).ToList();

            return candidates.Count > 0
                ? candidates[MBRandom.RandomInt(0, candidates.Count)]
                : null;
        }

        /// <summary>按 PersonalityTrait 筛选：精确匹配优先于 Any。</summary>
        private static List<DynamicRecord> FilterByTrait(List<DynamicRecord> candidates, string trait)
        {
            if (string.IsNullOrEmpty(trait) || trait == "Any")
            {
                // 不指定性格 → 优先返回 Any 行
                var any = candidates.Where(r =>
                {
                    string t = r.GetString("PersonalityTrait");
                    return string.IsNullOrEmpty(t) || t == "Any";
                }).ToList();
                return any.Count > 0 ? any : candidates;
            }

            // 指定性格 → 精确匹配优先
            var exact = candidates.Where(r =>
                string.Equals(r.GetString("PersonalityTrait"), trait, StringComparison.OrdinalIgnoreCase)
            ).ToList();
            if (exact.Count > 0)
                return exact;

            // 没有精确匹配 → 返回 Any 兜底
            var anyFallback = candidates.Where(r =>
            {
                string t = r.GetString("PersonalityTrait");
                return string.IsNullOrEmpty(t) || t == "Any";
            }).ToList();
            return anyFallback.Count > 0 ? anyFallback : new List<DynamicRecord>();
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
            if (data == null) return "我需要有人帮我办一件事。";

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
            string loc = evt.TargetSettlement?.Name?.ToString() ?? "附近";
            string victim = evt.TargetHero?.Name?.ToString() ?? "村民";
            string instigator = evt.IsGenericInstigator ? "一伙人" : (evt.InstigatorHero?.Name?.ToString() ?? "他们");
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
                            $"{instigator}的大军正在逼近{loc}。帮我们守住村子，不能让乡亲们遭殃——{reward}第纳尔。",
                        CommissionCategory.CaravanEscort =>
                            $"{instigator}随时会打过来。帮我把家眷和细软撤出{loc}，护送到安全的地方——{reward}第纳尔。",
                        CommissionCategory.SupplyEmergency =>
                            $"{instigator}要围城了。趁道路还没被切断，帮{loc}囤一批物资——{reward}第纳尔，越快越好。",
                        _ => $"{instigator}在{loc}边境集结了兵力。{victim}需要一个有本事的佣兵替他打前哨——{reward}第纳尔，生死自负。"
                    };
                }
                else // Instigator
                {
                    return data.Category switch
                    {
                        CommissionCategory.SupplyIntercept =>
                            $"我要对{victim}动手了。有一批补给正运往{loc}——你去截下来。物资归你，或者交给我换{reward}第纳尔。",
                        CommissionCategory.DecoyMission =>
                            $"进攻之前，我需要{victim}的斥候被引开。你带小队在{loc}另一边制造动静，吸引他们的注意——{reward}第纳尔。",
                        _ => $"{victim}在{loc}的部署已经拖太久了。我手上有些活需要人去办——{reward}第纳尔，你看着选。"
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
                            $"{instigator}垄断了{loc}的市场，粮价翻了三倍。{victim}的生意快撑不住了——{reward}第纳尔雇你帮忙打破困局。",
                        CommissionCategory.ProcurementAgent =>
                            $"{instigator}控制了{loc}的所有货源。我需要你跨城代购一批货，绕开他的垄断——{reward}第纳尔。",
                        _ => $"{loc}的粮价突然翻了三倍——{instigator}在背后垄断了市场。{victim}的生意快撑不住了，{reward}第纳尔雇你帮忙打破这个困局。"
                    };
                }
                else // Instigator（垄断商）
                {
                    return data.Category switch
                    {
                        CommissionCategory.SupplyIntercept =>
                            $"有人想绕过我在{loc}的生意网，从外地运货进来。帮我把那批货截下来——{reward}第纳尔，别让它们进城。",
                        CommissionCategory.DecoyMission =>
                            $"{victim}的人在{loc}四处打听，想挖我的墙角。你去制造点混乱，转移他们的注意——{reward}第纳尔。",
                        _ => $"{loc}的生意现在是我说了算。{victim}不服气想翻盘，帮我压住场子——{reward}第纳尔。"
                    };
                }
            }

            // ── Fugitive：逃犯（双方对立）──
            if (evt.Type == EventType.Fugitive)
            {
                if (isVictim)
                {
                    return $"{victim}的事没那么简单——追他的人说他是叛徒，他自己说是被陷害的。先帮我找到他藏身的地方，护送他安全离开——{reward}第纳尔。";
                }
                else
                {
                    return $"{victim}是个叛徒，至少追他的人这么说。不管真相如何，把他揪出来——活的死的都行，{reward}第纳尔。";
                }
            }

            // ── 以下事件只有受害方发委托（加害方是匪徒/刺客/天灾，不雇人）──
            if (!isVictim)
                return null; // 加害方没有委托叙事 → 返回 null，让调用方处理

            // ── 受害方通用叙事（按事件类型）──
            return evt.Type switch
            {
                EventType.BanditRaid =>
                    $"{victim}从{loc}逃出来报信——{instigator}带人正在劫掠村子！乡亲们凑了{reward}第纳尔，雇人去打退他们。你愿意出手吗？",
                EventType.Kidnapping =>
                    $"{victim}的家人急疯了——{instigator}把人绑走了，指定了赎金和地点。我们没有{reward}第纳尔去赎人，但有钱雇你去把人救回来。",
                EventType.Famine =>
                    $"这是{loc}的村长{victim}——村里断粮了，老人孩子已经吃了三天野菜。这{reward}第纳尔是乡亲们最后凑的，托你去买粮救命。",
                EventType.Betrayal =>
                    $"{victim}声音发抖——{instigator}，他最信任的人，卷走了账上的钱还带走了半个商队。{reward}第纳尔，帮我把人和钱追回来。",
                EventType.DebtTrap =>
                    $"{victim}低下了头——{instigator}放的高利贷已经滚到了他还不起的数目。如果不还，地就要被收走。{reward}第纳尔，帮我家渡过这个坎……",
                EventType.RomanticConflict =>
                    $"{victim}叹了口气——这事说来话长。总之现在需要有人替他出面解决一场决斗，{reward}第纳尔报酬。具体细节到了再说。",
                EventType.FalseAccusation =>
                    $"城主要杀鸡儆猴，{victim}成了替罪羊。我知道真凶是谁——但需要证据。{reward}第纳尔，帮我把证据找回来，救人一命。",
                EventType.InheritanceDispute =>
                    $"老族长走了，遗嘱却不见了。{victim}说父亲生前把信物交给了某个人——找到它，就能证明继承权。{reward}第纳尔。",
                EventType.SacredTheft =>
                    $"这是我们{loc}一族的祖传圣物——{instigator}从祠堂里把它盗走了。没有它新族长没法召开族会。{reward}第纳尔，把它追回来。",
                EventType.Assassination =>
                    $"{victim}死了。{loc}现在人心惶惶，下属们互相猜忌。有人悬赏{reward}第纳尔追查真凶——你接不接？",
                _ => null
            };
        }

        /// <summary>从 Narrative.csv 读取 WorldEvent 叙事文本。
        /// ID 格式逐级 fallback：
        ///   1. WorldEvent_{EventType}_{Role}_{Category}_{Phase}  （最精确）
        ///   2. WorldEvent_{EventType}_{Role}_{Phase}
        ///   3. WorldEvent_{EventType}_{Phase}                     （旧格式兼容）
        /// Closure 时追加 _Grade。</summary>
        private static string TryGetWorldEventNarrative(WorldEvent evt, CommissionData data, string phase, string grade, string role = null)
        {
            try
            {
                string gradeSuffix = !string.IsNullOrEmpty(grade) ? $"_{grade}" : "";
                string categorySuffix = data?.Category != null ? $"_{data.Category}" : "";

                // 定义 fallback 键列表，从最精确到最泛
                var keys = new List<string>();

                if (!string.IsNullOrEmpty(role) && !string.IsNullOrEmpty(categorySuffix))
                    keys.Add($"WorldEvent_{evt.Type}_{role}{categorySuffix}_{phase}{gradeSuffix}");

                if (!string.IsNullOrEmpty(role))
                    keys.Add($"WorldEvent_{evt.Type}_{role}_{phase}{gradeSuffix}");

                keys.Add($"WorldEvent_{evt.Type}_{phase}{gradeSuffix}");

                foreach (var eventId in keys)
                {
                    var filters = new NarrativeFilters { EventName = eventId };
                    var result = Resolve(filters);
                    if (result != null && !IsFallbackText(result.Text))
                    {
                        return SubstituteCommissionPlaceholders(result.Text, data);
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>委托结账结局叙事。</summary>
        public static string GetCommissionClosure(CommissionData data, NPCProfile giverProfile,
            NPCProfile payerProfile, CommissionGrade grade)
        {
            if (data == null) return "这是你的报酬。";

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
                string payerName = payerProfile.BaseHero?.Name?.ToString() ?? "结账人";
                text += $"（{payerName}代为转交了报酬。）";
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
            string loc = evt.TargetSettlement?.Name?.ToString() ?? "那边";
            string victim = evt.TargetHero?.Name?.ToString() ?? "委托人";
            string instigator = evt.IsGenericInstigator ? "那帮人" : (evt.InstigatorHero?.Name?.ToString() ?? "他们");
            string reward = data.NegotiatedReward.ToString();

            return (evt.Type, grade) switch
            {
                (EventType.BanditRaid, CommissionGrade.Perfect) =>
                    $"{victim}眼含热泪——{instigator}被彻底打跑了，{loc}的乡亲们终于能睡个安稳觉。{reward}第纳尔，这是我们能拿出的全部了。",
                (EventType.BanditRaid, CommissionGrade.Good) =>
                    $"匪帮退了！{loc}暂时安全了。{reward}第纳尔，拿好。",
                (EventType.BanditRaid, _) =>
                    $"总算是有了个结果。{reward}第纳尔报酬。",

                (EventType.Kidnapping, CommissionGrade.Perfect) =>
                    $"{victim}一把抱住被救回来的人——失声痛哭。{reward}第纳尔……这份恩情我们全家记一辈子。",
                (EventType.Kidnapping, CommissionGrade.Good) =>
                    $"人回来了。{victim}握着你的手说不出话。{reward}第纳尔，谢谢你。",
                (EventType.Kidnapping, _) =>
                    $"人救回来了。虽然过程不太完美……{reward}第纳尔报酬。",

                (EventType.Famine, CommissionGrade.Perfect) =>
                    $"粮食刚好赶上！{loc}的老人孩子终于有饭吃了。{victim}代表全村向你道谢——{reward}第纳尔。",
                (EventType.Famine, _) =>
                    $"粮食送到了。虽然晚了一些……总算是救了急。{reward}第纳尔。",

                (EventType.Betrayal, CommissionGrade.Perfect) =>
                    $"{victim}看着被追回的财物，沉默了很久。'他曾经是我最信任的人……' {reward}第纳尔，谢谢你还我公道。",
                (EventType.Betrayal, _) =>
                    $"事情了结了。{victim}叹了口气——有些伤不是钱能弥补的。{reward}第纳尔。",

                (EventType.DebtTrap, CommissionGrade.Perfect) =>
                    $"{victim}跪下了——'我终于不用躲着他们了。' {reward}第纳尔，这份恩情我当牛做马也会还。",
                (EventType.DebtTrap, _) =>
                    $"债主暂时不会来骚扰了。{victim}终于能喘口气。{reward}第纳尔。",

                (EventType.SacredTheft, CommissionGrade.Perfect) =>
                    $"圣物完好无损地回到了祠堂。{loc}的族老们含着泪向你致意——'祖宗的魂终于归位了。' {reward}第纳尔。",
                (EventType.SacredTheft, _) =>
                    $"东西追回来了。虽然有些磕碰……{reward}第纳尔。",

                (EventType.Assassination, CommissionGrade.Perfect) =>
                    $"真凶被绳之以法。{loc}恢复了秩序——至少表面上是这样。{reward}第纳尔，你让正义得到了伸张。",
                (EventType.Assassination, _) =>
                    $"凶手处理了。但{loc}的伤痕不会那么快愈合。{reward}第纳尔。",

                _ => null
            };
        }

        #endregion

        #region 占位符替换

        /// <summary>通用占位符替换（对话用）。</summary>
        public static string ApplyPlaceholders(string raw, Hero target, Agent agent)
        {
            if (string.IsNullOrEmpty(raw)) return raw;

            string playerName = Hero.MainHero?.Name?.ToString() ?? "你";
            string npcName = target?.Name?.ToString()
                ?? agent?.Name?.ToString()
                ?? "对方";
            string world = Settings.Instance?.WorldDescription ?? "";

            return raw
                .Replace("{PLAYER}", playerName)
                .Replace("{NPC}", npcName)
                .Replace("{WORLD}", world ?? "")
                .Replace("{TERM_LORD}", "大人");
        }

        /// <summary>委托占位符替换。</summary>
        public static string SubstituteCommissionPlaceholders(string template, CommissionData data)
        {
            if (string.IsNullOrEmpty(template)) return template;

            if (data.TargetHero != null)
                template = template.Replace("{TARGET}", data.TargetHero.Name?.ToString() ?? "目标");
            else
                template = template.Replace("{TARGET}", "目标");

            if (!string.IsNullOrEmpty(data.TargetSettlementId))
            {
                var s = Settlement.Find(data.TargetSettlementId);
                template = template.Replace("{LOCATION}", s?.Name?.ToString() ?? data.TargetSettlementId);
            }
            else
                template = template.Replace("{LOCATION}", "目的地");

            if (!string.IsNullOrEmpty(data.TargetItemId))
            {
                var item = MBObjectManager.Instance.GetObject<ItemObject>(data.TargetItemId);
                template = template.Replace("{ITEM}", item?.Name?.ToString() ?? data.TargetItemId);
            }
            else
                template = template.Replace("{ITEM}", "某物");

            template = template.Replace("{REWARD}", data.NegotiatedReward.ToString());
            template = template.Replace("{DEPOSIT}", data.DepositAmount.ToString());
            template = template.Replace("{GIVER}", data.QuestGiver?.Name?.ToString() ?? "委托人");
            template = template.Replace("{COUNT}", data.TargetItemCount.ToString());
            template = template.Replace("{DAYS}", ((int)(data.TimeRemainingHours / 24f) + 1).ToString());
            template = template.Replace("{PAYER}", data.RewardPayer?.Name?.ToString() ?? "结算人");

            return template;
        }

        #endregion

        #region 兜底

        /// <summary>判断 Resolve 返回的文本是否是兜底/无效文本（"……" 及其变体，如"……（微微颔首）"）。</summary>
        public static bool IsFallbackText(string text)
        {
            return string.IsNullOrEmpty(text) || text.StartsWith("……");
        }

        /// <summary>查询无结果时的代码级硬编码兜底（目标 &lt; 5 条）。</summary>
        private static NarrativeResult GetCodeFallback(NarrativeFilters filters)
        {
            // 委托 Opening
            if (filters.Category != null && filters.Phase == "Opening")
                return new NarrativeResult(
                    $"我需要有人帮我处理一件事。报酬{{REWARD}}第纳尔。你愿意接下吗？", "normal");

            // 委托 Closure
            if (filters.Category != null && filters.Phase == "Closure")
            {
                string gradeText = filters.Grade switch
                {
                    "Perfect" => "做得漂亮！{REWARD}第纳尔——你比我想的还要靠得住。",
                    "Good" => "办妥了。{REWARD}第纳尔，拿好。",
                    "Passable" => "总算是完成了。{REWARD}，说好的数。",
                    "Failed" => "这次就算了吧。希望下回能好些。",
                    _ => "这是{REWARD}第纳尔报酬。"
                };
                return new NarrativeResult(gradeText, "normal");
            }

            // 对话 Success
            if (filters.Outcome == "Success")
                return new NarrativeResult("……（微微颔首，似是默许了）", "positive");

            // 对话 Fail
            if (filters.Outcome == "Fail")
                return new NarrativeResult("……（摇了摇头，并未应允）", "negative");

            // 通用
            return new NarrativeResult("……（微微颔首）", "normal");
        }

        /// <summary>委托叙事 CSV 查不到时的兜底。</summary>
        private static string GetCommissionFallback(CommissionData data, string phase, CommissionGrade grade)
        {
            if (phase == "Opening")
            {
                string target = data.TargetHero?.Name?.ToString()
                    ?? (data.TargetSettlementId != null
                        ? Settlement.Find(data.TargetSettlementId)?.Name?.ToString() ?? "某地"
                        : "目标");
                return $"我需要有人帮我处理一件事——和{target}有关。报酬{data.NegotiatedReward}第纳尔。你愿意接下吗？";
            }
            else
            {
                return grade switch
                {
                    CommissionGrade.Perfect => $"做得漂亮！{data.NegotiatedReward}第纳尔——你比我想的还要靠得住。",
                    CommissionGrade.Good => $"办妥了。{data.NegotiatedReward}第纳尔，拿好。",
                    CommissionGrade.Passable => $"总算是完成了。{data.NegotiatedReward}，说好的数。",
                    CommissionGrade.Failed => $"这次就算了吧。希望下回能好些。",
                    _ => $"这是{data.NegotiatedReward}第纳尔报酬。"
                };
            }
        }

        #endregion
    }
}
