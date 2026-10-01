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
    ///   · 每条 LOD 的**顶点色 alpha 分布**（布料涂装：0 = 固定跟骨，>0 = 可自由漂移的半径）
    ///   · 每条 LOD 的 mesh flags / mesh material flags / 材质 shader flags（2026-10-01 加：
    ///     `cloth_simulation` 这个开关就挂在这三个列表之一上，具体哪层待定）
    ///
    /// 为什么要它：<c>meshdiff</c> 能看这些字段，但它一次吐 600 行、要人工筛；
    /// 而布料的每次验收（编辑器出来对不对、装机后丢没丢）都只看这几行。
    ///
    /// 🔴 两个口径坑（2026-10-01 实测原版缰绳时踩的）：
    ///   ① **顶点 alpha 非零 ≠ 开了布料** —— 原版普通网格（如 nasal_helmet_reinforced）顶点色
    ///      默认就是全白 alpha=1.0。布料的签名是**部分顶点被刻意涂成 0**（固定端）+ 其余高 alpha。
    ///   ② **ClothMetamesh 为空 ≠ 没布料** —— 那是「直接模拟」（不另建低模）的正常形态，
    ///      原版缰绳 19 套全是这样。判定行里的「未接线」只说明没有独立模拟网格，别当故障读。
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
                int alphaMeshes = 0, alphaVerts = 0, clothFlagged = 0;
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
                    // flags 里带 cloth_simulation = 这个子网格真的开了布料（原版凡开布料的网格都有它；
                    // 光看 alpha 会把普通网格的默认白顶点色误认成布料）
                    var matAsset = byGuid.TryGetValue(mesh.Material?.Guid ?? Guid.Empty, out var m0) ? m0 as Material : null;
                    var shaderFlags = matAsset?.ShaderMaterialFlags ?? new List<string>();
                    bool isCloth = mesh.Flags.Concat(mesh.MaterialFlags).Concat(shaderFlags)
                        .Any(f => f.IndexOf("cloth", StringComparison.OrdinalIgnoreCase) >= 0);
                    W($"    meshFlags=[{string.Join(",", mesh.Flags)}]  meshMatFlags=[{string.Join(",", mesh.MaterialFlags)}]"
                      + $"  shader[{matAsset?.Name ?? "?"}]=[{string.Join(",", shaderFlags)}]"
                      + $"  ubool={mesh.UnknownBool1}/{mesh.UnknownBool2}/{mesh.UnknownBool3}  ufloat1={mesh.UnknownFloat1}  ufloat3={mesh.UnknownInt3}");
                    if (isCloth) clothFlagged++;
                    if (nz > 0) { alphaMeshes++; alphaVerts += nz; }
                }
                W($"  >> 判定：{(wired ? "已接线（模拟网格 " + simName + "）" : "未接线（ClothMetamesh 为空）")}"
                  + $"，{clothFlagged}/{meta.Meshes.Count} 级 LOD 带 cloth_simulation flag"
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
