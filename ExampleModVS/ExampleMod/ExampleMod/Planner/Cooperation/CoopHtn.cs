using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace LivingWorldNpcs.Planner.Cooperation
{
    /// <summary>
    /// HTN 正向分解：展开复合任务的所有合资格方法，正向模拟原子效果，比较完整候选成本。
    /// 已提交的招募、财物、检定与玩家过门是硬边界（由活动层保存），这里只算符号域。
    /// </summary>
    public static class CoopHtn
    {
        private sealed class Node
        {
            public int Cost;
            public long Serial;
            public Dictionary<string, CoopTruth> State;
            public List<string> Tasks;
            public List<string> Sequence;
            public int Depth;
        }

        private sealed class NodeComparer : IComparer<Node>
        {
            public int Compare(Node x, Node y)
            {
                int byCost = x.Cost.CompareTo(y.Cost);
                return byCost != 0 ? byCost : x.Serial.CompareTo(y.Serial);
            }
        }

        public static CoopSearchResult Solve(
            IDictionary<string, CoopTruth> facts,
            CoopDomain domain,
            CoopGoalDef goal,
            CoopBudget budget,
            Func<bool> cancelled = null,
            IEnumerable<string> startTasks = null)
        {
            var watch = Stopwatch.StartNew();
            bool hasRoot = startTasks != null;
            if (facts == null || domain == null || goal == null || (!hasRoot && string.IsNullOrEmpty(goal.RootTask)))
            {
                watch.Stop();
                return CoopSearchResult.Failure(CoopSolveStatus.InvalidInput, "missing facts, domain or root task", 0);
            }
            if (!goal.Constraints.IsSatisfiedBy(facts))
            {
                watch.Stop();
                return CoopSearchResult.Failure(CoopSolveStatus.NoPlan, "goal constraints not satisfied", 0);
            }

            int expanded = 0;
            long serial = 0;
            var initialTasks = new List<string>();
            if (hasRoot)
            {
                initialTasks.AddRange(startTasks);
            }
            else
            {
                initialTasks.Add(goal.RootTask);
            }
            var start = new Node
            {
                Cost = 0,
                Serial = serial++,
                State = new Dictionary<string, CoopTruth>(facts, StringComparer.Ordinal),
                Tasks = initialTasks,
                Sequence = new List<string>(),
                Depth = 0
            };
            var heap = new List<Node> { start };
            var comparer = new NodeComparer();
            var best = new Dictionary<string, int>(StringComparer.Ordinal) { { Identity(start), 0 } };

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

                var node = Pop(heap, comparer);
                int known;
                if (!best.TryGetValue(Identity(node), out known) || known != node.Cost)
                {
                    continue;
                }
                expanded++;

                if (node.Tasks.Count == 0)
                {
                    if (goal.Conditions.IsSatisfiedBy(node.State))
                    {
                        watch.Stop();
                        return new CoopSearchResult
                        {
                            Status = CoopSolveStatus.Found,
                            Steps = node.Sequence,
                            Cost = node.Cost,
                            Expanded = expanded,
                            ElapsedMs = watch.Elapsed.TotalMilliseconds,
                            Reason = "found"
                        };
                    }
                    continue;
                }

                string head = node.Tasks[0];
                List<Node> successors = new List<Node>();

                if (head.Length > 0 && head[0] == '@')
                {
                    var methods = domain.MethodsFor(head);
                    if (methods != null)
                    {
                        foreach (var method in methods)
                        {
                            if (!method.Guards.IsSatisfiedBy(node.State))
                            {
                                continue;
                            }
                            if (node.Depth + 1 > budget.MaxDepth)
                            {
                                continue;
                            }
                            var tasks = new List<string>();
                            if (method.Subtasks != null)
                            {
                                tasks.AddRange(method.Subtasks);
                            }
                            tasks.AddRange(node.Tasks.GetRange(1, node.Tasks.Count - 1));
                            successors.Add(new Node
                            {
                                Cost = node.Cost,
                                Serial = serial++,
                                State = node.State,
                                Tasks = tasks,
                                Sequence = node.Sequence,
                                Depth = node.Depth + 1
                            });
                        }
                    }
                }
                else
                {
                    CoopActionDef action;
                    if (domain.Actions.TryGetValue(head, out action)
                        && action.Plannable == CoopPlannable.Plannable
                        && action.RequiredAtStart.IsSatisfiedBy(node.State)
                        && node.Sequence.Count < budget.MaxSteps)
                    {
                        var state = new Dictionary<string, CoopTruth>(node.State, StringComparer.Ordinal);
                        foreach (var effect in action.Effects.Items)
                        {
                            state[effect.Fact] = effect.Value ? CoopTruth.True : CoopTruth.False;
                        }
                        var sequence = new List<string>(node.Sequence) { head };
                        successors.Add(new Node
                        {
                            Cost = node.Cost + action.Cost,
                            Serial = serial++,
                            State = state,
                            Tasks = node.Tasks.GetRange(1, node.Tasks.Count - 1),
                            Sequence = sequence,
                            Depth = node.Depth
                        });
                    }
                }

                foreach (var successor in successors)
                {
                    string identity = Identity(successor);
                    int existing;
                    if (best.TryGetValue(identity, out existing) && successor.Cost >= existing)
                    {
                        continue;
                    }
                    best[identity] = successor.Cost;
                    Push(heap, comparer, successor);
                }
            }

            watch.Stop();
            return CoopSearchResult.Failure(CoopSolveStatus.NoPlan, "exhausted", expanded);
        }

        /// <summary>只判定可达性而不产出计划（用于“无解 vs 预算耗尽”的区分测试）。</summary>
        private static string Identity(Node node)
        {
            var builder = new StringBuilder();
            var names = new List<string>(node.State.Keys);
            names.Sort(StringComparer.Ordinal);
            foreach (var name in names)
            {
                var truth = node.State[name];
                if (truth == CoopTruth.Unknown)
                {
                    continue;
                }
                builder.Append(name).Append(truth == CoopTruth.True ? '+' : '-').Append('|');
            }
            builder.Append("##");
            for (int i = 0; i < node.Tasks.Count; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append(node.Tasks[i]);
            }
            return builder.ToString();
        }

        private static void Push(List<Node> heap, IComparer<Node> comparer, Node node)
        {
            heap.Add(node);
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

        private static Node Pop(List<Node> heap, IComparer<Node> comparer)
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
