using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence
{
	// Token: 0x02000056 RID: 86
	public static class ItemMentionParser
	{
		// Token: 0x060001EB RID: 491 RVA: 0x0004A1C8 File Offset: 0x000483C8
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static string GetMentionedItemsSummary(ItemRoster roster, IEnumerable<string> conversationHistory, int lastMessageCount = 6, bool isPlayerInventory = false, Hero contextHero = null)
		{
			if (roster != null)
			{
				goto IL_07;
			}
			bool flag = true;
			goto IL_14C;
			int num2;
			HashSet<ItemObject> mentionedItems;
			Settlement settlement2;
			for (;;)
			{
				IL_0C:
				int num = num2;
				uint num3;
				bool flag4;
				switch ((num3 = (uint)(-(uint)(-(uint)(-(-225811505 * 2145917185 ^ (792999245 ^ 839235973)) - num + (~-1653097258 - (-661848333 ^ 1838277679)))))) % 16U)
				{
				case 0U:
					goto IL_1DE;
				case 1U:
					if (!isPlayerInventory)
					{
						num2 = (int)(num3 * 2886451307U ^ 743050125U);
						continue;
					}
					goto IL_1EC;
				case 2U:
					mentionedItems = ItemMentionParser.GetMentionedItems(conversationHistory, lastMessageCount);
					num2 = ((mentionedItems.Count == 0) ? 585957759 : 40443950);
					continue;
				case 3U:
				{
					Settlement settlement;
					if ((settlement = \u200C\u206E\u202E\u206A\u200D\u200B\u200B\u206F\u206D\u202E\u200E\u200D\u202B\u206E\u202B\u200F\u206E\u202C\u202B\u202A\u200D\u206D\u200B\u202C\u206D\u206E\u206E\u200B\u200F\u202E\u200D\u206E\u200D\u206C\u202B\u202C\u200B\u200C\u202B\u206F\u202E.\u00A1jéë\u0015(\u202E\u200D\u202C\u200C\u206E\u202C\u200F\u200D\u200D\u202B\u206E\u206F\u200D\u200E\u202C\u202B\u206E\u206D\u206E\u200D\u200C\u200B\u206F\u200D\u206D\u200D\u206F\u200E\u206C\u202E\u202E\u206C\u200F\u200D\u200E\u202D\u206F\u202C\u200B\u200E\u202E.\u001D\u00ACvÉ\u0089())) == null)
					{
						MobileParty mobileParty = \u202A\u206A\u206E\u200D\u206E\u206C\u206A\u200B\u202B\u202A\u202B\u200F\u200E\u202B\u202D\u202C\u206C\u200C\u206E\u200C\u202D\u206C\u206B\u202A\u206D\u200E\u206B\u200B\u206D\u206B\u206B\u202C\u202A\u200E\u200D\u206D\u200E\u206C\u206C\u200E\u202E.\u001Fúæ}Ç(\u202E\u200D\u202C\u200C\u206E\u202C\u200F\u200D\u200D\u202B\u206E\u206F\u200D\u200E\u202C\u202B\u206E\u206D\u206E\u200D\u200C\u200B\u206F\u200D\u206D\u200D\u206F\u200E\u206C\u202E\u202E\u206C\u200F\u200D\u200E\u202D\u206F\u202C\u200B\u200E\u202E.\u001D\u00ACvÉ\u0089());
						settlement = ((mobileParty != null) ? \u206D\u206E\u206E\u206B\u202B\u200C\u206F\u202A\u202A\u200D\u206B\u206F\u206B\u202B\u200F\u206D\u206F\u202C\u206B\u206A\u200F\u200F\u202C\u202D\u202D\u200D\u200C\u200E\u200F\u206D\u206E\u202C\u202D\u206E\u206F\u202E\u200E\u202C\u206C\u206E\u202E.\u202A\u200E\u206A\u206D\u206B\u200C\u206A\u206C\u206E\u200B\u200F\u200B\u206D\u200E\u202C\u206F\u202D\u200E\u200D\u206D\u202C\u200D\u206A\u206C\u200D\u202B\u202E\u202D\u206B\u206B\u200D\u200B\u206C\u200F\u200C\u206B\u202D\u202E\u206B\u206A\u202E(mobileParty) : null);
					}
					settlement2 = settlement;
					num2 = -1349206790;
					continue;
				}
				case 4U:
				{
					bool flag2 = \u202E\u200D\u202C\u200C\u206E\u202C\u200F\u200D\u200D\u202B\u206E\u206F\u200D\u200E\u202C\u202B\u206E\u206D\u206E\u200D\u200C\u200B\u206F\u200D\u206D\u200D\u206F\u200E\u206C\u202E\u202E\u206C\u200F\u200D\u200E\u202D\u206F\u202C\u200B\u200E\u202E.\u206F\u206C\u200D\u202A\u206D\u206B\u202C\u206A\u206C\u200C\u200F\u200F\u202B\u200D\u202A\u202E\u206C\u202D\u200E\u206F\u206D\u200D\u202B\u200E\u202C\u200F\u202A\u206B\u202E\u206A\u206B\u206E\u206D\u202D\u200F\u206D\u206F\u206A\u202A\u202B\u202E() != null;
					num2 = ((!flag2) ? -1349206790 : -670172813);
					continue;
				}
				case 5U:
					goto IL_166;
				case 6U:
					goto IL_191;
				case 7U:
					if (!isPlayerInventory)
					{
						num2 = (int)(num3 * 2080267137U ^ 1796162710U);
						continue;
					}
					goto IL_174;
				case 8U:
				{
					settlement2 = null;
					bool flag3 = contextHero != null;
					num2 = ((!flag3) ? 106872402 : -1110297572);
					continue;
				}
				case 9U:
					if (conversationHistory != null)
					{
						num2 = 95214313;
						continue;
					}
					flag4 = true;
					goto IL_E9;
				case 10U:
				{
					Settlement settlement3;
					if ((settlement3 = \u200C\u206E\u202E\u206A\u200D\u200B\u200B\u206F\u206D\u202E\u200E\u200D\u202B\u206E\u202B\u200F\u206E\u202C\u202B\u202A\u200D\u206D\u200B\u202C\u206D\u206E\u206E\u200B\u200F\u202E\u200D\u206E\u200D\u206C\u202B\u202C\u200B\u200C\u202B\u206F\u202E.\u00A1jéë\u0015(contextHero)) == null)
					{
						MobileParty mobileParty2 = \u202A\u206A\u206E\u200D\u206E\u206C\u206A\u200B\u202B\u202A\u202B\u200F\u200E\u202B\u202D\u202C\u206C\u200C\u206E\u200C\u202D\u206C\u206B\u202A\u206D\u200E\u206B\u200B\u206D\u206B\u206B\u202C\u202A\u200E\u200D\u206D\u200E\u206C\u206C\u200E\u202E.\u001Fúæ}Ç(contextHero);
						settlement3 = ((mobileParty2 != null) ? \u206D\u206E\u206E\u206B\u202B\u200C\u206F\u202A\u202A\u200D\u206B\u206F\u206B\u202B\u200F\u206D\u206F\u202C\u206B\u206A\u200F\u200F\u202C\u202D\u202D\u200D\u200C\u200E\u200F\u206D\u206E\u202C\u202D\u206E\u206F\u202E\u200E\u202C\u206C\u206E\u202E.\u202A\u200E\u206A\u206D\u206B\u200C\u206A\u206C\u206E\u200B\u200F\u200B\u206D\u200E\u202C\u206F\u202D\u200E\u200D\u206D\u202C\u200D\u206A\u206C\u200D\u202B\u202E\u202D\u206B\u206B\u200D\u200B\u206C\u200F\u200C\u206B\u202D\u202E\u206B\u206A\u202E(mobileParty2) : null);
					}
					settlement2 = settlement3;
					num2 = -1349206790;
					continue;
				}
				case 11U:
					goto IL_07;
				case 13U:
					flag4 = !Enumerable.Any<string>(conversationHistory);
					goto IL_E9;
				case 14U:
					goto IL_13B;
				case 15U:
					if (!isPlayerInventory)
					{
						num2 = (int)(num3 * 2535775457U ^ 710068735U);
						continue;
					}
					goto IL_19F;
				}
				break;
				IL_E9:
				bool flag5 = flag4;
				num2 = ((!flag5) ? -329908972 : -1376454043);
			}
			goto IL_2A3;
			IL_13B:
			flag = (\u202B\u202B\u202E\u200D\u202A\u200C\u202D\u200E\u206B\u202E\u206C\u202E\u200B\u206E\u200E\u200D\u200F\u206A\u202A\u200D\u206A\u200F\u202C\u206E\u206E\u206F\u200F\u206C\u202A\u200D\u206E\u200B\u200E\u206E\u206C\u206D\u206C\u200F\u206A\u206A\u202E.ZMJÜ\u0084(roster) == 0);
			goto IL_14C;
			IL_166:
			string result = <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1040421428);
			goto IL_180;
			IL_174:
			result = <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(510516465);
			IL_180:
			return result;
			IL_191:
			string result2 = <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1040421428);
			goto IL_1AB;
			IL_19F:
			result2 = <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-525821880);
			IL_1AB:
			return result2;
			IL_1DE:
			string result3 = <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-363894626);
			goto IL_1F8;
			IL_1EC:
			result3 = <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-339452533);
			IL_1F8:
			return result3;
			IL_2A3:
			StringBuilder stringBuilder = \u202C\u206E\u200E\u202D\u202E\u200F\u202C\u202C\u206F\u200C\u206D\u206D\u202E\u202A\u202D\u202A\u206E\u200D\u202E\u200E\u202C\u202A\u206A\u200C\u202A\u206E\u206C\u202A\u202C\u202E\u206B\u206A\u206F\u200B\u202D\u200D\u202E\u202D\u206E\u202C\u202E.\u200D\u202D\u206B\u202B\u202E\u202A\u200B\u206E\u206C\u202B\u206E\u202E\u206D\u206F\u202D\u206A\u200B\u206F\u202B\u200B\u206E\u206A\u206C\u206A\u202D\u206F\u206D\u202E\u206C\u202B\u206C\u200E\u202C\u206D\u206A\u200E\u206C\u206A\u200B\u202D\u202E();
			List<string> list = new List<string>();
			List<string> list2 = new List<string>();
			List<string> list3 = new List<string>();
			List<string> list4 = new List<string>();
			IEnumerator<ItemRosterElement> enumerator = \u202A\u202C\u206A\u202E\u200F\u200F\u202B\u200C\u202E\u206D\u206F\u202A\u202B\u202B\u206E\u202D\u206B\u202C\u200B\u206C\u202A\u200F\u202E\u200F\u206B\u202C\u206B\u200D\u206B\u206F\u202C\u202E\u200F\u202A\u202B\u200E\u202B\u202E\u200D\u202B\u202E.OV\u00A7\u00B2Ç(roster);
			try
			{
				for (;;)
				{
					IL_7C2:
					ItemRosterElement itemRosterElement;
					ItemObject item;
					int num5;
					bool flag7;
					if (\u200C\u200D\u206F\u200E\u202B\u206E\u206A\u200E\u202E\u202A\u206F\u202C\u206F\u206F\u200D\u200B\u206E\u206F\u202D\u202C\u206B\u202C\u202E\u206F\u200E\u200B\u206D\u200C\u200E\u200B\u202C\u206F\u206D\u206A\u200D\u206D\u206D\u202E\u206B\u202B\u202E.f\u00A5úÎ~(enumerator))
					{
						for (;;)
						{
							IL_3B3:
							itemRosterElement = enumerator.Current;
							bool flag6 = itemRosterElement.Amount <= 0;
							int num4 = (!flag6) ? -558853199 : -104743114;
							for (;;)
							{
								int num = num4;
								switch (-(-(-(-225811505 * 2145917185 ^ (792999245 ^ 839235973)) - num + (~-1653097258 - (-661848333 ^ 1838277679)))) % 9)
								{
								case 0:
									num4 = -740902635;
									continue;
								case 2:
								{
									num5 = \u206E\u206E\u200E\u200C\u200C\u200F\u202B\u202C\u200D\u206C\u202E\u202D\u200F\u206D\u200E\u200B\u200B\u200B\u200E\u202C\u202D\u200E\u200B\u200F\u200F\u206A\u200E\u202D\u202D\u206D\u206E\u200F\u202B\u200F\u200F\u206F\u206A\u206C\u206E\u202D\u202E.\u008A\u0090q>Ý(item);
									flag7 = false;
									bool flag8 = settlement2 != null;
									if (flag8)
									{
										num4 = 543320160;
										continue;
									}
									goto IL_7B1;
								}
								case 3:
								{
									bool flag9 = !mentionedItems.Contains(item);
									num4 = ((!flag9) ? 3498818 : 343018844);
									continue;
								}
								case 4:
									goto IL_421;
								case 5:
									goto IL_39B;
								case 6:
									item = itemRosterElement.EquipmentElement.Item;
									num4 = ((item == null) ? 191216532 : -1392219446);
									continue;
								case 7:
									goto IL_3B3;
								case 8:
									goto IL_409;
								}
								goto Block_24;
							}
						}
						IL_39B:
						IL_409:
						IL_421:
						continue;
						IL_439:
						try
						{
							if (\u206C\u202B\u206E\u206D\u202D\u206A\u202A\u202C\u206A\u200B\u202C\u200B\u202E\u202C\u206F\u200D\u206A\u206E\u206D\u206C\u200C\u200E\u206F\u206F\u202B\u206A\u206F\u200B\u206D\u200B\u202B\u200B\u200E\u202C\u206D\u200B\u200D\u202C\u202E\u206E\u202E.Óß;\u0004å(settlement2))
							{
								goto IL_44B;
							}
							bool flag10 = false;
							goto IL_4FE;
							int num6;
							for (;;)
							{
								IL_450:
								int num = num6;
								uint num3;
								bool flag11;
								switch ((num3 = (uint)(-(uint)(-(uint)(-(-225811505 * 2145917185 ^ (792999245 ^ 839235973)) - num + (~-1653097258 - (-661848333 ^ 1838277679)))))) % 7U)
								{
								case 0U:
									num5 = \u200F\u200C\u202E\u200E\u200E\u206E\u200B\u200F\u202E\u202B\u200E\u202C\u200C\u202E\u200F\u202D\u200D\u200B\u200E\u206B\u206C\u200D\u206F\u206C\u202C\u202E\u206D\u206B\u202D\u202C\u202A\u202C\u206D\u202E\u206B\u200E\u202B\u206F\u202A\u200F\u202E.o\u008D\u007F\u00B7û(settlement2.Village, item, null, false);
									flag7 = true;
									num6 = (int)(num3 * 3037451097U ^ 2663606149U);
									continue;
								case 1U:
									goto IL_4F2;
								case 3U:
									goto IL_44B;
								case 4U:
									if (\u206C\u202B\u206E\u206D\u202D\u206A\u202A\u202C\u206A\u200B\u202C\u200B\u202E\u202C\u206F\u200D\u206A\u206E\u206D\u206C\u200C\u200E\u206F\u206F\u202B\u206A\u206F\u200B\u206D\u200B\u202B\u200B\u200E\u202C\u206D\u200B\u200D\u202C\u202E\u206E\u202E.[{\u0083\u0010_(settlement2))
									{
										num6 = 421712892;
										continue;
									}
									flag11 = false;
									goto IL_552;
								case 5U:
									flag11 = (settlement2.Village != null);
									goto IL_552;
								case 6U:
									num5 = \u200F\u200C\u202E\u200E\u200E\u206E\u200B\u200F\u202E\u202B\u200E\u202C\u200C\u202E\u200F\u202D\u200D\u200B\u200E\u206B\u206C\u200D\u206F\u206C\u202C\u202E\u206D\u206B\u202D\u202C\u202A\u202C\u206D\u202E\u206B\u200E\u202B\u206F\u202A\u200F\u202E.o\u008D\u007F\u00B7û(settlement2.Town, item, null, false);
									flag7 = true;
									num6 = (int)(num3 * 936546754U ^ 3821797623U);
									continue;
								}
								break;
								IL_552:
								bool flag12 = flag11;
								num6 = ((!flag12) ? -935779311 : -142851950);
							}
							goto IL_56C;
							IL_4F2:
							flag10 = (settlement2.Town != null);
							goto IL_4FE;
							IL_56C:
							goto IL_582;
							IL_44B:
							num6 = -612729478;
							goto IL_450;
							IL_4FE:
							num6 = (flag10 ? -500404109 : 509710278);
							goto IL_450;
						}
						catch
						{
							num5 = \u206E\u206E\u200E\u200C\u200C\u200F\u202B\u202C\u200D\u206C\u202E\u202D\u200F\u206D\u200E\u200B\u200B\u200B\u200E\u202C\u202D\u200E\u200B\u200F\u200F\u206A\u200E\u202D\u202D\u206D\u206E\u200F\u202B\u200F\u200F\u206F\u206A\u206C\u206E\u202D\u202E.\u008A\u0090q>Ý(item);
						}
						IL_582:
						goto IL_583;
						Block_24:
						goto IL_439;
					}
					int num7 = 11770173;
					string item2;
					for (;;)
					{
						IL_588:
						int num = num7;
						uint num3;
						bool flag13;
						switch ((num3 = (uint)(-(uint)(-(uint)(-(-225811505 * 2145917185 ^ (792999245 ^ 839235973)) - num + (~-1653097258 - (-661848333 ^ 1838277679)))))) % 16U)
						{
						case 0U:
							list3.Add(item2);
							num7 = (int)(num3 * 1244427612U ^ 1468159955U);
							continue;
						case 1U:
							goto IL_669;
						case 2U:
							num7 = 105876627;
							continue;
						case 3U:
							num7 = -308041831;
							continue;
						case 4U:
							goto IL_7B1;
						case 5U:
							list4.Add(item2);
							num7 = 105876627;
							continue;
						case 6U:
							num7 = (int)((\u206F\u200E\u202D\u206F\u206E\u202A\u200D\u200B\u200D\u206A\u202E\u200C\u200B\u200D\u206A\u206C\u200B\u202A\u206A\u206E\u206A\u206D\u206F\u200C\u200B\u200C\u206F\u202D\u206A\u206F\u200F\u202E\u200B\u200E\u202B\u206D\u200B\u200D\u202C\u200F\u202E.Êå8\u00A8â(item) ? 1437570920U : 1863974714U) ^ num3 * 3241747411U);
							continue;
						case 7U:
							if (!\u206F\u200E\u202D\u206F\u206E\u202A\u200D\u200B\u200D\u206A\u202E\u200C\u200B\u200D\u206A\u206C\u200B\u202A\u206A\u206E\u206A\u206D\u206F\u200C\u200B\u200C\u206F\u202D\u206A\u206F\u200F\u202E\u200B\u200E\u202B\u206D\u200B\u200D\u202C\u200F\u202E.3^\u00A4hî(item))
							{
								num7 = -1084326884;
								continue;
							}
							flag13 = true;
							goto IL_74A;
						case 8U:
						{
							bool flag14 = \u206F\u200E\u202D\u206F\u206E\u202A\u200D\u200B\u200D\u206A\u202E\u200C\u200B\u200D\u206A\u206C\u200B\u202A\u206A\u206E\u206A\u206D\u206F\u200C\u200B\u200C\u206F\u202D\u206A\u206F\u200F\u202E\u200B\u200E\u202B\u206D\u200B\u200D\u202C\u200F\u202E.Êå8\u00A8â(item);
							num7 = ((!flag14) ? -1264023791 : -1332091754);
							continue;
						}
						case 10U:
							flag13 = \u206F\u200E\u202D\u206F\u206E\u202A\u200D\u200B\u200D\u206A\u202E\u200C\u200B\u200D\u206A\u206C\u200B\u202A\u206A\u206E\u206A\u206D\u206F\u200C\u200B\u200C\u206F\u202D\u206A\u206F\u200F\u202E\u200B\u200E\u202B\u206D\u200B\u200D\u202C\u200F\u202E.ùç5e\u00B8(item);
							goto IL_74A;
						case 11U:
							goto IL_583;
						case 12U:
							list3.Add(item2);
							num7 = (int)(num3 * 3291770245U ^ 3938688024U);
							continue;
						case 13U:
							goto IL_7C2;
						case 14U:
							list2.Add(item2);
							num7 = 360135988;
							continue;
						case 15U:
							list.Add(item2);
							num7 = (int)(num3 * 1271842915U ^ 2541180670U);
							continue;
						}
						goto Block_30;
						IL_74A:
						bool flag15 = flag13;
						num7 = ((!flag15) ? 56865838 : -1510898064);
					}
					IL_669:
					<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-530232307);
					goto IL_683;
					IL_583:
					num7 = -1275011758;
					goto IL_588;
					IL_683:
					item2 = \u202D\u200E\u206D\u206A\u200C\u200C\u206C\u202B\u206F\u202E\u202D\u202B\u206A\u200D\u206D\u206E\u202A\u206B\u202D\u200E\u206F\u206E\u200C\u206D\u202D\u206F\u200F\u206E\u206C\u202B\u206E\u206F\u206B\u202B\u202A\u206F\u206F\u200C\u200B\u202C\u202E.\u200C\u206A\u202D\u202E\u200B\u202C\u202D\u202D\u206F\u202A\u206D\u202D\u200C\u202A\u202A\u202A\u202B\u200D\u206B\u202D\u202C\u206C\u202D\u202D\u206B\u200F\u202E\u200D\u200E\u206A\u206F\u202D\u206A\u206A\u206B\u200E\u202A\u206F\u200F\u206B\u202E(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1481943263), new object[]
					{
						\u202A\u200D\u200C\u202E\u200D\u206D\u200B\u200C\u202E\u202B\u206E\u202D\u202C\u202E\u206E\u206B\u202B\u202B\u202D\u202C\u206C\u206D\u202A\u202A\u206C\u206F\u202D\u202E\u206E\u200F\u202B\u206B\u200D\u202B\u200F\u206B\u202C\u200D\u206B\u202B\u202E.aëôÄì(item),
						\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(item),
						itemRosterElement.Amount,
						num5
					});
					num7 = (\u206F\u200E\u202D\u206F\u206E\u202A\u200D\u200B\u200D\u206A\u202E\u200C\u200B\u200D\u206A\u206C\u200B\u202A\u206A\u206E\u206A\u206D\u206F\u200C\u200B\u200C\u206F\u202D\u206A\u206F\u200F\u202E\u200B\u200E\u202B\u206D\u200B\u200D\u202C\u200F\u202E.nË\u0083\u00BB|(item) ? 107089751 : -1176259729);
					goto IL_588;
					IL_7B1:
					if (!flag7)
					{
						num7 = 357227173;
						goto IL_588;
					}
					<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(2000149896);
					goto IL_683;
				}
				Block_30:;
			}
			finally
			{
				if (enumerator != null)
				{
					for (;;)
					{
						IL_802:
						int num8 = -1447989892;
						for (;;)
						{
							int num = num8;
							uint num3;
							switch ((num3 = (uint)(-(uint)(-(uint)(-(-225811505 * 2145917185 ^ (792999245 ^ 839235973)) - num + (~-1653097258 - (-661848333 ^ 1838277679)))))) % 3U)
							{
							case 0U:
								goto IL_802;
							case 2U:
								\u200E\u206D\u202C\u200F\u202A\u206C\u202C\u206E\u206D\u206F\u206A\u200D\u206D\u206E\u206C\u202E\u206D\u206D\u200E\u206A\u200D\u206B\u206E\u206B\u206D\u206C\u206F\u206F\u206C\u200D\u206C\u206C\u202D\u206F\u200F\u206A\u206C\u202C\u206D\u206A\u202E.ôgÍ@\u000E(enumerator);
								num8 = (int)(num3 * 157297658U ^ 3654896547U);
								continue;
							}
							goto Block_45;
						}
					}
					Block_45:;
				}
			}
			bool flag16 = Enumerable.Any<string>(list);
			if (flag16)
			{
				goto IL_87E;
			}
			goto IL_925;
			int num9;
			string result4;
			for (;;)
			{
				IL_883:
				int num = num9;
				uint num3;
				bool flag17;
				string text;
				switch ((num3 = (uint)(-(uint)(-(uint)(-(-225811505 * 2145917185 ^ (792999245 ^ 839235973)) - num + (~-1653097258 - (-661848333 ^ 1838277679)))))) % 14U)
				{
				case 0U:
					goto IL_87E;
				case 1U:
					\u206D\u206C\u206B\u206E\u206B\u206A\u200F\u202B\u200C\u200F\u202A\u200C\u200F\u202D\u202B\u206C\u206A\u206D\u200B\u200C\u206C\u202C\u200D\u200E\u202A\u206E\u202A\u200C\u200D\u200C\u206A\u202C\u200B\u206E\u202B\u200C\u202B\u206E\u202B\u202E.w\u00B7\u000Cæ\u00B0(stringBuilder, \u206F\u206F\u206B\u202A\u200E\u200D\u206E\u206E\u206F\u206F\u202D\u206B\u206B\u200B\u202E\u206B\u206F\u202E\u202A\u202D\u200E\u200C\u206C\u200C\u200E\u200E\u200C\u200B\u200D\u206B\u202B\u206B\u200B\u206C\u206E\u200B\u206F\u200D\u202E._fØ\u00BBÜ(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(380194381), \u200C\u206C\u202C\u202A\u200C\u200C\u202B\u202C\u200E\u202A\u202A\u200E\u202E\u206F\u206B\u200F\u206D\u206D\u206B\u200F\u200D\u200D\u206D\u206D\u202C\u200E\u202A\u200C\u200C\u202A\u206F\u206E\u200E\u206D\u206B\u202A\u200B\u200D\u200E\u206D\u202E.Ùc\u0014\u0010q(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-210563508), list4)));
					num9 = -129498019;
					continue;
				case 2U:
					flag17 = (list4.Count < 10);
					goto IL_90B;
				case 3U:
					goto IL_925;
				case 5U:
					\u206D\u206C\u206B\u206E\u206B\u206A\u200F\u202B\u200C\u200F\u202A\u200C\u200F\u202D\u202B\u206C\u206A\u206D\u200B\u200C\u206C\u202C\u200D\u200E\u202A\u206E\u202A\u200C\u200D\u200C\u206A\u202C\u200B\u206E\u202B\u200C\u202B\u206E\u202B\u202E.w\u00B7\u000Cæ\u00B0(stringBuilder, \u206F\u206F\u206B\u202A\u200E\u200D\u206E\u206E\u206F\u206F\u202D\u206B\u206B\u200B\u202E\u206B\u206F\u202E\u202A\u202D\u200E\u200C\u206C\u200C\u200E\u200E\u200C\u200B\u200D\u206B\u202B\u206B\u200B\u206C\u206E\u200B\u206F\u200D\u202E._fØ\u00BBÜ(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1297647892), \u200C\u206C\u202C\u202A\u200C\u200C\u202B\u202C\u200E\u202A\u202A\u200E\u202E\u206F\u206B\u200F\u206D\u206D\u206B\u200F\u200D\u200D\u206D\u206D\u202C\u200E\u202A\u200C\u200C\u202A\u206F\u206E\u200E\u206D\u206B\u202A\u200B\u200D\u200E\u206D\u202E.Ùc\u0014\u0010q(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-234543546), list3)));
					num9 = -1492601744;
					continue;
				case 6U:
				{
					bool flag18 = Enumerable.Any<string>(list3);
					num9 = ((!flag18) ? -1492601744 : -835233267);
					continue;
				}
				case 7U:
					text = <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1960115933);
					goto IL_973;
				case 8U:
					if (Enumerable.Any<string>(list4))
					{
						num9 = -1145949474;
						continue;
					}
					flag17 = false;
					goto IL_90B;
				case 9U:
					if (!isPlayerInventory)
					{
						num9 = (int)(num3 * 3638044450U ^ 1208411081U);
						continue;
					}
					text = <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1794626555);
					goto IL_973;
				case 10U:
					\u206D\u206C\u206B\u206E\u206B\u206A\u200F\u202B\u200C\u200F\u202A\u200C\u200F\u202D\u202B\u206C\u206A\u206D\u200B\u200C\u206C\u202C\u200D\u200E\u202A\u206E\u202A\u200C\u200D\u200C\u206A\u202C\u200B\u206E\u202B\u200C\u202B\u206E\u202B\u202E.w\u00B7\u000Cæ\u00B0(stringBuilder, \u206F\u206F\u206B\u202A\u200E\u200D\u206E\u206E\u206F\u206F\u202D\u206B\u206B\u200B\u202E\u206B\u206F\u202E\u202A\u202D\u200E\u200C\u206C\u200C\u200E\u200E\u200C\u200B\u200D\u206B\u202B\u206B\u200B\u206C\u206E\u200B\u206F\u200D\u202E._fØ\u00BBÜ(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-2053432706), \u200C\u206C\u202C\u202A\u200C\u200C\u202B\u202C\u200E\u202A\u202A\u200E\u202E\u206F\u206B\u200F\u206D\u206D\u206B\u200F\u200D\u200D\u206D\u206D\u202C\u200E\u202A\u200C\u200C\u202A\u206F\u206E\u200E\u206D\u206B\u202A\u200B\u200D\u200E\u206D\u202E.Ùc\u0014\u0010q(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-234543546), list2)));
					num9 = 632120382;
					continue;
				case 11U:
				{
					bool flag19 = \u202D\u200F\u200E\u200C\u200E\u206E\u200F\u200D\u200C\u206C\u206F\u206B\u206B\u202C\u200E\u200E\u206C\u202C\u202C\u200E\u206B\u206A\u202E\u206B\u206F\u200C\u202B\u202A\u202E\u202E\u206A\u200D\u202E\u200F\u206A\u200B\u200B\u202C\u206E\u200C\u202E.\u008A\u0007S\u008BÂ(stringBuilder) == 0;
					num9 = ((!flag19) ? -1174891671 : 281805253);
					continue;
				}
				case 12U:
					\u206D\u206C\u206B\u206E\u206B\u206A\u200F\u202B\u200C\u200F\u202A\u200C\u200F\u202D\u202B\u206C\u206A\u206D\u200B\u200C\u206C\u202C\u200D\u200E\u202A\u206E\u202A\u200C\u200D\u200C\u206A\u202C\u200B\u206E\u202B\u200C\u202B\u206E\u202B\u202E.w\u00B7\u000Cæ\u00B0(stringBuilder, \u206F\u206F\u206B\u202A\u200E\u200D\u206E\u206E\u206F\u206F\u202D\u206B\u206B\u200B\u202E\u206B\u206F\u202E\u202A\u202D\u200E\u200C\u206C\u200C\u200E\u200E\u200C\u200B\u200D\u206B\u202B\u206B\u200B\u206C\u206E\u200B\u206F\u200D\u202E._fØ\u00BBÜ(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(417384412), \u200C\u206C\u202C\u202A\u200C\u200C\u202B\u202C\u200E\u202A\u202A\u200E\u202E\u206F\u206B\u200F\u206D\u206D\u206B\u200F\u200D\u200D\u206D\u206D\u202C\u200E\u202A\u200C\u200C\u202A\u206F\u206E\u200E\u206D\u206B\u202A\u200B\u200D\u200E\u206D\u202E.Ùc\u0014\u0010q(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-210563508), list)));
					num9 = -160755503;
					continue;
				case 13U:
					result4 = \u206C\u206B\u200B\u206E\u200C\u206F\u206C\u206A\u200B\u206C\u200B\u200F\u202D\u200D\u206B\u200E\u202B\u200B\u206B\u206B\u202B\u200F\u202C\u200F\u200B\u202B\u202C\u206B\u202E\u206C\u200F\u202A\u202E\u200C\u202D\u200D\u202D\u200E\u202D\u202D\u202E.sþELª(\u206F\u200D\u202B\u206A\u200F\u200D\u202B\u200B\u206C\u206B\u202E\u206E\u200F\u200D\u200F\u206D\u200B\u200C\u202C\u206D\u202A\u206C\u200C\u200B\u202A\u202E\u200E\u202B\u202C\u202E\u202E\u200E\u206D\u202B\u206E\u206A\u200D\u202D\u206E\u200E\u202E.Â^\u0084\u0005ð(stringBuilder), Array.Empty<char>());
					num9 = 307709542;
					continue;
				}
				break;
				IL_90B:
				bool flag20 = flag17;
				num9 = ((!flag20) ? -129498019 : 218532821);
				continue;
				IL_973:
				result4 = text;
				num9 = 307709542;
			}
			return result4;
			IL_87E:
			num9 = -811514292;
			goto IL_883;
			IL_925:
			num9 = (Enumerable.Any<string>(list2) ? -1138046762 : 632120382);
			goto IL_883;
			IL_07:
			num2 = 449528792;
			goto IL_0C;
			IL_14C:
			bool flag21 = flag;
			num2 = ((!flag21) ? 315843725 : -1195712297);
			goto IL_0C;
		}

		// Token: 0x060001EC RID: 492 RVA: 0x0004AD2C File Offset: 0x00048F2C
		public static HashSet<ItemObject> GetMentionedItems(IEnumerable<string> conversationHistory, int lastMessageCount = 6)
		{
			HashSet<ItemObject> hashSet = new HashSet<ItemObject>();
			bool flag = conversationHistory == null;
			if (flag)
			{
				goto IL_12;
			}
			goto IL_A0;
			int num2;
			List<string> list;
			HashSet<ItemObject> result;
			for (;;)
			{
				IL_17:
				int num = num2;
				int num4;
				switch (((num ^ (1869927147 ^ ~(--1172247585))) - 1187015823 * (-331235956 + -1598445209) + -473895489 * 2019372939) * -235921567 % 10)
				{
				case 0:
					goto IL_A0;
				case 1:
				{
					ItemMentionParser.\u206B\u200F\u206E\u206F\u200D\u200F\u206C\u206C\u202E\u202D\u206D\u202C\u206F\u206B\u202B\u200E\u206B\u200C\u200E\u202E\u200D\u202B\u206C\u200E\u202A\u200E\u200F\u200E\u206B\u202C\u202C\u200B\u202A\u206E\u202B\u202E\u200B\u202B\u206A\u200D\u202E();
					int num3 = \u202B\u206E\u206D\u202E\u202C\u202B\u200F\u202D\u206B\u202A\u206C\u206D\u202D\u200F\u206D\u200B\u200D\u200C\u202B\u200C\u206A\u206E\u200F\u202D\u202B\u202C\u206F\u200B\u200B\u202E\u202C\u202C\u202B\u206C\u206C\u200B\u202C\u206B\u200D\u202A\u202E.xÙ(ã\u0013(0, list.Count - lastMessageCount);
					num4 = num3;
					goto IL_362;
				}
				case 2:
					goto IL_EE;
				case 3:
				{
					IL_109:
					string text = ItemMentionParser.\u200F\u206B\u200E\u202A\u206F\u202C\u200B\u202C\u206E\u202B\u206E\u200F\u200F\u206D\u200B\u202A\u202B\u206E\u200F\u202D\u202B\u206A\u206D\u206F\u202E\u202E\u206C\u202A\u202B\u202E\u206D\u200B\u200C\u200B\u200D\u202C\u202A\u200D\u200B\u206A\u202E(list[num4]);
					num2 = (\u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(text) ? 558147487 : 542326525);
					continue;
				}
				case 4:
				{
					string text;
					string[] array = \u200D\u200C\u206E\u206E\u206E\u206C\u200F\u206F\u200D\u206E\u202A\u206E\u200D\u200B\u206B\u202D\u202B\u206F\u206F\u206D\u202A\u206E\u202D\u206C\u206A\u200D\u206B\u200B\u206B\u200C\u206D\u200B\u206A\u200F\u200F\u200B\u200B\u202E\u200F\u202A\u202E.\u202A\u200B\u200F\u202C\u202E\u202B\u206E\u200E\u200B\u202D\u202C\u202C\u200B\u202A\u200F\u200B\u202C\u206A\u200B\u206A\u200C\u206B\u202E\u200C\u206E\u206A\u206B\u202E\u200C\u202A\u200D\u206D\u202D\u206C\u200C\u206A\u206E\u206F\u206D\u206D\u202E(text, new char[]
					{
						' '
					}, StringSplitOptions.RemoveEmptyEntries);
					num2 = ((array.Length == 0) ? 1693250284 : -464283485);
					continue;
				}
				case 5:
					goto IL_12;
				case 6:
					goto IL_1D0;
				case 7:
					goto IL_85;
				case 8:
					goto IL_355;
				case 9:
					goto IL_355;
				default:
					goto IL_1D0;
				}
				int num5;
				bool flag2;
				for (;;)
				{
					IL_2FB:
					num = num5;
					uint num6;
					switch ((num6 = (uint)(((num ^ (1869927147 ^ ~(--1172247585))) - 1187015823 * (-331235956 + -1598445209) + -473895489 * 2019372939) * -235921567)) % 6U)
					{
					case 0U:
						goto IL_362;
					case 1U:
						goto IL_355;
					case 2U:
						goto IL_2F6;
					case 4U:
						if (!flag2)
						{
							num5 = (int)(num6 * 3007080293U ^ 50283610U);
							continue;
						}
						goto IL_109;
					case 5U:
						result = hashSet;
						num5 = (int)(num6 * 3519970223U ^ 3097771061U);
						continue;
					}
					return result;
				}
				IL_362:
				flag2 = (num4 < list.Count);
				num5 = -1444558477;
				goto IL_2FB;
				IL_355:
				num4++;
				num5 = -1358000199;
				goto IL_2FB;
				IL_2F6:
				num5 = -2042987802;
				goto IL_2FB;
				IL_1D0:
				using (List<ValueTuple<string, ItemObject>>.Enumerator enumerator = ItemMentionParser.\u206D\u200D\u206A\u206D\u200B\u200F\u200E\u206C\u206D\u200C\u206D\u200C\u206E\u206A\u200F\u200F\u200B\u206A\u206F\u202A\u200C\u206D\u202A\u200E\u202C\u200C\u202C\u206F\u202C\u206B\u202D\u206D\u206C\u206E\u206F\u200F\u200E\u200D\u206B\u202A\u202E.GetEnumerator())
				{
					for (;;)
					{
						IL_2C7:
						int num7 = enumerator.MoveNext() ? -45098562 : 330376565;
						for (;;)
						{
							num = num7;
							uint num6;
							bool flag3;
							switch ((num6 = (uint)(((num ^ (1869927147 ^ ~(--1172247585))) - 1187015823 * (-331235956 + -1598445209) + -473895489 * 2019372939) * -235921567)) % 7U)
							{
							case 1U:
							{
								ValueTuple<string, ItemObject> valueTuple;
								hashSet.Add(valueTuple.Item2);
								num7 = (int)(num6 * 3161495664U ^ 3036033664U);
								continue;
							}
							case 2U:
								num7 = -46017530;
								continue;
							case 3U:
							{
								ValueTuple<string, ItemObject> valueTuple = enumerator.Current;
								string[] array;
								if (ItemMentionParser.\u206F\u200E\u202B\u206B\u200D\u206C\u202C\u200C\u206F\u202A\u206E\u200D\u200D\u202C\u206C\u206E\u200E\u206B\u202C\u206E\u206A\u202B\u202A\u200B\u206E\u206B\u202E\u202C\u200F\u200E\u200B\u206A\u200F\u206A\u206B\u200E\u202E\u206C\u206A\u206B\u202E(array, valueTuple.Item1))
								{
									num7 = 366511030;
									continue;
								}
								flag3 = false;
								goto IL_2AD;
							}
							case 4U:
							{
								ValueTuple<string, ItemObject> valueTuple;
								flag3 = !hashSet.Contains(valueTuple.Item2);
								goto IL_2AD;
							}
							case 5U:
								num7 = -45098562;
								continue;
							case 6U:
								goto IL_2C7;
							}
							goto Block_10;
							IL_2AD:
							bool flag4 = flag3;
							num7 = ((!flag4) ? 1696955408 : -1573948350);
						}
					}
					Block_10:;
				}
				goto IL_2F6;
			}
			IL_85:
			return hashSet;
			IL_EE:
			result = hashSet;
			return result;
			IL_12:
			num2 = -1769849408;
			goto IL_17;
			IL_A0:
			list = Enumerable.ToList<string>(Enumerable.Where<string>(conversationHistory, new Func<string, bool>(ItemMentionParser.\u206D\u200B\u206F\u206D\u200E\u206D\u200D\u200B\u200D\u206A\u206D\u200F\u200C\u206D\u202A\u200E\u206D\u202A\u200B\u200F\u206E\u206F\u202A\u200D\u206B\u206E\u202C\u202C\u200D\u200E\u200E\u206B\u206C\u206E\u206F\u202D\u206A\u206E\u200F\u200E\u202E.<>9.\u200F\u206B\u200F\u200C\u200F\u206E\u200B\u200B\u202B\u200C\u200E\u200D\u200E\u202A\u206E\u206C\u206B\u200E\u200B\u202E\u202C\u202A\u200D\u202B\u206E\u202D\u206E\u206F\u206B\u200C\u206C\u202E\u202B\u200D\u206F\u200F\u202A\u206D\u200E\u202B\u202E)));
			bool flag5 = list.Count == 0;
			num2 = ((!flag5) ? -1093585652 : 1554941775);
			goto IL_17;
		}

		// Token: 0x060001ED RID: 493 RVA: 0x0004B0FC File Offset: 0x000492FC
		private static void \u206B\u200F\u206E\u206F\u200D\u200F\u206C\u206C\u202E\u202D\u206D\u202C\u206F\u206B\u202B\u200E\u206B\u200C\u200E\u202E\u200D\u202B\u206C\u200E\u202A\u200E\u200F\u200E\u206B\u202C\u202C\u200B\u202A\u206E\u202B\u202E\u200B\u202B\u206A\u200D\u202E()
		{
			bool flag = ItemMentionParser.\u206D\u200D\u206A\u206D\u200B\u200F\u200E\u206C\u206D\u200C\u206D\u200C\u206E\u206A\u200F\u200F\u200B\u206A\u206F\u202A\u200C\u206D\u202A\u200E\u202C\u200C\u202C\u206F\u202C\u206B\u202D\u206D\u206C\u206E\u206F\u200F\u200E\u200D\u206B\u202A\u202E != null;
			if (flag)
			{
				for (;;)
				{
					int num = 1759304313;
					switch (-(--65865590 + 1344885761 * -773586390 - (num + (969377118 * -80392783 + ~1534200780 + ~-968780483)) ^ -1414615166) % 3)
					{
					case 1:
						goto IL_63;
					case 2:
						continue;
					}
					break;
				}
				goto IL_78;
				IL_63:
				return;
			}
			IL_78:
			object u200F_u206E_u200D_u206C_u206B_u202C_u206F_u206B_u202D_u202A_u206A_u206D_u206A_u202D_u202D_u202A_u200B_u202D_u202D_u206C_u206B_u206F_u206D_u200B_u200F_u200F_u200C_u202A_u202C_u206A_u206A_u200D_u202A_u206F_u202D_u202B_u202C_u202D_u200D_u202D_u202E = ItemMentionParser.\u200F\u206E\u200D\u206C\u206B\u202C\u206F\u206B\u202D\u202A\u206A\u206D\u206A\u202D\u202D\u202A\u200B\u202D\u202D\u206C\u206B\u206F\u206D\u200B\u200F\u200F\u200C\u202A\u202C\u206A\u206A\u200D\u202A\u206F\u202D\u202B\u202C\u202D\u200D\u202D\u202E;
			bool flag2 = false;
			try
			{
				\u202E\u202C\u200E\u202D\u202B\u202A\u206C\u202B\u202E\u200E\u202D\u200F\u200C\u206C\u200F\u202E\u202C\u202B\u202A\u202E\u202E\u200E\u200E\u202C\u206D\u200F\u200C\u200B\u206C\u206C\u202D\u202E\u206C\u200B\u200E\u200D\u206A\u206F\u202A\u206A\u202E.B\u0005\u009A]\u00BD(u200F_u206E_u200D_u206C_u206B_u202C_u206F_u206B_u202D_u202A_u206A_u206D_u206A_u202D_u202D_u202A_u200B_u202D_u202D_u206C_u206B_u206F_u206D_u200B_u200F_u200F_u200C_u202A_u202C_u206A_u206A_u200D_u202A_u206F_u202D_u202B_u202C_u202D_u200D_u202D_u202E, ref flag2);
				bool flag3 = ItemMentionParser.\u206D\u200D\u206A\u206D\u200B\u200F\u200E\u206C\u206D\u200C\u206D\u200C\u206E\u206A\u200F\u200F\u200B\u206A\u206F\u202A\u200C\u206D\u202A\u200E\u202C\u200C\u202C\u206F\u202C\u206B\u202D\u206D\u206C\u206E\u206F\u200F\u200E\u200D\u206B\u202A\u202E != null;
				if (flag3)
				{
					for (;;)
					{
						int num = 1465900416;
						switch (-(--65865590 + 1344885761 * -773586390 - (num + (969377118 * -80392783 + ~1534200780 + ~-968780483)) ^ -1414615166) % 3)
						{
						case 1:
							goto IL_F1;
						case 2:
							continue;
						}
						break;
					}
					goto IL_106;
					IL_F1:
					return;
				}
				IL_106:
				ItemMentionParser.\u206D\u200D\u206A\u206D\u200B\u200F\u200E\u206C\u206D\u200C\u206D\u200C\u206E\u206A\u200F\u200F\u200B\u206A\u206F\u202A\u200C\u206D\u202A\u200E\u202C\u200C\u202C\u206F\u202C\u206B\u202D\u206D\u206C\u206E\u206F\u200F\u200E\u200D\u206B\u202A\u202E = new List<ValueTuple<string, ItemObject>>();
				using (List<ItemObject>.Enumerator enumerator = \u206F\u200B\u200D\u200B\u202B\u206E\u202C\u202A\u202C\u200C\u200F\u202E\u206D\u202B\u202A\u200E\u206A\u200F\u202D\u202C\u206A\u200D\u206C\u200F\u206A\u200D\u206E\u200D\u206C\u202C\u206F\u206C\u200D\u200B\u200D\u206C\u206A\u200B\u206C\u202A\u202E.ãkçm\u008F().GetEnumerator())
				{
					for (;;)
					{
						IL_245:
						int num2 = enumerator.MoveNext() ? 1748309755 : 1170699900;
						for (;;)
						{
							int num = num2;
							uint num3;
							bool flag6;
							switch ((num3 = (uint)(-(uint)(--65865590 + 1344885761 * -773586390 - (num + (969377118 * -80392783 + ~1534200780 + ~-968780483)) ^ -1414615166))) % 19U)
							{
							case 0U:
								num2 = 710649634;
								continue;
							case 1U:
							{
								string text;
								ItemObject itemObject;
								ItemMentionParser.\u206D\u200D\u206A\u206D\u200B\u200F\u200E\u206C\u206D\u200C\u206D\u200C\u206E\u206A\u200F\u200F\u200B\u206A\u206F\u202A\u200C\u206D\u202A\u200E\u202C\u200C\u202C\u206F\u202C\u206B\u202D\u206D\u206C\u206E\u206F\u200F\u200E\u200D\u206B\u202A\u202E.Add(new ValueTuple<string, ItemObject>(text, itemObject));
								num2 = (int)(num3 * 3318143262U ^ 727439896U);
								continue;
							}
							case 2U:
							{
								ItemObject itemObject;
								if (!\u206F\u200E\u202D\u206F\u206E\u202A\u200D\u200B\u200D\u206A\u202E\u200C\u200B\u200D\u206A\u206C\u200B\u202A\u206A\u206E\u206A\u206D\u206F\u200C\u200B\u200C\u206F\u202D\u206A\u206F\u200F\u202E\u200B\u200E\u202B\u206D\u200B\u200D\u202C\u200F\u202E.nË\u0083\u00BB|(itemObject))
								{
									num2 = 1066758943;
									continue;
								}
								goto IL_29C;
							}
							case 3U:
							{
								ItemObject itemObject;
								string text = ItemMentionParser.\u200F\u206B\u200E\u202A\u206F\u202C\u200B\u202C\u206E\u202B\u206E\u200F\u200F\u206D\u200B\u202A\u202B\u206E\u200F\u202D\u202B\u206A\u206D\u206F\u202E\u202E\u206C\u202A\u202B\u202E\u206D\u200B\u200C\u200B\u200D\u202C\u202A\u200D\u200B\u206A\u202E(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(itemObject));
								num2 = (int)(((!\u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(text)) ? 1974794786U : 563308434U) ^ num3 * 2457223540U);
								continue;
							}
							case 4U:
							{
								ItemObject itemObject;
								if (!\u206F\u200E\u202D\u206F\u206E\u202A\u200D\u200B\u200D\u206A\u202E\u200C\u200B\u200D\u206A\u206C\u200B\u202A\u206A\u206E\u206A\u206D\u206F\u200C\u200B\u200C\u206F\u202D\u206A\u206F\u200F\u202E\u200B\u200E\u202B\u206D\u200B\u200D\u202C\u200F\u202E.Êå8\u00A8â(itemObject))
								{
									num2 = (int)(num3 * 523003767U ^ 3017668294U);
									continue;
								}
								goto IL_29C;
							}
							case 5U:
							{
								ItemObject itemObject;
								TextObject textObject = \u202A\u200D\u200C\u202E\u200D\u206D\u200B\u200C\u202E\u202B\u206E\u202D\u202C\u202E\u206E\u206B\u202B\u202B\u202D\u202C\u206C\u206D\u202A\u202A\u206C\u206F\u202D\u202E\u206E\u200F\u202B\u206B\u200D\u202B\u200F\u206B\u202C\u200D\u206B\u202B\u202E.aëôÄì(itemObject);
								string text2 = (textObject != null) ? \u206F\u200D\u202B\u206A\u200F\u200D\u202B\u200B\u206C\u206B\u202E\u206E\u200F\u200D\u200F\u206D\u200B\u200C\u202C\u206D\u202A\u206C\u200C\u200B\u202A\u202E\u200E\u202B\u202C\u202E\u202E\u200E\u206D\u202B\u206E\u206A\u200D\u202D\u206E\u200E\u202E.\u200B\u206D\u206E\u200E\u200C\u200B\u202C\u200E\u200C\u206A\u200F\u202B\u206D\u206E\u202E\u202E\u200C\u202D\u202E\u202B\u206D\u206B\u200F\u202B\u200B\u202C\u202E\u200F\u202C\u206A\u200F\u200E\u202C\u206F\u202E\u200E\u206D\u202C\u206B\u206C\u202E(textObject) : null;
								bool flag4 = !\u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.\u00B1]OTö(text2);
								num2 = ((!flag4) ? 1893939622 : 867965309);
								continue;
							}
							case 6U:
							{
								string text2;
								string text3 = ItemMentionParser.\u200F\u206B\u200E\u202A\u206F\u202C\u200B\u202C\u206E\u202B\u206E\u200F\u200F\u206D\u200B\u202A\u202B\u206E\u200F\u202D\u202B\u206A\u206D\u206F\u202E\u202E\u206C\u202A\u202B\u202E\u206D\u200B\u200C\u200B\u200D\u202C\u202A\u200D\u200B\u206A\u202E(text2);
								bool flag5 = !\u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(text3);
								num2 = (int)(((!flag5) ? 2721947415U : 2467372215U) ^ num3 * 4214726959U);
								continue;
							}
							case 7U:
								num2 = 1893939622;
								continue;
							case 8U:
								goto IL_245;
							case 9U:
								num2 = 920692226;
								continue;
							case 10U:
							{
								ItemObject itemObject;
								num2 = ((!\u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.\u00B1]OTö(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(itemObject))) ? 664021921 : 710649634);
								continue;
							}
							case 11U:
								num2 = (int)(num3 * 1372460664U ^ 1815876426U);
								continue;
							case 13U:
							{
								ItemObject itemObject;
								flag6 = !\u206F\u200E\u202D\u206F\u206E\u202A\u200D\u200B\u200D\u206A\u202E\u200C\u200B\u200D\u206A\u206C\u200B\u202A\u206A\u206E\u206A\u206D\u206F\u200C\u200B\u200C\u206F\u202D\u206A\u206F\u200F\u202E\u200B\u200E\u202B\u206D\u200B\u200D\u202C\u200F\u202E.ùç5e\u00B8(itemObject);
								goto IL_29D;
							}
							case 14U:
							{
								ItemObject itemObject;
								if (!\u206F\u200E\u202D\u206F\u206E\u202A\u200D\u200B\u200D\u206A\u202E\u200C\u200B\u200D\u206A\u206C\u200B\u202A\u206A\u206E\u206A\u206D\u206F\u200C\u200B\u200C\u206F\u202D\u206A\u206F\u200F\u202E\u200B\u200E\u202B\u206D\u200B\u200D\u202C\u200F\u202E.3^\u00A4hî(itemObject))
								{
									num2 = (int)(num3 * 931229241U ^ 42729453U);
									continue;
								}
								goto IL_29C;
							}
							case 15U:
								num2 = 1748309755;
								continue;
							case 16U:
							{
								ItemObject itemObject = enumerator.Current;
								num2 = ((itemObject == null) ? 1680451009 : 1843617061);
								continue;
							}
							case 17U:
							{
								ItemObject itemObject;
								string text3;
								ItemMentionParser.\u206D\u200D\u206A\u206D\u200B\u200F\u200E\u206C\u206D\u200C\u206D\u200C\u206E\u206A\u200F\u200F\u200B\u206A\u206F\u202A\u200C\u206D\u202A\u200E\u202C\u200C\u202C\u206F\u202C\u206B\u202D\u206D\u206C\u206E\u206F\u200F\u200E\u200D\u206B\u202A\u202E.Add(new ValueTuple<string, ItemObject>(text3, itemObject));
								num2 = (int)(num3 * 2409331560U ^ 1956572085U);
								continue;
							}
							case 18U:
								num2 = (int)(num3 * 2733925366U ^ 2892997710U);
								continue;
							}
							goto Block_7;
							IL_29D:
							num2 = (flag6 ? 1333647666 : 671968974);
							continue;
							IL_29C:
							flag6 = false;
							goto IL_29D;
						}
					}
					Block_7:;
				}
			}
			finally
			{
				if (flag2)
				{
					for (;;)
					{
						IL_438:
						int num4 = 770223643;
						for (;;)
						{
							int num = num4;
							uint num3;
							switch ((num3 = (uint)(-(uint)(--65865590 + 1344885761 * -773586390 - (num + (969377118 * -80392783 + ~1534200780 + ~-968780483)) ^ -1414615166))) % 3U)
							{
							case 0U:
								goto IL_438;
							case 1U:
								\u202C\u206A\u200D\u202B\u206E\u202E\u200F\u200D\u200D\u200B\u200E\u202C\u202C\u206E\u206E\u200F\u206F\u202B\u200B\u200C\u206A\u200B\u202A\u202C\u200E\u206E\u200C\u202E\u200F\u200E\u202E\u202D\u206C\u202D\u200C\u206E\u200B\u206E\u202C\u206F\u202E.\u0011\u0004\u0008\u009F\u0004(u200F_u206E_u200D_u206C_u206B_u202C_u206F_u206B_u202D_u202A_u206A_u206D_u206A_u202D_u202D_u202A_u200B_u202D_u202D_u206C_u206B_u206F_u206D_u200B_u200F_u200F_u200C_u202A_u202C_u206A_u206A_u200D_u202A_u206F_u202D_u202B_u202C_u202D_u200D_u202D_u202E);
								num4 = (int)(num3 * 51812472U ^ 1228528926U);
								continue;
							}
							goto Block_21;
						}
					}
					Block_21:;
				}
			}
		}

		// Token: 0x060001EE RID: 494 RVA: 0x0004B5E8 File Offset: 0x000497E8
		private static string \u200F\u206B\u200E\u202A\u206F\u202C\u200B\u202C\u206E\u202B\u206E\u200F\u200F\u206D\u200B\u202A\u202B\u206E\u200F\u202D\u202B\u206A\u206D\u206F\u202E\u202E\u206C\u202A\u202B\u202E\u206D\u200B\u200C\u200B\u200D\u202C\u202A\u200D\u200B\u206A\u202E(string A_0)
		{
			bool flag = \u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.\u00B1]OTö(A_0);
			if (flag)
			{
				goto IL_13;
			}
			goto IL_FD;
			int num2;
			int num4;
			string text;
			StringBuilder stringBuilder;
			char c2;
			string result;
			for (;;)
			{
				IL_18:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(-(~(~num)) ^ 280126867)) % 16U)
				{
				case 0U:
					num2 = (int)(num3 * 3402086526U ^ 4062401843U);
					continue;
				case 1U:
					num2 = -1921494587;
					continue;
				case 2U:
				{
					char c;
					UnicodeCategory unicodeCategory = \u200E\u202A\u206C\u200E\u202A\u200F\u202B\u202C\u202E\u206E\u200D\u200B\u206E\u206F\u206F\u202B\u202A\u202C\u200C\u202B\u206D\u202C\u202C\u200D\u206B\u200F\u206A\u200D\u200C\u200B\u200C\u200D\u200D\u200E\u200C\u200B\u202E\u206B\u200F\u206C\u202E.c\u00A2fáq(c);
					num2 = (int)(((unicodeCategory == UnicodeCategory.NonSpacingMark) ? 2074617935U : 25078718U) ^ num3 * 1519080585U);
					continue;
				}
				case 3U:
					goto IL_FD;
				case 4U:
					num2 = ((num4 >= \u206B\u200E\u200F\u206F\u202E\u200B\u206E\u200C\u202B\u200B\u206A\u206F\u206C\u202C\u202E\u200D\u206C\u206C\u206B\u202E\u206B\u206C\u202D\u202A\u206D\u206F\u202C\u202D\u200F\u206F\u200B\u206D\u200B\u200E\u200B\u206E\u202C\u206B\u206C\u202E.j\u0003{\u0095\u00F7(text)) ? -2036529614 : -1117809036);
					continue;
				case 5U:
					goto IL_13;
				case 6U:
					\u206A\u202B\u202B\u200F\u202A\u206B\u202C\u200F\u200C\u206E\u200D\u206C\u202C\u206C\u202D\u206C\u202D\u200E\u200D\u202A\u202C\u202B\u206D\u206B\u202A\u200D\u200F\u206A\u202C\u202B\u202C\u200B\u202B\u202D\u206D\u206D\u200F\u200F\u200D\u206B\u202E.\u00B6Ø\u00B1~\u00F7(stringBuilder, ' ');
					c2 = ' ';
					num2 = (int)(num3 * 1757339684U ^ 566940870U);
					continue;
				case 7U:
				{
					char c;
					num2 = (\u206B\u200D\u206A\u206F\u202B\u202A\u200D\u206B\u200D\u200B\u206C\u200F\u202D\u206E\u200F\u200B\u200B\u200C\u200E\u206B\u200F\u200C\u202D\u206C\u200E\u202A\u206C\u206F\u200D\u206C\u206B\u202E\u202A\u200E\u202A\u206F\u202E\u206D\u206C\u200D\u202E.\u0099ÝÚ\u0011Â(c) ? -1920349834 : -1180836687);
					continue;
				}
				case 8U:
					num2 = -1092365;
					continue;
				case 9U:
				{
					char c;
					char c3 = \u206D\u202B\u202E\u200D\u202E\u202D\u206C\u202B\u202C\u202E\u202C\u206A\u200B\u206E\u206D\u202A\u202B\u200F\u200F\u206D\u202C\u200D\u202D\u200C\u206E\u206D\u200D\u202C\u206D\u206D\u206D\u200B\u206C\u206B\u206F\u202B\u206C\u206F\u202D\u206E\u202E.?\u001F<\u0009\u0091(c);
					\u206A\u202B\u202B\u200F\u202A\u206B\u202C\u200F\u200C\u206E\u200D\u206C\u202C\u206C\u202D\u206C\u202D\u200E\u200D\u202A\u202C\u202B\u206D\u206B\u202A\u200D\u200F\u206A\u202C\u202B\u202C\u200B\u202B\u202D\u206D\u206D\u200F\u200F\u200D\u206B\u202E.\u00B6Ø\u00B1~\u00F7(stringBuilder, c3);
					c2 = c3;
					num2 = (int)(num3 * 2495344624U ^ 2241687477U);
					continue;
				}
				case 10U:
					result = string.Empty;
					num2 = (int)(num3 * 106840861U ^ 3087902954U);
					continue;
				case 12U:
					num2 = ((c2 != ' ') ? -2031986965 : -890710498);
					continue;
				case 13U:
					result = \u202A\u200F\u200E\u200F\u202E\u202B\u200B\u206F\u206E\u206D\u200D\u202A\u202A\u200E\u206F\u206A\u200C\u206D\u202B\u200E\u202B\u200C\u202E\u200B\u200B\u200E\u206E\u202D\u206F\u206F\u202D\u206A\u200B\u202A\u200B\u202B\u202D\u202B\u200C\u206E\u202E.\u008Bè\u007Fgn(\u206F\u200D\u202B\u206A\u200F\u200D\u202B\u200B\u206C\u206B\u202E\u206E\u200F\u200D\u200F\u206D\u200B\u200C\u202C\u206D\u202A\u206C\u200C\u200B\u202A\u202E\u200E\u202B\u202C\u202E\u202E\u200E\u206D\u202B\u206E\u206A\u200D\u202D\u206E\u200E\u202E.Â^\u0084\u0005ð(stringBuilder));
					num2 = (int)(num3 * 3479323263U ^ 2211477019U);
					continue;
				case 14U:
					num4++;
					num2 = -1106975127;
					continue;
				case 15U:
				{
					char c = \u202E\u200B\u200C\u202D\u206E\u206D\u202D\u206D\u202C\u206E\u202B\u202D\u202A\u206D\u202A\u206D\u202C\u202D\u202B\u206D\u202D\u206D\u202B\u200C\u202D\u202E\u202A\u206A\u206B\u206C\u206A\u202E\u200F\u200B\u206F\u200B\u202B\u202C\u206A\u206D\u202E.JïÔ\u0093'(text, num4);
					num2 = -1351005665;
					continue;
				}
				}
				break;
			}
			return result;
			IL_13:
			num2 = -1807443689;
			goto IL_18;
			IL_FD:
			string text2 = \u206D\u206D\u202B\u202D\u202D\u200D\u202A\u206C\u200E\u200C\u202E\u202E\u206D\u200D\u206F\u200E\u206C\u200D\u200F\u206C\u206B\u200C\u202D\u202B\u200B\u202E\u202B\u202B\u206E\u206C\u206B\u200E\u202C\u202C\u206E\u206C\u202E\u206F\u200D\u202E.c\u00153Ù\u0080(A_0, NormalizationForm.FormD);
			stringBuilder = \u206A\u202E\u202C\u200F\u206A\u202B\u202B\u202E\u202D\u206A\u202E\u202D\u202A\u202A\u200C\u202A\u206C\u206F\u206F\u206C\u200D\u200E\u202A\u200E\u202A\u206C\u200F\u202B\u202E\u202C\u206B\u202C\u202C\u202C\u200D\u200B\u200F\u200C\u202E\u206B\u202E.\u0091\u00BCÊ\u0019r(\u206B\u200E\u200F\u206F\u202E\u200B\u206E\u200C\u202B\u200B\u206A\u206F\u206C\u202C\u202E\u200D\u206C\u206C\u206B\u202E\u206B\u206C\u202D\u202A\u206D\u206F\u202C\u202D\u200F\u206F\u200B\u206D\u200B\u200E\u200B\u206E\u202C\u206B\u206C\u202E.j\u0003{\u0095\u00F7(text2));
			c2 = '\0';
			text = text2;
			num4 = 0;
			num2 = -1106975127;
			goto IL_18;
		}

		// Token: 0x060001EF RID: 495 RVA: 0x0004B830 File Offset: 0x00049A30
		private static bool \u206F\u200E\u202B\u206B\u200D\u206C\u202C\u200C\u206F\u202A\u206E\u200D\u200D\u202C\u206C\u206E\u200E\u206B\u202C\u206E\u206A\u202B\u202A\u200B\u206E\u206B\u202E\u202C\u200F\u200E\u200B\u206A\u200F\u206A\u206B\u200E\u202E\u206C\u206A\u206B\u202E(string[] A_0, string A_1)
		{
			if (A_0 != null)
			{
				goto IL_07;
			}
			goto IL_3A9;
			int num2;
			bool result;
			for (;;)
			{
				IL_0C:
				int num = num2;
				uint num3;
				bool flag2;
				bool flag3;
				bool flag4;
				bool flag6;
				switch ((num3 = (uint)(~num * 296731455)) % 33U)
				{
				case 0U:
				{
					string[] array;
					int num4;
					string text = array[num4];
					bool flag = \u206B\u200E\u200F\u206F\u202E\u200B\u206E\u200C\u202B\u200B\u206A\u206F\u206C\u202C\u202E\u200D\u206C\u206C\u206B\u202E\u206B\u206C\u202D\u202A\u206D\u206F\u202C\u202D\u200F\u206F\u200B\u206D\u200B\u200E\u200B\u206E\u202C\u206B\u206C\u202E.j\u0003{\u0095\u00F7(text) < 3;
					num2 = ((!flag) ? -225912901 : -1644954684);
					continue;
				}
				case 1U:
				{
					string text2;
					if (\u206B\u200E\u200F\u206F\u202E\u200B\u206E\u200C\u202B\u200B\u206A\u206F\u206C\u202C\u202E\u200D\u206C\u206C\u206B\u202E\u206B\u206C\u202D\u202A\u206D\u206F\u202C\u202D\u200F\u206F\u200B\u206D\u200B\u200E\u200B\u206E\u202C\u206B\u206C\u202E.j\u0003{\u0095\u00F7(text2) >= 5)
					{
						num2 = -1135691013;
						continue;
					}
					flag2 = false;
					goto IL_33C;
				}
				case 2U:
				{
					string text;
					string text2;
					if (!\u200C\u200C\u206A\u202D\u202C\u202E\u206F\u206B\u202B\u206B\u206C\u202D\u206E\u202C\u206B\u206F\u202E\u206A\u206D\u202B\u200E\u206D\u202E\u206C\u206F\u202B\u200C\u200B\u206B\u202E\u200E\u206B\u200E\u200C\u206F\u206A\u206B\u206A\u200C\u202E\u202E.:lo\u00B2Ò(text2, text, StringComparison.Ordinal))
					{
						num2 = (int)(num3 * 3706587982U ^ 3254672871U);
						continue;
					}
					flag3 = true;
					goto IL_231;
				}
				case 3U:
					num2 = (int)(num3 * 2899040630U ^ 1910879586U);
					continue;
				case 4U:
					num2 = (int)(num3 * 4263753615U ^ 3840696712U);
					continue;
				case 5U:
				{
					string text;
					string text2;
					flag3 = \u200C\u200C\u206A\u202D\u202C\u202E\u206F\u206B\u202B\u206B\u206C\u202D\u206E\u202C\u206B\u206F\u202E\u206A\u206D\u202B\u200E\u206D\u202E\u206C\u206F\u202B\u200C\u200B\u206B\u202E\u200E\u206B\u200E\u200C\u206F\u206A\u206B\u206A\u200C\u202E\u202E.:lo\u00B2Ò(text, text2, StringComparison.Ordinal);
					goto IL_231;
				}
				case 6U:
					num2 = 895476444;
					continue;
				case 7U:
				{
					string text;
					string text2;
					num2 = (\u200C\u200C\u206A\u202D\u202C\u202E\u206F\u206B\u202B\u206B\u206C\u202D\u206E\u202C\u206B\u206F\u202E\u206A\u206D\u202B\u200E\u206D\u202E\u206C\u206F\u202B\u200C\u200B\u206B\u202E\u200E\u206B\u200E\u200C\u206F\u206A\u206B\u206A\u200C\u202E\u202E.\u0016Æ\u00B8Ä[(text2, text, StringComparison.Ordinal) ? 403528729 : 1656775860);
					continue;
				}
				case 8U:
					result = true;
					num2 = (int)(num3 * 1166099692U ^ 527492028U);
					continue;
				case 9U:
				{
					string[] array;
					int num4;
					num2 = ((num4 < array.Length) ? 1213907432 : 1965495966);
					continue;
				}
				case 10U:
					result = false;
					num2 = (int)(num3 * 2212401310U ^ 3764342256U);
					continue;
				case 11U:
				{
					string text;
					string text2;
					int num5 = \u202B\u206E\u206D\u202E\u202C\u202B\u200F\u202D\u206B\u202A\u206C\u206D\u202D\u200F\u206D\u200B\u200D\u200C\u202B\u200C\u206A\u206E\u200F\u202D\u202B\u202C\u206F\u200B\u200B\u202E\u202C\u202C\u202B\u206C\u206C\u200B\u202C\u206B\u200D\u202A\u202E.Õ\u00BCõmÿ(4, \u202B\u206E\u206D\u202E\u202C\u202B\u200F\u202D\u206B\u202A\u206C\u206D\u202D\u200F\u206D\u200B\u200D\u200C\u202B\u200C\u206A\u206E\u200F\u202D\u202B\u202C\u206F\u200B\u200B\u202E\u202C\u202C\u202B\u206C\u206C\u200B\u202C\u206B\u200D\u202A\u202E.Õ\u00BCõmÿ(\u206B\u200E\u200F\u206F\u202E\u200B\u206E\u200C\u202B\u200B\u206A\u206F\u206C\u202C\u202E\u200D\u206C\u206C\u206B\u202E\u206B\u206C\u202D\u202A\u206D\u206F\u202C\u202D\u200F\u206F\u200B\u206D\u200B\u200E\u200B\u206E\u202C\u206B\u206C\u202E.j\u0003{\u0095\u00F7(text2) - 1, \u206B\u200E\u200F\u206F\u202E\u200B\u206E\u200C\u202B\u200B\u206A\u206F\u206C\u202C\u202E\u200D\u206C\u206C\u206B\u202E\u206B\u206C\u202D\u202A\u206D\u206F\u202C\u202D\u200F\u206F\u200B\u206D\u200B\u200E\u200B\u206E\u202C\u206B\u206C\u202E.j\u0003{\u0095\u00F7(text) - 1));
					num2 = ((num5 >= 3) ? 1670245433 : 947416037);
					continue;
				}
				case 12U:
					goto IL_07;
				case 13U:
					num2 = 80110707;
					continue;
				case 14U:
				{
					string[] array2;
					string[] array = array2;
					int num4 = 0;
					num2 = 119698073;
					continue;
				}
				case 15U:
					num2 = (int)(num3 * 1544234180U ^ 855746312U);
					continue;
				case 16U:
					result = true;
					num2 = (int)(num3 * 930132261U ^ 4243728858U);
					continue;
				case 17U:
				{
					string text;
					flag2 = (\u206B\u200E\u200F\u206F\u202E\u200B\u206E\u200C\u202B\u200B\u206A\u206F\u206C\u202C\u202E\u200D\u206C\u206C\u206B\u202E\u206B\u206C\u202D\u202A\u206D\u206F\u202C\u202D\u200F\u206F\u200B\u206D\u200B\u200E\u200B\u206E\u202C\u206B\u206C\u202E.j\u0003{\u0095\u00F7(text) >= 5);
					goto IL_33C;
				}
				case 18U:
				{
					int num4;
					num4++;
					num2 = 119698073;
					continue;
				}
				case 19U:
					result = true;
					num2 = (int)(num3 * 2919198804U ^ 3444723508U);
					continue;
				case 20U:
					num2 = 947416037;
					continue;
				case 21U:
					result = false;
					num2 = (int)(num3 * 3343924189U ^ 1782240471U);
					continue;
				case 22U:
					num2 = (int)(((!flag4) ? 3838971792U : 3086851420U) ^ num3 * 1592320815U);
					continue;
				case 23U:
				{
					int num6;
					num6++;
					num2 = 995262650;
					continue;
				}
				case 25U:
					goto IL_39C;
				case 26U:
				{
					int num6;
					num2 = ((num6 < A_0.Length) ? 1394232658 : -1957589237);
					continue;
				}
				case 27U:
				{
					string text2;
					int num5;
					string text3 = \u202B\u200F\u200F\u202A\u202A\u202E\u200C\u206D\u200F\u206D\u200D\u200E\u202C\u206C\u206C\u206E\u206F\u206D\u200C\u206A\u200E\u200E\u200E\u206D\u200C\u202C\u206D\u200E\u202E\u202C\u206C\u200F\u206F\u206D\u202D\u206E\u202D\u202E\u200E\u202E\u202E.\u0089NÙUe(text2, 0, num5);
					string text;
					string text4 = \u202B\u200F\u200F\u202A\u202A\u202E\u200C\u206D\u200F\u206D\u200D\u200E\u202C\u206C\u206C\u206E\u206F\u206D\u200C\u206A\u200E\u200E\u200E\u206D\u200C\u202C\u206D\u200E\u202E\u202C\u206C\u200F\u206F\u206D\u202D\u206E\u202D\u202E\u200E\u202E\u202E.\u0089NÙUe(text, 0, num5);
					bool flag5 = \u200C\u200C\u206A\u202D\u202C\u202E\u206F\u206B\u202B\u206B\u206C\u202D\u206E\u202C\u206B\u206F\u202E\u206A\u206D\u202B\u200E\u206D\u202E\u206C\u206F\u202B\u200C\u200B\u206B\u202E\u200E\u206B\u200E\u200C\u206F\u206A\u206B\u206A\u200C\u202E\u202E.\u0016Æ\u00B8Ä[(text3, text4, StringComparison.Ordinal);
					num2 = (int)(((!flag5) ? 3184221190U : 1278546365U) ^ num3 * 2059664113U);
					continue;
				}
				case 28U:
				{
					int num6;
					string text2 = A_0[num6];
					if (!\u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(text2))
					{
						num2 = 149281981;
						continue;
					}
					flag6 = true;
					goto IL_186;
				}
				case 29U:
				{
					string[] array2 = \u200D\u200C\u206E\u206E\u206E\u206C\u200F\u206F\u200D\u206E\u202A\u206E\u200D\u200B\u206B\u202D\u202B\u206F\u206F\u206D\u202A\u206E\u202D\u206C\u206A\u200D\u206B\u200B\u206B\u200C\u206D\u200B\u206A\u200F\u200F\u200B\u200B\u202E\u200F\u202A\u202E.\u202A\u200B\u200F\u202C\u202E\u202B\u206E\u200E\u200B\u202D\u202C\u202C\u200B\u202A\u200F\u200B\u202C\u206A\u200B\u206A\u200C\u206B\u202E\u200C\u206E\u206A\u206B\u202E\u200C\u202A\u200D\u206D\u202D\u206C\u200C\u206A\u206E\u206F\u206D\u206D\u202E(A_1, new char[]
					{
						' '
					}, StringSplitOptions.RemoveEmptyEntries);
					int num6 = 0;
					num2 = 995262650;
					continue;
				}
				case 30U:
					if (A_0.Length != 0)
					{
						num2 = (int)(num3 * 2182589453U ^ 3407377664U);
						continue;
					}
					goto IL_3A9;
				case 31U:
				{
					string text2;
					flag6 = (\u206B\u200E\u200F\u206F\u202E\u200B\u206E\u200C\u202B\u200B\u206A\u206F\u206C\u202C\u202E\u200D\u206C\u206C\u206B\u202E\u206B\u206C\u202D\u202A\u206D\u206F\u202C\u202D\u200F\u206F\u200B\u206D\u200B\u200E\u200B\u206E\u202C\u206B\u206C\u202E.j\u0003{\u0095\u00F7(text2) < 3);
					goto IL_186;
				}
				case 32U:
					num2 = (int)(num3 * 720545106U ^ 2329258726U);
					continue;
				}
				break;
				IL_186:
				flag4 = flag6;
				num2 = 1553617452;
				continue;
				IL_231:
				num2 = (flag3 ? -397295094 : -1825267074);
				continue;
				IL_33C:
				num2 = (flag2 ? -696822023 : 80110707);
			}
			return result;
			IL_39C:
			bool flag7 = \u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(A_1);
			goto IL_3AA;
			IL_07:
			num2 = -106037282;
			goto IL_0C;
			IL_3A9:
			flag7 = true;
			IL_3AA:
			num2 = (flag7 ? -532840998 : 342333420);
			goto IL_0C;
		}

		// Token: 0x04000172 RID: 370
		public const int DefaultMessagesToScan = 6;

		// Token: 0x04000173 RID: 371
		private static readonly object \u200F\u206E\u200D\u206C\u206B\u202C\u206F\u206B\u202D\u202A\u206A\u206D\u206A\u202D\u202D\u202A\u200B\u202D\u202D\u206C\u206B\u206F\u206D\u200B\u200F\u200F\u200C\u202A\u202C\u206A\u206A\u200D\u202A\u206F\u202D\u202B\u202C\u202D\u200D\u202D\u202E = \u200B\u202C\u200C\u202B\u200C\u202A\u202D\u206E\u206E\u206F\u200E\u200C\u202B\u206E\u206F\u200E\u200F\u206C\u200F\u206B\u206B\u200F\u206C\u202C\u200F\u202B\u200F\u206D\u202B\u202A\u202D\u200E\u202A\u206C\u206A\u206E\u200D\u206F\u200C\u202B\u202E.ä\u007FÎé;();

		// Token: 0x04000174 RID: 372
		[TupleElementNames(new string[]
		{
			"NormalizedName",
			"Item"
		})]
		private static List<ValueTuple<string, ItemObject>> \u206D\u200D\u206A\u206D\u200B\u200F\u200E\u206C\u206D\u200C\u206D\u200C\u206E\u206A\u200F\u200F\u200B\u206A\u206F\u202A\u200C\u206D\u202A\u200E\u202C\u200C\u202C\u206F\u202C\u206B\u202D\u206D\u206C\u206E\u206F\u200F\u200E\u200D\u206B\u202A\u202E;

		// Token: 0x02000057 RID: 87
		[CompilerGenerated]
		[Serializable]
		private sealed class \u206D\u200B\u206F\u206D\u200E\u206D\u200D\u200B\u200D\u206A\u206D\u200F\u200C\u206D\u202A\u200E\u206D\u202A\u200B\u200F\u206E\u206F\u202A\u200D\u206B\u206E\u202C\u202C\u200D\u200E\u200E\u206B\u206C\u206E\u206F\u202D\u206A\u206E\u200F\u200E\u202E
		{
			// Token: 0x060001F3 RID: 499 RVA: 0x0004BCC0 File Offset: 0x00049EC0
			internal bool \u200F\u206B\u200F\u200C\u200F\u206E\u200B\u200B\u202B\u200C\u200E\u200D\u200E\u202A\u206E\u206C\u206B\u200E\u200B\u202E\u202C\u202A\u200D\u202B\u206E\u202D\u206E\u206F\u206B\u200C\u206C\u202E\u202B\u200D\u206F\u200F\u202A\u206D\u200E\u202B\u202E(string A_1)
			{
				return !\u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.\u00B1]OTö(A_1);
			}

			// Token: 0x04000175 RID: 373
			public static readonly ItemMentionParser.\u206D\u200B\u206F\u206D\u200E\u206D\u200D\u200B\u200D\u206A\u206D\u200F\u200C\u206D\u202A\u200E\u206D\u202A\u200B\u200F\u206E\u206F\u202A\u200D\u206B\u206E\u202C\u202C\u200D\u200E\u200E\u206B\u206C\u206E\u206F\u202D\u206A\u206E\u200F\u200E\u202E <>9 = new ItemMentionParser.\u206D\u200B\u206F\u206D\u200E\u206D\u200D\u200B\u200D\u206A\u206D\u200F\u200C\u206D\u202A\u200E\u206D\u202A\u200B\u200F\u206E\u206F\u202A\u200D\u206B\u206E\u202C\u202C\u200D\u200E\u200E\u206B\u206C\u206E\u206F\u202D\u206A\u206E\u200F\u200E\u202E();

			// Token: 0x04000176 RID: 374
			public static Func<string, bool> <>9__4_0;
		}
	}
}
