using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════
    // RuntimeWorldState.cs — 执行期世界状态（每 tick 实时刷新，100ms 节流）
    //
    // 职责（§5.2 封闭谓词词表 + §5.0 动态目标引用）：
    //   ① 实体引用解析：self/player/角色名/区域/物件/query 落点 → Agent 或位置
    //   ② 谓词求值：distance/seeing/alert_phase/following/facing/moving/
    //               in_zone/combat/player_action/time_since/dead/knocked_out/count + and/or/not
    //   ③ 通用修饰符：sustained（连续成立 N 秒防抖）+ was（曾成立过）
    //   ④ query 求值一次后注册为具名引用（后续步骤/谓词复用）
    //
    // 求值输入：每 tick 由 PlanExecutor 调 Tick(dt) 刷新（dt 用于 sustained 积分）。
    // ═══════════════════════════════════════════════════════════════

    public class RuntimeWorldState : WorldState
    {
        public SceneSnapshot Snapshot = new SceneSnapshot();
        public Agent OwnerAgent;                    // 主执行者（self）
        public PlanExecutor Owner;                  // 所属执行器（time_since/player_action/filter 走实例，防多执行器串扰）

        /// <summary>角色表：角色名 → Agent（由计划注入时构建）。</summary>
        public readonly Dictionary<string, Agent> RoleAgents = new Dictionary<string, Agent>(StringComparer.OrdinalIgnoreCase);
        /// <summary>具名位置：区域/物件/query 落点 → 坐标（query 求值一次后注册）。</summary>
        public readonly Dictionary<string, Vec3> NamedPositions = new Dictionary<string, Vec3>(StringComparer.OrdinalIgnoreCase);
        /// <summary>具名物件：名称 → MissionObject。</summary>
        public readonly Dictionary<string, MissionObject> NamedObjects = new Dictionary<string, MissionObject>(StringComparer.OrdinalIgnoreCase);
        /// <summary>具名区域半径（in_zone 判定用）。</summary>
        public readonly Dictionary<string, float> NamedZoneRadii = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        /// <summary>步骤完成时间戳：step_id → 计划内 elapsed。</summary>
        public readonly Dictionary<string, float> StepCompleteTime = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>ReactiveAgent 登记的跟随关系（M3）：(follower, leader) 对。</summary>
        public readonly HashSet<(int, int)> FollowPairs = new HashSet<(int, int)>();

        // sustained / was 修饰符状态
        private readonly Dictionary<string, float> _sustained = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly HashSet<string> _wasEver = new HashSet<string>(StringComparer.Ordinal);
        private float _dt = 0.02f;

        /// <summary>每 tick 刷新（PlanExecutor 调用；100ms 节流由执行器保证）。</summary>
        public void Tick(float dt)
        {
            _dt = dt;
            Snapshot = SceneSnapshot.Build(Mission.Current);
        }

        // ═══════════════════════════════════════════════════════════
        // 条件求值（§5.2）
        // ═══════════════════════════════════════════════════════════

        /// <summary>求值条件。actorContext = 当前步骤的执行 actor（self 解析目标）；goal/contingency 用 OwnerAgent。
        /// 条件树来自 JSON（无环），可安全递归——and/or 嵌套正常求值。</summary>
        public bool Evaluate(Condition c, Agent actorContext)
        {
            if (c == null) return true;
            bool opRaw = EvaluateRaw(c, actorContext);   // 含 op 的当前值
            string key = ConditionKey(c);

            // sustained：连续成立 N 秒（防抖确认）
            if (c.SustainedS > 0f)
            {
                if (opRaw)
                {
                    _sustained.TryGetValue(key, out float t);
                    _sustained[key] = t + _dt;
                    opRaw = _sustained[key] >= c.SustainedS;
                }
                else
                {
                    _sustained[key] = 0f;
                }
            }

            // was：记录"基础谓词曾成立"（不含 op，§5.2 与 op 结果 AND）
            // 例：following==false && was:true = "曾跟随、已停止"——wasEver 记的是 following==true 发生过，
            //     与当前 following==false 取 AND → 折返瞬间才成立，计划开局（从未跟随）不误触发
            if (c.Was)
            {
                bool baseRaw = EvaluateBaseRaw(c, actorContext);
                if (baseRaw) _wasEver.Add(key);
                opRaw = opRaw && _wasEver.Contains(key);
            }

            return opRaw;
        }

        /// <summary>基础谓词求值（忽略 op）：克隆条件把 op 置 "true"（布尔谓词）——was 记录用。</summary>
        private bool EvaluateBaseRaw(Condition c, Agent actorContext)
        {
            var baseC = new Condition
            {
                Type = c.Type,
                A = c.A,
                B = c.B,
                Entity = c.Entity,
                Phase = c.Phase,
                Op = "true",
                Of = c.Of,
                Conditions = c.Conditions,
            };
            return EvaluateRaw(baseC, actorContext);
        }

        /// <summary>GoalTemplate 接口（缺省 actor = 主执行者）。</summary>
        public override bool Evaluate(Condition c) => Evaluate(c, OwnerAgent);

        public override bool WasEverTrue(Condition c)
        {
            return c != null && _wasEver.Contains(ConditionKey(c));
        }

        /// <summary>重置 was 记录（新步骤/新阶段）。</summary>
        public void ResetWas() => _wasEver.Clear();

        /// <summary>遗忘某个条件的 was 记录（contingency 触发后清除 → 条件回落 → 可再次掉线触发）。</summary>
        public void ForgetWasEver(string conditionKey)
        {
            if (!string.IsNullOrEmpty(conditionKey)) _wasEver.Remove(conditionKey);
        }

        private bool EvaluateRaw(Condition c, Agent actorContext)
        {
            switch (c.Type)
            {
                case "and":
                    if (c.Conditions == null) return true;
                    return c.Conditions.All(sub => Evaluate(sub, actorContext));
                case "or":
                    if (c.Conditions == null) return false;
                    return c.Conditions.Any(sub => Evaluate(sub, actorContext));
                case "not":
                    if (c.Conditions == null || c.Conditions.Count == 0) return false;
                    return !Evaluate(c.Conditions[0], actorContext);
                case "distance": return EvalDistance(c, actorContext);
                case "seeing": return ApplyBoolOp(EvalSeeing(c, actorContext), c.Op);
                case "alert_phase": return EvalAlertPhase(c);
                case "following": return ApplyBoolOp(EvalFollowing(c, actorContext), c.Op);
                case "facing": return ApplyBoolOp(EvalFacing(c, actorContext), c.Op);
                case "moving": return ApplyBoolOp(EvalMoving(c, actorContext), c.Op);
                case "in_zone": return ApplyBoolOp(EvalInZone(c, actorContext), c.Op);
                case "combat": return ApplyBoolOp(EvalCombat(c, actorContext), c.Op);
                case "player_action": return ApplyBoolOp(EvalPlayerAction(c), c.Op);
                case "time_since": return EvalTimeSince(c);
                case "dead": return ApplyBoolOp(EvalDead(c), c.Op);
                case "knocked_out": return ApplyBoolOp(EvalKnockedOut(c), c.Op);
                case "count": return EvalCount(c, actorContext);
                default: return false;
            }
        }

        private bool EvalDistance(Condition c, Agent actorContext)
        {
            if (!TryResolvePosition(c.A, actorContext, out Vec3 a)) return false;
            if (!TryResolvePosition(c.B, actorContext, out Vec3 b)) return false;
            float dist = a.Distance(b);
            return CompareFloat(dist, c.Op, c.Value);
        }

        private bool EvalSeeing(Condition c, Agent actorContext)
        {
            // watcher 三值域：具体实体 / "all" / "any"
            string watcher = c.A ?? "";
            if (!TryResolveAgent(c.B, actorContext, out Agent subject)) return false;
            if (!subject.IsActive()) return false;

            bool result;
            try
            {
                if (string.Equals(watcher, "any", StringComparison.OrdinalIgnoreCase))
                {
                    // ∃ 任意一个会告发的目击者（犯罪裁决同款：清醒 + 非队友 + 看得见）
                    var witnesses = StealManager.GetWitnesses(subject, null, 15f, 120f);
                    result = witnesses.Count > 0;
                }
                else if (string.Equals(watcher, "all", StringComparison.OrdinalIgnoreCase))
                {
                    // ∀ 全部活跃人形都能看到
                    result = true;
                    foreach (var a in Mission.Current.Agents)
                    {
                        if (a == null || !a.IsActive() || !AgentControlHelper.IsHumanOrChild(a) || a == subject) continue;
                        if (!NpcSightSystem.CanAgentSeeTarget(a, subject)) { result = false; break; }
                    }
                }
                else
                {
                    if (!TryResolveAgent(watcher, actorContext, out Agent watcherAgent)) return false;
                    if (!watcherAgent.IsActive()) return false;
                    result = NpcSightSystem.CanAgentSeeTarget(watcherAgent, subject);
                }
            }
            catch
            {
                // Mission.Agents 活集合遍历/射线检测异常 → 保守返回 false（不崩 Mission tick）
                result = false;
            }
            return result;
        }

        private bool EvalAlertPhase(Condition c)
        {
            // "any" 值域：任一实体达到该警戒阶段
            if (string.Equals(c.Entity ?? c.A, "any", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    foreach (var a in Mission.Current.Agents)
                    {
                        if (a == null || !a.IsActive() || !AgentControlHelper.IsHumanOrChild(a)) continue;
                        var b = AgentAIController.GetBrainForAgent(a);
                        if (b == null) continue;
                        if (PhaseValue(b.AlertPhase) >= ParsePhase(c.Phase)) return true;
                    }
                }
                catch { }
                return false;
            }
            if (!TryResolveAgent(c.Entity ?? c.A, null, out Agent agent)) return false;
            var brain = AgentAIController.GetBrainForAgent(agent);
            if (brain == null) return false;
            var phase = brain.AlertPhase;
            int threshold = ParsePhase(c.Phase);
            int current = PhaseValue(phase);
            if (string.IsNullOrEmpty(c.Op) || c.Op == ">=") return current >= threshold;
            if (c.Op == ">") return current > threshold;
            if (c.Op == "==" || c.Op == "=") return current == threshold;
            if (c.Op == "<") return current < threshold;
            if (c.Op == "<=") return current <= threshold;
            return current >= threshold;
        }

        private bool EvalFollowing(Condition c, Agent actorContext)
        {
            if (!TryResolveAgent(c.A, actorContext, out Agent follower)) return false;
            if (!TryResolveAgent(c.B, actorContext, out Agent leader)) return false;

            // 通道①：ReactiveAgent 登记（M3 广播 registered）
            if (FollowPairs.Contains((follower.Index, leader.Index))) return true;

            // 通道②：brain 当前动作是跟随该目标
            // （原 ReactiveFollowAction 分支已删，附章③ 2026-08-11：跟走 = FollowAgentAction(optionalDuration)
            // 执行中；折返 = 下一步 MoveToPositionAction——CurrentAction 类型自然区分两个阶段）
            var brain = AgentAIController.GetBrainForAgent(follower);
            if (brain?.CurrentAction is FollowAgentAction fa && fa.TargetAgent == leader)
                return true;
            if (brain?.CurrentIntent?.Type == NpcIntentType.Following && brain.CurrentIntent.Target == leader)
                return true;
            return false;
        }

        private bool EvalFacing(Condition c, Agent actorContext)
        {
            if (!TryResolveAgent(c.A, actorContext, out Agent a)) return false;
            if (!TryResolvePosition(c.B, actorContext, out Vec3 b)) return false;
            if (!a.IsActive()) return false;
            try
            {
                Vec2 look = a.LookDirection.AsVec2.Normalized();
                Vec2 toTarget = (b - a.Position).AsVec2.Normalized();
                float dot = Vec2.DotProduct(look, toTarget);
                return dot > 0.6f;
            }
            catch { return false; }
        }

        private bool EvalMoving(Condition c, Agent actorContext)
        {
            if (!TryResolveAgent(c.A, actorContext, out Agent a)) return false;
            if (!a.IsActive()) return false;
            bool moving = false;
            try
            {
                moving = a.Velocity.LengthSquared > 0.25f;
            }
            catch { }
            return moving;
        }

        private bool EvalInZone(Condition c, Agent actorContext)
        {
            // "any" 值域（LOOKOUT 望风区：任意一人进入即触发）
            if (string.Equals(c.A, "any", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryResolveZone(c.B, out Vec3 zPos, out float zRadius)) return false;
                try
                {
                    foreach (var a in Mission.Current.Agents)
                    {
                        if (a == null || !a.IsActive() || !AgentControlHelper.IsHumanOrChild(a)) continue;
                        if (a.Position.Distance(zPos) <= zRadius) return true;
                    }
                }
                catch { }
                return false;
            }
            if (!TryResolvePosition(c.A, actorContext, out Vec3 pos)) return false;
            if (!TryResolveZone(c.B, out Vec3 zonePos, out float radius)) return false;
            float dist = pos.Distance(zonePos);
            return dist <= radius;
        }

        private bool EvalCombat(Condition c, Agent actorContext)
        {
            // combat(entity)：该实体在战斗中
            if (string.IsNullOrEmpty(c.B) && !string.IsNullOrEmpty(c.Entity))
            {
                if (!TryResolveAgent(c.Entity, actorContext, out Agent e)) return false;
                var brain = AgentAIController.GetBrainForAgent(e);
                return brain != null && brain.IsInCombat;
            }
            if (string.IsNullOrEmpty(c.B))
            {
                if (!TryResolveAgent(c.A, actorContext, out Agent e)) return false;
                var brain = AgentAIController.GetBrainForAgent(e);
                return brain != null && brain.IsInCombat;
            }
            // combat(a, b)：a 与 b 交战
            if (!TryResolveAgent(c.A, actorContext, out Agent a)) return false;
            if (!TryResolveAgent(c.B, actorContext, out Agent b)) return false;
            if (!a.IsActive() || !b.IsActive()) return false;
            bool fighting = false;
            try
            {
                var brainA = AgentAIController.GetBrainForAgent(a);
                var brainB = AgentAIController.GetBrainForAgent(b);
                bool aFight = brainA != null && brainA.IsInCombat;
                bool bFight = brainB != null && brainB.IsInCombat;
                bool targeting = a.GetTargetAgent() == b || b.GetTargetAgent() == a;
                bool enemyTeams = a.Team != null && a.Team.IsValid && b.Team != null && b.Team.IsValid
                    && a.Team.IsEnemyOf(b.Team);
                fighting = targeting || (aFight && bFight && enemyTeams);
            }
            catch { }
            return fighting;
        }

        private bool EvalPlayerAction(Condition c)
        {
            var main = Agent.Main;
            if (main == null) return false;
            string action = (c.A ?? "").ToLowerInvariant();
            bool result;
            switch (action)
            {
                case "crouch": result = main.CrouchMode; break;
                case "weapon_drawn":
                    {
                        result = false;
                        try { result = !main.WieldedWeapon.IsEmpty; }
                        catch { }
                        break;
                    }
                case "steal": result = Owner?.IsPlayerInModalUi == true; break;
                default: result = false; break;
            }
            return result;
        }

        private bool EvalTimeSince(Condition c)
        {
            if (string.IsNullOrEmpty(c.StepId)) return false;
            if (!StepCompleteTime.TryGetValue(c.StepId, out float t)) return false;
            float since = (Owner?.Elapsed ?? PlanExecutor.Instance?.Elapsed ?? 0f) - t;
            return CompareFloat(since, c.Op, c.Value);
        }

        private bool EvalDead(Condition c)
        {
            if (!TryResolveAgent(c.Entity ?? c.A, null, out Agent agent)) return true; // 无法解析 = 不在场
            return !agent.IsActive() || agent.Health <= 0f;
        }

        private bool EvalKnockedOut(Condition c)
        {
            if (!TryResolveAgent(c.Entity ?? c.A, null, out Agent agent)) return false;
            return AgentBrain.IsKnockedOut(agent);
        }

        private bool EvalCount(Condition c, Agent actorContext)
        {
            // count(of query, op, value)：query 解析集合后计数
            if (c.Of == null) return false;
            var set = ResolveSet(c.Of, actorContext);
            if (set == null) return false;
            int count = set.Count;
            return CompareFloat(count, c.Op, c.Value);
        }

        // ═══════════════════════════════════════════════════════════
        // 实体解析
        // ═══════════════════════════════════════════════════════════

        /// <summary>解析实体引用为 Agent。self = actorContext（null 时 = OwnerAgent）。</summary>
        public bool TryResolveAgent(string refName, Agent actorContext, out Agent agent)
        {
            agent = null;
            if (string.IsNullOrEmpty(refName)) return false;

            if (refName == "self")
            {
                agent = actorContext ?? OwnerAgent;
                return agent != null;
            }
            if (refName == "player")
            {
                agent = Agent.Main;
                return agent != null;
            }
            if (RoleAgents.TryGetValue(refName, out agent) && agent != null)
                return true;
            // 快照兜底（角色未注册时按显示名/职业找）
            if (Snapshot != null)
            {
                var info = Snapshot.FindAgent(refName);
                if (info?.Agent != null)
                {
                    agent = info.Agent;
                    RoleAgents[refName] = agent;
                    return true;
                }
            }
            return false;
        }

        /// <summary>解析引用为坐标：agent / 具名位置 / 物件 / zone / query。</summary>
        public bool TryResolvePosition(string refName, Agent actorContext, out Vec3 pos)
        {
            pos = Vec3.Zero;
            if (string.IsNullOrEmpty(refName)) return false;

            if (TryResolveAgent(refName, actorContext, out Agent agent))
            {
                pos = agent.Position;
                return true;
            }
            if (NamedPositions.TryGetValue(refName, out pos))
                return true;
            if (Snapshot != null)
            {
                var obj = Snapshot.FindObject(refName);
                if (obj != null)
                {
                    pos = SceneSnapshot.GetMissionObjectPosition(obj.MissionObject);
                    NamedObjects[refName] = obj.MissionObject;
                    NamedPositions[refName] = pos;
                    return true;
                }
                var zone = Snapshot.FindZone(refName);
                if (zone != null)
                {
                    pos = zone.Position;
                    NamedPositions[refName] = pos;
                    NamedZoneRadii[refName] = zone.Radius;
                    return true;
                }
            }
            // query 动态求值（求值一次注册具名）
            if (refName.StartsWith("nearest_enemy(") || refName.StartsWith("all_in(")
                || refName.StartsWith("hidden_spot(") || refName.StartsWith("lure_spot(")
                || refName.StartsWith("stand_spot(") || refName.StartsWith("zone(")
                || refName.StartsWith("point("))
            {
                if (ResolveQuery(refName, actorContext, out pos))
                {
                    NamedPositions[refName] = pos;
                    return true;
                }
            }
            return false;
        }

        /// <summary>解析区域（zone/具名位置）为 位置+半径。区域名找不到 → 回落物件匹配（gate/village 等按物件定位）。</summary>
        public bool TryResolveZone(string refName, out Vec3 pos, out float radius)
        {
            pos = Vec3.Zero;
            radius = 5f;
            if (string.IsNullOrEmpty(refName)) return false;
            if (NamedZoneRadii.TryGetValue(refName, out radius) && NamedPositions.TryGetValue(refName, out pos))
                return true;
            if (Snapshot != null)
            {
                var zone = Snapshot.FindZone(refName);
                if (zone != null)
                {
                    pos = zone.Position;
                    radius = zone.Radius;
                    NamedPositions[refName] = pos;
                    NamedZoneRadii[refName] = radius;
                    return true;
                }
                // 回落：物件名匹配（如 gate 门、village 无则诚实失败）
                var obj = Snapshot.FindObject(refName);
                if (obj != null)
                {
                    pos = SceneSnapshot.GetMissionObjectPosition(obj.MissionObject);
                    radius = 8f;
                    NamedPositions[refName] = pos;
                    NamedZoneRadii[refName] = radius;
                    return true;
                }
            }
            return false;
        }

        /// <summary>解析集合（count/nearest_enemy/all_in 用）。返回 null = 解析失败。</summary>
        public List<Agent> ResolveSet(JToken token, Agent actorContext)
        {
            string query = null;
            if (token == null) return null;
            if (token.Type == JTokenType.String) query = token.Value<string>();
            else if (token.Type == JTokenType.Object)
            {
                var q = token["query"];
                if (q != null && q.Type == JTokenType.String) query = q.Value<string>();
            }
            if (string.IsNullOrEmpty(query)) return null;
            return ResolveAgentSet(query, actorContext);
        }

        /// <summary>解析 agent 集合（query 求值）。</summary>
        public List<Agent> ResolveAgentSet(string query, Agent actorContext)
        {
            var result = new List<Agent>();
            if (string.IsNullOrEmpty(query)) return result;
            try
            {
                if (query.StartsWith("all_in("))
                {
                    string zoneName = ExtractArg(query, "all_in");
                    if (!TryResolveZone(zoneName, out Vec3 zonePos, out float radius)) return null;
                    foreach (var a in Mission.Current.Agents)
                    {
                        if (a == null || !a.IsActive() || !AgentControlHelper.IsHumanOrChild(a)) continue;
                        if (a.Position.Distance(zonePos) > radius) continue;
                        result.Add(a);
                    }
                    // 过滤规则（intent.filter: exclude_allies 默认）
                    string filter = Owner?.Plan?.Intent?.Filter ?? "exclude_allies";
                    if (filter == "exclude_allies")
                        result.RemoveAll(a => a == OwnerAgent || AgentBrain.IsPlayerTeammate(a));
                    return result;
                }
                if (query.StartsWith("nearest_enemy("))
                {
                    // 候选 = all_in(zone) 或全体非队友；自执行者取最近
                    string zoneArg = ExtractArg(query, "nearest_enemy");
                    List<Agent> candidates = null;
                    if (!string.IsNullOrEmpty(zoneArg) && !zoneArg.Equals("self", StringComparison.OrdinalIgnoreCase))
                        candidates = ResolveAgentSet($"all_in({zoneArg})", actorContext);
                    if (candidates == null)
                    {
                        candidates = new List<Agent>();
                        foreach (var a in Mission.Current.Agents)
                        {
                            if (a == null || !a.IsActive() || !AgentControlHelper.IsHumanOrChild(a)) continue;
                            if (a == OwnerAgent || AgentBrain.IsPlayerTeammate(a)) continue;
                            candidates.Add(a);
                        }
                    }
                    var self = actorContext ?? OwnerAgent;
                    if (self == null) return result;
                    return candidates
                        .OrderBy(a => a.Position.DistanceSquared(self.Position))
                        .Take(1)
                        .ToList();
                }
            }
            catch { }
            return result;
        }

        /// <summary>动态空间查询（§5.0）：求值一次，调用方负责注册具名。</summary>
        public bool ResolveQuery(string query, Agent actorContext, out Vec3 pos)
        {
            pos = Vec3.Zero;
            if (string.IsNullOrEmpty(query)) return false;
            try
            {
                var self = actorContext ?? OwnerAgent;
                var mission = Mission.Current;
                if (mission == null) return false;

                if (query.StartsWith("zone(") || query.StartsWith("point("))
                {
                    string name = query.Substring(query.IndexOf('(') + 1);
                    name = name.Substring(0, name.Length - 1).Trim();
                    if (Snapshot != null)
                    {
                        var zone = Snapshot.FindZone(name);
                        if (zone != null) { pos = zone.Position; return true; }
                        var obj = Snapshot.FindObject(name);
                        if (obj != null) { pos = SceneSnapshot.GetMissionObjectPosition(obj.MissionObject); return true; }
                    }
                    return false; // 场景无此地 → 诚实报告（不瞎带路）
                }

                if (query.StartsWith("hidden_spot(") || query.StartsWith("lure_spot("))
                {
                    bool isLure = query.StartsWith("lure_spot(");
                    string inner = query.Substring(query.IndexOf('(') + 1).TrimEnd(')');
                    var parts = inner.Split(',');
                    string nearRef = parts.Length > 0 ? parts[0].Trim() : null;
                    float minDist = parts.Length > 1 && float.TryParse(parts[1].Trim(), out float md) ? md : 12f;
                    if (!TryResolvePosition(nearRef, actorContext, out Vec3 nearPos)) return false;
                    var player = Agent.Main;

                    // 采样候选点：12 方向 × 2 距离环（8~15m），navmesh 可达 + 无 agent + 距离约束
                    var rng = new Random();
                    var candidates = new List<Vec3>();
                    for (int ring = 0; ring < 2; ring++)
                    {
                        float dist = 9f + ring * 6f;
                        for (int i = 0; i < 12; i++)
                        {
                            float ang = i * (MathF.PI * 2f / 12f) + rng.Next(0, 60) * 0.01f;
                            var cand = nearPos + new Vec3(MathF.Cos(ang) * dist, MathF.Sin(ang) * dist, nearPos.z);
                            if (IsNavmeshReachable(cand)) candidates.Add(cand);
                        }
                    }
                    foreach (var cand in candidates)
                    {
                        if (cand.Distance(nearPos) < minDist) continue;
                        if (isLure && player != null && cand.Distance(player.Position) < 8f) continue; // 不把守卫引到玩家埋伏点
                        // 半径 5m 内无其他活跃 agent
                        bool clear = true;
                        foreach (var a in mission.Agents)
                        {
                            if (a == null || !a.IsActive()) continue;
                            if (a.Position.Distance(cand) < 5f) { clear = false; break; }
                        }
                        if (!clear) continue;
                        pos = cand;
                        return true;
                    }
                    return false;
                }

                if (query.StartsWith("stand_spot("))
                {
                    string inner = query.Substring(query.IndexOf('(') + 1).TrimEnd(')');
                    var parts = inner.Split(',');
                    string targetRef = parts.Length > 0 ? parts[0].Trim() : null;
                    string anchorRef = parts.Length > 1 ? parts[1].Trim() : null;
                    if (!TryResolveAgent(targetRef, actorContext, out Agent target)) return false;
                    if (!TryResolvePosition(anchorRef, actorContext, out Vec3 anchor)) return false;
                    // 目标对侧：anchor → target 延长 2.5m
                    Vec2 dir = (target.Position.AsVec2 - anchor.AsVec2).Normalized();
                    if (dir.LengthSquared < 0.01f) dir = target.LookDirection.AsVec2.Normalized();
                    var spot = target.Position + dir.ToVec3(0f) * 2.5f;
                    if (!IsNavmeshReachable(spot)) return false;
                    pos = spot;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private bool IsNavmeshReachable(Vec3 pos)
        {
            var mission = Mission.Current;
            if (mission == null) return false;
            try
            {
                return V.NavMesh(mission.Scene, pos, out int _);
            }
            catch { return false; }
        }

        private static string ExtractArg(string query, string fn)
        {
            string inner = query.Substring(query.IndexOf('(') + 1);
            return inner.Substring(0, inner.Length - 1).Trim();
        }

        // ═══════════════════════════════════════════════════════════
        // 工具
        // ═══════════════════════════════════════════════════════════

        private static bool ApplyBoolOp(bool value, string op)
        {
            if (string.IsNullOrEmpty(op) || op == "true") return value;
            if (op == "false") return !value;
            return value;
        }

        private static bool CompareFloat(float actual, string op, float target)
        {
            switch (op)
            {
                case ">": return actual > target;
                case "<": return actual < target;
                case ">=": return actual >= target;
                case "<=": return actual <= target;
                case "==":
                case "=": return MathF.Abs(actual - target) < 0.001f;
                default: return actual > target;
            }
        }

        private static int ParsePhase(string phase)
        {
            if (string.IsNullOrEmpty(phase)) return 3;
            switch (phase.ToLowerInvariant())
            {
                case "normal": return 0;
                case "suspicious": return 1;
                case "cautious": return 2;
                case "alarmed": return 3;
                default: return 3;
            }
        }

        private static int PhaseValue(AlarmPhase phase)
        {
            switch (phase)
            {
                case AlarmPhase.Normal: return 0;
                case AlarmPhase.Suspicious: return 1;
                case AlarmPhase.Cautious: return 2;
                case AlarmPhase.Alarmed: return 3;
                default: return 0;
            }
        }

        /// <summary>条件规范化键（sustained/was 状态追踪用）。</summary>
        public static string ConditionKey(Condition c)
        {
            if (c == null) return "<null>";
            var sb = new StringBuilder();
            AppendCondition(sb, c);
            return sb.ToString();
        }

        private static void AppendCondition(StringBuilder sb, Condition c)
        {
            sb.Append(c.Type).Append('(');
            if (c.A != null) sb.Append(c.A);
            sb.Append(',');
            if (c.B != null) sb.Append(c.B);
            sb.Append(',').Append(c.Op).Append(',').Append(c.Value);
            if (c.Entity != null) sb.Append(",e:").Append(c.Entity);
            if (c.Phase != null) sb.Append(",p:").Append(c.Phase);
            if (c.StepId != null) sb.Append(",s:").Append(c.StepId);
            if (c.SustainedS > 0f) sb.Append(",su:").Append(c.SustainedS);   // sustained 纳入 key（同条件不同防抖独立计时）
            if (c.Conditions != null)
            {
                sb.Append(",[");
                foreach (var sub in c.Conditions) { AppendCondition(sb, sub); sb.Append(';'); }
                sb.Append(']');
            }
            sb.Append(')');
        }

        /// <summary>设置步骤完成时间（PlanExecutor 调）。</summary>
        public void MarkStepComplete(string stepId, float elapsed)
        {
            if (!string.IsNullOrEmpty(stepId)) StepCompleteTime[stepId] = elapsed;
        }
    }

    /// <summary>JToken 包装：{"query": "..."} 或 string。由 PlanExecutor 反序列化时填充。</summary>
    public class JTokenWrapper
    {
        public string Query;
        public string Ref;

        public JTokenWrapper() { }
        public JTokenWrapper(string query) { Query = query; }
    }
}
