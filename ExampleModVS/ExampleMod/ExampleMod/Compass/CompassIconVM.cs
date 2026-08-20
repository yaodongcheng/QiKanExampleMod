using System;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 罗盘带上的任务人物图标条目（原版 Alt 金色 ! 图标 + 距离文本）。
    /// 由 CompassHud 每 2 帧注入位置/距离；持有 TargetAgent 供扫描更新复用（AgentHudVM 同款模式）。
    /// </summary>
    public class CompassIconVM : ViewModel
    {
        private float _posX;
        private bool _isVisible;
        private string _distanceText;
        private int _iconType;

        /// <summary>图标对应的 Agent（已按距离近优先排序的 quest 人物）。</summary>
        public Agent TargetAgent { get; private set; }

        public CompassIconVM(Agent agent, int iconType)
        {
            TargetAgent = agent;
            IconType = iconType;
            // 距离文本初始值非 null（防绑定崩溃；首帧由 CompassHud 刷新）
            DistanceText = "0m";
            IsVisible = false;
            PosX = 0f;
        }

        /// <summary>由 CompassHud 注入：带内 x 坐标（中心对齐偏移已计入）+ 可见性。</summary>
        public void SetCompass(float posX, bool visible, string distanceText)
        {
            PosX = posX;
            if (visible != IsVisible) IsVisible = visible;
            if (distanceText != null && distanceText != _distanceText) DistanceText = distanceText;
        }

        [DataSourceProperty]
        public float PosX
        {
            get => _posX;
            set { if (Math.Abs(value - _posX) > 0.01f) { _posX = value; OnPropertyChangedWithValue(value, "PosX"); } }
        }

        [DataSourceProperty]
        public bool IsVisible
        {
            get => _isVisible;
            set { if (value != _isVisible) { _isVisible = value; OnPropertyChangedWithValue(value, "IsVisible"); } }
        }

        [DataSourceProperty]
        public string DistanceText
        {
            get => _distanceText;
            set { if (value != _distanceText) { _distanceText = value; OnPropertyChangedWithValue(value, "DistanceText"); } }
        }

        /// <summary>图标类型（int → QuestMarkerBrushWidget.QuestMarkerType；单一位：
        /// 16=TrackedStoryQuest > 8=TrackedIssue > 4=ActiveStoryQuest > 2=ActiveIssue > 1=AvailableIssue）。</summary>
        [DataSourceProperty]
        public int IconType
        {
            get => _iconType;
            set { if (value != _iconType) { _iconType = value; OnPropertyChangedWithValue(value, "IconType"); } }
        }
    }
}
