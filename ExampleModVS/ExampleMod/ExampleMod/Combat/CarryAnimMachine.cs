using LivingWorldNpcs.Animation;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **扛人者状态机要读的"事实"**（照飞行/钩索/被绑者那三台的写法）。
	/// 只需要一个量：扛人者自己这一帧在不在移动（`扛行 ⇄ 扛走` 用它切）。
	/// </summary>
	public sealed class CarryAnimContext : AnimContext, IAnimInputFacts
	{
		/// <summary>扛人者在移动（米/秒 &gt; <see cref="BindAnimConditions.CarrierMovingSpeed"/>）。</summary>
		public bool Moving;

		/// <summary>喂给 `anim=` 原语用（本机的边没用它，但接口要齐）。</summary>
		public float AnimRemainFrac = float.PositiveInfinity;

		bool IAnimInputFacts.KeyHeld(int keyIndex) => false;
		float IAnimInputFacts.AnimRemainFrac => AnimRemainFrac;
	}

	/// <summary>
	/// **扛人者状态机的谓词**（定义在 `ModuleData/statemachines/carry.xml`）。
	///
	/// 只用到一个普通谓词名 `carry-moving`；两个时刻标签（`carry-trigger` / `put-trigger`）
	/// **与被绑者那台共用**，由 <see cref="BindAnimConditions.RegisterAll"/> 兜底登记
	/// （事件标签从不在 Tick 里求值，共用是安全的；普通谓词才必须带前缀 —— 见那边的注释）。
	/// </summary>
	public static class CarryAnimConditions
	{
		public const string Moving = "carry-moving";

		/// <summary>复用被绑者那台已登记的时刻名（没有就补一个，避免注册顺序依赖）。</summary>
		public static void RegisterAll()
		{
			AnimConditions.Register(Moving, c =>
			{
				CarryAnimContext k = c as CarryAnimContext;
				return k != null && k.Moving;
			});
			if (!AnimConditions.TryGet(BindAnimConditions.CarryTrigger, out _))
			{
				AnimConditions.Register(BindAnimConditions.CarryTrigger, c => false);
			}
			if (!AnimConditions.TryGet(BindAnimConditions.PutTrigger, out _))
			{
				AnimConditions.Register(BindAnimConditions.PutTrigger, c => false);
			}
		}
	}

	/// <summary>
	/// **扛人者动画状态机**（2026-10-10 立，照 `GrappleAnimMachine` 的三件套写法）。
	///
	/// 🔴 **"什么时候播哪条"的规则不在代码里** —— 在
	/// [`ModuleData/statemachines/carry.xml`](../../../ModuleData/statemachines/carry.xml)：
	/// 四个状态（扛起 / 扛行 / 扛走 / 放下）+ 全部转移边。**改那个文件不用重编译**。
	///
	/// 🔴 **扛人者不一定是玩家**：这台机对任何 Agent 都成立（`GrappleCarry.Begin(carrier, carried)`），
	///    将来 NPC 扛人直接复用（铁律 18 平权）。所以这里**不许出现"玩家"字样**。
	///
	/// 🔴 **玩家扛着人时腿归引擎的移动层**（"边走边挥刀腿照走"就是它）——
	///    本机把姿态写在 0 号通道，腿由移动层按速度挑 ⇒ 读作"手在抱人、腿照走"。见
	///    [Knowledge/骑砍2动画通道与上下半身分层.md](../../../Knowledge/骑砍2动画通道与上下半身分层.md)。
	/// </summary>
	public static class CarryAnimMachine
	{
		/// <summary>注册名（<see cref="AnimMachineRegistry.Create"/> 用它取）。</summary>
		public const string Name = "carry";

		/// <summary>定义文件名（文件名 = 机器名）。</summary>
		public const string FileName = "carry.xml";

		/// <summary>定义文件所在子目录（相对 `ModuleData/`）。</summary>
		public const string SubDir = "statemachines";

		/// <summary>默认过渡时长（秒）——没写 `blend=` 的边用它。热调口子：`custom.grapple carry animblend &lt;秒&gt;`。</summary>
		public static float AnimBlendIn = 0.25f;

		/// <summary>给姿态动作带的优先级（写进 `additionalFlags`；真正生效的是 clip 元数据里的 Priority）。</summary>
		public static int ActionPriority = 0;

		/// <summary>注册进注册表（幂等：重名会覆盖，方便热改）。</summary>
		public static void Register()
		{
			CarryAnimConditions.RegisterAll();

			string path = System.IO.Path.Combine(BindAnimMachine.ModuleRoot, "ModuleData", SubDir, FileName);
			AnimMachineDef def = AnimMachineLoader.Load(path, out string error);
			if (def == null)
			{
				DebugLogger.Log("[Grapple] 扛人者状态机未注册（定义有问题，见上面的 [Anim] 报错）—— "
								+ "扛人机制照常，只是扛人者没有姿态动画");
				return;
			}

			def.DefaultBlend = () => AnimBlendIn;
			def.ActionPriority = () => ActionPriority;

			AnimMachineRegistry.Register(def);
		}
	}
}
