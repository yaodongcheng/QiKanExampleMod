using ExampleMod.StoryEngineBag;
using Newtonsoft.Json;
using SandBox.Conversation.MissionLogics;
using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static TaleWorlds.MountAndBlade.Agent;

namespace ExampleMod.AI.Actions
{
    // 接口保持不变
    public interface IAtomicAction
    {
        void OnStart(Agent agent);
        void OnTick(Agent agent, float dt);
        bool IsFinished(Agent agent);
        void OnEnd(Agent agent);
    }
    // 这个Action负责"点火"，即启动LLM生成任务
    public class PrepareOpeningAction : IAtomicAction
    {
        private Agent self;
        private SingNpcMemorySystem memory;
        InitiativeType Type;
        private string ContextDesc;

        private PendingConflict _conflictData; // 新增字段
        public PrepareOpeningAction(InitiativeType type,string desc)
        {
            Type = type;
            ContextDesc = desc;
        }
        // 【新增】构造函数 2：冲突逻辑（接收结构化数据）
        public PrepareOpeningAction(InitiativeType type, PendingConflict conflict)
        {
            Type = type; 
            _conflictData = conflict;
        }
        private async Task Thinking()
        {
            string openingPrompt = PromptBuilder.BuildOpeningPrompt(memory, self);
            try
            {
                string jsonResponse = await LLMService.Instance.ChatAsync(openingPrompt,300,true);
                jsonResponse = LLMService.CleanJson(jsonResponse); // 清洗可能存在的 markdown 符号
                memory.CurrentInitiative.JsonResponseOpening = jsonResponse;

            }
            catch (Exception ex)
            {
                //在这里构造默认，todo
                memory.CurrentInitiative.JsonResponseOpening =
                   "{ \"npc_reply\": \"(警惕地看着你) \", \"options\": [] }";
            }
            //反序列化
            try
            {
                var data = JsonConvert.DeserializeObject <LLMResponse_Opening> (memory.CurrentInitiative.JsonResponseOpening);
                memory.CurrentInitiative.CachedOpening = data;
            }
            catch
            {
                memory.CurrentInitiative.CachedOpening = null;
            }
        }
        public void OnTick(Agent agent,float dt)
        {

        }
        public void OnEnd(Agent agent)
        {

        }

        public void OnStart(Agent agent)
        {
            self = agent;
            memory = AllNpcMemoryManager.GetMemoryForAgent(self);
            if(_conflictData == null)
                memory.CurrentInitiative = new NpcInitiative(Type, ContextDesc);
            else
                memory.CurrentInitiative = new NpcInitiative(Type, _conflictData);
            _ = Task.Run(() => Thinking());
        }

        public bool IsFinished(Agent agent)
        {
            // 瞬间完成，绝不阻塞，立刻进入下一个 MoveToPositionAction
            return true;
        }
    }
    public class ForceTalkAction : IAtomicAction
    {
        private bool _isFinished = false;
        public bool IsFinished(Agent agent) => _isFinished;

        private float _timer = 0f;
        private SingNpcMemorySystem memory;
        public ForceTalkAction()
        {
            // 这个行为不需要目标参数，因为对话默认是和 MainAgent (玩家) 进行的
            // 或者是 Owner 主动发起的
        }

        public void OnStart(Agent agent)
        {

            memory = AllNpcMemoryManager.GetMemoryForAgent(agent);
            // 1. 让 NPC 停下来，防止一边滑步一边说话
            InformationManager.DisplayMessage(new InformationMessage($"{agent.Name} 正在向准备向你问话...", Colors.Yellow));
            // 2. 获取任务中的对话逻辑控制器
           
        }

        public void OnTick(Agent agent, float dt)
        {
            _timer+= dt;
            if(_timer> 0.5f)
            {
                _timer = 0f;
                //检查生成好了没
                if(memory.CurrentInitiative!= null && memory.CurrentInitiative.IsReady)
                {
                    _isFinished = true;
                }
            }
        }

