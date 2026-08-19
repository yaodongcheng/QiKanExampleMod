using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BannerlordTalk.Diagnostics;
using BannerlordTalk.Generation;
using BannerlordTalk.Generation.Tts;
using BannerlordTalk.Knowledge;
using BannerlordTalk.Prompts;
using BannerlordTalk.Settings;
using BannerlordTalk.UI;
using MCM.Abstractions;
using MCM.Abstractions.Base;
using MCM.Abstractions.Base.Global;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace BannerlordTalk.Runtime;

internal sealed class CampaignChatterBehavior : CampaignBehaviorBase
{
	private sealed class RequestSnapshot
	{
		internal Campaign Campaign { get; set; }

		internal string SessionId { get; set; }

		internal string SpeakerId { get; set; }

		internal string SpeakerName { get; set; }

		internal string SettlementId { get; set; } = "";


		internal string SettlementSource { get; set; } = "";


		internal string SettlementMenuId { get; set; } = "";


		internal string SettlementMenuOverlay { get; set; } = "";


		internal bool VerifiedSettlementMenu { get; set; }

		internal bool SettlementCacheFallback { get; set; }

		internal string BattleGateState { get; set; } = "";


		internal string BattleGateReasonCode { get; set; } = "";


		internal bool EffectiveCombat { get; set; }

		internal ChatterMode Mode { get; set; }

		internal bool Automatic { get; set; }

		internal bool PlayerSpeaking { get; set; }

		internal bool AllowAiPlayerSpeech { get; set; }

		internal long RequestSequence { get; set; }

		internal int ParticipantCount { get; set; }

		internal string Topic { get; set; }

		internal Hero Speaker { get; set; }

		internal List<Hero> Participants { get; set; } = new List<Hero>();


		internal List<ChatterLineState> SessionLines { get; set; } = new List<ChatterLineState>();


		internal List<MemoryRecord> RecalledMemories { get; set; } = new List<MemoryRecord>();


		internal List<string> RecentInnerVoicePhrases { get; set; } = new List<string>();


		internal List<CampaignEventRecord> RecalledCampaignEvents { get; set; } = new List<CampaignEventRecord>();


		internal MemoryRecord ProactiveMemory { get; set; }

		internal MemoryPolicy MemoryPolicy { get; set; }

		internal int RelevantMemoryRecallCount { get; set; }

		internal int RelevantThoughtRecallCount { get; set; }

		internal int ProactiveMemoryRecallCount { get; set; }

		internal int ProactiveThoughtRecallCount { get; set; }

		internal string LiveFacts { get; set; }

		internal string Persona { get; set; }

		internal string KnowledgeContext { get; set; } = "";


		internal string KnowledgeReason { get; set; } = "";


		internal string ActualBackend { get; set; } = "";


		internal int KnowledgeQueryTerms { get; set; }

		internal int KnowledgeCandidateCount { get; set; }

		internal int KnowledgeHitCount { get; set; }

		internal double KnowledgeElapsedMilliseconds { get; set; }

		internal string SystemPromptBase { get; set; }

		internal string SystemPrompt { get; set; }

		internal string UserPrompt { get; set; }

		internal int MaximumCharacters { get; set; }

		internal int MaximumActionCharacters { get; set; }

		internal int MaximumInnerVoiceCharacters { get; set; }

		internal int MaximumThoughtCharacters { get; set; }

		internal ThoughtGenerationMode ThoughtMode { get; set; }

		internal int ThoughtGenerationLineThreshold { get; set; }

		internal ChatGenerationOptions ChatOptions { get; set; }

		internal ChatGenerationOptions ThoughtOptions { get; set; }

		internal int GenerationHttpStatusCode { get; set; }

		internal string GenerationFinishReason { get; set; }

		internal long GenerationPromptTokens { get; set; }

		internal long GenerationCompletionTokens { get; set; }

		internal long GenerationTotalTokens { get; set; }

		internal int PendingNextSpeakerIndex { get; set; }

		internal bool PendingInitialSpeakerSelected { get; set; }
	}

	[CompilerGenerated]
	private sealed class <WeightedShuffle>d__62 : IEnumerable<Hero>, IEnumerable, IEnumerator<Hero>, IDisposable, IEnumerator
	{
		private int <>1__state;

		private Hero <>2__current;

		private int <>l__initialThreadId;

		private IEnumerable<Hero> heroes;

		public IEnumerable<Hero> <>3__heroes;

		public CampaignChatterBehavior <>4__this;

		private ChatterMcmSettings settings;

		public ChatterMcmSettings <>3__settings;

		private List<Hero> <remaining>5__2;

		Hero IEnumerator<Hero>.Current
		{
			[DebuggerHidden]
			get
			{
				return <>2__current;
			}
		}

		object IEnumerator.Current
		{
			[DebuggerHidden]
			get
			{
				return <>2__current;
			}
		}

		[DebuggerHidden]
		public <WeightedShuffle>d__62(int <>1__state)
		{
			this.<>1__state = <>1__state;
			<>l__initialThreadId = Environment.CurrentManagedThreadId;
		}

		[DebuggerHidden]
		void IDisposable.Dispose()
		{
			<remaining>5__2 = null;
			<>1__state = -2;
		}

		private bool MoveNext()
		{
			int num = <>1__state;
			CampaignChatterBehavior campaignChatterBehavior = <>4__this;
			switch (num)
			{
			default:
				return false;
			case 0:
				<>1__state = -1;
				<remaining>5__2 = (heroes ?? Array.Empty<Hero>()).Distinct().ToList();
				break;
			case 1:
				<>1__state = -1;
				break;
			}
			if (<remaining>5__2.Count > 0)
			{
				Hero item = campaignChatterBehavior.WeightedPick(<remaining>5__2, settings);
				<remaining>5__2.Remove(item);
				<>2__current = item;
				<>1__state = 1;
				return true;
			}
			return false;
		}

		bool IEnumerator.MoveNext()
		{
			//ILSpy generated this explicit interface implementation from .override directive in MoveNext
			return this.MoveNext();
		}

		[DebuggerHidden]
		void IEnumerator.Reset()
		{
			throw new NotSupportedException();
		}

		[DebuggerHidden]
		IEnumerator<Hero> IEnumerable<Hero>.GetEnumerator()
		{
			<WeightedShuffle>d__62 <WeightedShuffle>d__;
			if (<>1__state == -2 && <>l__initialThreadId == Environment.CurrentManagedThreadId)
			{
				<>1__state = 0;
				<WeightedShuffle>d__ = this;
			}
			else
			{
				<WeightedShuffle>d__ = new <WeightedShuffle>d__62(0)
				{
					<>4__this = <>4__this
				};
			}
			<WeightedShuffle>d__.heroes = <>3__heroes;
			<WeightedShuffle>d__.settings = <>3__settings;
			return <WeightedShuffle>d__;
		}

		[DebuggerHidden]
		IEnumerator IEnumerable.GetEnumerator()
		{
			return ((IEnumerable<Hero>)this).GetEnumerator();
		}
	}

	private const string SaveKey = "BannerlordTalk_State_v1";

	private readonly Stopwatch _wallClock = Stopwatch.StartNew();

	private readonly Random _random = new Random();

	private readonly BattleGatePolicy _battleGate = new BattleGatePolicy();

	private readonly StandaloneKnowledgeRetriever _knowledgeRetriever = new StandaloneKnowledgeRetriever();

	private readonly object _stateSync = new object();

	private ChatterSaveState _state = new ChatterSaveState();

	private OpenAICompatibleChatClient _chatClient = new OpenAICompatibleChatClient();

	private OpenAICompatibleChatClient _thoughtChatClient = new OpenAICompatibleChatClient();

	private IndependentThoughtGenerationClient _thoughtClient;

	private OpenAICompatibleChatClient _summaryChatClient = new OpenAICompatibleChatClient();

	private IndependentMemorySummaryClient _summaryClient;

	private TtsPlaybackService _ttsPlaybackService;

	private CancellationTokenSource _requestCancellation;

	private readonly List<CancellationTokenSource> _thoughtCancellations = new List<CancellationTokenSource>();

	private readonly List<CancellationTokenSource> _summaryCancellations = new List<CancellationTokenSource>();

	private CancellationTokenSource _personaGenerationCancellation;

	private string _personaGenerationRequestId = "";

	private long _personaGenerationEpoch;

	private readonly HashSet<string> _summaryOwnersInFlight = new HashSet<string>(StringComparer.Ordinal);

	private long _nextRequestAtMilliseconds;

	private int _consecutiveTransientFailures;

	private long _requestSequence;

	private long _activeRequestSequence;

	private bool _activeRequestTerminalPublished;

	private RequestSnapshot _activeSnapshot;

	private long _epoch;

	private bool _requestInFlight;

	private bool _loaded;

	private bool _battleEventLock;

	private MapEvent _lockedPlayerMapEvent;

	private bool _battleFeedCursorInitialized;

	private long _battleArchiveCursor;

	private long _nextBattleFeedBindAttemptAtMilliseconds;

	private long _nextBattleArchivePollAtMilliseconds;

	private string _lastBattleFeedError = "";

	private long _nextTtsSettingsRefreshAtMilliseconds;

	private string _ttsSettingsFingerprint = "";

	private string _lastBattleActivityFingerprint = "";

	internal IReadOnlyList<ChatterLineState> Lines => _state.Lines;

	public override void RegisterEvents()
	{
		CampaignEvents.OnGameLoadFinishedEvent.AddNonSerializedListener((object)this, (Action)OnCampaignReady);
		CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener((object)this, (Action<CampaignGameStarter>)delegate
		{
			OnCampaignReady();
		});
		CampaignEvents.MapEventStarted.AddNonSerializedListener((object)this, (Action<MapEvent, PartyBase, PartyBase>)OnMapEventStarted);
		CampaignEvents.BattleStarted.AddNonSerializedListener((object)this, (Action<PartyBase, PartyBase, object, bool>)OnBattleStarted);
		CampaignEvents.OnMissionStartedEvent.AddNonSerializedListener((object)this, (Action<IMission>)OnMissionStarted);
		CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener((object)this, (Action<IMission>)OnMissionEnded);
		CampaignEvents.MapEventEnded.AddNonSerializedListener((object)this, (Action<MapEvent>)OnMapEventEnded);
		CampaignEvents.AfterSettlementEntered.AddNonSerializedListener((object)this, (Action<MobileParty, Settlement, Hero>)OnSettlementEntered);
		CampaignEvents.OnSettlementLeftEvent.AddNonSerializedListener((object)this, (Action<MobileParty, Settlement>)OnSettlementLeft);
		CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener((object)this, (Action<Settlement, bool, Hero, Hero, Hero, ChangeOwnerOfSettlementDetail>)OnSettlementOwnerChanged);
		CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener((object)this, (Action<PartyBase, Hero>)OnHeroPrisonerTaken);
		CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener((object)this, (Action<Hero, PartyBase, IFaction, EndCaptivityDetail, bool>)OnHeroPrisonerReleased);
		CampaignEvents.HeroKilledEvent.AddNonSerializedListener((object)this, (Action<Hero, Hero, KillCharacterActionDetail, bool>)OnHeroKilled);
	}

	public override void SyncData(IDataStore dataStore)
	{
		string json = (dataStore.IsSaving ? SnapshotForSave().ToJson() : "");
		bool flag = dataStore.SyncData<string>("BannerlordTalk_State_v1", ref json);
		if (!dataStore.IsLoading)
		{
			return;
		}
		try
		{
			_state = (flag ? ChatterSaveState.FromJson(json) : new ChatterSaveState());
		}
		catch (Exception exception)
		{
			_state = new ChatterSaveState();
			Log.Error("save_state_parse_failed", exception);
		}
	}

	internal void ApplicationTick()
	{
		MainThreadDispatcher.Drain();
		ChatterMcmSettings instance = GlobalSettings<ChatterMcmSettings>.Instance;
		RefreshTtsSettingsAndRoster(instance);
		bool flag = IsSafeMapScreen();
		CurrentSettlementContext currentSettlementContext = CurrentSettlementResolver.ResolveContext();
		Settlement settlement = currentSettlementContext.Settlement;
		ChatterOverlay.Tick(flag, IsPaused(), flag && settlement != null, instance);
		if (_loaded && !_battleFeedCursorInitialized)
		{
			TryInitializeBattleFeedCursor(instance);
		}
		BattleGateEvaluation evaluation = EvaluateBattleGate(instance, currentSettlementContext);
		if (_requestInFlight && HasLocationContextChanged(_activeSnapshot, currentSettlementContext))
		{
			CancelForLocationChange();
		}
		else if (_requestInFlight && !evaluation.IsAllowed)
		{
			CancelForBattle(evaluation);
		}
		if (_loaded && flag && evaluation.IsAllowed)
		{
			PollVerifiedBattleArchives(instance);
		}
		if (_loaded && instance != null && instance.Enabled && flag && !_requestInFlight && _personaGenerationCancellation == null && (instance.AllowWhilePaused || !IsPaused()) && _wallClock.ElapsedMilliseconds >= _nextRequestAtMilliseconds)
		{
			TryStartRequest(null, automatic: true);
		}
	}

	internal void RequestMode(int rawMode)
	{
		if (rawMode < 0 || rawMode > 2)
		{
			return;
		}
		try
		{
			TryStartRequest((ChatterMode)rawMode, automatic: false);
		}
		catch (Exception exception)
		{
			Log.Error("manual_chat_request_failed mode=" + rawMode, exception);
			ScheduleShortRecheck();
			PublishBlocked("manual_request_exception", automatic: false, (ChatterMode)rawMode);
		}
	}

	internal void Shutdown(string reason)
	{
		_requestCancellation?.Cancel();
		CancelThoughtJobs();
		CancelSummaryJobs();
		CancelPersonaGeneration(_personaGenerationRequestId);
		PublishTerminalIfNeeded("cancelled", reason ?? "runtime_shutdown");
		_epoch++;
		_loaded = false;
		_requestInFlight = false;
		_requestCancellation?.Dispose();
		_requestCancellation = null;
		_activeSnapshot = null;
		_activeRequestSequence = 0L;
		_activeRequestTerminalPublished = false;
		_consecutiveTransientFailures = 0;
		_chatClient?.Dispose();
		_chatClient = null;
		_thoughtChatClient?.Dispose();
		_thoughtChatClient = null;
		_thoughtClient = null;
		_summaryChatClient?.Dispose();
		_summaryChatClient = null;
		_summaryClient = null;
		_ttsPlaybackService?.Dispose();
		_ttsPlaybackService = null;
		_battleGate.Reset();
		_battleEventLock = false;
		_lockedPlayerMapEvent = null;
		_battleFeedCursorInitialized = false;
		_battleArchiveCursor = 0L;
		_nextBattleFeedBindAttemptAtMilliseconds = 0L;
		_nextBattleArchivePollAtMilliseconds = 0L;
		_lastBattleFeedError = "";
		_nextTtsSettingsRefreshAtMilliseconds = 0L;
		_ttsSettingsFingerprint = "";
		_lastBattleActivityFingerprint = "";
		CurrentSettlementResolver.Reset();
		MainThreadDispatcher.Clear();
		ChatterManagerPanel.ConfigureDataSource(null);
		ChatterOverlay.Reset();
		Log.Info("runtime_shutdown reason=" + reason);
	}

	private void OnCampaignReady()
	{
		if (_loaded && Campaign.Current != null)
		{
			return;
		}
		_requestCancellation?.Cancel();
		CancelThoughtJobs();
		CancelSummaryJobs();
		CancelPersonaGeneration(_personaGenerationRequestId);
		PublishTerminalIfNeeded("cancelled", "campaign_changed");
		_epoch++;
		_loaded = true;
		_requestInFlight = false;
		_requestCancellation?.Dispose();
		_requestCancellation = null;
		_activeSnapshot = null;
		_activeRequestSequence = 0L;
		_activeRequestTerminalPublished = false;
		_consecutiveTransientFailures = 0;
		_battleGate.Reset();
		_battleEventLock = false;
		_lockedPlayerMapEvent = null;
		_battleFeedCursorInitialized = false;
		_battleArchiveCursor = 0L;
		_nextBattleFeedBindAttemptAtMilliseconds = 0L;
		_nextBattleArchivePollAtMilliseconds = 0L;
		_lastBattleFeedError = "";
		_lastBattleActivityFingerprint = "";
		CurrentSettlementResolver.Reset();
		if (_chatClient == null)
		{
			_chatClient = new OpenAICompatibleChatClient();
		}
		if (_thoughtChatClient == null)
		{
			_thoughtChatClient = new OpenAICompatibleChatClient();
		}
		if (_summaryChatClient == null)
		{
			_summaryChatClient = new OpenAICompatibleChatClient();
		}
		_thoughtClient = new IndependentThoughtGenerationClient(_thoughtChatClient);
		_summaryClient = new IndependentMemorySummaryClient(_summaryChatClient);
		ChatterMcmSettings instance = GlobalSettings<ChatterMcmSettings>.Instance;
		if ((instance?.ApplyLegacyMigrationOnce() ?? false) | RefreshTtsVoiceSlots(instance))
		{
			try
			{
				BaseSettingsProvider instance2 = BaseSettingsProvider.Instance;
				if (instance2 != null)
				{
					instance2.SaveSettings((BaseSettings)(object)instance);
				}
			}
			catch
			{
			}
		}
		_ttsPlaybackService?.Dispose();
		_ttsPlaybackService = new TtsPlaybackService(16, delegate(string reason)
		{
			LogDiagnostic(GlobalSettings<ChatterMcmSettings>.Instance, "tts_" + SafeReason(reason));
		});
		_ttsSettingsFingerprint = ComputeTtsSettingsFingerprint(instance);
		_nextTtsSettingsRefreshAtMilliseconds = _wallClock.ElapsedMilliseconds + 5000;
		NormalizeState(GlobalSettings<ChatterMcmSettings>.Instance);
		ChatterManagerPanel.ConfigureDataSource(new ChatterManagerDataSource(() => _state, () => GlobalSettings<ChatterMcmSettings>.Instance, InvalidateKnowledgeCache, BeginPersonaGeneration, CancelPersonaGeneration));
		ScheduleNext(success: true, initial: true);
		PublishAllLines();
		ChatterOverlay.Show();
		TryInitializeBattleFeedCursor(GlobalSettings<ChatterMcmSettings>.Instance);
		Log.Info("campaign_ready lines=" + _state.Lines.Count + " memories=" + _state.MemoryRecords.Count);
	}

