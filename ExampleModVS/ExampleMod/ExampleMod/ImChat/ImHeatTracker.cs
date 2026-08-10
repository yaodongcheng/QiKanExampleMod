using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs
{
    /// <summary>记忆容量档位（互动热度分档，用户决策 3：互动多的 NPC 容量大）。</summary>
    public enum ImHeatTier
    {
        Hot,        // 高频互动：大容量（RecentHistory 20 轮 / 动态 8 条 / 永久 500 字）
        Normal,     // 普通：现状容量（10 轮 / 5 条 / 300 字）
        Cold,       // 冷门：小容量（4 轮 / 2 条 / 100 字）
    }

    /// <summary>
    /// 互动热度追踪：决定 NPC 记忆容量分档（Phase 5 接 SingNpcMemorySystem）与群聊回复挑选加成。
    /// 加分：面对面对话开始 +2；IM 消息（收发）各 +1；群聊发言成员 +0.5。
    /// 衰减：每游戏日 -ImHeatDecayPerDay（MyBehavior.DailyTick 调 <see cref="DecayDaily"/>）。
    /// 分档阈值进 Settings（ImHeatHotThreshold / ImHeatNormalThreshold）。
    /// 存档：独立小 key（仅存热度 > 0 的 Hero，~4KB，无需分片）。
    /// </summary>
    public static class ImHeatTracker
    {
        private static readonly Dictionary<string, float> _heat = new Dictionary<string, float>();

        public static float Get(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return 0f;
            lock (_heat)
            {
                return _heat.TryGetValue(heroId, out var v) ? v : 0f;
            }
        }

        public static void Add(string heroId, float amount)
        {
            if (string.IsNullOrEmpty(heroId) || amount <= 0f) return;
            lock (_heat)
            {
                _heat.TryGetValue(heroId, out var v);
                _heat[heroId] = v + amount;
            }
        }

        /// <summary>每日衰减（每个游戏日 -ImHeatDecayPerDay，下限 0，归零清理防字典膨胀）。</summary>
        public static void DecayDaily()
        {
            float decay = Settings.Instance.ImHeatDecayPerDay;
            if (decay <= 0f) return;
            List<string> toRemove = null;
            lock (_heat)
            {
                var keys = _heat.Keys.ToList();
                foreach (var k in keys)
                {
                    _heat[k] -= decay;
                    if (_heat[k] <= 0f)
                    {
                        if (toRemove == null) toRemove = new List<string>();
                        toRemove.Add(k);
                    }
                }
                if (toRemove != null)
                    foreach (var k in toRemove) _heat.Remove(k);
            }
        }

        /// <summary>按热度值分档（阈值从 Settings 读）。</summary>
        public static ImHeatTier TierOf(float heat)
        {
            var s = Settings.Instance;
            if (heat >= s.ImHeatHotThreshold) return ImHeatTier.Hot;
            if (heat >= s.ImHeatNormalThreshold) return ImHeatTier.Normal;
            return ImHeatTier.Cold;
        }

        public static ImHeatTier TierOf(string heroId) => TierOf(Get(heroId));

        // ── 群聊回复挑选的加成（0~4 封顶，热度高者优先被回话）──
        public static float ReplyBonus(string heroId) => Math.Min(Get(heroId), 4f);

        // ── 存档 ──

        public static string Serialize()
        {
            lock (_heat)
            {
                return JsonConvert.SerializeObject(_heat);
            }
        }

        public static void Deserialize(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return;
                var map = JsonConvert.DeserializeObject<Dictionary<string, float>>(json);
                if (map == null) return;
                lock (_heat)
                {
                    _heat.Clear();
                    foreach (var kv in map)
                    {
                        if (kv.Value > 0f) _heat[kv.Key] = kv.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImHeatTracker] Deserialize 失败: {ex.Message}");
            }
        }
    }
}
