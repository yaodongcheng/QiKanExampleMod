using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TpacTool.IO;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// assetclone: 把一个资产对象(可含 MetaMesh/材质/贴图依赖图)整体拷贝改名,
    /// 生成 lwn_* 自包含资产包 —— 模仿 xxFemaleHead 的「自建资产」路线,产出:
    ///   lwn_head_male_a(新头网格) + 配套材质 + 贴图(参考值=源内容,后续由 gen 管线重画)
    /// 用法: tpaccli assetclone --packdir <d1[,d2]> --src <name> --newname <lwn_name>
    ///                          [--extra <name> ...] --out <dir>
    /// </summary>
    public static class AssetClone
    {
        public static int Run(string[] args)
        {
            string srcName = null, newName = null, outDir = ".", packName = "lwn_face.tpac";
            var packDirs = new List<string>();
            var extras = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--packdir": packDirs.AddRange(args[++i].Split(',')); break;
                    case "--src": srcName = args[++i]; break;
                    case "--newname": newName = args[++i]; break;
                    case "--extra": extras.Add(args[++i]); break;
                    case "--out": outDir = args[++i]; break;
                    case "--packname": packName = args[++i]; break;
                    default: Console.Error.WriteLine("unknown arg: " + args[i]); break;
                }
            }
            if (string.IsNullOrEmpty(srcName) || packDirs.Count == 0)
            {
                Console.Error.WriteLine("assetclone requires --src <name> and --packdir <dir>");
                return 1;
            }
            if (string.IsNullOrEmpty(newName))
                newName = "lwn_" + CleanName(srcName);

            // 1) 全量加载: guid -> item 索引 (跨包查依赖)
            var byGuid = new Dictionary<Guid, AssetItem>();
            var byName = new Dictionary<string, AssetItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in packDirs)
            {
                if (!Directory.Exists(dir)) { Console.Error.WriteLine("missing dir: " + dir); return 1; }
                foreach (var f in Directory.EnumerateFiles(dir, "*.tpac"))
                {
                    // loadDataNow=false: 段数据懒加载——全量强制加载会踩 animation 段解析坑("Frames not equal")
                    var pkg = new AssetPackage(f, true, false);
                    foreach (var it in pkg.Items)
                    {
                        byGuid[it.Guid] = it;
                        // 同名歧义(如 head_female_a 既是材质也是网格): Metamesh 优先(源码常指网格)
                        if (!byName.TryGetValue(it.Name, out var exist) || (exist is Material && it is Metamesh))
                            byName[it.Name] = it;
                    }
                }
            }
            Console.WriteLine($"loaded {byGuid.Count} assets");
            SetFinder(g => byGuid.TryGetValue(g, out var it) ? it : null);

            var src = byName.TryGetValue(srcName, out var s) ? s : null;
            if (src == null) { Console.Error.WriteLine("src not found: " + srcName); return 1; }

            // 2) 收集依赖图: metamesh -> material -> texture (+ extra 独立资产)
            var toClone = new List<AssetItem> { src };
            var seen = new HashSet<AssetItem>();
            var queue = new Queue<AssetItem>(toClone);
            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                if (!seen.Add(item)) continue;
                foreach (var dep in Dependencies(item))
                {
                    if (dep != null && (dep is Material or Texture || dep is Metamesh))
                    {
                        toClone.Add(dep);
                        queue.Enqueue(dep);
                    }
                }
            }
            foreach (var ex in extras)
            {
                if (byName.TryGetValue(ex, out var ei)) { toClone.Add(ei); queue.Enqueue(ei); }
                else Console.Error.WriteLine("warning: extra not found: " + ex);
                while (queue.Count > 0)
                {
                    var item = queue.Dequeue();
                    if (!seen.Add(item)) continue;
                    foreach (var dep in Dependencies(item))
                        if (dep is Material or Texture or Metamesh) { toClone.Add(dep); queue.Enqueue(dep); }
                }
            }

            // 3) 克隆: 新名 + 确定性 GUID + 依赖重指
            var cloneOf = new Dictionary<AssetItem, AssetItem>();
            var nameMap = new Dictionary<string, string>();
            foreach (var item in toClone)
            {
                var nn = item == src ? newName : "lwn_" + CleanName(item.Name);
                if (nameMap.TryGetValue(nn, out _)) nn = nn + "_" + (item.GetType().Name);
                nameMap[nn] = nn;
                cloneOf[item] = CloneItem(item, nn, byGuid, (dep) => ResolveClone(dep, cloneOf, toClone));
            }
            // 3b) raw 元数据 GUID 修补: 依赖引用(材质/贴图)换成克隆产物 guid; 其余(shader/子mesh/未克隆依赖)原样
            var oldNew = cloneOf.ToDictionary(kv => kv.Key.Guid, kv => kv.Value.Guid);
            foreach (var kv in cloneOf)
            {
                if (kv.Value.RawMeta == null) continue;
                kv.Value.RawMeta = PatchGuids((byte[])kv.Value.RawMeta.Clone(), oldNew);
            }

            // 5) 写包
            Directory.CreateDirectory(outDir);
            var outPath = Path.Combine(outDir, packName);
            var outPkg = new AssetPackage();
            foreach (var c in cloneOf.Values) outPkg.Items.Add(c);
            // 5b) 存盘前清理: 解析为「裸 ExternalData/主数据为 null」的段一律剔除,防 Save 空指针
            foreach (var it in outPkg.Items)
            {
                var keep = new List<AbstractExternalLoader>();
                foreach (var seg in it.TypelessDataSegments)
                {
                    if (ProbeSegmentOk(seg)) keep.Add(seg);
                    else Console.Error.WriteLine($"  cleanup: drop empty segment {it.Name} owner {seg.OwnerGuid}");
                }
                it.TypelessDataSegments.Clear();
                foreach (var seg in keep) it.TypelessDataSegments.Add(seg);
            }
            // 5c) 无像素贴图兜底: 私有 gts/BC7 源导不出的贴图补 1×1 中性像素,防引擎加载空纹理
            foreach (var it in outPkg.Items)
            {
                if (it is Texture tt && tt.TypelessDataSegments.Count == 0)
                    PadTexture(tt);
            }
            outPkg.Save(outPath);
            Console.WriteLine($"saved {outPath} ({new FileInfo(outPath).Length:N0} bytes, {outPkg.Items.Count} items)");
            foreach (var c in cloneOf.Values.OrderBy(c => c.Name))
                Console.WriteLine($"  {c.Name}\t{c.GetType().Name}");
            return 0;
        }

        static AssetItem ResolveClone(AssetItem dep, Dictionary<AssetItem, AssetItem> cloneOf, List<AssetItem> toClone)
        {
            return cloneOf.TryGetValue(dep, out var c) ? c : dep; // 不在克隆集(如 Shader)→ 原样引用
        }

        static IEnumerable<AssetItem> Dependencies(AssetItem item)
        {
            switch (item)
            {
                case Metamesh meta:
                    if (!meta.Material.Equals(Guid.Empty)) yield return Find(meta.Material);
                    foreach (var m in meta.Meshes)
                    {
                        if (m.Material != null && !m.Material.IsEmpty()) yield return Find(m.Material.Guid);
                        if (m.SecondMaterial != null && !m.SecondMaterial.IsEmpty()) yield return Find(m.SecondMaterial.Guid);
                    }
                    break;
                case Material mat:
                    foreach (var kv in mat.Textures) yield return Find(kv.Value.Guid);
                    break;
            }
            yield break;
        }

        // 依赖解析辅助经闭包注入(避免静态轮子纠缠): 由回调处理
        static Func<Guid, AssetItem> _finder;

        static AssetItem Find(Guid guid) => _finder(guid);

        public static void SetFinder(Func<Guid, AssetItem> finder) => _finder = finder;

        static AssetItem CloneItem(AssetItem item, string newName, Dictionary<Guid, AssetItem> byGuid,
            Func<AssetItem, AssetItem> resolveDep)
        {
            var guid = DeterministicGuid("tpac:" + newName);
            AssetItem clone;
            switch (item)
            {
                case Metamesh meta:
                {
                    var m = new Metamesh { Name = newName, Guid = guid };
                    m.CloneVersion = meta.Version;
                    m.Material = ResolveGuid(meta.Material, resolveDep);
                    m.UnknownFloat = meta.UnknownFloat;
                    m.UnknownString = meta.UnknownString;
                    m.ClothMetamesh = meta.ClothMetamesh;
                    m.UnknownUint = meta.UnknownUint;
                    m.ClothUint = meta.ClothUint;
                    m.ClothString = meta.ClothString;
                    m.Original = meta.Original;
                    m.UnknownBool1 = meta.UnknownBool1;
                    m.UnknownBool2 = meta.UnknownBool2;
                    foreach (var v in meta.Variations) m.Variations.Add(v);
                    foreach (var mesh in meta.Meshes)
                    {
                        var nm = new Mesh
                        {
                            Name = mesh.Name,
                            Guid = mesh.Guid, // 子网格 guid 仅段归属,新建条目内自洽
                            UnknownUInt2 = mesh.UnknownUInt2,
                            Lod = mesh.Lod,
                            IsCompleteMesh = mesh.IsCompleteMesh,
                            UnknownUint1 = mesh.UnknownUint1,
                            FactorColor = mesh.FactorColor,
                            Factor2Color = mesh.Factor2Color,
                            VectorArgument = mesh.VectorArgument,
                            VectorArgument2 = mesh.VectorArgument2,
                            BoundingBox = mesh.BoundingBox,
                            UnknownInt2 = mesh.UnknownInt2,
                            UnknownFloat1 = mesh.UnknownFloat1,
                            UnknownInt3 = mesh.UnknownInt3,
                            VertexKeyCount = mesh.VertexKeyCount,
                            PositionCount = mesh.PositionCount,
                            FaceCount = mesh.FaceCount,
                            VertexCount = mesh.VertexCount,
                            SkinDataSize = mesh.SkinDataSize,
                        };
                        foreach (var f in mesh.Flags) nm.Flags.Add(f);
                        foreach (var f in mesh.MaterialFlags) nm.MaterialFlags.Add(f);
                        // 深拷贝 Material/SecondMaterial 依赖(避免共享实例在克隆集上互相改)
                        nm.Material = new AssetDependence<Material>(ResolveGuid(mesh.Material.Guid, resolveDep));
                        nm.SecondMaterial = new AssetDependence<Material>(ResolveGuid(mesh.SecondMaterial.Guid, resolveDep));
                        // ClothingMaterial: 简单标量拷贝(头网格通常为空)
                        try { nm.ClothingMaterial = CopyCloth(mesh.ClothingMaterial); } catch { }
                        m.Meshes.Add(nm);
                    }
                    foreach (var seg in meta.TypelessDataSegments)
                    {
                        var segC = CloneSegment(seg);
                        if (segC != null) m.TypelessDataSegments.Add(segC);
                        else Console.Error.WriteLine($"  warn: skip segment {seg.OwnerGuid} {seg.GetType().Name}");
                    }
                    clone = m;
                    break;
                }
                case Material mat:
                {
                    var m = new Material { Name = newName, Guid = guid };
                    m.BillboardGuid = mat.BillboardGuid;
                    m.Version = mat.Version;
                    m.SubVersion = mat.SubVersion;
                    m.UnknownUint1 = mat.UnknownUint1;
                    m.UnknownUint2 = mat.UnknownUint2;
                    m.BlendMode = mat.BlendMode;
                    m.AlphaTest = mat.AlphaTest;
                    foreach (var f in mat.Flags) m.Flags.Add(f);
                    foreach (var f in mat.VertexLayoutFlags) m.VertexLayoutFlags.Add(f);
                    foreach (var f in mat.ShaderMaterialFlags) m.ShaderMaterialFlags.Add(f);
                    m.Shader = new AssetDependence<Shader>(mat.Shader.Guid); // Shader 引擎内置,不改
                    foreach (var kv in mat.Textures)
                        m.Textures[kv.Key] = new AssetDependence<Texture>(ResolveGuid(kv.Value.Guid, resolveDep));
                    m.ExtraMaterialSettings = CopyExtra(mat.ExtraMaterialSettings);
                    clone = m;
                    break;
                }
                case Texture tex:
                {
                    var t = new Texture { Name = newName, Guid = guid };
                    t.BillboardMaterial = tex.BillboardMaterial == null ? null :
                        new AssetDependence<Material>(ResolveGuid(tex.BillboardMaterial.Guid, resolveDep));
                    t.UnknownUint1 = tex.UnknownUint1;
                    t.Source = ""; // 引擎防外链校验: 打包产物一律空
                    t.UnknownUlong = tex.UnknownUlong;
                    t.UnknownBool = tex.UnknownBool;
                    t.UnknownUint2 = tex.UnknownUint2;
                    t.UnknownUint3 = tex.UnknownUint3;
                    t.UnknownByte = tex.UnknownByte;
                    t.Width = tex.Width;
                    t.Height = tex.Height;
                    t.UnknownUint4 = tex.UnknownUint4;
                    t.MipmapCount = tex.MipmapCount; // 优先原始段直拷 → 保留原始 mip 链
                    t.ArrayCount = tex.ArrayCount;
                    t.Format = tex.Format;
                    t.UnknownUint5 = tex.UnknownUint5;
                    t.UnknownUint6 = tex.UnknownUint6;
                    t.UnknownUint7 = tex.UnknownUint7;
                    t.UnknownUlong2 = tex.UnknownUlong2;
                    t.Flags = new List<string>(tex.Flags);
                    t.SystemFlags = new List<string>(tex.SystemFlags);
                    t.GeneratedAssets = new List<Tuple<Guid, Guid>>(tex.GeneratedAssets);
                    // 像素: ① 原始段直拷(保字节+mip,织丰原版零损失) → ② IO导出重编码 → ③ 末尾兜底1x1
                    foreach (var seg in tex.TypelessDataSegments)
                    {
                        var segC = CloneSegment(seg);
                        if (segC != null) t.TypelessDataSegments.Add(segC);
                    }
                    if (t.TypelessDataSegments.Count == 0)
                    {
                        // 源私有 gts / BC7 导不出的贴图: PNG 重编码(1 mip),失败则末尾 1×1 兜底
                        var pngTmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
                        try
                        {
                            TpacTool.IO.TextureExporter.ExportToFile(pngTmp, tex);
                            var png = Png.Open(File.ReadAllBytes(pngTmp));
                            byte[] rgba = new byte[png.Width * png.Height * 4];
                            int pos = 0;
                            for (int y = 0; y < png.Height; y++)
                                for (int x = 0; x < png.Width; x++)
                                {
                                    var px = png.GetPixel(x, y);
                                    rgba[pos++] = px.R; rgba[pos++] = px.G; rgba[pos++] = px.B; rgba[pos++] = px.A;
                                }
                            var bc3 = Bc3Encoder.Encode(rgba, png.Width, png.Height);
                            var loader = new ExternalLoader<TexturePixelData>(
                                new TexturePixelData
                                {
                                    PrimaryRawImage = bc3,
                                    RawImage = new[] { new[] { bc3 } },
                                })
                            { OwnerGuid = guid };
                            loader.UserData[TexturePixelData.KEY_WIDTH] = (int)png.Width;
                            loader.UserData[TexturePixelData.KEY_HEIGHT] = (int)png.Height;
                            loader.UserData[TexturePixelData.KEY_ARRAY] = 1;
                            loader.UserData[TexturePixelData.KEY_MIPMAP] = 1;
                            loader.UserData[TexturePixelData.KEY_FORMAT] = TextureFormat.DXT5;
                            t.TypelessDataSegments.Add(loader);
                            t.MipmapCount = 1;
                            t.Format = TextureFormat.DXT5;
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"  warn: re-encode failed {tex.Name}: {ex.Message}");
                        }
                        finally
                        {
                            try { File.Delete(pngTmp); } catch { }
                        }
                    }
                    clone = t;
                    break;
                }
                default:
                    throw new NotSupportedException(item.GetType().Name);
            }
            // raw 元数据整段随克隆(3b 再打 guid 补丁) —— Save 时 raw 直写, 引擎读到与原版逐字节相同结构
            clone.RawMeta = (byte[])item.RawMeta?.Clone();
            return clone;
        }

        static Guid ResolveGuid(Guid g, Func<AssetItem, AssetItem> resolveDep)
        {
            if (g.Equals(Guid.Empty)) return g;
            var known = _finder?.Invoke(g);
            if (known == null) return g;
            var c = resolveDep(known);
            return c == known ? g : c.Guid;
        }

        static void PadTexture(Texture tt)
        {
            bool normal = tt.Name.Contains("_n") && !tt.Name.EndsWith("_no_skinning");
            byte r = normal ? (byte)128 : (byte)200;
            byte g = normal ? (byte)128 : (byte)200;
            byte b = normal ? (byte)255 : (byte)200;
            byte[] rgba = { r, g, b, 255 };
            var bc3 = Bc3Encoder.Encode(rgba, 1, 1);
            var loader = new ExternalLoader<TexturePixelData>(
                new TexturePixelData { PrimaryRawImage = bc3, RawImage = new[] { new[] { bc3 } } })
            { OwnerGuid = tt.Guid };
            loader.UserData[TexturePixelData.KEY_WIDTH] = 1;
            loader.UserData[TexturePixelData.KEY_HEIGHT] = 1;
            loader.UserData[TexturePixelData.KEY_ARRAY] = 1;
            loader.UserData[TexturePixelData.KEY_MIPMAP] = 1;
            loader.UserData[TexturePixelData.KEY_FORMAT] = TextureFormat.DXT5;
            tt.TypelessDataSegments.Add(loader);
            Console.Error.WriteLine($"  pad: 1x1 neutral texture {tt.Name} (源私库/BC7 无像素)");
        }

        static byte[] PatchGuids(byte[] raw, Dictionary<Guid, Guid> oldNew)
        {
            foreach (var kv in oldNew)
            {
                if (kv.Key.Equals(Guid.Empty)) continue;
                var from = kv.Key.ToByteArray();
                var to = kv.Value.ToByteArray();
                for (int i = 0; i + 16 <= raw.Length; i++)
                {
                    bool match = true;
                    for (int j = 0; j < 16; j++)
                        if (raw[i + j] != from[j]) { match = false; break; }
                    if (match)
                    {
                        for (int j = 0; j < 16; j++) raw[i + j] = to[j];
                    }
                }
            }
            return raw;
        }

        static bool ProbeSegmentOk(AbstractExternalLoader seg)
        {
            var gt = seg.GetType();
            if (gt.IsGenericType && gt.GetGenericTypeDefinition() == typeof(ExternalLoader<>)
                && gt.GetGenericArguments()[0] == typeof(ExternalData))
                return false; // 泛型参数 = 基类 ExternalData 的占位 loader → Loader<ExternalData>.SaveTo 走基类写出 null
            var p = gt.GetProperty("Data");
            if (p == null) return true;
            var d = p.GetValue(seg);
            if (d == null) return false;
            if (d.GetType() == typeof(ExternalData)) return false; // 类型未解析的占位段
            if (d is EditmodeMiscData) return false; // 编辑器专用数据,库无写回实现 → Save 必崩;运行时不需要
            if (d is TexturePixelData tpd && tpd.PrimaryRawImage == null) return false;
            if (d is VertexStreamData vs && vs.Positions == null) return false;
            return true;
        }

        static AbstractExternalLoader CloneSegment(object seg)
        {
            // ExternalLoader<T> 深拷贝: 共享 Data 字节(只读), 新 OwnerGuid/UserData 副本
            var t = seg.GetType();
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ExternalLoader<>))
            {
                try
                {
                    var dataProp = t.GetProperty("Data");
                    var data = dataProp?.GetValue(seg);
                    // 未解析占位段 = 裸 ExternalData（PrimaryRawImage=null）→ 跳过，写回必崩
                    if (data == null || data.GetType() == typeof(ExternalData)) return null;
                    var clone = Activator.CreateInstance(t, new[] { data });
                    var og = (Guid)t.GetProperty("OwnerGuid").GetValue(seg);
                    t.GetProperty("OwnerGuid").SetValue(clone, og);
                    var srcUd = (System.Collections.IDictionary)t.GetProperty("UserData").GetValue(seg);
                    var dstUd = (System.Collections.IDictionary)t.GetProperty("UserData").GetValue(clone);
                    if (srcUd != null && dstUd != null)
                        foreach (System.Collections.DictionaryEntry e in srcUd)
                            dstUd[e.Key] = e.Value;
                    return (AbstractExternalLoader)clone;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  warn: segment clone failed ({t.Name}): {ex.Message}");
                    return null;
                }
            }
            return (AbstractExternalLoader)seg;
        }

        static Material.ExtraMaterialSetting CopyExtra(Material.ExtraMaterialSetting o)
        {
            var n = new Material.ExtraMaterialSetting();
            n.AreamapScale = o.AreamapScale; n.AreamapAmount = o.AreamapAmount;
            n.DetailnormalScale = o.DetailnormalScale; n.NormalmapPower = o.NormalmapPower;
            n.MeshVectorArgument = o.MeshVectorArgument; n.MeshVectorArgument2 = o.MeshVectorArgument2;
            n.MeshFactorColorMultiplier = o.MeshFactorColorMultiplier; n.MeshFactor2ColorMultiplier = o.MeshFactor2ColorMultiplier;
            n.RenderOrder = o.RenderOrder; n.MipmapBias = o.MipmapBias;
            n.SpecularCoef = o.SpecularCoef; n.GlossCoef = o.GlossCoef;
            n.ParallaxAmount = o.ParallaxAmount; n.ParallaxOffset = o.ParallaxOffset;
            n.AmbientOcclusionCoef = o.AmbientOcclusionCoef; n.ExposureCompensation = o.ExposureCompensation;
            return n;
        }

        static ClothingMaterial CopyCloth(ClothingMaterial o)
        {
            // 头网格通常是 null/默认: 反射拷贝简单标量,失败则原样引用(只读安全)
            if (o == null) return null;
            var n = (ClothingMaterial)Activator.CreateInstance(o.GetType());
            foreach (var p in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || !p.CanWrite) continue;
                var pv = p.GetValue(o);
                if (pv == null || pv.GetType().IsPrimitive || pv is string) p.SetValue(n, pv);
            }
            return n;
        }

        static string CleanName(string n)
        {
            var s = n;
            if (s.StartsWith("2")) s = s.Substring(1);
            s = s.Replace("sho_", "");
            s = s.Replace("sho", "");
            return s;
        }

        static Guid DeterministicGuid(string s)
        {
            using var sha = SHA256.Create();
            byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            return new Guid(h.Take(16).ToArray());
        }
    }
}
