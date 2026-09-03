using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Mission 侧性能面板宿主（层序 30：AgentHud 5 / Compass 8 / InteractArea 10 / 偷窃 16 之上，
    /// ESC 层 50 之下——暂停菜单打开时被原生 ESC 菜单自然覆盖）。
    /// 挂载在 MySubModule.OnMissionBehaviorInitialize 双闸门【之前】（不依赖 campaign API，
    /// 纯显示安全）——战场/自定义战斗（无 campaign）也能显示帧率。
    /// Mission 由本 MissionView 生命周期承接；Campaign 由 PerfHudManager（ScreenFrameTick）承接，互斥。
    /// 🔴 挂载模式 = AgentHudMissionView 同款（ScreenManager.TopScreen as MissionScreen——1.4.8 反编译
    /// MissionView.MissionScreen 是 internal set，时机不保证；TopScreen 实测可靠）；全文 try/catch，
    /// 任何失败落 [PerfHud] 日志（实机 2026-09-03：mission 面板无挂载日志 = OnMissionTick 早期抛）。
    /// </summary>
    public class PerfHudMissionView : MissionView
    {
        public static PerfHudMissionView Instance;

        private const int LayerOrder = 30;
        private PerfHudVM _vm;
        private GauntletLayer _layer;
        private float _refreshTimer;
        private bool _initLogged;

        public override void OnMissionScreenInitialize()
        {
            Instance = this;
            base.OnMissionScreenInitialize();

            // 立即尝试挂载（开关已开时首帧即面板）；开关中途开由 OnMissionTick 补 Mount
            try
            {
                if (!_initLogged)
                {
                    _initLogged = true;
                    DebugLogger.Log($"[PerfHud] mission view initialized (Top={ScreenManager.TopScreen?.GetType().Name ?? "null"})");
                }
                if (AnyPanelOn()) TryMount();
            }
            catch (Exception ex) { DebugLogger.Log($"[PerfHud] initialize 失败: {ex.Message}"); }
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            try
            {
                bool anyOn = AnyPanelOn();
                PerfWrapper.Enabled = Settings.Instance.ShowPerfSampler;

                if (anyOn)
                {
                    if (_layer == null) TryMount();
                    if (_layer == null) return;

                    // B 层包裹（幂等；内部节流扫描）
                    try { PerfWrapper.Tick(); } catch { }

                    _refreshTimer += dt;
                    if (_vm != null && _refreshTimer >= 0.5f)
                    {
                        _refreshTimer = 0f;
                        _vm.IsVisible = true;
                        _vm.Refresh();
                    }
                }
                else if (_layer != null)
                {
                    Close();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PerfHud] mission tick 失败: {ex.Message}");
            }
        }

        public override void OnMissionScreenFinalize()
        {
            Close();
            Instance = null;
            base.OnMissionScreenFinalize();
        }

        private static bool AnyPanelOn()
            => Settings.Instance.ShowPerfHud
               || Settings.Instance.ShowPerfProfiler
               || Settings.Instance.ShowPerfSampler;

        /// <summary>AgentHudMissionView 同款挂载模式：TopScreen as MissionScreen；全部 try/catch 落日志。</summary>
        private void TryMount()
        {
            if (_layer != null) return;
            var ms = ScreenManager.TopScreen as MissionScreen;
            if (ms == null)
            {
                DebugLogger.Log($"[PerfHud] mission mount 跳过（TopScreen={ScreenManager.TopScreen?.GetType().Name ?? "null"} 非 MissionScreen）");
                return;
            }
            try
            {
                if (_vm == null) _vm = new PerfHudVM();
                _layer = V.NewLayer(LayerOrder, "PerfHudLayer");
                V.LoadMov(_layer, "PerfHud", _vm);
                ms.AddLayer(_layer);
                _vm.IsVisible = false;
                DebugLogger.Log($"[PerfHud] mission layer mounted (order {LayerOrder}, MissionScreen={ms.GetType().Name})");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PerfHud] mission mount 失败: {ex.Message}");
                _layer = null;
            }
        }

        /// <summary>🔴 摘层守卫（V.LayerFinalized + try/catch——已 Finalize 的层禁止任何引擎操作，
        /// 1.2.12 二次 Finalize = NRE 实机教训）。AgentHudMissionView 同款（RemoveLayer 随屏释放，无单独 ReleaseMovie）。</summary>
        private void Close()
        {
            try
            {
                if (_layer != null && !V.LayerFinalized(_layer) && MissionScreen != null)
                {
                    MissionScreen.RemoveLayer(_layer);
                }
            }
            catch (Exception ex) { DebugLogger.Log($"[PerfHud] close 失败: {ex.Message}"); }
            _layer = null;
            _vm = null;
        }
    }
}
