using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.TwoDimension;
using Helpers; // v1.3.0+：InventoryManager 改名 InventoryScreenHelper，namespace 也改为 Helpers

namespace LivingWorldNpcs
{
    /// <summary>
    /// Version-compatibility static helpers. Each method wraps an API that changed between versions.
    /// Call sites use V.xxx() instead of raw API; the version macros select the correct implementation.
    ///
    /// 🔴 Version macro convention (threshold-based, cumulative):
    ///   #if MB2_GE_150   — API introduced/modified in v1.5.0+
    ///                      （2026-08-23 开发机升级 v1.5.1：编译验证 1.5.x 与 1.4.x 签名一致，尚无分支使用）
    ///   #elif MB2_GE_140  — API introduced in v1.4.0+
    ///   #elif MB2_GE_130  — API introduced in v1.3.0+
    ///   #else             — v1.2.12 (oldest supported)
    ///
    /// MB2_V1212 is the legacy "minimum version" marker; #else is semantically "≤ 1.2.12".
    /// New methods SHOULD use the #if MB2_GE_XXX / #elif / #else convention.
    /// Older methods may still use #if MB2_V1212 / #else — both are valid.
    ///
    /// 🔴 Non-VersionCompat #if registry（不可迁入 V 的合法裸 #if 桌面级扫描清单）:
    ///   每次新增版本或改动 API 后，必须核查以下所有位置是否仍需 #if、是否需要增删：
    ///
    ///   [override/abstract] — 基类虚方法签名跨版本不同：
    ///     SafeLordPartyComponent.cs:41        GetDefaultComponentBanner() override
    ///     CustomPartyComponent.cs:42          GetDefaultComponentBanner() override
    ///     AttackTriggerMissionLogic.cs:395    OnRegisterBlow(GameEntity→WeakGameEntity)
    ///     CommissionHubIssue.cs:413,424      CanPlayerTakeQuestConditions：v1.4.0+ 多 out int requiredGold；
    ///                                          v1.2.12~v1.3.x 为 4 参（MB2_GE_140 三分支，非 !MB2_V1212 二分）
    ///
    ///   [type-level] — 字段/变量类型跨版本不同：
    ///     MySubModule.cs:344                  IGauntletMovie vs GauntletMovieIdentifier
    ///     CameraDebuggerView.cs:34            同上
    ///     SpringArmCameraView.cs:40           同上
    ///     NinjaNotificationMissionView.cs:19  同上
    ///     MyCommands.cs:646                   MissionObject.GameEntity 返回类型
    ///     PlayerDetentionBehavior.cs:9,312    GameOverlays→GameMenu.MenuOverlayType
    ///
    ///   [Harmony] — 补丁目标/参数类型跨版本不同：
    ///     InteractionMissionView.cs:2529     F-to-talk 隐藏目标类+属性类型
    ///     InteractionMissionView.cs:2559     InventoryManager.OpenScreenAsTrade（1.2.12 only）
    ///     DebugLogger.cs:18                  FillPartyStacks→FillPartyManuallyAfterCreation
    ///
    ///   [structural] — 多语句算法/功能模块跨版本完全不同的实现：
    ///     WorldEventSimulator.cs:1668,1719    AreFacesOnSameIsland 移除
    ///     MyCommands.cs:1619                  stealth_debug 命令（1.4.x only）
    ///     MyCommands.cs:30                    using SandBox.Missions（1.4.x only）
    ///     MyBehavior.cs:33,45                CampaignEvents 事件注册差异：HeroPrisonerReleased
    ///                                         5参=1.3+ / 4参=1.2.12（lambda 适配）；BeforeHeroesMarried
    ///                                         1.3+ / 1.2.12 为同名同签名 HeroesMarried（婚后触发）
    ///   （搜刮/开箱 Loot 流的 InventoryManager #if 已于 2026-08-14 移除：类在 v1.3.0 改名
    ///     InventoryScreenHelper，签名一致，统一走 V.OpenLootScreen，不再裸 #if。）
    /// </summary>
    /// IMPORTANT: Compile BOTH Debug (LATEST) and Debug_v1.2.12 after every change to this file.
    /// </summary>
    public static class V
    {
        // ── UI 资源（Sprite partial-load，2026-08-30）─────────────
        // v1.2.12: UIResourceManager.UIResourceDepot（且无 GetSpriteCategory）
        // v1.3.0+: UIResourceManager.ResourceDepot

