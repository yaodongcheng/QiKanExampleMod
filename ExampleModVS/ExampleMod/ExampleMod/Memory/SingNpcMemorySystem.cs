using LivingWorldNpcs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

#pragma warning disable CS0618 // Intentional migration: uses deprecated NpcInitiative
namespace LivingWorldNpcs
{
    public class SingNpcMemorySystem
    {
        // NPC 个人资料
        public readonly NPCProfile _profile;
        // 1. 近期对话历史 (保留容量随互动热度动态分档)
        public List<ChatMessage> RecentHistory { get; private set; } = new List<ChatMessage>();
        //实际上，这里的历史记录可以更多一些,比如翻倍，等到MaxRecentHistoryCount*2条时触发记忆维护

        // 2. 近期记忆，由近期对话历史总结而来，先进先出，最多 MaxDynamicMemoryCount 条，每条30字以内
        public LinkedList<RecentMemory> DynamicMemories { get; private set; } = new LinkedList<RecentMemory>();

        // 3. 远期记忆 (上限 MaxPermanentLength 字)
        public StringBuilder PermanentMemory { get; private set; } = new StringBuilder();
        private volatile bool _isSummarizing = false; // 新增标记

        // 🔴 2026-08-16（方案 N）：大事记槽（≤12 条 FIFO）——写入时 C# 白名单分级锚定，
        // 平行于 LLM 淘汰晋升（CheckAndPromoteToPermanent 保留不动）。建国/获封/大婚等大事
        // 不被日常进城挤掉（D 感知闸门每日 30 条 vs 动态记忆 FIFO 8 条）。
        // 存档按 save 纪律（AllNpcMemoryManager.NpcMemorySaveEntry）；旧档无字段 → 空（不补写，
        // 正确——旧档玩家没有"大事记"记忆；空集 → prompt 不注入该段，零开销）。
        private const int MaxImportantEvents = 12;
        public List<string> ImportantEvents { get; private set; } = new List<string>();

        /// <summary>写入一条大事记（方案 N1：D2 感知层大事双写——RecordDynamicMemory + RecordImportantMemory；
        /// 大事 = kingdom_created/fief_granted/marriage/child_born/imprison/release/
        /// 限定版 battle_win（攻城战胜利或大捷：参战人数比 ≥2 或 SiegeEvent 相关）才进大事记——
        /// 防玩家打 12 仗后建国/获封被挤掉，N 的初衷（大事不被日常挤掉）自毁）。</summary>
        public void RecordImportantMemory(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            lock (_lock)
            {
                ImportantEvents.Add(content);
                if (ImportantEvents.Count > MaxImportantEvents)
                    ImportantEvents.RemoveAt(0);
            }
        }

        // ── 动态容量（用户决策 3：互动热度分档，Hot > Normal > Cold；模板 NPC 无热度维持 Normal 现状）──
        private bool? _isHeroMemory;

        /// <summary>是否 Hero 记忆（按 StringId 能在存活英雄中找到；缓存一次）。模板 NPC（TEMP/无 Hero）→ 永远 Normal 容量。</summary>
        private bool IsHeroMemory
        {
            get
            {
                if (_isHeroMemory == null)
                {
                    bool found = false;
                    try
                    {
                        if (_profile != null && !string.IsNullOrEmpty(_profile.StringId))
                            found = TaleWorlds.CampaignSystem.Hero.AllAliveHeroes?.Any(h => h != null && h.StringId == _profile.StringId) == true;
                    }
                    catch { }
                    _isHeroMemory = found;
                }
                return _isHeroMemory.Value;
            }
        }

        private int ComputeCap(int hot, int normal, int cold)
        {
            if (!IsHeroMemory) return normal;
            switch (ImHeatTracker.TierOf(_profile.StringId))
            {
                case ImHeatTier.Hot: return hot;
                case ImHeatTier.Cold: return cold;
                default: return normal;
            }
        }

        /// <summary>对话历史容量（轮数）：Hot 20 / Normal 10（现状）/ Cold 4。</summary>
        public int MaxRecentHistoryCount => ComputeCap(20, 10, 4);

        /// <summary>动态记忆容量（条数）：Hot 8 / Normal 5（现状）/ Cold 2。</summary>
        public int MaxDynamicMemoryCount => ComputeCap(8, 5, 2);

        /// <summary>永久记忆容量（字符）：Hot 500 / Normal 300（现状）/ Cold 100。</summary>
        public int MaxPermanentLength => ComputeCap(500, 300, 100);

        //开场白
        public NpcInitiative CurrentInitiative  = null;

        // [新增] 待处理的冲突/说服需求
        public PendingConflict ActiveConflict { get; set; } = null;
        //谈判状态，之后会代替说服任务
        public NegotiationState CurrentNegotiationState;

        // 4. 全局新闻 (外部注入)
        public string GlobalNews { get; set; } = "";
       

