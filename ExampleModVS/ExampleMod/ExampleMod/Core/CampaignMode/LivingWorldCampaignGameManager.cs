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
	/// 各层 DoLoading）→ OnLoadFinished → 建号界面。
	/// v0 只支持新游戏（读档按钮已禁用，_loadingSavedGame 分支为 v1 预留）。
	/// </summary>
	public class LivingWorldCampaignGameManager : MBGameManager
	{
		private bool _loadingSavedGame;

		public LivingWorldCampaignGameManager()
		{
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
						LivingWorldCampaign campaign = CreateCampaignForActiveContentPack();
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

		/// <summary>按已加载的内容包创建对应 thin 战役类；无内容包 = 默认通用战役（此路径防呆，按钮只在内容包模式下出现）。</summary>
		private static LivingWorldCampaign CreateCampaignForActiveContentPack()
		{
			string pack = CampaignModeActivator.ActiveContentPack;
			if (pack == "Taikou")
			{
				return new TaikouCampaign(CampaignGameMode.Campaign);
			}
			return new LivingWorldCampaign(CampaignGameMode.Campaign);
		}

		/// <summary>加载完成 → 建号界面（织丰式：CleanAndPushState + CharacterCreationState + 自定义 Content）。</summary>
		public override void OnLoadFinished()
		{
			base.OnLoadFinished();
			if (!_loadingSavedGame)
			{
#if MB2_V1212
				Game.Current.GameStateManager.CleanAndPushState(
					Game.Current.GameStateManager.CreateState<CharacterCreationState>(
						new LivingWorldCharacterCreationContent(CampaignModeActivator.ActiveContentPack)));
#else
				// 🔴 1.5.x：建号 = CharacterCreationManager 新体系（LivingWorldCharacterCreationContent 在该版本是空占位）——v0 不接入（v1 TODO）
#endif
			}
		}
	}
}
