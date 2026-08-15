using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BannerlordTalk.Runtime;

internal static class ResponseParser
{
	private static readonly Regex LeadingColonPhrase = new Regex("^\\s*(?<prefix>[^：:\\r\\n]{1,40})[：:]\\s*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	internal static bool TryParse(string raw, int maximumCharacters, int maximumActionCharacters, int maximumInnerVoiceCharacters, out ChatterResponse response, out string error)
	{
		//IL_03b7: Expected O, but got Unknown
		//IL_0129: Unknown result type (might be due to invalid IL or missing references)
		//IL_012f: Invalid comparison between Unknown and I4
		//IL_0149: Unknown result type (might be due to invalid IL or missing references)
		//IL_014f: Invalid comparison between Unknown and I4
		//IL_0180: Unknown result type (might be due to invalid IL or missing references)
		//IL_0186: Invalid comparison between Unknown and I4
		//IL_01ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b2: Invalid comparison between Unknown and I4
		//IL_01b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bc: Invalid comparison between Unknown and I4
		//IL_01cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d3: Invalid comparison between Unknown and I4
		//IL_01f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fc: Invalid comparison between Unknown and I4
		//IL_01d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dd: Invalid comparison between Unknown and I4
		//IL_0200: Unknown result type (might be due to invalid IL or missing references)
		//IL_0206: Invalid comparison between Unknown and I4
		//IL_0216: Unknown result type (might be due to invalid IL or missing references)
		//IL_021d: Invalid comparison between Unknown and I4
		//IL_0221: Unknown result type (might be due to invalid IL or missing references)
		//IL_0227: Invalid comparison between Unknown and I4
		//IL_029a: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a0: Invalid comparison between Unknown and I4
		//IL_02b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_02be: Invalid comparison between Unknown and I4
		response = null;
		error = "";
		if (string.IsNullOrWhiteSpace(raw))
		{
			error = "empty_response";
			return false;
		}
		try
		{
			JObject val = ParseObject(ExtractObject(raw));
			if (HasProperty(val, "speaker_id", "speaker", "name"))
			{
				error = "model_selected_speaker_rejected";
				return false;
			}
			if (HasProperty(val, "lines", "responses"))
			{
				error = "multi_speaker_payload_rejected";
				return false;
			}
			if (FindToken(val, "thought") != null)
			{
				error = "thought_field_rejected";
				return false;
			}
			if (val.Properties().GroupBy((JProperty property) => property.Name, StringComparer.OrdinalIgnoreCase).Any((IGrouping<string, JProperty> group) => group.Count() > 1))
			{
				error = "duplicate_field_rejected";
				return false;
			}
			JToken val2 = FindToken(val, "presentation");
			ChatterPresentation presentation = ChatterPresentation.Dialogue;
			string normalized = string.Empty;
			bool flag = val2 != null && (int)val2.Type == 8 && TryParsePresentation(Extensions.Value<string>((IEnumerable<JToken>)val2), out presentation, out normalized);
			string requestedPresentationName = ((val2 == null) ? "missing" : (((int)val2.Type != 8) ? "non_string" : (flag ? normalized : "unknown")));
			JToken val3 = FindToken(val, "text");
			if (val3 == null || (int)val3.Type != 8)
			{
				error = "text_string_required";
				return false;
			}
			JToken val4 = FindToken(val, "action");
			bool flag2 = val4 != null && (int)val4.Type != 10 && (int)val4.Type != 8;
			if (val4 != null && (int)val4.Type != 10 && (int)val4.Type != 8)
			{
				val4 = null;
			}
			JToken val5 = FindToken(val, "inner_voice");
			bool flag3 = val5 != null && (int)val5.Type != 10 && (int)val5.Type != 8;
			if (val5 != null && (int)val5.Type != 10 && (int)val5.Type != 8)
			{
				val5 = null;
			}
			if (HasLeadingSpeakerPrefix(Extensions.Value<string>((IEnumerable<JToken>)val3) ?? ""))
			{
				error = "speaker_prefix_rejected";
				return false;
			}
			int num = Math.Max(0, Math.Min(160, maximumActionCharacters));
			int num2 = Math.Max(0, Math.Min(240, maximumInnerVoiceCharacters));
			response = new ChatterResponse
			{
				Text = CleanLine(Extensions.Value<string>((IEnumerable<JToken>)val3), maximumCharacters),
				Action = ((val4 == null || (int)val4.Type != 8) ? null : Extensions.Value<string>((IEnumerable<JToken>)val4)),
				InnerVoice = ((val5 == null || (int)val5.Type != 8) ? null : Extensions.Value<string>((IEnumerable<JToken>)val5))
			};
			response.Action = ((val4 == null || num == 0) ? null : CleanScalar(response.Action, num));
			response.InnerVoice = ((val5 == null || num2 == 0) ? null : CleanScalar(response.InnerVoice, num2));
			if (response.Text.Length == 0)
			{
				error = "empty_text";
				response = null;
				return false;
			}
			if (response.Text.Contains("\n") || LooksLikeMultipleSpeakers(response.Text))
			{
				error = "multi_speaker_text_rejected";
				response = null;
				return false;
			}
			if (string.IsNullOrWhiteSpace(response.Action))
			{
				response.Action = null;
			}
			if (string.IsNullOrWhiteSpace(response.InnerVoice))
			{
				response.InnerVoice = null;
			}
			NormalizePresentation(response, flag, presentation, requestedPresentationName, flag2 || flag3);
			return true;
		}
		catch (JsonReaderException val6)
		{
			JsonReaderException val7 = val6;
			error = ((((Exception)(object)val7).Message.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0) ? "duplicate_field_rejected" : "invalid_json");
			return false;
		}
		catch (JsonException)
		{
			error = "invalid_json";
			return false;
		}
		catch
		{
			error = "response_parse_failed";
			return false;
		}
	}

	private static JObject ParseObject(string json)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Expected O, but got Unknown
		using StringReader stringReader = new StringReader(json ?? string.Empty);
		JsonTextReader val = new JsonTextReader((TextReader)stringReader);
		try
		{
			return JObject.Load((JsonReader)(object)val, new JsonLoadSettings
			{
				DuplicatePropertyNameHandling = (DuplicatePropertyNameHandling)2
			});
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static void NormalizePresentation(ChatterResponse response, bool declaredPresentationValid, ChatterPresentation declaredPresentation, string requestedPresentationName, bool optionalFieldTypeRepaired)
	{
		bool flag = !string.IsNullOrWhiteSpace(response.Action);
		bool flag2 = !string.IsNullOrWhiteSpace(response.InnerVoice);
		bool presentationRepaired = optionalFieldTypeRepaired;
		string presentationRepairReason = (optionalFieldTypeRepaired ? "optional_field_type_dropped" : "none");
		ChatterPresentation chatterPresentation;
		if (!declaredPresentationValid)
		{
			response.Action = null;
			response.InnerVoice = null;
			chatterPresentation = ChatterPresentation.Dialogue;
			presentationRepaired = true;
			presentationRepairReason = ((requestedPresentationName == "missing") ? "presentation_missing_defaulted" : "presentation_invalid_defaulted");
		}
		else
		{
			switch (declaredPresentation)
			{
			case ChatterPresentation.Dialogue:
				if (flag || flag2)
				{
					presentationRepaired = true;
					presentationRepairReason = "dialogue_optional_fields_dropped";
				}
				response.Action = null;
				response.InnerVoice = null;
				chatterPresentation = ChatterPresentation.Dialogue;
				break;
			case ChatterPresentation.DialogueAction:
				if (flag2)
				{
					presentationRepaired = true;
					presentationRepairReason = "dialogue_action_inner_voice_dropped";
				}
				response.InnerVoice = null;
				chatterPresentation = (flag ? ChatterPresentation.DialogueAction : ChatterPresentation.Dialogue);
				if (!flag)
				{
					presentationRepaired = true;
					presentationRepairReason = "dialogue_action_missing_action_downgraded";
				}
				break;
			case ChatterPresentation.DialogueInner:
				if (flag)
				{
					presentationRepaired = true;
					presentationRepairReason = "dialogue_inner_action_dropped";
				}
				response.Action = null;
				chatterPresentation = (flag2 ? ChatterPresentation.DialogueInner : ChatterPresentation.Dialogue);
				if (!flag2)
				{
					presentationRepaired = true;
					presentationRepairReason = "dialogue_inner_missing_inner_voice_downgraded";
				}
				break;
			default:
				chatterPresentation = InferPresentation(flag, flag2);
				if (chatterPresentation != ChatterPresentation.Full)
				{
					presentationRepaired = true;
					presentationRepairReason = "full_incomplete_downgraded";
				}
				break;
			}
		}
		response.RequestedPresentationName = requestedPresentationName ?? "missing";
		response.Presentation = chatterPresentation;
		response.PresentationName = PresentationName(chatterPresentation);
		response.PresentationRepaired = presentationRepaired;
		response.PresentationRepairReason = presentationRepairReason;
	}

	private static ChatterPresentation InferPresentation(bool hasAction, bool hasInnerVoice)
	{
		if (hasAction && hasInnerVoice)
		{
			return ChatterPresentation.Full;
		}
		if (hasAction)
		{
			return ChatterPresentation.DialogueAction;
		}
		if (hasInnerVoice)
		{
			return ChatterPresentation.DialogueInner;
		}
		return ChatterPresentation.Dialogue;
	}

	private static string PresentationName(ChatterPresentation presentation)
	{
		return presentation switch
		{
			ChatterPresentation.DialogueAction => "dialogue_action", 
			ChatterPresentation.DialogueInner => "dialogue_inner", 
			ChatterPresentation.Full => "full", 
			_ => "dialogue", 
		};
	}

	private static string ExtractObject(string raw)
	{
		string text = raw.Trim();
		if (text.StartsWith("```", StringComparison.Ordinal))
		{
			int num = text.IndexOf('\n');
			int num2 = text.LastIndexOf("```", StringComparison.Ordinal);
			if (num >= 0 && num2 > num)
			{
				text = text.Substring(num + 1, num2 - num - 1).Trim();
			}
		}
		int num3 = text.IndexOf('{');
		int num4 = text.LastIndexOf('}');
		if (num3 < 0 || num4 <= num3)
		{
			return text;
		}
		return text.Substring(num3, num4 - num3 + 1);
	}

	private static string CleanLine(string text, int maximumCharacters)
	{
		string text2 = (text ?? "").Replace('\0', ' ').Replace("\r\n", "\n").Replace('\r', '\n')
			.Trim();
		int num = Math.Max(1, Math.Min(240, maximumCharacters));
		if (text2.Length <= num)
		{
			return text2;
		}
		return text2.Substring(0, num).TrimEnd();
	}

	private static bool TryParsePresentation(string value, out ChatterPresentation presentation, out string normalized)
	{
		normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
		switch (normalized)
		{
		case "dialogue":
			presentation = ChatterPresentation.Dialogue;
			return true;
		case "dialogue_action":
			presentation = ChatterPresentation.DialogueAction;
			return true;
		case "dialogue_inner":
			presentation = ChatterPresentation.DialogueInner;
			return true;
		case "full":
			presentation = ChatterPresentation.Full;
			return true;
		default:
			presentation = ChatterPresentation.Dialogue;
			normalized = string.Empty;
			return false;
		}
	}

	private static bool HasLeadingSpeakerPrefix(string text)
	{
		Match match = LeadingColonPhrase.Match(text ?? string.Empty);
		if (!match.Success)
		{
			return false;
		}
		string prefix = match.Groups["prefix"].Value.Trim();
		if (prefix.Length == 0)
		{
			return false;
		}
		if (new string[12]
		{
			"是", "如下", "包括", "例如", "比如", "注意", "记住", "总之", "换句话说", "我想说",
			"我要说", "我只说一句"
		}.Any((string suffix) => prefix.EndsWith(suffix, StringComparison.Ordinal)))
		{
			return false;
		}
		string text2 = prefix.ToLowerInvariant();
		if (text2.EndsWith(" is", StringComparison.Ordinal) || text2.EndsWith(" follows", StringComparison.Ordinal) || text2.EndsWith(" namely", StringComparison.Ordinal))
		{
			return false;
		}
		return true;
	}

	private static string CleanScalar(string value, int limit)
	{
		string text = (value ?? "").Replace('\0', ' ').Replace('\r', ' ').Replace('\n', ' ')
			.Trim();
		if (text.Length <= limit)
		{
			return text;
		}
		return text.Substring(0, limit).TrimEnd();
	}

	private static bool LooksLikeMultipleSpeakers(string text)
	{
		if (text.Count((char character) => character == '：' || character == ':') < 2)
		{
			return Regex.IsMatch(text, "[。！？!?，,;；、]\\s*[^，,。！？!?;；、：:\\r\\n]{1,30}[：:]", RegexOptions.CultureInvariant);
		}
		return true;
	}

	private static bool HasProperty(JObject root, params string[] names)
	{
		foreach (string name in names)
		{
			if (root.Properties().FirstOrDefault((JProperty item) => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) != null)
			{
				return true;
			}
		}
		return false;
	}

	private static JToken FindToken(JObject root, string name)
	{
		if (root == null)
		{
			return null;
		}
		JProperty obj = root.Properties().FirstOrDefault((JProperty property) => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase));
		if (obj == null)
		{
			return null;
		}
		return obj.Value;
	}
}
