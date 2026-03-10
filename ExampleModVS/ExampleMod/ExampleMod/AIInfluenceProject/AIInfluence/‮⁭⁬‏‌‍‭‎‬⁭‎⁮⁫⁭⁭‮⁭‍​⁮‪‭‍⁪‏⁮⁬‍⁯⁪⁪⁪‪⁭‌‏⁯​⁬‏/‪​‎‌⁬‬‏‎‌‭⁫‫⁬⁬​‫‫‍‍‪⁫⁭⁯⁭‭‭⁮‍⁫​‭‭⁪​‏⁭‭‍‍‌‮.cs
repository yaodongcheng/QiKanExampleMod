0D\u200D\u206B\u202B\u200E\u200F\u202E \u008FÁi=K;
	}
}
﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence.Behaviors.AIActions.TaskSystem
{
	// Token: 0x02000316 RID: 790
	public class TaskBuilder
	{
		// Token: 0x06001626 RID: 5670 RVA: 0x0015045C File Offset: 0x0014E65C
		public TaskBuilder(Hero A_1)
		{
			this.\u206F\u202D\u206F\u202E\u200C\u202E\u200C\u200B\u202C\u200C\u206D\u202D\u202A\u200F\u200E\u202E\u202E\u202E\u202E\u200C\u206B\u202C\u206C\u200F\u202E\u202D\u202E\u206D\u202C\u200D\u200C\u206F\u206C\u206E\u200B\u200C\u206C\u200D\u202C\u206B\u202E = A_1;
			this.\u202E\u206A\u200C\u206C\u202B\u206B\u206B\u200F\u200D\u202C\u200F\u206F\u202E\u202E\u200B\u206F\u202C\u200D\u200D\u200D\u202D\u202D\u206D\u200E\u200B\u200D\u200F\u200E\u200C\u202E\u202E\u202C\u206A\u202D\u202C\u200D\u202B\u202A\u200E\u206D\u202E = new List<TaskStep>();
		}

		// Token: 0x06001627 RID: 5671 RVA: 0x00150484 File Offset: 0x0014E684
		public TaskBuilder WithDescription(string description)
		{
			this.\u200C\u206E\u200B\u206E\u202D\u202C\u206B\u206B\u206C\u206F\u202D\u206E\u206E\u202B\u206B\u200B\u202A\u206E\u202A\u200C\u202A\u202E\u202C\u202A\u206F\u200F\u200E\u202E\u202B\u206F\u200C\u202D\u206D\u200F\u202A\u202C\u202E\u206F\u200E\u202B\u202E = description;
			return this;
		}

		// Token: 0x06001628 RID: 5672 RVA: 0x001504A0 File Offset: 0x0014E6A0
		public TaskBuilder GoToSettlement(Settlement settlement, string description = null)
		{
			this.\u202E\u206A\u200C\u206C\u202B\u206B\u206B\u200F\u200D\u202C\u200F\u206F\u202E\u202E\u200B\u206F\u202C\u200D\u200D\u200D\u202D\u202D\u206D\u200E\u200B\u200D\u200F\u200E\u200C\u202E\u202E\u202C\u206A\u202D\u202C\u200D\u202B\u202A\u200E\u206D\u202E.Add(TaskStep.CreateGoToSettlement(settlement, description));
			return this;
		}

		// Token: 0x06001629 RID: 5673 RVA: 0x001504C8 File Offset: 0x0014E6C8
		public TaskBuilder WaitInSettlement(Settlement settlement, float waitDays, string description = null)
		{
			this.\u202E\u206A\u200C\u206C\u202B\u206B\u206B\u200F\u200D\u202C\u200F\u206F\u202E\u202E\u200B\u206F\u202C\u200D\u200D\u200D\u202D\u202D\u206D\u200E\u200B\u200D\u200F\u200E\u200C\u202E\u202E\u202C\u206A\u202D\u202C\u200D\u202B\u202A\u200E\u206D\u202E.Add(TaskStep.CreateWaitInSettlement(settlement, waitDays, description));
			return this;
		}

		// Token: 0x0600162A RID: 5674 RVA: 0x001504F0 File Offset: 0x0014E6F0
		public TaskBuilder ReturnToPlayer(string description = null)
		{
			this.\u202E\u206A\u200C\u206C\u202B\u206B\u206B\u200F\u200D\u202C\u200F\u206F\u202E\u202E\u200B\u206F\u202C\u200D\u200D\u200D\u202D\u202D\u206D\u200E\u200B\u200D\u200F\u200E\u200C\u202E\u202E\u202C\u206A\u202D\u202C\u200D\u202B\u202A\u200E\u206D\u202E.Add(TaskStep.CreateReturnToPlayer(description));
			return this;
		}

		// Token: 0x0600162B RID: 5675 RVA: 0x00150518 File Offset: 0x0014E718
		public TaskBuilder FollowPlayer(string description = null)
		{
			this.\u202E\u206A\u200C\u206C\u202B\u206B\u206B\u200F\u200D\u202C\u200F\u206F\u202E\u202E\u200B\u206F\u202C\u200D\u200D\u200D\u202D\u202D\u206D\u200E\u200B\u200D\u200F\u200E\u200C\u202E\u202E\u202C\u206A\u202D\u202C\u200D\u202B\u202A\u200E\u206D\u202E.Add(TaskStep.CreateFollowPlayer(description));
			return this;
		}

		// 