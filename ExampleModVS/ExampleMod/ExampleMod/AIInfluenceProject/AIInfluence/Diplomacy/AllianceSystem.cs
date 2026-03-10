using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AIInfluence.Util;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;

namespace AIInfluence.Diplomacy
{
	// Token: 0x020001C2 RID: 450
	public class AllianceSystem
	{
		// Token: 0x170002DE RID: 734
		// (get) Token: 0x06000E61 RID: 3681 RVA: 0x000F1AC0 File Offset: 0x000EFCC0
		public static AllianceSystem Instance
		{
			get
			{
				bool flag = AllianceSystem._instance == null;
				if (flag)
				{
					AllianceSystem._instance = new AllianceSystem();
				}
				return AllianceSystem._instance;
			}
		}

		// Token: 0x170002DF RID: 735
		// (get) Token: 0x06000E62 RID: 3682 RVA: 0x0001DF52 File Offset: 0x0001C152
		// (set) Token: 0x06000E63 RID: 3683 RVA: 0x0001DF5A File Offset: 0x0001C15A
		[JsonProperty("alliances")]
		public Dictionary<string, List<string>> Alliances { get; set; } = new Dictionary<string, List<string>>();

		// Token: 0x170002E0 RID: 736
		// (get) Token: 0x06000E64 RID: 3684 RVA: 0x0001DF63 File Offset: 0x0001C163
		// (set) Token: 0x06000E65 RID: 3685 RVA: 0x0001DF6B File Offset: 0x0001C16B
		[JsonProperty("alliance_times")]
		public Dictionary<string, CampaignTime> AllianceTimes { get; set; } = new Dictionary<string, CampaignTime>();

		// Token: 0x06000E66 RID: 3686 RVA: 0x0001DF74 File Offset: 0x0001C174
		private AllianceSystem()
		{
			this._storage = new DiplomacyStorage();
		}

