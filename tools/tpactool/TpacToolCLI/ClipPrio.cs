using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// clipprio —— 离线改 `AnimationClip` 的 **Priority** 字段（2026-09-25 立）。
    ///
    /// 为什么要它：飞行姿势全部 `Priority = 0`（20 条 flight_* 实测都是 0），而挥手动作是 2、
    /// 挥刀 10~15 ⇒ 飞行中（人冻住、没有走路动画给腿）**优先级低的那条会被高的抢走全身**。
    /// 想在 ModKit 里改要开编辑器 + Publish + 拷包；这条命令直接改交付包，改完重启游戏即可。
    ///
    /// 字段位置（`AnimationClip.ReadMetadata` 实读顺序）：
    ///   version(4) · Duration(4) · Source1(4) · Source2(4) · Param1(4) · Param2(4) · Param3(4)
    ///   ⇒ **Priority = int32 @ 偏移 28**（与 `clipset` 那条"偏移 28 / 84"的记录同源）。
    ///
    /// 纪律（照抄 `clipset`）：
    ///   · **只改元数据那 4 个字节**，其余一个不碰（库的 `WriteMetadata` 会硬写 version=5、
    ///     把 vec4.w 写成 0，与真实文件不符 ⇒ 绝不走"改对象再序列化"那条路）；
    ///   · 改前**自校验**（就地读出的值必须与对象模型一致，不一致就拒绝改）；
    ///   · 默认**写到独立目录**（`--inplace` 才原地覆盖，覆盖前自动留 `.bak-<时间戳>`）；
    ///   · 写出后**回读验证**。
    ///
    /// 用法：
    ///   tpaccli clipprio --packdir &lt;目录&gt; --filter &lt;clip 名子串&gt; --prio 30 [--out &lt;目录&gt;] [--inplace]
    /// </summary>
    public static class ClipPrio
    {
        private const int PriorityOffset = 28;

        public static int Run(string packDirs, string filter, int priority, string outDir, bool inPlace)
        {
            if (string.IsNullOrEmpty(filter))
            {
                Console.Error.WriteLine("clipprio 需要 --filter <clip 名子串>");
                return 1;
            }
            if (priority < 0 || priority > 255)
            {
                Console.Error.WriteLine("--prio 必须在 0~255（引擎的优先级只有一个字节，amf_priority_mask = 0xFF）");
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
                        if (raw == null || raw.Length < PriorityOffset + 4)
                        {
                            Console.Error.WriteLine($"  ❌ {clip.Name}: 没有 RawMeta，拒绝改");
                            failed++;
                            continue;
                        }
                        // ② 自校验：就地读出的 int 必须与对象模型一致
                        int before = BitConverter.ToInt32(raw, PriorityOffset);
                        if (before != clip.Priority)
                        {
                            Console.Error.WriteLine($"  ❌ {clip.Name}: 偏移自校验失败（字节流 {before} vs 对象模型 {clip.Priority}）—— 拒绝改");
                            failed++;
                            continue;
                        }
                        BitConverter.GetBytes(priority).CopyTo(raw, PriorityOffset);
                        clip.Priority = priority;      // 让回读两口径一致
                        Console.WriteLine($"  {clip.Name,-34} Priority {before} → {priority}");
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
                    int badPri = 0;
                    var backClips = back.Items.OfType<AnimationClip>()
                        .Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var bc in backClips)
                        if (bc.Priority != priority) { badPri++; Console.Error.WriteLine($"    ❌ 回读 {bc.Name} Priority={bc.Priority}"); }
                    bool ok = sameGuid && sameCount && badPri == 0 && backClips.Count == clips.Count;
                    Console.WriteLine($"  回读: items {back.Items.Count}/{pkg.Items.Count} · guid 一致={sameGuid} · "
                                    + $"Priority 全部={priority} 的条数 {backClips.Count - badPri}/{backClips.Count}");
                    Console.WriteLine(ok ? "  ✅ 写入生效且包结构完整" : "  ❌ 校验不过 —— 别用这个产物");
                    if (!ok) failed++;
                }
            }

            if (hitPacks == 0)
            {
                Console.Error.WriteLine($"没有哪个包里有名字含 '{filter}' 的 AnimationClip");
                return 1;
            }
            Console.WriteLine($"共 {hitPacks} 个包 / {hitClips} 条 clip 改成 Priority={priority}，失败 {failed} 项");
            return failed == 0 ? 0 : 1;
        }
    }
}
