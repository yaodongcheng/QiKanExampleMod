using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence
{
	// Token: 0x0200007D RID: 125
	[JsonSerializable]
	public class SettlementVisit
	{
		// Token: 0x17000154 RID: 340
		// (get) Token: 0x060004D0 RID: 1232 RVA: 0x0005C9D8 File Offset: 0x0005ABD8
		// (set) Token: 0x060004D1 RID: 1233 RVA: 0x0005C9EC File Offset: 0x0005ABEC
		[JsonProperty("SettlementId")]
		public string SettlementId { get; set; }

		// Token: 0x17000155 RID: 341
		// (get) Token: 0x060004D2 RID: 1234 RVA: 0x0005CA00 File Offset: 0x0005AC00
		// (set) Token: 0x060004D3 RID: 1235 RVA: 0x0005CA14 File Offset: 0x0005AC14
		[JsonProperty("SettlementName")]
		public string SettlementName { get; set; }

		// Token: 0x17000156 RID: 342
		// (get) Token: 0x060004D4 RID: 1236 RVA: 0x0005CA28 File Offset: 0x0005AC28
		// (set) Token: 0x060004D5 RID: 1237 RVA: 0x0005CA3C File Offset: 0x0005AC3C
		[JsonProperty("VisitTimeDays")]
		public double VisitTimeDays { get; set; }

		// Token: 0x17000157 RID: 343
		// (get) Token: 0x060004D6 RID: 1238 RVA: 0x0005CA50 File Offset: 0x0005AC50
		// (set) Token: 0x060004D7 RID: 1239 RVA: 0x0005CA88 File Offset: 0x0005AC88
		[JsonIgnore]
		public CampaignTime VisitTime
		{
			get
			{
				return (this.VisitTimeDays <= 0.0) ? \u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u200B\u200D\u200B\u202B\u202E\u200F\u200C\u206B\u206B\u202A\u200D\u200B\u206A\u202A\u202E\u202A\u202D\u206D\u200F\u206C\u206E\u202E\u202B\u206C\u206D\u200E\u206A\u200C\u200C\u200B\u200B\u200D\u202D\u202E\u206D\u200B\u200B\u202E\u206A\u206A\u202E() : \u200B\u202B\u202D\u202C\u206F\u206D\u200F\u206E\u200E\u206B\u202B\u206B\u200B\u206A\u206D\u206D\u200E\u202D\u206A\u206F\u202E\u206F\u206A\u202C\u206D\u202C\u200D\u206A\u200B\u202E\u206A\u202A\u206B\u202E\u206F\u206C\u206E\u200F\u202A\u200D\u202E.~\u000Dh\u0082ü((float)this.VisitTimeDays);
			}
			set
			{
				this.VisitTimeDays = (\u206F\u206C\u206F\u202C\u206A\u206A\u200F\u200F\u202E\u206B\u206C\u200E\u202D\u200C\u206A\u206A\u206D\u206C\u200D\u202A\u200C\u200F\u200E\u200D\u202B\u202C\u206F\u206C\u206E\u206F\u200F\u202E\u200B\u200E\u206F\u202C\u200F\u200C\u202B\u200E\u202E.s/\u008Ek.(value, \u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.ÂPèÍn()) ? 0.0 : value.ToDays);
			}
		}

		// Token: 0x060004D8 RID: 1240 RVA: 0x0005BFFC File Offset: 0x0005A1FC
		public SettlementVisit()
		{
		}

		// Token: 0x060004D9 RID: 1241 RVA: 0x0005CAC8 File Offset: 0x0005ACC8
		public SettlementVisit(string A_1, string A_2, CampaignTime A_3)
		{
			this.SettlementId = A_1;
			this.SettlementName = A_2;
			this.VisitTime = A_3;
		}

		// Token: 0x060004DA RID: 1242 RVA: 0x0005CAF8 File Offset: 0x0005ACF8
		public int GetDaysAgo()
		{
			bool flag = \u206F\u206C\u206F\u202C\u206A\u206A\u200F\u200F\u202E\u206B\u206C\u200E\u202D\u200C\u206A\u206A\u206D\u206C\u200D\u202A\u200C\u200F\u200E\u200D\u202B\u202C\u206F\u206C\u206E\u206F\u200F\u202E\u200B\u200E\u206F\u202C\u200F\u200C\u202B\u200E\u202E.s/\u008Ek.(this.VisitTime, \u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.ÂPèÍn());
			if (flag)
			{
				goto IL_1F;
			}
			goto IL_5D;
			int num2;
			int result;
			for (;;)
			{
				IL_24:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(~(~(-num)) ^ -1636414595)) % 4U)
				{
				case 0U:
					goto IL_5D;
				case 1U:
					result = -1;
					num2 = (int)(num3 * 2795940860U ^ 3452622426U);
					continue;
				case 2U:
					goto IL_1F;
				}
				break;
			}
			return result;
			IL_1F:
			num2 = 897268140;
			goto IL_24;
			IL_5D:
			result = (int)(\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u200D\u200D\u200C\u202E\u200B\u206F\u202C\u206B\u206A\u202A\u206D\u202E\u202A\u202B\u202B\u206F\u206D\u206E\u200B\u202C\u200D\u200F\u206C\u200C\u200C\u200D\u200F\u200C\u202E\u206A\u200B\u200B\u202C\u206E\u202A\u200C\u206B\u200B\u202E\u206B\u202E().ToDays - this.VisitTime.ToDays);
			num2 = 208240902;
			goto IL_24;
		}

		// Token: 0x040002CD RID: 717
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u206D\u200E\u200D\u202B\u200C\u200C\u206E\u200C\u206E\u200D\u200C\u206F\u200B\u202C\u202C\u202E\u200C\u206F\u206B\u202B\u202E\u200E\u206E\u202B\u200E\u200F\u202E\u202C\u202C\u206E\u202E\u206A\u202E\u206C\u202D\u200C\u206E\u200D\u202C\u206E\u202E;

		// Token: 0x040002CE RID: 718
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202A\u206A\u202B\u200D\u202B\u206C\u206C\u200B\u202D\u206B\u206F\u206C\u202B\u202B\u206E\u200E\u202D\u206C\u206B\u202D\u202E\u206D\u206C\u206B\u206A\u206D\u206A\u206F\u200E\u200C\u206C\u206E\u202D\u202C\u200E\u206A\u200D\u206C\u202C\u202A\u202E;

		// Token: 0x040002CF RID: 719
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private double \u202D\u206F\u200F\u202D\u200F\u206A\u206E\u206F\u202E\u206F\u206F\u206B\u202B\u206C\u206C\u202A\u200F\u200F\u202B\u200D\u200C\u200E\u202E\u200F\u200C\u200B\u206F\u206F\u202C\u206B\u200C\u206F\u206D\u202B\u200C\u206B\u202D\u206C\u200B\u200D\u202E;
	}
}
