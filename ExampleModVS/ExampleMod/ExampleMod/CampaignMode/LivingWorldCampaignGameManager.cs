using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterCreationContent;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 通用战役 GameManager（自定义战役的加载状态机）。
	/// 母本 = 织丰 ShokuhoCampaignGameManager（1.2.12 实读反编译）：
	/// DoLoadingForGameManager（LoadModuleData → 建 LivingWorldCampaign → Game.CreateGame →
	/// 各层 DoLoading）→ OnLoadFinished → **选人界面**（2026-09-10 起；此前直接进建号）。
	/// v0 只支持新游戏（读档按钮已禁用，_loadingSavedGame 分支为 v1 预留）。
	/// </summary>
	public class LivingWorldCampaignGameManager : MBGameManager
	{
		private bool _loadingSavedGame;

		/// <summary>
		/// 选人界面是否已弹过。
		/// 🔴 必需守卫（2026-09-10 实机「卡 loading」的根因）：引擎 <c>GameLoadingState.OnTick</c> 会
		/// **每帧**调 <c>OnLoadFinished</c>（它只对 DoLoadingForGameManager 有 `_loadingFinished` 守卫，
		/// 对 OnLoadFinished 没有）。没有本守卫 = 每帧重建一次选人界面 = 玩家的点击永远被下一次重建吞掉。
		/// </summary>
		private bool _heroSelectOpened;

		/// <summary>
		/// 玩家在主菜单选的**时代**（该时代的 GameType 字符串，如 "TaikouCampaign1560"）。
		/// 🔴 时代必须在**主菜单**决定（时序硬约束，见 plans/时代剧本切换-验证.md）：
		///   引擎在「new 出战役类」时才按类名过滤 XML 段 → 主菜单之后没有改的余地。
		///   「选角色」不决定加载哪套数据，故可留在此处（世界建好后由选人界面处理）。
		/// </summary>
		public string Era { get; private set; }

		/// <summary>主菜单入口用：选好时代再启动。</summary>
		public LivingWorldCampaignGameManager(string era)
		{
			Era = era;
		}

		public LivingWorldCampaignGameManager(int seed)
		{
		}

		public override void OnGameEnd(Game game)
		{
			base.OnGameEnd(game);
		}

		public override void OnAfterCampaignStart(Game game)
		{
			// 双版本抽象成员（织丰母本同款空实现），v0 无额外动作
		}

		/// <summary>加载状态机（织丰母本同签名：GameManagerBase.DoLoadingForGameManager）。</summary>
		protected override void DoLoadingForGameManager(GameManagerLoadingSteps gameManagerLoadingStep, out GameManagerLoadingSteps nextStep)
		{
			nextStep = (GameManagerLoadingSteps)(-1);
			switch ((int)gameManagerLoadingStep)
			{
				case 0:
					// 🔴 第 26 颗雷（2026-09-08）——MBGlobals.InitializeReferences() 必调：
					// _actionSets 静态词典由它初始化；漏调 → FaceGen/身体模型首个 GetActionSet NRE
					// （真凶链：MBGlobals.GetActionSet → _actionSets null；SandBox EditorSceneMissionManager
					// DoLoading case0 实锤必调点；织丰自家 GameManager 同样补调（Shokuho.dll:121150））。
					// 幂等（_initialized 守卫），任何内容包战役共通。
					MBGlobals.InitializeReferences();
					nextStep = (GameManagerLoadingSteps)1;
					break;
				case 1:
					MBGameManager.LoadModuleData(_loadingSavedGame);
					nextStep = (GameManagerLoadingSteps)2;
					break;
				case 2:
					if (!_loadingSavedGame)
					{
						MBGameManager.StartNewGame();
					}
					nextStep = (GameManagerLoadingSteps)3;
					break;
				case 3:
					if (!_loadingSavedGame)
					{
						// 新建：战役世界出生（内容包的 thin 类在这里被实例化——由此确定 GameType 身份）
						LivingWorldCampaign campaign = CreateCampaignForActiveContentPack(Era);
						Game.CreateGame(campaign, this);
						campaign.SetLoadingParameters((Campaign.GameLoadingType)1); // 1 = NewCampaign（织丰母本同款；GameLoadingType 是 Campaign 嵌套枚举）
					}
					else
					{
						// v0 TODO：读档路径（v1 实现：Game.LoadSaveGame → MapState）——按钮已禁用，此分支不会走到
						Debug.Print("[LivingWorldNpcs] CampaignMode: v0 不支持读档。");
					}
					Game.Current.DoLoading();
					nextStep = (GameManagerLoadingSteps)4;
					break;
				case 4:
				{
					bool flag = true;
					// 版本兼容：1.2.12 = Module.CurrentModule.SubModules；1.3+ = CollectSubModules()——既有轮子 V.CollectSubModules
					foreach (MBSubModuleBase item in V.CollectSubModules())
					{
						flag = flag && item.DoLoading(Game.Current);
					}
					nextStep = flag ? (GameManagerLoadingSteps)5 : (GameManagerLoadingSteps)4;
					break;
				}
				case 5:
					nextStep = Game.Current.DoLoading() ? (GameManagerLoadingSteps)(-1) : (GameManagerLoadingSteps)5;
					break;
			}
		}

		/// <summary>
		/// 按主菜单选定的时代创建对应的 thin 战役类。
		/// 🔴 这里 new 出的类决定了 GameType = 类名 → 决定引擎加载哪一套 XML 段。
		/// 未知/缺失时代 = 退回基类（防呆：按钮只在内容包模式下出现，正常走不到）。
		/// </summary>
		private static LivingWorldCampaign CreateCampaignForActiveContentPack(string era)
		{
			string pack = CampaignModeActivator.ActiveContentPack;
			if (pack == "Taikou")
			{
				switch (era)
				{
					case "TaikouCampaign1554": return new TaikouCampaign1554(CampaignGameMode.Campaign);
					case "TaikouCampaign1560": return new TaikouCampaign1560(CampaignGameMode.Campaign);
					case "TaikouCampaign1568": return new TaikouCampaign1568(CampaignGameMode.Campaign);
					case "TaikouCampaign1575": return new TaikouCampaign1575(CampaignGameMode.Campaign);
					case "TaikouCampaign1582": return new TaikouCampaign1582(CampaignGameMode.Campaign);
					case "TaikouCampaign1598": return new TaikouCampaign1598(CampaignGameMode.Campaign);
				}
			}
			return new LivingWorldCampaign(CampaignGameMode.Campaign);
		}

		/// <summary>
		/// 推入建号界面（原流程，一行未改）——由选人界面点「自定义人物」时调用。
		/// ⚠️ 仅在 1.2.12 有效（1.5.x 建号是另一套体系，见 <see cref="OnLoadFinished"/> 的 1.5.x 分支说明）。
		/// </summary>
		public static void PushCharacterCreation()
		{
#if MB2_V1212
			Game.Current.GameStateManager.CleanAndPushState(
				Game.Current.GameStateManager.CreateState<CharacterCreationState>(
					new LivingWorldCharacterCreationContent(CampaignModeActivator.ActiveContentPack)));
#endif
		}

#if MB2_V1212
		/// <summary>
		/// 加载完成 → **落地**（2026-09-11 改造：选人已挪到建世界**之前**）。
		///
		/// 两条路：
		///   · 玩家在选人屏按过 [决定] → 这里按 id 找到英雄，直接魂穿 + 走完引擎的建号收尾 → 进图。
		///   · 玩家走的是「自定义人物」→ 推建号状态，走原建号流程（不变）。
		///
		/// 🔴 **本方法会被引擎反复调用**（2026-09-10 实机「卡 loading」根因）：
		///   引擎 `GameLoadingState.OnTick` 对 `OnLoadFinished` **没有"完成"守卫**，只要它活着就每帧都调。
		///   ⇒ 本方法**必须幂等**（`_heroSelectOpened` 守卫，语义 = "本局已处理过起始流程"）。
		///
		/// 🔴 **不再需要 `PushCharacterCreation()` 给选人界面当宿主屏**——选人屏在主菜单阶段就已经
		///   选完并退场了（它只读静态表，不依赖 `Campaign.Current`，见 CampaignMode/HeroSelectScreen.cs）。
		///   这条改动砍掉了原实现里最脆的一段（挂层挂空 / 屏栈换代 / 静默失败都出在这里）。
		/// </summary>
		public override void OnLoadFinished()
		{
			base.OnLoadFinished();
			if (_loadingSavedGame || _heroSelectOpened)
			{
				return;
			}
			_heroSelectOpened = true;      // 先置位：落地/推建号过程中若被重入也不会做第二遍

			// ── 路径 A：选人开局（选人屏已经把 id 交给 StartingHero）──
			if (StartingHero.HasPending)
			{
				string heroId = StartingHero.PendingHeroId;
				Hero hero = HeroSelectData.FindHero(heroId);
				if (hero != null && StartingHero.ApplyPending(hero))
				{
					DebugLogger.Log($"[LWN-campaign] 世界已加载（{GetType().Name}）→ 直接落地为 {heroId}，收尾完成");
					return;
				}
				// 落地失败（世界里的英雄与目录不一致 / 收尾抛异常）→ 记日志并回退建号，不让玩家卡住
				DebugLogger.Log($"[LWN-campaign] 落地失败（{heroId}，world hero={(hero == null ? "未找到" : "找到但落地失败")}）→ 回退建号流程");
				StartingHero.ClearPending();
			}

			// ── 路径 B：自定义人物 → 推建号状态，走原建号流程 ──
			PushCharacterCreation();
			DebugLogger.Log($"[LWN-campaign] 世界已加载 GameType={GetType().Name} 时代={Era} → 进入建号流程");
		}
#else
		/// <summary>1.5.x：建号 = CharacterCreationManager 新体系（本类在该版本不接入，v1 TODO）。</summary>
		public override void OnLoadFinished()
		{
			base.OnLoadFinished();
		}
#endif
	}
}
