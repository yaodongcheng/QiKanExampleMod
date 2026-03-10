using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AIInfluence.Behaviors.AIActions;
using SandBox.Missions.MissionLogics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ViewModelCollection.Order.Visual;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x02000134 RID: 308
	public class PlayerReinforcementMissionLogic : MissionLogic
	{
		// Token: 0x060009BC RID: 2492 RVA: 0x000B79BC File Offset: 0x000B5BBC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public PlayerReinforcementMissionLogic(AIInfluenceBehavior behavior)
		{
			this._behavior = behavior;
			this._logger = SettlementCombatLogger.Instance;
			AIInfluenceBehavior behavior2 = this._behavior;
			this._combatManager = ((behavior2 != null) ? behavior2.GetSettlementCombatManager() : null);
			SettlementCombatManager combatManager = this._combatManager;
			this._defenderSpawner = ((combatManager != null) ? combatManager.GetDefenderSpawner() : null);
			this._behavior.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(2075578454));
			this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1128857272));
			this._canSummonTroops = false;
			this._previousCanSummonTroops = false;
			this._messageShown = false;
			this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1052512338));
		}

		// Token: 0x060009BD RID: 2493 RVA: 0x000B7AF8 File Offset: 0x000B5CF8
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override void OnRemoveBehavior()
		{
			base.OnRemoveBehavior();
			try
			{
				bool flag = this._orderProvider != null;
				if (flag)
				{
					VisualOrderFactory.UnregisterProvider(this._orderProvider);
					this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1145356714));
					this._orderProvider = null;
				}
				this._summonedAgents.Clear();
				this._agentToPartyMap.Clear();
				this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1661858505));
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1077231860), ex.Message, ex);
			}
		}

		// Token: 0x060009BE RID: 2494 RVA: 0x000B7BB0 File Offset: 0x000B5DB0
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override void OnBehaviorInitialize()
		{
			base.OnBehaviorInitialize();
			try
			{
				this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1140720878));
				this._behavior.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-2061221153));
				this._canSummonTroops = this.CanSummonTroopsInThisMission();
				this._previousCanSummonTroops = this._canSummonTroops;
				this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-915335931), this._canSummonTroops));
				this._behavior.LogMessage(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1683651391), this._canSummonTroops));
				this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1544459908));
				bool canSummonTroops = this._canSummonTroops;
				if (canSummonTroops)
				{
					this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1939510665));
					this._behavior.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(35191500));
					bool flag = Agent.Main != null;
					if (flag)
					{
						this._playerInitialSpawnPosition = Agent.Main.Position;
						this._playerInitialForward2D = Agent.Main.LookDirection.AsVec2;
						this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(613616816), this._playerInitialSpawnPosition));
						this._behavior.LogMessage(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(99816384), this._playerInitialSpawnPosition));
					}
					else
					{
						this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(275758385));
						this._behavior.LogMessage(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1356831801));
					}
				}
				else
				{
					this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(2003979099));
					this._behavior.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(556338015));
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1517650133), ex.Message, ex);
				this._behavior.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(252443367) + ex.Message);
			}
		}

		// Token: 0x060009BF RID: 2495 RVA: 0x000B7E0C File Offset: 0x000B600C
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override void OnMissionTick(float dt)
		{
			base.OnMissionTick(dt);
			try
			{
				bool flag = Settlement.CurrentSettlement == null;
				if (!flag)
				{
					bool flag2 = !this._onMissionTickCalled;
					if (flag2)
					{
						this._behavior.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1263952251));
						this._onMissionTickCalled = true;
					}
					this._canSummonTroops = this.CanSummonTroopsInThisMission();
					bool flag3 = this._canSummonTroops && !this._previousCanSummonTroops;
					if (flag3)
					{
						this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1189714122));
						this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(730432479), this._messageShown));
						this._messageShown = false;
						this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(37173757), this._messageShown));
						this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1105417742));
					}
					this._previousCanSummonTroops = this._canSummonTroops;
					bool flag4 = !this._canSummonTroops;
					if (!flag4)
					{
						bool flag5 = this._playerInitialSpawnPosition == Vec3.Zero && Agent.Main != null && Agent.Main.IsActive();
						if (flag5)
						{
							this._playerInitialSpawnPosition = Agent.Main.Position;
							this._playerInitialForward2D = Agent.Main.LookDirection.AsVec2;
							this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-48033473), this._playerInitialSpawnPosition));
							this._behavior.LogMessage(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-2058248268), this._playerInitialSpawnPosition));
						}
						bool flag6 = this._canSummonTroops && !this._playerTeamInitialized && Agent.Main != null && Agent.Main.IsActive();
						if (flag6)
						{
							this.InitializePlayerTeamForControl();
						}
						bool flag7 = !this._messageShown && Agent.Main != null && Agent.Main.IsActive();
						if (flag7)
						{
							this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1225682544));
							this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1257826345), this._messageShown));
							this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1203630409) + ((Agent.Main != null) ? <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1197641268) : <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1556416923)));
							this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1652330410), Agent.Main.IsActive()));
							this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(160163854));
							this._behavior.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1170619126));
							this.ShowSummonHint();
							this._messageShown = true;
							this._behavior.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1351207075));
						}
						else
						{
							bool flag8 = !this._messageShown;
							if (flag8)
							{
								this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-613088789), this._messageShown, (Agent.Main != null) ? <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1509773933) : <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1239366164), (Agent.Main != null) ? Agent.Main.IsActive().ToString() : <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-2072128903)));
							}
						}
						this.CleanupDeadAgents();
						bool flag9 = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
						bool flag10 = Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt);
						bool flag11 = Input.IsKeyPressed(InputKey.X);
						bool flag12 = flag11 && flag9;
						if (flag12)
						{
							this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(572741123), flag9, flag10, flag11));
							this._behavior.LogMessage(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1088959307), flag9, flag10, flag11));
							bool flag13 = flag10;
							if (flag13)
							{
								this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1324244831));
								this._behavior.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(2133877600));
								this.SummonAllPlayerTroops();
							}
							else
							{
								this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1521512150));
								this._behavior.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-388651289));
								this.SummonPlayerTroops();
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1815001443), ex.Message, ex);
			}
		}

		// Token: 0x060009C0 RID: 2496 RVA: 0x000B832C File Offset: 0x000B652C
		public bool IsSummonedTroop(Agent agent)
		{
			return this._summonedAgents.Contains(agent);
		}

		// Token: 0x060009C1 RID: 2497 RVA: 0x000B834C File Offset: 0x000B654C
		public bool HasActiveSummonedTroops()
		{
			bool result;
			try
			{
				bool flag = this._summonedAgents == null || this._summonedAgents.Count == 0;
				if (flag)
				{
					result = false;
				}
				else
				{
					result = Enumerable.Any<Agent>(this._summonedAgents, (Agent a) => a != null && a.IsActive());
				}
			}
			catch
			{
				result = false;
			}
			return result;
		}

		// Token: 0x060009C2 RID: 2498 RVA: 0x000B83C0 File Offset: 0x000B65C0
		public int GetSummonedTroopsCount()
		{
			int result;
			try
			{
				List<Agent> list = Enumerable.ToList<Agent>(Enumerable.Where<Agent>(this._summonedAgents, (Agent a) => a == null || !a.IsActive()));
				foreach (Agent agent in list)
				{
					this._summonedAgents.Remove(agent);
					this._agentToPartyMap.Remove(agent);
				}
				result = this._summonedAgents.Count;
			}
			catch
			{
				result = 0;
			}
			return result;
		}

		// Token: 0x060009C3 RID: 2499 RVA: 0x000B8478 File Offset: 0x000B6678
		[MethodImpl(MethodImplOptions.NoInlining)]
		public string GetSummonedTroopsInfo()
		{
			string result;
			try
			{
				List<Agent> list = Enumerable.ToList<Agent>(Enumerable.Where<Agent>(this._summonedAgents, (Agent a) => a == null || !a.IsActive()));
				foreach (Agent agent in list)
				{
					this._summonedAgents.Remove(agent);
					this._agentToPartyMap.Remove(agent);
				}
				bool flag = this._summonedAgents.Count == 0;
				if (flag)
				{
					result = <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1831273161);
				}
				else
				{
					Dictionary<string, int> dictionary = new Dictionary<string, int>();
					List<string> list2 = new List<string>();
					foreach (Agent agent2 in this._summonedAgents)
					{
						bool flag2 = agent2.Character != null;
						if (flag2)
						{
							CharacterObject characterObject = agent2.Character as CharacterObject;
							bool flag3 = characterObject != null && characterObject.IsHero;
							if (flag3)
							{
								string item = string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(376621889), characterObject.Name, characterObject.StringId);
								bool flag4 = !list2.Contains(item);
								if (flag4)
								{
									list2.Add(item);
								}
							}
							else
							{
								TextObject name = agent2.Character.Name;
								string text = ((name != null) ? name.ToString() : null) ?? <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(293976138);
								bool flag5 = dictionary.ContainsKey(text);
								if (flag5)
								{
									Dictionary<string, int> dictionary2 = dictionary;
									string key = text;
									int num = dictionary2[key];
									dictionary2[key] = num + 1;
								}
								else
								{
									dictionary[text] = 1;
								}
							}
						}
					}
					List<string> list3 = new List<string>();
					list3.AddRange(list2);
					foreach (KeyValuePair<string, int> keyValuePair in Enumerable.OrderByDescending<KeyValuePair<string, int>, int>(dictionary, (KeyValuePair<string, int> x) => x.Value))
					{
						list3.Add(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-240207200), keyValuePair.Key, keyValuePair.Value));
					}
					result = string.Join(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-234543546), list3);
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1509753310), ex.Message, ex);
				result = <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1351906266);
			}
			return result;
		}

		// Token: 0x060009C4 RID: 2500 RVA: 0x000B8784 File Offset: 0x000B6984
		[MethodImpl(MethodImplOptions.NoInlining)]
		private bool CanSummonTroopsInThisMission()
		{
			bool result;
			try
			{
				Settlement currentSettlement = Settlement.CurrentSettlement;
				bool flag = currentSettlement == null;
				if (flag)
				{
					result = false;
				}
				else
				{
					MobileParty mainParty = MobileParty.MainParty;
					bool flag2 = mainParty == null || mainParty.MemberRoster == null;
					if (flag2)
					{
						result = false;
					}
					else
					{
						Mission mission = Mission.Current;
						string text = ((mission != null) ? mission.SceneName : null) ?? "";
						bool flag3 = text.Contains(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(421416266)) || text.Contains(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(61840145));
						bool flag4 = text.Contains(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1054929364)) || text.Contains(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-730835455));
						bool flag5 = text.Contains(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(851906036)) || text.Contains(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-598357465)) || (text.Contains(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(2131054743)) && text.Contains(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1939008165))) || (text.Contains(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1173381543)) && text.Contains(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1939008165)));
						bool flag6 = text.Contains(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(847495609)) || text.Contains(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(908131800));
						bool flag7 = text.Contains(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1533160033)) || text.Contains(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-853490777));
						bool flag8 = flag3 || flag4 || flag5 || flag6 || flag7;
						if (flag8)
						{
							bool flag9 = !this._loggedRestrictedLocation;
							if (flag9)
							{
								this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1639780667), new object[]
								{
									flag3,
									flag4,
									flag5,
									flag6,
									flag7
								}));
								this._loggedRestrictedLocation = true;
							}
							result = false;
						}
						else
						{
							this._loggedRestrictedLocation = false;
							bool flag10 = currentSettlement.IsTown || currentSettlement.IsCastle;
							bool isVillage = currentSettlement.IsVillage;
							bool flag11 = !flag10 && !isVillage;
							if (flag11)
							{
								bool flag12 = !this._loggedNotCityOrCastle;
								if (flag12)
								{
									this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(246433690), currentSettlement.IsTown, currentSettlement.IsCastle, currentSettlement.IsVillage));
									this._loggedNotCityOrCastle = true;
								}
								result = false;
							}
							else
							{
								bool flag13 = isVillage;
								if (flag13)
								{
									this._loggedNotCityOrCastle = false;
									this._loggedCombatNotActive = false;
									this._loggedPromptNotReady = false;
									result = true;
								}
								else
								{
									this._loggedNotCityOrCastle = false;
									SettlementCombatManager combatManager = this._combatManager;
									bool flag14 = combatManager != null && combatManager.IsActiveCombat();
									bool flag15 = !flag14;
									if (flag15)
									{
										bool flag16 = !this._loggedCombatNotActive;
										if (flag16)
										{
											this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-633829790));
											this._loggedCombatNotActive = true;
										}
										result = false;
									}
									else
									{
										this._loggedCombatNotActive = false;
										SettlementCombatManager combatManager2 = this._combatManager;
										bool flag17 = combatManager2 != null && combatManager2.IsCombatPromptReady();
										bool flag18 = !flag17;
										if (flag18)
										{
											bool flag19 = !this._loggedPromptNotReady;
											if (flag19)
											{
												this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-627958941));
												this._loggedPromptNotReady = true;
											}
											result = false;
										}
										else
										{
											this._loggedPromptNotReady = false;
											result = true;
										}
									}
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(645920730), ex.Message, ex);
				result = false;
			}
			return result;
		}

		// Token: 0x060009C5 RID: 2501 RVA: 0x000B8B64 File Offset: 0x000B6D64
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void SummonPlayerTroops()
		{
			try
			{
				int num;
				List<ValueTuple<TroopRosterElement, MobileParty>> troopElementsWithParty = this.GetTroopElementsWithParty(out num);
				bool flag = troopElementsWithParty.Count == 0;
				if (flag)
				{
					this.ShowMessage(new TextObject(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1627809036), null).ToString(), \u200D\u206E\u200B\u202E\u206A\u202A\u200E\u202E\u200E\u202E\u202A\u202B\u200C\u206F\u202A\u206E\u206E\u200F\u206B\u202D\u200C\u200F\u206E\u200C\u206E\u202E\u200B\u200D\u202D\u202E\u206D\u206E\u202B\u200F\u200C\u206A\u202A\u202C\u206D\u202E.\u202C\u200D\u202E\u200D\u206F\u206E\u206C\u202E\u206A\u206C\u206F\u200F\u206F\u200C\u206A\u206D\u206B\u200B\u206B\u202C\u202A\u202E\u200B\u200B\u206B\u200D\u200D\u202B\u206F\u202E\u202E\u202E\u200F\u200C\u202A\u202D\u206A\u202D\u200F\u200C\u202E);
				}
				else
				{
					Dictionary<CharacterObject, MobileParty> dictionary = new Dictionary<CharacterObject, MobileParty>();
					foreach (ValueTuple<TroopRosterElement, MobileParty> valueTuple in troopElementsWithParty)
					{
						TroopRosterElement item = valueTuple.Item1;
						MobileParty item2 = valueTuple.Item2;
						bool flag2 = item.Character != null && !dictionary.ContainsKey(item.Character);
						if (flag2)
						{
							dictionary[item.Character] = item2;
						}
					}
					List<TroopRosterElement> rosterElements = Enumerable.ToList<TroopRosterElement>(Enumerable.Select<ValueTuple<TroopRosterElement, MobileParty>, TroopRosterElement>(troopElementsWithParty, ([TupleElementNames(new string[]
					{
						"Element",
						"Party"
					})] ValueTuple<TroopRosterElement, MobileParty> x) => x.Item1));
					List<PlayerReinforcementMissionLogic.SummonTroopInfo> troopsToSummon = this.GetTroopsToSummon(rosterElements, 10, dictionary);
					bool flag3 = troopsToSummon.Count == 0;
					if (flag3)
					{
						this.ShowMessage(new TextObject(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-65993770), null).ToString(), \u200D\u206E\u200B\u202E\u206A\u202A\u200E\u202E\u200E\u202E\u202A\u202B\u200C\u206F\u202A\u206E\u206E\u200F\u206B\u202D\u200C\u200F\u206E\u200C\u206E\u202E\u200B\u200D\u202D\u202E\u206D\u206E\u202B\u200F\u200C\u206A\u202A\u202C\u206D\u202E.\u202C\u200D\u202E\u200D\u206F\u206E\u206C\u202E\u206A\u206C\u206F\u200F\u206F\u200C\u206A\u206D\u206B\u200B\u206B\u202C\u202A\u202E\u200B\u200B\u206B\u200D\u200D\u202B\u206F\u202E\u202E\u202E\u200F\u200C\u202A\u202D\u206A\u202D\u200F\u200C\u202E);
					}
					else
					{
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-854831725), num, troopsToSummon.Count));
						int num2 = 0;
						foreach (PlayerReinforcementMissionLogic.SummonTroopInfo summonTroopInfo in troopsToSummon)
						{
							bool flag4 = num2 >= 10;
							if (flag4)
							{
								break;
							}
							int num3 = Math.Min(summonTroopInfo.Count, 10 - num2);
							for (int i = 0; i < num3; i++)
							{
								bool flag5 = this.SpawnPlayerTroop(summonTroopInfo.Character, false, summonTroopInfo.SourceParty);
								if (flag5)
								{
									num2++;
									bool flag6 = num2 >= 10;
									if (flag6)
									{
										break;
									}
								}
							}
						}
						this._totalSummoned += num2;
						bool flag7 = num2 > 0;
						if (flag7)
						{
							PlayerReinforcementMissionLogic.SummonTroopInfo summonTroopInfo2 = Enumerable.FirstOrDefault<PlayerReinforcementMissionLogic.SummonTroopInfo>(troopsToSummon);
							CharacterObject characterObject = (summonTroopInfo2 != null) ? summonTroopInfo2.Character : null;
							TextObject textObject = new TextObject(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-881294287), null);
							MBInformationManager.AddQuickInformation(textObject, 3000, characterObject, null, "");
							this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1172945972), num2, this._totalSummoned));
							this.SelectFormationsWithTroops();
							bool flag8 = !this._followModeHintShown;
							if (flag8)
							{
								this.ShowMessage(new TextObject(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-402715427), null).ToString(), \u200D\u206E\u200B\u202E\u206A\u202A\u200E\u202E\u200E\u202E\u202A\u202B\u200C\u206F\u202A\u206E\u206E\u200F\u206B\u202D\u200C\u200F\u206E\u200C\u206E\u202E\u200B\u200D\u202D\u202E\u206D\u206E\u202B\u200F\u200C\u206A\u202A\u202C\u206D\u202E.\u200C\u206B\u200F\u206B\u206A\u206C\u206D\u202D\u202E\u200F\u206B\u202D\u200F\u202A\u202D\u206D\u206A\u206D\u200D\u202A\u206F\u206C\u200B\u202B\u200D\u206C\u200E\u200F\u206D\u206C\u202D\u206B\u200F\u206E\u200D\u200F\u200C\u206F\u202C\u206F\u202E);
								this._followModeHintShown = true;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(487145358), ex.Message, ex);
			}
		}

		// Token: 0x060009C6 RID: 2502 RVA: 0x000B8EB8 File Offset: 0x000B70B8
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void SummonAllPlayerTroops()
		{
			try
			{
				int num;
				List<ValueTuple<TroopRosterElement, MobileParty>> troopElementsWithParty = this.GetTroopElementsWithParty(out num);
				bool flag = troopElementsWithParty.Count == 0;
				if (flag)
				{
					this.ShowMessage(new TextObject(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1627240957), null).ToString(), \u200D\u206E\u200B\u202E\u206A\u202A\u200E\u202E\u200E\u202E\u202A\u202B\u200C\u206F\u202A\u206E\u206E\u200F\u206B\u202D\u200C\u200F\u206E\u200C\u206E\u202E\u200B\u200D\u202D\u202E\u206D\u206E\u202B\u200F\u200C\u206A\u202A\u202C\u206D\u202E.\u202C\u200D\u202E\u200D\u206F\u206E\u206C\u202E\u206A\u206C\u206F\u200F\u206F\u200C\u206A\u206D\u206B\u200B\u206B\u202C\u202A\u202E\u200B\u200B\u206B\u200D\u200D\u202B\u206F\u202E\u202E\u202E\u200F\u200C\u202A\u202D\u206A\u202D\u200F\u200C\u202E);
				}
				else
				{
					Dictionary<CharacterObject, MobileParty> dictionary = new Dictionary<CharacterObject, MobileParty>();
					foreach (ValueTuple<TroopRosterElement, MobileParty> valueTuple in troopElementsWithParty)
					{
						TroopRosterElement item = valueTuple.Item1;
						MobileParty item2 = valueTuple.Item2;
						bool flag2 = item.Character != null && !dictionary.ContainsKey(item.Character);
						if (flag2)
						{
							dictionary[item.Character] = item2;
						}
					}
					List<TroopRosterElement> rosterElements = Enumerable.ToList<TroopRosterElement>(Enumerable.Select<ValueTuple<TroopRosterElement, MobileParty>, TroopRosterElement>(troopElementsWithParty, ([TupleElementNames(new string[]
					{
						"Element",
						"Party"
					})] ValueTuple<TroopRosterElement, MobileParty> x) => x.Item1));
					List<PlayerReinforcementMissionLogic.SummonTroopInfo> troopsToSummon = this.GetTroopsToSummon(rosterElements, int.MaxValue, dictionary);
					bool flag3 = troopsToSummon.Count == 0;
					if (flag3)
					{
						this.ShowMessage(new TextObject(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1627240957), null).ToString(), \u200D\u206E\u200B\u202E\u206A\u202A\u200E\u202E\u200E\u202E\u202A\u202B\u200C\u206F\u202A\u206E\u206E\u200F\u206B\u202D\u200C\u200F\u206E\u200C\u206E\u202E\u200B\u200D\u202D\u202E\u206D\u206E\u202B\u200F\u200C\u206A\u202A\u202C\u206D\u202E.\u202C\u200D\u202E\u200D\u206F\u206E\u206C\u202E\u206A\u206C\u206F\u200F\u206F\u200C\u206A\u206D\u206B\u200B\u206B\u202C\u202A\u202E\u200B\u200B\u206B\u200D\u200D\u202B\u206F\u202E\u202E\u202E\u200F\u200C\u202A\u202D\u206A\u202D\u200F\u200C\u202E);
					}
					else
					{
						this._isAllTroopsSummoned = true;
						this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(751995817));
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1882047565), num, troopsToSummon.Count));
						int num2 = 0;
						int num3 = 0;
						foreach (PlayerReinforcementMissionLogic.SummonTroopInfo summonTroopInfo in troopsToSummon)
						{
							for (int i = 0; i < summonTroopInfo.Count; i++)
							{
								bool flag4 = this.SpawnPlayerTroop(summonTroopInfo.Character, true, summonTroopInfo.SourceParty);
								bool flag5 = flag4;
								if (flag5)
								{
									num2++;
								}
								else
								{
									int maxTroopsPerSide = this.GetMaxTroopsPerSide();
									bool flag6 = this.CountActivePlayerTroops() >= maxTroopsPerSide;
									if (flag6)
									{
										num3++;
									}
								}
							}
						}
						this._totalSummoned += num2;
						bool flag7 = num2 > 0 || num3 > 0;
						if (flag7)
						{
							PlayerReinforcementMissionLogic.SummonTroopInfo summonTroopInfo2 = Enumerable.FirstOrDefault<PlayerReinforcementMissionLogic.SummonTroopInfo>(troopsToSummon);
							CharacterObject characterObject = (summonTroopInfo2 != null) ? summonTroopInfo2.Character : null;
							int maxTroopsPerSide2 = this.GetMaxTroopsPerSide();
							bool flag8 = num3 > 0;
							if (flag8)
							{
								TextObject textObject = new TextObject(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-26470135), null);
								textObject.SetTextVariable(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1805996339), num2);
								textObject.SetTextVariable(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(371197949), num3);
								MBInformationManager.AddQuickInformation(textObject, 3000, characterObject, null, "");
								this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1482001712), num2, num3, this._totalSummoned));
							}
							else
							{
								TextObject textObject2 = new TextObject(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-355247640), null);
								MBInformationManager.AddQuickInformation(textObject2, 3000, characterObject, null, "");
								this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1749734755), this._totalSummoned));
							}
							this.SelectFormationsWithTroops();
							bool flag9 = !this._followModeHintShown;
							if (flag9)
							{
								this.ShowMessage(new TextObject(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(835046068), null).ToString(), \u200D\u206E\u200B\u202E\u206A\u202A\u200E\u202E\u200E\u202E\u202A\u202B\u200C\u206F\u202A\u206E\u206E\u200F\u206B\u202D\u200C\u200F\u206E\u200C\u206E\u202E\u200B\u200D\u202D\u202E\u206D\u206E\u202B\u200F\u200C\u206A\u202A\u202C\u206D\u202E.\u200C\u206B\u200F\u206B\u206A\u206C\u206D\u202D\u202E\u200F\u206B\u202D\u200F\u202A\u202D\u206D\u206A\u206D\u200D\u202A\u206F\u206C\u200B\u202B\u200D\u206C\u200E\u200F\u206D\u206C\u202D\u206B\u200F\u206E\u200D\u200F\u200C\u206F\u202C\u206F\u202E);
								this._followModeHintShown = true;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1148794156), ex.Message, ex);
			}
		}

		// Token: 0x060009C7 RID: 2503 RVA: 0x000B92D0 File Offset: 0x000B74D0
		[MethodImpl(MethodImplOptions.NoInlining)]
		private List<MobileParty> GetTroopSourceParties()
		{
			PlayerReinforcementMissionLogic.<>c__DisplayClass37_0 CS$<>8__locals1;
			CS$<>8__locals1.result = new List<MobileParty>();
			try
			{
				MobileParty mainParty = MobileParty.MainParty;
				bool flag = mainParty == null;
				if (flag)
				{
					return CS$<>8__locals1.result;
				}
				PlayerReinforcementMissionLogic.<>c__DisplayClass37_1 CS$<>8__locals2;
				CS$<>8__locals2.visited = new HashSet<MobileParty>();
				PlayerReinforcementMissionLogic.<GetTroopSourceParties>g__AddParty|37_0(mainParty, ref CS$<>8__locals1, ref CS$<>8__locals2);
				Army army = mainParty.Army;
				bool flag2 = army != null && army.LeaderParty == mainParty;
				if (flag2)
				{
					foreach (MobileParty mobileParty in army.Parties)
					{
						bool flag3 = mobileParty == null || mobileParty == mainParty;
						if (!flag3)
						{
							PlayerReinforcementMissionLogic.<GetTroopSourceParties>g__AddParty|37_0(mobileParty, ref CS$<>8__locals1, ref CS$<>8__locals2);
						}
					}
				}
				bool flag4 = mainParty.AttachedParties != null;
				if (flag4)
				{
					foreach (MobileParty mobileParty2 in mainParty.AttachedParties)
					{
						bool flag5 = mobileParty2 == null || mobileParty2 == mainParty;
						if (!flag5)
						{
							PlayerReinforcementMissionLogic.<GetTroopSourceParties>g__AddParty|37_0(mobileParty2, ref CS$<>8__locals1, ref CS$<>8__locals2);
						}
					}
				}
				Settlement currentSettlement = Settlement.CurrentSettlement;
				bool flag6 = currentSettlement != null && AIActionManager.Instance != null;
				if (flag6)
				{
					List<Hero> heroesFollowingPlayerInSettlement = AIActionManager.Instance.GetHeroesFollowingPlayerInSettlement(currentSettlement, true);
					foreach (Hero hero in heroesFollowingPlayerInSettlement)
					{
						bool flag7 = hero == null || hero.PartyBelongedTo == null;
						if (!flag7)
						{
							bool flag8 = hero.PartyBelongedTo == mainParty;
							if (!flag8)
							{
								bool flag9 = CS$<>8__locals2.visited.Contains(hero.PartyBelongedTo);
								if (!flag9)
								{
									PlayerReinforcementMissionLogic.<GetTroopSourceParties>g__AddParty|37_0(hero.PartyBelongedTo, ref CS$<>8__locals1, ref CS$<>8__locals2);
									this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1980884894), hero.Name, hero.PartyBelongedTo.Name));
									bool flag10 = hero.PartyBelongedTo.Army != null && hero.PartyBelongedTo.Army.LeaderParty == hero.PartyBelongedTo;
									if (flag10)
									{
										foreach (MobileParty mobileParty3 in hero.PartyBelongedTo.Army.Parties)
										{
											bool flag11 = mobileParty3 == null || mobileParty3 == hero.PartyBelongedTo;
											if (!flag11)
											{
												PlayerReinforcementMissionLogic.<GetTroopSourceParties>g__AddParty|37_0(mobileParty3, ref CS$<>8__locals1, ref CS$<>8__locals2);
												this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(502328594), mobileParty3.Name));
											}
										}
									}
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(165863403), ex.Message, ex);
			}
			return CS$<>8__locals1.result;
		}

		// Token: 0x060009C8 RID: 2504 RVA: 0x000B9664 File Offset: 0x000B7864
		[MethodImpl(MethodImplOptions.NoInlining)]
		private List<TroopRosterElement> GetTroopElementsIncludingArmy(out int sourcePartyCount)
		{
			List<TroopRosterElement> list = new List<TroopRosterElement>();
			sourcePartyCount = 0;
			try
			{
				List<MobileParty> troopSourceParties = this.GetTroopSourceParties();
				sourcePartyCount = troopSourceParties.Count;
				foreach (MobileParty mobileParty in troopSourceParties)
				{
					TroopRoster memberRoster = mobileParty.MemberRoster;
					bool flag = memberRoster == null;
					if (!flag)
					{
						foreach (TroopRosterElement troopRosterElement in memberRoster.GetTroopRoster())
						{
							bool flag2 = troopRosterElement.Character != null && troopRosterElement.Number > 0;
							if (flag2)
							{
								list.Add(troopRosterElement);
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(222519738), ex.Message, ex);
			}
			return list;
		}

		// Token: 0x060009C9 RID: 2505 RVA: 0x000B9788 File Offset: 0x000B7988
		[MethodImpl(MethodImplOptions.NoInlining)]
		[return: TupleElementNames(new string[]
		{
			"Element",
			"Party"
		})]
		private List<ValueTuple<TroopRosterElement, MobileParty>> GetTroopElementsWithParty(out int sourcePartyCount)
		{
			List<ValueTuple<TroopRosterElement, MobileParty>> list = new List<ValueTuple<TroopRosterElement, MobileParty>>();
			sourcePartyCount = 0;
			try
			{
				List<MobileParty> troopSourceParties = this.GetTroopSourceParties();
				sourcePartyCount = troopSourceParties.Count;
				foreach (MobileParty mobileParty in troopSourceParties)
				{
					TroopRoster memberRoster = mobileParty.MemberRoster;
					bool flag = memberRoster == null;
					if (!flag)
					{
						foreach (TroopRosterElement troopRosterElement in memberRoster.GetTroopRoster())
						{
							bool flag2 = troopRosterElement.Character != null && troopRosterElement.Number > 0;
							if (flag2)
							{
								list.Add(new ValueTuple<TroopRosterElement, MobileParty>(troopRosterElement, mobileParty));
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1594663557), ex.Message, ex);
			}
			return list;
		}

		// Token: 0x060009CA RID: 2506 RVA: 0x000B98B4 File Offset: 0x000B7AB4
		[MethodImpl(MethodImplOptions.NoInlining)]
		private List<PlayerReinforcementMissionLogic.SummonTroopInfo> GetTroopsToSummon(IEnumerable<TroopRosterElement> rosterElements, int maxCount, Dictionary<CharacterObject, MobileParty> characterToPartyMap = null)
		{
			List<PlayerReinforcementMissionLogic.SummonTroopInfo> list = new List<PlayerReinforcementMissionLogic.SummonTroopInfo>();
			try
			{
				List<Agent> list2 = Enumerable.ToList<Agent>(Enumerable.Where<Agent>(this._summonedAgents, (Agent a) => a == null || !a.IsActive()));
				foreach (Agent agent in list2)
				{
					this._summonedAgents.Remove(agent);
					this._agentToPartyMap.Remove(agent);
				}
				List<TroopRosterElement> list3;
				if (rosterElements == null)
				{
					list3 = null;
				}
				else
				{
					list3 = Enumerable.ToList<TroopRosterElement>(Enumerable.Where<TroopRosterElement>(rosterElements, (TroopRosterElement element) => element.Character != null && element.Number > 0));
				}
				List<TroopRosterElement> list4 = list3 ?? new List<TroopRosterElement>();
				bool flag = list4.Count == 0;
				if (flag)
				{
					return list;
				}
				Dictionary<CharacterObject, int> dictionary = new Dictionary<CharacterObject, int>();
				foreach (TroopRosterElement troopRosterElement in list4)
				{
					int num;
					bool flag2 = !dictionary.TryGetValue(troopRosterElement.Character, out num);
					if (flag2)
					{
						dictionary[troopRosterElement.Character] = troopRosterElement.Number;
					}
					else
					{
						dictionary[troopRosterElement.Character] = num + troopRosterElement.Number;
					}
				}
				bool flag3 = maxCount == int.MaxValue;
				int num2 = flag3 ? int.MaxValue : Math.Max(0, maxCount);
				List<CharacterObject> list5 = Enumerable.ToList<CharacterObject>(Enumerable.Where<CharacterObject>(dictionary.Keys, (CharacterObject character) => character.IsHero && character.HeroObject != null && character.HeroObject.IsPlayerCompanion));
				foreach (CharacterObject characterObject in list5)
				{
					bool flag4 = !flag3 && num2 <= 0;
					if (flag4)
					{
						break;
					}
					bool flag5 = !this.IsHeroAlreadySummoned(characterObject);
					if (flag5)
					{
						MobileParty sourceParty = null;
						MobileParty mobileParty;
						bool flag6 = characterToPartyMap != null && characterToPartyMap.TryGetValue(characterObject, out mobileParty);
						if (flag6)
						{
							sourceParty = mobileParty;
						}
						list.Add(new PlayerReinforcementMissionLogic.SummonTroopInfo(characterObject, 1, sourceParty));
						bool flag7 = !flag3;
						if (flag7)
						{
							num2--;
						}
					}
					dictionary.Remove(characterObject);
				}
				List<KeyValuePair<CharacterObject, int>> list6 = Enumerable.ToList<KeyValuePair<CharacterObject, int>>(Enumerable.ThenByDescending<KeyValuePair<CharacterObject, int>, int>(Enumerable.ThenByDescending<KeyValuePair<CharacterObject, int>, int>(Enumerable.OrderByDescending<KeyValuePair<CharacterObject, int>, int>(Enumerable.Where<KeyValuePair<CharacterObject, int>>(dictionary, (KeyValuePair<CharacterObject, int> kvp) => kvp.Key != null && !kvp.Key.IsHero), (KeyValuePair<CharacterObject, int> kvp) => kvp.Key.Tier), (KeyValuePair<CharacterObject, int> kvp) => kvp.Key.Level), (KeyValuePair<CharacterObject, int> kvp) => kvp.Value));
				foreach (KeyValuePair<CharacterObject, int> keyValuePair in list6)
				{
					bool flag8 = !flag3 && num2 <= 0;
					if (flag8)
					{
						break;
					}
					CharacterObject key = keyValuePair.Key;
					int value = keyValuePair.Value;
					int summonedCountForTroop = this.GetSummonedCountForTroop(key);
					int num3 = value - summonedCountForTroop;
					bool flag9 = num3 <= 0;
					if (!flag9)
					{
						int num4 = flag3 ? num3 : Math.Min(num3, num2);
						bool flag10 = num4 <= 0;
						if (!flag10)
						{
							MobileParty sourceParty2 = null;
							MobileParty mobileParty2;
							bool flag11 = characterToPartyMap != null && characterToPartyMap.TryGetValue(key, out mobileParty2);
							if (flag11)
							{
								sourceParty2 = mobileParty2;
							}
							list.Add(new PlayerReinforcementMissionLogic.SummonTroopInfo(key, num4, sourceParty2));
							bool flag12 = !flag3;
							if (flag12)
							{
								num2 -= num4;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1136045004), ex.Message, ex);
			}
			return list;
		}

		// Token: 0x060009CB RID: 2507 RVA: 0x000B9D3C File Offset: 0x000B7F3C
		private bool IsHeroAlreadySummoned(CharacterObject character)
		{
			bool result;
			try
			{
				bool flag = Mission.Current == null || !character.IsHero;
				if (flag)
				{
					result = false;
				}
				else
				{
					result = Enumerable.Any<Agent>(this._summonedAgents, (Agent a) => a != null && a.IsActive() && a.Character != null && a.Character.StringId == character.StringId);
				}
			}
			catch
			{
				result = false;
			}
			return result;
		}

		// Token: 0x060009CC RID: 2508 RVA: 0x000B9DAC File Offset: 0x000B7FAC
		private int GetSummonedCountForTroop(CharacterObject character)
		{
			int result;
			try
			{
				bool isHero = character.IsHero;
				if (isHero)
				{
					result = (this.IsHeroAlreadySummoned(character) ? 1 : 0);
				}
				else
				{
					result = Enumerable.Count<Agent>(this._summonedAgents, (Agent a) => a != null && a.IsActive() && a.Character != null && a.Character.StringId == character.StringId);
				}
			}
			catch
			{
				result = 0;
			}
			return result;
		}

		// Token: 0x060009CD RID: 2509 RVA: 0x000B9E20 File Offset: 0x000B8020
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void InitializePlayerTeamForControl()
		{
			try
			{
				bool playerTeamInitialized = this._playerTeamInitialized;
				if (!playerTeamInitialized)
				{
					Mission mission = Mission.Current;
					Team team = (mission != null) ? mission.PlayerTeam : null;
					bool flag = team == null || Agent.Main == null;
					if (flag)
					{
						this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-2099249803));
					}
					else
					{
						this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1816357428));
						team.SetPlayerRole(true, false);
						this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-533343601), team.IsPlayerGeneral));
						team.PlayerOrderController.Owner = Agent.Main;
						this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-503748776));
						foreach (Formation formation in team.FormationsIncludingEmpty)
						{
							formation.PlayerOwner = Agent.Main;
							formation.SetControlledByAI(false, false);
							this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1551265540), formation.FormationIndex));
						}
						this.AddCriticalMissionBehaviors();
						bool flag2 = this._orderProvider == null;
						if (flag2)
						{
							this._orderProvider = new SettlementOrderProvider();
							VisualOrderFactory.RegisterProvider(this._orderProvider);
							this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(59513299));
						}
						this._playerTeamInitialized = true;
						this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-914795458));
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1362030830), ex.Message, ex);
			}
		}

		// Token: 0x060009CE RID: 2510 RVA: 0x000BA02C File Offset: 0x000B822C
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void AddCriticalMissionBehaviors()
		{
			try
			{
				this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1088401394));
				bool flag = Mission.Current.GetMissionBehavior<AgentHumanAILogic>() == null;
				if (flag)
				{
					AgentHumanAILogic agentHumanAILogic = new AgentHumanAILogic();
					Mission.Current.AddMissionBehavior(agentHumanAILogic);
					agentHumanAILogic.OnBehaviorInitialize();
					foreach (Agent agent in Mission.Current.Agents)
					{
						bool flag2 = agent != null && agent.IsActive() && agent.IsAIControlled && agent.IsHuman && agent.HumanAIComponent == null;
						if (flag2)
						{
							agent.AddComponent(new HumanAIComponent(agent));
						}
					}
					this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(863685206));
				}
				bool flag3 = Mission.Current.GetMissionBehavior<MountAgentLogic>() == null;
				if (flag3)
				{
					MountAgentLogic mountAgentLogic = new MountAgentLogic();
					Mission.Current.AddMissionBehavior(mountAgentLogic);
					mountAgentLogic.OnBehaviorInitialize();
					this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1859563567));
				}
				bool flag4 = Mission.Current.GetMissionBehavior<AssignPlayerRoleInTeamMissionController>() == null;
				if (flag4)
				{
					AssignPlayerRoleInTeamMissionController assignPlayerRoleInTeamMissionController = new AssignPlayerRoleInTeamMissionController(true, false, false, null);
					Mission.Current.AddMissionBehavior(assignPlayerRoleInTeamMissionController);
					assignPlayerRoleInTeamMissionController.OnBehaviorInitialize();
					this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(860126297));
				}
				this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-2137462570));
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(837109506), ex.Message, ex);
			}
		}

		// Token: 0x060009CF RID: 2511 RVA: 0x000BA21C File Offset: 0x000B841C
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void SelectFormationsWithTroops()
		{
			try
			{
				Mission mission = Mission.Current;
				Team team = (mission != null) ? mission.PlayerTeam : null;
				bool flag = team == null;
				if (!flag)
				{
					this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(476568967));
					List<Formation> list = Enumerable.ToList<Formation>(Enumerable.Where<Formation>(team.FormationsIncludingEmpty, (Formation f) => f != null && f.CountOfUnits > 0));
					this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1518952107), list.Count));
					foreach (Formation formation in list)
					{
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1489933213), formation.FormationIndex, formation.CountOfUnits));
					}
					bool flag2 = list.Count > 0;
					if (flag2)
					{
						team.PlayerOrderController.SelectAllFormations(false);
						this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-2094317264), team.PlayerOrderController.SelectedFormations.Count));
						TextObject textObject = new TextObject(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-245257684), null);
						textObject.SetTextVariable(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-851598683), list.Count);
						this.ShowMessage(textObject.ToString(), \u200D\u206E\u200B\u202E\u206A\u202A\u200E\u202E\u200E\u202E\u202A\u202B\u200C\u206F\u202A\u206E\u206E\u200F\u206B\u202D\u200C\u200F\u206E\u200C\u206E\u202E\u200B\u200D\u202D\u202E\u206D\u206E\u202B\u200F\u200C\u206A\u202A\u202C\u206D\u202E.\u200C\u206B\u200F\u206B\u206A\u206C\u206D\u202D\u202E\u200F\u206B\u202D\u200F\u202A\u202D\u206D\u206A\u206D\u200D\u202A\u206F\u206C\u200B\u202B\u200D\u206C\u200E\u200F\u206D\u206C\u202D\u206B\u200F\u206E\u200D\u200F\u200C\u206F\u202C\u206F\u202E);
					}
					else
					{
						this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1125521164));
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-125903995), ex.Message, ex);
			}
		}

		// Token: 0x060009D0 RID: 2512 RVA: 0x000BA42C File Offset: 0x000B862C
		[MethodImpl(MethodImplOptions.NoInlining)]
		private bool SpawnPlayerTroop(CharacterObject character, bool spawnFromBoundary, MobileParty sourceParty = null)
		{
			bool result;
			try
			{
				bool flag = Mission.Current == null || Agent.Main == null || character == null;
				if (flag)
				{
					result = false;
				}
				else
				{
					bool flag2 = !this._playerTeamInitialized;
					if (flag2)
					{
						this.InitializePlayerTeamForControl();
					}
					int num = this.CountActivePlayerTroops();
					int maxTroopsPerSide = this.GetMaxTroopsPerSide();
					bool flag3 = num >= maxTroopsPerSide;
					if (flag3)
					{
						bool flag4 = this._isAllTroopsSummoned && this._defenderSpawner != null;
						if (flag4)
						{
							this._defenderSpawner.AddToPlayerReserve(character);
							this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1569320899), character.Name, num, maxTroopsPerSide));
							result = false;
						}
						else
						{
							this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(756087224), num, maxTroopsPerSide));
							result = false;
						}
					}
					else
					{
						SettlementCombatManager combatManager = this._combatManager;
						Settlement settlement = (combatManager != null) ? combatManager.GetActiveCombatSettlement() : null;
						bool flag7;
						if (settlement != null && (settlement.IsTown || settlement.IsCastle || settlement.IsVillage))
						{
							if (!this._combatManager.IsCurrentLocationLargeOutdoor())
							{
								Mission mission = Mission.Current;
								bool flag5;
								if (mission == null)
								{
									flag5 = false;
								}
								else
								{
									string sceneName = mission.SceneName;
									flag5 = ((sceneName != null) ? new bool?(sceneName.ToLower().Contains(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1202074811))) : null).GetValueOrDefault();
								}
								if (!flag5)
								{
									Mission mission2 = Mission.Current;
									bool flag6;
									if (mission2 == null)
									{
										flag6 = false;
									}
									else
									{
										string sceneName2 = mission2.SceneName;
										flag6 = ((sceneName2 != null) ? new bool?(sceneName2.ToLower().Contains(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1771895972))) : null).GetValueOrDefault();
									}
									if (!flag6)
									{
										Mission mission3 = Mission.Current;
										if (mission3 == null)
										{
											flag7 = false;
										}
										else
										{
											string sceneName3 = mission3.SceneName;
											flag7 = ((sceneName3 != null) ? new bool?(sceneName3.ToLower().Contains(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-906929800))) : null).GetValueOrDefault();
										}
										goto IL_20A;
									}
								}
							}
							flag7 = true;
							IL_20A:;
						}
						else
						{
							flag7 = false;
						}
						bool flag8 = flag7;
						Vec3 vec = (spawnFromBoundary && flag8) ? this.GetBoundarySpawnPosition() : this.GetLocalSpawnPosition();
						MobileParty mainParty = MobileParty.MainParty;
						IAgentOriginBase troopOrigin = new PartyAgentOrigin(mainParty.Party, character, -1, default(UniqueTroopDescriptor), false, false);
						AgentBuildData agentBuildData = new AgentBuildData(character).Team(Mission.Current.PlayerTeam).TroopOrigin(troopOrigin).InitialPosition(vec);
						Vec2 movementDirection = Agent.Main.GetMovementDirection();
						AgentBuildData agentBuildData2 = agentBuildData.InitialDirection(movementDirection).Equipment(character.Equipment).NoHorses(false).Controller(1).ClothingColor1(Mission.Current.PlayerTeam.Color).ClothingColor2(Mission.Current.PlayerTeam.Color2);
						Agent agent = Mission.Current.SpawnAgent(agentBuildData2, false);
						bool flag9 = agent != null;
						if (flag9)
						{
							Formation formationForCharacter = this.GetFormationForCharacter(character);
							bool flag10 = formationForCharacter != null;
							if (flag10)
							{
								agent.Formation = formationForCharacter;
							}
							bool flag11 = this._combatManager != null && this._combatManager.IsActiveCombat();
							if (flag11)
							{
								this.WieldBestWeapon(agent);
								agent.SetWatchState(Agent.WatchState.Alarmed);
								this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1223741365));
							}
							this._summonedAgents.Add(agent);
							bool flag12 = sourceParty != null;
							if (flag12)
							{
								this._agentToPartyMap[agent] = sourceParty;
								this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1837435335), agent.Name, sourceParty.Name));
							}
							else
							{
								this._agentToPartyMap[agent] = MobileParty.MainParty;
							}
							result = true;
						}
						else
						{
							result = false;
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1661939866), ex.Message, ex);
				result = false;
			}
			return result;
		}

		// Token: 0x060009D1 RID: 2513 RVA: 0x000BA838 File Offset: 0x000B8A38
		[MethodImpl(MethodImplOptions.NoInlining)]
		private Formation GetFormationForCharacter(CharacterObject character)
		{
			Formation result;
			try
			{
				Mission mission = Mission.Current;
				Team team = (mission != null) ? mission.PlayerTeam : null;
				bool flag = team == null;
				if (flag)
				{
					result = null;
				}
				else
				{
					bool isMounted = character.IsMounted;
					FormationClass formationClass;
					if (isMounted)
					{
						bool isRanged = character.IsRanged;
						if (isRanged)
						{
							formationClass = FormationClass.HorseArcher;
						}
						else
						{
							formationClass = FormationClass.Cavalry;
						}
					}
					else
					{
						bool isRanged2 = character.IsRanged;
						if (isRanged2)
						{
							formationClass = FormationClass.Ranged;
						}
						else
						{
							formationClass = FormationClass.Infantry;
						}
					}
					result = team.GetFormation(formationClass);
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-589354921), ex.Message, ex);
				Mission mission2 = Mission.Current;
				Formation formation;
				if (mission2 == null)
				{
					formation = null;
				}
				else
				{
					Team playerTeam = mission2.PlayerTeam;
					formation = ((playerTeam != null) ? playerTeam.GetFormation(FormationClass.Infantry) : null);
				}
				result = formation;
			}
			return result;
		}

		// Token: 0x060009D2 RID: 2514 RVA: 0x000BA900 File Offset: 0x000B8B00
		[MethodImpl(MethodImplOptions.NoInlining)]
		private Vec3 GetLocalSpawnPosition()
		{
			Vec3 result;
			try
			{
				bool flag = Mission.Current == null || Mission.Current.Scene == null;
				if (flag)
				{
					result = Vec3.Zero;
				}
				else
				{
					Random random = new Random(Guid.NewGuid().GetHashCode());
					Vec3 vec = Vec3.Zero;
					bool flag2 = false;
					string[] array = new string[]
					{
						<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(603513864),
						<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(381413183),
						<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-757013729),
						<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(511193370),
						<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-931747223)
					};
					foreach (string tag in array)
					{
						GameEntity gameEntity = Mission.Current.Scene.FindEntityWithTag(tag);
						bool flag3 = gameEntity != null;
						if (flag3)
						{
							MatrixFrame globalFrame = gameEntity.GetGlobalFrame();
							vec = globalFrame.origin;
							flag2 = true;
							break;
						}
					}
					bool flag4 = !flag2;
					if (flag4)
					{
						Vec3 vec2;
						if (!(this._playerInitialSpawnPosition != Vec3.Zero))
						{
							Agent main = Agent.Main;
							vec2 = ((main != null) ? main.Position : Vec3.Zero);
						}
						else
						{
							vec2 = this._playerInitialSpawnPosition;
						}
						vec = vec2;
						this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1478037198), vec));
					}
					Vec3 vec3;
					bool flag5 = this.TryGetMainGateSpawnPosition(out vec3);
					if (flag5)
					{
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1714864990), vec3));
						this._playerInitialSpawnPosition = vec3;
						result = vec3;
					}
					else
					{
						Vec2 vec4 = this._playerInitialForward2D;
						bool flag6 = vec4.LengthSquared < 0.001f;
						if (flag6)
						{
							vec4 = ((Agent.Main != null) ? Agent.Main.LookDirection.AsVec2 : new Vec2(0f, 1f));
						}
						bool flag7 = vec4.LengthSquared > 0.001f;
						if (flag7)
						{
							vec4.Normalize();
						}
						Vec2 v = new Vec2(vec4.y, -vec4.x);
						float num = 12f + (float)(random.NextDouble() * 6.0);
						float num2 = (float)(random.NextDouble() * 8.0) - 4f;
						Vec2 vec5 = vec.AsVec2 - vec4 * num + v * num2;
						float z = vec.z;
						Mission.Current.Scene.GetHeightAtPoint(vec5, (BodyFlags)544321929U, ref z);
						Vec3 vec6 = new Vec3(vec5.x, vec5.y, z, -1f);
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-289089794), vec6, num, num2));
						result = vec6;
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1794849132), ex.Message, ex);
				Agent main2 = Agent.Main;
				result = ((main2 != null) ? main2.Position : Vec3.Zero);
			}
			return result;
		}

		// Token: 0x060009D3 RID: 2515 RVA: 0x000BAC50 File Offset: 0x000B8E50
		[MethodImpl(MethodImplOptions.NoInlining)]
		private Vec3 GetBoundarySpawnPosition()
		{
			Vec3 result;
			try
			{
				Mission mission = Mission.Current;
				bool flag = mission == null || mission.Scene == null || Agent.Main == null;
				if (flag)
				{
					result = Vec3.Zero;
				}
				else
				{
					Vec3 vec;
					bool flag2 = this.TryGetMainGateSpawnPosition(out vec);
					if (flag2)
					{
						this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-2061313363), vec));
						result = vec;
					}
					else
					{
						Random random = new Random(Guid.NewGuid().GetHashCode());
						Vec3 position = Agent.Main.Position;
						for (int i = 0; i < 10; i++)
						{
							float num = (float)(random.NextDouble() * 3.141592653589793 * 2.0);
							Vec2 v = new Vec2((float)Math.Cos((double)num), (float)Math.Sin((double)num));
							Vec2 position2 = position.AsVec2 + v * 30f;
							Vec2 closestBoundaryPosition = mission.GetClosestBoundaryPosition(position2);
							float f = 5f + (float)(random.NextDouble() * 5.0);
							Vec2 v2 = (closestBoundaryPosition - position.AsVec2).Normalized();
							Vec2 vec2 = closestBoundaryPosition - v2 * f;
							float z = 0f;
							mission.Scene.GetHeightAtPoint(vec2, (BodyFlags)544321929U, ref z);
							Vec3 vec3 = new Vec3(vec2.x, vec2.y, z, -1f);
							bool flag3 = this.IsPositionFlat(mission, vec3);
							if (flag3)
							{
								return vec3;
							}
						}
						this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(158030341));
						result = this.GetLocalSpawnPosition();
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1093551691), ex.Message, ex);
				Agent main = Agent.Main;
				result = ((main != null) ? main.Position : Vec3.Zero);
			}
			return result;
		}

		// Token: 0x060009D4 RID: 2516 RVA: 0x000BAE7C File Offset: 0x000B907C
		[MethodImpl(MethodImplOptions.NoInlining)]
		private bool TryGetMainGateSpawnPosition(out Vec3 spawnPosition)
		{
			spawnPosition = Vec3.Zero;
			bool result;
			try
			{
				Mission mission = Mission.Current;
				bool flag = mission == null || mission.Scene == null;
				if (flag)
				{
					result = false;
				}
				else
				{
					GameEntity gameEntity = mission.Scene.FindEntityWithTag(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1954610274));
					bool flag2 = gameEntity == null;
					if (flag2)
					{
						gameEntity = mission.Scene.FindEntityWithTag(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-2084022445));
					}
					bool flag3 = gameEntity == null;
					if (flag3)
					{
						gameEntity = mission.Scene.FindEntityWithTag(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1089845579));
					}
					bool flag4 = gameEntity == null;
					if (flag4)
					{
						result = false;
					}
					else
					{
						MatrixFrame globalFrame = gameEntity.GetGlobalFrame();
						Vec3 vec = globalFrame.rotation.f;
						bool flag5 = vec.LengthSquared < 0.001f;
						if (flag5)
						{
							vec = globalFrame.rotation.s;
						}
						bool flag6 = vec.LengthSquared < 0.001f;
						if (flag6)
						{
							vec = Vec3.Forward;
						}
						vec.Normalize();
						Vec3 s = globalFrame.rotation.s;
						bool flag7 = s.LengthSquared < 0.001f;
						if (flag7)
						{
							s = new Vec3(vec.y, -vec.x, 0f, -1f);
						}
						s.Normalize();
						Random random = new Random(Guid.NewGuid().GetHashCode());
						float f = 25f;
						float f2 = (float)(random.NextDouble() * 8.0) - 4f;
						Vec3 vec2 = globalFrame.origin - vec * f + s * f2;
						float z = vec2.z;
						mission.Scene.GetHeightAtPoint(vec2.AsVec2, (BodyFlags)544321929U, ref z);
						vec2.z = z;
						spawnPosition = vec2;
						this._playerInitialForward2D = new Vec2(vec.x, vec.y);
						this._playerInitialSpawnPosition = spawnPosition;
						this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(658376501), spawnPosition));
						result = true;
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1059954217), ex.Message, ex);
				result = false;
			}
			return result;
		}

		// Token: 0x060009D5 RID: 2517 RVA: 0x000BB104 File Offset: 0x000B9304
		private bool IsPositionFlat(Mission mission, Vec3 position)
		{
			bool result;
			try
			{
				float num = 2f;
				float z = position.z;
				Vec2[] array = new Vec2[]
				{
					new Vec2(position.x + num, position.y),
					new Vec2(position.x - num, position.y),
					new Vec2(position.x, position.y + num),
					new Vec2(position.x, position.y - num)
				};
				float num2 = 0f;
				foreach (Vec2 point in array)
				{
					float num3 = 0f;
					mission.Scene.GetHeightAtPoint(point, (BodyFlags)544321929U, ref num3);
					float num4 = Math.Abs(num3 - z);
					bool flag = num4 > num2;
					if (flag)
					{
						num2 = num4;
					}
				}
				result = (num2 <= 3f);
			}
			catch
			{
				result = true;
			}
			return result;
		}

		// Token: 0x060009D6 RID: 2518 RVA: 0x000BB218 File Offset: 0x000B9418
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void CleanupDeadAgents()
		{
			try
			{
				bool flag = this._summonedAgents.Count == 0;
				if (!flag)
				{
					List<Agent> list = Enumerable.ToList<Agent>(Enumerable.Where<Agent>(this._summonedAgents, (Agent a) => a == null || !a.IsActive()));
					foreach (Agent agent in list)
					{
						this._summonedAgents.Remove(agent);
						this._agentToPartyMap.Remove(agent);
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(2094896599), ex.Message, ex);
			}
		}

		// Token: 0x060009D7 RID: 2519 RVA: 0x000BB2FC File Offset: 0x000B94FC
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void ShowSummonHint()
		{
			try
			{
				this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1851588227));
				SettlementCombatLogger logger = this._logger;
				string str = <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1818614099);
				Settlement currentSettlement = Settlement.CurrentSettlement;
				string text;
				if (currentSettlement == null)
				{
					text = null;
				}
				else
				{
					TextObject name = currentSettlement.Name;
					text = ((name != null) ? name.ToString() : null);
				}
				logger.Log(str + (text ?? <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1099651297)));
				SettlementCombatLogger logger2 = this._logger;
				string str2 = <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(224336704);
				Mission mission = Mission.Current;
				logger2.Log(str2 + (((mission != null) ? mission.SceneName : null) ?? <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1287652219)));
				SettlementCombatLogger logger3 = this._logger;
				string format = <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-447865166);
				SettlementCombatManager combatManager = this._combatManager;
				logger3.Log(string.Format(format, combatManager != null && combatManager.IsActiveCombat()));
				TextObject textObject = new TextObject(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(969089176), null);
				InformationManager.DisplayMessage(new InformationMessage(textObject.ToString(), Colors.Gray));
				this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(110620938) + textObject.ToString());
				TextObject textObject2 = new TextObject(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1184969076), null);
				InformationManager.DisplayMessage(new InformationMessage(textObject2.ToString(), Colors.Gray));
				this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-542211010) + textObject2.ToString());
				TextObject textObject3 = new TextObject(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(39761824), null);
				InformationManager.DisplayMessage(new InformationMessage(textObject3.ToString(), Colors.Gray));
				this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1096058516) + textObject3.ToString());
				this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-462382096));
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1756256126), ex.Message, ex);
			}
		}

		// Token: 0x060009D8 RID: 2520 RVA: 0x000BB520 File Offset: 0x000B9720
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void ShowMessage(string message, Color color)
		{
			try
			{
				InformationManager.DisplayMessage(new InformationMessage(message, color));
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-2086354931), ex.Message, ex);
			}
		}

		// Token: 0x060009D9 RID: 2521 RVA: 0x000BB574 File Offset: 0x000B9774
		[MethodImpl(MethodImplOptions.NoInlining)]
		private int GetMaxTroopsPerSide()
		{
			int result;
			try
			{
				bool flag = Mission.Current == null;
				if (flag)
				{
					result = 75;
				}
				else
				{
					string sceneName = Mission.Current.SceneName;
					string text = ((sceneName != null) ? sceneName.ToLower() : null) ?? "";
					result = ((text.Contains(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(340344811)) || (text.Contains(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1586385775)) && text.Contains(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-602230111))) || text.Contains(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1750345065)) || text.Contains(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(40552433)) || text.Contains(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(847495609)) || text.Contains(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1228993677)) || (text.Contains(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1657795082)) && !text.Contains(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-906929800)))) ? 5 : 75);
				}
			}
			catch
			{
				result = 75;
			}
			return result;
		}

		// Token: 0x060009DA RID: 2522 RVA: 0x000BB69C File Offset: 0x000B989C
		private int CountActivePlayerTroops()
		{
			int result;
			try
			{
				bool flag = Mission.Current == null || Mission.Current.PlayerTeam == null;
				if (flag)
				{
					result = 0;
				}
				else
				{
					result = Enumerable.Count<Agent>(Mission.Current.Agents, (Agent a) => a != null && a.IsActive() && a.IsHuman && a.Team == Mission.Current.PlayerTeam);
				}
			}
			catch
			{
				result = 0;
			}
			return result;
		}

		// Token: 0x060009DB RID: 2523 RVA: 0x000BB714 File Offset: 0x000B9914
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void WieldBestWeapon(Agent agent)
		{
			try
			{
				bool flag = agent == null || !agent.IsActive();
				if (!flag)
				{
					EquipmentIndex equipmentIndex = EquipmentIndex.None;
					for (EquipmentIndex equipmentIndex2 = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex2 < EquipmentIndex.NumAllWeaponSlots; equipmentIndex2++)
					{
						bool flag2 = !agent.Equipment[equipmentIndex2].IsEmpty;
						if (flag2)
						{
							WeaponClass weaponClass = agent.Equipment[equipmentIndex2].CurrentUsageItem.WeaponClass;
							bool flag3 = weaponClass == WeaponClass.OneHandedSword || weaponClass == WeaponClass.TwoHandedSword || weaponClass == WeaponClass.OneHandedAxe || weaponClass == WeaponClass.TwoHandedAxe || weaponClass == WeaponClass.Mace || weaponClass == WeaponClass.TwoHandedMace || weaponClass == WeaponClass.Pick || weaponClass == WeaponClass.Dagger || weaponClass == WeaponClass.OneHandedPolearm || weaponClass == WeaponClass.TwoHandedPolearm || weaponClass == WeaponClass.LowGripPolearm;
							if (flag3)
							{
								equipmentIndex = equipmentIndex2;
								break;
							}
							bool flag4 = equipmentIndex == EquipmentIndex.None;
							if (flag4)
							{
								equipmentIndex = equipmentIndex2;
							}
						}
					}
					bool flag5 = equipmentIndex != EquipmentIndex.None;
					if (flag5)
					{
						agent.TryToWieldWeaponInSlot(equipmentIndex, Agent.WeaponWieldActionType.InstantAfterPickUp, false);
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-993690489), ex.Message, ex);
			}
		}

		// Token: 0x060009DC RID: 2524 RVA: 0x000BB83C File Offset: 0x000B9A3C
		[MethodImpl(MethodImplOptions.NoInlining)]
		public int TransferLordTroopsToDefenderTeam(Hero lord)
		{
			int num = 0;
			try
			{
				bool flag = lord == null || Mission.Current == null || Mission.Current.DefenderTeam == null;
				if (flag)
				{
					this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-615461392));
					return 0;
				}
				MobileParty partyBelongedTo = lord.PartyBelongedTo;
				bool flag2 = partyBelongedTo == null;
				if (flag2)
				{
					this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(2134418073), lord.Name));
					return 0;
				}
				this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(341735301), lord.Name, partyBelongedTo.Name));
				List<Agent> list = new List<Agent>();
				foreach (KeyValuePair<Agent, MobileParty> keyValuePair in Enumerable.ToList<KeyValuePair<Agent, MobileParty>>(this._agentToPartyMap))
				{
					Agent key = keyValuePair.Key;
					MobileParty value = keyValuePair.Value;
					bool flag3 = key == null || !key.IsActive() || key.Team != Mission.Current.PlayerTeam;
					if (!flag3)
					{
						bool flag4 = value == partyBelongedTo;
						bool flag5 = partyBelongedTo.Army != null && partyBelongedTo.Army.LeaderParty == partyBelongedTo && value != null && value.Army == partyBelongedTo.Army;
						bool flag6 = flag4 || flag5;
						if (flag6)
						{
							list.Add(key);
							this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-668386516) + key.Name + <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-2065881414) + (flag4 ? <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-758594049) : <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(21946399)));
						}
					}
				}
				foreach (Agent agent in list)
				{
					try
					{
						bool flag7 = agent == null || !agent.IsActive();
						if (!flag7)
						{
							agent.SetTeam(Mission.Current.DefenderTeam, true);
							agent.SetWatchState(Agent.WatchState.Alarmed);
							this.WieldBestWeapon(agent);
							bool flag8 = Agent.Main != null;
							if (flag8)
							{
								agent.SetLookAgent(Agent.Main);
								agent.SetAgentFlags(agent.GetAgentFlags() | AgentFlag.CanAttack | AgentFlag.CanDefend);
							}
							num++;
							this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1458529977) + agent.Name + <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-708080359));
						}
					}
					catch (Exception ex)
					{
						this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1343080209) + ((agent != null) ? agent.Name : null), ex.Message, ex);
					}
				}
				this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-381449460), num, lord.Name));
			}
			catch (Exception ex2)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1461426076), ex2.Message, ex2);
			}
			return num;
		}

		// Token: 0x060009DE RID: 2526 RVA: 0x000BBC50 File Offset: 0x000B9E50
		[CompilerGenerated]
		internal static void <GetTroopSourceParties>g__AddParty|37_0(MobileParty party, ref PlayerReinforcementMissionLogic.<>c__DisplayClass37_0 A_1, ref PlayerReinforcementMissionLogic.<>c__DisplayClass37_1 A_2)
		{
			bool flag = party == null || party.MemberRoster == null;
			if (!flag)
			{
				bool flag2 = !A_2.visited.Add(party);
				if (!flag2)
				{
					bool flag3 = party.MemberRoster.TotalManCount <= 0;
					if (!flag3)
					{
						A_1.result.Add(party);
					}
				}
			}
		}

		// Token: 0x040005F3 RID: 1523
		private readonly AIInfluenceBehavior _behavior;

		// Token: 0x040005F4 RID: 1524
		private readonly SettlementCombatLogger _logger;

		// Token: 0x040005F5 RID: 1525
		private SettlementCombatManager _combatManager;

		// Token: 0x040005F6 RID: 1526
		private DefenderSpawner _defenderSpawner;

		// Token: 0x040005F7 RID: 1527
		private bool _messageShown = false;

		// Token: 0x040005F8 RID: 1528
		private bool _followModeHintShown = false;

		// Token: 0x040005F9 RID: 1529
		private bool _canSummonTroops = false;

		// Token: 0x040005FA RID: 1530
		private bool _previousCanSummonTroops = false;

		// Token: 0x040005FB RID: 1531
		private int _totalSummoned = 0;

		// Token: 0x040005FC RID: 1532
		private Vec3 _playerInitialSpawnPosition = Vec3.Zero;

		// Token: 0x040005FD RID: 1533
		private Vec2 _playerInitialForward2D = Vec2.Zero;

		// Token: 0x040005FE RID: 1534
		private HashSet<Agent> _summonedAgents = new HashSet<Agent>();

		// Token: 0x040005FF RID: 1535
		private Dictionary<Agent, MobileParty> _agentToPartyMap = new Dictionary<Agent, MobileParty>();

		// Token: 0x04000600 RID: 1536
		private bool _playerTeamInitialized = false;

		// Token: 0x04000601 RID: 1537
		private SettlementOrderProvider _orderProvider = null;

		// Token: 0x04000602 RID: 1538
		private const int TROOPS_PER_SUMMON = 10;

		// Token: 0x04000603 RID: 1539
		private const int MAX_TROOPS_PER_SIDE_LARGE = 75;

		// Token: 0x04000604 RID: 1540
		private const int MAX_TROOPS_PER_SIDE_SMALL = 5;

		// Token: 0x04000605 RID: 1541
		private bool _isAllTroopsSummoned = false;

		// Token: 0x04000606 RID: 1542
		private bool _loggedRestrictedLocation = false;

		// Token: 0x04000607 RID: 1543
		private bool _loggedNotCityOrCastle = false;

		// Token: 0x04000608 RID: 1544
		private bool _loggedCombatNotActive = false;

		// Token: 0x04000609 RID: 1545
		private bool _loggedPromptNotReady = false;

		// Token: 0x0400060A RID: 1546
		private static readonly string[] MAIN_GATE_SPAWN_TAGS = new string[]
		{
			<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1954610274),
			<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1089845579),
			<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1407448206),
			<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(2081305382),
			<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(254050794)
		};

		// Token: 0x0400060B RID: 1547
		private bool _onMissionTickCalled = false;

		// Token: 0x02000135 RID: 309
		private class SummonTroopInfo
		{
			// Token: 0x170001EF RID: 495
			// (get) Token: 0x060009DF RID: 2527 RVA: 0x0001D613 File Offset: 0x0001B813
			public CharacterObject Character { get; }

			// Token: 0x170001F0 RID: 496
			// (get) Token: 0x060009E0 RID: 2528 RVA: 0x0001D61B File Offset: 0x0001B81B
			public int Count { get; }

			// Token: 0x170001F1 RID: 497
			// (get) Token: 0x060009E1 RID: 2529 RVA: 0x0001D623 File Offset: 0x0001B823
			public MobileParty SourceParty { get; }

			// Token: 0x060009E2 RID: 2530 RVA: 0x0001D62B File Offset: 0x0001B82B
			public SummonTroopInfo(CharacterObject character, int count, MobileParty sourceParty = null)
			{
				this.Character = character;
				this.Count = count;
				this.SourceParty = sourceParty;
			}
		}
	}
}
