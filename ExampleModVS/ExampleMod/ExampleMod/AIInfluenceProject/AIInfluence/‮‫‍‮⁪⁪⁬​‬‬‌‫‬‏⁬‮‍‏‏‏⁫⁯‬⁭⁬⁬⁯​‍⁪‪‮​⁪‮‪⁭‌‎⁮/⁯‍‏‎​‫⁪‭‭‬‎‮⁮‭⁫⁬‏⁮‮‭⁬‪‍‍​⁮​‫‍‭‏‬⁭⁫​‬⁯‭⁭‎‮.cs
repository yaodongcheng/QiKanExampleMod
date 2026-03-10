C\u202C\u202B\u202D\u200B\u206A\u206A\u202E\u206A\u200B\u202A\u202B\u206D\u202E\u200B\u200D\u206E\u206C\u206D\u200C\u202A\u206C\u206D\u202C\u202B\u202C\u200D\u202E \u0099\u008E5éÀ;
	}
}
﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AIInfluence.Behaviors.AIActions.TaskSystem;
using AIInfluence.Diplomacy;
using AIInfluence.Util;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;
using \u202E\u202B\u200D\u202E\u206A\u206A\u206C\u200B\u202C\u202C\u200C\u202B\u202C\u200F\u206C\u202E\u200D\u200F\u200F\u200F\u206B\u206F\u202C\u206D\u206C\u206C\u206F\u200B\u200D\u206A\u202A\u202E\u200B\u206A\u202E\u202A\u206D\u200C\u200E\u206E\u202E;

namespace AIInfluence.Behaviors.AIActions
{
	// Token: 0x02000308 RID: 776
	public sealed class SiegeSettlementAction : AIActionBase
	{
		// Token: 0x170003F6 RID: 1014
		// (get) Token: 0x060015CD RID: 5581 RVA: 0x0014BC6C File Offset: 0x00149E6C
		public override string ActionName
		{
			[MethodImpl(MethodImplOptions.NoInlining)]
			get
			{
				return <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1617932017);
			}
		}

		// Token: 0x170003F7 RID: 1015
		// (get) Token: 0x060015CE RID: 5582 RVA: 0x0014BC88 File Offset: 0x00149E88
		public override string Description
		{
			[MethodImpl(MethodImplOptions.NoInlining)]
			get
			{
				return <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(693329253);
			}
		}

		// Token: 0x060015CF RID: 5583 RVA: 0x0014BCA4 File Offset: 0x00149EA4
		public static bool PrepareSiegeTarge