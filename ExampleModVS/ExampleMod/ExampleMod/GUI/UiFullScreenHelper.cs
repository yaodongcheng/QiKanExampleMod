using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 2026-08-22：全屏 UI 统一检测（个人技能 / 背包 / 队伍 / 家族 / 王国 / 任务等原版全屏界面）。
    /// 常驻层（ImChatOpenButton 呼出按钮层等）在玩家打开全屏 UI 时必须隐藏——按钮穿透到全屏界面
    /// 之上既挡操作又出戏。
    /// 判定 = ScreenManager.TopScreen 类型名 Contains 匹配（ModInput.IsSystemModalActive 同款模式：
    /// 不引引擎 Screen 类型强引用，跨版本类名变体用 Contains 覆盖，漏判最坏只是多显一次，不会崩）。
    /// </summary>
    public static class UiFullScreenHelper
    {
        /// <summary>全屏 UI 屏名特征（SandBox.GauntletUI.dll 实锤类名 GauntletXxxScreen；
        /// Contains 覆盖各版本命名变体，禁止精确全名匹配）。
        /// 🔴 2026-08-23（实机日志定位）：个人技能屏实机类名 GauntletCharacterDeveloperScreen
        ///（不含 "CharacterScreen" 子串）→ 补 "CharacterDeveloperScreen"；ESC 选项屏
        /// GauntletOptionsScreen 同样漏网 → 补 "OptionsScreen"。</summary>
        private static readonly string[] FullScreenUiMarkers =
        {
            "CharacterScreen",          // 个人技能变体（部分版本命名）
            "CharacterDeveloperScreen", // 个人技能（GauntletCharacterDeveloperScreen，实机 2026-08-23）
            "InventoryScreen",      // 背包
            "PartyScreen",          // 队伍（GauntletPartyScreen）
            "ClanScreen",           // 家族（GauntletClanScreen）
            "KingdomScreen",        // 王国
            "QuestsScreen",         // 任务
            "EncyclopediaScreen",   // 百科全书（全屏覆盖）
            "CraftingScreen",       // 锻造
            "BannerEditorScreen",   // 旗帜编辑（新游戏流程）
            "TournamentScreen",     // 锦标赛报名
            "OptionsScreen",        // 选项/设置（GauntletOptionsScreen，ESC 打开，实机 2026-08-23）
        };

        /// <summary>当前是否打开了全屏 UI（TopScreen 是上述任一屏）。</summary>
        public static bool IsFullScreenUiOpen()
        {
            var top = ScreenManager.TopScreen;
            if (top == null) return false;
            string n = top.GetType().Name;
            for (int i = 0; i < FullScreenUiMarkers.Length; i++)
            {
                if (n.IndexOf(FullScreenUiMarkers[i], StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// ESC 菜单层是否打开（层存在 = 打开，RemoveLayer = 关闭）。两条路径反编译实锤（2026-08-23）：
        /// · Mission 内 = <c>MissionEscapeMenu</c> 层——MissionGauntletEscapeMenuBase（MissionBehavior）
        ///   ViewOrderPriority=50 建层挂 MissionScreen；🔴 50 &lt; IM 呼出按钮层 350 → ESC 打开时按钮
        ///   穿透显示且可点，必须显式判定。
        /// · Campaign = <c>MapEscapeMenu</c> 层——GauntletMapEscapeMenuView（MapView）层序 4400，
        ///   天然压盖 350，此判定仅为保险（防版本变体/层序调整）。
        /// 🔴 其他屏（教育/捏脸/旗帜等）的 ESC 是「屏自己的 GauntletLayer 里 LoadMovie("EscapeMenu")」，
        /// 无独立层——但它们已被 IsFullScreenUiOpen 遮住，不在本判定范围。
        /// 性能：TopScreen 层数 MapScreen ~9 / MissionScreen ~5，每层一次短字符串 IndexOf，每帧一次 &lt; 1μs，
        /// 可忽略（ShouldShow 本就每帧调用）。</summary>
        public static bool IsEscapeMenuOpen()
        {
            var top = ScreenManager.TopScreen;
            if (top == null) return false;
            for (int i = 0; i < top.Layers.Count; i++)
            {
                var layer = top.Layers[i];
                if (layer is GauntletLayer gl && gl.Name != null
                    && gl.Name.IndexOf("EscapeMenu", StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 🔴 2026-08-23（用户反馈：定居点菜单内手柄 ↑ 键帽仍显示）：定居点菜单（城镇/村庄/城堡
        /// GameMenu）是否打开。手柄 ↑/↓ 在该菜单导航选项——IM 呼出键（手柄 ↑）在此屏蔽
        /// （键帽隐藏 + CanOpen false）；键盘 M 无原版冲突不受影响。
        /// 🔴 判定方式（实机日志实锤）：**不能用 TopScreen 类名**——GameMenu 是 MapScreen 上的
        /// 覆盖 UI，不是独立 Screen，菜单打开时 TopScreen 恒为 MapScreen（日志：挂载 TopScreen=
        /// MapScreen 与 [GameMenu] ▶ 打开 同帧）。权威信号 = GameMenuVM 生命周期标志
        /// （GameMenuLogger.IsActive：Refresh 置 true / OnFinalize 清 false，同源补丁三版本已实锤）。
        /// 🔴 模式守卫（用户裁定 2026-08-23）：**GameMenu 只存在于 Campaign 模式**（Mission 模式下
        /// 不可能出现，战斗结算/等待菜单也是 Mission 结束回 Campaign 层才打开）——Mission 内恒
        /// false，↑ 呼出完全不受影响（不依赖「Mission 内无 GameMenuVM」推断——static 标志万一
        /// 残留卡 true 会误杀 Mission 内呼出，模式守卫从根上关死）。
        /// </summary>
        public static bool IsGameMenuOpen()
        {
            if (Mission.Current != null) return false;   // Mission 模式无 GameMenu（用户裁定）
            return GameMenuLogger.IsActive;
        }
    }
}
