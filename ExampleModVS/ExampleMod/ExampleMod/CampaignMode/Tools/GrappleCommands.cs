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
	///   custom.grapple hscale &lt;倍率&gt;      钩头网格缩放（默认 0.30）
	///   custom.grapple hmesh &lt;网格名&gt;     换钩头网格（第一候选；后两个兜底保留）
	///   custom.grapple spin &lt;rpm&gt; [半径]   **手里的钩**：绕右手转的转速 / 半径（= 手里那截绳的长度，默认 90 rpm / 0.20 m）
	///   custom.grapple spin on|off        手里的钩总开关（off = 手上不显示钩与绳）；无参 = 看当前值
	///   custom.grapple spinrope &lt;0|1&gt;     **待机那条绳**的开关（**默认 0 = 关**；开着才能看出"绳从手连到钩"）
	///   custom.grapple hand &lt;0..4&gt;       🔴 the "why is the hand hook invisible" LADDER（**每档只比上一档多一个变量** ⇒「第一档开始看不见」= 元凶就是那一档新加的变量；
		///                                     全程同一枚实体、换档不重建）：
		///                                     1 = 眼前 2 m / 高 1.2（= `custom.spawn_mesh` 逐字同款）· Identity · **只摆一次**
		///                                     2 = 同位置 · Identity · **每帧摆**（跟着人走）→ 比 1 多的是"每帧"
		///                                     3 = **右手骨上方 0.18 m**（= 阴魔斩蓄力球同一读点）· Identity · 每帧 → 比 2 多的是"手骨位置"
		///                                     4 = 手骨 + 离心（默认 0.25 m）· **径向朝向** · 每帧 = **现行为** → 比 3 多的是"径向基底"
		///                                     0 = 关（回正常那条：受 spin 开关与"握着钩索"判据管）· 无参 = 看当前档与含义
		///   custom.grapple armaxis &lt;0..4&gt; [轴外移米]  **手里那枚的绕转轴**（圆所在的平面 ⊥ 这个轴；即时生效、不用重编）：
		///                                     4 = 小臂轴（肘→手）**默认**（2026-10-07 晚用户裁定）·
		///                                     0 = 世界竖直 · 1 = 体侧前后甩 · 2 = 左右横扫 · 3 = 上臂弦（折臂时不准）
		///                                     第 2 参 = 圆心沿轴外移（**默认 0**；掌心的那 8 cm 由 `palm` 统一负责）
		///   custom.grapple palm &lt;米&gt;          **手部挂点沿小臂外移**（默认 0.08 = 掌心）—— **绳的近端 / 钩的圆心 / 开火起点三处共用**：
		///                                     0 = 正好压在腕关节 · 负值 = 往腕方向回缩
		///   custom.grapple hookface &lt;0|1|2|3&gt;  **钩朝哪**：哪个物体局部轴对准"离心向外"
		///                                     （**默认 3 + hookroll 180** = 用户实机定的"对的形态"；0=+Z · 1=−Z · 2=+Y · 3=−Y；
		///                                      🔴 引擎口径实测 = **钩体沿局部 +Y**（tpaccli 读装机包为准，FBX/Blender 侧会做轴换算））
		///   custom.grapple hookroll &lt;度&gt;      绕"离心向外"轴滚转（三爪朝哪边弯的观感项；即时生效）
		///   custom.grapple armout &lt;米&gt;         圆心**横向远离身体**（默认 0.10）—— 治"转一圈有一小段看不见"（圆内侧扫进躯干）
		///   custom.grapple spinlog &lt;0|1&gt;       限频帧日志（默认关）：每 ~0.4 s 打 角度/钩位置/**actual**，
		///                                     用来判"某个角度看不见"是**被遮挡**（actual 正常画圆）还是**摆位失效**（actual 跳走）
		///   custom.grapple ringface &lt;0|1|2&gt;   **左手环的自身轴指向**（环由我们自绘、挂在根实体下）：
		///                                     0 = 沿小臂（默认，环面 ⊥ 小臂 = 像松垮的镯子）· 1 = 世界竖直（环面水平）· 2 = 角色右方向
		///   custom.grapple ringscale &lt;倍率&gt;    左手环缩放（默认 1 = 资产 8 cm 外径）
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
	///   custom.grapple lookret [&lt;比例&gt; &lt;秒&gt;] 🪦 **已退役（2026-10-10）** —— 拉拽期间镜头现在完全跟玩家
	///                                     （要方向自己转鼠标；撒手那一刻我们把你转到的朝向写回引擎）。敲它=看迁移说明
	///   custom.grapple carrier [spawn|clear]  载具体检：无参 = 报告预制体/物理足迹（米）/板上人数；
	///                                     spawn = 在脚下现生一块（看"看不见" + 量足迹）；clear = 收掉
	///
	/// ⚠️ **相机本身的诊断命令不在这一族**（2026-10-04 起）：`custom.cam log|stat|info|test|lift`
	///    见 `Camera/CameraCommands.cs`（相机自己的模块）。
	///
	/// 首参可弃（项目纪律）：认不出的第一个参数**当作没有**，回落到"状态"并注明。
	/// 返回文本一律英文（控制台纪律）。
	/// </summary>
	internal static class GrappleCommands
	{
		/// <summary>`custom.grapple carrier spawn` 生成的那块"体检板"（钉在原地，直到 `carrier clear`）。</summary>
		private static Flight.CarrierBoard ProbeCarrier;

		/// <summary>体检板是在**哪个场景**生的 —— 换场景后实体已被引擎销毁，绝不能再碰它的指针。</summary>
		private static UIntPtr ProbeScenePtr;

		/// <summary>
		/// 取体检板（**顺带做场景守卫**）：当前场景不是生它那个 ⇒ 只丢引用、**不碰实体**
		/// （实体随旧场景一起没了；对已销毁实体调任何东西都可能直接崩）。
		/// </summary>
		private static Flight.CarrierBoard LiveProbeCarrier()
		{
			if (ProbeCarrier == null)
			{
				return null;
			}
			Scene scene = Mission.Current?.Scene;
			if (scene == null || scene.Pointer != ProbeScenePtr)
			{
				ProbeCarrier = null;
				ProbeScenePtr = UIntPtr.Zero;
				return null;
			}
			return ProbeCarrier;
		}

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
					|| s == "throw" || s == "probe" || s == "retract" || s == "range" || s == "hspeed" || s == "hscale" || s == "hmesh" || s == "spin" || s == "spinrope" || s == "hand" || s == "armaxis" || s == "hookface" || s == "hookroll" || s == "armout" || s == "spinlog"
					|| s == "ringface" || s == "ringscale" || s == "ropelog" || s == "palm" || s == "retspeed"
					|| s == "nz" || s == "dz" || s == "headroom" || s == "ring" || s == "backoff" || s == "lreset" || s == "equip" || s == "iconhook" || s == "iconscale" || s == "hookbelt" || s == "handring" || s == "tracelog"
					|| s == "pull" || s == "pulltime" || s == "arc" || s == "delay" || s == "autopull" || s == "cam" || s == "facehook" || s == "camret" || s == "lookret" || s == "carrier"
					|| s == "anim" || s == "animlock" || s == "animblend" || s == "animthr"
					|| s == "bind"
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

			// 🔴 `iconhook` = **钩索图标补丁**的开关（2026-10-08）：钩那件物品在**背包/装备界面**里的 2D 图标，
			//    画成真钩还是画成它自己的网格（隐形代理）。**任何界面都能敲**（背包/大地图/场景都行）——
			//    图标是懒加载 + 缓存的，改完**重开背包界面**才会重画（缓存没释放就重启一次）。
			if (sub == "iconhook")
			{
				if (at < args.Count)
				{
					string v = args[at].Trim().ToLowerInvariant();
					if (v == "0" || v == "off" || v == "false")
					{
						GrappleIconPatch.Enabled = false;
					}
					else if (v == "1" || v == "on" || v == "true")
					{
						GrappleIconPatch.Enabled = true;
					}
					else
					{
						return "Error: iconhook takes 0|1 (got '" + v + "')";
					}
				}
				return "OK: grapple hook icon patch = " + (GrappleIconPatch.Enabled ? "ON" : "OFF")
					+ " (mesh '" + GrappleIconPatch.HookMeshName + "'). Reopen the inventory screen to redraw the icon.";
			}

			// 🪦 `hookbelt`（2026-10-08 立，同日证伪，**死代码已删**）—— 保留这条命令只为**返回正确指引**
			//    （静默失效 = 用户以为"没用"，2026-10-05 相机那次事故的教训）：
			//    改挂件网格的帧对"挂到骨架"那条链**无效**（native 干的），唯一杠杆 = 本模块的槽位数据。
			if (sub == "hookbelt")
			{
				return "grapple: 'hookbelt' is RETIRED (proven ineffective 2026-10-08; its code has been deleted) -"
					+ " the holster attach is done by NATIVE code and ignores any mesh frame we set."
					+ " The only lever is the SLOT data: LivingWorldNpcs/ModuleData/item_holsters.xml ->"
					+ " item_holster 'lwn_grapple_hook_hip' -> holster_rotation_yaw_pitch_roll."
					+ " Axis roles (measured in game 2026-10-08): yaw = turn in the HORIZONTAL plane;"
					+ " pitch = spin about the hook's own long axis; roll = the VERTICAL tilt (the only one that"
					+ " can make it hang down). At '0,0,0' the claws point straight FORWARD; roll=70 is the natural"
					+ " hanging angle (90 = straight down but looks stiff).";
			}

			// 🔴 `iconscale` = 图标里两件道具**占画面宽度的比例**（2026-10-08 用户报"图标又小又偏"的修法）。
			//    背包图标的取景是**按物品类型写死**的（Bow→按真弓取景 / Arrows→按真箭取景）⇒ 我们的
			//    13 cm 绳 / 20 cm 钩顶着弓/箭的框，自然又小又偏。取景补丁按视场角算距离 ⇒ 这一格就是"填多满"。
			//    不给参数 = 看当前值；给 0 = 关掉（回到引擎原样，A/B 用）。
			if (sub == "iconscale")
			{
				float ropeF = ParseF(args, at + 0, float.NaN);
				if (!float.IsNaN(ropeF) && ropeF >= 0f) { GrappleIconPatch.RopeIconFill = ropeF; }
				float hookF = ParseF(args, at + 1, float.NaN);
				if (!float.IsNaN(hookF) && hookF >= 0f) { GrappleIconPatch.HookIconFill = hookF; }
				return $"OK: icon fill = rope {GrappleIconPatch.RopeIconFill:F2} / hook {GrappleIconPatch.HookIconFill:F2}"
					+ " (fraction of the icon width the item's diagonal spans; 0 = engine original). Reopen the inventory screen to redraw.";
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
					// 自动长度留的余量比例（1.2 = 永远比跨度长 20%）—— **飞行那档**；
					// 手里那档固定 1.0（绷直，用户 2026-10-08），第 2 个参数可改它。
					string a0 = ArgAt(args, at + 0);
					float v = ParseF(args, at + 0, -1f);
					if (string.IsNullOrEmpty(a0))
					{
						result = $"grapple: rope slack — flight={GrappleLogic.FlightRopeSlackRatio:F2}"
							+ $" | hand->hook(B)={GrappleLogic.HandRopeSlackRatio:F2} (必须 1.00 = 绷直)"
							+ $" | ring->hand(A)={GrappleLogic.RopeASlackRatio:F2} (要松)"
							+ $" | minLen flight={GrappleLogic.FlightRopeMinLength:F2} B={GrappleLogic.HandRopeMinLength:F2} A={GrappleLogic.RopeAMinLength:F2}"
							+ "   (usage: custom.grapple slack <flight> [handB] [ringA]; 1 = exactly the span = taut)";
						break;
					}
					if (v < 1f) { result = "Error: slack needs a ratio >= 1 (e.g. custom.grapple slack 1.2)"; break; }
					GrappleLogic.FlightRopeSlackRatio = v;
					rope.SlackRatio = v;
					float hand = ParseF(args, at + 1, -1f);
					if (hand >= 1f) { GrappleLogic.HandRopeSlackRatio = hand; }
					float ringA = ParseF(args, at + 2, -1f);
					if (ringA >= 1f) { GrappleLogic.RopeASlackRatio = ringA; }
					result = $"grapple: rope slack flight={GrappleLogic.FlightRopeSlackRatio:F2}"
						+ $" handB={GrappleLogic.HandRopeSlackRatio:F2} ringA={GrappleLogic.RopeASlackRatio:F2}"
						+ $"（下一帧起生效）· {logic.Refresh()}";
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
					if (string.IsNullOrEmpty(name)) { result = "Error: hmesh needs a mesh name (e.g. custom.grapple hmesh hook)"; break; }
					GrappleHook.MeshCandidates = new[] { name, "lwn_grapple_hook", "push_fork" };
					result = $"grapple: hook mesh candidates = {name}, lwn_grapple_hook, push_fork (takes effect on next throw)";
					break;
				}

				case "spin":
				{
					// 手里的钩（设计 B，2026-10-07）：绕右手转的转速/半径；off = 手上不显示钩与绳。
					// 无参 = 看当前值 + **实体/网格自证**（回答"到底召唤出实体没有、网格挂上没"）。
					string a0 = ArgAt(args, at + 0);
					if (string.IsNullOrEmpty(a0))
					{
						string park = "-";
						try { park = GrappleLogic.Ensure().ParkStateLine(); } catch (Exception) { }
						result = $"grapple: hand hook = {(GrappleLogic.HandHookEnabled ? "ON" : "OFF")}"
							+ $" | rpm idle {GrappleLogic.HandHookRpmIdle:F0} / aim {GrappleLogic.HandHookRpmAim:F0}"
							+ $" (now {GrappleLogic.HandHookRpmNow:F0} = {(GrappleLogic.HandHookAimingNow ? "aiming" : "idle")})"
							+ $" | radius {GrappleLogic.HandHookRadius:F2} m"
							+ $" | rope {(GrappleLogic.HandRopeEnabled ? "ON" : "OFF")}"
							+ $" | {park}"
							+ "   (usage: custom.grapple spin <rpm> [radius] | spin idle|aim <rpm> | spin on|off | spinrope <0|1>)";
						break;
					}
					if (a0 == "off")
					{
						GrappleLogic.HandHookEnabled = false;
						result = "grapple: hand hook OFF";
						break;
					}
					if (a0 == "on")
					{
						GrappleLogic.HandHookEnabled = true;
						result = "grapple: hand hook ON";
						break;
					}

					// 🔴 分档调（2026-10-08）：`spin idle 135` / `spin aim 270` —— 只改那一档。
					if (a0 == "idle" || a0 == "aim")
					{
						float v = ParseF(args, at + 1, float.NaN);
						if (float.IsNaN(v))
						{
							result = $"Error: spin {a0} needs a number (rpm). e.g. custom.grapple spin {a0} "
								+ (a0 == "idle" ? GrappleLogic.HandHookRpmIdle.ToString("F0") : GrappleLogic.HandHookRpmAim.ToString("F0"));
							break;
						}
						if (a0 == "idle") { GrappleLogic.HandHookRpmIdle = v; }
						else { GrappleLogic.HandHookRpmAim = v; }
						result = $"grapple: hand hook rpm {a0} = {v:F0}"
							+ $" (idle {GrappleLogic.HandHookRpmIdle:F0} / aim {GrappleLogic.HandHookRpmAim:F0}, takes effect immediately)";
						break;
					}

					float rpm = ParseF(args, at + 0, float.NaN);
					if (float.IsNaN(rpm))
					{
						result = "Error: spin needs a number (rpm), idle|aim <rpm>, or on|off. e.g. custom.grapple spin 135";
						break;
					}
					GrappleLogic.HandHookRpmIdle = rpm;      // 单一数值 = **两档一起设**（老用法不变）
					GrappleLogic.HandHookRpmAim = rpm;
					GrappleLogic.HandHookEnabled = true;
					float radius = ParseF(args, at + 1, -1f);
					if (radius > 0f) { GrappleLogic.HandHookRadius = radius; }
					result = $"grapple: hand hook rpm idle+aim {rpm:F0}, radius {GrappleLogic.HandHookRadius:F2} m"
						+ "   (per-state: custom.grapple spin idle|aim <rpm>)";
					break;
				}

				case "hand":
				{
					// 🔴 **手上那枚钩的分档诊断**（2026-10-07 晚）—— 阶梯表见 GrappleLogic.HandProbeStage。
					//    设计：每上一档**只比上一档多一个变量** ⇒ 「第一档开始看不见」= 元凶就在那一档新加的那个变量上。
					//    全程用同一枚实体（换档不重建），免得"重建"混进来当第二个变量。
					string a0 = ArgAt(args, at + 0);
					int stage = -1;
					if (!string.IsNullOrEmpty(a0))
					{
						int.TryParse(a0, out stage);
					}
					if (stage < 0 || stage > 4)
					{
						result = "grapple: hand probe = " + GrappleLogic.HandProbeStage
							+ " | " + HandProbeDesc(GrappleLogic.HandProbeStage)
							+ "   (usage: custom.grapple hand <0|1|2|3|4>  = " + HandProbeLadder + ")";
						break;
					}
					logic.SetHandProbe(stage);
					string park = "-";
					try { park = logic.ParkStateLine(); } catch (Exception) { }
					result = "grapple: hand probe = " + stage + " | " + HandProbeDesc(stage)
						+ " | " + park
						+ "   (the state above is from BEFORE the next tick - the new placement lands next frame;"
						+ " walk the ladder 1 -> 4: the FIRST stage you cannot see is where the culprit variable is)";
					break;
				}

				case "retspeed":
				{
					// 打空返程：钩头"追着手飞回"的速度 / 到位判定距离
					string a0 = ArgAt(args, at + 0);
					if (string.IsNullOrEmpty(a0))
					{
						result = $"grapple: return speed = {GrappleHook.ReturnSpeed:F0} m/s (flight {GrappleHook.Speed:F0})"
							+ $" | arrive distance = {GrappleLogic.ReturnArriveDistance:F2} m"
							+ "   (usage: custom.grapple retspeed <m/s> [arriveMeters])";
						break;
					}
					float v = ParseF(args, at + 0, -1f);
					if (v <= 0f) { result = "Error: retspeed needs m/s > 0 (e.g. custom.grapple retspeed 25)"; break; }
					GrappleHook.ReturnSpeed = v;
					float arr = ParseF(args, at + 1, -1f);
					if (arr > 0f) { GrappleLogic.ReturnArriveDistance = arr; }
					result = $"grapple: return speed = {v:F0} m/s | arrive distance = {GrappleLogic.ReturnArriveDistance:F2} m (takes effect immediately)";
					break;
				}

				case "palm":
				{
					// 手部挂点沿小臂外移（腕关节 → 掌心）—— 绳的近端 / 钩的圆心 / 开火起点**三处共用**
					string a0 = ArgAt(args, at + 0);
					if (string.IsNullOrEmpty(a0))
					{
						result = $"grapple: palm offset (wrist -> palm, along the forearm) = {GrappleLogic.HandPalmOffset:F3} m"
							+ "   (usage: custom.grapple palm <meters>; 0 = exactly on the wrist bone, 0.08 = palm centre,"
							+ " negative = pull back toward the wrist)";
						break;
					}
					float m = ParseF(args, at + 0, float.NaN);
					if (float.IsNaN(m)) { result = "Error: palm needs meters, e.g. custom.grapple palm 0.08"; break; }
					GrappleLogic.HandPalmOffset = m;
					result = $"grapple: palm offset = {m:F3} m (rope end / hook pivot / shot origin all move together; takes effect immediately)";
					break;
				}

				case "armaxis":
				{
					// 手里那枚的**绕转轴**（2026-10-07 晚用户要求"以手臂为轴做圆周运动"）——
					// 圆所在的平面 ⊥ 这个轴；即时生效、不用重编。
					string a0 = ArgAt(args, at + 0);
					if (string.IsNullOrEmpty(a0))
					{
						result = $"grapple: hand hook axis = {GrappleLogic.HandHookAxis} | {HandAxisDesc(GrappleLogic.HandHookAxis)}"
							+ $" | pivot offset along axis = {GrappleLogic.HandHookAxisOffset:F3} m"
							+ $" | arm bones = [{SpellCastInput.LastArmBones}]"
							+ "   (usage: custom.grapple armaxis <0|1|2|3|4> [pivotOffsetMeters])";
						break;
					}
					int ax;
					if (int.TryParse(a0, out ax) && ax >= 0 && ax <= 4)
					{
						GrappleLogic.HandHookAxis = ax;
						float off = ParseF(args, at + 1, float.NaN);
						if (!float.IsNaN(off)) { GrappleLogic.HandHookAxisOffset = off; }   // 可正可负：正 = 离心向外，负 = 往腕
						result = $"grapple: hand hook axis = {ax} | {HandAxisDesc(ax)}"
							+ $" | pivot offset = {GrappleLogic.HandHookAxisOffset:F3} m (takes effect immediately)";
					}
					else
					{
						result = "Error: armaxis takes 0 (world up), 1 (fore/aft at your side), 2 (left/right sweep),"
							+ " 3 (perpendicular to the shoulder->hand chord) or 4 (perpendicular to the FOREARM, elbow->hand)"
							+ "; optional 2nd arg = pivot offset along the axis in meters";
					}
					break;
				}

				case "hookface":
				{
					// 手里那枚**朝哪**（哪个物体局部轴对准"离心向外"）—— 资产实测 = 钩体沿 +Z（默认 0）
					string a0 = ArgAt(args, at + 0);
					int f;
					if (string.IsNullOrEmpty(a0))
					{
						result = $"grapple: hook facing = {GrappleHook.HandFaceMode} | {HookFaceDesc(GrappleHook.HandFaceMode)}"
							+ $" | roll = {GrappleHook.HandRollDeg:F0} deg"
							+ "   (usage: custom.grapple hookface <0|1|2|3>)";
						break;
					}
					if (int.TryParse(a0, out f) && f >= 0 && f <= 3)
					{
						GrappleHook.HandFaceMode = f;
						result = $"grapple: hook facing = {f} | {HookFaceDesc(f)} (takes effect immediately)";
					}
					else
					{
						result = "Error: hookface takes 0 (+Z out, measured default), 1 (-Z out), 2 (+Y out) or 3 (-Y out)";
					}
					break;
				}

				case "hookroll":
				{
					// 绕"离心向外"那根轴滚转（三爪朝哪边弯的观感项）
					string a0 = ArgAt(args, at + 0);
					if (string.IsNullOrEmpty(a0))
					{
						result = $"grapple: hook roll = {GrappleHook.HandRollDeg:F0} deg (spin around the outward axis)"
							+ "   (usage: custom.grapple hookroll <degrees>)";
						break;
					}
					float deg = ParseF(args, at + 0, float.NaN);
					if (float.IsNaN(deg))
					{
						result = "Error: hookroll needs degrees, e.g. custom.grapple hookroll 60";
						break;
					}
					GrappleHook.HandRollDeg = deg;
					result = $"grapple: hook roll = {deg:F0} deg (takes effect immediately)";
					break;
				}

				case "armout":
				{
					// 圆心**横向远离身体**的偏移（治"转一圈有一小段看不见" = 圆内侧扫进躯干）
					string a0 = ArgAt(args, at + 0);
					if (string.IsNullOrEmpty(a0))
					{
						result = $"grapple: pivot out-shift (away from body) = {GrappleLogic.HandHookOutShift:F2} m"
							+ "   (usage: custom.grapple armout <meters>; 0 = pivot sits exactly on the hand bone)";
						break;
					}
					float m = ParseF(args, at + 0, -1f);
					if (m < 0f)
					{
						result = "Error: armout needs a non-negative number of meters";
						break;
					}
					GrappleLogic.HandHookOutShift = m;
					result = $"grapple: pivot out-shift = {m:F2} m (takes effect immediately)";
					break;
				}

				case "spinlog":
				{
					// 每帧抓帧（默认 2 秒后自动关）—— 抓"钩在圆周上瞬间不见"那一帧
					string a0 = ArgAt(args, at + 0);
					if (string.IsNullOrEmpty(a0))
					{
						result = $"grapple: spin frame capture = {(GrappleLogic.HandHookLog ? "ARMED" : "off")}"
							+ $" | default duration {GrappleLogic.HandHookLogSeconds:F1}s"
							+ "   (usage: custom.grapple spinlog 1 [seconds] | spinlog 0)"
							+ "   [every frame -> '#n angle pos radial | hand-src lateral inner-edge height | global/local/root/shown | ropes', auto-stops]";
						break;
					}
					if (a0 == "1" || a0 == "on")
					{
						float sec = ParseF(args, at + 1, -1f);
						if (sec > 0f) { GrappleLogic.HandHookLogSeconds = sec; }
						GrappleLogic.HandHookLog = true;      // 下一帧由 tick 真正开抓（计数清零 + 计时）
						result = $"grapple: spin frame capture ARMED for {GrappleLogic.HandHookLogSeconds:F1}s (every frame)";
					}
					else if (a0 == "0" || a0 == "off")
					{
						GrappleLogic.HandHookLog = false;
						result = "grapple: spin frame capture OFF";
					}
					else
					{
						result = "Error: spinlog takes 1 [seconds] | 0";
					}
					break;
				}

				case "ringface":
				{
					// 左手环的自身轴指向哪（环 = 我们自己画的实体，跟钩/绳同属一个根实体）
					string a0 = ArgAt(args, at + 0);
					int f;
					if (string.IsNullOrEmpty(a0))
					{
						result = $"grapple: left-hand ring face = {GrappleRig.RingFaceMode} | {RingFaceDesc(GrappleRig.RingFaceMode)}"
							+ $" | palm offset {GrappleRig.RingPalmOffset:F2} m | scale {GrappleRig.RingScale:F2}"
							+ "   (usage: custom.grapple ringface <0|1|2> [palmOffsetMeters])";
						break;
					}
					if (int.TryParse(a0, out f) && f >= 0 && f <= 2)
					{
						GrappleRig.RingFaceMode = f;
						float off = ParseF(args, at + 1, float.NaN);
						if (!float.IsNaN(off)) { GrappleRig.RingPalmOffset = off; }   // 可正可负：正 = 往指尖，负 = 往腕
						result = $"grapple: left-hand ring face = {f} | {RingFaceDesc(f)}"
							+ $" | palm offset = {GrappleRig.RingPalmOffset:F2} m (takes effect immediately)";
					}
					else
					{
						result = "Error: ringface takes 0 (axis along the forearm), 1 (axis = world up) or 2 (axis = your right)";
					}
					break;
				}

				case "ringscale":
				{
					string a0 = ArgAt(args, at + 0);
					if (string.IsNullOrEmpty(a0))
					{
						result = $"grapple: left-hand ring scale = {GrappleRig.RingScale:F2}"
							+ "   (usage: custom.grapple ringscale <multiplier>; 1 = asset size, 8 cm outer diameter)";
						break;
					}
					float s = ParseF(args, at + 0, -1f);
					if (s <= 0f) { result = "Error: ringscale needs a positive multiplier"; break; }
					GrappleRig.RingScale = s;
					result = $"grapple: left-hand ring scale = {s:F2} (takes effect immediately)";
					break;
				}

				case "ropelog":
				{
					// 绳/环/钩的"召唤/销毁/显隐"事件日志开关（默认开 —— 查 AV 时"崩在哪个事件之后"是唯一线索）
					string a0 = ArgAt(args, at + 0);
					if (string.IsNullOrEmpty(a0))
					{
						result = $"grapple: rope event log = {(GrappleRope.LogEvents ? "ON" : "OFF")}"
							+ "   (usage: custom.grapple ropelog <0|1>; logs [Rope] <tag> spawn/rebuild/teardown/show/hide)";
						break;
					}
					if (a0 == "1" || a0 == "on") { GrappleRope.LogEvents = true; result = "grapple: rope event log ON"; }
					else if (a0 == "0" || a0 == "off") { GrappleRope.LogEvents = false; result = "grapple: rope event log OFF"; }
					else { result = "Error: ropelog takes 0|1 (or on|off)"; }
					break;
				}

				// 左手环显示开关（2026-10-08 新增，**默认关**）：左手那件现在由物品网格（绳）负责，环是上一版做法。
				case "handring":
				{
					string hr = ArgAt(args, at + 0);
					if (!string.IsNullOrEmpty(hr))
					{
						string v = hr.Trim().ToLowerInvariant();
						if (v == "0" || v == "off" || v == "false") { GrappleRig.HandRingEnabled = false; }
						else if (v == "1" || v == "on" || v == "true") { GrappleRig.HandRingEnabled = true; }
						else { result = "Error: handring takes 0|1"; break; }
					}
					result = "grapple: left-hand ring (runtime entity) = " + (GrappleRig.HandRingEnabled ? "ON" : "OFF")
						+ "   (usage: custom.grapple handring 0|1)   [default OFF: the left hand is the rope item mesh now]";
					break;
				}

				// 🔴 **每帧面包屑**（2026-10-08 立）—— 查"栈丢失的 AccessViolation"专用：开着时每一步都往
				//    运行日志写一行 `[GTrace] f<帧号> <阶段>`（`DebugLogger` 逐行落盘 ⇒ 硬崩也留得住）
				//    ⇒ **崩前的最后一行 = 死掉的那一步**。代价 = 每帧十几行，只在复现那一次开。
				case "tracelog":
				{
					string a0 = ArgAt(args, at + 0);
					if (!string.IsNullOrEmpty(a0))
					{
						string v = a0.Trim().ToLowerInvariant();
						if (v == "0" || v == "off" || v == "false") { GrappleLogic.TraceTick = false; }
						else if (v == "1" || v == "on" || v == "true") { GrappleLogic.TraceTick = true; }
						else { result = "Error: tracelog takes 0|1 (or on|off)"; break; }
					}
					result = "grapple: per-frame breadcrumb trace = " + (GrappleLogic.TraceTick ? "ON" : "OFF")
						+ "   (usage: custom.grapple tracelog <0|1>; writes '[GTrace] f<frame> <stage>' every step."
						+ " Arm it, reproduce the crash, then send the LAST 30 lines of the runtime log)";
					break;
				}

				case "spinrope":
				{
					// 待机时那两条绳的开关（2026-10-08：拆成 a / b 两条，一次只开一条来二分 AV 嫌疑）
					string a0 = ArgAt(args, at + 0);
					if (string.IsNullOrEmpty(a0))
					{
						result = $"grapple: hand ropes A(ring->hand)={(GrappleLogic.HandRopeAEnabled ? "ON" : "OFF")}"
							+ $" B(hand->hook)={(GrappleLogic.HandRopeEnabled ? "ON" : "OFF")}"
							+ "   (usage: custom.grapple spinrope <off|a|b|ab>)"
							+ "   [the hand rope reproduces the 10-07/10-08 AccessViolation -"
							+ " turn ONE on at a time to bisect; see also 'custom.grapple tracelog 1']";
						break;
					}
					switch (a0)
					{
						case "0":
						case "off":
							GrappleLogic.HandRopeAEnabled = false;
							GrappleLogic.HandRopeEnabled = false;
							result = "grapple: hand ropes A=OFF B=OFF";
							break;
						case "1":
						case "on":
						case "b":
							GrappleLogic.HandRopeAEnabled = false;
							GrappleLogic.HandRopeEnabled = true;
							result = "grapple: hand ropes A=OFF B=ON (hand->hook rope only)";
							break;
						case "a":
							GrappleLogic.HandRopeAEnabled = true;
							GrappleLogic.HandRopeEnabled = false;
							result = "grapple: hand ropes A=ON B=OFF (ring->hand rope only)";
							break;
						case "ab":
							GrappleLogic.HandRopeAEnabled = true;
							GrappleLogic.HandRopeEnabled = true;
							result = "grapple: hand ropes A=ON B=ON";
							break;
						default:
							result = "Error: spinrope takes off | a | b | ab";
							break;
					}
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
						result = "Error: 'pull target' is retired -> the step-5 flow is now 'bind'"
							+ " (try: custom.grapple bind test  |  or just shoot a person with the grapple)";
						break;
					}
					result = "Error: pull needs 'self' (target = step 5 -> use 'bind')";
					break;
				}

				// ─────────────────── 步骤 5：勾人（缠 → 倒 → 缚，2026-10-09）───────────────────

				case "bind":
					result = DoBind(logic, args, at);
					break;

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
					// 🪦 **2026-10-10 退役**（用户二选一裁定："不许玩家转镜头" vs "落地方向用玩家真实转角" ⇒ 选了后者）。
					//    旧行为 = 拉拽一开始就把方向朝"引擎解冻后会重置成的值"转（俯仰拉到 0）—— 代价是
					//    **玩家飞行途中自己转的镜头会被扳回去**。现在：全程鼠标可转，**撒手那一刻**把玩家
					//    真实朝向写回引擎（`CameraReturnPolicy.WriteBackLookOnRelease`），两头都不跳。
					//    名字留在白名单里只为返回这条说明（静默回落 = 用户以为"没用"，2026-10-05 相机那次事故的教训）。
					result = "grapple: 'lookret' is RETIRED (2026-10-10) - the camera now follows YOU during the pull"
						+ " (turn it with the mouse) and we write YOUR heading back to the engine at release,"
						+ " so there is no forced reset. No knob to tune; just look where you want to land."
						+ " Arm/FOV return timing is still tunable via 'custom.grapple camret'.";
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

				case "carrier":
				{
					// **载具体检**（2026-10-10 立；用户报的两条都落在这块板子上）：
					//   ① 视觉 = 隐形代理（预制体的网格换掉了）⇒ `spawn` 之后**脚下应该什么都看不见**；
					//   ② 物理足迹 = 预制体根节点 scale 0.300 ⇒ 原版 5.29 × 5.16 米缩到 ≈1.59 × 1.55 米
					//      （谁站在板面上谁被抬走：缩之前站在 2 米外的人也会跟着飞）。
					//   数值一律问**引擎自己的物理包围盒**（`CarrierBoard.TryGetFootprint`），不是我们算的。
					string what = (ArgAt(args, at + 0) ?? "").ToLowerInvariant();
					if (what == "clear")
					{
						LiveProbeCarrier()?.Remove();
						ProbeCarrier = null;
						ProbeScenePtr = UIntPtr.Zero;
						result = "grapple: carrier probe board removed.";
						break;
					}

					string head = "";
					if (what == "spawn")
					{
						LiveProbeCarrier()?.Remove();
						ProbeCarrier = null;
						var probe = new Flight.CarrierBoard();
						float feet = Flight.CarrierBoard.CollisionCapsuleBottomZ(Agent.Main);
						if (!probe.Spawn(Mission.Current.Scene,
								new Vec3(Agent.Main.Position.x, Agent.Main.Position.y, feet - Flight.FlightTuning.CarrierSpawnGap)))
						{
							result = "Error: carrier probe spawn failed (prefab or mesh missing? see log)";
							break;
						}
						ProbeCarrier = probe;
						ProbeScenePtr = Mission.Current.Scene.Pointer;
						head = "carrier probe spawned under you (must be INVISIBLE) | ";
					}

					Flight.CarrierBoard live = LiveProbeCarrier();
					if (live != null && live.TryGetFootprint(out _, out float phx, out float phy, out float ptop))
					{
						result = head + $"probe board physics box = {phx * 2f:F2} x {phy * 2f:F2} m (top z={ptop:F2}), "
							+ $"others standing on it = {live.CountRiders(Agent.Main)}"
							+ " | vanilla platform = 5.29 x 5.16 m, ours is scaled 0.30";
						break;
					}

					GrapplePull pull = GrappleLogic.Current?.Pull;
					if (pull != null
						&& pull.TryDescribeBoard(out _, out float hx, out float hy, out float top, out int others))
					{
						result = $"pull board physics box = {hx * 2f:F2} x {hy * 2f:F2} m (top z={top:F2}), "
							+ $"others on board = {others}"
							+ " | riders other than you get a gentle eviction while the board is still low"
							+ $" (<= {Flight.FlightTuning.CarrierEvictMaxLift:F1} m)";
						break;
					}

					result = head + "no carrier board right now - run 'custom.grapple carrier spawn'"
						+ " to put a probe board under you (it stays until 'custom.grapple carrier clear').";
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

		/// <summary>`custom.grapple hand` 的阶梯一句话（纯英文 —— 控制台纪律）。表见 GrappleLogic.HandProbeStage。</summary>
		private const string HandProbeLadder =
			"1 in-front-of-eyes/placed-once, 2 in-front-of-eyes/every-frame, 3 right-hand-bone/identity,"
			+ " 4 hand-orbit/radial-facing (= real thing), 0 off";

		/// <summary>`custom.grapple ringface` 的三档含义（命令回执用，纯英文）。</summary>
		private static string RingFaceDesc(int f)
		{
			switch (f)
			{
				case 1: return "1 = ring axis = world up => ring plane horizontal (flat on the palm)";
				case 2: return "2 = ring axis = your right => ring plane vertical, facing fore/aft";
				default: return "0 = ring axis along the FOREARM (elbow->hand) => ring plane perpendicular to the forearm (like a loose bracelet)";
			}
		}

		/// <summary>`custom.grapple hookface` 的四档含义（命令回执用，纯英文）。</summary>
		private static string HookFaceDesc(int f)
		{
			switch (f)
			{
				case 0: return "0 = +Z points outward";
				case 1: return "1 = -Z points outward";
				case 2: return "2 = +Y points outward (the hook BODY runs along +Y, tail ring at the origin)";
				case 3: return "3 = -Y points outward   [DEFAULT, together with hookroll 180 = user-approved]";
				default: return "(unknown)";
			}
		}

		/// <summary>`custom.grapple armaxis` 的三档含义（命令回执用，纯英文）。</summary>
		private static string HandAxisDesc(int axis)
		{
			switch (axis)
			{
				case 1: return "1 = axis along your right => circle in the vertical plane, swinging fore/aft";
				case 2: return "2 = axis along your facing => circle in the frontal plane, sweeping left/right";
				case 3: return "3 = axis = shoulder->hand chord (two bone reads) => circle PERPENDICULAR to that chord";
				case 4: return "4 = axis = FOREARM (elbow->hand, HumanBone.ForearmR/HandR) => circle PERPENDICULAR to the forearm   [DEFAULT]";
				default: return "0 = axis = world up => horizontal circle (spins around a hanging forearm)";
			}
		}

		/// <summary>某一档的含义（命令回执用，纯英文）。</summary>
		private static string HandProbeDesc(int stage)
		{
			switch (stage)
			{
				case 0: return "OFF - normal hand hook (gated by 'spin on|off' and the wielded-grapple check)";
				case 1: return "1 = in front of eyes (2 m ahead / 1.2 m up), identity rotation, placed ONCE (= custom.spawn_mesh recipe)";
				case 2: return "2 = in front of eyes, identity rotation, placed EVERY frame (follows you)";
				case 3: return "3 = right-hand bone + 0.18 m up (same anchor as the spell charge ball), identity rotation, every frame";
				case 4: return "4 = hand + 0.20 m orbit, RADIAL facing, every frame (= the real hand hook)";
				default: return "(unknown stage)";
			}
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
		/// 🔴 物品在**本模块**里（`ModuleData/items/grapple.xml`，2026-10-09 随通用玩法从内容包搬来）；
		///    名字改了 / 物品段没加载 = 这里给一句明确的英文错误，不崩。
		/// 🔴 两轮查找（铁律 5）：第一轮按 StringId 精确找；第二轮在内存里按名字**包含**扫一遍兜底。
		/// </summary>
		/// <summary>找一件物品该进哪个武器槽：**已经在手上**就还给它的槽；否则给第一个**空槽**；
		/// 没有空槽返回 <see cref="EquipmentIndex.None"/>。
		/// 🔴 2026-10-08 用户点出的问题：旧版硬写 0/1 号槽 ⇒ **会把玩家手里的剑盾顶掉**。
		/// 只扫前四个主武器槽（第 5 格 `ExtraWeaponSlot` 是旗子专用的，塞不进弹药）。</summary>
		private static EquipmentIndex FindFreeWeaponSlotFor(Agent agent, ItemObject item)
		{
			EquipmentIndex empty = EquipmentIndex.None;
			for (EquipmentIndex slot = EquipmentIndex.WeaponItemBeginSlot;
				slot < EquipmentIndex.NumPrimaryWeaponSlots; slot++)
			{
				MissionWeapon w = agent.Equipment[slot];
				if (w.Item != null && w.Item.StringId == item.StringId)
				{
					return slot;                       // 这件已经在手上
				}
				if (empty == EquipmentIndex.None && w.IsEmpty)
				{
					empty = slot;
				}
			}
			return empty;
		}

		private static string DoEquip()
		{
			// 🔴 物品 id 的**唯一真相**在 Combat/GrappleFirePatch.cs 的两个常量里（那里是拦截判据；
			//    该类的 namespace 是 `LivingWorldNpcs`，不是 `.Combat`）——
			//    这里引用它们，别再抄第二份字面量（2026-10-09 搬进 LWN 时顺手统一）。
			ItemObject rope = ResolveContentItem(GrappleFirePatch.RopeItemId, "GrappleRope");
			ItemObject hook = ResolveContentItem(GrappleFirePatch.HookItemId, "GrappleHook");
			if (rope == null || hook == null)
			{
				return $"Error: grapple items not found (expected {GrappleFirePatch.RopeItemId} / {GrappleFirePatch.HookItemId} from this module's ModuleData/items/grapple.xml)";
			}

			// ① 场景里：直接装到玩家手上（当场可用）
			Agent main = Agent.Main;
			if (Mission.Current != null && main != null)
			{
				try
				{
					// 🔴 先给**绳**找槽 → 装上 → 再给**钩**找槽（顺序不能反：
					//    两件都在找空槽时若先一起算，会算出同一个槽）。
					EquipmentIndex ropeSlot = FindFreeWeaponSlotFor(main, rope);
					if (ropeSlot == EquipmentIndex.None)
					{
						return "Error (mission): no free weapon slot for GrappleRope (4 slots all taken; sheath something first).";
					}
					if (main.Equipment[ropeSlot].Item == null)
					{
						MissionWeapon ropeWeapon = new MissionWeapon(rope, null, main.Origin?.Banner);
						main.EquipWeaponWithNewEntity(ropeSlot, ref ropeWeapon);
					}

					EquipmentIndex hookSlot = FindFreeWeaponSlotFor(main, hook);
					if (hookSlot == EquipmentIndex.None)
					{
						return "Error (mission): GrappleRope is on, but no free weapon slot for GrappleHook (it occupies one ammo slot - same cost as vanilla bow + arrows).";
					}
					if (main.Equipment[hookSlot].Item == null)
					{
						MissionWeapon hookWeapon = new MissionWeapon(hook, null, main.Origin?.Banner);
						main.EquipWeaponWithNewEntity(hookSlot, ref hookWeapon);
					}

					// 🔴 把钩的**数量钉死在 1**（2026-10-03）：物品 `stack_amount` 是 20 —— 为什么不是 1，
					//    见 `ModuleData/items/grapple.xml` 顶上的注释（HUD 的子弹数只统计"最大堆叠 > 1"的弹药；
					//    写 1 = 能射但永远显示 0）。装填完立刻设成 1 = "上限 20、实有 1"。
					main.SetWeaponAmountInSlot(hookSlot, 1, false);
					main.UpdateAgentStats();
					return $"OK (mission): GrappleRope in slot {(int)ropeSlot} + GrappleHook x1 in slot {(int)hookSlot} - hold LMB to aim, release to fire.";
				}
				catch (Exception ex)
				{
					return "Error: equip failed (" + ex.GetType().Name + ": " + ex.Message + ")";
				}
			}

			// ② 大地图：进主队辎重（玩家自己去物品栏装备 —— 两件是**一对**，要同时装备才有用）
			try
			{
				Hero hero = Hero.MainHero;
				if (hero == null)
				{
					return "Error: no main hero.";
				}
				int givenRope = AgentControlHelper.TransferItems(null, hero, rope, 1);
				int givenHook = AgentControlHelper.TransferItems(null, hero, hook, 1);
				if (givenRope <= 0 && givenHook <= 0)
				{
					return "Error: could not add grapple items to the party inventory.";
				}
				return $"OK (campaign): added to party inventory - GrappleRope x{givenRope}, GrappleHook x{givenHook}. Equip BOTH (weapon slot + ammo slot), then enter a battle and fire. Run again for spares.";
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

		/// <summary>
		/// `custom.grapple bind ...` —— 步骤 5「勾人」的一族（方案 = plans\钩索-实施计划.md §13.7）。
		/// 子命令（全部纯英文返回、首参可弃）：
		///   on|off                      总开关（关 = 勾到人也只挂住不动 = 旧行为）
		///   test                        对**最近的可捆目标**直接走整套（不走飞行；机制验收主入口）
		///   release                     立刻放开当前目标（起身动画 + 稍后还 AI）
		///   probe                       验收"被捆的人不接命令"：真发一条 ComeHere 给目标，看"事件忽略"计数涨不涨
		///   state                       状态一行（目标 / 相位 / 计时 / 当前动作名 / 钩位置 / 接管计数）
		///   time &lt;秒&gt;                 超时自动挣脱（默认 20）
		///   drag &lt;米&gt;                 拽倒时朝玩家拖多远（默认 2）
		///   yankhold / yankpull / fallwait &lt;秒&gt;   拽倒三段：绷住（默认 0.15）→ 猛拽（0.25）→ 倒下（0.70）
		///   yank curve|blow             拽倒用哪套（curve = 我们写位移 / blow = 引擎冲量）
		///   blowforce &lt;数值&gt; · blowalt 0|1       引擎冲量那条的力度与判定路径
		///   wrap &lt;秒&gt; [圈数]          缠的时长 / 圈数（默认 0.55 / 1.25）
		///   anim &lt;lay|cycle|standup|auto&gt;  强制目标播某一段（观感验收；auto = 交回流程）
		///   panim &lt;动作名|auto&gt;        在**玩家自己**身上试播一个动作（只为选型看一眼；auto = 什么都不做）
		/// </summary>
		private static string DoBind(GrappleLogic logic, List<string> args, int at)
		{
			string what = (ArgAt(args, at + 0) ?? "state").ToLowerInvariant();
			switch (what)
			{
				case "on":
					GrappleBind.Enabled = true;
					return "OK: grapple bind = ON";
				case "off":
					GrappleBind.Enabled = false;
					return "OK: grapple bind = OFF (hook hitting a person does nothing = old behavior)";
				case "state":
					return logic.BindStateLine();
				case "release":
					return logic.BindRelease();
				case "probe":
					return logic.BindProbe();
				case "test":
					return logic.BindTest(FindNearestBindableAgent(30f));
				case "time":
				{
					float v = ParseF(args, at + 1, -1f);
					if (v < 0f) return $"grapple: bind auto-release timeout = {GrappleBind.BoundSeconds:F0}s (0 = NEVER struggle free; the default is 0 - release is player-driven)";
					GrappleBind.BoundSeconds = v;
					return v <= 0f
						? "OK: bind auto-release = NEVER (target stays bound until 'bind release' or the interaction panel)"
						: $"OK: bind auto-release after {v:F1}s (debug value; 0 = never)";
				}
				case "drag":
				{
					float v = ParseF(args, at + 1, -1f);
					if (v < 0f) return $"grapple: bind drag = {GrappleBind.DragDistance:F2}m (the target is pulled this far toward the player)";
					GrappleBind.DragDistance = v;
					return $"OK: bind drag distance = {v:F2}m (the target is pulled this far toward the player)";
				}
				// ── 拽倒三段（2026-10-09：绷住 → 猛拽 → 倒下；位移与倒地必须分开才有"被拽"的读感）──
				case "yankhold":
				{
					float v = ParseF(args, at + 1, -1f);
					if (v < 0f) return $"grapple: yank hold = {GrappleBind.YankHoldSeconds:F2}s (target stays put this long; the rope goes taut)";
					GrappleBind.YankHoldSeconds = v;
					return $"OK: yank hold = {v:F2}s";
				}
				case "yankpull":
				{
					float v = ParseF(args, at + 1, -1f);
					if (v < 0f) return $"grapple: yank pull = {GrappleBind.YankPullSeconds:F2}s (fast drag while still standing; shorter = snappier)";
					GrappleBind.YankPullSeconds = v;
					return $"OK: yank pull = {v:F2}s (the visible displacement window; short = reads as a jerk)";
				}
				case "fallwait":
				{
					float v = ParseF(args, at + 1, -1f);
					if (v < 0f) return $"grapple: fall wait = {GrappleBind.YankFallSeconds:F2}s (how long the lie-down animation plays before the ground loop takes over)";
					GrappleBind.YankFallSeconds = v;
					return $"OK: fall wait = {v:F2}s (raise until the cut into the lying loop is invisible)";
				}
				case "wrap":
				{
					float sec = ParseF(args, at + 1, -1f);
					if (sec >= 0f) GrappleBind.WrapSeconds = sec;
					float turns = ParseF(args, at + 2, float.NaN);
					if (!float.IsNaN(turns)) GrappleBind.WrapTurns = turns;
					return $"OK: wrap = {GrappleBind.WrapSeconds:F2}s / {GrappleBind.WrapTurns:F2} turns";
				}
				// ── 拽倒方式 A/B（2026-10-09 用户提出"用引擎冲量"）──
				case "yank":
				{
					string m = (ArgAt(args, at + 1) ?? "state").ToLowerInvariant();
					if (m == "curve") { GrappleBind.Yank = GrappleBind.YankMode.Curve; }
					else if (m == "blow") { GrappleBind.Yank = GrappleBind.YankMode.Blow; }
					else if (m != "state") { return "Error: bind yank expects curve|blow"; }
					return $"OK: yank mode = {GrappleBind.Yank}"
						+ (GrappleBind.Yank == GrappleBind.YankMode.Blow
							? $" (engine impulse: force={GrappleBind.BlowDamage:F0}, knockDown={GrappleBind.BlowKnockDownFlag}, altAttack={GrappleBind.BlowAlternativeAttack})"
							: " (our own displacement curve)");
				}
				case "blowforce":
				{
					float v = ParseF(args, at + 1, -1f);
					if (v < 0f) return $"grapple: blow force = {GrappleBind.BlowDamage:F0} (damage number fed to the engine; target is Immortal during the blow so no HP is lost)";
					GrappleBind.BlowDamage = v;
					return $"OK: blow force = {v:F0} (engine knockdown/knockback threshold is 'damage >= maxHealth * (resistance - penetration)'; no HP is lost)";
				}
				case "blowalt":
				{
					float v = ParseF(args, at + 1, -1f);
					if (v < 0f) return $"grapple: blow altAttack = {GrappleBind.BlowAlternativeAttack} (1 = deterministic knockback path, 0 = natural weapon path)";
					GrappleBind.BlowAlternativeAttack = v > 0.5f;
					return $"OK: blow altAttack = {GrappleBind.BlowAlternativeAttack}";
				}
				case "anim":
					return logic.BindForceAnim(ArgAt(args, at + 1) ?? "auto");
				// 🔴 原版 AI 冻结开关（2026-10-09）：关掉 = 回到"只压旗标"的旧行为，做 A/B 用。
				//    旗标只禁"跑/攻击"，**不禁走、不禁选目标** ⇒ 战斗场景里关掉它对方照旧打架。
				case "aipause":
				{
					string m = ArgAt(args, at + 1)?.ToLowerInvariant();
					if (m == "0" || m == "off") { GrappleBind.PauseVanillaAI = false; }
					else if (m == "1" || m == "on") { GrappleBind.PauseVanillaAI = true; }
					else if (m != null && m != "state") { return "Error: bind aipause expects 0|1"; }
					return $"OK: pause target's vanilla AI = {GrappleBind.PauseVanillaAI}"
						+ (GrappleBind.PauseVanillaAI
							? " (SetIsAIPaused(true) while bound -- this is what actually holds them in battle)"
							: " (OFF: only DoNotRun|NoAttack are pressed -- they will still be driven by the combat AI in battle)");
				}
				case "panim":
					return DoBindPanim(args, at);
			}
			return "Error: bind expects on|off|test|release|probe|state|time|drag|wrap|yankhold|yankpull|fallwait|yank|blowforce|blowalt|anim|panim|aipause";
		}

		/// <summary>
		/// `bind panim &lt;动作|auto&gt;`：在**玩家自己**身上试播一个动作 —— 只为"拉人时玩家该演什么"选型时看一眼
		/// （v1 不驱动玩家侧动画，见方案 §13.6）。`auto` = 什么都不做。
		/// </summary>
		private static string DoBindPanim(List<string> args, int at)
		{
			string act = ArgAt(args, at + 1);
			if (string.IsNullOrEmpty(act) || act.ToLowerInvariant() == "auto")
			{
				return "OK: bind panim = auto (player animation not driven by the bind flow; see plan §13.6)";
			}
			Agent main = Agent.Main;
			if (main == null) return "Error: no main agent.";
			AgentControlHelper.SetPose(main, act);
			return $"OK: played '{act}' once on the player (look only; the bind flow does not drive the player's animation)";
		}

		/// <summary>
		/// 找**最近的可捆目标**（`bind test` 用）：排除自己 / 坐骑 / 骑在马上的人 / 儿童 / 失效者。
		/// 与 <see cref="GrappleBind.Begin"/> 的准入同口径（那边还会再判一次 —— 那是权威）。
		/// </summary>
		private static Agent FindNearestBindableAgent(float maxDist)
		{
			try
			{
				Mission mission = Mission.Current;
				if (mission == null || Agent.Main == null) return null;
				Agent best = null;
				float bestD = maxDist;
				foreach (Agent a in mission.Agents)
				{
					if (a == null || a == Agent.Main) continue;
					if (!AgentControlHelper.SafeIsActive(a)) continue;
					if (a.IsMount || a.RiderAgent != null) continue;
					if (a.MountAgent != null) continue;
					string mid = null;
					try { mid = a.Monster?.StringId; } catch (Exception) { }
					if (mid != null && mid.IndexOf("child", StringComparison.OrdinalIgnoreCase) >= 0) continue;
					float d = a.Position.Distance(Agent.Main.Position);
					if (d < bestD) { bestD = d; best = a; }
				}
				return best;
			}
			catch (Exception)
			{
				return null;
			}
		}
	}
}