	private void TryStartRequest(ChatterMode? requestedMode, bool automatic)
	{
		ChatterMcmSettings settings = GlobalSettings<ChatterMcmSettings>.Instance;
		if (settings == null || !settings.Enabled || _requestInFlight || _personaGenerationCancellation != null || !IsSafeMapScreen())
		{
			return;
		}
		CurrentSettlementContext currentSettlementContext = CurrentSettlementResolver.ResolveContext();
		string locationContextKey = ConversationContextPolicy.BuildLocationContextKey(SettlementId(currentSettlementContext));
		ApplyLocationContextBoundary(locationContextKey);
		BattleGateEvaluation battleGateEvaluation = EvaluateBattleGate(settings, currentSettlementContext);
		if (!battleGateEvaluation.IsAllowed)
		{
			ScheduleShortRecheck();
			PublishBlocked(battleGateEvaluation.ReasonCode, automatic, requestedMode, currentSettlementContext, battleGateEvaluation);
			return;
		}
		ChatGenerationOptions chatGenerationOptions = BuildChatOptions(settings);
		if (string.IsNullOrWhiteSpace(chatGenerationOptions.Endpoint) || string.IsNullOrWhiteSpace(chatGenerationOptions.Model))
		{
			ScheduleShortRecheck();
			PublishBlocked("chat_not_configured", automatic, requestedMode);
			return;
		}
		if (!settings.AllowWhilePaused && IsPaused())
		{
			ScheduleShortRecheck();
			PublishBlocked("paused", automatic, requestedMode);
			return;
		}
		if (!settings.AllowAtSea)
		{
			MobileParty mainParty = MobileParty.MainParty;
			if (mainParty != null && mainParty.IsCurrentlyAtSea)
			{
				ScheduleShortRecheck();
				PublishBlocked("at_sea", automatic, requestedMode);
				return;
			}
		}
		if (!settings.AllowInSettlement && currentSettlementContext.Settlement != null)
		{
			ScheduleShortRecheck();
			PublishBlocked("in_settlement", automatic, requestedMode);
			return;
		}
		List<Hero> partyCompanions = GetPartyCompanions();
		if (settings.AllowAiPlayerSpeech && IsEligiblePlayer(Hero.MainHero))
		{
			partyCompanions.Add(Hero.MainHero);
		}
		if (partyCompanions.Count == 0)
		{
			ScheduleShortRecheck();
			PublishBlocked("no_eligible_companions", automatic, requestedMode);
			return;
		}
		ConversationSessionState conversationSessionState = ResolveSession(requestedMode, partyCompanions, settings, automatic, locationContextKey);
		if (conversationSessionState == null)
		{
			ScheduleShortRecheck();
			PublishBlocked("session_unavailable", automatic, requestedMode);
			return;
		}
		List<Hero> list = (from hero in conversationSessionState.ParticipantIds.Select(ResolveHero)
			where IsEligibleParticipant(hero, settings.AllowAiPlayerSpeech)
			select hero).Distinct().ToList();
		int num = ((conversationSessionState.Mode == ChatterMode.Monologue) ? 1 : ((conversationSessionState.Mode == ChatterMode.Private) ? 2 : 3));
		if (list.Count < num)
		{
			_state.ActiveSession = null;
			ScheduleShortRecheck();
			PublishBlocked("session_participants_invalid", automatic, conversationSessionState.Mode);
			return;
		}
		Hero val = SelectSpeaker(conversationSessionState, list, automatic, settings);
		if (val == null)
		{
			_state.ActiveSession = null;
			ScheduleShortRecheck();
			PublishBlocked("speaker_unavailable", automatic, conversationSessionState.Mode);
			return;
		}
		long num2 = ++_requestSequence;
		Stopwatch stopwatch = Stopwatch.StartNew();
		RequestSnapshot requestSnapshot = CaptureRequestSnapshot(conversationSessionState, val, list, settings, automatic, num2, val == Hero.MainHero, settings.AllowAiPlayerSpeech);
		CaptureLocationContext(requestSnapshot, currentSettlementContext);
		CaptureBattleContext(requestSnapshot, battleGateEvaluation);
		int num3 = list.IndexOf(val);
		requestSnapshot.PendingNextSpeakerIndex = ((num3 < 0) ? conversationSessionState.NextSpeakerIndex : ((num3 + 1) % list.Count));
		requestSnapshot.PendingInitialSpeakerSelected = true;
		_requestInFlight = true;
		_requestCancellation = new CancellationTokenSource();
		CancellationToken token = _requestCancellation.Token;
		long epoch = _epoch;
		_activeRequestSequence = num2;
		_activeRequestTerminalPublished = false;
		_activeSnapshot = requestSnapshot;
		PublishProbe(requestSnapshot, "RequestAccepted", "accepted", "", "", 0, 0, 0, cacheHit: false, 0.0, 0.0, null);
		PublishProbe(requestSnapshot, "SnapshotCaptured", "completed", "", "main_thread", requestSnapshot.KnowledgeQueryTerms, requestSnapshot.KnowledgeCandidateCount, 0, cacheHit: false, stopwatch.Elapsed.TotalMilliseconds, stopwatch.Elapsed.TotalMilliseconds, new ChatterProbeContentDraft
		{
			AdditionalContext = BuildLocationProbeContext(requestSnapshot)
		});
		StartKnowledgeAndGeneration(requestSnapshot, epoch, token);
	}

	private RequestSnapshot CaptureRequestSnapshot(ConversationSessionState session, Hero speaker, List<Hero> participants, ChatterMcmSettings settings, bool automatic, long requestSequence, bool playerSpeaking, bool allowAiPlayerSpeech)
	{
		List<ChatterLineState> list = _state.Lines.Where((ChatterLineState line) => line.Sequence > 0 && string.Equals(line.SessionId, session.SessionId, StringComparison.Ordinal)).TakeLastCompat(Math.Min(20, settings.HistoryLineLimit)).ToList();
		string text = (string.IsNullOrWhiteSpace(session.TopicSeed) ? PromptBuilder.BuildTopicSeed(session.Mode, speaker) : session.TopicSeed);
		session.TopicSeed = text;
		int currentDay = CurrentCampaignDay();
		MemoryPolicy memoryPolicy = BuildMemoryPolicy(settings);
		IReadOnlyList<MemoryRecord> records = ConversationContextPolicy.ExcludeSessionLineMemories(_state.MemoryRecords, ((MBObjectBase)speaker).StringId, list);
		MemoryRecallResult memoryRecallResult = MemoryRecallSelector.Select(new MemoryRecallRequest
		{
			OwnerHeroId = ((MBObjectBase)speaker).StringId,
			ViewerHeroId = ((MBObjectBase)speaker).StringId,
			TopicText = text + "\n" + string.Join("\n", list.Select((ChatterLineState line) => line.Text)),
			Records = records,
			CurrentDay = currentDay,
			MaximumResults = settings.MemoryMaximumInjected,
			MaximumThoughtResults = settings.ThoughtRecallMaxPerTurn,
			IncludePrivate = true,
			EnableProactiveRecall = settings.EnableProactiveThoughtRecall,
			ProactiveChancePercent = settings.ProactiveThoughtRecallChancePercent,
			ProactiveRoll = _random.NextDouble(),
			Policy = memoryPolicy
		});
		CampaignEventScopeMode scopeMode = ResolveCampaignEventScope(settings);
		CampaignEventRecallResult campaignEventRecallResult = CampaignEventMemoryService.SelectForInjection(_state.CampaignEventRecords, new CampaignEventRecallRequest
		{
			ScopeMode = scopeMode,
			Context = BuildCampaignEventRelationContext(speaker, participants, text),
			CurrentDay = currentDay,
			CurrentTurn = requestSequence,
			MaximumResults = settings.CampaignEventMaximumInjected
		});
		HeroPersonaState persona = PersonaService.Get(_state.Personas, ((MBObjectBase)speaker).StringId);
		IReadOnlyList<string> source = ConversationContextPolicy.SelectRecentInnerVoicePhrases(_state.Lines, ((MBObjectBase)speaker).StringId, 3);
		string liveFacts = NativeHeroContextProvider.BuildLiveFacts(speaker, participants, text, settings.MatchKnowledgeFromQuest);
		ThoughtGenerationMode thoughtMode = ThoughtGenerationModeResolver.Resolve(settings.ThoughtGenerationMode?.SelectedIndex ?? 1);
		int maxActionCharacters = settings.MaxActionCharacters;
		RequestSnapshot requestSnapshot = new RequestSnapshot
		{
			Campaign = Campaign.Current,
			SessionId = session.SessionId,
			SpeakerId = ((MBObjectBase)speaker).StringId,
			SpeakerName = (((object)speaker.Name)?.ToString() ?? ((MBObjectBase)speaker).StringId),
			Mode = session.Mode,
			Automatic = automatic,
			PlayerSpeaking = playerSpeaking,
			AllowAiPlayerSpeech = allowAiPlayerSpeech,
			RequestSequence = requestSequence,
			ParticipantCount = participants.Count,
			Topic = text,
			Speaker = speaker,
			Participants = participants.ToList(),
			SessionLines = list,
			RecalledMemories = memoryRecallResult.RelevantRecords.ToList(),
			RecalledCampaignEvents = campaignEventRecallResult.Records.ToList(),
			RecentInnerVoicePhrases = source.ToList(),
			ProactiveMemory = memoryRecallResult.ProactiveRecord,
			RelevantMemoryRecallCount = memoryRecallResult.RelevantRecords.Count((MemoryRecord record) => record.Kind != MemoryKind.Thought),
			RelevantThoughtRecallCount = memoryRecallResult.RelevantRecords.Count((MemoryRecord record) => record.Kind == MemoryKind.Thought),
			ProactiveMemoryRecallCount = ((memoryRecallResult.ProactiveRecord != null && memoryRecallResult.ProactiveRecord.Kind != MemoryKind.Thought) ? 1 : 0),
			ProactiveThoughtRecallCount = ((memoryRecallResult.ProactiveRecord != null && memoryRecallResult.ProactiveRecord.Kind == MemoryKind.Thought) ? 1 : 0),
			LiveFacts = liveFacts,
			Persona = BuildPersonaText(persona),
			MemoryPolicy = memoryPolicy,
			ThoughtMode = thoughtMode,
			ThoughtGenerationLineThreshold = Math.Max(1, settings.ThoughtGenerationLineThreshold),
			MaximumCharacters = settings.MaxLineCharacters,
			MaximumActionCharacters = maxActionCharacters,
			MaximumInnerVoiceCharacters = settings.MaxInnerVoiceCharacters,
			MaximumThoughtCharacters = settings.MaxThoughtCharacters,
			ChatOptions = BuildChatOptions(settings),
			ThoughtOptions = BuildThoughtOptions(settings)
		};
		requestSnapshot.SystemPromptBase = PromptBuilder.BuildSystemPrompt(speaker, session.Mode, participants, NativeHeroContextProvider.BuildStableRole(speaker), requestSnapshot.Persona, text, settings, playerSpeaking, maxActionCharacters, requestSnapshot.MaximumInnerVoiceCharacters);
		PrepareKnowledge(requestSnapshot, settings, memoryRecallResult.AllRecords);
		requestSnapshot.UserPrompt = PromptBuilder.BuildUserPrompt(speaker, session.Mode, participants, list, requestSnapshot.RecalledMemories, requestSnapshot.ProactiveMemory, requestSnapshot.RecentInnerVoicePhrases, liveFacts, requestSnapshot.RecalledCampaignEvents, requestSnapshot.KnowledgeContext, text);
		PromptTemplateStore.RecordActualPreview("main_chat", requestSnapshot.SystemPromptBase, requestSnapshot.UserPrompt);
		return requestSnapshot;
	}

