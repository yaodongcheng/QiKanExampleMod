using System;
using System.IO;
using System.Reflection;
using LivingWorldNpcs.Animation;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 钩索动画状态机要读的**事实**（转移条件只看这里）。
	///
	/// 🔴 每帧由 <see cref="GrappleLogic"/> 填、**填完才 Tick 状态机**（照飞行 `PlayerFlightBehavior` 的接法）。
	///    三个量的来源与口径见各自注释。
	/// </summary>
	public sealed class GrappleAnimContext : AnimContext, IAnimInputFacts
	{
		/// <summary>
		/// **当前动画还剩多少**（占 clip 比例 0~1；循环状态 = +∞ ⇒ "剩余 &lt; X%" 永不成立）。
		/// 来源 = 每帧从状态机回读（`_anim.CurrentRemainFrac`）——是**当前状态**的剩余，
		/// 所以 `landing-due`（只在 过程 上求值）拿到的就是"过程段"的剩余。
		/// </summary>
		public float AnimRemainFrac = float.PositiveInfinity;

		/// <summary>
		/// 这一钩**解算出落点平台了吗**（`landing-due` / `fall-due` 的分支依据）。
		/// 来源 = `GrappleLogic._landing.Found`（钩头命中地形那一刻解算、此后不变）。
		/// 必须在**每一次开火时重置为 false**（上一钩的平台别带进这一钩）。
		/// </summary>
		public bool LandingFound;

		/// <summary>
		/// 这一钩**已放弃**（打空 / 命令收钩 / 受击）——`recover-due` 用它把动作切进"收手"（打空亮相）。
		/// 开火时重置；机器出机（收摊）后清。
		/// </summary>
		public bool Cancelled;

		// ── IAnimInputFacts：本机不用键盘谓词；只需把"剩余"接给 `anim=` 原语（落地 → outside 那条边在用）──
		bool IAnimInputFacts.KeyHeld(int i) => false;
		float IAnimInputFacts.AnimRemainFrac => AnimRemainFrac;
	}

	/// <summary>
	/// **钩索动画状态机**（2026-10-04 立，照 `Flight/FlightAnimMachine.cs` 的三件套写法）。
	///
	/// 🔴 **"什么时候播哪条"的规则不在代码里** —— 在
	/// [`ModuleData/statemachines/grapple.xml`](../../../ModuleData/statemachines/grapple.xml)：
	/// 四个状态（投掷 / 过程 / 落地 / 自由落体）+ 全部转移边。**改那个文件不用重编译**，重启游戏即生效。
	///
	/// 本文件只剩三件事：
	///   ① 注册**谓词与时刻名**（<see cref="GrappleAnimConditions.RegisterAll"/>）——判据本体留 C#；
	///   ② 从 XML **装载 + 校验**（<see cref="AnimMachineLoader"/>），校验不过**不注册**；
	///   ③ 挂上**运行期旋钮**（默认过渡时长 / 动作优先级）。
	///
	/// 🔴 **C# 里一个状态名都没有**（照飞行那条规矩）：四个接缝全按"时刻名"从定义里读
	///    （`TryEventTarget`）：`throw-trigger` / `pull-trigger` / `fall-trigger` 由相位 Force；
	///    `landing-due` / `fall-due` / `throw-cancelled` 由状态机每帧自己求值（谓词读上下文）。
	///
	/// 注册时机：`MySubModule.OnSubModuleLoad`（在 `FlightAnimMachine.Register()` **之后**——
	/// 复用的 `fall-trigger` / `land-trigger` 靠飞行先登记；本机也有"没有才登记"的兜底，顺序其实无所谓）。
	/// </summary>
	public static class GrappleAnimMachine
	{
		/// <summary>注册名（<see cref="AnimMachineRegistry.Create"/> 用它取）。</summary>
		public const string Name = "grapple";

		/// <summary>定义文件名（文件名 = 机器名）。</summary>
		public const string FileName = "grapple.xml";

		/// <summary>定义文件所在子目录（相对 `ModuleData/`）。</summary>
		public const string SubDir = "statemachines";

		/// <summary>
		/// 默认过渡时长（秒）——没写 `blend=` 的边用它。热调口子：`custom.grapple animblend &lt;秒&gt;`。
		/// </summary>
		public static float AnimBlendIn = 0.3f;

		/// <summary>
		/// 给姿态动作带的优先级（写进 `additionalFlags`，0 = 引擎按 clip 自带走）。
		/// 🔴 **真正生效的是 clip 元数据里的 Priority**（2026-10-04 晚实机：pull/land/recover 原为 0，
		///    被引擎开火后必播的上弦 `reload_bow_right`（P11）逐帧抢走 ⇒ 两套姿势糊在一起、"拉拽/落地动画有点怪"；
		///    修 = `tpaccli clipprio --filter <clip> --prio 30` 写进交付包，全提到 30 —— 与飞行工程同款修法）。
		///    运行期 `additionalFlags` 传引擎不认（旁证：`custom.anim_ch` 的 `prio=` 传了没用），这条留着只是保险。
		/// </summary>
		public static int ActionPriority = 0;

		/// <summary>注册进注册表（幂等：重名会覆盖，方便热改）。</summary>
		public static void Register()
		{
			// ① 判据（C#）：XML 里的 `when=` 只能引用这里登记过的名字
			GrappleAnimConditions.RegisterAll();

			// ② 结构（XML）：装载 + 校验；校验不过就不注册
			//    （钩索本体照常，只是没有姿态动画 —— 而不是带着半张错表跑）
			string path = Path.Combine(ModuleRoot, "ModuleData", SubDir, FileName);
			AnimMachineDef def = AnimMachineLoader.Load(path, out string error);
			if (def == null)
			{
				DebugLogger.Log("[Grapple] 状态机未注册（定义有问题，见上面的 [Anim] 报错）—— "
								+ "钩索本身照常，只是没有姿态动画");
				return;
			}

			// ③ 运行期旋钮（不属结构，故不进 XML）
			def.DefaultBlend = () => AnimBlendIn;
			def.ActionPriority = () => ActionPriority;

			AnimMachineRegistry.Register(def);
		}

		/// <summary>模块根目录（从 DLL 位置反推：`bin/Win64_Shipping_Client/xxx.dll` → 上两级）。</summary>
		private static string ModuleRoot
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
