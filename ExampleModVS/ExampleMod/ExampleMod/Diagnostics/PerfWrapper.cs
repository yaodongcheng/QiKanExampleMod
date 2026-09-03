using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ModuleManager;

namespace LivingWorldNpcs
{
    /// <summary>包裹槽的快照行（其他 DLL 的 tick 方法耗时）。</summary>
    public class WrapSlotInfo
    {
        public string Name;    // Assembly 短名 + DeclaringType.MethodName
        public float Ms;       // 1s 窗口累计 ms
        public float MaxMs;    // 单次峰值 ms
        public int Count;      // 调用次数
    }

    /// <summary>
    /// B 层：通用 DLL 归因——运行时动态包裹「所有 mod 的标准每帧入口」，无需对方配合、不维护名单。
    ///
    /// 包裹目标三类（幂等，HashSet&lt;MethodInfo&gt; 去重——Harmony 对同方法重复 Patch 会重复执行）：
    ///   a) Mission：MissionBehaviors → OnMissionTick / OnPreDisplayMissionTick（Mission.Current 变化时补装）；
    ///   b) 进程级 once：Module.CurrentModule.SubModules（走 V.CollectSubModules()，GetInstance 为 internal 不可调）→ 各 mod override 的 OnApplicationTick
    ///      （跳过 DeclaringType==MBSubModuleBase 的空基方法，避免把全体未改写 submodule 噪声包进来）；
    ///   c) Campaign 兜底：Campaign.Current.CampaignEntityComponents → OnTick(float,float)（覆盖量低）。
    /// 排除 LivingWorldNpcs 自身程序集（与 A 层插桩双计）。
    ///
    /// 🔴 patch 常驻（~3μs/帧底噪）：Enabled（= MCM ③）只控「计时 + 显示 + 卡顿行 [Wrap] 段」，
    ///    关→开即时生效，无需等新场景。计时配对用 [ThreadStatic] Stack（AI 并行线程不串，
    ///    栈深 &gt; 64 清空自愈）；三包装前后全 try/catch 防第三方行为被补丁搞崩。
    /// </summary>
    public static class PerfWrapper
    {
        /// <summary>= Settings.Instance.ShowPerfDetails（MCM 详情开关），宿主每帧透传。</summary>
        public static bool Enabled { get; set; }

        private static readonly Harmony _harmony = new Harmony("com.ydc.LivingWorldNpcs.PerfWrap");
        private static readonly ConcurrentDictionary<MethodBase, int> _slotIds = new ConcurrentDictionary<MethodBase, int>();
        private static readonly ConcurrentDictionary<MethodInfo, byte> _patched = new ConcurrentDictionary<MethodInfo, byte>();
        private static readonly List<string> _slotNames = new List<string>();
        private static readonly object _slotsLock = new object();
        private static int _slotCounter;

        // ── 窗口累计（主线程写；诊断工具可容忍竞态丢累计）──
        private static float[] _accWindow = new float[64];
        private static float[] _maxWindow = new float[64];
        private static int[] _cntWindow = new int[64];
        private static float[] _snapAcc = new float[64];
        private static float[] _snapMax = new float[64];
        private static int[] _snapCnt = new int[64];
        private static long _windowStartTicks = long.MinValue;

        // ── 安装惰性标记 ──
        private static bool _processDone;
        private static bool _campaignDone;
        private static Mission _lastMission;

        // ── 计时配对栈（ThreadStatic：AI 并行线程各自独立配对）──
        [ThreadStatic]
        private static Stack<(int id, long t0)> _t0Stack;

        static PerfWrapper()
        {
            // 卡顿行 [Wrap] 段钩子（PerfProfiler.LogStutter 调用；无包裹数据返回 null）
            PerfProfiler.StutterWrapSummaryHook = StutterSummary;
        }