        public static ResourceDepot UIResourceDepot()
        {
#if MB2_GE_130
            return UIResourceManager.ResourceDepot;
#else
            return UIResourceManager.UIResourceDepot;
#endif
        }

        // 镜像 Sprite 构造（2026-08-31，立绘镜像 SpriteMirror）：
        // v1.2.12: SpriteGeneric(string name, SpritePart spritePart) —— 2 参（1.2.12 客户端 DLL 实测 4046 行）
        // v1.4.x/v1.5.x: 3 参 (name, part, in SpriteNinePatchParameters)（1.5.1 bin 实测 4748 行）；🔴 1.3.x 未核（开发机 1.2.12，次验证机 1.5.1）
        public static TaleWorlds.TwoDimension.SpriteGeneric NewSpriteGeneric(string name, TaleWorlds.TwoDimension.SpritePart spritePart)
        {
#if MB2_V1212
            return new TaleWorlds.TwoDimension.SpriteGeneric(name, spritePart);
#else
            var nine = TaleWorlds.TwoDimension.SpriteNinePatchParameters.Empty;
            return new TaleWorlds.TwoDimension.SpriteGeneric(name, spritePart, nine);
#endif
        }

        // SpriteData.SpriteCategories 字典两版本同名同构，直接查询即可（1.2.12~1.5.1 均无 #if 需求）
        public static SpriteCategory GetSpriteCategory(string name)
        {
            var data = UIResourceManager.SpriteData;
            if (data == null) return null;
            return data.SpriteCategories.TryGetValue(name, out var cat) ? cat : null;
        }
        // ── Position ──────────────────────────────────────────────
        // v1.2.12: party.Position2D / settlement.Position2D
        // Latest:  party.GetPosition2D / settlement.GetPosition2D

        public static Vec2 Pos(MobileParty party)
        {
            if (party == null) return Vec2.Zero;
#if MB2_GE_130
            return party.GetPosition2D;
#else
            return party.Position2D;
#endif
        }

        public static Vec2 Pos(Settlement settlement)
        {
            if (settlement == null) return Vec2.Zero;
#if MB2_GE_130
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
#if MB2_GE_130
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
#if MB2_GE_130
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
#if MB2_GE_130
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
#if MB2_GE_130
            return kingdom.CurrentTotalStrength;
#else
            return kingdom.TotalStrength;
#endif
        }

        // ── Clan strength ─────────────────────────────────────────
        // v1.2.12: clan.TotalStrength
        // Latest:  clan.CurrentTotalStrength

        public static float ClanStr(Clan clan)
        {
            if (clan == null) return 0f;
#if MB2_GE_130
            return clan.CurrentTotalStrength;
#else
            return clan.TotalStrength;
#endif
        }

        // ── TextObject.Empty ──────────────────────────────────────
        // v1.2.12: TextObject.Empty
        // Latest:  TextObject.GetEmpty() or TextObject.Empty (check)

        public static TextObject EmptyText()
        {
#if MB2_GE_130
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
#if MB2_GE_130
            return agent.GetPrimaryWieldedItemIndex();
#else
            return agent.GetWieldedItemIndex(Agent.HandIndex.MainHand);
#endif
        }

