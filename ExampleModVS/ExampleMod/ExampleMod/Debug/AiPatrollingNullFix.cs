using HarmonyLib;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Fix vanilla NullReferenceException in AiPatrollingBehavior.AiHourlyTick.
    ///
    /// Crash condition:
    ///   mobileParty.MapFaction.Leader.IsLord
    ///   — if MapFaction resolves to a Clan whose leader was killed (by world events,
    ///     assassination expiry, or any other cause), Leader is null and the .IsLord
    ///     call throws NRE.
    ///
    /// Fix: when MapFaction or MapFaction.Leader is null, skip the original method
    ///       (same outcome as the early-return path — no patrol scoring for this party).
    /// </summary>
    [HarmonyPatch(typeof(AiPatrollingBehavior), "AiHourlyTick")]
    public static class AiPatrollingNullFix
    {
        [HarmonyPrefix]
        public static bool Prefix(MobileParty mobileParty)
        {
            try
            {
                if (mobileParty?.MapFaction == null || mobileParty.MapFaction.Leader == null)
                {
                    return false; // skip original → equivalent to early-return (no patrol)
                }
                return true; // safe — let original method run
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[AiPatrollingNullFix] Prefix error: {ex.Message}");
                return true; // on error, let original run (original crash is logged anyway)
            }
        }
    }

    /// <summary>
    /// Fix vanilla NullReferenceException in AiVisitSettlementBehavior.AiHourlyTick.
    ///
    /// Crash condition:
    ///   NRE reported at the main settlement-scoring foreach (decompiled line 126).
    ///   Vanilla IL there cannot produce NRE (the SortedList is freshly created and
    ///   never null) — the real null sits in an inlined getter on the
    ///   LeaderHero / MapFaction / Party.Owner chain, hit by parties in transient
    ///   states: leader just taken prisoner (TakePrisonerAction.RemovePartyLeader),
    ///   disbanding, clanless leader, etc. MobileParty.MapFaction and Hero.MapFaction
    ///   can both legitimately return null for such parties.
    ///
    /// Fix: Finalizer swallows the exception — skipping one hourly visit-scoring for
    ///   this party is harmless (same outcome as the early-return path), and the full
    ///   party state is logged to StoryEngine_RuntimeLog.txt so the real culprit can
    ///   be identified after one repro. Full diagnostics are logged once per party
    ///   (they tick hourly — a stuck party would otherwise spam the log).
    /// </summary>
    [HarmonyPatch(typeof(AiVisitSettlementBehavior), "AiHourlyTick")]
    public static class AiVisitSettlementNullFix
    {
        /// <summary>已记录过完整诊断的部队 StringId（同一只部队每小时都会 tick，只记首次）</summary>
        private static readonly HashSet<string> _loggedParties = new HashSet<string>();

        [HarmonyFinalizer]
        public static Exception Finalizer(MobileParty mobileParty, Exception __exception)
        {
            if (__exception == null)
            {
                return null; // 无异常，正常返回
            }
            try
            {
                string key = mobileParty?.StringId ?? "<null-party>";
                if (_loggedParties.Add(key))
                {
                    DebugLogger.Log($"[AiVisitSettlementNullFix] 拦截 {__exception.GetType().Name}，已吞掉（跳过该部队本小时 visit 评分）\n{DescribeParty(mobileParty)}\n{__exception}");
                }
                else
                {
                    DebugLogger.Log($"[AiVisitSettlementNullFix] 再次拦截同一部队: {key} ({__exception.GetType().Name}: {__exception.Message})");
                }
            }
            catch (Exception ex)
            {
                // 诊断本身绝不能再炸
                try { DebugLogger.Log($"[AiVisitSettlementNullFix] Finalizer error: {ex.Message}"); } catch { }
            }
            return null; // 吞掉异常
        }

        private static string DescribeParty(MobileParty party)
        {
            if (party == null) return "--- mobileParty 本身就是 null ---";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("--- 肇事部队状态 ---");
            sb.AppendLine($"Party: {Safe(() => party.Name?.ToString())} (ID: {Safe(() => party.StringId)})");
            sb.AppendLine($"LeaderHero: {Safe(() => party.LeaderHero?.Name?.ToString())}");
            sb.AppendLine($"Party.Owner: {Safe(() => party.Party?.Owner?.Name?.ToString())}");
            sb.AppendLine($"ActualClan: {Safe(() => party.ActualClan?.Name?.ToString())}");
            sb.AppendLine($"MapFaction: {Safe(() => party.MapFaction?.Name?.ToString())}");
            sb.AppendLine($"类型: Bandit={party.IsBandit} Lord={party.IsLordParty} Militia={party.IsMilitia} Caravan={party.IsCaravan} Villager={party.IsVillager} Garrison={party.IsGarrison} Disbanding={party.IsDisbanding} Active={party.IsActive}");
            sb.AppendLine($"CurrentSettlement: {Safe(() => party.CurrentSettlement?.Name?.ToString())}");
            sb.AppendLine($"LastVisitedSettlement: {Safe(() => party.LastVisitedSettlement?.Name?.ToString())}");
            sb.AppendLine($"TargetSettlement: {Safe(() => party.TargetSettlement?.Name?.ToString())}");
            sb.AppendLine($"Army: {Safe(() => party.Army?.Name?.ToString())}");
            sb.AppendLine($"Position: {party.Position2D}");
            return sb.ToString();
        }

        /// <summary>getter 本身可能抛异常（如 MapFaction 链上的连环解引用），逐个隔离</summary>
        private static string Safe(Func<string> getter)
        {
            try { return getter() ?? "null"; }
            catch (Exception ex) { return $"<访问抛 {ex.GetType().Name}>"; }
        }
    }
}
