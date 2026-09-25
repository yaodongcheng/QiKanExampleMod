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
    ///
    /// 🔴 移动键读的是**原始 WASD**（不读玩家的改键）：默认键位即 WASD，而这里的输入只服务飞行
    ///    这一个玩法，走原始键可以完全不依赖引擎的输入上下文（那东西在场景切换期可能还没就绪）。
    ///    **小键盘 8/2/4/6 一并保留**：它没绑任何游戏移动键，是排查「角色自己走下板」时
    ///    「不冻结也不会误触走路」的隔离对照组（2026-09-21 就是这么定位到问题的）。
    /// </summary>
    public static class FlightInput
    {
        private static float _spaceHold;
        private static bool _spaceLongConsumed;
        private static bool _spacePressedEdge;      // 空格"按下沿"（本帧刚按下），一次性消费
        private static bool _boostPressedEdge;      // 冲刺键"按下沿"，一次性消费（进冲刺入姿用）
        private static bool _boostWasHeld;          // 上一帧冲刺键是否按住（判按下沿用）
        private static bool _firePressedEdge;       // 左键"按下沿"，一次性消费（飞行中施法：发射）
        private static bool _fireWasHeld;           // 上一帧左键是否按住（判按下沿用）

        /// <summary>空格当前是否按住。</summary>
        public static bool SpaceHeld { get; private set; }

        /// <summary>空格已按住多少秒（给 UI 画进度用）。</summary>
        public static float SpaceHoldSeconds => _spaceHold;

        /// <summary>左 Shift 是否按住 = 冲刺。</summary>
        public static bool BoostHeld { get; private set; }

        /// <summary>
        /// 鼠标右键是否按住 = **瞄准机位**（N5）。
        /// 🔴 只在悬停 / 巡航生效 —— 加速中不给进（用户裁定：瞄准必须是巡航或悬停状态）。
        /// 判定在 <c>PlayerFlightBehavior.PickCamPreset</c> 里，这里只管读键。
        /// </summary>
        public static bool AimHeld { get; private set; }

        /// <summary>
        /// 移动轴：X = 右(D−A)，Y = 前(W−S)，范围 −1..1，对角线会归一化。
        /// </summary>
        public static Vec2 MoveAxis { get; private set; }

        /// <summary>有没有真的在推方向（含死区）。</summary>
        public static bool HasMoveInput => MoveAxis.LengthSquared > 0.02f;

        /// <summary>本帧是否该被 UI 拦下（全屏原版界面 / ESC 菜单 / IM 面板 / 当面对话流程开着时）。</summary>
        public static bool BlockedByUi { get; private set; }

        // ── 诊断用：把原始读到的状态原样暴露出来，排查"按键没反应"时一眼定位 ──
        /// <summary>诊断：每个键的**原始读数**（绕开一切逻辑，直接是 Input.IsKeyDown 的返回）。</summary>
        public static bool DiagWDown { get; private set; }
        public static bool DiagADown { get; private set; }
        public static bool DiagSDown { get; private set; }
        public static bool DiagDDown { get; private set; }
        public static bool DiagNumpad8Down { get; private set; }
        public static bool DiagNumpad2Down { get; private set; }
        public static bool DiagNumpad4Down { get; private set; }
        public static bool DiagNumpad6Down { get; private set; }
        public static bool DiagSpaceDown { get; private set; }
        public static bool DiagShiftDown { get; private set; }
        public static bool DiagLeftMouseDown { get; private set; }
        public static bool DiagRightMouseDown { get; private set; }
        /// <summary>诊断：本帧是否因为 UI 门控被整体清空。</summary>
        public static bool DiagWasReset { get; private set; }

        /// <summary>一行诊断摘要：键 + 门控 + 合成出来的轴。</summary>
        public static string Diagnose()
        {
            return string.Format(
                "ui={0} reset={1} | WASD {2}{3}{4}{5} | 小键盘 8={6} 4={7} 2={8} 6={9} | space={10} shift={11} | axis=({12:F2},{13:F2}) hasMove={14}",
                BlockedByUi ? 1 : 0, DiagWasReset ? 1 : 0,
                DiagWDown ? 1 : 0, DiagADown ? 1 : 0, DiagSDown ? 1 : 0, DiagDDown ? 1 : 0,
                DiagNumpad8Down ? 1 : 0, DiagNumpad4Down ? 1 : 0,
                DiagNumpad2Down ? 1 : 0, DiagNumpad6Down ? 1 : 0,
                DiagSpaceDown ? 1 : 0, DiagShiftDown ? 1 : 0,
                MoveAxis.x, MoveAxis.y, HasMoveInput ? 1 : 0);
        }

        /// <summary>
        /// 按 <see cref="AnimPrimitives.Keys"/> 的**键序**读原始键（给上下文填 <c>IAnimInputFacts</c> 用）。
        /// 🔴 这里的 case 顺序**必须**与 `AnimPrimitives.Keys` 一模一样（W A S D Space Shift RMB LMB）；
        ///    改那边就要改这里，否则键会错位。
        /// </summary>
        public static bool RawKeyAt(int i)
        {
            switch (i)
            {
                case 0: return DiagWDown;
                case 1: return DiagADown;
                case 2: return DiagSDown;
                case 3: return DiagDDown;
                case 4: return DiagSpaceDown;
                case 5: return DiagShiftDown;
                case 6: return DiagRightMouseDown;
                case 7: return DiagLeftMouseDown;
                default: return false;
            }
        }

        /// <summary>每帧调一次（由 <see cref="PlayerFlightBehavior"/> 驱动）。</summary>
        public static void Tick(float dt)
        {
            BlockedByUi = SafeIsUiBlocking();
            DiagWasReset = BlockedByUi;

            // 🔴 按下沿**只活一帧**：每帧开头先清掉上一帧遗留的。
            //    不清的后果很具体：在地面按空格起跳（按下沿置位但本帧还没离地、没被消费），
            //    下一帧刚好离地 → `ConsumeSpacePress()` 命中 ⇒ **一跳就直接进飞行**。
            _spacePressedEdge = false;
            _boostPressedEdge = false;

            // 诊断：不管门控开不开，先把原始读到的值记下来（读的就是后面逻辑要用的那几个键）
            DiagWDown = Input.IsKeyDown(InputKey.W);
            DiagADown = Input.IsKeyDown(InputKey.A);
            DiagSDown = Input.IsKeyDown(InputKey.S);
            DiagDDown = Input.IsKeyDown(InputKey.D);
            DiagNumpad8Down = Input.IsKeyDown(InputKey.Numpad8);
            DiagNumpad4Down = Input.IsKeyDown(InputKey.Numpad4);
            DiagNumpad2Down = Input.IsKeyDown(InputKey.Numpad2);
            DiagNumpad6Down = Input.IsKeyDown(InputKey.Numpad6);
            DiagSpaceDown = Input.IsKeyDown(InputKey.Space);
            DiagShiftDown = Input.IsKeyDown(InputKey.LeftShift);
            DiagLeftMouseDown = Input.IsKeyDown(InputKey.LeftMouseButton);
            DiagRightMouseDown = Input.IsKeyDown(InputKey.RightMouseButton);

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
                    // 按下沿：重新计时，同时记一个"刚按下"（二段跳起飞用）
                    _spaceHold = 0f;
                    _spaceLongConsumed = false;
                    _spacePressedEdge = true;
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
            if (BoostHeld && !_boostWasHeld)
                _boostPressedEdge = true;           // 按下沿：进冲刺入姿那一段动画用它
            _boostWasHeld = BoostHeld;
            AimHeld = Input.IsKeyDown(InputKey.RightMouseButton);

            // 左键"按下沿" = **飞行中施法的发射**（右键蓄力 → 左键放）。
            // 飞行期间引擎不接受玩家输入（冻结档 aipause），所以左键不会同时触发原版攻击 —— 这个键在飞行中是空的。
            // 🔴 **"按下沿只活一帧"**（本项目输入铁律）：先无条件清，本帧真按下才重新置位。
            //    消费方 `SpellCastInput` 跑在本 Tick **之前** ⇒ 它看到的是上一帧置的位 = 正好一帧窗口，
            //    不会把很久以前那一次点击积压到"刚开始蓄力就自己放一发"。
            _firePressedEdge = false;
            bool fire = Input.IsKeyDown(InputKey.LeftMouseButton);
            if (fire && !_fireWasHeld)
                _firePressedEdge = true;
            _fireWasHeld = fire;

            // 🔴 主路 = WASD（2026-09-21 T1 接回）：飞行的方向键就是游戏自己的走路键。
            //    它能成立的前提是**飞行期间把玩家冻结**（见 FlightTuning.FreezePlayerInput）——
            //    不冻结的话角色会自己走下木板。
            // 🔴 小键盘 8/2/4/6 一并保留，**不是历史残留**：它没绑任何游戏移动键，
            //    是"不冻结也不会误触走路"的隔离对照组 —— 万一 WASD 表现不对，
            //    用它飞一圈即可判定问题出在"冻结没生效"还是"板本身飞不动"。
            float x = 0f, y = 0f;
            if (Input.IsKeyDown(InputKey.D) || Input.IsKeyDown(InputKey.Numpad6)) x += 1f;
            if (Input.IsKeyDown(InputKey.A) || Input.IsKeyDown(InputKey.Numpad4)) x -= 1f;
            if (Input.IsKeyDown(InputKey.W) || Input.IsKeyDown(InputKey.Numpad8)) y += 1f;
            if (Input.IsKeyDown(InputKey.S) || Input.IsKeyDown(InputKey.Numpad2)) y -= 1f;

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

        /// <summary>
        /// 空格**按下沿**是否发生过 —— **边沿触发，一次按下只返回一次 true**。
        /// 二段跳起飞用它（跳跃中按一下空格 ⇒ 进飞行）。
        /// 🔴 与 <see cref="ConsumeSpaceLongPress"/> 是**两个独立通道**：同一个按下沿，
        ///    按下的那一帧 `ConsumeSpacePress` 命中；按住到阈值时 `ConsumeSpaceLongPress` 命中。
        ///    所以"跳跃中短按"和"站着长按"不会互相吃掉。
        /// </summary>
        public static bool ConsumeSpacePress()
        {
            if (!_spacePressedEdge)
                return false;
            _spacePressedEdge = false;
            return true;
        }

        /// <summary>
        /// 冲刺键（左 Shift）**按下沿**是否发生过 —— 边沿触发，一次按下只返回一次 true。
        /// 🔴 **当前无消费者**（2026-09-25 起）：入姿动画改判"来源是直立家 + Shift 按着"
        ///    （见 `FlightAnimMachine` 的 ③ 那段），一帧就消失的按下沿会被 Hold / 施法让位吞掉。
        ///    保留本方法是因为它是个**输入事实**（将来若有"双击 Shift"这类手势还要用），不是死逻辑。
        /// 与 <see cref="BoostHeld"/> 是两回事：那是"按着"，这是"刚按下"。
        /// </summary>
        public static bool ConsumeBoostPress()
        {
            if (!_boostPressedEdge)
                return false;
            _boostPressedEdge = false;
            return true;
        }

        /// <summary>
        /// 鼠标左键**按下沿**是否发生过 —— 边沿触发，一次按下只返回一次 true。
        /// 用途：**飞行中施法的"发射"**（右键蓄力 → 左键放；见 <c>Combat/SpellCastInput</c>）。
        /// 🔴 与 <see cref="AimHeld"/> 是两回事：那是"右键按着 = 蓄力/瞄准"，这是"左键刚点了一下"。
        /// </summary>
        public static bool ConsumeFirePress()
        {
            if (!_firePressedEdge)
                return false;
            _firePressedEdge = false;
            return true;
        }

        /// <summary>清空全部按键状态（进新场景 / 退出飞行时调）。</summary>
        public static void Reset()
        {
            SpaceHeld = false;
            BoostHeld = false;
            AimHeld = false;
            MoveAxis = Vec2.Zero;
            _spaceHold = 0f;
            _spaceLongConsumed = false;
            _spacePressedEdge = false;
            _boostPressedEdge = false;
            _boostWasHeld = false;
            _firePressedEdge = false;
            _fireWasHeld = false;
        }

        /// <summary>
        /// 模态门控：三种模态期间飞行不接受输入。
        ///
        /// 🔴 **WASD 接回后这条更要紧了**（2026-09-21）：WASD 同时是文本框里能打出来的字母 ——
        ///    IM 面板开着时打字，若不拦就会被当成飞行方向。门控清单与 <c>ModInput.Tick</c> 对齐
        ///    （全屏原版界面 / ESC 菜单 / IM 面板 / 当面对话密谋流程）。
        /// </summary>
        private static bool SafeIsUiBlocking()
        {
            try
            {
                if (UiFullScreenHelper.IsFullScreenUiOpen() || UiFullScreenHelper.IsEscapeMenuOpen())
                    return true;

                // IM 聊天面板：玩家在里面打字，字母键必须归它
                if (ImChatView.IsOpen)
                    return true;

                // 当面对话 / 密谋流程
                if (PlanCommandFlow.IsActive)
                    return true;

                return false;
            }
            catch
            {
                // UI 检测只是"别误触"的护栏，它自己出问题不该拖垮飞行
                return false;
            }
        }
    }
}