        public void OnEnd(Agent agent)
        {
            if (InteractionMissionView.Instance != null && Agent.Main != null && InteractionMissionView.IsChatting == false)
            {
                // 3. 核心调用：StartConversation
                // 参数1: talkToAgent (NPC自己)
                // 参数2: setActions (是否设置默认的站立/对视动作，true)
                // 参数3: isCivilian (是否是平民模式，false通常指战场或对峙，true指城镇闲聊，这里选 true/false 看你具体需求，通常 false 更通用)
                _ = InteractionMissionView.Instance.StartFreeConversationFlow(agent, false);

                InformationManager.DisplayMessage(new InformationMessage($"{agent.Name} 开启质问你...", Colors.Yellow));
            }
        }
       
    }
    public class DrawWeaponAction : IAtomicAction
    {
        private bool _isFinished = false;
        public bool IsFinished(Agent agent) => _isFinished;

        private float _timer = 0f;
        public DrawWeaponAction()
        {
            // 这个行为不需要目标参数，因为对话默认是和 MainAgent (玩家) 进行的
            // 或者是 Owner 主动发起的
        }

        public void OnStart(Agent agent)
        {
            agent.TryToWieldWeaponInSlot(EquipmentIndex.WeaponItemBeginSlot, Agent.WeaponWieldActionType.WithAnimation, false);
            
        }

        public void OnTick(Agent agent, float dt)
        {
            // 不需要持续逻辑，触发即结束
            _timer+= dt;
            if(_timer > 2.0f)
                _isFinished=true;
        }

        public void OnEnd(Agent agent)
        {

        }

    }
    public class TurnToDirectionAction : IAtomicAction
    {
        private Vec2 _targetDir;
        private float _precision;
        private bool _isFinished;

        // 超时机制：防止因为物理碰撞导致死活转不过去，卡在Action里
        private float _timer;
        private const float TIMEOUT = 3.0f;

        /// <summary>
        /// 转向动作
        /// </summary>
        /// <param name="direction">目标朝向 (Vec2)</param>
        /// <param name="precision">精度阈值 (0.0~1.0)，越接近1越精准。默认0.98 (约11度误差)</param>
        public TurnToDirectionAction(Vec2 direction, float precision = 0.98f)
        {
            _targetDir = direction.Normalized(); // 务必归一化
            _precision = precision;
            _isFinished = false;
            _timer = 0f;
        }

        public void OnStart(Agent agent)
        {
            // 初始时强制重置一下状态
            _isFinished = false;
            _timer = 0f;

            // 可以在开始时直接设定一次，响应更快
            agent.SetMovementDirection(_targetDir);
            agent.SetLookAgent(null);
        }

        public void OnTick(Agent agent, float dt)
        {
            _timer += dt;

            // 1. 执行你要求的关键旋转函数
            // SetMovementDirection 控制身体朝向意图
            agent.SetMovementDirection(_targetDir);

            // 建议：同时设置 SetLookDirection，否则 Agent 可能会身体转了头没转，
            // 或者视线判定导致逻辑认为还没转到位。
            //agent.LookDirection = (_targetDir.ToVec3());

            // 2. 检测是否完成
            // 获取当前 Agent 的视线/身体朝向
            Vec2 currentDir = agent.LookDirection.AsVec2.Normalized();

            // 计算点积：1.0 = 完全同向, 0 = 垂直, -1.0 = 反向
            float dot = Vec2.DotProduct(currentDir, _targetDir);

            // 如果角度足够接近，或者时间超时，标记为完成
            if (dot >= _precision || _timer > TIMEOUT)
            {
                _isFinished = true;
            }
        }

        public bool IsFinished(Agent agent)
        {
            return _isFinished;
        }

        public void OnEnd(Agent agent)
        {
            // 转向结束后，通常只需重置输入，避免 Agent 继续尝试旋转
            // 这里的 Reset 会清除 MovementDirection 和 LookDirection 的强制输入
            AgentControlHelper.StopAndReset(agent);
        }
    }

