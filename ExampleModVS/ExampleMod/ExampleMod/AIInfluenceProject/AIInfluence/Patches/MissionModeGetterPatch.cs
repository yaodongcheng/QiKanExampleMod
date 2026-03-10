using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using AIInfluence.SettlementCombat;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace AIInfluence.Patches
{
	// Token: 0x020002D7 RID: 727
	[HarmonyPatch(typeof(Mission), "Mode", MethodType.Getter)]
	public static class MissionModeGetterPatch
	{
		// Token: 0x06001459 RID: 5209 RVA: 0x00127960 File Offset: 0x00125B60
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool Prefix(Mission __instance, ref MissionMode __result)
		{
			bool result;
			try
			{
				MethodInfo left = AccessTools.PropertyGetter(typeof(Mission), <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-352851672));
				bool flag = left == null;
				if (flag)
				{
					result = true;
				}
				else
				{
					FieldInfo fieldInfo = AccessTools.Field(typeof(Mission), <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-139877814));
					bool flag2 = fieldInfo == null;
					if (flag2)
					{
						result = true;
					}
					else
					{
						MissionMode missionMode = (MissionMode)fieldInfo.GetValue(__instance);
						bool flag3 = ((__instance != null) ? __instance.PlayerTeam : null) != null && __instance.PlayerTeam.IsPlayerGeneral;
						if (flag3)
						{
							PlayerReinforcementMissionLogic missionBehavior = __instance.GetMissionBehavior<PlayerReinforcementMissionLogic>();
							bool flag4 = missionBehavior != null && missionBehavior.HasActiveSummonedTroops();
							if (flag4)
							{
								__result = MissionMode.Battle;
								return false;
							}
						}
						__result = missionMode;
						result = true;
					}
				}
			}
			catch (Exception ex)
			{
				MissionModeGetterPatch._logger.LogError(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1705358548), ex.Message, ex);
				result = true;
			}
			return result;
		}

		// Token: 0x04000EBE RID: 3774
		private static readonly SettlementCombatLogger _logger = SettlementCombatLogger.Instance;
	}
}
