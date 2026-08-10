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
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            // 🔴 O 只负责「打开」：面板开着时输入 o 不触发任何动作（打字不误关）；
            // 关闭走 ESC / 手柄 B / 关闭按钮 / 面板外点击（ImChatView.Tick 内统一处理）
            if (ModInput.ShortFired(InteractionIds.IM) && !ImChatView.IsOpen)
                ImChatView.Open();

            ImChatView.Tick(dt);
        }

        public override void OnMissionScreenFinalize()
        {
            // 防 ESC 退 Mission 泄漏 layer
            ImChatView.Close();
            base.OnMissionScreenFinalize();
        }
    }
}
