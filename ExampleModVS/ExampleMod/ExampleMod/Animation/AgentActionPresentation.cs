using System;
using System.Collections.Generic;
using SandBox;
using SandBox.Objects;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs
{
    /// <summary>策划定义动作的配套；乐器挂点和局部变换复用已注册的原版 InstrumentData / prefab。</summary>
    public sealed class ActionPresentationDef
    {
        public string Instrument;
        public string[] Sounds = new string[0];
        public float Speed = 1f;
    }

    /// <summary>
    /// 一份 PerformanceAction 独占的道具与3D声音。这里只管配套，不调度身体动画。
    /// 原版链路：PlayMusicPoint → AnimationPoint → AgentNavigator.SetPrefabVisibility。
    /// 有Navigator时复用其缓存；独立生成的Agent直接走同一原生挂接API，缓存由Agent持有。
    /// 不创建城镇AI，不改装备或物品栏。
    /// </summary>
    internal sealed class AgentActionPresentation
    {
        private readonly Agent _agent;
        private readonly Mission _mission;
        private readonly string _owner;
        private readonly List<Tuple<sbyte, string, bool, int>> _attachments = new List<Tuple<sbyte, string, bool, int>>();
        private AgentNavigator _navigator;
        private MBAgentVisuals _visuals;
        private ActionPresentationDef _definition;
        private string _action;
        private SoundEvent _sound;
        private int _nextSound;
        private float _retry;
        private bool _speedApplied;
        private string _state = "none";
        private string _reason = "";

        public AgentActionPresentation(Agent agent, Mission mission, string owner)
        { _agent = agent; _mission = mission; _owner = owner; }

        public bool Start(string action, ActionPresentationDef definition)
        {
            Stop();
            if (definition == null) return true;
            _action = action;
            _definition = definition;
            _nextSound = 0;
            _retry = 0f;
            try
            {
                if (Mission.Current != _mission || _mission?.Scene == null || !AgentControlHelper.SafeIsActive(_agent))
                    return Fail("owner unavailable");
                foreach (string name in definition.Sounds)
                    if (SoundFx.TryResolve(name) < 0) return Fail("sound missing: " + name);
                _visuals = _agent.AgentVisuals;
                if (!string.IsNullOrEmpty(definition.Instrument))
                {
                    bool Matches(InstrumentData instrument) => instrument != null
                        && (instrument.StandingAction == action || instrument.SittingAction == action)
                        && instrument.InstrumentEntities != null && instrument.InstrumentEntities.Count > 0;
                    var instrument = MBObjectManager.Instance.GetObject<InstrumentData>(definition.Instrument);
                    if (!Matches(instrument)) instrument = MBObjectManager.Instance.GetObject<InstrumentData>(Matches);
                    if (!Matches(instrument)) return Fail("no registered instrument for " + action);
                    _navigator = _agent.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
                    // 先查整套，避免原版缺prefab时悄悄用 rock_001 代替。
                    foreach (var item in instrument.InstrumentEntities)
                    {
                        sbyte bone = _agent.AgentVisuals.GetRealBoneIndex(item.Item1);
                        if (bone < 0 || !GameEntity.PrefabExists(item.Item2))
                            return Fail("instrument bone/prefab missing: " + item.Item2);
                    }
                    AgentPresentationPrefabs cache = null;
                    if (_navigator == null)
                    {
                        cache = _agent.GetComponent<AgentPresentationPrefabs>();
                        if (cache == null)
                        {
                            cache = new AgentPresentationPrefabs(_agent);
                            _agent.AddComponent(cache);
                        }
                    }
                    foreach (var item in instrument.InstrumentEntities)
                    {
                        sbyte bone = _visuals.GetRealBoneIndex(item.Item1);
                        if (_navigator != null)
                        {
                            _attachments.Add(Tuple.Create(bone, item.Item2, _navigator.GetPrefabVisibility(bone, item.Item2), -1));
                            _navigator.SetPrefabVisibility(bone, item.Item2, true);
                        }
                        else
                        {
                            int index;
                            bool existing = cache.TryGet(bone, item.Item2, out index);
                            bool wasVisible = existing && _agent.IsSynchedPrefabComponentVisible(index);
                            if (!existing) index = cache.Add(bone, item.Item2);
                            // 新挂件只归本次演出：先登记，再显示，失败可按实际句柄回滚。
                            _attachments.Add(Tuple.Create(bone, item.Item2, wasVisible, index));
                            _agent.SetSynchedPrefabComponentVisibility(index, true);
                        }
                    }
                }
                _state = "waiting-action";
                _reason = "";
                DebugLogger.Log("[ActionPresentation] begin owner=" + _owner + " action=" + action
                    + " instrument=" + (definition.Instrument ?? "-") + " prefabs=" + _attachments.Count
                    + " attachment=" + (_navigator == null ? "agent-native" : "navigator"));
                return true;
            }
            catch (Exception ex) { return Fail(ex.GetType().Name + ": " + ex.Message); }
        }

        public bool Tick(float dt, bool canControl)
        {
            if (_definition == null) return true;
            if (Mission.Current != _mission || !AgentControlHelper.SafeIsActive(_agent)) { Stop(); return true; }
            if (!canControl || !AgentControlHelper.IsPlayingPose(_agent, _action)) { Stop(); return true; }
            try
            {
                if (!_speedApplied)
                {
                    _agent.AgentVisuals.GetSkeleton().SetAnimationSpeedAtChannel(0, _definition.Speed);
                    _speedApplied = true;
                }
                _retry = Math.Max(0f, _retry - Math.Max(0f, dt));
                if (_sound != null)
                {
                    _sound.SetPosition(_agent.Position);
                    if (_sound.IsPlaying()) return true;
                    _sound.Release();
                    _sound = null;
                }
                if (_definition.Sounds.Length == 0) { _state = "attached"; return true; }
                if (_retry > 0f) return true;
                string name = _definition.Sounds[_nextSound];
                _sound = SoundFx.Play3D(name, _agent.Position);
                if (_sound == null)
                { _state = "sound-failed"; _reason = name; _retry = 2f; return true; }
                _state = "playing";
                _reason = name;
                _nextSound = (_nextSound + 1) % _definition.Sounds.Length;
                return true;
            }
            catch (Exception ex) { return Fail(ex.GetType().Name + ": " + ex.Message); }
        }

        public void Stop()
        {
            // 场景仍是原场景时声音句柄可收尾，即使演员已经死亡/移除也不能留音乐。
            bool sameScene = Mission.Current == _mission && _mission?.Scene != null;
            if (sameScene && _sound != null)
                try { _sound.Stop(); _sound.Release(); } catch (Exception ex) { LogCleanup(ex); }
            _sound = null;
            // 死亡时IsActive已经为false，但尸体视觉还有效；仍须隐藏本次挂件。
            bool hasVisuals = false;
            try { hasVisuals = sameScene && _agent.Mission == _mission && _visuals != null && _visuals.IsValid(); }
            catch (Exception ex) { LogCleanup(ex); }
            if (hasVisuals)
            {
                foreach (var item in _attachments)
                    try
                    {
                        if (item.Item4 >= 0) _agent.SetSynchedPrefabComponentVisibility(item.Item4, item.Item3);
                        else _navigator?.SetPrefabVisibility(item.Item1, item.Item2, item.Item3);
                    }
                    catch (Exception ex) { LogCleanup(ex); }
                // 不覆盖已接手的战斗/绑缚动作倍率。
                try
                {
                    if (_speedApplied && AgentControlHelper.SafeIsActive(_agent) && AgentControlHelper.IsPlayingPose(_agent, _action))
                        _visuals.GetSkeleton().SetAnimationSpeedAtChannel(0, 1f);
                }
                catch (Exception ex) { LogCleanup(ex); }
            }
            _attachments.Clear();
            _navigator = null;
            _visuals = null;
            _definition = null;
            _speedApplied = false;
            _state = "none";
            _reason = "";
        }

        public string Describe() => "presentation=" + _state + " attached=" + _attachments.Count
            + (_definition == null ? "" : " instrument=" + (_definition.Instrument ?? "-") + " speed=" + _definition.Speed)
            + (string.IsNullOrEmpty(_reason) ? "" : " detail=" + _reason);

        private bool Fail(string reason)
        {
            Stop();
            _state = "failed";
            _reason = reason;
            DebugLogger.Log("[ActionPresentation] failed owner=" + _owner + " action=" + _action + " reason=" + reason);
            return false;
        }

        private void LogCleanup(Exception ex)
            => DebugLogger.Log("[ActionPresentation] cleanup owner=" + _owner + " " + ex.Message);
    }

    /// <summary>
    /// 无城镇Navigator的演员也能复用原版Agent挂件。只缓存本Agent的原生句柄，不运行AI或写动画。
    /// 挂件生命周期归引擎，显示/隐藏归演出租约；多次暂停、回岗或开场重播不重复创建。
    /// </summary>
    internal sealed class AgentPresentationPrefabs : AgentComponent
    {
        private readonly Dictionary<Tuple<sbyte, string>, int> _indices = new Dictionary<Tuple<sbyte, string>, int>();
        public AgentPresentationPrefabs(Agent agent) : base(agent) { }
        public bool TryGet(sbyte bone, string prefab, out int index)
            => _indices.TryGetValue(Tuple.Create(bone, prefab), out index);
        public int Add(sbyte bone, string prefab)
        {
            int index = Agent.AddSynchedPrefabComponentToBone(prefab, bone);
            if (index < 0) throw new InvalidOperationException("agent prefab attachment failed: " + prefab);
            _indices.Add(Tuple.Create(bone, prefab), index);
            return index;
        }
        public override void OnAgentRemoved() => _indices.Clear();
        public override void OnComponentRemoved() => _indices.Clear();
    }
}