	private void PrepareKnowledge(RequestSnapshot snapshot, ChatterMcmSettings settings, IReadOnlyList<MemoryRecord> recalled)
	{
		snapshot.ActualBackend = (settings.EnableKnowledge ? "standalone_lexical" : "disabled");
		if (!settings.EnableKnowledge)
		{
			snapshot.KnowledgeReason = "knowledge_disabled";
			return;
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		try
		{
			IReadOnlyList<KnowledgeRule> knowledgeRules = _state.KnowledgeRules;
			IReadOnlyList<KnowledgeRule> readOnlyList = knowledgeRules ?? Array.Empty<KnowledgeRule>();
			snapshot.KnowledgeCandidateCount = readOnlyList.Count;
			if (readOnlyList.Count == 0)
			{
				snapshot.KnowledgeReason = "knowledge_library_empty";
				snapshot.KnowledgeElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
				return;
			}
			Settlement obj = CurrentSettlementResolver.Resolve();
			string text = ((obj == null) ? null : ((object)obj.Name)?.ToString()) ?? "";
			List<string> list = new List<string>();
			List<string> list2 = new List<string>();
			List<string> list3 = new List<string>();
			if (settings.MatchKnowledgeFromCurrentTopic)
			{
				list.Add(snapshot.Topic);
			}
			if (settings.MatchKnowledgeFromRecentDialogue)
			{
				list.AddRange(from line in snapshot.SessionLines.TakeLastCompat(6)
					select line.Text);
			}
			if (settings.MatchKnowledgeFromSpeakerIdentity)
			{
				list.Add(snapshot.SpeakerName);
				list.Add(snapshot.LiveFacts);
				list2.Add(snapshot.SpeakerName);
			}
			if (settings.MatchKnowledgeFromParticipants)
			{
				list2.AddRange(snapshot.Participants.Select((Hero hero) => ((object)hero.Name)?.ToString()));
			}
			if (settings.MatchKnowledgeFromLocation && text.Length > 0)
			{
				list.Add(text);
				list2.Add(text);
			}
			if (settings.MatchKnowledgeFromMemory)
			{
				list.AddRange(from item in recalled ?? Array.Empty<MemoryRecord>()
					where item.Kind != MemoryKind.Thought
					select item.Text);
			}
			if (settings.MatchKnowledgeFromThoughts)
			{
				list.AddRange(from item in recalled ?? Array.Empty<MemoryRecord>()
					where item.Kind == MemoryKind.Thought
					select item.Text);
			}
			if (settings.MatchKnowledgeFromRecentBattle)
			{
				list.AddRange(from item in recalled ?? Array.Empty<MemoryRecord>()
					where item.Kind == MemoryKind.Battle
					select item.Text);
			}
			if (settings.MatchKnowledgeFromQuest)
			{
				IReadOnlyList<string> matchingQuestTitles = NativeHeroContextProvider.GetMatchingQuestTitles(snapshot.Topic);
				list.AddRange(matchingQuestTitles);
				list3.AddRange(matchingQuestTitles);
			}
			string text2 = Bound(string.Join("\n", list.Where((string value) => !string.IsNullOrWhiteSpace(value))), 8000);
			snapshot.KnowledgeQueryTerms = MemoryService.Tokenize(text2).Count;
			StandaloneKnowledgeResult standaloneKnowledgeResult = _knowledgeRetriever.Retrieve(new StandaloneKnowledgeRequest
			{
				QueryText = text2,
				ExplicitEntities = list2.Where((string value) => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(24)
					.ToList(),
				PreferredTerms = list3,
				Rules = readOnlyList,
				Speaker = BuildKnowledgeSpeakerContext(snapshot),
				TopK = settings.KnowledgeTopK,
				MaximumCharacters = settings.KnowledgeCharacterBudget,
				PinnedCharacterBudget = settings.PinnedRuleCharacterBudget,
				EnableChaining = settings.EnableKnowledgeChaining,
				ChainingRounds = settings.KnowledgeChainingRounds
			});
			snapshot.KnowledgeContext = standaloneKnowledgeResult.Context ?? "";
			snapshot.KnowledgeHitCount = standaloneKnowledgeResult.Hits.Count;
			snapshot.KnowledgeReason = standaloneKnowledgeResult.DiagnosticCode;
		}
		catch (Exception ex)
		{
			snapshot.KnowledgeReason = "knowledge_runtime_" + ex.GetType().Name;
			snapshot.KnowledgeContext = "";
		}
		snapshot.KnowledgeElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
	}

	private void StartKnowledgeAndGeneration(RequestSnapshot snapshot, long requestEpoch, CancellationToken token)
	{
		PublishProbe(snapshot, "KnowledgeAttemptStarted", "started", snapshot.ActualBackend, "main_thread", snapshot.KnowledgeQueryTerms, snapshot.KnowledgeCandidateCount, 0, cacheHit: false, 0.0, 0.0, null);
		ContinueAfterKnowledge(snapshot, requestEpoch, token);
	}

	private void ContinueAfterKnowledge(RequestSnapshot snapshot, long requestEpoch, CancellationToken token)
	{
		if (!IsCurrentRequest(snapshot, requestEpoch) || token.IsCancellationRequested)
		{
			FinishStaleRequest(snapshot, "knowledge_result_stale");
			return;
		}
		CurrentSettlementContext currentSettlementContext = CurrentSettlementResolver.ResolveContext();
		if (HasLocationContextChanged(snapshot, currentSettlementContext))
		{
			CancelForLocationChange();
			return;
		}
		BattleGateEvaluation evaluation = EvaluateBattleGate(GlobalSettings<ChatterMcmSettings>.Instance, currentSettlementContext);
		if (!evaluation.IsAllowed)
		{
			CancelForBattle(evaluation);
			return;
		}
		snapshot.SystemPrompt = snapshot.SystemPromptBase;
		PublishProbe(snapshot, "KnowledgeAttemptCompleted", (snapshot.KnowledgeHitCount > 0) ? "completed" : "no_match", snapshot.ActualBackend, "main_thread", snapshot.KnowledgeQueryTerms, snapshot.KnowledgeCandidateCount, snapshot.KnowledgeHitCount, cacheHit: false, snapshot.KnowledgeElapsedMilliseconds, snapshot.KnowledgeElapsedMilliseconds, new ChatterProbeContentDraft
		{
			Topic = snapshot.Topic,
			KnowledgeContext = snapshot.KnowledgeContext,
			AdditionalContext = "source=campaign_knowledge_library;retrieval=lexical"
		}, snapshot.KnowledgeReason);
		StartGeneration(snapshot, requestEpoch, token);
	}

	private void StartGeneration(RequestSnapshot snapshot, long requestEpoch, CancellationToken token)
	{
		PublishProbe(snapshot, "GenerationStarted", "started", snapshot.ActualBackend, "worker", snapshot.KnowledgeQueryTerms, snapshot.KnowledgeCandidateCount, snapshot.KnowledgeHitCount, cacheHit: false, 0.0, 0.0, new ChatterProbeContentDraft
		{
			Topic = snapshot.Topic,
			KnowledgeContext = snapshot.KnowledgeContext,
			SystemPrompt = snapshot.SystemPrompt,
			UserPrompt = snapshot.UserPrompt,
			AdditionalContext = "boundary=module_to_chat_provider;source=BannerlordTalk"
		});
		Stopwatch clock = Stopwatch.StartNew();
		_chatClient.GenerateAsync(new ChatGenerationRequest
		{
			SystemPrompt = snapshot.SystemPrompt,
			UserPrompt = snapshot.UserPrompt,
			Options = snapshot.ChatOptions,
			Telemetry = delegate(ChatTransportTelemetry telemetry)
			{
				PublishTransportProbe(snapshot, telemetry);
			}
		}, token).ContinueWith(delegate(Task<ChatGenerationResult> task)
		{
			ChatGenerationResult result = ((task.Status == TaskStatus.RanToCompletion) ? task.Result : ChatGenerationResult.Failure(task.IsCanceled ? "chat_cancelled" : "background_task_failed", 0, 0L));
			MainThreadDispatcher.Enqueue(delegate
			{
				if (IsCurrentRequest(snapshot, requestEpoch) && !_activeRequestTerminalPublished)
				{
					snapshot.GenerationHttpStatusCode = result.HttpStatusCode;
					snapshot.GenerationFinishReason = result.FinishReason;
					snapshot.GenerationPromptTokens = result.PromptTokens;
					snapshot.GenerationCompletionTokens = result.CompletionTokens;
					snapshot.GenerationTotalTokens = result.TotalTokens;
					PublishProbe(snapshot, "GenerationCompleted", result.Succeeded ? "completed" : "failed", snapshot.ActualBackend, "main_thread", snapshot.KnowledgeQueryTerms, snapshot.KnowledgeCandidateCount, snapshot.KnowledgeHitCount, cacheHit: false, clock.Elapsed.TotalMilliseconds, 0.0, new ChatterProbeContentDraft
					{
						RawResponse = result.Text,
						AdditionalContext = "boundary=chat_provider_to_module"
					}, result.DiagnosticCode);
					CompleteRequest(snapshot, result, requestEpoch);
				}
			});
		}, TaskScheduler.Default);
	}

	private void CompleteRequest(RequestSnapshot snapshot, ChatGenerationResult result, long requestEpoch)
	{
		ChatterMcmSettings instance = GlobalSettings<ChatterMcmSettings>.Instance;
		if (requestEpoch != _epoch || snapshot.Campaign != Campaign.Current || !_loaded)
		{
			FinishStaleRequest(snapshot, "generation_result_stale");
			return;
		}
		CurrentSettlementContext currentSettlementContext = CurrentSettlementResolver.ResolveContext();
		if (HasLocationContextChanged(snapshot, currentSettlementContext))
		{
			CancelForLocationChange();
			return;
		}
		BattleGateEvaluation evaluation = EvaluateBattleGate(instance, currentSettlementContext);
		if (!evaluation.IsAllowed)
		{
			CancelForBattle(evaluation);
			return;
		}
		ChatterResponse response = null;
		string error = "";
		if (result == null || !result.Succeeded || !ResponseParser.TryParse(result.Text, snapshot.MaximumCharacters, snapshot.MaximumActionCharacters, snapshot.MaximumInnerVoiceCharacters, out response, out error))
		{
			string text = ((result != null && result.Succeeded) ? error : (result?.DiagnosticCode ?? "missing_result"));
			RequestRetryDecision requestRetryDecision = ScheduleAfterFailure(text);
			PublishProbe(snapshot, "ResponseValidated", "failed", snapshot.ActualBackend, "main_thread", snapshot.KnowledgeQueryTerms, snapshot.KnowledgeCandidateCount, snapshot.KnowledgeHitCount, cacheHit: false, 0.0, 0.0, new ChatterProbeContentDraft
			{
				RawResponse = result?.Text
			}, text);
			PublishProbe(snapshot, "RetryScheduled", "scheduled", snapshot.ActualBackend, "main_thread", snapshot.KnowledgeQueryTerms, snapshot.KnowledgeCandidateCount, snapshot.KnowledgeHitCount, cacheHit: false, 0.0, 0.0, new ChatterProbeContentDraft
			{
				AdditionalContext = "delay_seconds=" + requestRetryDecision.DelaySeconds + ";policy=" + requestRetryDecision.PolicyName + ";transient_failure_count=" + requestRetryDecision.TransientFailureCount
			}, text);
			FinishRequest(snapshot, "failed", text);
			return;
		}
		PublishProbe(snapshot, "ResponseValidated", "completed", snapshot.ActualBackend, "main_thread", snapshot.KnowledgeQueryTerms, snapshot.KnowledgeCandidateCount, snapshot.KnowledgeHitCount, cacheHit: false, 0.0, 0.0, new ChatterProbeContentDraft
		{
			RawResponse = result.Text,
			AdditionalContext = BuildPresentationProbeContext(response)
		});
		if (!IsEligibleParticipant(ResolveHero(snapshot.SpeakerId), instance.AllowAiPlayerSpeech) || _state.ActiveSession?.SessionId != snapshot.SessionId)
		{
			ScheduleNext(success: true, initial: false);
			FinishRequest(snapshot, "stale_discarded", "speaker_or_session_stale");
			return;
		}
		int num = CurrentCampaignDay();
		ChatterLineState chatterLineState = new ChatterLineState
		{
			SessionId = snapshot.SessionId,
			ParticipantIds = _state.ActiveSession.ParticipantIds.ToList(),
			SpeakerId = snapshot.SpeakerId,
			SpeakerName = snapshot.SpeakerName,
			Text = response.Text.Trim(),
			Action = (response.Action ?? ""),
			InnerVoiceText = (response.InnerVoice ?? ""),
			ThoughtText = "",
			PresentationOrder = ChatterSaveState.NormalizePresentationOrder("", _state.NextSequence),
			Mode = snapshot.Mode,
			CampaignDay = num,
			Sequence = _state.NextSequence++
		};
		_state.Lines.Add(chatterLineState);
		long nextSequence = _state.NextMemorySequence;
		IEnumerable<string> source;
		if (snapshot.Mode != 0)
		{
			IEnumerable<string> participantIds = chatterLineState.ParticipantIds;
			source = participantIds;
		}
		else
		{
			IEnumerable<string> participantIds = new string[1] { snapshot.SpeakerId };
			source = participantIds;
		}
		foreach (string item in source.Where((string value) => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal))
		{
			MemoryService.AppendCommittedMemory(_state.MemoryRecords, item, MemoryKind.Dialogue, MemoryLayer.Recent, (snapshot.Mode != 0) ? MemoryVisibility.Participants : MemoryVisibility.Private, snapshot.Topic, chatterLineState.Text, chatterLineState.ParticipantIds, num, 0.45f, 1f, snapshot.PlayerSpeaking ? "player_ai_line" : "campaign_chatter_line", "line:" + chatterLineState.Sequence + ":" + item, snapshot.MemoryPolicy, ref nextSequence);
		}
		_state.NextMemorySequence = nextSequence;
		MemoryService.MarkRecalled(snapshot.RecalledMemories.Concat((snapshot.ProactiveMemory == null) ? Array.Empty<MemoryRecord>() : new MemoryRecord[1] { snapshot.ProactiveMemory }), num);
		CampaignEventMemoryService.MarkInjected(snapshot.RecalledCampaignEvents, num, snapshot.RequestSequence);
		AdvanceSession(snapshot);
		NormalizeState(instance);
		ChatterOverlay.Publish(chatterLineState);
		TryEnqueueTts(chatterLineState, instance);
		ScheduleNext(success: true, initial: false);
		PublishProbe(snapshot, "LineCommitted", "committed", snapshot.ActualBackend, "main_thread", snapshot.KnowledgeQueryTerms, snapshot.KnowledgeCandidateCount, snapshot.KnowledgeHitCount, cacheHit: false, 0.0, 0.0, new ChatterProbeContentDraft
		{
			RawResponse = result.Text,
			PublishedText = BuildPublishedProbeText(chatterLineState),
			AdditionalContext = BuildPresentationProbeContext(response)
		});
		if (TryConsumeThoughtGenerationCadence(snapshot, out var sourceLineCount))
		{
			StartIndependentThought(snapshot, chatterLineState, requestEpoch, sourceLineCount);
		}
		TryStartMemorySummary(snapshot, requestEpoch);
		FinishRequest(snapshot, "committed", "line_committed");
	}

	private static string BuildPresentationProbeContext(ChatterResponse response)
	{
		if (response == null)
		{
			return "presentation_effective=dialogue;presentation_repaired=true;presentation_repair_reason=missing_response";
		}
		return "presentation_requested=" + (response.RequestedPresentationName ?? "missing") + ";presentation_effective=" + (response.PresentationName ?? "dialogue") + ";presentation_repaired=" + (response.PresentationRepaired ? "true" : "false") + ";presentation_repair_reason=" + (response.PresentationRepairReason ?? "none") + ";contract=model_choice_tolerant_receiver";
	}

	private void TryStartMemorySummary(RequestSnapshot snapshot, long requestEpoch)
	{
		ChatterMcmSettings instance = GlobalSettings<ChatterMcmSettings>.Instance;
		MemorySummaryGenerationMode memorySummaryGenerationMode = ResolveSummaryMode(instance);
		if (memorySummaryGenerationMode == MemorySummaryGenerationMode.Off || _summaryClient == null || snapshot == null || string.IsNullOrWhiteSpace(snapshot.SpeakerId) || _summaryOwnersInFlight.Contains(snapshot.SpeakerId))
		{
			return;
		}
		HashSet<string> summarizedSourceIds = new HashSet<string>(_state.MemoryRecords.Where((MemoryRecord record) => record != null && record.OwnerHeroId == snapshot.SpeakerId && record.Kind == MemoryKind.Summary).SelectMany((MemoryRecord record) => record.SourceRecordIds ?? new List<string>()), StringComparer.Ordinal);
		List<MemoryRecord> recent = (from record in _state.MemoryRecords
			where record != null && record.OwnerHeroId == snapshot.SpeakerId && record.Layer == MemoryLayer.Recent && record.Kind != MemoryKind.Thought && record.Source != "memory_summary" && !summarizedSourceIds.Contains(record.RecordId)
			orderby record.Sequence
			select record).Take(6).ToList();
		if (recent.Count < 6)
		{
			return;
		}
		string sourceKey = "summary:" + string.Join(",", recent.Select((MemoryRecord record) => record.RecordId));
		if (_state.MemoryRecords.Any((MemoryRecord record) => record != null && record.ExternalKey == sourceKey))
		{
			return;
		}
		ChatGenerationOptions chatGenerationOptions = ((memorySummaryGenerationMode == MemorySummaryGenerationMode.SameChatModel) ? BuildChatOptions(instance) : BuildSummaryOptions(instance));
		if (string.IsNullOrWhiteSpace(chatGenerationOptions.Endpoint) || string.IsNullOrWhiteSpace(chatGenerationOptions.Model))
		{
			return;
		}
		CancellationTokenSource cancellation = new CancellationTokenSource();
		_summaryCancellations.Add(cancellation);
		_summaryOwnersInFlight.Add(snapshot.SpeakerId);
		MemorySummaryRequest request = new MemorySummaryRequest(snapshot.SpeakerId, recent.Select((MemoryRecord record) => new CommittedMemorySnapshot(record.RecordId, record.Text, record.About, record.Kind.ToString(), record.Importance, record.CreatedDay)), chatGenerationOptions);
		_summaryClient.GenerateAsync(request, cancellation.Token).ContinueWith(delegate(Task<MemorySummaryResult> task)
		{
			MemorySummaryResult result = ((task.Status == TaskStatus.RanToCompletion) ? task.Result : MemorySummaryResult.Failure(task.IsCanceled ? "memory_summary_cancelled" : "memory_summary_background_failed"));
			MainThreadDispatcher.Enqueue(delegate
			{
				_summaryCancellations.Remove(cancellation);
				_summaryOwnersInFlight.Remove(snapshot.SpeakerId);
				cancellation.Dispose();
				if (result.Succeeded && result.Value != null && _loaded && requestEpoch == _epoch && snapshot.Campaign == Campaign.Current && EvaluateBattleGate(GlobalSettings<ChatterMcmSettings>.Instance).IsAllowed)
				{
					long nextSequence = _state.NextMemorySequence;
					MemoryAppendResult memoryAppendResult = MemoryService.AppendCommittedMemory(_state.MemoryRecords, snapshot.SpeakerId, MemoryKind.Summary, MemoryLayer.Situational, MemoryVisibility.Private, result.Value.About, result.Value.Summary, new string[1] { snapshot.SpeakerId }, CurrentCampaignDay(), result.Value.Importance, 0.8f, "memory_summary", sourceKey, snapshot.MemoryPolicy, ref nextSequence);
					if (memoryAppendResult.Record != null)
					{
						memoryAppendResult.Record.SourceRecordIds = (from record in recent
							select record.RecordId into value
							where !string.IsNullOrWhiteSpace(value)
							select value).Distinct(StringComparer.Ordinal).ToList();
					}
					_state.NextMemorySequence = nextSequence;
					NormalizeState(GlobalSettings<ChatterMcmSettings>.Instance);
				}
			});
		}, TaskScheduler.Default);
	}

	private bool TryConsumeThoughtGenerationCadence(RequestSnapshot snapshot, out int sourceLineCount)
	{
		sourceLineCount = 0;
		if (snapshot == null || snapshot.ThoughtMode == ThoughtGenerationMode.Off || string.IsNullOrWhiteSpace(snapshot.SpeakerId))
		{
			return false;
		}
		_state.ThoughtGenerationLineCounts = ChatterSaveState.NormalizeThoughtGenerationLineCounts(_state.ThoughtGenerationLineCounts);
		int num = Math.Max(1, snapshot.ThoughtGenerationLineThreshold);
		_state.ThoughtGenerationLineCounts.TryGetValue(snapshot.SpeakerId, out var value);
		int num2 = ((value >= 1000000) ? 1000000 : (value + 1));
		_state.ThoughtGenerationLineCounts[snapshot.SpeakerId] = num2;
		if (num2 < num)
		{
			return false;
		}
		sourceLineCount = Math.Max(1, Math.Min(20, num2));
		int num3 = num2 % num;
		if (num3 == 0)
		{
			_state.ThoughtGenerationLineCounts.Remove(snapshot.SpeakerId);
		}
		else
		{
			_state.ThoughtGenerationLineCounts[snapshot.SpeakerId] = num3;
		}
		return true;
	}

	private void StartIndependentThought(RequestSnapshot snapshot, ChatterLineState line, long requestEpoch, int sourceLineCount)
	{
		ChatGenerationOptions thoughtOptions = ((snapshot.ThoughtMode == ThoughtGenerationMode.SameChatModel) ? snapshot.ChatOptions : snapshot.ThoughtOptions);
		if (_thoughtClient == null || thoughtOptions == null || string.IsNullOrWhiteSpace(thoughtOptions.Endpoint) || string.IsNullOrWhiteSpace(thoughtOptions.Model))
		{
			PublishThoughtProbe(snapshot, thoughtOptions, "ThoughtGenerationCompleted", "failed", "thought_not_configured", null);
			return;
		}
		CancellationTokenSource cancellation = new CancellationTokenSource();
		_thoughtCancellations.Add(cancellation);
		PublishThoughtProbe(snapshot, thoughtOptions, "ThoughtGenerationStarted", "started", "", null);
		_thoughtClient.GenerateAsync(new ThoughtGenerationRequest
		{
			OwnerHeroId = snapshot.SpeakerId,
			SpeakerName = snapshot.SpeakerName,
			Persona = snapshot.Persona,
			LiveFacts = snapshot.LiveFacts,
			PublicLine = BuildThoughtSourceLines(snapshot, line, sourceLineCount),
			Topic = snapshot.Topic,
			RecalledPrivateMemory = PromptBuilder.FormatMemories(snapshot.RecalledMemories),
			MaximumCharacters = snapshot.MaximumThoughtCharacters,
			Options = thoughtOptions
		}, cancellation.Token).ContinueWith(delegate(Task<ThoughtGenerationResult> task)
		{
			ThoughtGenerationResult thoughtResult = ((task.Status == TaskStatus.RanToCompletion) ? task.Result : ThoughtGenerationResult.Failure(task.IsCanceled ? "thought_cancelled" : "thought_background_failed"));
			MainThreadDispatcher.Enqueue(delegate
			{
				_thoughtCancellations.Remove(cancellation);
				cancellation.Dispose();
				if (!thoughtResult.Succeeded || thoughtResult.Thought == null)
				{
					PublishThoughtProbe(snapshot, thoughtOptions, "ThoughtGenerationCompleted", "failed", thoughtResult.DiagnosticCode, thoughtResult);
				}
				else if (!_loaded || requestEpoch != _epoch || snapshot.Campaign != Campaign.Current)
				{
					PublishThoughtProbe(snapshot, thoughtOptions, "ThoughtGenerationCompleted", "stale_discarded", "thought_epoch_stale", thoughtResult);
				}
				else
				{
					BattleGateEvaluation battleGateEvaluation = EvaluateBattleGate(GlobalSettings<ChatterMcmSettings>.Instance);
					if (!battleGateEvaluation.IsAllowed)
					{
						PublishThoughtProbe(snapshot, thoughtOptions, "ThoughtGenerationCompleted", "stale_discarded", battleGateEvaluation.ReasonCode, thoughtResult);
					}
					else
					{
						long nextSequence = _state.NextMemorySequence;
						MemoryService.AppendThought(_state.MemoryRecords, snapshot.SpeakerId, thoughtResult.Thought, CurrentCampaignDay(), snapshot.MemoryPolicy, ref nextSequence, "independent_thought_model");
						_state.NextMemorySequence = nextSequence;
						NormalizeState(GlobalSettings<ChatterMcmSettings>.Instance);
						PublishThoughtProbe(snapshot, thoughtOptions, "ThoughtGenerationCompleted", "committed", thoughtResult.DiagnosticCode, thoughtResult);
					}
				}
			});
		}, TaskScheduler.Default);
	}

	private string BuildThoughtSourceLines(RequestSnapshot snapshot, ChatterLineState fallbackLine, int sourceLineCount)
	{
		int count = Math.Max(1, Math.Min(20, sourceLineCount));
		List<ChatterLineState> list = (from item in (from item in _state.Lines
				where item != null && string.Equals(item.SpeakerId, snapshot.SpeakerId, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(item.Text)
				orderby item.Sequence descending
				select item).Take(count)
			orderby item.Sequence
			select item).ToList();
		if (list.Count == 0 && fallbackLine != null)
		{
			list.Add(fallbackLine);
		}
		return string.Join("\n", list.Select((ChatterLineState item) => (string.IsNullOrWhiteSpace(item.SpeakerName) ? snapshot.SpeakerName : item.SpeakerName) + "：" + item.Text));
	}

	private ConversationSessionState ResolveSession(ChatterMode? requestedMode, List<Hero> candidates, ChatterMcmSettings settings, bool automatic, string locationContextKey)
	{
		if (!requestedMode.HasValue && IsSessionStillValid(_state.ActiveSession, candidates))
		{
			return _state.ActiveSession;
		}
		ChatterMode chatterMode = requestedMode ?? ChooseMode(candidates.Count, settings);
		if (!IsModeEnabled(chatterMode, settings) || !ConversationParticipantPolicy.TryGetCountRange(chatterMode, candidates.Count, settings.MaxParticipants, out var minimum, out var maximum))
		{
			return null;
		}
		int count = ((minimum == maximum) ? minimum : _random.Next(minimum, maximum + 1));
		List<Hero> source;
		if (automatic)
		{
			List<Hero> heroes = candidates.Where((Hero hero) => CanAutomaticallyInitiate(hero, settings)).ToList();
			Hero starter = WeightedPick(heroes, settings);
			if (starter == null)
			{
				return null;
			}
			source = ((IEnumerable<Hero>)(object)new Hero[1] { starter }).Concat(WeightedShuffle(candidates.Where((Hero hero) => hero != starter), settings)).Take(count).ToList();
		}
		else
		{
			source = WeightedShuffle(candidates, settings).Take(count).ToList();
		}
		_state.ActiveSession = new ConversationSessionState
		{
			SessionId = Guid.NewGuid().ToString("N"),
			Mode = chatterMode,
			ParticipantIds = source.Select((Hero hero) => ((MBObjectBase)hero).StringId).ToList(),
			TurnsRemaining = chatterMode switch
			{
				ChatterMode.Private => settings.SessionTurnsPrivate, 
				ChatterMode.Monologue => 1, 
				_ => settings.SessionTurnsGroup, 
			},
			NextSpeakerIndex = 0,
			InitialSpeakerSelected = false,
			LocationContextKey = locationContextKey
		};
		return _state.ActiveSession;
	}

	private ChatterMode ChooseMode(int candidateCount, ChatterMcmSettings settings)
	{
		List<ChatterMode> list = new List<ChatterMode>();
		if (settings.EnableMonologue && candidateCount >= 1)
		{
			list.Add(ChatterMode.Monologue);
		}
		if (settings.EnablePrivate && candidateCount >= 2)
		{
			list.Add(ChatterMode.Private);
		}
		if (settings.EnableGroup && candidateCount >= 3)
		{
			list.Add(ChatterMode.Group);
		}
		if (list.Count != 0)
		{
			return list[_random.Next(list.Count)];
		}
		return ChatterMode.Monologue;
	}

	private Hero SelectSpeaker(ConversationSessionState session, List<Hero> participants, bool automatic, ChatterMcmSettings settings)
	{
		if (participants.Count == 0)
		{
			return null;
		}
		if (!session.InitialSpeakerSelected)
		{
			IReadOnlyList<Hero> heroes = (automatic ? participants.Where((Hero hero) => CanAutomaticallyInitiate(hero, settings)).ToList() : participants);
			Hero val = WeightedPick(heroes, settings);
			if (val == null)
			{
				return null;
			}
			return val;
		}
		int num = session.NextSpeakerIndex % participants.Count;
		if (num < 0)
		{
			num += participants.Count;
		}
		return participants[num];
	}

	[IteratorStateMachine(typeof(<WeightedShuffle>d__62))]
	private IEnumerable<Hero> WeightedShuffle(IEnumerable<Hero> heroes, ChatterMcmSettings settings)
	{
		//yield-return decompiler failed: Unexpected instruction in Iterator.Dispose()
		return new <WeightedShuffle>d__62(-2)
		{
			<>4__this = this,
			<>3__heroes = heroes,
			<>3__settings = settings
		};
	}

	private Hero WeightedPick(IReadOnlyList<Hero> heroes, ChatterMcmSettings settings)
	{
		if (heroes == null || heroes.Count == 0)
		{
			return null;
		}
		float defaultChattiness = Math.Max(0f, Math.Min(1f, settings?.DefaultPersonaChattiness ?? 0.5f));
		double num = heroes.Sum((Hero hero) => Math.Max(0.01, PersonaService.Get(_state.Personas, ((MBObjectBase)hero).StringId)?.Chattiness ?? defaultChattiness));
		double num2 = _random.NextDouble() * num;
		foreach (Hero hero in heroes)
		{
			num2 -= Math.Max(0.01, PersonaService.Get(_state.Personas, ((MBObjectBase)hero).StringId)?.Chattiness ?? defaultChattiness);
			if (num2 <= 0.0)
			{
				return hero;
			}
		}
		return heroes[heroes.Count - 1];
	}

	private bool CanAutomaticallyInitiate(Hero hero, ChatterMcmSettings settings)
	{
		if (hero == null)
		{
			return false;
		}
		return PersonaService.Get(_state.Personas, ((MBObjectBase)hero).StringId)?.AutoInitiate ?? settings?.DefaultPersonaAutoInitiate ?? true;
	}

	private void AdvanceSession(RequestSnapshot snapshot)
	{
		ConversationSessionState activeSession = _state.ActiveSession;
		if (activeSession != null && snapshot != null && string.Equals(activeSession.SessionId, snapshot.SessionId, StringComparison.Ordinal))
		{
			activeSession.NextSpeakerIndex = Math.Max(0, snapshot.PendingNextSpeakerIndex);
			activeSession.InitialSpeakerSelected = snapshot.PendingInitialSpeakerSelected;
			activeSession.TurnsRemaining--;
			if (activeSession.TurnsRemaining <= 0)
			{
				_state.ActiveSession = null;
			}
		}
	}

	private List<Hero> GetPartyCompanions()
	{
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		MobileParty mainParty = MobileParty.MainParty;
		if (((mainParty != null) ? mainParty.MemberRoster : null) == null)
		{
			return new List<Hero>();
		}
		List<Hero> list = new List<Hero>();
		foreach (TroopRosterElement item in (List<TroopRosterElement>)(object)mainParty.MemberRoster.GetTroopRoster())
		{
			CharacterObject character = item.Character;
			Hero val = ((character != null) ? character.HeroObject : null);
			if (IsEligibleCompanion(val) && !list.Contains(val))
			{
				list.Add(val);
			}
		}
		return list;
	}

	private bool RefreshTtsVoiceSlots(ChatterMcmSettings settings)
	{
		if (settings == null)
		{
			return false;
		}
		string[] array = new string[10] { settings.TtsHero01Id, settings.TtsHero02Id, settings.TtsHero03Id, settings.TtsHero04Id, settings.TtsHero05Id, settings.TtsHero06Id, settings.TtsHero07Id, settings.TtsHero08Id, settings.TtsHero09Id, settings.TtsHero10Id };
		string[] array2 = new string[10] { settings.TtsHero01Name, settings.TtsHero02Name, settings.TtsHero03Name, settings.TtsHero04Name, settings.TtsHero05Name, settings.TtsHero06Name, settings.TtsHero07Name, settings.TtsHero08Name, settings.TtsHero09Name, settings.TtsHero10Name };
		string[] array3 = new string[10] { settings.TtsHero01ReferenceId, settings.TtsHero02ReferenceId, settings.TtsHero03ReferenceId, settings.TtsHero04ReferenceId, settings.TtsHero05ReferenceId, settings.TtsHero06ReferenceId, settings.TtsHero07ReferenceId, settings.TtsHero08ReferenceId, settings.TtsHero09ReferenceId, settings.TtsHero10ReferenceId };
		List<Hero> list = new List<Hero>();
		if (IsEligiblePlayer(Hero.MainHero))
		{
			list.Add(Hero.MainHero);
		}
		list.AddRange(GetPartyCompanions());
		list = (from @group in list.Where((Hero hero) => hero != null && !string.IsNullOrWhiteSpace(((MBObjectBase)hero).StringId)).GroupBy((Hero hero) => ((MBObjectBase)hero).StringId, StringComparer.Ordinal)
			select @group.First()).Take(10).ToList();
		bool flag = false;
		for (int i = 0; i < array.Length; i++)
		{
			string id = (array[i] ?? "").Trim();
			if (id.Length == 0)
			{
				continue;
			}
			Hero val = list.FirstOrDefault((Hero hero) => string.Equals(((MBObjectBase)hero).StringId, id, StringComparison.Ordinal));
			if (val == null)
			{
				if (string.IsNullOrWhiteSpace(array3[i]))
				{
					array[i] = "";
					array2[i] = "";
					flag = true;
				}
			}
			else
			{
				string text = ((object)val.Name)?.ToString() ?? id;
				if (!string.Equals(array2[i] ?? "", text, StringComparison.Ordinal))
				{
					array2[i] = text;
					flag = true;
				}
			}
		}
		HashSet<string> hashSet = new HashSet<string>(array.Where((string value) => !string.IsNullOrWhiteSpace(value)), StringComparer.Ordinal);
		foreach (Hero item in list)
		{
			if (hashSet.Contains(((MBObjectBase)item).StringId))
			{
				continue;
			}
			int num = -1;
			for (int j = 0; j < array.Length; j++)
			{
				if (string.IsNullOrWhiteSpace(array[j]) && string.IsNullOrWhiteSpace(array2[j]) && string.IsNullOrWhiteSpace(array3[j]))
				{
					num = j;
					break;
				}
			}
			if (num < 0)
			{
				break;
			}
			array[num] = ((MBObjectBase)item).StringId;
			array2[num] = ((object)item.Name)?.ToString() ?? ((MBObjectBase)item).StringId;
			hashSet.Add(((MBObjectBase)item).StringId);
			flag = true;
		}
		if (flag)
		{
			WriteTtsVoiceSlots(settings, array, array2, array3);
		}
		return flag;
	}

	private void RefreshTtsSettingsAndRoster(ChatterMcmSettings settings)
	{
		if (!_loaded || settings == null || _wallClock.ElapsedMilliseconds < _nextTtsSettingsRefreshAtMilliseconds)
		{
			return;
		}
		_nextTtsSettingsRefreshAtMilliseconds = _wallClock.ElapsedMilliseconds + 5000;
		bool num = RefreshTtsVoiceSlots(settings);
		string text = ComputeTtsSettingsFingerprint(settings);
		if (!string.Equals(text, _ttsSettingsFingerprint, StringComparison.Ordinal))
		{
			_ttsSettingsFingerprint = text;
			_ttsPlaybackService?.CancelPending();
		}
		if (!num)
		{
			return;
		}
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

	private static string ComputeTtsSettingsFingerprint(ChatterMcmSettings settings)
	{
		if (settings == null)
		{
			return "";
		}
		SHA256 hash = SHA256.Create();
		try
		{
			Action<string> action = delegate(string value)
			{
				byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
				byte[] bytes2 = BitConverter.GetBytes(bytes.Length);
				try
				{
					hash.TransformBlock(bytes2, 0, bytes2.Length, null, 0);
					hash.TransformBlock(bytes, 0, bytes.Length, null, 0);
				}
				finally
				{
					Array.Clear(bytes2, 0, bytes2.Length);
					Array.Clear(bytes, 0, bytes.Length);
				}
			};
			action(settings.EnableTts ? "1" : "0");
			action(settings.TtsEndpoint);
			action(settings.TtsApiKey);
			action(settings.TtsModel);
			action(settings.TtsTemperature.ToString("R", CultureInfo.InvariantCulture));
			action(settings.TtsTopP.ToString("R", CultureInfo.InvariantCulture));
			action(settings.TtsSpeed.ToString("R", CultureInfo.InvariantCulture));
			action(settings.TtsVolume.ToString("R", CultureInfo.InvariantCulture));
			action(settings.TtsThrottleMilliseconds.ToString(CultureInfo.InvariantCulture));
			action(settings.TtsSpeakText ? "1" : "0");
			action(settings.TtsSpeakAction ? "1" : "0");
			action(settings.TtsSpeakInnerVoice ? "1" : "0");
			foreach (VoiceReferenceBinding item in BuildTtsVoiceBindings(settings))
			{
				action(item.HeroId);
				action(item.ReferenceId);
			}
			hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
			return Convert.ToBase64String(hash.Hash);
		}
		finally
		{
			if (hash != null)
			{
				((IDisposable)hash).Dispose();
			}
		}
	}

	private static void WriteTtsVoiceSlots(ChatterMcmSettings settings, string[] ids, string[] names, string[] references)
	{
		settings.TtsHero01Id = ids[0] ?? "";
		settings.TtsHero01Name = names[0] ?? "";
		settings.TtsHero01ReferenceId = references[0] ?? "";
		settings.TtsHero02Id = ids[1] ?? "";
		settings.TtsHero02Name = names[1] ?? "";
		settings.TtsHero02ReferenceId = references[1] ?? "";
		settings.TtsHero03Id = ids[2] ?? "";
		settings.TtsHero03Name = names[2] ?? "";
		settings.TtsHero03ReferenceId = references[2] ?? "";
		settings.TtsHero04Id = ids[3] ?? "";
		settings.TtsHero04Name = names[3] ?? "";
		settings.TtsHero04ReferenceId = references[3] ?? "";
		settings.TtsHero05Id = ids[4] ?? "";
		settings.TtsHero05Name = names[4] ?? "";
		settings.TtsHero05ReferenceId = references[4] ?? "";
		settings.TtsHero06Id = ids[5] ?? "";
		settings.TtsHero06Name = names[5] ?? "";
		settings.TtsHero06ReferenceId = references[5] ?? "";
		settings.TtsHero07Id = ids[6] ?? "";
		settings.TtsHero07Name = names[6] ?? "";
		settings.TtsHero07ReferenceId = references[6] ?? "";
		settings.TtsHero08Id = ids[7] ?? "";
		settings.TtsHero08Name = names[7] ?? "";
		settings.TtsHero08ReferenceId = references[7] ?? "";
		settings.TtsHero09Id = ids[8] ?? "";
		settings.TtsHero09Name = names[8] ?? "";
		settings.TtsHero09ReferenceId = references[8] ?? "";
		settings.TtsHero10Id = ids[9] ?? "";
		settings.TtsHero10Name = names[9] ?? "";
		settings.TtsHero10ReferenceId = references[9] ?? "";
	}

	private void TryEnqueueTts(ChatterLineState line, ChatterMcmSettings settings)
	{
		if (line == null || settings == null || !settings.EnableTts || _ttsPlaybackService == null)
		{
			return;
		}
		string text = TtsTextComposer.Compose(line, settings.TtsSpeakText, settings.TtsSpeakAction, settings.TtsSpeakInnerVoice);
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		if (!VoiceReferenceMap.TryResolveExact(line.SpeakerId, BuildTtsVoiceBindings(settings), out var referenceId))
		{
			LogDiagnostic(settings, "tts_skipped reason=no_exact_voice_mapping");
			return;
		}
		FishTtsOptions options = new FishTtsOptions
		{
			Endpoint = settings.TtsEndpoint,
			Model = settings.TtsModel,
			ApiKey = settings.TtsApiKey,
			Temperature = settings.TtsTemperature,
			TopP = settings.TtsTopP,
			Speed = settings.TtsSpeed,
			Volume = settings.TtsVolume,
			ThrottleMilliseconds = settings.TtsThrottleMilliseconds,
			TimeoutSeconds = 60
		};
		if (!_ttsPlaybackService.Enqueue(new TtsPlaybackJob(line.SpeakerId, text, referenceId, options)))
		{
			LogDiagnostic(settings, "tts_skipped reason=queue_unavailable");
		}
	}

	private static IEnumerable<VoiceReferenceBinding> BuildTtsVoiceBindings(ChatterMcmSettings settings)
	{
		return new VoiceReferenceBinding[10]
		{
			new VoiceReferenceBinding
			{
				HeroId = settings.TtsHero01Id,
				ReferenceId = settings.TtsHero01ReferenceId
			},
			new VoiceReferenceBinding
			{
				HeroId = settings.TtsHero02Id,
				ReferenceId = settings.TtsHero02ReferenceId
			},
			new VoiceReferenceBinding
			{
				HeroId = settings.TtsHero03Id,
				ReferenceId = settings.TtsHero03ReferenceId
			},
			new VoiceReferenceBinding
			{
				HeroId = settings.TtsHero04Id,
				ReferenceId = settings.TtsHero04ReferenceId
			},
			new VoiceReferenceBinding
			{
				HeroId = settings.TtsHero05Id,
				ReferenceId = settings.TtsHero05ReferenceId
			},
			new VoiceReferenceBinding
			{
				HeroId = settings.TtsHero06Id,
				ReferenceId = settings.TtsHero06ReferenceId
			},
			new VoiceReferenceBinding
			{
				HeroId = settings.TtsHero07Id,
				ReferenceId = settings.TtsHero07ReferenceId
			},
			new VoiceReferenceBinding
			{
				HeroId = settings.TtsHero08Id,
				ReferenceId = settings.TtsHero08ReferenceId
			},
			new VoiceReferenceBinding
			{
				HeroId = settings.TtsHero09Id,
				ReferenceId = settings.TtsHero09ReferenceId
			},
			new VoiceReferenceBinding
			{
				HeroId = settings.TtsHero10Id,
				ReferenceId = settings.TtsHero10ReferenceId
			}
		};
	}

	private static bool IsEligibleCompanion(Hero hero)
	{
		if (hero != null && hero != Hero.MainHero && hero.IsAlive && !hero.IsPrisoner && hero.PartyBelongedTo == MobileParty.MainParty)
		{
			if (!hero.IsPlayerCompanion)
			{
				return hero.Clan == Clan.PlayerClan;
			}
			return true;
		}
		return false;
	}

	private static bool IsEligiblePlayer(Hero hero)
	{
		if (hero != null && hero == Hero.MainHero && hero.IsAlive)
		{
			return !hero.IsPrisoner;
		}
		return false;
	}

	private static bool IsEligibleParticipant(Hero hero, bool allowAiPlayerSpeech)
	{
		if (!IsEligibleCompanion(hero))
		{
			if (allowAiPlayerSpeech)
			{
				return IsEligiblePlayer(hero);
			}
			return false;
		}
		return true;
	}

	private static bool IsSessionStillValid(ConversationSessionState session, List<Hero> candidates)
	{
		if (session != null && session.TurnsRemaining > 0 && session.ParticipantIds != null)
		{
			return session.ParticipantIds.All((string id) => candidates.Any((Hero hero) => ((MBObjectBase)hero).StringId == id));
		}
		return false;
	}

	private static bool IsModeEnabled(ChatterMode mode, ChatterMcmSettings settings)
	{
		return mode switch
		{
			ChatterMode.Private => settings.EnablePrivate, 
			ChatterMode.Monologue => settings.EnableMonologue, 
			_ => settings.EnableGroup, 
		};
	}

	private static Hero ResolveHero(string id)
	{
		return ((IEnumerable<Hero>)Hero.AllAliveHeroes).FirstOrDefault((Hero hero) => string.Equals(((MBObjectBase)hero).StringId, id, StringComparison.Ordinal));
	}

	private static bool IsSafeMapScreen()
	{
		if (Campaign.Current != null)
		{
			Game current = Game.Current;
			object obj;
			if (current == null)
			{
				obj = null;
			}
			else
			{
				GameStateManager gameStateManager = current.GameStateManager;
				obj = ((gameStateManager != null) ? gameStateManager.ActiveState : null);
			}
			if (obj is MapState)
			{
				return Mission.Current == null;
			}
		}
		return false;
	}

	private static bool IsPaused()
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Invalid comparison between Unknown and I4
		CampaignTimeControlMode val = (CampaignTimeControlMode)((Campaign.Current != null) ? ((int)Campaign.Current.TimeControlMode) : 0);
		if ((int)val != 0)
		{
			return (int)val == 6;
		}
		return true;
	}

	private void NormalizeState(ChatterMcmSettings settings)
	{
		_state.Lines = _state.Lines ?? new List<ChatterLineState>();
		_state.MemoryRecords = _state.MemoryRecords ?? new List<MemoryRecord>();
		_state.Personas = _state.Personas ?? new List<HeroPersonaState>();
		_state.KnowledgeRules = _state.KnowledgeRules ?? new List<KnowledgeRule>();
		_state.CampaignEventRecords = _state.CampaignEventRecords ?? new List<CampaignEventRecord>();
		_state.ThoughtGenerationLineCounts = ChatterSaveState.NormalizeThoughtGenerationLineCounts(_state.ThoughtGenerationLineCounts);
		CampaignEventMemoryService.Normalize(_state.CampaignEventRecords, CurrentCampaignDay());
		KnowledgeLibraryCodec.NormalizeForPersistence(_state.KnowledgeRules);
		int num = Math.Max(5, settings?.HistoryLineLimit ?? 60);
		if (_state.Lines.Count > num)
		{
			_state.Lines = _state.Lines.OrderBy((ChatterLineState line) => line.Sequence).TakeLastCompat(num).ToList();
		}
		foreach (ChatterLineState line in _state.Lines)
		{
			line.ParticipantIds = line.ParticipantIds ?? new List<string>();
			line.Action = line.Action ?? "";
			line.InnerVoiceText = line.InnerVoiceText ?? "";
			line.ThoughtText = "";
			line.PresentationOrder = ChatterSaveState.NormalizePresentationOrder(line.PresentationOrder, line.Sequence);
		}
		MemoryPolicy policy = BuildMemoryPolicy(settings);
		foreach (string item in (from record in _state.MemoryRecords
			where record != null
			select record.OwnerHeroId).Distinct(StringComparer.Ordinal).ToList())
		{
			MemoryService.ApplyCapacity(_state.MemoryRecords, item, policy);
		}
		long nextSequence = _state.NextMemorySequence;
		MemoryService.NormalizeForPersistence(_state.MemoryRecords, ref nextSequence);
		_state.NextMemorySequence = nextSequence;
	}

	private ChatterSaveState SnapshotForSave()
	{
		lock (_stateSync)
		{
			NormalizeState(GlobalSettings<ChatterMcmSettings>.Instance);
			return _state;
		}
	}

	private void PublishAllLines()
	{
		ChatterOverlay.ReplaceAll(_state.Lines);
	}

	private void ScheduleNext(bool success, bool initial)
	{
		ChatterMcmSettings instance = GlobalSettings<ChatterMcmSettings>.Instance;
		if (!success)
		{
			ScheduleAfterFailure("unspecified_failure");
			return;
		}
		_consecutiveTransientFailures = 0;
		int num = Math.Max(1, instance?.IntervalSeconds ?? 15);
		_nextRequestAtMilliseconds = _wallClock.ElapsedMilliseconds + (long)num * 1000L;
	}

	private RequestRetryDecision ScheduleAfterFailure(string reasonCode)
	{
		ChatterMcmSettings instance = GlobalSettings<ChatterMcmSettings>.Instance;
		RequestRetryDecision requestRetryDecision = RequestRetryPolicy.Decide(reasonCode, _consecutiveTransientFailures, instance?.IntervalSeconds ?? 15, instance?.FailureBackoffSeconds ?? 600);
		_consecutiveTransientFailures = requestRetryDecision.TransientFailureCount;
		_nextRequestAtMilliseconds = _wallClock.ElapsedMilliseconds + (long)requestRetryDecision.DelaySeconds * 1000L;
		return requestRetryDecision;
	}

	private void ScheduleShortRecheck()
	{
		_nextRequestAtMilliseconds = _wallClock.ElapsedMilliseconds + 1000;
	}

	private bool IsCurrentRequest(RequestSnapshot snapshot, long requestEpoch)
	{
		if (snapshot != null && _requestInFlight && requestEpoch == _epoch && snapshot.Campaign == Campaign.Current && _loaded)
		{
			return _activeRequestSequence == snapshot.RequestSequence;
		}
		return false;
	}

	private BattleGateEvaluation EvaluateBattleGate(ChatterMcmSettings settings)
	{
		return EvaluateBattleGate(settings, CurrentSettlementResolver.ResolveContext());
	}

	private BattleGateEvaluation EvaluateBattleGate(ChatterMcmSettings settings, CurrentSettlementContext settlementContext)
	{
		BattleActivityInput input;
		try
		{
			MissionCombatKind missionCombatKind = ReadMissionCombatKind(Mission.Current);
			bool playerMapEventActive = MapEvent.PlayerMapEvent != null;
			MobileParty mainParty = MobileParty.MainParty;
			bool mainPartyMapEventActive = ((mainParty != null) ? mainParty.MapEvent : null) != null;
			bool isActive = PlayerEncounter.IsActive;
			if (!BattleActivityClassifier.HasLiveCombatActivity(new BattleActivityInput(missionCombatKind, playerMapEventActive, mainPartyMapEventActive, isActive, settlementContext.Settlement != null, settlementContext.IsVerifiedSettlementMenu, eventLockActive: false)))
			{
				_battleEventLock = false;
			}
			input = new BattleActivityInput(missionCombatKind, playerMapEventActive, mainPartyMapEventActive, isActive, settlementContext.Settlement != null, settlementContext.IsVerifiedSettlementMenu, _battleEventLock);
		}
		catch
		{
			_battleEventLock = true;
			settlementContext = CurrentSettlementContext.Empty;
			input = new BattleActivityInput(MissionCombatKind.None, playerMapEventActive: false, mainPartyMapEventActive: false, playerEncounterActive: false, settlementActive: false, settlementMenuActive: false, eventLockActive: true);
		}
		BattleActivitySnapshot snapshot = BattleActivityClassifier.Classify(input);
		BattleGateEvaluation battleGateEvaluation = _battleGate.Evaluate(snapshot, _wallClock.ElapsedMilliseconds, settings?.PostBattleQuietSeconds ?? 10);
		LogBattleActivityState(settings, input, settlementContext, snapshot, battleGateEvaluation);
		return battleGateEvaluation;
	}

	private static MissionCombatKind ReadMissionCombatKind(Mission mission)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Invalid comparison between Unknown and I4
		if (mission == null)
		{
			return MissionCombatKind.None;
		}
		try
		{
			MissionCombatType combatType = mission.CombatType;
			if ((int)combatType != 0)
			{
				if ((int)combatType == 1)
				{
					return MissionCombatKind.ArenaCombat;
				}
				return MissionCombatKind.NoCombat;
			}
			return MissionCombatKind.Combat;
		}
		catch
		{
			return MissionCombatKind.None;
		}
	}

	private void LogBattleActivityState(ChatterMcmSettings settings, BattleActivityInput input, CurrentSettlementContext settlementContext, BattleActivitySnapshot snapshot, BattleGateEvaluation evaluation)
	{
		//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02aa: Unknown result type (might be due to invalid IL or missing references)
		if (settings != null && settings.EnableDiagnosticLogging)
		{
			Settlement settlement = settlementContext.Settlement;
			string text = SafeLogToken((settlement != null) ? ((MBObjectBase)settlement).StringId : null);
			Settlement settlement2 = settlementContext.Settlement;
			string text2 = SafeLogToken((settlement2 == null) ? null : ((object)settlement2.Name)?.ToString());
			string text3 = SafeLogToken(settlementContext.MenuId);
			string[] obj = new string[13]
			{
				input.MissionCombatKind.ToString(),
				input.PlayerMapEventActive.ToString(),
				input.MainPartyMapEventActive.ToString(),
				input.PlayerEncounterActive.ToString(),
				input.SettlementActive.ToString(),
				input.SettlementMenuActive.ToString(),
				settlementContext.MenuId,
				null,
				null,
				null,
				null,
				null,
				null
			};
			MenuOverlayType overlay = settlementContext.Overlay;
			obj[7] = ((object)(MenuOverlayType)(ref overlay)).ToString();
			obj[8] = input.EventLockActive.ToString();
			obj[9] = settlementContext.Source.ToString();
			obj[10] = text;
			obj[11] = evaluation.State.ToString();
			obj[12] = evaluation.ReasonCode;
			string text4 = string.Join("|", obj);
			if (!string.Equals(text4, _lastBattleActivityFingerprint, StringComparison.Ordinal))
			{
				_lastBattleActivityFingerprint = text4;
				string[] obj2 = new string[30]
				{
					"battle_gate_state state=",
					evaluation.State.ToString().ToLowerInvariant(),
					" reason=",
					SafeReason(evaluation.ReasonCode),
					" mission=",
					input.MissionCombatKind.ToString().ToLowerInvariant(),
					" player_map_event=",
					input.PlayerMapEventActive.ToString().ToLowerInvariant(),
					" main_party_map_event=",
					input.MainPartyMapEventActive.ToString().ToLowerInvariant(),
					" player_encounter=",
					input.PlayerEncounterActive.ToString().ToLowerInvariant(),
					" settlement=",
					input.SettlementActive.ToString().ToLowerInvariant(),
					" settlement_menu=",
					input.SettlementMenuActive.ToString().ToLowerInvariant(),
					" menu_id=",
					text3,
					" menu_overlay=",
					null,
					null,
					null,
					null,
					null,
					null,
					null,
					null,
					null,
					null,
					null
				};
				overlay = settlementContext.Overlay;
				obj2[19] = ((object)(MenuOverlayType)(ref overlay)).ToString().ToLowerInvariant();
				obj2[20] = " settlement_source=";
				obj2[21] = settlementContext.Source.ToString().ToLowerInvariant();
				obj2[22] = " settlement_id=";
				obj2[23] = text;
				obj2[24] = " settlement_name=";
				obj2[25] = text2;
				obj2[26] = " event_lock=";
				obj2[27] = input.EventLockActive.ToString().ToLowerInvariant();
				obj2[28] = " effective_combat=";
				obj2[29] = snapshot.AnyActive.ToString().ToLowerInvariant();
				Log.Info(string.Concat(obj2));
			}
		}
	}

	private static string SafeLogToken(string value)
	{
		string value2 = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
		if (!string.IsNullOrWhiteSpace(value2))
		{
			return Bound(value2, 128);
		}
		return "none";
	}

	private void PollVerifiedBattleArchives(ChatterMcmSettings settings)
	{
		long elapsedMilliseconds = _wallClock.ElapsedMilliseconds;
		if (elapsedMilliseconds < _nextBattleArchivePollAtMilliseconds)
		{
			return;
		}
		_nextBattleArchivePollAtMilliseconds = elapsedMilliseconds + 2000;
		if (!_battleFeedCursorInitialized)
		{
			return;
		}
		if (!LivingCommandersArchiveBridge.TryReadAfter(_battleArchiveCursor, 8, out var latestSequence, out var cursorExpired, out var records, out var error))
		{
			LogBattleFeedErrorOnce(settings, error);
			return;
		}
		_lastBattleFeedError = "";
		_battleArchiveCursor = latestSequence;
		if (cursorExpired)
		{
			LogDiagnostic(settings, "battle_archive_cursor_expired");
		}
		foreach (VerifiedBattleArchive item in from item in records
			where item?.IsFinalOutcome ?? false
			orderby item.FeedSequence
			select item)
		{
			CommitVerifiedBattleArchive(item, settings);
		}
	}

	private void TryInitializeBattleFeedCursor(ChatterMcmSettings settings)
	{
		if (_battleFeedCursorInitialized)
		{
			return;
		}
		long elapsedMilliseconds = _wallClock.ElapsedMilliseconds;
		if (elapsedMilliseconds >= _nextBattleFeedBindAttemptAtMilliseconds)
		{
			_nextBattleFeedBindAttemptAtMilliseconds = elapsedMilliseconds + 3000;
			if (LivingCommandersArchiveBridge.TryReadAfter(long.MaxValue, 1, out var latestSequence, out var _, out var _, out var error))
			{
				_battleArchiveCursor = latestSequence;
				_battleFeedCursorInitialized = true;
				_nextBattleFeedBindAttemptAtMilliseconds = 0L;
				_lastBattleFeedError = "";
			}
			else
			{
				LogBattleFeedErrorOnce(settings, error);
			}
		}
	}

	private void CommitVerifiedBattleArchive(VerifiedBattleArchive archive, ChatterMcmSettings settings)
	{
		if (archive == null || string.IsNullOrWhiteSpace(archive.ArchiveId))
		{
			return;
		}
		foreach (string item in (from id in archive.HeroResults.Select((VerifiedBattleHeroResult result) => result.HeroId).Concat(archive.Speeches.Select((VerifiedBattleSpeech speech) => speech.SpeakerHeroId))
			where !string.IsNullOrWhiteSpace(id)
			select id).Distinct(StringComparer.Ordinal).Take(24))
		{
			Hero val = ResolveHero(item);
			if (!IsEligibleCompanion(val) && val != Hero.MainHero)
			{
				continue;
			}
			string text = archive.ArchiveId + ":" + item;
			if (_state.ProcessedBattleArchiveKeys.Contains(text, StringComparer.Ordinal))
			{
				continue;
			}
			string text2 = BuildHeroBattleArchiveSummary(archive, item);
			if (text2.Length != 0)
			{
				long nextSequence = _state.NextMemorySequence;
				MemoryAppendResult memoryAppendResult = MemoryService.AppendCommittedMemory(_state.MemoryRecords, item, MemoryKind.Battle, MemoryLayer.EventLog, MemoryVisibility.Private, "已验证战斗结果", text2, new string[1] { item }, CurrentCampaignDay(), 0.8f, 1f, "living_commanders_battle_feed", "battle:" + text, BuildMemoryPolicy(settings), ref nextSequence);
				_state.NextMemorySequence = nextSequence;
				if (memoryAppendResult.Added || memoryAppendResult.Merged || memoryAppendResult.DiagnosticCode == "memory_duplicate")
				{
					RememberProcessedBattleArchiveKey(text);
				}
			}
		}
	}

	private void RememberProcessedBattleArchiveKey(string key)
	{
		_state.ProcessedBattleArchiveKeys.Add(key);
		_state.ProcessedBattleArchiveKeys = _state.ProcessedBattleArchiveKeys.Where((string value) => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).TakeLastCompat(256)
			.ToList();
	}

	private static string BuildHeroBattleArchiveSummary(VerifiedBattleArchive archive, string heroId)
	{
		if (archive == null || string.IsNullOrWhiteSpace(heroId))
		{
			return "";
		}
		List<string> list = new List<string>();
		if (!string.IsNullOrWhiteSpace(archive.ConclusionKind))
		{
			list.Add("战斗结论：" + archive.ConclusionKind + "。");
		}
		if (!string.IsNullOrWhiteSpace(archive.WinningSide))
		{
			list.Add("胜方：" + archive.WinningSide + "。");
		}
		VerifiedBattleHeroResult verifiedBattleHeroResult = archive.HeroResults.FirstOrDefault((VerifiedBattleHeroResult result) => string.Equals(result.HeroId, heroId, StringComparison.Ordinal));
		if (verifiedBattleHeroResult != null && verifiedBattleHeroResult.Kills + verifiedBattleHeroResult.Knockouts + verifiedBattleHeroResult.Killed + verifiedBattleHeroResult.Unconscious > 0)
		{
			list.Add("本人战斗记录：击杀" + verifiedBattleHeroResult.Kills + "、击昏" + verifiedBattleHeroResult.Knockouts + "、被击杀记录" + verifiedBattleHeroResult.Killed + "、被击昏记录" + verifiedBattleHeroResult.Unconscious + "。");
		}
		List<string> list2 = (from speech in archive.Speeches
			where string.Equals(speech.SpeakerHeroId, heroId, StringComparison.Ordinal)
			select speech.Text into text
			where !string.IsNullOrWhiteSpace(text)
			select text).Take(2).ToList();
		if (list2.Count > 0)
		{
			list.Add("本人在战场公开说过：" + string.Join("；", list2));
		}
		return Bound(string.Join(" ", list), 1200);
	}

	private void LogBattleFeedErrorOnce(ChatterMcmSettings settings, string error)
	{
		string text = SafeReason(error);
		if (!string.Equals(_lastBattleFeedError, text, StringComparison.Ordinal))
		{
			_lastBattleFeedError = text;
			LogDiagnostic(settings, "battle_archive_feed_unavailable reason=" + text);
		}
	}

	private void OnMapEventStarted(MapEvent mapEvent, PartyBase attacker, PartyBase defender)
	{
		if ((mapEvent != null && mapEvent.IsPlayerMapEvent) || attacker == PartyBase.MainParty || defender == PartyBase.MainParty)
		{
			_lockedPlayerMapEvent = mapEvent;
			BeginBattleBlock("map_event_started");
		}
	}

	private static void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
	{
		CurrentSettlementResolver.ObserveSettlementEntered(party, settlement);
	}

	private static void OnSettlementLeft(MobileParty party, Settlement settlement)
	{
		CurrentSettlementResolver.ObserveSettlementLeft(party, settlement);
	}

	private void OnBattleStarted(PartyBase attacker, PartyBase defender, object subject, bool showNotification)
	{
		if (attacker == PartyBase.MainParty || defender == PartyBase.MainParty || MapEvent.PlayerMapEvent != null)
		{
			_lockedPlayerMapEvent = MapEvent.PlayerMapEvent;
			BeginBattleBlock("battle_started");
		}
	}

	private void OnMissionStarted(IMission mission)
	{
		MissionCombatKind missionCombatKind = ReadMissionCombatKind((Mission)(((object)((mission is Mission) ? mission : null)) ?? ((object)Mission.Current)));
		if (BattleActivityClassifier.IsCombatMission(missionCombatKind))
		{
			BeginBattleBlock((missionCombatKind == MissionCombatKind.ArenaCombat) ? "arena_mission_started" : "combat_mission_started");
		}
	}

	private void OnMissionEnded(IMission mission)
	{
		if (BattleActivityClassifier.IsCombatMission(ReadMissionCombatKind((Mission)(((object)((mission is Mission) ? mission : null)) ?? ((object)Mission.Current)))))
		{
			_battleEventLock = true;
			_battleGate.Reset();
		}
	}

	private void OnMapEventEnded(MapEvent mapEvent)
	{
		if (mapEvent != null && mapEvent == _lockedPlayerMapEvent)
		{
			_lockedPlayerMapEvent = null;
			_battleEventLock = true;
			_battleGate.Reset();
		}
	}

	private void BeginBattleBlock(string reason)
	{
		_battleEventLock = true;
		_battleGate.Reset();
		CancelThoughtJobs();
		CancelSummaryJobs();
		CancelPersonaGeneration(_personaGenerationRequestId);
		_ttsPlaybackService?.CancelPending();
		CancelForBattle(reason);
	}

	private ManagerPersonaGenerationStartData BeginPersonaGeneration(ManagerPersonaGenerationRequestData request, Action<ManagerPersonaGenerationResultData> completed)
	{
		if (request == null || completed == null || string.IsNullOrWhiteSpace(request.RequestId) || string.IsNullOrWhiteSpace(request.HeroId))
		{
			return new ManagerPersonaGenerationStartData
			{
				Started = false,
				Status = "人格智能生成请求无效。"
			};
		}
		if (!_loaded || Campaign.Current == null || _requestInFlight || _personaGenerationCancellation != null)
		{
			return new ManagerPersonaGenerationStartData
			{
				Started = false,
				Status = "当前有聊天或人格生成请求正在进行，请稍后再试。"
			};
		}
		if (!EvaluateBattleGate(GlobalSettings<ChatterMcmSettings>.Instance).IsAllowed)
		{
			return new ManagerPersonaGenerationStartData
			{
				Started = false,
				Status = "战斗或遭遇期间不会调用模型生成人格。"
			};
		}
		Hero hero = ResolveHero(request.HeroId);
		if (!IsEligibleCompanion(hero) && !IsEligiblePlayer(hero))
		{
			return new ManagerPersonaGenerationStartData
			{
				Started = false,
				Status = "已选角色已不在当前玩家或主队同伴范围内。"
			};
		}
		ChatGenerationOptions chatGenerationOptions = BuildChatOptions(GlobalSettings<ChatterMcmSettings>.Instance);
		if (string.IsNullOrWhiteSpace(chatGenerationOptions.Endpoint) || string.IsNullOrWhiteSpace(chatGenerationOptions.Model))
		{
			return new ManagerPersonaGenerationStartData
			{
				Started = false,
				Status = "请先配置主聊天 API 地址和模型名称。"
			};
		}
		string systemPrompt = "你是《骑马与砍杀2》角色人格卡助手。根据当前存档中的已知信息，生成一份便于角色扮演的自然语言草稿，不得伪造具体历史事件或未发生的行为。人格卡不是数值角色面板：不得添加技能、属性、专长、装备、特质分数或关系数值。只允许返回 persona、speaking_style、long_term_goal、values、taboos 这五个字符串字段，不得增加 skills、traits 等字段。只返回单个 JSON：{\"persona\":\"必需\",\"speaking_style\":\"可选\",\"long_term_goal\":\"可选\",\"values\":\"可选\",\"taboos\":\"可选\"}。";
		string userPrompt = BuildPersonaGenerationPrompt(hero, request);
		CancellationTokenSource cancellationTokenSource = (_personaGenerationCancellation = new CancellationTokenSource());
		_personaGenerationRequestId = request.RequestId;
		_personaGenerationEpoch = _epoch;
		Campaign campaign = Campaign.Current;
		_chatClient.GenerateAsync(new ChatGenerationRequest
		{
			SystemPrompt = systemPrompt,
			UserPrompt = userPrompt,
			Options = chatGenerationOptions
		}, cancellationTokenSource.Token).ContinueWith(delegate(Task<ChatGenerationResult> task)
		{
			ChatGenerationResult generation = ((task.Status == TaskStatus.RanToCompletion) ? task.Result : ChatGenerationResult.Failure(task.IsCanceled ? "persona_cancelled" : "persona_background_failed", 0, 0L));
			MainThreadDispatcher.Enqueue(delegate
			{
				bool num = string.Equals(_personaGenerationRequestId, request.RequestId, StringComparison.Ordinal);
				if (num)
				{
					_personaGenerationCancellation?.Dispose();
					_personaGenerationCancellation = null;
					_personaGenerationRequestId = "";
				}
				if (num && _loaded && _personaGenerationEpoch == _epoch && campaign == Campaign.Current && ChatterManagerPanel.IsOpen && ResolveHero(request.HeroId) != null)
				{
					PersonaGenerationParseResult personaGenerationParseResult = (generation.Succeeded ? PersonaResponseParser.Parse(generation.Text) : PersonaGenerationParseResult.Failure(generation.DiagnosticCode));
					HeroPersonaState heroPersonaState = PersonaService.Get(_state.Personas, request.HeroId);
					completed(new ManagerPersonaGenerationResultData
					{
						RequestId = request.RequestId,
						HeroId = request.HeroId,
						Succeeded = personaGenerationParseResult.Succeeded,
						DiagnosticCode = personaGenerationParseResult.DiagnosticCode,
						Preview = (personaGenerationParseResult.Succeeded ? new ManagerPersonaData
						{
							HeroId = request.HeroId,
							Persona = personaGenerationParseResult.Draft.Persona,
							SpeakingStyle = personaGenerationParseResult.Draft.SpeakingStyle,
							LongTermGoal = personaGenerationParseResult.Draft.LongTermGoal,
							Values = personaGenerationParseResult.Draft.Values,
							Taboos = personaGenerationParseResult.Draft.Taboos,
							Chattiness = (heroPersonaState?.Chattiness ?? GlobalSettings<ChatterMcmSettings>.Instance?.DefaultPersonaChattiness ?? 0.5f),
							AutoInitiate = (heroPersonaState?.AutoInitiate ?? GlobalSettings<ChatterMcmSettings>.Instance?.DefaultPersonaAutoInitiate ?? true)
						} : null)
					});
				}
			});
		}, TaskScheduler.Default);
		return new ManagerPersonaGenerationStartData
		{
			Started = true,
			Status = "正在用主聊天模型生成人格草稿；不会自动保存。"
		};
	}

	private void CancelPersonaGeneration(string requestId)
	{
		if (string.IsNullOrWhiteSpace(requestId) || !string.Equals(requestId, _personaGenerationRequestId, StringComparison.Ordinal))
		{
			return;
		}
		try
		{
			_personaGenerationCancellation?.Cancel();
		}
		catch
		{
		}
	}

	private static string BuildPersonaGenerationPrompt(Hero hero, ManagerPersonaGenerationRequestData request)
	{
		string value2 = ((hero != null && hero.IsFemale) ? "女" : "男");
		StringBuilder stringBuilder = new StringBuilder(3200);
		stringBuilder.AppendLine("[当前存档角色]").AppendLine(NativeHeroContextProvider.BuildStableRole(hero)).Append("性别：")
			.AppendLine(value2);
		string value3 = string.Join("\n", new string[5] { request.ExistingPersona, request.ExistingSpeakingStyle, request.ExistingLongTermGoal, request.ExistingValues, request.ExistingTaboos }.Where((string value) => !string.IsNullOrWhiteSpace(value)));
		if (!string.IsNullOrWhiteSpace(value3))
		{
			stringBuilder.AppendLine("[玩家现有草稿；只作方向，可优化但不要自行改掉核心设定]").AppendLine(Bound(value3, 5000));
		}
		stringBuilder.AppendLine("风格应具体、可扮演、不机械；不要添加技能清单、数值面板、道德说教、安全声明或过度限制。");
		return Bound(stringBuilder.ToString(), 9000);
	}

	private void CancelForBattle(BattleGateEvaluation evaluation)
	{
		CaptureBattleContext(_activeSnapshot, evaluation);
		CancelForBattleCore(evaluation.ReasonCode);
	}

	private void CancelForBattle(string reason)
	{
		CaptureBattleContext(_activeSnapshot, BattleGateState.Blocked.ToString(), reason, effectiveCombat: true);
		CancelForBattleCore(reason);
	}

	private void CancelForBattleCore(string reason)
	{
		if (_requestInFlight)
		{
			_requestCancellation?.Cancel();
			ScheduleShortRecheck();
			FinishRequest(_activeSnapshot, "cancelled", string.IsNullOrWhiteSpace(reason) ? "battle_blocked" : reason);
		}
	}

	private static string SettlementId(CurrentSettlementContext context)
	{
		Settlement settlement = context.Settlement;
		return ((settlement != null) ? ((MBObjectBase)settlement).StringId : null) ?? string.Empty;
	}

	private void ApplyLocationContextBoundary(string locationContextKey)
	{
		ConversationSessionState activeSession = _state.ActiveSession;
		if (activeSession != null && ConversationContextPolicy.HasLocationBoundary(activeSession.LocationContextKey, locationContextKey))
		{
			LogDiagnostic(GlobalSettings<ChatterMcmSettings>.Instance, "conversation_location_boundary old=" + SafeLogToken(activeSession.LocationContextKey) + " new=" + SafeLogToken(locationContextKey));
			_state.ActiveSession = null;
		}
	}

	private static void CaptureLocationContext(RequestSnapshot snapshot, CurrentSettlementContext context)
	{
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		if (snapshot != null)
		{
			snapshot.SettlementId = SettlementId(context);
			snapshot.SettlementSource = context.Source.ToString();
			snapshot.SettlementMenuId = context.MenuId ?? string.Empty;
			MenuOverlayType overlay = context.Overlay;
			snapshot.SettlementMenuOverlay = ((object)(MenuOverlayType)(ref overlay)).ToString();
			snapshot.VerifiedSettlementMenu = context.IsVerifiedSettlementMenu;
			snapshot.SettlementCacheFallback = context.IsCacheFallback;
		}
	}

	private static void CaptureBattleContext(RequestSnapshot snapshot, BattleGateEvaluation evaluation)
	{
		CaptureBattleContext(snapshot, evaluation.State.ToString(), evaluation.ReasonCode, evaluation.State == BattleGateState.Blocked);
	}

	private static void CaptureBattleContext(RequestSnapshot snapshot, string state, string reason, bool effectiveCombat)
	{
		if (snapshot != null)
		{
			snapshot.BattleGateState = state ?? string.Empty;
			snapshot.BattleGateReasonCode = reason ?? string.Empty;
			snapshot.EffectiveCombat = effectiveCombat;
		}
	}

	private static string BuildLocationProbeContext(RequestSnapshot snapshot)
	{
		if (snapshot == null)
		{
			return "location_context=missing";
		}
		return "settlement_id=" + SafeLogToken(snapshot.SettlementId) + ";settlement_source=" + SafeLogToken(snapshot.SettlementSource) + ";menu_id=" + SafeLogToken(snapshot.SettlementMenuId) + ";menu_overlay=" + SafeLogToken(snapshot.SettlementMenuOverlay) + ";verified_settlement_menu=" + snapshot.VerifiedSettlementMenu.ToString().ToLowerInvariant() + ";cache_fallback=" + snapshot.SettlementCacheFallback.ToString().ToLowerInvariant();
	}

	private static bool HasLocationContextChanged(RequestSnapshot snapshot, CurrentSettlementContext currentContext)
	{
		if (snapshot != null)
		{
			return !string.Equals(snapshot.SettlementId ?? string.Empty, SettlementId(currentContext), StringComparison.Ordinal);
		}
		return false;
	}

	private void CancelForLocationChange()
	{
		if (_requestInFlight)
		{
			_requestCancellation?.Cancel();
			if (string.Equals(_state.ActiveSession?.SessionId, _activeSnapshot?.SessionId, StringComparison.Ordinal))
			{
				_state.ActiveSession = null;
			}
			ScheduleShortRecheck();
			FinishRequest(_activeSnapshot, "cancelled", "location_context_changed");
		}
	}

	private void CancelThoughtJobs()
	{
		foreach (CancellationTokenSource item in _thoughtCancellations.ToList())
		{
			try
			{
				item.Cancel();
			}
			catch
			{
			}
		}
	}

	private void CancelSummaryJobs()
	{
		foreach (CancellationTokenSource item in _summaryCancellations.ToList())
		{
			try
			{
				item.Cancel();
			}
			catch
			{
			}
		}
		_summaryOwnersInFlight.Clear();
	}

	private static CampaignEventScopeMode ResolveCampaignEventScope(ChatterMcmSettings settings)
	{
		int valueOrDefault = (settings?.CampaignEventMemoryScope?.SelectedIndex).GetValueOrDefault(1);
		if (valueOrDefault > 0)
		{
			if (valueOrDefault < 2)
			{
				return CampaignEventScopeMode.PersonalAndFaction;
			}
			return CampaignEventScopeMode.WorldNews;
		}
		return CampaignEventScopeMode.Off;
	}

	private CampaignEventRelationContext BuildCampaignEventRelationContext(Hero speaker = null, IEnumerable<Hero> participants = null, string topic = "")
	{
		List<Hero> list = new List<Hero>();
		if (Hero.MainHero != null)
		{
			list.Add(Hero.MainHero);
		}
		list.AddRange(GetPartyCompanions());
		if (speaker != null)
		{
			list.Add(speaker);
		}
		list.AddRange(participants ?? Array.Empty<Hero>());
		Clan playerClan = Clan.PlayerClan;
		if (((playerClan != null) ? playerClan.Heroes : null) != null)
		{
			list.AddRange((IEnumerable<Hero>)Clan.PlayerClan.Heroes);
		}
		list = list.Where((Hero hero) => hero != null).Distinct().ToList();
		Settlement val = CurrentSettlementResolver.Resolve();
		CampaignEventRelationContext campaignEventRelationContext = new CampaignEventRelationContext();
		campaignEventRelationContext.HeroIds = (from hero in list
			select ((MBObjectBase)hero).StringId into value
			where !string.IsNullOrWhiteSpace(value)
			select value).Distinct(StringComparer.Ordinal).ToList();
		campaignEventRelationContext.ClanIds = (from value in list.Select(delegate(Hero hero)
			{
				Clan clan2 = hero.Clan;
				return (clan2 == null) ? null : ((MBObjectBase)clan2).StringId;
			})
			where !string.IsNullOrWhiteSpace(value)
			select value).Distinct(StringComparer.Ordinal).ToList();
		campaignEventRelationContext.KingdomIds = (from value in list.Select(delegate(Hero hero)
			{
				Clan clan = hero.Clan;
				if (clan == null)
				{
					return (string)null;
				}
				Kingdom kingdom = clan.Kingdom;
				return (kingdom == null) ? null : ((MBObjectBase)kingdom).StringId;
			})
			where !string.IsNullOrWhiteSpace(value)
			select value).Distinct(StringComparer.Ordinal).ToList();
		campaignEventRelationContext.SettlementIds = new string[1] { (val != null) ? ((MBObjectBase)val).StringId : null }.Where((string value) => !string.IsNullOrWhiteSpace(value)).ToList();
		campaignEventRelationContext.TopicText = topic ?? "";
		return campaignEventRelationContext;
	}

	private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturerHero, ChangeOwnerOfSettlementDetail detail)
	{
		if (!_loaded || settlement == null || (!settlement.IsTown && !settlement.IsCastle))
		{
			return;
		}
		ChatterMcmSettings instance = GlobalSettings<ChatterMcmSettings>.Instance;
		List<CampaignEventRecord> campaignEventRecords = _state.CampaignEventRecords;
		CampaignSettlementOwnershipEvent obj = new CampaignSettlementOwnershipEvent
		{
			SettlementId = ((MBObjectBase)settlement).StringId,
			SettlementName = (((object)settlement.Name)?.ToString() ?? ""),
			SettlementKind = (settlement.IsTown ? CampaignSettlementKind.Town : CampaignSettlementKind.Castle),
			ChangeKind = CampaignEventMemoryService.ParseSettlementChangeKind(((object)(ChangeOwnerOfSettlementDetail)(ref detail)).ToString()),
			OldOwnerHeroId = (((oldOwner != null) ? ((MBObjectBase)oldOwner).StringId : null) ?? ""),
			OldOwnerName = (((oldOwner == null) ? null : ((object)oldOwner.Name)?.ToString()) ?? ""),
			NewOwnerHeroId = (((newOwner != null) ? ((MBObjectBase)newOwner).StringId : null) ?? ""),
			NewOwnerName = (((newOwner == null) ? null : ((object)newOwner.Name)?.ToString()) ?? ""),
			CapturerHeroId = (((capturerHero != null) ? ((MBObjectBase)capturerHero).StringId : null) ?? ""),
			CapturerName = (((capturerHero == null) ? null : ((object)capturerHero.Name)?.ToString()) ?? "")
		};
		object obj2;
		if (oldOwner == null)
		{
			obj2 = null;
		}
		else
		{
			Clan clan = oldOwner.Clan;
			obj2 = ((clan != null) ? ((MBObjectBase)clan).StringId : null);
		}
		if (obj2 == null)
		{
			obj2 = "";
		}
		obj.OldClanId = (string)obj2;
		object obj3;
		if (oldOwner == null)
		{
			obj3 = null;
		}
		else
		{
			Clan clan2 = oldOwner.Clan;
			obj3 = ((clan2 == null) ? null : ((object)clan2.Name)?.ToString());
		}
		if (obj3 == null)
		{
			obj3 = "";
		}
		obj.OldClanName = (string)obj3;
		object obj4;
		if (newOwner == null)
		{
			obj4 = null;
		}
		else
		{
			Clan clan3 = newOwner.Clan;
			obj4 = ((clan3 != null) ? ((MBObjectBase)clan3).StringId : null);
		}
		if (obj4 == null)
		{
			obj4 = "";
		}
		obj.NewClanId = (string)obj4;
		object obj5;
		if (newOwner == null)
		{
			obj5 = null;
		}
		else
		{
			Clan clan4 = newOwner.Clan;
			obj5 = ((clan4 == null) ? null : ((object)clan4.Name)?.ToString());
		}
		if (obj5 == null)
		{
			obj5 = "";
		}
		obj.NewClanName = (string)obj5;
		object obj6;
		if (oldOwner == null)
		{
			obj6 = null;
		}
		else
		{
			Clan clan5 = oldOwner.Clan;
			if (clan5 == null)
			{
				obj6 = null;
			}
			else
			{
				Kingdom kingdom = clan5.Kingdom;
				obj6 = ((kingdom != null) ? ((MBObjectBase)kingdom).StringId : null);
			}
		}
		if (obj6 == null)
		{
			obj6 = "";
		}
		obj.OldKingdomId = (string)obj6;
		object obj7;
		if (oldOwner == null)
		{
			obj7 = null;
		}
		else
		{
			Clan clan6 = oldOwner.Clan;
			if (clan6 == null)
			{
				obj7 = null;
			}
			else
			{
				Kingdom kingdom2 = clan6.Kingdom;
				obj7 = ((kingdom2 == null) ? null : ((object)kingdom2.Name)?.ToString());
			}
		}
		if (obj7 == null)
		{
			obj7 = "";
		}
		obj.OldKingdomName = (string)obj7;
		object obj8;
		if (newOwner == null)
		{
			obj8 = null;
		}
		else
		{
			Clan clan7 = newOwner.Clan;
			if (clan7 == null)
			{
				obj8 = null;
			}
			else
			{
				Kingdom kingdom3 = clan7.Kingdom;
				obj8 = ((kingdom3 != null) ? ((MBObjectBase)kingdom3).StringId : null);
			}
		}
		if (obj8 == null)
		{
			obj8 = "";
		}
		obj.NewKingdomId = (string)obj8;
		object obj9;
		if (newOwner == null)
		{
			obj9 = null;
		}
		else
		{
			Clan clan8 = newOwner.Clan;
			if (clan8 == null)
			{
				obj9 = null;
			}
			else
			{
				Kingdom kingdom4 = clan8.Kingdom;
				obj9 = ((kingdom4 == null) ? null : ((object)kingdom4.Name)?.ToString());
			}
		}
		if (obj9 == null)
		{
			obj9 = "";
		}
		obj.NewKingdomName = (string)obj9;
		obj.CampaignDay = CurrentCampaignDay();
		CampaignEventMemoryService.AppendSettlementOwnership(campaignEventRecords, obj, BuildCampaignEventRelationContext(), ResolveCampaignEventScope(instance));
	}

	private void OnHeroPrisonerTaken(PartyBase capturer, Hero prisoner)
	{
		if (!_loaded || prisoner == null)
		{
			return;
		}
		Hero val = ((capturer != null) ? capturer.LeaderHero : null);
		List<CampaignEventRecord> campaignEventRecords = _state.CampaignEventRecords;
		CampaignHeroCapturedEvent obj = new CampaignHeroCapturedEvent
		{
			HeroId = ((MBObjectBase)prisoner).StringId,
			HeroName = (((object)prisoner.Name)?.ToString() ?? "")
		};
		Clan clan = prisoner.Clan;
		obj.ClanId = ((clan != null) ? ((MBObjectBase)clan).StringId : null) ?? "";
		Clan clan2 = prisoner.Clan;
		object obj2;
		if (clan2 == null)
		{
			obj2 = null;
		}
		else
		{
			Kingdom kingdom = clan2.Kingdom;
			obj2 = ((kingdom != null) ? ((MBObjectBase)kingdom).StringId : null);
		}
		if (obj2 == null)
		{
			obj2 = "";
		}
		obj.KingdomId = (string)obj2;
		object obj3;
		if (capturer == null)
		{
			obj3 = null;
		}
		else
		{
			MobileParty mobileParty = capturer.MobileParty;
			obj3 = ((mobileParty != null) ? ((MBObjectBase)mobileParty).StringId : null);
		}
		if (obj3 == null)
		{
			obj3 = "";
		}
		obj.CaptorPartyId = (string)obj3;
		obj.CaptorName = ((capturer == null) ? null : ((object)capturer.Name)?.ToString()) ?? "";
		object obj4;
		if (val == null)
		{
			obj4 = null;
		}
		else
		{
			Clan clan3 = val.Clan;
			obj4 = ((clan3 != null) ? ((MBObjectBase)clan3).StringId : null);
		}
		if (obj4 == null)
		{
			obj4 = "";
		}
		obj.CaptorClanId = (string)obj4;
		object obj5;
		if (val == null)
		{
			obj5 = null;
		}
		else
		{
			Clan clan4 = val.Clan;
			if (clan4 == null)
			{
				obj5 = null;
			}
			else
			{
				Kingdom kingdom2 = clan4.Kingdom;
				obj5 = ((kingdom2 != null) ? ((MBObjectBase)kingdom2).StringId : null);
			}
		}
		if (obj5 == null)
		{
			obj5 = "";
		}
		obj.CaptorKingdomId = (string)obj5;
		obj.IsNotable = IsNotableCampaignHero(prisoner);
		obj.CampaignDay = CurrentCampaignDay();
		CampaignEventMemoryService.AppendHeroCaptured(campaignEventRecords, obj, BuildCampaignEventRelationContext(), ResolveCampaignEventScope(GlobalSettings<ChatterMcmSettings>.Instance));
	}

	private void OnHeroPrisonerReleased(Hero prisoner, PartyBase party, IFaction capturerFaction, EndCaptivityDetail detail, bool showNotification)
	{
		if (_loaded && prisoner != null)
		{
			List<CampaignEventRecord> campaignEventRecords = _state.CampaignEventRecords;
			CampaignHeroReleasedEvent obj = new CampaignHeroReleasedEvent
			{
				HeroId = ((MBObjectBase)prisoner).StringId,
				HeroName = (((object)prisoner.Name)?.ToString() ?? "")
			};
			Clan clan = prisoner.Clan;
			obj.ClanId = ((clan != null) ? ((MBObjectBase)clan).StringId : null) ?? "";
			Clan clan2 = prisoner.Clan;
			object obj2;
			if (clan2 == null)
			{
				obj2 = null;
			}
			else
			{
				Kingdom kingdom = clan2.Kingdom;
				obj2 = ((kingdom != null) ? ((MBObjectBase)kingdom).StringId : null);
			}
			if (obj2 == null)
			{
				obj2 = "";
			}
			obj.KingdomId = (string)obj2;
			obj.ReleaseDetail = ((object)(EndCaptivityDetail)(ref detail)).ToString();
			obj.IsNotable = IsNotableCampaignHero(prisoner);
			obj.CampaignDay = CurrentCampaignDay();
			CampaignEventMemoryService.AppendHeroReleased(campaignEventRecords, obj, BuildCampaignEventRelationContext(), ResolveCampaignEventScope(GlobalSettings<ChatterMcmSettings>.Instance));
		}
	}

	private void OnHeroKilled(Hero victim, Hero killer, KillCharacterActionDetail detail, bool showNotification)
	{
		if (_loaded && victim != null)
		{
			List<CampaignEventRecord> campaignEventRecords = _state.CampaignEventRecords;
			CampaignHeroDeathEvent obj = new CampaignHeroDeathEvent
			{
				HeroId = ((MBObjectBase)victim).StringId,
				HeroName = (((object)victim.Name)?.ToString() ?? "")
			};
			Clan clan = victim.Clan;
			obj.ClanId = ((clan != null) ? ((MBObjectBase)clan).StringId : null) ?? "";
			Clan clan2 = victim.Clan;
			object obj2;
			if (clan2 == null)
			{
				obj2 = null;
			}
			else
			{
				Kingdom kingdom = clan2.Kingdom;
				obj2 = ((kingdom != null) ? ((MBObjectBase)kingdom).StringId : null);
			}
			if (obj2 == null)
			{
				obj2 = "";
			}
			obj.KingdomId = (string)obj2;
			obj.KillerHeroId = ((killer != null) ? ((MBObjectBase)killer).StringId : null) ?? "";
			obj.KillerName = ((killer == null) ? null : ((object)killer.Name)?.ToString()) ?? "";
			obj.CauseDetail = ((object)(KillCharacterActionDetail)(ref detail)).ToString();
			obj.IsNotable = IsNotableCampaignHero(victim);
			obj.CampaignDay = CurrentCampaignDay();
			CampaignEventMemoryService.AppendHeroDeath(campaignEventRecords, obj, BuildCampaignEventRelationContext(), ResolveCampaignEventScope(GlobalSettings<ChatterMcmSettings>.Instance));
		}
	}

	private static bool IsNotableCampaignHero(Hero hero)
	{
		if (hero != null)
		{
			Clan clan = hero.Clan;
			if (((clan != null) ? clan.Leader : null) != hero)
			{
				Clan clan2 = hero.Clan;
				object obj;
				if (clan2 == null)
				{
					obj = null;
				}
				else
				{
					Kingdom kingdom = clan2.Kingdom;
					obj = ((kingdom != null) ? kingdom.Leader : null);
				}
				return obj == hero;
			}
			return true;
		}
		return false;
	}

	private static ChatGenerationOptions BuildChatOptions(ChatterMcmSettings settings)
	{
		return new ChatGenerationOptions
		{
			Endpoint = (settings?.ChatEndpoint ?? ""),
			Model = (settings?.ChatModel ?? ""),
			ApiKey = (settings?.ChatApiKey ?? ""),
			TimeoutSeconds = (settings?.ChatTimeoutSeconds ?? 120),
			MaxTokens = (settings?.ChatMaxTokens ?? 512),
			Temperature = (settings?.ChatTemperature ?? 0.85f),
			EnableThinking = (settings?.ChatEnableThinking ?? false),
			ReasoningEffort = ResolveGemini37ReasoningEffort(settings)
		};
	}

	private static ChatReasoningEffort ResolveGemini37ReasoningEffort(ChatterMcmSettings settings)
	{
		return (settings?.Gemini37ThinkingLevel?.SelectedIndex).GetValueOrDefault() switch
		{
			1 => ChatReasoningEffort.Medium, 
			2 => ChatReasoningEffort.High, 
			_ => ChatReasoningEffort.Low, 
		};
	}

	private static ChatGenerationOptions BuildThoughtOptions(ChatterMcmSettings settings)
	{
		return new ChatGenerationOptions
		{
			Endpoint = (settings?.ThoughtEndpoint ?? ""),
			Model = (settings?.ThoughtModel ?? ""),
			ApiKey = (settings?.ThoughtApiKey ?? ""),
			TimeoutSeconds = (settings?.ThoughtTimeoutSeconds ?? 60),
			MaxTokens = (settings?.ThoughtMaxTokens ?? 256),
			Temperature = (settings?.ThoughtTemperature ?? 0.45f)
		};
	}

	private static ChatGenerationOptions BuildSummaryOptions(ChatterMcmSettings settings)
	{
		return new ChatGenerationOptions
		{
			Endpoint = (settings?.SummaryEndpoint ?? ""),
			Model = (settings?.SummaryModel ?? ""),
			ApiKey = (settings?.SummaryApiKey ?? ""),
			TimeoutSeconds = (settings?.SummaryTimeoutSeconds ?? 120),
			MaxTokens = (settings?.SummaryMaxTokens ?? 1200),
			Temperature = (settings?.SummaryTemperature ?? 0.2f)
		};
	}

	private static MemorySummaryGenerationMode ResolveSummaryMode(ChatterMcmSettings settings)
	{
		return (settings?.SummaryGenerationMode?.SelectedIndex).GetValueOrDefault() switch
		{
			1 => MemorySummaryGenerationMode.SameChatModel, 
			2 => MemorySummaryGenerationMode.IndependentModel, 
			_ => MemorySummaryGenerationMode.Off, 
		};
	}

	private static MemoryPolicy BuildMemoryPolicy(ChatterMcmSettings settings)
	{
		int valueOrDefault = (settings?.ThoughtInjectionStrength?.SelectedIndex).GetValueOrDefault(1);
		return new MemoryPolicy
		{
			RecentCapacity = (settings?.MemoryRecentCapacity ?? 6),
			SituationalCapacity = (settings?.MemorySituationalCapacity ?? 20),
			EventLogCapacity = (settings?.MemoryEventCapacity ?? 50),
			ArchiveCapacity = (settings?.MemoryArchiveCapacity ?? 50),
			RecentHalfLifeDays = (settings?.MemoryRecentHalfLifeDays ?? 3),
			SituationalHalfLifeDays = (settings?.MemorySituationalHalfLifeDays ?? 10),
			EventHalfLifeDays = (settings?.MemoryEventHalfLifeDays ?? 45),
			ArchiveHalfLifeDays = (settings?.MemoryArchiveHalfLifeDays ?? 180),
			MinimumRecallScore = (settings?.MemoryMinimumRecallScore ?? 0.12f),
			MidHalfLifeDays = (settings?.ThoughtMidHalfLifeDays ?? 4),
			LongHalfLifeDays = (settings?.ThoughtLongHalfLifeDays ?? 16),
			BeliefHalfLifeDays = (settings?.ThoughtBeliefHalfLifeDays ?? 60),
			MidToLongOccurrences = (settings?.ThoughtMidToLongOccurrences ?? 3),
			MidToLongWindowDays = (settings?.ThoughtMidToLongWindowDays ?? 5),
			LongToBeliefOccurrences = (settings?.ThoughtLongToBeliefOccurrences ?? 3),
			LongToBeliefWindowDays = (settings?.ThoughtLongToBeliefWindowDays ?? 15),
			ThoughtMidRecallBudget = ((valueOrDefault <= 0) ? 2 : ((valueOrDefault >= 2) ? 7 : 4)),
			ThoughtLongRecallBudget = ((valueOrDefault <= 0) ? 2 : ((valueOrDefault >= 2) ? 5 : 3)),
			ThoughtBeliefRecallBudget = ((valueOrDefault <= 0) ? 1 : ((valueOrDefault >= 2) ? 3 : 2))
		};
	}

	private static string BuildPersonaText(HeroPersonaState persona)
	{
		if (persona == null)
		{
			return "";
		}
		return Bound(string.Join("\n", new string[5]
		{
			persona.Persona,
			string.IsNullOrWhiteSpace(persona.SpeakingStyle) ? "" : ("说话风格：" + persona.SpeakingStyle),
			string.IsNullOrWhiteSpace(persona.LongTermGoal) ? "" : ("长期目标：" + persona.LongTermGoal),
			string.IsNullOrWhiteSpace(persona.Values) ? "" : ("价值观：" + persona.Values),
			string.IsNullOrWhiteSpace(persona.Taboos) ? "" : ("禁忌：" + persona.Taboos)
		}.Where((string value) => !string.IsNullOrWhiteSpace(value))), 8000);
	}

	private static KnowledgeSpeakerContext BuildKnowledgeSpeakerContext(RequestSnapshot snapshot)
	{
		//IL_01c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c7: Unknown result type (might be due to invalid IL or missing references)
		Hero speaker = snapshot.Speaker;
		Settlement val = CurrentSettlementResolver.Resolve();
		List<string> list = new List<string>();
		if (speaker != null)
		{
			list.Add(((MBObjectBase)speaker).StringId);
			if (speaker.CharacterObject != null)
			{
				list.Add(((MBObjectBase)speaker.CharacterObject).StringId);
			}
		}
		Dictionary<string, string> mappingValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["speaker"] = snapshot.SpeakerName ?? "",
			["location"] = ((val == null) ? null : ((object)val.Name)?.ToString()) ?? ""
		};
		Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		if (speaker != null)
		{
			if (DefaultSkills.Scouting != null)
			{
				dictionary["scouting"] = speaker.GetSkillValue(DefaultSkills.Scouting);
			}
			if (DefaultSkills.Steward != null)
			{
				dictionary["steward"] = speaker.GetSkillValue(DefaultSkills.Steward);
			}
		}
		KnowledgeSpeakerContext obj = new KnowledgeSpeakerContext
		{
			HeroId = (((speaker != null) ? ((MBObjectBase)speaker).StringId : null) ?? "")
		};
		object obj2;
		if (speaker == null)
		{
			obj2 = null;
		}
		else
		{
			CharacterObject characterObject = speaker.CharacterObject;
			obj2 = ((characterObject != null) ? ((MBObjectBase)characterObject).StringId : null);
		}
		if (obj2 == null)
		{
			obj2 = "";
		}
		obj.CharacterId = (string)obj2;
		object obj3;
		if (speaker == null)
		{
			obj3 = null;
		}
		else
		{
			CultureObject culture = speaker.Culture;
			obj3 = ((culture != null) ? ((MBObjectBase)culture).StringId : null);
		}
		if (obj3 == null)
		{
			obj3 = "";
		}
		obj.CultureId = (string)obj3;
		object obj4;
		if (speaker == null)
		{
			obj4 = null;
		}
		else
		{
			Clan clan = speaker.Clan;
			if (clan == null)
			{
				obj4 = null;
			}
			else
			{
				Kingdom kingdom = clan.Kingdom;
				obj4 = ((kingdom != null) ? ((MBObjectBase)kingdom).StringId : null);
			}
		}
		if (obj4 == null)
		{
			obj4 = "";
		}
		obj.KingdomId = (string)obj4;
		object obj5;
		if (speaker == null)
		{
			obj5 = null;
		}
		else
		{
			Clan clan2 = speaker.Clan;
			obj5 = ((clan2 != null) ? ((MBObjectBase)clan2).StringId : null);
		}
		if (obj5 == null)
		{
			obj5 = "";
		}
		obj.ClanId = (string)obj5;
		obj.SettlementId = ((val != null) ? ((MBObjectBase)val).StringId : null) ?? "";
		object obj6;
		if (speaker == null)
		{
			obj6 = null;
		}
		else
		{
			Occupation occupation = speaker.Occupation;
			obj6 = ((object)(Occupation)(ref occupation)).ToString();
		}
		if (obj6 == null)
		{
			obj6 = "";
		}
		obj.Role = (string)obj6;
		obj.SessionId = snapshot.SessionId;
		obj.IsFemale = ((speaker != null) ? new bool?(speaker.IsFemale) : null);
		int value;
		if (speaker != null)
		{
			Clan clan3 = speaker.Clan;
			value = ((((clan3 != null) ? clan3.Leader : null) == speaker) ? 1 : 0);
		}
		else
		{
			value = 0;
		}
		obj.IsClanLeader = (byte)value != 0;
		obj.IdentityIds = list;
		obj.Skills = dictionary;
		obj.MappingValues = mappingValues;
		return obj;
	}

