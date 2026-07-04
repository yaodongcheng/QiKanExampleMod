using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Settlements;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 据点荣誉存储：按 Settlement.StringId 记录玩家在各据点的荣誉值。
    /// 持久化经 MyBehavior.SyncData（JSON 序列化）。
    /// Modify() 可正可负，后续坏事件扣、任务完成涨均走同一入口。
    /// </summary>
    public static class SettlementHonorStore
    {
        private static Dictionary<string, int> _honor = new Dictionary<string, int>();

        public static int Get(Settlement s)
        {
            if (s == null) return 0;
            return Get(s.StringId);
        }

        public static int Get(string settlementId)
        {
            if (string.IsNullOrEmpty(settlementId)) return 0;
            _honor.TryGetValue(settlementId, out int v);
            return v;
        }

        public static void Modify(Settlement s, int delta)
        {
            if (s == null) return;
            int cur = Get(s.StringId);
            Set(s, cur + delta);
        }

        public static void Set(Settlement s, int value)
        {
            if (s == null) return;
            _honor[s.StringId] = value;
        }

        public static string Serialize()
        {
            try { return Newtonsoft.Json.JsonConvert.SerializeObject(_honor); }
            catch { return "{}"; }
        }

        public static void Deserialize(string json)
        {
            try
            {
                var d = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, int>>(json);
                _honor = d ?? new Dictionary<string, int>();
            }
            catch { _honor = new Dictionary<string, int>(); }
        }
    }
}
