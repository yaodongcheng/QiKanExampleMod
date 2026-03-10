using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using AIInfluence.DynamicEvents;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;

namespace AIInfluence.Diplomacy
{
	// Token: 0x0200020A RID: 522
	public class DiplomacyStorage
	{
		// Token: 0x06000FC1 RID: 4033 RVA: 0x000FF540 File Offset: 0x000FD740
		[MethodImpl(MethodImplOptions.NoInlining)]
		public DiplomacyStorage()
		{
			string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			string fullName = Directory.GetParent(Directory.GetParent(directoryName).FullName).FullName;
			this._saveDataPath = Path.Combine(fullName, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1148459801));
			bool flag = !Directory.Exists(this._saveDataPath);
			if (flag)
			{
				Directory.CreateDirectory(this._saveDataPath);
				this.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-409460685) + this._saveDataPath);
			}
		}

		// Token: 0x06000FC2 RID: 4034 RVA: 0x000FF674 File Offset: 0x000FD874
		[MethodImpl(MethodImplOptions.NoInlining)]
		private string GetCurrentSaveFolder()
		{
			Campaign campaign = Campaign.Current;
			string text = ((campaign != null) ? campaign.UniqueGameId : null) ?? <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1239366164);
			string path = (text != <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1556416923)) ? text : <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-18599928);
			string text2 = Path.Combine(this._saveDataPath, path);
			bool flag = this._cachedUniqueGameId != text;
			if (flag)
			{
				this.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1949921775) + text);
				this.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1786436152) + text2);
				this._cachedUniqueGameId = text;
				this._cachedSaveFolder = text2;
			}
			bool flag2 = !Directory.Exists(text2);
			if (flag2)
			{
				Directory.CreateDirectory(text2);
				this.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1000506242) + text2);
			}
			return text2;
		}

		// Token: 0x06000FC3 RID: 4035 RVA: 0x000FF760 File Offset: 0x000FD960
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void SaveAlliances(AllianceSystem allianceSystem)
		{
			bool flag = allianceSystem == null;
			if (flag)
			{
				this.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(800901600));
			}
			else
			{
				try
				{
					string text = Path.Combine(this.GetCurrentSaveFolder(), this._alliancesFileName);
					AllianceData allianceData = new AllianceData
					{
						Alliances = allianceSystem.Alliances,
						AllianceTimes = allianceSystem.AllianceTimes,
						SaveTime = DateTime.Now,
						CampaignDays = (float)(CampaignTime.Now.ToYears * 336.0 + (double)CampaignTime.Now.GetDayOfYear)
					};
					JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
					{
						Formatting = 1,
						NullValueHandling = 1
					};
					string contents = JsonConvert.SerializeObject(allianceData, jsonSerializerSettings);
					File.WriteAllText(text, contents);
					this.LogMessage(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(588201392), allianceSystem.Alliances.Count, text));
				}
				catch (Exception ex)
				{
					this.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-952259698) + ex.Message + <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-80608601) + ex.StackTrace);
				}
			}
		}

		// Token: 0x06000FC4 RID: 4036 RVA: 0x000FF89C File Offset: 0x000FDA9C
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void LoadAlliances(AllianceSystem allianceSystem)
		{
			bool flag = allianceSystem == null;
			if (flag)
			{
				this.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1524983171));
			}
			else
			{
				try
				{
					string text = Path.Combine(this.GetCurrentSaveFolder(), this._alliancesFileName);
					bool flag2 = !File.Exists(text);
					if (flag2)
					{
						this.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1056221526) + text);
					}
					else
					{
						string text2 = File.ReadAllText(text);
						bool flag3 = string.IsNullOrWhiteSpace(text2);
						if (flag3)
						{
							this.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1074513650));
						}
						else
						{
							AllianceData allianceData = JsonConvert.DeserializeObject<AllianceData>(text2);
							bool flag4 = allianceData == null;
							if (flag4)
							{
								this.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1418575562));
							}
							else
							{
								allianceSystem.Alliances = (allianceData.Alliances ?? new Dictionary<string, List<string>>());
								allianceSystem.AllianceTimes = (allianceData.AllianceTimes ?? new Dictionary<string, CampaignTime>());
								this.LogMessage(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-651994851), allianceSystem.Alliances.Count, text));
							}
						}
					}
				}
				catch (Exception ex)
				{
					this.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-378469515) + ex.Message + <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-2112900416) + ex.StackTrace);
				}
			}
		}

		// Token: 0x06000FC5 RID: 4037 RVA: 0x000FFA04 File Offset: 0x000FDC04
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void SaveWarStatistics(WarStatisticsTracker warTracker)
		{
			bool flag = warTracker == null;
			if (flag)
			{
				this.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-2028879458));
			}
			else
			{
				try
				{
					string text = Path.Combine(this.GetCurrentSaveFolder(), this._warStatsFileName);
					WarStatisticsData warStatisticsData = new WarStatisticsData
					{
						KingdomStats = warTracker.KingdomStats,
						SaveTime = DateTime.Now,
						CampaignDays = (float)(CampaignTime.Now.ToYears * 336.0 + (double)CampaignTime.Now.GetDayOfYear)
					};
					JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
					{
						Formatting = 1,
						NullValueHandling = 1
					};
					string contents = JsonConvert.SerializeObject(warStatisticsData, jsonSerializerSettings);
					File.WriteAllText(text, contents);
					this.LogMessage(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(199764018), warTracker.KingdomStats.Count, text));
				}
				catch (Exception ex)
				{
					this.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1923327936) + ex.Message + <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-2112900416) + ex.StackTrace);
				}
			}
		}

		// Token: 0x06000FC6 RID: 4038 RVA: 0x000FFB34 File Offset: 0x000FDD34
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void LoadWarStatistics(WarStatisticsTracker warTracker)
		{
			bool flag = warTracker == null;
			if (flag)
			{
				this.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1466417181));
			}
			else
			{
				try
				{
					string text = Path.Combine(this.GetCurrentSaveFolder(), this._warStatsFileName);
					bool flag2 = !File.Exists(text);
					if (flag2)
					{
						this.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1149414560) + text);
					}
					else
					{
						string text2 = File.ReadAllText(text);
						bool flag3 = string.IsNullOrWhiteSpace(text2);
						if (flag3)
						{
							this.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-702889807));
						}
						else
						{
							WarStatisticsData warStatisticsData = JsonConvert.DeserializeObject<WarStatisticsData>(text2);
							bool flag4 = warStatisticsData == null;
							if (flag4)
							{
								this.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-2091365635));
							}
							else
							{
								warTracker.KingdomStats = (warStatisticsData.KingdomStats ?? new Dictionary<string, KingdomWarStats>());
								this.LogMessage(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1203561642), warTracker.KingdomStats.Count, text));
							}
						}
					}
				}
				catch (Exception ex)
				{
					this.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1966820115) + ex.Message + <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-2112900416) + ex.StackTrace);
				}
			}
		}

		// Token: 0x06000FC7 RID: 4039 RVA: 0x000FFC84 File Offset: 0x000FDE84
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void SaveDiplomaticEvents(List<DynamicEvent> diplomaticEvents)
		{
			bool flag = diplomaticEvents == null;
			if (flag)
			{
				this.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(654282853));
			}
			else
			{
				try
				{
					string text = Path.Combine(this.GetCurrentSaveFolder(), this._diplomaticEventsFileName);
					DiplomaticEventsData diplomaticEventsData = new DiplomaticEventsData
					{
						DiplomaticEvents = diplomaticEvents,
						SaveTime = DateTime.Now,
						CampaignDays = (float)(CampaignTime.Now.ToYears * 336.0 + (double)CampaignTime.Now.GetDayOfYear)
					};
					JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
					{
						Formatting = 1,
						NullValueHandling = 1
					};
					string contents = JsonConvert.SerializeObject(diplomaticEventsData, jsonSerializerSettings);
					File.WriteAllText(text, contents);
					this.LogMessage(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-370855836), diplomaticEvents.Count, text));
				}
				catch (Exception ex)
				{
					this.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(244299538) + ex.Message + <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-80608601) + ex.StackTrace);
				}
			}
		}

		// Token: 0x06000FC8 RID: 4040 RVA: 0x000FFDA8 File Offset: 0x000FDFA8
		[MethodImpl(MethodImplOptions.NoInlining)]
		public List<DynamicEvent> LoadDiplomaticEvents()
		{
			List<DynamicEvent> result;
			try
			{
				string text = Path.Combine(this.GetCurrentSaveFolder(), this._diplomaticEventsFileName);
				bool flag = !File.Exists(text);
				if (flag)
				{
					this.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1724907057) + text);
					result = new List<DynamicEvent>();
				}
				else
				{
					string text2 = File.ReadAllText(text);
					bool flag2 = string.IsNullOrWhiteSpace(text2);
					if (flag2)
					{
						this.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1616887751));
						result = new List<DynamicEvent>();
					}
					else
					{
						DiplomaticEventsData diplomaticEventsData = JsonConvert.DeserializeObject<DiplomaticEventsData>(text2);
						bool flag3 = diplomaticEventsData == null;
						if (flag3)
						{
							this.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1622239784));
							result = new List<DynamicEvent>();
						}
						else
						{
							this.LogMessage(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(2062771251), diplomaticEventsData.DiplomaticEvents.Count, text));
							result = (diplomaticEventsData.DiplomaticEvents ?? new List<DynamicEvent>());
						}
					}
				}
			}
			catch (Exception ex)
			{
				this.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-433342013) + ex.Message + <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1517472131) + ex.StackTrace);
				result = new List<DynamicEvent>();
			}
			return result;
		}

		// Token: 0x06000FC9 RID: 4041 RVA: 0x000FFEEC File Offset: 0x000FE0EC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void SavePendingPlayerStatements(List<DelayedPlayerStatement> statements)
		{
			bool flag = statements == null;
			if (flag)
			{
				this.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-853744647));
			}
			else
			{
				try
				{
					string path = Path.Combine(this.GetCurrentSaveFolder(), this._pendingPlayerStatementsFileName);
					List<SerializedPlayerStatement> list = Enumerable.ToList<SerializedPlayerStatement>(Enumerable.Where<SerializedPlayerStatement>(Enumerable.Select<DelayedPlayerStatement, SerializedPlayerStatement>(statements, (DelayedPlayerStatement s) => SerializedPlayerStatement.FromDelayed(s)), (SerializedPlayerStatement s) => s != null));
					this.LogMessage(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-792716368), statements.Count));
					foreach (DelayedPlayerStatement delayedPlayerStatement in statements)
					{
						string format = <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1068834282);
						Kingdom playerKingdom = delayedPlayerStatement.PlayerKingdom;
						this.LogMessage(string.Format(format, (playerKingdom != null) ? playerKingdom.Name : null, delayedPlayerStatement.Action, delayedPlayerStatement.PublicationTime));
					}
					string text = JsonConvert.SerializeObject(list, 1);
					this.LogMessage(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-2040773000), text.Length, text));
					File.WriteAllText(path, text);
					this.LogMessage(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1029140439), list.Count, this._pendingPlayerStatementsFileName));
				}
				catch (Exception ex)
				{
					this.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-677403358) + ex.Message);
					DiplomacyLogger.Instance.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-162542571), <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1523255342), ex);
				}
			}
		}

		// Token: 0x06000FCA RID: 4042 RVA: 0x001000F4 File Offset: 0x000FE2F4
		[MethodImpl(MethodImplOptions.NoInlining)]
		public List<DelayedPlayerStatement> LoadPendingPlayerStatements()
		{
			List<DelayedPlayerStatement> result;
			try
			{
				string text = Path.Combine(this.GetCurrentSaveFolder(), this._pendingPlayerStatementsFileName);
				bool flag = !File.Exists(text);
				if (flag)
				{
					this.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1556285262) + text);
					result = new List<DelayedPlayerStatement>();
				}
				else
				{
					string text2 = File.ReadAllText(text);
					this.LogMessage(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1996320560), text2.Length, text2));
					List<SerializedPlayerStatement> list = JsonConvert.DeserializeObject<List<SerializedPlayerStatement>>(text2);
					this.LogMessage(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1985595752), (list != null) ? list.Count : 0));
					bool flag2 = list == null || !Enumerable.Any<SerializedPlayerStatement>(list);
					if (flag2)
					{
						this.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1347888023));
						result = new List<DelayedPlayerStatement>();
					}
					else
					{
						List<DelayedPlayerStatement> list2 = Enumerable.ToList<DelayedPlayerStatement>(Enumerable.Where<DelayedPlayerStatement>(Enumerable.Select<SerializedPlayerStatement, DelayedPlayerStatement>(list, (SerializedPlayerStatement s) => s.ToDelayed()), (DelayedPlayerStatement s) => s != null));
						this.LogMessage(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-563687592), list2.Count, this._pendingPlayerStatementsFileName));
						foreach (DelayedPlayerStatement delayedPlayerStatement in list2)
						{
							string format = <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-658254409);
							Kingdom playerKingdom = delayedPlayerStatement.PlayerKingdom;
							this.LogMessage(string.Format(format, (playerKingdom != null) ? playerKingdom.Name : null, delayedPlayerStatement.Action, delayedPlayerStatement.PublicationTime));
						}
						result = list2;
					}
				}
			}
			catch (Exception ex)
			{
				this.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(735215489) + ex.Message + <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-80608601) + ex.StackTrace);
				result = new List<DelayedPlayerStatement>();
			}
			return result;
		}

		// Token: 0x06000FCB RID: 4043 RVA: 0x00100340 File Offset: 0x000FE540
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void DeleteDiplomacyFiles()
		{
			try
			{
				string currentSaveFolder = this.GetCurrentSaveFolder();
				string[] array = new string[]
				{
					Path.Combine(currentSaveFolder, this._alliancesFileName),
					Path.Combine(currentSaveFolder, this._warStatsFileName),
					Path.Combine(currentSaveFolder, this._diplomaticEventsFileName),
					Path.Combine(currentSaveFolder, this._pendingPlayerStatementsFileName)
				};
				foreach (string path in array)
				{
					bool flag = File.Exists(path);
					if (flag)
					{
						File.Delete(path);
						this.LogMessage(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-661260377) + Path.GetFileName(path));
					}
				}
			}
			catch (Exception ex)
			{
				this.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(661406017) + ex.Message);
			}
		}

		// Token: 0x06000FCC RID: 4044 RVA: 0x00100420 File Offset: 0x000FE620
		public bool DiplomacyFilesExist()
		{
			string currentSaveFolder = this.GetCurrentSaveFolder();
			return File.Exists(Path.Combine(currentSaveFolder, this._alliancesFileName)) || File.Exists(Path.Combine(currentSaveFolder, this._warStatsFileName)) || File.Exists(Path.Combine(currentSaveFolder, this._diplomaticEventsFileName)) || File.Exists(Path.Combine(currentSaveFolder, this._pendingPlayerStatementsFileName));
		}

		// Token: 0x06000FCD RID: 4045 RVA: 0x00100488 File Offset: 0x000FE688
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void ClearCurrentSaveData()
		{
			try
			{
				string currentSaveFolder = this.GetCurrentSaveFolder();
				this.LogMessage(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-696543793) + currentSaveFolder);
				string[] array = new string[]
				{
					Path.Combine(currentSaveFolder, this._alliancesFileName),
					Path.Combine(currentSaveFolder, this._warStatsFileName),
					Path.Combine(currentSaveFolder, this._diplomaticEventsFileName),
					Path.Combine(currentSaveFolder, this._pendingPlayerStatementsFileName),
					Path.Combine(currentSaveFolder, <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(437454648)),
					Path.Combine(currentSaveFolder, this._expelledClansFileName)
				};
				foreach (string path in array)
				{
					bool flag = File.Exists(path);
					if (flag)
					{
						File.Delete(path);
						this.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(410379051) + Path.GetFileName(path));
					}
				}
				this.LogMessage(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-740648063));
			}
			catch (Exception ex)
			{
				this.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(580312618) + ex.Message);
			}
		}

		// Token: 0x06000FCE RID: 4046 RVA: 0x001005B8 File Offset: 0x000FE7B8
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void SaveTradeAgreements(TradeAgreementSystem system)
		{
			bool flag = system == null;
			if (!flag)
			{
				try
				{
					string path = Path.Combine(this.GetCurrentSaveFolder(), this._tradeAgreementsFileName);
					JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
					{
						Formatting = 1,
						NullValueHandling = 1
					};
					string contents = JsonConvert.SerializeObject(system.TradeAgreements, jsonSerializerSettings);
					File.WriteAllText(path, contents);
					this.LogMessage(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-477925283), system.TradeAgreements.Count));
				}
				catch (Exception ex)
				{
					this.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(940676858) + ex.Message);
				}
			}
		}

		// Token: 0x06000FCF RID: 4047 RVA: 0x00100674 File Offset: 0x000FE874
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void LoadTradeAgreements(TradeAgreementSystem system)
		{
			bool flag = system == null;
			if (!flag)
			{
				try
				{
					string path = Path.Combine(this.GetCurrentSaveFolder(), this._tradeAgreementsFileName);
					bool flag2 = File.Exists(path);
					if (flag2)
					{
						string text = File.ReadAllText(path);
						Dictionary<string, TradeAgreementInfo> dictionary = JsonConvert.DeserializeObject<Dictionary<string, TradeAgreementInfo>>(text);
						bool flag3 = dictionary != null;
						if (flag3)
						{
							system.TradeAgreements = dictionary;
							this.LogMessage(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-820035749), dictionary.Count));
						}
					}
				}
				catch (Exception ex)
				{
					this.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1770217749) + ex.Message);
				}
			}
		}

		// Token: 0x06000FD0 RID: 4048 RVA: 0x00100730 File Offset: 0x000FE930
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void SaveTerritoryTransfers(TerritoryTransferSystem system)
		{
			bool flag = system == null;
			if (!flag)
			{
				try
				{
					string path = Path.Combine(this.GetCurrentSaveFolder(), this._territoryTransfersFileName);
					JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
					{
						Formatting = 1,
						NullValueHandling = 1
					};
					string contents = JsonConvert.SerializeObject(system.TransferHistory, jsonSerializerSettings);
					File.WriteAllText(path, contents);
					this.LogMessage(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1095438112), system.TransferHistory.Count));
				}
				catch (Exception ex)
				{
					this.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1451923748) + ex.Message);
				}
			}
		}

		// Token: 0x06000FD1 RID: 4049 RVA: 0x001007EC File Offset: 0x000FE9EC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void LoadTerritoryTransfers(TerritoryTransferSystem system)
		{
			bool flag = system == null;
			if (!flag)
			{
				try
				{
					string path = Path.Combine(this.GetCurrentSaveFolder(), this._territoryTransfersFileName);
					bool flag2 = File.Exists(path);
					if (flag2)
					{
						string text = File.ReadAllText(path);
						List<TerritoryTransferRecord> list = JsonConvert.DeserializeObject<List<TerritoryTransferRecord>>(text);
						bool flag3 = list != null;
						if (flag3)
						{
							system.TransferHistory = list;
							this.LogMessage(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1844023666), list.Count));
						}
					}
				}
				catch (Exception ex)
				{
					this.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(2004249588) + ex.Message);
				}
			}
		}

		// Token: 0x06000FD2 RID: 4050 RVA: 0x001008A8 File Offset: 0x000FEAA8
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void SaveTributes(TributeSystem system)
		{
			bool flag = system == null;
			if (!flag)
			{
				try
				{
					string path = Path.Combine(this.GetCurrentSaveFolder(), this._tributesFileName);
					JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
					{
						Formatting = 1,
						NullValueHandling = 1
					};
					string contents = JsonConvert.SerializeObject(system.Tributes, jsonSerializerSettings);
					File.WriteAllText(path, contents);
					this.LogMessage(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(2007129059), system.Tributes.Count));
				}
				catch (Exception ex)
				{
					this.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-644571705) + ex.Message);
				}
			}
		}

		// Token: 0x06000FD3 RID: 4051 RVA: 0x00100964 File Offset: 0x000FEB64
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void LoadTributes(TributeSystem system)
		{
			bool flag = system == null;
			if (!flag)
			{
				try
				{
					string path = Path.Combine(this.GetCurrentSaveFolder(), this._tributesFileName);
					bool flag2 = File.Exists(path);
					if (flag2)
					{
						string text = File.ReadAllText(path);
						Dictionary<string, TributeAgreement> dictionary = JsonConvert.DeserializeObject<Dictionary<string, TributeAgreement>>(text);
						bool flag3 = dictionary != null;
						if (flag3)
						{
							system.Tributes = dictionary;
							this.LogMessage(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-983221548), dictionary.Count));
						}
					}
				}
				catch (Exception ex)
				{
					this.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(250783261) + ex.Message);
				}
			}
		}

		// Token: 0x06000FD4 RID: 4052 RVA: 0x00100A20 File Offset: 0x000FEC20
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void SaveReparations(ReparationsSystem system)
		{
			bool flag = system == null;
			if (!flag)
			{
				try
				{
					string path = Path.Combine(this.GetCurrentSaveFolder(), this._reparationsFileName);
					ReparationsData reparationsData = new ReparationsData
					{
						history = system.ReparationsHistory,
						pending_demands = system.PendingDemands
					};
					JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
					{
						Formatting = 1,
						NullValueHandling = 1
					};
					string contents = JsonConvert.SerializeObject(reparationsData, jsonSerializerSettings);
					File.WriteAllText(path, contents);
					this.LogMessage(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(470350800), system.ReparationsHistory.Count, system.PendingDemands.Count));
				}
				catch (Exception ex)
				{
					this.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1126636731) + ex.Message);
				}
			}
		}

		// Token: 0x06000FD5 RID: 4053 RVA: 0x00100B08 File Offset: 0x000FED08
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void LoadReparations(ReparationsSystem system)
		{
			bool flag = system == null;
			if (!flag)
			{
				try
				{
					string path = Path.Combine(this.GetCurrentSaveFolder(), this._reparationsFileName);
					bool flag2 = File.Exists(path);
					if (flag2)
					{
						string text = File.ReadAllText(path);
						ReparationsData reparationsData = JsonConvert.DeserializeObject<ReparationsData>(text);
						bool flag3 = reparationsData != null;
						if (flag3)
						{
							bool flag4 = reparationsData.history != null;
							if (flag4)
							{
								system.ReparationsHistory = reparationsData.history;
							}
							bool flag5 = reparationsData.pending_demands != null;
							if (flag5)
							{
								system.PendingDemands = reparationsData.pending_demands;
							}
							this.LogMessage(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1062609234), system.ReparationsHistory.Count, system.PendingDemands.Count));
						}
					}
				}
				catch (Exception ex)
				{
					this.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1943008108) + ex.Message);
				}
			}
		}

		// Token: 0x06000FD6 RID: 4054 RVA: 0x00100C14 File Offset: 0x000FEE14
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void SaveExpelledClans(Dictionary<string, List<ExpulsionRecord>> expelledClans)
		{
			bool flag = expelledClans == null;
			if (!flag)
			{
				try
				{
					string path = Path.Combine(this.GetCurrentSaveFolder(), this._expelledClansFileName);
					JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
					{
						Formatting = 1,
						NullValueHandling = 1
					};
					string contents = JsonConvert.SerializeObject(expelledClans, jsonSerializerSettings);
					File.WriteAllText(path, contents);
					this.LogMessage(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-2020661786), expelledClans.Count));
				}
				catch (Exception ex)
				{
					this.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1908144617) + ex.Message);
				}
			}
		}

		// Token: 0x06000FD7 RID: 4055 RVA: 0x00100CC4 File Offset: 0x000FEEC4
		[MethodImpl(MethodImplOptions.NoInlining)]
		public Dictionary<string, List<ExpulsionRecord>> LoadExpelledClans()
		{
			try
			{
				string path = Path.Combine(this.GetCurrentSaveFolder(), this._expelledClansFileName);
				bool flag = File.Exists(path);
				if (flag)
				{
					string text = File.ReadAllText(path);
					Dictionary<string, List<ExpulsionRecord>> dictionary = JsonConvert.DeserializeObject<Dictionary<string, List<ExpulsionRecord>>>(text);
					bool flag2 = dictionary != null;
					if (flag2)
					{
						int num = Enumerable.Sum<KeyValuePair<string, List<ExpulsionRecord>>>(dictionary, (KeyValuePair<string, List<ExpulsionRecord>> kv) => kv.Value.Count);
						this.LogMessage(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1495839558), num, dictionary.Count));
						return dictionary;
					}
				}
			}
			catch (Exception ex)
			{
				this.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1823895963) + ex.Message);
			}
			return new Dictionary<string, List<ExpulsionRecord>>();
		}

		// Token: 0x06000FD8 RID: 4056 RVA: 0x0001E87B File Offset: 0x0001CA7B
		private void LogMessage(string message)
		{
			DiplomacyLogger instance = DiplomacyLogger.Instance;
			if (instance != null)
			{
				instance.Log(message);
			}
		}

		// Token: 0x04000A7A RID: 2682
		private readonly string _saveDataPath;

		// Token: 0x04000A7B RID: 2683
		private readonly string _alliancesFileName = <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1017954458);

		// Token: 0x04000A7C RID: 2684
		private readonly string _warStatsFileName = <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-2128741924);

		// Token: 0x04000A7D RID: 2685
		private readonly string _diplomaticEventsFileName = <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(431605745);

		// Token: 0x04000A7E RID: 2686
		private readonly string _pendingPlayerStatementsFileName = <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1449339553);

		// Token: 0x04000A7F RID: 2687
		private readonly string _tradeAgreementsFileName = <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1184893915);

		// Token: 0x04000A80 RID: 2688
		private readonly string _territoryTransfersFileName = <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1169424809);

		// Token: 0x04000A81 RID: 2689
		private readonly string _tributesFileName = <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1140138652);

		// Token: 0x04000A82 RID: 2690
		private readonly string _reparationsFileName = <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-312018824);

		// Token: 0x04000A83 RID: 2691
		private readonly string _expelledClansFileName = <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1284241564);

		// Token: 0x04000A84 RID: 2692
		private string _cachedSaveFolder;

		// Token: 0x04000A85 RID: 2693
		private string _cachedUniqueGameId;
	}
}
