using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
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
        public int DialogueRound;            // 实时回应会话回合计数（防无限请求；历史在 SingNpcMemorySystem 三层记忆）
        public float LastDialogueTime;       // 会话内最后互动时间（>60s 无互动 = 新会话，轮次重置）

        private static readonly Dictionary<int, ReactiveAgent> _registry = new Dictionary<int, ReactiveAgent>();

        // ── 实时回应（BC-006 v2 → §5.6 统一管线）：respond 的台词 + 动作结果队列
        // （后台线程入队，主线程 TickAll 消费播放 + 执行动作）──
        private static readonly ConcurrentQueue<(Agent Agent, Agent Requester, string Text, string ActionCode, string ActionTarget, string ActionLevel)> _pendingReplies =
            new ConcurrentQueue<(Agent, Agent, string, string, string, string)>();
        private const int MaxDialogueRounds = 6;   // 会话回合上限（"聊天不会太长"：超限用模板短回应）
        private const int RespondTimeoutMs = 2000; // LLM 回应预算：2s 内必须返回，否则降级模板

        // ── 行动提议（Q4，2026-08-10）：propose_plan 的 LLM 提议结果队列（后台线程入队，主线程 TickAll 投递 IM）──
        private static readonly ConcurrentQueue<(Agent Agent, string Text)> _pendingProposals = new ConcurrentQueue<(Agent, string)>();

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
                // 服务/事务职业（BC-006）：被搭话默认 respond（实时 LLM 开口回应，超时降级模板台词）
                {
                    "tavernkeeper", new List<ReactiveResponse>
                    {
                        new ReactiveResponse { Event = "spoken_to", Reactions = new List<ReactiveResponse.ReactiveReaction>
                            { new ReactiveResponse.ReactiveReaction { Action = "respond", Weight = 0.85f },
                              new ReactiveResponse.ReactiveReaction { Action = "listen", Weight = 0.15f } } },
                        new ReactiveResponse { Event = "asked_to_follow", Reactions = new List<ReactiveResponse.ReactiveReaction>
                            { new ReactiveResponse.ReactiveReaction { Action = "consider", Weight = 0.6f },
                              new ReactiveResponse.ReactiveReaction { Action = "refuse", Weight = 0.4f } } },
                    }
                },
                {
                    "merchant", new List<ReactiveResponse>
                    {
                        new ReactiveResponse { Event = "spoken_to", Reactions = new List<ReactiveResponse.ReactiveReaction>
                            { new ReactiveResponse.ReactiveReaction { Action = "respond", Weight = 0.8f },
                              new ReactiveResponse.ReactiveReaction { Action = "listen", Weight = 0.2f } } },
                        new ReactiveResponse { Event = "asked_to_follow", Reactions = new List<ReactiveResponse.ReactiveReaction>
                            { new ReactiveResponse.ReactiveReaction { Action = "consider", Weight = 0.5f },
                              new ReactiveResponse.ReactiveReaction { Action = "refuse", Weight = 0.5f } } },
                    }
                },
                {
                    "chief", new List<ReactiveResponse>
                    {
                        new ReactiveResponse { Event = "spoken_to", Reactions = new List<ReactiveResponse.ReactiveReaction>
                            { new ReactiveResponse.ReactiveReaction { Action = "respond", Weight = 0.75f },
                              new ReactiveResponse.ReactiveReaction { Action = "consider", Weight = 0.25f } } },
                        new ReactiveResponse { Event = "asked_to_follow", Reactions = new List<ReactiveResponse.ReactiveReaction>
                            { new ReactiveResponse.ReactiveReaction { Action = "consider", Weight = 0.55f },
                              new ReactiveResponse.ReactiveReaction { Action = "refuse", Weight = 0.45f } } },
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

        /// <summary>触发词封闭词表（§6.2）——prompt 展示顺序 = 本数组顺序（单一事实源）。
        /// **注册新触发词 = 本数组加一行** + TryHandleEvent 处理分支；TriggerEvents 自动派生、
        /// prompt（BuildGrammar）自动读到。</summary>
        public static readonly string[] TriggerEventsInPromptOrder =
        {
            "approach_by", "spoken_to", "asked_to_follow", "asked_to_stay",
            "player_suspicious_near", "see_crime", "combat_nearby", "left_post_seconds",
            "alone_with", "seen_speaking", "see_ally_killed",
        };

        /// <summary>触发词封闭词表（校验用，由 TriggerEventsInPromptOrder 派生，勿单独修改）。</summary>
        public static readonly HashSet<string> TriggerEvents = new HashSet<string>(TriggerEventsInPromptOrder, StringComparer.Ordinal);

        /// <summary>反应动作封闭词表（§6.3）——prompt 展示顺序 = 本数组顺序（单一事实源）。
        /// **注册新反应动作 = 本数组加一行** + ExecuteReaction 处理分支；ReactionActions 自动派生。</summary>
        public static readonly string[] ReactionActionsInPromptOrder =
        {
            "listen", "consider", "respond", "refuse", "follow_for_a_bit", "investigate",
            "return_post", "stare", "alert_raise", "attack", "call_guards",
            "ignore", "relay_message", "pay", "hand_over_item", "flee",
            // 🔴 2026-08-10（im-command-action-upgrade.md §四 Q4）：propose_plan = 被搭话后想做自己的事
            // （NPC 主动提议 → 私聊消息 → 玩家批准后走 PlanCard 管线——Q2 三态闭环的 NPC 侧入口）
            "propose_plan",
        };

        /// <summary>反应动作封闭词表（校验用，由 ReactionActionsInPromptOrder 派生，勿单独修改）。</summary>
        public static readonly HashSet<string> ReactionActions = new HashSet<string>(ReactionActionsInPromptOrder, StringComparer.Ordinal);

        /// <summary>触发词白名单（§6.2）。返回 true = 本事件被消费（不再走 brain 其它分支）。</summary>
        public static bool IsTriggerEvent(string eventType)
        {
            return eventType != null && TriggerEvents.Contains(eventType);
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

            // 人格演算：weight × 修正，取最高者（§6.4）；bestScore 传给 respond（台词态度与公式结果一致）
            var best = response.Reactions[0];
            float bestScore = float.MinValue;
            foreach (var r in response.Reactions)
            {
                float score = r.Weight * Modifier(r.Action, ra.Personality);
                if (score > bestScore) { bestScore = score; best = r; }
            }

            ExecuteReaction(brain, ra, best.Action, requester, aiEvent.EventType, aiEvent.Args, bestScore);
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
                // 🔴 2026-08-10（Q4）：propose_plan = 尽职者被搭话后想做自己的事（duty 高 → 权重高，
                // 与 respond/ignore 竞争最高分；概率适中，不喧宾夺主）
                case "propose_plan": return 0.6f + p.Duty * 0.4f;
                default: return 1f;
            }
        }

        private static void ExecuteReaction(AgentBrain brain, ReactiveAgent ra, string action, Agent requester, string triggerEvent, object[] args = null, float score = 0f)
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
                case "respond":
                    // 开口回应（BC-006 v2）：LLM 实时生成目标台词——主题 + 上一句 + 对话历史 + 身份人格 +
                    // **演算意图（score：公式算出的意愿度，决定台词态度）**；
                    // 2s 预算内返回 → 队列播放；超时/失败 → 职业模板台词降级。随从台词/主题在 args[1]/args[2]。
                    StartRespond(brain, ra, requester, args, score, triggerEvent);
                    break;
                case "propose_plan":
                    // 🔴 2026-08-10（Q4）：被搭话后想做自己的事 → 提议行动（LLM 生成提议 → 私聊消息 → 玩家批准后走 PlanCard）
                    StartProposal(brain, ra, requester);
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
                case "flee":
                    // 跑离现场（恐慌反应 §6.2：远离触发者一段距离后停下；恐慌传播链 v2）
                    if (requester != null)
                    {
                        Vec3 away = agent.Position + (agent.Position - requester.Position).NormalizedCopy() * 20f;
                        brain.RunReactiveAction(new ReactiveFleeAction(away, agent));
                    }
                    break;
                case "ignore":
                default:
                    // 不动（不消费队列）
                    break;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 实时回应（BC-006 v2）：respond = LLM 实时生成目标台词
        // 上下文 = 身份/人格 + 主题 + 对话历史（含上一句）；2s 预算，失败降级模板。
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 🔴 2026-08-10（Q4）：行动提议——被搭话后想做自己的事（propose_plan）。
        /// LLM 生成一句「主公，我想去…」→ 入队 → 主线程 TickAll 投递到 NPC 的私聊会话（Proposal 消息，
        /// 带批准/拒绝按钮）→ 玩家批准后走 PlanCard 管线（Q2 三态闭环的 NPC 侧入口）。
        /// 降级：LLM 失败/超时/未配置 → 静默不投递（提议是增强不是承诺）。
        /// 记忆纪律：提议是聊天流消息（偏差②），不写 NPC 记忆。
        /// </summary>
        private static async void StartProposal(AgentBrain brain, ReactiveAgent ra, Agent requester)
        {
            try
            {
                var agent = brain.Owner;
                if (agent == null || !agent.IsActive()) return;
                // 铁律 1：LLM 未配置 → 静默（提议是增强）
                if (!Settings.Instance.IsLLMConfigured) return;
                // 仅 Hero 可提议（IM 私聊会话按 Hero StringId 索引；模板 NPC 无法进 IM，既有决策）
                var hero = (agent.Character as CharacterObject)?.HeroObject;
                if (hero == null || string.IsNullOrEmpty(hero.StringId)) return;

                DebugLogger.Log($"[ReactivePropose] {agent.Name} 演算 propose_plan（被 {requester?.Name ?? "?"} 搭话后）");

                var memory = AllNpcMemoryManager.GetMemoryForAgent(agent);
                string identity = ClassifyOccupation(agent);
                // 本地化：职业名（提议身份段）
                string occName = ResolvePromptFallback("LWN_prompt_trait_occupation_" + identity, identity);
                string persona = memory != null ? memory.GetPersonaPrompt() : "";
                // 提议 prompt：世界观 + 身份 + 人设 + 指令（想做自己的事，一句话）
                string prompt = string.Join("\n",
                // 本地化：世界观段标题（提议 prompt）
                    ResolvePromptFallback("LWN_plan_section_world", "【世界观】") + (Settings.Instance?.WorldDescription ?? ""),
                // 本地化：身份段标题（提议 prompt）
                    ResolvePromptFallback("LWN_plan_respond_section_identity", "【你的身份】") + occName,
                    string.IsNullOrEmpty(persona) ? "" : persona,
            // 提议 prompt 纪律（LLM 输入）
                // 本地化：提议 prompt 纪律（LLM 输入）
                    ResolvePromptFallback("LWN_plan_propose_rule",
                        "【行动提议】你刚被人搭话，忽然想起一件自己该做的事（巡逻/望风/讨账/探望/采购等，符合你的身份与当前处境）。用一句话向主公提出，格式：主公，我想去…（10~30 字，直接说，不要解释）。"));
                string proposal = await LLMService.Instance.ChatOnceAsync(prompt, 80, 0.8f, disableReasoning: true, timeoutMs: 8000);
                if (string.IsNullOrWhiteSpace(proposal))
                {
                    DebugLogger.Log($"[ReactivePropose] {agent.Name} 提议生成失败 → 静默");
                    return;
                }
                _pendingProposals.Enqueue((agent, proposal.Trim().Trim('"', '“', '”', '「', '」')));
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ReactivePropose] 异常: {ex.Message}");
            }
        }

        /// <summary>发起实时回应请求（fire-and-forget；结果入队，主线程 TickAll 消费播放）。
        /// score = 人格演算选中 respond 的权重分（公式算出的意愿度 → 台词态度）；triggerEvent = 本次触发词。
        /// 记忆（§八）：上下文与写入统一走 AllNpcMemoryManager 三层记忆（目标对任意人的对话历史）；简易 DialogueHistory 已退役。</summary>
        private static async void StartRespond(AgentBrain brain, ReactiveAgent ra, Agent requester, object[] args, float score, string triggerEvent)
        {
            try
            {
                var agent = brain.Owner;
                if (agent == null || !agent.IsActive()) return;
                string companionLine = args != null && args.Length > 1 ? args[1] as string : null;
                string topic = args != null && args.Length > 2 ? args[2] as string : null;
                string outlineStep = args != null && args.Length > 3 ? args[3] as string : null; // 对话模式当前走向段
                // 目标的三层记忆（Hero StringId 持久 / 模板 NPC TEMP_AGENT 兜底；null-guard 铁律 2）
                var memory = AllNpcMemoryManager.GetMemoryForAgent(agent);
                if (memory == null) { PlayRespondFallback(agent, requester, null); return; }
                // 随从对话触发的记忆维护失败静默（D4：不弹玩家红字；玩家对话路径默认 false 不变）
                memory.SuppressFailureAlerts = true;
                // 会话超时（>60s 无互动 → 新会话，轮次重置；历史在 RecentHistory 天然滚动）
                if (Mission.Current != null && Mission.Current.CurrentTime - ra.LastDialogueTime > 60f)
                    ra.DialogueRound = 0;
                ra.LastDialogueTime = Mission.Current != null ? Mission.Current.CurrentTime : 0f;
                // 回合上限（"聊天不会太长"）：超限不再发请求，直接模板短回应
                if (ra.DialogueRound >= MaxDialogueRounds)
                {
                    PlayRespondFallback(agent, requester, memory);
                    return;
                }
                ra.DialogueRound++;
                DebugLogger.Log($"[ReactiveRespond] {agent.Name} 演算 respond（score={score:F2}，触发={triggerEvent}，第 {ra.DialogueRound} 轮）");
                // 写入对方的话（Role=user 惯例同玩家对话；Content 拼"名字: 台词"；SpeakerId = 对方标识）
                if (!string.IsNullOrEmpty(companionLine))
                    memory.AddHistory("user", $"{requester?.Name}: {companionLine}", requester != null ? GetAgentId(requester) : null);

                // 🔴 §5.6 统一管线（DialogueComponent）：段计算 → GenerateLine（JSON 通道带动作）
                string occ = ClassifyOccupation(agent);
                string occName = ResolvePromptFallback("LWN_prompt_trait_occupation_" + occ, occ);
                string identity = string.Format(
                    // LWN_plan_respond_identity_template：身份模板
                    ResolvePromptFallback("LWN_plan_respond_identity_template", "你是{0}。{1}。"),
                    occName, DescribePersonality(ra.Personality));
                string otherId = requester != null ? GetAgentId(requester) : null;
                string lastLine = GetLastLineWith(memory, otherId);
                // 演算意图 → 台词态度（公式算出的意愿度，台词必须与之一致：热情/正常/敷衍）
                string intention = ResolvePromptFallback("LWN_plan_respond_section_attitude", "【你此刻的态度】")
                    + DescribeIntention(score, triggerEvent);
                string other = requester != null && requester.IsActive() ? requester.Name.ToString() : "";
                string actionSpace = ActionHandler.GetActionSpacePrompt(
                    (agent.Character as CharacterObject)?.HeroObject,
                    (requester?.Character as CharacterObject)?.HeroObject,
                    agent);
                var dline = await DialogueComponent.GenerateLine(
                    Settings.Instance?.WorldDescription ?? "", identity, intention,
                    string.IsNullOrEmpty(topic) ? "闲聊" : topic,
                    ra.DialogueRound > 1 ? $"（第 {ra.DialogueRound} 轮）" : "",
                    string.IsNullOrEmpty(outlineStep) ? ""
                        : ResolvePromptFallback("LWN_plan_respond_section_outline", "【对方正在聊】") + outlineStep,
                    string.IsNullOrEmpty(other) ? "一个陌生人" : other + "（对方是主动来和你搭话的人）",
                    PromptBuilder.GetPrompt_RespondContext(memory, otherId), lastLine,
                    "LWN_plan_respond_rule_json",
                    "【要求】用一句话口语化回应对方（10-40 字），符合身份、性格与此刻的态度，顺着对方的话接，直接说台词本身——不要引号、不要解释、不要动作描写。",
                    actionSpace, maxTokens: 220, timeoutMs: 8000);
                string result = dline != null ? DialogueComponent.Sanitize(dline.Reply, agent.Name?.ToString() ?? "") : null;
                if (!string.IsNullOrWhiteSpace(result))
                {
                    // 🔴 台词/记忆纪律（§5.6）：LLM 实时生成 → 写记忆（user/assistant 接力，对话续得上）
                    memory.AddHistory("assistant", $"{agent.Name}: {result}", GetAgentId(agent));
                    _pendingReplies.Enqueue((agent, requester, result, dline.ActionCode, dline.ActionTarget, dline.ActionLevel));
                }
                else
                {
                    // 模板降级（无 LLM / 超时 / 失败）：台词模板 → 不写记忆（§5.6 裁定）
                    PlayRespondFallback(agent, requester, memory);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ReactiveRespond] 异常: {ex.Message}");
            }
        }

        /// <summary>回应请求 prompt（静态骨架走本地化 LWN_plan_respond_*，动态上下文拼接；单一事实源纪律）。
        /// 上下文八层：世界观 → 身份 → 态度（演算意图）→ 主题/轮次 → **走向段（对话模式）** → 对方 → 记忆裁剪 → 对方刚说。</summary>

        /// <summary>随从台词生成（对话模式，BC-006 v3 → §5.6 统一管线）：LLM 实时生成随从的下一句
        /// （话题 + 走向段 + 双方历史）——走 DialogueComponent.GenerateLine（纯文本通道，动作空间不注入）；
        /// 2s 预算失败 → 走向模板兜底（开场 = 正常开场白模板，非"对了，{段}"）。fire-and-forget，结果回调 onResult。</summary>
        public static void GenerateCompanionLine(Agent companion, Agent target, SingNpcMemorySystem memory,
            string topic, string outlineStep, int index, int total, Action<string> onResult)
        {
            async void Run()
            {
                try
                {
                    var dline = await DialogueComponent.GenerateLine(
                        Settings.Instance?.WorldDescription ?? "",
                        string.Format(
                            // LWN_plan_respond_identity_template：身份模板（随从 = 名字 + 随从身份）
                            ResolvePromptFallback("LWN_plan_respond_identity_template", "你是{0}。{1}。"),
                            companion?.Name?.ToString() ?? "随从",
                            ResolvePromptFallback("LWN_trait_companion", "随从")),
                        "",   // 无演算意图（随从推进对话，态度由走向决定）
                        string.IsNullOrEmpty(topic) ? "闲聊" : topic, "",
                        ResolvePromptFallback("LWN_plan_respond_section_outline", "【对话走向】")
                            + $"第 {index + 1}/{total} 段：{outlineStep}",
                        target?.Name?.ToString() ?? "对方",
                        PromptBuilder.GetPrompt_RespondContext(memory, GetAgentId(target)),
                        GetLastLineWith(memory, GetAgentId(target)),
                        "LWN_plan_respond_rule",
                        "【要求】用一句话口语化对对方说（10-40 字），符合随从身份，顺着当前走向推进对话，直接说台词本身——不要引号、不要解释、不要动作描写。",
                        null, maxTokens: 80, timeoutMs: 2000);
                    string result = dline != null && dline.FromLlm
                        ? DialogueComponent.Sanitize(dline.Reply, companion?.Name?.ToString() ?? "")
                        : null;
                    if (!string.IsNullOrWhiteSpace(result))
                        onResult(result);
                    else
                        onResult(BuildOutlineFallback(outlineStep, index == 0));
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[ReactiveCompanion] 随从台词生成失败（走向模板兜底）: {ex.Message}");
                    try { onResult(BuildOutlineFallback(outlineStep, index == 0)); } catch { }
                }
            }
            Run();
        }

        /// <summary>随从台词请求 prompt（对话模式）：身份 + 话题 + 走向进度 + 双方历史 + 对方刚说。</summary>

        /// <summary>走向模板兜底（随从台词 LLM 失败时）：开场 = 正常开场白；续话 = "对了，{段}……"（用户拍板方案）。
        /// 模板含 {0} 占位 → 走 ResolvePrompt 纯字典读取（TextObject 会把 {…} 当变量，pitfalls 铁律）。</summary>
        private static string BuildOutlineFallback(string outlineStep, bool isOpening)
        {
            try
            {
                if (isOpening)
                {
                    // 开场降级：正常一句开场白（修复 "对了，（开场）。" 离谱输出）
                    return ResolvePromptFallback("LWN_plan_chat_opening_fallback",
                        "Excuse me, I would like to have a word with you.");
                }
                // LWN_plan_chat_fallback：走向模板（含 {0} 占位，走 ResolvePrompt 纯字典）
                string template = ResolvePromptFallback("LWN_plan_chat_fallback", "By the way, {0}.");
                return string.IsNullOrEmpty(outlineStep) ? "嗯。" : string.Format(template, outlineStep);
            }
            catch { return "嗯。"; }
        }

        /// <summary>说话人标识（与 AllNpcMemoryManager.GetMemoryForAgent 的 uniqueId 同算法，保证过滤匹配一致）。
        /// internal：DialogueComponent 续话器（SocialSlot 轮询目标记忆）复用。</summary>
        internal static string GetAgentId(Agent agent)
        {
            if (agent?.Character == null) return null;
            if (agent.Character.IsHero && agent.Character is CharacterObject co && co.HeroObject != null)
                return co.HeroObject.StringId;
            return $"TEMP_AGENT_{agent.Index}_{agent.Name}";
        }

        /// <summary>"对方刚说"：记忆里与对方相关的最后一句（无 SpeakerId 的旧行也保留，玩家对话同源）。
        /// internal：DialogueComponent 续话器（SocialSlot）复用。</summary>
        internal static string GetLastLineWith(SingNpcMemorySystem memory, string otherId)
        {
            if (memory == null) return "";
            for (int i = memory.RecentHistory.Count - 1; i >= 0; i--)
            {
                var msg = memory.RecentHistory[i];
                if (msg == null || string.IsNullOrEmpty(msg.Content)) continue;
                if (string.IsNullOrEmpty(msg.SpeakerId) || msg.SpeakerId == otherId)
                    return msg.Content;
            }
            return "";
        }

        /// <summary>演算意图 → 台词态度描述（阈值 0.75 热情 / 0.55 正常 / 更低 敷衍；与 §6.4 公式结果一致）。</summary>
        private static string DescribeIntention(float score, string triggerEvent)
        {
            if (score >= 0.75f)
                // 意愿度高：热情回应（LWN_plan_respond_attitude_hot）
                return ResolvePromptFallback("LWN_plan_respond_attitude_hot",
                    "对方主动搭话，你愿意聊下去（意愿度高）——回应热情些，顺着话题说。");
            if (score >= 0.55f)
                // 意愿度中等：正常寒暄（LWN_plan_respond_attitude_normal）
                return ResolvePromptFallback("LWN_plan_respond_attitude_normal",
                    "对方主动搭话，你愿意回应（意愿度中等）——正常寒暄即可。");
            // 意愿度低：敷衍冷淡（LWN_plan_respond_attitude_reluctant）
            return ResolvePromptFallback("LWN_plan_respond_attitude_reluctant",
                "你其实不太想搭理对方（意愿度低），但出于礼貌还是回一句——语气要敷衍冷淡，简短了事。");
        }

        /// <summary>降级：LLM 未配置/超时/失败 → 职业模板台词（铁律 1：不崩、对话不卡死）。
        /// 🔴 §5.6 记忆纪律（2026-08-10 用户裁定）：模板降级 → **不写记忆**——模板是重复无个性内容，
        /// 写进记忆会稀释真实事件记忆、且污染续话轮询（GetLastLineWith 会把它当真实回应接住）。</summary>
        private static void PlayRespondFallback(Agent agent, Agent requester, SingNpcMemorySystem memory)
        {
            try
            {
                if (agent == null || !agent.IsActive()) return;
                string occ = ClassifyOccupation(agent);
                // 降级台词：职业模板（无则默认模板）
                string text = LWNTextHelper.ResolveText("LWN_reactive_respond_" + occ,
                    // LWN_reactive_respond_default：默认模板兜底（fallback 英文惯例）
                    LWNTextHelper.ResolveText("LWN_reactive_respond_default", "I see."));
                if (string.IsNullOrEmpty(text)) text = "嗯，知道了。";
                // 🔴 §5.6：模板不写记忆（见上方纪律）
                _pendingReplies.Enqueue((agent, requester, text, null, null, null));
            }
            catch { }
        }

        /// <summary>人格数值 → 一句话描述（LLM 身份段用；世界观中性词，走本地化）。</summary>
        private static string DescribePersonality(ReactivePersonality p)
        {
            if (p == null) return "";
            var parts = new List<string>();
            // 人格数值 → 中文 trait 描述（阈值 0.7/0.3，中性世界观词）
            if (p.Duty >= 0.7f) parts.Add(LWNTextHelper.ResolveText("LWN_trait_duty_high", "尽职尽责"));
            else if (p.Duty <= 0.3f) parts.Add(LWNTextHelper.ResolveText("LWN_trait_duty_low", "随性散漫"));
            if (p.Temper >= 0.7f) parts.Add(LWNTextHelper.ResolveText("LWN_trait_temper_high", "脾气火爆"));
            if (p.Social >= 0.7f) parts.Add(LWNTextHelper.ResolveText("LWN_trait_social_high", "八面玲珑"));
            else if (p.Social <= 0.3f) parts.Add(LWNTextHelper.ResolveText("LWN_trait_social_low", "冷淡寡言"));
            if (p.Gullibility >= 0.7f) parts.Add(LWNTextHelper.ResolveText("LWN_trait_gullible", "轻信好哄"));
            if (parts.Count == 0) parts.Add(LWNTextHelper.ResolveText("LWN_trait_neutral", "性子平常"));
            return string.Join("，", parts);
        }

        /// <summary>主线程消费回应结果（AgentAIController.OnMissionTick → ReactiveAgent.TickAll）：
        /// **接管目标 brain**（face + stay：面向对方保持，对话期间停下来说话——用户要求"两个人面对面"）→ 冒泡。
        /// 🔴 2026-08-10（§5.6）：播放后执行动作决策（JSON 通道，与 IM 群聊同构——空间由 ActionHandler 裁决）。
        /// 🔴 2026-08-10（Q4）：顺带消费行动提议（_pendingProposals → 私聊 Proposal 消息）。</summary>
        public static void TickAll(float dt)
        {
            while (_pendingReplies.TryDequeue(out var item))
            {
                try
                {
                    if (item.Agent == null || !item.Agent.IsActive()) continue;
                    // 接管 brain：RunReactiveAction 清队列 + SuspendVanillaAI + 入队 LookAtAction（面向对方保持 8s）
                    if (item.Requester != null && item.Requester.IsActive())
                    {
                        var brain = AgentAIController.GetBrainForAgent(item.Agent);
                        if (brain != null)
                            brain.RunReactiveAction(new LookAtAction(item.Requester, 8f));
                        else
                            AgentControlHelper.FaceToActor(item.Agent, item.Requester);
                    }
                    AgentHudMissionView.AgentSay(item.Agent, item.Text);
                    // 🔴 §5.6 动作决策执行（说话带动作：威胁手势/拔刀/做表情…；空间+冷却+IsValid 全在 ActionHandler 内部）
                    if (!string.IsNullOrEmpty(item.ActionCode) && item.ActionCode != "NONE")
                    {
                        try
                        {
                            ActionHandler.HandleAction(item.ActionCode,
                                (item.Agent.Character as CharacterObject)?.HeroObject,
                                (item.Requester?.Character as CharacterObject)?.HeroObject,
                                item.Agent, item.ActionLevel, item.ActionTarget, item.Text);
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.Log($"[ReactiveRespond] 动作执行失败 {item.ActionCode}: {ex.Message}");
                        }
                    }
                }
                catch { }
            }

            // 🔴 Q4：行动提议投递（NPC 主动提议 → 私聊 Proposal 消息 → 玩家批准后走 PlanCard）
            while (_pendingProposals.TryDequeue(out var p))
            {
                try
                {
                    if (p.Agent == null || !p.Agent.IsActive()) continue;
                    var hero = (p.Agent.Character as CharacterObject)?.HeroObject;
                    if (hero == null || string.IsNullOrEmpty(hero.StringId)) continue;
                    if (string.IsNullOrWhiteSpace(p.Text)) continue;
                    // 会话定位（私聊 direct_{heroId}；不存在 → 运行时索引建立——既有机制）
                    ImChatStore.TouchDirectChat(hero.StringId, ImChatManager.NowUnixMs());
                    var conv = ImChatManager.GetDirectConversation(hero.StringId);
                    if (conv == null) continue;
                    ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(hero.StringId, hero.Name?.ToString() ?? "Companion", p.Text, ImMessageKind.Proposal)
                    {
                        ConvId = conv.Id,
                    });
                    ImChatStore.IncUnread(conv.Id);
                    ImChatManager.BroadcastMessageArrived(conv);
                    DebugLogger.Log($"[ReactivePropose] {p.Agent.Name} 提议已投递私聊: {p.Text}");
                }
                catch { }
            }
        }

        private static string ResolvePromptFallback(string key, string fallback)
        {
            string s = LWNTextHelper.ResolvePrompt(key);
            return string.IsNullOrEmpty(s) ? fallback : s;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 反应动作（ReactiveAgent 专用；复用既有原子行为 + 三件新行为）
    // ═══════════════════════════════════════════════════════════════

    /// <summary>反应链 IAtomicAction 实现已迁移至 AI/Actions/AtomicAction.cs（2026-08-11 统一）。
    /// 本文件仅保留 ReactiveAgent 反应系统本体。</summary>
}
