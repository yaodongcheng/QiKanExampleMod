using ExampleMod.StoryEngineBag;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using static TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultPerks;

namespace ExampleMod
{
    internal class SocialEventManager
    {
    }

    //事件类型
    public enum SocialEventType
    {
        None, //无
        Divorce, //离婚
        Marriage, //结婚
        Exile, //放逐
        Execution, //处决
        Betrayal, //背叛
        Harassment, //骚扰
        Kill, //杀人
        Insult,//侮辱
    }

    // 一条事件记录
    public class PendingGameAction
    {
        public SocialEventType Type;
        public Hero Initiator;
        public Hero Target;
        public string Context; // 备注，比如 "和平分手" 或 "强制休妻"

        public PendingGameAction(SocialEventType type, Hero initiator, Hero target, string context = "")
        {
            Type = type;
            Initiator = initiator;
            Target = target;
            Context = context;

        }
            
    }
    public class SocialEvent
    {
        public string EventId { get; set; }
        public SocialEventType EventTypeEnum { get; set; } // "Harassment", "Betrayal", "Gift"

        [JsonProperty("event_type")]
        public string EventType { 
            get { return EventTypeEnum.ToString(); } 
            set
            {
                // 兜底核心逻辑：尝试解析
                // 参数 true 表示忽略大小写 (e.g., "none" == "None")
                if (Enum.TryParse<SocialEventType>(value, true, out var result))
                {
                    EventTypeEnum = result;
                }
                else
                {
                    EventTypeEnum = SocialEventType.None;
                }
            }
        }

       

        public string InitiatorId { get; set; }   // 肇事者 (木下藤吉郎)
        public string InitiatorName { get; set; }   // 肇事者 (木下藤吉郎)
        public string VictimId { get; set; }      // 受害者 (阿市)
        public string VictimName { get; set; }      // 受害者 (阿市)
        public int BaseSeverity { get; set; }     // 基础严重程度 0-100 (本次骚扰定义为 80)，但是每个收听者都会有自己的在意程度判断

        public List<string> WitnessId { get; set; } = new List<string>(); //目击者列表
        public string Location { get; set; } //  事件发生的位置

        public string OccurTime { get; set; } // 事件发生的时间
        public string Description { get; set; } // "木下藤吉郎在走廊对阿市动手动脚"

        // 这里的 Tag 用于 AI 判断是否触犯价值观
        public List<string> Tags { get; set; } = new List<string> { "Dishonorable", "SexualHarassment", "Insulting" };

        public float TimeStamp { get; set; }

        // 【新增】关键证据字段
        public string KeyQuoteText { get; set; }    // 证言 "奉信长公之命，你必须嫁给我！"
        public string KeyQuoteSpeakerName { get; set; } // 证人

    }

    //事件记录员
    public static class ActionTransactionSystem
    {
        private static List<PendingGameAction> _buffer = new List<PendingGameAction>();
        private static bool _isRecording = false;

        public static void BeginTransaction()
        {
            _buffer.Clear();
            _isRecording = true;
        }

