using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// clipflags —— 离线给 `AnimationClip` **加/删 flags**（2026-09-25 立）。
    ///
    /// 为什么需要：`custom.anim_ch` 那个运行时 `additionalFlags` 参数**引擎很可能压根不读**
    /// （旁证：`prio=60` 传了没用），所以 `enforce_all` / `enforce_lowerbody` 到今天为止
    /// **从没被真正验证过**。官方文档说这两个 flag 是"保护本条动画的姿势/腿不被别的层动掉"
    /// （原版 510 / 355 条 clip 在用，清一色是坐着喝酒、吧台服务、蹲下这类固定姿势），
    /// 要验就得写进 clip 元数据本身 —— 也就是这个工具。
    ///
    /// 字段布局（`AnimationClip.ReadMetadata` 实读顺序，逐字段照走）：
    ///   u32 version · 6×f32 · i32 Priority · guid(16) · vec4(16) · 5×sizedString ·
    ///   2×i32 · sizedString · 2×f32 · bool(1) · i32 · [v≥4: 3×sizedString, [v≥5: sbyte], u32, u16]
    ///   ⇒ 紧跟其后就是 **Flags 列表**（i32 条数 + 每条 sizedString）。
    ///
    /// 🔴 走的是**字节级**：只动 Flags 那一段（插入/删除），其余字节原样保留。
    ///    **绝不走"改对象再序列化"** —— 库的 `AnimationClip.WriteMetadata` 有两处与真实文件不符
    ///    （硬写 version=5、vec4.w 写成 0），`clipset` 那条命令的注释里已经记过这个教训。
    ///    `AssetPackage.Save` 对元数据是 **RawMeta 优先直写**，会自动重算偏移，所以变长插入是安全的。
    ///
    /// 用法：
    ///   tpaccli clipflags --packdir &lt;目录&gt; --filter &lt;clip 名子串&gt; --add enforce_all [--remove cyclic]
    ///                     [--out &lt;目录&gt;] [--inplace]
    /// </summary>
    public static class ClipFlags
    {
        public static int Run(string packDirs, string filter, string addCsv, string removeCsv, string outDir, bool inPlace)
        {
            if (string.IsNullOrEmpty(filter)) { Console.Error.WriteLine("clipflags 需要 --filter <clip 名子串>"); return 1; }
            var toAdd = Split(addCsv);
            var toRemove = Split(removeCsv);
            if (toAdd.Count == 0 && toRemove.Count == 0)
            {
                Console.Error.WriteLine("clipflags 需要 --add 或 --remove（逗号分隔 flag 名）");
                return 1;
            }

            var dirs = (packDirs ?? ".").Split(',').Select(d => d.Trim()).Where(d => d.Length > 0).ToArray();
            int packs = 0, changed = 0, failed = 0;

            foreach (var pd in dirs)
            {
                if (!Directory.Exists(pd)) continue;
                foreach (var file in Directory.EnumerateFiles(pd, "*.tpac", SearchOption.AllDirectories))
                {
                    List<AnimationClip> hits;
                    try
                    {
                        hits = new AssetPackage(file, true, false).Items.OfType<AnimationClip>()
                               .Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                    }
                    catch (Exception e) { Console.Error.WriteLine($"跳过 {Path.GetFileName(file)}：读头失败 {e.Message}"); continue; }
                    if (hits.Count == 0) continue;

                    packs++;
                    var pkg = new AssetPackage(file, true, false);
                    var clips = pkg.Items.OfType<AnimationClip>()
                                .Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

                    Console.WriteLine($"== {file}  （{clips.Count} 条命中）");
                    foreach (var clip in clips)
                    {
                        var raw = clip.RawMeta;
                        if (raw == null) { Console.Error.WriteLine($"  ❌ {clip.Name}: 无 RawMeta"); failed++; continue; }

                        string err;
                        int flagsPos = FindFlagsOffset(raw, out int version, out err);
                        if (flagsPos < 0) { Console.Error.WriteLine($"  ❌ {clip.Name}: 定位 Flags 失败（{err}）"); failed++; continue; }

                        var (count, items, endPos) = ParseStringList(raw, flagsPos);
                        if (count < 0) { Console.Error.WriteLine($"  ❌ {clip.Name}: Flags 列表解析失败"); failed++; continue; }

                        // 自校验：解析出来的必须与对象模型一致（不一致 = 走错位了，立刻停手）
                        if (count != (clip.Flags?.Count ?? 0) || !items.SequenceEqual(clip.Flags ?? new List<string>()))
                        {
                            Console.Error.WriteLine($"  ❌ {clip.Name}: Flags 自校验失败（字节流 [{string.Join(",", items)}] vs 对象模型 [{string.Join(",", clip.Flags ?? new List<string>())}]）");
                            failed++;
                            continue;
                        }

                        var newItems = new List<string>(items);
                        foreach (var f in toRemove) newItems.Remove(f);
                        foreach (var f in toAdd) if (!newItems.Contains(f)) newItems.Add(f);
                        if (newItems.SequenceEqual(items))
                        {
                            Console.WriteLine($"  {clip.Name,-34} 不变（flags 已是 [{string.Join(",", items)}]）");
                            continue;
                        }

                        // 重建：前缀 + 新列表 + 后缀
                        var ms = new MemoryStream();
                        ms.Write(raw, 0, flagsPos);
                        var w = new BinaryWriter(ms);
                        w.Write(newItems.Count);
                        foreach (var s in newItems)
                        {
                            var bytes = Encoding.UTF8.GetBytes(s);
                            w.Write(bytes.Length);
                            w.Write(bytes);
                        }
                        w.Write(raw, endPos, raw.Length - endPos);
                        w.Flush();
                        clip.RawMeta = ms.ToArray();
                        // 同步对象模型（Save 走 RawMeta，改不改它都不影响写盘；同步只为回读比对时两口径一致）
                        clip.Flags.Clear();
                        clip.Flags.AddRange(newItems);
                        Console.WriteLine($"  {clip.Name,-34} flags v{version} [{string.Join(",", items)}] → [{string.Join(",", newItems)}]"
                                        + $"  ({raw.Length} → {clip.RawMeta.Length} B)");
                        changed++;
                    }

                    if (changed == 0) continue;

                    string outPath;
                    if (inPlace)
                    {
                        // 🔴 `--inplace` 实测**会失败**（源包还被读句柄占着，Save 写同一个路径抛异常）——
                        //    可靠做法 = 先 `--out` 到独立目录，再用 PowerShell 拷回（Copy-Item）。
                        //    这里保留入口但把失败讲清楚，免得又静默没写进去。
                        string bak = file + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                        File.Copy(file, bak, overwrite: false);
                        try
                        {
                            pkg.Save(file);
                            outPath = file;
                            Console.WriteLine($"  已原地覆盖（备份 {Path.GetFileName(bak)}）");
                        }
                        catch (Exception e)
                        {
                            Console.Error.WriteLine($"  ❌ 原地覆盖失败：{e.Message}");
                            Console.Error.WriteLine("     ⇒ 请改用 --out <目录> 先出到独立目录，再用 PowerShell Copy-Item 拷回交付包");
                            failed++;
                            continue;
                        }
                    }
                    else
                    {
                        var outRoot = outDir ?? ".";
                        Directory.CreateDirectory(outRoot);
                        outPath = Path.Combine(outRoot, Path.GetFileName(file));
                        pkg.Save(outPath);
                        Console.WriteLine($"  已写出: {outPath}  ({new FileInfo(outPath).Length} B, 原 {new FileInfo(file).Length} B)");
                    }

                    // 回读验证：对象模型读出来的 flags / 关键字段 / 条目数
                    var back = new AssetPackage(outPath, true, false);
                    bool sameGuid = back.Guid.Equals(pkg.Guid);
                    bool sameCount = back.Items.Count == pkg.Items.Count;
                    int bad = 0;
                    foreach (var bc in back.Items.OfType<AnimationClip>()
                             .Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                    {
                        var orig = clips.First(c => c.Name == bc.Name);
                        bool okFlags = (bc.Flags ?? new List<string>()).SequenceEqual(orig.Flags ?? new List<string>());
                        bool okDur = Math.Abs(bc.Duration - orig.Duration) < 1e-4;
                        bool okSrc = Math.Abs(bc.Source1 - orig.Source1) < 1e-4 && Math.Abs(bc.Source2 - orig.Source2) < 1e-4;
                        if (!okFlags || !okDur || !okSrc) { bad++; Console.Error.WriteLine($"    ❌ 回读 {bc.Name}: flags一致={okFlags} dur一致={okDur} src一致={okSrc}"); }
                    }
                    bool okAll = sameGuid && sameCount && bad == 0;
                    Console.WriteLine($"  回读: items {back.Items.Count}/{pkg.Items.Count} · guid 一致={sameGuid} · 异常 {bad} 条");
                    Console.WriteLine(okAll ? "  ✅ 写入生效且包结构完整" : "  ❌ 校验不过 —— 别用这个产物");
                    if (!okAll) failed++;
                }
            }

            if (packs == 0) { Console.Error.WriteLine($"没有哪个包里有名字含 '{filter}' 的 AnimationClip"); return 1; }
            Console.WriteLine($"共 {packs} 个包 / 改动 {changed} 条 clip，失败 {failed} 项");
            return failed == 0 ? 0 : 1;
        }

        private static List<string> Split(string csv) =>
            string.IsNullOrWhiteSpace(csv)
                ? new List<string>()
                : csv.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        /// <summary>按 `AnimationClip.ReadMetadata` 的顺序走到 Flags 列表的起始（= 条数那个 i32 的偏移）。</summary>
        private static int FindFlagsOffset(byte[] raw, out int version, out string err)
        {
            err = null;
            version = 0;
            try
            {
                int p = 0;
                version = BitConverter.ToInt32(raw, p); p += 4;
                p += 4 * 6;     // Duration / Source1 / Source2 / Param1 / Param2 / Param3
                p += 4;         // Priority
                p += 16;        // Animation guid
                p += 16;        // StepPoints (vec4)
                for (int i = 0; i < 5; i++) p = SkipSizedString(raw, p);   // SoundCode / VoiceCode / FacialAnimationId / BlendsWithAction / ContinueWithAction
                p += 4 + 4;     // LeftHandPose / RightHandPose
                p = SkipSizedString(raw, p);                                // CombatParameterId
                p += 4 + 4;     // BlendInPeriod / BlendOutPeriod
                p += 1;         // DoNotInterpolate (bool)
                p += 4;         // UnknownInt
                if (version >= 4)
                {
                    for (int i = 0; i < 3; i++) p = SkipSizedString(raw, p); // UnknownClipName / ClipSource1Name / ClipSource2Name
                    if (version >= 5) p += 1;                                // GeneratedIndex (sbyte)
                    p += 4;                                                  // UnknownUInt2
                    p += 2;                                                  // UnknownUShort
                }
                else
                {
                    p += 4;
                }
                if (p < 0 || p + 4 > raw.Length) { err = "越界"; return -1; }
                return p;
            }
            catch (Exception e) { err = e.Message; return -1; }
        }

        private static int SkipSizedString(byte[] raw, int p)
        {
            int len = BitConverter.ToInt32(raw, p);
            if (len < 0 || p + 4 + len > raw.Length) throw new Exception($"sized string 越界 (p={p} len={len})");
            return p + 4 + len;
        }

        private static (int count, List<string> items, int endPos) ParseStringList(byte[] raw, int p)
        {
            int count = BitConverter.ToInt32(raw, p);
            if (count < 0 || count > 64) return (-1, null, p);      // flags 不可能这么多
            var list = new List<string>(count);
            int q = p + 4;
            for (int i = 0; i < count; i++)
            {
                int len = BitConverter.ToInt32(raw, q);
                if (len < 0 || q + 4 + len > raw.Length) return (-1, null, p);
                list.Add(Encoding.UTF8.GetString(raw, q + 4, len));
                q += 4 + len;
            }
            return (count, list, q);
        }
    }
}
