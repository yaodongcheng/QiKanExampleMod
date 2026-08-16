using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 随从逮捕善后（Phase E，2026-08-13，hud-intent-unify-alert-suspect.md §2.8）：
    /// 随从犯法被执法击倒、玩家不调停离场 → MissionEnd 后随从被转押事件定居点牢房
    /// （TakePrisonerAction 原版 hero 俘虏机制，转押动作在 AttackTriggerMissionLogic.TransferArrestedCompanions，
    /// 逮捕瞬间缓存 Hero + Mission 结束只读大地图数据，2026-08-14 重构）。
    ///
    /// 本类 = 释放路径（大地图层）：
    /// 一级菜单：在 village/town/castle 菜单注入「赎回随从」入口 →
    /// 二级菜单（lwn_ransom_menu）：列出该定居点全部被关随从（姓名 + 各自赎金，
    /// 由 CrimePenaltyCalculator.ComputeCost(evt, Restitution) 统一定价，铁律 11）→
    /// AgentControlHelper.TransferGold 扣钱（归口铁律 4，收款方 = 权威 NPC / 世界）→
    /// PrisonRoster.RemoveTroop 释放（hero 回原队伍）→ 事件 Resolved。
    /// 城镇/城堡另有原版地牢交互路径（玩家进地牢，原版 dungeon 机制，实现时验证入口）。
    /// </summary>
    public class CompanionDetentionBehavior : CampaignBehaviorBase
    {
        public static CompanionDetentionBehavior Instance { get; private set; }

        /// <summary>选项注入的原版定居点菜单（与 PlayerDetentionBehavior 同款）。</summary>
        private static readonly string[] SETTLEMENT_MENUS = { "village", "town", "castle" };

        /// <summary>二级菜单 ID：列出该定居点被关随从，逐个赎回。</summary>
        private const string RANSOM_MENU_ID = "lwn_ransom_menu";

        /// <summary>二级菜单最多同时列出的人数（注册时定死，条件按索引实时判定）。</summary>
        private const int MAX_RANSOM_OPTIONS = 8;

        /// <summary>被关随从注册表：每条 = "heroId|settlementId|eventId"（List&lt;string&gt; 存档兼容）。
        /// ⚠️ 非 readonly——SyncData(ref) 存档需要字段可变。</summary>
        private List<string> _entries = new List<string>();
        public CompanionDetentionBehavior()
        {
            Instance = this;
        }

        /// <summary>转押登记（AttackTriggerMissionLogic.TransferArrestedCompanions 调用）。</summary>
        public static void RegisterDetained(Hero hero, Settlement settlement, string eventId)
        {
            var inst = Instance;
            if (inst == null || hero == null || settlement == null) return;
            string key = $"{hero.StringId}|{settlement.StringId}|{eventId ?? ""}";
            if (!inst._entries.Contains(key))
            {
                inst._entries.Add(key);
                DebugLogger.Log($"[CompanionDetention] 登记被关随从: {hero.Name} → {settlement.Name}（事件 {eventId ?? "?"}）");
            }
        }

        /// <summary>
        /// 在押查询（prompt 认知注入用，2026-08-16 用户裁定：被俘随从必须知道自己被俘——实机
        /// 百草药僧在押却答「主公，我在」，完全不知道自己被关着）。返回该随从当前被关押的定居点；
        /// 不在押/被押在移动部队（无法定名）→ null。
        /// 数据源：① mod 登记表 + 实况校验（hero.PartyBelongedToAsPrisoner == 登记 settlement 的 party——
        /// 登记表可能含过期条目，小时清理是异步兜底，实况校验防误报）；② 引擎实况兜底（原版俘虏路径
        /// 未被 mod 登记、但确实被关在定居点的俘虏，同样要知道自己被俘）。
        /// </summary>
        public static Settlement GetDetentionSettlement(Hero hero)
        {
            if (hero == null || Instance == null) return null;
            try
            {
                foreach (var entry in Instance._entries)
                {
                    if (!SplitKey(entry, out var heroId, out var settlementId, out _)) continue;
                    if (heroId != hero.StringId) continue;
                    var s = Settlement.Find(settlementId);
                    if (s != null && hero.PartyBelongedToAsPrisoner == s.Party) return s;
                }
                // 引擎实况兜底：被关在定居点的俘虏（非 mod 转押系统路径）
                var holder = hero.PartyBelongedToAsPrisoner;
                if (holder != null && holder.IsSettlement && holder.Settlement != null)
                    return holder.Settlement;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 该定居点所有**可赎回**的被关随从条目（按登记顺序）。过滤规则与赎回结算一致：
        /// 条目格式合法、hero 存在、事件存在——不满足即不可赎（小时清理会最终移除失效条目）。
        /// </summary>
        private List<string> GetRansomableEntriesIn(Settlement settlement)
        {
            var result = new List<string>();
            if (settlement == null) return result;
            string sid = settlement.StringId;
            foreach (var entry in _entries)
            {
                if (!SplitKey(entry, out var heroId, out var s, out var eventId)) continue;
                if (s != sid) continue;
                if (!EntryIsRansomable(heroId, eventId)) continue;
                result.Add(entry);
            }
            return result;
        }

        /// <summary>条目是否可赎回：hero 活着（小时清理兜底）+ 事件存在（赎金按事件算）。</summary>
        private static bool EntryIsRansomable(string heroId, string eventId)
        {
            if (string.IsNullOrEmpty(heroId)) return false;
            var hero = Hero.FindFirst(h => h.StringId == heroId);
            if (hero == null || !hero.IsAlive) return false;
            if (string.IsNullOrEmpty(eventId)) return false;
            return WorldEventStore.Find(eventId) != null;
        }

        private static bool SplitKey(string entry, out string heroId, out string settlementId, out string eventId)
        {
            heroId = settlementId = eventId = null;
            if (string.IsNullOrEmpty(entry)) return false;
            var parts = entry.Split('|');
            if (parts.Length < 3) return false;
            heroId = parts[0];
            settlementId = parts[1];
            eventId = parts[2];
            return true;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("lwn_companion_detained_entries", ref _entries);
            if (dataStore.IsLoading)
            {
                // 读档清理失效条目（随从已释放/已死亡/事件已结案）
                _entries.RemoveAll(e => !SplitKey(e, out _, out _, out _));
                DebugLogger.Log($"[CompanionDetention] 读档: {_entries.Count} 名随从在押");
            }
        }

        /// <summary>每小时清理：被关随从已不在 prison roster（外部释放/原版地牢救人/死亡）→ 移除条目。</summary>
        private void OnHourlyTick()
        {
            if (_entries.Count == 0) return;
            var stale = new List<string>();
            foreach (var entry in _entries)
            {
                if (!SplitKey(entry, out var heroId, out var settlementId, out _)) { stale.Add(entry); continue; }
                var hero = Hero.FindFirst(h => h.StringId == heroId);
                var settlement = Settlement.Find(settlementId);
                bool stillInJail = hero != null && settlement != null
                    && hero.PartyBelongedToAsPrisoner == settlement.Party;
                if (!stillInJail)
                    stale.Add(entry);
            }
            foreach (var e in stale)
            {
                _entries.Remove(e);
                DebugLogger.Log($"[CompanionDetention] 移除失效条目: {e}");
            }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            // ── 一级入口：village/town/castle 各挂一个「赎回随从」──
            // 条件 = 该定居点有可赎回的被关随从；点击 → 切到二级菜单 lwn_ransom_menu
            foreach (var menu in SETTLEMENT_MENUS)
            {
                starter.AddGameMenuOption(menu, "lwn_companion_ransom",
                    "{=LWN_ui_companion_ransom}Ransom companions.",
                    RansomMenuOnCondition, RansomMenuOnConsequence, false, 1);
            }

            // ── 二级菜单：列出该定居点全部被关随从，每人一个选项（按登记顺序）──
            // 选项在注册时定死 0..MAX_RANSOM_OPTIONS-1，条件按索引实时映射到条目：
            // 赎回一个移除一个，选项自然消失，可连续赎回多人。
            starter.AddGameMenu(RANSOM_MENU_ID,
                "{=LWN_ui_companion_ransom_menu}The guards look you over. In the cells:",
                RansomMenuOnInit);
            for (int i = 0; i < MAX_RANSOM_OPTIONS; i++)
            {
                int index = i;
                starter.AddGameMenuOption(RANSOM_MENU_ID, "lwn_ransom_item_" + i,
                    "{=LWN_ui_companion_ransom_item}Ransom {LWN_COMPANION} — {LWN_COMPANION_FINE}{GOLD_ICON}.",
                    args => RansomItemOnCondition(args, index),
                    args => RansomItemOnConsequence(args, index), false, 0);
            }
            starter.AddGameMenuOption(RANSOM_MENU_ID, "lwn_ransom_back",
                "{=LWN_ui_companion_ransom_back}Leave.",
                RansomBackOnCondition, RansomBackOnConsequence, true, 0);

            DebugLogger.Log("[CompanionDetention] 赎回菜单已注册（village/town/castle → lwn_ransom_menu）");
        }

        /// <summary>二级菜单背景沿用定居点等待场景（同原版劫狱菜单做法），标题用一级入口同名键。</summary>
        private void RansomMenuOnInit(MenuCallbackArgs args)
        {
            try
            {
                args.MenuContext.SetBackgroundMeshName(Settlement.CurrentSettlement?.SettlementComponent?.WaitMeshName);
                args.MenuTitle = new TextObject("{=LWN_ui_companion_ransom}Ransom companions");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[CompanionDetention] RansomMenuOnInit background failed: {ex.Message}");
            }
        }

        /// <summary>一级入口可见性：人在该定居点且该定居点有可赎回的被关随从。</summary>
        private bool RansomMenuOnCondition(MenuCallbackArgs args)
        {
            if (Settlement.CurrentSettlement == null) return false;
            if (GetRansomableEntriesIn(Settlement.CurrentSettlement).Count == 0) return false;
            args.optionLeaveType = GameMenuOption.LeaveType.Bribe;
            return true;
        }

        private void RansomMenuOnConsequence(MenuCallbackArgs args)
        {
            try { GameMenu.SwitchToMenu(RANSOM_MENU_ID); }
            catch (Exception ex) { DebugLogger.Log($"[CompanionDetention] open ransom menu failed: {ex.Message}"); }
        }

        /// <summary>二级选项可见性：该索引对应一个可赎回条目（付不起则灰掉并提示）。</summary>
        private bool RansomItemOnCondition(MenuCallbackArgs args, int index)
        {
            var settlement = Settlement.CurrentSettlement;
            if (settlement == null) return false;
            var entries = GetRansomableEntriesIn(settlement);
            if (index < 0 || index >= entries.Count) return false;
            if (!SplitKey(entries[index], out var heroId, out _, out var eventId)) return false;

            var hero = Hero.FindFirst(h => h.StringId == heroId);
            var evt = WorldEventStore.Find(eventId);
            if (hero == null || evt == null) return false;

            int fine = Math.Max(50, CrimePenaltyCalculator.ComputeCost(evt, CostType.Restitution));
            args.optionLeaveType = GameMenuOption.LeaveType.Bribe;
            // 🔴 2026-08-15（多条目显示串名根因修复，反编译实锤）：选项动态文本的正确机制 =
            // 对 args.Text（该选项自己的 TextObject 实例，MenuCallbackArgs(menuContext, Text) 传入）
            // 调**实例级** SetTextVariable——渲染时 TextObject 先查实例变量表再查全局表，
            // 多选项实例变量独立互不干扰（修复 08:42 两个「赎回阿速甘」的全局变量覆盖）。
            // ⚠️ 错误做法一：MBTextManager.SetTextVariable（全局表 → 多选项互相覆盖）；
            // ⚠️ 错误做法二：args.Text = new TextObject(...)（引擎不回读 args.Text，选项文本永远是注册时实例）。
            args.Text.SetTextVariable("LWN_COMPANION", hero.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_ui_name_target", "target"));
            args.Text.SetTextVariable("LWN_COMPANION_FINE", fine.ToString());

            if (Hero.MainHero.Gold < fine)
            {
                args.IsEnabled = false;
                // 付不起赎金提示
                args.Tooltip = new TextObject(LWNTextHelper.ResolveText("LWN_ui_detention_cannot_afford", "You cannot scrape together that much."));
            }
            return true;
        }

        /// <summary>二级选项结算：扣钱（归口）→ PrisonRoster.RemoveTroop 释放（hero 回原队伍）→ 事件 Resolved。</summary>
        private void RansomItemOnConsequence(MenuCallbackArgs args, int index)
        {
            var settlement = Settlement.CurrentSettlement;
            if (settlement == null) return;
            var entries = GetRansomableEntriesIn(settlement);
            if (index < 0 || index >= entries.Count) return;
            if (!SplitKey(entries[index], out var heroId, out _, out var eventId)) return;

            var hero = Hero.FindFirst(h => h.StringId == heroId);
            var evt = WorldEventStore.Find(eventId);
            if (hero == null || evt == null) return;

            try
            {
                int fine = Math.Max(50, CrimePenaltyCalculator.ComputeCost(evt, CostType.Restitution));
                if (fine > Hero.MainHero.Gold)
                {
                    DebugLogger.Log($"[CompanionDetention] 赎回失败: gold={Hero.MainHero.Gold} < fine={fine}");
                    return;
                }

                // 扣钱：收款方 = 权威 NPC，找不到 → 显式对接「世界」（铁律 4 的收发场景）
                Hero receiver = null;
                try { receiver = WorldEventStore.GetAuthorityNpc(evt) ?? settlement.OwnerClan?.Leader; }
                catch (Exception ex) { DebugLogger.Log($"[CompanionDetention] receiver lookup failed: {ex.Message}"); }
                AgentControlHelper.TransferGold(Hero.MainHero, receiver, fine);

                // ── 释放序列（M1 修复 2026-08-14，npc-risk-aware-planning.md）──
                // 反编译实锤：PrisonRoster.RemoveTroop 只是计数操作，不会自动回原队伍；
                // hero 入狱瞬间就被引擎从玩家队伍摘除（PartyBelongedTo = null）。
                // 官方释放语义（EndCaptivityAction）：ChangeState(Released) 必须，否则英雄卡 Prisoner 状态。
                // 反编译确认 Hero.ChangeState 无状态机校验（直接换状态 + 广播），Prisoner → Released 合法。
                try { hero.ChangeState(Hero.CharacterStates.Released); } catch (Exception ex) { DebugLogger.Log($"[CompanionDetention] ChangeState(Released) failed: {ex.Message}"); }
                settlement.Party.PrisonRoster.RemoveTroop(hero.CharacterObject, 1);
                // 立即归队（交付感：玩家赎回 = 当场领人；不走原版 fugitive 潜逃数日——候选方案已否决）
                if (hero.PartyBelongedTo == null)
                {
                    MobileParty.MainParty.MemberRoster.AddToCounts(hero.CharacterObject, 1);
                    // AddToCounts 走 OnHeroAdded → PartyBelongedTo = MainParty（反编译确认）
                    DebugLogger.Log($"[CompanionDetention] {hero.Name} 已归队（MemberRoster.AddToCounts，State={hero.HeroState}）");
                }
                _entries.Remove(entries[index]);
                // 事件结案（随从已赎出，案件了结）
                if (evt.Stage != EventStage.Resolved)
                    WorldEventStore.TransitionStage(evt, EventStage.Resolved, null, "companion_ransomed");
                InformationManager.DisplayMessage(new InformationMessage(
                    // 本地化：LWN_ui_companion_ransom_ack（玩家可见文本）
                    LWNTextHelper.ResolveCompound("LWN_ui_companion_ransom_ack",
                        "You pay the fine and take {NAME} back.",
                        // 本地化：LWN_ui_name_target（玩家可见文本）
                        ("NAME", hero.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_ui_name_target", "target"))),
                    Colors.Yellow));
                DebugLogger.Log($"[CompanionDetention] 赎回 {hero.Name}：罚款 {fine} → {receiver?.Name?.ToString() ?? "world"}，事件 {evt.EventId} Resolved");

                // 🔴 2026-08-15（赎回后菜单不实时更新，实机）：结算后重进菜单——选项条件实时重算，
                // 已赎条目立即消失；仍有可赎 → 重进二级菜单（列表刷新，可连续赎回）；无 → 退回一级菜单。
                // 旧实现结算后菜单选项是打开时构建的旧快照，玩家要退出重进才看到更新。
                try
                {
                    GameMenu.SwitchToMenu(GetRansomableEntriesIn(settlement).Count > 0
                        ? RANSOM_MENU_ID
                        : PlayerDetentionBehavior.SettlementMenuIdOf(settlement));
                }
                catch (Exception ex) { DebugLogger.Log($"[CompanionDetention] 赎回后菜单刷新失败: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[CompanionDetention] 赎回失败: {ex.Message}");
            }
        }
        private static bool RansomBackOnCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Leave;
            return true;
        }
        private void RansomBackOnConsequence(MenuCallbackArgs args)
        {
            try
            {
                GameMenu.SwitchToMenu(PlayerDetentionBehavior.SettlementMenuIdOf(Settlement.CurrentSettlement));
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[CompanionDetention] back to settlement menu failed: {ex.Message}");
            }
        }
    }
}