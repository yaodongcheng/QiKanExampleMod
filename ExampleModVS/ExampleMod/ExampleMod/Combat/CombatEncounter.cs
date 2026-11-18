using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>一次真实冲突的起因、知情者与结果。血量和倒地归原生伤害流程，收场归正常tick。</summary>
    public sealed class CombatEncounter
    {
        private static readonly List<CombatEncounter> Active = new List<CombatEncounter>();
        private readonly Mission _mission;
        private readonly HashSet<Agent> _beneficiaries;
        private readonly HashSet<Agent> _informed = new HashSet<Agent>();
        private readonly HashSet<Agent> _noted = new HashSet<Agent>();
        private Agent _removed;
        private bool _started, _closed, _cancelRequested, _combatEnded;
        public Agent Initiator { get; }
        public Agent Opponent { get; }
        public Action<Agent> Receipt { get; }
        public bool WoundOnDefeat { get; }
        public string ReasonKey { get; }
        public string StartLineKey { get; }
        public string EndLineKey { get; }
        public bool HasStarted => _started;

        public CombatEncounter(Agent initiator, Agent opponent, bool woundOnDefeat, string reasonKey,
            IEnumerable<Agent> beneficiaries, string startLineKey, string endLineKey, Action<Agent> receipt)
        {
            _mission = Mission.Current;
            Initiator = initiator; Opponent = opponent; WoundOnDefeat = woundOnDefeat;
            ReasonKey = reasonKey; StartLineKey = startLineKey; EndLineKey = endLineKey; Receipt = receipt;
            _beneficiaries = new HashSet<Agent>((beneficiaries ?? Enumerable.Empty<Agent>()).Where(a => a != null));
        }

        public bool Begin()
        {
            if (_closed || _started || _mission != Mission.Current || !Up(Initiator) || !Up(Opponent)) return false;
            if (Initiator.Team == null || Opponent.Team == null || !Initiator.Team.IsEnemyOf(Opponent.Team)) return false;
            _started = true;
            Active.Add(this);
            _informed.Add(Initiator); _informed.Add(Opponent);
            // 对话实际展示后的战斗请求：只有在现场能听见并看见说话者的受益者知道起因。
            foreach (var agent in _beneficiaries)
                if (Up(agent) && (NpcSightSystem.CanAgentSeeTarget(agent, Initiator, 15f, 360f)
                    || NpcSightSystem.CanAgentSeeTarget(agent, Opponent, 15f, 360f))) _informed.Add(agent);
            foreach (var agent in _informed)
                if (!string.IsNullOrEmpty(ReasonKey)) Remember(agent, Reason());
            DebugLogger.Log("[CombatEncounter] started " + Initiator.Name + " vs " + Opponent.Name
                + " wound=" + WoundOnDefeat + " reason=" + ReasonKey + " informed=" + _informed.Count);
            return true;
        }

        internal string Reason() => string.IsNullOrEmpty(ReasonKey) ? null
            : LWNTextHelper.ResolveCompound(ReasonKey, ("INITIATOR", Initiator.Name?.ToString() ?? ""),
                ("OPPONENT", Opponent.Name?.ToString() ?? ""));
        private static bool Up(Agent agent) => AgentControlHelper.SafeIsActive(agent) && agent.Health > 0f;
        private bool Pair(Agent a, Agent b) => (a == Initiator && b == Opponent) || (a == Opponent && b == Initiator);
        private static void Remember(Agent agent, string text)
        {
            try { AgentAIController.GetBrainForAgent(agent)?.RecordNarration(text); }
            catch (Exception ex) { DebugLogger.Log("[CombatEncounter] memory failed: " + ex.Message); }
        }

        public void Cancel()
        {
            if (_closed) return;
            // 动作OnEnd正在清自身队列时不能重入停战；未起战的拒绝可以直接回取消。
            if (!_started) Finish(null);
            else _cancelRequested = true;
        }

        internal void EndCombatant(Agent agent)
        {
            // StartFight每场只加一次计数，由发起动作配对归还一次；玩家随侧队整体还原。
            if (!_started || _combatEnded || agent != Initiator || _mission != Mission.Current) return;
            _combatEnded = true;
            CombatManager.EndFight(agent);
        }

        internal static bool Owns(Agent agent) => Active.Any(e => !e._closed && e._mission == Mission.Current
            && (agent == e.Initiator || agent == e.Opponent));

        private void Finish(Agent loser)
        {
            if (_closed) return;
            _closed = true;
            Active.Remove(this);
            // 先撤销身份，再发停战：OnEnd重入本方法不会重复收据，也不会恢复血量。
            foreach (var agent in _started && _mission == Mission.Current ? new[] { Initiator, Opponent } : new Agent[0])
                if (AgentControlHelper.SafeIsActive(agent) && !agent.IsMainAgent)
                    try { AgentAIController.Instance?.SendEventToAgent(agent, "event_stop_combat", Receipt); }
                    catch (Exception ex) { DebugLogger.Log("[CombatEncounter] stop failed: " + ex.Message); }
            // 发起者倒地没有Brain tick；仍按同一计数收场，存活者OnEnd已调用时这里为空操作。
            try { EndCombatant(Initiator); }
            catch (Exception ex) { DebugLogger.Log("[CombatEncounter] cleanup failed: " + ex.Message); }
            if (loser != null)
            {
                foreach (var agent in _informed)
                    if (agent != null) Remember(agent,
                        (Reason() ?? "交手") + "；" + loser.Name + "受伤倒地，未按切磋恢复。");
                var winner = loser == Initiator ? Opponent : Initiator;
                if (Up(winner) && !winner.IsMainAgent)
                    try
                    {
                        var context = SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(winner), loser, "combat", "战斗");
                        context.KnownConflict = Reason();
                        SpeechChannel.SayPolished(winner,
                            LWNTextHelper.ResolveText(EndLineKey ?? "LWN_action_combat_end", "Hmph. Overconfident fool."),
                            SpeechPriority.Combat, context);
                    }
                    catch (Exception ex) { DebugLogger.Log("[CombatEncounter] end speech failed: " + ex.Message); }
            }
            try { Receipt?.Invoke(loser); }
            catch (Exception ex) { DebugLogger.Log("[CombatEncounter] receipt failed: " + ex.Message); }
            DebugLogger.Log("[CombatEncounter] ended loser=" + (loser?.Name?.ToString() ?? "cancelled"));
        }

        internal static void NotifyRemoved(Agent agent, AgentState state)
        {
            foreach (var encounter in Active.ToArray())
                if (agent == encounter.Initiator || agent == encounter.Opponent)
                    if (state == AgentState.Killed || state == AgentState.Unconscious) encounter._removed = agent;
                    else encounter._cancelRequested = true; // 活着退出不是落败。
        }

        public static void Tick()
        {
            foreach (var encounter in Active.ToArray())
            {
                if (encounter._mission != Mission.Current) { encounter.Finish(null); continue; }
                if (encounter._removed != null) { encounter.Finish(encounter._removed); continue; }
                var loser = AgentControlHelper.SafeIsActive(encounter.Initiator)
                    && (encounter.Initiator.Health <= 0f || AgentBrain.IsKnockedOut(encounter.Initiator)) ? encounter.Initiator
                    : AgentControlHelper.SafeIsActive(encounter.Opponent)
                    && (encounter.Opponent.Health <= 0f || AgentBrain.IsKnockedOut(encounter.Opponent)) ? encounter.Opponent : null;
                if (loser != null) encounter.Finish(loser);
                else if (encounter._cancelRequested) encounter.Finish(null);
                else if (!Up(encounter.Initiator) || !Up(encounter.Opponent)) encounter.Finish(null);
            }
        }

        internal static bool ShouldWound(Mission mission, Agent agent) => Active.Any(e => !e._closed
            && e._mission == mission && e.WoundOnDefeat && (agent == e.Initiator || agent == e.Opponent));

        public static string KnownReason(Agent self, Agent other = null)
        {
            var encounter = Active.FirstOrDefault(e => e._mission == Mission.Current && e._informed.Contains(self)
                && (other == null || e.Pair(self, other) || other == e.Opponent || other == e.Initiator));
            return encounter?.Reason();
        }

        public static bool HelpsWitness(Agent witness, Agent a, Agent b = null) => Active.Any(e => e._mission == Mission.Current
            && e._beneficiaries.Contains(witness) && e._informed.Contains(witness)
            && (b == null ? a == e.Initiator || a == e.Opponent : e.Pair(a, b)));

        public static bool NoteHelpedAttack(Agent witness, Agent a, Agent b)
        {
            var encounter = Active.FirstOrDefault(e => e._mission == Mission.Current && e.Pair(a, b)
                && e._beneficiaries.Contains(witness) && e._informed.Contains(witness));
            if (encounter == null) return false;
            if (encounter._noted.Add(witness)) Remember(witness, encounter.Reason());
            return true;
        }

        internal static void Clear(Mission mission)
        {
            foreach (var encounter in Active.Where(e => e._mission == mission).ToArray()) encounter.Finish(null);
        }

        public static string Describe()
        {
            var encounters = Active.Where(e => e._mission == Mission.Current).ToArray();
            return "active=" + encounters.Length + " " + string.Join(" ; ", encounters.Select(e =>
                "pair=" + e.Initiator.Index + "/" + e.Opponent.Index + " mode=" + (e.WoundOnDefeat ? "wound" : "mortal")
                + " reason=" + (e.ReasonKey ?? "-") + " informed=[" + string.Join(",", e._informed.Select(a => a.Index))
                + "] cancelled=" + e._cancelRequested));
        }
    }

    // Mission先询问场景已有的IAgentStateDecider；这里只覆盖本次真实冲突的参与者。
    // 正规死亡裁定选Unconscious，随后仍走原生倒地、OnAgentRemoved、Hero伤病与玩家败退流程。
    [HarmonyPatch(typeof(Mission), "GetAgentState")]
    internal static class CombatEncounterWoundPatch
    {
        private static void Postfix(Mission __instance, Agent agent, ref AgentState __result)
        {
            if (CombatEncounter.ShouldWound(__instance, agent)) __result = AgentState.Unconscious;
        }
    }
}
