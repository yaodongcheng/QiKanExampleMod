using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 动态变故表：委托进行中随机触发的变故事件。
    /// 后端已切至 NotificationPipeline（弹窗 + 选择）。
    /// </summary>
    public static class ComplicationTable
    {
        public static void CheckAndTrigger(CommissionData data, CommissionQuest quest)
        {
            if (data == null || quest == null) return;
            if (MBRandom.RandomFloat > 0.15f) return;

            DebugLogger.Log($"[CommissionQuest] Complication triggered for {data.GetFlavorDescription()}");

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
            string targetName = data.TargetHero?.Name?.ToString() ?? "目标";

            if (roll < 0.3f)
            {
                NotificationPipeline.PopupWithChoice(
                    "竞争者出现",
                    $"另一个赏金猎人也在追捕{targetName}。他可能会先你一步得手。",
                    "加快追踪",
                    () => {
                        quest.AddLog(new TextObject("你加快了步伐——不能让别人抢了先。"));
                        data.TimeRemainingHours = Math.Max(1, data.TimeRemainingHours - 12);
                    },
                    "顺其自然",
                    () => quest.AddLog(new TextObject("多个猎人也好——也许他会把目标赶到你这边来。")));
            }
            else if (roll < 0.55f)
            {
                NotificationPipeline.PopupWithChoice(
                    "目标壮大",
                    $"情报：{targetName}招募了更多手下。但这也意味着赏金更高了。",
                    "接受挑战",
                    () => {
                        data.NegotiatedReward = (int)(data.NegotiatedReward * 1.1f);
                        quest.AddLog(new TextObject($"目标更强了——但报酬也涨到了{data.NegotiatedReward}第纳尔。"));
                    },
                    "无视",
                    () => quest.AddLog(new TextObject("不过是多几个小喽啰而已。")));
            }
            else if (roll < 0.75f)
            {
                NotificationPipeline.Notify(
                    $"探子来报：{targetName}更换了藏身地点。继续追踪。", "normal");
                quest.AddLog(new TextObject($"目标转移了位置——继续追踪。"));
            }
            else
            {
                NotificationPipeline.Notify(
                    "暴雨来袭！视野和移速大降。但也许多了这层雨幕，敌人同样看不清你。", "normal");
                quest.AddLog(new TextObject("暴风雨降低了所有人的视野和移动速度。"));
            }
        }

        private static void TryEscortComplication(CommissionData data, CommissionQuest quest)
        {
            float roll = MBRandom.RandomFloat;
            if (roll < 0.3f)
            {
                NotificationPipeline.PopupWithChoice(
                    "前方可疑",
                    "斥候发现前方有可疑队伍——可能是埋伏的盗贼。",
                    "绕路躲开",
                    () => {
                        Hero.MainHero.AddSkillXp(DefaultSkills.Scouting, 25);
                        quest.AddLog(new TextObject("你的Scout技能让你提前发现了埋伏，成功绕行。"));
                    },
                    "正面硬闯",
                    () => quest.AddLog(new TextObject("你决定正面通过——准备战斗！")));
            }
            else if (roll < 0.6f)
            {
                quest.AddLog(new TextObject("商队的一头驮马跛了脚，移动速度暂时下降。"));
                data.TimeRemainingHours = Math.Max(1, data.TimeRemainingHours - 4);
            }
            else
            {
                quest.AddLog(new TextObject("路上遇到难民——他们说前方有战事，最好绕路。"));
            }
        }

        private static void TrySupplyComplication(CommissionData data, CommissionQuest quest)
        {
            float roll = MBRandom.RandomFloat;
            string loc = data.TargetSettlementId ?? "目标城镇";
            if (roll < 0.4f)
            {
                NotificationPipeline.PopupWithChoice(
                    "需求紧迫",
                    $"{loc}的需求变得更加紧迫。加快送达可获得额外报酬！",
                    "全速前进",
                    () => {
                        data.NegotiatedReward = (int)(data.NegotiatedReward * 1.15f);
                        data.TimeRemainingHours = Math.Max(1, data.TimeRemainingHours - 8);
                        quest.AddLog(new TextObject($"全速赶往{loc}——报酬涨至{data.NegotiatedReward}第纳尔。"));
                    },
                    "保持节奏",
                    () => quest.AddLog(new TextObject("按原计划走——急中出错更麻烦。")));
            }
            else if (roll < 0.7f)
            {
                quest.AddLog(new TextObject("听说有其他商人也看到了这个商机——比速度的时候到了！"));
            }
            else
            {
                quest.AddLog(new TextObject($"市场传言{loc}的价格又涨了——现在送去赚更多。"));
                data.NegotiatedReward = (int)(data.NegotiatedReward * 1.05f);
            }
        }

        private static void TryGenericComplication(CommissionData data, CommissionQuest quest)
        {
            float roll = MBRandom.RandomFloat;
            if (roll < 0.5f)
            {
                NotificationPipeline.Notify(
                    "委托人派信使来询问进度——看来对方挺着急的。", "normal");
                quest.AddLog(new TextObject("信使来催了——动作快点。"));
            }
            else
            {
                NotificationPipeline.Notify(
                    "天气骤变，行进困难。但坏事也是好事——敌人同样被拖慢了。", "normal");
                quest.AddLog(new TextObject("天气变坏了。视野和移动速度都受了影响。"));
            }
        }
    }
}
