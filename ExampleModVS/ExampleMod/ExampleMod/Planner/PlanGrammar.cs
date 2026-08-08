using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════
    // PlanGrammar.cs — 计划语法（封闭词表）的模型 + 验证器
    //
    // 对应计划文档 plans/llm-goap-plan-execution.md §5（计划语法）：
    //   - PlanResponse：LLM 一次调用的输出壳（意图 + 计划 + 对话壳字段）
    //   - Plan：执行视图（steps 主链 + fallbacks 预案 + contingencies 意外 + loop 循环段 + triggers 事件驱动）
    //   - PlanValidator：铁律 2（LLM 输出不可信任）的运行时防线
    //
    // 铁律：LLM 只能从封闭词表选动作/谓词；每个字段 null-guard（defensive-coding.md）；
    //       跳转与预案入口双向校验（§5.1 铁律，与 Scripts/validate_plan_json.py 同规）。
    // ═══════════════════════════════════════════════════════════════

    /// <summary>LLM 计划调用完整响应（PlanResponse）。全部字段 null-guard 消费。</summary>
    public class PlanResponse
    {
        [JsonProperty("reply")] public string Reply;
        [JsonProperty("emotion")] public string Emotion;
        [JsonProperty("options")] public List<string> Options;
        [JsonProperty("intent")] public PlanIntent Intent;
        [JsonProperty("plan")] public Plan Plan;
        [JsonProperty("questions")] public List<ClarifyQuestion> Questions;
        [JsonProperty("needs_clarification")] public bool NeedsClarification;
        [JsonProperty("reactions")] public List<ReactivePlan> Reactions;   // 相关 NPC 反应计划（§6.1）
    }

    /// <summary>澄清轮问题（意图歧义优先澄清，最多 2 轮）。</summary>
    public class ClarifyQuestion
    {
        [JsonProperty("q")] public string Q;
        [JsonProperty("options")] public List<string> Options;
    }

    /// <summary>意图分类结果（§2.2 CommandIntent）。intent_type 是 LLM 输出的字符串，C# 侧 Parse 成 CommandIntentType。</summary>
    public class PlanIntent
    {
        [JsonProperty("intent_type")] public string IntentType;
        [JsonProperty("subjects")] public List<string> Subjects;
        [JsonProperty("target")] public JToken Target;            // string 或 {"query": "..."}（动态目标）
        [JsonProperty("who_does")] public string WhoDoes;
        [JsonProperty("watch_point")] public JToken WatchPoint;   // 看守点/锚点（可以是 string 角色名）
        [JsonProperty("destination")] public JToken Destination;  // 引开点/去向（string 或 query）
        [JsonProperty("meet_point")] public string MeetPoint;
        [JsonProperty("zone")] public JToken Zone;
        [JsonProperty("filter")] public string Filter;
        [JsonProperty("message")] public string Message;
        [JsonProperty("what")] public string What;
        [JsonProperty("opponent")] public string Opponent;
        [JsonProperty("amount")] public string Amount;
        [JsonProperty("params")] public Dictionary<string, JToken> Params;

        /// <summary>归一化 target：string → 角色名；{"query": "..."} → null + Query 字段。</summary>
        public string GetTargetRef(out string query)
        {
            return PlanRefUtil.Normalize(Target, out query);
        }

        public string GetWatchPointRef(out string query)
        {
            return PlanRefUtil.Normalize(WatchPoint, out query);
        }
    }

    /// <summary>执行视图计划（§5.1 Plan JSON）。</summary>
    public class Plan
    {
        [JsonProperty("intent")] public PlanIntent Intent;
        [JsonProperty("summary")] public string Summary;
        [JsonProperty("goal")] public Condition Goal;               // 可选具体化；缺省回落 GoalTemplate
        [JsonProperty("steps")] public List<PlanStep> Steps;        // 主链（游标顺序推进）
        [JsonProperty("fallbacks")] [JsonConverter(typeof(FallbackListConverter))] public List<List<PlanStep>> Fallbacks; // 预案数组（数组的数组）
        [JsonProperty("contingencies")] public List<Contingency> Contingencies; // 意外/跳转条件
        [JsonProperty("triggers")] public List<Trigger> Triggers;   // 事件驱动意图专用（LOOKOUT/SHADOW）
        [JsonProperty("loop")] public PlanLoop Loop;                // 循环段（批量目标）
        [JsonProperty("questions")] public List<ClarifyQuestion> Questions;
    }

    /// <summary>循环段（§5.0）：段内步骤循环执行，每轮求值 until；达成 → 顺序继续主链 steps。</summary>
    public class PlanLoop
    {
        [JsonProperty("steps")] public List<PlanStep> Steps;
        [JsonProperty("until")] public Condition Until;
    }

    /// <summary>计划步骤。on_timeout 缺省 @abort_gracefully、on_success 缺省顺序下一歩。</summary>
    public class PlanStep
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("action")] public string Action;
        [JsonProperty("actor")] public string Actor;      // 步骤级 actor 寻址（一带多）；缺省 = self
        [JsonProperty("target")] public JToken Target;    // string 或 {"query": "..."} 或 {"type": "..."}（谓词型）
        [JsonProperty("within")] public float Within;     // 到达判定半径（≤ 5m 钳制）
        [JsonProperty("timeout_s")] public float TimeoutS;
        [JsonProperty("text")] public string Text;        // say_to 冒泡文本
        [JsonProperty("content")] public string Content;  // 容错别名：模型常把 say_to 台词写成 content（§5.3 钳制精神）
        [JsonProperty("ask")] public string Ask;          // say_to 可选：follow = 邀请跟随
        [JsonProperty("seconds")] public float Seconds;   // wait 纯等待（与 until 互斥）
        [JsonProperty("until")] public Condition Until;   // wait 退出条件 / 动作步骤提前完成条件
        [JsonProperty("when")] public Condition When;     // GATE 前置门控
        [JsonProperty("on_timeout")] public string OnTimeout;
        [JsonProperty("on_success")] public string OnSuccess;
        [JsonProperty("on_event")] public List<StepEvent> OnEvent; // 决策结果事件 → 即时跳转
        [JsonProperty("result")] public JToken Result; // 判定型路由 {success: "s2",...} 或 end_plan 字符串 "success"/"fail"
        [JsonProperty("report")] public string Report;  // end_plan 收尾报告文本（当面报告）
        [JsonProperty("variant")] public string Variant;  // steal_attempt: item / pickpocket
        [JsonProperty("item")] public string Item;        // give_item / deliver_item
        [JsonProperty("amount")] public JToken Amount;    // give_gold: "stolen" 或数值
        [JsonProperty("rel_pos")] public string RelPos;   // follow: behind/line/left/right
        [JsonProperty("script")] public ScriptBlock Script; // 结算型步骤台本（negotiate/duel）

        /// <summary>result 路由查询（判定型原子：success/empty/impossible/interrupted → 目标 id）。</summary>
        public string ResultRoute(string key)
        {
            if (Result == null || Result.Type != JTokenType.Object) return null;
            var v = Result[key];
            return v?.Type == JTokenType.String ? v.Value<string>() : null;
        }

        /// <summary>end_plan 的 result 字符串（"success"/"fail"）。</summary>
        public string ResultString => Result?.Type == JTokenType.String ? Result.Value<string>() : null;

        /// <summary>台词兼容读取（模型常写 content 而非 text）。</summary>
        [JsonIgnore]
        public string TextOrContent => Text ?? Content;

        /// <summary>保持型/无限等待步骤（wait 省略 seconds/until、follow 省略 timeout）不套 30s 默认与总时长上限。</summary>
        public static bool IsUnboundedStep(PlanStep s)
        {
            if (s == null) return false;
            if (s.Action == "wait" && s.Seconds <= 0 && s.Until == null) return true;
            if (s.Action == "follow" && s.TimeoutS <= 0) return true;
            return false;
        }
    }

    /// <summary>步骤级事件跳转（§5.4 事件通道：refused/followed 等决策结果即时跳转）。</summary>
    public class StepEvent
    {
        [JsonProperty("type")] public string Type;
        [JsonProperty("then")] public string Then;
    }

    /// <summary>意外/跳转条件（contingencies）。when 成立（EDGE 上升沿）→ 跳 then。</summary>
    public class Contingency
    {
        [JsonProperty("when")] public Condition When;
        [JsonProperty("then")] public string Then;       // 步骤 id 或 @-保留指令
        [JsonProperty("one_shot")] public bool OneShot;
    }

    /// <summary>事件驱动触发（TRIGGER，上升沿 → signal_player，计划不结束）。</summary>
    public class Trigger
    {
        [JsonProperty("when")] public Condition When;
        [JsonProperty("then")] public TriggerAction Then;

        public class TriggerAction
        {
            [JsonProperty("action")] public string Action;
            [JsonProperty("text")] public string Text;
        }
    }

    /// <summary>封闭谓词条件（§5.2）。type 是词表谓词，LLM 只能从词表选。</summary>
    public class Condition
    {
        [JsonProperty("type")] public string Type;       // distance/seeing/following/.../and/or
        [JsonProperty("a")] public string A;             // 实体引用（self/player/角色名/any/all）或区域
        [JsonProperty("b")] public string B;
        [JsonProperty("op")] public string Op;           // >/<  /  true/false / =
        [JsonProperty("value")] public float Value;
        [JsonProperty("entity")] public string Entity;   // alert_phase 的 entity
        [JsonProperty("phase")] public string Phase;     // alert_phase 的 phase
        [JsonProperty("step_id")] public string StepId;  // time_since
        [JsonProperty("of")] public JToken Of;           // count: {"query": "..."}
        [JsonProperty("sustained_s")] public float SustainedS; // 顶层时间修饰符（防抖）
        [JsonProperty("was")] public bool Was;           // 顶层状态修饰符（曾成立）
        [JsonProperty("conditions")] public List<Condition> Conditions; // and/or 组合
    }

    /// <summary>结算型步骤台本（§5.5）：分支与运行时结算结果枚举一一对应。</summary>
    public class ScriptBlock
    {
        [JsonProperty("opening")] public List<ScriptLine> Opening;
        [JsonProperty("outcomes")] public Dictionary<string, List<ScriptLine>> Outcomes; // success/partial/fail（或 win/draw/lose）
        [JsonProperty("announce")] public string Announce; // {AMOUNT} 等占位符运行时填充
    }

    public class ScriptLine
    {
        [JsonProperty("self")] public string Self;
        [JsonProperty("target")] public string Target;
        [JsonProperty("speaker")] public string Speaker; // 可选第三方

        public string Text => Self ?? Target ?? Speaker ?? "";
        public bool IsSelf => Self != null;
    }

    /// <summary>fallbacks 形态容错（§5.3 钳制精神）：模型常写成单层对象数组 [{...}]，
    /// 规范为数组的数组 [[{...}]]——反序列化时自动包层，不因形态错误丢掉整个预案区。</summary>
    public class FallbackListConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(List<List<PlanStep>>);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);
            var result = new List<List<PlanStep>>();
            if (token == null || token.Type != JTokenType.Array) return result;
            foreach (var item in token)
            {
                if (item == null) continue;
                if (item.Type == JTokenType.Array)
                {
                    result.Add(item.ToObject<List<PlanStep>>(serializer) ?? new List<PlanStep>());
                }
                else
                {
                    // 单层对象 → 包成单元素预案（预案 = 至少一个步骤的序列）
                    var single = item.ToObject<PlanStep>(serializer);
                    if (single != null) result.Add(new List<PlanStep> { single });
                }
            }
            return result;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value);
        }
    }

    /// <summary>目标引用归一化工具：string 角色名 或 {"query": "..."} 动态查询。</summary>
    public static class PlanRefUtil
    {
        /// <summary>归一化 JToken 引用。返回具名引用（角色名），query 参数输出查询字符串（无则 null）。</summary>
        public static string Normalize(JToken token, out string query)
        {
            query = null;
            if (token == null) return null;
            if (token.Type == JTokenType.String) return token.Value<string>();
            if (token.Type == JTokenType.Object)
            {
                var q = token["query"];
                if (q != null && q.Type == JTokenType.String)
                {
                    query = q.Value<string>();
                    return null;
                }
                var t = token["type"];
                if (t != null && t.Type == JTokenType.String) return t.Value<string>();
            }
            return null;
        }

        /// <summary>数值归一化：字符串（"stolen"）或数字；无则默认。</summary>
        public static float NumberOr(JToken token, float def)
        {
            if (token == null) return def;
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer) return token.Value<float>();
            return def;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 封闭词表（LLM 只能从这里选；§4 动作表 + §5.2 谓词表 + §5.0 查询表）
    // ═══════════════════════════════════════════════════════════════

    public static class PlanVocab
    {
        /// <summary>动作词表容错别名（§5.3 钳制精神）：模型常写缩写/口语变体 → 校验时规范为词表名。
        /// 与 content→text 别名同源：形态修复，不放宽封闭语法。</summary>
        public static readonly Dictionary<string, string> ActionAliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "attack", "order_attack" },      // 模型常把攻击写成 attack
            { "move", "move_to" },
            { "stop", "stop_following" },
            { "speak", "say_to" },
            { "give", "give_item" },
            { "steal", "steal_attempt" },
        };

        /// <summary>封闭动作词表（§4 原子行为表）——prompt 展示顺序 = 本数组顺序（保持手写序，
        /// 模型对词表顺序敏感，82% 回归基线是此顺序跑出来的）。**注册新动作 = 本数组加一行**，
        /// Actions 自动派生、prompt 自动读到；执行语义在 InlineSteps.ExecuteStep 补 case。</summary>
        public static readonly string[] ActionsInPromptOrder =
        {
            "move_to", "follow", "stop_following", "order_attack", "knockout", "lead",
            "face", "look_at", "say_to", "wait", "emote", "make_noise", "signal_player",
            "steal_attempt", "give_item", "give_gold", "deliver_item", "shadow",
            "negotiate", "duel", "end_plan",
        };

        /// <summary>封闭动作词表（校验用，由 ActionsInPromptOrder 派生，勿单独修改）。</summary>
        public static readonly HashSet<string> Actions = new HashSet<string>(ActionsInPromptOrder, StringComparer.Ordinal);

        /// <summary>判定型/结算型动作（有 result 路由合法）——result 键必须在各动作允许集内。</summary>
        public static readonly Dictionary<string, HashSet<string>> AllowedResultKeys = new Dictionary<string, HashSet<string>>
        {
            { "steal_attempt", new HashSet<string> { "success", "empty", "impossible", "interrupted" } },
            { "negotiate", new HashSet<string> { "success", "partial", "fail" } },
            { "duel", new HashSet<string> { "win", "draw", "lose" } },
        };

        /// <summary>封闭谓词词表（§5.2）——prompt 展示顺序 = 本数组顺序。**注册新谓词 = 本数组加一行**。</summary>
        public static readonly string[] PredicatesInPromptOrder =
        {
            "distance", "seeing", "alert_phase", "following", "facing", "moving", "in_zone",
            "combat", "player_action", "time_since", "dead", "knocked_out", "count",
            "and", "or", "not",
        };

        /// <summary>封闭谓词词表（校验用，由 PredicatesInPromptOrder 派生）。</summary>
        public static readonly HashSet<string> Predicates = new HashSet<string>(PredicatesInPromptOrder, StringComparer.Ordinal);

        /// <summary>封闭查询词表（§5.0 动态目标引用）——prompt 展示顺序 = 本数组顺序。**注册新查询 = 本数组加一行**。</summary>
        public static readonly string[] QueriesInPromptOrder =
        {
            "nearest_enemy", "all_in", "hidden_spot", "lure_spot", "stand_spot", "zone", "point",
        };

        /// <summary>封闭查询词表（校验用，由 QueriesInPromptOrder 派生）。</summary>
        public static readonly HashSet<string> Queries = new HashSet<string>(QueriesInPromptOrder, StringComparer.Ordinal);

        /// <summary>实体三值域 any/all/self/player（其余 = 角色表引用）。</summary>
        public static readonly HashSet<string> EntityKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "any", "all", "self", "player",
        };

        /// <summary>@-保留指令（跳转目标合法值）。</summary>
        public static readonly HashSet<string> ReservedDirectives = new HashSet<string>(StringComparer.Ordinal)
        {
            "@abort_gracefully",
        };

        /// <summary>保留端步骤（end_plan = 收尾，无跳转消费）。</summary>
        public static readonly HashSet<string> TerminalActions = new HashSet<string>(StringComparer.Ordinal)
        {
            "end_plan",
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // PlanValidator — 铁律 2（LLM 输出不可信任）的运行时防线
    // 规则：未知动作/谓词/实体/phase → 该步丢弃 + 日志警告；
    //       缺 timeout_s 补默认 30s（保持型/无限等待除外）；
    //       跳转目标缺失 → 忽略跳转按默认处理；参数范围钳制。
    // 跳转双向校验（S1 目标存在 / S2 入口可达 / S3 不跳预案中间步 / S4 id 唯一）
    //   与 Scripts/validate_plan_json.py 同规。
    // ═══════════════════════════════════════════════════════════════

    public partial class PlanValidationResult
    {
        public bool Ok;                      // 是否整体可用
        public List<string> Warnings = new List<string>(); // 已修复的警告（丢弃步骤/钳制参数）
        public Plan Plan;                    // 校验+修复后的计划（null = 拒收）
    }

    public static class PlanValidator
    {
        public const float DefaultTimeout = 30f;
        public const float MaxTimeout = 120f;
        public const float MaxDistance = 50f;
        public const float MaxWithin = 5f;
        public const float MaxSustained = 30f;
        public const float MaxCount = 50f;

        public static PlanValidationResult Validate(Plan plan, string intentType)
        {
            var result = new PlanValidationResult { Plan = plan };
            if (plan == null)
            {
                result.Ok = false;
                result.Warnings.Add("plan 为 null");
                return result;
            }

            // S4: id 唯一
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in IterSteps(plan))
            {
                if (string.IsNullOrEmpty(s.Id))
                {
                    result.Warnings.Add($"步骤缺 id（action={s.Action}）→ 拒收");
                    result.Ok = false;
                    return result;
                }
                if (!ids.Add(s.Id))
                {
                    result.Warnings.Add($"重复步骤 id: {s.Id}");
                    result.Ok = false;
                    return result;
                }
            }

            // 动作/参数校验（按步骤原地修复；未知动作 → 丢弃该步，§5.3 步骤级降级）
            var discardIds = new HashSet<string>(StringComparer.Ordinal);
            int totalSteps = 0;
            foreach (var s in IterSteps(plan)) totalSteps++;
            foreach (var s in IterSteps(plan))
            {
                if (ValidateStep(s, result, discardIds)) discardIds.Add(s.Id);
            }
            if (discardIds.Count > 0)
            {
                // 从主链/循环段/预案中移除被丢弃的步骤（跳转引用由 S1 忽略跳转兜底）
                plan.Steps?.RemoveAll(s => s != null && discardIds.Contains(s.Id));
                plan.Loop?.Steps?.RemoveAll(s => s != null && discardIds.Contains(s.Id));
                if (plan.Fallbacks != null)
                    foreach (var entry in plan.Fallbacks)
                        entry?.RemoveAll(s => s != null && discardIds.Contains(s.Id));
                result.Warnings.Add($"丢弃 {discardIds.Count}/{totalSteps} 个步骤（未知动作/非法参数）");
                // 丢弃 > 50% → 整体拒收（§5.3：整体失败 → 重试）
                if (discardIds.Count > totalSteps / 2)
                {
                    result.Ok = false;
                    result.Warnings.Add($"丢弃步骤超过 50%（{discardIds.Count}/{totalSteps}）→ 计划拒收");
                }
            }

            // 条件谓词词表（与步骤级 ValidateCondition 同规）：goal / contingencies.when /
            // triggers.when / loop.until 同样受检——LLM 把事件词（approach_by 等）写进条件 = 未定义谓词
            if (plan.Goal != null) ValidateCondition(plan.Goal, result, "goal");
            if (plan.Loop != null && plan.Loop.Until != null) ValidateCondition(plan.Loop.Until, result, "loop.until");
            if (plan.Contingencies != null)
                foreach (var c in plan.Contingencies)
                    if (c?.When != null) ValidateCondition(c.When, result, "contingency");
            if (plan.Triggers != null)
                foreach (var t in plan.Triggers)
                    if (t?.When != null) ValidateCondition(t.When, result, "trigger");

            // 跳转双向校验（S1 目标存在 / S2 入口可达 / S3 只跳入口步）
            var allJumps = PlanValidationResult.CollectJumpTargets(plan);
            foreach (var t in allJumps)
            {
                result.JumpTargetsAll.Add(t);
                if (!t.StartsWith("@")) result.JumpSources.Add(t);
            }
            var targets = new HashSet<string>(ids, StringComparer.Ordinal);
            foreach (var t in allJumps)
            {
                if (!targets.Contains(t) && !PlanVocab.ReservedDirectives.Contains(t))
                {
                    // 忽略跳转按默认处理（§5.3）
                    result.Warnings.Add($"跳转目标不存在: {t} → 忽略该跳转");
                }
            }

            // 失败路径谎报成功防护（LLM 常见缺陷：条件等待步骤的 on_timeout/on_event 指向 success 收尾）：
            // 忽略该跳转 → 按缺省 @abort_gracefully（失败收尾），杜绝"没办成却报告成功"。
            // 只查"条件等待"步骤（带 until 的 wait/move_to）——超时 = 条件没达成 = 失败；
            // 纯时长等待（wait seconds，无 until）超时 = 等够了 = 完成，不算谎报。
            var stepById = new Dictionary<string, PlanStep>(StringComparer.Ordinal);
            foreach (var s in IterSteps(plan))
                if (!string.IsNullOrEmpty(s.Id)) stepById[s.Id] = s;
            foreach (var s in IterSteps(plan))
            {
                bool IsConditionWait = s.Until != null;
                if (!IsConditionWait) continue;
                bool IsSuccessEnd(string tgt)
                {
                    if (string.IsNullOrEmpty(tgt) || tgt.StartsWith("@")) return false;
                    return stepById.TryGetValue(tgt, out var t)
                        && t.Action == "end_plan" && t.Result?.ToString()?.Contains("success") == true;
                }
                if (IsSuccessEnd(s.OnTimeout))
                {
                    result.Warnings.Add($"条件等待失败路径 on_timeout 指向 success 收尾 {s.OnTimeout} → 忽略该跳转（防谎报成功）");
                    s.OnTimeout = null;
                }
                if (s.OnEvent != null)
                {
                    foreach (var e in s.OnEvent)
                    {
                        if (e != null && IsSuccessEnd(e.Then))
                        {
                            result.Warnings.Add($"条件等待失败路径 on_event 指向 success 收尾 {e.Then} → 忽略该跳转（防谎报成功）");
                            e.Then = null;
                        }
                    }
                }
            }

            // S2/S3: fallback 入口可达 + 只跳入口步
            foreach (var entry in plan.Fallbacks ?? new List<List<PlanStep>>())
            {
                if (entry == null || entry.Count == 0) continue;
                var entryId = entry[0].Id;
                if (!result.JumpSources.Contains(entryId))
                {
                    result.Warnings.Add($"死预案（无跳转进入）: fallback 入口 {entryId}");
                }
            }
            foreach (var t in result.JumpTargetsAll)
            {
                var fb = FindFallbackEntry(plan, t);
                if (fb >= 0 && fb != 0)
                {
                    result.Warnings.Add($"跳转进入预案中间步: {t} → 忽略该跳转");
                }
            }

            result.Ok = true;
            return result;
        }

        /// <summary>
        /// 计划质量诊断（纯报告：不拒收、不改动）——对照 prompt【输出质量要求】逐条打分，
        /// 抓 Validate 结构校验覆盖不到的质量项。调用方（PlanExecutor.Create）打进 [PlanQuality] 日志，
        /// 供人工/调试核对 LLM 输出是否达标；不参与执行决策。
        /// </summary>
        public static List<string> Diagnose(Plan plan, CommandIntentType intentType)
        {
            var notes = new List<string>();
            if (plan == null) { notes.Add("plan 为 null"); return notes; }
            bool keep = GoalTemplates.IsKeepType(intentType);

            // 1. 主链步数（任务型 ≥4；保持型豁免——2-3 步无限保持即完整）
            int mainSteps = plan.Steps?.Count ?? 0;
            if (!keep && mainSteps < 4)
                notes.Add($"任务型主链仅 {mainSteps} 步（要求 ≥4）");

            // 2. fallbacks 数量（≥2 个预案）
            int fbCount = plan.Fallbacks?.Count ?? 0;
            if (fbCount < 2)
                notes.Add($"预案仅 {fbCount} 个（要求 ≥2）");

            // 3. 预案步数（每条 ≥2 步，含 end_plan + report）
            if (plan.Fallbacks != null)
                foreach (var entry in plan.Fallbacks)
                    if (entry != null && entry.Count > 0 && entry.Count < 2)
                        notes.Add($"预案 {entry[0].Id} 仅 {entry.Count} 步（要求 ≥2，含 end_plan+report）");

            // 4. contingencies 数量（≥2 条：combat 必写 + 至少 1 条任务相关意外）
            int ctCount = plan.Contingencies?.Count ?? 0;
            if (ctCount < 2)
                notes.Add($"contingencies 仅 {ctCount} 条（要求 ≥2）");

            // 5. combat→abort 与 SPAR/DUEL 矛盾：切磋的正常进展就是战斗，开打即 abort = 计划自杀
            if (plan.Contingencies != null
                && (intentType == CommandIntentType.Spar || intentType == CommandIntentType.Duel))
            {
                foreach (var c in plan.Contingencies)
                {
                    if (c?.When != null && c.When.Type == "combat" && c.Then == "@abort_gracefully")
                        notes.Add($"combat contingency 与 {intentType} 矛盾：一开打就 abort（切磋中战斗是正常进展）");
                }
            }

            // 6. goal 纪律：任务型应有 goal（成功条件）；保持型不应设 goal（无限 wait + triggers）
            bool hasGoal = plan.Goal != null;
            if (!keep && !hasGoal)
                notes.Add("任务型计划缺 goal（成功条件）");
            if (keep && hasGoal)
                notes.Add("保持型计划不应设 goal（保持型 = 无限 wait + triggers，玩家叫停结束）");

            return notes;
        }

        /// <summary>步骤级校验（§5.3）：未知动作/参数非法 → 返回 true = 丢弃该步（不拒收整单）。
        /// 已知动作的参数问题 → 钳制/修复 + 警告。</summary>
        private static bool ValidateStep(PlanStep s, PlanValidationResult result, HashSet<string> discardIds)
        {
            // 动作别名规范化（attack → order_attack 等，§5.3 钳制精神）
            if (!string.IsNullOrEmpty(s.Action) && PlanVocab.ActionAliases.TryGetValue(s.Action, out string canonical))
            {
                result.Warnings.Add($"动作别名 {s.Action} → 规范为 {canonical}（id={s.Id}）");
                s.Action = canonical;
            }

            // 动作词表
            if (string.IsNullOrEmpty(s.Action) || !PlanVocab.Actions.Contains(s.Action))
            {
                result.Warnings.Add($"未知动作 {s.Action}（id={s.Id}）→ 丢弃该步");
                return true;
            }

            // 参数范围钳制（钳制而非拒收，§5.3）
            if (s.TimeoutS <= 0 && !IsUnbounded(s))
            {
                s.TimeoutS = DefaultTimeout;
                result.Warnings.Add($"缺 timeout_s（id={s.Id}）→ 补 {DefaultTimeout}s");
            }
            if (s.TimeoutS > MaxTimeout)
            {
                result.Warnings.Add($"timeout_s 超限（id={s.Id} {s.TimeoutS}s）→ 钳制 {MaxTimeout}s");
                s.TimeoutS = MaxTimeout;
            }
            if (s.Within > MaxWithin)
            {
                result.Warnings.Add($"within 超限（id={s.Id} {s.Within}）→ 钳制 {MaxWithin}");
                s.Within = MaxWithin;
            }
            if (s.Seconds > MaxTimeout)
            {
                result.Warnings.Add($"seconds 超限（id={s.Id}）→ 钳制 {MaxTimeout}");
                s.Seconds = MaxTimeout;
            }
            if (s.Until != null && s.Until.SustainedS > MaxSustained)
            {
                result.Warnings.Add($"sustained_s 超限（id={s.Id}）→ 钳制 {MaxSustained}");
                s.Until.SustainedS = MaxSustained;
            }

            // 谓词词表（until/when）
            if (s.Until != null) ValidateCondition(s.Until, result, s.Id);
            if (s.When != null) ValidateCondition(s.When, result, s.Id);

            // result 路由键（判定型原子）
            if (s.Result != null && s.Result.Type == JTokenType.Object
                && PlanVocab.AllowedResultKeys.TryGetValue(s.Action, out var allowed))
            {
                var obj = (JObject)s.Result;
                foreach (var prop in obj.Properties().ToList())
                {
                    if (!allowed.Contains(prop.Name))
                    {
                        result.Warnings.Add($"result 键不在 {s.Action} 允许集: {prop.Name}（id={s.Id}）→ 丢弃该路由");
                        prop.Remove();
                    }
                }
            }
            // end_plan 的 result 必须是字符串 success/fail
            if (s.Action == "end_plan" && s.Result != null && s.Result.Type == JTokenType.Object)
            {
                result.Warnings.Add($"end_plan 的 result 应为字符串（id={s.Id}）");
            }

            // 互斥：seconds 与 until 同写 → 保留 until
            if (s.Seconds > 0 && s.Until != null)
            {
                result.Warnings.Add($"seconds 与 until 同写（id={s.Id}）→ 保留 until");
                s.Seconds = 0;
            }

            // 台本：结算型步骤的分支必须覆盖运行时结果枚举（§5.5 铁律 1：缺分支 → 拒收该步骤）
            if (s.Script != null)
            {
                if (ValidateScript(s, result)) return true;
            }
            return false;
        }

        /// <summary>台本校验：返回 true = 该步应被丢弃（缺分支）。</summary>
        private static bool ValidateScript(PlanStep s, PlanValidationResult result)
        {
            var required = new HashSet<string>(StringComparer.Ordinal);
            if (s.Action == "negotiate") { required.Add("success"); required.Add("partial"); required.Add("fail"); }
            else if (s.Action == "duel") { required.Add("win"); required.Add("draw"); required.Add("lose"); }
            else
            {
                result.Warnings.Add($"script 挂在非结算型动作 {s.Action}（id={s.Id}）→ 丢弃 script");
                s.Script = null;
                return false;
            }
            if (s.Script.Outcomes == null)
            {
                result.Warnings.Add($"台本缺 outcomes（id={s.Id}）→ 拒收该步");
                return true;
            }
            foreach (var r in required)
            {
                if (!s.Script.Outcomes.ContainsKey(r))
                {
                    result.Warnings.Add($"台本缺结果分支 {r}（id={s.Id}）→ 拒收该步");
                    return true;
                }
            }
            return false;
        }

        private static void ValidateCondition(Condition c, PlanValidationResult result, string stepId)
        {
            if (c == null) return;
            if (string.IsNullOrEmpty(c.Type) || !PlanVocab.Predicates.Contains(c.Type))
            {
                result.Warnings.Add($"未知谓词 {c.Type}（step={stepId}）→ 丢弃该条件");
                return;
            }
            if (c.Type == "and" || c.Type == "or")
            {
                if (c.Conditions == null)
                {
                    result.Warnings.Add($"{c.Type} 缺 conditions（step={stepId}）→ 丢弃该条件");
                    return;
                }
                foreach (var sub in c.Conditions) ValidateCondition(sub, result, stepId);
            }
        }

        /// <summary>保持型/无限等待步骤（wait 省略 seconds/until、follow 省略 timeout）不套 30s 默认与总时长上限。</summary>
        private static bool IsUnbounded(PlanStep s)
        {
            if (s.Action == "wait" && s.Seconds <= 0 && s.Until == null) return true;
            if (s.Action == "follow" && s.TimeoutS <= 0) return true;
            return false;
        }

        /// <summary>遍历全部步骤（主链 + 循环段 + 预案）。</summary>
        public static IEnumerable<PlanStep> IterSteps(Plan plan)
        {
            if (plan == null) yield break;
            if (plan.Steps != null) foreach (var s in plan.Steps) if (s != null) yield return s;
            if (plan.Loop != null && plan.Loop.Steps != null) foreach (var s in plan.Loop.Steps) if (s != null) yield return s;
            if (plan.Fallbacks != null)
                foreach (var entry in plan.Fallbacks)
                    if (entry != null)
                        foreach (var s in entry) if (s != null) yield return s;
        }

        private static int FindFallbackEntry(Plan plan, string id)
        {
            if (plan.Fallbacks == null) return -1;
            foreach (var entry in plan.Fallbacks)
            {
                if (entry == null) continue;
                for (int i = 0; i < entry.Count; i++)
                {
                    if (entry[i] != null && entry[i].Id == id) return i;
                }
            }
            return -1;
        }
    }

    /// <summary>校验结果扩展：收集跳转来源/目标（供 S2/S3 用）。由 PlanValidator 填充。</summary>
    public partial class PlanValidationResult
    {
        public HashSet<string> JumpSources = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> JumpTargetsAll = new HashSet<string>(StringComparer.Ordinal);

        public static List<string> CollectJumpTargets(Plan plan)
        {
            var refs = new List<string>();
            foreach (var s in PlanValidator.IterSteps(plan))
            {
                foreach (var t in CollectStepJumps(s)) refs.Add(t);
            }
            if (plan.Contingencies != null)
                foreach (var c in plan.Contingencies)
                    if (c?.Then != null) refs.Add(c.Then);
            return refs;
        }

        private static IEnumerable<string> CollectStepJumps(PlanStep s)
        {
            if (!string.IsNullOrEmpty(s.OnTimeout)) yield return s.OnTimeout;
            if (!string.IsNullOrEmpty(s.OnSuccess)) yield return s.OnSuccess;
            if (s.OnEvent != null)
                foreach (var e in s.OnEvent)
                    if (e?.Then != null) yield return e.Then;
            if (s.Result != null && s.Result.Type == JTokenType.Object)
            {
                foreach (var prop in ((JObject)s.Result).Properties())
                {
                    var v = prop.Value;
                    if (v != null && v.Type == JTokenType.String)
                    {
                        var s2 = v.Value<string>();
                        if (!string.IsNullOrEmpty(s2)) yield return s2;
                    }
                }
            }
            if (s.Until != null) foreach (var r in CollectConditionRefs(s.Until)) yield return r;
        }

        private static IEnumerable<string> CollectConditionRefs(Condition c)
        {
            if (c == null) yield break;
            if (!string.IsNullOrEmpty(c.StepId)) yield return c.StepId; // time_since 引用步骤
            if (c.Conditions != null)
                foreach (var sub in c.Conditions)
                    foreach (var r in CollectConditionRefs(sub)) yield return r;
        }
    }
}
