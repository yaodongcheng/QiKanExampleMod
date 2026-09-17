using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 「按角色做的 race」（自建 race，本 mod 统一前缀 <c>lwn_</c>）= 这个人**有专属形象**
	/// （专属头模，多半还配了专属甲/兜）。
	///
	/// **判据只认前缀，不列名单**：race 名由内容包生成器决定（`gen_taikou_sw2_heads.py` 等），
	/// 代码里写死名单 = 每加一个人改两处。前缀是内容包与本 mod 的约定（与存档键/自定义节点同族命名）。
	/// 原版 race（human 一族 5 个 id）有上万个使用者，不属于「给某一个人做的」→ 一律不算。
	///
	/// 🔴 `CharacterObject.Race` 是 **int 索引**（反序列化时由 `FaceGen.GetRaceOrDefault(race名)` 算好），
	/// **不是字符串** —— 要经 `FaceGen.GetRaceNames()[索引]` 换回名字。名字与索引都来自引擎同一张 race 表
	/// ⇒ 不会错位（口径与 <see cref="RaceDefaultBodySource"/> 内那份查表同源，那边是热循环自带一份）。
	/// </summary>
	internal static class CustomRaceHelper
	{
		/// <summary>自建 race 的统一前缀（内容包「按角色做的 race」都从这里起名）。</summary>
		internal const string RacePrefix = "lwn_";

		/// <summary>这个角色的 race 是不是「给某一个人做的」（自建 race）；拿不到 race 名一律 false。</summary>
		internal static bool IsCustomRace(CharacterObject character)
		{
			string raceId = RaceIdOf(character);
			return raceId != null && raceId.StartsWith(RacePrefix, StringComparison.Ordinal);
		}

		/// <summary>角色的 race 索引 → race 名；越界 / 索引表还没就绪一律 null。</summary>
		internal static string RaceIdOf(CharacterObject character)
		{
			if (character == null)
			{
				return null;
			}
			string[] raceNames = FaceGen.GetRaceNames();
			if (raceNames == null)
			{
				return null;
			}
			int index = character.Race;
			return (index >= 0 && index < raceNames.Length) ? raceNames[index] : null;
		}
	}
}
