using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 幕后黑手（Conspiracy）机制 — 将孤立事件串联成暗线故事。
    ///
    /// 不是独立事件类型，而是附着在 WorldEvent 上的可选隐藏层。
    ///
    /// 运作方式：
    ///   1. ~5% 概率事件有幕后黑手
    ///   2. 幕后黑手是一个真实 Hero（敌对领主/有野心的 clan leader）
    ///   3. 同一 ConspiracyId 的 2-4 个事件共享一条暗线
    ///   4. 玩家通过审问俘虏/Scout 检定/酒馆闲聊发现线索
    ///   5. 集齐足够线索 → 解锁传奇级 BountyHunt 面对幕后黑手
    /// </summary>
    public static class ConspiracyManager
    {
        private const float MASTERMIND_PROBABILITY = 0.05f;
        private const int CLUES_TO_UNLOCK = 3;
        private const int MAX_EVENTS_PER_CONSPIRACY = 4;

        /// <summary>活跃的阴谋（ConspiracyId → 线索数）。</summary>
        private static Dictionary<string, ConspiracyState> _activeConspiracies
            = new Dictionary<string, ConspiracyState>();

        /// <summary>每个阴谋的状态。</summary>
        public class ConspiracyState
        {
            public string ConspiracyId;
            public string MastermindHeroId;
            public string MastermindName;
            public List<string> LinkedEventIds = new List<string>();
            public int CluesDiscovered;
            public bool IsRevealed; // 玩家已发现幕后黑手身份
            public bool IsConfronted; // 玩家已面对幕后黑手
        }

        /// <summary>
        /// 事件创建时调用。低概率为事件分配幕后黑手。
        /// 优先复用现有的活跃阴谋（同类型事件加入同一暗线），否则新建。
        /// </summary>
        public static void TryAssignMastermind(WorldEvent worldEvent)
        {
            if (worldEvent == null) return;
            if (worldEvent.Type == EventType.NemesisRevenge) return; // 宿敌复仇不进阴谋
            if (MBRandom.RandomFloat > MASTERMIND_PROBABILITY) return;

            // 尝试加入现有活跃阴谋（优先匹配事件类型相似的）
            var existingConspiracy = _activeConspiracies.Values
                .Where(c => !c.IsConfronted && c.LinkedEventIds.Count < MAX_EVENTS_PER_CONSPIRACY)
                .OrderBy(c => MBRandom.RandomFloat)
                .FirstOrDefault();

            if (existingConspiracy != null)
            {
                worldEvent.HiddenMastermindId = existingConspiracy.MastermindHeroId;
                worldEvent.ConspiracyId = existingConspiracy.ConspiracyId;
                existingConspiracy.LinkedEventIds.Add(worldEvent.EventId);
                DebugLogger.Log($"[Conspiracy] Event {worldEvent.EventId} joined existing conspiracy {existingConspiracy.ConspiracyId} ({existingConspiracy.LinkedEventIds.Count}/{MAX_EVENTS_PER_CONSPIRACY} events)");
                return;
            }

            // 新建阴谋
            var mastermind = FindMastermindCandidate();
            if (mastermind == null) return;

            string conspiracyId = $"conspiracy_{mastermind.StringId}_{DateTime.UtcNow.Ticks:X8}";
            var state = new ConspiracyState
            {
                ConspiracyId = conspiracyId,
                MastermindHeroId = mastermind.StringId,
                // 幕后黑手名兜底：幕后之人
                MastermindName = mastermind.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_conspiracy_unknown_mastermind", "The one behind it all"),
                LinkedEventIds = { worldEvent.EventId },
                CluesDiscovered = 0,
            };

            _activeConspiracies[conspiracyId] = state;

            worldEvent.HiddenMastermindId = mastermind.StringId;
            worldEvent.ConspiracyId = conspiracyId;

            DebugLogger.Log($"[Conspiracy] New conspiracy {conspiracyId}: mastermind={mastermind.Name}, first event={worldEvent.EventId}");
        }

        /// <summary>找一个合适的幕后黑手（敌对领主）。</summary>
        private static Hero FindMastermindCandidate()
        {
            var candidates = new List<Hero>();
            foreach (var clan in Clan.All)
            {
                if (clan == null || clan == Clan.PlayerClan) continue;
                foreach (var hero in clan.Heroes)
                {
                    if (hero != null && hero.IsAlive && hero.IsLord
                        && hero != clan.Leader
                        && !_activeConspiracies.Values.Any(c => c.MastermindHeroId == hero.StringId))
                    {
                        candidates.Add(hero);
                    }
                }
            }
            if (candidates.Count == 0) return null;
            return candidates[MBRandom.RandomInt(0, candidates.Count)];
        }

        /// <summary>
        /// 玩家击败事件 party 头目后调用 → 审问俘虏可能发现线索。
        /// Social 检定：成功发现线索。
        /// </summary>
        public static bool TryDiscoverClue(string eventId, out string clueMessage)
        {
            clueMessage = null;

            var worldEvent = WorldEventStore.FindEvent(eventId);
            if (worldEvent == null || !worldEvent.HasHiddenMastermind) return false;

            if (!_activeConspiracies.TryGetValue(worldEvent.ConspiracyId, out var state))
                return false;

            // Social 检定：Charm 技能
            int playerCharm = Hero.MainHero.GetSkillValue(DefaultSkills.Charm);
            float chance = 0.3f + playerCharm * 0.003f; // 基础30% + Charm 加成
            if (MBRandom.RandomFloat > chance) return false;

            state.CluesDiscovered++;
            DebugLogger.Log($"[Conspiracy] Clue discovered for {state.ConspiracyId}: {state.CluesDiscovered}/{CLUES_TO_UNLOCK}");

            if (!state.IsRevealed && state.CluesDiscovered >= CLUES_TO_UNLOCK)
            {
                state.IsRevealed = true;
                // 线索揭示：俘虏吐出幕后黑手之名
                clueMessage = LWNTextHelper.ResolveCompound("LWN_conspiracy_clue_reveal", ("NAME", state.MastermindName));
                NinjaNotificationManager.Show(clueMessage, () => { });
                return true;
            }

            switch (state.CluesDiscovered)
            {
                case 1:
                    // 线索 1：俘虏闪烁其词
                    clueMessage = LWNTextHelper.ResolveText("LWN_conspiracy_clue_1", "The prisoner's eyes dart around — there is someone behind all this... but you cannot get more out of him.");
                    break;
                case 2:
                    // 线索 2：俘虏漏出半句话
                    clueMessage = LWNTextHelper.ResolveText("LWN_conspiracy_clue_2", "In fear the prisoner lets slip half a sentence: 'We all take orders from...' then clams up. You hear that name again, and begin to feel these events are connected.");
                    break;
                default:
                    // 线索 3+：零碎信息
                    clueMessage = LWNTextHelper.ResolveText("LWN_conspiracy_clue_3", "The prisoner gives up scattered scraps of information. You feel someone is pulling the strings behind all this.");
                    break;
            }

            return true;
        }

        /// <summary>
        /// 检查是否应该解锁传奇级 BountyHunt 委托 → 面对幕后黑手。
        /// 当所有关联事件被解决且幕后黑手身份已揭示时触发。
        /// </summary>
        public static bool CheckUnlockConfrontation(string conspiracyId, out Hero mastermind, out string narrativeHint)
        {
            mastermind = null;
            narrativeHint = null;

            if (!_activeConspiracies.TryGetValue(conspiracyId, out var state))
                return false;
            if (!state.IsRevealed || state.IsConfronted) return false;

            // 检查所有关联事件是否已解决
            bool allResolved = state.LinkedEventIds.All(id =>
            {
                var evt = WorldEventStore.FindEvent(id);
                return evt == null || evt.Stage == EventStage.Resolved;
            });

            if (!allResolved) return false;

            mastermind = Hero.FindFirst(h => h.StringId == state.MastermindHeroId);
            if (mastermind == null || !mastermind.IsAlive) return false;

            state.IsConfronted = true;
            // 对决解锁：所有线索指向幕后黑手
            narrativeHint = LWNTextHelper.ResolveCompound("LWN_conspiracy_confront_unlock", ("NAME", state.MastermindName));

            DebugLogger.Log($"[Conspiracy] Confrontation unlocked: {state.ConspiracyId} → {state.MastermindName}");
            return true;
        }

        /// <summary>获取某事件的幕后黑手信息（供 CommissionQuest 叙事使用）。</summary>
        public static string GetMastermindNarrative(string eventId)
        {
            var worldEvent = WorldEventStore.FindEvent(eventId);
            if (worldEvent == null || !worldEvent.HasHiddenMastermind) return null;
            if (!_activeConspiracies.TryGetValue(worldEvent.ConspiracyId, out var state)) return null;

            int remaining = CLUES_TO_UNLOCK - state.CluesDiscovered;
            return state.IsRevealed
                // 叙事：已揭示幕后黑手
                ? LWNTextHelper.ResolveCompound("LWN_conspiracy_narrative_revealed", ("NAME", state.MastermindName))
                // 叙事：还差几条线索
                : LWNTextHelper.ResolveCompound("LWN_conspiracy_narrative_progress", ("REMAINING", remaining.ToString()));
        }

        /// <summary>🔴 2026-08-23（跨档残留修复）：新档创建时清空（同进程主菜单直接开新档会残留旧档阴谋状态）。</summary>
        public static void ResetAll()
        {
            _activeConspiracies.Clear();
        }

        #region Persistence

        public static string Serialize()
        {
            try
            {
                var list = _activeConspiracies.Values
                    .Select(s => new
                    {
                        s.ConspiracyId,
                        s.MastermindHeroId,
                        s.MastermindName,
                        s.LinkedEventIds,
                        s.CluesDiscovered,
                        s.IsRevealed,
                        s.IsConfronted,
                    }).ToList();
                return Newtonsoft.Json.JsonConvert.SerializeObject(list, Newtonsoft.Json.Formatting.None);
            }
            catch { return "[]"; }
        }

        public static void Deserialize(string json)
        {
            _activeConspiracies.Clear();
            if (string.IsNullOrEmpty(json) || json == "[]") return;
            try
            {
                var list = Newtonsoft.Json.JsonConvert.DeserializeObject<List<ConspiracyState>>(json);
                if (list != null)
                    foreach (var s in list)
                        if (s != null && !string.IsNullOrEmpty(s.ConspiracyId))
                            _activeConspiracies[s.ConspiracyId] = s;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Conspiracy] Deserialize error: {ex.Message}");
            }
        }

        #endregion
    }
}
