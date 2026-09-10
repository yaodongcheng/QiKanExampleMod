using System;
using System.Collections.Generic;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 通用战役模式（自定义 GameType）的激活器。
	/// 🔴 双模式开关（2026-09-07 用户裁定，CLAUDE.md 拆分架构铁则 5）：
	///   - 内容包（Taikou）已加载 → 接通用战役模式（主菜单接线，本文件 WireMainMenu）；
	///   - 内容包未加载 → 纯功能包（什么都不做，LWN 以原版战役扩展形态存在）。
	/// 判断为运行时（ModuleHelper.GetModuleInfo），非编译分叉——同一个 dll 服务两种玩家。
	/// 🔴 版本：自定义战役 API 在 1.2.12（MBGameManager.DoLoading + CharacterCreationContentBase）
	///   与 1.5.1（GameManagerBase.DoLoadingForGameManager + CharacterCreationManager 新体系）之间
	///   为结构性差异 → 1.2.12 = 完整战役模式（织丰母本签名，唯一运行目标机）；1.5.1 = 暂不接入
	///   （该版本不装 Taikou 数据包，天然纯功能包）。仅在内容包存在时两者行为才分叉。
	/// </summary>
	public static class CampaignModeActivator
	{
		/// <summary>已加载的内容包名（当前仅 Taikou；三国扩展在此追加）。null = 纯功能包模式。</summary>
		public static string ActiveContentPack => IsModuleLoaded("Taikou") ? "Taikou" : null;

		/// <summary>
		/// 模块是否在本次启动的启用列表中。
		/// 🔴 判据 = ModuleActivationHelper.IsModuleEnabled（引擎启用列表 Utilities.GetModulesNames()，
		///    与 LoadSubModules 装配 DLL 同一来源——1.2.12 ~ 1.5.1 四版本同签名）。
		/// ⚠️ 不能用 ModuleHelper.GetModuleInfo(id) != null：1.2.12 它查「物理安装目录扫描表」
		///    （文件夹存在即返回，与勾选无关），装而未勾选会误判 → 误进战役模式接线崩 IndexOutOfRange（2026-09-07）。
		/// ⚠️ 也不能用 info.IsSelected：反编译实证——游戏本体进程内 IsSelected 唯一写点 =
		///    LoadWithFullPath 的 `IsSelected = IsNative`（ModuleManager.dll:381）；launcher 勾选状态是经
		///    命令行 _MODULES_ 传 C++ 引擎的（Launcher.Library:1108-1113），不会写回游戏本体的 ModuleInfo
		///    → 非 Native 模块恒 false，勾上 Taikou 也永远进不了战役模式。
		/// </summary>
		public static bool IsModuleLoaded(string moduleId)
		{
			return ModuleActivationHelper.IsModuleEnabled(moduleId);
		}

		/// <summary>内容包就绪时，把主菜单从"原版战役入口"换成"通用战役入口"（织丰式接线，1.2.12 实读母本）。</summary>
		public static void TryActivateCampaignMode()
		{
			string pack = ActiveContentPack;
			if (pack == null)
			{
				TaleWorlds.Library.Debug.Print("[LivingWorldNpcs] CampaignMode: 内容包未加载——保持纯功能包模式（玩原版战役）。");
				return;
			}
			TaleWorlds.Library.Debug.Print($"[LivingWorldNpcs] CampaignMode: 内容包 {pack} 已加载——激活通用战役模式。");
			WireMainMenu();
		}

		private static void WireMainMenu()
		{
			// 1) 剔除原版战役按钮（沙盒新游戏 / 剧情新游戏 / 继续战役），其余（战斗/编队等）保留
			var kept = new List<InitialStateOption>();
			foreach (InitialStateOption item in Module.CurrentModule.GetInitialStateOptions())
			{
				if (item.Id != "SandBoxNewGame" && item.Id != "StoryModeNewGame" && item.Id != "ContinueCampaign")
				{
					kept.Add(item);
				}
			}
			Module.CurrentModule.ClearStateOptions();
			foreach (InitialStateOption item in kept)
			{
				Module.CurrentModule.AddInitialStateOption(item);
			}

			// 2) 「剧本」入口 → 打开**自建选剧本界面**（ScenarioSelectScreen）。
			// 🔴 时代必须在**主菜单层**决定（时序硬约束，见 plans/时代剧本切换-验证.md）：
			//   引擎在「new 出战役类」时才按类名过滤 XML 段，主菜单之后没有改的余地。
			//   界面而不是改主菜单选项列表：列表被 MCM 等 mod 的 Harmony 盯着（实机崩溃教训，
			//   见 ScenarioSelectScreen 类注释）。时代清单在 EraCatalog（加时代只改那一个文件）。
			InitialStateOption scenarioEntry = EraCatalog.BuildEntryOption();
			if (scenarioEntry != null)
			{
				Module.CurrentModule.AddInitialStateOption(scenarioEntry);
			}

			// 3) 继续战役：v0 读档路径未实现，禁用
			Module.CurrentModule.AddInitialStateOption(new InitialStateOption(
				"LivingWorldContinueGame",
				new TextObject("{=LWN_campaign_continue}Continue Campaign"),
				2,
				() => { },
				() => new ValueTuple<bool, TextObject>(true, new TextObject("{=LWN_campaign_continue_disabled}Save-loading is not implemented yet.")),
				null));
		}
	}
}
