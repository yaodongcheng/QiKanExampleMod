using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using AIInfluence.Behaviors.AIActions;
using AIInfluence.SettlementCombat.PhrasesDisplay;
using AIInfluence.SettlementCombat.PhrasesDisplay.Popup;
using AIInfluence.Util;
using Helpers;
using Newtonsoft.Json;
using SandBox.Conversation.MissionLogics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x02000140 RID: 320
	public class SettlementCombatManager
	{
		// Token: 0x06000A19 RID: 2585 RVA: 0x000BD174 File Offset: 0x000BB374
		public SettlementCombatManager(AIInfluenceBehavior behavior)
		{
			this._behavior = behavior;
			this._logger = SettlementCombatLogger.Instance;
			this._promptGenerator = new CombatPromptGenerator(behavior);
			this._statistics = new CombatStatistics(behavior);
			this._defenderSpawner = new DefenderSpawner(behavior);
			this._civilianBehavior = new CivilianBehavior(behavior);
			this._eventCreator = new PostCombatEventCreator(behavior);
			this._defenderSpawner.SetStatistics(this._statistics);
			CampaignEvents.OnMissionStartedEvent.AddNonSerializedListener(this, new Action<IMission>(this.OnMissionStarted));
			CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this, new Action<IMission>(this.OnMissionEnded));
		}

		// Token: 0x06000A1A RID: 2586 RVA: 0x000BD284 File Offset: 0x000BB484
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void OnPlayerKnockedOut(float playerPower, float enemyPower)
		{
			try
			{
				bool flag = this._activeCombat == null || this._activeCombat.Settlement == null;
				if (!flag)
				{
					CombatSituationAnalysis analysis = this._activeCombat.Analysis;
					string text = (analysis != null) ? analysis.AggressorStringId : null;
					bool flag2;
					if (!(text == <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1636222184)))
					{
						string a = text;
						Hero mainHero = Hero.MainHero;
						flag2 = (a == ((mainHero != null) ? mainHero.StringId : null));
					}
					else
					{
						flag2 = true;
					}
					bool flag3 = flag2;
					this._playerKnockout = new SettlementCombatManager.PlayerKnockoutInfo
					{
						IsAggressor = flag3,
						PlayerPower = playerPower,
						EnemyPower = enemyPower,
						Settlement = this._activeCombat.Settlement
					};
					this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1326542437), flag3, playerPower, enemyPower));
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-652695940), ex.Message, ex);
			}
		}

		// Token: 0x06000A1B RID: 2587 RVA: 0x000BD394 File Offset: 0x000BB594
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void OnMissionStarted(IMission mission)
		{
			try
			{
				bool flag = this._savedCombatForTransition == null;
				if (!flag)
				{
					bool flag2 = Settlement.CurrentSettlement == null || Settlement.CurrentSettlement != this._savedCombatForTransition.Settlement;
					if (flag2)
					{
						this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-464930081));
						this._savedCombatForTransition = null;
					}
					else
					{
						this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1713048754));
						this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-343222193) + this._previousSceneName);
						SettlementCombatLogger logger = this._logger;
						string str = <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(793270001);
						Mission mission2 = Mission.Current;
						logger.Log(str + (((mission2 != null) ? mission2.SceneName : null) ?? <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-57576760)));
						this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(370381146), this._savedCombatForTransition.LocationType));
						this._activeCombat = this._savedCombatForTransition;
						this._savedCombatForTransition = null;
						bool flag3 = Agent.Main != null;
						if (flag3)
						{
							this._activeCombat.PlayerEntryPosition = Agent.Main.Position;
							this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-168564945), this._activeCombat.PlayerEntryPosition));
						}
						this._activeCombat.LocationType = this.DetermineLocationType();
						this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-2043862388), this._activeCombat.LocationType));
						this.ContinueCombatInNewLocation();
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1678162297), ex.Message, ex);
				this._savedCombatForTransition = null;
			}
		}

		// Token: 0x06000A1C RID: 2588 RVA: 0x000BD598 File Offset: 0x000BB798
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void OnMissionEnded(IMission mission)
		{
			try
			{
				bool flag = this._activeCombat == null;
				if (flag)
				{
					return;
				}
				bool flag2 = !this._combatEnded;
				if (flag2)
				{
					Mission mission2 = Mission.Current;
					this._previousSceneName = (((mission2 != null) ? mission2.SceneName : null) ?? "");
					bool insideSettlement = PlayerEncounter.InsideSettlement;
					bool flag3 = insideSettlement && this._activeCombat.LocationType == LocationType.SmallIndoor;
					if (flag3)
					{
						this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-939391178));
						this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1597976623));
						this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-814160986), this._activeCombat.DefendersSpawnedInSmallLocation));
						bool flag4 = Mission.Current != null;
						if (flag4)
						{
							this._activeCombat.NPCsFromSmallLocation = new List<string>();
							foreach (Agent agent in Mission.Current.Agents)
							{
								bool flag5 = agent == null || !agent.IsActive() || !agent.IsHuman || agent == Agent.Main;
								if (!flag5)
								{
									CharacterObject characterObject = agent.Character as CharacterObject;
									bool flag6 = characterObject == null;
									if (!flag6)
									{
										bool flag7 = this._civilianBehavior != null && this._civilianBehavior.IsAgentUnderCivilianControl(agent);
										if (flag7)
										{
											this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-493176711) + agent.Name);
										}
										else
										{
											bool flag8 = !characterObject.IsHero && this.IsDefenderOrMilitary(characterObject);
											if (flag8)
											{
												string stringId = characterObject.StringId;
												bool flag9 = !string.IsNullOrEmpty(stringId) && !this._activeCombat.NPCsFromSmallLocation.Contains(stringId);
												if (flag9)
												{
													this._activeCombat.NPCsFromSmallLocation.Add(stringId);
													this._logger.Log(string.Concat(new string[]
													{
														<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-411807675),
														agent.Name,
														<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-432756618),
														stringId,
														<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1487645123)
													}));
												}
											}
											else
											{
												bool isHero = characterObject.IsHero;
												if (isHero)
												{
													this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(932251390) + agent.Name + <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1980697885));
												}
											}
										}
									}
								}
							}
							this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1129928006), this._activeCombat.NPCsFromSmallLocation.Count));
						}
						this._savedCombatForTransition = this._activeCombat;
						this._activeCombat = null;
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1181308856), ex.Message, ex);
			}
			bool flag10 = this._activeCombat != null;
			if (flag10)
			{
				this._behavior.GetDelayedTaskManager().AddTask(0.1, delegate
				{
					try
					{
						bool flag12 = this._activeCombat == null;
						if (!flag12)
						{
							bool flag13 = Mission.Current != null;
							if (!flag13)
							{
								bool insideSettlement2 = PlayerEncounter.InsideSettlement;
								bool flag14;
								if (Settlement.CurrentSettlement == null)
								{
									MobileParty mainParty = MobileParty.MainParty;
									flag14 = (((mainParty != null) ? mainParty.CurrentSettlement : null) != null);
								}
								else
								{
									flag14 = true;
								}
								bool flag15 = flag14;
								bool flag16 = !insideSettlement2 && !flag15;
								if (flag16)
								{
									this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1024978166));
									this.OnPlayerLeavesSettlement();
								}
								else
								{
									bool flag17 = insideSettlement2 && Mission.Current == null;
									if (flag17)
									{
										bool flag18 = this.ForcePlayerExitAfterCombat(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1265897172));
										bool flag19 = !flag18;
										if (flag19)
										{
											this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(742046668));
											this.OnPlayerLeavesSettlement();
										}
									}
								}
							}
						}
					}
					catch (Exception ex2)
					{
						this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(57186453), ex2.Message, ex2);
					}
				});
			}
			bool flag11 = this._activeCombat != null;
			if (flag11)
			{
				this.ScheduleSettlementEscapeMonitor();
			}
		}

		// Token: 0x06000A1D RID: 2589 RVA: 0x0001D8C0 File Offset: 0x0001BAC0
		private void ScheduleSettlementEscapeMonitor()
		{
			this._behavior.GetDelayedTaskManager().AddTask(1.0, delegate
			{
				try
				{
					bool flag = this._activeCombat == null;
					if (!flag)
					{
						bool flag2 = Mission.Current != null;
						if (flag2)
						{
							this.ScheduleSettlementEscapeMonitor();
						}
						else
						{
							bool insideSettlement = PlayerEncounter.InsideSettlement;
							bool flag3;
							if (Settlement.CurrentSettlement == null)
							{
								MobileParty mainParty = MobileParty.MainParty;
								flag3 = (((mainParty != null) ? mainParty.CurrentSettlement : null) != null);
							}
							else
							{
								flag3 = true;
							}
							bool flag4 = flag3;
							bool flag5 = insideSettlement || flag4;
							if (flag5)
							{
								this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1873025462));
								bool flag6 = this.ForcePlayerExitAfterCombat(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1695974845));
								bool flag7 = !flag6;
								if (flag7)
								{
									this.OnPlayerLeavesSettlement();
								}
							}
							else
							{
								this.OnPlayerLeavesSettlement();
							}
						}
					}
				}
				catch (Exception ex)
				{
					this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1882467195), ex.Message, ex);
				}
			});
		}

		// Token: 0x06000A1E RID: 2590 RVA: 0x000BD94C File Offset: 0x000BBB4C
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void ContinueCombatInNewLocation()
		{
			try
			{
				this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-524248850));
				bool flag = Mission.Current != null;
				if (flag)
				{
					bool flag2 = Mission.Current.Mode != MissionMode.Battle;
					if (flag2)
					{
						this._previousMissionMode = Mission.Current.Mode;
						Mission.Current.SetMissionMode(MissionMode.Battle, false);
						this._missionModeChanged = true;
						this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-36460963), this._previousMissionMode));
					}
					else
					{
						bool flag3 = !this._missionModeChanged;
						if (flag3)
						{
							this._previousMissionMode = MissionMode.Battle;
							this._missionModeChanged = true;
							this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1804839960));
						}
					}
				}
				this._behavior.GetDelayedTaskManager().AddTask(0.20000000298023224, delegate
				{
					this.SetupTeamHostility();
				});
				try
				{
					Mission mission = Mission.Current;
					MissionConversationLogic missionConversationLogic = (mission != null) ? mission.GetMissionBehavior<MissionConversationLogic>() : null;
					if (missionConversationLogic != null)
					{
						missionConversationLogic.DisableStartConversation(true);
					}
					this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(765002259));
				}
				catch
				{
				}
				bool flag4 = Mission.Current != null;
				if (flag4)
				{
					CombatPhrasesDisplay missionBehavior = new CombatPhrasesDisplay(this._activeCombat.Analysis);
					Mission.Current.AddMissionBehavior(missionBehavior);
					this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-2117421122));
					CombatPhrasePopupView.EnsureCreated();
				}
				bool flag5 = this._activeCombat.Analysis != null && this._activeCombat.Analysis.NeedsDefenders;
				if (flag5)
				{
					int defendersSpawnedInSmallLocation = this._activeCombat.DefendersSpawnedInSmallLocation;
					this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1549074109), defendersSpawnedInSmallLocation));
					bool flag6 = defendersSpawnedInSmallLocation > 0;
					if (flag6)
					{
						this._defenderSpawner.SpawnGuardsForTransition(this._activeCombat, defendersSpawnedInSmallLocation);
					}
					bool flag7 = this._activeCombat.LocationType == LocationType.LargeOutdoor;
					if (flag7)
					{
						this._defenderSpawner.HandleTransitionToLargeLocation(this._activeCombat);
					}
				}
				else
				{
					this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(551305886));
				}
				bool flag8 = this._activeCombat.Analysis != null && this._activeCombat.Analysis.CivilianPanic;
				if (flag8)
				{
					this._behavior.GetDelayedTaskManager().AddTask(2.0, delegate
					{
						try
						{
							bool flag10 = this.HasChildrenInLocation();
							this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-822405363), flag10));
							this._civilianBehavior.InitiatePanic(this._activeCombat, flag10);
						}
						catch (Exception ex2)
						{
							this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-866437058), ex2.Message, ex2);
						}
					});
				}
				else
				{
					this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1319692646));
				}
				this.PreparePlayerTroopsForBattle();
				this._behavior.GetDelayedTaskManager().AddTask(0.5, delegate
				{
					this.PrepareExistingDefenders();
				});
				bool flag9 = this._activeCombat.NPCsFromSmallLocation != null && this._activeCombat.NPCsFromSmallLocation.Count > 0;
				if (flag9)
				{
					this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-889027547), this._activeCombat.NPCsFromSmallLocation.Count));
					this._behavior.GetDelayedTaskManager().AddTask(8.0, delegate
					{
						this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1985113334));
						this.SpawnNPCsFromSmallLocation();
					});
				}
				this.StartCombatTracking();
				this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(665372434));
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-975909131), ex.Message, ex);
			}
			this._behavior.GetDelayedTaskManager().AddTask(1.0, delegate
			{
				try
				{
					bool flag10 = Mission.Current != null && Mission.Current.Mode != MissionMode.Battle;
					if (flag10)
					{
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-901720474), Mission.Current.Mode));
						this._previousMissionMode = Mission.Current.Mode;
						Mission.Current.SetMissionMode(MissionMode.Battle, false);
						this._missionModeChanged = true;
					}
				}
				catch (Exception ex2)
				{
					this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-314586919), ex2.Message, ex2);
				}
			});
		}

		// Token: 0x06000A1F RID: 2591 RVA: 0x0001D8E9 File Offset: 0x0001BAE9
		public DefenderSpawner GetDefenderSpawner()
		{
			return this._defenderSpawner;
		}

		// Token: 0x06000A20 RID: 2592 RVA: 0x000BDD50 File Offset: 0x000BBF50
		public bool IsActiveCombat()
		{
			return (this._activeCombat != null && !this._combatEnded) || this._savedCombatForTransition != null;
		}

		// Token: 0x06000A21 RID: 2593 RVA: 0x000BDD80 File Offset: 0x000BBF80
		public bool ShouldBlockMissionExit()
		{
			bool flag = !this.IsActiveCombat();
			bool result;
			if (flag)
			{
				result = false;
			}
			else
			{
				ActiveCombat activeCombat = this._activeCombat;
				bool flag2 = ((activeCombat != null) ? activeCombat.Analysis : null) == null;
				if (flag2)
				{
					result = true;
				}
				else
				{
					bool flag3 = !this._activeCombat.Analysis.NeedsDefenders && !this._activeCombat.Analysis.CivilianPanic;
					result = !flag3;
				}
			}
			return result;
		}

		// Token: 0x06000A22 RID: 2594 RVA: 0x000BDDF4 File Offset: 0x000BBFF4
		public CivilianBehavior GetCivilianBehavior()
		{
			return this._civilianBehavior;
		}

		// Token: 0x06000A23 RID: 2595 RVA: 0x000BDE0C File Offset: 0x000BC00C
		public bool IsCombatPromptReady()
		{
			bool flag = this._activeCombat != null;
			bool result;
			if (flag)
			{
				result = (this._activeCombat.Analysis != null);
			}
			else
			{
				bool flag2 = this._savedCombatForTransition != null;
				result = (flag2 && this._savedCombatForTransition.Analysis != null);
			}
			return result;
		}

		// Token: 0x06000A24 RID: 2596 RVA: 0x000BDE5C File Offset: 0x000BC05C
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void InitiateCombat(Hero npc, NPCContext context, CombatTriggerType triggerType, string npcResponse = null)
		{
			try
			{
				this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-31075478));
				this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(963609080), triggerType));
				SettlementCombatLogger logger = this._logger;
				string str = <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-997961266);
				string text;
				if (npc == null)
				{
					text = null;
				}
				else
				{
					TextObject name = npc.Name;
					text = ((name != null) ? name.ToString() : null);
				}
				logger.Log(str + (text ?? <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1099651297)));
				this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1613090758) + ((context != null) ? <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1197641268) : <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1287652219)));
				this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(414582649) + (string.IsNullOrEmpty(npcResponse) ? <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1503458985) : npcResponse.Substring(0, Math.Min(50, npcResponse.Length))));
				bool flag = this._activeCombat != null;
				if (flag)
				{
					this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(897904454));
				}
				else
				{
					bool flag2 = Mission.Current == null;
					if (flag2)
					{
						this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1734943901));
					}
					else
					{
						Settlement currentSettlement = Settlement.CurrentSettlement;
						bool flag3 = currentSettlement == null;
						if (flag3)
						{
							this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-427591254));
							this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1099401087) + ((Mission.Current != null) ? <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1914977490) : <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1626403898)));
							this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1800347163));
						}
						else
						{
							SettlementCombatLogger logger2 = this._logger;
							string settlementName = currentSettlement.Name.ToString();
							string stringId = currentSettlement.StringId;
							string triggerType2 = triggerType.ToString();
							string text2;
							if (npc == null)
							{
								text2 = null;
							}
							else
							{
								TextObject name2 = npc.Name;
								text2 = ((name2 != null) ? name2.ToString() : null);
							}
							logger2.LogCombatInitiated(settlementName, stringId, triggerType2, text2 ?? <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-2041001075), ((npc != null) ? npc.StringId : null) ?? <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(665038079));
							this._currentRetryAttempt = 0;
							LocationType locationType = this.DetermineLocationType();
							this._activeCombat = new ActiveCombat
							{
								Settlement = currentSettlement,
								TriggerNPC = npc,
								TriggerContext = context,
								TriggerType = triggerType,
								TriggerResponse = npcResponse,
								StartTime = CampaignTime.Now,
								LocationType = locationType,
								DefendersSpawnedInSmallLocation = 0,
								NPCsFromSmallLocation = new List<string>()
							};
							this._activeCombat.PlayerCompanions = this.CollectCompanionsForCombat(currentSettlement);
							this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1185459041), this._activeCombat.PlayerCompanions.Count));
							this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(273448985), locationType));
							this.SendSituationAnalysisPrompt();
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1169967919), ex.Message, ex);
			}
		}

		// Token: 0x06000A25 RID: 2597 RVA: 0x000BE1D8 File Offset: 0x000BC3D8
		[DebuggerStepThrough]
		private void SendSituationAnalysisPrompt()
		{
			SettlementCombatManager.<SendSituationAnalysisPrompt>d__38 <SendSituationAnalysisPrompt>d__ = new SettlementCombatManager.<SendSituationAnalysisPrompt>d__38();
			<SendSituationAnalysisPrompt>d__.<>t__builder = AsyncVoidMethodBuilder.Create();
			<SendSituationAnalysisPrompt>d__.<>4__this = this;
			<SendSituationAnalysisPrompt>d__.<>1__state = -1;
			<SendSituationAnalysisPrompt>d__.<>t__builder.Start<SettlementCombatManager.<SendSituationAnalysisPrompt>d__38>(ref <SendSituationAnalysisPrompt>d__);
		}

		// Token: 0x06000A26 RID: 2598 RVA: 0x000BE214 File Offset: 0x000BC414
		[MethodImpl(MethodImplOptions.NoInlining)]
		private List<Hero> CollectCompanionsForCombat(Settlement settlement)
		{
			HashSet<Hero> hashSet = new HashSet<Hero>();
			try
			{
				bool flag = settlement == null;
				if (flag)
				{
					return new List<Hero>();
				}
				foreach (Hero hero in Hero.AllAliveHeroes)
				{
					bool flag2 = hero == null || hero == Hero.MainHero || hero.IsDead || hero.IsPrisoner;
					if (!flag2)
					{
						bool flag3 = hero.CompanionOf == Clan.PlayerClan || hero == Hero.MainHero.Spouse;
						bool flag4 = !flag3;
						if (!flag4)
						{
							bool flag5 = this.IsHeroPresentWithPlayer(hero, settlement);
							if (flag5)
							{
								hashSet.Add(hero);
							}
						}
					}
				}
				AIActionManager instance = AIActionManager.Instance;
				List<Hero> list = (instance != null) ? instance.GetHeroesFollowingPlayerInSettlement(settlement, true) : null;
				bool flag6 = list != null;
				if (flag6)
				{
					foreach (Hero hero2 in list)
					{
						bool flag7 = hero2 == null || hero2 == Hero.MainHero || hero2.IsDead || hero2.IsPrisoner;
						if (!flag7)
						{
							bool flag8 = this.IsHeroPresentWithPlayer(hero2, settlement);
							if (flag8)
							{
								hashSet.Add(hero2);
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1702711765), ex.Message, ex);
			}
			return Enumerable.ToList<Hero>(Enumerable.OrderBy<Hero, string>(Enumerable.Where<Hero>(hashSet, (Hero h) => h != null && !string.IsNullOrEmpty(h.StringId)), delegate(Hero h)
			{
				TextObject name = h.Name;
				return (name != null) ? name.ToString() : null;
			}));
		}

		// Token: 0x06000A27 RID: 2599 RVA: 0x000BE43C File Offset: 0x000BC63C
		private bool IsHeroPresentWithPlayer(Hero hero, Settlement settlement)
		{
			bool flag = hero == null;
			bool result;
			if (flag)
			{
				result = false;
			}
			else
			{
				bool flag2 = hero.PartyBelongedTo == MobileParty.MainParty;
				if (flag2)
				{
					result = true;
				}
				else
				{
					bool flag3 = settlement != null;
					if (flag3)
					{
						bool flag4 = hero.CurrentSettlement == settlement || hero.StayingInSettlement == settlement;
						if (flag4)
						{
							return true;
						}
						MobileParty partyBelongedTo = hero.PartyBelongedTo;
						bool flag5 = ((partyBelongedTo != null) ? partyBelongedTo.CurrentSettlement : null) == settlement;
						if (flag5)
						{
							return true;
						}
					}
					bool flag6 = Mission.Current != null;
					result = (flag6 && Enumerable.Any<Agent>(Mission.Current.Agents, (Agent agent) => agent != null && agent.IsActive() && agent.Character != null && agent.Character.StringId == hero.StringId));
				}
			}
			return result;
		}

		// Token: 0x06000A28 RID: 2600 RVA: 0x000BE514 File Offset: 0x000BC714
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void HandleAIError()
		{
			this._currentRetryAttempt++;
			bool flag = this._currentRetryAttempt < 3;
			if (flag)
			{
				this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-936401575), 5f, this._currentRetryAttempt + 1, 3));
				TextObject textObject = new TextObject(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1663940765), null).SetTextVariable(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-742709571), this._currentRetryAttempt).SetTextVariable(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(172009164), 3).SetTextVariable(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-209171439), 5f, 2);
				InformationManager.DisplayMessage(new InformationMessage(textObject.ToString(), \u200D\u206E\u200B\u202E\u206A\u202A\u200E\u202E\u200E\u202E\u202A\u202B\u200C\u206F\u202A\u206E\u206E\u200F\u206B\u202D\u200C\u200F\u206E\u200C\u206E\u202E\u200B\u200D\u202D\u202E\u206D\u206E\u202B\u200F\u200C\u206A\u202A\u202C\u206D\u202E.\u202C\u200D\u202E\u200D\u206F\u206E\u206C\u202E\u206A\u206C\u206F\u200F\u206F\u200C\u206A\u206D\u206B\u200B\u206B\u202C\u202A\u202E\u200B\u200B\u206B\u200D\u200D\u202B\u206F\u202E\u202E\u202E\u200F\u200C\u202A\u202D\u206A\u202D\u200F\u200C\u202E));
				this._behavior.GetDelayedTaskManager().AddTask(5.0, delegate
				{
					bool flag6 = this._activeCombat != null;
					if (flag6)
					{
						this.SendSituationAnalysisPrompt();
					}
				});
			}
			else
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-84642706), string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(163188310), 3), null);
				TextObject textObject2 = new TextObject(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1988749658), null);
				InformationManager.DisplayMessage(new InformationMessage(textObject2.ToString(), \u200D\u206E\u200B\u202E\u206A\u202A\u200E\u202E\u200E\u202E\u202A\u202B\u200C\u206F\u202A\u206E\u206E\u200F\u206B\u202D\u200C\u200F\u206E\u200C\u206E\u202E\u200B\u200D\u202D\u202E\u206D\u206E\u202B\u200F\u200C\u206A\u202A\u202C\u206D\u202E.\u202C\u200D\u202E\u200D\u206F\u206E\u206C\u202E\u206A\u206C\u206F\u200F\u206F\u200C\u206A\u206D\u206B\u200B\u206B\u202C\u202A\u202E\u200B\u200B\u206B\u200D\u200D\u202B\u206F\u202E\u202E\u202E\u200F\u200C\u202A\u202D\u206A\u202D\u200F\u200C\u202E));
				ActiveCombat activeCombat = this._activeCombat;
				bool flag2 = ((activeCombat != null) ? activeCombat.TriggerContext : null) != null;
				if (flag2)
				{
					this._activeCombat.TriggerContext.PendingSettlementCombat = null;
					this._activeCombat.TriggerContext.SettlementCombatResponse = null;
					this._activeCombat.TriggerContext.PendingDeath = null;
					this._activeCombat.TriggerContext.KillerStringId = null;
					this._activeCombat.TriggerContext.CombatResponse = null;
					this._activeCombat.TriggerContext.IsSurrendering = false;
					this._activeCombat.TriggerContext.NegativeToneCount = new int?(0);
					this._activeCombat.TriggerContext.EscalationState = <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1978016777);
					bool flag3 = this._activeCombat.TriggerNPC != null;
					if (flag3)
					{
						this._behavior.SaveNPCContext(this._activeCombat.TriggerNPC.StringId, this._activeCombat.TriggerNPC, this._activeCombat.TriggerContext);
						this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(2113278391), this._activeCombat.TriggerNPC.Name));
					}
				}
				Campaign campaign = Campaign.Current;
				bool flag4;
				if (campaign == null)
				{
					flag4 = false;
				}
				else
				{
					ConversationManager conversationManager = campaign.ConversationManager;
					flag4 = ((conversationManager != null) ? new bool?(conversationManager.IsConversationInProgress) : null).GetValueOrDefault();
				}
				bool flag5 = flag4;
				if (flag5)
				{
					Campaign.Current.ConversationManager.EndConversation();
				}
				this._activeCombat = null;
				this._combatEnded = false;
				this._currentRetryAttempt = 0;
			}
		}

		// Token: 0x06000A29 RID: 2601 RVA: 0x000BE800 File Offset: 0x000BCA00
		[MethodImpl(MethodImplOptions.NoInlining)]
		private CombatSituationAnalysis ParseSituationAnalysis(string aiResponse)
		{
			CombatSituationAnalysis result;
			try
			{
				this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(718259584));
				this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-170422693), (aiResponse != null) ? aiResponse.Length : 0));
				string text = JsonCleaner.CleanJsonGeneric(aiResponse);
				bool flag = string.IsNullOrEmpty(text);
				if (flag)
				{
					this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1386078842), <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1183090475), null);
					this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1885323769));
					this._logger.Log(aiResponse ?? "");
					this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1615856553));
					result = null;
				}
				else
				{
					this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1430188134), text.Length));
					this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-774536303));
					this._logger.Log(text ?? "");
					this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(518622147));
					CombatSituationAnalysis combatSituationAnalysis = JsonConvert.DeserializeObject<CombatSituationAnalysis>(text);
					bool flag2 = combatSituationAnalysis == null;
					if (flag2)
					{
						this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1059104970), <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1528202117), null);
						result = null;
					}
					else
					{
						bool flag3 = combatSituationAnalysis.NeedsDefenders && !combatSituationAnalysis.CivilianPanic;
						if (flag3)
						{
							this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(743462328));
							combatSituationAnalysis.CivilianPanic = true;
						}
						this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(842414005), combatSituationAnalysis.NeedsDefenders, combatSituationAnalysis.CivilianPanic));
						result = combatSituationAnalysis;
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1127601160), <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-51459358) + ex.Message, ex);
				this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-291778502) + aiResponse);
				result = null;
			}
			return result;
		}

		// Token: 0x06000A2A RID: 2602 RVA: 0x000BEA6C File Offset: 0x000BCC6C
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void ProcessAIAnalysis(CombatSituationAnalysis analysis)
		{
			try
			{
				this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-876413828));
				this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-895795081) + analysis.AggressorStringId + <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-2144772140) + analysis.DefenderStringId);
				SettlementCombatLogger logger = this._logger;
				string format = <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1823894783);
				List<string> witnesses = analysis.Witnesses;
				logger.Log(string.Format(format, (witnesses != null) ? witnesses.Count : 0));
				SettlementCombatLogger logger2 = this._logger;
				string aggressorStringId = analysis.AggressorStringId;
				string defenderStringId = analysis.DefenderStringId;
				List<string> witnesses2 = analysis.Witnesses;
				int witnessCount = (witnesses2 != null) ? witnesses2.Count : 0;
				bool needsDefenders = analysis.NeedsDefenders;
				List<LordIntervention> lords = analysis.Lords;
				logger2.LogAnalysisResult(aggressorStringId, defenderStringId, witnessCount, needsDefenders, (lords != null) ? lords.Count : 0);
				bool flag = analysis.Lords != null && Enumerable.Any<LordIntervention>(analysis.Lords);
				if (flag)
				{
					this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1942272527));
					foreach (LordIntervention lordIntervention in analysis.Lords)
					{
						this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1921499726) + lordIntervention.StringId + <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(408982189));
						this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(561654794) + lordIntervention.Side);
						this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1504317430) + lordIntervention.InterventionReason);
						this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1071946341) + lordIntervention.ArrivalPhrase + <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1036563585));
					}
				}
				this._activeCombat.Analysis = analysis;
				this.ApplyCompanionDecisionsFromAnalysis(analysis);
				this.CloseDialogAndStartCombat();
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1240539773), ex.Message, ex);
			}
		}

		// Token: 0x06000A2B RID: 2603 RVA: 0x000BECCC File Offset: 0x000BCECC
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void CloseDialogAndStartCombat()
		{
			try
			{
				Campaign campaign = Campaign.Current;
				bool flag;
				if (campaign == null)
				{
					flag = false;
				}
				else
				{
					ConversationManager conversationManager = campaign.ConversationManager;
					flag = ((conversationManager != null) ? new bool?(conversationManager.IsConversationInProgress) : null).GetValueOrDefault();
				}
				bool flag2 = flag;
				if (flag2)
				{
					Campaign.Current.ConversationManager.EndConversation();
					this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-2012308222));
				}
				CombatSituationAnalysis analysis = this._activeCombat.Analysis;
				bool flag3 = !string.IsNullOrEmpty((analysis != null) ? analysis.SituationDescription : null);
				if (flag3)
				{
					InformationManager.DisplayMessage(new InformationMessage(this._activeCombat.Analysis.SituationDescription, Colors.Gray));
				}
				bool flag4 = this._activeCombat.TriggerType == CombatTriggerType.RoleplayDeath;
				if (flag4)
				{
					this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1719749930));
					this._behavior.GetDelayedTaskManager().AddTask(0.3, delegate
					{
						try
						{
							Hero triggerNPC = this._activeCombat.TriggerNPC;
							NPCContext context = this._activeCombat.TriggerContext;
							bool flag6 = triggerNPC != null && !triggerNPC.IsDead;
							if (flag6)
							{
								Hero hero = null;
								NPCContext context2 = context;
								bool flag7 = !string.IsNullOrEmpty((context2 != null) ? context2.KillerStringId : null);
								if (flag7)
								{
									hero = Hero.FindFirst((Hero h) => h != null && h.StringId == context.KillerStringId);
									bool flag8 = hero != null;
									if (flag8)
									{
										this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-771800239), hero.Name, context.KillerStringId));
									}
									else
									{
										this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(479950452) + context.KillerStringId + <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1712162120));
									}
								}
								else
								{
									this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1069500684));
								}
								this._behavior.KillCharacterHeroPublic(triggerNPC, hero, false);
								this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(457898317), triggerNPC.Name));
								bool flag9 = context != null;
								if (flag9)
								{
									context.PendingDeath = null;
									context.KillerStringId = null;
									this._behavior.SaveNPCContext(triggerNPC.StringId, triggerNPC, context);
								}
							}
						}
						catch (Exception ex2)
						{
							this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1874852659), ex2.Message, ex2);
						}
					});
				}
				bool flag5 = Mission.Current != null && Mission.Current.Mode != MissionMode.Battle;
				if (flag5)
				{
					this._previousMissionMode = Mission.Current.Mode;
					Mission.Current.SetMissionMode(MissionMode.Battle, false);
					this._missionModeChanged = true;
					this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1537942396), this._previousMissionMode));
				}
				this.SetupTeamHostility();
				try
				{
					Mission mission = Mission.Current;
					MissionConversationLogic missionConversationLogic = (mission != null) ? mission.GetMissionBehavior<MissionConversationLogic>() : null;
					if (missionConversationLogic != null)
					{
						missionConversationLogic.DisableStartConversation(true);
					}
					this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1288884930));
				}
				catch
				{
				}
				this.SpawnCombatParticipants();
				this.PreparePlayerTroopsForBattle();
				this.PrepareExistingDefenders();
				this.StartCombatTracking();
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(427154930), ex.Message, ex);
			}
		}

		// Token: 0x06000A2C RID: 2604 RVA: 0x000BEF04 File Offset: 0x000BD104
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void ApplyCompanionDecisionsFromAnalysis(CombatSituationAnalysis analysis)
		{
			try
			{
				bool flag = this._activeCombat == null;
				if (!flag)
				{
					bool flag2 = this._activeCombat.PlayerCompanions == null || !Enumerable.Any<Hero>(this._activeCombat.PlayerCompanions);
					if (flag2)
					{
						this._activeCombat.CompanionDecisions.Clear();
					}
					else
					{
						Dictionary<string, CompanionCombatDecision> dictionary = new Dictionary<string, CompanionCombatDecision>();
						Dictionary<string, string> dictionary2 = (analysis != null) ? analysis.CompanionStance : null;
						bool flag3 = dictionary2 != null;
						if (flag3)
						{
							using (Dictionary<string, string>.KeyCollection.Enumerator enumerator = dictionary2.Keys.GetEnumerator())
							{
								while (enumerator.MoveNext())
								{
									string key = enumerator.Current;
									bool flag4 = Enumerable.Any<Hero>(this._activeCombat.PlayerCompanions, (Hero h) => ((h != null) ? h.StringId : null) == key);
									bool flag5 = !flag4;
									if (flag5)
									{
										this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1716860867) + key + <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-812932524));
									}
								}
							}
						}
						foreach (Hero hero in this._activeCombat.PlayerCompanions)
						{
							bool flag6 = hero == null || string.IsNullOrEmpty(hero.StringId);
							if (!flag6)
							{
								CompanionCombatDecision companionCombatDecision = CompanionCombatDecision.StayOut;
								string text;
								bool flag7 = dictionary2 != null && dictionary2.TryGetValue(hero.StringId, out text);
								if (flag7)
								{
									bool flag8 = this.TryParseCompanionDecision(text, out companionCombatDecision);
									bool flag9 = !flag8;
									if (flag9)
									{
										this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1125868671), text, hero.Name));
									}
								}
								else
								{
									this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-748154483), hero.Name));
								}
								dictionary[hero.StringId] = companionCombatDecision;
								this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1468974856), hero.Name, hero.StringId, companionCombatDecision));
							}
						}
						this._activeCombat.CompanionDecisions = dictionary;
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-2019611978), ex.Message, ex);
			}
		}

		// Token: 0x06000A2D RID: 2605 RVA: 0x000BF1D0 File Offset: 0x000BD3D0
		[MethodImpl(MethodImplOptions.NoInlining)]
		private bool TryParseCompanionDecision(string value, out CompanionCombatDecision decision)
		{
			string text = (value != null) ? value.Trim().ToLowerInvariant() : null;
			string a = text;
			bool result;
			if (!(a == <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-585500992)))
			{
				if (!(a == <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1834307532)))
				{
					if (!(a == <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1385924605)))
					{
						decision = CompanionCombatDecision.StayOut;
						result = false;
					}
					else
					{
						decision = CompanionCombatDecision.StayOut;
						result = true;
					}
				}
				else
				{
					decision = CompanionCombatDecision.OpposePlayer;
					result = true;
				}
			}
			else
			{
				decision = CompanionCombatDecision.SupportPlayer;
				result = true;
			}
			return result;
		}

		// Token: 0x06000A2E RID: 2606 RVA: 0x000BF250 File Offset: 0x000BD450
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void HandleCompanionAgentDecision(Agent agent, CompanionCombatDecision decision, bool playerIsAggressor, Agent aggressorAgent, ref int defendersFound)
		{
			try
			{
				bool flag = agent == null || Mission.Current == null;
				if (!flag)
				{
					switch (decision)
					{
					case CompanionCombatDecision.SupportPlayer:
					{
						bool flag2 = Mission.Current.PlayerTeam != null && agent.Team != Mission.Current.PlayerTeam;
						if (flag2)
						{
							agent.SetTeam(Mission.Current.PlayerTeam, true);
						}
						this.WieldBestMeleeWeapon(agent);
						agent.SetWatchState(Agent.WatchState.Alarmed);
						agent.SetAgentFlags(agent.GetAgentFlags() | AgentFlag.CanAttack | AgentFlag.CanDefend);
						bool flag3 = !playerIsAggressor && aggressorAgent != null && aggressorAgent != Agent.Main;
						if (flag3)
						{
							agent.SetLookAgent(aggressorAgent);
						}
						this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1410550297) + agent.Name + <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(568035814));
						break;
					}
					case CompanionCombatDecision.OpposePlayer:
					{
						bool flag4 = Mission.Current.DefenderTeam != null && agent.Team != Mission.Current.DefenderTeam;
						if (flag4)
						{
							agent.SetTeam(Mission.Current.DefenderTeam, true);
						}
						this.WieldBestMeleeWeapon(agent);
						agent.SetWatchState(Agent.WatchState.Alarmed);
						agent.SetAgentFlags(agent.GetAgentFlags() | AgentFlag.CanAttack | AgentFlag.CanDefend);
						bool flag5 = Agent.Main != null;
						if (flag5)
						{
							agent.SetLookAgent(Agent.Main);
						}
						defendersFound++;
						this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(2081340306) + agent.Name + <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-387267400));
						this.TrySummonLordTroops(agent);
						break;
					}
					case CompanionCombatDecision.StayOut:
					{
						CivilianBehavior civilianBehavior = this._civilianBehavior;
						if (civilianBehavior != null)
						{
							civilianBehavior.ForceAgentPanic(agent);
						}
						this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1849173677) + agent.Name + <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1866815385));
						break;
					}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1561596231), ex.Message, ex);
			}
		}

		// Token: 0x06000A2F RID: 2607 RVA: 0x000BF494 File Offset: 0x000BD694
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void TrySummonLordTroops(Agent agent)
		{
			try
			{
				bool flag = agent == null || Mission.Current == null || this._activeCombat == null;
				if (!flag)
				{
					CharacterObject characterObject = agent.Character as CharacterObject;
					bool flag2 = characterObject == null || !characterObject.IsHero || characterObject.HeroObject == null;
					if (!flag2)
					{
						Hero heroObject = characterObject.HeroObject;
						bool flag3 = heroObject.IsWanderer || heroObject.Clan == null || heroObject.Clan == Clan.PlayerClan;
						if (flag3)
						{
							this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-13681201), heroObject.Name));
						}
						else
						{
							bool flag4 = heroObject.PartyBelongedTo != null && heroObject.PartyBelongedTo.MemberRoster != null && heroObject.PartyBelongedTo.MemberRoster.TotalManCount > 0;
							bool flag5 = heroObject.PartyBelongedTo != null && heroObject.PartyBelongedTo.Army != null && heroObject.PartyBelongedTo.Army.LeaderParty == heroObject.PartyBelongedTo;
							bool flag6 = !flag4 && !flag5;
							if (flag6)
							{
								this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(927399456), heroObject.Name));
							}
							else
							{
								this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1025632732), heroObject.Name, flag4, flag5));
								bool flag7 = Mission.Current != null;
								if (flag7)
								{
									PlayerReinforcementMissionLogic missionBehavior = Mission.Current.GetMissionBehavior<PlayerReinforcementMissionLogic>();
									bool flag8 = missionBehavior != null;
									if (flag8)
									{
										int num = missionBehavior.TransferLordTroopsToDefenderTeam(heroObject);
										bool flag9 = num > 0;
										if (flag9)
										{
											this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1737860986), num, heroObject.Name));
										}
									}
								}
								this._defenderSpawner.SpawnLordTroopsForHostileCompanion(heroObject, this._activeCombat);
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1055140456), ex.Message, ex);
			}
		}

		// Token: 0x06000A30 RID: 2608 RVA: 0x000BF6D0 File Offset: 0x000BD8D0
		[MethodImpl(MethodImplOptions.NoInlining)]
		internal void ApplyCompanionDecisionToAgent(Agent agent)
		{
			try
			{
				SettlementCombatManager.<>c__DisplayClass49_0 CS$<>8__locals1 = new SettlementCombatManager.<>c__DisplayClass49_0();
				bool flag = agent == null || Mission.Current == null;
				if (!flag)
				{
					CharacterObject characterObject = agent.Character as CharacterObject;
					bool flag2 = characterObject == null || string.IsNullOrEmpty(characterObject.StringId);
					if (!flag2)
					{
						ActiveCombat activeCombat = this._activeCombat ?? this._savedCombatForTransition;
						bool flag3 = activeCombat == null || activeCombat.CompanionDecisions == null;
						if (!flag3)
						{
							CompanionCombatDecision decision;
							bool flag4 = !activeCombat.CompanionDecisions.TryGetValue(characterObject.StringId, out decision);
							if (!flag4)
							{
								SettlementCombatManager.<>c__DisplayClass49_0 CS$<>8__locals2 = CS$<>8__locals1;
								CombatSituationAnalysis analysis = activeCombat.Analysis;
								CS$<>8__locals2.aggressorId = ((analysis != null) ? analysis.AggressorStringId : null);
								bool flag5;
								if (!(CS$<>8__locals1.aggressorId == <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1636222184)))
								{
									string aggressorId = CS$<>8__locals1.aggressorId;
									Hero mainHero = Hero.MainHero;
									flag5 = (aggressorId == ((mainHero != null) ? mainHero.StringId : null));
								}
								else
								{
									flag5 = true;
								}
								bool flag6 = flag5;
								Agent aggressorAgent = null;
								bool flag7 = flag6;
								if (flag7)
								{
									aggressorAgent = Agent.Main;
								}
								else
								{
									bool flag8 = !string.IsNullOrEmpty(CS$<>8__locals1.aggressorId);
									if (flag8)
									{
										aggressorAgent = Enumerable.FirstOrDefault<Agent>(Mission.Current.Agents, (Agent a) => a != null && a.IsActive() && a.Character != null && a.Character.StringId == CS$<>8__locals1.aggressorId);
									}
								}
								int num = 0;
								this.HandleCompanionAgentDecision(agent, decision, flag6, aggressorAgent, ref num);
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-2021180330), ex.Message, ex);
			}
		}

		// Token: 0x06000A31 RID: 2609 RVA: 0x000BF868 File Offset: 0x000BDA68
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void SpawnCombatParticipants()
		{
			try
			{
				CombatSituationAnalysis analysis = this._activeCombat.Analysis;
				bool flag = Mission.Current != null;
				if (flag)
				{
					CombatPhrasesDisplay missionBehavior = new CombatPhrasesDisplay(analysis);
					Mission.Current.AddMissionBehavior(missionBehavior);
					this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(870668187));
					CombatPhrasePopupView.EnsureCreated();
				}
				bool needsDefenders = analysis.NeedsDefenders;
				if (needsDefenders)
				{
					this._defenderSpawner.SpawnDefenders(this._activeCombat);
				}
				bool civilianPanic = analysis.CivilianPanic;
				if (civilianPanic)
				{
					bool hasChildren = this.HasChildrenInLocation();
					this._civilianBehavior.InitiatePanic(this._activeCombat, hasChildren);
				}
				else
				{
					this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(529253751));
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-2118596420), ex.Message, ex);
			}
		}

		// Token: 0x06000A32 RID: 2610 RVA: 0x000BF960 File Offset: 0x000BDB60
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void SetupTeamHostility()
		{
			try
			{
				bool flag = Mission.Current == null;
				if (!flag)
				{
					Team playerTeam = Mission.Current.PlayerTeam;
					Team defenderTeam = Mission.Current.DefenderTeam;
					bool flag2 = playerTeam == null || defenderTeam == null;
					if (flag2)
					{
						this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-908234135));
					}
					else
					{
						ActiveCombat activeCombat = this._activeCombat;
						string text;
						if (activeCombat == null)
						{
							text = null;
						}
						else
						{
							CombatSituationAnalysis analysis = activeCombat.Analysis;
							text = ((analysis != null) ? analysis.AggressorStringId : null);
						}
						string text2 = text;
						bool flag3;
						if (!(text2 == <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-819386896)))
						{
							string a = text2;
							Hero mainHero = Hero.MainHero;
							flag3 = (a == ((mainHero != null) ? mainHero.StringId : null));
						}
						else
						{
							flag3 = true;
						}
						bool flag4 = flag3;
						bool flag5 = flag4;
						if (flag5)
						{
							playerTeam.SetIsEnemyOf(defenderTeam, true);
							defenderTeam.SetIsEnemyOf(playerTeam, true);
							this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(939659657));
						}
						else
						{
							playerTeam.SetIsEnemyOf(defenderTeam, false);
							defenderTeam.SetIsEnemyOf(playerTeam, false);
							this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1167556365));
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1244581244), ex.Message, ex);
			}
		}

		// Token: 0x06000A33 RID: 2611 RVA: 0x000BFABC File Offset: 0x000BDCBC
		[MethodImpl(MethodImplOptions.NoInlining)]
		private LocationType DetermineLocationType()
		{
			LocationType result;
			try
			{
				bool flag = Mission.Current == null;
				if (flag)
				{
					result = LocationType.LargeOutdoor;
				}
				else
				{
					string sceneName = Mission.Current.SceneName;
					string text = ((sceneName != null) ? sceneName.ToLower() : null) ?? "";
					bool flag2 = text.Contains(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-436513514)) || (text.Contains(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1586385775)) && text.Contains(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1559486858)));
					bool flag3 = text.Contains(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-2083324708));
					bool flag4 = text.Contains(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(2101609653));
					bool flag5 = text.Contains(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(113651161));
					bool flag6 = text.Contains(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1533160033));
					bool flag7 = text.Contains(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(375324877)) && text.Contains(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(722037755));
					bool flag8 = flag2 || flag3 || flag4 || flag5 || flag6 || flag7;
					bool flag9 = flag8;
					if (flag9)
					{
						this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-114556099), new object[]
						{
							flag2,
							flag3,
							flag4,
							flag5,
							flag6,
							flag7
						}));
						result = LocationType.SmallIndoor;
					}
					else
					{
						this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1640937525));
						result = LocationType.LargeOutdoor;
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(737202770), ex.Message, ex);
				result = LocationType.LargeOutdoor;
			}
			return result;
		}

		// Token: 0x06000A34 RID: 2612 RVA: 0x000BFC9C File Offset: 0x000BDE9C
		[MethodImpl(MethodImplOptions.NoInlining)]
		private bool HasChildrenInLocation()
		{
			bool result;
			try
			{
				bool flag = Mission.Current == null;
				if (flag)
				{
					result = false;
				}
				else
				{
					string sceneName = Mission.Current.SceneName;
					string text = ((sceneName != null) ? sceneName.ToLower() : null) ?? "";
					bool flag2 = text.Contains(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(421416266));
					bool flag3 = text.Contains(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1054929364));
					bool flag4 = text.Contains(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1331588021));
					bool flag5 = text.Contains(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-932456274));
					bool flag6 = text.Contains(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(950724188)) || (text.Contains(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(148566146)) && text.Contains(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-739781267)));
					bool flag7 = text.Contains(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(375324877)) && text.Contains(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(722037755));
					bool flag8 = flag2 || flag3 || flag4 || flag5 || flag6 || flag7;
					bool flag9 = flag8;
					if (flag9)
					{
						this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-693783844), new object[]
						{
							flag2,
							flag3,
							flag4,
							flag5,
							flag6,
							flag7
						}));
					}
					result = !flag8;
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1948862128), ex.Message, ex);
				result = false;
			}
			return result;
		}

		// Token: 0x06000A35 RID: 2613 RVA: 0x000BFE64 File Offset: 0x000BE064
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void PreparePlayerTroopsForBattle()
		{
			try
			{
				bool flag = Mission.Current == null || Mission.Current.PlayerTeam == null;
				if (!flag)
				{
					int num = 0;
					foreach (Agent agent in Mission.Current.PlayerTeam.ActiveAgents)
					{
						bool flag2 = agent != null && agent.IsActive() && agent.IsHuman;
						if (flag2)
						{
							this.WieldBestMeleeWeapon(agent);
							agent.SetWatchState(Agent.WatchState.Alarmed);
							num++;
						}
					}
					this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1149387571), num));
					this.PrepareNPCAttacker();
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1999694503), ex.Message, ex);
			}
		}

		// Token: 0x06000A36 RID: 2614 RVA: 0x000BFF74 File Offset: 0x000BE174
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void PrepareExistingDefenders()
		{
			try
			{
				SettlementCombatManager.<>c__DisplayClass55_0 CS$<>8__locals1 = new SettlementCombatManager.<>c__DisplayClass55_0();
				bool flag = Mission.Current == null || this._activeCombat == null;
				if (!flag)
				{
					bool flag2 = this._activeCombat.Analysis != null && !this._activeCombat.Analysis.CivilianPanic && !this._activeCombat.Analysis.NeedsDefenders;
					if (flag2)
					{
						this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(248448529));
					}
					else
					{
						SettlementCombatManager.<>c__DisplayClass55_0 CS$<>8__locals2 = CS$<>8__locals1;
						CombatSituationAnalysis analysis = this._activeCombat.Analysis;
						CS$<>8__locals2.aggressorId = ((analysis != null) ? analysis.AggressorStringId : null);
						bool flag3;
						if (!(CS$<>8__locals1.aggressorId == <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(699873898)))
						{
							string aggressorId = CS$<>8__locals1.aggressorId;
							Hero mainHero = Hero.MainHero;
							flag3 = (aggressorId == ((mainHero != null) ? mainHero.StringId : null));
						}
						else
						{
							flag3 = true;
						}
						bool flag4 = flag3;
						Agent agent = null;
						bool flag5 = flag4;
						if (flag5)
						{
							agent = Agent.Main;
						}
						else
						{
							bool flag6 = !string.IsNullOrEmpty(CS$<>8__locals1.aggressorId);
							if (flag6)
							{
								agent = Enumerable.FirstOrDefault<Agent>(Mission.Current.Agents, (Agent a) => a != null && a.IsActive() && a.Character != null && a.Character.StringId == CS$<>8__locals1.aggressorId);
							}
						}
						bool flag7 = agent == null;
						if (flag7)
						{
							this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-926187159));
						}
						this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(638307151) + ((agent != null) ? agent.Name.ToString() : <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1091927997)));
						int num = 0;
						PlayerReinforcementMissionLogic missionBehavior = Mission.Current.GetMissionBehavior<PlayerReinforcementMissionLogic>();
						foreach (Agent agent2 in Mission.Current.Agents)
						{
							bool flag8 = agent2 == null || !agent2.IsActive() || !agent2.IsHuman;
							if (!flag8)
							{
								bool flag9 = agent2 == Agent.Main;
								if (!flag9)
								{
									bool flag10 = agent2 == agent;
									if (!flag10)
									{
										bool flag11 = this._civilianBehavior != null && this._civilianBehavior.IsAgentUnderCivilianControl(agent2);
										if (!flag11)
										{
											bool flag12 = !flag4 && agent2.Team == Mission.Current.PlayerTeam;
											if (!flag12)
											{
												bool flag13 = missionBehavior != null && missionBehavior.IsSummonedTroop(agent2);
												if (!flag13)
												{
													CharacterObject characterObject = agent2.Character as CharacterObject;
													bool flag14 = characterObject == null;
													if (!flag14)
													{
														CompanionCombatDecision decision;
														bool flag15 = this._activeCombat.CompanionDecisions != null && characterObject.IsHero && this._activeCombat.CompanionDecisions.TryGetValue(characterObject.StringId, out decision);
														if (flag15)
														{
															this.HandleCompanionAgentDecision(agent2, decision, flag4, agent, ref num);
														}
														else
														{
															bool flag16 = Settlement.CurrentSettlement != null && Settlement.CurrentSettlement.IsTown;
															bool flag17 = characterObject.Occupation == Occupation.Bandit || characterObject.Occupation == Occupation.GangLeader || characterObject.Occupation == Occupation.Gangster;
															bool flag18 = flag16 && flag17;
															if (flag18)
															{
																bool flag19 = this._random.NextDouble() < 0.3;
																bool flag20 = flag19;
																if (flag20)
																{
																	this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1422022495) + agent2.Name);
																	bool flag21 = agent2.Team != Mission.Current.PlayerTeam;
																	if (flag21)
																	{
																		agent2.SetTeam(Mission.Current.PlayerTeam, true);
																	}
																	this.WieldBestMeleeWeapon(agent2);
																	agent2.SetWatchState(Agent.WatchState.Alarmed);
																	AgentFlag agentFlag = agent2.GetAgentFlags();
																	agentFlag |= AgentFlag.CanAttack;
																	agentFlag |= AgentFlag.CanDefend;
																	agentFlag &= ~AgentFlag.RunsAwayWhenHit;
																	agentFlag &= ~AgentFlag.CanGetScared;
																	agent2.SetAgentFlags(agentFlag);
																	bool flag22 = !flag4 && agent != null;
																	if (flag22)
																	{
																		agent2.SetLookAgent(agent);
																	}
																	num++;
																}
																else
																{
																	this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-685085888) + agent2.Name);
																	CivilianBehavior civilianBehavior = this._civilianBehavior;
																	if (civilianBehavior != null)
																	{
																		civilianBehavior.ForceAgentPanic(agent2);
																	}
																}
															}
															else
															{
																bool flag23 = this.IsDefenderOrMilitary(characterObject);
																bool flag24 = flag23;
																if (flag24)
																{
																	this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(562253166), agent2.Name, characterObject.IsHero));
																	bool flag25 = flag4 && agent2.Team != Mission.Current.DefenderTeam;
																	if (flag25)
																	{
																		agent2.SetTeam(Mission.Current.DefenderTeam, true);
																	}
																	this.WieldBestMeleeWeapon(agent2);
																	agent2.SetWatchState(Agent.WatchState.Alarmed);
																	bool flag26 = agent != null;
																	if (flag26)
																	{
																		agent2.SetLookAgent(agent);
																	}
																	agent2.SetAgentFlags(agent2.GetAgentFlags() | AgentFlag.CanAttack | AgentFlag.CanDefend);
																	num++;
																}
															}
														}
													}
												}
											}
										}
									}
								}
							}
						}
						this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1469387309), num));
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-888907866), ex.Message, ex);
			}
		}

		// Token: 0x06000A37 RID: 2615 RVA: 0x000C0500 File Offset: 0x000BE700
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void SpawnNPCsFromSmallLocation()
		{
			try
			{
				bool flag = Mission.Current == null || this._activeCombat == null || this._activeCombat.NPCsFromSmallLocation == null;
				if (!flag)
				{
					this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-432260943));
					this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-928601709), this._activeCombat.NPCsFromSmallLocation.Count));
					int num = 0;
					List<CharacterObject> list = new List<CharacterObject>();
					using (List<string>.Enumerator enumerator = this._activeCombat.NPCsFromSmallLocation.GetEnumerator())
					{
						while (enumerator.MoveNext())
						{
							string npcId = enumerator.Current;
							bool flag2 = string.IsNullOrEmpty(npcId);
							if (!flag2)
							{
								CharacterObject characterObject = MBObjectManager.Instance.GetObject<CharacterObject>(npcId);
								bool flag3 = characterObject == null;
								if (flag3)
								{
									Hero hero = Hero.FindFirst((Hero h) => h != null && h.StringId == npcId);
									bool flag4 = hero != null;
									if (flag4)
									{
										characterObject = hero.CharacterObject;
									}
								}
								bool flag5 = characterObject == null;
								if (flag5)
								{
									this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1563057144) + npcId);
								}
								else
								{
									bool isHero = characterObject.IsHero;
									if (isHero)
									{
										this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1807272186), characterObject.Name, npcId));
									}
									else
									{
										list.Add(characterObject);
										num++;
									}
								}
							}
						}
					}
					bool flag6 = list.Count > 0;
					if (flag6)
					{
						Vec3 vec = this._activeCombat.PlayerEntryPosition;
						bool flag7 = vec == Vec3.Zero;
						if (flag7)
						{
							bool flag8 = Agent.Main != null && Agent.Main.IsActive();
							if (flag8)
							{
								vec = Agent.Main.Position;
							}
							else
							{
								Mission mission = Mission.Current;
								bool flag9 = mission != null;
								if (flag9)
								{
									DefenderSpawner defenderSpawner = this._defenderSpawner;
								}
							}
						}
						this._defenderSpawner.SpawnDefendersAtPosition(list, this._activeCombat, vec, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1003578968), true);
					}
					this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1448993200), num));
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-857126496), ex.Message, ex);
			}
		}

		// Token: 0x06000A38 RID: 2616 RVA: 0x000C07D0 File Offset: 0x000BE9D0
		private bool IsDefenderOrMilitary(CharacterObject character)
		{
			bool result;
			try
			{
				bool isHero = character.IsHero;
				if (isHero)
				{
					result = true;
				}
				else
				{
					bool flag = character.Occupation == Occupation.Soldier || character.Occupation == Occupation.Guard || character.Occupation == Occupation.PrisonGuard || character.Occupation == Occupation.CaravanGuard || character.Occupation == Occupation.Mercenary || character.Occupation == Occupation.BannerBearer || character.Occupation == Occupation.GangLeader || character.Occupation == Occupation.Gangster || character.Occupation == Occupation.Bandit;
					if (flag)
					{
						result = true;
					}
					else
					{
						bool flag2 = character.Tier > 0 && character.Equipment != null;
						if (flag2)
						{
							bool flag3 = false;
							bool flag4 = false;
							for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
							{
								bool flag5 = !character.Equipment[equipmentIndex].IsEmpty;
								if (flag5)
								{
									flag3 = true;
									break;
								}
							}
							bool flag6 = !character.Equipment[EquipmentIndex.Body].IsEmpty || !character.Equipment[EquipmentIndex.NumAllWeaponSlots].IsEmpty;
							if (flag6)
							{
								flag4 = true;
							}
							bool flag7 = flag3 && flag4;
							if (flag7)
							{
								return true;
							}
						}
						result = false;
					}
				}
			}
			catch
			{
				result = false;
			}
			return result;
		}

		// Token: 0x06000A39 RID: 2617 RVA: 0x000C092C File Offset: 0x000BEB2C
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void PrepareNPCAttacker()
		{
			try
			{
				bool flag = this._activeCombat == null || this._activeCombat.TriggerType > CombatTriggerType.NPCAttack;
				if (!flag)
				{
					Hero triggerNPC = this._activeCombat.TriggerNPC;
					bool flag2 = triggerNPC == null || triggerNPC.CharacterObject == null;
					if (flag2)
					{
						this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(378091958));
					}
					else
					{
						Agent agent = Enumerable.FirstOrDefault<Agent>(Mission.Current.Agents, (Agent a) => a != null && a.IsActive() && a.Character != null && a.Character == triggerNPC.CharacterObject);
						bool flag3 = agent != null;
						if (flag3)
						{
							bool flag4 = this._civilianBehavior != null && this._civilianBehavior.IsAgentUnderCivilianControl(agent);
							if (flag4)
							{
								this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1339904173) + agent.Name + <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-359901992));
							}
							else
							{
								this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(311935553) + agent.Name);
								CombatSituationAnalysis analysis = this._activeCombat.Analysis;
								string a2 = (analysis != null) ? analysis.AggressorStringId : null;
								bool flag5 = a2 == triggerNPC.StringId;
								bool flag6 = flag5;
								if (flag6)
								{
									this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(298704272), triggerNPC.Name));
									bool flag7 = agent.Team != Mission.Current.DefenderTeam;
									if (flag7)
									{
										agent.SetTeam(Mission.Current.DefenderTeam, true);
									}
									this.WieldBestMeleeWeapon(agent);
									agent.SetWatchState(Agent.WatchState.Alarmed);
									bool flag8 = Agent.Main != null;
									if (flag8)
									{
										agent.SetLookAgent(Agent.Main);
									}
									agent.SetAgentFlags(agent.GetAgentFlags() | AgentFlag.CanAttack | AgentFlag.CanDefend);
									this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1594359663));
								}
							}
						}
						else
						{
							this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1124471673), triggerNPC.Name));
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1468296463), ex.Message, ex);
			}
		}

		// Token: 0x06000A3A RID: 2618 RVA: 0x000C0BA8 File Offset: 0x000BEDA8
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void WieldBestMeleeWeapon(Agent agent)
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
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-97161822), ex.Message, ex);
			}
		}

		// Token: 0x06000A3B RID: 2619 RVA: 0x000C0CD0 File Offset: 0x000BEED0
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void SheathPlayerTroopsWeapons()
		{
			try
			{
				bool flag = Mission.Current == null || Mission.Current.PlayerTeam == null;
				if (!flag)
				{
					int num = 0;
					int num2 = 0;
					foreach (Agent agent in Mission.Current.PlayerTeam.ActiveAgents)
					{
						bool flag2 = agent != null && agent.IsActive() && agent.IsHuman;
						if (flag2)
						{
							bool flag3 = agent == Agent.Main;
							if (!flag3)
							{
								CharacterObject characterObject = agent.Character as CharacterObject;
								bool flag4 = characterObject != null;
								if (flag4)
								{
									bool flag5 = characterObject.Occupation == Occupation.Soldier || characterObject.Occupation == Occupation.Guard || characterObject.Occupation == Occupation.PrisonGuard || characterObject.Occupation == Occupation.CaravanGuard || characterObject.Occupation == Occupation.Mercenary || characterObject.Occupation == Occupation.BannerBearer;
									bool flag6 = characterObject.IsHero && characterObject.HeroObject != null && characterObject.HeroObject.IsLord;
									bool flag7 = flag5 || flag6;
									if (flag7)
									{
										num2++;
										continue;
									}
								}
								bool flag8 = agent.GetWieldedItemIndex(Agent.HandIndex.MainHand) != EquipmentIndex.None;
								if (flag8)
								{
									agent.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.WithAnimation);
								}
								bool flag9 = agent.GetWieldedItemIndex(Agent.HandIndex.OffHand) != EquipmentIndex.None;
								if (flag9)
								{
									agent.TryToSheathWeaponInHand(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.WithAnimation);
								}
								num++;
							}
						}
					}
					this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-992516788), num, num2));
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1627450074), ex.Message, ex);
			}
		}

		// Token: 0x06000A3C RID: 2620 RVA: 0x0001D8F1 File Offset: 0x0001BAF1
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void StartCombatTracking()
		{
			this._statistics.StartTracking(this._activeCombat, this._civilianBehavior, this);
			this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1575670863));
		}

		// Token: 0x06000A3D RID: 2621 RVA: 0x000C0EDC File Offset: 0x000BF0DC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void OnAllEnemiesEliminated()
		{
			try
			{
				this._villagePostCombatInquiryShown = false;
				bool flag = this._activeCombat == null;
				if (flag)
				{
					this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1605189322));
				}
				else
				{
					this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(2061870064));
					this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(157550899), this._missionModeChanged));
					this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(836627750), this._previousMissionMode));
					SettlementCombatLogger logger = this._logger;
					string format = <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-2052757457);
					Mission mission = Mission.Current;
					logger.Log(string.Format(format, (mission != null) ? new MissionMode?(mission.Mode) : null));
					this._combatEnded = true;
					this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(109055911));
					this.ForcePlayerExitAfterCombat(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-171149069));
					bool flag2 = this._missionModeChanged && Mission.Current != null;
					if (flag2)
					{
						Mission.Current.SetMissionMode(this._previousMissionMode, false);
						this._missionModeChanged = false;
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1365233982), this._previousMissionMode));
					}
					else
					{
						this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1089212132), this._missionModeChanged, Mission.Current != null));
					}
					try
					{
						Mission mission2 = Mission.Current;
						MissionConversationLogic missionConversationLogic = (mission2 != null) ? mission2.GetMissionBehavior<MissionConversationLogic>() : null;
						if (missionConversationLogic != null)
						{
							missionConversationLogic.DisableStartConversation(false);
						}
						this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(38489079));
					}
					catch
					{
					}
					this.SheathPlayerTroopsWeapons();
					try
					{
						DelayedTaskManager delayedTaskManager = this._behavior.GetDelayedTaskManager();
						if (delayedTaskManager != null)
						{
							delayedTaskManager.AddTask(5.0, delegate
							{
								try
								{
									this.TryShowVillagePostCombatInquiry();
								}
								catch (Exception ex4)
								{
									this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1378801980), ex4.Message, ex4);
								}
							});
						}
					}
					catch (Exception ex)
					{
						this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1410865678), ex.Message, ex);
						this.TryShowVillagePostCombatInquiry();
					}
					try
					{
						Settlement settlement = this._activeCombat.Settlement;
						bool flag3 = settlement != null && (settlement.IsTown || settlement.IsCastle);
						if (flag3)
						{
							CombatSituationAnalysis analysis = this._activeCombat.Analysis;
							string text = (analysis != null) ? analysis.AggressorStringId : null;
							bool flag4;
							if (!(text == <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(699873898)))
							{
								string a = text;
								Hero mainHero = Hero.MainHero;
								flag4 = (a == ((mainHero != null) ? mainHero.StringId : null));
							}
							else
							{
								flag4 = true;
							}
							bool flag5 = flag4;
							bool flag6 = settlement.OwnerClan != null && settlement.OwnerClan == Clan.PlayerClan;
							bool flag7 = flag5 && !flag6;
							if (flag7)
							{
								TextObject textObject = new TextObject(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1266644748), null);
								TextObject textObject2 = new TextObject(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1324260458), null);
								InformationManager.ShowInquiry(new InquiryData(textObject.ToString(), textObject2.ToString(), true, false, GameTexts.FindText(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1568113624), null).ToString(), string.Empty, null, null, "", 0f, null, null, null), true, false);
							}
						}
					}
					catch (Exception ex2)
					{
						this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-2126414077), ex2.Message, ex2);
					}
					this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(811875848));
				}
			}
			catch (Exception ex3)
			{
				this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-790740686), ex3.Message, ex3);
			}
		}

		// Token: 0x06000A3E RID: 2622 RVA: 0x000C1324 File Offset: 0x000BF524
		[MethodImpl(MethodImplOptions.NoInlining)]
		private bool ForcePlayerExitAfterCombat(string reason)
		{
			bool result;
			try
			{
				bool flag = !PlayerEncounter.InsideSettlement;
				if (flag)
				{
					this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-195263552) + reason + <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1291972570));
					result = false;
				}
				else
				{
					bool flag2 = Mission.Current != null;
					if (flag2)
					{
						this._logger.Log(string.Concat(new string[]
						{
							<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-4453640),
							reason,
							<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1842137245),
							Mission.Current.SceneName,
							<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-945209118)
						}));
						result = false;
					}
					else
					{
						bool flag3 = this._activeCombat == null;
						if (flag3)
						{
							this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-195263552) + reason + <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1195228228));
							result = false;
						}
						else
						{
							MobileParty mainParty = MobileParty.MainParty;
							string text;
							if (mainParty == null)
							{
								text = null;
							}
							else
							{
								Settlement currentSettlement = mainParty.CurrentSettlement;
								text = ((currentSettlement != null) ? currentSettlement.StringId : null);
							}
							string text2;
							if ((text2 = text) == null)
							{
								ActiveCombat activeCombat = this._activeCombat;
								string text3;
								if (activeCombat == null)
								{
									text3 = null;
								}
								else
								{
									Settlement settlement = activeCombat.Settlement;
									text3 = ((settlement != null) ? settlement.StringId : null);
								}
								text2 = (text3 ?? <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(378266911));
							}
							string str = text2;
							this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-4453640) + reason + <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1398322674) + str);
							PlayerEncounter.LeaveSettlement();
							Campaign campaign = Campaign.Current;
							bool flag4 = ((campaign != null) ? campaign.CurrentMenuContext : null) != null;
							if (flag4)
							{
								GameMenu.ExitToLast();
								this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-4453640) + reason + <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(479199308));
							}
							result = true;
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1598638649), ex.Message, ex);
				result = false;
			}
			return result;
		}

		// Token: 0x06000A3F RID: 2623 RVA: 0x000C1534 File Offset: 0x000BF734
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void OnPlayerLeavesSettlement()
		{
			try
			{
				bool combatEnded = this._combatEnded;
				bool flag = this._activeCombat == null;
				if (flag)
				{
					this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-309934654));
				}
				else
				{
					bool flag2 = !combatEnded;
					if (flag2)
					{
						this.ApplyEscapePenaltyIfNeeded();
					}
					else
					{
						this._pendingEscapePenalty = false;
						this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1270642218));
					}
					bool flag3 = !combatEnded;
					if (flag3)
					{
						try
						{
							Settlement settlement = this._activeCombat.Settlement;
							bool flag4 = settlement != null && MobileParty.MainParty != null;
							if (flag4)
							{
								Vec2 v = settlement.GatePosition.ToVec2();
								Vec2 v2 = settlement.Position.ToVec2();
								Vec2 v3 = v - v2;
								bool flag5 = v3.LengthSquared <= 0.0001f;
								if (flag5)
								{
									v3 = new Vec2(1f, 0f);
								}
								else
								{
									v3 = v3.Normalized();
								}
								Vec2 vec = v + v3 * 3f;
								MobileParty.MainParty.Position = new CampaignVec2(vec, true);
								this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1639980882), vec));
							}
						}
						catch (Exception ex)
						{
							this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(155607217), ex.Message, ex);
						}
					}
					else
					{
						this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1333644615));
					}
					bool flag6 = !combatEnded;
					if (flag6)
					{
						try
						{
							bool insideSettlement = PlayerEncounter.InsideSettlement;
							if (insideSettlement)
							{
								MobileParty mainParty = MobileParty.MainParty;
								string text;
								if (mainParty == null)
								{
									text = null;
								}
								else
								{
									Settlement currentSettlement = mainParty.CurrentSettlement;
									text = ((currentSettlement != null) ? currentSettlement.StringId : null);
								}
								string str = text ?? <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-547646552);
								PlayerEncounter.LeaveSettlement();
								this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(642823453) + str);
							}
							Campaign campaign = Campaign.Current;
							bool flag7 = ((campaign != null) ? campaign.CurrentMenuContext : null) != null;
							if (flag7)
							{
								GameMenu.ExitToLast();
								this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1726006482));
							}
						}
						catch (Exception ex2)
						{
							this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-433426610), ex2.Message, ex2);
						}
					}
					else
					{
						this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1853185738));
					}
					bool flag8 = this._activeCombat.Analysis == null;
					if (flag8)
					{
						this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1920947784));
						this._activeCombat = null;
						this._combatEnded = false;
					}
					else
					{
						this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1596384785));
						bool flag9 = this._missionModeChanged && Mission.Current != null;
						if (flag9)
						{
							Mission.Current.SetMissionMode(this._previousMissionMode, false);
							this._missionModeChanged = false;
							this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1363683297), this._previousMissionMode));
						}
						try
						{
							Mission mission = Mission.Current;
							MissionConversationLogic missionConversationLogic = (mission != null) ? mission.GetMissionBehavior<MissionConversationLogic>() : null;
							if (missionConversationLogic != null)
							{
								missionConversationLogic.DisableStartConversation(false);
							}
							this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(41343506));
						}
						catch
						{
						}
						this.SheathPlayerTroopsWeapons();
						this._statistics.StopTracking();
						CombatResult combatResult = this._statistics.GetCombatResult();
						this._logger.LogCombatEnded(this._activeCombat.Settlement.StringId, combatResult.TotalKilled, combatResult.TotalWounded, combatResult.CiviliansKilled, combatResult.CiviliansWounded);
						bool flag10 = this._activeCombat.Settlement.IsVillage && combatResult.CiviliansKilled > 0;
						if (flag10)
						{
							bool flag11 = SettlementPenaltyManager.Instance != null;
							if (flag11)
							{
								SettlementPenaltyManager.Instance.ApplyCasualtiesToVillage(this._activeCombat.Settlement, combatResult.CiviliansKilled);
							}
						}
						int num = combatResult.MilitiaKilled + combatResult.SimpleDefendersKilled;
						bool flag12 = num > 0;
						if (flag12)
						{
							bool flag13 = SettlementPenaltyManager.Instance != null;
							if (flag13)
							{
								SettlementPenaltyManager.Instance.ApplyMilitiaCasualties(this._activeCombat.Settlement, num);
							}
						}
						ActiveCombat activeCombat = this._activeCombat;
						try
						{
							this.HandlePlayerKnockoutConsequences(activeCombat, combatResult);
						}
						catch (Exception ex3)
						{
							this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-749718880), ex3.Message, ex3);
						}
						DefenderSpawner defenderSpawner = this._defenderSpawner;
						if (defenderSpawner != null)
						{
							defenderSpawner.ClearCombat();
						}
						this._savedCombatForTransition = null;
						this._activeCombat = null;
						this._combatEnded = false;
						this._currentPostCombatRetryAttempt = 0;
						this.SendPostCombatEventPrompt(activeCombat, combatResult);
					}
				}
			}
			catch (Exception ex4)
			{
				this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1432538147), ex4.Message, ex4);
			}
		}

		// Token: 0x06000A40 RID: 2624 RVA: 0x000C1AC4 File Offset: 0x000BFCC4
		[MethodImpl(MethodImplOptions.NoInlining)]
		internal void RegisterPlayerEscapeAttempt(int totalTroops, int nearbyTroops)
		{
			try
			{
				this._escapeTotalTroops = Math.Max(1, totalTroops);
				this._escapeNearbyTroops = Math.Min(Math.Max(0, nearbyTroops), this._escapeTotalTroops);
				bool flag = this._escapeNearbyTroops <= 0;
				if (flag)
				{
					this._escapeNearbyTroops = Math.Min(this._escapeTotalTroops, Math.Max(1, nearbyTroops));
				}
				bool flag2 = this._escapeTotalTroops <= 0;
				if (flag2)
				{
					this._pendingEscapePenalty = false;
				}
				else
				{
					float num = (float)this._escapeNearbyTroops / (float)this._escapeTotalTroops;
					bool flag3 = num < 0.7f;
					if (flag3)
					{
						this._pendingEscapePenalty = true;
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-2004745897), this._escapeNearbyTroops, this._escapeTotalTroops, num));
					}
					else
					{
						this._pendingEscapePenalty = false;
						this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-164179835), num, 0.7f));
					}
				}
			}
			catch (Exception ex)
			{
				this._pendingEscapePenalty = false;
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(2084300101), ex.Message, ex);
			}
		}

		// Token: 0x06000A41 RID: 2625 RVA: 0x000C1C24 File Offset: 0x000BFE24
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void ApplyEscapePenaltyIfNeeded()
		{
			bool flag = !this._pendingEscapePenalty;
			if (!flag)
			{
				this._pendingEscapePenalty = false;
				int escapeTotalTroops = this._escapeTotalTroops;
				int num = Math.Min(this._escapeNearbyTroops, escapeTotalTroops);
				this._escapeTotalTroops = 0;
				this._escapeNearbyTroops = 0;
				bool flag2 = escapeTotalTroops <= 0;
				if (!flag2)
				{
					float num2 = (float)num / (float)escapeTotalTroops;
					bool flag3 = num2 >= 0.7f;
					if (!flag3)
					{
						int num3 = (int)Math.Ceiling((double)((float)escapeTotalTroops * (0.7f - num2)));
						bool flag4 = num3 <= 0;
						if (!flag4)
						{
							int num4 = this.SacrificePlayerTroops(num3);
							bool flag5 = num4 <= 0;
							if (flag5)
							{
								this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1508401751));
							}
							else
							{
								this.ShowEscapePenaltyPopup(num4, escapeTotalTroops, num, num2);
								this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(771827236), new object[]
								{
									num4,
									escapeTotalTroops,
									num,
									num2
								}));
							}
						}
					}
				}
			}
		}

		// Token: 0x06000A42 RID: 2626 RVA: 0x000C1D48 File Offset: 0x000BFF48
		[MethodImpl(MethodImplOptions.NoInlining)]
		private int SacrificePlayerTroops(int requested)
		{
			int result;
			try
			{
				bool flag = MobileParty.MainParty == null || requested <= 0;
				if (flag)
				{
					result = 0;
				}
				else
				{
					TroopRoster memberRoster = MobileParty.MainParty.MemberRoster;
					int num = 0;
					for (int i = 0; i < requested; i++)
					{
						CharacterObject characterObject = null;
						float num2 = float.MaxValue;
						foreach (TroopRosterElement troopRosterElement in memberRoster.GetTroopRoster())
						{
							bool flag2 = troopRosterElement.Character == null || troopRosterElement.Character.IsHero;
							if (!flag2)
							{
								int num3 = troopRosterElement.Number - troopRosterElement.WoundedNumber;
								bool flag3 = num3 <= 0;
								if (!flag3)
								{
									float num4 = (float)troopRosterElement.Character.Level + ((troopRosterElement.WoundedNumber > 0) ? -0.5f : 0f);
									bool flag4 = num4 < num2;
									if (flag4)
									{
										num2 = num4;
										characterObject = troopRosterElement.Character;
									}
								}
							}
						}
						bool flag5 = characterObject == null;
						if (flag5)
						{
							break;
						}
						memberRoster.AddToCounts(characterObject, -1, false, 0, 0, true, -1);
						num++;
					}
					result = num;
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1904102443), ex.Message, ex);
				result = 0;
			}
			return result;
		}

		// Token: 0x06000A43 RID: 2627 RVA: 0x000C1EE8 File Offset: 0x000C00E8
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void ShowEscapePenaltyPopup(int removed, int total, int nearby, float ratio)
		{
			try
			{
				TextObject textObject = new TextObject(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1030690469), null);
				TextObject textObject2 = new TextObject(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-2145879561), null);
				textObject2.SetTextVariable(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1795752844), removed);
				TextObject textObject3 = new TextObject(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(2069700049), null);
				textObject3.SetTextVariable(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1930262283), nearby);
				textObject3.SetTextVariable(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1621203), total);
				textObject3.SetTextVariable(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1655216854), MathF.Round(ratio * 100f, 1), 2);
				string text = string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(2043237487), textObject2, textObject3);
				InformationManager.ShowInquiry(new InquiryData(textObject.ToString(), text, true, false, GameTexts.FindText(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-2016381142), null).ToString(), string.Empty, null, null, "", 0f, null, null, null), true, false);
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-839185894), ex.Message, ex);
			}
		}

		// Token: 0x06000A44 RID: 2628 RVA: 0x000C2020 File Offset: 0x000C0220
		[DebuggerStepThrough]
		private void SendPostCombatEventPrompt(ActiveCombat combat, CombatResult result)
		{
			SettlementCombatManager.<SendPostCombatEventPrompt>d__69 <SendPostCombatEventPrompt>d__ = new SettlementCombatManager.<SendPostCombatEventPrompt>d__69();
			<SendPostCombatEventPrompt>d__.<>t__builder = AsyncVoidMethodBuilder.Create();
			<SendPostCombatEventPrompt>d__.<>4__this = this;
			<SendPostCombatEventPrompt>d__.combat = combat;
			<SendPostCombatEventPrompt>d__.result = result;
			<SendPostCombatEventPrompt>d__.<>1__state = -1;
			<SendPostCombatEventPrompt>d__.<>t__builder.Start<SettlementCombatManager.<SendPostCombatEventPrompt>d__69>(ref <SendPostCombatEventPrompt>d__);
		}

		// Token: 0x06000A45 RID: 2629 RVA: 0x000C2068 File Offset: 0x000C0268
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void HandlePostCombatAIError()
		{
			this._currentPostCombatRetryAttempt++;
			bool flag = this._currentPostCombatRetryAttempt < 3;
			if (flag)
			{
				this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-567911346), 5f, this._currentPostCombatRetryAttempt + 1, 3));
				this._behavior.GetDelayedTaskManager().AddTask(5.0, delegate
				{
					bool flag2 = this._postCombatRetryData != null && this._postCombatRetryResult != null;
					if (flag2)
					{
						this.SendPostCombatEventPrompt(this._postCombatRetryData, this._postCombatRetryResult);
					}
				});
			}
			else
			{
				this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-234075894), string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-28552553), 3), null);
				this._currentPostCombatRetryAttempt = 0;
				this._postCombatRetryData = null;
				this._postCombatRetryResult = null;
				InformationManager.DisplayMessage(new InformationMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(740064411), \u200D\u206E\u200B\u202E\u206A\u202A\u200E\u202E\u200E\u202E\u202A\u202B\u200C\u206F\u202A\u206E\u206E\u200F\u206B\u202D\u200C\u200F\u206E\u200C\u206E\u202E\u200B\u200D\u202D\u202E\u206D\u206E\u202B\u200F\u200C\u206A\u202A\u202C\u206D\u202E.\u202C\u200D\u202E\u200D\u206F\u206E\u206C\u202E\u206A\u206C\u206F\u200F\u206F\u200C\u206A\u206D\u206B\u200B\u206B\u202C\u202A\u202E\u200B\u200B\u206B\u200D\u200D\u202B\u206F\u202E\u202E\u202E\u200F\u200C\u202A\u202D\u206A\u202D\u200F\u200C\u202E));
			}
		}

		// Token: 0x06000A46 RID: 2630 RVA: 0x000C2158 File Offset: 0x000C0358
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void ProcessPostCombatEventResponse(ActiveCombat combat, CombatResult combatResult, string aiResponse)
		{
			try
			{
				bool flag = combat == null;
				if (flag)
				{
					this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1781026417));
				}
				else
				{
					this._logger.LogPostCombatEventResponse(combat.Settlement.StringId, aiResponse);
					this._eventCreator.CreatePostCombatEvent(aiResponse, combat.Settlement, combatResult);
					this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1105807391), combat.Settlement.Name));
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-381037610), ex.Message, ex);
			}
		}

		// Token: 0x06000A47 RID: 2631 RVA: 0x0001D925 File Offset: 0x0001BB25
		public CombatStatistics GetStatistics()
		{
			return this._statistics;
		}

		// Token: 0x170001F3 RID: 499
		// (get) Token: 0x06000A48 RID: 2632 RVA: 0x0001D92D File Offset: 0x0001BB2D
		public bool HasActiveCombat
		{
			get
			{
				return this._activeCombat != null;
			}
		}

		// Token: 0x06000A49 RID: 2633 RVA: 0x000C2218 File Offset: 0x000C0418
		public Settlement GetActiveCombatSettlement()
		{
			bool flag = this._activeCombat != null;
			Settlement result;
			if (flag)
			{
				result = this._activeCombat.Settlement;
			}
			else
			{
				bool flag2 = this._savedCombatForTransition != null;
				if (flag2)
				{
					result = this._savedCombatForTransition.Settlement;
				}
				else
				{
					bool flag3 = this._postCombatRetryData != null;
					if (flag3)
					{
						result = this._postCombatRetryData.Settlement;
					}
					else
					{
						result = null;
					}
				}
			}
			return result;
		}

		// Token: 0x06000A4A RID: 2634 RVA: 0x000C227C File Offset: 0x000C047C
		public bool IsCurrentLocationLargeOutdoor()
		{
			bool result;
			try
			{
				bool flag = this._activeCombat != null;
				if (flag)
				{
					result = (this._activeCombat.LocationType == LocationType.LargeOutdoor);
				}
				else
				{
					bool flag2 = this._savedCombatForTransition != null;
					if (flag2)
					{
						result = (this._savedCombatForTransition.LocationType == LocationType.LargeOutdoor);
					}
					else
					{
						result = (this.DetermineLocationType() == LocationType.LargeOutdoor);
					}
				}
			}
			catch
			{
				result = true;
			}
			return result;
		}

		// Token: 0x06000A4B RID: 2635 RVA: 0x000C22EC File Offset: 0x000C04EC
		public bool TryGetCompanionDecision(string heroStringId, out CompanionCombatDecision decision)
		{
			decision = CompanionCombatDecision.StayOut;
			bool flag = string.IsNullOrEmpty(heroStringId);
			bool result;
			if (flag)
			{
				result = false;
			}
			else
			{
				ActiveCombat activeCombat = this._activeCombat ?? this._savedCombatForTransition;
				bool flag2 = ((activeCombat != null) ? activeCombat.CompanionDecisions : null) != null && activeCombat.CompanionDecisions.TryGetValue(heroStringId, out decision);
				result = flag2;
			}
			return result;
		}

		// Token: 0x06000A4C RID: 2636 RVA: 0x000C2348 File Offset: 0x000C0548
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void TryShowVillagePostCombatInquiry()
		{
			try
			{
				bool villagePostCombatInquiryShown = this._villagePostCombatInquiryShown;
				if (!villagePostCombatInquiryShown)
				{
					bool flag = this._activeCombat == null;
					if (!flag)
					{
						Settlement settlement = this._activeCombat.Settlement;
						bool flag2 = settlement == null || !settlement.IsVillage;
						if (flag2)
						{
							this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(311146951));
						}
						else
						{
							CombatSituationAnalysis analysis = this._activeCombat.Analysis;
							string text = (analysis != null) ? analysis.AggressorStringId : null;
							bool flag3;
							if (!(text == <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1636222184)))
							{
								string a = text;
								Hero mainHero = Hero.MainHero;
								flag3 = (a == ((mainHero != null) ? mainHero.StringId : null));
							}
							else
							{
								flag3 = true;
							}
							bool flag4 = flag3;
							bool flag5 = !flag4;
							if (flag5)
							{
								this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-483226647));
							}
							else
							{
								bool flag6 = this._activeCombat.Analysis != null && !this._activeCombat.Analysis.NeedsDefenders;
								if (flag6)
								{
									this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(2064586324));
								}
								else
								{
									bool flag7 = Mission.Current == null;
									if (flag7)
									{
										this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1417816422));
									}
									else
									{
										this._villagePostCombatInquiryShown = true;
										List<InquiryElement> inquiryElements = new List<InquiryElement>
										{
											new InquiryElement(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(754212373), new TextObject(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1981323506), null).ToString(), null, true, new TextObject(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(211413360), null).ToString()),
											new InquiryElement(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1321918314), new TextObject(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-19680998), null).ToString(), null, true, new TextObject(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1700971256), null).ToString()),
											new InquiryElement(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1336170578), new TextObject(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1698684426), null).ToString(), null, true, new TextObject(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(292749887), null).ToString()),
											new InquiryElement(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-225035549), new TextObject(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-665167289), null).ToString(), null, true, new TextObject(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1410110834), null).ToString())
										};
										TextObject textObject = new TextObject(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(297530227), null);
										TextObject textObject2 = new TextObject(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1584083472), null);
										MultiSelectionInquiryData data = new MultiSelectionInquiryData(textObject.ToString(), textObject2.ToString(), inquiryElements, true, 1, 1, GameTexts.FindText(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1809176277), null).ToString(), GameTexts.FindText(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1287058593), null).ToString(), new Action<List<InquiryElement>>(this.OnVillagePostCombatSelection), null, string.Empty, false);
										MBInformationManager.ShowMultiSelectionInquiry(data, false, false);
										this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(253934130));
									}
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-2001265768), ex.Message, ex);
			}
		}

		// Token: 0x06000A4D RID: 2637 RVA: 0x000C26B4 File Offset: 0x000C08B4
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void OnVillagePostCombatSelection(List<InquiryElement> elements)
		{
			try
			{
				bool flag = elements == null || elements.Count == 0;
				if (flag)
				{
					this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1613006496));
				}
				else
				{
					InquiryElement inquiryElement = elements[0];
					string text = ((inquiryElement != null) ? inquiryElement.Identifier : null) as string;
					bool flag2 = string.IsNullOrEmpty(text);
					if (flag2)
					{
						this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1635058631));
					}
					else
					{
						ActiveCombat activeCombat;
						if ((activeCombat = this._activeCombat) == null)
						{
							activeCombat = (this._savedCombatForTransition ?? this._postCombatRetryData);
						}
						ActiveCombat activeCombat2 = activeCombat;
						bool flag3 = activeCombat2 == null || activeCombat2.Settlement == null || !activeCombat2.Settlement.IsVillage;
						if (flag3)
						{
							this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-544085861));
						}
						else
						{
							Settlement settlement = activeCombat2.Settlement;
							bool flag4 = text == <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1264582763) || text == <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(479337761);
							bool flag5 = text == <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1253283453) || text == <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1759240112);
							bool flag6 = flag4;
							if (flag6)
							{
								this.OpenVillageLootScreen(settlement);
								activeCombat2.VillageLooted = true;
							}
							bool flag7 = flag5;
							if (flag7)
							{
								this.BurnVillage(settlement);
								activeCombat2.VillageBurned = true;
							}
							bool flag8 = !flag4 && !flag5;
							if (flag8)
							{
								this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1674752474));
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(145957095), ex.Message, ex);
			}
		}

		// Token: 0x06000A4E RID: 2638 RVA: 0x000C2894 File Offset: 0x000C0A94
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void OpenVillageLootScreen(Settlement settlement)
		{
			try
			{
				bool flag = settlement == null || !settlement.IsVillage;
				if (!flag)
				{
					bool flag2 = MobileParty.MainParty == null;
					if (!flag2)
					{
						Village village = settlement.Village;
						bool flag3 = village == null || village.VillageType == null;
						if (!flag3)
						{
							ItemRoster itemRoster = new ItemRoster();
							int num = 0;
							int num2 = 0;
							bool flag4 = this._postCombatRetryResult != null;
							if (flag4)
							{
								num = this._postCombatRetryResult.CiviliansKilled;
								num2 = this._postCombatRetryResult.MilitiaKilled + this._postCombatRetryResult.SimpleDefendersKilled;
							}
							float num3 = 1f + (float)num * 0.03f + (float)num2 * 0.01f;
							num3 = MathF.Clamp(num3, 1f, 4f);
							int num4 = MathF.Max((int)(village.Hearth * 0.1f * num3), 20);
							Campaign campaign = Campaign.Current;
							int? num5;
							if (campaign == null)
							{
								num5 = null;
							}
							else
							{
								GameModels models = campaign.Models;
								if (models == null)
								{
									num5 = null;
								}
								else
								{
									RaidModel raidModel = models.RaidModel;
									num5 = ((raidModel != null) ? new int?(raidModel.GoldRewardForEachLostHearth) : null);
								}
							}
							int? num6 = num5;
							int valueOrDefault = num6.GetValueOrDefault();
							bool flag5 = valueOrDefault > 0;
							if (flag5)
							{
								GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, num4 * valueOrDefault, false);
							}
							for (int i = 0; i < village.VillageType.Productions.Count; i++)
							{
								ValueTuple<ItemObject, float> valueTuple = village.VillageType.Productions[i];
								ItemObject item = valueTuple.Item1;
								int num7 = (int)(valueTuple.Item2 / 60f * (float)num4);
								bool flag6 = num7 > 0;
								if (flag6)
								{
									itemRoster.AddToCounts(item, num7);
								}
							}
							bool flag7 = itemRoster.Count <= 0;
							if (flag7)
							{
								this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(824040066));
							}
							else
							{
								InventoryScreenHelper.OpenScreenAsLoot(new Dictionary<PartyBase, ItemRoster>
								{
									{
										PartyBase.MainParty,
										itemRoster
									}
								});
								this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1125635261), settlement.StringId, itemRoster.Count));
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1586202164), ex.Message, ex);
			}
		}

		// Token: 0x06000A4F RID: 2639 RVA: 0x000C2B14 File Offset: 0x000C0D14
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void BurnVillage(Settlement settlement)
		{
			try
			{
				bool flag = settlement == null || !settlement.IsVillage;
				if (!flag)
				{
					bool flag2 = MobileParty.MainParty != null;
					if (flag2)
					{
						ChangeVillageStateAction.ApplyBySettingToLooted(settlement, MobileParty.MainParty);
					}
					float settlementHitPoints = settlement.SettlementHitPoints;
					bool flag3 = settlementHitPoints > 0f;
					if (flag3)
					{
						IncreaseSettlementHealthAction.Apply(settlement, -settlementHitPoints);
					}
					this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-101674396) + settlement.StringId + <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1943769164));
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1026843400), ex.Message, ex);
			}
		}

		// Token: 0x06000A50 RID: 2640 RVA: 0x000C2BDC File Offset: 0x000C0DDC
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void HandlePlayerKnockoutConsequences(ActiveCombat combat, CombatResult result)
		{
			try
			{
				bool flag = this._playerKnockout == null || combat == null || combat.Settlement == null;
				if (!flag)
				{
					SettlementCombatManager.PlayerKnockoutInfo playerKnockout = this._playerKnockout;
					this._playerKnockout = null;
					bool flag2 = !playerKnockout.IsAggressor;
					if (!flag2)
					{
						float num = MathF.Max(0.1f, playerKnockout.PlayerPower);
						float num2 = MathF.Max(0.1f, playerKnockout.EnemyPower);
						float num3 = num / num2;
						bool flag3 = num3 >= 1f;
						if (flag3)
						{
							bool flag4 = result != null;
							if (flag4)
							{
								result.PlayerEvacuated = true;
								result.Participants.Add(Hero.MainHero.StringId);
							}
							TextObject textObject = new TextObject(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1172942931), null);
							InformationManager.ShowInquiry(new InquiryData(new TextObject(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1709105383), null).ToString(), textObject.ToString(), true, false, GameTexts.FindText(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1243602992), null).ToString(), string.Empty, null, null, "", 0f, null, null, null), true, false);
						}
						else
						{
							Settlement settlement = combat.Settlement;
							bool flag5 = settlement == null;
							if (!flag5)
							{
								Settlement settlement2 = null;
								bool flag6 = settlement.IsTown || settlement.IsCastle;
								if (flag6)
								{
									settlement2 = settlement;
								}
								else
								{
									bool isVillage = settlement.IsVillage;
									if (isVillage)
									{
										Village village = settlement.Village;
										settlement2 = ((village != null) ? village.Bound : null);
									}
								}
								bool flag7 = settlement2 == null;
								if (flag7)
								{
									IFaction mapFaction = settlement.MapFaction;
									float num4 = float.MaxValue;
									foreach (Settlement settlement3 in Settlement.All)
									{
										bool flag8 = (settlement3.IsTown || settlement3.IsCastle) && settlement3.MapFaction == mapFaction;
										if (flag8)
										{
											float num5 = settlement3.GatePosition.DistanceSquared(settlement.GatePosition);
											bool flag9 = num5 < num4;
											if (flag9)
											{
												num4 = num5;
												settlement2 = settlement3;
											}
										}
									}
								}
								bool flag10 = settlement2 == null;
								if (!flag10)
								{
									try
									{
										IFaction mapFaction2 = settlement.MapFaction;
										bool flag11 = mapFaction2 != null;
										if (flag11)
										{
											ChangeCrimeRatingAction.Apply(mapFaction2, 40f, false);
										}
									}
									catch (Exception ex)
									{
										this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-164160573), ex.Message, ex);
									}
									try
									{
										HashSet<Hero> hashSet = new HashSet<Hero>();
										try
										{
											bool flag12 = combat.PlayerCompanions != null;
											if (flag12)
											{
												foreach (Hero hero in combat.PlayerCompanions)
												{
													bool flag13 = hero == null || hero == Hero.MainHero;
													if (!flag13)
													{
														bool flag14 = hero.IsDead || hero.IsPrisoner || hero.IsNotable;
														if (!flag14)
														{
															hashSet.Add(hero);
														}
													}
												}
											}
											bool flag15 = result != null && result.LordsArrived != null;
											if (flag15)
											{
												using (List<LordArrivalInfo>.Enumerator enumerator3 = result.LordsArrived.GetEnumerator())
												{
													while (enumerator3.MoveNext())
													{
														LordArrivalInfo lordInfo = enumerator3.Current;
														bool flag16 = lordInfo == null || !lordInfo.OnPlayerSide || string.IsNullOrEmpty(lordInfo.LordStringId);
														if (!flag16)
														{
															Hero hero2 = Hero.FindFirst((Hero h) => h.StringId == lordInfo.LordStringId);
															bool flag17 = hero2 != null && hero2 != Hero.MainHero && !hero2.IsDead && !hero2.IsPrisoner && !hero2.IsNotable;
															if (flag17)
															{
																hashSet.Add(hero2);
															}
														}
													}
												}
											}
										}
										catch (Exception ex2)
										{
											this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(925403579), ex2.Message, ex2);
										}
										bool flag18 = MobileParty.MainParty != null;
										if (flag18)
										{
											try
											{
												TroopRoster memberRoster = MobileParty.MainParty.MemberRoster;
												int i = memberRoster.Count - 1;
												while (i >= 0)
												{
													TroopRosterElement elementCopyAtIndex = memberRoster.GetElementCopyAtIndex(i);
													bool flag19 = elementCopyAtIndex.Character != null;
													if (flag19)
													{
														bool flag20 = elementCopyAtIndex.Character.IsHero && elementCopyAtIndex.Character.HeroObject == Hero.MainHero;
														if (!flag20)
														{
															bool flag21 = elementCopyAtIndex.Number > 0;
															if (flag21)
															{
																memberRoster.AddToCounts(elementCopyAtIndex.Character, -elementCopyAtIndex.Number, false, 0, 0, true, -1);
															}
														}
													}
													IL_4D2:
													i--;
													continue;
													goto IL_4D2;
												}
											}
											catch (Exception ex3)
											{
												this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(898941017), ex3.Message, ex3);
											}
										}
										bool flag22 = Hero.MainHero != null;
										if (flag22)
										{
											TeleportHeroAction.ApplyImmediateTeleportToSettlement(Hero.MainHero, settlement2);
										}
										PartyBase party = settlement2.Party;
										bool flag23 = party != null;
										if (flag23)
										{
											TakePrisonerAction.Apply(party, Hero.MainHero);
											bool flag24 = hashSet != null && hashSet.Count > 0;
											if (flag24)
											{
												foreach (Hero hero3 in hashSet)
												{
													try
													{
														bool flag25 = hero3 != null && !hero3.IsDead && !hero3.IsPrisoner;
														if (flag25)
														{
															TakePrisonerAction.Apply(party, hero3);
															bool flag26 = result != null && !string.IsNullOrEmpty(hero3.StringId);
															if (flag26)
															{
																bool flag27 = !result.CapturedHeroes.Contains(hero3.StringId);
																if (flag27)
																{
																	result.CapturedHeroes.Add(hero3.StringId);
																}
																bool flag28 = !result.Participants.Contains(hero3.StringId);
																if (flag28)
																{
																	result.Participants.Add(hero3.StringId);
																}
															}
														}
													}
													catch (Exception ex4)
													{
														this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1412217205), ex4.Message, ex4);
													}
												}
											}
										}
										bool flag29 = result != null;
										if (flag29)
										{
											result.PlayerCaptured = true;
											result.PlayerPrisonSettlement = settlement2;
											bool flag30 = !result.Participants.Contains(Hero.MainHero.StringId);
											if (flag30)
											{
												result.Participants.Add(Hero.MainHero.StringId);
											}
										}
										TextObject textObject2 = new TextObject(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(894991036), null).SetTextVariable(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-701363707), settlement.Name).SetTextVariable(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(53503622), settlement2.Name);
										InformationManager.ShowInquiry(new InquiryData(new TextObject(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1580371899), null).ToString(), textObject2.ToString(), true, false, GameTexts.FindText(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1243602992), null).ToString(), string.Empty, null, null, "", 0f, null, null, null), true, false);
									}
									catch (Exception ex5)
									{
										this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1570182965), ex5.Message, ex5);
									}
								}
							}
						}
					}
				}
			}
			catch (Exception ex6)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-749718880), ex6.Message, ex6);
			}
		}

		// Token: 0x04000632 RID: 1586
		private readonly AIInfluenceBehavior _behavior;

		// Token: 0x04000633 RID: 1587
		private readonly CombatPromptGenerator _promptGenerator;

		// Token: 0x04000634 RID: 1588
		private readonly CombatStatistics _statistics;

		// Token: 0x04000635 RID: 1589
		private readonly DefenderSpawner _defenderSpawner;

		// Token: 0x04000636 RID: 1590
		private readonly CivilianBehavior _civilianBehavior;

		// Token: 0x04000637 RID: 1591
		private readonly PostCombatEventCreator _eventCreator;

		// Token: 0x04000638 RID: 1592
		private readonly SettlementCombatLogger _logger;

		// Token: 0x04000639 RID: 1593
		private readonly Random _random = new Random();

		// Token: 0x0400063A RID: 1594
		private bool _villagePostCombatInquiryShown = false;

		// Token: 0x0400063B RID: 1595
		private ActiveCombat _activeCombat;

		// Token: 0x0400063C RID: 1596
		private bool _combatEnded = false;

		// Token: 0x0400063D RID: 1597
		private bool _pendingEscapePenalty = false;

		// Token: 0x0400063E RID: 1598
		private int _escapeTotalTroops = 0;

		// Token: 0x0400063F RID: 1599
		private int _escapeNearbyTroops = 0;

		// Token: 0x04000640 RID: 1600
		private ActiveCombat _savedCombatForTransition = null;

		// Token: 0x04000641 RID: 1601
		private string _previousSceneName = "";

		// Token: 0x04000642 RID: 1602
		private int _currentRetryAttempt = 0;

		// Token: 0x04000643 RID: 1603
		private int _currentPostCombatRetryAttempt = 0;

		// Token: 0x04000644 RID: 1604
		private ActiveCombat _postCombatRetryData = null;

		// Token: 0x04000645 RID: 1605
		private CombatResult _postCombatRetryResult = null;

		// Token: 0x04000646 RID: 1606
		private bool _missionModeChanged = false;

		// Token: 0x04000647 RID: 1607
		private MissionMode _previousMissionMode = MissionMode.StartUp;

		// Token: 0x04000648 RID: 1608
		private const int MAX_RETRY_ATTEMPTS = 3;

		// Token: 0x04000649 RID: 1609
		private const float RETRY_DELAY_SECONDS = 5f;

		// Token: 0x0400064A RID: 1610
		private SettlementCombatManager.PlayerKnockoutInfo _playerKnockout;

		// Token: 0x02000141 RID: 321
		private class PlayerKnockoutInfo
		{
			// Token: 0x0400064B RID: 1611
			public bool IsAggressor;

			// Token: 0x0400064C RID: 1612
			public float PlayerPower;

			// Token: 0x0400064D RID: 1613
			public float EnemyPower;

			// Token: 0x0400064E RID: 1614
			public Settlement Settlement;
		}
	}
}
