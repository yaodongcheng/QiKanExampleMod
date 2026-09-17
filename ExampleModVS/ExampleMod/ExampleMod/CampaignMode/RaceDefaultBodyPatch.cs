using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Selector;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.ViewModelCollection.FaceGenerator;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 捏人界面的「种族」下拉：选到**按角色做的 race** 时，把滑条与预览套成这个角色的默认身型
	/// （身高 / 体重 / 体型 —— 三项同处该角色自己的 <c>BodyProperties</c>）。
	///
	/// **为什么需要补丁**：引擎没有「race 默认 BodyProperties」这个读取点（雷 138）——
	/// `skins.xml` 的 race/skin 段没有任何脸键属性，`default_face_key` 全 DLL 0 命中，
	/// `BasicCharacterObject.Deserialize` 只认角色自己的 `&lt;face&gt;`。
	/// 而原版 `OnSelectRace` 只做 `UpdateRaceAndGenderBasedResources()` + `Refresh(true)`，
	/// **不重置任何滑条** → 选了 `lwn_nobunaga` 也只是换了头，体重/体型仍停在原值。
	///
	/// **值从哪来（不造离线表）**：直接从**已加载的领主数据反查** —— 扫描 `CharacterObject`，
	/// 取「Race == 当前选中的 race」的那个角色，读它自己的 `GetBodyPropertiesMin(returnBaseValue: true)`。
	/// 这样值与内容包 XML 同源（一份真相），不会出现「表写一套、领主写另一套」的分叉。
	/// 🔴 `returnBaseValue: true` 是故意的：`CharacterObject` 覆写过这个方法，**英雄走的是
	/// `HeroObject.BodyProperties`（运行期派生/带种子的那份）**；传 true 才拿「数据里的默认值」，
	/// 且**不依赖英雄对象是否存在**——建号阶段英雄可能还没建（那时走了另一条分支），两种时点给同一个值。
	/// 🔴 **不要**为这件事造一张 race → 身型的 XML 表：引擎零消费者 = 死数据，且会与领主数据分叉。
	///
	/// **机制（1.2.12 反编译实测，逐条别猜）**：
	///   · `OnSelectRace` 内 `UpdateFace(-20, …)` → `SetRaceGenderAndAdjustParams` 会把
	///     `_faceGenerationParams.CurrentRace` 置成新 race；所以 Postfix 时它已等于 `s.SelectedIndex`；
	///   · `SetBodyProperties(bp, ignoreDebugValues, race, gender, recordChange)` 里
	///     `flag = (CurrentRace != race)`：**传当前 race ⇒ flag=false ⇒ 不触发 `Refresh`**，
	///     只走 `UpdateFacegen()`（滑条按新参数刷新）+ `UpdateFace()`（重画预览）——正是我们要的；
	///     若传了别的 race，会走 `Refresh(true)` → 重建下拉 → 构造 `SelectorVM` 时 `SelectedIndex`
	///     赋新值会**回调 `OnSelectRace`**（`SelectorVM.SelectedIndex` setter 里 `_onChange?.Invoke`）
	///     → 最多再重入一层（下一层 race 已相同 → 收手），但没必要，所以按「传当前值」写。
	///   · 单机建号里 `_isAgeAvailable` 恒 false（= `_showDebugValues`）⇒ **Age 不会被套用**，
	///     玩家的年龄滑条不会被这个补丁覆盖（体重/体型单机恒可用，`_isWeightAvailable`/`_isBuildAvailable`）。
	///
	/// **边界（拿不到就静默不套，绝不影响建号流程）**：
	///   · 六代领主按时代互斥加载 ⇒ **某一代没有出场的角色，其 race 在这一代查不到使用者** →
	///     选到它不套默认值（无害）。想让"每个 race 都有默认值"= 数据层面做不到，别当成 bug；
	///   · 排除玩家自己的角色（`IsPlayerCharacter`）：玩家选了这个 race 后，反查要的仍是**领主模板**；
	///   · 同一 race 有多个使用者时取 StringId 最小的（确定性），优先 `IsHero`。
	///   · **只在玩家真点了下拉时动手**：原版 `Refresh(true)` 会重建下拉、而 `SelectorVM` 构造时
	///     赋 `SelectedIndex` 会**回调 `OnSelectRace`** ⇒ 界面打开 / 切性别这类连带回调也会进这里，
	///     那时滑条本来就是角色自己的值，不该被改（判据 = `_characterRefreshEnabled`，见 `IsUserInitiated`）；
	///   · 前缀闸门拦下的那次选择（`__runOriginal == false`）**什么都不做** —— race 没变，别去套。
	///
	/// **日志**：`[RaceDefaultBody]` 标签（建表 / 套用 / 异常；前两者一次性，不刷屏）。
	///
	/// ⚠️ 运行期补丁台账：登记于 Knowledge/自定义世界内容包从零起步必备清单.md（雷 139）；
	///    退役条件 = 引擎原生支持「race 级默认 BodyProperties」时删除。
	/// </summary>
	[HarmonyPatch(typeof(FaceGenVM), "OnSelectRace")]
	public static class FaceGenRaceDefaultBodyPatch
	{
		/// <summary>`_characterRefreshEnabled`（原版私有字段）：Refresh 期间为 false。</summary>
		private static readonly FieldInfo RefreshEnabledField =
			AccessTools.Field(typeof(FaceGenVM), "_characterRefreshEnabled");

		/// <summary>原版函数体跑完之后（此时 `_faceGenerationParams.CurrentRace` 已是新 race）。</summary>
		[HarmonyPostfix]
		private static void Postfix(FaceGenVM __instance, SelectorVM<SelectorItemVM> s, bool __runOriginal)
		{
			try
			{
				if (!__runOriginal)
				{
					// 原版函数体被前缀拦下了（`FaceGenOnSelectRaceGuard` 拒了非法/不可选索引）：
					// 这次选择**没有生效**，race 也没变 —— 我们什么都不做，别去替它套默认身型
					return;
				}
				if (__instance == null)
				{
					return;
				}
				if (!IsUserInitiated(__instance))
				{
					// 🔴 原版的 `Refresh(true)` 会**重建**种族下拉，而 `SelectorVM` 构造时给 SelectedIndex
					//    赋初值会**回调 `OnSelectRace`** —— 也就是"界面刚打开 / 别的东西触发了一次刷新"
					//    也会走到这里。那种情况下滑条本来就是这个角色自己的值，**不该被我们改**；
					//    只有玩家真去点了下拉才套默认值。（判据 = 原版的刷新标志：Refresh 期间它是 false）
					return;
				}
				// 以 VM 当前的种族选择器为准（原版 Refresh 可能已经换过实例），拿不到再退回参数里的那个
				int index = s != null ? s.SelectedIndex : -1;
				if (__instance.RaceSelector != null && __instance.RaceSelector.SelectedIndex >= 0)
				{
					index = __instance.RaceSelector.SelectedIndex;
				}
				string[] raceNames = TaleWorlds.Core.FaceGen.GetRaceNames();
				if (raceNames == null || index < 0 || index >= raceNames.Length)
				{
					return; // 单种族 / 还没建出来 / 索引异常 → 不做事
				}
				string raceId = raceNames[index];
				if (string.IsNullOrEmpty(raceId))
				{
					return;
				}
				int gender = __instance.SelectedGender;
				if (gender != 0 && gender != 1)
				{
					return; // 性别还没初始化 → 不碰
				}
				CharacterObject source;
				if (!RaceDefaultBodySource.TryGetSource(raceId, out source))
				{
					return; // 不是「按角色做的 race」，或这个 race 在当前时代没有使用者 → 静默跳过
				}

				BodyProperties bodyProperties = source.GetBodyPropertiesMin(returnBaseValue: true);
				// race 传当前值：SetBodyProperties 内部据此判定"不用重建下拉"，只刷新滑条与预览；
				// recordChange 传 false：原版 OnSelectRace 开头已经压过一次撤销快照 ⇒ 一次 Ctrl+Z
				// 就能把「换种族 + 套默认身型」整体撤回（再压一条 = 多按一次无效撤销）
				__instance.SetBodyProperties(bodyProperties, ignoreDebugValues: false, index, gender, recordChange: false);
				DebugLogger.Log($"[RaceDefaultBody] 选到 {raceId} → 套用 {source.StringId} 的默认身型"
					+ $"（weight={bodyProperties.Weight:F4} build={bodyProperties.Build:F4}）");
			}
			catch (Exception ex)
			{
				// 任何异常都只记账：捏脸界面套不上默认值是可以接受的，绝不能影响建号流程
				DebugLogger.Log($"[RaceDefaultBody] 套用默认身型异常（已忽略，不影响建号）：{ex.GetType().Name} {ex.Message}");
			}
		}

		/// <summary>
		/// 是「玩家点了下拉」还是「原版 Refresh 的连带回调」：后者（界面打开时的首次建下拉、
		/// 切性别、`SetBodyProperties` 自身的 Refresh）**什么都不改** —— 那时滑条本来就是角色自己的值。
		/// 判据 = 原版私有标志 `_characterRefreshEnabled`（Refresh 期间为 false，收尾恢复 true）。
		/// 🔴 读不到这个字段就**放行**（宁可多套一次，也别把功能做没了）。
		/// </summary>
		private static bool IsUserInitiated(FaceGenVM instance)
		{
			FieldInfo field = RefreshEnabledField;
			if (field == null)
			{
				return true;
			}
			object value = field.GetValue(instance);
			return !(value is bool enabled) || enabled;
		}
	}

	/// <summary>
	/// 「按角色做的 race」→ 该角色的反查表（懒建一次，之后常驻内存）。
	///
	/// **race 名 ↔ 角色的对应关系不在代码里写死**：`CharacterObject.Race` 是**索引**（int，
	/// 由 `FaceGen.GetRaceOrDefault(race名)` 在反序列化时算好），所以反查 = 遍历内存里已注册的
	/// `CharacterObject`，用 `Race` 索引经 `FaceGen.GetRaceNames()` 换回名字。
	/// 索引与名字都来自引擎同一张 race 表 ⇒ 不会错位。
	///
	/// **只认本 mod 自己前缀的 race**（`lwn_`，与存档键 / 自定义节点同族命名）：原版 race（human 等）
	/// 有成千上万个使用者，套谁的默认身型都没有意义 —— 前缀就是「这个 race 是给某一个角色做的」的标记。
	/// </summary>
	internal static class RaceDefaultBodySource
	{
		/// <summary>本 mod 给「按角色做的 race」的统一前缀（内容包自建 race 都从这里起名）。</summary>
		private const string RacePrefix = "lwn_";

		private static Dictionary<string, CharacterObject> _byRace;
		private static bool _built;
		private static bool _loggedNoList;

		internal static bool TryGetSource(string raceId, out CharacterObject source)
		{
			source = null;
			if (string.IsNullOrEmpty(raceId) || !raceId.StartsWith(RacePrefix, StringComparison.Ordinal))
			{
				return false; // 原版 / 别的 mod 的 race → 不替人家做主
			}
			Dictionary<string, CharacterObject> map = GetMap();
			return map != null && map.TryGetValue(raceId, out source) && source != null;
		}

		/// <summary>懒建一次；对象表还没就绪（返回 null）时下次再试，建好就不再扫。</summary>
		private static Dictionary<string, CharacterObject> GetMap()
		{
			if (_built)
			{
				return _byRace;
			}
			try
			{
				MBObjectManager manager = MBObjectManager.Instance;
				if (manager == null)
				{
					return null;
				}
				MBReadOnlyList<CharacterObject> list = manager.GetObjectTypeList<CharacterObject>();
				if (list == null)
				{
					// 时点太早（对象表未注册）→ 不标记建过，下次调用再试
					if (!_loggedNoList)
					{
						_loggedNoList = true;
						DebugLogger.Log("[RaceDefaultBody] 对象表还没就绪，暂不建表（下次选种族时再试）");
					}
					return null;
				}

				string[] raceNames = TaleWorlds.Core.FaceGen.GetRaceNames();
				var map = new Dictionary<string, CharacterObject>(StringComparer.Ordinal);
				int scanned = 0;
				for (int i = 0; i < list.Count; i++)
				{
					CharacterObject character = list[i];
					if (character == null || character.IsPlayerCharacter)
					{
						continue; // 玩家自己的角色不是"这个角色的默认值"来源
					}
					scanned++;
					string raceId = RaceIdOf(character, raceNames);
					if (raceId == null || !raceId.StartsWith(RacePrefix, StringComparison.Ordinal))
					{
						continue;
					}
					CharacterObject prev;
					if (map.TryGetValue(raceId, out prev) && prev != null
						&& !IsBetterSource(character, prev))
					{
						continue;
					}
					map[raceId] = character;
				}
				_byRace = map;
				_built = true;
				DebugLogger.Log($"[RaceDefaultBody] 建表完成：{map.Count} 个按角色 race"
					+ $"（扫过 {scanned} 个 CharacterObject）");
				return map;
			}
			catch (Exception ex)
			{
				_built = true; // 出错就认了，不反复扫（拿不到默认值不影响建号）
				DebugLogger.Log($"[RaceDefaultBody] 建表异常（本次不套默认值）：{ex.GetType().Name} {ex.Message}");
				return null;
			}
		}

		/// <summary>同一个 race 多个使用者时谁当代表：优先英雄，其次 StringId 小的（确定性，重开档结果一致）。</summary>
		private static bool IsBetterSource(CharacterObject candidate, CharacterObject current)
		{
			if (candidate.IsHero != current.IsHero)
			{
				return candidate.IsHero;
			}
			return string.CompareOrdinal(candidate.StringId, current.StringId) < 0;
		}

		/// <summary>角色的 race 索引 → race 名（引擎同一张 race 表，索引不会错位）；越界一律 null。</summary>
		private static string RaceIdOf(CharacterObject character, string[] raceNames)
		{
			if (raceNames == null)
			{
				return null;
			}
			int index = character.Race;
			return index >= 0 && index < raceNames.Length ? raceNames[index] : null;
		}
	}
}
