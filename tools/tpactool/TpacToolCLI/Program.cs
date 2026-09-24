using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using TpacCli;
using TpacTool.Lib;
using TpacTool.IO;
using TpacTool.IO.Assimp;

// tpaccli <command> [args]
//   list     --packdir <dir> [--filter <substr>]
//   dump     --packdir <dir> --filter <substr> [--out <dir>] [--format png|dds|raw]
//   inspect  --packdir <dir> --filter <substr>        (打印 texture 全字段，提取模板用)
//   clipinfo --packdir <dir> [--filter <substr>]      (打印 AnimationClip 全字段：Flags / ClipUsages / displacement 向量)
//   makepack --manifest <json> --out <dir>            (manifest 描述 -> 全新 tpac 包)
//
// Uses TpacTool.Lib to read TaleWorlds AssetPackages and export named assets.

var dir = Environment.CurrentDirectory;
string filter = null;
string outDir = null;
string format = "png";
string mapping = null;
bool mapsonly = false;
bool allArg = false;     // animbones: 全量一行一条
string dispArg = null;   // clipset: "X,Y,Z"
string endArg = null;    // clipset: endProgress（可省）
string durArg = null;    // clipduration: 新 Duration（秒），或 "auto"

string[] cmdLine = Environment.GetCommandLineArgs().Skip(1).ToArray();

string command = cmdLine.Length > 0 ? cmdLine[0] : "help";

// assetclone / morphinfo / morphfix 有自己完整参数集——顶层解析只认命令名,参数原样透传
if (command is not ("assetclone" or "morphinfo" or "morphfix" or "skinfix" or "meshdiff" or "clothinfo" or "particleimport"))
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
            case "--mapsonly": mapsonly = true; break;
            case "--all": allArg = true; break;
            case "--disp": dispArg = cmdLine[++i]; break;
            case "--end": endArg = cmdLine[++i]; break;
            case "--duration": durArg = cmdLine[++i]; break;
            default: Console.Error.WriteLine("unknown arg: " + args[i]); break;
        }
    }
}

if (command is "help" or "-h" or "--help")
{
    Console.WriteLine("tpaccli <list|dump|roundtrip> --packdir <dir> [--filter s] [--out dir] [--format png|dds|obj|fbx|dae]");
    return 1;
}

