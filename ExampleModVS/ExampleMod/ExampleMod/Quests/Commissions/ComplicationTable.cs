using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 动态变故表：委托进行中随机触发的变故事件。
    /// 每日检核，按委托类型和阶段决定触发概率。
    /// </summary>
    public static class ComplicationTable
    {
        /// <summary>检核并触发变故。DailyTick 中调用。</summary>
        public static void CheckAndTrigger(CommissionData data, CommissionQuest quest)
        {
            if (data == null || quest == null) return;
            float baseChance = 0.15f; // 每天 15% 概率触发某种变故

            if (MBRandom.RandomFloat > baseChance) return;

            // 按委托类型选择可用变故
            switch (data.Category)
            {
                case CommissionCategory.BountyHunt:
                case CommissionCategory.LegendaryHunt:
                    TryBountyHuntComplication(data, quest);
                    break;
                case CommissionCategory.CaravanEscort:
                case CommissionCategory.EmergencyDelivery:
                    TryEscortComplication(data, quest);
                    break;
                case CommissionCategory.SupplyEmergency:
                case CommissionCategory.ProcurementAgent:
                    TrySupplyComplication(data, quest);
                    break;
                default:
                    TryGenericComplication(data, quest);
                    break;
            }
        }

        private static void TryBountyHuntComplication(CommissionData data, CommissionQuest quest)
        {
            float roll = MBRandom.RandomFloat;
            if (roll < 0.3f)
            {
                // 竞争者出现
                quest.AddLog(new TextObject("传闻：另一个赏金猎人也在追捕同一个目标！你得加快速度了。"));
                // 可选：缩短时限
            }
            else if (roll < 0.55f)
            {
                // 目标壮大
                string targetName = data.TargetHero != null ? data.TargetHero.Name.ToString() : "目标";
                quest.AddLog(new TextObject($"情报：{targetName} 最近招募了更多手下，实力增强了。"));
                data.NegotiatedReward = (int)(data.NegotiatedReward * 1.1f); // 更危险 = 更高报酬
            }
            else if (roll < 0.75f)
            {
                // 目标转移
                quest.AddLog(new TextObject("探子来报：目标更换了藏身地点。继续追踪吧。"));
            }
            else
            {
                // 天候影响
                quest.AddLog(new TextObject("天候突变！暴风雨降低了所有人的视野和移动速度。"));
            }
        }

        private static void TryEscortComplication(CommissionData data, CommissionQuest quest)
        {
            float roll = MBRandom.RandomFloat;
            if (roll < 0.3f)
            {
                quest.AddLog(new TextObject("斥候发现了前方有可疑队伍——可能是埋伏的盗贼。Scout 技能可帮你绕开。"));
            }
            else if (roll < 0.6f)
            {
                quest.AddLog(new TextObject("商队的一头驮马跛了脚，移动速度暂时下降。"));
            }
            else
            {
                quest.AddLog(new TextObject("路上遇到了一队难民，他们说前方有战事——可能需要绕路。"));
            }
        }

        private static void TrySupplyComplication(CommissionData data, CommissionQuest quest)
        {
            float roll = MBRandom.RandomFloat;
            if (roll < 0.4f)
            {
                quest.AddLog(new TextObject($"消息：{data.TargetSettlementId ?? "目标城镇"} 的需求变得更加紧迫。加快送达可获得额外报酬！"));
                data.NegotiatedReward = (int)(data.NegotiatedReward * 1.15f);
            }
            else if (roll < 0.7f)
            {
                quest.AddLog(new TextObject("听说有其他商人也看到了这个商机——有人也在往那边运货。比速度的时候到了！"));
            }
            else
            {
                quest.AddLog(new TextObject("市场传言目标城镇的价格又涨了——现在送去能赚更多。"));
                data.NegotiatedReward = (int)(data.NegotiatedReward * 1.05f);
            }
        }

        private static void TryGenericComplication(CommissionData data, CommissionQuest quest)
        {
            float roll = MBRandom.RandomFloat;
            if (roll < 0.5f)
            {
                quest.AddLog(new TextObject("委托人派信使来询问进度。看来对方挺着急的。"));
            }
            else
            {
                quest.AddLog(new TextObject("天气变坏了。视野和移动速度都受了影响。"));
            }
        }
    }
}
