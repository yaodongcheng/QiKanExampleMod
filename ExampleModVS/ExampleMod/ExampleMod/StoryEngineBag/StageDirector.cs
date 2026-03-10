using SandBox.Missions.MissionLogics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static ExampleMod.StoryEngineBag.VisualCommands;

namespace ExampleMod.StoryEngineBag
{

    public enum StageSlot
    {
        None,
        Gate,    //大门
        Chair,       // 对应 CSV 的 ChairTrans
        Guest,      // 对应 CSV 的 GuestTrans
        Accompany1,  // 对应 CSV 的 Accompany1Trans
        Accompany2  // ...
    }
    public enum TransMode
    {

        None,  //错误
        Abs,           // 绝对坐标
        RelChair,      // 相对主席
        RelPlayer,      // 相对玩家
    }
    public struct SimpleTrans
    {
        public TransMode Mode;
        public Vec3 Offset;      // xyz 含义按 Mode 不同解释
        public float YawDeg;     // 朝向（平面角）
    }

    // 单个演员的状态节点
    public class ActorNode
    {
        public string EngineName;           // 演员名
        public StageSlot AssignedSlot;   // 分配到的槽位      
        public Vec3 SpecialPosition;    // 特殊位置（如果有）

    }

    public class StageConfig
    {
        public string SceneId;

        // 定义所有的槽位配置
        public SimpleTrans Gate;

        public SimpleTrans Chair;     // 主席/主人
        public SimpleTrans Guest;     // 贵宾
        public SimpleTrans Accompany1; // 陪席1
        public SimpleTrans Accompany2; // 陪席2

        // 从你的 DynamicRecord 解析
        public static StageConfig FromRecord(DynamicRecord record)
        {
            var cfg = new StageConfig();
            cfg.SceneId = record.GetString("ID");
            // 使用你写的 TryParseSimpleTrans
            TryParseSimpleTrans(record.GetString("GateTrans"), out cfg.Gate);
            TryParseSimpleTrans(record.GetString("ChairTrans"), out cfg.Chair);
            TryParseSimpleTrans(record.GetString("GuestTrans"), out cfg.Guest);
            TryParseSimpleTrans(record.GetString("Accompany1Trans"), out cfg.Accompany1);
            TryParseSimpleTrans(record.GetString("Accompany2Trans"), out cfg.Accompany2);

            return cfg;
        }

