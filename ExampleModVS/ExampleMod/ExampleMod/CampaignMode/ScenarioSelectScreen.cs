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

		/// <summary>本屏代表哪个时代（选人屏 [决定] 后要用它开战役）。</summary>
		private EraCatalog.Era _pendingEra;

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
			_vm = new ScenarioSelectVM(Close, OnEraPicked, OnRecommended, OnCustomHero);
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
		/// 选中剧本 → **先选人**（2026-09-11 改造：选人提到建世界之前）。
		/// 选完人按 [决定] 才开战役 —— 于是 loading 变成"加载我选好的这一局"，
		/// 而不是"先干等 loading，完了才弹选人框"。
		/// </summary>
		private void OnEraPicked(EraCatalog.Era era)
		{
			if (era == null)
			{
				return;
			}
			_pendingEra = era;
			EraCatalog.SelectedEraId = era.Id;
			DebugLogger.Log($"[EraCatalog] 已选剧本：{era.Id}（{era.Year}）→ 先选人（建世界之前）");
			HeroSelectScreen.Open(ParseYear(era.Year), false, OnHeroDecided);
		}

		/// <summary>
		/// 「推荐」→ 该时代的推荐人物列表（太阁5 原版流程：剧本页底部按钮，一步到位）。
		/// 🔴 **固定 1560**（<see cref="EraCatalog.RecommendedEra"/>），不跟随左侧当前选中的剧本——
		///   推荐人名单是 1560 那五个人（用户 2026-09-10 裁定）。
		/// </summary>
		private void OnRecommended()
		{
			EraCatalog.Era era = EraCatalog.RecommendedEra;
			if (era == null)
			{
				return;
			}
			_pendingEra = era;
			EraCatalog.SelectedEraId = era.Id;
			DebugLogger.Log($"[EraCatalog] 已点「推荐」→ {era.Id}（{era.Year}）· 推荐人物列表");
			HeroSelectScreen.Open(ParseYear(era.Year), true, OnHeroDecided);
		}

		/// <summary>
		/// 「自定义人物」→ 不走选人，直接用**当前选中的剧本**开世界（世界建好后照旧走建号流程）。
		/// 入口从选人界面上挪到这里——建号本来就不需要选人。
		/// </summary>
		private void OnCustomHero()
		{
			EraCatalog.Era era = _vm?.GetSelectedEra() ?? EraCatalog.RecommendedEra;
			if (era == null)
			{
				return;
			}
			StartingHero.ClearPending();      // 防上一局的待选残留
			EraCatalog.SelectedEraId = era.Id;
			DebugLogger.Log($"[EraCatalog] 「自定义人物」→ {era.Id}（{era.Year}）· 走建号流程");
			Close();
			EraCatalog.StartCampaign(era.Id);
		}

		/// <summary>
		/// 选人屏按了 [决定] → 关掉本屏（选人屏已自退）→ 开战役。
		/// 🔴 顺序要紧：<c>StartNewGame</c> 会替换主菜单屏，两个界面都必须先退出。
		/// </summary>
		private void OnHeroDecided(string heroId)
		{
			EraCatalog.Era era = _pendingEra;
			if (era == null)
			{
				DebugLogger.Log("[EraCatalog] [决定] 回调时没有待开时代——不开局（异常路径）");
				return;
			}
			DebugLogger.Log($"[EraCatalog] 已定人（{heroId}）→ 开战役 {era.Id}（世界建好后直接落地）");
			Close();
			EraCatalog.StartCampaign(era.Id);
		}

		private static int ParseYear(string year)
		{
			return int.TryParse(year, out int v) ? v : 0;
		}
	}
}