	private static string BuildPublishedProbeText(ChatterLineState line)
	{
		if (line != null)
		{
			return "action=" + line.Action + "\ntext=" + line.Text + "\ninner_voice=" + line.InnerVoiceText;
		}
		return "";
	}

	private void FinishStaleRequest(RequestSnapshot snapshot, string reason)
	{
		if (snapshot != null && snapshot.RequestSequence == _activeRequestSequence)
		{
			FinishRequest(snapshot, "stale_discarded", reason);
		}
	}

	private void FinishRequest(RequestSnapshot snapshot, string status, string reason)
	{
		if (snapshot != null && !_activeRequestTerminalPublished)
		{
			PublishProbe(snapshot, "RequestTerminal", status, snapshot.ActualBackend, "main_thread", snapshot.KnowledgeQueryTerms, snapshot.KnowledgeCandidateCount, snapshot.KnowledgeHitCount, cacheHit: false, 0.0, 0.0, null, reason);
			_activeRequestTerminalPublished = true;
		}
		_requestInFlight = false;
		_requestCancellation?.Dispose();
		_requestCancellation = null;
		_activeSnapshot = null;
		_activeRequestSequence = 0L;
	}

	private void PublishTerminalIfNeeded(string status, string reason)
	{
		if (_requestInFlight && _activeSnapshot != null && !_activeRequestTerminalPublished)
		{
			FinishRequest(_activeSnapshot, status, reason);
		}
	}