        private readonly object _lock = new object();

        // 事件传闻
        public List<NewsSpreadSystem.KnownEvent> KnownEvents { get; set; } = new List<NewsSpreadSystem.KnownEvent>();

        /// <summary>记忆维护失败时抑制玩家红字提示（§八/D4：随从对话触发的总结失败应静默，防打扰 + 防 429 弹窗）。
        /// 玩家对话路径默认 false 保持现状（连接失败要明确告知）。</summary>
        public bool SuppressFailureAlerts { get; set; }

        //人生目标
        public string CurrentGoal { get; set; } = "维持现状";
        // 4. 对玩家的隐藏态度 (独立于游戏原本的好感度)
        public int HiddenAttitudeTowardPlayer { get; set; } = 0;

        /// <summary>
        /// 当前最紧迫的世界事件（作为加害方或受害者）。
        /// null = 无事件缠身。
        /// 由 WorldEventDatabase 在事件创建/解决/过期时同步推送，是本 NPC "当前最关切的事"。
        /// 对话系统、委托系统、UI 选项等均从此读取，不再各自查询全局数据库。
        /// </summary>
        public WorldEvent CurrentUrgentEvent { get; set; } = null;

        /// <summary>
        /// 委托/Quest 历史记录。独立于对话历史的专用存储，
        /// 记录此 NPC 经手过的所有 Issue 接取、Quest 完成、因果链上下文。
        /// 最大 20 条，UI 通过"委托记录"Tab 查看。
        /// 因果引擎的 MapQuestToId / ExtractCausalityContext 均从此读取。
        /// </summary>
        public List<QuestRecord> QuestHistory { get; private set; } = new List<QuestRecord>();
        private const int MaxQuestHistoryCount = 20;



        public SingNpcMemorySystem(NPCProfile profile)
        {
            _profile = profile;
        }

        //基于最近听到的传闻，来决定是否要找玩家麻烦，生成说服任务PersuadeInfo
        public bool CheckAndGeneratePersuadeInfo()
        {
            // 如果已经有正在进行的谈判状态，或者已经有一个待处理冲突，先不覆盖
            if (CurrentNegotiationState != null || ActiveConflict != null)
                return ActiveConflict != null;
           

            if (KnownEvents.Count == 0) return false;

            // 1. 查找跟“玩家”有关的，且关注度最高的负面事件
            // 我们假设 severity > 50 且包含负面 Tag 的才算冲突

            string playerID = Hero.MainHero.StringId;

            var targetEventItem = KnownEvents
                .Where(e => e.PerceivedSeverity > 50) // 必须足够严重
                .OrderByDescending(e => e.PerceivedSeverity) // 优先处理最严重的
                .FirstOrDefault();

            if (targetEventItem == null) return false;

            SocialEvent sevt = NewsSpreadSystem.Instance.GetEventById(targetEventItem.EventId);
            if (sevt == null) return false;

            // 2. 分析角色关系 (Role Analysis)
            bool playerIsInitiator = sevt.InitiatorId == playerID;
            bool playerIsVictim = sevt.VictimId == playerID;
            bool npcIsVictim = sevt.VictimId == _profile.StringId;
            bool npcIsInitiator = sevt.InitiatorId == _profile.StringId;
            bool npcIsWitness = sevt.WitnessId.Contains(_profile.StringId);

            // 简单的 Tag 判断
            bool isDishonorable = sevt.Tags.Contains("Dishonorable") || sevt.Tags.Contains("Insulting") || sevt.Tags.Contains("Harassment");

            // 如果玩家完全无关，那这就是个八卦，不需要“说服” (除非你是正义使者，这里暂略)
            if (!playerIsInitiator && !playerIsVictim)
            {
                // 可以在这里设置一个 ChatTopic，比如 "Gossip_About_Nobunaga"，但不是 PersuadeInfo
                return false;
            }

            string goalDesc = "";
            string topicName = "";
            NegotiationGoalType goalType = NegotiationGoalType.None;

            // === 场景 1: 玩家干了坏事，NPC 是受害者 ===
            if (playerIsInitiator && npcIsVictim && isDishonorable)
            {
                topicName = "要求赔偿与道歉";
                goalDesc = $"说服{_profile.Name}原谅你的行为，并接受你的道歉或赔偿";
                goalType = NegotiationGoalType.ResolveConflict_Apology;
            }
            // === 场景 2: 玩家干了坏事，NPC 是正义路人/目击者/亲属 ===
            else if (playerIsInitiator && !npcIsVictim && isDishonorable)
            {
                topicName = "解释";
                goalDesc = $"向{_profile.Name}解释你为什么要对{sevt.VictimName}做出这种事";
                goalType = NegotiationGoalType.ResolveConflict_Explain;
            }
            // === 场景 3: 玩家是受害者，NPC 是肇事者 (NPC主动想洗白或者挑衅) ===
            else if (playerIsVictim && npcIsInitiator)
            {
                // 如果 NPC 性格嚣张
                if (_profile.PersonalityTraits.Contains("Arrogant") || _profile.PersonalityTraits.Contains("Impulsive"))
                {
                    topicName = "挑衅与施压";
                    goalDesc = $"面对{_profile.Name}的再次挑衅，你需要威慑他或者通过智慧让他闭嘴";
                }
                else
                {
                    topicName = "请求宽恕";
                    goalDesc = $"判断{_profile.Name}是否真心悔过，并决定是否原谅他";
                }
            }

            if (goalType != NegotiationGoalType.None)
            {
                // [关键修改] 不再创建 PersuadeInfo，而是创建 PendingConflict
                ActiveConflict = new PendingConflict(
                    sevt.EventId,
                    topicName,
                    goalDesc,
                    targetEventItem.PerceivedSeverity,
                    goalType
                );

                DebugLogger.Log($"[Conflict System] 生成了新的冲突: {topicName}");
                return true;
            }

            return false;


            
        }

