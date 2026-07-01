using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LivingWorldNpcs
{
    /// <summary>宿敌级别。</summary>
    public enum NemesisLevel
    {
        None = 0,       // 不是宿敌
        Rival = 1,      // 交手过 1-2 次
        Enemy = 2,      // 击败过或被击败过
        Nemesis = 3,    // 多次交手，有伤疤
        ArchNemesis = 4,// 不死不休
        Legendary = 5,  // 宿命对决
    }

    /// <summary>单次战斗结果。</summary>
    public enum BattleOutcome
    {
        PlayerWon,          // 玩家赢了
        PlayerWonAndKilled, // 玩家赢了且杀了对方
        PlayerLost,         // 玩家输了
        PlayerEscaped,      // 玩家逃了
    }

    /// <summary>
    /// 单条宿敌追踪记录（按 Hero.StringId 索引）。
    /// JSON 序列化，经 MyBehavior.SyncData 存档。
    /// </summary>
    [Serializable]
    public class NemesisRecord
    {
        public string HeroId;
        public string HeroName;              // 冗余存储（Hero 可能被删除）
        public int TimesEncountered;
        public int TimesDefeatedPlayer;
        public int TimesDefeatedByPlayer;
        public bool HasScar;                 // 被玩家击败但逃脱
        public NemesisLevel Level;
        public string GrudgeOriginEventId;   // 恩怨起源的 WorldEvent
        public float LastEncounterDay;       // 最近一次交手的游戏日
        public float NextRevengeDay;         // 下次复仇计划日（0=未计划）

        [JsonIgnore]
        public bool IsScheduledForRevenge => NextRevengeDay > 0
            && Campaign.Current != null
            && (float)CampaignTime.Now.ToDays >= NextRevengeDay;

        [JsonIgnore]
        public Hero Hero => string.IsNullOrEmpty(HeroId)
            ? null : Hero.FindFirst(h => h.StringId == HeroId);

        [JsonIgnore]
        public bool IsHeroAlive => Hero != null && Hero.IsAlive;

        /// <summary>生成描述文本。</summary>
        public string GetDescription()
        {
            string name = HeroName ?? "某人";
            return Level switch
            {
                NemesisLevel.Rival => $"{name} — 萍水相逢的对手",
                NemesisLevel.Enemy => $"{name} — 交手过的敌人",
                NemesisLevel.Nemesis => HasScar
                    ? $"{name} — 带伤疤的宿敌，那道疤还是你留下的"
                    : $"{name} — 多次交锋的宿敌",
                NemesisLevel.ArchNemesis => $"{name} — 不死不休的宿敌，每次见面都是生死战",
                NemesisLevel.Legendary => $"{name} — 宿命之敌，你们的对决已成为传说",
                _ => name
            };
        }
    }

    /// <summary>
    /// 宿敌追踪器 — 记录玩家与每个 NPC 的交手历史。
    /// 调用点：CommissionQuest.OnMapEventEnded 中检测有敌对 HeroId 的参战方。
    /// </summary>
    public static class HeroNemesisTracker
    {
        private static Dictionary<string, NemesisRecord> _records
            = new Dictionary<string, NemesisRecord>();

        public static IReadOnlyDictionary<string, NemesisRecord> AllRecords => _records;

        #region Battle Recording

        /// <summary>
        /// 记录一次战斗结果。CommissionQuest.OnMapEventEnded 中调用。
        /// </summary>
        /// <param name="hero">与玩家交战的对立 Hero</param>
        /// <param name="playerWon">玩家是否赢了</param>
        /// <param name="heroKilled">Hero 是否被杀死</param>
        public static void RecordBattleOutcome(Hero hero, bool playerWon, bool heroKilled)
        {
            if (hero == null || string.IsNullOrEmpty(hero.StringId)) return;

            var record = GetOrCreateRecord(hero);
            record.TimesEncountered++;
            record.LastEncounterDay = (float)CampaignTime.Now.ToDays;

            if (playerWon && heroKilled)
            {
                record.TimesDefeatedByPlayer++;
                // 宿敌被终结 → 封存
                record.Level = NemesisLevel.None;
                record.NextRevengeDay = 0;
                DebugLogger.Log($"[Nemesis] {hero.Name} has been slain — nemesis arc ended.");
                return;
            }

            if (playerWon)
            {
                record.TimesDefeatedByPlayer++;
                // 40% 概率留下伤疤（如果还没伤疤）
                if (!record.HasScar && MBRandom.RandomFloat < 0.4f)
                {
                    record.HasScar = true;
                    DebugLogger.Log($"[Nemesis] {hero.Name} escaped with a scar from {Hero.MainHero?.Name}.");
                }
            }
            else
            {
                record.TimesDefeatedPlayer++;
            }

            // 更新宿敌级别
            record.Level = CalculateNemesisLevel(record);

            // 如果成为了宿敌，安排复仇
            if (record.Level >= NemesisLevel.Nemesis && !heroKilled)
            {
                ScheduleRevenge(record);
            }
        }

        private static NemesisLevel CalculateNemesisLevel(NemesisRecord r)
        {
            int total = r.TimesEncountered;
            bool defeatedByPlayer = r.TimesDefeatedByPlayer > 0;
            bool defeatedPlayer = r.TimesDefeatedPlayer > 0;

            if (total >= 8 && defeatedByPlayer && defeatedPlayer) return NemesisLevel.Legendary;
            if (total >= 5 && (defeatedByPlayer || defeatedPlayer)) return NemesisLevel.ArchNemesis;
            if (total >= 3 && r.HasScar) return NemesisLevel.Nemesis;
            if (total >= 2 && (defeatedByPlayer || defeatedPlayer)) return NemesisLevel.Enemy;
            if (total >= 1) return NemesisLevel.Rival;
            return NemesisLevel.None;
        }

        #endregion

        #region Revenge

        /// <summary>安排复仇事件。等级越高，间隔越短。</summary>
        public static void ScheduleRevenge(NemesisRecord record)
        {
            if (record == null || record.NextRevengeDay > 0) return; // 已有计划

            // 等级越高，间隔越短
            float minDelay = record.Level switch
            {
                NemesisLevel.Nemesis => 5f,
                NemesisLevel.ArchNemesis => 3f,
                NemesisLevel.Legendary => 1f,
                _ => 7f,
            };
            float maxDelay = record.Level switch
            {
                NemesisLevel.Nemesis => 12f,
                NemesisLevel.ArchNemesis => 7f,
                NemesisLevel.Legendary => 3f,
                _ => 15f,
            };

            float delay = minDelay + MBRandom.RandomFloat * (maxDelay - minDelay);
            record.NextRevengeDay = (float)CampaignTime.Now.ToDays + delay;

            DebugLogger.Log($"[Nemesis] {record.HeroName} will seek revenge in {delay:F1} days (level={record.Level}).");
        }

        /// <summary>
        /// DailyTick 中检查是否有到期的复仇 → 生成 NemesisRevenge 事件。
        /// WorldEventSimulator 调用此方法。
        /// </summary>
        public static NemesisRecord CheckAndTriggerRevenge()
        {
            foreach (var record in _records.Values)
            {
                if (!record.IsScheduledForRevenge) continue;
                if (!record.IsHeroAlive)
                {
                    record.NextRevengeDay = 0;
                    continue;
                }

                // 生成 NemesisRevenge 事件
                var config = WorldEventConfig.Get(EventType.NemesisRevenge);
                if (config == null) return null;

                var hero = record.Hero;
                var settlement = hero.CurrentSettlement
                    ?? hero.HomeSettlement
                    ?? Settlement.All.FirstOrDefault();
                if (settlement == null) return null;

                // 创建 party
                MobileParty revengeParty = SpawnNemesisParty(record, hero, settlement);
                if (revengeParty == null)
                {
                    record.NextRevengeDay = 0;
                    return null;
                }

                // 创建 WorldEvent
                var worldEvent = new WorldEvent
                {
                    EventId = $"evt_nemesis_{hero.StringId}_{DateTime.UtcNow.Ticks:X8}",
                    Type = EventType.NemesisRevenge,
                    Stage = EventStage.Active,
                    TargetHeroId = Hero.MainHero?.StringId,
                    TargetSettlementId = settlement.StringId,
                    InitiatorId = hero.StringId,
                    IsGenericInstigator = false,
                    GeneratedPartyId = revengeParty.StringId,
                    OccurredDay = (float)CampaignTime.Now.ToDays,
                    DayLimit = 10f,
                    Severity = (5 + (int)record.Level) * 10,
                };

                WorldEventStore.AddEvent(worldEvent);

                // 推送通知
                string msg = record.HasScar
                    ? $"那道疤还在疼——{hero.Name}回来了，带着你留给他的印记。"
                    : $"你以为已经了结了？{hero.Name}不这么想。他在找你。";
                NinjaNotificationManager.Show(msg, () => { });

                record.NextRevengeDay = 0; // 清除计划
                record.TimesEncountered++;

                DebugLogger.Log($"[Nemesis] NemesisRevenge triggered: {hero.Name} (level={record.Level})");
                return record;
            }

            return null;
        }

        private static MobileParty SpawnNemesisParty(NemesisRecord record, Hero hero, Settlement nearSettlement)
        {
            try
            {
                if (hero.Clan == null)
                    hero.Clan = Clan.BanditFactions.FirstOrDefault() ?? Clan.PlayerClan;

                var component = new SafeLordPartyComponent(hero);
                string partyId = $"lwn_nemesis_{hero.StringId}_{MBRandom.RandomInt(10000)}";
                MobileParty party = V.MakeParty(partyId, component);
                if (party != null)
                {
                    string title = record.Level >= NemesisLevel.ArchNemesis ? "宿敌" : "复仇者";
                    V.SetPartyName(party, new TextObject($"{hero.Name}的{title}队"));
                }

                if (party == null) return null;

                party.ActualClan = hero.Clan;

                // 填充部队：宿敌级别越高兵越多
                PartyTemplateObject template = hero.Culture?.DefaultPartyTemplate;
                if (template != null)
                    V.InitPartyPos(party, template, V.Pos(party));
                party.MemberRoster.Clear();
                party.PrisonRoster.Clear();
                party.MemberRoster.AddToCounts(hero.CharacterObject, 1);

                int troopCount = 10 + (int)record.Level * 8;
                var basicTroop = hero.Culture?.BasicTroop;
                if (basicTroop != null)
                    party.MemberRoster.AddToCounts(basicTroop, troopCount);

                if ((int)record.Level >= 3)
                {
                    var eliteTroop = hero.Culture?.EliteBasicTroop ?? basicTroop;
                    if (eliteTroop != null)
                        party.MemberRoster.AddToCounts(eliteTroop, (int)record.Level * 3);
                }

                // 位置：玩家附近
                Vec2 offset = new Vec2(
                    (MBRandom.RandomFloat - 0.5f) * 30f,
                    (MBRandom.RandomFloat - 0.5f) * 30f);
                V.SetPos(party, V.Pos(MobileParty.MainParty) + offset);

                // AI：主动猎杀玩家
                V.SetMoveEngage(party, MobileParty.MainParty);
                party.Ai.SetDoNotMakeNewDecisions(true);
                party.SetPartyUsedByQuest(true);
                party.Party.SetVisualAsDirty();

                // 宿敌部队移动速度加快
                hero.SetSkillValue(DefaultSkills.Scouting, 300);

                DebugLogger.Log($"[Nemesis] Spawned nemesis party for {hero.Name}, troops={troopCount}");
                return party;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Nemesis] SpawnNemesisParty error: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Query

        public static NemesisRecord GetRecord(Hero hero)
        {
            if (hero == null || string.IsNullOrEmpty(hero.StringId)) return null;
            return _records.TryGetValue(hero.StringId, out var r) ? r : null;
        }

        public static NemesisRecord GetOrCreateRecord(Hero hero)
        {
            if (hero == null || string.IsNullOrEmpty(hero.StringId)) return null;

            if (!_records.TryGetValue(hero.StringId, out var record))
            {
                record = new NemesisRecord
                {
                    HeroId = hero.StringId,
                    HeroName = hero.Name?.ToString() ?? "无名",
                };
                _records[hero.StringId] = record;
            }
            else if (record.HeroName == "无名" && hero.Name != null)
            {
                record.HeroName = hero.Name.ToString();
            }

            return record;
        }

        /// <summary>获取所有存活的宿敌（Level ≥ Nemesis）。</summary>
        public static List<NemesisRecord> GetLivingNemeses()
        {
            return _records.Values
                .Where(r => r.Level >= NemesisLevel.Nemesis && r.IsHeroAlive)
                .OrderByDescending(r => (int)r.Level)
                .ToList();
        }

        /// <summary>获取 NPC 闲聊时关于玩家宿敌的议论（用于 Chat_Gossip）。</summary>
        public static string GetNemesisGossip()
        {
            var nemeses = GetLivingNemeses();
            if (nemeses.Count == 0)
            {
                // 检查是否有击败过玩家的（不再是宿敌但曾经击败过）
                var defeatedPlayer = _records.Values
                    .Where(r => r.TimesDefeatedPlayer > 0 && r.IsHeroAlive)
                    .OrderByDescending(r => r.TimesDefeatedPlayer)
                    .ToList();

                if (defeatedPlayer.Count > 0)
                {
                    var r = defeatedPlayer[MBRandom.RandomInt(0, defeatedPlayer.Count)];
                    string playerName = Hero.MainHero?.Name?.ToString() ?? "你";
                    return r.TimesDefeatedPlayer >= 3
                        ? $"听说{ r.HeroName }曾经不止一次地击败过{ playerName }……真没想到。"
                        : $"听说{ r.HeroName }和{ playerName }交过手……结果不太好看。";
                }
                return null;
            }

            var nemesis = nemeses[MBRandom.RandomInt(0, nemeses.Count)];
            if (nemesis.TimesDefeatedPlayer > 0)
            {
                return nemesis.Level >= NemesisLevel.ArchNemesis
                    ? $"提起{ nemesis.HeroName }……这名字让很多人噤声。听说你和他之间的恩怨可不是一两天的事了。"
                    : $"上回{ nemesis.HeroName }打赢你的事，附近已经传开了。你打算什么时候找回场子？";
            }
            if (nemesis.HasScar)
            {
                return $"有人说看到{ nemesis.HeroName }脸上多了道新疤——他们说是你留下的。他在找你。";
            }
            return $"最近有人在打听你的下落……一个叫{ nemesis.HeroName }的家伙。小心点。";
        }

        #endregion

        #region JSON Persistence

        public static string Serialize()
        {
            try
            {
                var list = _records.Values.ToList();
                return JsonConvert.SerializeObject(list, Formatting.None);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Nemesis] Serialize error: {ex.Message}");
                return "[]";
            }
        }

        public static void Deserialize(string json)
        {
            _records.Clear();
            if (string.IsNullOrEmpty(json) || json == "[]") return;

            try
            {
                var list = JsonConvert.DeserializeObject<List<NemesisRecord>>(json);
                if (list == null) return;
                foreach (var r in list)
                {
                    if (r != null && !string.IsNullOrEmpty(r.HeroId))
                        _records[r.HeroId] = r;
                }
                DebugLogger.Log($"[Nemesis] Deserialized {_records.Count} records");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Nemesis] Deserialize error: {ex.Message}");
            }
        }

        #endregion
    }
}
