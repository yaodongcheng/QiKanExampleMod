using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x0200011E RID: 286
	public class DefenderSpawner
	{
		// Token: 0x06000955 RID: 2389 RVA: 0x000B19F8 File Offset: 0x000AFBF8
		public DefenderSpawner(AIInfluenceBehavior behavior)
		{
			this._behavior = behavior;
			this._logger = SettlementCombatLogger.Instance;
		}

		// Token: 0x06000956 RID: 2390 RVA: 0x000B1AC4 File Offset: 0x000AFCC4
		private int GetMaxTroopsPerSide()
		{
			bool flag = this._currentCombat == null;
			int result;
			if (flag)
			{
				result = 75;
			}
			else
			{
				result = ((this._currentCombat.LocationType == LocationType.SmallIndoor) ? 5 : 75);
			}
			return result;
		}

		// Token: 0x06000957 RID: 2391 RVA: 0x0001D379 File Offset: 0x0001B579
		public void SetStatistics(CombatStatistics statistics)
		{
			this._statistics = statistics;
		}

		// Token: 0x06000958 RID: 2392 RVA: 0x000B1AFC File Offset: 0x000AFCFC
		public void OnTick(float dt)
		{
			bool flag = this._currentCombat == null || Mission.Current == null;
			if (!flag)
			{
				this._lastReinforcementCheck += dt;
				bool flag2 = this._lastReinforcementCheck >= 60f;
				if (flag2)
				{
					this._lastReinforcementCheck = 0f;
					this.CheckAndSpawnReinforcements();
				}
			}
		}

		// Token: 0x06000959 RID: 2393 RVA: 0x000B1B5C File Offset: 0x000AFD5C
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void SpawnGuardsForTransition(ActiveCombat combat, int count)
		{
			try
			{
				bool flag = Mission.Current == null || combat == null;
				if (!flag)
				{
					bool flag2 = combat.Analysis != null && !combat.Analysis.NeedsDefenders;
					if (flag2)
					{
						this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1278022222));
					}
					else
					{
						this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1134115387), count));
						this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-556197087), combat.LocationType));
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-353668995), this.GetMaxTroopsPerSide()));
						this._currentCombat = combat;
						Settlement settlement = combat.Settlement;
						List<CharacterObject> guardTemplates = this.GetGuardTemplates(settlement, 5);
						bool flag3 = guardTemplates == null || !Enumerable.Any<CharacterObject>(guardTemplates);
						if (flag3)
						{
							this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1053427156));
						}
						else
						{
							int num = 0;
							for (int i = 0; i < count; i++)
							{
								CharacterObject characterObject = guardTemplates[i % guardTemplates.Count];
								int num2 = this.CountActiveTroopsOnSide(false);
								int maxTroopsPerSide = this.GetMaxTroopsPerSide();
								this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1464473838), new object[]
								{
									i + 1,
									count,
									num2,
									maxTroopsPerSide
								}));
								bool flag4 = num2 >= maxTroopsPerSide;
								if (flag4)
								{
									this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-893569682));
									this._enemySideReserve.Enqueue(characterObject);
								}
								else
								{
									this.SpawnDefenderAgent(characterObject, combat, <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(2052027506), false, false);
									num++;
								}
							}
							this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1885294439), num, count));
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1902936147), ex.Message, ex);
			}
		}

		// Token: 0x0600095A RID: 2394 RVA: 0x000B1DD0 File Offset: 0x000AFFD0
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void AddToPlayerReserve(CharacterObject character)
		{
			try
			{
				bool flag = character == null;
				if (!flag)
				{
					this._playerSideReserve.Enqueue(character);
					this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1916167428), character.Name, this._playerSideReserve.Count));
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(654257070), ex.Message, ex);
			}
		}

		// Token: 0x0600095B RID: 2395 RVA: 0x000B1E60 File Offset: 0x000B0060
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void ClearCombat()
		{
			this._currentCombat = null;
			this._playerSideReserve.Clear();
			this._enemySideReserve.Clear();
			this._spawnedPlayerAgents.Clear();
			this._spawnedEnemyAgents.Clear();
			this._lastReinforcementCheck = 0f;
			this._cachedCityGuardSpawnPoints.Clear();
			this._cachedMissionForCitySpawnPoints = null;
			this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(908920402));
		}

		// Token: 0x0600095C RID: 2396 RVA: 0x000B1EDC File Offset: 0x000B00DC
		private void RegisterSpawnedAgent(Agent agent, bool onPlayerSide)
		{
			bool flag = agent == null;
			if (!flag)
			{
				HashSet<int> hashSet = onPlayerSide ? this._spawnedPlayerAgents : this._spawnedEnemyAgents;
				hashSet.Add(agent.Index);
			}
		}

		// Token: 0x0600095D RID: 2397 RVA: 0x000B1F14 File Offset: 0x000B0114
		private float GetRandomDelay(float minSeconds, float maxSeconds)
		{
			bool flag = maxSeconds <= minSeconds;
			float result;
			if (flag)
			{
				result = minSeconds;
			}
			else
			{
				double num = (double)(maxSeconds - minSeconds);
				result = minSeconds + (float)(this._random.NextDouble() * num);
			}
			return result;
		}

		// Token: 0x0600095E RID: 2398 RVA: 0x000B1F4C File Offset: 0x000B014C
		public bool HasPendingSpawns()
		{
			bool flag = this._pendingSimpleDefenders > 0 || this._pendingMilitia > 0 || this._pendingGuards > 0 || this._pendingLords > 0;
			bool flag2 = this._enemySideReserve.Count > 0;
			return flag || flag2;
		}

		// Token: 0x0600095F RID: 2399 RVA: 0x000B1F98 File Offset: 0x000B0198
		[MethodImpl(MethodImplOptions.NoInlining)]
		public string GetPendingSpawnsInfo()
		{
			return string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(900099548), new object[]
			{
				this._pendingSimpleDefenders,
				this._pendingMilitia,
				this._pendingGuards,
				this._pendingLords,
				this._enemySideReserve.Count
			});
		}

		// Token: 0x06000960 RID: 2400 RVA: 0x000B200C File Offset: 0x000B020C
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void SpawnDefenders(ActiveCombat combat)
		{
			try
			{
				this._currentCombat = combat;
				CombatSituationAnalysis analysis = combat.Analysis;
				int desiredSimpleDefenders = 0;
				int num = 0;
				int desiredGuards = 0;
				this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1294168763), combat.LocationType));
				this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-364774042), combat.Settlement.IsTown, combat.Settlement.IsCastle, combat.Settlement.IsVillage));
				bool flag = combat.LocationType == LocationType.SmallIndoor;
				bool isVillage = combat.Settlement.IsVillage;
				bool flag2 = combat.Settlement.IsTown || combat.Settlement.IsCastle;
				bool flag3 = flag;
				if (flag3)
				{
					this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1827688535));
				}
				else
				{
					bool flag4 = isVillage;
					if (flag4)
					{
						this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-613884188));
					}
					else
					{
						bool flag5 = flag2;
						if (flag5)
						{
							this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1688812809));
						}
					}
				}
				bool flag6 = isVillage && !flag;
				if (flag6)
				{
					int num2 = this.CalculateVillageDefenderCount(combat.Settlement);
					desiredSimpleDefenders = (int)Math.Round((double)((float)num2 * 0.6f), MidpointRounding.AwayFromZero);
					desiredSimpleDefenders = Math.Max(0, desiredSimpleDefenders);
					num = Math.Max(0, num2 - desiredSimpleDefenders);
					this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-657988458), num2, desiredSimpleDefenders, num));
				}
				else
				{
					bool flag7 = (flag2 || flag) && combat.Settlement != null;
					if (flag7)
					{
						desiredGuards = this.CalculateCityGuardCount(combat.Settlement);
						this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1933758989), desiredGuards));
					}
				}
				SettlementCombatLogger logger = this._logger;
				string format = <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1543319516);
				object[] array = new object[4];
				array[0] = desiredSimpleDefenders;
				array[1] = num;
				array[2] = desiredGuards;
				int num3 = 3;
				int? num4;
				if (analysis == null)
				{
					num4 = null;
				}
				else
				{
					List<LordIntervention> lords = analysis.Lords;
					num4 = ((lords != null) ? new int?(lords.Count) : null);
				}
				int? num5 = num4;
				array[num3] = num5.GetValueOrDefault();
				logger.Log(string.Format(format, array));
				bool flag8 = desiredSimpleDefenders > 0 && isVillage && !flag;
				if (flag8)
				{
					float simpleDefendersDelay = this.GetRandomDelay(30f, 60f);
					this._pendingSimpleDefenders = desiredSimpleDefenders;
					this._simpleDefendersScheduled = true;
					this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1574748865), desiredSimpleDefenders, simpleDefendersDelay));
					this._behavior.GetDelayedTaskManager().AddTask((double)simpleDefendersDelay, delegate
					{
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-937841236), simpleDefendersDelay));
						this.SpawnSimpleDefenders(combat, desiredSimpleDefenders);
						this._simpleDefendersScheduled = false;
					});
				}
				else
				{
					bool flag9 = desiredSimpleDefenders > 0;
					if (flag9)
					{
						this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1751736176), desiredSimpleDefenders));
					}
				}
				bool flag10 = num > 0 && isVillage && !flag;
				if (flag10)
				{
					MilitiaPartyComponent militiaPartyComponent = combat.Settlement.MilitiaPartyComponent;
					int? num6;
					if (militiaPartyComponent == null)
					{
						num6 = null;
					}
					else
					{
						MobileParty mobileParty = militiaPartyComponent.MobileParty;
						if (mobileParty == null)
						{
							num6 = null;
						}
						else
						{
							TroopRoster memberRoster = mobileParty.MemberRoster;
							num6 = ((memberRoster != null) ? new int?(memberRoster.TotalManCount) : null);
						}
					}
					num5 = num6;
					int valueOrDefault = num5.GetValueOrDefault();
					int actualMilitiaCount = Math.Min(num, valueOrDefault);
					bool flag11 = actualMilitiaCount != num;
					if (flag11)
					{
						this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1701010872), num, valueOrDefault, actualMilitiaCount));
					}
					bool flag12 = actualMilitiaCount > 0;
					if (flag12)
					{
						float militiaDelay = this.GetRandomDelay(90f, 150f);
						this._pendingMilitia = actualMilitiaCount;
						this._militiaScheduled = true;
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(2025965708), actualMilitiaCount, militiaDelay));
						this._behavior.GetDelayedTaskManager().AddTask((double)militiaDelay, delegate
						{
							this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1969228350), militiaDelay));
							this.SpawnMilitia(combat, actualMilitiaCount);
							this._militiaScheduled = false;
						});
					}
				}
				else
				{
					bool flag13 = num > 0;
					if (flag13)
					{
						this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1371422772), num));
					}
				}
				bool flag14 = flag || flag2;
				bool flag15 = desiredGuards > 0 && flag14;
				if (flag15)
				{
					float randomDelay = this.GetRandomDelay(80f, 150f);
					this._pendingGuards = desiredGuards;
					this._guardsScheduled = true;
					this._guardsSpawnScheduledTime = new CampaignTime?(CampaignTime.Now);
					this._guardsSpawnDelay = randomDelay;
					bool flag16 = flag;
					if (flag16)
					{
						this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1980912876), desiredGuards, 5));
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1942167595), randomDelay));
						this._behavior.GetDelayedTaskManager().AddTask((double)randomDelay, delegate
						{
							int num7 = Math.Min(5, this._pendingGuards);
							this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(218685517), num7));
							this.SpawnGuards(combat, num7);
							this._pendingGuards -= num7;
							this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1935761291), this._pendingGuards));
							this._guardsScheduled = (this._pendingGuards > 0);
							bool flag19 = !this._guardsScheduled;
							if (flag19)
							{
								this._guardsSpawnScheduledTime = null;
								this._guardsSpawnDelay = 0f;
							}
						});
					}
					else
					{
						this._behavior.GetDelayedTaskManager().AddTask((double)randomDelay, delegate
						{
							this.SpawnGuards(combat, desiredGuards);
							this._guardsScheduled = false;
							this._guardsSpawnScheduledTime = null;
						});
						this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1237567224), desiredGuards, randomDelay));
					}
				}
				else
				{
					bool flag17 = desiredGuards > 0 && !flag14;
					if (flag17)
					{
						this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1285759617), desiredGuards));
					}
				}
				bool flag18 = ((analysis != null) ? analysis.Lords : null) != null && Enumerable.Any<LordIntervention>(analysis.Lords);
				if (flag18)
				{
					this._pendingLords = analysis.Lords.Count;
					this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1078769209), this._pendingLords));
					this.SpawnLordsWithCalculatedDelay(combat, analysis.Lords);
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1849548628), ex.Message, ex);
			}
		}

		// Token: 0x06000961 RID: 2401 RVA: 0x000B2860 File Offset: 0x000B0A60
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void SpawnSimpleDefenders(ActiveCombat combat, int count)
		{
			try
			{
				this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1619606255), count));
				bool flag = Mission.Current == null;
				if (flag)
				{
					this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-183501807));
				}
				else
				{
					Settlement settlement = combat.Settlement;
					SettlementCombatLogger logger = this._logger;
					string format = <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-338354524);
					object name = settlement.Name;
					IFaction mapFaction = settlement.MapFaction;
					string text;
					if (mapFaction == null)
					{
						text = null;
					}
					else
					{
						CultureObject culture = mapFaction.Culture;
						if (culture == null)
						{
							text = null;
						}
						else
						{
							TextObject name2 = culture.Name;
							text = ((name2 != null) ? name2.ToString() : null);
						}
					}
					string arg;
					if ((arg = text) == null)
					{
						CultureObject culture2 = settlement.Culture;
						string text2;
						if (culture2 == null)
						{
							text2 = null;
						}
						else
						{
							TextObject name3 = culture2.Name;
							text2 = ((name3 != null) ? name3.ToString() : null);
						}
						arg = (text2 ?? <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1617165097));
					}
					logger.Log(string.Format(format, name, arg));
					List<CharacterObject> simpleDefenderTemplates = this.GetSimpleDefenderTemplates(settlement, 5);
					bool flag2 = simpleDefenderTemplates == null || !Enumerable.Any<CharacterObject>(simpleDefenderTemplates);
					if (flag2)
					{
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1778981796), settlement.Name));
					}
					else
					{
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(322333611), simpleDefenderTemplates.Count));
						for (int i = 0; i < count; i++)
						{
							CharacterObject character = simpleDefenderTemplates[i % simpleDefenderTemplates.Count];
							this.TrySpawnDefenderWithLimit(character, combat, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1730467099), false, true);
						}
						this._logger.LogDefendersSpawned(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1730442883), count, settlement.StringId);
						this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-212365439), count, simpleDefenderTemplates.Count));
						this._pendingSimpleDefenders = Math.Max(0, this._pendingSimpleDefenders - count);
						TextObject textObject = new TextObject(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(819080475), null);
						MBInformationManager.AddQuickInformation(textObject, 3000, simpleDefenderTemplates[0], null, "");
						bool flag3 = this._statistics != null;
						if (flag3)
						{
							this._statistics.RecordSimpleDefendersArrival(count);
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1460422071), ex.Message, ex);
			}
		}

		// Token: 0x06000962 RID: 2402 RVA: 0x000B2AE4 File Offset: 0x000B0CE4
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void SpawnMilitia(ActiveCombat combat, int count)
		{
			try
			{
				this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(723223490), count));
				bool flag = Mission.Current == null;
				if (flag)
				{
					this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1240164822));
				}
				else
				{
					Settlement settlement = combat.Settlement;
					SettlementCombatLogger logger = this._logger;
					string format = <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(266967714);
					object name = settlement.Name;
					IFaction mapFaction = settlement.MapFaction;
					string text;
					if (mapFaction == null)
					{
						text = null;
					}
					else
					{
						CultureObject culture = mapFaction.Culture;
						if (culture == null)
						{
							text = null;
						}
						else
						{
							TextObject name2 = culture.Name;
							text = ((name2 != null) ? name2.ToString() : null);
						}
					}
					string arg;
					if ((arg = text) == null)
					{
						CultureObject culture2 = settlement.Culture;
						string text2;
						if (culture2 == null)
						{
							text2 = null;
						}
						else
						{
							TextObject name3 = culture2.Name;
							text2 = ((name3 != null) ? name3.ToString() : null);
						}
						arg = (text2 ?? <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1617165097));
					}
					logger.Log(string.Format(format, name, arg));
					List<CharacterObject> militiaTemplates = this.GetMilitiaTemplates(settlement, 5);
					bool flag2 = militiaTemplates == null || !Enumerable.Any<CharacterObject>(militiaTemplates);
					if (flag2)
					{
						this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-607664849), settlement.Name));
					}
					else
					{
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(159147812), militiaTemplates.Count));
						for (int i = 0; i < count; i++)
						{
							CharacterObject character = militiaTemplates[i % militiaTemplates.Count];
							this.TrySpawnDefenderWithLimit(character, combat, <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-2010607329), false, false);
						}
						this._logger.LogDefendersSpawned(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(132685250), count, settlement.StringId);
						this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-856411718), count, militiaTemplates.Count));
						this._pendingMilitia = Math.Max(0, this._pendingMilitia - count);
						TextObject textObject = new TextObject(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1337194216), null);
						MBInformationManager.AddQuickInformation(textObject, 3000, militiaTemplates[0], null, "");
						bool flag3 = this._statistics != null;
						if (flag3)
						{
							this._statistics.RecordMilitiaArrival(count);
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1502512938), ex.Message, ex);
			}
		}

		// Token: 0x06000963 RID: 2403 RVA: 0x000B2D68 File Offset: 0x000B0F68
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void SpawnGuards(ActiveCombat combat, int count)
		{
			try
			{
				bool flag = Mission.Current == null;
				if (flag)
				{
					this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1810661472));
				}
				else
				{
					Settlement settlement = combat.Settlement;
					List<CharacterObject> guardTemplates = this.GetGuardTemplates(settlement, 5);
					bool flag2 = guardTemplates == null || !Enumerable.Any<CharacterObject>(guardTemplates);
					if (flag2)
					{
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(48887137), settlement.Name));
					}
					else
					{
						this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(2118259465), guardTemplates.Count));
						for (int i = 0; i < count; i++)
						{
							CharacterObject character = guardTemplates[i % guardTemplates.Count];
							this.TrySpawnDefenderWithLimit(character, combat, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(13603721), false, false);
						}
						this._logger.LogDefendersSpawned(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(18014148), count, settlement.StringId);
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(22424575), count, guardTemplates.Count));
						TextObject textObject = new TextObject(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-540930249), null);
						MBInformationManager.AddQuickInformation(textObject, 3000, guardTemplates[0], null, "");
						bool flag3 = this._statistics != null;
						if (flag3)
						{
							this._statistics.RecordGuardsArrival(count);
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(162894598), ex.Message, ex);
			}
		}

		// Token: 0x06000964 RID: 2404 RVA: 0x000B2F38 File Offset: 0x000B1138
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void SpawnLordsWithCalculatedDelay(ActiveCombat combat, List<LordIntervention> lords)
		{
			DefenderSpawner.<>c__DisplayClass44_0 CS$<>8__locals1 = new DefenderSpawner.<>c__DisplayClass44_0();
			CS$<>8__locals1.<>4__this = this;
			CS$<>8__locals1.combat = combat;
			try
			{
				Settlement settlement = CS$<>8__locals1.combat.Settlement;
				using (List<LordIntervention>.Enumerator enumerator = lords.GetEnumerator())
				{
					while (enumerator.MoveNext())
					{
						DefenderSpawner.<>c__DisplayClass44_1 CS$<>8__locals2 = new DefenderSpawner.<>c__DisplayClass44_1();
						CS$<>8__locals2.CS$<>8__locals1 = CS$<>8__locals1;
						CS$<>8__locals2.lordInfo = enumerator.Current;
						Hero lord = Hero.FindFirst((Hero h) => h != null && h.StringId == CS$<>8__locals2.lordInfo.StringId);
						bool flag = lord == null || lord.IsDead || lord.PartyBelongedTo == null;
						if (flag)
						{
							this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(407840778) + CS$<>8__locals2.lordInfo.StringId + <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(404728552));
						}
						else
						{
							float num = lord.PartyBelongedTo.Position.Distance(settlement.Position);
							bool flag2 = num > 12f;
							if (flag2)
							{
								this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-915666668), lord.Name, num));
							}
							else
							{
								float arrivalSeconds = 60f + num * 20f;
								this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1393083291), new object[]
								{
									lord.Name,
									CS$<>8__locals2.lordInfo.StringId,
									arrivalSeconds,
									num
								}));
								this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(742519549) + CS$<>8__locals2.lordInfo.Side);
								this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1526872762) + CS$<>8__locals2.lordInfo.InterventionReason);
								this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1293834826) + CS$<>8__locals2.lordInfo.ArrivalPhrase + <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(942801567));
								DelayedTaskManager delayedTaskManager = this._behavior.GetDelayedTaskManager();
								if (delayedTaskManager != null)
								{
									delayedTaskManager.AddTask((double)arrivalSeconds, delegate
									{
										try
										{
											bool flag3 = Mission.Current == null;
											if (flag3)
											{
												CS$<>8__locals2.CS$<>8__locals1.<>4__this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1370010025), lord.Name));
											}
											else
											{
												CS$<>8__locals2.CS$<>8__locals1.<>4__this.SpawnLord(lord, CS$<>8__locals2.lordInfo, CS$<>8__locals2.CS$<>8__locals1.combat);
												bool flag4 = !string.IsNullOrEmpty(CS$<>8__locals2.lordInfo.ArrivalPhrase);
												if (flag4)
												{
													TextObject textObject = new TextObject(CS$<>8__locals2.lordInfo.ArrivalPhrase, null);
													MBInformationManager.AddQuickInformation(textObject, 4000, lord.CharacterObject, null, "");
												}
												bool flag5 = CS$<>8__locals2.CS$<>8__locals1.<>4__this._pendingLords > 0;
												if (flag5)
												{
													CS$<>8__locals2.CS$<>8__locals1.<>4__this._pendingLords = CS$<>8__locals2.CS$<>8__locals1.<>4__this._pendingLords - 1;
												}
												CS$<>8__locals2.CS$<>8__locals1.<>4__this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-995176787), lord.Name, arrivalSeconds));
											}
										}
										catch (Exception ex2)
										{
											CS$<>8__locals2.CS$<>8__locals1.<>4__this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1134836248) + lord.StringId, ex2.Message, ex2);
										}
									});
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1394037306), ex.Message, ex);
			}
		}

		// Token: 0x06000965 RID: 2405 RVA: 0x000B3244 File Offset: 0x000B1444
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void SpawnLord(Hero lord, LordIntervention lordInfo, ActiveCombat combat)
		{
			try
			{
				bool flag = Mission.Current == null;
				if (!flag)
				{
					bool flag2 = lordInfo.Side == <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1267372264);
					this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-489746396), lord.Name, flag2 ? <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1354614467) : <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(522127149)));
					bool flag3 = lord.PartyBelongedTo != null && combat.Settlement != null;
					if (flag3)
					{
						lord.PartyBelongedTo.Position = combat.Settlement.GatePosition;
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1615051117), lord.Name, combat.Settlement.Name, combat.Settlement.GatePosition));
					}
					this.SpawnDefenderAgent(lord.CharacterObject, combat, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1022016772), flag2, false);
					int num = 0;
					bool flag4 = lord.PartyBelongedTo != null;
					if (flag4)
					{
						List<CharacterObject> allLordTroops = this.GetAllLordTroops(lord);
						num = allLordTroops.Count;
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1637103252), lord.Name, num));
						Queue<CharacterObject> queue = flag2 ? this._playerSideReserve : this._enemySideReserve;
						foreach (CharacterObject characterObject in allLordTroops)
						{
							queue.Enqueue(characterObject);
						}
						this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(5318282), num, flag2 ? <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(314960226) : <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1564815785)));
						this.SpawnInitialTroopsForLord(combat, flag2);
						SettlementCombatLogger logger = this._logger;
						TextObject name = lord.Name;
						logger.LogLordSpawned((name != null) ? name.ToString() : null, lord.StringId, num, combat.Settlement.StringId);
					}
					bool flag5 = this._statistics != null;
					if (flag5)
					{
						CombatStatistics statistics = this._statistics;
						string stringId = lord.StringId;
						TextObject name2 = lord.Name;
						statistics.RecordLordArrival(stringId, (name2 != null) ? name2.ToString() : null, flag2, num);
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(617568692), ex.Message, ex);
			}
		}

		// Token: 0x06000966 RID: 2406 RVA: 0x000B34F4 File Offset: 0x000B16F4
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void SpawnLordTroopsForHostileCompanion(Hero lord, ActiveCombat combat)
		{
			try
			{
				bool flag = lord == null || combat == null || Mission.Current == null;
				if (flag)
				{
					this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-196431514));
				}
				else
				{
					bool flag2 = lord.IsWanderer || lord.Clan == null || lord.Clan == Clan.PlayerClan;
					if (flag2)
					{
						this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1898597421), lord.Name));
					}
					else
					{
						List<CharacterObject> lordTroopsIncludingArmy = this.GetLordTroopsIncludingArmy(lord);
						bool flag3 = lordTroopsIncludingArmy == null || lordTroopsIncludingArmy.Count == 0;
						if (flag3)
						{
							this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(85678240), lord.Name));
						}
						else
						{
							this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-528316530), lordTroopsIncludingArmy.Count, lord.Name));
							Vec3 spawnPosition = this.GetSpawnPosition(Mission.Current, Agent.Main);
							this.SpawnDefendersAtPosition(lordTroopsIncludingArmy, combat, spawnPosition, string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-276833873), lord.Name), false);
							this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(426212151), lordTroopsIncludingArmy.Count, lord.Name));
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1077723903), ex.Message, ex);
			}
		}

		// Token: 0x06000967 RID: 2407 RVA: 0x000B36A0 File Offset: 0x000B18A0
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void SpawnDefenderAgentFromBoundary(CharacterObject character, ActiveCombat combat, string role, bool onPlayerSide = false)
		{
			try
			{
				Mission mission = Mission.Current;
				bool flag = mission == null || combat.Analysis == null;
				if (flag)
				{
					this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1817930759));
				}
				else
				{
					Hero aggressor = Hero.FindFirst((Hero h) => h != null && h.StringId == combat.Analysis.AggressorStringId);
					Agent agent = (aggressor != null) ? Enumerable.FirstOrDefault<Agent>(mission.Agents, (Agent a) => a != null && a.Character != null && a.Character == aggressor.CharacterObject && a.IsActive()) : null;
					Vec3 spawnPosition = this.GetSpawnPosition(mission, agent);
					this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1826751613), role, spawnPosition));
					Vec2 vec = Vec2.Forward;
					bool flag2 = agent != null;
					if (flag2)
					{
						Vec2 vec2 = (agent.Position.AsVec2 - spawnPosition.AsVec2).Normalized();
						vec = vec2;
					}
					Team team = onPlayerSide ? mission.PlayerTeam : mission.DefenderTeam;
					bool flag3 = team == null;
					if (flag3)
					{
						this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1835572467));
					}
					else
					{
						AgentBuildData agentBuildData = new AgentBuildData(character).Team(team).TroopOrigin(new SimpleAgentOrigin(character, -1, null, default(UniqueTroopDescriptor))).InitialPosition(spawnPosition).InitialDirection(vec).Equipment(character.Equipment).NoHorses(false).ClothingColor1(team.Color).ClothingColor2(team.Color2);
						Agent agent2 = mission.SpawnAgent(agentBuildData, false);
						bool flag4 = agent2 != null;
						if (flag4)
						{
							agent2.SetWatchState(Agent.WatchState.Alarmed);
							this.TryEquipWeapon(agent2);
							bool flag5 = agent != null && agent2.IsAIControlled;
							if (flag5)
							{
								agent2.SetLookAgent(agent);
								agent2.SetAgentFlags(agent2.GetAgentFlags() | AgentFlag.CanAttack | AgentFlag.CanDefend);
							}
							this.RegisterSpawnedAgent(agent2, onPlayerSide);
							this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-636613740) + role + <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1092468470) + agent2.Name);
						}
						else
						{
							this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(837564968) + role);
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1155255828), ex.Message, ex);
			}
		}

		// Token: 0x06000968 RID: 2408 RVA: 0x000B3944 File Offset: 0x000B1B44
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void SpawnDefendersAtPosition(List<CharacterObject> characters, ActiveCombat combat, Vec3 position, string rolePrefix, bool ignoreSideLimit = false)
		{
			try
			{
				bool flag = characters == null || characters.Count == 0 || Mission.Current == null || combat == null;
				if (!flag)
				{
					this._currentCombat = combat;
					int num = 0;
					for (int i = 0; i < characters.Count; i++)
					{
						CharacterObject characterObject = characters[i];
						bool flag2 = characterObject == null;
						if (!flag2)
						{
							int num2 = this.CountActiveTroopsOnSide(false);
							int maxTroopsPerSide = this.GetMaxTroopsPerSide();
							bool flag3 = !ignoreSideLimit && num2 >= maxTroopsPerSide;
							if (flag3)
							{
								this._enemySideReserve.Enqueue(characterObject);
								this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1371638867), rolePrefix, num2, maxTroopsPerSide));
							}
							else
							{
								float num3 = (float)(this._random.NextDouble() * 3.141592653589793 * 2.0);
								float num4 = (float)(this._random.NextDouble() * 2.0);
								Vec3 v = new Vec3((float)Math.Cos((double)num3) * num4, (float)Math.Sin((double)num3) * num4, 0f, -1f);
								Vec3 position2 = position + v;
								this.SpawnDefenderAgentAtPosition(characterObject, combat, position2, rolePrefix ?? "", false);
								num++;
							}
						}
					}
					bool flag4 = num > 0;
					if (flag4)
					{
						this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-636545675), num, characters.Count, rolePrefix));
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1151752976), ex.Message, ex);
			}
		}

		// Token: 0x06000969 RID: 2409 RVA: 0x000B3B28 File Offset: 0x000B1D28
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void SpawnDefenderAgentAtPosition(CharacterObject character, ActiveCombat combat, Vec3 position, string role, bool onPlayerSide = false)
		{
			try
			{
				Mission mission = Mission.Current;
				bool flag = mission == null || character == null || combat == null;
				if (!flag)
				{
					float z = position.z;
					mission.Scene.GetHeightAtPoint(position.AsVec2, (BodyFlags)544321929U, ref z);
					Vec3 vec = new Vec3(position.x, position.y, z, -1f);
					Agent agent = null;
					bool flag2 = combat.Analysis != null && !string.IsNullOrEmpty(combat.Analysis.AggressorStringId);
					if (flag2)
					{
						Hero aggressorHero = Hero.FindFirst((Hero h) => h != null && h.StringId == combat.Analysis.AggressorStringId);
						bool flag3 = aggressorHero != null;
						if (flag3)
						{
							agent = Enumerable.FirstOrDefault<Agent>(mission.Agents, (Agent a) => a != null && a.IsActive() && a.Character != null && a.Character == aggressorHero.CharacterObject);
						}
					}
					Vec2 vec2 = Vec2.Forward;
					bool flag4 = agent != null;
					if (flag4)
					{
						Vec2 vec3 = (agent.Position.AsVec2 - vec.AsVec2).Normalized();
						bool flag5 = vec3.LengthSquared > 0.001f;
						if (flag5)
						{
							vec2 = vec3;
						}
					}
					Team team = onPlayerSide ? mission.PlayerTeam : mission.DefenderTeam;
					bool flag6 = team == null;
					if (flag6)
					{
						this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(762587709) + role + <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(731714720));
					}
					else
					{
						bool flag7 = character.Equipment == null;
						if (flag7)
						{
							this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-602920709), (character != null) ? character.Name : null));
						}
						else
						{
							AgentBuildData agentBuildData = new AgentBuildData(character).Team(team).TroopOrigin(new SimpleAgentOrigin(character, -1, null, default(UniqueTroopDescriptor))).InitialPosition(vec).InitialDirection(vec2).Equipment(character.Equipment).NoHorses(false).ClothingColor1(team.Color).ClothingColor2(team.Color2);
							Agent agent2 = mission.SpawnAgent(agentBuildData, false);
							bool flag8 = agent2 != null;
							if (flag8)
							{
								agent2.SetWatchState(Agent.WatchState.Alarmed);
								this.TryEquipWeapon(agent2);
								bool flag9 = agent != null && agent2.IsAIControlled;
								if (flag9)
								{
									agent2.SetLookAgent(agent);
									agent2.SetAgentFlags(agent2.GetAgentFlags() | AgentFlag.CanAttack | AgentFlag.CanDefend);
								}
								this.RegisterSpawnedAgent(agent2, onPlayerSide);
								this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-2032830351) + role + <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(622154790) + agent2.Name);
							}
							else
							{
								this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(2017190765) + role + <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(756924223));
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-825337002), ex.Message, ex);
			}
		}

		// Token: 0x0600096A RID: 2410 RVA: 0x000B3E70 File Offset: 0x000B2070
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void TrySpawnDefenderWithLimit(CharacterObject character, ActiveCombat combat, string role, bool onPlayerSide = false, bool isLocal = false)
		{
			try
			{
				int num = this.CountActiveTroopsOnSide(onPlayerSide);
				int maxTroopsPerSide = this.GetMaxTroopsPerSide();
				bool flag = num >= maxTroopsPerSide;
				if (flag)
				{
					Queue<CharacterObject> queue = onPlayerSide ? this._playerSideReserve : this._enemySideReserve;
					queue.Enqueue(character);
					this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(828839854), new object[]
					{
						role,
						onPlayerSide ? <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-77847009) : <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1018208890),
						num,
						maxTroopsPerSide
					}));
				}
				else
				{
					this.SpawnDefenderAgent(character, combat, role, onPlayerSide, isLocal);
					bool flag2 = combat.LocationType == LocationType.SmallIndoor && !onPlayerSide;
					if (flag2)
					{
						int defendersSpawnedInSmallLocation = combat.DefendersSpawnedInSmallLocation;
						combat.DefendersSpawnedInSmallLocation = defendersSpawnedInSmallLocation + 1;
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(836205516), ex.Message, ex);
			}
		}

		// Token: 0x0600096B RID: 2411 RVA: 0x000B3F88 File Offset: 0x000B2188
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void SpawnDefenderAgent(CharacterObject character, ActiveCombat combat, string role, bool onPlayerSide = false, bool isLocal = false)
		{
			try
			{
				Mission mission = Mission.Current;
				bool flag = mission == null;
				if (flag)
				{
					this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(2095743938) + role);
				}
				else
				{
					bool flag2 = combat.Analysis == null;
					if (flag2)
					{
						this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1254420249) + role);
					}
					else
					{
						Hero aggressor = Hero.FindFirst((Hero h) => h != null && h.StringId == combat.Analysis.AggressorStringId);
						Agent agent = null;
						bool flag3 = aggressor != null;
						if (flag3)
						{
							agent = Enumerable.FirstOrDefault<Agent>(mission.Agents, (Agent a) => a != null && a.Character != null && a.Character == aggressor.CharacterObject && a.IsActive());
						}
						Vec3 vec = Vec3.Zero;
						bool flag4 = false;
						Vec2 vec2 = Vec2.Forward;
						bool flag5 = false;
						bool flag6 = false;
						bool flag7 = combat.Settlement != null && (combat.Settlement.IsTown || combat.Settlement.IsCastle);
						if (flag7)
						{
							bool flag8 = !string.IsNullOrEmpty(role) && role.IndexOf(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1991003477), StringComparison.OrdinalIgnoreCase) >= 0;
							if (flag8)
							{
								flag6 = true;
							}
						}
						bool flag9 = flag6;
						if (flag9)
						{
							Vec3 vec3;
							Vec2 vec4;
							bool flag10 = this.TryGetPlayerEntranceSpawn(mission, out vec3, out vec4);
							if (flag10)
							{
								vec = vec3;
								flag4 = true;
								vec2 = vec4;
								flag5 = (vec4.LengthSquared > 0.001f);
								this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-737420573));
							}
							else
							{
								this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-826373286));
							}
						}
						bool flag11 = !flag4 && isLocal;
						if (flag11)
						{
							this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-2131000898));
							vec = this.GetLocalSpawnPosition(mission, agent);
						}
						else
						{
							bool flag12 = !flag4 && combat.LocationType == LocationType.SmallIndoor;
							if (flag12)
							{
								vec = this.GetEntranceSpawnPosition(mission);
							}
							else
							{
								bool flag13 = !flag4;
								if (flag13)
								{
									bool flag14 = combat.Settlement != null && (combat.Settlement.IsTown || combat.Settlement.IsCastle);
									bool flag15 = !onPlayerSide && flag14;
									if (flag15)
									{
										vec = this.GetCityGuardSpawnPosition(mission, agent);
									}
									else
									{
										vec = this.GetSpawnPosition(mission, agent);
									}
								}
							}
						}
						Vec2 vec5 = Vec2.Forward;
						bool flag16 = agent != null;
						if (flag16)
						{
							Vec2 vec6 = (agent.Position.AsVec2 - vec.AsVec2).Normalized();
							vec5 = ((vec6.LengthSquared > 0.001f) ? vec6 : vec5);
						}
						else
						{
							bool flag17 = flag5;
							if (flag17)
							{
								vec5 = vec2.Normalized();
							}
						}
						Team team = onPlayerSide ? mission.PlayerTeam : mission.DefenderTeam;
						bool flag18 = team == null;
						if (flag18)
						{
							this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(577349775), role, mission.PlayerTeam != null, mission.DefenderTeam != null));
						}
						else
						{
							bool flag19 = character.Equipment == null;
							if (flag19)
							{
								this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(146603404), character.Name));
							}
							else
							{
								AgentBuildData agentBuildData = new AgentBuildData(character).Team(team).TroopOrigin(new SimpleAgentOrigin(character, -1, null, default(UniqueTroopDescriptor))).InitialPosition(vec).InitialDirection(vec5).Equipment(character.Equipment).NoHorses(false).ClothingColor1(team.Color).ClothingColor2(team.Color2);
								Agent agent2 = mission.SpawnAgent(agentBuildData, false);
								bool flag20 = agent2 != null;
								if (flag20)
								{
									agent2.SetWatchState(Agent.WatchState.Alarmed);
									this.TryEquipWeapon(agent2);
									bool flag21 = agent != null && agent2.IsAIControlled;
									if (flag21)
									{
										agent2.SetLookAgent(agent);
										agent2.SetAgentFlags(agent2.GetAgentFlags() | AgentFlag.CanAttack | AgentFlag.CanDefend);
									}
									this.RegisterSpawnedAgent(agent2, onPlayerSide);
								}
								else
								{
									this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(837564968) + role);
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(2031415348), ex.Message, ex);
			}
		}

		// Token: 0x0600096C RID: 2412 RVA: 0x000B4440 File Offset: 0x000B2640
		[MethodImpl(MethodImplOptions.NoInlining)]
		private bool TryGetPlayerEntranceSpawn(Mission mission, out Vec3 spawnPosition, out Vec2 forward2D)
		{
			spawnPosition = Vec3.Zero;
			forward2D = Vec2.Forward;
			bool result;
			try
			{
				bool flag = mission == null || mission.Scene == null;
				if (flag)
				{
					result = false;
				}
				else
				{
					GameEntity gameEntity = mission.Scene.FindEntityWithTag(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1269687682));
					bool flag2 = gameEntity == null;
					if (flag2)
					{
						gameEntity = mission.Scene.FindEntityWithTag(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(254050794));
					}
					bool flag3 = gameEntity == null;
					if (flag3)
					{
						foreach (string tag in DefenderSpawner.MAIN_GATE_SPAWN_TAGS)
						{
							gameEntity = mission.Scene.FindEntityWithTag(tag);
							bool flag4 = gameEntity != null;
							if (flag4)
							{
								break;
							}
						}
					}
					bool flag5 = gameEntity == null;
					if (flag5)
					{
						result = false;
					}
					else
					{
						MatrixFrame globalFrame = gameEntity.GetGlobalFrame();
						Vec3 vec = globalFrame.rotation.f;
						bool flag6 = vec.LengthSquared < 0.001f;
						if (flag6)
						{
							vec = globalFrame.rotation.s;
						}
						bool flag7 = vec.LengthSquared < 0.001f;
						if (flag7)
						{
							vec = Vec3.Forward;
						}
						vec.Normalize();
						Vec3 s = globalFrame.rotation.s;
						bool flag8 = s.LengthSquared < 0.001f;
						if (flag8)
						{
							s = new Vec3(vec.y, -vec.x, 0f, -1f);
						}
						s.Normalize();
						float f = 25f;
						float f2 = (float)(this._random.NextDouble() * 8.0) - 4f;
						Vec3 vec2 = globalFrame.origin - vec * f + s * f2;
						float z = vec2.z;
						mission.Scene.GetHeightAtPoint(vec2.AsVec2, (BodyFlags)544321929U, ref z);
						vec2.z = z;
						spawnPosition = vec2;
						forward2D = new Vec2(vec.x, vec.y);
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

		// Token: 0x0600096D RID: 2413 RVA: 0x000B46A8 File Offset: 0x000B28A8
		[MethodImpl(MethodImplOptions.NoInlining)]
		private List<CharacterObject> GetSimpleDefenderTemplates(Settlement settlement, int maxVariants = 5)
		{
			DefenderSpawner.<>c__DisplayClass53_0 CS$<>8__locals1 = new DefenderSpawner.<>c__DisplayClass53_0();
			DefenderSpawner.<>c__DisplayClass53_0 CS$<>8__locals2 = CS$<>8__locals1;
			IFaction mapFaction = settlement.MapFaction;
			CS$<>8__locals2.culture = (((mapFaction != null) ? mapFaction.Culture : null) ?? settlement.Culture);
			bool flag = CS$<>8__locals1.culture == null;
			List<CharacterObject> result;
			if (flag)
			{
				this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1603608045), settlement.Name));
				result = new List<CharacterObject>();
			}
			else
			{
				this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-673012642), CS$<>8__locals1.culture.Name));
				List<CharacterObject> list = Enumerable.ToList<CharacterObject>(Enumerable.Take<CharacterObject>(Enumerable.Where<CharacterObject>(MBObjectManager.Instance.GetObjectTypeList<CharacterObject>(), (CharacterObject c) => c.Culture == CS$<>8__locals1.culture && c.Occupation == Occupation.Soldier && c.Tier >= 1 && c.Tier <= 2 && !c.IsHero && !c.IsMounted), maxVariants));
				bool flag2 = Enumerable.Any<CharacterObject>(list);
				if (flag2)
				{
					this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-282901738), list.Count));
					foreach (CharacterObject characterObject in list)
					{
						this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(61825898), characterObject.Name, characterObject.Tier));
					}
				}
				else
				{
					this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1818897864), CS$<>8__locals1.culture.Name));
				}
				result = list;
			}
			return result;
		}

		// Token: 0x0600096E RID: 2414 RVA: 0x000B4844 File Offset: 0x000B2A44
		[MethodImpl(MethodImplOptions.NoInlining)]
		private List<CharacterObject> GetMilitiaTemplates(Settlement settlement, int maxVariants = 5)
		{
			DefenderSpawner.<>c__DisplayClass54_0 CS$<>8__locals1 = new DefenderSpawner.<>c__DisplayClass54_0();
			DefenderSpawner.<>c__DisplayClass54_0 CS$<>8__locals2 = CS$<>8__locals1;
			IFaction mapFaction = settlement.MapFaction;
			CS$<>8__locals2.culture = (((mapFaction != null) ? mapFaction.Culture : null) ?? settlement.Culture);
			bool flag = CS$<>8__locals1.culture == null;
			List<CharacterObject> result;
			if (flag)
			{
				this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-747391704), settlement.Name));
				result = new List<CharacterObject>();
			}
			else
			{
				this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(770606927), CS$<>8__locals1.culture.Name));
				List<CharacterObject> list = Enumerable.ToList<CharacterObject>(Enumerable.Take<CharacterObject>(Enumerable.Where<CharacterObject>(MBObjectManager.Instance.GetObjectTypeList<CharacterObject>(), (CharacterObject c) => c.Culture == CS$<>8__locals1.culture && c.Occupation == Occupation.Soldier && c.Tier >= 2 && c.Tier <= 3 && !c.IsHero && c.IsMounted), maxVariants));
				bool flag2 = Enumerable.Any<CharacterObject>(list);
				if (flag2)
				{
					this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(356828425), list.Count));
					foreach (CharacterObject characterObject in list)
					{
						this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(101642191), characterObject.Name, characterObject.Tier));
					}
				}
				else
				{
					this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(348007571), CS$<>8__locals1.culture.Name));
				}
				result = list;
			}
			return result;
		}

		// Token: 0x0600096F RID: 2415 RVA: 0x000B49E0 File Offset: 0x000B2BE0
		[MethodImpl(MethodImplOptions.NoInlining)]
		private List<CharacterObject> GetGuardTemplates(Settlement settlement, int maxVariants = 5)
		{
			DefenderSpawner.<>c__DisplayClass55_0 CS$<>8__locals1 = new DefenderSpawner.<>c__DisplayClass55_0();
			CS$<>8__locals1.<>4__this = this;
			List<CharacterObject> list = new List<CharacterObject>();
			try
			{
				Town town = settlement.Town;
				MobileParty mobileParty = (town != null) ? town.GarrisonParty : null;
				bool flag = ((mobileParty != null) ? mobileParty.MemberRoster : null) != null && mobileParty.MemberRoster.Count > 0;
				if (flag)
				{
					this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1144631738), settlement.Name));
					List<\u206E\u202A\u200B\u202C\u202E\u206D\u206B\u206C\u206C\u200D\u202D\u206A\u202D\u206C\u206C\u202C\u206A\u206E\u202B\u202B\u200E\u206C\u202A\u200C\u200D\u200F\u202A\u206E\u206E\u200C\u202B\u200C\u202C\u200C\u200C\u206D\u202E\u200D\u206F\u202B\u202E<CharacterObject, int>> list2 = Enumerable.ToList<\u206E\u202A\u200B\u202C\u202E\u206D\u206B\u206C\u206C\u200D\u202D\u206A\u202D\u206C\u206C\u202C\u206A\u206E\u202B\u202B\u200E\u206C\u202A\u200C\u200D\u200F\u202A\u206E\u206E\u200C\u202B\u200C\u202C\u200C\u200C\u206D\u202E\u200D\u206F\u202B\u202E<CharacterObject, int>>(Enumerable.Select<TroopRosterElement, \u206E\u202A\u200B\u202C\u202E\u206D\u206B\u206C\u206C\u200D\u202D\u206A\u202D\u206C\u206C\u202C\u206A\u206E\u202B\u202B\u200E\u206C\u202A\u200C\u200D\u200F\u202A\u206E\u206E\u200C\u202B\u200C\u202C\u200C\u200C\u206D\u202E\u200D\u206F\u202B\u202E<CharacterObject, int>>(Enumerable.Where<TroopRosterElement>(mobileParty.MemberRoster.GetTroopRoster(), (TroopRosterElement e) => e.Character != null && !e.Character.IsHero && e.Number > 0), (TroopRosterElement e) => new \u206E\u202A\u200B\u202C\u202E\u206D\u206B\u206C\u206C\u200D\u202D\u206A\u202D\u206C\u206C\u202C\u206A\u206E\u202B\u202B\u200E\u206C\u202A\u200C\u200D\u200F\u202A\u206E\u206E\u200C\u202B\u200C\u202C\u200C\u200C\u206D\u202E\u200D\u206F\u202B\u202E<CharacterObject, int>(e.Character, e.Number)));
					bool flag2 = list2.Count > 0;
					if (flag2)
					{
						List<\u206E\u202A\u200B\u202C\u202E\u206D\u206B\u206C\u206C\u200D\u202D\u206A\u202D\u206C\u206C\u202C\u206A\u206E\u202B\u202B\u200E\u206C\u202A\u200C\u200D\u200F\u202A\u206E\u206E\u200C\u202B\u200C\u202C\u200C\u200C\u206D\u202E\u200D\u206F\u202B\u202E<CharacterObject, int>> list3 = Enumerable.ToList<\u206E\u202A\u200B\u202C\u202E\u206D\u206B\u206C\u206C\u200D\u202D\u206A\u202D\u206C\u206C\u202C\u206A\u206E\u202B\u202B\u200E\u206C\u202A\u200C\u200D\u200F\u202A\u206E\u206E\u200C\u202B\u200C\u202C\u200C\u200C\u206D\u202E\u200D\u206F\u202B\u202E<CharacterObject, int>>(Enumerable.ThenBy<\u206E\u202A\u200B\u202C\u202E\u206D\u206B\u206C\u206C\u200D\u202D\u206A\u202D\u206C\u206C\u202C\u206A\u206E\u202B\u202B\u200E\u206C\u202A\u200C\u200D\u200F\u202A\u206E\u206E\u200C\u202B\u200C\u202C\u200C\u200C\u206D\u202E\u200D\u206F\u202B\u202E<CharacterObject, int>, double>(Enumerable.ThenByDescending<\u206E\u202A\u200B\u202C\u202E\u206D\u206B\u206C\u206C\u200D\u202D\u206A\u202D\u206C\u206C\u202C\u206A\u206E\u202B\u202B\u200E\u206C\u202A\u200C\u200D\u200F\u202A\u206E\u206E\u200C\u202B\u200C\u202C\u200C\u200C\u206D\u202E\u200D\u206F\u202B\u202E<CharacterObject, int>, int>(Enumerable.OrderByDescending<\u206E\u202A\u200B\u202C\u202E\u206D\u206B\u206C\u206C\u200D\u202D\u206A\u202D\u206C\u206C\u202C\u206A\u206E\u202B\u202B\u200E\u206C\u202A\u200C\u200D\u200F\u202A\u206E\u206E\u200C\u202B\u200C\u202C\u200C\u200C\u206D\u202E\u200D\u206F\u202B\u202E<CharacterObject, int>, int>(list2, (\u206E\u202A\u200B\u202C\u202E\u206D\u206B\u206C\u206C\u200D\u202D\u206A\u202D\u206C\u206C\u202C\u206A\u206E\u202B\u202B\u200E\u206C\u202A\u200C\u200D\u200F\u202A\u206E\u206E\u200C\u202B\u200C\u202C\u200C\u200C\u206D\u202E\u200D\u206F\u202B\u202E<CharacterObject, int> c) => c.Character.Tier), (\u206E\u202A\u200B\u202C\u202E\u206D\u206B\u206C\u206C\u200D\u202D\u206A\u202D\u206C\u206C\u202C\u206A\u206E\u202B\u202B\u200E\u206C\u202A\u200C\u200D\u200F\u202A\u206E\u206E\u200C\u202B\u200C\u202C\u200C\u200C\u206D\u202E\u200D\u206F\u202B\u202E<CharacterObject, int> c) => c.Count), (\u206E\u202A\u200B\u202C\u202E\u206D\u206B\u206C\u206C\u200D\u202D\u206A\u202D\u206C\u206C\u202C\u206A\u206E\u202B\u202B\u200E\u206C\u202A\u200C\u200D\u200F\u202A\u206E\u206E\u200C\u202B\u200C\u202C\u200C\u200C\u206D\u202E\u200D\u206F\u202B\u202E<CharacterObject, int> _) => CS$<>8__locals1.<>4__this._random.NextDouble()));
						using (List<\u206E\u202A\u200B\u202C\u202E\u206D\u206B\u206C\u206C\u200D\u202D\u206A\u202D\u206C\u206C\u202C\u206A\u206E\u202B\u202B\u200E\u206C\u202A\u200C\u200D\u200F\u202A\u206E\u206E\u200C\u202B\u200C\u202C\u200C\u200C\u206D\u202E\u200D\u206F\u202B\u202E<CharacterObject, int>>.Enumerator enumerator = list3.GetEnumerator())
						{
							while (enumerator.MoveNext())
							{
								\u206E\u202A\u200B\u202C\u202E\u206D\u206B\u206C\u206C\u200D\u202D\u206A\u202D\u206C\u206C\u202C\u206A\u206E\u202B\u202B\u200E\u206C\u202A\u200C\u200D\u200F\u202A\u206E\u206E\u200C\u202B\u200C\u202C\u200C\u200C\u206D\u202E\u200D\u206F\u202B\u202E<CharacterObject, int> entry = enumerator.Current;
								bool flag3 = Enumerable.Any<CharacterObject>(list, (CharacterObject ch) => ch == entry.Character);
								if (!flag3)
								{
									list.Add(entry.Character);
									this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1342356198), entry.Character.Name, entry.Character.Tier, entry.Count));
									bool flag4 = list.Count >= maxVariants;
									if (flag4)
									{
										break;
									}
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(353424631), ex.Message, ex);
			}
			bool flag5 = list.Count >= maxVariants;
			List<CharacterObject> result;
			if (flag5)
			{
				result = list;
			}
			else
			{
				DefenderSpawner.<>c__DisplayClass55_0 CS$<>8__locals3 = CS$<>8__locals1;
				IFaction mapFaction = settlement.MapFaction;
				CS$<>8__locals3.culture = (((mapFaction != null) ? mapFaction.Culture : null) ?? settlement.Culture);
				bool flag6 = CS$<>8__locals1.culture == null;
				if (flag6)
				{
					this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1170386592), settlement.Name));
					result = list;
				}
				else
				{
					bool flag7 = list.Count > 0;
					if (flag7)
					{
						this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1738565982), list.Count, maxVariants));
					}
					else
					{
						this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(727010830));
					}
					List<CharacterObject> list4 = Enumerable.ToList<CharacterObject>(Enumerable.ThenBy<CharacterObject, double>(Enumerable.OrderByDescending<CharacterObject, int>(Enumerable.Where<CharacterObject>(MBObjectManager.Instance.GetObjectTypeList<CharacterObject>(), (CharacterObject c) => c.Culture == CS$<>8__locals1.culture && c.Occupation == Occupation.Soldier && c.Tier >= 4 && c.Tier <= 6 && !c.IsHero), (CharacterObject c) => c.Tier), (CharacterObject _) => CS$<>8__locals1.<>4__this._random.NextDouble()));
					using (List<CharacterObject>.Enumerator enumerator2 = list4.GetEnumerator())
					{
						while (enumerator2.MoveNext())
						{
							CharacterObject troop = enumerator2.Current;
							bool flag8 = Enumerable.Any<CharacterObject>(list, (CharacterObject ch) => ch == troop);
							if (!flag8)
							{
								list.Add(troop);
								this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1568274973), troop.Name, troop.Tier));
								bool flag9 = list.Count >= maxVariants;
								if (flag9)
								{
									break;
								}
							}
						}
					}
					bool flag10 = list.Count == 0;
					if (flag10)
					{
						this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1076282380), CS$<>8__locals1.culture.Name));
					}
					result = list;
				}
			}
			return result;
		}

		// Token: 0x06000970 RID: 2416 RVA: 0x000B4EA4 File Offset: 0x000B30A4
		private List<CharacterObject> GetAllLordTroops(Hero lord)
		{
			List<CharacterObject> list = new List<CharacterObject>();
			MobileParty partyBelongedTo = lord.PartyBelongedTo;
			bool flag = ((partyBelongedTo != null) ? partyBelongedTo.MemberRoster : null) == null;
			List<CharacterObject> result;
			if (flag)
			{
				result = list;
			}
			else
			{
				TroopRoster memberRoster = lord.PartyBelongedTo.MemberRoster;
				foreach (TroopRosterElement troopRosterElement in memberRoster.GetTroopRoster())
				{
					bool flag2 = troopRosterElement.Character != null && !troopRosterElement.Character.IsHero;
					if (flag2)
					{
						for (int i = 0; i < troopRosterElement.Number; i++)
						{
							list.Add(troopRosterElement.Character);
						}
					}
				}
				result = this.ArrangeTroopsByTierAndRole(list);
			}
			return result;
		}

		// Token: 0x06000971 RID: 2417 RVA: 0x000B4F84 File Offset: 0x000B3184
		[MethodImpl(MethodImplOptions.NoInlining)]
		public List<CharacterObject> GetLordTroopsIncludingArmy(Hero lord)
		{
			List<CharacterObject> list = new List<CharacterObject>();
			List<CharacterObject> result;
			try
			{
				bool flag = lord == null;
				if (flag)
				{
					result = list;
				}
				else
				{
					MobileParty partyBelongedTo = lord.PartyBelongedTo;
					bool flag2 = ((partyBelongedTo != null) ? partyBelongedTo.MemberRoster : null) != null;
					if (flag2)
					{
						TroopRoster memberRoster = lord.PartyBelongedTo.MemberRoster;
						foreach (TroopRosterElement troopRosterElement in memberRoster.GetTroopRoster())
						{
							bool flag3 = troopRosterElement.Character != null && !troopRosterElement.Character.IsHero;
							if (flag3)
							{
								for (int i = 0; i < troopRosterElement.Number; i++)
								{
									list.Add(troopRosterElement.Character);
								}
							}
						}
					}
					bool flag4 = lord.PartyBelongedTo != null && lord.PartyBelongedTo.Army != null;
					if (flag4)
					{
						Army army = lord.PartyBelongedTo.Army;
						bool flag5 = army.LeaderParty == lord.PartyBelongedTo;
						if (flag5)
						{
							this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-785057829), lord.Name));
							foreach (MobileParty mobileParty in army.Parties)
							{
								bool flag6 = mobileParty == null || mobileParty.MemberRoster == null || mobileParty == lord.PartyBelongedTo;
								if (!flag6)
								{
									TroopRoster memberRoster2 = mobileParty.MemberRoster;
									foreach (TroopRosterElement troopRosterElement2 in memberRoster2.GetTroopRoster())
									{
										bool flag7 = troopRosterElement2.Character != null && !troopRosterElement2.Character.IsHero;
										if (flag7)
										{
											for (int j = 0; j < troopRosterElement2.Number; j++)
											{
												list.Add(troopRosterElement2.Character);
											}
										}
									}
								}
							}
						}
					}
					this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1349654495), lord.Name, list.Count));
					result = this.ArrangeTroopsByTierAndRole(list);
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(109844513), ex.Message, ex);
				result = list;
			}
			return result;
		}

		// Token: 0x06000972 RID: 2418 RVA: 0x000B527C File Offset: 0x000B347C
		private List<CharacterObject> ArrangeTroopsByTierAndRole(List<CharacterObject> troops)
		{
			bool flag = troops == null || troops.Count <= 1;
			List<CharacterObject> result;
			if (flag)
			{
				result = (troops ?? new List<CharacterObject>());
			}
			else
			{
				Dictionary<FormationClass, List<CharacterObject>> dictionary = new Dictionary<FormationClass, List<CharacterObject>>();
				foreach (CharacterObject characterObject in troops)
				{
					FormationClass key = this.DetermineFormationClass(characterObject);
					List<CharacterObject> list;
					bool flag2 = !dictionary.TryGetValue(key, out list);
					if (flag2)
					{
						list = new List<CharacterObject>();
						dictionary[key] = list;
					}
					list.Add(characterObject);
				}
				List<FormationClass> list2 = Enumerable.ToList<FormationClass>(Enumerable.OrderBy<FormationClass, int>(dictionary.Keys, (FormationClass f) => (int)f));
				foreach (FormationClass key2 in list2)
				{
					List<CharacterObject> value = Enumerable.ToList<CharacterObject>(Enumerable.ThenBy<CharacterObject, double>(Enumerable.OrderByDescending<CharacterObject, int>(dictionary[key2], (CharacterObject t) => t.Tier), (CharacterObject _) => this._random.NextDouble()));
					dictionary[key2] = value;
				}
				List<CharacterObject> list3 = new List<CharacterObject>(troops.Count);
				FormationClass formationClass = FormationClass.NumberOfRegularFormations;
				int num = 0;
				for (;;)
				{
					if (!Enumerable.Any<List<CharacterObject>>(dictionary.Values, (List<CharacterObject> l) => l.Count > 0))
					{
						break;
					}
					CharacterObject characterObject2 = null;
					FormationClass formationClass2 = FormationClass.NumberOfRegularFormations;
					float num2 = float.MinValue;
					foreach (FormationClass formationClass3 in list2)
					{
						List<CharacterObject> list5;
						List<CharacterObject> list4 = dictionary.TryGetValue(formationClass3, out list5) ? list5 : null;
						bool flag3 = list4 == null || list4.Count == 0;
						if (!flag3)
						{
							CharacterObject characterObject3 = list4[0];
							float num3 = (float)characterObject3.Tier + (float)this._random.NextDouble() * 0.25f;
							bool flag4 = formationClass3 == formationClass && num >= 2;
							if (flag4)
							{
								num3 -= 2f;
							}
							bool flag5 = num3 > num2;
							if (flag5)
							{
								num2 = num3;
								characterObject2 = characterObject3;
								formationClass2 = formationClass3;
							}
						}
					}
					bool flag6 = characterObject2 == null;
					if (flag6)
					{
						break;
					}
					dictionary[formationClass2].RemoveAt(0);
					list3.Add(characterObject2);
					bool flag7 = formationClass2 == formationClass;
					if (flag7)
					{
						num++;
					}
					else
					{
						formationClass = formationClass2;
						num = 1;
					}
				}
				result = list3;
			}
			return result;
		}

		// Token: 0x06000973 RID: 2419 RVA: 0x000B5564 File Offset: 0x000B3764
		private FormationClass DetermineFormationClass(CharacterObject character)
		{
			bool flag = character == null;
			FormationClass result;
			if (flag)
			{
				result = FormationClass.Infantry;
			}
			else
			{
				bool isMounted = character.IsMounted;
				if (isMounted)
				{
					result = (character.IsRanged ? FormationClass.HorseArcher : FormationClass.Cavalry);
				}
				else
				{
					result = (character.IsRanged ? FormationClass.Ranged : FormationClass.Infantry);
				}
			}
			return result;
		}

		// Token: 0x06000974 RID: 2420 RVA: 0x000B55A8 File Offset: 0x000B37A8
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void SpawnInitialTroopsForLord(ActiveCombat combat, bool onPlayerSide)
		{
			try
			{
				Queue<CharacterObject> queue = onPlayerSide ? this._playerSideReserve : this._enemySideReserve;
				int num = 0;
				int num2 = this.CountActiveTroopsOnSide(onPlayerSide);
				int maxTroopsPerSide = this.GetMaxTroopsPerSide();
				while (queue.Count > 0 && num2 < maxTroopsPerSide)
				{
					CharacterObject character = queue.Dequeue();
					this.SpawnDefenderAgent(character, combat, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-546797831), onPlayerSide, false);
					num++;
					num2++;
				}
				this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(727809588), num, onPlayerSide ? <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-354453500) : <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1564815785), queue.Count));
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1413631466), ex.Message, ex);
			}
		}

		// Token: 0x06000975 RID: 2421 RVA: 0x000B56A4 File Offset: 0x000B38A4
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void CheckAndSpawnReinforcements()
		{
			try
			{
				bool flag = this._currentCombat == null || Mission.Current == null;
				if (!flag)
				{
					this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-927555147));
					this.CheckAndSpawnForSide(true);
					this.CheckAndSpawnForSide(false);
					bool flag2 = this._currentCombat != null && this._currentCombat.LocationType == LocationType.SmallIndoor && this._pendingGuards > 0;
					if (flag2)
					{
						int num = Math.Min(5, this._pendingGuards);
						this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1931380126));
						this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1095228858), this._pendingGuards));
						this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(535232165), num));
						this.SpawnGuards(this._currentCombat, num);
						this._pendingGuards -= num;
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1438590315), this._pendingGuards));
						this._guardsScheduled = (this._pendingGuards > 0);
					}
					this.CheckAndClearPendingCounters();
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1425359034), ex.Message, ex);
			}
		}

		// Token: 0x06000976 RID: 2422 RVA: 0x000B5830 File Offset: 0x000B3A30
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void CheckAndClearPendingCounters()
		{
			try
			{
				bool flag = this._playerSideReserve.Count == 0 && this._enemySideReserve.Count == 0;
				bool flag2 = flag;
				if (flag2)
				{
					bool flag3 = this._pendingSimpleDefenders > 0 && !this._simpleDefendersScheduled;
					if (flag3)
					{
						this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-18057870), this._pendingSimpleDefenders));
						this._pendingSimpleDefenders = 0;
					}
					bool flag4 = this._pendingMilitia > 0 && !this._militiaScheduled;
					if (flag4)
					{
						this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(228895898), this._pendingMilitia));
						this._pendingMilitia = 0;
					}
					bool flag5 = this._pendingGuards > 0 && !this._guardsScheduled;
					if (flag5)
					{
						this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1255477767), this._pendingGuards));
						this._pendingGuards = 0;
						this._guardsSpawnScheduledTime = null;
						this._guardsSpawnDelay = 0f;
					}
				}
				else
				{
					this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1350381775), this._playerSideReserve.Count, this._enemySideReserve.Count));
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1222461068), ex.Message, ex);
			}
		}

		// Token: 0x06000977 RID: 2423 RVA: 0x000B59E8 File Offset: 0x000B3BE8
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void CheckAndSpawnForSide(bool playerSide)
		{
			try
			{
				Queue<CharacterObject> queue = playerSide ? this._playerSideReserve : this._enemySideReserve;
				bool flag = queue.Count == 0;
				if (!flag)
				{
					int num = this.CountActiveTroopsOnSide(playerSide);
					int maxTroopsPerSide = this.GetMaxTroopsPerSide();
					int num2 = maxTroopsPerSide - num;
					bool flag2 = num2 <= 0;
					if (!flag2)
					{
						this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1224777622), new object[]
						{
							playerSide ? <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1597163292) : <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1275404516),
							num,
							queue.Count,
							num2
						}));
						int num3 = 0;
						while (queue.Count > 0 && num3 < num2)
						{
							CharacterObject character = queue.Dequeue();
							this.SpawnDefenderAgent(character, this._currentCombat, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1279814943), playerSide, false);
							num3++;
						}
						this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-710055165), num3, playerSide ? <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(314960226) : <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1018208890)));
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-619394402), ex.Message, ex);
			}
		}

		// Token: 0x06000978 RID: 2424 RVA: 0x000B5B78 File Offset: 0x000B3D78
		private int CountActiveTroopsOnSide(bool playerSide)
		{
			int result;
			try
			{
				bool flag = Mission.Current == null;
				if (flag)
				{
					result = 0;
				}
				else
				{
					Team team = playerSide ? Mission.Current.PlayerTeam : Mission.Current.DefenderTeam;
					bool flag2 = team == null;
					if (flag2)
					{
						result = 0;
					}
					else
					{
						HashSet<int> hashSet = playerSide ? this._spawnedPlayerAgents : this._spawnedEnemyAgents;
						bool flag3 = hashSet.Count == 0;
						if (flag3)
						{
							result = 0;
						}
						else
						{
							HashSet<int> aliveIndices = new HashSet<int>();
							int num = 0;
							foreach (Agent agent in Mission.Current.Agents)
							{
								bool flag4 = agent == null;
								if (!flag4)
								{
									bool flag5 = !agent.IsActive() || !agent.IsHuman;
									if (!flag5)
									{
										bool flag6 = agent.Team != team;
										if (!flag6)
										{
											bool flag7 = hashSet.Contains(agent.Index);
											if (flag7)
											{
												num++;
												aliveIndices.Add(agent.Index);
											}
										}
									}
								}
							}
							bool flag8 = aliveIndices.Count != hashSet.Count;
							if (flag8)
							{
								hashSet.RemoveWhere((int index) => !aliveIndices.Contains(index));
							}
							result = num;
						}
					}
				}
			}
			catch
			{
				result = 0;
			}
			return result;
		}

		// Token: 0x06000979 RID: 2425 RVA: 0x000B5D20 File Offset: 0x000B3F20
		[MethodImpl(MethodImplOptions.NoInlining)]
		private Vec3 GetLocalSpawnPosition(Mission mission, Agent targetAgent)
		{
			Vec3 result;
			try
			{
				bool flag = Agent.Main != null;
				Vec2 asVec;
				if (flag)
				{
					asVec = Agent.Main.Position.AsVec2;
				}
				else
				{
					bool flag2 = targetAgent != null;
					if (flag2)
					{
						asVec = targetAgent.Position.AsVec2;
					}
					else
					{
						asVec = new Vec2(0f, 0f);
					}
				}
				float num = (float)(this._random.NextDouble() * 3.141592653589793 * 2.0);
				float num2 = 40f + (float)(this._random.NextDouble() * 40.0);
				Vec2 v = new Vec2((float)Math.Cos((double)num) * num2, (float)Math.Sin((double)num) * num2);
				Vec2 vec = asVec + v;
				float z = 0f;
				mission.Scene.GetHeightAtPoint(vec, (BodyFlags)544321929U, ref z);
				Vec3 vec2 = new Vec3(vec.x, vec.y, z, -1f);
				result = vec2;
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-190064523), ex.Message, ex);
				bool flag3 = Agent.Main != null;
				if (flag3)
				{
					result = Agent.Main.Position + new Vec3(15f, 0f, 0f, -1f);
				}
				else
				{
					result = new Vec3(0f, 0f, 0f, -1f);
				}
			}
			return result;
		}

		// Token: 0x0600097A RID: 2426 RVA: 0x000B5EB4 File Offset: 0x000B40B4
		[MethodImpl(MethodImplOptions.NoInlining)]
		private Vec3 GetEntranceSpawnPosition(Mission mission)
		{
			Vec3 result;
			try
			{
				bool flag = mission == null || mission.Scene == null;
				if (flag)
				{
					result = Vec3.Zero;
				}
				else
				{
					List<GameEntity> list = Enumerable.ToList<GameEntity>(mission.Scene.FindEntitiesWithTag(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-108479177)));
					bool flag2 = list != null && list.Count > 0;
					if (flag2)
					{
						GameEntity gameEntity = Enumerable.FirstOrDefault<GameEntity>(list, (GameEntity e) => e != null && !e.IsGhostObject()) ?? Enumerable.First<GameEntity>(list);
						MatrixFrame globalFrame = gameEntity.GetGlobalFrame();
						Vec3 origin = globalFrame.origin;
						float z = 0f;
						mission.Scene.GetHeightAtPoint(origin.AsVec2, (BodyFlags)544321929U, ref z);
						origin.z = z;
						this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-206824180), origin));
						result = origin;
					}
					else
					{
						this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1972893685));
						bool flag3 = Agent.Main != null && Agent.Main.IsActive();
						if (flag3)
						{
							result = Agent.Main.Position + new Vec3(5f, 5f, 0f, -1f);
						}
						else
						{
							result = Vec3.Zero;
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(322483742), ex.Message, ex);
				Agent main = Agent.Main;
				result = ((main != null) ? main.Position : Vec3.Zero);
			}
			return result;
		}

		// Token: 0x0600097B RID: 2427 RVA: 0x000B6070 File Offset: 0x000B4270
		[MethodImpl(MethodImplOptions.NoInlining)]
		private int CalculateVillageDefenderCount(Settlement settlement)
		{
			int result;
			try
			{
				bool flag = settlement == null;
				if (flag)
				{
					result = 0;
				}
				else
				{
					int num = 0;
					MilitiaPartyComponent militiaPartyComponent = settlement.MilitiaPartyComponent;
					TroopRoster troopRoster;
					if (militiaPartyComponent == null)
					{
						troopRoster = null;
					}
					else
					{
						MobileParty mobileParty = militiaPartyComponent.MobileParty;
						troopRoster = ((mobileParty != null) ? mobileParty.MemberRoster : null);
					}
					TroopRoster troopRoster2 = troopRoster;
					bool flag2 = troopRoster2 != null;
					if (flag2)
					{
						num = troopRoster2.TotalRegulars;
					}
					bool flag3 = num <= 0 && settlement.Militia > 0f;
					if (flag3)
					{
						num = (int)Math.Round((double)settlement.Militia, MidpointRounding.AwayFromZero);
					}
					num = Math.Max(0, num);
					string text;
					if (settlement == null)
					{
						text = null;
					}
					else
					{
						TextObject name = settlement.Name;
						text = ((name != null) ? name.ToString() : null);
					}
					string arg = text ?? <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(293976138);
					this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-251975132), arg, num));
					result = num;
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1037267938), ex.Message, ex);
				result = 0;
			}
			return result;
		}

		// Token: 0x0600097C RID: 2428 RVA: 0x000B6184 File Offset: 0x000B4384
		[MethodImpl(MethodImplOptions.NoInlining)]
		private int CalculateCityGuardCount(Settlement settlement)
		{
			int result;
			try
			{
				bool flag = settlement == null;
				if (flag)
				{
					result = 0;
				}
				else
				{
					int num = 0;
					Town town = settlement.Town;
					TroopRoster troopRoster;
					if (town == null)
					{
						troopRoster = null;
					}
					else
					{
						MobileParty garrisonParty = town.GarrisonParty;
						troopRoster = ((garrisonParty != null) ? garrisonParty.MemberRoster : null);
					}
					TroopRoster troopRoster2 = troopRoster;
					bool flag2 = troopRoster2 != null;
					if (flag2)
					{
						num = troopRoster2.TotalRegulars;
					}
					bool flag3 = num <= 0;
					if (flag3)
					{
						MilitiaPartyComponent militiaPartyComponent = settlement.MilitiaPartyComponent;
						TroopRoster troopRoster3;
						if (militiaPartyComponent == null)
						{
							troopRoster3 = null;
						}
						else
						{
							MobileParty mobileParty = militiaPartyComponent.MobileParty;
							troopRoster3 = ((mobileParty != null) ? mobileParty.MemberRoster : null);
						}
						TroopRoster troopRoster4 = troopRoster3;
						bool flag4 = troopRoster4 != null;
						if (flag4)
						{
							num = troopRoster4.TotalRegulars;
						}
					}
					bool flag5 = num <= 0 && settlement.Militia > 0f;
					if (flag5)
					{
						num = (int)Math.Round((double)settlement.Militia, MidpointRounding.AwayFromZero);
					}
					num = Math.Max(0, num);
					string text;
					if (settlement == null)
					{
						text = null;
					}
					else
					{
						TextObject name = settlement.Name;
						text = ((name != null) ? name.ToString() : null);
					}
					string arg = text ?? <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1617165097);
					this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(779846146), arg, num));
					result = num;
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-662747495), ex.Message, ex);
				result = 0;
			}
			return result;
		}

		// Token: 0x0600097D RID: 2429 RVA: 0x000B62E8 File Offset: 0x000B44E8
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void EnsureCityGuardSpawnPointsCached(Mission mission)
		{
			try
			{
				bool flag = mission == null || mission.Scene == null;
				if (!flag)
				{
					bool flag2 = this._cachedMissionForCitySpawnPoints == mission && this._cachedCityGuardSpawnPoints.Count > 0;
					if (!flag2)
					{
						this._cachedMissionForCitySpawnPoints = mission;
						this._cachedCityGuardSpawnPoints.Clear();
						string[] city_GUARD_SPAWN_TAGS = DefenderSpawner.CITY_GUARD_SPAWN_TAGS;
						int i = 0;
						while (i < city_GUARD_SPAWN_TAGS.Length)
						{
							string text = city_GUARD_SPAWN_TAGS[i];
							try
							{
								IEnumerable<GameEntity> enumerable = mission.Scene.FindEntitiesWithTag(text);
								bool flag3 = enumerable == null;
								if (!flag3)
								{
									foreach (GameEntity gameEntity in enumerable)
									{
										bool flag4 = gameEntity == null;
										if (!flag4)
										{
											bool flag5 = gameEntity.IsGhostObject();
											if (!flag5)
											{
												bool flag6 = this._cachedCityGuardSpawnPoints.Contains(gameEntity);
												if (!flag6)
												{
													this._cachedCityGuardSpawnPoints.Add(gameEntity);
												}
											}
										}
									}
								}
							}
							catch (Exception ex)
							{
								this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(390761170), <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(297029750) + text + <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-718607138) + ex.Message, ex);
							}
							IL_142:
							i++;
							continue;
							goto IL_142;
						}
						this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-972516360), this._cachedCityGuardSpawnPoints.Count));
					}
				}
			}
			catch (Exception ex2)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-744766715), ex2.Message, ex2);
			}
		}

		// Token: 0x0600097E RID: 2430 RVA: 0x000B64E8 File Offset: 0x000B46E8
		[MethodImpl(MethodImplOptions.NoInlining)]
		private Vec3 GetCityGuardSpawnPosition(Mission mission, Agent targetAgent)
		{
			Vec3 result;
			try
			{
				this.EnsureCityGuardSpawnPointsCached(mission);
				bool flag = this._cachedCityGuardSpawnPoints.Count == 0;
				if (flag)
				{
					this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-595497563));
					result = this.GetSpawnPosition(mission, targetAgent);
				}
				else
				{
					bool flag2 = Agent.Main != null && Agent.Main.IsActive();
					Vec2 v = flag2 ? Agent.Main.Position.AsVec2 : Vec2.Zero;
					List<GameEntity> list = new List<GameEntity>();
					foreach (GameEntity gameEntity in this._cachedCityGuardSpawnPoints)
					{
						bool flag3 = gameEntity == null;
						if (!flag3)
						{
							Vec3 globalPosition = gameEntity.GlobalPosition;
							bool flag4 = flag2;
							if (flag4)
							{
								float length = (globalPosition.AsVec2 - v).Length;
								bool flag5 = length < 70f;
								if (flag5)
								{
									continue;
								}
							}
							list.Add(gameEntity);
						}
					}
					bool flag6 = list.Count == 0;
					if (flag6)
					{
						this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-183553547), 70f));
						list = Enumerable.ToList<GameEntity>(Enumerable.Where<GameEntity>(this._cachedCityGuardSpawnPoints, (GameEntity e) => e != null));
					}
					bool flag7 = list.Count == 0;
					if (flag7)
					{
						this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1241795063));
						result = this.GetSpawnPosition(mission, targetAgent);
					}
					else
					{
						GameEntity gameEntity2 = list[this._random.Next(list.Count)];
						Vec3 globalPosition2 = gameEntity2.GlobalPosition;
						float num = (float)(this._random.NextDouble() * 3.141592653589793 * 2.0);
						float num2 = (float)(this._random.NextDouble() * 2.0);
						Vec2 v2 = new Vec2((float)Math.Cos((double)num) * num2, (float)Math.Sin((double)num) * num2);
						Vec2 vec = globalPosition2.AsVec2 + v2;
						float z = globalPosition2.z;
						mission.Scene.GetHeightAtPoint(vec, (BodyFlags)544321929U, ref z);
						Vec3 vec2 = new Vec3(vec.x, vec.y, z, -1f);
						string arg = <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-77203443);
						bool flag8 = flag2;
						if (flag8)
						{
							arg = (vec - v).Length.ToString(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(219026490));
						}
						this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-297269313), gameEntity2.Name, vec2, arg));
						result = vec2;
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1911338328), ex.Message, ex);
				result = this.GetSpawnPosition(mission, targetAgent);
			}
			return result;
		}

		// Token: 0x0600097F RID: 2431 RVA: 0x000B6838 File Offset: 0x000B4A38
		[MethodImpl(MethodImplOptions.NoInlining)]
		private Vec3 GetSpawnPosition(Mission mission, Agent targetAgent)
		{
			Vec3 result;
			try
			{
				bool flag = Agent.Main != null;
				Vec2 asVec;
				if (flag)
				{
					asVec = Agent.Main.Position.AsVec2;
				}
				else
				{
					bool flag2 = targetAgent != null;
					if (flag2)
					{
						asVec = targetAgent.Position.AsVec2;
					}
					else
					{
						asVec = new Vec2(0f, 0f);
					}
				}
				for (int i = 0; i < 10; i++)
				{
					float num = (float)(this._random.NextDouble() * 3.141592653589793 * 2.0);
					float num2 = 30f;
					Vec2 v = new Vec2((float)Math.Cos((double)num) * num2, (float)Math.Sin((double)num) * num2);
					Vec2 position = asVec + v;
					Vec2 closestBoundaryPosition = mission.GetClosestBoundaryPosition(position);
					Vec2 v2 = (asVec - closestBoundaryPosition).Normalized();
					float f = 5f + (float)(this._random.NextDouble() * 5.0);
					Vec2 vec = closestBoundaryPosition + v2 * f;
					float z = 0f;
					mission.Scene.GetHeightAtPoint(vec, (BodyFlags)544321929U, ref z);
					Vec3 vec2 = new Vec3(vec.x, vec.y, z, -1f);
					bool flag3 = this.IsPositionFlat(mission, vec2);
					if (flag3)
					{
						return vec2;
					}
				}
				this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1669775592));
				Vec2 v3 = new Vec2(1f, 0f);
				Vec2 vec3 = asVec + v3 * 20f;
				float z2 = 0f;
				mission.Scene.GetHeightAtPoint(vec3, (BodyFlags)544321929U, ref z2);
				result = new Vec3(vec3.x, vec3.y, z2, -1f);
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(2123829), ex.Message, ex);
				bool flag4 = Agent.Main != null;
				if (flag4)
				{
					result = Agent.Main.Position + new Vec3(10f, 10f, 0f, -1f);
				}
				else
				{
					result = new Vec3(0f, 0f, 0f, -1f);
				}
			}
			return result;
		}

		// Token: 0x06000980 RID: 2432 RVA: 0x000B6AB8 File Offset: 0x000B4CB8
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
				bool flag2 = num2 > 3f;
				if (flag2)
				{
					result = false;
				}
				else
				{
					result = true;
				}
			}
			catch
			{
				result = true;
			}
			return result;
		}

		// Token: 0x06000981 RID: 2433 RVA: 0x000B6BD4 File Offset: 0x000B4DD4
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void TryEquipWeapon(Agent agent)
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
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-632649226), ex.Message, ex);
			}
		}

		// Token: 0x06000982 RID: 2434 RVA: 0x000B6D00 File Offset: 0x000B4F00
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void HandleTransitionToLargeLocation(ActiveCombat combat)
		{
			try
			{
				bool flag = combat == null;
				if (!flag)
				{
					bool flag2 = combat.Analysis != null && !combat.Analysis.NeedsDefenders;
					if (flag2)
					{
						this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1267422281));
					}
					else
					{
						this._currentCombat = combat;
						bool flag3 = this._currentCombat.LocationType != LocationType.LargeOutdoor;
						if (!flag3)
						{
							this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1477988971));
							this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-2007660469), this._pendingGuards, this._enemySideReserve.Count));
							this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-286220820), this.CountActiveTroopsOnSide(false), this.GetMaxTroopsPerSide()));
							this.ReleaseReserveTroops(false, <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-855931436), int.MaxValue);
							bool flag4 = this._pendingGuards > 0;
							if (flag4)
							{
								float num = 5f;
								bool flag5 = this._guardsSpawnScheduledTime != null && this._guardsSpawnDelay > 0f;
								if (flag5)
								{
									float num2 = (float)(CampaignTime.Now - this._guardsSpawnScheduledTime.Value).ToSeconds;
									num = Math.Max(0f, this._guardsSpawnDelay - num2);
									this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-16449995));
									this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(2023317403), this._guardsSpawnDelay));
									this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(707827932), num2));
									this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1236304662), num));
									bool flag6 = num <= 0f;
									if (flag6)
									{
										num = 1f;
										this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(246290175), num));
									}
								}
								else
								{
									this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-928456270), num));
								}
								this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-888526811), this._pendingGuards, num));
								this._guardsScheduled = true;
								this.ScheduleLargeGuardWave(num);
							}
							else
							{
								bool flag7 = this._enemySideReserve.Count == 0;
								if (flag7)
								{
									this._guardsScheduled = false;
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-2028423980), ex.Message, ex);
			}
		}

		// Token: 0x06000983 RID: 2435 RVA: 0x000B7020 File Offset: 0x000B5220
		private void ScheduleLargeGuardWave(float delaySeconds)
		{
			bool flag = this._currentCombat == null || this._currentCombat.LocationType != LocationType.LargeOutdoor;
			if (!flag)
			{
				this._behavior.GetDelayedTaskManager().AddTask((double)delaySeconds, delegate
				{
					try
					{
						bool flag2 = this._currentCombat == null || this._currentCombat.LocationType != LocationType.LargeOutdoor || Mission.Current == null;
						if (!flag2)
						{
							int maxTroopsPerSide = this.GetMaxTroopsPerSide();
							int num = this.CountActiveTroopsOnSide(false);
							int num2 = Math.Max(0, maxTroopsPerSide - num);
							this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-188906537));
							this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1935578356), new object[]
							{
								num,
								maxTroopsPerSide,
								this._enemySideReserve.Count,
								this._pendingGuards
							}));
							bool flag3 = num2 > 0;
							if (flag3)
							{
								int num3 = this.ReleaseReserveTroops(false, <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-855931436), num2);
								num2 = Math.Max(0, num2 - num3);
								bool flag4 = num2 > 0 && this._pendingGuards > 0;
								if (flag4)
								{
									int num4 = Math.Min(num2, this._pendingGuards);
									this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(598194635), num4));
									this.SpawnGuards(this._currentCombat, num4);
									this._pendingGuards -= num4;
								}
							}
							else
							{
								this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(964200611));
							}
							bool flag5 = this._pendingGuards > 0 || this._enemySideReserve.Count > 0;
							if (flag5)
							{
								this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-544478320));
								this.ScheduleLargeGuardWave(60f);
							}
							else
							{
								this._guardsScheduled = false;
								this._guardsSpawnScheduledTime = null;
								this._guardsSpawnDelay = 0f;
								this.CheckAndClearPendingCounters();
							}
						}
					}
					catch (Exception ex)
					{
						this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1957813426), ex.Message, ex);
					}
				});
			}
		}

		// Token: 0x06000984 RID: 2436 RVA: 0x000B7070 File Offset: 0x000B5270
		[MethodImpl(MethodImplOptions.NoInlining)]
		private int ReleaseReserveTroops(bool playerSide, string rolePrefix, int maxToRelease = 2147483647)
		{
			int result;
			try
			{
				bool flag = this._currentCombat == null || Mission.Current == null;
				if (flag)
				{
					result = 0;
				}
				else
				{
					Queue<CharacterObject> queue = playerSide ? this._playerSideReserve : this._enemySideReserve;
					bool flag2 = queue.Count == 0 || maxToRelease <= 0;
					if (flag2)
					{
						result = 0;
					}
					else
					{
						int maxTroopsPerSide = this.GetMaxTroopsPerSide();
						int num = this.CountActiveTroopsOnSide(playerSide);
						int val = Math.Max(0, maxTroopsPerSide - num);
						int num2 = Math.Min(Math.Min(val, maxToRelease), queue.Count);
						bool flag3 = num2 <= 0;
						if (flag3)
						{
							result = 0;
						}
						else
						{
							int num3 = 0;
							while (queue.Count > 0 && num3 < num2)
							{
								CharacterObject character = queue.Dequeue();
								this.SpawnDefenderAgent(character, this._currentCombat, rolePrefix, playerSide, false);
								num3++;
							}
							bool flag4 = num3 > 0;
							if (flag4)
							{
								this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-607059211), num3, playerSide ? <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-354453500) : <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1564815785), rolePrefix));
							}
							result = num3;
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-2068117823), ex.Message, ex);
				result = 0;
			}
			return result;
		}

		// Token: 0x040005AC RID: 1452
		private readonly AIInfluenceBehavior _behavior;

		// Token: 0x040005AD RID: 1453
		private readonly SettlementCombatLogger _logger;

		// Token: 0x040005AE RID: 1454
		private readonly Random _random = new Random();

		// Token: 0x040005AF RID: 1455
		private CombatStatistics _statistics;

		// Token: 0x040005B0 RID: 1456
		private int _pendingSimpleDefenders = 0;

		// Token: 0x040005B1 RID: 1457
		private int _pendingMilitia = 0;

		// Token: 0x040005B2 RID: 1458
		private int _pendingGuards = 0;

		// Token: 0x040005B3 RID: 1459
		private int _pendingLords = 0;

		// Token: 0x040005B4 RID: 1460
		private bool _simpleDefendersScheduled = false;

		// Token: 0x040005B5 RID: 1461
		private bool _militiaScheduled = false;

		// Token: 0x040005B6 RID: 1462
		private bool _guardsScheduled = false;

		// Token: 0x040005B7 RID: 1463
		private CampaignTime? _guardsSpawnScheduledTime = null;

		// Token: 0x040005B8 RID: 1464
		private float _guardsSpawnDelay = 0f;

		// Token: 0x040005B9 RID: 1465
		private const int MAX_TROOPS_PER_SIDE_LARGE = 75;

		// Token: 0x040005BA RID: 1466
		private const int MAX_TOTAL_TROOPS_LARGE = 150;

		// Token: 0x040005BB RID: 1467
		private const int MAX_TROOPS_PER_SIDE_SMALL = 5;

		// Token: 0x040005BC RID: 1468
		private const int MAX_TOTAL_TROOPS_SMALL = 10;

		// Token: 0x040005BD RID: 1469
		private Queue<CharacterObject> _playerSideReserve = new Queue<CharacterObject>();

		// Token: 0x040005BE RID: 1470
		private Queue<CharacterObject> _enemySideReserve = new Queue<CharacterObject>();

		// Token: 0x040005BF RID: 1471
		private readonly HashSet<int> _spawnedPlayerAgents = new HashSet<int>();

		// Token: 0x040005C0 RID: 1472
		private readonly HashSet<int> _spawnedEnemyAgents = new HashSet<int>();

		// Token: 0x040005C1 RID: 1473
		private float _lastReinforcementCheck = 0f;

		// Token: 0x040005C2 RID: 1474
		private const float REINFORCEMENT_CHECK_INTERVAL = 60f;

		// Token: 0x040005C3 RID: 1475
		private ActiveCombat _currentCombat = null;

		// Token: 0x040005C4 RID: 1476
		private const float MIN_DISTANCE_FROM_PLAYER_FOR_CITY_SPAWN = 70f;

		// Token: 0x040005C5 RID: 1477
		private static readonly string[] CITY_GUARD_SPAWN_TAGS = new string[]
		{
			<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1427727707),
			<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(703623359),
			<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(69205807),
			<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1261737325),
			<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-857455291),
			<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-108479177),
			<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1801354088),
			<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1195307963),
			<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(300320170),
			<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(712865737),
			<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1470706663),
			<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(201160553),
			<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1068385557),
			<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1407448206),
			<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1350093107)
		};

		// Token: 0x040005C6 RID: 1478
		private Mission _cachedMissionForCitySpawnPoints = null;

		// Token: 0x040005C7 RID: 1479
		private readonly List<GameEntity> _cachedCityGuardSpawnPoints = new List<GameEntity>();

		// Token: 0x040005C8 RID: 1480
		private static readonly string[] MAIN_GATE_SPAWN_TAGS = new string[]
		{
			<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(381413183),
			<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(2072484528),
			<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-757013729),
			<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1212318669),
			<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(254050794)
		};
	}
}
