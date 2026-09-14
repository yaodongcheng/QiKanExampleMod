using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// metaparts —— 重排 / 裁剪 Metamesh 的子网格列表（只动元数据，几何与顶点流原样保留）。
    ///
    /// 动机（2026-09-13 晚，蒂法换头工程实机诊断）：
    ///   脸部件贴图**不是**按 MaterialFlags 认的，而是按子网格的【顺序 / 数量】认的——
    ///   原版 head_female_a 与能跑的参照 mod xxFemaleHead 都是 **4 件、顺序 = 脸→嘴→眼→睫**；
    ///   我们的头是 **6 件（脸→嘴→睫→眼影→眼→眉）**，眼排在第 5 位、还多出眼影/眉两件，
    ///   超范围的部分运行时落到"脸皮材质"上 → 实机症状：眼球/嘴/眉全糊上脸的贴图
    ///   （证据：包里的 head_tifa_a_d.png 是一整张脸，实机眼球上那张小脸与它同源）。
    ///
    /// 本命令用于**零成本验证**：直接在已编译的包上重排子网格，不必重导 FBX、不必开编辑器。
    /// 真正修法仍应落到 FBX（对象数量与顺序），本命令只是先验证结论。
    ///
    /// 用法：
    ///   tpaccli metaparts --packdir &lt;dir&gt; --filter &lt;mesh名子串&gt;                      # 只列出，不改
    ///   tpaccli metaparts --packdir &lt;dir&gt; --filter &lt;mesh名子串&gt; --out &lt;dir&gt; --order 0,1,4,2
    ///     --order = 要保留的子网格【原始下标】，按给定顺序排列；未列出的被移除。
    /// </summary>
    public static class MetaParts
    {
        public static int Run(string dir, string filter, string outDir, string order, bool clearFlags = false)
        {
            var pkg = MorphFix.LoadPackages(dir, filter, out var metas);
            if (pkg == null) return 1;

            var matByGuid = new Dictionary<Guid, Material>();
            foreach (var m in pkg.Items.OfType<Material>()) matByGuid[m.Guid] = m;

            string MatName(Guid g)
            {
                if (g == Guid.Empty) return "(空)";
                return matByGuid.TryGetValue(g, out var mm) ? mm.Name : g.ToString();
            }

            foreach (var meta in metas)
                ListOne(meta, MatName);

            if (order == null && !clearFlags)
            {
                Console.WriteLine("（未给 --order / --clearflags，仅列出。加 --order 0,1,4,2 或 --clearflags 才会改并输出新包）");
                return 0;
            }

            int touched = 0;

            // ---- 清空 MaterialFlags ----
            // 为什么需要：`face_base_mesh` 等标记是给【引擎生成脸部贴图】用的 —— 引擎会按**原版画布布局**
            // 把五官画到脸贴图上。自定义头若沿用源模型自带的 UV 布局（战无2 头挤在贴图左上 1/8 区域），
            // 两边就对不上 → 实机症状：眼睛/嘴糊成一片、头发那块被涂成肤色（2026-09-14 织田信长实机）。
            // 两个实机通过的自定义头（蒂法/萨菲罗斯）MaterialFlags **都是空的**（引擎不生成、直接用自带贴图）。
            if (clearFlags)
            {
                int n = 0;
                foreach (var meta in metas)
                {
                    bool dirty = false;
                    foreach (var mesh in meta.Meshes)
                    {
                        if (mesh.MaterialFlags == null || mesh.MaterialFlags.Count == 0) continue;
                        Console.WriteLine($"  [clearflags] {mesh.Name}: [{string.Join("|", mesh.MaterialFlags)}] -> []");
                        mesh.MaterialFlags.Clear();
                        dirty = true;
                        n++;
                    }
                    if (dirty) { meta.RawMeta = null; touched++; }   // 标记存在元数据里，不清 RawMeta 写不回去
                }
                Console.WriteLine($"clearflags: 清空 {n} 格材质标记");
            }

            if (order != null)
            {
                int[] idx;
                try { idx = order.Split(',').Select(s => int.Parse(s.Trim())).ToArray(); }
                catch { Console.Error.WriteLine("--order 解析失败，应形如 0,1,4,2"); return 1; }

                foreach (var meta in metas)
                {
                    var old = meta.Meshes.ToList();
                    foreach (var i in idx)
                        if (i < 0 || i >= old.Count)
                        {
                            Console.Error.WriteLine($"--order 下标 {i} 超出范围 0..{old.Count - 1}（{meta.Name} 只有 {old.Count} 件）");
                            return 1;
                        }
                    var kept = idx.Select(i => old[i]).ToList();
                    if (kept.Count == old.Count && kept.SequenceEqual(old))
                    {
                        Console.WriteLine($"  [skip] {meta.Name}: 顺序未变，无需改动");
                        continue;
                    }
                    Console.WriteLine($"  [reorder] {meta.Name}: 原 [{string.Join(",", Enumerable.Range(0, old.Count))}]"
                                    + $" -> 新 [{string.Join(",", idx)}]，移除 {old.Count - kept.Count} 件");
                    for (int n = 0; n < kept.Count; n++)
                        Console.WriteLine($"        新[{n}] = 原[{idx[n]}] {kept[n].Name}");

                    meta.Meshes.Clear();
                    meta.Meshes.AddRange(kept);
                    // 子网格列表存在 metamess 元数据里：不清 RawMeta 就写不回去（同 morphfix 的教训）
                    meta.RawMeta = null;
                    touched++;
                }
            }

            if (touched == 0)
            {
                Console.WriteLine("无改动，不输出文件。");
                return 0;
            }

            string outPath = Path.Combine(outDir ?? ".", pkg.File.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            pkg.Save(outPath);
            Console.WriteLine($"saved {outPath} ({new FileInfo(outPath).Length:N0} bytes)");

            // 回读自检：确认新顺序真的落盘了
            var back = new AssetPackage(outPath, true, false);
            Console.WriteLine("---- AFTER（回读）----");
            foreach (var meta in back.Items.OfType<Metamesh>()
                         .Where(m => m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                ListOne(meta, g => g.ToString());
            return 0;
        }

        static void ListOne(Metamesh meta, Func<Guid, string> matName)
        {
            Console.WriteLine($"== {meta.Name}  子网格 {meta.Meshes.Count}  default_material={meta.Material} ==");
            for (int i = 0; i < meta.Meshes.Count; i++)
            {
                var mesh = meta.Meshes[i];
                var flags = mesh.MaterialFlags == null || mesh.MaterialFlags.Count == 0
                    ? "(空)" : string.Join("|", mesh.MaterialFlags);
                var mname = matName(mesh.Material?.Guid ?? Guid.Empty);
                Console.WriteLine($"   [{i}] {mesh.Name,-22} mat={mname,-24} flags={flags,-18} lod={mesh.Lod} verts={mesh.VertexCount}");
            }
        }
    }
}
