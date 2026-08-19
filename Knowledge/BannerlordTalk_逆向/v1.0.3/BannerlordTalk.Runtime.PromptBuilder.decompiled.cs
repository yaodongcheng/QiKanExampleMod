using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BannerlordTalk.Prompts;
using BannerlordTalk.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerlordTalk.Runtime;

internal static class PromptBuilder
{
	internal static string BuildSystemPrompt(Hero speaker, ChatterMode mode, IReadOnlyList<Hero> participants, string stableRole, string persona, string topic, ChatterMcmSettings settings, bool playerSpeaking, int maximumActionCharacters, int maximumInnerVoiceCharacters)
	{
		string value = mode switch
		{
			ChatterMode.Group => "这是小队群聊：只有列出的参与者能听见。", 
			ChatterMode.Private => "这是两人私聊：只有列出的两名参与者能听见。", 
			_ => "这是独白：只有发言者本人，没有缺席听众。", 
		};
		int num = Math.Max(1, Math.Min(240, settings.MaxLineCharacters));
		int value2 = Math.Min(60, num);
		int actionLimit = Math.Max(0, Math.Min(160, maximumActionCharacters));
		int innerVoiceLimit = Math.Max(0, Math.Min(240, maximumInnerVoiceCharacters));
		StringBuilder stringBuilder = new StringBuilder(5200);
		stringBuilder.AppendLine("你在《骑马与砍杀2》的当前战役存档中扮演指定的一名英雄。").AppendLine("一次请求只生成当前发言者的一次自然发言，可以由一至三个相互连贯的短句组成；严禁代写、续写或模拟任何第二个人的台词、动作或内心。").AppendLine("不要输出姓名前缀、旁白、Markdown、代码块或额外解释。实时存档事实高于记忆，记忆和主观判断不能冒充客观事实。")
			.AppendLine(value)
			.Append("当前唯一发言人：")
			.AppendLine(HeroName(speaker))
			.Append("参与者：")
			.AppendLine(string.Join("、", participants.Select(HeroName)))
			.Append("正文通常写 ")
			.Append(value2)
			.Append(" 至 ")
			.Append(num)
			.Append(" 字符，自然结束即可、不必凑满；硬上限 ")
			.Append(num)
			.AppendLine(" 字符。");
		if (playerSpeaking)
		{
			stringBuilder.AppendLine("本轮唯一发言人是玩家角色，已明确允许 AI 生成这一轮玩家台词；这项允许同样适用于独白、私聊和群聊。").AppendLine("玩家台词只用于窗口显示和本 MOD 记忆，不会执行任何游戏操作。可以按人格自由口嗨、试探、撒谎、威胁、承诺、接受或拒绝；这些叙事不能冒充程序已经实际改动任务、金钱、物品、军队、关系或其他存档状态，除非实时存档事实明确证明。").AppendLine("玩家生成内容是低信任记录，不得自动晋升为玩家人格或长期信念。");
		}
		else
		{
			stringBuilder.AppendLine("不得替玩家角色说话或行动。");
		}
		stringBuilder.AppendLine("自然叙述当前处境，不要使用“士气值”“生命值”“技能等级”等游戏面板术语。").AppendLine("主回复绝不允许返回 thought 字段；隐藏思绪由正文提交后的独立请求生成，只进入该角色的私人记忆，和本轮可见演出不是同一字段。");
		AppendSection(stringBuilder, "原生稳定身份", stableRole, 2200);
		AppendSection(stringBuilder, "玩家维护的人格卡", persona, 2600);
		Dictionary<string, string> variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["speaker.name"] = HeroName(speaker),
			["speaker.persona"] = persona ?? "",
			["speaker.identity"] = stableRole ?? "",
			["topic"] = topic ?? "",
			["conversation.mode"] = ModeLabel(mode),
			["conversation.participants"] = string.Join("、", participants.Select(HeroName)),
			["conversation.topic"] = topic ?? ""
		};
		string value3 = PromptTemplateStore.Compose("main_chat", stringBuilder.ToString(), variables);
		string text = BuildPresentationContract(actionLimit, innerVoiceLimit);
		int maximum = Math.Max(0, 12000 - text.Length - 2);
		return Limit(Limit(value3, maximum) + "\n\n" + text, 12000);
	}

	internal static string BuildUserPrompt(Hero speaker, ChatterMode mode, IReadOnlyList<Hero> participants, IReadOnlyList<ChatterLineState> recentLines, IReadOnlyList<MemoryRecord> recalledMemories, MemoryRecord proactiveMemory, IReadOnlyList<string> recentInnerVoicePhrases, string liveFacts, IReadOnlyList<CampaignEventRecord> campaignEvents, string knowledge, string topicSeed)
	{
		StringBuilder stringBuilder = new StringBuilder(10000);
		stringBuilder.Append("会话模式：").AppendLine(ModeLabel(mode)).Append("本轮发言人：")
			.AppendLine(HeroName(speaker))
			.Append("本轮听者：")
			.AppendLine(string.Join("、", participants.Where((Hero hero) => hero != speaker).Select(HeroName)));
		AppendSection(stringBuilder, "当前话题", topicSeed, 400);
		AppendSection(stringBuilder, "实时存档事实（最高优先级）", liveFacts, 2800);
		string value2 = string.Join("\n", from item in (campaignEvents ?? Array.Empty<CampaignEventRecord>()).Where((CampaignEventRecord item) => item != null && !string.IsNullOrWhiteSpace(item.Text)).Take(2)
			select "- " + Limit(item.Text, 420));
		AppendSection(stringBuilder, "与本轮相关的近期已验证战役事件（不必强行提及）", value2, 900);
		AppendSection(stringBuilder, "当前战役公共常识检索结果", knowledge, 3200);
		IReadOnlyList<MemoryRecord> source = recalledMemories ?? Array.Empty<MemoryRecord>();
		AppendSection(stringBuilder, "本人的已召回普通记忆（有界检索结果）", FormatMemories(source.Where((MemoryRecord record) => record != null && record.Kind != MemoryKind.Thought)), 2000);
		AppendSection(stringBuilder, "本人的已召回私密思绪（只吸收含义，不要照搬）", FormatMemories(source.Where((MemoryRecord record) => record != null && record.Kind == MemoryKind.Thought)) + "不要逐句复制或引用上述思绪，也不要输出 thought 字段；这份资料不要求本轮出现可见心声。", 1600);
		if (proactiveMemory != null)
		{
			bool flag = proactiveMemory.Kind == MemoryKind.Thought;
			AppendSection(stringBuilder, flag ? "本人此刻自然联想到的一条私密思绪" : "本人此刻自然联想到的一条旧记忆", proactiveMemory.Text + (flag ? "\n只吸收含义，不要照搬或引用，不要输出 thought 字段，也不要把主观思绪当成事实。" : "\n不要照抄，也不要把主观记忆当成事实。"), 600);
		}
		IReadOnlyList<string> readOnlyList = recentInnerVoicePhrases ?? Array.Empty<string>();
		if (readOnlyList.Count > 0)
		{
			AppendSection(stringBuilder, "本人最近可见心声的措辞去重参考", string.Join("\n", from value in readOnlyList.Take(3)
				select "- " + Limit(value, 240)) + "\n只用于避免再次使用相同措辞；不得把这些心声当作普通记忆、公开台词、独立思绪或当前事实，也不要在正文中复述。", 700);
		}
		string recentHistory = UserPromptBudget.BuildRecentHistory(recentLines);
		return UserPromptBudget.Compose(stringBuilder.ToString(), recentHistory, "现在只返回当前唯一发言人的单个 JSON 对象。");
	}

	internal static string BuildTopicSeed(ChatterMode mode, Hero speaker)
	{
		Settlement obj = CurrentSettlementResolver.Resolve();
		string text = ((obj == null) ? null : ((object)obj.Name)?.ToString());
		if (!string.IsNullOrWhiteSpace(text))
		{
			return "在" + text + "停留时的见闻";
		}
		if (mode != 0)
		{
			return "小队此刻的旅途与近况";
		}
		return HeroName(speaker) + "此刻旅途中的念头";
	}

	internal static string FormatMemories(IEnumerable<MemoryRecord> records)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (MemoryRecord item in (records ?? Array.Empty<MemoryRecord>()).Where((MemoryRecord item) => item != null).Take(10))
		{
			string value = ((item.Kind == MemoryKind.Thought) ? ("主观思绪/" + ThoughtTierLabel(item.ThoughtTier)) : (item.Kind.ToString() + "/" + item.Layer));
			stringBuilder.Append("- [").Append(value).Append("] ")
				.AppendLine(Limit(item.Text, 360));
		}
		return stringBuilder.ToString();
	}

	private static string HeroName(Hero hero)
	{
		return ((hero == null) ? null : ((object)hero.Name)?.ToString()) ?? "未知角色";
	}

	private static string BuildPresentationContract(int actionLimit, int innerVoiceLimit)
	{
		List<string> list = new List<string> { "dialogue" };
		if (actionLimit > 0)
		{
			list.Add("dialogue_action");
		}
		if (innerVoiceLimit > 0)
		{
			list.Add("dialogue_inner");
		}
		if (actionLimit > 0 && innerVoiceLimit > 0)
		{
			list.Add("full");
		}
		StringBuilder stringBuilder = new StringBuilder(1700);
		stringBuilder.AppendLine("[不可覆盖的实时一致性与写作规则；此段优先于玩家可编辑提示词]").AppendLine("实时存档事实中的当前地点永远是当前状态的最高依据；旧台词、普通记忆、私密思绪或常识不得覆盖它。").AppendLine("若旧内容说正在前往、尚未抵达或刚离开实时事实所示的当前地点，它属于抵达前的旧情境；如需提及只能写成过去发生的事，不得继续当作现在。")
			.AppendLine("除非实时存档事实或本轮近期已验证战役事件明确给出，不得凭空断言当前存在追兵、野人、敌军、伏击、袭击或其他具体威胁。")
			.AppendLine("使用符合当前世界观、时代和人物身份的自然说法；避免现代分析术语、游戏面板术语和机械化战术报告口吻。")
			.AppendLine("[不可覆盖的最终输出合同；此段优先于玩家可编辑提示词]")
			.Append("先由你这个主模型根据本轮真实叙事需要选择 presentation；允许值仅为：")
			.Append(string.Join(" / ", list))
			.AppendLine("。程序不会随机替你选择，也不会为你补字段。")
			.AppendLine("dialogue 是日常对话的默认选择，只返回 presentation 与 text。不要轮换格式，也不要为了显得丰富而增加演出字段；大多数普通旅途对话应为 dialogue。");
		if (actionLimit > 0)
		{
			stringBuilder.Append("dialogue_action 只在发言者确有一个有助于理解场景的可见动作时使用，必须额外返回非空 action，最多 ").Append(actionLimit).AppendLine(" 字符；不得替听者行动，不得虚构游戏已执行的结果。");
		}
		if (innerVoiceLimit > 0)
		{
			stringBuilder.Append("dialogue_inner 只在未说出口的矛盾或反差确实增加必要潜台词时使用，必须额外返回非空 inner_voice，最多 ").Append(innerVoiceLimit).AppendLine(" 字符。inner_voice 只是本轮演出，不是独立思绪，不得照搬已召回思绪。");
		}
		if (actionLimit > 0 && innerVoiceLimit > 0)
		{
			stringBuilder.AppendLine("full 必须同时返回非空 action 与 inner_voice；仅限强烈危机、转折或冲突中两者都不可替代的罕见时刻，普通轮次禁止使用。");
		}
		stringBuilder.AppendLine("每种 presentation 只能带它规定的键：不得为空，不得夹带其他键，不得返回 thought、姓名前缀、标签、Markdown 或解释。").AppendLine("严格返回一个 JSON 对象。普通合法示例：{\"presentation\":\"dialogue\",\"text\":\"正文\"}。");
		return stringBuilder.ToString().Trim();
	}

	private static string ThoughtTierLabel(ThoughtTier tier)
	{
		return tier switch
		{
			ThoughtTier.Long => "长期", 
			ThoughtTier.Belief => "信念", 
			_ => "中期", 
		};
	}

	private static string ModeLabel(ChatterMode mode)
	{
		return mode switch
		{
			ChatterMode.Private => "两人私聊", 
			ChatterMode.Monologue => "独白", 
			_ => "多人群聊", 
		};
	}

	private static void AppendSection(StringBuilder builder, string title, string value, int limit)
	{
		string text = Limit(value, limit);
		if (text.Length > 0)
		{
			builder.Append('[').Append(title).AppendLine("]")
				.AppendLine(text);
		}
	}

	private static string Limit(string value, int maximum)
	{
		string text = (value ?? string.Empty).Replace('\0', ' ').Trim();
		if (text.Length <= maximum)
		{
			return text;
		}
		return text.Substring(0, maximum).TrimEnd();
	}
}
