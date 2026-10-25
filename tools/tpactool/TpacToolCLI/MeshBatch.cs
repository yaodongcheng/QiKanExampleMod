using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TpacTool.IO;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// meshbatch：一次产出 **N 个网格 + N 个材质 + N 套贴图**（一个 tpac 包）。
    ///
    /// 为什么需要它：`meshmake` 一次只能出一个网格、且借用模板材质（贴图换不了）。
    /// 圣丹尼斯要摆几千栋楼、每栋用自己的 albedo / 法线 / ORM，必须批量。
    ///
    /// 用法：
    ///   tpaccli meshbatch --manifest &lt;models.json&gt; --packdir &lt;模板包目录&gt;
    ///                     --src &lt;模板 Metamesh 名&gt; --out &lt;输出目录&gt; --packname &lt;包名.tpac&gt;
    ///
    /// manifest：
    ///   { "models": [ { "mdl": "...", "name": "rdr2_sdn_0001",
    ///                   "albedo": "...png", "normal": "...png", "spec": "...png" } ] }
    ///   （贴图一律 PNG —— jpg 由 Python 侧先转好；引擎侧不做 jpg 解码）
    ///
    /// 材质：克隆模板材质（保留 shader / flags / 其它槽），只替换 diffuse / normal / specular 三张图。
    /// </summary>
    public static class MeshBatch
    {
        public static int Run(string[] args)
        {
            string manifestPath = null, packDir = null, srcName = null, outDir = ".", packName = null, matName = null, matDefPath = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--manifest": manifestPath = args[++i]; break;
                    case "--packdir": packDir = args[++i]; break;
                    case "--src": srcName = args[++i]; break;
                    case "--out": outDir = args[++i]; break;
                    case "--packname": packName = args[++i]; break;
                    case "--mat": matName = args[++i]; break;
                    case "--matdef": matDefPath = args[++i]; break;
                }
            }
            if (manifestPath == null || packDir == null || srcName == null)
            {
                Console.Error.WriteLine("meshbatch requires --manifest <json> --packdir <dir> --src <template metamesh>");
                return 1;
            }
            var doc = JsonSerializer.Deserialize<BatchManifest>(File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (doc == null || doc.Models.Count == 0)
            {
                Console.Error.WriteLine("manifest has no models");
                return 1;
            }
            if (packName == null) packName = "rdr2_meshbatch.tpac";

            // 1) 扫模板包（惰性，只取元数据）
            var byName = new Dictionary<string, AssetItem>(StringComparer.OrdinalIgnoreCase);
            var byGuid = new Dictionary<Guid, AssetItem>();
            foreach (var dir in packDir.Split(';'))
            {
            if (!Directory.Exists(dir.Trim())) { Console.WriteLine("  (skip missing dir) " + dir.Trim()); continue; }
            foreach (var f in Directory.EnumerateFiles(dir.Trim(), "*.tpac"))
            {
                var pkg = new AssetPackage(f, true, false);
                foreach (var it in pkg.Items)
                {
                    if (!byName.TryGetValue(it.Name, out var exist) || (exist is Material && it is Metamesh))
                        byName[it.Name] = it;
                    byGuid[it.Guid] = it;
                }
            }
            }
            if (!byName.TryGetValue(srcName, out var found) || !(found is Metamesh template) || template.Meshes.Count == 0)
            {
                Console.Error.WriteLine("template metamesh not found or empty: " + srcName);
                return 1;
            }
            Material tmat = null;
            if (matName != null)
            {
                if (byName.TryGetValue(matName, out var mi) && mi is Material mm) { tmat = mm; Console.WriteLine("material by name: " + matName); }
                else { Console.Error.WriteLine("--mat not found: " + matName); return 1; }
            }
            else if (byGuid.TryGetValue(template.Material, out var tmatItem) && tmatItem is Material tm) tmat = tm;
            else
            {
                Console.Error.WriteLine("template material not found: " + template.Material + " (use --mat <name>)");
                return 1;
            }
            Console.WriteLine($"template: {template.Name} | material: {tmat.Name}");
            var slotNames = new Dictionary<int, string>();
            foreach (var kv in tmat.Textures)
                slotNames[kv.Key] = (byGuid.TryGetValue(kv.Value.Guid, out var ti) && ti is Texture) ? ti.Name : "?";
            Console.WriteLine("  material slots: " + string.Join(", ", slotNames.Select(kv => kv.Key + "=" + kv.Value)));
            int kDiffuse = PickSlot(slotNames, "_d", "_ab", "_albedo");
            int kNormal = PickSlot(slotNames, "_n", "_nm", "normal");
            int kSpec = PickSlot(slotNames, "_s", "spec");
            Console.WriteLine($"  -> diffuse={kDiffuse} normal={kNormal} specular={kSpec}");

            MatDef def = null;
            if (matDefPath != null)
            {
                def = MatDef.Parse(matDefPath);
                Console.WriteLine($"matdef: {def.Source} shader={def.Shader} blend={def.Blend} shaderFlags=[{string.Join(",", def.ShaderFlags)}] vl=[{string.Join(",", def.VertexLayoutFlags)}]");
            }

            var pkgOut = new AssetPackage();
            var tmplMesh = template.Meshes[0];
            bool hasQTangent = template.Version >= 1;
            int done = 0;

            foreach (var model in doc.Models)
            {
                if (string.IsNullOrWhiteSpace(model.Mdl) || string.IsNullOrWhiteSpace(model.Name))
                {
                    Console.Error.WriteLine("  skip: model without mdl/name");
                    continue;
                }
                if (!File.Exists(model.Mdl))
                {
                    Console.Error.WriteLine("  skip (mdl missing): " + model.Mdl);
                    continue;
                }
                var mdl = MeshMake.Rmdl.Read(model.Mdl);
                if (mdl.SubMeshes.Count == 0) { Console.Error.WriteLine("  skip (no submesh): " + model.Mdl); continue; }

                // 贴图
                var texD = AddTexture(pkgOut, model.Name + "_d", model.Albedo, keepAlpha: true);
                var texN = AddTexture(pkgOut, model.Name + "_n", model.Normal, keepAlpha: false);
                var texS = AddTexture(pkgOut, model.Name + "_s", model.Spec, keepAlpha: false);

                // 材质（克隆模板，只换三张图）
                // 始终以模板材质为壳（保证所有字段有值），有 --matdef 时再按定义覆盖 shader/flags
                var m = new Material
                {
                    Name = model.Name + "_mat",
                    Guid = MeshMake.GuidFromName("mat:" + model.Name),
                    BillboardGuid = tmat.BillboardGuid,
                    Version = tmat.Version,
                    SubVersion = tmat.SubVersion,
                    UnknownUint1 = tmat.UnknownUint1,
                    UnknownUint2 = tmat.UnknownUint2,
                    BlendMode = tmat.BlendMode,
                    AlphaTest = tmat.AlphaTest,
                    Shader = new AssetDependence<Shader>(tmat.Shader.Guid),
                };
                if (def != null)
                {
                    m.Shader = new AssetDependence<Shader>(def.Shader);
                    m.BlendMode = def.Blend;
                    m.AlphaTest = def.AlphaTest;
                    m.Flags.Clear(); m.VertexLayoutFlags.Clear(); m.ShaderMaterialFlags.Clear();
                    foreach (var f in def.Flags) m.Flags.Add(f);
                    foreach (var f in def.VertexLayoutFlags) m.VertexLayoutFlags.Add(f);
                    foreach (var f in def.ShaderFlags) m.ShaderMaterialFlags.Add(f);
                    m.ExtraMaterialSettings = def.MakeExtra();
                }
                else
                {
                    foreach (var f in tmat.Flags) m.Flags.Add(f);
                    foreach (var f in tmat.VertexLayoutFlags) m.VertexLayoutFlags.Add(f);
                    foreach (var f in tmat.ShaderMaterialFlags) m.ShaderMaterialFlags.Add(f);
                    m.ExtraMaterialSettings = tmat.ExtraMaterialSettings;
                }
                foreach (var kv in tmat.Textures)
                    m.Textures[kv.Key] = new AssetDependence<Texture>(kv.Value.Guid);
                if (def != null) { kDiffuse = 0; kNormal = 2; kSpec = 4; }
                if (kDiffuse >= 0 && texD != null) m.Textures[kDiffuse] = new AssetDependence<Texture>(texD.Guid);
                if (kNormal >= 0 && texN != null) m.Textures[kNormal] = new AssetDependence<Texture>(texN.Guid);
                if (kSpec >= 0 && texS != null) m.Textures[kSpec] = new AssetDependence<Texture>(texS.Guid);
                pkgOut.Items.Add(m);

                // 网格（外壳抄模板，材质指向新材质）
                var newMeta = new Metamesh
                {
                    Name = model.Name,
                    Guid = MeshMake.GuidFromName(model.Name),
                    Material = m.Guid,
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
                foreach (var v in template.Variations) newMeta.Variations.Add(v);

                for (int si = 0; si < mdl.SubMeshes.Count; si++)
                {
                    var sub = mdl.SubMeshes[si];
                    string meshName = mdl.SubMeshes.Count == 1 ? model.Name : model.Name + "_" + si;
                    var mesh = new Mesh
                    {
                        Name = meshName,
                        Guid = MeshMake.GuidFromName(model.Name + ":0:" + si),
                        IsCompleteMesh = true,
                        Lod = 0,
                        UnknownUint1 = tmplMesh.UnknownUint1,
                        Material = new AssetDependence<Material>(m.Guid),
                        SecondMaterial = new AssetDependence<Material>(m.Guid),
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
                    var vs = MeshMake.BuildVertexStream(sub, hasQTangent);
                    var loader = new ExternalLoader<VertexStreamData>(vs) { OwnerGuid = mesh.Guid };
                    if (hasQTangent) loader.UserData[VertexStreamData.KEY_HAS_QTANGENT] = true;
                    mesh.VertexStream = loader;
                    newMeta.Meshes.Add(mesh);
                    newMeta.TypelessDataSegments.Add(loader);
                }
                pkgOut.Items.Add(newMeta);

                done++;
                if (done % 25 == 0) Console.WriteLine($"  {done}/{doc.Models.Count}  ({model.Name})");
            }

            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, packName);
            pkgOut.Save(outPath);
            Console.WriteLine($"saved {outPath} ({new FileInfo(outPath).Length:N0} bytes, models={done})");
            return 0;
        }

        private static int PickSlot(Dictionary<int, string> slots, params string[] wanted)
        {
            foreach (var w in wanted)
                foreach (var kv in slots)
                    if (kv.Value != null && kv.Value.EndsWith(w, StringComparison.OrdinalIgnoreCase)) return kv.Key;
            foreach (var w in wanted)
                foreach (var kv in slots)
                    if (kv.Value != null && kv.Value.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return kv.Key;
            return -1;
        }

        private static Texture AddTexture(AssetPackage pkg, string name, string path, bool keepAlpha)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            var png = Png.Open(path);
            byte[] rgba = new byte[png.Width * png.Height * 4];
            int pos = 0;
            for (int y = 0; y < png.Height; y++)
                for (int x = 0; x < png.Width; x++)
                {
                    var px = png.GetPixel(x, y);
                    rgba[pos++] = px.R; rgba[pos++] = px.G; rgba[pos++] = px.B; rgba[pos++] = px.A;
                }
            byte[] encoded = keepAlpha ? Bc3Encoder.Encode(rgba, png.Width, png.Height)
                                       : Bc3Encoder.EncodeBc1(rgba, png.Width, png.Height);
            var asset = new Texture
            {
                Name = name,
                Source = "",
                Flags = new List<string> { "dont_degrade", "dont_delay_loading" },
                Width = (uint)png.Width,
                Height = (uint)png.Height,
                MipmapCount = 1,
                ArrayCount = 1,
                Format = keepAlpha ? TextureFormat.DXT5 : TextureFormat.DXT1,
                SystemFlags = keepAlpha ? new List<string> { "has_alpha" } : new List<string>(),
                GeneratedAssets = new List<Tuple<Guid, Guid>>(),
            };
            asset.Guid = MeshMake.GuidFromName("tex:" + name);
            var loader = new ExternalLoader<TexturePixelData>(new TexturePixelData
            {
                PrimaryRawImage = encoded,
                RawImage = new[] { new[] { encoded } },
            }) { OwnerGuid = asset.Guid };
            loader.UserData[TexturePixelData.KEY_WIDTH] = png.Width;
            loader.UserData[TexturePixelData.KEY_HEIGHT] = png.Height;
            loader.UserData[TexturePixelData.KEY_ARRAY] = 1;
            loader.UserData[TexturePixelData.KEY_MIPMAP] = 1;
            loader.UserData[TexturePixelData.KEY_FORMAT] = asset.Format;
            asset.TypelessDataSegments.Add(loader);
            pkg.Items.Add(asset);
            return asset;
        }

        private sealed class MatDef
        {
            public string Source;
            public Guid Shader;
            public string Blend = "no_alpha_blend";
            public float AlphaTest;
            public List<string> Flags = new List<string>();
            public List<string> VertexLayoutFlags = new List<string>();
            public List<string> ShaderFlags = new List<string>();
            public float Ao = 1f, Spec = 1f, Gloss = 1f;

            static List<string> ListVal(string line)
            {
                int a = line.IndexOf('['), b = line.IndexOf(']');
                if (a < 0 || b <= a) return new List<string>();
                return line.Substring(a + 1, b - a - 1)
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            }

            public static MatDef Parse(string path)
            {
                var d = new MatDef { Source = Path.GetFileName(path) };
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.TrimStart('\uFEFF', ' ', '\t');
                    if (line.StartsWith("shader =")) d.Shader = Guid.Parse(line.Substring(8).Trim());
                    else if (line.StartsWith("flags =")) d.Flags = ListVal(line);
                    else if (line.StartsWith("vertexLayoutFlags =")) d.VertexLayoutFlags = ListVal(line);
                    else if (line.StartsWith("shaderFlags =")) d.ShaderFlags = ListVal(line);
                    else if (line.StartsWith("blend =")) d.Blend = line.Substring(7).Trim();
                    else if (line.StartsWith("alphaTest =")) float.TryParse(line.Substring(11).Trim(), out d.AlphaTest);
                    else if (line.StartsWith("extra:"))
                    {
                        foreach (var part in line.Substring(6).Split(' '))
                        {
                            var kv = part.Split('=');
                            if (kv.Length != 2) continue;
                            if (!float.TryParse(kv[1], out var v)) continue;
                            if (kv[0] == "ao") d.Ao = v; else if (kv[0] == "spec") d.Spec = v; else if (kv[0] == "gloss") d.Gloss = v;
                        }
                    }
                }
                if (d.Shader == Guid.Empty) throw new InvalidDataException("matdef: no shader in " + path);
                return d;
            }

            public Material.ExtraMaterialSetting MakeExtra()
            {
                var e = new Material.ExtraMaterialSetting();
                e.AmbientOcclusionCoef = Ao;
                e.SpecularCoef = Spec;
                e.GlossCoef = Gloss;
                return e;
            }
        }

        public class BatchManifest
        {
            [JsonPropertyName("models")] public List<BatchModel> Models { get; set; } = new List<BatchModel>();
        }

        public class BatchModel
        {
            [JsonPropertyName("mdl")] public string Mdl { get; set; }
            [JsonPropertyName("name")] public string Name { get; set; }
            [JsonPropertyName("albedo")] public string Albedo { get; set; }
            [JsonPropertyName("normal")] public string Normal { get; set; }
            [JsonPropertyName("spec")] public string Spec { get; set; }
        }
    }
}
