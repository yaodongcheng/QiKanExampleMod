using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
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

        /// <summary>复数指代词（玩家对多人喊话）：命中且成员 ≥ 2 → 强制跟随回复（跳过随机）。
        /// 用明确的复数人称词，避开"都/一起"这类泛词误伤（"我都在线"≠对多人说话）。
        /// 2026-08-13：日志实锤"你们俩都过来我这" 跟随=无——复数语义未被识别。</summary>
        private static readonly string[] PluralAddressWords = new[]
        {
            "你们", "你俩", "俩人", "两人", "二位", "两位", "几位", "各位",
            "大家", "所有人", "全体", "诸位", "众位", "兄弟们", "姐妹们", "部下们", "随从们"
        };

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

        /// <summary>@提及候选名：全名 / 去引号全名 / 引号内称号 / FirstName。
        /// 玩家口头点名常用称号或简称，全名（含引号）IndexOf 必失败（2026-08-10 日志实锤）。</summary>
        private static IEnumerable<string> GetMentionCandidates(Hero h)
        {
            if (h == null) yield break;
            string full = h.Name?.ToString() ?? "";
            if (!string.IsNullOrEmpty(full)) yield return full;
            string clean = full.Replace("“", "").Replace("”", "").Replace("\"", "");
            if (!string.IsNullOrEmpty(clean) && clean != full) yield return clean;
            int q1 = full.IndexOf('“');
            int q2 = full.LastIndexOf('”');
            if (q1 >= 0 && q2 > q1 + 1)
            {
                string title = full.Substring(q1 + 1, q2 - q1 - 1);
                if (title.Length >= 2) yield return title;
            }
            string first = h.FirstName?.ToString() ?? "";
            if (!string.IsNullOrEmpty(first) && first.Length >= 2 && first != full) yield return first;
        }

        // ───────────────────────── 个体文本指纹 + bigram 相似度（2026-08-10） ─────────────────────────
        // 背景：关键词主题表对"人名/复杂表达"全 miss → default 兜底 → 打分退化成纯热度+随机
        // （日志实锤"你知道蒙楚格吗" topics=[default]）。方案：给每个成员构建"个体知识指纹"
        // （名字/称号/职业/家族王国/百科/人设三字段/最近发言），玩家文本 2-gram 与指纹的重叠率
        // 作为话题相关分（×3 封顶）——"有没有药"只有百草药僧的指纹命中 → 只有他回。

        // 指纹缓存（StringId → (文本, 构建时间戳)；5 分钟过期——百科/人设/发言会变）
        private static readonly Dictionary<string, (string text, double ts)> _fingerprintCache =
            new Dictionary<string, (string, double)>();
        private static readonly object _fpLock = new object();
        private const double FingerprintCacheSeconds = 300;

        private static string GetFingerprint(Hero h)
        {
            if (h == null) return "";
            lock (_fpLock)
            {
                if (_fingerprintCache.TryGetValue(h.StringId, out var cached)
                    && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - cached.ts < FingerprintCacheSeconds)
                    return cached.text;
            }
            var parts = new List<string>();
            string full = h.Name?.ToString() ?? "";
            parts.Add(full);
            string first = h.FirstName?.ToString() ?? "";
            if (!string.IsNullOrEmpty(first) && first != full) parts.Add(first);
            // 家族/王国
            if (h.Clan != null) parts.Add(h.Clan.Name?.ToString() ?? "");
            if (h.Clan?.Kingdom != null) parts.Add(h.Clan.Kingdom.Name?.ToString() ?? "");
            // 人设三字段（身世/性格/本事——个体知识核心）
            var mem = AllNpcMemoryManager.GetMemory(h.StringId);
            if (mem != null)
            {
                if (!string.IsNullOrEmpty(mem.BackgroundStory)) parts.Add(mem.BackgroundStory);
                if (!string.IsNullOrEmpty(mem.Personality)) parts.Add(mem.Personality);
                if (!string.IsNullOrEmpty(mem.Specialty)) parts.Add(mem.Specialty);
                // 🔴 最近发言（该成员自己说的最近 5 条，2026-08-10 修复）：
                // 旧实现取 RecentHistory 前 5 条——玩家连发时全被玩家行占满，自己的发言进不了指纹
                // → 玩家问"谁说过百来号人"时当事人相似度=0，背锅者被随机选中（日志实锤）。
                // 过滤 SpeakerId == 本人，保证"谁说过什么谁回应"。
                int shown = 0;
                foreach (var m in mem.SnapshotRecentHistory())
                {
                    if (m == null || string.IsNullOrEmpty(m.Content)) continue;
                    if (m.SpeakerId != h.StringId) continue;
                    parts.Add(m.Content);
                    if (++shown >= 5) break;
                }
            }
            // Hero 百科文本（"库赛特可汗"这类世界认知）
            try
            {
                string enc = h.EncyclopediaText?.ToString();
                if (!string.IsNullOrWhiteSpace(enc)) parts.Add(enc);
            }
            catch { }
            string fp = string.Join(" ", parts);
            lock (_fpLock)
            {
                _fingerprintCache[h.StringId] = (fp, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            }
            return fp;
        }

        /// <summary>中文 2-gram 重叠率（0~1）：玩家文本每 2 字切片在指纹中命中的比例。
        /// 中文无分词器，bigram 是低成本近似——称号/本事/发言里的相关词会被命中。</summary>
        private static float BigramSimilarity(string text, string fingerprint)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(fingerprint)) return 0f;
            var grams = new HashSet<string>();
            for (int i = 0; i < text.Length - 1; i++)
            {
                string g = text.Substring(i, 2);
                if (!string.IsNullOrWhiteSpace(g)) grams.Add(g);
            }
            if (grams.Count == 0) return 0f;
            int hit = 0;
            foreach (var g in grams)
            {
                if (fingerprint.IndexOf(g, StringComparison.OrdinalIgnoreCase) >= 0) hit++;
            }
            return (float)hit / grams.Count;
        }

        /// <summary>
        /// 挑群聊回复者：score = Σ(命中主题 × 职业亲和) + @提及(5) + 相似度(0~3) + 热度(0~2.5) + 沉寂(0~2.5) + 随机抖动(0~2)。
        /// 返回 (主回复者, 跟随回复者)。跟随回复者仅当 <see cref="Settings.ImGroupFollowUpChance"/>
        /// 掷中且成员 ≥ 2 时非 null（10% 概率其他人跟着回复，用户决策 1，概率可调）。
        /// 纯规则、零 LLM；全部成员不可用时返回 (null, null)。
        /// 🔴 打分明细落日志（[ImTopic]）：玩家问"为什么总是他回"时直接看日志定位。
        /// </summary>
        public static (Hero primary, Hero followUp) PickRepliers(List<Hero> members, string playerText)
        {
            if (members == null || members.Count == 0) return (null, null);

            var topics = MatchTopics(playerText);
            DebugLogger.Log($"[ImTopic] 挑人 text=\"{playerText}\" 候选={string.Join(",", members.Select(m => m?.Name?.ToString() ?? "?"))} topics=[{string.Join(",", topics)}]");

            var scored = new List<(Hero hero, float score, string detail)>();
            foreach (var h in members)
            {
                if (h == null || h == Hero.MainHero) continue;
                string occupation = AllNpcMemoryManager.GetMemory(h.StringId)?._profile?.Occupation;
                float affSum = 0f;
                foreach (var t in topics)
                    affSum += Affinity(occupation, t);
                // @提及优先（微信群聊语义）：玩家点名 → 大幅加分，必回。
                // 🔴 2026-08-10 修复：全名 IndexOf 匹配不上称号/简称（玩家打"百草药僧"，
                // 全名"“百草药僧”斯唐纳夫"含引号匹配失败，日志实锤 @提及=0）——
                // 候选名 = 全名 / 去引号全名 / 引号内称号 / FirstName，任一命中即点名。
                float mention = 0f;
                foreach (var cand in GetMentionCandidates(h))
                {
                    if (cand.Length >= 2 && playerText.IndexOf(cand, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        mention = 5f;
                        break;
                    }
                }
                // 字符串相似度（个体指纹 bigram 重叠率，×3 封顶）
                float similarity = BigramSimilarity(playerText, GetFingerprint(h)) * 3f;
                float heat = ImHeatTracker.ReplyBonus(h.StringId);
                float silence = ImHeatTracker.SilenceBonus(h.StringId);
                // 🔴 不在队加权（2026-08-11 用户裁定）：招募同伴在队里天天能当面说话，
                // 家族频道应优先不在玩家部队中的成员（配偶/外派者/定居在外的家人）——IM = 远程传讯，
                // 频道是他们主要的沟通渠道。判断 = 是否属于玩家 party（在场景里时 PartyBelongedTo 不变）。
                // 加权 2.0 < @提及(5)：玩家点名依然必回，不破坏点名语义。
                float remote = (h.PartyBelongedTo == MobileParty.MainParty) ? 0f : 2f;
                float jitter = MBRandom.RandomFloat * 2f; // 抖动：热度/职业相近时不完全可预测
                float score = affSum + mention + similarity + heat + silence + remote + jitter;
                scored.Add((h, score,
                    $"职业亲和={affSum:F1} @提及={mention:F1} 相似={similarity:F1} 热度={heat:F1} 沉寂={silence:F1} 远程={remote:F1} 抖动={jitter:F1}"));
                DebugLogger.Log($"[ImTopic]   {h.Name} (Occupation={occupation ?? "?"}): {scored[scored.Count - 1].detail} → {score:F1}");
            }
            if (scored.Count == 0) return (null, null);

            scored.Sort((a, b) => b.score.CompareTo(a.score));
            var primary = scored[0].hero;

            // 🔴 跟随回复 = 纯随机（2026-08-13 用户裁定：去掉保底）。
            // 2026-08-10 曾加"满 N 条必触发"保底（0.75^7≈13% 的 7 连不中实机出现过），
            // 但保底让跟随变成可预测的固定节拍（玩家发 N 句必然看到一句），假随机比真随机更出戏。
            // 保留纯随机：跟随 = 真正的偶尔惊喜，频道冷清由主回复者兜底（玩家消息必有回应）。
            // 🔴 复数称呼例外（2026-08-13）：玩家说"你们/两位/大家" = 明确对多人喊话，
            // 此时跟随不掷随机，必然触发（scored[1] 第二位）——否则"你们俩"只有一人应答（日志实锤）。
            Hero followUp = null;
            bool pluralAddress = scored.Count >= 2
                && !string.IsNullOrWhiteSpace(playerText)
                && PluralAddressWords.Any(w => playerText.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
            if (scored.Count >= 2
                && (pluralAddress || MBRandom.RandomFloat < Settings.Instance.ImGroupFollowUpChance))
                followUp = scored[1].hero;

            DebugLogger.Log($"[ImTopic] → 主回复={primary?.Name} 跟随={followUp?.Name?.ToString() ?? "无"}{(pluralAddress ? "（复数称呼强制）" : "")}");
            return (primary, followUp);
        }
    }
}
