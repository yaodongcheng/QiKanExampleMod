using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x02000112 RID: 274
	public class CombatStatistics
	{
		// Token: 0x0600089D RID: 2205 RVA: 0x000B0888 File Offset: 0x000AEA88
		public CombatStatistics(AIInfluenceBehavior behavior)
		{
			this._behavior = behavior;
			this._logger = SettlementCombatLogger.Instance;
		}

		// Token: 0x0600089E RID: 2206 RVA: 0x000B0934 File Offset: 0x000AEB34
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void StartTracking(ActiveCombat combat, CivilianBehavior civilianBehavior, SettlementCombatManager combatManager)
		{
			bool isTracking = this._isTracking;
			if (isTracking)
			{
				this._behavior.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1508459709));
				this.StopTracking();
			}
			this._currentCombat = combat;
			this._isTracking = true;
			this._deaths.Clear();
			this._wounds.Clear();
			this._participants.Clear();
			this._defenderCasualties = new SideCasualtySummary();
			this._attackerCasualties = new SideCasualtySummary();
			this._civilianCasualties = new CivilianCasualtySummary();
			this._vipCasualties.Clear();
			this._simpleDefendersArrived = false;
			this._simpleDefendersSpawned = 0;
			this._militiaArrived = false;
			this._militiaSpawned = 0;
			this._guardsArrived = false;
			this._guardsSpawned = 0;
			this._lordsArrived.Clear();
			bool flag = Mission.Current != null;
			if (flag)
			{
				SettlementCombatMissionLogic missionBehavior = new SettlementCombatMissionLogic(this, this._behavior, civilianBehavior, combatManager);
				Mission.Current.AddMissionBehavior(missionBehavior);
				this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-944767500));
				PlayerReinforcementMissionLogic playerReinforcementMissionLogic = Enumerable.FirstOrDefault<PlayerReinforcementMissionLogic>(Enumerable.OfType<PlayerReinforcementMissionLogic>(Mission.Current.MissionBehaviors));
				bool flag2 = playerReinforcementMissionLogic == null;
				if (flag2)
				{
					PlayerReinforcementMissionLogic missionBehavior2 = new PlayerReinforcementMissionLogic(this._behavior);
					Mission.Current.AddMissionBehavior(missionBehavior2);
					this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1069296233));
				}
				else
				{
					this._logger.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(120848868));
				}
			}
			this.SubscribeToEvents();
			this._logger.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1215654415), combat.Settlement.Name));
		}

		// Token: 0x0600089F RID: 2207 RVA: 0x000B0AE4 File Offset: 0x000AECE4
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void StopTracking()
		{
			bool flag = !this._isTracking;
			if (!flag)
			{
				this.UnsubscribeFromEvents();
				this._isTracking = false;
				this._behavior.LogMessage(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1551103109), this._deaths.Count, this._wounds.Count));
			}
		}

		// Token: 0x060008A0 RID: 2208 RVA: 0x000B0B4C File Offset: 0x000AED4C
		public CombatResult GetCombatResult()
		{
			CombatResult combatResult = new CombatResult();
			combatResult.Settlement = this._currentCombat.Settlement;
			combatResult.Duration = CampaignTime.Now - this._currentCombat.StartTime;
			combatResult.TotalKilled = this._deaths.Count;
			combatResult.TotalWounded = this._wounds.Count;
			combatResult.CiviliansKilled = Enumerable.Count<DeathRecord>(this._deaths, (DeathRecord d) => d.IsCivilian);
			combatResult.CiviliansWounded = Enumerable.Count<WoundRecord>(this._wounds, (WoundRecord w) => w.IsCivilian);
			combatResult.Deaths = Enumerable.ToList<DeathRecord>(this._deaths);
			combatResult.Wounds = Enumerable.ToList<WoundRecord>(this._wounds);
			combatResult.Participants = Enumerable.ToList<string>(this._participants);
			combatResult.SimpleDefendersArrived = this._simpleDefendersArrived;
			combatResult.SimpleDefendersSpawned = this._simpleDefendersSpawned;
			combatResult.MilitiaArrived = this._militiaArrived;
			combatResult.MilitiaSpawned = this._militiaSpawned;
			combatResult.GuardsArrived = this._guardsArrived;
			combatResult.GuardsSpawned = this._guardsSpawned;
			combatResult.LordsArrived = Enumerable.ToList<LordArrivalInfo>(this._lordsArrived);
			combatResult.DefenderCasualties = this._defenderCasualties.Clone();
			combatResult.AttackerCasualties = this._attackerCasualties.Clone();
			combatResult.CivilianCasualties = this._civilianCasualties.Clone();
			combatResult.ImportantCasualties = Enumerable.ToList<VipCasualtyRecord>(Enumerable.Select<VipCasualtyRecord, VipCasualtyRecord>(this._vipCasualties, (VipCasualtyRecord v) => v.Clone()));
			CombatResult combatResult2 = combatResult;
			using (List<LordArrivalInfo>.Enumerator enumerator = combatResult2.LordsArrived.GetEnumerator())
			{
				while (enumerator.MoveNext())
				{
					LordArrivalInfo lordInfo = enumerator.Current;
					lordInfo.TroopsLost = Enumerable.Count<DeathRecord>(this._deaths, (DeathRecord d) => d.VictimStringId != null && d.VictimStringId.Contains(lordInfo.LordStringId));
				}
			}
			combatResult2.MilitiaKilled = Enumerable.Count<DeathRecord>(this._deaths, (DeathRecord d) => d.VictimType == <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1261074898));
			combatResult2.SimpleDefendersKilled = Enumerable.Count<DeathRecord>(this._deaths, (DeathRecord d) => d.VictimType == <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1791795322));
			combatResult2.GuardsKilled = Enumerable.Count<DeathRecord>(this._deaths, (DeathRecord d) => d.VictimType == <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1785840666));
			return combatResult2;
		}

		// Token: 0x060008A1 RID: 2209 RVA: 0x0001CD41 File Offset: 0x0001AF41
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void RecordSimpleDefendersArrival(int count)
		{
			this._simpleDefendersArrived = true;
			this._simpleDefendersSpawned = count;
			this._logger.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1572928143), count));
		}

		// Token: 0x060008A2 RID: 2210 RVA: 0x0001CD75 File Offset: 0x0001AF75
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void RecordMilitiaArrival(int count)
		{
			this._militiaArrived = true;
			this._militiaSpawned = count;
			this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1128624864), count));
		}

		// Token: 0x060008A3 RID: 2211 RVA: 0x0001CDA9 File Offset: 0x0001AFA9
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void RecordGuardsArrival(int count)
		{
			this._guardsArrived = true;
			this._guardsSpawned = count;
			this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-36842488), count));
		}

		// Token: 0x060008A4 RID: 2212 RVA: 0x000B0E20 File Offset: 0x000AF020
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void RecordLordArrival(string lordId, string lordName, bool onPlayerSide, int troopsCount)
		{
			this._lordsArrived.Add(new LordArrivalInfo
			{
				LordStringId = lordId,
				LordName = lordName,
				OnPlayerSide = onPlayerSide,
				TroopsSpawned = troopsCount,
				TroopsLost = 0
			});
			this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1079267364), lordName, onPlayerSide, troopsCount));
		}

		// Token: 0x060008A5 RID: 2213 RVA: 0x000B0E94 File Offset: 0x000AF094
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void RecordDeath(string victimName, string victimId, string victimType, string killerName, string killerId, string killerType, bool isCivilian, CombatSide victimSide, bool isCivilianFemale, bool isCivilianChild, bool isImportant)
		{
			bool flag = !this._isTracking;
			if (!flag)
			{
				DeathRecord deathRecord = new DeathRecord
				{
					VictimStringId = victimId,
					VictimName = victimName,
					VictimType = victimType,
					KillerStringId = killerId,
					KillerName = killerName,
					KillerType = killerType,
					IsCivilian = isCivilian,
					DeathTime = CampaignTime.Now,
					VictimSide = victimSide,
					IsCivilianFemale = (isCivilian && isCivilianFemale),
					IsCivilianChild = (isCivilian && isCivilianChild),
					IsImportant = isImportant
				};
				this._deaths.Add(deathRecord);
				this._participants.Add(victimId);
				bool flag2 = killerId != <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1604186482);
				if (flag2)
				{
					this._participants.Add(killerId);
				}
				this._logger.Log(string.Concat(new string[]
				{
					<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-817719988),
					victimName,
					<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-331623597),
					victimType,
					<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1917332531),
					killerName,
					<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-432756618),
					killerType,
					<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1827987126)
				}));
				this.UpdateAggregatesForDeath(deathRecord);
			}
		}

		// Token: 0x060008A6 RID: 2214 RVA: 0x000B0FE8 File Offset: 0x000AF1E8
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void RecordWound(string victimName, string victimId, string victimType, string attackerName, string attackerId, string attackerType, bool isCivilian, CombatSide victimSide, bool isCivilianFemale, bool isCivilianChild, bool isImportant)
		{
			bool flag = !this._isTracking;
			if (!flag)
			{
				WoundRecord woundRecord = new WoundRecord
				{
					VictimStringId = victimId,
					VictimName = victimName,
					VictimType = victimType,
					AttackerStringId = attackerId,
					AttackerName = attackerName,
					AttackerType = attackerType,
					IsCivilian = isCivilian,
					WoundTime = CampaignTime.Now,
					VictimSide = victimSide,
					IsCivilianFemale = (isCivilian && isCivilianFemale),
					IsCivilianChild = (isCivilian && isCivilianChild),
					IsImportant = isImportant
				};
				this._wounds.Add(woundRecord);
				this._participants.Add(victimId);
				bool flag2 = attackerId != <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1604186482);
				if (flag2)
				{
					this._participants.Add(attackerId);
				}
				this._logger.Log(string.Concat(new string[]
				{
					<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1171728261),
					victimName,
					<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(371362249),
					victimType,
					<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1549518733),
					attackerName,
					<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-331623597),
					attackerType,
					<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-52838832)
				}));
				this.UpdateAggregatesForWound(woundRecord);
			}
		}

		// Token: 0x060008A7 RID: 2215 RVA: 0x0001CDDD File Offset: 0x0001AFDD
		private void SubscribeToEvents()
		{
			CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, new Action<Hero, Hero, KillCharacterAction.KillCharacterActionDetail, bool>(this.OnHeroKilled));
		}

		// Token: 0x060008A8 RID: 2216 RVA: 0x0001CDF8 File Offset: 0x0001AFF8
		private void UnsubscribeFromEvents()
		{
			CampaignEvents.HeroKilledEvent.ClearListeners(this);
		}

		// Token: 0x060008A9 RID: 2217 RVA: 0x000B113C File Offset: 0x000AF33C
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
		{
			bool flag = !this._isTracking;
			if (!flag)
			{
				try
				{
					bool flag2 = Enumerable.Any<DeathRecord>(this._deaths, (DeathRecord d) => d.VictimStringId == victim.StringId);
					if (!flag2)
					{
						bool flag3 = !victim.IsLord && !victim.IsWanderer;
						bool isCivilianChild = flag3 && this.IsChild(victim.Age, victim.IsChild);
						bool isCivilianFemale = flag3 && victim.IsFemale;
						bool isImportant = victim.IsLord || victim == Hero.MainHero;
						CombatSide victimSide = this.DetermineHeroSide(victim);
						TextObject name = victim.Name;
						string victimName = (name != null) ? name.ToString() : null;
						string stringId = victim.StringId;
						string victimType = victim.IsLord ? <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1991003477) : <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(2088827454);
						string text;
						if (killer == null)
						{
							text = null;
						}
						else
						{
							TextObject name2 = killer.Name;
							text = ((name2 != null) ? name2.ToString() : null);
						}
						this.RecordDeath(victimName, stringId, victimType, text ?? <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1597152944), ((killer != null) ? killer.StringId : null) ?? <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(378266911), (killer != null) ? <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1831925871) : <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-547646552), flag3, victimSide, isCivilianFemale, isCivilianChild, isImportant);
					}
				}
				catch (Exception ex)
				{
					this._behavior.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1721408184) + ex.Message);
				}
			}
		}

		// Token: 0x060008AA RID: 2218 RVA: 0x000B1314 File Offset: 0x000AF514
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void RecordWound(Agent victimAgent, Agent attackerAgent)
		{
			bool flag = !this._isTracking;
			if (!flag)
			{
				try
				{
					CharacterObject characterObject = victimAgent.Character as CharacterObject;
					Hero hero = (characterObject != null) ? characterObject.HeroObject : null;
					CharacterObject characterObject2 = ((attackerAgent != null) ? attackerAgent.Character : null) as CharacterObject;
					Hero hero2 = (characterObject2 != null) ? characterObject2.HeroObject : null;
					bool flag2 = hero == null;
					if (!flag2)
					{
						bool flag3 = !hero.IsLord && !hero.IsWanderer;
						bool isCivilianChild = flag3 && this.IsChild(hero.Age, hero.IsChild);
						bool isCivilianFemale = flag3 && hero.IsFemale;
						bool isImportant = hero.IsLord || hero == Hero.MainHero;
						CombatSide victimSide = this.DetermineHeroSide(hero);
						WoundRecord woundRecord = new WoundRecord();
						woundRecord.VictimStringId = hero.StringId;
						TextObject name = hero.Name;
						woundRecord.VictimName = ((name != null) ? name.ToString() : null);
						woundRecord.AttackerStringId = ((hero2 != null) ? hero2.StringId : null);
						string text;
						if (hero2 == null)
						{
							text = null;
						}
						else
						{
							TextObject name2 = hero2.Name;
							text = ((name2 != null) ? name2.ToString() : null);
						}
						woundRecord.AttackerName = (text ?? <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-2041001075));
						woundRecord.IsCivilian = flag3;
						woundRecord.WoundTime = CampaignTime.Now;
						woundRecord.VictimSide = victimSide;
						woundRecord.IsCivilianFemale = isCivilianFemale;
						woundRecord.IsCivilianChild = isCivilianChild;
						woundRecord.IsImportant = isImportant;
						WoundRecord woundRecord2 = woundRecord;
						this._wounds.Add(woundRecord2);
						this._participants.Add(hero.StringId);
						bool flag4 = hero2 != null;
						if (flag4)
						{
							this._participants.Add(hero2.StringId);
						}
						AIInfluenceBehavior behavior = this._behavior;
						string format = <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1050139732);
						object name3 = hero.Name;
						object obj;
						if (hero2 == null)
						{
							obj = null;
						}
						else
						{
							TextObject name4 = hero2.Name;
							obj = ((name4 != null) ? name4.ToString() : null);
						}
						behavior.LogMessage(string.Format(format, name3, obj ?? <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(293976138), flag3));
						this.UpdateAggregatesForWound(woundRecord2);
					}
				}
				catch (Exception ex)
				{
					this._behavior.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(494805078) + ex.Message);
				}
			}
		}

		// Token: 0x060008AB RID: 2219 RVA: 0x000B155C File Offset: 0x000AF75C
		private void UpdateAggregatesForDeath(DeathRecord death)
		{
			CombatSide victimSide = death.VictimSide;
			CombatSide combatSide = victimSide;
			if (combatSide != CombatSide.Defenders)
			{
				if (combatSide == CombatSide.Attackers)
				{
					SideCasualtySummary attackerCasualties = this._attackerCasualties;
					int num = attackerCasualties.Killed;
					attackerCasualties.Killed = num + 1;
				}
			}
			else
			{
				SideCasualtySummary defenderCasualties = this._defenderCasualties;
				int num = defenderCasualties.Killed;
				defenderCasualties.Killed = num + 1;
			}
			bool isCivilian = death.IsCivilian;
			if (isCivilian)
			{
				bool isCivilianChild = death.IsCivilianChild;
				if (isCivilianChild)
				{
					CivilianCasualtySummary civilianCasualties = this._civilianCasualties;
					int num = civilianCasualties.ChildrenKilled;
					civilianCasualties.ChildrenKilled = num + 1;
				}
				else
				{
					bool isCivilianFemale = death.IsCivilianFemale;
					if (isCivilianFemale)
					{
						CivilianCasualtySummary civilianCasualties2 = this._civilianCasualties;
						int num = civilianCasualties2.WomenKilled;
						civilianCasualties2.WomenKilled = num + 1;
					}
					else
					{
						CivilianCasualtySummary civilianCasualties3 = this._civilianCasualties;
						int num = civilianCasualties3.MenKilled;
						civilianCasualties3.MenKilled = num + 1;
					}
				}
			}
			bool isImportant = death.IsImportant;
			if (isImportant)
			{
				this._vipCasualties.Add(new VipCasualtyRecord
				{
					VictimName = death.VictimName,
					VictimStringId = death.VictimStringId,
					Side = death.VictimSide,
					IsKilled = true,
					KillerName = death.KillerName,
					KillerStringId = death.KillerStringId,
					KillerType = death.KillerType
				});
			}
		}

		// Token: 0x060008AC RID: 2220 RVA: 0x000B1694 File Offset: 0x000AF894
		private void UpdateAggregatesForWound(WoundRecord wound)
		{
			CombatSide victimSide = wound.VictimSide;
			CombatSide combatSide = victimSide;
			if (combatSide != CombatSide.Defenders)
			{
				if (combatSide == CombatSide.Attackers)
				{
					SideCasualtySummary attackerCasualties = this._attackerCasualties;
					int num = attackerCasualties.Wounded;
					attackerCasualties.Wounded = num + 1;
				}
			}
			else
			{
				SideCasualtySummary defenderCasualties = this._defenderCasualties;
				int num = defenderCasualties.Wounded;
				defenderCasualties.Wounded = num + 1;
			}
			bool isCivilian = wound.IsCivilian;
			if (isCivilian)
			{
				bool isCivilianChild = wound.IsCivilianChild;
				if (isCivilianChild)
				{
					CivilianCasualtySummary civilianCasualties = this._civilianCasualties;
					int num = civilianCasualties.ChildrenWounded;
					civilianCasualties.ChildrenWounded = num + 1;
				}
				else
				{
					bool isCivilianFemale = wound.IsCivilianFemale;
					if (isCivilianFemale)
					{
						CivilianCasualtySummary civilianCasualties2 = this._civilianCasualties;
						int num = civilianCasualties2.WomenWounded;
						civilianCasualties2.WomenWounded = num + 1;
					}
					else
					{
						CivilianCasualtySummary civilianCasualties3 = this._civilianCasualties;
						int num = civilianCasualties3.MenWounded;
						civilianCasualties3.MenWounded = num + 1;
					}
				}
			}
			bool isImportant = wound.IsImportant;
			if (isImportant)
			{
				this._vipCasualties.Add(new VipCasualtyRecord
				{
					VictimName = wound.VictimName,
					VictimStringId = wound.VictimStringId,
					Side = wound.VictimSide,
					IsKilled = false,
					KillerName = wound.AttackerName,
					KillerStringId = wound.AttackerStringId,
					KillerType = wound.AttackerType
				});
			}
		}

		// Token: 0x060008AD RID: 2221 RVA: 0x000B17CC File Offset: 0x000AF9CC
		private CombatSide DetermineHeroSide(Hero hero)
		{
			bool flag = hero == null;
			CombatSide result;
			if (flag)
			{
				result = CombatSide.Unknown;
			}
			else
			{
				bool flag2 = this._currentCombat != null && hero.MapFaction == this._currentCombat.Settlement.MapFaction;
				if (flag2)
				{
					result = CombatSide.Defenders;
				}
				else
				{
					result = CombatSide.Attackers;
				}
			}
			return result;
		}

		// Token: 0x060008AE RID: 2222 RVA: 0x000B1818 File Offset: 0x000AFA18
		private bool IsChild(float age, bool isChildOccupation)
		{
			bool result;
			if (isChildOccupation)
			{
				result = true;
			}
			else
			{
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
				bool flag = ageModel2 != null;
				if (flag)
				{
					result = (age < (float)ageModel2.HeroComesOfAge);
				}
				else
				{
					result = (age < 18f);
				}
			}
			return result;
		}

		// Token: 0x04000545 RID: 1349
		private readonly AIInfluenceBehavior _behavior;

		// Token: 0x04000546 RID: 1350
		private readonly SettlementCombatLogger _logger;

		// Token: 0x04000547 RID: 1351
		private ActiveCombat _currentCombat;

		// Token: 0x04000548 RID: 1352
		private bool _isTracking;

		// Token: 0x04000549 RID: 1353
		private List<DeathRecord> _deaths = new List<DeathRecord>();

		// Token: 0x0400054A RID: 1354
		private List<WoundRecord> _wounds = new List<WoundRecord>();

		// Token: 0x0400054B RID: 1355
		private HashSet<string> _participants = new HashSet<string>();

		// Token: 0x0400054C RID: 1356
		private SideCasualtySummary _defenderCasualties = new SideCasualtySummary();

		// Token: 0x0400054D RID: 1357
		private SideCasualtySummary _attackerCasualties = new SideCasualtySummary();

		// Token: 0x0400054E RID: 1358
		private CivilianCasualtySummary _civilianCasualties = new CivilianCasualtySummary();

		// Token: 0x0400054F RID: 1359
		private readonly List<VipCasualtyRecord> _vipCasualties = new List<VipCasualtyRecord>();

		// Token: 0x04000550 RID: 1360
		private bool _simpleDefendersArrived = false;

		// Token: 0x04000551 RID: 1361
		private int _simpleDefendersSpawned = 0;

		// Token: 0x04000552 RID: 1362
		private bool _militiaArrived = false;

		// Token: 0x04000553 RID: 1363
		private int _militiaSpawned = 0;

		// Token: 0x04000554 RID: 1364
		private bool _guardsArrived = false;

		// Token: 0x04000555 RID: 1365
		private int _guardsSpawned = 0;

		// Token: 0x04000556 RID: 1366
		private List<LordArrivalInfo> _lordsArrived = new List<LordArrivalInfo>();
	}
}
