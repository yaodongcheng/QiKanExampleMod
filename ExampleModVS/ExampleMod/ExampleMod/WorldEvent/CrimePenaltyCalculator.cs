using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 金额计算类型。
    /// </summary>
    public enum CostType
    {
        /// <summary>正式赔偿：被盗价值 × 阶段倍率 × 交易折扣</summary>
        Restitution,
        /// <summary>当场私了：被盗价值 × 2</summary>
        OnSpot,
        /// <summary>悬赏金额：单价 × 数量</summary>
        Bounty,
        /// <summary>当场罚款：严重度 × 2 × 交易折扣（保底50）</summary>
        Fine,
    }

    /// <summary>
    /// 统一金额计算入口。所有赔偿/罚款/悬赏/赎金都走这里。
    /// </summary>
    public static class CrimePenaltyCalculator
    {
        /// <summary>
        /// 估算受害者的"身价"（卖掉能值多少钱）：直接用原版俘虏赎金公式
        /// RansomValueCalculationModel.PrisonerRansomValue —— 与酒馆卖俘虏、英雄赎金同源，不自创。
        /// 士兵 = 招募成本×0.25（T5≈100，T6≈150）；Hero = 招募成本 + 家族等级加成 + √金币×6，
        /// 再乘王国系数（领主通常数千）。sellerHero 传 null：perk 加成是"卖家"的售价加成，不该抬高受害者身价。
        /// </summary>
        public static int EstimateVictimValue(Agent victim)
        {
            var co = victim?.Character as CharacterObject;
            if (co == null) return 0;
            try
            {
                return Campaign.Current?.Models?.RansomValueCalculationModel?.PrisonerRansomValue(co, null) ?? 0;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Penalty] EstimateVictimValue error: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 统一金额计算。根据 CostType 选择公式。
        /// </summary>
        /// <param name="evt">犯罪事件（null 时走兜底）</param>
        /// <param name="type">金额类型</param>
        /// <param name="forStage">Restitution 的阶段覆盖（null = 用 evt.Stage）</param>
        public static int ComputeCost(WorldEvent evt, CostType type, EventStage? forStage = null)
        {
            if (evt?.Config == null)
            {
                return type switch
                {
                    CostType.Bounty => 500,
                    CostType.Fine => 100,
                    _ => 100,
                };
            }

            var cfg = evt.Config;
            return type switch
            {
                CostType.Restitution => ComputeRestitution(evt, forStage),
                CostType.OnSpot => ComputeOnSpot(evt),
                CostType.Bounty => cfg.BaseBountyPerUnit * Math.Max(1, evt.TotalStolenCount),
                CostType.Fine => ComputeFine(evt),
                _ => 100,
            };
        }

        /// <summary>
        /// 无 WorldEvent 时的兜底罚款（当场被抓，事件尚未创建）。
        /// 有事件 → 走 ComputeCost(evt, Fine)；无事件 → 按行为严重度 × 10
        /// </summary>
        public static int ComputePenalty(WorldEvent evt, PlayerActionType? action = null)
        {
            if (evt != null)
                return ComputeCost(evt, CostType.Restitution);

            int severity = action switch
            {
                PlayerActionType.Crouching => 5,
                PlayerActionType.WeaponDrawn => 10,
                PlayerActionType.StealUIOpen => 15,
                PlayerActionType.Steal => 20,
                PlayerActionType.AttackAlly => 30,
                PlayerActionType.Knockout => 40,
                _ => 10
            };
            return severity * 10;
        }

        /// <summary>
        /// 无 WorldEvent 时的兜底私了价（×2）。
        /// 有事件 → 走 ComputeCost(evt, OnSpot)；无事件 → 按行为严重度 × 20
        /// </summary>
        public static int ComputeOnSpotPenalty(WorldEvent evt, PlayerActionType? action = null)
        {
            if (evt != null)
                return ComputeCost(evt, CostType.OnSpot);

            int severity = action switch
            {
                PlayerActionType.Crouching => 5,
                PlayerActionType.WeaponDrawn => 10,
                PlayerActionType.StealUIOpen => 15,
                PlayerActionType.Steal => 20,
                PlayerActionType.AttackAlly => 30,
                PlayerActionType.Knockout => 40,
                _ => 10
            };
            return severity * 20;
        }

        /// <summary>
        /// 战斗认输赎金：取玩家金币的 15% 或 200 的较大值。
        /// </summary>
        public static int ComputeSurrenderRansom()
        {
            return Math.Max(200, (int)(Hero.MainHero.Gold * 0.15f));
        }

        /// <summary>
        /// 砍价后金额。
        /// </summary>
        public static int ComputeHaggleAmount(int baseAmount, float discount)
        {
            if (discount <= 0f || discount >= 1f) return baseAmount;
            return (int)(baseAmount * discount);
        }

        // ── 私有计算 ──

        static float TradeDiscount()
        {
            return 1f - Math.Min(0.15f, Hero.MainHero.GetSkillValue(DefaultSkills.Trade) * 0.0005f);
        }

        static int BaseValue(WorldEvent evt)
        {
            // 赃物市值 + 袭击身价（击晕按受害者原版赎金价累计）；都没有 → Severity×10 兜底
            int v = evt.TotalStolenValue + evt.AssaultRestitutionValue;
            return v > 0 ? v : evt.Severity * 10;
        }

        static int ComputeRestitution(WorldEvent evt, EventStage? forStage)
        {
            var stage = forStage ?? evt.Stage;
            var cfg = evt.Config;
            float multiplier = stage switch
            {
                EventStage.Active => cfg.BaseRestitutionMultiplier,
                EventStage.Confrontation => cfg.BaseRestitutionMultiplier * 1.7f,
                _ => cfg.BaseRestitutionMultiplier * 0.7f,
            };
            return (int)(BaseValue(evt) * multiplier * TradeDiscount());
        }

        static int ComputeOnSpot(WorldEvent evt)
        {
            return BaseValue(evt) * 2;
        }

        static int ComputeFine(WorldEvent evt)
        {
            // 袭击案件：罚款至少按袭击身价收（否则击晕 T5 精兵也只罚 50 兜底价）
            int baseValue = Math.Max(Math.Max(1, evt.Severity * 2), evt.AssaultRestitutionValue);
            return Math.Max(50, (int)(baseValue * TradeDiscount()));
        }
    }
}
