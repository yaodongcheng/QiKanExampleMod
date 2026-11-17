using System;
using System.Collections.Generic;
using System.Globalization;

namespace LivingWorldNpcs
{
    internal static class EnvironmentCommands
    {
        private const string Prefix = "custom.story_probe environment ";
        internal static string Help => "Usage:\n" + Prefix + "status\n" + Prefix + "time <hour: 0 <= hour < 24>\n"
            + Prefix + "weather clear|rain|snow [density: 0..1; default 0.7 for rain/snow]\n"
            + Prefix + "snowcover <0..1>\n" + Prefix + "set <hour> clear|rain|snow <density> <snowCover>\n"
            + Prefix + "reset\nTemporary outdoor campaign-mission changes only; no JSON/save/clock edits.\n"
            + "time/set refresh the loaded native atmosphere interpolation on 1.2.12; simulation speed is preserved.\n"
            + "On 1.3+ missions time/set are refused before changes; entry time still follows JSON.\n"
            + "rain/snow attach distinct native precipitation effects; switching removes the old effect, clear stops both. The loaded sky/cloud preset is retained.";

        private static bool Number(string text, float min, float max, bool exclusiveMax, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !float.IsNaN(value) && !float.IsInfinity(value) && value >= min
                && (exclusiveMax ? value < max : value <= max);
        }

        internal static bool TryParse(List<string> args, out SceneEnvironment.Change change, out string error)
        {
            change = null;
            error = "NO: invalid environment arguments.\n" + Help;
            if (args == null || args.Count == 0) args = new List<string> { "status" };
            int start = 0;
            if (args.Count > 1 && !Known(args[0]) && Known(args[1])) start = 1;
            string op = (args[start] ?? "").ToLowerInvariant();
            int count = args.Count - start;
            var result = new SceneEnvironment.Change { Operation = op };
            float number;
            if ((op == "status" || op == "reset" || op == "help") && count == 1) { }
            else if (op == "time" && count == 2 && Number(args[start + 1], 0f, 24f, true, out number))
                result.Hour = number;
            else if (op == "snowcover" && count == 2 && Number(args[start + 1], 0f, 1f, false, out number))
                result.SnowCover = number;
            else if (op == "weather" && (count == 2 || count == 3))
            {
                result.Weather = (args[start + 1] ?? "").ToLowerInvariant();
                number = result.Weather == "clear" ? 0f : 0.7f;
                if (count == 3 && !Number(args[start + 2], 0f, 1f, false, out number)) return false;
                result.Density = number;
                if (!ValidWeather(result.Weather, number)) return false;
            }
            else if (op == "set" && count == 5)
            {
                if (!Number(args[start + 1], 0f, 24f, true, out number)) return false;
                result.Hour = number;
                result.Weather = (args[start + 2] ?? "").ToLowerInvariant();
                if (!Number(args[start + 3], 0f, 1f, false, out number) || !ValidWeather(result.Weather, number)) return false;
                result.Density = number;
                if (!Number(args[start + 4], 0f, 1f, false, out number)) return false;
                result.SnowCover = number;
            }
            else return false;
            change = result;
            error = null;
            return true;
        }

        private static bool Known(string value)
        {
            return value != null && (value.Equals("status", StringComparison.OrdinalIgnoreCase)
                || value.Equals("time", StringComparison.OrdinalIgnoreCase) || value.Equals("weather", StringComparison.OrdinalIgnoreCase)
                || value.Equals("snowcover", StringComparison.OrdinalIgnoreCase) || value.Equals("set", StringComparison.OrdinalIgnoreCase)
                || value.Equals("reset", StringComparison.OrdinalIgnoreCase) || value.Equals("help", StringComparison.OrdinalIgnoreCase));
        }

        private static bool ValidWeather(string weather, float density)
        {
            return weather == "clear" ? density == 0f : (weather == "rain" || weather == "snow") && density > 0f;
        }

        internal static string Execute(List<string> args)
        {
            if (!TryParse(args, out var change, out string error)) return error;
            if (change.Operation == "help") return Help;
            if (change.Operation == "status") return StoryEnvironment.Describe() + "\n" + SceneEnvironment.Describe(TaleWorlds.MountAndBlade.Mission.Current);
            try { return SceneEnvironment.Execute(change); }
            catch (Exception ex)
            {
                DebugLogger.Log("[SceneEnvironment] command failed: " + ex);
                return "NO: environment command failed; see [SceneEnvironment] in the runtime log.\nUse "
                    + Prefix + "status to inspect the scene; reset restores the entry environment.";
            }
        }
    }
}
