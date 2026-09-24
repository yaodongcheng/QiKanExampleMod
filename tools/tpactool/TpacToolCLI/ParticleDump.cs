using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// prtdump: 把粒子资产里**解析出来的字段**全部打出来 —— 用来跟同名 XML 逐条对账，
    /// 把「XML 参数 → 二进制字段」的表钉死（没名字的 F/U/I/V 字段靠已知值反推）。
    ///
    /// 用法: tpaccli prtdump --packdir &lt;dir&gt; --filter &lt;粒子名子串&gt;
    /// </summary>
    public static class ParticleDump
    {
        static string V(Vector4 v) => $"({v.X:0.###},{v.Y:0.###},{v.Z:0.###},{v.W:0.###})";
        static string V2(Vector2 v) => $"({v.X:0.###},{v.Y:0.###})";

        public static int Run(string dir, string filter)
        {
            if (dir == null || filter == null)
            {
                Console.Error.WriteLine("prtdump requires --packdir <dir> and --filter <粒子名子串>");
                return 1;
            }
            var mgr = new AssetManager();
            mgr.Load(new DirectoryInfo(dir));

            // GUID → 资产名：粒子里的材质/贴图是按 GUID 引用的，不打出来就是天书
            var byGuid = new Dictionary<Guid, AssetItem>();
            foreach (var pk in mgr.LoadedPackages)
            {
                foreach (var it in pk.Items)
                {
                    if (it.Guid != Guid.Empty && !byGuid.ContainsKey(it.Guid))
                        byGuid[it.Guid] = it;
                }
            }
            string R(Guid g)
            {
                if (g == Guid.Empty) return "<空>";
                return byGuid.TryGetValue(g, out var a) ? $"{a.Name} [type {a.Type}]" : "<未在包内找到>";
            }

            var filters = filter.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            bool csv = filters.Contains("--csv");            // 机器可读：一个 emitter 一行，反射把所有字段倒出来
            if (csv) filters = filters.Where(f => f != "--csv").ToArray();
            int n = 0;
            foreach (var p in mgr.LoadedPackages)
            {
                foreach (var item in p.Items.OfType<Particle>())
                {
                    if (filters.Length > 0 && !filters.Any(f => item.Name.Contains(f, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var loader = item.TypelessDataSegments
                        .OfType<ExternalLoader<ParticleEffectData>>().FirstOrDefault();
                    if (loader == null) { Console.WriteLine($"{item.Name}: 无数据段"); continue; }

                    ParticleEffectData d;
                    try { d = loader.Data; }
                    catch (Exception ex) { Console.WriteLine($"{item.Name}: 读取失败 {ex.GetType().Name}"); continue; }

                    n++;
                    Console.WriteLine($"   [asset] guid={item.Guid} version={item.Version} meta={(item.RawMeta?.Length ?? -1)} " +
                                      $"checksum={item.UnknownMetadataChecksum} deps={item.UnknownDependences.Count} " +
                                      $"段 owner={string.Join(",", item.TypelessDataSegments.Select(g => g.OwnerGuid.ToString()))}");
                    Console.WriteLine($"==== {item.Name}  raw {d.RawData?.Length ?? 0} bytes | SoundCode='{d.SoundCode}' | " +
                                      $"前置浮点 {d.UnknownFloats.Count} 个 | emitter {d.Emitters.Count} 个");
                    if (d.UnknownFloats.Count > 0)
                        Console.WriteLine("   UnknownFloats: " + string.Join(", ", d.UnknownFloats.Select(f => f.ToString("0.######"))));

                    for (int ei = 0; ei < d.Emitters.Count; ei++)
                    {
                        var e = d.Emitters[ei];
                        if (csv)
                        {
                            Console.WriteLine("E|" + item.Name + "|" + ei + "|" + CsvFields(e));
                            continue;
                        }
                        Console.WriteLine($"  -- emitter[{ei}]  name='{e.Name}'  subVersion={e.SubVersion}");
                        Console.WriteLine($"     guid: G1={e.G1}  -> {R(e.G1)}");
                        Console.WriteLine($"           G2={e.G2}  -> {R(e.G2)}");
                        Console.WriteLine($"           G3={e.G3}  -> {R(e.G3)}");
                        Console.WriteLine($"           G4={e.G4}  -> {R(e.G4)}");
                        Console.WriteLine($"           G5={e.G5}  -> {R(e.G5)}");
                        Console.WriteLine($"     U2={e.U2}  U3={e.U3}  U4={e.U4}  U5={e.U5}  U6={e.U6}  U7={e.U7}  U8={e.U8}");
                        Console.WriteLine($"     I1={e.I1} I2={e.I2} I3={e.I3} I4={e.I4} I5={e.I5}");
                        Console.WriteLine($"     F1={e.F1:0.######} F2={e.F2:0.######} F3={e.F3:0.######}");
                        Console.WriteLine($"     F4..F11 = {e.F4:0.######}, {e.F5:0.######}, {e.F6:0.######}, {e.F7:0.######}, " +
                                          $"{e.F8:0.######}, {e.F9:0.######}, {e.F10:0.######}, {e.F11:0.######}");
                        Console.WriteLine($"     F16={e.F16:0.######} F17={e.F17:0.######} F19={e.F19:0.######} F20={e.F20:0.######} F21={e.F21:0.######} " +
                                          $"F22={e.F22:0.######} F23={e.F23:0.######} F24={e.F24:0.######} F25={e.F25:0.######} " +
                                          $"F26={e.F26:0.######} F27={e.F27:0.######} F28={e.F28:0.######} F29={e.F29:0.######} " +
                                          $"F30={e.F30:0.######} F31={e.F31:0.######} F32={e.F32:0.######} F34={e.F34:0.######}");
                        Console.WriteLine($"     乘子: backlight={e.BacklightMultiplier:0.###} diffuse={e.DiffuseMultiplier:0.###} " +
                                          $"emissive={e.EmissiveMultiplier:0.###} heatmap={e.HeatmapMultiplier:0.###} coneEmitAngle={e.ConeEmitAngle:0.######}");
                        Console.WriteLine($"     尺寸: base={e.ParticleSizeBase:0.######} bias={e.ParticleSizeBias:0.######}  curveOp='{e.ParticleSizeCurveOp}'");
                        Console.WriteLine($"     精灵: count={e.TextureSpriteCountX},{e.TextureSpriteCountY} frameCount={e.TextureSpriteFrameCount} frameRate={e.TextureSpriteFrameRate:0.###}");
                        Console.WriteLine($"     decal: min={V2(e.DecalMinScale)} max={V2(e.DecalMaxScale)} idx={e.SkinnedDecalStartIndex}..{e.SkinnedDecalEndIndex} " +
                                          $"maxAlive={e.MaxAliveParticleCount}");
                        Console.WriteLine($"     quad: scale={V2(e.QuadScale)} bias={V2(e.QuadBias)}");
                        Console.WriteLine($"     字符串: collision='{e.CollisionBehaviour}' velModel='{e.EmissionVelocityModel}' " +
                                          $"billboard='{e.BillboardType}' volume='{e.EmitVolumeType}'");
                        Console.WriteLine($"             S4='{e.S4}' S3='{e.S3}' sound='{e.EmitterSoundCode}' S11='{e.S11}'");
                        Console.WriteLine($"     向量: V1={V(e.V1)} V2={V(e.V2)}");
                        Console.WriteLine($"           V3={V(e.V3)} V4={V(e.V4)} V5={V(e.V5)}");
                        Console.WriteLine($"           gravity={V(e.Gravity)} V7={V(e.V7)} fixedBillboardDir={V(e.FixedBillboardDirection)}");
                        Console.WriteLine($"     flags({e.Flags.Count}): " + string.Join(", ", e.Flags));
                        Console.WriteLine($"     guidList({e.GuidList.Count}): " + string.Join(" | ", e.GuidList.Select(g => g + " -> " + R(g))));
                        Console.WriteLine($"     color: unknownUInt={e.Color.UnknownUInt} colors={e.Color.Colors.Count} alphas={e.Color.Alphas.Count}");
                        foreach (var kv in e.Color.Colors) Console.WriteLine($"        color t={kv.Key:0.###} -> {V(kv.Value)}");
                        foreach (var kv in e.Color.Alphas) Console.WriteLine($"        alpha t={kv.Key:0.###} -> {kv.Value:0.######}");

                        int ci = 0;
                        foreach (var (label, ep) in Curves(e))
                        {
                            ci++;
                            if (ep == null) { Console.WriteLine($"     {label}: <null>"); continue; }
                            var c = ep.Curve;
                            Console.WriteLine($"     {label}: u1={ep.UnknownUInt1} u2={ep.UnknownUInt2} f1={ep.UnknownFloat1:0.######} f2={ep.UnknownFloat2:0.######} " +
                                              $"| curve ver={c.Version} default={c.Default:0.######} mult={c.CurveMultiplier:0.######} keys={c.Keys.Count / 2}");
                            for (int k = 0; k + 1 < c.Keys.Count; k += 2)
                                Console.WriteLine($"          key[{k / 2}] {V(c.Keys[k])}  tangent {V(c.Keys[k + 1])}");
                        }
                    }
                }
            }
            Console.WriteLine($"== 命中 {n} 个 ==");
            return n > 0 ? 0 : 1;
        }

        /// <summary>反射把 Emitter 的每个字段倒成 "名字=值" —— 新增字段自动出现，不会漏。</summary>
        static string CsvFields(object o, string prefix = "")
        {
            var sb = new System.Text.StringBuilder();
            foreach (var p in o.GetType().GetProperties())
            {
                object v;
                try { v = p.GetValue(o); } catch { continue; }
                string n = prefix + p.Name;
                if (v == null) { sb.Append(n).Append("=null|"); continue; }
                var t = v.GetType();
                if (t == typeof(Vector4)) sb.Append(n).Append('=').Append(Fmt((Vector4)v)).Append('|');
                else if (t == typeof(Vector2)) sb.Append(n).Append("=(").Append(((Vector2)v).X.ToString("0.######")).Append(',').Append(((Vector2)v).Y.ToString("0.######")).Append(")|");
                else if (t == typeof(float)) sb.Append(n).Append('=').Append(((float)v).ToString("0.######")).Append('|');
                else if (t == typeof(string)) sb.Append(n).Append('=').Append(((string)v).Replace("|", "/")).Append('|');
                else if (t == typeof(Guid)) sb.Append(n).Append('=').Append(v).Append('|');
                else if (t.IsPrimitive) sb.Append(n).Append('=').Append(v).Append('|');
                else if (v is System.Collections.IEnumerable en && !(v is string))
                {
                    var items = new List<string>();
                    foreach (var x in en) items.Add(x is Guid g ? g.ToString().Substring(0, 8) : Convert.ToString(x));
                    sb.Append(n).Append("=[").Append(string.Join(",", items)).Append("]|");
                }
                else if (t == typeof(ParticleEffectData.EmitterParameter) || t == typeof(ParticleEffectData.Curve) || t == typeof(ParticleEffectData.ParticleColorParameter))
                    sb.Append(CsvFields(v, n + "."));
            }
            return sb.ToString();
        }

        static string Fmt(Vector4 v) => "(" + v.X.ToString("0.######") + "," + v.Y.ToString("0.######") + "," +
                                        v.Z.ToString("0.######") + "," + v.W.ToString("0.######") + ")";

        static IEnumerable<(string, ParticleEffectData.EmitterParameter)> Curves(ParticleEffectData.Emitter e)
        {
            yield return ("curve1", e.Curve1);
            yield return ("curve2", e.Curve2);
            yield return ("curve3", e.Curve3);
            yield return ("curve4", e.Curve4);
            yield return ("curve5", e.Curve5);
            yield return ("curve6", e.Curve6);
            yield return ("curve7(raw)", ToParam(e.Curve7));
            yield return ("curve8(raw)", ToParam(e.Curve8));
            yield return ("curve9", e.Curve9);
            yield return ("curve10", e.Curve10);
            yield return ("curve11", e.Curve11);
            yield return ("curve12", e.Curve12);
            yield return ("curve13", e.Curve13);
            yield return ("curve14", e.Curve14);
            yield return ("curve15", e.Curve15);
        }

        static ParticleEffectData.EmitterParameter ToParam(ParticleEffectData.Curve c)
        {
            var ep = new ParticleEffectData.EmitterParameter();
            ep.Curve = c;
            return ep;
        }
    }
}
