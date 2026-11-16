using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════════════════
    // P0-D.1（2026-11-15）对话定义契约：稳定 ID、入口规则、条件、效果、严格发布
    //
    // 依据：总纲 §0.6.2 / §0.6.4（P0-D.1）
    //
    // 本文件只做三件事（运行时消费在 D.2～D.4，本文件**不执行**任何效果）：
    //   ① 登记条件谓词与效果原子的支持表（名字 / 参数 / 是否已实现）
    //   ② 严格解析 story_dialogue/2（未知字段、重复 JSON 键、重复 ID、悬空引用一律拒绝）
    //   ③ 一次性发布（失败保留上一有效版本，运行实例固定开始时的版本）
    //
    // 🔴 纪律：
    //   · 对话身份 = `DialogueId`，**不从文件名或 NPC 推导**；一个定义可被多名守卫复用。
    //   · 选项必须有局部 `Id`；`PlayerKey` 只是文本，不做身份。
    //   · `Implemented=false` 的条件/效果只能出现在夹具；正式数据引用即拒绝。
    //   · 本文件不读文本、不转钱、不开战 —— 发布期只做静态校验。
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class StoryPredicateSpec
    {
        public string Name;
        public string[] Params = new string[0];
        public string[] RequiredParams = new string[0];
        public bool Implemented;
        public string Notes = "";
    }

    /// <summary>条件谓词注册表 —— 数据里能写的条件只有这里登记过的。</summary>
    public static class StoryPredicateRegistry
    {
        private static readonly Dictionary<string, StoryPredicateSpec> _specs =
            new Dictionary<string, StoryPredicateSpec>(StringComparer.Ordinal);

        static StoryPredicateRegistry()
        {
            Add("phase", true, "剧情阶段（参数=阶段 id，如 first_meet）");
            Add("disguised", true, "玩家当前是否遮面伪装");
            Add("noble", true, "玩家是否穿着贵族行头");
            Add("stripped", true, "玩家是否已被没收装备");
            Add("gate_open", true, "城门是否已开");
            Add("stripped_disarmed", true, "守门人四个武器槽是否全空（参数可省略）");
            Add("asked", true, "是否问过某个话题（参数=话题 id）");
            Add("reward", true, "是否已领过某项奖励（参数=收据 id）");
            Add("quest_accepted", true, "委托是否已接（可选参数=npc id）");
            Add("quest_done", true, "委托是否已完成（可选参数=npc id）");
            Add("quest_turned", true, "委托是否已交差（可选参数=npc id）");
            Add("gold>=", true, "玩家钱数下限（参数=无符号整数）");
            Add("item", true, "玩家是否持有物品（参数=物品 id）");
            Add("event_resolved", true, "某起市井纠纷是否已解决（参数=事件编号）");
            Add("resolution", true, "玩家上次对某事件的做法（参数=做法 id）");
            Add("money_attitude", true, "该 NPC 对钱的态度（参数=open/covert/disdain）");
            Add("errand_ready", true, "零活冷却已过");
            Add("can_redeem_keepsake", true, "当铺信物已付款或身上够 30");
            Add("street_help_pending", true, "纠纷 1 已解决但谢礼未领");
            Add("step>=", true, "该 NPC 的五步关系到达第 n 步（参数=n）");
            Add("clue", true, "是否已经拿到某条线索（参数=线索 id）");
            Add("hazard", true, "是否已经记下某个隐患（参数=隐患 id）");
            Add("attitude", true, "该 NPC 对钱的态度（参数=open/covert/disdain，旧写法）");
            Add("event_active", true, "当前市井事件编号（参数=n）");
            Add("can_see", false, "角色是否看得见某个参与者（参数=角色名；等 D.2 接感知查询）");
            Add("knows", false, "本人是否知道某条事实（参数=事实 id；等 D.2 接个人知识）");
        }

        private static void Add(string name, bool implemented, string notes, string[] required = null)
        {
            _specs[name] = new StoryPredicateSpec
            {
                Name = name,
                Implemented = implemented,
                Notes = notes,
                RequiredParams = required ?? (name == "disguised" || name == "noble" || name == "stripped"
                    || name == "gate_open" || name == "stripped_disarmed"
                    || name == "errand_ready" || name == "can_redeem_keepsake" || name == "street_help_pending"
                    || name == "quest_accepted" || name == "quest_done" || name == "quest_turned"
                    ? new string[0] : new[] { "value" })
            };
        }

        public static bool TryGet(string name, out StoryPredicateSpec spec)
        {
            spec = null;
            return !string.IsNullOrEmpty(name) && _specs.TryGetValue(name, out spec);
        }

        public static IEnumerable<StoryPredicateSpec> All => _specs.Values.OrderBy(s => s.Name, StringComparer.Ordinal);
    }

    public sealed class StoryEffectSpec
    {
        public string Name;
        public string[] Params = new string[0];
        public string[] RequiredParams = new string[0];
        public string[] IntParams = new string[0];
        public bool Implemented;
        public string Notes = "";
    }

    /// <summary>效果原子注册表 —— 数据里的 `OnEnter` / `Effects` 只能引用这里登记过的原子。</summary>
    public static class StoryEffectRegistry
    {
        private static readonly Dictionary<string, StoryEffectSpec> _specs =
            new Dictionary<string, StoryEffectSpec>(StringComparer.Ordinal);

        static StoryEffectRegistry()
        {
            // 🔴 参数表 = 数据里允许出现的**全部**参数；必需参数不齐、表外参数出现，发布期就拒绝。
            Add("mark", true, new[] { "flag" }, new[] { "flag" }, "记一个剧情旗标（按实际说话者记 asked）");
            Add("hazard", true, new[] { "flag" }, new[] { "flag" }, "记一个隐患 flag");
            Add("clue", true, new[] { "clue" }, new[] { "clue" }, "记一条线索并把屏幕目标推进到 asked");
            Add("resolve_event", true, new[] { "event" }, new[] { "event" }, "把当前市井纠纷记为已解决（幂等）", new[] { "event" });
            Add("open_gate", true, new[] { "route", "faction", "delta", "crime", "hazard" }, new[] { "route" },
                "按门路开门；faction/delta/crime/hazard 有值才结算路线后果", new[] { "delta" });
            Add("force_defeat", true, new string[0], new string[0], "首遇硬闯后的真实败退请求（仍须真实战斗回执）");
            Add("pay", true, new[] { "amount", "to", "reason" }, new[] { "amount" },
                "足额转账；to 省略 = 本次实际说话者，reason=bribe 时记打点账", new[] { "amount" });
            Add("relation", true, new[] { "faction", "delta" }, new[] { "faction", "delta" }, "改势力账与代表英雄关系", new[] { "delta" });
            Add("skill_check", true, new[] { "skill", "threshold" }, new[] { "skill" },
                "统一掷点检定（结果写会话事实，路由由选项的 Check 决定）", new[] { "threshold" });
            Add("give_item", true, new[] { "item", "count" }, new[] { "item" }, "给玩家物品（失败不假发）", new[] { "count" });
            Add("start_fight", true, new[] { "target" }, new string[0], "发起真实战斗；target 省略 = 本次实际说话者");
            // ── D.5 迁移桥原子：复用既有 Quest/钱/事件管线，参数由数据给出 ──
            Add("accept_quest", true, new string[0], new string[0], "接当前说话者的原生委托");
            Add("turn_in", true, new string[0], new string[0], "向当前说话者交差");
            Add("complete_quest", true, new[] { "npc", "resolution" }, new[] { "npc", "resolution" },
                "把指定 NPC 的委托标记完成（resolution = charm/guile/force…）");
            Add("collect_debt", true, new[] { "amount" }, new[] { "amount" }, "收数人实收并结清委托", new[] { "amount" });
            Add("errand", true, new[] { "pay" }, new[] { "pay" }, "零活报酬（世界发放，带冷却）", new[] { "pay" });
            Add("street_help_reward", true, new string[0], new string[0], "协助解决纠纷后的谢礼（收据防重）");
            Add("redeem_keepsake", true, new string[0], new string[0], "赎回当铺信物");
            Add("money_offer", true, new string[0], new string[0], "按说话者对钱的态度当面递钱");
            Add("money_deal", true, new string[0], new string[0], "对 Covert 的人走“正经买卖”");
            Add("start_event", true, new[] { "event" }, new[] { "event" }, "强制起一起市井事件（诊断/内容用）", new[] { "event" });
            // ── Q09 通用内容原子：具体 NPC / 事件号 / 金额全部由数据给，C# 不再按角色分支 ──
            Add("quest_accept", true, new[] { "npc", "solved_event" }, new[] { "npc" }, "为指定 NPC 接原生委托", new[] { "solved_event" });
            Add("quest_turn_in", true, new[] { "npc" }, new[] { "npc" }, "向指定 NPC 交差");
            Add("debt_collect", true, new[] { "from", "amount", "quest" }, new[] { "from", "amount", "quest" },
                "从指定 NPC 实收欠款并结清委托", new[] { "amount" });
            Add("grant_gold", true, new[] { "amount" }, new[] { "amount" }, "世界发放金币（Grant，来源 null）", new[] { "amount" });
            Add("reward_receipt", true, new[] { "npc", "key" }, new[] { "npc", "key" }, "记一条奖励收据（防重）");
            Add("message", true, new[] { "key" }, new[] { "key" }, "显示一条本地化消息");
        }

        private static void Add(string name, bool implemented, string[] allowed, string[] required, string notes,
            string[] intParams = null)
        {
            _specs[name] = new StoryEffectSpec
            {
                Name = name,
                Implemented = implemented,
                Params = allowed ?? new string[0],
                RequiredParams = required ?? new string[0],
                IntParams = intParams ?? new string[0],
                Notes = notes
            };
        }

        public static bool TryGet(string name, out StoryEffectSpec spec)
        {
            spec = null;
            return !string.IsNullOrEmpty(name) && _specs.TryGetValue(name, out spec);
        }

        public static IEnumerable<StoryEffectSpec> All => _specs.Values.OrderBy(s => s.Name, StringComparer.Ordinal);
    }

    // ───────────────────────────── 数据模型 ─────────────────────────────

    public sealed class StoryDialogueEffect
    {
        public string Id;
        public Dictionary<string, string> Params = new Dictionary<string, string>(StringComparer.Ordinal);
        public string Commit = "on_select";   // on_enter / on_select / on_session_end
        public string Receipt = "once";       // once / session / never
    }

    /// <summary>
    /// 选项的技能检定（P0-D.4/D.5）：掷一次、按数据路由。
    /// `Target` 省略 = 本次实际说话者；`Fail` 省略 = 关闭窗口。
    /// </summary>
    public sealed class StoryDialogueCheck
    {
        public string Skill;      // charm / guile / force
        public string Target;     // 参与者角色名或实名演员 id
        public string Success;    // 成功节点（必填）
        public string Fail;       // 失败节点（可空 = 关闭）
    }

    public sealed class StoryDialogueOption
    {
        public string Id;
        public string Speaker;
        public string Listener;
        public string PlayerKey;
        public List<string> Requires = new List<string>();
        public List<StoryDialogueEffect> Effects = new List<StoryDialogueEffect>();
        public StoryDialogueCheck Check;
        public string NextNode;
    }

    public sealed class StoryDialogueNodeDef
    {
        public string Id;
        public string Speaker;                 // 参与者角色名（如 questioner）或实名演员 id
        public string Listener;                // player / 本次演员角色 / 实名演员 id
        public string NextNode;                // 连续 NPC 台词；与玩家 Options 互斥
        public string TextKey;
        public List<string> LineActions = new List<string>();
        public List<string> Requires = new List<string>();
        public List<StoryDialogueEffect> OnEnter = new List<StoryDialogueEffect>();
        public List<StoryDialogueOption> Options = new List<StoryDialogueOption>();
    }

    public sealed class StoryDialogueEntryRule
    {
        public string Id;
        public string Trigger;
        public List<string> Conditions = new List<string>();
        public int Priority = 50;
        public string Node;
    }

    public sealed class StoryDialogueTriggerDef
    {
        public string Id;
        public string Source = "player";       // player / post_ai / event / script
        public string Purpose = "talk";
        public List<string> Conditions = new List<string>();
        public List<string> Rank = new List<string>();
        public float CooldownSec;
        public float ReentrySec;
        public string OnCancel = "release";
    }

    public sealed class StoryDialogueDef
    {
        public string Schema;
        public string DialogueId;
        public int ContentVersion;
        // 表现层粘合（P0-D.5）：只有入口选项文本与「是否跳过原版开场」两键，不含剧情逻辑。
        public string EntryOption;
        public bool SkipVanillaOpening;
        public List<string> Participants = new List<string>();
        public List<string> Actors = new List<string>();      // 允许出现的实名演员 id
        public List<string> EntryActors = new List<string>(); // 可作为本定义入口/业务归属的演员；参与者不等于入口
        public List<StoryDialogueTriggerDef> Triggers = new List<StoryDialogueTriggerDef>();
        public List<StoryDialogueEntryRule> Entries = new List<StoryDialogueEntryRule>();
        public List<StoryDialogueNodeDef> Nodes = new List<StoryDialogueNodeDef>();

        public StoryDialogueNodeDef FindNode(string id) { return Nodes.FirstOrDefault(n => n.Id == id); }
    }

    public sealed class StoryDialogueIssue
    {
        public string Path, Code, Message;
        public StoryDialogueIssue(string path, string code, string message) { Path = path; Code = code; Message = message; }
        public override string ToString() { return Path + " [" + Code + "] " + Message; }
    }

    public sealed class StoryDialogueResult
    {
        public bool Ok;
        public StoryDialogueDef Data;
        public List<StoryDialogueIssue> Issues = new List<StoryDialogueIssue>();
        public string Source;

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append(Ok ? "OK: " : "FAILED: ").Append(Source ?? "-");
            if (Data != null)
                sb.Append(" dialogueId=").Append(Data.DialogueId)
                  .Append(" contentVersion=").Append(Data.ContentVersion)
                  .Append(" nodes=").Append(Data.Nodes.Count)
                  .Append(" entries=").Append(Data.Entries.Count)
                  .Append(" triggers=").Append(Data.Triggers.Count);
            sb.Append(" issues=").Append(Issues.Count);
            foreach (var i in Issues.Take(15)) sb.Append('\n').Append("  ").Append(i);
            if (Issues.Count > 15) sb.Append("\n  ... +").Append(Issues.Count - 15).Append(" more");
            return sb.ToString();
        }
    }

    /// <summary>
    /// story_dialogue/2 严格解析器。
    /// 🔴 有错时 Data 仍返回（供打印已读字段），但 Ok=false 的 Data **不得**被任何执行路径使用。
    /// </summary>
    public static class StoryDialogueParser
    {
        public const string RequiredSchema = "story_dialogue/2";
        // 🔴 `*Zh` = 只读中文审核副本（总纲 §18.5.1），由 Scripts/annotate_dialogue_zh.py 从语言表生成，
        //    解析器只放行、**从不读取**；漏放行 = 整批对话 unknown_key 全部不发布。
        public static readonly string[] RootKeys =
            { "Schema", "DialogueId", "ContentVersion", "Participants", "Actors", "EntryActors", "Triggers", "Entries", "Nodes",
              "EntryOption", "EntryOptionZh", "SkipVanillaOpening" };
        private static readonly string[] Sources = { "player", "post_ai", "event", "script" };
        private static readonly string[] Commits = { "on_enter", "on_select", "on_session_end" };
        private static readonly string[] Receipts = { "once", "session", "never" };
        private static readonly string[] RankKeys = { "distanceToPlayer", "npcId" };

        public static StoryDialogueResult Parse(string json, string source, bool formalMode)
        {
            var result = new StoryDialogueResult { Source = source };
            if (string.IsNullOrEmpty(json))
            {
                result.Issues.Add(new StoryDialogueIssue(source, "empty_input", "文件为空或读不到"));
                return result;
            }
            string dup = FindDuplicateKey(json);
            if (dup != null)
            {
                result.Issues.Add(new StoryDialogueIssue(source, "duplicate_json_key", "JSON 对象内重复键 '" + dup + "'"));
                return result;
            }
            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception ex) { result.Issues.Add(new StoryDialogueIssue(source, "json_parse", "JSON 解析失败：" + ex.Message)); return result; }

            var errors = result.Issues;
            var def = new StoryDialogueDef();
            result.Data = def;

            foreach (var p in root.Properties())
                if (!p.Name.StartsWith("_", StringComparison.Ordinal) && !RootKeys.Contains(p.Name))
                    errors.Add(new StoryDialogueIssue("$." + p.Name, "unknown_key", "根层未知字段 '" + p.Name + "'"));
            def.Schema = Str(root, "Schema", "$", errors, true);
            if (!string.IsNullOrEmpty(def.Schema) && def.Schema != RequiredSchema)
                errors.Add(new StoryDialogueIssue("$.Schema", "schema_mismatch", "必须是 '" + RequiredSchema + "'，实际 '" + def.Schema + "'"));
            def.DialogueId = Str(root, "DialogueId", "$", errors, true);
            if (!string.IsNullOrEmpty(def.DialogueId) && !System.Text.RegularExpressions.Regex.IsMatch(def.DialogueId, "^[a-z0-9_]+$"))
                errors.Add(new StoryDialogueIssue("$.DialogueId", "bad_token", "只能小写字母/数字/下划线：" + def.DialogueId));
            def.ContentVersion = Int(root, "ContentVersion", "$", errors, 0, 1, int.MaxValue);
            if (!root.ContainsKey("ContentVersion"))
                errors.Add(new StoryDialogueIssue("$.ContentVersion", "missing_field", "缺少 ContentVersion"));
            def.Participants = StrArray(root, "Participants", "$", errors, true);
            def.Actors = StrArray(root, "Actors", "$", errors, false);
            def.EntryActors = StrArray(root, "EntryActors", "$", errors, true);
            def.EntryOption = Str(root, "EntryOption", "$", errors, false);
            def.SkipVanillaOpening = Bool(root, "SkipVanillaOpening", "$", errors, false);
            if (formalMode && !def.SkipVanillaOpening)
                errors.Add(new StoryDialogueIssue("$.SkipVanillaOpening", "bad_turn", "正式剧情必须由入口 NPC 先说话，不注册玩家网关开场"));
            Dup(def.Participants, "$.Participants", "参与者", errors);
            Dup(def.Actors, "$.Actors", "实名演员", errors);
            Dup(def.EntryActors, "$.EntryActors", "入口演员", errors);
            foreach (var actor in def.EntryActors)
                if (!def.Actors.Contains(actor))
                    errors.Add(new StoryDialogueIssue("$.EntryActors", "unknown_actor", "入口演员必须登记在 Actors：" + actor));

            ParseTriggers(root, def, errors);
            ParseEntries(root, def, errors);
            ParseNodes(root, def, errors);
            ValidateReferences(def, errors, formalMode);

            result.Ok = errors.Count == 0;
            return result;
        }

        private static void ParseTriggers(JObject root, StoryDialogueDef def, List<StoryDialogueIssue> errors)
        {
            foreach (var t in Arr(root, "Triggers", "$", errors, true))
            {
                if (!(t is JObject to)) { errors.Add(new StoryDialogueIssue("$.Triggers", "wrong_type", "触发器必须是对象")); continue; }
                string path = "$.Triggers[" + def.Triggers.Count + "]";
                Keys(to, path, new[] { "Id", "Source", "Purpose", "Conditions", "Rank", "CooldownSec", "ReentrySec", "OnCancel" }, errors);
                var trig = new StoryDialogueTriggerDef
                {
                    Id = Str(to, "Id", path, errors, true),
                    Source = Str(to, "Source", path, errors, true) ?? "player",
                    Purpose = Str(to, "Purpose", path, errors, false) ?? "talk",
                    Conditions = StrList(to, "Conditions", path, errors, false),
                    Rank = StrList(to, "Rank", path, errors, false),
                    CooldownSec = Float(to, "CooldownSec", path, errors, 0f, 0f, 3600f),
                    ReentrySec = Float(to, "ReentrySec", path, errors, 0f, 0f, 3600f),
                    OnCancel = Str(to, "OnCancel", path, errors, false) ?? "release"
                };
                if (!Sources.Contains(trig.Source))
                    errors.Add(new StoryDialogueIssue(path + ".Source", "bad_token", "Source 只能是 " + string.Join("/", Sources)));
                if (trig.Rank.Count > 0)
                {
                    foreach (string r in trig.Rank)
                        if (!RankKeys.Contains(r))
                            errors.Add(new StoryDialogueIssue(path + ".Rank", "bad_token", "排序键只能是 " + string.Join("/", RankKeys)));
                    if (trig.Rank[trig.Rank.Count - 1] != "npcId")
                        errors.Add(new StoryDialogueIssue(path + ".Rank", "unstable_tiebreak", "排序键最后一项必须是 npcId"));
                }
                def.Triggers.Add(trig);
            }
            if (def.Triggers.Count == 0)
                errors.Add(new StoryDialogueIssue("$.Triggers", "empty_collection", "至少要有一个触发器"));
            Dup(def.Triggers.Select(t => t.Id).ToList(), "$.Triggers", "触发器", errors);
        }

        private static void ParseEntries(JObject root, StoryDialogueDef def, List<StoryDialogueIssue> errors)
        {
            foreach (var e in Arr(root, "Entries", "$", errors, true))
            {
                if (!(e is JObject eo)) { errors.Add(new StoryDialogueIssue("$.Entries", "wrong_type", "入口规则必须是对象")); continue; }
                string path = "$.Entries[" + def.Entries.Count + "]";
                Keys(eo, path, new[] { "Id", "Trigger", "Conditions", "Priority", "Node" }, errors);
                def.Entries.Add(new StoryDialogueEntryRule
                {
                    Id = Str(eo, "Id", path, errors, true),
                    Trigger = Str(eo, "Trigger", path, errors, true),
                    Conditions = StrList(eo, "Conditions", path, errors, false),
                    Priority = Int(eo, "Priority", path, errors, 50, -10000, 10000),
                    Node = Str(eo, "Node", path, errors, true)
                });
            }
            if (def.Entries.Count == 0)
                errors.Add(new StoryDialogueIssue("$.Entries", "empty_collection", "至少要有一个入口规则"));
            Dup(def.Entries.Select(e => e.Id).ToList(), "$.Entries", "入口规则", errors);
        }

        private static void ParseNodes(JObject root, StoryDialogueDef def, List<StoryDialogueIssue> errors)
        {
            foreach (var n in Arr(root, "Nodes", "$", errors, true))
            {
                if (!(n is JObject no)) { errors.Add(new StoryDialogueIssue("$.Nodes", "wrong_type", "节点必须是对象")); continue; }
                string path = "$.Nodes[" + def.Nodes.Count + "]";
                Keys(no, path, new[] { "Id", "Speaker", "Listener", "NextNode", "TextKey", "TextKeyZh", "LineActions", "Requires", "OnEnter", "Options" }, errors);
                var node = new StoryDialogueNodeDef
                {
                    Id = Str(no, "Id", path, errors, true),
                    Speaker = Str(no, "Speaker", path, errors, true),
                    Listener = Str(no, "Listener", path, errors, true),
                    NextNode = Str(no, "NextNode", path, errors, false),
                    TextKey = Str(no, "TextKey", path, errors, true),
                    LineActions = StrList(no, "LineActions", path, errors, false),
                    Requires = StrList(no, "Requires", path, errors, false)
                };
                node.OnEnter = ParseEffects(no, "OnEnter", path, errors);
                int oi = 0;
                foreach (var o in Arr(no, "Options", path, errors, false))
                {
                    string opath = path + ".Options[" + oi + "]";
                    oi++;
                    if (!(o is JObject oo)) { errors.Add(new StoryDialogueIssue(opath, "wrong_type", "选项必须是对象")); continue; }
                    Keys(oo, opath, new[] { "Id", "Speaker", "Listener", "PlayerKey", "PlayerKeyZh", "Requires", "Effects", "Check", "NextNode" }, errors);
                    var opt = new StoryDialogueOption
                    {
                        Id = Str(oo, "Id", opath, errors, true),
                        Speaker = Str(oo, "Speaker", opath, errors, true),
                        Listener = Str(oo, "Listener", opath, errors, true),
                        PlayerKey = Str(oo, "PlayerKey", opath, errors, true),
                        Requires = StrList(oo, "Requires", opath, errors, false),
                        Effects = ParseEffects(oo, "Effects", opath, errors),
                        Check = ParseCheck(oo, opath, errors),
                        NextNode = Str(oo, "NextNode", opath, errors, false)
                    };
                    node.Options.Add(opt);
                }
                Dup(node.Options.Select(x => x.Id).ToList(), path + ".Options", "选项", errors);
                def.Nodes.Add(node);
            }
            if (def.Nodes.Count == 0)
                errors.Add(new StoryDialogueIssue("$.Nodes", "empty_collection", "至少要有一个节点"));
            Dup(def.Nodes.Select(n => n.Id).ToList(), "$.Nodes", "节点", errors);
        }

        private static List<StoryDialogueEffect> ParseEffects(JObject owner, string name, string path, List<StoryDialogueIssue> errors)
        {
            var list = new List<StoryDialogueEffect>();
            int i = 0;
            foreach (var e in Arr(owner, name, path, errors, false))
            {
                string epath = path + "." + name + "[" + i + "]";
                i++;
                if (!(e is JObject eo)) { errors.Add(new StoryDialogueIssue(epath, "wrong_type", "效果必须是对象")); continue; }
                Keys(eo, epath, new[] { "Id", "Params", "Commit", "Receipt" }, errors);
                var eff = new StoryDialogueEffect
                {
                    Id = Str(eo, "Id", epath, errors, true),
                    Commit = Str(eo, "Commit", epath, errors, false) ?? "on_select",
                    Receipt = Str(eo, "Receipt", epath, errors, false) ?? "once"
                };
                if (!Commits.Contains(eff.Commit))
                    errors.Add(new StoryDialogueIssue(epath + ".Commit", "bad_token", "Commit 只能是 " + string.Join("/", Commits)));
                if (!Receipts.Contains(eff.Receipt))
                    errors.Add(new StoryDialogueIssue(epath + ".Receipt", "bad_token", "Receipt 只能是 " + string.Join("/", Receipts)));
                if (eo["Params"] is JObject po)
                    foreach (var pp in po.Properties())
                    {
                        if (pp.Value.Type != JTokenType.String && pp.Value.Type != JTokenType.Integer
                            && pp.Value.Type != JTokenType.Float && pp.Value.Type != JTokenType.Boolean)
                            errors.Add(new StoryDialogueIssue(epath + ".Params." + pp.Name, "wrong_type", "参数值只能是字符串/数字/布尔"));
                        eff.Params[pp.Name] = pp.Value.ToString();
                    }
                list.Add(eff);
            }
            return list;
        }

        private static StoryDialogueCheck ParseCheck(JObject owner, string path, List<StoryDialogueIssue> errors)
        {
            var token = owner?["Check"];
            if (token == null || token.Type == JTokenType.Null) return null;
            if (!(token is JObject co)) { errors.Add(new StoryDialogueIssue(path + ".Check", "wrong_type", "Check 必须是对象")); return null; }
            string cpath = path + ".Check";
            Keys(co, cpath, new[] { "Skill", "Target", "Success", "Fail" }, errors);
            var check = new StoryDialogueCheck
            {
                Skill = Str(co, "Skill", cpath, errors, true),
                Target = Str(co, "Target", cpath, errors, false),
                Success = Str(co, "Success", cpath, errors, true),
                Fail = Str(co, "Fail", cpath, errors, false)
            };
            if (!string.IsNullOrEmpty(check.Skill) && !IsAbilityToken(check.Skill))
                errors.Add(new StoryDialogueIssue(cpath + ".Skill", "bad_token",
                    "检定技能只能是 charm/guile/force：" + check.Skill));
            return check;
        }

        private static bool IsAbilityToken(string value)
            => string.Equals(value, "charm", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "guile", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "force", StringComparison.OrdinalIgnoreCase);

        private static void ValidateReferences(StoryDialogueDef def, List<StoryDialogueIssue> errors, bool formalMode)
        {
            var triggerIds = new HashSet<string>(def.Triggers.Select(t => t.Id), StringComparer.Ordinal);
            var nodeIds = new HashSet<string>(def.Nodes.Select(n => n.Id), StringComparer.Ordinal);
            var participants = new HashSet<string>(def.Participants, StringComparer.Ordinal);
            var actors = new HashSet<string>(def.Actors, StringComparer.Ordinal);

            foreach (var trig in def.Triggers)
                CheckConditions(trig.Conditions, "$.Triggers." + trig.Id, errors, formalMode);

            foreach (var entry in def.Entries)
            {
                string path = "$.Entries." + entry.Id;
                if (!triggerIds.Contains(entry.Trigger ?? ""))
                    errors.Add(new StoryDialogueIssue(path + ".Trigger", "unknown_trigger", "触发器 '" + entry.Trigger + "' 不存在"));
                if (!nodeIds.Contains(entry.Node ?? ""))
                    errors.Add(new StoryDialogueIssue(path + ".Node", "unknown_node", "节点 '" + entry.Node + "' 不存在"));
                CheckConditions(entry.Conditions, path, errors, formalMode);
            }

            // 入口并列必须靠稳定 ID 决胜
            foreach (var group in def.Entries.GroupBy(e => e.Trigger ?? ""))
            {
                var dupPriority = group.GroupBy(e => e.Priority).Any(g => g.Count() > 1);
                if (dupPriority && group.Select(e => e.Id).Distinct().Count() != group.Count())
                    errors.Add(new StoryDialogueIssue("$.Entries", "duplicate_id", "入口规则 ID 重复，无法稳定决胜"));
            }

            foreach (var node in def.Nodes)
            {
                string path = "$.Nodes." + node.Id;
                if (!participants.Contains(node.Speaker ?? "") && !actors.Contains(node.Speaker ?? ""))
                    errors.Add(new StoryDialogueIssue(path + ".Speaker", "unknown_speaker",
                        "说话者 '" + node.Speaker + "' 既不在 Participants 也不在 Actors"));
                if (!participants.Contains(node.Listener ?? "") && !actors.Contains(node.Listener ?? ""))
                    errors.Add(new StoryDialogueIssue(path + ".Listener", "unknown_listener", "听话者未登记：" + node.Listener));
                if (node.Speaker == "player" || node.Speaker == node.Listener)
                    errors.Add(new StoryDialogueIssue(path, "bad_turn", "节点必须由 NPC 对另一位参与者说一句话"));
                if (!string.IsNullOrEmpty(node.NextNode) && !nodeIds.Contains(node.NextNode))
                    errors.Add(new StoryDialogueIssue(path + ".NextNode", "unknown_node", "连续台词目标不存在：" + node.NextNode));
                if (!string.IsNullOrEmpty(node.NextNode) && node.Options.Count > 0)
                    errors.Add(new StoryDialogueIssue(path, "bad_turn", "NextNode 与 Options 只能二选一"));
                if (node.LineActions.Count > 1)
                    errors.Add(new StoryDialogueIssue(path + ".LineActions", "bad_turn", "一句台词最多配一个动作"));
                CheckConditions(node.Requires, path, errors, formalMode);
                CheckEffects(node.OnEnter, path, errors, formalMode);
                foreach (var opt in node.Options)
                {
                    string opath = path + ".Options." + opt.Id;
                    if (opt.Speaker != "player" || opt.Listener == "player")
                        errors.Add(new StoryDialogueIssue(opath, "bad_turn", "玩家选项必须由 player 对 NPC 说话"));
                    if (!participants.Contains(opt.Listener ?? "") && !actors.Contains(opt.Listener ?? ""))
                        errors.Add(new StoryDialogueIssue(opath + ".Listener", "unknown_listener", "选项听话者未登记：" + opt.Listener));
                    CheckConditions(opt.Requires, opath, errors, formalMode);
                    CheckEffects(opt.Effects, opath, errors, formalMode);
                    if (!string.IsNullOrEmpty(opt.NextNode) && !nodeIds.Contains(opt.NextNode))
                        errors.Add(new StoryDialogueIssue(opath + ".NextNode", "unknown_node", "节点 '" + opt.NextNode + "' 不存在"));
                    if (opt.Check != null)
                    {
                        // 检定路由与直连二选一：同时写 = 说不清该走哪条。
                        if (!string.IsNullOrEmpty(opt.NextNode))
                            errors.Add(new StoryDialogueIssue(opath, "bad_value", "Check 与 NextNode 只能二选一"));
                        if (!nodeIds.Contains(opt.Check.Success ?? ""))
                            errors.Add(new StoryDialogueIssue(opath + ".Check.Success", "unknown_node",
                                "成功节点 '" + opt.Check.Success + "' 不存在"));
                        if (!string.IsNullOrEmpty(opt.Check.Fail) && !nodeIds.Contains(opt.Check.Fail))
                            errors.Add(new StoryDialogueIssue(opath + ".Check.Fail", "unknown_node",
                                "失败节点 '" + opt.Check.Fail + "' 不存在"));
                        if (!string.IsNullOrEmpty(opt.Check.Target)
                            && !participants.Contains(opt.Check.Target) && !actors.Contains(opt.Check.Target))
                            errors.Add(new StoryDialogueIssue(opath + ".Check.Target", "unknown_speaker",
                                "检定目标 '" + opt.Check.Target + "' 既不在 Participants 也不在 Actors"));
                    }
                }
            }
            // 只检查无选择的连续边；允许有玩家选择的正常话题回环。
            foreach (var origin in def.Nodes)
            {
                var visited = new HashSet<string>(StringComparer.Ordinal);
                var cursor = origin;
                while (cursor != null && !string.IsNullOrEmpty(cursor.NextNode))
                {
                    if (!visited.Add(cursor.Id))
                    {
                        errors.Add(new StoryDialogueIssue("$.Nodes." + origin.Id, "continuation_cycle", "连续 NPC 台词不能成环"));
                        break;
                    }
                    cursor = def.FindNode(cursor.NextNode);
                }
            }
        }

        private static void CheckConditions(List<string> conditions, string path, List<StoryDialogueIssue> errors, bool formalMode)
        {
            foreach (string raw in conditions)
            {
                string term = raw ?? "";
                if (term.StartsWith("!", StringComparison.Ordinal)) term = term.Substring(1);
                if (term.Length == 0) { errors.Add(new StoryDialogueIssue(path + ".Requires", "bad_value", "空条件")); continue; }
                int split = term.IndexOf(':');
                string name = split < 0 ? term : term.Substring(0, split);
                string value = split < 0 ? null : term.Substring(split + 1);
                // 运算符式写法：gold>=100（与 item:xxx 这类冒号式共存）
                if (split < 0 && term.StartsWith("gold>=", StringComparison.Ordinal))
                {
                    name = "gold>=";
                    value = term.Substring("gold>=".Length);
                }
                else if (split < 0 && term.StartsWith("step>=", StringComparison.Ordinal))
                {
                    name = "step>=";
                    value = term.Substring("step>=".Length);
                }
                else if (split < 0 && term.IndexOf('@') > 0)
                {
                    // 跨 NPC 引用：quest_accepted@pawnbroker
                    int at = term.IndexOf('@');
                    name = term.Substring(0, at);
                    value = term.Substring(at + 1);
                }
                if (!StoryPredicateRegistry.TryGet(name, out var spec))
                {
                    errors.Add(new StoryDialogueIssue(path + ".Requires", "unknown_predicate", "未登记的条件谓词 '" + name + "'"));
                    continue;
                }
                if (spec.RequiredParams.Length > 0 && string.IsNullOrEmpty(value))
                    errors.Add(new StoryDialogueIssue(path + ".Requires", "missing_param", "谓词 '" + name + "' 需要参数"));
                if (value != null && (name == "gold>=" || name == "event_resolved"))
                {
                    if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
                        errors.Add(new StoryDialogueIssue(path + ".Requires", "bad_param", "谓词 '" + name + "' 的参数必须是非负整数：" + value));
                }
                if (!spec.Implemented && formalMode)
                    errors.Add(new StoryDialogueIssue(path + ".Requires", "unimplemented_predicate",
                        "条件 '" + name + "' 尚未实现，不能进正式数据（" + spec.Notes + "）"));
            }
        }

        private static void CheckEffects(List<StoryDialogueEffect> effects, string path, List<StoryDialogueIssue> errors, bool formalMode)
        {
            foreach (var eff in effects)
            {
                if (!StoryEffectRegistry.TryGet(eff.Id, out var spec))
                {
                    errors.Add(new StoryDialogueIssue(path + ".Effects", "unknown_effect", "未登记的效果原子 '" + eff.Id + "'"));
                    continue;
                }
                foreach (var kv in eff.Params)
                {
                    if (!spec.Params.Contains(kv.Key))
                        errors.Add(new StoryDialogueIssue(path + ".Effects." + eff.Id + ".Params", "unknown_param",
                            "效果 '" + eff.Id + "' 不接受参数 '" + kv.Key + "'"));
                    else if (string.IsNullOrEmpty(kv.Value))
                        errors.Add(new StoryDialogueIssue(path + ".Effects." + eff.Id + ".Params." + kv.Key, "bad_param",
                            "效果 '" + eff.Id + "' 的参数 '" + kv.Key + "' 不能为空"));
                    else if (spec.IntParams.Contains(kv.Key)
                             && !int.TryParse(kv.Value, System.Globalization.NumberStyles.Integer,
                                              System.Globalization.CultureInfo.InvariantCulture, out _))
                        errors.Add(new StoryDialogueIssue(path + ".Effects." + eff.Id + ".Params." + kv.Key, "bad_param",
                            "效果 '" + eff.Id + "' 的参数 '" + kv.Key + "' 必须是整数：" + kv.Value));
                }
                foreach (string req in spec.RequiredParams)
                    if (!eff.Params.ContainsKey(req))
                        errors.Add(new StoryDialogueIssue(path + ".Effects." + eff.Id + ".Params", "missing_param",
                            "效果 '" + eff.Id + "' 缺必需参数 '" + req + "'"));
                if (!spec.Implemented && formalMode)
                    errors.Add(new StoryDialogueIssue(path + ".Effects." + eff.Id, "unimplemented_effect",
                        "效果 '" + eff.Id + "' 尚未实现，不能进正式数据（" + spec.Notes + "）"));
            }
        }

        // ── 原语 ──
        private static string FindDuplicateKey(string json)
        {
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)))
                {
                    var stack = new List<HashSet<string>>();
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonToken.StartObject) stack.Add(new HashSet<string>(StringComparer.Ordinal));
                        else if (reader.TokenType == JsonToken.EndObject) { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); }
                        else if (reader.TokenType == JsonToken.PropertyName && stack.Count > 0)
                        {
                            string name = reader.Value as string ?? "";
                            if (!stack[stack.Count - 1].Add(name)) return name;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static void Keys(JObject o, string path, string[] allowed, List<StoryDialogueIssue> errors)
        {
            foreach (var p in o.Properties())
                if (!allowed.Contains(p.Name))
                    errors.Add(new StoryDialogueIssue(path + "." + p.Name, "unknown_key",
                        "未知字段 '" + p.Name + "'（允许：" + string.Join(", ", allowed) + "）"));
        }

        private static void Dup(List<string> ids, string path, string what, List<StoryDialogueIssue> errors)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in ids)
                if (!string.IsNullOrEmpty(id) && !seen.Add(id))
                    errors.Add(new StoryDialogueIssue(path, "duplicate_id", what + " ID 重复 '" + id + "'"));
        }

        private static JArray Arr(JObject o, string name, string path, List<StoryDialogueIssue> errors, bool required)
        {
            var t = o?[name];
            if (t == null || t.Type == JTokenType.Null)
            {
                if (required) errors.Add(new StoryDialogueIssue(path + "." + name, "missing_field", "缺少数组 '" + name + "'"));
                return new JArray();
            }
            if (!(t is JArray ja)) { errors.Add(new StoryDialogueIssue(path + "." + name, "wrong_type", "'" + name + "' 必须是数组")); return new JArray(); }
            return ja;
        }

        private static string Str(JObject o, string name, string path, List<StoryDialogueIssue> errors, bool required)
        {
            var t = o?[name];
            if (t == null || t.Type == JTokenType.Null)
            {
                if (required) errors.Add(new StoryDialogueIssue(path + "." + name, "missing_field", "缺少字符串 '" + name + "'"));
                return null;
            }
            if (t.Type != JTokenType.String) { errors.Add(new StoryDialogueIssue(path + "." + name, "wrong_type", "'" + name + "' 必须是字符串")); return null; }
            string v = t.ToString();
            if (required && v.Length == 0) errors.Add(new StoryDialogueIssue(path + "." + name, "missing_field", "'" + name + "' 不能为空"));
            return v;
        }

        private static int Int(JObject o, string name, string path, List<StoryDialogueIssue> errors, int fallback, int min, int max)
        {
            var t = o?[name];
            if (t == null || t.Type == JTokenType.Null) return fallback;
            if (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)
            { errors.Add(new StoryDialogueIssue(path + "." + name, "wrong_type", "'" + name + "' 必须是整数")); return fallback; }
            int v = (int)t;
            if (v < min || v > max) errors.Add(new StoryDialogueIssue(path + "." + name, "bad_range", "'" + name + "' 超出 [" + min + "," + max + "]：" + v));
            return v;
        }

        private static bool Bool(JObject o, string name, string path, List<StoryDialogueIssue> errors, bool fallback)
        {
            var t = o?[name];
            if (t == null || t.Type == JTokenType.Null) return fallback;
            if (t.Type != JTokenType.Boolean)
            {
                errors.Add(new StoryDialogueIssue(path + "." + name, "wrong_type", "'" + name + "' 必须是布尔值"));
                return fallback;
            }
            return (bool)t;
        }

        private static float Float(JObject o, string name, string path, List<StoryDialogueIssue> errors, float fallback, float min, float max)
        {
            var t = o?[name];
            if (t == null || t.Type == JTokenType.Null) return fallback;
            if (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)
            { errors.Add(new StoryDialogueIssue(path + "." + name, "wrong_type", "'" + name + "' 必须是数字")); return fallback; }
            float v = (float)t;
            if (float.IsNaN(v) || float.IsInfinity(v))
            { errors.Add(new StoryDialogueIssue(path + "." + name, "bad_value", "'" + name + "' 必须是有限数")); return fallback; }
            if (v < min || v > max) errors.Add(new StoryDialogueIssue(path + "." + name, "bad_range", "'" + name + "' 超出 [" + min + "," + max + "]：" + v));
            return v;
        }

        private static List<string> StrList(JObject o, string name, string path, List<StoryDialogueIssue> errors, bool required)
        {
            var outList = new List<string>();
            foreach (var item in Arr(o, name, path, errors, required))
            {
                if (item.Type != JTokenType.String) { errors.Add(new StoryDialogueIssue(path + "." + name, "wrong_type", "元素必须是字符串")); continue; }
                outList.Add(item.ToString());
            }
            if (required && outList.Count == 0)
                errors.Add(new StoryDialogueIssue(path + "." + name, "empty_collection", "'" + name + "' 不能为空"));
            return outList;
        }

        private static List<string> StrArray(JObject o, string name, string path, List<StoryDialogueIssue> errors, bool required)
        {
            return StrList(o, name, path, errors, required);
        }
    }

    /// <summary>
    /// 已发布对话定义集（按 DialogueId 查找，一个 NPC/守卫可复用同一份）。
    /// 🔴 一次发布 = 全部解析与校验通过；失败保留旧版本；运行实例应固定开始时的版本。
    /// </summary>
    public static class StoryDialogueRegistry
    {
        private static readonly Dictionary<string, StoryDialogueDef> _published =
            new Dictionary<string, StoryDialogueDef>(StringComparer.Ordinal);
        private static int _publishedVersion = -1;
        private static string _source;
        private static bool _formal;

        public static IReadOnlyCollection<StoryDialogueDef> All => _published.Values;
        public static int Count => _published.Count;
        public static int PublishedVersion => _publishedVersion;
        public static bool PublishedFormal => _formal;

        public static bool TryPublish(StoryDialogueDef def, string source)
        {
            if (def == null) return false;
            _published[def.DialogueId] = def;
            _source = source;
            if (def.ContentVersion > _publishedVersion) _publishedVersion = def.ContentVersion;
            DebugLogger.Log("[StoryDialogue] published " + def.DialogueId + " v" + def.ContentVersion + " from " + source);
            return true;
        }

        /// <summary>整批发布（一次成功才替换）：先解析全部，任一失败就保留旧版。</summary>
        public static void ReplaceAll(IEnumerable<StoryDialogueDef> defs, string source, bool formal)
        {
            _published.Clear();
            _publishedVersion = -1;
            foreach (var def in defs)
            {
                _published[def.DialogueId] = def;
                if (def.ContentVersion > _publishedVersion) _publishedVersion = def.ContentVersion;
            }
            _source = source;
            _formal = formal;
            DebugLogger.Log("[StoryDialogue] published set count=" + _published.Count + " v" + _publishedVersion + " formal=" + formal + " from " + source);
        }

        public static bool TryGet(string dialogueId, out StoryDialogueDef def)
        {
            def = null;
            return !string.IsNullOrEmpty(dialogueId) && _published.TryGetValue(dialogueId, out def);
        }

        public static void Clear()
        {
            _published.Clear();
            _publishedVersion = -1;
            _source = null;
        }

        public static string Describe()
        {
            if (_published.Count == 0) return "OK: no published dialogue definitions";
            return "OK: dialogues=" + _published.Count + " version=" + _publishedVersion + " formal=" + _formal
                 + " ids=[" + string.Join(",", _published.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray()) + "] source=" + (_source ?? "-");
        }
    }

    /// <summary>
    /// 正式对话定义的装载（P0-D.5 的迁移桥）：
    ///   · 只认 `ModuleData/DesignData/Dialogues/*.dialog.json`（新格式），本目录只维护这一种格式，不回落旧文件。
    ///   · 先解析全部 → 任一失败则**整体不发布**、保留旧版；全部通过才替换。
    ///   · 按需装载一次；`custom.story_probe reload` 会强制重载。
    /// </summary>
    public static class StoryDialogueLoader
    {
        private static bool _loadedOnce;
        private static string _lastReport = "not loaded";

        public static string LastReport => _lastReport;

        public static void EnsureLoaded()
        {
            if (_loadedOnce) return;
            _loadedOnce = true;
            ReloadAll();
        }

        public static string ReloadAll()
        {
            string folder = StoryProbeDataFiles.DesignDataFolder;
            if (string.IsNullOrEmpty(folder)) { _lastReport = "no design data folder"; return _lastReport; }
            string dir = Path.Combine(folder, "Dialogues");
            if (!Directory.Exists(dir)) { _lastReport = "dialogue folder missing: " + dir; return _lastReport; }

            var files = Directory.GetFiles(dir, "*.dialog.json").OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (files.Count == 0)
            {
                _lastReport = "no *.dialog.json definitions";
                return _lastReport;
            }

            var parsed = new List<StoryDialogueDef>();
            var errors = new List<string>();
            var idSource = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string path in files)
            {
                var result = StoryDialogueParser.Parse(File.ReadAllText(path), path, formalMode: true);
                if (!result.Ok)
                {
                    errors.Add(result.Describe());
                    continue;
                }
                string id = result.Data.DialogueId;
                if (idSource.TryGetValue(id, out string first))
                {
                    // 跨文件同一 DialogueId = 后者会静默覆盖前者；整批拒绝、保留旧集。
                    errors.Add(path + " [duplicate_dialogue_id] DialogueId '" + id
                               + "' 已在 " + first + " 定义；本批不发布");
                    continue;
                }
                idSource[id] = path;
                parsed.Add(result.Data);
            }

            if (errors.Count > 0)
            {
                // 坏重载留旧版：一条失败 = 本批整体不发布
                _lastReport = "rejected " + errors.Count + "/" + files.Count + " file(s); kept previous set\n" +
                              string.Join("\n", errors.Take(5).ToArray());
                DebugLogger.Log("[StoryDialogue] reload rejected: " + _lastReport);
                return _lastReport;
            }

            StoryDialogueRegistry.ReplaceAll(parsed, dir, formal: true);
            _lastReport = "loaded " + parsed.Count + " definition(s) from " + dir;
            return _lastReport;
        }
    }
}