        public static EquipmentIndex OffWpn(Agent agent)
        {
            if (agent == null) return EquipmentIndex.None;
#if MB2_GE_130
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
#if MB2_GE_130
            party.SetMoveGoToPoint(new CampaignVec2(pos, true), MobileParty.NavigationType.All);
#else
            party.Ai.SetMoveGoToPoint(pos);
#endif
        }

        public static void SetMoveEngage(MobileParty party, MobileParty target)
        {
            if (party == null || target == null) return;
#if MB2_GE_130
            party.SetMoveEngageParty(target, MobileParty.NavigationType.All);
#else
            party.Ai.SetMoveEngageParty(target);
#endif
        }

        public static void SetMoveToTown(MobileParty party, Settlement settlement)
        {
            if (party == null || settlement == null) return;
#if MB2_GE_130
            party.SetMoveGoToSettlement(settlement, MobileParty.NavigationType.All, false);
#else
            party.Ai.SetMoveGoToSettlement(settlement);
#endif
        }

        public static void SetMovePatrol(MobileParty party, Vec2 pos)
        {
            if (party == null) return;
#if MB2_GE_130
            party.SetMovePatrolAroundPoint(new CampaignVec2(pos, true), MobileParty.NavigationType.All);
#else
            party.Ai.SetMovePatrolAroundPoint(pos);
#endif
        }

        public static void SetMoveEscort(MobileParty party, MobileParty target)
        {
            if (party == null || target == null) return;
#if MB2_GE_130
            party.SetMoveEscortParty(target, MobileParty.NavigationType.All, false);
#else
            party.Ai.SetMoveEscortParty(target);
#endif
        }

        public static MobileParty MoveTarget(MobileParty party)
        {
            if (party == null) return null;
#if MB2_GE_130
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
#if MB2_GE_130
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
#if MB2_GE_130
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
#if MB2_GE_130
            ChangeKingdomAction.ApplyByJoinToKingdomByDefection(clan, fromKingdom, toKingdom, CampaignTime.Zero, false);
#else
            ChangeKingdomAction.ApplyByJoinToKingdomByDefection(clan, toKingdom);
#endif
        }

        // v1.2.12: ChangeKingdomAction.ApplyByJoinToKingdom(clan, toKingdom, bool showNotification)
        // Latest:  ChangeKingdomAction.ApplyByJoinToKingdom(clan, toKingdom, CampaignTime, bool)

        public static void JoinKingdom(Clan clan, Kingdom toKingdom, bool showNotification)
        {
            if (clan == null || toKingdom == null) return;
#if MB2_GE_130
            ChangeKingdomAction.ApplyByJoinToKingdom(clan, toKingdom, CampaignTime.Zero, showNotification);
#else
            ChangeKingdomAction.ApplyByJoinToKingdom(clan, toKingdom, showNotification);
#endif
        }

        // ── Captivity release ─────────────────────────────────────
        // v1.2.12: EndCaptivityAction.ApplyByEscape(hero, facilitator)
        // Latest:  EndCaptivityAction.ApplyByEscape(hero, facilitator, bool showNotification)

        public static void EndCaptivityEscape(Hero character, Hero facilitator)
        {
            if (character == null) return;
#if MB2_GE_130
            EndCaptivityAction.ApplyByEscape(character, facilitator, true);
#else
            EndCaptivityAction.ApplyByEscape(character, facilitator);
#endif
        }

        // ── Agent action name ─────────────────────────────────────
        // v1.2.12: agent.GetCurrentActionValue(channelIndex).Name
        // Latest:  agent.GetCurrentActionType(channelIndex) returns enum, use ToString

        public static string ActName(Agent agent, int channelIndex = 0)
        {
            if (agent == null) return "";
#if MB2_GE_130
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
#if MB2_GE_130
            return Mission.Current.RayCastForClosestAgent(rayStart, rayEnd, excludedAgentIndex, rayThickness, out collisionDistance);
#else
            return Mission.Current.RayCastForClosestAgent(rayStart, rayEnd, out collisionDistance, excludedAgentIndex, rayThickness);
#endif
        }

