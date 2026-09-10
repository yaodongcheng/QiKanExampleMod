using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Mission 场景内 navmesh 可视化调试（2026-09-10）。
    ///
    /// 用途：建城 / 攻城 / 动态导航件（ImportNavigationMeshPrefab + SetAbilityOfFacesWithId）调试时，
    /// 在游戏内实时查看——
    ///   ① 玩家周围 navmesh 面：每面画球，按面组 ID 配色；运行时被禁用的面画暗红（动态开关效果肉眼可见）
    ///   ② 任意两点间 AI 寻路路径：折线，每帧重算 → 面组开关改动即时反映在路径上
    ///   ③ 任意坐标的导航面详情：面序号 / 面组 ID / 是否禁用 / 高度 / 所属岛
    ///
    /// 机制背景（1.2.12 反编译实证，详见 Knowledge/骑砍2动态Navmesh机制分析.md）：
    ///   navmesh 无运行时重建——所有可能状态的路在场景烘焙期就存在，按 NavMeshId 分组；
    ///   运行时 Scene.SetAbilityOfFacesWithId(id, enabled) 整组开关，寻路自动绕开禁用面。
    ///   本工具的面组配色 + 禁用面暗红 = 直接观察这套开关的生效情况。
    ///
    /// 用法（游戏内 `~` 控制台，返回文本纯英文；诊断详情走 DebugLogger 中文）：
    ///   custom.nav_debug                  # 开关面显示（翻转）
    ///   custom.nav_debug on|off           # 显式开关
    ///   custom.nav_debug radius 40        # 面显示半径（米，默认 30）
    ///   custom.nav_debug faces 600        # 每帧处理面上限（默认 400，分片轮转覆盖全场景）
    ///   custom.nav_debug text on|off      # 面上叠加面组 ID 文本（默认关，面多时开销大）
    ///   custom.nav_debug reset            # 🔴 一键全关（面球+路径+agent监视+party监视 + 立即清屏）
    ///   custom.nav_path x y               # 画「玩家当前位置 → (x,y)」寻路折线
    ///   custom.nav_path x1 y1 x2 y2       # 画任意两点寻路折线
    ///   custom.nav_path clear             # 清除路径显示
    ///   custom.nav_face x y               # 查该坐标的导航面详情（DebugLogger + 英文摘要）
    ///   custom.nav_agent                  # 【仅场景】监视玩家最近的 agent（绘制其路线 + 目标箭头，特殊配色）
    ///   custom.nav_agent <index>          # 【仅场景】监视指定 Agent.Index 的 agent
    ///   custom.nav_agent list             # 【仅场景】列出附近 40m 内 agent（index/名字/距离）供选择
    ///   custom.nav_agent clear            # 【仅场景】取消 agent 监视
    ///   custom.nav_party                  # 【仅大地图】监视主队
    ///   custom.nav_party nearest          # 【仅大地图】监视最近的其他 party
    ///   custom.nav_party list             # 【仅大地图】列出主队 + 60m 内 party（带序号）
    ///   custom.nav_party <seq>            # 【仅大地图】按 list 序号选择
    ///   custom.nav_party clear            # 【仅大地图】取消 party 监视
    ///
    /// 双世界自动分派（骑砍2 的 Campaign 与 Mission 是两个宿主）：
    ///   场景（Mission） 有 Agent（无 MobileParty）→ nav_agent / nav_path / nav_face 走 Mission.Scene；
    ///   大地图（Campaign）有 MobileParty（无 Agent）→ nav_party / nav_path / nav_face 走 MapScene.Scene。
    ///   两者是同一套引擎 navmesh（MapSceneWrapper 内部转调同一 Scene API），仅载体不同。
    ///   绘制触发：场景 = NavMeshDebugMissionView（MissionBehavior）；大地图 = NavMeshDebugMapTickPatch（ScreenBase.OnFrameTick，暂停也画）。
    ///   ⚠️ 大地图上调试渲染是否实际显示 = 待实测（见 NavMeshDebugMapTickPatch 注释）。
    ///
    /// 🔴 agent 路线口径：骑砍2 的 agent 内部路径在 native 不暴露公开 API——本工具画的是
    ///   「agent 脚下 → 它当前 AI 目标（PlayerAgent.GetTargetPosition）」的实时重算路径，
    ///   同起终点下引擎寻路是确定性的，即"它接下来最可能走的路线"；箭头方向用引擎直给的
    ///   GetTargetDirection（不自行推算）。
    ///
    /// 🔴 绘制通道纪律：绘制走 MBDebug.RenderDebug*（TaleWorlds.Engine）——这些方法全部带
    ///   [Conditional("_RGL_KEEP_ASSERTS")]，csproj 必须在 DefineConstants 定义 _RGL_KEEP_ASSERTS，
    ///   否则调用点被编译器静默剥离（代码编译通过但画面什么都不显示）。
    /// 🔴 颜色打包 = 0xAARRGGBB（默认 uint.MaxValue = 不透明白）。若实机色相异常（红蓝互换），
    ///   改 NavMeshDebugMissionView 的色表即可。
    ///
    /// 绘制循环在 <see cref="NavMeshDebugMissionView"/>（每帧 tick，仅开关打开时干活）。
    /// </summary>
    public static class NavMeshDebugState
    {
        /// <summary>面可视化总开关（static，跨 mission 保持——退出场景重进仍在）。</summary>
        public static bool Enabled;

        /// <summary>面显示半径（米）。</summary>
        public static float Radius = 30f;

        /// <summary>每帧处理的面数上限（场景面数多时靠分片轮转逐帧覆盖）。</summary>
        public static int MaxFacesPerFrame = 400;

        /// <summary>是否在面上叠加面组 ID 文本（默认关）。</summary>
        public static bool ShowFaceText;

        /// <summary>路径显示请求（由 custom.nav_path 设置，Mission 视图每帧重算重画）。</summary>
        public static bool HasPathRequest;

        public static Vec2 PathStart;
        public static Vec2 PathEnd;

        /// <summary>被监视 agent 的 Agent.Index（-1 = 不监视；由 custom.nav_agent 设置；仅场景）。</summary>
        public static int WatchedAgentIndex = -1;

        /// <summary>被监视的 MobileParty（null = 不监视；由 custom.nav_party 设置；仅大地图）。
        /// 直接存 party 引用（party 无稳定数字 id）；失效检测 = IsActive。</summary>
        public static MobileParty WatchedParty;

        /// <summary>nav_party list 的候选表（供 nav_party &lt;seq&gt; 选择；每次 list 重建）。</summary>
        public static List<MobileParty> LastPartyCandidates = new List<MobileParty>();
    }

    public class NavMeshDebugCommands
    {
        /* 🔴 控制台命令注册纪律（同 NavMeshProbeCommands）：委托签名 = public static string F(List<string>)，
           签名/可见性不符 = 启动时绑定失败 ArgumentException。返回值纯英文（铁律），中文只进 DebugLogger。 */

        [CommandLineFunctionality.CommandLineArgumentFunction("nav_debug", "custom")]
        public static string NavDebug(List<string> args)
        {
            try
            {
                if (args.Count == 0)
                {
                    NavMeshDebugState.Enabled = !NavMeshDebugState.Enabled;
                }
                else
                {
                    switch (args[0].ToLowerInvariant())
                    {
                        case "on":
                            NavMeshDebugState.Enabled = true;
                            break;
                        case "off":
                            NavMeshDebugState.Enabled = false;
                            break;
                        case "radius":
                            if (args.Count < 2 || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float radius))
                                return "usage: nav_debug radius <meters>";
                            NavMeshDebugState.Radius = Math.Max(5f, Math.Min(300f, radius));
                            break;
                        case "faces":
                            if (args.Count < 2 || !int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int faceBudget))
                                return "usage: nav_debug faces <count>";
                            NavMeshDebugState.MaxFacesPerFrame = Math.Max(50, Math.Min(5000, faceBudget));
                            break;
                        case "text":
                            if (args.Count < 2)
                                return "usage: nav_debug text on|off";
                            NavMeshDebugState.ShowFaceText = string.Equals(args[1], "on", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "reset":
                            // 一键全关：面球 + 路径 + agent 监视 + party 监视，并立即清掉已排入的调试图元
                            // （MBDebug 图元带 time 参数，不清的话最大还会残留 ~0.5s）。
                            // ⚠️ ClearRenderObjects 清的是引擎全部调试图元——目前只有本套工具在用。
                            NavMeshDebugState.Enabled = false;
                            NavMeshDebugState.HasPathRequest = false;
                            NavMeshDebugState.WatchedAgentIndex = -1;
                            NavMeshDebugState.WatchedParty = null;
                            MBDebug.ClearRenderObjects();
                            DebugLogger.Log("[NavDebug] 全部 navmesh 显示已关闭（面球 / 路径 / agent 监视 / party 监视）");
                            return "all nav debug overlays disabled";
                        default:
                            return "usage: nav_debug [on|off|radius <m>|faces <n>|text on|off|reset]";
                    }
                }

                DebugLogger.Log($"[NavDebug] 面显示={NavMeshDebugState.Enabled} 半径={NavMeshDebugState.Radius:F0}m " +
                    $"每帧面数={NavMeshDebugState.MaxFacesPerFrame} 面组文本={NavMeshDebugState.ShowFaceText}" +
                    $"（面组配色: 1城内/7门/8默认禁用组/13梯桥/暗红=运行时禁用）");
                return $"nav_debug enabled={NavMeshDebugState.Enabled} radius={NavMeshDebugState.Radius:F0} " +
                    $"faces_per_frame={NavMeshDebugState.MaxFacesPerFrame} text={NavMeshDebugState.ShowFaceText}";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[NavDebug] nav_debug 异常: {ex}");
                return "nav_debug_failed: " + ex.Message;
            }
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("nav_path", "custom")]
        public static string NavPath(List<string> args)
        {
            try
            {
                if (args.Count == 1 && string.Equals(args[0], "clear", StringComparison.OrdinalIgnoreCase))
                {
                    NavMeshDebugState.HasPathRequest = false;
                    DebugLogger.Log("[NavDebug] 路径显示已清除");
                    return "nav_path cleared";
                }

                if (Mission.Current?.Scene == null && !NavMeshDebugRenderer.IsCampaignMap)
                    return "not in a mission or campaign";

                Vec2 start;
                Vec2 end;
                if (args.Count == 2 && TryParseVec2(args[0], args[1], out end))
                {
                    // 两点模式：起点 = 玩家当前位置（场景 = Agent.Main，大地图 = MainParty）
                    if (Mission.Current?.Scene != null)
                    {
                        Agent mainAgent = Agent.Main;
                        if (mainAgent == null)
                            return "player agent not found";
                        start = mainAgent.Position.AsVec2;
                    }
                    else
                    {
                        MobileParty mainParty = MobileParty.MainParty;
                        if (mainParty == null)
                            return "main party not found";
                        start = mainParty.Position2D;
                    }
                }
                else if (args.Count == 4 && TryParseVec2(args[0], args[1], out start) && TryParseVec2(args[2], args[3], out end))
                {
                    // 四点模式：任意两点
                }
                else
                {
                    return "usage: nav_path x y | x1 y1 x2 y2 | clear";
                }

                NavMeshDebugState.PathStart = start;
                NavMeshDebugState.PathEnd = end;
                NavMeshDebugState.HasPathRequest = true;
                DebugLogger.Log($"[NavDebug] 路径请求 start=({start.X:F1},{start.Y:F1}) end=({end.X:F1},{end.Y:F1})" +
                    "（每帧重算绘制，面组开关改动即时反映；custom.nav_path clear 关闭）");
                return $"nav_path from=({start.X:F1},{start.Y:F1}) to=({end.X:F1},{end.Y:F1}) " +
                    "drawn every frame, use 'nav_path clear' to stop";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[NavDebug] nav_path 异常: {ex}");
                return "nav_path_failed: " + ex.Message;
            }
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("nav_face", "custom")]
        public static string NavFace(List<string> args)
        {
            try
            {
                if (args.Count < 2 || !TryParseVec2(args[0], args[1], out Vec2 position))
                    return "usage: nav_face x y";

                // 场景与大地图通用：MapScene.Scene 与 Mission.Scene 是同一套引擎 Scene API
                Scene scene = NavMeshDebugRenderer.GetActiveScene();
                if (scene == null)
                    return "no active scene (not in a mission or campaign)";
                PathFaceRecord recAny = PathFaceRecord.NullFaceRecord;
                PathFaceRecord recUsable = PathFaceRecord.NullFaceRecord;
                scene.GetNavMeshFaceIndex(ref recAny, position, false);
                scene.GetNavMeshFaceIndex(ref recUsable, position, true);

                int totalFaces = scene.GetNavMeshFaceCount();
                if (!recAny.IsValid())
                {
                    DebugLogger.Log($"[NavDebug] nav_face ({position.X:F1},{position.Y:F1})：无导航面" +
                        $"（场景外 / 建筑内部 / 未烘焙区域） totalFaces={totalFaces}");
                    return $"face=invalid total_faces={totalFaces}";
                }

                // checkIfDisabled=true 查不到 = 该面被运行时禁用（SetAbilityOfFacesWithId 关掉了它）
                bool disabled = !recUsable.IsValid();
                int groupId = scene.GetIdOfNavMeshFace(recAny.FaceIndex);
                float faceZ = scene.GetNavMeshFaceFirstVertexZ(recAny.FaceIndex);
                DebugLogger.Log($"[NavDebug] nav_face ({position.X:F1},{position.Y:F1})：faceIndex={recAny.FaceIndex} " +
                    $"groupId={groupId} z={faceZ:F2} disabled={disabled} island={recAny.FaceIslandIndex} " +
                    $"group={recAny.FaceGroupIndex} totalFaces={totalFaces}");
                return $"face_index={recAny.FaceIndex} group_id={groupId} z={faceZ:F2} " +
                    $"disabled={disabled} total_faces={totalFaces}";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[NavDebug] nav_face 异常: {ex}");
                return "nav_face_failed: " + ex.Message;
            }
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("nav_agent", "custom")]
        public static string NavAgent(List<string> args)
        {
            try
            {
                Mission mission = Mission.Current;
                if (mission?.Scene == null)
                    return "agents exist only in missions — on the campaign map use 'nav_party'";

                if (args.Count == 1 && string.Equals(args[0], "clear", StringComparison.OrdinalIgnoreCase))
                {
                    NavMeshDebugState.WatchedAgentIndex = -1;
                    DebugLogger.Log("[NavDebug] agent 监视已取消");
                    return "nav_agent cleared";
                }

                if (args.Count == 1 && string.Equals(args[0], "list", StringComparison.OrdinalIgnoreCase))
                {
                    Agent mainAgent = Agent.Main;
                    if (mainAgent == null)
                        return "player agent not found";
                    var lines = new List<string>();
                    foreach (Agent agent in mission.Agents)
                    {
                        if (agent == mainAgent || !agent.IsActive())
                            continue;
                        float dist = agent.Position.Distance(mainAgent.Position);
                        if (dist > 40f)
                            continue;
                        lines.Add($"#{agent.Index} {agent.Name} {dist:F0}m");
                        if (lines.Count >= 12)
                            break;
                    }
                    DebugLogger.Log($"[NavDebug] nav_agent list（40m 内，最多 12 个）: {(lines.Count > 0 ? string.Join(" | ", lines) : "无")}");
                    return lines.Count > 0 ? string.Join(" | ", lines) : "no agent within 40m";
                }

                Agent target = null;
                if (args.Count == 0 || (args.Count == 1 && string.Equals(args[0], "nearest", StringComparison.OrdinalIgnoreCase)))
                {
                    Agent mainAgent = Agent.Main;
                    if (mainAgent == null)
                        return "player agent not found";
                    float bestSq = float.MaxValue;
                    foreach (Agent agent in mission.Agents)
                    {
                        if (agent == mainAgent || !agent.IsActive())
                            continue;
                        float distSq = agent.Position.DistanceSquared(mainAgent.Position);
                        if (distSq < bestSq)
                        {
                            bestSq = distSq;
                            target = agent;
                        }
                    }
                    if (target == null)
                        return "no active agent found";
                }
                else if (args.Count == 1 && int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int wantedIndex))
                {
                    foreach (Agent agent in mission.Agents)
                    {
                        if (agent.IsActive() && agent.Index == wantedIndex)
                        {
                            target = agent;
                            break;
                        }
                    }
                    if (target == null)
                        return $"agent #{wantedIndex} not found (use 'nav_agent list' for candidates)";
                }
                else
                {
                    return "usage: nav_agent [<index>|nearest|list|clear]";
                }

                NavMeshDebugState.WatchedAgentIndex = target.Index;
                DebugLogger.Log($"[NavDebug] 监视 agent #{target.Index} {target.Name}" +
                    $"（绘制：白球=本体 青线=脚下→目标的实时路径 品红箭头=引擎目标方向 品红球=目标点；nav_agent clear 取消）");
                return $"watching agent #{target.Index} name='{target.Name}' — magenta arrow = target direction, cyan line = live path";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[NavDebug] nav_agent 异常: {ex}");
                return "nav_agent_failed: " + ex.Message;
            }
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("nav_party", "custom")]
        public static string NavParty(List<string> args)
        {
            try
            {
                if (!NavMeshDebugRenderer.IsCampaignMap)
                    return Mission.Current?.Scene != null
                        ? "parties are on the campaign map — in a mission use 'nav_agent'"
                        : "not in a campaign";

                if (args.Count == 1 && string.Equals(args[0], "clear", StringComparison.OrdinalIgnoreCase))
                {
                    NavMeshDebugState.WatchedParty = null;
                    DebugLogger.Log("[NavDebug] party 监视已取消");
                    return "nav_party cleared";
                }

                if (args.Count == 1 && string.Equals(args[0], "list", StringComparison.OrdinalIgnoreCase))
                {
                    MobileParty mainParty = MobileParty.MainParty;
                    var candidates = new List<MobileParty>();
                    var lines = new List<string>();
                    if (mainParty != null)
                    {
                        candidates.Add(mainParty);
                        lines.Add($"{candidates.Count - 1}={mainParty.Name}(main)");
                    }
                    foreach (MobileParty party in MobileParty.All)
                    {
                        if (party == mainParty || !party.IsActive)
                            continue;
                        float dist = mainParty != null ? party.Position2D.Distance(mainParty.Position2D) : 0f;
                        if (mainParty != null && dist > 60f)
                            continue;
                        candidates.Add(party);
                        lines.Add($"{candidates.Count - 1}={party.Name} {dist:F0}m");
                        if (candidates.Count >= 12)
                            break;
                    }
                    NavMeshDebugState.LastPartyCandidates = candidates;
                    DebugLogger.Log($"[NavDebug] nav_party list（主队 + 60m 内，最多 12）: {(lines.Count > 0 ? string.Join(" | ", lines) : "无")}");
                    return lines.Count > 0
                        ? string.Join(" | ", lines)
                        : "no party found";
                }

                MobileParty target;
                if (args.Count == 0)
                {
                    target = MobileParty.MainParty;
                    if (target == null)
                        return "main party not found";
                }
                else if (args.Count == 1 && string.Equals(args[0], "nearest", StringComparison.OrdinalIgnoreCase))
                {
                    MobileParty mainParty = MobileParty.MainParty;
                    if (mainParty == null)
                        return "main party not found";
                    target = null;
                    float bestSq = float.MaxValue;
                    foreach (MobileParty party in MobileParty.All)
                    {
                        if (party == mainParty || !party.IsActive)
                            continue;
                        float distSq = party.Position2D.DistanceSquared(mainParty.Position2D);
                        if (distSq < bestSq)
                        {
                            bestSq = distSq;
                            target = party;
                        }
                    }
                    if (target == null)
                        return "no active party found";
                }
                else if (args.Count == 1 && int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seq))
                {
                    List<MobileParty> candidates = NavMeshDebugState.LastPartyCandidates;
                    if (candidates == null || seq < 0 || seq >= candidates.Count)
                        return "seq out of range — run 'nav_party list' first";
                    target = candidates[seq];
                    if (target == null || !target.IsActive)
                        return "selected party is no longer active — run 'nav_party list' again";
                }
                else
                {
                    return "usage: nav_party [<seq>|nearest|list|clear]";
                }

                NavMeshDebugState.WatchedParty = target;
                DebugLogger.Log($"[NavDebug] 监视 party '{target.Name}'" +
                    "（绘制：白球=本体 青线=当前位置→TargetPosition 实时路径 品红箭头/球=目标；nav_party clear 取消）");
                return $"watching party '{target.Name}' — magenta arrow = target direction, cyan line = live path";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[NavDebug] nav_party 异常: {ex}");
                return "nav_party_failed: " + ex.Message;
            }
        }

        private static bool TryParseVec2(string xs, string ys, out Vec2 result)
        {
            result = Vec2.Zero;
            if (float.TryParse(xs, NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(ys, NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                result = new Vec2(x, y);
                return true;
            }
            return false;
        }
    }
}