    // 1. 移动到坐标动作
    public class MoveToPositionAction : IAtomicAction
    {
        private Vec3 _targetPos;
        private Vec2 _targetDir;
        private bool _run;
        private float _stopDistance;
        private float _timer;
        private float _maxTime;
        private float fixedTimer = 0;

        public MoveToPositionAction(Vec3 pos, Vec2 dir ,bool run = false, float stopDistance = 1.0f)
        {
            _targetPos = pos;
            _targetDir = dir;
            _run = run;
            _stopDistance = stopDistance;

            _timer = 0f;
            _maxTime = 8.0f; //可能用距离更合理一点

        }

        public void OnStart(Agent agent)
        {

            _= AgentControlHelper.MovePrepare(agent);
            // 调用 Helper，不再自己处理 Flags 和 NavMesh
           //AgentControlHelper.ScriptedMoveToPoint(agent, _targetPos, _run);
           
        }

        public void OnTick(Agent agent, float dt)
        {
            // 引擎会自动寻路，无需每帧 SetScriptedTargetFrame
            // 除非你想每隔几秒重新修正 NavMesh
            _timer += dt;
            fixedTimer += dt;
            if(_timer < 2.0f)
            {
                //给起身的时间
                return;
            }
            
            //每200ms 强制更新一次目标位置，避免Agent在移动过程中被打断
            if (fixedTimer > 0.2f)
            {
                fixedTimer = 0;
                //agent.SetScriptedPosition(ref _targetPos, false, _moveFlags);
                AgentControlHelper.ScriptedMoveToPoint(agent, _targetPos, _run);
            }
        }

        public bool IsFinished(Agent agent)
        {
            // 距离计算属于逻辑判断，保留在这里是合适的
            float distSq = agent.Position.DistanceSquared(_targetPos);
            return distSq <= (_stopDistance * _stopDistance) || _timer > _maxTime || !agent.IsActive();
        }

        public void OnEnd(Agent agent)
        {
            agent.TeleportToPosition(_targetPos);
            // 保持朝向瞬移
            agent.SetMovementDirection(_targetDir);

            AgentControlHelper.MoveEndAndInteractPrepare(agent);
            //AgentControlHelper.StopAndReset(agent);
        }
    }

    // 2. 跟随/追逐目标动作
    public class FollowAgentAction : IAtomicAction
    {
        private Agent _target;
        private bool _run;
        private float _stopDistance;
        private float _fixedTimer; // 
        // 内部状态，记录当前是否正在移动
        private bool _isMoving = false;
        private float _stopDistanceSq;  // 停止距离的平方
        private float _startDistanceSq; // 开始移动距离的平方 (StopDistance + Buffer)
        private bool _keepFollow;
        private float _currentDistanceSq ;
        // --- 极坐标参数 ---
        private float _radiusOffset;      // 距离目标多远 (例如 2.0米)
        private float _angleOffsetDeg;    // 角度偏移 (0=正前, 90=左, -90=右, 180=背后)

        private Vec3 _currentIdealPosition;
        //超时统计
        private float _timer;
        private float _maxTime;

        public FollowAgentAction(Agent target, bool run, float radius = 0.0f, float angleOffset = 0f, float stopDistance = 3.5f, float buffer = 1.5f, bool keepFollow = false)
        {
            _target = target;
            _run = run;
            _stopDistance = stopDistance;
            _fixedTimer = 0f;
            // 预计算平方值，避免每帧开根号，提升性能
            _stopDistanceSq = stopDistance * stopDistance;
            _startDistanceSq = (stopDistance + buffer) * (stopDistance + buffer);
            _keepFollow = keepFollow;
            _currentDistanceSq = _stopDistanceSq * 2;

            _radiusOffset = radius;
            _angleOffsetDeg = angleOffset;

            _timer = 0f;
            _maxTime = 5f;
        }

        public void OnStart(Agent agent)
        {
            // 刚开始不知道距离，先不做操作，交给 OnTick 判断
            _isMoving = false;


            _ = AgentControlHelper.MovePrepare(agent);
        }

