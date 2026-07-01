using LivingWorldNpcs.Story;
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

namespace LivingWorldNpcs
{
    public class SingNpcMemorySystem
    {
        // NPC 个人资料
        public readonly NPCProfile _profile;
        // 1. 近期对话历史 (保留最近10轮)        
        public List<ChatMessage> RecentHistory { get; private set; } = new List<ChatMessage>();
        //实际上，这里的历史记录可以更多一些,比如翻倍，等到MaxRecentHistoryCount*2条时触发记忆维护
        private const int MaxRecentHistoryCount = 10;

        // 2. 近期记忆，由近期对话历史总结而来，先进先出，最多5个，每条30字以内
        public LinkedList<RecentMemory> DynamicMemories { get; private set; } = new LinkedList<RecentMemory>();
        private const int MaxDynamicMemoryCount = 5;

        // 3. 远期记忆 (Max 300字)
        public StringBuilder PermanentMemory { get; private set; } = new StringBuilder();
        private const int MaxPermanentLength = 300;
        private volatile bool _isSummarizing = false; // 新增标记

        //开场白
        public NpcInitiative CurrentInitiative  = null;

        // [新增] 待处理的冲突/说服需求
        public PendingConflict ActiveConflict { get; set; } = null;
        //谈判状态，之后会代替说服任务
        public NegotiationState CurrentNegotiationState;

        // 4. 全局新闻 (外部注入)
        public string GlobalNews { get; set; } = "";
       

        private readonly object _lock = new object();

        //事件传闻
        public List<NewsSpreadSystem.KnownEvent> KnownEvents { get; set; } = new List<NewsSpreadSystem.KnownEvent>();

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


        public void AddHistory(string Role,string content)
        {
            lock (_lock)
            {
                RecentHistory.Add(new ChatMessage (Role,content));
            }

            _ = MaintainMemoryAsync();

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
            return _profile.GetPersonaPrompt();
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


                string summaryPrompt = PromptBuilder.BuildPromptForSummary(this,messagesToSummarize);
                // 获取 JSON 字符串
                string jsonResponse = await LLMService.Instance.SummarizeAsync(summaryPrompt);
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
                    DebugLogger.Log($"[警告] 大模型未返回标准 JSON，直接使用大模型生成内容作为Reply，其余使用默认值：{jsonResponse}");

                    response = new LLSSummaryResponse
                    {
                        Summary = jsonResponse
                    };
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
                
                
            string jsonResponse = await LLMService.Instance.MergeMemoryAsync(systemPrompt);
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

                    InformationManager.DisplayMessage(new InformationMessage($"NPC[{_profile.Name}] 永续记忆发生了变化，更新为: {PermanentMemory.ToString()}"));
                }
            }
            catch
            {
                

            }


            
        }
 
       
      
      
    }
}
