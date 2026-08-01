using System.Collections.Generic;
using TaleWorlds.InputSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Mod 语义动作（UE4 风格 Action Mapping）。
    /// 业务层只认动作、不认物理键：新增互动 = 在这里加一个枚举值 + 在 <see cref="ModInput"/> 的映射表加一行。
    /// </summary>
    public enum ModInputAction
    {
        Interact,       // 主互动：对话 / 偷窃 / 击晕 / 搜刮 / 撬锁（默认 F / X·□）
        AltInteract,    // 副互动：闲聊 / 接受认输（默认 G / Y·△）
        Inspect,        // 探查：NPC 信息板（默认 H / L3）
        StealAttempt,   // 偷窃条：出手（默认 空格 / A·✕）
        StealLeave,     // 偷窃条：收手（默认 Tab / B·○）
    }

    /// <summary>
    /// 输入统一归口（UE4 风格）：语义动作 → 键盘/手柄双映射 + 提示字形。
    ///
    /// ① 输入轮询：<see cref="Pressed"/> / <see cref="Released"/> 同时监听键盘与手柄绑定，
    ///    玩家中途换设备不需要任何切换逻辑，哪边按都算数。
    /// ② 提示字形：<see cref="Glyph"/> 按"最近一次输入设备"（引擎原生追踪
    ///    <c>Input.IsGamepadActive = IsControllerConnected &amp;&amp; !IsMouseActive</c>）
    ///    返回键盘字形或手柄字形；手柄再按 <c>Input.ControllerType.IsPlaystation()</c>
    ///    分 Xbox（X/Y/A/B）与 PS（□/△/✕/○）两套文本。
    /// ③ 设备切换 UI 刷新：调用方缓存 <see cref="UsingGamepad"/> 逐帧对比，
    ///    变化时刷新所有按键提示（范本：InteractionMissionView.OnMissionTick）。
    /// </summary>
    public static class ModInput
    {
        /// <summary>一条动作的物理绑定：键盘键 + 手柄键 + 三套显示字形。</summary>
        private sealed class Binding
        {
            public InputKey Keyboard;
            public InputKey Gamepad;
            public string KbGlyph;      // 键盘提示（InteractArea 圆徽 / 按钮文本通用）
            public string XboxGlyph;    // Xbox 手柄提示
            public string PsGlyph;      // PlayStation 手柄提示（□△✕○ 为 CJK 符号区，游戏字体可用）
        }

        /// <summary>
        /// 动作映射表 —— 全 mod 唯一改键入口。
        /// 手柄键位对照：RDown=A/✕　RRight=B/○　RUp=Y/△　RLeft=X/□　LThumb=L3。
        /// </summary>
        private static readonly Dictionary<ModInputAction, Binding> Map = new Dictionary<ModInputAction, Binding>
        {
            // 主互动：参照主机版骑砍惯例，X/□ = 互动（A 是跳，避开冲突）
            [ModInputAction.Interact] = new Binding
            {
                Keyboard = InputKey.F, Gamepad = InputKey.ControllerRLeft,
                KbGlyph = "F", XboxGlyph = "X", PsGlyph = "□",
            },
            // 副互动：Y/△
            [ModInputAction.AltInteract] = new Binding
            {
                Keyboard = InputKey.G, Gamepad = InputKey.ControllerRUp,
                KbGlyph = "G", XboxGlyph = "Y", PsGlyph = "△",
            },
            // 探查：L3（右肩键区留给战斗，信息类放摇杆按下）
            [ModInputAction.Inspect] = new Binding
            {
                Keyboard = InputKey.H, Gamepad = InputKey.ControllerLThumb,
                KbGlyph = "H", XboxGlyph = "L3", PsGlyph = "L3",
            },
            // 偷窃条出手：A/✕（确认键；条打开期间玩家控制已冻结，不会误触跳）
            [ModInputAction.StealAttempt] = new Binding
            {
                Keyboard = InputKey.Space, Gamepad = InputKey.ControllerRDown,
                // 键盘提示字形：空格键
                KbGlyph = LWNTextHelper.ResolveText("LWN_input_key_space", "Space"), XboxGlyph = "A", PsGlyph = "✕",
            },
            // 偷窃条收手：B/○（取消键）
            [ModInputAction.StealLeave] = new Binding
            {
                Keyboard = InputKey.Tab, Gamepad = InputKey.ControllerRRight,
                KbGlyph = "Tab", XboxGlyph = "B", PsGlyph = "○",
            },
        };

        /// <summary>玩家最近一次输入是手柄（引擎原生追踪：手柄已连接且鼠标未活动）。</summary>
        public static bool UsingGamepad => Input.IsGamepadActive && Input.IsControllerConnected;

        /// <summary>当前手柄是 PlayStation 系（DualShock/DualSense）。</summary>
        public static bool IsPlayStation => Input.ControllerType.IsPlaystation();

        /// <summary>按下沿：键盘或手柄任一命中即算（设备无关）。</summary>
        public static bool Pressed(ModInputAction action)
        {
            Binding b = Map[action];
            return Input.IsKeyPressed(b.Keyboard) || Input.IsKeyPressed(b.Gamepad);
        }

        /// <summary>松开沿：键盘或手柄任一命中即算（设备无关）。</summary>
        public static bool Released(ModInputAction action)
        {
            Binding b = Map[action];
            return Input.IsKeyReleased(b.Keyboard) || Input.IsKeyReleased(b.Gamepad);
        }

        /// <summary>当前设备下的提示字形（键盘 "F" / Xbox "X" / PS "□"）。</summary>
        public static string Glyph(ModInputAction action)
        {
            Binding b = Map[action];
            if (!UsingGamepad) return b.KbGlyph;
            return IsPlayStation ? b.PsGlyph : b.XboxGlyph;
        }
    }
}
