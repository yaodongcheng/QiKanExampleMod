using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x02000150 RID: 336
	[JsonSerializable]
	public class CombatSituationAnalysis
	{
		// Token: 0x17000203 RID: 515
		// (get) Token: 0x06000A96 RID: 2710 RVA: 0x0001DBE7 File Offset: 0x0001BDE7
		// (set) Token: 0x06000A97 RID: 2711 RVA: 0x0001DBEF File Offset: 0x0001BDEF
		[JsonProperty("aggressor_string_id")]
		public string AggressorStringId { get; set; }

		// Token: 0x17000204 RID: 516
		// (get) Token: 0x06000A98 RID: 2712 RVA: 0x0001DBF8 File Offset: 0x0001BDF8
		// (set) Token: 0x06000A99 RID: 2713 RVA: 0x0001DC00 File Offset: 0x0001BE00
		[JsonProperty("defender_string_id")]
		public string DefenderStringId { get; set; }

		// Token: 0x17000205 RID: 517
		// (get) Token: 0x06000A9A RID: 2714 RVA: 0x0001DC09 File Offset: 0x0001BE09
		// (set) Token: 0x06000A9B RID: 2715 RVA: 0x0001DC11 File Offset: 0x0001BE11
		[JsonProperty("witnesses")]
		public List<string> Witnesses { get; set; }

		// Token: 0x17000206 RID: 518
		// (get) Token: 0x06000A9C RID: 2716 RVA: 0x0001DC1A File Offset: 0x0001BE1A
		// (set) Token: 0x06000A9D RID: 2717 RVA: 0x0001DC22 File Offset: 0x0001BE22
		[JsonProperty("needs_defenders")]
		public bool NeedsDefenders { get; set; }

		// Token: 0x17000207 RID: 519
		// (get) Token: 0x06000A9E RID: 2718 RVA: 0x0001DC2B File Offset: 0x0001BE2B
		// (set) Token: 0x06000A9F RID: 2719 RVA: 0x0001DC33 File Offset: 0x0001BE33
		[JsonProperty("civilian_panic")]
		public bool CivilianPanic { get; set; }

		// Token: 0x17000208 RID: 520
		// (get) Token: 0x06000AA0 RID: 2720 RVA: 0x0001DC3C File Offset: 0x0001BE3C
		// (set) Token: 0x06000AA1 RID: 2721 RVA: 0x0001DC44 File Offset: 0x0001BE44
		[JsonProperty("lords")]
		public List<LordIntervention> Lords { get; set; }

		// Token: 0x17000209 RID: 521
		// (get) Token: 0x06000AA2 RID: 2722 RVA: 0x0001DC4D File Offset: 0x0001BE4D
		// (set) Token: 0x06000AA3 RID: 2723 RVA: 0x0001DC55 File Offset: 0x0001BE55
		[JsonProperty("player_involved")]
		public bool PlayerInvolved { get; set; }

		// Token: 0x1700020A RID: 522
		// (get) Token: 0x06000AA4 RID: 2724 RVA: 0x0001DC5E File Offset: 0x0001BE5E
		// (set) Token: 0x06000AA5 RID: 2725 RVA: 0x0001DC66 File Offset: 0x0001BE66
		[JsonProperty("situation_description")]
		public string SituationDescription { get; set; }

		// Token: 0x1700020B RID: 523
		// (get) Token: 0x06000AA6 RID: 2726 RVA: 0x0001DC6F File Offset: 0x0001BE6F
		// (set) Token: 0x06000AA7 RID: 2727 RVA: 0x0001DC77 File Offset: 0x0001BE77
		[JsonProperty("civilian_phrases_male_panic")]
		public List<string> CivilianPhrasesMalePanic { get; set; }

		// Token: 0x1700020C RID: 524
		// (get) Token: 0x06000AA8 RID: 2728 RVA: 0x0001DC80 File Offset: 0x0001BE80
		// (set) Token: 0x06000AA9 RID: 2729 RVA: 0x0001DC88 File Offset: 0x0001BE88
		[JsonProperty("civilian_phrases_male_fight")]
		public List<string> CivilianPhrasesMaleFight { get; set; }

		// Token: 0x1700020D RID: 525
		// (get) Token: 0x06000AAA RID: 2730 RVA: 0x0001DC91 File Offset: 0x0001BE91
		// (set) Token: 0x06000AAB RID: 2731 RVA: 0x0001DC99 File Offset: 0x0001BE99
		[JsonProperty("civilian_phrases_female")]
		public List<string> CivilianPhrasesFemale { get; set; }

		// Token: 0x1700020E RID: 526
		// (get) Token: 0x06000AAC RID: 2732 RVA: 0x0001DCA2 File Offset: 0x0001BEA2
		// (set) Token: 0x06000AAD RID: 2733 RVA: 0x0001DCAA File Offset: 0x0001BEAA
		[JsonProperty("civilian_phrases_child")]
		public List<string> CivilianPhrasesChild { get; set; }

		// Token: 0x1700020F RID: 527
		// (get) Token: 0x06000AAE RID: 2734 RVA: 0x0001DCB3 File Offset: 0x0001BEB3
		// (set) Token: 0x06000AAF RID: 2735 RVA: 0x0001DCBB File Offset: 0x0001BEBB
		[JsonProperty("notable_phrases")]
		public Dictionary<string, List<string>> NotablePhrases { get; set; }

		// Token: 0x17000210 RID: 528
		// (get) Token: 0x06000AB0 RID: 2736 RVA: 0x0001DCC4 File Offset: 0x0001BEC4
		// (set) Token: 0x06000AB1 RID: 2737 RVA: 0x0001DCCC File Offset: 0x0001BECC
		[JsonProperty("companion_stance")]
		public Dictionary<string, string> CompanionStance { get; set; }
	}
}