	private void PublishBlocked(string reason, bool automatic, ChatterMode? mode, CurrentSettlementContext? settlementContext = null, BattleGateEvaluation? battleGate = null)
	{
		ChatterProbeEventDraft draft = new ChatterProbeEventDraft
		{
			CampaignEpoch = _epoch,
			RequestSequence = 0L,
			Stage = "RequestBlocked",
			RequestedBackend = "standalone_lexical",
			Status = "blocked",
			ReasonCode = SafeReason(reason),
			ThreadRole = "main_thread",
			Automatic = automatic,
			ConversationMode = (mode?.ToString() ?? "automatic")
		};
		if (settlementContext.HasValue)
		{
			PopulateLocationProbeMetadata(draft, settlementContext.Value);
		}
		if (battleGate.HasValue)
		{
			PopulateBattleProbeMetadata(draft, battleGate.Value);
		}
		ChatterProbeApi.Publish(draft);
	}

	private void PublishProbe(RequestSnapshot snapshot, string stage, string status, string actualBackend, string threadRole, int queryTerms, int candidates, int hits, bool cacheHit, double elapsedMilliseconds, double mainThreadMilliseconds, ChatterProbeContentDraft content, string reason = "")
	{
		if (snapshot != null)
		{
			ChatterProbeEventDraft draft = new ChatterProbeEventDraft
			{
				CampaignEpoch = _epoch,
				RequestSequence = snapshot.RequestSequence,
				Stage = stage,
				RequestedBackend = "standalone_lexical",
				ActualBackend = (actualBackend ?? ""),
				Status = (status ?? ""),
				ReasonCode = SafeReason(reason),
				ThreadRole = (threadRole ?? ""),
				Automatic = snapshot.Automatic,
				ConversationMode = snapshot.Mode.ToString(),
				ParticipantCount = snapshot.ParticipantCount,
				QueryTermCount = queryTerms,
				CandidateCount = candidates,
				HitCount = hits,
				ResultCharacters = (content?.PublishedText?.Length ?? content?.RawResponse?.Length ?? (content?.KnowledgeContext?.Length).GetValueOrDefault()),
				CacheHit = cacheHit,
				ElapsedMilliseconds = elapsedMilliseconds,
				MainThreadMilliseconds = mainThreadMilliseconds,
				GenerationBackend = "openai_compatible_chat_completions",
				GenerationModel = (snapshot.ChatOptions?.Model ?? ""),
				GenerationHttpStatusCode = snapshot.GenerationHttpStatusCode,
				GenerationFinishReason = snapshot.GenerationFinishReason,
				PromptTokens = snapshot.GenerationPromptTokens,
				CompletionTokens = snapshot.GenerationCompletionTokens,
				TotalTokens = snapshot.GenerationTotalTokens,
				RelevantMemoryRecallCount = snapshot.RelevantMemoryRecallCount,
				RelevantThoughtRecallCount = snapshot.RelevantThoughtRecallCount,
				ProactiveMemoryRecallCount = snapshot.ProactiveMemoryRecallCount,
				ProactiveThoughtRecallCount = snapshot.ProactiveThoughtRecallCount,
				ThoughtRecallCommitted = (string.Equals(stage, "LineCommitted", StringComparison.Ordinal) && snapshot.RelevantThoughtRecallCount + snapshot.ProactiveThoughtRecallCount > 0),
				Content = content
			};
			PopulateRequestProbeMetadata(draft, snapshot);
			ChatterProbeApi.Publish(draft);
		}
	}

