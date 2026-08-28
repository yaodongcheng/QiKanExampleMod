using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SandBox;
using SandBox.Missions.AgentBehaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
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

        // 🔴 2026-08-12：玩家武器状态（事件源维护）——Agent.OnMainAgentWieldedItemChange 只在玩家
        // 武器切换时触发（引擎内 IsMainAgent 门控），NPC 感知/停战检测读本状态，不再每帧查武器。
        public bool PlayerWeaponDrawn;             // 玩家主副手任一持械（拔刀=武器出鞘）
        public float PlayerWeaponStateChangeTime;  // Mission 时间（最近一次拔刀/收刀时刻）
        private bool _weaponEventHooked;
        private bool _playerWeaponEventInited;     // 挂载时初始化一次状态（防首帧误判收刀）

        // ═══════════════════════════════════════════════════════════════
        // 🆕 PendingWorldEvent — Mission 作用域犯罪记录
        // ═══════════════════════════════════════════════════════════════

        // 🔴 2026-08-16（方案 G3②）：在场随从缓存名单（纯 C#，无 Agent native 引用）——
        // Mission 期间维护（OnAgentCreated 补录 + OnAgentDeleted 移除），OnRemoveBehavior 阶段
        // 禁止访问 Agent native（项目纪律），犯罪评论只读此缓存挑在场随从。
        private readonly List<Hero> _presentPartyMembers = new List<Hero>();

        /// <summary>在场随从缓存快照（只读引用；G3② 犯罪评论 memberFilter 用）。</summary>
        public IReadOnlyList<Hero> PresentPartyMembers => _presentPartyMembers;
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
            //    启动完成后给玩家队友（同伴/同部队）补设 Leader = Agent.Main，并补 NpcSightSystem
            //    追踪注册——OnAgentCreated 可能因 Agent.Main 未就绪 / SightSystem 未初始化而漏注册
            //    （2026-08-14 实机：随从同队却没进 TrackedTargets，感知侧永远不触发）。
            //    RegisterTrackedTarget 自带防重复，重跑安全。 ──
            foreach (var kv in _brains)
            {
                var b = kv.Value;
                if (Agent.Main == null || !FriendlinessHelper.IsPlayerPartyMember(b.Owner)) continue;
                if (b.Leader == null)
                    b.SetLeader(Agent.Main);
                // 🔴 2026-08-28（玩家日志刷屏修复）：注册侧同款守卫（理由同 OnAgentCreated 注）——
                // 仅 RegisterTrackedTarget 一行加框，SetLeader / _presentPartyMembers 补录照常，勿一并跳过。
                if (!Settings.Instance.IsInteractionDisabled())
                    NpcSightSystem.Instance?.RegisterTrackedTarget(b.Owner, 15f, 50f);
                // 🔴 2026-08-16（方案 G3②）：在场随从缓存补录（OnAgentCreated 时 Agent.Main 未就绪漏掉的）
                var ph = (b.Owner.Character as CharacterObject)?.HeroObject;
                if (ph != null && !_presentPartyMembers.Contains(ph))
                    _presentPartyMembers.Add(ph);
                if (IsDebugMode)
                    DebugLogger.Log($"[随从关系-兜底] {b.Owner.Name} → Leader=玩家");
            }

            // ── PendingWorldEvent 初始化 ──
            // 非战役模式（自定义战斗等）无 Campaign：Settlement.CurrentSettlement 的
            // getter 内部直接访问 MobileParty.MainParty，无 null 保护会抛 NRE，
            // 先判 Campaign.Current（兜底，正常流程已被 MySubModule 注册闸门挡住）
            var settlement = Campaign.Current == null ? null : (Settlement.CurrentSettlement ?? Hero.MainHero?.CurrentSettlement);
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
                    //if (observer == null || !AgentControlHelper.SafeIsActive(observer)) return;
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
            // 🔴 玩家本人永不注册 brain（根因防线）：Tick 有 Agent.Main 守卫但 ReceiveEvent 没有——
            // 玩家被打时护主/参战链会把玩家当 NPC：BubbleSay NPC 台词 + ClearAllActions + EnqueueAction
            // + SuspendVanillaAI（禁用玩家 DailyBehaviorGroup），且 Suspend 永不撤销 → 整场 Mission 无法移动。
            if (agent.IsMainAgent) return;

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
                    if (Agent.Main != null && FriendlinessHelper.IsPlayerPartyMember(agent))
                    {
                        _brains[agent.Index].SetLeader(Agent.Main);
                        // 🔴 2026-08-14（正规路线）：随从注册进 NpcSightSystem 追踪（与玩家同列），
                        // AgentBrain 蹲姿感知遍历 TrackedTargets 读随从脑 CrouchPoseActive——
                        // sight 职责统一归 NpcSightSystem，不搞每操作一个缓存列表。
                        // 🔴 2026-08-28（玩家日志刷屏修复）：注册侧加同款守卫——IsInteractionDisabled
                        // （战场 Battle/Deployment 等）时 NpcSightSystem.tick 已冻结，注册却无框：
                        // 战场 200+ 队伍兵全注册进 TrackedTargets，战斗结束 OnAgentDeleted 逐个注销
                        // → [SightTrack] 注销追踪逐条刷屏。非禁用场景（城镇等）行为不变，随从照常注册。
                        if (!Settings.Instance.IsInteractionDisabled())
                            NpcSightSystem.Instance?.RegisterTrackedTarget(agent, 15f, 50f);
                        if (IsDebugMode)
                            DebugLogger.Log($"[随从关系] {agent.Name} → Leader=玩家（身份判定自动建立）");
                    }
                    // 🔴 2026-08-16（方案 G3②）：在场随从缓存补录（纯 C# 名单，OnRemoveBehavior 犯罪评论用）
                    if (FriendlinessHelper.IsPlayerPartyMember(agent))
                    {
                        var hero = (agent.Character as CharacterObject)?.HeroObject;
                        if (hero != null && !_presentPartyMembers.Contains(hero))
                            _presentPartyMembers.Add(hero);
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
            // 🔴 2026-08-26 击杀崩溃修复（玩家反馈 10:47:45）：brain 移除最先执行——
            // 引擎 Mission.OnAgentDeleted 的 foreach(MissionBehaviors) 回调链无异常保护，
            // 前序 behavior 或下方任一清理步骤抛异常都会跳过/中断本方法 → brain 残留 →
            // 下一帧 OnMissionTick 对已销毁 Agent 调 IsActive()（解引用清零的 native 指针）= AV。
            try
            {
                if (_brains.TryGetValue(agent.Index, out var brain))
                {
                    if (IsDebugMode)
                        DebugLogger.Log($"因为删除 移除一个Agent的大脑 name {agent.Name} index{agent.Index} 当前总数{_brains.Count}");
                    brain.OnOwnerDeleted();
                    _brains.Remove(agent.Index);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[AIController] OnAgentDeleted 移除 brain 异常: {ex.Message}");
            }
            try
            {
                // 说话并联通道清理（M0：agent 删除 → 注册表移除）
                SpeechChannel.Remove(agent);
                // 🔴 2026-08-14：随从追踪注销（视线系统里玩家之外的注册目标）
                if (agent != Agent.Main)
                    NpcSightSystem.Instance?.UnregisterTrackedTarget(agent);
                // 🔴 2026-08-16（方案 G3②）：在场随从缓存移除（防把中途离场的随从算在场）
                var delHero = (agent.Character as CharacterObject)?.HeroObject;
                if (delHero != null)
                    _presentPartyMembers.Remove(delHero);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[AIController] OnAgentDeleted 清理异常: {ex.Message}");
            }
        }
        public override void OnMissionTick(float dt)
        {
            // 🔴 2026-08-12：玩家武器切换事件源（lazy 挂载，防 Agent.Main 未就绪）——
            // 拔刀/收刀全局状态 + 单条日志，替代 112 个 NPC 各自每帧翻转检测
            EnsurePlayerWeaponHook();

            // 🔴 2026-08-26 击杀崩溃修复（玩家反馈 10:47:45，栈：OnMissionTick→AgentBrain.Tick→AV）：
            // AgentControlHelper.SafeIsActive(Agent) = AgentHelper.GetAgentState(_statePointer)（unsafe 解引用 native 指针）。
            // 引擎销毁 Agent 时 Clear() 将 _statePointer 清零 → 残留 brain 再调 IsActive() = 解引用 0 指针 = AccessViolation
            // （致命异常，try/catch 抓不住，只能预防）。残留来源：引擎 OnAgentDeleted 回调链被异常中断，LWN 未执行移除。
            // 防御三层：① 判活用托管字段 owner.Mission（Clear() 同步置 null，读托管零 AV 风险；尸体阶段 Mission 仍有效，
            //            IsActive() 返回 false 时指针安全，照常跳过 Tick）；
            //          ② 检测到已销毁的残留 brain → 遍历后延迟移除（正常路径由 OnAgentDeleted 移除，这里只兜底）；
            //          ③ 单脑 Tick 包 try/catch，托管异常不中断整帧（AV 已被①拦截，不会走到这里）。
            List<int> staleBrains = null;
            foreach (var brain in _brains.Values)
            {
                var owner = brain?.Owner;
                if (owner == null || owner.Mission != Mission.Current)
                {
                    // 引擎已销毁该 Agent（Clear 已执行）但 brain 残留 → 兜底移除（不碰 native）
                    if (owner != null)
                        (staleBrains ??= new List<int>()).Add(owner.Index);
                    continue;
                }
                if (!AgentControlHelper.SafeIsActive(owner)) continue;   // 尸体/昏迷阶段：native 仍存活，IsActive() 安全
                try
                {
                    brain.Tick(dt);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[AIController] brain.Tick 异常 ({owner.Name}): {ex.Message}");
                }
            }
            if (staleBrains != null)
            {
                foreach (var idx in staleBrains)
                {
                    if (_brains.Remove(idx))
                        DebugLogger.Log($"[AIController] 兜底移除已销毁 Agent 的残留 brain (Index={idx})");
                }
            }

            // 密谋命令系统：执行器统一驱动（与 brain 队列解耦，收尾报告流程也在此推进）
            PlanExecutor.TickAll(dt);
            // 密谋命令系统：ReactiveAgent 实时回应结果消费（BC-006：respond 的 LLM 台词主线程播放）
            ReactiveAgent.TickAll(dt);
            // 🔴 2026-08-11 续话器：活跃对话的续话/中止策略调度（SocialSlot 威胁/NPC 闲聊跟进）
            DialogueComponent.TickContinuations(dt);
            // 🔴 M0 说话并联通道：气泡队列推进（与动作队列完全并联，不占 CurrentAction）
            SpeechChannel.TickAll(dt);
        }

        // 🔴 2026-08-12：玩家武器切换事件源（lazy 挂载：Agent.Main 就绪后一次性挂 OnMainAgentWieldedItemChange——
        // 引擎只在主玩家武器切换时触发，无每帧轮询；状态供 AgentBrain 感知 + FightEnemyAction 停战检测读取）
        private void EnsurePlayerWeaponHook()
        {
            if (_weaponEventHooked) return;
            var main = Agent.Main;
            if (main == null) return;
            main.OnMainAgentWieldedItemChange += OnPlayerWeaponChanged;
            _weaponEventHooked = true;
            // 挂载时同步一次初始状态（事件未触发过，但玩家可能已经拔着刀）
            OnPlayerWeaponChanged();
        }

        private void OnPlayerWeaponChanged()
        {
            try
            {
                var main = Agent.Main;
                if (main == null) return;
                bool drawn = V.MainWpn(main) != EquipmentIndex.None
                    || V.OffWpn(main) != EquipmentIndex.None;
                if (_playerWeaponEventInited && drawn == PlayerWeaponDrawn) return;   // 防御重复触发
                _playerWeaponEventInited = true;
                PlayerWeaponDrawn = drawn;
                PlayerWeaponStateChangeTime = Mission.Current?.CurrentTime ?? 0f;
                DebugLogger.Log($"[PlayerWeapon] 玩家{(drawn ? "拔刀" : "收刀")} (Mission t={PlayerWeaponStateChangeTime:F2})");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PlayerWeapon] 事件处理异常: {ex.Message}");
            }
        }

        public override void OnRemoveBehavior()
        {
            // 🔴 2026-08-16（方案 G3②）：犯罪主动评论（40%，Mission 销毁前）——只读 C# 缓存
            //（_presentPartyMembers），OnRemoveBehavior 阶段禁止访问 Agent native（项目纪律）。
            // 顺序在 FinalizePendingWorldEvent 之前：有目击犯罪才评论（PendingWorldEvent 激活过）。
            TryCommentCrime();
            // 密谋命令系统：Mission 结束 → 执行器统一收尾（OnMissionScreenFinalize 兜底纪律）
            PlanExecutor.ShutdownAll();
            FinalizePendingWorldEvent();
            // 🔴 Phase E（2026-08-14 重构）：被捕随从转押已迁到 AttackTriggerMissionLogic
            //（heroId 逮捕瞬间缓存 + Mission 结束只读大地图数据，零 teardown 期 Agent native 访问）。
            CombatManager.OnMissionEnd();
            // 🔴 2026-08-12：解挂玩家武器切换监听（实例随 Mission 销毁，防悬空引用）
            try { if (Agent.Main != null) Agent.Main.OnMainAgentWieldedItemChange -= OnPlayerWeaponChanged; } catch { }
            _weaponEventHooked = false;
            // 🔴 §5.7 附近频道：Mission 结束消息流归档（重进场景新流，agent.Index 身份不复用）
            NearbyFeed.Clear();
            // 🔴 2026-08-11 续话器：Mission 结束清理活跃对话（OnEnd 收尾）
            DialogueComponent.ClearContinuations();
            // 🔴 M0 说话并联通道：Mission 结束清注册表（气泡不跨场景）
            SpeechChannel.ClearAll();
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

            // 🆕 目击者当场看到作案 → 嫌疑人 = 顶条目嫌疑犯（2026-08-14 三态单一事实源，不回落玩家）：
            //   TopSuspectAgent() 为 null（-1 玩家语义）→ 玩家（MainHero）；
            //   嫌疑犯有 Hero（玩家本人/有名随从）→ 该 Hero StringId；
            //   嫌疑犯无 Hero（模板随从）→ 显式 unknown（"" 哨兵，TransitionStage 跳过 InferSuspect，不怪玩家）。
            // 不管之前是什么阶段（Dormant/Emerging），有人亲眼看见 → 嫌疑人明确，直接 Active。
            if (pending.Stage < EventStage.Active)
            {
                string suspectId;
                var suspectAgent = brain?.TopSuspectAgent();
                if (suspectAgent == null)
                    suspectId = Hero.MainHero?.StringId;   // -1 = 玩家语义
                else
                    suspectId = (suspectAgent.Character as CharacterObject)?.HeroObject?.StringId ?? "";  // 无名随从 = unknown
                WorldEventStore.TransitionStage(pending, EventStage.Active, suspectId);
                DebugLogger.Log($"[RegisterWitness] {brain.Owner.Name} witnessed crime → WorldEvent {pending.EventId} Stage → Active (suspect={(suspectAgent != null ? $"{suspectAgent.Name}(Idx={suspectAgent.Index})" : "player")})");
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

        /// <summary>偷窃目击者记账（StealManager/InlineSteps 调用）：witnessHeroIds/templateWitness 来自 GetWitnesses()
        /// 🔴 纯记账——只记录赃物证词（谁看到偷了什么）。**不推进阶段、不锁定嫌疑人**（2026-08-14 嫌疑人单一事实源修正）：
        /// 案件激活与嫌疑人完全由目击者脑内警戒拉满时的 RegisterWitness 推导（TopSuspectAgent），
        /// 无人拉满（3s 抑制期内离场/友方豁免）→ 事件保持 Dormant 过夜走 Emerging 无头案，
        /// 玩家自首（ConfessIntent）是另一独立来源。证词只承担「调查资产」语义（见 FinalizePendingWorldEvent）。</summary>
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
        }

        static void AddStealAction(WorldEvent pending, string heroId, string templateId,
            string itemId, string itemName, string targetName, int count = 1)
        {
            // 合并偷窃记录：同目击者证词内同 ItemId 的 Steal 记录数量相加（gold 的 Count = 面额，同样相加）。
            // 源头合并（语义正确 + 内存瘦身）；事件合并（MergeWitnessTestimonies）与旧档产生的
            // 重复记录由序列化前的 CompactItemRecordsForSerialize 兜底归一。

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

            // 同种赃物合并（仅限有 ItemId 的赃物记录；无 ItemId 的脉冲语境记录不参与合并）
            if (!string.IsNullOrEmpty(itemId))
            {
                var existing = testimony.Actions.FirstOrDefault(a =>
                    a.ActionType == "Steal" && a.ItemId == itemId);
                if (existing != null)
                {
                    existing.Count += Math.Max(1, count); // 旧档 Count=0 按 1 兜底
                    if (string.IsNullOrEmpty(existing.ItemName)) existing.ItemName = itemName;
                    return;
                }
            }
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

        /// <summary>
        /// G3② 犯罪主动评论（2026-08-16）：Mission 销毁前（OnRemoveBehavior）——
        /// 有目击犯罪（PendingWorldEvent 激活过，hasRealWitness）且 MBRandom.RandomFloat < 0.4 →
        /// BroadcastPlayerEvent("crime", desc, memberFilter: 在场名单)——只挑在场随从说话
        /// （亲历者才有资格评论犯罪细节，如"主公，我瞧见你偷了那商人的钱袋"；场外随从不参与——
        /// 无信息不编造，叙事铁律）。与 K2（当场秒级关切）互补：K=当场，G3=离场后 LLM 评论。
        /// 罪行描述复用 WorldEvent 域既有描述模板（BuildWitnessedActionDescription，不新造文案）。
        /// </summary>
        private void TryCommentCrime()
        {
            try
            {
                var pending = PendingWorldEvent;
                if (pending == null || _presentPartyMembers.Count == 0) return;
                bool hasRealWitness = pending.WitnessTestimonies?.Any(t => t != null
                    && (t.WitnessHeroId != null || t.TemplateId != null)) == true;
                if (!hasRealWitness) return;   // 无人目击 = 世界层面没发生，无评论
                if (MBRandom.RandomFloat >= 0.4f) return;
                var testimony = pending.WitnessTestimonies?.FirstOrDefault(t => t != null
                    && t.Actions != null && t.Actions.Count > 0);
                string actDesc = testimony != null
                    ? CrimeDialogueBuilder.BuildWitnessedActionDescription(testimony)
                    // 本地化：LWN_crime_witness_act_someone_stirring（玩家可见文本兜底）
                    : LWNTextHelper.ResolveText("LWN_crime_witness_act_someone_stirring", "someone was making trouble");
                string near = WorldFactProvider.NearestSettlementName(15f);
                string desc = near != null ? $"主公刚刚{actDesc}（{near}附近）" : $"主公刚刚{actDesc}";
                var present = new HashSet<string>();
                foreach (var h in _presentPartyMembers)
                    if (h != null) present.Add(h.StringId);
                ImEventBroadcaster.BroadcastPlayerEvent("crime", desc, chatComment: true,
                    memberFilter: h => h != null && present.Contains(h.StringId));
                DebugLogger.Log($"[ImEvent] 犯罪主动评论（40% 中签）: {desc}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImEvent] 犯罪评论失败: {ex.Message}");
            }
        }

        void FinalizePendingWorldEvent()
        {
            var pending = PendingWorldEvent;
            if (pending == null) return;
            var testimonies = pending.WitnessTestimonies;
            if (testimonies == null || testimonies.Count == 0) return;   // 无事发生，照丢
            // 🔴 2026-08-14 嫌疑人单一事实源修正：不再按「有目击证词」强行激活并写死玩家嫌疑人。
            // 案件激活只由目击者脑内警戒拉满时的 RegisterWitness 负责（嫌疑人从 TopSuspectAgent 推导）；
            // 无人拉满（3s 抑制期内离场 / 友方豁免）→ 事件保持 Dormant 入档，过夜由 ProcessDormant
            // 推进 Emerging 无头案（村民知道丢了东西，不知道是谁——调查引擎破案或冷案）。
            // 证词只保留「调查资产」语义：有真目击 → 调查进度直接满（目击者描述是破案线索，TryLockSuspect 走证据链）。
            bool hasRealWitness = testimonies.Any(t => t.WitnessHeroId != null || t.TemplateId != null);
            if (hasRealWitness)
            {
                pending.InvestigationProgress = 1.0f;
                pending.PublicAwareness = 0.3f;
            }

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
            BroadcastEventInRangeCore(center, radius, eventType, null, requireSight, true, args);
        }

        /// <param name="exclude">排除列表：这些 Agent 不会收到事件（如击晕受害者不参与围观）</param>
        public void BroadcastEventInRange(Vec3 center, float radius, string eventType, HashSet<Agent> exclude, bool requireSight, params object[] args)
        {
            BroadcastEventInRangeCore(center, radius, eventType, exclude, requireSight, true, args);
        }

        /// <summary>
        /// 带犯罪标记的范围广播（2026-08-13 suspect 化专用重载）：isCrime=false = 非犯罪事件
        /// （随从喊一嗓子 make_noise / NPC 投降），围观者只走行为不走 WitnessCrime 犯罪分类。
        /// 仅两个调用点使用（InlineSteps make_noise、AgentBrain NPC 投降广播）；其余广播默认犯罪。
        /// </summary>
        public void BroadcastEventInRange(Vec3 center, float radius, string eventType, HashSet<Agent> exclude, bool requireSight, bool isCrime, params object[] args)
        {
            BroadcastEventInRangeCore(center, radius, eventType, exclude, requireSight, isCrime, args);
        }

        /// <summary>广播核心实现（isCrime 透传到 WitnessCrime 舞台分配的两个 AIEvent 构造）。</summary>
        private void BroadcastEventInRangeCore(Vec3 center, float radius, string eventType, HashSet<Agent> exclude, bool requireSight, bool isCrime, params object[] args)
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
                if (!AgentControlHelper.SafeIsActive(brain.Owner) || brain.Owner == Agent.Main) continue;
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

                // 视线过滤：requireSight 时跳过看不见事件源的 NPC
                // 🔴 2026-08-14 修复：原锚点恒为玩家（CanNpcSeePlayer）——随从执行计划犯罪时，
                // 旁观者能看到犯罪现场（随从打人）却看不见远处的玩家 → 广播被误过滤，
                // 现场 NPC 收不到 WitnessCrime → 无人涨警戒（实机：阿速甘击晕那弥斯失败，无人拉满）。
                // WitnessCrime 的 args[0] 恒为事件源（犯罪者/喊叫者/投降者，全调用点约定一致），
                // 锚点改用事件源；距离用广播半径（原 15f 硬编码 < 广播 20f，边缘旁观者被误杀）。
                if (requireSight)
                {
                    bool canSeeEvent;
                    if (eventType == "WitnessCrime" && args.Length > 0 && args[0] is Agent anchor)
                        canSeeEvent = NpcSightSystem.CanAgentSeeTarget(brain.Owner, anchor, radius, 120f);
                    else
                        canSeeEvent = NpcSightSystem.CanNpcSeePlayer(brain.Owner);
                    if (!canSeeEvent)
                    {
                        if (IsDebugMode)
                            DebugLogger.Log($"{brain.Owner.Name} 看不见事件源，跳过广播 '{eventType}'");
                        continue;
                    }
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
                                    IsCrime = isCrime,
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
                                    IsCrime = isCrime,
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
                        IsCrime = isCrime,
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
