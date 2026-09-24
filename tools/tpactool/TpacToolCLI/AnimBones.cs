using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using TpacTool.Lib;

namespace TpacCli
{
    /// <summary>
    /// animbones —— 离线量「这条动画动了哪些骨」（2026-09-24 立）。
    ///
    /// 为什么需要它：引擎**没有按骨遮罩的接口**（`SetActionChannel` 参数里没有骨骼/权重项，
    /// 官方 flag 全表里也没有"只动上身"这种 flag）⇒ 想让一条动画"只动上半身"，
    /// 只能挑**轨道里本来就没写腿**的动画。这个命令用来筛这种动画，
    /// 免得拿实机一条条试（每条都要用户重启进战斗）。
    ///
    /// 判据口径：对每根骨，取它**相对区间首帧**的最大旋转角与最大位移
    /// （骨骼里存的是含静止姿势的局部变换，跟首帧比才等于"这段动画动了它多少"）。
    /// 骨骼名可读（`pelvis` / `l_thigh` / `l_calf` / `l_foot` / `spine*` / `*_upperarm*` / `head`），
    /// 所以"动没动腿"直接按名字算。
    ///
    /// 用法：
    ///   tpaccli animbones --packdir &lt;dir&gt; --filter &lt;clip名&gt;     单条详表（每根骨一行）
    ///   tpaccli animbones --packdir &lt;dir&gt; --all                 全量一行一条（筛"没动腿"的用这个）
    /// </summary>
    public static class AnimBones
    {
        private const float Fps = 30f;        // clip 的 Source1/Source2 是帧号
        private const float MoveRotDeg = 3f;  // "这根骨动了"的旋转阈值
        private const float MovePosCm = 0.5f;

        private static readonly string[] Legs =
        {
            "l_thigh", "l_calf", "l_foot", "l_toe0", "r_thigh", "r_calf", "r_foot", "r_toe0"
        };

