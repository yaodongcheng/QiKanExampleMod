using System;
using System.IO;
using System.Linq;
using TpacCli;
using TpacTool.Lib;
using TpacTool.IO;
using TpacTool.IO.Assimp;

// tpaccli <command> [args]
//   list     --packdir <dir> [--filter <substr>]
//   dump     --packdir <dir> --filter <substr> [--out <dir>] [--format png|dds|raw]
//   inspect  --packdir <dir> --filter <substr>        (打印 texture 全字段，提取模板用)
//   makepack --manifest <json> --out <dir>            (manifest 描述 -> 全新 tpac 包)
//
// Uses TpacTool.Lib to read TaleWorlds AssetPackages and export named assets.

var dir = Environment.CurrentDirectory;
string filter = null;
string outDir = null;
string format = "png";
string mapping = null;

string[] cmdLine = Environment.GetCommandLineArgs().Skip(1).ToArray();

string command = cmdLine.Length > 0 ? cmdLine[0] : "help";

// assetclone 有自己完整参数集——顶层解析只认命令名,参数原样透传
if (command != "assetclone")
{
    for (int i = 1; i < cmdLine.Length; i++)
    {
        switch (cmdLine[i])
        {
            case "--packdir": dir = args[++i]; break;
            case "--filter": filter = args[++i]; break;
            case "--out": outDir = args[++i]; break;
            case "--format": format = args[++i]; break;
            case "--mapping": mapping = args[++i]; break;
            default: Console.Error.WriteLine("unknown arg: " + args[i]); break;
        }
    }
}

if (command is "help" or "-h" or "--help")
{
    Console.WriteLine("tpaccli <list|dump|roundtrip> --packdir <dir> [--filter s] [--out dir] [--format png|dds|obj|fbx|dae]");
    return 1;
}

var mgr = new AssetManager();
// 多目录(--packdir 逗号分隔): 递归收集全部包 → 全局 byGuid + 缺项不炸的 resolver
var packDirs = (dir ?? ".").Split(',').Select(d => d.Trim()).Where(d => d.Length > 0).ToArray();
try { mgr.Load(new DirectoryInfo(packDirs[0])); } catch { }
var byGuid = new Dictionary<Guid, AssetItem>();
foreach (var pd in packDirs)
    foreach (var f in Directory.EnumerateFiles(pd, "*.tpac", SearchOption.AllDirectories))
        foreach (var it in new AssetPackage(f, true, false).Items)
            byGuid[it.Guid] = it;
DefaultDependenceResolver.Instance = new ByGuidResolver(byGuid);
IReadOnlyList<AssetItem> assets = byGuid.Values.ToList();
Console.WriteLine($"Loaded {mgr.LoadedPackages.Count} packages from {packDirs.Length} dirs, {assets.Count} assets");

if (!File.Exists(dir + "/dummy.lock"))
{
    // no-op to keep structure explicit
}


