using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BannerlordTalk.Knowledge;

internal sealed class StandaloneKnowledgeRetriever : IStandaloneKnowledgeRetriever
{
	private sealed class Candidate
	{
		internal KnowledgeRule Rule { get; set; }

		internal string Content { get; set; } = string.Empty;


		internal int VariantIndex { get; set; }

		internal int VariantSpecificity { get; set; }

		internal Dictionary<string, int> DocumentTokens { get; set; }

		internal double Score { get; set; }

		internal HashSet<string> MatchKinds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);


		internal HashSet<string> MatchedTerms { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	}

	private sealed class PickedVariant
	{
		internal int Index { get; set; } = -1;


		internal int Priority { get; set; } = int.MinValue;


		internal int Specificity { get; set; } = int.MinValue;


		internal string Content { get; set; } = string.Empty;

	}

	private const double Bm25K1 = 1.2;

	private const double Bm25B = 0.75;

	public StandaloneKnowledgeResult Retrieve(StandaloneKnowledgeRequest request)
	{
		if (request != null && request.EnableChaining && request.ChainingRounds > 1)
		{
			return RetrieveChained(request);
		}
		return RetrieveSingleRound(request);
	}

	private StandaloneKnowledgeResult RetrieveSingleRound(StandaloneKnowledgeRequest request)
	{
		if (request == null)
		{
			return StandaloneKnowledgeResult.Empty("knowledge_request_missing");
		}
		List<KnowledgeRule> list = (request.Rules ?? Array.Empty<KnowledgeRule>()).Where((KnowledgeRule rule) => rule != null && rule.Enabled && (!request.RequireCanMatch || rule.CanMatch)).ToList();
		if (list.Count == 0)
		{
			return StandaloneKnowledgeResult.Empty("knowledge_rules_empty");
		}
		KnowledgeSpeakerContext speaker = request.Speaker ?? new KnowledgeSpeakerContext();
		List<Candidate> list2 = new List<Candidate>();
		List<string> list3 = new List<string>();
		foreach (KnowledgeRule item2 in list)
		{
			if (!WhenMatches(item2.Scope, speaker, out var _))
			{
				AddBounded(list3, item2.Id, 128);
				continue;
			}
			PickedVariant pickedVariant = PickVariant(item2, speaker);
			string content = pickedVariant.Content;
			if (content.Length == 0)
			{
				AddBounded(list3, item2.Id, 128);
				continue;
			}
			list2.Add(new Candidate
			{
				Rule = item2,
				Content = ApplyMappings(item2, content, speaker),
				VariantIndex = pickedVariant.Index,
				VariantSpecificity = pickedVariant.Specificity,
				DocumentTokens = BuildDocumentTokens(item2)
			});
		}
		if (list2.Count == 0)
		{
			return new StandaloneKnowledgeResult
			{
				Succeeded = true,
				DiagnosticCode = "knowledge_scope_no_match",
				FilteredRuleIds = list3
			};
		}
		string text = Normalize(request.QueryText, 8000);
		List<string> list4 = NormalizeDistinct(request.ExplicitEntities, 64, 160);
		List<string> list5 = NormalizeDistinct(request.PreferredTerms, 64, 160);
		HashSet<string> hashSet = Tokenize(text);
		foreach (string item3 in list4.Concat(list5))
		{
			foreach (string item4 in Tokenize(item3))
			{
				hashSet.Add(item4);
			}
		}
		Dictionary<string, int> documentFrequency = BuildDocumentFrequency(list2);
		double averageLength = Math.Max(1.0, list2.Average((Candidate value) => Math.Max(1, value.DocumentTokens.Values.Sum())));
		foreach (Candidate item5 in list2)
		{
			ScoreCandidate(item5, text, hashSet, list4, list5, documentFrequency, list2.Count, averageLength);
			if (!MatchModeSatisfied(item5, text, list4, list5))
			{
				item5.Score = 0.0;
				item5.MatchKinds.Clear();
				item5.MatchedTerms.Clear();
			}
		}
		List<Candidate> first = (from value in list2
			where value.Rule.Pinned
			orderby value.Rule.Importance descending
			select value).ThenBy((Candidate value) => value.Rule.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList();
		List<Candidate> second = (from value in list2
			where !value.Rule.Pinned && value.Score > 0.0
			orderby value.Score descending, value.Rule.Importance descending
			select value).ThenBy((Candidate value) => value.Rule.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase).Take(Clamp(request.TopK, 1, 20)).ToList();
		List<StandaloneKnowledgeHit> list6 = new List<StandaloneKnowledgeHit>();
		HashSet<string> hashSet2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		int num = Clamp(request.PinnedCharacterBudget, 0, 6000);
		int num2 = Clamp(request.MaximumCharacters, 100, 12000);
		int num3 = 0;
		int num4 = 0;
		foreach (Candidate item6 in first.Concat(second))
		{
			string text2 = Normalize(item6.Rule.Id, 256);
			if (text2.Length == 0 || !hashSet2.Add(text2))
			{
				continue;
			}
			int num5 = num2 - num4;
			if (item6.Rule.Pinned)
			{
				num5 = Math.Min(num5, num - num3);
			}
			if (num5 <= 0)
			{
				continue;
			}
			string text3 = TrimAtBoundary(item6.Content, num5);
			if (text3.Length != 0)
			{
				StandaloneKnowledgeHit item = new StandaloneKnowledgeHit
				{
					Rule = item6.Rule,
					RuleId = text2,
					Title = GetTitle(item6.Rule),
					Content = text3,
					Score = item6.Score,
					MatchedBy = ((item6.MatchKinds.Count != 0) ? string.Join("+", item6.MatchKinds.OrderBy((string value) => value)) : (item6.Rule.Pinned ? "pinned" : "lexical")),
					MatchedTerms = item6.MatchedTerms.Take(16).ToList(),
					VariantIndex = item6.VariantIndex,
					IsPinned = item6.Rule.Pinned
				};
				list6.Add(item);
				num4 += text3.Length;
				if (item6.Rule.Pinned)
				{
					num3 += text3.Length;
				}
			}
		}
		return new StandaloneKnowledgeResult
		{
			Succeeded = true,
			DiagnosticCode = ((list6.Count > 0) ? "knowledge_lexical_ok" : "knowledge_no_match"),
			Context = BuildContext(list6, num2),
			Hits = list6,
			FilteredRuleIds = list3
		};
	}

	private StandaloneKnowledgeResult RetrieveChained(StandaloneKnowledgeRequest request)
	{
		int num = Clamp(request.ChainingRounds, 1, 5);
		int num2 = Clamp(request.TopK, 1, 20);
		int num3 = Clamp(request.MaximumCharacters, 100, 12000);
		int num4 = Clamp(request.PinnedCharacterBudget, 0, 6000);
		List<StandaloneKnowledgeHit> list = new List<StandaloneKnowledgeHit>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string text = request.QueryText ?? string.Empty;
		int round;
		for (round = 0; round < num; round++)
		{
			int num5 = num2 - list.Count;
			int num6 = list.Sum((StandaloneKnowledgeHit hit) => hit.Content?.Length ?? 0);
			int num7 = num3 - num6;
			if (num5 <= 0 || num7 <= 0 || string.IsNullOrWhiteSpace(text))
			{
				break;
			}
			IReadOnlyList<KnowledgeRule> readOnlyList = (request.Rules ?? Array.Empty<KnowledgeRule>()).Where((KnowledgeRule rule) => rule != null && rule.Enabled && (round == 0 || rule.CanMatch) && !seenIds.Contains(rule.Id ?? string.Empty)).ToList();
			if (readOnlyList.Count == 0)
			{
				break;
			}
			StandaloneKnowledgeRequest obj = new StandaloneKnowledgeRequest
			{
				QueryText = text
			};
			IReadOnlyList<string> explicitEntities;
			if (round != 0)
			{
				IReadOnlyList<string> readOnlyList2 = Array.Empty<string>();
				explicitEntities = readOnlyList2;
			}
			else
			{
				explicitEntities = request.ExplicitEntities;
			}
			obj.ExplicitEntities = explicitEntities;
			IReadOnlyList<string> preferredTerms;
			if (round != 0)
			{
				IReadOnlyList<string> readOnlyList2 = Array.Empty<string>();
				preferredTerms = readOnlyList2;
			}
			else
			{
				preferredTerms = request.PreferredTerms;
			}
			obj.PreferredTerms = preferredTerms;
			obj.Rules = readOnlyList;
			obj.Speaker = request.Speaker;
			obj.TopK = num5;
			obj.MaximumCharacters = num7;
			obj.PinnedCharacterBudget = Math.Max(0, num4 - list.Where((StandaloneKnowledgeHit hit) => hit.IsPinned).Sum((StandaloneKnowledgeHit hit) => hit.Content?.Length ?? 0));
			obj.EnableChaining = false;
			obj.ChainingRounds = 1;
			obj.RequireCanMatch = round > 0;
			StandaloneKnowledgeResult standaloneKnowledgeResult = RetrieveSingleRound(obj);
			foreach (string item in standaloneKnowledgeResult.FilteredRuleIds ?? Array.Empty<string>())
			{
				hashSet.Add(item);
			}
			List<StandaloneKnowledgeHit> list2 = (standaloneKnowledgeResult.Hits ?? Array.Empty<StandaloneKnowledgeHit>()).Where((StandaloneKnowledgeHit hit) => hit != null && seenIds.Add(hit.RuleId ?? string.Empty)).Take(num5).ToList();
			if (list2.Count == 0)
			{
				break;
			}
			list.AddRange(list2);
			text = string.Join("\n", from hit in list2
				where hit.Rule?.CanExtract ?? false
				select hit.Content into value
				where !string.IsNullOrWhiteSpace(value)
				select value);
		}
		return new StandaloneKnowledgeResult
		{
			Succeeded = true,
			DiagnosticCode = ((list.Count > 0) ? "knowledge_chain_ok" : "knowledge_no_match"),
			Context = BuildContext(list, num3),
			Hits = list,
			FilteredRuleIds = hashSet.Take(128).ToList()
		};
	}

	private static void ScoreCandidate(Candidate candidate, string query, ISet<string> queryTokens, IReadOnlyList<string> explicitEntities, IReadOnlyList<string> preferred, IReadOnlyDictionary<string, int> documentFrequency, int documentCount, double averageLength)
	{
		KnowledgeRule rule = candidate.Rule;
		string title = GetTitle(rule);
		string text = Normalize(rule.Id, 256);
		List<string> first = NormalizeDistinct(rule.Keywords, 128, 160);
		List<string> list = NormalizeDistinct(rule.Aliases, 128, 160);
		List<string> list2 = first.Concat(list).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		foreach (string explicitEntity in explicitEntities)
		{
			if (EqualsAny(explicitEntity, list2) || string.Equals(explicitEntity, title, StringComparison.OrdinalIgnoreCase) || string.Equals(explicitEntity, text, StringComparison.OrdinalIgnoreCase))
			{
				candidate.Score += 100.0 + Math.Min(10.0, (double)explicitEntity.Length / 4.0);
				candidate.MatchKinds.Add("entity");
				candidate.MatchedTerms.Add(explicitEntity);
			}
		}
		foreach (string item in list2)
		{
			if (item.Length > 0 && query.IndexOf(item, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				candidate.Score += 36.0 + Math.Min(12.0, (double)item.Length / 2.0);
				candidate.MatchKinds.Add(list.Contains(item, StringComparer.OrdinalIgnoreCase) ? "alias" : "keyword");
				candidate.MatchedTerms.Add(item);
			}
		}
		if (title.Length > 0 && query.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0)
		{
			candidate.Score += 24.0;
			candidate.MatchKinds.Add("title");
			candidate.MatchedTerms.Add(title);
		}
		if (text.Length > 3 && query.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
		{
			candidate.Score += 16.0;
			candidate.MatchKinds.Add("id");
			candidate.MatchedTerms.Add(text);
		}
		foreach (string item2 in preferred)
		{
			if (EqualsAny(item2, list2))
			{
				candidate.Score += 18.0;
				candidate.MatchKinds.Add("preferred");
				candidate.MatchedTerms.Add(item2);
			}
		}
		int num = Math.Max(1, candidate.DocumentTokens.Values.Sum());
		double num2 = 0.0;
		foreach (string queryToken in queryTokens)
		{
			if (candidate.DocumentTokens.TryGetValue(queryToken, out var value) && value > 0)
			{
				documentFrequency.TryGetValue(queryToken, out var value2);
				double num3 = Math.Log(1.0 + ((double)(documentCount - value2) + 0.5) / ((double)value2 + 0.5));
				double num4 = (double)value + 1.2 * (0.25 + 0.75 * (double)num / averageLength);
				num2 += num3 * (double)value * 2.2 / num4;
			}
		}
		if (num2 > 0.0)
		{
			candidate.Score += Math.Min(18.0, num2 * 1.8);
			candidate.MatchKinds.Add("bm25");
		}
		candidate.Score += Clamp(rule.Importance, 0.0, 1.0) * 2.0;
		candidate.Score += (double)candidate.VariantSpecificity * 0.05;
	}

	private static bool MatchModeSatisfied(Candidate candidate, string query, IReadOnlyList<string> explicitEntities, IReadOnlyList<string> preferred)
	{
		if (candidate?.Rule == null || candidate.Rule.Pinned)
		{
			return true;
		}
		List<string> list = NormalizeDistinct((candidate.Rule.Keywords ?? new List<string>()).Concat(candidate.Rule.Tags ?? new List<string>()).Concat(candidate.Rule.Aliases ?? new List<string>()), 128, 160);
		if (list.Count == 0)
		{
			return false;
		}
		HashSet<string> explicitSet = new HashSet<string>((explicitEntities ?? Array.Empty<string>()).Concat(preferred ?? Array.Empty<string>()), StringComparer.OrdinalIgnoreCase);
		Func<string, bool> predicate = (string term) => term.Length > 0 && (query.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 || explicitSet.Contains(term));
		if (!string.Equals(candidate.Rule.MatchMode, "All", StringComparison.OrdinalIgnoreCase))
		{
			return list.Any(predicate);
		}
		return list.All(predicate);
	}

	private static PickedVariant PickVariant(KnowledgeRule rule, KnowledgeSpeakerContext speaker)
	{
		PickedVariant pickedVariant = new PickedVariant();
		List<KnowledgeVariant> list = rule.Variants ?? new List<KnowledgeVariant>();
		for (int i = 0; i < list.Count; i++)
		{
			KnowledgeVariant knowledgeVariant = list[i];
			if (knowledgeVariant != null && knowledgeVariant.Enabled && !string.IsNullOrWhiteSpace(knowledgeVariant.Content) && WhenMatches(knowledgeVariant.When, speaker, out var specificity) && (pickedVariant.Index < 0 || specificity > pickedVariant.Specificity || (specificity == pickedVariant.Specificity && knowledgeVariant.Priority > pickedVariant.Priority)))
			{
				pickedVariant.Index = i;
				pickedVariant.Specificity = specificity;
				pickedVariant.Priority = knowledgeVariant.Priority;
				pickedVariant.Content = Normalize(knowledgeVariant.Content, 12000);
			}
		}
		if (pickedVariant.Index < 0 && !string.IsNullOrWhiteSpace(rule.Content))
		{
			pickedVariant.Content = Normalize(rule.Content, 12000);
			pickedVariant.Specificity = 0;
			pickedVariant.Priority = 0;
		}
		return pickedVariant;
	}

	internal static bool WhenMatches(KnowledgeWhen when, KnowledgeSpeakerContext speaker, out int specificity)
	{
		specificity = 0;
		if (when == null)
		{
			return true;
		}
		speaker = speaker ?? new KnowledgeSpeakerContext();
		if (!MatchList(when.HeroIds, speaker.HeroId, ref specificity) || !MatchList(when.Cultures, speaker.CultureId, ref specificity) || !MatchList(when.KingdomIds, speaker.KingdomId, ref specificity) || !MatchList(when.ClanIds, speaker.ClanId, ref specificity) || !MatchList(when.SettlementIds, speaker.SettlementId, ref specificity) || !MatchList(when.SessionIds, speaker.SessionId, ref specificity))
		{
			return false;
		}
		List<string> list = NormalizeDistinct(when.Roles, 64, 160);
		List<string> list2 = NormalizeDistinct(when.IdentityIds, 64, 160);
		if (list.Count > 0 || list2.Count > 0)
		{
			bool num = list.Any((string value) => string.Equals(value, speaker.Role, StringComparison.OrdinalIgnoreCase));
			HashSet<string> hashSet = new HashSet<string>(speaker.IdentityIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
			if (!string.IsNullOrWhiteSpace(speaker.HeroId))
			{
				hashSet.Add("hero:" + speaker.HeroId.Trim());
				hashSet.Add(speaker.HeroId.Trim());
			}
			if (!string.IsNullOrWhiteSpace(speaker.CharacterId))
			{
				hashSet.Add("char:" + speaker.CharacterId.Trim());
				hashSet.Add(speaker.CharacterId.Trim());
			}
			if (!num && !list2.Any(hashSet.Contains))
			{
				return false;
			}
			specificity++;
		}
		if (when.IsFemale.HasValue)
		{
			if (!speaker.IsFemale.HasValue || speaker.IsFemale.Value != when.IsFemale.Value)
			{
				return false;
			}
			specificity++;
		}
		if (when.IsClanLeader.HasValue)
		{
			if (!speaker.IsClanLeader.HasValue || speaker.IsClanLeader.Value != when.IsClanLeader.Value)
			{
				return false;
			}
			specificity++;
		}
		if (when.SkillMin != null)
		{
			foreach (KeyValuePair<string, int> item in when.SkillMin)
			{
				if (!string.IsNullOrWhiteSpace(item.Key) && item.Value >= 0)
				{
					if (speaker.Skills == null || !speaker.Skills.TryGetValue(item.Key.Trim(), out var value2) || value2 < item.Value)
					{
						return false;
					}
					specificity++;
				}
			}
		}
		return true;
	}

	private static bool MatchList(IEnumerable<string> allowed, string actual, ref int specificity)
	{
		List<string> list = NormalizeDistinct(allowed, 128, 160);
		if (list.Count == 0)
		{
			return true;
		}
		if (string.IsNullOrWhiteSpace(actual) || !list.Any((string value) => string.Equals(value, actual.Trim(), StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}
		specificity++;
		return true;
	}

	private static string ApplyMappings(KnowledgeRule rule, string content, KnowledgeSpeakerContext speaker)
	{
		string text = content ?? string.Empty;
		if (text.Length == 0 || speaker?.MappingValues == null)
		{
			return text;
		}
		foreach (KnowledgeTextMapping item in rule.TextMappings ?? new List<KnowledgeTextMapping>())
		{
			string text2 = Normalize(item?.SourceText, 400);
			string text3 = Normalize(item?.Kind, 160);
			if (text2.Length == 0 || text3.Length == 0)
			{
				continue;
			}
			string text4 = Normalize(item.TargetId, 160);
			string[] obj = ((text4.Length <= 0) ? new string[1] { text3 } : new string[2]
			{
				text3 + ":" + text4,
				text3
			});
			string value = string.Empty;
			string[] array = obj;
			foreach (string key in array)
			{
				if (speaker.MappingValues.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value))
				{
					break;
				}
				value = string.Empty;
			}
			if (value.Length == 0)
			{
				value = item.EmptyValueText ?? string.Empty;
			}
			if (value.Length > 0)
			{
				text = text.Replace(text2, value.Trim());
			}
		}
		return Normalize(text, 12000);
	}

	private static Dictionary<string, int> BuildDocumentTokens(KnowledgeRule rule)
	{
		Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		AddWeightedTokens(dictionary, GetTitle(rule), 3);
		foreach (string item in rule.Keywords ?? new List<string>())
		{
			AddWeightedTokens(dictionary, item, 3);
		}
		foreach (string item2 in rule.Aliases ?? new List<string>())
		{
			AddWeightedTokens(dictionary, item2, 3);
		}
		foreach (string item3 in rule.Tags ?? new List<string>())
		{
			AddWeightedTokens(dictionary, item3, 1);
		}
		foreach (string item4 in rule.RagShortTexts ?? new List<string>())
		{
			AddWeightedTokens(dictionary, item4, 1);
		}
		return dictionary;
	}

	private static void AddWeightedTokens(IDictionary<string, int> counts, string text, int weight)
	{
		foreach (string item in Tokenize(text))
		{
			counts.TryGetValue(item, out var value);
			counts[item] = value + Math.Max(1, weight);
		}
	}

	private static Dictionary<string, int> BuildDocumentFrequency(IEnumerable<Candidate> candidates)
	{
		Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (Candidate candidate in candidates)
		{
			foreach (string key in candidate.DocumentTokens.Keys)
			{
				dictionary.TryGetValue(key, out var value);
				dictionary[key] = value + 1;
			}
		}
		return dictionary;
	}

	private static HashSet<string> Tokenize(string text)
	{
		string text2 = Normalize(text, 16000).ToLowerInvariant();
		HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		StringBuilder word = new StringBuilder();
		StringBuilder cjkRun = new StringBuilder();
		Action action = delegate
		{
			if (word.Length >= 2)
			{
				result.Add(word.ToString());
			}
			word.Clear();
		};
		Action action2 = delegate
		{
			for (int j = 0; j < cjkRun.Length; j++)
			{
				if (j + 1 < cjkRun.Length)
				{
					result.Add(cjkRun.ToString(j, 2));
				}
				if (j + 2 < cjkRun.Length)
				{
					result.Add(cjkRun.ToString(j, 3));
				}
			}
			cjkRun.Clear();
		};
		string text3 = text2;
		foreach (char c in text3)
		{
			if (IsCjk(c))
			{
				action();
				cjkRun.Append(c);
			}
			else if (char.IsLetterOrDigit(c))
			{
				action2();
				word.Append(c);
			}
			else
			{
				action();
				action2();
			}
		}
		action();
		action2();
		return result;
	}

	private static string BuildContext(IEnumerable<StandaloneKnowledgeHit> hits, int maximum)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (StandaloneKnowledgeHit item in hits ?? Enumerable.Empty<StandaloneKnowledgeHit>())
		{
			string text = "【" + item.Title + "】\n" + item.Content;
			if (stringBuilder.Length > 0)
			{
				text = "\n\n" + text;
			}
			int num = maximum - stringBuilder.Length;
			if (num <= 0)
			{
				break;
			}
			stringBuilder.Append(TrimAtBoundary(text, num));
		}
		return stringBuilder.ToString().Trim();
	}

	private static string GetTitle(KnowledgeRule rule)
	{
		string text = Normalize(rule?.Title, 160);
		if (text.Length > 0)
		{
			return text;
		}
		text = (rule?.Keywords ?? new List<string>()).Select((string value) => Normalize(value, 160)).FirstOrDefault((string value) => value.Length > 0);
		return text ?? Normalize(rule?.Id, 160);
	}

	private static bool EqualsAny(string value, IEnumerable<string> candidates)
	{
		return candidates.Any((string candidate) => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));
	}

	private static List<string> NormalizeDistinct(IEnumerable<string> source, int maximumCount, int maximumCharacters)
	{
		return (from value in source ?? Enumerable.Empty<string>()
			select Normalize(value, maximumCharacters) into value
			where value.Length > 0
			select value).Distinct(StringComparer.OrdinalIgnoreCase).Take(maximumCount).ToList();
	}

	private static string Normalize(string value, int maximumCharacters)
	{
		string text = (value ?? string.Empty).Replace('\0', ' ').Replace('\r', ' ').Replace('\n', ' ')
			.Trim();
		text = string.Join(" ", text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
		if (text.Length > maximumCharacters)
		{
			return text.Substring(0, maximumCharacters).TrimEnd();
		}
		return text;
	}

	private static string TrimAtBoundary(string value, int maximumCharacters)
	{
		string text = (value ?? string.Empty).Trim();
		if (maximumCharacters <= 0 || text.Length == 0)
		{
			return string.Empty;
		}
		if (text.Length <= maximumCharacters)
		{
			return text;
		}
		int length = maximumCharacters;
		int num = Math.Max(0, maximumCharacters - 80);
		for (int num2 = maximumCharacters - 1; num2 >= num; num2--)
		{
			if ("。！？；.!?;\n".IndexOf(text[num2]) >= 0)
			{
				length = num2 + 1;
				break;
			}
		}
		return text.Substring(0, length).TrimEnd();
	}

	private static bool IsCjk(char value)
	{
		if (value >= '㐀')
		{
			return value <= '\u9fff';
		}
		return false;
	}

	private static int Clamp(int value, int minimum, int maximum)
	{
		return Math.Max(minimum, Math.Min(maximum, value));
	}

	private static double Clamp(double value, double minimum, double maximum)
	{
		return Math.Max(minimum, Math.Min(maximum, value));
	}

	private static void AddBounded(ICollection<string> values, string value, int maximum)
	{
		if (values.Count < maximum && !string.IsNullOrWhiteSpace(value))
		{
			values.Add(value.Trim());
		}
	}
}
