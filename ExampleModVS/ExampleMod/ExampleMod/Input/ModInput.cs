using System;
using System.Collections.Generic;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 玩法交互 ID（config.json 键名 = 玩法行）。
    /// 一个玩法行 = 一个玩法交互的 (键盘键, 手柄键, 按法) 配置 + 独立短/长按状态机。
    /// 同一物理键可挂多个玩法行（如 F 挂 Talk/Loot/Knockout/...），同一次按下各按各自阈值触发。
    /// </summary>
    public static class InteractionIds
    {
        public const string Talk = "Talk";
        public const string Loot = "Loot";
        public const string Knockout = "Knockout";
        public const string Pickpocket = "Pickpocket";
        public const string StealAnimal = "StealAnimal";
        public const string Lockpick = "Lockpick";
        public const string PlayerSurrender = "PlayerSurrender";
        public const string AcceptSurrender = "AcceptSurrender";
        public const string Inspect = "Inspect";
        public const string StealAttempt = "StealAttempt";
        public const string StealLeave = "StealLeave";
        public const string Plot = "Plot";          // 密谋：对随从下达自然语言命令（G 长按）
        public const string StopPlan = "StopPlan";  // 停止键：对执行中的随从喊停（G 长按，与 Plot 同键；互斥不同时显示）
        public const string IM = "IM";              // 传讯：打开/关闭 IM 聊天面板（键盘 O；手柄不占键，走通知点击）
    }

    /// <summary>
    /// 按法（配置维度，玩法行独立配置）：
    /// Short = 短按，快速按下松开即触发（按住超过阈值则取消——转入同键其他玩法行的长按路径）；
    /// Long  = 长按（KCD 语义），按住蓄力跨越阈值进入待命（UI 进度框满），
    ///         玩家选择时机松开才执行；阈值前松开不触发。
    /// </summary>
    public enum ModInputPressMode { Short, Long }

    /// <summary>
    /// 语义动作枚举（保留作历史语义分组参考；实际键位/按法由玩法行配置驱动，
    /// 业务侧只认 <see cref="InteractionIds"/> 玩法 ID，不再消费本枚举）。
    /// </summary>
    public enum ModInputAction
    {
        Interact,       // 主互动：对话 / 偷窃 / 击晕 / 搜刮 / 撬锁
        AltInteract,    // 副互动：闲聊 / 接受认输
        Inspect,        // 探查：NPC 信息板
        StealAttempt,   // 偷窃条：出手
        StealLeave,     // 偷窃条：收手
    }

    /// <summary>一条玩法交互的解析后绑定（UI 与输入共享同一份配置）。</summary>
    public sealed class InteractionBinding
    {
        public string InteractionId;
        public InputKey Keyboard;
        public InputKey Gamepad;
        public ModInputPressMode PressMode;
        public float ThresholdMs;    // 长按阈值（玩法级 HoldMs 覆盖全局 LongPressDurationMs）
        public string KbGlyph;       // 键盘提示（Space/Tab 走本地化键，其余按键名）
        public string XboxGlyph;     // Xbox 手柄提示（"Y"/"LB"/"R3"…）
        public string PsGlyph;       // PlayStation 手柄提示（"△"/"L1"/"R3"…）
    }

    /// <summary>
    /// 输入统一归口（玩法行模型，UE4 Action Mapping 升级版）。
    ///
    /// ① 键位/按法来自 config.json（<see cref="Settings.Interactions"/>），<see cref="RebuildBindings"/> 解析；
    ///    失败回落内置默认 + 日志警告（铁律 2 风格防御）。
    /// ② 短/长按状态机按玩法行独立跟踪（<see cref="Tick"/> 每帧驱动）：同一物理键按下时
    ///    挂该键的全部玩法行同时进入计时，各按各自阈值与按法触发（短/长互斥，不双重触发）。
    ///    Long = KCD 语义：跨阈值进入待命（进度框满），松开才触发——执行时机由玩家掌控。
    ///    触发标志为帧窗口一次性：Tick 每帧先清上一帧未消费标志 → 消费即清，
    ///    模态覆盖（对话/剧情）期间未消费的触发自然过期，杜绝陈旧触发。
    ///    另保留按下沿通道 <see cref="PressedFired"/>（节奏玩法专用：偷窃条出手/收手，
    ///    松开触发晚一次点按会发粘；配置层仍只有 Short/Long）。
    /// ③ 提示字形按"最近一次输入设备"（引擎原生追踪）返回键盘 / Xbox / PS 三套文本；
    ///    手柄配置写逻辑键（Xbox 名与 PS 名等价解析到同一引擎键），显示按当前手柄自动切换。
    /// </summary>
    public static class ModInput
    {
        /// <summary>一个玩法行的状态机：物理键按下跟踪 + 按下/短/长按触发标志。</summary>
        private sealed class RowState
        {
            public bool Tracking;          // 物理键当前按下中
            public float HoldTime;         // 已按住时长（秒，缩放 dt）
            public bool LongReady;         // 长按已满（跨阈值进入待命，等待玩家松手执行；KCD 语义）
            public bool PressedFired;      // 本帧按下沿触发（帧窗口一次性，消费即清；节奏玩法专用，如偷窃条）
            public bool ShortFired;        // 本帧短按触发（帧窗口一次性，消费即清）
            public bool LongFired;         // 本帧长按触发（帧窗口一次性，消费即清）
        }

        private static readonly Dictionary<string, InteractionBinding> _bindings = new Dictionary<string, InteractionBinding>();
        private static readonly Dictionary<string, RowState> _states = new Dictionary<string, RowState>();

        // ── 手柄逻辑键别名表（大小写不敏感）：Xbox 名与 PS 名等价解析，归一到同一引擎键 ──
        private static readonly Dictionary<string, InputKey> _gamepadAliases = new Dictionary<string, InputKey>(StringComparer.OrdinalIgnoreCase)
        {
            ["Y"] = InputKey.ControllerRUp, ["Triangle"] = InputKey.ControllerRUp,
            ["A"] = InputKey.ControllerRDown, ["Cross"] = InputKey.ControllerRDown,
            ["X"] = InputKey.ControllerRLeft, ["Square"] = InputKey.ControllerRLeft,
            ["B"] = InputKey.ControllerRRight, ["Circle"] = InputKey.ControllerRRight,
            ["LB"] = InputKey.ControllerLBumper, ["L1"] = InputKey.ControllerLBumper,
            ["RB"] = InputKey.ControllerRBumper, ["R1"] = InputKey.ControllerRBumper,
            ["LT"] = InputKey.ControllerLTrigger, ["L2"] = InputKey.ControllerLTrigger,
            ["RT"] = InputKey.ControllerRTrigger, ["R2"] = InputKey.ControllerRTrigger,
            ["L3"] = InputKey.ControllerLThumb,
            ["R3"] = InputKey.ControllerRThumb,
            ["DUp"] = InputKey.ControllerLUp, ["DDown"] = InputKey.ControllerLDown,
            ["DLeft"] = InputKey.ControllerLLeft, ["DRight"] = InputKey.ControllerLRight,
            ["View"] = InputKey.ControllerLOption, ["Touchpad"] = InputKey.ControllerLOption,
            ["Menu"] = InputKey.ControllerROption, ["Options"] = InputKey.ControllerROption,
        };

        // ── 引擎键 → 显示字形（Xbox / PS 两套；显示 = 别名表反查此表）──
        private static readonly Dictionary<InputKey, (string xbox, string ps)> _engineDisplay = new Dictionary<InputKey, (string, string)>
        {
            [InputKey.ControllerRUp] = ("Y", "△"),
            [InputKey.ControllerRDown] = ("A", "✕"),
            [InputKey.ControllerRLeft] = ("X", "□"),
            [InputKey.ControllerRRight] = ("B", "○"),
            [InputKey.ControllerLBumper] = ("LB", "L1"),
            [InputKey.ControllerRBumper] = ("RB", "R1"),
            [InputKey.ControllerLTrigger] = ("LT", "L2"),
            [InputKey.ControllerRTrigger] = ("RT", "R2"),
            [InputKey.ControllerLThumb] = ("L3", "L3"),
            [InputKey.ControllerRThumb] = ("R3", "R3"),
            [InputKey.ControllerLUp] = ("↑", "↑"),
            [InputKey.ControllerLDown] = ("↓", "↓"),
            [InputKey.ControllerLLeft] = ("←", "←"),
            [InputKey.ControllerLRight] = ("→", "→"),
            [InputKey.ControllerLOption] = ("View", "Touchpad"),
            [InputKey.ControllerROption] = ("Menu", "Options"),
        };

        static ModInput()
        {
            // 懒加载：首次访问即从 Settings（config.json）构建；Settings.Reload() 后由控制台指令重建
            RebuildBindings();
        }

        /// <summary>玩家最近一次输入是手柄（引擎原生追踪：手柄已连接且鼠标未活动）。</summary>
        public static bool UsingGamepad => Input.IsGamepadActive && Input.IsControllerConnected;

        /// <summary>当前手柄是 PlayStation 系（DualShock/DualSense）。</summary>
        public static bool IsPlayStation => Input.ControllerType.IsPlaystation();

        // ═══════════════════════ 绑定构建（config.json → 玩法行） ═══════════════════════

        /// <summary>
        /// 从 Settings（config.json）重建全部玩法行绑定（热重载入口：控制台 custom.input_reload）。
        /// 重建后所有按住状态清空（安全：配置变更视为新的输入会话）。
        /// </summary>
        public static void RebuildBindings()
        {
            _bindings.Clear();
            _states.Clear();

            var settings = Settings.Instance;
            var merged = new Dictionary<string, InteractionBindingConfig>(Settings.DefaultInteractions);
            if (settings.Interactions != null)
            {
                foreach (var kvp in settings.Interactions)
                    if (kvp.Value != null)
                        merged[kvp.Key] = kvp.Value;   // 玩家覆盖内置默认 / 新增玩法行
            }

            foreach (var kvp in merged)
            {
                Settings.DefaultInteractions.TryGetValue(kvp.Key, out var def);
                _bindings[kvp.Key] = ResolveBinding(kvp.Key, kvp.Value, def);
            }
        }

        /// <summary>解析一行配置 → 绑定。空值 = 用内置默认（config 文档约定）；非法值 = 回落默认 + 警告。</summary>
        private static InteractionBinding ResolveBinding(string id, InteractionBindingConfig cfg, InteractionBindingConfig def)
        {
            var binding = new InteractionBinding { InteractionId = id };

            // ── 键盘：InputKey 枚举名（"F"/"Q"/"Space"/"Tab"…）──
            var kb = ParseKeyboard(cfg?.Keyboard);
            if (!kb.ok)
            {
                if (!string.IsNullOrWhiteSpace(cfg?.Keyboard))
                    LogBindingFallback(id, "Keyboard", cfg.Keyboard);
                kb = ParseKeyboard(def?.Keyboard);
                if (!kb.ok) kb = (true, InputKey.F, "F");
            }
            binding.Keyboard = kb.key;
            binding.KbGlyph = kb.glyph;

            // ── 手柄：逻辑键别名表（Y/Triangle/…）→ 引擎枚举名兜底（ControllerRUp/…）──
            var gp = ParseGamepad(cfg?.Gamepad);
            if (!gp.ok)
            {
                if (!string.IsNullOrWhiteSpace(cfg?.Gamepad))
                    LogBindingFallback(id, "Gamepad", cfg.Gamepad);
                gp = ParseGamepad(def?.Gamepad);
                if (!gp.ok) gp = (true, InputKey.ControllerRUp, "Y", "△");
            }
            binding.Gamepad = gp.key;
            binding.XboxGlyph = gp.xbox;
            binding.PsGlyph = gp.ps;

            // ── 按法：Short / Long（空 = 内置默认；非法 → 回落默认 + 警告）──
            var pm = ParsePressMode(cfg?.PressMode);
            if (!pm.ok)
            {
                if (!string.IsNullOrWhiteSpace(cfg?.PressMode))
                    LogBindingFallback(id, "PressMode", cfg.PressMode);
                pm = ParsePressMode(def?.PressMode);
                if (!pm.ok) pm = (true, ModInputPressMode.Short);
            }
            binding.PressMode = pm.mode;

            // ── 阈值：玩法级 HoldMs（>0）覆盖全局 LongPressDurationMs（下限 50ms 防御除零/瞬发）──
            binding.ThresholdMs = (cfg?.HoldMs ?? 0) > 0
                ? Math.Max(50, cfg.HoldMs)
                : Math.Max(50, Settings.Instance.LongPressDurationMs);

            return binding;
        }

        private static (bool ok, InputKey key, string glyph) ParseKeyboard(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return (false, InputKey.F, "F");
            if (Enum.TryParse(value.Trim(), true, out InputKey key))
                return (true, key, DeriveKbGlyph(key));
            return (false, InputKey.F, "F");
        }

        private static (bool ok, InputKey key, string xbox, string ps) ParseGamepad(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return (false, InputKey.ControllerRUp, "Y", "△");
            string v = value.Trim();
            if (_gamepadAliases.TryGetValue(v, out InputKey aliasKey))
                return GlyphsFor(aliasKey);
            // 兜底：引擎枚举名（ControllerRUp 等）
            if (Enum.TryParse(v, true, out InputKey enumKey) && _engineDisplay.ContainsKey(enumKey))
                return GlyphsFor(enumKey);
            return (false, InputKey.ControllerRUp, "Y", "△");
        }

        private static (bool ok, ModInputPressMode mode) ParsePressMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return (false, ModInputPressMode.Short);
            if (Enum.TryParse(value.Trim(), true, out ModInputPressMode mode)) return (true, mode);
            return (false, ModInputPressMode.Short);
        }

        private static (bool ok, InputKey key, string xbox, string ps) GlyphsFor(InputKey key)
        {
            if (_engineDisplay.TryGetValue(key, out var g))
                return (true, key, g.xbox, g.ps);
            // 引擎名兜底显示（别名表未覆盖的键）
            return (true, key, key.ToString(), key.ToString());
        }

        /// <summary>键盘提示字形：单词键走本地化（Space/Tab 既有先例，铁律 13），单字母/数字用按键名。</summary>
        private static string DeriveKbGlyph(InputKey key)
        {
            switch (key)
            {
                case InputKey.Space:
                    return LWNTextHelper.ResolveText("LWN_input_key_space", "Space");
                case InputKey.Tab:
                    return LWNTextHelper.ResolveText("LWN_input_key_tab", "Tab");
                default:
                    return key.ToString();
            }
        }

        private static void LogBindingFallback(string id, string field, string badValue)
        {
            DebugLogger.Log($"[ModInput] 配置警告: {id}.{field} = \"{badValue}\" 无法解析，回落内置默认");
        }

        // ═══════════════════════ 短/长按状态机（按玩法行跟踪） ═══════════════════════

        /// <summary>
        /// 每帧驱动全部玩法行状态机（MissionTick 调用）。
        /// 帧窗口：先清上一帧未消费的触发标志 → 再处理本帧输入 → 消费点（HandleInput/TickStealBar）
        /// 在同帧内读取；未消费的标志下一帧自动过期（模态覆盖期间的按压不会陈旧触发）。
        /// 🔴 模态门控：Input.IsKeyDown 是物理键轮询，Gauntlet 层 InputRestrictions 拦不住——
        /// ① IM 聊天面板打开（ImChatView.IsOpen：打字/点卡片/翻历史/点按钮）整体暂停——密令已 IM 化，
        ///    面板打开就是旧 PlanCommandFlow 弹窗的模态等价（2026-08-11：旧门控没跟着迁过来，实机
        ///    面板打字时 F 探查等玩法行仍触发）；② 当面对话密谋流程激活（PlanCommandFlow.IsActive）
        ///    同样暂停；③ 系统弹窗（TopScreen 含 Inquiry）同样暂停。
        /// </summary>
        public static void Tick(float dt)
        {
            if (IsSystemModalActive() || PlanCommandFlow.IsActive || ImChatView.IsOpen)
            {
                ResetAll();   // 清空按住/触发状态，弹窗期间松开也不会陈旧触发
                return;
            }

            foreach (RowState st in _states.Values)
            {
                st.PressedFired = false;
                st.ShortFired = false;
                st.LongFired = false;
            }

            if (dt < 0f) dt = 0f;

            foreach (var kvp in _bindings)
            {
                InteractionBinding b = kvp.Value;
                if (!_states.TryGetValue(kvp.Key, out RowState st))
                {
                    st = new RowState();
                    _states[kvp.Key] = st;
                }

                bool keyDown = Input.IsKeyDown(b.Keyboard) || Input.IsKeyDown(b.Gamepad);

                if (keyDown)
                {
                    // 按下沿：开始计时 + 按下沿事件（节奏玩法专用，如偷窃条出手/收手——
                    // 松开触发晚一次点按会发粘，配置层仍只有 Short/Long，内部通道按需选择）
                    if (!st.Tracking)
                    {
                        st.Tracking = true;
                        st.HoldTime = 0f;
                        st.LongReady = false;
                        st.PressedFired = true;
                    }
                    else
                    {
                        st.HoldTime += dt;
                    }

                    // 长按行：按住跨越阈值 → 进入待命（Ready，进度框满），不触发；
                    // 松开才执行（KCD 语义：执行时机由玩家掌控，可等目标转身/目击者走开再松手）
                    if (b.PressMode == ModInputPressMode.Long && !st.LongReady
                        && st.HoldTime * 1000f >= b.ThresholdMs)
                    {
                        st.LongReady = true;
                    }
                }
                else if (st.Tracking)
                {
                    // 松开沿：长按行已待命（满）→ 触发（玩家选时机松手）；阈值前松开无触发；
                    // 短按行在阈值前松开 → 触发；超阈值 → 取消（转入同键其他行的长按路径）
                    if (b.PressMode == ModInputPressMode.Long && st.LongReady)
                        st.LongFired = true;
                    else if (b.PressMode == ModInputPressMode.Short && st.HoldTime * 1000f < b.ThresholdMs)
                        st.ShortFired = true;
                    st.Tracking = false;
                    st.HoldTime = 0f;
                    st.LongReady = false;
                }
            }
        }

        /// <summary>
        /// 按下沿触发（一次性）：物理键按下的那一帧。节奏玩法专用（偷窃条出手/收手）——
        /// 与 ShortFired 的区别：ShortFired 是松开触发（晚一次点按），连打/时机判定场景会发粘。
        /// 配置层仍只有 Short/Long，此通道是内部保留的按下沿判定（plan §4.1 兜底）。
        /// </summary>
        public static bool PressedFired(string interactionId)
        {
            if (!_states.TryGetValue(interactionId, out RowState st)) return false;
            bool fired = st.PressedFired;
            st.PressedFired = false;   // 消费即清
            return fired;
        }

        /// <summary>系统模态弹窗激活（ShowTextInquiry 输入框 / ShowInquiry 确认框）。
        /// 用 TopScreen 类型名判断（引擎稳定类名；含 "Inquiry" 即视为弹窗，覆盖 TextInquiryScreen 等变体）。
        /// 不引引擎 Screen 类型强引用——漏判最坏只是少拦一次，不会崩。</summary>
        public static bool IsSystemModalActive()
        {
            var top = ScreenManager.TopScreen;
            string n = top?.GetType().Name;
            return n != null && n.IndexOf("Inquiry", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>短按触发（一次性）：松开且按住时长 &lt; 该玩法阈值的一瞬间。</summary>
        public static bool ShortFired(string interactionId)
        {
            if (!_states.TryGetValue(interactionId, out RowState st)) return false;
            bool fired = st.ShortFired;
            st.ShortFired = false;   // 消费即清
            return fired;
        }

        /// <summary>
        /// 长按触发（一次性）：已满（跨阈值待命）后的松开沿——KCD 语义，
        /// 执行时机由玩家掌控（可保持按住等待目标转身/目击者走开再松手）。
        /// 阈值前松开无触发（动作取消）。
        /// </summary>
        public static bool LongFired(string interactionId)
        {
            if (!_states.TryGetValue(interactionId, out RowState st)) return false;
            bool fired = st.LongFired;
            st.LongFired = false;
            return fired;
        }

        /// <summary>该玩法行当前是否按住中（供进度 UI / 长按状态查询）。</summary>
        public static bool IsHeld(string interactionId)
        {
            return _states.TryGetValue(interactionId, out RowState st) && st.Tracking;
        }

        /// <summary>长按进度 0..1（按住时长 / 该玩法阈值；短按行恒 0——UI 不显示进度）。</summary>
        public static float HoldProgress(string interactionId)
        {
            if (!_bindings.TryGetValue(interactionId, out InteractionBinding b)) return 0f;
            if (b.PressMode != ModInputPressMode.Long) return 0f;
            if (!_states.TryGetValue(interactionId, out RowState st) || !st.Tracking) return 0f;
            return MathF.Clamp(st.HoldTime * 1000f / b.ThresholdMs, 0f, 1f);
        }

        /// <summary>取消某玩法行的按住状态（目标丢失 / 上下文退出 / UI 隐藏时调用：进度框立即消退、不误触发）。</summary>
        public static void Reset(string interactionId)
        {
            if (_states.TryGetValue(interactionId, out RowState st))
            {
                st.Tracking = false;
                st.HoldTime = 0f;
                st.LongReady = false;
                st.PressedFired = false;
                st.ShortFired = false;
                st.LongFired = false;
            }
        }

        /// <summary>取消全部玩法行状态（Mission 结束兜底）。</summary>
        public static void ResetAll()
        {
            foreach (RowState st in _states.Values)
            {
                st.Tracking = false;
                st.HoldTime = 0f;
                st.LongReady = false;
                st.PressedFired = false;
                st.ShortFired = false;
                st.LongFired = false;
            }
        }

        // ═══════════════════════ 查询 / 字形 ═══════════════════════

        /// <summary>取玩法行绑定（UI 读键位/按法；null = 未配置该玩法）。</summary>
        public static InteractionBinding GetBinding(string interactionId)
        {
            _bindings.TryGetValue(interactionId, out InteractionBinding b);
            return b;
        }

        /// <summary>当前设备下的提示字形（键盘 "F" / Xbox "Y" / PS "△"；未配置返回空串）。</summary>
        public static string Glyph(string interactionId)
        {
            if (!_bindings.TryGetValue(interactionId, out InteractionBinding b)) return "";
            if (!UsingGamepad) return b.KbGlyph;
            return IsPlayStation ? b.PsGlyph : b.XboxGlyph;
        }
    }
}