        public static void RecordAction(SocialEventType type, Hero initiator, Hero target, string context = "")
        {
            var action = new PendingGameAction(type,initiator,target,context);            
            if (!_isRecording)
            {
                // 如果没开事务，直接单条转化
                ProcessAndBroadcast(new List<PendingGameAction> { action });
            }
            else
            {
                _buffer.Add(action);
            }
        }
        public static void CommitTransaction()
        {
            _isRecording = false;
            if (_buffer.Count == 0) return;

            // 这里是核心：将缓冲区的动作列表，转化为你的 SocialEvent
            ProcessAndBroadcast(_buffer);

            _buffer.Clear();
        }
        private static void ProcessAndBroadcast(List<PendingGameAction> actions)
        {
            // 1. 模式匹配：检测是否是“为了新欢抛弃旧爱”
            var divorceAct = actions.FirstOrDefault(a => a.Type == SocialEventType.Divorce);
            var marryAct = actions.FirstOrDefault(a => a.Type == SocialEventType.Marriage);

            SocialEvent finalEvent = null;

            if (divorceAct != null && marryAct != null && divorceAct.Initiator == marryAct.Initiator)
            {
                // ==> 命中复杂逻辑：负心汉事件
                finalEvent = CreateBetrayalEvent(divorceAct, marryAct);
            }
            else if (marryAct != null)
            {
                // ==> 普通结婚事件
                finalEvent = CreateMarriageEvent(marryAct);
            }
            else if (divorceAct != null)
            {
                // ==> 普通离婚事件
                finalEvent = CreateDivorceEvent(divorceAct);
            }

            // 2. 发送给你的传播系统
            if (finalEvent != null)
            {
                NewsSpreadSystem.Instance.BroadcastEvent(finalEvent);
            }
        }
        private static SocialEvent CreateBetrayalEvent(PendingGameAction divorce, PendingGameAction marriage)
        {
            // 这里我们填入你定义的字段
            var evt = new SocialEvent
            {
                EventId = System.Guid.NewGuid().ToString(),
                EventTypeEnum = SocialEventType.Betrayal, // 你的类型定义
                InitiatorId = divorce.Initiator.StringId,
                InitiatorName = divorce.Initiator.Name.ToString(),
                VictimId = divorce.Target.StringId, // 受害者是前妻/前夫
                VictimName = divorce.Target.Name.ToString(),
                BaseSeverity = 90, // 这种事非常严重
                OccurTime = CampaignTime.Now.ToString(),
                Location = divorce.Initiator.CurrentSettlement?.Name?.ToString() ?? "未知",
                Description = $"{divorce.Initiator.Name} 狠心地休掉了 {divorce.Target.Name}，转头就迎娶了 {marriage.Target.Name}！",
                Tags = new List<string> { "Dishonorable", "Scandal", "Romance" },
                TimeStamp = (float)CampaignTime.Now.ToHours,

                // 关键证据：这里可以由 LLM 生成，或者预设模板
                KeyQuoteSpeakerName = divorce.Initiator.Name.ToString(),
                KeyQuoteText = $"我不爱你了，{marriage.Target.Name} 才是我的真爱！"
            };

            // 自动填充目击者 (假设只有当事人在场，或者你可以通过 Settlement 获取)
            evt.WitnessId.Add(marriage.Target.StringId); // 第三者必然在场
            return evt;
        }
        private static SocialEvent CreateMarriageEvent(PendingGameAction act)
        {
            return new SocialEvent
            {
                EventId = System.Guid.NewGuid().ToString(),
                EventTypeEnum = SocialEventType.Marriage,
                InitiatorId = act.Initiator.StringId,
                InitiatorName = act.Initiator.Name.ToString(),
                VictimId = act.Target.StringId, // 结婚里没有受害者，但为了兼容字段，填配偶
                VictimName = act.Target.Name.ToString(),
                BaseSeverity = 20, // 喜事，严重程度低（或者是正向的关注度）
                Description = $"{act.Initiator.Name} 与 {act.Target.Name} 喜结连理。",
                Tags = new List<string> { "Ceremony", "Happy" },
                // ... 其他字段
            };
        }
        private static SocialEvent CreateDivorceEvent(PendingGameAction act)
        {
            // 类似上面的逻辑...
            return null;
        }


    }


    public class SpreadReport
    {
        public string EventId { get; set; }
        // 记录谁收到了消息，以及当时的严重程度
        public List<string> Logs { get; set; } = new List<string>();
        // 记录那些严重程度超过阈值的“重点关注对象”
        public List<Hero> HighImpactHeroes { get; set; } = new List<Hero>();
    }

