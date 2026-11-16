using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.Engine;
using TaleWorlds.DotNet;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// P3（2026-11-15）准备批次：按 `story_stage/2` 的唯一布局生成布景、摆放演员，并逐项报告 R1～R7。
    ///
    /// 🔴 纪律：
    ///   · 几何只有布局一份 —— v1 台本里同 id 的 `prop_spawn` 会被跳过（`StoryStageRunner` 查 `HasProp`）。
    ///   · 失败的必需项**不忽略**：结果为 `missing`/`prefab_not_loaded`，等 Timeout 后置 `Failed`。
    ///   · 本类只做“进场准备”（发生在开场镜头下），不负责工作走位；工作动作归 `StoryWorkDirector`。
    /// </summary>
    public sealed class StoryPrepRunner
    {
        private readonly StoryProbeMissionBehavior _host;
        private readonly Dictionary<string, GameEntity> _props = new Dictionary<string, GameEntity>(StringComparer.Ordinal);
        private readonly Dictionary<string, Vec3> _standPos = new Dictionary<string, Vec3>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _results = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _requiredProps = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _requiredActors = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _placed = new HashSet<string>(StringComparer.Ordinal);
        private bool _skipRequested;
        private StoryStageDef _stage;
        private StoryLayoutDef _layout;
        private string _stageId = "";
        private float _elapsed;
        private bool _started;
        private bool _spawnPending;
        public Func<bool> HoldSpawning;
        private bool _ready;
        private bool _failed;
        private string _reason = "";

        public bool Ready => _ready;
        public bool Failed => _failed;
        public string Reason => _reason;
        public string StageId => _stageId;

        public StoryPrepRunner(StoryProbeMissionBehavior host) { _host = host; }

        public bool HasProp(string id) => !string.IsNullOrEmpty(id) && _props.ContainsKey(id)
                                          && _props[id] != null;

        public bool TryGetPropEntity(string id, out GameEntity entity)
        {
            entity = null;
            return !string.IsNullOrEmpty(id) && _props.TryGetValue(id, out entity) && entity != null;
        }
        /// <summary>某演员在本场布局里的站位（岗位/工作点）—— 职责点的数据来源。</summary>
        public bool TryGetStandingPosition(string actorId, out Vec3 position)
            => _standPos.TryGetValue(actorId, out position);

        public bool HasActor(string id) => !string.IsNullOrEmpty(id) && _standPos.ContainsKey(id)
                                           && _host.GetAgent(id) != null;
        public bool PlansActor(string id) => _layout?.Standing?.Any(s => s.Actor == id) == true;

        public bool Begin(string stageId)
        {
            _stageId = stageId ?? "";
            _elapsed = 0f;
            _ready = false;
            _failed = false;
            _reason = "";
            _props.Clear();
            _standPos.Clear();
            _results.Clear();
            _requiredProps.Clear();
            _requiredActors.Clear();
            _placed.Clear();
            _skipRequested = false;
            _started = false;
            _spawnPending = false;

            if (!StoryV2Data.EnsurePublished())
            {
                _reason = "no published v2 data: " + StoryV2Data.Report;
                _failed = true;
                return false;
            }
            var file = StoryLayoutRegistry.Published;
            if (file == null || !file.Stages.TryGetValue(_stageId, out _stage) || _stage == null)
            {
                _reason = "stage missing: " + _stageId;
                _failed = true;
                return false;
            }
            if (!file.Layouts.TryGetValue(_stage.Layout, out _layout) || _layout == null)
            {
                _reason = "layout missing: " + _stage.Layout;
                _failed = true;
                return false;
            }
            string scene = Mission.Current?.SceneName;
            if (!string.IsNullOrEmpty(file.Scene) && !string.IsNullOrEmpty(scene)
                && !string.Equals(file.Scene, scene, StringComparison.OrdinalIgnoreCase))
            {
                _reason = "scene mismatch data=" + file.Scene + " mission=" + scene;
                _failed = true;
                return false;
            }

            _started = true;
            _spawnPending = true;
            if (HoldSpawning == null || !HoldSpawning()) Materialize();
            return true;
        }

        private void Materialize()
        {
            var anchors = ResolveAnchors();
            SpawnProps(anchors);
            SpawnAndPlaceActors(anchors);
            _spawnPending = false;
            DebugLogger.Log("[StoryPrep] begin stage=" + _stageId + " props=" + _props.Count
                            + " actors=" + _standPos.Count + " results=" + _results.Count);
            EvaluateReadiness();
        }

        public void Tick(float dt)
        {
            if (!_started || _ready || _failed) return;
            if (_spawnPending)
            {
                if (HoldSpawning?.Invoke() == true)
                {
                    _elapsed += dt;
                    if (_elapsed > StoryProbeTuning.Current.Performance.ActorReadyTimeoutSec)
                    {
                        _failed = true;
                        _reason = "opening camera not ready; scenery withheld";
                    }
                    return;
                }
                Materialize();
            }
            _elapsed += dt;
            EvaluateReadiness();
        }

        public void Skip()
        {
            if (!_started || _ready) return;
            if (_spawnPending)
            {
                // 明确执行跳过命令时立即落实准备；自然进场仍遵守镜头遮挡闸门。
                Materialize();
            }
            // 准备期跳过 = **立刻把布景/摆人做完并当场判定**，不再等超时；
            // 必需项没齐就当场失败（不能一点就 ready，也不能照常开演）。
            _skipRequested = true;
            DebugLogger.Log("[StoryPrep] skip requested; evaluating readiness now");
            EvaluateReadiness();
            if (!_ready && !_failed)
            {
                _failed = true;
                _reason = "skip requested before ready (elapsed " + _elapsed.ToString("F1") + "s)";
                DebugLogger.Log("[StoryPrep] FAILED by skip: " + _reason);
            }
        }

        public void Stop(string reason)
        {
            if (_started) DebugLogger.Log("[StoryPrep] stop: " + (reason ?? "-"));
            _started = false;
        }

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append(_failed ? "FAILED: " : _ready ? "OK: ready " : "PENDING: ")
              .Append(_stageId).Append(" elapsed=").Append(_elapsed.ToString("F1"))
              .Append(" props=").Append(_props.Count).Append(" actors=").Append(_standPos.Count);
            if (!string.IsNullOrEmpty(_reason)) sb.Append(" reason=").Append(_reason);
            foreach (var kv in _results.OrderBy(k => k.Key, StringComparer.Ordinal))
                sb.Append("\n  ").Append(kv.Key).Append(" = ").Append(kv.Value);
            return sb.ToString();
        }

        // ───────────────────────── 锚点 ─────────────────────────

        /// <summary>
        /// 找场景实体（两轮策略，铁律 5）：① 按场景命名 `FindEntityWithName`；
        /// ② 按 **prefab 名**扫一遍（城门这类实体是 prefab 实例，场景命名不一定等于 prefab 名
        ///   —— 2026-11-16 实机：第二次进城 prep 因 `gate_entity` 判缺失而整场取消，就是这一步没兜底）；
        /// ③ 再退到 tag 同名。
        /// </summary>
        private static GameEntity FindSceneEntity(Scene scene, string name)
        {
            if (scene == null || string.IsNullOrWhiteSpace(name)) return null;
            try { var e = scene.FindEntityWithName(name); if (e != null) return e; } catch { }
            try
            {
                var roots = NativeObjectArray.Create();
                scene.GetRootEntities(roots);
                foreach (NativeObject obj in roots)
                {
                    if (!(obj is GameEntity root)) continue;
                    var hit = FindByPrefabRecursive(root, name, 0);
                    if (hit != null) return hit;
                }
            }
            catch { }
            try
            {
                var list = scene.FindEntitiesWithTag(name);
                if (list != null)
                    foreach (var e in list)
                        if (e != null) return e;
            }
            catch { }
            return null;
        }

        private static GameEntity FindByPrefabRecursive(GameEntity entity, string name, int depth)
        {
            if (entity == null || depth > 6) return null;
            // 🔴 两个名字都要认（2026-11-16 实机）：`GetPrefabName()` 对"场景加载后被展开的 prefab 实例"
            //    常常返回空，而 `entity.Name` 才是引擎给实例的默认名（`StealManager` 找储物道具用的就是它）。
            //    只比 GetPrefabName 会让 `gate_entity` 这类锚点永远 missing。
            string prefab = null, assigned = null;
            try { prefab = entity.GetPrefabName(); } catch { }
            try { assigned = entity.Name; } catch { }
            if (string.Equals(prefab, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(assigned, name, StringComparison.OrdinalIgnoreCase)) return entity;
            if (_prefabProbe.Count < 12 && !string.IsNullOrEmpty(assigned)) _prefabProbe.Add(assigned);
            int children = 0;
            try { children = entity.ChildCount; } catch { }
            for (int i = 0; i < children; i++)
            {
                GameEntity child = null;
                try { child = entity.GetChild(i); } catch { }
                var hit = FindByPrefabRecursive(child, name, depth + 1);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>首次查找失败时把扫到的实体名样本写进日志，省得再猜一轮。</summary>
        private static readonly List<string> _prefabProbe = new List<string>();

        private Dictionary<string, Vec3> ResolveAnchors()
        {
            var anchors = new Dictionary<string, Vec3>(StringComparer.Ordinal)
            {
                ["player"] = _host.PlayerAnchor
            };
            var scene = Mission.Current?.Scene;
            foreach (var kv in _layout.Anchors)
            {
                var def = kv.Value;
                if (def == null) continue;
                if (string.Equals(def.Source, "player", StringComparison.OrdinalIgnoreCase))
                {
                    anchors[kv.Key] = _host.PlayerAnchor;
                    _results["anchor:" + kv.Key] = "ok (player)";
                    continue;
                }
                GameEntity found = null;
                if (string.Equals(def.Source, "entity", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string name in def.Value ?? new string[0])
                    {
                        found = FindSceneEntity(scene, name);
                        if (found != null) break;
                    }
                }
                else
                {
                    IEnumerable<GameEntity> list = null;
                    foreach (string tag in def.Value ?? new string[0])
                    {
                        try { list = scene?.FindEntitiesWithTag(tag); } catch { }
                        if (list == null) continue;
                        if (string.Equals(def.Pick, "nearestToPlayer", StringComparison.OrdinalIgnoreCase))
                            found = list.Where(e => e != null)
                                        .OrderBy(e => e.GlobalPosition.Distance(_host.PlayerAnchor))
                                        .FirstOrDefault();
                        else
                            found = list.FirstOrDefault(e => e != null);
                        if (found != null) break;
                    }
                }

                if (found != null)
                {
                    anchors[kv.Key] = found.GlobalPosition;
                    _results["anchor:" + kv.Key] = "ok (" + def.Source + ")";
                }
                else
                {
                    anchors[kv.Key] = _host.PlayerAnchor;
                    _results["anchor:" + kv.Key] = def.Required ? "missing" : "optional-missing";
                    // 缺锚点时把扫到的实体名样本写出来 —— 一眼就能看出是"名字对不上"还是"压根没扫到"。
                    if (string.Equals(def.Source, "entity", StringComparison.OrdinalIgnoreCase))
                        DebugLogger.Log($"[StoryPrep] anchor '{kv.Key}' 未命中 [{(def.Value == null ? "" : string.Join("|", def.Value))}]"
                            + "；场景实体名样本: " + (_prefabProbe.Count == 0 ? "(扫描为空)" : string.Join(", ", _prefabProbe)));
                }
            }
            // 与 v1 台本/老锚点语义对齐：`gate` = 守门人位（BlockerPos），不是门洞中心。
            anchors["gate"] = _host.BlockerPos;
            anchors["gate_inner"] = _host.GoalPos;
            anchors["player"] = _host.PlayerAnchor;
            return anchors;
        }

        private Vec3 PointPos(Dictionary<string, Vec3> anchors, StoryPoint p)
        {
            if (p == null) return _host.PlayerAnchor;
            Vec3 origin = _host.PlayerAnchor;
            if (!string.IsNullOrEmpty(p.Anchor) && anchors.TryGetValue(p.Anchor, out var a)) origin = a;
            var f = _host.Forward;
            return origin
                   + new Vec3(f.x, f.y, 0f) * p.Fwd
                   + new Vec3(f.y, -f.x, 0f) * p.Right
                   + new Vec3(0f, 0f, p.Up);
        }

        // ───────────────────────── 布景 ─────────────────────────

        private void SpawnProps(Dictionary<string, Vec3> anchors)
        {
            foreach (var prop in _layout.Props)
            {
                if (prop == null || string.IsNullOrEmpty(prop.Id)) continue;
                if (prop.Required) _requiredProps.Add(prop.Id);
                if (_props.ContainsKey(prop.Id)) continue;

                if (string.Equals(prop.Source, "existing", StringComparison.OrdinalIgnoreCase))
                {
                    GameEntity existing = null;
                    foreach (string tag in prop.EntityTag ?? new string[0])
                    {
                        try { existing = Mission.Current?.Scene?.FindEntitiesWithTag(tag)?.FirstOrDefault(e => e != null); } catch { }
                        if (existing != null) break;
                    }
                    if (existing != null) { _props[prop.Id] = existing; _results["prop:" + prop.Id] = "ok (existing)"; }
                    else _results["prop:" + prop.Id] = "missing (existing)";
                    continue;
                }

                if (string.IsNullOrEmpty(prop.Prefab) || !GameEntity.PrefabExists(prop.Prefab))
                {
                    _results["prop:" + prop.Id] = "prefab_not_loaded: " + (prop.Prefab ?? "-");
                    continue;
                }
                try
                {
                    Vec3 pos = PointPos(anchors, prop.Place);
                    float requestedZ = pos.z;
                    if (prop.Support == "ground")
                    {
                        var probe = pos + new Vec3(0f, 0f, 5f);
                        float ground = V.GroundHeight(Mission.Current.Scene, probe);
                        if (float.IsNaN(ground) || float.IsInfinity(ground) || ground > probe.z
                            || Math.Abs(ground - requestedZ) > 5f)
                        {
                            _results["prop:" + prop.Id] = "ground_unavailable";
                            continue;
                        }
                        pos.z = ground + prop.Place.Up;
                    }
                    float rad = prop.Yaw * (float)Math.PI / 180f;
                    var fwd = new Vec3((float)Math.Cos(rad), (float)Math.Sin(rad), 0f);
                    Mat3 rot = Mat3.CreateMat3WithForward(fwd);
                    float scale = prop.Scale <= 0f ? 1f : prop.Scale;
                    if (Math.Abs(scale - 1f) > 0.001f) rot = rot * scale;
                    var entity = GameEntity.Instantiate(Mission.Current.Scene, prop.Prefab, new MatrixFrame(rot, pos));
                    if (entity != null)
                    {
                        _props[prop.Id] = entity;
                        _results["prop:" + prop.Id] = "ok (prefab) at " + pos;
                        DebugLogger.Log($"[StoryPrep] prop created: id={prop.Id} prefab={prop.Prefab} requestedZ={requestedZ:F2} support={prop.Support} actual={pos}");
                    }
                    else _results["prop:" + prop.Id] = "spawn_failed";
                }
                catch (Exception ex)
                {
                    _results["prop:" + prop.Id] = "error: " + ex.Message;
                }
            }
        }

        // ───────────────────────── 演员 ─────────────────────────

        private void SpawnAndPlaceActors(Dictionary<string, Vec3> anchors)
        {
            var byActor = new Dictionary<string, StoryStandingDef>(StringComparer.Ordinal);
            foreach (var s in _layout.Standing)
                if (s != null && !string.IsNullOrEmpty(s.Actor)) byActor[s.Actor] = s;

            foreach (string actor in _stage.Actors.Concat(_stage.OptionalActors).Distinct())
            {
                var def = StoryProbeDefs.GetNpc(actor);
                if (def == null) { _results["actor:" + actor] = "no cast def"; continue; }
                bool required = _stage.Actors.Contains(actor);
                if (required) _requiredActors.Add(actor);

                Vec3 pos = StandingPos(anchors, byActor.TryGetValue(actor, out var standing) ? standing : null, actor);
                _standPos[actor] = pos;

                var agent = _host.EnsureStageActor(actor, pos, def.Civilian);
                if (agent == null)
                {
                    _results["actor:" + actor] = required ? "missing" : "optional-missing";
                    continue;
                }
                try
                {
                    agent.TeleportToPosition(pos);
                    _placed.Add(actor);
                    _results["actor:" + actor] = "ok at " + pos.ToString();
                }
                catch (Exception ex)
                {
                    _results["actor:" + actor] = "placement_failed: " + ex.Message;
                }
            }
        }

        private Vec3 StandingPos(Dictionary<string, Vec3> anchors, StoryStandingDef standing, string actor)
        {
            var pt = _layout.FindPoint(standing?.Point);
            if (pt == null) return _host.PlayerAnchor;
            if (!string.IsNullOrEmpty(pt.Prop))
            {
                var prop = _layout.FindProp(pt.Prop);
                if (prop == null || !_props.TryGetValue(prop.Id, out var entity) || entity == null)
                    return _host.PlayerAnchor;
                return WorkPos(entity, prop, pt);
            }
            return pt.Place.HasAbsolute
                ? new Vec3(pt.Place.Absolute[0], pt.Place.Absolute[1], pt.Place.Absolute[2])
                : PointPos(anchors, pt.Place);
        }

        /// <summary>工作点站位 = 家具位置 + 按家具朝向旋转的侧向偏移。</summary>
        public static Vec3 WorkPos(GameEntity propEntity, StoryPropDef prop, StoryPointDef wp)
        {
            Vec3 basePos = propEntity.GlobalPosition;
            float yaw = (prop?.Yaw ?? 0f) * (float)Math.PI / 180f;
            var fwd = new Vec3((float)Math.Cos(yaw), (float)Math.Sin(yaw), 0f);
            var right = new Vec3(fwd.y, -fwd.x, 0f);
            Vec3 dir;
            switch ((wp.Side ?? "front").ToLowerInvariant())
            {
                case "back": dir = fwd * -1f; break;
                case "left": dir = right * -1f; break;
                case "right": dir = right; break;
                default: dir = fwd; break;
            }
            return basePos + dir * wp.Offset;
        }

        /// <summary>
        /// 给玩家看的"这次缺什么"（空 = 不是缺项，多半是超时/异常）。
        /// 只列真正没过的项：`anchor:xxx` / `prop:xxx` / `actor:xxx`，可选缺失不算。
        /// </summary>
        public string DescribeMissing()
        {
            var parts = new List<string>();
            foreach (var kv in _results)
            {
                if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value)) continue;
                if (kv.Value.StartsWith("ok", StringComparison.Ordinal)) continue;
                if (kv.Value.StartsWith("optional-missing", StringComparison.Ordinal)) continue;
                parts.Add(kv.Key + " = " + kv.Value);
            }
            parts.Sort(StringComparer.Ordinal);
            return string.Join("; ", parts);
        }

        // ───────────────────────── 就绪 ─────────────────────────

        private void EvaluateReadiness()
        {
            if (_failed || _ready) return;

            // R1 数据/场景已在 Begin 检查
            // R2 必需锚点
            bool anchorsOk = _layout.Anchors.All(kv => kv.Value == null || !kv.Value.Required
                || (_results.TryGetValue("anchor:" + kv.Key, out var r) && r.StartsWith("ok", StringComparison.Ordinal)));
            // R3 必需道具
            bool propsOk = _requiredProps.All(id => HasProp(id));
            // R4 必需演员在场
            bool actorsOk = _requiredActors.All(id => _host.GetAgent(id) != null);
            // R5 摆放（每个必需演员都有目标点）
            // R5 摆放：每个必需演员都实际 Teleport 成功（异常记 placement_failed）
            bool placedOk = _requiredActors.All(id => _placed.Contains(id));
            // R6 控制就绪：激活、未死、Brain 已挂上
            bool controlOk = _requiredActors.All(id =>
            {
                var a = _host.GetAgent(id);
                return a != null && AgentControlHelper.SafeIsActive(a) && a.Health > 0f
                       && AgentAIController.GetBrainForAgent(a) != null;
            });
            // R7 支撑/空间：道具 support/footprint 合法；演员站位之间不重叠
            bool spaceOk = true;
            foreach (var prop in _layout.Props)
            {
                if (prop == null || !prop.Required) continue;
                if (string.IsNullOrEmpty(prop.Support) || prop.Footprint == null || prop.Footprint.Length < 2)
                { spaceOk = false; break; }
                if (prop.Footprint[0] <= 0f || prop.Footprint[1] <= 0f) { spaceOk = false; break; }
            }
            if (spaceOk)
            {
                var ids = _requiredActors.Where(id => _standPos.ContainsKey(id)).ToList();
                for (int i = 0; i < ids.Count && spaceOk; i++)
                    for (int j = i + 1; j < ids.Count; j++)
                        if (_standPos[ids[i]].Distance(_standPos[ids[j]]) < 0.35f)
                        { spaceOk = false; break; }
            }

            if (anchorsOk && propsOk && actorsOk && placedOk && controlOk && spaceOk)
            {
                _ready = true;
                DebugLogger.Log("[StoryPrep] ready stage=" + _stageId + " elapsed=" + _elapsed.ToString("F1"));
                return;
            }

            if (_elapsed > Math.Max(0.1f, _stage.Prep.ActorReadyTimeoutSec))
            {
                _failed = true;
                _reason = "timeout after " + _elapsed.ToString("F1") + "s"
                        + " anchorsOk=" + anchorsOk + " propsOk=" + propsOk
                        + " actorsOk=" + actorsOk + " placedOk=" + placedOk
                        + " controlOk=" + controlOk + " spaceOk=" + spaceOk
                        + " skipRequested=" + _skipRequested
                        // 失败时把每个锚点的解算结果打出来（缺哪个一眼可见，不用再猜）
                        + " | anchors: " + string.Join(", ", _layout.Anchors.Keys.Select(k =>
                            k + "=" + (_results.TryGetValue("anchor:" + k, out var r0) ? r0 : "unresolved")));
                DebugLogger.Log("[StoryPrep] FAILED stage=" + _stageId + ": " + _reason);
            }
        }
    }
}
