using System;
using System.Collections.Generic;

namespace LivingWorldNpcs.Planner.Cooperation
{
    /// <summary>
    /// 三种模式的统一入口：纯 GOAP、纯 HTN、组合（GOAP 选手段 + HTN 展开执行细节）。
    /// 同一次活动固定模式与数据版本，切换不重新付款或重掷检定（§7）。
    /// </summary>
    public static class CoopPlanner
    {
        public const int HybridMaxAttempts = 3;

        public static CoopSearchResult Solve(
            IDictionary<string, CoopTruth> facts,
            CoopDomain domain,
            CoopGoalDef goal,
            CoopAlgorithm algorithm,
            CoopBudget budget,
            Func<bool> cancelled = null)
        {
            if (budget == null)
            {
                budget = CoopBudget.Default();
            }
            switch (algorithm)
            {
                case CoopAlgorithm.Goap:
                    return CoopGoap.Solve(facts, domain, goal, budget, cancelled);
                case CoopAlgorithm.Htn:
                    return CoopHtn.Solve(facts, domain, goal, budget, cancelled);
                case CoopAlgorithm.Hybrid:
                    return SolveHybrid(facts, domain, goal, budget, cancelled);
                default:
                    return CoopSearchResult.Failure(CoopSolveStatus.InvalidInput, "unknown algorithm", 0);
            }
        }

        /// <summary>
        /// 组合模式：GOAP 在已登记手段中选候选；HTN 只展开所选手段。
        /// 展开失败则排除该候选，在剩余预算内重新选（不静默删步骤继续成功）。
        /// </summary>
        public static CoopSearchResult SolveHybrid(
            IDictionary<string, CoopTruth> facts,
            CoopDomain domain,
            CoopGoalDef goal,
            CoopBudget budget,
            Func<bool> cancelled = null)
        {
            var excluded = new HashSet<string>(StringComparer.Ordinal);
            int totalExpanded = 0;
            double totalMs = 0;
            CoopSearchResult last = null;

            for (int attempt = 0; attempt < HybridMaxAttempts; attempt++)
            {
                CoopDomain working = excluded.Count == 0 ? domain : Without(domain, excluded);
                var selection = CoopGoap.Solve(facts, working, goal, Remaining(budget, totalExpanded), cancelled);
                totalExpanded += selection.Expanded;
                totalMs += selection.ElapsedMs;
                last = selection;
                if (!selection.Found)
                {
                    return Merge(selection, totalExpanded, totalMs);
                }

                var tasks = new List<string>();
                bool expandable = false;
                foreach (var id in selection.Steps)
                {
                    CoopActionDef action;
                    domain.Actions.TryGetValue(id, out action);
                    if (action != null && !string.IsNullOrEmpty(action.ExpandTask))
                    {
                        tasks.Add(action.ExpandTask);
                        expandable = true;
                    }
                    else
                    {
                        tasks.Add(id);
                    }
                }
                if (!expandable)
                {
                    return Merge(selection, totalExpanded, totalMs);
                }

                var expansion = CoopHtn.Solve(facts, domain, goal, Remaining(budget, totalExpanded), cancelled, tasks);
                totalExpanded += expansion.Expanded;
                totalMs += expansion.ElapsedMs;
                if (expansion.Found)
                {
                    expansion.ElapsedMs = totalMs;
                    expansion.Expanded = totalExpanded;
                    return expansion;
                }

                // 展开失败：排除本次参与展开的手段后重新选，计数与预算继续累积。
                foreach (var id in selection.Steps)
                {
                    CoopActionDef action;
                    if (domain.Actions.TryGetValue(id, out action) && !string.IsNullOrEmpty(action.ExpandTask))
                    {
                        excluded.Add(id);
                    }
                }
                last = expansion;
            }

            if (last == null)
            {
                return CoopSearchResult.Failure(CoopSolveStatus.NoPlan, "hybrid exhausted", totalExpanded);
            }
            last.Expanded = totalExpanded;
            last.ElapsedMs = totalMs;
            if (last.Status == CoopSolveStatus.Found)
            {
                last.Status = CoopSolveStatus.NoPlan;
                last.Reason = "hybrid expansion exhausted";
            }
            return last;
        }

        /// <summary>把搜索结果固化成带版本的执行计划。</summary>
        public static CoopPlan ToPlan(CoopSearchResult result, CoopAlgorithm algorithm, CoopGoalDef goal, int revision, string configVersion)
        {
            if (result == null || !result.Found)
            {
                return null;
            }
            return new CoopPlan
            {
                Algorithm = algorithm,
                GoalId = goal != null ? goal.Id : string.Empty,
                Revision = revision,
                Steps = new List<string>(result.Steps),
                Cost = result.Cost,
                ConfigVersion = configVersion ?? string.Empty
            };
        }

        private static CoopBudget Remaining(CoopBudget budget, int used)
        {
            int left = budget.MaxNodes - used;
            return new CoopBudget
            {
                MaxNodes = left > 0 ? left : 1,
                MaxDepth = budget.MaxDepth,
                MaxSteps = budget.MaxSteps
            };
        }

        private static CoopSearchResult Merge(CoopSearchResult result, int expanded, double ms)
        {
            result.Expanded = expanded;
            result.ElapsedMs = ms;
            return result;
        }

        private static CoopDomain Without(CoopDomain domain, HashSet<string> excluded)
        {
            var copy = new CoopDomain();
            foreach (var pair in domain.Actions)
            {
                if (!excluded.Contains(pair.Key))
                {
                    copy.Actions[pair.Key] = pair.Value;
                }
            }
            foreach (var pair in domain.Methods)
            {
                copy.Methods[pair.Key] = new List<CoopMethodDef>(pair.Value);
            }
            return copy;
        }

        /// <summary>
        /// 目标选择：安全与状态禁令先否决，其次职责约束，再按优先级、代价与稳定平局顺序。
        /// 只读本人事实；不读另一人的秘密计划。
        /// </summary>
        public static CoopGoalDef SelectGoal(
            IList<CoopGoalDef> goals,
            IDictionary<string, CoopTruth> facts,
            CoopDomain domain,
            CoopBudget budget,
            CoopAlgorithm algorithm,
            out CoopSearchResult bestSolution)
        {
            bestSolution = null;
            CoopGoalDef best = null;
            int bestPriority = int.MinValue;
            int bestCost = int.MaxValue;
            if (goals == null || domain == null)
            {
                return null;
            }
            if (budget == null)
            {
                budget = CoopBudget.Default();
            }
            foreach (var goal in goals)
            {
                if (goal == null || !goal.Constraints.IsSatisfiedBy(facts))
                {
                    continue;
                }
                var solution = Solve(facts, domain, goal, algorithm, budget);
                if (!solution.Found)
                {
                    continue;
                }
                bool better = goal.Priority > bestPriority
                    || (goal.Priority == bestPriority && solution.Cost < bestCost)
                    || (goal.Priority == bestPriority && solution.Cost == bestCost
                        && best != null && string.CompareOrdinal(goal.Id, best.Id) < 0);
                if (better)
                {
                    best = goal;
                    bestPriority = goal.Priority;
                    bestCost = solution.Cost;
                    bestSolution = solution;
                }
            }
            return best;
        }
    }
}