    public class ScreenPlayOutline
    {// 场景角色分配
        public Hero Accused { get; set; }     // 被告 (对应 Initiator)
        public Hero Accuser { get; set; }     // 原告 (对应 Victim 或 其代理人)
        public Hero Authority { get; set; }   // 仲裁者 (领主/大名)
        public List<Hero> Gallery { get; set; } = new List<Hero>(); // 吃瓜群众

        // 原始数据引用
        public SocialEvent SourceEvent { get; private set; }

        public ScreenPlayOutline(SocialEvent evt, SpreadReport report)
        {
            SourceEvent = evt;

            Hero initiator = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == evt.InitiatorId);
            Hero victim = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == evt.VictimId);
            if (initiator == null || victim == null)
                return; // 构造失败

            //Accuser可以是关心程度最高的人
            var others = report.HighImpactHeroes
                 .OrderByDescending(h => AllNpcMemoryManager.GetMemory(h.StringId).KnownEvents.FirstOrDefault(e => e.EventId == evt.EventId).PerceivedSeverity);
            //others中PerceivedSeverity最高的人作为Accuser
            try
            {
                var potentialAccusers = report.HighImpactHeroes
            .Where(h => h.StringId != initiator.StringId) // 排除被告
            .Select(h => new
            {
                Hero = h,
                // 安全获取记忆，防止空引用
                EventMemory = AllNpcMemoryManager.GetMemory(h.StringId)?.KnownEvents.FirstOrDefault(e => e.EventId == evt.EventId)
            })
            .Where(x => x.EventMemory != null) // 确保这个人确实记得这件事
            .OrderByDescending(x => x.EventMemory.PerceivedSeverity) // 按严重程度降序
            .Select(x => x.Hero)
            .ToList();

                // 取严重程度最高的人，如果没有其他人关注，则默认受害者为原告
                this.Accuser = potentialAccusers.FirstOrDefault() ?? victim;
            }
            catch
            {
                this.Accuser = victim;
            }

            this.Accused = initiator;
            // --- 确定【仲裁者 (Authority)】 ---
            //肇事者的领袖作为裁决者
            Hero inviLeader = NewsSpreadSystem.GetLeader(initiator);
            Hero victimLeader = NewsSpreadSystem.GetLeader(victim);
            //优先向肇事者的领导告状
            if (inviLeader != null)
            {
                this.Authority = inviLeader;
            }
            //如果没有，向受害者的领导告状
            else if (victimLeader != null)
            {
                this.Authority = victimLeader;
            }
            else
            {   //没有人主持公道 
                this.Authority = null;
            }

            // 5. 确定【吃瓜群众 (Gallery)】
            // 记录已经有身份的人，避免角色重叠
            var keyRoleIds = new HashSet<string>();
            if (this.Accused != null) keyRoleIds.Add(this.Accused.StringId);
            if (this.Accuser != null) keyRoleIds.Add(this.Accuser.StringId);
            if (this.Authority != null) keyRoleIds.Add(this.Authority.StringId);

            // 从 potentialAccusers (已经按严重程度排序了) 或者 原始 report 中筛选
            // 剔除已分配角色的英雄
            var galleryCandidates = report.HighImpactHeroes
                .Where(h => !keyRoleIds.Contains(h.StringId)) // 关键：不在关键角色列表中
                .OrderByDescending(h =>
                {
                    var mem = AllNpcMemoryManager.GetMemory(h.StringId)?.KnownEvents.FirstOrDefault(e => e.EventId == evt.EventId);
                    return mem?.PerceivedSeverity ?? 0; // 同样按吃瓜兴趣（严重程度）排序
                });

