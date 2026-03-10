using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using \u200F\u202A\u206C\u202C\u202B\u206B\u206C\u202A\u200B\u200D\u202D\u202A\u200F\u200E\u202D\u200F\u202E\u200E\u206F\u202A\u200C\u206A\u206F\u206F\u202A\u202D\u206A\u200F\u202C\u206A\u206A\u206B\u200B\u206D\u206A\u200D\u206D\u200F\u206B\u202B\u202E;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence.DynamicEvents
{
	// Token: 0x02000162 RID: 354
	[JsonSerializable]
	public class DynamicEvent
	{
		// Token: 0x1700022B RID: 555
		// (get) Token: 0x06000B41 RID: 2881 RVA: 0x000C8114 File Offset: 0x000C6314
		// (set) Token: 0x06000B42 RID: 2882 RVA: 0x000C8128 File Offset: 0x000C6328
		[JsonProperty("id")]
		public string Id { get; set; }

		// Token: 0x1700022C RID: 556
		// (get) Token: 0x06000B43 RID: 2883 RVA: 0x000C813C File Offset: 0x000C633C
		// (set) Token: 0x06000B44 RID: 2884 RVA: 0x000C8150 File Offset: 0x000C6350
		[JsonProperty("type")]
		public string Type { get; set; }

		// Token: 0x1700022D RID: 557
		// (get) Token: 0x06000B45 RID: 2885 RVA: 0x000C8164 File Offset: 0x000C6364
		// (set) Token: 0x06000B46 RID: 2886 RVA: 0x000C8178 File Offset: 0x000C6378
		[JsonProperty("title")]
		public string Title { get; set; }

		// Token: 0x1700022E RID: 558
		// (get) Token: 0x06000B47 RID: 2887 RVA: 0x000C818C File Offset: 0x000C638C
		// (set) Token: 0x06000B48 RID: 2888 RVA: 0x000C81A0 File Offset: 0x000C63A0
		[JsonProperty("description")]
		public string Description { get; set; }

		// Token: 0x1700022F RID: 559
		// (get) Token: 0x06000B49 RID: 2889 RVA: 0x000C81B4 File Offset: 0x000C63B4
		// (set) Token: 0x06000B4A RID: 2890 RVA: 0x000C81C8 File Offset: 0x000C63C8
		[JsonProperty("event_history")]
		public List<EventUpdate> EventHistory { get; set; } = new List<EventUpdate>();

		// Token: 0x17000230 RID: 560
		// (get) Token: 0x06000B4B RID: 2891 RVA: 0x000C81DC File Offset: 0x000C63DC
		// (set) Token: 0x06000B4C RID: 2892 RVA: 0x000C81F0 File Offset: 0x000C63F0
		[JsonProperty("player_involved")]
		public bool PlayerInvolved { get; set; }

		// Token: 0x17000231 RID: 561
		// (get) Token: 0x06000B4D RID: 2893 RVA: 0x000C8204 File Offset: 0x000C6404
		// (set) Token: 0x06000B4E RID: 2894 RVA: 0x000C8218 File Offset: 0x000C6418
		[JsonProperty("kingdoms_involved")]
		public object KingdomsInvolved { get; set; }

		// Token: 0x17000232 RID: 562
		// (get) Token: 0x06000B4F RID: 2895 RVA: 0x000C822C File Offset: 0x000C642C
		// (set) Token: 0x06000B50 RID: 2896 RVA: 0x000C8240 File Offset: 0x000C6440
		[JsonProperty("characters_involved")]
		public List<string> CharactersInvolved { get; set; } = new List<string>();

		// Token: 0x17000233 RID: 563
		// (get) Token: 0x06000B51 RID: 2897 RVA: 0x000C8254 File Offset: 0x000C6454
		// (set) Token: 0x06000B52 RID: 2898 RVA: 0x000C8268 File Offset: 0x000C6468
		[JsonProperty("importance")]
		public int Importance { get; set; }

		// Token: 0x17000234 RID: 564
		// (get) Token: 0x06000B53 RID: 2899 RVA: 0x000C827C File Offset: 0x000C647C
		// (set) Token: 0x06000B54 RID: 2900 RVA: 0x000C8290 File Offset: 0x000C6490
		[JsonProperty("spread_speed")]
		public string SpreadSpeed { get; set; }

		// Token: 0x17000235 RID: 565
		// (get) Token: 0x06000B55 RID: 2901 RVA: 0x000C82A4 File Offset: 0x000C64A4
		// (set) Token: 0x06000B56 RID: 2902 RVA: 0x000C82B8 File Offset: 0x000C64B8
		[JsonProperty("allows_diplomatic_response")]
		public bool AllowsDiplomaticResponse { get; set; }

		// Token: 0x17000236 RID: 566
		// (get) Token: 0x06000B57 RID: 2903 RVA: 0x000C82CC File Offset: 0x000C64CC
		// (set) Token: 0x06000B58 RID: 2904 RVA: 0x000C82E0 File Offset: 0x000C64E0
		[JsonProperty("applicable_npcs")]
		public List<string> ApplicableNPCs { get; set; } = new List<string>();

		// Token: 0x17000237 RID: 567
		// (get) Token: 0x06000B59 RID: 2905 RVA: 0x000C82F4 File Offset: 0x000C64F4
		// (set) Token: 0x06000B5A RID: 2906 RVA: 0x000C8308 File Offset: 0x000C6508
		[JsonProperty("settlement_penalty")]
		public SettlementPenaltyData SettlementPenalty { get; set; }

		// Token: 0x17000238 RID: 568
		// (get) Token: 0x06000B5B RID: 2907 RVA: 0x000C831C File Offset: 0x000C651C
		// (set) Token: 0x06000B5C RID: 2908 RVA: 0x000C8330 File Offset: 0x000C6530
		[JsonProperty("economic_effects")]
		public List<EconomicEffect> EconomicEffects { get; set; } = new List<EconomicEffect>();

		// Token: 0x17000239 RID: 569
		// (get) Token: 0x06000B5D RID: 2909 RVA: 0x000C8344 File Offset: 0x000C6544
		// (set) Token: 0x06000B5E RID: 2910 RVA: 0x000C8358 File Offset: 0x000C6558
		[JsonProperty("creation_time")]
		public DateTime CreationTime { get; set; }

		// Token: 0x1700023A RID: 570
		// (get) Token: 0x06000B5F RID: 2911 RVA: 0x000C836C File Offset: 0x000C656C
		// (set) Token: 0x06000B60 RID: 2912 RVA: 0x000C8380 File Offset: 0x000C6580
		[JsonProperty("creation_campaign_days")]
		public float CreationCampaignDays { get; set; }

		// Token: 0x1700023B RID: 571
		// (get) Token: 0x06000B61 RID: 2913 RVA: 0x000C8394 File Offset: 0x000C6594
		// (set) Token: 0x06000B62 RID: 2914 RVA: 0x000C83A8 File Offset: 0x000C65A8
		[JsonProperty("expiration_time")]
		public DateTime ExpirationTime { get; set; }

		// Token: 0x1700023C RID: 572
		// (get) Token: 0x06000B63 RID: 2915 RVA: 0x000C83BC File Offset: 0x000C65BC
		// (set) Token: 0x06000B64 RID: 2916 RVA: 0x000C83D0 File Offset: 0x000C65D0
		[JsonProperty("expiration_campaign_days")]
		public float ExpirationCampaignDays { get; set; }

		// Token: 0x1700023D RID: 573
		// (get) Token: 0x06000B65 RID: 2917 RVA: 0x000C83E4 File Offset: 0x000C65E4
		[JsonIgnore]
		public int DaysSinceCreation
		{
			[MethodImpl(MethodImplOptions.NoInlining)]
			get
			{
				\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ();
				bool flag = false;
				if (flag)
				{
					goto IL_11;
				}
				goto IL_6B;
				int num2;
				int result;
				for (;;)
				{
					IL_16:
					int num = num2;
					uint num3;
					switch ((num3 = (uint)((num + -531566284 ^ 529537682 ^ 204407546) * 1576456841)) % 5U)
					{
					case 0U:
						goto IL_11;
					case 1U:
						result = 0;
						num2 = (int)(num3 * 30043912U ^ 2655062502U);
						continue;
					case 2U:
						goto IL_56;
					case 4U:
						goto IL_6B;
					}
					break;
				}
				return result;
				IL_11:
				num2 = 1820580703;
				goto IL_16;
				IL_56:
				float num4;
				result = \u202B\u206E\u206D\u202E\u202C\u202B\u200F\u202D\u206B\u202A\u206C\u206D\u202D\u200F\u206D\u200B\u200D\u200C\u202B\u200C\u206A\u206E\u200F\u202D\u202B\u202C\u206F\u200B\u200B\u202E\u202C\u202C\u202B\u206C\u206C\u200B\u202C\u206B\u200D\u202A\u202E.xÙ(ã\u0013(0, (int)num4);
				num2 = -655755650;
				goto IL_16;
				IL_6B:
				float num5 = (float)\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u200D\u200D\u200C\u202E\u200B\u206F\u202C\u206B\u206A\u202A\u206D\u202E\u202A\u202B\u202B\u206F\u206D\u206E\u200B\u202C\u200D\u200F\u206C\u200C\u200C\u200D\u200F\u200C\u202E\u206A\u200B\u200B\u202C\u206E\u202A\u200C\u206B\u200B\u202E\u206B\u202E().ToDays;
				num4 = num5 - this.CreationCampaignDays;
				DynamicEventsLogger instance = DynamicEventsLogger.Instance;
				if (instance == null)
				{
					goto IL_56;
				}
				instance.Log(\u206B\u202E\u206F\u206A\u200D\u206B\u202D\u202E\u206F\u200D\u206F\u200B\u206A\u206B\u206C\u200E\u202C\u202E\u206F\u200B\u206B\u206B\u200B\u200F\u200D\u200B\u202B\u200D\u202B\u202D\u206A\u206B\u200F\u202E\u206F\u206B\u206D\u202C\u206E\u200D\u202E.y=\u001E/\u0091(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-397013032), num5, this.CreationCampaignDays, num4));
				num2 = 1625630260;
				goto IL_16;
			}
		}

		// Token: 0x06000B66 RID: 2918 RVA: 0x000C84D4 File Offset: 0x000C66D4
		[MethodImpl(MethodImplOptions.NoInlining)]
		public DynamicEvent()
		{
			this.Id = \u206A\u200C\u200F\u200C\u202C\u200B\u206D\u202B\u206A\u206F\u206D\u206C\u202E\u200D\u200F\u206E\u200C\u206F\u200C\u206A\u206F\u200E\u200E\u206B\u206C\u206D\u206A\u200C\u206B\u206E\u200B\u206A\u200E\u202B\u202C\u202A\u202E\u200C\u200D\u202C\u202E.åËø\u00A6Ó().ToString();
			this.CreationTime = \u200B\u202A\u206E\u202A\u202D\u206D\u202B\u200E\u202D\u200F\u206D\u206A\u202B\u206D\u202C\u200F\u206F\u202C\u202C\u200C\u202C\u200F\u200F\u202E\u202E\u206B\u206C\u206D\u206C\u200D\u202A\u200C\u206E\u202E\u200E\u200E\u206D\u202B\u200D\u200D\u202E.Y\u000C8FÛ();
			this.Type = <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(2093045641);
			this.Title = string.Empty;
			this.Importance = 5;
			this.SpreadSpeed = <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(460298598);
			this.PlayerInvolved = false;
			this.AllowsDiplomaticResponse = true;
			this.EventHistory = new List<EventUpdate>();
		}

		// Token: 0x06000B67 RID: 2919 RVA: 0x000C85E0 File Offset: 0x000C67E0
		public bool IsExpired()
		{
			\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ();
			bool flag = false;
			if (flag)
			{
				goto IL_11;
			}
			goto IL_51;
			int num2;
			bool result;
			for (;;)
			{
				IL_16:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(~(~(-num)) - 526223041)) % 4U)
				{
				case 0U:
					goto IL_11;
				case 1U:
					goto IL_51;
				case 2U:
					result = false;
					num2 = (int)(num3 * 739259974U ^ 2705770876U);
					continue;
				}
				break;
			}
			return result;
			IL_11:
			num2 = -1379102987;
			goto IL_16;
			IL_51:
			float num4 = (float)\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u200D\u200D\u200C\u202E\u200B\u206F\u202C\u206B\u206A\u202A\u206D\u202E\u202A\u202B\u202B\u206F\u206D\u206E\u200B\u202C\u200D\u200F\u206C\u200C\u200C\u200D\u200F\u200C\u202E\u206A\u200B\u200B\u202C\u206E\u202A\u200C\u206B\u200B\u202E\u206B\u202E().ToDays;
			result = (num4 > this.ExpirationCampaignDays);
			num2 = -1009818304;
			goto IL_16;
		}

		// Token: 0x06000B68 RID: 2920 RVA: 0x000C8660 File Offset: 0x000C6860
		public void AddEventUpdate(string newDescription, string updateReason = "Event Update")
		{
			bool flag = this.EventHistory == null;
			if (flag)
			{
				for (;;)
				{
					IL_0E:
					int num = 867980181;
					for (;;)
					{
						int num2 = num;
						uint num3;
						switch ((num3 = (uint)(-(uint)(-num2 - -615072771 * (173012387 * -1977878586) ^ (-1080656227 ^ 2007681958)))) % 3U)
						{
						case 0U:
							goto IL_0E;
						case 2U:
							this.EventHistory = new List<EventUpdate>();
							num = (int)(num3 * 324406323U ^ 3678153997U);
							continue;
						}
						goto Block_1;
					}
				}
				Block_1:;
			}
			float num4 = (float)\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u200D\u200D\u200C\u202E\u200B\u206F\u202C\u206B\u206A\u202A\u206D\u202E\u202A\u202B\u202B\u206F\u206D\u206E\u200B\u202C\u200D\u200F\u206C\u200C\u200C\u200D\u200F\u200C\u202E\u206A\u200B\u200B\u202C\u206E\u202A\u200C\u206B\u200B\u202E\u206B\u202E().ToDays;
			int daysSinceCreation = \u202B\u206E\u206D\u202E\u202C\u202B\u200F\u202D\u206B\u202A\u206C\u206D\u202D\u200F\u206D\u200B\u200D\u200C\u202B\u200C\u206A\u206E\u200F\u202D\u202B\u202C\u206F\u200B\u200B\u202E\u202C\u202C\u202B\u206C\u206C\u200B\u202C\u206B\u200D\u202A\u202E.xÙ(ã\u0013(0, (int)(num4 - this.CreationCampaignDays));
			EventUpdate item = new EventUpdate(newDescription, updateReason)
			{
				DaysSinceCreation = daysSinceCreation
			};
			this.EventHistory.Add(item);
			this.Description = newDescription;
		}

		// Token: 0x06000B69 RID: 2921 RVA: 0x000C8724 File Offset: 0x000C6924
		[MethodImpl(MethodImplOptions.NoInlining)]
		public string GetEventHistoryForPrompt()
		{
			if (this.EventHistory != null)
			{
				goto IL_0C;
			}
			bool flag = true;
			goto IL_17D;
			int num2;
			string result;
			for (;;)
			{
				IL_11:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(-1823550986 - ((num - (~(1138524909 + -581799173) ^ ~-1076021139 * 452014125)) * -741382655 - ~-2095469843))) % 11U)
				{
				case 0U:
					goto IL_0C;
				case 1U:
				{
					StringBuilder stringBuilder;
					\u206F\u200B\u202A\u202E\u202E\u206B\u206A\u200D\u200B\u202E\u206D\u200F\u206B\u206B\u200B\u206D\u202E\u202D\u202D\u206B\u200B\u202E\u200E\u200E\u200D\u200B\u202A\u202B\u202A\u200D\u200F\u202D\u206C\u202C\u200D\u200B\u202B\u206F\u206C\u202D\u202E.i\u00B6ø\u00ACô(stringBuilder);
					num2 = (int)(num3 * 2953970605U ^ 897411768U);
					continue;
				}
				case 2U:
				{
					int num4 = 0;
					num2 = (int)(num3 * 1969386753U ^ 3678399963U);
					continue;
				}
				case 3U:
				{
					int num4;
					List<EventUpdate> list;
					num2 = ((num4 < list.Count) ? -1764128409 : -504581133);
					continue;
				}
				case 4U:
				{
					int num4;
					num4++;
					num2 = 996763684;
					continue;
				}
				case 5U:
					result = this.Description;
					num2 = (int)(num3 * 1022142715U ^ 701293839U);
					continue;
				case 6U:
				{
					int num4;
					List<EventUpdate> list;
					EventUpdate eventUpdate = list[num4];
					StringBuilder stringBuilder;
					\u206D\u206C\u206B\u206E\u206B\u206A\u200F\u202B\u200C\u200F\u202A\u200C\u200F\u202D\u202B\u206C\u206A\u206D\u200B\u200C\u206C\u202C\u200D\u200E\u202A\u206E\u202A\u200C\u200D\u200C\u206A\u202C\u200B\u206E\u202B\u200C\u202B\u206E\u202B\u202E.w\u00B7\u000Cæ\u00B0(stringBuilder, \u200B\u202D\u206E\u206D\u200D\u200B\u206B\u200B\u200E\u206C\u202B\u200D\u200D\u206C\u206B\u206E\u206C\u200E\u202A\u200F\u202C\u206F\u200D\u202E\u202C\u200F\u202D\u206A\u202A\u200C\u200F\u200F\u206C\u206D\u206C\u202E\u206C\u200F\u200D\u206B\u202E.jèCÕ4(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-832183798), eventUpdate.DaysSinceCreation, eventUpdate.UpdateReason));
					\u206D\u206C\u206B\u206E\u206B\u206A\u200F\u202B\u200C\u200F\u202A\u200C\u200F\u202D\u202B\u206C\u206A\u206D\u200B\u200C\u206C\u202C\u200D\u200E\u202A\u206E\u202A\u200C\u200D\u200C\u206A\u202C\u200B\u206E\u202B\u200C\u202B\u206E\u202B\u202E.w\u00B7\u000Cæ\u00B0(stringBuilder, eventUpdate.Description);
					bool flag2 = num4 < list.Count - 1;
					num2 = ((!flag2) ? 1160165756 : -176033167);
					continue;
				}
				case 7U:
				{
					StringBuilder stringBuilder;
					result = \u206F\u200D\u202B\u206A\u200F\u200D\u202B\u200B\u206C\u206B\u202E\u206E\u200F\u200D\u200F\u206D\u200B\u200C\u202C\u206D\u202A\u206C\u200C\u200B\u202A\u202E\u200E\u202B\u202C\u202E\u202E\u200E\u206D\u202B\u206E\u206A\u200D\u202D\u206E\u200E\u202E.Â^\u0084\u0005ð(stringBuilder);
					num2 = (int)(num3 * 4270158045U ^ 2954881725U);
					continue;
				}
				case 8U:
				{
					StringBuilder stringBuilder = \u202C\u206E\u200E\u202D\u202E\u200F\u202C\u202C\u206F\u200C\u206D\u206D\u202E\u202A\u202D\u202A\u206E\u200D\u202E\u200E\u202C\u202A\u206A\u200C\u202A\u206E\u206C\u202A\u202C\u202E\u206B\u206A\u206F\u200B\u202D\u200D\u202E\u202D\u206E\u202C\u202E.\u200D\u202D\u206B\u202B\u202E\u202A\u200B\u206E\u206C\u202B\u206E\u202E\u206D\u206F\u202D\u206A\u200B\u206F\u202B\u200B\u206E\u206A\u206C\u206A\u202D\u206F\u206D\u202E\u206C\u202B\u206C\u200E\u202C\u206D\u206A\u200E\u206C\u206A\u200B\u202D\u202E();
					\u206D\u206C\u206B\u206E\u206B\u206A\u200F\u202B\u200C\u200F\u202A\u200C\u200F\u202D\u202B\u206C\u206A\u206D\u200B\u200C\u206C\u202C\u200D\u200E\u202A\u206E\u202A\u200C\u200D\u200C\u206A\u202C\u200B\u206E\u202B\u200C\u202B\u206E\u202B\u202E.w\u00B7\u000Cæ\u00B0(stringBuilder, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1646717730));
					\u206F\u200B\u202A\u202E\u202E\u206B\u206A\u200D\u200B\u202E\u206D\u200F\u206B\u206B\u200B\u206D\u202E\u202D\u202D\u206B\u200B\u202E\u200E\u200E\u200D\u200B\u202A\u202B\u202A\u200D\u200F\u202D\u206C\u202C\u200D\u200B\u202B\u206F\u206C\u202D\u202E.i\u00B6ø\u00ACô(stringBuilder);
					List<EventUpdate> list = Enumerable.ToList<EventUpdate>(Enumerable.OrderBy<EventUpdate, float>(this.EventHistory, new Func<EventUpdate, float>(DynamicEvent.\u200C\u202B\u202C\u200C\u200E\u206B\u206D\u206D\u202D\u202D\u206F\u202B\u202C\u206D\u200D\u206E\u202B\u200E\u206D\u200E\u202A\u200D\u206E\u202C\u200B\u202C\u206C\u202E\u200B\u202B\u200B\u206E\u202C\u202A\u202A\u202C\u202D\u206A\u200F\u202D\u202E.<>9.\u200C\u200F\u200D\u200C\u206C\u200D\u206B\u200F\u206A\u206E\u200C\u200F\u200D\u202B\u206C\u200F\u206B\u206C\u202A\u206C\u206A\u202B\u206A\u206E\u206A\u206B\u200D\u200F\u206B\u206F\u200D\u200F\u202A\u206C\u206F\u206E\u200F\u206A\u206E\u202C\u202E)));
					num2 = 156243142;
					continue;
				}
				case 9U:
					goto IL_169;
				}
				break;
			}
			return result;
			IL_169:
			flag = (this.EventHistory.Count <= 1);
			goto IL_17D;
			IL_0C:
			num2 = -993981970;
			goto IL_11;
			IL_17D:
			num2 = (flag ? 367440221 : 1089994683);
			goto IL_11;
		}

		// Token: 0x06000B6A RID: 2922 RVA: 0x000C895C File Offset: 0x000C6B5C
		[MethodImpl(MethodImplOptions.NoInlining)]
		public List<string> GetKingdomStringIds()
		{
			bool flag = this.KingdomsInvolved == null;
			if (flag)
			{
				goto IL_11;
			}
			goto IL_12D;
			int num2;
			List<string> result;
			for (;;)
			{
				IL_16:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)((num * 1040037109 - (-578943567 + 1824163191) * 737565789 ^ 707165986 - 1113554375) - 691702568)) % 11U)
				{
				case 0U:
				{
					JArray jarray = this.KingdomsInvolved as JArray;
					num2 = ((jarray != null) ? -1223081917 : -1795012756);
					continue;
				}
				case 2U:
				{
					List<string> list;
					result = list;
					num2 = (int)(num3 * 2409494047U ^ 296322692U);
					continue;
				}
				case 3U:
					result = new List<string>();
					num2 = -827476672;
					continue;
				case 4U:
				{
					List<string> list = this.KingdomsInvolved as List<string>;
					bool flag2 = list != null;
					num2 = ((!flag2) ? 2128085863 : -1589122101);
					continue;
				}
				case 5U:
					result = new List<string>();
					num2 = (int)(num3 * 1117765627U ^ 3833708290U);
					continue;
				case 6U:
					goto IL_146;
				case 7U:
					goto IL_11;
				case 8U:
				{
					JArray jarray;
					result = jarray.ToObject<List<string>>();
					num2 = (int)(num3 * 3899353433U ^ 3657937988U);
					continue;
				}
				case 9U:
					goto IL_12D;
				case 10U:
					result = new List<string>
					{
						<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1098312393)
					};
					num2 = -827476672;
					continue;
				}
				break;
			}
			return result;
			IL_146:
			string text;
			bool flag3 = \u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1223935971));
			goto IL_160;
			IL_11:
			num2 = 1097258505;
			goto IL_16;
			IL_12D:
			text = (this.KingdomsInvolved as string);
			if (text != null)
			{
				num2 = 915494015;
				goto IL_16;
			}
			flag3 = false;
			IL_160:
			bool flag4 = flag3;
			num2 = ((!flag4) ? -1078560759 : -1795999192);
			goto IL_16;
		}

		// Token: 0x06000B6B RID: 2923 RVA: 0x000C8B0C File Offset: 0x000C6D0C
		[MethodImpl(MethodImplOptions.NoInlining)]
		public bool IsKingdomInvolved(string kingdomStringId)
		{
			bool flag = this.KingdomsInvolved == null;
			if (flag)
			{
				goto IL_0E;
			}
			goto IL_89;
			int num2;
			bool result;
			for (;;)
			{
				IL_13:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(~(uint)(-(uint)(-(uint)(num - (-1915897658 - -(-1601523824 - 1403996001))))))) % 7U)
				{
				case 0U:
				{
					List<string> kingdomStringIds = this.GetKingdomStringIds();
					result = kingdomStringIds.Contains(kingdomStringId);
					num2 = -1272210521;
					continue;
				}
				case 2U:
					goto IL_89;
				case 3U:
					goto IL_58;
				case 4U:
					result = false;
					num2 = (int)(num3 * 3669152605U ^ 1306107182U);
					continue;
				case 5U:
					goto IL_0E;
				case 6U:
					result = true;
					num2 = (int)(num3 * 3081072358U ^ 1532601149U);
					continue;
				}
				break;
			}
			return result;
			IL_58:
			string text;
			bool flag2 = \u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-629724995));
			goto IL_72;
			IL_0E:
			num2 = -835909673;
			goto IL_13;
			IL_72:
			bool flag3 = flag2;
			num2 = ((!flag3) ? -1041387410 : -1057836107);
			goto IL_13;
			IL_89:
			text = (this.KingdomsInvolved as string);
			if (text != null)
			{
				num2 = -1163489960;
				goto IL_13;
			}
			flag2 = false;
			goto IL_72;
		}

		// Token: 0x06000B6C RID: 2924 RVA: 0x000C8C00 File Offset: 0x000C6E00
		[MethodImpl(MethodImplOptions.NoInlining)]
		public string GetDescriptionWithAge()
		{
			return \u200B\u202D\u206E\u206D\u200D\u200B\u206B\u200B\u200E\u206C\u202B\u200D\u200D\u206C\u206B\u206E\u206C\u200E\u202A\u200F\u202C\u206F\u200D\u202E\u202C\u200F\u202D\u206A\u202A\u200C\u200F\u200F\u206C\u206D\u206C\u202E\u206C\u200F\u200D\u206B\u202E.jèCÕ4(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(605913318), this.Description, this.DaysSinceCreation);
		}

		// Token: 0x1700023E RID: 574
		// (get) Token: 0x06000B6D RID: 2925 RVA: 0x000C8C3C File Offset: 0x000C6E3C
		// (set) Token: 0x06000B6E RID: 2926 RVA: 0x000C8C50 File Offset: 0x000C6E50
		[JsonProperty("participating_kingdoms")]
		public List<string> ParticipatingKingdoms { get; set; } = new List<string>();

		// Token: 0x1700023F RID: 575
		// (get) Token: 0x06000B6F RID: 2927 RVA: 0x000C8C64 File Offset: 0x000C6E64
		// (set) Token: 0x06000B70 RID: 2928 RVA: 0x000C8C78 File Offset: 0x000C6E78
		[JsonProperty("kingdom_statements")]
		public List<KingdomStatement> KingdomStatements { get; set; } = new List<KingdomStatement>();

		// Token: 0x17000240 RID: 576
		// (get) Token: 0x06000B71 RID: 2929 RVA: 0x000C8C8C File Offset: 0x000C6E8C
		// (set) Token: 0x06000B72 RID: 2930 RVA: 0x000C8CA0 File Offset: 0x000C6EA0
		[JsonProperty("requires_diplomatic_analysis")]
		public bool RequiresDiplomaticAnalysis { get; set; }

		// Token: 0x17000241 RID: 577
		// (get) Token: 0x06000B73 RID: 2931 RVA: 0x000C8CB4 File Offset: 0x000C6EB4
		// (set) Token: 0x06000B74 RID: 2932 RVA: 0x000C8CC8 File Offset: 0x000C6EC8
		[JsonProperty("diplomatic_rounds")]
		public int DiplomaticRounds { get; set; } = 0;

		// Token: 0x17000242 RID: 578
		// (get) Token: 0x06000B75 RID: 2933 RVA: 0x000C8CDC File Offset: 0x000C6EDC
		// (set) Token: 0x06000B76 RID: 2934 RVA: 0x000C8CF0 File Offset: 0x000C6EF0
		[JsonProperty("statements_at_round_start")]
		public int StatementsAtRoundStart { get; set; } = 0;

		// Token: 0x17000243 RID: 579
		// (get) Token: 0x06000B77 RID: 2935 RVA: 0x000C8D04 File Offset: 0x000C6F04
		// (set) Token: 0x06000B78 RID: 2936 RVA: 0x000C8D18 File Offset: 0x000C6F18
		[JsonIgnore]
		public DiplomaticAnalysisResult LastAnalysisResult { get; set; }

		// Token: 0x17000244 RID: 580
		// (get) Token: 0x06000B79 RID: 2937 RVA: 0x000C8D2C File Offset: 0x000C6F2C
		// (set) Token: 0x06000B7A RID: 2938 RVA: 0x000C8D40 File Offset: 0x000C6F40
		[JsonProperty("next_analysis_attempt_days")]
		public float NextAnalysisAttemptDays { get; set; } = 0f;

		// Token: 0x17000245 RID: 581
		// (get) Token: 0x06000B7B RID: 2939 RVA: 0x000C8D54 File Offset: 0x000C6F54
		// (set) Token: 0x06000B7C RID: 2940 RVA: 0x000C8D68 File Offset: 0x000C6F68
		[JsonProperty("next_statement_attempt_days")]
		public Dictionary<string, float> NextStatementAttemptDays { get; set; } = new Dictionary<string, float>();

		// Token: 0x17000246 RID: 582
		// (get) Token: 0x06000B7D RID: 2941 RVA: 0x000C8D7C File Offset: 0x000C6F7C
		// (set) Token: 0x06000B7E RID: 2942 RVA: 0x000C8D90 File Offset: 0x000C6F90
		[JsonProperty("failed_statement_attempts")]
		public Dictionary<string, int> FailedStatementAttempts { get; set; } = new Dictionary<string, int>();

		// Token: 0x06000B7F RID: 2943 RVA: 0x000C8DA4 File Offset: 0x000C6FA4
		public bool IsReadyForAnalysis()
		{
			\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ();
			bool result;
			for (;;)
			{
				IL_0C:
				int num = -1717304478;
				for (;;)
				{
					int num2 = num;
					uint num3;
					switch ((num3 = (uint)(~(uint)(num2 ^ (-1022799319 ^ -2081940521 * 1812206871) + (1852085708 + 1205621547 + 1005900657 * -249899711)))) % 5U)
					{
					case 0U:
						goto IL_0C;
					case 1U:
						result = true;
						num = (int)(num3 * 2854234028U ^ 1464426876U);
						continue;
					case 2U:
					{
						bool flag = false;
						num = (int)(((!flag) ? 1439425527U : 700384701U) ^ num3 * 107355655U);
						continue;
					}
					case 3U:
					{
						float num4 = (float)\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u200D\u200D\u200C\u202E\u200B\u206F\u202C\u206B\u206A\u202A\u206D\u202E\u202A\u202B\u202B\u206F\u206D\u206E\u200B\u202C\u200D\u200F\u206C\u200C\u200C\u200D\u200F\u200C\u202E\u206A\u200B\u200B\u202C\u206E\u202A\u200C\u206B\u200B\u202E\u206B\u202E().ToDays;
						result = (num4 >= this.NextAnalysisAttemptDays);
						num = -1101378192;
						continue;
					}
					}
					return result;
				}
			}
			return result;
		}

		// Token: 0x06000B80 RID: 2944 RVA: 0x000C8E6C File Offset: 0x000C706C
		public void SetAnalysisRetryDelay(int delayDays)
		{
			\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ();
			bool flag = true;
			if (flag)
			{
				for (;;)
				{
					IL_11:
					int num = -947850410;
					for (;;)
					{
						int num2 = num;
						uint num3;
						switch ((num3 = (uint)(-(uint)((num2 - ~(-(-1458593446 + -1485241069))) * 858156045))) % 3U)
						{
						case 0U:
							goto IL_11;
						case 1U:
						{
							float num4 = (float)\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ().ToDays;
							this.NextAnalysisAttemptDays = num4 + (float)delayDays;
							num = (int)(num3 * 1129613832U ^ 2365915270U);
							continue;
						}
						}
						goto Block_1;
					}
				}
				Block_1:;
			}
		}

		// Token: 0x06000B81 RID: 2945 RVA: 0x000C8EF0 File Offset: 0x000C70F0
		public bool IsKingdomReadyForStatement(string kingdomId)
		{
			\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ();
			bool flag = false;
			if (flag)
			{
				goto IL_11;
			}
			goto IL_89;
			int num2;
			bool result;
			float num5;
			for (;;)
			{
				IL_16:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(~((num ^ ~(-2130518588 + 1623157849) + (56975218 + -1697328059 + 1263149315 * -1312917119)) - (1917935886 - ~623321109)) * -1414247665)) % 6U)
				{
				case 0U:
					goto IL_11;
				case 1U:
					result = true;
					num2 = -863476498;
					continue;
				case 2U:
					goto IL_89;
				case 4U:
					result = true;
					num2 = (int)(num3 * 4245017191U ^ 2201439854U);
					continue;
				case 5U:
				{
					float num4 = (float)\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ().ToDays;
					result = (num4 >= num5);
					num2 = (int)(num3 * 3934248360U ^ 1573602454U);
					continue;
				}
				}
				break;
			}
			return result;
			IL_11:
			num2 = 1758559679;
			goto IL_16;
			IL_89:
			num2 = (this.NextStatementAttemptDays.TryGetValue(kingdomId, out num5) ? -1168950118 : -1928929462);
			goto IL_16;
		}

		// Token: 0x06000B82 RID: 2946 RVA: 0x000C8FEC File Offset: 0x000C71EC
		public void SetKingdomStatementRetryDelay(string kingdomId, int delayDays)
		{
			\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ();
			bool flag = true;
			if (flag)
			{
				for (;;)
				{
					IL_11:
					int num = 578993175;
					for (;;)
					{
						int num2 = num;
						uint num3;
						switch ((num3 = (uint)(-(uint)(-(uint)(-(-905221170 + 41576684) - num2 * 560544481)))) % 3U)
						{
						case 0U:
							goto IL_11;
						case 1U:
						{
							float num4 = (float)\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ().ToDays;
							this.NextStatementAttemptDays[kingdomId] = num4 + (float)delayDays;
							num = (int)(num3 * 2416816549U ^ 856529117U);
							continue;
						}
						}
						goto Block_1;
					}
				}
				Block_1:;
			}
		}

		// Token: 0x06000B83 RID: 2947 RVA: 0x000C9078 File Offset: 0x000C7278
		public bool IncrementFailedStatementAttempt(string kingdomId)
		{
			bool result;
			for (;;)
			{
				IL_01:
				int num = 876747394;
				for (;;)
				{
					int num2 = num;
					uint num3;
					switch ((num3 = (uint)((num2 ^ 1595545737 * -276407611) - ~1336008702 ^ 638632152)) % 5U)
					{
					case 0U:
						goto IL_01;
					case 1U:
					{
						Dictionary<string, int> failedStatementAttempts = this.FailedStatementAttempts;
						int num4 = failedStatementAttempts[kingdomId];
						failedStatementAttempts[kingdomId] = num4 + 1;
						result = (this.FailedStatementAttempts[kingdomId] < 3);
						num = -219457449;
						continue;
					}
					case 2U:
						num = (int)(((!this.FailedStatementAttempts.ContainsKey(kingdomId)) ? 3089974250U : 1913886302U) ^ num3 * 1327198412U);
						continue;
					case 4U:
						this.FailedStatementAttempts[kingdomId] = 0;
						num = (int)(num3 * 2623324902U ^ 3740815890U);
						continue;
					}
					return result;
				}
			}
			return result;
		}

		// Token: 0x06000B84 RID: 2948 RVA: 0x000C9150 File Offset: 0x000C7350
		public void ResetFailedStatementAttempt(string kingdomId)
		{
			this.FailedStatementAttempts.Remove(kingdomId);
		}

		// Token: 0x040006E7 RID: 1767
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200B\u202C\u200B\u206F\u200C\u206F\u200B\u206C\u202A\u200B\u202D\u200F\u202D\u206B\u200D\u202C\u200D\u202D\u200D\u200B\u202D\u206F\u200B\u206C\u206B\u206D\u200B\u200C\u206A\u206B\u200D\u202B\u202B\u202E\u202C\u206B\u200F\u202D\u202E\u200C\u202E;

		// Token: 0x040006E8 RID: 1768
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206C\u206B\u202C\u206B\u200E\u202A\u202E\u206F\u200B\u202C\u200E\u200D\u206C\u200B\u200B\u200B\u202C\u206F\u200E\u206C\u200F\u206B\u206A\u202D\u202D\u202E\u200B\u200E\u200E\u206B\u206E\u200F\u206D\u200D\u200F\u202B\u206C\u200F\u206B\u206C\u202E;

		// Token: 0x040006E9 RID: 1769
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202A\u206B\u206F\u206F\u200F\u202A\u206A\u202E\u202E\u206D\u206B\u200D\u200B\u202E\u206F\u200F\u206F\u200E\u202B\u200E\u202A\u206C\u200E\u200C\u200D\u202E\u200B\u202A\u206D\u200D\u206F\u200B\u202C\u202D\u206E\u202A\u202C\u206F\u202C\u202E\u202E;

		// Token: 0x040006EA RID: 1770
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202B\u200D\u200F\u206E\u200E\u206F\u200C\u206D\u202D\u200B\u200E\u200E\u200E\u202A\u202C\u200D\u202B\u206B\u202A\u202A\u200E\u200E\u206D\u200F\u202D\u200C\u200B\u206F\u202B\u202B\u206D\u202B\u202D\u206E\u206D\u206C\u206F\u206C\u200D\u202C\u202E;

		// Token: 0x040006EB RID: 1771
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<EventUpdate> \u202A\u206A\u200F\u200B\u202D\u200B\u202E\u206A\u206A\u206E\u206B\u200E\u202C\u200D\u200C\u206E\u206A\u206E\u200B\u206C\u206C\u200E\u200F\u202E\u202D\u200C\u202B\u200E\u200B\u206C\u200F\u202C\u206E\u206F\u202C\u206B\u202D\u202C\u202A\u206B\u202E;

		// Token: 0x040006EC RID: 1772
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u206C\u200F\u200F\u202B\u206B\u202A\u206C\u202B\u200B\u200E\u206A\u200D\u200F\u206B\u206B\u200E\u200C\u200B\u200D\u202D\u200C\u206A\u200C\u200E\u200F\u202D\u200C\u200C\u202C\u200F\u202C\u200C\u206A\u200F\u200E\u202D\u206B\u202B\u202A\u202E;

		// Token: 0x040006ED RID: 1773
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private object \u206F\u200E\u206D\u206C\u206C\u206C\u202C\u200C\u202C\u200B\u200B\u206D\u202E\u206B\u202D\u206C\u200F\u200D\u200E\u200E\u200C\u206F\u200F\u206B\u200D\u202E\u202D\u200C\u200D\u202A\u200C\u206C\u206D\u200E\u200B\u202C\u206E\u206D\u206F\u206D\u202E;

		// Token: 0x040006EE RID: 1774
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<string> \u200D\u202A\u206A\u202C\u200F\u206A\u202A\u202A\u202B\u206D\u200F\u200D\u202A\u206A\u200D\u202C\u202E\u200C\u202D\u206F\u202C\u200B\u206A\u206D\u206D\u200D\u202A\u200E\u206C\u206E\u200D\u202A\u202D\u202B\u202C\u206D\u200B\u200D\u200D\u200F\u202E;

		// Token: 0x040006EF RID: 1775
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u206E\u202B\u206C\u200E\u202C\u206D\u202C\u202A\u206A\u200E\u206D\u200C\u206A\u206B\u200F\u202A\u200D\u202B\u200D\u206A\u200B\u200D\u202A\u206A\u206D\u206A\u206A\u202B\u206E\u206B\u206E\u202E\u202C\u206C\u200C\u206E\u200F\u200F\u200C\u202C\u202E;

		// Token: 0x040006F0 RID: 1776
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200E\u206F\u206A\u206C\u200D\u206B\u200F\u200B\u206F\u202D\u206A\u206F\u206F\u200E\u200E\u202D\u206F\u200B\u202D\u202C\u200C\u200E\u200B\u200C\u206F\u202D\u200C\u200E\u202E\u206C\u206F\u200E\u200F\u206C\u200D\u202B\u200D\u200F\u206E\u202B\u202E;

		// Token: 0x040006F1 RID: 1777
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u202E\u202A\u200E\u200E\u200C\u202E\u206C\u200E\u202E\u206A\u202E\u206C\u202D\u200F\u200D\u200D\u200D\u206D\u202B\u202E\u206F\u200C\u200D\u200D\u200D\u206B\u200D\u206A\u202D\u202C\u202B\u206C\u200C\u202E\u206E\u200E\u206B\u206C\u202D\u200F\u202E;

		// Token: 0x040006F2 RID: 1778
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<string> \u202A\u200C\u206D\u200D\u200E\u206E\u206E\u200D\u206D\u202C\u202A\u202C\u200D\u200E\u202D\u202A\u200B\u200C\u202E\u202A\u200F\u206F\u202E\u206E\u200F\u206B\u202B\u202C\u206D\u200E\u206E\u206E\u202D\u206A\u206C\u206E\u202D\u202D\u206B\u202A\u202E;

		// Token: 0x040006F3 RID: 1779
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private SettlementPenaltyData \u206A\u206F\u202D\u200D\u200B\u206C\u206E\u200F\u200F\u200F\u200D\u202A\u206F\u202C\u202D\u206E\u206D\u200C\u202C\u202A\u206A\u200D\u200D\u200F\u200D\u206A\u206B\u202B\u200B\u202A\u206F\u206F\u200F\u202E\u206D\u206B\u202C\u200D\u200E\u206D\u202E;

		// Token: 0x040006F4 RID: 1780
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<EconomicEffect> \u200C\u202E\u202C\u200C\u206F\u202B\u202D\u206D\u202A\u200C\u202A\u206B\u202B\u206A\u200E\u206A\u200B\u206C\u202A\u200F\u206E\u202A\u202A\u202C\u206C\u206D\u200D\u206A\u202E\u200C\u200E\u202C\u206B\u202C\u206D\u202C\u200F\u202A\u202C\u202B\u202E;

		// Token: 0x040006F5 RID: 1781
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private DateTime \u202E\u200E\u200E\u206E\u206F\u206A\u200E\u202C\u200E\u206E\u202B\u202E\u202B\u200C\u206D\u202C\u202A\u202A\u206D\u202E\u202E\u206A\u200F\u202E\u206F\u202A\u202A\u206B\u202C\u202A\u206A\u206B\u206E\u206B\u206D\u200B\u206B\u202D\u206D\u202E\u202E;

		// Token: 0x040006F6 RID: 1782
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u202C\u200B\u206D\u206B\u206A\u200B\u206D\u202E\u206D\u202D\u206D\u206E\u200C\u206A\u202C\u206B\u202D\u202D\u206F\u206E\u200F\u202A\u202B\u200B\u206B\u206B\u206D\u200B\u202A\u202B\u200B\u202D\u206A\u206B\u202B\u200B\u206C\u202B\u200B\u202A\u202E;

		// Token: 0x040006F7 RID: 1783
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private DateTime \u206F\u200E\u200C\u206E\u200B\u206E\u200E\u202A\u202E\u202A\u200B\u202B\u206D\u206A\u206E\u202B\u200D\u202A\u206B\u200B\u200F\u200F\u200B\u202C\u206D\u200D\u200D\u206D\u202E\u206D\u202C\u200E\u202C\u200B\u202B\u200D\u206B\u200F\u202B\u202A\u202E;

		// Token: 0x040006F8 RID: 1784
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u202D\u202B\u200F\u206D\u206E\u206B\u206A\u206C\u206D\u202C\u206F\u202B\u206B\u202C\u202E\u206A\u202A\u200D\u200C\u200E\u202D\u206B\u202C\u202D\u202E\u200B\u202B\u202B\u200C\u206F\u200C\u202C\u200C\u202D\u200D\u202A\u202C\u200D\u206F\u202E\u202E;

		// Token: 0x040006F9 RID: 1785
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<string> \u200B\u200D\u200B\u200D\u206C\u206B\u202D\u206E\u200E\u202A\u200E\u200D\u206B\u200D\u202C\u206C\u200D\u202D\u200E\u206E\u200D\u200B\u206E\u200F\u200B\u200E\u200E\u206D\u206C\u202E\u202E\u200C\u206C\u200E\u200C\u200E\u200C\u200E\u206D\u206E\u202E;

		// Token: 0x040006FA RID: 1786
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<KingdomStatement> \u200F\u202A\u200B\u202A\u202E\u200B\u206F\u206A\u206B\u206B\u200B\u200E\u202E\u202E\u200F\u202E\u200D\u206E\u200D\u206F\u206C\u206C\u206B\u200B\u206B\u206C\u206A\u200B\u202E\u200B\u206F\u202B\u206B\u206D\u206B\u202B\u202C\u200D\u200B\u200D\u202E;

		// Token: 0x040006FB RID: 1787
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u202D\u202C\u202D\u206C\u202A\u200D\u200B\u200D\u206C\u200B\u202D\u200E\u202C\u200C\u202C\u202B\u206D\u202D\u200D\u200F\u202E\u202B\u202C\u202D\u202A\u202E\u202B\u200F\u202D\u206A\u200E\u200C\u202A\u206D\u206E\u202D\u200C\u206A\u206F\u202E\u202E;

		// Token: 0x040006FC RID: 1788
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u200D\u206E\u200E\u206B\u206A\u202E\u200B\u202A\u202C\u202C\u200F\u202A\u202A\u206E\u200B\u202C\u200F\u202B\u200F\u202D\u206E\u206C\u206F\u200F\u206B\u202C\u200E\u202C\u202C\u202C\u202C\u206E\u206C\u200E\u200D\u206F\u202C\u200E\u200C\u202E\u202E;

		// Token: 0x040006FD RID: 1789
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u200E\u200C\u202D\u206A\u202E\u202C\u200C\u206B\u202C\u200E\u200B\u202D\u202D\u206C\u200C\u200C\u206D\u206D\u200C\u206F\u200F\u202C\u206B\u202A\u206D\u200B\u206B\u200E\u202A\u200E\u206E\u206A\u206A\u200D\u206D\u206C\u202E\u200C\u202B\u200D\u202E;

		// Token: 0x040006FE RID: 1790
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private DiplomaticAnalysisResult \u206B\u202B\u206C\u200C\u202E\u206C\u200E\u200E\u206A\u206E\u202D\u206C\u206B\u200C\u206E\u200B\u206E\u200B\u202C\u202A\u202D\u206C\u206D\u202C\u200E\u206A\u202D\u202D\u206C\u206A\u200B\u202D\u200D\u200C\u200B\u202A\u202B\u202C\u202C\u206E\u202E;

		// Token: 0x040006FF RID: 1791
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u200F\u202E\u206D\u202A\u206F\u202E\u200B\u206B\u202B\u200B\u200F\u200F\u206F\u202D\u206C\u206A\u200B\u202E\u200B\u206B\u200B\u202C\u202C\u206C\u202D\u200D\u206C\u202B\u200D\u206D\u200F\u206E\u202A\u200D\u206C\u206B\u200F\u206B\u200F\u202A\u202E;

		// Token: 0x04000700 RID: 1792
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private Dictionary<string, float> \u202A\u200C\u202B\u200E\u202D\u200C\u202C\u206A\u200F\u202A\u200D\u200C\u206D\u200C\u202A\u200F\u200D\u206C\u202B\u202D\u200D\u206C\u200C\u200B\u202A\u206B\u206B\u206B\u200D\u206C\u206B\u206D\u206B\u202E\u200B\u206C\u206E\u206E\u200D\u206B\u202E;

		// Token: 0x04000701 RID: 1793
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private Dictionary<string, int> \u202E\u206A\u206C\u206B\u206E\u200D\u206D\u200E\u200D\u202D\u202C\u200F\u202E\u202B\u200B\u200B\u206C\u200D\u202C\u206F\u200F\u202A\u206C\u206F\u206C\u200D\u206A\u202B\u202C\u206C\u200E\u206C\u202A\u202D\u206F\u200E\u206D\u202E\u200C\u206B\u202E;

		// Token: 0x02000163 RID: 355
		[CompilerGenerated]
		[Serializable]
		private sealed class \u200C\u202B\u202C\u200C\u200E\u206B\u206D\u206D\u202D\u202D\u206F\u202B\u202C\u206D\u200D\u206E\u202B\u200E\u206D\u200E\u202A\u200D\u206E\u202C\u200B\u202C\u206C\u202E\u200B\u202B\u200B\u206E\u202C\u202A\u202A\u202C\u202D\u206A\u200F\u202D\u202E
		{
			// Token: 0x06000B87 RID: 2951 RVA: 0x000A0854 File Offset: 0x0009EA54
			internal float \u200C\u200F\u200D\u200C\u206C\u200D\u206B\u200F\u206A\u206E\u200C\u200F\u200D\u202B\u206C\u200F\u206B\u206C\u202A\u206C\u206A\u202B\u206A\u206E\u206A\u206B\u200D\u200F\u206B\u206F\u200D\u200F\u202A\u206C\u206F\u206E\u200F\u206A\u206E\u202C\u202E(EventUpdate A_1)
			{
				return A_1.CampaignDays;
			}

			// Token: 0x04000702 RID: 1794
			public static readonly DynamicEvent.\u200C\u202B\u202C\u200C\u200E\u206B\u206D\u206D\u202D\u202D\u206F\u202B\u202C\u206D\u200D\u206E\u202B\u200E\u206D\u200E\u202A\u200D\u206E\u202C\u200B\u202C\u206C\u202E\u200B\u202B\u200B\u206E\u202C\u202A\u202A\u202C\u202D\u206A\u200F\u202D\u202E <>9 = new DynamicEvent.\u200C\u202B\u202C\u200C\u200E\u206B\u206D\u206D\u202D\u202D\u206F\u202B\u202C\u206D\u200D\u206E\u202B\u200E\u206D\u200E\u202A\u200D\u206E\u202C\u200B\u202C\u206C\u202E\u200B\u202B\u200B\u206E\u202C\u202A\u202A\u202C\u202D\u206A\u200F\u202D\u202E();

			// Token: 0x04000703 RID: 1795
			public static Func<EventUpdate, float> <>9__77_0;
		}
	}
}
