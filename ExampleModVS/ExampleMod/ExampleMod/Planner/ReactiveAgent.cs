using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════
    // ReactiveAgent.cs — 通用对抗方/被叫者模型（§6）
    //
    // 任何 NPC（守卫/村长/商人/路人）都可能是计划的相关方。同一套框架两种用法：
    //   对手方（DISTRACT 的守卫）：职责记忆（离岗到点折返）→ 制造对抗
    //   被叫方（BRING 的村长）：人格决定是否跟来 → 制造"请得动请不动"的差异
    //
    // 驱动：事件驱动——brain ReceiveEvent 收触发词（asked_to_follow/spoken_to…）
    //       → ReactiveAgent 人格演算（weight × 人格修正，取最高者）
    //       → 反应动作入队（同一套原子行为）。
    // 决策结果广播：follow_for_a_bit → followed(请求方)；refuse → refused(请求方)
    //       —— 走既有 AIEvent 通道，执行器步骤级 on_event 消费（§5.4 事件通道）。
    //
    // 默认人格模板兜底：LLM 没写 responses → 按职业默认模板（守卫 duty 高/酒客
    // gullibility 高/未知 → 中性），LLM 的 responses 只是覆盖默认——对抗性不依赖
    // LLM 写得好不好（§6.4）。
    // ═══════════════════════════════════════════════════════════════

    /// <summary>人格参数（§6.1，0~1）。</summary>
    public class ReactivePersonality
    {
        [JsonProperty("gullibility")] public float Gullibility = 0.5f;  // 轻信（跟走/被骗权重）
        [JsonProperty("duty")] public float Duty = 0.5f;                // 尽职（拒绝/折返权重）
        [JsonProperty("temper")] public float Temper = 0.5f;            // 暴躁（反抗权重）
        [JsonProperty("social")] public float Social = 0.5f;            // 社交（答应/回应权重）
        [JsonProperty("greed")] public float Greed = 0.5f;              // 贪婪（利益诱惑权重）
    }

    /// <summary>反应条目：触发词 → 加权反应表。</summary>
    public class ReactiveResponse
    {
        [JsonProperty("event")] public string Event;                     // 触发词
        [JsonProperty("reactions")] public List<ReactiveReaction> Reactions;

        public class ReactiveReaction
        {
            [JsonProperty("action")] public string Action;               // listen/consider/refuse/follow_for_a_bit/investigate/return_post/stare/alert_raise/attack/call_guards/ignore/relay_message/warn_away
            [JsonProperty("weight")] public float Weight = 1f;
        }
    }

    /// <summary>LLM 可注入的反应计划（计划阶段生成，每相关 NPC 一份；缺省回落默认模板）。</summary>
    public class ReactivePlan
    {
        [JsonProperty("role")] public string Role;                       // guard/chief/tavernkeeper…（快照角色标签）
        [JsonProperty("personality")] public ReactivePersonality Personality;
        [JsonProperty("responses")] public List<ReactiveResponse> Responses;
    }

    /// <summary>运行时 ReactiveAgent（每 NPC 一份；静态注册表 + 职业默认模板兜底）。</summary>
    public class ReactiveAgent
    {
        public Agent Agent;
        public ReactivePersonality Personality;
        public List<ReactiveResponse> Responses;
        public Vec3 PostPos;                 // 岗位（return_post 用；首个触发词时记录当前位置）
        public bool HasPost;

        private static readonly Dictionary<int, ReactiveAgent> _registry = new Dictionary<int, ReactiveAgent>();

        // ── 默认人格模板（职业兜底，§6.4）──
        private static readonly Dictionary<string, ReactivePersonality> DefaultPersonalities =
            new Dictionary<string, ReactivePersonality>(StringComparer.OrdinalIgnoreCase)
            {
                { "guard", new ReactivePersonality { Gullibility = 0.3f, Duty = 0.9f, Temper = 0.6f, Social = 0.4f, Greed = 0.2f } },
                { "villager", new ReactivePersonality { Gullibility = 0.5f, Duty = 0.5f, Temper = 0.4f, Social = 0.6f, Greed = 0.4f } },
                { "drunkard", new ReactivePersonality { Gullibility = 0.8f, Duty = 0.2f, Temper = 0.7f, Social = 0.5f, Greed = 0.3f } },
                { "merchant", new ReactivePersonality { Gullibility = 0.4f, Duty = 0.5f, Temper = 0.4f, Social = 0.7f, Greed = 0.6f } },
                { "chief", new ReactivePersonality { Gullibility = 0.4f, Duty = 0.6f, Temper = 0.4f, Social = 0.7f, Greed = 0.4f } },
                { "tavernkeeper", new ReactivePersonality { Gullibility = 0.5f, Duty = 0.5f, Temper = 0.5f, Social = 0.8f, Greed = 0.5f } },
            };

        // ── 默认反应表（职业兜底；LLM responses 覆盖）──
        private static readonly Dictionary<string, List<ReactiveResponse>> DefaultResponses =
            new Dictionary<string, List<ReactiveResponse>>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "guard", new List<ReactiveResponse>
                    {
                        new ReactiveResponse { Event = "approach_by", Reactions = new List<ReactiveResponse.ReactiveReaction>
                            { new ReactiveResponse.ReactiveReaction { Action = "listen", Weight = 0.9f },
                              new ReactiveResponse.ReactiveReaction { Action = "warn_away", Weight = 0.1f } } },
                        new ReactiveResponse { Event = "spoken_to", Reactions = new List<ReactiveResponse.ReactiveReaction>
                            { new ReactiveResponse.ReactiveReaction { Action = "consider", Weight = 0.7f },
                              new ReactiveResponse.ReactiveReaction { Action = "refuse", Weight = 0.3f } } },
                        new ReactiveResponse { Event = "asked_to_follow", Reactions = new List<ReactiveResponse.ReactiveReaction>
                            { new ReactiveResponse.ReactiveReaction { Action = "follow_for_a_bit", Weight = 0.5f },
                              new ReactiveResponse.ReactiveReaction { Action = "refuse", Weight = 0.5f } } },
                    }
                },
                {
                    "villager", new List<ReactiveResponse>
                    {
                        new ReactiveResponse { Event = "spoken_to", Reactions = new List<ReactiveResponse.ReactiveReaction>
                            { new ReactiveResponse.ReactiveReaction { Action = "listen", Weight = 0.8f },
                              new ReactiveResponse.ReactiveReaction { Action = "ignore", Weight = 0.2f } } },
                        new ReactiveResponse { Event = "asked_to_follow", Reactions = new List<ReactiveResponse.ReactiveReaction>
                            { new ReactiveResponse.ReactiveReaction { Action = "follow_for_a_bit", Weight = 0.7f },
                              new ReactiveResponse.ReactiveReaction { Action = "refuse", Weight = 0.3f } } },
                    }
                },
                {
                    "drunkard", new List<ReactiveResponse>
                    {
                        new ReactiveResponse { Event = "spoken_to", Reactions = new List<ReactiveResponse.ReactiveReaction>
                            { new ReactiveResponse.ReactiveReaction { Action = "listen", Weight = 0.9f },
                              new ReactiveResponse.ReactiveReaction { Action = "ignore", Weight = 0.1f } } },
                        new ReactiveResponse { Event = "asked_to_follow", Reactions = new List<ReactiveResponse.ReactiveReaction>
                            { new ReactiveResponse.ReactiveReaction { Action = "follow_for_a_bit", Weight = 0.8f },
                              new ReactiveResponse.ReactiveReaction { Action = "refuse", Weight = 0.2f } } },
                    }
                },
            };

        // ═══════════════════════════════════════════════════════════
        // 注册表
        // ═══════════════════════════════════════════════════════════

        /// <summary>获取 NPC 的 ReactiveAgent（无 → 按职业默认模板兜底创建）。</summary>
        public static ReactiveAgent Get(Agent agent)
        {
            if (agent == null) return null;
            if (_registry.TryGetValue(agent.Index, out var ra)) return ra;
            var created = CreateDefault(agent);
            _registry[agent.Index] = created;
            return created;
        }

        /// <summary>LLM 注入反应计划（计划批准时应用；覆盖默认模板）。</summary>
        public static void ApplyPlan(Agent agent, ReactivePlan plan)
        {
            if (agent == null || plan == null) return;
            var ra = Get(agent);
            if (plan.Personality != null) ra.Personality = plan.Personality;
            if (plan.Responses != null && plan.Responses.Count > 0) ra.Responses = plan.Responses;
            ra.HasPost = false;   // 岗位以本次触发时的位置为准
        }

        /// <summary>移除（Agent 删除时）。</summary>
        public static void Remove(Agent agent)
        {
            if (agent != null) _registry.Remove(agent.Index);
        }

        public static void ClearAll() => _registry.Clear();

        private static ReactiveAgent CreateDefault(Agent agent)
        {
            string occ = ClassifyOccupation(agent);
            var ra = new ReactiveAgent
            {
                Agent = agent,
                Personality = DefaultPersonalities.TryGetValue(occ, out var p) ? Clone(p) : new ReactivePersonality(),
                Responses = DefaultResponses.TryGetValue(occ, out var r) ? CloneResponses(r) : null,
            };
            return ra;
        }

        /// <summary>深拷贝默认反应表（防止同职业 NPC 共享 List 引用被互相污染）。</summary>
        private static List<ReactiveResponse> CloneResponses(List<ReactiveResponse> source)
        {
            var result = new List<ReactiveResponse>();
            foreach (var r in source)
            {
                var copy = new ReactiveResponse { Event = r.Event, Reactions = new List<ReactiveResponse.ReactiveReaction>() };
                if (r.Reactions != null)
                {
                    foreach (var rr in r.Reactions)
                        copy.Reactions.Add(new ReactiveResponse.ReactiveReaction { Action = rr.Action, Weight = rr.Weight });
                }
                result.Add(copy);
            }
            return result;
        }

        private static string ClassifyOccupation(Agent agent)
        {
            try
            {
                string id = agent.Character?.StringId ?? "";
                if (id.Contains("guard")) return "guard";
                if (id.Contains("drunkard")) return "drunkard";
                if (id.Contains("villager")) return "villager";
                if (id.Contains("tavernkeeper")) return "tavernkeeper";
                if (id.Contains("merchant")) return "merchant";
                if (id.Contains("notable") || id.Contains("headman")) return "chief";
            }
            catch { }
            return "default";
        }

        private static ReactivePersonality Clone(ReactivePersonality p)
        {
            return new ReactivePersonality
            {
                Gullibility = p.Gullibility,
                Duty = p.Duty,
                Temper = p.Temper,
                Social = p.Social,
                Greed = p.Greed,
            };
        }

        // ═══════════════════════════════════════════════════════════
        // 触发词处理（brain ReceiveEvent 调用）
        // ═══════════════════════════════════════════════════════════

        /// <summary>触发词白名单（§6.2）。返回 true = 本事件被消费（不再走 brain 其它分支）。</summary>
        public static bool IsTriggerEvent(string eventType)
        {
            switch (eventType)
            {
                case "approach_by":
                case "spoken_to":
                case "asked_to_follow":
                case "asked_to_stay":
                case "player_suspicious_near":
                case "see_crime":
                case "combat_nearby":
                case "left_post_seconds":
                case "alone_with":
                case "seen_speaking":
                case "see_ally_killed":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>处理触发词：人格演算 → 反应动作（brain ReceiveEvent 内联调用）。</summary>
        public static bool TryHandleEvent(AgentBrain brain, AIEvent aiEvent)
        {
            if (brain?.Owner == null) return false;
            if (!IsTriggerEvent(aiEvent.EventType)) return false;
            if (Settings.Instance.IsInteractionDisabled()) return false;

            var agent = brain.Owner;
            var ra = Get(agent);

            // 首个触发词：记录岗位（return_post 基准）
            if (!ra.HasPost)
            {
                ra.HasPost = true;
                ra.PostPos = agent.Position;
            }

            // 请求方（决策结果广播目标）：触发事件带 Args[0] = 请求方 agent
            Agent requester = aiEvent.Args != null && aiEvent.Args.Length > 0 ? aiEvent.Args[0] as Agent : null;

            // 找匹配反应条目
            var response = ra.Responses?.FirstOrDefault(r =>
                string.Equals(r.Event, aiEvent.EventType, StringComparison.OrdinalIgnoreCase));

            // 固定反应（不靠权重）：left_post_seconds → return_post（§6.1 权重 1.0）
            if (aiEvent.EventType == "left_post_seconds")
            {
                brain.RunReactiveAction(new ReactiveReturnPostAction(ra.PostPos, agent));
                return true;
            }

            if (response == null || response.Reactions == null || response.Reactions.Count == 0)
            {
                // 无反应条目 → 默认：不理（listen 一下）——对抗性由默认模板兜底
                if (aiEvent.EventType == "asked_to_follow")
                {
                    brain.RunReactiveAction(new ReactiveSayAction(agent, "…", 1.5f));
                }
                return true;
            }

            // 人格演算：weight × 修正，取最高者（§6.4）
            var best = response.Reactions[0];
            float bestScore = float.MinValue;
            foreach (var r in response.Reactions)
            {
                float score = r.Weight * Modifier(r.Action, ra.Personality);
                if (score > bestScore) { bestScore = score; best = r; }
            }

            ExecuteReaction(brain, ra, best.Action, requester, aiEvent.EventType);
            return true;
        }

        /// <summary>人格修正系数（§6.4：duty 高 → follow 下调/refuse 上调等）。</summary>
        private static float Modifier(string action, ReactivePersonality p)
        {
            if (p == null) return 1f;
            switch (action)
            {
                case "follow_for_a_bit": return (1f - p.Duty * 0.6f) * (1f + p.Gullibility * 0.4f);
                case "refuse": return (1f + p.Duty * 0.5f) * (1f - p.Social * 0.3f);
                case "investigate": return 1f + p.Duty * 0.4f;
                case "attack": return 1f + p.Temper * 0.6f;
                case "listen": return 1f - p.Duty * 0.2f;
                case "ignore": return 1f - p.Social * 0.3f;
                case "warn_away": return 1f + p.Duty * 0.3f;
                case "consider": return 1f;
                default: return 1f;
            }
        }

        private static void ExecuteReaction(AgentBrain brain, ReactiveAgent ra, string action, Agent requester, string triggerEvent)
        {
            var agent = brain.Owner;
            if (agent == null || !agent.IsActive()) return;

            switch (action)
            {
                case "listen":
                    brain.RunReactiveAction(new LookAtAction(requester ?? Agent.Main, 2f));
                    break;
                case "consider":
                    // 短暂犹豫（2.5s 后由 asked_to_follow 的后续事件决定）
                    brain.RunReactiveAction(new LookAtAction(requester ?? Agent.Main, 2.5f));
                    break;
                case "refuse":
                    // 本地化：拒绝台词（被叫方/对手方）
                    brain.RunReactiveAction(new ReactiveSayAction(agent, LWNTextHelper.ResolveText("LWN_reactive_refuse", "No, I cannot do that."), 2.5f));
                    // 决策结果广播（统一事件名 refused，拒绝任何请求）
                    if (requester != null)
                        AgentAIController.Instance?.SendEventToAgent(requester, "plan_decision", "refused", agent);
                    break;
                case "warn_away":
                    // 本地化：警告台词（守卫警告靠近者）
                    brain.RunReactiveAction(new ReactiveSayAction(agent, LWNTextHelper.ResolveText("LWN_reactive_warnaway", "Stay back! Do not come closer."), 2.5f));
                    break;
                case "follow_for_a_bit":
                    {
                        if (requester == null) break;
                        // 跟随时长按 duty 运行时定（§6.4：duty 高 → 折返快）
                        float followTime = MathF.Max(8f, 28f - ra.Personality.Duty * 18f);
                        brain.RunReactiveAction(new ReactiveFollowAction(requester, ra.PostPos, followTime, agent));
                        // 决策结果广播（跟走）
                        AgentAIController.Instance?.SendEventToAgent(requester, "plan_decision", "followed", agent);
                        break;
                    }
                case "investigate":
                    {
                        // 走向目标区域 + 盯着（复用 move_to + look_at）
                        Vec3 pos = requester?.Position ?? agent.Position;
                        brain.RunReactiveAction(new ReactiveInvestigateAction(pos, requester ?? Agent.Main, agent));
                        break;
                    }
                case "return_post":
                    brain.RunReactiveAction(new ReactiveReturnPostAction(ra.PostPos, agent));
                    break;
                case "stare":
                    brain.RunReactiveAction(new LookAtAction(requester ?? Agent.Main, 5f));
                    break;
                case "alert_raise":
                    // 警戒脉冲（阶段穿越由 brain 认知更新自行处理）
                    brain.AddAlert(PlayerActionType.AttackAlly, 2.0f);
                    break;
                case "attack":
                    if (requester != null)
                    {
                        brain.RunReactiveAction(new FightEnemyAction(requester));
                    }
                    break;
                case "call_guards":
                    AgentAIController.Instance?.BroadcastEventInRange(
                        agent.Position, 30f, "combat_nearby", exclude: null, requireSight: false, agent);
                    break;
                case "ignore":
                default:
                    // 不动（不消费队列）
                    break;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 反应动作（ReactiveAgent 专用；复用既有原子行为 + 三件新行为）
    // ═══════════════════════════════════════════════════════════════

    /// <summary>反应台词：冒泡一句话后结束（refuse/ignore/warn_away 用）。</summary>
    public class ReactiveSayAction : IAtomicAction
    {
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

    /// <summary>跟走一段后折返回岗位（follow_for_a_bit + return_post 一体化；duty 决定时长）。</summary>
    public class ReactiveFollowAction : IAtomicAction
    {
        private enum Phase { Follow, Return, Done }
        private readonly Agent _target;
        private readonly Vec3 _postPos;
        private readonly float _followTime;
        private Phase _phase = Phase.Follow;
        private float _timer;
        private float _fixedTimer;
        private bool _interrupted;
        public void RequestInterrupt() { _interrupted = true; }

        /// <summary>是否仍在"跟随"阶段（following 谓词判定用：true = 守卫正在跟走）。</summary>
        internal bool IsFollowingNow => _phase == Phase.Follow && !_interrupted;

        /// <summary>跟随目标（following 谓词判定用）。</summary>
        internal Agent TargetAgent => _target;

        public ReactiveFollowAction(Agent target, Vec3 postPos, float followTime, Agent owner)
        {
            _target = target;
            _postPos = postPos;
            _followTime = followTime;
        }

        public void OnStart(Agent agent)
        {
            AgentControlHelper.ForceUnlockAgent(agent);
            _timer = 0f;
            _fixedTimer = 0f;
        }

        public void OnTick(Agent agent, float dt)
        {
            _timer += dt;
            _fixedTimer += dt;
            if (_interrupted) { _phase = Phase.Done; return; }

            switch (_phase)
            {
                case Phase.Follow:
                    // 跟随目标（跟走）
                    if (_fixedTimer >= 0.2f)
                    {
                        _fixedTimer = 0f;
                        if (_target != null && _target.IsActive())
                            AgentControlHelper.ScriptedMoveToPoint(agent, _target.Position, false);
                        else
                            _phase = Phase.Return;   // 目标没了 → 折返
                    }
                    if (_timer >= _followTime)
                        _phase = Phase.Return;       // 到点折返（left_post_seconds 语义）
                    break;
                case Phase.Return:
                    if (_fixedTimer >= 0.2f)
                    {
                        _fixedTimer = 0f;
                        AgentControlHelper.ScriptedMoveToPoint(agent, _postPos, false);
                    }
                    if (agent.Position.Distance(_postPos) < 1.5f || _timer > _followTime + 20f)
                        _phase = Phase.Done;
                    break;
            }
        }

        public bool IsFinished(Agent agent) => _phase == Phase.Done || _interrupted;

        public void OnEnd(Agent agent)
        {
            AgentControlHelper.ForceUnlockAgent(agent);
        }
    }

    /// <summary>折返回岗位（return_post 反应）。</summary>
    public class ReactiveReturnPostAction : IAtomicAction
    {
        private readonly Vec3 _postPos;
        private bool _done;
        private float _fixedTimer;
        private bool _interrupted;
        public void RequestInterrupt() { _interrupted = true; }

        public ReactiveReturnPostAction(Vec3 postPos, Agent owner)
        {
            _postPos = postPos;
        }

        public void OnStart(Agent agent)
        {
            AgentControlHelper.ForceUnlockAgent(agent);
        }

        public void OnTick(Agent agent, float dt)
        {
            _fixedTimer += dt;
            if (_interrupted) { _done = true; return; }
            if (_fixedTimer >= 0.2f)
            {
                _fixedTimer = 0f;
                AgentControlHelper.ScriptedMoveToPoint(agent, _postPos, false);
            }
            if (agent.Position.Distance(_postPos) < 1.5f) _done = true;
        }

        public bool IsFinished(Agent agent) => _done || _interrupted;

        public void OnEnd(Agent agent)
        {
            AgentControlHelper.ForceUnlockAgent(agent);
        }
    }

    /// <summary>调查（investigate）：走向目标位置 + 盯着。</summary>
    public class ReactiveInvestigateAction : IAtomicAction
    {
        private readonly Vec3 _pos;
        private readonly Agent _lookTarget;
        private bool _done;
        private float _fixedTimer;
        private float _totalTimer;
        private bool _interrupted;
        public void RequestInterrupt() { _interrupted = true; }

        public ReactiveInvestigateAction(Vec3 pos, Agent lookTarget, Agent owner)
        {
            _pos = pos;
            _lookTarget = lookTarget;
        }

        public void OnStart(Agent agent)
        {
            AgentControlHelper.ForceUnlockAgent(agent);
            if (_lookTarget != null) AgentControlHelper.LookAtAgent(agent, _lookTarget);
        }

        public void OnTick(Agent agent, float dt)
        {
            _fixedTimer += dt;
            _totalTimer += dt;
            if (_interrupted) { _done = true; return; }
            if (_fixedTimer >= 0.2f)
            {
                _fixedTimer = 0f;
                AgentControlHelper.ScriptedMoveToPoint(agent, _pos, false);
            }
            if (agent.Position.Distance(_pos) < 2f || _totalTimer > 30f) _done = true;
        }

        public bool IsFinished(Agent agent) => _done || _interrupted;

        public void OnEnd(Agent agent)
        {
            AgentControlHelper.ForceUnlockAgent(agent);
        }
    }
}
