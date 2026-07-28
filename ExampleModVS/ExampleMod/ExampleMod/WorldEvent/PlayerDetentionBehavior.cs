using System;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Overlay;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 玩家被当地人扣押（大地图层）。
    ///
    /// 解决什么问题：玩家在定居点里跟村民/守卫动手打输了（AttackTriggerMissionLogic 只负责
    /// "倒地 → 结束 Mission → 菜单落到定居点菜单"），回到大地图后需要一个
    /// "交罚金 / 关几天自己出来 → 案件结案"的闭环。
    /// 原版对这条路径只有 "settlement_player_unconscious"（好心村民把你扶起来，什么都没发生）。
    ///
    /// 不另开自定义开场菜单：赔钱/认罚两个选项**直接注入原版定居点菜单**
    /// （village / town / castle / settlement_player_unconscious），
    /// 玩家爬起来就站在村口的菜单里做选择。
    /// 待选择期间原版选项（敌对行动/四处转转/离开）全部被
    /// <see cref="DetentionMenuLockPatch"/> 藏掉 —— 被按在地上还能抬脚就走会直接出戏，
    /// 也等于让玩家白嫖（案件不结就溜了）；标题和正文由
    /// <see cref="DetentionMenuTextPatch"/> 换成扣押叙事 —— 选项是我们的、
    /// 正文还在念"村民们都忙于农活"同样出戏。保留原版菜单是为了留住定居点的背景图与人物 overlay。
    ///
    /// 复用原版俘虏界面：真正关押走 <see cref="TakePrisonerAction"/> + 原版
    /// "settlement_wait" 等待菜单（背景图/天数文本/时间流逝全是原版的），
    /// 我们只往上加一个"交罚金赎身"选项，并同样换掉标题正文。
    ///
    /// 关键坑：原版 <see cref="PlayerCaptivityCampaignBehavior.CheckCaptivityChange"/> 会
    /// 在"关押方是村庄"或"关押方与玩家不处于战争状态且犯罪值不高"时**立刻**放人
    /// （menu_captivity_end_no_more_enemies）。所以扣押期间必须用 Harmony 前缀把它关掉，
    /// 见 <see cref="PlayerCaptivityCheckSuppressPatch"/>。
    ///
    /// 阶段机：
    ///   0 = 无 / 1 = 待玩家选择（选项挂在定居点菜单上）/ 2 = 关押中 / 3 = 待弹出释放菜单
    /// </summary>
    public class PlayerDetentionBehavior : CampaignBehaviorBase
    {
        public static PlayerDetentionBehavior Instance { get; private set; }

        private const string MENU_RELEASE = "lwn_detention_release";

        /// <summary>关押中复用的原版俘虏等待菜单</summary>
        private const string MENU_WAIT = "settlement_wait";

        /// <summary>选项要注入的原版菜单（玩家倒地后可能落在其中任意一个）</summary>
        private static readonly string[] SETTLEMENT_MENUS =
            { "village", "town", "castle", "settlement_player_unconscious" };

        private const int STAGE_NONE = 0;
        private const int STAGE_OFFER = 1;
        private const int STAGE_DETAINED = 2;
        private const int STAGE_PENDING_RELEASE = 3;

        /// <summary>待弹释放菜单时的重试节流（秒）</summary>
        private const float MENU_RETRY_SECONDS = 0.6f;

        private int _stage;
        private string _settlementId;
        private string _eventId;
        private int _fine;
        private int _days;
        /// <summary>刑期结束的大地图日（CampaignTime.Now.ToDays）</summary>
        private float _releaseDay;
        /// <summary>"fine" = 交了罚金；"served" = 关满出来</summary>
        private string _releaseReason;
        /// <summary>是否真的走了 TakePrisonerAction（决定释放时要不要 EndCaptivity）</summary>
        private bool _jailed;

        private float _menuRetryDelay;

        /// <summary>
        /// 扣押是否正在生效。Harmony 前缀用它压制原版 CheckCaptivityChange
        /// （否则村庄关押会被原版秒放）。
        /// </summary>
        public static bool IsDetentionActive
        {
            get
            {
                var inst = Instance;
                return inst != null && inst._jailed
                    && (inst._stage == STAGE_DETAINED || inst._stage == STAGE_PENDING_RELEASE);
            }
        }

        public PlayerDetentionBehavior()
        {
            Instance = this;
        }

        // ═══════════════════════════════════════════════════════════════
        // 对外入口
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 请求扣押玩家（战斗侧调用：玩家在定居点里被打倒）。
        /// 此时还没进俘虏状态 —— 赔钱/认罚两个选项会出现在定居点菜单上，等玩家自己选。
        /// </summary>
        public static void RequestDetention(Settlement settlement, WorldEvent evt)
        {
            var inst = Instance;
            if (inst == null)
            {
                DebugLogger.Log("[Detention] RequestDetention ignored: behavior not registered");
                return;
            }
            if (inst._stage != STAGE_NONE)
            {
                DebugLogger.Log($"[Detention] RequestDetention ignored: already at stage {inst._stage}");
                return;
            }

            settlement = settlement ?? Settlement.CurrentSettlement ?? Hero.MainHero?.CurrentSettlement;
            if (settlement == null)
            {
                DebugLogger.Log("[Detention] RequestDetention ignored: no settlement context");
                return;
            }

            inst._stage = STAGE_OFFER;
            inst._settlementId = settlement.StringId;
            inst._eventId = evt?.EventId;
            inst._fine = ComputeFine(evt);
            inst._days = ComputeDays(evt);
            inst._releaseDay = 0f;
            inst._releaseReason = null;
            inst._jailed = false;
            inst._menuRetryDelay = 0f;

            DebugLogger.Log($"[Detention] Requested at {settlement.Name} event={inst._eventId ?? "none"} " +
                            $"fine={inst._fine} days={inst._days}{DescribeFineOrigin(evt, inst._fine)}");
        }

        /// <summary>
        /// 直接进入关押（对话里主动认罪/束手就擒的路径，跳过选择）。
        /// </summary>
        public static void ApplyImmediateDetention(Settlement settlement, WorldEvent evt, string reason)
        {
            var inst = Instance;
            if (inst == null || settlement == null)
            {
                DebugLogger.Log($"[Detention] ApplyImmediateDetention ignored ({reason}): inst={inst != null} settlement={settlement?.Name}");
                return;
            }
            if (inst._stage == STAGE_DETAINED || inst._stage == STAGE_PENDING_RELEASE)
            {
                DebugLogger.Log($"[Detention] ApplyImmediateDetention ignored: already detained");
                return;
            }

            inst._stage = STAGE_OFFER;
            inst._settlementId = settlement.StringId;
            inst._eventId = evt?.EventId;
            inst._fine = ComputeFine(evt);
            inst._days = ComputeDays(evt);
            inst._menuRetryDelay = 0f;

            DebugLogger.Log($"[Detention] Immediate ({reason}) at {settlement.Name} event={inst._eventId ?? "none"} " +
                            $"fine={inst._fine} days={inst._days}{DescribeFineOrigin(evt, inst._fine)}");

            inst.StartJail(settlement, $"immediate:{reason}");
        }

        /// <summary>罚金来源的一行日志（对话里报过多少 → 现在收多少 → 中间玩家干了什么）。排查金额争议全靠它。</summary>
        private static string DescribeFineOrigin(WorldEvent evt, int fine)
        {
            if (evt == null) return "";
            string reasons = (evt.PriceEscalationReasons?.Count > 0)
                ? string.Join(" / ", evt.PriceEscalationReasons) : "none";
            return $" | stage={evt.Stage} firstQuote={evt.FirstQuotedAmount}@{evt.FirstQuotedStage} " +
                   $"lastQuote={evt.LastQuotedAmount} escalations=[{reasons}]";
        }

        // ═══════════════════════════════════════════════════════════════
        // 生命周期
        // ═══════════════════════════════════════════════════════════════

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            int stage = _stage;
            string settlementId = _settlementId ?? string.Empty;
            string eventId = _eventId ?? string.Empty;
            int fine = _fine;
            int days = _days;
            float releaseDay = _releaseDay;
            string releaseReason = _releaseReason ?? string.Empty;
            bool jailed = _jailed;

            dataStore.SyncData("lwn_detention_stage", ref stage);
            dataStore.SyncData("lwn_detention_settlement", ref settlementId);
            dataStore.SyncData("lwn_detention_event", ref eventId);
            dataStore.SyncData("lwn_detention_fine", ref fine);
            dataStore.SyncData("lwn_detention_days", ref days);
            dataStore.SyncData("lwn_detention_release_day", ref releaseDay);
            dataStore.SyncData("lwn_detention_release_reason", ref releaseReason);
            dataStore.SyncData("lwn_detention_jailed", ref jailed);

            if (dataStore.IsLoading)
            {
                _stage = stage;
                _settlementId = string.IsNullOrEmpty(settlementId) ? null : settlementId;
                _eventId = string.IsNullOrEmpty(eventId) ? null : eventId;
                _fine = fine;
                _days = days;
                _releaseDay = releaseDay;
                _releaseReason = string.IsNullOrEmpty(releaseReason) ? null : releaseReason;
                _jailed = jailed;
                _menuRetryDelay = 0f;
            }
        }

        private void OnTick(float dt)
        {
            // 只有"待弹释放菜单"需要主动切菜单；待选择阶段的选项是挂在原版定居点菜单上的，
            // 玩家自己走到那儿就能看到，不抢菜单、不打断操作。
            if (_stage != STAGE_PENDING_RELEASE) return;
            if (Campaign.Current == null) return;
            if (TaleWorlds.MountAndBlade.Mission.Current != null) return;   // 场景里不弹菜单
            if (!(Game.Current?.GameStateManager?.ActiveState is MapState)) return;
            if (IsCurrentMenu(MENU_RELEASE)) return;  // 已经停在目标菜单上 → 别每帧重复 SwitchToMenu

            _menuRetryDelay -= dt;
            if (_menuRetryDelay > 0f) return;
            ShowMenu(MENU_RELEASE);
        }

        private static bool IsCurrentMenu(string menuId)
        {
            try { return Campaign.Current?.CurrentMenuContext?.GameMenu?.StringId == menuId; }
            catch { return false; }
        }

        private void OnHourlyTick()
        {
            // 待选择阶段：案件被别的途径了结了（赔款/说服/剧情）→ 撤掉菜单选项，别留个幽灵
            if (_stage == STAGE_OFFER && !string.IsNullOrEmpty(_eventId))
            {
                var pending = WorldEventStore.Find(_eventId);
                if (pending == null || pending.Stage == EventStage.Resolved)
                {
                    DebugLogger.Log($"[Detention] Offer dropped: case {_eventId} no longer open");
                    Cleanup();
                }
                return;
            }

            if (_stage != STAGE_DETAINED) return;

            // 外力（原版/其他 mod/剧情）把玩家放了 → 收尾，别让案件卡住
            if (_jailed && !Hero.MainHero.IsPrisoner)
            {
                DebugLogger.Log("[Detention] Player no longer prisoner (external release) → close case");
                ResolveCase(paidFine: false);
                Cleanup();
                return;
            }

            if ((float)CampaignTime.Now.ToDays >= _releaseDay)
                BeginRelease("served", paidFine: false);
        }

        // ═══════════════════════════════════════════════════════════════
        // 菜单
        // ═══════════════════════════════════════════════════════════════

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            // ── 赔钱 / 认罚：直接注入原版定居点菜单，不另开自定义开场菜单 ──
            // 注意：一个 AddGameMenuOption 只挂一个菜单，而玩家倒地后会落在
            // village / town / castle（按定居点类型）或没抢过原版时的 settlement_player_unconscious，
            // 所以四个菜单各注册一遍，靠 IsOfferVisible() 决定什么时候真的显示。
            // 待选择期间这些菜单上的原版选项由 DetentionMenuLockPatch 全部藏掉。
            foreach (var menu in SETTLEMENT_MENUS)
            {
                starter.AddGameMenuOption(menu, "lwn_detention_pay_fine",
                    "{=!}赔钱了事 —— 交 {LWN_FINE}{GOLD_ICON} 罚金给{LWN_SETTLEMENT}的人。",
                    PayFineOfferOnCondition, PayFineFromOfferOnConsequence,
                    false, 1);

                starter.AddGameMenuOption(menu, "lwn_detention_accept",
                    "{=!}认罚 —— 在{LWN_LOCKUP}里关{LWN_DAYS}天。",
                    AcceptDetentionOnCondition, AcceptDetentionOnConsequence,
                    false, 2);
            }

            // ── 复用原版俘虏等待菜单，只加一个"赎身"选项 ──
            starter.AddGameMenuOption(MENU_WAIT, "lwn_detention_pay_fine_wait",
                "{=!}托人带话，交 {LWN_FINE}{GOLD_ICON} 罚金赎身。",
                PayFineWhileDetainedOnCondition, PayFineWhileDetainedOnConsequence,
                false, 0);

            // ── 放人（叙事收尾，Continue 里干真活）──
            starter.AddGameMenu(MENU_RELEASE, "{=!}{LWN_DETENTION_TEXT}", DetentionReleaseOnInit,
                GameOverlays.MenuOverlayType.None);

            starter.AddGameMenuOption(MENU_RELEASE, "lwn_detention_release_continue", "{=!}继续……",
                ContinueOnCondition, ReleaseContinueOnConsequence);

            DebugLogger.Log("[Detention] Game menus registered");
        }

        private void ShowMenu(string menuId)
        {
            try
            {
                if (Campaign.Current.CurrentMenuContext != null)
                    GameMenu.SwitchToMenu(menuId);
                else
                    GameMenu.ActivateGameMenu(menuId);
                DebugLogger.Log($"[Detention] Menu shown: {menuId}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Detention] ShowMenu({menuId}) failed: {ex.Message}");
                // 弹不出来也不能让阶段机卡死
                _menuRetryDelay = MENU_RETRY_SECONDS;
            }
        }

        /// <summary>待选择阶段的两个选项共用的可见性判断：必须人在案发的那个定居点里</summary>
        private bool IsOfferVisible()
        {
            if (_stage != STAGE_OFFER) return false;
            if (Hero.MainHero.IsPrisoner) return false;
            var here = Settlement.CurrentSettlement;
            return here != null && here.StringId == _settlementId;
        }

        /// <summary>
        /// 待选择期间是否要把这个菜单上的**原版选项全部藏掉**（只留我们的赔钱/认罚）。
        ///
        /// 为什么必须藏：玩家刚被按在地上，菜单里还挂着"敌对行动 / 四处转转 / 离开"
        /// —— 一是直接出戏，二是等于放玩家白嫖（案件不结就能抬脚走人）。
        /// 藏掉原版的 leave 选项后 ESC 也失效（引擎 GetLeaveMenuOption 返回 null，
        /// SandBox.View 那边有 null 检查，不会崩），玩家只能在两个选项里选一个。
        /// "认罚"永远可选（不看钱包），所以不存在锁死。
        ///
        /// 实现见 <see cref="DetentionMenuLockPatch"/>。
        /// </summary>
        public static bool ShouldHideVanillaOptions(string menuId)
        {
            var inst = Instance;
            // 这个判据每帧被所有菜单选项调用，最热的短路放最前
            if (inst == null || inst._stage != STAGE_OFFER) return false;
            if (menuId == null || Array.IndexOf(SETTLEMENT_MENUS, menuId) < 0) return false;
            return inst.IsOfferVisible();
        }

        /// <summary>
        /// 扣押期间接管菜单的**标题与正文**。
        ///
        /// 为什么必须接管：藏掉原版选项只解决了一半 —— 玩家刚被一群人按在地上，
        /// 菜单正文还在念"这个村庄的经济程度一般，人们和牲口看上去都健康强壮，村民们都忙于农活"，
        /// 标题还写着"村庄"。选项是我们的、叙事是原版的，直接出戏。
        ///
        /// 为什么打在 ViewModel 层而不是 <see cref="GameMenu.MenuTitle"/> / <see cref="GameMenu.GetText"/>：
        /// MenuTitle 的私有字段会在 <c>RunOnInit</c> 里被 <c>args.MenuTitle</c> 回写，
        /// 从 getter 改会把我们的标题**持久化进原版菜单对象**，扣押结束后村庄菜单标题还是错的。
        /// 改 VM 的 TitleText/ContextText 只影响这一帧的渲染，零状态泄漏。
        ///
        /// 每帧覆盖：VM 的 setter 自带值相等短路，不会产生额外的属性变更通知。
        /// 挂点见 <see cref="DetentionMenuTextPatch"/>（Priority.First，保证在
        /// <see cref="GameMenuVMFrameTickLoggerPatch"/> 记日志之前就改完，日志记的才是玩家真正看到的字）。
        /// </summary>
        public static void ApplyMenuPresentation(GameMenuVM vm)
        {
            var inst = Instance;
            if (inst == null || inst._stage == STAGE_NONE) return;

            string menuId = vm?.MenuContext?.GameMenu?.StringId;
            if (menuId == null) return;

            if (inst._stage == STAGE_OFFER)
            {
                if (Array.IndexOf(SETTLEMENT_MENUS, menuId) < 0) return;
                if (!inst.IsOfferVisible()) return;
                vm.TitleText = "被制住";
                vm.ContextText = inst.BuildOfferText();
                return;
            }

            if (inst._stage == STAGE_DETAINED && inst._jailed && menuId == MENU_WAIT)
            {
                var settlement = inst.DetentionSettlement;
                vm.TitleText = LockupName(settlement);
                vm.ContextText = inst.BuildDetainedText();
            }
        }

        /// <summary>待选择阶段的正文：刚被按住，两条路摆在面前</summary>
        private string BuildOfferText()
        {
            var settlement = DetentionSettlement;
            string name = settlement?.Name?.ToString() ?? "这里";
            string lockup = LockupName(settlement);

            string text = $"你被人从地上拖起来，胳膊反剪在背后 —— 武器被踢到一边，围上来的人还在喘。\n\n" +
                          $"带头的没跟你废话：钱赔了，这事就算了；不赔，就在{lockup}里待着，" +
                          $"{name}什么时候消气什么时候放人。";

            // 涨价缘由：玩家之前听过一个数，现在要的比那个数高 → 必须当面把账算清，
            // 否则玩家只看到"刚才 680、现在 1652"，会当成 bug 或系统坑人。
            string note = BuildFineEscalationNote();
            if (note != null) text += $"\n\n{note}";

            return text;
        }

        /// <summary>关押阶段的正文：原版 settlement_wait 的"你在此地等待"完全不是这个意思</summary>
        private string BuildDetainedText()
        {
            var settlement = DetentionSettlement;
            string lockup = LockupName(settlement);
            int daysLeft = Math.Max(1, (int)Math.Ceiling(_releaseDay - (float)CampaignTime.Now.ToDays));

            string text = $"{lockup}里没有窗。门外有人来回走动，偶尔停下来往里看一眼。\n\n" +
                          $"你的东西都不在身上了。看这架势还得再关 {daysLeft} 天 —— " +
                          $"除非托人带话回去，把罚金交了。";

            string note = BuildFineEscalationNote();
            if (note != null) text += $"\n\n{note}";

            return text;
        }

        /// <summary>
        /// 罚金涨价说明。null = 没什么要解释的（玩家没听过价，或现价没比听过的高）。
        /// 数据源是案件自己的报价台账（<see cref="WorldEvent.BuildPriceEscalationNote"/>），
        /// 不在这里重算金额 —— 涨价的账必须跟对话里报过的价对得上。
        /// </summary>
        private string BuildFineEscalationNote()
        {
            if (string.IsNullOrEmpty(_eventId)) return null;
            try
            {
                var evt = WorldEventStore.Find(_eventId);
                return evt?.BuildPriceEscalationNote(_fine);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Detention] BuildFineEscalationNote failed: {ex.Message}");
                return null;
            }
        }

        private bool PayFineOfferOnCondition(MenuCallbackArgs args)
        {
            if (!IsOfferVisible()) return false;

            args.optionLeaveType = GameMenuOption.LeaveType.Bribe;
            MBTextManager.SetTextVariable("LWN_FINE", _fine);
            MBTextManager.SetTextVariable("LWN_SETTLEMENT",
                DetentionSettlement?.Name ?? new TextObject("{=!}这里"), false);

            if (Hero.MainHero.Gold < _fine)
            {
                args.IsEnabled = false;
                args.Tooltip = new TextObject("{=!}" + BuildCannotAffordHint());
            }
            return true;
        }

        private bool AcceptDetentionOnCondition(MenuCallbackArgs args)
        {
            if (!IsOfferVisible()) return false;

            args.optionLeaveType = GameMenuOption.LeaveType.Surrender;
            MBTextManager.SetTextVariable("LWN_LOCKUP", LockupName(DetentionSettlement), false);
            MBTextManager.SetTextVariable("LWN_DAYS", _days);
            return true;
        }

        private static bool ContinueOnCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Continue;
            return true;
        }

        /// <summary>关押中出现在原版 settlement_wait 上的赎身选项</summary>
        private bool PayFineWhileDetainedOnCondition(MenuCallbackArgs args)
        {
            if (_stage != STAGE_DETAINED || !_jailed) return false;

            args.optionLeaveType = GameMenuOption.LeaveType.Bribe;
            MBTextManager.SetTextVariable("LWN_FINE", _fine);

            if (Hero.MainHero.Gold < _fine)
            {
                args.IsEnabled = false;
                args.Tooltip = new TextObject("{=!}" + BuildCannotAffordHint());
            }
            return true;
        }

        /// <summary>
        /// 付不起时的灰掉提示。价钱涨过 → 顺手把锚点报出来（"当初 680 就能了事"）。
        /// 玩家鼠标停在灰掉的选项上，问的就是"为什么这么贵"—— 答案得在这里。
        /// </summary>
        private string BuildCannotAffordHint()
        {
            int first = 0;
            try
            {
                if (!string.IsNullOrEmpty(_eventId))
                    first = WorldEventStore.Find(_eventId)?.FirstQuotedAmount ?? 0;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Detention] BuildCannotAffordHint failed: {ex.Message}");
            }

            return (first > 0 && _fine > (int)(first * 1.05f))
                ? $"你身上凑不出这个数。当初赔 {first} 就能了事的。"
                : "你身上凑不出这个数。";
        }

        private void PayFineFromOfferOnConsequence(MenuCallbackArgs args)
        {
            if (!PayFine()) return;
            BeginRelease("fine", paidFine: true);
        }

        private void PayFineWhileDetainedOnConsequence(MenuCallbackArgs args)
        {
            if (!PayFine()) return;
            BeginRelease("fine", paidFine: true);
        }

        private void AcceptDetentionOnConsequence(MenuCallbackArgs args)
        {
            StartJail(DetentionSettlement, "player-accepted");
        }

        private void DetentionReleaseOnInit(MenuCallbackArgs args)
        {
            var settlement = DetentionSettlement;
            string name = settlement?.Name?.ToString() ?? "这里";
            string text;

            if (_releaseReason == "fine")
            {
                text = _jailed
                    ? $"钱货两清。栓门的绳子被解开，你的东西一件件丢回给你 —— 少了什么也没人认。\n\n" +
                      $"{name}的人看着你走出村口，没人吭声。"
                    : $"钱货两清。按着你的手一只只松开，你的东西被丢回脚边。\n\n" +
                      $"{name}的人散了，这事算是了了。";
            }
            else
            {
                text = $"关到第 {_days} 天，看着你的人越来越少 —— 农忙时节，谁也没工夫成天守着一个外乡人。\n\n" +
                       $"天没亮，你自己推开了{LockupName(settlement)}的门。{name}还在睡，没人拦你。";
            }

            MBTextManager.SetTextVariable("LWN_DETENTION_TEXT", text, false);
            args.MenuTitle = new TextObject("{=!}放人");
        }

        private void ReleaseContinueOnConsequence(MenuCallbackArgs args)
        {
            var settlement = DetentionSettlement;
            bool wasJailed = _jailed;
            string reason = _releaseReason;

            Cleanup();

            if (wasJailed)
            {
                try
                {
                    // 与原版 menu_captivity_end_* 同源：EndCaptivity 内部会 ExitToLast 回大地图
                    if (PlayerCaptivity.IsCaptive)
                        PlayerCaptivity.EndCaptivity();
                    else
                        GameMenu.ExitToLast();
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[Detention] EndCaptivity failed: {ex.Message}");
                    try { GameMenu.ExitToLast(); } catch { }
                }
            }
            else
            {
                // 没真进牢房（当场交钱）→ 回定居点菜单
                try { GameMenu.SwitchToMenu(SettlementMenuIdOf(settlement)); }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[Detention] SwitchToMenu(settlement) failed: {ex.Message}");
                    try { GameMenu.ExitToLast(); } catch { }
                }
            }

            InformationManager.DisplayMessage(new InformationMessage(
                reason == "fine" ? "罚金已付，这桩事了了。" : "你从关押里出来了。",
                Colors.Yellow));

            DebugLogger.Log($"[Detention] Released (reason={reason}, wasJailed={wasJailed})");
        }

        // ═══════════════════════════════════════════════════════════════
        // 内部
        // ═══════════════════════════════════════════════════════════════

        private Settlement DetentionSettlement =>
            string.IsNullOrEmpty(_settlementId) ? Settlement.CurrentSettlement : Settlement.Find(_settlementId);

        private static string LockupName(Settlement settlement)
        {
            if (settlement == null) return "柴房";
            if (settlement.IsVillage) return "柴房";
            return "牢房";
        }

        /// <summary>
        /// 该定居点对应的原版菜单 ID。战斗侧倒地后用它做 SetNextMenu 落点
        /// （扣押选项就注入在这几个菜单上）。
        /// </summary>
        public static string SettlementMenuIdOf(Settlement settlement)
        {
            if (settlement == null) return "village";
            if (settlement.IsTown) return "town";
            if (settlement.IsCastle) return "castle";
            return "village";
        }

        /// <summary>真正关起来：走原版俘虏系统 + 原版 settlement_wait 等待界面</summary>
        private void StartJail(Settlement settlement, string reason)
        {
            if (settlement == null)
            {
                DebugLogger.Log($"[Detention] StartJail aborted ({reason}): settlement null");
                Cleanup();
                return;
            }

            try
            {
                TakePrisonerAction.Apply(settlement.Party, Hero.MainHero);
                _jailed = true;
                _stage = STAGE_DETAINED;
                _settlementId = settlement.StringId;
                _releaseDay = (float)CampaignTime.Now.ToDays + Math.Max(1, _days);
                DebugLogger.Log($"[Detention] Jailed at {settlement.Name} ({reason}), release on day {_releaseDay:F1}");

                InformationManager.DisplayMessage(new InformationMessage(
                    $"你被关进了{settlement.Name}的{LockupName(settlement)}。", Colors.Red));

                // 复用原版俘虏等待菜单（背景图/天数文本/时间流逝全是原版的）
                GameMenu.SwitchToMenu(MENU_WAIT);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Detention] StartJail failed ({reason}): {ex.Message}");
                Cleanup();
            }
        }

        /// <summary>扣钱。走 AgentControlHelper 归口（收款方 = 权威 NPC，找不到就是"世界"）。</summary>
        private bool PayFine()
        {
            int amount = Math.Max(0, _fine);
            if (amount > Hero.MainHero.Gold)
            {
                DebugLogger.Log($"[Detention] PayFine aborted: gold={Hero.MainHero.Gold} < fine={amount}");
                return false;
            }

            Hero receiver = null;
            try
            {
                var evt = string.IsNullOrEmpty(_eventId) ? null : WorldEventStore.Find(_eventId);
                receiver = evt != null ? WorldEventStore.GetAuthorityNpc(evt) : null;
                // 兜底：定居点所属家族领袖；都找不到 → null（显式对接"世界"，铁律 4 的收发场景）
                receiver = receiver ?? DetentionSettlement?.OwnerClan?.Leader;
            }
            catch (Exception ex) { DebugLogger.Log($"[Detention] Fine receiver lookup failed: {ex.Message}"); }

            if (amount > 0)
                AgentControlHelper.TransferGold(Hero.MainHero, receiver, amount);

            DebugLogger.Log($"[Detention] Fine paid: {amount} → {receiver?.Name?.ToString() ?? "world"}");
            return true;
        }

        /// <summary>进入"待放人"阶段：先结案，再弹叙事菜单</summary>
        private void BeginRelease(string reason, bool paidFine)
        {
            _releaseReason = reason;
            _stage = STAGE_PENDING_RELEASE;
            _menuRetryDelay = 0f;

            ResolveCase(paidFine);

            // 已经在菜单里（交罚金是在菜单里点的）→ 立刻切；否则等 OnTick
            if (TaleWorlds.MountAndBlade.Mission.Current == null
                && Game.Current?.GameStateManager?.ActiveState is MapState)
                ShowMenu(MENU_RELEASE);
        }

        /// <summary>把对应的 WorldEvent 结案（Resolved + 撤报复部队 + 解除永久敌对）</summary>
        private void ResolveCase(bool paidFine)
        {
            if (string.IsNullOrEmpty(_eventId)) return;
            try
            {
                var evt = WorldEventStore.Find(_eventId);
                if (evt == null)
                {
                    DebugLogger.Log($"[Detention] ResolveCase: event {_eventId} not found");
                    return;
                }
                if (evt.Stage == EventStage.Resolved)
                {
                    DebugLogger.Log($"[Detention] ResolveCase: event {_eventId} already resolved");
                    return;
                }
                WorldEventStore.OnPlayerDetained(evt, paidFine);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Detention] ResolveCase failed: {ex.Message}");
            }
        }

        private void Cleanup()
        {
            _stage = STAGE_NONE;
            _settlementId = null;
            _eventId = null;
            _fine = 0;
            _days = 0;
            _releaseDay = 0f;
            _releaseReason = null;
            _jailed = false;
            _menuRetryDelay = 0f;
        }

        /// <summary>
        /// 扣押罚金。**与 NPC 在对话里讨的赔款走同一个数据源**（<see cref="CostType.Restitution"/>）。
        ///
        /// 为什么不用 <see cref="CostType.Fine"/>：Fine 的基数只看 <c>Severity×2</c> 和
        /// <c>AssaultRestitutionValue</c>，**完全无视赃物市值**。同一桩案子对话里要 616
        /// （438 的赃物 + 2 的身价，×阶段倍率），扣押菜单只要 50（保底价）——
        /// 玩家会立刻发现两套系统各说各话，而且被打倒反而比好好谈便宜十倍。
        ///
        /// Fine 留作地板：纯斗殴、没有财物损失的案子 Restitution 会很低，取两者较大值。
        /// 金额在 RequestDetention 时快照进 _fine，之后菜单/赎身都读它 —— 不会因为案件阶段推进而中途变价。
        ///
        /// **涨价是有意的，但必须说出来**：Restitution 带阶段倍率（Emerging ×0.7 / Active ×1.0 /
        /// Confrontation ×1.7），玩家拒赔又动手，跨两级就是 2.43 倍 —— 对话里听到 680，
        /// 被拖进地牢时变成 1652。设计上这正是"闹大了更贵"的代价，
        /// 但玩家凭空看到翻倍的数字只会当成 bug，所以菜单正文和灰掉提示必须带上
        /// <see cref="WorldEvent.BuildPriceEscalationNote"/>（原价 + 玩家自己干的哪几件事把价钱抬上去的）。
        /// 见 <see cref="BuildFineEscalationNote"/> / <see cref="BuildCannotAffordHint"/>。
        /// </summary>
        private static int ComputeFine(WorldEvent evt)
        {
            int fine;
            try
            {
                if (evt == null)
                {
                    fine = CrimePenaltyCalculator.ComputePenalty(null, PlayerActionType.AttackAlly);
                }
                else
                {
                    int restitution = CrimePenaltyCalculator.ComputeCost(evt, CostType.Restitution);
                    int floor = CrimePenaltyCalculator.ComputeCost(evt, CostType.Fine);
                    fine = Math.Max(restitution, floor);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Detention] ComputeFine failed: {ex.Message}");
                fine = 200;
            }
            return Math.Max(50, fine);
        }

        private static int ComputeDays(WorldEvent evt)
        {
            int severity = evt?.Severity ?? 30;
            int days = 2 + severity / 50;   // 0→2, 50→3, 100→4
            return Math.Min(4, Math.Max(1, days));
        }
    }

    /// <summary>
    /// 扣押期间压制原版俘虏状态检查。
    ///
    /// 原版 CheckCaptivityChange 对"村庄关押"和"非战争状态 + 犯罪值不高的关押方"
    /// 一律走 menu_captivity_end_no_more_enemies **立刻放人**，
    /// 还会随机塞赎金 offer / 越狱判定 —— 全都会打断我们自己的刑期与菜单流程。
    /// </summary>
    [HarmonyPatch(typeof(PlayerCaptivityCampaignBehavior), "CheckCaptivityChange")]
    public static class PlayerCaptivityCheckSuppressPatch
    {
        private static bool Prefix()
        {
            return !PlayerDetentionBehavior.IsDetentionActive;
        }
    }

    /// <summary>
    /// 扣押期间把菜单的标题/正文换成扣押叙事。
    ///
    /// 打在 <see cref="GameMenuVM.OnFrameTick"/> 之后：此时原版已把 TitleText/ContextText
    /// 填成本帧的显示值，我们覆盖掉即可 —— 不碰引擎里的 GameMenu 对象，扣押结束自动恢复原样。
    ///
    /// <see cref="HarmonyPriority"/> = First：必须排在
    /// <see cref="GameMenuVMFrameTickLoggerPatch"/>（Priority.Last）之前，
    /// 否则调试日志记下的是被覆盖前的原版文案，回放时会误判。
    /// </summary>
    [HarmonyPatch(typeof(GameMenuVM), nameof(GameMenuVM.OnFrameTick))]
    public static class DetentionMenuTextPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        private static void Postfix(GameMenuVM __instance)
        {
            try { PlayerDetentionBehavior.ApplyMenuPresentation(__instance); }
            catch (Exception ex) { DebugLogger.Log($"[Detention] MenuText patch error: {ex.Message}"); }
        }
    }

    /// <summary>
    /// 待选择期间锁死定居点菜单：只保留本 mod 的选项（id 以 "lwn_" 开头），
    /// 原版的"敌对行动 / 四处转转 / 离开 / 继续"等一概不显示。
    ///
    /// 为什么打在 <see cref="GameMenuOption.GetConditionsHold"/>：这是引擎判定
    /// "某个选项这一帧要不要出现"的唯一入口（<see cref="GameMenu.GetMenuOptionConditionsHold"/>
    /// 和 <see cref="GameMenu.GetLeaveMenuOption"/> 都走它），一个 Prefix 就能覆盖所有来源的选项
    /// —— 不需要知道是原版还是别的 mod 加的，也不用逐个菜单去改。
    ///
    /// 用 Prefix 而不是 Postfix：直接跳过原版 condition，连它的副作用（设文本变量、
    /// 算价格、刷新 Tooltip）一起省掉。
    /// </summary>
    [HarmonyPatch(typeof(GameMenuOption), "GetConditionsHold")]
    public static class DetentionMenuLockPatch
    {
        private const string OwnOptionPrefix = "lwn_";

        private static bool Prefix(GameMenuOption __instance, MenuContext menuContext, ref bool __result)
        {
            try
            {
                if (__instance == null) return true;

                var id = __instance.IdString;
                if (id != null && id.StartsWith(OwnOptionPrefix, StringComparison.Ordinal))
                    return true;   // 自己的选项照常判定

                if (!PlayerDetentionBehavior.ShouldHideVanillaOptions(menuContext?.GameMenu?.StringId))
                    return true;   // 不在扣押待选择状态 → 完全不干预
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Detention] MenuLock patch error: {ex.Message}");
                return true;       // 出错一律放行，绝不因为这个补丁把菜单锁死
            }

            __result = false;
            return false;
        }
    }
}
