using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BannerlordTalk.Knowledge;
using BannerlordTalk.Prompts;
using BannerlordTalk.Settings;
using BannerlordTalk.UI;
using MCM.Abstractions;
using MCM.Abstractions.Base;
using MCM.Abstractions.Base.Global;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.ObjectSystem;

namespace BannerlordTalk.Runtime;

internal sealed class ChatterManagerDataSource : IChatterManagerDataSource
{
	private readonly Func<ChatterSaveState> _stateProvider;

	private readonly Func<ChatterMcmSettings> _settingsProvider;

	private readonly Action _knowledgeChanged;

	private readonly Func<ManagerPersonaGenerationRequestData, Action<ManagerPersonaGenerationResultData>, ManagerPersonaGenerationStartData> _beginPersonaGeneration;

	private readonly Action<string> _cancelPersonaGeneration;

	internal ChatterManagerDataSource(Func<ChatterSaveState> stateProvider, Func<ChatterMcmSettings> settingsProvider, Action knowledgeChanged, Func<ManagerPersonaGenerationRequestData, Action<ManagerPersonaGenerationResultData>, ManagerPersonaGenerationStartData> beginPersonaGeneration = null, Action<string> cancelPersonaGeneration = null)
	{
		_stateProvider = stateProvider ?? throw new ArgumentNullException("stateProvider");
		_settingsProvider = settingsProvider ?? throw new ArgumentNullException("settingsProvider");
		_knowledgeChanged = knowledgeChanged;
		_beginPersonaGeneration = beginPersonaGeneration;
		_cancelPersonaGeneration = cancelPersonaGeneration;
	}

	public IReadOnlyList<ManagerHeroData> GetHeroes()
	{
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		List<ManagerHeroData> list = new List<ManagerHeroData>();
		AddHero(list, Hero.MainHero, main: true);
		MobileParty mainParty = MobileParty.MainParty;
		if (((mainParty != null) ? mainParty.MemberRoster : null) != null)
		{
			foreach (TroopRosterElement item in (List<TroopRosterElement>)(object)mainParty.MemberRoster.GetTroopRoster())
			{
				CharacterObject character = item.Character;
				Hero val = ((character != null) ? character.HeroObject : null);
				if (val != null && val != Hero.MainHero && val.IsAlive && !val.IsPrisoner && val.PartyBelongedTo == mainParty && (val.IsPlayerCompanion || val.Clan == Clan.PlayerClan))
				{
					AddHero(list, val, main: false);
				}
			}
		}
		return (from @group in list.GroupBy((ManagerHeroData item) => item.HeroId, StringComparer.Ordinal)
			select @group.First() into item
			orderby item.IsMainHero descending
			select item).ThenBy((ManagerHeroData item) => item.DisplayName, StringComparer.CurrentCulture).ToList();
	}

	public ManagerPersonaData GetPersona(string heroId)
	{
		HeroPersonaState heroPersonaState = PersonaService.Get(State()?.Personas, heroId);
		ChatterMcmSettings chatterMcmSettings = _settingsProvider();
		return new ManagerPersonaData
		{
			HeroId = (heroId ?? ""),
			Persona = (heroPersonaState?.Persona ?? ""),
			SpeakingStyle = (heroPersonaState?.SpeakingStyle ?? ""),
			LongTermGoal = (heroPersonaState?.LongTermGoal ?? ""),
			Values = (heroPersonaState?.Values ?? ""),
			Taboos = (heroPersonaState?.Taboos ?? ""),
			Chattiness = (heroPersonaState?.Chattiness ?? chatterMcmSettings?.DefaultPersonaChattiness ?? 0.5f),
			AutoInitiate = (heroPersonaState?.AutoInitiate ?? chatterMcmSettings?.DefaultPersonaAutoInitiate ?? true)
		};
	}

