using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ExampleMod.AI
{
    public class AgentAIController : MissionLogic
    {
        public static AgentAIController Instance { get; private set; }

        //private Dictionary<Agent, AgentBrain> _brains = new Dictionary<Agent, AgentBrain>();
        // 1. 修改字典定义：Key 从 Agent 改为 int (Agent.Index)
        private Dictionary<int, AgentBrain> _brains = new Dictionary<int, AgentBrain>();
        private static bool IsDebugMode = false;
        public static AgentBrain GetBrainForAgent(Agent agent)
        {
            if (Instance != null && Instance._brains.TryGetValue(agent.Index, out var brain))
            {
                return brain;
            }
            return null;
        }

        public AgentAIController()
        {
            Instance = this;


        }
        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            Instance = this;
        }
        public override void AfterStart()
        {
            base.AfterStart();

            /*
            // 这里好像会导致重复添加agent
            foreach (var agent in Mission.Agents)
            {
                // 直接复用你写好的 OnAgentCreated 逻辑
                // 因为你的 OnAgentCreated 里已经有了 IsHuman 判断和 _brains.ContainsKey 检查
                // 所以这里调用是安全的，不会导致重复添加
                OnAgentCreated(agent);
            }
            */
        }

        public override void OnAgentCreated(Agent agent)
        {
            if (agent.IsHuman)
            {
                // 3. 修改注册逻辑：判断 Key 是否存在
                if (!_brains.ContainsKey(agent.Index))
                {
                    if (IsDebugMode)
                        DebugLogger.Log($"[新增] Name: {agent.Name} (Index: {agent.Index})");

                    _brains.Add(agent.Index, new AgentBrain(agent));
                }
                else
                {
                    if (IsDebugMode)
                        DebugLogger.Log($"[重复拦截] Name: {agent.Name} (Index: {agent.Index}) 已存在。跳过添加。当前总数: {_brains.Count}");
                    return;
                }
            }
            
        }

        public override void OnAgentDeleted(Agent agent)
        {
            if (_brains.ContainsKey(agent.Index))
            {
                if (IsDebugMode)
                    DebugLogger.Log($"因为删除 移除一个Agent的大脑 name {agent.Name} index{agent.Index} 当前总数{_brains.Count}");
                _brains.Remove(agent.Index);
            }
        }
        public override void OnMissionTick(float dt)
        {
            foreach (var brain in _brains.Values)
            {
                if (brain.Owner.IsActive())
                {
                    brain.Tick(dt);
                }
            }
            
        }

        // --- 外部调用接口 ---

        
        // 2. 发送事件给特定 Agent
        public void SendEventToAgent(Agent target, string eventType, params object[] args)
        {
            if(IsDebugMode)
                DebugLogger.Log($"尝试发送事件 '{eventType}' 给 {target.Name} (Index: {target.Index},当前brains总数{_brains.Count})");
            var brain = GetBrainForAgent(target);
            if (brain!=null)
            {
                InformationManager.DisplayMessage(new InformationMessage($"[事件发送] 发送事件 '{eventType}' 给 {target.Name}"));
                if (IsDebugMode)
                    DebugLogger.Log($"[事件发送] 发送事件 '{eventType}' 给 {target.Name}");
                var evt = new AIEvent { EventType = eventType, Sender = null, Args = args };
                brain.ReceiveEvent(evt);
            }
            else
            {
                InformationManager.DisplayMessage(new InformationMessage($"[警告] 试图发送事件 '{eventType}' 给 {target.Name}，但未找到对应的大脑。"));
                if(IsDebugMode)
                    DebugLogger.Log($"[警告] 试图发送事件 '{eventType}' 给 {target.Name}，但未找到对应的大脑。");
            }
        }

        // 3. 范围广播（最常用）
        public void BroadcastEventInRange(Vec3 center, float radius, string eventType, params object[] args)
        {
            // 1. 找出范围内所有的大脑
            List<AgentBrain> brainsInRange = new List<AgentBrain>();
            List<Agent> witnesses = new List<Agent>();
            if (IsDebugMode)
                DebugLogger.Log($"当前brains总数为{_brains.Count}");
            foreach (var brain in _brains.Values)
            {
                if (brain.Owner.IsActive() && brain.Owner.Position.Distance(center) <= radius && brain.Owner != Agent.Main)
                {
                    brainsInRange.Add(brain);
                    witnesses.Add(brain.Owner);
                    if (IsDebugMode)
                        DebugLogger.Log($"witnesses.Name: {brain.Owner.Name}  index {brain.Owner.Index}");
                }
                else
                {
                    if (IsDebugMode)
                        DebugLogger.Log($"{brain.Owner.Name}条件不满足无法成为目击者 ");
                }
            }
           // InformationManager.DisplayMessage(new InformationMessage($"{eventType} brainsInRange总数为: {brainsInRange.Count}"));

            // 2. 特殊处理：如果是围观事件 (WitnessCrime)，先进行统一舞台分配
            // 假设 args[0] 是被围观的目标 (Agent)
            // 假设 args[1] 是关键人物 (Agent)，如受害者，没有则为null
            if (eventType == "WitnessCrime" && args.Length > 0 && args[0] is Agent criminal)
            {
                // 确保犯人自己不参与围观分配
                if (witnesses.Contains(criminal)) {
                    
                    witnesses.Remove(criminal);                
                
                }
                try
                {
                    Agent judge = null;
                    if (args.Length > 1 && args[1] is Agent victim)
                    {
                        judge = victim;
                    }
                    if (IsDebugMode)
                        DebugLogger.Log($"选取的主审为{judge?.Name ?? "null"}");
                    // === 调用 GroupStageManager 进行计算 ===
                    // 这一步会填充 Manager 内部的静态字典，计算好每个人的坐标
                    GroupStageManager.PrecalculateAllocations(criminal, judge, witnesses);
                    if (IsDebugMode)
                        DebugLogger.Log($"GroupStageManager.PrecalculateAllocations done");
                    // 3. 分发事件
                    foreach (var brain in brainsInRange)
                    {
                        var agent = brain.Owner;
                        try
                        {
                            if (brain.Owner == criminal)
                            {
                                if (IsDebugMode)
                                    DebugLogger.Log($"{brain.Owner.Name}是犯人，需要跳过");
                                continue; // 跳过主角
                            }
                            // 每个人去查自己的位置
                            var assignedSpot = GroupStageManager.GetAssignedSpot(criminal, brain.Owner);

                            if (assignedSpot != null)
                            {
                                if (IsDebugMode)
                                    DebugLogger.Log($"Agent {brain.Owner.Name} 分配到位置: {assignedSpot.Position}");
                                // 发送带有具体坐标参数的事件
                                // 我们构造一个新的参数列表，把计算好的坐标传进去
                                // 约定：Args[0]=Target, Args[1]=Pos(Vec3), Args[2]=LookDir(Vec2)
                                brain.ReceiveEvent(new AIEvent
                                {
                                    EventType = "WitnessCrime_GatherOnLook",
                                    Sender = criminal,
                                    Args = new object[] { criminal,judge, assignedSpot.Position, assignedSpot.LookDirection }
                                });
                            }
                            else
                            {
                                if (IsDebugMode)
                                    DebugLogger.Log($"Agent {brain.Owner.Name} 没有分配到位置");
                                // 挤不进去的人，可能收到一个普通的事件，或者干脆不发让他原地呆着
                                // 这里选择发送普通事件，让他在原地看
                                brain.ReceiveEvent(new AIEvent
                                {
                                    EventType = "WitnessCrime_StayStare",
                                    Sender = criminal,
                                    Args = args // 保持原样
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            if (IsDebugMode)
                                DebugLogger.Log($"[严重错误] 处理 Agent {agent.Name} 时发生异常: {ex.Message}\n堆栈: {ex.StackTrace}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (IsDebugMode)
                        DebugLogger.Log($"[严重错误] 发生异常: {ex.Message}\n堆栈: {ex.StackTrace}");
                }
            }
            else
            {
                // 普通事件，直接广播，不做位置分配
                if (IsDebugMode)
                    DebugLogger.Log($"普通事件{eventType}直接广播");
                foreach (var brain in brainsInRange)
                {
                    brain.ReceiveEvent(new AIEvent
                    {
                        EventType = eventType,
                        Sender = null,
                        Args = args
                    });
                }
            }









        }
    }
}
