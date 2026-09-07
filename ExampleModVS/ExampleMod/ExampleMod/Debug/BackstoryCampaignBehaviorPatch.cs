using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 卡拉迪亚前史注入屏蔽（通用，2026-09-08 排雷第 4 颗雷）。
	///
	/// BackstoryCampaignBehavior.OnNewGameCreated = 纯卡拉迪亚背景剧情：
	///   1075 年「卢康侮辱事件」/ 1080 年叛乱、谋杀史——硬编码 GetObject&lt;CharacterObject&gt;("lord_1_7")
	///   等 8 个原版领主 + town_V6。自定义 GameType（TaikouCampaign 等）下这些对象不存在
	///   → 首行 .HeroObject 即 NRE（实机 2026-09-08 06:09 崩溃栈）。
	///
	/// 织丰同款（Shokuho.dll 99600 行实锤）：打 RegisterEvents 前缀返回 false ——
	///   整个行为退出事件注册（比只跳过 OnNewGameCreated 更彻底，未来新增事件也一并屏蔽）。
	/// 版本：方法名字符串运行期解析（1.2.12 二进制 grep 验证存在；1.5.x 若方法改名 = 静默跳过，
	///   视 [BackstoryPatch] 日志缺失即可察觉）。
	/// 补充：looters/neutral_culture 文化定义与织丰对齐（见 plan 决策表）——另有任务，不在本类。
	/// </summary>
	[HarmonyPatch(typeof(BackstoryCampaignBehavior), "RegisterEvents")]
	public static class BackstoryCampaignBehaviorPatch
	{
		[HarmonyPrefix]
		private static bool Prefix()
		{
			DebugLogger.Log("[BackstoryPatch] 自定义战役：屏蔽 BackstoryCampaignBehavior（卡拉迪亚前史——织丰同款做法）。");
			return false;
		}
	}
}