	public bool SavePersona(ManagerPersonaData value)
	{
		ChatterSaveState chatterSaveState = State();
		if (chatterSaveState == null || value == null || string.IsNullOrWhiteSpace(value.HeroId))
		{
			return false;
		}
		chatterSaveState.Personas = chatterSaveState.Personas ?? new List<HeroPersonaState>();
		return PersonaService.Upsert(chatterSaveState.Personas, new HeroPersonaState
		{
			HeroId = value.HeroId,
			Persona = value.Persona,
			SpeakingStyle = value.SpeakingStyle,
			LongTermGoal = value.LongTermGoal,
			Values = value.Values,
			Taboos = value.Taboos,
			Chattiness = value.Chattiness,
			AutoInitiate = value.AutoInitiate,
			UserEdited = true,
			Source = "manager"
		}) != null;
	}

	public bool ClearPersona(string heroId)
	{
		ChatterSaveState chatterSaveState = State();
		if (chatterSaveState != null)
		{
			return PersonaService.Clear(chatterSaveState.Personas, heroId);
		}
		return false;
	}

	public ManagerPersonaGenerationStartData BeginPersonaGeneration(ManagerPersonaGenerationRequestData request, Action<ManagerPersonaGenerationResultData> completed)
	{
		if (request == null || string.IsNullOrWhiteSpace(request.RequestId) || string.IsNullOrWhiteSpace(request.HeroId) || completed == null)
		{
			return new ManagerPersonaGenerationStartData
			{
				Started = false,
				Status = "未选择角色或人格生成请求无效。"
			};
		}
		if (_beginPersonaGeneration == null)
		{
			return new ManagerPersonaGenerationStartData
			{
				Started = false,
				Status = "当前战役的人格智能生成服务尚未连接。"
			};
		}
		try
		{
			return _beginPersonaGeneration(request, completed) ?? new ManagerPersonaGenerationStartData
			{
				Started = false,
				Status = "人格智能生成服务未返回启动状态。"
			};
		}
		catch (Exception exception)
		{
			Log.Error("persona_generation_start_failed", exception);
			return new ManagerPersonaGenerationStartData
			{
				Started = false,
				Status = "人格智能生成启动失败，原人格草稿未修改。"
			};
		}
	}

	public void CancelPersonaGeneration(string requestId)
	{
		if (string.IsNullOrWhiteSpace(requestId))
		{
			return;
		}
		try
		{
			_cancelPersonaGeneration?.Invoke(requestId);
		}
		catch (Exception exception)
		{
			Log.Error("persona_generation_cancel_failed", exception);
		}
	}

	public IReadOnlyList<ManagerMemoryData> GetMemories(string heroId, string layer, bool thoughtsOnly)
	{
		ChatterSaveState chatterSaveState = State();
		if (chatterSaveState == null || string.IsNullOrWhiteSpace(heroId))
		{
			return Array.Empty<ManagerMemoryData>();
		}
		MemoryLayer result;
		bool flag = Enum.TryParse<MemoryLayer>(layer ?? "", ignoreCase: true, out result);
		IEnumerable<MemoryRecord> source;
		if (!thoughtsOnly)
		{
			source = from record in MemoryService.List(chatterSaveState.MemoryRecords, heroId, flag ? new MemoryLayer?(result) : null)
				where record.Kind != MemoryKind.Thought
				select record;
		}
		else
		{
			IEnumerable<MemoryRecord> enumerable = MemoryService.ListThoughts(chatterSaveState.MemoryRecords, heroId);
			source = enumerable;
		}
		return source.Select(ToManagerMemory).ToList();
	}

