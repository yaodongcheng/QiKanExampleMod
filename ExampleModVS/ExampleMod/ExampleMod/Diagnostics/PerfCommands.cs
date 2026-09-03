using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 性能诊断控制台指令（🔴 注册签名铁律：Func&lt;List&lt;string&gt;, string&gt;，string[] args 必崩）。
    /// 控制台返回文本保持英文（开发者工具，不进翻译表；铁律 13 C# 不许中文字面量）。
    /// custom.perf_status           打印快照（FPS/帧时/根槽 Top3/包裹 Top3）
    /// custom.perf_threshold &lt;ms&gt;  设置卡顿阈值（默认 40ms；0 恢复默认）
    /// </summary>
    public static class PerfCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("perf_status", "custom")]
        public static string PerfStatus(List<string> args)
        {
            try
            {
                PerfProfiler.TakeSnapshot();
                PerfProfiler.GetFrameStats(out int frames, out float avgMs, out float maxMs);
                var sb = new StringBuilder();
                sb.AppendLine($"[Perf] fps≈{(avgMs > 0.01f ? (1000f / avgMs).ToString("F0") : "?")} avg={avgMs:F1}ms max={maxMs:F1}ms frames={frames} scene={PerfProfiler.CurrentScene()}");
                float modTotal = PerfProfiler.RootSlotTotalMs();
                sb.AppendLine($"[Perf] root-slots total {modTotal:F2}ms (window {frames} frames)");
                var tops = PerfProfiler.TopSlots(5);
                foreach (var t in tops)
                    sb.AppendLine($"[Perf]   {PerfProfiler.SlotName(t.slot),-24} {t.ms,8:F2}ms x{t.count}");
                var wraps = PerfWrapper.TopSlots(3);
                if (wraps.Count > 0)
                {
                    sb.AppendLine("[Perf] wraps (other DLLs):");
                    foreach (var w in wraps)
                        sb.AppendLine($"[Perf]   {w.Name,-40} {w.Ms,8:F2}ms x{w.Count}");
                }
                DebugLogger.Log(sb.ToString());
                return "";
            }
            catch (Exception ex)
            {
                return $"[Perf] status failed: {ex.Message}";
            }
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("perf_threshold", "custom")]
        public static string PerfThreshold(List<string> args)
        {
            if (args.Count < 1)
                return $"[Perf] stutter threshold: {PerfProfiler.StutterThresholdMs}ms (custom.perf_threshold <ms> to change, 0=default 40)";
            if (int.TryParse(args[0], out int ms) && ms > 0)
            {
                PerfProfiler.StutterThresholdMs = ms;
                return $"[Perf] stutter threshold -> {ms}ms";
            }
            PerfProfiler.StutterThresholdMs = 40;
            return "[Perf] stutter threshold -> 40ms (default)";
        }
    }
}
