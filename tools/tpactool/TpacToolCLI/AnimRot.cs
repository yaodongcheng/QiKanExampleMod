using System;
using System.Collections.Generic;
using System.Linq;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// animrot —— 打某条动画**某一帧的骨骼四元数**（默认第 1 帧 · 全部骨）—— 2026-10-10 立。
    ///
    /// 🔴 **为什么要有它**：判"人朝哪边"的读数**必须落在交付包上**（项目纪律：空间判断只认
    ///    `tpaccli` / 装机包，FBX / Blender 侧会被换轴）。而 `clipinfo` / `animlist` 只打元数据，
    ///    打不到骨骼轨道。有了本命令，`Debug/offline/_trf_face_check.py` 那套"减掉静止姿势看偏航"
    ///    的算法就能**直接跑在生成物上**（配 `bannerlord_skel.json` 当静止姿势）。
    ///
    /// 输出每行：`<动画名>\t<骨序>\t<帧>\tqx\tqy\tqz\tqw`
    /// </summary>
    public static class AnimRot
    {
        public static int Run(IEnumerable<AssetItem> assets, Dictionary<Guid, AssetItem> byGuid,
                              string filter, int frameIndex, int boneIndex, bool allBones)
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
            foreach (var sa in anims)
            {
                AnimationDefinitionData data;
                try { data = sa.Definition?.Data; }
                catch (Exception e) { Console.Error.WriteLine($"  {sa.Name}: 读动画数据失败 {e.Message}"); continue; }
                if (data == null) { Console.Error.WriteLine($"  {sa.Name}: 没有动画数据段"); continue; }

                var bones = data.BoneAnims;
                if (bones == null || bones.Count == 0) { Console.Error.WriteLine($"  {sa.Name}: 骨轨道为空"); continue; }
                int from = allBones ? 0 : Math.Max(0, boneIndex);
                int to = allBones ? bones.Count - 1 : from;
                for (int b = from; b <= to && b < bones.Count; b++)
                {
                    var frames = bones[b]?.RotationFrames;
                    if (frames == null || frames.Count == 0) continue;
                    int k = frameIndex <= 0 ? frames.Count + frameIndex : frameIndex - 1;
                    if (k < 0) k = 0;
                    if (k >= frames.Count) k = frames.Count - 1;
                    var q = frames.Values[k].Value;
                    Console.WriteLine($"{sa.Name}\t{b}\t{k + 1}\t{q.X:0.######}\t{q.Y:0.######}\t{q.Z:0.######}\t{q.W:0.######}");
                }
            }
            return 0;
        }
    }
}
