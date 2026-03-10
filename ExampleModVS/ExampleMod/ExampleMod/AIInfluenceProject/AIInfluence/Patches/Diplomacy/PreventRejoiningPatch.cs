using System;
using System.Runtime.CompilerServices;
using AIInfluence.Diplomacy;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Library;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence.Patches.Diplomacy
{
	// Token: 0x020002DB RID: 731
	[HarmonyPatch(typeof(ChangeKingdomAction))]
	public static class PreventRejoiningPatch
	{
		// Token: 0x06001461 RID: 5217 RVA: 0x001280AC File Offset: 0x001262AC
		[HarmonyPrefix]
		[HarmonyPatch("ApplyByJoinToKingdom")]
		public static bool Prefix_ApplyByJoinToKingdom(Clan clan, Kingdom newKingdom, CampaignTime shouldStayInKingdomUntil, bool showNotification)
		{
			return PreventRejoiningPatch.\u206B\u206B\u202D\u200E\u206E\u206F\u200D\u206B\u200B\u206C\u202C\u206A\u202E\u200B\u206C\u206E\u202A\u202A\u202E\u202A\u202D\u200B\u200F\u200F\u206A\u206E\u202D\u200C\u200D\u206A\u200B\u200E\u200F\u202E\u202D\u202C\u206B\u200C\u200D\u202E\u202E(clan, newKingdom);
		}

		// Token: 0x06001462 RID: 5218 RVA: 0x001280C8 File Offset: 0x001262C8
		[HarmonyPrefix]
		[HarmonyPatch("ApplyByJoinToKingdomByDefection")]
		public static bool Prefix_ApplyByJoinToKingdomByDefection(Clan clan, Kingdom oldKingdom, Kingdom newKingdom, CampaignTime shouldStayInKingdomUntil, bool showNotification)
		{
			return PreventRejoiningPatch.\u206B\u206B\u202D\u200E\u206E\u206F\u200D\u206B\u200B\u206C\u202C\u206A\u202E\u200B\u206C\u206E\u202A\u202A\u202E\u202A\u202D\u200B\u200F\u200F\u206A\u206E\u202D\u200C\u200D\u206A\u200B\u200E\u200F\u202E\u202D\u202C\u206B\u200C\u200D\u202E\u202E(clan, newKingdom);
		}

		// Token: 0x06001463 RID: 5219 RVA: 0x001280AC File Offset: 0x001262AC
		[HarmonyPrefix]
		[HarmonyPatch("ApplyByJoinFactionAsMercenary")]
		public static bool Prefix_ApplyByJoinFactionAsMercenary(Clan clan, Kingdom newKingdom, CampaignTime shouldStayInKingdomUntil, int awardMultiplier, bool showNotification)
		{
			return PreventRejoiningPatch.\u206B\u206B\u202D\u200E\u206E\u206F\u200D\u206B\u200B\u206C\u202C\u206A\u202E\u200B\u206C\u206E\u202A\u202A\u202E\u202A\u202D\u200B\u200F\u200F\u206A\u206E\u202D\u200C\u200D\u206A\u200B\u200E\u200F\u202E\u202D\u202C\u206B\u200C\u200D\u202E\u202E(clan, newKingdom);
		}

		// Token: 0x06001464 RID: 5220 RVA: 0x001280E4 File Offset: 0x001262E4
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool \u206B\u206B\u202D\u200E\u206E\u206F\u200D\u206B\u200B\u206C\u202C\u206A\u202E\u200B\u206C\u206E\u202A\u202A\u202E\u202A\u202D\u200B\u200F\u200F\u206A\u206E\u202D\u200C\u200D\u206A\u200B\u200E\u200F\u202E\u202D\u202C\u206B\u200C\u200D\u202E\u202E(Clan A_0, Kingdom A_1)
		{
			if (A_0 != null)
			{
				goto IL_04;
			}
			bool flag = true;
			goto IL_59;
			int num2;
			bool result;
			for (;;)
			{
				IL_09:
				int num = num2;
				uint num3;
				string text;
				bool flag2;
				string text2;
				switch ((num3 = (uint)(~(uint)(~(uint)(~num * 2135983097)))) % 11U)
				{
				case 0U:
					result = true;
					num2 = (int)(num3 * 3213704480U ^ 2414584167U);
					continue;
				case 1U:
				{
					ExpulsionRecord expulsionRecord = ExpelledClanSystem.Instance.GetExpulsionRecord(A_1, A_0);
					if (expulsionRecord == null)
					{
						num2 = (int)(num3 * 3886198252U ^ 1941820073U);
						continue;
					}
					text = expulsionRecord.Reason;
					goto IL_123;
				}
				case 2U:
					goto IL_04;
				case 3U:
					result = false;
					num2 = -69455257;
					continue;
				case 4U:
					flag2 = (A_1 == \u200D\u206C\u200F\u206C\u202B\u202A\u200B\u202D\u202B\u200F\u206D\u200C\u202B\u200E\u206E\u200C\u202E\u200E\u202D\u202C\u202A\u206C\u200F\u200C\u202C\u202E\u200B\u202D\u206C\u202A\u202C\u200F\u200B\u206A\u200C\u206A\u202A\u202A\u206D\u202A\u202E.\u001F&\u009DUÈ(\u200C\u200C\u206C\u202D\u202A\u206A\u200E\u202A\u202E\u206C\u202D\u206D\u200F\u206A\u206C\u206E\u206D\u200C\u200E\u206C\u202C\u206F\u206F\u202E\u206C\u202A\u200B\u200E\u200F\u206A\u200C\u202B\u200F\u206D\u206C\u202C\u200C\u200C\u200F\u206E\u202E.íËØq\u00BD()));
					goto IL_88;
				case 6U:
					result = true;
					num2 = -69455257;
					continue;
				case 7U:
					goto IL_52;
				case 8U:
					\u200D\u202B\u206F\u202D\u200C\u206E\u200D\u206B\u200B\u200B\u206D\u200C\u200D\u206A\u200E\u200F\u202D\u206B\u202B\u200F\u206D\u200F\u206C\u200E\u206C\u206D\u202C\u202A\u202B\u200B\u200E\u202A\u202A\u200F\u202B\u200F\u206E\u202E\u206A\u200E\u202E.\u00A1\u0005d\u00B2\u00BC(\u206E\u202C\u202C\u200F\u202C\u206A\u200D\u206D\u206A\u202B\u206F\u206B\u200B\u200D\u202C\u206F\u206F\u206F\u206B\u206C\u206C\u206C\u202C\u200D\u202B\u206A\u200B\u206E\u200E\u206A\u202E\u206A\u202C\u200E\u206A\u200E\u206B\u202A\u206E\u206C\u202E.{\u0001\u009CG\u0085(\u206B\u202E\u206F\u206A\u200D\u206B\u202D\u202E\u206F\u200D\u206F\u200B\u206A\u206B\u206C\u200E\u202C\u202E\u206F\u200B\u206B\u206B\u200B\u200F\u200D\u200B\u202B\u200D\u202B\u202D\u206A\u206B\u200F\u202E\u206F\u206B\u206D\u202C\u206E\u200D\u202E.y=\u001E/\u0091(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(868230290), \u202C\u202C\u202A\u206B\u202C\u200C\u202C\u206E\u206A\u200D\u200B\u202D\u202A\u200D\u206F\u206C\u200F\u200B\u206F\u206F\u200B\u200B\u200D\u200B\u206A\u200D\u200D\u206C\u202E\u206D\u200F\u200B\u206F\u200D\u202E\u200B\u202A\u206A\u206A\u202D\u202E.ÅÍÌ\u0083\u001D(A_0), \u200D\u202A\u200C\u206D\u206B\u206B\u200C\u206E\u200C\u206D\u206A\u202D\u200B\u202B\u206C\u200C\u200E\u206B\u200E\u206A\u200B\u206A\u206C\u206F\u206B\u206D\u202D\u202D\u206A\u200F\u202D\u202C\u206B\u206E\u200D\u200D\u202A\u206B\u206F\u200E\u202E.z<Ä?c(A_1), text2), Colors.Red));
					num2 = 1847524558;
					continue;
				case 9U:
					num2 = (ExpelledClanSystem.Instance.IsClanBanned(A_1, A_0) ? 1429025613 : -763183557);
					continue;
				case 10U:
					text = null;
					goto IL_123;
				}
				break;
				IL_88:
				num2 = (flag2 ? 2124885136 : 1847524558);
				continue;
				IL_123:
				text2 = (text ?? <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1028266261));
				DiplomacyLogger.Instance.Log(\u206B\u202E\u206F\u206A\u200D\u206B\u202D\u202E\u206F\u200D\u206F\u200B\u206A\u206B\u206C\u200E\u202C\u202E\u206F\u200B\u206B\u206B\u200B\u200F\u200D\u200B\u202B\u200D\u202B\u202D\u206A\u206B\u200F\u202E\u206F\u206B\u206D\u202C\u206E\u200D\u202E.y=\u001E/\u0091(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(2020870710), \u202C\u202C\u202A\u206B\u202C\u200C\u202C\u206E\u206A\u200D\u200B\u202D\u202A\u200D\u206F\u206C\u200F\u200B\u206F\u206F\u200B\u200B\u200D\u200B\u206A\u200D\u200D\u206C\u202E\u206D\u200F\u200B\u206F\u200D\u202E\u200B\u202A\u206A\u206A\u202D\u202E.ÅÍÌ\u0083\u001D(A_0), \u200D\u202A\u200C\u206D\u206B\u206B\u200C\u206E\u200C\u206D\u206A\u202D\u200B\u202B\u206C\u200C\u200E\u206B\u200E\u206A\u200B\u206A\u206C\u206F\u206B\u206D\u202D\u202D\u206A\u200F\u202D\u202C\u206B\u206E\u200D\u200D\u202A\u206B\u206F\u200E\u202E.z<Ä?c(A_1), text2));
				if (A_0 == \u200C\u200C\u206C\u202D\u202A\u206A\u200E\u202A\u202E\u206C\u202D\u206D\u200F\u206A\u206C\u206E\u206D\u200C\u200E\u206C\u202C\u206F\u206F\u202E\u206C\u202A\u200B\u200E\u200F\u206A\u200C\u202B\u200F\u206D\u206C\u202C\u200C\u200C\u200F\u206E\u202E.íËØq\u00BD())
				{
					flag2 = true;
					goto IL_88;
				}
				num2 = -1211865234;
			}
			return result;
			IL_52:
			flag = (A_1 == null);
			goto IL_59;
			IL_04:
			num2 = 1682106101;
			goto IL_09;
			IL_59:
			num2 = (flag ? -77229465 : -1954816060);
			goto IL_09;
		}
	}
}
