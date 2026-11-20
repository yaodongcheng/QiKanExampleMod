using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// `custom.ui tree [depth]` —— 把当前屏幕上**所有 Gauntlet 层的控件树**写进运行时日志。
    ///
    /// 为什么要有它：读 prefab 只能知道"写了什么"，看不到运行时**实际绑成了什么**
    /// （brush 有没有解析到、sprite 是不是 null、控件真实尺寸与位置）。
    /// 排查"背景透明 / 找不到贴图 / 布局不对位"这类问题时，原版与自建面板各 dump 一次，
    /// 逐行对比就是判据。
    ///
    /// 输出：`Debug/StoryEngine_RuntimeLog.txt`，前缀 `[UiTree]`；控制台只回一行摘要（纯英文）。
    /// </summary>
    internal static class UiTreeCommands
    {
        private const string LogTag = "[UiTree]";

        [CommandLineFunctionality.CommandLineArgumentFunction("ui", "custom")]
        public static string Ui(List<string> args)
        {
            try
            {
                // 首参可弃：`custom.ui` / `custom.ui tree 8` / `custom.ui 8` 都成立。
                string sub = args != null && args.Count > 0 ? (args[0] ?? "").ToLowerInvariant() : "tree";
                int depth = 6;
                if (sub != "tree" && sub != "dump")
                {
                    if (!int.TryParse(sub, out depth))
                        return "Usage: custom.ui tree [depth]  (depth 1-12, default 6; output goes to Debug/StoryEngine_RuntimeLog.txt)";
                }
                else if (args != null && args.Count > 1) int.TryParse(args[1], out depth);
                depth = Math.Max(1, Math.Min(depth, 12));

                var diag = new StringBuilder();
                int count = DumpAllLayers(depth, diag);
                if (count == 0)
                    return "NO widgets. " + diag + " (detail in Debug/StoryEngine_RuntimeLog.txt)";
                return "OK: dumped " + count + " widgets at depth " + depth + " [" + diag + "]"
                     + " -> Debug/StoryEngine_RuntimeLog.txt (grep [UiTree])";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"{LogTag} failed: {ex}");
                return "Error: " + ex.Message;
            }
        }

        private static int DumpAllLayers(int depth, StringBuilder diag)
        {
            var lines = new StringBuilder();
            int count = 0;
            int gauntletLayers = 0;

            var screens = GetAllScreens();
            diag.Append("screens=" + screens.Count);
            lines.AppendLine($"{LogTag} ===== dump start: screens={screens.Count} top={ScreenManager.TopScreen?.GetType().Name ?? "null"} =====");

            foreach (var screen in screens)
            {
                var layers = GetLayers(screen);
                lines.AppendLine($"{LogTag} [screen] {Safe(() => screen.GetType().Name)} layers={layers.Count}");
                for (int i = 0; i < layers.Count; i++)
                {
                    var layer = layers[i];
                    if (layer == null) continue;
                    bool gauntlet = layer.GetType().Name.IndexOf("Gauntlet", StringComparison.Ordinal) >= 0;
                    int order = SafeInt(() => layer.InputRestrictions?.Order ?? -1);
                    bool active = SafeBool(() => layer.IsActive);
                    if (gauntlet) gauntletLayers++;
                    lines.AppendLine($"{LogTag}   [layer {i}] {Safe(() => layer.GetType().Name)} order={order} active={active} gauntlet={gauntlet}");
                    if (!gauntlet) continue;

                    foreach (var root in GetRootWidgets(layer))
                    {
                        if (root == null) continue;
                        count += DumpWidget(root, 0, depth, lines);
                    }
                }
            }

            diag.Append(" gauntletLayers=" + gauntletLayers);
            lines.AppendLine($"{LogTag} ===== dump end: widgets={count} gauntletLayers={gauntletLayers} =====");
            DebugLogger.Log(lines.ToString());
            return count;
        }

        /// <summary>ScreenManager 里登记的所有屏（`_screenList` 是私有的）。</summary>
        private static List<ScreenBase> GetAllScreens()
        {
            var result = new List<ScreenBase>();
            try
            {
                var f = typeof(ScreenManager).GetField("_screenList", BindingFlags.NonPublic | BindingFlags.Static);
                if (f?.GetValue(null) is IEnumerable list)
                    foreach (var o in list)
                        if (o is ScreenBase sb && !result.Contains(sb)) result.Add(sb);
            }
            catch { }
            var top = ScreenManager.TopScreen;
            if (top != null && !result.Contains(top)) result.Insert(0, top);
            return result;
        }

        /// <summary>屏里的层（`ScreenBase._layers` 是私有的 List&lt;ScreenLayer&gt;，反射比强转可靠）。</summary>
        private static List<ScreenLayer> GetLayers(ScreenBase screen)
        {
            var result = new List<ScreenLayer>();
            if (screen == null) return result;
            try
            {
                var f = typeof(ScreenBase).GetField("_layers", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f?.GetValue(screen) is IEnumerable list)
                    foreach (var o in list)
                        if (o is ScreenLayer sl) result.Add(sl);
            }
            catch { }
            return result;
        }

        /// <summary>取一个 GauntletLayer 里所有 movie 的根控件（`_movieIdentifiers` 是私有字段，只能反射）。</summary>
        private static IEnumerable<Widget> GetRootWidgets(ScreenLayer layer)
        {
            int n = 0;
            foreach (var (movie, _) in GauntletUiIntrospect.GetMovies(layer))
            {
                var root = GauntletUiIntrospect.GetRootWidget(movie);
                if (root != null) { n++; yield return root; }
            }
            if (n == 0)
                DebugLogger.Log($"{LogTag}     (no movie root on {layer.GetType().Name})");
        }

        private static int DumpWidget(Widget w, int level, int maxDepth, StringBuilder sb)
        {
            if (w == null) return 0;
            int count = 1;
            string indent = new string(' ', level * 2);
            sb.AppendLine($"{LogTag} {indent}{Describe(w)}");

            if (level >= maxDepth)
            {
                int hidden = GetChildren(w).Count();
                if (hidden > 0) sb.AppendLine($"{LogTag} {indent}  ... {hidden} children (depth limit)");
                return count;
            }

            int shown = 0;
            foreach (var child in GetChildren(w))
            {
                if (child == null) continue;
                if (++shown > 40)
                {
                    sb.AppendLine($"{LogTag} {indent}  ... more children omitted");
                    break;
                }
                count += DumpWidget(child, level + 1, maxDepth, sb);
            }
            return count;
        }

        private static string Describe(Widget w)
        {
            string id = Safe(() => w.Id) ?? "";
            string type = Safe(() => w.GetType().Name) ?? "?";
            string size = Safe(() => { var s = w.Size; return $"{s.X:F0}x{s.Y:F0}"; }) ?? "?";
            string pos = Safe(() => { var p = w.GlobalPosition; return $"{p.X:F0},{p.Y:F0}"; }) ?? "?";
            string vis = Safe(() => w.IsVisible ? "1" : "0") ?? "?";
            string brush = DescribeBrush(w);
            string sprite = DescribeSprite(w);
            return $"{type} id='{id}' size={size} pos={pos} vis={vis}{brush}{sprite}";
        }

        /// <summary>Brush 名（含它第一层的 sprite 名）—— 背景透明时，这一项是空的就说明根本没绑上。</summary>
        private static string DescribeBrush(Widget w)
        {
            object brush = null;
            try
            {
                var prop = w.GetType().GetProperty("Brush", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                brush = prop?.GetValue(w);
            }
            catch { }
            if (brush == null) return "";

            string name = null;
            try { name = brush.GetType().GetProperty("Name")?.GetValue(brush) as string; } catch { }
            return $" brush='{(string.IsNullOrEmpty(name) ? "(unnamed)" : name)}'";
        }

        private static string DescribeSprite(Widget w)
        {
            try
            {
                var f = typeof(Widget).GetField("_sprite", BindingFlags.NonPublic | BindingFlags.Instance);
                var sprite = f?.GetValue(w);
                if (sprite == null) return "";
                var name = sprite.GetType().GetProperty("Name")?.GetValue(sprite) as string;
                return $" sprite='{(string.IsNullOrEmpty(name) ? "(unnamed)" : name)}'";
            }
            catch { return ""; }
        }

        private static IEnumerable<Widget> GetChildren(Widget w)
        {
            object raw = null;
            try
            {
                var p = typeof(Widget).GetProperty("Children", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null) raw = p.GetValue(w);
                else
                {
                    var f = typeof(Widget).GetField("_children", BindingFlags.NonPublic | BindingFlags.Instance);
                    raw = f?.GetValue(w);
                }
            }
            catch { }
            if (raw is IEnumerable en)
                foreach (var o in en)
                    if (o is Widget cw) yield return cw;
        }

        private static string Safe(Func<string> f)
        {
            try { return f(); } catch { return null; }
        }

        private static int SafeInt(Func<int> f)
        {
            try { return f(); } catch { return -1; }
        }

        private static bool SafeBool(Func<bool> f)
        {
            try { return f(); } catch { return false; }
        }
    }
}