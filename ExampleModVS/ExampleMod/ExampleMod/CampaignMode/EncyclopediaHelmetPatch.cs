using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.Core;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 百科全书「角色页」的 3D 立绘：给**自建 race 的人**（= 有专属形象的那些人）把头盔戴回去。
	///
	/// **为什么需要补丁**：原版在这一页**主动把头盔槽擦掉了** —— `EncyclopediaHeroPageVM.Refresh()`
	/// 里 `FillFrom` 之后连清三个槽：
	/// <code>
	/// HeroCharacter.SetEquipment(EquipmentIndex.ArmorItemEndSlot, default);   // 10 = 马
	/// HeroCharacter.SetEquipment(EquipmentIndex.HorseHarness,     default);   // 11 = 马具
	/// HeroCharacter.SetEquipment(EquipmentIndex.NumAllWeaponSlots, default);  // ← 数值 5 = 头盔
	/// </code>
	/// 第三行写法上几乎肯定是原版笔误：名字读作「所有武器槽」（想清掉手里的武器），
	/// 而 `EquipmentIndex.NumAllWeaponSlots` 的**数值 = 5，与 `EquipmentIndex.Head` 相等**
	/// （1.2.12 / 1.5.2 两份 DLL 逐字一致：枚举里这两个名字就是同一个数）——
	/// 效果 = **武器槽（0~3）没被清、头盔被摘**。所以原版全世界的角色在百科全书里都不戴兜、不骑马。
	///
	/// **我们的需求与原版不同**：自建 race 的人**有专属形象**（专属头模 + 多半配了专属兜），
	/// 百科立绘要把这份形象完整体现出来 ⇒ 给这部分人把头槽补回去；
	/// 原版 human 族的角色**保持原版行为**（这一页看得见脸），不替原版改体验。
	///
	/// **边界（任一条不满足就静默不动，绝不影响页面本身）**：
	///   · 只认自建 race（`lwn_` 前缀，见 <see cref="CustomRaceHelper"/>）——原版 / 别的 mod 的 race 不碰；
	///   · 他本来就没兜（头槽为空）→ 不动；
	///   · `FillFrom` 提前返回的两种情况（未成年 / 信息隐藏）→ 那时模型根本没建、VM 里装备是空的，
	///     别去写（`IsHidden` + 成熟度两道闸，与 `FillFrom` 同判据）；
	///   · 取装备的口径与 `HeroViewModel.FillFrom` **严格一致**：名门（`IsNotable`）/ 非战斗人员
	///     （`IsNoncombatant`）走**民用装**，其余走**战装**（口径不一致 = 给他补一个他没穿的头盔）。
	///
	/// **日志**：`[EncHelmet]` 标签（每人每次打开百科页一行，不刷屏）。
	///
	/// ⚠️ 运行期补丁台账：登记于 Knowledge/自定义世界内容包从零起步必备清单.md（雷 142）；
	///    退役条件 = 原版修掉那行笔误（改为逐槽清武器），或百科全书原生提供「显示头盔」开关时删除。
	/// </summary>
	[HarmonyPatch(typeof(EncyclopediaHeroPageVM), "Refresh")]
	public static class EncyclopediaHeroHelmetPatch
	{
		/// <summary>`EncyclopediaHeroPageVM._hero`（原版私有字段，1.2.12 与 1.5.2 同名）。</summary>
		private static readonly FieldInfo HeroField =
			AccessTools.Field(typeof(EncyclopediaHeroPageVM), "_hero");

		/// <summary>原版清完槽之后跑：把自建 race 的人的头盔补回去。</summary>
		[HarmonyPostfix]
		private static void Postfix(EncyclopediaHeroPageVM __instance)
		{
			try
			{
				if (__instance == null)
				{
					return;
				}
				Hero hero = HeroField?.GetValue(__instance) as Hero;
				if (hero == null)
				{
					return;
				}
				CharacterObject character = hero.CharacterObject;
				if (character == null || !CustomRaceHelper.IsCustomRace(character))
				{
					return; // 原版 human 族（或拿不到 race）→ 保持原版「这一页看得见脸」的行为
				}
				if (FaceGen.GetMaturityTypeWithAge(hero.Age) <= BodyMeshMaturityType.Child)
				{
					return; // 未成年：FillFrom 也是直接返回，VM 里装备是空的，别去写
				}
				HeroViewModel viewModel = __instance.HeroCharacter;
				if (viewModel == null || viewModel.IsHidden)
				{
					return; // 信息隐藏：这一页本来就不显示模型
				}

				// 与 HeroViewModel.FillFrom 同口径：名门 / 非战斗人员走民用装，其余走战装
				Equipment source = (hero.IsNotable || hero.IsNoncombatant)
					? hero.CivilianEquipment
					: hero.BattleEquipment;
				if (source == null)
				{
					return;
				}
				EquipmentElement helmet = source[EquipmentIndex.Head];
				if (helmet.IsEmpty)
				{
					return; // 这个人没有兜 → 没什么可补的
				}

				viewModel.SetEquipment(EquipmentIndex.Head, helmet);
				DebugLogger.Log($"[EncHelmet] {character.StringId} 补回头盔 {helmet.Item?.StringId}");
			}
			catch (Exception ex)
			{
				// 任何异常都只记账：百科立绘少一顶头盔可以接受，绝不能影响百科页面本身
				DebugLogger.Log($"[EncHelmet] 补头盔异常（已忽略，不影响百科页面）：{ex.GetType().Name} {ex.Message}");
			}
		}
	}
}
