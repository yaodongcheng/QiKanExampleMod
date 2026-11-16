using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 序章战役入口：城门菜单、固定地图刑期、刑满真实换装、回门外。
    /// 转场由应用帧驱动：暂停的 town menu 不会触发 CampaignEvents.TickEvent。
    /// 原版逛城不立 EnterViaOption / EnterJailViaOption，不加载序章。
    /// </summary>
    public class StoryProbeBehavior : CampaignBehaviorBase
    {
        public static StoryProbeBehavior Instance { get; private set; }

        private bool _opening;
        private bool _transitionStable;
        private bool _boundaryStable;
        private float _transitionCooldown;
        private int _transitionFailures;
        private float _restoreRetryCooldown;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
        }

        /// <summary>存档统一走 MyBehavior.SyncData 的 lwn_story_probe。</summary>
        public override void SyncData(IDataStore dataStore) { }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenuOption("town", "lwn_story_probe_enter",
                    "{=LWN_story_menu_enter}Enter the town (playtest)",
                    MenuCondition, MenuConsequence, false, 1);
                DebugLogger.Log("[StoryProbe] town menu option registered");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] menu registration failed: {ex.Message}");
            }
        }

        private bool MenuCondition(MenuCallbackArgs args)
        {
            try
            {
                if (!StoryProbeSettings.Enabled || StoryProbeState.Data.CancellationPending
                    || PlayerDetentionBehavior.IsDetentionActive || Hero.MainHero?.IsPrisoner == true) return false;
                var settlement = Settlement.CurrentSettlement;
                if (settlement == null || settlement.StringId != StoryProbeSettings.SettlementId) return false;
                if (StoryProbeState.Phase == StoryProbePhase.Done) return false;
                args.optionLeaveType = GameMenuOption.LeaveType.Mission;
                return true;
            }
            catch { return false; }
        }

        private void MenuConsequence(MenuCallbackArgs args)
        {
            switch (StoryProbeState.Phase)
            {
                case StoryProbePhase.FirstAttempt:
                    ShowIntroInquiry();
                    break;
                case StoryProbePhase.Jail:
                    if (StoryProbeState.Data.JailDone) QueueSecondFromJail();
                    else RequestJailInquiry();
                    break;
                default:
                    EnterTownCenter();
                    break;
            }
        }

        /// <summary>第一次进城：4 句背景 +【硬闯】【再想想】。</summary>
        public static void ShowIntroInquiry()
        {
            if (!CanUseTargetTown() || StoryProbeState.Phase != StoryProbePhase.FirstAttempt) return;
            int token = StoryProbeState.RuntimeToken;
            string townId = StoryProbeSettings.SettlementId;
            try
            {
                string title = LWNTextHelper.ResolveText("LWN_story_intro_inquiry_title", "The Gate");
                string body =
                    LWNTextHelper.ResolveText("LWN_story_intro_inquiry_body_1", "Three months ago your sister went into this town to work, and never wrote again.") + "\n" +
                    LWNTextHelper.ResolveText("LWN_story_intro_inquiry_body_2", "You followed her here. Your money was stolen on the road; all you have left is her last letter.") + "\n" +
                    LWNTextHelper.ResolveText("LWN_story_intro_inquiry_body_3", "Worse: the dockhands pinned something on you. Your face is on a small warrant at the gate.") + "\n" +
                    LWNTextHelper.ResolveText("LWN_story_intro_inquiry_body_4", "The front gate is closed to you. The people outside it each know a way in.");
                DebugLogger.Log("[StoryProbe] intro inquiry shown (first attempt)");
                InformationManager.ShowInquiry(new InquiryData(
                    title, body, true, true,
                    LWNTextHelper.ResolveText("LWN_story_intro_btn_go", "Force your way in"),
                    LWNTextHelper.ResolveText("LWN_story_intro_btn_back", "Think again"),
                    () =>
                    {
                        if (StoryProbeState.IsRuntimeCurrent(token) && townId == StoryProbeSettings.SettlementId
                            && StoryProbeState.Phase == StoryProbePhase.FirstAttempt) EnterTownCenter();
                    }, () => { }), true);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] ShowIntroInquiry failed: {ex.Message}");
            }
        }

        private static bool CanUseTargetTown()
            => StoryProbeSettings.Enabled && !StoryProbeState.Data.CancellationPending
               && Settlement.CurrentSettlement?.StringId == StoryProbeSettings.SettlementId;

        /// <summary>兼容调试入口；现在只排地图刑期，不打开地牢或赠衣弹窗。</summary>
        public static void ShowJailInquiry(bool manual = false) => RequestJailInquiry();

        /// <summary>保留旧入口名；场景退出后启动固定地图刑期，不打开地牢或赠衣弹窗。</summary>
        public static void RequestJailInquiry()
        {
            var d = StoryProbeState.Data;
            if (!CanUseTargetTown() || d.JailDone) return;
            d.TutorialLost = true;
            if (string.IsNullOrEmpty(d.DetentionOwnerId)) d.DetentionOwnerId = Guid.NewGuid().ToString("N");
            StoryProbeState.Phase = StoryProbePhase.Jail;
            d.PendingTransition = 1;
            d.TransitionSettlementId = Settlement.CurrentSettlement?.StringId ?? StoryProbeSettings.SettlementId;
            d.StorySettlementId = d.TransitionSettlementId;
            d.Suspended = false;
            d.JailSceneEntered = false;
            StoryProbeState.EnterViaOption = false;
            if (Instance != null)
            {
                Instance.ResetTransitionRetry();
            }
            DebugLogger.Log($"[StoryProbe] map sentence queued: {d.TransitionSettlementId}");
        }

        public static void QueueSecondFromJail()
        {
            var d = StoryProbeState.Data;
            if (!CanUseTargetTown() || !d.JailDone) return;
            d.PendingTransition = 2;
            d.TransitionSettlementId = Settlement.CurrentSettlement?.StringId ?? StoryProbeSettings.SettlementId;
            // 旧 prison Host 仍在演换装/出场，不能提早把共享 Phase 改成 Second。
            StoryProbeState.Phase = StoryProbePhase.Jail;
            Instance?.ResetTransitionRetry();
            DebugLogger.Log($"[StoryProbe] return outside gate queued: {d.TransitionSettlementId}");
        }

        public static void AbortPrisonTransition(string reason) => CancelPrologue(reason);

        /// <summary>统一取消：先废旧回调并停 Host，再清转场，最后结清尚欠账；失败绝不宣称归还成功。</summary>
        public static bool CancelPrologue(string reason, bool restart = false)
        {
            var d = StoryProbeState.Data;
            bool firstRequest = !d.CancellationPending;
            bool hadEscrow = d.Stripped;
            d.CancellationPending = true;
            d.CancellationRestart |= restart;
            if (firstRequest) StoryProbeState.InvalidateRuntime();
            try { StoryProbeMissionBehavior.Current?.CancelRuntime(reason); }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] runtime cancel failed: {ex.Message}"); }
            d.PendingTransition = 0;
            d.TransitionSettlementId = "";
            StoryProbeState.EnterViaOption = false;
            StoryProbeState.EnterJailViaOption = false;
            if (Instance != null)
            {
                Instance.ResetTransitionRetry();
                Instance._restoreRetryCooldown = 2f;
            }
            if ((d.Stripped || !string.IsNullOrEmpty(d.FirstGuardBoostHeroId))
                && (Campaign.Current == null || Campaign.Current.SaveHandler?.IsSaving == true))
            {
                DebugLogger.Log("[StoryProbe] cancellation escrow retry deferred until campaign/save boundary is safe");
                return false;
            }
            if (!PlayerDetentionBehavior.CancelSentence(d.DetentionOwnerId)) return false;
            bool skillsRestored = StoryProbeMissionBehavior.RestoreFirstGuardSkills();
            bool restored = StoryProbeMissionBehavior.RestorePlayerBelongings();
            if (!skillsRestored || !restored)
            {
                DebugLogger.Log($"[StoryProbe] cancelled with outstanding escrow/skills: {reason}");
                if (firstRequest)
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveText("LWN_info_equipment_error") + " " + LWNTextHelper.ResolveText("LWN_plan_btn_retry")));
                return false;
            }
            if (d.CancellationRestart)
            {
                if (!PrologueQuestLauncher.CancelAll()) return false;
                StoryProbeState.ResetAll();
                // 整轮重开 = 通行权一并作废（令牌在背包里，不收回就会"重置了剧情却还免检进城"）。
                StoryProbeMissionBehavior.RevokeGatePass("prologue reset");
            }
            else
            {
                d.CancellationPending = false;
                d.Suspended = true;
                d.JailSceneEntered = false;
                if (StoryProbeState.Phase == StoryProbePhase.Jail && d.JailDone)
                    StoryProbeState.Phase = StoryProbePhase.Second;
            }
            DebugLogger.Log($"[StoryProbe] cancellation settled: {reason}");
            if (hadEscrow)
                InformationManager.DisplayMessage(new InformationMessage(
                    LWNTextHelper.ResolveText("LWN_story_restored_safety",
                        "They give you back what was taken — the prologue is over for now.")));
            return true;
        }

        private void ResetTransitionRetry()
        {
            _transitionStable = false;
            _boundaryStable = false;
            _transitionCooldown = 0f;
            _transitionFailures = 0;
        }

        private static bool IsTransitionUiBlocked()
        {
            // Inquiry 可能挂在原屏的层上；原版 MissionScreen 也用 IsAnyInquiryActive，不能只看屏名。
            return InformationManager.IsAnyInquiryActive()
                || UiFullScreenHelper.IsEscapeMenuOpen() || UiFullScreenHelper.IsFullScreenUiOpen()
                || ModInput.IsSystemModalActive() || PlanCommandFlow.IsActive || ImChatView.IsOpen
                || ConversationEntryPatch.IsConversationActive();
        }

        /// <summary>MySubModule.OnApplicationTick 调用；应用 dt 冷却，不依赖暂停的战役时钟。</summary>
        public void TickPendingTransition(float dt)
        {
            if (_opening) return;
            if (!float.IsNaN(dt) && !float.IsInfinity(dt) && dt > 0f)
                _transitionCooldown = Math.Max(0f, _transitionCooldown - Math.Min(dt, 0.25f));
            try
            {
                var d = StoryProbeState.Data;
                if (Campaign.Current == null) return;
                if (!float.IsNaN(dt) && !float.IsInfinity(dt) && dt > 0f)
                    _restoreRetryCooldown = Math.Max(0f, _restoreRetryCooldown - Math.Min(dt, 0.25f));
                bool hasWork = StoryProbeMissionBehavior.Current?.Active == true || d.Stripped
                    || d.PendingTransition != 0 || (!d.Suspended && StoryProbeState.Phase == StoryProbePhase.Jail)
                    || d.CancellationPending;
                bool changedTown = !string.IsNullOrEmpty(d.StorySettlementId)
                    && d.StorySettlementId != StoryProbeSettings.SettlementId;
                if ((hasWork || changedTown) && (!StoryProbeSettings.Enabled || changedTown || d.CancellationPending))
                {
                    if (_restoreRetryCooldown <= 0f) CancelPrologue("settings changed or outstanding cancellation", restart: changedTown);
                    return;
                }
                // 读档时原 Mission 已不存在，临时增强只能恢复，不能永久留在 Hero 上。
                if (Mission.Current == null && !string.IsNullOrEmpty(d.FirstGuardBoostHeroId)
                    && _restoreRetryCooldown <= 0f)
                {
                    StoryProbeMissionBehavior.RestoreFirstGuardSkills();
                    _restoreRetryCooldown = 2f;
                }
                if (_transitionCooldown > 0f) return;
                if (!d.Suspended && StoryProbeState.Phase == StoryProbePhase.Jail && d.TutorialLost
                    && Mission.Current == null && GameStateManager.Current?.ActiveState is MapState
                    && !IsTransitionUiBlocked() && Campaign.Current.SaveHandler?.IsSaving != true)
                {
                    if (TickMapSentence()) return;
                }
                if (Campaign.Current == null || Mission.Current != null
                    || !(GameStateManager.Current?.ActiveState is MapState map) || !map.AtMenu
                    || !UiFullScreenHelper.IsCampaignMapScreen() || IsTransitionUiBlocked()
                    || Campaign.Current.SaveHandler == null || Campaign.Current.SaveHandler.IsSaving)
                {
                    _transitionStable = false;
                    return;
                }
                var settlement = Settlement.CurrentSettlement;
                if (!StoryProbeSettings.Enabled || settlement == null || !settlement.IsTown
                    || settlement.StringId != StoryProbeSettings.SettlementId)
                {
                    _transitionStable = false;
                    if (d.PendingTransition != 0 || d.Stripped || (!d.Suspended && StoryProbeState.Phase == StoryProbePhase.Jail))
                    {
                        if (!_boundaryStable) { _boundaryStable = true; return; }
                        AbortPrisonTransition("prologue disabled or transition target changed at menu");
                    }
                    return;
                }
                _boundaryStable = false;
                // 老失败存档曾停在地图 Jail 阶段：恢复真实入牢，不在地图补弹换装。
                if (!d.Suspended && d.PendingTransition == 0 && StoryProbeState.Phase == StoryProbePhase.Jail && !d.JailDone)
                    RequestJailInquiry();
                if (d.PendingTransition == 0) { _transitionStable = false; return; }
                if (d.TransitionSettlementId != settlement.StringId) { _transitionStable = false; return; }
                if (!_transitionStable) { _transitionStable = true; return; }
                // 每轮最多尝试五次；耗尽仍保留 pending，菜单重试可重新启用，不每帧刷屏。
                if (_transitionFailures >= 5) return;
                _opening = true;
                bool opened = false;
                int pending = d.PendingTransition;
                try
                {
                    if (pending == 1)
                    {
                        // 刑期尚未完成时绝不打开 prison mission。
                        opened = false;
                    }
                    else if (pending == 2)
                    {
                        StoryProbeState.EnterJailViaOption = false;
                        StoryProbeState.Phase = StoryProbePhase.Second;
                        opened = EnterTownCenter(queuedTransition: true);
                    }
                    if (opened)
                    {
                        d.PendingTransition = 0;
                        d.TransitionSettlementId = "";
                        ResetTransitionRetry();
                        DebugLogger.Log($"[StoryProbe] transition opened: pending={pending} town={settlement.StringId}");
                    }
                    else TransitionFailed(pending, "mission did not open");
                }
                catch (Exception ex) { TransitionFailed(pending, ex.Message); }
                finally { _opening = false; }
            }
            catch (Exception ex)
            {
                _transitionStable = false;
                _transitionCooldown = 4f;
                // 守卫异常同样有界，避免坏 UI 层造成每帧日志。
                if (_transitionFailures < 5)
                {
                    _transitionFailures++;
                    DebugLogger.Log($"[StoryProbe] transition guard failed ({_transitionFailures}/5): {ex.Message}");
                }
            }
        }

        /// <summary>地图刑期真实完成后才换装；收据和装备托管均可存档重试。</summary>
        private bool TickMapSentence()
        {
            var d = StoryProbeState.Data;
            if (d.JailDone && d.PendingTransition == 0) return false;
            if (string.IsNullOrEmpty(d.DetentionOwnerId))
                d.DetentionOwnerId = Guid.NewGuid().ToString("N");
            if (PlayerDetentionBehavior.IsSentenceRunning(d.DetentionOwnerId)) return true;
            if (!PlayerDetentionBehavior.IsSentenceCompleted(d.DetentionOwnerId))
            {
                var settlement = Settlement.CurrentSettlement;
                if (settlement == null || settlement.StringId != d.StorySettlementId
                    || Hero.MainHero?.IsPrisoner == true) return true;
                if (!PlayerDetentionBehavior.CanBeginSentence(settlement, d.DetentionOwnerId,
                    Math.Max(1, StoryProbeTuning.Current.Intro.DetentionDays))) return true;
                if (!StoryProbeMissionBehavior.ConfiscatePlayerBelongings()
                    || !PlayerDetentionBehavior.TryBeginSentence(settlement, d.DetentionOwnerId,
                        Math.Max(1, StoryProbeTuning.Current.Intro.DetentionDays)))
                {
                    _transitionCooldown = 2f;
                    DebugLogger.Log("[StoryProbe] map sentence pending: escrow or detention entry unavailable");
                    return true;
                }
                d.PendingTransition = 1; // 等持久刑期回执，不依赖场景或应用时钟。
                DebugLogger.Log("[StoryProbe] map sentence started: " + PlayerDetentionBehavior.DescribeSentence(d.DetentionOwnerId));
                return true;
            }
            if (!d.JailDone)
            {
                // 刑满：只做「换装 + 记事实」，**不自动开场景**（2026-11-16 用户裁定）：
                // 玩家应当停在城镇菜单，自己确认出狱与乔装，再从菜单里主动进城。
                if (!StoryProbeMissionBehavior.StripPlayer(advancePhase: true))
                { _transitionCooldown = 2f; return true; }
                d.StripAtHours = CampaignTime.Now.ToHours;
                d.PendingTransition = 0;
                d.TransitionSettlementId = "";
                DebugLogger.Log("[StoryProbe] sentence complete; disguise equipped; stay at town menu (manual re-entry)");
                InformationManager.DisplayMessage(new InformationMessage(
                    LWNTextHelper.ResolveText("LWN_story_jail_return_manual",
                        "You are out. Change into the old clothes, and walk back to the gate when you are ready.")));
            }
            // 原版 EndCaptivity 会离城并结束 Encounter；按原版重新进同一城，下一帧再开场景。
            if (Settlement.CurrentSettlement == null && PlayerEncounter.Current == null
                && Hero.MainHero?.IsPrisoner != true && TaleWorlds.CampaignSystem.Party.MobileParty.MainParty?.MapEvent == null)
            {
                var town = Settlement.Find(d.StorySettlementId);
                if (town == null || !town.IsTown) { CancelPrologue("sentence town no longer available"); return true; }
                EncounterManager.StartSettlementEncounter(TaleWorlds.CampaignSystem.Party.MobileParty.MainParty, town);
                if (PlayerEncounter.EncounterSettlement == town)
                {
                    PlayerEncounter.EnterSettlement();
                    GameMenu.ActivateGameMenu("town");
                }
                _transitionStable = false;
                return true;
            }
            return false;
        }

        private void TransitionFailed(int pending, string reason)
        {
            StoryProbeState.EnterViaOption = false;
            StoryProbeState.EnterJailViaOption = false;
            StoryProbeState.Phase = StoryProbePhase.Jail;
            _transitionFailures++;
            _transitionCooldown = Math.Min(8f, _transitionFailures * 2f);
            _transitionStable = false;
            DebugLogger.Log($"[StoryProbe] transition retry retained: pending={pending} attempt={_transitionFailures}/5 reason={reason}");
        }

        /// <summary>共享门外出生点入口；排队调用失败只记日志，不重复弹错误提示。</summary>
        public static bool EnterTownCenter(bool queuedTransition = false)
        {
            if (!CanUseTargetTown() || PlayerDetentionBehavior.IsDetentionActive || Hero.MainHero?.IsPrisoner == true
                || StoryProbeState.Phase == StoryProbePhase.Done || Mission.Current != null
                || !(GameStateManager.Current?.ActiveState is MapState map) || !map.AtMenu) return false;
            if (StoryProbeState.Phase == StoryProbePhase.Jail)
            {
                if (StoryProbeState.Data.JailDone) QueueSecondFromJail();
                else RequestJailInquiry();
                return false;
            }
            try
            {
                var settlement = Settlement.CurrentSettlement;
                if (settlement == null || !settlement.IsTown)
                {
                    if (queuedTransition) throw new InvalidOperationException("not in a town");
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveText("LWN_story_err_not_in_town", "You are not in a town.")));
                    return false;
                }
                var location = settlement.LocationComplex?.GetLocationWithId("center");
                if (location == null)
                {
                    if (queuedTransition) throw new InvalidOperationException("town center unavailable");
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveText("LWN_story_err_no_center", "This town has no center location.")));
                    return false;
                }
                var encounter = PlayerEncounter.LocationEncounter;
                if (encounter == null) throw new InvalidOperationException("active LocationEncounter unavailable");
                // 修改布局后重进专用场景就读新版；此处旧 Mission 已结束、演员尚未创建。
                // 坏配置保留上次有效发布；首次加载就失败则停止，不走旧布局猜测补生。
                if (!StoryV2Data.Reload())
                {
                    DebugLogger.Log("[StoryProbe] center entry refused: " + StoryV2Data.Report);
                    if (queuedTransition) throw new InvalidOperationException(StoryV2Data.Report);
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveText("LWN_story_err_gate_layout",
                            "This town's prologue gate is not ready. Choose a supported town.")));
                    return false;
                }
                StoryProbeState.Data.StorySettlementId = settlement.StringId;
                StoryProbeState.Data.Suspended = false;
                StoryProbeState.EnterViaOption = true;
                // 🔴 人口限制必须在 Mission 打开**之前**登记：原版 AfterStart 一开始就会批量生成居民。
                StoryPopulationGuard.Request(settlement.StringId, StoryProbeState.Phase.ToString());
                const string OutsideSpawnTag = "sp_outside_near_town_main_gate";
                var openedMission = encounter.CreateAndOpenMissionController(location, null, null, OutsideSpawnTag);
                if (openedMission == null)
                {
                    StoryPopulationGuard.CancelPending("mission did not open");
                    throw new InvalidOperationException("town center mission did not open");
                }
                // 返回的 mission 才是开场回执；EnterViaOption 可能已被同步启动的 Host 消费。
                DebugLogger.Log($"[StoryProbe] entering center of {settlement.StringId} phase={StoryProbeState.Phase}");
                return true;
            }
            catch (Exception ex)
            {
                StoryProbeState.EnterViaOption = false;
                StoryPopulationGuard.CancelPending("enter town center failed");
                DebugLogger.Log($"[StoryProbe] EnterTownCenter failed: {ex.Message}");
                return false;
            }
        }

        // Campaign tick 仅负责资产安全与边界清理，不负责转场或牢房弹窗。
        private void OnTick(float dt)
        {
            try
            {
                TickBelongingsSafety();
                if (Mission.Current != null || !(GameStateManager.Current?.ActiveState is MapState map)
                    || !UiFullScreenHelper.IsCampaignMapScreen())
                {
                    _boundaryStable = false;
                    return;
                }
                if (PlayerDetentionBehavior.IsSentenceRunning(StoryProbeState.Data.DetentionOwnerId)
                    || PlayerDetentionBehavior.IsSentenceCompleted(StoryProbeState.Data.DetentionOwnerId)
                        && StoryProbeState.Data.PendingTransition != 0) return;
                var settlement = Settlement.CurrentSettlement;
                bool outside = settlement == null ? !map.AtMenu
                    : settlement.StringId != StoryProbeState.Data.TransitionSettlementId;
                if (StoryProbeState.Data.PendingTransition == 0 || (!outside && StoryProbeSettings.Enabled))
                {
                    _boundaryStable = false;
                    return;
                }
                // 离城/关闭玩法连续两次战役 tick 才清队列，避开旧 Mission 刚卸载的瞬间。
                if (!_boundaryStable) { _boundaryStable = true; return; }
                AbortPrisonTransition("prologue disabled or left transition settlement");
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] Tick failed: {ex.Message}"); }
        }

        /// <summary>过渡期间不恢复；真实离城、被俘或超时才走原有资产安全归还。</summary>
        private void TickBelongingsSafety()
        {
            var d = StoryProbeState.Data;
            if (!d.Stripped || _opening || d.PendingTransition != 0 || Mission.Current != null
                || PlayerDetentionBehavior.IsSentenceRunning(d.DetentionOwnerId)
                || StoryProbeState.Phase == StoryProbePhase.Jail && d.TutorialLost && !d.JailDone) return;
            if (!(GameStateManager.Current?.ActiveState is MapState)
                || !UiFullScreenHelper.IsCampaignMapScreen()) return;
            var hero = Hero.MainHero;
            if (hero == null) return;
            bool leftTown = Settlement.CurrentSettlement == null
                            || Settlement.CurrentSettlement.StringId != StoryProbeSettings.SettlementId;
            bool prisoner = hero.IsPrisoner;
            bool timeout = d.StripAtHours > 0 && CampaignTime.Now.ToHours - d.StripAtHours > 2.0;
            if (!leftTown && !prisoner && !timeout) return;
            if (_restoreRetryCooldown > 0f) return;
            CancelPrologue($"safety boundary: leftTown={leftTown} prisoner={prisoner} timeout={timeout}");
        }
    }
}
