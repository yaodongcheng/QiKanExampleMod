using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>单人朝向探针：按需监听真实Agent入口，含原版调用；只观察，不改变参数、返回值或控制权。</summary>
    public static class AgentFacingTrace
    {
        private const string HarmonyId = "com.ydc.LivingWorldNpcs.FacingTrace";
        private static readonly Harmony _harmony = new Harmony(HarmonyId);
        private static readonly List<MethodInfo> _hooks = new List<MethodInfo>();
        private static readonly List<CallMark> _calls = new List<CallMark>();
        private static Agent _target;
        private static string _targetLabel;
        private static float _flushTime;
        private static float _sampleTime;
        private static float _lastYaw;
        private static float _startYaw;
        private static float _angularTravel;
        private static int _reversals;
        private static int _turnSign;
        private static bool _installed;
        private static string _unavailable = "none";
        private static Vec3 _lastPosition;
        private static int _overflow;
        private static int _writesSinceSample;
        private static long _sequence;

        private sealed class CallMark
        {
            public string Api, Source, First, Last;
            public int Count;
            public long FirstSequence, LastSequence;
        }

        public static string Describe() => _target == null ? "off" : _targetLabel + " hooks=" + _hooks.Count + " unavailable=" + _unavailable
            + " vanillaPoi=" + VanillaLookSuspendPatch.Describe(_target);

        public static string Enable(Agent target)
        {
            if (!AgentControlHelper.SafeIsActive(target)) return "Error: an active NPC in this mission is required; screen trace was not changed.";
            return Enable(Identity(target), target);
        }

        /// <summary>通用入口：复用custom.look命令族，按焦点／Agent编号／英文角色ID选择一个实例；控制台输入输出仅ASCII。</summary>
        public static string HandleCommand(IList<string> args)
        {
            try
            {
                if (args != null && args.Any(arg => arg != null && arg.Any(ch => ch > 127)))
                    return "Error: ASCII arguments only. Use aimed, #<index>, or id <characterId>. Selection unchanged.";
                string selector = args != null && args.Count > 0 ? args[0].Trim() : "aimed";
                string kind = selector.ToLowerInvariant();
                if (kind == "off")
                {
                    Stop();
                    return "OK: facing trace off; capture and sampling stopped. No NPC state changed.";
                }
                if (kind == "status") return "OK: facing trace = " + Describe();
                if (kind == "help") return "Usage: custom.look trace aimed | custom.look trace #<index> | custom.look trace id <characterId> | custom.look trace list [idFilter] | custom.look trace status | custom.look trace off";
                if (kind == "name") return "Error: name selection is not supported. Use custom.look trace aimed or custom.look trace #<index>. Selection unchanged.";
                if (Mission.Current == null) return "Error: enter a mission first. Existing trace selection unchanged.";
                var agents = Mission.Current.Agents.Where(a => a != Agent.Main && AgentControlHelper.SafeIsActive(a)).ToList();
                if (kind == "list")
                {
                    string filter = args.Count > 1 ? string.Join(" ", args.Skip(1)) : "";
                    var matches = agents.Where(a => Identity(a).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(a => a.Index).ToList();
                    return "OK: facing trace = " + Describe() + "; active NPC matches=" + matches.Count
                        + "; listed=" + Math.Min(40, matches.Count) + "\n"
                        + string.Join("\n", matches.Take(40).Select(Identity))
                        + (matches.Count > 40 ? "\nRefine the id filter to see remaining agents." : "");
                }
                if (kind == "id")
                {
                    string query = args.Count > 1 ? string.Join(" ", args.Skip(1)).Trim() : "";
                    if (query.Length == 0) return "Usage: custom.look trace id <characterId>. Selection unchanged.";
                    var matches = agents.Where(a => string.Equals(a.Character?.StringId, query, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (matches.Count == 0) return "Error: no active NPC matches " + kind + "='" + query + "'. Selection unchanged.";
                    if (matches.Count > 1) return "Error: ambiguous target; selection unchanged. Choose one with custom.look trace #<index>:\n"
                        + string.Join("\n", matches.Take(40).Select(Identity));
                    return Enable(matches[0]);
                }
                string indexed = selector;
                bool bareIndex = int.TryParse(selector, out int index);
                if (bareIndex) indexed = "#" + index;
                if (indexed.Contains("#"))
                {
                    // 复用既有编号解析器，重名／同模板NPC也按Agent实例精确选择。
                    if (AgentControlHelper.TryResolveIndexedTarget(indexed, out Agent selected, out _)) return Enable(selected);
                    if (!bareIndex) return "Error: no active agent for " + indexed + ". Selection unchanged; use custom.look trace list.";
                }
                // 首参占位容忍：未识别的占位回落到焦点，并明确回显；不回落到玩家。
                string note = kind == "aimed" ? "" : " [note: '" + selector + "' is a placeholder -> using aimed NPC]";
                Agent aimed = InteractionMissionView.Instance?.GetAimedAgent();
                if (!agents.Contains(aimed)) return "Error: no active NPC under the interaction focus. Selection unchanged; use custom.look trace id <characterId> or custom.look trace list." + note;
                return Enable(aimed) + note;
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[FacingApi] command failed: " + ex);
                return "Error: facing trace command failed. See [FacingApi] log. Selection may have changed.";
            }
        }

        // 外部模组的ID／调用方标签也可能含中文，控制台只显示ASCII转义，不读取本地化姓名。
        private static string ConsoleAscii(string value)
        {
            if (value == null || value.All(ch => ch <= 127)) return value;
            var text = new StringBuilder();
            foreach (char ch in value)
                if (ch <= 127) text.Append(ch);
                else text.Append("\\u").Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        private static string Identity(Agent agent) => "agent#" + agent.Index + " character=" + ConsoleAscii(agent.Character?.StringId ?? "none");

        public static string Enable(string targetLabel, Agent target)
        {
            if (!AgentControlHelper.SafeIsActive(target) || target == Agent.Main)
                return "Error: an active NPC in this mission is required; screen trace was not changed.";
            if (!Install()) return "Error: facing trace hooks could not be installed; see [FacingApi] log.";
            Stop();
            _target = target;
            _targetLabel = ConsoleAscii(targetLabel);
            _lastYaw = BodyYaw(target);
            _startYaw = _lastYaw;
            _lastPosition = target.Position;
            DebugLogger.Log("[FacingApi] enabled target=" + targetLabel + " hooks=" + string.Join(",", _hooks.Select(m => m.Name)));
            return "OK: screen facing trace on for " + _targetLabel + "; " + Describe()
                + ". One agent instance selected; previous screen target replaced. Values/caller/count and body samples are shown; no movement or AI changes. Stop: custom.look trace off";
        }

        /// <summary>启动期挂入口以防JIT先内联setter；关闭时仅比较人物引用，不构造args数组或调用栈。</summary>
        public static bool Install()
        {
            if (_installed) return true;
            if (_hooks.Count > 0) return false; // 上次撤补丁失败时不能假报已安装。
            try
            {
                // 按实际存在的方法挂载，避免版本签名不同或缺目标掐断补丁安装。
                var names = new HashSet<string>(StringComparer.Ordinal) {
                    "SetLookAgent", "SetMovementDirection", "SetScriptedPositionAndDirection",
                    "SetScriptedPosition", "DisableScriptedMovement", "DisableScriptedCombatMovement",
                    "SetTargetPositionAndDirection", "SetTargetPosition", "ClearTargetFrame",
                    "set_LookDirection", "set_LookDirectionAsAngle", "set_IsLookDirectionLocked",
                    "SetLookToPointOfInterest", "DisableLookToPointOfInterest", "SetScriptedFlags", "SetDirectionChangeTendency",
                    "SetTargetAgent", "SetScriptedTargetEntityAndPosition"
                };
                var methods = typeof(Agent).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Where(m => names.Contains(m.Name) && m.GetMethodBody() != null).ToList();
                if (!methods.Any(m => m.Name == "SetLookAgent") || !methods.Any(m => m.Name == "SetMovementDirection"))
                    throw new MissingMethodException("Required Agent facing methods are missing.");
                var postfix = new HarmonyMethod(typeof(AgentFacingTrace).GetMethod(nameof(AfterCall), BindingFlags.NonPublic | BindingFlags.Static));
                foreach (var method in methods)
                {
                    string prefixName = PrefixName(method);
                    var prefix = new HarmonyMethod(typeof(AgentFacingTrace).GetMethod(prefixName, BindingFlags.NonPublic | BindingFlags.Static));
                    _hooks.Add(method);
                    _harmony.Patch(method, prefix: prefix, postfix: postfix);
                }
                _unavailable = string.Join(",", names.Except(methods.Select(m => m.Name)));
                if (string.IsNullOrEmpty(_unavailable)) _unavailable = "none";
                DebugLogger.Log("[FacingApi] passive hooks installed=" + _hooks.Count + " unavailable=" + _unavailable);
                _installed = true;
                return true;
            }
            catch (Exception ex)
            {
                Stop();
                foreach (var method in _hooks.ToArray())
                {
                    try { _harmony.Unpatch(method, HarmonyPatchType.All, HarmonyId); _hooks.Remove(method); }
                    catch (Exception rollback) { DebugLogger.Log("[FacingApi] rollback failed: " + rollback); }
                }
                DebugLogger.Log("[FacingApi] install failed: " + ex);
                return false;
            }
        }

        public static void Stop()
        {
            // 撤目标后所有入口短路；保留启动期跳板，避免下一次打开时漏掉已内联的设置。
            _target = null;
            _calls.Clear();
            _targetLabel = null;
            _flushTime = _sampleTime = 0f;
            _overflow = _writesSinceSample = 0;
            _sequence = 0;
            _angularTravel = 0f;
            _reversals = _turnSign = 0;
        }

        private static string PrefixName(MethodInfo method)
        {
            if (method.Name == "SetScriptedPositionAndDirection") return nameof(BeforeScriptedDirection);
            if (method.Name == "SetScriptedPosition") return nameof(BeforeScriptedPosition);
            if (method.Name == "SetTargetPositionAndDirection") return nameof(BeforeTargetFrame);
            if (method.Name == "SetScriptedTargetEntityAndPosition") return nameof(BeforeTargetEntity);
            var parameters = method.GetParameters();
            if (parameters.Length == 0) return nameof(BeforeClear);
            var type = parameters[0].ParameterType;
            if (type.IsByRef) type = type.GetElementType();
            if (type == typeof(Agent)) return nameof(BeforeAgent);
            if (type == typeof(Vec2)) return nameof(BeforeVec2);
            if (type == typeof(Vec3)) return nameof(BeforeVec3);
            if (type == typeof(float)) return nameof(BeforeFloat);
            if (type == typeof(bool)) return nameof(BeforeBool);
            if (type == typeof(Agent.AIScriptedFrameFlags)) return nameof(BeforeFlags);
            throw new NotSupportedException("Unknown facing signature: " + method);
        }

        private static bool Watches(Agent agent) => _target != null && ReferenceEquals(agent, _target);

        // 按值读取原参数，既支持1.2.12的值参数，也支持新版本的in参数；从不传ref回写。
        private static void BeforeAgent(Agent __instance, Agent __0, MethodBase __originalMethod, out CallMark __state)
            => __state = Watches(__instance) ? Capture(__instance, __originalMethod, "target=" + Value(__0), __0 == null) : null;
        private static void BeforeVec2(Agent __instance, Vec2 __0, MethodBase __originalMethod, out CallMark __state)
            => __state = Watches(__instance) ? Capture(__instance, __originalMethod, "value=" + Value(__0) + " yaw=" + Yaw(__0).ToString("F1", CultureInfo.InvariantCulture) + "deg") : null;
        private static void BeforeVec3(Agent __instance, Vec3 __0, MethodBase __originalMethod, out CallMark __state)
            => __state = Watches(__instance) ? Capture(__instance, __originalMethod, "value=" + Value(__0)) : null;
        private static void BeforeFloat(Agent __instance, float __0, MethodBase __originalMethod, out CallMark __state)
            => __state = Watches(__instance) ? Capture(__instance, __originalMethod, "value=" + Value(__0)
                + (__originalMethod.Name == "set_LookDirectionAsAngle" ? "rad / " + (__0 * 180f / (float)Math.PI).ToString("F1", CultureInfo.InvariantCulture) + "deg" : "")) : null;
        private static void BeforeBool(Agent __instance, bool __0, MethodBase __originalMethod, out CallMark __state)
            => __state = Watches(__instance) ? Capture(__instance, __originalMethod, "value=" + __0) : null;
        private static void BeforeFlags(Agent __instance, Agent.AIScriptedFrameFlags __0, MethodBase __originalMethod, out CallMark __state)
            => __state = Watches(__instance) ? Capture(__instance, __originalMethod, "flags=" + __0) : null;
        private static void BeforeClear(Agent __instance, MethodBase __originalMethod, out CallMark __state)
            => __state = Watches(__instance) ? Capture(__instance, __originalMethod, "clear request", true) : null;
        private static void BeforeScriptedDirection(Agent __instance, WorldPosition __0, float __1, bool __2, Agent.AIScriptedFrameFlags __3, MethodBase __originalMethod, out CallMark __state)
            => __state = Watches(__instance) ? Capture(__instance, __originalMethod, FormattableString.Invariant($"position={Value(__0)} direction={__1:F4}rad/{__1 * 180f / (float)Math.PI:F1}deg delay={__2} flags={__3}")) : null;
        private static void BeforeScriptedPosition(Agent __instance, WorldPosition __0, bool __1, Agent.AIScriptedFrameFlags __2, MethodBase __originalMethod, out CallMark __state)
            => __state = Watches(__instance) ? Capture(__instance, __originalMethod, $"position={Value(__0)} delay={__1} flags={__2}") : null;
        private static void BeforeTargetFrame(Agent __instance, Vec2 __0, Vec3 __1, MethodBase __originalMethod, out CallMark __state)
            => __state = Watches(__instance) ? Capture(__instance, __originalMethod, "position=" + Value(__0) + " direction=" + Value(__1)) : null;
        private static void BeforeTargetEntity(Agent __instance, GameEntity __0, WorldPosition __1, Agent.AISpecialCombatModeFlags __2, bool __3, MethodBase __originalMethod, out CallMark __state)
            => __state = Watches(__instance) ? Capture(__instance, __originalMethod, $"entity={(__0 == null ? "none" : "present")} position={Value(__1)} flags={__2} ignoreIfAttacking={__3}") : null;

        private static CallMark Capture(Agent agent, MethodBase method, string value, bool clear = false)
        {
            BeforeCall(agent, out string source);
            return source == null ? null : new CallMark { Api = method.Name + (clear ? "/clear" : "/set"), Source = source, First = value };
        }

        private static void BeforeCall(Agent __instance, out string __state)
        {
            __state = null;
            if (!ReferenceEquals(__instance, _target) || _target == null) return;
            try
            {
                // 只对选中的人物取托管调用链；三层可同时看到Helper与真正发起的行为。
                __state = string.Join(" <- ", new StackTrace(2, true).GetFrames()
                    .Select(f => new { Frame = f, Method = f.GetMethod() })
                    .Where(x => x.Method?.DeclaringType != null && x.Method.DeclaringType != typeof(AgentFacingTrace)
                        && x.Method.DeclaringType != typeof(Agent)
                        && !x.Method.DeclaringType.FullName.StartsWith("HarmonyLib.", StringComparison.Ordinal)
                        && !x.Method.DeclaringType.FullName.StartsWith("System.", StringComparison.Ordinal))
                    .Take(3).Select(x => x.Method.DeclaringType.Name + "." + x.Method.Name
                        + (x.Frame.GetFileLineNumber() > 0 ? ":" + x.Frame.GetFileLineNumber() : "")));
                if (string.IsNullOrEmpty(__state)) __state = "managed caller unavailable";
            }
            catch (Exception ex) { __state = "caller unavailable: " + ex.GetType().Name; }
        }

        private static void AfterCall(Agent __instance, CallMark __state)
        {
            if (__state == null || !ReferenceEquals(__instance, _target) || _target == null) return;
            try
            {
                // 设置/取消分桶，快速往返不能被相同API的汇总吞掉；值变化保留首末值与调用序号。
                long sequence = ++_sequence;
                _writesSinceSample++;
                var mark = _calls.FirstOrDefault(m => m.Api == __state.Api && m.Source == __state.Source);
                if (mark == null)
                {
                    if (_calls.Count >= 64) { _overflow++; return; }
                    mark = __state;
                    mark.FirstSequence = sequence;
                    _calls.Add(mark);
                }
                mark.Last = __state.First;
                mark.LastSequence = sequence;
                mark.Count++;
            }
            catch (Exception ex) { DebugLogger.Log("[FacingApi] observe failed: " + ex.Message); }
        }

        private static string Value(object value)
        {
            try
            {
                if (value == null) return "none";
                if (value is Agent agent) return agent == Agent.Main ? "player#" + agent.Index : Identity(agent);
                if (value is Vec2 v2) return FormattableString.Invariant($"({v2.x:F3},{v2.y:F3})");
                if (value is Vec3 v3) return FormattableString.Invariant($"({v3.x:F3},{v3.y:F3},{v3.z:F3})");
                if (value is WorldPosition position) return "xy=" + Value(position.AsVec2);
                if (value is float angle) return angle.ToString("F4", CultureInfo.InvariantCulture);
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) { return "unavailable:" + ex.GetType().Name; }
        }

        private static float Yaw(Vec2 direction) => (float)(Math.Atan2(direction.y, direction.x) * 180d / Math.PI);
        private static float BodyYaw(Agent agent) => Yaw(agent.Frame.rotation.f.AsVec2);

        /// <summary>主线程延迟上屏，避免在引擎设置调用栈里发消息；同来源每秒汇总。</summary>
        public static void Tick(float dt)
        {
            if (_target == null) return;
            if (!AgentControlHelper.SafeIsActive(_target)) { Stop(); return; }
            try
            {
                _flushTime += Math.Max(0f, dt);
                _sampleTime += Math.Max(0f, dt);
                float currentYaw = BodyYaw(_target);
                float step = (currentYaw - _lastYaw + 540f) % 360f - 180f;
                _angularTravel += Math.Abs(step);
                if (Math.Abs(step) >= 0.2f)
                {
                    int sign = Math.Sign(step);
                    if (_turnSign != 0 && _turnSign != sign) _reversals++;
                    _turnSign = sign;
                }
                _lastYaw = currentYaw;
                if (_flushTime >= 1f)
                {
                    _flushTime = 0f;
                    int shown = 0;
                    foreach (var mark in _calls)
                    {
                        string values = mark.First == mark.Last ? mark.Last : mark.First + " -> " + mark.Last;
                        string detail = mark.Api + " " + values + " calls=" + mark.Count + " seq=" + mark.FirstSequence + ".." + mark.LastSequence + " from=" + mark.Source;
                        DebugLogger.Log("[FacingApi] target=" + _targetLabel + " " + detail);
                        if (shown++ < 6) Display("LWN_facing_trace_call", "[Facing trace] {NPC}: {DETAIL}", detail);
                    }
                    int hidden = Math.Max(0, _calls.Count - 6);
                    if (hidden > 0 || _overflow > 0)
                        Display("LWN_facing_trace_overflow", "[Facing trace] {NPC}: {DETAIL}",
                            "screenOmitted=" + hidden + " (see [FacingApi] log); bufferDropped=" + _overflow);
                    _calls.Clear();
                    _overflow = 0;
                }
                if (_sampleTime >= 2f)
                {
                    float delta = (currentYaw - _startYaw + 540f) % 360f - 180f;
                    string detail = FormattableString.Invariant($"bodyYaw={currentYaw:F1}deg delta={delta:F1}deg travel={_angularTravel:F1}deg reversals={_reversals} move={_target.Position.Distance(_lastPosition):F3}m gaze={Value(_target.GetLookAgent())} movement={Value(_target.GetMovementDirection())} target={Value(_target.GetTargetPosition())} flags={_target.GetScriptedFlags()} ctrl={_target.Controller} action={AgentAIController.GetBrainForAgent(_target)?.CurrentAction?.GetType().Name ?? "none"} managedCalls={_writesSinceSample} vanillaPoi={VanillaLookSuspendPatch.Describe(_target)}");
                    detail += " bodyRequest=" + NpcPerception.DescribeBodyRequest(_target)
                        + " headPointTarget=" + NpcPerception.DescribeHeadTarget(_target);
                    DebugLogger.Log("[FacingSample] target=" + _targetLabel + " " + detail);
                    DebugLogger.Log("[FacingPerception] target=" + _targetLabel + " " + NpcPerception.Describe(_target));
                    Display("LWN_facing_trace_sample", "[Facing sample] {NPC}: {DETAIL}", detail);
                    _startYaw = currentYaw;
                    _angularTravel = 0f;
                    _reversals = _turnSign = 0;
                    _lastPosition = _target.Position;
                    _sampleTime = 0f;
                    _writesSinceSample = 0;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[FacingApi] tick failed; stopped: " + ex);
                Stop();
            }
        }

        private static void Display(string key, string fallback, string detail)
            => InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound(key, fallback,
                ("NPC", _targetLabel), ("DETAIL", detail))));
    }
}
