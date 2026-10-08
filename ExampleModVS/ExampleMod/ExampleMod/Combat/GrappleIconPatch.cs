using System;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using HarmonyLib;
using TaleWorlds.MountAndBlade.View;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **钩索的"图标补丁"**（2026-10-08 立）—— 让**钩**这件物品在**背包 / 装备 / 交易界面**里显示成一枚真钩，
	/// 而**实机（真 mission）里一点都不变**（手里那枚钩仍由运行时实体负责、能绕小臂转）。
	///
	/// 🔴 为什么需要它（问题的形状）：
	///   一件物品的 **2D 图标**和它**在世界上长什么样**，在引擎里是**同一个字段**：
	///     · 箭类物品（我们的钩，`Type=Arrows`）的图标取 `holster_mesh` —— 而同一个 `holster_mesh`
	///       就是它挂在**身上鞘位**时的那件 ⇒ 直接把 `holster_mesh` 设成钩 = 图标有钩了，
	///       但**实机腰上/背上也会挂一枚钩**，跟手里那枚转的钩同框（用户明确不要）。
	///     · 弓类物品（绳那件）的图标取 `mesh` —— 同一个 `mesh` 就是**左手**那件。
	///   ⇒ 想要"图标是钩、实机不出现第二枚钩"，只能**在画图标那条链上换网格**（就是本补丁）。
	///
	/// 🔴 为什么可以这么做（关键事实，反编译 + 引擎日志实证）：
	///   界面的 2D 图标（列表格子 + 点开的大图）**和实机是两条不同的渲染链**：
	///     · 图标 = `TableauCacheManager` 拿物品网格**离屏渲染成一张贴图**（场景 `scn_item_tableau`，
	///       启动时就和 `scn_soldier` / `inventory_character_scene` 一起当 tableau 场景加载）；
	///     · 实机 = `AgentVisuals` 把装备网格挂到角色骨头上。
	///   本补丁打在 `ItemCollectionElementViewExtensions.GetItemMeshForInventory` 上 —— 反编译确认
	///   **全游戏只有 View.dll 里的 `TableauCacheManager` 调它**（三个调用点全在 tableau 方法里）
	///   ⇒ 打它 = 只影响图标，**碰不到实机**。
	///
	/// 行为：只对**钩那件物品**（`GrappleFirePatch.HookItemId`）生效，其余物品一行都不多做（返回 true 走原版）；
	///   解析不到网格、或任何异常 ⇒ 一律放行（引擎画它自己的代理，最多图标空白，**绝不崩**）。
	///   每次返回一份 `CreateCopy()` 的**新副本**（`AddItem` 会把网格交给场景，缓存实例共享会出事）。
	///
	/// 开关：`custom.grapple iconhook 0|1`（即时生效，重开背包界面刷新图标）。
	/// 只对内容包有意义 ⇒ 未装内容包时**不挂载**（`MySubModule` 的 contentPackOnly 清单）。
	/// </summary>
	[HarmonyPatch(typeof(ItemCollectionElementViewExtensions), "GetItemMeshForInventory")]
	internal static class GrappleIconPatch
	{
		/// <summary>画图标时替上去的网格（自造三爪钩，与运行时实体、`flying_mesh` 同一件）。</summary>
		public const string HookMeshName = "lwn_grapple_hook";

		/// <summary>补丁开关（`custom.grapple iconhook 0|1`）。</summary>
		public static bool Enabled = true;

		[HarmonyPrefix]
		private static bool Prefix(ItemRosterElement rosterElement, bool isFemale, ref MetaMesh __result)
		{
			try
			{
				if (!Enabled)
				{
					return true;
				}
				ItemObject item = rosterElement.EquipmentElement.Item;
				if (item == null || item.StringId != GrappleFirePatch.HookItemId)
				{
					return true;                  // 不是钩 → 原版行为
				}
				MetaMesh source = SpellWorld.ResolveMesh(HookMeshName);
				if (source == null)
				{
					DebugLogger.Log($"[Grapple] 图标补丁：网格 '{HookMeshName}' 解析不到 → 交回引擎（图标会是代理）");
					return true;
				}
				__result = source.CreateCopy();
				return false;
			}
			catch (Exception)
			{
				return true;                      // 出错退回原版（补丁永不成为新的崩溃源）
			}
		}
	}
}
