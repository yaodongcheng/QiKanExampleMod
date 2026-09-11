using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 选剧本界面（主菜单点「剧本」打开；配 <c>GUI/Prefabs/ScenarioSelect.xml</c>）。
	/// 🔴 为什么自建界面而不是改主菜单选项列表（2026-09-10 实机教训）：
	///   主菜单列表被 MCM 等 mod 用 Harmony 盯着——MCM 的 <c>RefreshMenuOptions</c> postfix
	///   按固定下标往列表插自己的按钮；我们把列表换短 → 它下标越界
	///   （ArgumentOutOfRangeException @ MCM.UI.Functionality.DefaultGameMenuScreenHandler.RefreshMenuOptionsPostfix）。
	///   自建 Screen 完全绕开那条链，且与"以后正式做 UI"的方向一致。
	/// 打开/关闭 = <c>ScreenManager.PushScreen</c> / <c>PopScreen</c>（主菜单屏留在栈下，返回即还原）。
	/// </summary>
	public class ScenarioSelectScreen : ScreenBase
	{
		private GauntletLayer _layer;
		private ScenarioSelectVM _vm;

		private ScenarioSelectScreen()
		{
		}

		/// <summary>打开界面（主菜单入口调用）。已在选剧本界面时不重复开。</summary>
		public static void Open()
		{
			if (ScreenManager.TopScreen is ScenarioSelectScreen)
			{
				return;
			}
			ScreenManager.PushScreen(new ScenarioSelectScreen());
		}

		protected override void OnInitialize()
		{
			base.OnInitialize();
			_vm = new ScenarioSelectVM(Close, OnEraPicked, OnRecommended);
			_layer = V.NewLayer(200, "LWN_ScenarioSelect");
			V.LoadMov(_layer, "ScenarioSelect", _vm);
			// 全遮罩：界面打开期间屏蔽游戏输入（主菜单屏在栈下，别让它同时响应）
			_layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
			AddLayer(_layer);
			_layer.IsFocusLayer = true;
			ScreenManager.TrySetFocus(_layer);
		}

		protected override void OnFinalize()
		{
			// 🔴 摘层纪律（手册：摘层守卫三件套）——RemoveLayer 会连带 Finalize 层，
			//   禁止二次 Finalize；置空防重复进入本方法。
			if (_layer != null)
			{
				RemoveLayer(_layer);
				_layer = null;
			}
			_vm = null;
			base.OnFinalize();
		}

		/// <summary>返回上一层（主菜单）。</summary>
		private void Close()
		{
			ScreenManager.PopScreen();
		}

		/// <summary>
		/// 选中剧本 → 直接开这一局。
		/// 🔴 **不是立刻进游戏**：世界开始加载 → 建好后由 <c>LivingWorldCampaignGameManager.OnLoadFinished</c>
		///   弹出**选人界面**（那里才有 `Campaign.Current` 可查王国/家族/英雄）。
		/// 顺序要紧：StartNewGame 会替换主菜单屏，本界面必须先退出。
		/// </summary>
		private void OnEraPicked(EraCatalog.Era era)
		{
			if (era == null)
			{
				return;
			}
			EraCatalog.SelectedEraId = era.Id;
			DebugLogger.Log($"[EraCatalog] 已选剧本：{era.Id}（{era.Year}）→ 加载世界（建好后弹选人界面）");
			Close();
			EraCatalog.StartCampaign(era.Id);
		}

		/// <summary>
		/// 「推荐」→ 直接进该时代的推荐人物列表（太阁5 原版流程：剧本页底部按钮，一步到位）。
		/// 🔴 **固定 1560**（<see cref="EraCatalog.RecommendedEra"/>），不跟随左侧当前选中的剧本——
		///   推荐人名单是 1560 那五个人（用户 2026-09-10 裁定）。
		/// 世界建好后由 <c>LivingWorldCampaignGameManager</c> 弹**推荐模式**的选人界面（只列那 5 人）。
		/// </summary>
		private void OnRecommended()
		{
			EraCatalog.Era era = EraCatalog.RecommendedEra;
			if (era == null)
			{
				return;
			}
			EraCatalog.SelectedEraId = era.Id;
			HeroSelectOverlay.RequestRecommended();
			DebugLogger.Log($"[EraCatalog] 已点「推荐」→ {era.Id}（{era.Year}）· 推荐人物列表");
			Close();
			EraCatalog.StartCampaign(era.Id);
		}
	}
}
