using System;
using System.Collections.Generic;
using System.Text;

namespace LivingWorldNpcs.Planner.Cooperation
{
    /// <summary>
    /// 三态事实：未知不能当作假，也不能当作真（实施计划 §2.3）。
    /// </summary>
    public enum CoopTruth
    {
        Unknown = 0,
        False = 1,
        True = 2
    }

    /// <summary>规划可用性：一项已注册能力是否已具备规划模型（§5.8.1）。</summary>
    public enum CoopPlannable
    {
        /// <summary>可进入 GOAP／HTN 搜索。</summary>
        Plannable = 0,
        /// <summary>可执行但尚无前提／结果模型，目录中列出但不进搜索。</summary>
        ExecutableOnly = 1,
        /// <summary>被玩法数据排除（资格、版本、内容关闭）。</summary>
        Excluded = 2
    }

    /// <summary>单个条件 = 某事实必须等于某值。</summary>
    public struct CoopCondition : IEquatable<CoopCondition>
    {
        public readonly string Fact;
        public readonly bool Value;

        public CoopCondition(string fact, bool value)
        {
            Fact = fact;
            Value = value;
        }

        public bool Equals(CoopCondition other)
        {
            return Value == other.Value && string.Equals(Fact, other.Fact, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is CoopCondition other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Fact == null ? 0 : StringComparer.Ordinal.GetHashCode(Fact)) * 2 + (Value ? 1 : 0);
        }

        public override string ToString()
        {
            return Fact + "=" + (Value ? "true" : "false");
        }
    }

    /// <summary>
    /// 条件的规范集合：去重、稳定排序、可做字典键。
    /// 规划中“满足”= 每个条件在真实事实里都有已知且相同的值；未知不满足任何一侧。
    /// </summary>
    public sealed class CoopConditionSet
    {
        public static readonly CoopConditionSet Empty = new CoopConditionSet(null);

        private readonly Dictionary<string, bool> _map;
        private readonly string _key;

        public CoopConditionSet(IEnumerable<CoopCondition> conditions)
        {
            _map = new Dictionary<string, bool>(StringComparer.Ordinal);
            if (conditions != null)
            {
                foreach (var condition in conditions)
                {
                    if (string.IsNullOrEmpty(condition.Fact))
                    {
                        continue;
                    }
                    if (_map.ContainsKey(condition.Fact))
                    {
                        if (_map[condition.Fact] != condition.Value)
                        {
                            throw new ArgumentException("contradictory condition on '" + condition.Fact + "'");
                        }
                        continue;
                    }
                    _map[condition.Fact] = condition.Value;
                }
            }
            _key = BuildKey(_map);
        }

        public int Count { get { return _map.Count; } }

        public IEnumerable<CoopCondition> Items
        {
            get
            {
                foreach (var pair in _map)
                {
                    yield return new CoopCondition(pair.Key, pair.Value);
                }
            }
        }

        public bool TryGet(string fact, out bool value)
        {
            return _map.TryGetValue(fact, out value);
        }

        public bool Contains(string fact)
        {
            return _map.ContainsKey(fact);
        }

        /// <summary>真实事实是否满足全部条件（未知一律不满足）。</summary>
        public bool IsSatisfiedBy(IDictionary<string, CoopTruth> facts)
        {
            if (_map.Count == 0)
            {
                return true;
            }
            foreach (var pair in _map)
            {
                CoopTruth truth;
                if (!facts.TryGetValue(pair.Key, out truth) || truth == CoopTruth.Unknown)
                {
                    return false;
                }
                if ((truth == CoopTruth.True) != pair.Value)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>两个条件集是否互相矛盾（同一事实要求不同值）。</summary>
        public bool ConflictsWith(CoopConditionSet other)
        {
            if (other == null || other._map.Count == 0 || _map.Count == 0)
            {
                return false;
            }
            foreach (var pair in other._map)
            {
                bool mine;
                if (_map.TryGetValue(pair.Key, out mine) && mine != pair.Value)
                {
                    return true;
                }
            }
            return false;
        }

        public CoopConditionSet Without(CoopConditionSet other)
        {
            if (other == null || other._map.Count == 0)
            {
                return this;
            }
            var list = new List<CoopCondition>();
            foreach (var pair in _map)
            {
                if (!other._map.ContainsKey(pair.Key))
                {
                    list.Add(new CoopCondition(pair.Key, pair.Value));
                }
            }
            return new CoopConditionSet(list);
        }

        public CoopConditionSet Union(CoopConditionSet other)
        {
            if (other == null || other._map.Count == 0)
            {
                return this;
            }
            var list = new List<CoopCondition>();
            foreach (var pair in _map)
            {
                list.Add(new CoopCondition(pair.Key, pair.Value));
            }
            foreach (var pair in other._map)
            {
                list.Add(new CoopCondition(pair.Key, pair.Value));
            }
            return new CoopConditionSet(list);
        }

        public string Key { get { return _key; } }

        private static string BuildKey(Dictionary<string, bool> map)
        {
            if (map.Count == 0)
            {
                return string.Empty;
            }
            var names = new List<string>(map.Keys);
            names.Sort(StringComparer.Ordinal);
            var builder = new StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append('|');
                }
                builder.Append(names[i]);
                builder.Append(map[names[i]] ? "+" : "-");
            }
            return builder.ToString();
        }

