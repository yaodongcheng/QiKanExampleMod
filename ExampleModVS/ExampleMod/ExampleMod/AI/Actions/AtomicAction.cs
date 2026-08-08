using Newtonsoft.Json;
using SandBox.Conversation.MissionLogics;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.CampaignSystem.Actions;
using static TaleWorlds.MountAndBlade.Agent;

#pragma warning disable CS0618 // Intentional migration: uses deprecated NpcInitiative
namespace LivingWorldNpcs
{
    public interface IAtomicAction
    {
        void OnStart(Agent agent);
        void OnTick(Agent agent, float dt);
        bool IsFinished(Agent agent);
        void OnEnd(Agent agent);

        /// <summary>
        /// 请求中断当前动作。设置内部标记使 IsFinished 返回 true，
        /// 下一帧 Tick 会走标准清理路径（OnEnd → _currentAction=null → dequeue next）。
        /// 比外部直接捅 _currentAction=null 安全：不会跳过 OnEnd 导致资源泄漏。
        /// </summary>
        void RequestInterrupt();
    }
    // 这个Action负责"点火"，即启动LLM生成任务
    public class PrepareOpeningAction : IAtomicAction
    {
        private Agent self;
        private SingNpcMemorySystem memory;
        InitiativeType Type;
        private string ContextDesc;
        private IntentBase _intent;
        private PendingConflict _conflictData;
        public PrepareOpeningAction(InitiativeType type, string desc)
        {
            Type = type;
            ContextDesc = desc;
        }
         public PrepareOpeningAction(InitiativeType type, PendingConflict conflict)
        {
            Type = type;
            _conflictData = conflict;
        }
        private async Task Thinking()
        {
            // LLM 不可用时跳过 HTTP 调用，避免 30s 超时让 NPC 原地发呆
            if (!Settings.Instance.IsLLMConfigured)
            {
                memory.CurrentInitiative.JsonResponseOpening =
                    // LLM 降级开场白：NPC 警惕地看着玩家（对话中直接显示给玩家）
                   "{ \"npc_reply\": \"" + LWNTextHelper.ResolveText("LWN_action_llm_fallback_wary", "(looks at you warily)") + "\", \"player_next_options\": [] }";
            }
            else
            {
                string openingPrompt = PromptBuilder.BuildOpeningPrompt(memory, self);
                try
                {
                    string jsonResponse = await LLMService.Instance.ChatAsync(openingPrompt, 300, true);
                    jsonResponse = LLMService.CleanJson(jsonResponse);
                    memory.CurrentInitiative.JsonResponseOpening = jsonResponse;
                }
                catch (Exception )
                {
                    memory.CurrentInitiative.JsonResponseOpening =
                        // LLM 降级开场白：NPC 警惕地看着玩家（对话中直接显示给玩家）
                       "{ \"npc_reply\": \"" + LWNTextHelper.ResolveText("LWN_action_llm_fallback_wary", "(looks at you warily)") + "\", \"player_next_options\": [] }";
                }
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

            // 新路径：IntentBase 驱动（优先）
            if (_intent != null)
            {
                // 从意图和冲突数据创建 NpcInitiative
                if (_conflictData != null)
                    memory.CurrentInitiative = new NpcInitiative(Type, _conflictData);
                else
                    memory.CurrentInitiative = new NpcInitiative(Type, _intent.DisplayName);
            }
            // 旧路径：直接传 InitiativeType
            else if (_conflictData == null)
                memory.CurrentInitiative = new NpcInitiative(Type, ContextDesc);
            else
                memory.CurrentInitiative = new NpcInitiative(Type, _conflictData);

            _ = Task.Run(() => Thinking());
        }

        public void RequestInterrupt() { }

        public bool IsFinished(Agent agent)
        {
            // 瞬间完成，绝不阻塞，立刻进入下一个 MoveToPositionAction
            return true;
        }
    }
    public class ForceTalkAction : IAtomicAction
    {
        private bool _isFinished = false;
        private bool _interrupted;
        public void RequestInterrupt() { _interrupted = true; }
        public bool IsFinished(Agent agent) => _isFinished || _interrupted;

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
            if (Settings.Instance.ShowDebugMessages)
                // 质问准备飘字：{NAME} 正走向玩家准备开口
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_action_ask_prepare", ("NAME", agent.Name.ToString())), Colors.Yellow));
            // 2. 如果 LLM 响应已经就绪（无 LLM 时立即 fallback），跳过等待
            if (memory.CurrentInitiative != null && memory.CurrentInitiative.IsReady)
            {
                _isFinished = true;
            }
        }

