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
                    "路边伤者",
                    "路边遇到一位受伤的旅行者，他向你求助。帮他处理伤口可能学到一些急救技巧，但也可能是个陷阱……",
                    "停下救治",
                    () =>
                    {
                        Hero.MainHero.AddSkillXp(DefaultSkills.Medicine, 30);
                        quest.AddLog(new TextObject("你停下来帮旅行者处理了伤口，他感激地告诉你一些前方的情报。"));
                    },
                    "继续赶路",
                    () =>
                    {
                        quest.AddLog(new TextObject("你瞥了一眼伤者，继续赶路。"));
                    });
            }
            else if (roll < 0.5f)
            {
                // 困住的车队 → 选择：帮忙 (+Engineering +Gold) / 路过
                NotificationPipeline.PopupWithChoice(
                    "陷进泥里的货车",
                    "一辆货车陷进了泥里，车主焦急地挥手求助。帮忙修好车轮可能需要一些工程技巧……",
                    "出手相助",
                    () =>
                    {
                        Hero.MainHero.AddSkillXp(DefaultSkills.Engineering, 20);
                        AgentControlHelper.TransferGold((Hero)null, Hero.MainHero, 30);
                        quest.AddLog(new TextObject("你用 Engineering 技能帮他们修好了车轮，车主给了你一些谢礼。"));
                    },
                    "爱莫能助",
                    () =>
                    {
                        quest.AddLog(new TextObject("你摇了摇头，驱马绕过货车继续前进。"));
                    });
            }
            else if (roll < 0.75f)
            {
                // 路遇同行 → 选择：结伴 (+Scouting) / 独行
                NotificationPipeline.PopupWithChoice(
                    "同路的商人",
                    "一个同方向的旅行商人邀请你结伴走一段。他说这一带有些值得注意的路况。",
                    "欣然同行",
                    () =>
                    {
                        Hero.MainHero.AddSkillXp(DefaultSkills.Scouting, 40);
                        quest.AddLog(new TextObject("商人分享了一些关于目的地的重要情报。"));
                    },
                    "婉拒好意",
                    () =>
                    {
                        quest.AddLog(new TextObject("你礼貌地拒绝了——独行更快。"));
                    });
            }
            else
            {
                // 天候突变 → 纯通知
                NotificationPipeline.Notify(
                    "突如其来的暴雨让你不得不放慢脚步。但也许多了这层雨幕，敌人也不容易发现你。",
                    "normal");
                quest.AddLog(new TextObject("暴雨倾盆，行路艰难。"));
            }

            // LLM 增强
            if (Settings.Instance.IsLLMReady)
                _ = EnhanceJourneyEvent(quest);
        }

        private static async System.Threading.Tasks.Task EnhanceJourneyEvent(CommissionQuest quest)
        {
            try
            {
                string prompt = $"给旅途事件加一句简短的风味描写（{Settings.Instance.WorldDescription}世界观，30字以内）：";
                string result = await LLMService.Instance.ChatAsync(prompt, 60, false);
                if (!string.IsNullOrEmpty(result))
                    quest.AddLog(new TextObject(result.Trim()));
            }
            catch { }
        }
    }
}
