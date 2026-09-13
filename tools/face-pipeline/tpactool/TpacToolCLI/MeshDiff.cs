using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TpacTool.IO;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// meshdiff —— 把 Metamesh 及其子网格 / 顶点流 / 编辑数据 / 材质的**全部公开字段**反射打平成
    /// key = value 文本，用于和「能跑通的参照物」（xxFemaleHead / Native head_female_a）做逐行差分。
    ///
    /// 动机：头部换脸的崩溃是「声明值 vs 实际数据」对不上，逐个字段猜一轮要一次实机启动，成本太高。
    /// 本命令离线把差异一次性列全。
    ///
    /// 用法: tpaccli meshdiff --packdir &lt;dir&gt; --filter &lt;mesh名子串&gt; [--out &lt;txt&gt;]
    /// </summary>
    public static class MeshDiff
    {
        public static int Run(string dir, string filter, string outFile)
        {
            var pkg = MorphFix.LoadPackages(dir, filter, out var metas);
            if (pkg == null) return 1;

            var matByGuid = new Dictionary<Guid, Material>();
            foreach (var m in pkg.Items.OfType<Material>()) matByGuid[m.Guid] = m;
            var byGuid = pkg.Items.ToDictionary(i => i.Guid, i => i);

            var sb = new System.Text.StringBuilder();
            void W(string s) { sb.AppendLine(s); }

            foreach (var meta in metas)
            {
                W($"########## Metamesh {meta.Name} ##########");
                Dump(meta, "meta", 0, W, skip: new[] { "Meshes" });

                foreach (var mesh in meta.Meshes)
                {
                    W($"---- Mesh {mesh.Name} ----");
                    Dump(mesh, "mesh", 0, W, skip: new[] { "EditData", "VertexStream" });

                    var ed = mesh.EditData?.Data;
                    if (ed != null) { W($"  [EditData]"); Dump(ed, "  ed", 0, W, skip: new[] { "Vertices", "Positions", "Faces", "MorphFrames" }); }
                    else W($"  [EditData] null");

                    var vs = mesh.VertexStream?.Data;
                    if (vs != null)
                    {
                        W($"  [VertexStream]");
                        Dump(vs, "  vs", 0, W, skip: new[] { "Positions", "Normals", "Uv1", "Uv2", "Indices", "Colors1", "Colors2", "BoneWeights", "BoneIndices", "Tangents" });
                        foreach (var kv in mesh.VertexStream.UserData)
                            W($"  vs.userdata[{kv.Key}] = {kv.Value}");
                    }
                    else W($"  [VertexStream] null");

                    foreach (var g in new[] { mesh.Material?.Guid ?? Guid.Empty, mesh.SecondMaterial?.Guid ?? Guid.Empty })
                    {
                        if (g == Guid.Empty) continue;
                        if (matByGuid.TryGetValue(g, out var mat))
                        {
                            W($"---- Material {mat.Name} ----");
                            Dump(mat, "mat", 0, W, skip: new[] { "Textures" });
                            foreach (var kv in mat.Textures)
                                W($"  mat.tex[{kv.Key}] = {kv.Value.Guid} ({(byGuid.TryGetValue(kv.Value.Guid, out var ti) ? ti.Name : "?")})");
                        }
                        else W($"---- Material guid {g} 不在本包内 ----");
                    }
                }
            }

            string text = sb.ToString();
            if (outFile != null) { File.WriteAllText(outFile, text, System.Text.Encoding.UTF8); Console.WriteLine($"written {outFile} ({text.Length} chars)"); }
            else Console.Write(text);
            return 0;
        }

        static void Dump(object o, string path, int depth, Action<string> w, string[] skip = null)
        {
            if (depth > 4) return;
            if (o == null) { w($"{path} = null"); return; }
            var t = o.GetType();
            if (o is string || o is Guid) { w($"{path} = {o}"); return; }
            if (t.IsEnum) { w($"{path} = {o}"); return; }
            if (t.IsPrimitive || t.IsValueType)
            {
                // 数值/结构体一律用 ToString（Vector4/Color/BoneIndex 等都有可读实现）
                if (!(o is IEnumerable)) { w($"{path} = {o}"); return; }
            }
            if (o is IEnumerable en)
            {
                int n = 0; var samples = new List<string>();
                foreach (var it in en) { if (n < 3) samples.Add(it?.ToString() ?? "null"); n++; }
                w($"{path} = [{t.Name} len={n}] {string.Join(" | ", samples)}");
                return;
            }
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                if (skip != null && skip.Contains(p.Name)) continue;
                object v;
                try { v = p.GetValue(o); } catch { continue; }
                Dump(v, $"{path}.{p.Name}", depth + 1, w, skip);
            }
        }
    }
}
