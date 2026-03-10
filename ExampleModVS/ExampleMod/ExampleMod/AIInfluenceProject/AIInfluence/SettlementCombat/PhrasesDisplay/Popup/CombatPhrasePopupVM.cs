using System;
using System.Runtime.CompilerServices;
using TaleWorlds.Library;

namespace AIInfluence.SettlementCombat.PhrasesDisplay.Popup
{
	// Token: 0x02000161 RID: 353
	public class CombatPhrasePopupVM : ViewModel, IDisposable
	{
		// Token: 0x06000B3B RID: 2875 RVA: 0x000C8008 File Offset: 0x000C6208
		public CombatPhrasePopupVM()
		{
			this._targets = new MBBindingList<CombatPhrasePopupItemVM>();
		}

		// Token: 0x1700022A RID: 554
		// (get) Token: 0x06000B3C RID: 2876 RVA: 0x000C8028 File Offset: 0x000C6228
		// (set) Token: 0x06000B3D RID: 2877 RVA: 0x000C803C File Offset: 0x000C623C
		[DataSourceProperty]
		public MBBindingList<CombatPhrasePopupItemVM> Targets
		{
			get
			{
				return this._targets;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = value != this._targets;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -1516167890;
						for (;;)
						{
							int num2 = num;
							switch ((-num2 - -(1669630849 + 137508795) + 1770000636 * -983967999 ^ -729473277) % 3)
							{
							case 0:
								goto IL_11;
							case 2:
								this._targets = value;
								base.OnPropertyChangedWithValue<MBBindingList<CombatPhrasePopupItemVM>>(value, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1922322408));
								num = -1601589397;
								continue;
							}
							goto Block_1;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x06000B3E RID: 2878 RVA: 0x000C80BC File Offset: 0x000C62BC
		public void AddItem(CombatPhrasePopupItemVM item)
		{
			this._targets.Add(item);
		}

		// Token: 0x06000B3F RID: 2879 RVA: 0x000C80D8 File Offset: 0x000C62D8
		public void RemoveItem(CombatPhrasePopupItemVM item)
		{
			this._targets.Remove(item);
		}

		// Token: 0x06000B40 RID: 2880 RVA: 0x000C80F4 File Offset: 0x000C62F4
		public void Dispose()
		{
			MBBindingList<CombatPhrasePopupItemVM> targets = this._targets;
			if (targets != null)
			{
				targets.Clear();
			}
		}

		// Token: 0x040006E6 RID: 1766
		private MBBindingList<CombatPhrasePopupItemVM> _targets;
	}
}
