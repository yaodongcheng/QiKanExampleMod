using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 选人界面的**取数与过滤**（纯查询，不碰 UI——可离线单测；界面只消费它的结果）。
	///
	/// 🔴 **数据源 = 选人目录（<see cref="HeroCatalogRegistry"/>），不是活世界**（2026-09-11 改造）：
	///   以前这里读 `Kingdom.All` / `kingdom.Clans` / `clan.Heroes`，所以界面**必须等世界建好**
	///   才弹得出来（长 loading 之后）。现在读内容包预生成的静态目录 → 选人可以放在建世界**之前**。
	///
	/// 三级：王国（含「无所属」那一档） → 家族 → 领主。
	/// 「无所属」= 目录里 <c>realm=""</c> 的那些家族（无王国的独立家族，如忍者/剑豪/水军/商人）。
	/// </summary>
	public static class HeroSelectData
	{
		/// <summary>把目录里的显示名原样串（<c>{=KEY}fallback</c>）解析成当前语言文本。</summary>
		public static string Resolve(string raw)
		{
			if (string.IsNullOrEmpty(raw))
			{
				return string.Empty;
			}
			try
			{
				return new TextObject(raw).ToString();
			}
			catch (Exception)
			{
				return raw;
			}
		}

		// ───────────────────────── 三级取数（全部来自目录）─────────────────────────

		/// <summary>该时代的可选王国（含「无所属」那一档，排在最后）。</summary>
		public static List<HeroCatalogRegistry.Realm> GetRealms(int year)
		{
			HeroCatalogRegistry.EraData era = HeroCatalogRegistry.GetEra(year);
			return era?.Realms ?? new List<HeroCatalogRegistry.Realm>();
		}

		/// <summary>该王国下的家族（<paramref name="realmId"/> 空串 = 「无所属」那一档）。</summary>
		public static List<HeroCatalogRegistry.House> GetHouses(int year, string realmId)
		{
			HeroCatalogRegistry.EraData era = HeroCatalogRegistry.GetEra(year);
			return era?.HousesOf(realmId) ?? new List<HeroCatalogRegistry.House>();
		}

		/// <summary>该家族下的领主。</summary>
		public static List<HeroCatalogRegistry.Lord> GetLords(int year, string houseId)
		{
			HeroCatalogRegistry.EraData era = HeroCatalogRegistry.GetEra(year);
			return era?.LordsOf(houseId) ?? new List<HeroCatalogRegistry.Lord>();
		}

		/// <summary>该时代某英雄的目录条目（做详情页/校验用）。</summary>
		public static HeroCatalogRegistry.Lord FindLord(int year, string heroId)
		{
			return HeroCatalogRegistry.GetEra(year)?.GetLord(heroId);
		}

		/// <summary>该英雄所属家族的显示名（详情页「所属」那一行）。</summary>
		public static string GetHouseName(int year, string houseId)
		{
			HeroCatalogRegistry.House h = HeroCatalogRegistry.GetEra(year)?.GetHouse(houseId);
			return Resolve(h?.NameRaw);
		}

		// ───────────────────────── 落地用（世界建好之后）─────────────────────────

		/// <summary>
		/// StringId → Hero（**世界建好之后**才可用；选人界面不再用它，只有落地那一步用）。
		/// 找不到返回 null（内容包与本世界不一致时的兜底，不抛）。
		/// </summary>
		public static Hero FindHero(string heroId)
		{
			if (string.IsNullOrEmpty(heroId))
			{
				return null;
			}
			try
			{
				return Hero.FindFirst(h => h.StringId == heroId);
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[HeroSelect] 按 id 查英雄失败（{heroId}）：{ex.Message}");
				return null;
			}
		}
	}
}
