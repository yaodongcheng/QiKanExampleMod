using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 2026-08-21（M4 风险排序补全，用户裁定）：随从挑目标候选时的出手风险评估——
    /// 候选不再只按距离排序（无脑距离询问然后送死，实机 2026-08-20 日志：23 候选纯距离排列、
    /// LLM 谎称"落单的弩手"选中后被 3 人盯着绕不到背后）。
    /// 四项整数计分：①目标 3m 内潜在目击者（友方豁免、守卫恒计入）②正盯着目标的人数（视线矩阵）
    /// ③身后站位可行性（仅偷窃语境——绕不到背后 = 任务失败，非送死）④目标战力对比
    /// （🔴 2026-08-21 用户二次裁定：战力 = 被发现后的后果系数——偷窃语境下目标独处（①②=0）
    /// 时战力不计入（偷不成安全撤离不会打起来）；非偷窃语境（击晕/战斗）战力直接计入）。
    /// → 等级 低/中/高 → rankScore = 距离 + 风险分×K 综合排序（K = 10 米/风险点）。
    /// 纯静态、无状态、不持有快照引用（CanSee 懒缓存随 snapshot 生命周期走）；所有调用点主线程。
    /// 与【目之所及】同源快照（SceneSnapshot）——风险评估 = 随从亲见视角，无情报越界。
    /// </summary>
    public static class TargetRiskEvaluator
    {
        public enum RiskTier { Low, Mid, High }

        public struct TargetAssessment
        {
            public SceneSnapshot.AgentInfo Info;   // 候选 AgentInfo（DisplayName/PositionDesc 复用，调用方无需二次查）
            public Agent Target;                   // Info.Agent 快捷引用
            public float DistanceMeters;           // 距离基准由调用方给定（回复轮 = 执行者距离；澄清卡 = 玩家距离）
            public int NearbyCount;                // ① 3m 内潜在目击者（豁免后）
            public int WatcherCount;               // ② 正盯着目标的人（豁免后）
            public bool BehindSpotOk;              // ③ 身后站位可行（非偷窃语境恒 true）
            public int TargetStatTotal;            // ④ 目标战力
            public float CombatRatio;              // selfStatTotal / targetStatTotal（target 保底 1 防除零）
            public int RiskScore;                  // 四项合计
            public RiskTier Tier;
            public float RankScore;                // DistanceMeters + RiskScore × RankMetersPerRiskPoint
        }

        // ── 常量（具名可调）──
        /// <summary>① 目击者半径（与 WorldFactProvider 单目标段现口径一致）。</summary>
        public const float NearbyRadiusM = 3f;
        /// <summary>② 视线预筛半径 = NpcSightSystem.CanAgentSeeTarget 内部半径（语义等价省 RayCast）。</summary>
        public const float WatchPrefilterRadiusM = 15f;
        /// <summary>② 视线预筛高度差 = CanAgentSeeTarget 内部高度差上限（同上）。</summary>
        public const float WatchPrefilterHeightM = 3f;
        /// <summary>K：每 1 风险点等价 10 米距离（rankScore = 距离 + 风险×K）。</summary>
        public const float RankMetersPerRiskPoint = 10f;
        public const int TierMidMin = 2;
        public const int TierHighMin = 4;
        /// <summary>④ 战力比阈值：ratio &gt; Strong 无风险；&lt; Weak 悬殊（高分惩罚）。</summary>
        public const float CombatStrongRatio = 1.2f;
        public const float CombatWeakRatio = 0.8f;

        /// <summary>偷窃语境触发词（候选段只在 RiskCommandKeywords 命中后出现——本就在命令语境，误伤面小）。</summary>
        private static readonly string[] StealKeywords = { "偷", "扒", "摸", "掏", "steal", "pickpocket", "rob" };

        /// <summary>偷窃语境判定：命令命中偷窃词 → 身后站位分量计入 + 战力分量被"有目击者才计"门控。</summary>
        public static bool IsStealContext(string commandText)
        {
            if (string.IsNullOrEmpty(commandText)) return false;
            foreach (var kw in StealKeywords)
                if (commandText.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>单目标评估（返回 tier/risk/rank；目标为 null/self/玩家 → 安全降级 Low/0 分）。</summary>
        public static TargetAssessment Assess(SceneSnapshot snap, Agent self,
            SceneSnapshot.AgentInfo info, float distanceMeters, bool behindSpotMatters)
        {
            var a = new TargetAssessment { Info = info, Target = info?.Agent, DistanceMeters = distanceMeters };
            var target = a.Target;
            if (snap == null || target == null || !AgentControlHelper.SafeIsActive(target)
                || target == self || target == Agent.Main)
            {
                a.BehindSpotOk = true;
                a.CombatRatio = 1f;
                a.Tier = RiskTier.Low;
                return a;
            }
            int selfTotal = AgentStatsHelper.GetAgentStatTotal(self);

            // ① 3m 内潜在目击者：守卫恒计入（先于友方豁免）；友方豁免；排除目标/执行者/玩家
            foreach (var oi in snap.Agents)
            {
                var o = oi?.Agent;
                if (o == null || o == target || o == self || o == Agent.Main) continue;
                if (o.Position.Distance(target.Position) > NearbyRadiusM) continue;
                if (oi.Role != "guard" && FriendlinessHelper.IsFriendlyToPlayer(o)) continue;
                a.NearbyCount++;
            }

            // ② 正盯着目标的人：同豁免规则 + 视线矩阵；预筛 = CanSee 内部口径（3D 距离 ≤15m 且高度差 ≤3m）
            //（预筛语义等价——CanAgentSeeTarget 内部即这两个条件先行判断，省 RayCast）
            foreach (var oi in snap.Agents)
            {
                var o = oi?.Agent;
                if (o == null || o == target || o == self || o == Agent.Main) continue;
                if (MathF.Abs(o.Position.z - target.Position.z) > WatchPrefilterHeightM) continue;
                if (o.Position.Distance(target.Position) > WatchPrefilterRadiusM) continue;
                if (oi.Role != "guard" && FriendlinessHelper.IsFriendlyToPlayer(o)) continue;
                if (snap.CanSee(o, target)) a.WatcherCount++;
            }

            // ③ 身后站位（仅偷窃语境：绕不到背后 = impossible 任务失败；击晕/战斗无背后要求）
            a.BehindSpotOk = !behindSpotMatters || AgentControlHelper.TryFindBehindSpot(target, out _);

            // ④ 战力对比（用户 2026-08-21 裁定语义）
            a.TargetStatTotal = AgentStatsHelper.GetAgentStatTotal(target);
            a.CombatRatio = (float)selfTotal / Math.Max(1, a.TargetStatTotal);
            bool combatCounts = !behindSpotMatters            // 击晕/战斗：直接计入
                || a.NearbyCount > 0 || a.WatcherCount > 0;    // 偷窃：有目击者才可能打起来（独处不计）

            int pNearby = a.NearbyCount <= 0 ? 0 : a.NearbyCount == 1 ? 1 : a.NearbyCount <= 3 ? 2 : 3;
            int pWatcher = a.WatcherCount <= 0 ? 0 : a.WatcherCount == 1 ? 1 : a.WatcherCount <= 3 ? 2 : 3;
            int pBehind = a.BehindSpotOk ? 0 : 2;
            int pCombat = 0;
            if (combatCounts)
                pCombat = a.CombatRatio > CombatStrongRatio ? 0 : a.CombatRatio >= CombatWeakRatio ? 1 : 3;

            a.RiskScore = pNearby + pWatcher + pBehind + pCombat;
            a.Tier = a.RiskScore >= TierHighMin ? RiskTier.High
                : a.RiskScore >= TierMidMin ? RiskTier.Mid : RiskTier.Low;
            a.RankScore = distanceMeters + a.RiskScore * RankMetersPerRiskPoint;

            DebugLogger.Log($"[TargetRisk] 候选={AgentControlHelper.GetDisplayName(target)} " +
                $"nearby={a.NearbyCount} watcher={a.WatcherCount} behind={(a.BehindSpotOk ? "OK" : "NO")} " +
                $"ratio={a.CombatRatio:F2} risk={a.RiskScore} tier={TierWord(a.Tier)} " +
                $"rank={a.RankScore:F1} dist={distanceMeters:F1}");
            return a;
        }

        /// <summary>批量评估（self 战力只算一次；返回与传入候选同序的评估列表）。</summary>
        public static List<TargetAssessment> AssessAll(SceneSnapshot snap, Agent self,
            List<SceneSnapshot.AgentInfo> candidates, Func<SceneSnapshot.AgentInfo, float> distanceOf,
            bool behindSpotMatters)
        {
            var list = new List<TargetAssessment>();
            if (candidates == null) return list;
            foreach (var ci in candidates)
            {
                if (ci?.Agent == null) continue;
                float d = 0f;
                try { d = distanceOf?.Invoke(ci) ?? 0f; } catch { }
                list.Add(Assess(snap, self, ci, d, behindSpotMatters));
            }
            return list;
        }

        /// <summary>综合排序：rankScore 升序（同分按距离，稳定排序不抖动）。</summary>
        public static void SortByRank(List<TargetAssessment> list)
        {
            list.Sort((x, y) =>
            {
                int c = x.RankScore.CompareTo(y.RankScore);
                return c != 0 ? c : x.DistanceMeters.CompareTo(y.DistanceMeters);
            });
        }

        /// <summary>等级词（本地化，双桶 XML）。</summary>
        public static string TierWord(RiskTier tier)
        {
            switch (tier)
            {
                // 本地化：LWN_risk_tier_low（风险等级词，双桶）
                case RiskTier.Low: return LWNTextHelper.ResolvePrompt("LWN_risk_tier_low");
                // 本地化：LWN_risk_tier_mid（风险等级词，双桶）
                case RiskTier.Mid: return LWNTextHelper.ResolvePrompt("LWN_risk_tier_mid");
                // 本地化：LWN_risk_tier_high（风险等级词，双桶）
                default: return LWNTextHelper.ResolvePrompt("LWN_risk_tier_high");
            }
        }

        /// <summary>等级括注后缀（按钮/候选行用）：（风险低/中/高）。</summary>
        public static string TierSuffix(RiskTier tier)
        {
            // 本地化：LWN_risk_tier_suffix（等级括注后缀，双桶）
            return LWNTextHelper.ResolveCompound("LWN_risk_tier_suffix", ("TIER", TierWord(tier)));
        }

        /// <summary>
        /// 等级 + 紧凑明细后缀（回复轮 prompt 候选行用，玩家询问候选情况时随从逐人讲解的依据）：
        /// （风险低：身边2人、被1人盯、身后无位、战力悬殊）——只列非零分量（目击者/视线/站位/战力
        /// 与计分同口径：战力悬殊仅在有目击者时显示，独处高战力 = 低风险不矛盾）；无明细回落纯等级词。
        /// 注意：玩家按钮链（CollectTargetCandidates）只用 TierSuffix——按钮仍只标等级，本方法不进按钮。
        /// </summary>
        public static string DetailSuffix(TargetAssessment a, bool stealContext)
        {
            if (a.Target == null) return TierSuffix(a.Tier);
            bool combatCounts = !stealContext || a.NearbyCount > 0 || a.WatcherCount > 0;
            var parts = new List<string>();
            if (a.NearbyCount > 0)
            {
                // 本地化：LWN_risk_detail_nearby（身边目击者明细，双桶）
                parts.Add(LWNTextHelper.ResolveCompound("LWN_risk_detail_nearby", ("COUNT", a.NearbyCount.ToString())));
            }
            if (a.WatcherCount > 0)
            {
                // 本地化：LWN_risk_detail_watchers（被盯明细，双桶）
                parts.Add(LWNTextHelper.ResolveCompound("LWN_risk_detail_watchers", ("COUNT", a.WatcherCount.ToString())));
            }
            if (stealContext && !a.BehindSpotOk)
            {
                // 本地化：LWN_risk_detail_nospot（身后无位明细，双桶）
                parts.Add(LWNTextHelper.ResolvePrompt("LWN_risk_detail_nospot"));
            }
            if (combatCounts && a.CombatRatio < CombatWeakRatio)
            {
                // 本地化：LWN_risk_detail_outmatched（战力悬殊明细，双桶）
                parts.Add(LWNTextHelper.ResolvePrompt("LWN_risk_detail_outmatched"));
            }
            if (parts.Count == 0) return TierSuffix(a.Tier);
            // 本地化：LWN_word_separator（明细分隔符，双桶）
            string details = string.Join(LWNTextHelper.ResolvePrompt("LWN_word_separator"), parts);
            // 本地化：LWN_risk_detail_line（等级+明细括注，双桶）
            return LWNTextHelper.ResolveCompound("LWN_risk_detail_line", ("TIER", TierWord(a.Tier)), ("DETAILS", details));
        }
    }
}
