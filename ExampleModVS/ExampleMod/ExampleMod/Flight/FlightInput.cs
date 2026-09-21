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

        /// <summary>每帧调一次（由 <see cref="PlayerFlightBehavior"/> 驱动）。</summary>
        public static void Tick(float dt)
        {
            BlockedByUi = SafeIsUiBlocking();

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

            float x = 0f, y = 0f;
            if (Input.IsKeyDown(InputKey.D)) x += 1f;
            if (Input.IsKeyDown(InputKey.A)) x -= 1f;
            if (Input.IsKeyDown(InputKey.W)) y += 1f;
            if (Input.IsKeyDown(InputKey.S)) y -= 1f;

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
