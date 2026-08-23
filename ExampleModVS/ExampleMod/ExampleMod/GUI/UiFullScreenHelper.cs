using System;
using TaleWorlds.Engine.GauntletUI;
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
    }
}