        public void OnTick(Agent agent, float dt)
        {
            if (_isFinished) return;
            _timer+= dt;
            if(_timer> 0.5f)
            {
                _timer = 0f;
                //检查生成好了没（LLM 路径可能需要等待几轮）
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
                // LLM 不可用时：KCD2/老滚 风格的本地化质问 — 固定选项 + 确定后果
                if (!Settings.Instance.IsLLMConfigured)
                {
                    ShowVanillaConfrontation(agent);
                    return;
                }
                _ = InteractionMissionView.Instance.StartFreeConversationFlow(agent, false);

                if (Settings.Instance.ShowDebugMessages)
                    // 质问开始飘字：{NAME} 开始质问玩家
                    InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_action_ask_begin", ("NAME", agent.Name.ToString())), Colors.Yellow));
            }
        }

        /// <summary>
        /// 无 LLM 时的降级质问流程：模仿 KCD2 / 上古卷轴
        /// 两个明确选项（赔钱/归还），玩家也可以直接拔刀或逃跑（游戏内即时操作）
        /// </summary>
        private void ShowVanillaConfrontation(Agent victim)
        {
            var victimHeroObj = (victim.Character as CharacterObject)?.HeroObject;
            string victimName = victim.Name?.ToString()
                // 兜底称呼：受害者是模板 NPC（无 HeroObject）时叫"守卫"
                ?? LWNTextHelper.ResolveText("LWN_action_victim_name_guard", "Guard");
            int stolenValue = StealManager.GetStolenValue(victim);
            int compensationGold = stolenValue > 50 ? stolenValue : 50;

            // 使用 NarrativeResolver 获取叙事文本
            string openingBubble = NarrativeResolver.GetDialogue("Steal_Caught", DialogueFactors.FromContext(null), out _, victimHeroObj, victim);
            AgentHudMissionView.AgentSay(victim, string.IsNullOrEmpty(openingBubble)
                // 冒泡台词：当场抓住偷窃的开场白（NarrativeResolver 无结果时兜底）
                ? LWNTextHelper.ResolveText("LWN_action_steal_caught_opening", "Ha! You dare steal from me?! You will answer for this!")
                : openingBubble);

            // UI 弹窗标题：{NAME} 发现了你的偷窃行为
            string inquiryTitle = LWNTextHelper.ResolveCompound("LWN_action_steal_caught_inquiry_title", ("NAME", victimName));
            // UI 弹窗描述：{NAME} 怒气冲冲地瞪着你，手已经按在了武器上
            string inquiryDesc = LWNTextHelper.ResolveCompound("LWN_action_steal_caught_inquiry_desc", ("NAME", victimName));
            // UI 按钮：破财消灾——掏出 {GOLD} 第纳尔赔钱
            string payButton = LWNTextHelper.ResolveCompound("LWN_action_steal_caught_pay_button", ("GOLD", compensationGold.ToString()));
            // UI 按钮：归还财物——双手奉还，低头认错
            string returnButton = LWNTextHelper.ResolveText("LWN_action_steal_caught_return_button", "Return the goods and beg forgiveness");
            InformationManager.ShowInquiry(new InquiryData(
                inquiryTitle,
                inquiryDesc,
                true,
                true,
                payButton,
                returnButton,
                () =>
                {
                    if (Hero.MainHero.Gold >= compensationGold)
                    {
                        AgentControlHelper.TransferGold(Hero.MainHero, victimHeroObj, compensationGold, notify: false);
                        string payBubble = NarrativeResolver.GetDialogue("Steal_Caught_PayGold", DialogueFactors.FromContext(null), out _, victimHeroObj, victim);
                        AgentHudMissionView.AgentSay(victim, string.IsNullOrEmpty(payBubble)
                            // 冒泡台词：赔钱成功（NarrativeResolver 无结果时兜底）
                            ? LWNTextHelper.ResolveText("LWN_action_steal_caught_pay_success", "Hmph. At least you know what's good for you.")
                            : payBubble);
                        string payNarrator = NarrativeResolver.GetDialogue("Steal_Caught_PayGold_Narrator", DialogueFactors.FromContext(null), out _)
                            .Replace("{GIVER}", compensationGold.ToString())
                            .Replace("{NPC}", victimName);
                        InformationManager.DisplayMessage(new InformationMessage(payNarrator, Colors.Yellow));
                        if (victimHeroObj != null)
                            ChangeRelationAction.ApplyPlayerRelation(victimHeroObj, -3);
                    }
                    else
                    {
                        string tooPoorBubble = NarrativeResolver.GetDialogue("Steal_Caught_PayGold_TooPoor", DialogueFactors.FromContext(null), out _, victimHeroObj, victim);
                        AgentHudMissionView.AgentSay(victim, string.IsNullOrEmpty(tooPoorBubble)
                            // 冒泡台词：想赔钱但钱不够（NarrativeResolver 无结果时兜底）
                            ? LWNTextHelper.ResolveText("LWN_action_steal_caught_too_poor", "Steal with no coin to pay? Then pay with your life!")
                            : tooPoorBubble);
                        // 赔不起钱飘字：{NAME} 见玩家没钱大怒拔刀
                        InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_action_too_poor_fight", ("NAME", victimName)), Colors.Red));
                        AgentAIController.Instance?.SendEventToAgent(victim, "order_attack", Agent.Main);
                        AgentAIController.Instance?.BroadcastEventInRange(victim.Position, 15, "event_agent_damaged", true, victim, Agent.Main);
                    }
                },
                () =>
                {
                    int returned = StealManager.ReturnStolenItems(victim);
                    if (returned > 0)
                    {
                        string returnBubble = NarrativeResolver.GetDialogue("Steal_Caught_ReturnItems", DialogueFactors.FromContext(null), out _, victimHeroObj, victim);
                        AgentHudMissionView.AgentSay(victim, string.IsNullOrEmpty(returnBubble)
                            // 冒泡台词：归还赃物成功（NarrativeResolver 无结果时兜底）
                            ? LWNTextHelper.ResolveText("LWN_action_steal_caught_return_success", "Get lost! Don't let me see you again.")
                            : returnBubble);
                        string returnNarrator = NarrativeResolver.GetDialogue("Steal_Caught_ReturnItems_Narrator", DialogueFactors.FromContext(null), out _)
                            .Replace("{COUNT}", returned.ToString())
                            .Replace("{NPC}", victimName);
                        InformationManager.DisplayMessage(new InformationMessage(returnNarrator, Colors.Green));
                        if (victimHeroObj != null)
                            ChangeRelationAction.ApplyPlayerRelation(victimHeroObj, -5);
                    }
                    else
                    {
                        string refuseBubble = NarrativeResolver.GetDialogue("Steal_Caught_Refuse", DialogueFactors.FromContext(null), out _, victimHeroObj, victim);
                        AgentHudMissionView.AgentSay(victim, string.IsNullOrEmpty(refuseBubble)
                            // 冒泡台词：拒绝归还赃物（NarrativeResolver 无结果时兜底）
                            ? LWNTextHelper.ResolveText("LWN_action_steal_caught_refuse", "Trying to weasel out of it?")
                            : refuseBubble);
                        string refuseNarrator = NarrativeResolver.GetDialogue("Steal_Caught_Refuse_Narrator", DialogueFactors.FromContext(null), out _)
                            .Replace("{NPC}", victimName);
                        InformationManager.DisplayMessage(new InformationMessage(refuseNarrator, Colors.Red));
                        if (victimHeroObj != null)
                            ChangeRelationAction.ApplyPlayerRelation(victimHeroObj, -8);
                    }
                },
                "",
                0f
            ), true);
        }

    }
    public class DrawWeaponAction : IAtomicAction
    {
        private bool _isFinished = false;
        private bool _interrupted;
        public void RequestInterrupt() { _interrupted = true; }
        public bool IsFinished(Agent agent) => _isFinished || _interrupted;

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
        private bool _interrupted;
        public void RequestInterrupt() { _interrupted = true; }

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
            return _isFinished || _interrupted;
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
        private float _maxTime;           // 卡死预算（按距离/速度算，不固定）
        private float fixedTimer = 0;
        private float _sampleTimer;       // 进度采样计时
        private float _lastDist;          // 上次采样距目标距离
        private float _lastProgressTime;  // 最近一次"有进展"时刻
        private bool _teleportOnEnd;      // 仅卡死兜底才瞬移（正常到达不瞬移）
        private bool _interrupted;
        public void RequestInterrupt() { _interrupted = true; }

