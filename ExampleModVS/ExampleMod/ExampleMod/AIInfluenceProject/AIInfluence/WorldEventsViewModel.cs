using System;
using System.Runtime.CompilerServices;
using Bannerlord.UIExtenderEx.Attributes;
using SandBox.View.Map;
using TaleWorlds.Library;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence
{
	// Token: 0x020000E5 RID: 229
	public class WorldEventsViewModel : ViewModel
	{
		// Token: 0x06000761 RID: 1889 RVA: 0x0009BBB4 File Offset: 0x00099DB4
		[MethodImpl(MethodImplOptions.NoInlining)]
		public WorldEventsViewModel()
		{
			this.WorldEventsButtonText = \u206F\u200D\u202B\u206A\u200F\u200D\u202B\u200B\u206C\u206B\u202E\u206E\u200F\u200D\u200F\u206D\u200B\u200C\u202C\u206D\u202A\u206C\u200C\u200B\u202A\u202E\u200E\u202B\u202C\u202E\u202E\u200E\u206D\u202B\u206E\u206A\u200D\u202D\u206E\u200E\u202E.Â^\u0084\u0005ð(\u206C\u200E\u200F\u206D\u200F\u206B\u200F\u202B\u200D\u202A\u206D\u202A\u202B\u206C\u200C\u200B\u200C\u206E\u200C\u200F\u200D\u206A\u202E\u206F\u206A\u206B\u206C\u200E\u202D\u206C\u206F\u200E\u200C\u202A\u202A\u200E\u206B\u200C\u206D\u202C\u202E.\u00BE\u00A4\u00A0(\u00B1(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1576995837), null));
		}

		// Token: 0x17000189 RID: 393
		// (get) Token: 0x06000762 RID: 1890 RVA: 0x0009BBF4 File Offset: 0x00099DF4
		// (set) Token: 0x06000763 RID: 1891 RVA: 0x0009BC08 File Offset: 0x00099E08
		[DataSourceProperty]
		public string WorldEventsButtonText { get; set; }

		// Token: 0x06000764 RID: 1892 RVA: 0x0009BC1C File Offset: 0x00099E1C
		[DataSourceMethod]
		public void ExecuteOpenWorldEvents()
		{
			WorldEventsWindowLayer worldEventsWindowLayer = new WorldEventsWindowLayer();
			MapScreen mapScreen = \u200C\u200D\u200F\u202C\u200B\u206C\u202A\u200F\u202A\u206B\u202E\u200B\u202B\u206D\u206C\u206F\u202A\u202E\u200F\u200F\u206C\u202C\u202E\u206D\u206B\u200B\u202B\u202B\u200D\u206E\u202B\u202B\u206D\u202D\u200D\u206F\u206E\u200C\u206F\u200E\u202E.ï\u0006a\u009C2() as MapScreen;
			bool flag = mapScreen != null;
			if (flag)
			{
				for (;;)
				{
					IL_1F:
					int num = 1459049826;
					for (;;)
					{
						int num2 = num;
						uint num3;
						switch ((num3 = (uint)(-((num2 + ~(-908969551 ^ ~-732670903)) * 757976203) * 1353631495)) % 3U)
						{
						case 0U:
							goto IL_1F;
						case 2U:
							\u202A\u202B\u200D\u200F\u202C\u206D\u206E\u206A\u200D\u206F\u206C\u202D\u202C\u202B\u200C\u206D\u200B\u206C\u202E\u206A\u202E\u206B\u206E\u206B\u200E\u202E\u200E\u206D\u200C\u200B\u202A\u200F\u200B\u206A\u202A\u202B\u202B\u200C\u200B\u206D\u202E.p[\u0016=\u0087(mapScreen, worldEventsWindowLayer);
							num = (int)(num3 * 4264562162U ^ 2652654306U);
							continue;
						}
						goto Block_1;
					}
				}
				Block_1:;
			}
		}
	}
}
