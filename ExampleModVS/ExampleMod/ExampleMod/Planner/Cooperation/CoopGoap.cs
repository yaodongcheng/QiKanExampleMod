using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace LivingWorldNpcs.Planner.Cooperation
{
    /// <summary>
    /// GOAP 反向回归：从想要的结果往回消去条件，加上前置条件，A* + h-max 下界。
    /// 语义与离线原型一致；下界与最低成本结论只适用于确定性动作域。
    /// </summary>
    public static class CoopGoap
    {
        private struct QueueEntry
        {
            public int Estimate;
            public int Cost;
            public long Serial;
            public CoopConditionSet Required;
            public string[] Suffix;
        }

        private sealed class QueueComparer : IComparer<QueueEntry>
        {
            public int Compare(QueueEntry x, QueueEntry y)
            {
                int byEstimate = x.Estimate.CompareTo(y.Estimate);
                if (byEstimate != 0) return byEstimate;
                int byCost = x.Cost.CompareTo(y.Cost);
                if (byCost != 0) return byCost;
                return x.Serial.CompareTo(y.Serial);
            }
        }

        public static CoopSearchResult Solve(
            IDictionary<string, CoopTruth> facts,
            CoopDomain domain,
            CoopGoalDef goal,
            CoopBudget budget,
            Func<bool> cancelled = null)
        {
            var watch = Stopwatch.StartNew();
            int expanded = 0;
            if (facts == null || domain == null || goal == null || goal.Conditions.Count == 0)
            {
                watch.Stop();
                return CoopSearchResult.Failure(CoopSolveStatus.InvalidInput, "missing facts, domain or goal", 0);
            }
            if (!Satisfies(goal.Constraints, facts))
            {
                watch.Stop();
                return CoopSearchResult.Failure(CoopSolveStatus.NoPlan, "goal constraints not satisfied", 0);
            }

            var actions = PlannableActions(domain);
            var relaxed = RelaxedCosts(facts, actions);
            var startRequired = goal.Conditions;

            int startEstimate = Heuristic(relaxed, startRequired);
            if (startEstimate == int.MaxValue)
            {
                watch.Stop();
                return CoopSearchResult.Failure(CoopSolveStatus.NoPlan, "goal unreachable in relaxed model", 0);
            }

            var heap = new List<QueueEntry>();
            var comparer = new QueueComparer();
            long serial = 0;
            Push(heap, comparer, new QueueEntry
            {
                Estimate = startEstimate,
                Cost = 0,
                Serial = serial++,
                Required = startRequired,
                Suffix = new string[0]
            });
            var best = new Dictionary<string, int>(StringComparer.Ordinal) { { startRequired.Key, 0 } };

            while (heap.Count > 0)
            {
                if (cancelled != null && cancelled())
                {
                    watch.Stop();
                    return CoopSearchResult.Failure(CoopSolveStatus.Cancelled, "cancelled", expanded);
                }
                if (expanded >= budget.MaxNodes)
                {
                    watch.Stop();
                    return CoopSearchResult.Failure(CoopSolveStatus.BudgetExhausted, "node budget", expanded);
                }

                var entry = Pop(heap, comparer);
                int known;
                if (!best.TryGetValue(entry.Required.Key, out known) || known != entry.Cost)
                {
                    continue;
                }
                expanded++;

                if (entry.Required.IsSatisfiedBy(facts))
                {
                    var result = Replay(facts, domain, goal, entry.Suffix);
                    watch.Stop();
                    if (result != null)
                    {
                        return new CoopSearchResult
                        {
                            Status = CoopSolveStatus.Found,
                            Steps = result,
                            Cost = entry.Cost,
                            Expanded = expanded,
                            ElapsedMs = watch.Elapsed.TotalMilliseconds,
                            Reason = "found"
                        };
                    }
                    // 正向重放不成立：该节点只是模型上的假满足，继续搜索。
                    continue;
                }

                foreach (var action in actions)
                {
                    if (!IsRelevant(action, entry.Required))
                    {
                        continue;
                    }

                    var predecessor = entry.Required.Without(action.Effects);
                    if (predecessor.ConflictsWith(action.RequiredAtStart))
                    {
                        continue;
                    }
                    predecessor = predecessor.Union(action.RequiredAtStart);

                    int estimate = Heuristic(relaxed, predecessor);
                    if (estimate == int.MaxValue)
                    {
                        continue;
                    }
                    int newCost = entry.Cost + action.Cost;
                    int existing;
                    if (best.TryGetValue(predecessor.Key, out existing) && newCost >= existing)
                    {
                        continue;
                    }
                    best[predecessor.Key] = newCost;
                    var suffix = new string[entry.Suffix.Length + 1];
                    suffix[0] = action.Id;
                    Array.Copy(entry.Suffix, 0, suffix, 1, entry.Suffix.Length);
                    Push(heap, comparer, new QueueEntry
                    {
                        Estimate = newCost + estimate,
                        Cost = newCost,
                        Serial = serial++,
                        Required = predecessor,
                        Suffix = suffix
                    });
                }
            }

            watch.Stop();
            return CoopSearchResult.Failure(CoopSolveStatus.NoPlan, "exhausted", expanded);
        }

        /// <summary>正向重放：确认产出的计划在当前事实下真的可执行。</summary>
        public static List<string> Replay(
            IDictionary<string, CoopTruth> facts,
            CoopDomain domain,
            CoopGoalDef goal,
            IEnumerable<string> steps)
        {
            var state = new Dictionary<string, CoopTruth>(facts, StringComparer.Ordinal);
            var list = new List<string>();
            foreach (var id in steps)
            {
                CoopActionDef action;
                if (!domain.Actions.TryGetValue(id, out action) || action.Plannable != CoopPlannable.Plannable)
                {
                    return null;
                }
                if (!action.RequiredAtStart.IsSatisfiedBy(state))
                {
                    return null;
                }
                foreach (var condition in action.Effects.Items)
                {
                    state[condition.Fact] = condition.Value ? CoopTruth.True : CoopTruth.False;
                }
                list.Add(id);
            }
            if (!goal.Conditions.IsSatisfiedBy(state))
            {
                return null;
            }
            return list;
        }

        private static bool Satisfies(CoopConditionSet set, IDictionary<string, CoopTruth> facts)
        {
            return set.IsSatisfiedBy(facts);
        }

        private static List<CoopActionDef> PlannableActions(CoopDomain domain)
        {
            var list = new List<CoopActionDef>();
            foreach (var pair in domain.Actions)
            {
                if (pair.Value.Plannable == CoopPlannable.Plannable)
                {
                    list.Add(pair.Value);
                }
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return list;
        }

        private static bool IsRelevant(CoopActionDef action, CoopConditionSet required)
        {
            bool supportsSomething = false;
            foreach (var effect in action.Effects.Items)
            {
                bool wanted;
                if (!required.TryGet(effect.Fact, out wanted))
                {
                    continue;
                }
                if (wanted != effect.Value)
                {
                    // 动作必然删掉仍需要的条件 => 拒绝。
                    return false;
                }
                supportsSomething = true;
            }
            return supportsSomething;
        }

        /// <summary>忽略删除效果的 h-max 下界；未知事实不进入初始成本表。</summary>
        private static Dictionary<CoopCondition, int> RelaxedCosts(
            IDictionary<string, CoopTruth> facts,
            List<CoopActionDef> actions)
        {
            var costs = new Dictionary<CoopCondition, int>();
            foreach (var pair in facts)
            {
                if (pair.Value != CoopTruth.Unknown)
                {
                    costs[new CoopCondition(pair.Key, pair.Value == CoopTruth.True)] = 0;
                }
            }

            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var action in actions)
                {
                    int max = 0;
                    bool feasible = true;
                    foreach (var pre in action.RequiredAtStart.Items)
                    {
                        int cost;
                        if (!costs.TryGetValue(pre, out cost))
                        {
                            feasible = false;
                            break;
                        }
                        if (cost > max) max = cost;
                    }
                    if (!feasible)
                    {
                        continue;
                    }
                    int total = max + action.Cost;
                    foreach (var effect in action.Effects.Items)
                    {
                        int existing;
                        if (!costs.TryGetValue(effect, out existing) || total < existing)
                        {
                            costs[effect] = total;
                            changed = true;
                        }
                    }
                }
            }
            return costs;
        }

        private static int Heuristic(Dictionary<CoopCondition, int> costs, CoopConditionSet required)
        {
            int max = 0;
            foreach (var condition in required.Items)
            {
                int cost;
                if (!costs.TryGetValue(condition, out cost))
                {
                    return int.MaxValue;
                }
                if (cost > max) max = cost;
            }
            return max;
        }

        private static void Push(List<QueueEntry> heap, IComparer<QueueEntry> comparer, QueueEntry entry)
        {
            heap.Add(entry);
            int index = heap.Count - 1;
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (comparer.Compare(heap[index], heap[parent]) >= 0)
                {
                    break;
                }
                var swap = heap[index];
                heap[index] = heap[parent];
                heap[parent] = swap;
                index = parent;
            }
        }

        private static QueueEntry Pop(List<QueueEntry> heap, IComparer<QueueEntry> comparer)
        {
            var root = heap[0];
            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            int index = 0;
            while (true)
            {
                int left = index * 2 + 1;
                if (left >= heap.Count) break;
                int right = left + 1;
                int smallest = (right < heap.Count && comparer.Compare(heap[right], heap[left]) < 0) ? right : left;
                if (comparer.Compare(heap[smallest], heap[index]) >= 0) break;
                var swap = heap[index];
                heap[index] = heap[smallest];
                heap[smallest] = swap;
                index = smallest;
            }
            return root;
        }
    }
}
