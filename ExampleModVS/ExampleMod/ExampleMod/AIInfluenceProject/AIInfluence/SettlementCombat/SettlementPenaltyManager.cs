using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x0200015A RID: 346
	public class SettlementPenaltyManager : CampaignBehaviorBase
	{
		// Token: 0x17000218 RID: 536
		// (get) Token: 0x06000AF9 RID: 2809 RVA: 0x0001DE75 File Offset: 0x0001C075
		// (set) Token: 0x06000AFA RID: 2810 RVA: 0x0001DE7C File Offset: 0x0001C07C
		public static SettlementPenaltyManager Instance { get; private set; }

		// Token: 0x06000AFB RID: 2811 RVA: 0x0001DE84 File Offset: 0x0001C084
		public SettlementPenaltyManager()
		{
			this._logger = SettlementCombatLogger.Instance;
			this._storage = new SettlementPenaltiesStorage();
			SettlementPenaltyManager.Instance = this;
		}

		// Token: 0x06000AFC RID: 2812 RVA: 0x0001DEBD File Offset: 0x0001C0BD
		public override void RegisterEvents()
		{
			CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(this.OnSessionLaunched));
			CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, new Action(this.OnDailyTick));
		}

		// Token: 0x06000AFD RID: 2813 RVA: 0x0001DEF0 File Offset: 0x0001C0F0
		public override void SyncData(IDataStore dataStore)
		{
		}

		// Token: 0x06000AFE RID: 2814 RVA: 0x000C5DFC File Offset: 0x000C3FFC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void Initialize()
		{
			bool initialized = this._initialized;
			if (initialized)
			{
				this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-482103549));
			}
			else
			{
				this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(403516994));
				Dictionary<string, ActivePenalty> dictionary = this._storage.LoadPenalties();
				bool flag = dictionary != null && dictionary.Count > 0;
				if (flag)
				{
					this._activePenalties = dictionary;
					this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(376725542), this._activePenalties.Count));
				}
				else
				{
					this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1251327582));
				}
				this._initialized = true;
				this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1015882444));
			}
		}

		// Token: 0x06000AFF RID: 2815 RVA: 0x0001DEF3 File Offset: 0x0001C0F3
		private void OnSessionLaunched(CampaignGameStarter starter)
		{
			this.Initialize();
		}

		// Token: 0x06000B00 RID: 2816 RVA: 0x000C5EDC File Offset: 0x000C40DC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void AddPenalty(Settlement settlement, float prosperityPenaltyPerDay, int durationDays, string reason)
		{
			try
			{
				bool flag = settlement == null;
				if (flag)
				{
					this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-478698893), <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1944932752), null);
				}
				else
				{
					ActivePenalty value = new ActivePenalty
					{
						SettlementId = settlement.StringId,
						ProsperityPenaltyPerDay = prosperityPenaltyPerDay,
						StartDay = (float)CampaignTime.Now.ToDays,
						DurationDays = durationDays,
						Reason = reason
					};
					this._activePenalties[settlement.StringId] = value;
					this._storage.SavePenalties(this._activePenalties);
					this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1493863049), new object[]
					{
						settlement.Name,
						prosperityPenaltyPerDay,
						durationDays,
						reason
					}));
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-2064077325), ex.Message, ex);
			}
		}

		// Token: 0x06000B01 RID: 2817 RVA: 0x000C6000 File Offset: 0x000C4200
		public ActivePenalty GetActivePenalty(Settlement settlement)
		{
			bool flag = settlement == null;
			ActivePenalty result;
			if (flag)
			{
				result = null;
			}
			else
			{
				ActivePenalty activePenalty;
				bool flag2 = this._activePenalties.TryGetValue(settlement.StringId, out activePenalty);
				if (flag2)
				{
					float num = (float)CampaignTime.Now.ToDays;
					bool flag3 = num < activePenalty.StartDay + (float)activePenalty.DurationDays;
					if (flag3)
					{
						return activePenalty;
					}
				}
				result = null;
			}
			return result;
		}

		// Token: 0x06000B02 RID: 2818 RVA: 0x000C6068 File Offset: 0x000C4268
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void ApplyCasualtiesToVillage(Settlement settlement, int civiliansKilled)
		{
			try
			{
				bool flag = settlement == null || !settlement.IsVillage || civiliansKilled <= 0;
				if (!flag)
				{
					float num = (float)civiliansKilled * 0.5f;
					bool flag2 = settlement.Village != null;
					if (flag2)
					{
						settlement.Village.Hearth = Math.Max(50f, settlement.Village.Hearth - num);
						this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(245126587), new object[]
						{
							settlement.Name,
							num,
							civiliansKilled,
							settlement.Village.Hearth
						}));
					}
					else
					{
						this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(499715639), settlement.Name));
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1942823379), ex.Message, ex);
			}
		}

		// Token: 0x06000B03 RID: 2819 RVA: 0x000C6180 File Offset: 0x000C4380
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void ApplyMilitiaCasualties(Settlement settlement, int militiaKilled)
		{
			try
			{
				bool flag = settlement == null || militiaKilled <= 0;
				if (!flag)
				{
					MilitiaPartyComponent militiaPartyComponent = settlement.MilitiaPartyComponent;
					MobileParty mobileParty = (militiaPartyComponent != null) ? militiaPartyComponent.MobileParty : null;
					bool flag2 = mobileParty == null || mobileParty.MemberRoster == null;
					if (flag2)
					{
						this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-471333231), settlement.Name));
					}
					else
					{
						int totalManCount = mobileParty.MemberRoster.TotalManCount;
						int num = Math.Min(militiaKilled, totalManCount);
						bool flag3 = num > 0;
						if (flag3)
						{
							int num2 = 0;
							List<TroopRosterElement> list = Enumerable.ToList<TroopRosterElement>(mobileParty.MemberRoster.GetTroopRoster());
							foreach (TroopRosterElement troopRosterElement in list)
							{
								bool flag4 = num2 >= num;
								if (flag4)
								{
									break;
								}
								int num3 = Math.Min(troopRosterElement.Number, num - num2);
								mobileParty.MemberRoster.AddToCounts(troopRosterElement.Character, -num3, false, 0, 0, true, -1);
								num2 += num3;
							}
							this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1350529105), new object[]
							{
								settlement.Name,
								num2,
								totalManCount,
								mobileParty.MemberRoster.TotalManCount
							}));
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1355279542), ex.Message, ex);
			}
		}

		// Token: 0x06000B04 RID: 2820 RVA: 0x000C6354 File Offset: 0x000C4554
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void OnDailyTick()
		{
			try
			{
				float num = (float)CampaignTime.Now.ToDays;
				List<string> list = new List<string>();
				foreach (KeyValuePair<string, ActivePenalty> keyValuePair in this._activePenalties)
				{
					string key = keyValuePair.Key;
					ActivePenalty value = keyValuePair.Value;
					bool flag = num >= value.StartDay + (float)value.DurationDays;
					if (flag)
					{
						list.Add(key);
						Settlement settlement = Settlement.Find(key);
						bool flag2 = settlement != null;
						if (flag2)
						{
							this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(2072114599), settlement.Name));
						}
					}
					else
					{
						Settlement settlement2 = Settlement.Find(key);
						bool flag3 = settlement2 != null;
						if (flag3)
						{
							bool flag4 = settlement2.IsVillage && settlement2.Village != null;
							if (flag4)
							{
								settlement2.Village.Hearth = Math.Max(50f, settlement2.Village.Hearth - value.ProsperityPenaltyPerDay);
								int num2 = value.DurationDays - (int)(num - value.StartDay);
								this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(2136711417), settlement2.Name, value.ProsperityPenaltyPerDay, num2));
							}
							else
							{
								bool flag5 = settlement2.IsTown && settlement2.Town != null;
								if (flag5)
								{
									this.ChangeSettlementProsperity(settlement2, -value.ProsperityPenaltyPerDay);
									int num3 = value.DurationDays - (int)(num - value.StartDay);
									this._logger.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1237880945), settlement2.Name, value.ProsperityPenaltyPerDay, num3));
								}
							}
						}
					}
				}
				foreach (string key2 in list)
				{
					this._activePenalties.Remove(key2);
				}
				bool flag6 = this._activePenalties.Count > 0 || list.Count > 0;
				if (flag6)
				{
					this._storage.SavePenalties(this._activePenalties);
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(2063107105), ex.Message, ex);
			}
		}

		// Token: 0x06000B05 RID: 2821 RVA: 0x000C663C File Offset: 0x000C483C
		private void ChangeSettlementProsperity(Settlement settlement, float change)
		{
			bool flag = settlement == null || settlement.Town == null;
			if (!flag)
			{
				settlement.Town.Prosperity = Math.Max(0f, settlement.Town.Prosperity + change);
			}
		}

		// Token: 0x040006C0 RID: 1728
		private Dictionary<string, ActivePenalty> _activePenalties = new Dictionary<string, ActivePenalty>();

		// Token: 0x040006C1 RID: 1729
		private readonly SettlementCombatLogger _logger;

		// Token: 0x040006C2 RID: 1730
		private readonly SettlementPenaltiesStorage _storage;

		// Token: 0x040006C3 RID: 1731
		private bool _initialized = false;
	}
}
