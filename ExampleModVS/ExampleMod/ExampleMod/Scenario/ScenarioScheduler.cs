using System;
using System.Collections.Generic;
using System.Linq;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 剧本调度器（W4 最小集）：01 调度模型——
    /// ①trigger 挂名事件表（按加载序注册，01：文件声明顺序 = 确定性排序依据）
    /// ②互斥选路：priority 分层（weak &lt; normal）→ 层内按声明序逐事件 condition → 第一个满足触发，其余本轮跳过
    /// ③once 执行完成 → Event::&lt;id&gt;.done（仓）；多次事件留在扫描集
    /// ④🔴 执行中锁：_isRunning 时任何 trigger = 跳过不检查（01 防重入；脚本强开 mission 不检查）
    /// ⑤08 链路最小集开关 = 本阶段只挂 game_start / daily / monthly（无场景依赖；场景类 trigger = 后置清单）
    /// </summary>
    public static class ScenarioScheduler
    {
        private static readonly Dictionary<string, List<ScenarioEventDef>> _byTrigger =
            new Dictionary<string, List<ScenarioEventDef>>(StringComparer.Ordinal);

        private static bool _isRunning;
        private static bool _loaded;
        private static long _lastMonthlyDay = -1;

        public static ScenarioEventDef ExecutingEvent { get; private set; }

        /// <summary>供指令/测试用：强制确保加载+注册（一般由 OnTrigger 懒加载）</summary>
        public static void EnsureLoadedAll() => EnsureLoaded();

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            ScenarioLoader.LoadAll();
            foreach (var evt in ScenarioLoader.Events)
            {
                if (!_byTrigger.TryGetValue(evt.Trigger, out var list))
                    _byTrigger[evt.Trigger] = list = new List<ScenarioEventDef>();
                list.Add(evt);   // 声明顺序 = 加载序（ScnLoader 顺序）
            }
            DebugLogger.Log($"[Scenario][Scheduler] 注册完成：{ScenarioLoader.Events.Count} 事件 / {_byTrigger.Count} trigger 组");
        }

        /// <summary>trigger 触发入口（本相挂接：daily/monthly/game_start；执行中 = 跳过——01 锁）</summary>
        public static void OnTrigger(string trigger)
        {
            try
            {
                if (_isRunning) { DebugLogger.Log($"[Scenario][Scheduler] trigger {trigger} 触发时事件执行中（锁）→ 本轮跳过"); return; }
                EnsureLoaded();
                if (!_byTrigger.TryGetValue(trigger, out var list)) return;

                // 互斥选路：一次时机只演一个（01：normal 全不满足才轮到 weak）
                foreach (var tier in new[] { "normal", "weak" })
                    for (int i = 0; i < list.Count; i++)
                    {
                        var evt = list[i];
                        if (evt.Priority == "weak" != (tier == "weak")) continue;   // 分段（normal 段优先）
                        if (IsDone(evt.Id)) continue;
                        if (DslEvaluator.Evaluate(evt.Condition))
                        {
                            ExecuteEvent(evt);
                            return;   // 一轮只演一个（01 互斥选路）
                        }
                    }
            }
            catch (Exception e)
            {
                DebugLogger.Log($"[Scenario][Scheduler] OnTrigger({trigger}) 异常: {e.Message}");
            }
        }

        public static bool IsDone(string eventId) => ScenarioStateStore.GetRaw("Event::" + eventId) == "done";

        /// <summary>执行事件（once 完成 → Event::&lt;id&gt;.done 写仓；执行完锁释放）。force_event 同用。</summary>
        public static void ExecuteEvent(ScenarioEventDef evt)
        {
            if (evt == null) return;
            if (_isRunning) { DebugLogger.Log($"[Scenario][Scheduler] 执行中，拒绝触发 {evt.Id}"); return; }
            _isRunning = true;
            ExecutingEvent = evt;
            try
            {
                var ctx = ScenarioContext.Instance;
                ctx.Clear();
                ctx.InitForEvent(null, null);   // event_settlement/event_hero 来源 = TK5 代入/事件头（W4 最小集留空，W6 回填）
                DebugLogger.Log($"[Scenario][Scheduler] ▶ 开始事件 {evt.Id}（{evt.Trigger}）");
                bool completed = ScenarioExecutor.RunSteps(evt.Script, ctx);
                if (!completed)
                    DebugLogger.Log($"[Scenario][Scheduler] 事件 {evt.Id} 异常中止（未记 done）");
                else if (evt.Once)
                {
                    ScenarioStateStore.SetRaw("Event::" + evt.Id, "done");
                    DebugLogger.Log($"[Scenario][Scheduler] ✔ 事件 {evt.Id} 完成（once → done）");
                }
                else
                    DebugLogger.Log($"[Scenario][Scheduler] ✔ 事件 {evt.Id} 完成（多次事件，留在扫描集）");
            }
            catch (Exception e)
            {
                DebugLogger.Log($"[Scenario][Scheduler] 执行事件 {evt.Id} 异常（锁释放，不崩）: {e.Message}");
            }
            finally
            {
                _isRunning = false;
                ExecutingEvent = null;
            }
        }

        // ── 挂接点（ScenarioCampaignBehavior 回调）──

        public static void OnGameStartTick()
        {
            OnTrigger("game_start");
        }

        public static void OnDailyTick()
        {
            OnTrigger("daily");
            // monthly：自建钩子（16 §二：无原生每月事件）——按 dayOfYear 每 30 天触发一次
            long day = ScenarioClock.CurrentDayOfYear();
            if (day > 0 && day / 30 != _lastMonthlyDay && day % 30 == 0)
            {
                _lastMonthlyDay = day / 30;
                OnTrigger("monthly");
            }
        }

        public static void Reset()
        {
            _byTrigger.Clear();
            _loaded = false;
            _isRunning = false;
            _lastMonthlyDay = -1;
        }
    }
}