        public void OnTick(Agent agent, float dt)
        {
            if (_target == null || !_target.IsActive()) return;
            _timer += dt;
            _fixedTimer += dt;
            if (_timer < 2.0f)
            {
                //给起身的时间
                return;
            }

            _currentIdealPosition = CalculateIdealPosition();
            _currentDistanceSq = agent.Position.DistanceSquared(_currentIdealPosition);       

            // --- 状态机逻辑 ---

            if (_timer > _maxTime && _currentDistanceSq > _stopDistanceSq)
            {
                if(!_keepFollow)
                    agent.TeleportToPosition(_currentIdealPosition);
            }

            if (_isMoving)
            {
                // 1. 如果正在移动，判断是否该停下了
                if (_currentDistanceSq <= _stopDistanceSq)
                {
                    // 到达指定范围内，刹车
                    StopMoving(agent);
                }
                else
                {
                    if (_fixedTimer > 0.2f)
                    {
                        _fixedTimer = 0;
                        StartMoving(agent);
                    }                   
                }
            }
            else
            {
                // 2. 如果是静止状态，判断是否被拉开太远，需要重新开始追
                // 注意这里用 _startDistanceSq (包含缓冲)，防止抖动
                if (_currentDistanceSq > _startDistanceSq)
                {
                    StartMoving(agent);
                }
                else
                {
                    // 也可以在这里加个 LookAt，让 Agent 停下来的时候看着目标，更自然
                    agent.SetLookAgent(_target);

                }
            }
        }
        private void StartMoving(Agent agent)
        {
            _isMoving = true;
            MoveToTarget(agent);
        }
        private Vec3 CalculateIdealPosition()
        {
            // 获取目标的朝向向量 (Forward)
            Vec3 targetDir = _target.LookFrame.rotation.f;
            targetDir.z = 0; // 忽略Z轴倾斜，只在平面计算
            targetDir.Normalize();

            // 如果有角度偏移，旋转向量
            if (MathF.Abs(_angleOffsetDeg) > 0.01f)
            {
                Mat3 rotMatrix = Mat3.Identity;
                rotMatrix.RotateAboutUp(MathF.DegToRad*(_angleOffsetDeg));
                targetDir = rotMatrix.TransformToParent(targetDir);
            }

            // 目标位置 + 方向 * 半径
            Vec3 idealPos = _target.Position + (targetDir * _radiusOffset);

            // --- 兜底逻辑：处理墙壁/不可达区域 ---
            // 获取场景的 NavMesh，找到 idealPos 最近的有效点
            // 这样如果 "面前2米" 是墙里，AI 会走到墙边，而不会傻站着不动或穿墙
            if (Mission.Current.Scene != null)
            {
                // 2. 修正导航网格
                WorldPosition validPos = new WorldPosition(Mission.Current.Scene, UIntPtr.Zero, idealPos, false);
                if (validPos.GetNavMesh() == UIntPtr.Zero)
                {
                    Mission.Current.Scene.GetNavigationMeshForPosition(ref idealPos);
                    validPos = new WorldPosition(Mission.Current.Scene, idealPos);
                }


                // 如果找到的有效点和理想点距离太远（说明理想点在虚空或墙里），就直接用有效点
                // 或者简单粗暴：直接返回 NavMesh 修正后的点，这是最稳妥的
                if (validPos.IsValid)
                {
                    return validPos.GetGroundVec3();
                }
            }

            return idealPos;
        }
        private void MoveToTarget(Agent agent)
        {
            AgentControlHelper.ScriptedMoveToPoint(agent, _currentIdealPosition, _run,true);
        }

