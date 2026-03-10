using System;
using System.Runtime.CompilerServices;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ViewModelCollection.Order.Visual;

namespace AIInfluence.SettlementCombat
{
	// Token: 0x02000158 RID: 344
	public class SimpleVisualOrderSet : VisualOrderSet
	{
		// Token: 0x17000215 RID: 533
		// (get) Token: 0x06000AEF RID: 2799 RVA: 0x0001DE4A File Offset: 0x0001C04A
		public override bool IsSoloOrder
		{
			get
			{
				return false;
			}
		}

		// Token: 0x17000216 RID: 534
		// (get) Token: 0x06000AF0 RID: 2800 RVA: 0x0001DE4D File Offset: 0x0001C04D
		public override string StringId
		{
			get
			{
				return this._stringId;
			}
		}

		// Token: 0x17000217 RID: 535
		// (get) Token: 0x06000AF1 RID: 2801 RVA: 0x0001DE55 File Offset: 0x0001C055
		public override string IconId
		{
			get
			{
				return this._iconId;
			}
		}

		// Token: 0x06000AF2 RID: 2802 RVA: 0x0001DE5D File Offset: 0x0001C05D
		public SimpleVisualOrderSet(string stringId, string iconId)
		{
			this._stringId = stringId;
			this._iconId = iconId;
		}

		// Token: 0x06000AF3 RID: 2803 RVA: 0x000C5AAC File Offset: 0x000C3CAC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override TextObject GetName(OrderController orderController)
		{
			return new TextObject(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1951436412) + this._stringId, null);
		}

		// Token: 0x040006BB RID: 1723
		private readonly string _stringId;

		// Token: 0x040006BC RID: 1724
		private readonly string _iconId;
	}
}
