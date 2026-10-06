using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// matflags —— 离线改 `Material` 的三样东西（2026-10-06 立）：
    ///   · shader flags（加/删，如 `alpha_test`）
    ///   · `alphaTest` 数值（阈值）
    ///   · `blend` 混合模式
    ///
    /// **为什么需要**：`alpha_test` 这个 **shader flag** 在 ModKit 的材质面板里**看不到勾选项**
    /// （原版贴花材质 `blood_decal_a` / `ashes_decal` / `decal_moss_a` 都带着它，我们自己建的一个都没有）。
    /// 没有它，`flagDefs.rsh` 的 `#define ALPHA_TEST 0` 不会被覆盖 ⇒ `apply_alpha_test` 那两行 clip
    /// 根本不编译 ⇒ 材质里 alphaTest 填多少都没用（"150 与 0 无差别"的真因）。
    /// 顺带把阈值/混合也做进同一个命令：做 A/B 实验时**不用每改一个数就回 ModKit 重新 Publish**。
    ///
    /// 字段布局（`Material.ReadMetadata` 实读顺序，逐字段照走）：
    ///   u32 Version · guid(16) BillboardGuid · u32 SubVersion · u32 UnknownUint1 ·
    ///   stringList Flags · u32 UnknownUint2 · stringList VertexLayoutFlags ·
    ///   **sizedString BlendMode** · guid(16) Shader ·
    ///   i32 texCount + texCount×(i32 index + guid 16) ·
    ///   **f32 AlphaTest** · **stringList ShaderMaterialFlags**（dump 里显示为 shaderFlags）
    ///   · ExtraMaterialSettings（按 subVersion）
    ///
    /// 🔴 走的是**字节级**：只重建这三段，其余字节原样保留。
    ///    **绝不走"改对象再序列化"** —— 库的 `AnimationClip.WriteMetadata` 有硬写 version、丢 vec4.w 的前科
    ///    （见 ClipFlags.cs 的教训）。`AssetPackage.Save` 对元数据是 **RawMeta 优先直写**、会自动重算偏移
    ///    ⇒ 变长插入安全。
    ///
    /// 用法：
    ///   tpaccli matflags --packdir &lt;目录&gt; --filter &lt;材质名子串&gt;
    ///                    [--add alpha_test] [--remove X] [--alphatest 0.0235] [--blend modulate]
    ///                    [--out &lt;目录&gt;] [--inplace]
    /// </summary>
    public static class MatFlags
    {
        public static int Run(string packDirs, string filter, string addCsv, string removeCsv,
                              float? alphaTest, string blend, string outDir, bool inPlace)
        {
            if (string.IsNullOrEmpty(filter)) { Console.Error.WriteLine("matflags 需要 --filter <材质名子串>"); return 1; }
            var toAdd = Split(addCsv);
            var toRemove = Split(removeCsv);
            if (toAdd.Count == 0 && toRemove.Count == 0 && !alphaTest.HasValue && string.IsNullOrEmpty(blend))
            {
                Console.Error.WriteLine("matflags 需要 --add / --remove / --alphatest / --blend 至少一项");
                return 1;
            }

            var dirs = (packDirs ?? ".").Split(',').Select(d => d.Trim()).Where(d => d.Length > 0).ToArray();
            int packs = 0, changed = 0, failed = 0;

            foreach (var pd in dirs)
            {
                if (!Directory.Exists(pd)) continue;
                foreach (var file in Directory.EnumerateFiles(pd, "*.tpac", SearchOption.AllDirectories))
                {
                    var pkg = new AssetPackage(file, true, false);
                    var mats = pkg.Items.OfType<Material>()
                                .Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (mats.Count == 0) continue;

                    packs++;
                    Console.WriteLine($"== {file}  （{mats.Count} 个命中）");
                    int changedHere = 0;

                    foreach (var mat in mats)
                    {
                        var raw = mat.RawMeta;
                        if (raw == null) { Console.Error.WriteLine($"  ❌ {mat.Name}: 无 RawMeta"); failed++; continue; }

                        string err;
                        Pos pos;
                        if (!Locate(raw, out pos, out err))
                        {
                            Console.Error.WriteLine($"  ❌ {mat.Name}: 定位字段失败（{err}）");
                            failed++;
                            continue;
                        }

                        // ── 自校验：从字节流读出来的必须与对象模型一致（不一致 = 走错位了，立刻停手） ──
                        var parsed = ParseStringList(raw, pos.flags);
                        if (parsed.count < 0) { Console.Error.WriteLine($"  ❌ {mat.Name}: shaderFlags 解析失败"); failed++; continue; }
                        var items = parsed.items;
                        int flagsEnd = parsed.endPos;

                        var modelFlags = mat.ShaderMaterialFlags ?? new List<string>();
                        if (!items.SequenceEqual(modelFlags))
                        {
                            Console.Error.WriteLine($"  ❌ {mat.Name}: shaderFlags 自校验失败（字节 [{string.Join(",", items)}] vs 模型 [{string.Join(",", modelFlags)}]）");
                            failed++;
                            continue;
                        }
                        float curAlpha = BitConverter.ToSingle(raw, pos.alpha);
                        string curBlend = Encoding.UTF8.GetString(raw, pos.blend + 4, pos.blendEnd - pos.blend - 4);
                        if (Math.Abs(curAlpha - mat.AlphaTest) > 1e-6 || curBlend != mat.BlendMode)
                        {
                            Console.Error.WriteLine($"  ❌ {mat.Name}: alphaTest/blend 自校验失败（字节 {curAlpha}/{curBlend} vs 模型 {mat.AlphaTest}/{mat.BlendMode}）");
                            failed++;
                            continue;
                        }

                        // ── 算新值 ──
                        var newItems = new List<string>(items);
                        foreach (var f in toRemove) newItems.Remove(f);
                        foreach (var f in toAdd) if (!newItems.Contains(f)) newItems.Add(f);
                        bool flagsChanged = !newItems.SequenceEqual(items);
                        bool alphaChanged = alphaTest.HasValue && Math.Abs(alphaTest.Value - curAlpha) > 1e-6;
                        bool blendChanged = !string.IsNullOrEmpty(blend) && blend != curBlend;

                        if (!flagsChanged && !alphaChanged && !blendChanged)
                        {
                            Console.WriteLine($"  {mat.Name,-46} 不变（flags=[{string.Join(",", items)}] alphaTest={curAlpha} blend={curBlend}）");
                            continue;
                        }

                        // ── 重建：前缀 + [新 blend] + 中段 + [新 alphaTest] + 后段 + [新 flags] + 后缀 ──
                        var ms = new MemoryStream();
                        var w = new BinaryWriter(ms);
                        ms.Write(raw, 0, pos.blend);
                        if (blendChanged) { var b = Encoding.UTF8.GetBytes(blend); w.Write(b.Length); w.Write(b); }
                        else ms.Write(raw, pos.blend, pos.blendEnd - pos.blend);
                        ms.Write(raw, pos.blendEnd, pos.alpha - pos.blendEnd);
                        if (alphaChanged) w.Write(alphaTest.Value);
                        else ms.Write(raw, pos.alpha, 4);
                        ms.Write(raw, pos.alpha + 4, pos.flags - (pos.alpha + 4));
                        if (flagsChanged)
                        {
                            w.Write(newItems.Count);
                            foreach (var s in newItems)
                            {
                                var bytes = Encoding.UTF8.GetBytes(s);
                                w.Write(bytes.Length);
                                w.Write(bytes);
                            }
                        }
                        else ms.Write(raw, pos.flags, flagsEnd - pos.flags);
                        ms.Write(raw, flagsEnd, raw.Length - flagsEnd);
                        w.Flush();
                        mat.RawMeta = ms.ToArray();

                        // 同步对象模型（Save 走 RawMeta，同步只为回读比对时两口径一致）
                        if (flagsChanged) { mat.ShaderMaterialFlags.Clear(); mat.ShaderMaterialFlags.AddRange(newItems); }
                        if (alphaChanged) mat.AlphaTest = alphaTest.Value;
                        if (blendChanged) mat.BlendMode = blend;

                        Console.WriteLine($"  {mat.Name,-46}"
                            + (blendChanged ? $" blend {curBlend}→{blend}" : "")
                            + (alphaChanged ? $" alphaTest {curAlpha:F6}→{alphaTest.Value:F6}" : "")
                            + (flagsChanged ? $" flags [{string.Join(",", items)}]→[{string.Join(",", newItems)}]" : "")
                            + $"  ({raw.Length} → {mat.RawMeta.Length} B)");
                        changed++;
                        changedHere++;
                    }

                    if (changedHere == 0) continue;

                    string outPath;
                    if (inPlace)
                    {
                        // 🔴 `--inplace` 同 clipflags：源包还被读句柄占着，Save 写同一个路径会抛。
                        //    可靠做法 = 先 `--out` 到独立目录，再用 PowerShell 拷回。
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
                            Console.Error.WriteLine("     => 请改用 --out <目录> 先出到独立目录，再用 PowerShell Copy-Item 拷回交付包");
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

                    // ── 回读验证：① 命中材质的四个字段 ② 整个包里除命中项外每个资产的 RawMeta 逐字节不变 ──
                    var srcPkg = new AssetPackage(file, true, false);
                    var back = new AssetPackage(outPath, true, false);
                    bool okAll = back.Guid.Equals(pkg.Guid) && back.Items.Count == pkg.Items.Count;
                    if (!okAll) Console.Error.WriteLine($"    ❌ 包结构不符：guid 一致={back.Guid.Equals(pkg.Guid)} items {back.Items.Count}/{pkg.Items.Count}");

                    var changedNames = back.Items.OfType<Material>()
                        .Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        .Select(i => i.Name).ToHashSet();

                    int bad = 0;
                    foreach (var bm in back.Items.OfType<Material>().Where(i => changedNames.Contains(i.Name)))
                    {
                        var orig = mats.First(m => m.Name == bm.Name);
                        bool okFlags = (bm.ShaderMaterialFlags ?? new List<string>()).SequenceEqual(orig.ShaderMaterialFlags ?? new List<string>());
                        bool okAlpha = Math.Abs(bm.AlphaTest - orig.AlphaTest) < 1e-6;
                        bool okBlend = bm.BlendMode == orig.BlendMode;
                        bool okTex = bm.Textures.Count == orig.Textures.Count;
                        if (!okFlags || !okAlpha || !okBlend || !okTex)
                        {
                            bad++;
                            Console.Error.WriteLine($"    ❌ 回读 {bm.Name}: flags={okFlags} alphaTest={okAlpha} blend={okBlend} 贴图数={okTex}");
                        }
                    }

                    int rawChecked = 0, rawDiff = 0;
                    foreach (var it in back.Items)
                    {
                        var src = srcPkg.Items.FirstOrDefault(x => x.Name == it.Name && x.Type == it.Type);
                        if (src?.RawMeta == null || it.RawMeta == null) continue;
                        rawChecked++;
                        if (!changedNames.Contains(it.Name) && !src.RawMeta.SequenceEqual(it.RawMeta))
                        {
                            rawDiff++;
                            if (rawDiff <= 5) Console.Error.WriteLine($"    ⚠ 未命中资产的元数据被动过: {it.Name}");
                        }
                    }

                    bool pass = okAll && bad == 0 && rawDiff == 0;
                    Console.WriteLine($"  回读: items {back.Items.Count}/{pkg.Items.Count} · 材质异常 {bad} 条 · 元数据逐字节比对 {rawChecked} 项（无改动 {rawDiff} 项）");
                    Console.WriteLine(pass ? "  ✅ 写入生效且其余资产逐字节未动" : "  ❌ 校验不过 —— 别用这个产物");
                    if (!pass) failed++;
                }
            }

            if (packs == 0) { Console.Error.WriteLine($"没有哪个包里有名字含 '{filter}' 的 Material"); return 1; }
            Console.WriteLine($"共 {packs} 个包 / 改动 {changed} 个材质，失败 {failed} 项");
            return failed == 0 ? 0 : 1;
        }

        private static List<string> Split(string csv) =>
            string.IsNullOrWhiteSpace(csv)
                ? new List<string>()
                : csv.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        private struct Pos { public int blend, blendEnd, alpha, flags; }

        /// <summary>按 `Material.ReadMetadata` 的顺序走到三个目标字段的偏移。</summary>
        private static bool Locate(byte[] raw, out Pos pos, out string err)
        {
            pos = default(Pos);
            err = null;
            try
            {
                int p = 0;
                p += 4;                                  // Version
                p += 16;                                 // BillboardGuid
                p += 4;                                  // SubVersion
                p += 4;                                  // UnknownUint1
                p = SkipStringList(raw, p);              // Flags（no_depth_test 那一串）
                p += 4;                                  // UnknownUint2
                p = SkipStringList(raw, p);              // VertexLayoutFlags
                pos.blend = p;
                p = SkipSizedString(raw, p);             // BlendMode
                pos.blendEnd = p;
                p += 16;                                 // Shader guid
                int texCount = BitConverter.ToInt32(raw, p); p += 4;
                if (texCount < 0 || texCount > 64) { err = $"texCount 异常 ({texCount})"; return false; }
                p += texCount * 20;                      // 每条 = i32 槽位 + guid 16
                pos.alpha = p;
                p += 4;                                  // AlphaTest
                pos.flags = p;
                if (p < 0 || p + 4 > raw.Length) { err = "越界"; return false; }
                return true;
            }
            catch (Exception e) { err = e.Message; return false; }
        }

        private static int SkipStringList(byte[] raw, int p)
        {
            int count = BitConverter.ToInt32(raw, p);
            if (count < 0 || count > 256) throw new Exception($"string list 条数异常 ({count})");
            int q = p + 4;
            for (int i = 0; i < count; i++) q = SkipSizedString(raw, q);
            return q;
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
            if (count < 0 || count > 256) return (-1, null, p);
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
