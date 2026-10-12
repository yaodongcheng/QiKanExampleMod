using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem.ViewModelCollection.Inventory;
using TaleWorlds.Core;

namespace LivingWorldNpcs
{
	// 🪦🪦 **本补丁已于 2026-10-12 晚停用（csproj 里已注释掉，不要重新启用）** —— 实机事故：
	//    在**装备界面**里直接写 `Equipment` 的槽位（哪怕是"只置换同一批槽位里的元素"），会跟 `InventoryLogic`
	//    的内部状态**脱钩**（界面上的槽位 VM 还指着原来的物品）⇒ 症状 = **箭和钩爪再也脱不下来**（用户 2026-10-12 报）。
	//    ⇒ 顺序归一化改到**进战场后一次性**做：`Combat/GrappleLogic.cs` 的 `NormalizeAmmoOrderOncePerMission()`
	//      （同样的规则、同样的判据，但没有任何界面在看着那份装备，零 UI 风险）。
	//    本文件留作**事故记录**与"规则/判据"的参考实现，别编译它。

	/// <summary>
	/// 🪦 **已停用**（见上面的事故记录）。原本的作用：装备界面里把「钩爪」与「其它箭类」的槽位顺序排对。
	/// 现在这份逻辑（含"方向由装备决定"的规则）已搬到 `GrappleLogic.NormalizeAmmoOrderOncePerMission()`。
	///
	/// 【原设计说明，保留备查】
	/// **为什么需要**（用户实机实测的结论，2026-10-12）：
	///   弓的弹药是**开火那一刻由引擎现挑**的（托管侧只有 `MissionEquipment.HasAmmo` 的"有没有"查询，
	///   挑槽在 native；弩才有装填绑定 `CheckLoadedAmmos`），而规则 = **按槽位顺序取第一个同类弹药**
	///   （`HasAmmo` 也是从 `WeaponItemBeginSlot` 往后扫）。实测三条：
	///     · 弓(W1) + 箭(W2) + 钩(W3) ⇒ **先把箭全射完**，轮到钩时被我们的瞄准闸拦下（+提示）；
	///     · 箭/钩**对调** ⇒ **一上来就打不出去**；
	///   ⇒ **排对顺序**就能控制"弓先用谁"：**要钩的那把（绳）在前就钩优先、别的弓在前就箭优先**。
	///   🔴 方向**由装备决定**（2026-10-12 用户实机纠正过一次：只装绳、没装别的弓时也要"钩在前"）：
	///     · 装备里有**绳**、且**没有别的远程武器** ⇒ 钩爪排**最前**（绳要用钩）；
	///     · 其余（有别的弓/弩）⇒ 钩爪排**最后**（让别的远程武器优先用箭）。
	///
	/// **挂哪几个时机**（2026-10-12 用户要求把"开/关界面"也覆盖上）：
	///   · `AfterTransfer` —— 原版**自己就在这个方法里**做同款规矩（"没马就自动脱挽具"，1.2.12 反编译 L3281-3287）
	///     ⇒ 我们同位置做顺序归一化（**每次装备动作之后**）；
	///   · `RefreshValues` —— 界面**打开**（以及每次刷新）时补一次；
	///   · `OnFinalize` —— 界面**关闭**时再兜一次。
	///   🔴 用 `TargetMethods()`（而不是三个字符串目标）：某个版本里方法改名/消失时**静默跳过**那个时机，
	///   不会像字符串目标那样让 `PatchAll` 当场抛异常、把整个 mod 拖半死（项目铁律）。
	///
	/// 🔴 **只置换"同一批槽位里的元素顺序"**（不增不减、不动别的物品）：`Equipment` 的索引器**有 setter**
	/// （1.2.12 实证）⇒ 把"钩 + 其它箭"这批槽位（只扫 0-3 号主武器槽）的元素读出来、按目标顺序重排、写回同一批槽位。
	/// 🔴 改完**立刻推回英雄**（照抄原版：`MainCharacter.SetEquipment(eq)`）。
	/// 🔴 只在"**钩爪 + 至少一个其它箭类**同框"且顺序**确实不符合上面那条目标**时才动手。
	/// ⚠️ 已知边界（**待根治**）：**普通弓 + 绳 + 钩 + 其他箭 四件同框**时，两把"弓"的诉求天然冲突
	///   （普通弓要箭在前、绳要钩在前），而弹药顺序只有一份 ⇒ 按**"钩在最后"**处理
	///   （选它是因"弓能正常射箭"比"绳先出钩"更要紧），此时**绳会先用箭**（射完才轮到钩）。
	///   根治 = **"绳的开火永远出钩索"**（把 `GrappleFirePatch` 判据放宽成"武器是绳即拦"，并退还这一发消耗的弹药）
	///   —— **未做**，见 plan §十六 决定 9。
	/// </summary>
	[HarmonyPatch]
	internal static class GrappleAmmoOrderPatch
	{
		private static PropertyInfo _activeEquipmentProp;
		private static FieldInfo _dirtyField;
		private static bool _reflectionTried;
		private static bool _errorLogged;

