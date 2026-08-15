using System;
using System.Collections.Generic;
using System.Text;

namespace BannerlordTalk.Runtime;

internal static class UserPromptBudget
{
	internal const int MaximumCharacters = 16000;

	internal const int RecentHistoryCharacters = 2400;

	internal static string BuildRecentHistory(IReadOnlyList<ChatterLineState> recentLines, int maximumCharacters = 2400)
	{
		int num = Math.Max(0, maximumCharacters);
		if (num == 0 || recentLines == null || recentLines.Count == 0)
		{
			return string.Empty;
		}
		List<string> list = new List<string>();
		int num2 = 0;
		for (int num3 = recentLines.Count - 1; num3 >= 0; num3--)
		{
			ChatterLineState chatterLineState = recentLines[num3];
			if (chatterLineState != null && !string.IsNullOrWhiteSpace(chatterLineState.Text))
			{
				string obj = (string.IsNullOrWhiteSpace(chatterLineState.SpeakerName) ? "未知角色" : Bound(chatterLineState.SpeakerName, 80));
				string text = Bound(chatterLineState.Text, 260);
				string text2 = obj + "：" + text + Environment.NewLine;
				if (text2.Length > num - num2)
				{
					break;
				}
				list.Add(text2);
				num2 += text2.Length;
			}
		}
		list.Reverse();
		return string.Concat(list);
	}

	internal static string Compose(string prioritizedContext, string recentHistory, string finalInstruction, int maximumCharacters = 16000)
	{
		int num = Math.Max(1, maximumCharacters);
		string text = BuildSuffix(recentHistory, finalInstruction);
		if (text.Length >= num)
		{
			return text.Substring(text.Length - num, num);
		}
		string text2 = (prioritizedContext ?? string.Empty).TrimEnd();
		int num2 = Environment.NewLine.Length * 2;
		int num3 = Math.Max(0, num - text.Length - num2);
		if (text2.Length > num3)
		{
			text2 = text2.Substring(0, num3).TrimEnd();
		}
		if (text2.Length != 0)
		{
			return text2 + Environment.NewLine + Environment.NewLine + text;
		}
		return text;
	}

	private static string BuildSuffix(string recentHistory, string finalInstruction)
	{
		StringBuilder stringBuilder = new StringBuilder(2560);
		string text = (recentHistory ?? string.Empty).Trim();
		if (text.Length > 0)
		{
			stringBuilder.AppendLine("[本会话最近公开台词；只供衔接，已省略可选演出字段]").AppendLine(text);
		}
		string text2 = (finalInstruction ?? string.Empty).Trim();
		if (text2.Length > 0)
		{
			stringBuilder.AppendLine(text2);
		}
		return stringBuilder.ToString().TrimEnd();
	}

	private static string Bound(string value, int maximum)
	{
		string text = (value ?? string.Empty).Replace('\0', ' ').Trim();
		if (text.Length <= maximum)
		{
			return text;
		}
		return text.Substring(0, maximum).TrimEnd();
	}
}
