using System;
using System.Collections.Generic;
using System.Linq;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// animpos —— 打某条动画的**根骨位移轨**（默认全帧）—— 2026-10-10 立。
    ///
    /// 🔴 **为什么要有它**：`animrot` 只能打**旋转**，而"被扛者站在扛人者哪一侧"这件事
    ///    **只写在位移轨上**（换边 = 把根骨位移的 X 取反，旋转逐位不动 —— 见
    ///    `tools/anim-retarget/pipeline/common/trf_edit.py` 的 `flipside`）。
    ///    判据必须落在交付包上（项目纪律：空间判断只认 `tpaccli` / 装机包，不认 FBX/Blender 侧）。
    ///
    /// 输出每行：`<动画名>\t<root|bone#>\t<帧号>\tX\tY\tZ\tW`
    ///   缺省打**根骨那道专用位移轨**（`RootPositionFrames`）；
    ///   `--posbone N` 改成打第 N 根骨的位置轨（`BoneAnims[N].PositionFrames`）。
    /// 末尾每个动画额外来一行 `# <名> frames=N first=(x,y,z) last=(x,y,z)`（人看的摘要，脚本可忽略）。
    /// </summary>
    public static class AnimPos
    {
        public static int Run(IEnumerable<AssetItem> assets, Dictionary<Guid, AssetItem> byGuid,
                              string filter, int posBone)
        {
            var anims = assets.OfType<SkeletalAnimation>()
                .Where(a => filter == null || a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.Name)
                .ToList();
            if (anims.Count == 0)
            {
                Console.WriteLine("no skeletal animation matched");
                return 1;
            }

            int printed = 0;
            foreach (var sa in anims)
            {
                AnimationDefinitionData data;
                try { data = sa.Definition?.Data; }
                catch (Exception e) { Console.Error.WriteLine($"  {sa.Name}: 读动画数据失败 {e.Message}"); continue; }
                if (data == null) { Console.Error.WriteLine($"  {sa.Name}: 没有动画数据段"); continue; }

                string tag = posBone < 0 ? "root" : ("bone" + posBone);
                var rows = new List<(int idx, float t, System.Numerics.Vector4 v)>();
                if (posBone < 0)
                {
                    var frames = data.RootPositionFrames;
                    if (frames == null || frames.Count == 0)
                    {
                        Console.WriteLine($"# {sa.Name}: 根骨位移轨为空（这条动画没有位移）");
                        continue;
                    }
                    int k = 0;
                    foreach (var pair in frames)
                        rows.Add((++k, pair.Key, pair.Value.Value));
                }
                else
                {
                    var bones = data.BoneAnims;
                    if (bones == null || posBone >= bones.Count) { Console.Error.WriteLine($"  {sa.Name}: 骨 {posBone} 不存在"); continue; }
                    var frames = bones[posBone]?.PositionFrames;
                    if (frames == null || frames.Count == 0)
                    {
                        Console.WriteLine($"# {sa.Name}: 骨 {posBone} 没有位置轨");
                        continue;
                    }
                    int k = 0;
                    foreach (var pair in frames)
                    {
                        var v3 = pair.Value.Value;                 // 骨位置轨是 Vector3
                        rows.Add((++k, pair.Key, new System.Numerics.Vector4(v3.X, v3.Y, v3.Z, 0f)));
                    }
                }

                foreach (var r in rows)
                    Console.WriteLine($"{sa.Name}\t{tag}\t{r.idx}\t{r.v.X:0.######}\t{r.v.Y:0.######}\t{r.v.Z:0.######}\t{r.v.W:0.######}");
                var f = rows[0]; var l = rows[rows.Count - 1];
                Console.WriteLine($"# {sa.Name} {tag} frames={rows.Count} t=[{f.t:0.###},{l.t:0.###}] "
                                  + $"first=({f.v.X:0.####},{f.v.Y:0.####},{f.v.Z:0.####}) "
                                  + $"last=({l.v.X:0.####},{l.v.Y:0.####},{l.v.Z:0.####})");
                printed++;
            }
            return printed == 0 ? 1 : 0;
        }
    }
}
