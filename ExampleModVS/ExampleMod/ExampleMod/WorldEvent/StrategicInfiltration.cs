using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs
{
    /// <summary>卧底叛变状态。</summary>
    public enum InfiltrationStatus
    {
        None,           // 无关系
        Available,      // 条件满足，可以策反
        Negotiating,    // 谈判中
        Active,         // 已策反，等待下次同场战斗触发
        Triggered,      // 已触发叛变
        Expired,        // 过期未触发
    }

    /// <summary>
    /// 单条卧底叛变记录。JSON 序列化存档。
    /// </summary>
    [Serializable]
    public class InfiltrationRecord
    {
        public string HeroId;
        public string HeroName;
        public InfiltrationStatus Status;
        public float CreatedDay;
        public float ExpiryDay;
        public int CostGold;
        public string RelatedEventId;

        [JsonIgnore]
        public Hero Hero => string.IsNullOrEmpty(HeroId)
            ? null : Hero.FindFirst(h => h.StringId == HeroId);
    }

    /// <summary>
    /// 卧底叛变系统（计划 5.4）。
    ///
    /// 条件：玩家与敌方 Hero 关系 > 60 + 曾帮他解决过 WorldEvent + 非 Nemesis
    /// 效果：在下次同场 MapEvent 中该 Hero 切换阵营支援玩家
    /// 代价：金币
    /// </summary>
    public static class StrategicInfiltration
    {
        private static Dictionary<string, InfiltrationRecord> _records
            = new Dictionary<string, InfiltrationRecord>();

        /// <summary>
        /// 检查是否可以对某个 Hero 发起策反。
        /// 在 CommissionQuest 完成时（resolve WorldEvent）调用。
        /// </summary>
        public static bool CheckAvailability(Hero hero, string eventId)
        {
            if (hero == null || string.IsNullOrEmpty(hero.StringId)) return false;
            if (hero == Hero.MainHero) return false;
            if (hero.Clan == Clan.PlayerClan) return false; // 已经是自己人

            float relation = hero.GetRelationWithPlayer();
            if (relation < 60f) return false;

            // 不是宿敌
            var nemesisRecord = HeroNemesisTracker.GetRecord(hero);
            if (nemesisRecord != null && nemesisRecord.Level >= NemesisLevel.Nemesis) return false;

            // 已有活跃记录
            if (_records.TryGetValue(hero.StringId, out var existing))
            {
                if (existing.Status == InfiltrationStatus.Active || existing.Status == InfiltrationStatus.Negotiating)
                    return false;
            }

            // 创建可用记录
            _records[hero.StringId] = new InfiltrationRecord
            {
                HeroId = hero.StringId,
                // 叛变对象名兜底：无名
                HeroName = hero.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_infiltration_unnamed", "Unknown"),
                Status = InfiltrationStatus.Available,
                RelatedEventId = eventId,
            };

            DebugLogger.Log($"[Infiltration] Available: {hero.Name} (relation={relation})");
            return true;
        }

        /// <summary>
        /// 玩家发起策反谈判。花费金币，30 天内有效。
        /// 下次同场战斗自动触发叛变。
        /// </summary>
        public static bool Negotiate(Hero hero, int costGold)
        {
            if (hero == null || !_records.TryGetValue(hero.StringId, out var record)) return false;
            if (record.Status != InfiltrationStatus.Available) return false;

            // 扣钱
            if (Hero.MainHero.Gold < costGold) return false;
            AgentControlHelper.TransferGold(Hero.MainHero, null, costGold);

            record.Status = InfiltrationStatus.Active;
            record.CostGold = costGold;
            record.CreatedDay = (float)CampaignTime.Now.ToDays;
            record.ExpiryDay = record.CreatedDay + 30f; // 30 天有效期

            DebugLogger.Log($"[Infiltration] Activated: {hero.Name} for {costGold} gold, valid 30 days");
            return true;
        }

        /// <summary>
        /// 在 MapEvent 中检查是否有可触发的卧底叛变。
        /// 如果敌方阵营中有已策反的 Hero → 切换阵营支援玩家。
        /// 调用点：CommissionQuest.OnMapEventEnded 或独立的战斗监听。
        /// </summary>
        public static Hero CheckBattlefieldTrigger()
        {
            // 清理过期记录
            float now = (float)CampaignTime.Now.ToDays;
            var expired = _records.Values
                .Where(r => r.Status == InfiltrationStatus.Active && now > r.ExpiryDay)
                .ToList();
            foreach (var r in expired)
                r.Status = InfiltrationStatus.Expired;

            // 找一个活跃的卧底
            var active = _records.Values
                .FirstOrDefault(r => r.Status == InfiltrationStatus.Active && r.Hero != null && r.Hero.IsAlive);

            if (active != null)
            {
                active.Status = InfiltrationStatus.Triggered;
                DebugLogger.Log($"[Infiltration] Triggered: {active.HeroName} switches sides!");
                return active.Hero;
            }

            return null;
        }

        /// <summary>获取某个 Hero 的叛变记录（供 UI 显示）。</summary>
        public static InfiltrationRecord GetRecord(Hero hero)
        {
            if (hero == null) return null;
            return _records.TryGetValue(hero.StringId, out var r) ? r : null;
        }

        /// <summary>获取所有可用/活跃的叛变记录。</summary>
        public static List<InfiltrationRecord> GetAvailableInfiltrations()
        {
            return _records.Values
                .Where(r => r.Status == InfiltrationStatus.Available || r.Status == InfiltrationStatus.Active)
                .OrderByDescending(r => r.Status == InfiltrationStatus.Active ? 0 : 1)
                .ToList();
        }

        /// <summary>🔴 2026-08-23（跨档残留修复）：新档创建时清空（同进程主菜单直接开新档会残留旧档卧底记录）。</summary>
        public static void ResetAll()
        {
            _records.Clear();
        }

        #region Persistence

        public static string Serialize()
        {
            try
            {
                return JsonConvert.SerializeObject(_records.Values.ToList(), Formatting.None);
            }
            catch { return "[]"; }
        }

        public static void Deserialize(string json)
        {
            _records.Clear();
            if (string.IsNullOrEmpty(json) || json == "[]") return;
            try
            {
                var list = JsonConvert.DeserializeObject<List<InfiltrationRecord>>(json);
                if (list != null)
                    foreach (var r in list)
                        if (r != null && !string.IsNullOrEmpty(r.HeroId))
                            _records[r.HeroId] = r;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Infiltration] Deserialize error: {ex.Message}");
            }
        }

        #endregion
    }
}
