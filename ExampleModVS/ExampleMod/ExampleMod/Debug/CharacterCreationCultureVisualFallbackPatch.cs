using HarmonyLib;
using System.Linq;
using TaleWorlds.MountAndBlade.GauntletUI.Widgets.CharacterCreation.Culture;

namespace LivingWorldNpcs
{
	/// <summary>
	/// CC 文化阶段"大图/按钮小图"空屏兜底（通用，2026-09-08 第 23 颗雷）。
	///
	/// CharacterCreationCultureVisualBrushWidget.SetCultureVisual(id) 按文化 id 找美术：
	/// 大数据图（UseSmallVisuals=false）→ 4 层 ParallaxItemBrushWidget.SetState(id)——Culture.Banner.Layer.1..4
	/// 每个文化是一个 &lt;Style Name="文化id"&gt;（Native 只有 battania/empire/sturgia/aserai/khuzait/vlandia/nord 7 个）；
	/// 小按钮图（默认 UseSmallVisuals=true）→ SpriteData.GetSprite("CharacterCreation\Culture\" + id)，
	/// 缺失用 blank_culture 占位（引擎自带兜底，不影响）。
	/// 自定义世界（内容包文化 id 无官方视觉状态）→ 大图区只剩边框 = 玩家眼中"图片坏了"。
	/// 修法：Prefix 检测当前文化 id 在 Culture.Banner.Layer.1 是否有匹配 Style；没有 → 替换为官方美术兜底 id。
	/// 通用性：判据 = 状态存在与否（数据驱动，不写死任何内容包文化名）；兜底 id = 原版文化 id（native 美术资源），
	/// 内容包未来自备 4 层艺术时改自建 brush/prefab（T5 日化），本补丁退出。
	/// 版本：方法名 string 运行期解析；1.5.2 已实锤（Widgets.dll 反编译）。1.2.12 机首次编译时若类不存在 =
	/// 编译报错（不会静默跳过，compile-time typeof 保护）；两机同源编译按迁移名单走。
	/// </summary>
	[HarmonyPatch(typeof(CharacterCreationCultureVisualBrushWidget), "SetCultureVisual")]
	public static class CharacterCreationCultureVisualFallbackPatch
	{
		/// <summary>官方七文化美术兜底 id（引擎原生资源；1.5.2 实锤 ——Culture.Banner.Layer.1 的 Style 集含 empire）。</summary>
		private const string FallbackStyleId = "empire";

		[HarmonyPrefix]
		private static bool Prefix(ref string newCultureId, CharacterCreationCultureVisualBrushWidget __instance)
		{
			if (string.IsNullOrEmpty(newCultureId) || newCultureId == FallbackStyleId)
			{
				return true;
			}

			var layer1Brush = __instance.Context.GetBrush("Culture.Banner.Layer.1");
			string cultureId = newCultureId; // ref 参数禁止进 lambda（CS1628）
			bool hasVisualState = layer1Brush != null && layer1Brush.Styles.Any(style => style.Name == cultureId);
			if (!hasVisualState)
			{
				DebugLogger.Log("[CCVisualFallback] 文化 " + newCultureId + " 无 CC 官方视觉状态 → 借用 " + FallbackStyleId);
				newCultureId = FallbackStyleId;
			}
			return true;
		}
	}
}
