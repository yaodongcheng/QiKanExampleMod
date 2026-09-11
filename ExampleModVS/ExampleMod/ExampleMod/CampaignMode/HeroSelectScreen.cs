using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 选人界面（**建世界之前**，配 <c>GUI/Prefabs/HeroSelect.xml</c> + <c>HeroDetail.xml</c>）。
	/// 流程：选剧本页 → 点剧本名 / 点「推荐」→ 本屏 → 点人名 → 详情页 → [决定] → 才开战役。
	///
	/// 🔴 **为什么能放在建世界之前**（2026-09-11 改造）：本屏与两个 VM 只读**静态表**
	///   （<see cref="HeroCatalogRegistry"/> 选人目录 + <see cref="HeroProfileRegistry"/> 画像表 + 立绘表），
	///   **不碰 `Campaign.Current`**——所以不必等世界建好（原实现挂在世界建好后的覆盖层上，
	///   玩家要先干等一个长 loading 才轮得到选人）。
	///
	/// 🔴 **一个屏、两个 prefab 互切**：列表 = <c>HeroSelect</c>，详情 = <c>HeroDetail</c>，
	///   同一个 <see cref="GauntletLayer"/> 上 `LoadMovie` 换片；**不新增层**（层序/焦点敏感）。
	/// 🔴 承载方式与 <see cref="ScenarioSelectScreen"/> 一致（主菜单阶段 `PushScreen` 自建屏）——
	///   **不要**改成"挂到 TopScreen 的层"：那是世界建好后才画得出来的做法（见 HeroSelectOverlay 的注释）。
	/// </summary>
	public class HeroSelectScreen : ScreenBase
	{
		private enum View
		{
			List,
			Detail,
		}

		private GauntletLayer _layer;
		private HeroSelectVM _listVm;
		private HeroDetailVM _detailVm;
		private View _view = View.List;

		private int _year;
		private bool _recommended;
		private Action<string> _onDecided;      // 由选剧本屏给：关掉两层 + 开战役

		private HeroSelectScreen()
		{
		}

		/// <summary>
		/// 打开选人界面。
		/// <paramref name="year"/> = 该时代的年份（取目录用）；<paramref name="recommended"/> = 只列推荐五人；
		/// <paramref name="onDecided"/> = 玩家在详情页按了 [决定] 时回调（参数 = 英雄 StringId）。
		/// </summary>
		public static void Open(int year, bool recommended, Action<string> onDecided)
		{
			if (ScreenManager.TopScreen is HeroSelectScreen)
			{
				return;
			}
			var screen = new HeroSelectScreen
			{
				_year = year,
				_recommended = recommended,
				_onDecided = onDecided,
			};
			ScreenManager.PushScreen(screen);
		}

		/// <summary>本屏是否正开着（重复打开保护）。</summary>
		public static bool IsOpen => ScreenManager.TopScreen is HeroSelectScreen;

		protected override void OnInitialize()
		{
			base.OnInitialize();
			_listVm = new HeroSelectVM(_year, Close, OnHeroChosen, _recommended);
			_layer = V.NewLayer(200, "LWN_HeroSelect");
			V.LoadMov(_layer, "HeroSelect", _listVm);
			// 全遮罩：界面打开期间屏蔽游戏输入（主菜单屏在栈下，别让它同时响应）
			_layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
			AddLayer(_layer);
			_layer.IsFocusLayer = true;
			ScreenManager.TrySetFocus(_layer);
			_view = View.List;
			DebugLogger.Log($"[HeroSelect] 选人屏已开（年份 {_year}，{(_recommended ? "推荐模式" : "树模式")}）");
		}

		protected override void OnFinalize()
		{
			// 🔴 摘层纪律：RemoveLayer 会连带 Finalize 层，禁止二次；置空防重复进入本方法
			if (_layer != null)
			{
				RemoveLayer(_layer);
				_layer = null;
			}
			_listVm = null;
			_detailVm = null;
			base.OnFinalize();
		}

		/// <summary>[返回]（列表页）→ 回上一层（选剧本页）。</summary>
		private void Close()
		{
			ScreenManager.PopScreen();
		}

		/// <summary>点人名 → 换片到角色详情页（不再直接开局）。</summary>
		private void OnHeroChosen(string heroId)
		{
			if (_layer == null || _view == View.Detail)
			{
				return;                     // 已在详情页（连点两下）→ 不重复换片
			}
			try
			{
				_detailVm = new HeroDetailVM(heroId, _year, OnDetailBack, () => OnDetailConfirm(heroId));
				V.LoadMov(_layer, "HeroDetail", _detailVm);
				_view = View.Detail;
				DebugLogger.Log($"[HeroSelect] 打开角色详情页：{heroId}");
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[HeroSelect] 详情页打开失败（留在列表）：{ex.GetType().Name} {ex.Message}");
				_detailVm = null;
			}
		}

		/// <summary>详情页 [返回] → 换片回选人列表（列表 VM 没重建，选中态自然保留）。</summary>
		private void OnDetailBack()
		{
			if (_layer == null || _listVm == null)
			{
				return;
			}
			try
			{
				V.LoadMov(_layer, "HeroSelect", _listVm);
				_view = View.List;
				_detailVm = null;
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[HeroSelect] 返回列表失败：{ex.GetType().Name} {ex.Message}");
			}
		}

		/// <summary>
		/// 详情页 [决定] → 记下要扮演的英雄，关掉本屏，交给选剧本屏开战役。
		/// 🔴 **这里不开战役**：开战役会替换主菜单屏，必须先把选人屏与选剧本屏都退掉（顺序要紧）。
		/// </summary>
		private void OnDetailConfirm(string heroId)
		{
			StartingHero.SetPending(heroId);
			DebugLogger.Log($"[HeroSelect] 已选定开局英雄：{heroId}（关界面 → 建世界后落地）");
			ScreenManager.PopScreen();      // 先退本屏
			_onDecided?.Invoke(heroId);     // 再让选剧本屏退掉自己并开战役
		}
	}
}
