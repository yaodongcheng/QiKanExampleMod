using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// **角色状态（AgentStatus，2026-10-20 立）** —— 挂在某个 agent 身上的**特殊状态**：
    /// 一个状态机负责姿势，本类统一负责"按住他"与两个出口。
    ///
    /// **第一个实例（也是当前唯一一个）= 倒地（downed）**：处决交互演完，人就躺在地上（躺地循环），
    /// 等起身或补刀。以后新增状态（眩晕 / 中毒 / 打坐……）只需要给 <see cref="TryApply"/> 传不同的
    /// `statusId` 和一个封装好 Enter/Exit 事件的驱动，**不需要再写一个新的 MissionLogic**。
    ///
    /// 🔴 **为什么状态不能归双人动作管**：`PairAction` 描述的是**两个人之间的交互**，交互演完就解散；
    /// 状态是**一个人**的事。让 pair 一直持有状态会产生假占用（`attacker/defender is busy`）——
    /// 处决完一个人后既不能再处决别人，躺着的受害者也被这一对锁住。
    ///
    /// 一个状态 = 三件事（都归这里）：
    ///   ① 状态机驱动（每帧 Tick，例如 `executed.xml` 的躺地循环）；
    ///   ② 按住的 AI / 玩家控制（状态期间不乱跑；玩家则冻结控制器）；
    ///   ③ 出口：<see cref="RequestEnd"/>（演退出 clip 后放人）/ <see cref="RequestKill"/>（不演退出、直接结算）。
    /// 脑侧另有 <see cref="StatusHoldAction"/> 占位（拒事件 / 拒清队列 / 拒参战），两半分工见该类注释。
    ///
    /// 🔴 与 <see cref="PairActionLogic"/> 同一个引擎坑：本类也是在 SubModule 的
    /// `OnMissionBehaviorInitialize` 里 `AddMissionBehavior` 挂上的 ⇒ **拿不到 `OnBehaviorInitialize`**
    /// （反编译 `Mission.Initialize` / `AddMissionBehavior` 实锤）⇒ `Current` 必须在 `OnCreated` 里设。
    /// </summary>
    public sealed class AgentStatus : MissionLogic
    {
        public static AgentStatus Current { get; private set; }

        /// <summary>退出动画的超时兜底（秒）：动画查不到/卡住时也要把人放出来。</summary>
        private const float EndTimeoutSeconds = 6f;

        private sealed class Entry
        {
            public Agent Agent;
            public string StatusId;       // 状态名（当前只有 "downed"；以后可有更多）
            public IPairSideDriver Driver;
            public PairControlLease Lease;
            public string SourceId;       // 来源（当前 = pair id，便于排查）
            public float HeldFor;
            public bool Ending;
            public float EndElapsed;
        }

        private readonly List<Entry> _active = new List<Entry>();

        // ───────────────────────────── 生命周期 ─────────────────────────────

        public override void OnCreated()
        {
            base.OnCreated();
            Current = this;
        }

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            Current = this;
        }

        protected override void OnEndMission()
        {
            // 🔴 Mission 卸载期不再碰 agent API；驱动 / 租约随场景一起作废。
            _active.Clear();
            if (ReferenceEquals(Current, this))
                Current = null;
            base.OnEndMission();
        }

        // ───────────────────────────── 挂上 / 查询 ─────────────────────────────

        /// <summary>
        /// 给一个人**挂上一个状态**（当前由 pair 在交互结束那一帧调用）。
        /// <paramref name="statusId"/> = 状态名（"downed"）；<paramref name="driver"/> 封装了进出动画；
        /// <paramref name="lease"/> = 从 pair 租约里拆出来的那一条（状态期间的 AI 暂停 / 玩家冻结）。
        /// </summary>
        public bool TryApply(Agent agent, string statusId, IPairSideDriver driver,
            PairControlLease lease, string sourceId, out string why)
        {
            why = "-";
            if (agent == null || !AgentControlHelper.SafeIsActive(agent))
            {
                why = "agent is null / inactive";
                return false;
            }
            if (driver == null)
            {
                why = "driver is null";
                return false;
            }
            if (IsInStatus(agent))
            {
                why = $"agent is already in status '{StatusIdOf(agent)}'";
                return false;
            }

            _active.Add(new Entry
            {
                Agent = agent,
                StatusId = string.IsNullOrWhiteSpace(statusId) ? "status" : statusId,
                Driver = driver,
                Lease = lease,
                SourceId = sourceId,
                HeldFor = 0f
            });

            // 🔴 **脑侧占位**（NPC 才有；玩家没有 brain）：先立标记、再入队 StatusHoldAction ——
            //    与 GrappleBind 的 MarkTargetBound 同序，中间不允许脑处理任何事件。
            //    不做这一步 = 脑空窗期把控制权还回原生 AI（人爬起来）、被打就入队战斗。
            try
            {
                AgentBrain brain = AgentAIController.GetBrainForAgent(agent);
                if (brain != null)
                {
                    brain.IsStatusHeld = true;
                    brain.EnqueuePlanAction(new StatusHoldAction());
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Status] 脑占位入队失败：{ex.GetType().Name}: {ex.Message}");
            }

            DebugLogger.Log($"[Status] 挂上状态 '{statusId}'：'{PairActionPose.Describe(agent)}'（来自 '{sourceId}'）");
            return true;
        }

        public bool IsInStatus(Agent agent) => Find(agent) != null;

        /// <summary>这个 agent 当前的状态名（不在状态里 = null）。</summary>
        public string StatusIdOf(Agent agent) => Find(agent)?.StatusId;

        /// <summary>最近一个还处于状态的 agent（命令没瞄到人时的兜底目标）。</summary>
        public Agent MostRecent()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Agent a = _active[i].Agent;
                if (a != null && AgentControlHelper.SafeIsActive(a))
                    return a;
            }
            return null;
        }

        // ───────────────────────────── 每帧 ─────────────────────────────

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (_active.Count == 0)
                return;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Entry e = _active[i];
                try
                {
                    if (e.Agent == null || !AgentControlHelper.SafeIsActive(e.Agent))
                    {
                        // 被别人打死 / 场景移除：只归还我们拿的控制权。
                        ReleaseEntry(e);
                        _active.RemoveAt(i);
                        continue;
                    }

                    e.HeldFor += dt;
                    e.Driver.Tick(e.Agent, dt);

                    if (!e.Ending)
                        continue;

                    e.EndElapsed += dt;
                    if (e.Driver.IsFinished(e.Agent) || e.EndElapsed > EndTimeoutSeconds)
                    {
                        DebugLogger.Log($"[Status] 状态 '{e.StatusId}' 退出结束：'{PairActionPose.Describe(e.Agent)}'");
                        ReleaseEntry(e);
                        _active.RemoveAt(i);
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[Status] Tick 异常：{ex.GetType().Name}: {ex.Message}");
                    ReleaseEntry(e);
                    _active.RemoveAt(i);
                }
            }
        }

        // ───────────────────────────── 两个出口 ─────────────────────────────

        /// <summary>结束状态：演退出 clip → 演完归还控制。false = 他不在状态里。</summary>
        public bool RequestEnd(Agent agent)
        {
            Entry e = Find(agent);
            if (e == null)
                return false;
            if (e.Ending)
                return true;

            e.Ending = true;
            e.EndElapsed = 0f;
            try { e.Driver.RequestExit(e.Agent); } catch (Exception) { }
            DebugLogger.Log($"[Status] 结束状态 '{e.StatusId}'：'{PairActionPose.Describe(agent)}'");
            return true;
        }

        /// <summary>**强制收尾**（不演退出 clip）：直接结算死亡。当前只给"倒地补刀"用。</summary>
        public bool RequestKill(Agent agent, string reason)
        {
            Entry e = Find(agent);
            if (e == null)
                return false;

            DebugLogger.Log($"[Status] 强制收尾（{reason}）：'{PairActionPose.Describe(agent)}'");
            PairActionInstance.KillAgent(agent, "status:" + reason);
            // 人已经死了 ⇒ 不必再清脑队列（脑会随 agent 一起销毁）。
            ReleaseEntry(e, clearBrain: false);
            _active.Remove(e);
            return true;
        }

        // ───────────────────────────── 诊断 ─────────────────────────────

        /// <summary>命令 `custom.pair state` 用的一段摘要（空 = 没有任何状态）。</summary>
        public string DescribeAll()
        {
            if (_active.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            sb.Append("status: ").Append(_active.Count);
            foreach (Entry e in _active)
            {
                sb.Append('\n').Append("  '").Append(PairActionPose.Describe(e.Agent)).Append("'")
                  .Append(" [").Append(e.StatusId).Append("]")
                  .Append(" from ").Append(e.SourceId)
                  .Append(e.Ending ? " | ending" : " | held")
                  .Append(" (").Append(e.HeldFor.ToString("F1")).Append("s)");
            }
            return sb.ToString();
        }

        // ───────────────────────────── 内部 ─────────────────────────────

        private Entry Find(Agent agent)
        {
            if (agent == null)
                return null;
            foreach (Entry e in _active)
            {
                if (ReferenceEquals(e.Agent, agent))
                    return e;
            }
            return null;
        }

        /// <summary>
        /// 收尾：**先清脑标记、再清动作**（与 GrappleBind 的放人顺序一致，否则 ClearAllActions 会被拒），
        /// 然后归还驱动与租约。
        /// </summary>
        private static void ReleaseEntry(Entry e, bool clearBrain = true)
        {
            if (clearBrain && e.Agent != null && AgentControlHelper.SafeIsActive(e.Agent))
            {
                try
                {
                    AgentBrain brain = AgentAIController.GetBrainForAgent(e.Agent);
                    if (brain != null)
                    {
                        brain.IsStatusHeld = false;   // 🔴 必须先清标记，否则下面会被 ClearAllActions 拒掉
                        brain.ClearAllActions();
                    }
                }
                catch (Exception) { }
            }
            try { e.Driver?.Release(e.Agent); } catch (Exception) { }
            try { e.Lease?.ReleaseAll(); } catch (Exception) { }
        }
    }
}