        public static bool RayBlocked(Vec3 from, Vec3 to, float maxDist)
        {
#if MB2_GE_130
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
#if MB2_GE_130
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
#if MB2_GE_130
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
#if MB2_GE_130
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
#if MB2_GE_130
            scene.GetNavigationMeshForPosition(in position, out faceIndex, 1.5f, false);
            return faceIndex != -1;
#else
            return scene.GetNavigationMeshForPosition(ref position, out faceIndex);
#endif
        }

        public static bool SaveNavMesh(Scene scene, Vec3 position)
        {
            if (scene == null) return false;
#if MB2_GE_130
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
#if MB2_GE_130
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
#if MB2_GE_130
            party.Party.SetCustomName(name);
#else
            party.SetCustomName(name);
#endif
        }

        // ── Set agent AI controlled ─────────────────────────────────
        // v1.2.12: agent.Controller = Agent.ControllerType.AI
        // Latest:  agent.Controller = AgentControllerType.AI (type renamed from nested enum to top-level)

        public static void SetAgentAI(Agent agent)
        {
            if (agent == null) return;
#if MB2_GE_130
            agent.Controller = AgentControllerType.AI;
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
#if MB2_GE_130
            return !agent.IsAIControlled;
#else
            return agent.Controller == Agent.ControllerType.Player;
#endif
        }

        public static void SetAgentPlayer(Agent agent)
        {
            if (agent == null) return;
#if MB2_GE_130
            agent.Controller = AgentControllerType.Player;
#else
            agent.Controller = Agent.ControllerType.Player;
#endif
        }

        // ── Map weather ──────────────────────────────────────────────
        // 天气（方案 G1，2026-08-16）：Campaign.Current.Models.MapWeatherModel.GetWeatherEventInPosition
        // ✅ 反编译实锤三版本均有；MapWeatherModel = ComponentInterfaces 命名空间，
        // WeatherEvent = MapWeatherModel 嵌套枚举（Clear/LightRain/HeavyRain/Snowy/Blizzard；
        // 🔴 Storm 为 v1.3.0+ 新增，v1.2.12 无此成员——WeatherWord 的 Storm 分支必须 #if MB2_GE_130）。
        // 返回 MapWeatherModel.WeatherEvent 枚举（无模型/无 Campaign → Clear 兜底，调用方按词表映射措辞）。

        public static TaleWorlds.CampaignSystem.ComponentInterfaces.MapWeatherModel.WeatherEvent GetWeatherAt(Vec2 pos)
        {
            try
            {
                if (Campaign.Current?.Models?.MapWeatherModel == null) return TaleWorlds.CampaignSystem.ComponentInterfaces.MapWeatherModel.WeatherEvent.Clear;
                return Campaign.Current.Models.MapWeatherModel.GetWeatherEventInPosition(pos);
            }
            catch { return TaleWorlds.CampaignSystem.ComponentInterfaces.MapWeatherModel.WeatherEvent.Clear; }
        }