        public bool ReceiveNews(string eventId, float severity, int decayCount)
        {
            //返回值表示是否继续往后面传播

            // 1. 过滤：不是很重要的，直接忽略 (逻辑来自原 AddKnowledge)
            if (severity < 20) return false;

            // 2. 过滤：传了好几手的，信息早已失真，忽略
            if (decayCount > 3) return false;


            // 检查是否已存在，逻辑同你之前的 NewsSpreadSystem.AddKnowledge
            var existing = KnownEvents.FirstOrDefault(e => e.EventId == eventId);
            if (existing == null)
            {
                KnownEvents.Add(new NewsSpreadSystem.KnownEvent
                {
                    EventId = eventId,
                    PerceivedSeverity = severity,
                    DecayCounter = decayCount
                });
                return true;
            }
            else
            {
                //已经听说过
                if (severity > existing.PerceivedSeverity * 0.8f)
                {
                    // 稍微增加一点关注度，强化记忆
                    existing.PerceivedSeverity += severity * 0.1f;
                    // 封顶 100
                    if (existing.PerceivedSeverity > 100) existing.PerceivedSeverity = 100;                    
                    return false;
                }
            }
            return false;
        }


        public void AddHistory(string Role, string content)
        {
            AddHistory(Role, content, null);
        }

        /// <summary>带说话人标识写入对话历史（§八 任意人对话泛化）。</summary>
        public void AddHistory(string Role, string content, string speakerId)
        {
            lock (_lock)
            {
                RecentHistory.Add(new ChatMessage(Role, content, speakerId));
            }

            _ = MaintainMemoryAsync();
        }

        /// <summary>
        /// 🔴 2026-08-11：同步写入一条动态记忆（主线程确定性事件用：战斗结果/切磋胜负等）。
        /// 与 LLM 总结管道（AddDynamicMemory）的区别：不触发耗时的 fade 重总结，锁内 FIFO + 超限淘汰。
        /// 通道语义：动态记忆进 prompt 的【近期回忆】段（GetPrompt_RespondContext 最新 2 条，
        /// IM 私聊/当面对话都带），且**不渲染为私聊聊天行**（GetDirectMessages 只认 im_user/im_npc 角色）——
        /// 战斗结果这样"NPC 该知道但没说出口"的事实正适合走这里，交给 LLM 用自己口吻说出来。
        /// </summary>
        public void RecordDynamicMemory(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock (_lock)
            {
                DynamicMemories.AddLast(new RecentMemory(content, now, now));
                if (DynamicMemories.Count > MaxDynamicMemoryCount)
                    DynamicMemories.RemoveFirst();
            }
        }

        // ── 经历旁白（Experience Narration，2026-08-11）──
        // 会话级（不存档）：AgentBrain 事件决策点写入的第一人称经历（"我遭到X的攻击"），
        // prompt【近期经历】段直接读最新几条；超限 → MaintainNarrationAsync 总结进 DynamicMemories（持久化）。
        private readonly List<RecentMemory> _narration = new List<RecentMemory>();
        /// <summary>旁白容量：超过 2× 触发 LLM 总结；硬上限 3× 丢弃最旧（防 LLM 故障期无界增长）。</summary>
        public const int MaxNarrationCount = 20;
        private volatile bool _isNarrating = false;