	private void PublishThoughtProbe(RequestSnapshot snapshot, ChatGenerationOptions thoughtOptions, string stage, string status, string reason, ThoughtGenerationResult result)
	{
		ChatterProbeEventDraft draft = new ChatterProbeEventDraft
		{
			CampaignEpoch = _epoch,
			RequestSequence = snapshot.RequestSequence,
			Stage = stage,
			RequestedBackend = "independent_thought",
			ActualBackend = "openai_compatible_chat_completions",
			Status = status,
			ReasonCode = SafeReason(reason),
			ThreadRole = "main_thread",
			Automatic = snapshot.Automatic,
			ConversationMode = snapshot.Mode.ToString(),
			ParticipantCount = snapshot.ParticipantCount,
			GenerationBackend = "openai_compatible_chat_completions",
			GenerationModel = (thoughtOptions?.Model ?? ""),
			GenerationHttpStatusCode = (result?.HttpStatusCode ?? 0),
			PromptTokens = (result?.PromptTokens ?? 0),
			CompletionTokens = (result?.CompletionTokens ?? 0),
			TotalTokens = (result?.TotalTokens ?? 0),
			RelevantMemoryRecallCount = snapshot.RelevantMemoryRecallCount,
			RelevantThoughtRecallCount = snapshot.RelevantThoughtRecallCount,
			ProactiveMemoryRecallCount = snapshot.ProactiveMemoryRecallCount,
			ProactiveThoughtRecallCount = snapshot.ProactiveThoughtRecallCount,
			ThoughtMemoryCommitted = string.Equals(status, "committed", StringComparison.Ordinal)
		};
		PopulateRequestProbeMetadata(draft, snapshot);
		ChatterProbeApi.Publish(draft);
	}