	public bool SaveMemory(ManagerMemoryData value)
	{
		ChatterSaveState chatterSaveState = State();
		if (chatterSaveState == null || value == null || string.IsNullOrWhiteSpace(value.OwnerHeroId) || string.IsNullOrWhiteSpace(value.Text))
		{
			return false;
		}
		MemoryKind memoryKind = (string.Equals(value.Kind, "思绪", StringComparison.Ordinal) ? MemoryKind.Thought : ParseEnum(value.Kind, MemoryKind.Event));
		MemoryLayer layer = ParseEnum(value.Layer, MemoryLayer.Situational);
		ThoughtTier thoughtTier = ((memoryKind == MemoryKind.Thought) ? ParseEnum(value.ThoughtTier, ThoughtTier.Mid) : ThoughtTier.None);
		MemoryRecordDraft draft = new MemoryRecordDraft
		{
			OwnerHeroId = value.OwnerHeroId,
			About = (string.IsNullOrWhiteSpace(value.About) ? "玩家手工记录" : value.About),
			Text = value.Text,
			Kind = memoryKind,
			Layer = layer,
			Visibility = ((memoryKind == MemoryKind.Thought) ? MemoryVisibility.Private : MemoryVisibility.Private),
			ThoughtTier = thoughtTier,
			Sentiment = value.Sentiment,
			Strength = value.Strength,
			Importance = value.Importance,
			Confidence = 1f,
			CurrentDay = CurrentCampaignDay(),
			Pinned = value.Pinned,
			Source = "manager"
		};
		chatterSaveState.MemoryRecords = chatterSaveState.MemoryRecords ?? new List<MemoryRecord>();
		if (!string.IsNullOrWhiteSpace(value.RecordId))
		{
			return MemoryService.Update(chatterSaveState.MemoryRecords, value.RecordId, draft, BuildPolicy());
		}
		long nextSequence = chatterSaveState.NextMemorySequence;
		MemoryAppendResult memoryAppendResult = MemoryService.Add(chatterSaveState.MemoryRecords, draft, BuildPolicy(), ref nextSequence);
		chatterSaveState.NextMemorySequence = nextSequence;
		return memoryAppendResult.Added;
	}

	public bool DeleteMemory(string recordId)
	{
		ChatterSaveState chatterSaveState = State();
		if (chatterSaveState != null)
		{
			return MemoryService.Delete(chatterSaveState.MemoryRecords, recordId);
		}
		return false;
	}

	public bool SetMemoryPinned(string recordId, bool pinned)
	{
		ChatterSaveState chatterSaveState = State();
		if (chatterSaveState != null)
		{
			return MemoryService.SetPinned(chatterSaveState.MemoryRecords, recordId, pinned);
		}
		return false;
	}

	public ManagerKnowledgeLibraryData GetKnowledgeLibrary()
	{
		List<KnowledgeRule> list = (State()?.KnowledgeRules ?? new List<KnowledgeRule>()).Where((KnowledgeRule rule) => rule != null).Select(KnowledgeLibraryCodec.CloneRule).ToList();
		KnowledgeLibraryCodec.NormalizeForPersistence(list);
		string text = KnowledgeLibraryCodec.ExportKnowledgeLines(list);
		return new ManagerKnowledgeLibraryData
		{
			RuleCount = list.Count,
			CharacterCount = text.Length,
			KnowledgeText = text
		};
	}

	public ManagerKnowledgeImportPreviewData PreviewKnowledgeReplacement(string content)
	{
		KnowledgeLibraryImportPreview knowledgeLibraryImportPreview = KnowledgeLibraryCodec.Preview(content, State()?.KnowledgeRules);
		return new ManagerKnowledgeImportPreviewData
		{
			ExistingCount = (State()?.KnowledgeRules?.Count((KnowledgeRule rule) => rule != null)).GetValueOrDefault(),
			ParsedCount = knowledgeLibraryImportPreview.Rules.Count,
			SourceCharacterCount = knowledgeLibraryImportPreview.SourceCharacterCount,
			SourceLineCount = knowledgeLibraryImportPreview.SourceLineCount,
			SourceRuleHeaderCount = knowledgeLibraryImportPreview.SourceRuleHeaderCount,
			NormalizedPreview = KnowledgeLibraryCodec.ExportKnowledgeLines(knowledgeLibraryImportPreview.Rules),
			Warnings = string.Join(Environment.NewLine, knowledgeLibraryImportPreview.Warnings),
			Errors = string.Join(Environment.NewLine, knowledgeLibraryImportPreview.Errors),
			CanCommit = knowledgeLibraryImportPreview.CanCommit
		};
	}

