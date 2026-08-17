using System;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 自动世界观总结生成行为（2026-08-17 计划 world-background-auto-summary.md §2/§3）。
    ///
    /// 状态机 Idle / Generating / Done / Failed：
    /// - 🔴 驱动 = 墙钟渲染帧（暂停也运转），**禁止依赖 CampaignEvents.TickEvent**——Campaign
    ///   时间静止时 dt=0 停发，世界背景永不生成（2026-08-17 实机：读档后玩家在家族屏/IM 上
    ///   暂停观察，日志无任何 [WorldBg] 记录）。驱动入口 = ImChatView.Tick（Mission/Campaign
    ///   双端每帧墙钟钩子，ImScreenFrameTickPatch → ScreenBase.OnFrameTick 同款轮子，
    ///   wheels.d/im.md）；Instance 由 RegisterEvents 挂接，入口 ?. 空安全
    /// - 墙钟帧 dt 累积 ≥15s 且数据就绪（Campaign + 王国 + 活英雄）→ 检查（15s = 用户裁定轮询间隔）
    /// - Generating → 跳过（防重入）；Failed → 本会话不再重试（防 LLM 宕机时重试风暴；
    ///   状态是 Behavior 实例字段，进档天然复位）；Done → 保留 300s 低频指纹巡检
    ///   （会话内阵营覆灭/新建/领袖更替 → 指纹变 → 重新生成，动态世界漂移兜底）
    /// - 🔴 未配置 LLM（铁律 1）→ 保持 **Idle**（不做高成本枚举）——玩家在 MCM 填好配置后
    ///   下一个 tick **自动触发生成**，无需重进档（2026-08-17 用户询问触发时机后修正）
    /// - 指纹判定：blob 空 或 指纹 ≠ 当前指纹 → 生成；否则置 Done
    /// - 主线程 BuildMaterialSnapshot + 记录战役纪元 → 线程池 ChatOnceAsync（maxTokens 600 /
    ///   30s / 非 JSON——默认 80 token 必截断 300 字）→ 结果入队 → 主线程 Tick 消费：
    ///   校验纪元与指纹 → 解析单段（=== 世界格局 === 标记）→ 写 blob + 指纹 + [WorldBg] 日志。
    ///
    /// 异步纪律：LLM continuation 线程池 → 结果入队 → 主线程 Tick 消费（wheels.d/im.md:
    /// async-over-sync 死锁教训），主线程禁止同步等 LLM。
    /// </summary>
    public class WorldBackgroundBehavior : CampaignBehaviorBase
    {
        /// <summary>首次检查间隔（墙钟秒）：进档后快速触发（2026-08-17 用户裁定）。</summary>
        private const float FirstCheckIntervalSeconds = 5f;
        /// <summary>常规检查间隔（墙钟秒）。🔴 15s（2026-08-17 用户裁定）：生成是秒级~30s 超时的事，
        /// 3s 轮询太频繁——配置就绪且 blob 空时每轮要做王国/文化/英雄枚举 + 指纹计算；
        /// 15s 足够"配置就绪自动触发"的响应性，且把轮询成本摊薄 5 倍。结果消费仍每帧（lock + 状态判断零成本）。
        /// 首次若未成功（未配置/数据未就绪/失败），后续重复检查一律按 15s。</summary>
        private const float GenerateIntervalSeconds = 15f;
        /// <summary>Done 后指纹巡检间隔（墙钟秒，2026-08-17 用户裁定 300s）：动态世界漂移兜底——
        /// 会话内阵营覆灭/新建/领袖更替 → 指纹变 → 重新生成（一档连玩数小时不读档也保持新鲜）。</summary>
        private const float RecheckIntervalSeconds = 300f;
        private const int MaxTokens = 600;
        private const int TimeoutMs = 30000;

        /// <summary>墙钟驱动入口（ImChatView.Tick 每帧调用，Mission/Campaign 双端；null = 未进档）。</summary>
        public static WorldBackgroundBehavior Instance { get; private set; }

        private float _accumDt;
        private bool _firstCheckPassed;  // 首次 5s 快速检查已执行过 → 之后一律 15s 间隔
        private float _recheckDt;        // Done 后指纹巡检累积（≥RecheckIntervalSeconds 比对一次）
        private readonly object _lock = new object();
        private string _result;          // 线程池回写，主线程 Tick 消费
        private bool _resultReady;
        private string _generatedFingerprint;  // 发起生成时的指纹（结果回来后再重算复核）

        /// <summary>状态机（实例字段：进档天然复位）。Failed = 本会话不再重试。</summary>
        public enum State { Idle, Generating, Done, Failed }

        public State CurrentState { get; private set; } = State.Idle;

        #region CampaignBehaviorBase

        public override void RegisterEvents()
        {
            // 仅挂接墙钟驱动入口（不监听 CampaignEvents.TickEvent——暂停时 dt=0 停发，见类注释）
            Instance = this;
        }

        public override void SyncData(IDataStore dataStore)
        {
            string blob = WorldBackgroundStore.Blob;
            string fp = WorldBackgroundStore.Fingerprint;
            dataStore.SyncData("lwn_world_background", ref blob);
            dataStore.SyncData("lwn_world_background_fp", ref fp);
            if (dataStore.IsLoading)
            {
                // 读档复原 + 防御纵深（SaveStringGuard：超长静默写坏存档，上限 30000 字节）
                WorldBackgroundStore.Blob = SaveStringGuard.GuardJson("lwn_world_background", blob ?? "");
                WorldBackgroundStore.Fingerprint = fp ?? "";
                // 🔴 读档初始化检查：确认存档是否带世界背景（空 = 旧档/未生成过 → 下一轮 tick 触发生成）
                DebugLogger.Log($"[WorldBg] 读档初始化：blob={ (string.IsNullOrEmpty(WorldBackgroundStore.Blob) ? "空" : WorldBackgroundStore.Blob.Length + " 字") } 指纹={ WorldBackgroundStore.Fingerprint ?? "(空)" }");
            }
        }

        #endregion

        /// <summary>墙钟帧驱动（ImChatView.Tick 每帧调用——Mission/Campaign 双端，暂停也运转）。
        /// 首次 5s 快速检查，之后 15s 间隔（首次未成功后续一律 15s）；每帧先消费生成结果。
        /// Failed 停止轮询（失败本会话不重试——进档状态复位重新开始）；Done 保留 300s 低频指纹巡检
        /// （会话内阵营覆灭/新建/领袖更替 → 指纹变 → 重新生成，动态世界漂移兜底）。</summary>
        public void OnFrameTick(float dt)
        {
            try
            {
                // 每帧消费生成结果（状态非 Generating 时零开销）
                ConsumeResult();
                // 🔴 失败即收工：停止轮询（防 LLM 宕机重试风暴；进档状态复位重新开始）
                if (CurrentState == State.Failed)
                    return;
                // Done 后低频指纹巡检：300s 墙钟比对一次；指纹变（阵营覆灭/新建/领袖更替）→ 重新生成
                if (CurrentState == State.Done)
                {
                    _recheckDt += dt;
                    if (_recheckDt >= RecheckIntervalSeconds)
                    {
                        _recheckDt = 0f;
                        string reFp = WorldBackgroundProvider.GetFingerprint();
                        // 空指纹 = 枚举异常（GetFingerprint 内部兜底返回 ""）→ 跳过，防误重生成
                        if (!string.IsNullOrEmpty(reFp) && reFp != WorldBackgroundStore.Fingerprint)
                        {
                            DebugLogger.Log($"[WorldBg] 指纹巡检（{RecheckIntervalSeconds}s）：存档指纹={WorldBackgroundStore.Fingerprint} 当前={reFp} 不同 → 重新生成");
                            CurrentState = State.Idle;
                            _accumDt = 0f;
                        }
                    }
                    return;
                }
                float interval = _firstCheckPassed ? GenerateIntervalSeconds : FirstCheckIntervalSeconds;
                _accumDt += dt;
                if (_accumDt < interval) return;
                _accumDt = 0f;
                _firstCheckPassed = true;
                TryGenerate();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldBg] OnCampaignTick error: {ex.Message}");
            }
        }

        /// <summary>检查并驱动生成（主线程；LLM 请求放线程池，结果入队回主线程）。</summary>
        private void TryGenerate()
        {
            try
            {
                // 状态闸门：Generating 防重入；Done/Failed 本会话不再试（进档复位）
                if (CurrentState == State.Generating || CurrentState == State.Done || CurrentState == State.Failed)
                    return;
                // 🔴 铁律 1 前置（2026-08-17 用户询问触发时机后修正）：未配置 LLM → 保持 Idle 等待
                // 配置就绪——玩家在 MCM 填好 BaseUrl/Key/Model 后**自动触发生成**（无需重进档）；
                // 不发请求、不做高成本枚举（配置就绪前跳过数据就绪/指纹计算）
                if (!Settings.Instance.IsLLMConfigured)
                {
                    CurrentState = State.Idle;
                    return;
                }
                // 数据就绪判定：王国/活英雄注册齐全（开档早期可能为空）
                if (Campaign.Current == null || Kingdom.All == null || Kingdom.All.Count == 0
                    || Hero.AllAliveHeroes == null || Hero.AllAliveHeroes.Count == 0)
                    return;

                // 指纹判定：blob 空 或 指纹变了（文化/王国/关键英雄更替、语言切换）→ 生成；否则收工
                string currentFp = WorldBackgroundProvider.GetFingerprint();
                if (!string.IsNullOrEmpty(WorldBackgroundStore.Blob)
                    && !string.IsNullOrEmpty(WorldBackgroundStore.Fingerprint)
                    && WorldBackgroundStore.Fingerprint == currentFp)
                {
                    CurrentState = State.Done;
                    // 🔴 比对值说清楚（2026-08-17 用户要求）：存档指纹 vs 当前指纹（相等才沿用）
                    DebugLogger.Log($"[WorldBg] 指纹匹配，沿用存档世界观（{WorldBackgroundStore.Blob.Length} 字）："
                        + $"存档指纹={WorldBackgroundStore.Fingerprint} 当前指纹={currentFp}"); // lwn-ignore: A（续行中文字符串，日志豁免）
                    return;
                }

                DebugLogger.Log($"[WorldBg] 触发生成：blob空={string.IsNullOrEmpty(WorldBackgroundStore.Blob)} "
                    + $"旧指纹={WorldBackgroundStore.Fingerprint ?? "(空)"} 新指纹={currentFp}"); // lwn-ignore: A

                CurrentState = State.Generating;
                _generatedFingerprint = currentFp;
                WorldBackgroundStore.CurrentCampaignEra = Campaign.Current;
                // 主线程构建快照（引擎对象只读主线程）——LLM 请求发线程池
                string snapshot = WorldBackgroundProvider.BuildMaterialSnapshot();
                string lang = LWNTextHelper.GetReplyLanguageInstruction();
                string prompt = WorldBackgroundProvider.BuildGeneratePrompt(snapshot, lang);

                _ = Task.Run(async () =>
                {
                    string raw = null;
                    try
                    {
                        raw = await LLMService.Instance.ChatOnceAsync(
                            prompt, MaxTokens, 0.7f, disableReasoning: true, timeoutMs: TimeoutMs, needJson: false);
                    }
                    catch { /* ChatOnceAsync 内部已静默，这里兜底 */ }
                    lock (_lock)
                    {
                        _result = raw;
                        _resultReady = true;
                    }
                });
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldBg] 生成启动异常: {ex.Message} → Failed（本会话不再重试）");
                CurrentState = State.Failed;
            }
        }

        /// <summary>主线程每帧消费生成结果（TryGenerate 后由 Tick 驱动）。</summary>
        public void ConsumeResult()
        {
            if (CurrentState != State.Generating) return;
            string raw;
            lock (_lock)
            {
                if (!_resultReady) return;
                raw = _result;
                _resultReady = false;
                _result = null;
            }

            try
            {
                // ① 战役纪元校验：跨战役（读档/新档）污染防护
                if (WorldBackgroundStore.CurrentCampaignEra != Campaign.Current)
                {
                    DebugLogger.Log("[WorldBg] 结果丢弃：战役纪元不符（读档/新档）");
                    CurrentState = State.Idle;   // 新战役重新来过
                    return;
                }
                // ② 指纹复核：生成期间语言切换/领袖更替 → 丢弃（口径错位防误注入）
                string nowFp = WorldBackgroundProvider.GetFingerprint();
                if (nowFp != _generatedFingerprint)
                {
                    DebugLogger.Log($"[WorldBg] 结果丢弃：指纹已变（{_generatedFingerprint} → {nowFp}）");
                    CurrentState = State.Idle;
                    return;
                }
                // ③ 解析单段（=== 世界格局 === 标记）
                string blob = ParseSingleSection(raw);
                if (string.IsNullOrWhiteSpace(blob))
                {
                    DebugLogger.Log("[WorldBg] 解析失败（无 === 世界格局 === 标记或内容为空）→ Failed");
                    CurrentState = State.Failed;
                    return;
                }
                // 上限 500 字（UTF-8 约 1.5KB，远低于 SaveStringGuard 30000）
                if (blob.Length > 500) blob = blob.Substring(0, 500);
                WorldBackgroundStore.Blob = blob;
                WorldBackgroundStore.Fingerprint = nowFp;
                CurrentState = State.Done;
                DebugLogger.Log($"[WorldBg] 生成成功（{blob.Length} 字）：{blob}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldBg] 结果消费异常: {ex.Message} → Failed");
                CurrentState = State.Failed;
            }
        }

        /// <summary>单段解析：取「=== 世界格局 ===」标记后的正文（容错：标记可换行/空格变体）。</summary>
        private static string ParseSingleSection(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            int idx = raw.IndexOf("世界格局", StringComparison.Ordinal); // lwn-ignore: A
            if (idx < 0) return null;
            // 跳过标记行（可能整行 = "=== 世界格局 ===" 或 "=== 世界格局 ===" 后跟正文）
            int after = raw.IndexOf('\n', idx);
            return after < 0 ? raw.Substring(idx).Trim() : raw.Substring(after + 1).Trim();
        }

        /// <summary>调试指令用：清空 blob 强制重生成（状态复位）。</summary>
        public void ForceRegenerate()
        {
            WorldBackgroundStore.Blob = "";
            WorldBackgroundStore.Fingerprint = "";
            _accumDt = GenerateIntervalSeconds;   // 下一 tick 立即触发
            CurrentState = State.Idle;
            DebugLogger.Log("[WorldBg] 强制重新生成（blob 已清空）");
        }
    }
}
