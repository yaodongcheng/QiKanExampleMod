using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 选人界面的**取数与过滤**（纯查询，不碰 UI——可离线单测；界面只消费它的结果）。
	/// 三级：王国 → 家族 → 英雄。过滤规则见 <c>plans/选人开局-实施计划.md</c> §3.2。
	/// </summary>
	public static class HeroSelectData
	{
		/// <summary>
		/// 可作开局的王国：排除小派系（雇佣兵/匪帮那类）、排除已灭亡的。
		/// 按据点数量降序（大势力排前面，玩家更好找）。
		/// </summary>
		public static List<Kingdom> GetSelectableKingdoms()
		{
			var list = new List<Kingdom>();
			foreach (Kingdom k in Kingdom.All)
			{
				if (k == null || k.IsEliminated || k.IsMinorFaction)
				{
					continue;
				}
				list.Add(k);
			}
			list.Sort((a, b) => b.Fiefs.Count.CompareTo(a.Fiefs.Count));
			return list;
		}

		/// <summary>该王国里**有可扮演英雄**的家族（空家族不列，免得点进去是空的）。</summary>
		public static List<Clan> GetSelectableClans(Kingdom kingdom)
		{
			var list = new List<Clan>();
			if (kingdom == null)
			{
				return list;
			}
			foreach (Clan c in kingdom.Clans)
			{
				if (c == null || c.IsEliminated)
				{
					continue;
				}
				if (GetSelectableHeroes(c).Count > 0)
				{
					list.Add(c);
				}
			}
			return list;
		}

		/// <summary>
		/// 该家族里可扮演的英雄：**在世 · 成年 · 非俘虏 · 非占位主角**。
		/// 🔴 过滤必须做——选到小孩/囚犯/死人 = 开局即废档。
		/// 排序：族长优先，其余按年龄降序（族长最有扮演价值）。
		/// </summary>
		public static List<Hero> GetSelectableHeroes(Clan clan)
		{
			var list = new List<Hero>();
			if (clan == null)
			{
				return list;
			}
			foreach (Hero h in clan.Heroes)
			{
				if (!IsSelectable(h))
				{
					continue;
				}
				list.Add(h);
			}
			list.Sort((a, b) =>
			{
				int byLeader = (b.IsClanLeader ? 1 : 0).CompareTo(a.IsClanLeader ? 1 : 0);
				return byLeader != 0 ? byLeader : b.Age.CompareTo(a.Age);
			});
			return list;
		}

		/// <summary>单个英雄是否可扮演（界面/取数共用同一判据，避免两处规则漂移）。</summary>
		public static bool IsSelectable(Hero hero)
		{
			try
			{
				return hero != null
					&& hero.IsAlive
					&& !hero.IsChild
					&& !hero.IsPrisoner
					&& !hero.IsNotSpawned          // 尚未进入世界（未出生/未登场）
					&& hero.Clan != null
					&& hero.StringId != "main_hero";   // 占位主角不给选（它是被替换的那个）
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>英雄的一句话身份（界面副标题用）：家族长 / 家族成员。</summary>
		public static string GetHeroRoleText(Hero hero)
		{
			if (hero == null)
			{
				return string.Empty;
			}
			if (hero.IsKingdomLeader)
			{
				return new TaleWorlds.Localization.TextObject("{=LWN_hero_select_role_king}Ruler").ToString();
			}
			if (hero.IsClanLeader)
			{
				return new TaleWorlds.Localization.TextObject("{=LWN_hero_select_role_clan_leader}Clan Leader").ToString();
			}
			return new TaleWorlds.Localization.TextObject("{=LWN_hero_select_role_member}Lord").ToString();
		}

		/// <summary>显示名（英雄/家族/王国统一入口；取不到时回落到 StringId，便于排查）。</summary>
		public static string GetDisplayName(Hero hero)
		{
			try
			{
				string name = hero?.Name?.ToString();
				return string.IsNullOrEmpty(name) ? (hero?.StringId ?? "?") : name;
			}
			catch (Exception)
			{
				return hero?.StringId ?? "?";
			}
		}

		/// <summary>StringId → Hero（供存档/静态交接还原用；找不到返回 null）。</summary>
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
			catch (Exception)
			{
				return null;
			}
		}
	}
}
