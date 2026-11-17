using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>当前场景的雨雪显示；入场配置与临时指令共用一个原版降水宿主。</summary>
    internal sealed class SceneEnvironmentMissionBehavior : MissionLogic
    {
        private sealed class Emitter
        {
            internal GameEntity Host;
            internal ParticleSystem Particle;
            internal string Kind, Resource;
        }

        private Emitter _active, _prepared;
        private float _density, _shelterTimer;
        private bool _sheltered, _emitting, _failed;

        internal static SceneEnvironmentMissionBehavior Get(Mission mission)
        {
            var behavior = mission.GetMissionBehavior<SceneEnvironmentMissionBehavior>();
            if (behavior != null) return behavior;
            behavior = new SceneEnvironmentMissionBehavior();
            mission.AddMissionBehavior(behavior);
            return behavior;
        }

        private static string Kind(float rain, float snow) { return snow > 0f ? "snow" : rain > 0f ? "rain" : "none"; }

        // 切换前先建停用的新宿主，旧效果保持到环境写入成功，失败可回滚。
        // 名字均来自三端Native particle_systems_hardcoded_misc2.xml，不用雪花冒充雨滴。
        internal void Prepare(float rain, float snow)
        {
            string kind = Kind(rain, snow);
            if (kind == "none" || _active?.Kind == kind)
            {
                Release(_prepared);
                _prepared = null;
                return;
            }
            if (_prepared?.Kind == kind) return;
            Release(_prepared);
            _prepared = null;
            int id = -1;
            var next = new Emitter { Kind = kind };
            var candidates = kind == "rain" ? new[] { "psys_game_rain", "psys_game_rain_close" }
                : new[] { "psys_game_snow", "psys_game_snow_2" };
            foreach (string name in candidates)
            {
                id = ParticleSystemManager.GetRuntimeIdByName(name);
                if (id >= 0) { next.Resource = name; break; }
            }
            if (id < 0) throw new InvalidOperationException("native " + kind + " particle resource is unavailable");
            try
            {
                next.Host = GameEntity.CreateEmpty(Mission.Scene, true);
                if (next.Host == null) throw new InvalidOperationException(kind + " host could not be created");
                MatrixFrame local = MatrixFrame.Identity;
                next.Particle = ParticleSystem.CreateParticleSystemAttachedToEntity(id, next.Host, ref local);
                if (next.Particle == null) throw new InvalidOperationException(kind + " particle could not be attached");
                next.Particle.SetEnable(false);
                next.Particle.SetRuntimeEmissionRateMultiplier(0f);
                _prepared = next;
                DebugLogger.Log($"[SceneEnvironment] precipitation prepared kind={kind} resource={next.Resource} runtimeId={id}; visual check pending");
            }
            catch { Release(next); throw; }
        }

        internal void SetDensity(float rain, float snow)
        {
            if (Kind(rain, snow) == "none") { Clear(); return; }
            Prepare(rain, snow);
            if (_prepared != null)
            {
                Release(_active);
                _active = _prepared;
                _prepared = null;
                _emitting = false;
            }
            _density = _active.Kind == "rain" ? rain : snow;
            _failed = false;
            _shelterTimer = 0f;
            _active.Particle.SetRuntimeEmissionRateMultiplier(_density);
            DebugLogger.Log($"[SceneEnvironment] precipitation attached kind={_active.Kind} resource={_active.Resource} density={_density:F2}; visual check pending");
        }

        public override void OnMissionTick(float dt)
        {
            if (_density <= 0f || _active == null || _failed) return;
            try
            {
                // 已换场景时旧原生实体可能失效，只放掉托管引用，不能再调停发射接口。
                if (Mission != Mission.Current) { Clear(); return; }
                if (SceneEnvironment.CheckMission(Mission) != null || Mission.MainAgent == null)
                {
                    SetEmitting(false);
                    return;
                }
                // 只跟最终渲染机位的位置，不接管相机，也不使用其方向。
                Vec3 camera = Mission.Scene.LastFinalRenderCameraFrame.origin;
                if (!Finite(camera.x) || !Finite(camera.y) || !Finite(camera.z)) { SetEmitting(false); return; }
                _shelterTimer -= dt;
                if (_shelterTimer <= 0f)
                {
                    Vec3 source = camera + new Vec3(0f, 0f, 0.1f);
                    Vec3 target = source + new Vec3(0f, 0f, 30f);
                    _sheltered = Mission.Scene.RayCastForClosestEntityOrTerrain(source, target, out float distance, 0.01f);
                    _shelterTimer = 0.2f;
                }
                // Native雨滴为20×20米平面、初速度-50米/秒；雪为40×40×10米体积。
                // 两者均在视点上方发射，雨使用真实雨滴材质与快速下落效果。
                _active.Host.SetGlobalFrame(new MatrixFrame(Mat3.Identity, camera + new Vec3(0f, 0f, 4f)));
                SetEmitting(!_sheltered);
            }
            catch (Exception ex)
            {
                _failed = true;
                try { SetEmitting(false); } catch { }
                DebugLogger.Log("[SceneEnvironment] precipitation tick failed: " + ex);
            }
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

        private void SetEmitting(bool enabled)
        {
            if (_emitting == enabled) return;
            _active.Particle.SetEnable(enabled);
            _emitting = enabled;
            DebugLogger.Log($"[SceneEnvironment] precipitation kind={_active.Kind} emitting={enabled} sheltered={_sheltered}; visual check pending");
        }

        internal string Describe()
        {
            return FormattableString.Invariant($"kind={_active?.Kind ?? "none"} resource={_active?.Resource ?? "none"} attached={_active != null} emitting={_emitting} density={_density:F2} sheltered={_sheltered} failed={_failed}");
        }

        private void Release(Emitter emitter)
        {
            if (emitter == null) return;
            bool safe = Mission == Mission.Current && Mission?.Scene != null;
            if (safe)
            {
                try { emitter.Particle?.SetEnable(false); }
                catch (Exception ex) { DebugLogger.Log("[SceneEnvironment] precipitation disable failed: " + ex.Message); }
                try { emitter.Host?.Remove(0); }
                catch (Exception ex) { DebugLogger.Log("[SceneEnvironment] precipitation host removal failed: " + ex.Message); }
            }
            if (emitter.Host != null) DebugLogger.Log($"[SceneEnvironment] precipitation released kind={emitter.Kind}");
            emitter.Particle = null;
            emitter.Host = null;
        }

        private void Clear()
        {
            _density = 0f;
            Release(_active);
            Release(_prepared);
            _active = _prepared = null;
            _emitting = _sheltered = _failed = false;
        }

        public override void OnEndMissionInternal() { Clear(); base.OnEndMissionInternal(); }
        public override void OnRemoveBehavior() { Clear(); base.OnRemoveBehavior(); }
    }
}
