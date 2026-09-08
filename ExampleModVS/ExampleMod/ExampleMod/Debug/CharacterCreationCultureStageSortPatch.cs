using HarmonyLib;
using TaleWorlds.CampaignSystem.ViewModelCollection.CharacterCreation;

namespace LivingWorldNpcs
{
	/// <summary>
	/// CC 文化阶段"摆拍排序"跳过（通用，2026-09-08 第 14 颗雷）。
	///
	/// CharacterCreationCultureStageVM.SortCultureList 用 `Single(x => CultureID.Contains("vlan"/"stur"/
	/// "empi"/"aser"/"khuz"))` 把原版六大文化按固定顺序摆拍——自定义 GameType 下这几个原版文化
	/// 不存在 → Single 抛 "Sequence contains no matching element" → CC 界面启动即崩。
	/// 修法：Prefix 整体跳过（自定义世界文化排序无意义；引擎默认序显示即可——1 文化世界更无需排）。
	/// 版本：方法名/所属 VM 类 string 运行期解析（1.2.12 已实锤；1.5.x 若 VM 重构 = 静默跳过无日志，首测留意）。
	/// </summary>
	[HarmonyPatch(typeof(CharacterCreationCultureStageVM), "SortCultureList")]
	public static class CharacterCreationCultureStageSortPatch
	{
		[HarmonyPrefix]
		private static bool Prefix()
		{
			DebugLogger.Log("[CCSortPatch] 跳过 SortCultureList（自定义世界无原版六大文化摆拍对象）。");
			return false;
		}
	}
}
