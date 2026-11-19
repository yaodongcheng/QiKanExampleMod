using System;
using System.Collections.Generic;

namespace LivingWorldNpcs.Planner.Cooperation
{
    /// <summary>一条个人事实：值 + 来源 + 观察时间 + 有效期 + 版本。</summary>
    public sealed class CoopFactRecord
    {
        public CoopTruth Truth;
        public string Source = string.Empty;
        /// <summary>观察发生时的游戏推进时间（秒）。暂停期间不增加。</summary>
        public double ObservedGameSeconds;
        /// <summary>失效的游戏时间；0 = 不过期。</summary>
        public double ExpiresGameSeconds;
        public int Version;
    }

    /// <summary>
    /// 每名角色自己的事实表。未知不能被当作假或真；权威查询不自动广播成知识（§2.1）。
    /// 只保存本人知道的事：听到、看到、收到的公开答复。
    /// </summary>
    public sealed class CoopFactStore
    {
        private readonly Dictionary<string, CoopFactRecord> _facts = new Dictionary<string, CoopFactRecord>(StringComparer.Ordinal);
        private int _version;

        public string OwnerId = string.Empty;

        public int Version { get { return _version; } }

        public int Count { get { return _facts.Count; } }

        public void Set(string fact, CoopTruth truth, string source, double simTime, double expiresGameSeconds = 0)
        {
            if (string.IsNullOrEmpty(fact))
            {
                return;
            }
            CoopFactRecord record;
            if (!_facts.TryGetValue(fact, out record))
            {
                record = new CoopFactRecord();
                _facts[fact] = record;
            }
            if (record.Truth == truth && expiresGameSeconds == record.ExpiresGameSeconds)
            {
                return;
            }
            record.Truth = truth;
            record.Source = source ?? string.Empty;
            record.ObservedGameSeconds = simTime;
            record.ExpiresGameSeconds = expiresGameSeconds;
            record.Version = ++_version;
        }

        public void SetTrue(string fact, string source, double simTime, double expiresGameSeconds = 0)
        {
            Set(fact, CoopTruth.True, source, simTime, expiresGameSeconds);
        }

        public void Forget(string fact, string source, double simTime)
        {
            Set(fact, CoopTruth.Unknown, source, simTime, 0);
        }

        public bool TryGet(string fact, out CoopFactRecord record)
        {
            return _facts.TryGetValue(fact, out record);
        }

        public CoopTruth Get(string fact)
        {
            CoopFactRecord record;
            return _facts.TryGetValue(fact, out record) ? record.Truth : CoopTruth.Unknown;
        }

        public bool IsTrue(string fact)
        {
            return Get(fact) == CoopTruth.True;
        }

        /// <summary>把过期事实降级为未知，返回是否有变化。</summary>
        public bool Expire(double simTime)
        {
            bool changed = false;
            foreach (var pair in _facts)
            {
                var record = pair.Value;
                if (record.ExpiresGameSeconds > 0 && record.Truth != CoopTruth.Unknown
                    && simTime >= record.ExpiresGameSeconds)
                {
                    record.Truth = CoopTruth.Unknown;
                    record.Version = ++_version;
                    changed = true;
                }
            }
            return changed;
        }

        public Dictionary<string, CoopTruth> Snapshot()
        {
            var snapshot = new Dictionary<string, CoopTruth>(_facts.Count, StringComparer.Ordinal);
            foreach (var pair in _facts)
            {
                snapshot[pair.Key] = pair.Value.Truth;
            }
            return snapshot;
        }

        public List<KeyValuePair<string, CoopFactRecord>> Entries()
        {
            var list = new List<KeyValuePair<string, CoopFactRecord>>(_facts);
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return list;
        }

        public void Clear(string source)
        {
            _facts.Clear();
            _version++;
        }
    }

