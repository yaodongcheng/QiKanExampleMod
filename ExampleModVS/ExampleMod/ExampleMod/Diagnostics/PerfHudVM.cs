using System;
using System.Text;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>性能面板表格行（2026-09-03 用户裁定：真网格——单 RichText 拼接对齐方向错误，改三列布局）。
    /// 列 1 = 名称/标题（固定 220 宽，ClipContents 裁剪）；列 2 = 每帧·占比；列 3 = 秒总·次数·峰。</summary>
    public class PerfRowVM : ViewModel
    {
        private string _col1 = "";
        private string _col2 = "";
        private string _col3 = "";

        [DataSourceProperty]
        public string Col1
        {
            get => _col1;
            set { if (value != _col1) { _col1 = value; OnPropertyChangedWithValue(value, "Col1"); } }
        }

        [DataSourceProperty]
        public string Col2
        {
            get => _col2;
            set { if (value != _col2) { _col2 = value; OnPropertyChangedWithValue(value, "Col2"); } }
        }

        [DataSourceProperty]
        public string Col3
        {
            get => _col3;
            set { if (value != _col3) { _col3 = value; OnPropertyChangedWithValue(value, "Col3"); } }
        }

        /// <summary>三列整体写入（任何一列变化才触发通知；空串标准化）。</summary>
        public void SetText(string col1, string col2, string col3)
        {
            Col1 = col1 ?? "";
            Col2 = col2 ?? "";
            Col3 = col3 ?? "";
        }
    }

    /// <summary>
    /// 性能诊断面板 VM（真表格：FPS 行独立 + 固定 13 行表格行（三列），0.5s 刷新一次）。
    /// 行序（固定）：[0-2] 合计三行（本 mod / 其他 DLL / 引擎未归因）、[3] 本 mod 分节标题、
    /// [4-7] 本 mod TOP4、[8] DLL 分节标题、[9-12] 其他 DLL TOP4。
    /// 🔴 列宽归布局控件（PerfHud.xml：220/170/200），不靠空格填充——顺带根治比例字体
    /// 非对齐 + 1.5 UI 缩放折行（2026-09-03 用户裁定：单 RichText 拼接对齐方向错误）。
    /// 场景词/分节标题/明细前缀走标准化本地化（LWN_perf_* 双桶）；模块/方法/DLL 名英文原样。
    /// </summary>
    public class PerfHudVM : ViewModel
    {
        /// <summary>表格行总数（固定创建，复用更新——防模板每 0.5s 全重建的布局抖动）。</summary>
        public const int TotalRowCount = 13;

        private bool _isVisible;
        private string _fpsLine;

        /// <summary>面板总可见性（宿主每帧推：任一 MCM 开关开 + 层有效）。</summary>
        [DataSourceProperty]
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (value != _isVisible)
                {
                    _isVisible = value;
                    OnPropertyChangedWithValue(value, "IsVisible");
                }
            }
        }

        /// <summary>FPS 行（独立 TextWidget，全宽单行）。</summary>
        [DataSourceProperty]
        public string FpsLine
        {
            get => _fpsLine;
            private set
            {
                if (value != _fpsLine)
                {
                    _fpsLine = value;
                    OnPropertyChangedWithValue(value, "FpsLine");
                }
            }
        }

        /// <summary>表格行（固定 13 行，常驻复用）。</summary>
        public MBBindingList<PerfRowVM> Rows { get; private set; }

        public PerfHudVM()
        {
            _isVisible = false;
            _fpsLine = "FPS --";
            Rows = new MBBindingList<PerfRowVM>();
            for (int i = 0; i < TotalRowCount; i++)
                Rows.Add(new PerfRowVM());
        }

        /// <summary>
        /// 面板刷新（宿主节流 0.5s 调用）。场景词 1s 重解析（Compass 切语言教训：
        /// 语言切换不重建 VM —— 解析一次缓存滞留旧语言，周期重解析让切语言 1s 内生效）。
        /// </summary>
        public void Refresh()
        {
            try
            {
                PerfProfiler.TakeSnapshot();
                PerfProfiler.GetFrameStats(out int frames, out float avgMs, out float maxMs);
                PerfProfiler.PerfScene scene = PerfProfiler.CurrentScene();

                // 场景标记（本地化）+ 语言周期重解析窗口（秒级）
                int langTickStamp = Environment.TickCount / 1000;
                if (langTickStamp != _lastSceneFlag)
                    RefreshLanguageKeys(langTickStamp, scene);

                // ── FPS 行（显示缓存 = 最近完成窗口，永不瞬时 0；FPS 档位色 span）──
                float fps = avgMs > 0.01f ? 1000f / avgMs : 0f;
                string fpsStyle = fps >= 50f ? "Good" : (fps >= 30f ? "Warn" : "Bad");
                FpsLine = $"<span style=\"{fpsStyle}\">{fps.ToString("F0")} FPS</span> · "
                          + $"{avgMs:F1}ms (max {maxMs:F1}ms) · {_sceneText}";

                // 清空全部行（底部两分区标题与剩余行保留占位）
                for (int i = 0; i < TotalRowCount; i++) Rows[i].SetText("", "", "");

                // ── 合计三行（Rows[0-2]）──
                float modTotal = PerfProfiler.RootSlotTotalMs();
                float wrapTotal = PerfWrapper.TotalMs();
                if (avgMs > 0.01f && frames > 0)
                {
                    float modPerFrame = modTotal / Math.Max(1, frames);
                    float wrapPerFrame = wrapTotal / Math.Max(1, frames);
                    // 未归因 = 整帧 − 可归因 = 引擎 native + 等待 + 未插桩暗角（native 无托管栈，语义=未被归因）
                    float unAttributed = Math.Max(0f, avgMs - modPerFrame - wrapPerFrame);

                    if (modPerFrame > 0f)
                        Rows[0].SetText(Truncate(_sectionModLabel, 20), $" {modPerFrame:F2}ms/帧 · 占帧 {Pct(modPerFrame, avgMs)}%", "");
                    if (wrapPerFrame > 0f)
                        Rows[1].SetText(Truncate(_sectionWrapLabel, 20), $" {wrapPerFrame:F2}ms/帧 · 占帧 {Pct(wrapPerFrame, avgMs)}%", "");
                    if (unAttributed > 0.005f)
                        Rows[2].SetText(Truncate(_unattributedText, 20), $" {unAttributed:F2}ms/帧 · 占帧 {Pct(unAttributed, avgMs)}%", "");
                }

                // ── Rows[3] 本 mod 分节标题 / Rows[4-7] TOP4 ──
                Rows[3].SetText(_sectionModText, "", "");
                var tops = PerfProfiler.TopSlots(8);
                int shown = 0;
                for (int i = 0; i < tops.Count && shown < 4; i++)
                {
                    var t = tops[i];
                    SetRow(4 + shown, PerfProfiler.SlotName(t.slot), t.ms, t.maxMs, t.count, avgMs, frames,
                        isDetail: (int)t.slot >= PerfProfiler.RootSlotCount);
                    shown++;
                }

                // ── Rows[8] DLL 分节标题 / Rows[9-12] TOP4 ──
                var wraps = PerfWrapper.TopSlots(4);
                if (wraps.Count > 0)
                {
                    Rows[8].SetText(_sectionWrapText, "", "");
                    for (int i = 0; i < wraps.Count; i++)
                        SetRow(9 + i, wraps[i].Name, wraps[i].Ms, wraps[i].MaxMs, wraps[i].Count, avgMs, frames, false);
                }

                // 🔴 防误读：本 mod 未展开（TOP 空）而 DLL 有数时，清掉恒空的本 mod 标题
                if (tops.Count == 0) Rows[3].SetText("", "", "");
            }
            catch (Exception ex)
            {
                FpsLine = "FPS --";
                Rows[0].SetText("[perf]", ex.Message, "");
            }
        }

        private void RefreshLanguageKeys(int stamp, PerfProfiler.PerfScene scene)
        {
            _lastSceneFlag = stamp;
            _sceneText = ResolveSceneText(scene);
            // 本地化：LWN_perf_section_mod（本 mod 分节标题；分隔符 ASCII '-'——U+2014 引擎字体缺字形呈方框）
            _sectionModText = LWNTextHelper.ResolveText("LWN_perf_section_mod", "--- this mod ---");
            // 本地化：LWN_perf_section_wrap（其他 DLL 分节标题）
            _sectionWrapText = LWNTextHelper.ResolveText("LWN_perf_section_wrap", "--- other DLLs ---");
            // 本地化：LWN_perf_detail_prefix（明细槽前缀）
            _detailPrefix = LWNTextHelper.ResolveText("LWN_perf_detail_prefix", "[d] ");
            // 本地化：LWN_perf_unattributed（未归因行标签）
            _unattributedText = LWNTextHelper.ResolveText("LWN_perf_unattributed", "engine/unattributed");
            // 本地化：LWN_perf_section_mod（合计行标签——与分节标题同键，各自取前段语义）
            _sectionModLabel = LWNTextHelper.ResolveText("LWN_perf_section_mod", "this mod");
            // 本地化：LWN_perf_section_wrap（合计行标签）
            _sectionWrapLabel = LWNTextHelper.ResolveText("LWN_perf_section_wrap", "other DLL");
        }

        private string _sceneText = "";
        private string _sectionModText = "";
        private string _sectionWrapText = "";
        private string _sectionModLabel = "this mod";
        private string _sectionWrapLabel = "other DLL";
        private string _detailPrefix = "[d] ";
        private string _unattributedText = "engine/unattributed";
        private int _lastSceneFlag = -1;

        private static string ResolveSceneText(PerfProfiler.PerfScene scene)
        {
            switch (scene)
            {
                case PerfProfiler.PerfScene.Mission:
                    // 本地化：LWN_perf_scene_mission（场景标识）
                    return LWNTextHelper.ResolveText("LWN_perf_scene_mission", "mission");
                case PerfProfiler.PerfScene.Campaign:
                    // 本地化：LWN_perf_scene_campaign（场景标识）
                    return LWNTextHelper.ResolveText("LWN_perf_scene_campaign", "map");
                case PerfProfiler.PerfScene.CampaignPaused:
                    // 本地化：LWN_perf_scene_campaign_paused（场景标识-暂停）
                    return LWNTextHelper.ResolveText("LWN_perf_scene_campaign_paused", "map (paused)");
                case PerfProfiler.PerfScene.UISave:
                    // 本地化：LWN_perf_scene_ui_save（场景标识-存档界面）
                    return LWNTextHelper.ResolveText("LWN_perf_scene_ui_save", "save-ui");
                case PerfProfiler.PerfScene.UILoading:
                    // 本地化：LWN_perf_scene_ui_loading（场景标识-加载界面）
                    return LWNTextHelper.ResolveText("LWN_perf_scene_ui_loading", "loading");
                default:
                    // 本地化：LWN_perf_scene_ui（场景标识-界面）
                    return LWNTextHelper.ResolveText("LWN_perf_scene_ui", "ui");
            }
        }

        private static float Pct(float perFrameMs, float avgMs)
            => avgMs > 0.01f ? perFrameMs * 100f / avgMs : 0f;

        /// <summary>模块行三列：Col1=名称（ClipContents 裁剪）、Col2="0.12ms/帧 · 4%"、
        /// Col3="24.0ms/s ×200 (峰 1.8ms)"（秒总、次数、峰值）。列宽全部归布局控件——
        /// 根治比例字体空格填充的非对齐（2026-09-03 用户裁定真网格方向）。</summary>
        private void SetRow(int index, string name, float ms, float maxMs, int count, float avgMs, int frames, bool isDetail)
        {
            float perFrame = frames > 0 ? ms / frames : 0f;
            float pct = Pct(perFrame, avgMs);
            string prefix = isDetail ? _detailPrefix : "";
            string totalStr = ms >= 0.05f ? $"{ms:F1}ms/s" : "<0.1ms/s";
            string perStr = perFrame >= 0.005f ? $"{perFrame:F2}ms/帧 · {pct:F0}%" : $"<0.01ms/帧 · {pct:F0}%";
            string tail = $"{totalStr} ×{count}" + (maxMs > 2f ? $" (峰 {maxMs:F1}ms)" : "");
            Rows[index].SetText(Truncate(name, 22), $" {perStr}", $" {tail}");
        }

        /// <summary>名称列超长截断（ASCII "..."——U+2026 引擎字体缺字形呈方框）；面板宽度由布局列保证。</summary>
        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length > maxLen)
                return s.Substring(0, maxLen - 3) + "...";
            return s;
        }
    }
}
