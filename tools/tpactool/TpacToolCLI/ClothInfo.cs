using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// clothinfo —— 只打「布料」相关的字段，行数少到能一眼看完。
    ///
    /// 打什么：
    ///   · Metamesh 的 <c>ClothMetamesh</c>（指向哪张模拟网格）/ <c>UnknownString</c>（碰撞体预设名）/
    ///     <c>ClothUint</c> / <c>ClothString</c>
    ///   · 每条 LOD 的 <c>ClothingMaterial</c>（一整套布料物理参数）
    ///   · 每条 LOD 的**顶点色 alpha 分布**（布料的总开关：>0 = 这个顶点挂到模拟网格）
    ///
    /// 为什么要它：<c>meshdiff</c> 能看这些字段，但它一次吐 600 行、要人工筛；
    /// 而布料的每次验收（编辑器出来对不对、装机后丢没丢）都只看这几行。
    ///
    /// 用法: tpaccli clothinfo --packdir &lt;目录&gt; --filter &lt;网格名子串&gt; [--out &lt;txt&gt;]
    /// </summary>
    public static class ClothInfo
    {
        public static int Run(string dir, string filter, string outFile)
        {
            var pkg = MorphFix.LoadPackages(dir, filter, out var metas);
            if (pkg == null) return 1;
            var byGuid = pkg.Items.ToDictionary(i => i.Guid, i => i);

            var sb = new System.Text.StringBuilder();
            void W(string s) { sb.AppendLine(s); }

            foreach (var meta in metas)
            {
                W($"########## Metamesh {meta.Name} ##########");
                string simName = null;
                if (meta.ClothMetamesh != Guid.Empty)
                    simName = byGuid.TryGetValue(meta.ClothMetamesh, out var sm) ? sm.Name : "(不在本包内)";
                W($"  ClothMetamesh = {(meta.ClothMetamesh == Guid.Empty ? "(空)" : meta.ClothMetamesh.ToString())}"
                  + (simName != null ? "  -> " + simName : ""));
                W($"  UnknownString = \"{meta.UnknownString}\"        <- 碰撞体预设名（cape_body / human_body / cloak_body …）");
                W($"  ClothUint = {meta.ClothUint}   ClothString = \"{meta.ClothString}\"");

                bool wired = meta.ClothMetamesh != Guid.Empty;
                int alphaMeshes = 0, alphaVerts = 0;
                foreach (var mesh in meta.Meshes)
                {
                    var cm = mesh.ClothingMaterial;
                    var vs = mesh.VertexStream?.Data;
                    var cols = vs?.Colors1;
                    int nz = 0; float mx = 0f; var hist = new SortedDictionary<int, int>();
                    if (cols != null)
                    {
                        foreach (var c in cols)
                        {
                            if (c.A == 0) continue;
                            nz++;
                            float a = c.A / 255f;
                            if (a > mx) mx = a;
                            int k = (int)Math.Round(a * 100);
                            hist[k] = hist.TryGetValue(k, out var n) ? n + 1 : 1;
                        }
                    }
                    W($"  ---- LOD{mesh.Lod} {mesh.Name}  v={mesh.VertexCount} ----");
                    W($"    ClothingMaterial: name=\"{cm?.Name}\" bend={cm?.BendingStiffness} shear={cm?.ShearingStiffness}"
                      + $" stretch={cm?.StretchingStiffness} anchor={cm?.AnchorStiffness} damp={cm?.Damping}"
                      + $" grav={cm?.Gravity} inertia={cm?.LinearInertia} wind={cm?.Wind} airdrag={cm?.AirDragMultiplier}");
                    W($"    顶点色 alpha: 非零 {nz}/{cols?.Length ?? 0}  最大 {(cols == null ? "-" : mx.ToString("F3"))}"
                      + (hist.Count > 0 ? "  分布[" + string.Join(" ", hist.Select(kv => $"{kv.Key / 100.0:F2}:{kv.Value}")) + "]" : ""));
                    if (nz > 0) { alphaMeshes++; alphaVerts += nz; }
                }
                W($"  >> 判定：{(wired ? "已接线（模拟网格 " + simName + "）" : "⚠️ 未接线（ClothMetamesh 为空）")}"
                  + $"，{alphaMeshes}/{meta.Meshes.Count} 级 LOD 有非零 alpha（共 {alphaVerts} 顶点）");
                W("");
            }

            string text = sb.ToString();
            if (outFile != null) { File.WriteAllText(outFile, text, System.Text.Encoding.UTF8); Console.WriteLine($"written {outFile} ({text.Length} chars)"); }
            else Console.Write(text);
            return 0;
        }
    }
}