            // 取前3个
            this.Gallery = galleryCandidates.Take(3).ToList();
        }

    }


    //新闻传播系统
    public class NewsSpreadSystem
    {
        // 作用：保证通过 ID 能查到唯一的事件本体
        private Dictionary<string, SocialEvent> _globalEventRegistry = new Dictionary<string, SocialEvent>();
        // 记录谁知道了什么事件，以及该事件在他心中的严重程度（可能会被添油加醋）
        //private Dictionary<string, List<KnownEvent>> _heroKnowledgeBase = new Dictionary<string, List<KnownEvent>>();
        public static NewsSpreadSystem Instance { get; } = new NewsSpreadSystem();
        public class KnownEvent
        {
            public string EventId { get; set; } // 只存 ID，不存整个对象
            public float PerceivedSeverity { get; set; } // 主观严重程度
            public int DecayCounter { get; set; } // 它是第几手消息

            // 方便获取原始事件的辅助属性
            public SocialEvent GetOriginalEvent()
            {
                return NewsSpreadSystem.Instance.GetEventById(EventId);
            }
        }
        public SocialEvent GetEventById(string eventId)
        {
            if (_globalEventRegistry.TryGetValue(eventId, out var evt))
            {
                return evt;
            }
            return null;
        }
        // 核心方法：触发一个新事件的传播
        public void BroadcastEvent(SocialEvent evt)
        {
            if (evt == null || string.IsNullOrEmpty(evt.EventId)) return;

            // 1. 先注册到全局库 (如果已存在则忽略或更新)
            if (!_globalEventRegistry.ContainsKey(evt.EventId))
            {
                _globalEventRegistry[evt.EventId] = evt;
            }
            // 2. 创建统计报告对象
            SpreadReport report = new SpreadReport { EventId = evt.EventId };
            // 用于防止循环传播的 Set (防止 A 传给 B，B 又传回给 A)
            HashSet<string> processedHeroes = new HashSet<string>();
            // 3. 初始传播源 (当事人 + 目击者 + 肇事者领导)
            List<(Hero hero, float severity)> initialTargets = new List<(Hero, float)>();


            // A. 首先，当事人必然知道
            Hero victim = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == evt.VictimId);

            if (victim != null)
            {
                initialTargets.Add((victim, evt.BaseSeverity));
            }

            //其次，目击者也会知道。但是没有当事人那么在乎(严重程度减半，因为不是发生在自己身上)
            foreach (var witnessId in evt.WitnessId)
            {
                Hero witness = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == witnessId);
                if (witness != null)
                {
                    initialTargets.Add((witness, evt.BaseSeverity * 0.5f));
                }
            }
            // 3. (可选) 肇事者自己也知道自己做了啥，但他不会到处去说（除非是炫耀），这里暂不处理

            //肇事者的领导也会知道，但是不会传播
            Hero invi = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == evt.InitiatorId);
            if (invi != null)
            {
                Hero clanLeader = GetClanLeader(invi);
                Hero kingdomLeader = GetKingdomLeader(invi);
                if (clanLeader != null && clanLeader != invi)
                {
                    initialTargets.Add((clanLeader, evt.BaseSeverity * 0.5f));
                }
                else if (kingdomLeader != null && kingdomLeader != invi)
                {
                    initialTargets.Add((kingdomLeader, evt.BaseSeverity * 0.5f));
                }
            }
            //基于上面要传播的初始传播对象，开始递归传播
            foreach (var target in initialTargets)
            {
                if (!report.HighImpactHeroes.Contains(target.hero) && report.HighImpactHeroes.Count < 5)
                {
                    report.HighImpactHeroes.Add(target.hero);
                }
                ProcessHeroRecursively(target.hero, evt, target.severity, 0, report, processedHeroes);
            }
            //统计事件总共传播到了哪些人，以及处理每个人之后和玩家话题（说服任务）
            PrintBroadcastSummary(report, evt);

            AIStoryGeneratorBehavior.Instance.StartGeneration(evt, report);
            // _ = GenerateScreenPlayDirector(evt, report);


        }

        public static Hero GetLeader(Hero hero)
        {
            //如果自己是族长，就获取kingdom的leader
            Hero clanLeader = GetClanLeader(hero);
            if (clanLeader != null)
            {
                if (clanLeader != hero)
                    return clanLeader;
            }
            Hero kingdomLeader = GetKingdomLeader(hero);
            if (kingdomLeader != null)
            {
                if (kingdomLeader != hero)
                    return kingdomLeader;
            }
            return null;
        }

        public static Hero GetClanLeader(Hero hero)
        {
            //如果自己是族长，就获取kingdom的leader
            if (hero.Clan != null && hero.Clan.Leader.IsAlive)
            {
                return hero.Clan.Leader;
            }
            return null;
        }
        public static Hero GetKingdomLeader(Hero hero)
        {
            //获取kingdom的leader
            if (hero.Clan != null && hero.Clan.Kingdom != null && hero.Clan.Kingdom.Leader.IsAlive)
            {
                return hero.Clan.Kingdom.Leader;
            }
            return null;
        }


        private void ProcessHeroRecursively(Hero target, SocialEvent evt, float severity, int decayCount, SpreadReport report, HashSet<string> processedHeroes)
        {
            if (target == null || !target.IsAlive) return;
            if (processedHeroes.Contains(target.StringId)) return; // 防止重复处理

            if (severity < 40)
                return;
            if (decayCount > 2)
                return;

            processedHeroes.Add(target.StringId); // 标记已处理

            // 获取 NPC 传闻系统
            var memory = AllNpcMemoryManager.GetMemory(target.StringId);
            if (memory == null) return;

            // 核心逻辑：写入 NPC 传闻
            bool isInteresting = memory.ReceiveNews(evt.EventId, severity, decayCount);

            // 记录统计数据
            report.Logs.Add($"[{target.Name}] 收到消息 (关注度:{severity:F1}, 层级:{decayCount}) - {(isInteresting ? "感兴趣" : "忽略")}");

            // 筛选重点关注人群 (比如关注度 > 50)
            //不能重复添加吧
            if (!report.HighImpactHeroes.Contains(target) && severity > 60)
            {
                report.HighImpactHeroes.Add(target);
            }


            // 如果感兴趣且未达到传播衰减极限，继续传播给他的关系网
            if (isInteresting && decayCount < 2)
            {
                // 获取关系网
                var network = memory._profile.GetCloseRelations(target, out string relationStr);
                float transferRate = 0.5f; // 传播衰减系数

                foreach (var relation in network)
                {
                    // 跳过肇事者(他知道自己干了啥)和已处理过的人
                    if (relation.StringId == evt.InitiatorId || processedHeroes.Contains(relation.StringId)) continue;

                    // 特殊逻辑：受害者配偶暴怒加成
                    float currentRate = transferRate;
                    if (relation.Spouse != null && relation.Spouse.StringId == evt.VictimId) currentRate = 1.5f;

                    float newSeverity = severity * currentRate;

                    // 只有概率满足才传播 (模拟消息丢失)
                    if (MBRandom.RandomFloat * 100 < newSeverity)
                    {
                        ProcessHeroRecursively(relation, evt, newSeverity, decayCount + 1, report, processedHeroes);
                    }
                }
            }
        }

        private void PrintBroadcastSummary(SpreadReport report, SocialEvent evt)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{evt.Description}");
            //sb.AppendLine($"事件总传播人数: {report.Logs.Count}");

            //sb.AppendLine("--- 传播详情 ---");
            foreach (var log in report.Logs)
            {
                //sb.AppendLine(log);
            }
            if (report.HighImpactHeroes.Count > 0)
            {
                sb.AppendLine($"据传这些人颇为关注此事：");
                string names = "";
                foreach (var hero in report.HighImpactHeroes)
                {
                    // 这里可以预判一下
                    if (hero.StringId == Hero.MainHero.StringId) continue;
                    names += $"{hero.Name} ";
                }
                sb.AppendLine(names);
            }
            DebugLogger.Log(sb.ToString());

            InformationManager.ShowInquiry(new InquiryData("大人：最新坊间传闻", sb.ToString(), true, false, "已阅", null, null, null));
        }

        // 辅助方法：获取关系网 (代码略)




    }



}
