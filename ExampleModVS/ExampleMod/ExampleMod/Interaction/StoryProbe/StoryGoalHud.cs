using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 序章常驻任务卡的 ViewModel（2026-11-13）。
    /// 形态：任务名（金色）+ 分隔线 + 目标行（+ 可选进度）。
    /// 位置（右上 / 左上）由 StoryProbeTuning.json 的 ScreenGoal.Position 决定，这里只吃结果。
    /// </summary>
    public class StoryGoalHudVM : ViewModel
    {
        private bool _isVisible;
        private bool _showRight = true;
        private bool _showLeft;
        private int _cardWidth = 380;
        private int _sideMargin = 24;
        private int _topMargin = 18;
        private string _title = "";
        private string _objective = "";
        private string _progress = "";
        private bool _showProgress;
        private string _titleColor = "#E8C55AFF";
        private string _progressColor = "#B9A67AFF";

        [DataSourceProperty]
        public bool IsVisible
        {
            get => _isVisible;
            set { if (_isVisible != value) { _isVisible = value; OnPropertyChanged(nameof(IsVisible)); } }
        }

        [DataSourceProperty]
        public bool ShowRight
        {
            get => _showRight;
            set { if (_showRight != value) { _showRight = value; OnPropertyChanged(nameof(ShowRight)); } }
        }

        [DataSourceProperty]
        public bool ShowLeft
        {
            get => _showLeft;
            set { if (_showLeft != value) { _showLeft = value; OnPropertyChanged(nameof(ShowLeft)); } }
        }

        [DataSourceProperty]
        public int CardWidth
        {
            get => _cardWidth;
            set { if (_cardWidth != value) { _cardWidth = value; OnPropertyChanged(nameof(CardWidth)); } }
        }

        [DataSourceProperty]
        public int SideMargin
        {
            get => _sideMargin;
            set { if (_sideMargin != value) { _sideMargin = value; OnPropertyChanged(nameof(SideMargin)); } }
        }

        [DataSourceProperty]
        public int TopMargin
        {
            get => _topMargin;
            set { if (_topMargin != value) { _topMargin = value; OnPropertyChanged(nameof(TopMargin)); } }
        }

        [DataSourceProperty]
        public string Title
        {
            get => _title;
            set { if (_title != value) { _title = value; OnPropertyChanged(nameof(Title)); } }
        }

        [DataSourceProperty]
        public string Objective
        {
            get => _objective;
            set { if (_objective != value) { _objective = value; OnPropertyChanged(nameof(Objective)); } }
        }

        [DataSourceProperty]
        public string Progress
        {
            get => _progress;
            set { if (_progress != value) { _progress = value; OnPropertyChanged(nameof(Progress)); } }
        }

        [DataSourceProperty]
        public bool ShowProgress
        {
            get => _showProgress;
            set { if (_showProgress != value) { _showProgress = value; OnPropertyChanged(nameof(ShowProgress)); } }
        }

        [DataSourceProperty]
        public string TitleColor
        {
            get => _titleColor;
            set { if (_titleColor != value) { _titleColor = value; OnPropertyChanged(nameof(TitleColor)); } }
        }

        [DataSourceProperty]
        public string ProgressColor
        {
            get => _progressColor;
            set { if (_progressColor != value) { _progressColor = value; OnPropertyChanged(nameof(ProgressColor)); } }
        }
    }

    /// <summary>
    /// 场景内常驻任务卡（Mission 层，纯显示）。
    /// 🔴 只在序章 Active 时出现；对话/菜单打开时由原版 UI 覆盖，不需要额外处理。
    /// 数据：目标文本仍走 <see cref="LWNScreenGoalText.Resolve"/>（单一出口），本类只负责"摆在哪、什么时候刷新"。
    /// </summary>
    public class StoryGoalHudMissionView : MissionView
    {
        private const int LayerOrder = 6;   // AgentHud=5 之上、InteractArea=10 之下

        /// <summary>调试覆盖：null = 跟随序章 Active（正常）。`custom.story_probe hud on|off|auto` 改这里。</summary>
        public static bool? ForceVisible;

        /// <summary>调试覆盖：null = 用 JSON 配置。`custom.story_probe hud left|right` 改这里。</summary>
        public static string ForcePosition;

        public static string LastPosition { get; private set; } = "top-right";
        public static bool LastVisible { get; private set; }

        private StoryGoalHudVM _vm;
        private GauntletLayer _layer;
        private ScreenGoalTuning _cfg = new ScreenGoalTuning();

        private float _pollTimer;
        private float _flashTimer;
        private string _lastObjective;
        private bool _forceRefresh;

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();

            try
            {
                var cfg = StoryProbeTuning.Current?.ScreenGoal;
                if (cfg != null) _cfg = cfg;
                if (!_cfg.Enabled) return;

                _vm = new StoryGoalHudVM
                {
                    CardWidth = _cfg.CardWidth,
                    SideMargin = _cfg.SideMargin,
                    TopMargin = _cfg.TopMargin,
                };

                string pos = (ForcePosition ?? _cfg.Position ?? "top-right").Trim().ToLowerInvariant();
                bool left = pos == "top-left" || pos == "topleft";
                _vm.ShowLeft = left;
                _vm.ShowRight = !left;
                LastPosition = left ? "top-left" : "top-right";
                _vm.IsVisible = false;

                _layer = V.NewLayer(LayerOrder, "StoryGoalLayer");
                _layer.LoadMovie("StoryGoalHud", _vm);
                (ScreenManager.TopScreen as MissionScreen)?.AddLayer(_layer);

                _forceRefresh = true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryGoalHud] init failed: {ex.Message}");
            }
        }

        public override void OnMissionTick(float dt)
        {
            if (_vm == null) return;

            try
            {
                var host = StoryProbeMissionBehavior.Current;
                bool show = ForceVisible ?? (host != null && host.Active);
                if (ForcePosition != null) ApplyPosition(ForcePosition);
                _vm.IsVisible = show;
                LastVisible = show;
                if (!show) return;

                _pollTimer += dt;
                if (_forceRefresh || _pollTimer >= Math.Max(0.05f, _cfg.PollSec))
                {
                    _pollTimer = 0f;
                    _forceRefresh = false;
                    RefreshGoal();
                }

                TickFlash(dt);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryGoalHud] tick failed: {ex.Message}");
                _vm = null;   // 出错就停，不要每帧刷日志
            }
        }

        /// <summary>把卡片摆到左/右上（两套镜像容器切 IsVisible，不依赖屏幕宽度）。</summary>
        private void ApplyPosition(string pos)
        {
            if (_vm == null) return;
            string p = (pos ?? "top-right").Trim().ToLowerInvariant();
            bool left = p == "top-left" || p == "topleft";
            if (_vm.ShowLeft == left && _vm.ShowRight == !left) return;
            _vm.ShowLeft = left;
            _vm.ShowRight = !left;
            LastPosition = left ? "top-left" : "top-right";
        }

        private void RefreshGoal()
        {            string objective = LWNScreenGoalText.Resolve();
            if (objective == _lastObjective) return;

            _lastObjective = objective;
            _vm.Title = LWNTextHelper.ResolveText("LWN_story_hud_title", "Get inside");
            _vm.Objective = objective;
            _flashTimer = Math.Max(0f, _cfg.FlashSec);
            ApplyFlash(true);
        }

        private void TickFlash(float dt)
        {
            if (_flashTimer <= 0f) return;
            _flashTimer -= dt;
            ApplyFlash(_flashTimer > 0f);
        }

        private void ApplyFlash(bool on)
        {
            _vm.TitleColor = on ? "#FFF3B0FF" : "#E8C55AFF";
            _vm.ProgressColor = on ? "#FFF3B0FF" : "#B9A67AFF";
        }

        public override void OnMissionScreenFinalize()
        {
            try
            {
                if (_layer != null)
                {
                    (ScreenManager.TopScreen as MissionScreen)?.RemoveLayer(_layer);
                    _layer = null;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryGoalHud] finalize failed: {ex.Message}");
            }
            _vm = null;
            base.OnMissionScreenFinalize();
        }
    }
}
