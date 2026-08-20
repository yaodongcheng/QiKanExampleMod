using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 随从画像统计（2026-08-16 方案 Q，cognition-sync-and-bragging-detection.md）：
    /// 「逢赌必输」「从没打过败仗」这类长期印象需要**确定性计数**（玩家视角事实聚合），
    /// 不靠 LLM 从 8 条动态记忆偶然聚合（会编）。
    /// 计数挂钩（在 D 广播处累加，同点原子）：battle_win / battle_lose / crime / imprison。
    /// 注入 = 触发式（聊过才注入，对齐 I1——不聊零开销）：【主公的成色】行。
    /// 存档：MyBehavior.SyncData 按既有 JSON 小 key 纪律（SaveStringGuard.GuardJson）。
    /// </summary>
    public static class PlayerImageStore
    {
        public static int BattleWins;
        public static int BattleLosses;
        public static int Crimes;
        public static int Imprisonments;

        public static void RecordBattle(bool won) { if (won) BattleWins++; else BattleLosses++; }
        public static void RecordCrime() { Crimes++; }
        public static void RecordImprisonment() { Imprisonments++; }

        /// <summary>【主公的成色】触发式注入行（方案 Q2，聊过战绩才注入，~20 token）：
        /// 计数为 0 的省略；全 0（早期游戏）→ 返回空串（无画像可说，模糊答）。</summary>
        public static string BuildRecordLine()
        {
            var parts = new System.Collections.Generic.List<string>();
            int battles = BattleWins + BattleLosses;
            // 本地化：LWN_prompt_record_battles（咱们随您打了 {BATTLES} 仗，赢了 {WINS}，双桶）
            if (battles > 0)
                // 本地化：LWN_prompt_record_battles（双桶）
                parts.Add(LWNTextHelper.ResolveCompound("LWN_prompt_record_battles", ("BATTLES", battles.ToString()), ("WINS", BattleWins.ToString())));
            // 本地化：LWN_prompt_record_imprisonments（您被擒过 {COUNT} 回，双桶）
            if (Imprisonments > 0)
                // 本地化：LWN_prompt_record_imprisonments（双桶）
                parts.Add(LWNTextHelper.ResolveCompound("LWN_prompt_record_imprisonments", ("COUNT", Imprisonments.ToString())));
            // 本地化：LWN_prompt_record_crimes（犯过 {COUNT} 回事，双桶）
            if (Crimes > 0)
                // 本地化：LWN_prompt_record_crimes（双桶）
                parts.Add(LWNTextHelper.ResolveCompound("LWN_prompt_record_crimes", ("COUNT", Crimes.ToString())));
            if (parts.Count == 0) return "";
            // 🔴 2026-08-17（称呼纪律 A 层）：段标题不再写死"主公"——【X 的成色】运行时拼玩家名
            //（无主英雄时兜底"主公"，同 ImReplyService 玩家名兜底 B 层惯例）
            // 本地化：LWN_prompt_section_record（【{NAME}的成色】，双桶）
            // 本地化：LWN_prompt_record_join（；）/ LWN_prompt_record_end（。），双桶
            return LWNTextHelper.ResolveCompound("LWN_prompt_section_record", ("NAME", Hero.MainHero?.Name?.ToString() ?? "主公"))
                + string.Join(LWNTextHelper.ResolvePrompt("LWN_prompt_record_join"), parts)
                // 本地化：LWN_prompt_record_end（双桶）
                + LWNTextHelper.ResolvePrompt("LWN_prompt_record_end");
        }

        public static string Serialize()
        {
            try
            {
                return JsonConvert.SerializeObject(new
                {
                    w = BattleWins, l = BattleLosses, c = Crimes, i = Imprisonments,
                });
            }
            catch { return "{}"; }
        }

        public static void Deserialize(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return;
                var o = JsonConvert.DeserializeObject<dynamic>(json);
                BattleWins = o?.w ?? 0;
                BattleLosses = o?.l ?? 0;
                Crimes = o?.c ?? 0;
                Imprisonments = o?.i ?? 0;
            }
            catch { /* 旧档/损坏 → 归零计数（正确：画像从零积累） */ }
        }
    }
}
