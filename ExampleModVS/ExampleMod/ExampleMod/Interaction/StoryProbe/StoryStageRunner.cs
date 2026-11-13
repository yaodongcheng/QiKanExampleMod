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
            _script = StoryStageDefs.Load(scriptName);
            if (_script == null)
            {
                DebugLogger.Log("[StoryStage] Begin failed: script not loaded");
                return false;
            }

            ResolveAllAnchors();
            _time = 0f;
            _next = 0;
            _running = true;
            DebugLogger.Log($"[StoryStage] begin '{_script.Script ?? scriptName}' beats={BeatCount} anchors={_anchors.Count}");
            return true;
        }

        /// <summary>控制台单发一个原子（不依赖台本、不动播放游标）。</summary>
        public void RunAtom(StageBeat beat)
        {
            if (_script == null) _script = StoryStageDefs.Load("StoryStage");
            if (_anchors.Count == 0) ResolveAllAnchors();
            Execute(beat, -1);
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
            if (!_running) return;
            _running = false;
            DebugLogger.Log($"[StoryStage] stopped: {reason} (at {_time:F2}s, next={_next}/{BeatCount})");
        }

        public void Tick(float dt)
        {
            if (!_running) return;
            _time += dt;

            while (_next < BeatCount && _script.Beats[_next].At <= _time)
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
            Execute(_script.Beats[index], index);
            return true;
        }

        /// <summary>跳过演出：把玩家和相机放回去。</summary>
        public void Skip()
        {
            _running = false;
            _next = BeatCount;
            try { CameraService.Stop(); } catch (Exception ex) { DebugLogger.Log($"[StoryStage] skip camera stop failed: {ex.Message}"); }
            FreezePlayer(false);
            DebugLogger.Log("[StoryStage] skipped by command");
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
                    case "camera_return":  CameraService.Stop(); DebugLogger.Log($"[StoryStage] #{index} camera_return"); break;
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
            }
        }

        private static void FreezePlayer(bool frozen)
        {
            var player = Agent.Main;
            if (player == null)
            {
                DebugLogger.Log($"[StoryStage] player_freeze='{frozen}' skipped: Agent.Main is null");
                return;
            }
            try
            {
                // 冻结 = 摘掉 Player 控制器交给 AI（偷窃条那套轮子）；但 AI 会自己走 ⇒ 再钉住限速
                V.SetPlayerControlFrozen(player, frozen);
                if (frozen) AgentControlHelper.HoldInPlace(player);
                else AgentControlHelper.ForceUnlockAgent(player);
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryStage] freeze player failed: {ex.Message}"); }
            DebugLogger.Log($"[StoryStage] player frozen={frozen}");
        }

        private void CutCamera(StageBeat b)
        {
            if (string.IsNullOrEmpty(b.Case))
            {
                DebugLogger.Log("[StoryStage] camera_cut without Case (skipped)");
                return;
            }
            Vec3 anchor = PlaceVec(b);
            bool ok = CameraService.ApplyPose(b.Case, null, null, anchor);
            DebugLogger.Log($"[StoryStage] camera_cut '{b.Case}' -> {ok} anchor=({anchor.x:F1},{anchor.y:F1},{anchor.z:F1})");
        }

        private void SpawnProp(StageBeat b, int index)
        {
            var scene = Mission.Current?.Scene;
            if (scene == null) { DebugLogger.Log($"[StoryStage] #{index} prop_spawn '{b.Id}': no scene"); return; }
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
            DebugLogger.Log($"[StoryStage] #{index} spawn '{b.Id}' ({b.Prefab}) at ({pos.x:F1},{pos.y:F1},{pos.z:F1}) yaw={b.Yaw} scale={scale} -> {(entity != null ? "ok" : "FAIL")}");
        }

        private void PlaceActor(StageBeat b, int index)
        {
            if (string.IsNullOrEmpty(b.Actor)) { DebugLogger.Log($"[StoryStage] #{index} actor_place without Actor"); return; }

            Vec3 pos = PlaceVec(b);
            float facingDeg = b.Facing < -0.5f ? FacePlayerAngle(pos) : b.Facing;

            bool ok = _host.SpawnOrPlaceActor(b.Actor, pos, facingDeg, b.Civilian);
            DebugLogger.Log($"[StoryStage] #{index} actor_place '{b.Actor}' at ({pos.x:F1},{pos.y:F1},{pos.z:F1}) facing={facingDeg:F0} civilian={b.Civilian} -> {(ok ? "ok" : "FAIL")}");
        }

        private void PlayAction(StageBeat b, int index)
        {
            var agent = _host.GetAgent(b.Actor);
            if (agent == null) { DebugLogger.Log($"[StoryStage] #{index} actor_action '{b.Actor}': agent null"); return; }
            if (string.IsNullOrEmpty(b.Action)) return;

            AgentControlHelper.ForcePlayAction(agent, b.Action, restoreAfter: true);
            DebugLogger.Log($"[StoryStage] #{index} actor_action '{b.Actor}' -> '{b.Action}'");
        }

        private void SayLine(StageBeat b, int index)
        {
            var agent = _host.GetAgent(b.Actor);
            if (agent == null) { DebugLogger.Log($"[StoryStage] #{index} actor_say '{b.Actor}': agent null"); return; }

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
