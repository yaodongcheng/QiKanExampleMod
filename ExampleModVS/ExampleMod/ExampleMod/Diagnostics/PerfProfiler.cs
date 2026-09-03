using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>本 mod 每帧子系统的插桩槽位（A 层）。序号 0.. 为根槽，RootCount 之后为明细槽。</summary>
    public enum PerfSlot
    {
        // ── 根槽（进 modShare；最外层计时点，互不嵌套）──
        AIC_BrainAll = 0,          // AgentAIController：全体 AgentBrain.Tick
        AIC_PlanExecutor,          // PlanExecutor.TickAll
        AIC_ReactiveAgent,         // ReactiveAgent.TickAll
        AIC_DialogueContinuations, // DialogueComponent.TickContinuations
        AIC_SpeechChannel,         // SpeechChannel.TickAll

        IMV_ModInput,              // InteractionMissionView：ModInput.Tick
        IMV_ImChatOpenButton,      // 呼出按钮 Tick
        IMV_PlanReplan,            // PlanReplan.Tick
        IMV_Compass,               // 罗盘 OnTick
        IMV_Interact,              // 交互上下文块
        IMV_Raycast,               // 交互射线

        HV_AgentHud,               // AgentHudMissionView 投影/刷新
        NV_NpcSight,               // NpcSightSystem 视线
        CV_CombatLogic,            // AttackTriggerMissionLogic
        CAM_SpringArm,             // SpringArmCameraView

        CT_InputSource,            // ImChatTick：ModInput.TickInputSource
        CT_WorldBackground,        // 世界观轮询
        CT_ImChatManager,          // ImChatView.Tick → ImChatManager.Tick

        ST_StoryEngine,            // StoryEngine.OnTick（OnApplicationTick 链）
        CP_MyBehavior,             // CampaignEvents.TickEvent → MyBehavior.OnTick
        CP_Detention,              // PlayerDetentionBehavior.OnTick
        CP_WorldEventSim,          // WorldEventSimulator.OnCampaignTick

        UI_ScreenTick,             // ScreenBase.OnFrameTick 总耗时（暂停/读档时 UI 层消耗）
        UI_MissionUIFrame,         // MissionScreen.OnFrameTick 总耗时

        // ── 明细槽（只进 TOP 列表，不进 modShare——与 CT_ImChatManager 嵌套，防双计）──
        ImMgr_Reply = 100,         // ImChatManager.Tick → ImReplyService.Tick
        ImMgr_CommandFlow,         // ImCommandFlow.Tick
        ImMgr_EventBroadcaster,    // ImEventBroadcaster.Tick
        ImMgr_AutonomyProposal,    // AutonomyProposal.Tick
        ImMgr_DelayedMsgs,         // 延迟投递队列消费

        // ── 明细槽：PlanExecutor 执行期分账（2026-09-03 P1 插桩；只进 TOP 不进 modShare——
        //    嵌套于根槽 AIC_PlanExecutor 中，防双计。用途：perf_status 分账执行期热段，
        //    区分「快照重建大头」vs「contingency 目击判定大头」）──
        PlanInner_World = 200,     // RuntimeWorldState.RebuildSnapshot（🔴 2026-09-03 懒化后 = 兜底
                                   // 解析 miss 时才重建；正常执行期该槽应为 0——计的是懒重建成本）
        PlanInner_Guard,           // TickGuardrails
        PlanInner_Contingency,     // TickContingencies（Evaluate 求值：seeing(any) 目击判定等）
        PlanInner_Trigger,         // TickTriggers
        PlanInner_Cursor,          // TickCursor（when 门控 Evaluate + 子动作轮询）
    }

    /// <summary>
    /// 性能采样核心（A/B/C 三层共用）：
    ///  - 插桩槽：Accum(slot, t0) 单点累计，不要求配对，提前 return 无副作用；
    ///  - 帧心跳：OnFrameTick() 挂 MySubModule.OnApplicationTick（暂停/读档界面照常每帧触发）；
    ///  - 卡顿捕获：单帧 &gt; 阈值（默认 40ms）写 DebugLogger 卡顿行，10s 限流；
    ///  - 1 秒滚动窗口：按墙钟切窗，读快照懒触发，无帧边界依赖。
    /// 🔴 A 层插桩常开（每帧 ~2μs 量级，不可感知）——卡顿行永远带 mod 数据；
    ///    MCM 开关只控「面板显示 + 其他 mod 包裹 + 卡顿行是否带 [Wrap] 段」。
    /// </summary>
    public static class PerfProfiler
    {
        /// <summary>根槽数量（0..RootCount-1），modShare 只加根槽（明细槽嵌套其中防双计）。</summary>
        public const int RootSlotCount = (int)PerfSlot.UI_MissionUIFrame + 1;

        /// <summary>枚举总槽数（含明细槽）。</summary>
        public const int TotalSlotCount = (int)PerfSlot.PlanInner_Cursor + 1;

        /// <summary>与 PerfSlot 对齐的显示名。</summary>
        public static readonly string[] SlotNames = new string[TotalSlotCount];

        /// <summary>卡顿阈值（ms），custom.perf_threshold 可改。</summary>
        public static int StutterThresholdMs = 40;

        /// <summary>卡顿行 [Wrap] 段填充钩子（PerfWrapper 注册；无包裹数据返回 null）。</summary>
        public static Func<string> StutterWrapSummaryHook;

        // ── 窗口累计（主线程独占，无锁）──
        private static readonly float[] _accWindow = new float[TotalSlotCount];
        private static readonly float[] _maxWindow = new float[TotalSlotCount];   // 单次峰值 ms
        private static readonly int[] _cntWindow = new int[TotalSlotCount];
        // ── 上一窗口快照（读时滚动）──
        private static readonly float[] _snapAcc = new float[TotalSlotCount];
        private static readonly float[] _snapMax = new float[TotalSlotCount];
        private static readonly int[] _snapCnt = new int[TotalSlotCount];

        private static long _windowStartTicks;
        private static int _frameCount;
        private static float _frameTotalMs;
        private static float _frameMaxMs;

        private static long _lastFrameTickTicks = long.MinValue;
        private static float _lastFrameMs;
        private static long _lastStutterLogTicks = long.MinValue;

        // ── 显示缓存（🔴 2026-09-03：0↔100+ 跳变修复）──
        // 显示层读「最近一个【完成】窗口」的平均值——窗口滚动清零的瞬间不暴露零数据。
        // 更新时机 = RollWindow 成功交换时（数据清零【前】保存）；窗口中途刷新不更新。
        private static float _dispAvgMs;
        private static float _dispMaxMs;
        private static int _dispFrameCount;

        static PerfProfiler()
        {
            SlotNames = BuildSlotNames();
        }

        private static string[] BuildSlotNames()
        {
            string[] names = new string[TotalSlotCount];
            foreach (PerfSlot s in Enum.GetValues(typeof(PerfSlot)))
                names[(int)s] = s.ToString();
            return names;
        }

        public static string SlotName(PerfSlot slot) => SlotNames[(int)slot];

        /// <summary>墙钟（高分辨率）。</summary>
        public static long Now() => Stopwatch.GetTimestamp();

        /// <summary>
        /// 单点累计当前帧耗时（不要求与调用点配对，提前 return 不会破坏任何状态）。
        /// 调用模式：long t0 = PerfProfiler.Now(); ...原逻辑...; PerfProfiler.Accum(PerfSlot.XXX, t0);
        /// </summary>
        public static void Accum(PerfSlot slot, long t0)
        {
            if (t0 <= 0) return;
            int idx = (int)slot;
            if (idx < 0 || idx >= TotalSlotCount) return;
            float ms = (float)((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
            _accWindow[idx] += ms;
            _cntWindow[idx]++;
            if (ms > _maxWindow[idx]) _maxWindow[idx] = ms;   // 峰值追踪（诊断尖峰比均值更有用）
        }

        // ═══════════════════════════ 帧心跳 ═══════════════════════════

        /// <summary>
        /// 帧心跳（每帧一次，挂 MySubModule.OnApplicationTick）。墙钟帧间隔 →
        /// 1s 滚动窗口 + 卡顿检测。⚠️ OnApplicationTick 是引擎应用层回调：Campaign 暂停
        /// （时间流速 0）与读档/加载界面期间照常触发——暂停 UI 卡顿、读档转场卡顿均被测到。
        /// 帧间隔 &gt; 250ms 视为加载/挂起，重置基线不计数。
        /// </summary>
        public static void OnFrameTick()
        {
            long now = Stopwatch.GetTimestamp();

            // 初始化基线
            if (_lastFrameTickTicks == long.MinValue)
            {
                _lastFrameTickTicks = now;
                _windowStartTicks = now;
                return;
            }

            float frameMs = (float)((now - _lastFrameTickTicks) * 1000.0 / Stopwatch.Frequency);
            _lastFrameTickTicks = now;
            _lastFrameMs = frameMs;

            if (frameMs > 250f)
            {
                // 加载/挂起/切屏：重置窗口基线（该帧不计入统计）
                RollWindow(now);
                _lastFrameTickTicks = now;
                return;
            }

            _frameCount++;
            _frameTotalMs += frameMs;
            if (frameMs > _frameMaxMs) _frameMaxMs = frameMs;

            // 卡顿检测（常开：即使用户没开任何 MCM 开关——10s 限流）
            if (frameMs > StutterThresholdMs)
                LogStutterIfThrottled(now, frameMs);
        }

        /// <summary>1s 滚动窗口：窗口期满 → 快照换入 _snapAcc/_snapCnt 并清零窗口。
        /// 🔴 清零【前】把刚完成的窗口数据写进显示缓存（_disp*）——显示永不读到零。</summary>
        private static void RollWindow(long now)
        {
            if (now - _windowStartTicks >= Stopwatch.Frequency)
            {
                Array.Copy(_accWindow, _snapAcc, TotalSlotCount);
                Array.Copy(_maxWindow, _snapMax, TotalSlotCount);
                Array.Copy(_cntWindow, _snapCnt, TotalSlotCount);
                Array.Clear(_accWindow, 0, TotalSlotCount);
                Array.Clear(_maxWindow, 0, TotalSlotCount);
                Array.Clear(_cntWindow, 0, TotalSlotCount);
                _windowStartTicks = now;
                // 显示缓存：清空前保存（1s 完整窗口；窗口中途则保留旧值）
                _dispFrameCount = _frameCount;
                _dispAvgMs = _frameCount > 0 ? _frameTotalMs / _frameCount : _dispAvgMs;
                _dispMaxMs = _frameMaxMs;
                _frameCount = 0;
                _frameTotalMs = 0f;
                _frameMaxMs = 0f;
            }
        }

        // ═══════════════════════════ 场景标记 ═══════════════════════════

        public enum PerfScene { Mission, Campaign, CampaignPaused, UISave, UILoading, UI }

        /// <summary>当前场景四态 + 读档/加载标记（供卡顿行与面板显示）。</summary>
        public static PerfScene CurrentScene()
        {
            try
            {
                if (TaleWorlds.MountAndBlade.Mission.Current != null) return PerfScene.Mission;
                if (Campaign.Current != null)
                {
                    // 🔴 1.3.x 后用 TimeControlMode（三锚点一致：1.2.12:323 / 1.3.15:356）；
                    // 暂停 = Stop（时间流速 0，TickEvent 停发但 UI 层每帧照跑）
                    return Campaign.Current.TimeControlMode == CampaignTimeControlMode.Stop
                        ? PerfScene.CampaignPaused
                        : PerfScene.Campaign;
                }
                string topName = ScreenManager.TopScreen?.GetType().Name ?? "";
                if (topName.IndexOf("SaveLoad", StringComparison.OrdinalIgnoreCase) >= 0) return PerfScene.UISave;
                if (topName.IndexOf("Loading", StringComparison.OrdinalIgnoreCase) >= 0) return PerfScene.UILoading;
                return PerfScene.UI;
            }
            catch
            {
                return PerfScene.UI;
            }
        }

        // ═══════════════════════════ 卡顿捕获 ═══════════════════════════

        private static void LogStutterIfThrottled(long now, float frameMs)
        {
            if (_lastStutterLogTicks != long.MinValue &&
                now - _lastStutterLogTicks < Stopwatch.Frequency * 10L)
                return;
            _lastStutterLogTicks = now;

            try
            {
                PerfScene scene = CurrentScene();
                float modTotal = RootSlotTotalMs();
                // 🔴 pct 语义 = 「mod 平均每帧耗时」占「平均帧长」：modTotal/帧数 ÷ avgMs（2026-09-03
                // 量级 bug 修正：窗口累计直接比单帧平均 = 差一个帧数，曾显示 1269%）
                float avgFrameMs = _frameCount > 0 ? _frameTotalMs / _frameCount : 0f;
                float pct = modTotal > 0f && avgFrameMs > 0f && _frameCount > 0
                    ? (modTotal / _frameCount) * 100f / avgFrameMs
                    : 0f;

                var sb = new StringBuilder();
                sb.Append($"[Perf] Stutter {frameMs:F1}ms scene={scene}");
                if (_frameCount > 0)
                {
                    sb.Append($" | mod {modTotal:F1}ms (≈{pct:F0}%/frame)");
                    var top = TopSlots(RootSlotCount, 3);
                    if (top.Count > 0)
                    {
                        string topStr = string.Join(", ",
                            top.Select(t => $"{SlotName(t.slot)} {t.ms:F1}ms x{t.count}"));
                        sb.Append($" | TOP: {topStr}");
                    }
                }
                string wrap = StutterWrapSummaryHook?.Invoke();
                if (wrap != null)
                    sb.Append($" | {wrap}");

                DebugLogger.Log(sb.ToString());
            }
            catch (Exception ex)
            {
                try { DebugLogger.Log($"[Perf] stutter log failed: {ex.Message}"); } catch { }
            }
        }

        // ═══════════════════════════ 快照 ═══════════════════════════

        public struct SlotCost { public PerfSlot slot; public float ms; public int count; }

        /// <summary>窗口快照（读时自动滚动；主线程独享）。</summary>
        public static void TakeSnapshot()
        {
            RollWindow(Stopwatch.GetTimestamp());
        }

        /// <summary>最近【完成】窗口的 FPS/平均帧时间/最大帧时间/帧数（显示缓存，永不瞬时 0）。</summary>
        public static void GetFrameStats(out int frameCount, out float avgMs, out float maxMs)
        {
            frameCount = _dispFrameCount;
            avgMs = _dispAvgMs;
            maxMs = _dispMaxMs;
        }

        /// <summary>上一窗口某槽的累计毫秒（0 基准安全）。</summary>
        public static float SlotMs(PerfSlot slot)
        {
            int idx = (int)slot;
            if (idx < 0 || idx >= TotalSlotCount) return 0f;
            return _snapAcc[idx];
        }

        public static int SlotCount(PerfSlot slot)
        {
            int idx = (int)slot;
            if (idx < 0 || idx >= TotalSlotCount) return 0;
            return _snapCnt[idx];
        }

        /// <summary>所有根槽合计（modShare 语义：只加根槽，明细槽嵌套其中防双计）。</summary>
        public static float RootSlotTotalMs()
        {
            float total = 0f;
            for (int i = 0; i < RootSlotCount; i++) total += _snapAcc[i];
            return total;
        }

        /// <summary>按累计毫秒降序取前 N 个槽（含明细槽标记）；ms=窗口累计、maxMs=单次峰值、count=次数。</summary>
        public static System.Collections.Generic.List<(PerfSlot slot, float ms, float maxMs, int count)> TopSlots(int limit)
        {
            return TopSlots(TotalSlotCount, limit);
        }

        public static System.Collections.Generic.List<(PerfSlot slot, float ms, float maxMs, int count)> TopSlots(int slotLimit, int limit)
        {
            var list = new System.Collections.Generic.List<(PerfSlot, float, float, int)>(TotalSlotCount);
            for (int i = 0; i < slotLimit && i < TotalSlotCount; i++)
            {
                if (_snapAcc[i] <= 0.0001f) continue;
                list.Add(((PerfSlot)i, _snapAcc[i], _snapMax[i], _snapCnt[i]));
            }
            list.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            if (list.Count > limit) list.RemoveRange(limit, list.Count - limit);
            return list.ConvertAll(t => (t.Item1, t.Item2, t.Item3, t.Item4));
        }
    }
}
