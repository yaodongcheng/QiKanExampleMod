using System;
using AIInfluence.Behaviors.DeathHistory;
using HarmonyLib;
using MCM.Abstractions.Base.Global;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace AIInfluence.Patches
{
	// Token: 0x020002CA RID: 714
	[HarmonyPatch(typeof(CampaignEventDispatcher), "OnBeforeMainCharacterDied")]
	public static class PlayerDeathPatch
	{
		// Token: 0x0600143C RID: 5180 RVA: 0x00126660 File Offset: 0x00124860
		[HarmonyPrefix]
		public static bool Prefix(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
		{
			try
			{
				bool flag = victim != Hero.MainHero;
				if (flag)
				{
					return true;
				}
				bool bypassPlayerDeathPatch = PlayerDeathPatch.BypassPlayerDeathPatch;
				if (bypassPlayerDeathPatch)
				{
					return true;
				}
				bool flag2 = GlobalSettings<ModSettings>.Instance == null || !GlobalSettings<ModSettings>.Instance.EnableModification;
				if (flag2)
				{
					return true;
				}
				Campaign campaign = Campaign.Current;
				DeathHistoryBehavior deathHistoryBehavior = (campaign != null) ? campaign.GetCampaignBehavior<DeathHistoryBehavior>() : null;
				bool flag3 = deathHistoryBehavior != null;
				if (flag3)
				{
					Hero savedVictim = victim;
					Hero savedKiller = killer;
					KillCharacterAction.KillCharacterActionDetail savedDetail = detail;
					bool savedNotification = showNotification;
					deathHistoryBehavior.ShowDeathHistoryInquiry(victim, true, delegate
					{
						PlayerDeathPatch.BypassPlayerDeathPatch = true;
						try
						{
							CampaignEventDispatcher.Instance.OnBeforeMainCharacterDied(savedVictim, savedKiller, savedDetail, savedNotification);
						}
						finally
						{
							PlayerDeathPatch.BypassPlayerDeathPatch = false;
						}
					});
					return false;
				}
			}
			catch (Exception)
			{
				return true;
			}
			return true;
		}

		// Token: 0x04000EB1 RID: 3761
		public static bool BypassPlayerDeathPatch;
	}
}
