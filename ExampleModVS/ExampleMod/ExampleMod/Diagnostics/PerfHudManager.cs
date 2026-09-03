using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.Data;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 性能面板统一宿主（ImChatOpenButtonManager 已验证范本，2026-09-03 用户指引）：
    ///  - **单一管理者，全场景通吃**——层永远挂 ScreenManager.TopScreen（Mission 时
    ///    TopScreen=MissionScreen，Campaign 时 MapScreen，主菜单/创角/读档/全屏 UI 屏照挂）；
    ///  - 🔴 驱动 = 单点 OnApplicationTick（MySubModule.OnApplicationTick 每帧调 Tick）——
    ///    引擎应用层每帧回调，主菜单/任意外屏/自定义战斗/Mission/Campaign 全场景都到，含暂停
    ///    （用户裁定「主菜单也要有」，2026-09-03；原 ScreenBase/MissionScreen 双补丁驱动已废弃）；
    ///  - 层序两档：Mission 30 / 其余 204（主菜单 UI 层序 1/2 实测压得住，MapBar 202 之上、
    ///    &lt; 系统菜单 4400）；
    ///  - 🔴 判废重挂（ImChat 实机教训）：owner 屏 != TopScreen ‖ 层已 Finalize → Close + 下帧
    ///    重挂——「摘旧屏挂新屏复用同一层」在引擎里不成立（RemoveLayer 必然 Finalize）；
    ///  - 🔴 _widgetsReady 门控（ImChat 实机教训）：LoadMovie 当帧 EventManager 未注册完成，
    ///    VM 属性写入（IsVisible setter → 绑定 RefreshState 链）会踩未就绪容器——**挂载当帧只挂引用，
    ///    属性写入延迟到下一帧**；
    ///  - 摘层：ImChat OpenButton 鲁棒版（层死判定 + ReleaseMovie(_movie) + 从 _layerOwnerScreen 摘
    ///    + HasLayer 校验 + try/catch——1.2.12 二次 Finalize NRE 史）。
    /// </summary>
    public static class PerfHudManager
    {
        private const int MissionLayerOrder = 30;
        private const int CampaignLayerOrder = 204;
        private const float RefreshIntervalSec = 0.5f;

        private static GauntletLayer _layer;
        private static ScreenBase _layerOwnerScreen;
        private static PerfHudVM _vm;
#if !MB2_V1212
        private static GauntletMovieIdentifier _movie;
#else
        private static IGauntletMovie _movie;
#endif
        private static float _refreshTimer;
        /// <summary>🔴 ImChat 实机教训：挂载/重挂下一帧才放行 VM 属性写入（LoadMovie 当帧容器未就绪）。</summary>
        private static bool _widgetsReady;

        /// <summary>每帧驱动（MySubModule.OnApplicationTick 单点——全场景覆盖，含暂停与主菜单）。</summary>
        public static void Tick(float dt)
        {
            try
            {
                bool anyOn = Settings.Instance.ShowPerfHud || Settings.Instance.ShowPerfDetails;
                PerfWrapper.Enabled = Settings.Instance.ShowPerfDetails;

                // 🔴 判废重挂（ImChat 模式）：owner 屏已不是 TopScreen / 层已 Finalize → Close + 下帧自动重挂
                if (_layer != null && _layerOwnerScreen != null
                    && (ScreenManager.TopScreen == null
                        || _layerOwnerScreen != ScreenManager.TopScreen
                        || V.LayerFinalized(_layer)))
                {
                    DebugLogger.Log($"[PerfHud] 层失效（owner={_layerOwnerScreen.GetType().Name} Top={ScreenManager.TopScreen?.GetType().Name ?? "null"}），重新挂载");
                    Close();
                }

                if (!anyOn)
                {
                    if (_layer != null) Close();
                    return;
                }

                if (_layer == null)
                    Mount();
                if (_layer == null) return;

                // B 层包裹（幂等；内部节流扫描）
                try { PerfWrapper.Tick(); } catch { }

                if (!_widgetsReady)
                {
                    _widgetsReady = true;   // 挂载下一帧才放行属性写入
                    return;
                }

                _refreshTimer += dt;
                if (_vm != null && _refreshTimer >= RefreshIntervalSec)
                {
                    _refreshTimer = 0f;
                    _vm.IsVisible = true;
                    _vm.Refresh();
                }
            }
            catch (Exception ex)
            {
                try { DebugLogger.Log($"[PerfHud] Tick 异常: {ex.Message}"); } catch { }
            }
        }

        // ───────────────────────── 挂载 / 关闭 ─────────────────────────

        private static void Mount()
        {
            try
            {
                int order = ScreenManager.TopScreen is MissionScreen ? MissionLayerOrder : CampaignLayerOrder;
                _layer = V.NewLayer(order, "PerfHudLayer");
                if (_vm == null) _vm = new PerfHudVM();
                _movie = _layer.LoadMovie("PerfHud", _vm);
                if (ScreenManager.TopScreen != null)
                {
                    ScreenManager.TopScreen.AddLayer(_layer);
                    _layerOwnerScreen = ScreenManager.TopScreen;
                }
                _widgetsReady = false;
                DebugLogger.Log($"[PerfHud] 挂载（层序 {order}，TopScreen={ScreenManager.TopScreen?.GetType().Name}）");
            }
            catch (Exception ex)
            {
                try { DebugLogger.Log($"[PerfHud] 挂载失败: {ex.Message}"); } catch { }
                Close();
            }
        }

        /// <summary>🔴 鲁棒摘层（ImChatOpenButton 同款）：层死判定 + ReleaseMovie + 从 _layerOwnerScreen 摘
        ///（层可能挂在非 TopScreen 屏——从 TopScreen 摘 = 摘错屏 + 层 Finalize 残留在 owner 屏，[ImChat 2026-08-17]）。</summary>
        private static void Close()
        {
            if (_layer != null)
            {
                bool layerDead = V.LayerFinalized(_layer);
                try
                {
                    if (!layerDead)
                    {
                        if (_movie != null)
                        {
                            _layer.ReleaseMovie(_movie);
                            _movie = null;
                        }
                        // 从层实际挂载的屏摘（HasLayer 校验：层可能已随屏销毁）
                        if (_layerOwnerScreen != null && _layerOwnerScreen.HasLayer(_layer))
                            _layerOwnerScreen.RemoveLayer(_layer);
                        else if (ScreenManager.TopScreen != null && ScreenManager.TopScreen.HasLayer(_layer))
                            ScreenManager.TopScreen.RemoveLayer(_layer);
                    }
                }
                catch (Exception ex)
                {
                    try { DebugLogger.Log($"[PerfHud] Close 失败: {ex.Message}"); } catch { }
                }
                _layer = null;
                _layerOwnerScreen = null;
                _movie = null;
            }
            _vm = null;
            _widgetsReady = false;
        }
    }
}
