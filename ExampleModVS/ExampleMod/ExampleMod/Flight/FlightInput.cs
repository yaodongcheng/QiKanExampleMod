using TaleWorlds.InputSystem;
using TaleWorlds.Library;

namespace LivingWorldNpcs.Flight
{
    /// <summary>
    /// 飞行按键轮询（2026-09-21）。
    ///
    /// 🔴 为什么不复用项目的 <c>ModInput</c>：
    ///    <c>ModInput.Tick</c> 只有两个调用点，其中 Mission 侧那个在
    ///    <c>InteractionMissionView.OnMissionTick</c> 里 —— 而那个视图挂在
    ///    「战场不跑玩法逻辑」的闸门（<c>Settings.Instance.IsInteractionDisabled()</c>）**之后**。
    ///    ⇒ 战场 / 竞技场里 ModInput 根本不 tick，飞行要在这两类场景生效就必须自己读原始键。
    ///
    /// 🔴 长按语义 = **按住达标即触发**（不是项目的 KCD 式"松手才触发"）——
    ///    起飞要的是"按住一会儿就起来"，松手才飞不符合直觉。
    /// </summary>
    public static class FlightInput
    {
        private static float _spaceHold;
        private static bool _spaceLongConsumed;

        /// <summary>空格当前是否按住。</summary>
        public static bool SpaceHeld { get; private set; }

        /// <summary>空格已按住多少秒（给 UI 画进度用）。</summary>
        public static float SpaceHoldSeconds => _spaceHold;

        /// <summary>左 Shift 是否按住 = 冲刺。</summary>
        public static bool BoostHeld { get; private set; }

        /// <summary>
        /// 移动轴：X = 右(D−A)，Y = 前(W−S)，范围 −1..1，对角线会归一化。
        /// </summary>
        public static Vec2 MoveAxis { get; private set; }

        /// <summary>有没有真的在推方向（含死区）。</summary>
        public static bool HasMoveInput => MoveAxis.LengthSquared > 0.02f;

        /// <summary>本帧是否该被 UI 拦下（全屏界面 / ESC 菜单开着时不接受飞行输入）。</summary>
        public static bool BlockedByUi { get; private set; }

        // ── 诊断用：把原始读到的状态原样暴露出来，排查"按键没反应"时一眼定位 ──
        /// <summary>诊断：每个键的**原始读数**（绕开一切逻辑，直接是 Input.IsKeyDown 的返回）。</summary>
        public static bool DiagWDown { get; private set; }
        public static bool DiagADown { get; private set; }
        public static bool DiagSDown { get; private set; }
        public static bool DiagDDown { get; private set; }
        public static bool DiagSpaceDown { get; private set; }
        public static bool DiagShiftDown { get; private set; }
        /// <summary>诊断：W 的另一种读法（和 <see cref="DiagWDown"/> 对照，判断是不是 API 选错）。</summary>
        public static bool DiagWImmediate { get; private set; }
        /// <summary>诊断：本帧是否因为 UI 门控被整体清空。</summary>
        public static bool DiagWasReset { get; private set; }

        /// <summary>一行诊断摘要：键 + 门控 + 合成出来的轴。</summary>
        public static string Diagnose()
        {
            return string.Format(
                "ui={0} reset={1} | 小键盘 8={2} 8imm={3} 4={4} 2={5} 6={6} space={7} shift={8} | axis=({9:F2},{10:F2}) hasMove={11}",
                BlockedByUi ? 1 : 0, DiagWasReset ? 1 : 0,
                DiagWDown ? 1 : 0, DiagWImmediate ? 1 : 0,
                DiagADown ? 1 : 0, DiagSDown ? 1 : 0, DiagDDown ? 1 : 0,
                DiagSpaceDown ? 1 : 0, DiagShiftDown ? 1 : 0,
                MoveAxis.x, MoveAxis.y, HasMoveInput ? 1 : 0);
        }

        /// <summary>每帧调一次（由 <see cref="PlayerFlightBehavior"/> 驱动）。</summary>
        public static void Tick(float dt)
        {
            BlockedByUi = SafeIsUiBlocking();
            DiagWasReset = BlockedByUi;

            // 诊断：不管门控开不开，先把原始读到的值记下来（读的就是后面逻辑要用的那几个键）
            DiagWDown = Input.IsKeyDown(InputKey.Numpad8);
            DiagWImmediate = Input.IsKeyDownImmediate(InputKey.Numpad8);
            DiagADown = Input.IsKeyDown(InputKey.Numpad4);
            DiagSDown = Input.IsKeyDown(InputKey.Numpad2);
            DiagDDown = Input.IsKeyDown(InputKey.Numpad6);
            DiagSpaceDown = Input.IsKeyDown(InputKey.Space);
            DiagShiftDown = Input.IsKeyDown(InputKey.LeftShift);

            if (BlockedByUi)
            {
                Reset();
                return;
            }

            bool space = Input.IsKeyDown(InputKey.Space);
            if (space)
            {
                if (!SpaceHeld)
                {
                    // 按下沿：重新计时
                    _spaceHold = 0f;
                    _spaceLongConsumed = false;
                }
                _spaceHold += dt;
            }
            else
            {
                _spaceHold = 0f;
                _spaceLongConsumed = false;
            }
            SpaceHeld = space;

            BoostHeld = Input.IsKeyDown(InputKey.LeftShift);

            // 🔴 实验期用**小键盘**（8 前 / 2 后 / 4 左 / 6 右），不用 WASD。
            //    理由：W/A/S/D 同时是游戏自己的走路键，而这一轮**故意不冻结玩家** ——
            //    按 W 会让人自己走下木板，那就分不清"板在动"还是"人在走"。
            //    小键盘没绑任何移动，按了只影响板 ⇒ 把"玩家控制器"这一层彻底排除。
            float x = 0f, y = 0f;
            if (Input.IsKeyDown(InputKey.Numpad6)) x += 1f;
            if (Input.IsKeyDown(InputKey.Numpad4)) x -= 1f;
            if (Input.IsKeyDown(InputKey.Numpad8)) y += 1f;
            if (Input.IsKeyDown(InputKey.Numpad2)) y -= 1f;

            Vec2 axis = new Vec2(x, y);
            float len = axis.Length;
            if (len > 1f)
                axis = axis / len;          // 斜向不加速
            MoveAxis = axis;
        }

        /// <summary>
        /// 长按空格是否刚好达标 —— **边沿触发，一次长按只返回一次 true**。
        /// 起飞与落地都用它（同一个手势管进出）。
        /// </summary>
        public static bool ConsumeSpaceLongPress()
        {
            if (!SpaceHeld || _spaceLongConsumed || _spaceHold < FlightTuning.LongPressSeconds)
                return false;
            _spaceLongConsumed = true;
            return true;
        }

        /// <summary>清空全部按键状态（进新场景 / 退出飞行时调）。</summary>
        public static void Reset()
        {
            SpaceHeld = false;
            BoostHeld = false;
            MoveAxis = Vec2.Zero;
            _spaceHold = 0f;
            _spaceLongConsumed = false;
        }

        private static bool SafeIsUiBlocking()
        {
            try
            {
                return UiFullScreenHelper.IsFullScreenUiOpen() || UiFullScreenHelper.IsEscapeMenuOpen();
            }
            catch
            {
                // UI 检测只是"别误触"的护栏，它自己出问题不该拖垮飞行
                return false;
            }
        }
    }
}
