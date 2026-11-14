using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// **场景演出执行器**（2026-11-13 立）—— 按台本 `StoryStage.json` 逐条执行原子指令。
    ///
    /// 🔴 **复用接缝**：这一层将来会被 `Story/StoryEngine.cs` 顶掉（见 plans §17.10）。
    ///    换的时候只换这个类，**台本数据和原子名一个字都不用改**。
    ///
    /// 时序：v1 = **绝对时间**（每条的 `At` 秒）。所有原子都是瞬时的，
    ///       所以 `Async` / `Group` 字段接受但不生效 —— 等 `actor_enter`（走位，有耗时）落地再启用。
    ///
    /// 诊断：所有输出都带 `[StoryStage]`；每个原子执行都打一行，失败也打。
    /// </summary>
    public sealed class StoryStageRunner
    {
        private readonly StoryProbeMissionBehavior _host;

        private StageScript _script;
        private float _time;
        private int _next;
        private bool _running;
        private Agent _frozenPlayer;
        private readonly string _cameraOwner = "story-stage-" + Guid.NewGuid().ToString("N");
        private readonly Mission _mission = Mission.Current;
        private string _actorOwner;
        private string _actorInterrupted;
        private float _readyWait;
        private readonly Dictionary<string, PerformanceAction> _actors = new Dictionary<string, PerformanceAction>();
        // 布景归场景，不归一次演出；Stop/重播保留，Mission 移除只清托管引用。
        private readonly Dictionary<string, GameEntity> _props = new Dictionary<string, GameEntity>();

        /// <summary>语义位解算结果（每次 Begin 重算一次，运行期不变）。</summary>
        private readonly Dictionary<string, Vec3> _anchors = new Dictionary<string, Vec3>();

        public StoryStageRunner(StoryProbeMissionBehavior host)
        {
            _host = host;
        }

        public bool Active => _running;
        public int BeatCount => _script?.Beats?.Count ?? 0;
        public int NextIndex => _next;
        public float Time => _time;
        public IEnumerable<string> AnchorNames => _anchors.Keys;

        // ═══════════════════════════════════════════════════════════════
        // 起演 / 逐帧
        // ═══════════════════════════════════════════════════════════════

        /// <summary>加载台本 + 解算语义位，从第 0 拍开始播。</summary>
        public bool Begin(string scriptName = "StoryStage")
        {
            Stop("restart");
            _script = StoryStageDefs.Load(scriptName);
            if (_script == null)
            {
                DebugLogger.Log("[StoryStage] Begin failed: script not loaded");
                return false;
            }

            ResolveAllAnchors();
            _time = 0f;
            _next = 0;
            _readyWait = 0f;
            _actorInterrupted = null;
            _actorOwner = "stage-" + Guid.NewGuid().ToString("N");
            _running = true;
            if (!AcquireActors()) { Stop("actor group unavailable"); return false; }
            DebugLogger.Log($"[StoryStage] begin '{_script.Script ?? scriptName}' beats={BeatCount} anchors={_anchors.Count}");
            return true;
        }

        /// <summary>控制台单发一个原子（不依赖台本、不动播放游标）。</summary>
        public void RunAtom(StageBeat beat)
        {
            if (beat == null) return;
            if (_script == null) _script = StoryStageDefs.Load("StoryStage");
            if (_anchors.Count == 0) ResolveAllAnchors();
            if (!string.IsNullOrEmpty(beat.Actor) && beat.Cmd != null && beat.Cmd.StartsWith("actor_"))
            {
                var anchors = _script?.Anchors;
                Stop("manual actor restart");
                _script = new StageScript { Script = "manual_actor", Anchors = anchors,
                    Beats = new List<StageBeat> { beat, new StageBeat { At = beat.At + Math.Max(StoryProbeTuning.Current.Performance.DebugActorDurationSec, beat.Seconds), Cmd = "wait" } } };
                _time = 0f;
                _next = 0;
                _readyWait = 0f;
                _actorInterrupted = null;
                _actorOwner = "stage-" + Guid.NewGuid().ToString("N");
                _running = true;
                if (!AcquireActors()) Stop("manual actor unavailable");
            }
            else Execute(beat, -1);
        }

        /// <summary>台本里有没有给这个角色安排站位（有的话，旧的硬编码偏移就不要再生成他）。</summary>
        public bool ScriptPlacesActor(string npcId)
        {
            if (string.IsNullOrEmpty(npcId) || _script?.Beats == null) return false;
            foreach (var b in _script.Beats)
                if (b.Cmd == "actor_place" && b.Actor == npcId) return true;
            return false;
        }

        public void Stop(string reason)
        {
            bool hadResources = _running || _frozenPlayer != null || CameraService.IsHeldBy(_cameraOwner);
            _running = false;
            var leases = _actors.Values.ToList();
            _actors.Clear();
            foreach (var lease in leases) lease.Release(reason);
            _actorInterrupted = null;
            ReturnCamera();
            try { FreezePlayer(false); }
            catch (Exception ex) { DebugLogger.Log($"[StoryStage] player release failed during stop: {ex.Message}"); }
            if (hadResources)
                DebugLogger.Log($"[StoryStage] stopped: {reason} (at {_time:F2}s, next={_next}/{BeatCount})");
        }

        private bool AcquireActors()
        {
            if (_script?.Beats == null || Mission.Current != _mission) return false;
            foreach (string id in _script.Beats.Where(b => b != null && !string.IsNullOrEmpty(b.Actor)
                && b.Cmd != null && b.Cmd.StartsWith("actor_")).Select(b => b.Actor).Distinct())
            {
                var place = _script.Beats.FirstOrDefault(b => b != null && b.Cmd == "actor_place" && b.Actor == id);
                var def = StoryProbeDefs.GetNpc(id);
                Vec3 initial = place != null ? PlaceVec(place) : _host.PlayerAnchor;
                var agent = _host.EnsureStageActor(id, initial, place?.Civilian ?? def?.Civilian ?? true);
                string session = _actorOwner;
                var lease = PerformanceAction.TryAcquire(agent, session, reason =>
                {
                    if (_running && _actorOwner == session) _actorInterrupted = id + ":" + reason;
                });
                if (lease == null) return false; // Begin/RunAtom 的 Stop 撤销此前已经接受的整组。
                _actors[id] = lease;
            }
            return true;
        }

        public void DisposeManaged()
        {
            _running = false;
            foreach (var lease in _actors.Values.ToList()) lease.InvalidateManaged();
            _actors.Clear();
            _props.Clear();
            _anchors.Clear();
            _frozenPlayer = null;
            _script = null;
        }

        private bool OwnsActor(string id)
            => !string.IsNullOrEmpty(id) && _actors.TryGetValue(id, out var lease) && lease.CanControl;

        public void Tick(float dt)
        {
            if (!_running) return;
            if (Mission.Current != _mission) { DisposeManaged(); return; }
            if (_actorInterrupted != null || _actors.Values.Any(a => a.Ended))
            {
                Stop("actor interrupted: " + _actorInterrupted);
                return;
            }
            if (_actors.Values.Any(a => !a.CanControl))
            {
                _readyWait += dt;
                if (_readyWait > StoryProbeTuning.Current.Performance.ActorReadyTimeoutSec) Stop("actor readiness timeout");
                return; // 演员尚未 OnStart：不推进台本、不冻结玩家、不写演员身体。
            }
            _time += dt;

            while (_running && _next < BeatCount && _script.Beats[_next].At <= _time)
            {
                Execute(_script.Beats[_next], _next);
                _next++;
            }

            if (_next >= BeatCount) Stop("script end");
        }

        /// <summary>只播某一条（调试用，不动播放游标）。</summary>
        public bool RunSingle(int index)
        {
            if (_script == null) _script = StoryStageDefs.Load("StoryStage");
            if (_script == null) return false;
            if (index < 0 || index >= BeatCount)
            {
                DebugLogger.Log($"[StoryStage] RunSingle out of range: {index} (0..{BeatCount - 1})");
                return false;
            }
            if (_anchors.Count == 0) ResolveAllAnchors();
            RunAtom(_script.Beats[index]);
            return true;
        }

        /// <summary>跳过演出：把玩家和相机放回去。</summary>
        public void Skip()
        {
            _next = BeatCount;
            Stop("skipped by command");
        }

        // ═══════════════════════════════════════════════════════════════
        // 原子
        // ═══════════════════════════════════════════════════════════════

        private void Execute(StageBeat b, int index)
        {
            if (b == null) return;
            try
            {
                switch (b.Cmd)
                {
                    case "player_freeze":  FreezePlayer(true); break;
                    case "player_release": FreezePlayer(false); break;
                    case "camera_cut":     CutCamera(b); break;
                    case "camera_return":  ReturnCamera(); DebugLogger.Log($"[StoryStage] #{index} camera_return"); break;
                    case "prop_spawn":     SpawnProp(b, index); break;
                    case "actor_place":    PlaceActor(b, index); break;
                    case "actor_action":   PlayAction(b, index); break;
                    case "actor_say":      SayLine(b, index); break;
                    case "wait":           DebugLogger.Log($"[StoryStage] #{index} wait {b.Seconds}s"); break;
                    default:
                        DebugLogger.Log($"[StoryStage] #{index} UNKNOWN cmd '{b.Cmd}' (skipped)");
                        break;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryStage] #{index} '{b.Cmd}' failed: {ex.Message}");
                if (b.Cmd == "player_freeze" || b.Cmd == "player_release" || b.Cmd == "camera_cut" || b.Cmd == "camera_return"
                    || (b.Cmd != null && b.Cmd.StartsWith("actor_")))
                    Stop("control atom failed");
            }
        }

        private void FreezePlayer(bool frozen)
        {
            var player = frozen ? Agent.Main : _frozenPlayer;
            if (player == null)
            {
                if (frozen) throw new InvalidOperationException("Cannot freeze: Agent.Main is null");
                return;
            }
            if (!AgentControlHelper.SafeIsActive(player))
            {
                if (frozen) throw new InvalidOperationException("Cannot freeze: main agent is inactive");
                _frozenPlayer = null;
                return;
            }
            if (frozen)
            {
                if (_frozenPlayer != null && _frozenPlayer != player)
                    throw new InvalidOperationException("Cannot freeze: main agent changed during stage");
                // 只登记本演出拿走的控制权；开始时已被其他系统冻结的玩家不能由本演出擅自释放。
                if (_frozenPlayer == null && player.Controller.ToString() != "Player")
                    throw new InvalidOperationException("Cannot freeze: main agent is already controlled by another system");
                _frozenPlayer = player;
                V.SetPlayerControlFrozen(player, true);
                AgentControlHelper.HoldInPlace(player);
            }
            else
            {
                // 后取得的捆缚/倒地接管优先：演出不能替其解锁，交给该状态的原有收尾归还玩家。
                if (AgentBrain.IsHeldByStatus(player) || AgentBrain.IsRoped(player)
                    || AgentBrain.IsKnockedOut(player) || player.Health <= 0f)
                {
                    _frozenPlayer = null;
                    return;
                }
                // 先解除位置/限速锁，最后归还 Player，不能被通用 NPC 解锁再改成 AI。
                try { AgentControlHelper.ForceUnlockAgent(player); }
                finally
                {
                    // 中途换主玩家时只清旧 Agent 的锁，不能把旧人重新设成 MainAgent。
                    if (player.IsMainAgent) V.SetPlayerControlFrozen(player, false);
                }
                _frozenPlayer = null;
            }
            DebugLogger.Log($"[StoryStage] player frozen={frozen} controller={player.Controller}");
        }

        private void ReturnCamera()
        {
            if (!CameraService.IsHeldBy(_cameraOwner)) return;
            try { CameraService.Stop(); }
            catch (Exception ex) { DebugLogger.Log($"[StoryStage] camera return failed: {ex.Message}"); }
        }

        private void CutCamera(StageBeat b)
        {
            if (string.IsNullOrEmpty(b.Case))
            {
                DebugLogger.Log("[StoryStage] camera_cut without Case (skipped)");
                return;
            }
            Vec3 anchor = PlaceVec(b);
            bool ok = CameraService.ApplyPose(b.Case, null, null, anchor, _cameraOwner);
            DebugLogger.Log($"[StoryStage] camera_cut '{b.Case}' -> {ok} anchor=({anchor.x:F1},{anchor.y:F1},{anchor.z:F1})");
            if (!ok) throw new InvalidOperationException("Cannot acquire stage camera: " + b.Case);
        }

        private void SpawnProp(StageBeat b, int index)
        {
            var scene = Mission.Current?.Scene;
            if (scene == null || Mission.Current != _mission) { DebugLogger.Log($"[StoryStage] #{index} prop_spawn '{b.Id}': no scene"); return; }
            if (string.IsNullOrEmpty(b.Id)) throw new InvalidOperationException("prop_spawn requires stable Id");
            if (_props.ContainsKey(b.Id))
            {
                DebugLogger.Log($"[StoryStage] #{index} prop_spawn '{b.Id}': reuse scene layout");
                return;
            }
            if (string.IsNullOrEmpty(b.Prefab)) { DebugLogger.Log($"[StoryStage] #{index} prop_spawn '{b.Id}': no Prefab"); return; }

            if (!GameEntity.PrefabExists(b.Prefab))
            {
                DebugLogger.Log($"[StoryStage] #{index} prop_spawn '{b.Id}': prefab '{b.Prefab}' NOT LOADED in this scene");
                return;
            }

            Vec3 pos = PlaceVec(b);
            float rad = b.Yaw * (float)Math.PI / 180f;
            Vec3 fwd = new Vec3((float)Math.Cos(rad), (float)Math.Sin(rad), 0f);
            Mat3 rot = Mat3.CreateMat3WithForward(fwd);
            float scale = b.Scale <= 0f ? 1f : b.Scale;
            if (Math.Abs(scale - 1f) > 0.001f) rot = rot * scale;

            var entity = GameEntity.Instantiate(scene, b.Prefab, new MatrixFrame(rot, pos));
            if (entity != null) _props[b.Id] = entity;
            DebugLogger.Log($"[StoryStage] #{index} spawn '{b.Id}' ({b.Prefab}) at ({pos.x:F1},{pos.y:F1},{pos.z:F1}) yaw={b.Yaw} scale={scale} -> {(entity != null ? "ok" : "FAIL")}");
        }

        private void PlaceActor(StageBeat b, int index)
        {
            if (string.IsNullOrEmpty(b.Actor)) { DebugLogger.Log($"[StoryStage] #{index} actor_place without Actor"); return; }

            Vec3 pos = PlaceVec(b);
            float facingDeg = b.Facing < -0.5f ? FacePlayerAngle(pos) : b.Facing;

            if (!OwnsActor(b.Actor)) throw new InvalidOperationException("actor_place without active performance: " + b.Actor);
            var agent = _host.GetAgent(b.Actor);
            if (agent == null) throw new InvalidOperationException("actor_place actor unavailable: " + b.Actor);
            agent.TeleportToPosition(pos);
            agent.LookDirectionAsAngle = facingDeg * (float)Math.PI / 180f;
            DebugLogger.Log($"[StoryStage] #{index} actor_place '{b.Actor}' at ({pos.x:F1},{pos.y:F1},{pos.z:F1}) facing={facingDeg:F0}");
        }

        private void PlayAction(StageBeat b, int index)
        {
            var agent = _host.GetAgent(b.Actor);
            if (agent == null) { DebugLogger.Log($"[StoryStage] #{index} actor_action '{b.Actor}': agent null"); return; }
            if (string.IsNullOrEmpty(b.Action)) return;

            if (!OwnsActor(b.Actor)) throw new InvalidOperationException("actor_action without active performance: " + b.Actor);
            // 租约结束负责清姿态，禁止延迟恢复 action set 回调在另一场演出/战斗中晚到。
            AgentControlHelper.ForcePlayAction(agent, b.Action, restoreAfter: false);
            DebugLogger.Log($"[StoryStage] #{index} actor_action '{b.Actor}' -> '{b.Action}'");
        }

        private void SayLine(StageBeat b, int index)
        {
            var agent = _host.GetAgent(b.Actor);
            if (agent == null) { DebugLogger.Log($"[StoryStage] #{index} actor_say '{b.Actor}': agent null"); return; }

            if (!OwnsActor(b.Actor)) throw new InvalidOperationException("actor_say without active performance: " + b.Actor);
            string text = LWNTextHelper.ResolveText(b.TextKey, b.Note ?? "");
            if (string.IsNullOrEmpty(text)) return;

            SpeechChannel.Say(agent, text);
            DebugLogger.Log($"[StoryStage] #{index} actor_say '{b.Actor}'");
        }

        // ═══════════════════════════════════════════════════════════════
        // 语义位
        // ═══════════════════════════════════════════════════════════════

        private void ResolveAllAnchors()
        {
            _anchors.Clear();
            _anchors["player"] = _host.PlayerAnchor;
            _anchors["gate"] = _host.BlockerPos;
            _anchors["gate_inner"] = _host.GoalPos;

            // 其余语义位由台本自己声明（`Anchors` 段）：加一个锚点 = 加一行 JSON
            if (_script?.Anchors != null)
            {
                foreach (var kv in _script.Anchors)
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value == null || kv.Value.Length == 0) continue;
                    AddNearestByTag(kv.Key, kv.Value);
                }
            }

            foreach (var kv in _anchors)
                DebugLogger.Log($"[StoryStage] anchor '{kv.Key}' = ({kv.Value.x:F1},{kv.Value.y:F1},{kv.Value.z:F1})");
        }

        private void AddNearestByTag(string anchorName, string[] tags)
        {
            var scene = Mission.Current?.Scene;
            if (scene == null) return;

            GameEntity best = null;
            float bestDist = float.MaxValue;
            foreach (var tag in tags)
            {
                IEnumerable<GameEntity> list = null;
                try { list = scene.FindEntitiesWithTag(tag); } catch { }
                if (list == null) continue;

                foreach (var e in list)
                {
                    if (e == null) continue;
                    float d = e.GlobalPosition.Distance(_host.PlayerAnchor);
                    if (d < bestDist) { bestDist = d; best = e; }
                }
            }

            if (best != null)
            {
                _anchors[anchorName] = best.GlobalPosition;
            }
            else
            {
                // 解不到 → 退回玩家位并**报警**（不静默，见 §17.3）
                _anchors[anchorName] = _host.PlayerAnchor;
                DebugLogger.Log($"[StoryStage] anchor '{anchorName}' NOT FOUND (tags={string.Join("/", tags)}) -> fallback to player");
            }
        }

        /// <summary>语义位 + 侧向偏移 → 世界坐标。</summary>
        private Vec3 PlaceVec(StageBeat b)
        {
            Vec3 basePos = ResolveAnchor(b.Anchor);
            Vec2 f = _host.Forward;
            Vec3 fwd = new Vec3(f.x, f.y, 0f);
            Vec3 right = new Vec3(f.y, -f.x, 0f);
            return basePos + fwd * b.Fwd + right * b.Right + new Vec3(0f, 0f, b.Up);
        }

        private Vec3 ResolveAnchor(string name)
        {
            if (string.IsNullOrEmpty(name)) return _host.PlayerAnchor;
            if (_anchors.TryGetValue(name, out var p)) return p;
            DebugLogger.Log($"[StoryStage] unknown anchor '{name}' -> player");
            return _host.PlayerAnchor;
        }

        /// <summary>朝向玩家的水平角度（度）。</summary>
        private float FacePlayerAngle(Vec3 from)
        {
            var player = Agent.Main;
            if (player == null) return 0f;
            Vec3 d = player.Position - from;
            return (float)(Math.Atan2(d.y, d.x) * 180.0 / Math.PI);
        }
    }
}
