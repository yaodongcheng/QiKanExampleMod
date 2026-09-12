using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 🔴 **临时定位日志（雷 107/待解的第 4 关）**——不做任何行为改动，只在 vanilha 刷领主部队前把
	/// 「谁、哪个值为空」打进运行日志，供一次实机定案。
	/// 背景：建世界期 `HeroSpawnCampaignBehavior` 给领主刷部队时，
	///   `LordPartyComponent.InitializeLordPartyProperties` 内部 NRE（栈顶是内联帧，真凶在被内联的子函数）。
	///   已排除：`Owner.Clan` 非空、196 家族/805 领主模板 culture 全有效、
	///   `Clan.DefaultPartyTemplate` 链（`_defaultPartyTemplate ?? Culture.DefaultPartyTemplate`）落到
	///   文化写的 `PartyTemplate.main_hero_party_template`（存在）。
	///   剩两个嫌疑：①领主部队用了占位模板（所有文化都指向 main_hero 模板，官方是逐家族的专用模板）；
	///   ②新部队 roster/ItemRoster 时序。
	/// 判读：日志出现 `[LordPartyDiag]` 行 → 看哪一格是 `NULL` 或 `→NRE`，那一格就是真凶。
	/// 🔴 定案（2026-09-12 实机日志）：真凶 = `culture=neutral_culture` 一格 —— 该文化（Taikou
	///   `spcultures.xml`）缺 `default_party_template`，`Clan.DefaultPartyTemplate` 兜底到文化后拿到 null
	///   → `FillPartyStacks(pt=null)` NRE。数据已补（必备清单雷 110），本补丁**已停用**（两步法第一步：
	///   注释掉 [HarmonyPatch] 特性，文件保留）；实机验证不再崩后删除本文件 + csproj 登记行。
	/// 版本：1.2.12 反编译实证
	///   `LordPartyComponent.InitializeLordPartyProperties(MobileParty, Vec2, float, Settlement)`（CampaignSystem.dll:95633）。
	/// </summary>
	// 🔴 已停用（2026-09-12 定案 + 数据已修，见类注释）——两步法第一步：注释特性即停用（PatchAll 只扫带
	//    [HarmonyPatch] 的类）；实机验证不再崩后删除本文件 + csproj 登记行。
	// [HarmonyPatch(typeof(LordPartyComponent), "InitializeLordPartyProperties",
	// 	new[] { typeof(MobileParty), typeof(Vec2), typeof(float), typeof(Settlement) })]
	public static class LordPartySpawnDiagPatch
	{
		private static int _count;

		[HarmonyPrefix]
		public static void Prefix(LordPartyComponent __instance, MobileParty mobileParty, Vec2 position,
			float spawnRadius, Settlement spawnSettlement)
		{
			_count++;
			Hero owner = null;
			try
			{
				owner = __instance.Owner;
			}
			catch (Exception e)
			{
				DebugLogger.Log($"[LordPartyDiag] #{_count} 读 __instance.Owner ← {e.GetType().Name}");
			}

			string clan = "?", clanCulture = "?", clanTemplate = "?", clanHome = "?", heroHome = "?";
			int clanSettlements = -1;
			try
			{
				Clan c = owner?.Clan;
				clan = c?.StringId ?? "NULL";
				clanSettlements = c?.Settlements?.Count ?? -1;
				clanHome = c?.HomeSettlement?.StringId ?? "NULL";
				clanCulture = c?.Culture?.StringId ?? "NULL";
				// 单独 try：这一格就是「Culture 为 null」嫌疑的落点
				try { clanTemplate = c?.DefaultPartyTemplate?.StringId ?? "NULL"; }
				catch (Exception e) { clanTemplate = "→" + e.GetType().Name; }
			}
			catch (Exception e)
			{
				DebugLogger.Log($"[LordPartyDiag] #{_count} 读 Clan 侧 ← {e.GetType().Name}");
			}
			try { heroHome = owner?.HomeSettlement?.StringId ?? "NULL"; }
			catch (Exception e) { heroHome = "→" + e.GetType().Name; }

			DebugLogger.Log($"[LordPartyDiag] #{_count} hero={owner?.StringId ?? "NULL"}"
				+ $" clan={clan}(地{clanSettlements}) culture={clanCulture}"
				+ $" clanDefaultPartyTemplate={clanTemplate} clanHome={clanHome} heroHome={heroHome}"
				+ $" spawnSettlement={spawnSettlement?.StringId ?? "NULL"}"
				+ $" pos=({position.X:F0},{position.Y:F0}) radius={spawnRadius:F1}"
				+ $" 队伍模板栈={(clanTemplate == "NULL" || clanTemplate.StartsWith("→") ? "未取到" : "见上")}");
		}
	}
}
