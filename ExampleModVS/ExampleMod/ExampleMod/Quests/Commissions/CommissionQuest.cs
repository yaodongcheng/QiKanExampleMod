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
        private bool _suspectIdentifiedLogged;  // 防止 Intent 和事件双重日志

        public override bool IsRemainingTimeHidden => false;
        public override TextObject Title => new TextObject(_data?.GetFlavorDescription() ?? "委托任务");
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
                ?? QuestGiver?.HomeSettlement?.Name?.ToString() ?? "未知地点";
            DebugLogger.Log($"[CommissionQuest] BeginNarrativePhase: {_data.GetFlavorDescription()} giver={QuestGiver?.Name} at {giverLoc}");
            AddLog(new TextObject($"📋 委托情报已记录：{_data.GetFlavorDescription()}"));

            // 阶段1：找到委托人（离散日志，0/1 表示是否完成）
            _findGiverLog = AddDiscreteLog(
                new TextObject($"第一步：前往 {giverLoc} 找 {QuestGiver?.Name} 当面了解委托详情"),
                new TextObject($"找到 {QuestGiver?.Name}"),
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
                AddLog(new TextObject($"定金 {actualDeposit} 第纳尔已到账。"));
            }

            // 正式启动（事件已在 OnStartQuest 的叙事分支里注册过了，这里只补运行启动逻辑）
            _playerCasualtiesAtStart = CountPlayerWounded();
            PerformFullStartup();
        }

        /// <summary>执行完整的委托启动逻辑（日志、进度条、生成部队/商队等）</summary>
        private void PerformFullStartup()
        {
            TextObject logText = new TextObject(
                "{=commission_start}【委托】{TITLE}\n委托人：{GIVER}\n报酬：{REWARD} 第纳尔 | 定金：{DEPOSIT}\n期限：{DAYS} 天\n难度：{TIER}\n{EXTRA}");
            logText.SetTextVariable("TITLE", _data.GetFlavorDescription());
            logText.SetTextVariable("GIVER", QuestGiver.Name);
            logText.SetTextVariable("REWARD", _data.NegotiatedReward);
            logText.SetTextVariable("DEPOSIT", _data.DepositAmount);
            logText.SetTextVariable("DAYS", ((int)(_data.TimeRemainingHours / 24f) + 1));
            logText.SetTextVariable("TIER", GetTierDisplayName());
            logText.SetTextVariable("EXTRA", GetExtraInfo());
            AddLog(logText);

            if (_totalProgress > 0)
            {
                CreateObjectiveLog();
            }

            if (_data.DepositAmount > 0)
                AddLog(new TextObject($"定金 {_data.DepositAmount} 第纳尔已到账。"));

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
                        AddLog(new TextObject("读档后委托目标部队已消失，委托自动取消。"));
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
                "{=commission_start}【委托】{TITLE}\n委托人：{GIVER}\n报酬：{REWARD} 第纳尔 | 定金：{DEPOSIT}\n期限：{DAYS} 天\n难度：{TIER}\n{EXTRA}");
            logText.SetTextVariable("TITLE", _data.GetFlavorDescription());
            logText.SetTextVariable("GIVER", QuestGiver.Name);
            logText.SetTextVariable("REWARD", _data.NegotiatedReward);
            logText.SetTextVariable("DEPOSIT", _data.DepositAmount);
            logText.SetTextVariable("DAYS", ((int)(_data.TimeRemainingHours / 24f) + 1));
            logText.SetTextVariable("TIER", GetTierDisplayName());
            logText.SetTextVariable("EXTRA", GetExtraInfo());
            AddLog(logText);

            if (_totalProgress > 0)
            {
                CreateObjectiveLog();
            }

            if (_data.DepositAmount > 0)
            {
                AddLog(new TextObject($"定金 {_data.DepositAmount} 第纳尔已到账。"));
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
                AddLog(new TextObject("完成高难度委托，恶名 -1。"));
            }

            // 清理生成的地图部队
            CleanupSpawnedParty();

            string gradeStr = GetGradeDisplayName();
            AddLog(new TextObject($"委托完成！评级：{gradeStr}，尾款 {reward} 第纳尔到账。"));
            AddLog(new TextObject($"与 {QuestGiver.Name} 的信任度 { (trustDelta >= 0 ? "+" : "") }{trustDelta}（当前：{TrustSystem.GetTrustDescription(TrustSystem.GetTrust(QuestGiver))}）"));
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
                AddLog(new TextObject($"委托超时失败！与 {QuestGiver.Name} 的关系恶化了。"));
            }
        }

        public override void OnFailed()
        {
            DebugLogger.Log($"[CommissionQuest] OnFailed: {_data?.GetFlavorDescription()} giver={QuestGiver?.Name} category={_data?.Category} worldEventId={_data?.WorldEventId} progress={_currentProgress}/{_totalProgress} timeRemain={_data?.TimeRemainingHours}h");
            if (QuestGiver == null) return;
            CleanupSpawnedParty();
            ChangeRelationAction.ApplyPlayerRelation(QuestGiver, -20);
            TrustSystem.AddTrust(QuestGiver, -20);
            AddLog(new TextObject($"委托失败！{QuestGiver.Name} 对你非常失望。"));
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

            // 旅途事件
            JourneyEvents.TryTrigger(_data, this);

            // ── DecoyMission: 生存计时 ──
            if (_data.Category == CommissionCategory.DecoyMission)
            {
                _data.PhaseProgress++;
                _data.NegotiatedReward += 50;
                // 坚持到时限结束即成功
                if (_data.TimeRemainingHours <= 0)
                {
                    AddLog(new TextObject("委托人已安全撤离！引开追兵的任务完成了。"));
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
                    AddLog(new TextObject($"委托人 {QuestGiver.Name} 已去世，报酬自动结算。"));
                    _data.RewardPayer = null; // 强制用 QuestGiver 遗产支付
                    CompleteWithRewardCollection();
                    return;
                }
                AddLog(new TextObject($"委托人 {QuestGiver.Name} 已去世，委托自动取消。"));
                FailQuest();
                return;
            }

            // 委托人被囚禁 > 30 天 → 委托失败
            if (QuestGiver != null && QuestGiver.IsPrisoner)
            {
                DebugLogger.Log($"[CommissionQuest] OnDailyTick FAIL: giver imprisoned, giver={QuestGiver.Name} category={_data?.Category}");
                AddLog(new TextObject($"委托人 {QuestGiver.Name} 被囚禁已久，委托自动取消。"));
                FailQuest();
                return;
            }

            // BountyHunt: 目标被第三方击杀 → 委托失败
            if ((_data.Category == CommissionCategory.BountyHunt ||
                 _data.Category == CommissionCategory.LegendaryHunt) &&
                _data.TargetHero != null && !_data.TargetHero.IsAlive && _currentProgress == 0)
            {
                DebugLogger.Log($"[CommissionQuest] OnDailyTick FAIL: target killed by third party, target={_data.TargetHero.Name} category={_data.Category}");
                AddLog(new TextObject($"目标 {_data.TargetHero.Name} 已被他人击杀，委托失败。"));
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
                    AddLog(new TextObject("商队已被摧毁！委托失败。"));
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
                        AddLog(new TextObject($"{_data.TargetHero.Name} 似乎已经不在监狱里了！"));
                        UpdateProgress(_totalProgress);
                        return;
                    }
                    // 提醒玩家
                    if (_data.PhaseProgress % 3 == 0) // 每 3 天提醒一次
                    {
                        AddLog(new TextObject($"提示：你已在 {Hero.MainHero.CurrentSettlement.Name}，寻找潜入监狱的方法解救 {_data.TargetHero.Name}。"));
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
                        $"战场上，一个熟悉的身影转向了你这边——{infiltrator.Name}倒戈了！",
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
                                    string escalateMsg = record.Level >= NemesisLevel.ArchNemesis
                                        ? $"{instigator.Name}又逃了——你们之间的恩怨已到了不死不休的地步。"
                                        : $"{instigator.Name}再次逃脱了。他知道你更强了——下一次他会带更多人来。";
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
                        string defectMsg = $"{defector.Name}在战场上倒戈了！——这就是策反的代价。";
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
                        AddLog(new TextObject($"到达了目的地 {settlement.Name}！"));
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
                            AddLog(new TextObject($"已将所需物资送达 {settlement.Name}！"));
                        }
                        else
                        {
                            AddLog(new TextObject($"已到达 {settlement.Name}，但物资不足！请确认携带了所有物资。"));
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
                            AddLog(new TextObject($"已将所需物资送达 {settlement.Name}！"));
                        }
                        else
                        {
                            AddLog(new TextObject($"已到达 {settlement.Name}，但尚未备齐所需物资。请先去采购。"));
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
                                AddLog(new TextObject($"找到了 {foundItem.Name} ×{_data.TargetItemCount}！"));
                            }
                        }
                        UpdateProgress(_totalProgress);
                        AddLog(new TextObject($"在 {settlement.Name} 找到了目标！"));
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
                AddLog(new TextObject("所需物资已备齐！前往目的地交货即可。"));
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
                    AddLog(new TextObject("在竞技场中获胜！委托人会很满意。"));
                    break;
                case CommissionCategory.ArenaSpecial:
                    _currentProgress++;
                    if (_progressLog != null)
                        _progressLog.UpdateCurrentProgress(_currentProgress);
                    if (_currentProgress >= _totalProgress)
                    {
                        UpdateProgress(_totalProgress);
                        AddLog(new TextObject($"在竞技场中连胜 {_totalProgress} 场！委托人非常满意。"));
                    }
                    else
                    {
                        AddLog(new TextObject($"竞技场胜利 {_currentProgress}/{_totalProgress}！再赢一场即可完成委托。"));
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
                AddLog(new TextObject($"警告：{village.Settlement.Name} 已被洗劫！委托可能已失败。"));
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
                        AddLog(new TextObject($"{_data.TargetHero.Name} 已成功救出！"));
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

            string resultDesc = _isTargetCaptured
                ? $"已活捉目标 {_data.TargetHero.Name}！押送回去可获全额报酬。注意：途中可能有同伙劫囚。"
                : $"已击败目标 {_data.TargetHero.Name}（击杀）。活捉可获得更高报酬。";

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
                    AddLog(new TextObject("已成功截获敌方补给队！"));
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
                    AddLog(new TextObject("已成功截获敌方补给队！"));
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
                AddLog(new TextObject("村庄防守成功！击退了来犯之敌。"));
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
                AddLog(new TextObject("匪穴已清剿！这片区域终于安全了。"));
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
                AddLog(new TextObject("你成功击退了追兵！委托人趁这段时间安全撤离了。"));
                CleanupSpawnedParty();
                UpdateProgress(_totalProgress);
            }
            else
            {
                // 被追上击败 → 委托失败
                DebugLogger.Log($"[CommissionQuest] HandleDecoyFightResult: player LOST — failing quest");
                AddLog(new TextObject("你被追兵击败了。委托人可能还来不及逃脱……"));
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
            string villageName = !string.IsNullOrEmpty(_data.TargetSettlementId)
                ? Settlement.Find(_data.TargetSettlementId)?.Name?.ToString() ?? "村庄" : "村庄";

            int tierFactor = _data.Tier switch
            { CommissionTier.Basic => 10, CommissionTier.Skilled => 15,
              CommissionTier.Expert => 25, CommissionTier.Legendary => 40, _ => 10 };
            int bribeCost = tierFactor * Math.Max(1, raiderTroopCount);
            float charmSkill = Hero.MainHero.GetSkillValue(DefaultSkills.Charm);
            float charmDiscount = 0.3f + (charmSkill / 300f) * 0.3f;
            int charmedCost = Math.Max(50, (int)(bribeCost * (1f - charmDiscount)));

            string body = $"前方出现劫掠 {villageName} 的匪徒！\n\n" +
                          $"⚔ 战斗 —— 正面迎击匪徒\n" +
                          $"💰 贿赂匪徒离开 —— 花费 {charmedCost} 第纳尔（原价 {bribeCost}，Charm {charmSkill:0} 减免 {(int)(charmDiscount * 100)}%）\n\n你的选择？";

            DebugLogger.Log($"[CommissionQuest] VillageDefense bribe inquiry: raiders={raiderTroopCount} cost={charmedCost}/{bribeCost}");

            InformationManager.ShowInquiry(new InquiryData(
                "遭遇匪徒", body, true, true, "⚔ 战斗", $"💰 贿赂 ({charmedCost}G)",
                () => { AddLog(new TextObject("你选择了战斗迎击匪徒。")); },
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
                    new InformationMessage($"你只有 {Hero.MainHero.Gold} 第纳尔，不够（需要 {finalCost}）。", Colors.Red));
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
                AddLog(new TextObject($"Charm 检定成功！你将贿赂从 {baseCost} 砍到 {finalCost} 第纳尔。匪首掂了掂钱袋，招呼手下转身离去。"));
            else
                AddLog(new TextObject($"你花了 {finalCost} 第纳尔。匪首掂了掂钱袋，骂骂咧咧地招呼手下转身离去。"));

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
                CommissionGrade.Perfect => "⭐⭐⭐ 完美",
                CommissionGrade.Good => "⭐⭐ 优良",
                CommissionGrade.Passable => "⭐ 完成",
                CommissionGrade.Failed => "✗ 失败",
                _ => "完成"
            };
        }

        private string GetTierDisplayName()
        {
            return _data.Tier switch
            {
                CommissionTier.Basic => "$ 简单",
                CommissionTier.Skilled => "$$ 普通",
                CommissionTier.Expert => "$$$ 困难",
                CommissionTier.Legendary => "★★★★ 传奇",
                _ => "简单"
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
            string locationName = settlement?.Name?.ToString() ?? "案发地";

            // 优先：从 WorldEvent 提取案情细节生成叙事日志
            var evt = !string.IsNullOrEmpty(_data.WorldEventId)
                ? WorldEventStore.FindEvent(_data.WorldEventId) : null;

            if (evt != null)
            {
                string itemName = "财物";
                if (!string.IsNullOrEmpty(evt.TargetItemId))
                {
                    var item = MBObjectManager.Instance.GetObject<ItemObject>(evt.TargetItemId);
                    itemName = item?.Name?.ToString() ?? "财物";
                }
                string scene = !string.IsNullOrEmpty(evt.Config?.CrimeScene) ? evt.Config.CrimeScene : "现场";
                string verb = !string.IsNullOrEmpty(evt.Config?.CrimeVerb) ? evt.Config.CrimeVerb : "丢失";
                int witnessCount = evt.WitnessCount;
                int windowDays = evt.Config?.InvestigationWindowDays ?? 7;

                string witnessClause = witnessCount > 0
                    ? $"有{witnessCount}人目击了事发经过。"
                    : "暂时无人目击。";

                AddLog(new TextObject(
                    $"前往 {locationName} 的{scene}附近搜集线索。" +
                    $"{itemName}{verb}了，{witnessClause}" +
                    $"与当地人交谈或回现场调查，找出是谁干的。"));
                AddLog(new TextObject(
                    $"提示：调查窗口约{windowDays}天，超时后案件将陷入僵局。可用 Scouting 技能加速线索搜集。"));
            }
            else
            {
                // 回退：无 WorldEvent 时使用通用文本
                AddLog(new TextObject($"前往 {locationName} 附近搜集线索。与当地人交谈或回现场调查，找出是谁干的。"));
                AddLog(new TextObject("提示：时间有限——调查窗口关闭后案件将陷入僵局。可用 Scouting 技能加速线索搜集。"));
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
                    AddLog(new TextObject($"目标 {_data.TargetHero.Name} 的匪帮正在劫掠{worldEvent.TargetSettlement?.Name?.ToString() ?? "附近"}。快去阻止他们！"));
                    AddLog(new TextObject("提示：活捉目标可获得额外报酬。可使用 Roguery 技能夜间偷袭增加活捉概率。"));
                    return;
                }
            }

            // 无 WorldEvent 关联 → 在大地图上生成目标部队
            SpawnBountyTargetParty();

            AddLog(new TextObject($"目标 {_data.TargetHero.Name} 的部队已出现在附近。追踪并击败他们！"));
            AddLog(new TextObject("提示：活捉目标可获得额外报酬。可使用 Roguery 技能夜间偷袭增加活捉概率。"));
        }

        private void OnStartCaravanEscort()
        {
            var targetSettlement = !string.IsNullOrEmpty(_data.TargetSettlementId)
                ? Settlement.Find(_data.TargetSettlementId) : null;

            // 生成商队跟随玩家
            SpawnEscortCaravanParty();

            if (targetSettlement != null)
            {
                AddLog(new TextObject($"商队已出发，目的地：{targetSettlement.Name}。请全程护送。"));
                AddLog(new TextObject("提示：Scout 技能可提前发现伏击。路上可能遭遇随机事件。"));
            }
        }

        private void OnStartSupplyEmergency()
        {
            if (!string.IsNullOrEmpty(_data.TargetItemId))
            {
                var item = MBObjectManager.Instance.GetObject<ItemObject>(_data.TargetItemId);
                AddLog(new TextObject($"需要采购：{(item != null ? item.Name.ToString() : _data.TargetItemId)} ×{_data.TargetItemCount}。"));
                AddLog(new TextObject("各城镇市场价格不同，Trade 技能可帮你砍价。预算管理好，差价归自己。"));
                AddLog(new TextObject("超时后每天报酬递减 5%，请尽快完成。"));
            }
        }

        private void OnStartUndergroundFight()
        {
            AddLog(new TextObject("前往竞技场参加比赛并获胜即可。"));
            AddLog(new TextObject("提示：可在自己身上押注（下注功能即将开放），赛前练级可提升胜率。"));
        }

        private void OnStartLegendaryHunt()
        {
            if (_data.TargetHero == null) return;
            SpawnBountyTargetParty();
            AddLog(new TextObject($"⚔ 传奇悬赏：{_data.TargetHero.Name} —— 横行已久的匪王！"));
            AddLog(new TextObject($"击败后可获得 {_data.TargetHero.Name} 身上独一无二的装备。"));
            AddLog(new TextObject("提示：战前务必充分准备——带足兵力、医疗物资和克制装备。"));
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
                        AddLog(new TextObject($"⚠ {village.Name} 即将遭到 {worldEvent.InstigatorHero?.Name?.ToString() ?? "敌人"} 的劫掠！"));
                        AddLog(new TextObject("你可以选择：主动出击拦截 / 在村庄等待迎击 / 花钱请对方离开。"));
                        AddLog(new TextObject("提示：Engineering 可修筑路障减少敌人数，Leadership 可组织民兵增加友军。"));
                        return;
                    }
                }
            }

            // 兜底：spawn 新的劫掠部队
            SpawnRaiderParty(village);
            AddLog(new TextObject($"⚠ {village.Name} 即将遭到劫掠！"));
            AddLog(new TextObject("你可以选择：主动出击拦截匪徒 / 在村庄等待迎击 / 花钱请匪徒离开。"));
            AddLog(new TextObject("提示：Engineering 可修筑路障减少敌人数，Leadership 可组织民兵增加友军。"));
        }

        private void OnStartLostItem()
        {
            if (string.IsNullOrEmpty(_data.TargetSettlementId)) return;
            var targetSettlement = Settlement.Find(_data.TargetSettlementId);
            string settleName = targetSettlement != null ? targetSettlement.Name.ToString() : _data.TargetSettlementId;
            AddLog(new TextObject($"线索指向 {settleName}。"));
            AddLog(new TextObject("前往该地，使用 Scout 技能搜索线索。找到物品后决定如何处理。"));
            float scoutSkill = Hero.MainHero.GetSkillValue(DefaultSkills.Scouting);
            if (scoutSkill > 80)
                AddLog(new TextObject("你的 Scout 技能很高——直觉告诉你小偷可能藏在当地的帮派据点。"));
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
                AddLog(new TextObject($"目标 {_data.TargetHero.Name} 的下落不明。先去酒馆打听消息。"));
                return;
            }

            _data.TargetSettlementId = prisonSettlement.StringId;

            AddLog(new TextObject($"🔓 {_data.TargetHero.Name} 被关在 {prisonSettlement.Name} 的监狱里。"));
            AddLog(new TextObject($"前往 {prisonSettlement.Name}，进入城镇后："));
            AddLog(new TextObject("  方案A：贿赂守卫（花费数百第纳尔）→ 正大光明进入地牢 → 带走囚犯"));
            AddLog(new TextObject("  方案B：潜入地牢（Roguery 检定）→ 高风险，省钱"));
            AddLog(new TextObject("  方案C：外交施压（你必须是领主）→ 和平交涉释放"));
            AddLog(new TextObject($"提示：到达 {prisonSettlement.Name} 后，在城镇界面留意「潜入监狱」或「进入地牢」选项。"));
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
                        AddLog(new TextObject($"敌方补给队已在运往 {targetSettlement?.Name?.ToString() ?? "目的地"} 的路上。必须在到达前拦截！"));
                        AddLog(new TextObject("提示：Scout 可提前发现补给队位置，寻找最佳伏击点。"));
                        AddLog(new TextObject("截获物资后可以选择：交给委托人（报酬）/ 自己留着（物资价值可能更高）。"));
                        return;
                    }
                }
            }

            // 兜底：WorldEvent 辅助部队已被消灭或不存在 → spawn 替代
            SpawnSupplyParty(targetSettlement);
            if (targetSettlement != null)
                AddLog(new TextObject($"敌方补给队正在前往 {targetSettlement.Name}。必须在到达前拦截！"));
            AddLog(new TextObject("提示：Scout 可提前发现补给队位置，寻找最佳伏击点。"));
            AddLog(new TextObject("截获物资后可以选择：交给委托人（报酬）/ 自己留着（物资价值可能更高）。"));
        }

        private void OnStartHideoutClear()
        {
            var targetSettlement = !string.IsNullOrEmpty(_data.TargetSettlementId)
                ? Settlement.Find(_data.TargetSettlementId) : null;
            string settleName = targetSettlement != null ? targetSettlement.Name.ToString() : _data.TargetSettlementId;

            AddLog(new TextObject($"目标：清剿 {settleName} 附近的匪徒藏身处。"));
            AddLog(new TextObject("前往该区域，清除所有匪徒即可完成委托。"));
            AddLog(new TextObject("白天进攻——敌人全在但视野好；夜间潜入——敌人少但黑暗。你选择哪种方式？"));
            AddLog(new TextObject("提示：先派斥候侦察（Scout）可得知内部敌人数量，针对性配兵。"));
        }

        private void OnStartEmergencyDelivery()
        {
            if (string.IsNullOrEmpty(_data.TargetItemId)) return;

            // 给玩家起始物资
            var item = MBObjectManager.Instance.GetObject<ItemObject>(_data.TargetItemId);
            if (item != null && MobileParty.MainParty != null)
            {
                AgentControlHelper.TransferItems(null, Hero.MainHero, item, _data.TargetItemCount);
                AddLog(new TextObject($"收到物资：{item.Name} ×{_data.TargetItemCount}。"));
            }

            var targetSettlement = !string.IsNullOrEmpty(_data.TargetSettlementId)
                ? Settlement.Find(_data.TargetSettlementId) : null;
            AddLog(new TextObject($"限时送达 {targetSettlement?.Name?.ToString() ?? _data.TargetSettlementId}！"));
            AddLog(new TextObject("提示：载重影响行军速度——带得越多赚得越多，但超时报酬会递减。也可以在途中分批次运输。"));
        }

        private void OnStartTreasureHunt()
        {
            if (string.IsNullOrEmpty(_data.TargetSettlementId)) return;
            var targetSettlement = Settlement.Find(_data.TargetSettlementId);

            AddLog(new TextObject($"藏宝图指向 {targetSettlement?.Name?.ToString() ?? _data.TargetSettlementId} 附近。"));
            AddLog(new TextObject($"到达后使用 Scout 技能搜索具体位置。宝藏附近可能有一些守护者——做好准备。"));
            AddLog(new TextObject("提示：也可以花金币雇当地向导，缩小搜索范围。或直接把藏宝图卖给他人变现。"));
        }

        private void OnStartHorseAcquisition()
        {
            if (!string.IsNullOrEmpty(_data.TargetItemId))
            {
                var item = MBObjectManager.Instance.GetObject<ItemObject>(_data.TargetItemId);
                string itemName = item != null ? item.Name.ToString() : _data.TargetItemId;
                AddLog(new TextObject($"委托人想要一匹 {itemName}。"));
                AddLog(new TextObject("各大城镇马市价格不同——多走几个城镇比价。Trade 技能可帮你砍价。"));
                AddLog(new TextObject("提示：如果市场上找不到，可以去看看有没有 NPC 拥有这匹马——交涉购买或直接抢夺（Roguery）。"));
            }
        }

        private void OnStartArenaSpecial()
        {
            _totalProgress = 2; // 需要连赢两场

            var targetSettlement = !string.IsNullOrEmpty(_data.TargetSettlementId)
                ? Settlement.Find(_data.TargetSettlementId) : null;
            string settleName = targetSettlement != null ? targetSettlement.Name.ToString() : "任意竞技场";

            AddLog(new TextObject($"前往 {settleName} 的竞技场，连赢两场比赛。"));
            AddLog(new TextObject("特别规则：禁用盾牌，纯武器对决。可在自己身上押注，赢了双倍报酬！"));
            AddLog(new TextObject("提示：赛前练级提升技能可以增加胜率。也可以雇高手代打（花钱）。"));
        }

        private void OnStartDecoyMission()
        {
            DebugLogger.Log($"[CommissionQuest] OnStartDecoyMission: spawning pursuer party, worldEventId={_data?.WorldEventId} targetSettlementId={_data?.TargetSettlementId} timeRemain={_data?.TimeRemainingHours}h");
            // 生成一个追击玩家的强敌部队
            SpawnPursuerParty();

            AddLog(new TextObject("追兵已经咬上你了！带少量兵力吸引他们注意，让委托人趁机逃跑。"));
            AddLog(new TextObject("坚持的时间越长——委托人逃得越远——报酬越高。如果被追上就只能硬拼了。"));
            AddLog(new TextObject("提示：可以诱敌深入——把追兵拉到友军附近；也可以花钱请佣兵帮忙挡一阵。"));
            AddLog(new TextObject("不要硬刚——边打边跑才是上策。"));
        }

        private void OnStartProcurementAgent()
        {
            if (!string.IsNullOrEmpty(_data.TargetItemId))
            {
                var item = MBObjectManager.Instance.GetObject<ItemObject>(_data.TargetItemId);
                string itemName = item != null ? item.Name.ToString() : _data.TargetItemId;

                AddLog(new TextObject($"委托人需要一件 {itemName}，给了你 {_data.NegotiatedReward} 第纳尔的预算。"));
                AddLog(new TextObject("去各大城镇搜索比价——花费越少，剩下的预算归你自己。"));
                AddLog(new TextObject($"提示：Trade 技能影响砍价幅度。如果市场上买不到，需要找到拥有此装备的 NPC 交涉购买。"));
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
                    V.SetPartyName(targetParty, new TextObject($"{_data.TargetHero.Name}的匪帮"));

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

                AddLog(new TextObject($"{_data.TargetHero.Name} 的部队出现在地图上，开始追踪吧。"));
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
                    V.SetPartyName(escortParty, new TextObject($"{QuestGiver.Name}的商队"));

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
                string raiderName = $"劫掠{targetVillage.Name}的匪帮";
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

                AddLog(new TextObject($"劫掠部队已出现在 {targetVillage.Name} 附近！"));
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

                var component = new CustomPartyComponent(targetSettlement, "敌方补给队");
                MobileParty supplyParty = V.MakeParty(partyId, component);
                if (supplyParty != null)
                    V.SetPartyName(supplyParty, new TextObject("敌方补给队"));

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

                AddLog(new TextObject("敌方补给队已出现在地图上。必须在它到达目的地之前拦截！"));
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
                var component = new CustomPartyComponent(home, "追兵");
                MobileParty pursuerParty = V.MakeParty(partyId, component);
                if (pursuerParty != null)
                    V.SetPartyName(pursuerParty, new TextObject("追兵"));

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

                AddLog(new TextObject($"⚠ 追兵已出现在地图上，正在追击你！坚持 {((int)(_data.TimeRemainingHours / 24f) + 1)} 天。"));
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
            AddLog(new TextObject($"委托被迫终止。与 {QuestGiver?.Name.ToString() ?? "委托人"} 的关系下降了。"));
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
            AddLog(new TextObject("调查完成：嫌犯已锁定，转入悬赏缉拿阶段。"));
            CompleteQuestWithSuccess();
            DebugLogger.Log($"[CommissionQuest] CompleteObjectivesFromExternal: {StringId} category={_data.Category}");
        }

        /// <summary>
        /// 由 Intent（FrameSuspectIntent 等）调用：通知调查 Quest "嫌犯已锁定"。
        /// 只加日志，不完成 Quest——Quest 完成由后续 Intent（如 AcceptBountyQuest）负责。
        /// </summary>
        public void NotifySuspectIdentified(string suspectName)
        {
            if (_data == null || _suspectIdentifiedLogged) return;
            _suspectIdentifiedLogged = true;
            string giverName = QuestGiver?.Name?.ToString() ?? "委托人";
            AddLog(new TextObject($"调查取得进展——嫌犯锁定为{suspectName}。回去向{giverName}汇报。"));
            DebugLogger.Log($"[CommissionQuest] NotifySuspectIdentified: {StringId} suspect={suspectName}");
        }

        /// <summary>
        /// WorldEvent 阶段变化回调（仅 Investigation Quest 注册）。
        /// 处理 NPC 后台调查查出嫌犯的 case（非玩家 Intent 驱动）。
        /// Intent 驱动的更新在 Intent.OnSuccess 中直接调用 NotifySuspectIdentified，
        /// 会先设置 _suspectIdentifiedLogged=true，此回调检测到后跳过。
        /// </summary>
        private void OnWorldEventStageChangedForQuest(WorldEvent evt)
        {
            if (_data == null) return;
            if (evt.EventId != _data.WorldEventId) return;
            if (_data.Category != CommissionCategory.Investigation) return;

            if (evt.Stage == EventStage.Active && !string.IsNullOrEmpty(evt.SuspectHeroId) && !_suspectIdentifiedLogged)
            {
                var suspect = Hero.FindFirst(h => h.StringId == evt.SuspectHeroId);
                NotifySuspectIdentified(suspect?.Name?.ToString() ?? "某人");
                DebugLogger.Log($"[CommissionQuest] OnWorldEventStageChanged: {StringId} stage=Active (NPC investigation found suspect)");
            }
            else if (evt.Stage == EventStage.Resolved)
            {
                // 案件已结案（赔款/威胁/嫌犯已交付等）→ 关闭调查委托
                if (_data.IsObjectivesComplete) return; // 已通过其他路径完成
                _data.IsObjectivesComplete = true;
                AddLog(new TextObject("案件已结案，调查委托自动完成。"));
                CompleteQuestWithSuccess();
                DebugLogger.Log($"[CommissionQuest] OnWorldEventStageChanged: {StringId} stage=Resolved — investigation quest auto-completed");
            }
        }

        #endregion

        #region Deposit Repayment

        private void ShowDepositRepaymentInquiry()
        {
            string body = $"委托超时失败。{QuestGiver.Name} 要求你退还定金 {_data.DepositAmount} 第纳尔。\n\n" +
                          "• 退还定金：信任 -10，可继续接委托\n" +
                          "• Charm 检定：减半退还（失败 = 拒绝退还后果）\n" +
                          "• 拒绝退还：信任 -40 + 恶名 +1 + 关系恶化";

            InformationManager.ShowInquiry(new InquiryData(
                "定金追讨",
                body,
                true, true,
                "退还定金",
                "拒绝退还",
                () =>
                {
                    // 退还
                    AgentControlHelper.TransferGold(Hero.MainHero, QuestGiver, _data.DepositAmount);
                    _depositRepaid = true;
                    _data.DepositRepaid = true;
                    TrustSystem.AddTrust(QuestGiver, -10);
                    ChangeRelationAction.ApplyPlayerRelation(QuestGiver, -5);
                    AddLog(new TextObject($"已退还定金 {_data.DepositAmount} 第纳尔。"));
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
                        AddLog(new TextObject($"Charm 检定成功！减半退还 {halfDeposit} 第纳尔。"));
                    }
                    else
                    {
                        // 拒绝退还后果
                        _depositRepaid = false;
                        _data.DepositRepaid = false;
                        TrustSystem.AddTrust(QuestGiver, -40);
                        ChangeRelationAction.ApplyPlayerRelation(QuestGiver, -15);
                        InfamySystem.AddInfamy(1);
                        AddLog(new TextObject($"拒绝退还定金！恶名 +1，与 {QuestGiver.Name} 的关系严重恶化。"));
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
                string prisonerName = _data.TargetHero != null ? _data.TargetHero.Name.ToString() : "囚犯";
                AddLog(new TextObject($"⚠ 警报：{prisonerName} 的同伙正在接近，试图劫囚！"));
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
                    new TextObject("{=commission_progress}委托进度"),
                    new TextObject("{=commission_progress_detail}完成度"),
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
                string payerName = payer?.Name?.ToString() ?? "委托人";
                string payerLoc = payer?.CurrentSettlement?.Name?.ToString()
                    ?? payer?.HomeSettlement?.Name?.ToString() ?? "未知地点";

                // 阶段3：找结账人领报酬
                _rewardLog = AddDiscreteLog(
                    new TextObject($"第三步：前往 {payerLoc} 找 {payerName} 领取报酬"),
                    new TextObject($"领取报酬"),
                    0, 1);

                AddLog(new TextObject($"委托目标已完成！前往 {payerLoc} 找 {payerName} 领取报酬。"));
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
                AddLog(new TextObject("完成高难度委托，恶名 -1。"));
            }

            string gradeStr = GetGradeDisplayName();
            AddLog(new TextObject($"委托完成！评级：{gradeStr}，报酬 {reward} 第纳尔已领取。"));
            AddLog(new TextObject($"与 {QuestGiver.Name} 的信任度 {(trustDelta >= 0 ? "+" : "")}{trustDelta}（当前：{TrustSystem.GetTrustDescription(TrustSystem.GetTrust(QuestGiver))}）"));

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

                AddLog(new TextObject("关联的世界事件已解决。"));
                DebugLogger.Log($"[CommissionQuest] Resolved WorldEvent: {_data.WorldEventId}");
            }

            CompleteQuestWithSuccess();
        }

        private string GetExtraInfo()
        {
            switch (_data.Category)
            {
                case CommissionCategory.Investigation:
                    return "在案发定居点附近搜索或与村民交谈可推进调查。";
                case CommissionCategory.BountyHunt:
                    return _data.TargetHero != null
                        ? $"目标：{_data.TargetHero.Name} — 活捉报酬 ×2.25"
                        : "";
                case CommissionCategory.CaravanEscort:
                    return "注意：旅途中可能遭遇盗贼伏击，Scout 技能可提前发现。";
                case CommissionCategory.SupplyEmergency:
                    return "超时后每天报酬递减 5%。";
                case CommissionCategory.UndergroundFight:
                    return "赛前练级提升技能可增加胜率。";
                default: return "";
            }
        }

        /// <summary>阶段2目标的简短描述（用于 quest 日志步骤标题）。</summary>
        private string GetObjectiveStepText()
        {
            string target = _data.TargetHero?.Name?.ToString()
                ?? (!string.IsNullOrEmpty(_data.TargetSettlementId)
                    ? Settlement.Find(_data.TargetSettlementId)?.Name?.ToString() ?? "目标地"
                    : "目标");
            string item = !string.IsNullOrEmpty(_data.TargetItemId)
                ? MBObjectManager.Instance.GetObject<ItemObject>(_data.TargetItemId)?.Name?.ToString() ?? "物资"
                : "物资";

            switch (_data.Category)
            {
                case CommissionCategory.Investigation:
                    return $"第二步：搜集线索，找出真凶";
                case CommissionCategory.BountyHunt:
                    return $"第二步：击败（最好活捉）{target}";
                case CommissionCategory.LegendaryHunt:
                    return $"第二步：讨伐匪王 {target}";
                case CommissionCategory.HideoutClear:
                    return $"第二步：清剿 {target} 附近的匪窝";
                case CommissionCategory.CaravanEscort:
                    return $"第二步：护送商队抵达 {target}";
                case CommissionCategory.EmergencyDelivery:
                    return $"第二步：将 {item}×{_data.TargetItemCount} 送达 {target}";
                case CommissionCategory.SupplyEmergency:
                    return $"第二步：采购 {item}×{_data.TargetItemCount} 送往 {target}";
                case CommissionCategory.ProcurementAgent:
                    return $"第二步：购得 {item} 交付";
                case CommissionCategory.LostItem:
                    return $"第二步：在 {target} 寻回失物";
                case CommissionCategory.TreasureHunt:
                    return $"第二步：在 {target} 附近寻得宝藏";
                case CommissionCategory.HorseAcquisition:
                    return $"第二步：寻购 {item}";
                case CommissionCategory.UndergroundFight:
                    return $"第二步：在竞技场获胜";
                case CommissionCategory.ArenaSpecial:
                    return $"第二步：在竞技场连胜";
                case CommissionCategory.VillageDefense:
                    return $"第二步：保卫 {target}（迎击或贿赂匪徒）";
                case CommissionCategory.PrisonBreak:
                    return $"第二步：从监狱救出 {target}";
                case CommissionCategory.SupplyIntercept:
                    return $"第二步：拦截运往 {target} 的补给队";
                case CommissionCategory.DecoyMission:
                    return $"第二步：引开追兵，坚持到委托人撤离";
                default:
                    return "第二步：完成委托目标";
            }
        }

        /// <summary>创建阶段2目标的进度日志（带描述性标题）。</summary>
        private void CreateObjectiveLog()
        {
            if (_totalProgress <= 0) return;
            _progressLog = AddDiscreteLog(
                new TextObject(GetObjectiveStepText()),
                new TextObject("完成度"),
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
                string prompt = $"给这句委托描述加一点简短的风味描写（{Settings.Instance.WorldDescription}世界观，20字以内，不要改核心信息）：\n{baseText}";
                string result = await LLMService.Instance.ChatAsync(prompt, 60, false);
                if (!string.IsNullOrEmpty(result))
                    AddLog(new TextObject(result.Trim()));
            }
            catch { /* LLM 失败静默 */ }
        }

        #endregion
    }
}
