using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 选人**界面覆盖层**（不是自建 Screen——见下方"为什么"）。
	/// 流程：主菜单 →「剧本」→ 选剧本 → 建世界 → 世界建好后挂上本层 → 玩家选人 →
	///   选了英雄 = 魂穿 + 走完建号收尾（进图）；点「自定义人物」= 走原建号流程。
	///
	/// 🔴 **为什么不用 ScreenBase + PushScreen（2026-09-10 实机两次踩坑）**：
	///   推入的屏在这个时点（世界刚建好、引擎的 GameLoadingState 仍活着）**画不出来**——
	///   实测屏在栈顶、IsActive=True、层 IsActive=True、焦点也拿到了，但屏幕上一个像素都没有。
	///   本仓库所有能正常显示的界面（AgentHud / IM / 相机调试）**无一例外**都是
	///   「拿 `ScreenManager.TopScreen` + `AddLayer`」——即**挂到引擎现成的屏上**，不自己造屏。
	///   故本类采用同一做法。
	///
	/// 🔴 **时点硬约束**：必须等世界建好（`Campaign.Current` 就绪）才能显示——
	///   界面要读 `Kingdom.All` / `Clan.Heroes`，世界之前读 = `Kingdom.get_All()` NRE（已踩）。
	///   所以由 <c>LivingWorldCampaignGameManager.OnLoadFinished</c> 调 <see cref="Request"/>，再由 Tick 挂层。
	/// </summary>
	public static class HeroSelectOverlay
	{
		private static GauntletLayer _layer;
		private static ScreenBase _host;      // 层挂在哪个屏上（ScreenLayer 没有公开的 owner 属性，自己记）
		private static HeroSelectVM _vm;
		private static bool _shown;
		private static bool _pending;         // 已请求显示、还没挂上（等屏就绪）
		private static int _retryTicks;

		/// <summary>本层是否正在显示。</summary>
		public static bool IsShown => _shown;

		/// <summary>请求显示选人界面（世界建好后调用，幂等）。真正的挂层在 <see cref="Tick"/> 里完成。</summary>
		public static void Request()
		{
			if (_shown || _pending)
			{
				return;
			}
			_pending = true;
			_retryTicks = 0;
			DebugLogger.Log("[HeroSelect] 已请求显示选人界面（等屏就绪后挂层）");
		}

		/// <summary>
		/// 每帧调用（由 SubModule 的 OnApplicationTick 驱动）：屏一就绪就把层挂上。
		/// 🔴 为什么不能一次性挂（2026-09-10 实机教训）：世界建好那一刻，引擎的屏栈正在换代，
		///   `ScreenManager.TopScreen` 可能是 null 或其上的屏马上被替换——一次性挂会挂空，
		///   而且**静默失败**（界面不出现、日志一片空白，排查极难）。
		///   故改为「待办 + 每帧重试」：前 600 帧每帧试，之后降为每 30 帧一次（省开销，不停试）。
		/// </summary>
		public static void Tick()
		{
			if (!_pending || _shown)
			{
				return;
			}
			_retryTicks++;
			if (_retryTicks > 600 && _retryTicks % 30 != 0)
			{
				return;                     // 低频重试档
			}

			ScreenBase host = ScreenManager.TopScreen;
			if (host == null)
			{
				return;                     // 屏栈换代空档 → 下一帧再试
			}

			try
			{
				_vm = new HeroSelectVM(OnBack, OnHeroPicked, OnCustomHero);
				_layer = V.NewLayer(200, "LWN_HeroSelect");
				V.LoadMov(_layer, "HeroSelect", _vm);
				_layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
				host.AddLayer(_layer);
				_host = host;
				// 🔴 不设 IsFocusLayer / 不 TrySetFocus——对齐本仓库能工作的范本（AgentHud / IM 都只 AddLayer）。
				//   抢焦点反而会和引擎当前的焦点管理（loading/地图屏）打架。
				_shown = true;
				_pending = false;
				DebugLogger.Log($"[HeroSelect] 选人层已挂到 {host.GetType().Name}（层数={host.Layers.Count}，等待 {_retryTicks} 帧）");
			}
			catch (Exception ex)
			{
				// 挂层失败：立刻放弃并记日志（不无限重试——多半是数据层问题，重试也没用）
				DebugLogger.Log($"[HeroSelect] 挂层失败：{ex.GetType().Name} {ex.Message}\n{ex.StackTrace}");
				_pending = false;
				Teardown();
			}
		}

		/// <summary>摘层（换人收尾 / 走建号前调用；幂等）。</summary>
		public static void Teardown()
		{
			_shown = false;
			_pending = false;
			if (_layer != null)
			{
				// 摘层守卫：RemoveLayer 会连带 Finalize 层，禁止二次
				try
				{
					if (!_layer.Finalized && _host != null)
					{
						_host.RemoveLayer(_layer);
					}
				}
				catch (Exception ex)
				{
					DebugLogger.Log($"[HeroSelect] 摘层失败（不致命）：{ex.Message}");
				}
				_layer = null;
			}
			_host = null;
			_vm = null;
		}

		/// <summary>返回 → 改走建号流程（想自己捏人就从这里走）。</summary>
		private static void OnBack()
		{
			OnCustomHero();
		}

		/// <summary>
		/// 选中英雄 → 就地换人（魂穿）+ 走完引擎的建号收尾（推入大地图等）。
		/// 🔴 先摘层再落地：收尾会 `CleanAndPushState(MapState)` 清掉屏栈，留着旧层会挂到已销毁的屏上。
		/// </summary>
		private static void OnHeroPicked(Hero hero)
		{
			Teardown();
			if (hero != null && StartingHero.ApplyPending(hero))
			{
				DebugLogger.Log($"[HeroSelect] 选人开局完成：{hero.StringId}");
				return;
			}
			DebugLogger.Log("[HeroSelect] 换人失败——回退到建号流程");
			FallbackToCharacterCreation();
		}

		/// <summary>「自定义人物」→ 走原建号流程（玩家自己捏脸/选出身/加点）。</summary>
		private static void OnCustomHero()
		{
			Teardown();
			FallbackToCharacterCreation();
		}

		/// <summary>
		/// 进原建号流程（「自定义人物」/ 返回 / 换人失败都走这里）。
		/// 🔴 推的是**全新**的建号状态（CleanAndPushState 清栈后重建）——
		///   底下那个给选人层当宿主用的建号屏已被 Teardown 摘层、随清栈销毁，不会留下两份。
		/// </summary>
		private static void FallbackToCharacterCreation()
		{
			LivingWorldCampaignGameManager.PushCharacterCreation();
		}
	}
}