        /// <summary>
        /// 🔴 2026-08-11：主线程同步写入一条经历旁白（AgentBrain 事件决策点调用）。
        /// 通道语义：旁白进 prompt 的【近期经历】段（GetPrompt_RespondContext 最新 3 条），
        /// 且**不渲染为私聊聊天行**（GetDirectMessages 只认 im_user/im_npc 角色）——
        /// "NPC 亲身经历但没说出口"的事实（被攻击/目击/奉命）走这里，写 RecentHistory 会出现玩家没见过的幽灵消息。
        /// 内容 = 第一人称 LLM prompt 材料（豁免铁律 13），中性表述交给 LLM 调口吻。
        /// 不阻塞主线程：LLM 总结 fire-and-forget，LLM 不可用时静默失败（铁律 1）。
        /// </summary>
        public void RecordNarration(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock (_lock)
            {
                _narration.Add(new RecentMemory(content, now, now));
                // 硬上限保护（LLM 故障期旁白不会无界增长）：超 3× 丢最旧到 2×
                if (_narration.Count > MaxNarrationCount * 3)
                {
                    _narration.RemoveRange(0, _narration.Count - MaxNarrationCount * 2);
                    DebugLogger.Log($"[Narration] {_profile?.Name} 旁白硬裁剪（LLM 总结未跟上）→ {_narration.Count} 条");
                }
                if (_narration.Count >= MaxNarrationCount * 2)
                    _ = MaintainNarrationAsync();
            }
        }

        /// <summary>线程安全的旁白快照（prompt 读取用；与 SnapshotDynamicMemories 同理）。</summary>
        public List<RecentMemory> SnapshotNarrationLog()
        {
            lock (_lock)
            {
                return _narration.ToList();
            }
        }

        /// <summary>
        /// 旁白总结（镜像 MaintainMemoryAsync 模式）：取最旧一批（count - MaxNarrationCount）→
        /// LLM 总结 → 成功才移除（解析失败作废保留，防污染，同对话历史纪律）→ 进 DynamicMemories（持久化）。
        /// </summary>
        private async Task MaintainNarrationAsync()
        {
            List<RecentMemory> linesToSummarize = null;
            double timeStamp_Start = 0, timeStamp_End = 0;
            lock (_lock)
            {
                if (_narration.Count > MaxNarrationCount)
                    linesToSummarize = _narration.Take(_narration.Count - MaxNarrationCount).ToList();
            }
            if (linesToSummarize == null || linesToSummarize.Count == 0) return;
            if (_isNarrating) return;
            _isNarrating = true;
            try
            {
                foreach (var m in linesToSummarize)
                {
                    if (timeStamp_Start == 0 || m.TimeStamp_Start < timeStamp_Start) timeStamp_Start = m.TimeStamp_Start;
                    if (m.TimeStamp_End > timeStamp_End) timeStamp_End = m.TimeStamp_End;
                }
                string summaryPrompt = PromptBuilder.BuildPromptForNarrationSummary(this, linesToSummarize);
                string jsonResponse = await LLMService.Instance.SummarizeAsync(summaryPrompt, showFailureAlert: !SuppressFailureAlerts);
                jsonResponse = LLMService.CleanJson(jsonResponse);
                LLSSummaryResponse response;
                try
                {
                    response = JsonConvert.DeserializeObject<LLSSummaryResponse>(jsonResponse);
                    if (response == null) throw new Exception("Empty JSON");
                }
                catch (Exception)
                {
                    // 🔴 同对话历史总结纪律：解析失败 → 作废本轮总结（旁白保留，下次超限重试）
                    DebugLogger.Log($"[警告] 旁白总结 JSON 解析失败，作废本轮（旁白保留）：{jsonResponse}");
                    return;
                }
                string summaryContent = response.Summary;
                if (string.IsNullOrWhiteSpace(summaryContent)) return;
                lock (_lock)
                {
                    int countToRemove = linesToSummarize.Count;
                    if (_narration.Count >= countToRemove)
                    {
                        _narration.RemoveRange(0, countToRemove);
                        DebugLogger.Log($"[Narration] {_profile?.Name} 旁白总结完成，移除 {countToRemove} 条 → {_narration.Count} 条");
                    }
                }
                await AddDynamicMemory(new RecentMemory(summaryContent, timeStamp_Start, timeStamp_End));
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Narration] {_profile?.Name} 旁白总结失败: {ex.Message}");
            }
            finally
            {
                _isNarrating = false;
            }
        }