switch (command)
{
    case "listformats":
    {
        AssimpModelExporter.InitAssimp();
        foreach (var f in Assimp.Unmanaged.AssimpLibrary.Instance.GetExportFormatDescriptions())
            Console.WriteLine(f.FormatId + "  " + f.Description);
        return 0;
    }
    case "assetclone":
    {
        return AssetClone.Run(cmdLine.Skip(1).ToArray());
    }
    case "makepack":
    {
        string manifestPath = null, makeOutDir = ".";
        for (int i = 1; i < cmdLine.Length; i++)
        {
            if (cmdLine[i] == "--manifest") manifestPath = cmdLine[++i];
            else if (cmdLine[i] == "--out") makeOutDir = cmdLine[++i];
        }
        if (manifestPath == null)
        {
            Console.Error.WriteLine("makepack requires --manifest <json>");
            return 1;
        }
        return MakePack.Run(manifestPath, makeOutDir);
    }
    case "texreplace":
    {
        return ReplaceTex.Run(dir, filter, mapping, outDir);
    }
    case "inspect":
    {
        var items = assets
            .Where(a => a is Texture && (filter == null || a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(a => a.Name)
            .ToList();
        if (items.Count == 0)
        {
            Console.WriteLine("no texture matched");
            return 1;
        }
        foreach (var it in items)
        {
            var tex = (Texture) it;
            Console.WriteLine($"== {tex.Name} (type {tex.Type}) guid {tex.Guid} version {tex.Version} ==");
            Console.WriteLine($"  BillboardMaterial.Guid = {tex.BillboardMaterial.Guid}");
            Console.WriteLine($"  UnknownUint1 = {tex.UnknownUint1}");
            Console.WriteLine($"  Source = \"{tex.Source}\"");
            Console.WriteLine($"  UnknownUlong = {tex.UnknownUlong}");
            Console.WriteLine($"  UnknownBool = {tex.UnknownBool}");
            Console.WriteLine($"  UnknownUint2 = {tex.UnknownUint2}");
            Console.WriteLine($"  Flags = [{string.Join(",", tex.Flags)}]");
            Console.WriteLine($"  UnknownUint3 = {tex.UnknownUint3}");
            Console.WriteLine($"  UnknownByte = {tex.UnknownByte}");
            Console.WriteLine($"  Width = {tex.Width}");
            Console.WriteLine($"  Height = {tex.Height}");
            Console.WriteLine($"  UnknownUint4 = {tex.UnknownUint4}");
            Console.WriteLine($"  MipmapCount = {tex.MipmapCount}");
            Console.WriteLine($"  ArrayCount = {tex.ArrayCount}");
            Console.WriteLine($"  Format = {tex.Format}");
            Console.WriteLine($"  UnknownUint5 = {tex.UnknownUint5}");
            Console.WriteLine($"  SystemFlags = [{string.Join(",", tex.SystemFlags)}]");
            Console.WriteLine($"  UnknownUint6 = {tex.UnknownUint6}");
            Console.WriteLine($"  UnknownUint7 = {tex.UnknownUint7}");
            Console.WriteLine($"  GeneratedAssets = {tex.GeneratedAssets.Count}");
            Console.WriteLine($"  UnknownUlong2 = {tex.UnknownUlong2}");
            foreach (var seg in tex.TypelessDataSegments)
            {
                Console.WriteLine($"  [segment] type {seg.TypeGuid} owner {seg.OwnerGuid} loaded={seg.IsDataLoaded()}");
                foreach (var kv in seg.UserData)
                {
                    Console.WriteLine($"      ud[{kv.Key}] = {kv.Value}");
                }
            }
        }
        return 0;
    }
    case "groups":
    {
        // 组名 = Source 字段里 AssetSources/ 的下一级目录 (如 GauntletUI) —— EmAssetPackages 组包按此归组
        var groups = new SortedDictionary<string, Dictionary<string, int>>();
        foreach (var item in assets)
        {
            string group = GroupOf(item);
            if (!groups.TryGetValue(group, out var tc)) groups[group] = tc = new Dictionary<string, int>();
            var t = item.GetType().Name;
            tc[t] = tc.TryGetValue(t, out var n) ? n + 1 : 1;
        }
        Console.WriteLine($"# groups = {groups.Count}, assets = {assets.Count}");
        foreach (var kv in groups)
            Console.WriteLine($"{kv.Key}\t{string.Join(", ", kv.Value.Select(x => $"{x.Key}:{x.Value}"))}");
        return 0;
    }
    case "segs":
    {
        // 段类型分布: 逐 item 打印其 TypelessDataSegments 类型统计 (对照 edit data 段是否存在于包内)
        var stat = new SortedDictionary<string, Dictionary<string, int>>();
        var segTypes = new SortedDictionary<string, int>();
        foreach (var item in assets)
        {
            if (filter != null && !item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            var t = item.GetType().Name;
            if (!stat.TryGetValue(t, out var c)) stat[t] = c = new Dictionary<string, int>();
            foreach (var seg in item.TypelessDataSegments)
            {
                var st = seg.GetType().Name; // ExternalLoader`1[...]
                if (st.StartsWith("ExternalLoader"))
                    st = "ExternalLoader<" + (seg.GetType().GetGenericArguments().Length > 0 ? seg.GetType().GetGenericArguments()[0].Name : "?") + ">";
                c[st] = c.TryGetValue(st, out var n) ? n + 1 : 1;
            }
        }
        foreach (var kv in stat)
            Console.WriteLine($"{kv.Key}\t{string.Join(", ", kv.Value.Select(x => $"{x.Key}:{x.Value}"))}");
        return 0;
    }
    case "missingrefs":
    {
        // 多目录(--packdir 逗号分隔,递归 *.tpac): 收集 byGuid, 报告 Metamesh 引用的材质/贴图 guid 是否齐
        var dirs = (dir ?? ".").Split(',').Select(d => d.Trim()).Where(d => d.Length > 0).ToArray();
        var refsGuid = new Dictionary<Guid, AssetItem>();
        foreach (var dd in dirs)
        {
            if (!Directory.Exists(dd)) { Console.Error.WriteLine("missing dir: " + dd); return 1; }
            foreach (var f in Directory.EnumerateFiles(dd, "*.tpac", SearchOption.AllDirectories))
            {
                try
                {
                    var pkg = new AssetPackage(f, true, false);
                    foreach (var it in pkg.Items) byGuid[it.Guid] = it;
                }
                catch (Exception ex) { Console.Error.WriteLine("warn load " + f + ": " + ex.Message); }
            }
        }
        Console.WriteLine($"refs: {refsGuid.Count} assets from {dirs.Length} dirs");
        int missing = 0;
        foreach (var kv in refsGuid)
        {
            if (kv.Value is not Metamesh meta) continue;
            var check = new List<Guid> { meta.Material };
            foreach (var m in meta.Meshes)
            {
                check.Add(m.Material.Guid);
                if (m.SecondMaterial != null) check.Add(m.SecondMaterial.Guid);
            }
            foreach (var g in check)
            {
                if (g.Equals(Guid.Empty)) continue;
                if (!refsGuid.ContainsKey(g))
                {
                    missing++;
                    if (missing <= 30) Console.WriteLine($"  MISSING {kv.Value.Name} needs mat-guid {g}");
                }
            }
        }
        Console.WriteLine($"missing material refs: {missing}");
        return 0;
    }
    case "list":
    {
        var items = assets
            .Where(a => filter == null || a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Name);
        foreach (var item in items)
        {
            Console.WriteLine($"{item.Name}\t{item.Type}");
        }
        return 0;
    }
    case "roundtrip":
    {
        if (filter == null)
        {
            Console.Error.WriteLine("roundtrip requires --filter (asset name substring)");
            return 1;
        }
        // Load one package whose name matches filter, save it back, re-load, verify.
        var match = mgr.LoadedPackages.FirstOrDefault(p =>
            p.File != null && p.File.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            Console.Error.WriteLine("no package matched " + filter);
            return 1;
        }
        Console.WriteLine($"roundtrip on {match.File.Name} ({match.Items.Count} items)");
        var outPath = Path.Combine(outDir ?? ".", "roundtrip_" + match.File.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
        match.Save(outPath);
        Console.WriteLine($"saved {outPath} ({new FileInfo(outPath).Length} bytes)");
        var back = new AssetPackage(outPath);
        Console.WriteLine($"reloaded: {back.Items.Count} items; same guid: {back.Guid == match.Guid}");
        Console.WriteLine($"first item: {back.Items[0].Name} guid {back.Items[0].Guid}");
        return 0;
    }

    case "dump":
    {
        if (filter == null)
        {
            Console.Error.WriteLine("dump requires --filter");
            return 1;
        }
        var items = assets
            .Where(a => a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Name)
            .ToList();
        Console.WriteLine($"dump: {items.Count} matches");
        foreach (var item in items)
        {
            if (outDir == null) outDir = "./export_" + filter;
            if (item is Texture && format != "png" && format != "dds")
            {
                // 纹理仅在 png/dds 模式下导出, 避免 fbx/obj 模式下产出假后缀垃圾
                continue;
            }
            var targetDir = Path.Combine(outDir, SubDirOf(item));
            Directory.CreateDirectory(targetDir);
            try
            {
                if (item is Texture tex)
                {
                    if (tex.HasPixelData)
                    {
                        var path = Path.Combine(targetDir, SafeName(tex.Name));
                        if (format == "dds")
                        {
                            TextureExporter.ExportToFile(path + ".dds", tex);
                        }
                        else
                        {
                            TextureExporter.ExportToFile(path + ".png", tex);
                        }
                        Console.WriteLine($"OK  texture {item.Name} -> {path}.{format}");
                    }
                    else
                    {
                        Console.WriteLine($"SKIP texture {item.Name} (no pixel data)");
                    }
                }
                else if (item is Metamesh meta)
                {
                    var path = Path.Combine(targetDir, SafeName(meta.Name));
                    if (format == "obj")
                    {
                        ExportObj(path + ".obj", meta);
                        Console.WriteLine($"OK  mesh {item.Name} -> {path}.obj");
                    }
                    else if (format is "fbx" or "dae" or "gltf" or "gltf2")
                    {
                        AssimpModelExporter.InitAssimp();
                        // 只有蒙皮网格(SkinDataSize>0)才关联骨架/动画; 纯静态件(建筑/道具)不绑骨
                        var skinned = meta.Meshes.Any(m => m.SkinDataSize > 0);
                        Skeleton skel = null;
                        SkeletalAnimation anim = null;
                        if (skinned)
                        {
                            // 人形骨架优先 (织丰本体无人体骨架, 来自 Native human 组); 次选动画多数派; 再任意
                            skel = assets.OfType<Skeleton>().FirstOrDefault(s =>
                                s.Name.ToLowerInvariant().Contains("human"));
                            if (skel == null)
                            {
                                var anims = assets.OfType<SkeletalAnimation>().ToList();
                                if (anims.Count > 0)
                                {
                                    var mainGuid = anims.GroupBy(a => a.Skeleton).OrderByDescending(g => g.Count()).First().Key;
                                    skel = assets.OfType<Skeleton>().FirstOrDefault(s => s.Guid == mainGuid);
                                }
                            }
                            skel ??= assets.OfType<Skeleton>().FirstOrDefault();
                        }
                        if (skinned)
                        foreach (var a in assets)
                            if (a is SkeletalAnimation sa &&
                                (sa.Skeleton == (skel?.Guid ?? Guid.Empty) || sa.GeometryGuid == meta.Guid))
                            { anim = sa; break; }
                        var noskel = Environment.GetEnvironmentVariable("TPAC_NO_SKEL") != null;
                        if (format == "gltf" || format == "gltf2")
                            ModelExporter.ExportToFile(new Gltf2Exporter(), path + ".gltf", meta,
                                noskel ? null : skel, noskel ? null : anim, null, 0);
                        else
                            AssimpModelExporter.ExportToFile(path + "." + format, meta, noskel ? null : skel, noskel ? null : anim, null, 0, 0, 24f);
                        Console.WriteLine($"OK  mesh {item.Name} -> {path}.{format} (skel={skel?.Name ?? "none"}, anim={anim?.Name ?? "none"})");
                    }
                    else
                    {
                        Console.WriteLine($"META mesh {item.Name} ({meta.Meshes.Count} submeshes, no fbx requested)");
                    }
                }
                else if (item is Material mat)
                {
                    // Material 二进制元数据已可解析：打印全部字段 + 解析贴图引用名
                    var path = Path.Combine(targetDir, SafeName(mat.Name) + ".mat.txt");
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"material {mat.Name}");
                    sb.AppendLine($"  version = {mat.Version}");
                    sb.AppendLine($"  typeGuid = {mat.Type}");
                    sb.AppendLine($"  billboard = {mat.BillboardGuid}");
                    sb.AppendLine($"  flags = [{string.Join(",", mat.Flags)}]");
                    sb.AppendLine($"  vertexLayoutFlags = [{string.Join(",", mat.VertexLayoutFlags)}]");
                    sb.AppendLine($"  blend = {mat.BlendMode}");
                    sb.AppendLine($"  shader = {mat.Shader.Guid}");
                    sb.AppendLine($"  shaderFlags = [{string.Join(",", mat.ShaderMaterialFlags)}]");
                    sb.AppendLine($"  alphaTest = {mat.AlphaTest}");
                    sb.AppendLine($"  extra: ao={mat.ExtraMaterialSettings.AmbientOcclusionCoef} spec={mat.ExtraMaterialSettings.SpecularCoef} gloss={mat.ExtraMaterialSettings.GlossCoef}");
                    foreach (var kv in mat.Textures.OrderBy(k => k.Key))
                    {
                        var refName = assets.FirstOrDefault(a => a.Guid == kv.Value.Guid)?.Name ?? "?";
                        sb.AppendLine($"  tex[{kv.Key}] = {kv.Value.Guid} ({refName})");
                    }
                    File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
                    Console.WriteLine($"OK  material {mat.Name} -> {path}");
                }
                else
                {
                    // non-texture: save the raw metadata blob for now
                    var path = Path.Combine(outDir, SafeName(item.Name) + ".meta");
                    File.WriteAllBytes(path, item.WriteMetadata());
                    Console.WriteLine($"META {item.GetType().Name} {item.Name} -> {path}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERR  {item.Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }
        return 0;
    }
}

return 0;

static string SafeName(string name)
{
    foreach (var ch in Path.GetInvalidFileNameChars())
        name = name.Replace(ch, '_');
    return name;
}

static void ExportObj(string path, Metamesh meta)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("# exported by tpaccli from " + meta.Name);
    int vOffset = 0;
    foreach (var mesh in meta.Meshes)
    {
        if (mesh?.VertexStream?.Data is not { } vs) continue;
        if (vs.Positions == null || vs.Indices == null) continue;
        sb.AppendLine("o " + mesh.Name);
        for (int i = 0; i < vs.Positions.Length; i++)
        {
            var p = vs.Positions[i];
            sb.AppendLine($"v {p.X} {p.Y} {p.Z}");
        }
        if (vs.Uv1 != null)
        {
            for (int i = 0; i < vs.Uv1.Length; i++)
            {
                var uv = vs.Uv1[i];
                sb.AppendLine($"vt {uv.X} {uv.Y}");
            }
        }
        bool hasUv = vs.Uv1 != null && vs.Uv1.Length == vs.Positions.Length;
        for (int i = 0; i + 2 < vs.Indices.Length; i += 3)
        {
            int a = vs.Indices[i] + 1 + vOffset;
            int b = vs.Indices[i + 1] + 1 + vOffset;
            int c = vs.Indices[i + 2] + 1 + vOffset;
            if (hasUv)
                sb.AppendLine($"f {a}/{a} {b}/{b} {c}/{c}");
            else
                sb.AppendLine($"f {a} {b} {c}");
        }
        vOffset += vs.Positions.Length;
    }
    File.WriteAllText(path, sb.ToString());
}

static string GroupOf(AssetItem item)
{
    // 组名 = Source 字段里 AssetSources/ 的下一级目录 (如 GauntletUI)
    string src = null;
    foreach (var p in item.GetType().GetProperties())
    {
        if (p.PropertyType == typeof(string) && (p.Name == "Source" || p.Name == "Original"))
        {
            src = p.GetValue(item) as string;
            if (!string.IsNullOrEmpty(src)) break;
        }
    }
    if (string.IsNullOrEmpty(src))
    {
        foreach (var p in item.GetType().GetProperties())
        {
            if (p.PropertyType != typeof(string)) continue;
            var v = p.GetValue(item) as string;
            if (v != null && v.Contains("AssetSources")) { src = v; break; }
        }
    }
    if (string.IsNullOrEmpty(src)) return "(none)";
    var m = System.Text.RegularExpressions.Regex.Match(src, @"AssetSources/([^/\\]+)[/\\]");
    return m.Success ? m.Groups[1].Value : "(unparsed)";
}

static string SubDirOf(AssetItem item)
{
    string sub = item switch
    {
        Texture => GroupOf(item),
        _ => PrefixOf(item.Name),
    };
    if (string.IsNullOrEmpty(sub)) sub = "misc";
    return SafeName(sub);
}

static string PrefixOf(string name)
{
    var idx = name.IndexOf('_');
    var s = idx > 0 ? name.Substring(0, idx) : name;
    s = s.ToLowerInvariant();
    foreach (var c in Path.GetInvalidFileNameChars()) if (s.Contains(c)) s = s.Replace(c, '_');
    return string.IsNullOrEmpty(s) ? "misc" : s;
}

sealed class ByGuidResolver : IDependenceResolver
{
    readonly Dictionary<Guid, AssetItem> _map;
    public ByGuidResolver(Dictionary<Guid, AssetItem> map) { _map = map; }
    public bool Resolve<T>(Guid guid, string name, out T result) where T : class, IDependence
    {
        if (_map.TryGetValue(guid, out var it) && it is T t) { result = t; return true; }
        result = (T)(IDependence)PlaceholderFor<T>(guid);
        return true; // 缺失依赖 → 占位实体, 防止 UnresolvedDependenceException 中断导出
    }
    static IDependence PlaceholderFor<T>(Guid guid) where T : class
    {
        var nm = "missing_" + guid.ToString("N").Substring(0, 8);
        if (typeof(T) == typeof(Texture)) return new Texture { Name = nm, Guid = guid };
        if (typeof(T) == typeof(Material)) return new Material { Name = nm, Guid = guid };
        if (typeof(T) == typeof(Skeleton)) return new Skeleton { Name = nm, Guid = guid };
        if (typeof(T) == typeof(Metamesh)) return new Metamesh { Name = nm, Guid = guid };
        throw new NotSupportedException(typeof(T).Name);
    }
}
