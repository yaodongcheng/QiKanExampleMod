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

            // 🔴 M/↑ 只负责「打开」：面板开着时输入不触发任何动作（打字不误关）；
            // 关闭走 ESC / 手柄 B / 关闭按钮 / 面板外点击（ImChatView.Tick 内统一处理）
            // 🔴 2026-08-15（用户裁定）：MCM 密聊开关（PlotEnabled）关闭 → 无法呼出聊天
            // 🔴 2026-08-17（Q4 手柄）：↑ 十字 = 手柄呼出键（与键盘同玩法行，ModInput）
            // 🔴 2026-08-17（实机「Mission 内无法呼出」）：OpenOrExpand——缩略开着时 = 放大为完整模式
            // 🔴 2026-08-22（用户裁定分层）：未配置 LLM → 无法呼出（传讯入口整体封死，同 Campaign 侧）
            // 🔴 2026-08-23（用户裁定：键盘 O→M，短按保持；GameMenu 内手柄屏蔽走 CanOpen）：
            // Mission 内无 GameMenu，本处不需要设备判定
            if (ModInput.ShortFired(InteractionIds.IM) && Settings.Instance.PlotEnabled && Settings.Instance.IsLLMConfigured)
                ImChatView.OpenOrExpand();

            // 🔴 2026-08-23：呼出按钮驱动已统一迁往 InteractionMissionView.OnMissionTick（与 InteractArea
            // 同 tick，玩家认知 = 右侧交互面板）；Mission ESC 期间由 ImChatView.OnScreenFrameTick
            //（MissionScreen.OnFrameTick 补丁，UI 层暂停也触发）兜底。本处不再驱动。

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
