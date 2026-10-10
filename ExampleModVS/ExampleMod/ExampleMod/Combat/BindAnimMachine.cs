using System;
using System.IO;
using System.Reflection;
using LivingWorldNpcs.Animation;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **被绑者状态机要读的"事实"**（转移条件只看这里；照飞行/钩索那两台的写法）。
	///
	/// 🔴 三个量都由 <see cref="GrappleBind"/> 填、**填完才 Tick 状态机**。
	///    状态机里一个"这个人在被谁扛/有没有玩家"之类的字眼都不该有 —— 只读这里的标志。
	/// </summary>
	public sealed class BindAnimContext : AnimContext, IAnimInputFacts
	{
		/// <summary>在挣扎（`站缚 ⇄ 挣扎`）。**本轮由 `custom.grapple bind struggle 0|1` 手动置** ——
		/// 自动触发条件（什么情况下算"在闹"）还没定，先把线接齐、行为留空。</summary>
		public bool Struggling;

		/// <summary>
		/// **已放人**（松绳 / 收摊 / 被别的钩替换）—— 由 <see cref="GrappleBind.Release"/> 置 true。
		///
		/// 🔴 这是本机的**出机正门**：C# 只置这个标志，**"走哪条路出机"由 bind.xml 说了算**
		///    （站着 = 直接出机；趴着 = 先演起身、落到站缚再出机）。
		///    这样"放人要不要演起身"这件事不在代码里，改图即可改行为。
		/// </summary>
		public bool Released;

		/// <summary>扛人者在移动（`被扛行 ⇄ 被扛走`）。读的是**扛人者**的速度，不是被扛者的
		/// （被扛者是每帧被我们摆位的，它自己的速度没有意义）。</summary>
		public bool CarrierMoving;

		/// <summary>喂给 `anim=` 原语用（本机现在的边没用它，但接口要齐 —— 见 <see cref="IAnimInputFacts"/>）。</summary>
		public float AnimRemainFrac = float.PositiveInfinity;

		bool IAnimInputFacts.KeyHeld(int keyIndex) => false;
		float IAnimInputFacts.AnimRemainFrac => AnimRemainFrac;
	}

	/// <summary>
	/// **被绑者状态机的谓词与"时刻"名**（XML 里只写名字，真身在这；照 GrappleAnimConditions）。
	///
	/// 定义在 `ModuleData/statemachines/bind.xml`。用到这些名字：
	///   · `bind-struggling`        —— 普通谓词：在挣扎（读上下文 <see cref="BindAnimContext.Struggling"/>）
	///   · `bind-released`          —— 普通谓词：**已放人 ⇒ 出机**（读 <see cref="BindAnimContext.Released"/>，出机正门）
	///   · `bind-carrier-moving`    —— 普通谓词：扛人者在移动（读 <see cref="BindAnimContext.CarrierMoving"/>）
	///   · `bind-hit` / `bind-hold` / `yank-trigger` / `rise-trigger` / `carry-trigger` / `put-trigger`
	///                              —— **时刻标签**（事件边）：真身 `c => false`，触发在 C#。
	///
	/// 🔴 **谓词名一律带 `bind-` 前缀**：谓词注册表是**全局按名字**查的（<see cref="AnimConditions"/>），
	///    两台机若用同一个名字，后注册的会覆盖先注册的 —— 而两台机的上下文类型不同，
	///    覆盖之后另一台读到的就是 null（条件恒 false，静默失效）。**事件标签不受此限**
	///    （它们从不在 Tick 里求值，只当名字用），所以 `carry-trigger` / `put-trigger` 两台机共用是安全的。
	/// </summary>
	public static class BindAnimConditions
	{
		// ── 普通谓词名（bind.xml 的 `when=` 写的就是这三个字符串）──
		public const string Struggling = "bind-struggling";
		public const string Released = "bind-released";
		public const string CarrierMoving = "bind-carrier-moving";

		// ── 时刻标签（事件边用；真身 c => false，真正的触发在 C#）──
		public const string HitTrigger = "bind-hit";
		public const string HoldTrigger = "bind-hold";
		public const string YankTrigger = "yank-trigger";
		public const string RiseTrigger = "rise-trigger";
		public const string CarryTrigger = "carry-trigger";
		public const string PutTrigger = "put-trigger";

		/// <summary>
		/// **被扛行／被扛走的切换阈值**（米/秒）。扛人者走得比这快 ⇒ 播"被扛走"，否则"被扛行"。
		/// 与飞行那台 `BoostStartSeconds` 同类：**运行期旋钮**（`custom.grapple carry movethr`）。
		/// </summary>
		public static float CarrierMovingSpeed = 0.35f;

		private static BindAnimContext C(AnimContext c) => c as BindAnimContext;

		/// <summary>把本机用到的名字全部登记进去（<see cref="BindAnimMachine.Register"/> 里调一次）。</summary>
		public static void RegisterAll()
		{
			// ── 普通谓词：读上下文 ──
			AnimConditions.Register(Struggling, c =>
			{
				BindAnimContext b = C(c);
				return b != null && b.Struggling;
			});
			AnimConditions.Register(Released, c =>
			{
				BindAnimContext b = C(c);
				return b != null && b.Released;
			});
			AnimConditions.Register(CarrierMoving, c =>
			{
				BindAnimContext b = C(c);
				return b != null && b.CarrierMoving;
			});

			// ── 时刻标签：真身只是"这个时刻存在" ──
			AnimConditions.Register(HitTrigger, c => false);
			AnimConditions.Register(HoldTrigger, c => false);
			AnimConditions.Register(YankTrigger, c => false);
			AnimConditions.Register(RiseTrigger, c => false);

			// ── 两台机共用的两个时刻名：扛人者那台（carry.xml）也引用它们。
			//    谁先装载都行 —— 没有就补一个（重复登记只会多一行日志）。──
			if (!AnimConditions.TryGet(CarryTrigger, out _))
			{
				AnimConditions.Register(CarryTrigger, c => false);
			}
			if (!AnimConditions.TryGet(PutTrigger, out _))
			{
				AnimConditions.Register(PutTrigger, c => false);
			}
		}
	}

	/// <summary>
	/// **被绑者动画状态机**（2026-10-10 立，照 `GrappleAnimMachine` 的三件套写法）。
	///
	/// 🔴 **"什么时候播哪条"的规则不在代码里** —— 在
	/// [`ModuleData/statemachines/bind.xml`](../../../ModuleData/statemachines/bind.xml)：
	/// 十个状态（受击 / 站缚 / 挣扎 / 拉倒 / 趴缚 / 起身 / 被扛起 / 被扛行 / 被扛走 / 被放下）
	/// + 全部转移边。**改那个文件不用重编译**，重启游戏即生效。
	///
	/// 本文件只剩三件事：
	///   ① 注册**谓词与时刻名**（<see cref="BindAnimConditions.RegisterAll"/>）——判据本体留 C#；
	///   ② 从 XML **装载 + 校验**（<see cref="AnimMachineLoader"/>），校验不过**不注册**；
	///   ③ 挂上**运行期旋钮**（默认过渡时长 / 动作优先级）。
	///
	/// 🔴 **C# 里一个状态名都没有**（照飞行那条规矩）：全部接缝按"时刻名"从定义里读
	///    （`TryEventTarget`）—— `bind-hit` / `yank-trigger` / `rise-trigger` / `carry-trigger` / `put-trigger`。
	///    出机也不用状态名：置上下文里的 `Released` 即可，走哪条路由图决定。
	///
	/// 注册时机：`MySubModule.OnSubModuleLoad`（在飞行 / 钩索之后，顺序无所谓 —— 共用名有"没有才登记"兜底）。
	/// </summary>
	public static class BindAnimMachine
	{
		/// <summary>注册名（<see cref="AnimMachineRegistry.Create"/> 用它取）。</summary>
		public const string Name = "bind";

		/// <summary>定义文件名（文件名 = 机器名）。</summary>
		public const string FileName = "bind.xml";

		/// <summary>定义文件所在子目录（相对 `ModuleData/`）。</summary>
		public const string SubDir = "statemachines";

		/// <summary>
		/// 默认过渡时长（秒）——没写 `blend=` 的边用它。热调口子：`custom.grapple bind animblend &lt;秒&gt;`。
		/// 默认比钩索那台（0.3）小一点：站缚 ⇄ 挣扎、被扛行 ⇄ 被扛走这两组是同族互切，
		/// 交叉淡化长了看着"软"。
		/// </summary>
		public static float AnimBlendIn = 0.25f;

		/// <summary>
		/// 给姿态动作带的优先级（写进 `additionalFlags`，0 = 引擎按 clip 自带走）。
		/// 🔴 **真正生效的是 clip 元数据里的 Priority**（见 `GrappleAnimMachine.ActionPriority` 那段实证）。
		/// </summary>
		public static int ActionPriority = 0;

		/// <summary>注册进注册表（幂等：重名会覆盖，方便热改）。</summary>
		public static void Register()
		{
			// ① 判据（C#）：XML 里的 `when=` 只能引用这里登记过的名字
			BindAnimConditions.RegisterAll();

			// ② 结构（XML）：装载 + 校验；校验不过就不注册
			//    （钩索本体照常，只是被绑的人没有姿态动画 —— 而不是带着半张错表跑）
			string path = Path.Combine(ModuleRoot, "ModuleData", SubDir, FileName);
			AnimMachineDef def = AnimMachineLoader.Load(path, out string error);
			if (def == null)
			{
				DebugLogger.Log("[Grapple] 被绑者状态机未注册（定义有问题，见上面的 [Anim] 报错）—— "
								+ "钩索本身照常，只是被绑的人没有姿态动画");
				return;
			}

			// ③ 运行期旋钮（不属结构，故不进 XML）
			def.DefaultBlend = () => AnimBlendIn;
			def.ActionPriority = () => ActionPriority;

			AnimMachineRegistry.Register(def);
		}

		/// <summary>模块根目录（从 DLL 位置反推：`bin/Win64_Shipping_Client/xxx.dll` → 上两级）。</summary>
		internal static string ModuleRoot
		{
			get
			{
				try
				{
					string dll = Assembly.GetExecutingAssembly().Location;
					return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(dll), "..", ".."));
				}
				catch
				{
					return ".";
				}
			}
		}
	}
}
