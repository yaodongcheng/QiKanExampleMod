using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 临时验证（2026-08-30 立绘 tpac 实机验证用——验证完成后删除本文件 + MySubModule 挂钩 + csproj 2 行）：
    /// 两个入口：
    ///   ① 控制台指令 `lwn.profile <tkid> [emo]`（例：lwn.profile 517 / lwn.profile 361 happy）——
    ///      → 任意屏上叠全屏深底半透明面板，中央立绘 512×768 + 右下 128×128 小头像；
    ///      15 秒后自动消失；同参再输 = 立即关闭。控制台开法：启动器勾选作弊模式，游戏内 Alt+~。
    ///   ② 队伍屏自动预览（备用）：按 C 开队伍屏 → 右下角两组固定卡（普通包+emotion 包）。
    /// 验证点：tpac 原生挂载 / SpriteData 全模块扫描 / PartialLoadAtIndex 按需加载 / 直 alpha 无黑边 /
    ///         LRU 驱逐（同时看 RuntimeLog 的 [SpriteAssets] 加载+驱逐行）。
    /// 与 SecretLetter 同款风格：纯 C# 注入、幂等、try/catch + 日志（铁律 1）。
    /// </summary>
    public static class PortraitPreviewInjector
    {
        private const string PreviewId = "LWN_PortraitPreview";
        private const float ScanIntervalSec = 0.5f;
        private const int OverlayAutoHideSec = 15;

        private static float _scanTimer;

        // 指令叠加层状态
        private static Widget _overlay;
        private static Widget _overlayRoot;      // 注入时的 UIContext.Root（屏销毁后失效）
        private static string _overlayKey;
        private static DateTime _overlayAutoHide;

        // ───────────────────────── 控制台指令（自动扫描注册） ─────────────────────────

        [CommandLineFunctionality.CommandLineArgumentFunction("profile", "lwn")]
        public static string ConsoleProfile(List<string> args)
        {
            try
            {
                if (args == null || args.Count < 1)
                    return "用法: lwn.profile <tkid> [emo]  例: lwn.profile 517  /  lwn.profile 361 happy";
                string tkid = args[0];
                string emo = args.Count > 1 ? args[1].ToLowerInvariant() : null;
                string bustup = emo == null ? $"lwnprof_bustup_{tkid}" : $"lwnprof_emobustup_{tkid}_{emo}";
                string mini = emo == null ? $"lwnprof_mini_{tkid}" : $"lwnprof_emomini_{tkid}_{emo}";
                string key = $"{bustup}|{mini}";

                if (_overlay != null && _overlayKey == key)
                {
                    HideOverlay();
                    return $"已关闭预览（{bustup}）";
                }
                bool ok = ShowOverlay(key, bustup, mini);
                return ok
                    ? $"显示 {bustup} + {mini}（{OverlayAutoHideSec} 秒后自动消失；同参再输 = 立即关闭）"
                    : $"失败：{bustup} / {mini} 取不到（内容包 4 tpac 或 LWProfilesSpriteData.xml 未加载 → 查 [SpriteAssets] 日志）";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PortraitPreview] lwn.profile 异常: {ex.Message}");
                return "异常: " + ex.Message;
            }
        }

        // ───────────────────────── 叠加层 ─────────────────────────

        private static bool ShowOverlay(string key, string bustupName, string miniName)
        {
            var top = ScreenManager.TopScreen;
            if (top == null) return false;
            GauntletLayer gl = null;
            foreach (var layer in top.Layers)
            {
                if (layer is GauntletLayer g && g.UIContext?.Root != null) { gl = g; break; }
            }
            if (gl == null) return false;
            HideOverlay();
            var root = gl.UIContext.Root;
            var context = root.Context;

            var panel = new Widget(context)
            {
                Id = PreviewId,
                WidthSizePolicy = SizePolicy.Fixed,
                HeightSizePolicy = SizePolicy.Fixed,
                SuggestedWidth = 620f,
                SuggestedHeight = 880f,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                DoNotAcceptEvents = false,   // 接受事件：ESC/点击阻断下屏 UI（叠加层临时，无需透传）
                IsVisible = true,
            };
            // 深色半透明底（直 alpha 目视判据：半透明立绘叠深底，边缘若有黑边一目了然）
            var bg = new ImageWidget(context)
            {
                WidthSizePolicy = SizePolicy.Fixed,
                HeightSizePolicy = SizePolicy.Fixed,
                SuggestedWidth = 620f,
                SuggestedHeight = 880f,
                Color = new Color(0.13f, 0.13f, 0.16f, 0.82f),
            };
            bg.Sprite = context.SpriteData?.GetSprite("BlankWhiteSquare_9");
            panel.AddChild(bg);

            var bust = new ImageWidget(context)
            {
                WidthSizePolicy = SizePolicy.Fixed,
                HeightSizePolicy = SizePolicy.Fixed,
                SuggestedWidth = 512f,
                SuggestedHeight = 768f,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            bust.Sprite = SpriteAssetsManager.GetOrLoad(bustupName);
            panel.AddChild(bust);

            var head = new ImageWidget(context)
            {
                WidthSizePolicy = SizePolicy.Fixed,
                HeightSizePolicy = SizePolicy.Fixed,
                SuggestedWidth = 128f,
                SuggestedHeight = 128f,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                MarginRight = 20f,
                MarginBottom = 20f,
            };
            head.Sprite = SpriteAssetsManager.GetOrLoad(miniName);
            panel.AddChild(head);

            root.AddChild(panel);
            _overlay = panel;
            _overlayRoot = root;
            _overlayKey = key;
            _overlayAutoHide = DateTime.UtcNow.AddSeconds(OverlayAutoHideSec);
            DebugLogger.Log($"[PortraitPreview] 叠加层显示 {bustupName} / {miniName}");
            return true;
        }

        private static void HideOverlay()
        {
            var overlay = _overlay;
            if (overlay == null) return;
            try
            {
                if (overlay.ParentWidget != null)
                    overlay.ParentWidget.RemoveChild(overlay);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PortraitPreview] 移除叠加层异常: {ex.Message}");
            }
            _overlay = null;
            _overlayRoot = null;
            _overlayKey = null;
        }

        private static void OverlayTick()
        {
            if (_overlay == null) return;
            // 屏已销毁（root 失效/面板被引擎回收）→ 丢弃状态
            if (_overlay.ParentWidget != _overlayRoot || _overlayRoot == null)
            {
                try { HideOverlay(); } catch { _overlay = null; }
                return;
            }
            if (DateTime.UtcNow >= _overlayAutoHide)
                HideOverlay();
        }

        // ───────────────────────── 每帧驱动 ─────────────────────────

        /// <summary>每帧（MySubModule.OnApplicationTick 调用）：叠加层计时清理 + 队伍屏自动注入（备用入口）。</summary>
        public static void Tick(float dt)
        {
            try
            {
                OverlayTick();
                _scanTimer += dt;
                if (_scanTimer < ScanIntervalSec) return;
                _scanTimer = 0f;
                var top = ScreenManager.TopScreen;
                if (top == null) return;
                if (!top.GetType().Name.Contains("PartyScreen")) return;
                foreach (var layer in top.Layers)
                {
                    var ui = (layer as GauntletLayer)?.UIContext;
                    if (ui?.Root == null) continue;
                    InjectPartyDefault(ui.Root);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PortraitPreview] Tick 异常: {ex.Message}");
            }
        }

        // ───────────────────────── 队伍屏默认预览（备用） ─────────────────────────

        private static void InjectPartyDefault(Widget root)
        {
            if (FindById(root, PreviewId + "_party") != null) return;

            var panel = new Widget(root.Context)
            {
                Id = PreviewId + "_party",
                WidthSizePolicy = SizePolicy.Fixed,
                HeightSizePolicy = SizePolicy.Fixed,
                SuggestedWidth = 168f,
                SuggestedHeight = 290f,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                MarginRight = 12f,
                MarginBottom = 40f,
                DoNotAcceptEvents = true,
            };
            float y = 0f;
            foreach (var (bustup, mini) in new[]
            {
                ("lwnprof_bustup_517", "lwnprof_mini_517"),                 // 普通包
                ("lwnprof_emobustup_361_happy", "lwnprof_emomini_361_happy") // emotion 包
            })
            {
                var bust = new ImageWidget(root.Context)
                {
                    WidthSizePolicy = SizePolicy.Fixed,
                    HeightSizePolicy = SizePolicy.Fixed,
                    SuggestedWidth = 160f,
                    SuggestedHeight = 240f,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    MarginRight = 8f,
                    MarginTop = y,
                };
                bust.Sprite = SpriteAssetsManager.GetOrLoad(bustup);
                panel.AddChild(bust);

                var head = new ImageWidget(root.Context)
                {
                    WidthSizePolicy = SizePolicy.Fixed,
                    HeightSizePolicy = SizePolicy.Fixed,
                    SuggestedWidth = 46f,
                    SuggestedHeight = 46f,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    MarginLeft = 4f,
                    MarginTop = y + 8f,
                };
                head.Sprite = SpriteAssetsManager.GetOrLoad(mini);
                panel.AddChild(head);
                y += 250f;
            }
            root.AddChild(panel);
            DebugLogger.Log("[PortraitPreview] 队伍屏注入立绘预览（普通包 + emotion 包）");
        }

        private static Widget FindById(Widget root, string id)
        {
            try
            {
                if (root.Id == id) return root;
                foreach (var child in root.Children)
                {
                    var hit = FindById(child, id);
                    if (hit != null) return hit;
                }
            }
            catch (Exception) { /* 树销毁窗口：按未命中处理 */ }
            return null;
        }
    }
}
