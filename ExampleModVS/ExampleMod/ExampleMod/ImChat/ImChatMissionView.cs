using TaleWorlds.MountAndBlade.View.MissionViews;

namespace LivingWorldNpcs
{
    /// <summary>
    /// IM 的 Mission 侧驱动：每帧 tick（回复管线 + 命令流 + UI 刷新）与热键检测。
    /// UI 层本身走 TopScreen.AddLayer（NinjaNotification 同款），本 View 只负责驱动与兜底清理。
    /// </summary>
    public class ImChatMissionView : MissionView
    {
        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();
            ImChatView.EnsureSubscribed();
            // 🔴 2026-08-17（用户裁定：进/出 Mission 两边都关）：进入 Mission 最早期关闭 IM 面板——
            // 大世界开着完整/缩略面板进 Mission → MissionScreen Push 全屏盖住面板（层挂 MapScreen 不可见
            // 但 IsOpen=true）→ 呼出入口全被挡（实机「Mission 内无法呼出」）。在 Initialize 就关掉，
            // 比 Tick 内 CheckMissionBoundary（第一帧才生效）更早；退出侧 OnMissionScreenFinalize 已有 Close。
            ImChatView.Close();
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            // 🔴 O 只负责「打开」：面板开着时输入 o 不触发任何动作（打字不误关）；
            // 关闭走 ESC / 手柄 B / 关闭按钮 / 面板外点击（ImChatView.Tick 内统一处理）
            // 🔴 2026-08-15（用户裁定）：MCM 密聊开关（PlotEnabled）关闭 → O 无法呼出聊天
            // 🔴 2026-08-17（Q4 手柄）：↑ 十字短按 = 手柄呼出键（与 O 完全等价，ModInput 玩法行）
            // 🔴 2026-08-17（实机「Mission 内无法呼出」）：OpenOrExpand——缩略开着时 = 放大为完整模式
            if (ModInput.ShortFired(InteractionIds.IM) && Settings.Instance.PlotEnabled)
                ImChatView.OpenOrExpand();

            // 🔴 Q5（2026-08-17 呼出按钮）：Mission 侧驱动（Campaign 侧由 ImChatView.OnScreenFrameTick 驱动）
            ImChatOpenButtonManager.Tick(dt);

            ImChatView.Tick(dt);
        }

        public override void OnMissionScreenFinalize()
        {
            // 防 ESC 退 Mission 泄漏 layer
            ImChatView.Close();
            ImChatOpenButtonManager.Close();
            base.OnMissionScreenFinalize();
        }
    }
}
