using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x02000159 RID: 345
	public class SettlementPenaltiesStorage
	{
		// Token: 0x06000AF4 RID: 2804 RVA: 0x000C5ADC File Offset: 0x000C3CDC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public SettlementPenaltiesStorage()
		{
			this._logger = SettlementCombatLogger.Instance;
			string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			string fullName = Directory.GetParent(Directory.GetParent(directoryName).FullName).FullName;
			this._saveDataPath = Path.Combine(fullName, <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1727138677));
			bool flag = !Directory.Exists(this._saveDataPath);
			if (flag)
			{
				Directory.CreateDirectory(this._saveDataPath);
				this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1003583990) + this._saveDataPath);
			}
		}

		// Token: 0x06000AF5 RID: 2805 RVA: 0x000C5B90 File Offset: 0x000C3D90
		[MethodImpl(MethodImplOptions.NoInlining)]
		private string GetCurrentSaveFolder()
		{
			Campaign campaign = Campaign.Current;
			string path = ((campaign != null) ? campaign.UniqueGameId : null) ?? <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-18599928);
			string text = Path.Combine(this._saveDataPath, path);
			bool flag = !Directory.Exists(text);
			if (flag)
			{
				Directory.CreateDirectory(text);
				this._logger.Log(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1784669680) + text);
			}
			return text;
		}

		// Token: 0x06000AF6 RID: 2806 RVA: 0x000C5C08 File Offset: 0x000C3E08
		private string GetPenaltiesFilePath()
		{
			return Path.Combine(this.GetCurrentSaveFolder(), this._penaltiesFileName);
		}

		// Token: 0x06000AF7 RID: 2807 RVA: 0x000C5C2C File Offset: 0x000C3E2C
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void SavePenalties(Dictionary<string, ActivePenalty> penalties)
		{
			bool flag = penalties == null;
			if (flag)
			{
				this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1883249818));
			}
			else
			{
				try
				{
					string penaltiesFilePath = this.GetPenaltiesFilePath();
					JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
					{
						Formatting = 1,
						NullValueHandling = 1
					};
					string contents = JsonConvert.SerializeObject(penalties, jsonSerializerSettings);
					File.WriteAllText(penaltiesFilePath, contents);
					this._logger.Log(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1387506287), penalties.Count, penaltiesFilePath));
				}
				catch (Exception ex)
				{
					this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-131092902), ex.Message, ex);
				}
			}
		}

		// Token: 0x06000AF8 RID: 2808 RVA: 0x000C5CF4 File Offset: 0x000C3EF4
		[MethodImpl(MethodImplOptions.NoInlining)]
		public Dictionary<string, ActivePenalty> LoadPenalties()
		{
			Dictionary<string, ActivePenalty> result;
			try
			{
				string penaltiesFilePath = this.GetPenaltiesFilePath();
				bool flag = !File.Exists(penaltiesFilePath);
				if (flag)
				{
					this._logger.Log(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(946248439) + penaltiesFilePath);
					result = new Dictionary<string, ActivePenalty>();
				}
				else
				{
					string text = File.ReadAllText(penaltiesFilePath);
					bool flag2 = string.IsNullOrWhiteSpace(text);
					if (flag2)
					{
						this._logger.Log(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1781502845));
						result = new Dictionary<string, ActivePenalty>();
					}
					else
					{
						Dictionary<string, ActivePenalty> dictionary = JsonConvert.DeserializeObject<Dictionary<string, ActivePenalty>>(text);
						this._logger.Log(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-300296015), (dictionary != null) ? dictionary.Count : 0, penaltiesFilePath));
						result = (dictionary ?? new Dictionary<string, ActivePenalty>());
					}
				}
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(910965023), ex.Message, ex);
				result = new Dictionary<string, ActivePenalty>();
			}
			return result;
		}

		// Token: 0x040006BD RID: 1725
		private readonly string _saveDataPath;

		// Token: 0x040006BE RID: 1726
		private readonly string _penaltiesFileName = <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(2089621662);

		// Token: 0x040006BF RID: 1727
		private readonly SettlementCombatLogger _logger;
	}
}
