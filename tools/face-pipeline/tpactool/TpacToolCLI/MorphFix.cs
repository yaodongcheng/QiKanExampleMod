using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using TpacTool.IO;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// morphinfo / morphfix —— 人类头部网格的 morph 通道诊断与补齐。
    ///
    /// 背景：皮肤 skins.xml 的 deform_keys 按 key_time_point 索引网格上的 morph 帧；
    /// 原生 head_female_a / xxFemale 头都是 101 帧（0..100，见 MorphNameMapping._headMapping：
    /// 0=Basis、1..59=脸形键、60..63=眼球方向、64..100=表情）。若网格帧数不足或 Time 对不上，
    /// 引擎在 face_generator 里撞到未测路径 → wEditor 版断言 "non-tested code execution!"，
    /// release 版继续跑读到非法内存 → AccessViolation。
    ///
    /// 🔴 关键：ExternalLoader.SaveTo 默认把**原始字节**原样写回，就地改 MorphFrames 不生效——
    ///    必须像 texreplace 那样新建 ExternalLoader 顶替原数据段。
    ///
    /// 用法：
    ///   tpaccli morphinfo --packdir &lt;dir&gt; --filter &lt;mesh名子串&gt;
    ///   tpaccli morphfix  --packdir &lt;dir&gt; --filter &lt;mesh名子串&gt; --out &lt;dir&gt; [--target 101] [--clearmat]
    /// </summary>
    public static class MorphFix
    {
        public static int Info(string dir, string filter)
        {
            var pkg = LoadPackages(dir, filter, out var metas);
            if (pkg == null) return 1;
            Dump(metas);
            return 0;
        }

        /// <summary>
        /// morphmap —— 逐帧位移「落在哪个部位」的诊断。
        ///
        /// morphinfo 的「逐帧位移签名」只打前 8 帧、且只有位移包围盒，看不出**动的是哪一块**。
        /// 本命令打**每一帧**：移动点数 + 最大位移 + **移动点质心（绝对坐标）**，
        /// 并带上 MorphNameMapping 的帧名 —— 一眼可判「拉鼻子动的却是后脑勺」这类错位。
        ///
        /// 判读基准（human 头，原点在脚底、+Y 朝前、Z 朝上）：
        ///   鼻尖 ≈ y +0.09 z 1.70   后脑 ≈ y −0.06 z 1.75   下巴 ≈ y +0.06 z 1.47
        /// 帧名 ↔ 皮肤 deform_keys 的 key_time_point 一一对应（MorphNameMapping._headMapping）。
        ///
        /// 用法：tpaccli morphmap --packdir &lt;dir&gt; --filter &lt;mesh名子串&gt; [--minmm 0.05]
        /// </summary>
        public static int MorphMap(string dir, string filter, float minMm, int maxFrames)
        {
            var pkg = LoadPackages(dir, filter, out var metas);
            if (pkg == null) return 1;
            float minM = minMm / 1000f;
            foreach (var meta in metas)
            {
                Console.WriteLine($"== {meta.Name}  子网格 {meta.Meshes.Count} ==");
                foreach (var mesh in meta.Meshes)
                {
                    var data = mesh.EditData?.Data;
                    if (data == null) { Console.WriteLine($"   {mesh.Name,-32} (无 EditData)"); continue; }
                    if (mesh.Lod != 0) continue;
                    int n = data.Positions.Length;
                    float bx0 = float.MaxValue, bx1 = float.MinValue, by0 = float.MaxValue,
                          by1 = float.MinValue, bz0 = float.MaxValue, bz1 = float.MinValue;
                    for (int k = 0; k < n; k++)
                    {
                        var p = data.Positions[k];
                        bx0 = Math.Min(bx0, p.X); bx1 = Math.Max(bx1, p.X);
                        by0 = Math.Min(by0, p.Y); by1 = Math.Max(by1, p.Y);
                        bz0 = Math.Min(bz0, p.Z); bz1 = Math.Max(bz1, p.Z);
                    }
                    Console.WriteLine($"   {mesh.Name,-32} 帧={data.MorphFrames.Count,4} 基础顶点={n,6}  "
                                    + $"bbox x[{bx0:0.000},{bx1:0.000}] y[{by0:0.000},{by1:0.000}] z[{bz0:0.000},{bz1:0.000}]");
                    if (data.MorphFrames.Count == 0 || n == 0) continue;
                    Console.WriteLine($"      {"帧",-5}{"帧名",-24}{"移动点",-9}{"最大位移mm",-12}质心(x,y,z)  ← 绝对坐标");
                    int lim = maxFrames > 0 ? Math.Min(maxFrames, data.MorphFrames.Count) : data.MorphFrames.Count;
                    for (int f = 0; f < lim; f++)
                    {
                        var fr = data.MorphFrames[f];
                        int m = Math.Min(fr.Positions.Length, n);
                        if (m == 0) { Console.WriteLine($"      f{f,-4}{"(空帧)"}"); continue; }
                        double sx = 0, sy = 0, sz = 0; int cnt = 0; float mx = 0f;
                        int mxK = -1;
                        for (int k = 0; k < m; k++)
                        {
                            var d = fr.Positions[k] - data.Positions[k];
                            float mag = MathF.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
                            if (mag > mx) { mx = mag; mxK = k; }
                            if (mag >= minM)
                            {
                                cnt++;
                                sx += data.Positions[k].X; sy += data.Positions[k].Y; sz += data.Positions[k].Z;
                            }
                        }
                        if (cnt == 0)
                        {
                            Console.WriteLine($"      f{f,-4}{MorphNameMapping.GetHumanHeadMorphName(f),-24}"
                                            + $"{"0",-9}{mx * 1000,-12:0.##}(未形变)");
                            continue;
                        }
                        var c = mxK >= 0 ? data.Positions[mxK] : default;
                        Console.WriteLine($"      f{f,-4}{MorphNameMapping.GetHumanHeadMorphName(f),-24}"
                                        + $"{cnt,-9}{mx * 1000,-12:0.##}"
                                        + $"({sx / cnt:0.000},{sy / cnt:0.000},{sz / cnt:0.000})"
                                        + $"   位移最大点=({c.X:0.000},{c.Y:0.000},{c.Z:0.000})");
                    }
                    Console.WriteLine();
                }
            }
            return 0;
        }

        public static int Fix(string dir, string filter, string outDir, int target, bool clearMat)
        {
            var pkg = LoadPackages(dir, filter, out var metas);
            if (pkg == null) return 1;

            Console.WriteLine("---- BEFORE ----");
            Dump(metas);

            int touched = 0;
            foreach (var meta in metas)
            {
                if (clearMat && !meta.Material.Equals(Guid.Empty))
                {
                    var oldName = meta.Material.ToString();
                    Console.WriteLine($"  [clearmat] {meta.Name}: default_material {oldName} -> empty");
                    meta.Material = Guid.Empty;
                    meta.RawMeta = null;   // Save 走 RawMeta ?? WriteMetadata()，不清 = 改了个寂寞
                }

                foreach (var mesh in meta.Meshes)
                {
                    if (mesh.EditData == null) continue;
                    if (mesh.Lod != 0)
                    {
                        Console.WriteLine($"  [skip] {mesh.Name}: Lod={mesh.Lod}（LOD 层不配 morph，原生同款）");
                        continue;
                    }
                    var data = mesh.EditData.Data;

                    // 帧数组长度必须与网格一致：Positions 按 PositionIndex 索引，Normals 按顶点索引
                    int posLen = data.Positions.Length, nrmLen = data.Vertices.Length;
                    if (data.MorphFrames.Count > 0)
                    {
                        var s = data.MorphFrames[0];
                        if (s.Positions.Length != posLen || s.Normals.Length != nrmLen)
                        {
                            Console.WriteLine($"  [warn] {mesh.Name}: 已有帧长度 {s.Positions.Length}/{s.Normals.Length} != 网格 {posLen}/{nrmLen}，沿用已有帧长度");
                            posLen = s.Positions.Length;
                            nrmLen = s.Normals.Length;
                        }
                    }

                    // Time 是 float32 位模式（0,1,2... 存成 0x00000000/0x3F800000/...），
                    // 直接写 int 会得到近似 0 的非规格化浮点 —— 必须做位转换。
                    var byIdx = new Dictionary<int, MeshEditData.VertexFrame>();
                    foreach (var f in data.MorphFrames)
                    {
                        int idx = (int)Math.Round(BitConverter.Int32BitsToSingle(f.Time));
                        if (!byIdx.ContainsKey(idx)) byIdx[idx] = f;
                    }
                    int need = Math.Max(target, byIdx.Count == 0 ? 0 : byIdx.Keys.Max() + 1);

                    // 🔴🔴 VertexFrame.Positions 是【绝对位置】，不是位移增量！
                    //   铁证（morphinfo 的 [MORPH] 诊断行）：正常包里帧 0（Basis，语义上"不形变"）的
                    //   三个采样点 (0.032,0.11,1.595)/(0.022,0.141,1.605)/(0.031,0.111,1.595) 与基础网格
                    //   Positions 逐点吻合（差 0.0002）；xxFemale 每一帧也都是头部坐标量级。
                    //   ⇒ 「不形变」的正确填法 = **复制基础网格的位置**。
                    //   ❌ 早期版本填 new Vector4[n]（全 0）= 绝对位置 (0,0,0) = 把整块网格拉向原点。
                    //      实机症状 = 「面部持续下坠」：脸壳参与 morph 混合所以掉，眼球不吃权重所以不掉。
                    var fillPos = new Vector4[posLen];
                    for (int i = 0; i < posLen && i < data.Positions.Length; i++) fillPos[i] = data.Positions[i];
                    var fillNrm = new Vector4[nrmLen];
                    for (int i = 0; i < nrmLen && i < data.Vertices.Length; i++) fillNrm[i] = data.Vertices[i].Normal;
                    bool IsBroken(MeshEditData.VertexFrame f) =>
                        f.Positions.Length > 0 && f.Positions.All(p => p.X == 0f && p.Y == 0f && p.Z == 0f);

                    var rebuilt = new List<MeshEditData.VertexFrame>(need);
                    int added = 0, repaired = 0;
                    for (int t = 0; t < need; t++)
                    {
                        if (byIdx.TryGetValue(t, out var keep))
                        {
                            // 修掉早期补丁留下的"整帧全零"坏帧（幂等：修好后重跑不再命中）
                            if (IsBroken(keep))
                            {
                                rebuilt.Add(new MeshEditData.VertexFrame
                                {
                                    Time = BitConverter.SingleToInt32Bits((float)t),
                                    Positions = (Vector4[])fillPos.Clone(),
                                    Normals = (Vector4[])fillNrm.Clone(),
                                });
                                repaired++;
                                continue;
                            }
                            rebuilt.Add(keep);
                            continue;
                        }
                        rebuilt.Add(new MeshEditData.VertexFrame
                        {
                            Time = BitConverter.SingleToInt32Bits((float)t),
                            Positions = (Vector4[])fillPos.Clone(),
                            Normals = (Vector4[])fillNrm.Clone(),
                        });
                        added++;
                    }
                    bool keyOk = mesh.VertexKeyCount == need;
                    if (added == 0 && repaired == 0 && keyOk)
                    {
                        Console.WriteLine($"  [ok]   {mesh.Name}: {data.MorphFrames.Count} 帧 + VertexKeyCount={mesh.VertexKeyCount}，一致，跳过");
                        continue;
                    }

                    if (added > 0 || repaired > 0)
                    {
                        data.MorphFrames.Clear();
                        data.MorphFrames.AddRange(rebuilt);

                        // 🔴 必须换 loader：原 loader 走的是"原始字节直写"路径，就地改不会被写回
                        var newLoader = new ExternalLoader<MeshEditData>(data) { OwnerGuid = mesh.Guid };
                        ReplaceSegment(meta, mesh.EditData, newLoader);
                        mesh.EditData = newLoader;
                    }

                    // 🔴 VertexKeyCount = 引擎分配 morph 缓冲的键数。不同步它 = 声明 59 却装 101 帧 /
                    //    皮肤索引到 63 → 缓冲越界 → native AccessViolation（本项目实机踩到）。
                    int oldKey = mesh.VertexKeyCount;
                    mesh.VertexKeyCount = need;
                    meta.RawMeta = null;   // 该字段在 metamess 元数据里，不清 RawMeta 写不进去

                    touched++;
                    Console.WriteLine($"  [fix]  {mesh.Name}: {rebuilt.Count - added} -> {rebuilt.Count} 帧（补 {added}，修坏帧 {repaired}），VertexKeyCount {oldKey} -> {need}");
                }
            }

            if (touched == 0)
            {
                Console.WriteLine("没有需要修改的子网格，不输出文件。");
                return 0;
            }

            string outPath = Path.Combine(outDir ?? ".", pkg.File.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            pkg.Save(outPath);
            Console.WriteLine($"saved {outPath} ({new FileInfo(outPath).Length:N0} bytes)");

            // 回读自检
            var back = new AssetPackage(outPath, true, false);
            var backMeta = back.Items.OfType<Metamesh>()
                .FirstOrDefault(m => m.Name.Equals(metas[0].Name, StringComparison.OrdinalIgnoreCase));
            if (backMeta != null)
            {
                Console.WriteLine("---- AFTER (回读) ----");
                Dump(new List<Metamesh> { backMeta });
            }
            return 0;
        }

        /// <summary>
        /// skinfix —— 给缺蒙皮数据的人类头部网格补骨绑定。
        ///
        /// 背景：原版头部（head_female_a）与能跑的 xxFemaleHead，每个 Lod0 子网格的顶点流都带
        /// BoneIndices/BoneWeights（非零），且 Mesh.SkinDataSize = Positions 数（&gt;0）。引擎靠
        /// SkinDataSize &gt; 0 判定"这网格带蒙皮"——我们的网格因为源 FBX 删了骨架，两者全是 0，
        /// 引擎按无蒙皮网格去绑 human_skeleton → AddSkinMeshes 里 native 越界(AccessViolation)。
        /// 参照系：原版/xxFemale 的脸部附件全部刚性绑在骨骼 13（头骨），主壳跨 11~13。
        /// 本命令统一刚性绑到指定骨骼（默认 13），零风险达到"能绑上"。
        /// </summary>
        public static int SkinFix(string dir, string filter, string outDir, int bone, bool fullMat, bool force)
        {
            var pkg = LoadPackages(dir, filter, out var metas);
            if (pkg == null) return 1;

            var matByGuid = new Dictionary<Guid, Material>();
            foreach (var m in pkg.Items.OfType<Material>()) matByGuid[m.Guid] = m;

            var usedMats = new List<Material>();
            var captured = new Dictionary<string, VertexStreamData>();
            int touched = 0;

            foreach (var meta in metas)
            {
                bool metaDirty = false;
                foreach (var mesh in meta.Meshes)
                {
                    // 收集本子网格引用的材质（材质是独立资产，不在 metamess 内）
                    foreach (var g in new[] { mesh.Material?.Guid ?? Guid.Empty, mesh.SecondMaterial?.Guid ?? Guid.Empty })
                        if (g != Guid.Empty && matByGuid.TryGetValue(g, out var mm) && !usedMats.Contains(mm))
                            usedMats.Add(mm);

                    if (mesh.Lod != 0) { Console.WriteLine($"  [skip] {mesh.Name}: Lod={mesh.Lod}"); continue; }

                    var old = mesh.VertexStream;
                    if (old == null) { Console.WriteLine($"  [skip] {mesh.Name}: 无 VertexStream 段"); continue; }
                    var vs = old.Data;
                    if (vs?.BoneIndices == null || vs.BoneWeights == null || vs.BoneIndices.Length == 0)
                    {
                        Console.WriteLine($"  [skip] {mesh.Name}: 无骨骼数组");
                        continue;
                    }

                    bool hasBones = vs.BoneIndices.Any(b => b.B1 != 0 || b.B2 != 0 || b.B3 != 0 || b.B4 != 0);
                    if (hasBones && mesh.SkinDataSize > 0 && !force)
                    {
                        Console.WriteLine($"  [ok]   {mesh.Name}: 已有蒙皮数据（SkinDataSize={mesh.SkinDataSize}），跳过");
                        continue;
                    }

                    for (int i = 0; i < vs.BoneIndices.Length; i++)
                    {
                        vs.BoneIndices[i] = new VertexStreamData.BoneIndex { B1 = (byte)bone };
                        vs.BoneWeights[i] = new VertexStreamData.BoneWeight { W1 = 255 };
                    }
                    int posCount = mesh.EditData?.Data?.Positions.Length ?? mesh.PositionCount;
                    mesh.SkinDataSize = posCount;

                    // 🔴 必须换 loader：原 loader 走"原始字节直写"，就地改不生效
                    var nl = new ExternalLoader<VertexStreamData>(vs) { OwnerGuid = mesh.Guid };
                    foreach (var kv in old.UserData) nl.UserData[kv.Key] = kv.Value;
                    ReplaceSegment(meta, old, nl);
                    mesh.VertexStream = nl;

                    metaDirty = true;
                    touched++;
                    captured[mesh.Name] = vs;
                    Console.WriteLine($"  [fix]  {mesh.Name}: 刚性绑骨骼 {bone} × {vs.BoneIndices.Length} 顶点, SkinDataSize 0 -> {posCount}");
                }

                // ---- 网格元数据对账：MaterialFlags(脸部角色标记) + UnknownInt2(用到骨骼数) ----
                foreach (var mesh in meta.Meshes)
                {
                    if (mesh.Lod != 0) continue;
                    var vs2 = mesh.VertexStream?.Data;
                    bool dirty = false;

                    // UnknownInt2：只在为 0 时兜底。
                    // 🔴 编辑器导入带骨架的 FBX 时会自己算出正确值（实测 = 28，与 xxFemale 一致）；
                    //    此时**绝不能覆盖**——按"B1~B4 去重骨骼数"推出来的 2 是错的（早期补丁踩过）。
                    if (mesh.UnknownInt2 == 0 && vs2?.BoneIndices != null && vs2.BoneIndices.Length > 0)
                    {
                        var set = new SortedSet<int>();
                        foreach (var b in vs2.BoneIndices) { set.Add(b.B1); set.Add(b.B2); set.Add(b.B3); set.Add(b.B4); }
                        Console.WriteLine($"  [meta] {mesh.Name}: UnknownInt2 0 -> {set.Count}（兜底值；编辑器未算时才用）");
                        mesh.UnknownInt2 = set.Count;
                        dirty = true;
                    }
                    else if (mesh.UnknownInt2 != 0)
                    {
                        Console.WriteLine($"  [meta] {mesh.Name}: UnknownInt2={mesh.UnknownInt2}（编辑器已算，保留）");
                    }

                    // MaterialFlags：脸部生成器靠 face_base/mouth/eye/eyelash_mesh 认「哪格是脸/嘴/眼/睫」。
                    // 原生 + xxFemale 的 Lod0 脸部件都带其一、LOD 层为空；我们全空 → 生成器找不到脸 → 解空指针。
                    if (mesh.MaterialFlags.Count == 0)
                    {
                        var matName = (matByGuid.TryGetValue(mesh.Material?.Guid ?? Guid.Empty, out var mm2) ? mm2.Name : "") ?? "";
                        var flag = FaceFlagFor(matName);
                        if (flag != null)
                        {
                            mesh.MaterialFlags.Add(flag);
                            Console.WriteLine($"  [meta] {mesh.Name}: MaterialFlags [] -> [{flag}]（材质 {matName}）");
                            dirty = true;
                        }
                        else
                        {
                            Console.WriteLine($"  [meta] {mesh.Name}: 材质 {matName} 在原版无对应角色标记，保持空");
                        }
                    }

                    if (dirty) { meta.RawMeta = null; touched++; }
                }
                if (metaDirty) meta.RawMeta = null;   // SkinDataSize 在 metamess 元数据里，不清 RawMeta 写不进去
            }

            foreach (var mat in usedMats)
            {
                bool ch = false;
                if (!mat.VertexLayoutFlags.Contains("skinning")) { mat.VertexLayoutFlags.Add("skinning"); ch = true; }
                if (!mat.VertexLayoutFlags.Contains("doubleuv")) { mat.VertexLayoutFlags.Add("doubleuv"); ch = true; }
                if (fullMat)
                {
                    ApplyNativeRecipe(mat, MatRole(mat.Name));
                    ch = true;
                }
                if (ch)
                {
                    mat.RawMeta = null;
                    touched++;
                    Console.WriteLine($"  [mat]  {mat.Name}: layout=[{string.Join(",", mat.VertexLayoutFlags)}] shader={mat.Shader?.Guid}");
                }
            }

            if (touched == 0)
            {
                Console.WriteLine("没有需要修改的对象，不输出文件。");
                return 0;
            }

            string outPath = Path.Combine(outDir ?? ".", pkg.File.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            pkg.Save(outPath);
            Console.WriteLine($"saved {outPath} ({new FileInfo(outPath).Length:N0} bytes)");

            // ---- 回读比对：验证顶点流写入无损（TpacTool 原版写入端有计数前缀 bug，改后必须自证） ----
            var back = new AssetPackage(outPath, true, false);
            var backMeta = back.Items.OfType<Metamesh>()
                .FirstOrDefault(m => m.Name.Equals(metas[0].Name, StringComparison.OrdinalIgnoreCase));
            if (backMeta != null)
            {
                Console.WriteLine("---- 回读比对（应为全 MATCH） ----");
                foreach (var bm in backMeta.Meshes)
                {
                    if (!captured.TryGetValue(bm.Name, out var before)) continue;
                    var after = bm.VertexStream?.Data;
                    if (after == null) { Console.WriteLine($"   {bm.Name,-22} DIFF: 回读无 VertexStream"); continue; }
                    Console.WriteLine($"   {bm.Name,-22} {CompareStream(before, after)}");
                }
                Console.WriteLine("---- AFTER ----");
                Dump(new List<Metamesh> { backMeta });
            }
            return 0;
        }

        /// <summary>按材质名判角色：face / mouth / eye / lash（影、眉归 lash）。</summary>
        static string MatRole(string matName)
        {
            var n = (matName ?? "").ToLowerInvariant();
            if (n.Contains("mouth")) return "mouth";
            if (n.Contains("lash")) return "lash";
            if (n.Contains("brow") || n.Contains("shadow")) return "lash";  // 原版无对位，按透明贴片处理
            if (n.Contains("eye")) return "eye";
            return "face";
        }

        /// <summary>
        /// 把材质逐字段抄成它的原生对位配方（原版头部四个角色材质各自不同，实测数据见下）。
        /// 脸 head_female_a : layout[bumpmap,skinning,doubleuv] shader 8c88213c  shaderFlags[use_specular,use_detailnormalmap] blend factor tex 0/1/2/3/4
        /// 嘴 mouth_mat     : layout[bumpmap,skinning]          shader cea0b671  shaderFlags[alpha_test,use_specular] alphaTest 0.039 tex 0/2/4
        /// 眼 eye_mat       : layout[bumpmap,skinning,skinning_precise] shader 71740d5e shaderFlags[use_specular] tex 0/1/2/3/4
        /// 睫 eyelashes_mat : layout[bumpmap,skinning]          shader a578e7cc  shaderFlags[alpha_test] alphaTest 0.5882 tex 0
        /// 纹理槽：我们只有 0/2/4 三张，需要 1/3 的角色（脸/眼）把 0→1、2→3 复制过去；不需要的角色（嘴/睫）保持空缺。
        /// </summary>
        static void ApplyNativeRecipe(Material mat, string role)
        {
            Guid S(string g) => Guid.Parse(g);
            mat.Flags.Clear();
            mat.VertexLayoutFlags.Clear();
            mat.ShaderMaterialFlags.Clear();
            mat.BlendMode = "factor";

            switch (role)
            {
                case "mouth":
                    mat.Flags.Add("dont_cast_shadow");
                    mat.Flags.Add("avoid_recomputation_of_normals");
                    mat.VertexLayoutFlags.Add("bumpmap");
                    mat.VertexLayoutFlags.Add("skinning");
                    mat.Shader = new AssetDependence<Shader>(S("cea0b671-384f-4211-a1a6-48a29a083c8f"));
                    mat.ShaderMaterialFlags.Add("alpha_test");
                    mat.ShaderMaterialFlags.Add("use_specular");
                    mat.AlphaTest = 0.039f;
                    DropSlots(mat, 1, 3);
                    break;

                case "eye":
                    mat.Flags.Add("dont_cast_shadow");
                    mat.VertexLayoutFlags.Add("bumpmap");
                    mat.VertexLayoutFlags.Add("skinning");
                    mat.VertexLayoutFlags.Add("skinning_precise");
                    mat.Shader = new AssetDependence<Shader>(S("71740d5e-4c77-4262-a206-fc040d2cbdcb"));
                    mat.ShaderMaterialFlags.Add("use_specular");
                    mat.AlphaTest = 0f;
                    DupSlots(mat, 0, 1);
                    DupSlots(mat, 2, 3);
                    break;

                case "lash":
                    mat.Flags.Add("two_sided");
                    mat.Flags.Add("dont_cast_shadow");
                    mat.Flags.Add("disable_streaming");
                    mat.Flags.Add("multi_pass_alpha");
                    mat.VertexLayoutFlags.Add("bumpmap");
                    mat.VertexLayoutFlags.Add("skinning");
                    mat.Shader = new AssetDependence<Shader>(S("a578e7cc-6f80-48a1-9df3-56e7bfa419b4"));
                    mat.ShaderMaterialFlags.Add("alpha_test");
                    mat.AlphaTest = 0.5882353f;
                    DropSlots(mat, 1, 3);
                    break;

                default: // face
                    mat.VertexLayoutFlags.Add("bumpmap");
                    mat.VertexLayoutFlags.Add("skinning");
                    mat.VertexLayoutFlags.Add("doubleuv");
                    mat.Shader = new AssetDependence<Shader>(S("8c88213c-ee55-491f-8523-1ebd5439bea6"));
                    mat.ShaderMaterialFlags.Add("use_specular");
                    mat.ShaderMaterialFlags.Add("use_detailnormalmap");
                    mat.AlphaTest = 0f;
                    DupSlots(mat, 0, 1);
                    DupSlots(mat, 2, 3);
                    break;
            }
            mat.RawMeta = null;
        }

        static void DupSlots(Material mat, int from, int to)
        {
            if (mat.Textures.TryGetValue(from, out var src))
                mat.Textures[to] = new AssetDependence<Texture>(src.Guid);
            else
                mat.Textures.Remove(to);
        }

        static void DropSlots(Material mat, params int[] slots)
        {
            foreach (var s in slots) mat.Textures.Remove(s);
        }

        /// <summary>
        /// 按材质名推断该子网格的脸部角色标记。
        /// 原版词表只有 4 个值（face_base_mesh / face_mouth_mesh / face_eye_mesh / face_eyelash_mesh），
        /// 且只标在 Lod0 的脸部件上；眼球/嘴/睫这类附件在原版头里各自对应一格。
        /// 蒂法的"眼影"(shadow)/"眉毛"(brow) 在原版头里没有对应子网格（原版眉毛走 eyebrow_meshes 独立件），
        /// 因此返回 null = 保持空（原生 LOD 层也是空，空标记本身合法）。
        /// </summary>
        static string FaceFlagFor(string matName)
        {
            var n = (matName ?? "").ToLowerInvariant();
            if (n.Contains("mouth")) return "face_mouth_mesh";
            if (n.Contains("lash")) return "face_eyelash_mesh";
            if (n.Contains("brow")) return null;      // 含 eyebrow；必须在 eye 之前判
            if (n.Contains("shadow")) return null;
            if (n.Contains("eye")) return "face_eye_mesh";
            return "face_base_mesh";
        }

        /// <summary>逐数组比对顶点流（长度 + 抽样元素），返回 MATCH / 差异描述。</summary>
        static string CompareStream(VertexStreamData a, VertexStreamData b)
        {
            var bad = new List<string>();
            void Chk<T>(string name, T[] x, T[] y) where T : struct
            {
                if (x == null && y == null) return;
                if (x == null || y == null) { bad.Add($"{name}(null)"); return; }
                if (x.Length != y.Length) { bad.Add($"{name}(len {x.Length}!={y.Length})"); return; }
                if (!x.AsSpan().SequenceEqual(y.AsSpan())) bad.Add($"{name}(内容不同)");
            }
            Chk("Uv1", a.Uv1, b.Uv1);
            Chk("Uv2", a.Uv2, b.Uv2);
            Chk("Pos", a.Positions, b.Positions);
            Chk("Nrm", a.Normals, b.Normals);
            Chk("BoneW", a.BoneWeights, b.BoneWeights);
            Chk("BoneI", a.BoneIndices, b.BoneIndices);
            return bad.Count == 0 ? "MATCH" : "DIFF: " + string.Join(", ", bad);
        }

        /// <summary>把 TypelessDataSegments 里的 old 段原位替换为 repl（保持段顺序）。</summary>
        static void ReplaceSegment(AssetItem item, AbstractExternalLoader old, AbstractExternalLoader repl)
        {
            var segs = item.TypelessDataSegments;
            for (int i = 0; i < segs.Count; i++)
            {
                if (ReferenceEquals(segs[i], old) || (segs[i].OwnerGuid == old.OwnerGuid && segs[i].TypeGuid == old.TypeGuid))
                {
                    segs[i] = repl;
                    return;
                }
            }
            segs.Add(repl);
        }

        internal static AssetPackage LoadPackages(string dir, string filter, out List<Metamesh> metas)
        {
            metas = new List<Metamesh>();
            if (dir == null || filter == null)
            {
                Console.Error.WriteLine("morphfix/morphinfo requires --packdir <dir> and --filter <mesh名子串>");
                return null;
            }
            var mgr = new AssetManager();
            mgr.Load(new DirectoryInfo(dir));
            // 有网格的那个包才是有用的包（一个目录下可能有多个 tpac）
            AssetPackage hit = null;
            int best = -1;
            foreach (var p in mgr.LoadedPackages)
            {
                var ms = p.Items.OfType<Metamesh>()
                    .Where(m => m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                if (ms.Count > best && ms.Count > 0) { best = ms.Count; hit = p; metas = ms; }
            }
            if (hit == null)
            {
                Console.Error.WriteLine($"没有包包含名字含 \"{filter}\" 的 Metamesh（已扫 {mgr.LoadedPackages.Count} 个包）");
                return null;
            }
            Console.WriteLine($"pack = {hit.File?.Name}  匹配 Metamesh {metas.Count} 个");
            return hit;
        }

        static void Dump(List<Metamesh> metas)
        {
            foreach (var meta in metas)
            {
                Console.WriteLine($"== {meta.Name}  子网格 {meta.Meshes.Count}  default_material={meta.Material} ==");
                foreach (var mesh in meta.Meshes)
                {
                    if (mesh.EditData == null)
                    {
                        Console.WriteLine($"   {mesh.Name,-34} (无 EditData)");
                        continue;
                    }
                    var data = mesh.EditData.Data;
                    var times = data.MorphFrames.Select(f => f.Time).ToList();
                    Console.WriteLine($"   {mesh.Name,-34} 帧 {data.MorphFrames.Count,4}  verts={data.Vertices.Length} pos={data.Positions.Length}");
                    Console.WriteLine($"        mesh记录: VertexKeyCount={mesh.VertexKeyCount}  PositionCount={mesh.PositionCount}  "
                                    + $"VertexCount={mesh.VertexCount}  FaceCount={mesh.FaceCount}  SkinDataSize={mesh.SkinDataSize}  Lod={mesh.Lod}");
                    // 顶点流：Uv2 = 脸部合成五官用的第二套 UV；BoneWeights/Indices = 蒙皮数据
                    var vs = mesh.VertexStream?.Data;
                    if (vs != null)
                    {
                        int uv2nz = vs.Uv2 == null ? -1
                            : vs.Uv2.Count(uv => Math.Abs(uv.X) > 1e-4f || Math.Abs(uv.Y) > 1e-4f);
                        Console.WriteLine($"        stream: Uv1={vs.Uv1?.Length ?? -1}  Uv2={vs.Uv2?.Length ?? -1}(非零 {uv2nz})  "
                                        + $"BoneW={vs.BoneWeights?.Length ?? -1}  BoneI={vs.BoneIndices?.Length ?? -1}  "
                                        + $"SkinDataSize={mesh.SkinDataSize}  Lod={mesh.Lod}");
                        if (vs.BoneIndices != null && vs.BoneIndices.Length > 0)
                        {
                            int nzIdx = vs.BoneIndices.Count(b => b.B1 != 0 || b.B2 != 0 || b.B3 != 0 || b.B4 != 0);
                            int nzW = vs.BoneWeights == null ? -1
                                : vs.BoneWeights.Count(w => w.W1 != 0 || w.W2 != 0 || w.W3 != 0 || w.W4 != 0);
                            var b1 = vs.BoneIndices.Select(b => (int)b.B1).Distinct().OrderBy(x => x).ToList();
                            var allB = new SortedSet<int>();
                            foreach (var b in vs.BoneIndices) { allB.Add(b.B1); allB.Add(b.B2); allB.Add(b.B3); allB.Add(b.B4); }
                            Console.WriteLine($"        骨骼: 索引非零 {nzIdx}/{vs.BoneIndices.Length}  权重非零 {nzW}/{vs.BoneWeights?.Length ?? -1}  "
                                            + $"B1 去重 {b1.Count} 个 范围 {b1.First()}..{b1.Last()}  |  B1~B4 去重 {allB.Count} 个 [{string.Join(",", allB.Take(12))}{((allB.Count > 12) ? "..." : "")}]  UnknownInt2={mesh.UnknownInt2}");
                            // 次级槽非零 = 顶点被分摊到第二/三/四根骨上 → 头不会刚性跟随头骨（会漂移/被拉到别处）
                            if (vs.BoneWeights != null && vs.BoneWeights.Length == vs.BoneIndices.Length)
                            {
                                var sb = new System.Text.StringBuilder();
                                for (int i = 0; i < Math.Min(5, vs.BoneIndices.Length); i++)
                                {
                                    var bi = vs.BoneIndices[i];
                                    var bw = vs.BoneWeights[i];
                                    sb.Append($"[{bi.B1},{bi.B2},{bi.B3},{bi.B4} | {bw.W1},{bw.W2},{bw.W3},{bw.W4}] ");
                                }
                                Console.WriteLine($"        前5顶点 B1..B4 | W1..W4: {sb}");
                                int w234 = vs.BoneWeights.Count(w => w.W2 != 0 || w.W3 != 0 || w.W4 != 0);
                                int b234 = vs.BoneIndices.Count(b => b.B2 != 0 || b.B3 != 0 || b.B4 != 0);
                                Console.WriteLine($"        🔴 次级槽非零: 权重 W2~W4 = {w234}/{vs.BoneWeights.Length}   索引 B2~B4 = {b234}/{vs.BoneIndices.Length}"
                                                + (w234 > 0 ? "   <== 顶点被分到多根骨，头不会刚性跟随" : "   (刚性单骨，正常)"));
                            }
                        }
                    }
                    else Console.WriteLine($"        stream: (无 VertexStream)  Lod={mesh.Lod}");
                    if (times.Count == 0) continue;
                    // Time 字段实际是 float 位模式（xxFemale 实测最大 = 101.0f = 1120403456）
                    var asFloat = times.Select(BitConverter.Int32BitsToSingle).ToList();
                    Console.WriteLine($"        Time(int)  首12: {string.Join(",", times.Take(12))}");
                    Console.WriteLine($"        Time(float)首12: {string.Join(",", asFloat.Take(12).Select(v => v.ToString("0.###")))}");
                    Console.WriteLine($"        Time(float)末4 : {string.Join(",", asFloat.Skip(Math.Max(0, asFloat.Count - 4)).Select(v => v.ToString("0.###")))}");
                    var uniq = asFloat.Distinct().OrderBy(v => v).ToList();
                    Console.WriteLine($"        去重 {uniq.Count} 个，范围 {uniq.First():0.###} .. {uniq.Last():0.###}");
                    bool integral = uniq.All(v => Math.Abs(v - Math.Round(v)) < 1e-4 && v >= 0 && v <= 200);
                    if (integral)
                    {
                        var idx = uniq.Select(v => (int)Math.Round(v)).ToList();
                        var missing = Enumerable.Range(0, 101).Where(t => !idx.Contains(t)).ToList();
                        Console.WriteLine($"        整数索引缺(0..100): {(missing.Count == 0 ? "无" : string.Join(",", missing))}");
                    }
                    // 采样帧幅：全零 = 该通道不形变
                    // 「未形变帧」= 位置与基础网格一致（不是"全零"——全零是坏帧）
                    var zeroFrames = Enumerable.Range(0, data.MorphFrames.Count)
                        .Where(i => data.MorphFrames[i].Positions.Length > 0 && data.MorphFrames[i].Positions
                            .Select((pp, k) => k < data.Positions.Length
                                ? Math.Abs(pp.X - data.Positions[k].X) + Math.Abs(pp.Y - data.Positions[k].Y) + Math.Abs(pp.Z - data.Positions[k].Z) : 0f)
                            .DefaultIfEmpty(0f).Max() < 1e-4f)
                        .ToList();
                    var brokenFrames = Enumerable.Range(0, data.MorphFrames.Count)
                        .Where(i => data.MorphFrames[i].Positions.Length > 0 && data.MorphFrames[i].Positions.All(pp => pp.X == 0f && pp.Y == 0f && pp.Z == 0f))
                        .ToList();
                    Console.WriteLine($"        未形变帧: {(zeroFrames.Count == 0 ? "无" : zeroFrames.Count + " 个")}   🔴坏帧(全零): {(brokenFrames.Count == 0 ? "无" : brokenFrames.Count + " 个 ← 必须跑 morphfix 修掉")}");
                    // 🔴 逐帧位移签名：帧 i 的（位置 − 基础网格）包围盒，单位 mm。
                    //    用途：与 FBX 里各 KeyTime_N 的几何包围盒对号（`fbx_probe.py` 能打出来），
                    //    判断"帧索引 ↔ 通道编号"有没有错位一格——皮肤的 deform_keys 用 key_time_point=N
                    //    取帧，若帧 0 装的是 KeyTime_1，则 N=1 会取到 KeyTime_2，整体错一格。
                    {
                        int nsig = Math.Min(8, data.MorphFrames.Count);
                        var sig = new System.Text.StringBuilder();
                        for (int i = 0; i < nsig; i++)
                        {
                            var fr = data.MorphFrames[i];
                            int m = Math.Min(fr.Positions.Length, data.Positions.Length);
                            if (m == 0) { sig.Append($"f{i}:空 "); continue; }
                            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue,
                                  y1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                            for (int k = 0; k < m; k++)
                            {
                                var d = fr.Positions[k] - data.Positions[k];
                                x0 = Math.Min(x0, d.X); x1 = Math.Max(x1, d.X);
                                y0 = Math.Min(y0, d.Y); y1 = Math.Max(y1, d.Y);
                                z0 = Math.Min(z0, d.Z); z1 = Math.Max(z1, d.Z);
                            }
                            sig.Append($"f{i}[x{x0 * 1000:0.#},{x1 * 1000:0.#} y{y0 * 1000:0.#},{y1 * 1000:0.#} z{z0 * 1000:0.#},{z1 * 1000:0.#}] ");
                        }
                        Console.WriteLine($"        [MORPH] 逐帧位移签名(前{nsig}帧, mm): {sig}");
                    }
                    // 🔴 诊断：VertexFrame.Positions 是【绝对位置】还是【位移】——决定补帧该填什么。
                    //    若帧 0（Basis，语义上应"不形变"）的数值 ≈ 基础网格 Positions，则为绝对位置，
                    //    补帧必须填基础位置；填 0 会让该帧把整块网格拉向原点（实机表现为"面部下坠"）。
                    if (data.MorphFrames.Count > 0 && data.Positions.Length > 0)
                    {
                        var f0 = data.MorphFrames[0];
                        var fLast = data.MorphFrames[data.MorphFrames.Count - 1];
                        string V(Vector4 v) => $"({v.X:0.###},{v.Y:0.###},{v.Z:0.###})";
                        Console.WriteLine($"        [MORPH] 帧0 前3位置   : {V(f0.Positions[0])} {V(f0.Positions[1])} {V(f0.Positions[2])}");
                        Console.WriteLine($"        [MORPH] 基础网格前3位置: {V(data.Positions[0])} {V(data.Positions[1])} {V(data.Positions[2])}");
                        Console.WriteLine($"        [MORPH] 末帧 前3位置  : {V(fLast.Positions[0])} {V(fLast.Positions[1])} {V(fLast.Positions[2])}");
                        float d0 = Math.Abs(f0.Positions[0].X - data.Positions[0].X) + Math.Abs(f0.Positions[0].Y - data.Positions[0].Y) + Math.Abs(f0.Positions[0].Z - data.Positions[0].Z);
                        Console.WriteLine($"        [MORPH] 帧0 与基础网格差 = {d0:0.####}  → {(d0 < 0.01 ? "【绝对位置】补帧必须填基础位置" : "【位移】补帧应填 0")}");
                    }
                }
            }
        }
    }
}
