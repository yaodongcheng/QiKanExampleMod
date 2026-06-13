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

            return "\n📜 委托任务系统\n\n" +
                   "世界的每个人都可能有求于人——商人需要护卫、村庄需要防御、" +
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

        #region NPC 第一人称叙事（接取 + 结账）

        /// <summary>
        /// 构建委托接取开场叙事（NPC 第一人称）。
        /// 从 CommissionNarrative.csv 中按 Category + 性格 + 信任 匹配模板，替换占位符。
        /// </summary>
        public static string BuildOpening(CommissionData data, NPCProfile giverProfile)
        {
            if (data == null) return "我需要有人帮我办一件事。";
            return ResolveNarrative(data, giverProfile, "Opening", CommissionGrade.Passable);
        }

        /// <summary>
        /// 构建委托结账结局叙事（NPC 第一人称）。
        /// 从 CommissionNarrative.csv 中按 Category + 性格 + 信任 + 评级 匹配模板。
        /// </summary>
        public static string BuildClosure(CommissionData data, NPCProfile giverProfile,
                                           NPCProfile payerProfile, CommissionGrade grade)
        {
            if (data == null) return "这是你的报酬。";

            string text = ResolveNarrative(data, giverProfile, "Closure", grade);

            // 如果结账人 ≠ 委托人，追加 payer 角度的台词
            if (payerProfile != null && giverProfile != null &&
                payerProfile.BaseHero != giverProfile.BaseHero)
            {
                string payerName = payerProfile.BaseHero?.Name?.ToString() ?? "结账人";
                text += $"（{payerName}代为转交了报酬。）";
            }
            return text;
        }

        /// <summary>
        /// 内部模板查表 + 替换逻辑。
        /// 优先级：Category 精确 > PersonalityTrait 精确 > Trust 区间 > Grade（仅 Closure）> 随机选一。
        /// </summary>
        private static string ResolveNarrative(CommissionData data, NPCProfile profile,
                                                string phase, CommissionGrade grade)
        {
            var table = GameDatabase.CommissionNarrative;
            if (table == null) return GetFallbackText(data, phase, grade);

            var allRecords = table.GetAll().ToList();
            if (allRecords.Count == 0) return GetFallbackText(data, phase, grade);

            string categoryStr = data.Category.ToString();

            // 1. 按 Category + Phase 筛选
            var candidates = allRecords
                .Where(r => r.GetString("Category") == categoryStr
                         && r.GetString("Phase") == phase)
                .ToList();

            if (candidates.Count == 0)
                return GetFallbackText(data, phase, grade);

            // 2. Closure 阶段再按 Grade 筛选
            if (phase == "Closure")
            {
                string gradeStr = grade.ToString(); // Perfect / Good / Passable / Failed
                var gradeFiltered = candidates
                    .Where(r => r.GetString("Grade") == gradeStr)
                    .ToList();
                if (gradeFiltered.Count > 0)
                    candidates = gradeFiltered;
                // 如果精确匹配不到，保留所有 candidates（兜底）
            }

            // 3. 按 PersonalityTrait 匹配（精确 > Any）
            string npcTraits = profile?.PersonalityTraits ?? "";
            var traitMatched = candidates
                .Where(r =>
                {
                    string trait = r.GetString("PersonalityTrait");
                    if (string.IsNullOrEmpty(trait) || trait == "Any") return true;
                    return npcTraits.IndexOf(trait, StringComparison.OrdinalIgnoreCase) >= 0;
                })
                .ToList();

            // 如果有精确性格匹配的，用它；否则用 Any 兜底
            var exactTrait = traitMatched
                .Where(r => r.GetString("PersonalityTrait") != "Any"
                         && !string.IsNullOrEmpty(r.GetString("PersonalityTrait")))
                .ToList();
            if (exactTrait.Count > 0)
                candidates = exactTrait;
            else if (traitMatched.Count > 0)
                candidates = traitMatched;

            // 4. 按 Trust 区间筛选
            int trust = TrustSystem.GetTrust(data.QuestGiver);
            var trustMatched = candidates
                .Where(r => trust >= r.GetInt("TrustMin") && trust <= r.GetInt("TrustMax", 100))
                .ToList();
            if (trustMatched.Count > 0)
                candidates = trustMatched;

            // 5. 随机选一条
            int idx = MBRandom.RandomInt(0, candidates.Count);
            string template = candidates[idx].GetString("Text");
            if (string.IsNullOrEmpty(template))
                return GetFallbackText(data, phase, grade);

            // 6. 替换占位符
            return SubstitutePlaceholders(template, data);
        }

        /// <summary>替换模板中的占位符。</summary>
        private static string SubstitutePlaceholders(string template, CommissionData data)
        {
            if (data.TargetHero != null)
                template = template.Replace("{TARGET}", data.TargetHero.Name?.ToString() ?? "目标");
            else
                template = template.Replace("{TARGET}", "目标");

            if (!string.IsNullOrEmpty(data.TargetSettlementId))
            {
                var s = Settlement.Find(data.TargetSettlementId);
                template = template.Replace("{LOCATION}", s?.Name?.ToString() ?? data.TargetSettlementId);
            }
            else
                template = template.Replace("{LOCATION}", "目的地");

            if (!string.IsNullOrEmpty(data.TargetItemId))
            {
                var item = MBObjectManager.Instance.GetObject<ItemObject>(data.TargetItemId);
                template = template.Replace("{ITEM}", item?.Name?.ToString() ?? data.TargetItemId);
            }
            else
                template = template.Replace("{ITEM}", "某物");

            template = template.Replace("{REWARD}", data.NegotiatedReward.ToString());
            template = template.Replace("{DEPOSIT}", data.DepositAmount.ToString());
            template = template.Replace("{GIVER}", data.QuestGiver?.Name?.ToString() ?? "委托人");
            template = template.Replace("{COUNT}", data.TargetItemCount.ToString());
            template = template.Replace("{DAYS}", ((int)(data.TimeRemainingHours / 24f) + 1).ToString());

            return template;
        }

        /// <summary>CSV 查不到时的兜底文本。</summary>
        private static string GetFallbackText(CommissionData data, string phase, CommissionGrade grade)
        {
            if (phase == "Opening")
            {
                string target = data.TargetHero?.Name?.ToString()
                    ?? (data.TargetSettlementId != null
                        ? Settlement.Find(data.TargetSettlementId)?.Name?.ToString() ?? "某地"
                        : "目标");
                return $"我需要有人帮我处理一件事——和{target}有关。报酬{data.NegotiatedReward}第纳尔。你愿意接下吗？";
            }
            else
            {
                return grade switch
                {
                    CommissionGrade.Perfect => $"做得漂亮！{data.NegotiatedReward}第纳尔——你比我想的还要靠得住。",
                    CommissionGrade.Good => $"办妥了。{data.NegotiatedReward}第纳尔，拿好。",
                    CommissionGrade.Passable => $"总算是完成了。{data.NegotiatedReward}，说好的数。",
                    CommissionGrade.Failed => $"这次就算了吧。希望下回能好些。",
                    _ => $"这是{data.NegotiatedReward}第纳尔报酬。"
                };
            }
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
