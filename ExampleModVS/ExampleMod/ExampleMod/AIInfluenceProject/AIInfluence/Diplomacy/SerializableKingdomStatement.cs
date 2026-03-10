2C\u206E\u206D\u206F\u206A\u200D\u206D\u206E\u206C\u202E\u206D\u206D\u200E\u206A\u200D\u206B\u206E\u206B\u206D\u206C\u206F\u206F\u206C\u200D\u206C\u206C\u202D\u206F\u200F\u206A\u206C\u202C\u206D\u206A\u202E ôgÍ@\u000E;
	}
}
﻿using System;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence.Behaviors.AIActions.TaskSystem
{
	// Token: 0x0200031C RID: 796
	public static class TaskStepExecutor
	{
		// Token: 0x06001670 RID: 5744 RVA: 0x001522BC File Offset: 0x001504BC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static void ExecuteNextTaskStep(Hero hero, TaskStep nextStep)
		{
			bool flag;
			if (hero == null)
			{
				flag = true;
				goto IL_5D;
			}
			IL_04:
			int num = 208397818;
			IL_09:
			int num2 = num;
			switch ((944412664 - -(num2 ^ ~(~1447346323) + (-1432409030 + -687046421 - (6114810 ^ -1955050693))) * -631008483) % 4)
			{
			case 1:
				flag = (nextStep == null);
				goto IL_5D;
			case 2:
				goto IL_04;
			case 3:
				return;
			}
			try
			{
				Settlement targetSettlement;
				int num3;
				Settlement targetSettlement2;
				Settlement targetSettlement3;
				switch (nextStep.StepType)
				{
				case TaskStepType.GoToSettlement:
				case TaskStepType.WaitInSettlement:
					IL_40C:
					targetSettlement = nextStep.GetTargetSettlement();
					num3 = ((targetSettlement != null) ? -30757069 : 1938389206);
					break;
				case TaskStepType.ReturnToPlayer:
					IL_254:
					AIActionManager.Instance.StartAction(hero, <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-433153296), false);
					num3 = -2018218517;
					break;
				case TaskStepType.FollowPlayer:
					IL_1AA:
					AIActionManager.Instance.StartAction(hero, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B