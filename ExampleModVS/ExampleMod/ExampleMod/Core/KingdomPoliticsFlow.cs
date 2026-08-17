using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 2026-08-16（方案 R）：政治动作空间——决策空间身份分级（H 的对偶）。
    /// 动作空间按**对话对象身份**注入政治动作组（L2 领主 / L3 国王）；执行层全部走原版官方管道：
    /// ChangeKingdomAction（劝降）/ KingdomDecision 提案投票（宣战/停战，禁止对话直改战争状态）/
    /// SetPartyAiAction（命令己方领主）。门槛 + 冷却 + 失败代价（铁律 12：无零成本最优解）。
    /// 身份判定（C# 确定性，共享 helper——H 方案身份判定同款复用）：
    ///   IsLord = hero.Clan != null / IsKingdom = Clan.Kingdom != null / IsKing = Kingdom.Leader == hero。
    /// </summary>
    public static class KingdomPoliticsFlow
    {
        // ── 冷却（内存态；键 = actionCode|defenderId → 墙钟日）──
        private static readonly Dictionary<string, string> _cooldownDay = new Dictionary<string, string>();
        private static readonly Dictionary<string, int> _cooldownDays = new Dictionary<string, int>();

        private static bool IsCooledDown(string actionCode, Hero defender, int days)
        {
            if (defender == null) return true;
            string key = actionCode + "|" + defender.StringId;
            string today = DateTime.UtcNow.ToString("yyyyMMdd");
            if (_cooldownDay.TryGetValue(key, out var day) && day == today) return false;
            // 冷却是现实日计数：记录首次执行日，N 天内不再触发（简化：同一天不重复 + 重启重置）
            _cooldownDay[key] = today;
            _cooldownDays[key] = days;
            return true;
        }

        /// <summary>身份判定（R1，C# 确定性）：对象是否是领主（有家族）。</summary>
        public static bool IsLord(Hero hero) => hero != null && hero.Clan != null;

        /// <summary>身份判定：对象是否是国王（玩家所属王国或任意王国的 Leader）。</summary>
        public static bool IsKing(Hero hero) => hero?.Clan?.Kingdom != null && hero.Clan.Kingdom.Leader == hero;

        /// <summary>叛逃倾向（R2，原版领主叛逃机制条件子集）：无领地 / 与国王关系 &lt; 0 / 与玩家关系 ≥ 20。</summary>
        public static bool HasDefectionTendency(Hero lord)
        {
            try
            {
                if (lord?.Clan == null || lord.Clan == Clan.PlayerClan) return false;
                if (lord.Clan.Fiefs == null || lord.Clan.Fiefs.Count == 0) return true;
                var king = lord.Clan.Kingdom?.Leader;
                if (king != null && king != lord && king.GetRelation(lord) < 0) return true;
                if (Hero.MainHero != null && lord.GetRelation(Hero.MainHero) >= 20) return true;
                return false;
            }
            catch { return false; }
        }

        /// <summary>劝降/招募领主加入玩家王国（persuade_join）：
        /// 玩家是国王 + defender 有叛逃倾向 → 检定（SingleRollResolver.Roll 统一入口，d20 风格）→
        /// 成功：V.JoinKingdom（ChangeKingdomAction.ApplyByJoinToKingdom 实锤签名，
        /// 🔴 1.2.12 = 3参 / 1.3+ = 4参，走 V 屏蔽）；失败：关系 -10 + 冷却 7 天。
        /// 后果（领主归属变更 = 全局事件）由原版自理（与原王国关系/战争状态）。</summary>
        public static void PersuadeLord(Hero lord)
        {
            try
            {
                var playerKingdom = Clan.PlayerClan?.Kingdom;
                if (playerKingdom == null || lord == null || lord.Clan == null)
                {
                    DebugLogger.Log($"[Kingdom] 劝降失败：玩家非国王或目标无效");
                    return;
                }
                if (lord.Clan == Clan.PlayerClan || lord.Clan == playerKingdom.RulingClan)
                {
                    DebugLogger.Log($"[Kingdom] 劝降失败：{lord.Name} 已是自己人");
                    return;
                }
                if (!IsCooledDown("persuade_join", lord, 7))
                {
                    DebugLogger.Log($"[Kingdom] 劝降冷却中（{lord.Name}）→ 降级 NONE");
                    return;
                }
                // 检定成功率（叛逃倾向基础 + 关系加成；钳制 [5%, 90%]）
                float chance = 0.40f;
                if (HasDefectionTendency(lord)) chance += 0.20f;
                if (Hero.MainHero != null)
                {
                    int rel = lord.GetRelation(Hero.MainHero);
                    if (rel >= 20) chance += 0.15f;
                    else if (rel <= -10) chance -= 0.15f;
                }
                chance = MathF.Clamp(chance, 0.05f, 0.90f);
                bool success = SingleRollResolver.Roll(chance);
                if (success)
                {
                    V.JoinKingdom(lord.Clan, playerKingdom, true);
                    // 本地化：LWN_plan_action_persuade_ok（玩家可见文本）
                    InformationManager.DisplayMessage(new InformationMessage(
                        // 本地化：plan_action_persuade_ok（玩家可见文本）
                        LWNTextHelper.ResolveCompound("LWN_plan_action_persuade_ok",
                            "{NAME} has sworn allegiance to our kingdom!",
                            ("NAME", lord.Name?.ToString() ?? "")), Colors.Green));
                    DebugLogger.Log($"[Kingdom] 劝降成功 {lord.Name} → {playerKingdom.Name}（检定 {chance:0%}）");
                    return;
                }
                // 失败代价：关系 -10 + 冷却 7 天（已登记）
                try { ChangeRelationAction.ApplyPlayerRelation(lord, -10, true, true); } catch { }
                // 本地化：LWN_plan_action_persuade_fail（玩家可见文本）
                InformationManager.DisplayMessage(new InformationMessage(
                    // 本地化：plan_action_persuade_fail（玩家可见文本）
                    LWNTextHelper.ResolveCompound("LWN_plan_action_persuade_fail",
                        "{NAME} refused your offer, and took offense. (relation -10)",
                        ("NAME", lord.Name?.ToString() ?? "")), Colors.Red));
                DebugLogger.Log($"[Kingdom] 劝降失败 {lord.Name}（检定 {chance:0%}，关系 -10，冷却 7 天）");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Kingdom] 劝降异常: {ex.Message}");
            }
        }

        /// <summary>目标王国名 → Kingdom（铁律 5 动态遍历 Kingdom.All 名匹配；无命中 → null）。</summary>
        public static Kingdom ResolveKingdomByName(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            foreach (var k in Kingdom.All)
            {
                if (k == null) continue;
                string n = k.Name?.ToString();
                if (string.IsNullOrEmpty(n)) continue;
                if (n.Equals(text, StringComparison.OrdinalIgnoreCase)
                    || n.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                    return k;
            }
            return null;
        }

        /// <summary>提议宣战（propose_war）：defender = 玩家所属王国的国王（L3）+ 玩家是王国成员 +
        /// 影响力 ≥ 200 + 国王关系 ≥ 0 → 原版王国决策管道（DeclareWarDecision → 家族投票 →
        /// DeclareWarAction.ApplyByKingdomDecision 生效）。失败：国王明确拒绝 + 影响力 -100。
        /// 🔴 禁止直改战争状态：DeclareWarAction.ApplyByDefault 不用于对话路径——宣战 = 全图战争，
        /// 必须走王国决策投票，失败有代价（铁律 12）。</summary>
        public static void ProposeWar(Hero king, string targetText)
        {
            try
            {
                var playerKingdom = Clan.PlayerClan?.Kingdom;
                if (playerKingdom == null || king == null || king != playerKingdom.Leader)
                {
                    DebugLogger.Log($"[Kingdom] 宣战提案失败：对象非玩家王国国王");
                    return;
                }
                var target = ResolveKingdomByName(targetText);
                if (target == null)
                {
                    DebugLogger.Log($"[Kingdom] 宣战提案失败：目标王国解析失败（{targetText}）");
                    return;
                }
                if (target == playerKingdom)
                {
                    DebugLogger.Log($"[Kingdom] 宣战提案失败：目标是自己王国");
                    return;
                }
                if (playerKingdom.IsAtWarWith(target))
                {
                    DebugLogger.Log($"[Kingdom] 宣战提案跳过：{playerKingdom.Name} 已与 {target.Name} 交战");
                    return;
                }
                if (!IsCooledDown("propose_war", king, 10))
                {
                    DebugLogger.Log($"[Kingdom] 宣战提案冷却中 → 降级 NONE");
                    return;
                }
                if (Clan.PlayerClan.Influence < 200)
                {
                    // 本地化：LWN_plan_action_war_noinfluence（国王拒绝文案——"国库空虚，还不是时候"）
                    InformationManager.DisplayMessage(new InformationMessage(
                        // 本地化：plan_action_war_noinfluence（玩家可见文本）
                        LWNTextHelper.ResolveText("LWN_plan_action_war_noinfluence",
                            "The king shook his head: the treasury is empty, now is not the time."), Colors.Red));
                    DebugLogger.Log($"[Kingdom] 宣战提案被拒：影响力不足（{Clan.PlayerClan.Influence:0}/200）");
                    return;
                }
                int kingRel = 0;
                try { kingRel = Hero.MainHero != null ? king.GetRelation(Hero.MainHero) : 0; } catch { }
                if (kingRel < 0)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        // 本地化：plan_action_war_noinfluence（玩家可见文本）
                        LWNTextHelper.ResolveText("LWN_plan_action_war_noinfluence",
                            "The king shook his head: the treasury is empty, now is not the time."), Colors.Red));
                    DebugLogger.Log($"[Kingdom] 宣战提案被拒：国王关系 {kingRel} < 0");
                    return;
                }
                // 提案（原版王国决策管道：家族投票决定，玩家只是提议者）
                Clan.PlayerClan.Influence -= 200;
                playerKingdom.AddDecision(new DeclareWarDecision(Clan.PlayerClan, target), false);
                // 本地化：LWN_plan_action_war_proposed（提交时明确播报——禁止静默，设计哲学原则一）
                InformationManager.DisplayMessage(new InformationMessage(
                    // 本地化：plan_action_war_proposed（玩家可见文本）
                    LWNTextHelper.ResolveCompound("LWN_plan_action_war_proposed",
                        "A declaration of war against {TARGET} has been proposed to the council. (influence -200)",
                        ("TARGET", target.Name?.ToString() ?? "")), Colors.Blue));
                DebugLogger.Log($"[Kingdom] 宣战提案已提交（{playerKingdom.Name} → {target.Name}，影响力 -200）");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Kingdom] 宣战提案异常: {ex.Message}");
            }
        }

        /// <summary>提议停战（negotiate_peace）：propose_war 同款对称（MakePeaceKingdomDecision +
        /// MakePeaceAction.ApplyByKingdomDecision）。</summary>
        public static void ProposePeace(Hero king, string targetText)
        {
            try
            {
                var playerKingdom = Clan.PlayerClan?.Kingdom;
                if (playerKingdom == null || king == null || king != playerKingdom.Leader)
                {
                    DebugLogger.Log($"[Kingdom] 停战提案失败：对象非玩家王国国王");
                    return;
                }
                var target = ResolveKingdomByName(targetText);
                if (target == null)
                {
                    DebugLogger.Log($"[Kingdom] 停战提案失败：目标王国解析失败（{targetText}）");
                    return;
                }
                if (!playerKingdom.IsAtWarWith(target))
                {
                    DebugLogger.Log($"[Kingdom] 停战提案跳过：已与 {target.Name} 和平");
                    return;
                }
                if (!IsCooledDown("negotiate_peace", king, 10))
                {
                    DebugLogger.Log($"[Kingdom] 停战提案冷却中 → 降级 NONE");
                    return;
                }
                if (Clan.PlayerClan.Influence < 200)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        // 本地化：plan_action_war_noinfluence（玩家可见文本）
                        LWNTextHelper.ResolveText("LWN_plan_action_war_noinfluence",
                            "The king shook his head: the treasury is empty, now is not the time."), Colors.Red));
                    DebugLogger.Log($"[Kingdom] 停战提案被拒：影响力不足");
                    return;
                }
                Clan.PlayerClan.Influence -= 200;
                playerKingdom.AddDecision(new MakePeaceKingdomDecision(Clan.PlayerClan, target), false);
                // 本地化：LWN_plan_action_peace_proposed（玩家可见文本）
                InformationManager.DisplayMessage(new InformationMessage(
                    // 本地化：plan_action_peace_proposed（玩家可见文本）
                    LWNTextHelper.ResolveCompound("LWN_plan_action_peace_proposed",
                        "A peace proposal with {TARGET} has been submitted to the council. (influence -200)",
                        ("TARGET", target.Name?.ToString() ?? "")), Colors.Blue));
                DebugLogger.Log($"[Kingdom] 停战提案已提交（{playerKingdom.Name} ↔ {target.Name}，影响力 -200）");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Kingdom] 停战提案异常: {ex.Message}");
            }
        }

        /// <summary>命令己方领主行动（order_march）：目标解析（定居点名 → SetMoveToTown；
        /// 部队名 → EngageParty——J 方案同款 API）；无检定（自家命令，直属关系），冷却 1 天。
        /// 用法："去攻 X 城" / "回领地防守" / "去拦住那支商队"。</summary>
        public static void OrderMarch(MobileParty party, string targetText)
        {
            try
            {
                if (party == null) return;
                if (!IsCooledDown("order_march", party.LeaderHero ?? Hero.MainHero, 1))
                {
                    DebugLogger.Log($"[Kingdom] order_march 冷却中 → 降级 NONE");
                    return;
                }
                if (string.IsNullOrWhiteSpace(targetText))
                {
                    DebugLogger.Log($"[Kingdom] order_march 无目标文本 → 降级 NONE");
                    return;
                }
                // 1) 定居点解析（"去攻 X 城" / "回领地防守"）
                var settlement = PartySplitFlow.ResolveSettlementByName(targetText);
                if (settlement != null)
                {
                    // 玩家领地 → 防守（patrol 领地周边）；其他定居点 → 前往（攻城决策由玩家自主，AI 到点自理）
                    if (settlement.OwnerClan == Clan.PlayerClan)
                        V.PatrolAround(party, settlement);
                    else
                        V.SetMoveToTown(party, settlement);
                    DebugLogger.Log($"[Kingdom] order_march {party.Name} → {(settlement.OwnerClan == Clan.PlayerClan ? "防守" : "前往")} {settlement.Name}");
                    return;
                }
                // 2) 部队解析（"去拦住那支商队"）
                var targetParty = PartySplitFlow.ResolvePartyByName(targetText);
                if (targetParty != null)
                {
                    V.EngageParty(party, targetParty);
                    DebugLogger.Log($"[Kingdom] order_march {party.Name} → 追击 {targetParty.Name}");
                    return;
                }
                DebugLogger.Log($"[Kingdom] order_march 目标解析失败: {targetText} → 降级 NONE");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Kingdom] order_march 异常: {ex.Message}");
            }
        }
    }
}
