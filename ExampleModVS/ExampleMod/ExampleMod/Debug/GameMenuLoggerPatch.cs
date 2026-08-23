using HarmonyLib;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 把玩家在「游戏菜单」（定居点/城镇/村庄/据点、遭遇、等待菜单等）里实际看到和点到的东西写进 DebugLogger。
    ///
    /// 日志标签：
    ///   [GameMenu] ▶ 打开   — 菜单显示出来（标题 + 正文 + 全部可见选项 + 是否可点 + 灰掉的原因）
    ///   [GameMenu] ● 点击   — 玩家选了哪个选项（含 ESC/快捷键/自动选第一项，全部走同一个引擎入口）
    ///   [GameMenu] ◀ 关闭   — 菜单界面被销毁（回大地图 / 进任务场景）
    ///
    /// 数据全部取自 ViewModel 层（GameMenuVM/GameMenuItemVM），即"屏幕上真正渲染的字符串"，
    /// 不是重算一遍条件——所见即所记。
    /// </summary>
    internal static class GameMenuLogger
    {
        /// <summary>
        /// 🔴 2026-08-23（用户反馈：定居点菜单内手柄 ↑ 键帽仍显示）：菜单显示状态权威标志。
        /// 背景：GameMenu（定居点/城镇/村庄菜单）是 MapScreen 上的覆盖 UI——**不是独立 Screen**，
        /// 实机日志实锤「[ImChatOpenButton] 挂载 TopScreen=MapScreen」与「[GameMenu] ▶ 打开」同帧，
        /// TopScreen 类名判定（UiFullScreenHelper 原实现）永远 false。
        /// 本标志由 GameMenuVM 生命周期补丁维护：Refresh（菜单显示）置 true / OnFinalize（销毁）置 false——
        /// 与日志同源同补丁（三版本已实锤生效），UiFullScreenHelper.IsGameMenuOpen 读取。
        /// </summary>
        public static bool IsActive { get; internal set; }

        // 上一次已记录的快照指纹：菜单ID | 标题 | 各选项(id+文字)
        private static string _lastSignature;

        // 上一次已记录的菜单 StringId + 标题，用于每帧廉价短路（避免每帧拼整个指纹）。
        // 标题也算进来：有补丁在 OnFrameTick 里改写标题时（扣押接管菜单），
        // 光比 StringId 会把这次改写漏掉。
        private static string _lastMenuId;
        private static string _lastTitle;

        // Refresh() 之后置位，等 OnFrameTick() 算完正文再落盘
        private static bool _pending;

        public static void MarkDirty()
        {
            _pending = true;
        }

        public static void Reset()
        {
            _lastSignature = null;
            _lastMenuId = null;
            _lastTitle = null;
            _pending = false;
        }

        /// <summary>
        /// 在 GameMenuVM.OnFrameTick 之后调用：此时 TitleText / ContextText / ItemList 全部已是当帧显示值。
        /// 只有指纹变化时才写日志，等待菜单的每帧刷新不会刷屏。
        /// </summary>
        public static void TryLogSnapshot(GameMenuVM vm)
        {
            MenuContext menuContext = vm?.MenuContext;
            GameMenu gameMenu = menuContext?.GameMenu;
            if (gameMenu == null) return;

            // 廉价短路：没被标脏且菜单+标题都没变 → 直接返回
            if (!_pending && gameMenu.StringId == _lastMenuId && vm.TitleText == _lastTitle) return;

            string signature = BuildSignature(vm, gameMenu);
            if (signature == _lastSignature)
            {
                _pending = false;
                _lastMenuId = gameMenu.StringId;
                _lastTitle = vm.TitleText;
                return;
            }

            _lastSignature = signature;
            _lastMenuId = gameMenu.StringId;
            _lastTitle = vm.TitleText;
            _pending = false;

            var sb = new StringBuilder();
            sb.Append($"[GameMenu] ▶ 打开 menu={gameMenu.StringId}{DescribeContext()}");
            sb.AppendLine();
            sb.AppendLine($"    标题: {Flatten(vm.TitleText)}");
            sb.AppendLine($"    正文: {Flatten(vm.ContextText)}");

            if (vm.ItemList == null || vm.ItemList.Count == 0)
            {
                sb.Append("    选项: (无)");
            }
            else
            {
                sb.AppendLine("    选项:");
                int n = 0;
                foreach (GameMenuItemVM item in vm.ItemList)
                {
                    n++;
                    string text = Flatten(item.Item);
                    string id = string.IsNullOrEmpty(item.OptionID) ? "?" : item.OptionID;
                    string state = item.IsEnabled ? "可点" : "灰掉";
                    string reason = item.IsEnabled ? "" : DescribeDisabledReason(item);
                    sb.AppendLine($"      {n}. {text}  [id={id}] {state}{reason}");
                }
                // 去掉最后一个换行，DebugLogger 自己会补
                sb.Length -= System.Environment.NewLine.Length;
            }

            DebugLogger.Log(sb.ToString());
        }

        public static void LogClose(GameMenuVM vm)
        {
            GameMenu gameMenu = vm?.MenuContext?.GameMenu;
            DebugLogger.Log($"[GameMenu] ◀ 关闭 menu={gameMenu?.StringId ?? "?"}{DescribeContext()}");
            Reset();
        }

        public static void LogSelection(GameMenuOption option, MenuContext menuContext)
        {
            string menuId = menuContext?.GameMenu?.StringId ?? "?";
            string id = string.IsNullOrEmpty(option.IdString) ? "?" : option.IdString;
            string text = Flatten(option.Text?.ToString());
            DebugLogger.Log($"[GameMenu] ● 点击 menu={menuId} 选项=\"{text}\" [id={id}]{DescribeContext()}");
        }

        private static string BuildSignature(GameMenuVM vm, GameMenu gameMenu)
        {
            var sb = new StringBuilder();
            sb.Append(gameMenu.StringId).Append('|').Append(vm.TitleText);
            if (vm.ItemList != null)
            {
                foreach (GameMenuItemVM item in vm.ItemList)
                {
                    sb.Append('|').Append(item.OptionID).Append('#').Append(item.Item);
                }
            }
            return sb.ToString();
        }

        /// <summary>选项被灰掉时，引擎把原因写在 tooltip 里（MenuCallbackArgs.IsEnabled=false 的同时设 Tooltip）。</summary>
        private static string DescribeDisabledReason(GameMenuItemVM item)
        {
            TaleWorlds.Localization.TextObject hint = item.ItemHint?.HintText;
            if (hint == null) return "";
            string reason = Flatten(hint.ToString());
            return string.IsNullOrEmpty(reason) ? "" : $" — {reason}";
        }

        /// <summary>补一句当前定居点/队伍上下文，方便回放时定位。</summary>
        private static string DescribeContext()
        {
            Settlement settlement = Settlement.CurrentSettlement;
            if (settlement == null) return "";

            string kind = settlement.IsTown ? "城镇"
                : settlement.IsVillage ? "村庄"
                : settlement.IsCastle ? "城堡"
                : settlement.IsHideout ? "匪巢"
                : "定居点";
            return $" @{settlement.Name}({kind})";
        }

        /// <summary>菜单正文含换行，压成单行免得把日志撑散。</summary>
        private static string Flatten(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r\n", " ⏎ ").Replace("\n", " ⏎ ").Replace("\r", " ⏎ ").Trim();
        }
    }

    /// <summary>Refresh(true) 会重建 ItemList，但正文要等下一帧才算出来 → 这里只标脏。
    /// 🔴 2026-08-23：同时置位 <see cref="GameMenuLogger.IsActive"/>（菜单显示 = Refresh 驱动，日志实锤）。</summary>
    [HarmonyPatch(typeof(GameMenuVM), nameof(GameMenuVM.Refresh))]
    public static class GameMenuVMRefreshLoggerPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try { GameMenuLogger.MarkDirty(); GameMenuLogger.IsActive = true; }
            catch { /* 日志系统绝不能影响游戏正常运行 */ }
        }
    }

    /// <summary>
    /// OnFrameTick 之后 TitleText/ContextText/ItemList 都是当帧显示值，在这里落盘。
    /// Priority.Last：排在所有会改写菜单文案的补丁（如 DetentionMenuTextPatch）之后，
    /// 保证日志记下的是玩家真正看到的字，而不是被覆盖前的原版文案。
    /// </summary>
    [HarmonyPatch(typeof(GameMenuVM), nameof(GameMenuVM.OnFrameTick))]
    public static class GameMenuVMFrameTickLoggerPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(GameMenuVM __instance)
        {
            try { GameMenuLogger.TryLogSnapshot(__instance); }
            catch { /* 日志系统绝不能影响游戏正常运行 */ }
        }
    }

    /// <summary>菜单界面销毁（回大地图 / 进场景）。
    /// 🔴 2026-08-23：同时清 <see cref="GameMenuLogger.IsActive"/>（菜单销毁 = OnFinalize，唯一关闭沿）。</summary>
    [HarmonyPatch(typeof(GameMenuVM), nameof(GameMenuVM.OnFinalize))]
    public static class GameMenuVMFinalizeLoggerPatch
    {
        [HarmonyPrefix]
        public static void Prefix(GameMenuVM __instance)
        {
            try { GameMenuLogger.LogClose(__instance); GameMenuLogger.IsActive = false; }
            catch { /* 日志系统绝不能影响游戏正常运行 */ }
        }
    }

    /// <summary>
    /// GameMenuOption.RunConsequence 是所有"选项被触发"的唯一收口：
    /// 鼠标点击 / 手柄 / 快捷键 / ESC 走 leave 选项 / AutoSelectFirst 全部经过这里。
    /// </summary>
    [HarmonyPatch(typeof(GameMenuOption), nameof(GameMenuOption.RunConsequence))]
    public static class GameMenuOptionSelectionLoggerPatch
    {
        [HarmonyPrefix]
        public static void Prefix(GameMenuOption __instance, MenuContext menuContext)
        {
            try { GameMenuLogger.LogSelection(__instance, menuContext); }
            catch { /* 日志系统绝不能影响游戏正常运行 */ }
        }
    }
}
