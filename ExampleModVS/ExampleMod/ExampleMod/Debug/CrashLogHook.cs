using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 全局异常钩子：把 mod 相关的异常自动写进运行日志，崩溃现场直接 Read
    /// Debug/StoryEngine_RuntimeLog.txt 即可，无需玩家手动复制异常对话框。
    /// 三层覆盖：
    /// ① FirstChanceException —— 被 try-catch 吞掉的异常（如 LLM 调用失败静默降级），最常见最隐蔽；
    /// ② UnobservedTaskException —— fire-and-forget async 任务的未观察异常；
    /// ③ UnhandledException —— 致命异常，记录后把当前日志快照到 Debug/crash/（下次启动会覆盖原日志）。
    /// 噪声控制：只记「我们的代码抛出的异常」或「网络/JSON 等可疑类型且栈里有我们的代码」的异常；
    /// 同型同抛出点 5 秒内去重，防止每帧抛异常的代码刷爆日志。
    /// </summary>
    public static class CrashLogHook
    {
        private const string OurNamespace = "LivingWorldNpcs";

        // 可疑类型：TargetSite 不在我们命名空间，但抛自 .NET/引擎且栈里可能有我们的代码
        private static readonly HashSet<string> _interestingTypes = new HashSet<string>
        {
            "System.Net.Http.HttpRequestException",
            "System.Threading.Tasks.TaskCanceledException", // HttpClient 超时
            "System.Net.WebException",
            "System.Net.Sockets.SocketException",
            "Newtonsoft.Json.JsonException",
            "System.Net.Http.HttpIOException",
        };

        // 去重：key=(异常类型|抛出方法)，最近一次记录时间
        private static readonly Dictionary<string, DateTime> _lastLogByKey = new Dictionary<string, DateTime>();
        private static readonly object _lock = new object();

        public static void Register()
        {
            // ① 被吞掉的异常——必须保持处理极快：正常路径只有一次命名空间比较
            AppDomain.CurrentDomain.FirstChanceException += (sender, e) =>
            {
                try
                {
                    var ex = e.Exception;
                    if (ex == null) return;
                    if (!IsWorthLogging(ex)) return;
                    LogDeduped($"[FirstChance] {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
                }
                catch
                {
                    // 钩子自身出错绝不回抛（会在每次异常时递归触发自己）
                }
            };

            // ② fire-and-forget async 的坑：Task 出错但没人 await
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                try
                {
                    var ex = e.Exception;
                    if (ex != null && IsWorthLogging(ex))
                        LogDeduped($"[UnobservedTask] {ex}");
                    e.SetObserved(); // 无论是否记录都标记已观察，避免进程被终结
                }
                catch
                {
                }
            };

            // ③ 致命异常：记录 + 快照当前日志（下次启动 WriteAllText 会清掉现场）
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                try
                {
                    string msg = $"[Crash] UnhandledException: {(e.ExceptionObject as Exception)?.ToString() ?? e.ExceptionObject?.ToString() ?? "unknown"}";
                    DebugLogger.Log(msg);
                    SnapshotLog();
                }
                catch
                {
                }
            };
        }

        /// <summary>快速判定值不值得记录。常规异常只走 TargetSite 命名空间比较（无栈遍历）。</summary>
        private static bool IsWorthLogging(Exception ex)
        {
            var ns = ex.TargetSite?.DeclaringType?.Namespace;
            if (ns != null && ns.StartsWith(OurNamespace))
                return true;

            // 引擎/.NET 抛的类型：只有「可疑类型」才建栈检查，防止每帧引擎正常异常拖慢游戏
            if (ex.GetType().FullName != null && _interestingTypes.Contains(ex.GetType().FullName))
            {
                var st = ex.StackTrace;
                return st != null && st.Contains(OurNamespace);
            }
            return false;
        }

        private static void LogDeduped(string msg)
        {
            string key = null;
            lock (_lock)
            {
                // key 从消息首行派生（异常类型|抛出方法）
                int bar = msg.IndexOf(':');
                int nl = msg.IndexOf('\n');
                key = bar > 0 && nl > bar ? msg.Substring(0, bar) + "|" + msg.Substring(bar + 1, nl - bar - 1).Trim() : msg;
                if (_lastLogByKey.TryGetValue(key, out var last) && (DateTime.Now - last).TotalSeconds < 5)
                    return; // 同型同点 5 秒内已记过，静默去重
                _lastLogByKey[key] = DateTime.Now;
            }
            DebugLogger.Log(msg);
        }

        /// <summary>把当前运行日志快照到 Debug/crash/ 带时间戳的文件，保留崩溃现场。</summary>
        private static void SnapshotLog()
        {
            try
            {
                string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string moduleRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(dllPath), "..", ".."));
                string debugDir = Path.Combine(moduleRoot, "Debug");
                string src = Path.Combine(debugDir, "StoryEngine_RuntimeLog.txt");
                if (!File.Exists(src)) return;

                string crashDir = Path.Combine(debugDir, "crash");
                Directory.CreateDirectory(crashDir);
                string dst = Path.Combine(crashDir, $"StoryEngine_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.Copy(src, dst, overwrite: false);
            }
            catch
            {
            }
        }
    }
}