        public override string ToString()
        {
            return _key;
        }
    }

    /// <summary>
    /// 一项目标的定义。守卫的目标不依赖玩家秘密任务（§2.1）。
    /// </summary>
    public sealed class CoopGoalDef
    {
        public string Id;
        public string DisplayKey;
        /// <summary>目标达成条件。</summary>
        public CoopConditionSet Conditions = CoopConditionSet.Empty;
        /// <summary>职责／风险限制：这些条件不满足时该目标不可选。</summary>
        public CoopConditionSet Constraints = CoopConditionSet.Empty;
        /// <summary>HTN 中对应的复合任务名（可空 = 只能走 GOAP）。</summary>
        public string RootTask;
        /// <summary>数值越大越优先；平局按 Id 稳定排序。</summary>
        public int Priority;
    }

    /// <summary>
    /// 一项可规划能力的模型。真实效果由原机制提交，本模型只描述规划语义（§5.8.2）。
    /// </summary>
    public sealed class CoopActionDef
    {
        public string Id;
        /// <summary>所属注册域 + 能力 ID，用于目录审计。</summary>
        public string Domain;
        public string CapabilityId;
        public CoopConditionSet Preconditions = CoopConditionSet.Empty;
        /// <summary>必须持续成立的条件（不是开始条件）。</summary>
        public CoopConditionSet Persistent = CoopConditionSet.Empty;
        /// <summary>持续条件在起步时也必须成立，搜索与真实执行共用这一入口。</summary>
        public CoopConditionSet RequiredAtStart => Preconditions.Union(Persistent);
        public CoopConditionSet Effects = CoopConditionSet.Empty;
        public int Cost = 1;
        /// <summary>玩家可读阶段分组 key（§5.5）。</summary>
        public string StageKey;
        /// <summary>基础功能说明本地化 key。</summary>
        public string DisplayKey;
        public string DescriptionKey;
        /// <summary>false = 结果未知，执行到这里必须停下等真实回执。</summary>
        public bool OutcomeCertain = true;
        /// <summary>等待真实回执的游戏时间上限（秒）；0 = 不设上限。</summary>
        public double TimeoutGameSeconds;
        /// <summary>失败后是否允许在同一授权内换方法重试。</summary>
        public bool Retryable = true;
        /// <summary>实际执行入口（由适配器解析）；空 = 尚不可规划。</summary>
        public CoopPlannable Plannable = CoopPlannable.Plannable;
        public string PlannableNote;
        /// <summary>组合模式下该手段的 HTN 展开任务名；空 = 组合模式直接执行该原子。</summary>
        public string ExpandTask;
        /// <summary>实际执行参数（点名点位角色／目标角色／台词 key 等），由适配器解释。</summary>
        public Dictionary<string, string> Args = new Dictionary<string, string>(StringComparer.Ordinal);

        public CoopActionDef CloneWithOutcome(bool certain)
        {
            return new CoopActionDef
            {
                Id = Id,
                Domain = Domain,
                CapabilityId = CapabilityId,
                Preconditions = Preconditions,
                Persistent = Persistent,
                Effects = Effects,
                Cost = Cost,
                StageKey = StageKey,
                DisplayKey = DisplayKey,
                DescriptionKey = DescriptionKey,
                OutcomeCertain = certain,
                TimeoutGameSeconds = TimeoutGameSeconds,
                Retryable = Retryable,
                Plannable = Plannable,
                PlannableNote = PlannableNote,
                ExpandTask = ExpandTask,
                Args = new Dictionary<string, string>(Args, StringComparer.Ordinal)
            };
        }
    }