        private void StopMoving(Agent agent)
        {
            _isMoving = false;
            // 清除移动指令，让 Agent 停下
            agent.ClearTargetFrame();
            // 如果不希望他完全发呆，可以让他看向目标
            agent.SetLookAgent(_target);
            // 还有一种更彻底的停止是 SetScriptedFlags(DoNotRun) 或者 Disable AI，视情况而定
            // 通常 ClearTargetFrame 就够了
        }
        public bool IsFinished(Agent agent)
        {
            if (_target == null || !_target.IsActive()) return true;
            if(_keepFollow)            
                return false; // 永远跟随
            else
            {
                if (_currentDistanceSq <= _stopDistanceSq )
                    return true;
            }
            return false;
        }

        public void OnEnd(Agent agent)
        {
            _isMoving = false;
            // 保持朝向瞬移
            agent.TeleportToPosition(_currentIdealPosition);
            Vec3 _targetDir = (_target.Position - agent.Position).NormalizedCopy();
            // 保持朝向瞬移
            agent.SetMovementDirection(_targetDir.AsVec2);

            AgentControlHelper.MoveEndAndInteractPrepare(agent);
        }
    }

    // 3. 看向某人
    public class LookAtAction : IAtomicAction
    {
        private Agent _target;
        private float _duration;
        private float _timer;

        public LookAtAction(Agent target, float duration = 2.0f)
        {
            _target = target;
            _duration = duration;
        }

        public void OnStart(Agent agent)
        {
            _timer = 0;
            AgentControlHelper.LookAtAgent(agent, _target);
        }

        public void OnTick(Agent agent, float dt)
        {
            _timer += dt;
            // 如果目标移动，AgentControlHelper.LookAtAgent 设置的是对象引用，
            // 引擎会自动追踪，不需要每帧 set。
            // 但如果 LookAtAgent 内部实现变了，这里可能需要再次调用，目前保持简单即可。
        }

        public bool IsFinished(Agent agent) {

            if (_target == null || !_target.IsActive()) return true;
            return _timer >= _duration ;
            }

        public void OnEnd(Agent agent)
        {
            //AgentControlHelper.StopLooking(agent);
        }
    }

    // 4. 播放动画动作
    public class PlayAnimAction : IAtomicAction
    {
        private string _animName;
        private bool _hasStarted = false;

        // 增加一个防卡死计时器，如果动画一直播不完（比如循环动画），强制结束
        private float _maxDuration;
        private float _timer;

        public PlayAnimAction(string animName, float maxDuration = 10f)
        {
            _animName = animName;
            _maxDuration = maxDuration;
        }

        public void OnStart(Agent agent)
        {
            AgentControlHelper.SetPose(agent, _animName);
            _hasStarted = true;
            _timer = 0f;
        }

        public void OnTick(Agent agent, float dt)
        {
            _timer += dt;
        }

        public bool IsFinished(Agent agent)
        {
            if (!_hasStarted) return false;

            // 1. 超时强制结束
            if (_timer >= _maxDuration) return true;

            // 2. 检查当前动作是否不再是目标动作（说明播放完毕，自动切回 idle 了）
            return !AgentControlHelper.IsPlayingPose(agent, _animName);
        }

        public void OnEnd(Agent agent) { }
    }

    public class FightEnemyAction : IAtomicAction
    {
        private Agent _targetEnemy;
        private bool _isFinished;
        private float _checkTimer;
        // 【新增】公开只读属性，让 Brain 可以检查当前在打谁
        public Agent TargetEnemy => _targetEnemy;
        /// <summary>
        /// 战斗行为：锁定并攻击指定敌人
        /// </summary>
        public FightEnemyAction(Agent targetEnemy)
        {
            _targetEnemy = targetEnemy;
            _isFinished = false;
            _checkTimer = 0f;
        }

        public void OnStart(Agent agent)
        {
            if (_targetEnemy == null || !_targetEnemy.IsActive())
            {
                _isFinished = true;
                return;
            }
            InformationManager.DisplayMessage(new InformationMessage($"开始攻击 {_targetEnemy.Name}！", Colors.Yellow));
            //BubbleSayMissionView.AgentBubbleSay(agent, "别碰我的老大！");
            CombatManager.StartFight(agent, _targetEnemy);

        }

