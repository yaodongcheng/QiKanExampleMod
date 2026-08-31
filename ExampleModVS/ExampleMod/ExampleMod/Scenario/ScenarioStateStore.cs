using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 剧本旗标计数仓门面（16b §3.2 + 16 §一 三档）：物理存储 = GlobalVariableBehavior._globalStates（存档键 lwn_scn_state）。
    /// key 带前缀四类：Flag:: / Time::counter_N / Variable:: / GlobalSlot::（11 表）。
    /// </summary>
    public static class ScenarioStateStore
    {
        private static GlobalVariableBehavior Sink => GlobalVariableBehavior.Instance;

        // 状态表达三层纪律（01）：事件执行过 = Event::<id>.done（调度器记，不走本仓）；分支选择 = Ctx::（事件内，无档）；
        // Flag:: 只留跨事件持久自定义标记。计数器 = Time::counter_N（每日 +1，见 ScenarioCampaignBehavior.OnDailyTick）。

        public static void SetFlag(string flag, bool val = true) => Set("Flag::" + flag, val ? "1" : "0");
        public static void ClearFlag(string flag) => Set("Flag::" + flag, "0");
        public static bool GetFlag(string flag) => Get("Flag::" + flag) == "1";

        public static void SetVariable(string name, string val) => Set("Variable::" + name, val);
        public static string GetVariable(string name) => Get("Variable::" + name);

        public static void GlobalSet(string slot, string refString) => Set("GlobalSlot::" + slot, refString);
        public static string GlobalGet(string slot) => Get("GlobalSlot::" + slot);

        /// <summary>计数器读/写（Time::counter_N；每日 +1 在 ScenarioCampaignBehavior）</summary>
        public static int GetCounter(int n) => int.TryParse(Get("Time::counter_" + n), out var v) ? v : 0;
        public static void SetCounter(int n, int val) => Set("Time::counter_" + n, val.ToString(CultureInfo.InvariantCulture));
        public static void CounterReset(int n) => SetCounter(n, 0);

        private static void Set(string key, string val)
        {
            if (string.IsNullOrEmpty(key)) return;
            try { Sink?.SetGlobalState(key, val); }
            catch (System.Exception e) { DebugLogger.Log($"[Scenario] StateStore Set 失败 {key}: {e.Message}"); }
        }

        /// <summary>按原始键读（Event::X.done / Time::assessment_flag / counter_N —— 读链统一入口）</summary>
        public static string GetRaw(string key) => Get(key);

        private static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            try { return Sink?.GetGlobalState(key); }
            catch (System.Exception e) { DebugLogger.Log($"[Scenario] StateStore Get 失败 {key}: {e.Message}"); return null; }
        }
    }

    /// <summary>
    /// 剧本战役行为：挂日 tick 给计数器 +1（Time::counter_N，16b §3.2「挂 Campaign 层日 tick，不挂 OnApplicationTick」）。
    /// W1 只承担计数器；trigger 调度锚点 = W4。
    /// </summary>
    public class ScenarioCampaignBehavior : CampaignBehaviorBase
    {
        private static readonly Regex CounterRegex = new Regex("^Time::counter_(\\d+)$", RegexOptions.Compiled);

        public override void RegisterEvents()
        {
            // 项目惯例（MyBehavior.cs:46 同款）：DailyTickEvent + AddNonSerializedListener
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnDailyTick()
        {
            var sink = GlobalVariableBehavior.Instance;
            if (sink == null) return;

            // 复制键列表防遍历中修改；计数器全部 +1（太阁「距那件事过了几天」全靠它）
            List<KeyValuePair<string, int>> counters = new List<KeyValuePair<string, int>>();
            foreach (var key in sink.EnumerateGlobalStateKeys())
            {
                var m = CounterRegex.Match(key);
                if (!m.Success) continue;
                counters.Add(new KeyValuePair<string, int>(
                    key, int.TryParse(sink.GetGlobalState(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0));
            }
            foreach (var kv in counters)
            {
                try { sink.SetGlobalState(kv.Key, (kv.Value + 1).ToString(CultureInfo.InvariantCulture)); }
                catch (System.Exception e) { DebugLogger.Log($"[Scenario] counter {kv.Key} +1 失败: {e.Message}"); }
            }
        }
    }
}
