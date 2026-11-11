using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// shapedump —— 打印 `PhysicsShape` 资产的**尺寸**（凸包顶点的包围盒），并可把几何导出成 OBJ。
    ///
    /// 为什么需要（2026-10-19）：承载板 `lwn_flight_sigil_body` 的物理体实测 5.11 × 4.92 m，
    /// 而作者预期 1.59 × 1.55 —— 预制体里的 `scale` **对物理体不生效**，而物理体尺寸来自
    /// `<physics shape="bo_xxx">` 引用的**引擎 shape 资产**（就在 tpac 包里，不是 XML）。
    /// 换 shape 之前必须先量：足迹多大、顶面在本地 z 的哪里（= `FlightTuning.CarrierTopLocalZ`）。
    ///
    /// 2026-11-11 增补：加 `--out` 导出 OBJ —— 用来研究原版建筑碰撞体到底长什么样
    /// （立方体拼接 / 减面壳 / 凸包）。OBJ 按 manifold 合并成一个网格，Z 向上、单位米。
    ///
    /// 用法: tpaccli shapedump --packdir &lt;目录&gt; --filter &lt;shape 名子串&gt; [--out &lt;目录&gt;]
    /// 输出: 名字 / 顶点数 / 包围盒 X×Y×Z / 本地 z 范围 / capsule·sphere·manifold 数
    /// </summary>
    public static class ShapeDump
    {
        public static int Run(string dir, string filter, string outDir)
        {
            if (dir == null)
            {
                Console.Error.WriteLine("shapedump requires --packdir <dir> [--filter <shape 名子串>] [--out <obj 目录>]");
                return 1;
            }

            if (!string.IsNullOrEmpty(outDir))
                Directory.CreateDirectory(outDir);

            var mgr = new AssetManager();
            mgr.Load(new DirectoryInfo(dir));

            int n = 0;
            var diskSizes = new System.Collections.Generic.List<long>();
            long totalDisk = 0, totalRaw = 0;
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
                    long disk = 0, raw = 0;
                    foreach (var seg in item.TypelessDataSegments)
                    {
                        disk += (long)seg.StorageSize;
                        raw += (long)seg.ActualSize;
                    }
                    diskSizes.Add(disk);
                    totalDisk += disk;
                    totalRaw += raw;

                    Console.WriteLine($"{item.Name,-38} v={verts,-5} {size}"
                        + $"  capsules={d.Capsules.Count} spheres={d.Spheres.Count} manifolds={d.Manifolds.Count}"
                        + $"  disk={disk,7} raw={raw,7}");

                    if (!string.IsNullOrEmpty(outDir))
                    {
                        try { WriteObj(Path.Combine(outDir, Sanitize(item.Name) + ".obj"), d); }
                        catch (Exception ex) { Console.WriteLine($"    OBJ 写入失败: {ex.Message}"); }
                    }
                }
            }

            Console.WriteLine($"-- {n} 个 shape（filter='{filter}'）");
            if (diskSizes.Count > 0)
            {
                diskSizes.Sort();
                long med = diskSizes[diskSizes.Count / 2];
                long p90 = diskSizes[(int)(diskSizes.Count * 0.90)];
                Console.WriteLine($"-- 体积: 合计 disk={Fmt(totalDisk)} raw={Fmt(totalRaw)}"
                    + $"  中位={Fmt(med)}  p90={Fmt(p90)}  最大={Fmt(diskSizes[diskSizes.Count - 1])}");
            }
            return 0;
        }

        private static string Fmt(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            return (bytes / (1024.0 * 1024 * 1024)).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        }

        /// <summary>
        /// 把一个 shape 的所有 manifold 合并写成一个 OBJ。四边面（Index4 有效且异于 Index3）
        /// 写成 `f a b c d`，否则写成三角形。
        /// </summary>
        private static void WriteObj(string path, PhysicsDescriptionData d)
        {
            var sb = new StringBuilder();
            sb.Append("# physics shape: ").Append(d.Name).Append('\n');
            sb.Append("# capsules=").Append(d.Capsules.Count)
              .Append(" spheres=").Append(d.Spheres.Count)
              .Append(" manifolds=").Append(d.Manifolds.Count).Append('\n');

            int offset = 0;
            int faceTotal = 0;
            foreach (var m in d.Manifolds)
            {
                foreach (var v in m.Vertices)
                    sb.Append("v ").Append(F(v.X)).Append(' ').Append(F(v.Y)).Append(' ').Append(F(v.Z)).Append('\n');

                foreach (var f in m.Faces)
                {
                    int i1 = f.Index1, i2 = f.Index2, i3 = f.Index3, i4 = f.Index4;
                    if (i1 < 0 || i2 < 0 || i3 < 0) continue;
                    bool quad = i4 >= 0 && i4 != i3;
                    sb.Append("f ").Append(i1 + offset + 1).Append(' ').Append(i2 + offset + 1).Append(' ').Append(i3 + offset + 1);
                    if (quad) sb.Append(' ').Append(i4 + offset + 1);
                    sb.Append('\n');
                    faceTotal++;
                }

                offset += m.Vertices.Length;
            }

            sb.Append("# faces=").Append(faceTotal).Append('\n');
            File.WriteAllText(path, sb.ToString());
        }

        private static string F(float v)
        {
            return v.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string Sanitize(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' ? c : '_');
            return sb.ToString();
        }
    }
}
