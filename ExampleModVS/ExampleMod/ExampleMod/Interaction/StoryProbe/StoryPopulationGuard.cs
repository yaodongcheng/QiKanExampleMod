using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// **剧情 Mission 的人口限制标记**（2026-11-15 立，对应总纲 §三 S1 / §14.4 P1）。
    ///
    /// 它只回答一个问题：**这个 Mission 是不是"只有剧情名单"的那一场？**
    ///   · 专用入口（`StoryProbeBehavior.EnterTownCenter`）在打开 Mission **之前**记一个待绑定请求；
    ///   · `MySubModule.OnMissionBehaviorInitialize` 在**任何原版生成之前**把请求绑到实际 Mission；
    ///   · 原生生成补丁只看"这个 Mission 实例有没有标记"，与 `EnterViaOption` 之类会被消耗的旗标无关。
    ///
    /// 🔴 纪律：
    ///   · 不"先让居民出生再删除"；只在生成入口处拦。
    ///   · 请求只绑一次；绑定即消费；普通逛城 / 地牢 / 下一次 Mission 不会继承。
    ///   · 打开失败、离场、重启剧情 = 释放标记；标记在 Mission 存续期内不会被撤销成"恢复原版人口"。
    /// </summary>
    public static class StoryPopulationGuard
    {
        private static Mission _activeMission;
        private static string _requestId;
        private static string _settlementId;
        private static string _stageId;
        private static bool _pending;

        private static readonly Dictionary<string, int> Blocked = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> Passed = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>普通入口永远不走这里；只有专用入口在打开 Mission 前调用。</summary>
        public static void Request(string settlementId, string stageId)
        {
            // 运行中的场景不能撤销人口限制；只在旧场已离开时清理残留。
            if (_activeMission != null)
            {
                if (IsActive) throw new InvalidOperationException("A story mission already owns the population marker.");
                Release(_activeMission, "stale marker before new entry");
            }
            _pending = true;
            _requestId = "pop-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            _settlementId = settlementId;
            _stageId = stageId;
            DebugLogger.Log($"[StoryPopulation] request {_requestId} settlement={settlementId} stage={stageId}");
        }

        public static bool HasPending => _pending;
        public static string RequestId => _requestId;

        /// <summary>把待请求绑到实际 Mission —— 必须在原版生成之前调用。</summary>
        public static bool Bind(Mission mission)
        {
            if (!_pending || mission == null) return false;
            // 同一时刻只允许一场；旧标记没有释放就换场 = 配置/时序错误，释放旧的再绑新的。
            if (_activeMission != null && _activeMission != mission)
            {
                DebugLogger.Log("[StoryPopulation] bind rejected: another mission owns the marker");
                CancelPending("another mission owns the marker");
                return false;
            }

            _attemptedBind = true;
            _activeMission = mission;
            _pending = false;
            Blocked.Clear();
            Passed.Clear();
            string scene = "";
            try { scene = mission.SceneName ?? ""; } catch { }
            DebugLogger.Log($"[StoryPopulation] bound {_requestId} to mission scene='{scene}' settlement={_settlementId} stage={_stageId} " +
                            "(native population sources are blocked from the first spawn onward)");
            return true;
        }

        private static bool _attemptedBind;

        /// <summary>本 Mission 是否受剧情人口限制。</summary>
        public static bool IsActive
        {
            get
            {
                if (_activeMission == null) return false;
                try { return ReferenceEquals(Mission.Current, _activeMission); }
                catch { return false; }
            }
        }

        /// <summary>原生生成入口的统一判据。返回 true = 本次生成必须被拦下。</summary>
        public static bool ShouldBlock(string source)
        {
            if (!IsActive) return false;
            Count(Blocked, source);
            int n = Blocked[source];
            // 每个来源只在前 5 次打日志，避免刷屏；计数始终累计。
            if (n <= 5) DebugLogger.Log($"[StoryPopulation] blocked {source} (#{n})");
            return true;
        }

        /// <summary>放行的来源（例如剧情演员走的自有生成链），只用于诊断对照。</summary>
        public static void NotePass(string source) { if (IsActive) Count(Passed, source); }

        private static void Count(Dictionary<string, int> table, string source)
        {
            if (string.IsNullOrEmpty(source)) source = "unknown";
            table.TryGetValue(source, out int n);
            table[source] = n + 1;
        }

        public static void Release(Mission mission, string reason)
        {
            if (mission != null && !ReferenceEquals(mission, _activeMission)) return;
            if (_activeMission == null) return;
            DebugLogger.Log($"[StoryPopulation] release {_requestId} ({reason})");
            _activeMission = null;
            _requestId = null;
            _settlementId = null;
            _stageId = null;
        }

        /// <summary>打开失败 / 离场时废止尚未绑定的请求，避免被下一次普通逛城继承。</summary>
        public static void CancelPending(string reason)
        {
            if (!_pending) return;
            DebugLogger.Log($"[StoryPopulation] pending request {_requestId} cancelled ({reason})");
            _pending = false;
            _requestId = null;
            _settlementId = null;
            _stageId = null;
        }

        /// <summary>`custom.story_probe population status` 正文（纯英文）。</summary>
        public static string Describe()
        {
            var sb = new StringBuilder();
            sb.Append("OK: population guard ").Append(IsActive ? "ACTIVE" : (_pending ? "PENDING" : "inactive"));
            if (_activeMission != null)
            {
                string scene = "";
                try { scene = _activeMission.SceneName ?? ""; } catch { }
                sb.Append(" request=").Append(_requestId ?? "-")
                  .Append(" settlement=").Append(_settlementId ?? "-")
                  .Append(" stage=").Append(_stageId ?? "-")
                  .Append(" scene='").Append(scene).Append('\'');
            }
            if (!_attemptedBind) sb.Append(" (no bind attempted yet)");
            sb.Append("\n  blocked: ").Append(Join(Blocked));
            sb.Append("\n  allowed-notes: ").Append(Join(Passed));
            return sb.ToString();
        }

        private static string Join(Dictionary<string, int> table)
        {
            if (table.Count == 0) return "(none)";
            var parts = new List<string>();
            foreach (var kv in table) parts.Add(kv.Key + "=" + kv.Value);
            parts.Sort(StringComparer.Ordinal);
            return string.Join(", ", parts.ToArray());
        }
    }
}
