using System;
using System.Collections.Generic;
using System.Globalization;
using LivingWorldNpcs.Animation;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
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
	///   custom.grapple equip [both|rope|hook]  把钩索武器 + 绳弹**当场发到玩家手上**（默认 both = 两件一起）——
	///                                     之后按住左键瞄准出准星、松手发射 = 钩头飞出（拦截补丁接的）。
	///                                     🔴 在场景里**选装是权威的**：`rope` = 配成"只有绳"（并卸掉身上已装备的钩，
	///                                     复现"缺钩爪"现场：手里那枚转的钩不显示、按开火键弹缺件提示）；
	///                                     `hook` = 配成"只有钩"。大地图那条只发物品、不动已有装备。
	///   custom.grapple hint               强制弹一次"缺钩爪"提示（不按键也能验文案 / 中文层 / 颜色）
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
	/// —— 音效（2026-10-10）——
	///   custom.grapple sound               🔴 验收：原地试听抛出绳声一次 + 打印状态
	///                                     （事件名 / 开关 / 名字能不能查到 —— 静音故障的第一诊断入口）
	///   custom.grapple sound on|off        绳声开关（默认开）
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
					|| s == "nz" || s == "dz" || s == "headroom" || s == "ring" || s == "backoff" || s == "lreset" || s == "equip" || s == "hint" || s == "iconhook" || s == "iconscale" || s == "hookbelt" || s == "handring" || s == "tracelog"
					|| s == "pull" || s == "pulltime" || s == "arc" || s == "delay" || s == "autopull" || s == "cam" || s == "facehook" || s == "camret" || s == "lookret" || s == "carrier"
					|| s == "mountedpull" || s == "mountedwait" || s == "horse"					|| s == "anim" || s == "animlock" || s == "animblend" || s == "animthr"
					|| s == "bind" || s == "carry"
					|| s == "aimcam" || s == "aimanchor" || s == "aimlift" || s == "aimsens"
					|| s == "sound")
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
			// 🔴 2026-10-12 起带选装参数：`equip rope` = **只给绳**（复现"缺钩爪"现场，验收用）。
			if (sub == "equip")
			{
				return DoEquip(ArgAt(args, at));
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
					result = DoEquip(ArgAt(args, at));
					break;

				// 缺件提示（2026-10-12）：不按键直接弹一次 —— 验文案 / 中文层 / 颜色用
				case "hint":
					result = DoHint(logic);
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

				// ─────────────── 步骤 6：扛人（被扛者纵向位移在动画里，2026-10-10）───────────────

				case "carry":
					result = DoCarry(logic, args, at);
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

				// ── 骑马勾到地形（2026-10-13）：自动下马 → 起飞（取代旧的"直接收钩"）──
				//    ⚠️ 名字带 pull：**玩家自己**骑马时"拉自己"的那条；"骑马的目标收不收"是 `bind mounted`。
				case "mountedpull":
				{
					string m = (ArgAt(args, at + 0) ?? "state").ToLowerInvariant();
					if (m == "0" || m == "off") { GrappleLogic.MountedPull = false; }
					else if (m == "1" || m == "on") { GrappleLogic.MountedPull = true; }
					else if (m != "state") { return "Error: mountedpull expects 0|1"; }
					result = $"grapple: self-pull while mounted = {GrappleLogic.MountedPull}"
						+ (GrappleLogic.MountedPull
							? " (hook bites terrain while YOU are on a horse -> we press the engine's DISMOUNT for you,"
								+ " the dismount plays inside the wind-up, and you take off the moment you are on foot)"
							: " (OFF = old behavior: the hook just retracts, nothing happens)");
					break;
				}
				case "mountedwait":
				{
					float v = ParseF(args, at + 0, -1f);
					if (v < 0f) return $"grapple: mounted dismount wait = {GrappleLogic.MountedDismountTimeoutSeconds:F1}s (give up + retract if still on the horse by then)";
					GrappleLogic.MountedDismountTimeoutSeconds = v;
					return $"OK: mounted dismount wait = {v:F1}s";
				}

				// ── 骑马测试夹具（2026-10-13）：召马 / 让人上马 / 让人落马 ──
				case "horse":
					result = DoHorse(args, at);
					break;

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

				case "sound":
					result = DoSound(args, at);
					break;

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
		/// `custom.grapple sound [on|off]`（2026-10-10）——
		///   无参 = **原地试听一次**抛出绳声 + 打印状态（静音故障的第一诊断入口）；
		///   on/off = 绳声开关（默认开）。事件名在 `ModuleData/module_sounds.xml`（`lwn_grapple_rope`）。
		/// </summary>
		private static string DoSound(List<string> args, int at)
		{
			string a0 = ArgAt(args, at + 0);
			if (a0 != null && (a0.Equals("on", StringComparison.OrdinalIgnoreCase) || a0.Equals("off", StringComparison.OrdinalIgnoreCase)))
			{
				GrappleLogic.RopeSoundEnabled = a0.Equals("on", StringComparison.OrdinalIgnoreCase);
				return $"OK. rope sound {(GrappleLogic.RopeSoundEnabled ? "ON" : "OFF")} | {SoundFx.Describe(GrappleLogic.RopeSoundName)}";
			}

			// 无参（或认不出的占位）= 试听：无视开关直拉一次（显式动作，不是自动播放）
			Agent main = Agent.Main;
			SoundFx.Play3D(GrappleLogic.RopeSoundName, main != null ? main.GetEyeGlobalPosition() : Vec3.Zero);
			return $"OK. played rope sound | enabled={GrappleLogic.RopeSoundEnabled} | {SoundFx.Describe(GrappleLogic.RopeSoundName)}";
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

		/// <summary>
		/// 把玩家身上某件物品（按 StringId）从**所有武器槽**卸掉，返回卸掉的件数（`equip rope|hook` 用，2026-10-12）。
		/// 目的 = 让"只有绳 / 只有钩"这种现场**一条命令就能复现**（否则上一局装的另一件还挂在身上）。
		/// 引擎 API = `Agent.RemoveEquippedWeapon(EquipmentIndex)`（1.2.12 ~ 1.5.x 各锚点都有）。
		/// 找不到 = 0（正常，不是错误）；异常吞掉（它只是调试命令的一条捷径，不值得让命令报错）。
		/// </summary>
		private static int RemoveEquippedById(Agent agent, string itemId)
		{
			int removed = 0;
			if (agent == null) { return 0; }
			try
			{
				for (EquipmentIndex slot = EquipmentIndex.WeaponItemBeginSlot;
					slot < EquipmentIndex.NumAllWeaponSlots; slot++)
				{
					MissionWeapon w = agent.Equipment[slot];
					if (w.Item != null && w.Item.StringId == itemId)
					{
						agent.RemoveEquippedWeapon(slot);
						removed++;
					}
				}
			}
			catch (Exception)
			{
			}
			return removed;
		}

		/// <summary>
		/// 发钩索装备（`which` = `both`（默认）| `rope` | `hook`；2026-10-12 起带选装参数）。
		/// 场景里**选装是权威的**：`rope` = 配成"只有绳"（并卸掉已装备的钩）—— 一条命令复现"缺钩爪"现场。
		/// </summary>
		private static string DoEquip(string which = null)
		{
			string what = string.IsNullOrEmpty(which) ? "both" : which.Trim().ToLowerInvariant();
			bool wantRope = what == "both" || what == "rope";
			bool wantHook = what == "both" || what == "hook";
			if (!wantRope && !wantHook)
			{
				return "Error: equip takes 'both' (default) | 'rope' (rope only - the missing-hook scene) | 'hook' (got '" + which + "')";
			}

			// 🔴 物品 id 的**唯一真相**在 Combat/GrappleFirePatch.cs 的两个常量里（那里是拦截判据；
			//    该类的 namespace 是 `LivingWorldNpcs`，不是 `.Combat`）——
			//    这里引用它们，别再抄第二份字面量（2026-10-09 搬进 LWN 时顺手统一）。
			ItemObject rope = wantRope ? ResolveContentItem(GrappleFirePatch.RopeItemId, "GrappleRope") : null;
			ItemObject hook = wantHook ? ResolveContentItem(GrappleFirePatch.HookItemId, "GrappleHook") : null;
			if ((wantRope && rope == null) || (wantHook && hook == null))
			{
				return $"Error: grapple items not found (expected {GrappleFirePatch.RopeItemId} / {GrappleFirePatch.HookItemId} from this module's ModuleData/items/grapple.xml)";
			}

			// ① 场景里：直接装到玩家手上（当场可用）
			Agent main = Agent.Main;
			if (Mission.Current != null && main != null)
			{
				try
				{
					// 🔴 选装是**权威**的（2026-10-12）：`equip rope` = 把玩家配成"只有绳"（并卸掉已装备的钩）。
					//    否则"缺件现场"没法一条命令复现 —— 上一局装的钩还挂在身上，光发新的没用。
					//    **先卸后装**：顺带把槽腾出来（四个槽全满时也装得上）。
					var notes = new List<string>();
					if (!wantHook)
					{
						int dropped = RemoveEquippedById(main, GrappleFirePatch.HookItemId);
						if (dropped > 0) { notes.Add($"removed {dropped}x GrappleHook"); }
					}
					if (!wantRope)
					{
						int dropped = RemoveEquippedById(main, GrappleFirePatch.RopeItemId);
						if (dropped > 0) { notes.Add($"removed {dropped}x GrappleRope"); }
					}

					// 🔴 先给**绳**找槽 → 装上 → 再给**钩**找槽（顺序不能反：
					//    两件都在找空槽时若先一起算，会算出同一个槽）。
					if (wantRope)
					{
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
						notes.Add($"GrappleRope in slot {(int)ropeSlot}");
					}

					if (wantHook)
					{
						EquipmentIndex hookSlot = FindFreeWeaponSlotFor(main, hook);
						if (hookSlot == EquipmentIndex.None)
						{
							return wantRope
								? "Error (mission): GrappleRope is on, but no free weapon slot for GrappleHook (it occupies one ammo slot - same cost as vanilla bow + arrows)."
								: "Error (mission): no free weapon slot for GrappleHook (it occupies one ammo slot).";
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
						notes.Add($"GrappleHook x1 in slot {(int)hookSlot}");
					}

					main.UpdateAgentStats();
					string tail = wantHook
						? " - hold LMB to aim, release to fire."
						: " - NO HOOK: cannot fire (this is the missing-hook scene; the hand hook is hidden and pressing fire shows the hint).";
					return $"OK (mission): {string.Join(" + ", notes)}{tail}";
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
				var notes = new List<string>();
				int givenRope = 0, givenHook = 0;
				if (wantRope)
				{
					givenRope = AgentControlHelper.TransferItems(null, hero, rope, 1);
					notes.Add($"GrappleRope x{givenRope}");
				}
				if (wantHook)
				{
					givenHook = AgentControlHelper.TransferItems(null, hero, hook, 1);
					notes.Add($"GrappleHook x{givenHook}");
				}
				if ((!wantRope || givenRope <= 0) && (!wantHook || givenHook <= 0))
				{
					return "Error: could not add grapple items to the party inventory.";
				}
				string tail = (wantRope && wantHook)
					? " Equip BOTH (weapon slot + ammo slot), then enter a battle and fire."
					: (wantRope
						? " Equip the rope - without a hook the grapple cannot fire."
						: " The hook alone does nothing without the rope to hold.");
				return $"OK (campaign): added to party inventory - {string.Join(", ", notes)}.{tail} Run again for spares.";
			}
			catch (Exception ex)
			{
				return "Error: campaign give failed (" + ex.GetType().Name + ": " + ex.Message + ")";
			}
		}

		/// <summary>
		/// `custom.grapple hint` —— **强制弹一次"缺钩爪"提示**（2026-10-12 加）。
		/// 为什么要有它：这条提示的正常触发挂在"按开火键"上，验收时未必方便按键（身上有钩时也进不了那条分支）
		/// ⇒ 给一个"直接弹给我看"的入口（项目纪律：每个功能都要能单独验）。
		/// </summary>
		private static string DoHint(GrappleLogic logic)
		{
			if (logic == null)
			{
				return "Error: no GrappleLogic in this mission.";
			}
			logic.ShowNoHookHint();
			return "OK: popped the missing-hook hint. It normally appears when you hold the GrappleRope,"
				+ " have no GrappleHook equipped, and press the fire button (LMB / gamepad RT).";
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
		///   test [mounted]              对**最近的可捆目标**直接走整套（不走飞行；机制验收主入口）。
		///                               加 `mounted` = 只挑**骑马的**（验收"拉下马"那条路）。
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
		///   mounted 0|1                 骑马的目标照不照收（默认 on = **拉下马**，落地才捆）
		///   mountedblow 0|1             "拉下马"要不要**同时打一发 `CanDismount` blow**（默认 on）——
		///                               native 直接执行、**不看马速**（观感 = 被绳扯下来）；关掉只走下马事件（A/B 用）
		///   diswait &lt;秒&gt;               "拉下马"等他落地的超时（默认 3.0；超时 = 收钩收绳）
		///   ── 🔴 绳长上限：牵着走 / 拖在地上（2026-10-13）──
		///   leash 0|1                   总开关（关 = 绳随跨度自由伸缩的旧行为）
		///   leashlen &lt;下限&gt; [上限]      锁定绳长的取值范围（锁定值本身 = 绑上那一刻的"手→胸口"距离）
		///   leashstiff &lt;值&gt;            拽人的弹簧系数（每超出 1 米给多少速度；越大追得越急）
		///   leashspeed &lt;米/秒&gt;         拽人速度上限
		///   dragthr &lt;米/秒&gt;            绳主速度超过它 = 被牵的人**拖在地上**（= 倍率门槛 × 自然速率，两种写法之一）
		///   dragmounted 0|1             绳主骑马在动就拖在地上（默认 on）
		///   dragface 0|1                拖在地上时背朝行进方向（脚在前；默认 on）
		///   leashprobe &lt;秒&gt;            牵绳探针（距离 / 绳长 / 拽速），默认关
		///   leashtaut &lt;米&gt; [平滑率]     "绷紧"门槛（默认 0.05）—— 超过它 = 拽人 **且** 同步进走姿（一条闸，防滑步）
		///   ── 🔴 自适应播放倍率（2026-10-14）：被牵着走时按实际拽速调 `binded_walk` 的播放倍率（治脚滑）──
		///   walkrate &lt;米/秒&gt;           那条走路动画"原速时看着像每秒走几米"（默认 1.1；原地无位移轨，只能实机看）
		///   walkmax &lt;倍率&gt;             倍率上限（默认 2.0）—— **超过它就直接改"拖在地上"**（用户裁定）
		///   walkmin &lt;倍率&gt;             倍率下限（默认 0.55；再慢像放幻灯片）
		///   walkspeed &lt;倍率&gt;           手动锁死倍率（0 = 自适应，默认）
		///   walkspeedauto 0|1           连续调速那条路（每帧推骨架；默认 on。关掉 = 只在切状态时写一次，做 A/B）
		///   walkthr &lt;进&gt; [出]          走姿的进/出门槛（米/秒，默认 0.15 / 0.06）—— 判据是**他自己的实际速度**
		///   ── 🔴 反向绑：NPC 牵着玩家走（2026-10-13）──
		///   reverse                     最近的 NPC 把**玩家**绑了（玩家控制器会被收走、姿态归 bind.xml）
		///   lead [米] [run]             绳主（NPC）往前走到 N 米外（默认 6），牵着玩家跟上来
		///   leadstop                    绳主停下
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
					return logic.BindTest(FindNearestBindableAgent(30f,
						(ArgAt(args, at + 1) ?? "").ToLowerInvariant() == "mounted"));
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
				// ── 🔴 2026-10-10 新口径（步骤 6）的三个旋钮 ──
				//    `tug` = **【拉紧】**：把被捆住的人拽倒（命中不再自动拽倒 —— 那是用户拍板的新口径）
				case "tug":
					return logic.BindTug();
				case "tugmin":
				{
					float v = ParseF(args, at + 1, -1f);
					if (v < 0f) return $"grapple: tug min distance = {GrappleBind.TugMinDistance:F1}m (you must be FURTHER than this to pull the bound one off their feet; measured from the feet)";
					GrappleBind.TugMinDistance = v;
					return $"OK: tug min distance = {v:F1}m";
				}
				case "laytime":
				{
					float v = ParseF(args, at + 1, -1f);
					if (v < 0f) return $"grapple: bind laytime = {GrappleBind.LaySeconds:F1}s (after being tugged down, the target stands up by itself; 0 = never)";
					GrappleBind.LaySeconds = v;
					return v <= 0f
						? "OK: laytime = 0 (once tugged down they stay down until you release them)"
						: $"OK: laytime = {v:F1}s (auto stand-up after being tugged down)";
				}
				case "laystart":
				{
					float v = ParseF(args, at + 1, -1f);
					if (v < 0f) return $"grapple: bind laystart = {GrappleBind.LayStartProgress:F2} (start progress of the lay-down clip; 0 = play it from the beginning)";
					if (v < 0f || v > 1f) return "Error: bind laystart expects 0..1";
					GrappleBind.LayStartProgress = v;
					return $"OK: laystart = {v:F2}";
				}
				case "struggle":
				{
					string m = ArgAt(args, at + 1)?.ToLowerInvariant();
					if (m == "0" || m == "off") { if (logic.BindStruggleState) logic.BindToggleStruggle(); }
					else if (m == "1" || m == "on") { if (!logic.BindStruggleState) logic.BindToggleStruggle(); }
					else if (m == "toggle") { logic.BindToggleStruggle(); }
					else if (m != null && m != "state") { return "Error: bind struggle expects 0|1|toggle"; }
					return $"OK: struggling = {logic.BindStruggleState}"
						+ " (station bound <-> struggling; no automatic trigger yet -- manual switch)";
				}
				// ── 骑马的目标：拉下马（2026-10-13 立）──
				//    钩住骑手 = ① 先打一发 CanDismount blow（native 直接执行、不看马速）
				//               ② 同时反复点引擎自己的"下马"事件兜底（它按马速可能拒收）
				//    两条都在跑，谁先把他弄下来算谁的；落地再走"缠 → 倒 → 缚"。
				case "mounted":
				{
					string m = ArgAt(args, at + 1)?.ToLowerInvariant();
					if (m == "0" || m == "off") { GrappleBind.PullDownMounted = false; }
					else if (m == "1" || m == "on") { GrappleBind.PullDownMounted = true; }
					else if (m != null && m != "state") { return "Error: bind mounted expects 0|1"; }
					return $"OK: pull a mounted target off the horse = {GrappleBind.PullDownMounted}"
						+ " (on = yank the rider off, then bind them on foot once they land;"
						+ " off = old behavior, the hook just dangles on them)";
				}
				case "mountedblow":
				{
					string m = ArgAt(args, at + 1)?.ToLowerInvariant();
					if (m == "0" || m == "off") { GrappleBind.MountedBlow = false; }
					else if (m == "1" || m == "on") { GrappleBind.MountedBlow = true; }
					else if (m != null && m != "state") { return "Error: bind mountedblow expects 0|1"; }
					return $"OK: hand-made CanDismount blow when pulling a rider off = {GrappleBind.MountedBlow}"
						+ " (on = also fire a hand-made blow that the engine executes regardless of horse speed;"
						+ " off = engine's dismount event only -- use this for A/B)";
				}
				case "diswait":
				{
					float v = ParseF(args, at + 1, -1f);
					if (v < 0f) return $"grapple: dismount wait = {GrappleBind.DismountTimeoutSeconds:F1}s (give up + retract if the rider is not on foot by then)";
					GrappleBind.DismountTimeoutSeconds = v;
					return $"OK: dismount wait = {v:F1}s";
				}
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

				// ═════════ 绳长上限：牵着走 / 拖在地上（2026-10-13，用户第 1、2 条）═════════
				//
				// 机制：绑上那一刻把绳长锁死 ⇒ 走远绳不再变长、绳绷紧、**人被拽着跟上**；
				//       绳主骑马 / 跑得快 ⇒ 直接**拖在地上**（趴缚循环）。
				// 验收：绑住一个人（`bind test`）→ 走远 → 看他是不是被拽着跟上来；
				//       骑马跑起来 → 看他是不是被拖在地上。
				case "leash":
				{
					string m = ArgAt(args, at + 1)?.ToLowerInvariant();
					if (m == "0" || m == "off") { GrappleBind.LeashEnabled = false; }
					else if (m == "1" || m == "on") { GrappleBind.LeashEnabled = true; }
					else if (m != null && m != "state") { return "Error: bind leash expects 0|1"; }
					return $"OK: rope length cap (leash) = {GrappleBind.LeashEnabled}"
						+ (GrappleBind.LeashEnabled
							? " (the rope length is locked when the bind starts; walk beyond it and the bound one is pulled along;"
								+ " riding a moving horse = dragged on the ground; on foot they always keep up on their feet)"
							: " (OFF: the rope stretches freely again -- the bound one stays where they are)");
				}
				case "leashlen":
				{
					float lo = ParseF(args, at + 1, -1f);
					if (lo < 0f) return $"grapple: leash floor = {GrappleBind.LeashMinLength:F1} m | rope visual cap = {GrappleBind.LeashMaxLength:F1} m"
						+ " (the lock itself = the hand-to-chest distance when the bind starts x slack; the cap only limits how long the rope is DRAWN)";
					GrappleBind.LeashMinLength = lo;
					float hi = ParseF(args, at + 2, float.NaN);
					if (!float.IsNaN(hi)) GrappleBind.LeashMaxLength = hi;
					return $"OK: leash floor = {GrappleBind.LeashMinLength:F1} m | rope visual cap = {GrappleBind.LeashMaxLength:F1} m";
				}
				case "leashstiff":
				{
					float v = ParseF(args, at + 1, -1f);
					if (v < 0f) return $"grapple: leash stiffness = {GrappleBind.LeashStiffness:F1} (extra m/s per metre beyond the rope length)";
					GrappleBind.LeashStiffness = v;
					return $"OK: leash stiffness = {v:F1} (higher = the bound one catches up harder)";
				}
				case "leashspeed":
				{
					float v = ParseF(args, at + 1, -1f);
					if (v < 0f) return $"grapple: leash max pull speed = {GrappleBind.LeashMaxSpeed:F1} m/s";
					GrappleBind.LeashMaxSpeed = v;
					return $"OK: leash max pull speed = {v:F1} m/s";
				}
				case "dragthr":
				{
					float v = ParseF(args, at + 1, -1f);
					if (v < 0f) return $"grapple: bound-walk rate cap = {GrappleBind.DragSpeedThreshold:F2} m/s"
						+ $" (= mult {GrappleBind.WalkMaxMult:F2} x natural {GrappleBind.WalkNaturalSpeed:F2}; how fast the walk clip is allowed to play"
						+ "; NOTE: on foot they are NEVER knocked down any more -- only riding a moving horse or your own 'bind tug' does that)";
					if (v <= 0f || GrappleBind.WalkNaturalSpeed <= 0.05f) return "Error: dragthr needs a positive m/s value";
					GrappleBind.WalkMaxMult = v / GrappleBind.WalkNaturalSpeed;
					return $"OK: bound-walk rate cap = {GrappleBind.DragSpeedThreshold:F2} m/s (mult {GrappleBind.WalkMaxMult:F2})";
				}
				// ── 🔴 自适应播放倍率（2026-10-14）：`binded_walk` 按实际拽速调速，脚才不滑 ──
				case "walkrate":
				{
					float v = ParseF(args, at + 1, -1f);
					if (v < 0f) return $"grapple: bound-walk natural speed = {GrappleBind.WalkNaturalSpeed:F2} m/s"
						+ $" (the clip is in-place, so this is measured by eye: at this drag speed the playback rate is 1x; the rate cap becomes {GrappleBind.DragSpeedThreshold:F2} m/s)";
					if (v <= 0.05f) return "Error: walkrate must be > 0.05 m/s";
					GrappleBind.WalkNaturalSpeed = v;
					return $"OK: bound-walk natural speed = {v:F2} m/s (rate 1x at this speed; rate cap is now {GrappleBind.DragSpeedThreshold:F2} m/s)";
				}
				case "walkmax":
				{
					float v = ParseF(args, at + 1, -1f);
					if (v < 0f) return $"grapple: walk-rate cap = {GrappleBind.WalkMaxMult:F2}x (how fast the bound-walk clip may play = {GrappleBind.DragSpeedThreshold:F2} m/s at natural rate {GrappleBind.WalkNaturalSpeed:F2})";
					if (v <= 0.05f) return "Error: walkmax must be > 0.05";
					GrappleBind.WalkMaxMult = v;
					return $"OK: walk-rate cap = {v:F2}x (= {GrappleBind.DragSpeedThreshold:F2} m/s of drag speed; beyond it the legs just slide, they do NOT fall down)";
				}
				case "walkmin":
				{
					float v = ParseF(args, at + 1, -1f);
					if (v < 0f) return $"grapple: walk-rate floor = {GrappleBind.WalkMinMult:F2}x (never play the bound walk slower than this)";
					if (v <= 0.05f) return "Error: walkmin must be > 0.05";
					GrappleBind.WalkMinMult = v;
					return $"OK: walk-rate floor = {v:F2}x";
				}
				case "walkspeed":
				{
					float v = ParseF(args, at + 1, -1f);
					if (v < 0f) return $"grapple: walk-rate override = {(GrappleBind.WalkSpeedOverride > 0f ? $"{GrappleBind.WalkSpeedOverride:F2}x (LOCKED)" : "auto (follows the drag speed)")}";
					GrappleBind.WalkSpeedOverride = v;
					return v <= 0f
						? "OK: walk-rate = AUTO (follows the actual drag speed; cap "
							+ $"{GrappleBind.WalkMaxMult:F2}x, floor {GrappleBind.WalkMinMult:F2}x)"
						: $"OK: walk-rate LOCKED at {v:F2}x (use 'walkspeed 0' to go back to auto)";
				}
				// ── 🔴 动画那条线（只看他自己的速度）：走姿的进/出门槛 ──
				case "walkthr":
				{
					float a = ParseF(args, at + 1, -1f);
					if (a < 0f) return $"grapple: bound-walk speed gates -- enter at {GrappleBind.LeashWalkEnterSpeed:F2} m/s, exit at {GrappleBind.LeashWalkExitSpeed:F2} m/s"
						+ " (his OWN speed, measured from position delta; below the enter gate he plays the bound idle, above he walks)";
					if (a <= 0f) return "Error: walkthr expects a positive m/s value";
					GrappleBind.LeashWalkEnterSpeed = a;
					float b = ParseF(args, at + 2, float.NaN);
					if (!float.IsNaN(b)) GrappleBind.LeashWalkExitSpeed = b;
					return $"OK: bound-walk speed gates = enter {GrappleBind.LeashWalkEnterSpeed:F2} m/s / exit {GrappleBind.LeashWalkExitSpeed:F2} m/s";
				}
				case "walkspeedauto":
				{
					string m = ArgAt(args, at + 1)?.ToLowerInvariant();
					if (m == "0" || m == "off") { GrappleBind.WalkSpeedAuto = false; }
					else if (m == "1" || m == "on") { GrappleBind.WalkSpeedAuto = true; }
					else if (m != null && m != "state") { return "Error: bind walkspeedauto expects 0|1"; }
					return $"OK: per-frame rate push = {GrappleBind.WalkSpeedAuto}"
						+ (GrappleBind.WalkSpeedAuto
							? " (the rate is written to the skeleton every frame: continuous adaptation)"
							: " (OFF: the rate is only written once when the bound-walk state starts -- A/B for 'does the rate work at all')");
				}
				case "dragmounted":
				{
					string m = ArgAt(args, at + 1)?.ToLowerInvariant();
					if (m == "0" || m == "off") { GrappleBind.DragWhenMounted = false; }
					else if (m == "1" || m == "on") { GrappleBind.DragWhenMounted = true; }
					else if (m != null && m != "state") { return "Error: bind dragmounted expects 0|1"; }
					return $"OK: riding a moving horse drags the bound one on the ground = {GrappleBind.DragWhenMounted}"
						+ " (this is now the ONLY automatic way to put them on the ground; the other one is your own 'bind tug')";
				}
				case "dragface":
				{
					string m = ArgAt(args, at + 1)?.ToLowerInvariant();
					if (m == "0" || m == "off") { GrappleBind.DragFaceAway = false; }
					else if (m == "1" || m == "on") { GrappleBind.DragFaceAway = true; }
					else if (m != null && m != "state") { return "Error: bind dragface expects 0|1"; }
					return $"OK: dragged one faces away from the pull = {GrappleBind.DragFaceAway}"
						+ (GrappleBind.DragFaceAway ? " (feet first, head away from the rope holder)" : " (head toward the rope holder -- default)");
				}
				// ── 🔴 "绷紧"两个门槛（2026-10-14 加）：档一 = 身体（绳的硬约束）/ 档二 = 走姿读感 ──
				case "leashtaut":
				{
					float a = ParseF(args, at + 1, -1f);
					if (a < 0f) return $"grapple: tautness gate = {GrappleBind.LeashDeadzone:F2} m"
						+ " (beyond this the bound one is pulled AND starts walking -- one gate for both, so he never slides in idle)"
						+ $" | smoothing = {GrappleBind.LeashSmoothSpeed:F0}/s (measured on root-to-root distance growth, not hand-to-chest)";
					if (a <= 0f || a > 3f) return "Error: leashtaut expects 0..3 (metres over the rope length)";
					GrappleBind.LeashDeadzone = a;
					float b = ParseF(args, at + 2, float.NaN);
					if (!float.IsNaN(b)) GrappleBind.LeashSmoothSpeed = b;
					return $"OK: tautness gate = {GrappleBind.LeashDeadzone:F2} m (exit at {GrappleBind.LeashDeadzone * GrappleBind.LeashReleaseRatio:F2} m) | smoothing {GrappleBind.LeashSmoothSpeed:F0}/s";
				}
				case "leashprobe":
				{
					float v = ParseF(args, at + 1, -1f);
					if (v < 0f) return $"grapple: leash probe interval = {GrappleBind.LeashProbeSeconds:F1}s (0 = off; logs distance / rope length / pull speed)";
					GrappleBind.LeashProbeSeconds = v;
					return v <= 0f ? "OK: leash probe OFF" : $"OK: leash probe every {v:F1}s";
				}

				// ═════════ 反向绑：NPC 牵着玩家走（2026-10-13，用户第 3 条）═════════
				case "reverse":
					return logic.BindReverse(FindNearestBindableAgent(30f));
				case "lead":
				{
					float m = ParseF(args, at + 1, float.NaN);
					string runArg = ArgAt(args, at + 2)?.ToLowerInvariant();
					bool run = runArg == "run" || runArg == "1" || runArg == "on";
					return logic.BindLead(float.IsNaN(m) ? 0f : m, run);
				}
				case "leadstop":
					return logic.BindLeadStop();
			}
			return "Error: bind expects on|off|test|reverse|release|probe|state|time|drag|wrap|yankhold|yankpull|fallwait|tug|tugmin|laytime|laystart|struggle|yank|blowforce|blowalt|anim|panim|aipause|mounted|mountedblow|diswait|leash|leashlen|leashstiff|leashspeed|dragthr|dragmounted|dragface|leashprobe|leashtaut|walkrate|walkmax|walkmin|walkspeed|walkspeedauto|walkthr|lead|leadstop";
		}

		/// <summary>
		/// **`custom.grapple carry …`**（2026-10-10，步骤 6「扛人」；方案 = plans\钩索-实施计划.md §十五）。
		///
		/// 把**当前被钩索捆住**的那个人扛到肩上 / 放下来。
		/// 🔴 **竖直那 0.68 米不在代码里** —— 在被扛者自己的动画里（根骨位置轨）。
		///    本族只负责"XZ 贴住扛人者 + 朝向对齐"，见 <see cref="GrappleCarry"/> 的类注释。
		///
		/// 子命令：
		///   on | off                    总开关（关 = 面板不出现【扛起】、命令直接拒）
		///   start                       把当前被捆的人扛起来（默认扛人者 = 玩家）
		///   put                         放下（两边同帧起播"放下/被放下"，演完才松手）
		///   ── 🔴 马背载人（2026-10-14）──
		///   horse                       把扛着的人**放到附近那匹马的背上**（之后他跟着马走；⚠️ 悬空高度等新动画）
		///   horseoff                    从马背拿下（落地回站缚，扛人关系结束）
		///   horserange                   （已退役：不再有独立半径 —— 用与面板同一套焦点检测）
		///   horseoffset &lt;后偏米&gt; [侧偏米]   放上去的位置（默认 0.55 / 0）
		///   horseface along|across|reverse   他在马背上的朝向（默认 along = 头朝马头）
		///   horseprobe &lt;秒&gt;            马背朝向探针（马朝向 / 写入 / 回读 / 夹角），默认关
		///   state                       状态一行（扛人者状态机 / 被扛者动作 / 距离 / 在不在动 / 偏移）
		///   offset &lt;前后&gt; [左右]     摆位微调（米；默认 0 —— 对齐量本来就烘在被扛者的动画里）
		///   maxdist &lt;米&gt;            起扛的最大距离（默认 3.0）
		/// </summary>
		private static string DoCarry(GrappleLogic logic, List<string> args, int at)
		{
			string what = (ArgAt(args, at + 0) ?? "state").ToLowerInvariant();
			switch (what)
			{
				case "on":
					return logic.CarrySetEnabled(true);
				case "off":
					return logic.CarrySetEnabled(false);
				case "state":
					return logic.CarryStateLine();
				case "start":
				{
					if (!logic.CarryPickUp(out string why))
						return $"Error: cannot pick up ({why}).";
					return "OK: picking the bound target up (both sides start their carry clip on the same frame)";
				}
				// ── 🔴 马背载人（2026-10-14；用户："扛起之后瞄着马可以放到马背上，玩家骑马带着他跑"）──
			case "horse":
				// 与面板**同一套**：取焦点那一匹（射线 + 身边 4 m 锥形；被扛的人已从焦点过滤）
				return logic.CarryPlaceOnHorse(null);
			case "horseoff":
				return logic.CarryTakeOffHorse();
			case "horseoffset":
			{
				float back = ParseF(args, at + 1, float.NaN);
				if (float.IsNaN(back)) return $"grapple: horse-back offset = {GrappleCarry.HorseBackOffset:F2} m back / {GrappleCarry.HorseSideOffset:F2} m side (face={GrappleCarry.HorseFaceMode})";
				GrappleCarry.HorseBackOffset = back;
				float side = ParseF(args, at + 2, float.NaN);
				if (!float.IsNaN(side)) GrappleCarry.HorseSideOffset = side;
				return $"OK: horse-back offset = {GrappleCarry.HorseBackOffset:F2} m back / {GrappleCarry.HorseSideOffset:F2} m side";
			}
			case "horserange":
				return "OK: no separate horse range any more -- 【放上马背】uses the SAME focus detection as everything else"
					+ " (ray + 4 m cone, i.e. the thing under/near your crosshair). Just look at the horse.";
			case "horseprobe":
			{
				float v = ParseF(args, at + 1, -1f);
				if (v < 0f) return $"grapple: horse probe = {GrappleCarry.HorseProbeSeconds:F1}s (0 = off; logs horse facing / written facing / read-back facing / angle)";
				GrappleCarry.HorseProbeSeconds = v;
				return v <= 0f ? "OK: horse probe OFF"
					: $"OK: horse probe every {v:F1}s (ride around and read '[Grapple] 马背探针' in the log)";
			}
			case "horseface":
			{
				string m = ArgAt(args, at + 1)?.ToLowerInvariant();
				if (m == "along" || m == "across" || m == "reverse") GrappleCarry.HorseFaceMode = m;
				else if (m != null && m != "state") return "Error: carry horseface expects along|across|reverse";
				return $"OK: on the horse the body faces = {GrappleCarry.HorseFaceMode}"
					+ " (along = head to the horse's head / across = side-on over the back / reverse = head to the tail)";
			}
			case "put":
				{
					if (!logic.CarryPutDown(out string why))
						return $"Error: cannot put down ({why}).";
					return "OK: putting them down (both sides play the put-down clip; we let go when it is done)";
				}
				case "offset":
				{
					float fwd = ParseF(args, at + 1, float.NaN);
					float right = ParseF(args, at + 2, float.NaN);
					if (float.IsNaN(fwd)) return $"grapple: carry offset = {GrappleCarry.OffsetForward:F2} forward / {GrappleCarry.OffsetRight:F2} right (0/0 = use the alignment baked in the carried animation)";
					return logic.CarrySetOffset(fwd, float.IsNaN(right) ? GrappleCarry.OffsetRight : right);
				}
				// ── Z 三件（2026-10-10 用户："扛起之后每帧写 xy，z 也尝试一下"）──
				case "z":
				{
					string m = ArgAt(args, at + 1)?.ToLowerInvariant();
					if (m == "0" || m == "off") { GrappleCarry.WriteZ = false; }
					else if (m == "1" || m == "on") { GrappleCarry.WriteZ = true; }
					else if (m != null && m != "state") { return "Error: carry z expects 0|1"; }
					return $"OK: write carrier Z to the carried agent = {GrappleCarry.WriteZ}"
						+ " (the engine usually re-derives Z from the terrain; watch the '[Grapple] Z probe' log to see which one happens)";
				}
				case "zlift":
				{
					float v = ParseF(args, at + 1, float.NaN);
					if (float.IsNaN(v)) return $"grapple: carry zlift = {GrappleCarry.ZLift:F2}m (added on top of the carrier's Z)";
					GrappleCarry.ZLift = v;
					return $"OK: carry zlift = {v:F2}m";
				}
				case "zprobe":
				{
					float v = ParseF(args, at + 1, float.NaN);
					if (float.IsNaN(v)) return $"grapple: carry zprobe = {GrappleCarry.ZProbeSeconds:F2}s (0 = off)";
					GrappleCarry.ZProbeSeconds = v;
					return v <= 0f ? "OK: carry Z probe off" : $"OK: carry Z probe every {v:F2}s (log tag '[Grapple] 扛人 Z 探针')";
				}
				case "maxdist":
				{
					float v = ParseF(args, at + 1, -1f);
					if (v < 0f) return $"grapple: carry maxdist = {GrappleCarry.MaxLiftDistance:F1}m (measured from the feet)";
					GrappleCarry.MaxLiftDistance = v;
					return $"OK: carry maxdist = {v:F1}m";
				}
				// ── 起扛时收武器（2026-10-13 用户要的："扛起来就别再端着弓、别挂准星"）──
				case "sheathe":
				{
					string m = ArgAt(args, at + 1)?.ToLowerInvariant();
					if (m == "0" || m == "off") { GrappleCarry.SheatheOnCarry = false; }
					else if (m == "1" || m == "on") { GrappleCarry.SheatheOnCarry = true; }
					else if (m != null && m != "state") { return "Error: carry sheathe expects 0|1"; }
					return $"OK: sheathe while carrying = {GrappleCarry.SheatheOnCarry}"
						+ " (on = the weapon is stowed the moment you pick someone up -> no crosshair, no archer idle;"
						+ " it is handed back to you when the carry ends, unless you drew something yourself)";
				}
			}
			return "Error: carry expects on|off|start|put|state|offset|maxdist|sheathe|z|zlift|zprobe";
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
		// ═════════════════════ 骑马测试夹具（2026-10-13 立）═════════════════════
		//
		// 目的 = **验收"骑马"那两条链**（`bind mounted` 拉下马 · `mountedpull` 马上勾墙自动下马）——
		// 不然每次都要真去战场上找一个敌骑才能试。
		//
		//   custom.grapple horse                      状态行：谁骑着什么 / 身边几匹空马
		//   custom.grapple horse spawn [数量] [物品id]  在**面前 3 米**刷马（站着不走 —— 自动清掉 CanWander）
		//   custom.grapple horse mount [#编号|名字|me]  让一个 NPC（或 `me` = 你自己）上最近那匹空马
		//   custom.grapple horse dismount [#编号|名字]  让骑着的那个落马（**引擎下马事件** = 我们"拉下马"用的同一条）
		//   custom.grapple horse down [#编号|名字] [blow|rear|kill]  **被动落马**三条路（blow = 掀人 blow / rear = 让马扬蹄 / kill = 杀马）
		//
		// 🔴 **mount / dismount 都是异步的**（引擎要演一段上下马动画）⇒ 判据 = 隔一秒再敲一次不带参数的
		//    `custom.grapple horse`，看状态行变了没有；`down` 另有一行结论落在运行时日志里
		//    （`[GrappleHorse] DownWatch request finished: watch done: …`）。
		//
		// 🔴 **命令别名口径（2026-10-13 改）**：`fall` / `unhorse` 从"下马"改挂到 **`down`（被动落马）** 上 ——
		//    "被掀下马"才是这两个词的意思；主动下马一律敲 `dismount`。
		//
		// 🔴 **`walkoff` 探针已撤**（2026-10-13，用户裁定"多此一举"）：它只是拿 `AIScriptedFrameFlags.GoWithoutMount`
		//    （官方马上比武"别骑着过去"那一处的旗标）做的语义探针；"下马 + 移动"我们本来就有两条命令，用不着它。
		//    旗标本身的知识保留在 Knowledge 骑乘文档 §5.5（哪天要"骑着到地方再自己下马"再回来查）。
		//
		// 🔴 测试夹具的两处"开挂"（回执里都会注明，别当成正式行为）：
		//   ① 目标缺 `AgentFlag.CanRide` 就补上（村民本来一辈子不上马）
		//   ② 把那匹马的 `MountDifficulty` 压到 0（引擎的 `CheckSkillForMounting` 要求骑术 ≥ 难度）
		//   两处都只作用在**这一次调用**涉及的 agent 上，不碰任何持久数据、也不改世界状态。
		//
		// 🔴 刷马的配方抄的是**官方** `SandBoxHelpers.MissionHelper.SpawnHorses`：
		//   `Mission.SpawnMonster(new ItemRosterElement(马物品, 1, null), default, pos, dir, -1)`
		//   —— 马是**物品**驱动的（`ItemObject.HorseComponent`，`IsMount` = 能骑且不是驮畜）。
		//   物品查找走铁律 5 两轮：先常见原版 id，再遍历内存里所有已注册物品兜底。

		private static string DoHorse(List<string> args, int at)
		{
			string what = (ArgAt(args, at + 0) ?? "state").ToLowerInvariant();
			switch (what)
			{
				case "spawn": return HorseSpawn(args, at);
				case "mount": case "ride": return HorseMount(args, at);
				case "dismount": return HorseDismount(args, at);
				case "down": case "fall": case "unhorse": return HorseDown(args, at);
				case "rider": return HorseSpawnRider(args, at);
				case "why": return HorseWhy(args, at);
				case "state": return HorseState();
			}
			return "Error: horse expects spawn|mount|dismount|down|rider|why|state";
		}

		/// <summary>`custom.grapple horse spawn [数量] [物品id]` —— 在玩家面前刷马（站着不走）。
		/// 不给物品 id 时：先试常见原版骑乘马，再遍历内存兜底（见 <see cref="ResolveHorseItem"/>）。</summary>
		private static string HorseSpawn(List<string> args, int at)
		{
			Mission mission = Mission.Current;
			Agent main = Agent.Main;
			if (mission == null || main == null) return "Error: not in mission.";
			Scene scene = mission.Scene;
			if (scene == null) return "Error: no scene.";

			// 可选：点名要哪种马（按物品 StringId 精确查；查不到就报错 —— 不静默换一匹）
			string wantId = ArgAt(args, at + 2);
			ItemObject item;
			string how;
			if (!string.IsNullOrEmpty(wantId))
			{
				item = null;
				try { item = MBObjectManager.Instance.GetObject<ItemObject>(wantId); } catch (Exception) { }
				if (item == null || !item.HasHorseComponent)
					return $"Error: no horse item with id '{wantId}' (check the id; 'saddle_horse' / 'empire_horse' are vanilla ones).";
				if (!item.HorseComponent.IsMount)
					return $"Error: '{wantId}' is not rideable (pack animal / not a mount).";
				how = "by given id";
			}
			else
			{
				item = ResolveHorseItem(out how);
				if (item == null) return "Error: no rideable horse item found in the loaded modules.";
			}

			int count = (int)ParseF(args, at + 1, 1f);
			if (count < 1) count = 1;
			if (count > 6) count = 6;

			Vec3 fwd = main.LookDirection;                 // Vec3（1.2.12 与 1.5.x 同签名）
			fwd.z = 0f;
			if (fwd.LengthSquared < 1e-4f) fwd = Vec3.Forward;
			fwd = fwd.NormalizedCopy();
			Vec3 right = new Vec3(fwd.y, -fwd.x, 0f);      // 右手侧（同 GrappleCarry 那条口径）
			Vec2 dir = fwd.AsVec2;

			string made = "";
			for (int i = 0; i < count; i++)
			{
				// 面前 3 米；刷多匹时左右错开，免得叠在一起
				float side = (i - (count - 1) * 0.5f) * 1.6f;
				Vec3 pos = main.Position + fwd * 3.0f + right * side;
				float gz = V.GroundHeight(scene, pos, BodyFlags.CommonCollisionExcludeFlags);
				pos.z = (float.IsNaN(gz) || Math.Abs(gz) > 1e5f) ? main.Position.z : gz + 0.05f;

				Agent horse;
				try
				{
					horse = mission.SpawnMonster(new ItemRosterElement(item, 1, null),
						default(ItemRosterElement), pos, dir, -1);
				}
				catch (Exception ex)
				{
					return $"Error: SpawnMonster threw ({ex.GetType().Name}: {ex.Message}).";
				}
				if (horse == null) return "Error: SpawnMonster returned null (is that item a rideable mount?).";

				// 🔴 **站着不走**：官方 `AnimalSpawnSettings.CheckAndSetAnimalAgentFlags` 干的就是这件事
				//    （把 `CanWander` 清掉）—— 不清的话马会自己溜达走，下一步"上马"就够不着了。
				try { horse.SetAgentFlags(horse.GetAgentFlags() & ~AgentFlag.CanWander); }
				catch (Exception) { }

				made += (made.Length > 0 ? ", " : "") + $"#{horse.Index}";
				SpawnedHorseIndices.Add(horse.Index);      // 让 `horse mount` 优先挑它（见 FindSpawnedRiderlessMount）
			}

			return $"OK: spawned {count} horse(s) [item '{item.StringId}', {how}] 3m in front of you -> {made}"
				+ " (wandering disabled so they stay put). Next: custom.grapple horse mount";
		}

		/// <summary>`custom.grapple horse mount [#编号|名字|me]` —— 让目标上最近那匹空马。</summary>
		private static string HorseMount(List<string> args, int at)
		{
			if (Mission.Current == null) return "Error: not in mission.";

			Agent npc = ResolveHorseTarget(args, at + 1, false, out string note);
			if (npc == null) return "Error: no NPC found." + note;
			if (npc.MountAgent != null)
				return $"Error: {AgentName(npc)} is already on a horse (use 'custom.grapple horse dismount' first).";

			// 🔴 **优先用自己刚刷的那匹**（2026-10-13 加）：镇上本来就有马时，"最近的一匹"可能不是你刚刷的。
			//    记的是 Agent.Index（agent 每局重建，索引会变 ⇒ 取到的一定是当下这一匹，或者取不到）。
			//    没刷过 = 这一步跳过 ⇒ 直接落到"最近的、能骑的、空着的"（**玩家自己那匹马也算**，
			//    前提是玩家先下来 —— 还骑着的马有主，会被跳过）。
			Agent horse = FindSpawnedRiderlessMount(npc.Position, 60f) ?? FindNearestRiderlessMount(npc.Position, 60f);
			if (horse == null)
			{
				bool mainMounted = false;
				try { mainMounted = Agent.Main != null && Agent.Main.MountAgent != null; } catch (Exception) { }
				return "Error: no rideable riderless horse nearby (60m)."
					+ (mainMounted
						? " NOTE: you are still ON your own horse -- a ridden horse is skipped, dismount first and re-run this."
						: " (spawn one with: custom.grapple horse spawn)")
					+ note;
			}

			// ── 夹具开挂 ①：补 CanRide（村民本来一辈子不上马）──
			bool grantedRide = false;
			try
			{
				if ((npc.GetAgentFlags() & AgentFlag.CanRide) == 0)
				{
					npc.SetAgentFlags(npc.GetAgentFlags() | AgentFlag.CanRide);
					grantedRide = true;
				}
			}
			catch (Exception) { }

			// ── 夹具开挂 ②：马的难度压到 0（`CheckSkillForMounting` 要求 骑术 ≥ MountDifficulty）──
			// 🔴 注意 API 落点：`MountDifficulty` 是 `AgentDrivenProperties` 上的属性，**不在 Agent 上**
			//    （`Agent` 侧只有这两个：读 `GetAgentDrivenPropertyValue` / 写 `SetAgentDrivenPropertyValueFromConsole`
			//    —— 后者是引擎自己的控制台写入口，也是唯一的公开 setter；1.2.12 / 1.5.x 同签名）。
			float oldDiff = float.NaN;
			try
			{
				oldDiff = horse.GetAgentDrivenPropertyValue(DrivenProperty.MountDifficulty);
				horse.SetAgentDrivenPropertyValueFromConsole(DrivenProperty.MountDifficulty, 0f);
			}
			catch (Exception) { }

			// 引擎的挂载交互**由 native 管距离**（managed 侧只有常量 `Agent.MaxMountInteractionDistance` = 1.75 m，
			// 谁在用读不到）⇒ 太远就先**挪到马身侧**。
			// 🔴 这是**测试夹具的捷径**：真实流程是 AI 自己走过去（那要驱动寻路 + 等，验收太慢），
			//    这里直接 `TeleportToPosition`（画面上就是"人闪一下站到马旁"）。
			float dist = 0f;
			bool moved = false;
			try
			{
				dist = npc.Position.Distance(horse.Position);
				if (dist > 1.6f)
				{
					Vec3 hv = horse.LookDirection; hv.z = 0f;
					if (hv.LengthSquared < 1e-4f) hv = Vec3.Forward;
					hv = hv.NormalizedCopy();
					Vec3 side = new Vec3(hv.y, -hv.x, 0f);       // 马身侧
					npc.TeleportToPosition(horse.Position + side * 1.2f);
					moved = true;
				}
			}
			catch (Exception) { }

			string act = CurrentClipOf(npc);

			// 🔴🔴 **第一步：把 AI 控制器摘掉**（2026-10-13，用户给的线索 + 反编译印证）——
			//    `Agent.EventControlFlags` 每帧由 AI 重写（`Agent.Tick` 里 `if (AllowAiTicking && IsAIControlled) TickAsAI()`），
			//    我们外部 `|= Mount` 的位**活不过一帧**，native 看不到 ⇒ 必须先 `Controller = None`。
			//    `V.SetAgentControllerNone` 是现成的版本兼容包装（飞行 spike 立的）。
			//    玩家自己不走这条（他是 Player 控制器，按 E 就行，摘了反而失控）。
			bool wasAI = false;
			bool controllerOff = false;
			if (npc != Agent.Main)
			{
				try
				{
					wasAI = npc.IsAIControlled;
					if (wasAI) { V.SetAgentControllerNone(npc); controllerOff = true; }
				}
				catch (Exception) { }
			}

			// ① 引擎的"点一下"入口（**玩家**按 E 上马走的就是这条：`Mission.OnAgentInteraction` → `Agent.Mount`）。
			npc.Mount(horse);

			bool accepted = false;
			try { accepted = (npc.EventControlFlags & Agent.EventControlFlag.Mount) != 0; }
			catch (Exception) { }

			// ② 🔴 **NPC 真正能上马的那条路 = 引擎自己的 AI 坐骑令**（2026-10-13 查明）：
			//    托管层**只有三处**会写 `EventControlFlag`（`Run` / `Mount` / `Dismount`，都在 `Agent.Mount()` 里），
			//    **AI 从来不写** ⇒ 我们外部点的 Mount 旗标对 AI agent **不生效**（实机症状：旗标读回来是 True，
			//    但什么都没发生）。引擎给 AI 的入口是 **`SetRidingOrder(Mount)` + `SetSelectedMountIndex`**：
			//    `HumanAIComponent.OnTick` 每 **2~3 秒**（`MissionTimer(2f + 随机)`）查一次
			//    「`GetRidingOrder() == 1` 且没马且没在撤退」→ 从 `Mission.Current.MountsWithoutRiders` 里挑
			//    最近的、过得了 `CanAgentRideMount`（= `CheckSkillForMounting`）的马 → `ReserveMount` → 走过去 → 上马。
			//    🔴 引擎 `ReserveMount` 内部只干两件事（反编译）：`Agent.SetSelectedMountIndex(mount.Index)`
			//       + `mount.CommonAIComponent.OnMountReserved(Agent.Index)`（后者是 internal，我们写不了，
			//       它只是"别被别人抢"的标记）⇒ **骑手侧那一半我们自己设**。
			bool orderSet = false;
			int orderNow = -1, selNow = -1;
			try
			{
				if (npc != Agent.Main)
				{
					npc.SetRidingOrder(RidingOrder.RidingOrderEnum.Mount);
					npc.SetSelectedMountIndex(horse.Index);
					orderSet = true;
					orderNow = npc.GetRidingOrder();
					selNow = npc.GetSelectedMountIndex();
				}
			}
			catch (Exception) { }

			// 🔴 **把请求按住 6 秒**（**每帧重发旗标**）+ 收尾时把控制权还回去（见 `GrappleLogic.TickMountRequest`）。
			//    🔴 2026-10-13 调研修正：**玩家也要按住** —— 玩家侧的事件旗标会被输入层**每帧清零重建**
			//    （`MissionMainAgentController` 先 `EventControlFlags = 0` 再按输入重建）⇒ 只点一次 = 下一帧就没了。
			//    NPC 侧重发同样幂等无害（同一位反复置 1）。
			try { GrappleLogic.Ensure()?.RequestMount(npc, horse, 6f, wasAI); }
			catch (Exception) { }

			// 往运行时日志再落一行（回执本身也会被 `[Cmd]` 记，但那行太长不好搜）——
			// 下次"没反应"先看这行：它把**选中了哪匹马、什么物品、距离、动作、两条路各自的回执**一次打全。
			DebugLogger.Log($"[GrappleHorse] mount: npc={AgentName(npc)}#{npc.Index} -> horse #{horse.Index}"
				+ $" item='{HorseItemIdOf(horse)}' monster={MonsterIdOf(horse)}"
				+ $" dist={dist:F1}m moved={moved} clip-before='{act}'"
				+ $" controllerOff={controllerOff} eventFlagAccepted={accepted} ridingOrderSet={orderSet}"
				+ $" ridingOrderNow={orderNow} selectedMountNow={selNow}"
				+ $" canRide={(grantedRide ? "granted" : "already")} difficulty={oldDiff:F0}->0");
			// 🔴 **顺手把整条 AI 链的读数也落一条**（省一轮"再敲 horse why"）：
			//    `why:` 那行里每一格就是引擎 `CommonAIComponent` 找马流程上的一道门。
			DebugLogger.Log("[GrappleHorse] " + HorseWhyLine(npc));

			return $"OK: ordered {AgentName(npc)} onto horse #{horse.Index}"
				+ $" (item '{HorseItemIdOf(horse)}', monster {MonsterIdOf(horse)})"
				+ $" | dist={dist:F1}m" + (moved ? " (too far -> teleported to its flank)" : " (already close enough)")
				+ $" clip-before={act}"
				+ (grantedRide ? " | [test helper] granted AgentFlag.CanRide" : "")
				+ (float.IsNaN(oldDiff) ? "" : $" | [test helper] mount difficulty {oldDiff:F0}->0")
				+ $" | controller->None={controllerOff} mount-flag={accepted} riding-order={orderSet}"
				+ (npc == Agent.Main ? "" : $" (now {orderNow}/sel#{selNow}; kept for 6s)")
				+ (npc == Agent.Main
					? ". (you are the player: the mount flag is re-sent every frame for 6s, so just stand next to the horse;"
						+ " re-run 'custom.grapple horse' in a second.)"
					: ". (NPC: AI controller taken off so nothing overwrites the mount flag, the engine plays the mount clip"
						+ " and we hand the AI back when it is done; re-run 'custom.grapple horse' in a few seconds to confirm.)")
				+ note;
		}

		/// <summary>`custom.grapple horse dismount [#编号|名字]` —— 让骑着的那个落马（引擎下马事件）。</summary>
		private static string HorseDismount(List<string> args, int at)
		{
			if (Mission.Current == null) return "Error: not in mission.";

			Agent npc = ResolveHorseTarget(args, at + 1, true, out string note);
			if (npc == null) return "Error: no mounted NPC found." + note;

			Agent mount = null;
			try { mount = npc.MountAgent; } catch (Exception) { }
			if (mount == null) return $"Error: {AgentName(npc)} is not on a horse." + note;

			// 🔴 **同"上马"那一套**：先摘 AI 控制器（否则 Dismount 旗标会被 AI 每帧覆写），
			//    点旗标，然后由 `GrappleLogic` 在"下来了/超时"时把控制权还回去。
			bool wasAI = false, controllerOff = false;
			if (npc != Agent.Main)
			{
				try
				{
					wasAI = npc.IsAIControlled;
					if (wasAI) { V.SetAgentControllerNone(npc); controllerOff = true; }
				}
				catch (Exception) { }
			}

			try { npc.EventControlFlags |= Agent.EventControlFlag.Dismount; }
			catch (Exception ex) { return $"Error: setting the dismount flag failed ({ex.GetType().Name})."; }

			bool set = false;
			try { set = (npc.EventControlFlags & Agent.EventControlFlag.Dismount) != 0; }
			catch (Exception) { }

			// 🔴 **玩家也要按住**（2026-10-13 调研）：玩家侧的事件旗标被输入层每帧清零重建 ⇒ 只点一次无效；
			//    引擎放行下马还有一道"**前进速度 < 0.5 m/s 且马不在扬蹄**"的门槛（native）——
			//    重发会一直等到它满足（所以骑着跑的时候点它没反应是正常的，勒住马就下来了）。
			try { GrappleLogic.Ensure()?.RequestDismount(npc, 6f, wasAI); }
			catch (Exception) { }

			DebugLogger.Log($"[GrappleHorse] dismount: npc={AgentName(npc)}#{npc.Index}"
				+ $" mount=#{mount.Index} item='{HorseItemIdOf(mount)}' clip-before='{CurrentClipOf(npc)}'"
				+ $" flagSet={set} controllerOff={controllerOff}");

			return $"OK: pressed the engine's DISMOUNT on {AgentName(npc)}"
				+ $" (mount #{mount.Index}, item '{HorseItemIdOf(mount)}') | flag set={set}"
				+ (npc == Agent.Main
					? " | the flag is re-sent every frame for 6s (the engine only lets go when you are slow, <0.5 m/s)"
					: $" | controller->None={controllerOff}")
				+ $" | clip-before={CurrentClipOf(npc)}"
				+ ". He plays act_dismount_horse_to_* and steps off;"
				+ " re-run 'custom.grapple horse' in a second to confirm." + note;
		}

		/// <summary>`custom.grapple horse down [#编号|名字] [blow|rear|kill]` —— **被动落马**三条路（2026-10-13 调研产出）：
		/// · `blow`（默认）= 手搓一发带 `BlowFlags.CanDismount`(0x800) 的 blow 打**骑手** ——
		///   引擎"被击落"的自然判定层很窄（步下攻击者 + 特定武器 flag + 一串判定），自己置 flag = 替引擎下指令、全部绕开；
		/// · `rear` = 同样手搓一发、但打**马**、带 `BlowFlags.MakesRear`(0x200) —— 让马扬蹄；
		/// · `kill` = `Mission.KillAgentCheat(马)` —— **马死掀人是纯 native**，这条验"引擎自己会不会把人摔下来"。
		/// 判据 = 观察窗到点那一行：骑手 clip 落在 `act_rider_only_fall_*` / `act_fall_rider_*` = 真摔。</summary>
		private static string HorseDown(List<string> args, int at)
		{
			if (Mission.Current == null) return "Error: not in mission.";

			// 🔴 **模式词可以在任意位置**（2026-10-13 实机踩到）：`down rear`（不写目标）与 `down #2 rear` 都要能用。
			//    早先只认第二个参数是模式 ⇒ `down rear` 把 "rear" 当成"目标名字"（回执里那句
			//    "nobody named 'rear' -> falling back" 就是它），模式悄悄落回默认 `blow`；
			//    现场症状 = "敲 rear 没扬蹄、敲 kill 马没死，但人都落马了"（因为两发都变成了打车手的掀人 blow）。
			string mode = null, targetTok = null;
			for (int i = 1; i <= 2; i++)
			{
				string tok = ArgAt(args, at + i);
				if (string.IsNullOrEmpty(tok)) continue;
				string low = tok.ToLowerInvariant();
				if (low == "blow" || low == "rear" || low == "kill")
				{
					if (mode != null && mode != low)
						return $"Error: down got two modes ('{mode}' and '{low}') -- pick one.";
					mode = low;
					continue;
				}
				if (targetTok == null) targetTok = tok;
			}
			mode = mode ?? "blow";

			Agent npc = ResolveHorseTargetToken(targetTok, true, out string note);
			if (npc == null) return "Error: no mounted NPC found." + note;

			Agent horse = null;
			try { horse = npc.MountAgent; } catch (Exception) { }
			if (horse == null) return $"Error: {AgentName(npc)} is not on a horse (nothing to fall from)." + note;

			if (mode == "kill")
			{
				try { Mission.Current.KillAgentCheat(horse); }
				catch (Exception ex) { return $"Error: KillAgentCheat threw ({ex.GetType().Name}: {ex.Message})."; }
				try { GrappleLogic.Ensure()?.RequestDownWatch(npc, horse, 4f); } catch (Exception) { }
				DebugLogger.Log($"[GrappleHorse] down kill: horse #{horse.Index} killed (item '{HorseItemIdOf(horse)}')"
					+ $" -> watching whether the engine throws rider #{npc.Index} off by itself");
				return $"OK: killed horse #{horse.Index} (item '{HorseItemIdOf(horse)}')"
					+ $" -- the engine should throw {AgentName(npc)} off on its own (pure native)."
					+ " Watch it happen; the verdict line lands in the log in ~4s"
					+ " (or re-run 'custom.grapple horse' to see the state)." + note;
			}

			// blow / rear：手搓 blow（引擎侧实现在 GrappleBind.FireDismountBlow，配方与已验证的击退 blow 同源）
			bool rear = mode == "rear";
			Agent victim = rear ? horse : npc;
			string err = GrappleBind.FireDismountBlow(victim, rear, 60f);
			if (err != null) return $"Error: the hand-made blow failed ({err}).";

			try { GrappleLogic.Ensure()?.RequestDownWatch(npc, horse, 5f); } catch (Exception) { }

			DebugLogger.Log($"[GrappleHorse] down {mode}: victim={AgentName(victim)}#{victim.Index}"
				+ $" flag={(rear ? "MakesRear" : "CanDismount")} damage=60 (victim kept Immortal)"
				+ $" riderOnHorse={(npc.MountAgent != null ? 1 : 0)}"
				+ $" riderClip='{CurrentClipOf(npc)}' horseClip='{CurrentClipOf(horse)}'");

			return $"OK: fired a hand-made blow at {(rear ? "the HORSE" : AgentName(npc))}"
				+ $" with BlowFlags.{(rear ? "MakesRear" : "CanDismount")} (damage 60; the victim is kept Immortal so no HP is lost)"
				+ $" | rider-on-horse={npc.MountAgent != null} riderClip={CurrentClipOf(npc)} horseClip={CurrentClipOf(horse)}"
				+ ". The verdict line lands in the log in ~5s"
				+ " ('[GrappleHorse] DownWatch request finished: watch done: ...')"
				+ " -- rider-clip in act_rider_only_fall_* / act_fall_rider_* means the engine really took him off."
				+ " NOTE: the blow walks the real hit pipeline (OnAgentHit fires), so witnesses/alerts may record it." + note;
		}

		/// <summary>`custom.grapple horse` —— 状态行（mount / dismount 都是异步的，隔一秒再敲一次就是判据）。
		/// 🔴 空马一律标出**马物品 id** 与**能不能骑**：驮马/骡子的 monster 也叫 "horse"，只看名字分不出来
		/// （2026-10-13 就是挂在驮马上"没反应"的）。</summary>
		private static string HorseState()
		{
			Mission mission = Mission.Current;
			if (mission == null) return "Error: not in mission.";
			Agent main = Agent.Main;

			int nMounted = 0, nFree = 0;
			string mounted = "", free = "";
			try
			{
				foreach (Agent a in mission.Agents)
				{
					if (a == null || !AgentControlHelper.SafeIsActive(a) || !a.IsMount) continue;
					float d = main != null ? a.Position.Distance(main.Position) : 0f;
					Agent r = a.RiderAgent;
					if (r != null)
					{
						nMounted++;
						if (nMounted <= 4) mounted += (mounted.Length > 0 ? ", " : "")
							+ $"{AgentName(r)}#{r.Index} on #{a.Index} '{HorseItemIdOf(a)}' ({d:F0}m)";
					}
					else
					{
						nFree++;
						if (nFree <= 4) free += (free.Length > 0 ? ", " : "")
							+ $"#{a.Index} '{HorseItemIdOf(a)}'{(IsRideableMount(a) ? "" : " NOT-RIDEABLE")} ({d:F0}m)";
					}
				}
			}
			catch (Exception) { }

			return $"grapple horse: mounted={nMounted} [{mounted}] | riderless={nFree} [{free}]"
				+ " | usage: horse spawn [n] [itemid] | horse mount [#idx|name|me] | horse dismount [#idx|name]";
		}

		/// <summary>
		/// 找一匹**能骑的活马**（铁律 5 两轮：先常见原版 id，再遍历内存里所有已注册物品兜底）。
		/// 🔴 判据 = `HorseComponent.IsMount`（= **能骑 且不是驮畜**）—— 骆驼/驴都算（本来就是骑乘兽），
		///    驮马 / 骡子被它排除。**不要**自己按名字猜哪些能骑。
		/// </summary>
		private static ItemObject ResolveHorseItem(out string how)
		{
			string[] prefer =
			{
				"saddle_horse", "empire_horse", "khuzait_horse", "aserai_horse",
				"vlandia_horse", "sturgia_horse", "battania_horse", "camel"
			};
			try
			{
				foreach (string id in prefer)
				{
					ItemObject it = MBObjectManager.Instance.GetObject<ItemObject>(id);
					if (it != null && it.HasHorseComponent && it.HorseComponent.IsMount)
					{
						how = "by known id";
						return it;
					}
				}
			}
			catch (Exception) { }

			how = "by memory scan";
			try
			{
				return MBObjectManager.Instance.GetObject<ItemObject>(
					it => it != null && it.HasHorseComponent && it.HorseComponent.IsMount);
			}
			catch (Exception) { return null; }
		}

		/// <summary>**本会话刷出来的马**的 `Agent.Index`（`horse spawn` 记，`mount` 优先挑它）。
		/// 只做"优先"用：agent 每局重建、索引会变 ⇒ 取不到就干净回落"最近那匹能骑的"。</summary>
		private static readonly List<int> SpawnedHorseIndices = new List<int>();

		/// <summary>刚刷的那几匹里，离 <paramref name="near"/> 最近、还能骑、还没主的。没有 = null。</summary>
		private static Agent FindSpawnedRiderlessMount(Vec3 near, float maxDist)
		{
			Agent best = null;
			float bestD = maxDist;
			try
			{
				foreach (Agent a in Mission.Current.Agents)
				{
					if (a == null || !AgentControlHelper.SafeIsActive(a) || !IsRideableMount(a)) continue;
					if (a.RiderAgent != null) continue;
					if (!SpawnedHorseIndices.Contains(a.Index)) continue;
					float d = a.Position.Distance(near);
					if (d < bestD) { bestD = d; best = a; }
				}
			}
			catch (Exception) { }
			return best;
		}

		/// <summary>
		/// `custom.grapple horse rider [模板id]` —— **刷一个"已经骑在马上"的 NPC**（必成的夹具）。
		///
		/// 🔴 为什么要有它（2026-10-13）：`horse mount` 让**已有 NPC**上马走的是引擎自己的 AI 坐骑令，
		///    那条链在村镇平民身上没跑通（见 `horse why` 的断点诊断）；而验"拉下马"只需要
		///    **一个骑在马上的人**，不需要他真的会自己上马。
		///
		/// 🔴 配方来自引擎自己的 spawn 分支（反编译 `Mission.SpawnAgent`）：
		///    <code>
		///    ItemObject item = agentSpawnEquipment[EquipmentIndex.ArmorItemEndSlot].Item;
		///    if (item != null && item.HasHorseComponent && item.HorseComponent.IsRideable)
		///        agent2 = CreateHorseAgentFromRosterElements(...);   // ← 造马并挂上去
		///    </code>
		///    ⇒ **只要 spawn 装备的马槽里有能骑的马，引擎就把人直接生成在马上**（`NoHorses(true)` 就是用来
		///    关掉这件事的 —— 我们别的 spawn 命令都带着它）。注意引擎这里用的判据也是 **`IsRideable`**。
		/// 默认模板 = **准星/最近那个人的角色**（连他的装备一起克隆 ⇒ 看着就是"本村的人骑了匹马"）；
		/// 也可以给模板 id（`custom.grapple horse rider imperial_vigla_recruit`）。
		/// </summary>
		private static string HorseSpawnRider(List<string> args, int at)
		{
			Mission mission = Mission.Current;
			Agent main = Agent.Main;
			if (mission == null || main == null) return "Error: not in mission.";

			ItemObject horseItem = null;
			try { horseItem = ResolveHorseItem(out _); } catch (Exception) { }
			if (horseItem == null) return "Error: no rideable horse item found in the loaded modules.";

			// 模板：给了 id 就用 id；没给就用"准星/最近那个人"的角色（克隆他的装备 ⇒ 外观一致）
			string wantId = ArgAt(args, at + 1);
			Equipment equip = null;
			string who = "";
			if (!string.IsNullOrEmpty(wantId))
			{
				CharacterObject tpl = null;
				try { tpl = CharacterObject.Find(wantId); } catch (Exception) { }
				if (tpl == null) return $"Error: no character template '{wantId}'.";
				who = tpl.StringId;
				equip = V.RandomBattleEquipment(tpl, MBRandom.RandomInt());
			}
			else
			{
				Agent from = ResolveHorseTarget(args, at + 1, false, out _) ?? main;
				who = AgentName(from) + "#" + from.Index;
				try { equip = from.SpawnEquipment.Clone(); } catch (Exception) { }
				if (equip == null)
				{
					var c = from.Character as CharacterObject;
					if (c != null) equip = V.RandomBattleEquipment(c, MBRandom.RandomInt());
				}
			}
			if (equip == null) return "Error: could not build equipment for the rider.";

			// 🔴 把马塞进"马槽"（ArmorItemEndSlot）—— 引擎看到它就会把人直接生成在马上
			equip[EquipmentIndex.ArmorItemEndSlot] = new EquipmentElement(horseItem);

			Vec3 fwd = main.LookDirection; fwd.z = 0f;
			if (fwd.LengthSquared < 1e-4f) fwd = Vec3.Forward;
			fwd = fwd.NormalizedCopy();
			Vec3 pos = main.Position + fwd * 3f;
			Scene scene = mission.Scene;
			if (scene != null)
			{
				float gz = V.GroundHeight(scene, pos, BodyFlags.CommonCollisionExcludeFlags);
				pos.z = (float.IsNaN(gz) || Math.Abs(gz) > 1e5f) ? main.Position.z : gz + 0.05f;
			}

			Agent rider;
			try
			{
				CharacterObject tpl = null;
				if (!string.IsNullOrEmpty(wantId)) tpl = CharacterObject.Find(wantId);
				if (tpl == null) tpl = (ResolveHorseTarget(args, at + 1, false, out _) ?? main).Character as CharacterObject;
				if (tpl == null) return "Error: no usable character template.";

				var data = new AgentBuildData(new SimpleAgentOrigin(tpl, -1, null))
					.Equipment(equip)                       // ← 覆盖 spawn 装备（马就在里面）
					.InitialPosition(pos)
					.InitialDirection(fwd.AsVec2)
					.Team(main.Team);
				rider = mission.SpawnAgent(data);
			}
			catch (Exception ex)
			{
				return $"Error: SpawnAgent threw ({ex.GetType().Name}: {ex.Message}).";
			}
			if (rider == null) return "Error: SpawnAgent returned null.";

			Agent mount = null;
			try { mount = rider.MountAgent; } catch (Exception) { }

			DebugLogger.Log($"[GrappleHorse] rider: spawned '{AgentName(rider)}'#{rider.Index}"
				+ $" from={who} horseItem='{horseItem.StringId}' mounted={(mount != null)}"
				+ (mount != null ? $" mount=#{mount.Index}" : ""));

			return $"OK: spawned '{AgentName(rider)}' #{rider.Index} 3m ahead"
				+ (mount != null
					? $" ALREADY MOUNTED on #{mount.Index} ('{HorseItemIdOf(mount)}')."
					: " (⚠ not mounted: the engine did not attach a horse -- check the item).")
				+ " Next: 'custom.grapple bind test mounted' to test the pull-down.";
		}

		/// <summary>
		/// `custom.grapple horse why [#编号|名字]` —— **"为什么他不上马"的断点诊断**（2026-10-13 立）。
		/// 把引擎那条链上**每一道门**的当前值打出来（缺哪个一眼可见），并落运行时日志 `[GrappleHorse] why:`。
		///
		/// 引擎的 AI 上马链（反编译 `CommonAIComponent.OnTick` / `FindClosestMountAvailable`）：
		///   `Mission.AllowAiTicking` → `IsAIControlled` → `CommonAIComponent != null` → 没马 →
		///   `!IsRetreating` → 搜索计时器到点（2~3 s）→ **`GetRidingOrder() == 1`** →
		///   从 `Mission.Current.MountsWithoutRiders` 里挑「活跃 + 没主 + **!IsRunningAway** +
		///   `CanAgentRideMount`」的最近一匹 → `ReserveMount` → native 走过去上马。
		/// </summary>
		private static string HorseWhy(List<string> args, int at)
		{
			Mission mission = Mission.Current;
			if (mission == null) return "Error: not in mission.";

			Agent npc = ResolveHorseTarget(args, at + 1, false, out string note);
			if (npc == null) return "Error: no NPC found." + note;

			string line = HorseWhyLine(npc);
			DebugLogger.Log("[GrappleHorse] " + line);
			return line;
		}

		/// <summary>
		/// 断点诊断那一行（`horse why` 与 `horse mount` 共用 —— **mount 跑完自动落一条**，
		/// 这样"上不去"的时候不用再让人多敲一次命令）。
		/// </summary>
		private static string HorseWhyLine(Agent npc)
		{
			Mission mission = Mission.Current;
			bool allowTick = false;
			try { allowTick = mission != null && mission.AllowAiTicking; } catch (Exception) { }

			Agent horse = FindSpawnedRiderlessMount(npc.Position, 60f) ?? FindNearestRiderlessMount(npc.Position, 60f);

			string npcPart = $"npc='{AgentName(npc)}'#{npc.Index}"
				+ $" aiControlled={Safe(() => npc.IsAIControlled)}"
				+ $" paused={Safe(() => npc.IsPaused)}"
				+ $" ridingOrder={SafeI(() => npc.GetRidingOrder())} (1=Mount)"
				+ $" retreating={Safe(() => npc.IsRetreating())}"
				+ $" commonAI={Safe(() => npc.CommonAIComponent != null)}"
				+ $" humanAI={Safe(() => npc.HumanAIComponent != null)}"
				+ $" formation={SafeS(() => npc.Formation != null ? npc.Formation.Index.ToString() : "none")}"
				+ $" canRideFlag={Safe(() => (npc.GetAgentFlags() & AgentFlag.CanRide) != 0)}"
				+ $" ridingSkill={SafeF(() => MissionGameModels.Current.AgentStatCalculateModel.GetEffectiveSkill(npc, DefaultSkills.Riding))}"
				+ $" clip='{CurrentClipOf(npc)}'";

			string horsePart = "(none)", checkPart = "-";
			if (horse != null)
			{
				Agent h = horse;
				horsePart = $"horse #{h.Index} '{HorseItemIdOf(h)}' dist={h.Position.Distance(npc.Position):F1}m"
					+ $" active={Safe(() => h.IsActive())}"
					+ $" riderless={Safe(() => h.RiderAgent == null)}"
					+ $" runningAway={Safe(() => h.IsRunningAway)}"
					+ $" inMountList={Safe(() => InMountsWithoutRiders(h))}"
					+ $" reservedRider={SafeI(() => h.CommonAIComponent?.ReservedRiderAgentIndex ?? -1)}";
				checkPart = $"checkSkillForMounting={Safe(() => npc.CheckSkillForMounting(h))}"
					+ $" canAgentRideMount={Safe(() => MissionGameModels.Current.AgentStatCalculateModel.CanAgentRideMount(npc, h))}";
			}

			return $"horse why: allowAiTicking={allowTick} | {npcPart} | {horsePart} | {checkPart}";
		}

		/// <summary>马是不是在引擎那份"无主马"表里（`Mission.MountsWithoutRiders` —— AI 只从这里挑马）。</summary>
		private static bool InMountsWithoutRiders(Agent horse)
		{
			try
			{
				foreach (var kv in Mission.Current.MountsWithoutRiders)
				{
					if (ReferenceEquals(kv.Key, horse)) return true;
				}
			}
			catch (Exception) { }
			return false;
		}

		// 诊断取值的小包装（任何一个读数抛异常都不该让整条诊断挂掉）
		private static string Safe(Func<bool> f) { try { return f() ? "1" : "0"; } catch (Exception) { return "?"; } }
		private static string SafeI(Func<int> f) { try { return f().ToString(); } catch (Exception) { return "?"; } }
		private static string SafeF(Func<float> f) { try { return f().ToString("F0"); } catch (Exception) { return "?"; } }
		private static string SafeS(Func<string> f) { try { return f() ?? "?"; } catch (Exception) { return "?"; } }

		/// <summary>离 <paramref name="near"/> 最近的**能骑的**空马。找不到 = null。</summary>
		/// <summary>最近的**能骑的马**（不管有没有人骑 —— "放上马背"允许放到别人骑着的马上）。</summary>
		/// <summary>
		/// **就近一匹能骑的马**（不管有没有人骑）—— 判据 = <see cref="IsRideableMount"/>（驮马也算）。
		/// 消费者：（放上马背时挑马）、。
		/// </summary>
		internal static Agent FindNearestRideableMount(Vec3 near, float maxDist)
			=> AgentControlHelper.FindNearestRideableMount(near, maxDist);

		private static Agent FindNearestAnyMount(Vec3 near, float maxDist)
			=> FindNearestRideableMount(near, maxDist);

		private static Agent FindNearestRiderlessMount(Vec3 near, float maxDist)		{
			Agent best = null;
			float bestD = maxDist;
			try
			{
				foreach (Agent a in Mission.Current.Agents)
				{
					if (a == null || !AgentControlHelper.SafeIsActive(a) || !IsRideableMount(a)) continue;
					if (a.RiderAgent != null) continue;               // 有主的不要
					float d = a.Position.Distance(near);
					if (d < bestD) { bestD = d; best = a; }
				}
			}
			catch (Exception) { }
			return best;
		}

		/// <summary>
		/// 这个 agent 是不是**能骑的**坐骑 —— 判据 = 它那件马物品的 `HorseComponent.IsRideable`
		/// （= 物品 XML 里的 `is_mountable`，也正是引擎 `ItemObject.IsMountable` 用的那一个）。
		///
		/// 🔴🔴 **别用 `Agent.IsMount`，也别用 `HorseComponent.IsMount`** —— 两个都会把**驮马**判死：
		///    · `Agent.IsMount` = `AgentFlag.Mountable`（旗标太粗，不可靠）；
		///    · `HorseComponent.IsMount` = `IsRideable && !IsPackAnimal` —— 而 `sumpter_horse`（驮马）
		///      的 XML 是 **`is_mountable="true" is_pack_animal="true"`** ⇒ 它是**能骑的**（只是同时算驮畜、
		///      进商队负重那套逻辑），却被 `IsMount` 判成 false。
		///    2026-10-13 实机两次踩坑：第一次挑中了玩家自己那匹驮马（`Agent.IsMount` 放行、驮马其实能骑，
		///    但后面另有原因没上去）；第二次我用 `HorseComponent.IsMount` 当过滤器 —— 那会把**玩家自己的马
		///    也筛掉**，方向反了。**唯一正确判据 = `IsRideable`**（引擎自己也是这么判的）。
		/// </summary>
		internal static bool IsRideableMount(Agent a) => AgentControlHelper.IsRideableMount(a);

		/// <summary>这个坐骑**是哪件马物品**（回执里显示用 —— 驮马 / 骑乘马在这里才分得开，
		/// 两者的 `Monster.StringId` 都是 `"horse"`）。取不到给 "?"。</summary>
		private static string HorseItemIdOf(Agent a)
		{
			try
			{
				ItemObject it = a?.SpawnEquipment[EquipmentIndex.ArmorItemEndSlot].Item;
				return it != null ? it.StringId : "?";
			}
			catch (Exception) { return "?"; }
		}

		/// <summary>agent 此刻在播哪个动作（**用 clip 名，不用动作名** —— 见 `V.ClipName` 的注释：
		/// `ActionIndexCache.GetName()` 在 1.3.15 上是坏的；`V.ActName` 给的又是动作**类别**不是名字）。</summary>
		private static string CurrentClipOf(Agent a)
		{
			try { return V.ClipName(a, a.GetCurrentAction(0), "-"); }
			catch (Exception) { return "-"; }
		}

		/// <summary>
		/// **目标怎么指**（骑马三条命令共用）：
		///   · `me` / `player` / `main` → 玩家自己（**验"骑马勾墙"那条链要用**）
		///   · `#12` 或 `12`            → 按 `Agent.Index` 精确找（编号从状态行或 `custom.ice list` 拿）
		///   · 别的字串                  → 按名字找（包含匹配，忽略大小写）
		///   · 不写 / 认不出             → **准星指着的人** → 最近的其他人
		///     （<paramref name="preferMounted"/> = true 时两条兜底都**只挑骑在马上的**）
		/// 认不出时往 <paramref name="note"/> 里写一句说明（控制台纪律：首参可弃、回落要注明）。
		/// </summary>
		private static Agent ResolveHorseTarget(List<string> args, int at, bool preferMounted, out string note)
		{
			note = "";
			string tok = ArgAt(args, at);
			if (!string.IsNullOrEmpty(tok))
			{
				string low = tok.ToLowerInvariant();
				if (low == "me" || low == "player" || low == "main" || low == "self")
					return Agent.Main;

				string num = low.StartsWith("#") ? low.Substring(1) : low;
				if (int.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
				{
					try
					{
						foreach (Agent a in Mission.Current.Agents)
						{
							if (a != null && a.Index == idx && AgentControlHelper.SafeIsActive(a) && !a.IsMount)
								return a;
						}
					}
					catch (Exception) { }
					note = $" [note: no active agent with index {idx} -> falling back]";
				}
				else
				{
					Agent byName = null;
					try
					{
						foreach (Agent a in Mission.Current.Agents)
						{
							if (a == null || !AgentControlHelper.SafeIsActive(a) || a.IsMount) continue;
							string n = AgentName(a);
							if (n != null && n.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0) { byName = a; break; }
						}
					}
					catch (Exception) { }
					if (byName != null) return byName;
					note = $" [note: nobody named '{tok}' -> falling back]";
				}
			}

			// 准星 → 最近的其他人（preferMounted = 只挑骑着的；玩家自己也算候选，验"我自己骑马"用）
			Agent looked = FindLookedAtAgent();
			if (looked != null && (!preferMounted || looked.MountAgent != null)) return looked;

			Agent best = null;
			float bestD = 60f;
			try
			{
				Vec3 from = Agent.Main != null ? Agent.Main.Position : Vec3.Zero;
				foreach (Agent a in Mission.Current.Agents)
				{
					if (a == null || !AgentControlHelper.SafeIsActive(a) || a.IsMount) continue;
					if (preferMounted && a.MountAgent == null) continue;
					if (!preferMounted && a == Agent.Main) continue;      // 不写目标时不默认挑玩家自己
					float d = a.Position.Distance(from);
					if (d < bestD) { bestD = d; best = a; }
				}
			}
			catch (Exception) { }
			return best;
		}

		/// <summary>
		/// 同上，但**目标 token 由调用方直接给** —— 给 `down` 这种"模式词可能与目标词换位"的命令用
		/// （`custom.grapple horse down rear` = 模式在前、无目标）。实现 = 包成单元素列表复用上面那个解析器，
		/// 不复制任何匹配逻辑。
		/// </summary>
		private static Agent ResolveHorseTargetToken(string tok, bool preferMounted, out string note)
		{
			var one = new List<string> { tok ?? "" };
			return ResolveHorseTarget(one, 0, preferMounted, out note);
		}

		/// <summary>
		/// 准星指着谁 —— 相机原点沿视线打一条 30 m 的射线问引擎。
		/// 方向走 <see cref="CameraLook.TryGet"/>（铁律 35：接管期间只有它算得对），位置照读
		/// `Mission.GetCameraFrame().origin`（位置两种相机下都对）。找不到 = null。
		/// </summary>
		private static Agent FindLookedAtAgent()
		{
			Mission m = Mission.Current;
			Agent me = Agent.Main;
			if (m == null || me == null) return null;
			try
			{
				if (!CameraLook.TryGet(out Vec3 forward)) return null;
				Vec3 origin = m.GetCameraFrame().origin;
				Agent hit = V.RayCastForClosestAgent(origin, origin + forward * 30f, me.Index, out float _, 0.15f);
				if (hit == null || hit == me || hit.IsMount) return null;
				return hit;
			}
			catch (Exception) { return null; }
		}

		/// <summary>名字（取不到就用 `agent#Index`）—— 控制台回执里显示用。</summary>
		private static string AgentName(Agent a)
		{
			try
			{
				string n = a?.Name?.ToString();
				return string.IsNullOrEmpty(n) ? ("agent#" + (a?.Index ?? -1)) : n;
			}
			catch (Exception) { return "agent"; }
		}

		/// <summary>出回执用的怪物 id（马 = "horse"/"camel"…）。取不到给 "?"。</summary>
		private static string MonsterIdOf(Agent a)
		{
			try { return a?.Monster?.StringId ?? "?"; }
			catch (Exception) { return "?"; }
		}

		/// <summary>
		/// `bind test` 的目标拾取：最近的一个"能捆的"。
		/// <paramref name="wantMounted"/> = true 时**只挑骑马的**（2026-10-13 加：验收"拉下马"用 ——
		/// 不然还得先去战场上找个敌骑才能试）。默认 false = 只挑步行的（旧行为）。
		/// </summary>
		private static Agent FindNearestBindableAgent(float maxDist, bool wantMounted = false)
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
					if (a.IsMount || a.RiderAgent != null) continue;      // 马本身不收（两条路都不收）
					bool mounted = false;
					try { mounted = a.MountAgent != null; } catch (Exception) { }
					if (mounted != wantMounted) continue;
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
