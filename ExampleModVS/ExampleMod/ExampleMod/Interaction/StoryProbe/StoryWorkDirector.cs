using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// P4（2026-11-15）工作点：占用、走位（经 Brain 的真实移动）、工作动作、局部让位。
    ///
    /// 🔴 纪律：
    ///   · 走位只发 `event_story_move`，由 Brain 入队 MoveToPositionAction；**不瞬移**，超时/中断回 false。
    ///   · 只有“正在跟这个 NPC 说话”才让位；别人聊天时其它工人继续。
    ///   · 占用按 WorkPoint 计数，MaxOccupants 到了就拒绝；被打断/被绑/倒地立即释放。
    /// </summary>
    public sealed class StoryWorkDirector
    {
        private sealed class Slot
        {
            public string ActorId;
            public string PointId;
            public string Action;
            public Vec3 Pos;
            public int MaxOccupants = 1;
            public float Tolerance = 0.6f;
            public PerformanceAction Lease;
            public string State = "idle";
            public string Reason = "";
            public float NextTryAt;
            public float NextActionAt;
            public bool MovePending;
        }

        private readonly StoryProbeMissionBehavior _host;
        private readonly Dictionary<string, Slot> _slots = new Dictionary<string, Slot>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _occupancy = new Dictionary<string, int>(StringComparer.Ordinal);
        private bool _started;
        private bool _stopped;
        private string _reason = "not started";

        public StoryWorkDirector(StoryProbeMissionBehavior host) { _host = host; }

        public bool Begin(string stageId)
        {
            _slots.Clear();
            _occupancy.Clear();
            _started = false;
            _stopped = false;
            _reason = "starting";
            if (!StoryV2Data.EnsurePublished())
            {
                _reason = "no published v2 data: " + StoryV2Data.Report;
                return false;
            }
            var file = StoryLayoutRegistry.Published;
            if (file == null || !file.Stages.TryGetValue(stageId, out var stage) || stage == null)
            {
                _reason = "stage missing: " + stageId;
                return false;
            }
            if (!file.Layouts.TryGetValue(stage.Layout, out var layout) || layout == null)
            {
                _reason = "layout missing: " + stage.Layout;
                return false;
            }

            foreach (var standing in layout.Standing)
            {
                if (standing == null || string.IsNullOrEmpty(standing.WorkPoint)) continue;
                var wp = layout.WorkPoints.FirstOrDefault(w => w != null && w.Id == standing.WorkPoint);
                if (wp == null) continue;
                var prop = layout.FindProp(wp.Prop);
                if (prop == null) { _reason += "; prop missing for " + wp.Id; continue; }
                if (_host.Prep == null || !_host.Prep.TryGetPropEntity(prop.Id, out var entity) || entity == null)
                {
                    _reason += "; prop entity missing for " + wp.Id;
                    continue;
                }
                _slots[standing.Actor] = new Slot
                {
                    ActorId = standing.Actor,
                    PointId = wp.Id,
                    Action = wp.Action,
                    Pos = StoryPrepRunner.WorkPos(entity, prop, wp),
                    MaxOccupants = Math.Max(1, wp.MaxOccupants),
                    Tolerance = Math.Max(0.05f, wp.ArriveTolerance),
                    State = "idle",
                };
            }
            _started = _slots.Count > 0;
            _reason = _started ? "ok slots=" + _slots.Count : "no work slots";
            DebugLogger.Log("[StoryWork] begin stage=" + stageId + " -> " + _reason);
            return _started;
        }

        public void Tick(float dt)
        {
            if (!_started || _stopped) return;
            var now = _host.Clock;
            string talkingNpc = ResolveConversationNpc();

            foreach (var slot in _slots.Values)
            {
                var agent = _host.GetAgent(slot.ActorId);
                if (agent == null || !AgentControlHelper.SafeIsActive(agent) || agent.Health <= 0f)
                {
                    Release(slot, "missing", now);
                    slot.State = "missing";
                    continue;
                }

                bool physicallyBlocked = AgentBrain.IsRoped(agent) || AgentBrain.IsHeldByStatus(agent)
                                         || AgentBrain.IsKnockedOut(agent);
                var brain = AgentAIController.GetBrainForAgent(agent);
                bool busy = brain != null && brain.IsInCombat;
                if (physicallyBlocked || busy)
                {
                    Release(slot, physicallyBlocked ? "status-held" : "in-combat", now);
                    slot.State = "blocked";
                    slot.Reason = physicallyBlocked ? "bound/held/downed" : "in combat";
                    slot.NextTryAt = now + 2f;
                    continue;
                }

                // 局部让位：只暂停正在跟这个人说话的工作；别人聊天不影响。
                if (!string.IsNullOrEmpty(talkingNpc) && talkingNpc == slot.ActorId)
                {
                    Release(slot, "conversation", now);
                    slot.State = "paused";
                    slot.Reason = "in conversation";
                    continue;
                }

                // 群组事件只让参与者让位；非参与者的工作继续。
                if (_host.Director?.IsEventParticipant(slot.ActorId) == true)
                {
                    Release(slot, "group event", now);
                    slot.State = "paused";
                    slot.Reason = "in group event";
                    continue;
                }

                if (slot.State == "paused" && string.IsNullOrEmpty(talkingNpc))
                {
                    slot.State = "idle";
                    slot.Reason = "";
                    slot.NextTryAt = 0f;
                }

                float dist = agent.Position.Distance(slot.Pos);

                if (slot.Lease == null)
                {
                    if (slot.MovePending || now < slot.NextTryAt) continue;

                    if (dist > slot.Tolerance)
                    {
                        slot.MovePending = true;
                        slot.State = "moving";
                        slot.Reason = "walking " + dist.ToString("F1") + "m";
                        var owner = slot;
                        try
                        {
                            AgentAIController.Instance?.SendEventToAgent(agent, "event_story_move",
                                owner.Pos, owner.Tolerance * 0.5f, new Action<bool>(ok =>
                                {
                                    owner.MovePending = false;
                                    if (ok) { owner.State = "idle"; owner.NextTryAt = 0f; owner.Reason = "arrived"; }
                                    else { owner.State = "idle"; owner.NextTryAt = _host.Clock + 3f; owner.Reason = "move failed/timeout"; }
                                }));
                        }
                        catch (Exception ex)
                        {
                            slot.MovePending = false;
                            slot.State = "idle";
                            slot.Reason = "move request failed: " + ex.Message;
                            slot.NextTryAt = now + 3f;
                        }
                        continue;
                    }

                    if (Occupied(slot.PointId) >= slot.MaxOccupants)
                    {
                        slot.State = "blocked";
                        slot.Reason = "workpoint occupied";
                        slot.NextTryAt = now + 1f;
                        continue;
                    }

                    var lease = PerformanceAction.TryAcquire(agent, "story-work-" + slot.PointId, r =>
                    {
                        // 被战斗/捆缚/对话打断：租约自己收走，下一轮按状态恢复。
                        slot.Lease = null;
                        slot.State = "idle";
                        slot.NextTryAt = _host.Clock + 1f;
                        slot.Reason = "lease lost: " + r;
                    });
                    if (lease == null)
                    {
                        slot.State = "blocked";
                        slot.Reason = "lease refused";
                        slot.NextTryAt = now + 2f;
                        continue;
                    }
                    slot.Lease = lease;
                    AddOccupant(slot.PointId, 1);
                    slot.State = "working";
                    slot.Reason = "working at " + slot.PointId;
                    PlayWorkAction(agent, slot);
                    slot.NextActionAt = now + 8f;
                    continue;
                }

                if (slot.Lease.Ended || !slot.Lease.CanControl)
                {
                    Release(slot, "lease ended", now);
                    slot.State = "idle";
                    slot.NextTryAt = now + 1f;
                    continue;
                }

                if (dist > slot.Tolerance * 3f)
                {
                    Release(slot, "drifted away", now);
                    slot.State = "idle";
                    slot.Reason = "drifted " + dist.ToString("F1") + "m";
                    slot.NextTryAt = 0f;
                    continue;
                }

                if (now >= slot.NextActionAt)
                {
                    PlayWorkAction(agent, slot);
                    slot.NextActionAt = now + 8f;
                }
            }
        }

        public void Stop(string reason)
        {
            if (_stopped) return;
            _stopped = true;
            foreach (var slot in _slots.Values) Release(slot, reason ?? "stop", _host.Clock);
            DebugLogger.Log("[StoryWork] stopped: " + (reason ?? "-"));
        }

        public bool ForceMove(string actorId)
        {
            if (string.IsNullOrEmpty(actorId) || !_slots.TryGetValue(actorId, out var slot)) return false;
            Release(slot, "force", _host.Clock);
            slot.State = "idle";
            slot.Reason = "forced";
            slot.NextTryAt = 0f;
            slot.MovePending = false;
            return true;
        }

        /// <summary>把某个工作槽的身体让出来（对话/群组事件要用），事件结束后自动恢复。</summary>
        public bool YieldActor(string actorId, string reason)
        {
            if (string.IsNullOrEmpty(actorId) || !_slots.TryGetValue(actorId, out var slot)) return false;
            Release(slot, reason, _host.Clock);
            slot.State = "paused";
            slot.Reason = reason;
            slot.NextTryAt = _host.Clock + 1f;
            return true;
        }

        public bool HasSlots => _started && _slots.Count > 0;
        public bool IsWorkActor(string actorId)
            => !string.IsNullOrEmpty(actorId) && _started && _slots.ContainsKey(actorId);

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append(_started ? "OK: work" : "FAILED: work").Append(" started=").Append(_started)
              .Append(" stopped=").Append(_stopped).Append(" slots=").Append(_slots.Count)
              .Append(" reason=").Append(_reason);
            foreach (var slot in _slots.Values.OrderBy(s => s.ActorId, StringComparer.Ordinal))
            {
                var agent = _host.GetAgent(slot.ActorId);
                float dist = agent != null ? agent.Position.Distance(slot.Pos) : -1f;
                sb.Append("\n  ").Append(slot.ActorId).Append(" @").Append(slot.PointId)
                  .Append(" state=").Append(slot.State)
                  .Append(" dist=").Append(dist < 0 ? "-" : dist.ToString("F1"))
                  .Append(" occupants=").Append(Occupied(slot.PointId)).Append("/").Append(slot.MaxOccupants)
                  .Append(" action=").Append(slot.Action ?? "-");
                if (!string.IsNullOrEmpty(slot.Reason)) sb.Append(" reason=").Append(slot.Reason);
            }
            return sb.ToString();
        }

        private int Occupied(string pointId)
            => _occupancy.TryGetValue(pointId, out int n) ? n : 0;

        private void AddOccupant(string pointId, int delta)
        {
            int n = Math.Max(0, Occupied(pointId) + delta);
            if (n == 0) _occupancy.Remove(pointId);
            else _occupancy[pointId] = n;
        }

        private void Release(Slot slot, string reason, float now)
        {
            if (slot?.Lease == null) return;
            try { slot.Lease.Release(reason); } catch { }
            slot.Lease = null;
            AddOccupant(slot.PointId, -1);
            slot.NextActionAt = 0f;
        }

        private void PlayWorkAction(Agent agent, Slot slot)
        {
            if (agent == null || string.IsNullOrEmpty(slot.Action)) return;
            try { AgentControlHelper.ForcePlayAction(agent, slot.Action, restoreAfter: false); }
            catch (Exception ex) { DebugLogger.Log("[StoryWork] action failed: " + ex.Message); }
        }

        private static string ResolveConversationNpc()
        {
            try
            {
                var cm = Campaign.Current?.ConversationManager;
                if (cm == null || !cm.IsConversationInProgress) return null;
                return StoryProbeDialogue.ResolveNpcId(cm.OneToOneConversationAgent as Agent);
            }
            catch { return null; }
        }
    }
}
