using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Core.ViewModelCollection.Selector;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.ViewModelCollection.FaceGenerator;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 建号/捏脸的「种族」下拉：按当前性别把「性别不符的种族」置灰（通用，2026-09-16）。
	///
	/// 背景：自定义头 = 一个头一个 race（skin 按 race×性别×年龄段选，没有"按角色"的口子）。
	/// 于是这个下拉里会堆一排角色专属 race（Taikou 的 lwn_* = 战无 28 人）。每个角色只有一个性别，
	/// 而**一个 race 只做单一性别的 skin** —— 选另一个性别时引擎按 (race, gender) 取不到皮肤，
	/// 直接 native AV 崩（2026-09-16 实机实锤；不是"落兜底皮肤"那么温柔）。
	///
	/// 四处机制，环环相扣（每一处都是踩出来的，别删）：
	///   ① **Postfix 置灰**：按当前性别把不符的条目 `CanBeSelected = false`
	///      （原版下拉 XML 里它就是条目按钮的 `IsEnabled`，而 GauntletUI 的 `EventManager` 派发
	///      鼠标事件时检查 `widget.IsEnabled && widget.IsVisible` → 变灰 = 收不到点击）；
	///   ② **必须换一个新 `SelectorVM` 实例**，不能只改现有条目的属性：原版下拉对 `@CanBeSelected`
	///      的**运行期变更不重画**（实测 VM 已置灰 22 项、界面整列全亮；而选一次种族——
	///      原版 `Refresh(true)` 会换实例——就正确了）。换实例 = 原版自己对颜色选择器的做法，安全；
	///      🔴 但**不能清空**条目列表：`ItemList.Clear()` 会让控件走
	///      `OnChildRemoved → set_IntValue → OnSelectionChanged`，把容器算出的**非法索引回写**进
	///      `SelectorVM.SelectedIndex` → 原版 `OnSelectRace` 拿它当种族索引 → native AV（实机崩过）。
	///      新实例的 onChange 先传 `null`（构造期不触发原版回调，否则会连锁 `Refresh(true)`
	///      再建一个默认全亮的列表，来回打架），构造完再 `SetOnChangeAction` 接回原版回调；
	///   ③ **Prefix 换当前项**：切性别时若当前 race 与新性别不符，必须在**原版函数体之前**换掉——
	///      它第一行就拿 `(_selectedRace, SelectedGender)` 去调 native，postfix 来不及；
	///   ④ **非法索引闸门**（`FaceGenOnSelectRaceGuard`）：任何来源（控件回写、列表重建、异常状态）
	///      送来的 -1 / 越界 / 不可选索引，一律**不许进原版函数体**——它会把 `_selectedRace`
	///      写成那个值，下一句 native 调用就是 AV。这是最后一层保险。
	///
	/// 机制细节（1.2.12 与 1.5.1 反编译逐条核对，方法名/签名一致）：
	///   · `UpdateRaceAndGenderBasedResources` 在「界面建好」和「切性别」两条路上都会被调
	///     （Refresh(bool)：FaceGenVM.cs:1872；SelectedGender setter → FaceGenVM.cs:1066）；
	///   · 不删条目、不重排 → 条目下标仍是引擎 race 索引，原版 `OnSelectRace` 拿 `s.SelectedIndex`
	///     当种族索引照常成立（这是不删条目的理由）。
	///
	/// 性别数据来源 = 内容包表 <c>ModuleData/AssetRegistry/RaceGenders.xml</c>
	/// （读侧见 <see cref="RaceGenderRegistry"/>；**不能**改成运行时从角色反推——六代领主按时代
	/// 互斥加载，缺项 = 过滤失效，2026-09-16 实机崩过一次，详见该读取器的注释）。
	/// 表里没有的 race 一律不限制（安全缺省：别的 mod 的 race 我们不知道）。
	///
	/// ⚠️ 运行期补丁台账：登记于 Knowledge/自定义世界内容包从零起步必备清单.md（雷 135）；
	///    退役条件 = 引擎原生支持「race 限定性别」时删除。
	/// </summary>
	[HarmonyPatch(typeof(FaceGenVM), "UpdateRaceAndGenderBasedResources")]
	public static class FaceGenRaceGenderFilterPatch
	{
		private static readonly MethodInfo OnSelectRaceMethod = AccessTools.Method(typeof(FaceGenVM), "OnSelectRace");

		private static int _lastLoggedGender = -1;
		private static int _lastLoggedDisabled = -1;
		private static bool _loggedUnknownRaces;
		private static bool _loggedNoRebuildChannel;

		/// <summary>
		/// 原版函数体之前：当前选中的 race 与当前性别不符 → 先换成第一个合法项。
		/// 🔴 必须在这里做：原版函数体第一行就是拿 (_selectedRace, SelectedGender) 调 native。
		/// </summary>
		[HarmonyPrefix]
		private static void Prefix(FaceGenVM __instance)
		{
			try
			{
				int gender = NormalizeGender(__instance.SelectedGender);
				if (gender < 0)
				{
					return; // 性别还没初始化（-1）→ 不动，免得把整列全灰了
				}
				SelectorVM<SelectorItemVM> selector = __instance.RaceSelector;
				if (selector == null)
				{
					return; // 种族选择器还没建出来（Refresh 的首次调用）
				}
				SelectorItemVM current = selector.SelectedItem;
				if (current == null || IsAllowed(current.StringItem, gender))
				{
					return;
				}
				int firstAllowed = FindFirstAllowed(selector.ItemList, gender);
				if (firstAllowed < 0)
				{
					return; // 这个性别一个合法种族都没有 = 数据异常 → 宁可不限制
				}
				// 走原版 SelectedIndex 赋值：原版 OnSelectRace 会把 _selectedRace 与脸面刷新到位；
				// 它内部会再调一次本函数（那时当前项已合法 → 本前缀直接返回，不会递归）
				DebugLogger.Log($"[FaceGenRace] 当前种族 {current.StringItem} 与性别 {gender} 不符 → 自动切到 {selector.ItemList[firstAllowed].StringItem}");
				selector.SelectedIndex = firstAllowed;
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[FaceGenRace] 纠正当前种族异常（已忽略）：{ex.GetType().Name} {ex.Message}");
			}
		}

		/// <summary>原版函数体之后：按当前性别刷新每个条目的可用状态（需要变化时换一个新选择器实例）。</summary>
		[HarmonyPostfix]
		private static void Postfix(FaceGenVM __instance)
		{
			try
			{
				int gender = NormalizeGender(__instance.SelectedGender);
				if (gender < 0)
				{
					return;
				}
				SelectorVM<SelectorItemVM> selector = __instance.RaceSelector;
				MBBindingList<SelectorItemVM> items = selector != null ? selector.ItemList : null;
				if (items == null || items.Count <= 0)
				{
					return; // 没有种族选择器（单种族 / 多人模式 / 还没建出来）
				}

				int wantBit = gender == 0 ? RaceGenderRegistry.Male : RaceGenderRegistry.Female;
				var allowed = new bool[items.Count];
				var unknown = new List<string>();
				int firstAllowed = -1;
				int disabledCount = 0;
				bool needsRebuild = false;
				for (int i = 0; i < items.Count; i++)
				{
					SelectorItemVM item = items[i];
					if (item == null)
					{
						allowed[i] = true;
						continue;
					}
					if (!RaceGenderRegistry.HasEntry(item.StringItem))
					{
						unknown.Add(item.StringItem); // 只记账：不认识的 race 按不限制处理
					}
					allowed[i] = (RaceGenderRegistry.GetAllowedGenders(item.StringItem) & wantBit) != 0;
					if (!allowed[i])
					{
						disabledCount++;
					}
					else if (firstAllowed < 0)
					{
						firstAllowed = i;
					}
					if (item.CanBeSelected != allowed[i])
					{
						needsRebuild = true;
					}
				}
				if (firstAllowed < 0)
				{
					return; // 全被禁 = 数据异常 → 宁可不限制，别做死界面
				}

				if (needsRebuild)
				{
					RebuildSelector(__instance, items, allowed);
				}

				if (disabledCount != _lastLoggedDisabled || gender != _lastLoggedGender)
				{
					_lastLoggedGender = gender;
					_lastLoggedDisabled = disabledCount;
					DebugLogger.Log($"[FaceGenRace] 性别 {gender}：种族下拉 {items.Count} 项，置灰 {disabledCount} 项{(needsRebuild ? "（已换新条目实例）" : string.Empty)}");
				}
				if (!_loggedUnknownRaces && unknown.Count > 0)
				{
					// 一次会话只报一次：下拉里出现内容包没登记的 race = 这张表漏了行（或那是别的 mod 的 race）
					_loggedUnknownRaces = true;
					DebugLogger.Log($"[FaceGenRace] 性别表里没有的种族（按不限制处理）：{string.Join(", ", unknown.ToArray())}");
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[FaceGenRace] 刷新条目状态异常（已忽略）：{ex.GetType().Name} {ex.Message}");
			}
		}

		/// <summary>
		/// 换一个**新** SelectorVM 实例（条目自带正确的 CanBeSelected）—— 原版下拉只在列表重建时
		/// 才重画条目状态，所以置灰/恢复都必须走这条路。
		/// 🔴 三个要点：① 不碰旧实例的条目列表（Clear 会引发非法索引回写 → native AV）；
		/// ② 构造时 onChange 传 null（不触发原版回调，避免连锁 Refresh(true) 再建一个默认全亮的列表）；
		/// ③ 构造完用 SetOnChangeAction 把原版 OnSelectRace 接回去（否则新选择器变成死的）。
		/// </summary>
		private static void RebuildSelector(FaceGenVM instance, MBBindingList<SelectorItemVM> items, bool[] allowed)
		{
			if (OnSelectRaceMethod == null)
			{
				if (!_loggedNoRebuildChannel)
				{
					_loggedNoRebuildChannel = true;
					DebugLogger.Log("[FaceGenRace] 找不到原版 OnSelectRace（版本不符）→ 本次不换选择器实例，只置灰（界面可能不重画）");
				}
				for (int i = 0; i < items.Count && i < allowed.Length; i++)
				{
					if (items[i] != null)
					{
						items[i].CanBeSelected = allowed[i];
					}
				}
				return;
			}

			int selectedIndex = instance.RaceSelector.SelectedIndex;
			var fresh = new List<SelectorItemVM>(items.Count);
			for (int i = 0; i < items.Count; i++)
			{
				SelectorItemVM old = items[i];
				fresh.Add(new SelectorItemVM(old != null ? old.StringItem : string.Empty)
				{
					CanBeSelected = allowed[i],
				});
			}
			// 🔴 构造姿势有讲究：`SelectorVM(IEnumerable<T>, int, Action)` 这个重载**不存在**
			//    （只有 `Refresh(IEnumerable<T>, int, Action)`）——所以先建空表，再 Refresh 填表。
			//    Refresh 内部 `SelectedIndex = selectedIndex` 会触发 _onChange，此时传 null ⇒ 构造期零回调。
			var rebuilt = new SelectorVM<SelectorItemVM>(selectedIndex, null);
			rebuilt.Refresh(fresh, selectedIndex, null);
			// 事后把原版回调接回去（否则新选择器是死的）。用 MethodInfo.Invoke 而不是
			// Delegate.CreateDelegate —— 后者绑私有方法有可见性风险，前者一定能过（点击频率极低，开销无所谓）。
			rebuilt.SetOnChangeAction(s => OnSelectRaceMethod.Invoke(instance, new object[] { s }));
			instance.RaceSelector = rebuilt;
		}

		/// <summary>0 = 男 / 1 = 女 / -1 = 还没初始化。</summary>
		internal static int NormalizeGender(int selectedGender)
		{
			return selectedGender == 0 || selectedGender == 1 ? selectedGender : -1;
		}

		internal static bool IsAllowed(string raceId, int gender)
		{
			int wantBit = gender == 0 ? RaceGenderRegistry.Male : RaceGenderRegistry.Female;
			return (RaceGenderRegistry.GetAllowedGenders(raceId) & wantBit) != 0;
		}

		internal static int FindFirstAllowed(MBBindingList<SelectorItemVM> items, int gender)
		{
			if (items == null)
			{
				return -1;
			}
			for (int i = 0; i < items.Count; i++)
			{
				SelectorItemVM item = items[i];
				if (item == null || IsAllowed(item.StringItem, gender))
				{
					return i;
				}
			}
			return -1;
		}
	}

	/// <summary>
	/// 🔴 非法种族索引闸门（2026-09-16 实机 AV 后加的最后一层保险）。
	///
	/// 原版 `OnSelectRace` 把 `s.SelectedIndex` **无条件**写进 `_selectedRace`，下一句就是
	/// `MBBodyProperties.GetParamsMax(_selectedRace, …)` → 索引非法（-1 / 越界）或该 race 与性别不符
	/// 时 = 原生越界读 = `AccessViolationException`（托管栈完好、日志戛然而止）。
	/// 这个非法值不只来自玩家点击——**控件自己也会回写**：列表一变动（如条目重排/重建），
	/// `Container.OnChildRemoved → set_IntValue → AnimatedDropdownWidget.OnSelectionChanged`
	/// 会把容器算出的下标经绑定写回 `SelectorVM.SelectedIndex`（实测就是这么崩的）。
	///
	/// 处置：非法值不送进原版函数体，改成回落到「当前性别下第一个合法 race」（走原版 SelectedIndex
	/// 赋值 → 重入本前缀时已合法 → 放行）。
	/// </summary>
	[HarmonyPatch(typeof(FaceGenVM), "OnSelectRace")]
	public static class FaceGenOnSelectRaceGuard
	{
		[HarmonyPrefix]
		private static bool Prefix(FaceGenVM __instance, SelectorVM<SelectorItemVM> s)
		{
			try
			{
				if (s == null)
				{
					return false;
				}
				MBBindingList<SelectorItemVM> items = s.ItemList;
				int index = s.SelectedIndex;
				if (index >= 0 && items != null && index < items.Count && items[index] != null && items[index].CanBeSelected)
				{
					return true; // 合法 → 放行
				}

				int gender = FaceGenRaceGenderFilterPatch.NormalizeGender(__instance.SelectedGender);
				int fallback = gender >= 0 ? FaceGenRaceGenderFilterPatch.FindFirstAllowed(items, gender) : -1;
				DebugLogger.Log($"[FaceGenRace] 拒绝非法种族索引 {index}（条目 {items?.Count ?? 0} 个，性别 {gender}）→ 回落到 {fallback}");
				if (fallback >= 0)
				{
					// 触发原版回调 → 重入本前缀（那次已合法 → 放行），把 _selectedRace 留在合法值上
					s.SelectedIndex = fallback;
				}
				return false;
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[FaceGenRace] 索引闸门异常（已忽略本次选择）：{ex.GetType().Name} {ex.Message}");
				return false;
			}
		}
	}
}
