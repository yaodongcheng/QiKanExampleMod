using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════════════════
    // 双人动作定义（2026-10-19，2026-10-19 晚精简）
    //
    // 数据源 = `ModuleData/DesignData/PairActions.csv`（四行头，与 Camera.csv 同一套
    // CsvLoader）。**一行 = 一个双人动作**；只保留"运行时真的会读"的列。
    //
    // 设计原则：
    //   · 能由 clip 元数据 / 适配器 / 全局常量决定的，一律不进表；
    //   · 两侧同值的（事件时刻、位移方式）合并成一列；
    //   · 侧别差异只保留"播什么"（`machine:名字` / `action:动作名`）。
    // ═══════════════════════════════════════════════════════════════════════════

    public enum PairPlayKind { None, Action, Machine }
    public enum PairMotionKind { None, Engine, Clip }
    public enum PairAttachMode { None, Persistent }
    public enum PairEndPolicy { Release, KillDefender, RagdollDefender, Hold, Knockout }

    /// <summary>
    /// 配对首帧相对站位（**defender 相对 attacker**，attacker 本地系）。
    /// 🔴 **只在水平面上**：没有 Z 列；竖直由地面/`clip` 根骨轨负责（默认两人同处一个水平面）。
    /// </summary>
    public sealed class PairStance
    {
        /// <summary>米；+X = attacker 右手侧。</summary>
        public float Right;

        /// <summary>米；+Y = attacker 正前。</summary>
        public float Forward;

        /// <summary>度；水平面朝向，0 = 同向，180 = 面对面。</summary>
        public float DirectionDeg;
    }

    /// <summary>一侧（attacker / defender）播什么。</summary>
    public sealed class PairSideDef
    {
        public PairPlayKind Play = PairPlayKind.None;

        /// <summary>`action:act_xxx` 里的动作名。</summary>
        public string ActionId;

        /// <summary>`machine:carry` 里的状态机名。</summary>
        public string MachineName;
    }

    /// <summary>
    /// **音效/粒子轨道的一条事件**（2026-10-23）：`time@id`。
    /// `Time` = 从演出（Playing）开始算的秒；`Id` = `module_sounds` 的事件名 / 粒子系统名。
    /// </summary>
    public sealed class PairTimedEvent
    {
        public float Time;
        public string Id;
    }

    /// <summary>CSV 的一行 = 一个双人动作。</summary>
    public sealed class PairActionDef
    {
        public string Id;

        /// <summary>
        /// `preposition`（默认）= 发起者先走到站位；
        /// `handoff` = 只能由另一个 pair 交接启动（直接进挂接态，不做预定位）。
        /// </summary>
        public string Start = "preposition";

        public PairStance Stance = new PairStance();

        public PairSideDef Attacker = new PairSideDef();
        public PairSideDef Defender = new PairSideDef();

        /// <summary>两侧**共用**的"进/出"时刻名（一个事件 token 同时 Force 两台状态机）。</summary>
        public string EnterEvent;
        public string ExitEvent;

        /// <summary>
        /// 两侧**共用**的位移方式：
        ///   · `engine` = 一次性 clip 自己带根运动（`displace_position` + `displacement` 字段）；
        ///   · `none`  = 原地 / 持续挂接（根骨轨只表达相对姿态）。
        /// </summary>
        public PairMotionKind Motion = PairMotionKind.None;

        public PairAttachMode AttachMode = PairAttachMode.None;
        public PairEndPolicy EndPolicy = PairEndPolicy.Release;

        /// <summary>
        /// **可选的交接目标**（不是结束策略）：本 pair 在挂接期间可以交给哪个后继 pair。
        /// 空 = 不能交接；非空 = `RequestHandoff` 只接受这个 id（调用方从这里读，不写死）。
        /// 例：`grapple_carry → mounted_on_mount`；平时结束仍走 `EndPolicy`（放下）。
        /// </summary>
        public string HandoffTo;

        /// <summary>
        /// 交接**触发器**的名字（与 `HandoffTo` 成对）：说明"在哪个动作之后交接"。
        /// 例：`put-on-mount` = 玩家在扛着人时选【放上马背】（`custom.grapple carry horse`）。
        /// 后继 pair 的参数 = 它的 `EnterEvent`/`ExitEvent`/`Stance_*`；新 attacker = 触发目标（那匹马）。
        /// </summary>
        public string HandoffOn;

        /// <summary>
        /// **音效轨道**：`time@事件名|time@事件名`（时间 = 从演出开始算的秒）。
        /// 到点由 `PairActionInstance` 在受害者**颈根骨**处 3D 播放（`SoundFx.Play3D`）。
        /// </summary>
        public List<PairTimedEvent> SoundTrack = new List<PairTimedEvent>();

        /// <summary>
        /// **粒子轨道**：`time@粒子名|time@粒子名`。到点挂到受害者**颈根骨**上
        /// （`MBAgentVisuals.CreateParticleSystemAttachedToBone`）。
        /// </summary>
        public List<PairTimedEvent> FxTrack = new List<PairTimedEvent>();

        public bool IsPersistent => AttachMode == PairAttachMode.Persistent;
        public bool StartsByHandoff => string.Equals(Start, "handoff", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// CSV 表的只读视图。按 <see cref="GameDatabase.PairActions"/> 建缓存；
    /// 表换了就重建（与 `CameraCase.TryGet` 同一套路）。
    /// </summary>
    public static class PairActionTable
    {
        private static Dictionary<string, PairActionDef> _cache;
        private static DataTable _cacheSource;
        private static bool _loadedLogged;

        /// <summary>表里有没有这一行（没有 = false；调用方自己决定拒绝并打日志）。</summary>
        public static bool TryGet(string id, out PairActionDef def)
        {
            def = null;
            if (string.IsNullOrWhiteSpace(id))
                return false;

            DataTable table = GameDatabase.PairActions;
            if (table == null)
                return false;

            if (!ReferenceEquals(table, _cacheSource))
            {
                _cache = new Dictionary<string, PairActionDef>(StringComparer.OrdinalIgnoreCase);
                _cacheSource = table;
                _loadedLogged = false;
            }

            if (_cache.TryGetValue(id, out def))
                return def != null;

            DynamicRecord row = table.GetByID(id);
            def = row == null ? null : Build(row);
            _cache[id] = def;
            return def != null;
        }

        /// <summary>
        /// 按**适配器拥有的两台状态机**取行（适配器自己的词汇，不是另一套 id）：
        /// `GrappleCarry` 找 `machine:carry` + `machine:bind` 那一行。
        /// </summary>
        public static bool FindByMachines(string attackerMachine, string defenderMachine, out PairActionDef def)
        {
            def = null;
            if (string.IsNullOrWhiteSpace(attackerMachine) || string.IsNullOrWhiteSpace(defenderMachine))
                return false;

            foreach (PairActionDef d in All())
            {
                if (d.Attacker.Play == PairPlayKind.Machine
                    && d.Defender.Play == PairPlayKind.Machine
                    && string.Equals(d.Attacker.MachineName, attackerMachine, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(d.Defender.MachineName, defenderMachine, StringComparison.OrdinalIgnoreCase))
                {
                    def = d;
                    return true;
                }
            }
            return false;
        }

        /// <summary>表里全部动作（`custom.pair list` 用）。</summary>
        public static List<PairActionDef> All()
        {
            var list = new List<PairActionDef>();
            DataTable table = GameDatabase.PairActions;
            if (table == null)
                return list;

            foreach (DynamicRecord row in table.GetAll())
            {
                if (row == null)
                    continue;
                string id = row.GetString("ID");
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                if (!TryGet(id, out PairActionDef def) || def == null)
                    continue;
                list.Add(def);
            }
            list.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>
        /// 启动时打一遍"装载了几条 + 每条有什么问题"。由 `MySubModule.OnSubModuleLoad`
        /// 在 `GameDatabase.Initialize()` 之后调用（只打一次日志，不注册任何 Harmony）。
        /// </summary>
        public static void LogLoadedOnce()
        {
            if (_loadedLogged)
                return;
            _loadedLogged = true;

            DataTable table = GameDatabase.PairActions;
            if (table == null)
            {
                DebugLogger.Log("[Pair] PairActions.csv 未加载（表为 null）—— 双人动作不可用");
                return;
            }

            List<PairActionDef> all = All();
            DebugLogger.Log($"[Pair] PairActions.csv 装载 {all.Count} 条双人动作");
            var machinePairs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (PairActionDef def in all)
            {
                if (def.Attacker.Play == PairPlayKind.Machine && def.Defender.Play == PairPlayKind.Machine)
                {
                    string key = def.Attacker.MachineName + "/" + def.Defender.MachineName;
                    machinePairs[key] = machinePairs.TryGetValue(key, out int n) ? n + 1 : 1;
                }

                if (Validate(def, out string error))
                {
                    string warn = Warnings(def);
                    DebugLogger.Log($"[Pair]   {def.Id} OK ({Describe(def)})"
                                    + (warn.Length > 0 ? " ⚠ " + warn : ""));
                }
                else
                {
                    DebugLogger.Log($"[Pair]   🔴 {def.Id} 定义有问题：{error}");
                }
            }
            foreach (var kv in machinePairs)
            {
                if (kv.Value > 1)
                    DebugLogger.Log($"[Pair]   ⚠ 状态机组 '{kv.Key}' 出现 {kv.Value} 次（FindByMachines 只取第一条）");
            }
        }

        /// <summary>单条定义自检（`custom.pair verify` 也用它）。</summary>
        public static bool Validate(PairActionDef d, out string error)
        {
            var problems = new List<string>();
            if (d == null)
            {
                error = "def is null";
                return false;
            }
            if (string.IsNullOrWhiteSpace(d.Id))
                problems.Add("ID is empty");

            string start = (d.Start ?? "").ToLowerInvariant();
            if (start != "preposition" && start != "handoff")
                problems.Add($"Start='{d.Start}' (expected preposition/handoff)");

            if (d.AttachMode == PairAttachMode.Persistent && d.Motion != PairMotionKind.None)
                problems.Add("Attach_Mode=persistent requires Motion=none (挂接绝不走引擎位移)");

            if (!string.IsNullOrWhiteSpace(d.HandoffTo))
            {
                DataTable table = GameDatabase.PairActions;
                if (table == null || table.GetByID(d.HandoffTo) == null)
                    problems.Add($"HandoffTo='{d.HandoffTo}' not found in PairActions.csv");
                if (string.IsNullOrWhiteSpace(d.HandoffOn))
                    problems.Add("HandoffTo requires HandoffOn (name the trigger, e.g. put-on-mount)");
            }
            else if (!string.IsNullOrWhiteSpace(d.HandoffOn))
            {
                problems.Add("HandoffOn is set but HandoffTo is empty");
            }

            if (d.StartsByHandoff && string.IsNullOrWhiteSpace(d.EnterEvent))
                problems.Add("Start=handoff requires EnterEvent (交接时由调用方 Force 给 defender)");

            if ((d.Attacker.Play == PairPlayKind.Machine || d.Defender.Play == PairPlayKind.Machine)
                && (string.IsNullOrWhiteSpace(d.EnterEvent) || string.IsNullOrWhiteSpace(d.ExitEvent)))
                problems.Add("machine-driven sides require EnterEvent and ExitEvent");

            ValidateSide(d.Attacker, "Attacker_Play", problems);
            ValidateSide(d.Defender, "Defender_Play", problems);

            if (d.Stance == null)
                problems.Add("Stance is null");

            error = problems.Count == 0 ? null : string.Join("; ", problems);
            return problems.Count == 0;
        }

        private static void ValidateSide(PairSideDef side, string col, List<string> problems)
        {
            if (side == null)
            {
                problems.Add($"{col} is null");
                return;
            }
            if (side.Play == PairPlayKind.Action && string.IsNullOrWhiteSpace(side.ActionId))
                problems.Add($"{col}='action:' but action id is empty");
            if (side.Play == PairPlayKind.Machine && string.IsNullOrWhiteSpace(side.MachineName))
                problems.Add($"{col}='machine:' but machine name is empty");
        }

        /// <summary>结构合法但需要人确认的项（不阻断装载，`verify` 会打出来）。</summary>
        public static string Warnings(PairActionDef d)
        {
            if (d == null)
                return "";
            var warnings = new List<string>();
            if (d.AttachMode == PairAttachMode.Persistent
                && d.Attacker.Play == PairPlayKind.None
                && d.Defender.Play == PairPlayKind.None
                && d.StartsByHandoff)
                warnings.Add("pure attachment pair (no playback) -- animation is driven by an existing state machine; this is intentional only if that machine owns the pose");
            return string.Join("; ", warnings);
        }

        // ───────────────────────────── 行解析 ─────────────────────────────

        private static PairActionDef Build(DynamicRecord row)
        {
            var d = new PairActionDef
            {
                Id = Str(row, "ID"),
                Start = Str(row, "Start", "preposition").Trim().ToLowerInvariant(),
            };

            d.Stance.Right = F(row, "Stance_Right");
            d.Stance.Forward = F(row, "Stance_Forward");
            d.Stance.DirectionDeg = F(row, "Stance_Direction");

            d.Attacker = BuildSide(row, "Attacker_Play");
            d.Defender = BuildSide(row, "Defender_Play");

            d.EnterEvent = Str(row, "EnterEvent");
            d.ExitEvent = Str(row, "ExitEvent");
            d.Motion = ParseMotion(Str(row, "Motion", "none"));
            d.AttachMode = ParseAttach(Str(row, "Attach_Mode", "none"));
            d.EndPolicy = ParseEnd(Str(row, "End_Policy", "release"));
            d.HandoffTo = Str(row, "HandoffTo").Trim();
            d.HandoffOn = Str(row, "HandoffOn").Trim();
            d.SoundTrack = ParseTimedEvents(Str(row, "Sound_Track"));
            d.FxTrack = ParseTimedEvents(Str(row, "Fx_Track"));

            return d;
        }

        /// <summary>`machine:carry` / `action:act_execution02` / 空。</summary>
        private static PairSideDef BuildSide(DynamicRecord row, string key)
        {
            string raw = Str(row, key).Trim();
            if (raw.StartsWith("machine:", StringComparison.OrdinalIgnoreCase))
                return new PairSideDef { Play = PairPlayKind.Machine, MachineName = raw.Substring(8).Trim() };
            if (raw.StartsWith("action:", StringComparison.OrdinalIgnoreCase))
                return new PairSideDef { Play = PairPlayKind.Action, ActionId = raw.Substring(7).Trim() };
            return new PairSideDef { Play = PairPlayKind.None };
        }

        private static string Str(DynamicRecord row, string key, string def = "")
            => row == null ? def : row.GetString(key, def);

        /// <summary>浮点：空 = 默认值；非空 = 表里的值（**0 是显式值**，不是"没填"）。</summary>
        private static float F(DynamicRecord row, string key, float def = 0f)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.GetString(key)))
                return def;
            return row.GetFloat(key, def);
        }

        /// <summary>布尔：空 = 默认值；否则按 True/False 解析（不认 0/1，见 CameraCase 注释）。</summary>
        private static bool B(DynamicRecord row, string key, bool def)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.GetString(key)))
                return def;
            return bool.TryParse(row.GetString(key), out bool v) ? v : def;
        }

        private static PairMotionKind ParseMotion(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "engine": return PairMotionKind.Engine;
                case "clip": return PairMotionKind.Clip;
                default: return PairMotionKind.None;
            }
        }

        private static PairAttachMode ParseAttach(string s)
            => (s ?? "").Trim().ToLowerInvariant() == "persistent"
                ? PairAttachMode.Persistent
                : PairAttachMode.None;

        private static PairEndPolicy ParseEnd(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "kill_defender": return PairEndPolicy.KillDefender;
                case "ragdoll_defender": return PairEndPolicy.RagdollDefender;
                case "knockout": return PairEndPolicy.Knockout;
                case "hold": return PairEndPolicy.Hold;
                default: return PairEndPolicy.Release;
            }
        }

        /// <summary>
        /// 解析 `time@id|time@id`（`@` 避开事件名里的 `:` / `/`，`|` 避开 CSV 逗号）。
        /// 解析失败的单条静默跳过（数据表里留空 = 没有轨道）。
        /// </summary>
        private static List<PairTimedEvent> ParseTimedEvents(string raw)
        {
            var list = new List<PairTimedEvent>();
            if (string.IsNullOrWhiteSpace(raw))
                return list;

            foreach (string part in raw.Split('|'))
            {
                string s = (part ?? "").Trim();
                if (s.Length == 0) continue;

                int at = s.IndexOf('@');
                if (at <= 0 || at >= s.Length - 1) continue;
                if (!float.TryParse(s.Substring(0, at).Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float time))
                    continue;

                string id = s.Substring(at + 1).Trim();
                if (id.Length == 0 || time < 0f) continue;
                list.Add(new PairTimedEvent { Time = time, Id = id });
            }

            list.Sort((a, b) => a.Time.CompareTo(b.Time));
            return list;
        }

        /// <summary>一行摘要（命令 / 日志用）。</summary>
        public static string Describe(PairActionDef d)
        {
            if (d == null) return "(null)";
            var sb = new StringBuilder();
            sb.Append(d.Id)
              .Append(" start=").Append(d.Start)
              .Append(" stance=(").Append(d.Stance.Right.ToString("F3")).Append(',')
              .Append(d.Stance.Forward.ToString("F3")).Append(',')
              .Append(d.Stance.DirectionDeg.ToString("F1")).Append("°)")
              .Append(" motion=").Append(d.Motion)
              .Append(" attach=").Append(d.AttachMode)
              .Append(" end=").Append(d.EndPolicy);
            if (!string.IsNullOrWhiteSpace(d.HandoffTo))
                sb.Append(" handoffOn=").Append(d.HandoffOn).Append(" -> ").Append(d.HandoffTo);
            return sb.ToString();
        }
    }
}
