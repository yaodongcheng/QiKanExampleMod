using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HarmonyLib;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// IME（输入法）组合态检测——**三信号**：VK_PROCESSKEY 按键消费 + WM_IME 消息跟踪 + IMM32 轮询。
    /// 背景（2026-08-21 实机）：中文输入法组词期间（拼音未上屏），退格/回车/方向键由输入法消费
    /// （改拼音/翻候选/上屏），但骑砍2 的输入系统是**原始按键轮询**（Input.IsKeyPressed），
    /// 物理按键照单全收——于是输入框把「输入法里的退格」也当成删字处理，打好的字全被删掉。
    /// ⚠️ 实测教训 1（搜狗 TSF）：TSF 型输入法（搜狗/微软拼音）不走 IMM32 上下文——
    /// `ImmGetCompositionString(GCS_COMPSTR)` 返回 0，单靠 IMM32 轮询漏检。
    /// ⚠️ 实测教训 2（WM_IME 消息路由盲区）：用户组合 "nihao"+2退格（~2s）期间，主窗口钩子
    /// **只收到一对 14ms 的 START/END**——组合消息根本不全到主窗口（TSF 组合消息路由不同/被
    /// 游戏 native 层消耗）。组合期间游戏门是开的 → 退格照删。消息路由不可依赖。
    /// ⚠️ 实测教训 3（游戏轮询延迟）：游戏 Input 按键沿比物理按键晚 1~2 帧——事件时刻的判定
    /// 必须用物理/消息层信号，不能用游戏帧轮询状态。
    /// 🔴 最终方案——**VK_PROCESSKEY 按键消费信号**：被输入法消费的键，窗口收到的 WM_KEYDOWN
    /// 的 wParam = VK_PROCESSKEY(0xE5)（不是真实 VK 码）。这是**每个按键、事件时刻、与消息
    /// 路由无关**的信号——组合期间每个字母/退格都会产生它。最近一次 VK_PROCESSKEY 后 150ms
    /// 内门保持关闭：覆盖游戏轮询延迟 1~2 帧（33-50ms）的残余沿；组合结束后的新按键（反应
    /// 时间 &gt;200ms）不受影响。叠加：WM_IME 消息组合态 + IMM32 轮询（经典输入法）+ 武装键。
    /// 兜底：组合中 &gt;5s 无任何 WM_IME 消息 = 视为组合已结束（防钩子失效后永久锁死）。
    /// 诊断：DebugLogger 的 [ImeInput] 记录挂载/消息序列/按键消费/门开关——验证用
    /// Debug/StoryEngine_RuntimeLog.txt。
    /// </summary>
    public static class ImeCompositionHelper
    {
        // ── Win32 常量 ──
        private const int WM_KEYDOWN = 0x0100;
        private const int VK_PROCESSKEY = 0xE5;                 // 输入法消费的按键（wParam 替换为它）
        private const int WM_IME_STARTCOMPOSITION = 0x010D;     // 组合开始
        private const int WM_IME_ENDCOMPOSITION = 0x010E;       // 组合结束（上屏或取消都会发）
        private const int WM_IME_COMPOSITION = 0x010F;          // 组合内容变化（lParam = GCS_* 标志）
        private const int GCS_COMPSTR = 0x0008;                 // 组合字符串存在
        private const int GWLP_WNDPROC = -4;
        /// <summary>VK_PROCESSKEY 后的锁门宽限（ms）：覆盖游戏轮询延迟 1~2 帧的残余按键沿。</summary>
        private const int ImeKeyGraceMs = 150;

        // 导航键虚拟键码（vanilla HandleInput 轮询的键；Enter 与 NumpadEnter 物理同键 VK_RETURN）
        private const int VK_BACK = 0x08, VK_RETURN = 0x0D, VK_END = 0x23, VK_HOME = 0x24,
            VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28, VK_DELETE = 0x2E;

        // ── 信号 1：VK_PROCESSKEY 按键消费（🔴 主信号，事件时刻、按键级、路由无关）──
        private static int _lastVkProcessKeyTick = int.MinValue;

        // ── 信号 2：WM_IME 消息组合态（WndProc 子类化，补充信号）──
        private static bool _imeMsgComposing;
        private static int _lastImeMsgTick; // 最近一次 WM_IME_* 消息时刻（超时兜底用）

        // ── 信号 3：武装键（组合结束瞬间物理按下的导航键掩码，bit: 0=Back 1=Delete 2=Enter 3=Left 4=Up 5=Right 6=Down 7=Home 8=End）──
        private static int _armedKeys;

        // ── WndProc 回调异常节流（native 回调内绝不传播异常）──
        private static int _hookErrorTick;

        // ── WndProc 子类化（**所有顶层窗口**——组合消息路由到哪个窗口不确定，全挂；委托与
        // 函数指针必须静态保活，否则 GC 回收后回调崩溃）──
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private static WndProcDelegate _newWndProc;
        private static IntPtr _newWndProcPtr;
        private static readonly Dictionary<IntPtr, IntPtr> _oldProcByHwnd = new Dictionary<IntPtr, IntPtr>();
        private static bool _hookInstalled;
        private static bool _hookFailed;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetContext(IntPtr hWnd);
        [DllImport("imm32.dll")]
        private static extern int ImmGetCompositionStringW(IntPtr hIMC, int dwIndex, IntPtr lpBuf, int dwBufLen);
        [DllImport("imm32.dll")]
        private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lProcessId);

        /// <summary>true = 输入法正在组词（拼音未上屏），或组合刚结束的宽限窗内。
        /// 三信号任一命中 + 武装键三态。</summary>
        public static bool IsComposing()
        {
            EnsureHookInstalled();

            // 信号 1（主）：VK_PROCESSKEY 后 150ms 宽限——组合期间每个字母/退格都被输入法消费，
            // 持续刷新；组合结束 → 无新消费 → 宽限过后门开（新按键是正常编辑）
            // 🔴 2026-08-22（PC 实机：退格全失效——启动即误判组合态）：初始哨兵 int.MinValue 与
            // TickCount 相减必然 int 溢出为负数 → 负数 < 150 恒成立 → 组合态从启动起永远 true，
            // 可打印字符（123）按「组合中上屏」放行、退格/Delete/方向键/Enter 全被吞（日志：
            // 启动即「组合态吞键」、无任何 WM_IME 消息）。修：哨兵值跳过判定 + (uint) 差值
            // 比较（TickCount 无符号溢出安全，系统 uptime 24.8 天翻转免疫）。
            if (_lastVkProcessKeyTick != int.MinValue
                && (uint)(Environment.TickCount - _lastVkProcessKeyTick) < ImeKeyGraceMs) return true;

            // 信号 2：WM_IME 消息组合态
            bool composing = _imeMsgComposing || Imm32Poll();

            // 兜底：组合中 >5s 无任何 WM_IME 消息 = 视为组合已结束（防钩子失效后永久锁死）
            if (composing && Environment.TickCount - _lastImeMsgTick > 5000)
            {
                DebugLogger.Log("[ImeInput] 组合态超时兜底（5s 无 IME 消息，按组合结束处理）");
                _imeMsgComposing = false;
                composing = false;
            }

            if (composing) return true;

            // 信号 3：武装键——组合结束瞬间物理按下的导航键（= 输入法消费掉的按键），按住期间
            // 门保持关闭（覆盖游戏轮询延迟沿）；GetAsyncKeyState 物理状态零延迟
            if (_armedKeys != 0)
            {
                int stillDown = 0;
                if ((_armedKeys & (1 << 0)) != 0 && IsPhysDown(VK_BACK)) stillDown |= 1 << 0;
                if ((_armedKeys & (1 << 1)) != 0 && IsPhysDown(VK_DELETE)) stillDown |= 1 << 1;
                if ((_armedKeys & (1 << 2)) != 0 && IsPhysDown(VK_RETURN)) stillDown |= 1 << 2;
                if ((_armedKeys & (1 << 3)) != 0 && IsPhysDown(VK_LEFT)) stillDown |= 1 << 3;
                if ((_armedKeys & (1 << 4)) != 0 && IsPhysDown(VK_UP)) stillDown |= 1 << 4;
                if ((_armedKeys & (1 << 5)) != 0 && IsPhysDown(VK_RIGHT)) stillDown |= 1 << 5;
                if ((_armedKeys & (1 << 6)) != 0 && IsPhysDown(VK_DOWN)) stillDown |= 1 << 6;
                if ((_armedKeys & (1 << 7)) != 0 && IsPhysDown(VK_HOME)) stillDown |= 1 << 7;
                if ((_armedKeys & (1 << 8)) != 0 && IsPhysDown(VK_END)) stillDown |= 1 << 8;
                _armedKeys = stillDown; // 已松开的武装键清除
                if (stillDown != 0) return true;
            }
            return false;
        }

        /// <summary>信号 2 补充：IMM32 轮询（经典 IMM32 输入法路径）。</summary>
        private static bool Imm32Poll()
        {
            try
            {
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd == IntPtr.Zero) return false;
                IntPtr hIMC = ImmGetContext(hWnd);
                if (hIMC == IntPtr.Zero) return false;
                try
                {
                    return ImmGetCompositionStringW(hIMC, GCS_COMPSTR, IntPtr.Zero, 0) > 0;
                }
                finally
                {
                    ImmReleaseContext(hWnd, hIMC);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>物理键是否处于按下状态（GetAsyncKeyState 高位置位；键盘事件即时刷新，无游戏轮询延迟）。
        /// 🔴 P/Invoke 必须 try/catch——Wine/Proton（SteamDeck）等兼容层下 DLL 导出可能有差异，
        /// 失败 = 降级「未按下」（不拦截 = 原版行为），绝不传播异常。</summary>
        private static bool IsPhysDown(int vk)
        {
            try
            {
                return (GetAsyncKeyState(vk) & 0x8000) != 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>🔴 诊断串（排查用，2026-08-21）：按键消费宽限/消息组合态/武装掩码/距最近 IME 消息毫秒/
        /// 物理键状态/游戏键状态。一行还原完整时序。</summary>
        public static string DiagState()
        {
            int vkElapsed = _lastVkProcessKeyTick == int.MinValue ? -1 : Environment.TickCount - _lastVkProcessKeyTick;
            int imeElapsed = _lastImeMsgTick == 0 ? -1 : Environment.TickCount - _lastImeMsgTick;
            return $"vk={vkElapsed}ms msg={(_imeMsgComposing ? 1 : 0)} armed=0x{_armedKeys:X} lastIme={imeElapsed}ms "
                + $"physBack={IsPhysDown(VK_BACK)} physDel={IsPhysDown(VK_DELETE)} physEnt={IsPhysDown(VK_RETURN)} "
                + $"gameBack={Input.IsKeyDown(InputKey.BackSpace)}";
        }

        /// <summary>首次调用时把 WndProc 子类化到**本进程全部顶层窗口**（懒安装，须在窗口线程上调用——
        /// HandleInput/ImChatView.Tick 都在游戏主线程 = 窗口创建线程，合规）。窗口未就绪时静默重试。</summary>
        private static void EnsureHookInstalled()
        {
            if (_hookInstalled || _hookFailed) return;
            try
            {
                uint myPid = (uint)Process.GetCurrentProcess().Id;
                int count = 0;
                EnumWindows(delegate (IntPtr hWnd, IntPtr lParam)
                {
                    GetWindowThreadProcessId(hWnd, out uint pid);
                    if (pid == myPid && InstallHookOn(hWnd)) count++;
                    return true;
                }, IntPtr.Zero);
                _hookInstalled = count > 0;
                DebugLogger.Log($"[ImeInput] WndProc 已挂载 {count} 个窗口（hwnd 清单: {string.Join(",", _oldProcByHwnd.Keys)}）");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImeInput] WndProc 挂载异常: {ex.Message}");
                _hookFailed = true;
            }
        }

        private static bool InstallHookOn(IntPtr hWnd)
        {
            IntPtr oldProc = GetWindowLongPtr(hWnd, GWLP_WNDPROC);
            if (oldProc == IntPtr.Zero) return false;
            if (_oldProcByHwnd.ContainsKey(hWnd)) return true;
            if (_newWndProc == null)
            {
                _newWndProc = WndProcHook;
                _newWndProcPtr = Marshal.GetFunctionPointerForDelegate(_newWndProc);
            }
            if (SetWindowLongPtr(hWnd, GWLP_WNDPROC, _newWndProcPtr) == IntPtr.Zero) return false;
            _oldProcByHwnd[hWnd] = oldProc;
            return true;
        }

        private static IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                switch (msg)
                {
                    case WM_KEYDOWN:
                        // 🔴 信号 1：输入法消费的键 = VK_PROCESSKEY——组合期间每个字母/退格都会到
                        if (wParam.ToInt32() == VK_PROCESSKEY)
                        {
                            _lastVkProcessKeyTick = Environment.TickCount;
                            DebugLogger.Log($"[ImeInput] 按键被输入法消费 vk=0xE5 scan=0x{(lParam.ToInt64() >> 16) & 0xFF:X} hwnd={hWnd}");
                        }
                        break;
                    case WM_IME_STARTCOMPOSITION:
                        _lastImeMsgTick = Environment.TickCount;
                        DebugLogger.Log($"[ImeInput] MSG STARTCOMPOSITION hwnd={hWnd}");
                        SetMsgComposing(true, "WM_IME_STARTCOMPOSITION");
                        break;
                    case WM_IME_ENDCOMPOSITION:
                        _lastImeMsgTick = Environment.TickCount;
                        DebugLogger.Log($"[ImeInput] MSG ENDCOMPOSITION hwnd={hWnd}");
                        SetMsgComposing(false, "WM_IME_ENDCOMPOSITION");
                        ArmKeysDownAtCompositionEnd(); // 组合结束瞬间武装物理按下的导航键
                        break;
                    case WM_IME_COMPOSITION:
                        // lParam = 0 表示组合被清空（部分输入法不发 ENDCOMPOSITION 直接清空）；
                        // GCS_COMPSTR 位 = 组合字符串在变（部分输入法漏发 STARTCOMPOSITION，用它兜底）
                        _lastImeMsgTick = Environment.TickCount;
                        DebugLogger.Log($"[ImeInput] MSG COMPOSITION lParam=0x{lParam.ToInt64():X} hwnd={hWnd}");
                        if (lParam == IntPtr.Zero)
                        {
                            SetMsgComposing(false, "WM_IME_COMPOSITION(清空)");
                            ArmKeysDownAtCompositionEnd();
                        }
                        else if ((lParam.ToInt64() & GCS_COMPSTR) != 0)
                        {
                            SetMsgComposing(true, "WM_IME_COMPOSITION(GCS_COMPSTR)");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                // 🔴 native 回调内绝不允许异常传播（Wine/Proton 兼容层下 P/Invoke 行为可能异常）——
                // 吞掉继续转发原消息；重复异常节流防刷屏
                if (Environment.TickCount - _hookErrorTick > 5000)
                {
                    _hookErrorTick = Environment.TickCount;
                    DebugLogger.Log($"[ImeInput] WndProc 处理异常（已吞，不影响窗口）: {ex.Message}");
                }
            }
            IntPtr oldProc;
            return _oldProcByHwnd.TryGetValue(hWnd, out oldProc)
                ? CallWindowProc(oldProc, hWnd, msg, wParam, lParam)
                : DefWindowProc(hWnd, msg, wParam, lParam);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        /// <summary>组合态翻转才打日志（低频，验证用）。</summary>
        private static void SetMsgComposing(bool composing, string reason)
        {
            if (_imeMsgComposing == composing) return;
            _imeMsgComposing = composing;
            DebugLogger.Log($"[ImeInput] 组合态 {(_imeMsgComposing ? "开始" : "结束")}（{reason}）");
        }

        /// <summary>武装组合结束瞬间物理按下的导航键（= 输入法消费掉的按键）。
        /// 此刻输入法刚同步处理完按键、手指仍按着，GetAsyncKeyState 读物理状态零延迟——
        /// 之后游戏轮询的按键沿（延迟 1~2 帧）出现时这些键必然仍被按住，门保持关闭。
        /// 🔴 诊断：无条件打日志（含 0x0——掩码为 0 意味着组合结束与导航键无关，排查必备）。</summary>
        private static void ArmKeysDownAtCompositionEnd()
        {
            int mask = 0;
            if (IsPhysDown(VK_BACK)) mask |= 1 << 0;
            if (IsPhysDown(VK_DELETE)) mask |= 1 << 1;
            if (IsPhysDown(VK_RETURN)) mask |= 1 << 2;
            if (IsPhysDown(VK_LEFT)) mask |= 1 << 3;
            if (IsPhysDown(VK_UP)) mask |= 1 << 4;
            if (IsPhysDown(VK_RIGHT)) mask |= 1 << 5;
            if (IsPhysDown(VK_DOWN)) mask |= 1 << 6;
            if (IsPhysDown(VK_HOME)) mask |= 1 << 7;
            if (IsPhysDown(VK_END)) mask |= 1 << 8;
            _armedKeys = mask;
            DebugLogger.Log($"[ImeInput] 组合结束武装掩码=0x{mask:X}（physBack={IsPhysDown(VK_BACK)} physDel={IsPhysDown(VK_DELETE)} physEnt={IsPhysDown(VK_RETURN)}）");
        }
    }

    /// <summary>
    /// 🔴 2026-08-21（IME 组合态吞键——中文输入法退格删掉已打好的字，用户实机反馈）：
    /// vanilla EditableTextWidget.HandleInput 对退格/Delete/方向键/Home/End/Enter 全部走
    /// **原始按键轮询**（反编译 TaleWorlds.GauntletUI.dll EditableTextWidget.HandleInput 实锤：
    /// `Input.IsKeyPressed(InputKey.BackSpace)` → DeleteChar）。输入法组词期间这些物理按键
    /// 正被输入法消费（改拼音/翻候选/上屏），但游戏轮询照单全收 → 删字、跳光标、误触发
    /// "TextEntered"（Enter 上屏候选字）。处置：组合态时**整个跳过 HandleInput**——
    /// 轮询的按键全部不生效；但上屏帧提交的字符（lastKeysPressed 可打印字符）必须放行。
    /// 组合态判定 = ImeCompositionHelper 三信号（VK_PROCESSKEY 按键消费 + WM_IME 消息 + IMM32），
    /// 见类注释——搜狗等 TSF 型输入法走 VK_PROCESSKEY 路径（消息路由盲区的唯一可靠信号）。
    /// ⚠️ 版本兼容：补丁目标 `HandleInput(IReadOnlyList&lt;int&gt;)` 与命名空间
    /// `TaleWorlds.GauntletUI.BaseTypes` 已用 ilspycmd 三锚点验证
    /// （1.2.12 / 1.3.15 / 1.4.6 参考 DLL 签名与 namespace 一致，1.4.8 实测反编译一致）。
    /// 全局生效（非 IM 专属）：vanilla 改名/命名等输入框同样受益，组合态外行为与原版逐字节一致。
    /// </summary>
    [HarmonyPatch(typeof(EditableTextWidget), "HandleInput")]
    public static class EditableTextImePatch
    {
        /// <summary>本次组合是否已打过吞键日志（每次组合只打一行，防刷屏）。</summary>
        private static bool _gated;

        /// <summary>与 vanilla HandleInput 相同的可打印字符判定（num &gt;= 32 && (num &lt; 127 || num &gt;= 160)，排除 &lt;&gt;）。</summary>
        private static bool IsPrintableChar(int c)
        {
            return c >= 32 && (c < 127 || c >= 160) && c != 60 && c != 62;
        }

        [HarmonyPrefix]
        public static bool Prefix(IReadOnlyList<int> lastKeysPressed)
        {
            bool composing = ImeCompositionHelper.IsComposing();

            // 🔴 诊断（2026-08-21 排查时序）：门开时导航键沿 + 组合中收到的字符事件——
            // 前者还原「游戏什么时候看到退格/回车」，后者验证「组合期间游戏是否收到拼音字母」
            if (!composing && (Input.IsKeyPressed(InputKey.BackSpace) || Input.IsKeyPressed(InputKey.Delete)
                || Input.IsKeyPressed(InputKey.Enter) || Input.IsKeyPressed(InputKey.NumpadEnter)))
            {
                DebugLogger.Log($"[ImeInput] 门开导航沿 {ImeCompositionHelper.DiagState()}");
            }
            if (composing && lastKeysPressed.Count > 0)
            {
                DebugLogger.Log($"[ImeInput] 组合中字符事件 n={lastKeysPressed.Count} vals=[{string.Join(",", lastKeysPressed)}]");
            }

            if (!composing)
            {
                _gated = false;
                return true; // 不在组合：原版行为不变
            }

            // 组合期间仍有可打印字符到达（= 上屏帧，提交的汉字经 WM_CHAR 进 lastKeysPressed）：
            // 放行让字符上屏；否则跳过整个方法（退格/Delete/方向键/Home/End/Enter 轮询全部无效）
            for (int i = 0; i < lastKeysPressed.Count; i++)
                if (IsPrintableChar(lastKeysPressed[i])) return true;
            if (!_gated)
            {
                _gated = true;
                DebugLogger.Log("[ImeInput] 组合态吞键（退格/Delete/方向键/Enter 轮询不生效）");
            }
            return false;
        }
    }
}
