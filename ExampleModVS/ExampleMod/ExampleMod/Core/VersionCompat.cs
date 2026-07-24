using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Version-compatibility static helpers. Each method wraps an API that changed between v1.2.12 and Latest.
    /// Call sites use V.xxx() instead of the raw API; the #if !MB2_V1212 branches select the correct implementation.
    ///
    /// IMPORTANT: Compile BOTH Debug (LATEST) and Debug_v1.2.12 after every change to this file.
    /// </summary>
    public static class V
    {
        // ── Position ──────────────────────────────────────────────
        // v1.2.12: party.Position2D / settlement.Position2D
        // Latest:  party.GetPosition2D / settlement.GetPosition2D

        public static Vec2 Pos(MobileParty party)
        {
            if (party == null) return Vec2.Zero;
#if !MB2_V1212
            return party.GetPosition2D;
#else
            return party.Position2D;
#endif
        }

        public static Vec2 Pos(Settlement settlement)
        {
            if (settlement == null) return Vec2.Zero;
#if !MB2_V1212
            return settlement.GetPosition2D;
#else
            return settlement.Position2D;
#endif
        }

        // Position setter (assignment case)
        // v1.2.12: party.Position2D = value
        // Latest:  party.Position = new CampaignVec2(value, true)

        public static void SetPos(MobileParty party, Vec2 pos)
        {
            if (party == null) return;
#if !MB2_V1212
            party.Position = new CampaignVec2(pos, true);
#else
            party.Position2D = pos;
#endif
        }

        // ── Agent AI check ────────────────────────────────────────
        // v1.2.12: agent.Controller == Agent.ControllerType.AI
        // Latest:  agent.IsAIControlled

        public static bool IsAgentAI(Agent agent)
        {
            if (agent == null) return false;
#if !MB2_V1212
            return agent.IsAIControlled;
#else
            return agent.Controller == Agent.ControllerType.AI;
#endif
        }

        // ── Campaign start time ───────────────────────────────────
        // v1.2.12: Campaign.Current.CampaignStartTime
        // Latest:  Campaign.Current.CampaignStartTime

        public static CampaignTime GetStartTime()
        {
#if !MB2_V1212
            return Campaign.Current.Models.CampaignTimeModel.CampaignStartTime;
#else
            return Campaign.Current.CampaignStartTime;
#endif
        }

        // ── Kingdom strength ──────────────────────────────────────
        // v1.2.12: kingdom.TotalStrength
        // Latest:  kingdom.CurrentTotalStrength

        public static float KingdomStr(Kingdom kingdom)
        {
            if (kingdom == null) return 0f;
#if !MB2_V1212
            return kingdom.CurrentTotalStrength;
#else
            return kingdom.TotalStrength;
#endif
        }

        // ── TextObject.Empty ──────────────────────────────────────
        // v1.2.12: TextObject.Empty
        // Latest:  TextObject.GetEmpty() or TextObject.Empty (check)

        public static TextObject EmptyText()
        {
#if !MB2_V1212
            return TextObject.GetEmpty();
#else
            return TextObject.Empty;
#endif
        }

        // ── Agent weapon indices ──────────────────────────────────
        // v1.2.12: agent.GetWieldedItemIndex(Agent.HandIndex.MainHand)
        // Latest:  agent.GetPrimaryWieldedItemIndex(UsageType) or similar

        public static EquipmentIndex MainWpn(Agent agent)
        {
            if (agent == null) return EquipmentIndex.None;
#if !MB2_V1212
            return agent.GetPrimaryWieldedItemIndex();
#else
            return agent.GetWieldedItemIndex(Agent.HandIndex.MainHand);
#endif
        }

        public static EquipmentIndex OffWpn(Agent agent)
        {
            if (agent == null) return EquipmentIndex.None;
#if !MB2_V1212
            return agent.GetOffhandWieldedItemIndex();
#else
            return agent.GetWieldedItemIndex(Agent.HandIndex.OffHand);
#endif
        }

        // ── Party movement (Ai.SetMove* vs party.SetMove*) ────────
        // v1.2.12: party.Ai.SetMoveGoToPoint(pos)
        // Latest:  party.SetMoveGoToPoint(pos)

        public static void SetMoveTo(MobileParty party, Vec2 pos)
        {
            if (party == null) return;
#if !MB2_V1212
            party.SetMoveGoToPoint(new CampaignVec2(pos, true), MobileParty.NavigationType.All);
#else
            party.Ai.SetMoveGoToPoint(pos);
#endif
        }

        public static void SetMoveEngage(MobileParty party, MobileParty target)
        {
            if (party == null || target == null) return;
#if !MB2_V1212
            party.SetMoveEngageParty(target, MobileParty.NavigationType.All);
#else
            party.Ai.SetMoveEngageParty(target);
#endif
        }

        public static void SetMoveToTown(MobileParty party, Settlement settlement)
        {
            if (party == null || settlement == null) return;
#if !MB2_V1212
            party.SetMoveGoToSettlement(settlement, MobileParty.NavigationType.All, false);
#else
            party.Ai.SetMoveGoToSettlement(settlement);
#endif
        }

        public static void SetMovePatrol(MobileParty party, Vec2 pos)
        {
            if (party == null) return;
#if !MB2_V1212
            party.SetMovePatrolAroundPoint(new CampaignVec2(pos, true), MobileParty.NavigationType.All);
#else
            party.Ai.SetMovePatrolAroundPoint(pos);
#endif
        }

        public static void SetMoveEscort(MobileParty party, MobileParty target)
        {
            if (party == null || target == null) return;
#if !MB2_V1212
            party.SetMoveEscortParty(target, MobileParty.NavigationType.All, false);
#else
            party.Ai.SetMoveEscortParty(target);
#endif
        }

        public static MobileParty MoveTarget(MobileParty party)
        {
            if (party == null) return null;
#if !MB2_V1212
            return party.MoveTargetParty;
#else
            return party.Ai.MoveTargetParty;
#endif
        }

        // ── Party lifecycle ───────────────────────────────────────
        // v1.2.12: MobileParty.CreateParty(id, comp, delegate)
        // Latest:  MobileParty.CreateParty(id, comp)

        public static MobileParty MakeParty(string id, PartyComponent comp)
        {
            if (string.IsNullOrEmpty(id) || comp == null) return null;
#if !MB2_V1212
            return MobileParty.CreateParty(id, comp);
#else
            return MobileParty.CreateParty(id, comp, null);
#endif
        }

        // v1.2.12: party.RemoveParty()
        // Latest:  DestroyPartyAction.Apply(null, party)

        public static void DelParty(MobileParty party)
        {
            if (party == null) return;
#if !MB2_V1212
            DestroyPartyAction.Apply(null, party);
#else
            party.RemoveParty();
#endif
        }

        // ── Kingdom / clan ────────────────────────────────────────
        // v1.2.12: ChangeKingdomAction.ApplyByJoinToKingdomByDefection(clan, toKingdom)
        // Latest:  ChangeKingdomAction.ApplyByJoinToKingdomByDefection(clan, fromKingdom, toKingdom, CampaignTime, bool)

        public static void JoinDefect(Clan clan, Kingdom fromKingdom, Kingdom toKingdom)
        {
            if (clan == null || toKingdom == null) return;
#if !MB2_V1212
            ChangeKingdomAction.ApplyByJoinToKingdomByDefection(clan, fromKingdom, toKingdom, CampaignTime.Zero, false);
#else
            ChangeKingdomAction.ApplyByJoinToKingdomByDefection(clan, toKingdom);
#endif
        }

        // ── Agent action name ─────────────────────────────────────
        // v1.2.12: agent.GetCurrentActionValue(channelIndex).Name
        // Latest:  agent.GetCurrentActionType(channelIndex) returns enum, use ToString

        public static string ActName(Agent agent, int channelIndex = 0)
        {
            if (agent == null) return "";
#if !MB2_V1212
            return agent.GetCurrentActionType(channelIndex).ToString();
#else
            return agent.GetCurrentActionValue(channelIndex).Name;
#endif
        }

        // ── Ray cast (GameEntity vs WeakGameEntity) ───────────────
        // Latest OnRegisterBlow uses WeakGameEntity, but general raycast is the same

        /// <summary>
        /// 射线检测最近的 Agent（屏蔽版本差异：out dist 参数位置不同）。
        /// v1.2.12: out dist 在第 3 位；Latest: out dist 在最后。
        /// </summary>
        public static Agent RayCastForClosestAgent(Vec3 rayStart, Vec3 rayEnd, int excludedAgentIndex,
            out float collisionDistance, float rayThickness = 0.1f)
        {
#if !MB2_V1212
            return Mission.Current.RayCastForClosestAgent(rayStart, rayEnd, excludedAgentIndex, rayThickness, out collisionDistance);
#else
            return Mission.Current.RayCastForClosestAgent(rayStart, rayEnd, out collisionDistance, excludedAgentIndex, rayThickness);
#endif
        }

        public static bool RayBlocked(Vec3 from, Vec3 to, float maxDist)
        {
#if !MB2_V1212
            // In Latest, raycast uses out WeakGameEntity
            float dist = to.Distance(from);
            if (dist > maxDist) return true;
            return Mission.Current != null && Mission.Current.Scene != null
                && Mission.Current.Scene.RayCastForClosestEntityOrTerrain(from, to, out _, 0.3f);
#else
            float dist = to.Distance(from);
            if (dist > maxDist) return true;
            return Mission.Current != null && Mission.Current.Scene != null
                && Mission.Current.Scene.RayCastForClosestEntityOrTerrain(from, to, out _, 0.3f);
#endif
        }

        /// <summary>
        /// RayCastForClosestEntityOrTerrain 的版本兼容封装。
        /// v1.2.12: out GameEntity；Latest: out WeakGameEntity。
        /// </summary>
        public static bool RayCastForClosestEntityOrTerrain(Vec3 from, Vec3 to,
            out float collisionDistance, out Vec3 closestPoint, float rayThickness, BodyFlags bodyFlags)
        {
            if (Mission.Current == null || Mission.Current.Scene == null)
            {
                collisionDistance = 0f;
                closestPoint = Vec3.Invalid;
                return false;
            }
#if !MB2_V1212
            return Mission.Current.Scene.RayCastForClosestEntityOrTerrain(
                from, to, out collisionDistance, out closestPoint, out WeakGameEntity _,
                rayThickness, bodyFlags);
#else
            return Mission.Current.Scene.RayCastForClosestEntityOrTerrain(
                from, to, out collisionDistance, out closestPoint, out GameEntity _,
                rayThickness, bodyFlags);
#endif
        }

        // ── GauntletLayer ─────────────────────────────────────────
        // v1.2.12: new GauntletLayer(int localOrder, string name)
        // Latest:  new GauntletLayer(string name, int localOrder)  ← params reversed!

        public static GauntletLayer NewLayer(int order, string name = null)
        {
#if !MB2_V1212
            return new GauntletLayer(name ?? "LivingWorldLayer", order);
#else
            return new GauntletLayer(order, name ?? "LivingWorldLayer");
#endif
        }

        // ── LoadMovie return type ─────────────────────────────────
        // v1.2.12: layer.LoadMovie(name, vm) returns IGauntletMovie, vm is ViewModel
        // Latest:  layer.LoadMovie(name, vm) — return type may differ

        public static void LoadMov(GauntletLayer layer, string name, TaleWorlds.Library.ViewModel vm)
        {
            if (layer == null || vm == null) return;
#if !MB2_V1212
            layer.LoadMovie(name, vm);
#else
            layer.LoadMovie(name, vm);
#endif
        }

        // ── NavMesh ───────────────────────────────────────────────
        // v1.2.12: scene.GetNavigationMeshForPosition(ref pos, out faceIndex) → returns bool
        // Latest:  scene.GetNavigationMeshForPosition(in pos, out faceIndex, float, bool) → returns UIntPtr

        public static bool NavMesh(Scene scene, Vec3 position, out int faceIndex)
        {
            faceIndex = -1;
            if (scene == null) return false;
#if !MB2_V1212
            scene.GetNavigationMeshForPosition(in position, out faceIndex, 1.5f, false);
            return faceIndex != -1;
#else
            return scene.GetNavigationMeshForPosition(ref position, out faceIndex);
#endif
        }

        public static bool SaveNavMesh(Scene scene, Vec3 position)
        {
            if (scene == null) return false;
#if !MB2_V1212
            scene.GetNavigationMeshForPosition(in position, out _, 1.5f, false);
            return true;
#else
            return scene.GetNavigationMeshForPosition(ref position, out _);
#endif
        }

        // ── InitializeMobilePartyAtPosition ─────────────────────────
        // v1.2.12: party.InitializeMobilePartyAtPosition(template, Vec2)
        // Latest:  party.InitializeMobilePartyAtPosition(template, CampaignVec2)

        public static void InitPartyPos(MobileParty party, PartyTemplateObject template, Vec2 pos)
        {
            if (party == null) return;
#if !MB2_V1212
            party.InitializeMobilePartyAtPosition(template, new CampaignVec2(pos, true));
#else
            party.InitializeMobilePartyAtPosition(template, pos);
#endif
        }

        // ── Set party custom name ───────────────────────────────────
        // v1.2.12: party.SetCustomName(name)
        // Latest:  MobileParty.Name is read-only; name set through component

        public static void SetPartyName(MobileParty party, TextObject name)
        {
            if (party == null) return;
#if !MB2_V1212
            party.Party.SetCustomName(name);
#else
            party.SetCustomName(name);
#endif
        }

        // ── Set agent AI controlled ─────────────────────────────────
        // v1.2.12: agent.Controller = Agent.ControllerType.AI
        // Latest:  ControllerType enum removed; use IsAIControlled (read-only)

        public static void SetAgentAI(Agent agent)
        {
            if (agent == null) return;
#if !MB2_V1212
            // Agent.ControllerType removed in Latest; agents default to AI
#else
            agent.Controller = Agent.ControllerType.AI;
#endif
        }

        // ── Agent player checks ─────────────────────────────────────
        // v1.2.12: agent.Controller == Agent.ControllerType.Player
        // Latest:  !agent.IsAIControlled (approximate)

        public static bool IsAgentPlayer(Agent agent)
        {
            if (agent == null) return false;
#if !MB2_V1212
            return !agent.IsAIControlled;
#else
            return agent.Controller == Agent.ControllerType.Player;
#endif
        }

        public static void SetAgentPlayer(Agent agent)
        {
            if (agent == null) return;
#if !MB2_V1212
            // TODO: Find Latest API for setting player control
#else
            agent.Controller = Agent.ControllerType.Player;
#endif
        }

        // ── Freeze/unfreeze player control（偷窃条输入隔离）────────────
        // v1.2.12: agent.Controller = ControllerType.AI 冻结——输入处理权从玩家控制器移交 AI 组件，
        //          主角无 AI 指令 = 原地待机（SandBox 官方同款用法；AgentBrain.Tick 对 Agent.Main 早退，
        //          本 mod 不会接管）。实测 ControllerType.None 无效（MainAgent 疑被原生特判仍处理输入）。
        //          恢复 Player 时 Mission.MainAgent 自动重指 + 广播 OnAgentControllerSetToPlayer，官方可逆。
        // Latest:  ControllerType 枚举已被官方删除，冻结 API 待查——暂 no-op（Latest 侧空格误跳待解）。

        public static void SetPlayerControlFrozen(Agent agent, bool frozen)
        {
            if (agent == null) return;
#if !MB2_V1212
            // TODO: Find Latest API for freezing player control
#else
            var target = frozen ? Agent.ControllerType.AI : Agent.ControllerType.Player;
            if (agent.Controller != target)
                agent.Controller = target;
#endif
        }

        // ── Enemy kingdoms ──────────────────────────────────────────
        // v1.2.12: FactionManager.GetEnemyKingdoms(kingdom)
        // Latest:  Iterate Kingdom.All with IsAtWarWith

        public static IEnumerable<Kingdom> GetEnemyKingdoms(Kingdom kingdom)
        {
            if (kingdom == null) yield break;
#if !MB2_V1212
            foreach (var k in Kingdom.All)
            {
                if (k != kingdom && kingdom.IsAtWarWith(k))
                    yield return k;
            }
#else
            foreach (var k in FactionManager.GetEnemyKingdoms(kingdom))
                yield return k;
#endif
        }

        // ── Scene raycast（视线拾取）────────────────────────────────
        // v1.2.12: out GameEntity
        // Latest:  out WeakGameEntity（读取接口等价，提取字串后统一返回）

        /// <summary>视线射线命中结果（两版本统一的只读快照）。</summary>
        public struct LookAtHit
        {
            public bool Hit;
            public float Distance;
            public Vec3 Point;
            public string EntityName;   // null = 命中地形/无实体
            public string PrefabName;
            public string MeshName;
        }

        /// <summary>
        /// 沿视线做射线检测（与原版交互聚焦同一 API，默认 CommonFocusRayCastExcludeFlags）。
        /// 只命中有物理碰撞体的实体 + 地形；纯装饰 mesh 会穿透，调用方需自行兜底（如视锥几何扫描）。
        /// </summary>
        public static LookAtHit RayCastLookAt(Scene scene, Vec3 src, Vec3 dst)
        {
            var r = new LookAtHit();
            if (scene == null) return r;
#if !MB2_V1212
            r.Hit = scene.RayCastForClosestEntityOrTerrain(src, dst, out float d, out Vec3 p, out WeakGameEntity e);
            r.Distance = d; r.Point = p;
            if (r.Hit && e.IsValid)
            {
                r.EntityName = e.Name;
                try { r.PrefabName = e.GetPrefabName(); } catch (Exception) { /* 实体失效时跳过资源名 */ }
                try { if (e.MultiMeshComponentCount > 0) r.MeshName = e.GetMetaMesh(0)?.GetName(); } catch (Exception) { }
            }
#else
            r.Hit = scene.RayCastForClosestEntityOrTerrain(src, dst, out float d, out Vec3 p, out GameEntity e);
            r.Distance = d; r.Point = p;
            if (r.Hit && e != null)
            {
                r.EntityName = e.Name;
                try { r.PrefabName = e.GetPrefabName(); } catch (Exception) { /* 实体失效时跳过资源名 */ }
                try { if (e.MultiMeshComponentCount > 0) r.MeshName = e.GetMetaMesh(0)?.GetName(); } catch (Exception) { }
            }
#endif
            return r;
        }
    }
}
