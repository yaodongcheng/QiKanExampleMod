using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AIInfluence.Behaviors.AIActions;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Missions;
using TaleWorlds.ObjectSystem;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x020000FC RID: 252
	public class CivilianBehavior
	{
		// Token: 0x06000830 RID: 2096 RVA: 0x000A8258 File Offset: 0x000A6458
		public CivilianBehavior(AIInfluenceBehavior behavior)
		{
			this._behavior = behavior;
			this._logger = SettlementCombatLogger.Instance;
		}

		// Token: 0x06000831 RID: 2097 RVA: 0x000A8324 File Offset: 0x000A6524
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void InitiatePanic(ActiveCombat combat, bool hasChildren = true)
		{
			try
			{
				Mission mission = Mission.Current;
				bool flag = mission == null;
				if (flag)
				{
					this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-108359966));
				}
				else
				{
					CombatSituationAnalysis analysis = combat.Analysis;
					bool flag2 = analysis == null;
					if (flag2)
					{
						this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1434652730));
					}
					else
					{
						bool flag3 = !analysis.CivilianPanic;
						if (flag3)
						{
							this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1815305043));
							this._currentCombat = combat;
							this._currentAnalysis = analysis;
							this.Cleanup();
						}
						else
						{
							this._currentCombat = combat;
							this._currentAnalysis = analysis;
							this._processedAgents.Clear();
							this.Cleanup();
							this._phraseTimers.Clear();
							this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(2048797253), combat.Settlement.Name, hasChildren));
							this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1811786635), analysis.AggressorStringId, analysis.CivilianPanic));
							SettlementCombatLogger logger = this._logger;
							string format = <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(850417974);
							object arg = mission.SceneName ?? <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1617165097);
							AgentReadOnlyList agents = mission.Agents;
							logger.Log(string.Format(format, arg, (agents != null) ? Enumerable.Count<Agent>(agents) : 0));
							bool flag4 = Enumerable.Any<SettlementCombatMissionLogic>(Enumerable.OfType<SettlementCombatMissionLogic>(mission.MissionBehaviors));
							bool flag5 = !flag4;
							if (flag5)
							{
								this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1697722691));
								SettlementCombatManager settlementCombatManager = this._behavior.GetSettlementCombatManager();
								bool flag6 = settlementCombatManager != null;
								if (flag6)
								{
									CombatStatistics statistics = settlementCombatManager.GetStatistics();
									bool flag7 = statistics != null;
									if (flag7)
									{
										SettlementCombatMissionLogic missionBehavior = new SettlementCombatMissionLogic(statistics, this._behavior, this, settlementCombatManager);
										mission.AddMissionBehavior(missionBehavior);
										this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1797991599));
									}
									else
									{
										this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1141729066));
									}
								}
								else
								{
									this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-487607753));
								}
							}
							else
							{
								this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(761565383));
							}
							bool flag8 = !hasChildren && analysis.CivilianPhrasesChild != null;
							if (flag8)
							{
								analysis.CivilianPhrasesChild.Clear();
								this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-2006001916));
							}
							Agent agent = null;
							bool flag9 = combat.TriggerType == CombatTriggerType.NPCAttack && combat.TriggerNPC != null;
							if (flag9)
							{
								agent = Enumerable.FirstOrDefault<Agent>(mission.Agents, (Agent a) => a != null && a.IsActive() && a.Character != null && a.Character.StringId == combat.TriggerNPC.StringId);
								bool flag10 = agent != null;
								if (flag10)
								{
									SettlementCombatLogger logger2 = this._logger;
									string str = <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1331169854);
									string name = agent.Name;
									logger2.Log(str + (((name != null) ? name.ToString() : null) ?? <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1617165097)) + <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1824400354));
									this.MakeCivilianFight(agent, analysis);
									this._processedAgents.Add(agent);
								}
								else
								{
									this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1530740059));
								}
							}
							List<Agent> list = this.FindCivilians(mission, combat);
							this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(22522270), list.Count));
							bool flag11 = list.Count == 0;
							if (flag11)
							{
								this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-183001071));
								DelayedTaskManager delayedTaskManager = this._behavior.GetDelayedTaskManager();
								bool flag12 = delayedTaskManager != null;
								if (flag12)
								{
									Func<Agent, bool> <>9__2;
									delayedTaskManager.AddTask(1.0, delegate
									{
										try
										{
											bool flag13 = Mission.Current == null || this._currentCombat == null || this._currentAnalysis == null;
											if (!flag13)
											{
												List<Agent> list2 = this.FindCivilians(Mission.Current, this._currentCombat);
												this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1703295426), list2.Count));
												bool flag14 = list2.Count > 0;
												if (flag14)
												{
													Agent triggerAgent = null;
													bool flag15 = this._currentCombat.TriggerType == CombatTriggerType.NPCAttack && this._currentCombat.TriggerNPC != null;
													if (flag15)
													{
														IEnumerable<Agent> agents2 = Mission.Current.Agents;
														Func<Agent, bool> func;
														if ((func = <>9__2) == null)
														{
															func = (<>9__2 = ((Agent a) => a != null && a.IsActive() && a.Character != null && a.Character.StringId == this._currentCombat.TriggerNPC.StringId));
														}
														triggerAgent = Enumerable.FirstOrDefault<Agent>(agents2, func);
													}
													this.ProcessCiviliansForPanicAndFight(list2, this._currentCombat, this._currentAnalysis, triggerAgent);
												}
											}
										}
										catch (Exception ex2)
										{
											this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1654375031), ex2.Message, ex2);
										}
									});
								}
							}
							else
							{
								this.ProcessCiviliansForPanicAndFight(list, combat, analysis, agent);
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1813422491), ex.Message, ex);
			}
		}

		// Token: 0x06000832 RID: 2098 RVA: 0x000A87BC File Offset: 0x000A69BC
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void ProcessCiviliansForPanicAndFight(List<Agent> civilians, ActiveCombat combat, CombatSituationAnalysis analysis, Agent triggerAgent)
		{
			try
			{
				bool flag = civilians == null || civilians.Count == 0;
				if (flag)
				{
					this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1646891896));
				}
				else
				{
					DelayedTaskManager delayedTaskManager = this._behavior.GetDelayedTaskManager();
					bool flag2 = delayedTaskManager != null;
					if (flag2)
					{
						delayedTaskManager.AddTask(1.0, delegate
						{
							try
							{
								List<Agent> list = new List<Agent>();
								foreach (Agent agent2 in civilians)
								{
									bool flag13 = triggerAgent != null && agent2 == triggerAgent;
									if (!flag13)
									{
										bool flag14 = !analysis.CivilianPanic;
										if (flag14)
										{
											CharacterObject characterObject2 = agent2.Character as CharacterObject;
											bool flag15 = characterObject2 != null && characterObject2.IsHero && characterObject2.HeroObject != null && characterObject2.HeroObject.IsNotable;
											if (flag15)
											{
												continue;
											}
										}
										CivilianBehavior.CivilianType civilianType2 = this.GetCivilianType(agent2);
										bool flag16 = civilianType2 == CivilianBehavior.CivilianType.Child;
										if (!flag16)
										{
											bool flag17 = this.HasUsableWeapon(agent2);
											bool flag18 = civilianType2 == CivilianBehavior.CivilianType.Woman;
											if (flag18)
											{
												bool flag19 = flag17;
												if (flag19)
												{
													list.Add(agent2);
												}
											}
											else
											{
												bool flag20 = civilianType2 == CivilianBehavior.CivilianType.Man;
												if (flag20)
												{
													double num3 = 0.25;
													bool flag21 = this._random.NextDouble() < num3;
													this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-592507960), new object[]
													{
														agent2.Name,
														flag17,
														num3,
														flag21
													}));
													bool flag22 = flag21;
													if (flag22)
													{
														list.Add(agent2);
													}
												}
											}
										}
									}
								}
								this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1454237960), list.Count));
								foreach (Agent agent3 in list)
								{
									bool flag23 = agent3 != null && agent3.IsActive();
									if (flag23)
									{
										this._processedAgents.Add(agent3);
										this.MakeCivilianFight(agent3, analysis);
									}
								}
							}
							catch (Exception ex2)
							{
								this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1316026523), ex2.Message, ex2);
							}
						});
						delayedTaskManager.AddTask(2.0, delegate
						{
							try
							{
								List<Agent> list = new List<Agent>();
								List<Agent> list2 = new List<Agent>();
								foreach (Agent agent2 in civilians)
								{
									bool flag13 = triggerAgent != null && agent2 == triggerAgent;
									if (!flag13)
									{
										bool flag14 = this._processedAgents.Contains(agent2);
										if (!flag14)
										{
											bool flag15 = this._fightingCivilians.Contains(agent2);
											if (!flag15)
											{
												bool flag16 = !analysis.CivilianPanic;
												if (flag16)
												{
													CharacterObject characterObject2 = agent2.Character as CharacterObject;
													bool flag17 = characterObject2 != null && characterObject2.IsHero && characterObject2.HeroObject != null && characterObject2.HeroObject.IsNotable;
													if (flag17)
													{
														continue;
													}
												}
												CivilianBehavior.CivilianType civilianType2 = this.GetCivilianType(agent2);
												bool flag18 = this.HasUsableWeapon(agent2);
												bool flag19 = civilianType2 == CivilianBehavior.CivilianType.Child;
												if (flag19)
												{
													list2.Add(agent2);
												}
												else
												{
													bool flag20 = civilianType2 == CivilianBehavior.CivilianType.Woman;
													if (flag20)
													{
														bool flag21 = !flag18;
														if (flag21)
														{
															list.Add(agent2);
														}
													}
													else
													{
														bool flag22 = civilianType2 == CivilianBehavior.CivilianType.Man;
														if (flag22)
														{
															list.Add(agent2);
														}
													}
												}
											}
										}
									}
								}
								this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1124944634), list.Count, list2.Count));
								foreach (Agent agent3 in list)
								{
									bool flag23 = agent3 != null && agent3.IsActive();
									if (flag23)
									{
										this._processedAgents.Add(agent3);
										this.MakeCivilianPanic(agent3, analysis);
									}
								}
								foreach (Agent agent4 in list2)
								{
									bool flag24 = agent4 != null && agent4.IsActive();
									if (flag24)
									{
										this._processedAgents.Add(agent4);
										this.MakeCivilianPanicNonKillable(agent4, analysis);
									}
								}
								this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-805158497), this._panickedCivilians.Count, this._fightingCivilians.Count));
								int num3 = this._panickedCivilians.Count + this._fightingCivilians.Count;
								this._logger.LogCivilianPanic(num3, civilians.Count, combat.Settlement.StringId);
								this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1004192886), num3, this._panickedCivilians.Count, this._fightingCivilians.Count));
							}
							catch (Exception ex2)
							{
								this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1122198102), ex2.Message, ex2);
							}
						});
						this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-133587404));
					}
					else
					{
						this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1557642827));
						foreach (Agent agent in civilians)
						{
							bool flag3 = triggerAgent != null && agent == triggerAgent;
							if (!flag3)
							{
								bool flag4 = !analysis.CivilianPanic;
								if (flag4)
								{
									CharacterObject characterObject = agent.Character as CharacterObject;
									bool flag5 = characterObject != null && characterObject.IsHero && characterObject.HeroObject != null && characterObject.HeroObject.IsNotable;
									if (flag5)
									{
										continue;
									}
								}
								this._processedAgents.Add(agent);
								CivilianBehavior.CivilianType civilianType = this.GetCivilianType(agent);
								bool flag6 = this.HasUsableWeapon(agent);
								bool flag7 = civilianType == CivilianBehavior.CivilianType.Child;
								if (flag7)
								{
									this.MakeCivilianPanicNonKillable(agent, analysis);
								}
								else
								{
									bool flag8 = civilianType == CivilianBehavior.CivilianType.Woman;
									if (flag8)
									{
										bool flag9 = flag6;
										if (flag9)
										{
											this.MakeCivilianFight(agent, analysis);
										}
										else
										{
											this.MakeCivilianPanic(agent, analysis);
										}
									}
									else
									{
										bool flag10 = civilianType == CivilianBehavior.CivilianType.Man;
										if (flag10)
										{
											double num = 0.25;
											bool flag11 = this._random.NextDouble() < num;
											bool flag12 = flag11;
											if (flag12)
											{
												this.MakeCivilianFight(agent, analysis);
											}
											else
											{
												this.MakeCivilianPanic(agent, analysis);
											}
										}
									}
								}
							}
						}
						this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1279472626), this._panickedCivilians.Count, this._fightingCivilians.Count));
						int num2 = this._panickedCivilians.Count + this._fightingCivilians.Count;
						this._logger.LogCivilianPanic(num2, civilians.Count, combat.Settlement.StringId);
						this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1004192886), num2, this._panickedCivilians.Count, this._fightingCivilians.Count));
					}
					this.StartPhraseSystem(analysis);
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-2147135580), ex.Message, ex);
			}
		}

		// Token: 0x06000833 RID: 2099 RVA: 0x000A8B68 File Offset: 0x000A6D68
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void ForceAgentPanic(Agent agent)
		{
			try
			{
				bool flag = agent == null || !agent.IsActive();
				if (!flag)
				{
					CombatSituationAnalysis analysis = this._currentAnalysis ?? new CombatSituationAnalysis();
					this.SetCivilianState(agent, CivilianBehavior.CivilianState.Panic, delegate
					{
						this.MakeCivilianPanicInternal(agent, analysis);
					});
					this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-866367009) + agent.Name);
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(435150634), ex.Message, ex);
			}
		}

		// Token: 0x06000834 RID: 2100 RVA: 0x000A8C5C File Offset: 0x000A6E5C
		private List<Agent> FindCivilians(Mission mission, ActiveCombat combat)
		{
			List<Agent> list = new List<Agent>();
			foreach (Agent agent in mission.Agents)
			{
				bool flag = agent == null || !agent.IsActive() || !agent.IsHuman;
				if (!flag)
				{
					CharacterObject characterObject = agent.Character as CharacterObject;
					bool flag2 = characterObject == null;
					if (!flag2)
					{
						bool flag3 = characterObject.Occupation == Occupation.Soldier || characterObject.Occupation == Occupation.Guard || characterObject.Occupation == Occupation.PrisonGuard || characterObject.Occupation == Occupation.CaravanGuard || characterObject.Occupation == Occupation.Mercenary || characterObject.Occupation == Occupation.BannerBearer;
						bool flag4 = characterObject.IsHero && characterObject.HeroObject != null && characterObject.HeroObject.IsLord && this.HasUsableWeapon(agent);
						bool flag5 = flag3 || flag4;
						if (!flag5)
						{
							bool flag6 = agent.Team == mission.PlayerTeam;
							if (flag6)
							{
								PlayerReinforcementMissionLogic missionBehavior = mission.GetMissionBehavior<PlayerReinforcementMissionLogic>();
								bool flag7 = missionBehavior != null && missionBehavior.IsSummonedTroop(agent);
								if (flag7)
								{
									continue;
								}
								bool flag8 = this.IsCompanionOrFollower(agent);
								if (flag8)
								{
									continue;
								}
							}
							bool flag9 = agent.Team == mission.DefenderTeam && agent.GetAgentFlags().HasAnyFlag(AgentFlag.CanAttack);
							if (!flag9)
							{
								bool flag10 = this.IsCompanionOrFollower(agent);
								if (!flag10)
								{
									bool flag11 = this.IsCivilian(characterObject);
									bool flag12 = characterObject.IsHero && characterObject.IsFemale && !this.HasUsableWeapon(agent);
									bool flag13 = flag11 || flag12;
									if (flag13)
									{
										list.Add(agent);
									}
								}
							}
						}
					}
				}
			}
			return list;
		}

		// Token: 0x06000835 RID: 2101 RVA: 0x000A8E40 File Offset: 0x000A7040
		private bool IsCivilian(CharacterObject character)
		{
			bool isHero = character.IsHero;
			if (isHero)
			{
				Hero heroObject = character.HeroObject;
				bool isLord = heroObject.IsLord;
				if (isLord)
				{
					return false;
				}
			}
			Occupation occupation = character.Occupation;
			Occupation occupation2 = occupation;
			if (occupation2 <= Occupation.Soldier)
			{
				if (occupation2 != Occupation.Mercenary && occupation2 != Occupation.Soldier)
				{
					goto IL_81;
				}
			}
			else if (occupation2 != Occupation.Bandit)
			{
				switch (occupation2)
				{
				case Occupation.GangLeader:
				case Occupation.PrisonGuard:
				case Occupation.Guard:
				case Occupation.Gangster:
				case Occupation.BannerBearer:
				case Occupation.CaravanGuard:
					break;
				case Occupation.RuralNotable:
				case Occupation.ShopWorker:
				case Occupation.Musician:
				case Occupation.Blacksmith:
					goto IL_81;
				default:
					goto IL_81;
				}
			}
			return false;
			IL_81:
			return true;
		}

		// Token: 0x06000836 RID: 2102 RVA: 0x000A8ED4 File Offset: 0x000A70D4
		private void MakeCivilianPanic(Agent civilian, CombatSituationAnalysis analysis)
		{
			bool flag = this.IsCompanionOrFollower(civilian);
			if (!flag)
			{
				this.SetCivilianState(civilian, CivilianBehavior.CivilianState.Panic, delegate
				{
					this.MakeCivilianPanicInternal(civilian, analysis);
				});
			}
		}

		// Token: 0x06000837 RID: 2103 RVA: 0x000A8F2C File Offset: 0x000A712C
		private void MakeCivilianPanicNonKillable(Agent civilian, CombatSituationAnalysis analysis)
		{
			bool flag = this.IsCompanionOrFollower(civilian);
			if (!flag)
			{
				this.SetCivilianState(civilian, CivilianBehavior.CivilianState.Panic, delegate
				{
					this.MakeCivilianPanicNonKillableInternal(civilian, analysis);
				});
			}
		}

		// Token: 0x06000838 RID: 2104 RVA: 0x000A8F84 File Offset: 0x000A7184
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void MakeCivilianFight(Agent civilian, CombatSituationAnalysis analysis)
		{
			bool flag = this.IsCompanionOrFollower(civilian);
			if (!flag)
			{
				bool flag2 = this._panickedCivilians.Contains(civilian);
				if (flag2)
				{
					this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-897777464) + civilian.Name + <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1774423431));
				}
				else
				{
					this.SetCivilianState(civilian, CivilianBehavior.CivilianState.Fight, delegate
					{
						this.MakeCivilianFightInternal(civilian, analysis);
					});
				}
			}
		}

		// Token: 0x06000839 RID: 2105 RVA: 0x000A9028 File Offset: 0x000A7228
		[MethodImpl(MethodImplOptions.NoInlining)]
		private bool IsCompanionOrFollower(Agent agent)
		{
			try
			{
				bool flag = ((agent != null) ? agent.Character : null) == null;
				if (flag)
				{
					return false;
				}
				CharacterObject characterObject = agent.Character as CharacterObject;
				bool flag2 = characterObject == null;
				if (flag2)
				{
					return false;
				}
				Hero heroObject = characterObject.HeroObject;
				bool flag3 = heroObject == null;
				if (flag3)
				{
					return false;
				}
				bool flag4 = heroObject.CompanionOf == Clan.PlayerClan || heroObject == Hero.MainHero.Spouse;
				if (flag4)
				{
					return true;
				}
				AIActionManager instance = AIActionManager.Instance;
				bool flag5 = instance != null && instance.IsActionActive(heroObject, <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1157550292));
				if (flag5)
				{
					return true;
				}
				SettlementCombatManager settlementCombatManager = this._behavior.GetSettlementCombatManager();
				CompanionCombatDecision companionCombatDecision;
				bool flag6 = settlementCombatManager != null && settlementCombatManager.TryGetCompanionDecision(heroObject.StringId, out companionCombatDecision);
				if (flag6)
				{
					return true;
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1239265171), ex.Message, ex);
			}
			return false;
		}

		// Token: 0x0600083A RID: 2106 RVA: 0x000A914C File Offset: 0x000A734C
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void StartPhraseSystem(CombatSituationAnalysis analysis)
		{
			try
			{
				bool flag = analysis == null;
				if (!flag)
				{
					bool flag2 = this._initialPhrasesMalePanic.Count == 0 && analysis.CivilianPhrasesMalePanic != null;
					if (flag2)
					{
						this._initialPhrasesMalePanic = new List<string>(analysis.CivilianPhrasesMalePanic);
					}
					bool flag3 = this._initialPhrasesMaleFight.Count == 0 && analysis.CivilianPhrasesMaleFight != null;
					if (flag3)
					{
						this._initialPhrasesMaleFight = new List<string>(analysis.CivilianPhrasesMaleFight);
					}
					bool flag4 = this._initialPhrasesFemale.Count == 0 && analysis.CivilianPhrasesFemale != null;
					if (flag4)
					{
						this._initialPhrasesFemale = new List<string>(analysis.CivilianPhrasesFemale);
					}
					bool flag5 = this._initialPhrasesChild.Count == 0 && analysis.CivilianPhrasesChild != null;
					if (flag5)
					{
						this._initialPhrasesChild = new List<string>(analysis.CivilianPhrasesChild);
					}
					this._phrasesManPanic = new List<string>(this._initialPhrasesMalePanic);
					this._phrasesManFight = new List<string>(this._initialPhrasesMaleFight);
					this._phrasesWoman = new List<string>(this._initialPhrasesFemale);
					this._phrasesChild = new List<string>(this._initialPhrasesChild);
					int num = this._phrasesManPanic.Count + this._phrasesManFight.Count + this._phrasesWoman.Count + this._phrasesChild.Count;
					this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(615442831), num));
					this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-955113015), new object[]
					{
						this._phrasesManPanic.Count,
						this._phrasesManFight.Count,
						this._phrasesWoman.Count,
						this._phrasesChild.Count
					}));
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1544422763), ex.Message, ex);
			}
		}

		// Token: 0x0600083B RID: 2107 RVA: 0x000A9370 File Offset: 0x000A7570
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void GiveSimpleWeaponToCivilian(Agent civilian)
		{
			try
			{
				bool flag = civilian == null || !civilian.IsActive();
				if (!flag)
				{
					EquipmentIndex? equipmentIndex = null;
					for (EquipmentIndex equipmentIndex2 = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex2 < EquipmentIndex.NumAllWeaponSlots; equipmentIndex2++)
					{
						bool isEmpty = civilian.Equipment[equipmentIndex2].IsEmpty;
						if (isEmpty)
						{
							equipmentIndex = new EquipmentIndex?(equipmentIndex2);
							break;
						}
					}
					bool flag2 = equipmentIndex == null;
					if (flag2)
					{
						civilian.Equipment[EquipmentIndex.WeaponItemBeginSlot] = MissionWeapon.Invalid;
						equipmentIndex = new EquipmentIndex?(EquipmentIndex.WeaponItemBeginSlot);
					}
					List<ItemObject> list = new List<ItemObject>();
					MBReadOnlyList<ItemObject> objectTypeList = MBObjectManager.Instance.GetObjectTypeList<ItemObject>();
					foreach (ItemObject itemObject in objectTypeList)
					{
						bool flag3 = itemObject == null;
						if (!flag3)
						{
							WeaponComponent weaponComponent = itemObject.WeaponComponent;
							bool flag4 = weaponComponent == null;
							if (!flag4)
							{
								bool flag5 = itemObject.Tier > ItemObject.ItemTiers.Tier1;
								if (!flag5)
								{
									WeaponComponentData primaryWeapon = weaponComponent.PrimaryWeapon;
									bool flag6 = primaryWeapon == null;
									if (!flag6)
									{
										WeaponClass weaponClass = primaryWeapon.WeaponClass;
										bool flag7 = weaponClass == WeaponClass.Crossbow || weaponClass == WeaponClass.Stone;
										if (!flag7)
										{
											list.Add(itemObject);
										}
									}
								}
							}
						}
					}
					bool flag8 = list.Count > 0;
					if (flag8)
					{
						ItemObject itemObject2 = list[this._random.Next(list.Count)];
						MissionWeapon value = new MissionWeapon(itemObject2, null, null);
						civilian.Equipment[equipmentIndex.Value] = value;
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1874385242), itemObject2.StringId, civilian.Name, equipmentIndex.Value));
					}
					else
					{
						this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1577150048) + civilian.Name);
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1038911128), ex.Message, ex);
			}
		}

		// Token: 0x0600083C RID: 2108 RVA: 0x000A95C8 File Offset: 0x000A77C8
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void WieldWeapon(Agent agent)
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
						this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-246287169), agent.Name, equipmentIndex));
					}
					else
					{
						this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1519198957) + agent.Name + <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1108017169));
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-677152169), ex.Message, ex);
			}
		}

		// Token: 0x0600083D RID: 2109 RVA: 0x000A9758 File Offset: 0x000A7958
		private CivilianBehavior.CivilianType GetCivilianType(Agent civilian)
		{
			CharacterObject characterObject = civilian.Character as CharacterObject;
			bool flag = characterObject == null;
			CivilianBehavior.CivilianType result;
			if (flag)
			{
				result = CivilianBehavior.CivilianType.Man;
			}
			else
			{
				bool flag2 = characterObject.Age < 18f;
				if (flag2)
				{
					result = CivilianBehavior.CivilianType.Child;
				}
				else
				{
					bool isFemale = characterObject.IsFemale;
					if (isFemale)
					{
						result = CivilianBehavior.CivilianType.Woman;
					}
					else
					{
						result = CivilianBehavior.CivilianType.Man;
					}
				}
			}
			return result;
		}

		// Token: 0x0600083E RID: 2110 RVA: 0x000A97A8 File Offset: 0x000A79A8
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void OnTick(float dt)
		{
			try
			{
				bool flag = Mission.Current == null;
				if (!flag)
				{
					this.RestorePanicAnimationForPanickingAgents();
					this.ClearPanicAnimationForFightingAgents();
					this.UpdateFleeingFromAggressors(dt);
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-2036997101), ex.Message, ex);
			}
		}

		// Token: 0x0600083F RID: 2111 RVA: 0x000A9814 File Offset: 0x000A7A14
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void ClearPanicAnimationForFightingAgents()
		{
			try
			{
				foreach (Agent agent in Enumerable.ToList<Agent>(this._fightingCivilians))
				{
					bool flag = agent == null || !agent.IsActive();
					if (!flag)
					{
						CivilianBehavior.CivilianState civilianState;
						bool flag2 = this._civilianStates.TryGetValue(agent, out civilianState) && civilianState == CivilianBehavior.CivilianState.Fight;
						if (flag2)
						{
							bool flag3 = !this.HasUsableWeapon(agent);
							if (flag3)
							{
								this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1790763770) + agent.Name + <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1676699826));
								this._fightingCivilians.Remove(agent);
								AgentFlag agentFlag = agent.GetAgentFlags();
								agentFlag &= ~AgentFlag.CanAttack;
								agentFlag &= ~AgentFlag.CanDefend;
								agentFlag |= AgentFlag.RunsAwayWhenHit;
								agent.SetAgentFlags(agentFlag);
								agent.SetAlarmState(Agent.AIStateFlag.None);
								agent.SetLookAgent(null);
								bool flag4 = this._currentAnalysis != null;
								if (flag4)
								{
									this.MakeCivilianPanic(agent, this._currentAnalysis);
								}
							}
							else
							{
								bool flag5 = !agent.IsAlarmed();
								if (flag5)
								{
									agent.SetAlarmState(Agent.AIStateFlag.Cautious | Agent.AIStateFlag.Alarmed);
								}
								bool flag6 = agent.CurrentWatchState != Agent.WatchState.Alarmed;
								if (flag6)
								{
									agent.SetWatchState(Agent.WatchState.Alarmed);
								}
								AgentFlag agentFlag2 = agent.GetAgentFlags();
								bool flag7 = !agentFlag2.HasAnyFlag(AgentFlag.CanAttack | AgentFlag.CanDefend);
								if (flag7)
								{
									agentFlag2 |= AgentFlag.CanAttack;
									agentFlag2 |= AgentFlag.CanDefend;
									agentFlag2 &= ~AgentFlag.RunsAwayWhenHit;
									agentFlag2 &= ~AgentFlag.CanGetScared;
									agent.SetAgentFlags(agentFlag2);
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(26316425), ex.Message, ex);
			}
		}

		// Token: 0x06000840 RID: 2112 RVA: 0x000A9A18 File Offset: 0x000A7C18
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void RestorePanicAnimationForPanickingAgents()
		{
			try
			{
				foreach (Agent agent in Enumerable.ToList<Agent>(this._panickedCivilians))
				{
					bool flag = agent == null || !agent.IsActive();
					if (!flag)
					{
						bool flag2 = this._fightingCivilians.Contains(agent);
						if (flag2)
						{
							this._panickedCivilians.Remove(agent);
							this._lastFleePositions.Remove(agent);
							agent.SetAlarmState(Agent.AIStateFlag.None);
							agent.SetWatchState(Agent.WatchState.Alarmed);
							agent.SetLookAgent(null);
						}
						else
						{
							CivilianBehavior.CivilianState civilianState;
							bool flag3 = this._civilianStates.TryGetValue(agent, out civilianState) && civilianState == CivilianBehavior.CivilianState.Fight;
							if (flag3)
							{
								this._panickedCivilians.Remove(agent);
								this._lastFleePositions.Remove(agent);
								agent.SetAlarmState(Agent.AIStateFlag.None);
								agent.SetWatchState(Agent.WatchState.Alarmed);
								agent.SetLookAgent(null);
							}
							else
							{
								bool flag4 = !agent.IsAlarmed();
								if (flag4)
								{
									agent.SetAlarmState(Agent.AIStateFlag.Cautious | Agent.AIStateFlag.Alarmed);
									agent.SetWatchState(Agent.WatchState.Alarmed);
								}
								AgentFlag agentFlag = agent.GetAgentFlags();
								bool flag5 = false;
								bool flag6 = agentFlag.HasAnyFlag(AgentFlag.CanAttack | AgentFlag.CanDefend);
								if (flag6)
								{
									agentFlag &= ~AgentFlag.CanAttack;
									agentFlag &= ~AgentFlag.CanDefend;
									agentFlag |= AgentFlag.RunsAwayWhenHit;
									agentFlag &= ~AgentFlag.CanGetScared;
									flag5 = true;
								}
								bool flag7 = !agentFlag.HasAnyFlag(AgentFlag.RunsAwayWhenHit);
								if (flag7)
								{
									agentFlag |= AgentFlag.RunsAwayWhenHit;
									flag5 = true;
								}
								bool flag8 = this.HasUsableWeapon(agent);
								if (flag8)
								{
									this.DropAllWeapons(agent);
								}
								bool flag9 = flag5;
								if (flag9)
								{
									agent.SetAgentFlags(agentFlag);
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1898785664), ex.Message, ex);
			}
		}

		// Token: 0x06000841 RID: 2113 RVA: 0x000A9C18 File Offset: 0x000A7E18
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void UpdateFleeingFromAggressors(float dt)
		{
			try
			{
				bool flag = this._currentAnalysis == null || Mission.Current == null;
				if (!flag)
				{
					List<Agent> aggressorAgents = this.GetAggressorAgents();
					bool flag2 = aggressorAgents.Count == 0;
					if (!flag2)
					{
						foreach (Agent agent in Enumerable.ToList<Agent>(this._panickedCivilians))
						{
							bool flag3 = agent == null || !agent.IsActive();
							if (flag3)
							{
								bool flag4 = agent != null;
								if (flag4)
								{
									this._lastFleePositions.Remove(agent);
								}
							}
							else
							{
								bool flag5 = this._fightingCivilians.Contains(agent);
								if (flag5)
								{
									this._lastFleePositions.Remove(agent);
								}
								else
								{
									CivilianBehavior.CivilianState civilianState;
									bool flag6 = this._civilianStates.TryGetValue(agent, out civilianState) && civilianState == CivilianBehavior.CivilianState.Fight;
									if (flag6)
									{
										this._panickedCivilians.Remove(agent);
										this._lastFleePositions.Remove(agent);
									}
									else
									{
										Agent agent2 = null;
										float num = float.MaxValue;
										foreach (Agent agent3 in aggressorAgents)
										{
											bool flag7 = agent3 == null || !agent3.IsActive();
											if (!flag7)
											{
												float num2 = agent.Position.DistanceSquared(agent3.Position);
												bool flag8 = num2 < num;
												if (flag8)
												{
													num = num2;
													agent2 = agent3;
												}
											}
										}
										float num3 = (agent2 != null) ? MathF.Sqrt(num) : float.MaxValue;
										bool flag9 = agent2 != null && num3 > 42f;
										if (flag9)
										{
											agent.SetLookAgent(agent2);
										}
										else
										{
											bool flag10 = agent2 != null && num <= 2025f;
											if (flag10)
											{
												agent.SetLookAgent(null);
												Vec2 vec = agent.Position.AsVec2 - agent2.Position.AsVec2;
												float num4 = vec.Normalize();
												bool flag11 = num4 < 0.1f;
												if (flag11)
												{
													float x = (float)(this._random.NextDouble() * 3.141592653589793 * 2.0);
													vec = new Vec2(MathF.Cos(x), MathF.Sin(x));
												}
												WorldPosition value = this.FindSafeFleePosition(agent, vec, agent2, 35f);
												bool flag12 = !value.IsValid;
												if (flag12)
												{
													Vec2 vec2 = agent.Position.AsVec2 + vec * 35f;
													Vec3 position = new Vec3(vec2.x, vec2.y, agent.Position.z, -1f);
													WorldPosition worldPosition = new WorldPosition(Mission.Current.Scene, UIntPtr.Zero, position, false);
													Vec3 groundVec = worldPosition.GetGroundVec3();
													value = new WorldPosition(Mission.Current.Scene, UIntPtr.Zero, groundVec, false);
												}
												agent.SetScriptedPosition(ref value, false, Agent.AIScriptedFrameFlags.None);
												this._lastFleePositions[agent] = value;
												agent.SetMaximumSpeedLimit(-1f, false);
											}
											else
											{
												bool flag13 = agent2 != null;
												if (flag13)
												{
													agent.SetLookAgent(null);
													bool flag14 = this._lastFleePositions.ContainsKey(agent);
													if (flag14)
													{
														agent.DisableScriptedMovement();
														this._lastFleePositions.Remove(agent);
													}
												}
												else
												{
													agent.SetLookAgent(null);
													bool flag15 = this._lastFleePositions.ContainsKey(agent);
													if (flag15)
													{
														agent.DisableScriptedMovement();
														this._lastFleePositions.Remove(agent);
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
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1153582230), ex.Message, ex);
			}
		}

		// Token: 0x06000842 RID: 2114 RVA: 0x000AA060 File Offset: 0x000A8260
		[MethodImpl(MethodImplOptions.NoInlining)]
		private WorldPosition FindSafeFleePosition(Agent civilian, Vec2 preferredDirection, Agent aggressor, float fleeDistance)
		{
			WorldPosition invalid;
			try
			{
				bool flag = Mission.Current == null || civilian == null || !civilian.IsActive();
				if (flag)
				{
					invalid = WorldPosition.Invalid;
				}
				else
				{
					Vec2 asVec = civilian.Position.AsVec2;
					Vec3 position = civilian.Position;
					List<Vec2> list = new List<Vec2>();
					list.Add(preferredDirection);
					float num = MathF.Atan2(preferredDirection.y, preferredDirection.x);
					for (int i = -2; i <= 2; i++)
					{
						bool flag2 = i == 0;
						if (!flag2)
						{
							float x = num + (float)i * 3.1415927f / 4f;
							list.Add(new Vec2(MathF.Cos(x), MathF.Sin(x)));
						}
					}
					foreach (Vec2 v in list)
					{
						Vec2 vec = asVec + v * fleeDistance;
						bool flag3 = true;
						for (int j = 1; j <= 5; j++)
						{
							float num2 = (float)j / 5f;
							Vec2 point = asVec + v * (fleeDistance * num2);
							float num3 = 0f;
							Mission.Current.Scene.GetHeightAtPoint(point, (BodyFlags)544321929U, ref num3);
							float num4 = position.z + num2 * (num3 - position.z);
							float num5 = MathF.Abs(num3 - num4);
							bool flag4 = num5 > 2f;
							if (flag4)
							{
								flag3 = false;
								break;
							}
						}
						bool flag5 = !flag3;
						if (!flag5)
						{
							float num6 = 0f;
							Mission.Current.Scene.GetHeightAtPoint(vec, (BodyFlags)544321929U, ref num6);
							float num7 = MathF.Abs(num6 - position.z);
							bool flag6 = num7 > 2f;
							if (!flag6)
							{
								Vec2[] array = new Vec2[]
								{
									new Vec2(vec.x + 15f, vec.y),
									new Vec2(vec.x - 15f, vec.y),
									new Vec2(vec.x, vec.y + 15f),
									new Vec2(vec.x, vec.y - 15f),
									vec
								};
								float num8 = 0f;
								foreach (Vec2 point2 in array)
								{
									float num9 = 0f;
									Mission.Current.Scene.GetHeightAtPoint(point2, (BodyFlags)544321929U, ref num9);
									float num10 = MathF.Abs(num9 - num6);
									bool flag7 = num10 > num8;
									if (flag7)
									{
										num8 = num10;
									}
								}
								bool flag8 = num8 > 2f;
								if (!flag8)
								{
									bool flag9 = aggressor != null && aggressor.IsActive();
									if (flag9)
									{
										Vec2 asVec2 = aggressor.Position.AsVec2;
										float num11 = vec.DistanceSquared(asVec2);
										bool flag10 = num11 < 4f;
										if (flag10)
										{
											continue;
										}
									}
									Vec3 position2 = new Vec3(vec.x, vec.y, num6, -1f);
									WorldPosition result = new WorldPosition(Mission.Current.Scene, UIntPtr.Zero, position2, false);
									return result;
								}
							}
						}
					}
					invalid = WorldPosition.Invalid;
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1697968162), ex.Message, ex);
				invalid = WorldPosition.Invalid;
			}
			return invalid;
		}

		// Token: 0x06000843 RID: 2115 RVA: 0x000AA450 File Offset: 0x000A8650
		[MethodImpl(MethodImplOptions.NoInlining)]
		private List<Agent> GetAggressorAgents()
		{
			List<Agent> list = new List<Agent>();
			try
			{
				bool flag = Mission.Current == null || this._currentAnalysis == null;
				if (flag)
				{
					this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1180044792), Mission.Current != null, this._currentAnalysis != null));
					return list;
				}
				string aggressorId = this._currentAnalysis.AggressorStringId;
				bool flag2 = string.IsNullOrEmpty(aggressorId);
				if (flag2)
				{
					this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1568076598));
					return list;
				}
				bool flag3;
				if (!(aggressorId == <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1636222184)))
				{
					string aggressorId2 = aggressorId;
					Hero mainHero = Hero.MainHero;
					flag3 = (aggressorId2 == ((mainHero != null) ? mainHero.StringId : null));
				}
				else
				{
					flag3 = true;
				}
				bool flag4 = flag3;
				bool flag5 = flag4;
				if (flag5)
				{
					bool flag6 = Agent.Main != null && Agent.Main.IsActive();
					if (flag6)
					{
						list.Add(Agent.Main);
					}
					else
					{
						this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1327205340));
					}
				}
				else
				{
					Agent agent = Enumerable.FirstOrDefault<Agent>(Mission.Current.Agents, (Agent a) => a != null && a.IsActive() && a.Character != null && a.Character.StringId == aggressorId);
					bool flag7 = agent != null;
					if (flag7)
					{
						list.Add(agent);
						this._logger.Log(string.Concat(new string[]
						{
							<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1672495253),
							agent.Name,
							<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1219514938),
							aggressorId,
							<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-52838832)
						}));
					}
					else
					{
						this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(2032566564) + aggressorId + <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1592750713));
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1886427740), ex.Message, ex);
			}
			return list;
		}

		// Token: 0x06000844 RID: 2116 RVA: 0x000AA6A0 File Offset: 0x000A88A0
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void EnsureAgentTeam(Agent civilian)
		{
			try
			{
				bool flag = Mission.Current == null || civilian == null;
				if (!flag)
				{
					bool flag2 = this._civilianTeam == null || this._civilianTeam.Mission != Mission.Current;
					if (flag2)
					{
						Team aggressorTeam = null;
						bool flag3 = this._currentAnalysis != null && !string.IsNullOrEmpty(this._currentAnalysis.AggressorStringId);
						if (flag3)
						{
							string aggressorId = this._currentAnalysis.AggressorStringId;
							bool flag4;
							if (!(aggressorId == <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(699873898)))
							{
								string aggressorId2 = aggressorId;
								Hero mainHero = Hero.MainHero;
								flag4 = (aggressorId2 == ((mainHero != null) ? mainHero.StringId : null));
							}
							else
							{
								flag4 = true;
							}
							bool flag5 = flag4;
							bool flag6 = flag5;
							if (flag6)
							{
								aggressorTeam = Mission.Current.PlayerTeam;
							}
							else
							{
								Agent agent = Enumerable.FirstOrDefault<Agent>(Mission.Current.Agents, (Agent a) => a != null && a.IsActive() && a.Character != null && a.Character.StringId == aggressorId);
								bool flag7 = agent != null;
								if (flag7)
								{
									aggressorTeam = agent.Team;
								}
							}
						}
						bool flag8 = Mission.Current.DefenderTeam != null && Mission.Current.DefenderTeam != aggressorTeam;
						if (flag8)
						{
							this._civilianTeam = Mission.Current.DefenderTeam;
						}
						else
						{
							this._civilianTeam = (Enumerable.FirstOrDefault<Team>(Mission.Current.Teams, (Team t) => t != null && t != aggressorTeam && t.IsValid) ?? Mission.Current.DefenderTeam);
							bool flag9 = this._civilianTeam == aggressorTeam;
							if (flag9)
							{
								this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(483490859));
							}
						}
						bool flag10 = this._civilianTeam == null;
						if (flag10)
						{
							this._civilianTeam = (Mission.Current.DefenderTeam ?? Mission.Current.PlayerTeam);
							this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1329999310));
						}
						bool flag11 = this._civilianTeam != null && aggressorTeam != null;
						if (flag11)
						{
							bool flag12 = !this._civilianTeam.IsEnemyOf(aggressorTeam);
							if (flag12)
							{
								this._civilianTeam.SetIsEnemyOf(aggressorTeam, true);
								aggressorTeam.SetIsEnemyOf(this._civilianTeam, true);
								this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1135782295), this._civilianTeam.Side, aggressorTeam.Side));
							}
						}
					}
					bool flag13 = civilian.Team != this._civilianTeam && this._civilianTeam != null;
					if (flag13)
					{
						civilian.SetTeam(this._civilianTeam, false);
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1070680630), ex.Message, ex);
			}
		}

		// Token: 0x06000845 RID: 2117 RVA: 0x000AA9C4 File Offset: 0x000A8BC4
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void DropAllWeapons(Agent agent)
		{
			try
			{
				for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
				{
					bool flag = !agent.Equipment[equipmentIndex].IsEmpty;
					if (flag)
					{
						agent.DropItem(equipmentIndex, WeaponClass.Undefined);
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(28298682), ex.Message, ex);
			}
		}

		// Token: 0x06000846 RID: 2118 RVA: 0x0001CA2B File Offset: 0x0001AC2B
		public void Cleanup()
		{
			this._panickedCivilians.Clear();
			this._fightingCivilians.Clear();
			this._phraseTimers.Clear();
			this._civilianStates.Clear();
			this._lastFleePositions.Clear();
		}

		// Token: 0x06000847 RID: 2119 RVA: 0x000AAA48 File Offset: 0x000A8C48
		public bool IsAgentUnderCivilianControl(Agent agent)
		{
			bool flag = agent == null;
			return !flag && this._civilianStates.ContainsKey(agent);
		}

		// Token: 0x06000848 RID: 2120 RVA: 0x000AAA74 File Offset: 0x000A8C74
		public bool IsAgentPanicking(Agent agent)
		{
			bool flag = agent == null;
			CivilianBehavior.CivilianState civilianState;
			return !flag && this._civilianStates.TryGetValue(agent, out civilianState) && civilianState == CivilianBehavior.CivilianState.Panic;
		}

		// Token: 0x06000849 RID: 2121 RVA: 0x000AAAAC File Offset: 0x000A8CAC
		public bool IsAgentFighting(Agent agent)
		{
			bool flag = agent == null;
			CivilianBehavior.CivilianState civilianState;
			return !flag && this._civilianStates.TryGetValue(agent, out civilianState) && civilianState == CivilianBehavior.CivilianState.Fight;
		}

		// Token: 0x0600084A RID: 2122 RVA: 0x000AAAE4 File Offset: 0x000A8CE4
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void SetCivilianState(Agent civilian, CivilianBehavior.CivilianState state, Action applyState)
		{
			bool flag = civilian == null || !civilian.IsActive();
			if (!flag)
			{
				bool flag2 = this._panickedCivilians.Contains(civilian);
				bool flag3 = this._fightingCivilians.Contains(civilian);
				CivilianBehavior.CivilianState civilianState;
				bool flag4 = this._civilianStates.TryGetValue(civilian, out civilianState);
				bool flag5 = flag2 && flag3;
				if (flag5)
				{
					this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1481857239) + civilian.Name + <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1241247428));
					this._panickedCivilians.Remove(civilian);
					this._fightingCivilians.Remove(civilian);
					this._lastFleePositions.Remove(civilian);
				}
				bool flag6 = flag4;
				if (flag6)
				{
					bool flag7 = civilianState == state;
					if (flag7)
					{
						applyState();
						return;
					}
					bool flag8 = civilianState == CivilianBehavior.CivilianState.Panic;
					if (flag8)
					{
						this._panickedCivilians.Remove(civilian);
						this._lastFleePositions.Remove(civilian);
						this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1049604569), civilian.Name, state));
					}
					else
					{
						bool flag9 = civilianState == CivilianBehavior.CivilianState.Fight;
						if (flag9)
						{
							this._fightingCivilians.Remove(civilian);
							this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(487173690), civilian.Name, state));
						}
					}
				}
				bool flag10 = state == CivilianBehavior.CivilianState.Fight;
				if (flag10)
				{
					bool flag11 = this._panickedCivilians.Contains(civilian);
					if (flag11)
					{
						this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1716259166) + civilian.Name + <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1037338365));
						this._panickedCivilians.Remove(civilian);
						this._lastFleePositions.Remove(civilian);
					}
				}
				else
				{
					bool flag12 = state == CivilianBehavior.CivilianState.Panic;
					if (flag12)
					{
						bool flag13 = this._fightingCivilians.Contains(civilian);
						if (flag13)
						{
							this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1449080839) + civilian.Name + <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(790026348));
							this._fightingCivilians.Remove(civilian);
						}
					}
				}
				applyState();
				this._civilianStates[civilian] = state;
			}
		}

		// Token: 0x0600084B RID: 2123 RVA: 0x000AAD28 File Offset: 0x000A8F28
		private void ApplyCustomPanic(Agent civilian)
		{
			bool flag = civilian == null || !civilian.IsActive();
			if (!flag)
			{
				civilian.SetAlarmState(Agent.AIStateFlag.Cautious | Agent.AIStateFlag.Alarmed);
				civilian.SetWatchState(Agent.WatchState.Alarmed);
			}
		}

		// Token: 0x0600084C RID: 2124 RVA: 0x000AAD5C File Offset: 0x000A8F5C
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void MakeCivilianPanicInternal(Agent civilian, CombatSituationAnalysis analysis)
		{
			try
			{
				bool flag = Mission.Current == null || civilian == null || !civilian.IsActive();
				if (!flag)
				{
					bool flag2 = this._fightingCivilians.Contains(civilian);
					if (flag2)
					{
						CivilianBehavior.CivilianState civilianState;
						this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-2100535460) + civilian.Name + <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1389980637) + (this._civilianStates.TryGetValue(civilian, out civilianState) ? civilianState.ToString() : <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1125403524)));
					}
					else
					{
						CivilianBehavior.CivilianState civilianState2;
						bool flag3 = this._civilianStates.TryGetValue(civilian, out civilianState2) && civilianState2 == CivilianBehavior.CivilianState.Fight;
						if (flag3)
						{
							this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1385338984) + civilian.Name + <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-151671829));
						}
						else
						{
							this._fightingCivilians.Remove(civilian);
							bool flag4 = !this._panickedCivilians.Contains(civilian);
							if (flag4)
							{
								this._panickedCivilians.Add(civilian);
							}
							else
							{
								this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1477470105) + civilian.Name + <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1402980692));
							}
							this.EnsureAgentTeam(civilian);
							civilian.SetIsAIPaused(false);
							AgentFlag agentFlag = civilian.GetAgentFlags();
							agentFlag &= ~AgentFlag.CanAttack;
							agentFlag &= ~AgentFlag.CanDefend;
							agentFlag |= AgentFlag.RunsAwayWhenHit;
							agentFlag &= ~AgentFlag.CanGetScared;
							civilian.SetAgentFlags(agentFlag);
							AgentFlag agentFlags = civilian.GetAgentFlags();
							bool flag5 = agentFlags.HasAnyFlag(AgentFlag.CanAttack | AgentFlag.CanDefend);
							if (flag5)
							{
								this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1031739001), civilian.Name, agentFlags));
							}
							civilian.SetLookAgent(null);
							this.DropAllWeapons(civilian);
							this.ApplyCustomPanic(civilian);
							this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1117269336) + civilian.Name);
							this._phraseTimers[civilian] = (float)(this._random.NextDouble() * 5.0);
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1417696711), ex.Message, ex);
			}
		}

		// Token: 0x0600084D RID: 2125 RVA: 0x000AAFCC File Offset: 0x000A91CC
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void MakeCivilianPanicNonKillableInternal(Agent civilian, CombatSituationAnalysis analysis)
		{
			try
			{
				bool flag = Mission.Current == null || civilian == null || !civilian.IsActive();
				if (!flag)
				{
					bool flag2 = this._fightingCivilians.Contains(civilian);
					if (flag2)
					{
						this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-25720193) + civilian.Name + <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(512501512));
					}
					else
					{
						this._fightingCivilians.Remove(civilian);
						this._panickedCivilians.Add(civilian);
						this.EnsureAgentTeam(civilian);
						civilian.SetIsAIPaused(false);
						AgentFlag agentFlag = civilian.GetAgentFlags();
						agentFlag &= ~AgentFlag.CanAttack;
						agentFlag &= ~AgentFlag.CanDefend;
						agentFlag |= AgentFlag.RunsAwayWhenHit;
						civilian.SetAgentFlags(agentFlag);
						civilian.SetLookAgent(null);
						this.DropAllWeapons(civilian);
						this.ApplyCustomPanic(civilian);
						this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1433853681) + civilian.Name + <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1241798069));
						this._phraseTimers[civilian] = (float)(this._random.NextDouble() * 5.0);
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-61003609), ex.Message, ex);
			}
		}

		// Token: 0x0600084E RID: 2126 RVA: 0x000AB134 File Offset: 0x000A9334
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void MakeCivilianFightInternal(Agent civilian, CombatSituationAnalysis analysis)
		{
			try
			{
				bool flag = Mission.Current == null || civilian == null || !civilian.IsActive();
				if (!flag)
				{
					bool flag2 = this._panickedCivilians.Contains(civilian);
					if (flag2)
					{
						this._panickedCivilians.Remove(civilian);
						this._lastFleePositions.Remove(civilian);
						civilian.SetAlarmState(Agent.AIStateFlag.None);
						civilian.SetWatchState(Agent.WatchState.Cautious);
						civilian.SetLookAgent(null);
						this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1570758591) + civilian.Name + <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1347129879));
					}
					bool flag3 = !this._fightingCivilians.Contains(civilian);
					if (flag3)
					{
						this._fightingCivilians.Add(civilian);
					}
					else
					{
						this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1477470105) + civilian.Name + <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1006676117));
					}
					civilian.SetTeam(Mission.Current.DefenderTeam, false);
					civilian.SetMaximumSpeedLimit(-1f, false);
					civilian.SetWatchState(Agent.WatchState.Alarmed);
					civilian.SetAlarmState(Agent.AIStateFlag.Cautious | Agent.AIStateFlag.Alarmed);
					bool flag4 = !this.HasUsableWeapon(civilian);
					if (flag4)
					{
						this.GiveSimpleWeaponToCivilian(civilian);
					}
					this.WieldWeapon(civilian);
					bool flag5 = !this.HasUsableWeapon(civilian);
					if (flag5)
					{
						this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1570758591) + civilian.Name + <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1940231526));
						this._fightingCivilians.Remove(civilian);
						AgentFlag agentFlag = civilian.GetAgentFlags();
						agentFlag &= ~AgentFlag.CanAttack;
						agentFlag &= ~AgentFlag.CanDefend;
						agentFlag |= AgentFlag.RunsAwayWhenHit;
						civilian.SetAgentFlags(agentFlag);
						civilian.SetAlarmState(Agent.AIStateFlag.None);
						civilian.SetLookAgent(null);
						this.MakeCivilianPanic(civilian, analysis);
					}
					else
					{
						this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-301972516) + civilian.Name + <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-162443430));
						this._phraseTimers[civilian] = (float)(this._random.NextDouble() * 5.0);
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-128017994), ex.Message, ex);
			}
		}

		// Token: 0x0600084F RID: 2127 RVA: 0x000AB3A0 File Offset: 0x000A95A0
		private bool HasUsableWeapon(Agent agent)
		{
			bool result;
			try
			{
				bool flag = agent == null;
				if (flag)
				{
					result = false;
				}
				else
				{
					for (EquipmentIndex equipmentIndex = EquipmentIndex.WeaponItemBeginSlot; equipmentIndex < EquipmentIndex.NumAllWeaponSlots; equipmentIndex++)
					{
						bool flag2 = !agent.Equipment[equipmentIndex].IsEmpty && agent.Equipment[equipmentIndex].Item != null;
						if (flag2)
						{
							return true;
						}
					}
					result = false;
				}
			}
			catch
			{
				result = false;
			}
			return result;
		}

		// Token: 0x040004FF RID: 1279
		private readonly AIInfluenceBehavior _behavior;

		// Token: 0x04000500 RID: 1280
		private readonly SettlementCombatLogger _logger;

		// Token: 0x04000501 RID: 1281
		private readonly Random _random = new Random();

		// Token: 0x04000502 RID: 1282
		private readonly HashSet<Agent> _panickedCivilians = new HashSet<Agent>();

		// Token: 0x04000503 RID: 1283
		private readonly HashSet<Agent> _fightingCivilians = new HashSet<Agent>();

		// Token: 0x04000504 RID: 1284
		private Dictionary<Agent, float> _phraseTimers = new Dictionary<Agent, float>();

		// Token: 0x04000505 RID: 1285
		private readonly Dictionary<Agent, CivilianBehavior.CivilianState> _civilianStates = new Dictionary<Agent, CivilianBehavior.CivilianState>();

		// Token: 0x04000506 RID: 1286
		private Team _civilianTeam;

		// Token: 0x04000507 RID: 1287
		private List<string> _phrasesManPanic = new List<string>();

		// Token: 0x04000508 RID: 1288
		private List<string> _phrasesManFight = new List<string>();

		// Token: 0x04000509 RID: 1289
		private List<string> _phrasesWoman = new List<string>();

		// Token: 0x0400050A RID: 1290
		private List<string> _phrasesChild = new List<string>();

		// Token: 0x0400050B RID: 1291
		private List<string> _initialPhrasesMalePanic = new List<string>();

		// Token: 0x0400050C RID: 1292
		private List<string> _initialPhrasesMaleFight = new List<string>();

		// Token: 0x0400050D RID: 1293
		private List<string> _initialPhrasesFemale = new List<string>();

		// Token: 0x0400050E RID: 1294
		private List<string> _initialPhrasesChild = new List<string>();

		// Token: 0x0400050F RID: 1295
		private ActiveCombat _currentCombat;

		// Token: 0x04000510 RID: 1296
		private CombatSituationAnalysis _currentAnalysis;

		// Token: 0x04000511 RID: 1297
		private HashSet<Agent> _processedAgents = new HashSet<Agent>();

		// Token: 0x04000512 RID: 1298
		private Dictionary<Agent, WorldPosition> _lastFleePositions = new Dictionary<Agent, WorldPosition>();

		// Token: 0x020000FD RID: 253
		private enum CivilianType
		{
			// Token: 0x04000514 RID: 1300
			Man,
			// Token: 0x04000515 RID: 1301
			Woman,
			// Token: 0x04000516 RID: 1302
			Child
		}

		// Token: 0x020000FE RID: 254
		private enum CivilianState
		{
			// Token: 0x04000518 RID: 1304
			Panic,
			// Token: 0x04000519 RID: 1305
			Fight
		}
	}
}
