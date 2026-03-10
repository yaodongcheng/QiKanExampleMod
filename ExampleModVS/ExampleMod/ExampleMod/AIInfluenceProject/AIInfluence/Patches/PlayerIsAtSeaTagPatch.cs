using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace AIInfluence.Patches
{
	// Token: 0x020002D1 RID: 721
	[HarmonyPatch]
	public static class PlayerIsAtSeaTagPatch
	{
		// Token: 0x06001447 RID: 5191 RVA: 0x00126CB0 File Offset: 0x00124EB0
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static bool Prepare()
		{
			Type type = AccessTools.TypeByName(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1882027542));
			bool flag = type == null;
			bool result;
			if (flag)
			{
				result = false;
			}
			else
			{
				MethodInfo left = AccessTools.Method(type, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1004806266), null, null);
				result = (left != null);
			}
			return result;
		}

		// Token: 0x06001448 RID: 5192 RVA: 0x00126D00 File Offset: 0x00124F00
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static MethodInfo TargetMethod()
		{
			return AccessTools.Method(AccessTools.TypeByName(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1136146727)), <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(683117075), null, null);
		}

		// Token: 0x06001449 RID: 5193 RVA: 0x00126D38 File Offset: 0x00124F38
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static bool Prefix(ref bool __result)
		{
			bool result;
			try
			{
				bool flag = Hero.MainHero == null;
				if (flag)
				{
					__result = false;
					result = false;
				}
				else
				{
					Hero mainHero = Hero.MainHero;
					bool isPrisoner = mainHero.IsPrisoner;
					MobileParty mobileParty;
					if (isPrisoner)
					{
						PartyBase partyBelongedToAsPrisoner = mainHero.PartyBelongedToAsPrisoner;
						mobileParty = ((partyBelongedToAsPrisoner != null) ? partyBelongedToAsPrisoner.MobileParty : null);
					}
					else
					{
						mobileParty = mainHero.PartyBelongedTo;
					}
					bool flag2 = mobileParty == null;
					if (flag2)
					{
						__result = false;
						result = false;
					}
					else
					{
						result = true;
					}
				}
			}
			catch (Exception ex)
			{
				InformationManager.DisplayMessage(new InformationMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(2058822435) + ex.Message));
				__result = false;
				result = false;
			}
			return result;
		}
	}
}
