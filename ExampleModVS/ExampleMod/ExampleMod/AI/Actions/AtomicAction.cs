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

        /// <summary>
        /// 经历旁白（2026-08-11）：出队翻译（AgentBrain.RecordActionNarration 调用）。
        /// **每个动作在自身定义处声明**——值得记住的经历（"我遭到X的攻击，与他交战"/"吓得逃走了"）
        /// 返回第一人称文本（LLM prompt 材料，豁免铁律 13）；机械动作返回 null = 不记录（零噪声）。
        /// owner = 正在执行本动作的 Agent；需要被攻击语境时经 AgentAIController.GetBrainForAgent(owner)
        /// 调 ConsumeAttackContext()（消费式，一次交战只补一次语境）。
        /// </summary>
        string GetNarration(Agent owner);
    }
    // 这个Action负责"点火"，即启动LLM生成任务
    public class PrepareOpeningAction : IAtomicAction
    {
        /// <summary>机械/台词类动作：不产生旁白（内容进对话历史）。</summary>
        public string GetNarration(Agent owner) => null;

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
        /// <summary>机械/台词类动作：不产生旁白（内容进对话历史）。</summary>
        public string GetNarration(Agent owner) => null;

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
        /// <summary>机械/台词类动作：不产生旁白（后续交战记录覆盖）。</summary>
        public string GetNarration(Agent owner) => null;

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
        /// <summary>机械动作：不产生旁白。</summary>
        public string GetNarration(Agent owner) => null;

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
    // 🔴 2026-08-11 参数化合并：原 FleeFromAction（儿童逃跑）/ ReactiveFleeAction（恐慌逃跑）/
    // ReactiveReturnPostAction（回岗）并入本类——差异全部参数化（起身延迟/固定超时/收尾行为/旁白），
    // 完成判定 = 到点 + 超时/卡死兜底，移动逻辑只剩一份，改移动节奏只改一个类。
    public class MoveToPositionAction : IAtomicAction
    {
        /// <summary>移动结束行为：InteractPrepare = 到位后准备互动（精确移动/赴约——对峙/对话场景）；
        /// Unlock = 解锁恢复原版 AI（逃跑/回岗——逃完就该回归日常，不该准备互动）。</summary>
        public enum EndBehavior { InteractPrepare, Unlock }

        /// <summary>经历旁白：逃跑模式传"吓得逃走了"（值得记的经历）；机械移动传 null（零噪声）。</summary>
        private readonly string _narration;
        public string GetNarration(Agent owner) => _narration;

        private Vec3 _targetPos;
        private Vec2 _targetDir;
        private bool _run;
        private float _stopDistance;
        private float _timer;
        private float _maxTime;           // 卡死预算（按距离/速度算，不固定）
        private readonly float _maxTimeOverride;   // 固定超时（>0 = 到时即完成，不瞬移——恐慌/逃跑"放弃"语义）
        private readonly bool _skipGetupDelay;     // 跳过起身延迟（恐慌/逃跑：坐蹲躺也立即动）
        private readonly Agent _lookTarget;        // 边走边盯的目标（调查场景；null = 不盯）
        private readonly EndBehavior _endBehavior;
        private float _moveStartTimer;             // 开始移动时刻（_timer 基准；起身预支 2s 不计入超时）
        private float fixedTimer = 0;
        private float _sampleTimer;       // 进度采样计时
        private float _lastDist;          // 上次采样距目标距离
        private float _lastProgressTime;  // 最近一次"有进展"时刻
        private bool _teleportOnEnd;      // 仅卡死兜底才瞬移（正常到达不瞬移）
        private bool _interrupted;
        public void RequestInterrupt() { _interrupted = true; }

        /// <param name="pos">目标点</param>
        /// <param name="dir">到位朝向（逃跑可传 Vec2.Zero）</param>
        /// <param name="run">跑/走</param>
        /// <param name="stopDistance">完成距离</param>
        /// <param name="maxTime">固定超时（秒）；≤0 = 用动态卡死预算（精确移动兜底）</param>
        /// <param name="skipGetupDelay">跳过起身延迟（恐慌/逃跑：立刻动，不等过渡动画）</param>
        /// <param name="endBehavior">收尾行为（精确移动 InteractPrepare / 逃跑回岗 Unlock）</param>
        /// <param name="narration">经历旁白（null = 不记录）</param>
        /// <param name="lookTarget">边走边盯的目标（调查场景；null = 不盯）</param>
        public MoveToPositionAction(Vec3 pos, Vec2 dir, bool run = false, float stopDistance = 1.0f,
            float maxTime = 0f, bool skipGetupDelay = false,
            EndBehavior endBehavior = EndBehavior.InteractPrepare, string narration = null,
            Agent lookTarget = null)
        {
            _targetPos = pos;
            _targetDir = dir;
            _run = run;
            _stopDistance = stopDistance;
            _maxTimeOverride = maxTime;
            _skipGetupDelay = skipGetupDelay;
            _endBehavior = endBehavior;
            _narration = narration;
            _lookTarget = lookTarget;

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

            // 调查场景：边走边盯目标（原 ReactiveInvestigateAction 语义，2026-08-11 并入）
            if (_lookTarget != null && _lookTarget.IsActive())
                AgentControlHelper.LookAtAgent(agent, _lookTarget);

            // 恐慌/逃跑（skipGetupDelay）：坐蹲躺也立即动；否则按现状（不需要过渡动画 → 提前起身）
            if (!needsDelay || _skipGetupDelay)
                _timer = 2.0f;
            _moveStartTimer = _timer;   // 起身预支不计入超时预算（基准 = 实际开始移动时刻）

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
            // 固定超时模式（恐慌/逃跑）：到点或到时二选一，到时即完成且不瞬移——逃跑语义是"放弃"不是"到位"。
            // 超时从开始移动算（2026-08-11 修正：起身预支 2s 不计入，10s 预算 = 实际走 10s）
            if (_maxTimeOverride > 0f && _timer - _moveStartTimer > _maxTimeOverride) return true;
            // 卡死判定（精确移动）：超预算 且 最近 3s 无进展 → 瞬移兜底；仍在走（有速度）→ 继续
            if (_timer - _moveStartTimer > _maxTime && _timer - _lastProgressTime > 3f)
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
            // 朝向守卫：逃跑/回岗传 Vec2.Zero（无意义方向）→ 跳过，避免零向量下发给引擎（解锁后原版 AI 接管）
            if (_targetDir.LengthSquared > 0.01f)
                agent.SetMovementDirection(_targetDir);

            // 收尾行为参数化：精确移动 = 到位准备互动（对峙/对话）；逃跑/回岗 = 解锁恢复原版 AI
            if (_endBehavior == EndBehavior.InteractPrepare)
                AgentControlHelper.MoveEndAndInteractPrepare(agent);
            else
                AgentControlHelper.ForceUnlockAgent(agent);
        }

        /// <summary>逃跑工厂（原 FleeFromAction，2026-08-11 并入）：远离威胁 ±45° 抖动 8~14m，
        /// 取第一个 navmesh 有效点；找不到 → 直线方向 8m 兜底（引擎自动修正 navmesh）。
        /// isRun=false = walk（as_human_child 无 run 动画）；maxTime 固定超时（默认 10s，到时即完成不瞬移）。</summary>
        public static MoveToPositionAction FleeFrom(Agent agent, Agent threat, bool isRun = false, float maxTime = 10f)
        {
            // 逃跑方向：远离威胁 ±45° 抖动，8~14m，取第一个 navmesh 有效点（照动物挣脱轮子 OnAnimalStruggleFlee）
            Vec3 away = threat != null && threat.IsActive()
                ? agent.Position - threat.Position
                : new Vec3(1f, 0f, 0f);
            away.z = 0f;
            if (away.LengthSquared < 0.001f) away = new Vec3(1f, 0f, 0f);
            away = away.NormalizedCopy();

            Vec3 fleePos = agent.Position + away * 8f;
            for (int i = 0; i < 6; i++)
            {
                float angle = (MBRandom.RandomFloat - 0.5f) * MathF.PI * 0.5f; // ±45°
                float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
                Vec3 dir = new Vec3(away.x * cos - away.y * sin, away.x * sin + away.y * cos, 0f);
                Vec3 candidate = agent.Position + dir * (8f + MBRandom.RandomFloat * 6f);
                if (agent.Mission?.Scene != null && V.NavMesh(agent.Mission.Scene, candidate, out _))
                {
                    fleePos = candidate;
                    break;
                }
            }
            return new MoveToPositionAction(fleePos, Vec2.Zero, isRun, stopDistance: 1f,
                maxTime: maxTime, skipGetupDelay: true, endBehavior: EndBehavior.Unlock,
                narration: "吓得逃走了");
        }
    }

    // 2.5 逃离动作：已并入 MoveToPositionAction.FleeFrom 工厂（2026-08-11，参数化合并）——
    // 儿童逃跑/恐慌逃跑共用"逃跑"语义（目标源/姿势/超时参数区分），见 MoveToPositionAction.FleeFrom。

    // 2. 跟随/追逐目标动作
    public class FollowAgentAction : IAtomicAction
    {
        /// <summary>持续状态动作：不产生旁白（跟随是状态不是事件）。</summary>
        public string GetNarration(Agent owner) => null;

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

        private readonly float _optionalDuration;   // 跟走模式时长（>0 = 到时 IsFinished，忽略距离判定）
        private float _moveStartTimer;              // 开始移动时刻（_timer 基准；起身预支 2s 不计入时长）

        public FollowAgentAction(Agent target, bool run, float radius = 0.0f, float angleOffset = 0f, float stopDistance = 3.5f, float buffer = 1.5f, bool keepFollow = false, float optionalDuration = 0f)
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

            // 🔴 跟走模式（附章③，2026-08-11）：optionalDuration > 0 = 跟走一段后到时完成
            // （原 ReactiveFollowAction 的 Follow 阶段——一直追目标，不因距离完成）
            _optionalDuration = optionalDuration;

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
            _moveStartTimer = _timer;   // 起身预支不计入跟走时长/超时预算（基准 = 实际开始移动时刻）
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

            // 🔴 追赶瞬移仅限持续跟随（keepFollow=true 抑制以外）——跟走模式（optionalDuration > 0）
            // 禁止瞬移：跟走语义 = "跟不上就落后，到时自然结束"（原 ReactiveFollowAction 无瞬移，
            // 2026-08-11 并入后若不抑制，玩家跑/骑马跑远 → 守卫 5s 后穿墙瞬移——可见 bug）
            if (_timer - _moveStartTimer > _maxTime && _currentDistanceSq > _stopDistanceSq
                && _optionalDuration <= 0f)
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
                    // 🔴 2026-08-10 动态重算间隔（im-command-action-upgrade.md §5.4）：
                    // 旧实现每 0.2s 无条件重算理想点 + 重发寻路（ScriptedMoveToPoint = SetScriptedPosition
                    // native 全量路径重发）——远距目标动 1m 对百米外执行者毫无意义，纯浪费；
                    // 近距离快速目标（0.2s 间隔）又可能跟不上冲刺/骑马。
                    // 动态间隔 = 目标速度（AverageVelocity 内建平均窗口，防瞬时抖动）+ 距离双因子：
                    // 越远间隔越大（远距微小位移无意义）、越快间隔越小（目标点变化快）。
                    // 上限 = 心跳：目标不可达（跳崖/绕路/卡墙）仍周期性自愈纠偏，不永久走错方向。
                    if (_fixedTimer > ComputeRepathInterval())
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

        /// <summary>
        /// 🔴 2026-08-10 动态重算间隔（§5.4）：目标平面速度 + 欧氏直线距离双因子，C# 确定性。
        /// interval = 0.15 * (1 + dist/10) / max(targetSpeed/2.5, 0.25)，clamp [FollowRepathMin, FollowRepathMax]。
        /// 距离因子：越远间隔越大（远距微小位移无意义）；速度因子：越快间隔越小（目标点变化快）。
        /// 速度用 AverageVelocity（native 平均窗口）不用 MovementVelocity（瞬时抖动会让间隔乱跳）。
        /// 直线距离 ≈ 寻路长度×曲折系数，公式只做量级分级，够用。
        /// </summary>
        private float ComputeRepathInterval()
        {
            try
            {
                float targetSpeed = new Vec2(_target.AverageVelocity.x, _target.AverageVelocity.y).Length;
                float dist = MathF.Sqrt(_currentDistanceSq);
                float min = Settings.Instance.FollowRepathMin;
                float max = Settings.Instance.FollowRepathMax;
                float interval = 0.15f * (1f + dist / 10f) / MathF.Max(targetSpeed / 2.5f, 0.25f);
                return MathF.Clamp(interval, min, max);
            }
            catch
            {
                return 0.15f;   // 解析失败 → 回到灵敏下限（行为不劣化）
            }
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
            // 🔴 跟走模式（optionalDuration > 0）：只按时长完成（目标消失已在上方拦截），
            // 忽略距离判定——跟走语义 = 一直追目标直到时间到（原 ReactiveFollowAction Follow 阶段）。
            // 时长从开始移动算（起身预支不计入，坐蹲躺的起身期不吞跟走时间）
            if (_optionalDuration > 0f) return _timer - _moveStartTimer >= _optionalDuration;
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
        /// <summary>机械动作：不产生旁白。</summary>
        public string GetNarration(Agent owner) => null;

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
        /// <summary>机械动作：不产生旁白。</summary>
        public string GetNarration(Agent owner) => null;

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
        /// <summary>经历旁白（2026-08-11）：交战开始。"被攻击"由事件层记录（event_agent_damaged 事件事实）。</summary>
        public string GetNarration(Agent owner)
        {
            string targetName = _targetEnemy?.Name?.ToString() ?? "对手";
            return $"与{targetName}交战";
        }

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
            // 🔴 2026-08-11：玩家参与且分出胜负的战斗 → ①当事人动态记忆（NPC 知道"和谁打、谁赢"，
            // 后续 IM/当面对话的【近期回忆】接得住，不再瞎编结果）②ImEventBroadcaster 队伍广播
            // （框架复用：群聊议论 + 参与度记忆 + 接话，custom.im_test_event 同入口）。
            RecordFightResultIfPlayerInvolved(agent);
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

        /// <summary>
        /// 🔴 2026-08-11：玩家参与的定局战斗结果记录。胜负判定：一方倒下（不活跃或 HP≤0）即定局；
        /// 双方都站着（打断/撤退/目标变更）→ 不记录。
        /// 覆盖范围说明：本动作由 NPC Brain 执行，AgentAIController 只 tick 活跃 Owner
        /// （AgentAIController.cs:217）——执行者倒下时 OnEnd 不触发，"玩家胜、执行者败"的
        /// 输家记忆/胜利广播天然缺位（切磋胜方是玩家时玩家自己知道结果，可接受，不作补救）。
        /// </summary>
        private void RecordFightResultIfPlayerInvolved(Agent agent)
        {
            try
            {
                if (_targetEnemy == null || _targetEnemy != Agent.Main) return;   // 只处理玩家参与的战斗
                if (Hero.MainHero == null) return;
                bool targetDown = !_targetEnemy.IsActive() || _targetEnemy.Health <= 0;
                bool selfDown = !agent.IsActive() || agent.Health <= 0;
                if (targetDown == selfDown) return;                               // 未分胜负

                bool executorWon = targetDown;
                string playerName = Hero.MainHero.Name?.ToString() ?? "主公";
                // ① 当事人确定性记忆（第一人称，LLM prompt 材料；走【近期回忆】不污染私聊 UI）
                var hero = (agent.Character as CharacterObject)?.HeroObject;
                if (hero != null)
                    AllNpcMemoryManager.GetMemory(hero.StringId)?.RecordDynamicMemory(
                        executorWon ? $"刚与{playerName}交手，我赢了。" : $"刚与{playerName}交手，我输了。");
                // ② 队伍感知（ImEventBroadcaster 框架复用）：玩家败 → battle_lose / 玩家胜 → battle_win；
                //    描述带对手名让 LLM 评论更具体（如"主公与阿速甘切磋落败"）
                ImEventBroadcaster.BroadcastPlayerEvent(executorWon ? "battle_lose" : "battle_win",
                    executorWon
                        ? $"主公方才与{agent.Name}交手，落败被打晕了过去"
                        : $"主公方才与{agent.Name}交手，占了上风");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[FightResult] 记录失败: {ex.Message}");
            }
        }

        public bool IsFinished(Agent agent)
        {
            return _isFinished || _interrupted;
        }
    }

    public class StayAction : IAtomicAction
    {
        /// <summary>机械动作（含击晕占位）：不产生旁白（击晕本身由事件记录）。</summary>
        public string GetNarration(Agent owner) => null;

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
        /// <summary>内部决策动作：不产生旁白。</summary>
        public string GetNarration(Agent owner) => null;

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
        /// <summary>台词/对话类动作：不产生旁白（质问内容进对话历史）。</summary>
        public string GetNarration(Agent owner) => null;

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

    // ═══════════════════════════════════════════════════════════════
    // 反应链动作（ReactiveAgent 反应系统使用，原定义于 ReactiveAgent.cs，
    // 2026-08-11 迁移统一——所有 IAtomicAction 实现集中在本文件）
    // ═══════════════════════════════════════════════════════════════

    /// <summary>反应台词：冒泡一句话后结束（refuse/ignore/warn_away 用）。</summary>
    public class ReactiveSayAction : IAtomicAction
    {
        /// <summary>台词类动作：不产生旁白（内容进对话历史）。</summary>
        public string GetNarration(Agent owner) => null;

        private readonly Agent _agent;
        private readonly string _text;
        private readonly float _duration;
        private float _timer;
        private bool _interrupted;
        public void RequestInterrupt() { _interrupted = true; }

        public ReactiveSayAction(Agent agent, string text, float duration = 2.5f)
        {
            _agent = agent;
            _text = text;
            _duration = duration;
        }

        public void OnStart(Agent agent)
        {
            if (!string.IsNullOrEmpty(_text))
                AgentHudMissionView.AgentSay(agent, _text);
        }

        public void OnTick(Agent agent, float dt)
        {
            _timer += dt;
        }

        public bool IsFinished(Agent agent) => _interrupted || _timer >= _duration;

        public void OnEnd(Agent agent) { }
    }

    // ═══════════════════════════════════════════════════════════════
    // 行为性内联队列适配器（单脑化重构 M0/D3，原 ExecutePlanAction 已于 D1 删除）
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 行为性内联步骤队列适配器（D3）：把内联状态机（lead/steal_attempt/knockout/emote）包成
    /// IAtomicAction 入队，使中断语义覆盖到它们——OnStart/OnEnd 空实现（状态机构造时已初始化、
    /// teardown 无资源需释放）；完成判定 = 状态机 Finished/Interrupted；RequestInterrupt 传导到
    /// 状态机 Interrupt()（中断标记使 OnTick 直接结束、不会真执行——无僵尸动作）。
    /// </summary>
    public class InlinePlanAction : IAtomicAction
    {
        /// <summary>内部驱动动作：不产生旁白（行为性内联的经历由自身步骤结算记录，与重构前一致）。</summary>
        public string GetNarration(Agent owner) => null;

        private readonly IInlineStep _inline;
        public IInlineStep Inline => _inline;

        public InlinePlanAction(IInlineStep inline)
        {
            _inline = inline;
        }

        public void OnStart(Agent a) { }                                  // 状态机构造时已初始化
        public void OnTick(Agent a, float dt) => _inline.OnTick(dt);
        public bool IsFinished(Agent a) => _inline.Finished || _inline.Interrupted;
        public void OnEnd(Agent a) { }
        public void RequestInterrupt() => _inline.Interrupt();
    }
}