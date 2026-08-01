using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using TaleWorlds.SaveSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// NPC 委托任务 QuestBase 子类。
    /// Phase 2 完整版：活捉/击杀区分、质量评级、难度分级、动态变故、旅途事件、定金追讨。
    /// </summary>
    public class CommissionQuest : QuestBase
    {
        [SaveableField(40)] private CommissionData _data;
        [SaveableField(41)] private int _currentProgress;
        [SaveableField(42)] private JournalLog _progressLog;
        [SaveableField(43)] private int _totalProgress;
        [SaveableField(44)] private int _playerCasualtiesAtStart;
        [SaveableField(45)] private bool _isTargetCaptured; // 目标是否被活捉
        [SaveableField(46)] private CommissionGrade _finalGrade;
        [SaveableField(47)] private bool _depositRepaid;
        [SaveableField(48)] private string _escortPartyId; // 商队/目标部队的 ID
        [SaveableField(51)] private bool _bribeAttempted;
        [SaveableField(52)] private bool _bribeSuccessful;
        [SaveableField(49)] private JournalLog _findGiverLog;  // 阶段1：找委托人
        [SaveableField(50)] private JournalLog _rewardLog;     // 阶段3：领报酬
        [SaveableField(54)] private bool _suspectIdentifiedLogged;  // 防止 Intent 和事件双重日志

        public override bool IsRemainingTimeHidden => false;
        // 任务标题兜底：无风味描述时的通用委托标题
        public override TextObject Title => new TextObject(_data?.GetFlavorDescription() ?? LWNTextHelper.ResolveText("LWN_quest_commission_title_fallback", "Commission"));
        public CommissionData Data => _data;
        public Hero CommissionGiver => _data?.QuestGiver;
        public CommissionGrade FinalGrade => _finalGrade;

        public static bool IsHeroInvolvedInActiveCommission(Hero hero, out CommissionQuest foundQuest, out bool isGiver)
        {
            foundQuest = null;
            isGiver = false;
            if (hero == null) return false;
            foreach (var quest in Campaign.Current.QuestManager.Quests)
            {
                if (quest is CommissionQuest cq)
                {
                    if (cq._data?.QuestGiver == hero) { foundQuest = cq; isGiver = true; return true; }
                    if (cq._data?.TargetHero == hero) { foundQuest = cq; isGiver = false; return true; }
                }
            }
            return false;
        }

        public static int GetActiveCommissionCount()
        {
            return Campaign.Current.QuestManager.Quests.Count(q => q is CommissionQuest);
        }

        public CommissionQuest(string questId, CommissionData data)
            : base(questId, data.QuestGiver,
                  CampaignTime.Now + CampaignTime.Days(data.TimeRemainingHours / 24f),
                  data.NegotiatedReward)
        {
            _data = data;
            _currentProgress = 0;
            _totalProgress = 1;
            _playerCasualtiesAtStart = 0;
            _isTargetCaptured = false;
            _finalGrade = CommissionGrade.Passable;
            _depositRepaid = false;
            SetDialogs();
        }

        /// <summary>用于扫描未确认的委托（从告示板接取后、见委托人前）</summary>
        public static CommissionQuest FindPendingCommissionForGiver(Hero questGiver)
        {
            foreach (var quest in Campaign.Current.QuestManager.Quests)
            {
                if (quest is CommissionQuest cq && cq._data?.QuestGiver == questGiver
                    && cq._data.IsNarrativePhase)
                    return cq;
            }
            return null;
        }

        /// <summary>开始叙事阶段：不启动任务，只记录"去找委托人"</summary>
        public void BeginNarrativePhase()
        {
            string giverLoc = QuestGiver?.CurrentSettlement?.Name?.ToString()
                // 委托人所在地未知时的兜底文本
                ?? QuestGiver?.HomeSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_quest_commission_unknown_location", "Unknown location");
            DebugLogger.Log($"[CommissionQuest] BeginNarrativePhase: {_data.GetFlavorDescription()} giver={QuestGiver?.Name} at {giverLoc}");
            // 委托情报已记录的叙事日志：附委托风味描述
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_intel_recorded", "📋 Commission intel recorded: {DESC}", ("DESC", _data.GetFlavorDescription()))));

            // 阶段1：找到委托人（离散日志，0/1 表示是否完成）
            _findGiverLog = AddDiscreteLog(
                // 阶段1日志标题：前往委托人所在地当面了解详情
                new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_step1_goto", "Step 1: Go to {LOCATION} and find {GIVER} to learn the details of the commission in person", ("LOCATION", giverLoc), ("GIVER", QuestGiver?.Name.ToString()))),
                // 阶段1日志进度：已找到委托人
                new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_step1_found_giver", "Find {GIVER}", ("GIVER", QuestGiver?.Name.ToString()))),
                0, 1);
            // 不注册事件，不定金，不启动——只是占个位
        }

        /// <summary>委托人当面确认：叙事结束，正式启动委托</summary>
        public void ConfirmQuest()
        {
            if (_data == null || !_data.IsNarrativePhase) return;
            _data.IsNarrativePhase = false;

            DebugLogger.Log($"[CommissionQuest] ConfirmQuest: {_data.GetFlavorDescription()} giver={QuestGiver?.Name} deposit={_data.DepositAmount}");

            // 阶段1完成：找到了委托人
            if (_findGiverLog != null)
                _findGiverLog.UpdateCurrentProgress(1);

            // 定金到账
            if (_data.DepositAmount > 0)
            {
                int actualDeposit = AgentControlHelper.TransferGold(_data.QuestGiver, Hero.MainHero, _data.DepositAmount);
                _data.DepositAmount = actualDeposit;
                // 委托定金到账日志
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_deposit_received", "The {GOLD} denar deposit has been received.", ("GOLD", actualDeposit.ToString()))));
            }

            // 正式启动（事件已在 OnStartQuest 的叙事分支里注册过了，这里只补运行启动逻辑）
            _playerCasualtiesAtStart = CountPlayerWounded();
            PerformFullStartup();
        }

        /// <summary>执行完整的委托启动逻辑（日志、进度条、生成部队/商队等）</summary>
        private void PerformFullStartup()
        {
            TextObject logText = new TextObject(
                // 委托启动日志模板：任务标题+委托人+报酬+定金+期限+附加信息汇总
                "{=LWN_quest_commission_start}[Commission] {TITLE}\nGiver: {GIVER}\nReward: {REWARD} denars | Deposit: {DEPOSIT}\nDeadline: {DAYS} days\n{EXTRA}");
            logText.SetTextVariable("TITLE", _data.GetFlavorDescription());
            logText.SetTextVariable("GIVER", QuestGiver.Name);
            logText.SetTextVariable("REWARD", _data.NegotiatedReward);
            logText.SetTextVariable("DEPOSIT", _data.DepositAmount);
            logText.SetTextVariable("DAYS", ((int)(_data.TimeRemainingHours / 24f) + 1));

            logText.SetTextVariable("EXTRA", GetExtraInfo());
            AddLog(logText);

            if (_totalProgress > 0)
            {
                CreateObjectiveLog();
            }

            if (_data.DepositAmount > 0)
                // 委托定金到账日志
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_deposit_received", "The {GOLD} denar deposit has been received.", ("GOLD", _data.DepositAmount.ToString()))));

            if (Settings.Instance.IsLLMReady)
                _ = EnhanceFlavorText(_data.GetFlavorDescription());

            switch (_data.Category)
            {
                case CommissionCategory.Investigation: OnStartInvestigation(); break;
                case CommissionCategory.BountyHunt: OnStartBountyHunt(); break;
                case CommissionCategory.LegendaryHunt: OnStartLegendaryHunt(); break;
                case CommissionCategory.CaravanEscort: OnStartCaravanEscort(); break;
                case CommissionCategory.SupplyEmergency: OnStartSupplyEmergency(); break;
                case CommissionCategory.UndergroundFight: OnStartUndergroundFight(); break;
                case CommissionCategory.VillageDefense: OnStartVillageDefense(); break;
                case CommissionCategory.LostItem: OnStartLostItem(); break;
                case CommissionCategory.PrisonBreak: OnStartPrisonBreak(); break;
                case CommissionCategory.SupplyIntercept: OnStartSupplyIntercept(); break;
                case CommissionCategory.HideoutClear: OnStartHideoutClear(); break;
                case CommissionCategory.EmergencyDelivery: OnStartEmergencyDelivery(); break;
                case CommissionCategory.TreasureHunt: OnStartTreasureHunt(); break;
                case CommissionCategory.HorseAcquisition: OnStartHorseAcquisition(); break;
                case CommissionCategory.ArenaSpecial: OnStartArenaSpecial(); break;
                case CommissionCategory.DecoyMission: OnStartDecoyMission(); break;
                case CommissionCategory.ProcurementAgent: OnStartProcurementAgent(); break;
            }
        }

        #region QuestBase Overrides

        protected override void SetDialogs() { }

        protected override void InitializeQuestOnGameLoad()
        {
            SetDialogs();
            // 读档后验证生成的部队是否还存在
            if (!string.IsNullOrEmpty(_escortPartyId))
            {
                bool partyExists = false;
                foreach (var mp in Campaign.Current.MobileParties)
                {
                    if (mp.StringId == _escortPartyId)
                    {
                        partyExists = true;
                        break;
                    }
                }
                if (!partyExists)
                {
                    DebugLogger.Log($"[CommissionQuest] InitializeQuestOnGameLoad FAIL: party disappeared after load, partyId={_escortPartyId} category={_data?.Category}");
                    // 部队已消失（被其他队伍消灭或游戏清理）
                    // 对于依赖部队的委托类型，自动失败
                    if (_data.Category == CommissionCategory.CaravanEscort ||
                        _data.Category == CommissionCategory.BountyHunt ||
                        _data.Category == CommissionCategory.LegendaryHunt ||
                        _data.Category == CommissionCategory.VillageDefense ||
                        _data.Category == CommissionCategory.SupplyIntercept ||
                        _data.Category == CommissionCategory.DecoyMission)
                    {
                        // 读档后委托目标部队已消失的失败日志
                        AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_target_party_gone", "The commission's target party has disappeared after loading the save — the commission was cancelled automatically.")));
                        _escortPartyId = null;
                        FailQuest();
                        return;
                    }
                    _escortPartyId = null;
                }
            }
        }

        protected override void HourlyTick() { }

        protected override void RegisterEvents()
        {
            DebugLogger.Log($"[CommissionQuest] RegisterEvents: category={_data?.Category} questId={StringId}");
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            switch (_data.Category)
            {
                case CommissionCategory.Investigation:
                    // NPC 后台调查推进 → 订阅 WorldEvent 阶段变化
                    WorldEventStore.OnEventStageChanged += OnWorldEventStageChangedForQuest;
                    break;
                case CommissionCategory.BountyHunt:
                case CommissionCategory.LegendaryHunt:
                case CommissionCategory.HideoutClear:
                    CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
                    break;
                case CommissionCategory.CaravanEscort:
                case CommissionCategory.EmergencyDelivery:
                    CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
                    break;
                case CommissionCategory.SupplyEmergency:
                case CommissionCategory.ProcurementAgent:
                case CommissionCategory.HorseAcquisition:
                    CampaignEvents.PlayerInventoryExchangeEvent.AddNonSerializedListener(this, OnInventoryExchange);
                    CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
                    break;
                case CommissionCategory.UndergroundFight:
                case CommissionCategory.ArenaSpecial:
                    CampaignEvents.TournamentFinished.AddNonSerializedListener(this, OnTournamentFinished);
                    break;
                case CommissionCategory.LostItem:
                case CommissionCategory.TreasureHunt:
                    CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
                    break;
                case CommissionCategory.SupplyIntercept:
                case CommissionCategory.DecoyMission:
                    CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
                    break;
                case CommissionCategory.VillageDefense:
                    CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
                    CampaignEvents.MapEventStarted.AddNonSerializedListener(this, OnMapEventStartedForBribe);
                    CampaignEvents.VillageLooted.AddNonSerializedListener(this, OnVillageLooted);
                    break;
                case CommissionCategory.PrisonBreak:
                    CampaignEvents.PrisonersChangeInSettlement.AddNonSerializedListener(this, OnPrisonerChanged);
                    break;
            }
        }

        protected override void OnStartQuest()
        {
            SetDialogs();
            DebugLogger.Log($"[CommissionQuest] OnStartQuest: {_data?.GetFlavorDescription()} narrativePhase={_data?.IsNarrativePhase} questId={StringId}");

            // 叙事阶段：不启动任何游戏逻辑，只等玩家找到委托人
            if (_data.IsNarrativePhase)
            {
                RegisterEvents(); // 只注册 DailyTick（健壮性检查）
                DebugLogger.Log($"[CommissionQuest] Narrative phase — waiting for player to find {QuestGiver?.Name}");
                return;
            }

            _playerCasualtiesAtStart = CountPlayerWounded();
            DebugLogger.Log($"[CommissionQuest] Full startup: giver={QuestGiver?.Name} reward={_data.NegotiatedReward} deposit={_data.DepositAmount} days={_data.TimeRemainingHours/24f:0} tier={_data.Tier}");

            // 记录世界事件导演：玩家接受了委托
            WorldEventDirector.RecordCommissionAccepted();

            TextObject logText = new TextObject(
                // 委托启动日志模板：任务标题+委托人+报酬+定金+期限+附加信息汇总
                "{=LWN_quest_commission_start}[Commission] {TITLE}\nGiver: {GIVER}\nReward: {REWARD} denars | Deposit: {DEPOSIT}\nDeadline: {DAYS} days\n{EXTRA}");
            logText.SetTextVariable("TITLE", _data.GetFlavorDescription());
            logText.SetTextVariable("GIVER", QuestGiver.Name);
            logText.SetTextVariable("REWARD", _data.NegotiatedReward);
            logText.SetTextVariable("DEPOSIT", _data.DepositAmount);
            logText.SetTextVariable("DAYS", ((int)(_data.TimeRemainingHours / 24f) + 1));

            logText.SetTextVariable("EXTRA", GetExtraInfo());
            AddLog(logText);

            if (_totalProgress > 0)
            {
                CreateObjectiveLog();
            }

            if (_data.DepositAmount > 0)
            {
                // 委托定金到账日志
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_deposit_received", "The {GOLD} denar deposit has been received.", ("GOLD", _data.DepositAmount.ToString()))));
            }

            // 异步增强风味文本
            if (Settings.Instance.IsLLMReady)
                _ = EnhanceFlavorText(_data.GetFlavorDescription());

            switch (_data.Category)
            {
                case CommissionCategory.Investigation: OnStartInvestigation(); break;
                case CommissionCategory.BountyHunt: OnStartBountyHunt(); break;
                case CommissionCategory.LegendaryHunt: OnStartLegendaryHunt(); break;
                case CommissionCategory.CaravanEscort: OnStartCaravanEscort(); break;
                case CommissionCategory.SupplyEmergency: OnStartSupplyEmergency(); break;
                case CommissionCategory.UndergroundFight: OnStartUndergroundFight(); break;
                case CommissionCategory.VillageDefense: OnStartVillageDefense(); break;
                case CommissionCategory.LostItem: OnStartLostItem(); break;
                case CommissionCategory.PrisonBreak: OnStartPrisonBreak(); break;
                case CommissionCategory.SupplyIntercept: OnStartSupplyIntercept(); break;
                case CommissionCategory.HideoutClear: OnStartHideoutClear(); break;
                case CommissionCategory.EmergencyDelivery: OnStartEmergencyDelivery(); break;
                case CommissionCategory.TreasureHunt: OnStartTreasureHunt(); break;
                case CommissionCategory.HorseAcquisition: OnStartHorseAcquisition(); break;
                case CommissionCategory.ArenaSpecial: OnStartArenaSpecial(); break;
                case CommissionCategory.DecoyMission: OnStartDecoyMission(); break;
                case CommissionCategory.ProcurementAgent: OnStartProcurementAgent(); break;
            }
            DebugLogger.Log($"[CommissionQuest] OnStartQuest DONE: category={_data.Category} giver={QuestGiver?.Name} questId={StringId}");
        }

        protected override void OnCompleteWithSuccess()
        {
            WorldEventStore.OnEventStageChanged -= OnWorldEventStageChangedForQuest;
            if (QuestGiver == null) return;

            // 如果走的是新流程（CompleteWithRewardCollection 已结算），跳过旧逻辑
            if (_data != null && _data.IsObjectivesComplete)
            {
                DebugLogger.Log($"[CommissionQuest] OnCompleteWithSuccess: already settled via reward collection flow");
                return;
            }

            // 旧存档兼容：直接完成（无延迟领报酬流程）
            ComputeFinalGrade();
            int reward = CalculateFinalReward();
            int trustDelta = GetGradeTrustDelta();

            DebugLogger.Log($"[CommissionQuest] OnCompleteWithSuccess (legacy): {_data?.GetFlavorDescription()} grade={_finalGrade} reward={reward}");

            AgentControlHelper.TransferGold(QuestGiver, Hero.MainHero, reward);
            ChangeRelationAction.ApplyPlayerRelation(QuestGiver, 5);
            GainRenownAction.Apply(Hero.MainHero, 2);
            int oldTrust = TrustSystem.GetTrust(QuestGiver);
            TrustSystem.AddTrust(QuestGiver, trustDelta);

            // 难度递进：记录完成
            var oldTier = CommissionTierProgression.GetAvailableTier(_data.Category);
            CommissionTierProgression.RecordCompletion(_data.Category, _data.Tier, _finalGrade);
            var newTier = CommissionTierProgression.GetAvailableTier(_data.Category);

            // ── 叙事反馈 ──
            string milestoneMsg = CommissionNarrative.CheckTrustMilestone(QuestGiver, oldTrust,
                TrustSystem.GetTrust(QuestGiver));
            if (!string.IsNullOrEmpty(milestoneMsg))
                AddLog(new TextObject(milestoneMsg));

            string tierMsg = CommissionNarrative.CheckTierUnlock(_data.Category, oldTier, newTier);
            if (!string.IsNullOrEmpty(tierMsg))
                AddLog(new TextObject(tierMsg));

            // 高难度完成消除恶名
            if (_data.Tier >= CommissionTier.Expert && InfamySystem.Infamy > 0)
            {
                InfamySystem.ReduceInfamy(1);
                // 完成高难度委托削减恶名的日志
                AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_hard_complete_infamy", "Completed a high-difficulty commission — infamy -1.")));
            }

            // 清理生成的地图部队
            CleanupSpawnedParty();

            string gradeStr = GetGradeDisplayName();
            // 委托完成日志：评级+尾款到账
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_complete_final_payment",
                "Commission complete! Grade: {GRADE} — final payment of {REWARD} denars received.",
                ("GRADE", gradeStr), ("REWARD", reward.ToString()))));
            // 委托完成日志：与委托人的信任度变化
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_trust_changed",
                "Your standing with {GIVER} changed by {TRUSTDELTA} (currently: {TRUSTDESC})",
                ("GIVER", QuestGiver.Name.ToString()),
                ("TRUSTDELTA", (trustDelta >= 0 ? "+" : "") + trustDelta),
                ("TRUSTDESC", TrustSystem.GetTrustDescription(TrustSystem.GetTrust(QuestGiver))))));
        }

        protected override void OnTimedOut()
        {
            DebugLogger.Log($"[CommissionQuest] OnTimedOut: {_data?.GetFlavorDescription()} category={_data?.Category} deposit={_data?.DepositAmount} repaid={_depositRepaid} timeRemain={_data?.TimeRemainingHours}h");

            // DecoyMission: 自然超时 = 坚持到了委托人撤离，属成功
            if (_data.Category == CommissionCategory.DecoyMission && _data.TimeRemainingHours <= 0)
            {
                DebugLogger.Log($"[CommissionQuest] OnTimedOut DecoyMission: time expired naturally → SUCCESS");
                CleanupSpawnedParty();
                UpdateProgress(_totalProgress);
                return;
            }

            _finalGrade = CommissionGrade.Failed;
            CleanupSpawnedParty();

            if (_data.DepositAmount > 0 && !_depositRepaid)
            {
                ShowDepositRepaymentInquiry();
            }
            else
            {
                int penalty = _data.DepositAmount > 0 ? -15 : -5;
                ChangeRelationAction.ApplyPlayerRelation(QuestGiver, penalty);
                TrustSystem.AddTrust(QuestGiver, -10);
                // 委托超时失败日志：与委托人关系恶化
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_timeout_failed", "The commission failed due to timeout! Your relationship with {GIVER} has worsened.", ("GIVER", QuestGiver.Name.ToString()))));
            }
        }

        public override void OnFailed()
        {
            DebugLogger.Log($"[CommissionQuest] OnFailed: {_data?.GetFlavorDescription()} giver={QuestGiver?.Name} category={_data?.Category} worldEventId={_data?.WorldEventId} progress={_currentProgress}/{_totalProgress} timeRemain={_data?.TimeRemainingHours}h");
            if (QuestGiver == null) return;
            CleanupSpawnedParty();
            ChangeRelationAction.ApplyPlayerRelation(QuestGiver, -20);
            TrustSystem.AddTrust(QuestGiver, -20);
            // 委托失败日志：委托人对玩家失望
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_failed_disappoint", "The commission failed! {GIVER} is deeply disappointed in you.", ("GIVER", QuestGiver.Name.ToString()))));
        }

        #endregion

        #region Event Handlers

        private void OnDailyTick()
        {
            if (_data == null) return;

            // 时间递减
            _data.TimeRemainingHours = Math.Max(0, _data.TimeRemainingHours - 24f);

            DebugLogger.Log($"[CommissionQuest] DailyTick: {_data.GetFlavorDescription()} timeRemain={_data.TimeRemainingHours:0}h reward={_data.NegotiatedReward}");

            // 超时报酬递减
            if (_data.TimeRemainingHours <= 0 && _data.NegotiatedReward > 0)
            {
                float dailyDecay = _data.NegotiatedReward * 0.05f;
                int floor = Math.Max(1, (int)(_data.DepositAmount * 0.5f));
                _data.NegotiatedReward = Math.Max(floor, _data.NegotiatedReward - (int)dailyDecay);
            }

            // 动态变故检测
            ComplicationTable.CheckAndTrigger(_data, this);

            // 旅途事件，先屏蔽了，突然弹窗特别突兀
            // JourneyEvents.TryTrigger(_data, this);

            // ── DecoyMission: 生存计时 ──
            if (_data.Category == CommissionCategory.DecoyMission)
            {
                _data.PhaseProgress++;
                _data.NegotiatedReward += 50;
                // 坚持到时限结束即成功
                if (_data.TimeRemainingHours <= 0)
                {
                    // 诱敌任务成功：委托人安全撤离
                    AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_decoy_safe", "The giver has safely escaped! The mission to lure away the pursuers is complete.")));
                    CleanupSpawnedParty();
                    UpdateProgress(_totalProgress);
                    return;
                }
            }

            // ── 健壮性检查 ──
            // 委托人死亡 → 委托失败
            if (QuestGiver != null && !QuestGiver.IsAlive)
            {
                DebugLogger.Log($"[CommissionQuest] OnDailyTick FAIL: giver died, giver={QuestGiver.Name} isObjectivesComplete={_data?.IsObjectivesComplete}");
                // 如果目标已完成但未领报酬，先自动支付再失败
                if (_data != null && _data.IsObjectivesComplete)
                {
                    // 委托人去世但目标已完成：报酬自动结算
                    AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_giver_deceased_pay", "The giver {GIVER} has passed away — the reward has been settled automatically.", ("GIVER", QuestGiver.Name.ToString()))));
                    _data.RewardPayer = null; // 强制用 QuestGiver 遗产支付
                    CompleteWithRewardCollection();
                    return;
                }
                // 委托人去世导致委托取消的日志
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_giver_deceased_cancel", "The giver {GIVER} has passed away — the commission was cancelled automatically.", ("GIVER", QuestGiver.Name.ToString()))));
                FailQuest();
                return;
            }

            // 委托人被囚禁 > 30 天 → 委托失败
            if (QuestGiver != null && QuestGiver.IsPrisoner)
            {
                DebugLogger.Log($"[CommissionQuest] OnDailyTick FAIL: giver imprisoned, giver={QuestGiver.Name} category={_data?.Category}");
                // 委托人长期被囚禁导致委托取消的日志
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_giver_imprisoned_cancel", "The giver {GIVER} has been imprisoned for too long — the commission was cancelled automatically.", ("GIVER", QuestGiver.Name.ToString()))));
                FailQuest();
                return;
            }

            // BountyHunt: 目标被第三方击杀 → 委托失败
            if ((_data.Category == CommissionCategory.BountyHunt ||
                 _data.Category == CommissionCategory.LegendaryHunt) &&
                _data.TargetHero != null && !_data.TargetHero.IsAlive && _currentProgress == 0)
            {
                DebugLogger.Log($"[CommissionQuest] OnDailyTick FAIL: target killed by third party, target={_data.TargetHero.Name} category={_data.Category}");
                // 目标被第三方击杀导致委托失败的日志
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_target_killed_third_party", "The target {TARGET} has been killed by someone else — the commission failed.", ("TARGET", _data.TargetHero.Name.ToString()))));
                FailQuest();
                return;
            }

            // CaravanEscort: 商队被摧毁 → 委托失败
            if ((_data.Category == CommissionCategory.CaravanEscort) &&
                !string.IsNullOrEmpty(_escortPartyId) && _currentProgress == 0)
            {
                MobileParty escortParty = null;
                foreach (var mp in Campaign.Current.MobileParties)
                {
                    if (mp.StringId == _escortPartyId) { escortParty = mp; break; }
                }
                if (escortParty == null || escortParty.MemberRoster?.TotalManCount <= 0)
                {
                    DebugLogger.Log($"[CommissionQuest] OnDailyTick FAIL: caravan destroyed, partyId={_escortPartyId}");
                    // 商队被摧毁导致委托失败的日志
                    AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_caravan_destroyed", "The caravan has been destroyed! The commission failed.")));
                    _escortPartyId = null;
                    FailQuest();
                    return;
                }
            }

            // 押送阶段劫囚检测（BountyHunt 活捉后）
            if (_isTargetCaptured && _data.Category == CommissionCategory.BountyHunt)
            {
                TryPrisonerEscapeEvent();
            }

            // ── PrisonBreak: 到达监狱城镇时给出提示 ──
            if (_data.Category == CommissionCategory.PrisonBreak && _data.TargetHero != null)
            {
                if (Hero.MainHero.CurrentSettlement != null &&
                    Hero.MainHero.CurrentSettlement.StringId == _data.TargetSettlementId)
                {
                    // 玩家在监狱城镇
                    if (!_data.TargetHero.IsPrisoner)
                    {
                        // 目标已被释放！立即检测完成
                        // 目标已不在监狱的日志
                        AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_prisoner_escaped", "{TARGET} no longer seems to be in prison!", ("TARGET", _data.TargetHero.Name.ToString()))));
                        UpdateProgress(_totalProgress);
                        return;
                    }
                    // 提醒玩家
                    if (_data.PhaseProgress % 3 == 0) // 每 3 天提醒一次
                    {
                        // 定期提醒玩家解救囚犯的日志
                        AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_prison_reminder",
                            "Hint: You are in {LOCATION} — find a way to sneak into the prison and rescue {TARGET}.",
                            ("LOCATION", Hero.MainHero.CurrentSettlement.Name.ToString()), ("TARGET", _data.TargetHero.Name.ToString()))));
                    }
                    _data.PhaseProgress++;
                }
            }
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (_data == null) return;

            // DecoyMission 特殊处理：玩家输赢都要结算
            if (_data.Category == CommissionCategory.DecoyMission)
            {
                DebugLogger.Log($"[CommissionQuest] OnMapEventEnded DecoyMission: IsPlayerMapEvent={mapEvent.IsPlayerMapEvent} WinningSide={mapEvent.WinningSide} PlayerSide={mapEvent.PlayerSide} DefenderSide={mapEvent.DefenderSide} AttackerSide={mapEvent.AttackerSide} eventId={_data?.WorldEventId}");
                if (mapEvent.IsPlayerMapEvent)
                    HandleDecoyFightResult(mapEvent);
                else
                    DebugLogger.Log($"[CommissionQuest] OnMapEventEnded DecoyMission: ignoring non-player MapEvent (WinningSide={mapEvent.WinningSide})");
                return;
            }

            if (!mapEvent.IsPlayerMapEvent || mapEvent.WinningSide != mapEvent.PlayerSide) return;

            switch (_data.Category)
            {
                case CommissionCategory.BountyHunt:
                case CommissionCategory.LegendaryHunt:
                    HandleBountyHuntVictory(mapEvent);
                    break;
                case CommissionCategory.SupplyIntercept:
                    HandleSupplyInterceptVictory(mapEvent);
                    break;
                case CommissionCategory.VillageDefense:
                    HandleVillageDefenseVictory(mapEvent);
                    break;
                case CommissionCategory.HideoutClear:
                    HandleHideoutClearVictory(mapEvent);
                    break;
                case CommissionCategory.DecoyMission:
                    HandleDecoyFightResult(mapEvent);
                    break;
            }

            // 宿敌追踪：记录战斗中所有对立 Hero 的交手结果
            RecordNemesisOutcomes(mapEvent);

            // 卧底叛变：检查是否有可触发的内应
            TryTriggerInfiltration(mapEvent);
        }

        /// <summary>检查并触发卧底叛变。</summary>
        private void TryTriggerInfiltration(MapEvent mapEvent)
        {
            try
            {
                var infiltrator = StrategicInfiltration.CheckBattlefieldTrigger();
                if (infiltrator != null && infiltrator.Clan != null)
                {
                    // Hero 切换阵营支援玩家
                    infiltrator.Clan = Clan.PlayerClan;
                    NinjaNotificationManager.Show(
                        // 卧底战场倒戈的弹窗提示
                        LWNTextHelper.ResolveCompound("LWN_quest_commission_infiltrator_defect_battle", "On the battlefield, a familiar figure turns toward you — {NAME} has defected!", ("NAME", infiltrator.Name.ToString())),
                        () => { });
                    DebugLogger.Log($"[Infiltration] {infiltrator.Name} switched sides in battle!");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Infiltration] Trigger error: {ex.Message}");
            }
        }

        /// <summary>记录 MapEvent 中的所有对立 Hero 与玩家的交火结果。</summary>
        private void RecordNemesisOutcomes(MapEvent mapEvent)
        {
            if (mapEvent == null || _data == null) return;
            try
            {
                bool playerWon = mapEvent.WinningSide == mapEvent.PlayerSide;

                // 核心：记录委托目标 Hero
                if (_data.TargetHero != null && _data.TargetHero != Hero.MainHero)
                {
                    bool killed = !_data.TargetHero.IsAlive;
                    HeroNemesisTracker.RecordBattleOutcome(_data.TargetHero, playerWon, killed);
                }

                // 记录委托中涉及的 instigator（加害方）
                if (!string.IsNullOrEmpty(_data.WorldEventId))
                {
                    var evt = WorldEventStore.FindEvent(_data.WorldEventId);
                    if (evt != null && !string.IsNullOrEmpty(evt.InitiatorId))
                    {
                        var instigator = Hero.FindFirst(h => h.StringId == evt.InitiatorId);
                        if (instigator != null && instigator != _data.TargetHero && instigator != Hero.MainHero)
                        {
                            bool killed = !instigator.IsAlive;
                            HeroNemesisTracker.RecordBattleOutcome(instigator, playerWon, killed);

                            // 宿敌复仇事件：玩家赢了但没杀死 → 宿敌升级，下次更强更快
                            if (evt.Type == EventType.NemesisRevenge && playerWon && !killed)
                            {
                                var record = HeroNemesisTracker.GetRecord(instigator);
                                if (record != null && record.Level < NemesisLevel.Legendary)
                                {
                                    record.Level = (NemesisLevel)(Math.Min((int)record.Level + 1, (int)NemesisLevel.Legendary));
                                    // 下次复仇间隔缩短
                                    HeroNemesisTracker.ScheduleRevenge(record);
                                    // 宿敌逃脱的弹窗文案
                                    string escalateMsg = record.Level >= NemesisLevel.ArchNemesis
                                        // 宿敌升级为死仇的文案
                                        ? LWNTextHelper.ResolveCompound("LWN_quest_commission_nemesis_escalated", "{NAME} escaped again — the feud between you has reached the point of no return.", ("NAME", instigator.Name.ToString()))
                                        // 宿敌准备卷土重来的文案
                                        : LWNTextHelper.ResolveCompound("LWN_quest_commission_nemesis_escaped", "{NAME} escaped once more. He knows you are stronger now — next time he will bring more men.", ("NAME", instigator.Name.ToString()));
                                    NinjaNotificationManager.Show(escalateMsg, () => { });
                                    DebugLogger.Log($"[Nemesis] {instigator.Name} escaped again, escalated to {record.Level}");
                                }
                            }
                        }
                    }
                }

                // 检查卧底叛变触发：敌方阵营中有已策反的 Hero → 切换阵营（不限于 WorldEvent 委托）
                if (playerWon)
                {
                    var defector = StrategicInfiltration.CheckBattlefieldTrigger();
                    if (defector != null)
                    {
                        // 策反成功的战场倒戈弹窗文案
                        string defectMsg = LWNTextHelper.ResolveCompound("LWN_quest_commission_defector_battle", "{NAME} has defected on the battlefield! — This is the payoff of your recruitment.", ("NAME", defector.Name.ToString()));
                        NinjaNotificationManager.Show(defectMsg, () => { });
                        InformationManager.DisplayMessage(new InformationMessage(defectMsg));
                        DebugLogger.Log($"[Infiltration] Defector triggered in battle: {defector.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Nemesis] RecordNemesisOutcomes error: {ex.Message}");
            }
        }

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (_data == null || party != MobileParty.MainParty) return;
            string targetId = _data.TargetSettlementId;

            switch (_data.Category)
            {
                case CommissionCategory.CaravanEscort:
                    if (!string.IsNullOrEmpty(targetId) && settlement.StringId == targetId)
                    {
                        CleanupSpawnedParty();
                        UpdateProgress(_totalProgress);
                        // 护送任务到达目的地的日志
                        AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_arrived_destination", "Arrived at the destination {LOCATION}!", ("LOCATION", settlement.Name.ToString()))));
                    }
                    break;
                case CommissionCategory.EmergencyDelivery:
                    if (!string.IsNullOrEmpty(targetId) && settlement.StringId == targetId)
                    {
                        if (HasRequiredItems())
                        {
                            ConsumeRequiredItems();
                            CleanupSpawnedParty();
                            UpdateProgress(_totalProgress);
                            // 紧急送货任务送达成功的日志
                            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_delivery_delivered", "The required supplies have been delivered to {LOCATION}!", ("LOCATION", settlement.Name.ToString()))));
                        }
                        else
                        {
                            // 到达目的地但物资不足的日志
                            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_delivery_missing_items", "Arrived at {LOCATION}, but the supplies are insufficient! Make sure you are carrying all of them.", ("LOCATION", settlement.Name.ToString()))));
                        }
                    }
                    break;
                case CommissionCategory.SupplyEmergency:
                case CommissionCategory.ProcurementAgent:
                case CommissionCategory.HorseAcquisition:
                    if (!string.IsNullOrEmpty(targetId) && settlement.StringId == targetId)
                    {
                        if (HasRequiredItems())
                        {
                            ConsumeRequiredItems();
                            UpdateProgress(_totalProgress);
                            // 供应/采购任务送达成功的日志
                            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_delivery_delivered", "The required supplies have been delivered to {LOCATION}!", ("LOCATION", settlement.Name.ToString()))));
                        }
                        else
                        {
                            // 到达目的地但物资未备齐的日志
                            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_delivery_not_ready", "Arrived at {LOCATION}, but the required supplies are not ready yet. Go purchase them first.", ("LOCATION", settlement.Name.ToString()))));
                        }
                    }
                    break;
                case CommissionCategory.LostItem:
                case CommissionCategory.TreasureHunt:
                    if (!string.IsNullOrEmpty(targetId) && settlement.StringId == targetId)
                    {
                        // 找到物品：生成奖励物品给玩家
                        if (!string.IsNullOrEmpty(_data.TargetItemId))
                        {
                            var foundItem = MBObjectManager.Instance.GetObject<ItemObject>(_data.TargetItemId);
                            if (foundItem != null && MobileParty.MainParty != null)
                            {
                                AgentControlHelper.TransferItems(null, Hero.MainHero, foundItem,
                                    _data.TargetItemCount > 0 ? _data.TargetItemCount : 1);
                                // 找到失物/宝藏的日志：物品名+数量
                                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_item_found", "Found {ITEM} ×{COUNT}!", ("ITEM", foundItem.Name.ToString()), ("COUNT", _data.TargetItemCount.ToString()))));
                            }
                        }
                        UpdateProgress(_totalProgress);
                        // 在目标地点找到目标的日志
                        AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_target_found", "Found the target in {LOCATION}!", ("LOCATION", settlement.Name.ToString()))));
                    }
                    break;
            }
        }

        private void OnInventoryExchange(List<(ItemRosterElement, int)> incomingItems,
            List<(ItemRosterElement, int)> outgoingItems, bool isTrading)
        {
            if (_data == null) return;
            if (_data.Category != CommissionCategory.SupplyEmergency &&
                _data.Category != CommissionCategory.ProcurementAgent &&
                _data.Category != CommissionCategory.HorseAcquisition) return;

            if (HasRequiredItems() && _currentProgress == 0)
            {
                _currentProgress = 1;
                if (_progressLog != null) _progressLog.UpdateCurrentProgress(_currentProgress);
                // 物资备齐可交货的日志
                AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_goods_ready", "All required supplies are ready! Head to the destination to deliver them.")));
            }
        }

        private void OnTournamentFinished(CharacterObject winner,
            MBReadOnlyList<CharacterObject> participants, Town town, ItemObject prize)
        {
            if (_data == null || winner != Hero.MainHero.CharacterObject) return;

            switch (_data.Category)
            {
                case CommissionCategory.UndergroundFight:
                    UpdateProgress(_totalProgress);
                    // 地下格斗任务在竞技场获胜的日志
                    AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_arena_won", "You won in the arena! The giver will be pleased.")));
                    break;
                case CommissionCategory.ArenaSpecial:
                    _currentProgress++;
                    if (_progressLog != null)
                        _progressLog.UpdateCurrentProgress(_currentProgress);
                    if (_currentProgress >= _totalProgress)
                    {
                        UpdateProgress(_totalProgress);
                        // 竞技场连胜达标完成的日志
                        AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_arena_streak_complete", "Won {COUNT} consecutive arena matches! The giver is very pleased.", ("COUNT", _totalProgress.ToString()))));
                    }
                    else
                    {
                        // 竞技场连胜进度日志：还差一场
                        AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_arena_progress", "Arena victory {PROGRESS}/{TOTAL}! One more win and the commission is done.", ("PROGRESS", _currentProgress.ToString()), ("TOTAL", _totalProgress.ToString()))));
                    }
                    break;
            }
        }

        private void OnVillageLooted(Village village)
        {
            if (_data == null || _data.Category != CommissionCategory.VillageDefense) return;
            if (village.Settlement.StringId == _data.TargetSettlementId &&
                village.Settlement.LastAttackerParty != MobileParty.MainParty)
            {
                // 村庄被洗劫的警告日志
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_village_looted_warning", "Warning: {LOCATION} has been looted! The commission may have failed.", ("LOCATION", village.Settlement.Name.ToString()))));
            }
        }

        private void OnPrisonerChanged(Settlement settlement, FlattenedTroopRoster troopRoster, Hero hero, bool takenFromPrison)
        {
            if (_data == null || _data.Category != CommissionCategory.PrisonBreak) return;
            if (_data.TargetHero != null && takenFromPrison)
            {
                foreach (var element in troopRoster)
                {
                    if (element.Troop?.HeroObject == _data.TargetHero)
                    {
                        UpdateProgress(_totalProgress);
                        // 越狱任务成功救出目标的日志
                        AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_prisoner_rescued", "{TARGET} has been successfully rescued!", ("TARGET", _data.TargetHero.Name.ToString()))));
                        break;
                    }
                }
            }
        }

        #endregion

        #region Victory Handlers (Kill vs Capture)

        private void HandleBountyHuntVictory(MapEvent mapEvent)
        {
            if (_data.TargetHero == null) return;

            // 检测目标是否在战败方
            bool targetInBattle = false;
            bool targetCaptured = false;

            foreach (var party in mapEvent.InvolvedParties)
            {
                if (party.Owner == _data.TargetHero)
                {
                    targetInBattle = true;
                    break;
                }
                // 检查是否为囚犯
                if (party.PrisonRoster?.GetTroopRoster()?.Any(e => e.Character?.HeroObject == _data.TargetHero) == true)
                {
                    targetInBattle = true;
                    targetCaptured = true;
                    break;
                }
            }

            if (!targetInBattle) return;

            _isTargetCaptured = targetCaptured;

            // 检查玩家囚犯栏
            if (!targetCaptured)
            {
                var prisonerRoster = MobileParty.MainParty?.PrisonRoster;
                if (prisonerRoster != null)
                {
                    foreach (var element in prisonerRoster.GetTroopRoster())
                    {
                        if (element.Character?.HeroObject == _data.TargetHero)
                        {
                            _isTargetCaptured = true;
                            break;
                        }
                    }
                }
            }

            UpdateProgress(_totalProgress);

            // 悬赏任务胜利结算文案：活捉/击杀分支
            string resultDesc = _isTargetCaptured
                // 活捉目标的胜利文案
                ? LWNTextHelper.ResolveCompound("LWN_quest_commission_bounty_captured", "The target {TARGET} has been captured alive! Escort them back for the full reward. Beware — allies may try to free them en route.", ("TARGET", _data.TargetHero.Name.ToString()))
                // 击杀目标的胜利文案
                : LWNTextHelper.ResolveCompound("LWN_quest_commission_bounty_killed", "The target {TARGET} has been defeated (killed). Capturing them alive would have paid more.", ("TARGET", _data.TargetHero.Name.ToString()));

            AddLog(new TextObject(resultDesc));
            DebugLogger.Log($"[CommissionQuest] BountyHuntVictory: target={_data.TargetHero?.Name} captured={_isTargetCaptured}");
        }

        private void HandleSupplyInterceptVictory(MapEvent mapEvent)
        {
            // 检测我们生成的补给队是否被击败（通过 _escortPartyId 匹配）
            if (!string.IsNullOrEmpty(_escortPartyId))
            {
                bool supplyPartyDefeated = mapEvent.InvolvedParties.Any(p =>
                    p.MobileParty != null && p.MobileParty.StringId == _escortPartyId);
                if (supplyPartyDefeated)
                {
                    UpdateProgress(_totalProgress);
                    // 成功截获敌方补给队的日志
                    AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_supply_intercepted", "Enemy supply convoy successfully intercepted!")));
                    return;
                }
            }
            // 兜底：检查是否有前往目标城镇的敌方队伍被击败
            if (!string.IsNullOrEmpty(_data.TargetSettlementId))
            {
                bool targetDefeated = mapEvent.InvolvedParties.Any(p =>
                    p.MobileParty?.TargetSettlement?.StringId == _data.TargetSettlementId);
                if (targetDefeated)
                {
                    UpdateProgress(_totalProgress);
                    // 成功截获敌方补给队的日志
                    AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_supply_intercepted", "Enemy supply convoy successfully intercepted!")));
                }
            }
        }

        private void HandleVillageDefenseVictory(MapEvent mapEvent)
        {
            // 检测来犯之敌是否被击败（WorldEvent 复用时可能是领主部队，非匪徒）
            bool raidersDefeated = false;

            // 方式1：按 party ID 精确匹配
            if (!string.IsNullOrEmpty(_escortPartyId))
            {
                raidersDefeated = mapEvent.InvolvedParties.Any(p =>
                    p.MobileParty?.StringId == _escortPartyId && p.Side != mapEvent.PlayerSide);
            }

            // 方式2：兜底 — 检测匪徒 faction
            if (!raidersDefeated)
            {
                raidersDefeated = mapEvent.InvolvedParties.Any(p =>
                    p.MapFaction != null && p.MapFaction.IsBanditFaction && p.Side != mapEvent.PlayerSide);
            }

            if (raidersDefeated)
            {
                DebugLogger.Log($"[CommissionQuest] VillageDefenseVictory: target={_data?.TargetSettlementId} party={_escortPartyId}");
                UpdateProgress(_totalProgress);
                // 村庄防守成功的日志
                AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_village_defended", "The village was defended! The attackers have been driven off.")));
            }
        }

        private void HandleHideoutClearVictory(MapEvent mapEvent)
        {
            // 检测是否清剿了匪穴/匪徒
            bool banditsDefeated = mapEvent.InvolvedParties.Any(p =>
                p.MapFaction != null && p.MapFaction.IsBanditFaction && p.Side != mapEvent.PlayerSide);
            if (banditsDefeated)
            {
                UpdateProgress(_totalProgress);
                // 匪穴清剿成功的日志
                AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_hideout_cleared", "The bandit hideout has been cleared! This area is finally safe.")));
            }
        }

        private void HandleDecoyFightResult(MapEvent mapEvent)
        {
            DebugLogger.Log($"[CommissionQuest] HandleDecoyFightResult: WinningSide={mapEvent.WinningSide} PlayerSide={mapEvent.PlayerSide} progress={_currentProgress}/{_totalProgress}");
            // 玩家选择反击追兵
            if (mapEvent.WinningSide == mapEvent.PlayerSide)
            {
                // 反击成功，提前完成（报酬按已坚持天数计算）
                DebugLogger.Log($"[CommissionQuest] HandleDecoyFightResult: player WON — completing quest early");
                // 诱敌任务反击追兵成功的日志
                AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_decoy_counterattack_won", "You drove off the pursuers! The giver used this time to escape safely.")));
                CleanupSpawnedParty();
                UpdateProgress(_totalProgress);
            }
            else
            {
                // 被追上击败 → 委托失败
                DebugLogger.Log($"[CommissionQuest] HandleDecoyFightResult: player LOST — failing quest");
                // 诱敌任务被追兵击败的失败日志
                AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_decoy_was_defeated", "You were defeated by the pursuers. The giver may not have had time to escape...")));
                _finalGrade = CommissionGrade.Failed;
                CleanupSpawnedParty();
                FailQuest();
            }
        }

        #endregion

        #region VillageDefense Bribe (大地图遭遇 → 贿赂匪徒)

        private void OnMapEventStartedForBribe(MapEvent mapEvent, PartyBase attackerParty, PartyBase defenderParty)
        {
            if (_data == null || _data.Category != CommissionCategory.VillageDefense) return;
            if (_bribeAttempted || _bribeSuccessful) return;

            string raiderPartyId = _escortPartyId;
            if (string.IsNullOrEmpty(raiderPartyId)) return;

            bool raidersInvolved = false;
            int raiderTroopCount = 0;
            if (attackerParty?.MobileParty?.StringId == raiderPartyId)
            { raidersInvolved = true; raiderTroopCount = attackerParty.MemberRoster?.TotalManCount ?? 0; }
            if (defenderParty?.MobileParty?.StringId == raiderPartyId)
            { raidersInvolved = true; raiderTroopCount = defenderParty.MemberRoster?.TotalManCount ?? 0; }
            if (!raidersInvolved) return;

            _bribeAttempted = true;
            // 村庄名称兜底文本
            string villageFallback = LWNTextHelper.ResolveText("LWN_quest_commission_village_fallback", "village");
            string villageName = !string.IsNullOrEmpty(_data.TargetSettlementId)
                ? Settlement.Find(_data.TargetSettlementId)?.Name?.ToString() ?? villageFallback : villageFallback;

            int tierFactor = _data.Tier switch
            { CommissionTier.Basic => 10, CommissionTier.Skilled => 15,
              CommissionTier.Expert => 25, CommissionTier.Legendary => 40, _ => 10 };
            int bribeCost = tierFactor * Math.Max(1, raiderTroopCount);
            float charmSkill = Hero.MainHero.GetSkillValue(DefaultSkills.Charm);
            float charmDiscount = 0.3f + (charmSkill / 300f) * 0.3f;
            int charmedCost = Math.Max(50, (int)(bribeCost * (1f - charmDiscount)));

            // 贿赂弹窗正文：前方匪徒情报 + 战斗/贿赂选项与费用明细
            string body = LWNTextHelper.ResolveCompound("LWN_quest_commission_raider_inquiry_body",
                "Raiders are ahead, about to pillage {LOCATION}!\n\n" +
                "⚔ Fight — meet the raiders head-on\n" +
                "💰 Bribe them to leave — {CHARMEDCOST} denars (originally {BRIBECOST}, Charm {CHARMSKILL} cuts it by {DISCOUNT}%)\n\nWhat will you do?",
                ("LOCATION", villageName), ("CHARMEDCOST", charmedCost.ToString()),
                ("BRIBECOST", bribeCost.ToString()), ("CHARMSKILL", charmSkill.ToString("0")),
                ("DISCOUNT", ((int)(charmDiscount * 100)).ToString()));

            DebugLogger.Log($"[CommissionQuest] VillageDefense bribe inquiry: raiders={raiderTroopCount} cost={charmedCost}/{bribeCost}");

            InformationManager.ShowInquiry(new InquiryData(
                // 贿赂弹窗标题：遭遇匪徒
                LWNTextHelper.ResolveText("LWN_quest_commission_raider_inquiry_title", "Raiders Ahead"), body, true, true,
                // 战斗按钮文案
                LWNTextHelper.ResolveText("LWN_quest_commission_fight_button", "⚔ Fight"),
                // 贿赂按钮文案：含贿赂金额
                LWNTextHelper.ResolveCompound("LWN_quest_commission_bribe_button", "💰 Bribe ({COST}G)", ("COST", charmedCost.ToString())),
                // 选择战斗的日志
                () => { AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_fight_chosen", "You chose to fight the raiders."))); },
                () => TryBribeRaiders(charmedCost, bribeCost, charmSkill, mapEvent)));
        }

        private void TryBribeRaiders(int actualCost, int baseCost, float charmSkill, MapEvent mapEvent)
        {
            float charmSuccessChance = 0.3f + (charmSkill / 300f) * 0.4f;
            bool charmSuccess = MBRandom.RandomFloat < charmSuccessChance;
            int finalCost = charmSuccess ? actualCost : baseCost;

            if (Hero.MainHero.Gold < finalCost)
            {
                InformationManager.DisplayMessage(
                    // 金币不足的提示信息
                    new InformationMessage(LWNTextHelper.ResolveCompound("LWN_quest_commission_gold_insufficient", "You only have {GOLD} denars — not enough (need {NEED}).", ("GOLD", Hero.MainHero.Gold.ToString()), ("NEED", finalCost.ToString())), Colors.Red));
                _bribeAttempted = false;
                return;
            }

            AgentControlHelper.TransferGold(Hero.MainHero, null, finalCost);
            _bribeSuccessful = true;

            // ── WorldEvent 关联的部队不能直接删除 → 重定向离开目标 ──
            if (!string.IsNullOrEmpty(_data?.WorldEventId) && !string.IsNullOrEmpty(_escortPartyId))
            {
                var worldEvent = WorldEventStore.FindEvent(_data.WorldEventId);
                if (worldEvent != null)
                {
                    var party = worldEvent.GeneratedParty;
                    if (party != null && party.IsActive)
                    {
                        // 重定向：让 party 远离目标定居点
                        Vec2 awayPos = V.Pos(worldEvent.TargetSettlement);
                        if (awayPos == Vec2.Zero) awayPos = V.Pos(party);
                        float angle = MBRandom.RandomFloat * 2f * (float)Math.PI;
                        awayPos += new Vec2((float)Math.Cos(angle) * 40f, (float)Math.Sin(angle) * 40f);
                        V.SetMoveTo(party, awayPos);
                        party.Ai.SetDoNotMakeNewDecisions(false);
                        party.SetPartyUsedByQuest(false);
                        Campaign.Current?.VisualTrackerManager?.RemoveTrackedObject(party, forceRemove: true);
                        DebugLogger.Log($"[CommissionQuest] VillageDefense bribe: redirected WorldEvent party {_escortPartyId} away from target");
                    }
                }
            }
            else
            {
                CleanupSpawnedParty();
            }

            if (charmSuccess)
                // Charm 检定成功后的砍价日志
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_bribe_charm_success", "Charm check succeeded! You haggled the bribe from {BASECOST} down to {FINALCOST} denars. The raider leader weighed the coin pouch, then turned and led his men away.", ("BASECOST", baseCost.ToString()), ("FINALCOST", finalCost.ToString()))));
            else
                // 按原价支付贿赂后的日志
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_bribe_paid", "You paid {FINALCOST} denars. The raider leader weighed the coin pouch, then turned grumbling and led his men away.", ("FINALCOST", finalCost.ToString()))));

            if (mapEvent != null)
            { try { mapEvent.FinalizeEvent(); } catch { } }

            UpdateProgress(_totalProgress);
            DebugLogger.Log($"[CommissionQuest] VillageDefense bribe success: cost={finalCost} charmSuccess={charmSuccess}");
        }

        #endregion

        #region Grade & Reward Calculation

        private void ComputeFinalGrade()
        {
            int currentWounded = CountPlayerWounded();
            int casualties = Math.Max(0, currentWounded - _playerCasualtiesAtStart);
            bool withinTimeLimit = _data.TimeRemainingHours > 0;

            // 完美：限时 + 无伤亡 + (悬赏类需要活捉)
            if (withinTimeLimit && casualties == 0 &&
                (_data.Category != CommissionCategory.BountyHunt || _isTargetCaptured))
            {
                _finalGrade = CommissionGrade.Perfect;
            }
            // 优良：限时 + 轻度伤亡(≤3)
            else if (withinTimeLimit && casualties <= 3)
            {
                _finalGrade = CommissionGrade.Good;
            }
            // 完成：其他
            else
            {
                _finalGrade = CommissionGrade.Passable;
            }
        }

        private int CalculateFinalReward()
        {
            float multiplier = _finalGrade switch
            {
                CommissionGrade.Perfect => 1.5f,
                CommissionGrade.Good => 1.0f,
                CommissionGrade.Passable => 0.7f,
                _ => 0.5f
            };

            // 悬赏类：击杀惩罚 ×0.5（计划 3.4）
            if ((_data.Category == CommissionCategory.BountyHunt ||
                 _data.Category == CommissionCategory.LegendaryHunt) && !_isTargetCaptured)
            {
                multiplier *= 0.5f;
            }

            // 速度奖
            float daysRemaining = _data.TimeRemainingHours / 24f;
            if (daysRemaining > 2) multiplier += 0.1f;
            if (daysRemaining > 5) multiplier += 0.1f;

            return (int)(_data.NegotiatedReward * multiplier);
        }

        private int GetGradeTrustDelta()
        {
            return _finalGrade switch
            {
                CommissionGrade.Perfect => 15,
                CommissionGrade.Good => 10,
                CommissionGrade.Passable => 5,
                CommissionGrade.Failed => -10,
                _ => 0
            };
        }

        private string GetGradeDisplayName()
        {
            return _finalGrade switch
            {
                // 评级显示名：完美
                CommissionGrade.Perfect => LWNTextHelper.ResolveText("LWN_quest_commission_grade_perfect", "⭐⭐⭐ Perfect"),
                // 评级显示名：优良
                CommissionGrade.Good => LWNTextHelper.ResolveText("LWN_quest_commission_grade_good", "⭐⭐ Good"),
                // 评级显示名：完成
                CommissionGrade.Passable => LWNTextHelper.ResolveText("LWN_quest_commission_grade_passable", "⭐ Completed"),
                // 评级显示名：失败
                CommissionGrade.Failed => LWNTextHelper.ResolveText("LWN_quest_commission_grade_failed", "✗ Failed"),
                // 评级显示名兜底：完成
                _ => LWNTextHelper.ResolveText("LWN_quest_commission_grade_default", "Completed")
            };
        }

        private string GetTierDisplayName()
        {
            return _data.Tier switch
            {
                // 难度显示名：简单
                CommissionTier.Basic => LWNTextHelper.ResolveText("LWN_quest_commission_tier_basic", "$ Easy"),
                // 难度显示名：普通
                CommissionTier.Skilled => LWNTextHelper.ResolveText("LWN_quest_commission_tier_skilled", "$$ Normal"),
                // 难度显示名：困难
                CommissionTier.Expert => LWNTextHelper.ResolveText("LWN_quest_commission_tier_expert", "$$$ Hard"),
                // 难度显示名：传奇
                CommissionTier.Legendary => LWNTextHelper.ResolveText("LWN_quest_commission_tier_legendary", "★★★★ Legendary"),
                // 难度显示名兜底：简单
                _ => LWNTextHelper.ResolveText("LWN_quest_commission_tier_default", "Easy")
            };
        }

        private int CountPlayerWounded()
        {
            int count = 0;
            if (MobileParty.MainParty?.MemberRoster != null)
            {
                foreach (var element in MobileParty.MainParty.MemberRoster.GetTroopRoster())
                {
                    if (element.Character?.IsHero == true) continue;
                    if (element.WoundedNumber > 0)
                        count += element.WoundedNumber;
                }
            }
            return count;
        }

        #endregion

        #region Type-Specific Startup

        private void OnStartInvestigation()
        {
            var settlement = !string.IsNullOrEmpty(_data.TargetSettlementId)
                ? Settlement.Find(_data.TargetSettlementId) : null;
            // 案发地名称兜底文本
            string locationName = settlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_quest_commission_crime_scene_fallback", "the crime scene");

            // 优先：从 WorldEvent 提取案情细节生成叙事日志
            var evt = !string.IsNullOrEmpty(_data.WorldEventId)
                ? WorldEventStore.FindEvent(_data.WorldEventId) : null;

            if (evt != null)
            {
                // 案情从事实派生（袭击+失窃如实还原），不再用 EventType 静态模板拼接
                string facts = evt.BuildDiscoveryFacts();
                // 现场描述兜底文本
                string scene = !string.IsNullOrEmpty(evt.Config?.CrimeScene) ? evt.Config.CrimeScene : LWNTextHelper.ResolveText("LWN_quest_commission_scene_fallback", "the scene");
                int witnessCount = evt.WitnessCount;
                int windowDays = evt.Config?.InvestigationWindowDays ?? 7;

                // 目击者人数的叙事表述
                string witnessClause = witnessCount > 0
                    // 有目击者的表述：目击人数
                    ? LWNTextHelper.ResolveCompound("LWN_quest_commission_witness_count", "{COUNT} people witnessed the incident.", ("COUNT", witnessCount.ToString()))
                    // 无目击者的表述
                    : LWNTextHelper.ResolveText("LWN_quest_commission_witness_none", "No one witnessed it for now.");

                // 调查任务叙事日志：前往案发地搜集线索
                AddLog(new TextObject(
                    // 调查叙事日志主体：地点+案情+目击情况+调查指引
                    LWNTextHelper.ResolveCompound("LWN_quest_commission_investigation_gather_clues",
                    "Go to {LOCATION} and gather clues near {SCENE}. {FACTS}, {WITNESS} Speak with the locals or return to the scene to investigate and find out who did it.",
                    ("LOCATION", locationName), ("SCENE", scene), ("FACTS", facts), ("WITNESS", witnessClause))));
                // 调查窗口提示日志：约多少天
                AddLog(new TextObject(
                    // 调查窗口天数提示
                    LWNTextHelper.ResolveCompound("LWN_quest_commission_investigation_window",
                    "Hint: The investigation window is about {DAYS} days. Once it lapses, the case will stall. Use the Scouting skill to speed up clue gathering.",
                    ("DAYS", windowDays.ToString()))));
            }
            else
            {
                // 回退：无 WorldEvent 时使用通用文本
                // 无 WorldEvent 时的调查引导日志
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_investigation_generic", "Go to {LOCATION} to gather clues. Speak with the locals or return to the scene to investigate and find out who did it.", ("LOCATION", locationName))));
                // 无 WorldEvent 时的调查时间提示
                AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_investigation_window_generic", "Hint: Time is limited — once the investigation window closes, the case will stall. Use the Scouting skill to speed up clue gathering.")));
            }
        }

        private void OnStartBountyHunt()
        {
            if (_data.TargetHero == null) return;

            // 如果关联了 WorldEvent，使用已有的 party，不重复生成
            if (!string.IsNullOrEmpty(_data.WorldEventId))
            {
                var worldEvent = WorldEventStore.FindEvent(_data.WorldEventId);
                if (worldEvent != null && !string.IsNullOrEmpty(worldEvent.GeneratedPartyId))
                {
                    _escortPartyId = worldEvent.GeneratedPartyId;
                    // 劫掠地点名称兜底文本
                    string raidingLoc = worldEvent.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_quest_commission_nearby_fallback", "nearby");
                    // 目标匪帮正在劫掠的日志
                    AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_bounty_gang_raiding", "The gang of {TARGET} is raiding {LOCATION}. Go stop them!", ("TARGET", _data.TargetHero.Name.ToString()), ("LOCATION", raidingLoc))));
                    // 活捉目标可获得额外报酬的提示
                    AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_bounty_capture_hint", "Hint: capturing the target alive earns extra reward. Use the Roguery skill for a night raid to improve your chances.")));
                    return;
                }
            }

            // 无 WorldEvent 关联 → 在大地图上生成目标部队
            SpawnBountyTargetParty();

            // 目标部队已出现的追踪引导日志
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_bounty_party_spawned", "The troops of {TARGET} have appeared nearby. Track them down and defeat them!", ("TARGET", _data.TargetHero.Name.ToString()))));
            // 活捉目标可获得额外报酬的提示
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_bounty_capture_hint", "Hint: capturing the target alive earns extra reward. Use the Roguery skill for a night raid to improve your chances.")));
        }

        private void OnStartCaravanEscort()
        {
            var targetSettlement = !string.IsNullOrEmpty(_data.TargetSettlementId)
                ? Settlement.Find(_data.TargetSettlementId) : null;

            // 生成商队跟随玩家
            SpawnEscortCaravanParty();

            if (targetSettlement != null)
            {
                // 商队出发日志：目的地+全程护送要求
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_caravan_departed", "The caravan has departed, destination: {LOCATION}. Escort it the whole way.", ("LOCATION", targetSettlement.Name.ToString()))));
                // 商队护送提示：Scout 侦察与随机事件
                AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_caravan_hint", "Hint: the Scout skill lets you spot ambushes in advance. Random events may occur on the road.")));
            }
        }

        private void OnStartSupplyEmergency()
        {
            if (!string.IsNullOrEmpty(_data.TargetItemId))
            {
                var item = MBObjectManager.Instance.GetObject<ItemObject>(_data.TargetItemId);
                // 采购清单日志：物品名+数量
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_supply_need_purchase", "Purchase needed: {ITEM} ×{COUNT}.", ("ITEM", item != null ? item.Name.ToString() : _data.TargetItemId), ("COUNT", _data.TargetItemCount.ToString()))));
                // 采购任务提示：跨城比价与砍价
                AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_supply_trade_hint", "Market prices differ between towns — the Trade skill helps you haggle. Manage the budget well; the difference is yours to keep.")));
                // 采购任务提示：超时报酬递减
                AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_supply_timeout_decay", "The reward decays 5% per day after the deadline — finish quickly.")));
            }
        }

        private void OnStartUndergroundFight()
        {
            // 地下格斗任务引导日志
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_underground_win", "Head to the arena, compete, and win.")));
            // 地下格斗任务提示：押注与练级
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_underground_bet_hint", "Hint: you can bet on yourself (betting opens soon); level up beforehand to improve your odds.")));
        }

        private void OnStartLegendaryHunt()
        {
            if (_data.TargetHero == null) return;
            SpawnBountyTargetParty();
            // 传奇悬赏任务公告日志
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_legendary_announce", "⚔ Legendary bounty: {TARGET} — the bandit king who has plagued the land!", ("TARGET", _data.TargetHero.Name.ToString()))));
            // 传奇悬赏任务奖励提示
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_legendary_reward_hint", "Defeating {TARGET} grants his unique equipment.", ("TARGET", _data.TargetHero.Name.ToString()))));
            // 传奇悬赏任务战前准备提示
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_legendary_prepare_hint", "Hint: prepare thoroughly before battle — bring enough troops, medical supplies, and counter-gear.")));
        }

        private void OnStartVillageDefense()
        {
            var village = !string.IsNullOrEmpty(_data.TargetSettlementId)
                ? Settlement.Find(_data.TargetSettlementId) : null;
            if (village == null) return;

            // ── 优先复用 WorldEvent 已有的 instigator 部队（加害方正带兵前来）──
            if (!string.IsNullOrEmpty(_data.WorldEventId))
            {
                var worldEvent = WorldEventStore.FindEvent(_data.WorldEventId);
                if (worldEvent != null && !string.IsNullOrEmpty(worldEvent.GeneratedPartyId))
                {
                    var existingParty = worldEvent.GeneratedParty;
                    if (existingParty != null && existingParty.IsActive)
                    {
                        _escortPartyId = existingParty.StringId;
                        // 加害方名称兜底文本
                        string instigatorName = worldEvent.InstigatorHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_quest_commission_enemy_fallback", "enemies");
                        // 村庄即将遭到劫掠的警告日志（WorldEvent 复用分支）
                        AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_village_threat_worldevent", "⚠ {LOCATION} is about to be raided by {ENEMY}!", ("LOCATION", village.Name.ToString()), ("ENEMY", instigatorName))));
                        // 村庄防守三种应对方式的日志
                        AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_village_choices_worldevent", "You can choose: attack to intercept / wait in the village to meet them / pay them to leave.")));
                        // 村庄防守提示：工程与领导力技能
                        AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_village_engineering_hint", "Hint: Engineering lets you build barricades to reduce enemy numbers; Leadership lets you rally militia for allies.")));
                        return;
                    }
                }
            }

            // 兜底：spawn 新的劫掠部队
            SpawnRaiderParty(village);
            // 村庄即将遭到劫掠的警告日志
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_village_threat", "⚠ {LOCATION} is about to be raided!", ("LOCATION", village.Name.ToString()))));
            // 村庄防守三种应对方式的日志
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_village_choices", "You can choose: attack the bandits to intercept / wait in the village to meet them / pay the bandits to leave.")));
            // 村庄防守提示：工程与领导力技能
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_village_engineering_hint", "Hint: Engineering lets you build barricades to reduce enemy numbers; Leadership lets you rally militia for allies.")));
        }

        private void OnStartLostItem()
        {
            if (string.IsNullOrEmpty(_data.TargetSettlementId)) return;
            var targetSettlement = Settlement.Find(_data.TargetSettlementId);
            string settleName = targetSettlement != null ? targetSettlement.Name.ToString() : _data.TargetSettlementId;
            // 失物任务线索指向日志
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_lost_item_clue", "The clues point to {LOCATION}.", ("LOCATION", settleName))));
            // 失物任务搜索引导日志
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_lost_item_search_hint", "Go there and use the Scout skill to search for clues. Once you find the item, decide how to handle it.")));
            float scoutSkill = Hero.MainHero.GetSkillValue(DefaultSkills.Scouting);
            if (scoutSkill > 80)
                // 高 Scout 技能玩家的直觉提示
                AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_lost_item_scout_intuition", "Your Scout skill is high — instinct tells you the thief may be hiding in a local gang hideout.")));
        }

        private void OnStartPrisonBreak()
        {
            if (_data.TargetHero == null) return;

            // 查找目标被关在哪里
            Settlement prisonSettlement = null;
            if (_data.TargetHero.IsPrisoner)
            {
                foreach (var s in Settlement.All)
                {
                    if (s.Party?.PrisonRoster?.GetTroopRoster()?.Any(e => e.Character?.HeroObject == _data.TargetHero) == true)
                    {
                        prisonSettlement = s;
                        break;
                    }
                }
            }

            if (prisonSettlement == null)
            {
                // 退而求其次：用 CurrentSettlement
                prisonSettlement = _data.TargetHero.CurrentSettlement;
            }

            if (prisonSettlement == null)
            {
                // 目标下落不明的引导日志
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_prison_target_unknown", "The whereabouts of {TARGET} are unknown. Go ask around at the tavern first.", ("TARGET", _data.TargetHero.Name.ToString()))));
                return;
            }

            _data.TargetSettlementId = prisonSettlement.StringId;

            // 目标被关押地点的日志
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_prison_located", "🔓 {TARGET} is locked in the prison of {LOCATION}.", ("TARGET", _data.TargetHero.Name.ToString()), ("LOCATION", prisonSettlement.Name.ToString()))));
            // 进入城镇后的引导日志
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_prison_go_town", "Go to {LOCATION}; once inside the town:", ("LOCATION", prisonSettlement.Name.ToString()))));
            // 越狱方案A：贿赂守卫
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_prison_plan_a", "  Plan A: bribe the guards (costs a few hundred denars) → enter the dungeon openly → take the prisoner")));
            // 越狱方案B：潜入地牢
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_prison_plan_b", "  Plan B: sneak into the dungeon (Roguery check) → high risk, saves money")));
            // 越狱方案C：外交施压
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_prison_plan_c", "  Plan C: diplomatic pressure (you must be a lord) → peaceful release")));
            // 越狱界面入口提示
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_prison_ui_hint", "Hint: once you reach {LOCATION}, look for the \"Sneak into the Prison\" or \"Enter the Dungeon\" options in the town screen.", ("LOCATION", prisonSettlement.Name.ToString()))));
        }

        private void OnStartSupplyIntercept()
        {
            var targetSettlement = !string.IsNullOrEmpty(_data.TargetSettlementId)
                ? Settlement.Find(_data.TargetSettlementId) : null;

            // ── 优先复用 WorldEvent 已 spawn 的辅助部队（世界不等玩家）──
            if (!string.IsNullOrEmpty(_data.WorldEventId))
            {
                var worldEvent = WorldEventStore.FindEvent(_data.WorldEventId);
                if (worldEvent != null)
                {
                    var existingParty = worldEvent.GetAuxiliaryParty("SupplyConvoy");
                    if (existingParty != null && existingParty.IsActive)
                    {
                        _escortPartyId = existingParty.StringId;
                        // 将辅助部队解锁 AI 使其向目标移动（事件创建时是巡逻态）
                        existingParty.Ai.SetDoNotMakeNewDecisions(false);
                        if (targetSettlement != null)
                            V.SetMoveToTown(existingParty,targetSettlement);
                        // 补给队目的地的兜底文本
                        string convoyDest = targetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_quest_commission_destination_fallback", "its destination");
                        // 敌方补给队已在途中的拦截日志（WorldEvent 复用分支）
                        AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_intercept_en_route", "The enemy supply convoy is already en route to {LOCATION}. Intercept it before it arrives!", ("LOCATION", convoyDest))));
                        // 补给拦截提示：Scout 侦察伏击点
                        AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_intercept_scout_hint", "Hint: the Scout skill reveals the convoy's position early, helping you find the best ambush point.")));
                        // 截获物资后的处置选择提示
                        AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_intercept_loot_choice", "After seizing the supplies you can choose: hand them to the giver (reward) / keep them for yourself (may be worth more).")));
                        return;
                    }
                }
            }

            // 兜底：WorldEvent 辅助部队已被消灭或不存在 → spawn 替代
            SpawnSupplyParty(targetSettlement);
            if (targetSettlement != null)
                // 敌方补给队正在前往目标的拦截日志
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_intercept_heading", "The enemy supply convoy is heading for {LOCATION}. Intercept it before it arrives!", ("LOCATION", targetSettlement.Name.ToString()))));
            // 补给拦截提示：Scout 侦察伏击点
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_intercept_scout_hint", "Hint: the Scout skill reveals the convoy's position early, helping you find the best ambush point.")));
            // 截获物资后的处置选择提示
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_intercept_loot_choice", "After seizing the supplies you can choose: hand them to the giver (reward) / keep them for yourself (may be worth more).")));
        }

        private void OnStartHideoutClear()
        {
            var targetSettlement = !string.IsNullOrEmpty(_data.TargetSettlementId)
                ? Settlement.Find(_data.TargetSettlementId) : null;
            string settleName = targetSettlement != null ? targetSettlement.Name.ToString() : _data.TargetSettlementId;

            // 匪穴清剿任务目标日志
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_hideout_target", "Objective: clear the bandit hideout near {LOCATION}.", ("LOCATION", settleName))));
            // 匪穴清剿任务完成条件日志
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_hideout_clear_all", "Go there and eliminate all the bandits to complete the commission.")));
            // 匪穴清剿进攻时机选择日志
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_hideout_approach", "Attack by day — all enemies present but good visibility; sneak in at night — fewer enemies but darkness. Which way do you choose?")));
            // 匪穴清剿侦察提示
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_hideout_recon_hint", "Hint: send a scout ahead (Scout) to learn the enemy count inside and tailor your forces.")));
        }

        private void OnStartEmergencyDelivery()
        {
            if (string.IsNullOrEmpty(_data.TargetItemId)) return;

            // 给玩家起始物资
            var item = MBObjectManager.Instance.GetObject<ItemObject>(_data.TargetItemId);
            if (item != null && MobileParty.MainParty != null)
            {
                AgentControlHelper.TransferItems(null, Hero.MainHero, item, _data.TargetItemCount);
                // 收到起始物资的日志：物品名+数量
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_delivery_goods_received", "Supplies received: {ITEM} ×{COUNT}.", ("ITEM", item.Name.ToString()), ("COUNT", _data.TargetItemCount.ToString()))));
            }

            var targetSettlement = !string.IsNullOrEmpty(_data.TargetSettlementId)
                ? Settlement.Find(_data.TargetSettlementId) : null;
            // 限时送达目的地的日志
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_delivery_time_limit", "Deliver within the time limit to {LOCATION}!", ("LOCATION", targetSettlement?.Name?.ToString() ?? _data.TargetSettlementId))));
            // 送货任务载重与分批次运输提示
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_delivery_carry_hint", "Hint: carrying capacity affects march speed — the more you carry, the more you earn, but the reward decays after the deadline. You can also transport in batches.")));
        }

        private void OnStartTreasureHunt()
        {
            if (string.IsNullOrEmpty(_data.TargetSettlementId)) return;
            var targetSettlement = Settlement.Find(_data.TargetSettlementId);

            // 藏宝图指向位置的日志
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_treasure_map", "The treasure map points near {LOCATION}.", ("LOCATION", targetSettlement?.Name?.ToString() ?? _data.TargetSettlementId))));
            // 宝藏搜索方式与守护者提示
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_treasure_search_hint", "Use the Scout skill to search for the exact spot after arriving. There may be guardians near the treasure — be prepared.")));
            // 宝藏任务雇向导或卖图的提示
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_treasure_guide_hint", "Hint: you can also hire a local guide with gold to narrow the search, or sell the treasure map to someone else for cash.")));
        }

        private void OnStartHorseAcquisition()
        {
            if (!string.IsNullOrEmpty(_data.TargetItemId))
            {
                var item = MBObjectManager.Instance.GetObject<ItemObject>(_data.TargetItemId);
                string itemName = item != null ? item.Name.ToString() : _data.TargetItemId;
                // 委托人求购马匹的日志
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_horse_wanted", "The giver wants a {ITEM}.", ("ITEM", itemName))));
                // 马匹市场比价提示
                AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_horse_market_hint", "Horse market prices differ between towns — compare prices across several towns. The Trade skill helps you haggle.")));
                // 市场上无马时的 NPC 获取提示
                AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_horse_npc_hint", "Hint: if it is not on the market, check whether any NPC owns this horse — negotiate a purchase or simply take it by force (Roguery).")));
            }
        }

        private void OnStartArenaSpecial()
        {
            _totalProgress = 2; // 需要连赢两场

            var targetSettlement = !string.IsNullOrEmpty(_data.TargetSettlementId)
                ? Settlement.Find(_data.TargetSettlementId) : null;
            // 竞技场名称兜底文本
            string settleName = targetSettlement != null ? targetSettlement.Name.ToString() : LWNTextHelper.ResolveText("LWN_quest_commission_arena_fallback", "any arena");

            // 特殊竞技场任务引导日志
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_arena_go_win", "Go to the arena of {LOCATION} and win two matches in a row.", ("LOCATION", settleName))));
            // 特殊竞技场特别规则日志
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_arena_special_rule", "Special rule: shields banned — pure weapon duels. You can bet on yourself; win and the payout doubles!")));
            // 特殊竞技场赛前准备提示
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_arena_train_hint", "Hint: level up skills before the match to improve your odds. You can also hire a pro to fight for you (pay gold).")));
        }

        private void OnStartDecoyMission()
        {
            DebugLogger.Log($"[CommissionQuest] OnStartDecoyMission: spawning pursuer party, worldEventId={_data?.WorldEventId} targetSettlementId={_data?.TargetSettlementId} timeRemain={_data?.TimeRemainingHours}h");
            // 生成一个追击玩家的强敌部队
            SpawnPursuerParty();

            // 诱敌任务开局日志：追兵已咬上
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_decoy_pursued", "The pursuers are on your tail! Keep them busy with a small force while the giver escapes.")));
            // 诱敌任务坚持时间与报酬的关系日志
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_decoy_longer_better", "The longer you hold out — the farther the giver gets — the higher the reward. If they catch you, you must fight.")));
            // 诱敌任务战术提示
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_decoy_tactic_hint", "Hint: you can lure the pursuers deep — drag them near friendly troops; or hire mercenaries with gold to hold them off.")));
            // 诱敌任务边打边跑的建议
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_decoy_run_advice", "Do not fight head-on — hit and run is the best strategy.")));
        }

        private void OnStartProcurementAgent()
        {
            if (!string.IsNullOrEmpty(_data.TargetItemId))
            {
                var item = MBObjectManager.Instance.GetObject<ItemObject>(_data.TargetItemId);
                string itemName = item != null ? item.Name.ToString() : _data.TargetItemId;

                // 采购代理任务：需求物品与预算日志
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_procurement_needed", "The giver needs a {ITEM} and gives you a budget of {BUDGET} denars.", ("ITEM", itemName), ("BUDGET", _data.NegotiatedReward.ToString()))));
                // 采购代理任务比价提示
                AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_procurement_compare_hint", "Search and compare prices across towns — the less you spend, the more of the budget is yours.")));
                // 采购代理任务交易技能提示
                AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_procurement_trade_hint", "Hint: the Trade skill affects haggling. If it is not on the market, find an NPC who owns this equipment and negotiate a purchase.")));
            }
        }

        #endregion

        #region Party Spawning (BountyHunt + CaravanEscort)

        private void SpawnBountyTargetParty()
        {
            if (_data.TargetHero == null) return;

            try
            {
                if (_data.TargetHero.Clan == null)
                    _data.TargetHero.Clan = Clan.BanditFactions.FirstOrDefault() ?? Clan.PlayerClan;

                var partyComponent = new SafeLordPartyComponent(_data.TargetHero);
                var partyId = $"commission_bounty_{_data.TargetHero.StringId}_{MBRandom.RandomInt(1000)}";
                MobileParty targetParty = V.MakeParty(partyId, partyComponent);
                if (targetParty != null)
                    // 悬赏目标匪帮的部队名称
                    V.SetPartyName(targetParty, new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_party_bounty_name", "{NAME}'s gang", ("NAME", _data.TargetHero.Name.ToString()))));

                if (targetParty == null) return;

                _escortPartyId = partyId;

                // 设为敌对匪帮
                var banditClan = Clan.BanditFactions.FirstOrDefault(c => c.StringId == "looters");
                if (banditClan != null)
                    targetParty.ActualClan = banditClan;

                // 放置在大地图玩家附近
                Vec2 offset = new Vec2(3f + MBRandom.RandomFloat * 5f, 3f + MBRandom.RandomFloat * 5f);
                V.SetPos(targetParty, V.Pos(MobileParty.MainParty) + offset);

                // 按难度填充兵力
                int troopCount = _data.Tier switch
                {
                    CommissionTier.Basic => 3 + MBRandom.RandomInt(4),
                    CommissionTier.Skilled => 8 + MBRandom.RandomInt(6),
                    CommissionTier.Expert => 15 + MBRandom.RandomInt(10),
                    CommissionTier.Legendary => 30 + MBRandom.RandomInt(20),
                    _ => 5
                };

                PartyTemplateObject template = _data.TargetHero.Culture?.DefaultPartyTemplate;
                if (template != null)
                    V.InitPartyPos(targetParty, template, V.Pos(targetParty));

                targetParty.MemberRoster.Clear();
                targetParty.PrisonRoster.Clear();
                targetParty.MemberRoster.AddToCounts(_data.TargetHero.CharacterObject, 1);

                var basicTroop = _data.TargetHero.Culture?.BasicTroop;
                if (basicTroop != null)
                    targetParty.MemberRoster.AddToCounts(basicTroop, troopCount);

                // AI：在大地图巡逻
                V.SetMovePatrol(targetParty,V.Pos(targetParty));
                targetParty.SetPartyUsedByQuest(true);
                targetParty.Party.SetVisualAsDirty();

                // 悬赏目标部队出现在地图上的追踪日志
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_bounty_party_spawned_map", "The troops of {TARGET} have appeared on the map. Start tracking them down.", ("TARGET", _data.TargetHero.Name.ToString()))));
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"Failed to spawn bounty target party: {ex.Message}");
            }
        }

        private void SpawnEscortCaravanParty()
        {
            try
            {
                string partyId = $"commission_escort_{QuestGiver.StringId}_{MBRandom.RandomInt(1000)}";
                var escortComponent = new SafeLordPartyComponent(QuestGiver);
                MobileParty escortParty = V.MakeParty(partyId, escortComponent);
                if (escortParty != null)
                    // 护送商队的部队名称
                    V.SetPartyName(escortParty, new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_party_caravan_name", "{NAME}'s caravan", ("NAME", QuestGiver.Name.ToString()))));

                if (escortParty == null) return;

                _escortPartyId = partyId;

                Vec2 offset = new Vec2(1f, 1f);
                V.SetPos(escortParty, V.Pos(MobileParty.MainParty) + offset);

                // 少量护卫
                var culture = QuestGiver.Culture ?? Hero.MainHero.Culture;
                PartyTemplateObject template = culture?.DefaultPartyTemplate;
                if (template != null)
                    V.InitPartyPos(escortParty, template, V.Pos(escortParty));
                escortParty.MemberRoster.Clear();
                var basicTroop = culture?.BasicTroop;
                if (basicTroop != null)
                    escortParty.MemberRoster.AddToCounts(basicTroop, 8);

                // AI：跟随玩家
                V.SetMoveEngage(escortParty,MobileParty.MainParty);
                escortParty.SetPartyUsedByQuest(true);
                escortParty.Party.SetVisualAsDirty();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"Failed to spawn escort party: {ex.Message}");
            }
        }

        private void SpawnRaiderParty(Settlement targetVillage)
        {
            if (targetVillage == null) return;
            try
            {
                string partyId = $"commission_raiders_{targetVillage.StringId}_{MBRandom.RandomInt(1000)}";
                var banditClan = Clan.BanditFactions.FirstOrDefault(c => c.StringId == "looters")
                    ?? Clan.BanditFactions.FirstOrDefault();
                if (banditClan == null) return;

                // 使用自定义 PartyComponent（泛型匪帮，无 Hero leader）
                // 劫掠村庄的匪帮部队名称
                string raiderName = LWNTextHelper.ResolveCompound("LWN_quest_commission_party_raider_name", "Raiders pillaging {VILLAGE}", ("VILLAGE", targetVillage.Name.ToString()));
                var component = new CustomPartyComponent(targetVillage, raiderName);
                MobileParty raiderParty = V.MakeParty(partyId, component);
                if (raiderParty != null)
                    V.SetPartyName(raiderParty, new TextObject(raiderName));

                if (raiderParty == null) return;

                _escortPartyId = partyId;
                raiderParty.ActualClan = banditClan;

                Vec2 spawnPos = V.Pos(targetVillage);
                Vec2 offset = new Vec2(MBRandom.RandomFloat * 10f - 5f, MBRandom.RandomFloat * 10f - 5f);
                V.SetPos(raiderParty, spawnPos + offset);

                int troopCount = _data.Tier switch
                {
                    CommissionTier.Basic => 5 + MBRandom.RandomInt(5),
                    CommissionTier.Skilled => 12 + MBRandom.RandomInt(8),
                    CommissionTier.Expert => 20 + MBRandom.RandomInt(10),
                    _ => 6
                };

                var template = banditClan.DefaultPartyTemplate;
                if (template != null)
                    V.InitPartyPos(raiderParty, template, V.Pos(raiderParty));
                raiderParty.MemberRoster.Clear();
                var banditTroop = banditClan.Culture?.BasicTroop;
                if (banditTroop != null)
                    raiderParty.MemberRoster.AddToCounts(banditTroop, troopCount);

                V.SetMoveToTown(raiderParty,targetVillage);
                raiderParty.Ai.SetDoNotMakeNewDecisions(true);
                raiderParty.SetPartyUsedByQuest(true);
                raiderParty.Party.SetVisualAsDirty();

                // 劫掠部队出现在村庄附近的日志
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_raider_party_spawned", "The raiding party has appeared near {LOCATION}!", ("LOCATION", targetVillage.Name.ToString()))));
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"Failed to spawn raider party: {ex.Message}");
            }
        }

        private void SpawnSupplyParty(Settlement targetSettlement)
        {
            try
            {
                string partyId = $"commission_supply_{MBRandom.RandomInt(1000)}";
                var enemyFaction = Hero.MainHero.MapFaction;
                var enemyClan = Clan.All.FirstOrDefault(c => c.MapFaction != enemyFaction && !c.IsBanditFaction)
                    ?? Clan.BanditFactions.FirstOrDefault();
                if (enemyClan == null) enemyClan = Clan.PlayerClan;

                // 敌方补给队的部队名称
                var component = new CustomPartyComponent(targetSettlement, LWNTextHelper.ResolveText("LWN_quest_commission_party_supply_name", "Enemy Supply Convoy"));
                MobileParty supplyParty = V.MakeParty(partyId, component);
                if (supplyParty != null)
                    // 敌方补给队的部队名称（设置部队显示名）
                    V.SetPartyName(supplyParty, new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_party_supply_name", "Enemy Supply Convoy")));

                if (supplyParty == null) return;

                _escortPartyId = partyId;
                supplyParty.ActualClan = enemyClan;

                Vec2 offset = new Vec2(8f + MBRandom.RandomFloat * 10f, 8f + MBRandom.RandomFloat * 10f);
                V.SetPos(supplyParty, V.Pos(MobileParty.MainParty) + offset);

                var template = enemyClan.DefaultPartyTemplate;
                if (template != null)
                    V.InitPartyPos(supplyParty, template, V.Pos(supplyParty));
                supplyParty.MemberRoster.Clear();
                var troop = enemyClan.Culture?.BasicTroop;
                if (troop != null)
                    supplyParty.MemberRoster.AddToCounts(troop, 4 + MBRandom.RandomInt(4));

                if (targetSettlement != null)
                    V.SetMoveToTown(supplyParty,targetSettlement);
                else
                    V.SetMovePatrol(supplyParty,V.Pos(supplyParty));

                supplyParty.Ai.SetDoNotMakeNewDecisions(true);
                supplyParty.SetPartyUsedByQuest(true);
                supplyParty.Party.SetVisualAsDirty();

                // 敌方补给队已出现在地图上的拦截日志
                AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_supply_party_spawned", "The enemy supply convoy has appeared on the map. Intercept it before it reaches its destination!")));
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"Failed to spawn supply party: {ex.Message}");
            }
        }

        private void SpawnPursuerParty()
        {
            try
            {
                string partyId = $"commission_pursuer_{MBRandom.RandomInt(1000)}";
                var banditClan = Clan.BanditFactions.FirstOrDefault(c => c.StringId == "looters")
                    ?? Clan.BanditFactions.FirstOrDefault();
                if (banditClan == null)
                {
                    DebugLogger.Log("[CommissionQuest] SpawnPursuerParty FAILED: no bandit clan found");
                    return;
                }

                Settlement home = Settlement.Find(_data?.TargetSettlementId)
                    ?? MobileParty.MainParty?.CurrentSettlement
                    ?? Settlement.All.FirstOrDefault();
                // 追兵部队的名称
                var component = new CustomPartyComponent(home, LWNTextHelper.ResolveText("LWN_quest_commission_party_pursuer_name", "Pursuers"));
                MobileParty pursuerParty = V.MakeParty(partyId, component);
                if (pursuerParty != null)
                    // 追兵部队的名称（设置部队显示名）
                    V.SetPartyName(pursuerParty, new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_party_pursuer_name", "Pursuers")));

                if (pursuerParty == null)
                {
                    DebugLogger.Log("[CommissionQuest] SpawnPursuerParty FAILED: MobileParty.CreateParty returned null");
                    return;
                }

                _escortPartyId = partyId;
                pursuerParty.ActualClan = banditClan;

                Vec2 offset = new Vec2(5f, 5f);
                V.SetPos(pursuerParty, V.Pos(MobileParty.MainParty) + offset);

                int troopCount = _data.Tier switch
                {
                    CommissionTier.Basic => 8,
                    CommissionTier.Skilled => 15,
                    CommissionTier.Expert => 25,
                    _ => 10
                };

                var template = banditClan.DefaultPartyTemplate;
                if (template != null)
                    V.InitPartyPos(pursuerParty, template, V.Pos(pursuerParty));
                pursuerParty.MemberRoster.Clear();
                var troop = banditClan.Culture?.BasicTroop;
                if (troop != null)
                    pursuerParty.MemberRoster.AddToCounts(troop, troopCount);

                // 追兵追击玩家！
                V.SetMoveEngage(pursuerParty,MobileParty.MainParty);
                pursuerParty.Ai.SetDoNotMakeNewDecisions(true);
                pursuerParty.SetPartyUsedByQuest(true);
                pursuerParty.Party.SetVisualAsDirty();

                DebugLogger.Log($"[CommissionQuest] SpawnPursuerParty OK: partyId={partyId} troopCount={troopCount} pos=({V.Pos(pursuerParty).X:F1},{V.Pos(pursuerParty).Y:F1}) clan={banditClan.StringId}");

                // 追兵出现在地图上的警告日志：需坚持的天数
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_pursuer_spawned", "⚠ Pursuers have appeared on the map and are chasing you! Hold out for {DAYS} days.", ("DAYS", ((int)(_data.TimeRemainingHours / 24f) + 1).ToString()))));
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"Failed to spawn pursuer party: {ex.Message}");
            }
        }

        private void CleanupSpawnedParty()
        {
            if (string.IsNullOrEmpty(_escortPartyId)) return;

            // 如果关联了 WorldEvent，不清理事件 party（事件仍需 AI 或其他委托解决）
            if (!string.IsNullOrEmpty(_data?.WorldEventId))
            {
                DebugLogger.Log($"[CommissionQuest] CleanupSpawnedParty SKIP: WorldEvent-linked, eventId={_data.WorldEventId} partyId={_escortPartyId} category={_data?.Category}");
                return;
            }

            DebugLogger.Log($"[CommissionQuest] CleanupSpawnedParty REMOVE: partyId={_escortPartyId} category={_data?.Category}");
            try
            {
                MobileParty party = null;
                foreach (var mp in Campaign.Current.MobileParties)
                {
                    if (mp.StringId == _escortPartyId)
                    {
                        party = mp;
                        break;
                    }
                }
                if (party != null)
                {
                    V.DelParty(party);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"Failed to cleanup party {_escortPartyId}: {ex.Message}");
            }
        }

        /// <summary>主动失败委托（非超时），清理并触发失败后果。</summary>
        private void FailQuest()
        {
            WorldEventStore.OnEventStageChanged -= OnWorldEventStageChangedForQuest;
            DebugLogger.Log($"[CommissionQuest] FailQuest called: category={_data?.Category} giver={QuestGiver?.Name} worldEventId={_data?.WorldEventId} progress={_currentProgress}/{_totalProgress} timeRemain={_data?.TimeRemainingHours}h");
            CleanupSpawnedParty();
            _finalGrade = CommissionGrade.Failed;
            // 委托人名称兜底文本
            string giverNameFallback = LWNTextHelper.ResolveText("LWN_quest_commission_giver_fallback", "the giver");
            // 委托被迫终止的日志：与委托人关系下降
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_forced_failure", "The commission was forcibly ended. Your relationship with {GIVER} has suffered.", ("GIVER", QuestGiver?.Name?.ToString() ?? giverNameFallback))));
            CompleteQuestWithFail(); // 触发 OnFailed() 统一处理惩罚
            DebugLogger.Log($"[CommissionQuest] FailQuest finished: category={_data?.Category}");
        }

        /// <summary>
        /// 由外部系统（如 AcceptBountyQuestIntent）调用，完成调查委托并关闭。
        /// 不走完整的"领报酬"流程——调查 Quest 被悬赏 Quest 替代时使用。
        /// </summary>
        public void CompleteObjectivesFromExternal()
        {
            if (_data == null) return;
            _data.IsObjectivesComplete = true; // 跳过 OnCompleteWithSuccess 的旧版报酬逻辑
            // 调查完成转入悬赏缉拿阶段的日志
            AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_investigation_bounty_stage", "Investigation complete: the suspect has been identified. Moving to the bounty hunt phase.")));
            CompleteQuestWithSuccess();
            DebugLogger.Log($"[CommissionQuest] CompleteObjectivesFromExternal: {StringId} category={_data.Category}");
        }

        /// <summary>
        /// 由 CommissionIssueBehavior 在阶段变更时调用：完成旧的调查 Quest。
        /// 在新 Issue/Quest 创建前必须释放 NPC 的委托槽位（MaxCommissionsPerNpc=1），
        /// 否则 HasCommissionsFor 会因旧 Quest 仍在进行中而拒绝创建新 Issue。
        /// suspectIsPlayer=true → 背叛结局（贼喊捉贼）；false → 正常成功结案。
        /// </summary>
        internal void CompleteInvestigationExternally(bool suspectIsPlayer)
        {
            if (_data == null || !IsOngoing) return;
            _suspectIdentifiedLogged = true;
            _data.IsObjectivesComplete = true;

            if (suspectIsPlayer)
            {
                // 嫌犯是玩家本人的背叛日志
                AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_investigation_self", "The investigation points to myself. The giver will never trust me again.")));
                // 背叛结局的任务名：贼喊捉贼
                CompleteQuestWithBetrayal(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_betrayal_trust", "Betrayed the giver's trust — crying thief while being one.")));
                DebugLogger.Log($"[CommissionQuest] CompleteInvestigationExternally: {StringId} betrayed (suspect=self)");
            }
            else
            {
                // 调查完成转入悬赏缉拿阶段的日志
                AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_investigation_bounty_stage", "Investigation complete: the suspect has been identified. Moving to the bounty hunt phase.")));
                CompleteQuestWithSuccess();
                DebugLogger.Log($"[CommissionQuest] CompleteInvestigationExternally: {StringId} completed (suspect identified)");
            }
        }

        /// <summary>
        /// 由 Intent（FrameSuspectIntent 等）调用：通知调查 Quest "嫌犯已锁定"。
        /// 嫌犯=玩家时只更新进度不进入 Phase 3（领取报酬），后续走对峙/betray 路线。
        /// 嫌犯≠玩家时正常进入 Phase 3。
        /// </summary>
        public void NotifySuspectIdentified(string suspectName)
        {
            if (_data == null || _suspectIdentifiedLogged) return;
            _suspectIdentifiedLogged = true;
            // 委托人名称兜底文本
            string giverName = QuestGiver?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_quest_commission_giver_fallback", "the giver");

            // 判断嫌犯是不是玩家自己
            bool suspectIsPlayer = suspectName == Hero.MainHero.Name?.ToString();
            if (!suspectIsPlayer && !string.IsNullOrEmpty(_data.WorldEventId))
            {
                var evt = WorldEventStore.Find(_data.WorldEventId);
                if (evt != null)
                    suspectIsPlayer = evt.SuspectHeroId == Hero.MainHero.StringId;
            }

            if (suspectIsPlayer)
            {
                // 嫌犯=玩家：进度更新但不领报酬，后续由对峙 Intent 处理
                _currentProgress = _totalProgress;
                if (_progressLog != null)
                    _progressLog.UpdateCurrentProgress(_currentProgress);
                _data.IsObjectivesComplete = true;
                // 嫌犯锁定的进展日志：向委托人汇报
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_suspect_identified", "The investigation progresses — the suspect is {SUSPECT}. Go report back to {GIVER}.", ("SUSPECT", suspectName), ("GIVER", giverName))));
                DebugLogger.Log($"[CommissionQuest] NotifySuspectIdentified: {StringId} suspect=self — progress={_currentProgress}/{_totalProgress}, Phase 3 skipped (confrontation path)");
            }
            else
            {
                // 嫌犯锁定的进展日志：向委托人汇报
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_suspect_identified", "The investigation progresses — the suspect is {SUSPECT}. Go report back to {GIVER}.", ("SUSPECT", suspectName), ("GIVER", giverName))));
                UpdateProgress(_totalProgress);
                DebugLogger.Log($"[CommissionQuest] NotifySuspectIdentified: {StringId} suspect={suspectName}");
            }
        }

        /// <summary>
        /// WorldEvent 阶段变化回调（仅 Investigation Quest 注册）。
        /// 处理 NPC 后台调查查出嫌犯的 case（非玩家 Intent 驱动），
        /// 也统一处理嫌犯=玩家时的背叛结局。
        /// </summary>
        private void OnWorldEventStageChangedForQuest(WorldEvent evt)
        {
            if (_data == null) return;
            if (evt.EventId != _data.WorldEventId) return;
            if (_data.Category != CommissionCategory.Investigation) return;
            if (!IsOngoing) return; // 已被别处结束

            bool suspectIsPlayer = evt.SuspectHeroId == Hero.MainHero.StringId;

            if (evt.Stage == EventStage.Active && !string.IsNullOrEmpty(evt.SuspectHeroId) && !_suspectIdentifiedLogged)
            {
                var suspect = Hero.FindFirst(h => h.StringId == evt.SuspectHeroId);
                // 嫌犯名称兜底文本：未知时称"某人"
                NotifySuspectIdentified(suspect?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_quest_commission_someone_fallback", "someone"));
                DebugLogger.Log($"[CommissionQuest] OnWorldEventStageChanged: {StringId} stage=Active (suspect identified)");

                // 嫌犯=玩家 → 调查任务直接背叛结局（WalkAway / 自首后跑路 / NPC查出玩家）
                if (suspectIsPlayer)
                {
                    // 嫌犯是玩家本人的背叛日志
                    AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_investigation_self", "The investigation points to myself. The giver will never trust me again.")));
                    // 背叛结局的任务名：贼喊捉贼
                    CompleteQuestWithBetrayal(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_betrayal_trust", "Betrayed the giver's trust — crying thief while being one.")));
                    DebugLogger.Log($"[CommissionQuest] OnWorldEventStageChanged: {StringId} stage=Active suspect=self → Betrayal");
                }
            }
            else if (evt.Stage == EventStage.Unsolved)
            {
                // 冷案：调查走入死胡同——不算玩家违约，取消收尾（无定金/信任惩罚）
                // 冷案日志：线索中断案件陷入僵局
                AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_cold_case", "The trail went cold and the case has stalled. The giver can only accept this outcome.")));
                CompleteQuestWithCancel();
                DebugLogger.Log($"[CommissionQuest] OnWorldEventStageChanged: {StringId} stage=Unsolved — cold case, quest cancelled without penalty");
            }
            else if (evt.Stage == EventStage.Resolved)
            {
                if (_data.IsObjectivesComplete && !suspectIsPlayer) return; // 非嫌犯且已完成 → 跳过

                _data.IsObjectivesComplete = true;

                if (suspectIsPlayer)
                {
                    // 嫌犯=玩家 → 背叛结局（赔钱了事 / 威胁成功 / 以工抵债完成）
                    // 案件结案但委托人已知玩家是贼的日志
                    AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_resolved_self", "The case is closed. Even though it was settled, the giver knows I was the thief.")));
                    // 背叛结局的任务名：罪行败露
                    CompleteQuestWithBetrayal(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_resolved_forced_duty", "The crime came to light and I was forced to answer for it.")));
                    DebugLogger.Log($"[CommissionQuest] OnWorldEventStageChanged: {StringId} stage=Resolved suspect=self → Betrayal");
                }
                else
                {
                    // 案件结案调查委托自动完成的日志
                    AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_resolved_auto_complete", "The case is closed — the investigation commission is automatically complete.")));
                    CompleteQuestWithSuccess();
                    DebugLogger.Log($"[CommissionQuest] OnWorldEventStageChanged: {StringId} stage=Resolved — investigation quest auto-completed");
                }
            }
        }

        #endregion

        #region Deposit Repayment

        private void ShowDepositRepaymentInquiry()
        {
            // 定金追讨弹窗正文：超时说明与三个选项的后果
            string body = LWNTextHelper.ResolveCompound("LWN_quest_commission_deposit_inquiry_body",
                "The commission failed due to timeout. {GIVER} demands the return of your {DEPOSIT} denar deposit.\n\n" +
                "• Return the deposit: -10 trust, you may take commissions again\n" +
                "• Charm check: return half (failure = consequences of refusing)\n" +
                "• Refuse: -40 trust + 1 infamy + relationship damage",
                ("GIVER", QuestGiver.Name.ToString()), ("DEPOSIT", _data.DepositAmount.ToString()));

            InformationManager.ShowInquiry(new InquiryData(
                // 定金追讨弹窗标题
                LWNTextHelper.ResolveText("LWN_quest_commission_deposit_inquiry_title", "Deposit Repayment"),
                body,
                true, true,
                // 退还定金按钮文案
                LWNTextHelper.ResolveText("LWN_quest_commission_deposit_return_button", "Return the deposit"),
                // 拒绝退还按钮文案
                LWNTextHelper.ResolveText("LWN_quest_commission_deposit_refuse_button", "Refuse to return"),
                () =>
                {
                    // 退还
                    AgentControlHelper.TransferGold(Hero.MainHero, QuestGiver, _data.DepositAmount);
                    _depositRepaid = true;
                    _data.DepositRepaid = true;
                    TrustSystem.AddTrust(QuestGiver, -10);
                    ChangeRelationAction.ApplyPlayerRelation(QuestGiver, -5);
                    // 已退还定金的日志
                    AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_deposit_returned", "Deposit of {GOLD} denars returned.", ("GOLD", _data.DepositAmount.ToString()))));
                },
                () =>
                {
                    // Charm 检定
                    float charmSkill = Hero.MainHero.GetSkillValue(DefaultSkills.Charm);
                    float chance = 0.3f + charmSkill / 300f * 0.4f;
                    if (MBRandom.RandomFloat < chance)
                    {
                        int halfDeposit = _data.DepositAmount / 2;
                        AgentControlHelper.TransferGold(Hero.MainHero, QuestGiver, halfDeposit);
                        _depositRepaid = true;
                        _data.DepositRepaid = true;
                        TrustSystem.AddTrust(QuestGiver, -15);
                        ChangeRelationAction.ApplyPlayerRelation(QuestGiver, -8);
                        // Charm 检定成功减半退还定金的日志
                        AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_deposit_charm_half", "Charm check succeeded! Half the deposit ({GOLD} denars) was returned.", ("GOLD", halfDeposit.ToString()))));
                    }
                    else
                    {
                        // 拒绝退还后果
                        _depositRepaid = false;
                        _data.DepositRepaid = false;
                        TrustSystem.AddTrust(QuestGiver, -40);
                        ChangeRelationAction.ApplyPlayerRelation(QuestGiver, -15);
                        InfamySystem.AddInfamy(1);
                        // 拒绝退还定金的后果日志
                        AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_deposit_refused", "You refused to return the deposit! Infamy +1, and your relationship with {GIVER} has seriously worsened.", ("GIVER", QuestGiver.Name.ToString()))));
                    }
                }));
        }

        #endregion

        #region Prisoner Escape

        private void TryPrisonerEscapeEvent()
        {
            float chance = 0.15f; // 每天 15% 概率劫囚
            if (MBRandom.RandomFloat < chance)
            {
                // 囚犯名称兜底文本
                string prisonerName = _data.TargetHero != null ? _data.TargetHero.Name.ToString() : LWNTextHelper.ResolveText("LWN_quest_commission_prisoner_fallback", "the prisoner");
                // 同伙试图劫囚的警报日志
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_prison_escape_alert", "⚠ Alert: allies of {NAME} are approaching, trying to free the prisoner!", ("NAME", prisonerName))));
                // 可以在此生成一个劫囚 MobileParty
            }
        }

        #endregion

        #region Progress & Helpers

        private void UpdateProgress(int newProgress)
        {
            _currentProgress = Math.Min(newProgress, _totalProgress);
            if (_progressLog != null)
                _progressLog.UpdateCurrentProgress(_currentProgress);
            else if (_totalProgress > 0)
            {
                _progressLog = AddDiscreteLog(
                    // 委托进度日志标题（引擎模板键：委托进度）
                    new TextObject("{=LWN_quest_commission_progress}Quest Progress"),
                    // 委托进度日志标题（引擎模板键：完成度）
                    new TextObject("{=LWN_quest_commission_progress_detail}Completion"),
                    _currentProgress, _totalProgress);
            }
            if (_currentProgress >= _totalProgress)
            {
                // 阶段2完成：目标进度拉满（离散日志自动显示完成）
                // 不立即完成 —— 标记为等待领取报酬
                _data.IsObjectivesComplete = true;
                CleanupSpawnedParty();

                // 结账人
                Hero payer = _data.RewardPayer ?? QuestGiver;
                // 结账人名称兜底文本
                string payerName = payer?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_quest_commission_giver_fallback", "the giver");
                // 结账人所在地兜底文本
                string payerLoc = payer?.CurrentSettlement?.Name?.ToString()
                    // 结账人所在地未知时的兜底文本
                    ?? payer?.HomeSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_quest_commission_unknown_location", "Unknown location");

                // 阶段3：找结账人领报酬
                _rewardLog = AddDiscreteLog(
                    // 阶段3日志标题：前往结账人所在地领取报酬
                    new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_step3_claim_reward", "Step 3: Go to {LOCATION} and find {GIVER} to claim the reward", ("LOCATION", payerLoc), ("GIVER", payerName))),
                    // 阶段3日志进度：领取报酬
                    new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_reward_claim", "Claim the reward")),
                    0, 1);

                // 委托目标已完成前往领报酬的日志
                AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_objectives_complete", "The commission objective is complete! Go to {LOCATION} and find {GIVER} to claim your reward.", ("LOCATION", payerLoc), ("GIVER", payerName))));
                DebugLogger.Log($"[CommissionQuest] Objectives complete: {_data.GetFlavorDescription()} payer={payerName} at {payerLoc}");
            }
        }

        /// <summary>
        /// 玩家找到结账人后调用 —— 真正完成委托，转账报酬。
        /// 包含原 OnCompleteWithSuccess 的全部结算逻辑。
        /// </summary>
        public void CompleteWithRewardCollection()
        {
            if (_data == null || !_data.IsObjectivesComplete) return;
            if (QuestGiver == null) return;

            ComputeFinalGrade();
            int reward = CalculateFinalReward();
            int trustDelta = GetGradeTrustDelta();

            Hero payer = _data.RewardPayer ?? QuestGiver;
            DebugLogger.Log($"[CommissionQuest] CompleteWithRewardCollection: {_data.GetFlavorDescription()} grade={_finalGrade} reward={reward} trustDelta={trustDelta} payer={payer?.Name}");

            // 阶段3完成：报酬已领取
            if (_rewardLog != null)
                _rewardLog.UpdateCurrentProgress(1);

            // 从结账人转账报酬（结账人可能就是委托人）
            AgentControlHelper.TransferGold(payer, Hero.MainHero, reward);
            ChangeRelationAction.ApplyPlayerRelation(QuestGiver, 5);
            GainRenownAction.Apply(Hero.MainHero, 2);
            int oldTrust = TrustSystem.GetTrust(QuestGiver);
            TrustSystem.AddTrust(QuestGiver, trustDelta);

            // 难度递进
            var oldTier = CommissionTierProgression.GetAvailableTier(_data.Category);
            CommissionTierProgression.RecordCompletion(_data.Category, _data.Tier, _finalGrade);
            var newTier = CommissionTierProgression.GetAvailableTier(_data.Category);

            // 叙事反馈
            string milestoneMsg = CommissionNarrative.CheckTrustMilestone(QuestGiver, oldTrust,
                TrustSystem.GetTrust(QuestGiver));
            if (!string.IsNullOrEmpty(milestoneMsg))
                AddLog(new TextObject(milestoneMsg));

            string tierMsg = CommissionNarrative.CheckTierUnlock(_data.Category, oldTier, newTier);
            if (!string.IsNullOrEmpty(tierMsg))
                AddLog(new TextObject(tierMsg));

            if (_data.Tier >= CommissionTier.Expert && InfamySystem.Infamy > 0)
            {
                InfamySystem.ReduceInfamy(1);
                // 完成高难度委托削减恶名的日志
                AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_hard_complete_infamy", "Completed a high-difficulty commission — infamy -1.")));
            }

            string gradeStr = GetGradeDisplayName();
            // 委托完成日志：评级+报酬已领取
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_complete_reward_claimed",
                "Commission complete! Grade: {GRADE} — the reward of {REWARD} denars has been claimed.",
                ("GRADE", gradeStr), ("REWARD", reward.ToString()))));
            // 委托完成日志：与委托人的信任度变化
            AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_quest_commission_trust_changed",
                "Your standing with {GIVER} changed by {TRUSTDELTA} (currently: {TRUSTDESC})",
                ("GIVER", QuestGiver.Name.ToString()),
                ("TRUSTDELTA", (trustDelta >= 0 ? "+" : "") + trustDelta),
                ("TRUSTDESC", TrustSystem.GetTrustDescription(TrustSystem.GetTrust(QuestGiver))))));

            // 关联了 WorldEvent → 结算事件
            if (!string.IsNullOrEmpty(_data.WorldEventId))
            {
                WorldEventStore.ResolveEvent(_data.WorldEventId);

                // 检查卧底叛变条件（玩家帮 instigator 解决了事件 → 可以策反）
                var worldEvent = WorldEventStore.FindEvent(_data.WorldEventId);
                if (worldEvent != null && !string.IsNullOrEmpty(worldEvent.InitiatorId))
                {
                    var instigator = Hero.FindFirst(h => h.StringId == worldEvent.InitiatorId);
                    if (instigator != null)
                        StrategicInfiltration.CheckAvailability(instigator, _data.WorldEventId);
                }

                // 尝试发现阴谋线索
                if (ConspiracyManager.TryDiscoverClue(_data.WorldEventId, out string clueMsg))
                {
                    AddLog(new TextObject(clueMsg));
                }

                // 检查是否应解锁幕后黑手对决
                if (worldEvent != null && !string.IsNullOrEmpty(worldEvent.ConspiracyId)
                    && ConspiracyManager.CheckUnlockConfrontation(worldEvent.ConspiracyId, out var mastermind, out var hint))
                {
                    NinjaNotificationManager.Show(hint, () => { });
                    AddLog(new TextObject($"🔍 {hint}"));
                }

                // 关联世界事件已解决的日志
                AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_related_event_resolved", "The linked world event has been resolved.")));
                DebugLogger.Log($"[CommissionQuest] Resolved WorldEvent: {_data.WorldEventId}");
            }

            CompleteQuestWithSuccess();
        }

        private string GetExtraInfo()
        {
            switch (_data.Category)
            {
                case CommissionCategory.Investigation:
                    // 调查委托的附加说明：搜索或询问推进调查
                    return LWNTextHelper.ResolveText("LWN_quest_commission_extra_investigation", "Search near the settlement or talk to the villagers to advance the investigation.");
                case CommissionCategory.BountyHunt:
                    // 悬赏委托附加信息：活捉奖励
                    return _data.TargetHero != null
                        // 悬赏附加信息：含目标名
                        ? LWNTextHelper.ResolveCompound("LWN_quest_commission_extra_bounty", "Target: {TARGET} — capture alive for ×2.25 reward", ("TARGET", _data.TargetHero.Name.ToString()))
                        : "";
                case CommissionCategory.CaravanEscort:
                    // 护送委托附加说明：盗贼伏击风险
                    return LWNTextHelper.ResolveText("LWN_quest_commission_extra_caravan", "Note: bandit ambushes may occur en route; the Scout skill lets you spot them in advance.");
                case CommissionCategory.SupplyEmergency:
                    // 供应委托附加说明：超时报酬递减
                    return LWNTextHelper.ResolveText("LWN_quest_commission_extra_supply", "The reward decays 5% per day after the deadline.");
                case CommissionCategory.UndergroundFight:
                    // 地下格斗委托附加说明：练级提升胜率
                    return LWNTextHelper.ResolveText("LWN_quest_commission_extra_fight", "Level up skills before the match to improve your odds.");
                default: return "";
            }
        }

        /// <summary>阶段2目标的简短描述（用于 quest 日志步骤标题）。</summary>
        private string GetObjectiveStepText()
        {
            // 阶段2目标/物资名称的兜底文本
            string targetFallback = LWNTextHelper.ResolveText("LWN_quest_commission_target_fallback", "the target");
            // 阶段2目标地点名称的兜底文本
            string targetLocFallback = LWNTextHelper.ResolveText("LWN_quest_commission_target_location_fallback", "the target location");
            // 阶段2物资名称的兜底文本
            string goodsFallback = LWNTextHelper.ResolveText("LWN_quest_commission_goods_fallback", "supplies");
            string target = _data.TargetHero?.Name?.ToString()
                ?? (!string.IsNullOrEmpty(_data.TargetSettlementId)
                    ? Settlement.Find(_data.TargetSettlementId)?.Name?.ToString() ?? targetLocFallback
                    : targetFallback);
            string item = !string.IsNullOrEmpty(_data.TargetItemId)
                ? MBObjectManager.Instance.GetObject<ItemObject>(_data.TargetItemId)?.Name?.ToString() ?? goodsFallback
                : goodsFallback;

            switch (_data.Category)
            {
                case CommissionCategory.Investigation:
                    // 阶段2目标：搜集线索找出真凶
                    return LWNTextHelper.ResolveText("LWN_quest_commission_step2_investigation", "Step 2: Gather clues and find the culprit");
                case CommissionCategory.BountyHunt:
                    // 阶段2目标：击败（最好活捉）目标
                    return LWNTextHelper.ResolveCompound("LWN_quest_commission_step2_bounty", "Step 2: Defeat (preferably capture alive) {TARGET}", ("TARGET", target));
                case CommissionCategory.LegendaryHunt:
                    // 阶段2目标：讨伐匪王
                    return LWNTextHelper.ResolveCompound("LWN_quest_commission_step2_legendary", "Step 2: Hunt down the bandit king {TARGET}", ("TARGET", target));
                case CommissionCategory.HideoutClear:
                    // 阶段2目标：清剿目标附近的匪窝
                    return LWNTextHelper.ResolveCompound("LWN_quest_commission_step2_hideout", "Step 2: Clear the bandit hideout near {TARGET}", ("TARGET", target));
                case CommissionCategory.CaravanEscort:
                    // 阶段2目标：护送商队抵达目标
                    return LWNTextHelper.ResolveCompound("LWN_quest_commission_step2_caravan", "Step 2: Escort the caravan to {TARGET}", ("TARGET", target));
                case CommissionCategory.EmergencyDelivery:
                    // 阶段2目标：送达物资到目标
                    return LWNTextHelper.ResolveCompound("LWN_quest_commission_step2_delivery", "Step 2: Deliver {ITEM}×{COUNT} to {TARGET}", ("ITEM", item), ("COUNT", _data.TargetItemCount.ToString()), ("TARGET", target));
                case CommissionCategory.SupplyEmergency:
                    // 阶段2目标：采购物资送往目标
                    return LWNTextHelper.ResolveCompound("LWN_quest_commission_step2_supply", "Step 2: Purchase {ITEM}×{COUNT} and deliver to {TARGET}", ("ITEM", item), ("COUNT", _data.TargetItemCount.ToString()), ("TARGET", target));
                case CommissionCategory.ProcurementAgent:
                    // 阶段2目标：购得物品交付
                    return LWNTextHelper.ResolveCompound("LWN_quest_commission_step2_procurement", "Step 2: Acquire {ITEM} and deliver it", ("ITEM", item));
                case CommissionCategory.LostItem:
                    // 阶段2目标：在目标处寻回失物
                    return LWNTextHelper.ResolveCompound("LWN_quest_commission_step2_lost_item", "Step 2: Recover the lost item in {TARGET}", ("TARGET", target));
                case CommissionCategory.TreasureHunt:
                    // 阶段2目标：在目标附近寻得宝藏
                    return LWNTextHelper.ResolveCompound("LWN_quest_commission_step2_treasure", "Step 2: Find the treasure near {TARGET}", ("TARGET", target));
                case CommissionCategory.HorseAcquisition:
                    // 阶段2目标：寻购马匹
                    return LWNTextHelper.ResolveCompound("LWN_quest_commission_step2_horse", "Step 2: Find and buy {ITEM}", ("ITEM", item));
                case CommissionCategory.UndergroundFight:
                    // 阶段2目标：在竞技场获胜
                    return LWNTextHelper.ResolveText("LWN_quest_commission_step2_arena", "Step 2: Win in the arena");
                case CommissionCategory.ArenaSpecial:
                    // 阶段2目标：在竞技场连胜
                    return LWNTextHelper.ResolveText("LWN_quest_commission_step2_arena_streak", "Step 2: Win consecutive arena matches");
                case CommissionCategory.VillageDefense:
                    // 阶段2目标：保卫目标村庄（迎击或贿赂匪徒）
                    return LWNTextHelper.ResolveCompound("LWN_quest_commission_step2_defense", "Step 2: Defend {TARGET} (fight off or bribe the bandits)", ("TARGET", target));
                case CommissionCategory.PrisonBreak:
                    // 阶段2目标：从监狱救出目标
                    return LWNTextHelper.ResolveCompound("LWN_quest_commission_step2_prison", "Step 2: Rescue {TARGET} from prison", ("TARGET", target));
                case CommissionCategory.SupplyIntercept:
                    // 阶段2目标：拦截运往目标的补给队
                    return LWNTextHelper.ResolveCompound("LWN_quest_commission_step2_intercept", "Step 2: Intercept the supply convoy heading for {TARGET}", ("TARGET", target));
                case CommissionCategory.DecoyMission:
                    // 阶段2目标：引开追兵坚持到委托人撤离
                    return LWNTextHelper.ResolveText("LWN_quest_commission_step2_decoy", "Step 2: Lure the pursuers away and hold out until the giver escapes");
                default:
                    // 阶段2目标兜底：完成委托目标
                    return LWNTextHelper.ResolveText("LWN_quest_commission_step2_default", "Step 2: Complete the commission objective");
            }
        }

        /// <summary>创建阶段2目标的进度日志（带描述性标题）。</summary>
        private void CreateObjectiveLog()
        {
            if (_totalProgress <= 0) return;
            _progressLog = AddDiscreteLog(
                new TextObject(GetObjectiveStepText()),
                // 进度日志的完成度标题
                new TextObject(LWNTextHelper.ResolveText("LWN_quest_commission_progress_detail", "Completion")),
                _currentProgress, _totalProgress);
        }

        private bool HasRequiredItems()
        {
            if (string.IsNullOrEmpty(_data.TargetItemId)) return true;
            var item = MBObjectManager.Instance.GetObject<ItemObject>(_data.TargetItemId);
            if (item == null) return false;
            int total = 0;
            if (MobileParty.MainParty?.ItemRoster != null)
            {
                foreach (var element in MobileParty.MainParty.ItemRoster)
                    if (element.EquipmentElement.Item == item)
                        total += element.Amount;
            }
            return total >= _data.TargetItemCount;
        }

        private void ConsumeRequiredItems()
        {
            if (string.IsNullOrEmpty(_data.TargetItemId)) return;
            var item = MBObjectManager.Instance.GetObject<ItemObject>(_data.TargetItemId);
            if (item == null) return;
            int remaining = _data.TargetItemCount;
            var roster = MobileParty.MainParty?.ItemRoster;
            if (roster != null)
            {
                for (int i = roster.Count - 1; i >= 0 && remaining > 0; i--)
                {
                    var element = roster[i];
                    if (element.EquipmentElement.Item == item)
                    {
                        int take = Math.Min(element.Amount, remaining);
                        AgentControlHelper.TransferItems(Hero.MainHero, null, item, take);
                        remaining -= take;
                    }
                }
            }
        }

        private async System.Threading.Tasks.Task EnhanceFlavorText(string baseText)
        {
            try
            {
                // LLM 风味文本提示词：请求为委托描述添加简短风味描写
                string prompt = LWNTextHelper.ResolveCompound("LWN_quest_commission_llm_flavor_prompt",
                    "Add a short flavor description to this commission text ({WORLDDESC} setting, within 20 words, do not change the core information):\n{TEXT}",
                    ("WORLDDESC", Settings.Instance.WorldDescription), ("TEXT", baseText));
                string result = await LLMService.Instance.ChatAsync(prompt, 60, false);
                if (!string.IsNullOrEmpty(result))
                    AddLog(new TextObject(result.Trim()));
            }
            catch { /* LLM 失败静默 */ }
        }

        #endregion

        /// <summary>
        /// 给关联当前 WorldEvent 的调查 Quest 加一条叙事日志（玩家视角的"冒险日记"）。
        /// 遍历所有活跃 Quest，找到匹配 WorldEventId 的 CommissionQuest（Investigation 类别）并写入。
        /// 如果没有活跃的调查 Quest（例如玩家未接任务就自首），静默跳过。
        /// </summary>
        public static void AddNarrativeLogForEvent(WorldEvent evt, string message)
        {
            if (evt == null || string.IsNullOrEmpty(message)) return;
            try
            {
                foreach (var q in Campaign.Current?.QuestManager?.Quests ?? Enumerable.Empty<QuestBase>())
                {
                    if (q is CommissionQuest cq
                        && cq.Data?.WorldEventId == evt.EventId
                        && cq.Data?.Category == CommissionCategory.Investigation)
                    {
                        cq.AddLog(new TextObject(message));
                        return;
                    }
                }
            }
            catch { /* 日志失败不影响游戏逻辑 */ }
        }

    }
}
