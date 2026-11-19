using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LivingWorldNpcs.Planner.Cooperation
{
    /// <summary>一次逻辑尝试的稳定身份：同一人物、目的、手段和实质筹码只掷一次。</summary>
    public struct CoopAttemptKey
    {
        public string ActorId;
        public string TargetId;
        public string Purpose;
        public string Method;
        public string Offer;
        /// <summary>剧情有效时期；换节点／换会话不改变它，新增证据才改变。</summary>
        public string Period;

        public string Key
        {
            get
            {
                var builder = new StringBuilder();
                builder.Append(ActorId).Append('|').Append(TargetId).Append('|')
                    .Append(Purpose).Append('|').Append(Method).Append('|')
                    .Append(Offer).Append('|').Append(Period);
                return builder.ToString();
            }
        }
    }

    /// <summary>一次已提交的检定结果（持久业务记录；读档不重掷）。</summary>
    public sealed class CoopAttemptRecord
    {
        public string AttemptId = string.Empty;
        public string ActorId = string.Empty;
        public string TargetId = string.Empty;
        public string Purpose = string.Empty;
        public string Method = string.Empty;
        public string Offer = string.Empty;
        public string Period = string.Empty;
        public float Chance;
        public float Threshold;
        public float Roll;
        public bool Success;
        public double SimTime;
        public string Source = string.Empty;
        /// <summary>本次尝试之后是否又登记了新的实质条件。</summary>
        public string Evidence = string.Empty;
    }

    public enum CoopRequestState
    {
        Received = 0,
        /// <summary>不合资格：零掷骰。</summary>
        Rejected = 1,
        Accepted = 2,
        Resolved = 3,
        Answered = 4,
        Expired = 5
    }

    /// <summary>一次双方交互：已收到 → 受理 → 一次裁定 → 公开答复。</summary>
    public sealed class CoopInteractionRequest
    {
        public string Id = string.Empty;
        public string FromActorId = string.Empty;
        public string ToActorId = string.Empty;
        public string Purpose = string.Empty;
        public string Method = string.Empty;
        public string Offer = string.Empty;
        public string Period = string.Empty;
        public CoopRequestState State = CoopRequestState.Received;
        public string AttemptId = string.Empty;
        public bool Accepted;
        public string RefusalReason = string.Empty;
        public double CreatedSim;
        public double ExpireSim;
    }

    /// <summary>
    /// 交互裁定与业务收据账本。持久保存的是业务记录（attempt / receipt / 关系），
    /// 不保存执行栈、Agent 引用或搜索树（§4.2）。
    /// </summary>
    public sealed class CoopLedger
    {
        public const int MaxAttemptsKept = 400;
        public const int MaxReceiptsKept = 400;

        public readonly Dictionary<string, CoopAttemptRecord> Attempts = new Dictionary<string, CoopAttemptRecord>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Receipts = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Relations = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly List<string> Unresolved = new List<string>();
        private int _sequence;

        public string NextId(string prefix)
        {
            _sequence++;
            return prefix + "-" + _sequence.ToString(CultureInfo.InvariantCulture);
        }

        public CoopAttemptRecord FindAttempt(CoopAttemptKey key)
        {
            CoopAttemptRecord record;
            return Attempts.TryGetValue(key.Key, out record) ? record : null;
        }

        public CoopAttemptRecord Record(
            CoopAttemptKey key, float chance, float threshold, float roll, bool success, double simTime, string source, string evidence)
        {
            var record = new CoopAttemptRecord
            {
                AttemptId = NextId("att"),
                ActorId = key.ActorId,
                TargetId = key.TargetId,
                Purpose = key.Purpose,
                Method = key.Method,
                Offer = key.Offer,
                Period = key.Period,
                Chance = chance,
                Threshold = threshold,
                Roll = roll,
                Success = success,
                SimTime = simTime,
                Source = source ?? string.Empty,
                Evidence = evidence ?? string.Empty
            };
            Attempts[key.Key] = record;
            TrimAttempts();
            return record;
        }

        private void TrimAttempts()
        {
            if (Attempts.Count <= MaxAttemptsKept)
            {
                return;
            }
            var list = new List<KeyValuePair<string, CoopAttemptRecord>>(Attempts);
            list.Sort((a, b) => a.Value.SimTime.CompareTo(b.Value.SimTime));
            int remove = Attempts.Count - MaxAttemptsKept;
            for (int i = 0; i < remove && i < list.Count; i++)
            {
                Attempts.Remove(list[i].Key);
            }
        }

        public bool HasReceipt(string operationId)
        {
            return !string.IsNullOrEmpty(operationId) && Receipts.ContainsKey(operationId);
        }

        /// <summary>记录一次已提交的业务效果；重复调用返回 false（幂等）。</summary>
        public bool TryCommitReceipt(string operationId, string detail)
        {
            if (string.IsNullOrEmpty(operationId) || Receipts.ContainsKey(operationId))
            {
                return false;
            }
            if (Receipts.Count >= MaxReceiptsKept)
            {
                var keys = new List<string>(Receipts.Keys);
                for (int i = 0; i < keys.Count / 4; i++)
                {
                    Receipts.Remove(keys[i]);
                }
            }
            Receipts[operationId] = detail ?? string.Empty;
            return true;
        }

        public string GetRelation(string actorId, string key)
        {
            string value;
            return Relations.TryGetValue(actorId + "|" + key, out value) ? value : null;
        }

        public void SetRelation(string actorId, string key, string value)
        {
            Relations[actorId + "|" + key] = value ?? string.Empty;
        }
    }

    /// <summary>
    /// 双方交互裁定：资格拒绝零掷骰；合资格一次掷点；结果只经公开答复送达。
    /// 能力计算由调用方以实际发起者／接受者提供（不读 Hero.MainHero）。
    /// </summary>
    public static class CoopInteraction
    {
        public sealed class Resolution
        {
            public bool Eligible;
            public bool Reused;
            public string Reason = string.Empty;
            public CoopAttemptRecord Attempt;
            public float Chance;
            public float Threshold;
        }

        /// <summary>
        /// 提交一次交涉。eligibility 返回 false 时直接拒绝且不消耗随机输入。
        /// 已有同键记录 → 复用，不再掷骰（T02）。
        /// </summary>
        public static Resolution Resolve(
            CoopLedger ledger,
            CoopAttemptKey key,
            bool eligible,
            string ineligibleReason,
            Func<float> chanceProvider,
            Func<float> rng,
            double simTime,
            string source,
            string evidence = null,
            Func<float, float, bool> adjudicate = null)
        {
            var result = new Resolution();
            if (!eligible)
            {
                result.Eligible = false;
                result.Reason = string.IsNullOrEmpty(ineligibleReason) ? "not_eligible" : ineligibleReason;
                return result;
            }
            result.Eligible = true;
            var existing = ledger.FindAttempt(key);
            if (existing != null)
            {
                result.Reused = true;
                result.Attempt = existing;
                result.Chance = existing.Chance;
                result.Threshold = existing.Threshold;
                result.Reason = "attempt_reused";
                return result;
            }
            float chance = chanceProvider != null ? chanceProvider() : 0.5f;
            if (chance < 0f) chance = 0f;
            if (chance > 1f) chance = 1f;
            float threshold = 1f - chance;
            float roll = rng != null ? rng() : 0f;
            if (roll < 0f) roll = 0f;
            if (roll > 1f) roll = 1f;
            // 铁律 17：d20 风格，掷点 ≥ 门槛成功。
            bool success = adjudicate != null ? adjudicate(chance, roll) : roll >= threshold;
            var record = ledger.Record(key, chance, threshold, roll, success, simTime, source, evidence);
            result.Attempt = record;
            result.Chance = chance;
            result.Threshold = threshold;
            result.Reason = success ? "success" : "failed";
            return result;
        }

        /// <summary>
        /// 稳定业务身份：改动文案、文件、节点顺序不改变它，因此不构成新的付款／掷骰资格。
        /// </summary>
        public static string OperationId(string purpose, string actorId, string targetId, string detail)
        {
            return "op:" + purpose + ":" + actorId + ":" + (targetId ?? "-") + ":" + (detail ?? "-");
        }
    }

    /// <summary>业务账本的持久序列化（只存业务，不存执行栈）。</summary>
    public sealed class CoopLedgerData
    {
        public int Version = 1;
        public List<CoopAttemptRecord> Attempts = new List<CoopAttemptRecord>();
        public Dictionary<string, string> Receipts = new Dictionary<string, string>();
        public Dictionary<string, string> Relations = new Dictionary<string, string>();
        public List<string> Unresolved = new List<string>();
        public int Sequence;

        public static CoopLedgerData From(CoopLedger ledger)
        {
            var data = new CoopLedgerData { Sequence = ledger.Attempts.Count + ledger.Receipts.Count };
            data.Attempts.AddRange(ledger.Attempts.Values);
            foreach (var pair in ledger.Receipts)
            {
                data.Receipts[pair.Key] = pair.Value;
            }
            foreach (var pair in ledger.Relations)
            {
                data.Relations[pair.Key] = pair.Value;
            }
            data.Unresolved.AddRange(ledger.Unresolved);
            return data;
        }

        public CoopLedger ToLedger()
        {
            var ledger = new CoopLedger();
            if (Attempts != null)
            {
                foreach (var record in Attempts)
                {
                    if (record == null || string.IsNullOrEmpty(record.ActorId))
                    {
                        continue;
                    }
                    var key = new CoopAttemptKey
                    {
                        ActorId = record.ActorId,
                        TargetId = record.TargetId,
                        Purpose = record.Purpose,
                        Method = record.Method,
                        Offer = record.Offer,
                        Period = record.Period
                    };
                    ledger.Attempts[key.Key] = record;
                }
            }
            if (Receipts != null)
            {
                foreach (var pair in Receipts)
                {
                    ledger.Receipts[pair.Key] = pair.Value;
                }
            }
            if (Relations != null)
            {
                foreach (var pair in Relations)
                {
                    ledger.Relations[pair.Key] = pair.Value;
                }
            }
            if (Unresolved != null)
            {
                ledger.Unresolved.AddRange(Unresolved);
            }
            return ledger;
        }
    }
}
