using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AIInfluence.Diplomacy;
using AIInfluence.DynamicEvents;
using MCM.Abstractions.Base.Global;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x0200013B RID: 315
	public class PostCombatEventCreator
	{
		// Token: 0x060009F9 RID: 2553 RVA: 0x0001D7A0 File Offset: 0x0001B9A0
		public PostCombatEventCreator(AIInfluenceBehavior behavior)
		{
			this._behavior = behavior;
			this._logger = SettlementCombatLogger.Instance;
		}

		// Token: 0x060009FA RID: 2554 RVA: 0x000BBCAC File Offset: 0x000B9EAC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void CreatePostCombatEvent(string aiResponse, Settlement settlement, CombatResult combatResult = null)
		{
			try
			{
				this._behavior.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(318834577));
				DynamicEvent dynamicEvent = this.ParseAIResponse(aiResponse);
				bool flag = dynamicEvent == null;
				if (flag)
				{
					this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(261979822), <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1157256244), null);
					this._behavior.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1642339343));
				}
				else
				{
					bool flag2 = combatResult != null && combatResult.PlayerCaptured;
					if (flag2)
					{
						dynamicEvent.PlayerInvolved = true;
					}
					bool flag3 = combatResult != null && combatResult.CapturedHeroes != null && combatResult.CapturedHeroes.Count > 0;
					if (flag3)
					{
						bool flag4 = dynamicEvent.CharactersInvolved == null;
						if (flag4)
						{
							dynamicEvent.CharactersInvolved = new List<string>();
						}
						foreach (string text in combatResult.CapturedHeroes)
						{
							bool flag5 = string.IsNullOrEmpty(text);
							if (!flag5)
							{
								bool flag6 = !dynamicEvent.CharactersInvolved.Contains(text);
								if (flag6)
								{
									dynamicEvent.CharactersInvolved.Add(text);
								}
							}
						}
					}
					dynamicEvent.CreationTime = DateTime.Now;
					dynamicEvent.CreationCampaignDays = (float)CampaignTime.Now.ToDays;
					dynamicEvent.ExpirationCampaignDays = dynamicEvent.CreationCampaignDays + (float)GlobalSettings<ModSettings>.Instance.DynamicEventsLifespan;
					dynamicEvent.ExpirationTime = DateTime.Now.AddDays((double)GlobalSettings<ModSettings>.Instance.DynamicEventsLifespan);
					bool flag7 = dynamicEvent.EventHistory == null;
					if (flag7)
					{
						dynamicEvent.EventHistory = new List<EventUpdate>();
					}
					bool flag8 = !Enumerable.Any<EventUpdate>(dynamicEvent.EventHistory);
					if (flag8)
					{
						dynamicEvent.AddEventUpdate(dynamicEvent.Description, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(746153276));
					}
					DynamicEventsManager.Instance.AddEvent(dynamicEvent);
					this._logger.LogDynamicEventCreated(dynamicEvent.Id, dynamicEvent.Description, <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-351303755));
					this._behavior.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1074205993) + dynamicEvent.Id);
					this.CheckAndProcessDiplomaticResponse(dynamicEvent);
					this.ApplySettlementPenalty(dynamicEvent, settlement);
					this.SpreadEventToNPCs(dynamicEvent);
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(2039344376), ex.Message, ex);
				this._behavior.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(51049556) + ex.Message + <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1517472131) + ex.StackTrace);
			}
		}

		// Token: 0x060009FB RID: 2555 RVA: 0x000BBF90 File Offset: 0x000BA190
		[MethodImpl(MethodImplOptions.NoInlining)]
		private DynamicEvent ParseAIResponse(string aiResponse)
		{
			DynamicEvent result;
			try
			{
				string text = ((aiResponse != null) ? aiResponse.Trim() : null) ?? "";
				bool flag = text.StartsWith(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(155072967));
				if (flag)
				{
					text = text.Substring(7);
				}
				else
				{
					bool flag2 = text.StartsWith(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(187878692));
					if (flag2)
					{
						text = text.Substring(3);
					}
				}
				bool flag3 = text.EndsWith(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(390421275));
				if (flag3)
				{
					text = text.Substring(0, text.Length - 3);
				}
				text = text.Trim();
				DynamicEvent dynamicEvent = JsonConvert.DeserializeObject<DynamicEvent>(text);
				result = dynamicEvent;
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1038309226), <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-51459358) + ex.Message, ex);
				this._behavior.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(746209562) + ex.Message);
				this._behavior.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-913189358) + aiResponse);
				result = null;
			}
			return result;
		}

		// Token: 0x060009FC RID: 2556 RVA: 0x000BC0C4 File Offset: 0x000BA2C4
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void SpreadEventToNPCs(DynamicEvent dynamicEvent)
		{
			try
			{
				this._behavior.LogMessage(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(644713455) + string.Join(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-36731900), dynamicEvent.ApplicableNPCs));
			}
			catch (Exception ex)
			{
				this._behavior.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(594494655) + ex.Message);
			}
		}

		// Token: 0x060009FD RID: 2557 RVA: 0x000BC144 File Offset: 0x000BA344
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void ApplySettlementPenalty(DynamicEvent dynamicEvent, Settlement settlement)
		{
			try
			{
				bool flag = dynamicEvent.SettlementPenalty == null;
				if (flag)
				{
					this._behavior.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1429426820));
				}
				else
				{
					SettlementPenaltyData settlementPenalty = dynamicEvent.SettlementPenalty;
					bool flag2 = settlementPenalty.ProsperityPenaltyPerDay <= 0f;
					if (flag2)
					{
						this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(569736196), string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(556504915), settlementPenalty.ProsperityPenaltyPerDay), null);
					}
					else
					{
						bool flag3 = settlementPenalty.PenaltyDurationDays < 1;
						if (flag3)
						{
							this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-2134822853), string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(278530744), settlementPenalty.PenaltyDurationDays), null);
						}
						else
						{
							bool flag4 = settlement != null && SettlementPenaltyManager.Instance != null;
							if (flag4)
							{
								SettlementPenaltyManager.Instance.AddPenalty(settlement, settlementPenalty.ProsperityPenaltyPerDay, settlementPenalty.PenaltyDurationDays, settlementPenalty.PenaltyReason ?? <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1597152944));
								this._behavior.LogMessage(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(728801567), new object[]
								{
									settlement.Name,
									settlementPenalty.ProsperityPenaltyPerDay,
									settlementPenalty.PenaltyDurationDays,
									settlementPenalty.PenaltyReason
								}));
							}
							else
							{
								this._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1086682215), string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1082981943), (settlement != null) ? settlement.Name : null, SettlementPenaltyManager.Instance), null);
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(834266700), ex.Message, ex);
				this._behavior.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(2102865642) + ex.Message);
			}
		}

		// Token: 0x060009FE RID: 2558 RVA: 0x000BC360 File Offset: 0x000BA560
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void CheckAndProcessDiplomaticResponse(DynamicEvent dynamicEvent)
		{
			try
			{
				DiplomacyLogger instance = DiplomacyLogger.Instance;
				if (instance != null)
				{
					instance.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1815905433) + dynamicEvent.Id);
				}
				DiplomacyLogger instance2 = DiplomacyLogger.Instance;
				if (instance2 != null)
				{
					instance2.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(2141245537), dynamicEvent.AllowsDiplomaticResponse));
				}
				DiplomacyLogger instance3 = DiplomacyLogger.Instance;
				if (instance3 != null)
				{
					instance3.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(96723210), GlobalSettings<ModSettings>.Instance.EnableDiplomacy));
				}
				bool flag = dynamicEvent.AllowsDiplomaticResponse && GlobalSettings<ModSettings>.Instance.EnableDiplomacy;
				if (flag)
				{
					List<string> kingdomStringIds = dynamicEvent.GetKingdomStringIds();
					bool flag2 = Enumerable.Any<string>(kingdomStringIds) && kingdomStringIds[0] != <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1223935971);
					if (flag2)
					{
						List<string> list = Enumerable.ToList<string>(Enumerable.Where<string>(kingdomStringIds, (string k) => k != <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-629724995)));
						bool flag3 = list.Count >= 1;
						if (flag3)
						{
							dynamicEvent.ParticipatingKingdoms = list;
							dynamicEvent.RequiresDiplomaticAnalysis = true;
							DiplomacyManager instance4 = DiplomacyManager.Instance;
							bool flag4 = instance4 != null && instance4.HasActiveDiplomaticEvents();
							bool flag5 = Enumerable.Any<DynamicEvent>(DynamicEventsManager.Instance.GetActiveEvents(), (DynamicEvent e) => e.AllowsDiplomaticResponse && e.RequiresDiplomaticAnalysis && e.Id != dynamicEvent.Id);
							bool flag6 = flag4 || flag5;
							if (flag6)
							{
								DiplomacyLogger instance5 = DiplomacyLogger.Instance;
								if (instance5 != null)
								{
									instance5.Log(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(758823281) + dynamicEvent.Id + <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1841146447));
								}
								this._behavior.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(636350191));
								DiplomacyManager instance6 = DiplomacyManager.Instance;
								if (instance6 != null)
								{
									instance6.QueueDiplomaticEvent(dynamicEvent);
								}
							}
							else
							{
								DiplomacyLogger instance7 = DiplomacyLogger.Instance;
								if (instance7 != null)
								{
									instance7.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(492417309) + dynamicEvent.Id + <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1163914579) + string.Join(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-234543546), list));
								}
								this.ProcessDiplomaticEventAsync(dynamicEvent);
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1770579615), ex.Message, ex);
			}
		}

		// Token: 0x060009FF RID: 2559 RVA: 0x000BC610 File Offset: 0x000BA810
		[DebuggerStepThrough]
		private Task ProcessDiplomaticEventAsync(DynamicEvent diplomaticEvent)
		{
			PostCombatEventCreator.<ProcessDiplomaticEventAsync>d__8 <ProcessDiplomaticEventAsync>d__ = new PostCombatEventCreator.<ProcessDiplomaticEventAsync>d__8();
			<ProcessDiplomaticEventAsync>d__.<>t__builder = AsyncTaskMethodBuilder.Create();
			<ProcessDiplomaticEventAsync>d__.<>4__this = this;
			<ProcessDiplomaticEventAsync>d__.diplomaticEvent = diplomaticEvent;
			<ProcessDiplomaticEventAsync>d__.<>1__state = -1;
			<ProcessDiplomaticEventAsync>d__.<>t__builder.Start<PostCombatEventCreator.<ProcessDiplomaticEventAsync>d__8>(ref <ProcessDiplomaticEventAsync>d__);
			return <ProcessDiplomaticEventAsync>d__.<>t__builder.Task;
		}

		// Token: 0x04000624 RID: 1572
		private readonly AIInfluenceBehavior _behavior;

		// Token: 0x04000625 RID: 1573
		private readonly SettlementCombatLogger _logger;
	}
}
