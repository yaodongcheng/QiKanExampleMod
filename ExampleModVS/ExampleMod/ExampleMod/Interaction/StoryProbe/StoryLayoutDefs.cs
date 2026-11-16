using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════════════════
    // P0 契约（2026-11-15 立）—— 数据定义与执行契约的唯一登记处
    //
    // 依据：plans/逆水寒511/逆水寒511-总纲与实施计划.md §14.3
    //
    // 本文件只做三件事：
    //   ① 原子注册表（名字 / 参数 / 是否需要演员 / 等待语义 / 运行时是否已实现）
    //   ② v2 布局+阶段+行为+流程数据的严格解析（未知字段、未知 token、缺引用一律拒绝）
    //   ③ 发布（解析→全量校验→一次发布；失败保留上一有效版本）
    //
    // 🔴 纪律：
    //   · 这里**没有**任何角色 ID、事件编号、阶段名专属逻辑 —— 具体内容全在 JSON。
    //   · 新增原子/字段 = 先在本表登记，再改数据；未登记的 token 一律拒绝。
    //   · `Implemented=false` 的原子只允许出现在夹具里；正式数据引用即拒绝（S8）。
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>一个原子的契约：允许的参数、需要的演员绑定、等待语义、运行时是否已经实现。</summary>
    public sealed class StoryAtomSpec
    {
        public string Name;
        /// <summary>Args / 台本字段里允许出现的参数名。</summary>
        public string[] Params = new string[0];
        /// <summary>其中必须出现的参数名。</summary>
        public string[] RequiredParams = new string[0];
        public string[] NumberParams = new string[0];
        public string[] BooleanParams = new string[0];
        /// <summary>none / required（台本必须写 Actor）。</summary>
        public string ActorRole = "none";
        /// <summary>该原子接受哪些 wait.Kind；空数组 = 不接受 Wait。</summary>
        public string[] WaitKinds = new string[0];
        /// <summary>运行时是否已经能真正执行（false = 只有契约，正式数据禁止引用）。</summary>
        public bool Implemented;
        public string Notes = "";
    }

    /// <summary>
    /// 原子注册表 —— 511 演出/流程能用的**全部**原子都在这里，一处登记。
    /// 🔴 加一条 = 先在这里登记，再谈能不能写进正式数据。
    /// </summary>
    public static class StoryAtomRegistry
    {
        private static readonly Dictionary<string, StoryAtomSpec> _atoms =
            new Dictionary<string, StoryAtomSpec>(StringComparer.OrdinalIgnoreCase);

        static StoryAtomRegistry()
        {
            Add(new StoryAtomSpec
            {
                Name = "player_freeze", Implemented = true,
                Notes = "冻结并原地按住主玩家；已有他人控制时拒绝。"
            });
            Add(new StoryAtomSpec
            {
                Name = "player_release", Implemented = true,
                Notes = "交还玩家控制；已被捆缚/击晕/倒地接管时不擅自解锁。"
            });
            Add(new StoryAtomSpec
            {
                Name = "camera_cut", Implemented = true,
                RequiredParams = new[] { "Case" }, Params = new[] { "Case" },
                Notes = "按 Camera.csv 的机位 ID 接管相机。"
            });
            Add(new StoryAtomSpec
            {
                Name = "camera_return", Implemented = true,
                Notes = "交还引擎相机。"
            });
            Add(new StoryAtomSpec
            {
                Name = "prop_spawn", Implemented = true,
                RequiredParams = new[] { "Id" }, Params = new[] { "Id", "Prefab" },
                Notes = "v2 只允许引用阶段已登记、声明为延后出现的道具；不得携带 Anchor/Fwd/Right/Yaw/Scale（几何只有布局一份）。"
            });
            Add(new StoryAtomSpec
            {
                Name = "actor_place", Implemented = true,
                Params = new[] { "Post", "WorkPoint", "Anchor", "Fwd", "Right", "Up", "Facing" },
                NumberParams = new[] { "Fwd", "Right", "Up", "Facing" },
                ActorRole = "required",
                Notes = "只摆已登记、已获控制的演员；Post/WorkPoint/Anchor 三选一。"
            });
            Add(new StoryAtomSpec
            {
                Name = "actor_action", Implemented = true,
                RequiredParams = new[] { "Action" }, Params = new[] { "Action", "Async" },
                BooleanParams = new[] { "Async" },
                ActorRole = "required",
                Notes = "动作必须存在于该 NPC 实际使用的动作集（离线检查器另核）。"
            });
            Add(new StoryAtomSpec
            {
                Name = "actor_say", Implemented = true,
                RequiredParams = new[] { "TextKey", "Listener" }, Params = new[] { "TextKey", "Listener" },
                ActorRole = "required",
                Notes = "本地化键，必须在中英两侧语言 XML 存在。"
            });
            Add(new StoryAtomSpec
            {
                Name = "wait", Implemented = false,
                RequiredParams = new[] { "Seconds" }, Params = new[] { "Seconds" },
                NumberParams = new[] { "Seconds" },
                Notes = "v1 只打印一行日志，没有阻塞语义；要真正等待请用流程步骤的 Wait。"
            });
            Add(new StoryAtomSpec
            {
                Name = "actor_move", Implemented = false,
                RequiredParams = new[] { "To" }, Params = new[] { "To", "StopMeters" },
                NumberParams = new[] { "StopMeters" },
                ActorRole = "required", WaitKinds = new[] { "arrival" },
                Notes = "经 Brain 走到目标；需真实到位/超时/中断结果，未实现前不许进正式数据。"
            });
            Add(new StoryAtomSpec
            {
                Name = "dialogue_open", Implemented = true,
                RequiredParams = new[] { "Dialogue" }, Params = new[] { "Dialogue" },
                ActorRole = "required", WaitKinds = new[] { "dialogue_open" },
                Notes = "打开原版对话；已有全局对话互斥。"
            });
            Add(new StoryAtomSpec
            {
                Name = "dialogue_wait", Implemented = true,
                ActorRole = "required", WaitKinds = new[] { "dialogue_end" },
                Notes = "等本次对话实际结束；不能用固定秒数冒充。"
            });
        }

        private static void Add(StoryAtomSpec spec) { _atoms[spec.Name] = spec; }

        public static bool TryGet(string name, out StoryAtomSpec spec)
        {
            spec = null;
            if (string.IsNullOrEmpty(name)) return false;
            return _atoms.TryGetValue(name, out spec);
        }

        public static IEnumerable<StoryAtomSpec> All => _atoms.Values.OrderBy(a => a.Name, StringComparer.Ordinal);

        /// <summary>`custom.story_probe script list` 的正文（纯英文）。</summary>
        public static string Describe()
        {
            var sb = new StringBuilder();
            sb.Append("OK: atoms=").Append(_atoms.Count).Append('\n');
            foreach (var a in All)
            {
                sb.Append("  ").Append(a.Implemented ? "[ready] " : "[contract-only] ").Append(a.Name);
                if (a.Params.Length > 0) sb.Append(" params=").Append(string.Join(",", a.Params));
                if (a.RequiredParams.Length > 0) sb.Append(" required=").Append(string.Join(",", a.RequiredParams));
                if (a.ActorRole != "none") sb.Append(" actor=").Append(a.ActorRole);
                if (a.WaitKinds.Length > 0) sb.Append(" wait=").Append(string.Join(",", a.WaitKinds));
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }

    public enum StoryValidationMode { Fixture, Formal }

    public sealed class StoryValidationIssue
    {
        public string Path;
        public string Code;
        public string Message;
        public StoryValidationIssue(string path, string code, string message)
        { Path = path; Code = code; Message = message; }
        public override string ToString() { return Path + " [" + Code + "] " + Message; }
    }

    // ───────────────────────────── 数据模型 ─────────────────────────────

    public sealed class StoryPoint
    {
        public string Anchor;
        public float Fwd, Right, Up;
    }

    public sealed class StoryAnchorDef
    {
        public string Source = "tag";           // tag / entity / player
        public string[] Value = new string[0];
        public string Pick = "first";           // first / nearestToPlayer
        public bool Required = true;
    }

    public sealed class StoryGateDef
    {
        public StoryPoint Center = new StoryPoint();
        public StoryPoint Outside = new StoryPoint();
        public StoryPoint Inner = new StoryPoint();
        public float HalfWidth = 3f;
        public float InsideMeters = 8f;
        public float OutsideMeters = 3f;
    }

    public sealed class StoryPropDef
    {
        public string Id;
        public string Source = "prefab";        // prefab / existing
        public string Prefab;
        public string[] EntityTag = new string[0];
        public StoryPoint Place = new StoryPoint();
        public float Yaw;
        public float Scale = 1f;
        public string Support = "ground";       // ground / fixed-up
        public float[] Footprint = new float[0];
        public bool Required = true;
    }

    public sealed class StoryWorkPointDef
    {
        public string Id;
        public string Prop;
        public string Side = "front";           // front / back / left / right
        public float Offset;
        public float[] Contact = new float[0];
        public float Yaw;
        public float ArriveTolerance = 0.6f;
        public int MaxOccupants = 1;
        public string Action;
    }

    public sealed class StoryPostDef
    {
        public string Id;
        public string Gate;
        public StoryPoint Place = new StoryPoint();
        public float Facing = -1f;
    }

    public sealed class StoryStandingDef
    {
        public string Actor;
        public string Post;
        public string WorkPoint;
        public StoryPoint Place = new StoryPoint();
        public float Facing = -1f;
    }

    public sealed class StoryLayoutDef
    {
        public string Id;
        public int Version = 1;
        public Dictionary<string, StoryAnchorDef> Anchors = new Dictionary<string, StoryAnchorDef>(StringComparer.Ordinal);
        public Dictionary<string, StoryGateDef> Gates = new Dictionary<string, StoryGateDef>(StringComparer.Ordinal);
        public List<StoryPropDef> Props = new List<StoryPropDef>();
        public List<StoryWorkPointDef> WorkPoints = new List<StoryWorkPointDef>();
        public List<StoryPostDef> Posts = new List<StoryPostDef>();
        public List<StoryStandingDef> Standing = new List<StoryStandingDef>();

        public StoryPropDef FindProp(string id) { return Props.FirstOrDefault(p => p.Id == id); }
    }

    public sealed class StoryPrepDef
    {
        public bool SkipAllowed = true;
        public float ActorReadyTimeoutSec = 3f;
    }

    public sealed class StoryStageDef
    {
        public string Id;
        public string Layout;
        public List<string> Actors = new List<string>();
        public List<string> Props = new List<string>();
        public List<string> OptionalActors = new List<string>();
        public List<string> OptionalProps = new List<string>();
        public bool Opening;
        public StoryPrepDef Prep = new StoryPrepDef();
    }

    public sealed class StoryPerceptionDef
    {
        public bool RequiresSight = true;
        public float RangeMeters = 12f;
    }

    public sealed class StoryTriggerDef
    {
        public bool PlayerInsideGate = true;
        /// <summary>
        /// 岗位职责区（2026-11-16）：玩家只要进了**我这名守卫的感知半径**就算进我的区，
        /// 不再要求他正好踩在门轴中线 ±3 米那条窄带里。
        /// false = 沿用旧的 PlayerInsideGate 口径（旧数据行为不变）。
        /// </summary>
        public bool PlayerInPostZone = false;
        public int MaxPerEncounter = 1;
    }

    public sealed class StoryCooldownDef
    {
        public float RetrySec = 3f;
        public float ReentrySec = 8f;
    }

    public sealed class StoryBehaviorDef
    {
        public string Id;
        public string Kind = "post";            // post / group
        public List<string> Posts = new List<string>();
        public List<string> Stages = new List<string>();
        public StoryPerceptionDef Perception = new StoryPerceptionDef();
        public StoryTriggerDef Trigger = new StoryTriggerDef();
        public StoryCooldownDef Cooldown = new StoryCooldownDef();
        public List<string> Rank = new List<string>();   // distanceToPlayer / npcId
        public string Flow;
        public int MaxActive = 1;
        // ── Kind=group（市井群组事件，P4 迁入）──
        public List<string> Members = new List<string>();
        public string Host;
        public List<string> Lines = new List<string>();
        public List<string> Speakers = new List<string>();
        public List<string> Listeners = new List<string>();
        public List<string> LineFallbacks = new List<string>();
        public List<string> ActionsA = new List<string>();
        public List<string> ActionsB = new List<string>();
        public string JoinNode;
    }

    public sealed class StoryStepWait
    {
        public string Kind;                     // arrival / dialogue_open / dialogue_end / timer
        public float TimeoutSec;
    }

    public sealed class StoryFlowStep
    {
        public string Id;
        public string Atom;
        public string Actor;                    // $questioner / $player / 角色ID
        public Dictionary<string, string> Args = new Dictionary<string, string>(StringComparer.Ordinal);
        public StoryStepWait Wait;
        public string OnFail, OnTimeout, OnReject, OnCancel, OnInterrupt;
    }

    public sealed class StoryFlowDef
    {
        public string Id;
        public Dictionary<string, string> Roles = new Dictionary<string, string>(StringComparer.Ordinal);
        public List<StoryFlowStep> Steps = new List<StoryFlowStep>();
    }

    public sealed class StoryBeatDef
    {
        public float At;
        public string Cmd;
        public string Id, Case, Actor, Action, TextKey, Listener, Prefab, Anchor, Note;
        public float Fwd, Right, Up, Yaw, Scale = 1f, Facing = -1f, Seconds;
        public bool Async, Civilian = true;
    }

    public sealed class StoryLayoutFile
    {
        public string Schema;
        public int ContentVersion;
        public string Scene;
        public string WallLevel;
        public Dictionary<string, StoryLayoutDef> Layouts = new Dictionary<string, StoryLayoutDef>(StringComparer.Ordinal);
        public Dictionary<string, StoryStageDef> Stages = new Dictionary<string, StoryStageDef>(StringComparer.Ordinal);
        public Dictionary<string, StoryBehaviorDef> Behaviors = new Dictionary<string, StoryBehaviorDef>(StringComparer.Ordinal);
        public Dictionary<string, StoryFlowDef> Flows = new Dictionary<string, StoryFlowDef>(StringComparer.Ordinal);
        public List<StoryBeatDef> Beats = new List<StoryBeatDef>();
    }

    /// <summary>解算结果：一个对象在它的锚点基下的分量（+Fwd 朝锚点前方，+Right 右手侧，+Up 竖直）。</summary>
    public sealed class StorySolvedPoint
    {
        public string Kind;      // prop / work / post
        public string Id;
        public string Anchor;
        public float Fwd, Right, Up;
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1}@{2} f={3:F2} r={4:F2} u={5:F2}",
                Kind, Id, Anchor, Fwd, Right, Up);
        }
    }

    public sealed class StoryValidationResult
    {
        public bool Ok;
        public StoryLayoutFile Data;
        public List<StoryValidationIssue> Issues = new List<StoryValidationIssue>();
        public List<StorySolvedPoint> Solved = new List<StorySolvedPoint>();
        public string Source;

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append(Ok ? "OK: " : "FAILED: ").Append(Source ?? "-");
            if (Data != null)
            {
                sb.Append(" schema=").Append(Data.Schema)
                  .Append(" contentVersion=").Append(Data.ContentVersion)
                  .Append(" scene=").Append(Data.Scene)
                  .Append(" layouts=").Append(Data.Layouts.Count)
                  .Append(" stages=").Append(Data.Stages.Count)
                  .Append(" behaviors=").Append(Data.Behaviors.Count)
                  .Append(" flows=").Append(Data.Flows.Count)
                  .Append(" beats=").Append(Data.Beats.Count);
            }
            sb.Append(" issues=").Append(Issues.Count).Append(" solved=").Append(Solved.Count);
            foreach (var issue in Issues.Take(20))
                sb.Append('\n').Append("  ").Append(issue.Path).Append(" [").Append(issue.Code).Append(']');
            if (Issues.Count > 20) sb.Append("\n  ... +").Append(Issues.Count - 20).Append(" more");
            foreach (var p in Solved.Take(12)) sb.Append('\n').Append("  ").Append(p);
            return sb.ToString();
        }
    }

    // ───────────────────────────── 解析与校验 ─────────────────────────────

    /// <summary>
    /// v2 数据解析器：**先严格校验再给模型**。
    /// 🔴 失败时 Data 为 null，绝不返回半份数据；调用方保留上一有效版本。
    /// </summary>
    public static class StoryLayoutParser
    {
        public const string RequiredSchema = "story_stage/2";

        /// <summary>读选角表里的角色 ID 集合（只取 Id，用于「名单外对象」校验）。</summary>
        public static List<string> LoadCastIds(string castJsonPath)
        {
            var ids = new List<string>();
            if (string.IsNullOrEmpty(castJsonPath) || !File.Exists(castJsonPath)) return ids;
            try
            {
                var root = JObject.Parse(File.ReadAllText(castJsonPath));
                if (root["Npcs"] is JArray npcs)
                    foreach (var n in npcs)
                    {
                        string id = n["Id"]?.ToString();
                        if (!string.IsNullOrEmpty(id) && !ids.Contains(id)) ids.Add(id);
                    }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[StoryLayout] cast read failed " + castJsonPath + ": " + ex.Message);
            }
            return ids;
        }

        public static StoryValidationResult Parse(string json, string source,
            IEnumerable<string> castIds, StoryValidationMode mode)
        {
            var result = new StoryValidationResult { Source = source };
            if (string.IsNullOrEmpty(json))
            {
                result.Issues.Add(new StoryValidationIssue(source, "empty_input", "文件为空或读不到"));
                return result;
            }

            JObject root;
            try { root = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }); }
            catch (Exception ex)
            {
                result.Issues.Add(new StoryValidationIssue(source, "json_parse", "JSON 解析失败：" + ex.Message));
                return result;
            }

            var errors = result.Issues;
            var data = new StoryLayoutFile();
            result.Data = data;   // 仅供 Describe 展示已读字段；Ok 为 false 时调用方不得使用

            // ── 根 ──
            // 只允许 _ 开头的注释键出现在根层（子层出现未知键一律拒绝）。
            foreach (var p in root.Properties())
                if (!p.Name.StartsWith("_", StringComparison.Ordinal) && !RootKeys.Contains(p.Name))
                    errors.Add(new StoryValidationIssue("$." + p.Name, "unknown_key",
                        "根层未知字段 '" + p.Name + "'（允许：" + string.Join(", ", RootKeys) + "；注释键须以 _ 开头）"));

            data.Schema = Str(root, "Schema", "$", errors, true);
            if (!string.IsNullOrEmpty(data.Schema) && data.Schema != RequiredSchema)
                errors.Add(new StoryValidationIssue("$.Schema", "schema_mismatch",
                    "结构版本必须是 '" + RequiredSchema + "'，实际 '" + data.Schema + "'"));

            data.ContentVersion = Int(root, "ContentVersion", "$", errors, 1, 1, int.MaxValue);
            if (root.Property("ContentVersion") == null)
                errors.Add(new StoryValidationIssue("$.ContentVersion", "missing_field", "缺少必需整数 'ContentVersion'"));
            data.Scene = Str(root, "Scene", "$", errors, true);
            data.WallLevel = Str(root, "WallLevel", "$", errors, true);

            var cast = new HashSet<string>(castIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

            ParseLayouts(root, data, errors);
            ParseStages(root, data, errors, cast);
            ParseFlows(root, data, errors, mode, cast);
            ParseBehaviors(root, data, errors);
            ParseBeats(root, data, errors, mode, cast);

            // 行为规则引用的岗位必须在它声明的每个阶段布局里存在
            foreach (var b in data.Behaviors.Values)
            {
                foreach (string stageId in b.Stages)
                {
                    if (!data.Stages.TryGetValue(stageId, out var stage)) continue;
                    if (!data.Layouts.TryGetValue(stage.Layout ?? "", out var layout)) continue;
                    foreach (string postId in b.Posts)
                        if (layout.Posts.All(p => p.Id != postId))
                            errors.Add(new StoryValidationIssue("$.Behaviors." + b.Id + ".Posts", "unknown_post",
                                "岗位 '" + postId + "' 不在阶段 '" + stageId + "' 的布局 '" + layout.Id + "' 里"));
                }
            }

            // 解算（只在无错时给出，避免用坏数据算出一堆假坐标）
            if (errors.Count == 0) Solve(data, result);

            result.Ok = errors.Count == 0;
            return result;
        }

        private static readonly string[] RootKeys =
            { "Schema", "ContentVersion", "Scene", "WallLevel", "Layouts", "Stages", "Behaviors", "Flows", "Beats" };

        // ── 布局 ──
        private static void ParseLayouts(JObject root, StoryLayoutFile data, List<StoryValidationIssue> errors)
        {
            var layouts = Obj(root, "Layouts", "$", errors, true);
            if (layouts == null) return;
            if (layouts.Count == 0) errors.Add(new StoryValidationIssue("$.Layouts", "empty_collection", "至少要有一份布局"));

            foreach (var lp in layouts.Properties())
            {
                string path = "$.Layouts." + lp.Name;
                if (!(lp.Value is JObject lo)) { errors.Add(new StoryValidationIssue(path, "wrong_type", "布局必须是对象")); continue; }
                var layout = new StoryLayoutDef { Id = lp.Name };
                Keys(lo, path, new[] { "Version", "Anchors", "Gates", "Props", "WorkPoints", "Posts", "Standing" }, errors);
                layout.Version = Int(lo, "Version", path, errors, 1, 1, int.MaxValue);

                var anchors = Obj(lo, "Anchors", path, errors, true);
                if (anchors != null)
                {
                    if (anchors.Count == 0) errors.Add(new StoryValidationIssue(path + ".Anchors", "empty_collection", "至少要有一个锚点"));
                    foreach (var ap in anchors.Properties())
                    {
                        string apath = path + ".Anchors." + ap.Name;
                        if (!(ap.Value is JObject ao)) { errors.Add(new StoryValidationIssue(apath, "wrong_type", "锚点必须是对象")); continue; }
                        Keys(ao, apath, new[] { "Source", "Value", "Pick", "Required" }, errors);
                        var a = new StoryAnchorDef();
                        a.Source = Str(ao, "Source", apath, errors, true) ?? "tag";
                        if (a.Source != "tag" && a.Source != "entity" && a.Source != "player")
                            errors.Add(new StoryValidationIssue(apath + ".Source", "bad_token", "Source 只能是 tag / entity / player，实际 '" + a.Source + "'"));
                        a.Value = StrArray(ao, "Value", apath, errors, a.Source != "player");
                        if (a.Source == "player" && a.Value.Length > 0)
                            errors.Add(new StoryValidationIssue(apath + ".Value", "bad_field", "Source=player 时不允许写 Value"));
                        a.Pick = Str(ao, "Pick", apath, errors, false) ?? "first";
                        if (a.Pick != "first" && a.Pick != "nearestToPlayer")
                            errors.Add(new StoryValidationIssue(apath + ".Pick", "bad_token", "Pick 只能是 first / nearestToPlayer，实际 '" + a.Pick + "'"));
                        a.Required = Bool(ao, "Required", apath, errors, true);
                        layout.Anchors[ap.Name] = a;
                    }
                }

                var gates = Obj(lo, "Gates", path, errors, false);
                if (gates != null)
                {
                    foreach (var gp in gates.Properties())
                    {
                        string gpath = path + ".Gates." + gp.Name;
                        if (!(gp.Value is JObject go)) { errors.Add(new StoryValidationIssue(gpath, "wrong_type", "门区必须是对象")); continue; }
                        Keys(go, gpath, new[] { "Center", "Outside", "Inner", "HalfWidth", "InsideMeters", "OutsideMeters" }, errors);
                        var g = new StoryGateDef
                        {
                            Center = Point(go, "Center", gpath, errors, layout, true),
                            Outside = Point(go, "Outside", gpath, errors, layout, true),
                            Inner = Point(go, "Inner", gpath, errors, layout, true),
                            HalfWidth = Float(go, "HalfWidth", gpath, errors, 3f, 0.01f, 1000f),
                            InsideMeters = Float(go, "InsideMeters", gpath, errors, 8f, 0f, 10000f),
                            OutsideMeters = Float(go, "OutsideMeters", gpath, errors, 3f, 0f, 10000f)
                        };
                        layout.Gates[gp.Name] = g;
                    }
                }

                var props = Arr(lo, "Props", path, errors);
                if (props != null)
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < props.Count; i++)
                    {
                        string ppath = path + ".Props[" + i + "]";
                        if (!(props[i] is JObject po)) { errors.Add(new StoryValidationIssue(ppath, "wrong_type", "道具必须是对象")); continue; }
                        Keys(po, ppath, new[] { "Id", "Source", "Prefab", "EntityTag", "Anchor", "Fwd", "Right", "Up", "Yaw", "Scale", "Support", "Footprint", "Required" }, errors);
                        var p = new StoryPropDef { Id = Str(po, "Id", ppath, errors, true) };
                        if (string.IsNullOrEmpty(p.Id)) continue;
                        if (!seen.Add(p.Id)) errors.Add(new StoryValidationIssue(ppath + ".Id", "duplicate_id", "道具 ID 重复 '" + p.Id + "'"));
                        p.Source = Str(po, "Source", ppath, errors, true) ?? "prefab";
                        if (p.Source == "prefab")
                        {
                            p.Prefab = Str(po, "Prefab", ppath, errors, true);
                            p.Place = Point(po, null, ppath, errors, layout, true);
                            p.Yaw = Float(po, "Yaw", ppath, errors, 0f, -3600f, 3600f);
                            p.Scale = Float(po, "Scale", ppath, errors, 1f, 0.01f, 100f);
                            if (po.Property("EntityTag") != null)
                                errors.Add(new StoryValidationIssue(ppath + ".EntityTag", "bad_field", "Source=prefab 时不允许写 EntityTag"));
                        }
                        else if (p.Source == "existing")
                        {
                            p.EntityTag = StrArray(po, "EntityTag", ppath, errors, true);
                            if (po.Property("Anchor") != null || po.Property("Fwd") != null || po.Property("Right") != null
                                || po.Property("Up") != null || po.Property("Yaw") != null || po.Property("Scale") != null)
                                errors.Add(new StoryValidationIssue(ppath, "bad_field", "Source=existing 时不允许写位置/朝向/缩放（按场景实际变换登记）"));
                        }
                        else
                        {
                            errors.Add(new StoryValidationIssue(ppath + ".Source", "bad_token", "Source 只能是 prefab / existing，实际 '" + p.Source + "'"));
                        }

                        p.Support = Str(po, "Support", ppath, errors, true) ?? "ground";
                        if (p.Support != "ground" && p.Support != "fixed-up")
                            errors.Add(new StoryValidationIssue(ppath + ".Support", "bad_token", "Support 只能是 ground / fixed-up，实际 '" + p.Support + "'"));
                        p.Footprint = FloatArray(po, "Footprint", ppath, errors, 2, true);
                        if (p.Footprint.Length == 2 && (p.Footprint[0] <= 0f || p.Footprint[1] <= 0f))
                            errors.Add(new StoryValidationIssue(ppath + ".Footprint", "bad_range", "占地两个分量都必须 > 0"));
                        p.Required = Bool(po, "Required", ppath, errors, true);
                        layout.Props.Add(p);
                    }
                }

                var works = Arr(lo, "WorkPoints", path, errors);
                if (works != null)
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < works.Count; i++)
                    {
                        string wpath = path + ".WorkPoints[" + i + "]";
                        if (!(works[i] is JObject wo)) { errors.Add(new StoryValidationIssue(wpath, "wrong_type", "工作点必须是对象")); continue; }
                        Keys(wo, wpath, new[] { "Id", "Prop", "Side", "Offset", "Contact", "Yaw", "ArriveTolerance", "MaxOccupants", "Action" }, errors);
                        var w = new StoryWorkPointDef { Id = Str(wo, "Id", wpath, errors, true) };
                        if (string.IsNullOrEmpty(w.Id)) continue;
                        if (!seen.Add(w.Id)) errors.Add(new StoryValidationIssue(wpath + ".Id", "duplicate_id", "工作点 ID 重复 '" + w.Id + "'"));
                        w.Prop = Str(wo, "Prop", wpath, errors, true);
                        if (!string.IsNullOrEmpty(w.Prop) && layout.FindProp(w.Prop) == null)
                            errors.Add(new StoryValidationIssue(wpath + ".Prop", "unknown_prop", "工作点引用的道具 '" + w.Prop + "' 不在同一布局里"));
                        w.Side = Str(wo, "Side", wpath, errors, true) ?? "front";
                        if (w.Side != "front" && w.Side != "back" && w.Side != "left" && w.Side != "right")
                            errors.Add(new StoryValidationIssue(wpath + ".Side", "bad_token", "Side 只能是 front / back / left / right，实际 '" + w.Side + "'"));
                        w.Offset = Float(wo, "Offset", wpath, errors, 0f, 0f, 100f);
                        w.Contact = FloatArray(wo, "Contact", wpath, errors, 3, true);
                        w.Yaw = Float(wo, "Yaw", wpath, errors, 0f, -3600f, 3600f);
                        w.ArriveTolerance = Float(wo, "ArriveTolerance", wpath, errors, 0.6f, 0.01f, 20f);
                        w.MaxOccupants = Int(wo, "MaxOccupants", wpath, errors, 1, 1, 8);
                        w.Action = Str(wo, "Action", wpath, errors, true);
                        layout.WorkPoints.Add(w);
                    }
                }

                var posts = Arr(lo, "Posts", path, errors);
                if (posts != null)
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < posts.Count; i++)
                    {
                        string ppath = path + ".Posts[" + i + "]";
                        if (!(posts[i] is JObject po)) { errors.Add(new StoryValidationIssue(ppath, "wrong_type", "岗位必须是对象")); continue; }
                        Keys(po, ppath, new[] { "Id", "Gate", "Anchor", "Fwd", "Right", "Up", "Facing" }, errors);
                        var p = new StoryPostDef { Id = Str(po, "Id", ppath, errors, true) };
                        if (string.IsNullOrEmpty(p.Id)) continue;
                        if (!seen.Add(p.Id)) errors.Add(new StoryValidationIssue(ppath + ".Id", "duplicate_id", "岗位 ID 重复 '" + p.Id + "'"));
                        p.Gate = Str(po, "Gate", ppath, errors, true);
                        if (!string.IsNullOrEmpty(p.Gate) && !layout.Gates.ContainsKey(p.Gate))
                            errors.Add(new StoryValidationIssue(ppath + ".Gate", "unknown_gate", "岗位引用的门区 '" + p.Gate + "' 不在同一布局里"));
                        p.Place = Point(po, null, ppath, errors, layout, true);
                        p.Facing = Float(po, "Facing", ppath, errors, -1f, -3600f, 3600f);
                        layout.Posts.Add(p);
                    }
                }

                var standing = Arr(lo, "Standing", path, errors);
                if (standing != null)
                {
                    for (int i = 0; i < standing.Count; i++)
                    {
                        string spath = path + ".Standing[" + i + "]";
                        if (!(standing[i] is JObject so)) { errors.Add(new StoryValidationIssue(spath, "wrong_type", "站位必须是对象")); continue; }
                        Keys(so, spath, new[] { "Actor", "Post", "WorkPoint", "Anchor", "Fwd", "Right", "Up", "Facing" }, errors);
                        var s = new StoryStandingDef { Actor = Str(so, "Actor", spath, errors, true) };
                        s.Post = Str(so, "Post", spath, errors, false);
                        s.WorkPoint = Str(so, "WorkPoint", spath, errors, false);
                        int sources = (string.IsNullOrEmpty(s.Post) ? 0 : 1) + (string.IsNullOrEmpty(s.WorkPoint) ? 0 : 1) + (so.Property("Anchor") != null ? 1 : 0);
                        if (sources != 1)
                            errors.Add(new StoryValidationIssue(spath, "bad_field", "站位必须在 Post / WorkPoint / Anchor 里三选一（当前 " + sources + " 个）"));
                        if (!string.IsNullOrEmpty(s.Post) && layout.Posts.All(p => p.Id != s.Post))
                            errors.Add(new StoryValidationIssue(spath + ".Post", "unknown_post", "站位引用的岗位 '" + s.Post + "' 不存在"));
                        if (!string.IsNullOrEmpty(s.WorkPoint) && layout.WorkPoints.All(w => w.Id != s.WorkPoint))
                            errors.Add(new StoryValidationIssue(spath + ".WorkPoint", "unknown_workpoint", "站位引用的工作点 '" + s.WorkPoint + "' 不存在"));
                        if (so.Property("Anchor") != null) s.Place = Point(so, null, spath, errors, layout, true);
                        s.Facing = Float(so, "Facing", spath, errors, -1f, -3600f, 3600f);
                        layout.Standing.Add(s);
                    }
                }

                data.Layouts[lp.Name] = layout;
            }
        }

        // ── 阶段 ──
        private static void ParseStages(JObject root, StoryLayoutFile data, List<StoryValidationIssue> errors, HashSet<string> cast)
        {
            var stages = Obj(root, "Stages", "$", errors, true);
            if (stages == null) return;
            if (stages.Count == 0) errors.Add(new StoryValidationIssue("$.Stages", "empty_collection", "至少要有一个阶段"));
            int opening = 0;

            foreach (var sp in stages.Properties())
            {
                string path = "$.Stages." + sp.Name;
                if (!(sp.Value is JObject so)) { errors.Add(new StoryValidationIssue(path, "wrong_type", "阶段必须是对象")); continue; }
                Keys(so, path, new[] { "Layout", "Actors", "Props", "OptionalActors", "OptionalProps", "Opening", "Prep" }, errors);
                var stage = new StoryStageDef { Id = sp.Name };
                stage.Layout = Str(so, "Layout", path, errors, true);
                if (!string.IsNullOrEmpty(stage.Layout) && !data.Layouts.ContainsKey(stage.Layout))
                    errors.Add(new StoryValidationIssue(path + ".Layout", "unknown_layout", "阶段引用的布局 '" + stage.Layout + "' 不存在"));

                stage.Actors = StrList(so, "Actors", path, errors, true);
                stage.Props = StrList(so, "Props", path, errors, false);
                stage.OptionalActors = StrList(so, "OptionalActors", path, errors, false);
                stage.OptionalProps = StrList(so, "OptionalProps", path, errors, false);
                stage.Opening = Bool(so, "Opening", path, errors, false);
                if (stage.Opening) opening++;

                var prep = Obj(so, "Prep", path, errors, false);
                if (prep != null)
                {
                    Keys(prep, path + ".Prep", new[] { "SkipAllowed", "ActorReadyTimeoutSec" }, errors);
                    stage.Prep.SkipAllowed = Bool(prep, "SkipAllowed", path + ".Prep", errors, true);
                    stage.Prep.ActorReadyTimeoutSec = Float(prep, "ActorReadyTimeoutSec", path + ".Prep", errors, 3f, 0.1f, 600f);
                }

                // 名单内去重 + 与选角表对照
                DupCheck(stage.Actors, path + ".Actors", "演员", errors);
                DupCheck(stage.Props, path + ".Props", "道具", errors);
                foreach (string actor in stage.Actors.Concat(stage.OptionalActors))
                {
                    if (string.IsNullOrEmpty(actor)) continue;
                    if (cast.Count > 0 && !cast.Contains(actor))
                        errors.Add(new StoryValidationIssue(path, "unknown_cast_actor", "演员 '" + actor + "' 不在选角表里"));
                }

                if (!string.IsNullOrEmpty(stage.Layout) && data.Layouts.TryGetValue(stage.Layout, out var layout))
                {
                    foreach (string propId in stage.Props.Concat(stage.OptionalProps))
                        if (!string.IsNullOrEmpty(propId) && layout.FindProp(propId) == null)
                            errors.Add(new StoryValidationIssue(path, "unknown_prop", "道具 '" + propId + "' 不在布局 '" + layout.Id + "' 里"));
                }

                data.Stages[sp.Name] = stage;
            }

            if (opening > 1)
                errors.Add(new StoryValidationIssue("$.Stages", "multiple_opening", "只能有一个阶段声明 Opening=true（当前 " + opening + "）"));
        }

        // ── 流程 ──
        private static void ParseFlows(JObject root, StoryLayoutFile data, List<StoryValidationIssue> errors, StoryValidationMode mode, HashSet<string> cast)
        {
            var flows = Obj(root, "Flows", "$", errors, false);
            if (flows == null) return;

            foreach (var fp in flows.Properties())
            {
                string path = "$.Flows." + fp.Name;
                if (!(fp.Value is JObject fo)) { errors.Add(new StoryValidationIssue(path, "wrong_type", "流程必须是对象")); continue; }
                Keys(fo, path, new[] { "Roles", "Steps" }, errors);
                var flow = new StoryFlowDef { Id = fp.Name };

                var roles = Obj(fo, "Roles", path, errors, false);
                if (roles != null)
                    foreach (var rp in roles.Properties())
                    {
                        string binding = Str(roles, rp.Name, path + ".Roles", errors, true);
                        if (!string.IsNullOrWhiteSpace(binding))
                        {
                            if (binding != "candidate" && binding != "player" && !cast.Contains(binding))
                                errors.Add(new StoryValidationIssue(path + ".Roles." + rp.Name, "unknown_cast_actor", "角色绑定必须是 candidate、player 或选角表 ID"));
                            flow.Roles[rp.Name] = binding;
                        }
                    }

                var steps = Arr(fo, "Steps", path, errors);
                if (steps == null || steps.Count == 0)
                {
                    errors.Add(new StoryValidationIssue(path + ".Steps", "empty_collection", "流程至少要有一个步骤"));
                    data.Flows[fp.Name] = flow;
                    continue;
                }

                var ids = new List<string>();
                for (int i = 0; i < steps.Count; i++)
                {
                    string stepPath = path + ".Steps[" + i + "]";
                    if (!(steps[i] is JObject so)) { errors.Add(new StoryValidationIssue(stepPath, "wrong_type", "步骤必须是对象")); continue; }
                    Keys(so, stepPath, new[] { "Id", "Atom", "Actor", "Args", "Wait", "OnFail", "OnTimeout", "OnReject", "OnCancel", "OnInterrupt" }, errors);
                    var step = new StoryFlowStep { Id = Str(so, "Id", stepPath, errors, true) };
                    step.Atom = Str(so, "Atom", stepPath, errors, true);
                    step.Actor = Str(so, "Actor", stepPath, errors, false);
                    if (!string.IsNullOrEmpty(step.Actor))
                        ValidateFlowActor(step.Actor, stepPath + ".Actor", flow, cast, errors);

                    StoryAtomSpec spec = null;
                    if (!string.IsNullOrEmpty(step.Atom) && !StoryAtomRegistry.TryGet(step.Atom, out spec))
                        errors.Add(new StoryValidationIssue(stepPath + ".Atom", "unknown_atom", "未注册的原子 '" + step.Atom + "'"));
                    else if (spec != null)
                    {
                        if (spec.ActorRole == "required" && string.IsNullOrEmpty(step.Actor))
                            errors.Add(new StoryValidationIssue(stepPath + ".Actor", "missing_field", "原子 '" + spec.Name + "' 需要演员绑定"));
                        if (spec.ActorRole != "required" && !string.IsNullOrEmpty(step.Actor))
                            errors.Add(new StoryValidationIssue(stepPath + ".Actor", "bad_field", "原子 '" + spec.Name + "' 不接受演员绑定"));
                        if (!spec.Implemented && mode == StoryValidationMode.Formal)
                            errors.Add(new StoryValidationIssue(stepPath + ".Atom", "unimplemented_atom",
                                "原子 '" + spec.Name + "' 尚未实现，不能进正式数据（" + spec.Notes + "）"));
                    }

                    var args = Obj(so, "Args", stepPath, errors, false);
                    if (args != null)
                    {
                        foreach (var ap in args.Properties())
                        {
                            if (spec != null && !spec.Params.Contains(ap.Name))
                                errors.Add(new StoryValidationIssue(stepPath + ".Args." + ap.Name, "unknown_param",
                                    "原子 '" + spec.Name + "' 不接受参数 '" + ap.Name + "'（允许：" + string.Join(", ", spec.Params) + "）"));
                            else if (spec != null)
                            {
                                string argPath = stepPath + ".Args." + ap.Name;
                                if (spec.NumberParams.Contains(ap.Name))
                                {
                                    float v = Float(args, ap.Name, stepPath + ".Args", errors, 0f, -100000f, 100000f);
                                    if ((ap.Name == "Seconds" && v <= 0f) || (ap.Name == "StopMeters" && v < 0f))
                                        errors.Add(new StoryValidationIssue(argPath, "bad_range", "等待秒数须 >0；停止距离须 >=0"));
                                }
                                else if (spec.BooleanParams.Contains(ap.Name))
                                    Bool(args, ap.Name, stepPath + ".Args", errors, false);
                                else
                                    Str(args, ap.Name, stepPath + ".Args", errors, true);
                                if (ap.Name == "To" && ap.Value.Type == JTokenType.String && ap.Value.ToString().StartsWith("$", StringComparison.Ordinal))
                                    ValidateFlowActor(ap.Value.ToString(), argPath, flow, cast, errors);
                                if (spec.Name == "actor_say" && ap.Name == "Listener" && ap.Value.Type == JTokenType.String)
                                {
                                    string listener = ap.Value.ToString();
                                    if (listener != "player") ValidateFlowActor(listener, argPath, flow, cast, errors);
                                    if (listener == step.Actor)
                                        errors.Add(new StoryValidationIssue(argPath, "unknown_listener", "听话者不能是说话者自己"));
                                }
                                if (ap.Value.Type == JTokenType.Null)
                                    errors.Add(new StoryValidationIssue(argPath, "wrong_type", "原子参数不能是 null"));
                            }
                            step.Args[ap.Name] = ap.Value?.ToString() ?? "";
                        }
                        if (spec != null)
                            foreach (string req in spec.RequiredParams)
                                if (!step.Args.ContainsKey(req))
                                    errors.Add(new StoryValidationIssue(stepPath + ".Args", "missing_field", "缺少必需参数 '" + req + "'"));
                    }
                    else if (spec != null && spec.RequiredParams.Length > 0)
                    {
                        errors.Add(new StoryValidationIssue(stepPath + ".Args", "missing_field", "缺少 Args（必需参数：" + string.Join(", ", spec.RequiredParams) + "）"));
                    }

                    var wait = Obj(so, "Wait", stepPath, errors, false);
                    if (wait != null)
                    {
                        Keys(wait, stepPath + ".Wait", new[] { "Kind", "TimeoutSec" }, errors);
                        step.Wait = new StoryStepWait
                        {
                            Kind = Str(wait, "Kind", stepPath + ".Wait", errors, true),
                            TimeoutSec = Float(wait, "TimeoutSec", stepPath + ".Wait", errors, 0f, 0f, 100000f)
                        };
                        if (spec != null && spec.WaitKinds.Length == 0)
                            errors.Add(new StoryValidationIssue(stepPath + ".Wait", "bad_field", "原子 '" + spec.Name + "' 不接受 Wait"));
                        else if (spec != null && !string.IsNullOrEmpty(step.Wait.Kind) && !spec.WaitKinds.Contains(step.Wait.Kind))
                            errors.Add(new StoryValidationIssue(stepPath + ".Wait.Kind", "bad_token",
                                "原子 '" + spec.Name + "' 只接受 wait.Kind=" + string.Join("/", spec.WaitKinds) + "，实际 '" + step.Wait.Kind + "'"));
                        // 🔴 每个等待必须写超时上限（无出口等待一律拒绝）
                        if (step.Wait.TimeoutSec <= 0f)
                            errors.Add(new StoryValidationIssue(stepPath + ".Wait.TimeoutSec", "wait_no_timeout",
                                "等待必须写 TimeoutSec > 0（无出口等待不允许进数据）"));
                    }
                    else if (spec != null && spec.WaitKinds.Length > 0)
                    {
                        errors.Add(new StoryValidationIssue(stepPath + ".Wait", "missing_field",
                            "原子 '" + spec.Name + "' 声明了等待语义，步骤必须写 Wait（含 TimeoutSec）"));
                    }

                    step.OnFail = Str(so, "OnFail", stepPath, errors, false);
                    step.OnTimeout = Str(so, "OnTimeout", stepPath, errors, false);
                    step.OnReject = Str(so, "OnReject", stepPath, errors, false);
                    step.OnCancel = Str(so, "OnCancel", stepPath, errors, false);
                    step.OnInterrupt = Str(so, "OnInterrupt", stepPath, errors, false);

                    if (!string.IsNullOrEmpty(step.Id) && ids.Contains(step.Id))
                        errors.Add(new StoryValidationIssue(stepPath + ".Id", "duplicate_id", "流程步骤 ID 重复：'" + step.Id + "'"));
                    if (TerminalTokens.Contains(step.Id) || step.Id == "next")
                        errors.Add(new StoryValidationIssue(stepPath + ".Id", "bad_value", "步骤 ID 不能与分支保留词重名"));
                    ids.Add(step.Id);
                    flow.Steps.Add(step);
                }

                if (flow.Steps.Any(s => string.IsNullOrEmpty(s.OnCancel)))
                    errors.Add(new StoryValidationIssue(path, "missing_branch", "每个步骤都必须写 OnCancel（取消后去哪）"));

                // 分支目标：终态令牌、后续步骤 ID、或 next；禁止回跳（P0 不支持循环，避免即时无限递归）
                for (int i = 0; i < flow.Steps.Count; i++)
                {
                    foreach (var branch in BranchTargets(flow.Steps[i]))
                    {
                        if (string.IsNullOrEmpty(branch.Value)) continue;
                        string target = branch.Value;
                        if (TerminalTokens.Contains(target) || target == "next") continue;
                        int idx = ids.IndexOf(target);
                        if (idx < 0)
                            errors.Add(new StoryValidationIssue(path + ".Steps[" + i + "]." + branch.Name, "unknown_branch",
                                "'" + target + "' 既不是终态（" + string.Join("/", TerminalTokens) + "）也不是本流程的步骤 ID"));
                        else if (idx <= i)
                            errors.Add(new StoryValidationIssue(path + ".Steps[" + i + "]." + branch.Name, "backward_branch",
                                "P0 只支持前向跳转，'" + target + "' 不在当前步骤之后（循环结构未注册）"));
                    }
                }

                data.Flows[fp.Name] = flow;
            }
        }

        private static readonly string[] TerminalTokens = { "release", "retry", "reselect", "end" };

        private static void ValidateFlowActor(string actor, string path, StoryFlowDef flow, HashSet<string> cast, List<StoryValidationIssue> errors)
        {
            if (actor.StartsWith("$", StringComparison.Ordinal))
            {
                if (!flow.Roles.ContainsKey(actor.Substring(1)))
                    errors.Add(new StoryValidationIssue(path, "unknown_role", "未定义流程角色 '" + actor + "'"));
            }
            else if (!cast.Contains(actor))
                errors.Add(new StoryValidationIssue(path, "unknown_cast_actor", "演员 '" + actor + "' 不在选角表"));
        }

        private struct BranchRef
        {
            public string Name;
            public string Value;
            public BranchRef(string name, string value) { Name = name; Value = value; }
        }

        private static IEnumerable<BranchRef> BranchTargets(StoryFlowStep s)
        {
            yield return new BranchRef("OnFail", s.OnFail);
            yield return new BranchRef("OnTimeout", s.OnTimeout);
            yield return new BranchRef("OnReject", s.OnReject);
            yield return new BranchRef("OnCancel", s.OnCancel);
            yield return new BranchRef("OnInterrupt", s.OnInterrupt);
        }

        // ── 行为规则 ──
        private static void ParseBehaviors(JObject root, StoryLayoutFile data, List<StoryValidationIssue> errors)
        {
            var behaviors = Obj(root, "Behaviors", "$", errors, false);
            if (behaviors == null) return;

            foreach (var bp in behaviors.Properties())
            {
                string path = "$.Behaviors." + bp.Name;
                if (!(bp.Value is JObject bo)) { errors.Add(new StoryValidationIssue(path, "wrong_type", "行为规则必须是对象")); continue; }
                Keys(bo, path, new[] { "Kind", "Posts", "Stages", "Perception", "Trigger", "Cooldown", "Rank", "Flow", "MaxActive",
                    "Members", "Host", "Lines", "Speakers", "Listeners", "LineFallbacks", "ActionsA", "ActionsB", "JoinNode" }, errors);
                var b = new StoryBehaviorDef { Id = bp.Name };
                b.Kind = Str(bo, "Kind", path, errors, true) ?? "post";
                if (b.Kind != "post" && b.Kind != "group")
                    errors.Add(new StoryValidationIssue(path + ".Kind", "bad_token", "Kind 只能是 post/group，实际 '" + b.Kind + "'"));

                b.Stages = StrList(bo, "Stages", path, errors, true);
                if (b.Stages.Count == 0) errors.Add(new StoryValidationIssue(path + ".Stages", "empty_collection", "行为规则至少要绑定一个阶段"));
                foreach (string stageId in b.Stages)
                    if (!data.Stages.ContainsKey(stageId))
                        errors.Add(new StoryValidationIssue(path + ".Stages", "unknown_stage", "阶段 '" + stageId + "' 不存在"));

                if (b.Kind == "post")
                {
                    b.Posts = StrList(bo, "Posts", path, errors, true);
                    if (b.Posts.Count == 0) errors.Add(new StoryValidationIssue(path + ".Posts", "empty_collection", "岗位规则至少要绑定一个岗位"));
                }
                else
                {
                    b.Members = StrList(bo, "Members", path, errors, true);
                    b.Host = Str(bo, "Host", path, errors, true);
                    b.Lines = StrList(bo, "Lines", path, errors, false);
                    b.Speakers = StrList(bo, "Speakers", path, errors, false);
                    b.Listeners = StrList(bo, "Listeners", path, errors, b.Lines.Count > 0);
                    if (b.Listeners.Count != b.Lines.Count)
                        errors.Add(new StoryValidationIssue(path + ".Listeners", "line_count", "Listeners 必须与 Lines 逐句对应"));
                    for (int i = 0; i < b.Listeners.Count; i++)
                    {
                        string listener = b.Listeners[i];
                        string speaker = i < b.Speakers.Count ? b.Speakers[i]
                            : b.Members.Count > 0 ? b.Members[i % b.Members.Count] : null;
                        if ((listener != "player" && !b.Members.Contains(listener)) || listener == speaker)
                            errors.Add(new StoryValidationIssue(path + ".Listeners[" + i + "]", "unknown_listener",
                                "听话者必须为 player 或另一位群组成员"));
                    }
                    foreach (string sp in b.Speakers)
                        if (!b.Members.Contains(sp))
                            errors.Add(new StoryValidationIssue(path + ".Speakers", "bad_value",
                                "说话者 '" + sp + "' 不在 Members 里"));
                    b.LineFallbacks = StrList(bo, "LineFallbacks", path, errors, false);
                    b.ActionsA = StrList(bo, "ActionsA", path, errors, false);
                    b.ActionsB = StrList(bo, "ActionsB", path, errors, false);
                    b.JoinNode = Str(bo, "JoinNode", path, errors, false);
                    if (b.Members.Count == 0)
                        errors.Add(new StoryValidationIssue(path + ".Members", "empty_collection", "群组至少要有 1 名成员"));
                    if (!string.IsNullOrEmpty(b.Host) && !b.Members.Contains(b.Host))
                        errors.Add(new StoryValidationIssue(path + ".Host", "bad_value", "Host 必须在 Members 里"));
                }

                var perception = Obj(bo, "Perception", path, errors, b.Kind == "post");
                if (perception != null)
                {
                    Keys(perception, path + ".Perception", new[] { "RequiresSight", "RangeMeters" }, errors);
                    b.Perception.RequiresSight = Bool(perception, "RequiresSight", path + ".Perception", errors, true);
                    b.Perception.RangeMeters = Float(perception, "RangeMeters", path + ".Perception", errors, 12f, 0.1f, 500f);
                }

                var trigger = Obj(bo, "Trigger", path, errors, b.Kind == "post");
                if (trigger != null)
                {
                    Keys(trigger, path + ".Trigger", new[] { "PlayerInsideGate", "PlayerInPostZone", "MaxPerEncounter" }, errors);
                    b.Trigger.PlayerInsideGate = Bool(trigger, "PlayerInsideGate", path + ".Trigger", errors, true);
                    b.Trigger.PlayerInPostZone = Bool(trigger, "PlayerInPostZone", path + ".Trigger", errors, false);
                    b.Trigger.MaxPerEncounter = Int(trigger, "MaxPerEncounter", path + ".Trigger", errors, 1, 1, 16);
                }

                var cooldown = Obj(bo, "Cooldown", path, errors, false);
                if (cooldown != null)
                {
                    Keys(cooldown, path + ".Cooldown", new[] { "RetrySec", "ReentrySec" }, errors);
                    b.Cooldown.RetrySec = Float(cooldown, "RetrySec", path + ".Cooldown", errors, 3f, 0f, 3600f);
                    b.Cooldown.ReentrySec = Float(cooldown, "ReentrySec", path + ".Cooldown", errors, 8f, 0f, 3600f);
                }

                if (b.Kind == "post")
                {
                    b.Rank = StrList(bo, "Rank", path, errors, true);
                    if (b.Rank.Count == 0)
                        errors.Add(new StoryValidationIssue(path + ".Rank", "empty_collection", "候选排序不能为空（禁止用字典顺序选人）"));
                    foreach (string key in b.Rank)
                        if (key != "distanceToPlayer" && key != "npcId")
                            errors.Add(new StoryValidationIssue(path + ".Rank", "bad_token", "排序键只能是 distanceToPlayer / npcId，实际 '" + key + "'"));
                    // 🔴 稳定的最终并列裁决必须存在（同一批候选不能靠遍历顺序定输赢）
                    if (b.Rank.Count > 0 && b.Rank[b.Rank.Count - 1] != "npcId")
                        errors.Add(new StoryValidationIssue(path + ".Rank", "unstable_tiebreak", "排序键最后一项必须是 npcId（候选并列要有稳定身份键）"));

                    b.Flow = Str(bo, "Flow", path, errors, true);
                    if (!string.IsNullOrEmpty(b.Flow) && !data.Flows.ContainsKey(b.Flow))
                        errors.Add(new StoryValidationIssue(path + ".Flow", "unknown_flow", "流程 '" + b.Flow + "' 不存在"));
                }
                b.MaxActive = Int(bo, "MaxActive", path, errors, 1, 1, 8);

                data.Behaviors[bp.Name] = b;
            }
        }

        // ── 开场时间轴 ──
        private static void ParseBeats(JObject root, StoryLayoutFile data, List<StoryValidationIssue> errors,
            StoryValidationMode mode, HashSet<string> cast)
        {
            var beats = Arr(root, "Beats", "$", errors);
            if (beats == null) return;

            var openingStage = data.Stages.Values.FirstOrDefault(s => s.Opening);
            HashSet<string> roster = null;
            if (openingStage != null) roster = new HashSet<string>(openingStage.Actors.Concat(openingStage.OptionalActors), StringComparer.Ordinal);

            for (int i = 0; i < beats.Count; i++)
            {
                string path = "$.Beats[" + i + "]";
                if (!(beats[i] is JObject bo)) { errors.Add(new StoryValidationIssue(path, "wrong_type", "台本拍必须是对象")); continue; }
                var beat = new StoryBeatDef { At = Float(bo, "At", path, errors, 0f, 0f, 100000f) };
                beat.Cmd = Str(bo, "Cmd", path, errors, true);
                if (string.IsNullOrEmpty(beat.Cmd)) continue;

                if (!StoryAtomRegistry.TryGet(beat.Cmd, out var spec))
                {
                    errors.Add(new StoryValidationIssue(path + ".Cmd", "unknown_atom", "未注册的原子 '" + beat.Cmd + "'"));
                    continue;
                }
                if (!spec.Implemented && mode == StoryValidationMode.Formal)
                    errors.Add(new StoryValidationIssue(path + ".Cmd", "unimplemented_atom",
                        "原子 '" + beat.Cmd + "' 尚未实现，不能进正式数据（" + spec.Notes + "）"));

                string[] allowed = BeatKeys(beat.Cmd);
                if (allowed != null) Keys(bo, path, allowed, errors);
                if (bo.Property("Note") != null) beat.Note = bo["Note"]?.ToString();

                switch (beat.Cmd)
                {
                    case "camera_cut":
                        beat.Case = Str(bo, "Case", path, errors, true);
                        beat.Anchor = Str(bo, "Anchor", path, errors, false);
                        beat.Fwd = Float(bo, "Fwd", path, errors, 0f, -1000f, 1000f);
                        beat.Right = Float(bo, "Right", path, errors, 0f, -1000f, 1000f);
                        beat.Up = Float(bo, "Up", path, errors, 0f, -1000f, 1000f);
                        if (!string.IsNullOrEmpty(beat.Anchor) && openingStage != null
                            && data.Layouts.TryGetValue(openingStage.Layout ?? "", out var l0) && !l0.Anchors.ContainsKey(beat.Anchor))
                            errors.Add(new StoryValidationIssue(path + ".Anchor", "unknown_anchor", "锚点 '" + beat.Anchor + "' 不在布局 '" + l0.Id + "' 里"));
                        break;
                    case "prop_spawn":
                        beat.Id = Str(bo, "Id", path, errors, true);
                        beat.Prefab = Str(bo, "Prefab", path, errors, false);
                        // 🔴 台本不许再写一份几何：位置/朝向/缩放只能来自布局
                        if (bo.Property("Anchor") != null || bo.Property("Fwd") != null || bo.Property("Right") != null
                            || bo.Property("Up") != null || bo.Property("Yaw") != null || bo.Property("Scale") != null)
                            errors.Add(new StoryValidationIssue(path, "dual_geometry",
                                "prop_spawn 不得携带位置/朝向/缩放（几何只能来自布局 Props；台本只允许引用已登记的延后道具）"));
                        if (!string.IsNullOrEmpty(beat.Id))
                        {
                            if (openingStage == null || (!openingStage.Props.Contains(beat.Id) && !openingStage.OptionalProps.Contains(beat.Id)))
                                errors.Add(new StoryValidationIssue(path + ".Id", "prop_not_in_roster",
                                    "道具 '" + beat.Id + "' 不在开场阶段道具名单里"));
                        }
                        break;
                    case "actor_place":
                        beat.Actor = Str(bo, "Actor", path, errors, true);
                        beat.Anchor = Str(bo, "Anchor", path, errors, false);
                        beat.Fwd = Float(bo, "Fwd", path, errors, 0f, -1000f, 1000f);
                        beat.Right = Float(bo, "Right", path, errors, 0f, -1000f, 1000f);
                        beat.Up = Float(bo, "Up", path, errors, 0f, -1000f, 1000f);
                        beat.Facing = Float(bo, "Facing", path, errors, -1f, -3600f, 3600f);
                        // Post / WorkPoint / Anchor 三选一，且引用的对象必须存在
                        {
                            string postId = Str(bo, "Post", path, errors, false);
                            string workId = Str(bo, "WorkPoint", path, errors, false);
                            int sources = (string.IsNullOrEmpty(postId) ? 0 : 1) + (string.IsNullOrEmpty(workId) ? 0 : 1)
                                        + (bo.Property("Anchor") != null ? 1 : 0);
                            if (sources != 1)
                                errors.Add(new StoryValidationIssue(path, "bad_field",
                                    "actor_place 必须在 Post / WorkPoint / Anchor 里三选一（当前 " + sources + " 个）"));
                            if (openingStage != null && data.Layouts.TryGetValue(openingStage.Layout ?? "", out var lp))
                            {
                                if (!string.IsNullOrEmpty(postId) && lp.Posts.All(x => x.Id != postId))
                                    errors.Add(new StoryValidationIssue(path + ".Post", "unknown_post", "岗位 '" + postId + "' 不在布局里"));
                                if (!string.IsNullOrEmpty(workId) && lp.WorkPoints.All(x => x.Id != workId))
                                    errors.Add(new StoryValidationIssue(path + ".WorkPoint", "unknown_workpoint", "工作点 '" + workId + "' 不在布局里"));
                            }
                        }
                        checkActor();
                        break;
                    case "actor_action":
                        beat.Actor = Str(bo, "Actor", path, errors, true);
                        beat.Action = Str(bo, "Action", path, errors, true);
                        beat.Async = Bool(bo, "Async", path, errors, false);
                        checkActor();
                        break;
                    case "actor_say":
                        beat.Actor = Str(bo, "Actor", path, errors, true);
                        beat.TextKey = Str(bo, "TextKey", path, errors, true);
                        beat.Listener = Str(bo, "Listener", path, errors, true);
                        if (beat.Listener == beat.Actor || (beat.Listener != "player"
                            && (roster == null || !roster.Contains(beat.Listener))))
                            errors.Add(new StoryValidationIssue(path + ".Listener", "unknown_listener",
                                "听话者必须为 player 或另一位开场演员"));
                        checkActor();
                        break;
                    case "wait":
                        beat.Seconds = Float(bo, "Seconds", path, errors, 0f, 0f, 100000f);
                        break;
                }

                data.Beats.Add(beat);

                void checkActor()
                {
                    if (string.IsNullOrEmpty(beat.Actor)) return;
                    if (roster == null)
                    {
                        errors.Add(new StoryValidationIssue(path + ".Actor", "no_opening_stage",
                            "台本里有演员动作，但没有任何阶段声明 Opening=true，无法确定名单"));
                        return;
                    }
                    if (!roster.Contains(beat.Actor))
                        errors.Add(new StoryValidationIssue(path + ".Actor", "actor_not_in_roster",
                            "演员 '" + beat.Actor + "' 不在开场阶段名单里（台本不能暗中补人）"));
                    if (cast.Count > 0 && !cast.Contains(beat.Actor))
                        errors.Add(new StoryValidationIssue(path + ".Actor", "unknown_cast_actor",
                            "演员 '" + beat.Actor + "' 不在选角表里"));
                }
            }
        }

        /// <summary>台本每种原子允许出现的键（At/Cmd/Note 恒允许）。</summary>
        private static string[] BeatKeys(string cmd)
        {
            switch (cmd)
            {
                case "player_freeze":
                case "player_release":
                case "camera_return":
                    return new[] { "At", "Cmd", "Note" };
                case "camera_cut":
                    return new[] { "At", "Cmd", "Case", "Anchor", "Fwd", "Right", "Up", "Note" };
                case "prop_spawn":
                    return new[] { "At", "Cmd", "Id", "Prefab", "Anchor", "Fwd", "Right", "Up", "Yaw", "Scale", "Note" };
                case "actor_place":
                    return new[] { "At", "Cmd", "Actor", "Post", "WorkPoint", "Anchor", "Fwd", "Right", "Up", "Facing", "Note" };
                case "actor_action":
                    return new[] { "At", "Cmd", "Actor", "Action", "Async", "Note" };
                case "actor_say":
                    return new[] { "At", "Cmd", "Actor", "TextKey", "Listener", "Note" };
                case "wait":
                    return new[] { "At", "Cmd", "Seconds", "Note" };
                default:
                    return null;
            }
        }

        // ── 解算：把引用图算成锚点基下的分量 ──
        private static void Solve(StoryLayoutFile data, StoryValidationResult result)
        {
            foreach (var layout in data.Layouts.Values)
            {
                foreach (var p in layout.Props.Where(p => p.Source == "prefab"))
                    result.Solved.Add(new StorySolvedPoint { Kind = "prop", Id = p.Id, Anchor = p.Place.Anchor, Fwd = p.Place.Fwd, Right = p.Place.Right, Up = p.Place.Up });

                foreach (var post in layout.Posts)
                    result.Solved.Add(new StorySolvedPoint { Kind = "post", Id = post.Id, Anchor = post.Place.Anchor, Fwd = post.Place.Fwd, Right = post.Place.Right, Up = post.Place.Up });

                foreach (var w in layout.WorkPoints)
                {
                    var prop = layout.FindProp(w.Prop);
                    if (prop == null || prop.Source != "prefab") continue;
                    double yaw = prop.Yaw * Math.PI / 180.0;
                    double cos = Math.Cos(yaw), sin = Math.Sin(yaw);
                    // 道具自身坐标系里的「哪一侧」（r = 右手，f = 前方）
                    double r0 = 0, f0 = 0;
                    switch (w.Side)
                    {
                        case "front": f0 = 1; break;
                        case "back": f0 = -1; break;
                        case "left": r0 = -1; break;
                        case "right": r0 = 1; break;
                    }
                    double r1 = r0 * cos - f0 * sin;
                    double f1 = r0 * sin + f0 * cos;
                    float f = prop.Place.Fwd + (float)(f1 * w.Offset);
                    float r = prop.Place.Right + (float)(r1 * w.Offset);
                    result.Solved.Add(new StorySolvedPoint
                    {
                        Kind = "work", Id = w.Id, Anchor = prop.Place.Anchor,
                        Fwd = f, Right = r, Up = prop.Place.Up
                    });
                }
            }
        }

        // ───────────────────────── 原语 ─────────────────────────

        private static void Keys(JObject o, string path, string[] allowed, List<StoryValidationIssue> errors)
        {
            foreach (var p in o.Properties())
                if (!allowed.Contains(p.Name))
                    errors.Add(new StoryValidationIssue(path + "." + p.Name, "unknown_key",
                        "未知字段 '" + p.Name + "'（允许：" + string.Join(", ", allowed) + "）"));
        }

        private static JObject Obj(JObject o, string name, string path, List<StoryValidationIssue> errors, bool required)
        {
            var t = o?[name];
            if (t == null || t.Type == JTokenType.Null)
            {
                if (required) errors.Add(new StoryValidationIssue(path + "." + name, "missing_field", "缺少必需对象 '" + name + "'"));
                return null;
            }
            if (!(t is JObject jo)) { errors.Add(new StoryValidationIssue(path + "." + name, "wrong_type", "'" + name + "' 必须是对象")); return null; }
            return jo;
        }

        private static JArray Arr(JObject o, string name, string path, List<StoryValidationIssue> errors)
        {
            var t = o?[name];
            if (t == null || t.Type == JTokenType.Null) return null;
            if (!(t is JArray ja)) { errors.Add(new StoryValidationIssue(path + "." + name, "wrong_type", "'" + name + "' 必须是数组")); return null; }
            return ja;
        }

        private static string Str(JObject o, string name, string path, List<StoryValidationIssue> errors, bool required)
        {
            var t = o?[name];
            if (t == null || t.Type == JTokenType.Null)
            {
                if (required) errors.Add(new StoryValidationIssue(path + "." + name, "missing_field", "缺少必需字符串 '" + name + "'"));
                return null;
            }
            if (t.Type != JTokenType.String) { errors.Add(new StoryValidationIssue(path + "." + name, "wrong_type", "'" + name + "' 必须是字符串")); return null; }
            string v = t.ToString();
            if (required && v.Length == 0) errors.Add(new StoryValidationIssue(path + "." + name, "missing_field", "'" + name + "' 不能为空"));
            return v;
        }

        private static int Int(JObject o, string name, string path, List<StoryValidationIssue> errors, int fallback, int min, int max)
        {
            var t = o?[name];
            if (t == null || t.Type == JTokenType.Null) return fallback;
            if (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)
            { errors.Add(new StoryValidationIssue(path + "." + name, "wrong_type", "'" + name + "' 必须是整数")); return fallback; }
            int v;
            try { v = (int)t; }
            catch { errors.Add(new StoryValidationIssue(path + "." + name, "wrong_type", "'" + name + "' 必须是整数")); return fallback; }
            if (v < min || v > max)
                errors.Add(new StoryValidationIssue(path + "." + name, "bad_range", "'" + name + "' 超出范围 [" + min + "," + max + "]：实际 " + v));
            return v;
        }

        private static float Float(JObject o, string name, string path, List<StoryValidationIssue> errors, float fallback, float min, float max)
        {
            var t = o?[name];
            if (t == null || t.Type == JTokenType.Null) return fallback;
            if (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)
            { errors.Add(new StoryValidationIssue(path + "." + name, "wrong_type", "'" + name + "' 必须是数字")); return fallback; }
            float v;
            try { v = (float)t; }
            catch { errors.Add(new StoryValidationIssue(path + "." + name, "wrong_type", "'" + name + "' 必须是数字")); return fallback; }
            if (float.IsNaN(v) || float.IsInfinity(v))
            { errors.Add(new StoryValidationIssue(path + "." + name, "bad_value", "'" + name + "' 不是有限数")); return fallback; }
            if (v < min || v > max)
                errors.Add(new StoryValidationIssue(path + "." + name, "bad_range", "'" + name + "' 超出范围 [" + min + "," + max + "]：实际 " + v));
            return v;
        }

        private static bool Bool(JObject o, string name, string path, List<StoryValidationIssue> errors, bool fallback)
        {
            var t = o?[name];
            if (t == null || t.Type == JTokenType.Null) return fallback;
            if (t.Type != JTokenType.Boolean)
            { errors.Add(new StoryValidationIssue(path + "." + name, "wrong_type", "'" + name + "' 必须是布尔值")); return fallback; }
            return (bool)t;
        }

        private static string[] StrArray(JObject o, string name, string path, List<StoryValidationIssue> errors, bool required)
        {
            var t = o?[name];
            if (t == null || t.Type == JTokenType.Null)
            {
                if (required) errors.Add(new StoryValidationIssue(path + "." + name, "missing_field", "缺少必需数组 '" + name + "'"));
                return new string[0];
            }
            if (!(t is JArray ja)) { errors.Add(new StoryValidationIssue(path + "." + name, "wrong_type", "'" + name + "' 必须是数组")); return new string[0]; }
            var list = new List<string>();
            foreach (var item in ja)
            {
                if (item.Type != JTokenType.String)
                { errors.Add(new StoryValidationIssue(path + "." + name, "wrong_type", "'" + name + "' 的元素必须是字符串")); continue; }
                list.Add(item.ToString());
            }
            if (required && list.Count == 0)
                errors.Add(new StoryValidationIssue(path + "." + name, "empty_collection", "'" + name + "' 不能为空"));
            return list.ToArray();
        }

        private static List<string> StrList(JObject o, string name, string path, List<StoryValidationIssue> errors, bool required)
        {
            return new List<string>(StrArray(o, name, path, errors, required));
        }

        private static float[] FloatArray(JObject o, string name, string path, List<StoryValidationIssue> errors, int length, bool required)
        {
            var t = o?[name];
            if (t == null || t.Type == JTokenType.Null)
            {
                if (required) errors.Add(new StoryValidationIssue(path + "." + name, "missing_field", "缺少必需数组 '" + name + "'"));
                return new float[0];
            }
            if (!(t is JArray ja)) { errors.Add(new StoryValidationIssue(path + "." + name, "wrong_type", "'" + name + "' 必须是数组")); return new float[0]; }
            if (ja.Count != length)
            {
                errors.Add(new StoryValidationIssue(path + "." + name, "bad_length", "'" + name + "' 必须是 " + length + " 个数，实际 " + ja.Count));
                return new float[0];
            }
            var result = new float[length];
            for (int i = 0; i < length; i++)
            {
                if (ja[i].Type != JTokenType.Integer && ja[i].Type != JTokenType.Float)
                { errors.Add(new StoryValidationIssue(path + "." + name + "[" + i + "]", "wrong_type", "必须是数字")); return new float[0]; }
                result[i] = (float)ja[i];
            }
            return result;
        }

        /// <summary>
        /// 点：写 Anchor + Fwd/Right/Up；name 为 null 时表示这些键直接挂在父对象上。
        /// </summary>
        private static StoryPoint Point(JObject o, string name, string path, List<StoryValidationIssue> errors,
            StoryLayoutDef layout, bool required)
        {
            JObject src = o;
            if (name != null)
            {
                src = Obj(o, name, path, errors, required);
                if (src == null) return new StoryPoint();
                path = path + "." + name;
                Keys(src, path, new[] { "Anchor", "Fwd", "Right", "Up" }, errors);
            }
            var p = new StoryPoint
            {
                Anchor = Str(src, "Anchor", path, errors, required),
                Fwd = Float(src, "Fwd", path, errors, 0f, -1000f, 1000f),
                Right = Float(src, "Right", path, errors, 0f, -1000f, 1000f),
                Up = Float(src, "Up", path, errors, 0f, -1000f, 1000f)
            };
            if (!string.IsNullOrEmpty(p.Anchor) && layout != null && !layout.Anchors.ContainsKey(p.Anchor))
                errors.Add(new StoryValidationIssue(path + ".Anchor", "unknown_anchor", "锚点 '" + p.Anchor + "' 不在同一布局里"));
            return p;
        }

        private static void DupCheck(List<string> list, string path, string what, List<StoryValidationIssue> errors)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string item in list)
                if (!string.IsNullOrEmpty(item) && !seen.Add(item))
                    errors.Add(new StoryValidationIssue(path, "duplicate_id", what + " '" + item + "' 重复"));
        }
    }

    /// <summary>
    /// 正式布局的读盘与发布。入场前重读一次；场内查询只使用已发布数据。
    /// 校验/读盘失败保留旧版，不能先 Clear 再尝试；没有有效旧版则拒绝入场。
    /// </summary>
    public static class StoryV2Data
    {
        private static bool _attempted;
        private static string _report = "not loaded";

        public static string Report => _report;
        public static bool Published => StoryLayoutRegistry.Published != null;

        public static bool EnsurePublished() => _attempted ? Published : Reload();

        /// <summary>
        /// 返回是否有可用布局（新发布或保留的旧版）。只在旧 Mission 已结束时发布，
        /// 保证一场戏期间不会混用新旧岗位/行为/工作点数据。
        /// </summary>
        public static bool Reload()
        {
            if (TaleWorlds.MountAndBlade.Mission.Current != null)
            {
                // 场内首次需要数据仍允许初始化；已发布内容不可在活跃场景被替换。
                if (Published)
                {
                    _report = "deferred: leave the current mission before reloading; kept v"
                              + StoryLayoutRegistry.PublishedVersion;
                    DebugLogger.Log("[StoryV2] " + _report);
                    return true;
                }
            }
            _attempted = true;
            try
            {
                string folder = StoryProbeDataFiles.DesignDataFolder;
                if (string.IsNullOrEmpty(folder)) return Reject("no design data folder");
                string path = Path.Combine(folder, "StoryStageV2.json");
                if (!File.Exists(path)) return Reject("StoryStageV2.json missing");
                var ids = StoryLayoutParser.LoadCastIds(Path.Combine(folder, "StoryProbeCast.json"));
                if (!StoryLayoutRegistry.TryPublish(File.ReadAllText(path), path, ids,
                    StoryValidationMode.Formal, out var result))
                    return Reject(result.Describe());
                _report = "published v" + result.Data.ContentVersion + " layout=" + result.Data.Schema;
                DebugLogger.Log("[StoryV2] " + _report);
                return true;
            }
            catch (Exception ex) { return Reject(ex.GetType().Name + ": " + ex.Message); }
        }

        private static bool Reject(string reason)
        {
            _report = "rejected: " + reason + (Published
                ? "; kept previous v" + StoryLayoutRegistry.PublishedVersion
                : "; no valid layout available");
            DebugLogger.Log("[StoryV2] " + _report);
            return Published;
        }
    }

    /// <summary>布局解析、校验全部通过才一次发布；失败保留上一有效版本。</summary>
    public static class StoryLayoutRegistry
    {
        private static StoryLayoutFile _published;
        private static string _source;
        private static int _version;
        private static StoryValidationMode _mode = StoryValidationMode.Formal;
        private static List<StorySolvedPoint> _solved = new List<StorySolvedPoint>();

        public static StoryLayoutFile Published => _published;
        public static string Source => _source;
        public static int PublishedVersion => _version;
        public static StoryValidationMode PublishedMode => _mode;
        public static IReadOnlyList<StorySolvedPoint> Solved => _solved;

        public static bool TryPublish(string json, string source, IEnumerable<string> castIds, StoryValidationMode mode,
            out StoryValidationResult result)
        {
            result = StoryLayoutParser.Parse(json, source, castIds, mode);
            if (!result.Ok) return false;
            _published = result.Data;
            _source = source;
            _version = result.Data.ContentVersion;
            _mode = mode;
            _solved = result.Solved ?? new List<StorySolvedPoint>();
            DebugLogger.Log($"[StoryLayout] published {source} mode={mode} schema={result.Data.Schema} " +
                            $"contentVersion={_version} scene={result.Data.Scene} " +
                            $"layouts={result.Data.Layouts.Count} stages={result.Data.Stages.Count} solved={result.Solved.Count}");
            return true;
        }

        public static void Clear() { _published = null; _source = null; _version = 0; _mode = StoryValidationMode.Formal; }

        public static string Describe()
        {
            if (_published == null) return "OK: no published layout (run 'script validate' or wait for P3 migration)";
            return $"OK: published contentVersion={_version} mode={_mode} scene={_published.Scene} wallLevel={_published.WallLevel} " +
                   $"layouts={_published.Layouts.Count} stages={_published.Stages.Count} " +
                   $"behaviors={_published.Behaviors.Count} flows={_published.Flows.Count} beats={_published.Beats.Count} source={_source}";
        }
    }
}