        public MoveToPositionAction(Vec3 pos, Vec2 dir ,bool run = false, float stopDistance = 1.0f)
        {
            _targetPos = pos;
            _targetDir = dir;
            _run = run;
            _stopDistance = stopDistance;

            _timer = 0f;
        }

        public void OnStart(Agent agent)
        {
            // 只有自然站立/走路的 NPC 才跳过 2 秒起身延迟；
            // 坐椅子、蹲着、躺着（自定义 pose）的都需要过渡动画时间。
            // 注意：必须在 MovePrepare 之前判，否则 StopUsingGameObject 可能提前改状态。
            bool needsDelay = agent.IsUsingGameObject
                           || agent.CrouchMode
                           || !string.IsNullOrEmpty(AgentControlHelper.GetPose(agent));

            _= AgentControlHelper.MovePrepare(agent);

            if (!needsDelay)
                _timer = 2.0f;

            // 🔴 卡死预算按"距离/速度"算，不固定 8s（实机 badcase：远距离目标走着走着被瞬移）。
            // 走 ~1.5m/s / 跑 ~3.5m/s，×1.5 给寻路绕路余量，下限 5s。
            float dist = agent.Position.Distance(_targetPos);
            float speed = _run ? 3.5f : 1.5f;
            _maxTime = Math.Max(5f, dist / speed * 1.5f);
            _lastDist = dist;
            _lastProgressTime = 0f;
            _teleportOnEnd = false;
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
                AgentControlHelper.ScriptedMoveToPoint(agent, _targetPos, _run);
            }

