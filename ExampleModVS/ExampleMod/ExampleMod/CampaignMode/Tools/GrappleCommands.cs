using System;
using System.Collections.Generic;
using System.Globalization;
using LivingWorldNpcs.Animation;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

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
	/// —— 步骤 2 起（钩头 + 落点平台，2026-10-01）——
	///   custom.grapple throw              朝准星发一根钩头（完整链路：飞行 → 命中 → 落点平台解算）
	///   custom.grapple probe              只解算不发射：打印瞄准点 + 落点平台逐环明细（调参主力）
	///   custom.grapple retract            收钩（钩头拆掉、绳藏起来）
	///   custom.grapple range &lt;米&gt;         瞄准射线最大长度（默认 20；UE 参考工程是 12）
	///   custom.grapple hspeed &lt;m/s&gt;       钩头飞行速度（默认 35）
	///   custom.grapple hscale &lt;倍率&gt;      钩头占位网格缩放（默认 0.35）
	///   custom.grapple hmesh &lt;网格名&gt;     换钩头占位网格（第一候选；后两个兜底保留）
	///   custom.grapple nz &lt;值&gt;           落点平台：地面法线竖直度阈值（默认 0.7，越大越平）
	///   custom.grapple dz &lt;min&gt; &lt;max&gt;    落点平台：允许的高度窗口（米，默认 -1 ~ 4）
	///   custom.grapple headroom &lt;米&gt;      落点平台：头顶净空要求（默认 2.0）
	///   custom.grapple ring &lt;r1,r2,...&gt;   落点平台：环形采样半径表（米，默认 0.4,0.8,1.3,2.0）
	///   custom.grapple backoff &lt;米&gt;       没平台时沿射线退回的距离（默认 1.5）
	///   custom.grapple lreset             落点平台参数恢复默认
	///
	/// —— 步骤 3 起（武器接线）——
	///   custom.grapple equip              把钩索武器 + 绳弹**当场发到玩家手上**（Weapon0/1）——
	///                                     之后右键瞄准出准星、松手发射 = 钩头飞出（拦截补丁接的）
	///
	/// —— 步骤 4 起（拉自己）——
	///   custom.grapple pull self          把玩家拉向钩点（要求已勾住：先 throw 或开一枪）
	///   custom.grapple pulltime &lt;秒&gt;      拉升时长（0 = 自动：按距离缩放的地面 1.2 / 空中 0.95 秒）
	///   custom.grapple arc &lt;米&gt;          拉拽弧线高度（默认 1.5；0 = 直线）
	///   custom.grapple delay &lt;地面&gt; &lt;空中&gt; 蓄势时长（钩住后到开始拉，默认 0.65 / 0.35 秒）
	///   custom.grapple autopull &lt;0|1&gt;     武器开火命中后自动拉（默认开；关掉 = 只勾住，自己敲 pull self）
	///   custom.grapple cam &lt;米|t:模板名|off&gt;  拉拽机位：&lt;米&gt; = 引擎机位 + 臂长拉远（默认 8）·
	///                                     t:&lt;名&gt; = 用 Camera.csv 的模板机位（方向相对角色、接管瞬间硬切，调试角度用）· off = 不接管
	///   custom.grapple facehook &lt;0|1&gt;     拉拽期间是否把身体转向钩点（默认开；关掉 = 朝向完全交给引擎，隔离实验用）
	///   custom.grapple camret [&lt;比例&gt; &lt;秒&gt;]  相机（臂长/FOV）归还时机：比例 = 拉拽进度到多少就**提前**滑回引擎机位
	///                                     （默认 0.5，1 = 到位才开始 = 旧行为）；秒 = 滑行时长（默认 1.2）。
	///                                     无参 = 看当前值
	///   custom.grapple lookret [&lt;比例&gt; &lt;秒&gt;] **方向**归还时机（与 camret 分开计时）：比例 = 拉拽进度到多少
	///                                     就开始把方向转向"引擎重置后"的姿态（默认 **0 = 拉拽一开始**）；
	///                                     秒 = 转完时长（默认 0 = 跟拉拽时长一致）。无参 = 看当前值
	///
	/// ⚠️ **相机本身的诊断命令不在这一族**（2026-10-04 起）：`custom.cam log|stat|info|test|lift`
	///    见 `Camera/CameraCommands.cs`（相机自己的模块）。
	///
	/// 首参可弃（项目纪律）：认不出的第一个参数**当作没有**，回落到"状态"并注明。
	/// 返回文本一律英文（控制台纪律）。
	/// </summary>
	internal static class GrappleCommands
	{
		[CommandLineFunctionality.CommandLineArgumentFunction("grapple", "custom")]
		public static string Execute(List<string> args)
		{
			string sub = "dump";
			int at = 0;
			bool discarded = false;
			if (args != null && args.Count > 0 && args[0] != null)
			{
				string s = args[0].Trim().ToLowerInvariant();
				// 🔴 **这张白名单是子命令的准入表 —— 加新 case 必须同步加名字**（2026-10-05 事故：
				//    aimcam/aimanchor/aimlift/aimsens 只加了 case 忘了加这里 ⇒ 命令被当"不认识"、
				//    静默回落到 dump，用户敲 aimlift -100 "似乎根本没用"）。
				// ⚠️ 那四个名字 2026-10-05 阶段 3 起**只用来返回"已迁移到 custom.cam"的提示**（功能已删）。
				if (s == "anchor" || s == "clear" || s == "seg" || s == "len" || s == "radius"
					|| s == "overlap" || s == "damp" || s == "grav" || s == "mesh" || s == "ground"
					|| s == "auto" || s == "slack" || s == "smooth" || s == "mat" || s == "chain" || s == "part" || s == "freeze" || s == "release" || s == "dump" || s == "status"
					|| s == "throw" || s == "probe" || s == "retract" || s == "range" || s == "hspeed" || s == "hscale" || s == "hmesh"
					|| s == "nz" || s == "dz" || s == "headroom" || s == "ring" || s == "backoff" || s == "lreset" || s == "equip"
					|| s == "pull" || s == "pulltime" || s == "arc" || s == "delay" || s == "autopull" || s == "cam" || s == "facehook" || s == "camret" || s == "lookret"
					|| s == "anim" || s == "animlock" || s == "animblend" || s == "animthr"
					|| s == "aimcam" || s == "aimanchor" || s == "aimlift" || s == "aimsens")
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

			// 🔴 `equip` **两边都能敲**（2026-10-01 用户要求）：
			//    · 在场景里 = 直接把钩索+绳弹发到 Weapon0/1（当场可用）；
			//    · 在大地图 = 把两件物品**进主队辎重**（虚空来源，铁律 4 的 Grant），玩家自己去物品栏装备。
			//    其余子命令都依赖场景（绳/钩头/落点都在 Mission 里），照旧拦。
			if (sub == "equip")
			{
				return DoEquip();
			}

			// 🔴 瞄准相机的四个旋钮**已迁移到相机模块**（2026-10-05 阶段 3：命令统一到 `custom.cam`）——
			//    名字仍留在白名单里，是为了**返回迁移提示**（静默回落 = 用户以为"没用"，2026-10-05 那次事故）。
			if (sub == "aimcam" || sub == "aimanchor" || sub == "aimlift" || sub == "aimsens")
			{
				return "grapple: '" + sub + "' retired -> use 'custom.cam set grapple_shot <column> <value>'"
					 + " [arm | socketz | sens | anchorhead 0|1 | anchorheight <m>], "
					 + "or 'custom.cam play grapple_shot [seconds]' to watch it. (custom.cam list / show for details)";
			}

			if (Mission.Current == null || Agent.Main == null)
			{
				return "Error: not in mission.";
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

				// ─────────────────────────── 步骤 2：钩头 + 落点平台 ───────────────────────────

				case "throw":
					result = logic.Throw();
					break;

				case "probe":
					result = logic.Probe();
					break;

				case "retract":
					result = logic.Release();
					break;

				case "range":
				{
					float v = ParseF(args, at + 0, -1f);
					if (v <= 0f) { result = "Error: range needs meters > 0 (e.g. custom.grapple range 20)"; break; }
					GrappleLogic.AimRange = v;
					result = $"grapple: aim range = {v:F1}m";
					break;
				}

				case "hspeed":
				{
					float v = ParseF(args, at + 0, -1f);
					if (v <= 0f) { result = "Error: hspeed needs m/s > 0 (e.g. custom.grapple hspeed 35)"; break; }
					GrappleHook.Speed = v;
					result = $"grapple: hook speed = {v:F1} m/s";
					break;
				}

				case "hscale":
				{
					float v = ParseF(args, at + 0, -1f);
					if (v <= 0f) { result = "Error: hscale needs a multiplier > 0 (e.g. custom.grapple hscale 0.35)"; break; }
					GrappleHook.MeshScale = v;
					result = $"grapple: hook mesh scale = {v:F2}";
					break;
				}

				case "hmesh":
				{
					string name = ArgAt(args, at + 0);
					if (string.IsNullOrEmpty(name)) { result = "Error: hmesh needs a mesh name (e.g. custom.grapple hmesh push_fork)"; break; }
					GrappleHook.MeshCandidates = new[] { name, "push_fork", "bolt_bl_a" };
					result = $"grapple: hook mesh candidates = {name}, push_fork, bolt_bl_a (takes effect on next throw)";
					break;
				}

				case "nz":
				{
					float v = ParseF(args, at + 0, -1f);
					if (v < 0f || v > 1f) { result = "Error: nz needs 0~1 (e.g. custom.grapple nz 0.7)"; break; }
					GrappleLanding.MinNormalZ = v;
					result = $"grapple: landing min normal.z = {v:F2}";
					break;
				}

				case "dz":
				{
					float lo = ParseF(args, at + 0, float.NaN);
					float hi = ParseF(args, at + 1, float.NaN);
					if (float.IsNaN(lo) || float.IsNaN(hi) || lo >= hi)
					{
						result = "Error: dz needs two meters (min < max), e.g. custom.grapple dz -1 4";
						break;
					}
					GrappleLanding.MinDz = lo;
					GrappleLanding.MaxDz = hi;
					result = $"grapple: landing height window = [{lo:F1}, {hi:F1}]m";
					break;
				}

				case "headroom":
				{
					float v = ParseF(args, at + 0, -1f);
					if (v < 0f) { result = "Error: headroom needs meters >= 0 (e.g. custom.grapple headroom 2.0)"; break; }
					GrappleLanding.Headroom = v;
					result = $"grapple: landing headroom = {v:F1}m";
					break;
				}

				case "ring":
				{
					string csv = ArgAt(args, at + 0);
					float[] parsed = ParseFloatList(csv, 0f);
					if (parsed == null || parsed.Length == 0)
					{
						result = "Error: ring needs a comma list of meters > 0 (e.g. custom.grapple ring 0.4,0.8,1.3,2.0)";
						break;
					}
					GrappleLanding.Radii = parsed;
					result = "grapple: landing rings = " + string.Join(",", Array.ConvertAll(parsed, x => x.ToString("F2", CultureInfo.InvariantCulture)));
					break;
				}

				case "backoff":
				{
					float v = ParseF(args, at + 0, -1f);
					if (v < 0f) { result = "Error: backoff needs meters >= 0 (e.g. custom.grapple backoff 1.5)"; break; }
					GrappleLanding.FallbackBackoff = v;
					result = $"grapple: no-platform backoff = {v:F1}m";
					break;
				}

				case "lreset":
					GrappleLanding.ResetDefaults();
					result = "grapple: landing params reset to defaults";
					break;

				case "equip":
					result = DoEquip();
					break;

				// ─────────────────────────── 步骤 4：拉自己 ───────────────────────────

				case "pull":
				{
					string what = (ArgAt(args, at + 0) ?? "self").ToLowerInvariant();
					if (what == "self")
					{
						result = logic.PullSelf();
						break;
					}
					if (what == "target")
					{
						result = "Error: pull target not implemented yet (step 5).";
						break;
					}
					result = "Error: pull needs 'self' (target = step 5)";
					break;
				}

				case "pulltime":
				{
					float v = ParseF(args, at + 0, -1f);
					if (v < 0f) { result = "Error: pulltime needs seconds >= 0 (0 = auto by distance)"; break; }
					GrapplePull.DurationOverride = v;
					result = v <= 0f ? "grapple: pull duration = auto (by distance)" : $"grapple: pull duration = {v:F2}s (fixed)";
					break;
				}

				case "arc":
				{
					float v = ParseF(args, at + 0, -1f);
					if (v < 0f) { result = "Error: arc needs meters >= 0 (0 = straight line)"; break; }
					GrapplePull.ArcHeight = v;
					result = $"grapple: pull arc height = {v:F1}m";
					break;
				}

				// ── 姿态动画（2026-10-04；定义 = ModuleData/statemachines/grapple.xml）──
				case "anim":
				{
					float v = ParseF(args, at + 0, -1f);
					if (v < 0f)
					{
						result = GrappleLogic.AnimEnabled
							? "grapple anim: ON | " + AnimStateLine(logic)
							: "grapple anim: OFF (old bow-anim behavior)";
						break;
					}
					GrappleLogic.AnimEnabled = v > 0.5f;
					result = GrappleLogic.AnimEnabled ? "grapple anim: ON" : "grapple anim: OFF (old bow-anim behavior)";
					break;
				}

				case "animlock":
				{
					// 锁死播某一段静观：`animlock 投掷` / `animlock clear`（无参 = 看当前）
					string st = args != null && args.Count > at && args[at] != null ? args[at].Trim() : "";
					result = st.Length == 0 ? AnimStateLine(logic) : "grapple: " + logic.SetAnimLock(st);
					break;
				}

				case "animblend":
				{
					float v = ParseF(args, at + 0, -1f);
					if (v <= 0f) { result = $"grapple: anim blend = {GrappleAnimMachine.AnimBlendIn:F2}s"; break; }
					GrappleAnimMachine.AnimBlendIn = v;
					result = $"grapple: anim blend = {v:F2}s";
					break;
				}

				case "animthr":
				{
					// 两个"剩多少就切"阈值：切落地/自由落体 · 投掷失败的收手点
					float a = ParseF(args, at + 0, -1f);
					float b = ParseF(args, at + 1, -1f);
					if (a < 0f && b < 0f)
					{
						result = $"grapple: anim thr | switch(land/fall) remain={GrappleAnimConditions.SwitchRemainFrac * 100f:F0}%"
							+ $" throwcancel remain={GrappleAnimConditions.CancelRemainFrac * 100f:F0}%";
						break;
					}
					if (a >= 0f) GrappleAnimConditions.SwitchRemainFrac = a > 1f ? a / 100f : a;
					if (b >= 0f) GrappleAnimConditions.CancelRemainFrac = b > 1f ? b / 100f : b;
					result = $"grapple: anim thr | switch remain={GrappleAnimConditions.SwitchRemainFrac * 100f:F0}%"
						+ $" throwcancel remain={GrappleAnimConditions.CancelRemainFrac * 100f:F0}%";
					break;
				}

				case "delay":
				{
					float ground = ParseF(args, at + 0, -1f);
					float air = ParseF(args, at + 1, -1f);
					if (ground < 0f && air < 0f)
					{
						result = "Error: delay needs seconds (ground [air]), e.g. custom.grapple delay 0.65 0.35";
						break;
					}
					if (ground >= 0f) GrappleLogic.PullDelayGround = ground;
					if (air >= 0f) GrappleLogic.PullDelayAir = air;
					result = $"grapple: pull delay = ground {GrappleLogic.PullDelayGround:F2}s / air {GrappleLogic.PullDelayAir:F2}s";
					break;
				}

				case "autopull":
				{
					GrappleLogic.AutoPull = ParseF(args, at + 0, 1f) > 0.5f;
					result = GrappleLogic.AutoPull
						? "grapple: autopull ON (weapon fire -> attach -> pull automatically)"
						: "grapple: autopull OFF (weapon fire stops at attached; use 'pull self')";
					break;
				}

				case "facehook":
				{
					// 隔离实验用：拉拽期间"把身体转向钩点"的写入开关（关掉 = 朝向完全交给引擎）
					GrapplePull.FaceHook = ParseF(args, at + 0, 1f) > 0.5f;
					result = GrapplePull.FaceHook
						? "grapple: facehook ON (body is turned toward the hook during the pull)"
						: "grapple: facehook OFF (we write nothing; engine decides the facing)";
					break;
				}

				case "lookret":
				{
					// **方向**归还时机（与 camret 的臂长/FOV 归还分开计时；2026-10-04 用户要求"从开始拉拽就渐变"）：
					//   比例 = 拉拽进度 u 到多少就开始转（默认 0 = 一开始；0.5 = 后半程）
					//   秒   = 转完时长（默认 0 = 跟拉拽时长一致）
					// 无参 = 看当前；只给比例 = 只改比例。
					float start = ParseF(args, at + 0, -1f);
					float secs = ParseF(args, at + 1, -1f);
					string show = $"grapple: look return = start u>={GrapplePull.LookReturnStart:F2} seconds "
						+ (GrapplePull.LookReturnSeconds <= 0.05f ? "auto(=pull duration)" : GrapplePull.LookReturnSeconds.ToString("F2"));
					if (start < 0f && secs < 0f)
					{
						result = show;
						break;
					}
					if (start >= 0f)
					{
						if (start > 1f) { result = "Error: lookret start must be 0..1 (0 = from the very beginning of the pull)"; break; }
						GrapplePull.LookReturnStart = start;
					}
					if (secs >= 0f)
					{
						GrapplePull.LookReturnSeconds = secs;      // 0 = auto（跟拉拽时长一致）
					}
					show = $"grapple: look return = start u>={GrapplePull.LookReturnStart:F2} seconds "
						+ (GrapplePull.LookReturnSeconds <= 0.05f ? "auto(=pull duration)" : GrapplePull.LookReturnSeconds.ToString("F2"));
					result = show;
					break;
				}

				case "camret":
				{
					// 相机（臂长/FOV）归还时机（2026-10-03 用户要求"还没落地就开始渐变"）：
					//   比例 = 拉拽进度 u 到多少就起飞（默认 0.5；1 = 到位才开始 = 旧行为）
					//   秒   = 滑行时长（默认 1.2；旧值 0.35 太快，看着像硬切）
					// 无参 = 看当前；只给比例 = 只改比例。
					float start = ParseF(args, at + 0, -1f);
					float glide = ParseF(args, at + 1, -1f);
					if (start < 0f && glide < 0f)
					{
						result = $"grapple: camera return = start u>={GrapplePull.CameraReturnStart:F2} glide {GrapplePull.CameraReturnGlideSeconds:F2}s";
						break;
					}
					if (start >= 0f)
					{
						if (start > 1f) { result = "Error: camret start must be 0..1 (1 = return only after landing)"; break; }
						GrapplePull.CameraReturnStart = start;
					}
					if (glide >= 0f)
					{
						if (glide < 0.05f) { result = "Error: camret glide must be >= 0.05 seconds"; break; }
						GrapplePull.CameraReturnGlideSeconds = glide;
					}
					result = $"grapple: camera return = start u>={GrapplePull.CameraReturnStart:F2} glide {GrapplePull.CameraReturnGlideSeconds:F2}s";
					break;
				}

				case "cam":
				{
					// 🔴 **钩索相机总开关**（2026-10-05 阶段 3 收敛成这一条）：无参 = 看当前；
					//    `cam off|on` = 瞄准 + 拉拽**两个相机一起**开关；`cam <米>` = 拉拽臂长（行里的值）。
					//    （逐参数调节已迁移到 `custom.cam set grapple_pull arm <米>`。）
					string name = ArgAt(args, at + 0);
					if (string.IsNullOrEmpty(name))
					{
						result = $"grapple: gcams={(GrappleAimCamera.Enabled ? "ON" : "OFF")} pullArm={GrapplePull.CameraArmLength:F1}m"
							   + (GrappleAimCamera.Enabled && GrapplePull.CameraArmLength <= 0f ? " (pull camera OFF)" : "")
							   + " | " + GrappleAimCamera.StatusLine();
						break;
					}
					if (name.Equals("off", StringComparison.OrdinalIgnoreCase) || name == "0")
					{
						GrappleAimCamera.Enabled = false;
						result = "grapple: grapple cameras OFF (aim + pull use the engine camera; for comparison)";
						break;
					}
					if (name.Equals("on", StringComparison.OrdinalIgnoreCase) || name == "1")
					{
						GrappleAimCamera.Enabled = true;
						result = "grapple: grapple cameras ON | " + GrappleAimCamera.StatusLine();
						break;
					}
					if (name.StartsWith("t:", StringComparison.OrdinalIgnoreCase))
					{
						result = "grapple: 'cam t:<template>' retired -> use 'custom.cam play <case> [seconds]'"
							   + " (any Camera.csv row can be played)";
						break;
					}
					float arm = ParseF(args, at + 0, -1f);
					if (arm < 0f)
					{
						result = "Error: cam needs 'off' | 'on' | <meters> (e.g. custom.grapple cam 8)";
						break;
					}
					GrappleAimCamera.Enabled = true;
					GrapplePull.CameraArmLength = arm;
					result = $"grapple: pull camera = engine-look + arm {arm:F1}m";
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

		/// <summary>姿态动画的一行状态（命令回执用，纯英文 —— 控制台纪律）。</summary>
		private static string AnimStateLine(GrappleLogic logic)
		{
			AgentAnimStateMachine m = logic?.Anim;
			if (m == null) return "anim: (no machine)";
			string cur = m.Current ?? "(never took over)";
			return string.Format("anim: {0} | current={1} action={2} remain={3:F2}",
				GrappleLogic.AnimEnabled ? "ON" : "OFF", cur, m.CurrentAction ?? "-", m.CurrentRemainFrac);
		}

		/// <summary>
		/// 发钩索装备 —— **两边都能敲**：
		///   · **在场景里** = 直接装到手上（Weapon0 = 钩索 · Weapon1 = 绳弹），当场可试；
		///   · **在大地图** = 两件物品**进主队辎重**（虚空来源，铁律 4 的 Grant；走 AgentControlHelper），
		///     玩家自己去物品栏装备 —— 这是"正常流程"验收要走的路（进战斗前装配）。
		/// 🔴 物品在**内容包**里（`Modules/Taikou/ModuleData/taikou_items/grapple.xml`）；
		///    没装内容包 / 名字改了 = 这里给一句明确的英文错误，不崩。
		/// 🔴 两轮查找（铁律 5）：第一轮按 StringId 精确找；第二轮在内存里按名字**包含**扫一遍兜底。
		/// </summary>
		private static string DoEquip()
		{
			ItemObject hook = ResolveContentItem("taikou_grapple_hook", "grapple_hook");
			ItemObject dart = ResolveContentItem("taikou_grapple_dart", "grapple_dart");
			if (hook == null || dart == null)
			{
				return "Error: grapple items not found (content pack Taikou not loaded? expected taikou_grapple_hook / taikou_grapple_dart)";
			}

			// ① 场景里：直接装到玩家手上（当场可用）
			Agent main = Agent.Main;
			if (Mission.Current != null && main != null)
			{
				try
				{
					MissionWeapon hookWeapon = new MissionWeapon(hook, null, main.Origin?.Banner);
					main.EquipWeaponWithNewEntity(EquipmentIndex.Weapon0, ref hookWeapon);
					MissionWeapon dartWeapon = new MissionWeapon(dart, null, main.Origin?.Banner);
					main.EquipWeaponWithNewEntity(EquipmentIndex.Weapon1, ref dartWeapon);
					// 🔴 把绳弹钉死在 **1 发**（2026-10-03）：物品的 `stack_amount` 是 20 —— 为什么不是 1，
					//    见 `taikou_items/grapple.xml` 顶上的注释（HUD 的子弹数只统计"最大堆叠 > 1"的弹药；
					//    写 1 = 能射但永远显示 0）。装填完立刻设成 1 = "上限 20、实有 1"。
					main.SetWeaponAmountInSlot(EquipmentIndex.Weapon1, 1, false);
					main.UpdateAgentStats();
					return "OK (mission): equipped GrappleHook (slot 0) + GrappleDart x1 (slot 1) - draw with RMB, release to fire.";
				}
				catch (Exception ex)
				{
					return "Error: equip failed (" + ex.GetType().Name + ": " + ex.Message + ")";
				}
			}

			// ② 大地图：进主队辎重（玩家自己去物品栏装备）
			try
			{
				Hero hero = Hero.MainHero;
				if (hero == null)
				{
					return "Error: no main hero.";
				}
				int givenHook = AgentControlHelper.TransferItems(null, hero, hook, 1);
				int givenDart = AgentControlHelper.TransferItems(null, hero, dart, 1);
				if (givenHook <= 0 && givenDart <= 0)
				{
					return "Error: could not add grapple items to the party inventory.";
				}
				return $"OK (campaign): added to party inventory - GrappleHook x{givenHook}, GrappleDart x{givenDart}. Equip them in the inventory screen (bow slot + arrow slot), then enter a battle and fire. Run again for spares.";
			}
			catch (Exception ex)
			{
				return "Error: campaign give failed (" + ex.GetType().Name + ": " + ex.Message + ")";
			}
		}

		/// <summary>两轮查物品：① StringId 精确 ② 内存里按 StringId 包含片段扫一遍（铁律 5）。</summary>
		private static ItemObject ResolveContentItem(string exactId, string nameFragment)
		{
			try
			{
				ItemObject exact = MBObjectManager.Instance.GetObject<ItemObject>(exactId);
				if (exact != null)
				{
					return exact;
				}
				foreach (ItemObject item in MBObjectManager.Instance.GetObjectTypeList<ItemObject>())
				{
					if (item != null && item.StringId != null
						&& item.StringId.IndexOf(nameFragment, StringComparison.OrdinalIgnoreCase) >= 0)
					{
						return item;
					}
				}
			}
			catch (Exception)
			{
				// 查不到就返回 null，调用方给英文错误
			}
			return null;
		}

		/// <summary>把 "0.4,0.8,1.3" 这样的逗号串解成 float 数组（全解析不出 = null；非法项丢弃）。</summary>
		private static float[] ParseFloatList(string csv, float minExclusive)
		{
			if (string.IsNullOrEmpty(csv)) return null;
			string[] parts = csv.Split(',');
			List<float> values = new List<float>();
			foreach (string part in parts)
			{
				if (float.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
					&& v > minExclusive)
				{
					values.Add(v);
				}
			}
			return values.Count > 0 ? values.ToArray() : null;
		}

		private static string ArgAt(List<string> args, int i)
		{
			if (args == null || i < 0 || i >= args.Count || args[i] == null) return null;
			return args[i].Trim();
		}
	}
}