// morphinfo / morphfix / skinfix / metaparts 自带参数集且自己做加载——放在全局 preload 之前，避免被无关 tpac 拖累/拖崩
if (command is "morphinfo" or "morphfix" or "skinfix" or "meshdiff" or "metaparts" or "morphmap" or "clothinfo")
{
    string mDir = null, mFilter = null, mOut = null, mOrder = null;
    int mTarget = 101, mBone = 13, mMaxFrames = 0;
    float mMinMm = 0.05f;
    bool mClearMat = false, mFullMat = false, mForce = false, mClearFlags = false;
    for (int i = 1; i < cmdLine.Length; i++)
    {
        switch (cmdLine[i])
        {
            case "--packdir": mDir = cmdLine[++i]; break;
            case "--filter": mFilter = cmdLine[++i]; break;
            case "--out": mOut = cmdLine[++i]; break;
            case "--order": mOrder = cmdLine[++i]; break;
            case "--target": mTarget = int.Parse(cmdLine[++i]); break;
            case "--bone": mBone = int.Parse(cmdLine[++i]); break;
            case "--minmm": mMinMm = float.Parse(cmdLine[++i]); break;
            case "--maxframes": mMaxFrames = int.Parse(cmdLine[++i]); break;
            case "--clearmat": mClearMat = true; break;
            case "--fullmat": mFullMat = true; break;
            case "--force": mForce = true; break;
            case "--clearflags": mClearFlags = true; break;
        }
    }
    return command switch
    {
        "morphinfo" => MorphFix.Info(mDir, mFilter),
        "morphmap" => MorphFix.MorphMap(mDir, mFilter, mMinMm, mMaxFrames),
        "morphfix" => MorphFix.Fix(mDir, mFilter, mOut, mTarget, mClearMat),
        "meshdiff" => MeshDiff.Run(mDir, mFilter, mOut),
        "metaparts" => MetaParts.Run(mDir, mFilter, mOut, mOrder, mClearFlags),
        "clothinfo" => ClothInfo.Run(mDir, mFilter, mOut),
        _ => MorphFix.SkinFix(mDir, mFilter, mOut, mBone, mFullMat, mForce),
    };
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
    case "particleimport":
    {
        return ParticleImport.Run(cmdLine.Skip(1).ToArray());
    }
    case "prtdump":
    {
        return ParticleDump.Run(dir, filter);
    }
    case "prtroundtrip":
    {
        return ParticleRoundtrip.Run(dir, filter);
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
    case "texinfo":
    {
        // 贴图导入设置速查：色彩空间/格式/标记。用途 = 比对「我们的贴图 vs 原版贴图」的 sRGB 标记
        // （脸部 shader 只对 tex[1] 做 INPUT_TEX_GAMMA，tex[0] 期望硬件按贴图标记转换 → 标记错 = 整体偏暗）
        var texs = assets.OfType<Texture>()
            .Where(a => filter == null || a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Name);
        foreach (var t in texs)
        {
            Console.WriteLine($"{t.Name,-40} fmt={t.Format,-14} {t.Width}x{t.Height} mips={t.MipmapCount} "
                            + $"flags=[{string.Join(",", t.Flags ?? new List<string>())}] "
                            + $"sys=[{string.Join(",", t.SystemFlags ?? new List<string>())}]");
        }
        return 0;
    }
    case "animbones":
        // 离线量「这条动画动了哪些骨」—— 筛"轨道里没写腿"的动画用（引擎没有按骨遮罩接口）
        return AnimBones.Run(assets, byGuid, filter, allArg);
    case "clipinfo":
    {
        // AnimationClip 全字段速查：Duration / Source 区间 / Flags / ClipUsages（含 displacement 向量）
        // 用途 = 不开 ModKit 就能核对两件事：① 这条 clip 有没有带位移数据 ② 它绑的是哪条骨架动画
        var clips = assets.OfType<AnimationClip>()
            .Where(a => filter == null || a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Name)
            .ToList();
        foreach (var c in clips)
        {
            string skel = "(unresolved)";
            if (!c.Animation.Equals(Guid.Empty) && byGuid.TryGetValue(c.Animation, out var sk))
                skel = $"{sk.Name} [{sk.GetType().Name}]";
            Console.WriteLine($"== {c.Name} ==");
            Console.WriteLine($"  Duration        = {c.Duration}");
            Console.WriteLine($"  Source1 / 2     = {c.Source1} / {c.Source2}");
            Console.WriteLine($"  SkeletalAnim    = {skel}   (guid {c.Animation})");
            Console.WriteLine($"  Flags           = [{(c.Flags == null ? "" : string.Join(", ", c.Flags))}]");
            Console.WriteLine($"  Priority={c.Priority} Param1={c.Param1} Param2={c.Param2} Param3={c.Param3}");
            Console.WriteLine($"  BlendIn={c.BlendInPeriod} BlendOut={c.BlendOutPeriod} DoNotInterpolate={c.DoNotInterpolate}");
            Console.WriteLine($"  ContinueWith    = {(string.IsNullOrEmpty(c.ContinueWithAction) ? "(empty)" : "\"" + c.ContinueWithAction + "\"")}");
            Console.WriteLine($"  BlendsWith      = {(string.IsNullOrEmpty(c.BlendsWithAction) ? "(empty)" : "\"" + c.BlendsWithAction + "\"")}");
            Console.WriteLine($"  HandPose L/R    = {c.LeftHandPose} / {c.RightHandPose}");
            Console.WriteLine($"  ClipUsages      = {c.ClipUsages.Count}");
            foreach (var u in c.ClipUsages)
            {
                if (u is AnimationClip.DisplacementUsage d)
                    Console.WriteLine($"    [displacement] vector=({d.DisplacementVector.X:0.####}, {d.DisplacementVector.Y:0.####}, "
                                    + $"{d.DisplacementVector.Z:0.####}) |v|={d.DisplacementVector.Length():0.####} "
                                    + $"endProgress={d.DisplacementEndProgress} rawUInt={d.UnknownUInt}");
                else
                    Console.WriteLine($"    [{u.Type}] rawUInt={u.UnknownUInt}");
            }
        }
        if (clips.Count == 0)
        {
            Console.WriteLine("no animation clip matched");
            return 1;
        }
        return 0;
    }
    case "clipset":
    {
        // 改 AnimationClip 的 displacement 用法（骑砍2 引擎靠这栏推动画角色位移；TRF 里那条位置轨引擎不认）。
        // 用法：clipset --packdir <含该 clip 的 tpac 目录> --filter <clip 名> --disp X,Y,Z [--end 0.4] --out <目录>
        if (filter == null || dispArg == null)
        {
            Console.Error.WriteLine("clipset requires --filter <clipName> --disp X,Y,Z [--end <endProgress>] [--out dir]");
            return 1;
        }
        var parts = dispArg.Split(',');
        if (parts.Length != 3)
        {
            Console.Error.WriteLine("--disp must be three comma-separated numbers, e.g. 0.2462,3.6734,0");
            return 1;
        }
        float[] v;
        try
        {
            v = parts.Select(s => float.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
        }
        catch (FormatException)
        {
            Console.Error.WriteLine("--disp 里有解析不出的数字: " + dispArg);
            return 1;
        }
        float endP = float.NaN;
        if (endArg != null && !float.TryParse(endArg.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out endP))
        {
            Console.Error.WriteLine("--end 解析不出: " + endArg);
            return 1;
        }

        // ① 先只读头(便宜)，定位含该 clip 的那个 .tpac —— 避免把几百 MB 的包整个读进内存
        string target = null;
        var scanDirs = (dir ?? ".").Split(',').Select(d => d.Trim()).Where(d => d.Length > 0).ToArray();
        foreach (var pd in scanDirs)
        {
            if (!Directory.Exists(pd)) continue;
            foreach (var f in Directory.EnumerateFiles(pd, "*.tpac", SearchOption.AllDirectories))
            {
                if (new AssetPackage(f, true, false).Items.Any(i =>
                        i.Type.Equals(AnimationClip.TYPE_GUID) &&
                        i.Name.Equals(filter, StringComparison.OrdinalIgnoreCase)))
                {
                    target = f;
                    break;
                }
            }
            if (target != null) break;
        }
        if (target == null)
        {
            Console.Error.WriteLine($"没有哪个 .tpac 里含名为 '{filter}' 的 AnimationClip");
            return 1;
        }
        Console.WriteLine($"目标包: {target}");

        // ② 只读头（🔴 **故意不读数据段**）—— 两个理由：
        //    ① `loadDataNow:true` 会去解析每个数据段，而库里 OptimizedAnimation.ReadData 与真实数据不兼容
        //       （实测抛 `Frames not equal: 102 - -1863246975`）→ 整包加载直接崩；
        //    ② `AssetPackage.Save` 对**未加载**的数据段是原样搬运（`roundtrip` 命令走的同一条路，实测
        //       存出来的包字节数与原件完全相同）。我们要改的只有元数据，数据段本就不该被重新编码。
        var pkg = new AssetPackage(target, true, false);
        AnimationClip targetClip = null;
        foreach (var it in pkg.Items)
            if (it.Type.Equals(AnimationClip.TYPE_GUID) && it.Name.Equals(filter, StringComparison.OrdinalIgnoreCase))
                targetClip = (AnimationClip) it;
        if (targetClip == null)
        {
            Console.Error.WriteLine("二遍扫描没找到该 clip（加载模式不一致？）");
            return 1;
        }

        // ③ 定位 displacement 用法在【原始元数字节】里的偏移
        //    🔴 为什么不改对象再序列化：库的 AnimationClip.WriteMetadata 有两处与真实数据不符，
        //    只有走"重新序列化"这条路才会暴露（实测 15 字节差异里混着 3 处非预期改动）：
        //      · 硬写 `stream.Write(5)`，而原文件 metadata version = **6**（版本字节被降级）
        //      · `WriteVec3AsVec4` 把 vec4 的第 4 分量写成 0，原文件是 **1.0**
        //    ⇒ 只就地改那 16 个字节（x/y/z + endProgress），其余字节一个不碰。
        var raw = targetClip.RawMeta;
        if (raw == null)
        {
            Console.Error.WriteLine("该 clip 没有 RawMeta（不是从文件读来的），拒绝走字节替换路径");
            return 1;
        }
        string marker = "displacement";
        int at = -1;
        for (int i = 0; i + marker.Length <= raw.Length; i++)
        {
            bool hit = true;
            for (int k = 0; k < marker.Length; k++)
                if (raw[i + k] != (byte) marker[k]) { hit = false; break; }
            if (hit) { at = i; break; }
        }
        if (at < 0)
        {
            Console.Error.WriteLine($"该 clip 的 ClipUsages 里没有 '{marker}' 用法 —— 本命令只会改【已存在】的位移用法，"
                                  + "新增得在 ModKit 里加（或另写插入路径）");
            return 1;
        }
        var usage0 = targetClip.ClipUsages.OfType<AnimationClip.DisplacementUsage>().FirstOrDefault();
        if (usage0 == null)
        {
            Console.Error.WriteLine($"字节流里有 '{marker}' 但对象模型里没有 —— 两边对不上，拒绝改");
            return 1;
        }
        // 布局（实测文件字节）：[4B 串长 = 12][12B "displacement"][4B UnknownUInt][16B vec4(x,y,z,w=1.0)][4B endProgress]
        //   🔴 `at` 指向的是**串内容起点**（不是长度前缀）→ 只需 skip 串本身 + 一个 u32
        int pVec = at + marker.Length + 4;
        // ④ 自校验：就地读出的三个 float 必须与对象模型解析出来的一致，否则说明偏移算错了
        float rx = BitConverter.ToSingle(raw, pVec);
        float ry = BitConverter.ToSingle(raw, pVec + 4);
        float rz = BitConverter.ToSingle(raw, pVec + 8);
        float rend = BitConverter.ToSingle(raw, pVec + 16);
        if (Math.Abs(rx - usage0.DisplacementVector.X) > 1e-5 ||
            Math.Abs(ry - usage0.DisplacementVector.Y) > 1e-5 ||
            Math.Abs(rz - usage0.DisplacementVector.Z) > 1e-5 ||
            Math.Abs(rend - usage0.DisplacementEndProgress) > 1e-5)
        {
            Console.Error.WriteLine($"偏移自校验失败：字节流里读到 ({rx}, {ry}, {rz}) end={rend}，"
                                  + $"对象模型却是 ({usage0.DisplacementVector.X}, {usage0.DisplacementVector.Y}, "
                                  + $"{usage0.DisplacementVector.Z}) end={usage0.DisplacementEndProgress} —— 拒绝改");
            return 1;
        }
        Console.WriteLine($"  改前: vector=({rx}, {ry}, {rz}) endProgress={rend}  @ 元数据偏移 {pVec}");

        // ⑤ 就地替换那 16 个字节
        BitConverter.GetBytes(v[0]).CopyTo(raw, pVec);
        BitConverter.GetBytes(v[1]).CopyTo(raw, pVec + 4);
        BitConverter.GetBytes(v[2]).CopyTo(raw, pVec + 8);
        if (!float.IsNaN(endP)) BitConverter.GetBytes(endP).CopyTo(raw, pVec + 16);
        // 同步对象模型，便于回读比对时两口径一致（Save 走 RawMeta，改不改它都不影响写盘）
        usage0.DisplacementVector = new Vector3(v[0], v[1], v[2]);
        if (!float.IsNaN(endP)) usage0.DisplacementEndProgress = endP;

        // ⑥ 存到独立目录（绝不原地覆盖）
        var outRoot = outDir ?? ".";
        Directory.CreateDirectory(outRoot);
        var outPath = Path.Combine(outRoot, Path.GetFileName(target));
        pkg.Save(outPath);
        Console.WriteLine($"  已写出: {outPath} ({new FileInfo(outPath).Length} bytes, 原 {new FileInfo(target).Length} bytes)");

        // ⑥ 回读验证：包级 + clip 级两道
        var back = new AssetPackage(outPath, true, false);
        bool sameGuid = back.Guid.Equals(pkg.Guid);
        bool sameCount = back.Items.Count == pkg.Items.Count;
        AnimationClip backClip = null;
        foreach (var it in back.Items)
            if (it.Type.Equals(AnimationClip.TYPE_GUID) && it.Name.Equals(filter, StringComparison.OrdinalIgnoreCase))
                backClip = (AnimationClip) it;
        Console.WriteLine($"  回读: items {back.Items.Count}/{pkg.Items.Count} · 包 guid 一致={sameGuid}");
        if (backClip == null)
        {
            Console.Error.WriteLine("  ❌ 回读找不到该 clip —— 产物不可用");
            return 1;
        }
        var bd = backClip.ClipUsages.OfType<AnimationClip.DisplacementUsage>().FirstOrDefault();
        Console.WriteLine($"  改后: Duration={backClip.Duration} Source={backClip.Source1}/{backClip.Source2} "
                        + $"flags=[{string.Join(", ", backClip.Flags ?? new List<string>())}]");
        Console.WriteLine($"        vector={(bd == null ? "(无 displacement)" : $"({bd.DisplacementVector.X}, {bd.DisplacementVector.Y}, {bd.DisplacementVector.Z}) endProgress={bd.DisplacementEndProgress}")}");
        bool ok = sameGuid && sameCount && bd != null
                  && Math.Abs(bd.DisplacementVector.X - v[0]) < 1e-4
                  && Math.Abs(bd.DisplacementVector.Y - v[1]) < 1e-4
                  && Math.Abs(bd.DisplacementVector.Z - v[2]) < 1e-4;
        Console.WriteLine(ok ? "  ✅ 写入生效且包结构完整" : "  ❌ 校验不过 —— 别用这个产物");
        return ok ? 0 : 1;
    }
    case "clipduration":
    {
        // 批量修 AnimationClip 的 Duration（秒）—— 只改元数据里那 4 个字节，数据段一个不碰。
        //
        // 用法：clipduration --packdir <目录> --filter <名字子串> --duration auto|<秒> [--out <目录>]
        //
        // 🔴 为什么要它：ModKit 的 Duration 输入框**只有 2 位小数**，而骑砍动画是 30fps ——
        //    31 帧的真实时长是 1.0333 秒，四舍五入成 1.03 就**仍然偏短**。
        //    正确做法是**向上取整**（1.04 / 2.04 / 3.04），`auto` 就是这个口径：
        //        auto = ceil( (Source2 − Source1 + 1) / 30 × 100 ) / 100
        //    并且**只升不降**（已经比 auto 长的保持原样）。
        //
        // 🔴 Duration 在元数据里的位置 = 文件头【偏移 4】的一个 float32
        //    （布局：u32 version → float Duration → float Source1 → float Source2 …，
        //     见 TpacTool.Lib/AnimationClip/AnimationClip.cs 的 ReadMetadata）。
        //    与 clipset 同一条路：**就地改这 4 个字节，其余字节一个不碰** ——
        //    绝不走"改对象再序列化"（库的 WriteMetadata 会把 metadata version 从 6 降成 5、
        //    还会把 vec4 第 4 分量写成 0，实测 15 字节非预期改动）。
        if (dir == null || filter == null || durArg == null)
        {
            Console.Error.WriteLine("clipduration requires --packdir <dir> --filter <substr> --duration auto|<seconds> [--out dir]");
            return 1;
        }
        float forced = float.NaN;
        bool auto = string.Equals(durArg.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
        if (!auto && !float.TryParse(durArg.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out forced))
        {
            Console.Error.WriteLine("--duration 只认 'auto' 或一个秒数: " + durArg);
            return 1;
        }

        // ① 只读头定位含目标 clip 的那个 .tpac
        string target = null;
        var scanDirs = (dir ?? ".").Split(',').Select(d => d.Trim()).Where(d => d.Length > 0).ToArray();
        foreach (var pd in scanDirs)
        {
            if (!Directory.Exists(pd)) continue;
            foreach (var f in Directory.EnumerateFiles(pd, "*.tpac", SearchOption.AllDirectories))
            {
                if (new AssetPackage(f, true, false).Items.Any(i =>
                        i.Type.Equals(AnimationClip.TYPE_GUID) &&
                        i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                {
                    target = f;
                    break;
                }
            }
            if (target != null) break;
        }
        if (target == null)
        {
            Console.Error.WriteLine($"没有哪个 .tpac 里含名字匹配 '{filter}' 的 AnimationClip");
            return 1;
        }
        Console.WriteLine($"目标包: {target}");

        // ② 只读头（故意不读数据段，理由同 clipset）
        var pkg = new AssetPackage(target, true, false);
        var hits = pkg.Items.OfType<AnimationClip>()
                      .Where(c => c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                      .OrderBy(c => c.Name).ToList();
        if (hits.Count == 0)
        {
            Console.Error.WriteLine("二遍扫描没找到（加载模式不一致？）");
            return 1;
        }

        int changed = 0;
        foreach (var c in hits)
        {
            var raw = c.RawMeta;
            if (raw == null || raw.Length < 8)
            {
                Console.Error.WriteLine($"  {c.Name}: 没有 RawMeta，跳过");
                continue;
            }
            float onDisk = BitConverter.ToSingle(raw, 4);
            if (Math.Abs(onDisk - c.Duration) > 1e-5)
            {
                Console.Error.WriteLine($"  {c.Name}: 偏移自校验失败（字节流 {onDisk} vs 对象 {c.Duration}）—— 跳过");
                continue;
            }

            int frames = (int)Math.Round(c.Source2 - c.Source1) + 1;
            double want = auto ? Math.Ceiling(frames / 30.0 * 100.0 - 1e-9) / 100.0 : forced;

            // 只升不降：已经够长的保持原样（例：闪避 1.87 > 1.8667，本来就是对的）
            if (want <= c.Duration + 1e-6)
            {
                Console.WriteLine($"  {c.Name,-32} {c.Duration:F4} → 不动（目标 {want:F4} 不更大；帧 {frames}）");
                continue;
            }
            BitConverter.GetBytes((float)want).CopyTo(raw, 4);
            c.Duration = (float)want;
            Console.WriteLine($"  {c.Name,-32} {onDisk:F4} → {want:F4}   (帧 {frames}，真实 {frames / 30.0:F4})");
            changed++;
        }
        if (changed == 0)
        {
            Console.WriteLine("没有任何 clip 需要改 —— 不写出。");
            return 0;
        }

        // ③ 写到独立目录（绝不原地覆盖）
        var outRoot = outDir ?? ".";
        Directory.CreateDirectory(outRoot);
        var outPath = Path.Combine(outRoot, Path.GetFileName(target));
        pkg.Save(outPath);
        Console.WriteLine($"已写出: {outPath} ({new FileInfo(outPath).Length} bytes, 原 {new FileInfo(target).Length} bytes)");

        // ④ 回读验证
        var back = new AssetPackage(outPath, true, false);
        bool sameGuid = back.Guid.Equals(pkg.Guid);
        bool sameCount = back.Items.Count == pkg.Items.Count;
        bool allOk = sameGuid && sameCount;
        foreach (var c in hits)
        {
            AnimationClip bc = null;
            foreach (var it in back.Items)
                if (it.Type.Equals(AnimationClip.TYPE_GUID) && it.Name.Equals(c.Name, StringComparison.Ordinal))
                    bc = (AnimationClip) it;
            if (bc == null || Math.Abs(bc.Duration - c.Duration) > 1e-5)
            {
                Console.Error.WriteLine($"  ❌ 回读 {c.Name}: {(bc == null ? "缺失" : bc.Duration.ToString())} ≠ {c.Duration}");
                allOk = false;
            }
        }
        Console.WriteLine($"回读: items {back.Items.Count}/{pkg.Items.Count} · 包 guid 一致={sameGuid} · 改动 {changed} 条");
        Console.WriteLine(allOk ? "✅ 写入生效且包结构完整" : "❌ 校验不过 —— 别用这个产物");
        return allOk ? 0 : 1;
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
        // --filter 支持逗号分隔多值(OR): "beards_c,rock_1" = 名字含任一
        var filters = filter.Split(',').Select(f => f.Trim()).Where(f => f.Length > 0).ToList();
        var items = assets
            .Where(a => filters.Any(f => a.Name.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(a => a.Name)
            .ToList();
        Console.WriteLine($"dump: {items.Count} matches");
        foreach (var item in items)
        {
            if (outDir == null) outDir = "./export_" + filter;
            if (mapsonly && item is not Metamesh) continue;
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
                    if (mapsonly)
                    {
                        // 仅写 mesh→材质→贴图 映射(不改动已有 meshes//materials/ 产物)
                        var mapDir = Path.Combine(outDir, "mesh_maps", SubDirOf(item));
                        Directory.CreateDirectory(mapDir);
                        var mapPath = Path.Combine(mapDir, SafeName(meta.Name) + ".mat_map.json");
                        WriteMatMap(mapPath, meta, assets);
                        Console.WriteLine($"OK  matmap {item.Name} -> {mapPath}");
                        continue;
                    }
                    var path = Path.Combine(targetDir, SafeName(meta.Name));
                    // mesh→材质→贴图 全链映射（formats 无关，obj/fbx 模式都写）
                    WriteMatMap(path + ".mat_map.json", meta, assets);
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

// mesh→材质→贴图 全链映射 JSON：submesh(Mesh.Material/SecondMaterial GUID→材质名) → 材质 Textures 槽位(k→纹理名)
static void WriteMatMap(string path, Metamesh meta, IReadOnlyList<AssetItem> assets)
{
    string TexName(Guid g) => assets.OfType<Texture>().FirstOrDefault(t => t.Guid == g)?.Name
        ?? "missing_" + g.ToString("N").Substring(0, 8);
    var submeshes = new System.Collections.Generic.List<object>();
    foreach (var m in meta.Meshes)
    {
        var mainMat = m.Material;                // AssetDependence<Material>
        var secMat = m.SecondMaterial;
        var mainObj = mainMat == null || mainMat.IsEmpty() ? null
            : assets.OfType<Material>().FirstOrDefault(x => x.Guid == mainMat.Guid);
        var secObj = secMat == null || secMat.IsEmpty() ? null
            : assets.OfType<Material>().FirstOrDefault(x => x.Guid == secMat.Guid);
        var slots = new System.Collections.Generic.SortedDictionary<int, string>();
        if (mainObj != null)
            foreach (var kv in mainObj.Textures)
                slots[kv.Key] = TexName(kv.Value.Guid);
        submeshes.Add(new
        {
            name = m.Name,
            lod = m.Lod,
            material = mainObj?.Name,
            second_material = secObj?.Name,
            textures = slots,
        });
    }
    var doc = new
    {
        mesh = meta.Name,
        guid = meta.Guid.ToString(),
        // Metamesh.Material 可能指向非 Material 资产(如 billboard 纹理, 引擎注释 "billboard texture will ref the same guid"), 故查任意类型
        default_material = assets.FirstOrDefault(a => a.Guid == meta.Material)?.Name,
        default_material_type = assets.FirstOrDefault(a => a.Guid == meta.Material)?.GetType().Name,
        submeshes,
    };
    var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(path, json);
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
