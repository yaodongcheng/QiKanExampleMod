using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.CampaignMode.Tools
{
	/// <summary>
	/// 钩索的验收命令（归口：C# 命令实现进 CampaignMode\Tools\）。
	///
	///   custom.grapple                    状态（等同 dump）
	///   custom.grapple anchor &lt;距离&gt; &lt;绳长&gt; &lt;段数&gt;
	///                                    ① 准星外推距离（米，默认 12）—— 射线最多打这么远，打空就钉在那距离的空中
	///                                    ② 绳**自身总长**（米，默认 自动 = 跨度 +30%）
	///                                    ③ 段数（默认 自动，按绳长算每段 ≈18cm）
	///                                    每段多长 = 绳长 ÷ 段数，自动算，不用给。
	///                                    例：`anchor 8 10 40` = 朝准星 8 米找锚点 · 绳 10 米 · 40 段
	///   custom.grapple clear              收绳（藏起来，实体留着复用）
	///   custom.grapple seg &lt;n|auto&gt;       模拟分辨率：把绳切成几段点（默认 48；**不随绳长变**）
	///   custom.grapple auto &lt;0|1&gt;         **橡皮筋**：1 = 绳长跟着跨度走（默认）· 0 = 固定长度
	///   custom.grapple slack &lt;比例&gt;       橡皮筋留的余量（默认 1.2 = 永远比跨度长 20%）
	///   custom.grapple len &lt;米&gt;           固定绳长（**会关掉橡皮筋**；anchor 时不给绳长也一样）
	///   custom.grapple radius &lt;倍率&gt;      粗细倍率（默认 0.6 —— 原网格 5cm）
	///   custom.grapple overlap &lt;倍率&gt;     段长重叠（默认 1.35；1 = 首尾相接）
	///   custom.grapple damp &lt;0.9~0.99&gt;    速度阻尼（默认 0.94；越小越快停，甩尾越短）
	///   custom.grapple grav &lt;m/s²&gt;        重力（默认 9.8）
	///   custom.grapple mesh &lt;名&gt;         回退网格用哪份原版元网格（默认 rope_stealth_mission_a）
	///   custom.grapple mat &lt;网格名&gt;     材质从哪份原版网格借（空 = 借 mesh 那件）—— 让铁环像铁
	///   custom.grapple chain &lt;0|1&gt;        铁链：1 = 竖长铁环（相邻转 90°）· 0 = 关（回分段管）
	///   custom.grapple smooth &lt;米&gt;       铁链用的光滑曲线加密步长（默认 0.03；越小越顺）
	///   custom.grapple ground &lt;0|1&gt;      贴地开关（默认 1）
	///   custom.grapple freeze &lt;0|1&gt;      定格：冻住点链不再模拟（静止看接缝/形状用）
	///   custom.grapple release            远端松手（绳自己掉下去 —— 验重力；再 anchor 一次钉回去）
	///   custom.grapple dump               详细状态（段数 / 长度 / 手与锚点坐标 / 绷直还是松垂）
	///
	/// 首参可弃（项目纪律）：认不出的第一个参数**当作没有**，回落到"状态"并注明。
	/// 返回文本一律英文（控制台纪律）。
	/// </summary>
	internal static class GrappleCommands
	{
		[CommandLineFunctionality.CommandLineArgumentFunction("grapple", "custom")]
		public static string Execute(List<string> args)
		{
			if (Mission.Current == null || Agent.Main == null)
			{
				return "Error: not in mission.";
			}

			string sub = "dump";
			int at = 0;
			bool discarded = false;
			if (args != null && args.Count > 0 && args[0] != null)
			{
				string s = args[0].Trim().ToLowerInvariant();
				if (s == "anchor" || s == "clear" || s == "seg" || s == "len" || s == "radius"
					|| s == "overlap" || s == "damp" || s == "grav" || s == "mesh" || s == "ground"
					|| s == "auto" || s == "slack" || s == "smooth" || s == "mat" || s == "chain" || s == "part" || s == "freeze" || s == "release" || s == "dump" || s == "status")
				{
					sub = s == "status" ? "dump" : s;
					at = 1;
				}
				else
				{
					// 可弃占位（`custom.grapple 1` 这类）—— 回落到状态
					discarded = true;
				}
			}

			GrappleLogic logic = GrappleLogic.Ensure();
			if (logic == null)
			{
				return "Error: no mission.";
			}
			GrappleRope rope = logic.Rope;

			string result;
			switch (sub)
			{
				case "anchor":
					result = DoAnchor(logic,
						ParseF(args, at + 0, 12f),      // ① 准星外推距离（米）
						ParseF(args, at + 1, -1f),      // ② 绳总长（米，<=0 = 自动）
						(int)ParseF(args, at + 2, -1f)); // ③ 段数（<=0 = 自动）
					break;

				case "clear":
					result = logic.Clear();
					break;

				case "dump":
					result = logic.Status();
					break;

				case "seg":
				{
					string a0 = ArgAt(args, at + 0);
					if (a0 != null && a0.Equals("auto", StringComparison.OrdinalIgnoreCase))
					{
						rope.Segments = 48;                 // 默认分辨率
						result = logic.Refresh();
						break;
					}
					float n = ParseF(args, at + 0, -1f);
					if (n < 0f)
					{
						result = "Error: seg needs a number or 'auto' (e.g. custom.grapple seg 64)";
						break;
					}
					rope.Segments = (int)n;
					result = logic.Refresh();
					break;
				}

				case "auto":
				{
					// 橡皮筋开关：1 = 绳长跟着跨度走（默认）· 0 = 用固定长度
					rope.AutoLength = ParseF(args, at + 0, 1f) > 0.5f;
					result = logic.Refresh();
					break;
				}

				case "slack":
				{
					// 自动长度留的余量比例（1.2 = 永远比跨度长 20%）
					float v = ParseF(args, at + 0, -1f);
					if (v < 1f) { result = "Error: slack needs a ratio >= 1 (e.g. custom.grapple slack 1.2)"; break; }
					rope.SlackRatio = v;
					result = logic.Refresh();
					break;
				}

				case "len":
				{
					float v = ParseF(args, at + 0, -1f);
					if (v <= 0f) { result = "Error: len needs meters (e.g. custom.grapple len 4.5)"; break; }
					rope.Length = v;
					rope.AutoLength = false;   // 显式给长度 = 关掉橡皮筋
					result = logic.Refresh();
					break;
				}

				case "radius":
				{
					float v = ParseF(args, at + 0, -1f);
					if (v <= 0f) { result = "Error: radius needs a multiplier (e.g. custom.grapple radius 0.8)"; break; }
					rope.RadiusScale = v;
					GrappleRope.RememberRadius(v);
					result = logic.Refresh();
					break;
				}

				case "overlap":
				{
					float v = ParseF(args, at + 0, -1f);
					if (v < 1f) { result = "Error: overlap needs a multiplier >= 1 (e.g. custom.grapple overlap 1.4)"; break; }
					rope.Overlap = v;
					result = logic.Refresh();
					break;
				}

				case "damp":
				{
					float v = ParseF(args, at + 0, -1f);
					if (v <= 0f || v > 0.999f)
					{
						result = "Error: damp needs 0.5~0.999 (e.g. custom.grapple damp 0.90)";
						break;
					}
					rope.Damping = v;
					result = logic.Refresh();
					break;
				}

				case "grav":
				{
					float v = ParseF(args, at + 0, -1f);
					if (v < 0f) { result = "Error: grav needs m/s^2 >= 0 (e.g. custom.grapple grav 9.8)"; break; }
					rope.Gravity = v;
					result = logic.Refresh();
					break;
				}

				case "mesh":
				{
					string name = ArgAt(args, at + 0);
					if (string.IsNullOrEmpty(name))
					{
						result = "Error: mesh needs a name (e.g. custom.grapple mesh rope_stealth_mission_b)";
						break;
					}
					rope.MeshName = name;
					result = logic.Refresh();
					break;
				}

				case "smooth":
				{
					// 铺链前的加密步长（米）：越小编的曲线越顺
					float v = ParseF(args, at + 0, -1f);
					if (v <= 0f) { result = "Error: smooth needs meters (e.g. custom.grapple smooth 0.02)"; break; }
					rope.ChainFineStep = v;
					GrappleRope.RememberFineStep(v);
					result = logic.Refresh();
					break;
				}

				case "mat":
				{
					// 材质从哪份原版网格借（空 = 借 mesh 那件）—— 让铁环像铁，不用开 ModKit
					rope.MaterialSourceMesh = ArgAt(args, at + 0) ?? "";
					GrappleRope.RememberMatSource(rope.MaterialSourceMesh);
					rope.InvalidateBuiltMeshes();
					logic.Refresh();
					result = logic.Status();
					break;
				}

				case "chain":
				{
					// 铁链：0 = 关（回到分段管）· 1 = 开（竖长铁环，相邻转 90°）
					rope.ChainMode = ParseF(args, at + 0, 1f) > 0.5f;
					GrappleRope.RememberChain(rope.ChainMode);   // 记住：下个场景自动生效
					logic.Refresh();
					result = logic.Status();
					break;
				}

				case "part":
				{
					float v = ParseF(args, at + 0, -1f);
					if (v < 0f) { result = "Error: part needs an index (0=hook, 1=strand of rope_stealth_mission_a)"; break; }
					rope.MeshPart = (int)v;
					logic.Refresh();
					result = logic.Status();
					break;
				}

				case "ground":
				{
					rope.GroundClamp = ParseF(args, at + 0, 1f) > 0.5f;
					result = logic.Refresh();
					break;
				}

				case "freeze":
				{
					rope.Frozen = ParseF(args, at + 0, 1f) > 0.5f;
					result = (rope.Frozen ? "grapple: FROZEN (rope holds its shape; freeze 0 to resume)" : "grapple: resumed");
					result += " | " + rope.Status();
					break;
				}

				case "release":
				{
					// 远端松手 —— 绳该自己掉下去（验重力的一锤定音）。再 `anchor` 一次即可钉回去。
					rope.FreeEnd = true;
					result = "grapple: far end released -> rope should fall. " + rope.Status();
					break;
				}

				default:
					result = logic.Status();
					break;
			}

			if (discarded)
			{
				result += " | [note: first arg not a subcommand -> showed status]";
			}
			return result;
		}

		/// <summary>
		/// 朝视线打一条射线定锚点，然后钉绳。
		/// 三个参数一条说清（2026-10-01 用户要求）：
		///   ① <paramref name="dist"/> 准星外推距离（米）—— 射线最多打这么远；打空就钉在这个距离的空中
		///   ② <paramref name="len"/> 绳**自身总长**（米，≤0 = 自动 = 跨度 +30%，保证看得见垂度）
		///   ③ <paramref name="segs"/> 段数（≤0 = 自动，按绳长算，每段 ≈18cm）
		/// 每段多长 = 绳长 ÷ 段数，自动算，不用给。
		/// </summary>
		private static string DoAnchor(GrappleLogic logic, float dist, float len, int segs)
		{
			Vec3 from = Agent.Main.GetEyeGlobalPosition();
			Vec3 look = Agent.Main.LookDirection;
			if (CameraLook.TryGet(out Vec3 camLook) && camLook.LengthSquared > 1e-6f)
			{
				look = camLook;   // 铁律 35：接管相机时视线只认接管方
			}
			Vec3 to = from + look * MathF.Max(0.5f, dist);

			Vec3 point = to;
			string hitNote = "no hit -> anchored in the air at that distance";
			try
			{
				Scene scene = Mission.Current.Scene;
				if (scene != null && scene.RayCastForClosestEntityOrTerrain(from, to, out float hitDist,
						out Vec3 hitPoint, 0.01f, BodyFlags.CommonCollisionExcludeFlagsForMissile))
				{
					point = hitPoint;
					hitNote = $"hit at {hitDist:F2}m";
				}
			}
			catch (Exception ex)
			{
				hitNote = "raycast failed: " + ex.GetType().Name;
			}
			return logic.Anchor(point, len, segs) + " | " + hitNote;
		}

		/// <summary>取第 i 个参数当 float；缺失/解析不出就用默认（首参可弃的同一套精神）。</summary>
		private static float ParseF(List<string> args, int i, float fallback)
		{
			if (args == null || i < 0 || i >= args.Count || args[i] == null) return fallback;
			return float.TryParse(args[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
				? v : fallback;
		}

		private static string ArgAt(List<string> args, int i)
		{
			if (args == null || i < 0 || i >= args.Count || args[i] == null) return null;
			return args[i].Trim();
		}
	}
}