		/// <summary>要挂的三个时机（见类注释）。拿不到的**跳过并记一行**，绝不让 PatchAll 抛异常。</summary>
		private static IEnumerable<MethodBase> TargetMethods()
		{
			Type t = typeof(SPInventoryVM);
			string[] names = { "AfterTransfer", "RefreshValues", "OnFinalize" };
			for (int i = 0; i < names.Length; i++)
			{
				MethodBase m = AccessTools.Method(t, names[i]);
				if (m != null)
				{
					yield return m;
				}
				else
				{
					DebugLogger.Log($"[Grapple] 装备顺序归一化：SPInventoryVM.{names[i]} 在本版本不存在 ⇒ 该时机跳过（其余照常）");
				}
			}
		}

		[HarmonyPostfix]
		private static void Postfix(SPInventoryVM __instance)
		{
			try
			{
				if (__instance == null)
				{
					return;
				}
				EnsureReflection(__instance);
				if (_activeEquipmentProp == null)
				{
					return;
				}
				Equipment eq = _activeEquipmentProp.GetValue(__instance) as Equipment;
				if (eq == null)
				{
					return;
				}
				if (!ReorderAmmo(eq))
				{
					return;      // 没这个组合 / 顺序本来就对 ⇒ 一行都不多做
				}
				// 立刻推回英雄（照抄原版 RefreshInformationValues 的做法）—— 不依赖"置脏后等刷新"
				try
				{
					__instance.MainCharacter?.SetEquipment(eq);
				}
				catch (Exception)
				{
				}
				// 顺手置脏（原版自己那套；无害）
				try
				{
					_dirtyField?.SetValue(__instance, true);
				}
				catch (Exception)
				{
				}
			}
			catch (Exception ex)
			{
				if (!_errorLogged)
				{
					_errorLogged = true;
					DebugLogger.Log($"[Grapple] 装备顺序归一化异常（忽略，界面照常）：{ex.GetType().Name} {ex.Message}");
				}
			}
		}

		/// <summary>反射取两个私有成员（只试一次；拿不到 = 功能静默不生效，绝不影响装备界面）。</summary>
		private static void EnsureReflection(SPInventoryVM vm)
		{
			if (_reflectionTried)
			{
				return;
			}
			_reflectionTried = true;
			try
			{
				_activeEquipmentProp = AccessTools.Property(vm.GetType(), "ActiveEquipment");
				_dirtyField = AccessTools.Field(vm.GetType(), "_isCharacterEquipmentDirty");
			}
			catch (Exception)
			{
			}
			if (_activeEquipmentProp == null)
			{
				DebugLogger.Log("[Grapple] 装备顺序归一化：拿不到 SPInventoryVM.ActiveEquipment ⇒ 本功能不生效（其余不受影响）");
			}
		}

