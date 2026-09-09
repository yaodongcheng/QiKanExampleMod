using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 马出生点空物品兜底（通用基座，2026-09-09 雷 41 —— 太阁世界进城 TownCenter 崩溃）。
	///
	/// 机制（反编译 1.2.12 SandBox.dll MissionAgentHandler.SpawnHorses + 场景/prefab 实锤）：
	///   SpawnHorses 扫任务场景 FindEntitiesWithTag("sp_horse")，取实体 Tags[1]（= 物品 id）
	///   → MBObjectManager.GetObject&lt;ItemObject&gt; → new ItemRosterElement(...)。自定义世界
	///   （GameType 过滤）下官方马物品未装载 → GetObject = null → ItemRosterElement(null) NRE。
	///   官方 town 场景（empire_town_a 等）内 sp_horse_* prefab 就是引用这些物品（1.2.12 Native
	///   Prefabs/editor_spawnpoints.xml 实锤：7 个 sp_horse prefab = 6 种马 + 1 重复）。
	///   织丰对照：织丰用自有场景 + 自有 sp_horse_kiso prefab + 自家马物品 → 从不踩；我们用官方场景 = 踩。
	/// 本补丁 = 前缀替换（完整重实现原逻辑）+ 三层空保护：Tags 缺项 / 物品不存在 / 场景无 ——
	///   物品不存在 = 跳过该出生点并打 [HorseSpawnGuard] 日志（不崩；马匹缺失属可接受降级）。
	/// 版本：类型+方法名字符串运行期解析（1.2.12 = MissionAgentHandler.SpawnHorses；
	///   1.5.2 = SandBoxHelpers.MissionHelper.SpawnHorses）；选中版本命中，其他版本静默跳过。
	/// </summary>
	public static class HorseSpawnNullGuardPatch
	{
		[HarmonyPatch("SandBox.Missions.MissionLogics.MissionAgentHandler", "SpawnHorses")]
		public static class Patch1212
		{
			[HarmonyPrefix]
			private static bool Prefix(ref List<Agent> __result)
			{
				__result = SpawnHorsesSafe();
				return false;
			}
		}

		[HarmonyPatch("SandBox.SandBoxHelpers.MissionHelper", "SpawnHorses")]
		public static class Patch150
		{
			[HarmonyPrefix]
			private static bool Prefix(ref List<Agent> __result)
			{
				__result = SpawnHorsesSafe();
				return false;
			}
		}

		private static MethodInfo _flagsSetter;    // AnimalSpawnSettings.CheckAndSetAnimalAgentFlags(GameEntity, Agent)
		private static MethodInfo _animSimulator;  // MissionAgentHandler.SimulateAnimalAnimations(Agent)
		private static bool _lookedUpHelpers;

		private static void EnsureHelpers()
		{
			if (_lookedUpHelpers)
			{
				return;
			}
			_lookedUpHelpers = true;
			foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				Type[] types;
				try
				{
					types = asm.GetTypes();
				}
				catch (ReflectionTypeLoadException)
				{
					continue;
				}
				foreach (Type t in types)
				{
					if (_flagsSetter == null && t.Name == "AnimalSpawnSettings")
					{
						_flagsSetter = t.GetMethod("CheckAndSetAnimalAgentFlags",
							BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
							null, new[] { typeof(GameEntity), typeof(Agent) }, null);
					}
					if (_animSimulator == null && (t.Name == "MissionAgentHandler" || t.Name == "MissionHelper"))
					{
						_animSimulator = t.GetMethod("SimulateAnimalAnimations",
							BindingFlags.Static | BindingFlags.NonPublic,
							null, new[] { typeof(Agent) }, null);
					}
				}
			}
			DebugLogger.Log($"[HorseSpawnGuard] 动画/旗标注入辅助解析: CheckAndSetAnimalAgentFlags={(_flagsSetter != null)} SimulateAnimalAnimations={(_animSimulator != null)}");
		}

		private static List<Agent> SpawnHorsesSafe()
		{
			var result = new List<Agent>();
			try
			{
				Mission mission = Mission.Current;
				if (mission == null || mission.Scene == null)
				{
					return result;
				}
				EnsureHelpers();
				foreach (GameEntity item in mission.Scene.FindEntitiesWithTag("sp_horse"))
				{
					string objectName = item.Tags.Count > 1 ? item.Tags[1] : null;
					ItemObject itemObject = objectName == null
						? null
						: MBObjectManager.Instance.GetObject<ItemObject>(objectName);
					if (itemObject == null)
					{
						DebugLogger.Log($"[HorseSpawnGuard] 跳过马出生点（物品未装载）: 实体={item?.Name ?? "?"} " +
							$"tags=[{string.Join("/", item.Tags)}] 引用物品={objectName ?? "(null)"} —— 自定义世界物品库缺该物品，马匹降级缺失");
						continue;
					}
					if (!itemObject.HasHorseComponent)
					{
						continue;
					}
					MatrixFrame globalFrame = item.GetGlobalFrame();
					globalFrame.rotation.OrthonormalizeAccordingToForwardAndKeepUpAsZAxis();
					var rosterElement = new ItemRosterElement(itemObject, 1);
					ItemRosterElement harnessRosterElement = default;
					ref Vec3 origin = ref globalFrame.origin;
					Vec2 initialDirection = globalFrame.rotation.f.AsVec2;
#if MB2_GE_140
					Agent agent = mission.SpawnMonster(rosterElement, harnessRosterElement, in origin, in initialDirection, -1);
#else
					Agent agent = mission.SpawnMonster(rosterElement, harnessRosterElement, in origin, in initialDirection);
#endif
					try
					{
						_flagsSetter?.Invoke(null, new object[] { item, agent });
						_animSimulator?.Invoke(null, new object[] { agent });
					}
					catch (Exception)
					{
						// 旗标/动画注入失败 = 纯表现层降级（马无 wander 旗标/少几帧预演），不拦
					}
					result.Add(agent);
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[HorseSpawnGuard] 兜底实现异常（已吞，马匹缺失继续）: {ex.Message}");
			}
			return result;
		}
	}
}
