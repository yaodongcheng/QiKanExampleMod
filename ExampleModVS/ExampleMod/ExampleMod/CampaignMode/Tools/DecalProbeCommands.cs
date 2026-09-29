// ═══════════════════════════════════════════════════════════════════════════
// 贴花探针（2026-09-29）—— 控制台入口 custom.decal
//
// 目的：一条指令在场景里召唤引擎贴花（Decal），肉眼验证四件事——
//   ① 贴花在这台机器 / 这个场景里到底出不出得来；
//   ② UV 矩形（scale_x, scale_y, offset_x, offset_y）从 C# 每帧改能不能动；
//   ③ SetFactor1 的染色 / 透明度通不通；
//   ④ 哪些现成材质能用（焦土类：fire_damage / decal_city_ground / …）。
//
// 【为什么值得走这条路】火焰地面若用粒子铺，覆盖大面积 = 上千透明面片 + 大量 overdraw。
// 贴花是引擎一等公民：**一次绘制**、按屏幕 tile 做距离剔除、贴图走共享图集，
// 覆盖同样面积便宜得多。原版自己就这么干 —— 火粒子撞地盖一张 fire_damage 贴花
// （Native/ModuleData/collision_infos.xml），大地图光标本身也是一张每帧手摇图集的贴花
// （SandBox.View 的 MapCursor）。
//
// 【机制要点（反编译实证）】
//   · 运行期三步：`Decal.CreateDecal()` → 挂到 `GameEntity.AddComponent` →
//     `Scene.AddDecalInstance(decal, "editor_set", deletable)`；之后位置靠实体
//     `SetGlobalFrame`、尺寸在帧里 `Scale` —— 与 MapCursor 逐字同款。
//   · UV 走 `Decal.SetVectorArgument(sx, sy, ox, oy)`，shader 侧 `deferred_decal.rsh:52`
//     直接把它当图集矩形用 ⇒ **手摇翻页 / 滚动完全由 C# 掌控**。
//   · 🔴 材质 flag `use_animated_texture_coords` 对贴花**不生效**（只有 pbr_standart /
//     light_emitter_sprite / show_texture / ui 那几支 shader 实现了它）⇒ 必须代码驱动。
//   · 贴花能自发光：着色里有 `emission_amount` 项（pbr_shading_functions.rsh:282 →
//     :406），但**从 C# 怎么调还没探明**（Decal 公开 API 只有 Factor1 / VectorArgument 两组）。
//   · 🔴 **材质名 ≠ 图集条目名**（2026-09-29 实机教训）：`decal_textures_*.xml` 里的
//     `ashes_a_d` / `plain_white` 之流是**图集里的贴图条目**，`Material.GetFromResource`
//     查不到它们（全 [missing]）。**真材质名看**：场景 prefab 里的
//     `<decal_component material="…"/>`（本文件 Candidates 就是从那儿捞的）。
//   · 🔴 **原版只在战役大地图用运行期 Decal**（全库搜 `AddDecalInstance` 只有
//     SandBox.View 一处在调：MapCursor / 攻城器械圈 / 据点轮廓）；任务场景（Mission）里
//     的贴花全是场景自带的 `ScenePropDecal` 实体。**所以 Mission 里这条路通不通是未知数，
//     正是本探针要回答的第一个问题** —— 因此本探针**两个场景都支持**，好做对照。
//
// 用法（游戏内 `~` 控制台；返回文本纯英文，详细读数走 DebugLogger）：
//   custom.decal                        眼前 8m 地面召唤一张（默认 fire_damage，5m）
//   custom.decal fire_damage 8 12       指定材质 / 尺寸(m) / 距离(m)
//   custom.decal grid                   一排 5 个**不同真材质**做对照，一眼看出哪个能显
//   custom.decal list                   列出候选材质，并标出哪个在本模块集里加载得到
//   custom.decal uv 1 1 0 0             直接写 UV 矩形（整张图集铺满，一眼可见）
//   custom.decal anim uv                手摇 UV 滚动（证明 UV 通道是活的）
//   custom.decal anim pulse             呼吸式改透明度（证明 SetFactor1 是活的）
//   custom.decal anim off               停
//   custom.decal color 1 0.5 0.2 1      写 Factor1（染色 + 透明度）
//   custom.decal clear                  收掉
//
// 🔴 两条项目纪律：返回文本**纯英文**（显示在游戏内控制台）；**首参可弃**（认不出的
//    当占位符，回落默认并在返回里注明）。
// ⚠️ 本件是**探针**，不是成品。验完即整对删（连同 MySubModule 那一行与 csproj 那一行）。
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
    /// 贴花探针本体（静态状态 + 操作）。做成静态类而不是只挂在 Mission 行为上，是因为
    /// **大地图没有 Mission**（对照实验必须能在图上跑）—— 行为只负责在 Mission 里每帧调 <see cref="Tick"/>。
    /// </summary>
    public static class DecalProbe
    {
        private static readonly List<GameEntity> Entities = new List<GameEntity>();
        private static readonly List<Decal> Decals = new List<Decal>();
        private static readonly List<string> Names = new List<string>();

        private static int _animMode;      // 0 = off / 1 = uv scroll / 2 = alpha pulse
        private static float _phase;
        private static float _uvSx = 1f, _uvSy = 1f, _uvOx, _uvOy;
        private static bool _hasUv;
        private static float _colR = 1f, _colG = 1f, _colB = 1f, _colA = 1f;
        private static bool _hasCol;

        public static bool HasDecal => Decals.Count > 0;

        // ── 场景解析（复用 NavMeshDebugRenderer 的既有范式：Mission 优先，否则大地图） ──

        private static Scene ResolveScene(out string tag, out Vec3 anchor)
        {
            tag = "(none)";
            anchor = Vec3.Zero;

            Mission mission = Mission.Current;
            if (mission?.Scene != null)
            {
                try { tag = "mission:" + (string.IsNullOrEmpty(mission.SceneName) ? "?" : mission.SceneName); }
                catch { tag = "mission:?"; }
                Agent main = Agent.Main;
                if (main != null)
                    anchor = main.Position;
                return mission.Scene;
            }

            if (Campaign.Current != null && Campaign.Current.MapSceneWrapper is SandBox.MapScene mapScene)
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

            return null;
        }

        /// <summary>
        /// 召唤一张贴花。`distance` = 沿锚点朝向前方多少米（大地图无朝向 → 只取锚点）；贴到地面高度。
        /// 逐字照 MapCursor：建空实体 → 造 Decal → 设材质 → AddComponent → AddDecalInstance → 强制可见。
        /// </summary>
        public static string Spawn(string materialName, float size, float distance)
        {
            Scene scene = ResolveScene(out string tag, out Vec3 anchor);
            if (scene == null)
                return "error: no active scene (neither a mission nor a loaded campaign map).";

            Material mat = Material.GetFromResource(materialName);
            if (mat == null || !mat.IsValid)
                return $"error: material '{materialName}' not found/valid. Run: custom.decal list";

            Clear();

            Vec3 pos = anchor + Forward(distance);
            float groundZ = GroundZ(scene, pos, anchor.Z);
            pos = new Vec3(pos.X, pos.Y, groundZ);

            string diag = SpawnOne(scene, mat, materialName, pos, size, null, out string err);
            if (err != null)
                return "error: " + err;

            DebugLogger.Log($"[DecalProbe] spawn '{materialName}' size={size:F2} scene={tag} pos=({pos.X:F2},{pos.Y:F2},{pos.Z:F2})");

            return string.Format(CultureInfo.InvariantCulture,
                "OK: '{0}' x{1} at ({2:F1},{3:F1},{4:F1}) scene={5} size={6:F1}m | {7} | close the console (~) to look!",
                materialName, Decals.Count, pos.X, pos.Y, pos.Z, tag, size, diag);
        }

        /// <summary>
        /// 🔴 **绕开编辑器贴图限制**：底材质复制一份 → 运行时把 DiffuseMap 换成任意贴图 → 建贴花。
        ///
        /// 【为什么需要它】编辑器里贴花材质的贴图槽**只认 decal_textures_*.xml 里注册过的贴图**
        /// （实测：`type_a_decal` 能预览、`boardgames_aserai_d` 不能）。那就意味着"自己的火焰图"
        /// 在编辑器里根本塞不进材质 —— 但这条限制**是编辑器材质面板的行为**。
        ///
        /// 引擎自己的 <c>ScenePropDecal</c> 就是这么干的：`GetFromResource(底材质).CreateCopy()`
        /// 然后 `SetTexture(DiffuseMap, 图)` —— 运行时直接塞指针，**不经过编辑器校验**。
        /// 本命令照抄它的做法，用来验证"运行时能不能用未注册的贴图"。
        ///
        /// 用法：`custom.decal tex blood_terrain_decal_3 &lt;你的贴图名&gt; [尺寸] [距离]`
        /// </summary>
        public static string SpawnTextured(string baseMaterialName, string textureName, float size, float distance)
        {
            Scene scene = ResolveScene(out string tag, out Vec3 anchor);
            if (scene == null)
                return "error: no active scene (neither a mission nor a loaded campaign map).";

            Material baseMat = Material.GetFromResource(baseMaterialName);
            if (baseMat == null || !baseMat.IsValid)
                return $"error: base material '{baseMaterialName}' not found/valid (try: blood_terrain_decal_3).";

            // 🔴 用 CheckAndGetFromResource —— 它在贴图不存在时返回 null，是引擎自己的判空口径
            //    （ScenePropDecal 就这么写的），而 GetFromResource 对缺失项的行为不可靠。
            Texture tex = Texture.CheckAndGetFromResource(textureName);
            if (tex == null)
                return $"error: texture '{textureName}' not found. (It must be an imported texture asset in a loaded module.)";

            Clear();

            Material mat;
            try
            {
                mat = baseMat.CreateCopy();
                mat.SetTexture(Material.MBTextureType.DiffuseMap, tex);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[DecalProbe] tex 建材质异常: {ex}");
                return $"error: building material copy failed: {ex.Message}";
            }

            Vec3 pos = anchor + Forward(distance);
            pos = new Vec3(pos.X, pos.Y, GroundZ(scene, pos, anchor.Z));

            string err = SpawnOne(scene, mat, $"{baseMaterialName}+{textureName}", pos, size, null, out string diag);
            if (err != null)
                return "error: " + err;

            DebugLogger.Log($"[DecalProbe] tex: base='{baseMaterialName}' diffuse<-'{textureName}' size={size:F1} "
                          + $"scene={tag} pos=({pos.X:F1},{pos.Y:F1},{pos.Z:F1}) {diag}");

            return string.Format(CultureInfo.InvariantCulture,
                "OK: decal with base '{0}' + diffuse '{1}' at ({2:F1},{3:F1},{4:F1}) size={5:F1}m scene={6} | {7} "
                + "| close the console (~) and look ahead. Renders => runtime CAN use unregistered textures.",
                baseMaterialName, textureName, pos.X, pos.Y, pos.Z, size, tag, diag);
        }

        /// <summary>
        /// **不可能看错的单张判据**（2026-09-29 立，起因：前几轮"到底渲不渲染"一直没问死）。
        ///
        /// 做法：先把旧的全隐藏 → 在**玩家脚底下**放一张 **12 米**的贴花 → 再把它染成**纯红不透明**。
        /// 关掉控制台低头看：脚下地面变红 = 运行期贴花在 Mission 场景里**明确渲染**；
        /// 一丝变化都没有 = **不渲染**（染色/UV 都不必再谈了）。
        ///
        /// 🔴 为什么是"脚下 + 12 米 + 纯红"三者一起：任一环节不确定时，单一信号都可能被误读
        ///    （贴花可能只是贴图淡、位置偏、被控制台挡住）。尺寸大到覆盖半径 12 米、
        ///    颜色改到纯红，**只要渲染就必然看得出来**。
        /// </summary>
        public static string SpawnLoud()
        {
            Scene scene = ResolveScene(out string tag, out Vec3 anchor);
            if (scene == null)
                return "error: no active scene (neither a mission nor a loaded campaign map).";

            // 自动挑第一个能加载的候选（不再死磕 fire_damage —— 它未必是最合适的那个）
            string useName = null;
            Material mat = null;
            foreach (string c in Candidates)
            {
                Material m = Material.GetFromResource(c);
                if (m != null && m.IsValid) { useName = c; mat = m; break; }
            }
            if (mat == null)
                return "error: none of the candidate decal materials load in this module set. Run: custom.decal list";

            Clear();

            Vec3 pos = new Vec3(anchor.X, anchor.Y, GroundZ(scene, anchor, anchor.Z));
            string err = SpawnOne(scene, mat, useName, pos, 12f, null, out string diag);
            if (err != null)
                return "error: " + err;

            SetColor(1f, 0f, 0f, 1f);          // 纯红不透明 —— 生效则地面明显变红

            DebugLogger.Log($"[DecalProbe] LOUD: 1 decal '{useName}' size=12 at feet ({pos.X:F1},{pos.Y:F1},{pos.Z:F1}) "
                          + $"scene={tag}, tinted pure red, {diag}");

            return string.Format(CultureInfo.InvariantCulture,
                "OK: ONE decal ('{0}'), size 12m, AT YOUR FEET ({1:F1},{2:F1},{3:F1}), tinted PURE RED. scene={4}. "
                + "Close the console (~) and LOOK DOWN. Ground turns red => runtime decals DO render here. "
                + "Nothing at all => they do NOT render (stop chasing texture/UV).",
                useName, pos.X, pos.Y, pos.Z, tag);
        }

        /// <summary>
        /// **把候选材质全铺一遍** —— 一次回答两个卡住我们的问题：
        ///   ① 哪些资源名在**本模块集**里真能加载（结果进日志：`material invalid` / `null`）；
        ///   ② 同一个场景里到底能不能同时存在**多张**运行期贴花（还是只有第一张会渲染）。
        /// 能加载的按 6 列排成网格铺在面前，**每项都单独写日志**（控制台那行会被面板宽度截断）。
        ///
        /// 判据：**铺出来几张就显几张** = 机制没问题；**只第 1 张显** = 引擎只认第一张
        /// （那就改用 MapCursor 的 `CreateCopy()` 路线，或复用同一实例靠移动位置复用）。
        /// </summary>
        public static string SpawnRow(string[] overrideNames = null)
        {
            Scene scene = ResolveScene(out string tag, out Vec3 anchor);
            if (scene == null)
                return "error: no active scene (neither a mission nor a loaded campaign map).";

            Clear();

            string[] names = (overrideNames != null && overrideNames.Length > 0) ? overrideNames : Candidates;

            Vec3 fwd = Forward(1f);
            Vec3 right = new Vec3(fwd.Y, -fwd.X, 0f);     // 水平右向（左手系：把前向转 -90°）
            if (right.Length < 1e-3f) right = new Vec3(0f, 1f, 0f);

            // 🔴 列数必须按**实际条目数**算，不能写死 6 —— 写死过一次：只传 2 个材质时
            //    仍按"6 列居中"排版，两张被推到侧前方十几米外，正前方什么都看不见
            //    （2026-09-29 实机踩到，用户原话"你这个指令是个垃圾"）。
            int cols = names.Length < 6 ? names.Length : 6;
            if (cols < 1) cols = 1;
            const float gap = 5f;
            const float ahead = 8f;
            const float size = 4f;

            int made = 0, invalid = 0, failed = 0;
            Decal template = null;                        // 第 1 张 CreateDecal，其余照原版 MapScreen 用 CreateCopy
            StringBuilder placed = new StringBuilder();

            foreach (string name in names)
            {
                Material mat = Material.GetFromResource(name);
                if (mat == null || !mat.IsValid)
                {
                    invalid++;
                    DebugLogger.Log($"[DecalProbe] row '{name}': Material.GetFromResource -> "
                                  + (mat == null ? "null" : "invalid"));
                    continue;
                }

                int col = made % cols, row = made / cols;
                float dx = (col - (cols - 1) * 0.5f) * gap;
                float dz = ahead + row * gap;
                Vec3 pos = anchor + fwd * dz + right * dx;
                pos = new Vec3(pos.X, pos.Y, GroundZ(scene, pos, anchor.Z));

                string err = SpawnOne(scene, mat, name, pos, size, template, out string diag);
                if (err != null)
                {
                    failed++;
                    DebugLogger.Log($"[DecalProbe] row '{name}': {err}");
                    continue;
                }
                if (template == null)
                    template = Decals[Decals.Count - 1];
                made++;
                placed.Append(' ').Append(made).Append('=').Append(name)
                      .AppendFormat(CultureInfo.InvariantCulture, "({0:F0}m前,{1:+0;-0;0}m侧)", dz, dx);
            }

            DebugLogger.Log($"[DecalProbe] row in scene={tag}: made={made} invalid={invalid} failed={failed} "
                          + $"| 判据：铺出几张就显几张=机制没问题；只第 1 张显=引擎只认第一张");

            return string.Format(CultureInfo.InvariantCulture,
                "OK: row made={0} invalid={1} failed={2} scene={3} | close the console (~) and look STRAIGHT AHEAD "
                + "({4}m decals, {5}m apart, {6} per row) | placed:{7}",
                made, invalid, failed, tag, size, gap, cols, placed);
        }

        /// <summary>真正的建造（一个材质一张），返回诊断串；失败时 err 非 null。</summary>
        private static string SpawnOne(Scene scene, Material mat, string name, Vec3 pos, float size,
                                       Decal template, out string err)
        {
            err = null;
            try
            {
                GameEntity entity = GameEntity.CreateEmpty(scene, true);
                entity.Name = "lwnDecalProbe_" + name;

                // 🔴 与 MapCursor 一致：第一张 CreateDecal，其余 CreateCopy。
                //    原版 MapScreen 建多张轮廓贴花时就是这么做的（一 Create + 三 Copy）——
                //    怀疑引擎对「从零 CreateDecal」有限制，先照抄它的做法。
                bool copied = template != null;
                Decal decal = copied ? template.CreateCopy() : Decal.CreateDecal(null);
                if (decal == null)
                {
                    err = copied ? "Decal.CreateCopy returned null" : "Decal.CreateDecal returned null";
                    DebugLogger.Log($"[DecalProbe] SpawnOne('{name}') 失败: {err}");
                    return null;
                }

                decal.SetMaterial(mat);
                entity.AddComponent(decal);
                scene.AddDecalInstance(decal, "editor_set", true);

                // 局部帧扛尺寸，实体全局帧扛位置（与 MapCursor 同构）
                MatrixFrame local = new MatrixFrame(Mat3.Identity, Vec3.Zero);
                local.Scale(new Vec3(size, size, size));
                decal.SetFrame(local);
                entity.SetGlobalFrame(new MatrixFrame(Mat3.Identity, pos));

                // 🔴 显式置可见 —— MapCursor 每次显隐都手写这一句，新手建的实体默认可见性不敢赌
                bool visBefore = entity.GetVisibilityExcludeParents();
                entity.SetVisibilityExcludeParents(true);
                bool visAfter = entity.GetVisibilityExcludeParents();
                bool liveVisible = entity.IsVisibleIncludeParents();

                // 🔴 诊断一律进日志（控制台那行会被面板宽度截断，之前就这么瞎了一轮）
                DebugLogger.Log(string.Format(CultureInfo.InvariantCulture,
                    "[DecalProbe] +1 '{0}' {1} pos=({2:F1},{3:F1},{4:F1}) size={5:F1} visFlag={6}->{7} liveVisible={8} total={9}",
                    name, copied ? "COPY" : "CREATE", pos.X, pos.Y, pos.Z, size,
                    visBefore, visAfter, liveVisible, Entities.Count + 1));

                Entities.Add(entity);
                Decals.Add(decal);
                Names.Add(name);

                return string.Format(CultureInfo.InvariantCulture, "{0} vis={1}", copied ? "copy" : "create", liveVisible);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[DecalProbe] SpawnOne('{name}') 异常: {ex}");
                err = ex.Message;
                return null;
            }
        }

        /// <summary>锚点水平朝前方向（Mission 取玩家脸朝向；大地图/无朝向 → +X）。</summary>
        private static Vec3 Forward(float distance)
        {
            Vec3 look = Vec3.Zero;
            Mission mission = Mission.Current;
            if (mission != null && Agent.Main != null)
            {
                try { look = Agent.Main.LookDirection; } catch { look = Vec3.Zero; }
            }
            Vec3 flat = new Vec3(look.X, look.Y, 0f);
            if (flat.Length < 1e-3f)
                flat = new Vec3(1f, 0f, 0f);
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
                if (float.IsNaN(z) || z > 1e5f || z < -1e5f)
                    return fallbackZ;
                return z;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[DecalProbe] 取地面高度异常，退回 {fallbackZ:F1}: {ex.Message}");
                return fallbackZ;
            }
        }

        // ── 收掉 / 手改 / 手摇 ────────────────────────────────────────────────

        /// <summary>收掉。引擎没暴露「删单张贴花」的 API（只有 `Scene.ClearDecals()` 会把全场贴花清光，
        /// 血渍也一起没），所以这里只隐藏 —— 眼不见为净，也不误伤别人的贴花。</summary>
        public static string Clear()
        {
            if (Entities.Count > 0)
                DebugLogger.Log($"[DecalProbe] clear: hiding {Entities.Count} decal(s) [{string.Join(",", Names.ToArray())}]");
            for (int i = 0; i < Entities.Count; i++)
            {
                try { Entities[i]?.SetVisibilityExcludeParents(false); }
                catch (Exception ex) { DebugLogger.Log($"[DecalProbe] 隐藏异常: {ex.Message}"); }
            }
            Entities.Clear();
            Decals.Clear();
            Names.Clear();
            _hasUv = false;
            _hasCol = false;
            _animMode = 0;
            return "OK: decal(s) cleared.";
        }

        public static string SetUv(float sx, float sy, float ox, float oy)
        {
            if (!HasDecal)
                return "error: no decal yet - spawn one first (custom.decal)";
            _uvSx = sx; _uvSy = sy; _uvOx = ox; _uvOy = oy;
            _hasUv = true;
            ApplyUv(ox, oy);
            return string.Format(CultureInfo.InvariantCulture,
                "OK: uv = ({0:F3},{1:F3},{2:F3},{3:F3}) on {4} decal(s). "
                + "(1,1,0,0) = the whole texture/atlas sheet at once - if you see the sheet, the UV channel is live.",
                sx, sy, ox, oy, Decals.Count);
        }

        public static string SetColor(float r, float g, float b, float a)
        {
            if (!HasDecal)
                return "error: no decal yet - spawn one first (custom.decal)";
            _colR = r; _colG = g; _colB = b; _colA = a;
            _hasCol = true;
            ApplyColor(a);
            return string.Format(CultureInfo.InvariantCulture,
                "OK: color = ({0:F2},{1:F2},{2:F2},{3:F2}) [alpha last] on {4} decal(s).", r, g, b, a, Decals.Count);
        }

        public static string SetAnim(int mode)
        {
            if (!HasDecal)
                return "error: no decal yet - spawn one first (custom.decal)";
            _animMode = mode;
            _phase = 0f;
            string extra = string.Empty;
            if (mode == 1 && !_hasUv)
                extra = " (uv baseline not set -> using 1,1,0,0 = whole sheet; on an atlas material you will see atlas neighbours scroll by - expected, the point is that the channel moves)";
            if (mode == 2 && !_hasCol)
                extra = " (color baseline defaults to 1,1,1,1)";
            if (Mission.Current == null)
                extra += " [note: on the campaign map there is no mission tick -> anim will NOT run here]";
            return "OK: anim = " + (mode == 1 ? "uv-scroll" : mode == 2 ? "alpha-pulse" : "off") + extra;
        }

        private static void ApplyUv(float ox, float oy)
        {
            for (int i = 0; i < Decals.Count; i++)
            {
                try { Decals[i]?.SetVectorArgument(_uvSx, _uvSy, ox, oy); }
                catch (Exception ex) { DebugLogger.Log($"[DecalProbe] SetVectorArgument 异常: {ex.Message}"); }
            }
        }

        private static void ApplyColor(float a)
        {
            uint packed = new Color(_colR, _colG, _colB, a).ToUnsignedInteger();
            for (int i = 0; i < Decals.Count; i++)
            {
                try { Decals[i]?.SetFactor1(packed); }
                catch (Exception ex) { DebugLogger.Log($"[DecalProbe] SetFactor1 异常: {ex.Message}"); }
            }
        }

        /// <summary>每帧手摇（由 <see cref="DecalProbeBehavior"/> 在 Mission 里调）。</summary>
        public static void Tick(float dt)
        {
            if (_animMode == 0 || !HasDecal)
                return;
            try
            {
                _phase += dt;
                if (_animMode == 1)
                {
                    float ox = _uvOx + _phase * 0.25f;
                    ox -= (float)Math.Floor(ox);
                    ApplyUv(ox, _uvOy);
                }
                else
                {
                    float k = 0.625f + 0.375f * (float)Math.Sin(_phase * 6.0);
                    ApplyColor(_colA * k);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[DecalProbe] tick 异常，停手摇: {ex.Message}");
                _animMode = 0;
            }
        }

        /// <summary>状态一行（英文，给控制台回显）。</summary>
        public static string Describe()
        {
            if (!HasDecal)
                return "no decal";
            string anim = _animMode == 1 ? "uv-scroll" : _animMode == 2 ? "alpha-pulse" : "off";
            return string.Format(CultureInfo.InvariantCulture,
                "{0} decal(s) [{1}] anim={2}", Decals.Count, string.Join(",", Names.ToArray()), anim);
        }

        /// <summary>默认材质（本模块集里唯一确认能加载到的贴花材质）。</summary>
        private const string DefaultMaterial = "fire_damage";

        /// <summary>
        /// 候选材质 —— 🔴 **只列 `decal_textures_*.xml` 里 `is_dynamic="true"` 的那些**。
        ///
        /// **这是唯一正确的候选集**（2026-09-29 实机定案，血泪）：
        /// 运行期用 `Decal.CreateDecal()` 建的贴花，**只有动态图集条目会渲染**；
        /// 静态条目「建得出来、不报错、画面空白」。实测印证：用户试 `blood_terrain_decal_9` / `_0` 立刻出画，
        /// 而 `fire_damage` / `ashes_a` / `plain_white` 等（全静态）一片空白。
        ///
        /// ❌ 别再犯的两个错（我都犯过）：
        ///   ① 从 `decal_textures_*.xml` 剥 `_d/_n/_s` 后缀推材质名 —— 那是**贴图条目**，不等于材质；
        ///   ② 把 `decal_textures_all.xml` 当公共候选池 —— 它的动态条目数是 **0**。
        /// </summary>
        public static readonly string[] Candidates =
        {
            // battle / town / multiplayer 三张图集里都动态的 —— 血/伤/焦痕一族（12 种）
            "blood_terrain_decal_0", "blood_terrain_decal_1", "blood_terrain_decal_2",
            "blood_terrain_decal_3", "blood_terrain_decal_4", "blood_terrain_decal_5",
            "blood_terrain_decal_6", "blood_terrain_decal_7", "blood_terrain_decal_8",
            "blood_terrain_decal_9", "blood_terrain_decal_10", "blood_terrain_decal_11",
            // 雪地脚印（town 动态）
            "decal_snow_footprint",
            // worldmap 动态 —— 原版 MapScreen 运行期造攻城器械圈用的就是前三个
            "decal_siege_ram", "decal_siege_ranged", "decal_siege_tower",
            "map_track_arrow",
        };
    }

    /// <summary>Mission 侧驱动（大地图没有 Mission，那边只生成不手摇）。</summary>
    public class DecalProbeBehavior : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;
        public override void OnMissionTick(float dt) => DecalProbe.Tick(dt);
    }

    /// <summary>
    /// 控制台入口 `custom.decal`（2026-09-29）。用法与机制见本文件头部注释。
    /// </summary>
    public static class DecalProbeCommands
    {
        private const string DefaultMaterial = "fire_damage";
        private const float DefaultSize = 5f;
        private const float DefaultDistance = 8f;

        [TaleWorlds.Library.CommandLineFunctionality.CommandLineArgumentFunction("decal", "custom")]
        public static string ExecuteDecal(List<string> args)
        {
            string a0 = (args != null && args.Count >= 1) ? (args[0] ?? string.Empty).Trim() : string.Empty;
            string note = string.Empty;

            switch (a0.ToLowerInvariant())
            {
                case "":
                    break;

                case "l":
                case "list":
                    {
                        // 🔴 只把「能加载的」打回控制台 —— 73 个名字全打会被面板宽度截断，
                        //    完整结果进日志（2026-09-29 为截断瞎过一轮）。
                        StringBuilder ok = new StringBuilder();
                        int n = 0;
                        foreach (string c in DecalProbe.Candidates)
                        {
                            Material m = Material.GetFromResource(c);
                            bool valid = m != null && m.IsValid;
                            if (valid)
                            {
                                n++;
                                ok.Append(' ').Append(c);
                            }
                            DebugLogger.Log($"[DecalProbe] list '{c}': {(valid ? "OK" : "missing")}");
                        }
                        return string.Format(CultureInfo.InvariantCulture,
                            "OK: {0}/{1} candidate decal materials load. loadable:{2} | usage: custom.decal <material> [size_m] [distance_m] | status: {3} | full list -> Debug/StoryEngine_RuntimeLog.txt [DecalProbe]",
                            n, DecalProbe.Candidates.Length, ok, DecalProbe.Describe());
                    }

                case "grid":
                case "row":
                    {
                        // 给了材质名就只铺这些（A/B 对照用）；没给就铺内置候选表
                        List<string> extra = null;
                        if (args != null)
                        {
                            for (int i = 1; i < args.Count; i++)
                            {
                                if (string.IsNullOrWhiteSpace(args[i]) || LooksLikePlaceholder(args[i])) continue;
                                (extra ?? (extra = new List<string>())).Add(args[i].Trim());
                            }
                        }
                        return DecalProbe.SpawnRow(extra?.ToArray());
                    }

                case "loud":
                case "big":
                    return DecalProbe.SpawnLoud();

                case "tex":
                case "texture":
                    {
                        if (args == null || args.Count < 3)
                            return "error: usage: custom.decal tex <base_material> <texture_name> [size_m] [distance_m]";
                        float tsize = 6f, tdist = 8f;
                        if (args.Count >= 4) TryF(args[3], out tsize);
                        if (args.Count >= 5) TryF(args[4], out tdist);
                        if (tsize <= 0f) tsize = 6f;
                        if (tdist <= 0f) tdist = 8f;
                        return DecalProbe.SpawnTextured(args[1].Trim(), args[2].Trim(), tsize, tdist);
                    }

                case "clear":
                case "off":
                    return DecalProbe.Clear();

                case "status":
                case "info":
                    return "OK: " + DecalProbe.Describe();

                case "anim":
                case "anim_off":
                case "anim_uv":
                case "anim_pulse":
                    {
                        string modeArg = a0.ToLowerInvariant();
                        if (modeArg == "anim")
                            modeArg = (args != null && args.Count >= 2) ? args[1].ToLowerInvariant() : "off";
                        int mode = modeArg == "uv" ? 1 : modeArg == "pulse" ? 2 : 0;
                        if (modeArg != "uv" && modeArg != "pulse" && modeArg != "off" && modeArg != "0" && modeArg != "1" && modeArg != "2")
                            note = $" [note: anim mode '{modeArg}' unknown -> off; use: off | uv | pulse]";
                        return DecalProbe.SetAnim(mode) + note;
                    }

                case "uv":
                    {
                        if (args.Count < 5)
                            return "error: usage: custom.decal uv <scale_x> <scale_y> <offset_x> <offset_y>";
                        float sx, sy, ox, oy;
                        if (!TryF(args[1], out sx) || !TryF(args[2], out sy) || !TryF(args[3], out ox) || !TryF(args[4], out oy))
                            return "error: uv args must be 4 numbers.";
                        return DecalProbe.SetUv(sx, sy, ox, oy);
                    }

                case "color":
                case "colour":
                    {
                        if (args.Count < 5)
                            return "error: usage: custom.decal color <r> <g> <b> <a>  (0..1 each)";
                        float r, g, b, a;
                        if (!TryF(args[1], out r) || !TryF(args[2], out g) || !TryF(args[3], out b) || !TryF(args[4], out a))
                            return "error: color args must be 4 numbers (0..1).";
                        return DecalProbe.SetColor(r, g, b, a);
                    }
            }

            // ── 召唤（首参可弃：认不出的材质名回落默认并注明） ──────────────
            string material = a0;
            float size = DefaultSize;
            float dist = DefaultDistance;

            if (args != null && args.Count >= 2)
            {
                float v;
                if (TryF(args[1], out v) && v > 0f) size = v;
                else note += $" [note: '{args[1]}' is not a size -> {DefaultSize:F1}m]";
            }
            if (args != null && args.Count >= 3)
            {
                float v;
                if (TryF(args[2], out v) && v > 0f) dist = v;
                else note += $" [note: '{args[2]}' is not a distance -> {DefaultDistance:F1}m]";
            }

            if (string.IsNullOrEmpty(material) || LooksLikePlaceholder(material))
            {
                note += $" [note: '{material}' is not a material name -> using {DefaultMaterial}]";
                material = DefaultMaterial;
            }

            return DecalProbe.Spawn(material, size, dist) + note;
        }

        /// <summary>首参可弃判据：纯数字 / 单个字符这种「随手补的占位」不当作材质名。</summary>
        private static bool LooksLikePlaceholder(string s)
        {
            if (s.Length <= 1)
                return true;
            float dummy;
            return TryF(s, out dummy);
        }

        private static bool TryF(string s, out float v)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }
}