	public bool ReplaceKnowledgeLibrary(string content, out string status)
	{
		status = "";
		ChatterSaveState chatterSaveState = State();
		if (chatterSaveState == null)
		{
			status = "当前没有可写入的战役存档。";
			return false;
		}
		KnowledgeLibraryImportPreview knowledgeLibraryImportPreview = KnowledgeLibraryCodec.Preview(content, chatterSaveState.KnowledgeRules ?? new List<KnowledgeRule>());
		if (!knowledgeLibraryImportPreview.CanCommit)
		{
			status = ((knowledgeLibraryImportPreview.Errors.Count > 0) ? string.Join("；", knowledgeLibraryImportPreview.Errors) : "没有识别到可导入的常识。");
			return false;
		}
		List<KnowledgeRule> list = knowledgeLibraryImportPreview.Rules.Where((KnowledgeRule rule) => rule != null).Select(KnowledgeLibraryCodec.CloneRule).ToList();
		KnowledgeLibraryCodec.NormalizeForPersistence(list);
		if (list.Count == 0)
		{
			status = "没有识别到可替换常识库的规则；旧库未改动。";
			return false;
		}
		chatterSaveState.KnowledgeRules = list;
		_knowledgeChanged?.Invoke();
		status = "已原子替换当前战役常识库：" + list.Count + " 条。";
		return true;
	}

	public IReadOnlyList<ManagerPromptTemplateData> GetPromptTemplates()
	{
		return PromptTemplateStore.List().Select(ToManagerPromptTemplate).ToList();
	}

	public ManagerPromptTemplateData GetPromptTemplate(string templateId)
	{
		PromptTemplateSnapshot promptTemplateSnapshot = PromptTemplateStore.Get(templateId);
		if (promptTemplateSnapshot != null)
		{
			return ToManagerPromptTemplate(promptTemplateSnapshot);
		}
		return null;
	}

	public ManagerPromptImportPreviewData PreviewPromptText(string templateId, string content)
	{
		return ToManagerPromptPreview(PromptTemplateStore.PreviewPlainText(templateId, content));
	}

	public ManagerPromptImportPreviewData PreviewPromptJsonImport(string templateId, string content)
	{
		return ToManagerPromptPreview(PromptTemplateStore.PreviewJsonImport(templateId, content));
	}

	public bool SavePromptTemplateText(string templateId, string content, out string status)
	{
		return PromptTemplateStore.SavePlainText(templateId, content, out status);
	}

	public bool ResetPromptTemplate(string templateId, out string status)
	{
		return PromptTemplateStore.Reset(templateId, out status);
	}

	public bool CommitPromptJsonImport(string templateId, string content, out string status)
	{
		return PromptTemplateStore.ImportJson(templateId, content, out status);
	}

	public bool ExportPromptPreset(out string content, out string status)
	{
		return PromptTemplateStore.Export(out content, out status);
	}

	private static ManagerPromptTemplateData ToManagerPromptTemplate(PromptTemplateSnapshot value)
	{
		return new ManagerPromptTemplateData
		{
			TemplateId = value.Id,
			DisplayName = value.DisplayName,
			Template = value.Template,
			Preview = value.Preview,
			HasActualPreview = value.HasActualPreview
		};
	}

	private static ManagerPromptImportPreviewData ToManagerPromptPreview(PromptTemplateImportPreview value)
	{
		if (value == null)
		{
			return null;
		}
		string previewText = string.Join(Environment.NewLine + Environment.NewLine, value.Templates.Select((KeyValuePair<string, string> pair) => "[" + pair.Key + "]" + Environment.NewLine + pair.Value));
		return new ManagerPromptImportPreviewData
		{
			SourceCharacterCount = value.SourceCharacterCount,
			TemplateCount = value.Templates.Count,
			TemplateIds = string.Join(", ", value.Templates.Keys),
			PreviewText = previewText,
			Warnings = string.Join(Environment.NewLine, value.Warnings),
			Errors = string.Join(Environment.NewLine, value.Errors),
			CanCommit = value.CanCommit
		};
	}

	private ChatterSaveState State()
	{
		return _stateProvider();
	}

