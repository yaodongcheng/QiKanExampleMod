using System;
using System.IO;
using System.Linq;
using System.Numerics;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// shapedump —— 打印 `PhysicsShape` 资产的**尺寸**（凸包顶点的包围盒）。
    ///
    /// 为什么需要（2026-10-19）：承载板 `lwn_flight_sigil_body` 的物理体实测 5.11 × 4.92 m，
    /// 而作者预期 1.59 × 1.55 —— 预制体里的 `scale` **对物理体不生效**，而物理体尺寸来自
    /// `<physics shape="bo_xxx">` 引用的**引擎 shape 资产**（就在 tpac 包里，不是 XML）。
    /// 换 shape 之前必须先量：足迹多大、顶面在本地 z 的哪里（= `FlightTuning.CarrierTopLocalZ`）。
    ///
    /// 用法: tpaccli shapedump --packdir &lt;目录&gt; --filter &lt;shape 名子串&gt;
    /// 输出: 名字 / 顶点数 / 包围盒 X×Y×Z / 本地 z 范围 / capsule·sphere·manifold 数
    /// </summary>
    public static class ShapeDump
    {
        public static int Run(string dir, string filter)
        {
            if (dir == null)
            {
                Console.Error.WriteLine("shapedump requires --packdir <dir> [--filter <shape 名子串>]");
                return 1;
            }

            var mgr = new AssetManager();
            mgr.Load(new DirectoryInfo(dir));

            int n = 0;
            foreach (var p in mgr.LoadedPackages)
            {
                foreach (var item in p.Items.OfType<PhysicsShape>())
                {
                    if (!string.IsNullOrEmpty(filter)
                        && !item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var loader = item.TypelessDataSegments
                        .OfType<ExternalLoader<PhysicsDescriptionData>>().FirstOrDefault();
                    if (loader == null)
                    {
                        Console.WriteLine($"{item.Name}: 无 PhysicsDescriptionData 段");
                        continue;
                    }

                    PhysicsDescriptionData d;
                    try { d = loader.Data; }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{item.Name}: 读取失败 {ex.GetType().Name}: {ex.Message}");
                        continue;
                    }

                    Vector3 mn = new(float.MaxValue), mx = new(float.MinValue);
                    int verts = 0;
                    foreach (var m in d.Manifolds)
                    {
                        foreach (var v in m.Vertices)
                        {
                            mn = Vector3.Min(mn, v);
                            mx = Vector3.Max(mx, v);
                            verts++;
                        }
                    }

                    string size = verts > 0
                        ? $"box {(mx.X - mn.X):0.###} x {(mx.Y - mn.Y):0.###} x {(mx.Z - mn.Z):0.###}"
                          + $"  x[{mn.X:0.###},{mx.X:0.###}] y[{mn.Y:0.###},{mx.Y:0.###}] z[{mn.Z:0.###},{mx.Z:0.###}]"
                        : "（无 manifold —— 可能是纯 capsule/sphere）";
                    n++;
                    Console.WriteLine($"{item.Name,-38} v={verts,-5} {size}"
                        + $"  capsules={d.Capsules.Count} spheres={d.Spheres.Count} manifolds={d.Manifolds.Count}");
                }
            }

            Console.WriteLine($"-- {n} 个 shape（filter='{filter}'）");
            return 0;
        }
    }
}
