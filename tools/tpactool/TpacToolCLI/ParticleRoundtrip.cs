using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// prtroundtrip: 粒子资产的**写侧闸门** —— 把包里的粒子数据读出来、丢掉原始字节、
    /// 用自己的序列化器重新写一遍，与原始字节**逐字节比对**。
    ///
    /// 为什么需要它：`ParticleEffectData` 读侧存在而写侧是我们补的；写侧只要有一个字段
    /// 顺序/类型对不上，引擎读到的就是垃圾（而且不报错）。逐字节一致 = 写侧正确。
    ///
    /// 用法: tpaccli prtroundtrip --packdir &lt;dir&gt; --filter &lt;粒子名子串&gt;
    /// </summary>
    public static class ParticleRoundtrip
    {
        public static int Run(string dir, string filter)
        {
            if (dir == null || filter == null)
            {
                Console.Error.WriteLine("prtroundtrip requires --packdir <dir> and --filter <粒子名子串>");
                return 1;
            }

            var mgr = new AssetManager();
            mgr.Load(new DirectoryInfo(dir));

            int seen = 0, ok = 0, fail = 0, nodata = 0;
            foreach (var p in mgr.LoadedPackages)
            {
                foreach (var item in p.Items.OfType<Particle>())
                {
                    if (!item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        continue;
                    seen++;

                    var loader = item.TypelessDataSegments
                        .OfType<ExternalLoader<ParticleEffectData>>().FirstOrDefault();
                    if (loader == null)
                    {
                        Console.WriteLine($"  {item.Name}: 没有粒子数据段（段数 {item.TypelessDataSegments.Count}）");
                        nodata++;
                        continue;
                    }

                    ParticleEffectData data;
                    try
                    {
                        data = loader.Data; // 惰性加载，取一次即触发
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  {item.Name}: 读取失败 {ex.GetType().Name}: {ex.Message}");
                        fail++;
                        continue;
                    }
                    if (data == null || data.RawData == null)
                    {
                        Console.WriteLine($"  {item.Name}: RawData 为空（没走我们的 ReadData？）");
                        nodata++;
                        continue;
                    }

                    byte[] raw = data.RawData;
                    data.DiscardRawData(); // 强制走自序列化
                    byte[] rebuilt;
                    using (var ms = new MemoryStream())
                    {
                        using (var w = new BinaryWriter(ms, Encoding.UTF8))
                        {
                            data.WriteData(w, new Dictionary<object, object>());
                            w.Flush();
                        }
                        rebuilt = ms.ToArray();
                    }

                    if (raw.Length == rebuilt.Length && raw.AsSpan().SequenceEqual(rebuilt))
                    {
                        Console.WriteLine($"  {item.Name}: OK ({raw.Length} bytes, 逐字节一致)");
                        ok++;
                    }
                    else
                    {
                        int first = -1;
                        int n = Math.Min(raw.Length, rebuilt.Length);
                        for (int i = 0; i < n; i++)
                        {
                            if (raw[i] != rebuilt[i]) { first = i; break; }
                        }
                        if (first < 0) first = n; // 前缀相同，只是长度不同
                        Console.WriteLine($"  {item.Name}: ✗ 不一致 —— 原 {raw.Length} / 新 {rebuilt.Length} 字节，" +
                                          $"首个差异 @{first}（原 {Hex(raw, first)} vs 新 {Hex(rebuilt, first)}）");
                        fail++;
                    }
                }
            }

            Console.WriteLine($"== 命中 {seen} 个粒子：一致 {ok} · 不一致 {fail} · 无数据 {nodata} ==");
            return fail == 0 && ok > 0 ? 0 : (ok > 0 ? 2 : 1);
        }

        static string Hex(byte[] buf, int offset)
        {
            if (offset >= buf.Length) return "<末尾>";
            int n = Math.Min(8, buf.Length - offset);
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++) sb.Append(buf[offset + i].ToString("X2")).Append(' ');
            return sb.ToString().TrimEnd();
        }
    }
}
