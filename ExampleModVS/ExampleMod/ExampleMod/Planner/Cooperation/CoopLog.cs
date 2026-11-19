using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;

namespace LivingWorldNpcs.Planner.Cooperation
{
    /// <summary>
    /// 协作过程的单行结构化日志。标签固定 [Coop]，值纯英文；玩家界面不读日志。
    /// 字段见测试与日志说明 §3.1：event/seq/run/sceneEpoch/actor/agent/target/...
    /// </summary>
    public static class CoopLog
    {
        public const string Tag = "[Coop]";
        private static long _seq;
        private static string _run = string.Empty;
        private static int _sceneEpoch;
        private static bool _verbose = true;
        /// <summary>日志出口；游戏内装 DebugLogger.Log，离线测试装内存收集器。</summary>
        public static Action<string> Sink;
        private static readonly List<string> Ring = new List<string>();
        private const int RingCapacity = 512;

        public static void SetRun(string runId)
        {
            _run = runId ?? string.Empty;
        }

        public static void SetSceneEpoch(int epoch)
        {
            _sceneEpoch = epoch;
        }

        public static void SetVerbose(bool verbose)
        {
            _verbose = verbose;
        }

        public static long NextSeq()
        {
            return ++_seq;
        }

        public static long Seq { get { return _seq; } }

        /// <summary>最近 N 条关键记录（status/trace 命令用）；完整日志始终在文件里。</summary>
        public static List<string> Recent()
        {
            lock (Ring)
            {
                return new List<string>(Ring);
            }
        }

        public static CoopLogEntry Event(string name)
        {
            return new CoopLogEntry(name);
        }

        /// <summary>只记一次的文件级异常提示，避免刷屏。</summary>
        public static void Write(CoopLogEntry entry)
        {
            if (entry == null)
            {
                return;
            }
            string line = entry.Build(_run, _sceneEpoch);
            lock (Ring)
            {
                Ring.Add(line);
                if (Ring.Count > RingCapacity)
                {
                    Ring.RemoveRange(0, Ring.Count - RingCapacity);
                }
            }
            if (_verbose && Sink != null)
            {
                Sink(line);
            }
        }

        internal static string Quote(string value)
        {
            if (value == null)
            {
                return "null";
            }
            return JsonConvert.SerializeObject(value);
        }
    }

    /// <summary>日志条目构造器：未赋值的字段不写入（不凭空造编号）。</summary>
    public sealed class CoopLogEntry
    {
        private readonly string _event;
        private readonly List<KeyValuePair<string, string>> _fields = new List<KeyValuePair<string, string>>();

        public CoopLogEntry(string name)
        {
            _event = name ?? "unknown";
        }

        private CoopLogEntry Add(string key, string value)
        {
            if (value != null)
            {
                _fields.Add(new KeyValuePair<string, string>(key, value));
            }
            return this;
        }

        private CoopLogEntry AddRaw(string key, string raw)
        {
            if (raw != null)
            {
                _fields.Add(new KeyValuePair<string, string>(key, raw));
            }
            return this;
        }

        public CoopLogEntry Actor(string id) { return Add("actor", CoopLog.Quote(id)); }
        public CoopLogEntry Agent(int index) { return AddRaw("agent", index.ToString(CultureInfo.InvariantCulture)); }
        public CoopLogEntry Target(string id) { return Add("target", CoopLog.Quote(id)); }
        public CoopLogEntry Activity(string id) { return Add("activity", CoopLog.Quote(id)); }
        public CoopLogEntry Source(string source) { return Add("source", CoopLog.Quote(source)); }
        public CoopLogEntry Owner(int owner) { return AddRaw("owner", owner.ToString(CultureInfo.InvariantCulture)); }
        public CoopLogEntry PlanRevision(int revision) { return AddRaw("planRevision", revision.ToString(CultureInfo.InvariantCulture)); }
        public CoopLogEntry Stage(string stage) { return Add("stage", CoopLog.Quote(stage)); }
        public CoopLogEntry Step(string step) { return Add("step", CoopLog.Quote(step)); }
        public CoopLogEntry Request(string id) { return Add("request", CoopLog.Quote(id)); }
        public CoopLogEntry Attempt(string id) { return Add("attempt", CoopLog.Quote(id)); }
        public CoopLogEntry Receipt(string id) { return Add("receipt", CoopLog.Quote(id)); }
        public CoopLogEntry State(string state) { return Add("state", CoopLog.Quote(state)); }
        public CoopLogEntry Result(string result) { return Add("result", CoopLog.Quote(result)); }
        public CoopLogEntry Reason(string reason) { return Add("reason", CoopLog.Quote(reason)); }
        public CoopLogEntry ConfigVersion(string version) { return Add("configVersion", CoopLog.Quote(version)); }
        public CoopLogEntry ConfigHash(string hash) { return Add("configHash", CoopLog.Quote(hash)); }
        public CoopLogEntry FactsVersion(int version) { return AddRaw("factsVersion", version.ToString(CultureInfo.InvariantCulture)); }
        public CoopLogEntry SimTime(double seconds) { return AddRaw("simTime", seconds.ToString("0.###", CultureInfo.InvariantCulture)); }
        public CoopLogEntry TestMode(bool test) { return AddRaw("testMode", test ? "true" : "false"); }
        public CoopLogEntry Injection(string injection) { return Add("injection", CoopLog.Quote(injection)); }
        public CoopLogEntry ContractMode(string mode) { return Add("contractMode", CoopLog.Quote(mode)); }
        public CoopLogEntry Operation(string id) { return Add("operationId", CoopLog.Quote(id)); }
        public CoopLogEntry Detail(string text) { return Add("detail", CoopLog.Quote(text)); }
        public CoopLogEntry Value(string key, string value) { return Add(key, CoopLog.Quote(value)); }
        public CoopLogEntry Raw(string key, double value) { return AddRaw(key, value.ToString("0.###", CultureInfo.InvariantCulture)); }
        public CoopLogEntry Raw(string key, int value) { return AddRaw(key, value.ToString(CultureInfo.InvariantCulture)); }

        public string Build(string run, int sceneEpoch)
        {
            var builder = new StringBuilder(160);
            builder.Append(CoopLog.Tag).Append('{');
            builder.Append("\"event\":").Append(CoopLog.Quote(_event));
            builder.Append(",\"seq\":").Append(CoopLog.NextSeq().ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(run))
            {
                builder.Append(",\"run\":").Append(CoopLog.Quote(run));
            }
            builder.Append(",\"sceneEpoch\":").Append(sceneEpoch.ToString(CultureInfo.InvariantCulture));
            foreach (var pair in _fields)
            {
                builder.Append(',').Append(CoopLog.Quote(pair.Key)).Append(':').Append(pair.Value);
            }
            builder.Append('}');
            return builder.ToString();
        }

        public void Write()
        {
            CoopLog.Write(this);
        }
    }
}