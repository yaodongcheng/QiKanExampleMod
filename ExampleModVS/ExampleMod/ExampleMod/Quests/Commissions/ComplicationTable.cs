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
            // 目标名兜底（无目标英雄时显示"目标"）
            string targetName = data.TargetHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_complication_target_fallback", "the target");

            if (roll < 0.3f)
            {
                NotificationPipeline.PopupWithChoice(
                    // 弹窗标题：竞争者出现
                    LWNTextHelper.ResolveText("LWN_complication_rival_hunter", "A rival hunter appears"),
                    // 弹窗正文：另一个赏金猎人也在追捕目标
                    LWNTextHelper.ResolveCompound("LWN_complication_rival_hunter_desc",
                        "Another bounty hunter is also after {TARGET}. They might get there first.",
                        ("TARGET", targetName)),
                    // 弹窗选项：加快追踪
                    LWNTextHelper.ResolveText("LWN_complication_rival_hurry", "Hurry the hunt"),
                    () => {
                        // 任务日志：加快步伐抢先一步
                        quest.AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_complication_rival_hurry_log",
                            "You quicken your pace — you cannot let anyone else get there first.")));
                        data.TimeRemainingHours = Math.Max(1, data.TimeRemainingHours - 12);
                    },
                    // 弹窗选项：顺其自然
                    LWNTextHelper.ResolveText("LWN_complication_rival_let_be", "Let it be"),
                    // 任务日志：多个猎人也许能把目标赶到这边
                    () => quest.AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_complication_rival_let_be_log",
                        "More hunters is fine — maybe they will drive the target right to you."))));
            }
            else if (roll < 0.55f)
            {
                NotificationPipeline.PopupWithChoice(
                    // 弹窗标题：目标壮大
                    LWNTextHelper.ResolveText("LWN_complication_target_stronger", "The target grows stronger"),
                    // 弹窗正文：目标招募更多手下，赏金更高
                    LWNTextHelper.ResolveCompound("LWN_complication_target_stronger_desc",
                        "Word is that {TARGET} has recruited more men. But that also means a bigger bounty.",
                        ("TARGET", targetName)),
                    // 弹窗选项：接受挑战
                    LWNTextHelper.ResolveText("LWN_complication_accept_challenge", "Accept the challenge"),
                    () => {
                        data.NegotiatedReward = (int)(data.NegotiatedReward * 1.1f);
                        // 任务日志：目标更强但报酬上涨
                        quest.AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_complication_target_stronger_log",
                            "The target is stronger — but the reward has grown to {GOLD} denars.",
                            ("GOLD", data.NegotiatedReward.ToString()))));
                    },
                    // 弹窗选项：无视
                    LWNTextHelper.ResolveText("LWN_complication_ignore", "Ignore it"),
                    // 任务日志：不过是多几个喽啰
                    () => quest.AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_complication_ignore_log",
                        "Just a few more thugs, nothing more."))));
            }
            else if (roll < 0.75f)
            {
                NotificationPipeline.Notify(
                    // 通知正文：目标更换藏身地点
                    LWNTextHelper.ResolveCompound("LWN_complication_target_moved",
                        "Your scout reports that {TARGET} has moved hiding places. Keep tracking.",
                        ("TARGET", targetName)), "normal");
                // 任务日志：目标转移位置继续追踪
                quest.AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_complication_target_moved_log",
                    "The target has moved — keep tracking.")));
            }
            else
            {
                NotificationPipeline.Notify(
                    // 通知正文：暴雨来袭影响视野移速
                    LWNTextHelper.ResolveText("LWN_complication_storm",
                        "A storm hits! Visibility and speed drop sharply. But in this downpour, the enemy cannot see you any better."), "normal");
                // 任务日志：暴风雨降低视野移速
                quest.AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_complication_storm_log",
                    "The storm lowers everyone's visibility and movement speed.")));
            }
        }

        private static void TryEscortComplication(CommissionData data, CommissionQuest quest)
        {
            float roll = MBRandom.RandomFloat;
            if (roll < 0.3f)
            {
                NotificationPipeline.PopupWithChoice(
                    // 弹窗标题：前方可疑
                    LWNTextHelper.ResolveText("LWN_complication_suspicious_ahead", "Something suspicious ahead"),
                    // 弹窗正文：斥候发现可疑队伍
                    LWNTextHelper.ResolveText("LWN_complication_suspicious_ahead_desc",
                        "Scouts spot a suspicious band ahead — possibly bandits lying in ambush."),
                    // 弹窗选项：绕路躲开
                    LWNTextHelper.ResolveText("LWN_complication_reroute", "Take a detour"),
                    () => {
                        Hero.MainHero.AddSkillXp(DefaultSkills.Scouting, 25);
                        // 任务日志：侦察技能发现埋伏成功绕行
                        quest.AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_complication_reroute_log",
                            "Your scouting skill lets you spot the ambush in time and detour around it.")));
                    },
                    // 弹窗选项：正面硬闯
                    LWNTextHelper.ResolveText("LWN_complication_charge_through", "Charge straight through"),
                    // 任务日志：正面通过准备战斗
                    () => quest.AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_complication_charge_through_log",
                        "You decide to push straight through — prepare for battle!"))));
            }
            else if (roll < 0.6f)
            {
                // 任务日志：驮马跛脚移速下降
                quest.AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_complication_lame_pack_horse_log",
                    "One of the caravan's pack horses has gone lame, slowing the group for a while.")));
                data.TimeRemainingHours = Math.Max(1, data.TimeRemainingHours - 4);
            }
            else
            {
                // 任务日志：难民提示前方有战事
                quest.AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_complication_refugee_warning_log",
                    "You come across refugees — they say there is fighting ahead and advise going around.")));
            }
        }

        private static void TrySupplyComplication(CommissionData data, CommissionQuest quest)
        {
            float roll = MBRandom.RandomFloat;
            // 目标城镇名兜底（无法解析时显示"目标城镇"）
            string loc = data.TargetSettlementId ?? LWNTextHelper.ResolveText("LWN_complication_location_fallback", "the target town");
            if (roll < 0.4f)
            {
                NotificationPipeline.PopupWithChoice(
                    // 弹窗标题：需求紧迫
                    LWNTextHelper.ResolveText("LWN_complication_urgent_demand", "Urgent demand"),
                    // 弹窗正文：目标城镇需求紧迫加快送达有额外报酬
                    LWNTextHelper.ResolveCompound("LWN_complication_urgent_demand_desc",
                        "Demand in {LOCATION} has grown more urgent. Deliver faster for extra pay!",
                        ("LOCATION", loc)),
                    // 弹窗选项：全速前进
                    LWNTextHelper.ResolveText("LWN_complication_full_speed", "Full speed ahead"),
                    () => {
                        data.NegotiatedReward = (int)(data.NegotiatedReward * 1.15f);
                        data.TimeRemainingHours = Math.Max(1, data.TimeRemainingHours - 8);
                        // 任务日志：全速赶往目标城镇报酬上涨
                        quest.AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_complication_full_speed_log",
                            "You race to {LOCATION} — the reward has grown to {GOLD} denars.",
                            ("LOCATION", loc),
                            ("GOLD", data.NegotiatedReward.ToString()))));
                    },
                    // 弹窗选项：保持节奏
                    LWNTextHelper.ResolveText("LWN_complication_keep_pace", "Keep the pace"),
                    // 任务日志：按原计划走避免出错
                    () => quest.AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_complication_keep_pace_log",
                        "Stick to the plan — rushing invites mistakes."))));
            }
            else if (roll < 0.7f)
            {
                // 任务日志：其他商人加入竞争
                quest.AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_complication_rival_merchant_log",
                    "You hear other merchants have spotted the same opportunity — time to race!")));
            }
            else
            {
                // 任务日志：市场价格上涨现在送去赚更多
                quest.AddLog(new TextObject(LWNTextHelper.ResolveCompound("LWN_complication_market_price_log",
                    "Rumors say prices in {LOCATION} have risen again — deliver now for more profit.",
                    ("LOCATION", loc))));
                data.NegotiatedReward = (int)(data.NegotiatedReward * 1.05f);
            }
        }

        private static void TryGenericComplication(CommissionData data, CommissionQuest quest)
        {
            float roll = MBRandom.RandomFloat;
            if (roll < 0.5f)
            {
                NotificationPipeline.Notify(
                    // 通知正文：委托人派信使询问进度
                    LWNTextHelper.ResolveText("LWN_complication_messenger_inquiry",
                        "Your client sends a messenger to check on your progress — they seem anxious."), "normal");
                // 任务日志：信使催促进度
                quest.AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_complication_messenger_inquiry_log",
                    "The messenger urges you on — hurry up.")));
            }
            else
            {
                NotificationPipeline.Notify(
                    // 通知正文：天气骤变行进困难敌人同样被拖慢
                    LWNTextHelper.ResolveText("LWN_complication_bad_weather",
                        "The weather turns harsh, slowing travel. But every cloud has a silver lining — the enemy is slowed too."), "normal");
                // 任务日志：天气变坏影响视野移速
                quest.AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_complication_bad_weather_log",
                    "The weather has worsened. Visibility and movement speed both suffer.")));
            }
        }
    }
}
