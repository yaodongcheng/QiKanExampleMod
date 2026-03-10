using System;
using AIInfluence.API;
using AIInfluence.Behaviors.DeathHistory;
using AIInfluence.Diplomacy;
using AIInfluence.DynamicEvents;
using AIInfluence.SettlementCombat;
using AIInfluence.Util;
using Bannerlord.UIExtenderEx;
using HarmonyLib;
using MCM.Abstractions.Base.Global;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace AIInfluence
{
	// Token: 0x020000F2 RID: 242
	public class SubModule : MBSubModuleBase
	{
		// Token: 0x060007C8 RID: 1992 RVA: 0x000A1050 File Offset: 0x0009F250
		protected override void OnSubModuleLoad()
		{
			base.OnSubModuleLoad();
			this._uiExtender.Register(typeof(SubModule).Assembly);
			this._uiExtender.Enable();
			SubModule.harmony.PatchAll();
			DiplomacyPatches.ApplyPatches(SubModule.harmony);
		}

		// Token: 0x060007C9 RID: 1993 RVA: 0x000A10A4 File Offset: 0x0009F2A4
		protected override void OnBeforeInitialModuleScreenSetAsRoot()
		{
			bool flag = !this._isInitialized;
			if (flag)
			{
				this._isInitialized = true;
			}
		}

		// Token: 0x060007CA RID: 1994 RVA: 0x000A10C8 File Offset: 0x0009F2C8
		protected override void OnGameStart(Game game, IGameStarter gameStarter)
		{
			base.OnGameStart(game, gameStarter);
			CampaignGameStarter campaignGameStarter = gameStarter as CampaignGameStarter;
			bool flag = campaignGameStarter != null;
			if (flag)
			{
				this._aiInfluenceBehavior = new AIInfluenceBehavior();
				campaignGameStarter.AddBehavior(this._aiInfluenceBehavior);
				campaignGameStarter.AddBehavior(new SettlementPenaltyManager());
				campaignGameStarter.AddBehavior(new DeathHistoryBehavior());
				campaignGameStarter.AddBehavior(new EconomicEffectsManager());
			}
		}

		// Token: 0x060007CB RID: 1995 RVA: 0x000A112C File Offset: 0x0009F32C
		public override void OnGameEnd(Game game)
		{
			base.OnGameEnd(game);
			Console.WriteLine("[SUBMODULE] OnGameEnd called - resetting systems");
			bool flag = this._aiInfluenceBehavior != null;
			if (flag)
			{
				this._aiInfluenceBehavior = null;
			}
			InitializationManager.Instance.ResetAllSystems();
			try
			{
				Player2UsageTracker.Instance.OnGameEnd();
			}
			catch (Exception)
			{
			}
			try
			{
				Player2Client.StopHeartbeatTimer();
			}
			catch (Exception)
			{
			}
		}

		// Token: 0x060007CC RID: 1996 RVA: 0x000A11B0 File Offset: 0x0009F3B0
		protected override void OnApplicationTick(float dt)
		{
			base.OnApplicationTick(dt);
			bool flag = !this._loadMessageDisplayed;
			if (flag)
			{
				this._loadMessageTimer += dt;
				bool flag2 = this._loadMessageTimer >= 1f;
				if (flag2)
				{
					InformationManager.DisplayMessage(new InformationMessage("AI Influence Mod Loaded [BETA 3.2.3]"));
					this._loadMessageDisplayed = true;
					try
					{
						Player2Client.StartHeartbeatTimer();
					}
					catch (Exception ex)
					{
					}
					try
					{
						bool flag3 = GlobalSettings<ModSettings>.Instance != null;
						if (flag3)
						{
							Player2UsageTracker.Instance.OnGameStart();
						}
					}
					catch (Exception ex2)
					{
					}
				}
			}
			bool flag4 = Campaign.Current != null && !this._startMessageDisplayed;
			if (flag4)
			{
				this._startMessageTimer += dt;
				bool flag5 = this._startMessageTimer >= 1f;
				if (flag5)
				{
					InformationManager.DisplayMessage(new InformationMessage("AI Influence Mod Started in Game [BETA 3.2.3]"));
					this._startMessageDisplayed = true;
				}
			}
			this._backendCheckTimer += dt;
			bool flag6 = this._backendCheckTimer >= 1f;
			if (flag6)
			{
				this._backendCheckTimer = 0f;
				AIInfluenceBehavior.CheckAndEnforcePlayer2Backend();
			}
			try
			{
				Player2UsageTracker.Instance.Update();
			}
			catch (Exception)
			{
			}
			bool flag7 = Campaign.Current != null;
			if (flag7)
			{
				AIInfluenceBehavior campaignBehavior = Campaign.Current.GetCampaignBehavior<AIInfluenceBehavior>();
				bool flag8 = campaignBehavior != null;
				if (flag8)
				{
					campaignBehavior.Tick(dt);
				}
			}
			this.TryAttachWorldEventsLayer();
		}

		// Token: 0x060007CD RID: 1997 RVA: 0x000A1348 File Offset: 0x0009F548
		private void TryAttachWorldEventsLayer()
		{
			MapScreen mapScreen = ScreenManager.TopScreen as MapScreen;
			bool flag = this.ShouldShowWorldEventsButton();
			bool flag2 = mapScreen == null || !flag;
			if (flag2)
			{
				bool flag3 = this._worldEventsUILayer != null;
				if (flag3)
				{
					MapScreen worldEventsLayerOwner = this._worldEventsLayerOwner;
					if (worldEventsLayerOwner != null)
					{
						worldEventsLayerOwner.RemoveLayer(this._worldEventsUILayer);
					}
					if (mapScreen != null)
					{
						mapScreen.RemoveLayer(this._worldEventsUILayer);
					}
					this._worldEventsUILayer = null;
					this._worldEventsLayerOwner = null;
				}
			}
			else
			{
				bool flag4 = this._worldEventsUILayer == null;
				if (flag4)
				{
					this._worldEventsUILayer = new WorldEventsUILayer();
					mapScreen.AddLayer(this._worldEventsUILayer);
					this._worldEventsLayerOwner = mapScreen;
				}
			}
		}

		// Token: 0x060007CE RID: 1998 RVA: 0x000A13F0 File Offset: 0x0009F5F0
		private bool ShouldShowWorldEventsButton()
		{
			ModSettings instance = GlobalSettings<ModSettings>.Instance;
			bool flag = instance == null;
			return !flag && (instance.EnableModification && instance.EnableDiplomacy && instance.EnableDynamicEvents) && instance.CanEnableDiplomacy();
		}

		// Token: 0x040004D4 RID: 1236
		private const string HarmonyId = "com.mfive.aiinfluence";

		// Token: 0x040004D5 RID: 1237
		private static readonly Harmony harmony = new Harmony("com.mfive.aiinfluence");

		// Token: 0x040004D6 RID: 1238
		private readonly UIExtender _uiExtender = GameVersionCompatibility.CreateUIExtender("AIInfluence");

		// Token: 0x040004D7 RID: 1239
		private AIInfluenceBehavior _aiInfluenceBehavior;

		// Token: 0x040004D8 RID: 1240
		private bool _isInitialized;

		// Token: 0x040004D9 RID: 1241
		private bool _loadMessageDisplayed;

		// Token: 0x040004DA RID: 1242
		private float _loadMessageTimer;

		// Token: 0x040004DB RID: 1243
		private const float MessageDelay = 1f;

		// Token: 0x040004DC RID: 1244
		private bool _startMessageDisplayed;

		// Token: 0x040004DD RID: 1245
		private float _startMessageTimer;

		// Token: 0x040004DE RID: 1246
		private float _backendCheckTimer;

		// Token: 0x040004DF RID: 1247
		private const float BackendCheckInterval = 1f;

		// Token: 0x040004E0 RID: 1248
		private WorldEventsUILayer _worldEventsUILayer;

		// Token: 0x040004E1 RID: 1249
		private MapScreen _worldEventsLayerOwner;
	}
}