	private void PublishTransportProbe(RequestSnapshot snapshot, ChatTransportTelemetry telemetry)
	{
		if (snapshot != null && telemetry != null)
		{
			ChatterProbeEventDraft draft = new ChatterProbeEventDraft
			{
				CampaignEpoch = _epoch,
				RequestSequence = snapshot.RequestSequence,
				Stage = telemetry.Stage,
				RequestedBackend = "openai_compatible_chat_completions",
				ActualBackend = "openai_compatible_chat_completions",
				Status = telemetry.Status,
				ReasonCode = SafeReason(telemetry.ReasonCode),
				ThreadRole = "worker",
				Automatic = snapshot.Automatic,
				ConversationMode = snapshot.Mode.ToString(),
				ParticipantCount = snapshot.ParticipantCount,
				GenerationBackend = "openai_compatible_chat_completions",
				GenerationModel = (snapshot.ChatOptions?.Model ?? ""),
				GenerationHttpStatusCode = telemetry.HttpStatusCode,
				ElapsedMilliseconds = telemetry.ElapsedMilliseconds
			};
			PopulateRequestProbeMetadata(draft, snapshot);
			ChatterProbeApi.Publish(draft);
		}
	}

	private static void PopulateRequestProbeMetadata(ChatterProbeEventDraft draft, RequestSnapshot snapshot)
	{
		if (draft != null && snapshot != null)
		{
			draft.SettlementId = snapshot.SettlementId;
			draft.SettlementSource = snapshot.SettlementSource;
			draft.SettlementMenuId = snapshot.SettlementMenuId;
			draft.SettlementMenuOverlay = snapshot.SettlementMenuOverlay;
			draft.VerifiedSettlementMenu = snapshot.VerifiedSettlementMenu;
			draft.SettlementCacheFallback = snapshot.SettlementCacheFallback;
			draft.BattleGateState = snapshot.BattleGateState;
			draft.BattleGateReasonCode = snapshot.BattleGateReasonCode;
			draft.EffectiveCombat = snapshot.EffectiveCombat;
		}
	}

