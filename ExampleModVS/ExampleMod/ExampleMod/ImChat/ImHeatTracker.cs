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

        // ── 群聊回复挑选的加成（选人专用，2026-08-10 上限 4→2.5）──
        // 🔴 日志实锤：热度衰减按游戏日，同一场 IM 会话内恒定——高互动者（每轮回复 +1）恒满 4.0，
        // 其他人永远追不上 → "怎么一直只有你一个人说话"。上限降到 2.5：差距缩到 1.0，
        // 抖动(0~2)能让其他人有机会开口；点名（@提及 +5）与新人沉寂补偿(2.5)依然保证必回。
        // 注意：只影响选人，不影响记忆容量档（容量档走 Get/TierOf，仍是 0~4 语义）。
        public static float ReplyBonus(string heroId) => Math.Min(Get(heroId), 2.5f);

        // ── 群聊回复挑选的沉寂补偿（没回过话/久未回话的成员优先开口，2026-08-10）──
        // 背景：新招募成员 heat=0，语义匹配 + 抖动永远挑不中 → 频道里永远是老面孔在说话。
        private static readonly Dictionary<string, double> _lastReplyAt = new Dictionary<string, double>();

        /// <summary>记录一次回复投递（墙钟秒；ImReplyService 投递成功后调用）。</summary>
        public static void RecordReply(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return;
            lock (_lastReplyAt)
            {
                _lastReplyAt[heroId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
        }

        /// <summary>沉寂补偿分（0~2.5）：从未回过话 +2.5（新人必回一次自我介绍）；
        /// 回过话按距上次回复的墙钟小时递增（每 24h +0.5，封顶 2.5）。</summary>
        public static float SilenceBonus(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return 0f;
            lock (_lastReplyAt)
            {
                if (!_lastReplyAt.TryGetValue(heroId, out var last)) return 2.5f;
                double hours = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - last) / 3600.0;
                return Math.Min(2.5f, (float)(hours / 24.0 * 0.5));
            }
        }

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
