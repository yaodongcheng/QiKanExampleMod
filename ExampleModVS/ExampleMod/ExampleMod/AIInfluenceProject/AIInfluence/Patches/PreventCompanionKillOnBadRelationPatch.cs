using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace AIInfluence.Patches
{
	// Token: 0x020002D3 RID: 723
	[HarmonyPatch]
	public static class PreventCompanionKillOnBadRelationPatch
	{
		// Token: 0x0600144B RID: 5195 RVA: 0x00126FB0 File Offset: 0x001251B0
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static bool Prepare()
		{
			Type type = AccessTools.TypeByName(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1154760784));
			bool flag = type == null;
			bool result;
			if (flag)
			{
				result = false;
			}
			else
			{
				MethodInfo left = AccessTools.Method(type, <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1426364761), null, null);
				result = (left != null);
			}
			return result;
		}

		// Token: 0x0600144C RID: 5196 RVA: 0x00127000 File Offset: 0x00125200
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static MethodInfo TargetMethod()
		{
			return AccessTools.Method(AccessTools.TypeByName(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(402952629)), <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1021589125), null, null);
		}

		// Token: 0x0600144D RID: 5197 RVA: 0x00127038 File Offset: 0x00125238
		[HarmonyPrefix]
		public static bool Prefix(Hero effectiveHero, Hero effectiveHeroGainedRelationWith, int relationChange, bool showNotification, ChangeRelationAction.ChangeRelationDetail detail, Hero originalHero, Hero originalGainedRelationWith)
		{
			bool result;
			try
			{
				bool flag = (effectiveHero == Hero.MainHero && effectiveHeroGainedRelationWith.IsPlayerCompanion) || (effectiveHero.IsPlayerCompanion && effectiveHeroGainedRelationWith == Hero.MainHero);
				bool flag2 = !flag;
				if (flag2)
				{
					result = true;
				}
				else
				{
					bool flag3 = relationChange < 0 && effectiveHero.GetRelation(effectiveHeroGainedRelationWith) < -10;
					if (flag3)
					{
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
				result = true;
			}
			return result;
		}
	}
}
