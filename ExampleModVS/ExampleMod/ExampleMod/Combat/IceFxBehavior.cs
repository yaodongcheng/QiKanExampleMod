using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.Combat
{
    /// <summary>
    /// 🔴 冰冻效果 · 探针版（2026-10-07 立，控制台 <c>custom.ice</c>）
    ///
    /// 【本件要回答的两个问题】
    ///   ① 能不能"**只冻他一个**" —— 他的动作停住，旁边的人照常；
    ///   ② 能不能"**只把他身上的材质换成冰雪**" —— 且不串给穿同一件甲的其他人。
    ///
    /// 【① 动作定格：走引擎自己的暂停接口，不自己每帧写骨头】
    ///   · <c>GameEntity.PauseSkeletonAnimation()</c> / <c>ResumeSkeletonAnimation()</c> / <c>IsSkeletonAnimationPaused()</c>
    ///     —— 引擎在多人同步里就用它暂停/恢复动作（`MissionNetworkComponent` 收 `SetMissionObjectAnimationPaused` 后照做）。
    ///   · <c>Skeleton.Freeze(true)</c> / <c>IsFrozen()</c> —— 同一件事的另一入口
    ///     （编辑器定格预览脚本 `CharacterSpawner` + 城门 `CastleGate` / 攻城器械 都在用，见
    ///      [Knowledge/骑砍2骨骼级动画API与运行时姿势合成.md] 五·先例表）。
    ///   · 光定格只停"画面"：还得 <c>Agent.SetIsAIPaused(true)</c> 让他别滑走；
    ///     目标是玩家时另走 `V.SetPlayerControlFrozen`（Controller 切 AI）。
    ///   🔴 **两条路都做、日志分开打** —— 到底哪条真管用，由实机判定（探针的意义就在这）。
    ///
    /// 【② 换材质：为什么默认不会串给别人】
    ///   引擎给角色建视觉时，**每件装备都是各人一份的副本** ——
    ///   `ItemObject.GetMultiMesh → GetMultiMeshCopyWithGenderData → MetaMesh.GetCopy`（反编译实证，
    ///   `TaleWorlds.MountAndBlade.View/AgentVisuals.GetMultiMesh`）。引擎自己给队伍色/旗帜改材质就是这个套路：
    ///   `Material.CreateCopy()` → 改参数 → `mesh.SetMaterial(副本)`（同文件 `AddArmorMultiMeshesToAgentEntity`）。
    ///   🔴 **纪律：只换网格的材质指针，绝不改共享的材质资源本身**（改了 = 全世界穿这件甲的人都变）。
    ///
    /// 【默认配方（零新资产 —— 贴图直接用游戏自带的冰）】
    ///   ① **换贴图**：把材质的三个槽换成冰的三件套 ——
    ///      diffuse / 法线 / 高光 = 槽位 0 / 2 / 4（`MBTextureType` 与材质槽一一对应）。
    ///      游戏里现成的三套：`ice_a_d/_n/_s`（**默认** —— 已导出 PNG 亲眼确认：带裂纹的白冰）
    ///      · `ice_1_d/_n/_s` · `icicle_d/_n/_s_`（后两套我们的 tpaccli 取不出像素，游戏侧不受影响，
    ///      想试就 `custom.ice tex ice_1_d`）。全套清单 = `tpaccli inspect --filter ice`。
    ///      🔴 换材质**只换材质副本的贴图槽**，原材质资源一个字节都不动 —— 所以不会串给别人。
    ///   ② **自发光**：`self_illumination` flag + VA 的颜色/强度 —— 配方抄自第三方 mod SwordBeam
    ///      （[Knowledge/SwordBeam剑气_实现分析.md] §1.2/§1.3，已在实机跑通）：
    ///      flag 位**按名字查**（`Shader.GetMaterialShaderFlagMask`，查不到才用常量 524288），
    ///      强度 = `MeshVectorArgument` 的 **w 分量**。冰的"内透光"就靠它。
    ///   两样都能单独关（`custom.ice tex none` 只发光 / `custom.ice tint 0.72 0.88 1 0` 只换贴图）。
    ///
    /// 【🔴 负向检查（本件第一判据，比"变没变"更重要）】
    ///   冻住一个兵 → **看旁边穿同款甲的兵有没有跟着变**。
    ///   跟着变 = 网格是被共享的 ⇒ 这条路作废，改用 `MBAgentVisuals.ReplaceMeshWithMesh` 逐部位替换。
    ///
    /// 【🔴 头部（脸）：已判定 —— 现阶段盖不全，默认不做】
    ///   角色那件"身体+头"走的是 `MBAgentVisuals.AddSkinMeshes`，**不注册成任何可遍历的组件**
    ///   （三条路都试过：实体树 / 骨架组件 / 逐骨挂件；手 hands_male_a、脚 feet_male_a 捞得到，
    ///    头和身体捞不到）。native 接口只有 `AddMultiMesh`/`RemoveMultiMesh`，**两个都要指针、没有 getter**
    ///   （引擎自己加 banner 标签也是自己存字典）。皮肤的"这个 race 用哪个头"写在 skins.xml，
    ///   但换头模块是拿 **skins.xslt 改写**它的 ⇒ 照原文件查表查不到真名，且整条链在 native 里。
    ///   试过的补救：`AddMultiMesh(头副本, BodyMeshTypes.Head)` 往头上叠一颗冰头 ——
    ///   能贴上，但**形状跟真头对不上**（副本是资源里的基础头，真头带捏脸/自定义），真脸穿出来；
    ///   想缩放兜住 ⇒ 给网格写 `MetaMesh.Frame`/`Mesh.SetLocalFrame` 会把**蒙皮绑定冲掉**（整颗头飞半空）。
    ///   ⇒ 结论：**头默认不换**（`custom.ice on` 不含它）；`custom.ice head on` 保留作实验。
    ///
    /// 【已知未验项（实机一并看）】
    ///   · 定格解除后动作会不会"跳一下"（定格期间逻辑层的动作可能仍在推进）。
    ///   · 🔴 **网格是否跨角色共享**：日志里见过 `xxx(copy)(copy)` 的材质名，提示同一件装备的网格
    ///     可能在多人之间复用 —— 冻一个人之后**必须看旁边穿同款的人有没有跟着变**。
    /// </summary>
    public class IceFxBehavior : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        /// <summary>全局唯一一份（命令侧通过它操作）。</summary>
        public static IceFxBehavior Instance { get; private set; }

        // ── 每个被处理过的 agent 的记录 ────────────────────────────
        private sealed class MeshRec
        {
            public Mesh Mesh;
            public Material Original;
            public uint Color;
            public uint Color2;
        }

        private sealed class TargetState
        {
            public Agent Agent;
            public readonly List<MeshRec> Meshes = new List<MeshRec>();
            public bool Frosted;

            public GameEntity Entity;
            public Skeleton Skeleton;
            public bool EntityWasPaused;
            public bool SkelWasFrozen;
            public bool AiWasPaused;
            public bool PlayerWasFrozen;
            public bool Frozen;
        }

        private readonly Dictionary<Agent, TargetState> _states = new Dictionary<Agent, TargetState>();

        /// <summary>给我们自己加进去的"冰头"留的引用（引擎没有 getter，只能自己记账 —— 引擎自己的
        /// banner 标签也是这么干的：`_agentMeshes[agent]`）。
        /// ⚠️ 冰头是**实验件、默认不参与**（`custom.ice on` 不带它）：抄来的头形状跟真头对不上，
        /// 盖不全；想缩放又会把蒙皮绑定冲掉。只有显式敲 `custom.ice head on` 才会加。</summary>
        private readonly Dictionary<Agent, MetaMesh> _headShells = new Dictionary<Agent, MetaMesh>();

        // ── 可调参数（custom.ice tint / tex 改）────────────────────
        private float _r = 0.72f, _g = 0.88f, _b = 1.00f, _glow = 1.2f;

        /// <summary>冰贴图（**diffuse 名**；`_n` / `_s` 由它推导）—— 默认用游戏自带的冰三件套。</summary>
        private string _texName = "ice_a_d";
        private string _texHow = "(not resolved yet)";
        private Texture _texD, _texN, _texS;

        // 最近一次扫描的账（probe 打印 + 日志用）
        private int _lastMeshes;
        private int _lastFromEntity, _lastFromSkeleton, _lastFromBones;
        private readonly List<string> _lastMatNames = new List<string>();

        private float _cleanupTimer;

        public override void OnCreated()
        {
            Instance = this;
            base.OnCreated();
        }

        public override void OnRemoveBehavior()
        {
            // 收场要干净：把还活着的目标全部还原（材质 + 动作）
            List<Agent> keys = new List<Agent>(_states.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                try { RestoreOne(keys[i]); } catch (Exception) { }
            }
            _states.Clear();
            List<Agent> shellKeys = new List<Agent>(_headShells.Keys);
            for (int i = 0; i < shellKeys.Count; i++)
            {
                try { RemoveHeadShell(shellKeys[i]); } catch (Exception) { }
            }
            _headShells.Clear();
            if (Instance == this) Instance = null;
            base.OnRemoveBehavior();
        }

        public override void OnMissionTick(float dt)
        {
            if (_states.Count == 0) return;
            _cleanupTimer += dt;
            if (_cleanupTimer < 2f) return;
            _cleanupTimer = 0f;

            // 死掉/离场的 agent 直接丢记录（可视件已没，还原无意义）
            List<Agent> dead = null;
            foreach (KeyValuePair<Agent, TargetState> kv in _states)
            {
                Agent a = kv.Key;
                if (a == null || !a.IsActive() || a.AgentVisuals == null || !a.AgentVisuals.IsValid())
                    (dead ?? (dead = new List<Agent>())).Add(a);
            }
            if (dead == null) return;
            for (int i = 0; i < dead.Count; i++) _states.Remove(dead[i]);
        }

        // ══════════════════════════════════════════════════════════
        //  对外：探针
        // ══════════════════════════════════════════════════════════

        /// <summary>只查不改：这个人身上到底有多少网格、材质叫什么、骨架能不能冻。</summary>
        public string Probe(Agent a)
        {
            if (a == null) return "error: no target agent.";

            StringBuilder sb = new StringBuilder();
            sb.Append("OK (probe). agent='").Append(a.Name ?? "?").Append("' idx=").Append(a.Index)
              .Append(" human=").Append(a.IsHuman).Append(" active=").Append(a.IsActive())
              .Append(" aiPaused=").Append(a.IsPaused);

            GameEntity ent = SafeEntity(a);
            if (ent == null)
            {
                sb.Append(" | entity=NULL -- agent has no visuals (dead/unspawned?)");
                return sb.ToString();
            }

            sb.Append(" | entity='").Append(ent.Name ?? "?").Append("'");
            sb.Append(" childCount=").Append(SafeInt(() => ent.ChildCount));
            sb.Append(" multiMeshComponents=").Append(SafeInt(() => ent.MultiMeshComponentCount));
            sb.Append(" clothComponents=").Append(SafeInt(() => ent.ClothSimulatorComponentCount));

            // 🔴 角色网格分三处挂（2026-10-07 实机定案：第一次只从实体找，只找到 3 个网格 = 没扫到甲）：
            //    ① 实体树的 MetaMesh 组件  ② **骨架自己的组件**（甲/身体在这）
            //    ③ 逐骨挂件（武器/披风这类绑在某根骨上的）
            List<MeshHit> hits = CollectAgentMeshes(a, out _lastFromEntity, out _lastFromSkeleton, out _lastFromBones, _lastMatNames);
            _lastMeshes = hits.Count;
            DumpMeshesToLog(a, hits);
            DumpComponentTypes(a);
            sb.Append(" | subMeshes=").Append(_lastMeshes)
              .Append(" (entity ").Append(_lastFromEntity)
              .Append(" / skeleton ").Append(_lastFromSkeleton)
              .Append(" / bones ").Append(_lastFromBones).Append(")");

            if (_lastMatNames.Count > 0)
            {
                sb.Append(" | materials=[");
                for (int i = 0; i < _lastMatNames.Count && i < 8; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(_lastMatNames[i]);
                }
                if (_lastMatNames.Count > 8) sb.Append(", +").Append(_lastMatNames.Count - 8).Append(" more");
                sb.Append("]");
            }
            else
            {
                sb.Append(" | materials=[] <- NO MESH REACHED");
            }

            Skeleton skel = SafeSkeleton(a);
            if (skel != null)
            {
                sb.Append(" | skeleton bones=").Append(SafeInt(() => (int)skel.GetBoneCount()))
                  .Append(" frozen=").Append(SafeBool(() => skel.IsFrozen()));
            }
            else sb.Append(" | skeleton=NULL");

            sb.Append(" | skeletonAnimPaused=").Append(SafeBool(() => ent.IsSkeletonAnimationPaused()));

            // 装备清单（人工核对"该有的件在不在"）
            try
            {
                var eq = a.SpawnEquipment;
                if (eq != null)
                {
                    sb.Append(" | equip=[");
                    bool first = true;
                    for (int i = 0; i < 12; i++)
                    {
                        var item = eq[(EquipmentIndex)i].Item;
                        if (item == null) continue;
                        if (!first) sb.Append(", ");
                        first = false;
                        sb.Append((EquipmentIndex)i).Append(":").Append(item.StringId);
                    }
                    sb.Append("]");
                }
            }
            catch (Exception) { }

            TargetState st;
            if (_states.TryGetValue(a, out st))
                sb.Append(" | tracked: frozen=").Append(st.Frozen).Append(" frosted=").Append(st.Frosted)
                  .Append(" records=").Append(st.Meshes.Count);

            return sb.ToString();
        }

        // ══════════════════════════════════════════════════════════
        //  对外：定格动作
        // ══════════════════════════════════════════════════════════

        public string SetFreeze(Agent a, bool on)
        {
            if (a == null) return "error: no target agent.";
            if (a.AgentVisuals == null || !a.AgentVisuals.IsValid())
                return "error: target has no valid visuals.";

            TargetState st = GetOrCreate(a);

            if (on)
            {
                if (st.Frozen) return $"OK: '{a.Name}' is already frozen.";

                GameEntity ent = SafeEntity(a);
                if (ent != null)
                {
                    st.Entity = ent;
                    st.EntityWasPaused = SafeBool(() => ent.IsSkeletonAnimationPaused());
                    if (!st.EntityWasPaused) { try { ent.PauseSkeletonAnimation(); } catch (Exception ex) { DebugLogger.Log($"[IceFx] PauseSkeletonAnimation 失败: {ex.Message}"); } }
                }

                Skeleton skel = SafeSkeleton(a);
                if (skel != null)
                {
                    st.Skeleton = skel;
                    st.SkelWasFrozen = SafeBool(() => skel.IsFrozen());
                    if (!st.SkelWasFrozen) { try { skel.Freeze(true); } catch (Exception ex) { DebugLogger.Log($"[IceFx] Skeleton.Freeze 失败: {ex.Message}"); } }
                }

                st.AiWasPaused = a.IsPaused;
                try { a.SetIsAIPaused(true); } catch (Exception ex) { DebugLogger.Log($"[IceFx] SetIsAIPaused 失败: {ex.Message}"); }

                if (a == Agent.Main)
                {
                    st.PlayerWasFrozen = true;
                    try { V.SetPlayerControlFrozen(a, true); } catch (Exception ex) { DebugLogger.Log($"[IceFx] SetPlayerControlFrozen 失败: {ex.Message}"); }
                }

                st.Frozen = true;
                DebugLogger.Log($"[IceFx] FREEZE on '{a.Name}' idx={a.Index} | entityPaused={st.Entity != null} skelFrozen={st.Skeleton != null} aiPaused={a.IsPaused}");
                return $"OK: froze '{a.Name}' (idx {a.Index}) -- skeleton animation paused + AI paused"
                     + (a == Agent.Main ? " + player control frozen" : "")
                     + ". Watch: does HE stop while others keep moving?";
            }

            if (!st.Frozen) return $"OK: '{a.Name}' was not frozen.";
            Unfreeze(a, st);
            return $"OK: unfroze '{a.Name}'. Watch: does he resume smoothly (or jump)?";
        }

        private void Unfreeze(Agent a, TargetState st)
        {
            try { if (st.Skeleton != null && !st.SkelWasFrozen) st.Skeleton.Freeze(false); } catch (Exception) { }
            try { if (st.Entity != null && !st.EntityWasPaused) st.Entity.ResumeSkeletonAnimation(); } catch (Exception) { }
            try { a.SetIsAIPaused(st.AiWasPaused); } catch (Exception) { }
            if (st.PlayerWasFrozen)
            {
                try { V.SetPlayerControlFrozen(a, false); } catch (Exception) { }
                st.PlayerWasFrozen = false;
            }
            st.Frozen = false;
            DebugLogger.Log($"[IceFx] FREEZE off '{a.Name}'");
        }

        // ══════════════════════════════════════════════════════════
        //  对外：换材质
        // ══════════════════════════════════════════════════════════

        public string SetFrost(Agent a, bool on)
        {
            if (a == null) return "error: no target agent.";
            GameEntity root = SafeEntity(a);
            if (root == null) return "error: target has no visual entity.";

            TargetState st = GetOrCreate(a);

            if (on)
            {
                if (st.Frosted) return $"OK: '{a.Name}' is already frosted.";

                List<string> matNames = new List<string>();
                List<MeshHit> hits = CollectAgentMeshes(a, out int cEnt, out int cSkel, out int cBones, matNames);
                _lastFromEntity = cEnt; _lastFromSkeleton = cSkel; _lastFromBones = cBones;
                _lastMeshes = hits.Count;
                _lastMatNames.Clear();
                _lastMatNames.AddRange(matNames);

                if (hits.Count == 0)
                    return "FAILED: 0 sub-meshes reachable (entity + skeleton + bones all empty) -- "
                         + "cannot reach this agent's meshes. Next step: MBAgentVisuals.ReplaceMeshWithMesh per body part.";

                ResolveFrostTextures();
                Color col = new Color(_r, _g, _b);
                uint colU = col.ToUnsignedInteger();

                // 🔴 **同一个材质只复制一份**：25 个网格往往只共用三五个材质，
                //    每个网格各造一份新材质 = 一次性上传 25 个原生材质对象（卡顿来源之一）。
                //    key 用材质资源名（同名即同一份资源 ⇒ 可以共用副本）。
                Dictionary<string, Material> copies = new Dictionary<string, Material>();
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                long msFirstMesh = -1;

                int applied = 0, texApplied = 0, glowApplied = 0;
                for (int h = 0; h < hits.Count; h++)
                {
                    {
                        Mesh mesh = hits[h].Mesh;
                        string src = hits[h].Source;
                        {
                            Material orig = null;
                            try { orig = mesh.GetMaterial(); } catch (Exception) { }
                            if (orig == null) continue;

                            try
                            {
                                string key = null;
                                try { key = orig.Name; } catch (Exception) { }
                                Material copy = null;
                                if (!string.IsNullOrEmpty(key)) copies.TryGetValue(key, out copy);

                                if (copy == null)
                                {
                                    if (!BuildIceMaterial(orig, out copy)) continue;
                                    if (_texD != null) texApplied++;
                                    if (_glow > 0f) glowApplied++;
                                    if (!string.IsNullOrEmpty(key)) copies[key] = copy;
                                }

                                st.Meshes.Add(new MeshRec
                                {
                                    Mesh = mesh,
                                    Original = orig,
                                    Color = SafeUInt(() => mesh.Color),
                                    Color2 = SafeUInt(() => mesh.Color2),
                                });
                                mesh.SetMaterial(copy);
                                if (_glow > 0f) { mesh.Color = colU; mesh.Color2 = colU; }
                                applied++;
                                if (msFirstMesh < 0) msFirstMesh = sw.ElapsedMilliseconds;
                            }
                            catch (Exception ex)
                            {
                                DebugLogger.Log($"[IceFx] 换材质失败(src={src} h={h}): {ex.Message}");
                            }
                        }
                    }
                }
                sw.Stop();

                st.Frosted = true;
                string sample = _lastMatNames.Count > 0 ? string.Join(" | ", _lastMatNames.GetRange(0, Math.Min(4, _lastMatNames.Count))) : "-";
                DebugLogger.Log($"[IceFx] FROST on '{a.Name}' idx={a.Index} | meshes={applied}(entity {cEnt}/skel {cSkel}/bone {cBones}) "
                              + $"materials={copies.Count} texApplied={texApplied} glowApplied={glowApplied} tex={_texName}({_texHow})");
                DebugLogger.Log($"[IceFx]   timing: {sw.ElapsedMilliseconds}ms total, first mesh at {msFirstMesh}ms (loop {hits.Count} hits)");
                DebugLogger.Log($"[IceFx]   hit sample: {sample}");
                DumpMeshesToLog(a, hits);
                return $"OK: iced '{a.Name}' -- {applied} mesh(es) rematerialized [entity {cEnt} / skeleton {cSkel} / bones {cBones}], "
                     + $"{copies.Count} new material(s) in {sw.ElapsedMilliseconds}ms (diffuse {texApplied}, glow {glowApplied}, ice set '{_texName}' [{_texHow}]). "
                     + "🔴 NEGATIVE CHECK: look at OTHER soldiers wearing the same armor -- if they changed too, meshes are shared and we must switch to ReplaceMeshWithMesh.";
            }

            if (!st.Frosted) return $"OK: '{a.Name}' was not frosted.";
            RestoreMeshes(st);
            return $"OK: material restored on '{a.Name}' ({st.Meshes.Count} record(s) cleared). Check: other soldiers still look correct?";
        }

        private void RestoreMeshes(TargetState st)
        {
            // 🔴 **倒序还原**：同一个网格可能被两个来源都收到（实体 + 骨架），
            //    正序会把最后那份"冰材质副本"当成原材质写回去 —— 倒序保证最早的记录（真·原材质）最终生效。
            for (int i = st.Meshes.Count - 1; i >= 0; i--)
            {
                MeshRec r = st.Meshes[i];
                try
                {
                    if (r.Mesh != null)
                    {
                        r.Mesh.SetMaterial(r.Original);
                        r.Mesh.Color = r.Color;
                        r.Mesh.Color2 = r.Color2;
                    }
                }
                catch (Exception) { }
            }
            st.Meshes.Clear();
            st.Frosted = false;
        }

        private void RestoreOne(Agent a)
        {
            TargetState st;
            if (!_states.TryGetValue(a, out st)) return;
            if (st.Frosted) RestoreMeshes(st);
            if (st.Frozen) Unfreeze(a, st);
            RemoveHeadShell(a);
        }

        private void RemoveHeadShell(Agent a)
        {
            MetaMesh shell;
            if (a == null || !_headShells.TryGetValue(a, out shell)) return;
            try
            {
                if (a.AgentVisuals != null && a.AgentVisuals.IsValid())
                    a.AgentVisuals.ReplaceMeshWithMesh(shell, null, BodyMeshTypes.Head);
            }
            catch (Exception ex) { DebugLogger.Log($"[IceFx] 摘冰头异常: {ex.Message}"); }
            _headShells.Remove(a);
        }

        /// <summary>把所有冰头壳摘掉（`custom.ice head off` 不写目标时走这条）。</summary>
        public string RemoveAllHeadShells()
        {
            int n = 0;
            List<Agent> keys = new List<Agent>(_headShells.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                try { RemoveHeadShell(keys[i]); n++; } catch (Exception) { }
            }
            DebugLogger.Log($"[IceFx] HEAD shells removed: {n}");
            return $"OK: removed {n} ice head shell(s).";
        }

        /// <summary>
        /// 把**所有**处理过的目标还原（命令侧 <c>custom.ice off</c> 不写目标时走这条）——
        /// 当"取消"用：转身之后再敲 off 也不会指错人。
        /// </summary>
        public string RestoreAll(bool freeze, bool frost)
        {
            int unfroze = 0, unfrosted = 0;
            List<Agent> keys = new List<Agent>(_states.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                TargetState st;
                if (!_states.TryGetValue(keys[i], out st)) continue;
                try
                {
                    if (frost && st.Frosted) { RestoreMeshes(st); unfrosted++; }
                    if (freeze && st.Frozen) { Unfreeze(keys[i], st); unfroze++; }
                    if (frost) RemoveHeadShell(keys[i]);
                }
                catch (Exception ex) { DebugLogger.Log($"[IceFx] 全还原异常: {ex.Message}"); }
            }
            // 冰头壳可能属于"只加过头、没进 _states"的目标 —— 单独扫一遍，别漏
            if (frost)
            {
                List<Agent> shells = new List<Agent>(_headShells.Keys);
                for (int i = 0; i < shells.Count; i++)
                {
                    try { RemoveHeadShell(shells[i]); unfrosted++; } catch (Exception) { }
                }
            }

            DebugLogger.Log($"[IceFx] RESTORE ALL | unfroze={unfroze} unfrosted={unfrosted} tracked={_states.Count}");
            return $"OK: restored all -- unfroze {unfroze}, un-frosted {unfrosted} (tracked {_states.Count}).";
        }

        /// <summary>这个人当前被处理成什么样（点名册/日志用）。没处理过返回 null。</summary>
        public string TagOf(Agent a)
        {
            TargetState st;
            if (a == null || !_states.TryGetValue(a, out st)) return null;
            if (st.Frozen && st.Frosted) return "frozen+frosted";
            if (st.Frozen) return "frozen";
            if (st.Frosted) return "frosted";
            return null;
        }

        // ══════════════════════════════════════════════════════════
        //  对外：冰头壳（往 BodyMeshTypes.Head 槽上加一件）
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// 🔴 **头和身体那件网格拿不到** —— 它走 `AddSkinMeshes` 的 native 路径，不注册成任何我们能遍历的组件
        /// （2026-10-07 实机定案：手 `hands_male_a` / 脚 `feet_male_a` 枚举得到，**头和身体枚举不到**；
        ///  引擎内部按 `BodyMeshTypes` 管着这些件，但**没有 getter**，它自己加 banner 标签也是自己存字典）。
        ///
        /// 出路在 `ReplaceMeshWithMesh` 的反编译实现上：
        /// <code>
        /// if (old != null) MBAPI.IMBAgentVisuals.RemoveMultiMesh(p, old.Pointer, type);
        /// if (new != null) MBAPI.IMBAgentVisuals.AddMultiMesh(p, new.Pointer, type);
        /// </code>
        /// **两边都能传 null** ⇒ **不用交出旧指针，就能往某个部位"加"一件网格**。
        /// 于是换个打法：不删原来那颗头，**直接在 `BodyMeshTypes.Head` 槽上加一颗冰头**。
        /// 资源名来自我们自己的换脸文档（男 `head_male_a` / 女 `head_female_a`，[Knowledge/脸部系统分析.md]）。
        ///
        /// ⚠️ 未验：同一槽加第二件是"替换"还是"并存"。并存的话会两颗头重叠（真脸穿出来 / z-fighting），
        /// 那就得再想办法（给冰头放大一点把真头整个包住）。
        /// </summary>
        public string SetHeadShell(Agent a, bool on)
        {
            if (a == null) return "error: no target agent.";
            if (a.AgentVisuals == null || !a.AgentVisuals.IsValid()) return "error: target has no valid visuals.";

            if (on)
            {
                if (_headShells.ContainsKey(a)) return $"OK: '{a.Name}' already has an ice head shell.";
                MetaMesh shell = BuildIceHeadMesh(a, out string how);
                if (shell == null) return $"FAILED: could not build an ice head mesh ({how}).";
                try { a.AgentVisuals.AddMultiMesh(shell, BodyMeshTypes.Head); }
                catch (Exception ex) { return $"FAILED: AddMultiMesh threw {ex.Message}"; }

                // 🔴 **不要在这儿写帧**（`MetaMesh.Frame` / `Mesh.SetLocalFrame`）——
                //    2026-10-07 实机：写了之后蒙皮绑定被冲掉，整颗头**飞在半空**。
                //    引擎自己的 `UseHeadBoneFaceGenScaling` 也没敢跟（那一版同样挪位）。
                //    结论：这条路只能"原样加进去"，形状对不上就是盖不全，接受。

                _headShells[a] = shell;
                DebugLogger.Log($"[IceFx] HEAD shell on '{a.Name}' idx={a.Index} | src={how}");
                return $"OK: ice head shell added at BodyMeshTypes.Head (source: {how}). "
                     + "Watch: did the FACE turn to ice? Any double head / z-fighting / wrong size?";
            }

            MetaMesh cur;
            if (!_headShells.TryGetValue(a, out cur)) return $"OK: '{a.Name}' has no ice head shell.";
            RemoveHeadShell(a);
            return $"OK: ice head shell removed from '{a.Name}'.";
        }

        /// <summary>按性别挑头网格资源名，复制一份、全部子网格上冰材质。</summary>
        private MetaMesh BuildIceHeadMesh(Agent a, out string how)
        {
            bool female = false;
            try { female = a.Character != null && a.Character.IsFemale; } catch (Exception) { }
            string[] cands = female
                ? new[] { "head_female_a", "head_male_a" }
                : new[] { "head_male_a", "head_female_a" };

            List<string> tried = new List<string>();
            for (int i = 0; i < cands.Length; i++)
            {
                MetaMesh mm = null;
                try { mm = MetaMesh.GetCopy(cands[i], false, true); }
                catch (Exception ex) { tried.Add($"{cands[i]}(throw:{ex.Message})"); continue; }
                if (mm == null) { tried.Add($"{cands[i]}(null)"); continue; }
                int mc = SafeInt(() => mm.MeshCount);
                if (mc <= 0) { tried.Add($"{cands[i]}(0 mesh)"); continue; }

                int iced = 0;
                for (int j = 0; j < mc; j++)
                {
                    Mesh mesh = null;
                    try { mesh = mm.GetMeshAtIndex(j); } catch (Exception) { }
                    if (mesh == null) continue;
                    Material orig = null;
                    try { orig = mesh.GetMaterial(); } catch (Exception) { }
                    if (orig == null) continue;
                    if (!BuildIceMaterial(orig, out Material ice)) continue;
                    try { mesh.SetMaterial(ice); iced++; } catch (Exception) { }
                }
                how = $"{cands[i]} ({mc} submesh, {iced} iced)";
                return mm;
            }
            how = "none resolved: " + string.Join(" / ", tried.ToArray());
            return null;
        }

        // ══════════════════════════════════════════════════════════
        //  对外：参数
        // ══════════════════════════════════════════════════════════

        public string SetTint(float r, float g, float b, float glow)
        {
            _r = Clamp01(r); _g = Clamp01(g); _b = Clamp01(b);
            _glow = glow < 0f ? 0f : glow;
            DebugLogger.Log($"[IceFx] tint = ({_r:F2},{_g:F2},{_b:F2}) glow={_glow:F2}");
            // 已经在冻的目标：立刻按新参数重上一遍（先还原再上，避免材质副本叠层）
            foreach (KeyValuePair<Agent, TargetState> kv in _states)
            {
                if (!kv.Value.Frosted) continue;
                try
                {
                    RestoreMeshes(kv.Value);
                    SetFrost(kv.Key, true);
                }
                catch (Exception ex) { DebugLogger.Log($"[IceFx] 重上色失败: {ex.Message}"); }
            }
            return $"OK: tint=({_r:F2},{_g:F2},{_b:F2}) glow={_glow:F2} (0 = no glow). Re-applied to live targets.";
        }

        public string SetTex(string name)
        {
            _texName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            if (_texName != null && string.Equals(_texName, "none", StringComparison.OrdinalIgnoreCase)) _texName = null;
            ResolveFrostTextures();
            DebugLogger.Log($"[IceFx] tex source = '{_texName ?? "(none)"}' -> {_texHow}");

            // 已经在冻的目标：立刻按新贴图重上一遍（先还原再上，避免材质副本叠层）
            int reapplied = 0;
            foreach (KeyValuePair<Agent, TargetState> kv in _states)
            {
                if (!kv.Value.Frosted) continue;
                try
                {
                    RestoreMeshes(kv.Value);
                    SetFrost(kv.Key, true);
                    reapplied++;
                }
                catch (Exception ex) { DebugLogger.Log($"[IceFx] 重上冰贴图失败: {ex.Message}"); }
            }
            return $"OK: ice texture = '{_texName ?? "(none)"}' [{_texHow}]. Re-applied to {reapplied} live target(s).";
        }

        public string Describe()
        {
            return $"ice: tint=({_r:F2},{_g:F2},{_b:F2}) glow={_glow:F2} tex='{_texName ?? "(none)"}' [{_texHow}] "
                 + $"| tracked={_states.Count} headShells={_headShells.Count} | verbs: list [radius] / probe / freeze on|off / frost on|off / head on|off (实验) / on / off / tint R G B [glow] / tex <name|none>";
        }

        /// <summary>当前自发光强度（命令侧 `tint` 不带第 4 参时沿用它）。</summary>
        public float Glow => _glow;

        // ══════════════════════════════════════════════════════════
        //  内部
        // ══════════════════════════════════════════════════════════

        private TargetState GetOrCreate(Agent a)
        {
            TargetState st;
            if (_states.TryGetValue(a, out st)) return st;
            st = new TargetState { Agent = a };
            _states[a] = st;
            return st;
        }

        private static GameEntity SafeEntity(Agent a)
        {
            try
            {
                MBAgentVisuals v = a?.AgentVisuals;
                if (v == null || !v.IsValid()) return null;
                return v.GetEntity();
            }
            catch (Exception) { return null; }
        }

        private static Skeleton SafeSkeleton(Agent a)
        {
            try
            {
                MBAgentVisuals v = a?.AgentVisuals;
                if (v == null || !v.IsValid()) return null;
                return v.GetSkeleton();
            }
            catch (Exception) { return null; }
        }

        /// <summary>实体 + 所有子孙（甲/武器可能是子实体；深度截断防环）。</summary>
        private static void CollectEntities(GameEntity e, List<GameEntity> outList, int depth)
        {
            if (e == null || depth > 4) return;
            outList.Add(e);
            int n = SafeInt(() => e.ChildCount);
            for (int i = 0; i < n; i++)
            {
                GameEntity c = null;
                try { c = e.GetChild(i); } catch (Exception) { }
                if (c != null) CollectEntities(c, outList, depth + 1);
            }
        }

        /// <summary>一个"要换材质的网格"（连同它所在的 MetaMesh 与来源，便于诊断）。</summary>
        private sealed class MeshHit
        {
            public Mesh Mesh;
            public MetaMesh MetaMesh;
            public string Source;
        }

        /// <summary>
        /// 🔴 **角色身上到底有哪些网格** —— 三处都收（2026-10-07 实机定案）：
        ///   ① **实体树**的 MetaMesh 组件（`MultiMeshComponentCount` / `GetMetaMesh`）——
        ///      第一次只走了这一条，**只找到 3 个网格**（甲一个都没扫到）；
        ///   ② 🔴 **骨架自己的组件** —— 甲/身体实际挂在这：
        ///      `Skeleton.GetComponentCount(GameEntity.ComponentType.MetaMesh)` + `GetComponentAtIndex`。
        ///      引擎给角色加甲走的是 `MBAgentVisuals.AddMultiMesh(metamesh, bodyMeshType)`（native），
        ///      **不体现为实体组件**，所以实体那条路看不见它；
        ///   ③ **逐骨挂件** —— 绑在某一根骨上的（武器/披风这类）：
        ///      `GetBoneComponentCount(bone)` + `GetBoneComponentAtIndex(bone, i)`。
        /// 三个来源可能有重叠（同一个 MetaMesh 两处都能取到），去重靠**还原时倒序**（见 <see cref="RestoreMeshes"/>）。
        /// </summary>
        private static List<MeshHit> CollectAgentMeshes(Agent a, out int fromEntity, out int fromSkeleton,
                                                        out int fromBones, List<string> matNames)
        {
            List<MeshHit> list = new List<MeshHit>();
            fromEntity = fromSkeleton = fromBones = 0;

            // ① 实体树
            GameEntity root = SafeEntity(a);
            if (root != null)
            {
                List<GameEntity> ents = new List<GameEntity>();
                CollectEntities(root, ents, 0);
                for (int e = 0; e < ents.Count; e++)
                {
                    GameEntity ent = ents[e];
                    int mmCount = SafeInt(() => ent.MultiMeshComponentCount);
                    for (int i = 0; i < mmCount; i++)
                    {
                        MetaMesh mm = null;
                        try { mm = ent.GetMetaMesh(i); } catch (Exception) { }
                        int before = list.Count;
                        AddMetaMesh(list, mm, "entity:" + (ent.Name ?? "?"), matNames);
                        fromEntity += list.Count - before;
                    }
                }
            }

            // ② 骨架自己的组件 + ③ 逐骨挂件
            Skeleton skel = SafeSkeleton(a);
            if (skel != null)
            {
                int n = SafeInt(() => skel.GetComponentCount(GameEntity.ComponentType.MetaMesh));
                for (int i = 0; i < n; i++)
                {
                    MetaMesh mm = null;
                    try { mm = skel.GetComponentAtIndex(GameEntity.ComponentType.MetaMesh, i) as MetaMesh; } catch (Exception) { }
                    int before = list.Count;
                    AddMetaMesh(list, mm, "skeleton", matNames);
                    fromSkeleton += list.Count - before;
                }

                int boneCount = SafeInt(() => (int)skel.GetBoneCount());
                for (sbyte b = 0; b < boneCount; b++)
                {
                    int bc = SafeInt(() => skel.GetBoneComponentCount(b));
                    for (int k = 0; k < bc; k++)
                    {
                        MetaMesh mm = null;
                        try { mm = skel.GetBoneComponentAtIndex(b, k) as MetaMesh; } catch (Exception) { }
                        int before = list.Count;
                        AddMetaMesh(list, mm, "bone:" + b, matNames);
                        fromBones += list.Count - before;
                    }
                }
            }

            return list;
        }

        private static void AddMetaMesh(List<MeshHit> list, MetaMesh mm, string source, List<string> matNames)
        {
            if (mm == null) return;
            int meshCount = SafeInt(() => mm.MeshCount);
            for (int j = 0; j < meshCount; j++)
            {
                Mesh mesh = null;
                try { mesh = mm.GetMeshAtIndex(j); } catch (Exception) { }
                if (mesh == null) continue;
                list.Add(new MeshHit { Mesh = mesh, MetaMesh = mm, Source = source });
                if (matNames != null)
                {
                    try
                    {
                        Material m = mesh.GetMaterial();
                        matNames.Add((m != null ? (m.Name ?? "(unnamed)") : "(no material)") + "@" + source);
                    }
                    catch (Exception) { matNames.Add("(throw)@" + source); }
                }
            }
        }

        /// <summary>
        /// 把**每一个**网格的名字与材质名打进日志 —— 实机排查"哪一件没被换到"就靠这份清单
        /// （控制台返回串会截断，所以完整清单只进日志）。
        /// </summary>
        private static void DumpMeshesToLog(Agent a, List<MeshHit> hits)
        {
            DebugLogger.Log($"[IceFx] mesh dump: agent='{a?.Name}' idx={a?.Index} total={hits?.Count ?? 0}");
            if (hits == null) return;
            for (int i = 0; i < hits.Count; i++)
            {
                string meshName = "?", matName = "?", mmName = "?";
                try { meshName = hits[i].Mesh.Name ?? "?"; } catch (Exception) { }
                try { Material m = hits[i].Mesh.GetMaterial(); matName = m != null ? (m.Name ?? "(unnamed)") : "(null)"; } catch (Exception) { }
                try { mmName = hits[i].MetaMesh != null ? (hits[i].MetaMesh.GetName() ?? "?") : "?"; } catch (Exception) { }
                DebugLogger.Log($"[IceFx]   [{i}] src={hits[i].Source} mm='{mmName}' mesh='{meshName}' mat='{matName}'");
            }
        }

        /// <summary>
        /// 🔴 **组件全类型扫描**（2026-10-07）—— 实机发现"身体+头"那件网格既不在实体树、也不在骨架的
        /// MetaMesh 组件里（手/脚在，头和身体不在）。这个扫描把实体与骨架上**每一种**组件类型都列一遍，
        /// 用来钉死"那件到底挂在哪个槽"。只进日志，控制台不显示。
        /// </summary>
        private static void DumpComponentTypes(Agent a)
        {
            GameEntity ent = SafeEntity(a);
            Skeleton skel = SafeSkeleton(a);
            Skeleton entSkel = null;
            try { entSkel = ent?.Skeleton; } catch (Exception) { }

            DebugLogger.Log($"[IceFx] component dump: agent='{a?.Name}' idx={a?.Index}");
            DebugLogger.Log($"[IceFx]   visuals.skeleton==entity.skeleton ? {ReferenceEquals(skel, entSkel)}");

            for (int t = 0; t <= 7; t++)
            {
                GameEntity.ComponentType ct = (GameEntity.ComponentType)t;
                int ec = -1, sc = -1, esc = -1;
                try { if (ent != null) ec = ent.GetComponentCount(ct); } catch (Exception) { }
                try { if (skel != null) sc = skel.GetComponentCount(ct); } catch (Exception) { }
                if (!ReferenceEquals(entSkel, skel))
                {
                    try { if (entSkel != null) esc = entSkel.GetComponentCount(ct); } catch (Exception) { }
                }
                DebugLogger.Log($"[IceFx]   type {ct}({t}): entity={ec} visualsSkel={sc} entitySkel={esc}");

                DumpSkeletonComponents(skel, ct, "vskel");
                if (!ReferenceEquals(entSkel, skel)) DumpSkeletonComponents(entSkel, ct, "eskel");
                DumpEntityComponents(ent, ct);
            }

            // 顺带试一把按 tag 取网格（能不能取到组件枚举之外的东西）
            if (ent != null)
            {
                string[] tags = { "", "*", "body", "head", "skin", "face", "cloth", "banner_replacement_mesh" };
                for (int i = 0; i < tags.Length; i++)
                {
                    int n;
                    try { n = Count(ent.GetAllMeshesWithTag(tags[i])); }
                    catch (Exception ex) { DebugLogger.Log($"[IceFx]   tag '{tags[i]}' threw {ex.Message}"); continue; }
                    DebugLogger.Log($"[IceFx]   GetAllMeshesWithTag('{tags[i]}') -> {n}");
                }
            }
        }

        private static int Count(IEnumerable<Mesh> e)
        {
            if (e == null) return -1;
            int n = 0;
            foreach (Mesh m in e) { if (m != null) n++; if (n > 999) break; }
            return n;
        }

        private static void DumpSkeletonComponents(Skeleton skel, GameEntity.ComponentType ct, string tag)
        {
            if (skel == null) return;
            int n = SafeInt(() => skel.GetComponentCount(ct));
            for (int i = 0; i < n && i < 40; i++)
            {
                try { DebugLogger.Log($"[IceFx]     {tag}[{ct}][{i}] = {Describe(skel.GetComponentAtIndex(ct, i))}"); }
                catch (Exception ex) { DebugLogger.Log($"[IceFx]     {tag}[{ct}][{i}] threw {ex.Message}"); }
            }
        }

        private static void DumpEntityComponents(GameEntity ent, GameEntity.ComponentType ct)
        {
            if (ent == null) return;
            int n = SafeInt(() => ent.GetComponentCount(ct));
            for (int i = 0; i < n && i < 40; i++)
            {
                try { DebugLogger.Log($"[IceFx]     ent[{ct}][{i}] = {Describe(ent.GetComponentAtIndex(i, ct))}"); }
                catch (Exception ex) { DebugLogger.Log($"[IceFx]     ent[{ct}][{i}] threw {ex.Message}"); }
            }
        }

        private static string Describe(GameEntityComponent c)
        {
            if (c == null) return "null";
            string cn = c.GetType().Name;
            MetaMesh mm = c as MetaMesh;
            if (mm == null) return cn;
            int mc = 0; string mn = "?";
            try { mc = mm.MeshCount; } catch (Exception) { }
            try { mn = mm.GetName() ?? "?"; } catch (Exception) { }
            return $"{cn} meshes={mc} name='{mn}'";
        }

        /// <summary>
        /// 造一份"冰材质"：**从原材质复制**（保住 shader 与蒙皮标志），然后
        ///   ① 把 diffuse 的两个槽都换成冰贴图（0 号常规 albedo + **1 号** —— 角色的皮肤/脸走 1 号，
        ///      引擎那个 `use_double_colormap_with_mask_texture` 就是"两张颜色图"，只写 0 号 = 头脸不换）；
        ///   ② 加 `self_illumination`（冰的内透光），强度 = `MeshVectorArgument` 的 w 分量。
        /// 🔴 全程只动**副本**，原材质资源一个字节都不碰（碰了 = 全世界同款一起变）。
        /// </summary>
        private bool BuildIceMaterial(Material orig, out Material copy)
        {
            copy = null;
            if (orig == null) return false;
            try
            {
                copy = orig.CreateCopy();
                if (copy == null) return false;

                if (_texD != null)
                {
                    copy.SetTexture(Material.MBTextureType.DiffuseMap, _texD);
                    copy.SetTexture(Material.MBTextureType.DiffuseMap2, _texD);
                }
                if (_texN != null) copy.SetTexture(Material.MBTextureType.BumpMap, _texN);
                if (_texS != null) copy.SetTexture(Material.MBTextureType.SpecularMap, _texS);

                // 兜底：副本必须保持"蒙皮"标志，否则网格不跟骨架动（一帧的错位/消失）
                try { if (orig.UsingSkinning()) copy.SetEnableSkinning(true); } catch (Exception) { }

                // ⚠️ 加 flag 会给 shader 造出**新组合** ⇒ 引擎可能当场现编着色器（表现为"透明一下"）。
                //    想验是不是它：`custom.ice tint .72 .88 1 0` 关掉发光再上。
                if (_glow > 0f)
                {
                    ulong mask = 0UL;
                    try { mask = copy.GetShader()?.GetMaterialShaderFlagMask("self_illumination", false) ?? 0UL; }
                    catch (Exception) { }
                    if (mask == 0UL) mask = 524288UL;
                    copy.SetShaderFlags(copy.GetShaderFlags() | mask);
                    copy.SetMeshVectorArgument(_r, _g, _b, _glow);
                }
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[IceFx] 造冰材质失败: {ex.Message}");
                copy = null;
                return false;
            }
        }

        /// <summary>
        /// 冰贴图从哪来：给的是 **diffuse 名**（如 `ice_1_d`），`_n` / `_s` 由它推导 ——
        /// 游戏自带三套现成的：`ice_1` · `ice_a` · `icicle`（`icicle_s_` 多一个下划线，单独兜）。
        /// 找不到贴图资源时退一步当**材质**资源名试（取它的 diffuse），再不行就只上自发光。
        /// </summary>
        private void ResolveFrostTextures()
        {
            _texD = _texN = _texS = null;
            string name = _texName;
            if (string.IsNullOrWhiteSpace(name)) { _texHow = "(no texture source -> glow only)"; return; }

            string baseName = name;
            if (baseName.EndsWith("_d", StringComparison.OrdinalIgnoreCase))
                baseName = baseName.Substring(0, baseName.Length - 2);

            _texD = LoadTextureResource(name);
            _texN = LoadTextureResource(baseName + "_n");
            _texS = LoadTextureResource(baseName + "_s") ?? LoadTextureResource(baseName + "_s_");

            _texHow = $"diffuse={(_texD != null ? "ok" : "MISS")} normal={(_texN != null ? "ok" : "MISS")} specular={(_texS != null ? "ok" : "MISS")}";
            if (_texD == null)
                DebugLogger.Log($"[IceFx] 🔴 贴图源 '{name}' 找不到 —— 用 custom.ice tex <别的名字> 换一个（此时只上自发光）");
        }

        /// <summary>按资源名取贴图：先当贴图找，再退一步当材质找（取它的 diffuse 槽）。</summary>
        private static Texture LoadTextureResource(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            try { Texture t = Texture.GetFromResource(name); if (t != null) return t; } catch (Exception) { }
            try { Texture t = Texture.CheckAndGetFromResource(name); if (t != null) return t; } catch (Exception) { }
            try
            {
                Material m = Material.GetFromResource(name);
                if (m != null) return m.GetTexture(Material.MBTextureType.DiffuseMap);
            }
            catch (Exception) { }
            return null;
        }

        private static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }

        private static int SafeInt(Func<int> f) { try { return f(); } catch (Exception) { return -1; } }
        private static bool SafeBool(Func<bool> f) { try { return f(); } catch (Exception) { return false; } }
        private static uint SafeUInt(Func<uint> f) { try { return f(); } catch (Exception) { return 0u; } }
    }
}
