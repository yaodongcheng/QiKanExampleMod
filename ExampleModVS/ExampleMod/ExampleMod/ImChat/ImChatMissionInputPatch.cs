using HarmonyLib;
using TaleWorlds.InputSystem;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 2026-08-19（用户裁定：Mission 内 IM 面板占用 A/十字键/LB/RB/B 时，输入只归面板，
    /// 禁止漏到玩法动作——实机：缩略 IM 聚焦发送按钮按 A 角色跳跃、按 B 关面板同时弹「离开战场」）：
    /// 拦截 vanilla mission 输入。两个补丁（IsDown + IsPressed）共用 ShouldBlock 门控。
    /// 机制（反编译实锤）：
    /// - MissionMainAgentController（TaleWorlds.MountAndBlade.View.dll 23174 行起）在 OnPreMissionTick
    ///   里用 `base.Input.IsGameKeyDown(id)` / `IsGameKeyPressed(id)` 收集玩家动作输入——
    ///   Jump(14)=A 走 **IsGameKeyPressed（按下沿，24128 行 EventControlFlag|=8）**、
    ///   Crouch(15)=十字键下、ViewCharacter(25)=十字键左、Cheer(31)=十字键上、PushToTalk(33)=十字键右；
    ///   手柄键位可被玩家在设置里改绑（BannerlordGameKeys.xml 覆盖 GameKey.ControllerKey）
    /// - Mission.OnTick（TaleWorlds.MountAndBlade.dll 55953 行）友好 mission 里
    ///   `InputManager.IsGameKeyDown(4)` = Generic "Leave"（Tab+B）→ OnEndMissionRequest——
    ///   B 关面板的同一按会同时弹离开确认（实机）
    /// - Input.IsGameKeyDown/IsGameKeyPressed → GameKey.IsDown/IsPressed（TaleWorlds.InputSystem.dll）
    ///   = 键盘/手柄键池统一判定（KeyboardKey || ControllerKey）——任何设备（含 native 手柄映射）
    ///   都走这里（与 ImChatMapInputPatch 同结论）；层 mask 无手柄位（InputUsageMask 只有
    ///   Mouse/Keyboard），吞键只能在这里
    /// - GameKey.ControllerKey 反映运行时改绑——按物理键（ControllerKey.InputKey）判拦 =
    ///   玩家把 A/十字键改绑到任何动作（Attack/骑马/跳跃…）都不漏
    /// 处置：两档拦（IsDown + IsPressed 同逻辑）：
    ///   ① 战斗分类（CombatHotKeyCategory）且 ControllerKey ∈ {A, 十字键 4 向, LB, RB, B}
    ///      → false，**仅面板占用态**（ImChatView.IsPanelKeyOwner——缩略聚焦/输入框聚焦/完整模态；
    ///      缩略无焦点 = 半模态玩态，A 还给游戏跳跃）
    ///   ② ESC 模型：IM 打开时 B 单发——B 全分类吞掉（Tick 已消费关面板），同一次按下
    ///      不许漏到任意分类的 B GameKey（Generic.Leave=4 离开 mission / MissionOrder 选 3 等）
    /// 不拦：左/右摇杆（移动/镜头）、扳机（攻击/格挡）、Y（互动/上马）、X（踢）——
    /// 缩略半模态岛设计（玩家继续操作角色），面板只认 A/十字键/LB/RB/B。
    /// ⚠️ 与 ImChatMapInputPatch 同目标方法双 Prefix——条件互斥（地图 vs 战斗分类），
    /// Harmony 顺序执行各自判 false，无冲突。
    /// </summary>
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

        /// <summary>统一门控（IsDown / IsPressed 两补丁共用）。</summary>
        private static bool ShouldBlock(GameKey key)
        {
            if (!ImChatView.IsOpen || Mission.Current == null) return false;
            // ② ESC 模型：B 全分类吞——IM 打开时 B 只关聊天（Tick 消费），任何聚焦态都吞
            if (key.ControllerKey != null && key.ControllerKey.InputKey == InputKey.ControllerRRight)
                return true;
            // ① 战斗分类 + 面板键：仅面板占用态才拦——缩略无焦点（半模态玩态）时 A/D-pad 还给游戏
            if (!ImChatView.IsPanelKeyOwner) return false;
            return key.GroupId == CombatHotKeyCategoryId
                && key.ControllerKey != null
                && IsPanelKey(key.ControllerKey.InputKey);
        }

        /// <summary>按住态漏斗（MissionMainAgentController IsGameKeyDown：Crouch/ViewCharacter/水上 Jump 等）。</summary>
        [HarmonyPatch(typeof(GameKey), "IsDown")]
        public static class PatchIsDown
        {
            [HarmonyPrefix]
            public static bool Prefix(GameKey __instance, ref bool __result)
            {
                if (ShouldBlock(__instance))
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// 🔴 2026-08-19（实机补漏：缩略 IM 聚焦发送按钮按 A 角色仍跳跃）——跳跃走
        /// IsGameKeyPressed(14)（MissionMainAgentController 24128 行，按下沿 EventControlFlag|=8），
        /// 只拦 IsDown 拦不住 A 跳跃。按下沿漏斗必须同逻辑补丁。
        /// </summary>
        [HarmonyPatch(typeof(GameKey), "IsPressed")]
        public static class PatchIsPressed
        {
            [HarmonyPrefix]
            public static bool Prefix(GameKey __instance, ref bool __result)
            {
                if (ShouldBlock(__instance))
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }
    }
}
