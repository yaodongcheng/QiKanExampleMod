using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LivingWorldNpcs.Planner.Cooperation
{
    /// <summary>
    /// 协作规划的内容定义（策划 JSON）：能力模型、目标、HTN 方法、预算与阶段说明。
    /// 这是手维护源，不是生成物；校验失败整份拒收，旧有效版本继续服务（§5.7）。
    /// </summary>
    public sealed class CoopContentData
    {
        public int SchemaVersion = 1;
        public string ContentVersion = "0";
        public string SourcePath = string.Empty;
        public string ConfigHash = string.Empty;
        public CoopBudget Budget = CoopBudget.Default();
        /// <summary>全场新规划共享的每帧 CPU 预算（毫秒）。</summary>
        public double FrameCpuMs = 2.0;
        /// <summary>单次求解累计 CPU 上限（毫秒，分帧推进）。</summary>
        public double SolveCpuMs = 64.0;
        public int MaxAttempts = 3;
        public int MaxReplans = 3;
        public readonly CoopDomain Domain = new CoopDomain();
        public readonly List<CoopGoalDef> Goals = new List<CoopGoalDef>();
        public readonly Dictionary<string, CoopGoalDef> GoalsById = new Dictionary<string, CoopGoalDef>(StringComparer.Ordinal);
        /// <summary>阶段说明：stageKey → 本地化 key。</summary>
        public readonly Dictionary<string, string> StageTextKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>参与者角色（内容引用，不写死 C# 分支）。</summary>
        public readonly List<CoopRoleDef> Roles = new List<CoopRoleDef>();
        /// <summary>招募块：候选人、价格与前置（§4.3）。</summary>
        public CoopRecruitmentConfig Recruitment = new CoopRecruitmentConfig();
        public readonly List<string> Errors = new List<string>();

        public bool IsValid { get { return Errors.Count == 0; } }

        public CoopGoalDef GoalOrNull(string id)
        {
            CoopGoalDef goal;
            return id != null && GoalsById.TryGetValue(id, out goal) ? goal : null;
        }

        public CoopRoleDef RoleOrNull(string id) => Roles.Find(r => r.Id == id);

        public static CoopContentData Load(string path)
        {
            var data = new CoopContentData { SourcePath = path ?? string.Empty };
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                data.Errors.Add("content file not found: " + path);
                return data;
            }
            string json;
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
                json = Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex)
            {
                data.Errors.Add("read failed: " + ex.Message);
                return data;
            }
            data.ConfigHash = Hash(bytes);
            return Parse(json, data);
        }

        public static CoopContentData Parse(string json, CoopContentData data = null)
        {
            data = data ?? new CoopContentData();
            try { return ParseUnchecked(json, data); }
            catch (Exception ex)
            {
                data.Errors.Add("invalid content value: " + ex.GetType().Name);
                return data;
            }
        }

        private static CoopContentData ParseUnchecked(string json, CoopContentData data)
        {
            data = data ?? new CoopContentData();
            JObject root;
            try
            {
                root = JObject.Parse(json, new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                });
            }
            catch (Exception ex)
            {
                data.Errors.Add("json parse: " + ex.Message);
                return data;
            }

            data.SchemaVersion = root.Value<int?>("SchemaVersion") ?? 1;
            data.ContentVersion = root.Value<string>("ContentVersion") ?? "0";
            if (data.SchemaVersion != 1)
            {
                data.Errors.Add("unsupported SchemaVersion " + data.SchemaVersion);
            }

            var budget = root["Budget"] as JObject;
            if (budget != null)
            {
                data.Budget.MaxNodes = budget.Value<int?>("MaxNodes") ?? data.Budget.MaxNodes;
                data.Budget.MaxDepth = budget.Value<int?>("MaxDepth") ?? data.Budget.MaxDepth;
                data.Budget.MaxSteps = budget.Value<int?>("MaxSteps") ?? data.Budget.MaxSteps;
                data.FrameCpuMs = budget.Value<double?>("FrameCpuMs") ?? data.FrameCpuMs;
                data.SolveCpuMs = budget.Value<double?>("SolveCpuMs") ?? data.SolveCpuMs;
                data.MaxAttempts = budget.Value<int?>("MaxAttempts") ?? data.MaxAttempts;
                data.MaxReplans = budget.Value<int?>("MaxReplans") ?? data.MaxReplans;
            }
            if (data.Budget.MaxNodes <= 0) data.Errors.Add("Budget.MaxNodes must be positive");
            if (data.Budget.MaxDepth <= 0) data.Errors.Add("Budget.MaxDepth must be positive");
            if (data.Budget.MaxSteps <= 0) data.Errors.Add("Budget.MaxSteps must be positive");
            if (!FinitePositive(data.FrameCpuMs) || !FinitePositive(data.SolveCpuMs)) data.Errors.Add("cpu budget must be finite and positive");
            if (data.MaxAttempts < 0 || data.MaxReplans < 0) data.Errors.Add("retry budget must not be negative");

            var stages = root["Stages"] as JObject;
            if (stages != null)
            {
                foreach (var property in stages.Properties())
                {
                    var key = property.Value.Type == JTokenType.String ? property.Value.ToString() : null;
                    if (string.IsNullOrEmpty(key))
                    {
                        data.Errors.Add("stages." + property.Name + ": text key required");
                        continue;
                    }
                    data.StageTextKeys[property.Name] = key;
                }
            }

            var roles = root["Roles"] as JArray;
            if (roles != null)
            {
                foreach (var token in roles)
                {
                    var role = CoopRoleDef.Parse(token as JObject, data.Errors);
                    if (role != null)
                    {
                        data.Roles.Add(role);
                    }
                }
            }

            var recruitment = root["Recruitment"] as JObject;
            if (recruitment != null)
            {
                data.Recruitment = CoopRecruitmentConfig.Parse(recruitment, data.Errors);
            }

            ExpandParticipants(root, data.Roles, data.Errors);
            var capabilities = root["Capabilities"] as JArray;
            if (capabilities != null)
            {
                foreach (var token in capabilities)
                {
                    var action = ParseAction(token as JObject, data.Errors);
                    if (action == null)
                    {
                        continue;
                    }
                    if (data.Domain.Actions.ContainsKey(action.Id))
                    {
                        data.Errors.Add("duplicate capability id '" + action.Id + "'");
                        continue;
                    }
                    data.Domain.Actions[action.Id] = action;
                }
            }

            var goals = root["Goals"] as JArray;
            if (goals != null)
            {
                foreach (var token in goals)
                {
                    var goal = ParseGoal(token as JObject, data.Errors);
                    if (goal == null)
                    {
                        continue;
                    }
                    if (data.GoalsById.ContainsKey(goal.Id))
                    {
                        data.Errors.Add("duplicate goal id '" + goal.Id + "'");
                        continue;
                    }
                    data.Goals.Add(goal);
                    data.GoalsById[goal.Id] = goal;
                }
            }

            var methods = root["Methods"] as JArray;
            if (methods != null)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var token in methods)
                {
                    var method = ParseMethod(token as JObject, data.Errors);
                    if (method == null)
                    {
                        continue;
                    }
                    if (!seen.Add(method.Task + "::" + method.Id))
                    {
                        data.Errors.Add("duplicate method '" + method.Id + "' for task '" + method.Task + "'");
                        continue;
                    }
                    data.Domain.AddMethod(method);
                }
            }

            ValidateClosure(data);
            return data;
        }

        private static CoopActionDef ParseAction(JObject obj, List<string> errors)
        {
            if (obj == null)
            {
                errors.Add("capability entry is not an object");
                return null;
            }
            var id = obj.Value<string>("Id");
            if (string.IsNullOrEmpty(id))
            {
                errors.Add("capability missing Id");
                return null;
            }
            var action = new CoopActionDef
            {
                Id = id,
                Domain = obj.Value<string>("Domain") ?? "coop",
                CapabilityId = obj.Value<string>("CapabilityId") ?? id,
                Cost = obj.Value<int?>("Cost") ?? 1,
                StageKey = obj.Value<string>("Stage") ?? string.Empty,
                DisplayKey = obj.Value<string>("DisplayKey") ?? string.Empty,
                DescriptionKey = obj.Value<string>("DescriptionKey") ?? string.Empty,
                OutcomeCertain = obj.Value<bool?>("OutcomeCertain") ?? true,
                TimeoutGameSeconds = obj.Value<double?>("TimeoutGameSeconds") ?? 0,
                Retryable = obj.Value<bool?>("Retryable") ?? true,
                ExpandTask = obj.Value<string>("ExpandTask"),
                PlannableNote = obj.Value<string>("Note")
            };
            var plannable = obj.Value<string>("Plannable");
            if (!string.IsNullOrEmpty(plannable))
            {
                CoopPlannable parsed;
                if (Enum.TryParse(plannable, true, out parsed))
                {
                    action.Plannable = parsed;
                }
                else
                {
                    errors.Add("capability '" + id + "': unknown Plannable '" + plannable + "'");
                }
            }
            if (action.Cost < 0)
            {
                errors.Add("capability '" + id + "': negative cost");
            }
            if (action.TimeoutGameSeconds < 0 || double.IsNaN(action.TimeoutGameSeconds) || double.IsInfinity(action.TimeoutGameSeconds))
            {
                errors.Add("capability '" + id + "': negative timeout");
            }
            var args = obj["Args"] as JObject;
            if (args != null)
            {
                foreach (var property in args.Properties())
                {
                    var value = property.Value.Type == JTokenType.Null ? null : property.Value.ToString();
                    if (string.IsNullOrEmpty(value))
                    {
                        errors.Add("capability '" + id + "': empty arg '" + property.Name + "'");
                        continue;
                    }
                    action.Args[property.Name] = value;
                }
            }
            try
            {
                action.Preconditions = ReadConditions(obj, "Pre", errors, id);
                action.Effects = ReadConditions(obj, "Effects", errors, id);
                action.Persistent = ReadConditions(obj, "Persistent", errors, id);
                if (action.Preconditions.ConflictsWith(action.Persistent))
                    errors.Add("capability '" + id + "': start and persistent conditions conflict");
                // 前置是执行前状态，效果是执行后状态；离岗/归队等合法动作会改变它。
                // ReadConditions 自身拒绝同一效果集里的相反赋值。
            }
            catch (ArgumentException ex)
            {
                errors.Add("capability '" + id + "': " + ex.Message);
            }
            ValidateCapability(action, errors);
            return action;
        }

        // 有限角色集合展开：人数与安排在JSON，通用机制不认任何具体人物ID。
        private static void ExpandParticipants(JObject root, List<CoopRoleDef> roles, List<string> errors)
        {
            foreach (var name in new[] { "Capabilities", "Methods" })
            {
                if (!(root[name] is JArray entries)) continue;
                var expanded = new JArray();
                foreach (var token in entries)
                {
                    if (!(token is JObject entry) || entry["ForEachRole"] == null) { expanded.Add(token.DeepClone()); continue; }
                    string roleId = entry.Value<string>("ForEachRole");
                    var role = roles.Find(r => r.Id == roleId);
                    if (role == null || role.Actors.Count == 0 || role.Actors.Count > 16)
                    { errors.Add("invalid participant role '" + roleId + "'"); continue; }
                    foreach (var actor in role.Actors)
                    {
                        var copy = (JObject)entry.DeepClone();
                        copy.Remove("ForEachRole");
                        ReplaceActor(copy, actor);
                        expanded.Add(copy);
                    }
                }
                root[name] = expanded;
            }
            // all:<role>:<fact>／each:<role>:<task>只在相应条件／子任务数组中使用。
            foreach (var obj in new List<JToken>(root.Descendants()))
            {
                if (!(obj is JArray array) || !(array.Parent is JProperty property)) continue;
                bool tasks = property.Name == "Subtasks";
                if (!tasks && property.Name != "Pre" && property.Name != "Persistent" && property.Name != "Effects"
                    && property.Name != "Conditions" && property.Name != "Constraints" && property.Name != "Guards") continue;
                var replacement = new List<JToken>();
                foreach (var item in array)
                {
                    string value = item.Type == JTokenType.String ? item.ToString() : "";
                    string prefix = tasks ? "each:" : "all:";
                    if (!value.StartsWith(prefix, StringComparison.Ordinal)) { replacement.Add(item.DeepClone()); continue; }
                    var parts = value.Split(new[] { ':' }, 3);
                    var role = parts.Length == 3 ? roles.Find(r => r.Id == parts[1]) : null;
                    if (role == null || role.Actors.Count == 0 || role.Actors.Count > 16)
                    { errors.Add("invalid participant expansion '" + value + "'"); continue; }
                    foreach (var actor in role.Actors) replacement.Add(new JValue(parts[2].Replace("{actor}", actor)));
                }
                array.ReplaceAll(replacement);
            }
        }
        private static void ReplaceActor(JToken token, string actor)
        {
            if (token is JValue value && value.Type == JTokenType.String)
                value.Value = value.ToString().Replace("{actor}", actor);
            else if (token is JContainer container) foreach (var child in container.Children()) ReplaceActor(child, actor);
        }

        private static bool FinitePositive(double value) => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

        // 能力参数合同与Mission适配器同源；扩展原子能力时在此登记，不能默默忽略未知参数。
        private static readonly Dictionary<string, string[]> CapabilityArgs = new Dictionary<string, string[]>(StringComparer.Ordinal) {
            {"move_to_point",new[]{"point","target","Offset","StopDistance","MoveTimeout"}},
            {"follow_to_point",new[]{"point","target","StopDistance","MoveTimeout"}},
            {"face_target",new[]{"target","FaceSeconds"}}, {"hold",new[]{"target","HoldSeconds"}},
            {"speak_line",new[]{"target","line"}}, {"signal_player",new[]{"line","delivery","action"}},
            {"wait_fact",new[]{"fact","Distance"}}, {"observe_post",new[]{"target","point","Distance"}},
            {"interact_request",new[]{"target","purpose","method","offer","period","line","replyLine","evidence"}}
        };
        public static bool HasRuntimeContract(string capability) => capability != null && CapabilityArgs.ContainsKey(capability);

        private static void ValidateCapability(CoopActionDef action, List<string> errors)
        {
            if (!CapabilityArgs.TryGetValue(action.CapabilityId, out var allowed)) return; // 未建模能力由目录排除。
            string prefix = "capability '" + action.Id + "': ";
            foreach (var arg in action.Args)
            {
                if (Array.IndexOf(allowed, arg.Key) < 0) { errors.Add(prefix + "unknown argument '" + arg.Key + "'"); continue; }
                if (arg.Key == "Offset" || arg.Key == "StopDistance" || arg.Key == "MoveTimeout"
                    || arg.Key == "HoldSeconds" || arg.Key == "FaceSeconds" || arg.Key == "Distance" || arg.Key == "offer")
                {
                    if (!double.TryParse(arg.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                        || double.IsNaN(number) || double.IsInfinity(number) || number < 0
                        || (arg.Key != "offer" && arg.Key != "Offset" && number == 0))
                        errors.Add(prefix + "invalid numeric argument '" + arg.Key + "'");
                }
            }
            void Require(string key) { if (!action.Args.ContainsKey(key)) errors.Add(prefix + "missing argument '" + key + "'"); }
            switch (action.CapabilityId)
            {
                case "follow_to_point":
                    Require("target"); Require("point");
                    if (!action.Args.TryGetValue("point", out var followPoint)
                        || !followPoint.StartsWith("scene:", StringComparison.Ordinal) || followPoint.Length <= 6)
                        errors.Add(prefix + "follow destination requires a scene point");
                    break;
                case "move_to_point":
                    Require("point");
                    if (action.Args.TryGetValue("point", out var point))
                    {
                        if (Array.IndexOf(new[]{"self","target","player","post","relief_post"},point) < 0
                            && (!point.StartsWith("scene:", StringComparison.Ordinal) || point.Length <= 6)) errors.Add(prefix + "unknown point query");
                        if (point == "target") Require("target");
                    }
                    break;
                case "face_target": Require("target"); break;
                case "observe_post":
                    Require("target");
                    if (action.Args.TryGetValue("point", out var observationPoint)
                        && (!observationPoint.StartsWith("scene:", StringComparison.Ordinal) || observationPoint.Length <= 6))
                        errors.Add(prefix + "observation destination requires a scene point");
                    break;
                case "interact_request": Require("target"); Require("purpose"); Require("line"); break;
                case "signal_player":
                    if (action.Args.TryGetValue("delivery", out var delivery))
                    {
                        if (delivery != "voice" && delivery != "visual") errors.Add(prefix + "unknown signal delivery");
                        if (delivery == "visual") Require("action");
                    }
                    Require("line"); break;
                case "speak_line": Require("line"); break;
                case "wait_fact": Require("fact"); break;
            }
            if ((action.CapabilityId == "wait_fact" || action.CapabilityId == "observe_post"
                || action.CapabilityId == "interact_request" || action.CapabilityId == "follow_to_point") && !FinitePositive(action.TimeoutGameSeconds))
                errors.Add(prefix + "waiting capability requires a finite timeout");
        }

        private static CoopGoalDef ParseGoal(JObject obj, List<string> errors)
        {
            if (obj == null)
            {
                errors.Add("goal entry is not an object");
                return null;
            }
            var id = obj.Value<string>("Id");
            if (string.IsNullOrEmpty(id))
            {
                errors.Add("goal missing Id");
                return null;
            }
            var goal = new CoopGoalDef
            {
                Id = id,
                DisplayKey = obj.Value<string>("DisplayKey") ?? string.Empty,
                RootTask = obj.Value<string>("RootTask"),
                Priority = obj.Value<int?>("Priority") ?? 0
            };
            try
            {
                goal.Conditions = ReadConditions(obj, "Conditions", errors, "goal:" + id);
                goal.Constraints = ReadConditions(obj, "Constraints", errors, "goal:" + id);
            }
            catch (ArgumentException ex)
            {
                errors.Add("goal '" + id + "': " + ex.Message);
                return null;
            }
            if (goal.Conditions.Count == 0)
            {
                errors.Add("goal '" + id + "': empty conditions");
            }
            return goal;
        }

        private static CoopMethodDef ParseMethod(JObject obj, List<string> errors)
        {
            if (obj == null)
            {
                errors.Add("method entry is not an object");
                return null;
            }
            var id = obj.Value<string>("Id");
            var task = obj.Value<string>("Task");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(task))
            {
                errors.Add("method missing Id or Task");
                return null;
            }
            var method = new CoopMethodDef { Id = id, Task = task };
            var subtasks = obj["Subtasks"] as JArray;
            if (subtasks != null)
            {
                var list = new List<string>();
                foreach (var token in subtasks)
                {
                    var name = token.ToString();
                    if (string.IsNullOrEmpty(name))
                    {
                        errors.Add("method '" + id + "': empty subtask");
                        continue;
                    }
                    list.Add(name);
                }
                method.Subtasks = list.ToArray();
            }
            try
            {
                method.Guards = ReadConditions(obj, "Guards", errors, "method:" + id);
            }
            catch (ArgumentException ex)
            {
                errors.Add("method '" + id + "': " + ex.Message);
                return null;
            }
            return method;
        }

        private static CoopConditionSet ReadConditions(JObject obj, string name, List<string> errors, string owner)
        {
            var token = obj[name];
            if (token == null || token.Type == JTokenType.Null)
            {
                return CoopConditionSet.Empty;
            }
            var array = token as JArray;
            if (array == null)
            {
                errors.Add(owner + ": '" + name + "' must be an array");
                return CoopConditionSet.Empty;
            }
            var list = new List<CoopCondition>();
            foreach (var item in array)
            {
                if (item.Type != JTokenType.String)
                {
                    errors.Add(owner + ": '" + name + "' entries must be strings");
                    continue;
                }
                var text = item.ToString().Trim();
                if (text.Length == 0)
                {
                    continue;
                }
                bool value = true;
                if (text[0] == '!')
                {
                    value = false;
                    text = text.Substring(1).Trim();
                }
                if (text.Length == 0)
                {
                    errors.Add(owner + ": empty fact in '" + name + "'");
                    continue;
                }
                list.Add(new CoopCondition(text, value));
            }
            return new CoopConditionSet(list);
        }

        /// <summary>
        /// 引用闭合：每个方法引用的子任务要么是可规划动作、要么是有方法定义的复合任务；
        /// 复合任务必须有终止分支，且不得形成无限递归。
        /// </summary>
        private static void ValidateClosure(CoopContentData data)
        {
            var facts = new HashSet<string>(new[]{"alive","cancelled","briefed","companion_ready","talk_authorized","investigation_authorized","near_guard",
                "engaged","gap","signal","passed","regrouped","other_watcher","player_crossed","off_post",
                "at_post","watching","relief_confirmed","report_checked","report_heard","talk_heard",
                "cover_requested","covering","coverage_done","conversation_held","single_guard","guide_departed","lure_heard"}, StringComparer.Ordinal);
            foreach (var role in data.Roles) foreach (var actor in role.Actors)
            { facts.Add("present_" + actor); facts.Add("handled_" + actor); }
            foreach (var action in data.Domain.Actions.Values) foreach (var effect in action.Effects.Items) facts.Add(effect.Fact);
            foreach (var role in data.Roles) foreach (var policy in role.Requests.Values)
                if (!string.IsNullOrEmpty(policy.HeardFact)) facts.Add(policy.HeardFact);
            void CheckFacts(CoopConditionSet conditions, string owner)
            {
                foreach (var condition in conditions.Items)
                    if (!facts.Contains(condition.Fact)) data.Errors.Add(owner + ": unregistered fact '" + condition.Fact + "'");
            }
            foreach (var action in data.Domain.Actions.Values)
            {
                CheckFacts(action.Preconditions, action.Id); CheckFacts(action.Persistent, action.Id);
                if (action.CapabilityId == "wait_fact" && action.Args.TryGetValue("fact",out var fact) && !facts.Contains(fact))
                    data.Errors.Add(action.Id + ": unknown fact query '" + fact + "'");
            }
            foreach (var goal in data.Goals) { CheckFacts(goal.Conditions, goal.Id); CheckFacts(goal.Constraints, goal.Id); }
            foreach (var methods in data.Domain.Methods.Values) foreach (var method in methods) CheckFacts(method.Guards,method.Id);
            foreach (var pair in data.Domain.Methods)
            {
                foreach (var method in pair.Value)
                {
                    foreach (var subtask in method.Subtasks)
                    {
                        if (data.Domain.Actions.ContainsKey(subtask))
                        {
                            continue;
                        }
                        if (subtask.Length > 0 && subtask[0] == '@')
                        {
                            if (data.Domain.MethodsFor(subtask) == null)
                            {
                                data.Errors.Add("method '" + method.Id + "': unknown compound task '" + subtask + "'");
                            }
                            continue;
                        }
                        data.Errors.Add("method '" + method.Id + "': unknown subtask '" + subtask + "'");
                    }
                }
            }

            foreach (var goal in data.Goals)
            {
                if (!string.IsNullOrEmpty(goal.RootTask) && data.Domain.MethodsFor(goal.RootTask) == null)
                {
                    data.Errors.Add("goal '" + goal.Id + "': root task '" + goal.RootTask + "' has no method");
                }
            }

            foreach (var pair in data.Domain.Actions)
            {
                var task = pair.Value.ExpandTask;
                if (!string.IsNullOrEmpty(task) && data.Domain.MethodsFor(task) == null)
                {
                    data.Errors.Add("capability '" + pair.Key + "': ExpandTask '" + task + "' has no method");
                }
            }

            var roleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var role in data.Roles)
            {
                if (!roleIds.Add(role.Id)) data.Errors.Add("duplicate role id '" + role.Id + "'");
                foreach (var policy in role.Requests.Values)
                {
                    if (!role.GoalIds.Contains(policy.Goal ?? "")) data.Errors.Add("role request references an unowned goal");
                    if (policy.RequiresRelief && !role.GoalIds.Contains(policy.ReliefGoal ?? "")) data.Errors.Add("role request relief goal missing");
                    if (string.IsNullOrEmpty(policy.AcceptLine) || string.IsNullOrEmpty(policy.RefuseLine)
                        || string.IsNullOrEmpty(policy.IneligibleLine)) data.Errors.Add("role request requires reply keys");
                }
                if (!string.IsNullOrEmpty(role.ParticipantRole) && data.RoleOrNull(role.ParticipantRole) == null)
                    data.Errors.Add("unknown participant role '" + role.ParticipantRole + "'");
                if (!string.IsNullOrEmpty(role.RecoveryRole))
                {
                    var recovery = data.RoleOrNull(role.RecoveryRole);
                    if (recovery == null || recovery.RequiresContract || !string.IsNullOrEmpty(recovery.RecoveryRole))
                        data.Errors.Add("recovery role must exist, require no contract and have no recovery chain");
                }
                foreach (var policy in role.Requests.Values)
                    if (!string.IsNullOrEmpty(policy.AuthorizationPurpose)
                        && policy.AuthorizationPurpose != "talk" && policy.AuthorizationPurpose != "investigate")
                        data.Errors.Add("unknown authorization purpose '" + policy.AuthorizationPurpose + "'");
                if (role.Actors.Count > 16 || new HashSet<string>(role.Actors, StringComparer.Ordinal).Count != role.Actors.Count)
                    data.Errors.Add("role actor list must be unique and bounded");
                foreach (var goalId in role.GoalIds)
                {
                    if (!data.GoalsById.ContainsKey(goalId))
                    {
                        data.Errors.Add("role '" + role.Id + "': unknown goal '" + goalId + "'");
                    }
                }
                if (!string.IsNullOrEmpty(role.Algorithm)
                    && !Enum.TryParse(role.Algorithm, true, out CoopAlgorithm _))
                {
                    data.Errors.Add("role '" + role.Id + "': unknown algorithm '" + role.Algorithm + "'");
                }
            }

            foreach (var pair in data.StageTextKeys)
            {
                if (string.IsNullOrEmpty(pair.Value))
                {
                    data.Errors.Add("stage '" + pair.Key + "': empty text key");
                }
            }
            foreach (var action in data.Domain.Actions.Values)
            {
                if (!string.IsNullOrEmpty(action.StageKey) && !data.StageTextKeys.ContainsKey(action.StageKey))
                {
                    data.Errors.Add("capability '" + action.Id + "': stage '" + action.StageKey + "' has no text key");
                }
            }

            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var done = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in data.Domain.Methods)
            {
                if (!HasTerminatingBranch(data, pair.Key, visiting, done))
                {
                    data.Errors.Add("task '" + pair.Key + "': no terminating branch (cycle)");
                }
            }
        }

        private static bool HasTerminatingBranch(CoopContentData data, string task, HashSet<string> visiting, HashSet<string> done)
        {
            if (done.Contains(task))
            {
                return true;
            }
            if (!visiting.Add(task))
            {
                return false;
            }
            var methods = data.Domain.MethodsFor(task);
            bool any = false;
            if (methods != null)
            {
                foreach (var method in methods)
                {
                    bool all = true;
                    foreach (var subtask in method.Subtasks)
                    {
                        if (subtask.Length > 0 && subtask[0] == '@' && data.Domain.MethodsFor(subtask) != null)
                        {
                            if (!HasTerminatingBranch(data, subtask, visiting, done))
                            {
                                all = false;
                                break;
                            }
                        }
                    }
                    if (all)
                    {
                        any = true;
                        break;
                    }
                }
            }
            visiting.Remove(task);
            if (any)
            {
                done.Add(task);
            }
            return any;
        }

        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }

    /// <summary>内容声明的一个参与者角色（如“随从”“门岗”），引用实际对象由运行时解析。</summary>
    public sealed class CoopRoleDef
    {
        public string Id;
        public bool RequiresContract;
        public readonly List<string> Actors = new List<string>();
        public readonly Dictionary<string, CoopResponseDef> Requests = new Dictionary<string, CoopResponseDef>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Presentation = new Dictionary<string, string>(StringComparer.Ordinal);
        public string GoalId;
        public string ParticipantRole;
        public string RecoveryRole;
        /// <summary>可选目标列表（按顺序）；空 = 单一 GoalId。</summary>
        public readonly List<string> GoalIds = new List<string>();
        /// <summary>该角色使用的算法；空 = 跟随全局默认。</summary>
        public string Algorithm;

        public static CoopRoleDef Parse(JObject obj, List<string> errors)
        {
            if (obj == null)
            {
                errors.Add("role entry is not an object");
                return null;
            }
            var id = obj.Value<string>("Id");
            if (string.IsNullOrEmpty(id))
            {
                errors.Add("role missing Id");
                return null;
            }
            var role = new CoopRoleDef
            {
                Id = id,
                GoalId = obj.Value<string>("Goal"),
                Algorithm = obj.Value<string>("Algorithm"),
                ParticipantRole = obj.Value<string>("ParticipantRole"),
                RecoveryRole = obj.Value<string>("RecoveryRole")
            };
            role.RequiresContract = obj.Value<bool?>("RequiresContract") ?? false;
            if (obj["Presentation"] is JObject presentation) foreach (var pair in presentation.Properties())
                role.Presentation[pair.Name] = pair.Value.ToString();
            var actors = obj["Actors"] as JArray;
            if (actors != null) foreach (var actor in actors) role.Actors.Add(actor.ToString());
            var requests = obj["Requests"] as JObject;
            if (requests != null) foreach (var pair in requests.Properties())
            {
                if (!(pair.Value is JObject policy)) { errors.Add("role request must be an object"); continue; }
                role.Requests[pair.Name] = new CoopResponseDef
                {
                    Goal = policy.Value<string>("Goal"), HeardFact = policy.Value<string>("HeardFact"),
                    RequiresRelief = policy.Value<bool?>("RequiresRelief") ?? false,
                    Skill = policy.Value<string>("Skill") ?? "Charm",
                    AcceptLine = policy.Value<string>("AcceptLine"), RefuseLine = policy.Value<string>("RefuseLine"),
                    IneligibleLine = policy.Value<string>("IneligibleLine"),
                    ReliefGoal = policy.Value<string>("ReliefGoal"),
                    AuthorizationPurpose = policy.Value<string>("AuthorizationPurpose")
                };
                if (policy["ActorLines"] != null && !(policy["ActorLines"] is JObject))
                    errors.Add("role request ActorLines must be an object");
                if (policy["ActorLines"] is JObject actorLines) foreach (var actor in actorLines.Properties())
                {
                    if (!role.Actors.Contains(actor.Name) || !(actor.Value is JObject entries))
                    { errors.Add("role request ActorLines requires a declared actor and an object"); continue; }
                    var lines = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var entry in entries.Properties())
                    {
                        if (Array.IndexOf(new[] { "challenge", "accept", "refuse", "ineligible" }, entry.Name) < 0
                            || entry.Value.Type != JTokenType.String || string.IsNullOrWhiteSpace(entry.Value.ToString()))
                        { errors.Add("role request ActorLines has an invalid line"); continue; }
                        lines[entry.Name] = entry.Value.ToString();
                    }
                    role.Requests[pair.Name].ActorLines[actor.Name] = lines;
                }
            }
            var goals = obj["Goals"] as JArray;
            if (goals != null)
            {
                foreach (var token in goals)
                {
                    var text = token.ToString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        role.GoalIds.Add(text);
                    }
                }
            }
            if (role.GoalIds.Count == 0 && !string.IsNullOrEmpty(role.GoalId))
            {
                role.GoalIds.Add(role.GoalId);
            }
            if (role.GoalIds.Count == 0)
            {
                errors.Add("role '" + id + "': no goal declared");
            }
            return role;
        }
    }

    public sealed class CoopResponseDef
    {
        public readonly Dictionary<string, Dictionary<string, string>> ActorLines = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        public string LineFor(string actorId, string kind)
        {
            if (ActorLines.TryGetValue(actorId, out var lines) && lines.TryGetValue(kind, out var key)) return key;
            return kind == "accept" ? AcceptLine : kind == "refuse" ? RefuseLine : kind == "ineligible" ? IneligibleLine : null;
        }
        public string Goal, HeardFact, Skill, AcceptLine, RefuseLine, IneligibleLine, ReliefGoal, AuthorizationPurpose;
        public bool RequiresRelief;
    }

    /// <summary>内容 JSON 的招募块：候选人、价格与前置条件。</summary>
    public sealed class CoopRecruitmentConfig
    {
        public string Candidate = string.Empty;
        public int PriceGold = 30;
        public int FavorCost;
        public string FavorSourceKey = string.Empty;
        public string FavorSourceFallback = string.Empty;
        public bool RequireQuestTurnedIn = true;
        public bool RequireEventSettled = true;
        public string PriceHintKey = "LWN_coop_price";
        public readonly List<string> RecruitableIds = new List<string>();

        public static CoopRecruitmentConfig Parse(Newtonsoft.Json.Linq.JObject obj, List<string> errors)
        {
            var config = new CoopRecruitmentConfig();
            if (obj == null)
            {
                return config;
            }
            config.Candidate = obj.Value<string>("Candidate") ?? string.Empty;
            config.PriceGold = obj.Value<int?>("PriceGold") ?? 30;
            config.FavorCost = obj.Value<int?>("FavorCost") ?? 0;
            config.FavorSourceKey = obj.Value<string>("FavorSourceKey") ?? string.Empty;
            config.FavorSourceFallback = obj.Value<string>("FavorSourceFallback") ?? string.Empty;
            if (config.FavorCost <= 0 || string.IsNullOrWhiteSpace(config.FavorSourceKey)
                || string.IsNullOrWhiteSpace(config.FavorSourceFallback))
                errors.Add("Recruitment positive FavorCost and localized FavorSourceKey/FavorSourceFallback required");
            config.RequireQuestTurnedIn = obj.Value<bool?>("RequireQuestTurnedIn") ?? true;
            config.RequireEventSettled = obj.Value<bool?>("RequireEventSettled") ?? true;
            config.PriceHintKey = obj.Value<string>("PriceHintKey") ?? config.PriceHintKey;
            if (config.PriceGold < 0)
            {
                errors.Add("Recruitment.PriceGold must not be negative");
            }
            var list = obj["Recruitable"] as Newtonsoft.Json.Linq.JArray;
            if (list != null)
            {
                foreach (var token in list)
                {
                    var id = token.ToString();
                    if (!string.IsNullOrEmpty(id))
                    {
                        config.RecruitableIds.Add(id);
                    }
                }
            }
            if (string.IsNullOrEmpty(config.Candidate) && config.RecruitableIds.Count > 0)
            {
                config.Candidate = config.RecruitableIds[0];
            }
            return config;
        }
    }
}