    /// <summary>HTN 方法定义：把复合任务分解成子任务列。</summary>
    public sealed class CoopMethodDef
    {
        public string Id;
        public string Task;
        public string[] Subtasks = new string[0];
        /// <summary>该方法在当前事实下是否可选（空 = 总是可选）。</summary>
        public CoopConditionSet Guards = CoopConditionSet.Empty;
    }

    /// <summary>一次活动的动作域快照（固定版本，运行中不换）。</summary>
    public sealed class CoopDomain
    {
        public readonly Dictionary<string, CoopActionDef> Actions = new Dictionary<string, CoopActionDef>(StringComparer.Ordinal);
        public readonly Dictionary<string, List<CoopMethodDef>> Methods = new Dictionary<string, List<CoopMethodDef>>(StringComparer.Ordinal);

        public void AddAction(CoopActionDef action)
        {
            if (action == null || string.IsNullOrEmpty(action.Id))
            {
                throw new ArgumentException("action id required");
            }
            if (Actions.ContainsKey(action.Id))
            {
                throw new ArgumentException("duplicate action id '" + action.Id + "'");
            }
            Actions[action.Id] = action;
        }

        public void AddMethod(CoopMethodDef method)
        {
            if (method == null || string.IsNullOrEmpty(method.Task) || string.IsNullOrEmpty(method.Id))
            {
                throw new ArgumentException("method task and id required");
            }
            List<CoopMethodDef> list;
            if (!Methods.TryGetValue(method.Task, out list))
            {
                list = new List<CoopMethodDef>();
                Methods[method.Task] = list;
            }
            list.Add(method);
        }

        public List<CoopMethodDef> MethodsFor(string task)
        {
            List<CoopMethodDef> list;
            return Methods.TryGetValue(task, out list) ? list : null;
        }
    }

    /// <summary>搜索预算（数据可配，有限正数，不得无界）。</summary>
    public sealed class CoopBudget
    {
        public int MaxNodes = 4096;
        public int MaxDepth = 16;
        public int MaxSteps = 64;

        public static CoopBudget Default()
        {
            return new CoopBudget();
        }
    }

    public enum CoopSolveStatus
    {
        Found = 0,
        /// <summary>有限模型已穷尽，仍无可执行计划。</summary>
        NoPlan = 1,
        /// <summary>预算耗尽：不等于已证明无解。</summary>
        BudgetExhausted = 2,
        InvalidInput = 3,
        Cancelled = 4
    }

    public sealed class CoopSearchResult
    {
        public CoopSolveStatus Status;
        public List<string> Steps = new List<string>();
        public int Cost;
        public int Expanded;
        public double ElapsedMs;
        public string Reason = string.Empty;

        public bool Found { get { return Status == CoopSolveStatus.Found; } }

        public static CoopSearchResult Failure(CoopSolveStatus status, string reason, int expanded)
        {
            return new CoopSearchResult { Status = status, Reason = reason ?? string.Empty, Expanded = expanded };
        }

        public override string ToString()
        {
            return Status + "(" + Reason + ") steps=" + Steps.Count + " cost=" + Cost + " nodes=" + Expanded;
        }
    }

    /// <summary>一种算法的选择结果（用于比较与默认模式决策）。</summary>
    public enum CoopAlgorithm
    {
        Goap = 0,
        Htn = 1,
        Hybrid = 2
    }

    /// <summary>
    /// 由搜索产出的可执行计划。执行只推进当前确定的一段，未知结果处停下（§2.3）。
    /// </summary>
    public sealed class CoopPlan
    {
        public CoopAlgorithm Algorithm;
        public string GoalId;
        public int Revision;
        public List<string> Steps = new List<string>();
        public int Cost;
        /// <summary>计划说明版本号（与内容 hash 绑定）。</summary>
        public string ConfigVersion = string.Empty;

        /// <summary>第一个结果未知的步骤下标；-1 = 全部确定。</summary>
        public int FirstUnknownIndex(CoopDomain domain)
        {
            for (int i = 0; i < Steps.Count; i++)
            {
                CoopActionDef action;
                if (domain.Actions.TryGetValue(Steps[i], out action) && !action.OutcomeCertain)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
