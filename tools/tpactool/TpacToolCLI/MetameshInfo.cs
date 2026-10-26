using System;
using System.Collections.Generic;
using System.Linq;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// metameshinfo：打印每个 Metamesh 的 **LOD 结构**（档位 / 面数 / 顶点 / 材质 / 最大距离）。
    ///
    /// 用途：研究原版资产怎么分档减面（我们照着定减面比率），以及验收自己生成的 LOD 链。
    /// 只读元数据，不加载顶点流，跑得很快。
    ///
    /// 用法：tpaccli metameshinfo --packdir &lt;dir&gt; [--filter 名字子串] [--onlymulti]
    /// 输出（TSV）：name / lods / maxdist / lod / faces / verts / pos / materials
    /// </summary>
    public static class MetameshInfo
    {
        public static int Run(IReadOnlyList<AssetItem> assets, string filter, bool onlyMulti)
        {
            var items = assets.OfType<Metamesh>()
                .Where(m => filter == null || m.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (items.Count == 0)
            {
                Console.WriteLine("no metamesh matched");
                return 1;
            }

            var matName = new Dictionary<Guid, string>();
            foreach (var m in assets.OfType<Material>())
                matName[m.Guid] = m.Name;

            Console.WriteLine("name\tlods\tmaxdist\tlod\tfaces\tverts\tpos\tmaterials");
            int shown = 0;
            foreach (var mm in items)
            {
                var lods = mm.Meshes.GroupBy(x => x.Lod).OrderBy(g => g.Key).ToList();
                if (onlyMulti && lods.Count < 2) continue;
                if (lods.Count == 0) continue;
                shown++;
                foreach (var g in lods)
                {
                    int faces = g.Sum(x => x.FaceCount);
                    int verts = g.Sum(x => x.VertexCount);
                    int pos = g.Sum(x => x.PositionCount);
                    var mats = string.Join("|", g.Select(x =>
                        x.Material != null && matName.TryGetValue(x.Material.Guid, out var n)
                            ? n : x.Material?.Guid.ToString().Substring(0, 8) ?? "?")
                        .Distinct());
                    Console.WriteLine($"{mm.Name}\t{lods.Count}\t{mm.UnknownFloat:F1}\t{g.Key}\t{faces}\t{verts}\t{pos}\t{mats}");
                }
            }
            Console.WriteLine($"# metameshes shown = {shown}");
            return 0;
        }
    }
}