        /// <summary>读档重建（MyBehavior.SyncData → AllNpcMemoryManager.DeserializeSlot 调用）。</summary>
        public void RestoreFromSave(List<ChatMessage> history, List<RecentMemory> dynamic, string permanent,
            string backgroundStory = null, string personality = null, string specialty = null,
            List<string> importantEvents = null)
        {
            lock (_lock)
            {
                if (history != null)
                {
                    RecentHistory.Clear();
                    RecentHistory.AddRange(history);
                }
                if (dynamic != null)
                {
                    DynamicMemories.Clear();
                    foreach (var d in dynamic)
                    {
                        if (d != null) DynamicMemories.AddLast(d);
                    }
                }
                PermanentMemory.Clear();
                if (!string.IsNullOrEmpty(permanent))
                    PermanentMemory.Append(permanent);
                // 人设三字段（常驻人设）：旧存档无此字段 → null 不覆盖（保持兼容；
                // 旧档只有身世 → 其余字段空，懒触发补生成，生成输入含已有身世保持稳定）
                if (!string.IsNullOrEmpty(backgroundStory))
                    BackgroundStory = backgroundStory;
                if (!string.IsNullOrEmpty(personality))
                    Personality = personality;
                if (!string.IsNullOrEmpty(specialty))
                    Specialty = specialty;
                // 🔴 2026-08-16（方案 N）：大事记槽读档（旧档无字段 → 空，不补写——正确）
                if (importantEvents != null)
                {
                    ImportantEvents = new List<string>(importantEvents);
                    if (ImportantEvents.Count > MaxImportantEvents)
                        ImportantEvents = ImportantEvents.GetRange(ImportantEvents.Count - MaxImportantEvents, MaxImportantEvents);
                }
            }
        }

        /// <summary>
        /// 线程安全的历史快照（IM 显示轮询用）：🔴 MaintainMemoryAsync 的 LLM 续体在线程池线程
        /// 锁内 RemoveRange——主线程直接 foreach RecentHistory 会在极窄窗口抛 InvalidOperationException。
        /// 读取端一律走快照。
        /// </summary>
        public List<ChatMessage> SnapshotRecentHistory()
        {
            lock (_lock)
            {
                return RecentHistory.ToList();
            }
        }

        /// <summary>线程安全的动态记忆快照（IM 淡忘断层检测用，与 SnapshotRecentHistory 同理）。</summary>
        public List<RecentMemory> SnapshotDynamicMemories()
        {
            lock (_lock)
            {
                return DynamicMemories.ToList();
            }
        }

        /// <summary>
        /// 添加一条委托记录到 QuestHistory。自动维护上限（20 条）。
        /// </summary>
        public void AddQuestRecord(QuestRecord record)
        {
            if (record == null) return;
            lock (_lock)
            {
                QuestHistory.Add(record);
                while (QuestHistory.Count > MaxQuestHistoryCount)
                    QuestHistory.RemoveAt(0);
            }
        }

