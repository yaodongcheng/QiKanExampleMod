using System.Collections.Generic;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs.Story
{
    /// <summary>
    /// 意图冷却存储：失败的对抗意图（求婚/招募/策反…）在一段游戏时间内不能对同一 NPC 重复发起。
    /// key = "heroId|goalType"，value = 到期的游戏天数 (CampaignTime.ToDays)。
    ///
    /// 记忆系统 SingNpcMemorySystem 全程内存、不进存档，所以冷却不放那里。
    /// 这里用静态字典做运行时状态，由 MyBehavior.SyncData 以 JSON 字符串持久化（跨存档）。
    /// </summary>
    public static class IntentCooldownStore
    {
        private static Dictionary<string, double> _expiryDays = new Dictionary<string, double>();

        private static string Key(Hero hero, NegotiationGoalType goal)
        {
            string id = hero != null ? hero.StringId : "unknown";
            return id + "|" + goal.ToString();
        }

        /// <summary>当前是否处于冷却中。</summary>
        public static bool IsOnCooldown(Hero hero, NegotiationGoalType goal)
        {
            if (hero == null) return false;
            if (_expiryDays.TryGetValue(Key(hero, goal), out double expiry))
                return CampaignTime.Now.ToDays < expiry;
            return false;
        }

        /// <summary>还剩多少天解冻（向上取整，最小 1，用于置灰提示）。</summary>
        public static int DaysLeft(Hero hero, NegotiationGoalType goal)
        {
            if (hero == null) return 0;
            if (_expiryDays.TryGetValue(Key(hero, goal), out double expiry))
            {
                double left = expiry - CampaignTime.Now.ToDays;
                if (left <= 0) return 0;
                return (int)System.Math.Ceiling(left);
            }
            return 0;
        }

        /// <summary>设置冷却：从现在起 days 天内不可再发起。</summary>
        public static void Set(Hero hero, NegotiationGoalType goal, float days)
        {
            if (hero == null || days <= 0f) return;
            _expiryDays[Key(hero, goal)] = CampaignTime.Now.ToDays + days;
        }

        // ── 存档序列化（供 MyBehavior.SyncData 调用）──

        public static string Serialize()
        {
            try { return JsonConvert.SerializeObject(_expiryDays); }
            catch { return "{}"; }
        }

        public static void Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json)) { _expiryDays = new Dictionary<string, double>(); return; }
            try
            {
                _expiryDays = JsonConvert.DeserializeObject<Dictionary<string, double>>(json)
                              ?? new Dictionary<string, double>();
            }
            catch { _expiryDays = new Dictionary<string, double>(); }
        }
    }
}
