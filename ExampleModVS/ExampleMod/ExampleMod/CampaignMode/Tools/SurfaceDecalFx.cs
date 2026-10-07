// ═══════════════════════════════════════════════════════════════════════════
// 元素地表贴花：**生命周期 + 元素过渡** 控制器 —— 控制台入口 `custom.surface`
// （2026-09-30，接《元素地表系统》方案 §三 / §六 的落地件）
//
// 🔴🔴 **铁律：贴花只做单层**（2026-09-30 用户裁定）
//   · **一个 prefab 只准有一个 `decal_mesh`**。多层的路**封死，以后不准走**。
//   · **双层有且只有一种合法形态**：**两个不同元素的单层贴花互相重叠做渐变**
//     —— 那是**两个实体**（各单层），不是一个 prefab 里塞两层。
//   · 本类强制这条：实例化后发现 decal mesh > 1 个，**只用第一个**并在日志里报警。
//   ⇒ 所以默认 prefab = `lwnDecalScrochAdd`（**单层**），不是 `lwn_decal_lava`（那是两层，已弃用）。
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
        //    prefab 名与材质一一对应；下面两个是**同一张贴图、只差 blend** 的一对，做 A/B 对照：
        //      `lwnDecalScrochAdd` → 材质 `lwn_manual_scroch_addalpha` = **add_alpha**（加法：火/光）
        //      `lwnDecalScrochMod` → 材质 `lwn_manual_scroch_modulate` = **modulate**（覆盖：焦土/油）
        //    `lwn_decal_lava` / `lwn_decal_ice` = 两层，**已弃用不准再用**。
        private static string _prefab = "lwnDecalScrochAdd";
        private const float DefaultSize = 3f;   // prefab 自带的边长（米）—— 子节点 scale 写的是 3.0
        private const int MaxLayers = 1;        // 超过这个数就报警并只用第一层

        /// <summary>切换用哪个单层 prefab（也就切换了混合模式）。传 "-" 回默认。</summary>
        public static string SetPrefab(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "-") { _prefab = "lwnDecalScrochAdd"; }
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
            /// <summary>这个材质的 alphaTest 阈值（0 = 没开 alpha test）。
            /// 🔴 在 `CollectMeshes` 和 `ApplyMaterial` 两处刷新 —— 换材质是 spawn 之后发生的，
            /// 只填一次会拿到旧材质的阈值。淡出补偿曲线要用它（`SetColor`）。</summary>
            public float AlphaTest = 0f;
            /// <summary>上次真正写进 `Mesh.Color` 的驱动量（-1 = 还没写过）。
            /// 用来跳过"值没变"的帧 —— 见 `Tick` 里的写入闸门。</summary>
            public float LastWritten = -1f;
        }

        public sealed class Patch
        {
            public GameEntity Entity;
            public readonly List<LayerRef> Layers = new List<LayerRef>();
            public string Element = "";   // 元素名（= 材质名）
            /// <summary>地表过渡里的角色：0=普通片；1=先进方(A)；2=后退方(B)。
            /// 见 `Transition()` 与 `Tick` 的过渡分支。</summary>
            public int Role = 0;
            public Vec3 Center;
            public float Radius;
            public float Life;            // 剩余寿命（秒）；MaxLife<=0 = 永久
            public float MaxLife;
            public float Weight = 1f;     // 空间权重（每帧重算）
        }

        private static readonly List<Patch> Patches = new List<Patch>();
        private static int _seq;
        private static float _lastAlpha = -1f;
        /// <summary>置位后下一帧**忽略"值没变就跳过"的闸门**，全量重写一遍颜色。
        /// 凡是改变了「同一个驱动量 → 不同颜色」的映射的操作都要置位：改基色 / 改驱动通道 /
        /// 改混合 / 改锁定 / 换材质。</summary>
        private static bool _colorDirty = true;

        /// <summary>地表过渡的水位进度：&lt;0 = 不在过渡；0 = 全是后退方(B)；1 = 全是先进方(A)。
        /// 见 `Transition()` 与 `Tick` 的过渡分支。</summary>
        private static float _transitionP = -1f;
        private static float _transDur = 8f;

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

        /// <summary>
        /// 面向方向的**右手边**（水平；把前向绕 Z 转 −90°：(x,y) → (y,−x)）。
        /// A/B 对照沿它错开 —— 这样**不管你面朝哪边**，A 永远在你左手边、B 永远在右手边。
        /// （`SpawnOffset` 用的是**世界 X 轴**，那是给"拼成一条"用的：两片的边必须互相平行。
        ///   对照不用拼，用玩家右向才好认 —— 之前 A/B 沿世界 X 放，人一转身左右就颠倒了。）
        /// </summary>
        private static Vec3 FlatRight(float distance)
        {
            Vec3 look = Vec3.Zero;
            try { if (!CameraLook.TryGet(out look)) look = Vec3.Forward; } catch { look = Vec3.Forward; }
            Vec3 flat = new Vec3(look.X, look.Y, 0f);
            if (flat.Length < 1e-3f) flat = new Vec3(0f, 1f, 0f);
            flat.Normalize();
            return new Vec3(flat.Y, -flat.X, 0f) * distance;
        }

        /// <summary>把"这一点在哪"讲清楚：世界坐标 + 相对玩家的前后/左右（米）。
        /// 目的：贴花看不见时，能分清是"没渲染"还是"放偏了/太小" —— 引擎对运行时贴花
        /// 被丢弃是**静默**的（native 里只有粒子那条 `Can not find decal material`），没有日志可查。</summary>
        private static string Spot(Vec3 pos, Agent main, string extra)
        {
            Vec3 d = new Vec3(pos.X - main.Position.X, pos.Y - main.Position.Y, pos.Z - main.Position.Z);
            Vec3 f = FlatForward(1f);
            Vec3 r = FlatRight(1f);
            float ahead = d.X * f.X + d.Y * f.Y + d.Z * f.Z;
            float side = d.X * r.X + d.Y * r.Y + d.Z * r.Z;
            float dist = (float)Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
            return string.Format(CultureInfo.InvariantCulture,
                "pos=({0:F1},{1:F1},{2:F1}) {3:F1}m away [ahead {4:F1}m, right {5:F1}m] {6}",
                pos.X, pos.Y, pos.Z, dist, ahead, side, extra);
        }

        private static float GroundZ(Scene scene, Vec3 pos, float fallbackZ)        {
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
        /// 在指定中心**侧移**造一片（`twin` / `strip` 用）。
        ///
        /// 🔴 侧移方向 = **世界 X 轴**（2026-10-01 修正）。
        ///    贴花片是**轴对齐**的 —— `SpawnAt` 用 `MatrixFrame.Identity`、不旋转，
        ///    所以只有沿世界轴错开，两片的边才互相平行、才能拼成一条。
        ///    旧版用「相机右向」错开：片不旋转但错开方向斜着 ⇒ 实机看到两个方块斜错开、拼不上
        ///    （用户 2026-10-01 截图实证）。
        /// </summary>
        public static bool SpawnOffset(string prefab, float radius, float life,
                                       Vec3 center, float side, out Patch patch, out string err)
        {
            Vec3 flat = new Vec3(1f, 0f, 0f);
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
                            float alphaThr = 0f;
                            try
                            {
                                Material m = mesh.GetMaterial();
                                if (m != null) { mat = m.Name; alphaThr = m.GetAlphaTestValue(); }
                            }
                            catch { }
                            p.Layers.Add(new LayerRef { Mesh = mesh, Name = gname, Material = mat, AlphaTest = alphaThr });
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

            _colorDirty = true;   // 换了材质 ⇒ 同一个驱动量会算出不同颜色，必须全量重写
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
                    // 🔴 换完材质必须**重新读阈值** —— LayerRef.AlphaTest 是 spawn 时那个材质的，
                    //    淡出补偿曲线要用新材料的值（否则用错阈值 → 溶解曲线算歪）。
                    try
                    {
                        Material now = mesh.GetMaterial();
                        if (now != null)
                        {
                            p.Layers[i].Material = now.Name;
                            p.Layers[i].AlphaTest = now.GetAlphaTestValue();
                        }
                    }
                    catch { }
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
                _colorDirty = true;
                return "OK: blend override cleared (use material's own blend)";
            }
            _forceBlend = mode;
            _colorDirty = true;

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

            // ── 地表「过渡」模式（2026-10-06）──────────────────────────────────
            //   两层**互补 alpha** 的地表互相推一条分界线。贴图的 alpha 就是一张**高度场** f
            //   （亮=1=先进方 A 的地盘，暗=0=后退方 B 的地盘）；水位 L 从 1 扫到 0，
            //   分界线 = f = L 那条等高线 ⇒ **分界线形状离线烘焙、位置运行期一个数就能推**。
            //
            //   两层的有效阈值必须**互补**（A 显示 {f ≥ L}、B 显示 {f ≤ L}）：
            //     A 的 factor.a = 阈值 ÷ L        B 的 factor.a = 阈值 ÷ (1 − L)
            //   （还是"有效阈值 = 阈值 ÷ factor.a"那条老关系，这次用它推**分界线位置**，不是推淡出。）
            //   🔴 互补 ⇒ 两层绘制区域**不重叠** ⇒ 多层也不怕绘制顺序（那条老坑直接绕过去了）。
            if (_transitionP >= 0f)
            {
                for (int i = 0; i < Patches.Count; i++)
                {
                    Patch p = Patches[i];
                    if (p.Role == 0) continue;
                    float thr = (p.Layers.Count > 0 && p.Layers[0].AlphaTest > 0f) ? p.Layers[0].AlphaTest : 0.05f;
                    float L = (p.Role == 1) ? (1f - _transitionP) : _transitionP;
                    float ka = Clamp01(thr / Math.Max(1e-4f, L));
                    uint a8 = (uint)Math.Round(ka * 255f) << 24;
                    for (int j = 0; j < p.Layers.Count; j++)
                    {
                        if (!LayerMatches(p.Layers[j])) continue;
                        try { p.Layers[j].Mesh.Color = a8 | 0x00FFFFFFu; } catch { }
                    }
                }
                if (_transDur > 0.05f)
                {
                    _transitionP += dt / _transDur;
                    if (_transitionP > 1f) _transitionP = 1f;      // 到 1 停在"全是 A"
                }
                _lastAlpha = _transitionP;
                return;                 // 过渡期间不走常规那套（寿命/权重/写入闸门）
            }

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
                    LayerRef lr = p.Layers[j];
                    // 🔴 **只在数值真变了才写**（2026-10-06）：满值保持期（12 秒寿命里占 8.5 秒）
                    //    每帧都在写同一个数，纯属白费。阈值取 1/255 —— Mesh.Color 本来就会被量化到
                    //    8 位，更小的变化引擎和眼睛都看不见。
                    //    `_colorDirty` 在 tint/drive/blend/lock/换材质时置位，保证那些改动立刻全量重写。
                    if (_colorDirty || lr.LastWritten < 0f || Math.Abs(a - lr.LastWritten) >= 1f / 255f)
                    {
                        SetColor(lr.Mesh, a, lr.AlphaTest);
                        lr.LastWritten = a;
                    }
                }
            }
            _lastAlpha = sum / Patches.Count;
            _colorDirty = false;
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
        private static void SetColor(Mesh m, float a, float alphaTest)
        {
            float k = Clamp01(a);

            // 🔴🔴 2026-10-06 实机定案：**`modulate` 是"替换反照率"，不是乘法** ——
            //   判据：贴图给纯白时把它推到 factor=1，画面直接**变成白板**（而不是"不变"）。
            //   ⇒ 替换语义下**贴图值没有"无痕"档**：白就盖成白、黑就盖成黑，
            //     唯一"不生效"的方式 = **根本不画**（靠 `alpha_test` 裁）。
            //   ⇒ 这种层的淡出**只能靠裁剪**（形状从边缘往内收 = `tools/decal-pipeline` 那套抛物面配方）；
            //     推 RGB 只会把"盖上去的颜色"推向黑，推不出"回到底面"。
            //   （曾按"乘法"假设给这层加过反相映射 `factor = 1−k`，实机证伪，已撤。）
            uint baseRgb = _tint ?? 0x00FFFFFFu;
            uint r = (uint)Math.Round(((baseRgb >> 16) & 0xFF) * k);
            uint g = (uint)Math.Round(((baseRgb >> 8) & 0xFF) * k);
            uint b = (uint)Math.Round((baseRgb & 0xFF) * k);
            uint rgb = (r << 16) | (g << 8) | b;

            // 🔴 溶解补偿曲线（2026-10-06；只作用于**被 alpha test 消费的那个 alpha**）：
            //   shader 的裁定是 `factor.a × 贴图alpha ≥ 阈值`，变形 = `贴图alpha ≥ 阈值 / factor.a`
            //   —— 固定的阈值被 factor.a 一除就成了"有效阈值"。factor.a 线性降 ⇒ 有效阈值走**倒数**
            //   （前 95% 的时间几乎不动、最后一下暴走）⇒ 形状变化全挤在末段（实测：0.0235 阈值下
            //   98% 的时长里形状只缩了 1%）。
            //   反解出让有效阈值**线性上升**的 factor.a：T(p) = thr + (1−thr)·p，k = thr / T，p = 1 − a
            //     ⇒ k_alpha = thr / (thr + (1 − thr) × (1 − a))
            //   效果：形状在**整段淡出时间里均匀缩小**，走到末尾正好归零。
            //   RGB 那一路（加法层的淡出）**保持线性** —— 它本来就是对的，别动。
            float kAlpha = k;
            if (alphaTest > 0.0001f && _drive != "rgb")
            {
                float thr = Clamp01(alphaTest);
                if (thr < 0.999f) kAlpha = thr / (thr + (1f - thr) * (1f - k));
            }

            switch (_drive)
            {
                case "alpha":
                    try { m.Color = ((uint)Math.Round(kAlpha * 255f) << 24) | 0x00FFFFFFu; } catch { }
                    break;
                case "both":
                    try { m.Color = ((uint)Math.Round(kAlpha * 255f) << 24) | rgb; } catch { }
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
            _colorDirty = true;
            return $"OK: drive = {_drive}. rgb = multiply to black (works on additive layers); "
                 + "alpha = transparency (what modulate layers need).";
        }

        /// <summary>给所有片设一个基色（"ff0000"）。传 "-" 清掉（回白）。</summary>
        public static string SetTint(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex == "-") { _tint = null; _colorDirty = true; return "OK: tint cleared (white base)"; }
            string h = hex.TrimStart('#');
            if (h.Length == 6) h = "FF" + h;
            uint v;
            if (!uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v))
                return $"error: bad hex '{hex}' (want RRGGBB)";
            _tint = v & 0x00FFFFFFu;
            _colorDirty = true;
            return $"OK: base tint = 0x{_tint.Value:X6} (fade multiplies this). "
                 + "NOTE: a base whose channel is ~0 in the texture will make the decal vanish.";
        }

        // 🔴 引擎没有"删单个实体/贴花"的 API —— 只能隐藏（先例：DecalFx.ClearInternal）
        private static void Hide(Patch p)
        {
            try { p.Entity?.SetVisibilityExcludeParents(false); } catch { }
        }

        // ── A 路对照（Decal 组件 + 全局图集）2026-10-06 ─────────────────────
        //
        // 🔴 与 B 路的本质差别（shader 源码实证）：
        //    · B 路（decal_mesh）采样**材质自己的贴图** —— 所以我们画的图能上屏。
        //    · A 路（Decal 组件）**从不采样材质贴图**：引擎拿「材质 tex[0] 的**贴图名**」
        //      去查 `decal_textures_<当前场景组>.xml`（Native 里烘死的表）——
        //      查不到 ⇒ **整条 decal 丢弃**（表现是"什么都不出"）；查到 ⇒ 按 UV 从
        //      **预烘图集** `decal_atlas_<组>` 取像素。
        //      （证据：`decal_atlas_texture` 只被 `shared_decal_functions.rsh` ×7 /
        //        `gbuffer_functions.rsh` ×1 / parallax / pbr_shading 采样，
        //        我们的 `decal.rs` / `deferred_decal.rsh` 里 **0 次**。）
        //    ⇒ 用**非覆写的自定义材质**走 A 路，预期 = **什么都不出**，且这个结果与
        //      贴图是 DDS 还是 PNG、有没有 alpha_test **无关**（贴图根本没被采样）。
        //
        // 照抄官方两处用例（1.2.12 SandBox.View 反编译：MapCursor / 攻城器械圆圈）：
        //   CreateDecal → SetMaterial → AddComponent → SetGlobalFrame
        //   → `Scene.AddDecalInstance(decal, "editor_set", deletable: true)`
        //   🔴 最后那句不能省（网上教程的 snippet 常漏掉它）。
        //   `editor_set`（Native/ModuleData/decal_sets.xml）= 寿命近乎无限、不淡出，正合探针。
        //
        // 🔴 1.2.12 **没有** `Scene.RemoveDecalInstance`（1.5.x 才加）⇒ 清理只能靠
        //    `Scene.ClearDecals()`（会连带清掉其它**运行期**贴花实例；静态 decal_component 不受影响）。
        private static readonly List<GameEntity> _routeAEntities = new List<GameEntity>();
        private static readonly List<string> _routeALog = new List<string>();   // 给 Describe() 用：放了哪些 A 片、在哪

        /// <summary>B 路那片网格的**本地**包围盒（含子节点缩放前的口径）——用来判断"是不是空的/退化的"。
        /// 世界尺寸 ≈ 本地 × k（k = radius / DefaultSize，见 SpawnAt）。</summary>
        private static string MeshBox(Patch p)
        {
            if (p == null || p.Layers.Count == 0) return "?";
            try
            {
                Mesh m = p.Layers[0].Mesh;
                Vec3 mn = m.GetBoundingBoxMin(), mx = m.GetBoundingBoxMax();
                return string.Format(CultureInfo.InvariantCulture, "{0:F2}x{1:F2}x{2:F2}(local)",
                    mx.X - mn.X, mx.Y - mn.Y, mx.Z - mn.Z);
            }
            catch { return "?"; }
        }

        private static Decal SpawnRouteAAt(string materialName, Vec3 pos, float scale, out string err)
        {
            err = null;
            Mission mission = Mission.Current;
            if (mission == null) { err = "no mission."; return null; }
            Scene scene = mission.Scene;
            if (scene == null) { err = "mission has no scene."; return null; }

            Material mat = null;
            try { mat = Material.GetFromResource(materialName); } catch { }
            if (mat == null) { err = $"material '{materialName}' not found."; return null; }

            pos = new Vec3(pos.X, pos.Y, GroundZ(scene, pos, pos.Z));
            if (scale <= 0f) scale = 1f;

            GameEntity ent = null;
            try
            {
                ent = GameEntity.CreateEmpty(scene);
                ent.Name = "lwn_routeA_" + materialName;

                Decal decal = Decal.CreateDecal();
                decal.SetMaterial(mat);
                decal.SetFactor1Linear(0xFFFFFFFFu);
                ent.AddComponent(decal);

                MatrixFrame frame = MatrixFrame.Identity;
                frame.origin = pos;
                frame.Scale(new Vec3(scale, scale, scale));   // A 路 = 均匀缩放（官方两处都这么写）
                ent.SetGlobalFrame(frame);
                ent.SetVisibilityExcludeParents(true);

                scene.AddDecalInstance(decal, "editor_set", deletable: true);

                _routeAEntities.Add(ent);
                DebugLogger.Log($"[Surface] routeA mat='{materialName}' scale={scale:F2} "
                              + $"pos=({pos.X:F2},{pos.Y:F2},{pos.Z:F2}) total={_routeAEntities.Count}");
                return decal;
            }
            catch (Exception ex)
            {
                err = "A-route threw: " + ex.Message;
                DebugLogger.Log($"[Surface] routeA 异常: {ex}");
                try { ent?.SetVisibilityExcludeParents(false); } catch { }
                return null;
            }
        }

        /// <summary>
        /// A/B 路对照 + 多层叠加统一入口。`mode` = "a" | "b" | "ab" | "pair"。
        /// 由 `custom.surface spawn &lt;材质…&gt; &lt;距离&gt; &lt;方法&gt; [半径] [寿命秒] [A缩放]` 调用。
        /// · `a`/`b`/`ab` 只用**第一个**材质（`ab` = 两片并排：A 在左、B 在右，各偏 radius 米）
        /// · `pair` 把**每一个**材质各放一片在**同一个位置**（测多层叠加）—— 顺序 = 参数顺序。
        ///   🔴 必须原子放：分两条命令打的话，中间镜头一动，两次算出的位置就不同 ⇒ 错位
        ///   （位置 = 玩家坐标 + **镜头朝向** × 距离）。
        /// `life` &gt; 0 ⇒ 有寿命（会走淡入/淡出）；≤ 0 ⇒ 永久片。
        /// 🔴 返回文本一律英文（控制台纪律）。
        /// </summary>
        public static string Route(string mode, List<string> materials, float distance, float radius, float life, float scale)
        {
            Mission mission = Mission.Current;
            if (mission == null) return "FAIL: no mission (enter a scene first).";
            Agent main = mission.MainAgent;
            if (main == null) return "FAIL: no main agent to anchor on.";

            if (materials == null || materials.Count == 0) return "FAIL: no material name given.";
            mode = (mode ?? "b").ToLowerInvariant();
            if (mode != "a" && mode != "ab" && mode != "pair") mode = "b";
            if (mode != "pair" && materials.Count > 1) materials = new List<string> { materials[0] };

            // 逐个校验材质是否存在（不静默吞 —— 打错一个字就白跑一轮）
            var ok = new List<string>();
            var bad = new List<string>();
            for (int i = 0; i < materials.Count; i++)
            {
                Material probe = null;
                try { probe = Material.GetFromResource(materials[i]); } catch { }
                if (probe == null) bad.Add(materials[i]); else ok.Add(materials[i]);
            }
            if (ok.Count == 0)
                return "FAIL: material not found: " + string.Join(", ", bad) + " (is its package loaded?).";
            materials = ok;

            if (distance <= 0f) distance = 4f;      // 同 SurfaceCommands.DefaultDistance
            if (radius <= 0f) radius = DefaultSize;
            if (scale <= 0f) scale = 1f;

            string unlockNote = ReleaseLock();
            Vec3 center = main.Position + FlatForward(distance);
            float gap = mode == "ab" ? radius : 0f;

            var lines = new List<string>();
            for (int i = 0; i < materials.Count; i++)
            {
                string mat = materials[i];

                if (mode == "pair")
                {
                    // 每个材质一片、全部落在同一个中心点（顺序 = 参数顺序，用来验绘制顺序）
                    Patch pp; string perr;
                    if (SpawnAt(_prefab, radius, life, center, out pp, out perr))
                    {
                        ApplyMaterial(pp, mat);
                        lines.Add($"[{i}] '{mat}' at SAME spot");
                    }
                    else lines.Add($"[{i}] '{mat}' FAILED({perr})");
                    continue;
                }

                string aLine = null, bLine = null;
                if (mode == "a" || mode == "ab")
                {
                    Vec3 posA = center - FlatRight(gap);
                    string err;
                    Decal d = SpawnRouteAAt(mat, posA, scale, out err);
                    if (d != null)
                    {
                        _routeALog.Add($"{mat} scale={scale:F2} @ ({posA.X:F1},{posA.Y:F1},{posA.Z:F1})");
                        aLine = "A(left): " + Spot(posA, main,
                            $"scale={scale:F2}, set='editor_set' | CAVEAT: route A's visual size is engine-side and UNCALIBRATED");
                    }
                    else aLine = $"A(left): FAILED -- {err}";
                }
                if (mode == "b" || mode == "ab")
                {
                    Vec3 posB = center + FlatRight(gap);
                    Patch p; string err;
                    if (SpawnAt(_prefab, radius, life, posB, out p, out err))
                    {
                        ApplyMaterial(p, mat);      // 按名字换材质（实测比对象重载可靠）
                        bLine = "B(right): " + Spot(p.Center, main,
                            $"size={radius:F1}m, life={(life > 0f ? life.ToString("F1", CultureInfo.InvariantCulture) + "s" : "inf")}, "
                          + $"layers={p.Layers.Count}, mat='{mat}', meshBBox={MeshBox(p)}");
                    }
                    else bLine = $"B(right): FAILED -- {err}";
                }
                var parts = new List<string>();
                if (aLine != null) parts.Add(aLine);
                if (bLine != null) parts.Add(bLine);
                lines.Add($"[{i}] '{mat}': " + string.Join(" · ", parts));
            }

            return $"OK: route {mode.ToUpperInvariant()} | {materials.Count} material(s) | dist={distance:F1} r={radius:F1}"
                 + $" life={(life > 0f ? life.ToString("F1", CultureInfo.InvariantCulture) + "s" : "inf")} scaleA={scale:F2}"
                 + (bad.Count > 0 ? $" | NOT FOUND (skipped): {string.Join(",", bad)}" : "")
                 + " | " + string.Join(" | ", lines)
                 + (mode == "pair" ? " | NOTE: pair = all layers at the SAME spot; their ORDER = your argument order"
                                      + " (swap the two names to test the reverse draw order)." : "")
                 + " | clear: 'custom.surface decalClear' (A: Scene.ClearDecals) + 'custom.surface clear' (B)"
                 + unlockNote;
        }

        /// <summary>清掉本命令放下的 A 路实例。1.2.12 没有单条删除 API ⇒ 只能整场景 `ClearDecals()`。</summary>
        public static string ClearRouteA()
        {
            int n = _routeAEntities.Count;
            for (int i = 0; i < n; i++)
            {
                try { _routeAEntities[i]?.SetVisibilityExcludeParents(false); } catch { }
            }
            _routeAEntities.Clear();
            _routeALog.Clear();

            bool wiped = false;
            try { Mission.Current?.Scene?.ClearDecals(); wiped = true; } catch { }
            DebugLogger.Log($"[Surface] routeA clear: hidden={n} ClearDecals={wiped}");
            return $"OK: routeA entities hidden ({n})"
                 + (wiped ? " + Scene.ClearDecals() called (also wipes other RUNTIME decal instances; static decal_components are unaffected)"
                          : " ; Scene.ClearDecals() failed -- re-enter the scene to clear")
                 + ". B-route patches -> use 'custom.surface clear'.";
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
            // 🔴 错开方向 = **世界 X 轴**（2026-10-01 修正）：贴花片是**轴对齐**的
            //    （`SpawnAt` 用 `MatrixFrame.Identity`、不旋转），只有沿世界轴错开、
            //    两片的边才互相平行、才能拼成一条。旧版沿「相机右向」⇒ 片不转但错开方向斜着
            //    ⇒ 实机看到两个方块斜错开、拼不上（用户截图实证）。
            Vec3 flatR = new Vec3(1f, 0f, 0f);

            // 🔴 坐标换算（2026-10-01 修正）：`size` 是**片的边长**（SpawnAt 把子节点缩放到 radius = size），
            //    所以「刚好挨着」的中心距 = size、「完全重叠」= 0 ⇒ halfSep = size × (1−overlap) / 2。
            //    旧公式漏了 /2 ⇒ 中心距翻倍 ⇒ overlap=0 时隔着一整片、17% 时也仍然分离
            //    （实机症状：两片并排不相交，"过渡带"根本不存在）。
            float halfSep = size * (1f - overlap) * 0.5f;
            Vec3 center = main.Position + fwd * distance;

            Patch pa, pb;
            if (!SpawnOffset(prefabA, size, 0f, center - flatR * halfSep, 0f, out pa, out err))
                return false;
            if (!SpawnOffset(prefabB, size, 0f, center + flatR * halfSep, 0f, out pb, out err))
                return false;

            float sep = halfSep * 2f;
            float band = Math.Max(0f, size - sep);        // 两片的重叠宽度 = 边长 − 中心距
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
            _colorDirty = true;
            return " [note: drive lock released (was sticky)]";
        }

        public static string SetLock(float a)
        {
            if (a < 0f)
            {
                _lockAlpha = -1f;
                _colorDirty = true;
                return "OK: lock released (drive = weight x life again)";
            }
            _lockAlpha = Clamp01(a);
            _colorDirty = true;
            return $"OK: drive LOCKED to {_lockAlpha:F2} (RGB = base x this). 0 = invisible, 1 = full.";
        }

        /// <summary>
        /// 启动一次**地表过渡**：A（先进方）与 B（后退方）两层**互补 alpha** 的贴图，同位置叠放，
        /// 水位从"全是 B"扫到"全是 A"。
        ///
        /// 两张贴图必须**互补**（B 的 alpha = 1 − A 的 alpha），由
        /// `tools/decal-pipeline/scripts/gen_transition_pair.py` 生成。
        /// 分界线的**形状**烘焙在贴图的 alpha 高度场里（线性/径向/手绘任意），运行期只推水位。
        /// 🔴 互补 ⇒ 两层绘制区域不重叠 ⇒ **不用管绘制顺序**。
        /// 🔴 材质阈值要小（本项目用 0.01）、alpha 下限也要小（0.02）—— 否则水位扫到两端会**留残边**。
        /// </summary>
        public static string Transition(string matA, string matB, float distance, float radius, float seconds)
        {
            Mission mission = Mission.Current;
            if (mission == null) return "FAIL: no mission (enter a scene first).";
            Agent main = mission.MainAgent;
            if (main == null) return "FAIL: no main agent to anchor on.";
            if (string.IsNullOrEmpty(matA) || string.IsNullOrEmpty(matB))
                return "FAIL: need TWO material names: transition <advancingA> <retreatingB> <dist> [radius] [seconds]";
            foreach (string n in new[] { matA, matB })
            {
                Material probe = null;
                try { probe = Material.GetFromResource(n); } catch { }
                if (probe == null) return $"FAIL: material '{n}' not found (is its package loaded?).";
            }
            if (distance <= 0f) distance = 4f;
            if (radius <= 0f) radius = DefaultSize;
            if (seconds <= 0.1f) seconds = 8f;

            string unlock = ReleaseLock();
            Vec3 center = main.Position + FlatForward(distance);
            Clear();                                   // 先清场，免得旧片混进来（Clear 会把水位复位）

            string e1, e2;
            Patch pa, pb;
            bool okA = SpawnAt(_prefab, radius, -1f, center, out pa, out e1);
            if (okA) { ApplyMaterial(pa, matA); pa.Role = 1; }
            bool okB = SpawnAt(_prefab, radius, -1f, center, out pb, out e2);
            if (okB) { ApplyMaterial(pb, matB); pb.Role = 2; }
            if (!okA || !okB)
            {
                _transitionP = -1f;
                return $"FAIL: spawn A={okA}({e1}) B={okB}({e2})";
            }

            _transitionP = 0f;                         // 从"全是 B"开始
            _transDur = seconds;
            Tick(0f);
            DebugLogger.Log($"[Surface] transition A='{matA}' B='{matB}' dur={seconds:F1}s r={radius:F1} "
                          + $"@({center.X:F1},{center.Y:F1},{center.Z:F1})");
            return $"OK: transition '{matA}' (advancing) <- '{matB}' (retreating) | dur={seconds:F1}s r={radius:F1}"
                 + " | water level L sweeps 1->0; A shows {alpha>=L}, B shows {alpha<=L};"
                 + " the two layers are COMPLEMENTARY so draw order does not matter"
                 + " | stop/reset: 'custom.surface clear'" + unlock;
        }

        public static string Clear()
        {
            for (int i = 0; i < Patches.Count; i++) Hide(Patches[i]);
            int n = Patches.Count;
            Patches.Clear();
            _transitionP = -1f;                 // 顺带退出过渡模式
            _colorDirty = true;
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
                sb.Append("\n  B#").Append(i).Append(" '").Append(p.Element).Append("'  r=")
                  .Append(p.Radius.ToString("F1", CultureInfo.InvariantCulture)).Append("m  w=")
                  .Append(p.Weight.ToString("F2", CultureInfo.InvariantCulture)).Append("  life=")
                  .Append(p.MaxLife <= 0f ? "inf" : p.Life.ToString("F1", CultureInfo.InvariantCulture))
                  .Append("  layers=").Append(p.Layers.Count)
                  .Append("  pos=(").Append(p.Center.X.ToString("F1", CultureInfo.InvariantCulture)).Append(",")
                  .Append(p.Center.Y.ToString("F1", CultureInfo.InvariantCulture)).Append(",")
                  .Append(p.Center.Z.ToString("F1", CultureInfo.InvariantCulture)).Append(")");
            }
            for (int i = 0; i < _routeALog.Count; i++)
                sb.Append("\n  A#").Append(i).Append(" ").Append(_routeALog[i]);
            return sb.ToString();
        }

        public static void OnSceneGone()
        {
            Patches.Clear();
            _routeAEntities.Clear();
            _routeALog.Clear();
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
                     + " | usage: custom.surface spawn <material> [radius_m] [life_s] | spawn <mat...> <dist> <a|b|ab|pair> [radius] [life_s] [scaleA]"
                     + " | transition <matA> <matB> <dist> [radius] [secs]"
                     + " | decalClear | twin <matA> <matB> [radius] [gap]"
                     + " | fade <in_s> <out_s> | band <m> | lock <0..1|-1> | layer <kw|-all> | probe | clear";

            if (sub == "clear")
                return SurfaceDecalFx.Clear();

            if (sub == "probe")
                return "OK: " + SurfaceDecalFx.Probe();

            // 🔴 地形图层那条线（元素地表 §3.1 旧结论的更正件，2026-09-30）：
            //    引擎的地形本身就是「每层权重图 + 逐层 over 合成」，天生带渐变与优先级。
            //    命令只做最小验证 —— 先探测能不能读，再试能不能写权重。
            //    数字参数被统一收进 nums，所以地形子命令名落在 rest[0]：
            //      custom.surface terrain                                  → info（只读，零风险）
            //      custom.surface terrain weight <层> <值> [nodeX] [nodeY]  → 试写（未验证，见 SurfaceTerrainFx 文件头风险段）
            //      custom.surface terrain finalize                         → 写完没反应时试它
            if (sub == "terrain")
            {
                string t = rest.Count > 0 ? rest[0].ToLowerInvariant() : "info";

                if (t == "info" || t == "probe" || t == "status")
                    return "OK: " + SurfaceTerrainFx.Info();

                if (t == "finalize")
                    return SurfaceTerrainFx.FinalizeEdit();

                if (t == "weight" || t == "paint")
                {
                    if (nums.Count < 2)
                        return "Usage: custom.surface terrain weight <layer> <value 0..1> [nodeX] [nodeY]";
                    int layer = (int)nums[0];
                    float val = nums[1];
                    int nx = nums.Count >= 3 ? (int)nums[2] : 0;
                    int ny = nums.Count >= 4 ? (int)nums[3] : 0;
                    return SurfaceTerrainFx.Weight(layer, val, nx, ny);
                }

                return "error: unknown terrain subcommand '" + t
                     + "'. usage: custom.surface terrain [info | weight <layer> <value> [x] [y] | finalize]";
            }

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

            // ── 地表过渡（2026-10-06）──────────────────────────────────────
            //   custom.surface transition <先进方材质A> <后退方材质B> <距离> [半径] [秒]
            //   两层**互补 alpha** 的贴图同位置叠放，水位从"全是 B"扫到"全是 A"。
            //   贴图对由 tools/decal-pipeline/scripts/gen_transition_pair.py 生成（必须互补）。
            if (sub == "transition")
            {
                string mA = rest.Count > 0 ? rest[0] : null;
                string mB = rest.Count > 1 ? rest[1] : null;
                float td = nums.Count >= 1 ? nums[0] : 0f;
                float tr = nums.Count >= 2 ? nums[1] : 0f;
                float ts = nums.Count >= 3 ? nums[2] : 0f;
                return SurfaceDecalFx.Transition(mA, mB, td, tr, ts);
            }

            // ── A 路清理（2026-10-06）──────────────────────────────────────
            //   走 A 路的片**没有**单条删除 API（1.2.12 无 RemoveDecalInstance）⇒ 只能整场景 ClearDecals()。
            //   ⚠️ 这会连带清掉其它**运行期**贴花实例；静态 decal_component 不受影响。
            if (sub == "decalclear")
                return SurfaceDecalFx.ClearRouteA();

            if (sub == "spawn")
            {
                // 🔴 两种形式，靠**有没有 a/b/ab 这个"方法"词**区分（prefab 名永远不会是这三个）：
                //    ① A/B 路对照 + 多层叠加（2026-10-06）：
                //         custom.surface spawn <材质名> <距离> <a|b|ab> [半径] [寿命秒] [A缩放]
                //         custom.surface spawn <下层> <上层> <距离> pair [半径] [寿命秒]
                //         参数1 = 材质名（pair 模式给两个，**顺序 = 放置顺序**）· 参数2 = 距离 · 参数3 = 方法
                //           a  = A 路（Decal 组件 + 全局图集，自定义材质预期什么都不出）
                //           b  = B 路（decal_mesh + 材质自己的贴图）
                //           ab = 两片并排对照（A 在左、B 在右）
                //           pair = 给出的每个材质各放一片在**同一个位置**（测多层叠加用；
                //                  分两条命令打会因镜头移动而错位，所以必须原子放）
                //         [寿命秒] > 0 ⇒ 会走淡入/淡出（`custom.surface fade <in> <out>` 调时长）
                //   🔴 方法词在 rest 里**任意位置**都能认（其余 token 一律当材质名）——
                //      这样"材质名恰好叫 a/b"也不会误判，且顺序完全由参数顺序决定。
                string method = null;
                var matNames = new List<string>();
                for (int i = 0; i < rest.Count; i++)
                {
                    string t = rest[i].ToLowerInvariant();
                    if (method == null && (t == "a" || t == "b" || t == "ab" || t == "pair")) { method = t; continue; }
                    matNames.Add(rest[i]);
                }
                if (method != null && matNames.Count > 0)
                {
                    float rDist = nums.Count >= 1 ? nums[0] : 0f;    // 0 = 用默认
                    float rRad = nums.Count >= 2 ? nums[1] : 0f;
                    float rLife = nums.Count >= 3 ? nums[2] : 0f;    // 0 = 永久
                    float rScaleA = nums.Count >= 4 ? nums[3] : 0f;
                    return SurfaceDecalFx.Route(method, matNames, rDist, rRad, rLife, rScaleA);
                }

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
                string pa = rest.Count > 0 ? rest[0] : "lwnDecalScrochAdd";
                string pb = rest.Count > 1 ? rest[1] : "lwnDecalScrochMod";
                string note = rest.Count == 0 ? " [note: no prefabs given -> lwnDecalScrochAdd | lwnDecalScrochMod]" : string.Empty;
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
                string a = rest.Count > 0 ? rest[0] : "lwnDecalScrochAdd";
                string b = rest.Count > 1 ? rest[1] : "lwnDecalScrochMod";
                float radius = nums.Count >= 1 ? nums[0] : DefaultSize;
                if (radius <= 0f) radius = DefaultSize;
                float gap = nums.Count >= 2 ? nums[1] : 0f;    // 两片中心的额外间距（0 = 完全重叠）
                float dist = nums.Count >= 3 ? nums[2] : DefaultDistance;

                string note = string.Empty;
                if (rest.Count == 0) note = " [note: no prefabs given -> lwnDecalScrochAdd vs lwnDecalScrochMod]";

                // A 在中心，B 沿侧向偏开 —— 侧偏量 = **边长 + gap**（片边长 = radius）。
                // 🔴 2026-10-01 修正：旧公式写 `radius * 2f + gap` ⇒ 间隔翻倍；只有 gap=0（完全重叠）那档是对的。
                float side = gap <= 0f ? 0f : (radius + gap);

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

            return "error: unknown subcommand '" + sub + "'. usage: custom.surface [status|spawn|twin|decalClear|fade|band|lock|clear]";
        }
    }
}
