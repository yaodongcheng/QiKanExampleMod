using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 非 LLM 语义检索（用户决策 1：群聊挑「最希望回复的人」用规则引擎，零 LLM 调用）：
    /// 玩家文本 → 关键词命中主题 → 职业亲和度 + 热度加成 + 随机抖动 → 打分挑人。
    ///
    /// 词表是 C# 静态数组（单一事实源，v1 写死；要改就改本文件，注释标明主题归属）。
    /// 主题命中用 contains 匹配（中文无分词器，子串命中是低成本近似，够用）。
    /// </summary>
    public static class ImTopicMatcher
    {
        /// <summary>主题 → 关键词（中英双语 + 游戏黑话）。v1 十个主题。</summary>
        private static readonly Dictionary<string, string[]> TopicKeywords = new Dictionary<string, string[]>
        {
            ["combat"] = new[] { "战斗", "打", "杀", "军队", "敌", "仗", "兵", "攻城", "守城", "出击", "attack", "fight", "battle", "enemy", "army", "war", "kill" },  // lwn-ignore: A
            ["trade"] = new[] { "钱", "金", "买卖", "价格", "商队", "贸易", "第纳尔", "贵", "便宜", "赚", "买", "卖", "gold", "trade", "merchant", "price", "denar", "money", "buy", "sell" },  // lwn-ignore: A
            ["food"] = new[] { "粮食", "吃", "饿", "麦子", "收成", "酒", "菜", "food", "hunger", "hungry", "harvest", "wheat", "bread", "drink" },  // lwn-ignore: A
            ["crime"] = new[] { "偷", "贼", "犯罪", "抢劫", "强盗", "扒手", "监狱", "罚", "steal", "thief", "crime", "robber", "prison", "stolen" },  // lwn-ignore: A
            ["news"] = new[] { "听说", "消息", "传闻", "八卦", "新闻", "hear", "news", "rumor", "gossip", "know about" },  // lwn-ignore: A
            ["family"] = new[] { "家人", "家里", "回家", "妻子", "丈夫", "孩子", "儿子", "女儿", "父母", "嫁", "娶", "family", "wife", "husband", "son", "daughter", "child" },  // lwn-ignore: A
            ["health"] = new[] { "伤", "病", "疼", "治", "药", "康复", "受伤", "hurt", "wound", "wounded", "sick", "heal", "medicine" },  // lwn-ignore: A
            ["location"] = new[] { "哪里", "去哪儿", "在哪", "位置", "附近", "城镇", "村庄", "堡垒", "where", "location", "near", "town", "village", "castle" },  // lwn-ignore: A
            ["greeting"] = new[] { "你好", "在吗", "嗨", "哈喽", "早上好", "晚上好", "hello", "hi", "hey", "greetings" },  // lwn-ignore: A
        };

        /// <summary>兜底主题（任何文本至少命中一个，保证总能打分）。</summary>
        private const string DefaultTopic = "default";

        /// <summary>职业（NPCProfile.Occupation 中文取值）→ 高亲和主题（2 分），其余主题 0.5 分。</summary>
        private static readonly Dictionary<string, string[]> OccupationTopics = new Dictionary<string, string[]>
        {
            ["贵族"] = new[] { "combat", "family", "location" },  // lwn-ignore: A
            ["商人"] = new[] { "trade", "news", "location" },  // lwn-ignore: A
            ["帮派头目"] = new[] { "crime", "combat", "trade" },  // lwn-ignore: A
            ["游民"] = new[] { "news", "location", "food" },  // lwn-ignore: A
            ["足轻"] = new[] { "combat", "location", "health" },  // lwn-ignore: A
            ["村民"] = new[] { "food", "family", "crime" },  // lwn-ignore: A
        };

        /// <summary>命中主题（空文本 → 空；无命中 → [default]）。</summary>
        public static List<string> MatchTopics(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (var kv in TopicKeywords)
            {
                foreach (var kw in kv.Value)
                {
                    if (text.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result.Add(kv.Key);
                        break;
                    }
                }
            }
            if (result.Count == 0) result.Add(DefaultTopic);
            return result;
        }

        /// <summary>该 NPC 对指定主题的亲和权重（职业命中 2.0，未命中 0.5；主题都不中时用 default 0.5）。</summary>
        public static float Affinity(string occupation, string topic)
        {
            if (string.IsNullOrEmpty(occupation)) return 0.5f;
            if (OccupationTopics.TryGetValue(occupation, out var topics) && topics.Contains(topic))
                return 2.0f;
            return 0.5f;
        }

        /// <summary>
        /// 挑群聊回复者：score = Σ(命中主题 × 职业亲和) + 热度加成(0~4) + 随机抖动(0~2)。
        /// 返回 (主回复者, 跟随回复者)。跟随回复者仅当 <see cref="Settings.ImGroupFollowUpChance"/>
        /// 掷中且成员 ≥ 2 时非 null（10% 概率其他人跟着回复，用户决策 1，概率可调）。
        /// 纯规则、零 LLM；全部成员不可用时返回 (null, null)。
        /// </summary>
        public static (Hero primary, Hero followUp) PickRepliers(List<Hero> members, string playerText)
        {
            if (members == null || members.Count == 0) return (null, null);

            var topics = MatchTopics(playerText);

            var scored = new List<(Hero hero, float score)>();
            foreach (var h in members)
            {
                if (h == null || h == Hero.MainHero) continue;
                string occupation = AllNpcMemoryManager.GetMemory(h.StringId)?._profile?.Occupation;
                float score = 0f;
                foreach (var t in topics)
                    score += Affinity(occupation, t);
                // @提及优先（微信群聊语义）：玩家点名（文本含其名字）→ 大幅加分，必回
                string heroName = h.Name?.ToString() ?? "";
                if (!string.IsNullOrEmpty(heroName)
                    && playerText.IndexOf(heroName, StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 5f;
                score += ImHeatTracker.ReplyBonus(h.StringId);
                score += MBRandom.RandomFloat * 2f; // 抖动：热度/职业相近时不完全可预测
                scored.Add((h, score));
            }
            if (scored.Count == 0) return (null, null);

            scored.Sort((a, b) => b.score.CompareTo(a.score));
            var primary = scored[0].hero;

            Hero followUp = null;
            if (scored.Count >= 2 && MBRandom.RandomFloat < Settings.Instance.ImGroupFollowUpChance)
                followUp = scored[1].hero;

            return (primary, followUp);
        }
    }
}
