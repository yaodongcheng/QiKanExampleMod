using HarmonyLib;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 大地图边界诊断日志（通用基座，2026-09-08 日本图相机「空气墙」排雷）。
	///
	/// 症状：日本图里相机/WASD 移到京都以东就动不了——像是空气墙。
	/// 机制实锤（反编译）：
	///   相机目标位置每帧被钳进 [Campaign.MapMinimumPosition, Campaign.MapMaximumPosition]
	///   （SandBox.View.dll ComputeMapCamera），而这两个值来自
	///   SandBox.MapScene.GetMapBorders —— 读场景里两个名字叫 border_min / border_max 的实体。
	///   Taikou Main_map 最初缺这两个实体：
	///     1.2.12：引擎兜底 min=(0,0) max=(900,900) → 相机墙在 x=900（京都 x=969 恰好在墙外，症状吻合）
	///     1.5.x： 无兜底、直接解引用 → 缺实体 = 进图崩溃
	///   修复 = 场景 XML 补齐 border_min(0,0,0) / border_max(2048,1280,1000)（地形 16×10 节点 × 128m = 2048×1280）。
	///
	/// 本补丁 = 每次战役地图加载打一行关键日志，验证边界值是否正常；还能识别
	/// 1.2.12 的引擎兜底值 (0,0)/(900,900)（= 场景又缺实体的信号）。
	/// 版本：方法名字符串运行期解析（1.2.12/1.5.x 二进制 grep 均命中）；方法改名 = 静默跳过,
	///   以 [MapBorder] 日志缺失即可察觉。
	/// </summary>
	[HarmonyPatch(typeof(SandBox.MapScene), "GetMapBorders")]
	public static class MapBorderDiagnosticPatch
	{
		// GetMapBorders 在整局游戏里可能被多次调用（部队移动合法性检查也走它），只记第一次。
		private static bool _logged;

		private const float FallbackMinX = 0f;
		private const float FallbackMaxXY = 900f;   // 1.2.12 引擎兜底
		private const float FallbackMaxHeight = 670f;

		[HarmonyPostfix]
		private static void Postfix(ref Vec2 minimumPosition, ref Vec2 maximumPosition, ref float maximumHeight)
		{
			if (_logged)
			{
				return;
			}
			_logged = true;

			bool isFallback =
				minimumPosition.X == FallbackMinX && minimumPosition.Y == FallbackMinX &&
				maximumPosition.X == FallbackMaxXY && maximumPosition.Y == FallbackMaxXY &&
				maximumHeight == FallbackMaxHeight;

			string line = $"[MapBorder] GetMapBorders → min=({minimumPosition.X:F1},{minimumPosition.Y:F1}) " +
				$"max=({maximumPosition.X:F1},{maximumPosition.Y:F1}) height={maximumHeight:F1}";
			if (isFallback)
			{
				line += " ⚠️=引擎兜底值（场景缺 border_min/border_max 实体，相机被钳在 900×900 = 空气墙！）";
			}
			DebugLogger.Log(line);
		}
	}
}
