using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 委托系统的"声音"——让引擎对玩家说话。
    /// 负责：首次介绍、Trust升级通知、难度解锁庆祝、目标预览。
    /// 纯展示层，不包含业务逻辑。
    /// </summary>
    public static class CommissionNarrative
    {
        private static bool _hasIntroduced = false;

        /// <summary>玩家第一次看到委托列表时触发。返回介绍文本，只触发一次。</summary>
        public static string GetIntroduction()
        {
            if (_hasIntroduced) return null;
            _hasIntroduced = true;

            // 首次介绍文本：委托系统说明（玩家第一次打开委托列表）
            return LWNTextHelper.ResolveText("LWN_commission_narrative_introduction",
                "\n📜 Commission System\n\n" +
                "Everyone in this world may need a hand — merchants need escorts, villages need defenders, " +
                "wanderers need allies. Help them solve their problems and you will be rewarded:\n\n" +
                "  💰 Coin rewards — your employer pays a deposit and the final fee based on the difficulty\n" +
                "  🤝 Trust — the more a client trusts you, the lower the deposit and the higher the pay\n" +
                "  ⭐ Rising difficulty — start with simple jobs and unlock riskier, higher-paying commissions\n" +
                "  ⚠️ Note: once accepted, a commission is your responsibility. Fail or run late and the deposit is reclaimed; refuse and you will earn a bad name.\n\n" +
                "—— Good luck, mercenary.\n");
        }

        /// <summary>Trust 跨越等级时返回祝贺文本。null = 没有跨越。</summary>
        public static string CheckTrustMilestone(Hero hero, int oldTrust, int newTrust)
        {
            string oldLevel = TrustSystem.GetTrustDescription(oldTrust);
            string newLevel = TrustSystem.GetTrustDescription(newTrust);
            if (oldLevel == newLevel) return null;

            // 委托人名称兜底：委托人为空时的默认称呼
            string heroName = hero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_commission_narrative_unknown_client", "the client");

            if (newTrust >= 81 && oldTrust < 81)
            {
                // Trust 里程碑庆祝：心腹（定金降至 15%，可同时接 4 个委托）
                return LWNTextHelper.ResolveCompound("LWN_commission_narrative_trust_confidant",
                    "🎖 {HERO} now regards you as a confidant!\n" +
                    "   • Deposit reduced to 15%\n" +
                    "   • Can take on 4 commissions at once\n" +
                    "   • Exclusive high-difficulty quest line opening soon",
                    ("HERO", heroName));
            }
            if (newTrust >= 51 && oldTrust < 51)
            {
                // Trust 里程碑庆祝：信赖之人（定金降至 20%，可同时接 3 个委托）
                return LWNTextHelper.ResolveCompound("LWN_commission_narrative_trust_trusted",
                    "📈 {HERO} now regards you as a trusted one!\n" +
                    "   • Deposit reduced to 20%\n" +
                    "   • Can take on 3 commissions at once\n" +
                    "   • High-difficulty commissions begin to appear",
                    ("HERO", heroName));
            }
            if (newTrust >= 21 && oldTrust < 21)
            {
                // Trust 里程碑庆祝：熟人（定金降至 25%，可同时接 2 个委托）
                return LWNTextHelper.ResolveCompound("LWN_commission_narrative_trust_acquaintance",
                    "👍 {HERO} now regards you as an acquaintance.\n" +
                    "   • Deposit reduced to 25%\n" +
                    "   • Can take on 2 commissions at once",
                    ("HERO", heroName));
            }
            return null;
        }

        /// <summary>难度解锁时返回庆祝文本。</summary>
        public static string CheckTierUnlock(CommissionCategory category,
            CommissionTier oldTier, CommissionTier newTier)
        {
            if (newTier <= oldTier) return null;

            string catName = GetCategoryDisplayName(category);
            string tierDesc = newTier switch
            {
                // 难度标识：普通
                CommissionTier.Skilled => LWNTextHelper.ResolveText("LWN_commission_narrative_tier_skilled", "$$ Normal"),
                // 难度标识：困难
                CommissionTier.Expert => LWNTextHelper.ResolveText("LWN_commission_narrative_tier_expert", "$$$ Hard"),
                // 难度标识：传奇
                CommissionTier.Legendary => LWNTextHelper.ResolveText("LWN_commission_narrative_tier_legendary", "★★★★ Legendary"),
                _ => null
            };
            if (tierDesc == null) return null;

            // 难度解锁庆祝：委托进阶提示（{CATNAME}=分类名，{TIERDESC}=新难度标识）
            return LWNTextHelper.ResolveCompound("LWN_commission_narrative_tier_unlock",
                "🌟 Commission Unlocked!「{CATNAME}」now offers {TIERDESC} difficulty!\n" +
                "   Higher difficulty = higher risk = higher reward.",
                ("CATNAME", catName), ("TIERDESC", tierDesc));
        }

        /// <summary>委托列表顶部显示玩家当前状态。</summary>
        public static string GetPlayerStatusHeader()
        {
            int infamy = InfamySystem.Infamy;
            int activeQuests = CommissionQuest.GetActiveCommissionCount();
            int maxQuests = TrustSystem.GetMaxConcurrentQuests(
                Math.Max(TrustSystem.GetTrust(Hero.MainHero), 20)); // 取所有 NPC 中最高 Trust 的近似值

            // 状态面板标题：你的状态
            string status = LWNTextHelper.ResolveText("LWN_commission_narrative_status_header", "┌─ Your Status ─────────────────\n");
            // 状态面板行：活跃委托数量
            status += LWNTextHelper.ResolveCompound("LWN_commission_narrative_status_active_quests", "│ Active Commissions: {COUNT}\n", ("COUNT", activeQuests.ToString()));

            if (infamy > 0)
                // 状态面板行：恶名值及其等级描述
                status += LWNTextHelper.ResolveCompound("LWN_commission_narrative_status_infamy", "│ Infamy: {COUNT} — {DESC}\n", ("COUNT", infamy.ToString()), ("DESC", InfamySystem.GetDescription()));

            // 显示即将解锁的难度
            var nextUnlocks = GetNextUnlockHints();
            if (!string.IsNullOrEmpty(nextUnlocks))
                // 状态面板行：即将解锁的难度提示
                status += LWNTextHelper.ResolveCompound("LWN_commission_narrative_status_unlock_hint", "│ {HINT}\n", ("HINT", nextUnlocks));

            status += $"└──────────────────────────\n\n";
            return status;
        }

        private static string GetNextUnlockHints()
        {
            // 检查每种委托类型，看哪个最接近解锁下一级
            foreach (var def in CommissionDef.AllDefs)
            {
                var currentTier = CommissionTierProgression.GetAvailableTier(def.Category);
                if (currentTier >= CommissionTier.Legendary) continue;

                int done = CommissionTierProgression.GetCompletionsAtTier(def.Category, currentTier);
                int needed = currentTier switch
                {
                    CommissionTier.Basic => 3,
                    CommissionTier.Skilled => 5,
                    CommissionTier.Expert => 5,
                    _ => 999
                };

                if (done < needed && done > 0)
                {
                    string catShort = def.TitleTemplate.Length > 8
                        ? def.TitleTemplate.Substring(0, 8) + "…"
                        : def.TitleTemplate;
                    var next = currentTier + 1;
                    string nextStr = next == CommissionTier.Skilled ? "$$" :
                                     next == CommissionTier.Expert ? "$$$" : "★★★★";
                    // 解锁进度提示：完成 {DONE}/{NEEDED} 件后解锁 {TIER} 难度
                    return LWNTextHelper.ResolveCompound("LWN_commission_narrative_unlock_progress",
                        "▸ {CATEGORY}: {DONE}/{NEEDED} → unlock {TIER}",
                        ("CATEGORY", catShort), ("DONE", done.ToString()), ("NEEDED", needed.ToString()), ("TIER", nextStr));
                }
            }
            return null;
        }

        private static string GetCategoryDisplayName(CommissionCategory cat)
        {
            switch (cat)
            {
                // 委托分类名：悬赏缉拿
                case CommissionCategory.BountyHunt: return LWNTextHelper.ResolveText("LWN_commission_narrative_cat_bounty_hunt", "Bounty Hunt");
                // 委托分类名：猎杀传奇匪首
                case CommissionCategory.LegendaryHunt: return LWNTextHelper.ResolveText("LWN_commission_narrative_cat_legendary_hunt", "Legendary Bandit Chief Hunt");
                // 委托分类名：清剿匪穴
                case CommissionCategory.HideoutClear: return LWNTextHelper.ResolveText("LWN_commission_narrative_cat_hideout_clear", "Clear Out a Hideout");
                // 委托分类名：护卫商队
                case CommissionCategory.CaravanEscort: return LWNTextHelper.ResolveText("LWN_commission_narrative_cat_caravan_escort", "Escort a Caravan");
                // 委托分类名：限时运粮
                case CommissionCategory.EmergencyDelivery: return LWNTextHelper.ResolveText("LWN_commission_narrative_cat_emergency_delivery", "Timed Grain Delivery");
                // 委托分类名：失物追寻
                case CommissionCategory.LostItem: return LWNTextHelper.ResolveText("LWN_commission_narrative_cat_lost_item", "Find a Lost Item");
                // 委托分类名：寻宝
                case CommissionCategory.TreasureHunt: return LWNTextHelper.ResolveText("LWN_commission_narrative_cat_treasure_hunt", "Treasure Hunt");
                // 委托分类名：寻购名马
                case CommissionCategory.HorseAcquisition: return LWNTextHelper.ResolveText("LWN_commission_narrative_cat_horse_acquisition", "Acquire a Fine Horse");
                // 委托分类名：地下拳赛
                case CommissionCategory.UndergroundFight: return LWNTextHelper.ResolveText("LWN_commission_narrative_cat_underground_fight", "Underground Brawl");
                // 委托分类名：村防应援
                case CommissionCategory.VillageDefense: return LWNTextHelper.ResolveText("LWN_commission_narrative_cat_village_defense", "Village Defense Support");
                // 委托分类名：竞技场特别赛
                case CommissionCategory.ArenaSpecial: return LWNTextHelper.ResolveText("LWN_commission_narrative_cat_arena_special", "Arena Special Tournament");
                // 委托分类名：越狱营救
                case CommissionCategory.PrisonBreak: return LWNTextHelper.ResolveText("LWN_commission_narrative_cat_prison_break", "Prison Break Rescue");
                // 委托分类名：物资截获
                case CommissionCategory.SupplyIntercept: return LWNTextHelper.ResolveText("LWN_commission_narrative_cat_supply_intercept", "Intercept Supplies");
                // 委托分类名：引开追兵
                case CommissionCategory.DecoyMission: return LWNTextHelper.ResolveText("LWN_commission_narrative_cat_decoy_mission", "Distract Pursuers");
                // 委托分类名：紧急供货
                case CommissionCategory.SupplyEmergency: return LWNTextHelper.ResolveText("LWN_commission_narrative_cat_supply_emergency", "Emergency Supply");
                // 委托分类名：跨城代购
                case CommissionCategory.ProcurementAgent: return LWNTextHelper.ResolveText("LWN_commission_narrative_cat_procurement_agent", "Cross-City Procurement");
                // 委托分类名兜底：委托
                default: return LWNTextHelper.ResolveText("LWN_commission_narrative_cat_default", "Commission");
            }
        }

        #region NPC 第一人称叙事（接取 + 结账）

        /// <summary>
        /// 构建委托接取开场叙事（NPC 第一人称）。
        /// 后端已切至 NarrativeResolver（查 Narrative.csv，维度渐进 fallback）。
        /// </summary>
        public static string BuildOpening(CommissionData data, NPCProfile giverProfile)
        {
            // 接取叙事兜底：NPC 请玩家帮忙办一件事
            if (data == null) return LWNTextHelper.ResolveText("LWN_commission_narrative_opening_fallback", "I need someone to handle a matter for me.");
            return NarrativeResolver.GetCommissionOpening(data, giverProfile);
        }

        /// <summary>
        /// 构建委托结账结局叙事（NPC 第一人称）。
        /// 后端已切至 NarrativeResolver。
        /// </summary>
        public static string BuildClosure(CommissionData data, NPCProfile giverProfile,
                                           NPCProfile payerProfile, CommissionGrade grade)
        {
            // 结账叙事兜底：NPC 支付报酬
            if (data == null) return LWNTextHelper.ResolveText("LWN_commission_narrative_closure_fallback", "This is your payment.");
            return NarrativeResolver.GetCommissionClosure(data, giverProfile, payerProfile, grade);
        }

        #endregion

        #region Persistence
        public static string Serialize()
        {
            return _hasIntroduced ? "1" : "0";
        }
        public static void Deserialize(string data)
        {
            _hasIntroduced = data == "1";
        }
        #endregion
    }
}
