using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════
    // SpeechChannel.cs — 说话并联通道（npc-dialogue-session-plan.md §3，M0）
    //
    // 每个 AgentBrain 旁挂一个 SpeechChannel，与动作队列**完全并联**：
    // 不占 CurrentAction、不进 queue——NPC 在执行任何表现型行动（移动/战斗/
    // 站岗）时都可以说话，且说话不再让 NPC 停下（原 ReactiveSayAction 占队列
    // 播 2.5s 的语义被废弃，2026-08-11 用户裁定）。
    //
    // 输入：SpeechRequest（文本 + 优先级 + 语境 + 时长）
    // 调度：同 agent 气泡不重叠（排队，上限 2 溢出丢最旧）；高优先级抢占当前播放
    // 输出：AgentHudMissionView.AgentSay（既有单一出口：3D 冒泡 + 距离分层 + 附近频道）
    // 全局闸门：同 agent 最小发声间隔（防刷屏纪律，im.md 同款精神）
    //
    // 🔴 双轨契约（2026-08-12 升级，用户拍板"先升级能力，用不用是开关"）：
    //   - `Say` = 纯播放器：入队的 text 必须是**已生成**的文本（LLM 润色完成或模板兜底），
    //     LLM 由调用方在 Say 之前 fire-and-forget 发起（PersuadeSlot / 插嘴 / 社交续话）。
    //     队列/播放逻辑**禁止**加 LLM 同步等待——主线程禁止同步等 LLM（im.md 纪律）。
    //   - `SayPolished` = 统一双轨入口（模板调用点升级用）：有 LLM 且开关开 → **立即**发起
    //     润色（fire-and-forget，budgetS 预算，超时/失败/无配置 → 原模板立即兜底，铁律 1）；
    //     开关关（Settings.PolishSpeechEnabled）/无配置（IsLLMConfigured）→ 直接播模板
    //     （= 升级前行为，零延迟）。润色请求在入口发起、结果回来才入队播放——排队等的是
    //     气泡显示时机，不是 LLM（"先请求再播出"）。
    //
    // 🔴 线程安全（2026-08-12）：见下方 _syncLock——LLM 回调线程可直调 Say。
    // ═══════════════════════════════════════════════════════════════

    /// <summary>说话优先级（同 agent 气泡冲突时高者抢占；100 最高）。</summary>
    public enum SpeechPriority
    {
        Chat = 20,        // 日常回应：无会话的单次回应（模板/LLM）
        Interject = 40,   // 旁观插嘴：seen_speaking 接话
        Dialogue = 60,    // 对话轮次：会话成员发言
        Warning = 80,     // 警戒警告：warn_away / 质问开场 / 犯罪指控
        Combat = 100,     // 战斗喊话：开战/受伤/处决宣言
    }

    /// <summary>说话语境（「说的话符合当前行动」——LLM prompt 与模板选择共用注入）。</summary>
    public struct SpeechContext
    {
        public string StimulusType;   // spoken_to / attacked / seen_crime / approach_by / combat …
        public Agent Speaker;         // 刺激源（玩家或 NPC，不是说话者自己）
        public string Topic;          // 话题（对话会话 topic；战斗 = "战斗"；日常 = null）
        public string CurrentAction;  // 本 agent 当前动作类名（FightEnemyAction/MoveToPositionAction…）
        public string Intent;         // 本 agent 当前 NpcIntentType
        public string LastLine;       // 上一句（对话接力）
        public float Agree;           // 会话内当前倾向（0~1；非对话语境 = 无关）
        public int Round;             // 会话轮次

        /// <summary>
        /// 从说话者的 brain 快照当前动作语境（null-guard 铁律 2）。
        /// 🔴 参数语义（2026-08-12 澄清，防止传反）：
        ///   - brain = 说话者本人的 brain（Owner = 说话者；CurrentAction/Intent 快照取自他）
        ///   - speaker = **刺激源（对方）**——触发我这次说话的谁（玩家/NPC/敌人），**不是说话者自己**；
        ///     无明确刺激源（自发言语：make_noise/bubble/im_message/plan_report）传 null
        /// </summary>
        public static SpeechContext FromBrain(AgentBrain brain, Agent speaker = null, string stimulus = null, string topic = null)
        {
            var ctx = new SpeechContext
            {
                Speaker = speaker,
                StimulusType = stimulus,
                Topic = topic,
            };
            try
            {
                if (brain != null)
                {
                    ctx.CurrentAction = brain.CurrentAction?.GetType().Name;
                    if (brain.CurrentIntent != null)
                        ctx.Intent = brain.CurrentIntent.Type.ToString();
                }
            }
            catch { }
            return ctx;
        }
    }

    /// <summary>一次说话请求（入队单元）。</summary>
    public struct SpeechRequest
    {
        public string Text;              // 台词（LLM 润色后或模板）
        public SpeechPriority Priority;
        public SpeechContext Context;    // 语境（3.2）
        public float Duration;           // 冒泡时长（0 = 按文本长度自动估算）
        // 🔴 2026-08-15（私聊不进附近频道，UI 层过滤）：false = 冒泡照播但不转发附近频道消息流
        //（IM 密信送达冒泡）；NPC 记忆/对话历史链路不受影响（转发是纯 UI 层）。
        public bool ForwardToNearby;     // 默认 true（场景说话 = 玩家亲耳可闻）
    }

    /// <summary>说话并联通道（每 agent 一个；静态注册表按 agent.Index）。</summary>
    public class SpeechChannel
    {
        // ── 静态注册表（agent.Index → channel；AgentAIController.OnAgentDeleted 清理）──
        private static readonly Dictionary<int, SpeechChannel> _registry = new Dictionary<int, SpeechChannel>();
        // 🔴 线程保护（2026-08-12）：既有 LLM 回调（社交续话/PlanReplan）在非主线程直接说话——
        // Get/Say/Enqueue/Tick 对共享状态（注册表 + 实例队列）统一加锁。主线程调用时无竞争，开销可忽略。
        private static readonly object _syncLock = new object();

        private const int QueueLimit = 2;        // 队列上限（溢出丢最旧——气泡不无限堆积）
        private const float MinGapS = 0.6f;      // 同 agent 最小发声间隔（全局闸门，防刷屏）
        private const float MinGapCombatS = 0.4f; // 战斗喊话间隔（更紧：战斗台词优先但不轰炸）

        private readonly Queue<SpeechRequest> _queue = new Queue<SpeechRequest>();
        private readonly Agent _owner;            // 持有 agent（AgentAIController.OnAgentDeleted 清理注册表）
        private SpeechRequest _playing;
        private bool _hasPlaying;
        private float _playTimer;
        private float _lastSayAt;                // Mission 时间（闸门基准）
        private bool _polishing;                 // 润色单飞锁（同 agent 只允许 1 个活跃润色任务）
        private int _polishSeq;                  // 🔴 2026-08-12 抢占序号：新请求递增，旧任务完成时序号不符 → 结果作废不播
        private SpeechPriority _polishPriority;  // 🔴 2026-08-12 在途任务优先级（抢占判定基准）

        private SpeechChannel(Agent owner)
        {
            _owner = owner;
        }

        /// <summary>取（无 → 创建）agent 的说话通道。</summary>
        public static SpeechChannel Get(Agent agent)
        {
            if (agent == null) return null;
            lock (_syncLock)
            {
                if (_registry.TryGetValue(agent.Index, out var ch)) return ch;
                ch = new SpeechChannel(agent);
                _registry[agent.Index] = ch;
                return ch;
            }
        }

        /// <summary>移除（Agent 删除时；AgentAIController.OnAgentDeleted 调用）。</summary>
        public static void Remove(Agent agent)
        {
            if (agent != null)
            {
                lock (_syncLock) { _registry.Remove(agent.Index); }
            }
        }

        /// <summary>统一发声入口（主线程或 LLM 回调线程；线程安全）。高优先级抢占当前播放；同优先级入队排队。
        /// 🔴 2026-08-15（私聊不进附近频道）：forwardToNearby=false = 冒泡照播但不进附近频道消息流
        ///（IM 密信送达冒泡专用；其余调用点默认 true 行为不变）。</summary>
        public static void Say(Agent agent, string text, SpeechPriority priority = SpeechPriority.Chat,
            SpeechContext context = default, float duration = 0f, bool forwardToNearby = true)
        {
            if (agent == null || string.IsNullOrWhiteSpace(text)) return;
            var ch = Get(agent);
            if (ch == null) return;
            lock (_syncLock)
            {
                ch.Enqueue(new SpeechRequest { Text = text, Priority = priority, Context = context, Duration = duration, ForwardToNearby = forwardToNearby });
            }
        }

        /// <summary>
        /// 统一发声入口（模板调用点升级用，2026-08-12 重构）：
        /// 🔴 分级（2026-08-12 用户裁定 + 实机验证）：**只有「有效互动」喊话走 LLM**——
        ///   ① 高优先级（Combat/Warning/Dialogue：战斗喊话/警告质问/当面对话轮次）；
        ///   ② Chat 冒泡但刺激可注入「当前处境」（被攻击/见义勇为/命令等 = 玩家关注的核心互动）。
        ///   纯氛围冒泡（围观警戒等无处境上下文）→ 直接模板（零延迟零成本，模板按场景编写内容贴切；
        ///   实机证明：围观者走 LLM 无上下文 → 台词跑题 + 白花额度，双输）。
        /// 润色请求入口发起（fire-and-forget，预算 clamp 1.5s，超时/失败 → 原模板兜底）。
        /// 单飞 + 抢占：同 agent 已有润色在途 → 高优先级抢占（旧结果作废）、低/等优先级**丢弃**
        /// （不播模板——双轨根源 = 模板先播 + 润色后播，模板只用于超时/未配置）。
        /// 🔴 先请求再播出：结果回来才入队——SpeechChannel 排队等的是气泡时机，不是 LLM。
        /// </summary>
        /// <param name="fallbackText">离线模板文本（永远存在：铁律 1 兜底）</param>
        /// <param name="budgetS">LLM 预算秒数（上限 1.5s：用户要求 2s 内返回、尽量 1.5s；终局时敏台词可传更小）</param>
        public static void SayPolished(Agent agent, string fallbackText, SpeechPriority priority = SpeechPriority.Chat,
            SpeechContext context = default, float budgetS = 2f)
        {
            if (agent == null || string.IsNullOrWhiteSpace(fallbackText)) return;
            // 🔴 分级：非有效互动（无 LLM/开关关/纯氛围冒泡）→ 直接模板（零延迟）
            if (!ShouldPolish(agent, priority, context))
            {
                Say(agent, fallbackText, priority, context);
                return;
            }
            var ch = Get(agent);
            if (ch == null) return;
            int seq;
            lock (_syncLock)
            {
                // 同 agent 单飞 + 抢占（防乱序/刷屏，同时根治双轨）
                if (ch._polishing)
                {
                    if (priority <= ch._polishPriority) return;   // 低/等优先级：丢弃（不播模板）
                    ch._polishSeq++;                              // 高优先级：抢占（旧结果作废）
                }
                else
                {
                    ch._polishing = true;
                    ch._polishSeq++;
                }
                ch._polishPriority = priority;
                seq = ch._polishSeq;
            }
            _ = PolishLineAsync(agent, fallbackText, priority, context, budgetS, seq);
        }

        /// <summary>
        /// 🔴 2026-08-12 分级判定：该喊话是否值得走 LLM（有效互动 vs 氛围冒泡）。
        /// 高优先级（战斗/警告/对话轮次）必 LLM；Chat 冒泡仅当刺激能注入「当前处境」
        /// （= 与玩家/事件的核心互动：被攻击/见义勇为/命令/传讯等）才 LLM；
        /// 纯氛围冒泡（bubble 等无处境注入）→ false = 模板直接播。
        /// </summary>
        private static bool ShouldPolish(Agent agent, SpeechPriority priority, SpeechContext context)
        {
            if (!Settings.Instance.IsLLMConfigured || !Settings.Instance.PolishSpeechEnabled) return false;
            if (priority >= SpeechPriority.Dialogue) return true;   // Dialogue/Warning/Combat
            // Chat/Interject：有「当前处境」注入 = 有效互动
            return BuildSituationLine(agent, context) != null;
        }

        /// <summary>润色后台任务：预算内 LLM 润色 → 成功播润色版，否则播模板（finally 释放单飞锁）。
        /// 🔴 2026-08-12：seq 校验——被抢占的任务完成时不播、不释放（释放权归最新任务）。</summary>
        private static async Task PolishLineAsync(Agent agent, string fallback, SpeechPriority priority,
            SpeechContext context, float budgetS, int seq)
        {
            string polished = null;
            try
            {
                polished = await BuildPolishedLine(agent, fallback, priority, context, budgetS);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Speech] 润色失败: {ex.Message}");
            }
            var ch = Get(agent);
            if (ch == null) return;
            lock (_syncLock)
            {
                // 已被抢占（更新 seq 的任务接管）→ 结果作废：不播、不释放
                if (ch._polishSeq != seq) return;
                ch._polishing = false;
            }
            // 播放（成功 = 润色版；失败/超时/空 = 模板兜底——用户裁定：模板只在这时用）
            Say(agent, string.IsNullOrWhiteSpace(polished) ? fallback : polished, priority, context);
        }

        /// <summary>LLM 润色构造（🔴 2026-08-12 重构）：身份真名 + 对方关系 + 当前处境（刺激/意图/血量）
        /// 注入——有效信息 = 说话者是谁/对方是谁/正在发生什么；模板语义仅作锚防跑题。
        /// 体积控制：每段单行短句，max_tokens 60，预算 clamp 1.5s（用户要求 2s 内返回、尽量 1.5s）。
        /// prompt 材料豁免铁律 13。</summary>
        private static async Task<string> BuildPolishedLine(Agent agent, string fallback, SpeechPriority priority,
            SpeechContext context, float budgetS)
        {
            if (agent == null || !agent.IsActive()) return null;
            try
            {
                string name = agent.Name?.ToString() ?? "";
                string occ = ReactiveAgent.ClassifyOccupation(agent);
                // 本地化：LWN_prompt_trait_occupation_（玩家可见文本）
                string occName = LWNTextHelper.ResolvePrompt("LWN_prompt_trait_occupation_" + occ);
                if (string.IsNullOrEmpty(occName)) occName = occ;
                string personality = ReactiveAgent.DescribePersonalityForPrompt(ReactiveAgent.Get(agent)?.Personality);
                // 身份：真名（职业、人格）——2026-08-12 升级：不再是无名「路人」
                string identity = string.Format(
                    // 本地化：LWN_plan_respond_identity_template（玩家可见文本）
                    DialogueComponent.ResolvePrompt("LWN_plan_respond_identity_template", "你是{0}。{1}。"),
                    string.IsNullOrEmpty(name) ? occName : $"{name}（{occName}）", personality);

                // 有效上下文段（关系 + 处境；null 跳过，单行短句控体积）
                string relation = BuildRelationLine(agent, context.Speaker);
                string situation = BuildSituationLine(agent, context);
                string attitude = "";
                if (!string.IsNullOrEmpty(relation)) attitude += relation + "\n";
                if (!string.IsNullOrEmpty(situation)) attitude += situation;
                if (string.IsNullOrWhiteSpace(attitude)) attitude = null;

                // 语气按优先级（与计划 §3.3 优先级表对应）
                string mood = priority switch
                {
                    SpeechPriority.Combat => "用一句符合你身份和处境的话回应（8-25 字），语气激烈，贴合战况",
                    SpeechPriority.Warning => "用一句符合你身份和处境的话回应（8-30 字），语气强硬",
                    SpeechPriority.Dialogue => "用一句符合你身份和处境的话回应（8-30 字），自然贴合语境",
                    _ => "用一句符合你身份和处境的话回应（8-30 字），口语化，贴合当下",
                };
                string topic = string.IsNullOrEmpty(context.Topic) ? "说话" : context.Topic;
                string anchor = string.IsNullOrEmpty(fallback) ? "" : $"（大意是：{fallback}，可以换更自然的说法，但别偏离意思）";
                var dline = await DialogueComponent.GenerateLine(
                    WorldBackgroundProvider.GetWorldSection(agent), identity, attitude,
                    topic, "",
                    "",
                    context.Speaker?.Name?.ToString() ?? "对方",
                    "", "",
                    // 本地化：LWN_plan_respond_rule（玩家可见文本）
                    "LWN_plan_respond_rule",
                    $"【要求】{mood}。{anchor}直接说台词本身——不要引号、不要解释、不要动作描写。",
                    null, maxTokens: 60, timeoutMs: Math.Max(300, (int)(Math.Min(budgetS, 1.5f) * 1000)),
                    addressSection: PromptBuilder.BuildAddressAndKinshipSections(agent, context.Speaker));
                return dline != null && dline.FromLlm
                    ? DialogueComponent.Sanitize(dline.Reply, agent.Name?.ToString() ?? "")
                    : null;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Speech] 润色构造异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>对方关系段（LLM prompt 材料，豁免铁律 13）：玩家主公 / 玩家 / 同伴 / 友善。null = 不注入。</summary>
        private static string BuildRelationLine(Agent self, Agent other)
        {
            if (other == null) return null;
            try
            {
                string otherName = other.Name?.ToString() ?? "对方";
                if (other == Agent.Main)
                {
                    // 对方是玩家：自己是队伍成员 → 主公（主从关系是核心语境：被主公打 vs 被陌生人打）
                    bool isCompanion = FriendlinessHelper.IsPlayerPartyMember(self);
                    return isCompanion
                        ? $"【对方关系】对方是你的主公{otherName}（玩家）"
                        : $"【对方关系】对方是玩家{otherName}";
                }
                if (FriendlinessHelper.IsPlayerPartyMember(other))
                    return $"【对方关系】对方是你的同伴{otherName}";
                if (FriendlinessHelper.IsFriendlyToPlayer(other))
                    return $"【对方关系】对方与你友善（{otherName}）";
                return null;   // 敌对/陌生：不给多余关系（台词由处境段自然表达）
            }
            catch { return null; }
        }

        /// <summary>当前处境段（刺激/意图/血量 → 一句「正在发生什么」）。null = 不注入。</summary>
        private static string BuildSituationLine(Agent self, SpeechContext context)
        {
            try
            {
                string hp = "";
                if (self != null && self.IsActive() && self.HealthLimit > 0)
                {
                    float ratio = self.Health / self.HealthLimit;
                    if (ratio < 0.3f) hp = "，你已筋疲力尽";
                    else if (ratio < 0.6f) hp = "，你身上带伤";
                }
                string intent = context.Intent ?? "";
                string action = context.CurrentAction ?? "";
                string stim = context.StimulusType ?? "";
                if (intent == "Surrendering")
                    return $"【当前处境】你正在向对方认输求饶{hp}";
                if (intent == "Fighting" || action == "FightEnemyAction")
                    return $"【当前处境】你正在与对方厮杀{hp}";
                switch (stim)
                {
                    case "attacked": return $"【当前处境】对方刚刚对你动手，你正要反击{hp}";
                    case "combat": return $"【当前处境】战斗刚刚爆发，你正要出手{hp}";
                    case "approach_by": return "【当前处境】对方正在向你靠近";
                    case "seen_crime": return "【当前处境】你刚目睹了对方的不法行为";
                    case "spoken_to": return "【当前处境】对方刚刚跟你搭话";
                    case "plan_command": return "【当前处境】主公刚给你下达了命令";
                    case "plan_report": return "【当前处境】你正在向主公汇报任务结果";
                    case "im_message": return "【当前处境】对方刚给你发来消息";
                    default: return null;
                }
            }
            catch { return null; }
        }

        /// <summary>所有通道推进（AgentAIController.OnMissionTick 驱动）。</summary>
        public static void TickAll(float dt)
        {
            if (_registry.Count == 0) return;
            // 快照迭代（OnTick 内可能触发新 Enqueue，安全）
            List<SpeechChannel> snapshot;
            lock (_syncLock) { snapshot = new List<SpeechChannel>(_registry.Values); }
            foreach (var ch in snapshot)
            {
                try
                {
                    lock (_syncLock) { ch.Tick(dt); }
                }
                catch { }
            }
        }

        /// <summary>Mission 结束清理（AgentAIController.OnRemoveBehavior 调用）。</summary>
        public static void ClearAll()
        {
            lock (_syncLock) { _registry.Clear(); }
        }

        // ── 实例 ──

        private void Enqueue(SpeechRequest req)
        {
            float now = Mission.Current != null ? Mission.Current.CurrentTime : 0f;

            // 全局闸门：同 agent 最小发声间隔（Combat 稍紧）
            float gap = req.Priority >= SpeechPriority.Combat ? MinGapCombatS : MinGapS;
            if (_hasPlaying && now - _lastSayAt < gap)
            {
                // 间隔内：高优先级仍可抢占（战斗喊话打断日常嘀咕）
                if (req.Priority < _playing.Priority) return;
            }

            // 高优先级抢占当前播放（低优先级被挤掉；播放器不产生队列动作，纯丢弃安全）
            if (_hasPlaying && req.Priority > _playing.Priority)
            {
                _hasPlaying = false;
                _playTimer = 0f;
            }

            // 排队（上限 2，溢出丢最旧）
            if (_queue.Count >= QueueLimit)
                _queue.Dequeue();

            if (!_hasPlaying)
            {
                _playing = req;
                _hasPlaying = true;
                _playTimer = 0f;
                _lastSayAt = now;
                Play(req);
            }
            else
            {
                _queue.Enqueue(req);
            }
        }

        private void Tick(float dt)
        {
            if (!_hasPlaying) return;
            _playTimer += dt;
            float duration = _playing.Duration > 0f
                ? _playing.Duration
                : EstimateDuration(_playing.Text);
            if (_playTimer < duration) return;

            _hasPlaying = false;
            _playTimer = 0f;
            if (_queue.Count > 0)
            {
                var next = _queue.Dequeue();
                _playing = next;
                _hasPlaying = true;
                _lastSayAt = Mission.Current != null ? Mission.Current.CurrentTime : 0f;
                Play(next);
            }
        }

        /// <summary>播放 = AgentHudMissionView.AgentSay（单一出口：3D 冒泡 + 距离分层 + 附近频道转发）。
        /// 🔴 前因日志（2026-08-11）：说话日志统一由 AgentSay 入口打（[Say] 全覆盖），本方法只负责把
        /// 「为什么说这句」序列化传过去——刺激/当前行动/意图/话题/倾向/轮次/优先级。各调用点只需传对
        /// SpeechContext，排查时不再靠各调用点自觉打日志；从日志「{agent}: {text} ← 前因」可反查。</summary>
        private void Play(SpeechRequest req)
        {
            try
            {
                if (_owner == null || !_owner.IsActive()) return;
                // 前因序列化：只收非空字段（context 未传 = 调用点没给前因，仅打优先级兜底）
                var c = req.Context;
                string reason = $"优先级={req.Priority}";
                if (!string.IsNullOrEmpty(c.StimulusType)) reason += $" 刺激={c.StimulusType}";
                if (!string.IsNullOrEmpty(c.CurrentAction)) reason += $" 行动={c.CurrentAction}";
                if (!string.IsNullOrEmpty(c.Intent)) reason += $" 意图={c.Intent}";
                if (!string.IsNullOrEmpty(c.Topic)) reason += $" 话题={c.Topic}";
                if (c.Round > 0) reason += $" 轮次={c.Round}";
                if (c.Agree > 0f) reason += $" 倾向={c.Agree:F2}";
                AgentHudMissionView.AgentSay(_owner, req.Text, reason, req.ForwardToNearby);
            }
            catch { }
        }

        /// <summary>冒泡时长估算（与 SayInlineState 单句模式同款：2s + 字长×0.12，clamp 2~6s）。</summary>
        private static float EstimateDuration(string text)
        {
            float len = string.IsNullOrEmpty(text) ? 0 : text.Length;
            return MathF.Min(MathF.Max(2f + len * 0.12f, 2f), 6f);
        }
    }
}