	private static void PopulateLocationProbeMetadata(ChatterProbeEventDraft draft, CurrentSettlementContext context)
	{
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		if (draft != null)
		{
			draft.SettlementId = SettlementId(context);
			draft.SettlementSource = context.Source.ToString();
			draft.SettlementMenuId = context.MenuId ?? string.Empty;
			MenuOverlayType overlay = context.Overlay;
			draft.SettlementMenuOverlay = ((object)(MenuOverlayType)(ref overlay)).ToString();
			draft.VerifiedSettlementMenu = context.IsVerifiedSettlementMenu;
			draft.SettlementCacheFallback = context.IsCacheFallback;
		}
	}

	private static void PopulateBattleProbeMetadata(ChatterProbeEventDraft draft, BattleGateEvaluation evaluation)
	{
		if (draft != null)
		{
			draft.BattleGateState = evaluation.State.ToString();
			draft.BattleGateReasonCode = evaluation.ReasonCode;
			draft.EffectiveCombat = evaluation.State == BattleGateState.Blocked;
		}
	}

	private static string SafeReason(string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return Bound(value, 256);
		}
		return "none";
	}

	private static int CurrentCampaignDay()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		if (Campaign.Current != null)
		{
			CampaignTime now = CampaignTime.Now;
			return (int)Math.Floor(((CampaignTime)(ref now)).ToDays);
		}
		return 0;
	}

	private static void LogDiagnostic(ChatterMcmSettings settings, string message)
	{
		if (settings != null && settings.EnableDiagnosticLogging)
		{
			Log.Info(message);
		}
	}

	private static string Bound(string value, int maximum)
	{
		string text = (value ?? "").Replace('\0', ' ').Trim();
		if (text.Length <= maximum)
		{
			return text;
		}
		return text.Substring(0, maximum).TrimEnd();
	}

	private void InvalidateKnowledgeCache()
	{
		LogDiagnostic(GlobalSettings<ChatterMcmSettings>.Instance, "knowledge_cache_invalidated");
	}
}
