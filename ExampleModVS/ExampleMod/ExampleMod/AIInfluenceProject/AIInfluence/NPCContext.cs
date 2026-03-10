using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence
{
	// Token: 0x0200006B RID: 107
	[JsonSerializable]
	public class NPCContext
	{
		// Token: 0x170000BC RID: 188
		// (get) Token: 0x06000383 RID: 899 RVA: 0x0005AA44 File Offset: 0x00058C44
		// (set) Token: 0x06000384 RID: 900 RVA: 0x0005AA58 File Offset: 0x00058C58
		public string Name { get; set; } = <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(923044409);

		// Token: 0x170000BD RID: 189
		// (get) Token: 0x06000385 RID: 901 RVA: 0x0005AA6C File Offset: 0x00058C6C
		// (set) Token: 0x06000386 RID: 902 RVA: 0x0005AA80 File Offset: 0x00058C80
		public string StringId { get; set; } = "";

		// Token: 0x170000BE RID: 190
		// (get) Token: 0x06000387 RID: 903 RVA: 0x0005AA94 File Offset: 0x00058C94
		// (set) Token: 0x06000388 RID: 904 RVA: 0x0005AAA8 File Offset: 0x00058CA8
		public string Gender { get; set; }

		// Token: 0x170000BF RID: 191
		// (get) Token: 0x06000389 RID: 905 RVA: 0x0005AABC File Offset: 0x00058CBC
		// (set) Token: 0x0600038A RID: 906 RVA: 0x0005AAD0 File Offset: 0x00058CD0
		public string AssignedTTSVoice { get; set; } = "";

		// Token: 0x170000C0 RID: 192
		// (get) Token: 0x0600038B RID: 907 RVA: 0x0005AAE4 File Offset: 0x00058CE4
		// (set) Token: 0x0600038C RID: 908 RVA: 0x0005AAF8 File Offset: 0x00058CF8
		public string LastTTSPlayedText { get; set; } = "";

		// Token: 0x170000C1 RID: 193
		// (get) Token: 0x0600038D RID: 909 RVA: 0x0005AB0C File Offset: 0x00058D0C
		// (set) Token: 0x0600038E RID: 910 RVA: 0x0005AB20 File Offset: 0x00058D20
		public string LastTTSInstructions { get; set; } = "";

		// Token: 0x170000C2 RID: 194
		// (get) Token: 0x0600038F RID: 911 RVA: 0x0005AB34 File Offset: 0x00058D34
		// (set) Token: 0x06000390 RID: 912 RVA: 0x0005AB48 File Offset: 0x00058D48
		public bool IsInPlayerParty { get; set; }

		// Token: 0x170000C3 RID: 195
		// (get) Token: 0x06000391 RID: 913 RVA: 0x0005AB5C File Offset: 0x00058D5C
		// (set) Token: 0x06000392 RID: 914 RVA: 0x0005AB70 File Offset: 0x00058D70
		public bool IsWithPlayer { get; set; }

		// Token: 0x170000C4 RID: 196
		// (get) Token: 0x06000393 RID: 915 RVA: 0x0005AB84 File Offset: 0x00058D84
		// (set) Token: 0x06000394 RID: 916 RVA: 0x0005AB98 File Offset: 0x00058D98
		public bool IsPlayerKnown { get; set; }

		// Token: 0x170000C5 RID: 197
		// (get) Token: 0x06000395 RID: 917 RVA: 0x0005ABAC File Offset: 0x00058DAC
		// (set) Token: 0x06000396 RID: 918 RVA: 0x0005ABC0 File Offset: 0x00058DC0
		public PlayerInfo PlayerInfo { get; set; } = new PlayerInfo();

		// Token: 0x170000C6 RID: 198
		// (get) Token: 0x06000397 RID: 919 RVA: 0x0005ABD4 File Offset: 0x00058DD4
		// (set) Token: 0x06000398 RID: 920 RVA: 0x0005ABE8 File Offset: 0x00058DE8
		public List<string> ConversationHistory { get; set; } = new List<string>();

		// Token: 0x170000C7 RID: 199
		// (get) Token: 0x06000399 RID: 921 RVA: 0x0005ABFC File Offset: 0x00058DFC
		// (set) Token: 0x0600039A RID: 922 RVA: 0x0005AC10 File Offset: 0x00058E10
		public string WarStatus { get; set; }

		// Token: 0x170000C8 RID: 200
		// (get) Token: 0x0600039B RID: 923 RVA: 0x0005AC24 File Offset: 0x00058E24
		// (set) Token: 0x0600039C RID: 924 RVA: 0x0005AC38 File Offset: 0x00058E38
		public PlayerRelation PlayerRelation { get; set; }

		// Token: 0x170000C9 RID: 201
		// (get) Token: 0x0600039D RID: 925 RVA: 0x0005AC4C File Offset: 0x00058E4C
		// (set) Token: 0x0600039E RID: 926 RVA: 0x0005AC60 File Offset: 0x00058E60
		public string CurrentTask { get; set; }

		// Token: 0x170000CA RID: 202
		// (get) Token: 0x0600039F RID: 927 RVA: 0x0005AC74 File Offset: 0x00058E74
		// (set) Token: 0x060003A0 RID: 928 RVA: 0x0005AC88 File Offset: 0x00058E88
		public List<CampaignEvent> RecentEvents { get; set; } = new List<CampaignEvent>();

		// Token: 0x170000CB RID: 203
		// (get) Token: 0x060003A1 RID: 929 RVA: 0x0005AC9C File Offset: 0x00058E9C
		// (set) Token: 0x060003A2 RID: 930 RVA: 0x0005ACB0 File Offset: 0x00058EB0
		[JsonProperty("DialogueAnalysisEvents")]
		public List<CampaignEvent> DialogueAnalysisEvents { get; set; } = new List<CampaignEvent>();

		// Token: 0x170000CC RID: 204
		// (get) Token: 0x060003A3 RID: 931 RVA: 0x0005ACC4 File Offset: 0x00058EC4
		// (set) Token: 0x060003A4 RID: 932 RVA: 0x0005ACD8 File Offset: 0x00058ED8
		public EmotionalState EmotionalState { get; set; }

		// Token: 0x170000CD RID: 205
		// (get) Token: 0x060003A5 RID: 933 RVA: 0x0005ACEC File Offset: 0x00058EEC
		// (set) Token: 0x060003A6 RID: 934 RVA: 0x0005AD00 File Offset: 0x00058F00
		public string LocationType { get; set; }

		// Token: 0x170000CE RID: 206
		// (get) Token: 0x060003A7 RID: 935 RVA: 0x0005AD14 File Offset: 0x00058F14
		// (set) Token: 0x060003A8 RID: 936 RVA: 0x0005AD28 File Offset: 0x00058F28
		public TimeContext TimeContext { get; set; }

		// Token: 0x170000CF RID: 207
		// (get) Token: 0x060003A9 RID: 937 RVA: 0x0005AD3C File Offset: 0x00058F3C
		// (set) Token: 0x060003AA RID: 938 RVA: 0x0005AD50 File Offset: 0x00058F50
		public PlayerForces PlayerForces { get; set; }

		// Token: 0x170000D0 RID: 208
		// (get) Token: 0x060003AB RID: 939 RVA: 0x0005AD64 File Offset: 0x00058F64
		// (set) Token: 0x060003AC RID: 940 RVA: 0x0005AD78 File Offset: 0x00058F78
		public NPCForces NPCForces { get; set; }

		// Token: 0x170000D1 RID: 209
		// (get) Token: 0x060003AD RID: 941 RVA: 0x0005AD8C File Offset: 0x00058F8C
		// (set) Token: 0x060003AE RID: 942 RVA: 0x0005ADA0 File Offset: 0x00058FA0
		public List<string> Quirks { get; set; } = new List<string>();

		// Token: 0x170000D2 RID: 210
		// (get) Token: 0x060003AF RID: 943 RVA: 0x0005ADB4 File Offset: 0x00058FB4
		// (set) Token: 0x060003B0 RID: 944 RVA: 0x0005ADC8 File Offset: 0x00058FC8
		public float TrustLevel { get; set; }

		// Token: 0x170000D3 RID: 211
		// (get) Token: 0x060003B1 RID: 945 RVA: 0x0005ADDC File Offset: 0x00058FDC
		// (set) Token: 0x060003B2 RID: 946 RVA: 0x0005ADF0 File Offset: 0x00058FF0
		public int InteractionCount { get; set; }

		// Token: 0x170000D4 RID: 212
		// (get) Token: 0x060003B3 RID: 947 RVA: 0x0005AE04 File Offset: 0x00059004
		// (set) Token: 0x060003B4 RID: 948 RVA: 0x0005AE18 File Offset: 0x00059018
		public string InformationAccessLevel { get; set; }

		// Token: 0x170000D5 RID: 213
		// (get) Token: 0x060003B5 RID: 949 RVA: 0x0005AE2C File Offset: 0x0005902C
		// (set) Token: 0x060003B6 RID: 950 RVA: 0x0005AE40 File Offset: 0x00059040
		public float LiePenaltySum { get; set; }

		// Token: 0x170000D6 RID: 214
		// (get) Token: 0x060003B7 RID: 951 RVA: 0x0005AE54 File Offset: 0x00059054
		// (set) Token: 0x060003B8 RID: 952 RVA: 0x0005AE68 File Offset: 0x00059068
		public int? NegativeToneCount { get; set; }

		// Token: 0x170000D7 RID: 215
		// (get) Token: 0x060003B9 RID: 953 RVA: 0x0005AE7C File Offset: 0x0005907C
		// (set) Token: 0x060003BA RID: 954 RVA: 0x0005AE90 File Offset: 0x00059090
		public string EscalationState { get; set; } = <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1657918500);

		// Token: 0x170000D8 RID: 216
		// (get) Token: 0x060003BB RID: 955 RVA: 0x0005AEA4 File Offset: 0x000590A4
		// (set) Token: 0x060003BC RID: 956 RVA: 0x0005AEB8 File Offset: 0x000590B8
		public string CombatResponse { get; set; }

		// Token: 0x170000D9 RID: 217
		// (get) Token: 0x060003BD RID: 957 RVA: 0x0005AECC File Offset: 0x000590CC
		// (set) Token: 0x060003BE RID: 958 RVA: 0x0005AEE0 File Offset: 0x000590E0
		public bool IsSurrendering { get; set; }

		// Token: 0x170000DA RID: 218
		// (get) Token: 0x060003BF RID: 959 RVA: 0x0005AEF4 File Offset: 0x000590F4
		// (set) Token: 0x060003C0 RID: 960 RVA: 0x0005AF08 File Offset: 0x00059108
		public bool IsPlayerSurrendering { get; set; }

		// Token: 0x170000DB RID: 219
		// (get) Token: 0x060003C1 RID: 961 RVA: 0x0005AF1C File Offset: 0x0005911C
		// (set) Token: 0x060003C2 RID: 962 RVA: 0x0005AF30 File Offset: 0x00059130
		public string MarriageResponse { get; set; }

		// Token: 0x170000DC RID: 220
		// (get) Token: 0x060003C3 RID: 963 RVA: 0x0005AF44 File Offset: 0x00059144
		// (set) Token: 0x060003C4 RID: 964 RVA: 0x0005AF58 File Offset: 0x00059158
		public string PendingDeath { get; set; }

		// Token: 0x170000DD RID: 221
		// (get) Token: 0x060003C5 RID: 965 RVA: 0x0005AF6C File Offset: 0x0005916C
		// (set) Token: 0x060003C6 RID: 966 RVA: 0x0005AF80 File Offset: 0x00059180
		public string PendingSettlementCombat { get; set; }

		// Token: 0x170000DE RID: 222
		// (get) Token: 0x060003C7 RID: 967 RVA: 0x0005AF94 File Offset: 0x00059194
		// (set) Token: 0x060003C8 RID: 968 RVA: 0x0005AFA8 File Offset: 0x000591A8
		public string SettlementCombatResponse { get; set; }

		// Token: 0x170000DF RID: 223
		// (get) Token: 0x060003C9 RID: 969 RVA: 0x0005AFBC File Offset: 0x000591BC
		// (set) Token: 0x060003CA RID: 970 RVA: 0x0005AFD0 File Offset: 0x000591D0
		[JsonProperty("RoleplayDeathReason")]
		public string DeathReason { get; set; }

		// Token: 0x170000E0 RID: 224
		// (get) Token: 0x060003CB RID: 971 RVA: 0x0005AFE4 File Offset: 0x000591E4
		// (set) Token: 0x060003CC RID: 972 RVA: 0x0005AFF8 File Offset: 0x000591F8
		[JsonProperty("KillerStringId")]
		public string KillerStringId { get; set; }

		// Token: 0x170000E1 RID: 225
		// (get) Token: 0x060003CD RID: 973 RVA: 0x0005B00C File Offset: 0x0005920C
		// (set) Token: 0x060003CE RID: 974 RVA: 0x0005B020 File Offset: 0x00059220
		public string LastDynamicResponse { get; set; }

		// Token: 0x170000E2 RID: 226
		// (get) Token: 0x060003CF RID: 975 RVA: 0x0005B034 File Offset: 0x00059234
		// (set) Token: 0x060003D0 RID: 976 RVA: 0x0005B048 File Offset: 0x00059248
		public AIResponse PendingAIResponse { get; set; }

		// Token: 0x170000E3 RID: 227
		// (get) Token: 0x060003D1 RID: 977 RVA: 0x0005B05C File Offset: 0x0005925C
		// (set) Token: 0x060003D2 RID: 978 RVA: 0x0005B070 File Offset: 0x00059270
		public PendingRelationChange PendingRelationChange { get; set; }

		// Token: 0x170000E4 RID: 228
		// (get) Token: 0x060003D3 RID: 979 RVA: 0x0005B084 File Offset: 0x00059284
		// (set) Token: 0x060003D4 RID: 980 RVA: 0x0005B098 File Offset: 0x00059298
		public PendingRelationChange PendingLiePenalty { get; set; }

		// Token: 0x170000E5 RID: 229
		// (get) Token: 0x060003D5 RID: 981 RVA: 0x0005B0AC File Offset: 0x000592AC
		// (set) Token: 0x060003D6 RID: 982 RVA: 0x0005B0C0 File Offset: 0x000592C0
		public PendingWorkshopSale PendingWorkshopSale { get; set; }

		// Token: 0x170000E6 RID: 230
		// (get) Token: 0x060003D7 RID: 983 RVA: 0x0005B0D4 File Offset: 0x000592D4
		// (set) Token: 0x060003D8 RID: 984 RVA: 0x0005B0E8 File Offset: 0x000592E8
		public MoneyTransferInfo PendingMoneyTransfer { get; set; }

		// Token: 0x170000E7 RID: 231
		// (get) Token: 0x060003D9 RID: 985 RVA: 0x0005B0FC File Offset: 0x000592FC
		// (set) Token: 0x060003DA RID: 986 RVA: 0x0005B110 File Offset: 0x00059310
		public List<ItemTransferData> PendingItemTransfers { get; set; }

		// Token: 0x170000E8 RID: 232
		// (get) Token: 0x060003DB RID: 987 RVA: 0x0005B124 File Offset: 0x00059324
		// (set) Token: 0x060003DC RID: 988 RVA: 0x0005B138 File Offset: 0x00059338
		public string CharacterDescription { get; set; } = "";

		// Token: 0x170000E9 RID: 233
		// (get) Token: 0x060003DD RID: 989 RVA: 0x0005B14C File Offset: 0x0005934C
		// (set) Token: 0x060003DE RID: 990 RVA: 0x0005B160 File Offset: 0x00059360
		[JsonProperty("AIGeneratedPersonality")]
		public string AIGeneratedPersonality { get; set; } = null;

		// Token: 0x170000EA RID: 234
		// (get) Token: 0x060003DF RID: 991 RVA: 0x0005B174 File Offset: 0x00059374
		// (set) Token: 0x060003E0 RID: 992 RVA: 0x0005B188 File Offset: 0x00059388
		[JsonProperty("AIGeneratedBackstory")]
		public string AIGeneratedBackstory { get; set; } = null;

		// Token: 0x170000EB RID: 235
		// (get) Token: 0x060003E1 RID: 993 RVA: 0x0005B19C File Offset: 0x0005939C
		// (set) Token: 0x060003E2 RID: 994 RVA: 0x0005B1B0 File Offset: 0x000593B0
		[JsonProperty("KnownSecrets")]
		public List<string> KnownSecrets { get; set; } = new List<string>();

		// Token: 0x170000EC RID: 236
		// (get) Token: 0x060003E3 RID: 995 RVA: 0x0005B1C4 File Offset: 0x000593C4
		// (set) Token: 0x060003E4 RID: 996 RVA: 0x0005B1D8 File Offset: 0x000593D8
		[JsonProperty("KnownInfo")]
		public List<string> KnownInfo { get; set; } = new List<string>();

		// Token: 0x170000ED RID: 237
		// (get) Token: 0x060003E5 RID: 997 RVA: 0x0005B1EC File Offset: 0x000593EC
		// (set) Token: 0x060003E6 RID: 998 RVA: 0x0005B200 File Offset: 0x00059400
		[JsonProperty("KnownSecretsUserEdited")]
		public bool KnownSecretsUserEdited { get; set; } = false;

		// Token: 0x170000EE RID: 238
		// (get) Token: 0x060003E7 RID: 999 RVA: 0x0005B214 File Offset: 0x00059414
		// (set) Token: 0x060003E8 RID: 1000 RVA: 0x0005B228 File Offset: 0x00059428
		[JsonProperty("KnownInfoUserEdited")]
		public bool KnownInfoUserEdited { get; set; } = false;

		// Token: 0x170000EF RID: 239
		// (get) Token: 0x060003E9 RID: 1001 RVA: 0x0005B23C File Offset: 0x0005943C
		// (set) Token: 0x060003EA RID: 1002 RVA: 0x0005B250 File Offset: 0x00059450
		[JsonProperty("ClanTierRecognitionChecked")]
		public bool ClanTierRecognitionChecked { get; set; } = false;

		// Token: 0x170000F0 RID: 240
		// (get) Token: 0x060003EB RID: 1003 RVA: 0x0005B264 File Offset: 0x00059464
		// (set) Token: 0x060003EC RID: 1004 RVA: 0x0005B278 File Offset: 0x00059478
		[JsonProperty("RomanceLevel")]
		public float RomanceLevel { get; set; } = 0f;

		// Token: 0x170000F1 RID: 241
		// (get) Token: 0x060003ED RID: 1005 RVA: 0x0005B28C File Offset: 0x0005948C
		// (set) Token: 0x060003EE RID: 1006 RVA: 0x0005B2A0 File Offset: 0x000594A0
		[JsonProperty("LastRomanceInteractionDays")]
		public int LastRomanceInteractionDays { get; set; } = -1;

		// Token: 0x170000F2 RID: 242
		// (get) Token: 0x060003EF RID: 1007 RVA: 0x0005B2B4 File Offset: 0x000594B4
		// (set) Token: 0x060003F0 RID: 1008 RVA: 0x0005B2C8 File Offset: 0x000594C8
		[JsonProperty("IsRomanceEligible")]
		public bool IsRomanceEligible { get; set; } = true;

		// Token: 0x170000F3 RID: 243
		// (get) Token: 0x060003F1 RID: 1009 RVA: 0x0005B2DC File Offset: 0x000594DC
		// (set) Token: 0x060003F2 RID: 1010 RVA: 0x0005B2F0 File Offset: 0x000594F0
		[JsonProperty("SettlementCombatInfo")]
		public SettlementCombatInfo SettlementCombatInfo { get; set; }

		// Token: 0x170000F4 RID: 244
		// (get) Token: 0x060003F3 RID: 1011 RVA: 0x0005B304 File Offset: 0x00059504
		// (set) Token: 0x060003F4 RID: 1012 RVA: 0x0005B318 File Offset: 0x00059518
		[JsonProperty("DynamicEvents")]
		public List<string> DynamicEvents { get; set; } = new List<string>();

		// Token: 0x170000F5 RID: 245
		// (get) Token: 0x060003F5 RID: 1013 RVA: 0x0005B32C File Offset: 0x0005952C
		// (set) Token: 0x060003F6 RID: 1014 RVA: 0x0005B340 File Offset: 0x00059540
		[JsonProperty("LastEventAnalysisMessageIndex")]
		public int LastEventAnalysisMessageIndex { get; set; } = -1;

		// Token: 0x170000F6 RID: 246
		// (get) Token: 0x060003F7 RID: 1015 RVA: 0x0005B354 File Offset: 0x00059554
		// (set) Token: 0x060003F8 RID: 1016 RVA: 0x0005B368 File Offset: 0x00059568
		[JsonProperty("LastSeenFriends")]
		public Dictionary<string, float> LastSeenFriends { get; set; } = new Dictionary<string, float>();

		// Token: 0x170000F7 RID: 247
		// (get) Token: 0x060003F9 RID: 1017 RVA: 0x0005B37C File Offset: 0x0005957C
		// (set) Token: 0x060003FA RID: 1018 RVA: 0x0005B390 File Offset: 0x00059590
		[JsonProperty("IsNPCInitiatedConversation")]
		public bool IsNPCInitiatedConversation { get; set; } = false;

		// Token: 0x170000F8 RID: 248
		// (get) Token: 0x060003FB RID: 1019 RVA: 0x0005B3A4 File Offset: 0x000595A4
		// (set) Token: 0x060003FC RID: 1020 RVA: 0x0005B3B8 File Offset: 0x000595B8
		[JsonProperty("IsHostileInitiative")]
		public bool IsHostileInitiative { get; set; } = false;

		// Token: 0x170000F9 RID: 249
		// (get) Token: 0x060003FD RID: 1021 RVA: 0x0005B3CC File Offset: 0x000595CC
		// (set) Token: 0x060003FE RID: 1022 RVA: 0x0005B3E0 File Offset: 0x000595E0
		[JsonProperty("AdditionalContext")]
		public string AdditionalContext { get; set; } = "";

		// Token: 0x170000FA RID: 250
		// (get) Token: 0x060003FF RID: 1023 RVA: 0x0005B3F4 File Offset: 0x000595F4
		// (set) Token: 0x06000400 RID: 1024 RVA: 0x0005B408 File Offset: 0x00059608
		[JsonProperty("LastInteractionTimeDays")]
		public double LastInteractionTimeDays { get; set; } = -1.0;

		// Token: 0x170000FB RID: 251
		// (get) Token: 0x06000401 RID: 1025 RVA: 0x0005B41C File Offset: 0x0005961C
		// (set) Token: 0x06000402 RID: 1026 RVA: 0x0005B430 File Offset: 0x00059630
		[JsonProperty("VisitedSettlements")]
		public List<SettlementVisit> VisitedSettlements { get; set; } = new List<SettlementVisit>();

		// Token: 0x170000FC RID: 252
		// (get) Token: 0x06000403 RID: 1027 RVA: 0x0005B444 File Offset: 0x00059644
		// (set) Token: 0x06000404 RID: 1028 RVA: 0x0005B47C File Offset: 0x0005967C
		[JsonIgnore]
		public CampaignTime LastInteractionTime
		{
			get
			{
				return (this.LastInteractionTimeDays < 0.0) ? \u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u200B\u200D\u200B\u202B\u202E\u200F\u200C\u206B\u206B\u202A\u200D\u200B\u206A\u202A\u202E\u202A\u202D\u206D\u200F\u206C\u206E\u202E\u202B\u206C\u206D\u200E\u206A\u200C\u200C\u200B\u200B\u200D\u202D\u202E\u206D\u200B\u200B\u202E\u206A\u206A\u202E() : \u200B\u202B\u202D\u202C\u206F\u206D\u200F\u206E\u200E\u206B\u202B\u206B\u200B\u206A\u206D\u206D\u200E\u202D\u206A\u206F\u202E\u206F\u206A\u202C\u206D\u202C\u200D\u206A\u200B\u202E\u206A\u202A\u206B\u202E\u206F\u206C\u206E\u200F\u202A\u200D\u202E.~\u000Dh\u0082ü((float)this.LastInteractionTimeDays);
			}
			set
			{
				this.LastInteractionTimeDays = (\u206F\u206C\u206F\u202C\u206A\u206A\u200F\u200F\u202E\u206B\u206C\u200E\u202D\u200C\u206A\u206A\u206D\u206C\u200D\u202A\u200C\u200F\u200E\u200D\u202B\u202C\u206F\u206C\u206E\u206F\u200F\u202E\u200B\u200E\u206F\u202C\u200F\u200C\u202B\u200E\u202E.s/\u008Ek.(value, \u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.ÂPèÍn()) ? -1.0 : value.ToDays);
			}
		}

		// Token: 0x06000405 RID: 1029 RVA: 0x0005B4BC File Offset: 0x000596BC
		public void AddMessage(string message)
		{
			this.ConversationHistory.Add(message);
		}

		// Token: 0x06000406 RID: 1030 RVA: 0x0005B4D8 File Offset: 0x000596D8
		[MethodImpl(MethodImplOptions.NoInlining)]
		public string GetFormattedHistory()
		{
			string text;
			if (Enumerable.Any<string>(this.ConversationHistory))
			{
				text = \u200C\u206C\u202C\u202A\u200C\u200C\u202B\u202C\u200E\u202A\u202A\u200E\u202E\u206F\u206B\u200F\u206D\u206D\u206B\u200F\u200D\u200D\u206D\u206D\u202C\u200E\u202A\u200C\u200C\u202A\u206F\u206E\u200E\u206D\u206B\u202A\u200B\u200D\u200E\u206D\u202E.Ùc\u0014\u0010q(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-385087622), this.ConversationHistory);
				goto IL_8E;
			}
			IL_0E:
			int num = 447908742;
			IL_13:
			int num2 = num;
			switch ((-(num2 + ((-21175025 ^ 1957321557) + -1574524289 + ((-1236445332 ^ -111755507) - (314935893 ^ -2026302655)))) * 2056045937 ^ 908633038) % 3)
			{
			case 0:
				goto IL_0E;
			case 1:
				text = <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(900896502);
				goto IL_8E;
			}
			string result;
			return result;
			IL_8E:
			result = text;
			num = -1674455172;
			goto IL_13;
		}

		// Token: 0x06000407 RID: 1031 RVA: 0x0005B580 File Offset: 0x00059780
		public void AddDynamicEvent(string eventId)
		{
			bool flag = this.DynamicEvents == null;
			if (flag)
			{
				goto IL_0E;
			}
			goto IL_87;
			int num2;
			for (;;)
			{
				IL_13:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)((num ^ ~(-694743859 * -1255940821 - (432805358 ^ -1638050280)) ^ -388280008 + -1461498585 + ~1973161476) * -752839057 ^ -1297412685)) % 5U)
				{
				case 1U:
					goto IL_87;
				case 2U:
					this.DynamicEvents = new List<string>();
					num2 = (int)(num3 * 274926619U ^ 4289318984U);
					continue;
				case 3U:
					goto IL_0E;
				case 4U:
					this.DynamicEvents.Add(eventId);
					num2 = (int)(num3 * 1553325493U ^ 2934903206U);
					continue;
				}
				break;
			}
			return;
			IL_0E:
			num2 = -1204065471;
			goto IL_13;
			IL_87:
			num2 = ((!this.DynamicEvents.Contains(eventId)) ? -67611803 : 2119883506);
			goto IL_13;
		}

		// Token: 0x06000408 RID: 1032 RVA: 0x0005B65C File Offset: 0x0005985C
		public void RemoveExpiredEvents(List<string> expiredEventIds)
		{
			NPCContext.\u202D\u200B\u206B\u206D\u206A\u206A\u206B\u206A\u206F\u206B\u206E\u206D\u202A\u206D\u202B\u200C\u206F\u206F\u206B\u202E\u206A\u206B\u200F\u206B\u200D\u206E\u200D\u206D\u206E\u206F\u206D\u202A\u206A\u202E\u206D\u206E\u206A\u202D\u206A\u206F\u202E u202D_u200B_u206B_u206D_u206A_u206A_u206B_u206A_u206F_u206B_u206E_u206D_u202A_u206D_u202B_u200C_u206F_u206F_u206B_u202E_u206A_u206B_u200F_u206B_u200D_u206E_u200D_u206D_u206E_u206F_u206D_u202A_u206A_u202E_u206D_u206E_u206A_u202D_u206A_u206F_u202E = new NPCContext.\u202D\u200B\u206B\u206D\u206A\u206A\u206B\u206A\u206F\u206B\u206E\u206D\u202A\u206D\u202B\u200C\u206F\u206F\u206B\u202E\u206A\u206B\u200F\u206B\u200D\u206E\u200D\u206D\u206E\u206F\u206D\u202A\u206A\u202E\u206D\u206E\u206A\u202D\u206A\u206F\u202E();
			u202D_u200B_u206B_u206D_u206A_u206A_u206B_u206A_u206F_u206B_u206E_u206D_u202A_u206D_u202B_u200C_u206F_u206F_u206B_u202E_u206A_u206B_u200F_u206B_u200D_u206E_u200D_u206D_u206E_u206F_u206D_u202A_u206A_u202E_u206D_u206E_u206A_u202D_u206A_u206F_u202E.\u202E\u206E\u206C\u202C\u206E\u200B\u206B\u200D\u202B\u206B\u202C\u200E\u202C\u202A\u202C\u200E\u206C\u200D\u202B\u206F\u200F\u202D\u200F\u200B\u200F\u202B\u206A\u202A\u206B\u200F\u202E\u202E\u202E\u200C\u206D\u202A\u206B\u202C\u200C\u202E\u202E = expiredEventIds;
			if (this.DynamicEvents != null)
			{
				goto IL_16;
			}
			bool flag = true;
			goto IL_5C;
			int num2;
			for (;;)
			{
				IL_1B:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(-num - (798024124 * -1153091769 - ~1098125710))) % 5U)
				{
				case 0U:
					num2 = (int)(num3 * 3189654666U ^ 112796073U);
					continue;
				case 1U:
					goto IL_50;
				case 3U:
					goto IL_16;
				case 4U:
					this.DynamicEvents.RemoveAll(new Predicate<string>(u202D_u200B_u206B_u206D_u206A_u206A_u206B_u206A_u206F_u206B_u206E_u206D_u202A_u206D_u202B_u200C_u206F_u206F_u206B_u202E_u206A_u206B_u200F_u206B_u200D_u206E_u200D_u206D_u206E_u206F_u206D_u202A_u206A_u202E_u206D_u206E_u206A_u202D_u206A_u206F_u202E.\u206B\u202E\u206C\u206A\u200F\u202C\u202A\u200C\u206D\u206E\u202B\u206F\u200D\u206B\u206C\u200C\u200E\u202D\u200D\u206C\u206F\u206B\u206A\u200F\u206F\u206B\u206F\u202C\u202E\u202C\u206D\u200C\u200C\u200B\u202A\u202E\u200B\u202C\u206F\u206F\u202E));
					num2 = 57631943;
					continue;
				}
				break;
			}
			return;
			IL_50:
			flag = (u202D_u200B_u206B_u206D_u206A_u206A_u206B_u206A_u206F_u206B_u206E_u206D_u202A_u206D_u202B_u200C_u206F_u206F_u206B_u202E_u206A_u206B_u200F_u206B_u200D_u206E_u200D_u206D_u206E_u206F_u206D_u202A_u206A_u202E_u206D_u206E_u206A_u202D_u206A_u206F_u202E.\u202E\u206E\u206C\u202C\u206E\u200B\u206B\u200D\u202B\u206B\u202C\u200E\u202C\u202A\u202C\u200E\u206C\u200D\u202B\u206F\u200F\u202D\u200F\u200B\u200F\u202B\u206A\u202A\u206B\u200F\u202E\u202E\u202E\u200C\u206D\u202A\u206B\u202C\u200C\u202E\u202E == null);
			goto IL_5C;
			IL_16:
			num2 = 547253729;
			goto IL_1B;
			IL_5C:
			num2 = (flag ? 923299970 : 510627466);
			goto IL_1B;
		}

		// Token: 0x06000409 RID: 1033 RVA: 0x0005B70C File Offset: 0x0005990C
		public bool HasKnowledgeOf(string eventId)
		{
			bool flag;
			if (this.DynamicEvents == null)
			{
				flag = false;
				goto IL_57;
			}
			IL_09:
			int num = 114943188;
			IL_0E:
			int num2 = num;
			switch ((num2 - ~(~(266686126 ^ -1762185459)) - (437637268 - -1512956507 - ~-720193428)) % 3)
			{
			case 1:
				flag = this.DynamicEvents.Contains(eventId);
				goto IL_57;
			case 2:
				goto IL_09;
			}
			bool result;
			return result;
			IL_57:
			result = flag;
			num = 1557507608;
			goto IL_0E;
		}

		// Token: 0x0600040A RID: 1034 RVA: 0x0005B77C File Offset: 0x0005997C
		public List<string> GetNewMessagesForEventAnalysis()
		{
			if (this.ConversationHistory != null)
			{
				goto IL_09;
			}
			bool flag = true;
			goto IL_5C;
			int num2;
			List<string> result;
			for (;;)
			{
				IL_0E:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(-(-num) - -1241119963 ^ -1150218111)) % 7U)
				{
				case 0U:
					goto IL_09;
				case 1U:
					result = new List<string>();
					num2 = (int)(num3 * 1527760453U ^ 2943987510U);
					continue;
				case 2U:
					goto IL_4B;
				case 3U:
					result = new List<string>();
					num2 = (int)(num3 * 1807461591U ^ 3466898139U);
					continue;
				case 5U:
				{
					int num4 = this.LastEventAnalysisMessageIndex + 1;
					bool flag2 = num4 >= this.ConversationHistory.Count;
					num2 = ((!flag2) ? 2056268076 : 1411561296);
					continue;
				}
				case 6U:
				{
					int num4;
					result = Enumerable.ToList<string>(Enumerable.Skip<string>(this.ConversationHistory, num4));
					num2 = -1589565411;
					continue;
				}
				}
				break;
			}
			return result;
			IL_4B:
			flag = (this.ConversationHistory.Count == 0);
			goto IL_5C;
			IL_09:
			num2 = 1169055576;
			goto IL_0E;
			IL_5C:
			bool flag3 = flag;
			num2 = ((!flag3) ? 993596003 : -1920075437);
			goto IL_0E;
		}

		// Token: 0x0600040B RID: 1035 RVA: 0x0005B87C File Offset: 0x00059A7C
		public void MarkMessagesAsSentToEventAnalysis()
		{
			if (this.ConversationHistory != null)
			{
				goto IL_09;
			}
			bool flag = false;
			goto IL_48;
			int num2;
			for (;;)
			{
				IL_0E:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(num * -238348479 * 1307473941)) % 4U)
				{
				case 0U:
					this.LastEventAnalysisMessageIndex = this.ConversationHistory.Count - 1;
					num2 = (int)(num3 * 1324258807U ^ 4191280450U);
					continue;
				case 1U:
					goto IL_37;
				case 3U:
					goto IL_09;
				}
				break;
			}
			return;
			IL_37:
			flag = (this.ConversationHistory.Count > 0);
			goto IL_48;
			IL_09:
			num2 = -1780513047;
			goto IL_0E;
			IL_48:
			num2 = (flag ? -393943276 : -545141634);
			goto IL_0E;
		}

		// Token: 0x0600040C RID: 1036 RVA: 0x0005B90C File Offset: 0x00059B0C
		[MethodImpl(MethodImplOptions.NoInlining)]
		public NPCContext()
		{
		}

		// Token: 0x04000236 RID: 566
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206A\u202E\u202E\u202D\u200E\u206B\u206A\u200C\u206C\u200D\u206C\u200D\u200F\u206A\u206E\u200F\u200E\u200C\u200B\u200C\u206B\u206C\u202B\u202C\u202D\u206E\u206B\u206F\u206D\u206B\u206C\u202A\u202C\u200C\u206F\u202A\u200D\u206B\u202D\u206B\u202E;

		// Token: 0x04000237 RID: 567
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206E\u202E\u202D\u206C\u206F\u200D\u206C\u206D\u206A\u202D\u202B\u200C\u206A\u200B\u200F\u202E\u206F\u206E\u206E\u206D\u206A\u202B\u206C\u206D\u206E\u202D\u206D\u202A\u206C\u206C\u200D\u200E\u202B\u202C\u202D\u202E\u202C\u202B\u206E\u202D\u202E;

		// Token: 0x04000238 RID: 568
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200E\u200F\u202C\u200C\u200D\u200B\u206E\u200D\u206B\u206F\u200D\u206D\u206E\u202C\u202C\u202C\u206E\u206D\u206B\u206C\u206B\u200B\u206C\u200D\u202B\u200C\u200B\u202D\u200F\u206B\u200E\u202B\u206B\u200B\u200C\u206B\u200F\u202B\u206D\u206B\u202E;

		// Token: 0x04000239 RID: 569
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206C\u200C\u202D\u206F\u206E\u200E\u200F\u200F\u206E\u200E\u202B\u202C\u206A\u202D\u206C\u200D\u200D\u202D\u200F\u202B\u206B\u202A\u202A\u206A\u200C\u202C\u202A\u202A\u206F\u206E\u200E\u200B\u202B\u206B\u206B\u206C\u206E\u206A\u200F\u206E\u202E;

		// Token: 0x0400023A RID: 570
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200E\u202A\u206F\u206C\u202D\u202B\u202C\u206A\u206B\u202E\u202C\u202D\u202A\u200F\u200F\u200C\u202D\u202B\u206F\u206A\u206A\u206A\u200B\u200E\u200F\u206D\u206A\u200D\u202A\u206A\u200B\u202A\u202D\u200D\u202A\u206D\u202A\u200F\u206D\u206C\u202E;

		// Token: 0x0400023B RID: 571
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200C\u200F\u202A\u200F\u202A\u200F\u206B\u206C\u200F\u200E\u206B\u200C\u206D\u202D\u202C\u202E\u200E\u206E\u202B\u202E\u200F\u200C\u206D\u200E\u206C\u206F\u202C\u206B\u206B\u206F\u202C\u206E\u200C\u202B\u202E\u200B\u202D\u206D\u200F\u202B\u202E;

		// Token: 0x0400023C RID: 572
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u200C\u206E\u200D\u202A\u200D\u206E\u206E\u206A\u200D\u206C\u202A\u200E\u202A\u200C\u206D\u200D\u200C\u200C\u206A\u202B\u206D\u202E\u202D\u206B\u200E\u202C\u202D\u200E\u200E\u202B\u202C\u202C\u200C\u202B\u206F\u200C\u200C\u200C\u200D\u202B\u202E;

		// Token: 0x0400023D RID: 573
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u200C\u200D\u200D\u200B\u202E\u200B\u206E\u200E\u206A\u202C\u206D\u206F\u202C\u206D\u200D\u206A\u202C\u202E\u202C\u206E\u206F\u200D\u206B\u202A\u206D\u202B\u200C\u202D\u202D\u206E\u202E\u200E\u200F\u202C\u206B\u200D\u202E\u202B\u200F\u206D\u202E;

		// Token: 0x0400023E RID: 574
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u202B\u200C\u200C\u202E\u206F\u202B\u202A\u200F\u206B\u200B\u200D\u202B\u202B\u206E\u202B\u200B\u202D\u200E\u200D\u206C\u200C\u206F\u200D\u200C\u202C\u200F\u202C\u202B\u200E\u200C\u202A\u206B\u206F\u200E\u206A\u206B\u200C\u200E\u200C\u200C\u202E;

		// Token: 0x0400023F RID: 575
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private PlayerInfo \u206C\u202E\u200C\u206B\u200F\u202D\u200D\u202C\u202D\u200F\u202A\u202A\u202E\u202B\u200C\u200E\u206E\u202E\u200E\u202E\u206F\u200B\u200B\u200D\u206E\u202A\u206F\u200C\u200D\u202E\u202D\u200E\u206B\u200D\u200B\u202B\u206F\u202A\u206D\u206A\u202E;

		// Token: 0x04000240 RID: 576
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<string> \u200E\u200D\u202A\u206C\u202D\u200D\u202A\u202C\u206A\u200F\u206E\u206B\u206E\u200B\u202B\u200F\u206F\u202B\u200C\u206F\u206F\u200C\u206D\u200F\u200E\u200B\u200F\u200C\u202B\u206C\u206F\u206C\u200B\u202B\u206D\u202C\u206D\u200E\u200D\u200E\u202E;

		// Token: 0x04000241 RID: 577
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202D\u202A\u200B\u200F\u200C\u206D\u206D\u206A\u206D\u202E\u202E\u202E\u202C\u202E\u206C\u206B\u200B\u206C\u202E\u206A\u200F\u202D\u202D\u200C\u202E\u202B\u200D\u206E\u202C\u202C\u206C\u202E\u206A\u202E\u202C\u200C\u202E\u206F\u206E\u206A\u202E;

		// Token: 0x04000242 RID: 578
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private PlayerRelation \u202D\u200D\u206A\u206E\u206E\u202D\u206E\u206E\u200B\u206B\u206C\u200C\u202C\u202E\u200C\u206C\u202A\u202C\u206B\u206F\u206E\u202B\u206C\u206B\u206C\u202C\u206A\u200B\u200E\u202D\u200B\u200D\u206B\u206F\u206E\u200D\u202D\u202A\u202D\u206A\u202E;

		// Token: 0x04000243 RID: 579
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200C\u200E\u202D\u206E\u200F\u206D\u200B\u206B\u202D\u200C\u206F\u206C\u202B\u206B\u206F\u206A\u206B\u200B\u200B\u202D\u202A\u202E\u206E\u200B\u200B\u200E\u200E\u200E\u200C\u202D\u206E\u200C\u200D\u206E\u202C\u200E\u200F\u206D\u202D\u202C\u202E;

		// Token: 0x04000244 RID: 580
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<CampaignEvent> \u200B\u200E\u200D\u202B\u206E\u206E\u202E\u206F\u206C\u202A\u200D\u206B\u206D\u206F\u206A\u206D\u200B\u206E\u206B\u202A\u200B\u202E\u206F\u202E\u200B\u200F\u202A\u206D\u202B\u202B\u206B\u200E\u200F\u202B\u206F\u206E\u200C\u202B\u206E\u202E;

		// Token: 0x04000245 RID: 581
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<CampaignEvent> \u202D\u206C\u200B\u200B\u206A\u200F\u200B\u200C\u202C\u200C\u202D\u200C\u202A\u200C\u202C\u206A\u202C\u206C\u202B\u206B\u200B\u202D\u200E\u202E\u202E\u202B\u202A\u206B\u202C\u206F\u206A\u202E\u200F\u206D\u202B\u206C\u200F\u200E\u206B\u202D\u202E;

		// Token: 0x04000246 RID: 582
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private EmotionalState \u202B\u202B\u202D\u202B\u206A\u200F\u200F\u200F\u206B\u202C\u202A\u202A\u200F\u206A\u206C\u200F\u202E\u200C\u200F\u206A\u200F\u206E\u206F\u202A\u206D\u206C\u200C\u202E\u202C\u200D\u202E\u200E\u202D\u200B\u200C\u202E\u202D\u200B\u202D\u206F\u202E;

		// Token: 0x04000247 RID: 583
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200F\u202C\u206D\u206A\u200D\u202B\u202E\u202C\u200D\u202C\u200B\u200E\u200E\u202E\u202B\u202E\u206C\u202D\u202C\u200B\u202E\u206D\u206F\u206D\u202B\u202E\u202A\u202E\u202A\u202C\u200B\u202B\u200B\u202E\u202D\u202D\u202E\u206A\u202A\u202C\u202E;

		// Token: 0x04000248 RID: 584
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private TimeContext \u206E\u206B\u206B\u206D\u202D\u206F\u200E\u206B\u206A\u200C\u202C\u202D\u200F\u202B\u200C\u200F\u206C\u206B\u200D\u200F\u206D\u200F\u202A\u206D\u206D\u206C\u206D\u206E\u202D\u206C\u200E\u206E\u200E\u206A\u200C\u206B\u202D\u202C\u202A\u200E\u202E;

		// Token: 0x04000249 RID: 585
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private PlayerForces \u206B\u206E\u206D\u202E\u206D\u206B\u202A\u200B\u200B\u206D\u206C\u206C\u206E\u202E\u206B\u202E\u200F\u202E\u206C\u206C\u200E\u200E\u206A\u200F\u206C\u206F\u206E\u200D\u200C\u206B\u202D\u202A\u200F\u206E\u206E\u206B\u202A\u206F\u200B\u202B\u202E;

		// Token: 0x0400024A RID: 586
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private NPCForces \u200E\u202B\u202E\u200B\u200C\u200E\u202E\u206C\u206E\u206E\u200F\u200B\u200C\u206A\u202E\u202E\u202A\u206E\u200C\u206A\u206B\u206A\u202E\u202B\u200B\u200D\u202E\u206C\u202A\u200D\u206A\u202D\u206F\u206C\u206E\u206B\u206B\u200C\u200B\u206F\u202E;

		// Token: 0x0400024B RID: 587
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<string> \u202A\u200D\u202B\u206B\u202A\u200F\u200F\u202D\u200C\u202B\u202A\u202B\u202E\u206C\u200E\u202E\u200C\u202B\u202C\u206C\u206F\u206E\u202B\u202B\u200B\u200D\u202A\u206E\u206D\u202A\u200C\u206D\u200D\u206D\u200F\u202B\u202A\u206D\u206E\u206F\u202E;

		// Token: 0x0400024C RID: 588
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u206F\u206A\u202A\u202B\u202C\u202D\u206D\u206E\u202C\u200F\u202D\u200C\u206B\u202B\u206A\u206A\u200C\u206B\u200C\u200B\u202A\u206A\u200C\u200D\u200B\u206A\u202B\u202C\u206E\u206C\u206B\u206D\u206E\u200E\u202B\u206B\u200D\u202B\u202E\u202A\u202E;

		// Token: 0x0400024D RID: 589
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u202C\u206D\u200B\u200C\u200F\u200E\u206F\u206F\u202C\u202D\u200F\u200D\u200B\u206F\u202B\u206B\u200F\u202D\u206B\u206A\u200F\u206B\u206D\u202A\u206A\u202C\u206E\u206E\u206A\u206E\u202A\u202D\u206E\u206D\u200B\u206C\u202C\u206B\u206A\u202B\u202E;

		// Token: 0x0400024E RID: 590
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206D\u202A\u206D\u206A\u206F\u202C\u206E\u200B\u206A\u202C\u206D\u200B\u202A\u206F\u200C\u200B\u202D\u202A\u202D\u206D\u202C\u200C\u200D\u202C\u206A\u200D\u206D\u206C\u206C\u200F\u200D\u206F\u200F\u200B\u200C\u200E\u200D\u206A\u202D\u200F\u202E;

		// Token: 0x0400024F RID: 591
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u206C\u206A\u202B\u200D\u200C\u206D\u200D\u202A\u206E\u202D\u206E\u202A\u200F\u202D\u200E\u206D\u206A\u202A\u206E\u202B\u202E\u206D\u200E\u200E\u200D\u200B\u200B\u200C\u202C\u202A\u206D\u200C\u202A\u202E\u200F\u200B\u202B\u200D\u202E;

		// Token: 0x04000250 RID: 592
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int? \u202E\u200B\u200C\u202B\u200C\u206F\u202B\u202A\u200E\u206A\u202C\u202B\u200B\u206C\u206E\u200E\u206C\u202D\u206D\u206D\u206A\u202B\u200F\u202A\u206B\u206A\u202D\u206F\u206D\u206F\u206F\u200F\u202B\u200C\u206B\u202B\u206F\u200F\u200C\u206F\u202E;

		// Token: 0x04000251 RID: 593
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206B\u202D\u206F\u200E\u202E\u206D\u202A\u202D\u200C\u202A\u202A\u206A\u202A\u202A\u206F\u200C\u202B\u206E\u202B\u202E\u202D\u200D\u200E\u202A\u206A\u206B\u206D\u202D\u206C\u200B\u206E\u202C\u200D\u202B\u206A\u200D\u206A\u206C\u202C\u202E\u202E;

		// Token: 0x04000252 RID: 594
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200D\u200B\u206D\u202A\u206F\u206C\u206F\u206F\u206D\u206D\u200C\u206A\u202D\u202D\u200E\u200E\u202D\u202A\u206B\u202A\u202A\u202D\u202C\u206A\u200F\u206F\u202B\u206F\u200D\u206B\u206D\u206C\u202A\u206A\u206A\u202B\u206A\u206D\u200F\u202D\u202E;

		// Token: 0x04000253 RID: 595
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u200F\u202C\u202B\u202A\u202C\u202D\u202B\u202D\u202A\u206C\u206E\u206D\u202E\u202C\u202B\u206F\u200C\u206C\u202B\u206D\u206E\u206C\u200B\u202E\u206A\u202C\u200F\u206A\u200B\u206F\u206A\u206A\u206C\u206F\u206D\u202B\u202E\u206E\u200E\u202E;

		// Token: 0x04000254 RID: 596
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u200B\u206D\u206F\u200D\u200B\u206C\u202A\u200C\u200D\u200B\u206D\u200F\u202E\u200B\u206B\u206F\u200C\u202A\u200D\u200D\u200F\u202D\u202B\u200B\u202C\u202E\u202A\u202A\u202B\u200E\u202C\u200D\u206D\u202B\u202E\u200F\u206A\u202E\u206D\u202E;

		// Token: 0x04000255 RID: 597
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206E\u200B\u202B\u202D\u200C\u200D\u200C\u206D\u200D\u200C\u206B\u202D\u200E\u202C\u206B\u202C\u202E\u200F\u200E\u200B\u202B\u200F\u206B\u200C\u202E\u200D\u202D\u206E\u200E\u202C\u206E\u200F\u200D\u202C\u202A\u202D\u202A\u202C\u202D\u202A\u202E;

		// Token: 0x04000256 RID: 598
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200E\u206A\u202C\u200B\u202D\u202C\u200B\u200B\u206F\u206D\u202E\u206C\u200B\u206D\u206F\u202B\u202E\u206D\u200D\u202C\u200D\u202E\u206C\u200D\u202B\u202B\u206D\u206B\u200C\u206E\u206F\u202C\u202B\u200F\u206E\u200E\u206C\u206D\u206D\u202E\u202E;

		// Token: 0x04000257 RID: 599
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200C\u202A\u202D\u200D\u206E\u200E\u206E\u206B\u202A\u206B\u202A\u202A\u206F\u206B\u206A\u206E\u202A\u206E\u202A\u202A\u206D\u202C\u206B\u202A\u206E\u200B\u200B\u200C\u202D\u206B\u202E\u202B\u200F\u200B\u206F\u206F\u206F\u206C\u206B\u202B\u202E;

		// Token: 0x04000258 RID: 600
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206F\u206A\u202C\u206A\u202E\u206A\u206F\u206E\u200C\u206D\u200E\u206E\u202E\u200D\u206F\u202B\u206C\u200C\u206F\u202A\u206A\u202B\u200E\u202D\u202D\u206E\u200B\u206C\u200B\u202A\u200C\u200F\u200B\u206D\u202C\u206F\u206F\u200F\u200B\u200C\u202E;

		// Token: 0x04000259 RID: 601
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202C\u206C\u206F\u200F\u202C\u206B\u200B\u202C\u200D\u206D\u200B\u206B\u200D\u206A\u200D\u200C\u200B\u202C\u206A\u200D\u202D\u200D\u202D\u202D\u206D\u200D\u202C\u206A\u202C\u206D\u206A\u206A\u202B\u206E\u202A\u206E\u200D\u206D\u200B\u202E\u202E;

		// Token: 0x0400025A RID: 602
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206F\u206C\u206A\u206B\u202D\u206F\u200B\u202E\u206E\u206F\u202E\u206E\u202A\u200C\u202B\u200B\u206A\u206C\u206F\u206B\u200D\u206E\u206E\u200D\u202A\u200F\u206F\u206F\u206A\u200E\u206F\u200E\u202B\u202B\u202D\u202E\u202A\u200F\u206F\u206C\u202E;

		// Token: 0x0400025B RID: 603
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200B\u200E\u200E\u202B\u200D\u206D\u202E\u200C\u202E\u200D\u200C\u206E\u202B\u202D\u200E\u202E\u206D\u206E\u206B\u206C\u202A\u200B\u202A\u206F\u206F\u202B\u200E\u206A\u200E\u206B\u206A\u202C\u200E\u200B\u202A\u200E\u200D\u200E\u202B\u200C\u202E;

		// Token: 0x0400025C RID: 604
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private AIResponse \u206A\u206F\u206C\u202C\u200D\u206D\u202A\u202A\u202D\u206B\u202A\u206D\u206C\u206F\u202B\u200E\u202B\u202A\u202E\u202C\u206C\u202B\u206B\u202C\u206E\u200B\u202E\u200B\u202C\u202A\u200C\u200C\u200E\u206F\u200C\u206D\u200B\u200B\u200C\u200C\u202E;

		// Token: 0x0400025D RID: 605
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private PendingRelationChange \u206E\u206E\u202B\u200D\u206C\u206C\u206A\u206F\u200E\u202B\u202A\u202C\u206B\u202E\u202A\u202D\u202D\u206E\u206B\u206B\u206F\u206B\u202B\u202B\u200B\u206A\u206B\u206F\u206F\u202C\u202E\u206E\u206B\u202E\u206E\u202B\u206F\u202D\u202B\u206D\u202E;

		// Token: 0x0400025E RID: 606
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private PendingRelationChange \u202B\u206F\u202A\u200E\u206E\u200C\u206F\u202C\u202A\u200B\u206A\u206D\u200F\u200C\u200B\u200F\u206A\u206F\u200C\u200B\u200D\u206B\u202D\u200C\u206B\u200E\u206D\u206C\u202C\u202E\u200B\u200E\u202A\u200F\u206C\u206E\u200B\u202B\u206D\u202A\u202E;

		// Token: 0x0400025F RID: 607
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private PendingWorkshopSale \u202B\u202E\u206F\u206F\u206A\u202C\u200D\u202E\u206E\u200B\u206E\u206F\u202A\u200D\u206A\u206D\u202E\u206F\u202B\u202B\u200B\u200F\u206E\u206A\u200D\u202D\u200B\u202E\u202B\u206D\u200F\u202D\u206D\u200B\u200D\u206D\u206A\u202A\u202A\u202C\u202E;

		// Token: 0x04000260 RID: 608
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private MoneyTransferInfo \u202D\u200D\u206F\u200F\u200F\u202E\u206A\u202A\u202C\u202B\u200D\u206B\u200E\u200C\u206B\u206E\u202C\u206B\u202C\u202A\u202A\u200D\u200F\u206F\u202A\u200C\u206F\u206A\u200E\u206C\u206F\u202B\u206E\u206A\u202D\u200F\u202A\u206A\u206B\u200C\u202E;

		// Token: 0x04000261 RID: 609
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<ItemTransferData> \u200F\u200E\u206C\u206F\u202B\u206C\u206E\u202E\u206F\u202A\u206A\u200E\u206C\u202A\u200D\u206A\u202D\u200B\u206C\u200F\u200C\u206A\u206C\u200F\u200C\u200E\u206F\u206D\u202E\u206A\u206B\u202E\u206B\u206B\u206D\u200C\u206C\u202A\u200B\u206A\u202E;

		// Token: 0x04000262 RID: 610
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200B\u202B\u200D\u206A\u206D\u202B\u206A\u200F\u202A\u202A\u200B\u206B\u206C\u202D\u206C\u202E\u206C\u200D\u206F\u202B\u202A\u202C\u206A\u206C\u202A\u202C\u200C\u206C\u206E\u200E\u206F\u202A\u202D\u206F\u206C\u202D\u206D\u200C\u200C\u200D\u202E;

		// Token: 0x04000263 RID: 611
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202B\u202B\u206B\u202B\u200F\u200D\u200B\u202E\u200F\u206D\u206E\u202C\u200C\u206F\u200C\u202D\u200D\u206E\u200D\u206F\u202D\u202A\u200E\u206D\u206D\u202A\u202B\u200C\u202B\u202A\u206D\u202C\u202A\u202E\u206C\u206D\u200F\u206E\u200C\u202A\u202E;

		// Token: 0x04000264 RID: 612
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202C\u202E\u206D\u206F\u202C\u202A\u200D\u206A\u202D\u206A\u202A\u206E\u206E\u202E\u200D\u202E\u200B\u202A\u200C\u200F\u206C\u200F\u200F\u202C\u200D\u206F\u206A\u200C\u200B\u200B\u200C\u200C\u202E\u202E\u202C\u202D\u200D\u206C\u206A\u200F\u202E;

		// Token: 0x04000265 RID: 613
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<string> \u206A\u200E\u206C\u200C\u200B\u202D\u200D\u202A\u202C\u206A\u200E\u206B\u200B\u202D\u206F\u206B\u202E\u200B\u202B\u200C\u202C\u200B\u202E\u202D\u202B\u200C\u206D\u206B\u200F\u206B\u200E\u206A\u202D\u202E\u200D\u200F\u206F\u206B\u200C\u202B\u202E;

		// Token: 0x04000266 RID: 614
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<string> \u206C\u206B\u202A\u200C\u202B\u200C\u202A\u206E\u202E\u202C\u206A\u206E\u202D\u202E\u200E\u202B\u202E\u206B\u200F\u202A\u202B\u206A\u206F\u206B\u200F\u200E\u202A\u206E\u202D\u206A\u206A\u202D\u206D\u200D\u200B\u206C\u202A\u202D\u200E\u202D\u202E;

		// Token: 0x04000267 RID: 615
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u202D\u202A\u206E\u202A\u206C\u202A\u202C\u202A\u200D\u200B\u202A\u200C\u206B\u206D\u202B\u202C\u200D\u202A\u200E\u202B\u202D\u200E\u206F\u206C\u200D\u206A\u206B\u202A\u202D\u206C\u202D\u202A\u202A\u200B\u200E\u202B\u202D\u206C\u200B\u206C\u202E;

		// Token: 0x04000268 RID: 616
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u206E\u200F\u202C\u202D\u206F\u200B\u206E\u206F\u202D\u206A\u206B\u202E\u200C\u206F\u202B\u200E\u200F\u200B\u206A\u206E\u200E\u202D\u206E\u200B\u200C\u206A\u206B\u202B\u206D\u206B\u202E\u206C\u206D\u206A\u202B\u206F\u200E\u200B\u206D\u202E;

		// Token: 0x04000269 RID: 617
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u200C\u200C\u202D\u206F\u206D\u206B\u202B\u200B\u206E\u200D\u206A\u202A\u200D\u200E\u206E\u206E\u206E\u206C\u206B\u202C\u202B\u202D\u202D\u200F\u206A\u200F\u202E\u206B\u206A\u202B\u206A\u202B\u200C\u206A\u206F\u206A\u206B\u202A\u200C\u200C\u202E;

		// Token: 0x0400026A RID: 618
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u200D\u206C\u206C\u206E\u202D\u202A\u202A\u200C\u206C\u206B\u206C\u202D\u202B\u206A\u206C\u206F\u206D\u200E\u200F\u200D\u202C\u202A\u200D\u200D\u200B\u202B\u206C\u200F\u200B\u202D\u206D\u206F\u202C\u202A\u206E\u202A\u202B\u206F\u206A\u206F\u202E;

		// Token: 0x0400026B RID: 619
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u200D\u200C\u202D\u202A\u200D\u202D\u206D\u200D\u202C\u206E\u200D\u202E\u206D\u206A\u206F\u202E\u200C\u202E\u202A\u206B\u206D\u200F\u206B\u206B\u202C\u202A\u206D\u202B\u200D\u202B\u206A\u202C\u206D\u206C\u206E\u206B\u206D\u200E\u200B\u206C\u202E;

		// Token: 0x0400026C RID: 620
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u202D\u200E\u206B\u200C\u206A\u202C\u206C\u206B\u202D\u200D\u206C\u202E\u206E\u206A\u206F\u206E\u202B\u200E\u200F\u202C\u206F\u202E\u206D\u200D\u200C\u206B\u206D\u206A\u202D\u200D\u202A\u202C\u200B\u206D\u200C\u202E\u206C\u200C\u206C\u200D\u202E;

		// Token: 0x0400026D RID: 621
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private SettlementCombatInfo \u200E\u200F\u206E\u206B\u200D\u200E\u206F\u200E\u200B\u206E\u200B\u206A\u200D\u200E\u206A\u206B\u202B\u200C\u206E\u206E\u200C\u200C\u206F\u202A\u200F\u202B\u202E\u200B\u200C\u206C\u200F\u206F\u206D\u202B\u200C\u200E\u200C\u202A\u206D\u206F\u202E;

		// Token: 0x0400026E RID: 622
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<string> \u206F\u202B\u200B\u202C\u206E\u206C\u206A\u200C\u202A\u202A\u202C\u200E\u200D\u206C\u206E\u206D\u200B\u206D\u200F\u202D\u206D\u200D\u206A\u200D\u206C\u202D\u200C\u206F\u202D\u200B\u206A\u206E\u202D\u206C\u202B\u206D\u200C\u200B\u206F\u200E\u202E;

		// Token: 0x0400026F RID: 623
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u200D\u200D\u202B\u206C\u200F\u200B\u206B\u200D\u200B\u206A\u206F\u200D\u206C\u202B\u206C\u206D\u206D\u206C\u200D\u206D\u200E\u206C\u202D\u200E\u202C\u202B\u202B\u206F\u202B\u200C\u206F\u202E\u206B\u200C\u200B\u206A\u200E\u206C\u202A\u202E\u202E;

		// Token: 0x04000270 RID: 624
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private Dictionary<string, float> \u202D\u200F\u200C\u206B\u202B\u206B\u202A\u200B\u206D\u200E\u206E\u200F\u200D\u206B\u200B\u200D\u200E\u200F\u202B\u206C\u200B\u200F\u202B\u202C\u200F\u206F\u200C\u206C\u202D\u202B\u206F\u200C\u202B\u200B\u206C\u202A\u206B\u200C\u206C\u200D\u202E;

		// Token: 0x04000271 RID: 625
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u206A\u202B\u200D\u200E\u206C\u202C\u206E\u202B\u200B\u206F\u200E\u202C\u202E\u206B\u202C\u200F\u200E\u202D\u200F\u206E\u202C\u200F\u200D\u202B\u202D\u202B\u206C\u202A\u206B\u206A\u206D\u206A\u206F\u202B\u206E\u206F\u206E\u200D\u200E\u200E\u202E;

		// Token: 0x04000272 RID: 626
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u202D\u202E\u200E\u206F\u206D\u200C\u200B\u202A\u202D\u206F\u206F\u206E\u200F\u202D\u202A\u200F\u206E\u206A\u206C\u200B\u200F\u202A\u200F\u206D\u200B\u206A\u200C\u200F\u206F\u202C\u206C\u202C\u202A\u202B\u200E\u206B\u200E\u202D\u200B\u206E\u202E;

		// Token: 0x04000273 RID: 627
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200F\u202A\u200C\u202B\u202B\u200F\u200B\u206D\u206B\u206C\u206E\u202D\u206C\u200B\u206C\u200F\u206E\u200B\u200B\u200F\u206D\u206F\u206B\u202E\u206E\u202A\u202C\u206D\u206D\u206C\u202C\u206F\u202B\u200D\u206D\u206B\u202C\u206F\u206C\u202E;

		// Token: 0x04000274 RID: 628
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private double \u200C\u202E\u206B\u202D\u202E\u200B\u200D\u202C\u206B\u200D\u200D\u202D\u202C\u200C\u202E\u200F\u206E\u200E\u200B\u200C\u202C\u200E\u200E\u202D\u200B\u206D\u202E\u206C\u200E\u200F\u200F\u206C\u200F\u202A\u202C\u206D\u200B\u202B\u200D\u202A\u202E;

		// Token: 0x04000275 RID: 629
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<SettlementVisit> \u202E\u200C\u206A\u200C\u202E\u202B\u200B\u200D\u200B\u200E\u202C\u206C\u200C\u200D\u200D\u206E\u200D\u202D\u206B\u206F\u200F\u200B\u206A\u202A\u206F\u200E\u206A\u206D\u202D\u202B\u206B\u206F\u206D\u206A\u200C\u202B\u200E\u202D\u206F\u200C\u202E;

		// Token: 0x0200006C RID: 108
		[CompilerGenerated]
		private sealed class \u202D\u200B\u206B\u206D\u206A\u206A\u206B\u206A\u206F\u206B\u206E\u206D\u202A\u206D\u202B\u200C\u206F\u206F\u206B\u202E\u206A\u206B\u200F\u206B\u200D\u206E\u200D\u206D\u206E\u206F\u206D\u202A\u206A\u202E\u206D\u206E\u206A\u202D\u206A\u206F\u202E
		{
			// Token: 0x0600040E RID: 1038 RVA: 0x0005BA54 File Offset: 0x00059C54
			internal bool \u206B\u202E\u206C\u206A\u200F\u202C\u202A\u200C\u206D\u206E\u202B\u206F\u200D\u206B\u206C\u200C\u200E\u202D\u200D\u206C\u206F\u206B\u206A\u200F\u206F\u206B\u206F\u202C\u202E\u202C\u206D\u200C\u200C\u200B\u202A\u202E\u200B\u202C\u206F\u206F\u202E(string A_1)
			{
				return this.\u202E\u206E\u206C\u202C\u206E\u200B\u206B\u200D\u202B\u206B\u202C\u200E\u202C\u202A\u202C\u200E\u206C\u200D\u202B\u206F\u200F\u202D\u200F\u200B\u200F\u202B\u206A\u202A\u206B\u200F\u202E\u202E\u202E\u200C\u206D\u202A\u206B\u202C\u200C\u202E\u202E.Contains(A_1);
			}

			// Token: 0x04000276 RID: 630
			public List<string> \u202E\u206E\u206C\u202C\u206E\u200B\u206B\u200D\u202B\u206B\u202C\u200E\u202C\u202A\u202C\u200E\u206C\u200D\u202B\u206F\u200F\u202D\u200F\u200B\u200F\u202B\u206A\u202A\u206B\u200F\u202E\u202E\u202E\u200C\u206D\u202A\u206B\u202C\u200C\u202E\u202E;
		}
	}
}
