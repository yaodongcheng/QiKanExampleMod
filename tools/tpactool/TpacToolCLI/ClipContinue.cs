using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// clipcontinue —— 离线改 `AnimationClip` 的 **ContinueWithAction** 字段（2026-10-14 立）。
    ///
    /// 为什么要它（实机破案，见 `plans/钩索-实施计划.md` §14.8）：
    ///   引擎每帧给"当前这条动画片段"找**后续动作**时会读这个字段。字段是**空串** ⇒ 运行时存成 **-1**；
    ///   而引擎的查表函数**不挡 -1**，直接算 `表 + (-1)*8` = 读到表前面那一格的内存（堆布局运气），
    ///   再拿这坨垃圾当索引去查全局片段表 ⇒ **偶发 AccessViolation**（崩在引擎 `Agent_anim_system` 里，与我们的代码无关）。
    ///   原版弓的持械循环 `ready_continue_bow` 该字段 = 一个真实动作 ⇒ 引擎那条路走得通；
    ///   我们的 clip 全是空 ⇒ 每条都埋着这颗雷。**填一个真实动作名即可绕开。**
    ///
    /// 🔴 填什么：**必须是当前武器动作集里注册过的动作名**（如 `act_grapple_ground_hold` 填它自己）。
    ///   **别填 release** —— 那等于告诉引擎"循环走完接着甩出去"，玩家还按着键它就会往下播。
    ///
    /// 字段位置（`AnimationClip.ReadMetadata` 实读顺序，与 `acmeta_read.py` 同源）：
    ///   version(4) · Duration/Source1/Source2/Param1/Param2/Param3(6×f32) · Priority(i32) · Animation(guid16)
    ///   · StepPoints(vec4=16) · SoundCode(s) · VoiceCode(s) · FacialAnimationId(s) · BlendsWithAction(s)
    ///   ⇒ **ContinueWithAction(s)** ← 本命令改这一个（s = int32 长度 + UTF8 字节；空串 = 长度 0）
    ///
    /// 纪律（照抄 `ClipPrio`）：
    ///   · 只动**元数据原始字节**（`RawMeta`，Save 时优先直写 ⇒ 改长度也安全，库负责重排索引）；
    ///     绝不走"改对象再序列化"（库的 `WriteMetadata` 会硬写 version=5 / 把 vec4.w 写成 0，与真实文件不符）；
    ///   · 改前**两处自校验**（Priority 偏移口径 + 就地解出的字段值 vs 对象模型），不一致就拒绝改；
    ///   · 默认**写到独立目录**（`--inplace` 才原地覆盖，覆盖前自动留 `.bak-<时间戳>`）；
    ///   · 写出后**回读验证**（结构 + 字段值 + 新字节能重新解出同一个值）。
    ///
    /// 用法：
    ///   tpaccli clipcontinue --packdir &lt;目录&gt; --filter &lt;clip 名子串&gt; --action &lt;动作名&gt; [--out &lt;目录&gt;] [--inplace]
    /// </summary>
    public static class ClipContinue
    {
        /// <summary>Priority 的字节偏移（与 `ClipPrio` 同一个常数：version(4) + 6×f32(24)）。</summary>
        private const int PriorityOffset = 28;

        public static int Run(string packDirs, string filter, string actionName, string fieldName, string outDir, bool inPlace, bool clear)
        {
            if (string.IsNullOrEmpty(filter))
            {
                Console.Error.WriteLine("clipcontinue requires --filter <clipNameSubstr> --action <actionName> [--out dir] [--inplace]");
                return 1;
            }
            if (clear)
                actionName = string.Empty;      // --clear = 把该字段清回空串（PowerShell 会把空串参数吞掉，所以用开关而不是传 ""）
            if (actionName == null)
            {
                Console.Error.WriteLine("clipcontinue requires --action <actionName> 或 --clear（清回空串）");
                return 1;
            }

            if (string.IsNullOrEmpty(fieldName))
                fieldName = "continue";
            if (!string.Equals(fieldName, "continue", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fieldName, "blends", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"--field 只认 continue | blends（给的是 '{fieldName}'）");
                return 1;
            }
            bool useBlends = string.Equals(fieldName, "blends", StringComparison.OrdinalIgnoreCase);

            var dirs = (packDirs ?? ".").Split(',').Select(d => d.Trim()).Where(d => d.Length > 0).ToArray();
            int hitPacks = 0, hitClips = 0, failed = 0;

            foreach (var pd in dirs)
            {
                if (!Directory.Exists(pd)) continue;
                foreach (var file in Directory.EnumerateFiles(pd, "*.tpac", SearchOption.AllDirectories))
                {
                    // ① 只读头，先看这个包里有没有命中的 clip（不读数据段）
                    List<AnimationClip> matches;
                    try
                    {
                        var scan = new AssetPackage(file, true, false);
                        matches = scan.Items.OfType<AnimationClip>()
                            .Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine($"跳过 {Path.GetFileName(file)}：读头失败 {e.Message}");
                        continue;
                    }
                    if (matches.Count == 0) continue;

                    hitPacks++;
                    var pkg = new AssetPackage(file, true, false);
                    var clips = pkg.Items.OfType<AnimationClip>()
                        .Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    Console.WriteLine($"== {file}  （{clips.Count} 条命中）");
                    foreach (var clip in clips)
                    {
                        var raw = clip.RawMeta;
                        if (raw == null || raw.Length < PriorityOffset + 4)
                        {
                            Console.Error.WriteLine($"  ❌ {clip.Name}: 没有 RawMeta，拒绝改");
                            failed++;
                            continue;
                        }
                        // ② 自校验 A：偏移口径（与 ClipPrio 同款）
                        if (BitConverter.ToInt32(raw, PriorityOffset) != clip.Priority)
                        {
                            Console.Error.WriteLine($"  ❌ {clip.Name}: Priority 偏移自校验失败 —— 拒绝改");
                            failed++;
                            continue;
                        }
                        // ③ 定位两段相邻的 sized string（BlendsWithAction / ContinueWithAction）
                        long bs, be, cs, ce;
                        string bVal, cVal;
                        if (!TryFindSpans(raw, out bs, out be, out bVal, out cs, out ce, out cVal))
                        {
                            Console.Error.WriteLine($"  ❌ {clip.Name}: 定位字段失败 —— 拒绝改");
                            failed++;
                            continue;
                        }
                        // ④ 自校验 B：就地解出的值必须与对象模型一致（库两个属性都核）
                        if (!string.Equals(bVal, clip.BlendsWithAction ?? string.Empty, StringComparison.Ordinal)
                            || !string.Equals(cVal, clip.ContinueWithAction ?? string.Empty, StringComparison.Ordinal))
                        {
                            Console.Error.WriteLine($"  ❌ {clip.Name}: 字段自校验失败（字节流 blends='{bVal}' continue='{cVal}' "
                                                  + $"vs 模型 blends='{clip.BlendsWithAction}' continue='{clip.ContinueWithAction}'）—— 拒绝改");
                            failed++;
                            continue;
                        }

                        // ⑤ 选字段 + 就地重拼：前缀 + 新 sized string + 后缀（长度可变）
                        long start = useBlends ? bs : cs;
                        long end = useBlends ? be : ce;
                        string parsed = useBlends ? bVal : cVal;
                        byte[] encoded = EncodeSizedString(actionName);
                        var newRaw = new byte[raw.Length - (int)(end - start) + encoded.Length];
                        Buffer.BlockCopy(raw, 0, newRaw, 0, (int)start);
                        Buffer.BlockCopy(encoded, 0, newRaw, (int)start, encoded.Length);
                        Buffer.BlockCopy(raw, (int)end, newRaw, (int)start + encoded.Length, raw.Length - (int)end);

                        clip.RawMeta = newRaw;
                        if (useBlends) clip.BlendsWithAction = actionName; else clip.ContinueWithAction = actionName;   // 让回读两口径一致
                        Console.WriteLine($"  {clip.Name,-34} [{clip.Name}] BlendsWithAction «{bVal}» · ContinueWithAction «{cVal}»");
                        Console.WriteLine($"  {"",-34}   → 写 {fieldName}: «{parsed}» → «{actionName}»   （元数据 {raw.Length} → {newRaw.Length} B）");
                        hitClips++;
                    }

                    // ⑥ 写出
                    string outPath;
                    if (inPlace)
                    {
                        string bak = file + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                        File.Copy(file, bak, overwrite: false);
                        pkg.Save(file);
                        outPath = file;
                        Console.WriteLine($"  已原地覆盖（备份 {Path.GetFileName(bak)}）");
                    }
                    else
                    {
                        var outRoot = outDir ?? ".";
                        Directory.CreateDirectory(outRoot);
                        outPath = Path.Combine(outRoot, Path.GetFileName(file));
                        pkg.Save(outPath);
                        Console.WriteLine($"  已写出: {outPath}  ({new FileInfo(outPath).Length} B, 原 {new FileInfo(file).Length} B)");
                    }

                    // ⑦ 回读验证
                    var back = new AssetPackage(outPath, true, false);
                    bool sameGuid = back.Guid.Equals(pkg.Guid);
                    bool sameCount = back.Items.Count == pkg.Items.Count;
                    int bad = 0;
                    var backClips = back.Items.OfType<AnimationClip>()
                        .Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var bc in backClips)
                    {
                        string got = useBlends ? bc.BlendsWithAction : bc.ContinueWithAction;
                        if (!string.Equals(got ?? string.Empty, actionName, StringComparison.Ordinal))
                        {
                            bad++;
                            Console.Error.WriteLine($"    ❌ 回读 {bc.Name} {fieldName}=«{got}»");
                            continue;
                        }
                        // 再解一遍原始字节，确认新长度也解析得通
                        long s2, e2, s3, e3;
                        string p2, p3;
                        if (!TryFindSpans(bc.RawMeta, out s2, out e2, out p2, out s3, out e3, out p3))
                        {
                            bad++;
                            Console.Error.WriteLine($"    ❌ 回读 {bc.Name}: 新字节解析失败");
                            continue;
                        }
                        string backVal = useBlends ? p2 : p3;
                        if (!string.Equals(backVal, actionName, StringComparison.Ordinal))
                        {
                            bad++;
                            Console.Error.WriteLine($"    ❌ 回读 {bc.Name}: 新字节解不出同一个值（解出 «{backVal}»）");
                        }
                    }
                    bool ok = sameGuid && sameCount && bad == 0 && backClips.Count == clips.Count;
                    Console.WriteLine($"  回读: items {back.Items.Count}/{pkg.Items.Count} · guid 一致={sameGuid} · "
                                    + $"字段={actionName} 的条数 {backClips.Count - bad}/{backClips.Count}");
                    Console.WriteLine(ok ? "  ✅ 写入生效且包结构完整" : "  ❌ 校验不过 —— 别用这个产物");
                    if (!ok) failed++;
                }
            }

            if (hitPacks == 0)
            {
                Console.Error.WriteLine($"没有哪个包里有名字含 '{filter}' 的 AnimationClip");
                return 1;
            }
            Console.WriteLine($"-- 命中包 {hitPacks} · 改动 clip {hitClips} 条 · 失败 {failed}");
            return failed == 0 ? 0 : 2;
        }

        /// <summary>
        /// 在原始元数据里定位 `ContinueWithAction` / `BlendsWithAction` 两段 sized string 的字节区间。
        /// 走的是**库里同一套读法**（字段顺序照 `AnimationClip.ReadMetadata`），返回 [start, end) 与就地解出的值。
        /// 🔴 **两个都返回**（2026-10-14 实机教训）：运行时那个"续接动作号"到底取自哪一个，必须先对着
        ///    **已知答案的样例**（如原版 `release_sling` → `act_release_sling_continue`）核对，别想当然。
        /// </summary>
        private static bool TryFindSpans(byte[] raw, out long blendsStart, out long blendsEnd, out string blendsValue,
                                                        out long contStart, out long contEnd, out string contValue)
        {
            blendsStart = blendsEnd = contStart = contEnd = 0;
            blendsValue = contValue = null;
            try
            {
                using (var ms = new MemoryStream(raw, false))
                using (var br = new BinaryReader(ms))
                {
                    br.ReadUInt32();                          // version
                    for (int i = 0; i < 6; i++) br.ReadSingle();   // Duration / Source1 / Source2 / Param1 / Param2 / Param3
                    br.ReadInt32();                           // Priority
                    br.ReadBytes(16);                         // Animation (guid)
                    for (int i = 0; i < 4; i++) br.ReadSingle();   // StepPoints (vec4)
                    ReadSizedString(br);                      // SoundCode
                    ReadSizedString(br);                      // VoiceCode
                    ReadSizedString(br);                      // FacialAnimationId
                    blendsStart = ms.Position;
                    blendsValue = ReadSizedString(br);        // ← BlendsWithAction
                    blendsEnd = ms.Position;
                    contStart = ms.Position;
                    contValue = ReadSizedString(br);          // ← ContinueWithAction
                    contEnd = ms.Position;
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 读一段 sized string —— **照库内部（`TpacTool.Lib/Util/Utils.cs` 的 `Utils.ReadSizedString`）逐字抄**：
        /// 那边是 `internal`，CLI 工程看不见，所以在这儿重写一份（口径必须一致：int32 长度 + UTF8，长度 0 = 空串）。
        /// </summary>
        private static string ReadSizedString(BinaryReader br)
        {
            int length = br.ReadInt32();
            if (length == 0)
                return string.Empty;
            var bytes = br.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>写一段 sized string（同库口径；空串/null = 只写一个 int32 0）。</summary>
        private static void WriteSizedString(BinaryWriter bw, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                bw.Write(0);
                return;
            }
            var bytes = Encoding.UTF8.GetBytes(value);
            bw.Write(bytes.Length);
            bw.Write(bytes);
        }

        /// <summary>按库同款口径编码一段 sized string（int32 长度 + UTF8；空串 = 只写长度 0）。</summary>
        private static byte[] EncodeSizedString(string value)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                WriteSizedString(bw, value);
                bw.Flush();
                return ms.ToArray();
            }
        }
    }
}
