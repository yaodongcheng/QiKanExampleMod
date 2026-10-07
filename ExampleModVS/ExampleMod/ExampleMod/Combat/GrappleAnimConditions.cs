using LivingWorldNpcs.Animation;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **钩索状态机的谓词与"时刻"名**（XML 里只写名字，真身在这；照 Flight/FlightAnimConditions.cs）。
	///
	/// 定义在 `ModuleData/statemachines/grapple.xml`（2026-10-04 二稿：前半段 ready/hold/release 归**武器 usage**，
	/// 本机只管"过程"起的后半段），用到这些名字：
	///   · `pull-trigger`    —— 时刻标签（事件边）：**开火后 1.167 s（release 播完；2026-10-07 重切后的新时长）**，进入"过程"那一刻。
	///                          真身 `c => false`，触发在 C#（GrappleLogic 的计时）。
	///   · `fall-trigger`    —— 时刻标签（事件边）：**拉拽途中收摊且人还在空中** ⇒ C# 力送 自由落体。
	///   · `landing-due`     —— 普通谓词：过程段快播完（剩 ≤ 阈值）**且**这一钩有平台 ⇒ 该切 落地。
	///   · `fall-due`        —— 普通谓词：过程段快播完**且**没有平台 ⇒ 该切 自由落体。
	///   · `recover-due`     —— 普通谓词：过程段刚起头（剩 ≤ 阈值）**且**这一钩已放弃 ⇒ 切 收手（打空出口）。
	///   · `land-trigger`    —— 复用飞行已登记的时刻名（自由落体 → outside 那条触地边，只为把出口画全）。
	///
	/// 🔴 **两个"剩多少就切"的阈值是这里的静态字段**：`custom.grapple` 命令可热调（不重编译、不改 XML）。
	///    为什么阈值在 C# 而不在 XML：一条边只能写**一种**条件（`anim=` 与 `when=` 互斥，装载器规矩），
	///    而这几条都要"**时间点 + 分支**"两个输入，所以合进谓词真身。
	/// </summary>
	public static class GrappleAnimConditions
	{
		// ── 时刻标签（事件边用；真身 c => false，真正的触发在 C#）──
		public const string PullTrigger = "pull-trigger";
		public const string FallTrigger = "fall-trigger";

		// ── 普通谓词名（grapple.xml 的 `when=` 写的就是这三个字符串）──
		public const string LandingDue = "landing-due";
		public const string FallDue = "fall-due";
		public const string RecoverDue = "recover-due";

		/// <summary>飞行那条"板顶触地"时刻名（复用；飞行没登记就先补一个，避免注册顺序依赖）。</summary>
		public const string LandTrigger = "land-trigger";

		/// <summary>
		/// **"过程"段剩多少就切 落地 / 自由落体**（占 clip 比例，默认 0.10）。
		///
		/// 依据（2026-10-04 二稿时间轴）：过程段（合并件 v17..55）**末帧 = 落地起点 v56**
		/// ⇒ 剩 10%（留一点余量让交叉淡化从活姿势接上，同 finish-margin 的道理）。
		/// 落地段开头 ~0.45 s 是"边收边落"的收势，盖住位移的减速尾段（位移真正结束 ≈ 源 GrappleEnd 帧 70）。
		/// </summary>
		public static float SwitchRemainFrac = 0.10f;

		/// <summary>
		/// **打空/中止时，"过程"段剩多少就切 收手**（占 clip 比例，默认 0.90 = 刚进过程 0.13 s 就切）。
		///
		/// 依据（2026-10-04 逐帧核对）：收手段 = 源帧 21..29（手臂从甩出的顶点放回），而合并件从**帧 17** 起
		/// ⇒ 过程段播 4 帧（0.13 s）后正好接上收手段的起点，**无缝**。
		/// （2026-10-07 重切后：release 1.17 + 0.13 + 收手 1.17 ≈ 2.5 s；旧剪辑那套 ≈0.77 s。）
		/// </summary>
		public static float CancelRemainFrac = 0.90f;

		private static GrappleAnimContext C(AnimContext c) => c as GrappleAnimContext;

		/// <summary>把本机用到的名字全部登记进去（`GrappleAnimMachine.Register` 里调一次）。</summary>
		public static void RegisterAll()
		{
			// ── 时刻标签：真身只是"这个时刻存在"，触发在 C#（与飞行三个 trigger 同套路）──
			AnimConditions.Register(PullTrigger, c => false);

			// ── 普通谓词：读上下文（段剩余 + 落点有无 / 放弃标志）──
			AnimConditions.Register(LandingDue, c =>
			{
				GrappleAnimContext g = C(c);
				return g != null && g.LandingFound && g.AnimRemainFrac <= SwitchRemainFrac;
			});
			AnimConditions.Register(FallDue, c =>
			{
				GrappleAnimContext g = C(c);
				return g != null && !g.LandingFound && g.AnimRemainFrac <= SwitchRemainFrac;
			});
			AnimConditions.Register(RecoverDue, c =>
			{
				GrappleAnimContext g = C(c);
				return g != null && g.Cancelled && g.AnimRemainFrac <= CancelRemainFrac;
			});

			// ── 复用的两个时刻名：飞行那台通常已登记；没有就补一个（注册顺序无关，重复登记只会多一行日志）──
			if (!AnimConditions.TryGet(FallTrigger, out _))
			{
				AnimConditions.Register(FallTrigger, c => false);
			}
			if (!AnimConditions.TryGet(LandTrigger, out _))
			{
				AnimConditions.Register(LandTrigger, c => false);
			}
		}
	}
}