        public static bool TryParseSimpleTrans(string raw, out SimpleTrans result)
        {
            result = default;

            if (string.IsNullOrEmpty(raw))
                return false;

            var parts = raw.Split('|');
            if (parts.Length < 2)
                return false;

            TransMode mode;
            switch (parts[0].Trim().ToLower())
            {
                case "abs":
                    mode = TransMode.Abs;
                    break;
                case "rel_chair":
                    mode = TransMode.RelChair;
                    break;
                case "rel_player":
                    mode = TransMode.RelPlayer;
                    break;
                default:
                    return false;
            }
            float ParseOrDefault(int idx, float def = 0f)
            {
                if (idx >= parts.Length) return def;
                return float.TryParse(parts[idx], System.Globalization.NumberStyles.Float,
                                      System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;
            }

            float x = ParseOrDefault(1);
            float y = ParseOrDefault(2);
            float z = ParseOrDefault(3);
            float yawDeg = ParseOrDefault(4);

            result = new SimpleTrans
            {
                Mode = mode,
                Offset = new Vec3(x, y, z),
                YawDeg = yawDeg
            };
            return true;
        }
        public static MatrixFrame BuildWorldFrame( in SimpleTrans trans, MatrixFrame chairTargetFrame, MatrixFrame playerFrame)
        {

            MatrixFrame parentFrame;

            switch (trans.Mode)
            {
                case TransMode.Abs:
                    parentFrame = MatrixFrame.Identity;
                    break;
                case TransMode.RelChair:
                    parentFrame = chairTargetFrame; // 使用传入的“理论坐标”
                    break;
                case TransMode.RelPlayer:
                    parentFrame = playerFrame;
                    break;
                default:
                    parentFrame = MatrixFrame.Identity;
                    break;
            }

            if (trans.Mode == TransMode.Abs)
            {
                // ... (保持你原有的 Abs 逻辑) ...
                MatrixFrame result = MatrixFrame.Identity;
                result.origin = trans.Offset;
                float radYaw = trans.YawDeg * (MathF.PI / 180.0f);
                Mat3 rot = Mat3.Identity;
                rot.RotateAboutUp(radYaw);
                result.rotation = rot;
                return result;
            }

            // 复用你已经写好的局部转世界逻辑
            return BuildWorldFrameFromLocalPose(parentFrame, trans.Offset, trans.YawDeg);

           
        }
        public static MatrixFrame BuildWorldFrameFromLocalPose(in MatrixFrame parentFrame,  in Vec3 localOffset,  float yawDeg)
        {
            const float DegToRad = MathF.PI / 180.0f;

            // 1. 世界位置 = parent.origin + localOffset 在 parent 轴上的线性组合
            Vec3 worldPos =
                  parentFrame.origin
                + parentFrame.rotation.s * localOffset.x
                + parentFrame.rotation.f * localOffset.y
                + parentFrame.rotation.u * localOffset.z;

            // 获取地面高度，防止穿模或悬空
            float targetZ = worldPos.z;
            if (Mission.Current.Scene != null)
            {
                worldPos.z = Mission.Current.Scene.GetGroundHeightAtPosition(new Vec3(worldPos.x, worldPos.y, targetZ));
            }


            // 2. 世界旋转 = parent.rotation * (绕 Up 的 yawRot)
            Mat3 yawRot = Mat3.Identity;
            float radYaw = yawDeg * DegToRad;
            yawRot.RotateAboutUp(radYaw); // 如果方向相反，改成 -radYaw

            Mat3 worldRot = parentFrame.rotation.TransformToParent(yawRot);

            MatrixFrame result = MatrixFrame.Identity;
            result.origin = worldPos;
            result.rotation = worldRot;
            return result;
        }
    }
    public class StageDirector
    {
        // 存储当前所有演员：Key 是 AgentName, Value 是节点信息
        private Dictionary<string, ActorNode> _actorMap = new Dictionary<string, ActorNode>();

        // 反向索引，方便快速找这个槽位目前是谁
        private Dictionary<StageSlot, ActorNode> _slotMap = new Dictionary<StageSlot, ActorNode>();

        public StageSlot GetActorAssignedSlot(string engineName)
        {
            if (_actorMap.TryGetValue(engineName, out ActorNode node))
            {
                return node.AssignedSlot;
            }
            return StageSlot.None;
        }

        // 当前场景的 CSV 配置数据
        private StageConfig _currentConfig;
        public StageConfig CurrentConfig
        {
            get { return _currentConfig; }
        }

        public static StageDirector Instance;

        // 【新增】存储每个槽位对应的世界坐标
        private Dictionary<StageSlot, MatrixFrame> _cachedSlotFrames = new Dictionary<StageSlot, MatrixFrame>();

        public void Initialize(StageConfig config)
        {
            _currentConfig = config;
            _actorMap.Clear();
            _slotMap.Clear();
            Instance = this;
        }