        /// <summary>天气枚举 → 中文词（LLM prompt 材料，铁律 13 豁免；与方案 G1 词表同口径）。
        /// 🔴 v1.2.12 枚举无 Storm（v1.3.0+ 新增），Storm 分支必须 #if MB2_GE_130。</summary>
        public static string WeatherWord(TaleWorlds.CampaignSystem.ComponentInterfaces.MapWeatherModel.WeatherEvent w)
        {
            switch (w)
            {
                // 本地化：LWN_word_weather_*（天气描述词，双桶）
                case TaleWorlds.CampaignSystem.ComponentInterfaces.MapWeatherModel.WeatherEvent.LightRain: return LWNTextHelper.ResolveText("LWN_word_weather_drizzle");
                // 本地化：LWN_word_weather_*（天气描述词，双桶）
                case TaleWorlds.CampaignSystem.ComponentInterfaces.MapWeatherModel.WeatherEvent.HeavyRain: return LWNTextHelper.ResolveText("LWN_word_weather_heavyrain");
                // 本地化：LWN_word_weather_*（天气描述词，双桶）
                case TaleWorlds.CampaignSystem.ComponentInterfaces.MapWeatherModel.WeatherEvent.Snowy: return LWNTextHelper.ResolveText("LWN_word_weather_snow");
                // 本地化：LWN_word_weather_*（天气描述词，双桶）
                case TaleWorlds.CampaignSystem.ComponentInterfaces.MapWeatherModel.WeatherEvent.Blizzard: return LWNTextHelper.ResolveText("LWN_word_weather_blizzard");
#if MB2_GE_130
                // 本地化：LWN_word_weather_*（天气描述词，双桶）
                case TaleWorlds.CampaignSystem.ComponentInterfaces.MapWeatherModel.WeatherEvent.Storm: return LWNTextHelper.ResolveText("LWN_word_weather_storm");
#endif
                // 本地化：LWN_word_weather_*（天气描述词，双桶）
                default: return LWNTextHelper.ResolveText("LWN_word_weather_clear");
            }
        }

        // ── Freeze/unfreeze player control（偷窃条输入隔离）────────────
        // v1.2.12: agent.Controller = Agent.ControllerType.AI/Player 切换
        // Latest:  agent.Controller = AgentControllerType.AI/Player（类型从嵌套枚举改名，setter 仍可用）
        //          切换后主角待机，跳/走/攻击全死。空格/ESC 轮询是原始设备状态与此无关，照常可用。

