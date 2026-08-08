using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 旅途事件表：委托旅途中随机触发的小事件。
    /// 后端已切至 NotificationPipeline（CK3 式弹窗 + 选择）。
    /// </summary>
    public static class JourneyEvents
    {
        /// <summary>DailyTick 中调用，按概率触发旅途事件。</summary>
        public static void TryTrigger(CommissionData data, CommissionQuest quest)
        {
            if (data == null || quest == null) return;
            if (MBRandom.RandomFloat > 0.25f) return; // 每天 25% 概率触发

            DebugLogger.Log($"[CommissionQuest] JourneyEvent triggered for {data.GetFlavorDescription()}");

            float roll = MBRandom.RandomFloat;

            if (roll < 0.25f)
            {
                // 受伤旅行者 → 选择：救人 (+Medicine) / 赶路
                NotificationPipeline.PopupWithChoice(
                    // 弹窗标题：路边伤者
                    LWNTextHelper.ResolveText("LWN_journey_injured_traveler_title", "Injured Traveler"),
                    // 弹窗描述：求助与风险提示
                    LWNTextHelper.ResolveText("LWN_journey_injured_traveler_desc", "You come across an injured traveler who begs for help. Treating the wound might earn you some first-aid experience... but it could also be a trap."),
                    // 选项A：停下救治
                    LWNTextHelper.ResolveText("LWN_journey_choice_stop_and_help", "Stop and Help"),
                    () =>
                    {
                        Hero.MainHero.AddSkillXp(DefaultSkills.Medicine, 30);
                        // 任务日志：救治了旅行者并获知情报
                        quest.AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_journey_log_helped_traveler", "You stopped to tend the traveler's wounds; grateful, he shares some intel about the road ahead.")));
                    },
                    // 选项B：继续赶路
                    LWNTextHelper.ResolveText("LWN_journey_choice_move_on", "Keep Moving"),
                    () =>
                    {
                        // 任务日志：无视伤者继续赶路
                        quest.AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_journey_log_passed_by", "You glance at the wounded man and continue on your way.")));
                    });
            }
            else if (roll < 0.5f)
            {
                // 困住的车队 → 选择：帮忙 (+Engineering +Gold) / 路过
                NotificationPipeline.PopupWithChoice(
                    // 弹窗标题：陷进泥里的货车
                    LWNTextHelper.ResolveText("LWN_journey_stuck_wagon_title", "Wagon Stuck in Mud"),
                    // 弹窗描述：车主求助
                    LWNTextHelper.ResolveText("LWN_journey_stuck_wagon_desc", "A wagon is stuck in the mud and the owner waves frantically for help. Fixing the wheel might take some engineering skill..."),
                    // 选项A：出手相助
                    LWNTextHelper.ResolveText("LWN_journey_choice_lend_hand", "Lend a Hand"),
                    () =>
                    {
                        Hero.MainHero.AddSkillXp(DefaultSkills.Engineering, 20);
                        AgentControlHelper.TransferGold((Hero)null, Hero.MainHero, 30);
                        // 任务日志：修好车轮并收到谢礼
                        quest.AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_journey_log_fixed_wagon", "You use your Engineering skill to fix their wheel, and the owner offers you a token of gratitude.")));
                    },
                    // 选项B：爱莫能助
                    LWNTextHelper.ResolveText("LWN_journey_choice_unable_to_help", "Unable to Help"),
                    () =>
                    {
                        // 任务日志：摇头驱马绕过货车
                        quest.AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_journey_log_rode_past", "You shake your head, ride around the wagon, and continue on.")));
                    });
            }
            else if (roll < 0.75f)
            {
                // 路遇同行 → 选择：结伴 (+Scouting) / 独行
                NotificationPipeline.PopupWithChoice(
                    // 弹窗标题：同路的商人
                    LWNTextHelper.ResolveText("LWN_journey_fellow_merchant_title", "Merchant on the Same Road"),
                    // 弹窗描述：邀请结伴同行
                    LWNTextHelper.ResolveText("LWN_journey_fellow_merchant_desc", "A traveling merchant heading the same way invites you to ride together for a while. He mentions some road conditions worth knowing."),
                    // 选项A：欣然同行
                    LWNTextHelper.ResolveText("LWN_journey_choice_join_gladly", "Gladly Join"),
                    () =>
                    {
                        Hero.MainHero.AddSkillXp(DefaultSkills.Scouting, 40);
                        // 任务日志：商人分享目的地情报
                        quest.AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_journey_log_merchant_intel", "The merchant shares some important intel about your destination.")));
                    },
                    // 选项B：婉拒好意
                    LWNTextHelper.ResolveText("LWN_journey_choice_decline", "Politely Decline"),
                    () =>
                    {
                        // 任务日志：礼貌拒绝独行更快
                        quest.AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_journey_log_declined", "You politely decline — traveling alone is faster.")));
                    });
            }
            else
            {
                // 天候突变 → 纯通知
                NotificationPipeline.Notify(
                    // 弹窗通知：暴雨放慢脚步（也遮蔽行踪）
                    LWNTextHelper.ResolveText("LWN_journey_storm_notify", "A sudden downpour forces you to slow your pace. But maybe, under this curtain of rain, your enemies will have a harder time spotting you."),
                    "normal");
                // 任务日志：暴雨倾盆行路艰难
                quest.AddLog(new TextObject(LWNTextHelper.ResolveText("LWN_journey_log_storm", "The rain pours down; the road grows rough.")));
            }

            // LLM 增强
            if (Settings.Instance.IsLLMConfigured)
                _ = EnhanceJourneyEvent(quest);
        }

        private static async System.Threading.Tasks.Task EnhanceJourneyEvent(CommissionQuest quest)
        {
            try
            {
                // LLM 提示词：给旅途事件生成一句话风味描写（世界观名占位）
                string prompt = LWNTextHelper.ResolveCompound("LWN_journey_llm_flavor_prompt", "Add a short flavor description to this journey event ({WORLD_DESCRIPTION} world setting, within 30 characters):", ("WORLD_DESCRIPTION", Settings.Instance.WorldDescription));
                string result = await LLMService.Instance.ChatAsync(prompt, 60, false);
                if (!string.IsNullOrEmpty(result))
                    quest.AddLog(new TextObject(result.Trim()));
            }
            catch { }
        }
    }
}
