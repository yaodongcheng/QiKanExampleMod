using System.Collections.Generic;
using System.Linq;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 村庄动物偷窃持久化追踪 + 场景自然动物数缓存。
    ///
    /// 两层数据（均按 "settlementId|monsterId" 为 key）：
    ///   _stolenCounts     — 被偷走且尚未恢复的只数（每日衰减）
    ///   _naturalCounts    — 第一次进场景时缓存该村庄每种动物的自然生成数（不衰减）
    ///
    /// 三向闭环：
    ///   进场景 → 缓存自然数 + 按偷窃记录裁剪 + ItemRoster 补足
    ///   开菜单 → 读缓存自然数 → ItemRoster 补足（无需进场景）
    ///   偷动物 → RecordTheft + ItemRoster 扣减 + FadeOut
    ///   DailyTick → DecayDaily 自然恢复（每天每种被偷动物恢复 1 只）
    /// </summary>
    public static class VillageAnimalTracker
    {
        /// <summary>key = "settlementId|monsterId", value = 被偷走且尚未恢复的只数</summary>
        private static Dictionary<string, int> _stolenCounts = new Dictionary<string, int>();

        /// <summary>key = "settlementId|monsterId", value = 场景自然生成数（首次进场景时缓存，不衰减）</summary>
        private static Dictionary<string, int> _naturalCounts = new Dictionary<string, int>();

        // ── 偷窃追踪 ──

        public static void RecordTheft(string settlementId, string monsterId, int count = 1)
        {
            if (string.IsNullOrEmpty(settlementId) || string.IsNullOrEmpty(monsterId)) return;
            string key = $"{settlementId}|{monsterId}";
            _stolenCounts.TryGetValue(key, out int current);
            _stolenCounts[key] = current + count;
        }

        public static int GetStolenCount(string settlementId, string monsterId)
        {
            if (string.IsNullOrEmpty(settlementId) || string.IsNullOrEmpty(monsterId)) return 0;
            string key = $"{settlementId}|{monsterId}";
            _stolenCounts.TryGetValue(key, out int count);
            return count;
        }

        public static void DecayDaily()
        {
            if (_stolenCounts.Count == 0) return;
            var keys = _stolenCounts.Keys.ToList();
            foreach (var key in keys)
            {
                _stolenCounts[key]--;
                if (_stolenCounts[key] <= 0)
                    _stolenCounts.Remove(key);
            }
        }

        // ── 自然动物数缓存 ──

        /// <summary>缓存某村庄某种动物的场景自然生成数</summary>
        public static void SetNaturalCount(string settlementId, string monsterId, int count)
        {
            if (string.IsNullOrEmpty(settlementId) || string.IsNullOrEmpty(monsterId)) return;
            string key = $"{settlementId}|{monsterId}";
            _naturalCounts[key] = count;
        }

        /// <summary>获取某村庄某种动物的缓存自然数（未缓存返回 0）</summary>
        public static int GetNaturalCount(string settlementId, string monsterId)
        {
            if (string.IsNullOrEmpty(settlementId) || string.IsNullOrEmpty(monsterId)) return 0;
            string key = $"{settlementId}|{monsterId}";
            _naturalCounts.TryGetValue(key, out int count);
            return count;
        }

        /// <summary>该村庄是否已有自然数缓存（至少一种动物）</summary>
        public static bool HasNaturalCache(string settlementId)
        {
            if (string.IsNullOrEmpty(settlementId)) return false;
            string prefix = $"{settlementId}|";
            return _naturalCounts.Keys.Any(k => k.StartsWith(prefix));
        }

        // ── 序列化 ──

        public static string Serialize()
        {
            try
            {
                var data = new Dictionary<string, object>
                {
                    { "stolen", _stolenCounts },
                    { "natural", _naturalCounts }
                };
                return Newtonsoft.Json.JsonConvert.SerializeObject(data);
            }
            catch { return "{}"; }
        }

        public static void Deserialize(string json)
        {
            try
            {
                var dict = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (dict == null) return;

                if (dict.TryGetValue("stolen", out var stolen))
                    _stolenCounts = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, int>>(stolen.ToString()) ?? new Dictionary<string, int>();

                if (dict.TryGetValue("natural", out var natural))
                    _naturalCounts = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, int>>(natural.ToString()) ?? new Dictionary<string, int>();
            }
            catch { _stolenCounts = new Dictionary<string, int>(); _naturalCounts = new Dictionary<string, int>(); }
        }
    }
}
