using HarmonyLib;
using TaleWorlds.InputSystem;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 2026-08-19（用户裁定：Mission 内 IM 打开期间，A/十字键/LB/RB/B 的手柄输入只归面板，
    /// 禁止漏到玩法动作——实机：缩略 IM 聚焦发送按钮按 A 角色挥拳、十字键导航触发骑马、
    /// 按 B 关面板同时弹「离开战场」确认）：拦截 vanilla mission 输入。
    /// 机制（反编译实锤）：
    /// - MissionMainAgentController（TaleWorlds.MountAndBlade.View.dll 23174 行起）在 OnPreMissionTick
    ///   里用 `base.Input.IsGameKeyDown(id)` 收集玩家动作输入——Jump(14)=A、Crouch(15)=十字键下、
    ///   ViewCharacter(25)=十字键左、Cheer(31)=十字键上、PushToTalk(33)=十字键右；手柄键位可被
    ///   玩家在设置里改绑（BannerlordGameKeys.xml 覆盖 GameKey.ControllerKey）
    /// - Mission.OnTick（TaleWorlds.MountAndBlade.dll 55953 行）友好 mission 里
    ///   `InputManager.IsGameKeyDown(4)` = Generic "Leave"（Tab+B）→ OnEndMissionRequest——
    ///   B 关面板的同一按会同时弹离开确认（实机）
    /// - Input.IsGameKeyDown → GameKey.IsDown（TaleWorlds.InputSystem.dll，internal 5 参）
    ///   = 键盘/手柄键池统一判定（KeyboardKey.IsDown() || ControllerKey.IsDown()）——任何设备
    ///   （含 native 手柄映射）都走这里（与 ImChatMapInputPatch 同结论）；层 mask 无手柄位
    ///   （InputUsageMask 只有 Mouse/Keyboard），吞键只能在这里
    /// - GameKey.ControllerKey 反映运行时改绑——按物理键（ControllerKey.InputKey）判拦 =
    ///   玩家把 A/十字键改绑到任何动作（Attack/骑马/跳跃…）都不漏
    /// 处置：Prefix 拦 GameKey.IsDown——两档：
    ///   ① 战斗分类（CombatHotKeyCategory）且 ControllerKey ∈ {A, 十字键 4 向, LB, RB, B}
    ///      → 直接 false。零挂接（纯条件判定，IM 关 → 自然放行）。
    ///   ② ESC 模型（用户裁定 2026-08-19）：IM 打开时 B 单发——B 只关聊天（Tick 消费），
    ///      同一按不许漏到任意分类的 B GameKey（Generic.Leave=4 离开 mission / MissionOrder 选 3 等）
    /// 不拦：左/右摇杆（移动/镜头）、扳机（攻击/格挡）、Y（互动/上马）、X（踢）——缩略模式
    /// 半模态岛设计（玩家继续操作角色），面板只认 A/十字键/LB/RB/B。
    /// ⚠️ 与 ImChatMapInputPatch 同目标方法（GameKey.IsDown）双 Prefix——条件互斥（地图 vs
    /// 战斗分类），Harmony 顺序执行各自判 false，无冲突。
    /// </summary>
    [HarmonyPatch(typeof(GameKey), "IsDown")]
    public static class ImChatMissionInputPatch
    {
        private const string CombatHotKeyCategoryId = "CombatHotKeyCategory";

        /// <summary>IM 面板占用的物理手柄键（按物理键判拦，改绑免疫）：A=激活点击、十字键=移动
        /// 焦点、LB/RB=完整模式翻页、B=关闭面板。</summary>
        private static bool IsPanelKey(InputKey key)
        {
            switch (key)
            {
                case InputKey.ControllerRDown:      // A（面板激活/点击）
                case InputKey.ControllerLUp:        // 十字键 ↑（面板移动焦点）
                case InputKey.ControllerLDown:      // 十字键 ↓
                case InputKey.ControllerLLeft:      // 十字键 ←
                case InputKey.ControllerLRight:     // 十字键 →
                case InputKey.ControllerLBumper:    // LB（完整模式翻页滚动）
                case InputKey.ControllerRBumper:    // RB
                case InputKey.ControllerRRight:     // B（关闭面板）
                    return true;
                default:
                    return false;
            }
        }

        [HarmonyPrefix]
        public static bool Prefix(GameKey __instance, ref bool __result)
        {
            // GroupId 精准门控（①）：只拦战斗分类——防误伤 Generic.Leave(B)/地图/面板的同键 GameKey；
            // IM 关 / 非 Mission → 自然放行。
            if (ImChatView.IsOpen
                && Mission.Current != null
                && __instance.GroupId == CombatHotKeyCategoryId
                && __instance.ControllerKey != null
                && IsPanelKey(__instance.ControllerKey.InputKey))
            {
                __result = false;
                return false;
            }
            // ESC 模型（②）：IM 打开时 B 单发——B 全分类吞掉（Tick 已消费关面板），
            // 同一次按下不许漏到底层（Generic.Leave=4 离开 mission / MissionOrder 选 3 等）
            if (ImChatView.IsOpen
                && Mission.Current != null
                && __instance.ControllerKey != null
                && __instance.ControllerKey.InputKey == InputKey.ControllerRRight)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
