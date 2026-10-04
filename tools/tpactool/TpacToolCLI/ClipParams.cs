using System;
using System.IO;
using System.Linq;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// clipparams —— 离线改 `AnimationClip` 的 **Param1 / Param2 / Param3**（2026-10-04 立）。
    ///
    /// 为什么要它：弓系瞄准的 ready clip（原版 `ready_bow`）带 **Param1=0.42 / Param2=0.8**，
    /// 而弩系/投掷系的 ready 全是 0 —— 即这两栏是"弓的拉弓进度"专用的刻度，钩索（走弓系）
    /// 要照配就得改自己 clip 的这两个值。只进 ModKit 面板能改，但要重发布整个包；
    /// 本命令照 `clipprio` 的**字节级**做法补上（无编辑器、改完重启游戏即生效）。
    ///
    /// 字段位置（`AnimationClip.ReadMetadata` 实读顺序，逐字段照走）：
    ///   version(4) · Duration(4) · Source1(4) · Source2(4)
    ///   ⇒ **Param1 = float @ 偏移 16 · Param2 @ 20 · Param3 @ 24**（与 `clipprio` 的 Priority@28 同源）。
    ///
    /// 纪律（照抄 clipprio）：
    ///   · **只改那 4 个字节**，其余一个不碰（库的 `WriteMetadata` 会硬写 version=5、
    ///     把 vec4.w 写成 0，与真实文件不符 ⇒ 绝不走"改对象再序列化"那条路）；
    ///   · 改前**自校验**（就地读出的值必须与对象模型一致，不一致就拒绝改）；
    ///   · 默认**写到独立目录**（`--inplace` 才原地覆盖，覆盖前自动留 `.bak-<时间戳>`）；
    ///   · 写出后**回读验证**。
    ///
    /// 用法：
    ///   tpaccli clipparams --packdir &lt;目录&gt; --filter &lt;clip 名子串&gt; [--p1 0.42] [--p2 0.8] [--p3 0]
    ///                      [--out &lt;目录&gt;] [--inplace]
    /// </summary>
    public static class ClipParams
    {
        private const int P1Offset = 16;
        private const int P2Offset = 20;
        private const int P3Offset = 24;

        public static int Run(string packDirs, string filter, float? p1, float? p2, float? p3, string outDir, bool inPlace)
        {
            if (string.IsNullOrEmpty(filter))
            {
                Console.Error.WriteLine("clipparams 需要 --filter <clip 名子串>");
                return 1;
            }
            if (p1 == null && p2 == null && p3 == null)
            {
                Console.Error.WriteLine("clipparams 需要 --p1 / --p2 / --p3 至少给一个");
                return 1;
            }

            var dirs = (packDirs ?? ".").Split(',').Select(d => d.Trim()).Where(d => d.Length > 0).ToArray();
            int hitPacks = 0, hitClips = 0, failed = 0;

            foreach (var pd in dirs)
            {
                if (!Directory.Exists(pd)) continue;
                foreach (var file in Directory.EnumerateFiles(pd, "*.tpac", SearchOption.AllDirectories))
                {
                    // ① 只读头，先看这个包里有没有命中的 clip（不读数据段 —— 库里 OptimizedAnimation.ReadData 会崩）
                    List<AnimationClip> matches;
                    try
                    {
                        var scan = new AssetPackage(file, true, false);
                        matches = scan.Items.OfType<AnimationClip>()
                            .Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine($"跳过 {Path.GetFileName(file)}：读头失败 {e.Message}");
                        continue;
                    }
                    if (matches.Count == 0) continue;

                    hitPacks++;
                    var pkg = new AssetPackage(file, true, false);
                    var clips = pkg.Items.OfType<AnimationClip>()
                        .Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    Console.WriteLine($"== {file}  （{clips.Count} 条命中）");
                    foreach (var clip in clips)
                    {
                        var raw = clip.RawMeta;
                        if (raw == null || raw.Length < P3Offset + 4)
                        {
                            Console.Error.WriteLine($"  ❌ {clip.Name}: 没有 RawMeta，拒绝改");
                            failed++;
                            continue;
                        }
                        // ② 自校验：就地读出的 float 必须与对象模型一致（不一致 = 走错位了，立刻停手）
                        float b1 = BitConverter.ToSingle(raw, P1Offset);
                        float b2 = BitConverter.ToSingle(raw, P2Offset);
                        float b3 = BitConverter.ToSingle(raw, P3Offset);
                        if (b1 != clip.Param1 || b2 != clip.Param2 || b3 != clip.Param3)
                        {
                            Console.Error.WriteLine($"  ❌ {clip.Name}: 偏移自校验失败（字节流 {b1}/{b2}/{b3} vs 对象模型 "
                                                  + $"{clip.Param1}/{clip.Param2}/{clip.Param3}）—— 拒绝改");
                            failed++;
                            continue;
                        }

                        string before = $"{b1}/{b2}/{b3}";
                        if (p1 != null) { BitConverter.GetBytes(p1.Value).CopyTo(raw, P1Offset); clip.Param1 = p1.Value; }
                        if (p2 != null) { BitConverter.GetBytes(p2.Value).CopyTo(raw, P2Offset); clip.Param2 = p2.Value; }
                        if (p3 != null) { BitConverter.GetBytes(p3.Value).CopyTo(raw, P3Offset); clip.Param3 = p3.Value; }
                        Console.WriteLine($"  {clip.Name,-34} Param {before} → {clip.Param1}/{clip.Param2}/{clip.Param3}");
                        hitClips++;
                    }

                    // ③ 写出
                    string outPath;
                    if (inPlace)
                    {
                        string bak = file + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                        File.Copy(file, bak, overwrite: false);
                        pkg.Save(file);
                        outPath = file;
                        Console.WriteLine($"  已原地覆盖（备份 {Path.GetFileName(bak)}）");
                    }
                    else
                    {
                        var outRoot = outDir ?? ".";
                        Directory.CreateDirectory(outRoot);
                        outPath = Path.Combine(outRoot, Path.GetFileName(file));
                        pkg.Save(outPath);
                        Console.WriteLine($"  已写出: {outPath}  ({new FileInfo(outPath).Length} B, 原 {new FileInfo(file).Length} B)");
                    }

                    // ④ 回读验证
                    var back = new AssetPackage(outPath, true, false);
                    bool sameGuid = back.Guid.Equals(pkg.Guid);
                    bool sameCount = back.Items.Count == pkg.Items.Count;
                    int bad = 0;
                    var backClips = back.Items.OfType<AnimationClip>()
                        .Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var bc in backClips)
                    {
                        bool okClip = (p1 == null || bc.Param1 == p1.Value)
                                   && (p2 == null || bc.Param2 == p2.Value)
                                   && (p3 == null || bc.Param3 == p3.Value);
                        if (!okClip) { bad++; Console.Error.WriteLine($"    ❌ 回读 {bc.Name} Param={bc.Param1}/{bc.Param2}/{bc.Param3}"); }
                    }
                    bool ok = sameGuid && sameCount && bad == 0 && backClips.Count == clips.Count;
                    Console.WriteLine($"  回读: items {back.Items.Count}/{pkg.Items.Count} · guid 一致={sameGuid} · "
                                    + $"Param 对上的条数 {backClips.Count - bad}/{backClips.Count}");
                    Console.WriteLine(ok ? "  ✅ 写入生效且包结构完整" : "  ❌ 校验不过 —— 别用这个产物");
                    if (!ok) failed++;
                }
            }

            if (hitPacks == 0)
            {
                Console.Error.WriteLine($"没有哪个包里有名字含 '{filter}' 的 AnimationClip");
                return 1;
            }
            Console.WriteLine($"共 {hitPacks} 个包 / {hitClips} 条 clip 改 Param，失败 {failed} 项");
            return failed == 0 ? 0 : 1;
        }
    }
}