        public static void SetPlayerControlFrozen(Agent agent, bool frozen)
        {
            if (agent == null) return;
#if MB2_GE_130
            var target = frozen ? AgentControllerType.AI : AgentControllerType.Player;
            if (agent.Controller != target)
                agent.Controller = target;
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
#if MB2_GE_130
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
#if MB2_GE_130
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

        // ── SetPartyAiAction overloads (2-arg → 3～5-arg) ──
        // v1.2.12: SetPartyAiAction.GetActionFor*(party, settlement)
        // v1.3.0+: SetPartyAiAction.GetActionFor*(..., NavigationType, bool, ...)

        public static void PatrolAround(MobileParty party, Settlement settlement)
        {
            if (party == null || settlement == null) return;
#if MB2_GE_130
            SetPartyAiAction.GetActionForPatrollingAroundSettlement(party, settlement,
                MobileParty.NavigationType.Default, false, false);
#else
            SetPartyAiAction.GetActionForPatrollingAroundSettlement(party, settlement);
#endif
        }

        /// <summary>
        /// 集结：defender party 移向玩家 party（闲聊 Party 空间动作 GATHER_TO_PLAYER，§5.2）。
        /// 语义 = 护送玩家部队（SetPartyAiAction.EscortParty：跟随玩家 party 移动；反编译确认，
        /// EngageParty 是交战追击不适合集结）。版本差异：v1.2.12 = 2 参；v1.3.0+ = 5 参（实测签名一致）。
        /// </summary>
        public static void GatherToPlayer(MobileParty party)
        {
            if (party == null || MobileParty.MainParty == null) return;
#if MB2_GE_130
            SetPartyAiAction.GetActionForEscortingParty(party, MobileParty.MainParty,
                MobileParty.NavigationType.Default, false, false);
#else
            SetPartyAiAction.GetActionForEscortingParty(party, MobileParty.MainParty);
#endif
        }

        public static void RaidSettlement(MobileParty party, Settlement settlement)
        {
            if (party == null || settlement == null) return;
            // v1.4.0+：5 参（新增 isTargetingPort）；v1.3.x：4 参（无 isTargetingPort）；v1.2.12：2 参
#if MB2_GE_140
            SetPartyAiAction.GetActionForRaidingSettlement(party, settlement,
                MobileParty.NavigationType.Default, false, false);
#elif MB2_GE_130
            SetPartyAiAction.GetActionForRaidingSettlement(party, settlement,
                MobileParty.NavigationType.Default, false);
#else
            SetPartyAiAction.GetActionForRaidingSettlement(party, settlement);
#endif
        }

        public static void BesiegeSettlement(MobileParty party, Settlement settlement)
        {
            if (party == null || settlement == null) return;
#if MB2_GE_130
            SetPartyAiAction.GetActionForBesiegingSettlement(party, settlement,
                MobileParty.NavigationType.Default, false);
#else
            SetPartyAiAction.GetActionForBesiegingSettlement(party, settlement);
#endif
        }

        public static void EngageParty(MobileParty party, MobileParty target)
        {
            if (party == null || target == null) return;
#if MB2_GE_130
            SetPartyAiAction.GetActionForEngagingParty(party, target,
                MobileParty.NavigationType.Default, false);
#else
            SetPartyAiAction.GetActionForEngagingParty(party, target);
#endif
        }

        /// <summary>
        /// hero 的 stealth 装备层（v1.4.0+ 新增；v1.3.x 及更早的 Hero 无此属性）。
        /// 返回 null = 当前版本无此层，调用方跳过 stealth 处理即可。
        /// 用于偷窃/搜刮时清空英雄第三套装备（Battle/Civilian/Stealth 三套一致处理）。
        /// </summary>
        public static Equipment GetStealthEquipment(Hero hero)
        {
            if (hero == null) return null;
#if MB2_GE_140
            return hero.StealthEquipment;
#else
            return null;
#endif
        }

        /// <summary>
        /// party 上所有有实职的 职位→任职者 名列表（版本兼容，2026-08-21）：正向反查（职位→人），
        /// 与 GetPartyRoleKeys（人→职位）互补。用 Effective 四职位（全版本公开、带 PartyBelongedTo
        /// 校验，留守/失效职位天然排除）；船职位 1.4.x 独有（#if）。RoleKey = PartyRole 枚举名
        /// （对应 LWN_prompt_role_* 本地化键）；HeroName 为空串 = 取不到名（理论不发生）。
        /// 返回空列表 = 无职位或输入无效，调用方整段跳过即可。
        /// </summary>
        public static List<(string RoleKey, string HeroName)> GetPartyRoleHeroes(MobileParty party)
        {
            var result = new List<(string, string)>();
            if (party == null) return result;
            void Add(Hero h, string key)
            {
                if (h != null) result.Add((key, h.Name?.ToString() ?? ""));
            }
            Add(party.EffectiveQuartermaster, "Quartermaster");
            Add(party.EffectiveScout, "Scout");
            Add(party.EffectiveSurgeon, "Surgeon");
            Add(party.EffectiveEngineer, "Engineer");
#if MB2_GE_140
            Add(party.EffectiveFirstMate, "FirstMate");
            Add(party.EffectiveNavigator, "Navigator");
#endif
            return result;
        }

        /// <summary>
        /// hero 在指定部队中担任的职位键列表（版本兼容，2026-08-21）：
        /// v1.4.0+ 用 MobileParty.GetHeroPartyRoles 原生查询（含船长 Captain/FirstMate/Navigator）；
        /// v1.2.12/v1.3.x 无此 API，用 Effective 四职位手动比对（全版本存在，带 PartyBelongedTo 校验，
        /// 留守/驻扎者天然无职位）。返回枚举名字符串（"Quartermaster" 等），调用方用
        /// GameTexts.FindText("role", key) 取引擎本地化职位名（CampaignUIHelper.GetHeroClanRoleText 同源）。
        /// 返回空列表 = 无职位或输入无效，调用方直接跳过即可。
        /// </summary>
        public static List<string> GetPartyRoleKeys(MobileParty party, Hero hero)
        {
            var result = new List<string>();
            if (party == null || hero == null) return result;
#if MB2_GE_140
            var roles = party.GetHeroPartyRoles(hero);
            if (roles != null)
                result.AddRange(roles.Select(r => r.ToString()));
#else
            // 四职位：EffectiveXxx 全版本存在（v1.2.12/v1.3.x 无 GetHeroPartyRoles 且无船职位）
            if (party.EffectiveQuartermaster == hero) result.Add("Quartermaster");
            if (party.EffectiveScout == hero) result.Add("Scout");
            if (party.EffectiveSurgeon == hero) result.Add("Surgeon");
            if (party.EffectiveEngineer == hero) result.Add("Engineer");
#endif
            return result;
        }

        // ── Navigation mesh snap (in/ref + return-type difference) ──
        // v1.2.12: scene.GetNavigationMeshForPosition(ref pos, out faceIndex) → bool
        // v1.3.0+: scene.GetNavigationMeshForPosition(in pos, out faceIndex, 1.5f, false) → UIntPtr

        public static void NavMeshSnap(Scene scene, ref Vec3 position)
        {
            if (scene == null) return;
#if MB2_GE_130
            scene.GetNavigationMeshForPosition(in position, out _, 1.5f, false);
#else
            scene.GetNavigationMeshForPosition(ref position, out _);
#endif
        }

        // ── NavigationMeshWrapper helpers ──
        // v1.2.12: wrapper.GetAccessiblePointNearPosition(Vec2, float) → Vec2
        // v1.3.0+: wrapper.GetAccessiblePointNearPosition(CampaignVec2, float) → CampaignVec2

        public static Vec2 AccessiblePointNear(IMapScene wrapper, Vec2 pos, float radius)
        {
            if (wrapper == null) return pos;
#if MB2_GE_130
            return wrapper.GetAccessiblePointNearPosition(
                new CampaignVec2(pos, true), radius).ToVec2();
#else
            return wrapper.GetAccessiblePointNearPosition(pos, radius);
#endif
        }

        // v1.2.12: wrapper.GetFaceIndex(Vec2) → PathFaceRecord
        // v1.3.0+: wrapper.GetFaceIndex(CampaignVec2) → PathFaceRecord

        public static PathFaceRecord FaceIndex(IMapScene wrapper, Vec2 pos)
        {
            if (wrapper == null) return default;
#if MB2_GE_130
            return wrapper.GetFaceIndex(new CampaignVec2(pos, true));
#else
            return wrapper.GetFaceIndex(pos);
#endif
        }

        // ── Camera animation ──
        // v1.2.12: mapState.Handler.StartCameraAnimation(Vec2, float)
        // v1.3.0+: mapState.Handler.StartCameraAnimation(CampaignVec2, float)

        public static void CameraAnimate(MapState mapState, Vec2 pos, float duration)
        {
            if (mapState?.Handler == null) return;
#if MB2_GE_130
            mapState.Handler.StartCameraAnimation(new CampaignVec2(pos, true), duration);
#else
            mapState.Handler.StartCameraAnimation(pos, duration);
#endif
        }

        // ── Loot/inventory screen ────────────────────────────────────
        // v1.2.12: InventoryManager.OpenScreenAsLoot(dict)（TaleWorlds.CampaignSystem.Inventory）
        // v1.3.0+: 类整体改名 InventoryScreenHelper（namespace Helpers），方法签名不变。
        //   打开"战利品挑选"库存界面；itemRosters 以 PartyBase.MainParty 为键，roster 会被原地修改。
        //   使用场景：偷窃/搜刮/开箱的"自己挑选"（InteractionMissionView）。

        public static void OpenLootScreen(Dictionary<PartyBase, ItemRoster> itemRosters)
        {
            if (itemRosters == null) return;
#if MB2_GE_130
            InventoryScreenHelper.OpenScreenAsLoot(itemRosters);
#else
            InventoryManager.OpenScreenAsLoot(itemRosters);
#endif
        }

        // ── UI 层死层判定 / 焦点设置 / 软键盘（2026-08-21，1.2.12 兼容）────────────────────────
        // v1.2.12: Layer.Finalized / EventManager.SetWidgetFocused(w)（setter private）/
        //          无软键盘 API（InputSystem.dll 无 OnScreenKeyboard 字样，二进制 grep 实锤）
        // v1.3.0+: Layer.IsFinalized / EventManager.FocusedWidget 公开 setter / Input.IsOnScreenKeyboardActive
        // 使用场景：IM 层归属迁移（IsFinalized 权威死层标志）、手柄焦点再固守 + CT/KT 聚焦输入框、
        //          软键盘降级预案（1.2.12 恒 false = 无软键盘 → 十字键退出输入态路径照常）。

        /// <summary>层是否已 Finalize（死层权威标志）。v1.2.12 属性名 Finalized，v1.3.0+ 改名 IsFinalized
        ///（类 = ScreenLayer，两版本同名——GauntletLayer : ScreenLayer）。</summary>
        /// <summary>层是否已 Finalize（死层）。🔴 null 层 = 视为已死（true）——调用方对 dead=true
        /// 的处理 = 跳过一切引擎操作，语义安全（实机 2026-09-03：PerfHud 未判 null 直接调本方法 NRE，
        /// 调用方三层判断极易漏一层，工具方法自身兜底）。</summary>
        public static bool LayerFinalized(TaleWorlds.ScreenSystem.ScreenLayer layer)
        {
            if (layer == null) return true;
#if MB2_GE_130
            return layer.IsFinalized;
#else
            return layer.Finalized;
#endif
        }

        /// <summary>设置 EventManager 焦点 widget。v1.2.12 公开 setter 不存在（private set），
        /// 用 SetWidgetFocused（同语义：OnLoseFocus/OnGainFocus + 控制器激活时弹软键盘）；v1.3.0+ 用公开 setter。
        /// Widget 类两版本都在 TaleWorlds.GauntletUI.BaseTypes 命名空间（TaleWorlds.GauntletUI 下无 Widget，
        /// 1.2.12 编译实锤 CS0234）。</summary>
        public static void SetFocusedWidget(TaleWorlds.GauntletUI.EventManager eventManager, TaleWorlds.GauntletUI.BaseTypes.Widget w)
        {
            if (eventManager == null) return;
#if MB2_GE_130
            eventManager.FocusedWidget = w;
#else
            eventManager.SetWidgetFocused(w);
#endif
        }

        /// <summary>软键盘是否激活。v1.2.12 无此 API → 恒 false（1.2.12 无软键盘机制）。</summary>
        public static bool IsOnScreenKeyboardActive()
        {
#if MB2_GE_130
            return TaleWorlds.InputSystem.Input.IsOnScreenKeyboardActive;
#else
            return false;
#endif
        }

        /// <summary>全部已加载 Mod 的 SubModule 实例列表（性能诊断 B 层包裹用）。
        /// 🔴 版本分歧 API：1.2.12 = Module.GetInstance().SubModules（1.2.12:Mission.cs:3485 引擎用法）；
        ///    1.3.15+ = Module.CurrentModule.CollectSubModules()（1.3.15:Mission.cs:3759 引擎用法）。
        /// CurrentModule 可能为 null（加载早期）→ 返回空列表。</summary>
        public static IEnumerable<MBSubModuleBase> CollectSubModules()
        {
#if MB2_GE_130
            var list = TaleWorlds.MountAndBlade.Module.CurrentModule?.CollectSubModules();
            return list ?? (IEnumerable<MBSubModuleBase>)new MBSubModuleBase[0];
#else
            return TaleWorlds.MountAndBlade.Module.GetInstance().SubModules;
#endif
        }
    }
}
