using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace BannerlordTalk.Runtime;

internal static class NativeHeroContextProvider
{
	internal static string BuildStableRole(Hero hero)
	{
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		if (hero == null)
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(800);
		Append(stringBuilder, "姓名", ((object)hero.Name)?.ToString());
		CultureObject culture = hero.Culture;
		Append(stringBuilder, "文化", (culture == null) ? null : ((object)((BasicCultureObject)culture).Name)?.ToString());
		Clan clan = hero.Clan;
		Append(stringBuilder, "家族", (clan == null) ? null : ((object)clan.Name)?.ToString());
		Clan clan2 = hero.Clan;
		object value;
		if (clan2 == null)
		{
			value = null;
		}
		else
		{
			Kingdom kingdom = clan2.Kingdom;
			value = ((kingdom == null) ? null : ((object)kingdom.Name)?.ToString());
		}
		Append(stringBuilder, "王国", (string)value);
		Occupation occupation = hero.Occupation;
		Append(stringBuilder, "身份", OccupationLabel(((object)(Occupation)(ref occupation)).ToString()));
		if (hero.Age > 0f)
		{
			Append(stringBuilder, "年龄", ((int)Math.Floor(hero.Age)).ToString());
		}
		return Bound(stringBuilder.ToString(), 1000);
	}

