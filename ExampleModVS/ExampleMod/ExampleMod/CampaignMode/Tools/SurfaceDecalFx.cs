// ═══════════════════════════════════════════════════════════════════════════
// 元素地表贴花：**生命周期 + 元素过渡** 控制器 —— 控制台入口 `custom.surface`
// （2026-09-30，接《元素地表系统》方案 §三 / §六 的落地件）
//
// 🔴🔴 **铁律：贴花只做单层**（2026-09-30 用户裁定）
//   · **一个 prefab 只准有一个 `decal_mesh`**。多层的路**封死，以后不准走**。
//   · **双层有且只有一种合法形态**：**两个不同元素的单层贴花互相重叠做渐变**
//     —— 那是**两个实体**（各单层），不是一个 prefab 里塞两层。
//   · 本类强制这条：实例化后发现 decal mesh > 1 个，**只用第一个**并在日志里报警。
//   ⇒ 所以默认 prefab = `customDecalTest`（**单层**），不是 `lwn_decal_lava`（那是两层，已弃用）。
//
// 【要解决什么】
//     ① **生命周期管理渐变**：一片贴花要能淡入 → 存活 → 淡出 → 回收，而不是"啪"地出现/消失
//     ② **不同元素的过渡**：火和水交界处要交叉淡入，而不是一条硬边
//        （=`custom.surface twin`，两个单层实体互相退让 alpha —— 这就是唯一合法的"双层"）
//
// 【怎么做到的 —— 押在一个引擎能力上，且已经实机验通】
//   🔴🔴 **驱动量走 RGB，不走 alpha**（2026-09-30 实机四连测定案）：
//     · `Mesh.Color` 就是 shader 里的 `g_mesh_factor_color`（native 桥接实证 + 染色实测双证）
//     · shader 里 `albedo_color.rgb *= g_mesh_factor_color.rgb` —— **无条件生效**
//     · **alpha 与这一层无关**：只改 alpha 一点不变；改成蓝基色还会直接消失
//     实机证据：`tint ff0000` 变红 ✓ · `tint 00ff00` 变绿 ✓ · `tint 0000ff` **变黑消失** ✓
//     （焦土贴图是暖色、蓝分量≈0 ⇒ 蓝×蓝≈0 ⇒ 加法加 0 = 看不见）· `lock 0.1`（只改 alpha）**无效** ✓
//   ⇒ **淡入 / 淡出 / 过渡 全部写成 RGB 乘法**：`final_rgb = 基色 × 空间权重 × 寿命因子`。
//     乘法到 0 = 加法加 0 = 看不见 = **淡出**。这比 alpha 好用：一个旋钮管三件事。
//
// 【两条曲线分开算，最后相乘】
//   驱动量 = **权重**（空间：离异类边界多远） × **寿命因子**（时间：淡入/存活/淡出）
//     · 权重 = 1 − 0.5 × 与"异类贴花"的最大重叠比  ⇒ 交界处交叉淡入
//     · 寿命 = 淡入爬升 → 满值存活 → 淡出下降      ⇒ 出生/消亡都是渐变
//
// 【用法】（游戏内 `~` 控制台；返回文本纯英文）
//   custom.surface                                状态
//   custom.surface spawn <材质> [尺寸米] [寿命秒]    造**一片单层**贴花（寿命 0 = 永久）
//   custom.surface twin  <材质A> <材质B> [尺寸] [间距] 两个不同元素的单层片叠一起 ⇒ **验过渡看这个**
//   custom.surface probe                            🔴 列出每片的层名 / 材质名 / 当前颜色
//   custom.surface layer <关键词|-all>               只影响名字或材质含该词的层
//   custom.surface fade  <淡入秒> <淡出秒>           改生命周期曲线（对已存在的片立刻生效）
//   custom.surface band  <米>                       过渡带宽度
//   custom.surface lock  <0~1|-1>                   锁死驱动量（0=看不见 / 1=满，-1 解锁）
//   custom.surface clear                            清空
//
// 【用法补充】custom.surface tint <RRGGBB|->  设基色（淡出乘在它上面）
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.CampaignMode.Tools
{
    public static class SurfaceDecalFx
    {
        // ── 可调参数 ──────────────────────────────────────────────────────
        private static float _fadeIn = 0.6f;      // 淡入时长（秒）
        private static float _fadeOut = 2.0f;     // 淡出时长（秒）
        private static float _band = 1.5f;        // 过渡带宽（米）
        private static float _lockAlpha = -1f;    // ≥0 = 锁死这个 alpha（地基实验用）；-1 = 正常

        // 🔴 **单层 prefab**（铁律：一个 prefab 只准一个 decal_mesh）。
        //    两个现成的单层 prefab，**混合模式不同**，用来分别验两条地表的驱动：
        //      `customDecalTest`         → 材质 `lwn_manual_blood_decal_1` = **add_alpha**（加法：火/光）
        //      `customDecalTest_vanilla` → 材质 `lwn_blood_terrain_decal_3` = **modulate**（压暗：焦土/油）
        //    `lwn_decal_lava` / `lwn_decal_ice` = 两层，**已弃用不准再用**。
        private static string _prefab = "customDecalTest";
        private const float DefaultSize = 3f;   // prefab 自带的边长（米）—— 子节点 scale 写的是 3.0
        private const int MaxLayers = 1;        // 超过这个数就报警并只用第一层

        /// <summary>切换用哪个单层 prefab（也就切换了混合模式）。传 "-" 回默认。</summary>
        public static string SetPrefab(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "-") { _prefab = "customDecalTest"; }
            else _prefab = name;
            bool exists = false;
            try { exists = GameEntity.PrefabExists(_prefab); } catch { }
            string blendHint = _prefab.IndexOf("vanilla", StringComparison.OrdinalIgnoreCase) >= 0
                ? " (expected modulate -> replace albedo; RGB fade makes a BLACK patch, not a fade)" : "";
            return $"OK: prefab = {_prefab}{blendHint}"
                 + (exists ? "" : "  ⚠ prefab not found (Taikou module in launch list?)");
        }

        // ── 数据 ──────────────────────────────────────────────────────────
        public sealed class LayerRef
        {
            public Mesh Mesh;
            public string Name = "";      // 子节点名（prefab 里写的，如 lwn_decal_lava_glow）
            public string Material = "";  // 材质名
        }

        public sealed class Patch
        {
            public GameEntity Entity;
            public readonly List<LayerRef> Layers = new List<LayerRef>();
            public string Element = "";   // 元素名（= 材质名）
            public Vec3 Center;
            public float Radius;
            public float Life;            // 剩余寿命（秒）；MaxLife<=0 = 永久
            public float MaxLife;
            public float Weight = 1f;     // 空间权重（每帧重算）
        }

        private static readonly List<Patch> Patches = new List<Patch>();
        private static int _seq;
        private static float _lastAlpha = -1f;

        // ── 场景辅助 ──────────────────────────────────────────────────────
        private static Vec3 FlatForward(float distance)
        {
            Vec3 look = Vec3.Zero;
            try { if (!CameraLook.TryGet(out look)) look = Vec3.Forward; } catch { look = Vec3.Forward; }
            Vec3 flat = new Vec3(look.X, look.Y, 0f);
            if (flat.Length < 1e-3f) flat = new Vec3(1f, 0f, 0f);
            flat.Normalize();
            return flat * distance;
        }

        private static float GroundZ(Scene scene, Vec3 pos, float fallbackZ)
        {
            try
            {
                float z = scene.GetGroundHeightAtPositionMT(new Vec3(pos.X, pos.Y, fallbackZ + 50f),
                                                            BodyFlags.CommonCollisionExcludeFlags);
                if (float.IsNaN(z) || z > 1e5f || z < -1e5f) return fallbackZ;
                return z;
            }
            catch { return fallbackZ; }
        }

        // ── 召唤 ──────────────────────────────────────────────────────────
        /// <summary>造一片。返回 false 表示失败，err 里是原因。</summary>
        public static bool Spawn(string prefab, float radius, float life, float distance,
                                 out Patch patch, out string err)
        {
            patch = null; err = null;

            Mission mission = Mission.Current;
            if (mission == null) { err = "no mission (enter a scene first)."; return false; }
            Agent main = mission.MainAgent;
            if (main == null) { err = "no main agent to anchor on."; return false; }

            Vec3 pos = main.Position + FlatForward(distance);
            return SpawnAt(prefab, radius, life, pos, out patch, out err);
        }

        /// <summary>
        /// 在指定中心**侧移**造一片（`twin` 用）：B 从 A 的落点沿相机右向偏开 side 米。
        /// 两片共用同一次地面高度计算，避免各自算导致高度错位。
        /// </summary>
        public static bool SpawnOffset(string prefab, float radius, float life,
                                       Vec3 center, float side, out Patch patch, out string err)
        {
            Vec3 right = Vec3.Zero;
            try
            {
                Vec3 look;
                if (CameraLook.TryGet(out look)) { right = Vec3.CrossProduct(Vec3.Up, look); }
            }
            catch { }
            Vec3 flat = new Vec3(right.X, right.Y, 0f);
            if (flat.Length < 1e-3f) flat = new Vec3(1f, 0f, 0f);
            flat.Normalize();
            return SpawnAt(prefab, radius, life, center + flat * side, out patch, out err);
        }

        private static bool SpawnAt(string prefab, float radius, float life, Vec3 pos,
                                    out Patch patch, out string err)
        {
            patch = null; err = null;

            Mission mission = Mission.Current;
            if (mission == null) { err = "no mission."; return false; }
            Scene scene = mission.Scene;
            if (scene == null) { err = "mission has no scene."; return false; }

            if (string.IsNullOrEmpty(prefab)) prefab = _prefab;
            if (!GameEntity.PrefabExists(prefab))
            {
                err = $"prefab '{prefab}' not found (Taikou module must be in the launch list).";
                return false;
            }

            if (radius <= 0f) radius = DefaultSize;
            pos = new Vec3(pos.X, pos.Y, GroundZ(scene, pos, pos.Z));

            MatrixFrame frame = MatrixFrame.Identity;
            frame.origin = pos;
            float k = radius / DefaultSize;
            frame.Scale(new Vec3(k, k, 1f));   // Z 不缩放（子节点自带厚度容差）

            GameEntity ent;
            try { ent = GameEntity.Instantiate(scene, prefab, frame); }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Surface] Instantiate('{prefab}') 异常: {ex}");
                err = $"Instantiate threw: {ex.Message}";
                return false;
            }
            if (ent == null) { err = "Instantiate returned null."; return false; }

            Patch p = new Patch
            {
                Entity = ent,
                Element = prefab,
                Center = pos,
                Radius = radius,
                Life = life > 0f ? life : -1f,
                MaxLife = life > 0f ? life : -1f,
            };
            CollectMeshes(ent, p, prefab);
            ApplyMaterial(p, null);

            if (p.Layers.Count == 0)
            {
                err = $"spawned but found 0 decal meshes under '{prefab}'.";
                try { ent.SetVisibilityExcludeParents(false); } catch { }
                return false;
            }

            Patches.Add(p);
            _seq++;
            RecomputeWeights();
            Tick(0f);

            DebugLogger.Log($"[Surface] spawn #{_seq} '{p.Element}' r={radius:F1} life={life:F1} "
                          + $"pos=({pos.X:F2},{pos.Y:F2},{pos.Z:F2}) meshes={p.Layers.Count} patches={Patches.Count}");
            patch = p;
            return true;
        }

        private static void CollectMeshes(GameEntity root, Patch p, string prefabName)
        {
            List<GameEntity> all = new List<GameEntity>();
            try { root.GetChildrenRecursive(ref all); }
            catch (Exception ex) { DebugLogger.Log($"[Surface] GetChildrenRecursive 异常: {ex.Message}"); }
            all.Insert(0, root);

            for (int i = 0; i < all.Count; i++)
            {
                GameEntity g = all[i];
                if (g == null) continue;
                string gname = "";
                try { gname = g.Name ?? ""; } catch { }
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
                            if (mesh == null) continue;
                            string mat = "";
                            try { Material m = mesh.GetMaterial(); mat = m != null ? m.Name : ""; } catch { }
                            p.Layers.Add(new LayerRef { Mesh = mesh, Name = gname, Material = mat });
                        }
                    }
                    catch (Exception ex) { DebugLogger.Log($"[Surface] 取 MetaMesh/Mesh 异常: {ex.Message}"); }
                }
            }

            // 🔴 **单层铁律**：一个 prefab 只准一个 decal mesh。多了就只用第一个 + 报警。
            if (p.Layers.Count > MaxLayers)
            {
                DebugLogger.Log($"[Surface] ⚠ 单层铁律被违反：prefab '{prefabName}' 带 {p.Layers.Count} 个 decal mesh，"
                              + $"只保留第 1 个。多层贴花已废弃（唯一合法的两层 = 两个不同元素的单层片互相重叠）。");
                p.Layers.RemoveRange(MaxLayers, p.Layers.Count - MaxLayers);
            }
        }

        /// <summary>
        /// 换材质 = 换元素。**按名字设**（`Mesh.SetMaterial(string)`）——
        /// 上一轮用 `Material.GetFromResource` + 对象重载**没生效**（probe 里材质名没变），改走名字。
        ///
        /// 若设了 <see cref="_forceBlend"/>，先 `CreateCopy()` 再改混合模式
        /// 🔴 **必须 CreateCopy** —— `GetFromResource` 返回的是**共享实例**，
        ///    直接改它会把全局用同一材质的贴花一起改掉。
        /// </summary>
        private static void ApplyMaterial(Patch p, string materialKey)
        {
            if (string.IsNullOrEmpty(materialKey)) return;

            Material use = null;
            try { use = Material.GetFromResource(materialKey); } catch { }
            if (use == null)
            {
                DebugLogger.Log($"[Surface] material '{materialKey}' not found; keeping prefab default.");
                return;
            }

            for (int i = 0; i < p.Layers.Count; i++)
            {
                Mesh mesh = p.Layers[i].Mesh;
                try
                {
                    if (string.IsNullOrEmpty(_forceBlend))
                    {
                        mesh.SetMaterial(materialKey);          // 按名字设（实测更可靠）
                    }
                    else
                    {
                        Material copy = use.CreateCopy();
                        copy.SetAlphaBlendMode(ParseBlend(_forceBlend));
                        mesh.SetMaterial(copy);
                    }
                }
                catch (Exception ex) { DebugLogger.Log($"[Surface] SetMaterial 异常: {ex.Message}"); }
            }
        }

        private static string _forceBlend = null;   // null = 用材质自带；否则 "Modulate"/"AddAlpha"...

        private static Material.MBAlphaBlendMode ParseBlend(string s)
        {
            if (string.IsNullOrEmpty(s)) return Material.MBAlphaBlendMode.Modulate;
            string k = s.Replace("_", "").Replace("-", "").ToLowerInvariant();
            if (k.StartsWith("add")) return Material.MBAlphaBlendMode.AddAlpha;
            if (k.StartsWith("mod")) return Material.MBAlphaBlendMode.Modulate;
            if (k.StartsWith("mult")) return Material.MBAlphaBlendMode.Multiply;
            if (k.StartsWith("factor")) return Material.MBAlphaBlendMode.Factor;
            try { return (Material.MBAlphaBlendMode)Enum.Parse(typeof(Material.MBAlphaBlendMode), s, true); }
            catch { return Material.MBAlphaBlendMode.Modulate; }
        }

        /// <summary>运行时改混合模式（对**已存在**的片也立刻生效）。传 "-" 恢复材质自带。</summary>
        public static string SetBlend(string mode)
        {
            if (string.IsNullOrEmpty(mode) || mode == "-")
            {
                _forceBlend = null;
                return "OK: blend override cleared (use material's own blend)";
            }
            _forceBlend = mode;

            // 对已存在的片重新套一遍
            int n = 0;
            for (int i = 0; i < Patches.Count; i++)
            {
                Patch p = Patches[i];
                for (int j = 0; j < p.Layers.Count; j++)
                {
                    Mesh mesh = p.Layers[j].Mesh;
                    try
                    {
                        Material cur = mesh.GetMaterial();
                        if (cur == null) continue;
                        Material copy = cur.CreateCopy();
                        copy.SetAlphaBlendMode(ParseBlend(mode));
                        mesh.SetMaterial(copy);
                        n++;
                    }
                    catch (Exception ex) { DebugLogger.Log($"[Surface] blend 切换异常: {ex.Message}"); }
                }
            }
            return $"OK: blend override = {ParseBlend(mode)} (applied to {n} layer(s)). "
                 + "AddAlpha consumes alpha; Modulate does NOT (it replaces albedo).";
        }

        /// <summary>报告一片的每个层：节点名 / 材质名 / **混合模式** / 当前 Color。</summary>
        private static string LayerLine(LayerRef l, int j)
        {
            uint c = 0; try { c = l.Mesh.Color; } catch { }
            string blend = "?";
            try { Material m = l.Mesh.GetMaterial(); if (m != null) blend = m.GetAlphaBlendMode().ToString(); } catch { }
            return $"\n    [{j}] node='{l.Name}'  mat='{l.Material}'  blend={blend}"
                 + $"  color=0x{c.ToString("X8", CultureInfo.InvariantCulture)}"
                 + (LayerMatches(l) ? "  <-- affected" : "  (filtered out)");
        }

        // ── 每帧：寿命推进 → 算权重 → 写 alpha ──────────────────────────────
        public static void Tick(float dt)
        {
            if (Patches.Count == 0) return;

            for (int i = Patches.Count - 1; i >= 0; i--)
            {
                Patch p = Patches[i];
                if (p.MaxLife <= 0f) continue;           // 永久片
                p.Life -= dt;
                if (p.Life <= 0f) { Hide(p); Patches.RemoveAt(i); }
            }
            if (Patches.Count == 0) return;

            RecomputeWeights();

            float sum = 0f;
            for (int i = 0; i < Patches.Count; i++)
            {
                Patch p = Patches[i];
                float a = _lockAlpha >= 0f ? _lockAlpha : (p.Weight * LifeFactor(p));
                a = Clamp01(a);
                sum += a;
                for (int j = 0; j < p.Layers.Count; j++)
                {
                    if (!LayerMatches(p.Layers[j])) continue;   // 层筛选（probe 用）
                    SetColor(p.Layers[j].Mesh, a);
                }
            }
            _lastAlpha = sum / Patches.Count;
        }

        /// <summary>层筛选：空 = 全部；否则只看名字或材质里含这个词的层（如 "glow" / "frost"）。</summary>
        private static string _layerFilter = "";
        private static bool LayerMatches(LayerRef l)
        {
            if (string.IsNullOrEmpty(_layerFilter)) return true;
            return (l.Name ?? "").IndexOf(_layerFilter, StringComparison.OrdinalIgnoreCase) >= 0
                || (l.Material ?? "").IndexOf(_layerFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>寿命因子：淡入爬升 → 满值 → 淡出下降。永久片恒为 1。</summary>
        private static float LifeFactor(Patch p)
        {
            if (p.MaxLife <= 0f) return 1f;
            float age = p.MaxLife - p.Life;
            if (_fadeIn > 0.001f && age < _fadeIn) return Clamp01(age / _fadeIn);
            if (_fadeOut > 0.001f && p.Life < _fadeOut) return Clamp01(p.Life / _fadeOut);
            return 1f;
        }

        /// <summary>
        /// 空间权重 = 1 − 0.5 × 与「异类贴花」的最大重叠比。
        /// 两片完全重叠 ⇒ 各 0.5（五五开）；刚好挨着 ⇒ 接近 1。**同类之间不互相退让。**
        /// </summary>
        private static void RecomputeWeights()
        {
            for (int i = 0; i < Patches.Count; i++) Patches[i].Weight = 1f;

            for (int i = 0; i < Patches.Count; i++)
            {
                Patch a = Patches[i];
                float worst = 0f;
                for (int j = 0; j < Patches.Count; j++)
                {
                    if (i == j) continue;
                    Patch b = Patches[j];
                    if (string.Equals(a.Element, b.Element, StringComparison.OrdinalIgnoreCase)) continue;

                    float d = (a.Center - b.Center).Length;
                    float overlap = (a.Radius + b.Radius) - d;
                    if (overlap <= 0f) continue;
                    float ratio = Clamp01(overlap / Math.Max(0.01f, 2f * _band));
                    if (ratio > worst) worst = ratio;
                }
                a.Weight = 1f - 0.5f * worst;
            }
        }

        /// <summary>
        /// 🔴🔴 **驱动量走 RGB，不走 alpha**（2026-09-30 实机定案）。
        ///
        /// **实机证据**（`custom.surface` 四连测）：
        ///   `tint ff0000` → **变红** ✓ · `tint 00ff00` → **变绿** ✓ ·
        ///   `tint 0000ff` → **变黑消失**（焦土贴图是暖色、蓝分量≈0，蓝×蓝≈0 = 加法加 0）·
        ///   `lock 0.1`（只改 alpha）→ **无效** ✓
        ///
        /// ⇒ 结论：**这一层是加法（add_alpha），加的是 `贴图RGB × factor的RGB`，alpha 不参与混合。**
        ///   `Mesh.Color` 就是 `g_mesh_factor_color`（native 桥接实证 + 染色实测双证）。
        ///
        /// ⇒ 所以**淡入/淡出/过渡全部写成 RGB 乘法**：
        ///   `final_rgb = 基色 × 权重 × 寿命因子`；`= 0` 时加 0 = 看不见 = **淡出**。
        ///   基色由 <see cref="_tint"/> 给（默认白 = 保留材质原色，靠贴图自己带颜色）。
        /// </summary>
        private static void SetColor(Mesh m, float a)
        {
            float k = Clamp01(a);
            uint baseRgb = _tint ?? 0x00FFFFFFu;
            uint r = (uint)Math.Round(((baseRgb >> 16) & 0xFF) * k);
            uint g = (uint)Math.Round(((baseRgb >> 8) & 0xFF) * k);
            uint b = (uint)Math.Round((baseRgb & 0xFF) * k);
            uint rgb = (r << 16) | (g << 8) | b;

            switch (_drive)
            {
                case "alpha":
                    // 只动 alpha，RGB 保持白 —— 用来单独验 alpha 这条路
                    try { m.Color = ((uint)Math.Round(k * 255f) << 24) | 0x00FFFFFFu; } catch { }
                    break;
                case "both":
                    try { m.Color = ((uint)Math.Round(k * 255f) << 24) | rgb; } catch { }
                    break;
                default:   // "rgb"
                    try { m.Color = 0xFF000000u | rgb; } catch { }
                    break;
            }
        }

        private static uint? _tint = null;   // 基色（null = 白）；驱动的浓淡是它 × 权重 × 寿命
        private static string _drive = "rgb"; // "rgb" | "alpha" | "both" —— A/B 用

        /// <summary>
        /// 🔴 **A/B 开关**：把"驱动量写在哪"分开验。
        /// · `rgb`（默认）= 乘法变暗 —— 加法层等效淡出；**modulate 层会变黑斑**
        /// · `alpha` = 只改透明度 —— 两种层都是"消失"；若它生效，淡出就统一走它
        /// · `both` = 两个一起
        /// </summary>
        public static string SetDrive(string mode)
        {
            if (string.IsNullOrEmpty(mode)) return $"OK: drive = {_drive}";
            string k = mode.ToLowerInvariant();
            if (k != "rgb" && k != "alpha" && k != "both")
                return "error: drive must be rgb | alpha | both";
            _drive = k;
            return $"OK: drive = {_drive}. rgb = multiply to black (works on additive layers); "
                 + "alpha = transparency (what modulate layers need).";
        }

        /// <summary>给所有片设一个基色（"ff0000"）。传 "-" 清掉（回白）。</summary>
        public static string SetTint(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex == "-") { _tint = null; return "OK: tint cleared (white base)"; }
            string h = hex.TrimStart('#');
            if (h.Length == 6) h = "FF" + h;
            uint v;
            if (!uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v))
                return $"error: bad hex '{hex}' (want RRGGBB)";
            _tint = v & 0x00FFFFFFu;
            return $"OK: base tint = 0x{_tint.Value:X6} (fade multiplies this). "
                 + "NOTE: a base whose channel is ~0 in the texture will make the decal vanish.";
        }

        // 🔴 引擎没有"删单个实体/贴花"的 API —— 只能隐藏（先例：DecalFx.ClearInternal）
        private static void Hide(Patch p)
        {
            try { p.Entity?.SetVisibilityExcludeParents(false); } catch { }
        }

        // ── 控制台用 ──────────────────────────────────────────────────────
        public static string SetFade(float fin, float fout)
        {
            _fadeIn = Math.Max(0f, fin);
            _fadeOut = Math.Max(0f, fout);
            return $"OK: fadeIn={_fadeIn:F2}s fadeOut={_fadeOut:F2}s (applies to existing patches immediately)";
        }

        public static string SetBand(float b)
        {
            _band = Math.Max(0.05f, b);
            RecomputeWeights();
            return $"OK: transition band = {_band:F2}m";
        }

        /// <summary>
        /// 🔴 **诊断**：把每片贴花的**每个层**列出来 —— 子节点名 / 材质名 / 当前 Color 的 ARGB。
        /// 用来回答"这一层到底是不是 add_alpha""alpha 写进去了没有"。
        /// </summary>
        public static string Probe()
        {
            if (Patches.Count == 0) return "no patch. spawn one first.";
            StringBuilder sb = new StringBuilder();
            sb.Append("layerFilter='").Append(_layerFilter).Append("'  lock=")
              .Append(_lockAlpha >= 0f ? _lockAlpha.ToString("F2", CultureInfo.InvariantCulture) : "off");
            for (int i = 0; i < Patches.Count; i++)
            {
                Patch p = Patches[i];
                sb.Append("\n#").Append(i).Append(" '").Append(p.Element).Append("'  layers=").Append(p.Layers.Count);
                for (int j = 0; j < p.Layers.Count; j++)
                    sb.Append(LayerLine(p.Layers[j], j));
            }
            return sb.ToString();
        }

        /// <summary>层筛选：设成 "glow" 只影响名字/材质含 glow 的层；空串 = 全部。</summary>
        public static string SetLayerFilter(string f)
        {
            _layerFilter = f ?? "";
            return $"OK: layer filter = '{_layerFilter}' (empty = all layers)";
        }

        /// <summary>
        /// 🔴 **三地块布局**：左 A、右 B、**中间是两者的交界带**。
        /// 中心距 = `2 × size × (1 − overlap)`：
        ///   · `overlap = 0`   → 两片**刚好挨着**（没有交界带 = 硬边）
        ///   · `overlap = 0.5` → 重叠一半 ⇒ **中间出现一条明显过渡带**  ← 要看的
        ///   · `overlap = 1`   → 完全重叠（两片叠在一处）
        /// 看到的三个区 = `[A] [A∩B] [B]`。
        /// </summary>
        public static bool SpawnStrip(string prefabA, string prefabB, float size, float overlap,
                                      float distance, out string report, out string err)
        {
            report = null; err = null;
            if (size <= 0f) size = DefaultSize;
            overlap = Clamp01(overlap);

            Mission mission = Mission.Current;
            if (mission == null) { err = "no mission (enter a scene first)."; return false; }
            Agent main = mission.MainAgent;
            if (main == null) { err = "no main agent to anchor on."; return false; }

            Vec3 fwd = FlatForward(1f);
            Vec3 right = Vec3.Zero;
            try { Vec3 look; if (CameraLook.TryGet(out look)) right = Vec3.CrossProduct(Vec3.Up, look); } catch { }
            Vec3 flatR = new Vec3(right.X, right.Y, 0f);
            if (flatR.Length < 1e-3f) flatR = new Vec3(0f, 1f, 0f);
            flatR.Normalize();

            float halfSep = size * (1f - overlap);        // 各自离中心的偏移
            Vec3 center = main.Position + fwd * distance;

            Patch pa, pb;
            if (!SpawnOffset(prefabA, size, 0f, center - flatR * halfSep, 0f, out pa, out err))
                return false;
            if (!SpawnOffset(prefabB, size, 0f, center + flatR * halfSep, 0f, out pb, out err))
                return false;

            float sep = halfSep * 2f;
            float band = Math.Max(0f, size * 2f - sep);   // 中间交界带的宽度
            report = string.Format(CultureInfo.InvariantCulture,
                "OK strip: [A '{0}'] | overlap {1:F0}% -> junction band ~{2:F1}m | [B '{3}']  (centre distance {4:F1}m)",
                prefabA, overlap * 100f, band, prefabB, sep)
                + "\n  left zone = A only | middle zone = A+B cross-fade | right zone = B only"
                + "\n  " + Describe();
            return true;
        }

        /// <summary>解锁驱动（spawn 时自动调，防止上一次的 lock 把寿命曲线旁路掉）。</summary>
        public static string ReleaseLock()
        {
            if (_lockAlpha < 0f) return string.Empty;
            _lockAlpha = -1f;
            return " [note: drive lock released (was sticky)]";
        }

        public static string SetLock(float a)
        {
            if (a < 0f)
            {
                _lockAlpha = -1f;
                return "OK: lock released (drive = weight x life again)";
            }
            _lockAlpha = Clamp01(a);
            return $"OK: drive LOCKED to {_lockAlpha:F2} (RGB = base x this). 0 = invisible, 1 = full.";
        }

        public static string Clear()
        {
            for (int i = 0; i < Patches.Count; i++) Hide(Patches[i]);
            int n = Patches.Count;
            Patches.Clear();
            return $"OK: hid {n} patch(es).";
        }

        public static string Describe()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Patches.Count).Append(" patch(es). ")
              .Append("band=").Append(_band.ToString("F2", CultureInfo.InvariantCulture)).Append("m  ")
              .Append("fadeIn=").Append(_fadeIn.ToString("F2", CultureInfo.InvariantCulture)).Append("s ")
              .Append("fadeOut=").Append(_fadeOut.ToString("F2", CultureInfo.InvariantCulture)).Append("s  ")
              .Append(_lockAlpha >= 0f ? "lock=" + _lockAlpha.ToString("F2", CultureInfo.InvariantCulture) + "  " : "lock=off  ")
              .Append("base=").Append((_tint ?? 0xFFFFFFu).ToString("X6", CultureInfo.InvariantCulture)).Append("  ")
              .Append("prefab=").Append(_prefab).Append("  seq=").Append(_seq)
              .Append(_lastAlpha >= 0f ? "  avgAlpha=" + _lastAlpha.ToString("F2", CultureInfo.InvariantCulture) : "");
            for (int i = 0; i < Patches.Count; i++)
            {
                Patch p = Patches[i];
                sb.Append("\n  #").Append(i).Append(" '").Append(p.Element).Append("'  r=")
                  .Append(p.Radius.ToString("F1", CultureInfo.InvariantCulture)).Append("m  w=")
                  .Append(p.Weight.ToString("F2", CultureInfo.InvariantCulture)).Append("  life=")
                  .Append(p.MaxLife <= 0f ? "inf" : p.Life.ToString("F1", CultureInfo.InvariantCulture))
                  .Append("  layers=").Append(p.Layers.Count);
            }
            return sb.ToString();
        }

        public static void OnSceneGone()
        {
            Patches.Clear();
            _seq = 0;
            _lastAlpha = -1f;
            _layerFilter = "";
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }

    /// <summary>每帧驱动：生命周期推进 + 权重重算 + 写 alpha。</summary>
    public class SurfaceDecalBehavior : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;
        public override void OnMissionTick(float dt) => SurfaceDecalFx.Tick(dt);
        protected override void OnEndMission() => SurfaceDecalFx.OnSceneGone();
    }

    /// <summary>
    /// 控制台入口 `custom.surface`。文本纯英文（铁律）；**首参可弃**（认不出当占位符，回落默认并注明）。
    /// </summary>
    public static class SurfaceCommands
    {
        private const float DefaultSize = 3f;
        private const float DefaultDistance = 4f;

        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("surface", "custom")]
        public static string ExecuteSurface(List<string> args)
        {
            string sub = null;
            List<string> rest = new List<string>();
            List<float> nums = new List<float>();

            if (args != null)
            {
                for (int i = 0; i < args.Count; i++)
                {
                    string a = args[i];
                    if (string.IsNullOrEmpty(a)) continue;
                    float f;
                    if (float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) { nums.Add(f); continue; }
                    if (sub == null) { sub = a.ToLowerInvariant(); continue; }
                    rest.Add(a);
                }
            }

            // ── 无参 / status ──
            if (sub == null || sub == "status" || sub == "list")
                return "OK: " + SurfaceDecalFx.Describe()
                     + " | usage: custom.surface spawn <material> [radius_m] [life_s] | twin <matA> <matB> [radius] [gap]"
                     + " | fade <in_s> <out_s> | band <m> | lock <0..1|-1> | layer <kw|-all> | probe | clear";

            if (sub == "clear")
                return SurfaceDecalFx.Clear();

            if (sub == "probe")
                return "OK: " + SurfaceDecalFx.Probe();

            if (sub == "prefab")
            {
                return SurfaceDecalFx.SetPrefab(rest.Count > 0 ? rest[0] : null);
            }

            if (sub == "drive")
            {
                return SurfaceDecalFx.SetDrive(rest.Count > 0 ? rest[0] : null);
            }

            if (sub == "tint")
            {
                if (rest.Count < 1) return "Usage: custom.surface tint <RRGGBB|->";
                return SurfaceDecalFx.SetTint(rest[0]);
            }

            if (sub == "blend")
            {
                if (rest.Count < 1) return "Usage: custom.surface blend <addalpha|modulate|-> (>'-' = clear override)";
                return SurfaceDecalFx.SetBlend(rest[0]);
            }

            if (sub == "layer")
            {
                if (rest.Count < 1) return "Usage: custom.surface layer <keyword|-> (e.g. glow, frost; '-' or 'all' = no filter)";
                string kw = (rest[0] == "-" || rest[0].Equals("all", StringComparison.OrdinalIgnoreCase)) ? "" : rest[0];
                return SurfaceDecalFx.SetLayerFilter(kw);
            }

            if (sub == "fade")
            {
                if (nums.Count < 2) return "Usage: custom.surface fade <fadeIn_s> <fadeOut_s>";
                return SurfaceDecalFx.SetFade(nums[0], nums[1]);
            }

            if (sub == "band")
            {
                if (nums.Count < 1) return "Usage: custom.surface band <meters>";
                return SurfaceDecalFx.SetBand(nums[0]);
            }

            if (sub == "lock")
            {
                if (nums.Count < 1) return "Usage: custom.surface lock <0..1> (or -1 to release)";
                return SurfaceDecalFx.SetLock(nums[0]);
            }

            if (sub == "spawn")
            {
                // 🔴 第一参 = **prefab 名**（现在一个 prefab 对应一种材质/混合模式，prefab 就是"元素"）。
                //    不填就用当前默认（`custom.surface prefab <名>` 可改）。
                string prefab = rest.Count > 0 ? rest[0] : null;
                string note = prefab == null ? " [note: no prefab given -> current default]" : string.Empty;

                float radius = nums.Count >= 1 ? nums[0] : DefaultSize;
                if (radius <= 0f) radius = DefaultSize;
                float life = nums.Count >= 2 ? nums[1] : 0f;   // 0 = 永久
                float dist = nums.Count >= 3 ? nums[2] : DefaultDistance;

                // 🔴 **spawn 自动解锁驱动**：`lock` 是粘性的，上一测设过没解锁会把寿命曲线整个旁路掉
                //    （实机踩过：lock 0.2 之后 spawn 带寿命的片，看不到淡入淡出）。
                string unlockNote = SurfaceDecalFx.ReleaseLock();

                SurfaceDecalFx.Patch p;
                string err;
                if (!SurfaceDecalFx.Spawn(prefab, radius, life, dist, out p, out err))
                    return "error: " + err + note;

                return string.Format(CultureInfo.InvariantCulture,
                    "OK: spawned '{0}' size={1:F1}m life={2} at {3:F1}m ahead. close the console (~) and look ahead. | {4}{5}",
                    p.Element, radius, life <= 0f ? "inf" : life.ToString("F1", CultureInfo.InvariantCulture) + "s",
                    dist, SurfaceDecalFx.Describe(), note + unlockNote);
            }

            if (sub == "strip")
            {
                // 🔴 三地块：左 A | 中间交界带 | 右 B
                string pa = rest.Count > 0 ? rest[0] : "customDecalTest";
                string pb = rest.Count > 1 ? rest[1] : "customDecalTest_vanilla";
                string note = rest.Count == 0 ? " [note: no prefabs given -> customDecalTest | customDecalTest_vanilla]" : string.Empty;
                float size = nums.Count >= 1 ? nums[0] : DefaultSize * 2f;
                if (size <= 0f) size = DefaultSize * 2f;
                float ov = nums.Count >= 2 ? nums[1] : 35f;     // 重叠百分比（0~100）
                if (ov > 1.0f) ov /= 100f;                       // 允许 "35" 也允许 "0.35"
                float dist = nums.Count >= 3 ? nums[2] : DefaultDistance + size;

                string rep, err;
                if (!SurfaceDecalFx.SpawnStrip(pa, pb, size, ov, dist, out rep, out err))
                    return "error: " + err + note;
                return rep + note;
            }

            if (sub == "twin")
            {
                string a = rest.Count > 0 ? rest[0] : "customDecalTest";
                string b = rest.Count > 1 ? rest[1] : "customDecalTest_vanilla";
                float radius = nums.Count >= 1 ? nums[0] : DefaultSize;
                if (radius <= 0f) radius = DefaultSize;
                float gap = nums.Count >= 2 ? nums[1] : 0f;    // 两片中心的额外间距（0 = 完全重叠）
                float dist = nums.Count >= 3 ? nums[2] : DefaultDistance;

                string note = string.Empty;
                if (rest.Count == 0) note = " [note: no prefabs given -> customDecalTest vs customDecalTest_vanilla]";

                // A 在中心，B 沿侧向偏开 —— 侧偏量 = 半径×2 + gap（0 就是完全叠在一起看混合）
                float side = gap <= 0f ? 0f : (radius * 2f + gap);

                SurfaceDecalFx.Patch pa, pb;
                string err;
                if (!SurfaceDecalFx.Spawn(a, radius, 0f, dist, out pa, out err))
                    return "error: A: " + err + note;

                // B：从 A 的位置侧移。用 A 的实际落点算，避免各算一次地面高度导致错位
                Vec3 saved = pa.Center;
                if (!SurfaceDecalFx.SpawnOffset(b, radius, 0f, saved, side, out pb, out err))
                    return "error: B: " + err + note;

                return string.Format(CultureInfo.InvariantCulture,
                    "OK: twin '{0}' (w={1:F2}) vs '{2}' (w={3:F2}), side offset={4:F1}m. "
                    + "Watch the seam: it should cross-fade, not hard-cut. | {5}{6}",
                    a, pa.Weight, b, pb.Weight, side, SurfaceDecalFx.Describe(), note);
            }

            return "error: unknown subcommand '" + sub + "'. usage: custom.surface [status|spawn|twin|fade|band|lock|clear]";
        }
    }
}
