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
    /// 序章战役入口：城门菜单、真实 prison 转场、牢内换装确认、回门外。
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
        private bool _jailInquiryOpen;
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
                if (!StoryProbeSettings.Enabled || StoryProbeState.Data.CancellationPending) return false;
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

        private static bool IsJailHostReady(StoryProbeMissionBehavior host)
            => CanUseTargetTown() && Mission.Current != null && host != null && host.Active
               && host == StoryProbeMissionBehavior.Current && host.Phase == StoryProbePhase.Jail
               && StoryProbeState.Phase == StoryProbePhase.Jail
               && StoryProbeState.Data.JailSceneEntered && !StoryProbeState.Data.JailDone
               && CampaignMission.Current?.Location?.StringId == "prison";

        /// <summary>只在真实 prison 内醒来后确认换装；拒绝后的重提示由 Host 的场景时钟负责。</summary>
        public static void ShowJailInquiry(bool manual = false)
        {
            var host = StoryProbeMissionBehavior.Current;
            if (!IsJailHostReady(host) || Instance == null || Instance._jailInquiryOpen) return;
            if (IsTransitionUiBlocked()) return;
            int token = StoryProbeState.RuntimeToken;
            var owner = Instance;
            try
            {
                string title = LWNTextHelper.ResolveText("LWN_story_jail_title", "The Cell");
                string body =
                    LWNTextHelper.ResolveText("LWN_story_jail_body_1", "You wake on straw. The back of your head is wet.") + "\n" +
                    LWNTextHelper.ResolveText("LWN_story_jail_body_2", "An old prisoner shoves a bundle at you: a set of old clothes, and a cloth to cover your face.") + "\n" +
                    LWNTextHelper.ResolveText("LWN_story_jail_body_3", "\"You want in? First make sure nobody knows you.\"");
                Instance._jailInquiryOpen = true;
                DebugLogger.Log($"[StoryProbe] jail inquiry shown in prison (manual={manual})");
                InformationManager.ShowInquiry(new InquiryData(
                    title, body, true, true,
                    LWNTextHelper.ResolveText("LWN_story_jail_btn_wear", "Put them on"),
                    LWNTextHelper.ResolveText("LWN_story_jail_btn_wait", "Not yet"),
                    () =>
                    {
                        if (Instance != owner || !StoryProbeState.IsRuntimeCurrent(token)) return;
                        owner._jailInquiryOpen = false;
                        // 同一轮、同一 Host、同一城，且仍在真实牢内才允许结算。
                        if (IsJailHostReady(host)) host.CompleteJailDisguise();
                    },
                    () =>
                    {
                        if (Instance == owner && token == StoryProbeState.RuntimeToken) owner._jailInquiryOpen = false;
                    }), true);
            }
            catch (Exception ex)
            {
                if (Instance != null) Instance._jailInquiryOpen = false;
                DebugLogger.Log($"[StoryProbe] ShowJailInquiry failed: {ex.Message}");
            }
        }

        /// <summary>保留旧入口名，但只排队；旧 Mission 必须真正结束后才自动打开 prison。</summary>
        public static void RequestJailInquiry()
        {
            var d = StoryProbeState.Data;
            if (!CanUseTargetTown() || d.JailDone) return;
            d.TutorialLost = true;
            StoryProbeState.Phase = StoryProbePhase.Jail;
            d.PendingTransition = 1;
            d.TransitionSettlementId = Settlement.CurrentSettlement?.StringId ?? StoryProbeSettings.SettlementId;
            d.StorySettlementId = d.TransitionSettlementId;
            d.JailSceneEntered = false;
            StoryProbeState.EnterViaOption = false;
            if (Instance != null)
            {
                Instance._jailInquiryOpen = false;
                Instance.ResetTransitionRetry();
            }
            DebugLogger.Log($"[StoryProbe] prison transition queued: {d.TransitionSettlementId}");
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
        public static bool CancelPrologue(string reason)
        {
            var d = StoryProbeState.Data;
            bool firstRequest = !d.CancellationPending;
            bool hadEscrow = d.Stripped;
            d.CancellationPending = true;
            if (firstRequest) StoryProbeState.InvalidateRuntime();
            try { StoryProbeMissionBehavior.Current?.CancelRuntime(reason); }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] runtime cancel failed: {ex.Message}"); }
            d.PendingTransition = 0;
            d.TransitionSettlementId = "";
            StoryProbeState.EnterViaOption = false;
            StoryProbeState.EnterJailViaOption = false;
            if (Instance != null)
            {
                Instance._jailInquiryOpen = false;
                Instance.ResetTransitionRetry();
                Instance._restoreRetryCooldown = 2f;
            }
            if ((d.Stripped || !string.IsNullOrEmpty(d.FirstGuardBoostHeroId))
                && (Campaign.Current == null || Campaign.Current.SaveHandler?.IsSaving == true))
            {
                DebugLogger.Log("[StoryProbe] cancellation escrow retry deferred until campaign/save boundary is safe");
                return false;
            }
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
            d.CancellationPending = false;
            d.StorySettlementId = "";
            d.JailSceneEntered = false;
            d.JailDone = false;
            d.TutorialLost = false;
            d.IntroPlayed = false;
            d.GoalReached = false;
            d.GoalStage = "enter";
            d.GateOpened = false;
            d.ChosenApproach = "";
            StoryProbeState.Phase = StoryProbePhase.FirstAttempt;
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
                    || d.PendingTransition != 0 || StoryProbeState.Phase == StoryProbePhase.Jail
                    || d.CancellationPending;
                bool changedTown = !string.IsNullOrEmpty(d.StorySettlementId)
                    && d.StorySettlementId != StoryProbeSettings.SettlementId;
                if (hasWork && (!StoryProbeSettings.Enabled || changedTown || d.CancellationPending))
                {
                    if (_restoreRetryCooldown <= 0f) CancelPrologue("settings changed or outstanding cancellation");
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
                    if (d.PendingTransition != 0 || d.Stripped || StoryProbeState.Phase == StoryProbePhase.Jail)
                    {
                        if (!_boundaryStable) { _boundaryStable = true; return; }
                        AbortPrisonTransition("prologue disabled or transition target changed at menu");
                    }
                    return;
                }
                _boundaryStable = false;
                // 老失败存档曾停在地图 Jail 阶段：恢复真实入牢，不在地图补弹换装。
                if (d.PendingTransition == 0 && StoryProbeState.Phase == StoryProbePhase.Jail && !d.JailDone)
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
                        var prison = settlement.LocationComplex?.GetLocationWithId("prison");
                        var encounter = PlayerEncounter.LocationEncounter;
                        if (prison == null || encounter == null)
                            throw new InvalidOperationException("prison location or active LocationEncounter unavailable");
                        StoryProbeState.Phase = StoryProbePhase.Jail;
                        StoryProbeState.EnterJailViaOption = true;
                        // 原版 indoor 分支依赖静态 LocationEncounter，且丢弃 spawn tag；牢内摆位归 Host 首 tick。
                        opened = encounter.CreateAndOpenMissionController(prison) != null;
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
            if (!CanUseTargetTown() || StoryProbeState.Phase == StoryProbePhase.Done || Mission.Current != null
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
                StoryProbeState.Data.StorySettlementId = settlement.StringId;
                StoryProbeState.EnterViaOption = true;
                const string OutsideSpawnTag = "sp_outside_near_town_main_gate";
                var openedMission = encounter.CreateAndOpenMissionController(location, null, null, OutsideSpawnTag);
                if (openedMission == null) throw new InvalidOperationException("town center mission did not open");
                // 返回的 mission 才是开场回执；EnterViaOption 可能已被同步启动的 Host 消费。
                DebugLogger.Log($"[StoryProbe] entering center of {settlement.StringId} phase={StoryProbeState.Phase}");
                return true;
            }
            catch (Exception ex)
            {
                StoryProbeState.EnterViaOption = false;
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
                _jailInquiryOpen = false;
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
            if (!d.Stripped || _opening || d.PendingTransition != 0 || Mission.Current != null) return;
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