	internal static string BuildLiveFacts(Hero speaker, IReadOnlyList<Hero> participants, string topic, bool includeQuestTitles)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		MobileParty mainParty = MobileParty.MainParty;
		StringBuilder stringBuilder = new StringBuilder(1800);
		CampaignTime now = CampaignTime.Now;
		double toDays = ((CampaignTime)(ref now)).ToDays;
		int num = Math.Max(1, (int)Math.Floor(toDays) + 1);
		int num2 = Math.Max(0, Math.Min(23, (int)Math.Floor((toDays - Math.Floor(toDays)) * 24.0)));
		Append(stringBuilder, "日期时间", "第" + num + "天 " + num2.ToString("00") + "时");
		Settlement obj = CurrentSettlementResolver.Resolve();
		Append(stringBuilder, "地点", ((obj == null) ? null : ((object)obj.Name)?.ToString()) ?? ((mainParty != null && mainParty.IsCurrentlyAtSea) ? "海上航行" : "大地图旅途中"));
		string text = TryReadTerrain(mainParty);
		string text2 = TryReadWeather(mainParty);
		if (text.Length > 0)
		{
			Append(stringBuilder, "地形", text);
		}
		if (text2.Length > 0)
		{
			Append(stringBuilder, "天气", text2);
		}
		if (mainParty != null)
		{
			try
			{
				Append(stringBuilder, "队伍补给", FoodSupplyBand(mainParty.GetNumDaysForFoodToLast()));
			}
			catch
			{
			}
			Append(stringBuilder, "同行队伍", PartySizeBand(ReadRosterCount(mainParty.MemberRoster)));
			if (ReadRosterCount(mainParty.PrisonRoster) > 0)
			{
				Append(stringBuilder, "俘虏情况", "队伍正押送着俘虏");
			}
		}
		if (speaker != null)
		{
			if (speaker.IsWounded)
			{
				Append(stringBuilder, "发言人近况", "带伤同行");
			}
			if (Hero.MainHero != null)
			{
				Append(stringBuilder, "发言人与玩家关系", RelationBand(speaker.GetRelation(Hero.MainHero)));
			}
		}
		foreach (Hero item in (participants ?? Array.Empty<Hero>()).Where((Hero item) => item != null))
		{
			string text3 = ((speaker == null || item == speaker) ? string.Empty : ("，与发言人关系" + RelationBand(speaker.GetRelation(item))));
			string name = "参与者" + (object)item.Name;
			CultureObject culture = item.Culture;
			Append(stringBuilder, name, (((culture == null) ? null : ((object)((BasicCultureObject)culture).Name)?.ToString()) ?? "文化未知") + (item.IsWounded ? "，带伤" : "") + text3);
		}
		if (includeQuestTitles)
		{
			AppendMatchingQuestTitles(stringBuilder, topic);
		}
		return Bound(stringBuilder.ToString(), 2600);
	}

	internal static IReadOnlyList<string> GetMatchingQuestTitles(string topic)
	{
		List<string> list = new List<string>();
		if (!string.IsNullOrWhiteSpace(topic))
		{
			Campaign current = Campaign.Current;
			object obj;
			if (current == null)
			{
				obj = null;
			}
			else
			{
				QuestManager questManager = current.QuestManager;
				obj = ((questManager != null) ? questManager.Quests : null);
			}
			if (obj != null)
			{
				foreach (QuestBase item in (List<QuestBase>)(object)Campaign.Current.QuestManager.Quests)
				{
					if (item == null || !item.IsOngoing)
					{
						continue;
					}
					string text = ((object)item.Title)?.ToString() ?? string.Empty;
					if (text.Length >= 2 && topic.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 && !list.Contains(text, StringComparer.OrdinalIgnoreCase))
					{
						list.Add(text);
						if (list.Count >= 3)
						{
							break;
						}
					}
				}
				return list;
			}
		}
		return list;
	}

	private static void AppendMatchingQuestTitles(StringBuilder builder, string topic)
	{
		IReadOnlyList<string> matchingQuestTitles = GetMatchingQuestTitles(topic);
		if (matchingQuestTitles.Count > 0)
		{
			Append(builder, "当前明确提及的任务", string.Join("、", matchingQuestTitles));
		}
	}

	private static string TryReadWeather(MobileParty party)
	{
		try
		{
			object obj = ((object)Campaign.Current)?.GetType().GetProperty("Models")?.GetValue(Campaign.Current);
			object obj2 = obj?.GetType().GetProperty("MapWeatherModel")?.GetValue(obj);
			object obj3 = ReadVec2Position(party);
			return WeatherLabel((obj2?.GetType().GetMethods().FirstOrDefault((MethodInfo item) => item.Name == "GetWeatherEventInPosition" && item.GetParameters().Length == 1))?.Invoke(obj2, new object[1] { obj3 })?.ToString());
		}
		catch
		{
			return string.Empty;
		}
	}

	private static string TryReadTerrain(MobileParty party)
	{
		try
		{
			object obj = ((object)Campaign.Current)?.GetType().GetProperty("MapSceneWrapper")?.GetValue(Campaign.Current);
			object obj2 = ((object)party)?.GetType().GetProperty("Position")?.GetValue(party);
			return TerrainLabel((obj?.GetType().GetMethods().FirstOrDefault((MethodInfo item) => item.Name == "GetTerrainTypeAtPosition" && item.GetParameters().Length == 1))?.Invoke(obj, new object[1] { obj2 })?.ToString());
		}
		catch
		{
			return string.Empty;
		}
	}

	private static object ReadVec2Position(MobileParty party)
	{
		object obj = ((object)party)?.GetType().GetProperty("Position")?.GetValue(party);
		return obj?.GetType().GetMethod("ToVec2", Type.EmptyTypes)?.Invoke(obj, null) ?? obj;
	}

	private static int ReadRosterCount(object roster)
	{
		try
		{
			return (roster?.GetType().GetProperty("TotalManCount")?.GetValue(roster) is int val) ? Math.Max(0, val) : 0;
		}
		catch
		{
			return 0;
		}
	}

	private static string FoodSupplyBand(double days)
	{
		if (!(days <= 0.1))
		{
			if (!(days < 3.0))
			{
				if (!(days < 8.0))
				{
					return "较为充足";
				}
				return "尚可维持";
			}
			return "较为紧张";
		}
		return "几乎耗尽";
	}

	private static string PartySizeBand(int count)
	{
		if (count > 10)
		{
			if (count > 30)
			{
				if (count > 80)
				{
					return "规模庞大";
				}
				return "人数不少";
			}
			return "一支小队";
		}
		return "寥寥数人";
	}

	private static string RelationBand(int relation)
	{
		if (relation < 50)
		{
			if (relation < 15)
			{
				if (relation > -50)
				{
					if (relation > -15)
					{
						return "一般";
					}
					return "不和";
				}
				return "仇视";
			}
			return "友好";
		}
		return "非常亲近";
	}

	private static string WeatherLabel(string value)
	{
		return (value ?? string.Empty).Trim() switch
		{
			"Clear" => "晴朗", 
			"LightRain" => "下着小雨", 
			"HeavyRain" => "大雨不断", 
			"Snowy" => "正在飘雪", 
			"Blizzard" => "刮着暴风雪", 
			"Storm" => "风暴正盛", 
			_ => Bound(value, 80), 
		};
	}

	private static string TerrainLabel(string value)
	{
		return (value ?? string.Empty).Trim() switch
		{
			"Plain" => "开阔平原", 
			"Desert" => "干燥荒漠", 
			"Snow" => "积雪地带", 
			"Forest" => "林地", 
			"Steppe" => "草原", 
			"Mountain" => "山地", 
			"Swamp" => "沼泽", 
			"CoastalSea" => "近海", 
			"OpenSea" => "外海", 
			_ => Bound(value, 80), 
		};
	}

	private static string OccupationLabel(string value)
	{
		return (value ?? string.Empty).Trim() switch
		{
			"Lord" => "领主", 
			"Wanderer" => "游历者", 
			"GangLeader" => "帮派首领", 
			"Artisan" => "工匠", 
			"Merchant" => "商人", 
			"RuralNotable" => "乡绅", 
			"Headman" => "村庄头人", 
			"Preacher" => "传教者", 
			"Soldier" => "军人", 
			_ => Bound(value, 80), 
		};
	}

	private static void Append(StringBuilder builder, string name, string value)
	{
		string text = Bound(value, 240);
		if (text.Length > 0)
		{
			builder.Append(name).Append('：').AppendLine(text);
		}
	}

	private static string Bound(string value, int maximum)
	{
		string text = (value ?? string.Empty).Replace('\0', ' ').Replace('\r', ' ').Replace('\n', ' ')
			.Trim();
		if (text.Length <= maximum)
		{
			return text;
		}
		return text.Substring(0, maximum).TrimEnd();
	}
}
