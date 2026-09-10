using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.CampaignMode
{
    /// <summary>
    /// navmesh 可视化共享绘制核心（2026-09-10）——Mission（场景）与 Campaign（大地图）两宿主共用。
    ///
    /// 宿主 tick：场景 = <see cref="NavMeshDebugMissionView"/>（MissionBehavior）；
    ///          大地图 = <see cref="NavMeshDebugMapTickPatch"/>（ScreenBase.OnFrameTick，暂停也触发）。
    /// 命令与开关状态见 <see cref="NavMeshDebugCommands"/>。
    ///
    /// 两世界机制同源（引擎同一套 navmesh）：Mission 有 Agent、场景坐标；Campaign 有 MobileParty、
    /// 地图坐标（== MapScene 世界坐标，经 MapSceneWrapper 内部同一 _scene 查询）。
    ///
    /// 🔴 颜色打包 = 0xAARRGGBB（默认 uint.MaxValue = 不透明白）。若实机色相异常（红蓝互换）改本表。
    /// 🔴 绘制通道 MBDebug.RenderDebug* 带 [Conditional("_RGL_KEEP_ASSERTS")]，csproj 必须定义该符号。
    /// ⚠️ 大地图上调试渲染是否实际显示 = 待实测项（引擎调试渲染经 MapScene 渲染循环，理论有效）。
    /// </summary>
    public static class NavMeshDebugRenderer
    {
        // ── 面球配色（按面组 ID）──
        public const uint ColInsideCastle = 0xFF3FA9F5u;   // 面组 1  = 城内 / 墙上（TeamAISiegeComponent.InsideCastleNavMeshID）
        public const uint ColGate = 0xFFFFD700u;           // 面组 7  = 门（GateNavMeshId）
        public const uint ColDisabledGroup = 0xFFFF4444u;  // 面组 8  = 默认禁用组（DisabledNavMeshID）
        public const uint ColLadderBridge = 0xFF44FF44u;   // 面组 13 = 梯子 / 地面到桥（SiegeLadder.OverTheWallNavMeshID）
        public const uint ColDisabledFace = 0xFF802020u;   // 运行时被禁用的面（暗红）

        // ── 路径 / 监视对象配色（与面球刻意区分）──
        public const uint ColPath = 0xFF00FF88u;           // nav_path 两点路径折线（绿）
        public const uint ColPathFail = 0xFFFF2200u;       // 端点无效 / 不可达（红）
        public const uint ColWatchedBody = 0xFFFFFFFFu;    // 白  = 被监视 agent / party 本体
        public const uint ColWatchedPath = 0xFF00E5FFu;    // 青  = 被监视对象 脚下→目标 的实时路径
        public const uint ColWatchedTarget = 0xFFFF2FD6u;  // 品红 = 目标箭头 + 目标点

        private static readonly uint[] Palette =
        {
            0xFFE0E0E0u, 0xFF9ACD32u, 0xFF00CED1u, 0xFFDA70D6u,
            0xFFF4A460u, 0xFF7B68EEu, 0xFF66CDAAu, 0xFFCD5C5Cu
        };

        private static int _faceCursor;
        private static float _lastLogSecond = -100f;
        private static readonly NavigationPath PathBuffer = new NavigationPath();

        /// <summary>活动场景：Mission 优先，其次战役大地图（MapScene.Scene）——TerrainExportCommands 同款范式。</summary>
        public static Scene GetActiveScene()
        {
            Mission mission = Mission.Current;
            if (mission?.Scene != null)
                return mission.Scene;
            if (Campaign.Current != null && Campaign.Current.MapSceneWrapper is SandBox.MapScene mapScene)
                return mapScene.Scene;
            return null;
        }

        /// <summary>是否在大地图（campaign 已加载且不在 mission 中）。</summary>
        public static bool IsCampaignMap => Mission.Current == null && Campaign.Current != null;

        /// <summary>大地图侧绘制入口（由 NavMeshDebugMapTickPatch 每帧调用）。</summary>
        public static void TickCampaignMap(Scene scene)
        {
            MobileParty mainParty = MobileParty.MainParty;
            Vec3 refPos = mainParty != null ? ToDrawPos(scene, mainParty.Position2D) : Vec3.Zero;

            if (NavMeshDebugState.Enabled)
                DrawFaceSpheres(scene, refPos);
            if (NavMeshDebugState.HasPathRequest)
                DrawPathRaw(scene, NavMeshDebugState.PathStart, NavMeshDebugState.PathEnd, ColPath, "nav_path");
            if (NavMeshDebugState.WatchedParty != null)
                DrawPartyWatch(scene);
        }

        /// <summary>
        /// 面球：参考点周围 NavMeshDebugState.Radius 米内的面，按面组 ID 配色；
        /// 运行时被禁用的面（SetAbilityOfFacesWithId 关掉的）画暗红。
        /// 分片轮转：每帧只查 MaxFacesPerFrame 个面（场景面数可达上万，全量每帧会卡）。
        /// </summary>
        public static void DrawFaceSpheres(Scene scene, Vec3 refPos)
        {
            int faceCount = scene.GetNavMeshFaceCount();
            if (faceCount <= 0)
                return;

            float radiusSq = NavMeshDebugState.Radius * NavMeshDebugState.Radius;
            int budget = Math.Max(50, Math.Min(NavMeshDebugState.MaxFacesPerFrame, faceCount));

            int i = _faceCursor;
            for (int checkedCount = 0; checkedCount < budget; checkedCount++)
            {
                Vec3 center = Vec3.Zero;
                scene.GetNavMeshCenterPosition(i, ref center);

                if (center.DistanceSquared(refPos) <= radiusSq)
                {
                    int groupId = scene.GetIdOfNavMeshFace(i);

                    // 禁用面检测：checkIfDisabled=true 查不到 = 该面被运行时禁用。
                    // ⚠️ 面中心恰好落在相邻面上会误判（调试用途可接受）；只对半径内的面做，成本可控。
                    PathFaceRecord rec = PathFaceRecord.NullFaceRecord;
                    scene.GetNavMeshFaceIndex(ref rec, center.AsVec2, true);
                    bool disabled = !rec.IsValid();

                    uint color = disabled ? ColDisabledFace : ColorForGroup(groupId);
                    Vec3 drawPos = center + new Vec3(0f, 0f, 0.3f);
                    MBDebug.RenderDebugSphere(drawPos, disabled ? 0.35f : 0.25f, color, false, 0.5f);
                    if (NavMeshDebugState.ShowFaceText)
                        MBDebug.RenderDebugText3D(drawPos + new Vec3(0f, 0f, 0.8f), groupId.ToString(), color, 0, 0, 0.5f);
                }

                i++;
                if (i >= faceCount)
                    i = 0;
            }
            _faceCursor = i;
        }

        /// <summary>
        /// 两点寻路折线（每帧重算 → 面组开关改动即时反映）。
        /// 成功 = 画 okColor 折线；失败（端点无面 / 不可达）= 两端画 ColPathFail 红球 + 节流日志。
        /// </summary>
        public static bool DrawPathRaw(Scene scene, Vec2 start, Vec2 end, uint okColor, string logTag)
        {
            if (start.DistanceSquared(end) < 1f)
                return false; // 目标 ≈ 起点，无路径可画

            PathFaceRecord startFace = PathFaceRecord.NullFaceRecord;
            PathFaceRecord endFace = PathFaceRecord.NullFaceRecord;
            scene.GetNavMeshFaceIndex(ref startFace, start, false);
            scene.GetNavMeshFaceIndex(ref endFace, end, false);

            if (!startFace.IsValid() || !endFace.IsValid())
            {
                ThrottledLog($"{logTag}: 端点无导航面 start_valid={startFace.IsValid()} end_valid={endFace.IsValid()}");
                MBDebug.RenderDebugSphere(ToDrawPos(scene, start), 0.6f, ColPathFail, false, 0.3f);
                MBDebug.RenderDebugSphere(ToDrawPos(scene, end), 0.6f, ColPathFail, false, 0.3f);
                return false;
            }

            if (!scene.GetPathBetweenAIFaces(startFace.FaceIndex, endFace.FaceIndex, start, end, 0.5f, PathBuffer) || PathBuffer.Size <= 0)
            {
                ThrottledLog($"{logTag}: 两点间无路径（不可达，或路径所需面被禁用）");
                MBDebug.RenderDebugSphere(ToDrawPos(scene, start), 0.6f, ColPathFail, false, 0.3f);
                MBDebug.RenderDebugSphere(ToDrawPos(scene, end), 0.6f, ColPathFail, false, 0.3f);
                return false;
            }

            Vec3 prev = ToDrawPos(scene, PathBuffer[0]);
            MBDebug.RenderDebugSphere(prev, 0.45f, okColor, false, 0.3f);
            for (int k = 1; k < PathBuffer.Size; k++)
            {
                Vec3 cur = ToDrawPos(scene, PathBuffer[k]);
                MBDebug.RenderDebugLine(prev + new Vec3(0f, 0f, 0.5f), cur - prev, okColor, false, 0.3f);
                prev = cur;
            }
            MBDebug.RenderDebugSphere(prev, 0.55f, okColor, false, 0.3f);
            return true;
        }

        /// <summary>
        /// 被监视 Agent：白球（本体）+ 头顶标签 + 品红箭头（GetTargetDirection 引擎直给方向）
        /// + 青线（脚下 → GetTargetPosition 目标的实时重算路径）。
        /// agent 内部路径在 native 不暴露 —— 青线 = "它接下来最可能走的路线"。
        /// </summary>
        public static void DrawAgentWatch(Scene scene, Agent watched)
        {
            Vec3 agentPos = watched.Position;
            Vec3 bodyPos = agentPos + new Vec3(0f, 0f, 0.8f);
            MBDebug.RenderDebugSphere(bodyPos, 0.5f, ColWatchedBody, false, 0.1f);
            MBDebug.RenderDebugText3D(agentPos + new Vec3(0f, 0f, 2.2f),
                $"#{watched.Index} {watched.Name}", ColWatchedBody, 0, 0, 0.1f);

            Vec3 targetDir = watched.GetTargetDirection();
            if (targetDir.LengthSquared > 0.01f)
                MBDebug.RenderDebugDirectionArrow(bodyPos, targetDir.NormalizedCopy(), ColWatchedTarget, false);

            Vec2 agentFace2D = agentPos.AsVec2;
            Vec2 targetPos = watched.GetTargetPosition();
            if (targetPos.DistanceSquared(agentFace2D) < 1f)
                return;

            MBDebug.RenderDebugSphere(ToDrawPos(scene, targetPos), 0.5f, ColWatchedTarget, false, 0.1f);
            DrawPathRaw(scene, agentFace2D, targetPos, ColWatchedPath, $"agent #{watched.Index}");
        }

        /// <summary>
        /// 被监视 MobileParty（大地图）：白球 + 名称 + 品红箭头（朝向 TargetPosition 推算）
        /// + 青线（当前位置 → TargetPosition 的实时重算路径）。
        /// ⚠️ party 版箭头方向为自算（TargetPosition - Position2D）——MobileParty 无 GetTargetDirection。
        /// </summary>
        private static void DrawPartyWatch(Scene scene)
        {
            MobileParty party = NavMeshDebugState.WatchedParty;
            if (party == null)
                return;
            if (!party.IsActive)
            {
                ThrottledLog("被监视 party 已不存在，监视自动取消（nav_party list 重新选择）");
                NavMeshDebugState.WatchedParty = null;
                return;
            }

            Vec2 pos = party.Position2D;
            Vec2 target = party.TargetPosition;
            Vec3 bodyPos = ToDrawPos(scene, pos) + new Vec3(0f, 0f, 1.0f);
            MBDebug.RenderDebugSphere(bodyPos, 0.8f, ColWatchedBody, false, 0.1f);
            MBDebug.RenderDebugText3D(bodyPos + new Vec3(0f, 0f, 1.5f),
                party.Name?.ToString() ?? "party", ColWatchedBody, 0, 0, 0.1f);

            Vec3 dir = new Vec3(target.X - pos.X, target.Y - pos.Y, 0f);
            if (dir.LengthSquared > 0.01f)
                MBDebug.RenderDebugDirectionArrow(bodyPos, dir.NormalizedCopy(), ColWatchedTarget, false);

            if (target.DistanceSquared(pos) < 1f)
                return;
            MBDebug.RenderDebugSphere(ToDrawPos(scene, target), 0.8f, ColWatchedTarget, false, 0.1f);
            DrawPathRaw(scene, pos, target, ColWatchedPath, $"party {party.Name}");
        }

        /// <summary>路径点（Vec2，无高度）→ 带 navmesh 面高度的绘制坐标（抬高 0.4m 防 z-fighting）。</summary>
        private static Vec3 ToDrawPos(Scene scene, Vec2 point)
        {
            float z = 0f;
            PathFaceRecord rec = PathFaceRecord.NullFaceRecord;
            scene.GetNavMeshFaceIndex(ref rec, point, false);
            if (rec.IsValid())
                z = scene.GetNavMeshFaceFirstVertexZ(rec.FaceIndex);
            return new Vec3(point.X, point.Y, z + 0.4f);
        }

        /// <summary>无效/不可达状态日志节流：每 3 秒最多一条（墙钟计时——campaign 暂停时游戏时间不走）。</summary>
        private static void ThrottledLog(string message)
        {
            float now = Environment.TickCount / 1000f;
            if (now - _lastLogSecond < 3f)
                return;
            _lastLogSecond = now;
            DebugLogger.Log($"[NavDebug] {message}");
        }

        private static uint ColorForGroup(int groupId)
        {
            switch (groupId)
            {
                case 1: return ColInsideCastle;
                case 7: return ColGate;
                case 8: return ColDisabledGroup;
                case 13: return ColLadderBridge;
                default: return Palette[groupId & 7];
            }
        }
    }
}
