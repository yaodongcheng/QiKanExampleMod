using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>当前Mission的临时环境控制。只写Scene，入场数据与战役时钟均不写。</summary>
    internal static class SceneEnvironment
    {
        private static readonly ConditionalWeakTable<Mission, Session> _sessions = new ConditionalWeakTable<Mission, Session>();
        private static readonly PropertyInfo _initializer = AccessTools.Property(typeof(Mission), "InitializerRecord");

        internal sealed class Session
        {
            internal AtmosphereInfo Entry, Requested;
            internal bool Active, LightingChanged;
        }

        internal sealed class Change
        {
            internal string Operation, Weather;
            internal float? Hour, Density, SnowCover;
        }

        internal static string CheckMission(Mission mission)
        {
            if (mission?.Scene == null || mission.CurrentState != Mission.State.Continuing || mission.MissionIsEnding)
                return "NO: enter a running outdoor mission first.";
            if (mission.Scene.IsMultiplayerScene()) return "NO: multiplayer scenes are not supported.";
            if (mission.Scene.IsAtmosphereIndoor) return "NO: weather and daylight control require an outdoor scene.";
            return null;
        }

        private static Session Capture(Mission mission)
        {
            var record = (TaleWorlds.Core.MissionInitializerRecord)_initializer.GetValue(mission, null);
            var entry = record.AtmosphereOnCampaign;
            // 本命令所写的光照须有原版入场记录，才能准确恢复。无战役大气的场景拒绝修改。
            if (entry.IsValid || string.IsNullOrEmpty(entry.InterpolatedAtmosphereName))
                throw new InvalidOperationException("this mission has no initialized campaign atmosphere interpolation");
            return new Session { Entry = entry, Requested = entry };
        }

        internal static AtmosphereInfo Merge(AtmosphereInfo current, Change change)
        {
            if (change.Hour.HasValue)
                current.TimeInfo.TimeOfDay = change.Hour.Value;
            if (change.Weather != null)
            {
                current.RainInfo.Density = change.Weather == "rain" ? change.Density.Value : 0f;
                current.SnowInfo.Density = change.Weather == "snow" ? change.Density.Value : 0f;
            }
            if (change.SnowCover.HasValue) current.TimeInfo.WinterTimeFactor = change.SnowCover.Value;
            return current;
        }

        // 1.2.12 native实证：Mission入场与SetFrameForAtmosphere使用Scene的同一个插值对象。
        // 前提是1.2.12且入场记录使用InterpolatedAtmosphereName；Capture和V先把关。
        // 不调LoadAtmosphereData（它加载大地图数据），不把战役模型的曝光字段直接当Scene预设。
        // Scene.TimeSpeed是模拟速度，不是日照时钟；写0会把运动与粒子降到约万分之一速度。
        internal static void ApplyScene(Mission mission, AtmosphereInfo atmosphere, bool lighting)
        {
            var scene = mission.Scene;
            if (lighting && V.SceneEnvironmentTimeError != null)
                throw new InvalidOperationException(V.SceneEnvironmentTimeError);
            var precipitation = SceneEnvironmentMissionBehavior.Get(mission);
            precipitation.Prepare(atmosphere.RainInfo.Density, atmosphere.SnowInfo.Density);
            if (lighting)
            {
                V.RefreshSceneEnvironmentHour(scene, atmosphere.TimeInfo.TimeOfDay);
            }
            scene.SetRainDensity(atmosphere.RainInfo.Density);
            scene.SetSnowDensity(atmosphere.SnowInfo.Density);
            scene.SetWinterTimeFactor(atmosphere.TimeInfo.WinterTimeFactor);
            scene.SetDrynessFactor(atmosphere.TimeInfo.WinterTimeFactor > 0f ? 0f : atmosphere.TimeInfo.DrynessFactor);
            precipitation.SetDensity(atmosphere.RainInfo.Density, atmosphere.SnowInfo.Density);
        }

        // 原版插值入场路径未把SnowInfo.Density复制到Scene；在场景就绪后补一次。
        // 与临时命令共用雨雪显示，不创建第二套发射器，不重算光照，不改模拟速度。
        internal static void ApplyEntryWeather(Mission mission, AtmosphereInfo atmosphere)
        { ApplyScene(mission, atmosphere, false); }

        internal static string Execute(Change change)
        {
            var mission = Mission.Current;
            string error = CheckMission(mission);
            if (error != null) return error;
            if (change.Hour.HasValue && V.SceneEnvironmentTimeError != null) return V.SceneEnvironmentTimeError;
            if (change.Operation == "status") return Describe(mission);
            if (change.Operation == "reset")
            {
                if (!_sessions.TryGetValue(mission, out var old) || !old.Active)
                    return "OK: no temporary environment override to reset.";
                ApplyScene(mission, old.Entry, old.LightingChanged);
                old.Requested = old.Entry;
                old.Active = !Matches(mission.Scene, old.Entry);
                if (!old.Active) old.LightingChanged = false;
                return Report(mission, old, "reset to entry environment");
            }

            // Capture核实原生插值对象的入场来源；坏命令、无插值对象的场景不会写光照。
            bool known = _sessions.TryGetValue(mission, out var session);
            if (!known) session = Capture(mission);
            var current = session.Requested;
            current.TimeInfo.TimeOfDay = mission.Scene.TimeOfDay;
            current.RainInfo.Density = mission.Scene.GetRainDensity();
            current.SnowInfo.Density = mission.Scene.GetSnowDensity();
            current.TimeInfo.WinterTimeFactor = mission.Scene.GetWinterTimeFactor();
            var requested = Merge(current, change);
            if (!known) _sessions.Add(mission, session);
            // 先保留恢复凭据；原生写入若抛异常，仍能恢复，不会留下无回执的半次修改。
            bool wasActive = session.Active;
            bool wasLightingChanged = session.LightingChanged;
            session.Active = true;
            session.LightingChanged |= change.Hour.HasValue;
            try { ApplyScene(mission, requested, change.Hour.HasValue); }
            catch (Exception ex)
            {
                try
                {
                    ApplyScene(mission, current, change.Hour.HasValue);
                    session.Active = wasActive;
                    session.LightingChanged = wasLightingChanged;
                }
                catch (Exception rollback) { DebugLogger.Log("[SceneEnvironment] rollback failed: " + rollback); }
                throw new InvalidOperationException("scene update failed; inspect status or reset", ex);
            }
            session.Requested = requested;
            session.Active = true;
            return Report(mission, session, "temporary " + change.Operation);
        }

        private static bool Matches(Scene scene, AtmosphereInfo requested)
        {
            return Math.Abs(scene.TimeOfDay - requested.TimeInfo.TimeOfDay) < 0.02f
                && Math.Abs(scene.GetRainDensity() - requested.RainInfo.Density) < 0.02f
                && Math.Abs(scene.GetSnowDensity() - requested.SnowInfo.Density) < 0.02f
                && Math.Abs(scene.GetWinterTimeFactor() - requested.TimeInfo.WinterTimeFactor) < 0.02f;
        }

        private static string Report(Mission mission, Session session, string operation)
        {
            bool matches = Matches(mission.Scene, session.Requested);
            string result = (matches ? "OK: " : "NO: scene readback differs after ") + operation + "\n" + Describe(mission);
            DebugLogger.Log("[SceneEnvironment] " + result.Replace("\n", "; "));
            return result + "\nScene readback and emitter attachment are not visual checks. The loaded sky/cloud preset is retained.";
        }

        internal static string Describe(Mission mission)
        {
            if (mission?.Scene == null) return "No current mission scene.";
            var scene = mission.Scene;
            string result = FormattableString.Invariant($"actualHour={scene.TimeOfDay:F2} rain={scene.GetRainDensity():F2} snow={scene.GetSnowDensity():F2} snowCover={scene.GetWinterTimeFactor():F2}; simulationSpeed={scene.TimeSpeed:F4}; sunDirection={scene.GetSunDirection()}; fog={scene.GetFog():F4}");
            if (_sessions.TryGetValue(mission, out var session))
                result += FormattableString.Invariant($"\nOverrideActive={session.Active}; entryHour={session.Entry.TimeInfo.TimeOfDay:F2} entryRain={session.Entry.RainInfo.Density:F2} entrySnow={session.Entry.SnowInfo.Density:F2} entrySnowCover={session.Entry.TimeInfo.WinterTimeFactor:F2}; requestedHour={session.Requested.TimeInfo.TimeOfDay:F2} requestedRain={session.Requested.RainInfo.Density:F2} requestedSnow={session.Requested.SnowInfo.Density:F2} requestedSnowCover={session.Requested.TimeInfo.WinterTimeFactor:F2} matches={Matches(scene, session.Requested)}");
            else result += "\nOverrideActive=False";
            var precipitation = mission.GetMissionBehavior<SceneEnvironmentMissionBehavior>();
            result += "\nTemporaryTimeSupported=" + (V.SceneEnvironmentTimeError == null)
                + "; lighting=entry native preset" + (V.SceneEnvironmentTimeError == null ? " (temporary changes use loaded interpolation)" : " (temporary time disabled)")
                + "; precipitation=" + (precipitation?.Describe() ?? "inactive");
            return result;
        }
    }
}
