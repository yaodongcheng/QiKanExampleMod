using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence.DynamicEvents
{
	// Token: 0x020001C0 RID: 448
	[JsonSerializable]
	public class ActiveEconomicEffect
	{
		// Token: 0x170002D4 RID: 724
		// (get) Token: 0x06000E46 RID: 3654 RVA: 0x000F1568 File Offset: 0x000EF768
		// (set) Token: 0x06000E47 RID: 3655 RVA: 0x000F157C File Offset: 0x000EF77C
		public string TargetType { get; set; }

		// Token: 0x170002D5 RID: 725
		// (get) Token: 0x06000E48 RID: 3656 RVA: 0x000F1590 File Offset: 0x000EF790
		// (set) Token: 0x06000E49 RID: 3657 RVA: 0x000F15A4 File Offset: 0x000EF7A4
		public string TargetId { get; set; }

		// Token: 0x170002D6 RID: 726
		// (get) Token: 0x06000E4A RID: 3658 RVA: 0x000F15B8 File Offset: 0x000EF7B8
		// (set) Token: 0x06000E4B RID: 3659 RVA: 0x000F15CC File Offset: 0x000EF7CC
		public float ProsperityDeltaPerDay { get; set; }

		// Token: 0x170002D7 RID: 727
		// (get) Token: 0x06000E4C RID: 3660 RVA: 0x000F15E0 File Offset: 0x000EF7E0
		// (set) Token: 0x06000E4D RID: 3661 RVA: 0x000F15F4 File Offset: 0x000EF7F4
		public float FoodDeltaPerDay { get; set; }

		// Token: 0x170002D8 RID: 728
		// (get) Token: 0x06000E4E RID: 3662 RVA: 0x000F1608 File Offset: 0x000EF808
		// (set) Token: 0x06000E4F RID: 3663 RVA: 0x000F161C File Offset: 0x000EF81C
		public float SecurityDeltaPerDay { get; set; }

		// Token: 0x170002D9 RID: 729
		// (get) Token: 0x06000E50 RID: 3664 RVA: 0x000F1630 File Offset: 0x000EF830
		// (set) Token: 0x06000E51 RID: 3665 RVA: 0x000F1644 File Offset: 0x000EF844
		public float LoyaltyDeltaPerDay { get; set; }

		// Token: 0x170002DA RID: 730
		// (get) Token: 0x06000E52 RID: 3666 RVA: 0x000F1658 File Offset: 0x000EF858
		// (set) Token: 0x06000E53 RID: 3667 RVA: 0x000F166C File Offset: 0x000EF86C
		public float IncomeMultiplier { get; set; } = 1f;

		// Token: 0x170002DB RID: 731
		// (get) Token: 0x06000E54 RID: 3668 RVA: 0x000F1680 File Offset: 0x000EF880
		// (set) Token: 0x06000E55 RID: 3669 RVA: 0x000F1694 File Offset: 0x000EF894
		public float StartDay { get; set; }

		// Token: 0x170002DC RID: 732
		// (get) Token: 0x06000E56 RID: 3670 RVA: 0x000F16A8 File Offset: 0x000EF8A8
		// (set) Token: 0x06000E57 RID: 3671 RVA: 0x000F16BC File Offset: 0x000EF8BC
		public int DurationDays { get; set; }

		// Token: 0x170002DD RID: 733
		// (get) Token: 0x06000E58 RID: 3672 RVA: 0x000F16D0 File Offset: 0x000EF8D0
		// (set) Token: 0x06000E59 RID: 3673 RVA: 0x000F16E4 File Offset: 0x000EF8E4
		public string Reason { get; set; }

		// Token: 0x06000E5A RID: 3674 RVA: 0x000F16F8 File Offset: 0x000EF8F8
		public int GetRemainingDays()
		{
			float num = (float)\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ().ToDays;
			int num2 = this.DurationDays - (int)(num - this.StartDay);
			return \u202B\u206E\u206D\u202E\u202C\u202B\u200F\u202D\u206B\u202A\u206C\u206D\u202D\u200F\u206D\u200B\u200D\u200C\u202B\u200C\u206A\u206E\u200F\u202D\u202B\u202C\u206F\u200B\u200B\u202E\u202C\u202C\u202B\u206C\u206C\u200B\u202C\u206B\u200D\u202A\u202E.xÙ(ã\u0013(0, num2);
		}

		// Token: 0x0400090F RID: 2319
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200B\u200C\u206C\u206F\u206D\u200F\u200E\u202D\u200D\u206C\u206E\u206E\u200C\u200E\u200F\u200D\u202A\u202A\u206D\u202C\u206A\u200C\u200E\u200F\u200E\u202D\u206C\u202E\u206F\u202D\u206C\u202E\u202E\u200D\u202B\u206F\u202D\u206E\u202E\u200C\u202E;

		// Token: 0x04000910 RID: 2320
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u200C\u200F\u202C\u202A\u200E\u200C\u206D\u202D\u200B\u202B\u206A\u200F\u202E\u206C\u202A\u206C\u200D\u202B\u200D\u202C\u200B\u202A\u202B\u206B\u206F\u200B\u206E\u206D\u206A\u202B\u206C\u206F\u200C\u202E\u202A\u202A\u206B\u206A\u200D\u206B\u202E;

		// Token: 0x04000911 RID: 2321
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u206B\u206B\u206F\u200B\u200E\u202C\u202A\u202C\u200E\u202B\u200B\u200B\u202A\u200F\u202A\u206A\u200E\u200E\u202E\u200B\u202D\u200D\u200C\u202B\u206C\u202E\u206F\u200B\u206A\u202B\u206E\u206F\u200F\u202D\u206A\u200C\u206A\u206C\u206F\u206A\u202E;

		// Token: 0x04000912 RID: 2322
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u200E\u202E\u200E\u200F\u206B\u200F\u200B\u200C\u202A\u206D\u202A\u206F\u206C\u206D\u200C\u206C\u202A\u200B\u206D\u202E\u206C\u202A\u200F\u200E\u206D\u202D\u206B\u206D\u200B\u206B\u202D\u202D\u202B\u206F\u206A\u206F\u202B\u206D\u200C\u206F\u202E;

		// Token: 0x04000913 RID: 2323
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u200D\u202C\u200E\u206B\u202B\u202C\u200E\u206B\u200D\u202E\u200C\u206E\u206E\u202B\u202E\u202E\u202E\u202A\u206C\u206B\u200E\u200C\u206B\u202A\u200F\u206D\u202E\u202D\u202B\u200F\u206E\u206D\u206A\u202B\u200D\u202C\u206F\u202A\u200B\u200D\u202E;

		// Token: 0x04000914 RID: 2324
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u202C\u200D\u206E\u202C\u200C\u206E\u200D\u200B\u202C\u206A\u206C\u200E\u206D\u206A\u206C\u206F\u200F\u206C\u202C\u206F\u206F\u202B\u202A\u200C\u202C\u202E\u200B\u200B\u202B\u200C\u200F\u200F\u200B\u200B\u206F\u206E\u202C\u202B\u200E\u202C\u202E;

		// Token: 0x04000915 RID: 2325
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u206E\u200F\u206C\u200C\u200C\u202C\u206F\u202D\u202E\u202B\u200D\u200F\u206F\u202C\u206C\u202C\u200C\u202A\u206E\u200B\u200E\u200F\u200C\u200E\u200E\u200C\u200B\u202E\u200D\u206B\u202D\u202D\u200C\u206C\u200C\u202E\u202B\u206A\u200E\u206B\u202E;

		// Token: 0x04000916 RID: 2326
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private float \u206F\u206C\u206E\u200F\u206A\u200D\u202A\u200F\u200E\u202C\u200D\u206C\u206C\u206D\u206D\u200C\u200F\u206C\u200E\u200C\u202B\u200B\u206D\u200B\u206B\u202A\u202B\u200D\u206A\u200E\u200F\u200C\u206F\u206A\u200C\u202B\u200B\u206F\u200C\u202E\u202E;

		// Token: 0x04000917 RID: 2327
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private int \u200F\u206E\u206F\u202A\u200F\u206E\u206F\u200C\u206A\u202B\u200D\u202B\u206C\u200C\u206C\u202A\u206F\u200E\u202A\u206F\u206B\u200D\u206C\u202B\u200C\u206B\u206D\u206F\u202E\u206C\u206B\u200D\u200D\u206F\u200E\u202B\u200D\u202B\u202C\u206E\u202E;

		// Token: 0x04000918 RID: 2328
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string \u202D\u202A\u200D\u200D\u206E\u202C\u206B\u200C\u206E\u202B\u202D\u202E\u206A\u206A\u200F\u206B\u200F\u206F\u200D\u206B\u200B\u202D\u206A\u206D\u200F\u202C\u206B\u206D\u202E\u200F\u206C\u206D\u202E\u202B\u202A\u200E\u200D\u206C\u202C\u200D\u202E;
	}
}