        /// <summary>
        /// 从 QuestHistory 中查找最近一条指定类型的记录。
        /// </summary>
        public QuestRecord FindLatestQuestRecord(string recordType)
        {
            lock (_lock)
            {
                for (int i = QuestHistory.Count - 1; i >= 0; i--)
                {
                    if (QuestHistory[i].RecordType == recordType)
                        return QuestHistory[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 从 QuestHistory 中查找最近一条 QuestIssued 或 Causality 记录（用于 MapQuestToId）。
        /// </summary>
        public QuestRecord FindLatestQuestIssued()
        {
            lock (_lock)
            {
                for (int i = QuestHistory.Count - 1; i >= 0; i--)
                {
                    var r = QuestHistory[i];
                    if (r.RecordType == "Issued" || r.RecordType == "Causality")
                        return r;
                }
            }
            return null;
        }

        public string GetPersonaPrompt()
        {
            // 🔴 人设精炼懒触发（2026-08-10）：第一次有对话素材时异步生成三字段常驻人设
            // （身世/性格/本事）；当前回复先用旧人设，生成成功后下次对话自动拼入。幂等，不阻塞。
            EnsureProfileSummary();
            var sb = new StringBuilder(_profile.GetPersonaPrompt());
            if (!string.IsNullOrEmpty(BackgroundStory))
                sb.AppendLine("\n" + LWNTextHelper.ResolveText("LWN_prompt_section_background", "## My Story") + "\n" + BackgroundStory); // lwn-ignore: B
            if (!string.IsNullOrEmpty(Personality))
                sb.AppendLine("\n" + LWNTextHelper.ResolveText("LWN_prompt_section_personality", "## My Temperament") + "\n" + Personality); // lwn-ignore: B
            if (!string.IsNullOrEmpty(Specialty))
                sb.AppendLine("\n" + LWNTextHelper.ResolveText("LWN_prompt_section_specialty", "## My Skills") + "\n" + Specialty); // lwn-ignore: B
            return sb.ToString();
        }

        // ───────────────────────── 人设精炼（常驻三字段，2026-08-10） ─────────────────────────
        // 背景：招募台词等素材在 RecentHistory 里会被挤出；性格/技能是引擎真实数值但 LLM 用不好
        // 硬数字。方案：第一次有素材时一次 LLM 调用精炼成三字段第一人称人设，此后每次对话拼入：
        //   BackgroundStory 身世（过往） / Personality 性格（数值→人话） / Specialty 本事（技能→人话）。
        // 🔴 必须存档（NpcMemorySaveEntry 管道，见 AllNpcMemoryManager）——不存档每次读档重生成，
        // 重复烧 LLM 且人设漂移。旧存档只有 BackgroundStory → 读档后补生成其余字段（生成输入含已有值）。

        public string BackgroundStory { get; set; } = "";
        public string Personality { get; set; } = "";
        public string Specialty { get; set; } = "";

        // 生成防重 + 失败冷却（LLM 挂了不会每句话都重试）
        private static readonly HashSet<string> _generatingStories = new HashSet<string>();
        private static readonly Dictionary<string, double> _storyAttemptAt = new Dictionary<string, double>();
        private static readonly object _storyLock = new object();
        private const double StoryRetryCooldownSeconds = 300; // 失败后 5 分钟冷却再试

        /// <summary>懒触发人设精炼（幂等）：三字段齐备 / 正在生成 / 冷却中 / 无素材 / LLM 未配置 → 跳过。
        /// 触发成功落日志（[人设精炼] 触发）——排查"为什么没生成"看失败/跳过侧日志。</summary>
        public void EnsureProfileSummary()
        {
            if (!string.IsNullOrEmpty(BackgroundStory)
                && !string.IsNullOrEmpty(Personality)
                && !string.IsNullOrEmpty(Specialty)) return;
            if (!Settings.Instance.IsLLMConfigured) return; // 铁律 1：LLM 不可用不阻塞
            if (SnapshotRecentHistory().Count == 0) return; // 无素材（招募台词等还没进记忆）
            string id = _profile?.StringId ?? "";
            if (string.IsNullOrEmpty(id)) return;
            lock (_storyLock)
            {
                if (_generatingStories.Contains(id)) return;
                double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (_storyAttemptAt.TryGetValue(id, out var last) && now - last < StoryRetryCooldownSeconds) return;
                _generatingStories.Add(id);
                _storyAttemptAt[id] = now;
            }
            DebugLogger.Log($"[人设精炼] {_profile?.Name} 触发生成（素材 {SnapshotRecentHistory().Count} 条，缺 身世={string.IsNullOrEmpty(BackgroundStory)} 性格={string.IsNullOrEmpty(Personality)} 本事={string.IsNullOrEmpty(Specialty)}）");
            _ = GenerateProfileSummaryAsync();
        }

        private async Task GenerateProfileSummaryAsync()
        {
            string id = _profile?.StringId ?? "";
            try
            {
                string prompt = PromptBuilder.BuildPromptForProfileSummary(this);
                // 🔴 2026-08-10 日志实锤：SummarizeAsync 默认 max_tokens=50 且不关 reasoning——
                // 50 token 被 reasoning_content 占满 → content 空 → "Model returned empty whitespace" 三连败。
                // 三字段 JSON 人设必须 300 token + 关闭思考模式。
                string json = await LLMService.Instance.SummarizeAsync(prompt, showFailureAlert: false,
                    maxTokens: 300, disableReasoning: true);
                json = LLMService.CleanJson(json);
                var resp = JsonConvert.DeserializeObject<ProfileSummaryResponse>(json);
                bool any = false;
                if (resp != null)
                {
                    string story = resp.BackgroundStory?.Trim();
                    string person = resp.Personality?.Trim();
                    string spec = resp.Specialty?.Trim();
                    lock (_lock)
                    {
                        if (!string.IsNullOrWhiteSpace(story))
                        {
                            BackgroundStory = story.Length > 100 ? story.Substring(0, 100) : story;
                            any = true;
                        }
                        if (!string.IsNullOrWhiteSpace(person))
                        {
                            Personality = person.Length > 60 ? person.Substring(0, 60) : person;
                            any = true;
                        }
                        if (!string.IsNullOrWhiteSpace(spec))
                        {
                            Specialty = spec.Length > 60 ? spec.Substring(0, 60) : spec;
                            any = true;
                        }
                    }
                    if (any)
                        DebugLogger.Log($"[人设精炼] {_profile?.Name} 完成 身世={BackgroundStory.Length}字 性格={Personality.Length}字 本事={Specialty.Length}字");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[人设精炼] {_profile?.Name} 生成失败: {ex.Message}（{StoryRetryCooldownSeconds}s 冷却后重试）");
            }
            finally
            {
                lock (_storyLock)
                {
                    _generatingStories.Remove(id);
                }
            }
        }
       
        public SocialEvent ParseSocialEventJson(string jsonResponse)
        {
            try
            {
                // 1. 处理 "NONE" 的情况
                if (string.IsNullOrWhiteSpace(jsonResponse) || jsonResponse.Trim().ToUpper().Contains("None"))
                {
                    return null;
                }

                string cleanJson = LLMService.CleanJson(jsonResponse);

                // 3. 反序列化
                SocialEvent evt = JsonConvert.DeserializeObject<SocialEvent>(cleanJson);

                // 4. 后处理与校验 (Post-processing)
                if (evt != null)
                {
                    // 生成唯一的 EventId (如果 LLM 没生成或者我们希望自己控制)
                    if (string.IsNullOrEmpty(evt.EventId))
                    {
                        evt.EventId = Guid.NewGuid().ToString();
                    }

                    if(evt.EventType == "None")
                    {
                        return null;
                    }

                    // 补全时间戳（LLM 无法知道当前游戏的精确 float 时间，需要在 C# 层赋值）
                    // 注意：这里需要你传入当前游戏时间，或者在外部赋值。
                    evt.TimeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); 
                    evt.Location = $"{VisualCommands.GetCurrentSettlementId()} {VisualCommands.GetCurrentLocationId()}";

                    string playerName = Hero.MainHero.Name.ToString();
                    string playerStringId = Hero.MainHero.StringId;
                    //临时转换下
                    if (evt.VictimName == _profile.Name)                    
                        evt.VictimId = _profile.StringId;
                    if(evt.InitiatorName == _profile.Name)
                        evt.InitiatorId = _profile.StringId;
                    if(evt.InitiatorName == playerName)
                        evt.InitiatorId = playerStringId;
                    if(evt.VictimName == playerName)
                        evt.VictimId = playerStringId;
                    

                    // 确保列表不为 null
                    if (evt.WitnessId == null) evt.WitnessId = new List<string>();
                    if (evt.Tags == null) evt.Tags = new List<string>();

                    // 简单校验：如果没有发起者或受害者，视为无效事件
                    if (string.IsNullOrEmpty(evt.InitiatorId) || string.IsNullOrEmpty(evt.VictimId))
                    {
                        return null;
                    }
                }

                return evt;
            }
            catch 
            {
                // 记录日志，方便调试 Prompt 效果
                // Debug.WriteLine($"Failed to parse SocialEvent: {ex.Message}. Response was: {jsonResponse}");
                return null;
            }
        }

        private async Task MaintainMemoryAsync()
        {
           

            List<ChatMessage> messagesToSummarize = null;
            double timeStamp_Start = 0;
            double timeStamp_End = 0;
            lock (_lock)
            {
                // 建议：不要直接删除，而是先复制出来
                if (RecentHistory.Count > MaxRecentHistoryCount * 2)
                {
                    messagesToSummarize = RecentHistory.Take(RecentHistory.Count - MaxRecentHistoryCount).ToList();
                }
            }



            


            if (messagesToSummarize != null)
            {
                if (_isSummarizing) return; // 如果正在总结，直接跳过本次触发
                _isSummarizing = true;

                //获取即将移除的对话历史的时间戳
                foreach (ChatMessage message in messagesToSummarize)
                {
                    //取messagesToSummarize中最早的作为timeStamp_Start，最后的作为timeStamp_End
                    if (message.TimeStamp < timeStamp_Start || timeStamp_Start == 0)
                    {
                        timeStamp_Start = message.TimeStamp;
                    }
                    if (message.TimeStamp > timeStamp_End)
                    {
                        timeStamp_End = message.TimeStamp;
                    }
                }


                // 🔴 2026-08-12（用户裁定）：channel_nearby 行（玩家附近喊话亲历）是瞬时低价值信息——
                // 不参与总结（从总结 prompt 剔除），随滚动自然淘汰——只在对话历史短暂保留，不进长期记忆。
                var toSummarize = messagesToSummarize.Where(m => m == null || m.Role != "channel_nearby").ToList();
                if (toSummarize.Count == 0)
                {
                    // 待归档全是瞬时亲历行：无总结价值，直接移除（不调 LLM）
                    lock (_lock)
                    {
                        if (RecentHistory.Count >= messagesToSummarize.Count)
                            RecentHistory.RemoveRange(0, messagesToSummarize.Count);
                    }
                    DebugLogger.Log($"记忆维护：仅瞬时亲历行（channel_nearby），直接淘汰 {messagesToSummarize.Count} 条，不总结。");
                    _isSummarizing = false;
                    return;
                }
                string summaryPrompt = PromptBuilder.BuildPromptForSummary(this, toSummarize);
                // 获取 JSON 字符串（静默参数：随从对话触发的总结失败不弹玩家红字，D4）
                string jsonResponse = await LLMService.Instance.SummarizeAsync(summaryPrompt, showFailureAlert: !SuppressFailureAlerts);
                string summaryContent;
                jsonResponse = LLMService.CleanJson(jsonResponse);
                LLSSummaryResponse response = null;
                try
                {
                    response = JsonConvert.DeserializeObject<LLSSummaryResponse>(jsonResponse);
                    // 双重检查：如果反序列化成功但对象为空（极为罕见）
                    if (response == null) throw new Exception("Empty JSON");
                }
                catch (Exception) // 捕获 JsonReaderException 或其他解析错误
                {
                    // 🔴 2026-08-10 记忆污染修复：解析失败 → 该轮总结作废，**不存记忆、不移除历史**。
                    // 旧实现把截断的 JSON 原文当总结存进动态记忆（日志实锤：
                    // 动态记忆里出现 {"Summary":"那人自称拉盖娅女皇的老公，又问 这种垃圾行，污染后续 prompt）。
                    DebugLogger.Log($"[警告] 记忆总结 JSON 解析失败，作废本轮总结（历史保留，下次触发重试）：{jsonResponse}");
                    return;
                }

                try
                {
                    summaryContent = response.Summary;
                    if (!string.IsNullOrWhiteSpace(summaryContent))
                    {
                        int countToRemove = messagesToSummarize.Count;
                        // 成功获取总结后，再安全地移除历史记录
                        lock (_lock)
                        {
                            //再次检查以防止索引越界（虽然在lock保护下通常没事）
                            if (RecentHistory.Count >= countToRemove)
                            {

                                DebugLogger.Log($"总结完成，移除已归档的 {countToRemove} 条历史记录。");
                                
                                

                                RecentHistory.RemoveRange(0, countToRemove);
                            }                           

                        }
                        RecentMemory newMemory = new RecentMemory(summaryContent, timeStamp_Start, timeStamp_End);
                        await AddDynamicMemory(newMemory); // 存入纯文本
                    }
                }
                catch (Exception ex)
                {

                    DebugLogger.Log("记忆解析失败：" + ex.Message);
                }
                finally
                {
                    _isSummarizing = false; // 重置标记
                }

              
            }


        }

        private async Task AddDynamicMemory(RecentMemory newMemory)
        {

            RecentMemory fadingMemory = null;

            // 1. 内存操作阶段（加锁，极快）
            lock (_lock)
            {
                DynamicMemories.AddLast(newMemory);
                DebugLogger.Log($"NPC[{_profile.Name}] 新增动态记忆: {newMemory.Content}");

                if (DynamicMemories.Count > MaxDynamicMemoryCount)
                {
                    // 获取即将被移除的记忆
                    fadingMemory = DynamicMemories.First.Value;

                    // 【关键修改】：
                    // 必须在锁内直接移除它，以保持 List 的大小对其他线程是准确的。
                    // 否则如果有并发请求，其他线程可能会看到 List 还是满的。
                    DynamicMemories.RemoveFirst();
                }
            }

            // 2. 耗时处理阶段（无锁，异步）
            // 此时已经持有了 fadingMemory 的副本，且它已经从列表中移除了，
            // 所以这里可以安全地慢慢处理，不影响主线程或其他 NPC 逻辑。
            if (fadingMemory != null)
            {
                DebugLogger.Log($"NPC[{_profile.Name}] 动态记忆满，准备遗忘记忆: {fadingMemory.Content}");

                // 这里可以安全地使用 await
                await CheckAndPromoteToPermanent(fadingMemory);
            }


            
        }
    


        private async Task CheckAndPromoteToPermanent(RecentMemory memory)
        {
            // 这里注意：PermanentMemory 也需要保护，或者仅在这里修改
            string oldMemStr;
            lock (_lock) { oldMemStr = PermanentMemory.ToString(); }
            DebugLogger.Log($"NPC[{_profile.Name}] 永续记忆即将发生变化，变化前: {oldMemStr}");
            string systemPrompt = PromptBuilder.BuildPromptForPermanentMemory(this,memory.Content, oldMemStr);
            string updatedPermMemory;
                
                
            string jsonResponse = await LLMService.Instance.MergeMemoryAsync(systemPrompt, showFailureAlert: !SuppressFailureAlerts);
            jsonResponse = LLMService.CleanJson(jsonResponse);
            LLSSummaryResponse response = null;
            try
            {
                response = JsonConvert.DeserializeObject<LLSSummaryResponse>(jsonResponse);
                // 双重检查：如果反序列化成功但对象为空（极为罕见）
                if (response == null) throw new Exception("Empty JSON");
            }
            catch (Exception) // 捕获 JsonReaderException 或其他解析错误
            {
                DebugLogger.Log($"[警告] 大模型未返回标准 JSON，直接使用大模型生成内容作为Reply，其余使用默认值：{jsonResponse}");

                response = new LLSSummaryResponse
                {
                    Summary = jsonResponse
                };
            }

            try
            {
                updatedPermMemory = response.Summary;
                lock (_lock)
                {
                    PermanentMemory.Clear();
                    PermanentMemory.Append(updatedPermMemory);
                    DebugLogger.Log($"NPC[{_profile.Name}] 永续记忆发生了变化，更新为: {PermanentMemory.ToString()}");

                    if (Settings.Instance.ShowDebugMessages)
                        InformationManager.DisplayMessage(new InformationMessage($"NPC[{_profile.Name}] 永续记忆发生了变化，更新为: {PermanentMemory.ToString()}"));
                }
            }
            catch
            {
                

            }


            
        }
 
       
      
      
    }
}
