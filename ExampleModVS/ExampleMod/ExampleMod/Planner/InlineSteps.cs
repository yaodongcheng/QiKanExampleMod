using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════
    // InlineSteps.cs — 执行器内联步骤（不经过 IAtomicAction 队列的步骤）
    //
    // 设计：这些步骤的推进逻辑属于执行器（计时/广播/结算），
    // 不适合塞进原子行为库；每个步骤一个轻量状态机，由 ActorCursor 驱动。
    // 与 IAtomicAction 的界限：原子行为 = 可复用的引擎级动作；
    //              内联步骤 = 计划语法相关的编排逻辑。
    // ═══════════════════════════════════════════════════════════════

    public interface IInlineStep
    {
        bool Ok { get; }        // 创建成功（目标可解析等）
        bool Finished { get; }  // 本步完成
        void OnTick(float dt);
    }

    /// <summary>say_to：单句模式（text，现状）/ 对话模式（outline + topic，多轮 LLM 实时对话）。
    /// 🔴 2026-08-11（§5.6 四槽位体系）：对话模式状态机平移为 SayToSlot（DialogueComponent）——
    /// 本类变薄壳：单句模式保留原逻辑，对话模式委托 SayToSlot.Tick（BC-006 行为等价）。</summary>
    public class SayInlineState : IInlineStep
    {
        private readonly Agent _agent;
        private readonly Agent _target;
        private readonly PlanStep _step;
        private readonly PlanExecutor _executor;
        private readonly SayToSlot _chatSlot;   // 🔴 对话模式插槽（2026-08-11：ChatPhase 状态机平移）
        private float _timer;
        private float _duration;
        private bool _said;
        private bool _broadcastDone;
        public bool Ok { get; private set; }
        public bool Finished { get; private set; }

        public SayInlineState(PlanExecutor executor, ActorCursor cursor, PlanStep step)
        {
            _executor = executor;
            _step = step;
            _agent = cursor.Agent;
            _target = null;
            string refName = PlanRefUtil.Normalize(step.Target, out string query);
            if (query != null) refName = query;
            if (string.IsNullOrEmpty(refName) || !executor.World.TryResolveAgent(refName, cursor.Agent, out _target))
            {
                Ok = false;
                return;
            }
            Ok = true;
            // 🔴 对话模式（outline 2+ 段）→ SayToSlot 插槽（状态机平移，行为等价）；单句模式保留原逻辑
            if (step.IsChatMode)
            {
                _chatSlot = new SayToSlot(executor, cursor, step);
                if (_chatSlot.Finished) { Finished = true; return; }
                return;
            }
            // 单句模式（现状不变）：冒泡时长按文本长度估算（"N 秒内必须播完"兜底由步骤 timeout 负责）
            float len = string.IsNullOrEmpty(step.TextOrContent) ? 0 : step.TextOrContent.Length;
            _duration = MathF.Min(MathF.Max(2f + len * 0.12f, 2f), step.TimeoutS > 0 ? step.TimeoutS : 6f);
        }

        public void OnTick(float dt)
        {
            if (Finished || !Ok) return;
            // 🔴 对话模式 → SayToSlot 插槽驱动（2026-08-11 架构收敛：统一 DialogueSession，每帧调 Slot.OnTick）
            if (_chatSlot != null)
            {
                var session = _chatSlot.Session;
                if (session != null && session.Slot != null)
                    session.Slot.OnTick(session, dt);
                if (_chatSlot.Finished)
                    Finished = true;   // 行为等价：对话模式完成仅置 Finished，执行器 CompleteStep 推进
                return;
            }
            // ── 单句模式（现状）──
            _timer += dt;
            if (!_said)
            {
                _said = true;
                try
                {
                    if (_target != null && _target.IsActive())
                        AgentControlHelper.FaceToActor(_agent, _target);
                    if (!string.IsNullOrEmpty(_step.TextOrContent))
                        AgentHudMissionView.AgentSay(_agent, _step.TextOrContent);
                }
                catch { }
            }
            if (_timer >= _duration)
            {
                if (!_broadcastDone)
                {
                    _broadcastDone = true;
                    BroadcastSpokenTo(_step.TextOrContent, null);
                    if (_executor != null)
                        _executor.NotifySayDone(_step, _target);
                }
                Finished = true;
            }
        }

        /// <summary>广播 spoken_to（Args[1]=随从台词、Args[2]=主题、Args[3]=当前走向段）；ask:follow 照旧。
        /// 🔴 §5.6（2026-08-10）：说话广播收敛到 DialogueComponent.HandleDialogue（含旁观者 seen_speaking 插话广播）。</summary>
        private void BroadcastSpokenTo(string line, string outlineStep)
        {
            // ask: follow → 广播 asked_to_follow(target)（ReactiveAgent"跟不跟"演算的触发词）
            if (!string.IsNullOrEmpty(_step.Ask) && _step.Ask == "follow" && _target != null)
                AgentAIController.Instance?.SendEventToAgent(_target, "asked_to_follow", _agent);
            // 通用 spoken_to 广播 + 旁观者 seen_speaking（统一入口；Args[2]=主题（对话模式用 topic，否则计划摘要））
            if (_target != null)
            {
                string topic = !string.IsNullOrEmpty(_step.Topic) ? _step.Topic : _executor?.Summary;
                DialogueComponent.HandleDialogue(_agent, _target, topic, line, outlineStep);
            }
        }
    }

    /// <summary>wait：seconds（纯等待）/ until（等世界状态，由游标检查）/ 两者皆省 = 无限保持。</summary>
    public class WaitInlineState : IInlineStep
    {
        private readonly PlanStep _step;
        private float _timer;
        public bool Ok { get; private set; } = true;
        public bool Finished { get; private set; }

        public WaitInlineState(PlanStep step)
        {
            _step = step;
        }

        public void OnTick(float dt)
        {
            if (Finished) return;
            if (_step.Seconds > 0f)
            {
                _timer += dt;
                if (_timer >= _step.Seconds) Finished = true;
            }
            // until 由游标在 TickCursor 中统一检查；两者皆省 = 无限（结束 = R3 停止键）
        }
    }

    /// <summary>signal_player：密信报告（非模态即时信号）。</summary>
    public class SignalInlineState : IInlineStep
    {
        private readonly PlanExecutor _executor;
        private readonly PlanStep _step;
        private bool _sent;
        public bool Ok { get; private set; } = true;
        public bool Finished { get; private set; }

        public SignalInlineState(PlanExecutor executor, PlanStep step)
        {
            _executor = executor;
            _step = step;
        }

        public void OnTick(float dt)
        {
            if (Finished) return;
            if (!_sent)
            {
                _sent = true;
                _executor?.SignalPlayer(_step.Text ?? "");
            }
            Finished = true;
        }
    }

    /// <summary>end_plan：收尾三路（result + report）。</summary>
    public class EndPlanInlineState : IInlineStep
    {
        private readonly PlanExecutor _executor;
        private readonly PlanStep _step;
        private bool _applied;
        public bool Ok { get; private set; } = true;
        public bool Finished { get; private set; }

        public EndPlanInlineState(PlanExecutor executor, PlanStep step)
        {
            _executor = executor;
            _step = step;
        }

        public void OnTick(float dt)
        {
            if (Finished) return;
            if (!_applied)
            {
                _applied = true;
                string result = _step.ResultString ?? "fail";
                _executor?.ApplyEndPlan(_step, result);
            }
            Finished = true;
        }
    }

    /// <summary>emote：语义标签 → 引擎动画（M5 验证动画表；失败降级无动作，不崩不穿模）。</summary>
    public class EmoteInlineState : IInlineStep
    {
        private static readonly System.Collections.Generic.Dictionary<string, string> AnimMap =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "nod", "act_agree_1" },
                { "shake", "act_disagree_1" },
                { "wave", "act_wave_1" },
                { "cheer", "act_cheer_1" },
                { "bow", "act_bow_1" },
                { "shrug", "act_shrug_1" },
                { "point", "act_point_1" },
                { "threaten", "act_threaten_1" },
                { "disappointed", "act_defeat_1" },
            };

        private readonly Agent _agent;
        private float _timer;
        private bool _played;
        public bool Ok { get; private set; }
        public bool Finished { get; private set; }

        public EmoteInlineState(Agent agent, PlanStep step)
        {
            _agent = agent;
            if (string.IsNullOrEmpty(step.Text) || !AnimMap.TryGetValue(step.Text, out string anim))
            {
                // 未知语义标签 → 降级无动作（装饰步骤不改世界状态，§4 emote 行）
                Ok = true;
                Finished = true;
                return;
            }
            Ok = true;
            try
            {
                AgentControlHelper.SetPose(agent, anim);
                _played = true;
            }
            catch
            {
                // 播放失败 → 降级无动作（不崩、不穿模）
                Ok = true;
                Finished = true;
            }
        }

        public void OnTick(float dt)
        {
            if (Finished) return;
            _timer += dt;
            if (!_played) { Finished = true; return; }
            // 动画播完（不再处于该 pose）或 2s 兜底
            if (_timer >= 2f)
            {
                try { AgentControlHelper.StopAndReset(_agent); } catch { }
                Finished = true;
            }
        }
    }

    /// <summary>make_noise：喊叫 + 复用 WitnessCrime 围观聚集（criminal=随从 = 纯围观无犯罪副作用，§4）。</summary>
    public class MakeNoiseInlineState : IInlineStep
    {
        private readonly Agent _agent;
        private float _timer;
        public bool Ok { get; private set; } = true;
        public bool Finished { get; private set; }

        public MakeNoiseInlineState(PlanExecutor executor, ActorCursor cursor)
        {
            _agent = cursor.Agent;
            try
            {
                // 本地化：喊叫台词（COMMOTION 引众围观）
                AgentHudMissionView.AgentSay(_agent, LWNTextHelper.ResolveText("LWN_plan_noise", "Hey! Over here! Look at this!"));
                // 围观聚集：WitnessCrime 事件（criminal=self 非玩家 → 不加犯罪脉冲，纯围观）
                AgentAIController.Instance?.BroadcastEventInRange(
                    _agent.Position, 20f, "WitnessCrime",
                    exclude: null, requireSight: false,
                    _agent, null);
                DebugLogger.Log($"[PlanExecutor] make_noise: {_agent.Name} 引起围观");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PlanExecutor] make_noise 失败: {ex.Message}");
            }
        }

        public void OnTick(float dt)
        {
            if (Finished) return;
            _timer += dt;
            if (_timer >= 2f) Finished = true;
        }
    }

    /// <summary>lead：带路（GUIDE）——朝目的地前进 + 定期回望（节奏同步在原子行为内部）。</summary>
    public class LeadInlineState : IInlineStep
    {
        private readonly Agent _agent;
        private readonly Vec3 _destination;
        private readonly PlanExecutor _executor;
        private enum Phase { Moving, Waiting }
        private Phase _phase = Phase.Moving;
        private float _fixedTimer;
        private float _waitTimer;
        private bool _finishedFlag;
        public bool Ok { get; private set; }
        public bool Finished => _finishedFlag;

        public LeadInlineState(PlanExecutor executor, ActorCursor cursor, PlanStep step)
        {
            _executor = executor;
            _agent = cursor.Agent;
            string refName = PlanRefUtil.Normalize(step.Target, out string query);
            if (query != null) refName = query;
            if (string.IsNullOrEmpty(refName) || !executor.World.TryResolvePosition(refName, cursor.Agent, out _destination))
            {
                Ok = false;
                return;
            }
            Ok = true;
        }

        public void OnTick(float dt)
        {
            if (Finished) return;
            var player = Agent.Main;
            if (_agent == null || !_agent.IsActive()) { _finishedFlag = true; return; }

            _fixedTimer += dt;
            switch (_phase)
            {
                case Phase.Moving:
                    // 前进（每 200ms 刷新目标点）
                    if (_fixedTimer >= 0.2f)
                    {
                        _fixedTimer = 0f;
                        try { AgentControlHelper.ScriptedMoveToPoint(_agent, _destination, false); } catch { }
                    }
                    // 到达目的地（GOAL：玩家到达）
                    if (player != null && player.IsActive() && player.Position.Distance(_destination) < 3f)
                    {
                        _finishedFlag = true;
                        return;
                    }
                    // 玩家跟丢（> 8m）→ 停下等
                    if (player != null && player.IsActive() && _agent.Position.Distance(player.Position) > 8f)
                    {
                        _phase = Phase.Waiting;
                        _waitTimer = 0f;
                        try { AgentControlHelper.FaceToActor(_agent, player); } catch { }
                    }
                    break;

                case Phase.Waiting:
                    _waitTimer += dt;
                    // 玩家跟上 → 继续
                    if (player != null && player.IsActive() && _agent.Position.Distance(player.Position) < 3f)
                    {
                        _phase = Phase.Moving;
                        _fixedTimer = 0f;
                        return;
                    }
                    // 等待超时 → 当面报告"你走不走啊" → 中止
                    if (_waitTimer > 15f)
                    {
                        _executor?.AbortWithReport(PlanTexts.LeadWaiting);
                        _finishedFlag = true;
                    }
                    break;
            }
        }
    }

    /// <summary>steal_attempt：NPC 侧偷窃原子（§4 两个变体 + §4.1 责权归属）。
    /// 物变体（箱子）：接近 → 蹲下 + Intent 显示 → 成功率公式 → 得手/摸空；目击 → 中断/问责玩家。
    /// 人变体（扒窃）：绕背定位（目标背后盲区 + 无目击者）→ 公式 → 从目标钱包守恒转移；无钱包 → 诚实 impossible。</summary>
    public class StealAttemptInlineState : IInlineStep
    {
        private enum AttemptPhase { Approach, Behind, Rolling, Settled }
        private AttemptPhase _phase = AttemptPhase.Approach;
        private float _timer;
        private string _resultKey;    // success / empty / impossible / interrupted
        private readonly PlanExecutor _executor;
        private readonly Agent _agent;
        private readonly PlanStep _step;
        private readonly bool _variantItem;
        private readonly Random _rng = new Random();
        private float _amount;
        public bool Ok { get; private set; }
        public bool Finished { get; private set; }

        public StealAttemptInlineState(PlanExecutor executor, ActorCursor cursor, PlanStep step)
        {
            _executor = executor;
            _agent = cursor.Agent;
            _step = step;
            _variantItem = step.Variant == "item";
            // 目标解析（物 = 箱子物件；人 = 扒窃目标）
            string refName = PlanRefUtil.Normalize(step.Target, out string query);
            if (query != null) refName = query;
            if (string.IsNullOrEmpty(refName)) { Ok = false; return; }
            bool targetOk = _variantItem
                ? executor.World.TryResolvePosition(refName, cursor.Agent, out _)
                : executor.World.TryResolveAgent(refName, cursor.Agent, out _);
            Ok = targetOk;
        }

        public void OnTick(float dt)
        {
            if (Finished || !Ok) return;
            _timer += dt;
            switch (_phase)
            {
                case AttemptPhase.Approach:
                    // 蹲下姿态（玩家靠视觉感知"他在偷"）——CrouchMode 只读，用动作表意
                    try { AgentControlHelper.SetPose(_agent, "act_crouch"); } catch { }
                    if (_variantItem)
                    {
                        // 物变体：接近目标物件（≤2m）
                        string refName = PlanRefUtil.Normalize(_step.Target, out string q);
                        if (q != null) refName = q;
                        if (!string.IsNullOrEmpty(refName) && _executor.World.TryResolvePosition(refName, _agent, out Vec3 pos))
                        {
                            if (_agent.Position.Distance(pos) > 2f)
                                AgentControlHelper.ScriptedMoveToPoint(_agent, pos, false);
                        }
                        if (_timer >= 2f) _phase = AttemptPhase.Rolling;
                    }
                    else
                    {
                        _phase = AttemptPhase.Behind;
                    }
                    break;

                case AttemptPhase.Behind:
                    // 人变体：绕背定位（目标背后盲区 + 可达）
                    if (!_executor.World.TryResolveAgent(PlanRefUtil.Normalize(_step.Target, out _), _agent, out Agent target))
                    {
                        _resultKey = "impossible";
                        _phase = AttemptPhase.Settled;
                        return;
                    }
                    if (!target.IsActive())
                    {
                        _resultKey = "impossible";
                        _phase = AttemptPhase.Settled;
                        return;
                    }
                    float dist = _agent.Position.Distance(target.Position);
                    bool behind = false;
                    try
                    {
                        Vec2 look = target.LookDirection.AsVec2.Normalized();
                        Vec2 toSelf = (_agent.Position - target.Position).AsVec2.Normalized();
                        behind = Vec2.DotProduct(look, toSelf) < -0.4f;
                    }
                    catch { }
                    if (dist > 2.5f)
                    {
                        // 还没到位 → 绕到目标背后
                        AgentControlHelper.ScriptedMoveToPoint(_agent, target.Position, false);
                    }
                    else if (behind)
                    {
                        _phase = AttemptPhase.Rolling;
                        _timer = 0f;
                    }
                    else if (_timer > 8f)
                    {
                        // 站位不可行 → 诚实报告（没地方下手）
                        _resultKey = "impossible";
                        _phase = AttemptPhase.Settled;
                    }
                    break;

                case AttemptPhase.Rolling:
                    {
                        // 目击检查：有目击者（排除扒窃目标）→ 中断
                        var witnesses = StealManager.GetWitnesses(_agent, null, 15f, 120f);
                        if (!_variantItem)
                        {
                            if (_executor.World.TryResolveAgent(PlanRefUtil.Normalize(_step.Target, out _), _agent, out Agent t))
                                witnesses.RemoveAll(w => w == t);
                        }
                        if (witnesses.Count > 0)
                        {
                            _resultKey = "interrupted";
                            _phase = AttemptPhase.Settled;
                            return;
                        }

                        // 成功率公式：随从 Roguery vs 目标警觉（§4）
                        float chance = 0.5f;
                        try
                        {
                            var hero = (_agent.Character as CharacterObject)?.HeroObject;
                            if (hero != null)
                            {
                                float roguery = hero.GetSkillValue(DefaultSkills.Roguery);
                                chance = MathF.Min(0.85f, 0.3f + roguery / 300f * 0.55f);
                            }
                        }
                        catch { }
                        bool success = _rng.NextDouble() < chance;

                        if (_variantItem)
                        {
                            // 物变体：得手 = 箱子财物（记账语义；箱子无真实库存）
                            if (success)
                            {
                                _amount = 15f + (float)_rng.NextDouble() * 25f;
                                _executor.RecordStolenGold(_amount);
                                _executor.RecordStolenItem("chest_loot", PlanTexts.Loot);
                                _resultKey = "success";
                            }
                            else
                            {
                                _resultKey = "empty";
                            }
                        }
                        else
                        {
                            // 人变体：得手 = 目标钱包守恒转移（目标有 Hero 才可转移）
                            Agent target2 = null;
                            _executor.World.TryResolveAgent(PlanRefUtil.Normalize(_step.Target, out _), _agent, out target2);
                            var targetHero = (target2?.Character as CharacterObject)?.HeroObject;
                            if (!success)
                            {
                                _resultKey = "empty";
                            }
                            else if (targetHero == null)
                            {
                                // 目标无财产 → 诚实"没摸到钱"（守恒铁律：不凭空生成）
                                _resultKey = "empty";
                            }
                            else
                            {
                                _amount = 10f + (float)_rng.NextDouble() * 40f;
                                _executor.RecordStolenGold(_amount);
                                _executor.RecordStolenSource(targetHero);
                                _resultKey = "success";
                            }
                        }

                        // 目击问责（§4.1）：roll 后重新查目击——"得手时被看到"才问责玩家
                        // （roll 前被看到 = interrupted 已返回；此处是得手瞬间的目击者）
                        if (_resultKey == "success")
                        {
                            var finalWitnesses = StealManager.GetWitnesses(_agent, null, 15f, 120f);
                            if (!_variantItem)
                            {
                                if (_executor.World.TryResolveAgent(PlanRefUtil.Normalize(_step.Target, out _), _agent, out Agent t))
                                    finalWitnesses.RemoveAll(w => w == t);
                            }
                            if (finalWitnesses.Count > 0)
                            {
                                var heroIds = new List<string>();
                                var templateWitness = new Dictionary<string, int>();
                                foreach (var w in finalWitnesses)
                                {
                                    var wh = (w.Character as CharacterObject)?.HeroObject;
                                    if (wh != null) heroIds.Add(wh.StringId);
                                    else if (w.Character != null) templateWitness[w.Character.StringId] = 1;
                                    // 警戒脉冲（目击者警觉）
                                    var wb = AgentAIController.GetBrainForAgent(w);
                                    wb?.SetPulseTarget(PlayerActionType.Steal, w.Name?.ToString() ?? "", "gold");
                                    wb?.AddAlert(PlayerActionType.Steal, 3.0f);
                                }
                                AgentAIController.Instance?.RegisterTheftWitnesses(heroIds, templateWitness,
                                    "gold", PlanTexts.Gold, targetName: PlanTexts.Gold, count: (int)_amount);
                                DebugLogger.Log($"[PlanExecutor] 随从偷窃被目击 → 问责玩家（{finalWitnesses.Count} 名目击者）");
                            }
                            else if (!_variantItem)
                            {
                                // 无人目击扒窃 → 暗账（次日发现，保持 Dormant）
                                AgentAIController.Instance?.RegisterUnwitnessedTheft("gold", PlanTexts.Gold, count: (int)_amount);
                            }
                        }

                        _phase = AttemptPhase.Settled;
                        try { AgentControlHelper.StopAndReset(_agent); } catch { }
                        break;
                    }

                case AttemptPhase.Settled:
                    if (_timer >= 2.0f)
                    {
                        _executor.SetStepResultKey(_resultKey);
                        Finished = true;
                    }
                    break;
            }
        }
    }

    /// <summary>give_item / give_gold：移交玩家（铁律 4 守恒；Item==null = 金钱走 Hero 转移）。
    /// give_gold "stolen"：从扒窃源 Hero 钱包守恒转移；物变体（箱子记账）→ 直接记到手。</summary>
    public class GiveInlineState : IInlineStep
    {
        private readonly PlanExecutor _executor;
        private readonly ActorCursor _cursor;
        private readonly Agent _agent;
        private readonly PlanStep _step;
        private bool _applied;
        public bool Ok { get; private set; }
        public bool Finished { get; private set; }

        public GiveInlineState(PlanExecutor executor, ActorCursor cursor, PlanStep step)
        {
            _executor = executor;
            _cursor = cursor;
            _agent = cursor.Agent;
            _step = step;
            // 目标 = 玩家（铁律：移交对象是玩家）
            Ok = Agent.Main != null;
        }

        public void OnTick(float dt)
        {
            if (Finished) return;
            if (!Ok)
            {
                // 创建失败（目标不可解析）→ 步骤失败，不静默成功（铁律 12 出口要有代价/结果）
                _executor.FailStep(_cursor, _step);
                Finished = true;
                return;
            }
            if (_applied) { Finished = true; return; }
            _applied = true;

            try
            {
                var playerHero = Hero.MainHero;
                if (_step.Action == "give_gold")
                {
                    float amount = PlanRefUtil.NumberOr(_step.Amount, 0f);
                    if (_step.Amount != null && _step.Amount.Type == Newtonsoft.Json.Linq.JTokenType.String
                        && (string)_step.Amount == "stolen")
                        amount = _executor.StolenGold;
                    if (amount <= 0f) { Ok = false; Finished = true; return; }   // 没摸到钱 → 该步失败

                    // 守恒转移：扒窃源 Hero 钱包 → 玩家钱包（铁律 4：一方扣一方加）
                    var sourceHero = _executor.StolenSource;
                    if (sourceHero != null && playerHero != null)
                    {
                        AgentControlHelper.TransferGold(sourceHero, playerHero, (int)amount);
                        Ok = true;
                    }
                    else if (playerHero != null)
                    {
                        // 物变体（箱子记账语义）：目标无真实钱包，财物已在 steal_attempt 记入世界侧
                        // （TheftLedger/暗账）——此处为 Grant（虚空 → 玩家，null 显式标注来源），合法非违规
                        AgentControlHelper.SetGold(playerHero, playerHero.Gold + (int)amount);
                        Ok = true;
                    }
                    else
                    {
                        Ok = false;
                    }
                }
                else if (_step.Action == "give_item")
                {
                    // 赃物移交（箱子记账语义：随从把偷到的财物当面交到玩家手上）
                    if (_executor.StolenItem != null)
                    {
                        // 本地化：移交赃物台词
                        AgentHudMissionView.AgentSay(_agent, LWNTextHelper.ResolveText("LWN_plan_handover", "Here, take it."));
                        InformationManager.DisplayMessage(new InformationMessage(
                            // 本地化：随从移交赃物提示
                            LWNTextHelper.ResolveCompound("LWN_plan_handover_msg", ("NAME", _agent.Name?.ToString() ?? ""), ("ITEM", _executor.StolenItem)), Colors.Green));
                        Ok = true;
                    }
                    else
                    {
                        DebugLogger.Log($"[PlanExecutor] give_item 无赃物记录（{_agent.Name}）→ 该步失败");
                        Ok = false;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PlanExecutor] {_step.Action} 失败: {ex.Message}");
                Ok = false;
            }
            if (!Ok)
            {
                // 移交失败（没摸到钱/无源可转）→ 步骤失败路径（on_timeout/abort），不静默成功
                _executor.FailStep(_cursor, _step);
            }
            Finished = true;
        }
    }

    /// <summary>deliver_item：送物（v2；M4 后落地）。</summary>
    public class DeliverInlineState : IInlineStep
    {
        public bool Ok { get; private set; }
        public bool Finished { get; private set; }

        public DeliverInlineState(PlanExecutor executor, ActorCursor cursor, PlanStep step)
        {
            Ok = false;   // 未实现 → 步骤失败走 on_timeout/失败路径，不静默
            DebugLogger.Log($"[PlanExecutor] deliver_item 在 v2 排期，当前失败");
        }

        public void OnTick(float dt) { Finished = true; }
    }

    /// <summary>knockout：背后击晕（复用击晕轮子：ForcePlayAction + event_agent_knocked_out + 袭击记账）。
    /// 成功 = 目标击晕（GOAL/knocked_out 谓词判定）；失败 = 目标反击（event_agent_damaged）→ 本步失败走 on_timeout/abort。</summary>
    public class KnockoutInlineState : IInlineStep
    {
        private enum KPhase { Approach, Strike, Settled }
        private KPhase _phase = KPhase.Approach;
        private float _timer;
        private bool _struck;
        private readonly PlanExecutor _executor;
        private readonly ActorCursor _cursor;
        private readonly Agent _agent;
        private readonly PlanStep _step;
        private readonly Random _rng = new Random();
        public bool Ok { get; private set; }
        public bool Finished { get; private set; }

        public KnockoutInlineState(PlanExecutor executor, ActorCursor cursor, PlanStep step)
        {
            _executor = executor;
            _cursor = cursor;
            _agent = cursor.Agent;
            _step = step;
            string refName = PlanRefUtil.Normalize(step.Target, out string query);
            if (query != null) refName = query;
            if (string.IsNullOrEmpty(refName) || !executor.World.TryResolveAgent(refName, cursor.Agent, out _))
            {
                Ok = false;
                return;
            }
            Ok = true;
        }

        public void OnTick(float dt)
        {
            if (Finished || !Ok) return;
            _timer += dt;
            if (_agent == null || !_agent.IsActive()) { Finished = true; return; }

            string refName = PlanRefUtil.Normalize(_step.Target, out string q);
            if (q != null) refName = q;
            if (!_executor.World.TryResolveAgent(refName, _agent, out Agent target) || !target.IsActive())
            {
                // 目标已离场 → 本步失败（不静默）
                _executor.FailStep(_cursor, _step);
                Finished = true;
                return;
            }

            switch (_phase)
            {
                case KPhase.Approach:
                    // 接近目标（绕背盲区）
                    float dist = _agent.Position.Distance(target.Position);
                    if (dist > 1.8f)
                    {
                        AgentControlHelper.ScriptedMoveToPoint(_agent, target.Position, false);
                    }
                    else
                    {
                        _phase = KPhase.Strike;
                        _timer = 0f;
                    }
                    break;

                case KPhase.Strike:
                    if (!_struck)
                    {
                        _struck = true;
                        Strike(target);
                    }
                    if (_timer >= 1.2f)
                    {
                        _phase = KPhase.Settled;
                        _timer = 0f;
                    }
                    break;

                case KPhase.Settled:
                    // 判定：目标已击晕（GOAL 由谓词检查）；未击晕 → 反击已触发 → 本步失败
                    if (AgentBrain.IsKnockedOut(target))
                    {
                        _executor.IncrementKnockoutCount();
                        Finished = true;
                    }
                    else if (_timer >= 1.5f)
                    {
                        // 没打晕 → 目标已反击（event_agent_damaged）→ 计划性失败走 abort
                        _executor.FailStep(_cursor, _step);
                        Finished = true;
                    }
                    break;
            }
        }

        private void Strike(Agent target)
        {
            try
            {
                AgentControlHelper.FaceToActor(_agent, target);

                // 成功率：随从 Vigor/Control vs 目标（模板 NPC 默认 10）
                int selfVigor = 10, selfControl = 10;
                int tVigor = 10, tControl = 10;
                var selfHero = (_agent.Character as CharacterObject)?.HeroObject;
                var tHero = (target.Character as CharacterObject)?.HeroObject;
                if (selfHero != null)
                {
                    selfVigor = selfHero.GetAttributeValue(DefaultCharacterAttributes.Vigor);
                    selfControl = selfHero.GetAttributeValue(DefaultCharacterAttributes.Control);
                }
                if (tHero != null)
                {
                    tVigor = tHero.GetAttributeValue(DefaultCharacterAttributes.Vigor);
                    tControl = tHero.GetAttributeValue(DefaultCharacterAttributes.Control);
                }
                float successRate = MathF.Min(0.85f, 0.25f + (selfVigor + selfControl - tVigor - tControl) * 0.03f);
                bool success = _rng.NextDouble() < successRate;

                // 出手即是袭击，记账（复用玩家击晕同款）
                AgentAIController.Instance?.RecordAssaultVictim(target);

                if (success && !AgentBrain.IsKnockedOut(target))
                {
                    // 成功：目标倒地 + 击晕事件（标记顺序与玩家路径一致：先标记再广播）
                    AgentControlHelper.ForcePlayAction(target, "act_death_fall_front");
                    target.SetScriptedFlags(Agent.AIScriptedFrameFlags.DoNotRun | Agent.AIScriptedFrameFlags.NoAttack);
                    AgentAIController.Instance?.SendEventToAgent(target, "event_agent_knocked_out");
                    DebugLogger.Log($"[PlanExecutor] {_agent.Name} 击晕了 {target.Name}");
                }
                else
                {
                    // 失败：目标察觉反击（受害者直接进战斗）
                    AgentAIController.Instance?.SendEventToAgent(target, "event_agent_damaged", _agent, target);
                    DebugLogger.Log($"[PlanExecutor] {_agent.Name} 击晕失败，{target.Name} 反击");
                }

                // 第三方目击广播（受害者排除）
                AgentAIController.Instance?.BroadcastEventInRange(
                    target.Position, 20f, "WitnessCrime",
                    exclude: new System.Collections.Generic.HashSet<Agent> { target },
                    requireSight: true,
                    _agent, target);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PlanExecutor] knockout 执行异常: {ex.Message}");
            }
        }
    }
}
