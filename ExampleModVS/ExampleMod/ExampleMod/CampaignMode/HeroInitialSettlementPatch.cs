using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 开局落点：把领主部队刷到**数据指定的城**（2026-09-12 用户裁定「把相关的人直接放在对应的城里」）。
	///
	/// 背景：vanilla <c>Helpers.SettlementHelper.GetBestSettlementToSpawnAround(hero)</c>（反编译实锤）
	///   遍历 <c>Settlement.All</c> 按「势力关系 × 城类型 × 距离」打分选一个，**不读人物数据** ——
	///   所以织田信长会被刷到那古野城，而不是他自己的清洲城。
	///
	/// 数据源：设计数据表 `Modules/Taikou/ModuleData/DesignData/HeroExtraInfo.csv`
	///   （LWN 对英雄的**通用扩展属性表**：每行一个英雄，`ID` 之外的列全是扩展属性；
	///   本代表 `Spawn_&lt;年&gt;` = 开局落点，值 = 据点 StringId）。
	///   表头走 DesignData 约定（三行：英文键 / 类型 / 中文标签，数据从第 4 行起）。
	///   由 `Scripts/gen_hero_extra_info.py` 从 `TaikouHero.csv` 的 `City_&lt;年&gt;` 生成。
	///   🔴 数据**独立成表**、不往引擎段（NPCCharacter 等）上挂自定义属性：引擎段只放引擎认识的东西。
	///
	/// 口径（用户裁定「优先走有指定数据的，没指定就算了」）：该英雄在当前年代**有值** → 用它的城；
	///   **空** → 不接管，走 vanilla 原逻辑。
	/// 势力校验：vanilla 调用点（`HeroSpawnCampaignBehavior.SpawnLordParty`）随后还会要求
	///   `settlement.MapFaction == hero.MapFaction` —— 本补丁**同样只在势力一致时接管**，
	///   否则不接管（避免把领主塞进敌城，交回原逻辑挑）。
	///
	/// 版本：1.2.12 反编译实证。🔴 只在数据包 Taikou 存在时生效（文件读不到 = 空表 = 全走原逻辑，
	///   纯功能包模式不受影响）。
	/// </summary>
	[HarmonyPatch(typeof(Helpers.SettlementHelper), "GetBestSettlementToSpawnAround")]
	public static class HeroInitialSettlementPatch
	{
		private static readonly string[] Eras = { "1554", "1560", "1568", "1575", "1582", "1598" };

		private static Dictionary<string, string> _map;      // heroId → 据点 StringId（懒加载一次）
		private static bool _loaded;

		[HarmonyPrefix]
		public static bool Prefix(Hero hero, ref Settlement __result)
		{
			try
			{
				if (hero == null)
				{
					return true;
				}
				Dictionary<string, string> map = GetMap();
				if (map == null || map.Count == 0)
				{
					return true;                             // 无数据（纯功能包 / 读失败）→ 原逻辑
				}
				string sid;
				if (!map.TryGetValue(hero.StringId, out sid) || string.IsNullOrEmpty(sid))
				{
					return true;                             // 该英雄这代没指定 → 原逻辑
				}
				Settlement s = Settlement.Find(sid);
				if (s == null)
				{
					return true;
				}
				if (s.MapFaction != hero.MapFaction)
				{
					return true;                             // 势力不一致（vanilla 调用点也会拒）→ 原逻辑
				}
				__result = s;
				return false;                                // 接管：跳过原方法
			}
			catch (Exception e)
			{
				DebugLogger.Log("[HeroSpawn] 落点补丁异常（回退原逻辑）：" + e.Message);
				return true;
			}
		}

		private static Dictionary<string, string> GetMap()
		{
			if (_loaded)
			{
				return _map;
			}
			_loaded = true;
			_map = new Dictionary<string, string>();
			try
			{
				string era = CurrentEra();
				string file = Path.Combine(BasePath.Name, "Modules", "Taikou", "ModuleData",
					"DesignData", "HeroExtraInfo.csv");
				if (!File.Exists(file))
				{
					DebugLogger.Log("[HeroSpawn] 落点表不存在（" + file + "）→ 全部走引擎默认落点");
					return _map;
				}
				string[] lines = File.ReadAllLines(file, Encoding.UTF8);
				if (lines.Length < 4)
				{
					return _map;
				}
				// DesignData 三行表头：L1 英文键 / L2 类型 / L3 中文标签 → 数据从 L4 起
				string[] hdr = Split(lines[0]);
				int idCol = Array.IndexOf(hdr, "ID");
				int spawnCol = Array.IndexOf(hdr, "Spawn_" + era);
				if (idCol < 0 || spawnCol < 0)
				{
					DebugLogger.Log("[HeroSpawn] 落点表缺列（ID / Spawn_" + era + "）→ 全部走引擎默认落点");
					return _map;
				}
				for (int i = 3; i < lines.Length; i++)
				{
					string[] c = Split(lines[i]);
					if (c.Length <= Math.Max(idCol, spawnCol))
					{
						continue;
					}
					string id = Clean(c[idCol]);
					string sid = Clean(c[spawnCol]);
					if (id.Length > 0 && sid.Length > 0)
					{
						_map[id] = sid;
					}
				}
				DebugLogger.Log(string.Format("[HeroSpawn] 落点表就绪：{0} 代 {1} 人带指定落点", era, _map.Count));
			}
			catch (Exception e)
			{
				DebugLogger.Log("[HeroSpawn] 落点表读取失败（全部走引擎默认）：" + e.Message);
			}
			return _map;
		}

		/// <summary>表由生成器保证「值内无半角逗号」（铁律 24）→ 直接按逗号切。</summary>
		private static string[] Split(string line)
		{
			string[] parts = (line ?? "").Split(',');
			for (int i = 0; i < parts.Length; i++)
			{
				parts[i] = Clean(parts[i]);
			}
			return parts;
		}

		/// <summary>去空白与 UTF-8 BOM（文件是 utf-8-sig，首格会带 U+FEFF）。</summary>
		private static string Clean(string s)
		{
			return (s ?? "").Trim().TrimStart('\uFEFF').Trim();
		}

		/// <summary>当前战役的年代（从 Campaign 实现类名取，如 `TaikouCampaign1560` → 1560）。</summary>
		private static string CurrentEra()
		{
			try
			{
				Campaign c = Campaign.Current;
				string n = c == null ? "" : c.GetType().Name;
				foreach (string e in Eras)
				{
					if (n.Contains(e))
					{
						return e;
					}
				}
			}
			catch (Exception)
			{
				// 早期时点取不到 → 走默认
			}
			return "1560";                                   // 与生成器 BASELINE_ERA 同口径
		}
	}
}
