using System;
using HarmonyLib;
using TaleWorlds.GauntletUI.BaseTypes;

namespace AIInfluence.Patches
{
	// Token: 0x020002CD RID: 717
	[HarmonyPatch(typeof(EditableTextWidget), "get_MaxLength")]
	public static class EditableTextWidgetMaxLengthPatch
	{
		// Token: 0x06001440 RID: 5184 RVA: 0x001269EC File Offset: 0x00124BEC
		[HarmonyPostfix]
		public static void Postfix(ref int __result)
		{
			bool flag = __result <= 512;
			if (flag)
			{
				__result = 2048;
			}
		}

		// Token: 0x04000EB6 RID: 3766
		private const int NewMaxLength = 2048;

		// Token: 0x04000EB7 RID: 3767
		private const int OldMaxLength = 512;
	}
}
