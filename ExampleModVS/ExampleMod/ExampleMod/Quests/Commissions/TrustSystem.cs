using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 委托信任系统：按 Hero.StringId 记录玩家在各 NPC 处的信任值。
    /// 持久化经 MyBehavior.SyncData（JSON 序列化）。
    /// </summary>
    public static class TrustSystem
    {
        private static Dictionary<string, int> _trust = new Dictionary<string, int>();

        /// <summary>获取信任值（0-100）</summary>
        public static int GetTrust(Hero hero)
        {
            if (hero == null) return 0;
            return GetTrust(hero.StringId);
        }

        public static int GetTrust(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return 0;
            _trust.TryGetValue(heroId, out int v);
            return v;
        }

        /// <summary>增减信任值（自动钳制 0-100）</summary>
        public static void AddTrust(Hero hero, int delta)
        {
            if (hero == null) return;
            int cur = GetTrust(hero.StringId);
            SetTrust(hero, cur + delta);
        }

        public static void SetTrust(Hero hero, int value)
        {
            if (hero == null) return;
            _trust[hero.StringId] = System.Math.Max(0, System.Math.Min(100, value));
        }

        /// <summary>根据信任等级获取定金比例</summary>
        public static float GetDepositRatio(int trust)
        {
            if (trust >= 81) return 0.15f;  // 心腹：15%
            if (trust >= 51) return 0.20f;  // 信赖：20%
            if (trust >= 21) return 0.25f;  // 熟人：25%
            return 0.30f;                    // 陌生人：30%
        }

        /// <summary>根据信任等级获取可同时接取委托数</summary>
        public static int GetMaxConcurrentQuests(int trust)
        {
            if (trust >= 81) return 4;  // 心腹
            if (trust >= 51) return 3;  // 信赖
            if (trust >= 21) return 2;  // 熟人
            return 1;                    // 陌生人
        }

        /// <summary>获取信任等级描述</summary>
        public static string GetTrustDescription(int trust)
        {
            // 信任等级：心腹（81+）
            if (trust >= 81) return LWNTextHelper.ResolveText("LWN_trust_level_confidant", "Confidant");
            // 信任等级：信赖（51+）
            if (trust >= 51) return LWNTextHelper.ResolveText("LWN_trust_level_trusted", "Trusted");
            // 信任等级：熟人（21+）
            if (trust >= 21) return LWNTextHelper.ResolveText("LWN_trust_level_acquaintance", "Acquaintance");
            // 信任等级：陌生人
            return LWNTextHelper.ResolveText("LWN_trust_level_stranger", "Stranger");
        }

        /// <summary>🔴 2026-08-23（跨档残留修复）：新档创建时清空（同进程主菜单直接开新档会残留旧档信任）。</summary>
        public static void ResetAll()
        {
            _trust.Clear();
        }

        #region Persistence (JSON via MyBehavior.SyncData)

        public static string Serialize()
        {
            try { return Newtonsoft.Json.JsonConvert.SerializeObject(_trust); }
            catch { return "{}"; }
        }

        public static void Deserialize(string json)
        {
            try
            {
                var d = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, int>>(json);
                _trust = d ?? new Dictionary<string, int>();
            }
            catch { _trust = new Dictionary<string, int>(); }
        }

        #endregion
    }
}