        // 辅助方法：计算单个配置并存入字典
        private void CacheSlot(StageSlot slot, SimpleTrans trans, MatrixFrame chairFrame, MatrixFrame playerFrame)
        {
            // 这里存入计算结果
            var frame = StageConfig.BuildWorldFrame(trans, chairFrame, playerFrame);
            _cachedSlotFrames[slot] = frame;
        }
        // 步骤1：注册演员（按照名字分配角色）
        public void RegisterActor(string engineName, StageSlot slot)
        {
            var node = new ActorNode
            {
                EngineName = engineName,
                AssignedSlot = slot
                
            };

            if (_actorMap.ContainsKey(engineName)) _actorMap[engineName] = node;
            else _actorMap.Add(engineName, node);

            if (_slotMap.ContainsKey(slot)) _slotMap[slot] = node;
            else _slotMap.Add(slot, node);
        }
        public void ChangeActorSlot(string engineName, StageSlot newSlot)
        {
            if (_actorMap.TryGetValue(engineName, out ActorNode node))
            {
                // 1. 更新反向索引 _slotMap (如果不维护 _slotMap 可以跳过，但建议维护)
                if (_slotMap.ContainsKey(node.AssignedSlot) && _slotMap[node.AssignedSlot] == node)
                {
                    _slotMap.Remove(node.AssignedSlot);
                }

                // 2. 核心修改
                node.AssignedSlot = newSlot;

                // 3. 更新新槽位的反向索引
                if (_slotMap.ContainsKey(newSlot)) _slotMap[newSlot] = node;
                else _slotMap.Add(newSlot, node);
            }
        }
        // 步骤2：计算所有坐标（核心！）
        // 在对话开始前调用一次，之后就不再调用，从而锁死位置防止闪烁
        public void CalculateAllPositions()
        {


            _cachedSlotFrames.Clear();


            MatrixFrame playerFrame = Agent.Main.Frame;
            MatrixFrame chairFrame = MatrixFrame.Identity;

            if (_currentConfig.Chair.Mode != TransMode.None)
            {
                chairFrame = StageConfig.BuildWorldFrame(
                    _currentConfig.Chair,
                    MatrixFrame.Identity, 
                    playerFrame
                );              
               
            }
            _cachedSlotFrames[StageSlot.Chair] = chairFrame;
            // 3. 统一计算并缓存其他所有槽位 (无论当前是否有人)
            // 这样即使以后有人从 Chair 移动到 Gate，Gate 的坐标也早就准备好了
            CacheSlot(StageSlot.Gate, _currentConfig.Gate, chairFrame, playerFrame);
            CacheSlot(StageSlot.Guest, _currentConfig.Guest, chairFrame, playerFrame);
            CacheSlot(StageSlot.Accompany1, _currentConfig.Accompany1, chairFrame, playerFrame);
            CacheSlot(StageSlot.Accompany2, _currentConfig.Accompany2, chairFrame, playerFrame);         
        }

       //设置某个演员应该去的特殊坐标
       public void SetActorSpecialPosition(string engineName, Vec3 position)
        {
            if (_actorMap.TryGetValue(engineName, out ActorNode node))
            {
                node.SpecialPosition = position;
            }
            //以防actorMap里面丢失了信息
            

        }

        // 获取某个演员应该去的坐标
        public MatrixFrame GetTargetPosition(string engineName,string stringId = null)
        {
            //进入新的一幕了 actorMap没有这些正在退场的人的信息了，这些还是作为普通变量记在
            if (_actorMap.TryGetValue(engineName, out ActorNode node))
            {
                if (node != null && node.AssignedSlot != StageSlot.None && _cachedSlotFrames.TryGetValue(node.AssignedSlot, out MatrixFrame frame))
                {
                    return frame;
                }
            }
            //如果Slot为None，看看这个Agent有没有被分配了独有的目标位置
            if(node!= null && node.AssignedSlot == StageSlot.None)
            {
                if(node.SpecialPosition != Vec3.Invalid)
                {
                    return new MatrixFrame(Mat3.Identity, node.SpecialPosition);
                }
            }
            //尝试用VariableManager获取
            if (stringId != null)
            {
                float x = float.Parse(VariableManager.GetV(stringId, "SpecialPositionX"));
                float y = float.Parse(VariableManager.GetV(stringId, "SpecialPositionY"));
                float z = float.Parse(VariableManager.GetV(stringId, "SpecialPositionZ"));
                return new MatrixFrame(Mat3.Identity, new Vec3(x,y,z));
            }
            // 没找到或者 Slot 为 None 的情况
            return MatrixFrame.Identity;
        }


