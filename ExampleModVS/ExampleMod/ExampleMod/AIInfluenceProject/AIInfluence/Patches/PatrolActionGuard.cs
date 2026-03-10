using System;
using System.Runtime.CompilerServices;
using AIInfluence.Behaviors.AIActions;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace AIInfluence.Patches
{
	// Token: 0x020002D0 RID: 720
	internal static class PatrolActionGuard
	{
		// Token: 0x06001443 RID: 5187 RVA: 0x00126A4C File Offset: 0x00124C4C
		public static bool ShouldBlockSettlementOrder(MobileParty owner, Settlement settlement)
		{
			Hero hero = (owner != null) ? owner.LeaderHero : null;
			bool flag = !PatrolActionGuard.IsPatrolActionActive(hero);
			bool result;
			if (flag)
			{
				result = false;
			}
			else
			{
				string text;
				bool flag2 = !PatrolSettlementAction.TryGetActivePatrolTargetId(hero, out text) || string.IsNullOrWhiteSpace(text);
				if (flag2)
				{
					result = false;
				}
				else
				{
					bool flag3 = settlement != null && settlement.StringId == text;
					if (flag3)
					{
						result = false;
					}
					else
					{
						PatrolActionGuard.LogBlock(hero, owner, settlement, text, false);
						result = true;
					}
				}
			}
			return result;
		}

		// Token: 0x06001444 RID: 5188 RVA: 0x00126AC4 File Offset: 0x00124CC4
		public static bool ShouldBlockPointOrder(MobileParty owner)
		{
			Hero hero = (owner != null) ? owner.LeaderHero : null;
			bool flag = !PatrolActionGuard.IsPatrolActionActive(hero);
			bool result;
			if (flag)
			{
				result = false;
			}
			else
			{
				PatrolActionGuard.LogBlock(hero, owner, null, null, true);
				result = true;
			}
			return result;
		}

		// Token: 0x06001445 RID: 5189 RVA: 0x00126B04 File Offset: 0x00124D04
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool IsPatrolActionActive(Hero hero)
		{
			bool flag = hero == null;
			bool result;
			if (flag)
			{
				result = false;
			}
			else
			{
				AIActionManager instance = AIActionManager.Instance;
				result = (instance != null && instance.IsActionActive(hero, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(146277774)));
			}
			return result;
		}

		// Token: 0x06001446 RID: 5190 RVA: 0x00126B44 File Offset: 0x00124D44
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void LogBlock(Hero hero, MobileParty owner, Settlement newSettlement, string activeTargetId, bool isPoint)
		{
			string text;
			if (hero == null)
			{
				text = null;
			}
			else
			{
				TextObject name = hero.Name;
				text = ((name != null) ? name.ToString() : null);
			}
			string text2;
			if ((text2 = text) == null)
			{
				string text3;
				if (owner == null)
				{
					text3 = null;
				}
				else
				{
					TextObject name2 = owner.Name;
					text3 = ((name2 != null) ? name2.ToString() : null);
				}
				text2 = (text3 ?? <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(665038079));
			}
			string text4 = text2;
			string text5;
			if (owner == null)
			{
				text5 = null;
			}
			else
			{
				TextObject name3 = owner.Name;
				text5 = ((name3 != null) ? name3.ToString() : null);
			}
			string text6 = text5 ?? text4;
			string text7;
			if (newSettlement == null)
			{
				text7 = null;
			}
			else
			{
				TextObject name4 = newSettlement.Name;
				text7 = ((name4 != null) ? name4.ToString() : null);
			}
			string text8 = text7 ?? (isPoint ? <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-916597726) : <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1085054637));
			string message = isPoint ? string.Concat(new string[]
			{
				<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-251869200),
				text6,
				<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1219514938),
				text4,
				<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(439130705)
			}) : string.Concat(new string[]
			{
				<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-2054918317),
				text6,
				<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-331623597),
				text4,
				<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-355087943),
				text8,
				<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-671656761),
				activeTargetId,
				<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1072862733)
			});
			AIInfluenceBehavior instance = AIInfluenceBehavior.Instance;
			if (instance != null)
			{
				instance.LogMessage(message);
			}
		}
	}
}