    /// <summary>
    /// 可规划能力目录：把内容声明的能力与运行时可用性合成只读快照。
    /// 未建模能力如实列“不可规划”原因，不向搜索开放（§5.8.1 / T37）。
    /// </summary>
    public sealed class CoopCatalog
    {
        public sealed class Entry
        {
            public CoopActionDef Action;
            public CoopPlannable Plannable;
            public string Reason = string.Empty;
            public string Domain = string.Empty;
            public string CapabilityId = string.Empty;
            public string ContractVersion = "1";
        }

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        public string ContentVersion = string.Empty;
        public string ContentHash = string.Empty;
        public int BuiltAtSceneEpoch;

        public IEnumerable<Entry> Entries { get { return _entries.Values; } }

        public int Count { get { return _entries.Count; } }

        public int PlannableCount
        {
            get
            {
                int count = 0;
                foreach (var entry in _entries.Values)
                {
                    if (entry.Plannable == CoopPlannable.Plannable)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public Entry Find(string id)
        {
            Entry entry;
            return id != null && _entries.TryGetValue(id, out entry) ? entry : null;
        }

        /// <summary>
        /// 生成目录。availability 返回 null = 可用；返回原因字符串 = 不可用并记入目录。
        /// </summary>
        public static CoopCatalog Build(CoopContentData content, Func<CoopActionDef, string> availability = null, int sceneEpoch = 0)
        {
            var catalog = new CoopCatalog
            {
                ContentVersion = content != null ? content.ContentVersion : string.Empty,
                ContentHash = content != null ? content.ConfigHash : string.Empty,
                BuiltAtSceneEpoch = sceneEpoch
            };
            if (content == null)
            {
                return catalog;
            }
            foreach (var pair in content.Domain.Actions)
            {
                var action = pair.Value;
                var entry = new Entry
                {
                    Action = action,
                    Plannable = action.Plannable,
                    Domain = action.Domain,
                    CapabilityId = action.CapabilityId,
                    Reason = action.PlannableNote ?? string.Empty
                };
                if (entry.Plannable == CoopPlannable.Plannable && availability != null)
                {
                    var reason = availability(action);
                    if (!string.IsNullOrEmpty(reason))
                    {
                        entry.Plannable = CoopPlannable.Excluded;
                        entry.Reason = reason;
                    }
                }
                catalog._entries[action.Id] = entry;
            }
            return catalog;
        }

        public CoopDomain PlannableDomain(CoopDomain source)
        {
            var domain = new CoopDomain();
            foreach (var entry in _entries.Values)
                if (entry.Plannable == CoopPlannable.Plannable) domain.Actions[entry.Action.Id] = entry.Action;
            foreach (var pair in source.Methods) domain.Methods[pair.Key] = new List<CoopMethodDef>(pair.Value);
            return domain;
        }

        /// <summary>只读诊断文本（控制台英文，§1.2 cooperate catalog）。</summary>
        public string Describe(string filter = null)
        {
            var builder = new System.Text.StringBuilder();
            builder.Append("OK: catalog content=").Append(ContentVersion)
                .Append(" hash=").Append(string.IsNullOrEmpty(ContentHash) ? "-" : ContentHash.Substring(0, Math.Min(12, ContentHash.Length)))
                .Append(" entries=").Append(_entries.Count).Append(" plannable=").Append(PlannableCount);
            var list = new List<Entry>(_entries.Values);
            list.Sort((a, b) => string.CompareOrdinal(a.Action.Id, b.Action.Id));
            foreach (var entry in list)
            {
                if (!string.IsNullOrEmpty(filter)
                    && entry.Action.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                    && (entry.CapabilityId == null || entry.CapabilityId.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    continue;
                }
                builder.Append("\n  ").Append(entry.Action.Id)
                    .Append(" [").Append(entry.Plannable).Append(']')
                    .Append(" domain=").Append(entry.Domain)
                    .Append(" cap=").Append(entry.CapabilityId)
                    .Append(" cost=").Append(entry.Action.Cost);
                if (entry.Action.Preconditions.Count > 0)
                {
                    builder.Append(" pre=").Append(entry.Action.Preconditions.Key);
                }
                if (!string.IsNullOrEmpty(entry.Reason))
                {
                    builder.Append(" reason=").Append(entry.Reason);
                }
            }
            return builder.ToString();
        }
    }
}
