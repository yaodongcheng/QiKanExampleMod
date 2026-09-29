// ═══════════════════════════════════════════════════════════════════════════
// 贴花特效：熔岩地表（流动）+ 结冰水面（静态）—— 控制台入口 custom.decal_fx
// （2026-09-29，接上一轮「路线 B 网格贴花」的实机验证结论）
//
// 【要解决什么】
//   上一轮验通了：自己画的 PNG → 导入 → decal 材质 → `decal_mesh` → 地面上的贴花。
//   这一轮要的是**会动的贴花**（熔岩要流动），而贴花这条路天生难动：
//     · 贴花 shader **自身没有任何时间项** —— `g_time_var` 在 decal 路径里根本没被用
//       （反编译 deferred_decal.rsh 实证；唯一用到它的是被注释掉的 brush 分支）。
//     · 材质 flag `use_animated_texture_coords` **贴花吃不到**（只有 pbr_standart 等几支实现了）；
//       而 **`use_texture_sweep` 贴花可以吃** —— 贴花族的 `deferred_blood_terrain` 就读它（见下面 B 路线）。
//   ⇒ 缺省做法：**C# 每帧写 UV**。写的入口是
//     `Mesh.SetVectorArgument(sx, sy, ox, oy)` —— 就是 prefab 里那个 `argument="…"` 的运行时版。
//
// 【为什么"推 UV"就能流动】（两条实证，缺一不可）
//   ① shader 里贴图坐标 = `足迹UV × uv_scale + uv_offset`（deferred_decal.rsh:57），
//      所以改 offset 就是平移贴图；
//   ② 采样器是 **WRAP** —— 引擎根签名里 s5（= anisotropic_sampler，贴花 diffuse 用的就是它）
//      明写 `TEXTURE_ADDRESS_WRAP`（Shaders/Sources/root_signature.rsh）
//      ⇒ 只要贴图**四边无缝**，offset 到 1.0 回绕时看不出接缝，可以无限循环流动。
//
// 【熔岩的分层：谁滚、谁钉住】（2026-09-29 用户定的模型 —— 这才是熔岩的物理关系）
//   **岩壳是静止的（裂开就不动了），流动的是岩壳底下那层岩浆，只能从缝里看见。** 于是：
//     · 岩浆层（`lwn_decal_lava_glow`，AddAlpha）—— **滚动**、**全铺满**、无形状；
//     · 岩壳层（`lwn_decal_lava_crust`，Modulate）—— **静止**，alpha = 1 岩板 / 0 裂缝。
//   哪层不滚由**子节点名**判定（见 StaticLayerMarkers）：名字含 crust/rock/water 的层钉住不动。
//   🔴 "让整个裂纹网跟着一起滚"是错的 —— 看着像"地上滑动着一张发光网"。
//   🎨 附带收益：岩壳静止 ⇒ 它的贴图边缘羽化**不会漂移** ⇒ **足迹外沿可以做软边**
//      （会滚的层做不到：羽化位置每帧都在变，硬边是数学必然）。
//   结冰水面两层本来就全静止，走常规做法：形状与羽化直接烘进贴图。
//
// 【两条驱动路线 —— 材质选哪条就用哪条】
//   A. **本类每帧推 VA1**（默认）：任何 decal shader 都吃。`custom.decal_fx lava`
//   B. **引擎自走**（`use_texture_sweep`）：材质必须是读这个 flag 的贴花 shader
//      （`deferred_blood_terrain`，见 Shaders/Sources/deferred_blood_terrain.rsh:94），
//      走 `uv += VA2.xy * g_time_var * 0.1` 由引擎全局时间推进 ⇒ C# 只写一次 VA2。
//      `custom.decal_fx lava sweep`
//   🔴 两条互斥，判断方法也简单：**B 模式下如果贴花是静止的，就说明材质挂的不是那条 shader**
//      （普通 `decal` shader 根本不读 VA2）—— 这正是 B 模式的价值，一眼分辨材质对不对。
//   先例：阴魔斩（`lwn_yinmo_crescent`）就是靠这个 flag 流动的（`shaderFlags` 里有 `use_texture_sweep`），
//   只不过它是网格 shader（pbr_standart 族），**网格 shader 不能拿来画贴花**
//   （挂到 `decal_mesh` 上会被当普通网格画成一个立方体）。
//
// 【用法】（游戏内 `~` 控制台；返回文本纯英文）
//   custom.decal_fx                          看状态（放了几片、走哪条驱动、滚到哪了）
//   custom.decal_fx lava [尺寸米] [距离米]     召唤流动熔岩（默认 8 米 / 眼前 3 米）
//   custom.decal_fx lava sweep               同上但走引擎自走（材质须是 use_texture_sweep 那条 shader）
//   custom.decal_fx ice  [尺寸米] [距离米]     召唤结冰水面（默认 6 米 / 眼前 3 米，静态）
//   custom.decal_fx speed 0.4                锁流动速度倍率（0 = 冻住，方便静观贴图；两种驱动都生效）
//   custom.decal_fx clear                    收掉本命令放的（只隐藏，引擎没有删单张的 API）
//
// 🔴 两条项目纪律：返回文本**纯英文**（显示在游戏内控制台）；**首参可弃**
//    （认不出的当占位符忽略，回落默认并在返回里注明 —— 实测引擎在"不填参数"时可能
//     压根不触发命令，所以用户习惯随手补个 `1`）。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.CampaignMode
{
    /// <summary>
    /// 贴花特效本体（静态状态 + 操作）。做成静态类是因为**大地图没有 Mission**
    /// （战役地图上也想放熔岩）；Mission 侧由 <see cref="DecalFxBehavior"/> 每帧调 <see cref="Tick"/>。
    /// </summary>
    public static class DecalFx
    {
        /// <summary>prefab 子节点里烘的默认边长（米），见 Taikou/Prefabs/lwn_decal_*.xml。</summary>
        public const float LavaDefaultSize = 8f;

        /// <inheritdoc cref="LavaDefaultSize"/>
        public const float IceDefaultSize = 6f;

        /// <summary>基准流速：贴图单位/秒。1.0 = 每秒滚过一整张贴图。0.14 ⇒ 约 7 秒一轮。</summary>
        private const float FlowSpeed = 0.14f;

        /// <summary>
        /// 引擎自走（`use_texture_sweep`）的速度系数。那个 shader 的公式是
        /// `uv += VA2.xy * g_time_var * 0.1` ⇒ VA2=1.4 时约 7 秒一轮，和 <see cref="FlowSpeed"/> 对齐。
        /// </summary>
        private const float SweepK = 1.4f;

        /// <summary>流动方向（贴图空间，单位向量化前的权重）：斜着走，单轴看着像传送带。</summary>
        private const float DirX = 0.86f;
        private const float DirY = 0.51f;

        private sealed class Layer
        {
            public Mesh Mesh;
            /// <summary>false = 这一层**不推 UV**（静止层：岩壳/冰面这类"形状本来就该钉住"的层）。</summary>
            public bool Scroll;
        }

        private sealed class Entry
        {
            public GameEntity Entity;
            public readonly List<Layer> Layers = new List<Layer>();
            public string Kind;
            public bool Animated;

            /// <summary>true = 交给引擎自走（写一次 VA2，之后不用管）；false = 本类每帧推 VA1。</summary>
            public bool EngineSweep;

            public float OffX;
            public float OffY;

            public int MeshCount => Layers.Count;
        }

        /// <summary>
        /// 🔴 **哪一层不该滚动**：名字里带这些片段的子节点是「静止层」。
        ///
        /// 熔岩的结构是「**岩壳静止 + 底下岩浆流动**」（岩壳裂开就不动了，只有缝里的岩浆在流）
        /// ⇒ prefab 里 `lwn_decal_lava_crust` 这层必须**钉住不动**，否则看着像"地上滑动着一张发光网"。
        /// 冰面两层本来就全静止，不受影响。
        /// </summary>
        private static readonly string[] StaticLayerMarkers = { "crust", "rock", "water" };

        private static bool IsStaticLayer(string entityName)
        {
            if (string.IsNullOrEmpty(entityName)) return false;
            for (int i = 0; i < StaticLayerMarkers.Length; i++)
                if (entityName.IndexOf(StaticLayerMarkers[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private static readonly List<Entry> Entries = new List<Entry>();
        private static Scene _scene;
        private static float _speedScale = 1f;
        private static int _seq;

        public static string Describe()
        {
            if (Entries.Count == 0)
                return string.Format(CultureInfo.InvariantCulture,
                    "no decal. speedMul={0:F2}. usage: custom.decal_fx <lava|ice> [size_m] [dist_m] | speed <x> | clear",
                    _speedScale);
            int meshes = 0;
            for (int i = 0; i < Entries.Count; i++) meshes += Entries[i].MeshCount;
            return string.Format(CultureInfo.InvariantCulture,
                "{0} decal(s) / {1} mesh(es). speedMul={2:F2}. offsets: {3}",
                Entries.Count, meshes, _speedScale, Offsets());
        }

        private static string Offsets()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < Entries.Count; i++)
            {
                Entry e = Entries[i];
                if (i > 0) sb.Append(' ');
                sb.Append(e.Kind).Append('[')
                  .Append(e.EngineSweep ? "sweep(VA2)" : (e.Animated ? "C#(VA1) " : "static   "))
                  .Append(e.OffX.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                  .Append(e.OffY.ToString("F3", CultureInfo.InvariantCulture)).Append(']');
            }
            return sb.ToString();
        }

        // ── 场景解析（沿用 NavMeshDebugRenderer / TerrainExportCommands 的既有范式） ──

        private static Scene ResolveScene(out string tag, out Vec3 anchor, out string err)
        {
            tag = "(none)";
            anchor = Vec3.Zero;
            err = null;

            Mission mission = Mission.Current;
            if (mission != null && mission.Scene != null)
            {
                try { tag = "mission:" + (string.IsNullOrEmpty(mission.SceneName) ? "?" : mission.SceneName); }
                catch { tag = "mission:?"; }
                if (Agent.Main != null) anchor = Agent.Main.Position;
                return mission.Scene;
            }

            if (Campaign.Current != null && Campaign.Current.MapSceneWrapper is SandBox.MapScene mapScene
                && mapScene.Scene != null)
            {
                tag = "campaign_map";
                MobileParty party = MobileParty.MainParty;
                if (party != null)
                {
                    Vec2 p = V.Pos(party);
                    anchor = new Vec3(p.X, p.Y, 0f);
                }
                return mapScene.Scene;
            }

            err = "no active scene (neither a mission nor a loaded campaign map).";
            return null;
        }

        /// <summary>锚点水平朝前方向（Mission 取玩家脸朝向；大地图无朝向 → +X）。</summary>
        private static Vec3 Forward(float distance)
        {
            Vec3 look = Vec3.Zero;
            Mission mission = Mission.Current;
            if (mission != null && Agent.Main != null)
            {
                try { look = Agent.Main.LookDirection; } catch { look = Vec3.Zero; }
            }
            Vec3 flat = new Vec3(look.X, look.Y, 0f);
            if (flat.Length < 1e-3f) flat = new Vec3(1f, 0f, 0f);
            flat.Normalize();
            return flat * distance;
        }

        /// <summary>取地面高度；失败/返回哨兵值就退回 fallbackZ。</summary>
        private static float GroundZ(Scene scene, Vec3 pos, float fallbackZ)
        {
            try
            {
                float z = scene.GetGroundHeightAtPositionMT(new Vec3(pos.X, pos.Y, fallbackZ + 50f),
                                                            BodyFlags.CommonCollisionExcludeFlags);
                if (float.IsNaN(z) || z > 1e5f || z < -1e5f) return fallbackZ;
                return z;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[DecalFx] ground height failed, fallback {fallbackZ:F1}: {ex.Message}");
                return fallbackZ;
            }
        }

        // ── 召唤 ──────────────────────────────────────────────────────────────

        public static string Spawn(string kind, float size, float distance, bool sweepMode)
        {
            string prefab = "lwn_decal_" + kind;
            Scene scene = ResolveScene(out string tag, out Vec3 anchor, out string err);
            if (scene == null) return "error: " + err;

            if (!GameEntity.PrefabExists(prefab))
                return $"error: prefab '{prefab}' not found. (Taikou module must be in the launch list, and Prefabs/{prefab}.xml must exist.)";

            float def = kind == "ice" ? IceDefaultSize : LavaDefaultSize;
            if (size <= 0f) size = def;

            Vec3 pos = anchor + Forward(distance);
            pos = new Vec3(pos.X, pos.Y, GroundZ(scene, pos, anchor.Z));

            // 尺寸靠**根节点缩放**下发；子节点里烘的 8.0/6.0 是默认值（根缩放若失效也还能用）。
            // 🔴 Z 不参与缩放：子节点自带 scale Z=0.5（上下各留 25cm 地形容差），跟着放大会变厚。
            MatrixFrame frame = MatrixFrame.Identity;
            frame.origin = pos;
            float k = size / def;
            frame.Scale(new Vec3(k, k, 1f));

            ClearInternal();   // 同一时刻只留一批，免得前后重叠糊成一片

            GameEntity ent;
            try { ent = GameEntity.Instantiate(scene, prefab, frame); }
            catch (Exception ex)
            {
                DebugLogger.Log($"[DecalFx] Instantiate('{prefab}') 异常: {ex}");
                return $"error: Instantiate '{prefab}' threw: {ex.Message}";
            }
            if (ent == null) return $"error: Instantiate '{prefab}' returned null.";

            Entry e = new Entry { Entity = ent, Kind = kind, Animated = kind == "lava", EngineSweep = sweepMode && kind == "lava" };
            CollectMeshes(ent, e);
            Entries.Add(e);
            _scene = scene;
            _seq++;

            if (e.EngineSweep) ApplySweep(e);

            DebugLogger.Log($"[DecalFx] spawn #{_seq} '{prefab}' size={size:F1} dist={distance:F1} scene={tag} "
                          + $"pos=({pos.X:F2},{pos.Y:F2},{pos.Z:F2}) meshes={e.MeshCount} layers={MovingLayers(e)}/{e.MeshCount} "
                          + $"anim={e.Animated} "
                          + $"mode={(e.EngineSweep ? "engine_sweep(VA2)" : (e.Animated ? "per_frame(VA1)" : "static"))}");

            if (e.MeshCount == 0)
                return $"warn: spawned '{prefab}' but found 0 decal meshes under it (nothing will render or animate). scene={tag}";

            return string.Format(CultureInfo.InvariantCulture,
                "OK: {0} #{1} size={2:F1}m at ({3:F1},{4:F1},{5:F1}) scene={6} meshes={7} drive={8}. "
                + "close the console (~) and look ahead. | status: {9}",
                kind, _seq, size, pos.X, pos.Y, pos.Z, tag, e.MeshCount, DriveName(e), Describe());
        }

        private static string DriveName(Entry e)
            => e.EngineSweep ? "engine texture-sweep (VA2 set once)"
             : e.Animated ? "per-frame VA1 (C# scroll)"
             : "static";

        /// <summary>
        /// 引擎自走模式：把流动速度写进 **VA2.xy**，之后由 shader 里的
        /// `atlassed_texture_coord.xy += VA2.xy * g_time_var * 0.1` 自己推进（g_time_var = 引擎全局时间）。
        ///
        /// 🔴 这条只有在**材质用的是读 `use_texture_sweep` 的贴花 shader**（如 `deferred_blood_terrain`）
        ///    时才有画面效果。用的是普通 `decal` shader 的话 VA2 没人读 ⇒ **贴花会是静止的**
        ///    （这正是本模式的价值：一眼分辨材质到底挂对了没有）。
        /// </summary>
        private static void ApplySweep(Entry e)
        {
            float k = SweepK * _speedScale;
            for (int i = 0; i < e.Layers.Count; i++)
            {
                if (!e.Layers[i].Scroll) continue;          // 静止层不参与自走
                try { e.Layers[i].Mesh.SetVectorArgument2(DirX * k, DirY * k, 0f, 0f); }
                catch (Exception ex) { DebugLogger.Log($"[DecalFx] 写 VA2 异常: {ex.Message}"); }
            }
        }

        /// <summary>把实体自己 + 所有子孙的 MetaMesh 里的 Mesh 全收进来（流动靠写它们的 VA1）。
        /// 每格记一个"要不要滚"的开关 —— 判定依据是**子节点名**（见 <see cref="StaticLayerMarkers"/>）。</summary>
        private static void CollectMeshes(GameEntity root, Entry e)
        {
            List<GameEntity> all = new List<GameEntity>();
            try { root.GetChildrenRecursive(ref all); }
            catch (Exception ex) { DebugLogger.Log($"[DecalFx] GetChildrenRecursive 异常: {ex.Message}"); }
            all.Insert(0, root);

            for (int i = 0; i < all.Count; i++)
            {
                GameEntity g = all[i];
                if (g == null) continue;

                string name = null;
                try { name = g.Name; } catch { name = null; }
                bool scroll = !IsStaticLayer(name);

                int n;
                try { n = g.MultiMeshComponentCount; } catch { continue; }
                for (int j = 0; j < n; j++)
                {
                    try
                    {
                        MetaMesh mm = g.GetMetaMesh(j);
                        if (mm == null || !mm.IsValid) continue;
                        int mc = mm.MeshCount;
                        for (int k = 0; k < mc; k++)
                        {
                            Mesh mesh = mm.GetMeshAtIndex(k);
                            if (mesh != null) e.Layers.Add(new Layer { Mesh = mesh, Scroll = scroll });
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"[DecalFx] 取 MetaMesh/Mesh 异常: {ex.Message}");
                    }
                }
            }
        }

        // ── 每帧 ──────────────────────────────────────────────────────────────

        /// <summary>由 <see cref="DecalFxBehavior.OnMissionTick"/> 每帧调（无 Mission 时 Campaign 侧另有入口）。</summary>
        public static void Tick(float dt)
        {
            if (Entries.Count == 0) return;

            Mission mission = Mission.Current;
            if (mission == null || mission.Scene != _scene)
            {
                // 换场景了：旧实体已经不归我们管，别再去碰（native 悬垂指针）。
                Entries.Clear();
                return;
            }

            float s = FlowSpeed * _speedScale;
            if (s <= 0f) return;                       // 冻住：不写，停在当前相位
            if (dt <= 0f) dt = 0.016f;
            if (dt > 0.25f) dt = 0.25f;                // 卡帧别让贴图瞬移

            for (int i = 0; i < Entries.Count; i++)
            {
                Entry e = Entries[i];
                if (!e.Animated || e.EngineSweep) continue;   // 引擎自走的那些不用我们推
                // 斜向流动：单一轴向看着像传送带。1.0 处回绕 —— 贴图无缝，接缝看不出来。
                e.OffX = Wrap01(e.OffX + DirX * s * dt);
                e.OffY = Wrap01(e.OffY + DirY * s * dt);
                for (int j = 0; j < e.Layers.Count; j++)
                {
                    if (!e.Layers[j].Scroll) continue;      // 🔴 岩壳那类静止层：钉住不动
                    try { e.Layers[j].Mesh.SetVectorArgument(1f, 1f, e.OffX, e.OffY); }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"[DecalFx] 写 VA1 异常: {ex.Message}");
                        e.Layers.RemoveAt(j);
                        j--;
                    }
                }
            }
        }

        /// <summary>这个实体里有几层是会滚的（其余 = 静止层，比如岩壳）。</summary>
        private static int MovingLayers(Entry e)
        {
            int n = 0;
            for (int i = 0; i < e.Layers.Count; i++) if (e.Layers[i].Scroll) n++;
            return n;
        }

        private static float Wrap01(float v)
        {
            v = v - (float)Math.Floor(v);
            return v < 0f ? v + 1f : v;
        }

        // ── 手改 / 收掉 ───────────────────────────────────────────────────────

        public static string SetSpeed(float mul)
        {
            if (float.IsNaN(mul) || float.IsInfinity(mul)) return "error: speed must be a number.";
            _speedScale = Math.Max(0f, Math.Min(5f, mul));
            // 引擎自走的那批速度写在 VA2 里，得跟着重写一次（0 = 速度归零 = 冻住）。
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].EngineSweep) ApplySweep(Entries[i]);
            DebugLogger.Log($"[DecalFx] speedMul = {_speedScale:F2}");
            return string.Format(CultureInfo.InvariantCulture,
                "OK: flow speed multiplier = {0:F2} (0 = frozen, 1 = default ~7s per texture lap). | {1}",
                _speedScale, Describe());
        }

        /// <summary>收掉。引擎没暴露「删单张贴花」的 API（`Scene.ClearDecals()` 会把全场贴花
        /// 一起清光，连原版血渍都没），所以只隐藏 —— 眼不见为净，也不误伤别人的贴花。</summary>
        public static string Clear()
        {
            int n = Entries.Count;
            ClearInternal();
            DebugLogger.Log($"[DecalFx] clear: hid {n} decal entity(ies)");
            return string.Format(CultureInfo.InvariantCulture, "OK: hid {0} decal entity(ies).", n);
        }

        private static void ClearInternal()
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                try { Entries[i].Entity?.SetVisibilityExcludeParents(false); }
                catch (Exception ex) { DebugLogger.Log($"[DecalFx] hide 异常: {ex.Message}"); }
            }
            Entries.Clear();
        }

        /// <summary>Mission 结束时把引用清干净（实体随场景一起没了，留着就是悬垂）。</summary>
        public static void OnSceneGone() => Entries.Clear();
    }

    /// <summary>挂在 Mission 上，每帧驱动流动。</summary>
    public class DecalFxBehavior : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        public override void OnMissionTick(float dt) => DecalFx.Tick(dt);

        protected override void OnEndMission() => DecalFx.OnSceneGone();
    }

    /// <summary>控制台入口。</summary>
    public static class DecalFxCommands
    {
        private const float DefaultDistance = 3f;

        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("decal_fx", "custom")]
        public static string ExecuteDecalFx(List<string> args)
        {
            // ── 解析：首参可弃。认得出 kind 就当 kind，认不出的数字当尺寸/距离，其余当占位符忽略 ──
            string kind = null;
            string sub = null;
            bool sweep = false;
            List<float> nums = new List<float>();
            List<string> junk = new List<string>();

            if (args != null)
            {
                for (int i = 0; i < args.Count; i++)
                {
                    string a = (args[i] ?? string.Empty).Trim();
                    if (a.Length == 0) continue;
                    string lo = a.ToLowerInvariant();

                    if (lo == "clear" || lo == "speed" || lo == "status" || lo == "info")
                    {
                        sub = lo == "info" ? "status" : lo;
                        continue;
                    }
                    if (lo == "sweep")
                    {
                        sweep = true;      // 交给引擎自走（材质必须是读 use_texture_sweep 的贴花 shader）
                        continue;
                    }
                    if (kind == null && (lo == "lava" || lo == "ice"))
                    {
                        kind = lo;
                        continue;
                    }
                    float v;
                    if (float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    {
                        nums.Add(v);
                        continue;
                    }
                    junk.Add(a);
                }
            }

            string note = junk.Count > 0
                ? string.Format(CultureInfo.InvariantCulture, " | note: ignored placeholder(s) '{0}'", string.Join("','", junk.ToArray()))
                : string.Empty;

            if (sub == "status")
                return "OK: " + DecalFx.Describe() + note;
            if (sub == "clear")
                return DecalFx.Clear() + note;
            if (sub == "speed")
            {
                if (nums.Count == 0) return "Usage: custom.decal_fx speed <multiplier 0..5> (0 = frozen)" + note;
                return DecalFx.SetSpeed(nums[0]) + note;
            }

            if (args == null || args.Count == 0)
                return "OK: " + DecalFx.Describe() + " | usage: custom.decal_fx <lava|ice> [size_m] [dist_m] | speed <x> | clear";

            bool assumed = false;
            if (kind == null) { kind = "lava"; assumed = true; }
            string anote = assumed ? " [note: no kind given -> lava]" : string.Empty;

            float size = nums.Count >= 1 ? nums[0] : 0f;
            float dist = nums.Count >= 2 ? nums[1] : DefaultDistance;
            if (dist < 0f) dist = 0f;

            return DecalFx.Spawn(kind, size, dist, sweep) + note + anote;
        }
    }
}
