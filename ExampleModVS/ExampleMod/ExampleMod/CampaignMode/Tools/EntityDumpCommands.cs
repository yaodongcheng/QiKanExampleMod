using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.CampaignMode
{
    /// <summary>
    /// 场景实体探针（2026-11-11）：打印当前 Mission 场景里某个 game_entity 的**全部坐标**，
    /// 有 metamesh 的话连它自己的 local 变换与包围盒一起打印。
    ///
    ///   custom.entity new_pie_train_43_2     按名字打印（先精确匹配，再子串）
    ///   custom.entity                        无参数 ⇒ 打印玩家 5 m 内最近的命名实体
    ///   custom.entity pie_train 5            名字子串 + 最多打印 5 个匹配
    ///
    /// 每个实体打印：
    ///   · local  —— 相对父实体的位置/欧拉角/缩放（GameEntity.GetFrame）
    ///   · global —— 世界位置/欧拉角/缩放（GameEntity.GetGlobalFrame + GetGlobalScale）
    ///   · metamesh[i] —— 组件自己的 local 变换（MetaMesh.Frame）+ 网格数
    ///                  + 局部包围盒（实体空间）+ 世界包围盒
    ///   · children —— 前 10 个子实体的名字与本地位置
    ///
    /// 世界包围盒 = 实体全局帧 × metamesh 帧 × 各 Mesh 局部包围盒。它就是「这坨几何实际占在哪」，
    /// 判贴地/浮空看它的 min.z，不看实体原点（原点常常在物体半高）。
    ///
    /// 🔴 输出纯英文（控制台纪律）；首参可弃（给 "1" 且查不到同名实体时回落最近的实体，不报错）。
    /// </summary>
    public class EntityDumpCommands
    {
        private const int MaxMatches = 3;
        private const int MaxChildren = 10;

        [CommandLineFunctionality.CommandLineArgumentFunction("entity", "custom")]
        public static string DumpEntity(List<string> args)
        {
            Mission mission = Mission.Current;
            if (mission == null) return "Error: no mission. Enter a scene first.";
            Scene scene = mission.Scene;
            if (scene == null) return "Error: mission has no scene.";

            string name = (args != null && args.Count >= 1) ? (args[0] ?? string.Empty).Trim() : string.Empty;
            int maxMatches = MaxMatches;
            if (args != null && args.Count >= 2)
                int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out maxMatches);
            if (maxMatches <= 0) maxMatches = 1;

            var all = new List<GameEntity>();
            try { scene.GetEntities(ref all); }
            catch (Exception ex) { return "GetEntities threw: " + ex.Message; }

            var exact = new List<GameEntity>();
            var partial = new List<GameEntity>();
            foreach (GameEntity e in all)
            {
                if (e == null) continue;
                string n;
                try { n = e.Name; } catch (Exception) { continue; }
                if (string.IsNullOrEmpty(n)) continue;
                if (name.Length == 0) continue;
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) exact.Add(e);
                else if (n.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) partial.Add(e);
            }

            List<GameEntity> hits = exact.Count > 0 ? exact : partial;
            string how = exact.Count > 0 ? "exact" : "substring";

            // 无参数 / 查不到 → 回落到玩家附近最近的命名实体
            if (name.Length == 0 || hits.Count == 0)
            {
                Agent me = Agent.Main;
                if (me == null)
                    return name.Length == 0
                        ? "Usage: custom.entity <name>  (or stand in a scene to use the nearest-entity fallback)"
                        : "No entity named or containing '" + name + "'. (no Agent.Main for a fallback)";

                Vec3 p = me.Position;
                GameEntity best = null;
                float bestD = float.MaxValue;
                foreach (GameEntity e in all)
                {
                    if (e == null) continue;
                    string n;
                    try { n = e.Name; } catch (Exception) { continue; }
                    if (string.IsNullOrEmpty(n)) continue;
                    Vec3 gp;
                    try { gp = e.GlobalPosition; } catch (Exception) { continue; }
                    float dx = gp.x - p.x, dy = gp.y - p.y, dz = gp.z - p.z;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (d < bestD) { bestD = d; best = e; }
                }

                if (best == null) return "No named entity found in this scene.";
                var fb = new StringBuilder();
                fb.Append("[entity] '").Append(name).Append("' -> no match; fallback = nearest named entity (")
                  .Append(F(bestD)).Append(" m from player)\n");
                AppendEntity(fb, best, 1);
                string text = fb.ToString();
                try { DebugLogger.Log("[Entity]\n" + text); } catch (Exception) { }
                return text;
            }

            var sb = new StringBuilder();
            sb.Append("[entity] '").Append(name).Append("' -> ").Append(hits.Count).Append(" match(es) by ").Append(how)
              .Append(", showing ").Append(Math.Min(maxMatches, hits.Count)).Append('\n');
            int shown = 0;
            foreach (GameEntity e in hits)
            {
                if (shown >= maxMatches) break;
                shown++;
                AppendEntity(sb, e, shown);
            }

            string result = sb.ToString();
            try { DebugLogger.Log("[Entity]\n" + result); } catch (Exception) { }
            return result;
        }

        private static void AppendEntity(StringBuilder sb, GameEntity e, int index)
        {
            string n = "-", parentName = "-";
            try { n = e.Name ?? "-"; } catch (Exception) { }
            try { parentName = e.Parent != null ? (e.Parent.Name ?? "-") : "<none>"; } catch (Exception) { }

            int childCount = 0;
            try { childCount = e.ChildCount; } catch (Exception) { }

            sb.Append('#').Append(index).Append(" name=").Append(n)
              .Append("  parent=").Append(parentName)
              .Append("  children=").Append(childCount).Append('\n');

            try
            {
                MatrixFrame local = e.GetFrame();
                AppendFrame(sb, "   local ", local);
            }
            catch (Exception ex) { sb.Append("   local  threw: ").Append(ex.Message).Append('\n'); }

            MatrixFrame global = MatrixFrame.Identity;
            try
            {
                global = e.GetGlobalFrame();
                AppendFrame(sb, "   global", global);
                Vec3 gs = e.GetGlobalScale();
                sb.Append("   global scale=(").Append(F(gs.x)).Append(", ").Append(F(gs.y)).Append(", ").Append(F(gs.z)).Append(")\n");
            }
            catch (Exception ex) { sb.Append("   global threw: ").Append(ex.Message).Append('\n'); }

            int comps = 0;
            try { comps = e.MultiMeshComponentCount; } catch (Exception) { }
            sb.Append("   metamesh components=").Append(comps).Append('\n');

            for (int i = 0; i < comps; i++)
            {
                try
                {
                    MetaMesh mm = e.GetMetaMesh(i);
                    if (mm == null) { sb.Append("   metamesh[").Append(i).Append("] null\n"); continue; }

                    MatrixFrame mf = mm.Frame;
                    AppendFrame(sb, "   metamesh[" + i + "] local", mf);
                    sb.Append("      meshes=").Append(mm.MeshCount).Append('\n');

                    if (TryLocalBounds(mm, out Vec3 lmn, out Vec3 lmx))
                    {
                        sb.Append("      localBBox=(").Append(V(lmn)).Append(")..(").Append(V(lmx)).Append(")\n");

                        Vec3 wmn = new Vec3(1e30f, 1e30f, 1e30f);
                        Vec3 wmx = new Vec3(-1e30f, -1e30f, -1e30f);
                        for (int k = 0; k < 8; k++)
                        {
                            var corner = new Vec3(
                                ((k & 1) == 0) ? lmn.x : lmx.x,
                                ((k & 2) == 0) ? lmn.y : lmx.y,
                                ((k & 4) == 0) ? lmn.z : lmx.z);
                            Vec3 w = global.TransformToParent(mf.TransformToParent(corner));
                            wmn = Vec3.Vec3Min(wmn, w);
                            wmx = Vec3.Vec3Max(wmx, w);
                        }
                        sb.Append("      worldBBox=(").Append(V(wmn)).Append(")..(").Append(V(wmx)).Append(")\n");
                    }
                }
                catch (Exception ex)
                {
                    sb.Append("   metamesh[").Append(i).Append("] threw: ").Append(ex.Message).Append('\n');
                }
            }

            try
            {
                int shown = 0;
                foreach (GameEntity c in e.GetChildren())
                {
                    if (c == null) continue;
                    if (shown >= MaxChildren) { sb.Append("   children: ... (truncated)\n"); break; }
                    string cn = "-";
                    try { cn = c.Name ?? "-"; } catch (Exception) { }
                    MatrixFrame cf;
                    try { cf = c.GetFrame(); } catch (Exception) { continue; }
                    sb.Append("   child[").Append(shown).Append("] ").Append(Pad(cn, 28))
                      .Append(" local pos=(").Append(V(cf.origin)).Append(")\n");
                    shown++;
                }
            }
            catch (Exception) { }
        }

        private static void AppendFrame(StringBuilder sb, string label, MatrixFrame f)
        {
            Vec3 euler = f.rotation.GetEulerAngles() * (180f / (float)Math.PI);
            sb.Append(label).Append(" pos=(").Append(V(f.origin)).Append(')')
              .Append(" rot_euler_deg=(").Append(F(euler.x)).Append(", ").Append(F(euler.y)).Append(", ").Append(F(euler.z)).Append(')')
              .Append(" s=(").Append(V(f.rotation.s)).Append(')')
              .Append(" f=(").Append(V(f.rotation.f)).Append(')')
              .Append(" u=(").Append(V(f.rotation.u)).Append(')')
              .Append('\n');
        }

        /// <summary>metamesh 内所有 Mesh 的局部包围盒并集（metamesh 空间）。</summary>
        private static bool TryLocalBounds(MetaMesh mm, out Vec3 mn, out Vec3 mx)
        {
            mn = new Vec3(1e30f, 1e30f, 1e30f);
            mx = new Vec3(-1e30f, -1e30f, -1e30f);
            bool any = false;
            int count = mm.MeshCount;
            for (int j = 0; j < count; j++)
            {
                Mesh m = mm.GetMeshAtIndex(j);
                if (m == null) continue;
                Vec3 a = m.GetBoundingBoxMin();
                Vec3 b = m.GetBoundingBoxMax();
                mn = Vec3.Vec3Min(mn, a);
                mx = Vec3.Vec3Max(mx, b);
                any = true;
            }
            return any;
        }

        private static string V(Vec3 v)
        {
            return F(v.x) + ", " + F(v.y) + ", " + F(v.z);
        }

        private static string F(float v)
        {
            return v.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string Pad(string s, int width)
        {
            if (s.Length >= width) return s.Substring(0, width);
            return s.PadRight(width);
        }
    }
}
