using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using LivingWorldNpcs.Combat;

namespace LivingWorldNpcs.CampaignMode.Tools
{
    /// <summary>
    /// 螺旋丸的验收命令（归口：C# 命令实现进 CampaignMode\Tools\）。
    ///
    ///   custom.rasengan spawn [dist] [height]      在面前 dist 米、抬高 height 米处生成（默认 0 / 1.7）
    ///   custom.rasengan spin &lt;blade&gt; &lt;air&gt; &lt;core&gt;   设三个环的峰值转速（度/秒，正 = 俯视逆时针）
    ///   custom.rasengan charge &lt;sec&gt;               设蓄力时长并重播
    ///   custom.rasengan replay                      重播蓄力（尺寸/转速回到起点再涨）
    ///   custom.rasengan status                      打印当前状态
    ///
    /// 首参可弃（项目纪律）：第一个参数认不出子命令时**当作 spawn 的距离**处理，
    /// 这样 `custom.rasengan 0` 和 `custom.rasengan spawn 0` 都能用，随手补个 `1` 也不会报错。
    /// 返回文本一律英文（控制台纪律）。
    /// </summary>
    internal static class RasenganCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("rasengan", "custom")]
        public static string Execute(List<string> args)
        {
            if (Mission.Current == null || Agent.Main == null)
                return "Error: not in mission.";

            string sub = "spawn";
            int at = 0;
            if (args != null && args.Count > 0 && args[0] != null)
            {
                string s = args[0].Trim().ToLowerInvariant();
                if (s == "spawn" || s == "spin" || s == "charge" || s == "replay" || s == "status")
                {
                    sub = s;
                    at = 1;
                }
                // 其它值 = 可弃占位（或 spawn 的距离），留给下面按位置解析
            }

            switch (sub)
            {
                case "status":
                {
                    RasenganController c = RasenganController.Current;
                    return c == null ? "rasengan: none (spawn one first)" : c.Status();
                }
                case "replay":
                {
                    RasenganController c = RasenganController.Current;
                    if (c == null) return "rasengan: none (spawn one first)";
                    c.Recharge();
                    return "rasengan: charge replayed from 0.";
                }
                case "spin":
                {
                    RasenganController c = RasenganController.Ensure();
                    if (c == null) return "Error: no mission.";
                    float a = ParseF(args, at + 0, 360f);
                    float b = ParseF(args, at + 1, 200f);
                    float d = ParseF(args, at + 2, 0f);
                    c.Configure(-1f, a, b, d);
                    return $"rasengan: dps set blade={a:F0} air={b:F0} core={d:F0} (deg/s, + = CCW seen from above)";
                }
                case "charge":
                {
                    RasenganController c = RasenganController.Ensure();
                    if (c == null) return "Error: no mission.";
                    float sec = ParseF(args, at + 0, 1.5f);
                    c.Configure(sec, -1f, -1f, -1f);   // -1 = 不改该项
                    c.Recharge();
                    return $"rasengan: charge time = {sec:F2}s, replayed.";
                }
                default:   // spawn
                {
                    RasenganController c = RasenganController.Ensure();
                    if (c == null) return "Error: no mission.";

                    float dist = ParseF(args, at + 0, 0f);
                    float height = ParseF(args, at + 1, 1.7f);

                    Vec3 look = Agent.Main.LookDirection;
                    float hl = MathF.Sqrt(look.x * look.x + look.y * look.y);
                    Vec3 fwd = hl > 1e-3f ? new Vec3(look.x / hl, look.y / hl, 0f) : new Vec3(0f, 1f, 0f);
                    Vec3 pos = Agent.Main.Position + fwd * dist;
                    pos.z += height;

                    GameEntity root = c.Spawn(pos);
                    if (root == null)
                        return $"rasengan: spawn failed (prefab '{RasenganController.PrefabName}' missing or scene null)";
                    return $"rasengan: spawned at ({pos.x:F1},{pos.y:F1},{pos.z:F1}) dist={dist:F1} height={height:F2}";
                }
            }
        }

        /// <summary>取第 i 个参数当 float；缺失/解析不出就用默认（首参可弃的同一套精神）。</summary>
        private static float ParseF(List<string> args, int i, float fallback)
        {
            if (args == null || i < 0 || i >= args.Count || args[i] == null) return fallback;
            return float.TryParse(args[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
                ? v : fallback;
        }
    }
}