        public static int Run(IReadOnlyList<AssetItem> assets, Dictionary<Guid, AssetItem> byGuid,
                              string filter, bool all)
        {
            bool batch = all || string.IsNullOrEmpty(filter);
            // 单条详表模式：filter 命中多个时只做第一条（避免刷屏）
            var clips = assets.OfType<AnimationClip>()
                .Where(c => string.IsNullOrEmpty(filter)
                            || c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                            || (c.Animation != Guid.Empty && byGuid.TryGetValue(c.Animation, out var a)
                                && a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(c => c.Name)
                .ToList();
            if (clips.Count == 0)
            {
                Console.WriteLine("no animation clip matched");
                return 1;
            }

            var skelCache = new Dictionary<Guid, List<BoneNode>>();
            int unresolved = 0;
            foreach (var c in clips)
            {
                SkeletalAnimation sa = null;
                if (c.Animation != Guid.Empty && byGuid.TryGetValue(c.Animation, out var item))
                    sa = item as SkeletalAnimation;
                var animData = sa?.Definition?.Data;
                if (animData == null)
                {
                    if (!batch) Console.WriteLine($"== {c.Name} ==\n  (SkeletalAnimation unresolved)");
                    unresolved++;
                    continue;
                }

                List<BoneNode> bones = null;
                if (sa.Skeleton != Guid.Empty)
                {
                    if (!skelCache.TryGetValue(sa.Skeleton, out bones))
                    {
                        bones = byGuid.TryGetValue(sa.Skeleton, out var sk) ? (sk as Skeleton)?.Definition?.Data?.Bones : null;
                        skelCache[sa.Skeleton] = bones;
                    }
                }
                var names = ResolveNames(animData, bones);

                // clip 区间：🔴 Source1/Source2 **就是动画自己的关键帧刻度**（实测校准：原始区间抓到 79 个关键帧
                // = 每帧一个、抬手 167°；若按"帧号/30 秒"解释只抓到 2 帧、幅度 1° = 错的）。
                float f0 = Math.Min(c.Source1, c.Source2), f1 = Math.Max(c.Source1, c.Source2);
                if (f1 - f0 < 0.5f) { f0 = float.MinValue; f1 = float.MaxValue; }

                var full = Measure(animData, float.MinValue, float.MaxValue);
                var win = Measure(animData, f0, f1);

                var legIdx = names.Select((n, i) => (n, i)).Where(x => Legs.Contains(x.n)).Select(x => x.i).ToList();
                float legWin = legIdx.Count == 0 ? 0f : legIdx.Max(i => win[i].rotDeg);
                float legFull = legIdx.Count == 0 ? 0f : legIdx.Max(i => full[i].rotDeg);

                if (batch)
                {
                    Console.WriteLine($"{c.Name,-44} anim={sa.Name,-42} skel={(byGuid.TryGetValue(sa.Skeleton, out var sk0) ? sk0.Name : "?")},"
                                    + $"legsN={legIdx.Count},bones={names.Count} "
                                    + $"legsWin={legWin,6:0.0}deg legsFull={legFull,6:0.0}deg "
                                    + $"maxWin={win.Max(s => s.rotDeg),6:0.0}deg keys={win.KeyCount,4}/{full.KeyCount,4}");
                    continue;
                }

                Console.WriteLine($"== {c.Name} ==");
                Console.WriteLine($"  anim        = {sa.Name}   (bones={names.Count}, BoneNum={sa.BoneNum})");
                Console.WriteLine($"  skel        = {(byGuid.TryGetValue(sa.Skeleton, out var skelItem) ? skelItem.Name : "(?)")}   (bones={bones?.Count ?? -1})");
                Console.WriteLine($"  anim keys   = {full.MinKey:0.000}..{full.MaxKey:0.000}  (keys/bone max={full.KeyCount})");
                Console.WriteLine($"  clip range  = source {f0:0}..{f1:0} (Duration={c.Duration}s)   [keys caught={win.KeyCount}]");
                Console.WriteLine($"  idx name                  parent                rest(x,y,z)                rotFull  rotWin  posWinCm");
                for (int i = 0; i < names.Count; i++)
                {
                    var b = (bones != null && i < bones.Count) ? bones[i] : null;
                    string rest = b == null ? "?" : $"{b.RestFrame.M41,7:0.000},{b.RestFrame.M42,7:0.000},{b.RestFrame.M43,7:0.000}";
                    Console.WriteLine($"  {i,3} {names[i],-21} {(b?.Parent?.Name ?? "-"),-21} {rest,-26} {full[i].rotDeg,7:0.0} {win[i].rotDeg,7:0.0} {win[i].posCm,8:0.0}");
                }
                Console.WriteLine($"  LEGS: win={legWin:0.0}deg  full={legFull:0.0}deg   (legs moved in window = {legWin >= MoveRotDeg})");
            }
            if (unresolved > 0) Console.WriteLine($"({unresolved} clip(s) unresolved)");
            return 0;
        }

        private static List<string> ResolveNames(AnimationDefinitionData anim, List<BoneNode> bones)
        {
            var names = new List<string>(anim.BoneAnims.Count);
            for (int i = 0; i < anim.BoneAnims.Count; i++)
                names.Add(bones != null && i < bones.Count ? bones[i].Name : $"bone_{i}");
            return names;
        }

        private struct BoneStat { public float rotDeg; public float posCm; }

        private sealed class Stats : List<BoneStat>
        {
            public float MinKey = float.MaxValue;
            public float MaxKey = float.MinValue;
            public int KeyCount;
        }

        private static Stats Measure(AnimationDefinitionData anim, float t0, float t1)
        {
            var res = new Stats();
            foreach (var bone in anim.BoneAnims)
            {
                var rotFrames = bone.RotationFrames.Where(f => f.Key >= t0 && f.Key <= t1).ToList();
                var posFrames = bone.PositionFrames.Where(f => f.Key >= t0 && f.Key <= t1).ToList();

                float rotMax = 0f, posMax = 0f;
                if (rotFrames.Count > 0)
                {
                    var refQ = rotFrames[0].Value.Value;
                    foreach (var f in rotFrames)
                        rotMax = Math.Max(rotMax, AngleDeg(refQ, f.Value.Value));
                }
                if (posFrames.Count > 0)
                {
                    var refP = posFrames[0].Value.Value;
                    foreach (var f in posFrames)
                    {
                        var d = f.Value.Value - refP;
                        posMax = Math.Max(posMax, new Vector3(d.X, d.Y, d.Z).Length() * 100f);
                    }
                }

                foreach (var f in rotFrames) { res.MinKey = Math.Min(res.MinKey, f.Key); res.MaxKey = Math.Max(res.MaxKey, f.Key); }
                foreach (var f in posFrames) { res.MinKey = Math.Min(res.MinKey, f.Key); res.MaxKey = Math.Max(res.MaxKey, f.Key); }

                res.KeyCount = Math.Max(res.KeyCount, rotFrames.Count);
                res.Add(new BoneStat { rotDeg = rotMax, posCm = posMax });
            }
            if (res.MinKey > res.MaxKey) { res.MinKey = 0f; res.MaxKey = 0f; }
            return res;
        }

        /// <summary>两个四元数之间的夹角（度）。四元数双覆盖：q 与 −q 是同一姿态。</summary>
        private static float AngleDeg(Quaternion a, Quaternion b)
        {
            float dot = Math.Abs(a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W);
            dot = Math.Min(1f, Math.Max(-1f, dot));
            return (float)(2.0 * Math.Acos(dot) * 180.0 / Math.PI);
        }
    }
}
