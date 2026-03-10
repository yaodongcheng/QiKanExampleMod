using System;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x02000153 RID: 339
	public class SettlementCombatMissionLogic : MissionLogic
	{
		// Token: 0x06000ABC RID: 2748 RVA: 0x000C43E4 File Offset: 0x000C25E4
		public SettlementCombatMissionLogic(CombatStatistics statistics, AIInfluenceBehavior behavior, CivilianBehavior civilianBehavior, SettlementCombatManager combatManager)
		{
			this._statistics = statistics;
			this._behavior = behavior;
			this._civilianBehavior = civilianBehavior;
			this._combatManager = combatManager;
			this._defenderSpawner = ((combatManager != null) ? combatManager.GetDefenderSpawner() : null);
			this._logger = SettlementCombatLogger.Instance;
			bool flag = Mission.Current != null;
			if (flag)
			{
				Mission.Current.GetOverriddenFleePositionForAgent += this.GetOverriddenFleePositionForAgent;
				this._eventSubscribed = true;
			}
		}

		// Token: 0x06000ABD RID: 2749 RVA: 0x000C4474 File Offset: 0x000C2674
		public override void AfterStart()
		{
			base.AfterStart();
			bool flag = !this._eventSubscribed && Mission.Current != null;
			if (flag)
			{
				Mission.Current.GetOverriddenFleePositionForAgent += this.GetOverriddenFleePositionForAgent;
				this._eventSubscribed = true;
			}
		}

		// Token: 0x06000ABE RID: 2750 RVA: 0x000C44C0 File Offset: 0x000C26C0
		[MethodImpl(MethodImplOptions.NoInlining)]
		private WorldPosition? GetOverriddenFleePositionForAgent(Agent agent)
		{
			WorldPosition? result;
			try
			{
				bool flag = this._civilianBehavior != null;
				if (flag)
				{
					bool flag2 = this._civilianBehavior.IsAgentPanicking(agent);
					bool flag3 = this._civilianBehavior.IsAgentFighting(agent);
					this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-661936453), agent.Name, flag2, flag3));
				}
				bool flag4 = this._civilianBehavior != null && this._civilianBehavior.IsAgentPanicking(agent);
				if (flag4)
				{
					result = null;
				}
				else
				{
					result = null;
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-638295112), ex.Message, ex);
				result = null;
			}
			return result;
		}

		// Token: 0x06000ABF RID: 2751 RVA: 0x000C45A4 File Offset: 0x000C27A4
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override void OnMissionTick(float dt)
		{
			base.OnMissionTick(dt);
			try
			{
				CivilianBehavior civilianBehavior = this._civilianBehavior;
				if (civilianBehavior != null)
				{
					civilianBehavior.OnTick(dt);
				}
				DefenderSpawner defenderSpawner = this._defenderSpawner;
				if (defenderSpawner != null)
				{
					defenderSpawner.OnTick(dt);
				}
				this._combatCheckTimer += dt;
				bool flag = this._combatCheckTimer >= 2f;
				if (flag)
				{
					this._combatCheckTimer = 0f;
					this.CheckCombatEnd();
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1405769708), ex.Message, ex);
			}
		}

		// Token: 0x06000AC0 RID: 2752 RVA: 0x000C4650 File Offset: 0x000C2850
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void CheckCombatEnd()
		{
			try
			{
				bool flag = Mission.Current == null || Mission.Current.PlayerTeam == null || Mission.Current.DefenderTeam == null;
				if (!flag)
				{
					bool flag2 = Mission.Current.Mode != MissionMode.Battle;
					if (!flag2)
					{
						int num = 0;
						foreach (Agent agent in Mission.Current.DefenderTeam.ActiveAgents)
						{
							bool flag3 = agent == null || !agent.IsActive() || !agent.IsHuman;
							if (!flag3)
							{
								bool flag4 = agent.GetAgentFlags().HasAnyFlag(AgentFlag.CanAttack);
								if (flag4)
								{
									num++;
								}
							}
						}
						DefenderSpawner defenderSpawner = this._combatManager.GetDefenderSpawner();
						bool flag5 = defenderSpawner != null && defenderSpawner.HasPendingSpawns();
						bool flag6 = num == 0;
						if (flag6)
						{
							bool flag7 = flag5;
							if (flag7)
							{
								this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1036905309) + defenderSpawner.GetPendingSpawnsInfo());
							}
							else
							{
								this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(225692689));
								this._combatManager.OnAllEnemiesEliminated();
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1045229171), ex.Message, ex);
			}
		}

		// Token: 0x06000AC1 RID: 2753 RVA: 0x000C47F8 File Offset: 0x000C29F8
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override void OnAgentCreated(Agent agent)
		{
			base.OnAgentCreated(agent);
			try
			{
				bool flag = agent == null || !agent.IsHuman;
				if (!flag)
				{
					CharacterObject characterObject = agent.Character as CharacterObject;
					bool flag2 = characterObject == null || string.IsNullOrEmpty(characterObject.StringId);
					if (!flag2)
					{
						CompanionCombatDecision companionCombatDecision;
						bool flag3 = this._combatManager != null && this._combatManager.TryGetCompanionDecision(characterObject.StringId, out companionCombatDecision);
						if (flag3)
						{
							this._combatManager.ApplyCompanionDecisionToAgent(agent);
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(135966267), ex.Message, ex);
			}
		}

		// Token: 0x06000AC2 RID: 2754 RVA: 0x000C48B4 File Offset: 0x000C2AB4
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow blow)
		{
			try
			{
				bool flag = affectedAgent == null || !affectedAgent.IsHuman;
				if (!flag)
				{
					bool flag2 = agentState == AgentState.Killed;
					bool flag3 = agentState == AgentState.Unconscious;
					bool flag4 = !flag2 && !flag3;
					if (!flag4)
					{
						bool flag5 = flag3 && affectedAgent == Agent.Main && this._combatManager != null;
						if (flag5)
						{
							try
							{
								float num = 0f;
								float num2 = 0f;
								Mission mission = Mission.Current;
								bool flag6 = ((mission != null) ? mission.PlayerTeam : null) != null;
								if (flag6)
								{
									foreach (Agent agent in Mission.Current.PlayerTeam.ActiveAgents)
									{
										bool flag7 = agent != null && agent.IsHuman && agent.Health > 0f;
										if (flag7)
										{
											num += agent.CharacterPowerCached;
										}
									}
								}
								Mission mission2 = Mission.Current;
								bool flag8 = ((mission2 != null) ? mission2.DefenderTeam : null) != null;
								if (flag8)
								{
									foreach (Agent agent2 in Mission.Current.DefenderTeam.ActiveAgents)
									{
										bool flag9 = agent2 != null && agent2.IsHuman && agent2.Health > 0f;
										if (flag9)
										{
											num2 += agent2.CharacterPowerCached;
										}
									}
								}
								this._combatManager.OnPlayerKnockedOut(num, num2);
							}
							catch (Exception ex)
							{
								this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1980192015), ex.Message, ex);
							}
						}
						string victimName = affectedAgent.Name ?? <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-57576760);
						BasicCharacterObject character = affectedAgent.Character;
						string victimId = ((character != null) ? character.StringId : null) ?? <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1847268266);
						bool flag10 = this.IsCivilian(affectedAgent.Character);
						CharacterObject characterObject = affectedAgent.Character as CharacterObject;
						bool isCivilianFemale = flag10 && characterObject != null && characterObject.IsFemale;
						Campaign campaign = Campaign.Current;
						AgeModel ageModel;
						if (campaign == null)
						{
							ageModel = null;
						}
						else
						{
							GameModels models = campaign.Models;
							ageModel = ((models != null) ? models.AgeModel : null);
						}
						AgeModel ageModel2 = ageModel;
						bool flag11 = ageModel2 != null && affectedAgent.Age < (float)ageModel2.BecomeTeenagerAge;
						bool isCivilianChild = flag10 && flag11;
						bool isImportant = this.IsImportantCharacter(characterObject);
						CombatSide agentSide = this.GetAgentSide(affectedAgent);
						Mission mission3 = Mission.Current;
						PlayerReinforcementMissionLogic playerReinforcementMissionLogic = (mission3 != null) ? mission3.GetMissionBehavior<PlayerReinforcementMissionLogic>() : null;
						bool isPlayerTroop = playerReinforcementMissionLogic != null && playerReinforcementMissionLogic.IsSummonedTroop(affectedAgent);
						string text = <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1617165097);
						string text2 = <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1604186482);
						bool isPlayerTroop2 = false;
						bool flag12 = affectorAgent != null;
						if (flag12)
						{
							text = (affectorAgent.Name ?? <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1617165097));
							BasicCharacterObject character2 = affectorAgent.Character;
							text2 = (((character2 != null) ? character2.StringId : null) ?? <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1999575292));
							isPlayerTroop2 = (playerReinforcementMissionLogic != null && playerReinforcementMissionLogic.IsSummonedTroop(affectorAgent));
						}
						else
						{
							bool flag13 = blow.OwnerId >= 0 && blow.OwnerId < base.Mission.Agents.Count;
							if (flag13)
							{
								Agent agent3 = base.Mission.Agents.Find((Agent a) => a != null && a.Index == blow.OwnerId);
								bool flag14 = agent3 != null;
								if (flag14)
								{
									text = (agent3.Name ?? <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-57576760));
									BasicCharacterObject character3 = agent3.Character;
									text2 = (((character3 != null) ? character3.StringId : null) ?? <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1045287129));
									isPlayerTroop2 = (playerReinforcementMissionLogic != null && playerReinforcementMissionLogic.IsSummonedTroop(agent3));
								}
							}
						}
						string victimType = this.DetermineAgentType(affectedAgent, isPlayerTroop, flag10);
						string text3 = (affectorAgent != null) ? this.DetermineAgentType(affectorAgent, isPlayerTroop2, false) : <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(378266911);
						bool flag15 = flag2;
						if (flag15)
						{
							this._statistics.RecordDeath(victimName, victimId, victimType, text, text2, text3, flag10, agentSide, isCivilianFemale, isCivilianChild, isImportant);
						}
						else
						{
							bool flag16 = flag3;
							if (flag16)
							{
								this._statistics.RecordWound(victimName, victimId, victimType, text, text2, text3, flag10, agentSide, isCivilianFemale, isCivilianChild, isImportant);
							}
						}
					}
				}
			}
			catch (Exception ex2)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1098385362), ex2.Message, ex2);
			}
		}

		// Token: 0x06000AC3 RID: 2755 RVA: 0x000C4DA8 File Offset: 0x000C2FA8
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override void OnRemoveBehavior()
		{
			try
			{
				bool flag = this._eventSubscribed && Mission.Current != null;
				if (flag)
				{
					Mission.Current.GetOverriddenFleePositionForAgent -= this.GetOverriddenFleePositionForAgent;
					this._eventSubscribed = false;
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(714491714), ex.Message, ex);
			}
			base.OnRemoveBehavior();
		}

		// Token: 0x06000AC4 RID: 2756 RVA: 0x000C4E2C File Offset: 0x000C302C
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override InquiryData OnEndMissionRequest(out bool canPlayerLeave)
		{
			InquiryData result;
			try
			{
				SettlementCombatManager combatManager = this._combatManager;
				bool flag = combatManager != null && combatManager.ShouldBlockMissionExit();
				bool flag2 = flag;
				if (flag2)
				{
					canPlayerLeave = false;
					TextObject textObject = new TextObject(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(831381968), null);
					MBInformationManager.AddQuickInformation(textObject, 0, null, null, "");
					result = null;
				}
				else
				{
					canPlayerLeave = true;
					result = null;
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-467990037), ex.Message, ex);
				canPlayerLeave = true;
				result = null;
			}
			return result;
		}

		// Token: 0x06000AC5 RID: 2757 RVA: 0x000C4EC4 File Offset: 0x000C30C4
		[MethodImpl(MethodImplOptions.NoInlining)]
		private string DetermineAgentType(Agent agent, bool isPlayerTroop, bool isCivilian)
		{
			bool flag = agent == null;
			string result;
			if (flag)
			{
				result = <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(665038079);
			}
			else if (isPlayerTroop)
			{
				result = <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(2103618246);
			}
			else if (isCivilian)
			{
				result = <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1358409005);
			}
			else
			{
				CharacterObject characterObject = agent.Character as CharacterObject;
				bool flag2 = characterObject != null && characterObject.IsHero;
				if (flag2)
				{
					bool flag3 = characterObject.HeroObject == Hero.MainHero;
					if (flag3)
					{
						return <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(314960226);
					}
					bool isLord = characterObject.HeroObject.IsLord;
					if (isLord)
					{
						return string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1573874190), characterObject.HeroObject.Name);
					}
					bool isPlayerCompanion = characterObject.HeroObject.IsPlayerCompanion;
					if (isPlayerCompanion)
					{
						return <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-2147395031);
					}
				}
				bool flag4 = agent.Team == base.Mission.DefenderTeam;
				if (flag4)
				{
					bool flag5 = characterObject != null;
					if (flag5)
					{
						bool flag6 = characterObject.Tier <= 2;
						if (flag6)
						{
							return <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(2014008699);
						}
						bool flag7 = characterObject.Tier <= 3 && agent.HasMount;
						if (flag7)
						{
							return <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-325416080);
						}
						bool flag8 = characterObject.Tier >= 4;
						if (flag8)
						{
							return <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1465379348);
						}
					}
					result = <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1539323787);
				}
				else
				{
					result = <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-711308808);
				}
			}
			return result;
		}

		// Token: 0x06000AC6 RID: 2758 RVA: 0x000C5068 File Offset: 0x000C3268
		private bool IsCivilian(BasicCharacterObject character)
		{
			bool flag = character == null;
			bool result;
			if (flag)
			{
				result = false;
			}
			else
			{
				CharacterObject characterObject = character as CharacterObject;
				bool flag2 = characterObject == null;
				if (flag2)
				{
					result = false;
				}
				else
				{
					bool isHero = characterObject.IsHero;
					if (isHero)
					{
						Hero heroObject = characterObject.HeroObject;
						bool flag3 = heroObject != null && heroObject.IsLord;
						if (flag3)
						{
							return false;
						}
					}
					result = (characterObject.Occupation != Occupation.Soldier && characterObject.Occupation != Occupation.Bandit && characterObject.Occupation != Occupation.Mercenary);
				}
			}
			return result;
		}

		// Token: 0x06000AC7 RID: 2759 RVA: 0x000C50EC File Offset: 0x000C32EC
		private CombatSide GetAgentSide(Agent agent)
		{
			bool flag = ((agent != null) ? agent.Team : null) == null;
			CombatSide result;
			if (flag)
			{
				result = CombatSide.Unknown;
			}
			else
			{
				bool flag2 = Mission.Current != null && agent.Team == Mission.Current.DefenderTeam;
				if (flag2)
				{
					result = CombatSide.Defenders;
				}
				else
				{
					bool flag3 = Mission.Current != null && agent.Team == Mission.Current.AttackerTeam;
					if (flag3)
					{
						result = CombatSide.Attackers;
					}
					else
					{
						result = CombatSide.Attackers;
					}
				}
			}
			return result;
		}

		// Token: 0x06000AC8 RID: 2760 RVA: 0x000C5160 File Offset: 0x000C3360
		private bool IsImportantCharacter(CharacterObject character)
		{
			bool flag = character == null;
			bool result;
			if (flag)
			{
				result = false;
			}
			else
			{
				bool isHero = character.IsHero;
				if (isHero)
				{
					Hero heroObject = character.HeroObject;
					bool flag2 = heroObject != null && (heroObject.IsLord || heroObject == Hero.MainHero);
					if (flag2)
					{
						return true;
					}
				}
				result = false;
			}
			return result;
		}

		// Token: 0x04000699 RID: 1689
		private readonly CombatStatistics _statistics;

		// Token: 0x0400069A RID: 1690
		private readonly SettlementCombatLogger _logger;

		// Token: 0x0400069B RID: 1691
		private readonly AIInfluenceBehavior _behavior;

		// Token: 0x0400069C RID: 1692
		private readonly CivilianBehavior _civilianBehavior;

		// Token: 0x0400069D RID: 1693
		private readonly SettlementCombatManager _combatManager;

		// Token: 0x0400069E RID: 1694
		private readonly DefenderSpawner _defenderSpawner;

		// Token: 0x0400069F RID: 1695
		private float _combatCheckTimer = 0f;

		// Token: 0x040006A0 RID: 1696
		private const float COMBAT_CHECK_INTERVAL = 2f;

		// Token: 0x040006A1 RID: 1697
		private bool _eventSubscribed = false;
	}
}