	private static ManagerMemoryData ToManagerMemory(MemoryRecord record)
	{
		return new ManagerMemoryData
		{
			RecordId = record.RecordId,
			OwnerHeroId = record.OwnerHeroId,
			About = record.About,
			Text = record.Text,
			Kind = record.Kind.ToString(),
			Layer = record.Layer.ToString(),
			ThoughtTier = record.ThoughtTier.ToString(),
			Sentiment = record.Sentiment,
			Strength = record.Strength,
			Importance = record.Importance,
			Pinned = record.Pinned
		};
	}

	private static void AddHero(ICollection<ManagerHeroData> result, Hero hero, bool main)
	{
		if (hero != null && !string.IsNullOrWhiteSpace(((MBObjectBase)hero).StringId))
		{
			result.Add(new ManagerHeroData
			{
				HeroId = ((MBObjectBase)hero).StringId,
				DisplayName = (((object)hero.Name)?.ToString() ?? ((MBObjectBase)hero).StringId),
				IsMainHero = main
			});
		}
	}

	private static MemoryPolicy BuildPolicy()
	{
		ChatterMcmSettings instance = GlobalSettings<ChatterMcmSettings>.Instance;
		int valueOrDefault = (instance?.ThoughtInjectionStrength?.SelectedIndex).GetValueOrDefault(1);
		return new MemoryPolicy
		{
			RecentCapacity = (instance?.MemoryRecentCapacity ?? 6),
			SituationalCapacity = (instance?.MemorySituationalCapacity ?? 20),
			EventLogCapacity = (instance?.MemoryEventCapacity ?? 50),
			ArchiveCapacity = (instance?.MemoryArchiveCapacity ?? 50),
			RecentHalfLifeDays = (instance?.MemoryRecentHalfLifeDays ?? 3),
			SituationalHalfLifeDays = (instance?.MemorySituationalHalfLifeDays ?? 10),
			EventHalfLifeDays = (instance?.MemoryEventHalfLifeDays ?? 45),
			ArchiveHalfLifeDays = (instance?.MemoryArchiveHalfLifeDays ?? 180),
			MinimumRecallScore = (instance?.MemoryMinimumRecallScore ?? 0.12f),
			MidHalfLifeDays = (instance?.ThoughtMidHalfLifeDays ?? 4),
			LongHalfLifeDays = (instance?.ThoughtLongHalfLifeDays ?? 16),
			BeliefHalfLifeDays = (instance?.ThoughtBeliefHalfLifeDays ?? 60),
			MidToLongOccurrences = (instance?.ThoughtMidToLongOccurrences ?? 3),
			MidToLongWindowDays = (instance?.ThoughtMidToLongWindowDays ?? 5),
			LongToBeliefOccurrences = (instance?.ThoughtLongToBeliefOccurrences ?? 3),
			LongToBeliefWindowDays = (instance?.ThoughtLongToBeliefWindowDays ?? 15),
			ThoughtMidRecallBudget = ((valueOrDefault <= 0) ? 2 : ((valueOrDefault >= 2) ? 7 : 4)),
			ThoughtLongRecallBudget = ((valueOrDefault <= 0) ? 2 : ((valueOrDefault >= 2) ? 5 : 3)),
			ThoughtBeliefRecallBudget = ((valueOrDefault <= 0) ? 1 : ((valueOrDefault >= 2) ? 3 : 2))
		};
	}

	private static int CurrentCampaignDay()
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			CampaignTime now = CampaignTime.Now;
			return Math.Max(0, (int)Math.Floor(((CampaignTime)(ref now)).ToDays));
		}
		catch
		{
			return 0;
		}
	}

	private static T ParseEnum<T>(string value, T fallback) where T : struct
	{
		if (!Enum.TryParse<T>(value ?? "", ignoreCase: true, out var result))
		{
			return fallback;
		}
		return result;
	}

	private static string GetTransferDirectory(string name)
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Mount and Blade II Bannerlord", "Configs", "BannerlordTalk", name);
	}

	private static void TrySaveSettings(ChatterMcmSettings settings)
	{
		try
		{
			BaseSettingsProvider instance = BaseSettingsProvider.Instance;
			if (instance != null)
			{
				instance.SaveSettings((BaseSettings)(object)settings);
			}
		}
		catch
		{
		}
	}
}
