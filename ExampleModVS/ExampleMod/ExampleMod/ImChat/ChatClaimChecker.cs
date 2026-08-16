using System;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 口嗨检测（吹牛前缀，2026-08-16 方案 C）：LLM 只能写台词不能执行行动，常声称"带你去逛街吃面"
    /// 而实际无任何执行。每次闲聊输出后检查台词声称的行动 vs 实际动作决策（actionCode / needPlan /
    /// adjustPlan），声称而零执行路径 → 台词前加（吹牛）前缀（本地化 LWN_im_bragging_tag），供持续优化观察。
    ///
    /// 判定 = 确定性 C# 关键词表 + 守卫（C5 设计裁定，2026-08-16）：
    /// 裁决必须留 C#（不可让渡）——口嗨判定 = 「声称的行动 ∈ 动作空间？」，需要动作空间的世界模型，
    /// LLM 没有（它不知道"带你去吃面"没有对应 action）；可执行的另一半（actionCode/needPlan）是引擎
    /// 真实决策，只有 C# 拿得到。不请 LLM 自评（铁律 2：JSON 不可信，自评 = 全项目最不可信的字段类型）。
    ///
    /// 🔴 本地化豁免声明（2026-08-16）：本文件所有中文/英文词表（ClaimGroups/GuardWords/
    /// YiDingActionSuffixes/EnglishPatterns）均为**检测匹配词典**（同 RAG 主题表 Keywords 口径）——
    /// 不是玩家可见文本，铁律 13 只管"玩家能看到"的文本，词表不在其列，故不进本地化 XML；
    /// 本文件唯一玩家可见的输出 =（吹牛）前缀，已走 LWN_im_bragging_tag 本地化（下方 CheckAndMark）。
    /// 中英对称性：中文为主词表（项目主要受众），英文为兜底层（11 条声称 + 独立英文守卫，
    /// 英文守卫窗口 20 字符——英文单词间距大，"if I can, I'll take you" 守卫词离声称短语远）。
    ///
    /// 与方案 J/R 联动：动作注册后动作空间自然出现——LLM 声称"我去 X 城"时动作空间有对应码 → 有执行
    /// 路径豁免 ✓；C 级未实现的声称（"我去募兵"）→ 口嗨拦截 ✓——行为空间与口嗨检测互为表里：
    /// 注册了才是真的，没注册就是吹牛。
    /// </summary>
    public static class ChatClaimChecker
    {
        // ── 声称短语表（第一人称未来行动；命中即"声称了行动"）──
        // 分组注释 = 声称语义；命中任一即 hasClaim=true。不追求完美过滤——[Bragging] 日志驱动迭代。
        private static readonly string[][] ClaimGroups =
        {
            // 带路/陪同（引擎无"带玩家走"动作，命中即口嗨）
            new[] { "带你去", "带您去", "领你去", "领您去", "陪你去", "陪您去" },
            // 请客（"你请我吃"方向性天然不匹配）
            new[] { "我请你", "我请您", "请你吃", "请您吃", "请你喝", "我请客", "请你下馆子" },
            // 这就动身
            new[] { "我这就", "这就去", "就去办", "马上就去", "立即去办", "这就动身", "即刻动身", "我去去就回" },
            // 时间承诺
            new[] { "回头我", "改天我", "明天我", "明日我", "稍后我", "待会我", "待会儿我", "晚些我", "今晚我" },
            // 包办
            new[] { "包在我身上", "放心交给我", "交给我了", "我来搞定", "我来办", "我帮你搞定", "我替你搞定",
                    "我去办", "我来想办法", "我去想办法", "我来处理", "我去处理", "我来安排", "我去安排",
                    "此事包", "这事包" },
            // 动手/找人
            new[] { "我去收拾", "我去教训", "我来教训", "我去找他", "我去出气", "我替你出气", "我给你出气" },
            // 去办某事（具体动作；🔴 2026-08-16 注：方案 J 落地后，带明确目标的这类声称走判定 2 豁免——
            // 无目标/无对应动作的空声称（"我去打听"无落点）仍口嗨）
            new[] { "我去看看", "我去打听", "我去问问", "我去查查", "我去望风", "我去盯着", "我去跟踪",
                    "我去传话", "我去叫人", "我去买", "我去拿", "我去取", "我去弄", "我去找" },
            // 强承诺（必当/定当；"我一定"单独不命中——见 YiDingActionSuffixes 收紧规则）
            new[] { "必当", "定当" },
        };

        // 🔴 2026-08-16 审查收紧："我一定"单独不命中——"我一定小心/我一定记住"是行为承诺非行动声称
        // （中文高频词，按原表必误伤）；要求后接动作性短语才命中（如"我一定去办"）。
        private static readonly string[] YiDingActionSuffixes =
        {
            "去", "办", "搞定", "处理", "找到", "安排", "盯", "拿", "问", "查", "说", "打听",
        };

        // 英文兜底（第一人称未来行动；小写比对）
        private static readonly string[] EnglishPatterns =
        {
            "i will take you", "i'll take you", "i'll bring you", "let me handle",
            "leave it to me", "i'll handle", "i'll go", "i will go", "i'll find",
            "i'll get", "i'll take care",
        };

        // 🔴 2026-08-16（本地化补齐）：英文守卫（小写比对）——英文否定形（"i will not take you"）
        // 已天然不命中声称子串（will 与 take 之间插入否定词），但转述/条件/过去时仍会误伤
        //（"He said I'll take you there" 直接引语命中 "i'll take you"）。窗口 20 字符——
        // 英文单词间距大，"if I can, I'll take you" 的 "if" 离声称短语 8 字符，中文 4 字符窗口不够。
        private static readonly string[] EnglishGuardPatterns =
        {
            // 转述他人（直接引语）
            "he said", "she said", "they said",
            // 条件式
            "if", "unless", "when",
            // 过去时/既往
            "yesterday", "last time", "earlier", "before",
            // 否定保险（防变体如 "i certainly won't take you" 类）
            "won't", "never", "wouldn't",
        };

        // ── 误伤守卫（匹配处前 4 字符内出现即跳过）──
        private static readonly string[] GuardWords =
        {
            // 否定（"我不会带你去"）
            "不", "没", "别", "无",
            // 转述他人（"他说带你去"）
            "他说", "她说",
            // 过去时（"上次我请你吃面"）
            "上次", "上回", "昨天", "之前", "当年", "刚才", "方才",
            // 条件式（"要是能带你去就好了"）
            "如果", "要是", "倘若", "只要", "万一",
        };

        /// <summary>检查台词是否声称了无法执行的动作（口嗨）→ 是：加（吹牛）前缀 + [Bragging] 日志；否：原样返回。
        /// 判定（2026-08-16 方案 C1）：
        ///   1. 无声称（HasActionClaim 全 miss）→ 原样返回
        ///   2. 有声称 + 有真实执行路径（actionCode 非空非 NONE，或 needPlan/adjustPlan 真）
        ///      → 不标前缀；actionCode 非 NONE 时打 [Bragging] 观察日志（声称≠决策）
        ///   3. 有声称 + 零执行路径（NONE 且无计划按钮）→ 口嗨：日志 + 前缀
        /// 前缀拼进消息 Content（非 SenderName）→ 随消息一起写记忆（NPC 日后可自嘲/被质疑，叙事自洽）。
        /// 前缀为玩家可见文本 → 铁律 13 走 LWNTextHelper（XML 中文「（吹牛）」/ C# fallback 英文）。</summary>
        public static string CheckAndMark(string reply, string actionCode, bool needPlan, bool adjustPlan, string speakerName)
        {
            if (string.IsNullOrWhiteSpace(reply)) return reply;
            bool hasClaim = HasActionClaim(reply);
            if (!hasClaim) return reply;                      // ① 无声称 → 原样
            bool hasExecution = (!string.IsNullOrEmpty(actionCode) && actionCode != "NONE") || needPlan || adjustPlan;
            if (hasExecution)
            {
                // ② 有声称 + 有执行路径 → 不标前缀；声称≠决策打观察日志（优化素材，不误伤）
                if (!string.IsNullOrEmpty(actionCode) && actionCode != "NONE")
                    DebugLogger.Log($"[Bragging] {speakerName} 声称行动但决策不同（action={actionCode}）: 「{Truncate(reply)}」");
                return reply;
            }
            // ③ 口嗨：声称行动、决策 NONE
            DebugLogger.Log($"[Bragging] {speakerName} 口嗨（声称行动、决策 NONE）: 「{Truncate(reply)}」");
            return LWNTextHelper.ResolveText("LWN_im_bragging_tag", "(bragging) ") + reply;
        }

        /// <summary>台词是否含行动声称（第一人称未来行动短语；守卫跳过误伤）。</summary>
        private static bool HasActionClaim(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            // 英文兜底（小写比对，命中即声称；守卫检查匹配点前 20 字符）
            string low = text.ToLowerInvariant();
            foreach (var p in EnglishPatterns)
            {
                int eIdx = 0;
                while ((eIdx = low.IndexOf(p, eIdx, StringComparison.Ordinal)) >= 0)
                {
                    if (!IsEnglishGuarded(low, eIdx)) return true;
                    eIdx += p.Length;
                }
            }
            // 中文声称表（守卫检查匹配处前 4 字符）
            for (int i = 0; i < ClaimGroups.Length; i++)
            {
                foreach (var p in ClaimGroups[i])
                {
                    int idx = text.IndexOf(p, StringComparison.Ordinal);
                    if (idx >= 0 && !IsGuarded(text, idx))
                        return true;
                }
            }
            // "我一定" + 动作后缀（收紧规则：行为承诺不误伤）
            int yd = text.IndexOf("我一定", StringComparison.Ordinal);
            while (yd >= 0)
            {
                if (!IsGuarded(text, yd))
                {
                    string tail = text.Substring(yd + 3);
                    foreach (var suf in YiDingActionSuffixes)
                    {
                        if (tail.IndexOf(suf, StringComparison.Ordinal) >= 0) return true;
                    }
                }
                yd = text.IndexOf("我一定", yd + 1, StringComparison.Ordinal);
            }
            return false;
        }

        /// <summary>守卫：匹配处前 4 字符内出现否定/转述/过去时/条件式 → 跳过（"我不会带你去"不误伤）。</summary>
        private static bool IsGuarded(string text, int idx)
        {
            int from = Math.Max(0, idx - 4);
            string ctx = text.Substring(from, idx - from);
            foreach (var g in GuardWords)
            {
                if (ctx.IndexOf(g, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        /// <summary>英文守卫：匹配点前 20 字符内出现转述/条件/过去时/否定 → 跳过。
        /// 窗口比中文大（4→20）——英文单词间距大，守卫词离声称短语常隔 5-10 字符。</summary>
        private static bool IsEnglishGuarded(string lowText, int idx)
        {
            int from = Math.Max(0, idx - 20);
            string ctx = lowText.Substring(from, idx - from);
            foreach (var g in EnglishGuardPatterns)
            {
                if (ctx.IndexOf(g, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        private static string Truncate(string s, int max = 80)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