            // 进度采样（每 0.5s）：仍在接近 = 有速度 → 记"最近有进展时刻"，永不瞬移
            _sampleTimer += dt;
            if (_sampleTimer >= 0.5f)
            {
                _sampleTimer = 0f;
                float dist = agent.Position.Distance(_targetPos);
                if (_lastDist - dist > 0.2f) { _lastDist = dist; _lastProgressTime = _timer; }
            }
        }

        public bool IsFinished(Agent agent)
        {
            if (_interrupted) return true;
            if (!agent.IsActive()) return true;
            float dist = agent.Position.Distance(_targetPos);
            if (dist <= _stopDistance) return true;   // 正常到达 → 不瞬移（对齐 FollowAgentAction 纪律）
            // 卡死判定：超预算 且 最近 3s 无进展 → 瞬移兜底；仍在走（有速度）→ 继续
            if (_timer > _maxTime && _timer - _lastProgressTime > 3f)
            {
                _teleportOnEnd = true;
                return true;
            }
            return false;
        }

        public void OnEnd(Agent agent)
        {
            // 仅卡死兜底才瞬移；正常到达保留原位（几十厘米偏差肉眼不可见，瞬移反而突兀）
            if (_teleportOnEnd && agent.IsActive())
                agent.TeleportToPosition(_targetPos);
            agent.SetMovementDirection(_targetDir);

            AgentControlHelper.MoveEndAndInteractPrepare(agent);
        }
    }

    // 2.5 逃离动作：远离威胁 Agent 跑开一段距离（引擎级非战斗人员——儿童专用）。
    // 任何"进入战斗"的流程（order_attack / DeferredCombat / 护主参战）对儿童都替换为本动作：
    // 不战斗、单纯逃离。跑完恢复原版 AI（不像 MoveToPositionAction 那样锁定进对话）。
    public class FleeFromAction : IAtomicAction
    {
        private readonly Agent _threat;
        private Vec3 _targetPos;
        private bool _foundTarget;
        private float _timer;
        private float _fixedTimer;
        private float _maxTime;
        private bool _interrupted;
        public void RequestInterrupt() { _interrupted = true; }

        /// <param name="threat">要逃离的威胁（通常是玩家或攻击者）</param>
        public FleeFromAction(Agent threat)
        {
            _threat = threat;
            _timer = 0f;
            _fixedTimer = 0f;
            _maxTime = 10f; // 8~14m walk 速度约 6~10s，留 10s 保险
        }

        public void OnStart(Agent agent)
        {
            if (agent == null || !agent.IsActive()) { _interrupted = true; return; }

            // 逃跑方向：远离威胁 ±45° 抖动，8~14m，取第一个 navmesh 有效点（照动物挣脱轮子 OnAnimalStruggleFlee）
            Vec3 away = _threat != null && _threat.IsActive()
                ? agent.Position - _threat.Position
                : new Vec3(1f, 0f, 0f);
            away.z = 0f;
            if (away.LengthSquared < 0.001f) away = new Vec3(1f, 0f, 0f);
            away = away.NormalizedCopy();

            for (int i = 0; i < 6; i++)
            {
                float angle = (MBRandom.RandomFloat - 0.5f) * MathF.PI * 0.5f; // ±45°
                float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
                Vec3 dir = new Vec3(away.x * cos - away.y * sin, away.x * sin + away.y * cos, 0f);
                Vec3 fleePos = agent.Position + dir * (8f + MBRandom.RandomFloat * 6f);
                if (agent.Mission?.Scene != null && V.NavMesh(agent.Mission.Scene, fleePos, out _))
                {
                    _targetPos = fleePos;
                    _foundTarget = true;
                    break;
                }
            }
            // 兜底：找不到可寻路点 → 直线逃离方向（引擎自动修正 navmesh）
            if (!_foundTarget)
                _targetPos = agent.Position + away * 8f;

            _ = AgentControlHelper.MovePrepare(agent);
        }

        public void OnTick(Agent agent, float dt)
        {
            _timer += dt;
            _fixedTimer += dt;
            if (_fixedTimer < 0.2f) return;
            _fixedTimer = 0f;
            // 持续刷新脚本移动目标（as_human_child 无 run 动画，isRun=false 走 walk）
            AgentControlHelper.ScriptedMoveToPoint(agent, _targetPos, isRun: false);
        }

        public bool IsFinished(Agent agent)
        {
            float distSq = agent.Position.DistanceSquared(_targetPos);
            return _interrupted || distSq <= 1.0f || _timer > _maxTime || !agent.IsActive();
        }

        public void OnEnd(Agent agent)
        {
            // 逃跑完成 → 恢复原版 AI（DisableScriptedMovement + 回 AI 控制）
            if (agent != null && agent.IsActive())
                AgentControlHelper.ForceUnlockAgent(agent);
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
        private bool _interrupted;
        public void RequestInterrupt() { _interrupted = true; }

        /// <summary>跟随目标（执行器 following 谓词判定用）。</summary>
        internal Agent TargetAgent => _target;

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

            // 只有自然站立/走路的 NPC 才跳过 2 秒起身延迟；
            // 坐椅子、蹲着、躺着（自定义 pose）的都需要过渡动画时间。
            // 注意：必须在 MovePrepare 之前判，否则 StopUsingGameObject 可能提前改状态。
            bool needsDelay = agent.IsUsingGameObject
                           || agent.CrouchMode  ;

            _ = AgentControlHelper.MovePrepare(agent);

            if (!needsDelay)
                _timer = 2.0f;
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
                    V.NavMeshSnap(Mission.Current.Scene, ref idealPos);
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
        }
        public bool IsFinished(Agent agent)
        {
            if (_interrupted) return true;
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
            // 不瞬移：NPC 已在 stopDistance 内（ComeHere 默认 0.5m），
            // 几十厘米的偏差肉眼不可见，瞬移反而比到位的视觉跳变更突兀。
            if (_target != null && _target.IsActive())
            {
                Vec3 targetDir = (_target.Position - agent.Position).NormalizedCopy();
                agent.SetMovementDirection(targetDir.AsVec2);
            }
            AgentControlHelper.MoveEndAndInteractPrepare(agent);
        }
    }

    // 3. 看向某人
    public class LookAtAction : IAtomicAction
    {
        private Agent _target;
        private float _duration;
        private float _timer;
        private bool _interrupted;
        public void RequestInterrupt() { _interrupted = true; }

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

            if (_interrupted) return true;
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
        private bool _interrupted;
        public void RequestInterrupt() { _interrupted = true; }

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
            if (_interrupted) return true;
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
        private bool _interrupted;
        public void RequestInterrupt() { _interrupted = true; }
        private float _checkTimer;
        // 【新增】公开只读属性，让 Brain 可以检查当前在打谁
        public Agent TargetEnemy => _targetEnemy;

        /// <summary>残血认输已触发标记（每次创建新实例重置）</summary>
        private bool _surrenderTriggered = false;
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
            // 战斗入口必须解锁：ClearAllActions 默认会给 Agent 留下
            // SetScriptedPosition(DoNotRun | NoAttack) 锁，不清除则原生战斗 AI
            // 不能追人也不能出手（登记为战斗者却傻站着）。
            // 各事件处理器（order_attack / DeferredCombat / 目击反击）无需各自补 ForceUnlockAgent。
            AgentControlHelper.ForceUnlockAgent(agent);
            if (Settings.Instance.ShowDebugMessages)
                // 开战飘字：{NAME} 开始攻击 {ENEMY}
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_action_attack_start",
                    ("NAME", agent.Name.ToString()), ("INDEX", agent.Index.ToString()), ("ENEMY", _targetEnemy.Name.ToString())), Colors.Yellow));
            //AgentHudMissionView.AgentSay(agent, "别碰我的老大！");
            //玩家阵营1，自己阵营2，这里之后再看
            CombatManager.StartFight(agent, _targetEnemy,2,1);

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

            // ── 残血认输：仅当目标是玩家时 ──
            if (!_surrenderTriggered && _targetEnemy == Agent.Main)
            {
                float healthRatio = agent.Health / agent.HealthLimit;
                if (healthRatio < 0.30f)
                {
                    _surrenderTriggered = true;
                    AgentAIController.Instance?.SendEventToAgent(agent, "event_npc_surrender", Agent.Main);
                    AgentHudMissionView.AgentSay(agent,
                        // 冒泡台词：残血认输喊话
                        LWNTextHelper.ResolveText("LWN_action_surrender_bubble", "I surrender! Stop!"));
                }
            }

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
            // 完整结束战斗（注销战斗者 + 移回原始队伍）
            // UnregisterCombatant 对非玩家战斗是 no-op，SetTeam 恢复总是需要的
            CombatManager.EndFight(agent);
            _targetEnemy = null;
            // 战斗结束收武器，避免 NPC 提着刀回归巡逻
            agent.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);
            agent.TryToSheathWeaponInHand(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.Instant);
            AgentControlHelper.StopAndReset(agent); // 确保退出时清理状态
            // 战斗结束警戒值归零，避免 NPC 立刻重新进入 Alarmed → 再次质问玩家
            AgentAIController.GetBrainForAgent(agent)?.ClearAllAlerts();
            DebugLogger.Log($"[FightEnd] {agent.Name}(Idx={agent.Index}) 战斗结束，收起武器，且警戒值归零");
        }

        public bool IsFinished(Agent agent)
        {
            return _isFinished || _interrupted;
        }
    }

    public class StayAction : IAtomicAction
    {
        private Agent _lookTarget; // 要盯着谁看
        private bool _keepRotating; // 是否要时刻调整朝向
        private Vec3 stayPos;
        private float _timer;
        private bool _interrupted;
        public void RequestInterrupt() { _interrupted = true; }

        /// <summary>是否是被击晕导致的 StayAction（永久静止，直到外部解除）</summary>
        public bool IsKnockout { get; }

        public StayAction(Agent lookTarget, bool keepRotating = true, bool isKnockout = false)
        {
            _lookTarget = lookTarget;
            _keepRotating = keepRotating;
            IsKnockout = isKnockout;
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
            // 关键点：永远返回 false（除非被 RequestInterrupt 中断）
            // 除非外部调用 ClearAllActions() 或 AbortCurrentAction()，否则它永远不会自己结束
            // 这样就不会掉回 DefaultBehavior (回岗)
            return _interrupted;
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
        private bool _interrupted;
        public void RequestInterrupt() { _interrupted = true; }

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

        public bool IsFinished(Agent agent) => _isFinished || _interrupted;
    }

    /// <summary>
    /// 🆕 L3 警戒质问。
    /// 走到玩家面前后强制开启原版对话，注入 CrimeDialogueBuilder.BuildAlertInterceptScript。
    /// 对话期间持有（IsFinished=false），对话结束后由 ResetCrimeDialogueOnConversationEndPatch
    /// 清除标记 + 广播 EndInteraction → AgentBrain 标准清理路径。
    ///
    /// 注入策略：InjectScriptAsOpening — 把 NPC 台词直接挂在 start token（优先级 200），
    /// 碾压原版开场白。start 是所有 NPC 类型的通用对话入口，无需按 Hero/模板/强盗/士兵
    /// 分别适配 token。
    /// </summary>
    public class AlertForceConversationAction : IAtomicAction
    {
        /// <summary>正在等待对话结束的 NPC。Patch 在 ConversationManager.EndConversation 时读取并清理。</summary>
        internal static Agent ActiveConversationAgent;

        private bool _started;
        private bool _interrupted;
        public void RequestInterrupt() { _interrupted = true; }

        /// <summary>显式质问上下文覆盖（嫌犯逃跑围堵等场景，NPC 自身无警戒明细可推导时传入）。null = 按 Brain.PrimaryAction 推导。</summary>
        private readonly ConfrontationType? _detailOverride;
        private readonly PlayerActionType? _actionOverride;

        public AlertForceConversationAction(ConfrontationType? detailOverride = null, PlayerActionType? actionOverride = null)
        {
            _detailOverride = detailOverride;
            _actionOverride = actionOverride;
        }

        public void OnStart(Agent agent)
        {
            _started = false;

            if (Agent.Main == null || !agent.IsActive())
            {
                DebugLogger.Log($"[AlertForceConv] {agent.Name}(Idx={agent.Index}) 启动失败: Agent.Main={Agent.Main != null}, IsActive={agent.IsActive()}");
                return;
            }

            var npcHero = (agent.Character as CharacterObject)?.HeroObject;
            string npcDesc = npcHero != null ? $"Hero={npcHero.Name}" : $"模板NPC({agent.Name})"; // lwn-ignore: A (debug)
            DebugLogger.Log($"[AlertForceConv] {agent.Name}(Idx={agent.Index}) {npcDesc} — 使用 InjectScriptAsOpening (start token)");

            var brain = AgentAIController.GetBrainForAgent(agent);
            PlayerActionType? primaryAction = brain?.PrimaryAction;

            // 根据 PrimaryAction 确定 ConfrontationType detail；显式覆盖优先（嫌犯逃跑围堵等无警戒明细场景）
            var detail = _detailOverride ?? (primaryAction switch
            {
                PlayerActionType.Crouching or PlayerActionType.WeaponDrawn => ConfrontationType.Deter,
                PlayerActionType.StealUIOpen => ConfrontationType.Search,
                PlayerActionType.Steal => StealManager.HasStolenItemsFrom(agent)
                    ? ConfrontationType.Recover
                    : ConfrontationType.Deter,
                PlayerActionType.AttackAlly or PlayerActionType.Knockout => ConfrontationType.Stop,
                PlayerActionType.SuspectFlee => ConfrontationType.Stop,
                _ => ConfrontationType.Deter
            });
            brain?.SetNpcIntent(NpcIntentType.Confronting, Agent.Main, interceptDetail: detail);

            // 设 trigger：TryInjectCrimeDialogue（StartConversation Prefix/Postfix）统一构建并注入脚本
            ConversationEntryPatch._pendingTrigger = DialogueTrigger.Alert;
            ConversationEntryPatch._pendingConfrontation = detail;
            ConversationEntryPatch._pendingTriggerAction = _actionOverride ?? primaryAction ?? PlayerActionType.Crouching;

            // 强制开启原版对话
            try
            {
                var conversationLogic = Mission.Current?.GetMissionBehavior<MissionConversationLogic>();
                if (conversationLogic != null)
                {
                    // ★ 必须在 StartConversation 之前设置 ActiveConversationAgent，
                    // 因为 StartConversation → Harmony Prefix 立即需要知道谁是真正的对话对象。
                    // 若等到 StartConversation 返回后才设，Prefix 只能看到
                    // MissionConversationLogic.ConversationAgent 的过期值（如刚被打晕的 NPC）。
                    ActiveConversationAgent = agent;
                    DebugLogger.Log($"[AlertForceConv] {agent.Name}(Idx={agent.Index}) ActiveConversationAgent 设置成功");
                    conversationLogic.StartConversation(agent, true, false);
                    _started = true;
                    DebugLogger.Log($"[AlertForceConv] {agent.Name}(Idx={agent.Index}) 对话启动成功");
                }
                else
                {
                    DebugLogger.Log($"[AlertForceConv] {agent.Name}(Idx={agent.Index}) 启动失败: MissionConversationLogic=null");
                    AgentHudMissionView.AgentSay(agent,
                        // 冒泡台词：强制对话启动失败时喊住玩家
                        LWNTextHelper.ResolveText("LWN_action_alert_force_callout", "Hey! You there!"));
                }
            }
            catch (Exception ex)
            {
                ActiveConversationAgent = null; // 启动失败 → 清理，防止残留
                DebugLogger.Log($"[AlertForceConv] {agent.Name}(Idx={agent.Index}) 启动异常: {ex.Message} ActiveConversationAgent 变成null");
                AgentHudMissionView.AgentSay(agent,
                    // 冒泡台词：强制对话启动失败时喊住玩家
                    LWNTextHelper.ResolveText("LWN_action_alert_force_callout", "Hey! You there!"));
            }
        }

        public void OnTick(Agent agent, float dt) { }

        /// <summary>
        /// 对话进行中 → 持有（false），防止 NPC 掉回原版 AI。
        /// 对话结束后 Patch 清除 ActiveConversationAgent → IsFinished=true → OnEnd 清标志。
        /// 启动失败（_started=false）→ 立即完成。
        /// </summary>
        public bool IsFinished(Agent agent)
        {
            if (_interrupted) return true;
            if (!_started) return true;
            return ActiveConversationAgent != agent;
        }

        public void OnEnd(Agent agent)
        {
            if (_started)
            {
                // Patch 已广播 EndInteraction → ClearAllActions 会调到这。
                // 这里只清理残留状态，不再重复广播。

                if (ActiveConversationAgent == agent)
                {
                    DebugLogger.Log($"[AlertForceConv] {agent.Name}(Idx={agent.Index}) ActiveConversationAgent OnEnd清理成功");
                    ActiveConversationAgent = null;
                }
            }
        }
    }
}