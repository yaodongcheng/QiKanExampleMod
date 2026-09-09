using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Core;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 战斗兵力战力计算（BattlePowerCalculationLogic.CalculateTeamPowers）空键防护（通用基座，
	/// 2026-09-09 雷 36 —— Taikou 遭遇战「攻击！」开战崩 KeyNotFoundException）。
	///
	/// 机制实锤（反编译 1.2.12 TaleWorlds.MountAndBlade.dll）：
	///   CalculateTeamPowers = 先把 Mission.Teams 全部按阵营 Add(team,0) 进字典，再遍历
	///   MissionAgentSpawnLogic.GetAllTroopsForSide 的每个兵源，取 Mission.GetAgentTeam(...)
	///   （非玩家方恒 = PlayerEnemyTeam）→ dictionary[agentTeam] += power ——
	///   若 PlayerEnemyTeam == null（TeamCollection.AdjustPlayerTeams：Player.IsEnemyOf(Defender)
	///   为 false 时归 null）→ dictionary[null] → KeyNotFoundException。
	///   触发链：部署阶段 Tactics 决策 → TeamQuerySystem 战力查询 → 惰性 Evaluate。
	///   自定义世界（玩家无王国/战争关系未成立等）易踩；原版攻防顺序天然成立关系。
	///
	/// 本补丁 = 替换原方法（前缀返回 false，完整重实现，逻辑与官方一致）+ 空键兜底：
	///   任何 GetAgentTeam 拿不到/没登记的队 → 先登记再从 0 累加（不崩）+ 日志记录
	///   缺失身份与战争状态（供根因排查）；实现段自身异常一律吞掉（战斗照常，重实现是兜底名）。
	/// 版本：类型+方法名字符串运行期解析（1.2.12 二进制 grep 命中）；改名 = 静默跳过，
	///   以 [BattlePowerGuard] 日志缺失即可察觉。
	/// </summary>
	[HarmonyPatch("TaleWorlds.MountAndBlade.BattlePowerCalculationLogic", "CalculateTeamPowers")]
	public static class BattlePowerCalculationGuardPatch
	{
		[HarmonyPrefix]
		private static bool Prefix(object __instance)
		{
			try
			{
				// ── 与官方同款的私有字段操作（反射，失败即走保险分支）──
				var sidePowerField = AccessTools.Field(__instance.GetType(), "_sidePowerData");
				// IsTeamPowersCalculated 的字段在下方幂等标记段一并解析（auto-property 有私有 backing field）

				Mission mission = Mission.Current;
				if (mission == null)
				{
					return false; // 无任务场景：不执行（原版同语义：直接返回）
				}

				Dictionary<Team, float>[] dicts = new Dictionary<Team, float>[2];
				for (int i = 0; i < 2; i++)
				{
					dicts[i] = new Dictionary<Team, float>();
				}

				foreach (Team team in mission.Teams)
				{
					int sideIndex = (int)team.Side;
					if (sideIndex >= 0 && sideIndex < 2)
					{
						if (!dicts[sideIndex].ContainsKey(team))
						{
							dicts[sideIndex].Add(team, 0f);
						}
					}
				}

				// 接口版跨版本可用（1.2.12/1.5.2 双验：GetMissionBehavior<T> 约束 = class,IMissionBehavior，接口同构
				// —— 1.2.12 类名 MissionAgentSpawnLogic 已在 1.5.x 合并为 DefaultBattleMissionAgentSpawnLogic，禁引用具体类）
				IMissionAgentSpawnLogic spawnLogic = mission.GetMissionBehavior<IMissionAgentSpawnLogic>();
				// GetAllTroopsForSide 不在各版本公共接口上（1.2.12 仅具体类有、1.5.2 移到 IBattleMissionAgentSpawnLogic）——反射调用
				MethodInfo getAllTroopsMethod = spawnLogic?.GetType().GetMethod("GetAllTroopsForSide", new[] { typeof(BattleSideEnum) });
				if (getAllTroopsMethod != null)
				{
					bool loggedOnce = false;
					for (int i = 0; i < 2; i++)
					{
						Dictionary<Team, float> dict = dicts[i];
						bool isPlayerSide = mission.PlayerTeam != null && mission.PlayerTeam.Side == (BattleSideEnum)i;
						foreach (IAgentOriginBase origin in (getAllTroopsMethod.Invoke(spawnLogic, new object[] { (BattleSideEnum)i }) as IEnumerable<IAgentOriginBase>) ?? Enumerable.Empty<IAgentOriginBase>())
						{
							Team agentTeam = Mission.GetAgentTeam(origin, isPlayerSide);
							if (agentTeam == null)
							{
								// 兜底根因日志 + 无队登记：直接计 0 跳过（保战斗）
								if (!loggedOnce)
								{
									loggedOnce = true;
									DebugLogger.Log($"[BattlePowerGuard] GetAgentTeam 返回 null（缺 PlayerEnemy/PlayerAlly 队）: " +
										$"side={i} playerSide={isPlayerSide} playerTeam={(mission.PlayerTeam?.ToString() ?? "null")} " +
										$"playerEnemy={mission.PlayerEnemyTeam?.ToString() ?? "null"} playerAlly={mission.PlayerAllyTeam?.ToString() ?? "null"} " +
										$"missionTeamCount={mission.Teams.Count}");
								}
								continue;
							}
							if (!dict.ContainsKey(agentTeam))
							{
								if (!loggedOnce)
								{
									loggedOnce = true;
									DebugLogger.Log($"[BattlePowerGuard] 战力字典缺队（补登记 0 战力）: team={agentTeam} " +
										$"side={i} playerSide={isPlayerSide} playerEnemy={mission.PlayerEnemyTeam?.ToString() ?? "null"} " +
										$"playerTeam={mission.PlayerTeam?.ToString() ?? "null"} missionTeamCount={mission.Teams.Count}");
								}
								dict.Add(agentTeam, 0f);
							}
							BasicCharacterObject troop = origin.Troop;
							dict[agentTeam] += troop?.GetPower() ?? 0f;
						}
					}
				}

				foreach (Team team in mission.Teams)
				{
					team.QuerySystem.Expire();
				}

				// 回写结果字典（GetTotalTeamPower 按 (int)team.Side 查 _sidePowerData —— 必须注入）
				sidePowerField?.SetValue(__instance, dicts);
				if (sidePowerField == null)
				{
					DebugLogger.Log("[BattlePowerGuard] _sidePowerData 字段注入失败——战力查询会被跳过（请报告版本）");
				}

				// 幂等标记：auto-property（get; private set;）——先走 SetValue（私有 setter 全信任可调），
				// 兜底显式 backing field 名（AccessTools.Field 对 "<X>k__BackingField" 的解析版本间不保证）；
				// 🔴 必须设上：GetTotalTeamPower 每个 QueryData 过期（5s）都查这个标记，设不上 = 每次重算 +
				//    每次重算记一行日志 = 「日志刷屏」根因（2026-09-09 实测日志证实）
				PropertyInfo flagsProp = __instance.GetType().GetProperty("IsTeamPowersCalculated", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				FieldInfo flagsField = AccessTools.Field(__instance.GetType(), "IsTeamPowersCalculated")
					?? AccessTools.Field(__instance.GetType(), "<IsTeamPowersCalculated>k__BackingField");
				if (flagsProp != null && flagsProp.CanWrite)
				{
					flagsProp.SetValue(__instance, true);
				}
				else
				{
					flagsField?.SetValue(__instance, true);
				}
				if (flagsProp == null && flagsField == null)
				{
					DebugLogger.Log("[BattlePowerGuard] 幂等标记字段注入失败——每次查询都会重算（请报告版本）");
				}
				return false; // 已完整替代原方法
			}
			catch (Exception ex)
			{
				// 兜底名：重实现段自身异常 → 放弃（返回 false 让调用方拿到"未计算"状态，战斗继续）
				DebugLogger.Log($"[BattlePowerGuard] 重实现段异常（不拦截，跳过战力计算）: {ex.Message}");
				return false;
			}
		}
	}
}
