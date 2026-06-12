using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

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

            return "\n📜 委托任务系统\n\n" +
                   "卡拉迪亚的每个人都可能有求于人——商人需要护卫、村庄需要防御、" +
                   "浪人需要帮手。帮他们解决问题，你将获得：\n\n" +
                   "  💰 金币报酬——委托人会根据任务难度支付定金和尾款\n" +
                   "  🤝 信任积累——同一个委托人越信任你，定金越低、报酬越高\n" +
                   "  ⭐ 难度递进——从简单活计开始，逐步解锁高风险高回报的委托\n" +
                   "  ⚠️ 注意：接了委托就要负责。失败或超时会追讨定金，拒还会背上恶名。\n\n" +
                   "—— 祝你好运，佣兵。\n";
        }

        /// <summary>Trust 跨越等级时返回祝贺文本。null = 没有跨越。</summary>
        public static string CheckTrustMilestone(Hero hero, int oldTrust, int newTrust)
        {
            string oldLevel = TrustSystem.GetTrustDescription(oldTrust);
            string newLevel = TrustSystem.GetTrustDescription(newTrust);
            if (oldLevel == newLevel) return null;

            string heroName = hero?.Name?.ToString() ?? "委托人";

            if (newTrust >= 81 && oldTrust < 81)
            {
                return $"🎖 {heroName} 现在视你为 心腹！\n" +
                       "   • 定金降至 15%\n" +
                       "   • 可同时接 4 个委托\n" +
                       "   • 专属高难度任务线即将开放";
            }
            if (newTrust >= 51 && oldTrust < 51)
            {
                return $"📈 {heroName} 现在视你为 信赖之人！\n" +
                       "   • 定金降至 20%\n" +
                       "   • 可同时接 3 个委托\n" +
                       "   • 高难度委托开始出现";
            }
            if (newTrust >= 21 && oldTrust < 21)
            {
                return $"👍 {heroName} 现在视你为 熟人。\n" +
                       "   • 定金降至 25%\n" +
                       "   • 可同时接 2 个委托";
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
                CommissionTier.Skilled => "$$ 普通",
                CommissionTier.Expert => "$$$ 困难",
                CommissionTier.Legendary => "★★★★ 传奇",
                _ => null
            };
            if (tierDesc == null) return null;

            return $"🌟 委托进阶！「{catName}」解锁 {tierDesc} 难度！\n" +
                   "   更高难度 = 更高风险 = 更高报酬。";
        }

        /// <summary>委托列表顶部显示玩家当前状态。</summary>
        public static string GetPlayerStatusHeader()
        {
            int infamy = InfamySystem.Infamy;
            int activeQuests = CommissionQuest.GetActiveCommissionCount();
            int maxQuests = TrustSystem.GetMaxConcurrentQuests(
                Math.Max(TrustSystem.GetTrust(Hero.MainHero), 20)); // 取所有 NPC 中最高 Trust 的近似值

            string status = $"┌─ 你的状态 ─────────────────\n";
            status += $"│ 活跃委托：{activeQuests} 个\n";

            if (infamy > 0)
                status += $"│ 恶名：{infamy} — {InfamySystem.GetDescription()}\n";

            // 显示即将解锁的难度
            var nextUnlocks = GetNextUnlockHints();
            if (!string.IsNullOrEmpty(nextUnlocks))
                status += $"│ {nextUnlocks}\n";

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
                    return $"▸ {catShort}：{done}/{needed} → 解锁{nextStr}";
                }
            }
            return null;
        }

        private static string GetCategoryDisplayName(CommissionCategory cat)
        {
            switch (cat)
            {
                case CommissionCategory.BountyHunt: return "悬赏缉拿";
                case CommissionCategory.LegendaryHunt: return "猎杀传奇匪首";
                case CommissionCategory.HideoutClear: return "清剿匪穴";
                case CommissionCategory.CaravanEscort: return "护卫商队";
                case CommissionCategory.EmergencyDelivery: return "限时运粮";
                case CommissionCategory.LostItem: return "失物追寻";
                case CommissionCategory.TreasureHunt: return "寻宝";
                case CommissionCategory.HorseAcquisition: return "寻购名马";
                case CommissionCategory.UndergroundFight: return "地下拳赛";
                case CommissionCategory.VillageDefense: return "村防应援";
                case CommissionCategory.ArenaSpecial: return "竞技场特别赛";
                case CommissionCategory.PrisonBreak: return "越狱营救";
                case CommissionCategory.SupplyIntercept: return "物资截获";
                case CommissionCategory.DecoyMission: return "引开追兵";
                case CommissionCategory.SupplyEmergency: return "紧急供货";
                case CommissionCategory.ProcurementAgent: return "跨城代购";
                default: return "委托";
            }
        }

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