        /// <summary>宿主每帧调用（节流扫描 + Enabled 已在外部透传）。</summary>
        public static void Tick()
        {
            long now = PerfProfiler.Now();
            RollWindow(now);

            if (!_processDone)
            {
                _processDone = true;
                try { InstallProcessTargets(); }
                catch (Exception ex) { DebugLogger.Log($"[PerfWrap] process install failed: {ex.Message}"); }
            }

            if (!_campaignDone && Campaign.Current != null)
            {
                // 🔴 就绪检查 = 反射读 _campaignEntitySystem，≠ null 才碰 getter：
                //    CampaignEntityComponents getter 无空守卫（=> _campaignEntitySystem.Components），
                //    加载窗口期直接调必抛 NRE——catch 了 vs 不 catch，VS「抛出时中断」照样每帧弹一次。
                //    （2026-09-03 实机：catch+重试写法仍被 NRE 弹窗刷屏）
                if (CampaignEntitySystemReady())
                {
                    _campaignDone = true;
                    try { InstallCampaignTargets(); }
                    catch (Exception ex) { DebugLogger.Log($"[PerfWrap] campaign install failed: {ex.Message}"); }
                }
                // else：实体系统尚未初始化（新战役/读档加载窗口期）→ 下一帧重试
            }

            // Mission 补装：**只在 Mission.Current 引用变化时扫**（🔴 2026-09-03 实机教训：
            // 原「每 2s 兜底扫描」在 mission 内周期全量 GetMethods 反射 + 失败重试 = 每 2s 一帧
            // 卡顿 + 日志刷屏；同 mission 期间无新方法，无需周期扫）
            var mission = Mission.Current;
            if (mission != null && mission != _lastMission)
            {
                _lastMission = mission;
                try { InstallMissionTargets(mission); }
                catch (Exception ex) { DebugLogger.Log($"[PerfWrap] mission install failed: {ex.Message}"); }
            }
        }

        // ═══════════════════════════ 安装 ═══════════════════════════

        private static void InstallProcessTargets()
        {
            foreach (var sub in V.CollectSubModules())
            {
                if (sub == null) continue;
                var m = sub.GetType().GetMethod("OnApplicationTick",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null || m.IsAbstract) continue;
                if (m.DeclaringType == typeof(MBSubModuleBase)) continue; // 空基方法：全体不 Rewrite 的噪声
                TryPatch(m);
            }
        }

