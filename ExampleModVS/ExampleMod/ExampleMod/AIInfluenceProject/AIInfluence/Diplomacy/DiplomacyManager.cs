using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AIInfluence.DynamicEvents;
using AIInfluence.Util;
using MCM.Abstractions.Base.Global;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.LogEntries;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AIInfluence.Diplomacy
{
	// Token: 0x020001D2 RID: 466
	public class DiplomacyManager
	{
		// Token: 0x170002E5 RID: 741
		// (get) Token: 0x06000EB7 RID: 3767 RVA: 0x000F4144 File Offset: 0x000F2344
		public static DiplomacyManager Instance
		{
			get
			{
				bool flag = DiplomacyManager._instance == null;
				if (flag)
				{
					DiplomacyManager._instance = new DiplomacyManager();
				}
				return DiplomacyManager._instance;
			}
		}

		// Token: 0x170002E6 RID: 742
		// (get) Token: 0x06000EB8 RID: 3768 RVA: 0x0001E1A8 File Offset: 0x0001C3A8
		public bool IsInitialized
		{
			get
			{
				return this._initialized;
			}
		}

		// Token: 0x06000EB9 RID: 3769 RVA: 0x000F4174 File Offset: 0x000F2374
		private DiplomacyManager()
		{
			this._warTracker = WarStatisticsTracker.Instance;
			this._allianceSystem = AllianceSystem.Instance;
			this._fatigueSystem = WarFatigueSystem.Instance;
			this._tradeAgreementSystem = TradeAgreementSystem.Instance;
			this._territoryTransferSystem = TerritoryTransferSystem.Instance;
			this._tributeSystem = TributeSystem.Instance;
			this._reparationsSystem = ReparationsSystem.Instance;
			this._storage = new DiplomacyStorage();
			this._lastDiplomaticUpdate = CampaignTime.Now;
			this._nextUpdateIntervalDays = this.GetRandomUpdateInterval();
		}

		// Token: 0x06000EBA RID: 3770 RVA: 0x000F4258 File Offset: 0x000F2458
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void Initialize()
		{
			bool initialized = this._initialized;
			if (initialized)
			{
				DiplomacyLogger.Instance.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1025889117));
				this._activeDiplomaticEvents.Clear();
				this._eventStatementSchedule.Clear();
				this._eventAnalysisSchedule.Clear();
				this._pendingStatements.Clear();
				this._pendingPlayerStatements.Clear();
				this._playerKingdomLastStatement.Clear();
				this._battleInitialTroops.Clear();
				this._initialized = false;
				DiplomacyLogger.Instance.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1440887354));
			}
			DiplomacyLogger.Instance.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1216819380));
			try
			{
				this._allianceSystem.Initialize();
				this._warTracker.Initialize();
				this._tradeAgreementSystem.Initialize();
				this._territoryTransferSystem.Initialize();
				this._tributeSystem.Initialize();
				this._reparationsSystem.Initialize();
				this.SynchronizeWars();
				List<DynamicEvent> list = this._storage.LoadDiplomaticEvents();
				bool flag = list != null && Enumerable.Any<DynamicEvent>(list);
				if (flag)
				{
					this._activeDiplomaticEvents.AddRange(list);
					DiplomacyLogger.Instance.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(507564234), list.Count));
				}
				List<DelayedPlayerStatement> list2 = this._storage.LoadPendingPlayerStatements();
				bool flag2 = list2 != null && Enumerable.Any<DelayedPlayerStatement>(list2);
				if (flag2)
				{
					this._pendingPlayerStatements.AddRange(list2);
					DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(777457248), list2.Count));
					foreach (DelayedPlayerStatement delayedPlayerStatement in list2)
					{
						DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1034298263), delayedPlayerStatement.PlayerKingdom.Name, delayedPlayerStatement.Action, delayedPlayerStatement.PublicationTime));
					}
				}
				else
				{
					DiplomacyLogger.Instance.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-991072848));
				}
				this.RestoreDiplomaticEventsFromDynamicEvents();
				bool flag3 = Enumerable.Any<DynamicEvent>(this._activeDiplomaticEvents);
				if (flag3)
				{
					this.RestoreEventSchedules();
				}
				bool flag4 = Enumerable.Any<DynamicEvent>(this._activeDiplomaticEvents) || (list2 != null && Enumerable.Any<DelayedPlayerStatement>(list2));
				bool flag5 = !flag4;
				if (flag5)
				{
					DiplomacyLogger.Instance.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1891609893));
					foreach (Kingdom kingdom in Enumerable.Where<Kingdom>(Kingdom.All, (Kingdom k) => !k.IsEliminated))
					{
						this._warTracker.InitializeKingdomStats(kingdom);
					}
				}
				else
				{
					DiplomacyLogger.Instance.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(662674420));
				}
				this.RegisterEvents();
				this._statementGenerator = new KingdomStatementGenerator(this);
				this._eventsAnalyzer = new DynamicEventsAnalyzer(this, AIInfluenceBehavior.Instance);
				this._playerAnalyzer = new PlayerStatementAnalyzer(this);
				this._initialized = true;
				DiplomacyLogger.Instance.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1631184388));
			}
			catch (Exception ex)
			{
				DiplomacyLogger.Instance.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1131881298) + ex.Message);
				DiplomacyLogger.Instance.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-474801656), <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1294813227), ex);
			}
		}

		// Token: 0x06000EBB RID: 3771 RVA: 0x000F4668 File Offset: 0x000F2868
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void RegisterEvents()
		{
			CampaignEvents.WarDeclared.AddNonSerializedListener(this, new Action<IFaction, IFaction, DeclareWarAction.DeclareWarDetail>(this.OnWarDeclared));
			CampaignEvents.MakePeace.AddNonSerializedListener(this, new Action<IFaction, IFaction, MakePeaceAction.MakePeaceDetail>(this.OnPeaceMade));
			CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, new Action(this.OnHourlyTick));
			CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, new Action(this.OnDailyTick));
			CampaignEvents.MapEventStarted.AddNonSerializedListener(this, new Action<MapEvent, PartyBase, PartyBase>(this.OnBattleStarted));
			CampaignEvents.MapEventEnded.AddNonSerializedListener(this, new Action<MapEvent>(this.OnBattleEnded));
			CampaignEvents.OnPrisonerTakenEvent.AddNonSerializedListener(this, new Action<FlattenedTroopRoster>(this.OnPrisonerTaken));
			CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, new Action<Hero, Hero, KillCharacterAction.KillCharacterActionDetail, bool>(this.OnHeroKilled));
			CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, new Action<Settlement, bool, Hero, Hero, Hero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail>(this.OnSettlementOwnerChanged));
			CampaignEvents.KingdomDestroyedEvent.AddNonSerializedListener(this, new Action<Kingdom>(this.OnKingdomDestroyed));
			DiplomacyLogger.Instance.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-253923889));
		}

		// Token: 0x06000EBC RID: 3772 RVA: 0x000F4780 File Offset: 0x000F2980
		[DebuggerStepThrough]
		public Task ProcessDiplomaticEvent(DynamicEvent diplomaticEvent)
		{
			DiplomacyManager.<ProcessDiplomaticEvent>d__30 <ProcessDiplomaticEvent>d__ = new DiplomacyManager.<ProcessDiplomaticEvent>d__30();
			<ProcessDiplomaticEvent>d__.<>t__builder = AsyncTaskMethodBuilder.Create();
			<ProcessDiplomaticEvent>d__.<>4__this = this;
			<ProcessDiplomaticEvent>d__.diplomaticEvent = diplomaticEvent;
			<ProcessDiplomaticEvent>d__.<>1__state = -1;
			<ProcessDiplomaticEvent>d__.<>t__builder.Start<DiplomacyManager.<ProcessDiplomaticEvent>d__30>(ref <ProcessDiplomaticEvent>d__);
			return <ProcessDiplomaticEvent>d__.<>t__builder.Task;
		}

		// Token: 0x06000EBD RID: 3773 RVA: 0x000F47CC File Offset: 0x000F29CC
		[DebuggerStepThrough]
		public Task<List<KingdomStatement>> GenerateKingdomStatements(DynamicEvent diplomaticEvent)
		{
			DiplomacyManager.<GenerateKingdomStatements>d__31 <GenerateKingdomStatements>d__ = new DiplomacyManager.<GenerateKingdomStatements>d__31();
			<GenerateKingdomStatements>d__.<>t__builder = AsyncTaskMethodBuilder<List<KingdomStatement>>.Create();
			<GenerateKingdomStatements>d__.<>4__this = this;
			<GenerateKingdomStatements>d__.diplomaticEvent = diplomaticEvent;
			<GenerateKingdomStatements>d__.<>1__state = -1;
			<GenerateKingdomStatements>d__.<>t__builder.Start<DiplomacyManager.<GenerateKingdomStatements>d__31>(ref <GenerateKingdomStatements>d__);
			return <GenerateKingdomStatements>d__.<>t__builder.Task;
		}

		// Token: 0x06000EBE RID: 3774 RVA: 0x000F4818 File Offset: 0x000F2A18
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void HandleKingdomStatement(KingdomStatement statement)
		{
			bool flag = statement == null;
			if (!flag)
			{
				DiplomacyLogger.Instance.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(137563794) + statement.KingdomId);
				try
				{
					Kingdom kingdom = Enumerable.FirstOrDefault<Kingdom>(Kingdom.All, (Kingdom k) => k.StringId == statement.KingdomId);
					bool flag2 = kingdom == null;
					if (flag2)
					{
						DiplomacyLogger.Instance.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-302438586) + statement.KingdomId);
					}
					else
					{
						List<DiplomaticAction> list;
						if (statement.Actions == null || !Enumerable.Any<DiplomaticAction>(statement.Actions))
						{
							(list = new List<DiplomaticAction>()).Add(statement.Action);
						}
						else
						{
							list = statement.Actions;
						}
						List<DiplomaticAction> list2 = list;
						DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1763497198), list2.Count, string.Join<DiplomaticAction>(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-234543546), list2)));
						foreach (DiplomaticAction diplomaticAction in list2)
						{
							bool flag3 = diplomaticAction == DiplomaticAction.None;
							if (!flag3)
							{
								this.ExecuteDiplomaticAction(new DiplomaticActionInfo
								{
									Action = diplomaticAction,
									SourceKingdomId = statement.KingdomId,
									TargetKingdomId = statement.TargetKingdomId,
									TargetClanId = statement.TargetClanId,
									Reason = (statement.Reason ?? statement.StatementText),
									SettlementId = statement.SettlementId,
									DailyTributeAmount = statement.DailyTributeAmount,
									TributeDurationDays = statement.TributeDurationDays,
									ReparationsAmount = statement.ReparationsAmount,
									TradeAgreementDurationYears = statement.TradeAgreementDurationYears
								});
							}
						}
					}
				}
				catch (Exception ex)
				{
					DiplomacyLogger.Instance.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1072348385) + ex.Message);
					DiplomacyLogger.Instance.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1703737129), <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-3777639), ex);
				}
			}
		}

		// Token: 0x06000EBF RID: 3775 RVA: 0x000F4ACC File Offset: 0x000F2CCC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void ExecuteDiplomaticAction(DiplomaticActionInfo actionInfo)
		{
			bool flag = actionInfo == null;
			if (!flag)
			{
				Kingdom kingdom = Enumerable.FirstOrDefault<Kingdom>(Kingdom.All, (Kingdom k) => k.StringId == actionInfo.SourceKingdomId);
				bool flag2 = actionInfo.Action != DiplomaticAction.ExpelClan;
				Kingdom kingdom2 = flag2 ? Enumerable.FirstOrDefault<Kingdom>(Kingdom.All, (Kingdom k) => k.StringId == actionInfo.TargetKingdomId) : null;
				bool flag3 = kingdom == null;
				if (flag3)
				{
					DiplomacyLogger.Instance.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-28536708));
				}
				else
				{
					bool flag4 = flag2 && kingdom2 == null;
					if (flag4)
					{
						DiplomacyLogger.Instance.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-2078206479));
					}
					else
					{
						string message = flag2 ? string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(921816001), actionInfo.Action, kingdom.Name, kingdom2.Name) : string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-919484922), actionInfo.Action, kingdom.Name);
						DiplomacyLogger.Instance.Log(message);
						switch (actionInfo.Action)
						{
						case DiplomaticAction.DeclareWar:
						{
							bool flag5 = FactionManager.IsAtWarAgainstFaction(kingdom, kingdom2);
							if (flag5)
							{
								DiplomacyLogger.Instance.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1983134125), kingdom.Name, kingdom2.Name));
								return;
							}
							bool flag6 = this._allianceSystem.AreAllied(kingdom, kingdom2);
							if (flag6)
							{
								DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1935503851), kingdom.Name, kingdom2.Name));
							}
							this.DeclareWar(kingdom, kingdom2, actionInfo.Reason);
							return;
						}
						case DiplomaticAction.ProposePeace:
						{
							bool flag7 = !FactionManager.IsAtWarAgainstFaction(kingdom, kingdom2);
							if (flag7)
							{
								DiplomacyLogger.Instance.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-48410212), kingdom.Name, kingdom2.Name));
								return;
							}
							DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1903474215), kingdom.Name, kingdom2.Name));
							return;
						}
						case DiplomaticAction.ProposeAlliance:
						{
							bool flag8 = this._allianceSystem.AreAllied(kingdom, kingdom2);
							if (flag8)
							{
								DiplomacyLogger.Instance.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1775924686), kingdom.Name, kingdom2.Name));
								return;
							}
							DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(230604873), kingdom.Name, kingdom2.Name));
							return;
						}
						case DiplomaticAction.AcceptPeace:
						{
							bool flag9 = !FactionManager.IsAtWarAgainstFaction(kingdom, kingdom2);
							if (flag9)
							{
								DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-593526768), kingdom.Name, kingdom2.Name));
								return;
							}
							this.MakePeace(kingdom, kingdom2, actionInfo.Reason);
							return;
						}
						case DiplomaticAction.AcceptAlliance:
						{
							bool flag10 = this._allianceSystem.AreAllied(kingdom, kingdom2);
							if (flag10)
							{
								DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-709757008), kingdom.Name, kingdom2.Name));
								return;
							}
							this._allianceSystem.CreateAlliance(kingdom, kingdom2);
							bool flag11 = !string.IsNullOrEmpty(actionInfo.Reason);
							if (flag11)
							{
								this._warTracker.SaveDiplomaticReason(kingdom, kingdom2, <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(229617868), actionInfo.Reason, actionInfo.Reason);
							}
							return;
						}
						case DiplomaticAction.BreakAlliance:
						{
							bool flag12 = !this._allianceSystem.AreAllied(kingdom, kingdom2);
							if (flag12)
							{
								DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-547861079), kingdom.Name, kingdom2.Name));
								return;
							}
							this._allianceSystem.BreakAlliance(kingdom, kingdom2);
							bool flag13 = !string.IsNullOrEmpty(actionInfo.Reason);
							if (flag13)
							{
								this._warTracker.SaveDiplomaticReason(kingdom, kingdom2, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1811110804), actionInfo.Reason, actionInfo.Reason);
							}
							return;
						}
						case DiplomaticAction.ProposeTradeAgreement:
							DiplomacyLogger.Instance.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-771928775), kingdom.Name, kingdom2.Name));
							return;
						case DiplomaticAction.AcceptTradeAgreement:
						{
							bool flag14 = this._tradeAgreementSystem.CanMakeMoreAgreements(kingdom) && this._tradeAgreementSystem.CanMakeMoreAgreements(kingdom2);
							if (flag14)
							{
								float durationYears = (actionInfo.TradeAgreementDurationYears > 0f) ? actionInfo.TradeAgreementDurationYears : 1f;
								this._tradeAgreementSystem.CreateTradeAgreement(kingdom, kingdom2, durationYears);
							}
							else
							{
								DiplomacyLogger.Instance.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(744389577));
							}
							return;
						}
						case DiplomaticAction.RejectTradeAgreement:
							DiplomacyLogger.Instance.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-704678843), kingdom.Name));
							return;
						case DiplomaticAction.EndTradeAgreement:
							this._tradeAgreementSystem.EndTradeAgreement(kingdom, kingdom2);
							return;
						case DiplomaticAction.TransferTerritory:
						{
							bool flag15 = !string.IsNullOrEmpty(actionInfo.SettlementId);
							if (flag15)
							{
								this._territoryTransferSystem.TransferSettlementById(actionInfo.SettlementId, kingdom, kingdom2, actionInfo.Reason ?? <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(315204856));
							}
							else
							{
								DiplomacyLogger.Instance.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(2077196117));
							}
							return;
						}
						case DiplomaticAction.DemandTerritory:
							DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(624958359), kingdom.Name, kingdom2.Name));
							return;
						case DiplomaticAction.DemandTribute:
						{
							bool flag16 = actionInfo.DailyTributeAmount > 0 && actionInfo.TributeDurationDays > 0;
							if (flag16)
							{
								this._tributeSystem.EstablishTribute(kingdom2, kingdom, actionInfo.DailyTributeAmount, actionInfo.TributeDurationDays, actionInfo.Reason ?? <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1403506802));
							}
							else
							{
								DiplomacyLogger.Instance.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1820156635));
							}
							return;
						}
						case DiplomaticAction.AcceptTribute:
							DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-275211825), kingdom2.Name, kingdom.Name));
							return;
						case DiplomaticAction.RejectTribute:
							DiplomacyLogger.Instance.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(2007514183), kingdom2.Name, kingdom.Name));
							return;
						case DiplomaticAction.EndTribute:
							this._tributeSystem.EndTribute(kingdom, kingdom2);
							return;
						case DiplomaticAction.DemandReparations:
						{
							bool flag17 = actionInfo.ReparationsAmount > 0;
							if (flag17)
							{
								this._reparationsSystem.DemandReparations(kingdom, kingdom2, actionInfo.ReparationsAmount, actionInfo.Reason ?? <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-283784974));
							}
							else
							{
								DiplomacyLogger.Instance.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1289791036));
							}
							return;
						}
						case DiplomaticAction.AcceptReparations:
						{
							ReparationDemand pendingDemand = this._reparationsSystem.GetPendingDemand(kingdom, kingdom2);
							bool flag18 = pendingDemand != null;
							if (flag18)
							{
								this._reparationsSystem.PayReparations(kingdom2, kingdom, pendingDemand.Amount, pendingDemand.Reason);
							}
							else
							{
								DiplomacyLogger.Instance.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1361545297));
							}
							return;
						}
						case DiplomaticAction.RejectReparations:
							this._reparationsSystem.RejectReparations(kingdom, kingdom2);
							return;
						case DiplomaticAction.ExpelClan:
						{
							DiplomacyLogger.Instance.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1843638542) + (actionInfo.TargetClanId ?? <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(375450415)));
							bool flag19 = !string.IsNullOrEmpty(actionInfo.TargetClanId);
							if (flag19)
							{
								string targetClanId = actionInfo.TargetClanId;
								bool flag20;
								if (targetClanId == <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1904506040))
								{
									Hero mainHero = Hero.MainHero;
									flag20 = (((mainHero != null) ? mainHero.Clan : null) != null);
								}
								else
								{
									flag20 = false;
								}
								bool flag21 = flag20;
								if (flag21)
								{
									targetClanId = Hero.MainHero.Clan.StringId;
									DiplomacyLogger.Instance.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1444595548) + targetClanId);
								}
								Clan clan = Enumerable.FirstOrDefault<Clan>(Clan.All, (Clan c) => c.StringId == targetClanId);
								DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-2131602103), new object[]
								{
									targetClanId,
									clan != null,
									(clan != null) ? clan.Name : null,
									((clan != null) ? clan.Kingdom : null) == kingdom
								}));
								bool flag22 = clan != null && clan.Kingdom == kingdom;
								if (flag22)
								{
									bool flag23 = clan == kingdom.RulingClan;
									if (flag23)
									{
										DiplomacyLogger.Instance.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-316208524));
									}
									else
									{
										ExpelledClanSystem.Instance.BanClan(kingdom, clan, actionInfo.Reason ?? <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1820530385));
										ChangeKingdomAction.ApplyByLeaveKingdom(clan, true);
										DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1058802719), new object[]
										{
											clan.Name,
											targetClanId,
											kingdom.Name,
											(kingdom.Leader == Hero.MainHero) ? <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-967257747) : <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(800599449)
										}));
									}
								}
								else
								{
									DiplomacyLogger.Instance.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-2084466037), new object[]
									{
										actionInfo.TargetClanId,
										targetClanId,
										clan != null,
										((clan != null) ? clan.Kingdom : null) == kingdom
									}));
									bool flag24;
									if (actionInfo.TargetClanId == <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(985156191))
									{
										Hero mainHero2 = Hero.MainHero;
										flag24 = (((mainHero2 != null) ? mainHero2.Clan : null) != null);
									}
									else
									{
										flag24 = false;
									}
									bool flag25 = flag24;
									if (flag25)
									{
										Clan clan2 = Hero.MainHero.Clan;
										bool flag26 = clan2.Kingdom == kingdom && clan2 != kingdom.RulingClan;
										if (flag26)
										{
											DiplomacyLogger.Instance.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(701850287), clan2.Name, clan2.StringId));
											ExpelledClanSystem.Instance.BanClan(kingdom, clan2, actionInfo.Reason ?? <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1794928789));
											ChangeKingdomAction.ApplyByLeaveKingdom(clan2, true);
											DiplomacyLogger.Instance.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-677313435), clan2.Name, clan2.StringId, kingdom.Name));
										}
									}
								}
							}
							else
							{
								DiplomacyLogger.Instance.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1358526516));
							}
							return;
						}
						case DiplomaticAction.GrantFief:
						{
							bool flag27 = !string.IsNullOrEmpty(actionInfo.SettlementId);
							if (flag27)
							{
								this._territoryTransferSystem.TransferSettlementToClanById(actionInfo.SettlementId, Clan.PlayerClan, actionInfo.Reason ?? <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1635369460));
							}
							else
							{
								DiplomacyLogger.Instance.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-103852583));
							}
							return;
						}
						case DiplomaticAction.ReceiveFief:
						{
							bool flag28 = !string.IsNullOrEmpty(actionInfo.SettlementId) && !string.IsNullOrEmpty(actionInfo.TargetClanId);
							if (flag28)
							{
								Clan clan3 = Enumerable.FirstOrDefault<Clan>(Clan.All, (Clan c) => c.StringId == actionInfo.TargetClanId);
								bool flag29 = clan3 != null;
								if (flag29)
								{
									this._territoryTransferSystem.TransferSettlementToClanById(actionInfo.SettlementId, clan3, actionInfo.Reason ?? <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1864379543));
								}
								else
								{
									DiplomacyLogger.Instance.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1792405037) + actionInfo.TargetClanId + <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1305175162));
								}
							}
							else
							{
								DiplomacyLogger.Instance.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1964961128));
							}
							return;
						}
						}
						DiplomacyLogger.Instance.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(874310162), actionInfo.Action));
					}
				}
			}
		}

		// Token: 0x06000EC0 RID: 3776 RVA: 0x000F580C File Offset: 0x000F3A0C
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void DeclareWar(Kingdom kingdom1, Kingdom kingdom2, string reason)
		{
			bool flag = FactionManager.IsAtWarAgainstFaction(kingdom1, kingdom2);
			if (flag)
			{
				DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-225970749), kingdom1.Name, kingdom2.Name));
			}
			else
			{
				bool flag2 = this._allianceSystem.AreAllied(kingdom1, kingdom2);
				if (flag2)
				{
					DiplomacyLogger.Instance.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1505418469), kingdom1.Name, kingdom2.Name));
					this._allianceSystem.BreakAlliance(kingdom1, kingdom2);
					DiplomacyLogger.Instance.LogDiplomaticAction(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-157837262), kingdom1.StringId, kingdom2.StringId, <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-2047129498));
				}
				try
				{
					DiplomacyPatches.WithBypass(delegate
					{
						DeclareWarAction.ApplyByDefault(kingdom1, kingdom2);
					});
					this._warTracker.TrackWarStart(kingdom1, kingdom2);
					bool flag3 = !string.IsNullOrEmpty(reason);
					if (flag3)
					{
						this._warTracker.SaveDiplomaticReason(kingdom1, kingdom2, <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(919999426), reason, reason);
					}
					DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-2108800436), kingdom1.Name, kingdom2.Name));
					DiplomacyLogger.Instance.LogDiplomaticAction(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1565816585), kingdom1.StringId, kingdom2.StringId, reason ?? <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1583458293));
				}
				catch (Exception ex)
				{
					DiplomacyLogger.Instance.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1198628681) + ex.Message);
				}
			}
		}

		// Token: 0x06000EC1 RID: 3777 RVA: 0x000F5A2C File Offset: 0x000F3C2C
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void MakePeace(Kingdom kingdom1, Kingdom kingdom2, string reason)
		{
			bool flag = !FactionManager.IsAtWarAgainstFaction(kingdom1, kingdom2);
			if (flag)
			{
				DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-170914378), kingdom1.Name, kingdom2.Name));
			}
			else
			{
				try
				{
					DiplomacyPatches.WithBypass(delegate
					{
						MakePeaceAction.Apply(kingdom1, kingdom2);
					});
					this._warTracker.OnWarEnded(kingdom1, kingdom2);
					bool flag2 = !string.IsNullOrEmpty(reason);
					if (flag2)
					{
						this._warTracker.SaveDiplomaticReason(kingdom1, kingdom2, <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1273982816), reason, reason);
					}
					DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1631972990), kingdom1.Name, kingdom2.Name));
					DiplomacyLogger.Instance.LogDiplomaticAction(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-2062348427), kingdom1.StringId, kingdom2.StringId, reason ?? <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1206346121));
				}
				catch (Exception ex)
				{
					DiplomacyLogger.Instance.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1470864195) + ex.Message);
				}
			}
		}

		// Token: 0x06000EC2 RID: 3778 RVA: 0x000F5BAC File Offset: 0x000F3DAC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void UpdateWarStatistics()
		{
			try
			{
				this._warTracker.UpdateWarStatistics();
				DiplomacyLogger.Instance.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-520876670));
			}
			catch (Exception ex)
			{
				DiplomacyLogger.Instance.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-254712491) + ex.Message);
			}
		}

		// Token: 0x06000EC3 RID: 3779 RVA: 0x000F5C18 File Offset: 0x000F3E18
		[MethodImpl(MethodImplOptions.NoInlining)]
		private List<Kingdom> GetParticipatingKingdoms(DynamicEvent diplomaticEvent)
		{
			List<Kingdom> list = new List<Kingdom>();
			DiplomacyLogger.Instance.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1074261575) + string.Join(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-232295527), diplomaticEvent.ParticipatingKingdoms ?? new List<string>()));
			bool flag = diplomaticEvent.ParticipatingKingdoms != null && Enumerable.Any<string>(diplomaticEvent.ParticipatingKingdoms);
			if (flag)
			{
				using (List<string>.Enumerator enumerator = diplomaticEvent.ParticipatingKingdoms.GetEnumerator())
				{
					while (enumerator.MoveNext())
					{
						string kingdomId = enumerator.Current;
						Kingdom kingdom = Enumerable.FirstOrDefault<Kingdom>(Kingdom.All, (Kingdom k) => k.StringId == kingdomId);
						bool flag2 = kingdom != null && !kingdom.IsEliminated;
						if (flag2)
						{
							list.Add(kingdom);
							DiplomacyLogger.Instance.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(322628002), kingdom.Name, kingdomId));
						}
						else
						{
							DiplomacyLogger.Instance.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1095035779) + kingdomId);
						}
					}
				}
			}
			else
			{
				DiplomacyLogger.Instance.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-2069785994));
			}
			return list;
		}

		// Token: 0x06000EC4 RID: 3780 RVA: 0x000F5D88 File Offset: 0x000F3F88
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void OnWarDeclared(IFaction faction1, IFaction faction2, DeclareWarAction.DeclareWarDetail detail)
		{
			Kingdom kingdom = faction1 as Kingdom;
			Kingdom kingdom2;
			bool flag;
			if (kingdom != null)
			{
				kingdom2 = (faction2 as Kingdom);
				flag = (kingdom2 != null);
			}
			else
			{
				flag = false;
			}
			bool flag2 = flag;
			if (flag2)
			{
				this._warTracker.TrackWarStart(kingdom, kingdom2);
				this._tradeAgreementSystem.OnWarDeclared(kingdom, kingdom2);
				this._tributeSystem.OnWarDeclared(kingdom, kingdom2);
				DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(447816748), kingdom.Name, kingdom2.Name));
			}
		}

		// Token: 0x06000EC5 RID: 3781 RVA: 0x000F5E08 File Offset: 0x000F4008
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void OnPeaceMade(IFaction faction1, IFaction faction2, MakePeaceAction.MakePeaceDetail detail)
		{
			Kingdom kingdom = faction1 as Kingdom;
			Kingdom kingdom2;
			bool flag;
			if (kingdom != null)
			{
				kingdom2 = (faction2 as Kingdom);
				flag = (kingdom2 != null);
			}
			else
			{
				flag = false;
			}
			bool flag2 = flag;
			if (flag2)
			{
				this._warTracker.OnWarEnded(kingdom, kingdom2);
				DiplomacyLogger.Instance.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(688474948), kingdom.Name, kingdom2.Name));
			}
		}

		// Token: 0x06000EC6 RID: 3782 RVA: 0x000F5E6C File Offset: 0x000F406C
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void OnBattleStarted(MapEvent mapEvent, PartyBase attackerParty, PartyBase defenderParty)
		{
			bool flag = mapEvent == null;
			if (!flag)
			{
				try
				{
					int totalTroopCount = GameVersionCompatibility.GetTotalTroopCount(mapEvent.PartiesOnSide(BattleSideEnum.Attacker));
					int totalTroopCount2 = GameVersionCompatibility.GetTotalTroopCount(mapEvent.PartiesOnSide(BattleSideEnum.Defender));
					this._battleInitialTroops[mapEvent] = new ValueTuple<int, int>(totalTroopCount, totalTroopCount2);
				}
				catch (Exception ex)
				{
					DiplomacyLogger.Instance.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1069800400) + ex.Message);
				}
			}
		}

		// Token: 0x06000EC7 RID: 3783 RVA: 0x000F5EF0 File Offset: 0x000F40F0
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void OnBattleEnded(MapEvent mapEvent)
		{
			bool flag = mapEvent == null;
			if (!flag)
			{
				try
				{
					ValueTuple<int, int> valueTuple;
					bool flag2 = !this._battleInitialTroops.TryGetValue(mapEvent, out valueTuple);
					if (!flag2)
					{
						int item = valueTuple.Item1;
						int item2 = valueTuple.Item2;
						int totalTroopCount = GameVersionCompatibility.GetTotalTroopCount(mapEvent.PartiesOnSide(BattleSideEnum.Attacker));
						int totalTroopCount2 = GameVersionCompatibility.GetTotalTroopCount(mapEvent.PartiesOnSide(BattleSideEnum.Defender));
						int item3 = Math.Max(0, item - totalTroopCount);
						int item4 = Math.Max(0, item2 - totalTroopCount2);
						this._battleInitialTroops.Remove(mapEvent);
						Kingdom kingdom;
						Kingdom kingdom2;
						bool flag3 = !DiplomacyHelpers.VerifyBattleSides(mapEvent.AttackerSide.LeaderParty, mapEvent.DefenderSide.LeaderParty, out kingdom, out kingdom2);
						if (!flag3)
						{
							Dictionary<Kingdom, ValueTuple<int, Kingdom>> dictionary = new Dictionary<Kingdom, ValueTuple<int, Kingdom>>();
							bool flag4 = kingdom != null;
							if (flag4)
							{
								dictionary[kingdom] = new ValueTuple<int, Kingdom>(item3, kingdom2);
							}
							bool flag5 = kingdom2 != null;
							if (flag5)
							{
								dictionary[kingdom2] = new ValueTuple<int, Kingdom>(item4, kingdom);
							}
							foreach (KeyValuePair<Kingdom, ValueTuple<int, Kingdom>> keyValuePair in dictionary)
							{
								Kingdom key = keyValuePair.Key;
								int item5 = keyValuePair.Value.Item1;
								Kingdom item6 = keyValuePair.Value.Item2;
								bool flag6 = item5 > 0;
								if (flag6)
								{
									this._warTracker.UpdateCasualties(key, item5, item6);
									DiplomacyLogger.Instance.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(923272142), key.Name, item5) + ((item6 != null) ? string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1138124761), item6.Name) : ""));
								}
							}
							bool flag7 = mapEvent.WinningSide != BattleSideEnum.None && mapEvent.DefeatedSide != BattleSideEnum.None;
							if (flag7)
							{
								MapEventSide mapEventSide = mapEvent.GetMapEventSide(mapEvent.WinningSide);
								MapEventSide mapEventSide2 = mapEvent.GetMapEventSide(mapEvent.DefeatedSide);
								Kingdom kingdom3;
								Kingdom kingdom4;
								bool flag8 = DiplomacyHelpers.VerifyBattleSides(mapEventSide.LeaderParty, mapEventSide2.LeaderParty, out kingdom3, out kingdom4);
								if (flag8)
								{
									foreach (MapEventParty mapEventParty in mapEventSide2.Parties)
									{
										bool flag9 = mapEventParty.Party.IsMobile && mapEventParty.Party.MobileParty != null && mapEventParty.Party.MobileParty.IsCaravan;
										if (flag9)
										{
											Hero owner = mapEventParty.Party.Owner;
											Kingdom kingdom5;
											if (owner == null)
											{
												kingdom5 = null;
											}
											else
											{
												Clan clan = owner.Clan;
												kingdom5 = ((clan != null) ? clan.Kingdom : null);
											}
											Kingdom kingdom6 = kingdom5;
											bool flag10 = kingdom6 != null && kingdom6 == kingdom4;
											if (flag10)
											{
												this._warTracker.IncrementCaravansDestroyed(kingdom4);
												DiplomacyLogger.Instance.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1799852791), kingdom4.Name, kingdom3.Name));
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
					DiplomacyLogger.Instance.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(490795704) + ex.Message);
				}
			}
		}

		// Token: 0x06000EC8 RID: 3784 RVA: 0x000F627C File Offset: 0x000F447C
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void OnPrisonerTaken(FlattenedTroopRoster roster)
		{
			bool flag = roster == null;
			if (!flag)
			{
				try
				{
					foreach (FlattenedTroopRosterElement flattenedTroopRosterElement in roster)
					{
						bool flag2 = flattenedTroopRosterElement.Troop != null && flattenedTroopRosterElement.Troop.IsHero;
						if (flag2)
						{
							Hero heroObject = flattenedTroopRosterElement.Troop.HeroObject;
							bool flag3 = heroObject != null && heroObject.IsLord;
							if (flag3)
							{
								PartyBase partyBelongedToAsPrisoner = heroObject.PartyBelongedToAsPrisoner;
								Hero hero = (partyBelongedToAsPrisoner != null) ? partyBelongedToAsPrisoner.LeaderHero : null;
								Kingdom kingdom;
								Kingdom kingdom2;
								bool flag4 = hero != null && DiplomacyHelpers.VerifyHeroEventSides(hero, heroObject, out kingdom, out kingdom2);
								if (flag4)
								{
									bool flag5 = FactionManager.IsAtWarAgainstFaction(kingdom2, kingdom);
									if (flag5)
									{
										this._warTracker.IncrementLordsCaptured(kingdom2);
										DiplomacyLogger.Instance.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(266757363), heroObject.Name, kingdom2.Name, kingdom.Name));
									}
								}
							}
						}
					}
				}
				catch (Exception ex)
				{
					DiplomacyLogger.Instance.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1383736662) + ex.Message);
				}
			}
		}

		// Token: 0x06000EC9 RID: 3785 RVA: 0x000F63D8 File Offset: 0x000F45D8
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
		{
			bool flag = victim == null || !victim.IsLord;
			if (!flag)
			{
				try
				{
					Kingdom kingdom;
					Kingdom kingdom2;
					bool flag2 = killer != null && DiplomacyHelpers.VerifyHeroEventSides(killer, victim, out kingdom, out kingdom2);
					if (flag2)
					{
						bool flag3 = FactionManager.IsAtWarAgainstFaction(kingdom2, kingdom);
						if (flag3)
						{
							bool flag4 = detail != KillCharacterAction.KillCharacterActionDetail.None && detail != KillCharacterAction.KillCharacterActionDetail.DiedInLabor && detail != KillCharacterAction.KillCharacterActionDetail.DiedOfOldAge;
							if (flag4)
							{
								this._warTracker.IncrementLordsKilled(kingdom2);
								DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(928489220), new object[]
								{
									victim.Name,
									kingdom2.Name,
									kingdom.Name,
									detail
								}));
							}
						}
					}
				}
				catch (Exception ex)
				{
					DiplomacyLogger.Instance.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-177057208) + ex.Message);
				}
			}
		}

		// Token: 0x06000ECA RID: 3786 RVA: 0x000F64D0 File Offset: 0x000F46D0
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
		{
			bool flag = settlement == null;
			if (!flag)
			{
				try
				{
					Kingdom kingdom;
					Kingdom kingdom2;
					bool flag2 = newOwner != null && oldOwner != null && DiplomacyHelpers.VerifyHeroEventSides(newOwner, oldOwner, out kingdom, out kingdom2);
					if (flag2)
					{
						bool flag3 = FactionManager.IsAtWarAgainstFaction(kingdom2, kingdom);
						if (flag3)
						{
							this._warTracker.IncrementSettlementsLost(kingdom2);
							DiplomacyLogger.Instance.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1497452428), settlement.Name, kingdom2.Name, kingdom.Name));
						}
					}
				}
				catch (Exception ex)
				{
					DiplomacyLogger.Instance.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-50958197) + ex.Message);
				}
			}
		}

		// Token: 0x06000ECB RID: 3787 RVA: 0x000F6590 File Offset: 0x000F4790
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void OnKingdomDestroyed(Kingdom kingdom)
		{
			bool flag = kingdom == null;
			if (!flag)
			{
				try
				{
					this._tradeAgreementSystem.OnKingdomDestroyed(kingdom);
					this._tributeSystem.OnKingdomDestroyed(kingdom);
					this._reparationsSystem.OnKingdomDestroyed(kingdom);
					DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(831459826), kingdom.Name));
				}
				catch (Exception ex)
				{
					DiplomacyLogger.Instance.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(957617449) + ex.Message);
				}
			}
		}

		// Token: 0x06000ECC RID: 3788 RVA: 0x000F6630 File Offset: 0x000F4830
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void OnHourlyTick()
		{
			bool flag = !GlobalSettings<ModSettings>.Instance.EnableDiplomacy || !this._initialized;
			if (flag)
			{
				bool flag2 = GlobalSettings<ModSettings>.Instance.EnableDiplomacy && !this._initialized;
				if (flag2)
				{
					DiplomacyLogger.Instance.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(160407259));
				}
			}
			else
			{
				this._warTracker.UpdateWarStatistics();
				this.ProcessScheduledStatements();
				this.ProcessScheduledAnalyses();
				this.ProcessPendingPlayerStatements();
				this.CheckPlayerKingdomTimeouts();
			}
		}

		// Token: 0x06000ECD RID: 3789 RVA: 0x000F66BC File Offset: 0x000F48BC
		public bool HasActiveDiplomaticEvents()
		{
			return Enumerable.Any<DynamicEvent>(this._activeDiplomaticEvents);
		}

		// Token: 0x06000ECE RID: 3790 RVA: 0x000F66DC File Offset: 0x000F48DC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void ClearActiveDiplomaticEvents()
		{
			try
			{
				int count = this._activeDiplomaticEvents.Count;
				this._activeDiplomaticEvents.Clear();
				this._storage.SaveDiplomaticEvents(this._activeDiplomaticEvents);
				DiplomacyLogger.Instance.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(211781973), count));
			}
			catch (Exception ex)
			{
				DiplomacyLogger.Instance.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-700165618) + ex.Message);
			}
		}

		// Token: 0x06000ECF RID: 3791 RVA: 0x000F6774 File Offset: 0x000F4974
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void OnDailyTick()
		{
			bool flag = !GlobalSettings<ModSettings>.Instance.EnableDiplomacy || !this._initialized;
			if (!flag)
			{
				this._allianceSystem.CleanupEliminatedKingdoms();
				this._warTracker.ApplyDailyFatigueRecovery();
				this._tradeAgreementSystem.OnDailyTick();
				this._tributeSystem.ProcessDailyPayments();
				this._reparationsSystem.OnDailyTick();
				this._territoryTransferSystem.CleanOldRecords();
				this._reparationsSystem.CleanOldRecords();
				DiplomaticStatementsStorage.Instance.GetRecentStatements(-1, 15);
				try
				{
					this._allianceSystem.SynchronizeAlliances();
					this.SynchronizeWars();
					this._tradeAgreementSystem.SynchronizeTradeAgreements();
				}
				catch (Exception ex)
				{
					DiplomacyLogger.Instance.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(707967870) + ex.Message);
				}
			}
		}

		// Token: 0x06000ED0 RID: 3792 RVA: 0x000F6860 File Offset: 0x000F4A60
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void SynchronizeWars()
		{
			DiplomacyLogger.Instance.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(681505308));
			HashSet<string> hashSet = new HashSet<string>();
			int num = 0;
			int num2 = 0;
			using (IEnumerator<Kingdom> enumerator = Enumerable.Where<Kingdom>(Kingdom.All, (Kingdom k) => !k.IsEliminated).GetEnumerator())
			{
				while (enumerator.MoveNext())
				{
					Kingdom kingdom1 = enumerator.Current;
					IEnumerable<Kingdom> all = Kingdom.All;
					Func<Kingdom, bool> func;
					Func<Kingdom, bool> <>9__1;
					if ((func = <>9__1) == null)
					{
						func = (<>9__1 = ((Kingdom k) => !k.IsEliminated && k != kingdom1));
					}
					foreach (Kingdom kingdom in Enumerable.Where<Kingdom>(all, func))
					{
						string[] array = new string[]
						{
							kingdom1.StringId,
							kingdom.StringId
						};
						Array.Sort<string>(array);
						string text = string.Join(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(582880270), array);
						bool flag = hashSet.Contains(text);
						if (!flag)
						{
							hashSet.Add(text);
							bool flag2 = false;
							KingdomWarStats kingdomWarStats = this._warTracker.KingdomStats.ContainsKey(kingdom1.StringId) ? this._warTracker.KingdomStats[kingdom1.StringId] : null;
							bool flag3 = kingdomWarStats != null && kingdomWarStats.WarsAgainstKingdoms.ContainsKey(kingdom.StringId);
							if (flag3)
							{
								flag2 = true;
							}
							bool flag4 = FactionManager.IsAtWarAgainstFaction(kingdom1, kingdom);
							bool flag5 = flag2 && !flag4;
							if (flag5)
							{
								DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(74881943), kingdom1.Name, kingdom.Name));
								this.DeclareWar(kingdom1, kingdom, <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1746781364));
								num++;
							}
							else
							{
								bool flag6 = !flag2 && flag4;
								if (flag6)
								{
									DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1980986872), kingdom1.Name, kingdom.Name));
									this.MakePeace(kingdom1, kingdom, <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-914739876));
									num2++;
								}
							}
						}
					}
				}
			}
			DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(558013352), num, num2));
		}

		// Token: 0x06000ED1 RID: 3793 RVA: 0x0001E1B0 File Offset: 0x0001C3B0
		public WarStatisticsTracker GetWarTracker()
		{
			return this._warTracker;
		}

		// Token: 0x06000ED2 RID: 3794 RVA: 0x0001E1B8 File Offset: 0x0001C3B8
		public AllianceSystem GetAllianceSystem()
		{
			return this._allianceSystem;
		}

		// Token: 0x06000ED3 RID: 3795 RVA: 0x0001E1C0 File Offset: 0x0001C3C0
		public WarFatigueSystem GetFatigueSystem()
		{
			return this._fatigueSystem;
		}

		// Token: 0x06000ED4 RID: 3796 RVA: 0x0001E1C8 File Offset: 0x0001C3C8
		public TradeAgreementSystem GetTradeAgreementSystem()
		{
			return this._tradeAgreementSystem;
		}

		// Token: 0x06000ED5 RID: 3797 RVA: 0x0001E1D0 File Offset: 0x0001C3D0
		public TerritoryTransferSystem GetTerritoryTransferSystem()
		{
			return this._territoryTransferSystem;
		}

		// Token: 0x06000ED6 RID: 3798 RVA: 0x0001E1D8 File Offset: 0x0001C3D8
		public TributeSystem GetTributeSystem()
		{
			return this._tributeSystem;
		}

		// Token: 0x06000ED7 RID: 3799 RVA: 0x0001E1E0 File Offset: 0x0001C3E0
		public ReparationsSystem GetReparationsSystem()
		{
			return this._reparationsSystem;
		}

		// Token: 0x06000ED8 RID: 3800 RVA: 0x000F6B44 File Offset: 0x000F4D44
		public List<DynamicEvent> GetActiveDiplomaticEvents()
		{
			List<DynamicEvent> activeDiplomaticEvents = this._activeDiplomaticEvents;
			return ((activeDiplomaticEvents != null) ? Enumerable.ToList<DynamicEvent>(activeDiplomaticEvents) : null) ?? new List<DynamicEvent>();
		}

		// Token: 0x06000ED9 RID: 3801 RVA: 0x000F6B74 File Offset: 0x000F4D74
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void SaveAllData()
		{
			try
			{
				this._allianceSystem.SaveData();
				this._warTracker.SaveData();
				this._tradeAgreementSystem.SaveData();
				this._territoryTransferSystem.SaveData();
				this._tributeSystem.SaveData();
				this._reparationsSystem.SaveData();
				this._storage.SaveDiplomaticEvents(this._activeDiplomaticEvents);
				DiplomacyLogger.Instance.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(540371644));
			}
			catch (Exception ex)
			{
				DiplomacyLogger.Instance.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-547320606) + ex.Message);
			}
		}

		// Token: 0x06000EDA RID: 3802 RVA: 0x000F6C30 File Offset: 0x000F4E30
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void ClearCurrentSaveData()
		{
			try
			{
				DiplomacyLogger.Instance.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-262951587));
				this._storage.ClearCurrentSaveData();
				this._allianceSystem.Alliances.Clear();
				this._allianceSystem.AllianceTimes.Clear();
				this._warTracker.ClearAllData();
				this._activeDiplomaticEvents.Clear();
				DiplomaticStatementsStorage.Instance.ClearAllStatements();
				DiplomacyLogger.Instance.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-600887811));
			}
			catch (Exception ex)
			{
				DiplomacyLogger.Instance.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-243348166) + ex.Message);
			}
		}

		// Token: 0x06000EDB RID: 3803 RVA: 0x000F6CF8 File Offset: 0x000F4EF8
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void ProcessScheduledStatements()
		{
			List<string> list = new List<string>();
			foreach (KeyValuePair<string, CampaignTime> keyValuePair in Enumerable.ToList<KeyValuePair<string, CampaignTime>>(this._eventStatementSchedule))
			{
				bool flag = CampaignTime.Now >= keyValuePair.Value;
				if (flag)
				{
					DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-325489111), keyValuePair.Key, keyValuePair.Value, CampaignTime.Now));
					list.Add(keyValuePair.Key);
				}
			}
			using (List<string>.Enumerator enumerator2 = list.GetEnumerator())
			{
				while (enumerator2.MoveNext())
				{
					string eventId = enumerator2.Current;
					DynamicEvent diplomaticEvent = Enumerable.FirstOrDefault<DynamicEvent>(this._activeDiplomaticEvents, (DynamicEvent e) => e.Id == eventId);
					bool flag2 = diplomaticEvent != null;
					if (flag2)
					{
						DiplomacyLogger.Instance.Log(string.Concat(new string[]
						{
							<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(516249696),
							eventId,
							<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-877916182),
							diplomaticEvent.Type,
							<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-753296414),
							string.Join(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(672823355), diplomaticEvent.ParticipatingKingdoms ?? new List<string>())
						}));
						this._eventStatementSchedule.Remove(eventId);
						List<DelayedPlayerStatement> list2 = Enumerable.ToList<DelayedPlayerStatement>(Enumerable.Where<DelayedPlayerStatement>(this._pendingPlayerStatements, (DelayedPlayerStatement s) => s.PlayerKingdom != null && diplomaticEvent.ParticipatingKingdoms.Contains(s.PlayerKingdom.StringId)));
						bool flag3 = Enumerable.Any<DelayedPlayerStatement>(list2);
						if (flag3)
						{
							DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(390417126), eventId, list2.Count));
						}
						else
						{
							bool flag4 = this._pendingStatements.ContainsKey(eventId);
							if (flag4)
							{
								Kingdom kingdom = this._pendingStatements[eventId];
								this._pendingStatements.Remove(eventId);
								DiplomacyLogger.Instance.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(259089643), kingdom.Name));
								this.GenerateSingleStatementForEvent(diplomaticEvent, kingdom);
							}
							else
							{
								DiplomacyLogger.Instance.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-205400323));
								this.GenerateStatementsForEvent(diplomaticEvent);
							}
						}
					}
					else
					{
						DiplomacyLogger.Instance.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-2016997686) + eventId + <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-636265323));
						this._eventStatementSchedule.Remove(eventId);
					}
				}
			}
		}

		// Token: 0x06000EDC RID: 3804 RVA: 0x000F7030 File Offset: 0x000F5230
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void ScheduleNextStatement(string eventId, Kingdom kingdom, CampaignTime scheduledTime)
		{
			this._eventStatementSchedule[eventId] = scheduledTime;
			this._pendingStatements[eventId] = kingdom;
			DiplomacyLogger.Instance.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1659044997), kingdom.Name, eventId));
		}

		// Token: 0x06000EDD RID: 3805 RVA: 0x000F7080 File Offset: 0x000F5280
		public bool IsKingdomBusyWithStatements(string kingdomId)
		{
			foreach (KeyValuePair<string, Kingdom> keyValuePair in this._pendingStatements)
			{
				bool flag = keyValuePair.Value.StringId == kingdomId;
				if (flag)
				{
					return true;
				}
			}
			foreach (KeyValuePair<string, CampaignTime> keyValuePair2 in this._eventStatementSchedule)
			{
				bool flag2 = keyValuePair2.Value <= CampaignTime.Now && this._pendingStatements.ContainsKey(keyValuePair2.Key) && this._pendingStatements[keyValuePair2.Key].StringId == kingdomId;
				if (flag2)
				{
					return true;
				}
			}
			return false;
		}

		// Token: 0x06000EDE RID: 3806 RVA: 0x000F7188 File Offset: 0x000F5388
		[DebuggerStepThrough]
		private Task GenerateSingleStatementForEvent(DynamicEvent diplomaticEvent, Kingdom kingdom)
		{
			DiplomacyManager.<GenerateSingleStatementForEvent>d__64 <GenerateSingleStatementForEvent>d__ = new DiplomacyManager.<GenerateSingleStatementForEvent>d__64();
			<GenerateSingleStatementForEvent>d__.<>t__builder = AsyncTaskMethodBuilder.Create();
			<GenerateSingleStatementForEvent>d__.<>4__this = this;
			<GenerateSingleStatementForEvent>d__.diplomaticEvent = diplomaticEvent;
			<GenerateSingleStatementForEvent>d__.kingdom = kingdom;
			<GenerateSingleStatementForEvent>d__.<>1__state = -1;
			<GenerateSingleStatementForEvent>d__.<>t__builder.Start<DiplomacyManager.<GenerateSingleStatementForEvent>d__64>(ref <GenerateSingleStatementForEvent>d__);
			return <GenerateSingleStatementForEvent>d__.<>t__builder.Task;
		}

		// Token: 0x06000EDF RID: 3807 RVA: 0x000F71DC File Offset: 0x000F53DC
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void ProcessScheduledAnalyses()
		{
			List<string> list = new List<string>();
			using (List<KeyValuePair<string, CampaignTime>>.Enumerator enumerator = Enumerable.ToList<KeyValuePair<string, CampaignTime>>(this._eventAnalysisSchedule).GetEnumerator())
			{
				while (enumerator.MoveNext())
				{
					KeyValuePair<string, CampaignTime> kvp = enumerator.Current;
					DynamicEvent dynamicEvent = Enumerable.FirstOrDefault<DynamicEvent>(this._activeDiplomaticEvents, (DynamicEvent e) => e.Id == kvp.Key);
					bool flag = dynamicEvent != null && CampaignTime.Now >= kvp.Value && dynamicEvent.IsReadyForAnalysis();
					if (flag)
					{
						list.Add(kvp.Key);
					}
				}
			}
			using (List<string>.Enumerator enumerator2 = list.GetEnumerator())
			{
				while (enumerator2.MoveNext())
				{
					string eventId = enumerator2.Current;
					DynamicEvent diplomaticEvent = Enumerable.FirstOrDefault<DynamicEvent>(this._activeDiplomaticEvents, (DynamicEvent e) => e.Id == eventId);
					bool flag2 = diplomaticEvent != null;
					if (flag2)
					{
						List<DelayedPlayerStatement> list2 = Enumerable.ToList<DelayedPlayerStatement>(Enumerable.Where<DelayedPlayerStatement>(this._pendingPlayerStatements, (DelayedPlayerStatement s) => s.PlayerKingdom != null && diplomaticEvent.ParticipatingKingdoms.Contains(s.PlayerKingdom.StringId)));
						bool flag3 = Enumerable.Any<DelayedPlayerStatement>(list2);
						if (flag3)
						{
							DiplomacyLogger.Instance.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(541772075), eventId, list2.Count));
						}
						else
						{
							this._eventAnalysisSchedule.Remove(eventId);
							List<KingdomStatement> statements = Enumerable.ToList<KingdomStatement>(diplomaticEvent.KingdomStatements);
							this.AnalyzeAndExecuteDiplomaticActions(diplomaticEvent, statements);
						}
					}
				}
			}
		}

		// Token: 0x06000EE0 RID: 3808 RVA: 0x000F73B8 File Offset: 0x000F55B8
		[DebuggerStepThrough]
		private Task GenerateStatementsForEvent(DynamicEvent diplomaticEvent)
		{
			DiplomacyManager.<GenerateStatementsForEvent>d__66 <GenerateStatementsForEvent>d__ = new DiplomacyManager.<GenerateStatementsForEvent>d__66();
			<GenerateStatementsForEvent>d__.<>t__builder = AsyncTaskMethodBuilder.Create();
			<GenerateStatementsForEvent>d__.<>4__this = this;
			<GenerateStatementsForEvent>d__.diplomaticEvent = diplomaticEvent;
			<GenerateStatementsForEvent>d__.<>1__state = -1;
			<GenerateStatementsForEvent>d__.<>t__builder.Start<DiplomacyManager.<GenerateStatementsForEvent>d__66>(ref <GenerateStatementsForEvent>d__);
			return <GenerateStatementsForEvent>d__.<>t__builder.Task;
		}

		// Token: 0x06000EE1 RID: 3809 RVA: 0x000F7404 File Offset: 0x000F5604
		private int GetRandomUpdateInterval()
		{
			Random random = new Random();
			int statementGenerationIntervalDays = GlobalSettings<ModSettings>.Instance.StatementGenerationIntervalDays;
			return random.Next(1, statementGenerationIntervalDays + 1);
		}

		// Token: 0x06000EE2 RID: 3810 RVA: 0x000F7434 File Offset: 0x000F5634
		[DebuggerStepThrough]
		private Task AnalyzeAndExecuteDiplomaticActions(DynamicEvent diplomaticEvent, List<KingdomStatement> statements)
		{
			DiplomacyManager.<AnalyzeAndExecuteDiplomaticActions>d__68 <AnalyzeAndExecuteDiplomaticActions>d__ = new DiplomacyManager.<AnalyzeAndExecuteDiplomaticActions>d__68();
			<AnalyzeAndExecuteDiplomaticActions>d__.<>t__builder = AsyncTaskMethodBuilder.Create();
			<AnalyzeAndExecuteDiplomaticActions>d__.<>4__this = this;
			<AnalyzeAndExecuteDiplomaticActions>d__.diplomaticEvent = diplomaticEvent;
			<AnalyzeAndExecuteDiplomaticActions>d__.statements = statements;
			<AnalyzeAndExecuteDiplomaticActions>d__.<>1__state = -1;
			<AnalyzeAndExecuteDiplomaticActions>d__.<>t__builder.Start<DiplomacyManager.<AnalyzeAndExecuteDiplomaticActions>d__68>(ref <AnalyzeAndExecuteDiplomaticActions>d__);
			return <AnalyzeAndExecuteDiplomaticActions>d__.<>t__builder.Task;
		}

		// Token: 0x06000EE3 RID: 3811 RVA: 0x000F7488 File Offset: 0x000F5688
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void QueueDiplomaticEvent(DynamicEvent diplomaticEvent)
		{
			bool flag = diplomaticEvent == null;
			if (!flag)
			{
				bool flag2 = Enumerable.Any<DynamicEvent>(this._activeDiplomaticEvents, (DynamicEvent e) => e.Id == diplomaticEvent.Id) || Enumerable.Any<DynamicEvent>(this._queuedDiplomaticEvents, (DynamicEvent e) => e.Id == diplomaticEvent.Id);
				if (!flag2)
				{
					this._queuedDiplomaticEvents.Enqueue(diplomaticEvent);
					DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1886115450), diplomaticEvent.Id, this._queuedDiplomaticEvents.Count));
				}
			}
		}

		// Token: 0x06000EE4 RID: 3812 RVA: 0x000F7538 File Offset: 0x000F5738
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void TryStartNextQueuedDiplomaticEvent()
		{
			bool flag = this._queuedDiplomaticEvents.Count == 0;
			if (!flag)
			{
				DynamicEvent dynamicEvent = this._queuedDiplomaticEvents.Dequeue();
				DiplomacyLogger.Instance.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-111218245), dynamicEvent.Id, this._queuedDiplomaticEvents.Count));
				this.ProcessDiplomaticEvent(dynamicEvent);
			}
		}

		// Token: 0x06000EE5 RID: 3813 RVA: 0x000F75A0 File Offset: 0x000F57A0
		[DebuggerStepThrough]
		private Task ContinueDiplomaticNegotiations(DynamicEvent diplomaticEvent, List<string> priorityKingdoms = null)
		{
			DiplomacyManager.<ContinueDiplomaticNegotiations>d__71 <ContinueDiplomaticNegotiations>d__ = new DiplomacyManager.<ContinueDiplomaticNegotiations>d__71();
			<ContinueDiplomaticNegotiations>d__.<>t__builder = AsyncTaskMethodBuilder.Create();
			<ContinueDiplomaticNegotiations>d__.<>4__this = this;
			<ContinueDiplomaticNegotiations>d__.diplomaticEvent = diplomaticEvent;
			<ContinueDiplomaticNegotiations>d__.priorityKingdoms = priorityKingdoms;
			<ContinueDiplomaticNegotiations>d__.<>1__state = -1;
			<ContinueDiplomaticNegotiations>d__.<>t__builder.Start<DiplomacyManager.<ContinueDiplomaticNegotiations>d__71>(ref <ContinueDiplomaticNegotiations>d__);
			return <ContinueDiplomaticNegotiations>d__.<>t__builder.Task;
		}

		// Token: 0x06000EE6 RID: 3814 RVA: 0x000F75F4 File Offset: 0x000F57F4
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void ShowDiplomaticActionToPlayer(DiplomaticActionInfo action)
		{
			try
			{
				Kingdom kingdom = Enumerable.FirstOrDefault<Kingdom>(Kingdom.All, (Kingdom k) => k.StringId == action.SourceKingdomId);
				Kingdom kingdom2 = Enumerable.FirstOrDefault<Kingdom>(Kingdom.All, (Kingdom k) => k.StringId == action.TargetKingdomId);
				bool flag = kingdom == null || kingdom2 == null;
				if (!flag)
				{
					string actionDisplayText = this.GetActionDisplayText(action.Action);
					string text = string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1144473478), actionDisplayText, kingdom.Name, kingdom2.Name);
					Color actionColor = this.GetActionColor(action.Action);
				}
			}
			catch (Exception ex)
			{
				DiplomacyLogger.Instance.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1979407520) + ex.Message);
			}
		}

		// Token: 0x06000EE7 RID: 3815 RVA: 0x000F76D4 File Offset: 0x000F58D4
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void ShowEventUpdateToPlayer(DynamicEvent diplomaticEvent, string eventUpdate)
		{
			try
			{
				Color color = new Color(1f, 0.55f, 0f, 1f);
				InformationManager.DisplayMessage(new InformationMessage(eventUpdate, color));
			}
			catch (Exception ex)
			{
				DiplomacyLogger.Instance.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-397178988) + ex.Message);
			}
		}

		// Token: 0x06000EE8 RID: 3816 RVA: 0x000F7748 File Offset: 0x000F5948
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void ShowEventCompletionToPlayer(DynamicEvent diplomaticEvent)
		{
			try
			{
				string text = <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1201630187) + diplomaticEvent.Type;
			}
			catch (Exception ex)
			{
				DiplomacyLogger.Instance.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(115878642) + ex.Message);
			}
		}

		// Token: 0x06000EE9 RID: 3817 RVA: 0x000F77AC File Offset: 0x000F59AC
		[MethodImpl(MethodImplOptions.NoInlining)]
		private string GetActionDisplayText(DiplomaticAction action)
		{
			switch (action)
			{
			case DiplomaticAction.DeclareWar:
				return <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1914732674);
			case DiplomaticAction.ProposePeace:
				return <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1873057416);
			case DiplomaticAction.ProposeAlliance:
				return <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-2135266203);
			case DiplomaticAction.AcceptPeace:
				return <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(772141175);
			case DiplomaticAction.AcceptAlliance:
				return <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1847927687);
			case DiplomaticAction.BreakAlliance:
				return <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1245226284);
			}
			return <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1800620017);
		}

		// Token: 0x06000EEA RID: 3818 RVA: 0x000F7854 File Offset: 0x000F5A54
		private Color GetActionColor(DiplomaticAction action)
		{
			switch (action)
			{
			case DiplomaticAction.DeclareWar:
			case DiplomaticAction.BreakAlliance:
				return \u200D\u206E\u200B\u202E\u206A\u202A\u200E\u202E\u200E\u202E\u202A\u202B\u200C\u206F\u202A\u206E\u206E\u200F\u206B\u202D\u200C\u200F\u206E\u200C\u206E\u202E\u200B\u200D\u202D\u202E\u206D\u206E\u202B\u200F\u200C\u206A\u202A\u202C\u206D\u202E.\u202C\u200D\u202E\u200D\u206F\u206E\u206C\u202E\u206A\u206C\u206F\u200F\u206F\u200C\u206A\u206D\u206B\u200B\u206B\u202C\u202A\u202E\u200B\u200B\u206B\u200D\u200D\u202B\u206F\u202E\u202E\u202E\u200F\u200C\u202A\u202D\u206A\u202D\u200F\u200C\u202E;
			case DiplomaticAction.ProposePeace:
			case DiplomaticAction.AcceptPeace:
				return \u200D\u206E\u200B\u202E\u206A\u202A\u200E\u202E\u200E\u202E\u202A\u202B\u200C\u206F\u202A\u206E\u206E\u200F\u206B\u202D\u200C\u200F\u206E\u200C\u206E\u202E\u200B\u200D\u202D\u202E\u206D\u206E\u202B\u200F\u200C\u206A\u202A\u202C\u206D\u202E.\u200C\u206B\u200F\u206B\u206A\u206C\u206D\u202D\u202E\u200F\u206B\u202D\u200F\u202A\u202D\u206D\u206A\u206D\u200D\u202A\u206F\u206C\u200B\u202B\u200D\u206C\u200E\u200F\u206D\u206C\u202D\u206B\u200F\u206E\u200D\u200F\u200C\u206F\u202C\u206F\u202E;
			case DiplomaticAction.ProposeAlliance:
			case DiplomaticAction.AcceptAlliance:
				return Colors.Blue;
			}
			return Colors.White;
		}

		// Token: 0x06000EEB RID: 3819 RVA: 0x000F78B4 File Offset: 0x000F5AB4
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void ApplyRelationChange(RelationChangeInfo relationChange)
		{
			try
			{
				Kingdom kingdom = Enumerable.FirstOrDefault<Kingdom>(Kingdom.All, (Kingdom k) => k.StringId == relationChange.Kingdom1Id);
				Kingdom kingdom2 = Enumerable.FirstOrDefault<Kingdom>(Kingdom.All, (Kingdom k) => k.StringId == relationChange.Kingdom2Id);
				bool flag = kingdom == null || kingdom2 == null;
				if (flag)
				{
					DiplomacyLogger.Instance.Log(string.Concat(new string[]
					{
						<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-554259690),
						relationChange.Kingdom1Id,
						<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(990685120),
						relationChange.Kingdom2Id,
						<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-52838832)
					}));
				}
				else
				{
					Hero leader = kingdom.Leader;
					Hero leader2 = kingdom2.Leader;
					bool flag2 = leader == null || leader2 == null;
					if (flag2)
					{
						DiplomacyLogger.Instance.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-309639124));
					}
					else
					{
						int num = Math.Max(GlobalSettings<ModSettings>.Instance.MinKingdomRelationChange, Math.Min(GlobalSettings<ModSettings>.Instance.MaxKingdomRelationChange, relationChange.Change));
						bool flag3 = num == 0;
						if (flag3)
						{
							DiplomacyLogger.Instance.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-654557086));
						}
						else
						{
							int relation = leader.GetRelation(leader2);
							bool flag4 = true;
							string text = "";
							bool flag5 = num > 0 && relation >= 100;
							if (flag5)
							{
								flag4 = false;
								text = <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(855256177);
							}
							else
							{
								bool flag6 = num < 0 && relation <= -100;
								if (flag6)
								{
									flag4 = false;
									text = <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-2017383552);
								}
							}
							bool flag7 = !flag4;
							if (flag7)
							{
								DiplomacyLogger.Instance.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(57339847), new object[]
								{
									num,
									relation,
									text,
									relationChange.Reason
								}));
							}
							else
							{
								ChangeRelationAction.ApplyRelationChangeBetweenHeroes(leader, leader2, num, true);
								int relation2 = leader.GetRelation(leader2);
								DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(35168416), new object[]
								{
									kingdom.Name,
									kingdom2.Name,
									relation,
									relation2,
									num
								}));
								DiplomacyLogger.Instance.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-2066483253) + relationChange.Reason);
								bool flag8 = kingdom == Hero.MainHero.MapFaction || kingdom2 == Hero.MainHero.MapFaction;
								if (flag8)
								{
									string text2 = (num > 0) ? <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-632372267) : <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1759337366);
									Color color = (num > 0) ? \u200D\u206E\u200B\u202E\u206A\u202A\u200E\u202E\u200E\u202E\u202A\u202B\u200C\u206F\u202A\u206E\u206E\u200F\u206B\u202D\u200C\u200F\u206E\u200C\u206E\u202E\u200B\u200D\u202D\u202E\u206D\u206E\u202B\u200F\u200C\u206A\u202A\u202C\u206D\u202E.\u200C\u206B\u200F\u206B\u206A\u206C\u206D\u202D\u202E\u200F\u206B\u202D\u200F\u202A\u202D\u206D\u206A\u206D\u200D\u202A\u206F\u206C\u200B\u202B\u200D\u206C\u200E\u200F\u206D\u206C\u202D\u206B\u200F\u206E\u200D\u200F\u200C\u206F\u202C\u206F\u202E : \u200D\u206E\u200B\u202E\u206A\u202A\u200E\u202E\u200E\u202E\u202A\u202B\u200C\u206F\u202A\u206E\u206E\u200F\u206B\u202D\u200C\u200F\u206E\u200C\u206E\u202E\u200B\u200D\u202D\u202E\u206D\u206E\u202B\u200F\u200C\u206A\u202A\u202C\u206D\u202E.\u202C\u200D\u202E\u200D\u206F\u206E\u206C\u202E\u206A\u206C\u206F\u200F\u206F\u200C\u206A\u206D\u206B\u200B\u206B\u202C\u202A\u202E\u200B\u200B\u206B\u200D\u200D\u202B\u206F\u202E\u202E\u202E\u200F\u200C\u202A\u202D\u206A\u202D\u200F\u200C\u202E;
								}
								DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(119704293), new object[]
								{
									kingdom.Name,
									relationChange.Kingdom1Id,
									kingdom2.Name,
									relationChange.Kingdom2Id,
									relation,
									relation2,
									num,
									relationChange.Reason
								}));
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				DiplomacyLogger.Instance.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(909098716) + ex.Message);
				DiplomacyLogger.Instance.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1273998661), <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(618005799) + relationChange.Kingdom1Id + <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1758190514) + relationChange.Kingdom2Id, ex);
			}
		}

		// Token: 0x06000EEC RID: 3820 RVA: 0x000F7CC0 File Offset: 0x000F5EC0
		[DebuggerStepThrough]
		public Task ProcessPlayerStatement(string playerText)
		{
			DiplomacyManager.<ProcessPlayerStatement>d__78 <ProcessPlayerStatement>d__ = new DiplomacyManager.<ProcessPlayerStatement>d__78();
			<ProcessPlayerStatement>d__.<>t__builder = AsyncTaskMethodBuilder.Create();
			<ProcessPlayerStatement>d__.<>4__this = this;
			<ProcessPlayerStatement>d__.playerText = playerText;
			<ProcessPlayerStatement>d__.<>1__state = -1;
			<ProcessPlayerStatement>d__.<>t__builder.Start<DiplomacyManager.<ProcessPlayerStatement>d__78>(ref <ProcessPlayerStatement>d__);
			return <ProcessPlayerStatement>d__.<>t__builder.Task;
		}

		// Token: 0x06000EED RID: 3821 RVA: 0x000F7D0C File Offset: 0x000F5F0C
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void ProcessPendingPlayerStatements()
		{
			List<DelayedPlayerStatement> list = Enumerable.ToList<DelayedPlayerStatement>(Enumerable.Where<DelayedPlayerStatement>(this._pendingPlayerStatements, (DelayedPlayerStatement s) => CampaignTime.Now >= s.PublicationTime));
			bool flag = Enumerable.Any<DelayedPlayerStatement>(list);
			if (flag)
			{
				DiplomacyLogger.Instance.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-738722371), list.Count));
			}
			foreach (DelayedPlayerStatement delayedPlayerStatement in list)
			{
				this._pendingPlayerStatements.Remove(delayedPlayerStatement);
				this.PublishPlayerStatement(delayedPlayerStatement);
			}
			bool flag2 = Enumerable.Any<DelayedPlayerStatement>(list);
			if (flag2)
			{
				this._storage.SavePendingPlayerStatements(this._pendingPlayerStatements);
			}
		}

		// Token: 0x06000EEE RID: 3822 RVA: 0x000F7DF0 File Offset: 0x000F5FF0
		[MethodImpl(MethodImplOptions.NoInlining)]
		private Color GetVanillaDiplomaticNotificationColorForPlayer(DelayedPlayerStatement statement)
		{
			Color result;
			try
			{
				bool flag = statement.PlayerKingdom == null || Clan.PlayerClan == null;
				if (flag)
				{
					result = Color.White;
				}
				else
				{
					List<Kingdom> list = new List<Kingdom>();
					bool flag2 = statement.TargetKingdomIds != null && Enumerable.Any<string>(statement.TargetKingdomIds);
					if (flag2)
					{
						foreach (string objectName in statement.TargetKingdomIds)
						{
							MBObjectManager instance = MBObjectManager.Instance;
							Kingdom kingdom = (instance != null) ? instance.GetObject<Kingdom>(objectName) : null;
							bool flag3 = kingdom != null;
							if (flag3)
							{
								list.Add(kingdom);
							}
						}
					}
					bool flag4 = Clan.PlayerClan.Kingdom != null;
					ChatNotificationType notificationType;
					if (flag4)
					{
						notificationType = ChatNotificationType.PlayerClanPositive;
					}
					else
					{
						bool flag5;
						if (statement.PlayerKingdom != Clan.PlayerClan.MapFaction)
						{
							flag5 = Enumerable.Any<Kingdom>(list, (Kingdom k) => k == Clan.PlayerClan.MapFaction);
						}
						else
						{
							flag5 = true;
						}
						bool flag6 = flag5;
						if (flag6)
						{
							notificationType = ChatNotificationType.PlayerFactionPositive;
						}
						else
						{
							notificationType = ChatNotificationType.PlayerFactionIndirectPositive;
						}
					}
					uint notificationColor = Campaign.Current.Models.DiplomacyModel.GetNotificationColor(notificationType);
					result = Color.FromUint(notificationColor);
				}
			}
			catch (Exception ex)
			{
				DiplomacyLogger.Instance.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1333041229) + ex.Message);
				result = Color.White;
			}
			return result;
		}

		// Token: 0x06000EEF RID: 3823 RVA: 0x000F7F90 File Offset: 0x000F6190
		[DebuggerStepThrough]
		private Task PublishPlayerStatement(DelayedPlayerStatement statement)
		{
			DiplomacyManager.<PublishPlayerStatement>d__81 <PublishPlayerStatement>d__ = new DiplomacyManager.<PublishPlayerStatement>d__81();
			<PublishPlayerStatement>d__.<>t__builder = AsyncTaskMethodBuilder.Create();
			<PublishPlayerStatement>d__.<>4__this = this;
			<PublishPlayerStatement>d__.statement = statement;
			<PublishPlayerStatement>d__.<>1__state = -1;
			<PublishPlayerStatement>d__.<>t__builder.Start<DiplomacyManager.<PublishPlayerStatement>d__81>(ref <PublishPlayerStatement>d__);
			return <PublishPlayerStatement>d__.<>t__builder.Task;
		}

		// Token: 0x06000EF0 RID: 3824 RVA: 0x000F7FDC File Offset: 0x000F61DC
		[DebuggerStepThrough]
		private Task CreateDiplomaticEventFromPlayerStatement(DelayedPlayerStatement statement)
		{
			DiplomacyManager.<CreateDiplomaticEventFromPlayerStatement>d__82 <CreateDiplomaticEventFromPlayerStatement>d__ = new DiplomacyManager.<CreateDiplomaticEventFromPlayerStatement>d__82();
			<CreateDiplomaticEventFromPlayerStatement>d__.<>t__builder = AsyncTaskMethodBuilder.Create();
			<CreateDiplomaticEventFromPlayerStatement>d__.<>4__this = this;
			<CreateDiplomaticEventFromPlayerStatement>d__.statement = statement;
			<CreateDiplomaticEventFromPlayerStatement>d__.<>1__state = -1;
			<CreateDiplomaticEventFromPlayerStatement>d__.<>t__builder.Start<DiplomacyManager.<CreateDiplomaticEventFromPlayerStatement>d__82>(ref <CreateDiplomaticEventFromPlayerStatement>d__);
			return <CreateDiplomaticEventFromPlayerStatement>d__.<>t__builder.Task;
		}

		// Token: 0x06000EF1 RID: 3825 RVA: 0x000F8028 File Offset: 0x000F6228
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void CheckPlayerKingdomTimeouts()
		{
			try
			{
				Hero mainHero = Hero.MainHero;
				Kingdom kingdom;
				if (mainHero == null)
				{
					kingdom = null;
				}
				else
				{
					Clan clan = mainHero.Clan;
					kingdom = ((clan != null) ? clan.Kingdom : null);
				}
				Kingdom kingdom2 = kingdom;
				bool flag = kingdom2 == null;
				if (!flag)
				{
					foreach (DynamicEvent dynamicEvent in Enumerable.ToList<DynamicEvent>(this._activeDiplomaticEvents))
					{
						bool flag2 = !dynamicEvent.ParticipatingKingdoms.Contains(kingdom2.StringId);
						if (!flag2)
						{
							HashSet<string> hashSet = Enumerable.ToHashSet<string>(Enumerable.Select<KingdomStatement, string>(Enumerable.Skip<KingdomStatement>(dynamicEvent.KingdomStatements, dynamicEvent.StatementsAtRoundStart), (KingdomStatement s) => s.KingdomId));
							bool flag3 = hashSet.Contains(kingdom2.StringId);
							if (!flag3)
							{
								bool flag4 = this._playerKingdomLastStatement.ContainsKey(kingdom2);
								if (flag4)
								{
									CampaignTime campaignTime = this._playerKingdomLastStatement[kingdom2];
									float num = (float)(CampaignTime.Now.ToDays - campaignTime.ToDays);
									bool flag5 = num >= 3f;
									if (flag5)
									{
										DiplomacyLogger.Instance.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1693050692), kingdom2.Name, dynamicEvent.Id));
										KingdomStatement kingdomStatement = new KingdomStatement
										{
											KingdomId = kingdom2.StringId,
											StatementText = <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1280116105),
											Action = DiplomaticAction.None,
											TargetKingdomId = null,
											Reason = <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1575844714),
											Timestamp = CampaignTime.Now,
											EventId = dynamicEvent.Id
										};
										dynamicEvent.KingdomStatements.Add(kingdomStatement);
										DiplomaticStatementsStorage.Instance.AddStatement(kingdomStatement);
										DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-530787955), kingdom2.Name));
										List<Kingdom> participatingKingdoms = this.GetParticipatingKingdoms(dynamicEvent);
										int num2 = Enumerable.Count<Kingdom>(participatingKingdoms, delegate(Kingdom k)
										{
											Hero leader = k.Leader;
											return leader == null || !leader.IsPrisoner;
										});
										int count = dynamicEvent.KingdomStatements.Count;
										int num3 = count - dynamicEvent.StatementsAtRoundStart;
										bool flag6 = num3 >= num2;
										if (flag6)
										{
											Random random = new Random();
											int num4 = random.Next(1, 3);
											CampaignTime value = CampaignTime.DaysFromNow((float)num4);
											this._eventAnalysisSchedule[dynamicEvent.Id] = value;
											DiplomacyLogger.Instance.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1834244983), num4));
										}
										this._storage.SaveDiplomaticEvents(this._activeDiplomaticEvents);
									}
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				DiplomacyLogger.Instance.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1579050051) + ex.Message);
			}
		}

		// Token: 0x06000EF2 RID: 3826 RVA: 0x000F8350 File Offset: 0x000F6550
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void RestoreEventSchedules()
		{
			DiplomacyLogger.Instance.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1454521318));
			foreach (DynamicEvent dynamicEvent in this._activeDiplomaticEvents)
			{
				bool flag = dynamicEvent.ParticipatingKingdoms == null || !Enumerable.Any<string>(dynamicEvent.ParticipatingKingdoms);
				if (!flag)
				{
					List<Kingdom> participatingKingdoms = this.GetParticipatingKingdoms(dynamicEvent);
					int num = Enumerable.Count<Kingdom>(participatingKingdoms, delegate(Kingdom k)
					{
						Hero leader4 = k.Leader;
						return leader4 == null || !leader4.IsPrisoner;
					});
					List<KingdomStatement> kingdomStatements = dynamicEvent.KingdomStatements;
					int num2 = (kingdomStatements != null) ? kingdomStatements.Count : 0;
					int num3 = num2 - dynamicEvent.StatementsAtRoundStart;
					bool flag2 = num2 == 0;
					if (flag2)
					{
						DiplomacyLogger.Instance.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1723573355) + dynamicEvent.Id + <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1686105389));
						CampaignTime value = CampaignTime.HoursFromNow(1f);
						this._eventStatementSchedule[dynamicEvent.Id] = value;
						DiplomacyLogger.Instance.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-891582347) + dynamicEvent.Id + <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-606592450));
					}
					else
					{
						DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-317665744), new object[]
						{
							dynamicEvent.Id,
							dynamicEvent.DiplomaticRounds,
							num,
							participatingKingdoms.Count - num,
							num3
						}));
						bool flag3 = num2 >= num;
						bool flag4 = num3 < num && !flag3;
						if (flag4)
						{
							HashSet<string> respondedKingdoms = Enumerable.ToHashSet<string>(Enumerable.Select<KingdomStatement, string>(Enumerable.Skip<KingdomStatement>(dynamicEvent.KingdomStatements, dynamicEvent.StatementsAtRoundStart), (KingdomStatement s) => s.KingdomId));
							DiplomacyLogger.Instance.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1623475918) + string.Join(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-232295527), respondedKingdoms));
							using (List<string>.Enumerator enumerator2 = dynamicEvent.ParticipatingKingdoms.GetEnumerator())
							{
								while (enumerator2.MoveNext())
								{
									string kid = enumerator2.Current;
									Kingdom kingdom4 = Enumerable.FirstOrDefault<Kingdom>(Kingdom.All, (Kingdom kingdom) => kingdom.StringId == kid);
									bool flag5 = kingdom4 != null;
									if (flag5)
									{
										bool flag6 = DiplomacyManagerHelpers.IsPlayerKingdom(kingdom4);
										Hero leader = kingdom4.Leader;
										bool flag7 = leader != null && leader.IsPrisoner;
										bool flag8 = respondedKingdoms.Contains(kid);
										DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-280843247), new object[]
										{
											kingdom4.Name,
											kid,
											flag6,
											flag7,
											flag8
										}));
									}
								}
							}
							string nextKingdomId = Enumerable.FirstOrDefault<string>(Enumerable.Where<string>(dynamicEvent.ParticipatingKingdoms, delegate(string kid)
							{
								Kingdom kingdom5 = Enumerable.FirstOrDefault<Kingdom>(Kingdom.All, (Kingdom kingdom) => kingdom.StringId == kid);
								bool result;
								if (kingdom5 != null && !DiplomacyManagerHelpers.IsPlayerKingdom(kingdom5))
								{
									Hero leader4 = kingdom5.Leader;
									result = (leader4 == null || !leader4.IsPrisoner);
								}
								else
								{
									result = false;
								}
								return result;
							}), (string k) => !respondedKingdoms.Contains(k));
							bool flag9 = !string.IsNullOrEmpty(nextKingdomId);
							if (flag9)
							{
								Kingdom kingdom2 = Enumerable.FirstOrDefault<Kingdom>(Kingdom.All, (Kingdom k) => k.StringId == nextKingdomId);
								bool flag10 = kingdom2 != null;
								if (flag10)
								{
									Random random = new Random();
									int num4 = random.Next(1, 7);
									CampaignTime value2 = CampaignTime.HoursFromNow((float)num4);
									this._eventStatementSchedule[dynamicEvent.Id] = value2;
									this._pendingStatements[dynamicEvent.Id] = kingdom2;
									DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1538677836), kingdom2.Name, num4));
								}
							}
							else
							{
								DiplomacyLogger.Instance.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1002259204) + dynamicEvent.Id);
							}
						}
						else
						{
							bool flag11 = flag3;
							if (flag11)
							{
								DiplomacyLogger.Instance.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-598315955) + dynamicEvent.Id);
								using (List<string>.Enumerator enumerator3 = dynamicEvent.ParticipatingKingdoms.GetEnumerator())
								{
									while (enumerator3.MoveNext())
									{
										string kid = enumerator3.Current;
										Kingdom kingdom3 = Enumerable.FirstOrDefault<Kingdom>(Kingdom.All, (Kingdom kingdom) => kingdom.StringId == kid);
										bool flag12 = kingdom3 != null;
										if (flag12)
										{
											bool flag13 = DiplomacyManagerHelpers.IsPlayerKingdom(kingdom3);
											Hero leader2 = kingdom3.Leader;
											bool flag14 = leader2 != null && leader2.IsPrisoner;
											DiplomacyLogger instance = DiplomacyLogger.Instance;
											string format = <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1638754745);
											object[] array = new object[5];
											array[0] = kingdom3.Name;
											array[1] = kid;
											array[2] = flag13;
											array[3] = flag14;
											int num5 = 4;
											Hero leader3 = kingdom3.Leader;
											object obj;
											if (leader3 == null)
											{
												obj = null;
											}
											else
											{
												TextObject name = leader3.Name;
												obj = ((name != null) ? name.ToString() : null);
											}
											array[num5] = (obj ?? <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1239366164));
											instance.Log(string.Format(format, array));
										}
									}
								}
								bool flag15 = !this._eventAnalysisSchedule.ContainsKey(dynamicEvent.Id);
								if (flag15)
								{
									Random random2 = new Random();
									int num6 = random2.Next(1, 3);
									CampaignTime value3 = CampaignTime.DaysFromNow((float)num6);
									this._eventAnalysisSchedule[dynamicEvent.Id] = value3;
									DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1499254997), dynamicEvent.Id, num6));
								}
							}
							else
							{
								bool flag16 = !this._eventAnalysisSchedule.ContainsKey(dynamicEvent.Id);
								if (flag16)
								{
									Random random3 = new Random();
									int num7 = random3.Next(1, 4);
									CampaignTime value4 = CampaignTime.DaysFromNow((float)num7);
									this._eventAnalysisSchedule[dynamicEvent.Id] = value4;
									DiplomacyLogger.Instance.Log(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-575749168), dynamicEvent.Id, num7));
								}
							}
						}
					}
				}
			}
			DiplomacyLogger.Instance.Log(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1186627994), this._eventStatementSchedule.Count, this._eventAnalysisSchedule.Count));
		}

		// Token: 0x06000EF3 RID: 3827 RVA: 0x000F8A6C File Offset: 0x000F6C6C
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void RestoreDiplomaticEventsFromDynamicEvents()
		{
			try
			{
				DynamicEventsManager instance = DynamicEventsManager.Instance;
				bool flag = instance == null;
				if (flag)
				{
					DiplomacyLogger.Instance.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-2052170830));
				}
				else
				{
					List<DynamicEvent> list = instance.GetAllEvents() ?? new List<DynamicEvent>();
					List<DynamicEvent> list2 = Enumerable.ToList<DynamicEvent>(Enumerable.Where<DynamicEvent>(Enumerable.Where<DynamicEvent>(list, (DynamicEvent e) => e.AllowsDiplomaticResponse && e.RequiresDiplomaticAnalysis && !e.IsExpired()), (DynamicEvent e) => !Enumerable.Any<DynamicEvent>(this._activeDiplomaticEvents, (DynamicEvent d) => d.Id == e.Id)));
					bool flag2 = !Enumerable.Any<DynamicEvent>(list2);
					if (!flag2)
					{
						DiplomacyLogger.Instance.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(141984056), list2.Count));
						foreach (DynamicEvent dynamicEvent in list2)
						{
							this._activeDiplomaticEvents.Add(dynamicEvent);
							DiplomacyLogger.Instance.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(731002755) + dynamicEvent.Id);
						}
						this._storage.SaveDiplomaticEvents(this._activeDiplomaticEvents);
					}
				}
			}
			catch (Exception ex)
			{
				DiplomacyLogger.Instance.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1698992083) + ex.Message);
			}
		}

		// Token: 0x06000EF4 RID: 3828 RVA: 0x0001E1E8 File Offset: 0x0001C3E8
		private void LogMessage(string message)
		{
			DiplomacyLogger.Instance.Log(message);
		}

		// Token: 0x0400093F RID: 2367
		private static DiplomacyManager _instance;

		// Token: 0x04000940 RID: 2368
		private readonly WarStatisticsTracker _warTracker;

		// Token: 0x04000941 RID: 2369
		private readonly AllianceSystem _allianceSystem;

		// Token: 0x04000942 RID: 2370
		private readonly WarFatigueSystem _fatigueSystem;

		// Token: 0x04000943 RID: 2371
		private readonly DiplomacyStorage _storage;

		// Token: 0x04000944 RID: 2372
		private readonly TradeAgreementSystem _tradeAgreementSystem;

		// Token: 0x04000945 RID: 2373
		private readonly TerritoryTransferSystem _territoryTransferSystem;

		// Token: 0x04000946 RID: 2374
		private readonly TributeSystem _tributeSystem;

		// Token: 0x04000947 RID: 2375
		private readonly ReparationsSystem _reparationsSystem;

		// Token: 0x04000948 RID: 2376
		private KingdomStatementGenerator _statementGenerator;

		// Token: 0x04000949 RID: 2377
		private DynamicEventsAnalyzer _eventsAnalyzer;

		// Token: 0x0400094A RID: 2378
		private PlayerStatementAnalyzer _playerAnalyzer;

		// Token: 0x0400094B RID: 2379
		private readonly List<DynamicEvent> _activeDiplomaticEvents = new List<DynamicEvent>();

		// Token: 0x0400094C RID: 2380
		private readonly Queue<DynamicEvent> _queuedDiplomaticEvents = new Queue<DynamicEvent>();

		// Token: 0x0400094D RID: 2381
		private readonly Dictionary<string, CampaignTime> _eventStatementSchedule = new Dictionary<string, CampaignTime>();

		// Token: 0x0400094E RID: 2382
		[TupleElementNames(new string[]
		{
			"attackerInitial",
			"defenderInitial"
		})]
		private readonly Dictionary<MapEvent, ValueTuple<int, int>> _battleInitialTroops = new Dictionary<MapEvent, ValueTuple<int, int>>();

		// Token: 0x0400094F RID: 2383
		private readonly Dictionary<string, CampaignTime> _eventAnalysisSchedule = new Dictionary<string, CampaignTime>();

		// Token: 0x04000950 RID: 2384
		private readonly Dictionary<string, Kingdom> _pendingStatements = new Dictionary<string, Kingdom>();

		// Token: 0x04000951 RID: 2385
		private readonly List<DelayedPlayerStatement> _pendingPlayerStatements = new List<DelayedPlayerStatement>();

		// Token: 0x04000952 RID: 2386
		private readonly Dictionary<Kingdom, CampaignTime> _playerKingdomLastStatement = new Dictionary<Kingdom, CampaignTime>();

		// Token: 0x04000953 RID: 2387
		private bool _initialized = false;

		// Token: 0x04000954 RID: 2388
		private CampaignTime _lastDiplomaticUpdate;

		// Token: 0x04000955 RID: 2389
		private int _nextUpdateIntervalDays;
	}
}
