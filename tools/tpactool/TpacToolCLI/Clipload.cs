using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// clipload —— 查 / 改 `AnimationClip` 的 **loading_type**（2026-10-10 立）。
    ///
    /// 🔴 **为什么要它**：ModKit 一开工程就弹
    ///   `WARNING: Animation is flagged Load_when_needed but already shorter than 3.000 seconds!`
    ///   紧跟一条 **native 断言**（`TaleWorlds.Native.dll`：`rglSkeleton_inner_data.h:633`，
    ///   `Expression: duration > memory_shortening_optimization_time_limit_second`）。
    ///   引擎的规矩（同族字符串还有 "%s animation is shorted than %.3f second(s). Please set loading
    ///   type as Always keep in memory" / "Reversed animation can not be Load When Needed!"）：
    ///   **`Load_when_needed` 的 clip 必须长于 3 秒**（内存缩短优化的阈值）。
    ///
    /// 🔴 **字段在哪**：`AnimationClip.ReadMetadata` 里那几个没命名的 `Unknown*`
    ///   —— 本次拿**原版 5623 条 clip** 对照定死：`UnknownUInt2` 就是 loading_type，
    ///   取值 `0 = Always_keep_in_memory` / `1 = Load_when_needed` / `2 = Never_load`
    ///   （判据：原版里取值 1 的 **356 条全部 ≥ 3 秒，一条都不越线**；0 与 2 都有大量 <3 秒的）。
    ///
    /// 用法：
    ///   tpaccli clipload --packdir &lt;目录&gt; [--filter &lt;clip 名子串&gt;]                  ← 只查（默认）
    ///   tpaccli clipload --packdir &lt;目录&gt; [--filter …] --fix [--inplace] [--out &lt;目录&gt;]  ← 改成 Always_keep_in_memory
    ///
    /// 纪律（照抄 clipprio）：**只改那 4 个字节**，其余一个不碰；改前**偏移自校验**；
    /// `--inplace` 先留 `.bak-&lt;时间戳&gt;`；写出后**回读验证**。
    /// </summary>
    public static class Clipload
    {
        /// <summary>引擎的内存缩短阈值（秒）—— 取自 native 的那条断言。</summary>
        public const float ShorteningTimeLimitSeconds = 3.0f;

        public const uint AlwaysKeepInMemory = 0;
        public const uint LoadWhenNeeded = 1;
        public const uint NeverLoad = 2;

        public static int Run(string packDirs, string filter, bool fix, string outDir, bool inPlace)
        {
            var dirs = (packDirs ?? ".").Split(',').Select(d => d.Trim()).Where(d => d.Length > 0).ToArray();
            int hitPacks = 0, violators = 0, fixedCount = 0, failed = 0;

            foreach (var pd in dirs)
            {
                if (!Directory.Exists(pd)) continue;
                foreach (var file in Directory.EnumerateFiles(pd, "*.tpac", SearchOption.AllDirectories))
                {
                    List<AnimationClip> clips;
                    try
                    {
                        var scan = new AssetPackage(file, true, false);
                        clips = scan.Items.OfType<AnimationClip>()
                            .Where(i => filter == null || i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine($"skip {Path.GetFileName(file)}: header read failed {e.Message}");
                        continue;
                    }
                    if (clips.Count == 0) continue;
                    hitPacks++;

                    int packBad = 0, packFixed = 0;
                    var pkg = fix ? new AssetPackage(file, true, false) : null;
                    var workClips = fix
                        ? pkg.Items.OfType<AnimationClip>()
                             .Where(i => filter == null || i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList()
                        : clips;

                    foreach (var clip in workClips)
                    {
                        var raw = clip.RawMeta;
                        if (raw == null)
                        {
                            Console.Error.WriteLine($"  [X] {clip.Name}: no RawMeta");
                            failed++;
                            continue;
                        }
                        int off = FindLoadingTypeOffset(raw, clip);
                        if (off < 0)
                        {
                            Console.Error.WriteLine($"  [X] {clip.Name}: offset self-check failed (ReadMetadata layout mismatch) -- refusing to touch");
                            failed++;
                            continue;
                        }
                        uint type = BitConverter.ToUInt32(raw, off);
                        bool bad = type == LoadWhenNeeded && clip.Duration <= ShorteningTimeLimitSeconds + 1e-4f;
                        if (bad) { violators++; packBad++; }

                        if (bad || type != AlwaysKeepInMemory)
                        {
                            string tag = bad ? "<<< VIOLATION (will trip the native assert)" : "  (>= limit, legal but non-default)";
                            string name = type switch
                            {
                                AlwaysKeepInMemory => "Always_keep_in_memory",
                                LoadWhenNeeded => "Load_when_needed",
                                NeverLoad => "Never_load",
                                _ => "Unknown(" + type + ")",
                            };
                            Console.WriteLine($"  {clip.Name,-34} dur={clip.Duration,7:F3}s  loading_type={name,-22} {tag}");
                        }

                        if (fix && type == LoadWhenNeeded)
                        {
                            BitConverter.GetBytes(AlwaysKeepInMemory).CopyTo(raw, off);
                            clip.UnknownUInt2 = AlwaysKeepInMemory;   // 让回读两口径一致
                            packFixed++;
                            fixedCount++;
                        }
                    }

                    if (!fix)
                    {
                        if (packBad == 0)
                        {
                            Console.WriteLine($"== {file}: {clips.Count} clip(s), all clean");
                        }
                        continue;
                    }

                    if (packFixed == 0)
                    {
                        Console.WriteLine($"== {file}: nothing to fix (all Always_keep_in_memory)");
                        continue;
                    }

                    string outPath;
                    if (inPlace)
                    {
                        string bak = file + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                        File.Copy(file, bak, overwrite: false);
                        pkg.Save(file);
                        outPath = file;
                        Console.WriteLine($"  overwritten in place (backup: {Path.GetFileName(bak)})");
                    }
                    else
                    {
                        var outRoot = outDir ?? ".";
                        Directory.CreateDirectory(outRoot);
                        outPath = Path.Combine(outRoot, Path.GetFileName(file));
                        pkg.Save(outPath);
                        Console.WriteLine($"  written: {outPath}  ({new FileInfo(outPath).Length} B, original {new FileInfo(file).Length} B)");
                    }

                    // 回读验证
                    var back = new AssetPackage(outPath, true, false);
                    int bad2 = 0;
                    foreach (var bc in back.Items.OfType<AnimationClip>()
                                 .Where(i => filter == null || i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (bc.UnknownUInt2 != AlwaysKeepInMemory) { bad2++; Console.Error.WriteLine($"    [X] read-back {bc.Name} loading_type={bc.UnknownUInt2}"); }
                    }
                    bool ok = back.Guid.Equals(pkg.Guid) && back.Items.Count == pkg.Items.Count && bad2 == 0;
                    Console.WriteLine($"  read-back: items {back.Items.Count}/{pkg.Items.Count} | guid same={back.Guid.Equals(pkg.Guid)} | still-wrong {bad2}");
                    Console.WriteLine(ok ? "  OK: written, package structure intact" : "  [X] verification failed -- do not ship this output");
                    if (!ok) failed++;
                }
            }

            if (hitPacks == 0)
            {
                Console.Error.WriteLine("no matching AnimationClip in any package");
                return 1;
            }
            Console.WriteLine(fix
                ? $"result: fixed {fixedCount} | violations (native assert) {violators} | failed {failed}"
                : $"result: violations (Load_when_needed and <= {ShorteningTimeLimitSeconds:F1}s -> ModKit native assert) {violators} | failed {failed}");
            return (violators > 0 && !fix) || failed > 0 ? 1 : 0;
        }

        /// <summary>
        /// 算出 `UnknownUInt2`（= loading_type）在 RawMeta 里的字节偏移。
        /// **逐字段照抄 `AnimationClip.ReadMetadata` 的读序**，并在两个已知字段上做自校验
        /// （Priority / UnknownInt）—— 对不上就返回 -1，绝不瞎改。
        /// </summary>
        private static int FindLoadingTypeOffset(byte[] raw, AnimationClip clip)
        {
            try
            {
                int p = 0;
                uint version = BitConverter.ToUInt32(raw, p); p += 4;
                p += 4 * 6;                                   // Duration / Source1 / Source2 / Param1..3
                int prio = BitConverter.ToInt32(raw, p);
                if (prio != clip.Priority) return -1;         // 自校验①（偏移 28）
                p += 4;
                p += 16;                                      // Animation guid
                p += 16;                                      // StepPoints vec4
                p = SkipSized(raw, p);                        // SoundCode
                p = SkipSized(raw, p);                        // VoiceCode
                p = SkipSized(raw, p);                        // FacialAnimationId
                p = SkipSized(raw, p);                        // BlendsWithAction
                p = SkipSized(raw, p);                        // ContinueWithAction
                p += 4 + 4;                                   // LeftHandPose / RightHandPose
                p = SkipSized(raw, p);                        // CombatParameterId
                p += 4 + 4;                                   // BlendInPeriod / BlendOutPeriod
                p += 1;                                       // DoNotInterpolate (bool)
                int unkInt = BitConverter.ToInt32(raw, p);
                if (unkInt != clip.UnknownInt) return -1;     // 自校验②
                p += 4;
                if (version >= 4)
                {
                    p = SkipSized(raw, p);                    // UnknownClipName
                    p = SkipSized(raw, p);                    // ClipSource1Name
                    p = SkipSized(raw, p);                    // ClipSource2Name
                    if (version >= 5) p += 1;                 // GeneratedIndex (sbyte)
                }
                uint cur = BitConverter.ToUInt32(raw, p);
                if (cur != clip.UnknownUInt2) return -1;      // 自校验③（就是它）
                return p;
            }
            catch
            {
                return -1;
            }
        }

        private static int SkipSized(byte[] raw, int p)
        {
            int len = BitConverter.ToInt32(raw, p);
            if (len < 0 || p + 4 + len > raw.Length) throw new EndOfStreamException();
            return p + 4 + len;
        }
    }
}