		/// <summary>
		/// 把"钩 + 其它箭类弹药"这批槽位里的元素重排成目标顺序。
		///
		/// **目标顺序由装备决定**（2026-10-12 用户实机纠正：只装绳、没装别的弓时，钩必须排**最前**）：
		///   · **装备里有绳、且没有别的远程武器（弓/弩）** ⇒ 钩爪排**最前** —— 绳就是"要用钩的那把弓"，
		///     这是钩索本体该有的样子（否则绳会先去射箭，钩索用不出来）；
		///   · **其余情况**（有别的弓/弩）⇒ 钩爪排**最后** —— 让别的远程武器优先用箭
		///     （四件同框"弓 + 绳 + 钩 + 箭"也走这条：**"弓能正常射箭"比"绳先出钩"更要紧**；
		///      绳那条留待"绳的开火永远出钩索"来根治，见 plan §十六 决定 9）。
		/// 返回 true = 真的改了顺序（调用方据此推回英雄）；false = 没这个组合 / 顺序本来就对。
		/// </summary>
		private static bool ReorderAmmo(Equipment eq)
		{
			int slotBegin = (int)EquipmentIndex.WeaponItemBeginSlot;
			int slotEnd = (int)EquipmentIndex.NumPrimaryWeaponSlots;      // 只看四个主武器槽（弹药只可能在这几个）

			List<int> ammoSlots = new List<int>();      // 钩 + 其它箭，所在的槽位
			List<EquipmentElement> arrows = new List<EquipmentElement>();
			EquipmentElement hook = default(EquipmentElement);
			bool hasHook = false;
			int hookSlot = -1;
			bool ropePresent = false;
			bool otherRangedPresent = false;

			for (int i = slotBegin; i < slotEnd; i++)
			{
				EquipmentElement e = eq[i];
				if (e.Item == null)
				{
					continue;
				}
				bool isHook = e.Item.StringId == GrappleFirePatch.HookItemId;
				if (e.Item.StringId == GrappleFirePatch.RopeItemId)
				{
					ropePresent = true;
				}
				else if (!isHook
					&& (e.Item.Type == ItemObject.ItemTypeEnum.Bow || e.Item.Type == ItemObject.ItemTypeEnum.Crossbow))
				{
					otherRangedPresent = true;
				}

				if (isHook)
				{
					hasHook = true;
					hook = e;
					hookSlot = i;
					ammoSlots.Add(i);
				}
				else if (e.Item.Type == ItemObject.ItemTypeEnum.Arrows)
				{
					arrows.Add(e);
					ammoSlots.Add(i);
				}
			}

			if (!hasHook || arrows.Count == 0 || ammoSlots.Count != arrows.Count + 1)
			{
				return false;      // 没有"钩 + 至少一个其它箭"同框 ⇒ 不动
			}

			// 目标：绳要用钩（钩在前）；有别的远程武器就让位（钩在后）
			bool hookFirst = ropePresent && !otherRangedPresent;

			// 顺序已经对了吗？
			bool ok = true;
			for (int i = 0; i < ammoSlots.Count; i++)
			{
				if (ammoSlots[i] == hookSlot)
				{
					continue;
				}
				bool arrowBeforeHook = ammoSlots[i] < hookSlot;
				if (arrowBeforeHook == hookFirst)      // 钩在前却还有箭在它前面 / 钩在后却还有箭在它后面
				{
					ok = false;
					break;
				}
			}
			if (ok)
			{
				return false;
			}

			ammoSlots.Sort();                     // 升序（与原有槽位顺序一致）
			int at = 0;
			if (hookFirst)
			{
				eq[(EquipmentIndex)ammoSlots[at++]] = hook;
			}
			for (int i = 0; i < arrows.Count; i++)
			{
				eq[(EquipmentIndex)ammoSlots[at++]] = arrows[i];
			}
			if (!hookFirst)
			{
				eq[(EquipmentIndex)ammoSlots[at]] = hook;
			}

			DebugLogger.Log($"[Grapple] 装备顺序归一化：钩爪从 {hookSlot} 号槽挪到 "
				+ $"{(hookFirst ? ammoSlots[0] : ammoSlots[at])} 号槽"
				+ $"（{(hookFirst ? "装备里有钩索绳、且没有别的远程武器 ⇒ 钩优先" : "有别的弓/弩 ⇒ 让箭优先")}；共 {arrows.Count} 组箭）");
			return true;
		}
	}
}
