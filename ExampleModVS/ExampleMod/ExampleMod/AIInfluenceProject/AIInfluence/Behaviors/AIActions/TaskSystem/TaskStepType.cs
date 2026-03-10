using System;

namespace AIInfluence.Behaviors.AIActions.TaskSystem
{
	// Token: 0x02000319 RID: 793
	public enum TaskStepType
	{
		// Token: 0x04000F9D RID: 3997
		GoToSettlement,
		// Token: 0x04000F9E RID: 3998
		WaitInSettlement,
		// Token: 0x04000F9F RID: 3999
		ReturnToPlayer,
		// Token: 0x04000FA0 RID: 4000
		FollowPlayer,
		// Token: 0x04000FA1 RID: 4001
		AttackParty,
		// Token: 0x04000FA2 RID: 4002
		SiegeSettlement,
		// Token: 0x04000FA3 RID: 4003
		PatrolSettlement,
		// Token: 0x04000FA4 RID: 4004
		WaitNearSettlement,
		// Token: 0x04000FA5 RID: 4005
		RaidVillage,
		// Token: 0x04000FA6 RID: 4006
		Custom
	}
}
