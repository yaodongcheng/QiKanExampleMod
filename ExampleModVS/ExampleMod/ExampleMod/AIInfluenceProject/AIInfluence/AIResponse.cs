using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace AIInfluence
{
	// Token: 0x02000077 RID: 119
	[JsonSerializable]
	public class AIResponse
	{
		// Token: 0x17000129 RID: 297
		// (get) Token: 0x06000474 RID: 1140 RVA: 0x0005C320 File Offset: 0x0005A520
		// (set) Token: 0x06000475 RID: 1141 RVA: 0x0005C334 File Offset: 0x0005A534
		[JsonProperty("response")]
		public string Response { get; set; }

		// Token: 0x1700012A RID: 298
		// (get) Token: 0x06000476 RID: 1142 RVA: 0x0005C348 File Offset: 0x0005A548
		// (set) Token: 0x06000477 RID: 1143 RVA: 0x0005C35C File Offset: 0x0005A55C
		[JsonProperty("suspected_lie")]
		public bool SuspectedLie { get; set; }

		// Token: 0x1700012B RID: 299
		// (get) Token: 0x06000478 RID: 1144 RVA: 0x0005C370 File Offset: 0x0005A570
		// (set) Token: 0x06000479 RID: 1145 RVA: 0x0005C384 File Offset: 0x0005A584
		[JsonProperty("claimed_name")]
		public string ClaimedName { get; set; }

		// Token: 0x1700012C RID: 300
		// (get) Token: 0x0600047A RID: 1146 RVA: 0x0005C398 File Offset: 0x0005A598
		// (set) Token: 0x0600047B RID: 1147 RVA: 0x0005C3AC File Offset: 0x0005A5AC
		[JsonProperty("claimed_clan")]
		public string ClaimedClan { get; set; }

		// Token: 0x1700012D RID: 301
		// (get) Token: 0x0600047C RID: 1148 RVA: 0x0005C3C0 File Offset: 0x0005A5C0
		// (set) Token: 0x0600047D RID: 1149 RVA: 0x0005C3D4 File Offset: 0x0005A5D4
		[JsonProperty("claimed_age")]
		public int? ClaimedAge { get; set; }

		// Token: 0x1700012E RID: 302
		// (get) Token: 0x0600047E RID: 1150 RVA: 0x0005C3E8 File Offset: 0x0005A5E8
		// (set) Token: 0x0600047F RID: 1151 RVA: 0x0005C3FC File Offset: 0x0005A5FC
		[JsonProperty("tone")]
		public string Tone { get; set; }

		// Token: 0x1700012F RID: 303
		// (get) Token: 0x06000480 RID: 1152 RVA: 0x0005C410 File Offset: 0x0005A610
		// (set) Token: 0x06000481 RID: 1153 RVA: 0x0005C424 File Offset: 0x0005A624
		[JsonProperty("threat_level")]
		public string ThreatLevel { get; set; }

		// Token: 0x17000130 RID: 304
		// (get) Token: 0x06000482 RID: 1154 RVA: 0x0005C438 File Offset: 0x0005A638
		// (set) Token: 0x06000483 RID: 1155 RVA: 0x0005C44C File Offset: 0x0005A64C
		[JsonProperty("escalation_state")]
		public string EscalationState { get; set; }

		// Token: 0x17000131 RID: 305
		// (get) Token: 0x06000484 RID: 1156 RVA: 0x0005C460 File Offset: 0x0005A660
		// (set) Token: 0x06000485 RID: 1157 RVA: 0x0005C474 File Offset: 0x0005A674
		[JsonProperty("deescalation_attempt")]
		public bool DeescalationAttempt { get; set; }

		// Token: 0x17000132 RID: 306
		// (get) Token: 0x06000486 RID: 1158 RVA: 0x0005C488 File Offset: 0x0005A688
		// (set) Token: 0x06000487 RID: 1159 RVA: 0x0005C49C File Offset: 0x0005A69C
		[JsonProperty("decision")]
		public string Decision { get; set; }

		// Token: 0x17000133 RID: 307
		// (get) Token: 0x06000488 RID: 1160 RVA: 0x0005C4B0 File Offset: 0x0005A6B0
		// (set) Token: 0x06000489 RID: 1161 RVA: 0x0005C4C4 File Offset: 0x0005A6C4
		[JsonProperty("romance_intent")]
		public string RomanceIntent { get; set; }

		// Token: 0x17000134 RID: 308
		// (get) Token: 0x0600048A RID: 1162 RVA: 0x0005C4D8 File Offset: 0x0005A6D8
		// (set) Token: 0x0600048B RID: 1163 RVA: 0x0005C4EC File Offset: 0x0005A6EC
		[JsonProperty("kingdom_action")]
		public string KingdomAction { get; set; }

		// Token: 0x17000135 RID: 309
		// (get) Token: 0x0600048C RID: 1164 RVA: 0x0005C500 File Offset: 0x0005A700
		// (set) Token: 0x0600048D RID: 1165 RVA: 0x0005C514 File Offset: 0x0005A714
		[JsonProperty("kingdom_action_reason")]
		public string KingdomActionReason { get; set; }

		// Token: 0x17000136 RID: 310
		// (get) Token: 0x0600048E RID: 1166 RVA: 0x0005C528 File Offset: 0x0005A728
		// (set) Token: 0x0600048F RID: 1167 RVA: 0x0005C53C File Offset: 0x0005A73C
		[JsonProperty("technical_action")]
		public string TechnicalAction { get; set; }

		// Token: 0x17000137 RID: 311
		// (get) Token: 0x06000490 RID: 1168 RVA: 0x0005C550 File Offset: 0x0005A750
		// (set) Token: 0x06000491 RID: 1169 RVA: 0x0005C564 File Offset: 0x0005A764
		[JsonProperty("claimed_gold")]
		public int ClaimedGold { get; set; }

		// Token: 0x17000138 RID: 312
		// (get) Token: 0x06000492 RID: 1170 RVA: 0x0005C578 File Offset: 0x0005A778
		// (set) Token: 0x06000493 RID: 1171 RVA: 0x0005C58C File Offset: 0x0005A78C
		[JsonProperty("workshop_action")]
		public string WorkshopAction { get; set; }

		// Token: 0x17000139 RID: 313
		// (get) Token: 0x06000494 RID: 1172 RVA: 0x0005C5A0 File Offset: 0x0005A7A0
		// (set) Token: 0x06000495 RID: 1173 RVA: 0x0005C5B4 File Offset: 0x0005A7B4
		[JsonProperty("workshop_string_id")]
		public string WorkshopStringId { get; set; }

		// Token: 0x1700013A RID: 314
		// (get) Token: 0x06000496 RID: 1174 RVA: 0x0005C5C8 File Offset: 0x0005A7C8
		// (set) Token: 0x06000497 RID: 1175 RVA: 0x0005C5DC File Offset: 0x0005A7DC
		[JsonProperty("workshop_price")]
		public int WorkshopPrice { get; set; }

		// Token: 0x1700013B RID: 315
		// (get) Token: 0x06000498 RID: 1176 RVA: 0x0005C5F0 File Offset: 0x0005A7F0
		// (set) Token: 0x06000499 RID: 1177 RVA: 0x0005C604 File Offset: 0x0005A804
		[JsonProperty("character_death")]
		public CharacterDeathInfo CharacterDeath { get; set; }

		// Token: 0x1700013C RID: 316
		// (get) Token: 0x0600049A RID: 1178 RVA: 0x0005C618 File Offset: 0x0005A818
		// (set) Token: 0x0600049B RID: 1179 RVA: 0x0005C62C File Offset: 0x0005A82C
		[JsonProperty("money_transfer")]
		public MoneyTransferInfo MoneyTransfer { get; set; }

		// Token: 0x1700013D RID: 317
		// (get) Token: 0x0600049C RID: 1180 RVA: 0x0005C640 File Offset: 0x0005A840
		// (set) Token: 0x0600049D RID: 1181 RVA: 0x0005C654 File Offset: 0x0005A854
		[JsonProperty("item_transfers")]
		public List<ItemTransferData> ItemTransfers { get; set; }

		// Token: 0x1700013E RID: 318
		// (get) Token: 0x0600049E RID: 1182 RVA: 0x0005C668 File Offset: 0x0005A868
		// (set) Token: 0x0600049F RID: 1183 RVA: 0x0005C67C File Offset: 0x0005A87C
		[JsonProperty("character_personality")]
		public string CharacterPersonality { get; set; }

		// Token: 0x1700013F RID: 319
		// (get) Token: 0x060004A0 RID: 1184 RVA: 0x0005C690 File Offset: 0x0005A890
		// (set) Token: 0x060004A1 RID: 1185 RVA: 0x0005C6A4 File Offset: 0x0005A8A4
		[JsonProperty("character_backstory")]
		public string CharacterBackstory { get; set; }

		// Token: 0x17000140 RID: 320
		// (get) Token: 0x060004A2 RID: 1186 RVA: 0x0005C6B8 File Offset: 0x0005A8B8
		// (set) Token: 0x060004A3 RID: 1187 RVA: 0x0005C6CC File Offset: 0x0005A8CC
		[JsonProperty("tts_instructions")]
		public string TTSInstructions { get; set; }

		// Token: 0x040002A2 RID: 674
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202B\u200C\u200D\u202B\u206E\u200B\u200D\u206A\u206E\u200F\u202E\u200C\u206D\u200E\u202C\u202A\u202B\u200C\u200F\u200B\u202B\u206E\u200B\u200C\u202E\u202D\u202D\u200D\u202B\u200E\u200D\u206B\u200C\u206D\u202E\u202C\u206D\u202B\u202E\u202C\u202E;

		// Token: 0x040002A3 RID: 675
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u200B\u206A\u200B\u202C\u202B\u206D\u202B\u202B\u202C\u200B\u202A\u202A\u206C\u200F\u200C\u206D\u206B\u206D\u202B\u206C\u206B\u206A\u200E\u202E\u200F\u200F\u200D\u206A\u206F\u202A\u206D\u206F\u202B\u202C\u200F\u206C\u206B\u202C\u202B\u202C\u202E;

		// Token: 0x040002A4 RID: 676
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202B\u206E\u206C\u200F\u202E\u200F\u206B\u200C\u202D\u202E\u206C\u206D\u206A\u200D\u202E\u206A\u202E\u202B\u206C\u202D\u202C\u200B\u202E\u206A\u206D\u202B\u206D\u206E\u206B\u202A\u200C\u206D\u202A\u206E\u200B\u202B\u202E\u200B\u202A\u206D\u202E;

		// Token: 0x040002A5 RID: 677
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200D\u206B\u206E\u200B\u200B\u202B\u206F\u200B\u206B\u202E\u202B\u200F\u202A\u202A\u200E\u200B\u202E\u206E\u200E\u206D\u206F\u200B\u206F\u200E\u206A\u206E\u200B\u202E\u206A\u202D\u202E\u202A\u200B\u200F\u206C\u200E\u200F\u206C\u202D\u206B\u202E;

		// Token: 0x040002A6 RID: 678
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int? \u206F\u202B\u206A\u200B\u206F\u206D\u202C\u206A\u206C\u202C\u206D\u206E\u206C\u200B\u200F\u202E\u200B\u206C\u200D\u202C\u202A\u206F\u206E\u200D\u200F\u202E\u200E\u202D\u206D\u206E\u206D\u206A\u200E\u202E\u206B\u206F\u206D\u206D\u206A\u206E\u202E;

		// Token: 0x040002A7 RID: 679
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206F\u206E\u202A\u206D\u200C\u206E\u206F\u202A\u202E\u206C\u202A\u200C\u200F\u200E\u200C\u200D\u206F\u200C\u200B\u200F\u206D\u206A\u200C\u200D\u206F\u206E\u206D\u202A\u206B\u202A\u200D\u206D\u206C\u202E\u202D\u202E\u202A\u202D\u206B\u200D\u202E;

		// Token: 0x040002A8 RID: 680
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206B\u206E\u200D\u202C\u206D\u206C\u206E\u206A\u206B\u202E\u200C\u200D\u206F\u206E\u200B\u206E\u202D\u206C\u200E\u206A\u206A\u200E\u200D\u206E\u202E\u206F\u206F\u202D\u206C\u206D\u202A\u202E\u206A\u200F\u202C\u206F\u200E\u200C\u202D\u200D\u202E;

		// Token: 0x040002A9 RID: 681
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206D\u202D\u202A\u206A\u206F\u200E\u206D\u202C\u202D\u200B\u200F\u200F\u202C\u206F\u200C\u200B\u202B\u200C\u206E\u200C\u202C\u206F\u200B\u200F\u202C\u206A\u200C\u200C\u206D\u202A\u202B\u202A\u202E\u200B\u202B\u200B\u202C\u206D\u206E\u206C\u202E;

		// Token: 0x040002AA RID: 682
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u206B\u202E\u202C\u206F\u206A\u206B\u200B\u202D\u202D\u206F\u206F\u200B\u200D\u202E\u206C\u202A\u206A\u206D\u206D\u200B\u206A\u202B\u200F\u202C\u202D\u202A\u202C\u200E\u200D\u202B\u200D\u200E\u206B\u200F\u202D\u206E\u202C\u206B\u206C\u206F\u202E;

		// Token: 0x040002AB RID: 683
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202A\u202B\u202C\u206C\u206F\u202E\u200E\u202B\u206B\u206A\u206A\u202B\u202D\u206E\u200E\u200E\u206C\u206E\u200F\u200D\u202D\u202D\u206E\u200E\u202C\u206D\u200D\u206E\u200F\u206C\u202B\u206E\u200C\u202C\u200B\u200C\u202D\u202A\u202B\u200D\u202E;

		// Token: 0x040002AC RID: 684
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206C\u206F\u206F\u200B\u200F\u200F\u200D\u202D\u206C\u206F\u206C\u206E\u202B\u206C\u200B\u206C\u206A\u200D\u206E\u202D\u200B\u202E\u206D\u206C\u202C\u200E\u206B\u206A\u206C\u200F\u200E\u200B\u202A\u206D\u202E\u202A\u200E\u206D\u202C\u206E\u202E;

		// Token: 0x040002AD RID: 685
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206B\u202A\u202C\u206D\u206A\u202E\u202D\u200F\u200C\u200B\u200F\u200F\u202C\u202C\u200C\u200E\u206B\u200C\u202C\u206A\u200C\u202C\u206D\u202E\u202E\u206C\u206B\u200F\u206A\u202A\u206A\u206D\u202E\u202D\u202D\u202A\u202A\u202C\u200D\u206D\u202E;

		// Token: 0x040002AE RID: 686
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206F\u202D\u206B\u200B\u206D\u206D\u200D\u206B\u206B\u200F\u206B\u206D\u202B\u200C\u200C\u206A\u200F\u206C\u200D\u202A\u202A\u200B\u206F\u200F\u206F\u206D\u200C\u206E\u206D\u202B\u202B\u200F\u200D\u206B\u206E\u202A\u202A\u202C\u202D\u202D\u202E;

		// Token: 0x040002AF RID: 687
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206F\u200F\u200B\u202A\u200E\u206B\u202A\u202E\u206D\u200B\u202E\u202A\u206F\u200F\u202D\u200B\u200D\u200C\u206A\u206C\u200E\u202A\u200D\u200F\u206B\u200E\u200F\u206B\u202D\u200E\u202C\u202C\u202A\u202C\u200F\u200F\u200D\u202E\u200D\u202E;

		// Token: 0x040002B0 RID: 688
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u206D\u200D\u200F\u206F\u206F\u206B\u206E\u206A\u206C\u206F\u200E\u202A\u202B\u200D\u206F\u202D\u202C\u206C\u206B\u202B\u200F\u200D\u206C\u200D\u200B\u202E\u200D\u200B\u206F\u202D\u202B\u206F\u202A\u202A\u200E\u202B\u206A\u200D\u202A\u206E\u202E;

		// Token: 0x040002B1 RID: 689
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206A\u206A\u206B\u206A\u206B\u200E\u202C\u202E\u206A\u206E\u206B\u206A\u200D\u206B\u206B\u206C\u200D\u202E\u200C\u200F\u206C\u200E\u200C\u202E\u200E\u200E\u202C\u202C\u200B\u206F\u200E\u206B\u206D\u206E\u206A\u206A\u202E\u206F\u202A\u200C\u202E;

		// Token: 0x040002B2 RID: 690
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206B\u200E\u206D\u206F\u200F\u200B\u202D\u200F\u206E\u206E\u202A\u206F\u206B\u206F\u200D\u200D\u206E\u202B\u202A\u202E\u206C\u206B\u200D\u206C\u202E\u202E\u206D\u206F\u206A\u206E\u206B\u200B\u200E\u202E\u206B\u200E\u202B\u206C\u200B\u206A\u202E;

		// Token: 0x040002B3 RID: 691
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u200E\u206C\u206D\u202D\u202D\u202E\u202D\u206F\u200D\u200B\u200C\u202C\u202D\u206A\u206B\u202D\u206E\u206A\u200F\u206A\u206C\u200E\u200F\u202A\u202A\u206E\u206E\u202A\u202B\u200B\u206E\u200B\u202E\u202B\u202B\u206E\u200D\u200C\u206E\u206A\u202E;

		// Token: 0x040002B4 RID: 692
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private CharacterDeathInfo \u200C\u200C\u200E\u206A\u202A\u200E\u202E\u206E\u200C\u206A\u202A\u206D\u206B\u206E\u206C\u202D\u202C\u206E\u202D\u200F\u202C\u202E\u202E\u200B\u206B\u206F\u206D\u200F\u200C\u206D\u202E\u206B\u200E\u206A\u206A\u200D\u206A\u202A\u200D\u202A\u202E;

		// Token: 0x040002B5 RID: 693
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private MoneyTransferInfo \u206E\u200C\u202C\u202B\u202B\u200D\u206B\u202A\u202B\u206F\u200B\u206F\u200D\u200B\u200F\u206C\u206B\u206A\u202C\u200F\u202C\u202E\u202D\u206A\u200F\u206A\u202C\u202C\u206B\u202C\u206C\u206E\u200E\u202A\u206B\u206F\u202D\u202D\u206F\u200D\u202E;

		// Token: 0x040002B6 RID: 694
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<ItemTransferData> \u200B\u206B\u202E\u206D\u202A\u200B\u206C\u200F\u202D\u206D\u206D\u202C\u206D\u200E\u206C\u202E\u202A\u206E\u202D\u206E\u202D\u202B\u206E\u206D\u206F\u200C\u206D\u202A\u206C\u200C\u206D\u202A\u200F\u202B\u202C\u206C\u200F\u200C\u202A\u202C\u202E;

		// Token: 0x040002B7 RID: 695
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206C\u206C\u202A\u202E\u200F\u202E\u200C\u206B\u200D\u206F\u202D\u206E\u200B\u206A\u202A\u200B\u202C\u202C\u200E\u206D\u206F\u206F\u202D\u200C\u200F\u200C\u202B\u206B\u200D\u200F\u202B\u200D\u200D\u202B\u202C\u200B\u200B\u202B\u200E\u202C\u202E;

		// Token: 0x040002B8 RID: 696
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206D\u202B\u202B\u206B\u206A\u202E\u200F\u200D\u200D\u200D\u206C\u200C\u200D\u200D\u200E\u202C\u206C\u200D\u202A\u206B\u202B\u202B\u206E\u200C\u206A\u200B\u202E\u206F\u206C\u200F\u206F\u202B\u200B\u202C\u200B\u200D\u200B\u200F\u200E\u200F\u202E;

		// Token: 0x040002B9 RID: 697
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200F\u206A\u206A\u202D\u202C\u202A\u206F\u202C\u206B\u202E\u202E\u202B\u202A\u206B\u200C\u202D\u200D\u200E\u200E\u202C\u206B\u206B\u202C\u206C\u206A\u200F\u200C\u202C\u202C\u200F\u200D\u206B\u202A\u202C\u206D\u200B\u202D\u200D\u200B\u206C\u202E;
	}
}