        public bool HideActor( ScriptActorInfo info)
        {
            var storyEngine = StoryEngine.Instance;

            var agent = VisualCommands.GetAgentInMission(info.HeroId, info.ScriptName);

            if (!VisualCommands.IsSettlementOwner(agent.Name))
            {
                if (agent != null)
                {
                        //获取主角理论位置
                        var playerFrame = GetTargetPosition(Agent.Main.Name);
                        //计算主角理论位置周围一圈的随机点
                        Vec3 spawnPos = GetRandomNavMeshPoint(playerFrame.origin, 7); 
                        WorldPosition targetPos;

                        if (spawnPos != Vec3.Invalid)
                        {
                        VariableManager.SetV(info.HeroId, "SpecialPositionX", spawnPos.x.ToString());
                        VariableManager.SetV(info.HeroId, "SpecialPositionY", spawnPos.y.ToString());
                        VariableManager.SetV(info.HeroId, "SpecialPositionZ", spawnPos.z.ToString());
                        ChangeActorSlot(info.EngineName, StageSlot.None);
                            SetActorSpecialPosition(info.EngineName, spawnPos);
                            targetPos = new WorldPosition(Agent.Main.Mission.Scene, spawnPos);
                        }
                        else
                        {
                            SimpleTrans gate = _currentConfig.Gate;
                            targetPos = new WorldPosition(Agent.Main.Mission.Scene, gate.Offset);
                            ChangeActorSlot(info.EngineName, StageSlot.Gate);
                        }

                        
                       

                        // 有些 NPC 可能被设置为 None 或其他状态，确保他是 AI
                        if (agent.Controller != Agent.ControllerType.AI)
                        {
                            agent.Controller = Agent.ControllerType.AI;
                        }
                        agent.ClearTargetFrame();
                        agent.SetScriptedCombatFlags(Agent.AISpecialCombatModeFlags.None);
                        agent.SetScriptedFlags(Agent.AIScriptedFrameFlags.None);
                        //agent.SetAgentFlags(TaleWorlds.Core.AgentFlag.None);
                        agent.ResetEnemyCaches();
                        //走到理论位置，行走需要时间，看这里怎么处理，是否要停掉，然后等到了位置之后，再Resume
                        agent.SetScriptedPosition(ref targetPos, false);

                        // 4. 将 Agent 加入等待列表
                        if (!storyEngine._movingAgentsForLeaving.Contains(agent))
                        {
                            storyEngine._movingAgentsForLeaving.Add(agent);
                        }

                        // 标记为正在等待移动
                        storyEngine._isWaitingForMovement_Leaving = true;

                        // 返回 true，告诉 RunLoop 暂停
                        return true;
                    }

                    
                
            }
            return false;
        }

        public void NinjaShow()
        {
            var engine = StoryEngine.Instance;
            var ctx = engine.GetCurrentContext();
            foreach (var kvp in engine.currentSceneParticipants)
            {
                var info = kvp.Value;
                if (info.HeroId.ToLower().Contains("ninja") || info.ScriptName.Contains("忍者"))
                {
                    //这里单独放烟雾弹就行
                    var playerFrame = GetTargetPosition(Agent.Main.Name);
                    Vec3 spawnPos = GetRandomNavMeshPoint(playerFrame.origin, 5);
                    Vec3 targetPos;
                    if (spawnPos != Vec3.Invalid)
                    {
                        //ChangeActorSlot(info.EngineName, StageSlot.None);
                        SetActorSpecialPosition(info.EngineName, spawnPos);
                        targetPos = spawnPos;
                    }
                    else
                    {
                        SimpleTrans gate = _currentConfig.Gate;
                        targetPos = gate.Offset;
                    }

                    
                    MatrixFrame particleFrame = new MatrixFrame(Agent.Main.Frame.rotation, targetPos);
                    string particleName = "psys_game_boulder_stone_coll";
                    int particleId = ParticleSystemManager.GetRuntimeIdByName(particleName);
                    if (particleId != -1)
                    {
                        //InformationManager.DisplayMessage(new InformationMessage($"忍者出场特效{particleId}。", Colors.Red));
                        Mission.Current.Scene.CreateBurstParticle(particleId, particleFrame);
                    }
                    //SpringArmCameraView.UseCameraTemlate("xm_Any_Side45_Far_R", null, null, Vec3.Zero);
                    //SystemCommands.ExecuteWait(2.0f);
                    return;
                }
            }
        }

