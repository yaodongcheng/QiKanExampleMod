using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TpacTool.IO;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// editormeta：**先造编辑器资产**（贴图 / 材质），再导 FBX —— FBX 按材质名自动挂上。
    ///
    /// 为什么：Bannerlord 导入 FBX 时拿子网格的材质名去工程里找同名 Material 资产，
    /// 找不到就 "Unable to find material for mesh ..." 并把子网格留空。
    ///
    /// 编辑器资产 = 普通 tpac（元数据为主）：
    ///   Assets/textures/&lt;名&gt;_tex.tpac   Texture：Source=$BASE/...png、Format=DXT1、用途 albedo
    ///   Assets/materials/&lt;名&gt;_mtl.tpac  Material：flags + shader + 贴图槽
    ///
    /// 用法：
    ///   tpaccli editormeta --tex --name &lt;贴图名&gt; --png &lt;源图.png&gt; --out &lt;输出文件&gt;
    ///   tpaccli editormeta --mat --name &lt;材质名&gt; --tex &lt;贴图Guid&gt;
    ///                      [--shader &lt;guid&gt;] [--flags a,b,c] [--out &lt;输出文件&gt;]
    /// </summary>
    public static class EditorMeta
    {
        public static int Run(string[] args)
        {
            bool isTex = false, isMat = false;
            string name = null, png = null, outPath = null, texGuid = null;
            string shader = "328d3572-5e9e-4183-b49e-f451a19213d0"; // adobe_stone_wall 默认
            string fromPath = null, sourcePath = null, genMatsDir = null, mtlRef = null, aliasesPath = null;
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var flags = new List<string> { "use_specular", "bumpmap", "do_not_use_alpha" };
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--tex": isTex = true; break;
                    case "--mat": isMat = true; break;
                    case "--name": name = args[++i]; break;
                    case "--png": png = args[++i]; break;
                    case "--out": outPath = args[++i]; break;
                    case "--texguid": texGuid = args[++i]; break;
                    case "--shader": shader = args[++i]; break;
                    case "--flags": flags = new List<string>(args[++i].Split(',')); break;
                    case "--from": fromPath = args[++i]; break;
                    case "--source": sourcePath = args[++i]; break;
                    case "--genmats": genMatsDir = args[++i]; break;
                    case "--mtlref": mtlRef = args[++i]; break;
                    case "--aliases":
                        aliasesPath = args[++i];
                        foreach (var line in File.ReadAllLines(aliasesPath))
                        {
                            var t = line.Trim();
                            if (t.Length == 0 || t.StartsWith("#")) continue;
                            var parts = t.Split(new[] { '=' }, 2);
                            if (parts.Length == 2) aliases[parts[0].Trim()] = parts[1].Trim();
                        }
                        break;
                }
            }
            if (name == null || outPath == null) { Console.Error.WriteLine("need --name and --out"); return 1; }

            if (genMatsDir != null)
            {
                // 遍历编辑器生成的 *_tex.tpac：读名字+GUID，逐个克隆材质模板生成 *_mtl.tpac
                if (mtlRef == null) { Console.Error.WriteLine("--genmats needs --mtlref"); return 1; }
                var refPkg = new AssetPackage(mtlRef, true, false);
                AssetItem refItem = null;
                foreach (var it in refPkg.Items) { refItem = it; break; }
                if (!(refItem is Material refMat)) { Console.Error.WriteLine("mtlref is not a material: " + mtlRef); return 1; }
                Guid oldTex = Guid.Empty;
                foreach (var kv in refMat.Textures) { oldTex = kv.Value.Guid; break; }
                Console.WriteLine($"ref material slot0 -> {oldTex}");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
                // 先收集 贴图资产名 -> (guid)，别名要用
                var texGuids = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
                foreach (var tf in Directory.EnumerateFiles(genMatsDir, "*_tex.tpac"))
                {
                    var tp0 = new AssetPackage(tf, true, false);
                    foreach (var it0 in tp0.Items) if (it0 is Texture t0) texGuids[t0.Name] = t0.Guid;
                }
                int n = 0;
                foreach (var texFile in Directory.EnumerateFiles(genMatsDir, "*_tex.tpac"))
                {
                    var tp = new AssetPackage(texFile, true, false);
                    AssetItem texItem = null;
                    foreach (var it in tp.Items) { texItem = it; break; }
                    if (!(texItem is Texture tex)) continue;
                    string matName = tex.Name;   // 资产名就是 FBX 里的材质名

                    var clone = new AssetPackage(mtlRef, true, false);
                    AssetItem ci = null;
                    foreach (var it in clone.Items) { ci = it; break; }
                    var m = (Material)ci;
                    m.Name = matName;
                    m.Guid = Det("mat:" + matName);
                    m.Textures[0] = new AssetDependence<Texture>(tex.Guid);
                    // 🔴 RawMeta 优先于类型化字段：不补字节，保存出来还是模板的贴图 GUID
                    if (m.RawMeta != null)
                    {
                        var map = new Dictionary<Guid, Guid>();
                        if (!oldTex.Equals(Guid.Empty)) map[oldTex] = tex.Guid;
                        if (!refMat.Guid.Equals(Guid.Empty)) map[refMat.Guid] = m.Guid;
                        m.RawMeta = PatchGuids((byte[])m.RawMeta.Clone(), map);
                    }
                    var outp = new AssetPackage();
                    outp.Items.Add(m);
                    outp.Save(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outPath)), matName + "_mtl.tpac"));
                    n++;
                }
                // 别名：材质名 != 贴图名的情况（FBX 要的是 _ab，贴图导入成了 _abal 之类）
                foreach (var kv in aliases)
                {
                    if (!texGuids.TryGetValue(kv.Value, out var ag)) { Console.Error.WriteLine($"alias tex not found: {kv.Value}"); continue; }
                    var clone2 = new AssetPackage(mtlRef, true, false);
                    AssetItem ci2 = null;
                    foreach (var it in clone2.Items) { ci2 = it; break; }
                    var m2 = (Material)ci2;
                    m2.Name = kv.Key;
                    m2.Guid = Det("mat:" + kv.Key);
                    m2.Textures[0] = new AssetDependence<Texture>(ag);
                    if (m2.RawMeta != null)
                    {
                        var map2 = new Dictionary<Guid, Guid>();
                        if (!oldTex.Equals(Guid.Empty)) map2[oldTex] = ag;
                        if (!refMat.Guid.Equals(Guid.Empty)) map2[refMat.Guid] = m2.Guid;
                        m2.RawMeta = PatchGuids((byte[])m2.RawMeta.Clone(), map2);
                    }
                    var outp2 = new AssetPackage();
                    outp2.Items.Add(m2);
                    outp2.Save(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outPath)), kv.Key + "_mtl.tpac"));
                    n++;
                    Console.WriteLine($"  alias: {kv.Key} -> {kv.Value}");
                }
                Console.WriteLine($"generated {n} materials -> {Path.GetDirectoryName(Path.GetFullPath(outPath))}");
                return 0;
            }

            if (fromPath != null)
            {
                // 克隆编辑器自己产出的参考件：只改名字 / 源图 / 贴图槽，其余字段原样保留
                var src = new AssetPackage(fromPath, true, false);
                AssetItem item = null;
                foreach (var it in src.Items) { item = it; break; }
                if (item == null) { Console.Error.WriteLine("empty ref pack: " + fromPath); return 1; }
                item.Name = name;
                item.Guid = Det((isTex ? "tex:" : "mat:") + name);
                if (item is Texture t2)
                {
                    if (sourcePath != null) t2.Source = sourcePath;
                    // 编辑器资产不存像素：清掉数据段（Publish 时从源图现编）
                    t2.TypelessDataSegments.Clear();
                }
                else if (item is Material m2 && texGuid != null)
                {
                    m2.Textures[0] = new AssetDependence<Texture>(Guid.Parse(texGuid));
                }
                var pkg2 = new AssetPackage();
                pkg2.Items.Add(item);
                if (isTex) Console.WriteLine($"texture (cloned): {name}  guid={item.Guid}  source={sourcePath}");
                else Console.WriteLine($"material (cloned): {name}  guid={item.Guid}  slot0 -> {texGuid}");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
                pkg2.Save(outPath);
                Console.WriteLine($"saved {outPath} ({new FileInfo(outPath).Length:N0} bytes)");
                return 0;
            }

            var pkg = new AssetPackage();
            if (isTex)
            {
                if (png == null) { Console.Error.WriteLine("--tex needs --png"); return 1; }
                var asset = BuildTexture(name, png);
                pkg.Items.Add(asset);
                Console.WriteLine($"texture asset: {name}  guid={asset.Guid}  {asset.Width}x{asset.Height}  source={asset.Source}");
            }
            else if (isMat)
            {
                if (texGuid == null) { Console.Error.WriteLine("--mat needs --texguid"); return 1; }
                var mat = BuildMaterial(name, Guid.Parse(texGuid), Guid.Parse(shader), flags);
                pkg.Items.Add(mat);
                Console.WriteLine($"material asset: {name}  guid={mat.Guid}  shader={shader}  slot0 -> {texGuid}");
            }
            else { Console.Error.WriteLine("need --tex or --mat"); return 1; }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            pkg.Save(outPath);
            Console.WriteLine($"saved {outPath} ({new FileInfo(outPath).Length:N0} bytes)");
            return 0;
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
                    for (int j = 0; j < 16; j++) if (raw[i + j] != from[j]) { match = false; break; }
                    if (match) for (int j = 0; j < 16; j++) raw[i + j] = to[j];
                }
            }
            return raw;
        }

        static Guid Det(string s)
        {
            using var md5 = MD5.Create();
            return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes("editormeta:" + s)));
        }

        static Texture BuildTexture(string name, string pngPath)
        {
            var png = Png.Open(pngPath);
            byte[] rgba = new byte[png.Width * png.Height * 4];
            int pos = 0;
            for (int y = 0; y < png.Height; y++)
                for (int x = 0; x < png.Width; x++)
                {
                    var px = png.GetPixel(x, y);
                    rgba[pos++] = px.R; rgba[pos++] = px.G; rgba[pos++] = px.B; rgba[pos++] = px.A;
                }
            byte[] encoded = Bc3Encoder.EncodeBc1(rgba, png.Width, png.Height);
            var t = new Texture
            {
                Name = name,
                Source = pngPath,
                Flags = new List<string> { "dont_degrade", "dont_delay_loading" },
                Width = (uint)png.Width,
                Height = (uint)png.Height,
                MipmapCount = 1,
                ArrayCount = 1,
                Format = TextureFormat.DXT1,
                SystemFlags = new List<string>(),
                GeneratedAssets = new List<Tuple<Guid, Guid>>(),
            };
            t.Guid = Det("tex:" + name);
            var loader = new ExternalLoader<TexturePixelData>(new TexturePixelData
            {
                PrimaryRawImage = encoded,
                RawImage = new[] { new[] { encoded } },
            }) { OwnerGuid = t.Guid };
            loader.UserData[TexturePixelData.KEY_WIDTH] = png.Width;
            loader.UserData[TexturePixelData.KEY_HEIGHT] = png.Height;
            loader.UserData[TexturePixelData.KEY_ARRAY] = 1;
            loader.UserData[TexturePixelData.KEY_MIPMAP] = 1;
            loader.UserData[TexturePixelData.KEY_FORMAT] = TextureFormat.DXT1;
            t.TypelessDataSegments.Add(loader);
            return t;
        }

        static Material BuildMaterial(string name, Guid tex, Guid shader, List<string> flags)
        {
            var m = new Material
            {
                Name = name,
                Guid = Det("mat:" + name),
                BlendMode = "no_alpha_blend",
                AlphaTest = 0,
                Shader = new AssetDependence<Shader>(shader),
            };
            foreach (var f in flags) m.ShaderMaterialFlags.Add(f.Trim());
            m.VertexLayoutFlags.Add("bumpmap");
            m.Textures[0] = new AssetDependence<Texture>(tex);
            var extra = new Material.ExtraMaterialSetting
            {
                AmbientOcclusionCoef = 1f,
                SpecularCoef = 1f,
                GlossCoef = 1f,
            };
            m.ExtraMaterialSettings = extra;
            return m;
        }
    }
}
