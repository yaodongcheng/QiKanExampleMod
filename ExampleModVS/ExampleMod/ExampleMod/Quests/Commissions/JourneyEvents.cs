using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 旅途事件表：委托旅途中随机触发的小事件。
    /// 模板文本为主，有 LLM 时增强 flavor。
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
                // 受伤旅行者
                quest.AddLog(new TextObject("路边遇到一位受伤的旅行者。你用 Medicine 技能帮他处理了伤口，他感激地告诉你一些前方的情报。"));
                Hero.MainHero.AddSkillXp(DefaultSkills.Medicine, 30);
            }
            else if (roll < 0.5f)
            {
                // 困住的车队
                quest.AddLog(new TextObject("一辆货车陷进了泥里。你用 Engineering 技能帮他们修好了车轮，车主给了你一些谢礼。"));
                Hero.MainHero.AddSkillXp(DefaultSkills.Engineering, 20);
                if (MobileParty.MainParty != null)
                {
                    // 小报酬：给点食物
                    AgentControlHelper.TransferGold(null, Hero.MainHero, 30);
                }
            }
            else if (roll < 0.75f)
            {
                // 路遇同行
                quest.AddLog(new TextObject("遇到一个同方向的旅行商人。结伴走了一段，他分享了一些关于目的地的有用情报。"));
                Hero.MainHero.AddSkillXp(DefaultSkills.Scouting, 40);
            }
            else
            {
                // 天候突变
                quest.AddLog(new TextObject("突如其来的暴雨让你不得不放慢脚步。但也许多了这层雨幕，敌人也不容易发现你。"));
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
