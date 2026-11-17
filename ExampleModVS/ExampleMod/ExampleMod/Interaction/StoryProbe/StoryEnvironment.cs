using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SandBox;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 原版初始化器仍负责创建完整Mission。仅在指定场景的环境计算期间替换模型的小时输入，
    /// 让太阳、天空、雾色和曝光一起按新小时计算；不改CampaignTime，不复制天气公式。
    /// 三端真实DLL已核：初始化器四参、DefaultMapWeatherModel.GetHourOfDay均存在。
    /// </summary>
    internal static class StoryEnvironment
    {
        [ThreadStatic] private static EntryScope _pending;
        private static readonly ConditionalWeakTable<Mission, EntryScope> _receipts = new ConditionalWeakTable<Mission, EntryScope>();

        public sealed class EntryScope : IDisposable
        {
            internal string SceneName, StageId;
            internal int Version, Depth, HourReads;
            internal bool Computing, Applied, Reported;
            internal DefaultMapWeatherModel Model;
            internal StoryEnvironmentDef Def;
            internal AtmosphereInfo AppliedAtmosphere;

            public void Attach(Mission mission)
            {
                if (mission == null || Def == null) return;
                if (!Applied) throw new InvalidOperationException("story environment initializer was not reached");
                _receipts.Add(mission, this);
                DebugLogger.Log(FormattableString.Invariant(
                    $"[StoryEnvironment] initializer applied stage={StageId} v={Version} scene={SceneName} hour={Def.Hour:F2} weather={Def.Weather} density={Def.Density:F2} snowCover={Def.SnowCover:F2} hourReads={HourReads}; actual scene readback pending"));
            }

            public void Dispose()
            {
                if (ReferenceEquals(_pending, this)) _pending = null;
                Computing = false;
                // 诊断回执只保留纯数据，不持有战役模型。
                Model = null;
            }
        }

        internal sealed class InitializerFrame
        {
            internal EntryScope Scope;
            internal bool WasComputing, Selected;
        }

        public static EntryScope Open(string sceneName, StoryStageDef stage, int version)
        {
            if (_pending != null) throw new InvalidOperationException("story environment entry already in progress");
            var scope = new EntryScope { SceneName = sceneName, StageId = stage.Id, Version = version };
            var def = stage.Environment;
            if (def == null) return scope;
            if (!(Campaign.Current?.Models.MapWeatherModel is DefaultMapWeatherModel model))
                throw new InvalidOperationException("story environment requires the native DefaultMapWeatherModel");
            var initializer = AccessTools.Method(typeof(SandBoxMissions), "CreateSandBoxMissionInitializerRecord");
            var hour = AccessTools.Method(typeof(DefaultMapWeatherModel), "GetHourOfDay");
            if (!HasPatch(initializer, typeof(StoryEnvironmentInitializerPatch), "Prefix", 0)
                || !HasPatch(initializer, typeof(StoryEnvironmentInitializerPatch), "Postfix", 1)
                || !HasPatch(initializer, typeof(StoryEnvironmentInitializerPatch), "Finalizer", 2)
                || !HasPatch(hour, typeof(StoryEnvironmentHourPatch), "Prefix", 0))
                throw new InvalidOperationException("story environment patches are unavailable or disabled");
            // 本次入场固定版本；中途重载不会修改已经加载的环境。
            scope.Def = new StoryEnvironmentDef { Hour = def.Hour, Weather = def.Weather, Density = def.Density, SnowCover = def.SnowCover };
            scope.Model = model;
            _pending = scope;
            return scope;
        }

        private static bool HasPatch(MethodBase target, Type type, string name, int kind)
        {
            var patches = target == null ? null : Harmony.GetPatchInfo(target);
            if (patches == null) return false;
            var list = kind == 0 ? patches.Prefixes : kind == 1 ? patches.Postfixes : patches.Finalizers;
            return list.Any(p => p.PatchMethod.DeclaringType == type && p.PatchMethod.Name == name);
        }

        internal static InitializerFrame EnterInitializer(string sceneName)
        {
            var scope = _pending;
            if (scope == null) return null;
            var frame = new InitializerFrame { Scope = scope, WasComputing = scope.Computing,
                Selected = scope.Depth == 0 && !scope.Applied && sceneName == scope.SceneName };
            scope.Depth++;
            scope.Computing = frame.Selected;
            return frame;
        }

        internal static void LeaveInitializer(InitializerFrame frame)
        {
            if (frame == null) return;
            frame.Scope.Depth--;
            frame.Scope.Computing = frame.WasComputing;
        }

        internal static bool TryHour(DefaultMapWeatherModel model, out float hour)
        {
            var scope = _pending;
            hour = 0f;
            if (scope == null || !scope.Computing || !ReferenceEquals(scope.Model, model)) return false;
            hour = scope.Def.Hour;
            scope.HourReads++;
            return true;
        }

        internal static void Apply(InitializerFrame frame, ref MissionInitializerRecord record)
        {
            if (frame?.Selected != true) return;
            var scope = frame.Scope;
            if (scope.HourReads == 0 || Math.Abs(record.AtmosphereOnCampaign.TimeInfo.TimeOfDay - scope.Def.Hour) > 0.001f)
                throw new InvalidOperationException("native weather model did not consume the configured hour");
            ApplyWeather(scope.Def, ref record);
            scope.AppliedAtmosphere = record.AtmosphereOnCampaign;
            scope.Applied = true;
        }

        // 原版GetSelectedAtmosphereId三端均用0.85区分浓雨雪；资源名是引擎协议。
        internal static void ApplyWeather(StoryEnvironmentDef def, ref MissionInitializerRecord record)
        {
            V.SetMissionEnvironmentHour(ref record, def.Hour);
            var atmosphere = record.AtmosphereOnCampaign;
            atmosphere.TimeInfo.TimeOfDay = def.Hour;
            atmosphere.RainInfo.Density = def.Weather == "rain" ? def.Density : 0f;
            atmosphere.SnowInfo.Density = def.Weather == "snow" ? def.Density : 0f;
            atmosphere.TimeInfo.WinterTimeFactor = def.SnowCover;
            if (def.SnowCover > 0f)
            {
                atmosphere.TimeInfo.Season = (int)CampaignTime.Seasons.Winter;
                atmosphere.TimeInfo.DrynessFactor = 0f;
            }
            atmosphere.InterpolatedAtmosphereName = def.Weather == "snow"
                ? (def.Density >= 0.85f ? "dense_snowy" : "semi_snowy")
                : def.Weather == "rain" ? (def.Density >= 0.85f ? "dense_rainy" : "semi_rainy")
                : "semicloudy_field_battle";
            record.AtmosphereOnCampaign = atmosphere;
        }

        public static void ReportActual(Mission mission)
        {
            if (mission == null || mission != Mission.Current || mission.Scene == null
                || !_receipts.TryGetValue(mission, out var receipt) || receipt.Reported) return;
            if (SceneEnvironment.CheckMission(mission) != null) return;
            receipt.Reported = true;
            try
            {
                DebugLogger.Log(FormattableString.Invariant($"[StoryEnvironment] entry weather before completion rain={mission.Scene.GetRainDensity():F2} snow={mission.Scene.GetSnowDensity():F2}"));
                SceneEnvironment.ApplyEntryWeather(mission, receipt.AppliedAtmosphere);
                DebugLogger.Log("[StoryEnvironment] " + DescribeActual(mission, receipt));
            }
            catch (Exception ex) { DebugLogger.Log("[StoryEnvironment] scene readback failed: " + ex.Message); }
        }

        private static string DescribeActual(Mission mission, EntryScope receipt)
        {
            if (mission.Scene == null) return "actual=pending (scene not ready)";
            float hour = mission.Scene.TimeOfDay;
            float rain = mission.Scene.GetRainDensity(), snow = mission.Scene.GetSnowDensity();
            float cover = mission.Scene.GetWinterTimeFactor();
            bool matches = Math.Abs(hour - receipt.Def.Hour) < 0.02f
                && Math.Abs(rain - receipt.AppliedAtmosphere.RainInfo.Density) < 0.02f
                && Math.Abs(snow - receipt.AppliedAtmosphere.SnowInfo.Density) < 0.02f
                && Math.Abs(cover - receipt.Def.SnowCover) < 0.02f;
            return FormattableString.Invariant($"stage={receipt.StageId} v={receipt.Version} actualHour={hour:F2} rain={rain:F2} snow={snow:F2} snowCover={cover:F2} simulationSpeed={mission.Scene.TimeSpeed:F4} matches={matches}; precipitation={mission.GetMissionBehavior<SceneEnvironmentMissionBehavior>()?.Describe() ?? "inactive"}; visual precipitation requires in-game observation");
        }

        public static string Describe()
        {
            var result = new System.Text.StringBuilder("OK: published story entry environment (JSON edits apply on next entry; temporary controls: custom.story_probe environment help)");
            var plan = StoryLayoutRegistry.Published;
            if (plan != null)
                foreach (var stage in plan.Stages.Values)
                    result.Append("\n").Append(stage.Id).Append(stage.Environment == null ? ": campaign environment"
                        : FormattableString.Invariant($": publishedV={plan.ContentVersion} hour={stage.Environment.Hour:F2} weather={stage.Environment.Weather} density={stage.Environment.Density:F2} snowCover={stage.Environment.SnowCover:F2}"));
            var mission = Mission.Current;
            if (mission != null && _receipts.TryGetValue(mission, out var receipt))
                result.Append("\n").Append(DescribeActual(mission, receipt));
            else result.Append("\nNo configured environment receipt in the current mission.");
            return result.ToString();
        }
    }

    [HarmonyPatch(typeof(SandBoxMissions), "CreateSandBoxMissionInitializerRecord")]
    internal static class StoryEnvironmentInitializerPatch
    {
        private static void Prefix(string sceneName, out StoryEnvironment.InitializerFrame __state)
        { __state = StoryEnvironment.EnterInitializer(sceneName); }

        private static void Postfix(ref MissionInitializerRecord __result, StoryEnvironment.InitializerFrame __state)
        { StoryEnvironment.Apply(__state, ref __result); }

        private static Exception Finalizer(Exception __exception, StoryEnvironment.InitializerFrame __state)
        {
            StoryEnvironment.LeaveInitializer(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(DefaultMapWeatherModel), "GetHourOfDay")]
    internal static class StoryEnvironmentHourPatch
    {
        private static bool Prefix(DefaultMapWeatherModel __instance, ref float __result)
        {
            if (!StoryEnvironment.TryHour(__instance, out float hour)) return true;
            __result = hour;
            return false;
        }
    }
}