        public void OnTick(Agent agent, float dt)
        {
            // 如果已经结束，就不浪费算力了
            if (_isFinished) return;

            // --- 终止条件检查 ---

            // 1. 目标不存在了，或者目标死了，或者目标被打晕了
            if (_targetEnemy == null || !_targetEnemy.IsActive() || _targetEnemy.Health <= 0)
            {
                _isFinished = true;
                return;
            }

            // 2. 我自己死了（大脑通常会处理，但这里为了保险）
            if (!agent.IsActive() || agent.Health <= 0)
            {
                _isFinished = true;
                return;
            }

            // --- 持续性指令 ---

            // 某些情况下引擎会重置目标（比如被另一个人砍了一刀），这里做一个强制纠偏
            // 每 0.5 秒检查并重申一次目标，不需要每帧都设
            _checkTimer += dt;
            if (_checkTimer > 0.5f)
            {
                _checkTimer = 0f;
                // 如果当前引擎锁定的目标不是我们要打的人，强制改回来
                if (agent.GetTargetAgent() != _targetEnemy)
                {
                    agent.SetTargetAgent(_targetEnemy);
                }
            }
        }

        public void OnEnd(Agent agent)
        {
            AgentControlHelper.StopAndReset(agent); // 确保退出时清理状态
        }

        public bool IsFinished(Agent agent)
        {
            return _isFinished;
        }
    }

    public class StayAction : IAtomicAction
    {
        private Agent _lookTarget; // 要盯着谁看
        private bool _keepRotating; // 是否要时刻调整朝向
        private Vec3 stayPos;
        private float _timer;

        public StayAction(Agent lookTarget, bool keepRotating = true)
        {
            _lookTarget = lookTarget;
            _keepRotating = keepRotating;
        }

        public void OnStart(Agent agent)
        {
            // 刚开始时，强制停止移动，防止上一个 MoveAction 留下的惯性
            //AgentControlHelper.StopAndReset(agent);
            //AgentControlHelper.MoveEndAndInteractPrepare(agent);

            stayPos = agent.Position;

            if (_lookTarget == null) return;
            Vec3 dir = (_lookTarget.Position - agent.Position).NormalizedCopy();

            agent.SetMovementDirection(dir.AsVec2);
            agent.SetLookAgent(_lookTarget);
        }

        public void OnTick(Agent agent, float dt)
        {
            _timer += dt;
            if(_timer > 0.2f)
            {
                _timer = 0;
                AgentControlHelper.MoveEndAndInteractPrepare(agent,stayPos);
            }

        }

        public bool IsFinished(Agent agent)
        {
            // 关键点：永远返回 false
            // 除非外部调用 ClearAllActions()，否则它永远不会自己结束
            // 这样就不会掉回 DefaultBehavior (回岗)
            return false;
        }

        public void OnEnd(Agent agent)
        {
            // 结束时不需要做特殊清理
            agent.SetLookAgent(null);
            AgentControlHelper.StopAndReset(agent);
        }
    }

    public class ReactionDecisionAction : IAtomicAction
    {
        private float _delayTimer;
        private Action<Agent> _onDecisionTime; // 延迟结束后执行的逻辑
        private bool _isFinished = false;

        public ReactionDecisionAction(float delaySeconds, Action<Agent> onDecisionTime)
        {
            _delayTimer = delaySeconds;
            _onDecisionTime = onDecisionTime;
        }

        public void OnStart(Agent agent)
        {
            // 可以在这里让 Agent 做一个“惊讶”的表情，或者愣住的动作
            //agent.SetMovementDirection(TaleWorlds.Library.Vec2.Zero);
        }

        public void OnTick(Agent agent, float dt)
        {
            if (_isFinished) return;

            _delayTimer -= dt;
            if (_delayTimer <= 0)
            {
                // 时间到！执行决策逻辑

                _isFinished = true;
                _onDecisionTime?.Invoke(agent);
            }
        }
        public void OnEnd(Agent agent)
        {

        }

        public bool IsFinished(Agent agent) => _isFinished;
    }
}