using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════
    // SessionDialogueTemplates.cs — 说服会话模板选择器（npc-dialogue-session-plan.md §5.3.1，M1）
    //
    // 🔴 铁律 1 完整降级：没有 LLM 时必须支持完整的多轮模板会话（不是单句 fallback——
    // 换掉润色器，公式/轮次/兑现机制零改动）。模板载体 = 既有 Languages XML
    // （{=LWN_KEY}English fallback + Languages/{lang}/std_*.xml，天然多语言）。
    //
    // key 体系（意图分类为主维度——模板响应必须严格对应发起者目的）：
    //   LWN_dialog_{category}_{occupation}_{role}_{tier}_{n}   分类×职业风味
    // → LWN_dialog_{category}_{role}_{tier}_{n}                分类通用（必配最小集）
    // → LWN_dialog_{role}_{tier}_{n}                           中性兜底（目的无关，禁止带具体语义）
    //
    // {category} = move_req / affair / combat / chat（分类间严格隔离：BRING 的同意
    // "我随你去"绝不能出现在 TALK_TO 里）
    // {role}     = initiator（劝说句）/ responder（回应句）/ bystander（插嘴句）
    // {tier}     = refuse(agree<0.35) / waver(0.35~0.5) / near(0.5~0.65) / agree(≥0.65)
    //              （chat 分类只用 refuse/agree 两档——无档位演化，仅接话）
    // {n}        = 每档 1~2 句，随机选 + 会话内去重（防复读）
    //
    // 纯 C# 确定性选择器：输入 agree/role/intent/occupation/round/usedKeys，输出台词文本。
    // ═══════════════════════════════════════════════════════════════

    public static class SessionDialogueTemplates
    {
        /// <summary>意图 → 分类静态表（30 种意图按语义归类，分类间严格隔离）。</summary>
        private static readonly Dictionary<string, string> IntentCategories =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // move_req：是否愿意"移动/跟着走"
                { "BRING", "move_req" }, { "GUIDE", "move_req" }, { "LEAD", "move_req" }, { "FOLLOW", "move_req" },
                // affair：是否愿意"办这件事"
                { "TALK_TO", "affair" }, { "DELIVER", "affair" }, { "PURCHASE", "affair" },
                { "COLLECT", "affair" }, { "FETCH", "affair" },
                // combat：武力冲突下的回应
                { "ATTACK", "combat" }, { "DUEL", "combat" }, { "KNOCKOUT", "combat" },
                { "DRIVE_AWAY", "combat" }, { "ANNIHILATE", "combat" },
            };

        /// <summary>意图 → 分类（未命中 → chat 中性寒暄）。</summary>
        public static string Categorize(string intent)
        {
            if (string.IsNullOrEmpty(intent)) return "chat";
            return IntentCategories.TryGetValue(intent, out var c) ? c : "chat";
        }

        /// <summary>agree → 档位（tier）。</summary>
        private static string TierOf(float agree)
        {
            if (agree >= 0.65f) return "agree";
            if (agree > 0.5f) return "near";
            if (agree >= 0.35f) return "waver";
            return "refuse";
        }

        /// <summary>
        /// 模板台词选择（纯 C# 确定性）。返回 null = 全档耗尽/无模板（调用方兜底短句）。
        /// 查找顺序（逐级回落）：分类×职业 → 分类 → 中性兜底；会话内已用 key 不重选。
        /// </summary>
        /// <param name="agree">当前倾向（0~1）</param>
        /// <param name="role">initiator / responder / bystander</param>
        /// <param name="intent">发起者目的（BRING/TALK_TO/…；未命中 → chat）</param>
        /// <param name="occupation">接受者职业（guard/villager/…；模板职业风味档，可空）</param>
        /// <param name="round">当前轮次（未用——保留签名与计划一致）</param>
        /// <param name="usedKeys">本会话已用 key 集（防复读；可空）</param>
        public static string Resolve(float agree, string role, string intent, string occupation,
            int round, HashSet<string> usedKeys)
        {
            try
            {
                if (string.IsNullOrEmpty(role)) role = "responder";
                string category = Categorize(intent);
                string tier = TierOf(agree);
                // chat 分类无档位演化：只区分 refuse / 其余一律 agree（中性接话）
                if (category == "chat" && tier != "refuse") tier = "agree";
                // bystander 只有"听见了"接话档（计划 §5.5：模板降级"听见了"类）
                if (role == "bystander") tier = tier == "refuse" ? "refuse" : "agree";

                // 候选 key（按优先级排列）：分类×职业 → 分类 → 中性；每级 2 句（n=1,2）
                var candidates = new List<string>();
                if (!string.IsNullOrEmpty(occupation))
                {
                    AddTierKeys(candidates, $"LWN_dialog_{category}_{occupation}_{role}_{tier}", 2);
                }
                AddTierKeys(candidates, $"LWN_dialog_{category}_{role}_{tier}", 2);
                // 中性兜底（🔴 必须目的无关——"此事容我再想想"，禁止"军务在身/我随你去"这类带具体语义的文本）
                AddTierKeys(candidates, $"LWN_dialog_{role}_{tier}", 2);

                // 过滤已用 key；全用光 → 允许重复（防崩溃，保底最后一个）
                var fresh = candidates.Where(k => usedKeys == null || !usedKeys.Contains(k)).ToList();
                var pool = fresh.Count > 0 ? fresh : candidates;
                if (pool.Count == 0) return null;

                string key = pool[MBRandom.RandomInt(pool.Count)];
                usedKeys?.Add(key);
                // 铁律 13：玩家可见文本走标准本地化（{=KEY}English fallback 机制；英文兜底自动从 English 桶取）
                return LWNTextHelper.ResolveText(key);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[SessionTemplates] 解析异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>一档多个候选 key（n=1..count）追加进候选列表。
        /// 🔴 只收 XML 真实存在的 key（HasEnglishKey）——缺档（如 combat 的 waver/near）自然回落下一级，
        /// 不会选中"代码枚举但 XML 未配"的 key 显示 key 名。</summary>
        private static void AddTierKeys(List<string> candidates, string prefix, int count)
        {
            for (int n = 1; n <= count; n++)
            {
                string key = $"{prefix}_{n}";
                if (LWNTextHelper.HasEnglishKey(key))
                    candidates.Add(key);
            }
        }

        /// <summary>Δagree 方向 → 台词态度段（LLM 路径 §5.3：注入"你开始动摇"/"你态度坚决"；本地化）。</summary>
        public static string DescribeDirection(float agree)
        {
            if (agree >= 0.5f)
            {
                // 倾向松动：你开始动摇（LLM 态度段）
                string s = LWNTextHelper.ResolvePrompt("LWN_plan_persuade_direction_waver");
                return string.IsNullOrEmpty(s)
                    ? "【你此刻的态度】你有些动摇，对方的话你听进去了，开始认真考虑。"
                    : s;
            }
            // 倾向抗拒：你态度坚决（LLM 态度段）
            string firm = LWNTextHelper.ResolvePrompt("LWN_plan_persuade_direction_firm");
            return string.IsNullOrEmpty(firm)
                ? "【你此刻的态度】你态度坚决，不想答应对方，但出于礼貌还是回一句。"
                : firm;
        }
    }
}
