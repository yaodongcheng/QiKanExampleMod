using System;
using System.Collections.Generic;
using LivingWorldNpcs.Animation;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.Flight
{
    /// <summary>
    /// **骨挂持续粒子的执行者**（2026-09-28 立；实现 <see cref="IAnimFxHost"/>）。
    ///
    /// 🔴 **这里只提供"能力"，不规定"内容"**（用户 2026-09-28 裁定）：
    ///    **挂哪个粒子、挂哪几根骨、什么时候挂** —— 全是数据，写在 `flight.xml` 的轨道上：
    ///    <code>
    ///    &lt;track particle="lwn_manual_fly_handtrail" bone="HandL+HandR" /&gt;              &lt;!-- 全程 --&gt;
    ///    &lt;track particle="lwn_manual_fly_bodytrail" bone="Abdomen" at="0.2" remove="0.7" /&gt;
    ///    </code>
    ///    本类只实现"怎么挂 / 怎么摘"，**一个粒子名都不认识**。
    ///
    /// 🔴🔴 **必须绕"宿主实体"这一圈**（2026-09-28 实机教训，别再走回头路）：
    ///    直接 `ParticleSystem.CreateParticleSystemAttachedToBone(id, skeleton, bone, …)` ——
    ///    拿**裸 Skeleton** 建粒子、没有宿主实体 ⇒ 那个粒子系统**从没进过场景的更新/渲染名单**：
    ///    日志一切正常（id 解析到、骨找到、返回非 null、无异常），**画面上什么都没有**。
    ///    正解 = 四步（照 `Knowledge/HikageRising_忍者体系技术实现分析.md` 里实机跑通的写法）：
    ///    ① `GameEntity.CreateEmpty(scene)` ② `CreateParticleSystemAttachedToEntity` 挂实体
    ///    ③ `AgentVisuals.AddChildEntity` ④ `Skeleton.AddComponentToBone`。
    ///
    /// 🔴 **"粒子摘不掉"是分 API 的**：只有 `Scene.CreateBurstParticle`（不返回句柄）摘不掉；
    ///    挂实体 / 挂骨**都返回 `ParticleSystem` 句柄**。摘除 = `SetEnable(false)` → 从骨上摘
    ///    → **销毁宿主实体**（保底杀招）。
    ///
    /// **验收命令**：`custom.flight trail` 列出当前挂着的粒子；`on <粒子名>` / `off <粒子名>` 单独开关。
    /// </summary>
    internal sealed class FlightBoneFx : IAnimFxHost
    {
        /// <summary>全局唯一实例（`FlightAnimMachine.Register` 里交给状态机当 <c>FxHost</c>）。</summary>
        public static readonly FlightBoneFx Instance = new FlightBoneFx();

        /// <summary>一个粒子 = 一组宿主实体 + 句柄（按**粒子名**记账）。</summary>
        private sealed class Entry
        {
            public string Particle;
            public string[] BoneNames;       // XML 里写的原名（日志用）
            public sbyte[] RealBone;         // 解析出来的真实骨索引
            public GameEntity[] Host;
            public ParticleSystem[] Ps;

            public int Alive
            {
                get
                {
                    int n = 0;
                    for (int i = 0; i < Ps.Length; i++)
                    {
                        if (Ps[i] != null || Host[i] != null) n++;
                    }
                    return n;
                }
            }
        }

        /// <summary>当前挂着的粒子（键 = 粒子名）。空 = 一个都没挂。</summary>
        private readonly Dictionary<string, Entry> _live = new Dictionary<string, Entry>(StringComparer.Ordinal);

        /// <summary>
        /// 挂上粒子时那个场景的指针 —— **防"上一个 Mission 的野句柄"**。
        ///
        /// 🔴 为什么必须有它：如果 Mission 结束得比"摘"更早（玩家阵亡 / 直接退场景），
        ///    引擎已经把那些粒子系统连同场景一起销毁了，而我们的 C# 字典里还捏着**悬空指针**；
        ///    下次再碰它们（`SetEnable` / `RemoveBoneComponent`）就是**访问已释放内存 = 崩**。
        ///    ⇒ 摘之前先对一下场景指针：**不是同一个场景了就只丢句柄、一个 native 调用都不发**。
        /// </summary>
        private UIntPtr _scenePtr = UIntPtr.Zero;

        /// <summary>已经报过"这个粒子没注册"的粒子名（挂在每帧热路径上，不防会刷屏）。</summary>
        private readonly HashSet<string> _warnedMissing = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>现在挂着哪些粒子（命令提示用）。</summary>
        public ICollection<string> LiveParticles => _live.Keys;

        // ───────────────────────── IAnimFxHost ─────────────────────────

        /// <summary>
        /// **挂上**（幂等：同一个粒子重复调 = 先摘干净再挂，不会叠加）。
        /// 🔴 **绝不外抛** —— 调用方是每帧跑的状态机。失败一律"记一行 + 返回"。
        /// </summary>
        public void AttachFx(string particle, string[] bones)
        {
            try
            {
                Attach(particle, bones);
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[Flight-Fx] 挂 '" + particle + "' 异常（已忽略）: " + ex.Message);
            }
        }

        /// <summary>**摘掉**（幂等：本来没挂就什么也不做）。同样绝不外抛。</summary>
        public void DetachFx(string particle)
        {
            try
            {
                Detach(particle);
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[Flight-Fx] 摘 '" + particle + "' 异常（已忽略）: " + ex.Message);
            }
        }

        // ───────────────────────── 实现 ─────────────────────────

        /// <summary>挂上一颗粒子（把 <paramref name="boneNames"/> 逐根解析成真实骨）。</summary>
        public bool Attach(string particle, string[] boneNames)
        {
            if (string.IsNullOrEmpty(particle) || boneNames == null || boneNames.Length == 0)
            {
                DebugLogger.Log("[Flight-Fx] 挂载参数不全（粒子名 / 骨名单为空）—— 跳过");
                return false;
            }

            Detach(particle);       // 幂等：先收拾干净

            if (!TryResolveFxId(particle, out int id))
            {
                return false;
            }
            Mission mission = Mission.Current;
            Agent main = mission != null ? mission.MainAgent : null;
            MBAgentVisuals visuals = main != null ? main.AgentVisuals : null;
            Skeleton skel = visuals != null ? visuals.GetSkeleton() : null;
            Scene scene = mission != null ? mission.Scene : null;
            if (mission == null || main == null || visuals == null || skel == null || scene == null)
            {
                DebugLogger.Log("[Flight-Fx] 不在 mission 里 / 主角没有 visuals —— 挂不了");
                return false;
            }

            var e = new Entry
            {
                Particle = particle,
                BoneNames = boneNames,
                RealBone = new sbyte[boneNames.Length],
                Host = new GameEntity[boneNames.Length],
                Ps = new ParticleSystem[boneNames.Length],
            };
            MatrixFrame agentFrame = visuals.GetGlobalFrame();
            var placed = new List<string>();
            for (int i = 0; i < boneNames.Length; i++)
            {
                e.RealBone[i] = -1;
                if (!Enum.TryParse(boneNames[i], out HumanBone hb))
                {
                    continue;      // 装载期已校验过；真到这里说明定义绕过装载器来的 —— 跳过
                }
                sbyte bone = visuals.GetRealBoneIndex(hb);
                if (bone < 0)
                {
                    continue;      // 这个 race 没有这根骨 —— 跳过（其它骨照挂）
                }

                // ① 宿主实体（**关键**：粒子必须挂在场景里的实体上，否则不进渲染名单）
                GameEntity host = GameEntity.CreateEmpty(scene, true);
                if (host == null)
                {
                    continue;
                }
                Vec3 world = agentFrame.TransformToParent(skel.GetBoneEntitialFrameWithIndex(bone).origin);
                host.SetGlobalFrame(new MatrixFrame(Mat3.Identity, world));

                // ② 粒子挂到实体上（不是挂到裸 Skeleton 上 —— 第一版就栽在这里）
                MatrixFrame local = MatrixFrame.Identity;
                ParticleSystem ps = ParticleSystem.CreateParticleSystemAttachedToEntity(id, host, ref local);
                if (ps == null)
                {
                    try { host.Remove(0); } catch { }
                    continue;
                }

                // ③ 实体成为 agent visuals 的子实体（跟着 agent 走）
                try { visuals.AddChildEntity(host); } catch { }

                // ④ 粒子再挂到骨上 —— 引擎负责"骨怎么动它怎么动"
                try { skel.AddComponentToBone(bone, ps); } catch { }

                e.Host[i] = host;
                e.Ps[i] = ps;
                e.RealBone[i] = bone;
                placed.Add(hb + "(骨" + bone + ")");
            }

            if (placed.Count == 0)
            {
                DebugLogger.Log("[Flight-Fx] '" + particle + "' 一根骨都没挂上（骨名：" + string.Join("+", boneNames) + "）");
                return false;
            }
            _live[particle] = e;
            _scenePtr = scene.Pointer;
            DebugLogger.Log("[Flight-Fx] '" + particle + "' 已挂上（id " + id + "，"
                            + placed.Count + " 根骨：" + string.Join(" / ", placed) + "）");
            return true;
        }

        /// <summary>摘掉一颗粒子（键认不出 = 什么也不做）。</summary>
        public bool Detach(string particle)
        {
            if (string.IsNullOrEmpty(particle) || !_live.TryGetValue(particle, out Entry e))
            {
                return false;
            }
            _live.Remove(particle);

            UIntPtr curScene = UIntPtr.Zero;
            try
            {
                Mission m = Mission.Current;
                if (m != null && m.Scene != null)
                {
                    curScene = m.Scene.Pointer;
                }
            }
            catch
            {
                // 拿不到当前场景 = 当"不是同一个场景"处理（保守：不发 native 调用）
            }
            bool sameScene = _scenePtr != UIntPtr.Zero && curScene == _scenePtr;

            Skeleton skel = null;
            if (sameScene)
            {
                try { skel = Mission.Current?.MainAgent?.AgentVisuals?.GetSkeleton(); }
                catch { skel = null; }
            }

            string how = "none";
            for (int i = 0; i < e.Ps.Length; i++)
            {
                ParticleSystem ps = e.Ps[i];
                GameEntity host = e.Host[i];
                if (ps == null && host == null)
                {
                    continue;
                }
                if (sameScene)
                {
                    // ① 先停发射（不依赖后面两步成功）
                    if (ps != null)
                    {
                        try { ps.SetEnable(false); }
                        catch { /* 引擎可能已经自己收走了 —— 忽略 */ }
                    }
                    // ② 从骨上摘（记下走的是哪条路）
                    try
                    {
                        if (ps != null && skel != null && e.RealBone[i] >= 0 && skel.HasBoneComponent(e.RealBone[i], ps))
                        {
                            skel.RemoveBoneComponent(e.RealBone[i], ps);
                            how = "bone";
                        }
                        else if (ps != null && skel != null && skel.HasComponent(ps))
                        {
                            skel.RemoveComponent(ps);
                            how = "skel";
                        }
                        else if (ps != null && how == "none")
                        {
                            how = "enable-off-only";
                        }
                    }
                    catch (Exception ex)
                    {
                        how = "throw:" + ex.Message;
                    }
                    // ③ 销毁宿主实体 —— **保底杀招**：实体一没，挂在上面的粒子跟着没
                    if (host != null)
                    {
                        try { host.Remove(0); }
                        catch (Exception ex) { DebugLogger.Log("[Flight-Fx] 销毁宿主实体异常（已忽略）: " + ex.Message); }
                    }
                }
                e.Ps[i] = null;
                e.Host[i] = null;
                e.RealBone[i] = -1;
            }

            if (_live.Count == 0)
            {
                _scenePtr = UIntPtr.Zero;
            }
            DebugLogger.Log(sameScene
                ? "[Flight-Fx] '" + particle + "' 已摘除（骨上摘除路径=" + how + "，宿主实体已销毁）"
                : "[Flight-Fx] '" + particle + "' 句柄已丢弃（场景已换，未触碰 native 对象）");
            return true;
        }

        /// <summary>摘掉**全部**（收摊 / 命令用）。</summary>
        public void DetachAll()
        {
            if (_live.Count == 0)
            {
                return;
            }
            var names = new List<string>(_live.Keys);
            for (int i = 0; i < names.Count; i++)
            {
                try { Detach(names[i]); }
                catch (Exception ex) { DebugLogger.Log("[Flight-Fx] 全摘时异常（已忽略）: " + ex.Message); }
            }
        }

        /// <summary>一句话状态（给 `custom.flight trail` 用）。</summary>
        public string Describe()
        {
            if (_live.Count == 0)
            {
                return "none attached";
            }
            var sb = new System.Text.StringBuilder();
            bool first = true;
            foreach (KeyValuePair<string, Entry> kv in _live)
            {
                if (!first) sb.Append(" · ");
                first = false;
                sb.Append(kv.Key).Append('(').Append(kv.Value.Alive).Append('/')
                  .Append(kv.Value.BoneNames.Length).Append(')');
            }
            return sb.ToString();
        }

        /// <summary>粒子名 → 引擎 id（缺失**每个名字只报一次**）。</summary>
        private bool TryResolveFxId(string particle, out int id)
        {
            id = ParticleSystemManager.GetRuntimeIdByName(particle);
            if (id != -1)
            {
                return true;
            }
            if (_warnedMissing.Add(particle))
            {
                DebugLogger.Log("[Flight-Fx] '" + particle + "' NOT REGISTERED (id -1) —— "
                                + "粒子包发布进游戏加载的那个模块了吗？");
            }
            return false;
        }
    }
}
