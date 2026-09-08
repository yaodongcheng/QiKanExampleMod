using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 🔬 探针（2026-09-08 雷 27 排雷中）：BodyGeneratorView 构造链 NRE 定位。
	/// ctor 内联调用无法从栈帧定位 —— 给链上每个方法打进入日志，崩溃前的最后一条 = 死点。
	/// 定位后删除（探针不留）。
	/// </summary>
	[HarmonyPatch(typeof(BodyGenerator), "InitBodyGenerator")]
	public static class BodyGeneratorProbe_InitBodyGenerator
	{
		[HarmonyPostfix]
		private static void Postfix()
		{
			DebugLogger.Log("[FaceGenProbe] 通过 InitBodyGenerator");
		}
	}

	[HarmonyPatch(typeof(TaleWorlds.MountAndBlade.GauntletUI.BodyGenerator.BodyGeneratorView), "OpenScene")]
	public static class BodyGeneratorProbe_OpenScene
	{
		[HarmonyPrefix]
		private static void Prefix()
		{
			DebugLogger.Log("[FaceGenProbe] 进入 BodyGeneratorView.OpenScene");
		}

		[HarmonyPostfix]
		private static void Postfix()
		{
			DebugLogger.Log("[FaceGenProbe] 通过 OpenScene");
		}
	}

	[HarmonyPatch(typeof(TaleWorlds.MountAndBlade.GauntletUI.BodyGenerator.BodyGeneratorView), "AddCharacterEntity")]
	public static class BodyGeneratorProbe_AddCharacterEntity
	{
		[HarmonyPrefix]
		private static void Prefix()
		{
			DebugLogger.Log("[FaceGenProbe] 进入 AddCharacterEntity");
		}

		[HarmonyPostfix]
		private static void Postfix()
		{
			DebugLogger.Log("[FaceGenProbe] 通过 AddCharacterEntity");
		}
	}
}
