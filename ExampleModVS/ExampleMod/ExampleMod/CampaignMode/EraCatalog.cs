using System;
using System.Collections.Generic;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 时代（剧本）清单——**加时代只改这一个文件**。
	/// 每项的 Id 必须与另外两处**逐字一致**：
	///   ① 该时代的战役类名 ↔ <c>CampaignMode/TaikouCampaign&lt;年份&gt;.cs</c>
	///   ② 同上 ↔ <c>Taikou/SubModule.xml</c> 的 &lt;GameType value="…"/&gt;
	/// （三处不一致 = 该时代的 XML 段一个都加载不到，静默空世界。）
	/// 三处一致性由离线检查 <c>Scripts/check_era_segments.py</c> 常驻守。
	/// </summary>
	public static class EraCatalog
	{
		/// <summary>一个时代 = 选剧本界面上的一个条目。</summary>
		public sealed class Era
		{
			/// <summary>时代 id = 战役类名 = GameType（如 "TaikouCampaign1560"）。</summary>
			public string Id { get; }

			/// <summary>脚本年份（界面按钮上的大字，如 "1560"）。</summary>
			public string Year { get; }

			/// <summary>剧本卷名（界面按钮上的小字，如 "日轮之卷"）。</summary>
			public TextObject VolumeName { get; }

			/// <summary>右侧大图的 sprite 名（留空 = 界面画占位块）。</summary>
			public string PreviewSprite { get; }

			public Era(string id, string year, string volumeNameKey, string previewSprite = null)
			{
				Id = id;
				Year = year;
				VolumeName = new TextObject(volumeNameKey);
				PreviewSprite = previewSprite;
			}
		}

		/// <summary>
		/// 全部时代。顺序 = 界面显示顺序。
		/// 🔴 加时代三步：①这里加一行 ②LWN 加一个 thin 战役类（类名 = Id）
		///   ③ SubModule.xml 给该时代的差异段注册对应 GameType（共用段追加即可）。
		/// </summary>
		public static readonly IReadOnlyList<Era> All = new List<Era>
		{
			// fallback 写**要显示的文字**：本地化查不到时引擎回落显示它（写成键名 = 界面上印键名，实机踩过）
			new Era("TaikouCampaign1554", "1554", "{=LWN_scenario_1554}The Tangled Thread"),
			new Era("TaikouCampaign1560", "1560", "{=LWN_scenario_1560}The Wheel of Fate"),
			new Era("TaikouCampaign1568", "1568", "{=LWN_scenario_1568}The Rising Dragon"),
			new Era("TaikouCampaign1575", "1575", "{=LWN_scenario_1575}The Way of Supremacy"),
			new Era("TaikouCampaign1582", "1582", "{=LWN_scenario_1582}The Turning Point"),
			new Era("TaikouCampaign1598", "1598", "{=LWN_scenario_1598}The Great Peace"),
		};

		/// <summary>时代数量（菜单入口是否出现的判据）。</summary>
		public static int Count => All.Count;

		/// <summary>
		/// 「推荐」入口固定的时代 = **1560**（太阁5 的推荐人名单绑 1560 年；2026-09-10 用户裁定
		/// 「点推荐固定进 1560，不论在选剧本界面选中的是哪个剧本」）。
		/// 按年份找、找不到就退回第一档（**不硬编码类名**——加时代时不用改这里）。
		/// </summary>
		public static Era RecommendedEra
		{
			get
			{
				foreach (Era e in All)
				{
					if (e.Year == "1560")
					{
						return e;
					}
				}
				return Count > 0 ? All[0] : null;
			}
		}

		/// <summary>
		/// 玩家在选剧本界面定下的时代（后续步骤——选人界面/建号——都从它取）。
		/// 时序：选剧本界面写入 → 选人界面消费（开局或走自定义建号）。
		/// </summary>
		public static string SelectedEraId { get; set; }

		/// <summary>
		/// 当前时代的**年份**（1560 之类；时代未选定 = 0）。
		/// 🔴 为什么不用 `CampaignTime.Now.GetYear`：那个是**开局以来经过的年数**
		///   （反编译实锤：`_numTicks / 72576000000L`，全新存档 = 0），
		///   不是历史年份。界面要显示「25 岁」这类**史实年龄**，必须用时代年份算。
		/// 写入点：<see cref="StartCampaign"/>（选剧本与「推荐」两条路都经过它）。
		/// </summary>
		public static int SelectedEraYear { get; private set; }

		/// <summary>时代的年份（"1560" → 1560；解析失败 = 0）。</summary>
		private static int ParseYear(string year)
		{
			return int.TryParse(year, out int v) ? v : 0;
		}

		/// <summary>
		/// 主菜单上的入口按钮（点它 = 打开选剧本界面，**不动主菜单选项列表**）。
		/// 时代为空时返回 null（调用方据此不加入口）。
		/// </summary>
		public static InitialStateOption BuildEntryOption()
		{
			if (Count == 0)
			{
				return null;
			}
			return new InitialStateOption(
				"LivingWorldScenarioEntry",
				// fallback 必须是**要显示的文字**（见上）
				new TextObject("{=LWN_scenario_menu_entry}Scenarios"),
				2,
				OpenSelectScreen,
				() => new ValueTuple<bool, TextObject>(false, null),
				null);
		}

		/// <summary>
		/// 打开选剧本界面。
		/// 🔴 为什么自建界面而不是改主菜单选项列表：主菜单列表被 MCM 等 mod 用 Harmony 盯着
		///   （MCM 的 RefreshMenuOptions postfix 按固定下标插自己的按钮）——换短列表会让它下标越界崩
		///   （实机 2026-09-10 ArgumentOutOfRangeException @ MCM.UI...RefreshMenuOptionsPostfix）。
		///   自建 Screen 完全绕开那条链，且与"以后正式做 UI"的方向一致。
		/// </summary>
		private static void OpenSelectScreen()
		{
			ScenarioSelectScreen.Open();
		}

		/// <summary>
		/// 开一局该时代的战役（**选人开局**路径：世界建好后由 GameManager 把玩家换成所选英雄，不走建号）。
		/// </summary>
		public static void StartCampaign(string eraId)
		{
			if (string.IsNullOrEmpty(eraId))
			{
				DebugLogger.Log("[EraCatalog] StartCampaign 拿到空时代 id —— 不开局");
				return;
			}
			// 记下年份供界面算史实年龄（见 SelectedEraYear 注释）
			foreach (Era e in All)
			{
				if (e.Id == eraId)
				{
					SelectedEraYear = ParseYear(e.Year);
					break;
				}
			}
			MBGameManager.StartNewGame(new LivingWorldCampaignGameManager(eraId));
		}

		/// <summary>
		/// 开一局该时代的战役，并**保留原建号流程**（「自定义英雄」路径：捏脸/出身/加点那一套）。
		/// </summary>
		public static void StartCampaignWithCharacterCreation(string eraId)
		{
			StartCampaign(eraId);
		}
	}
}
