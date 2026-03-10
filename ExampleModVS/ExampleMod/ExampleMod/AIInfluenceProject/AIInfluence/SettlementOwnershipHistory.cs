using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence
{
	// Token: 0x020000BC RID: 188
	public class SettlementOwnershipHistory
	{
		// Token: 0x1700016C RID: 364
		// (get) Token: 0x06000655 RID: 1621 RVA: 0x00082368 File Offset: 0x00080568
		// (set) Token: 0x06000656 RID: 1622 RVA: 0x0008237C File Offset: 0x0008057C
		[JsonProperty("SettlementId")]
		public string SettlementId { get; set; }

		// Token: 0x1700016D RID: 365
		// (get) Token: 0x06000657 RID: 1623 RVA: 0x00082390 File Offset: 0x00080590
		// (set) Token: 0x06000658 RID: 1624 RVA: 0x000823A4 File Offset: 0x000805A4
		[JsonProperty("SettlementName")]
		public string SettlementName { get; set; }

		// Token: 0x1700016E RID: 366
		// (get) Token: 0x06000659 RID: 1625 RVA: 0x000823B8 File Offset: 0x000805B8
		// (set) Token: 0x0600065A RID: 1626 RVA: 0x000823CC File Offset: 0x000805CC
		[JsonProperty("OriginalOwnerKingdomId")]
		public string OriginalOwnerKingdomId { get; set; }

		// Token: 0x1700016F RID: 367
		// (get) Token: 0x0600065B RID: 1627 RVA: 0x000823E0 File Offset: 0x000805E0
		// (set) Token: 0x0600065C RID: 1628 RVA: 0x000823F4 File Offset: 0x000805F4
		[JsonProperty("OriginalOwnerKingdomName")]
		public string OriginalOwnerKingdomName { get; set; }

		// Token: 0x17000170 RID: 368
		// (get) Token: 0x0600065D RID: 1629 RVA: 0x00082408 File Offset: 0x00080608
		// (set) Token: 0x0600065E RID: 1630 RVA: 0x0008241C File Offset: 0x0008061C
		[JsonProperty("CurrentOwnerKingdomId")]
		public string CurrentOwnerKingdomId { get; set; }

		// Token: 0x17000171 RID: 369
		// (get) Token: 0x0600065F RID: 1631 RVA: 0x00082430 File Offset: 0x00080630
		// (set) Token: 0x06000660 RID: 1632 RVA: 0x00082444 File Offset: 0x00080644
		[JsonProperty("CurrentOwnerKingdomName")]
		public string CurrentOwnerKingdomName { get; set; }

		// Token: 0x17000172 RID: 370
		// (get) Token: 0x06000661 RID: 1633 RVA: 0x00082458 File Offset: 0x00080658
		// (set) Token: 0x06000662 RID: 1634 RVA: 0x0008246C File Offset: 0x0008066C
		[JsonProperty("OwnershipChanges")]
		public List<OwnershipChange> OwnershipChanges { get; set; } = new List<OwnershipChange>();

		// Token: 0x06000663 RID: 1635 RVA: 0x00082480 File Offset: 0x00080680
		[MethodImpl(MethodImplOptions.NoInlining)]
		public string GetOwnershipDescription()
		{
			bool flag = \u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(this.OriginalOwnerKingdomId, this.CurrentOwnerKingdomId);
			if (flag)
			{
				goto IL_1E;
			}
			goto IL_EB;
			int num2;
			string result;
			string text;
			for (;;)
			{
				IL_23:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(-(uint)(((-777194762 * 1962635483 ^ (-795549619 ^ 1555798179) ^ (-359267462 + -1194753557 ^ (1519526609 ^ 1012789950))) - num) * 1381313617 + -1193675009 * 2142635601))) % 14U)
				{
				case 0U:
					goto IL_1E;
				case 1U:
					num2 = (int)(num3 * 385751604U ^ 3464150132U);
					continue;
				case 2U:
					goto IL_EB;
				case 3U:
					num2 = 614722965;
					continue;
				case 4U:
				{
					bool flag2 = this.OwnershipChanges.Count > 1;
					num2 = ((!flag2) ? 614722965 : 1245009632);
					continue;
				}
				case 5U:
					result = \u202B\u202E\u202C\u200D\u206F\u206D\u206F\u202A\u206A\u206C\u206C\u206B\u206C\u202A\u200C\u202B\u200D\u206F\u202D\u206F\u206A\u202B\u206A\u206C\u206B\u200F\u202C\u200C\u206D\u202B\u206A\u202D\u200D\u202B\u200D\u206C\u206B\u206A\u206D\u200C\u202E.Þ\u00A4\u00A4F'(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-511133706), this.OriginalOwnerKingdomName, <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(276378332));
					num2 = -66671123;
					continue;
				case 6U:
					result = text;
					num2 = -66671123;
					continue;
				case 7U:
				{
					int num4;
					text = \u202B\u202E\u202C\u200D\u206F\u206D\u206F\u202A\u206A\u206C\u206C\u206B\u206C\u202A\u200C\u202B\u200D\u206F\u202D\u206F\u206A\u202B\u206A\u206C\u206B\u200F\u202C\u200C\u206D\u202B\u206A\u202D\u200D\u202B\u200D\u206C\u206B\u206A\u206D\u200C\u202E.Þ\u00A4\u00A4F'(text, <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-2117630651), this.OwnershipChanges[num4].ToKingdomName);
					num4++;
					num2 = -1311198786;
					continue;
				}
				case 8U:
				{
					int num4 = 0;
					num2 = (int)(num3 * 751199529U ^ 788949550U);
					continue;
				}
				case 9U:
					num2 = (int)(num3 * 2329515093U ^ 2612203977U);
					continue;
				case 11U:
				{
					int num4;
					num2 = ((num4 < this.OwnershipChanges.Count - 1) ? 129916000 : -1841676552);
					continue;
				}
				case 12U:
				{
					OwnershipChange ownershipChange = this.OwnershipChanges[0];
					text = \u202B\u202E\u202C\u200D\u206F\u206D\u206F\u202A\u206A\u206C\u206C\u206B\u206C\u202A\u200C\u202B\u200D\u206F\u202D\u206F\u206A\u202B\u206A\u206C\u206B\u200F\u202C\u200C\u206D\u202B\u206A\u202D\u200D\u202B\u200D\u206C\u206B\u206A\u206D\u200C\u202E.Þ\u00A4\u00A4F'(text, <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(77884993), ownershipChange.ToKingdomName);
					num2 = 614722965;
					continue;
				}
				case 13U:
				{
					OwnershipChange ownershipChange2 = Enumerable.Last<OwnershipChange>(this.OwnershipChanges);
					text = \u202B\u202E\u202C\u200D\u206F\u206D\u206F\u202A\u206A\u206C\u206C\u206B\u206C\u202A\u200C\u202B\u200D\u206F\u202D\u206F\u206A\u202B\u206A\u206C\u206B\u200F\u202C\u200C\u206D\u202B\u206A\u202D\u200D\u202B\u200D\u206C\u206B\u206A\u206D\u200C\u202E.Þ\u00A4\u00A4F'(text, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1102930446), ownershipChange2.ToKingdomName);
					num2 = ((this.OwnershipChanges.Count > 2) ? 1496248947 : -226543960);
					continue;
				}
				}
				break;
			}
			return result;
			IL_1E:
			num2 = 28539798;
			goto IL_23;
			IL_EB:
			text = \u206F\u206F\u206B\u202A\u200E\u200D\u206E\u206E\u206F\u206F\u202D\u206B\u206B\u200B\u202E\u206B\u206F\u202E\u202A\u202D\u200E\u200C\u206C\u200C\u200E\u200E\u200C\u200B\u200D\u206B\u202B\u206B\u200B\u206C\u206E\u200B\u206F\u200D\u202E._fØ\u00BBÜ(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(193328081), this.OriginalOwnerKingdomName);
			num2 = ((this.OwnershipChanges.Count == 1) ? -2026937835 : -270479121);
			goto IL_23;
		}

		// Token: 0x04000415 RID: 1045
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206D\u202C\u202E\u206C\u206E\u202B\u202B\u206D\u206D\u206F\u206C\u206A\u200F\u206E\u200D\u202D\u206A\u200B\u202B\u206C\u202B\u200B\u206C\u202D\u206B\u202E\u206C\u206D\u206E\u206D\u202E\u202A\u206C\u200C\u206A\u202D\u202E\u206B\u202B\u206B\u202E;

		// Token: 0x04000416 RID: 1046
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206C\u200C\u206C\u206D\u206B\u206A\u200D\u202C\u200B\u200B\u206D\u200C\u200B\u202B\u200F\u206D\u206C\u202B\u200C\u200F\u206F\u206A\u202D\u202C\u206A\u200C\u202C\u206F\u206B\u202C\u206E\u200E\u206A\u202C\u202E\u206D\u206D\u200F\u200F\u202E;

		// Token: 0x04000417 RID: 1047
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202B\u202B\u200F\u206E\u206A\u206B\u202C\u200D\u206A\u202B\u200C\u206C\u202A\u206F\u202A\u206D\u200E\u206A\u200E\u200E\u202E\u202A\u206A\u206E\u202D\u206C\u200C\u206F\u200C\u202B\u202D\u200C\u206F\u206B\u206C\u200B\u206D\u206D\u200E\u206D\u202E;

		// Token: 0x04000418 RID: 1048
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206D\u200B\u202C\u200C\u200C\u200D\u206B\u206C\u206F\u202D\u206F\u206C\u206D\u202D\u206F\u202A\u200F\u202A\u202D\u200F\u200C\u202A\u206D\u202D\u206E\u200F\u206D\u200B\u200B\u206E\u206C\u206A\u202B\u206B\u206B\u206A\u200D\u200C\u206A\u200F\u202E;

		// Token: 0x04000419 RID: 1049
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200C\u200C\u200D\u200C\u200B\u206F\u206E\u200D\u200C\u200C\u206F\u206D\u206A\u202E\u206A\u200C\u202B\u206F\u202C\u202E\u206B\u200B\u206E\u202E\u206E\u202C\u200D\u200F\u200D\u202C\u206D\u206E\u202C\u202E\u202E\u202B\u200E\u200E\u202D\u206D\u202E;

		// Token: 0x0400041A RID: 1050
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206D\u206E\u202B\u206A\u206A\u202D\u206E\u202D\u200E\u202E\u202A\u202D\u200E\u202C\u206F\u202C\u200E\u202C\u202A\u206F\u202E\u202B\u206F\u206D\u206E\u200E\u200C\u206C\u202D\u200D\u206D\u206B\u202E\u200E\u200D\u206A\u202E\u206F\u202B\u206E\u202E;

		// Token: 0x0400041B RID: 1051
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private List<OwnershipChange> \u206D\u202E\u206E\u206F\u200E\u200E\u206F\u200B\u200D\u200C\u206F\u206B\u202A\u200C\u206A\u202D\u206F\u206F\u206F\u206B\u206D\u206A\u200F\u202E\u202A\u200F\u206C\u202D\u206C\u206B\u200D\u202A\u206C\u200F\u200C\u202B\u206A\u200C\u200E\u206B\u202E;
	}
}
