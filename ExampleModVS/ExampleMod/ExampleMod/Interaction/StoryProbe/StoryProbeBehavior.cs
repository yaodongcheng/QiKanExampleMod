using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 序章的战役层（2026-11-12，plans/逆水寒511玩法验证-城池剧情任务.md §3）。
    ///
    /// 职责：
    ///   ① 在目标城的城镇菜单加一条「入城（玩法测试）」——**默认不出现**，只在目标城 + 开关打开时出现；
    ///   ② 第一次进场前的弹窗（城门 / 4 句背景 /【硬闯】【再想想】）；
    ///   ③ 出狱弹窗（旧衣 + 面罩 /【换上】【我再想想】）—— 只在战役层出现，Mission 已结束；
    ///   ④ 进场景（原版 town_center 的同一条路，只是先立 EnterViaOption 标记）。
    ///
    /// 🔴 原版「在城里逛逛」永远不加载序章：判据是 `StoryProbeState.EnterViaOption`（只有这条菜单会立）。
    /// </summary>
    public class StoryProbeBehavior : CampaignBehaviorBase
    {
        public static StoryProbeBehavior Instance { get; private set; }

        private float _nextJailPromptAt = -1f;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
        }

        /// <summary>存档统一走 MyBehavior.SyncData 的 `lwn_story_probe`（本行为自身无字段要存）。</summary>
        public override void SyncData(IDataStore dataStore) { }

        // ═══════════════════════════════════════════════════════════════
        // 菜单
        // ═══════════════════════════════════════════════════════════════

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
                if (!StoryProbeSettings.Enabled) return false;

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
                    ShowJailInquiry(manual: true);
                    break;
                default:
                    EnterTownCenter();
                    break;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 弹窗
        // ═══════════════════════════════════════════════════════════════

        /// <summary>第一次进城的弹窗（§3.2.1）：4 句背景 +【硬闯】【再想想】。</summary>
        public static void ShowIntroInquiry()
        {
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
                    title, body,
                    true, true,
                    LWNTextHelper.ResolveText("LWN_story_intro_btn_go", "Force your way in"),
                    LWNTextHelper.ResolveText("LWN_story_intro_btn_back", "Think again"),
                    () => EnterTownCenter(),
                    () => { }), true);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] ShowIntroInquiry failed: {ex.Message}");
            }
        }

        /// <summary>出狱弹窗（§3.2.2）：换旧衣 + 面罩（没收装备与钱）。</summary>
        public static void ShowJailInquiry(bool manual = false)
        {
            try
            {
                string title = LWNTextHelper.ResolveText("LWN_story_jail_title", "The Cell");
                string body =
                    LWNTextHelper.ResolveText("LWN_story_jail_body_1", "You wake on straw. The back of your head is wet.") + "\n" +
                    LWNTextHelper.ResolveText("LWN_story_jail_body_2", "An old prisoner shoves a bundle at you: a set of old clothes, and a cloth to cover your face.") + "\n" +
                    LWNTextHelper.ResolveText("LWN_story_jail_body_3", "\"You want in? First make sure nobody knows you.\"");

                DebugLogger.Log($"[StoryProbe] jail inquiry shown (manual={manual})");
                InformationManager.ShowInquiry(new InquiryData(
                    title, body,
                    true, true,
                    LWNTextHelper.ResolveText("LWN_story_jail_btn_wear", "Put them on"),
                    LWNTextHelper.ResolveText("LWN_story_jail_btn_wait", "Not yet"),
                    () =>
                    {
                        StoryProbeMissionBehavior.StripPlayer();
                        if (Instance != null) Instance._nextJailPromptAt = -1f;
                    },
                    () =>
                    {
                        // 不锁死：过几秒再弹（§3.2.2 点【再想想】→ 弹窗再出现）
                        if (Instance != null) Instance._nextJailPromptAt = (float)CampaignTime.Now.ToSeconds + 4f;
                    }), true);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] ShowJailInquiry failed: {ex.Message}");
            }
        }

        /// <summary>Mission 侧强制失败结束后调用（战役层负责弹框）。</summary>
        public static void RequestJailInquiry()
        {
            if (Instance != null) Instance._nextJailPromptAt = -1f;
            DebugLogger.Log("[StoryProbe] jail inquiry requested");
        }

        // ═══════════════════════════════════════════════════════════════
        // 进场景
        // ═══════════════════════════════════════════════════════════════

        public static void EnterTownCenter()
        {
            if (StoryProbeState.Phase == StoryProbePhase.Done) return;
            if (StoryProbeState.Phase == StoryProbePhase.Jail)
            {
                ShowJailInquiry(manual: true);
                return;
            }

            try
            {
                var settlement = Settlement.CurrentSettlement;
                if (settlement == null)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveText("LWN_story_err_not_in_town", "You are not in a town.")));
                    return;
                }

                var location = LocationComplex.Current?.GetLocationWithId("center")
                            ?? settlement.LocationComplex?.GetLocationWithId("center");
                if (location == null)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveText("LWN_story_err_no_center", "This town has no center location.")));
                    return;
                }

                StoryProbeState.EnterViaOption = true;

                // 🔴 2026-11-13 实机数据核对：原版城镇中心的默认出生点是 `sp_player`（480.1,367.4），
                //    **已经站在城门里面**；序章要求玩家从门外来，所以必须显式指定出门点。
                //    该 tag 在 empire_town_g 里存在（435.4,386.9）；别的城没有时会安全回落到默认点
                //    （反编译 MissionAgentHandler.SpawnPlayer：找不到特殊 tag 就用 `spawnpoint_player`）。
                const string OutsideSpawnTag = "sp_outside_near_town_main_gate";

                var encounter = PlayerEncounter.LocationEncounter;
                if (encounter != null)
                {
                    encounter.CreateAndOpenMissionController(location, null, null, OutsideSpawnTag);
                }
                else
                {
                    new TownEncounter(settlement).CreateAndOpenMissionController(location, null, null, OutsideSpawnTag);
                }
                DebugLogger.Log($"[StoryProbe] entering center of {settlement.StringId} phase={StoryProbeState.Phase}");
            }
            catch (Exception ex)
            {
                StoryProbeState.EnterViaOption = false;
                DebugLogger.Log($"[StoryProbe] EnterTownCenter failed: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Tick：出狱弹窗（只在战役层、只在本城）
        // ═══════════════════════════════════════════════════════════════

        private void OnTick(float dt)
        {
            try
            {
                if (!StoryProbeSettings.Enabled) return;

                TickBelongingsSafety();

                if (StoryProbeState.Phase != StoryProbePhase.Jail) return;
                if (StoryProbeState.Data.JailDone) return;

                // Mission 里不弹（战斗/对话中弹窗会打断演出的）
                if (Mission.Current != null) return;
                if (!(GameStateManager.Current?.ActiveState is MapState)) return;

                // 🔴 R2（2026-11-13 外部审查属实）：这里**不能**再要求"必须站在目标城的菜单里"。
                //    只要 EndMission 之后 CurrentSettlement 为空（或被别的东西清掉），
                //    玩家就会裸装站在地图上、牢房弹窗永不出现。弹窗只和"阶段"绑定。
                float now = (float)CampaignTime.Now.ToSeconds;
                if (_nextJailPromptAt > 0f && now < _nextJailPromptAt) return;

                // 第一次进入 Jail 阶段时自动弹（不需要玩家再点菜单）
                _nextJailPromptAt = now + 6f;
                ShowJailInquiry();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] Tick failed: {ex.Message}");
            }
        }

        /// <summary>
        /// R3（2026-11-13 外部审查属实）：剥夺后的**兜底归还**。
        /// 一旦满足下面任一条，就把钱与装备还回去，避免"卡住 = 存档永久裸装"：
        ///   · 玩家离开了目标城（放弃序章）；· 玩家被俘/入狱；· 剥夺超过 2 个游戏小时。
        /// 归还后阶段不变（回来还能接着玩），只保证不会永久损失。
        /// </summary>
        private void TickBelongingsSafety()
        {
            var d = StoryProbeState.Data;
            if (!d.Stripped) return;
            if (Mission.Current != null) return;

            var hero = Hero.MainHero;
            if (hero == null) return;

            bool leftTown = Settlement.CurrentSettlement == null
                            || Settlement.CurrentSettlement.StringId != StoryProbeSettings.SettlementId;
            bool prisoner = hero.IsPrisoner;
            bool timeout = d.StripAtHours > 0 && CampaignTime.Now.ToHours - d.StripAtHours > 2.0;

            if (!leftTown && !prisoner && !timeout) return;

            StoryProbeMissionBehavior.RestorePlayerBelongings();
            InformationManager.DisplayMessage(new InformationMessage(
                LWNTextHelper.ResolveText("LWN_story_restored_safety",
                    "They give you back what was taken — the prologue is over for now.")));
            DebugLogger.Log($"[StoryProbe] safety restore: leftTown={leftTown} prisoner={prisoner} timeout={timeout}");
        }
    }
}
