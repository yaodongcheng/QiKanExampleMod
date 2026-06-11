using LivingWorldNpcs;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.Story
{
    /// <summary>
    /// 无 LLM 模板台词查表。台词存 GameDatabase.Dialogue（CSV，内容包可注入）。
    /// ID 约定：对抗类 = "{GoalType}_Success" / "{GoalType}_Fail"；闲聊话题 = "Chat_Greeting" 等；命令 = "Order"。
    /// 占位符 {PLAYER}/{NPC}/{WORLD}/{TERM_LORD} 用 Settings.Instance 世界观字段替换（守不硬编码铁律）。
    /// </summary>
    public static class DialogueTemplateHelper
    {
        /// <summary>对抗类：按成败取台词。</summary>
        public static string Get(string dialogueKey, bool success, out string emotion, Hero target, Agent agent)
        {
            return Lookup(dialogueKey + (success ? "_Success" : "_Fail"), out emotion, target, agent, success);
        }

        /// <summary>即时类/话题：按完整 ID 取台词。</summary>
        public static string Get(string id, out string emotion, Hero target, Agent agent)
        {
            return Lookup(id, out emotion, target, agent, true);
        }

        private static string Lookup(string id, out string emotion, Hero target, Agent agent, bool success)
        {
            emotion = "normal";
            string raw = null;
            try
            {
                var rec = GameDatabase.Dialogue != null ? GameDatabase.Dialogue.GetByID(id) : null;
                if (rec != null)
                {
                    var lines = rec.GetList("Lines");
                    if (lines != null && lines.Count > 0)
                        raw = lines[MBRandom.RandomInt(lines.Count)];
                    string emo = rec.GetString("Emotion", "normal");
                    if (!string.IsNullOrEmpty(emo)) emotion = emo;
                }
            }
            catch { /* 表缺失/异常一律走兜底 */ }

            if (string.IsNullOrEmpty(raw))
            {
                // P11 兜底：纯 Mod A（无内容包）时台词表为空，给通用句
                raw = success ? "……（微微颔首，似是默许了）" : "……（摇了摇头，并未应允）";
                emotion = success ? "positive" : "negative";
            }
            return ApplyPlaceholders(raw, target, agent);
        }

        private static string ApplyPlaceholders(string raw, Hero target, Agent agent)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            string playerName = Hero.MainHero != null && Hero.MainHero.Name != null ? Hero.MainHero.Name.ToString() : "你";
            string npcName = target != null && target.Name != null
                ? target.Name.ToString()
                : (agent != null && agent.Name != null ? agent.Name.ToString() : "对方");
            string world = Settings.Instance != null ? Settings.Instance.WorldDescription : "";
            return raw
                .Replace("{PLAYER}", playerName)
                .Replace("{NPC}", npcName)
                .Replace("{WORLD}", world ?? "")
                .Replace("{TERM_LORD}", "大人");
        }
    }
}
