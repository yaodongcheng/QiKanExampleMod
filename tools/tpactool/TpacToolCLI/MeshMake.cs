using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// meshmake：把 RDR2 的 RMDL1 网格写成骑砍的 Metamesh 资产包（直写 tpac 的验证工具）。
    ///
    /// 用法：
    ///   tpaccli meshmake --packdir &lt;模板包目录&gt; --src &lt;模板 Metamesh 名&gt;
    ///                    --mdl &lt;RMDL1 文件&gt; --newname &lt;新网格名&gt;
    ///                    --out &lt;输出目录&gt; [--packname &lt;包名&gt;]
    ///
    /// 设计：模板 Metamesh 提供材质/顶点布局/各种引擎字段；本工具只替换几何
    /// （位置/法线/UV/索引/切线），并保留模板的 Material guid。
    /// 输出包只含新 Metamesh；材质与贴图仍在模板包里（两个包都放进 AssetPackages 即可）。
    /// </summary>
    public static class MeshMake
    {
        public static int Run(string[] args)
        {
            string packDir = null, srcName = null, mdlPath = null, newName = null;
            string outDir = ".", packName = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--packdir": packDir = args[++i]; break;
                    case "--src": srcName = args[++i]; break;
                    case "--mdl": mdlPath = args[++i]; break;
                    case "--newname": newName = args[++i]; break;
                    case "--out": outDir = args[++i]; break;
                    case "--packname": packName = args[++i]; break;
                    default: Console.Error.WriteLine("unknown arg: " + args[i]); break;
                }
            }
            if (packDir == null || srcName == null || mdlPath == null)
            {
                Console.Error.WriteLine("meshmake requires --packdir <dir> --src <template metamesh> --mdl <file>");
                return 1;
            }
            if (newName == null)
                newName = "rdr2_m_" + Path.GetFileNameWithoutExtension(mdlPath);
            if (packName == null)
                packName = newName + ".tpac";

            // 1) 加载模板包（惰性段数据，取 Metamesh）
            var byName = new Dictionary<string, AssetItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.EnumerateFiles(packDir, "*.tpac"))
            {
                var pkg = new AssetPackage(f, true, false);
                foreach (var it in pkg.Items)
                {
                    if (!byName.TryGetValue(it.Name, out var exist) || (exist is Material && it is Metamesh))
                        byName[it.Name] = it;
                }
            }
            if (!byName.TryGetValue(srcName, out var found) || !(found is Metamesh template))
            {
                Console.Error.WriteLine("template metamesh not found: " + srcName);
                return 1;
            }
            if (template.Meshes.Count == 0)
            {
                Console.Error.WriteLine("template metamesh has no meshes: " + srcName);
                return 1;
            }
            Console.WriteLine($"template: {template.Name}, meshes={template.Meshes.Count}, version={template.Version}");

            // 2) 读 RMDL1
            var mdl = Rmdl.Read(mdlPath);
            if (mdl.SubMeshes.Count == 0)
            {
                Console.Error.WriteLine("mdl has no submeshes: " + mdlPath);
                return 1;
            }
            Console.WriteLine($"mdl: {mdl.SubMeshes.Count} submeshes, bbox=({mdl.Min.X:F2},{mdl.Min.Y:F2},{mdl.Min.Z:F2})..({mdl.Max.X:F2},{mdl.Max.Y:F2},{mdl.Max.Z:F2})");

            // 3) 用模板的外壳建新 Metamesh
            var newMeta = new Metamesh
            {
                Name = newName,
                Guid = GuidFromName(newName),
                Material = template.Material,
                UnknownFloat = template.UnknownFloat,
                UnknownString = template.UnknownString,
                ClothMetamesh = template.ClothMetamesh,
                UnknownUint = template.UnknownUint,
                ClothUint = template.ClothUint,
                ClothString = template.ClothString,
                Original = template.Original,
                UnknownBool1 = template.UnknownBool1,
                UnknownBool2 = template.UnknownBool2,
            };
            newMeta.CloneVersion = template.Version;
            foreach (var v in template.Variations)
                newMeta.Variations.Add(v);

            var tmplMesh = template.Meshes[0];
            // 模板数据段是懒加载的；这里不需要原网格数据，q-tangent 按版本判断。
            bool hasQTangent = template.Version >= 1;

            for (int si = 0; si < mdl.SubMeshes.Count; si++)
            {
                var sub = mdl.SubMeshes[si];
                var mesh = new Mesh
                {
                    Name = mdl.SubMeshes.Count == 1 ? newName : newName + "_" + si,
                    Guid = GuidFromName(newName + ":" + si),
                    IsCompleteMesh = true,
                    Lod = 0,
                    UnknownUint1 = tmplMesh.UnknownUint1,
                    Material = tmplMesh.Material,
                    SecondMaterial = tmplMesh.SecondMaterial,
                    FactorColor = tmplMesh.FactorColor,
                    Factor2Color = tmplMesh.Factor2Color,
                    VectorArgument = tmplMesh.VectorArgument,
                    VectorArgument2 = tmplMesh.VectorArgument2,
                    VertexKeyCount = 0,
                    PositionCount = sub.Positions.Length,
                    FaceCount = sub.Indices.Length / 3,
                    VertexCount = sub.Positions.Length,
                    SkinDataSize = 0,
                    BoundingBox = new BoundingBox(mdl.Min, mdl.Max),
                    UnknownInt2 = tmplMesh.UnknownInt2,
                    UnknownFloat1 = tmplMesh.UnknownFloat1,
                    ClothingMaterial = tmplMesh.ClothingMaterial,
                    UnknownInt3 = tmplMesh.UnknownInt3,
                    UnknownBool1 = tmplMesh.UnknownBool1,
                    UnknownBool2 = tmplMesh.UnknownBool2,
                    UnknownBool3 = tmplMesh.UnknownBool3,
                };
                foreach (var f in tmplMesh.Flags) mesh.Flags.Add(f);
                foreach (var f in tmplMesh.MaterialFlags) mesh.MaterialFlags.Add(f);

                var vs = BuildVertexStream(sub, hasQTangent);
                var loader = new ExternalLoader<VertexStreamData>(vs) { OwnerGuid = mesh.Guid };
                if (hasQTangent)
                    loader.UserData[VertexStreamData.KEY_HAS_QTANGENT] = true;
                mesh.VertexStream = loader;

                newMeta.Meshes.Add(mesh);
                newMeta.TypelessDataSegments.Add(loader);
            }

            // 4) 保存
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, packName);
            var pkgOut = new AssetPackage();
            pkgOut.Items.Add(newMeta);
            pkgOut.Save(outPath);
            Console.WriteLine($"saved {outPath} ({new FileInfo(outPath).Length:N0} bytes, meshes={newMeta.Meshes.Count})");
            return 0;
        }

        private static VertexStreamData BuildVertexStream(Rmdl.SubMesh sub, bool hasQTangent)
        {
            int n = sub.Positions.Length;
            int ni = sub.Indices.Length;
            var colors = new AbstractMeshData.Color[n];
            var white = new AbstractMeshData.Color { R = 255, G = 255, B = 255, A = 255 };
            for (int i = 0; i < n; i++) colors[i] = white;

            var uv2 = new Vector2[n];
            Array.Copy(sub.Uvs, uv2, n);

            var tangents = ComputeTangents(sub.Positions, sub.Normals, sub.Uvs, sub.Indices);

            var vs = new VertexStreamData
            {
                Indices = sub.Indices,
                Colors1 = colors,
                Colors2 = colors,
                Uv1 = sub.Uvs,
                Uv2 = uv2,
                Positions = sub.Positions,
                UnknownAnotherPositions = new Vector3[0],
                Normals = sub.Normals,
                Tangents = tangents,
                BoneWeights = new VertexStreamData.BoneWeight[0],
                BoneIndices = new VertexStreamData.BoneIndex[0],
                CompressedNormals = new VertexStreamData.X11Y11Z10[0],
                CompressedPositions = new VertexStreamData.Half4[0],
                CompressedTangents = new VertexStreamData.X10Y11Z10W1[0],
                TangentTransform = hasQTangent ? BuildNeutralQT(n) : null,
            };
            return vs;
        }

        private static VertexStreamData.SnormShort4[] BuildNeutralQT(int n)
        {
            // 模板是 q-tangent 版本时补一组中性四元数（w=1），避免段长度对不上。
            var a = new VertexStreamData.SnormShort4[n];
            for (int i = 0; i < n; i++) a[i].RawW = short.MaxValue;
            return a;
        }

        private static Vector4[] ComputeTangents(Vector3[] p, Vector3[] nrm, Vector2[] uv, int[] idx)
        {
            var tan = new Vector3[p.Length];
            var bit = new Vector3[p.Length];
            for (int i = 0; i + 2 < idx.Length; i += 3)
            {
                int i0 = idx[i], i1 = idx[i + 1], i2 = idx[i + 2];
                Vector3 e1 = p[i1] - p[i0], e2 = p[i2] - p[i0];
                Vector2 d1 = uv[i1] - uv[i0], d2 = uv[i2] - uv[i0];
                float den = d1.X * d2.Y - d2.X * d1.Y;
                if (Math.Abs(den) < 1e-9f) continue;
                float f = 1f / den;
                Vector3 t = (e1 * d2.Y - e2 * d1.Y) * f;
                Vector3 b = (e2 * d1.X - e1 * d2.X) * f;
                tan[i0] += t; tan[i1] += t; tan[i2] += t;
                bit[i0] += b; bit[i1] += b; bit[i2] += b;
            }
            var outT = new Vector4[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                Vector3 t = tan[i];
                Vector3 nn = nrm[i];
                if (t.LengthSquared() < 1e-12f || nn.LengthSquared() < 1e-12f)
                {
                    outT[i] = new Vector4(1, 0, 0, 1);
                    continue;
                }
                t = Vector3.Normalize(t - nn * Vector3.Dot(nn, t));
                float w = Vector3.Dot(Vector3.Cross(nn, t), bit[i]) < 0 ? -1f : 1f;
                outT[i] = new Vector4(t, w);
            }
            return outT;
        }

        private static Guid GuidFromName(string name)
        {
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes("rdr2:" + name));
                return new Guid(hash);
            }
        }

        // ── RMDL1 读取器（格式见解包目录 README_骑砍2交接.md §5.3）──
        private static class Rmdl
        {
            public sealed class SubMesh
            {
                public int TexId;
                public Vector3[] Positions;
                public Vector3[] Normals;
                public Vector2[] Uvs;
                public int[] Indices;
            }

            public sealed class Model
            {
                public Vector3 Min, Max;
                public List<SubMesh> SubMeshes = new List<SubMesh>();
            }

            public static Model Read(string path)
            {
                byte[] data = File.ReadAllBytes(path);
                if (data.Length < 36 || Encoding.ASCII.GetString(data, 0, 5) != "RMDL1")
                    throw new InvalidDataException("not RMDL1: " + path);
                var model = new Model();
                using (var br = new BinaryReader(new MemoryStream(data)))
                {
                    br.BaseStream.Position = 8;
                    int nsub = br.ReadInt32();
                    float minx = br.ReadSingle(), miny = br.ReadSingle(), minz = br.ReadSingle();
                    float maxx = br.ReadSingle(), maxy = br.ReadSingle(), maxz = br.ReadSingle();
                    model.Min = new Vector3(minx, miny, minz);
                    model.Max = new Vector3(maxx, maxy, maxz);
                    var table = new int[nsub * 7];
                    for (int i = 0; i < nsub * 7; i++) table[i] = br.ReadInt32();

                    for (int s = 0; s < nsub; s++)
                    {
                        int o = s * 7;
                        var sub = new SubMesh
                        {
                            TexId = table[o],
                            Positions = ReadVec3(data, table[o + 3], table[o + 1]),
                            Normals = ReadVec3(data, table[o + 4], table[o + 1]),
                            Uvs = ReadVec2(data, table[o + 5], table[o + 1]),
                            Indices = ReadInts(data, table[o + 6], table[o + 2]),
                        };
                        model.SubMeshes.Add(sub);
                    }
                }
                return model;
            }

            private static Vector3[] ReadVec3(byte[] data, int off, int count)
            {
                var a = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    int p = off + i * 12;
                    a[i] = new Vector3(BitConverter.ToSingle(data, p), BitConverter.ToSingle(data, p + 4), BitConverter.ToSingle(data, p + 8));
                }
                return a;
            }

            private static Vector2[] ReadVec2(byte[] data, int off, int count)
            {
                var a = new Vector2[count];
                for (int i = 0; i < count; i++)
                {
                    int p = off + i * 8;
                    a[i] = new Vector2(BitConverter.ToSingle(data, p), BitConverter.ToSingle(data, p + 4));
                }
                return a;
            }

            private static int[] ReadInts(byte[] data, int off, int count)
            {
                var a = new int[count];
                for (int i = 0; i < count; i++)
                    a[i] = BitConverter.ToInt32(data, off + i * 4);
                return a;
            }
        }
    }
}