        /// <summary>Campaign.EntitySystem 就绪检查——引擎私有字段 _campaignEntitySystem（1.2.12~1.5.1 同名实锤）。
        /// getter 无空守卫（=> _campaignEntitySystem.Components），加载窗口期（Current 已置、字段未初始化）
        /// 直接调 CampaignEntityComponents 必抛 NRE；反射读字段 = 零异常的就绪检测。</summary>
        private static readonly FieldInfo _campaignEntitySystemField =
            typeof(Campaign).GetField("_campaignEntitySystem",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static bool CampaignEntitySystemReady()
        {
            Campaign campaign = Campaign.Current;
            if (campaign == null) return false;
            if (_campaignEntitySystemField == null) return true; // 未来版本字段改名：放行，外层 catch 兜底
            return _campaignEntitySystemField.GetValue(campaign) != null;
        }

        private static void InstallCampaignTargets()
        {
            foreach (var c in Campaign.Current.CampaignEntityComponents)
            {
                if (c == null) continue;
                var m = c.GetType().GetMethod("OnTick",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null || m.IsAbstract || m.GetParameters().Length != 2) continue;
                TryPatch(m);
            }
        }

        private static void InstallMissionTargets(Mission mission)
        {
            foreach (var behavior in mission.MissionBehaviors)
            {
                if (behavior == null) continue;
                foreach (string name in TapNames())
                {
                    // 🔴 不用 GetMethod：个别第 3 方行为类有同名重载 → AmbiguousMatchException；
                    //    🔴 🔴 !IsAbstract 过滤（2026-09-03 实机刷屏+卡顿根因）：GetMethods 返回继承链
                    //    上【所有】声明（含基类 abstract，如 1.4.8 MissionBehavior.OnMissionTick 是
                    //    abstract——Harmony 报「can only patch implemented methods」→ 异常+日志刷屏）。
                    //    `!IsAbstract` = 只取具体实现：未 override 的行为落基类声明（concrete，合并包
                    //    装点）；abstract 声明一律跳过；中间类 override（concrete）照常收。
                    var methods = behavior.GetType()
                        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(mm => !mm.IsStatic && !mm.IsAbstract && mm.Name == name && mm.GetParameters().Length == 1);
                    foreach (var m in methods)
                        TryPatch(m);
                }
            }
        }

        private static readonly string[] _tapNames = { "OnMissionTick", "OnPreDisplayMissionTick" };
        private static string[] TapNames() => _tapNames;

        private static void TryPatch(MethodInfo m)
        {
            if (m == null || m.DeclaringType == null) return;
            if (m.IsAbstract) return;   // 抽象方法声明不可 patch（具体实现在链上另有条目）
            // 🔴 🔴 抽象类里声明的 virtual 方法（空体）也不能 patch：Harmony 报「can only patch
            // implemented methods... Patch the declared method on <具体类> instead」——抽象类方法
            // 它要求在具体类里重声明（实机 2026-09-03：107 条 skip 全来源此——MissionBehavior /
            // ButterLib wrapper 等抽象基类）。未 override 的引擎调抽象类空实现（零耗时，不测无
            // 损失）；具体 override 以具体类声明再次出现 → 正常 patch。
            if (m.DeclaringType.IsAbstract) return;
            if (m.DeclaringType.Assembly == typeof(MySubModule).Assembly) return; // 排除自身（A 层已插桩）
            if (!_patched.TryAdd(m, 0)) return; // 幂等：成功或失败都算处理过，永不重试（防周期重试刷屏）
            try
            {
                EnsureSlot(m);
                _harmony.Patch(m,
                    prefix: new HarmonyMethod(typeof(PerfWrapper), nameof(Prefix)),
                    postfix: new HarmonyMethod(typeof(PerfWrapper), nameof(Postfix)));
                if (Settings.Instance.ShowDebugMessages)
                    DebugLogger.Log($"[PerfWrap] wrapped {m.DeclaringType.Name}.{m.Name} ({shortAsm(m)})");
            }
            catch (Exception ex)
            {
                // 🔴 失败保留 _patched 登记（永久跳过）——否则下轮扫描重试 = 异常构建 + 日志写盘循环
                // （实机 2026-09-03：每 2s 全量重试几十个失败点 = 周期性卡顿 + 日志刷屏）
                DebugLogger.Log($"[PerfWrap] skip once {m.DeclaringType.Name}.{m.Name}: {ex.Message}");
            }
        }

        private static int EnsureSlot(MethodInfo m)
        {
            return _slotIds.GetOrAdd(m, mi =>
            {
                int id;
                lock (_slotsLock)
                {
                    id = _slotCounter++;
                    _slotNames.Add(SlotDisplayName(mi));
                    EnsureLocked(id + 1);
                }
                return id;
            });
        }

        /// <summary>
        /// 包裹槽显示名："{DLL 短名} {类名}.{方法名}"——DLL 名只取最后一段（TaleWorlds.MountAndBlade.View
        /// → View；第三方 mod 名本来短）；类名 + 方法名为主体。🔴 2026-09-03（用户反馈：全名太长面板换行）。
        /// </summary>
        private static string SlotDisplayName(MethodBase m)
        {
            string dll = m?.DeclaringType?.Assembly?.GetName().Name ?? "?";
            int dot = dll.LastIndexOf('.');
            if (dot >= 0 && dot < dll.Length - 1) dll = dll.Substring(dot + 1);
            string type = m?.DeclaringType?.Name ?? "?";
            string method = m?.Name ?? "?";
            return $"{dll} {type}.{method}";
        }

        private static string shortAsm(MethodBase m)
        {
            string name = m?.DeclaringType?.Assembly?.GetName().Name ?? "?";
            return name;
        }

        /// <summary>🔴 锁内调用（EnsureSlot 的 lock 上下文）——数组只增不缩。</summary>
        private static void EnsureLocked(int size)
        {
            if (_accWindow.Length < size)
            {
                Array.Resize(ref _accWindow, size);
                Array.Resize(ref _maxWindow, size);
                Array.Resize(ref _cntWindow, size);
                Array.Resize(ref _snapAcc, size);
                Array.Resize(ref _snapMax, size);
                Array.Resize(ref _snapCnt, size);
            }
        }

        // ═══════════════════════════ 计时（Harmony 注入 __originalMethod）══════════════════════════

        public static void Prefix(MethodBase __originalMethod)
        {
            if (!Enabled || __originalMethod == null) return;
            if (!_slotIds.TryGetValue(__originalMethod, out int id)) return;
            var stack = _t0Stack ??= new Stack<(int, long)>(8);
            if (stack.Count > 64) stack.Clear();  // 开关切换造成的不平衡自愈
            stack.Push((id, System.Diagnostics.Stopwatch.GetTimestamp()));
        }

        public static void Postfix(MethodBase __originalMethod)
        {
            if (!Enabled || __originalMethod == null) return;
            var stack = _t0Stack;
            if (stack == null || stack.Count == 0) return;
            (int id, long t0) = stack.Pop();
            if (!_slotIds.TryGetValue(__originalMethod, out int expected) || expected != id) return;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            float ms = (float)((now - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            if (id < _accWindow.Length)
            {
                _accWindow[id] += ms;
                _cntWindow[id]++;
                if (ms > _maxWindow[id]) _maxWindow[id] = ms;   // 峰值追踪
            }
        }

        // ═══════════════════════════ 窗口与快照 ═══════════════════════════

        private static void RollWindow(long now)
        {
            if (_windowStartTicks == long.MinValue)
            {
                _windowStartTicks = now;
                return;
            }
            if (now - _windowStartTicks < System.Diagnostics.Stopwatch.Frequency)
                return;
            lock (_slotsLock)
            {
                Array.Copy(_accWindow, _snapAcc, _accWindow.Length);
                Array.Copy(_maxWindow, _snapMax, _accWindow.Length);
                Array.Copy(_cntWindow, _snapCnt, _accWindow.Length);
                Array.Clear(_accWindow, 0, _accWindow.Length);
                Array.Clear(_maxWindow, 0, _accWindow.Length);
                Array.Clear(_cntWindow, 0, _accWindow.Length);
                _windowStartTicks = now;
            }
        }

        /// <summary>按累计 ms 降序取前 N 个包裹槽（读时滚动窗口）。</summary>
        public static List<WrapSlotInfo> TopSlots(int limit)
        {
            RollWindow(PerfProfiler.Now());
            var list = new List<WrapSlotInfo>();
            lock (_slotsLock)
            {
                for (int i = 0; i < _slotNames.Count && i < _snapAcc.Length; i++)
                {
                    if (_snapAcc[i] <= 0.0001f) continue;
                    list.Add(new WrapSlotInfo
                    {
                        Name = _slotNames[i],
                        Ms = _snapAcc[i],
                        MaxMs = _snapMax[i],
                        Count = _snapCnt[i],
                    });
                }
            }
            list.Sort((a, b) => b.Ms.CompareTo(a.Ms));
            if (list.Count > limit) list.RemoveRange(limit, list.Count - limit);
            return list;
        }

        /// <summary>包裹槽总耗时（1s 窗口累计 ms；mod 占比之外「其他 DLL 占比」用）。</summary>
        public static float TotalMs()
        {
            RollWindow(PerfProfiler.Now());
            float total = 0f;
            lock (_slotsLock)
            {
                for (int i = 0; i < _snapAcc.Length; i++)
                    if (_snapAcc[i] > 0) total += _snapAcc[i];
            }
            return total;
        }

        /// <summary>卡顿行 [Wrap] 段（PerfProfiler 卡顿钩子调用；无数据返回 null 时行不带该段）。</summary>
        private static string StutterSummary()
        {
            if (!Enabled) return null;
            var tops = TopSlots(1);
            if (tops.Count == 0) return null;
            var t = tops[0];
            return $"[Wrap] {t.Name} {t.Ms:F1}ms x{t.Count}";
        }
    }
}
