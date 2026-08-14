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
    /// （TakePrisonerAction 原版 hero 俘虏机制，转押动作在 AgentAIController.TransferArrestedCompanionsToJail）。
    ///
    /// 本类 = 释放路径（大地图层）：
    /// 在 village/town/castle 菜单注入「赎回随从」选项——
    /// CrimePenaltyCalculator.ComputeCost(evt, Restitution) 统一定价（铁律 11）→
    /// AgentControlHelper.TransferGold 扣钱（归口铁律 4，收款方 = 权威 NPC / 世界）→
    /// PrisonRoster.RemoveTroop 释放（hero 回原队伍）→ 事件 Resolved。
    /// 城镇/城堡另有原版地牢交互路径（玩家进地牢，原版 dungeon 机制，实现时验证入口）。
    /// </summary>
    public class CompanionDetentionBehavior : CampaignBehaviorBase
    {
        public static CompanionDetentionBehavior Instance { get; private set; }

        /// <summary>选项注入的原版定居点菜单（与 PlayerDetentionBehavior 同款）。</summary>
        private static readonly string[] SETTLEMENT_MENUS = { "village", "town", "castle" };

        /// <summary>被关随从注册表：每条 = "heroId|settlementId|eventId"（List&lt;string&gt; 存档兼容）。
        /// ⚠️ 非 readonly——SyncData(ref) 存档需要字段可变。</summary>
        private List<string> _entries = new List<string>();
        public CompanionDetentionBehavior()
        {
            Instance = this;
        }

        /// <summary>转押登记（AgentAIController.TransferArrestedCompanionsToJail 调用）。</summary>
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

        /// <summary>该定居点是否有被关的随从（菜单选项可见性）。</summary>
        private bool HasDetainedIn(Settlement settlement)
        {
            if (settlement == null) return false;
            string sid = settlement.StringId;
            return _entries.Any(e => SplitKey(e, out _, out var s, out _) && s == sid);
        }

        /// <summary>取该定居点的第一个被关随从条目（每次赎回一个，释放后下一个自然顶上）。</summary>
        private string FindEntryIn(Settlement settlement)
        {
            if (settlement == null) return null;
            string sid = settlement.StringId;
            return _entries.FirstOrDefault(e => SplitKey(e, out _, out var s, out _) && s == sid);
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
            // 注入「赎回随从」选项：village/town/castle 各挂一份（仿 PlayerDetentionBehavior 菜单注入模式）
            foreach (var menu in SETTLEMENT_MENUS)
            {
                starter.AddGameMenuOption(menu, "lwn_companion_ransom",
                    "{=LWN_ui_companion_ransom}Ransom {LWN_COMPANION} — {LWN_COMPANION_FINE}{GOLD_ICON}.",
                    RansomOnCondition, RansomOnConsequence, false, 1);
            }
            DebugLogger.Log("[CompanionDetention] 赎回菜单已注册（village/town/castle）");
        }

        /// <summary>选项可见性：人在该定居点且该定居点有被关随从（付不起罚款则灰掉并提示）。</summary>
        private bool RansomOnCondition(MenuCallbackArgs args)
        {
            var settlement = Settlement.CurrentSettlement;
            if (settlement == null) return false;
            string entry = FindEntryIn(settlement);
            if (entry == null) return false;
            if (!SplitKey(entry, out var heroId, out _, out var eventId)) return false;

            var hero = Hero.FindFirst(h => h.StringId == heroId);
            var evt = string.IsNullOrEmpty(eventId) ? null : WorldEventStore.Find(eventId);
            if (hero == null || evt == null) return false;

            int fine = Math.Max(50, CrimePenaltyCalculator.ComputeCost(evt, CostType.Restitution));
            args.optionLeaveType = GameMenuOption.LeaveType.Bribe;
            MBTextManager.SetTextVariable("LWN_COMPANION", hero.Name, false);
            MBTextManager.SetTextVariable("LWN_COMPANION_FINE", fine);

            if (Hero.MainHero.Gold < fine)
            {
                args.IsEnabled = false;
                // 付不起赎金提示
                args.Tooltip = new TextObject(LWNTextHelper.ResolveText("LWN_ui_detention_cannot_afford", "You cannot scrape together that much."));
            }
            return true;
        }

        /// <summary>赎回结算：扣钱（归口）→ PrisonRoster.RemoveTroop 释放（hero 回原队伍）→ 事件 Resolved。</summary>
        private void RansomOnConsequence(MenuCallbackArgs args)
        {
            var settlement = Settlement.CurrentSettlement;
            if (settlement == null) return;
            string entry = FindEntryIn(settlement);
            if (entry == null) return;
            if (!SplitKey(entry, out var heroId, out _, out var eventId)) return;

            var hero = Hero.FindFirst(h => h.StringId == heroId);
            var evt = string.IsNullOrEmpty(eventId) ? null : WorldEventStore.Find(eventId);
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

                // 释放：从牢房移除（hero 自动回原队伍——原版 prisoner roster 移除语义）
                settlement.Party.PrisonRoster.RemoveTroop(hero.CharacterObject, 1);
                _entries.Remove(entry);

                // 事件结案（随从已赎出，案件了结）
                if (evt.Stage != EventStage.Resolved)
                    WorldEventStore.TransitionStage(evt, EventStage.Resolved, null, "companion_ransomed");

                InformationManager.DisplayMessage(new InformationMessage(
                    LWNTextHelper.ResolveCompound("LWN_ui_companion_ransom_ack",
                        "You pay the fine and take {NAME} back.",
                        ("NAME", hero.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_ui_name_target", "target"))),
                    Colors.Yellow));
                DebugLogger.Log($"[CompanionDetention] 赎回 {hero.Name}：罚款 {fine} → {receiver?.Name?.ToString() ?? "world"}，事件 {evt.EventId} Resolved");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[CompanionDetention] 赎回失败: {ex.Message}");
            }
        }
    }
}