		// Token: 0x06000E67 RID: 3687 RVA: 0x000F1AF0 File Offset: 0x000EFCF0
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void Initialize()
		{
			this.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1483307968));
			this._storage.LoadAlliances(this);
			this.SynchronizeAlliances();
			this.LogMessage(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1288203996), this.Alliances.Count));
		}

		// Token: 0x06000E68 RID: 3688 RVA: 0x000F1B50 File Offset: 0x000EFD50
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void SynchronizeAlliances()
		{
			this.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-377286067));
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
						string allianceKey = this.GetAllianceKey(kingdom1.StringId, kingdom.StringId);
						bool flag = hashSet.Contains(allianceKey);
						if (!flag)
						{
							hashSet.Add(allianceKey);
							bool flag2 = this.Alliances.ContainsKey(kingdom1.StringId) && this.Alliances[kingdom1.StringId].Contains(kingdom.StringId);
							bool flag3 = GameVersionCompatibility.IsAlliedWithFaction(kingdom1, kingdom);
							bool flag4 = flag2 && !flag3;
							if (flag4)
							{
								this.LogMessage(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-957596332), kingdom1.Name, kingdom.Name));
								this.CreateAllianceInGame(kingdom1, kingdom);
								num++;
							}
							else
							{
								bool flag5 = !flag2 && flag3;
								if (flag5)
								{
									this.LogMessage(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-979648467), kingdom1.Name, kingdom.Name));
									this.BreakAllianceInGame(kingdom1, kingdom);
									num2++;
								}
							}
						}
					}
				}
			}
			this.LogMessage(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(442500675), num, num2));
		}

		// Token: 0x06000E69 RID: 3689 RVA: 0x0001DF9F File Offset: 0x0001C19F
		public void SaveData()
		{
			this._storage.SaveAlliances(this);
		}

		// Token: 0x06000E6A RID: 3690 RVA: 0x000F1DC4 File Offset: 0x000EFFC4
		public bool AreAllied(Kingdom kingdom1, Kingdom kingdom2)
		{
			bool flag = kingdom1 == null || kingdom2 == null;
			bool result;
			if (flag)
			{
				result = false;
			}
			else
			{
				bool flag2 = kingdom1 == kingdom2;
				if (flag2)
				{
					result = false;
				}
				else
				{
					bool flag3 = GameVersionCompatibility.IsAlliedWithFaction(kingdom1, kingdom2);
					if (flag3)
					{
						result = true;
					}
					else
					{
						string stringId = kingdom1.StringId;
						string stringId2 = kingdom2.StringId;
						result = (this.Alliances.ContainsKey(stringId) && this.Alliances[stringId].Contains(stringId2));
					}
				}
			}
			return result;
		}

		// Token: 0x06000E6B RID: 3691 RVA: 0x000F1E38 File Offset: 0x000F0038
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void CreateAlliance(Kingdom kingdom1, Kingdom kingdom2)
		{
			bool flag = kingdom1 == null || kingdom2 == null;
			if (flag)
			{
				this.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(967320296));
			}
			else
			{
				bool flag2 = kingdom1 == kingdom2;
				if (flag2)
				{
					this.LogMessage(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(214788648));
				}
				else
				{
					bool flag3 = this.AreAllied(kingdom1, kingdom2);
					if (flag3)
					{
						this.LogMessage(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-345938936), kingdom1.Name, kingdom2.Name));
					}
					else
					{
						string stringId = kingdom1.StringId;
						string stringId2 = kingdom2.StringId;
						bool flag4 = !this.Alliances.ContainsKey(stringId);
						if (flag4)
						{
							this.Alliances[stringId] = new List<string>();
						}
						bool flag5 = !this.Alliances.ContainsKey(stringId2);
						if (flag5)
						{
							this.Alliances[stringId2] = new List<string>();
						}
						this.Alliances[stringId].Add(stringId2);
						this.Alliances[stringId2].Add(stringId);
						string allianceKey = this.GetAllianceKey(stringId, stringId2);
						this.AllianceTimes[allianceKey] = CampaignTime.Now;
						DiplomacyPatches.WithBypass(delegate
						{
							GameVersionCompatibility.DeclareAlliance(kingdom1, kingdom2);
						});
						bool flag6 = GameVersionCompatibility.IsAlliedWithFaction(kingdom1, kingdom2);
						this.LogMessage(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(152175996), kingdom1.Name, kingdom2.Name, flag6));
						DiplomacyLogger.Instance.LogDiplomaticAction(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-2086393009), stringId, stringId2, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-515425665));
						this.SaveData();
					}
				}
			}
		}

		// Token: 0x06000E6C RID: 3692 RVA: 0x000F2034 File Offset: 0x000F0234
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void CreateAllianceInGame(Kingdom kingdom1, Kingdom kingdom2)
		{
			bool flag = kingdom1 == null || kingdom2 == null || kingdom1 == kingdom2;
			if (!flag)
			{
				bool flag2 = GameVersionCompatibility.IsAlliedWithFaction(kingdom1, kingdom2);
				if (flag2)
				{
					this.LogMessage(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1797527605), kingdom1.Name, kingdom2.Name));
				}
				else
				{
					DiplomacyPatches.WithBypass(delegate
					{
						GameVersionCompatibility.DeclareAlliance(kingdom1, kingdom2);
					});
					bool flag3 = GameVersionCompatibility.IsAlliedWithFaction(kingdom1, kingdom2);
					this.LogMessage(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1851982161), kingdom1.Name, kingdom2.Name, flag3));
				}
			}
		}

		// Token: 0x06000E6D RID: 3693 RVA: 0x000F2120 File Offset: 0x000F0320
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void BreakAlliance(Kingdom kingdom1, Kingdom kingdom2)
		{
			bool flag = kingdom1 == null || kingdom2 == null;
			if (!flag)
			{
				bool flag2 = !this.AreAllied(kingdom1, kingdom2);
				if (flag2)
				{
					this.LogMessage(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-951874574), kingdom1.Name, kingdom2.Name));
				}
				else
				{
					string stringId = kingdom1.StringId;
					string stringId2 = kingdom2.StringId;
					bool flag3 = this.Alliances.ContainsKey(stringId);
					if (flag3)
					{
						this.Alliances[stringId].Remove(stringId2);
					}
					bool flag4 = this.Alliances.ContainsKey(stringId2);
					if (flag4)
					{
						this.Alliances[stringId2].Remove(stringId);
					}
					string allianceKey = this.GetAllianceKey(stringId, stringId2);
					this.AllianceTimes.Remove(allianceKey);
					DiplomacyPatches.WithBypass(delegate
					{
						GameVersionCompatibility.EndAlliance(kingdom1, kingdom2);
					});
					this.LogMessage(string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1582565762), kingdom1.Name, kingdom2.Name));
					DiplomacyLogger.Instance.LogDiplomaticAction(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1486198899), stringId, stringId2, <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1874713784));
					this.SaveData();
				}
			}
		}

		// Token: 0x06000E6E RID: 3694 RVA: 0x000F2294 File Offset: 0x000F0494
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void BreakAllianceInGame(Kingdom kingdom1, Kingdom kingdom2)
		{
			bool flag = kingdom1 == null || kingdom2 == null;
			if (!flag)
			{
				bool flag2 = !GameVersionCompatibility.IsAlliedWithFaction(kingdom1, kingdom2);
				if (flag2)
				{
					this.LogMessage(string.Format(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1658525846), kingdom1.Name, kingdom2.Name));
				}
				else
				{
					DiplomacyPatches.WithBypass(delegate
					{
						GameVersionCompatibility.EndAlliance(kingdom1, kingdom2);
					});
					this.LogMessage(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(448541132), kingdom1.Name, kingdom2.Name));
				}
			}
		}

		// Token: 0x06000E6F RID: 3695 RVA: 0x000F235C File Offset: 0x000F055C
		public List<Kingdom> GetAllies(Kingdom kingdom)
		{
			bool flag = kingdom == null;
			List<Kingdom> result;
			if (flag)
			{
				result = new List<Kingdom>();
			}
			else
			{
				string stringId = kingdom.StringId;
				bool flag2 = !this.Alliances.ContainsKey(stringId);
				if (flag2)
				{
					result = new List<Kingdom>();
				}
				else
				{
					List<string> list = this.Alliances[stringId];
					List<Kingdom> list2 = new List<Kingdom>();
					using (List<string>.Enumerator enumerator = list.GetEnumerator())
					{
						while (enumerator.MoveNext())
						{
							string allyId = enumerator.Current;
							Kingdom kingdom2 = Enumerable.FirstOrDefault<Kingdom>(Kingdom.All, (Kingdom k) => k.StringId == allyId);
							bool flag3 = kingdom2 != null && !kingdom2.IsEliminated;
							if (flag3)
							{
								list2.Add(kingdom2);
							}
						}
					}
					result = list2;
				}
			}
			return result;
		}

		// Token: 0x06000E70 RID: 3696 RVA: 0x000F2448 File Offset: 0x000F0648
		[MethodImpl(MethodImplOptions.NoInlining)]
		public string GetAllianceInfoForAI(Kingdom kingdom)
		{
			bool flag = kingdom == null;
			string result;
			if (flag)
			{
				result = <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(606694831);
			}
			else
			{
				List<Kingdom> allies = this.GetAllies(kingdom);
				bool flag2 = !Enumerable.Any<Kingdom>(allies);
				if (flag2)
				{
					result = string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1449149606), kingdom.Name);
				}
				else
				{
					List<string> list = new List<string>();
					foreach (Kingdom kingdom2 in allies)
					{
						string allianceKey = this.GetAllianceKey(kingdom.StringId, kingdom2.StringId);
						CampaignTime g;
						bool flag3 = this.AllianceTimes.TryGetValue(allianceKey, out g);
						if (flag3)
						{
							int num = (int)(CampaignTime.Now - g).ToDays;
							list.Add(string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1402400786), kingdom2.Name, num));
						}
						else
						{
							list.Add(string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1019353465), kingdom2.Name));
						}
					}
					result = string.Format(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(371477937), kingdom.Name, string.Join(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-234543546), list));
				}
			}
			return result;
		}

		// Token: 0x06000E71 RID: 3697 RVA: 0x000F25AC File Offset: 0x000F07AC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public string GetDetailedAllianceInfo(Kingdom kingdom)
		{
			bool flag = kingdom == null;
			string result;
			if (flag)
			{
				result = <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1574550191);
			}
			else
			{
				List<Kingdom> allies = this.GetAllies(kingdom);
				bool flag2 = !Enumerable.Any<Kingdom>(allies);
				if (flag2)
				{
					result = string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-968582601), kingdom.Name);
				}
				else
				{
					string text = string.Format(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-383295744), kingdom.Name);
					foreach (Kingdom kingdom2 in allies)
					{
						string allianceKey = this.GetAllianceKey(kingdom.StringId, kingdom2.StringId);
						CampaignTime g;
						bool flag3 = this.AllianceTimes.TryGetValue(allianceKey, out g);
						if (flag3)
						{
							int num = (int)(CampaignTime.Now - g).ToDays;
							text += string.Format(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-71889107), kingdom2.Name, num);
						}
						else
						{
							text += string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1444770996), kingdom2.Name);
						}
					}
					result = text;
				}
			}
			return result;
		}

		// Token: 0x06000E72 RID: 3698 RVA: 0x000F26F8 File Offset: 0x000F08F8
		public bool WouldBeIsolated(Kingdom kingdom, Kingdom allyToRemove)
		{
			bool flag = kingdom == null;
			bool result;
			if (flag)
			{
				result = false;
			}
			else
			{
				List<Kingdom> allies = this.GetAllies(kingdom);
				result = (allies.Count <= 1 && allies.Contains(allyToRemove));
			}
			return result;
		}

		// Token: 0x06000E73 RID: 3699 RVA: 0x000F2734 File Offset: 0x000F0934
		public int GetAllianceStrength(Kingdom kingdom)
		{
			bool flag = kingdom == null;
			int result;
			if (flag)
			{
				result = 0;
			}
			else
			{
				result = this.GetAllies(kingdom).Count;
			}
			return result;
		}

		// Token: 0x06000E74 RID: 3700 RVA: 0x000F2760 File Offset: 0x000F0960
		public bool HaveCommonAllies(Kingdom kingdom1, Kingdom kingdom2)
		{
			bool flag = kingdom1 == null || kingdom2 == null;
			bool result;
			if (flag)
			{
				result = false;
			}
			else
			{
				List<Kingdom> allies = this.GetAllies(kingdom1);
				List<Kingdom> allies2 = this.GetAllies(kingdom2);
				result = Enumerable.Any<Kingdom>(Enumerable.Intersect<Kingdom>(allies, allies2));
			}
			return result;
		}

		// Token: 0x06000E75 RID: 3701 RVA: 0x000F27A0 File Offset: 0x000F09A0
		public List<Kingdom> GetEnemyAllies(Kingdom kingdom, Kingdom enemy)
		{
			bool flag = kingdom == null || enemy == null;
			List<Kingdom> result;
			if (flag)
			{
				result = new List<Kingdom>();
			}
			else
			{
				List<Kingdom> allies = this.GetAllies(enemy);
				List<Kingdom> myAllies = this.GetAllies(kingdom);
				result = Enumerable.ToList<Kingdom>(Enumerable.Where<Kingdom>(allies, (Kingdom ally) => !myAllies.Contains(ally) && ally != kingdom));
			}
			return result;
		}

		// Token: 0x06000E76 RID: 3702 RVA: 0x000F280C File Offset: 0x000F0A0C
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void CleanupEliminatedKingdoms()
		{
			List<string> list = Enumerable.ToList<string>(Enumerable.Select<Kingdom, string>(Enumerable.Where<Kingdom>(Kingdom.All, (Kingdom k) => k.IsEliminated), (Kingdom k) => k.StringId));
			foreach (string text in list)
			{
				bool flag = this.Alliances.ContainsKey(text);
				if (flag)
				{
					List<string> list2 = new List<string>(this.Alliances[text]);
					foreach (string text2 in list2)
					{
						bool flag2 = this.Alliances.ContainsKey(text2);
						if (flag2)
						{
							this.Alliances[text2].Remove(text);
						}
						string allianceKey = this.GetAllianceKey(text, text2);
						this.AllianceTimes.Remove(allianceKey);
					}
					this.Alliances.Remove(text);
					this.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-130506436) + text);
				}
			}
		}

		// Token: 0x06000E77 RID: 3703 RVA: 0x000F2980 File Offset: 0x000F0B80
		[MethodImpl(MethodImplOptions.NoInlining)]
		private string GetAllianceKey(string kingdom1Id, string kingdom2Id)
		{
			bool flag = string.Compare(kingdom1Id, kingdom2Id, StringComparison.Ordinal) < 0;
			string result;
			if (flag)
			{
				result = kingdom1Id + <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(223901966) + kingdom2Id;
			}
			else
			{
				result = kingdom2Id + <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(235954832) + kingdom1Id;
			}
			return result;
		}

		// Token: 0x06000E78 RID: 3704 RVA: 0x000F29CC File Offset: 0x000F0BCC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public List<string> GetAllActiveAlliances()
		{
			List<string> list = new List<string>();
			HashSet<string> hashSet = new HashSet<string>();
			foreach (KeyValuePair<string, List<string>> keyValuePair in this.Alliances)
			{
				string kingdom1Id = keyValuePair.Key;
				using (List<string>.Enumerator enumerator2 = keyValuePair.Value.GetEnumerator())
				{
					Func<Kingdom, bool> <>9__0;
					while (enumerator2.MoveNext())
					{
						string kingdom2Id = enumerator2.Current;
						string allianceKey = this.GetAllianceKey(kingdom1Id, kingdom2Id);
						bool flag = !hashSet.Contains(allianceKey);
						if (flag)
						{
							IEnumerable<Kingdom> all = Kingdom.All;
							Func<Kingdom, bool> func;
							if ((func = <>9__0) == null)
							{
								func = (<>9__0 = ((Kingdom k) => k.StringId == kingdom1Id));
							}
							Kingdom kingdom = Enumerable.FirstOrDefault<Kingdom>(all, func);
							Kingdom kingdom2 = Enumerable.FirstOrDefault<Kingdom>(Kingdom.All, (Kingdom k) => k.StringId == kingdom2Id);
							bool flag2 = kingdom != null && kingdom2 != null;
							if (flag2)
							{
								list.Add(string.Format(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-934957635), kingdom.Name, kingdom2.Name));
							}
							hashSet.Add(allianceKey);
						}
					}
				}
			}
			return list;
		}

		// Token: 0x06000E79 RID: 3705 RVA: 0x0001DFAF File Offset: 0x0001C1AF
		private void LogMessage(string message)
		{
			AIInfluenceBehavior instance = AIInfluenceBehavior.Instance;
			if (instance != null)
			{
				instance.LogMessage(message);
			}
		}

		// Token: 0x0400091B RID: 2331
		private static AllianceSystem _instance;

		// Token: 0x0400091E RID: 2334
		private readonly DiplomacyStorage _storage;
	}
}
