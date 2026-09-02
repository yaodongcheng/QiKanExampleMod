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
mgr.Load(new DirectoryInfo(dir));

if (!File.Exists(dir + "/dummy.lock"))
{
    // no-op to keep structure explicit
}

Console.WriteLine($"Loaded {mgr.LoadedPackages.Count} packages, {mgr.LoadedAssets.Count} assets");

switch (command)
{
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
    case "inspect":
    {
        var items = mgr.LoadedAssets
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
    case "list":
    {
        var items = mgr.LoadedAssets
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
        var items = mgr.LoadedAssets
            .Where(a => a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Name)
            .ToList();
        Console.WriteLine($"dump: {items.Count} matches");
        foreach (var item in items)
        {
            if (outDir == null) outDir = "./export_" + filter;
            Directory.CreateDirectory(outDir);
            try
            {
                if (item is Texture tex)
                {
                    if (tex.HasPixelData)
                    {
                        var path = Path.Combine(outDir, SafeName(tex.Name));
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
                    var path = Path.Combine(outDir, SafeName(meta.Name));
                    if (format == "obj")
                    {
                        ExportObj(path + ".obj", meta);
                        Console.WriteLine($"OK  mesh {item.Name} -> {path}.obj");
                    }
                    else if (format is "fbx" or "dae")
                    {
                        AssimpModelExporter.InitAssimp();
                        AssimpModelExporter.ExportToFile(path + "." + format, meta, null, null, null, 0, 0, 24f);
                        Console.WriteLine($"OK  mesh {item.Name} -> {path}.{format}");
                    }
                    else
                    {
                        Console.WriteLine($"META mesh {item.Name} ({meta.Meshes.Count} submeshes, no fbx requested)");
                    }
                }
                else if (item is Material mat)
                {
                    // Material 二进制元数据已可解析：打印全部字段 + 解析贴图引用名
                    var path = Path.Combine(outDir, SafeName(mat.Name) + ".mat.txt");
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
                        var refName = mgr.LoadedAssets.FirstOrDefault(a => a.Guid == kv.Value.Guid)?.Name ?? "?";
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
                Console.Error.WriteLine($"ERR  {item.Name}: {ex.Message}");
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
