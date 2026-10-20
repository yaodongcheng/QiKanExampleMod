using LivingWorldNpcs.Animation;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **被处决者动画状态机**（2026-10-19 立）—— 定义在 `ModuleData/statemachines/executed.xml`。
	///
	/// 为什么要有它：处决不是"播一条一次性 clip 就完"——演完之后**躺地循环还是起身**必须是一个状态，
	/// 否则只有 `kill_defender` / 定格两种结果。本机把后处置态做成数据：
	///   受击（act_executed02，一次性）→ 躺地（循环）→ 起身（一次性）→ outside
	///
	/// 驱动 = `PairActionPlayback.MachinePairDriver`（通用状态机驱动，按 `machine:executed` 找它）；
	/// 进入/退出用 `PairActions.csv` 的 `EnterEvent=exec-hit` / `ExitEvent=exec-rise`。
	///
	/// ⚠️ `躺地` / `起身` 目前**指向被绑套件的占位 clip**（`binded_lay_loop` / `binded_lay_end`，
	///    见 action_sets.xml 的注释）；等专门的"被处决躺地/起身"clip 出来，只换 action_set 的指向。
	/// </summary>
	public static class ExecutedAnimMachine
	{
		public const string Name = "executed";
		public const string FileName = "executed.xml";
		public const string SubDir = "statemachines";

		/// <summary>进入"受击"的时刻名（与 PairActions.csv 的 EnterEvent 对齐）。</summary>
		public const string HitTrigger = "exec-hit";

		/// <summary>从"躺地"进"起身"的时刻名（与 PairActions.csv 的 ExitEvent 对齐）。</summary>
		public const string RiseTrigger = "exec-rise";

		/// <summary>默认过渡时长（秒）。</summary>
		public static float AnimBlendIn = 0.2f;

		/// <summary>注册进注册表（幂等：重名会覆盖，方便热改）。</summary>
		public static void Register()
		{
			// 事件 token 也要在条件表里登记（装载器对 event 边同样查名字；返回值从不在 Tick 里用）。
			AnimConditions.Register(HitTrigger, c => false);
			AnimConditions.Register(RiseTrigger, c => false);

			string path = System.IO.Path.Combine(BindAnimMachine.ModuleRoot, "ModuleData", SubDir, FileName);
			AnimMachineDef def = AnimMachineLoader.Load(path, out string error);
			if (def == null)
			{
				DebugLogger.Log("[Pair] 被处决者状态机未注册（定义有问题，见上面的 [Anim] 报错）—— "
								+ "处决仍能播 clip，但没有躺地/起身后处置态");
				return;
			}

			def.DefaultBlend = () => AnimBlendIn;
			def.ActionPriority = () => 0;
			AnimMachineRegistry.Register(def);
		}
	}
}
