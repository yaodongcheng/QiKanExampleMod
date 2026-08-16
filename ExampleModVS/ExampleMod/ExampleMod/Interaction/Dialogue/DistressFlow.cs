using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 2026-08-16（方案 S）：受困求情对话——玩家被俘（和强盗求放）与犯罪被抓（和守卫求饶）。
    /// 不是新对话框架：受困判定（C# 确定性，不进 LLM）+ 求情动作组（ActionRegistry 注册，受困状态才注入）
    /// + 既有轮子接线（赔偿对话子图纪律 restitution_demand + ComputeCost 统一入口 / PersuadeSlot /
    /// AgentControlHelper 守恒 / 原版释放 API）。
    /// S1 受困判定：被俘（IsPrisoner / 被押解 PartyBelongedTo 敌方 / 地牢场景 sp_prisoner tag）；
    /// 被抓（PendingWorldEvent 有真实目击者——犯罪系统语义：没人知道 = 没发生）。
    /// S2 【受困处境】段：respond 链路看守 prompt 注入（看守的认知里玩家是囚犯——对方身份 + 欠的账，
    /// 铁律 11 统一入口 ComputeCost(Restitution)）。
    /// S3 求情动作组：pay_ransom（赎金——赔偿对话纪律：玩家不先开价，金额 = 对方说了算）/ beg_mercy
    /// （认罚——ComputeCost 统一入口 + 转账守恒 + 清除犯罪后果 + 释放）/ bribe_guard（贿赂——秘密转移 +
    /// 检定可失败：钱没了罪还在，代价真实）。
    /// 铁律 12：每个出口都有代价（赎金/罚金/贿赂钱 + 检定失败风险），无零成本最优解。
    /// </summary>
    public static class DistressFlow
    {
        // ═══════════════════════════════════════════════════════════
        // S1 受困状态判定（C# 确定性，不进 LLM）
        // ═══════════════════════════════════════════════════════════

        /// <summary>玩家被俘判定链：IsPrisoner / 被押解（PartyBelongedTo 是敌方 party 且非主队）。
        /// 🔴 2026-08-16（修复，实机 21:28:52）：原含「地牢场景 sp_prisoner tag」判定——玩家**自由走进
        /// 地牢探监/劫狱**也命中 → 误判被俘 → 求情动作组泄漏进 NPC 决策空间（阿速甘选 pay_ransom 虚空扣
        /// 玩家 15% 身家）。被俘玩家的 HeroState 必为 Prisoner（IsPrisoner true）→ sp_prisoner tag 判定
        /// 冗余，删除；被押解过渡由 PartyBelongedTo 敌方判定覆盖。</summary>
        public static bool IsPlayerCaptive()
        {
            try
            {
                var hero = Hero.MainHero;
                if (hero == null) return false;
                try { if (hero.IsPrisoner) return true; } catch { }
                // 🔴 Hero.PartyBelongedTo 返回 MobileParty——被押解（敌方 party 且非主队）
                var p = hero.PartyBelongedTo;
                if (p != null && p != MobileParty.MainParty) return true;
                return false;
            }
            catch { return false; }
        }

        /// <summary>玩家犯罪被抓（PendingWorldEvent 激活——有真实目击者；无人看见 = 世界层面没发生）。</summary>
        public static bool IsPlayerCaught()
        {
            try
            {
                var pending = AgentAIController.Instance?.PendingWorldEvent;
                if (pending == null) return false;
                return pending.WitnessTestimonies?.Any(t => t != null
                    && (t.WitnessHeroId != null || t.TemplateId != null)) == true;
            }
            catch { return false; }
        }

        /// <summary>受困判定总入口（求情动作组 IsValid 与【受困处境】注入共用）。</summary>
        public static bool IsInDistress() => IsPlayerCaptive() || IsPlayerCaught();

        // ═══════════════════════════════════════════════════════════
        // S2 【受困处境】上下文注入（respond 链路看守 prompt；prompt 材料豁免铁律 13）
        // ═══════════════════════════════════════════════════════════

        /// <summary>构建看守视角的受困处境段（对方 = 玩家，被关押/逮住；欠的账 = 铁律 11 统一入口）。
        /// 无受困状态 → 空串（零注入）。</summary>
        public static string BuildDistressSection(Agent keeper)
        {
            try
            {
                var player = Hero.MainHero;
                if (player == null) return null;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("【受困处境】");
                if (IsPlayerCaptive())
                    sb.AppendLine($"- 对方是 {player.Name}，被你们关押着（身份：俘虏）。他想求你们放人——赎金由你开价，他说了不算。");
                if (IsPlayerCaught())
                {
                    sb.AppendLine($"- 对方是 {player.Name}，因犯了事被逮住（身份：嫌犯）。他欠的账还没算清。");
                    try
                    {
                        var evt = AgentAIController.Instance?.PendingWorldEvent;
                        if (evt != null)
                        {
                            int cost = CrimePenaltyCalculator.ComputeCost(evt, CostType.Restitution);
                            sb.AppendLine($"- 他该赔的数目（{cost}）由你说了算——他要认罚就按这个数，他要狡辩就让他碰碰壁。");
                        }
                    }
                    catch { }
                }
                return sb.ToString();
            }
            catch { return null; }
        }

        // ═══════════════════════════════════════════════════════════
        // S3 求情动作执行（ActionRegistry 注册，受困状态才注入）
        // ═══════════════════════════════════════════════════════════

        /// <summary>赎金金额（对方说了算——强盗勒索人设）：有 WorldEvent → ComputeCost(Restitution)
        /// 统一入口（铁律 11）；无事件（纯被俘）→ 勒索基础值 = 玩家身家 15% 钳制 [200, 5000]。
        /// 🔴 金额只在 NPC 开价时算（restitution_demand 节点语义）——玩家先报价被驳回/无视（纪律验证）。</summary>
        public static int RansomAmount()
        {
            try
            {
                var evt = AgentAIController.Instance?.PendingWorldEvent;
                if (evt != null)
                    return CrimePenaltyCalculator.ComputeCost(evt, CostType.Restitution);
                int gold = Hero.MainHero?.Gold ?? 0;
                int ransom = (int)(gold * 0.15f);
                return Math.Max(200, Math.Min(5000, ransom / 100 * 100));
            }
            catch { return 500; }
        }

        /// <summary>接受赎金：转账守恒（看守有 Hero → TransferGold(玩家→看守)；无 Hero → 虚空 Sink——
        /// "赎金被强盗们收走"属单边 Sink，注释标注，非半截转移，铁律 4）+ 释放玩家（EndCaptivityAction
        /// .ApplyByRansom 实锤签名）。钱不够 → 全扣光不释放（代价真实）。
        /// 🔴 2026-08-16（防御守卫，实机 21:28:52 阿速甘案）：玩家非被俘状态禁止执行——NPC 误选
        /// pay_ransom 曾虚空扣玩家 15% 身家（IsValid 已加 attacker 守卫，此处兜底防未来误用）。</summary>
        public static void AcceptRansom(Hero keeper, int amount)
        {
            try
            {
                var player = Hero.MainHero;
                if (player == null || amount <= 0) return;
                if (!IsPlayerCaptive())
                {
                    DebugLogger.Log($"[Distress] 赎金拒绝执行：玩家非被俘状态（防虚空扣钱，keeper={keeper?.Name?.ToString() ?? "null"}）");
                    return;
                }
                int paid = AgentControlHelper.TransferGold(player, keeper, amount, notify: true);
                if (paid < amount)
                {
                    // 本地化：LWN_distress_ransom_short（玩家可见文本）
                    InformationManager.DisplayMessage(new InformationMessage(
                        // 本地化：distress_ransom_short（玩家可见文本）
                        LWNTextHelper.ResolveText("LWN_distress_ransom_short",
                            "The bandits took every coin you had... and still it was not enough."), Colors.Red));
                    DebugLogger.Log($"[Distress] 赎金不足：付 {paid}/{amount} → 不放人（代价真实）");
                    return;
                }
                // 释放玩家（看守有 Hero → facilitator；无 Hero → null）
                EndCaptivityAction.ApplyByRansom(player, keeper);
                DebugLogger.Log($"[Distress] 赎金释放：{player.Name} 付 {amount} → 获释（keeper={keeper?.Name?.ToString() ?? "虚空（强盗收走）"}）");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Distress] 赎金执行异常: {ex.Message}");
            }
        }

        /// <summary>认罚（beg_mercy）：ComputeCost(Restitution) 统一入口（与赔偿对话同价）→ 转账守恒
        ///（守卫 Hero 或虚空 Sink）+ 清除犯罪后果（WorldEvent 标记 PlayerPaidRestitution——原版罪行了结）
        /// + 释放（守卫有 Hero → ApplyByRansom 语义；无 → ApplyByEscape 自行离开）。</summary>
        public static void BegMercy(Hero guard)
        {
            try
            {
                var player = Hero.MainHero;
                if (player == null) return;
                // 🔴 2026-08-16（防御守卫，同 AcceptRansom）：玩家非被抓状态禁止执行（防 NPC 误选虚空扣钱）
                if (!IsPlayerCaught())
                {
                    DebugLogger.Log($"[Distress] 认罚拒绝执行：玩家非被抓状态（guard={guard?.Name?.ToString() ?? "null"}）");
                    return;
                }
                int cost = RansomAmount();
                int paid = AgentControlHelper.TransferGold(player, guard, cost, notify: true);
                if (paid < cost)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        // 本地化：distress_fine_short（玩家可见文本）
                        LWNTextHelper.ResolveText("LWN_distress_fine_short",
                            "The guard counted your coins and shook his head - not enough. You are still under arrest."), Colors.Red));
                    DebugLogger.Log($"[Distress] 认罚不足：付 {paid}/{cost} → 仍被收押（代价真实）");
                    return;
                }
                // 清除犯罪后果（PlayerPaidRestitution——WorldEvent 域既有标记；通缉/逮捕状态由事件结算自理）
                try
                {
                    var evt = AgentAIController.Instance?.PendingWorldEvent;
                    if (evt != null) WorldEventStore.OnPlayerPaidRestitution(evt);
                }
                catch { }
                // 释放
                if (guard != null)
                    EndCaptivityAction.ApplyByRansom(player, guard);
                else
                    EndCaptivityAction.ApplyByEscape(player, null, true);
                DebugLogger.Log($"[Distress] 认罚释放：{player.Name} 付 {cost} → 获释（guard={guard?.Name?.ToString() ?? "虚空"}）");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Distress] 认罚执行异常: {ex.Message}");
            }
        }

        /// <summary>贿赂（bribe_guard）：秘密转移（玩家 → 守卫 Hero 或虚空 Sink）+ 检定（守卫品格影响——
        /// Honor 高难收买；可失败：守卫收了钱照样抓你 → 钱没了罪还在，代价真实）。</summary>
        public static bool BribeGuard(Hero guard, int amount)
        {
            try
            {
                var player = Hero.MainHero;
                if (player == null || amount <= 0) return false;
                // 🔴 2026-08-16（防御守卫，同 AcceptRansom）：玩家非被抓状态禁止执行（防 NPC 误选虚空扣钱）
                if (!IsPlayerCaught())
                {
                    DebugLogger.Log($"[Distress] 贿赂拒绝执行：玩家非被抓状态（guard={guard?.Name?.ToString() ?? "null"}）");
                    return false;
                }
                int paid = AgentControlHelper.TransferGold(player, guard, amount, notify: true);
                if (paid < amount)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        // 本地化：distress_bribe_short（玩家可见文本）
                        LWNTextHelper.ResolveText("LWN_distress_bribe_short",
                            "Your purse came up empty. The guard is not impressed."), Colors.Red));
                    return false;
                }
                // 检定：守卫品格影响收买成功率（Honor ≥1 → -20%；Mercy ≥1 → +10%；基础 50%）
                float chance = 0.5f;
                if (guard != null)
                {
                    try
                    {
                        int honor = guard.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Honor);
                        int mercy = guard.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Mercy);
                        if (honor >= 1) chance -= 0.20f;
                        if (mercy >= 1) chance += 0.10f;
                    }
                    catch { }
                }
                chance = MathF.Clamp(chance, 0.05f, 0.85f);
                bool success = SingleRollResolver.Roll(chance);
                if (success)
                {
                    // 释放
                    if (guard != null)
                        EndCaptivityAction.ApplyByRansom(player, guard);
                    else
                        EndCaptivityAction.ApplyByEscape(player, null, true);
                    InformationManager.DisplayMessage(new InformationMessage(
                        // 本地化：distress_bribe_ok（玩家可见文本）
                        LWNTextHelper.ResolveText("LWN_distress_bribe_ok",
                            "The guard pockets the coin and looks the other way. You slip free."), Colors.Green));
                    DebugLogger.Log($"[Distress] 贿赂成功：{player.Name} 付 {amount} → 获释（检定 {chance:0%}）");
                    return true;
                }
                InformationManager.DisplayMessage(new InformationMessage(
                    // 本地化：distress_bribe_fail（玩家可见文本）
                    LWNTextHelper.ResolveText("LWN_distress_bribe_fail",
                        "The guard takes your coin... then grabs your arm. You are still under arrest."), Colors.Red));
                DebugLogger.Log($"[Distress] 贿赂失败：{player.Name} 付 {amount} 钱没了罪还在（检定 {chance:0%}）");
                return false;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Distress] 贿赂执行异常: {ex.Message}");
                return false;
            }
        }
    }
}
