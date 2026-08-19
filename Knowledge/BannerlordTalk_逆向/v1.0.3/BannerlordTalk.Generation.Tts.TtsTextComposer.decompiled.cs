using System;
using System.Collections.Generic;
using BannerlordTalk.Runtime;

namespace BannerlordTalk.Generation.Tts;

internal static class TtsTextComposer
{
	internal static string Compose(ChatterLineState line, bool includeText, bool includeAction, bool includeInnerVoice)
	{
		if (line == null)
		{
			return string.Empty;
		}
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["text"] = (includeText ? line.Text : string.Empty),
			["action"] = (includeAction ? line.Action : string.Empty),
			["inner_voice"] = (includeInnerVoice ? line.InnerVoiceText : string.Empty)
		};
		string text = ChatterSaveState.NormalizePresentationOrder(line.PresentationOrder, line.Sequence);
		List<string> parts = new List<string>(3);
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		string[] array = text.Split(',');
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = (array[i] ?? string.Empty).Trim();
			if (hashSet.Add(text2) && dictionary.TryGetValue(text2, out var value))
			{
				AddPart(parts, value);
			}
		}
		array = new string[3] { "text", "action", "inner_voice" };
		foreach (string text3 in array)
		{
			if (hashSet.Add(text3))
			{
				AddPart(parts, dictionary[text3]);
			}
		}
		return TtsTextRules.NormalizeAndLimit(JoinNaturally(parts), 240);
	}

	private static void AddPart(ICollection<string> parts, string value)
	{
		string text = TtsTextRules.NormalizeAndLimit(value, 240);
		if (text.Length > 0)
		{
			parts.Add(text);
		}
	}

	private static string JoinNaturally(IReadOnlyList<string> parts)
	{
		if (parts == null || parts.Count == 0)
		{
			return string.Empty;
		}
		string text = string.Empty;
		for (int i = 0; i < parts.Count; i++)
		{
			string text2 = parts[i] ?? string.Empty;
			if (text2.Length != 0)
			{
				if (text.Length > 0 && !EndsWithSentencePunctuation(text))
				{
					text += "。";
				}
				text += text2;
			}
		}
		return text;
	}

	private static bool EndsWithSentencePunctuation(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return false;
		}
		char c = value[value.Length - 1];
		if (c != '。' && c != '！' && c != '？' && c != '；' && c != '…' && c != '.' && c != '!' && c != '?')
		{
			return c == ';';
		}
		return true;
	}
}
