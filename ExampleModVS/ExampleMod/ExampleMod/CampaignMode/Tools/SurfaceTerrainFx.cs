// ═══════════════════════════════════════════════════════════════════════════
// 元素地表：**地形图层**探测与写入 —— 控制台入口 `custom.surface terrain`
// （2026-09-30，验证《元素地表系统》§3.1 那条被推翻的旧结论）
//
// 【为什么有它】
//   引擎的地形本身就是「每层一张权重图 + 逐层 over 合成」，天生带**渐变**与**优先级覆盖**：
//     · 从高序号层往低序号走：最终色 += 本层色 × 本层权重 × 剩余不透明度；剩余不透明度 ×= (1 − 本层权重)
//       ⇒ 上层权重 0.5 时自动占一半、下层拿剩下一半 = **平滑过渡是 over 合成的自然结果**
//     · 过渡带宽度 = 一个独立参数 SmoothBlendAmount（把权重图的 [0,S] 区间重拉到 [0,1]）
//   这套东西如果运行期能写，就是「元素地表」的第二条路，而且好几处比贴花强（真替换而非加亮、
//   连续网格天生无缝、零额外几何）。旧结论「运行时没有任何接口能读写它」是**错的** ——
//   反编译已挖到 native 桥接 ITerrainEdit 全套方法（建地形/加层/换贴图/改属性/写权重/写高度）。
//
// 【本文件的定位：一次最小验证，不是成品】
//   要回答的核心问题只有一个：**游戏运行时（非编辑器）到底能不能写图层权重。**
//
// 🔴 【风险，必须先读 —— 同族接口有崩溃前科】
//   本项目 2026-09-07 实测（见 TerrainExportCommands.cs）：
//     · `Scene.GetTerrainHeightData`（读整格高度）对织丰 = **direct native crash**，
//       托管 try/catch 包不住、引擎 crash handler 都不弹 ⇒ 已永久禁用
//     · `Scene.GetTerrainNodeData` 在客户端返回无效值 ⇒ 元数据读不到
//   本文件走的是**另一个假设**：权重图是**渲染必需**的（地形 shader 每帧采样它），
//   所以那块数据结构在客户端一定活着 ⇒ 写它比读整格高度更可能安全。
//   **这是推理，不是实证。** 万一它同样有 native 崩溃风险，症状会是「游戏直接消失」——
//   在**战斗场景**里试（崩了重启即可，不碰存档）。
//
// 🔴 【为什么走反射，而不是 new TerrainEditContext】
//   挖到的 C# 包装类 `TaleWorlds.Engine.TerrainEditContext` 的构造函数**第一件事就是
//   CreateTerrain**（按传进来的维度/尺寸/高度范围**重建整块地形**）⇒ 对已有战场地形用它
//   等于把地形整个换掉，不是我们要的。
//   而 native 桥接是**按 scenePointer 工作**的，本来就不需要先建地形 ⇒
//   直接反射调 internal 的 `EngineApplicationInterface.ITerrainEdit`，绕开构造函数。
//
// 【命令】
//   custom.surface terrain                      只读探测（零风险，先跑这个看地形参数）
//   custom.surface terrain weight <层> <值> [x] [y]
//                                               把节点 (x,y) 上第 <层> 的权重全部写成 <值>
//   custom.surface terrain finalize             调 native Finalize（写完没反应时试它）
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.CampaignMode.Tools
{
    /// <summary>地形图层探测与写入（最小验证件）。详见文件头。</summary>
    public static class SurfaceTerrainFx
    {
        // ── 反射句柄（懒绑定，只解一次）────────────────────────────────────
        private static object _isScene;        // EngineApplicationInterface.IScene
        private static object _iTerrainEdit;   // EngineApplicationInterface.ITerrainEdit
        private static string _bindErr;

        private const BindingFlags AnyStatic =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>
        /// 拿 internal 的 EngineApplicationInterface 下两个桥接单例。
        /// 失败不抛 —— 把原因记在 <see cref="_bindErr"/> 里由命令回报。
        /// </summary>
        private static bool Bind()
        {
            if (_iTerrainEdit != null && _isScene != null) return true;
            if (_bindErr != null) return false;
            try
            {
                Assembly asm = typeof(Scene).Assembly;   // TaleWorlds.Engine
                Type eai = asm.GetType("TaleWorlds.Engine.EngineApplicationInterface", false);
                if (eai == null) { _bindErr = "type EngineApplicationInterface not found"; return false; }

                FieldInfo fs = eai.GetField("IScene", AnyStatic);
                FieldInfo ft = eai.GetField("ITerrainEdit", AnyStatic);
                _isScene = fs != null ? fs.GetValue(null) : null;
                _iTerrainEdit = ft != null ? ft.GetValue(null) : null;

                if (_iTerrainEdit == null) { _bindErr = "ITerrainEdit field missing or null"; return false; }
                if (_isScene == null) { _bindErr = "IScene field missing or null"; return false; }
                return true;
            }
            catch (Exception ex)
            {
                _bindErr = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static object Call(object target, string method, params object[] args)
        {
            MethodInfo mi = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public);
            if (mi == null) throw new MissingMethodException(target.GetType().Name + "." + method);
            return mi.Invoke(target, args);
        }

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static Scene CurrentScene()
        {
            try { return Mission.Current != null ? Mission.Current.Scene : null; }
            catch { return null; }
        }

        // ── ① 只读探测（零风险）──────────────────────────────────────────
        /// <summary>
        /// 把场景地形能读到的参数全打出来：有没有地形 / 几层 / 节点维度与尺寸 / 每节点顶点数 /
        /// 反射通不通 / 节点数据计数多少。**全是只读调用**，用来给后续写入定参数。
        /// </summary>
        public static string Info()
        {
            Scene scene = CurrentScene();
            if (scene == null) return "no mission scene (enter a battle/scene first).";

            StringBuilder sb = new StringBuilder();
            bool has;
            try { has = scene.ContainsTerrain; }
            catch (Exception ex) { return "ContainsTerrain threw: " + ex.Message; }

            sb.Append("ContainsTerrain=").Append(has);
            try { sb.Append("  HasTerrainHeightmap=").Append(scene.HasTerrainHeightmap); }
            catch { }
            if (!has) return sb.Append("  (this scene has no terrain)").ToString();

            try
            {
                scene.GetTerrainData(out Vec2i dim, out float nodeSize, out int layerCount, out int layerVersion);
                sb.Append("\n  nodeDim=").Append(dim.X).Append('x').Append(dim.Y)
                  .Append("  nodeSize=").Append(F(nodeSize)).Append("m")
                  .Append("  layers=").Append(layerCount)
                  .Append("  layerVersion=").Append(layerVersion)
                  .Append("\n  world extent ~").Append(F(dim.X * nodeSize)).Append(" x ").Append(F(dim.Y * nodeSize)).Append(" m")
                  .Append("\n  (nodeSize is the per-node world size; 0 means the client did not fill it)");
            }
            catch (Exception ex) { sb.Append("\n  GetTerrainData threw: ").Append(ex.Message); }

            try
            {
                scene.GetTerrainNodeData(0, 0, out int vtx, out float quadLen, out float mn, out float mx);
                sb.Append("\n  node(0,0): vertexCountAlongAxis=").Append(vtx)
                  .Append("  quadLength=").Append(F(quadLen))
                  .Append("  minH=").Append(F(mn)).Append("  maxH=").Append(F(mx));
                if (vtx <= 0 || quadLen <= 0)
                    sb.Append("   <== INVALID in client (known since 2026-09-07)");
            }
            catch (Exception ex) { sb.Append("\n  GetTerrainNodeData threw: ").Append(ex.Message); }

            if (!Bind())
            {
                sb.Append("\n  reflection: FAILED - ").Append(_bindErr);
                return sb.ToString();
            }

            sb.Append("\n  reflection: OK (IScene + ITerrainEdit bound)");

            try
            {
                object n = Call(_isScene, "GetNodeDataCount", scene, 0, 0);
                sb.Append("\n  GetNodeDataCount(0,0)=").Append(n)
                  .Append("   <== this is the array length for one node; <=0 means we cannot size the write");
            }
            catch (Exception ex) { sb.Append("\n  GetNodeDataCount threw: ").Append(ex.Message); }

            return sb.ToString();
        }

        // ── ② 写一个节点的图层权重（未验证，可能无效或崩）──────────────────
        /// <summary>
        /// 把节点 (nodeX, nodeY) 上第 layer 层的权重**全部**写成 value（0~1）。
        /// 数组长度取自 <c>GetNodeDataCount</c>（同一套网格，与高度数据等长）；
        /// 长度读不到就直接不写 —— 长度错了可能越界写，比"没效果"严重得多。
        /// </summary>
        public static string Weight(int layer, float value, int nodeX, int nodeY)
        {
            Scene scene = CurrentScene();
            if (scene == null) return "error: no mission scene (enter a battle/scene first).";
            if (!Bind()) return "error: reflection failed - " + _bindErr;

            int n;
            try { n = Convert.ToInt32(Call(_isScene, "GetNodeDataCount", scene, nodeX, nodeY)); }
            catch (Exception ex) { return "error: GetNodeDataCount threw: " + ex.Message; }

            if (n <= 0)
                return string.Format(CultureInfo.InvariantCulture,
                    "error: GetNodeDataCount({0},{1}) = {2} -- cannot size the weight array, refusing to write "
                    + "(a wrong length could write out of bounds). Try `custom.surface terrain` first.", nodeX, nodeY, n);

            if (n > 4 * 1024 * 1024)
                return string.Format(CultureInfo.InvariantCulture,
                    "error: node data count {0} is implausibly large, refusing.", n);

            float v = value < 0f ? 0f : (value > 1f ? 1f : value);
            float[] w = new float[n];
            for (int i = 0; i < n; i++) w[i] = v;

            try
            {
                Call(_iTerrainEdit, "SetNodeLayerWeightData", scene, nodeX, nodeY, layer, w, n);
            }
            catch (Exception ex)
            {
                return "error: SetNodeLayerWeightData threw: " + ex.GetType().Name + ": " + ex.Message;
            }

            DebugLogger.Log(string.Format(CultureInfo.InvariantCulture,
                "[SurfaceTerrain] write layer={0} value={1} node=({2},{3}) samples={4}", layer, v, nodeX, nodeY, n));

            return string.Format(CultureInfo.InvariantCulture,
                "OK: wrote layer {0} weight = {1} to node ({2},{3}), {4} samples.\n"
                + "  LOOK AT THE GROUND around you. Three possible outcomes:\n"
                + "    · the terrain visibly changed  -> write works, we have a real path\n"
                + "    · nothing changed             -> try `custom.surface terrain finalize`\n"
                + "    · the game vanished            -> native gate/crash; this path is dead in client", layer, v, nodeX, nodeY, n);
        }

        // ── ③ 提交 ────────────────────────────────────────────────────────
        /// <summary>调 native 的 Finalize（写完没反应时试它 —— 可能是"要提交才重建渲染数据"）。</summary>
        public static string FinalizeEdit()
        {
            Scene scene = CurrentScene();
            if (scene == null) return "error: no mission scene.";
            if (!Bind()) return "error: reflection failed - " + _bindErr;

            try
            {
                PropertyInfo pi = typeof(Scene).GetProperty("Pointer",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi == null) return "error: Scene.Pointer not reflectable.";
                object ptr = pi.GetValue(scene);
                Call(_iTerrainEdit, "Finalize", ptr);
                return "OK: Finalize called.";
            }
            catch (Exception ex)
            {
                return "error: Finalize threw: " + ex.GetType().Name + ": " + ex.Message;
            }
        }
    }
}
