using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SandBox;
using SandBox.Missions.AgentBehaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    public class AgentAIController : MissionLogic
    {
        public static AgentAIController Instance { get; private set; }

        // 1. 修改字典定义：Key 从 Agent 改为 int (Agent.Index)
        private Dictionary<int, AgentBrain> _brains = new Dictionary<int, AgentBrain>();
        private static bool IsDebugMode = false; // 排查时改 true

        /// <summary>广播事件楼层闸门：与事件中心高度差超过该值视为不同楼层，不接收广播（二楼打晕不惊动一楼）</summary>
        private const float SAME_FLOOR_MAX_HEIGHT_DIFF = 2.0f;

        /// <summary>全局 Misconduct 事件序号，确保同小时内多次犯案 EventId 不撞车</summary>
        private static int _misconductSeq = 0;

        // ═══════════════════════════════════════════════════════════════
        // 🆕 PendingWorldEvent — Mission 作用域犯罪记录
        // ═══════════════════════════════════════════════════════════════
        /// <summary>
        /// 本场 Mission 的待提交犯罪事件。NPC 进入 Alarmed 时注册目击证词；
        /// 离开场景时一次性持久化到 WorldEventStore。
        /// </summary>
        public WorldEvent PendingWorldEvent { get; private set; }
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

            // ── 自动随从关系兜底：OnAgentCreated 时 Agent.Main 可能未就绪（玩家 Agent 晚创建），
            //    启动完成后给玩家队友（同伴/同部队）补设 Leader = Agent.Main。 ──
            foreach (var kv in _brains)
            {
                var b = kv.Value;
                if (b.Leader == null && Agent.Main != null && AgentBrain.IsPlayerTeammate(b.Owner))
                {
                    b.SetLeader(Agent.Main);
                    if (IsDebugMode)
                        DebugLogger.Log($"[随从关系-兜底] {b.Owner.Name} → Leader=玩家");
                }
            }

            // ── PendingWorldEvent 初始化 ──
            var settlement = Settlement.CurrentSettlement ?? Hero.MainHero?.CurrentSettlement;
            if (settlement != null)
            {
                string sceneLoc = WorldEvent.ResolveSceneLocationName(CampaignMission.Current?.Location?.StringId);

                // 场景感知查找：同子场景的已有事件可续档；旧存档中未设置子场景的事件首次遇
                // 到具体场景时自动升级 LocationName（之后便不会与其他子场景的事件合并）。
                WorldEvent existing = null;
                if (!string.IsNullOrEmpty(sceneLoc))
                {
                    // 有具体子场景 → 优先匹配同场景；其次匹配旧存档未设置子场景的事件（升级之）
                    existing = WorldEventStore.FindOnGoing(settlement.StringId, evt =>
                        evt.Type == EventType.Misconduct &&
                        (evt.LocationName == sceneLoc || evt.LocationName == null));

                    if (existing != null && existing.LocationName == null)
                    {
                        existing.LocationName = sceneLoc;
                        DebugLogger.Log($"[WorldEvent] Upgraded legacy event {existing.EventId} LocationName: null → {sceneLoc}");
                    }
                }
                else
                {
                    // 无具体子场景（城镇中心 / 村庄中心等） → 只匹配同样无子场景的事件
                    existing = WorldEventStore.FindOnGoing(settlement.StringId, evt =>
                        evt.Type == EventType.Misconduct && evt.LocationName == null);
                }

                PendingWorldEvent = existing
                    ?? new WorldEvent
                    {
                        EventId = $"misconduct_{settlement.StringId}_{(int)CampaignTime.Now.ToHours}_{++_misconductSeq}",
                        Category = EventCategory.Crime,
                        Type = EventType.Misconduct,
                        InitiatorId = Hero.MainHero?.StringId ?? "player",
                        TargetSettlementId = settlement.StringId,
                        OccurredDay = (float)CampaignTime.Now.ToDays,
                        LocationName = sceneLoc,
                        Stage = EventStage.Dormant,
                        WitnessTestimonies = new List<WitnessTestimony>(),
                    };
            }

            // ── NpcSightSystem → AgentBrain 事件桥接 ──
            // 当 NPC 开始看到玩家时，路由为事件发给对应 AgentBrain，
            // 由 AgentBrain.ReceiveEvent 统一决定是否 BubbleSay。
            // 不在外部调用 BubbleSay/AgentSay。
            var sight = Mission.Current?.GetMissionBehavior<NpcSightSystem>();
            if (sight != null)
            {
                sight.OnAgentStartObserving += (observer, target) =>
                {
                    //暂时关掉看到玩家的事件，避免干扰测试
                    return;
                    //if (target != Agent.Main) return;
                    //if (observer == null || !observer.IsActive()) return;
                    //if (InteractionMissionView.IsChatting) return;
                    //SendEventToAgent(observer, "StartObservingPlayer");
                };
            }

            if(!AgentAIController.IsDebugMode)
                return;

            // ── 临时 Debug：打印场景内所有 Agent 的原版 AI 状态 ──
            int humanCount = 0;
            foreach (var agent in Mission.Agents)
            {
                if (!AgentControlHelper.IsHumanOrChild(agent)) continue;
                humanCount++;

                var nav = agent.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
                if (nav != null)
                {
                    var daily = nav.GetBehaviorGroup<DailyBehaviorGroup>();
                    DebugLogger.Log($"[AI-Debug-Init] {agent.Name} (Idx={agent.Index}) | " +
                        $"hasNavigator=yes | dailyActive={daily?.IsActive} | " +
                        $"scriptedFlags={agent.GetScriptedFlags()} | " +
                        $"suspended={AgentBrain.SuspendedAgentIndices.Contains(agent.Index)}");
                }
                else
                {
                    DebugLogger.Log($"[AI-Debug-Init] {agent.Name} (Idx={agent.Index}) | hasNavigator=no (战斗单位?)");
                }
            }
            DebugLogger.Log($"[AI-Debug-Init] 总计 {humanCount} 个人形 Agent，脑数量={_brains.Count}");
        }

        public override void OnAgentCreated(Agent agent)
        {
            // 人类或儿童都注册 brain：小孩在玩家认知里也是人，对话/警戒/感知与大人同等对待
            if (AgentControlHelper.IsHumanOrChild(agent))
            {
                // 3. 修改注册逻辑：判断 Key 是否存在
                if (!_brains.ContainsKey(agent.Index))
                {
                    if (IsDebugMode)
                        DebugLogger.Log($"[新增] Name: {agent.Name} (Index: {agent.Index})");

                    _brains.Add(agent.Index, new AgentBrain(agent));
                    // 自动随从关系：玩家 party 带入的随从（同伴/同部队）→ Leader = Agent.Main。
                    // 真随从不走对话 FollowIntent 玩法行（该行大部分情况不出现），Leader 关系
                    // 由身份判定直接建立——isCompanion（密谋入口/计划系统）依赖此关系。
                    // Agent.Main 可能晚于 NPC 创建 → 未就绪时交给 AfterStart 兜底补设。
                    if (Agent.Main != null && AgentBrain.IsPlayerTeammate(agent))
                    {
                        _brains[agent.Index].SetLeader(Agent.Main);
                        if (IsDebugMode)
                            DebugLogger.Log($"[随从关系] {agent.Name} → Leader=玩家（身份判定自动建立）");
                    }
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
            if (_brains.TryGetValue(agent.Index, out var brain))
            {
                if (IsDebugMode)
                    DebugLogger.Log($"因为删除 移除一个Agent的大脑 name {agent.Name} index{agent.Index} 当前总数{_brains.Count}");
                brain.OnOwnerDeleted();
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

            // 密谋命令系统：执行器统一驱动（与 brain 队列解耦，收尾报告流程也在此推进）
            PlanExecutor.TickAll(dt);
        }

        public override void OnRemoveBehavior()
        {
            // 密谋命令系统：Mission 结束 → 执行器统一收尾（OnMissionScreenFinalize 兜底纪律）
            PlanExecutor.ShutdownAll();
            FinalizePendingWorldEvent();
            CombatManager.OnMissionEnd();
            base.OnRemoveBehavior();
        }

        // ═══════════════════════════════════════════════════════════════
        // 🆕 PendingWorldEvent — 目击者注册与持久化
        // ═══════════════════════════════════════════════════════════════

        /// <summary>AgentBrain 到达 Alarmed 时调用：将此 NPC 注册为目击者，并推进 WorldEvent 阶段。</summary>
        public void RegisterWitness(AgentBrain brain)
        {
            var pending = PendingWorldEvent;
            if (pending == null) return;

            // 🆕 目击者当场看到玩家作案 → 嫌疑人=玩家，直接 Active
            // 不管之前是什么阶段（Dormant/Emerging），有人亲眼看见 → 嫌疑人明确，直接 Active。
            if (pending.Stage < EventStage.Active)
            {
                WorldEventStore.TransitionStage(pending, EventStage.Active, Hero.MainHero?.StringId);
                DebugLogger.Log($"[RegisterWitness] {brain.Owner.Name} witnessed crime → WorldEvent {pending.EventId} Stage → Active (suspect=player)");
            }

            pending.WitnessTestimonies = pending.WitnessTestimonies ?? new List<WitnessTestimony>();

            var hero = (brain.Owner.Character as CharacterObject)?.HeroObject;
            string heroId = hero?.StringId;
            string templateId = hero == null ? brain.Owner.Character?.StringId : null;

            // 已有同 NPC 的 testimony → 不重复创建，只同步
            var existing = pending.WitnessTestimonies.FirstOrDefault(t =>
                (heroId != null && t.WitnessHeroId == heroId) ||
                (templateId != null && t.TemplateId == templateId));
            if (existing != null)
            {
                existing.Actions = existing.Actions ?? new List<ActionRecord>();
                SyncActions(brain, existing);
                return;
            }

            // 新建 testimony 并从 brain._alertBreakdown 同步
            var testimony = new WitnessTestimony
            {
                WitnessHeroId = heroId,
                TemplateId = templateId,
                Actions = new List<ActionRecord>()
            };
            SyncActions(brain, testimony);
            pending.WitnessTestimonies.Add(testimony);
        }

        void SyncActions(AgentBrain brain, WitnessTestimony testimony)
        {
            var breakdown = brain.AlertBreakdown;
            if (breakdown == null) return;
            testimony.Actions = testimony.Actions ?? new List<ActionRecord>();

            foreach (var kv in breakdown)
            {
                var entry = kv.Value;
                var existing = testimony.Actions.FirstOrDefault(a => a.ActionType == kv.Key.ToString());
                if (existing != null)
                {
                    existing.AlertValue = entry.Value;
                    existing.TargetName = entry.TargetName ?? existing.TargetName;
                    existing.ItemName = entry.ItemName ?? existing.ItemName;
                }
                else
                {
                    testimony.Actions.Add(new ActionRecord
                    {
                        ActionType = kv.Key.ToString(),
                        AlertValue = entry.Value,
                        TargetName = entry.TargetName,
                        ItemName = entry.ItemName,
                    });
                }
            }
        }

        /// <summary>偷窃目击者（StealManager 调用）：witnessHeroIds/templateWitness 来自 GetWitnesses()</summary>
        /// <param name="count">数量/面额：gold = 第纳尔面额；普通物品 = 件数（默认 1）</param>
        public void RegisterTheftWitnesses(List<string> witnessHeroIds, Dictionary<string, int> templateWitness,
            string itemId, string itemName, string targetName = null, int count = 1)
        {
            var pending = PendingWorldEvent;
            if (pending == null) return;

            pending.WitnessTestimonies = pending.WitnessTestimonies ?? new List<WitnessTestimony>();

            foreach (var heroId in witnessHeroIds)
                AddStealAction(pending, heroId, null, itemId, itemName, targetName, count);
            foreach (var kv in templateWitness)
                AddStealAction(pending, null, kv.Key, itemId, itemName, targetName, count);

            // 有目击者当场看到玩家偷窃 → 嫌疑人=玩家，直接 Active
            if (pending.Stage < EventStage.Active)
                WorldEventStore.TransitionStage(pending, EventStage.Active, Hero.MainHero?.StringId);
        }

        static void AddStealAction(WorldEvent pending, string heroId, string templateId,
            string itemId, string itemName, string targetName, int count = 1)
        {
            //合并偷窃记录

            var testimony = pending.WitnessTestimonies.FirstOrDefault(t =>
                (heroId != null && t.WitnessHeroId == heroId) || //某个英雄角色再次目击
                (templateId != null && t.TemplateId == templateId) || //某个模版角色再次目击
                (heroId == null && templateId == null && t.WitnessHeroId == null && t.TemplateId == null)); // 再次出现无人目击的偷窃事实
            if (testimony == null)
            {
                testimony = new WitnessTestimony
                {
                    WitnessHeroId = heroId,
                    TemplateId = templateId,
                    Actions = new List<ActionRecord>()
                };
                pending.WitnessTestimonies.Add(testimony);
            }
            testimony.Actions = testimony.Actions ?? new List<ActionRecord>();
            testimony.Actions.Add(new ActionRecord
            {
                ActionType = "Steal",
                AlertValue = 3.0f,
                TargetName = targetName,
                ItemId = itemId,
                ItemName = itemName,
                Count = count,
            });
        }

        /// <summary>
        /// 无人目击的偷窃记账（StealManager 各偷窃路径的无人目击分支调用）：
        /// 写入「系统暗账」（双 null 证词，见 WitnessTestimony 注释）。
        /// 事件保持 Dormant 不推进阶段——无人看见就不知道是谁，等 ProcessDormant 过夜被发现。
        /// </summary>
        /// <param name="count">数量/面额：gold = 第纳尔面额；普通物品 = 件数（默认 1）</param>
        public void RegisterUnwitnessedTheft(string itemId, string itemName, string targetName = null, int count = 1)
        {
            var pending = PendingWorldEvent;
            if (pending == null) return;

            pending.WitnessTestimonies = pending.WitnessTestimonies ?? new List<WitnessTestimony>();
            AddStealAction(pending, null, null, itemId, itemName, targetName, count);
            DebugLogger.Log($"[DarkTheft] Unwitnessed: {itemName ?? itemId} → pending {pending.EventId} stays Dormant");
        }

        /// <summary>
        /// 击晕/袭击记账（击晕时调用）：受害者身价（原版俘虏赎金价）累计进 PendingWorldEvent，
        /// 赔偿基础值即身价本身。无目击时事件不会激活入档，此记账自然随 PendingWorldEvent 丢弃——不算无头案。
        /// </summary>
        public void RecordAssaultVictim(Agent victim)
        {
            var pending = PendingWorldEvent;
            if (pending == null || victim == null) return;

            int value = CrimePenaltyCalculator.EstimateVictimValue(victim);
            pending.AssaultValue += value;
            pending.AssaultVictimNames = pending.AssaultVictimNames ?? new List<string>();
            string name = victim.Name?.ToString();
            if (!string.IsNullOrEmpty(name) && !pending.AssaultVictimNames.Contains(name))
                pending.AssaultVictimNames.Add(name);
            DebugLogger.Log($"[Assault] {name} 身价={value} → 事件 {pending.EventId} AssaultValue={pending.AssaultValue}（赔偿基数 {pending.AssaultRestitutionValue}）");
        }

        void FinalizePendingWorldEvent()
        {
            var pending = PendingWorldEvent;
            if (pending == null) return;
            var testimonies = pending.WitnessTestimonies;
            if (testimonies == null || testimonies.Count == 0) return;   // 无事发生，照丢

            // 区分真目击（Alarmed NPC 证词）与系统暗账（无人目击的偷窃事实）
            bool hasRealWitness = testimonies.Any(t => t.WitnessHeroId != null || t.TemplateId != null);

            if (hasRealWitness)
            {
                // 有目击者 → 嫌疑人=玩家，直接 Active
                if (pending.Stage < EventStage.Active)
                {
                    WorldEventStore.TransitionStage(pending, EventStage.Active, Hero.MainHero?.StringId);
                }
                pending.InvestigationProgress = 1.0f;
                pending.PublicAwareness = 0.3f;
            }
            // else: 只有暗账 → 保持 Dormant 入档，等 WorldEventStore.ProcessDormant 过夜被发现
            //       （村民知道丢了什么，不知道是谁——调查/冷案/栽赃走既有的 Emerging 机器）

            WorldEventStore.AddOrMerge(pending);
        }

        /// <summary>
        /// 结案广播清警戒：事件 Resolved（赔钱/坐牢/自首/宽恕等）时调用。
        /// 清掉场景内所有与该事件受害者相关的警戒条目——否则其他目击者（如旁观群众）
        /// 带着旧警戒值在结案后升级 Alarmed → 再次质问玩家（"已经付过钱还要道歉"bug）。
        /// 受害者名来源：AssaultVictimNames（袭击受害者）+ WitnessTestimonies 的 TargetName（失窃/袭击目标）。
        /// </summary>
        public void ClearAlertsForEvent(WorldEvent evt)
        {
            if (evt == null) return;

            var victimNames = new HashSet<string>();
            if (evt.AssaultVictimNames != null)
            {
                foreach (var n in evt.AssaultVictimNames)
                    if (!string.IsNullOrEmpty(n)) victimNames.Add(n);
            }
            if (evt.WitnessTestimonies != null)
            {
                foreach (var t in evt.WitnessTestimonies)
                {
                    if (t?.Actions == null) continue;
                    foreach (var a in t.Actions)
                        if (!string.IsNullOrEmpty(a?.TargetName)) victimNames.Add(a.TargetName);
                }
            }
            if (victimNames.Count == 0) return;

            int cleared = 0;
            foreach (var brain in _brains.Values)
            {
                if (brain != null && brain.ClearAlertsForVictimNames(victimNames))
                    cleared++;
            }
            if (cleared > 0)
                DebugLogger.Log($"[WorldEvent] 结案广播清警戒: {evt.EventId} 清除 {cleared} 个 brain 的警戒");
        }

        // --- 外部调用接口 ---

        
        // 2. 发送事件给特定 Agent
        public void SendEventToAgent(Agent target, string eventType, params object[] args)
        {
            // 战斗模式下不发送 LLM 事件——原生 AI 接管所有战斗行为
            if (Settings.Instance.IsInteractionDisabled())
                return;

            if(IsDebugMode)
                DebugLogger.Log($"尝试发送事件 '{eventType}' 给 {target.Name} (Index: {target.Index},当前brains总数{_brains.Count})");
            var brain = GetBrainForAgent(target);
            if (brain!=null)
            {
                var dist = Agent.Main != null ? target.Position.Distance(Agent.Main.Position).ToString("F1") : "?";
                var dir = Agent.Main != null ? GetDirectionFromTo(Agent.Main.Position, target.Position) : "?";
                if (Settings.Instance.ShowDebugMessages)
                    // 事件发送调试飘字：通知事件已发给目标 Agent
                    InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ai_event_send",
                        ("EVENT", eventType), ("NAME", target.Name.ToString()), ("INDEX", target.Index.ToString()), ("DIST", dist), ("DIR", dir))));
                if (IsDebugMode)
                    DebugLogger.Log($"[事件发送] 发送事件 '{eventType}' 给 {target.Name} (Index:{target.Index}, 距离:{dist}m, 方位:{dir})");
                var evt = new AIEvent { EventType = eventType, Sender = null, Args = args };
                brain.ReceiveEvent(evt);
            }
            else
            {
                if (Settings.Instance.ShowDebugMessages)
                    // 事件发送失败飘字：目标 Agent 没有对应的大脑
                    InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_ai_event_send_no_brain",
                        ("EVENT", eventType), ("NAME", target.Name.ToString()))));
                if(IsDebugMode)
                    DebugLogger.Log($"[警告] 试图发送事件 '{eventType}' 给 {target.Name}，但未找到对应的大脑。");
            }
        }

        // 3. 范围广播（最常用）
        /// <param name="requireSight">true=只看能看到玩家的 NPC（默认），false=范围内全部通知</param>
        public void BroadcastEventInRange(Vec3 center, float radius, string eventType, bool requireSight = true, params object[] args)
        {
            BroadcastEventInRange(center, radius, eventType, null, requireSight, args);
        }

        /// <param name="exclude">排除列表：这些 Agent 不会收到事件（如击晕受害者不参与围观）</param>
        public void BroadcastEventInRange(Vec3 center, float radius, string eventType, HashSet<Agent> exclude, bool requireSight, params object[] args)
        {
            // 战斗模式下不发送 LLM 事件——原生 AI 接管所有战斗行为
            if (Settings.Instance.IsInteractionDisabled())
                return;

            // 1. 找出范围内所有的大脑
            List<AgentBrain> brainsInRange = new List<AgentBrain>();
            List<Agent> witnesses = new List<Agent>();
            if (IsDebugMode)
                DebugLogger.Log($"当前brains总数为{_brains.Count}");
            foreach (var brain in _brains.Values)
            {
                if (!brain.Owner.IsActive() || brain.Owner == Agent.Main) continue;
                if (brain.Owner.Position.Distance(center) > radius) continue;
                // 楼层闸门：与事件中心高度差 > 2m 视为不同楼层，拦截（二楼打晕不应惊动一楼）
                if (MathF.Abs(brain.Owner.Position.z - center.z) > SAME_FLOOR_MAX_HEIGHT_DIFF)
                {
                    if (IsDebugMode)
                        DebugLogger.Log($"{brain.Owner.Name} 与事件中心高度差 {brain.Owner.Position.z - center.z:F1}m > {SAME_FLOOR_MAX_HEIGHT_DIFF}m，跨楼层拦截广播 '{eventType}'");
                      //InformationManager.DisplayMessage(new InformationMessage($"{brain.Owner.Name} 跨楼层拦截 '{eventType}' 高度差 {brain.Owner.Position.z - center.z:F1}m"));
                      
                    continue;
                }
                if (exclude != null && exclude.Contains(brain.Owner)) continue;

                // 视线过滤：requireSight 时跳过看不见玩家的 NPC
                if (requireSight && !NpcSightSystem.CanNpcSeePlayer(brain.Owner))
                {
                    if (IsDebugMode)
                        DebugLogger.Log($"{brain.Owner.Name} 看不见玩家，跳过广播 '{eventType}'");
                    continue;
                }

                brainsInRange.Add(brain);
                witnesses.Add(brain.Owner);
                if (IsDebugMode)
                    DebugLogger.Log($"witnesses.Name: {brain.Owner.Name}  index {brain.Owner.Index}");
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
                    brainsInRange.Remove(GetBrainForAgent(criminal));
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
                                // 约定：Args[0]=Hero, Args[1]=Pos(Vec3), Args[2]=LookDir(Vec2)
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

        /// <summary>
        /// 计算从 from 到 to 的罗盘方位（中文八方向）
        /// </summary>
        private static string GetDirectionFromTo(Vec3 from, Vec3 to)
        {
            float dx = to.X - from.X;
            float dy = to.Y - from.Y;
            float angle = MathF.Atan2(dx, dy) * (180f / MathF.PI); // 0° = 北, 顺时针
            if (angle < 0) angle += 360f;

            // 罗盘八方向本地化：北（英文 N）
            if (angle < 22.5f || angle >= 337.5f) return LWNTextHelper.ResolveText("LWN_ai_dir_north", "N");
            // 罗盘八方向本地化：东北（英文 NE）
            if (angle < 67.5f) return LWNTextHelper.ResolveText("LWN_ai_dir_northeast", "NE");
            // 罗盘八方向本地化：东（英文 E）
            if (angle < 112.5f) return LWNTextHelper.ResolveText("LWN_ai_dir_east", "E");
            // 罗盘八方向本地化：东南（英文 SE）
            if (angle < 157.5f) return LWNTextHelper.ResolveText("LWN_ai_dir_southeast", "SE");
            // 罗盘八方向本地化：南（英文 S）
            if (angle < 202.5f) return LWNTextHelper.ResolveText("LWN_ai_dir_south", "S");
            // 罗盘八方向本地化：西南（英文 SW）
            if (angle < 247.5f) return LWNTextHelper.ResolveText("LWN_ai_dir_southwest", "SW");
            // 罗盘八方向本地化：西（英文 W）
            if (angle < 292.5f) return LWNTextHelper.ResolveText("LWN_ai_dir_west", "W");
            // 罗盘八方向本地化：西北（英文 NW）
            return LWNTextHelper.ResolveText("LWN_ai_dir_northwest", "NW");
        }
    }
}