        public bool ShowActor(ScriptActorInfo info)
        {

            var storyEngine = StoryEngine.Instance;
            //玩家和忍者可以瞬间出现，其他人都要走到指定位置
            if(info.EngineName == Agent.Main.Name)
            {
                Agent agent = Agent.Main;
                var targetFrame = GetTargetPosition(agent.Name);
                agent.TeleportToPosition(targetFrame.origin);
                agent.SetMovementDirection(targetFrame.rotation.f.AsVec2);
                
            }
           
            else
            {

                Agent agent = VisualCommands.GetAgentInMission(info.HeroId, info.ScriptName);

                //只有不是初始就出现的才需要后续登场
                if (agent != null)
                {
                    //获取主角理论位置
                    var playerFrame = GetTargetPosition(Agent.Main.Name);
                    //计算主角理论位置周围一圈的随机点
                    Vec3 spawnPos;
                    bool IsNinja = info.HeroId.ToLower().Contains("ninja") || info.ScriptName.Contains("忍者");
                    _actorMap.TryGetValue(info.EngineName, out ActorNode node);
                    if(IsNinja && node != null && node.SpecialPosition != Vec3.Invalid)
                            spawnPos = node.SpecialPosition; 
                    else
                        spawnPos = GetRandomNavMeshPoint(playerFrame.origin, 7);
                    string currentLocation = GetCurrentLocationId();
                    if (spawnPos != Vec3.Invalid && currentLocation != "lordshall")
                    {
                        agent.TeleportToPosition(spawnPos);                        
                    }
                    else
                    {
                        //城主间也要走大门 之后可能改成只有center才随机生成
                        //默认走大门
                        SimpleTrans gate = _currentConfig.Gate;
                        //瞬移到入口
                        agent.TeleportToPosition(gate.Offset);
                    }



                    //获取理论位置
                    var targetFrame = GetTargetPosition(agent.Name);
                    WorldPosition targetPos = new WorldPosition(Agent.Main.Mission.Scene, targetFrame.origin);
                    agent.SetScriptedPosition(ref targetPos, false, Agent.AIScriptedFrameFlags.GoToPosition | Agent.AIScriptedFrameFlags.NeverSlowDown);

                    // 4. 将 Agent 加入等待列表
                    if (!storyEngine._movingAgentsForEntering.Contains(agent))
                    {
                        storyEngine._movingAgentsForEntering.Add(agent);
                    }

                    // 标记为正在等待移动
                    storyEngine._isWaitingForMovement_Entering = true;

                    // 返回 true，告诉 RunLoop 暂停
                    return true;
                }
            }         

            return false;
            
        }

        public Vec3 GetRandomNavMeshPoint(Vec3 centerPos, float radius)
        {
            if (Mission.Current == null || Mission.Current.Scene == null) return Vec3.Invalid;

            Scene scene = Mission.Current.Scene;

            // 尝试次数，防止随机随到了墙里
            int maxAttempts = 15;

            for (int i = 0; i < maxAttempts; i++)
            {
                // 1. 随机角度
                float angle = MBRandom.RandomFloat * 2 * MathF.PI;

                // 2. 计算圆周上的几何坐标 (保留中心点的高度 Z)
                float x = centerPos.x + MathF.Cos(angle) * radius;
                float y = centerPos.y + MathF.Sin(angle) * radius;

                // 构造初始探测点
                Vec3 targetPos = new Vec3(x, y, centerPos.z);

                // 3. 核心修改：使用你源码中存在的 GetNavigationMeshForPosition
                // 参数说明：
                // ref targetPos: 输入尝试的坐标，如果成功，它会被修改为 NavMesh 上的精确坐标
                // out int faceId: 输出该点所在的网格面ID，我们可以忽略
                // 2.0f: 高度容差，如果地面比中心点高/低2米以内，都能吸附
                int faceId;
                if (scene.GetNavigationMeshForPosition(ref targetPos, out faceId, 2.0f))
                {
                    // 如果返回 true，targetPos 已经被修改为有效的 NavMesh 坐标了
                    return targetPos;
                }
            }

            // 如果尝试了多次都没找到（比如中心点在一个极小的孤岛上），返回无效坐标
            return Vec3.Invalid;
        }






    }
    
    
}
