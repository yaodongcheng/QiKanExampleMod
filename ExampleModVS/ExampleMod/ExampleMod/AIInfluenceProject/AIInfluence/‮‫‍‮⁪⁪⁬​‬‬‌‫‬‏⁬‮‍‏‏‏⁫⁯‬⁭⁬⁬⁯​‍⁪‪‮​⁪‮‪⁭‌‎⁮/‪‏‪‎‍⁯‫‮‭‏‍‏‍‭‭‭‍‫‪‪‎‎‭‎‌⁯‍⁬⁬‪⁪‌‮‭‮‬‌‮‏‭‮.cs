using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AIInfluence.Behaviors.AIActions.TaskSystem;
using AIInfluence.Diplomacy;
using AIInfluence.Util;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;
using \u202E\u202B\u200D\u202E\u206A\u206A\u206C\u200B\u202C\u202C\u200C\u202B\u202C\u200F\u206C\u202E\u200D\u200F\u200F\u200F\u206B\u206F\u202C\u206D\u206C\u206C\u206F\u200B\u200D\u206A\u202A\u202E\u200B\u206A\u202E\u202A\u206D\u200C\u200E\u206E\u202E;

namespace AIInfluence.Behaviors.AIActions
{
	// Token: 0x02000301 RID: 769
	public sealed class RaidVillageAction : AIActionBase
	{
		// Token: 0x170003F2 RID: 1010
		// (get) Token: 0x0600159E RID: 5534 RVA: 0x00149378 File Offset: 0x00147578
		public override string ActionName
		{
			[MethodImpl(MethodImplOptions.NoInlining)]
			get
			{
				return <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-481678347);
			}
		}

		// Token: 0x170003F3 RID: 1011
		// (get) Token: 0x0600159F RID: 5535 RVA: 0x00149394 File Offset: 0x00147594
		public override string Description
		{
			[MethodImpl(MethodImplOptions.NoInlining)]
			get
			{
				return <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-236017057);
			}
		}

		// Token: 0x060015A0 RID: 5536 RVA: 0x001493B0 File Offset: 0x001475B0
		public static bool PrepareRaidTarget(Hero hero, string settlementId)
		{
			if (hero != null)
			{
				goto IL_07;
			}
			bool flag = true;
			goto IL_9F;
			int num2;
			bool result;
			for (;;)
			{
				IL_0C:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(~(~(-2127115583 + -1874248714 ^ -835892276) - num) - 505754709 * 1220395944 - 1036504233)) % 5U)
				{
				case 0U:
					goto IL_07;
			