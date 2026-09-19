// ═══════════════════════════════════════════════════════════════════════════
// 飞天实验合集（2026-09-18/19）
//
// 🔴 这是**实验脚手架**，不是玩法功能。所有尝试的结论已归档到
//    Knowledge/骑砍2Agent运动与位置机制.md §6「离地尝试全记录」（9+ 条路全死）。
//    留在这里是为了将来复验某条路时能直接拿来用——**不要再往这里加新玩法代码**。
//
// 内容（全部在本文件里，按机制分块、分隔线标出）：
//   · 控制台指令：custom.fly / custom.flytest（最早期的写高度尝试）
//   · custom.plate：动态导航件升降板
//   · FlySpikeExperimentCommands：飞行尝试专用指令 —— vlift（抬外观帧）/
//     lift（抬坐点骨·坐骑根骨）/ airhold·airjump（空中态）/ mount_scale（坐骑缩放）/
//     chair_to_me·chairlift·usable_near（物件载人）
//   · 每帧驱动：FlySpikeMissionView（任务 tick）、FlySpikeFrameTickPatch（MissionScreen.OnFrameTick 晚钩子）
//
// ⚠️ 通用调试指令（set_height / move / set_controller / print_pos_dir）留在 Debug/MyCommands.cs ——
//    它们不属于飞行（任何调试都可能用），那里是 custom.* 指令的既有归口。
//
// 挂载：MySubModule.OnMissionBehaviorInitialize（置于玩法闸门之前）
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using SandBox.Objects.Usables;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;

namespace LivingWorldNpcs.CampaignMode
{

    // ─────────────────── 原 FlySpikeCommands.cs ───────────────────
    /// <summary>
    /// 飞天 spike · 状态与命令（2026-09-18）。
    ///
    /// 目的：验证「玩家 agent 能不能被我们按帧抬起来」这一件事，回答三个未知数——
    ///   ① 每帧写 Z 能不能稳定悬停，还是被 native 拽回地面 / 肉眼可见抖动；
    ///   ② 写位置的时刻与 native 物理时刻谁先谁后（表现为 targetZ 与 actualZ 的稳定偏差量）；
    ///   ③ 引擎是否认为 agent 在空中（IsOnLand）。
    /// 结论只用于定设计，不代表最终玩法——正式设计见后续 plan。
    ///
    /// 机制背景（1.2.12 反编译实证）：
    ///   · 引擎**没有** agent 级重力开关——全客户端 DLL 搜 SetGravity / GravityMultiplier /
    ///     ZeroGravity / NoGravity / GravityScale / UseGravity 全部 0 命中；唯一带 gravity 的
    ///     GameEntityExtensions.DisableGravity 是给场景实体（道具）用的，管不到 agent 运动。
    ///   · WorldPosition 结构只认 GetNavMeshVec3 / GetGroundVec3 / ZValidityState——引擎对
    ///     agent 的 Z 只有「地面 / 导航面高度」一个语义；连 Agent.SetTargetPosition 都只吃 Vec2。
    ///   · 所以唯一做法 = 每帧把位置写回去（Agent.TeleportToPosition → native SetPosition）。
    ///
    /// 本 spike 只覆盖 Z，X/Y 完全交给引擎（玩家照常走，碰撞/动画/输入全原生）。
    /// 🔴 起飞时必须**先夺走玩家控制权**（禁移动方向 + 放开 Controller）——引擎把位置判给玩家
    ///    控制器，不夺走写入就无效（实机两次验证：任务 tick 与 UI 晚钩子写入均被整个吞掉）。
    /// 🔴 夺走方式默认 `Controller = None`（引擎 AI 完全退场）；`custom.fly ai` 可切到
    ///    `AI + SetMaximumSpeedLimit(0)`。**别用裸 AI**——2026-09-18 实机：Controller=AI 后
    ///    `Agent.Tick` 的 `if (AllowAiTicking && IsAIControlled) TickAsAI(dt)` 会把它当 AI 单位驱动，
    ///    agent 自己跑去了 z=15.06 的别处（用户描述「直接把我弄到地图边缘」）。
    /// 落地/结束时自动交还。
    ///
    /// 用法（游戏内 `~` 控制台，返回文本纯英文；测量详情走 DebugLogger 中文）：
    ///   custom.fly             # 开始上升（期间键盘失效，落地自动还你）；再输一次 = 匀速降回地面
    ///   custom.fly on|off      # 显式上升 / 下降
    ///   custom.fly 5           # 设置上升速度 5 m/s 并开始上升
    ///   custom.fly none|ai     # 切换控制权接管方式（默认 none）
    ///   custom.fly drop        # 🔴 立即断开写入并交还控制权（自由落体，测重力）
    /// 测量行：`[FlySpike] t=…s targetZ=… actualZ=… delta=… onLand=… locked=…`（每秒一行）
    ///   delta = actualZ − targetZ：稳定的小负数 = 我们在 native 物理之后写（安全）；
    ///   持续扩大的负数 = 重力在赢，需要闭环修正。
    ///
    /// 驱动循环在 <see cref="FlySpikeMissionView"/>。
    /// </summary>
    /// <summary>探针模式（诊断「写入为什么被吞」用，与常规飞行互斥）。</summary>
    public enum FlyProbe
    {
        None,

        /// <summary>每帧写「锚点 +2m 水平」，看 X/Y 写入能不能被接受——分开「整个 SetPosition 无效」与「只有 Z 被锁」。</summary>
        XY,

        /// <summary>头两帧写一次 z+20 然后停手，观察 2 秒——分开「每帧被硬拉回地面」与「自由落体」。</summary>
        Z,

        /// <summary>同 Z，但改走 Agent.SetInitialFrame(..., canSpawnOutsideOfMissionBoundary:true)。</summary>
        Init,

        /// <summary>打 AgentFlag.CanJump + 触发 EventControlFlag.Jump，观察人形能不能离地（测「人类跳被关 = 引擎不许离地」假说）。</summary>
        Jump,

        /// <summary>
        /// 🔴 先按 StoryEngine 的成对套路夺走玩家控制权（禁用主角移动 + Controller→AI），再写位置——
        /// 测「引擎是不是因为玩家控制器占着位置才拒绝写入」。跑完自动交还控制权。
        /// </summary>
        ScriptCtrl,

        /// <summary>
        /// 🔴 单项隔离（AI）：**只把 Controller 翻成 AI，一次**——不写位置、不动移动方向。
        /// 看 agent 会不会自己跑掉。用来证伪/证实「是改 AI 本身有问题」。
        /// </summary>
        AiOnly,

        /// <summary>
        /// 🔴 单项隔离（None）：只把 Controller 翻成 None，一次——不写位置、不动移动方向。
        /// 与 AiOnly 对照，切分到底是哪种控制权状态触发引擎搬迁。
        /// </summary>
        NoneOnly
    }

    /// <summary>接管玩家控制权的方式（custom.fly none|ai 切换）。</summary>
    public enum FlyControlMode
    {
        /// <summary>`Controller = None`：引擎 AI 完全退场，没人驱动位置——外部按帧写位置才成立（默认）。</summary>
        Free,

        /// <summary>`Controller = AI` + `SetMaximumSpeedLimit(0)`：保留 AI 但不给它速度，测「AI 是不是元凶」。</summary>
        AiPinned
    }

    public static class FlySpikeState
    {
        /// <summary>上升中（每帧把目标高度往上加）。</summary>
        public static bool Rising;

        /// <summary>下降中（每帧把目标高度往下减，到出发点地面高度自动结束）。</summary>
        public static bool Descending;

        /// <summary>升降速度（米/秒）。</summary>
        public static float RiseSpeed = 3f;

        /// <summary>起飞时的地面高度（下降目标；只覆盖 Z 时水平随便走，跨地形会失准，spike 够用）。</summary>
        public static float GroundZ;

        /// <summary>我们要求的高度（我们自己维护，绝对赋值，不用增量以免累积误差）。</summary>
        public static float TargetZ;

        /// <summary>被驱动的 agent（当前恒为玩家；换 Mission 后失效由 SafeIsActive 兜底）。</summary>
        public static Agent Target;

        /// <summary>上升累计时长（秒，仅用于日志时间轴）。</summary>
        public static float Elapsed;

        /// <summary>日志节流累加器。</summary>
        public static float LogTimer;

        // ── 探针（custom.flytest）──

        public static FlyProbe Probe = FlyProbe.None;
        public static float ProbeTime;
        public static float ProbeDuration = 1.5f;
        public static int ProbeFrames;

        /// <summary>自动连跑模式（custom.flytest 无参）：xy → z → jump 依次跑完。</summary>
        public static bool ProbeSequence;
        public static int ProbeStage;

        /// <summary>探针锚点（= 启动探针时玩家的位置）。</summary>
        public static Vec3 ProbeAnchor;

        /// <summary>🔴 玩家控制权是否正被我们夺走（NoneCtrl 探针用；任何离开该探针的路径都必须交还）。</summary>
        public static bool ControllerTaken;

        /// <summary>接管方式（默认 None = 引擎完全退场）。</summary>
        public static FlyControlMode ControlMode = FlyControlMode.Free;

        public static void Reset()
        {
            Rising = false;
            Descending = false;
            Target = null;
            TargetZ = 0f;
            GroundZ = 0f;
            Elapsed = 0f;
            LogTimer = 0f;
            Probe = FlyProbe.None;
            ProbeTime = 0f;
            ProbeFrames = 0;
            ProbeSequence = false;
            ProbeStage = 0;
            // 注意：ControllerTaken 不在这里清——交还控制权必须拿到 Agent 引用，由视图/命令负责
        }
    }

    public class FlySpikeCommands
    {
        /* 🔴 控制台命令注册纪律（同 NavMeshProbeCommands / PerfCommands）：
           委托签名 = public static string F(List<string>)，签名/可见性不符 = 启动时绑定失败 ArgumentException。
           返回文本纯英文（铁律），中文只进 DebugLogger。
           首参可弃：认不出的一律当占位符，回落到默认动作并在返回里注明。 */

        [CommandLineFunctionality.CommandLineArgumentFunction("fly", "custom")]
        public static string Fly(List<string> args)
        {
            try
            {
                if (Mission.Current == null || Agent.Main == null)
                    return "[Fly] no active mission / player agent - enter a scene first";

                string raw = args.Count > 0 ? args[0].Trim() : "";
                string key = raw.ToLowerInvariant();

                switch (key)
                {
                    case "on":
                        return StartRising(null);

                    case "off":
                        return StartDescending();

                    case "none":
                        FlySpikeState.ControlMode = FlyControlMode.Free;
                        return "[Fly] control mode -> None (engine AI fully out; default)";

                    case "ai":
                        FlySpikeState.ControlMode = FlyControlMode.AiPinned;
                        return "[Fly] control mode -> AI pinned (maxSpeed 0) - next takeoff uses it";

                    case "drop":
                        // 断开写入 = 让引擎自己接管高度：这是「重力会不会把人拉下去」的对照组
                        if (!FlySpikeState.Rising && !FlySpikeState.Descending)
                            return "[Fly] not flying - nothing to drop";
                        FlySpikeState.Reset();
                        DebugLogger.Log("[FlySpike] tether cut (drop) -> free fall, watching gravity");
                        return "[Fly] tether cut - free fall (gravity owns you again)";

                    case "":
                        if (FlySpikeState.Rising) return StartDescending();
                        if (FlySpikeState.Descending) return "[Fly] already descending";
                        return StartRising(null);
                }

                if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float speed)
                    && speed > 0.01f && speed < 200f)
                {
                    FlySpikeState.RiseSpeed = speed;
                    return StartRising($"speed={speed:F1}m/s");
                }

                // 首参可弃：认不出的当占位符处理，回落默认切换动作
                return StartRising(null) + $" [note: '{raw}' is not a known keyword -> treated as toggle]";
            }
            catch (Exception ex)
            {
                FlySpikeState.Reset();
                DebugLogger.Log($"[FlySpike] command failed: {ex}");
                return $"[Fly] command failed: {ex.Message}";
            }
        }

        /// <summary>
        /// 诊断探针：分开「写入为什么被吞」的几种可能。每帧打一行 [FlyProbe]（含实测坐标）。
        /// 🔴 无参 = 自动连跑 xy → z → jump 三段（各 1.5 秒），只需输一次就能拿齐全部答案。
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("flytest", "custom")]
        public static string FlyTest(List<string> args)
        {
            try
            {
                if (Mission.Current == null || Agent.Main == null)
                    return "[FlyTest] no active mission / player agent - enter a scene first";

                string key = args.Count > 0 ? args[0].Trim().ToLowerInvariant() : "auto";

                if (key == "stop")
                {
                    FlySpikeState.Probe = FlyProbe.None;
                    FlySpikeState.ProbeSequence = false;
                    RestoreController(Agent.Main);
                    return "[FlyTest] stopped";
                }

                if (key == "restore")
                {
                    RestoreController(Agent.Main);
                    return "[FlyTest] controller restored to player";
                }

                FlyProbe probe;
                switch (key)
                {
                    case "auto": probe = FlyProbe.XY; break;
                    case "xy": probe = FlyProbe.XY; break;
                    case "z": probe = FlyProbe.Z; break;
                    case "init": probe = FlyProbe.Init; break;
                    case "jump": probe = FlyProbe.Jump; break;
                    case "script":
                    case "none": probe = FlyProbe.ScriptCtrl; break;
                    case "ai": probe = FlyProbe.AiOnly; break;
                    case "ctrl": probe = FlyProbe.AiOnly; break;
                    case "noneonly": probe = FlyProbe.NoneOnly; break;
                    default:
                        return $"[FlyTest] unknown mode '{args[0]}' - use (no arg) | xy | z | init | jump | script | ai | noneonly | stop | restore";
                }

                StartProbe(probe, sequence: key == "auto");
                return key == "auto"
                    ? "[FlyTest] auto sequence xy -> z -> jump -> ai -> noneonly running (~10s) - read [FlyProbe] lines"
                    : $"[FlyTest] {key} running {FlySpikeState.ProbeDuration:F1}s";
            }
            catch (Exception ex)
            {
                FlySpikeState.Probe = FlyProbe.None;
                FlySpikeState.ProbeSequence = false;
                DebugLogger.Log($"[FlyProbe] command failed: {ex}");
                return $"[FlyTest] command failed: {ex.Message}";
            }
        }

        /// <summary>自动连跑的次序。两个 ctrl 单项隔离放最后——它们会短暂夺走玩家控制权。</summary>
        internal static readonly FlyProbe[] ProbeOrder = { FlyProbe.XY, FlyProbe.Z, FlyProbe.Jump, FlyProbe.AiOnly, FlyProbe.NoneOnly };

        /// <summary>
        /// 🔴 单项隔离：**只翻控制权一次**，不写位置、不动移动方向。
        /// 与 <see cref="TakeController"/>（起飞用，含 SetMovementDirection 归零）刻意分开——
        /// 变量越少越能定性。
        /// </summary>
        internal static void FlipControllerOnly(Agent player, bool useAi)
        {
            if (FlySpikeState.ControllerTaken || !AgentControlHelper.SafeIsActive(player))
                return;

            if (useAi)
            {
                V.SetPlayerControlFrozen(player, true);
                DebugLogger.Log("[FlyProbe] controller -> AI ONLY (no direction zero, no position write)");
            }
            else
            {
                V.SetAgentControllerNone(player);
                DebugLogger.Log("[FlyProbe] controller -> None ONLY (no direction zero, no position write)");
            }

            FlySpikeState.ControllerTaken = true;
        }

        /// <summary>
        /// 夺走玩家控制权（ScriptCtrl 探针第一步）。
        /// 🔴 照 StoryEngine.Play 的成对套路：先禁掉主角移动方向，再 Controller→AI。
        /// 🔴 一律走 V.* 版本兼容封装——`Agent.ControllerType` 是 1.2.12 的嵌套枚举，
        ///    1.3+ 改名为顶级 `AgentControllerType`，业务层裸写会在高版本编不过。
        /// </summary>
        internal static void TakeController(Agent player)
        {
            if (FlySpikeState.ControllerTaken || !AgentControlHelper.SafeIsActive(player))
                return;
            // 先禁掉移动方向（StoryEngine 同款首步），再放开控制
            player.SetMovementDirection(Vec2.Zero);

            if (FlySpikeState.ControlMode == FlyControlMode.AiPinned)
            {
                V.SetPlayerControlFrozen(player, true);   // -> AI
                player.SetMaximumSpeedLimit(0f, false);   // 钉住：AI 想走也走不动
                DebugLogger.Log("[FlyProbe] controller -> AI + maxSpeed 0 (pinned)");
            }
            else
            {
                V.SetAgentControllerNone(player);         // 引擎 AI 完全退场
                DebugLogger.Log("[FlyProbe] controller -> None (engine AI fully out)");
            }

            FlySpikeState.ControllerTaken = true;
        }

        /// <summary>交还玩家控制权。任何离开 ScriptCtrl 探针/飞行会话的路径都要调它。</summary>
        internal static void RestoreController(Agent player)
        {
            if (!FlySpikeState.ControllerTaken)
                return;
            if (AgentControlHelper.SafeIsActive(player))
            {
                V.SetPlayerControlFrozen(player, false);
                player.SetMaximumSpeedLimit(-1f, false);  // -1 = 恢复默认限速
                DebugLogger.Log("[FlyProbe] controller -> Player (input returned)");
            }
            FlySpikeState.ControllerTaken = false;
        }

        /// <summary>启动一段探针（自动连跑时由 FlySpikeMissionView 逐段调用）。</summary>
        internal static void StartProbe(FlyProbe probe, bool sequence)
        {
            // 常规飞行与探针互斥：先停掉前者，免得两组写入混在一起看不清
            FlySpikeState.Rising = false;
            FlySpikeState.Descending = false;
            FlySpikeState.Probe = probe;
            FlySpikeState.ProbeSequence = sequence;
            FlySpikeState.ProbeTime = 0f;
            FlySpikeState.ProbeFrames = 0;
            FlySpikeState.ProbeAnchor = Agent.Main.Position;

            DebugLogger.Log(
                $"[FlyProbe] {probe} start anchor=({FlySpikeState.ProbeAnchor.x:F2},{FlySpikeState.ProbeAnchor.y:F2},{FlySpikeState.ProbeAnchor.z:F2}) " +
                $"duration={FlySpikeState.ProbeDuration:F1}s seq={sequence}");
        }

        private static string StartRising(string note)
        {
            if (FlySpikeState.Rising)
                return $"[Fly] already rising at {FlySpikeState.RiseSpeed:F1}m/s (z={Agent.Main.Position.z:F2})";

            float z = Agent.Main.Position.z;
            FlySpikeState.Rising = true;
            FlySpikeState.Descending = false;
            FlySpikeState.GroundZ = z;
            FlySpikeState.TargetZ = z;
            FlySpikeState.Elapsed = 0f;
            FlySpikeState.LogTimer = 1f; // 首帧即打一行，便于确认已接管
            FlySpikeState.Target = Agent.Main;
            FlySpikeFrameTickPatch.ResetSession(); // 晚钩子补丁复位（清 1s 日志节流与异常熔断）

            DebugLogger.Log($"[FlySpike] start rising: groundZ={z:F2} speed={FlySpikeState.RiseSpeed:F1}m/s");
            return $"[Fly] rising at {FlySpikeState.RiseSpeed:F1}m/s from z={z:F2}"
                 + (string.IsNullOrEmpty(note) ? "" : $" ({note})")
                 + " - type custom.fly again to descend";
        }

        private static string StartDescending()
        {
            if (!FlySpikeState.Rising && !FlySpikeState.Descending)
                return "[Fly] not flying";
            if (FlySpikeState.Descending)
                return "[Fly] already descending";

            FlySpikeState.Rising = false;
            FlySpikeState.Descending = true;
            DebugLogger.Log($"[FlySpike] descending to groundZ={FlySpikeState.GroundZ:F2}");
            return $"[Fly] descending to z={FlySpikeState.GroundZ:F2} then releasing";
        }
    }

    // ─────────────────── 原 FlySpikeMissionView.cs ───────────────────
    /// <summary>
    /// 飞天 spike · 驱动循环 · Mission（场景）侧（2026-09-18）。
    /// 状态与命令见 <see cref="FlySpikeCommands"/>。
    ///
    /// 挂载：MySubModule.OnMissionBehaviorInitialize，置于玩法闸门（IsInteractionDisabled）之前
    /// —— 与 NavMeshDebugMissionView / FirearmFxLogic 同理：飞天要在战场/攻城/城镇全场景可测，
    /// 被「战场不跑玩法逻辑」的闸门拦掉就白做了。本视图纯按帧写位置、不碰战役 API，
    /// 未开命令时每帧只做一次 bool 判断，零开销。
    ///
    /// 每帧只做一件事：把玩家 agent 的 Z 写成我们维护的目标高度（X/Y 不动，交给引擎）。
    /// 🔴 顺序纪律：先读 Position 再写 TeleportToPosition——读的是 native 本帧算完的位置，
    ///    写的是下一帧的起点，这样日志里的 delta 才是「native 物理插进来多少」的真实读数。
    /// </summary>
    public class FlySpikeMissionView : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        public override void OnMissionTick(float dt)
        {
            // 🔴 控制权交还必须放在总闸**之前**：飞行/探针结束后状态归零就不再往下走，
            //    若放在闸门后面就再也不 tick，控制权永远拿不回来（玩家会变成不能动的木桩）。
            // 🔴 两种"要控制权"必须分开：
            //    wantsFlightControl = 飞行/脚本探针 → 走 TakeController（含 SetMovementDirection 归零）
            //    wantsAnyControl    = 上面 + 两个单项隔离探针 —— 隔离探针**自己**翻控制权（FlipControllerOnly，
            //                         不动移动方向、不写位置），所以这里绝不能替它调 TakeController，
            //                         否则隔离就不成立了（变量混在一起）。
            bool wantsFlightControl = FlySpikeState.Rising || FlySpikeState.Descending
                                      || FlySpikeState.Probe == FlyProbe.ScriptCtrl;
            bool wantsAnyControl = wantsFlightControl
                                   || FlySpikeState.Probe == FlyProbe.AiOnly
                                   || FlySpikeState.Probe == FlyProbe.NoneOnly;
            if (FlySpikeState.ControllerTaken && !wantsAnyControl)
                FlySpikeCommands.RestoreController(Agent.Main);

            if (!FlySpikeState.Rising && !FlySpikeState.Descending && FlySpikeState.Probe == FlyProbe.None)
                return;

            try
            {
                Agent player = Agent.Main;
                if (!AgentControlHelper.SafeIsActive(player))
                    return; // Mission 卸载中：留给 OnRemoveBehavior 收尾

                // 🔴 飞行要先夺走玩家控制权——引擎把位置判给玩家控制器，夺走才写得动。
                if (wantsFlightControl)
                    FlySpikeCommands.TakeController(player);

                if (FlySpikeState.Probe != FlyProbe.None)
                {
                    RunProbe(player, dt);
                    return;
                }

                if (FlySpikeState.Target != player)
                {
                    FlySpikeState.Target = player;
                    FlySpikeState.TargetZ = player.Position.z;
                    DebugLogger.Log($"[FlySpike] attach -> player agent idx={player.Index} z={player.Position.z:F2}");
                }

                bool landed = false;
                if (FlySpikeState.Rising)
                {
                    FlySpikeState.Elapsed += dt;
                    FlySpikeState.TargetZ += FlySpikeState.RiseSpeed * dt;
                }
                else
                {
                    FlySpikeState.TargetZ -= FlySpikeState.RiseSpeed * dt;
                    if (FlySpikeState.TargetZ <= FlySpikeState.GroundZ)
                    {
                        FlySpikeState.TargetZ = FlySpikeState.GroundZ;
                        landed = true;
                    }
                }

                Vec3 pos = player.Position;
                player.TeleportToPosition(new Vec3(pos.x, pos.y, FlySpikeState.TargetZ));

                if (landed)
                {
                    DebugLogger.Log($"[FlySpike] landed & released at z={FlySpikeState.GroundZ:F2} after {FlySpikeState.Elapsed:F1}s");
                    FlySpikeState.Reset();
                    return;
                }

                FlySpikeState.LogTimer += dt;
                if (FlySpikeState.LogTimer >= 1f)
                {
                    FlySpikeState.LogTimer = 0f;
                    Vec3 now = player.Position;
                    DebugLogger.Log(
                        $"[FlySpike] t={FlySpikeState.Elapsed:F1}s targetZ={FlySpikeState.TargetZ:F2} " +
                        $"pos=({now.x:F1},{now.y:F1},{now.z:F2}) " +
                        $"delta={now.z - FlySpikeState.TargetZ:+0.000;-0.000;0.000} " +
                        $"ctrl={(FlySpikeState.ControlMode == FlyControlMode.AiPinned ? "AIpin" : "None")} " +
                        $"onLand={player.IsOnLand()} locked={player.MovementLockedState}");
                }
            }
            catch (Exception ex)
            {
                // 一次异常即全部关闭（防每帧刷屏 + 防持续异常拖垮游戏）——同 NavMeshDebugMissionView
                FlySpikeState.Reset();
                DebugLogger.Log($"[FlySpike] tick exception, flight disabled: {ex}");
            }
        }

        /// <summary>
        /// 诊断探针：每帧打一行实测坐标，2 秒后自动结束。
        /// 🔴 除 XY 探针外，写入只发生在头两帧，之后纯观察——这样才分得清
        ///    「每帧被硬拉回地面」与「自由落体（重力）」。
        /// </summary>
        private static void RunProbe(Agent player, float dt)
        {
            FlySpikeState.ProbeTime += dt;
            FlySpikeState.ProbeFrames++;

            Vec3 now = player.Position;
            Vec3 anchor = FlySpikeState.ProbeAnchor;
            string note = "";

            switch (FlySpikeState.Probe)
            {
                case FlyProbe.XY:
                    // 每帧都写：固定点 = 锚点水平 +2m。看引擎接受多少（X/Y 动了 = SetPosition 本身有效）
                    player.TeleportToPosition(new Vec3(anchor.x + 2f, anchor.y, anchor.z));
                    break;

                case FlyProbe.Z:
                    if (FlySpikeState.ProbeFrames <= 2)
                    {
                        player.TeleportToPosition(new Vec3(now.x, now.y, anchor.z + 20f));
                        note = " [wrote z+20]";
                    }
                    break;

                case FlyProbe.Init:
                    if (FlySpikeState.ProbeFrames <= 2)
                    {
                        player.SetInitialFrame(new Vec3(now.x, now.y, anchor.z + 20f),
                            player.LookDirection.AsVec2, canSpawnOutsideOfMissionBoundary: true);
                        note = " [SetInitialFrame z+20]";
                    }
                    break;

                case FlyProbe.Jump:
                    if (FlySpikeState.ProbeFrames <= 2)
                    {
                        player.SetAgentFlags(player.GetAgentFlags() | AgentFlag.CanJump);
                        player.EventControlFlags |= Agent.EventControlFlag.Jump;
                        note = " [CanJump+Jump]";
                    }
                    break;

                case FlyProbe.ScriptCtrl:
                    if (FlySpikeState.ProbeFrames <= 2)
                    {
                        FlySpikeCommands.TakeController(player);
                        note = " [controller->AI]";
                    }
                    // 每帧写 z+3m：抬得够明显，落下来也不至于摔死
                    player.TeleportToPosition(new Vec3(now.x, now.y, anchor.z + 3f));
                    break;

                case FlyProbe.AiOnly:
                    // 🔴 单项隔离：只翻一次 Controller→AI，**不写位置、不动移动方向**
                    if (FlySpikeState.ProbeFrames <= 2)
                    {
                        FlySpikeCommands.FlipControllerOnly(player, useAi: true);
                        note = " [AI ONLY, no position write]";
                    }
                    break;

                case FlyProbe.NoneOnly:
                    // 🔴 单项隔离：只翻一次 Controller→None
                    if (FlySpikeState.ProbeFrames <= 2)
                    {
                        FlySpikeCommands.FlipControllerOnly(player, useAi: false);
                        note = " [None ONLY, no position write]";
                    }
                    break;
            }

            Vec3 after = player.Position;
            DebugLogger.Log(
                $"[FlyProbe] {FlySpikeState.Probe} t={FlySpikeState.ProbeTime:F2}s idx={player.Index} " +
                $"pos=({after.x:F1},{after.y:F1},{after.z:F2}) " +
                $"dxy=({after.x - anchor.x:+0.00;-0.00;0.00},{after.y - anchor.y:+0.00;-0.00;0.00}) " +
                $"dz={after.z - anchor.z:+0.00;-0.00;0.00} onLand={player.IsOnLand()} " +
                $"locked={player.MovementLockedState} canJump={(player.GetAgentFlags() & AgentFlag.CanJump) != 0}{note}");

            if (FlySpikeState.ProbeTime < FlySpikeState.ProbeDuration)
                return;

            DebugLogger.Log(
                $"[FlyProbe] {FlySpikeState.Probe} done: dxy=({after.x - anchor.x:+0.00;-0.00;0.00}," +
                $"{after.y - anchor.y:+0.00;-0.00;0.00}) dz={after.z - anchor.z:+0.00;-0.00;0.00}");

            // 自动连跑：切下一段（重新取锚点）
            if (FlySpikeState.ProbeSequence)
            {
                FlySpikeState.ProbeStage++;
                if (FlySpikeState.ProbeStage < FlySpikeCommands.ProbeOrder.Length)
                {
                    FlySpikeCommands.StartProbe(FlySpikeCommands.ProbeOrder[FlySpikeState.ProbeStage], sequence: true);
                    return;
                }
                DebugLogger.Log("[FlyProbe] auto sequence finished");
            }

            FlySpikeState.Probe = FlyProbe.None;
            FlySpikeState.ProbeSequence = false;
        }

        public override void OnRemoveBehavior()
        {
            FlySpikeCommands.RestoreController(Agent.Main);
            FlySpikeState.Reset();
            base.OnRemoveBehavior();
        }
    }

    // ─────────────────── 原 FlySpikeFrameTickPatch.cs ───────────────────
    /// <summary>
    /// 飞天 spike · 晚钩子写入（2026-09-18）。
    ///
    /// 为什么需要它（Mission.OnTick 反编译实证）：
    ///   1. OnPreDisplayMissionTick（含玩家输入 → MovementFlags）
    ///   2. 相机
    ///   3. OnMissionTick          ← FlySpikeMissionView 在这里写
    ///   4. TickAgentsAndTeams*    ← foreach (AllAgents) agent.Tick(dt)，**在我们之后**
    /// 实机日志证明第 3 步的写入被整个吞掉（actualZ 纹丝不动、delta 严格线性扩大）。
    /// 本补丁把同一份写入挪到 MissionScreen.OnFrameTick（UI 层回调，比任务 tick 更晚，
    /// 仓库已有先例 PerfMissionFrameTickPatch），看它是否活得下来。
    ///
    /// 本条日志（[FlySpike-UI]）与 [FlySpike]（任务 tick 侧）对照读：
    ///   · UI 侧 postWriteZ ≈ targetZ  → 时序问题确诊，写入位置改到这里即可
    ///   · 两侧都是地面高度            → 与时机无关，是引擎不接受 agent 的 Z（另找路子）
    ///
    /// 🔴 与 PerfMissionFrameTickPatch / ImMissionButtonRefreshPatch 同目标多 postfix 共存
    ///    （Bannerlord.Harmony 允许多 postfix，已实机验证）。
    /// 🔴 只在 Rising/Descending 时干活；异常一次即停（防每帧刷屏）。
    /// </summary>
    [HarmonyPatch(typeof(MissionScreen), "OnFrameTick")]
    public static class FlySpikeFrameTickPatch
    {
        private static float _logTimer;
        private static bool _broken;

        [HarmonyPostfix]
        public static void Postfix(float dt)
        {
            // 🔴 空中态接管（custom.airhold）：只在 onLand=False 时写位置。
            FlySpikeExperimentCommands.TickAirHold(dt);

            // 骑手坐点骨 / 坐骑根骨抬升（custom.lift）：必须在动画更新之后每帧重写骨骼。
            FlySpikeExperimentCommands.TickAgentLift();

            // 「凳子」实验（custom.chairlift）：抬玩家正在使用的可交互物件。
            FlySpikeExperimentCommands.TickChairLift();

            // 🔴 外观帧抬升（custom.vlift）：把 agent 的**外观**（非逻辑位置）抬起来。
            FlySpikeExperimentCommands.TickVisualLift();

            if (_broken || (!FlySpikeState.Rising && !FlySpikeState.Descending))
                return;

            try
            {
                Agent player = Agent.Main;
                if (!AgentControlHelper.SafeIsActive(player))
                    return;

                Vec3 pos = player.Position;
                player.TeleportToPosition(new Vec3(pos.x, pos.y, FlySpikeState.TargetZ));

                _logTimer += dt;
                if (_logTimer >= 1f)
                {
                    _logTimer = 0f;
                    Vec3 after = player.Position;
                    DebugLogger.Log(
                        $"[FlySpike-UI] postWriteZ={after.z:F2} targetZ={FlySpikeState.TargetZ:F2} " +
                        $"delta={after.z - FlySpikeState.TargetZ:+0.000;-0.000;0.000} onLand={player.IsOnLand()}");
                }
            }
            catch (Exception ex)
            {
                _broken = true;
                DebugLogger.Log($"[FlySpike-UI] patch disabled after exception: {ex}");
            }
        }

        /// <summary>飞行会话结束时复位（由 FlySpikeMissionView 调用）。</summary>
        internal static void ResetSession()
        {
            _logTimer = 0f;
            _broken = false;
        }
    }

    // ─────────────────── 原 PlateSpike.cs ───────────────────
    /// <summary>
    /// 导航件升降板验证（2026-09-18）。
    ///
    /// 背景（[骑砍2Agent运动与位置机制.md] §7）：实测确认 agent 的 **X/Y 可写、Z 写不进去（自动贴地）**，
    /// 所以唯一能让 agent 离地的路子 = **给它脚下一块会动的 navmesh 面**（攻城塔带兵上城用的就是这套
    /// 「动态导航件」）。但四个未知数没有先例可查，本文件就是去证伪/证实它们：
    ///   ① 面**垂直移动**时 agent 跟不跟着上（攻城塔是水平移动 + 旋转，垂直带人无先例）
    ///   ② agent 怎么"上"到板面（引擎按 `GetCurrentNavigationFaceId` 记账）
    ///   ③ `AttachNavigationMeshFaces` 的 `autoLocalize` 该给什么（vanilla 范本给 false）
    ///   ④ 同一 (x,y) 上同时有场景地面与板面时引擎选哪个
    ///
    /// 判定口径（每帧一行 [Plate]）：
    ///   · `onPlate=True`（玩家当前导航面 id 落在我们挂的面组区间内）→ ②④ 通了：人确实站在板上
    ///   · `onPlate=True` 且板上升时 `player z` 跟着涨 → ① 通了：**电梯成立**
    ///   · `onPlate` 始终 False → 人没上板，得换"让人走上去"或别的策略
    ///
    /// 用法（控制台返回纯英文，详情进 DebugLogger）：
    ///   custom.plate                       # 状态
    ///   custom.plate spawn [navPrefab]     # 建板（默认导航件 ballista_a），板面在玩家脚底 +0.3m
    ///   custom.plate scan                  # 🔴 列出我们面组区间里**真正有面**的组（面序号/组 id/面心）
    ///   custom.plate connect on|off        # 重挂面：isConnected（默认 on = 人能走上去）
    ///   custom.plate localize on|off       # 重挂面：autoLocalize（默认 on）
    ///   custom.plate under                 # 板对齐到玩家正下方（脚底 +0.3m）
    ///   custom.plate z 20 / +1 / -1        # 板面绝对高度 / 增量升降
    ///   custom.plate up [speed] / stop     # 板持续上升（默认 1 m/s）——真正测"人被带着走"
    ///   custom.plate remove                # 拆板
    /// </summary>
    public static class PlateSpikeState
    {
        public static GameEntity Plate;
        public static int NavIdStart = -1;

        /// <summary>导入导航件前后的场景面数（用来判断 LoadNavMeshPrefab 到底成没成）。</summary>
        public static int FaceCountBefore = -1;
        public static int FaceCountAfter = -1;

        public static bool Localized = true;

        /// <summary>面是否接到主 navmesh（agent 能不能走上去的关键，默认 on）。</summary>
        public static bool Connected = true;

        public static bool Watch = true;

        /// <summary>板持续上升。</summary>
        public static bool Rising;
        public static float RiseSpeed = 1f;

        public static float LogTimer;

        /// <summary>面组区间：import 拿到 start，我们挂 start+0..start+9（空组无副作用）。</summary>
        public static bool PlayerOnPlate(Agent player)
        {
            if (NavIdStart < 0 || player == null) return false;
            int f = player.GetCurrentNavigationFaceId();
            return f >= NavIdStart && f <= NavIdStart + 9;
        }
    }

    public class PlateSpikeMissionView : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        public override void OnMissionTick(float dt)
        {
            if (!PlateSpikeState.Watch || PlateSpikeState.Plate == null)
                return;

            try
            {
                GameEntity plate = PlateSpikeState.Plate;
                if (plate.Pointer == UIntPtr.Zero)
                {
                    PlateSpikeState.Plate = null;
                    return;
                }

                if (PlateSpikeState.Rising)
                {
                    MatrixFrame f = plate.GetFrame();
                    f.origin = new Vec3(f.origin.x, f.origin.y, f.origin.z + PlateSpikeState.RiseSpeed * dt);
                    plate.SetFrame(ref f);
                }

                PlateSpikeState.LogTimer += dt;
                if (PlateSpikeState.LogTimer < 0.5f)
                    return;
                PlateSpikeState.LogTimer = 0f;

                Agent player = Agent.Main;
                Vec3 pp = player != null ? player.Position : Vec3.Zero;
                MatrixFrame pf = plate.GetFrame();
                int faceId = player != null ? player.GetCurrentNavigationFaceId() : -1;

                DebugLogger.Log(
                    $"[Plate] onPlate={PlateSpikeState.PlayerOnPlate(player)} face={faceId} " +
                    $"plate=({pf.origin.x:F1},{pf.origin.y:F1},{pf.origin.z:F2}) " +
                    $"player=({pp.x:F1},{pp.y:F1},{pp.z:F2}) dz={pp.z - pf.origin.z:+0.00;-0.00;0.00} " +
                    $"ourIds=[{PlateSpikeState.NavIdStart},{PlateSpikeState.NavIdStart + 9}] " +
                    $"onLand={(player != null && player.IsOnLand())}");
            }
            catch (Exception ex)
            {
                PlateSpikeState.Rising = false;
                DebugLogger.Log($"[Plate] tick exception, rise stopped: {ex}");
            }
        }

        public override void OnRemoveBehavior()
        {
            PlateSpikeState.Plate = null;
            PlateSpikeState.Rising = false;
            base.OnRemoveBehavior();
        }
    }

    public class PlateSpikeCommands
    {
        /* 控制台命令注册纪律：public static string F(List<string>)，返回文本纯英文，首参可弃。 */

        [CommandLineFunctionality.CommandLineArgumentFunction("plate", "custom")]
        public static string Plate(List<string> args)
        {
            try
            {
                Mission mission = Mission.Current;
                if (mission == null || mission.Scene == null)
                    return "[Plate] no active mission";

                string a = args.Count > 0 ? args[0].Trim().ToLowerInvariant() : "";
                string b = args.Count > 1 ? args[1].Trim() : "";

                switch (a)
                {
                    case "spawn":
                    case "": // 首参可弃：直接 `custom.plate` 当状态查询，`custom.plate spawn` 才建板
                        {
                            if (a == "")
                                return Status();

                            string navPrefab = string.IsNullOrEmpty(b) ? "ballista_a" : b;
                            return Spawn(mission, navPrefab, localized: true);
                        }

                    case "under":
                        return AlignUnderPlayer();

                    case "z":
                        {
                            if (!float.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                                return $"[Plate] 'z' needs a number, e.g. custom.plate z 20 (got '{b}')";
                            return MoveTo(null, null, z);
                        }

                    case "up":
                        {
                            float sp = 1f;
                            if (!string.IsNullOrEmpty(b))
                                float.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out sp);
                            PlateSpikeState.RiseSpeed = sp <= 0f ? 1f : sp;
                            PlateSpikeState.Rising = true;
                            return $"[Plate] rising at {PlateSpikeState.RiseSpeed:F2} m/s - watch [Plate] lines";
                        }

                    case "stop":
                        PlateSpikeState.Rising = false;
                        return "[Plate] rise stopped";

                    case "scan":
                        return ScanFaces(mission);

                    case "connect":
                        {
                            bool on = b != "off";
                            if (PlateSpikeState.Plate == null)
                                return "[Plate] no plate - spawn first";
                            PlateSpikeState.Connected = on;
                            Reattach(mission, PlateSpikeState.Localized);
                            return $"[Plate] faces re-attached with isConnected={on}"
                                 + (on ? " (agent may step onto it now)" : " (isolated island)");
                        }

                    case "localize":
                        {
                            bool on = b != "off";
                            if (PlateSpikeState.Plate == null)
                                return "[Plate] no plate - spawn first";
                            Reattach(mission, on);
                            return $"[Plate] faces re-attached with autoLocalize={on}";
                        }

                    case "remove":
                        {
                            if (PlateSpikeState.Plate != null)
                            {
                                PlateSpikeState.Plate.Remove(0);
                                PlateSpikeState.Plate = null;
                            }
                            PlateSpikeState.Rising = false;
                            PlateSpikeState.NavIdStart = -1;
                            return "[Plate] removed";
                        }
                }

                if (a.StartsWith("+") || a.StartsWith("-"))
                {
                    if (!float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out float d))
                        return $"[Plate] '{a}' is not a delta -> ignored. {Status()}";
                    return MoveTo(null, null, null, d);
                }

                return $"[Plate] unknown '{a}' - use spawn|under|z|+d|-d|up|stop|localize|remove. {Status()}";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Plate] command failed: {ex}");
                return $"[Plate] failed: {ex.Message}";
            }
        }

        private static string Status()
        {
            if (PlateSpikeState.Plate == null)
                return "[Plate] no plate. usage: custom.plate spawn [navPrefab]";
            MatrixFrame f = PlateSpikeState.Plate.GetFrame();
            Agent p = Agent.Main;
            Vec3 pp = p != null ? p.Position : Vec3.Zero;
            return $"[Plate] plate z={f.origin.z:F2} xy=({f.origin.x:F1},{f.origin.y:F1}) navIds=[{PlateSpikeState.NavIdStart},{PlateSpikeState.NavIdStart + 9}] " +
                   $"faces {PlateSpikeState.FaceCountBefore}->{PlateSpikeState.FaceCountAfter} localize={PlateSpikeState.Localized} rising={PlateSpikeState.Rising} " +
                   $"; player z={pp.z:F2} face={p?.GetCurrentNavigationFaceId() ?? -1} onPlate={PlateSpikeState.PlayerOnPlate(p)}";
        }

        private static string Spawn(Mission mission, string navPrefab, bool localized)
        {
            Agent player = Agent.Main;
            if (player == null)
                return "[Plate] no player agent";

            // 1. 建一个无网格实体（不依赖任何场景预制体）
            PlateSpikeState.FaceCountBefore = mission.Scene.GetNavMeshFaceCount();

            GameEntity e = GameEntity.CreateEmpty(mission.Scene, isModifiableFromEditor: false);
            if (e == null)
                return "[Plate] CreateEmpty failed";

            NavPrefabName = navPrefab;   // 先定名字，Reattach 要用

            Vec3 pp = player.Position;
            MatrixFrame frame = MatrixFrame.Identity;
            // 稍高于脚底：给玩家一个能**走上去**的低台阶（同高共面时引擎不会把人换到新面上）
            frame.origin = new Vec3(pp.x, pp.y, pp.z + StepUpHeight);
            e.SetFrame(ref frame);
            PlateSpikeState.Plate = e;

            // 2. 导入导航件 + 把面挂到实体上
            Reattach(mission, localized);

            PlateSpikeState.FaceCountAfter = mission.Scene.GetNavMeshFaceCount();

            DebugLogger.Log(
                $"[Plate] spawn navPrefab='{navPrefab}' at ({pp.x:F1},{pp.y:F1},{pp.z:F2}) " +
                $"navIdStart={PlateSpikeState.NavIdStart} faces {PlateSpikeState.FaceCountBefore}->{PlateSpikeState.FaceCountAfter} localize={localized}");

            return $"[Plate] spawned at player pos; navPrefab='{navPrefab}' navIdStart={PlateSpikeState.NavIdStart} " +
                   $"sceneFaces {PlateSpikeState.FaceCountBefore}->{PlateSpikeState.FaceCountAfter} (rise in face count = import worked). " +
                   $"Now: custom.plate up 1  and watch [Plate] onPlate=";
        }

        private static void Reattach(Mission mission, bool localized)
        {
            if (PlateSpikeState.Plate == null) return;

            if (PlateSpikeState.NavIdStart < 0)
                PlateSpikeState.NavIdStart = mission.GetNextDynamicNavMeshIdStart();

            // 面来自 ModuleData 同级 NavMeshPrefabs/*.bin（Native 里有 20+ 个现成范本）
            mission.Scene.ImportNavigationMeshPrefab(NavPrefabName, PlateSpikeState.NavIdStart);

            // 🔴 挂整整 10 个面组（start+0 .. start+9），而不是照抄 vanilla 的 +1..+4 四槽——
            //    实机 2026-09-18：导入 'ballista_a' 后场景面数只 +1，**我们并不知道那一个面落在哪个组**，
            //    照抄四槽很可能挂了个空组（表现：onPlate 恒 False）。全挂一遍最稳，
            //    空组挂上去无副作用（`custom.plate scan` 会列出到底哪些组真有面）。
            int s = PlateSpikeState.NavIdStart;
            for (int i = 0; i <= 9; i++)
                PlateSpikeState.Plate.AttachNavigationMeshFaces(s + i, isConnected: PlateSpikeState.Connected,
                    isBlocker: false, autoLocalize: localized);

            PlateSpikeState.Localized = localized;
        }

        /// <summary>导航件名（默认借 Native 现成的弩炮台面板 2.6×3.2m）。</summary>
        private static string NavPrefabName = "ballista_a";

        /// <summary>板相对玩家脚底的高度：留一个能走上去的低台阶（0.3m）。</summary>
        private const float StepUpHeight = 0.3f;

        /// <summary>
        /// 列出我们面组区间里**真正有面**的那些组（面序号 / 组 id / 面心坐标）。
        /// 🔴 用途：导入导航件后我们并不知道面落在哪个组、更不知道它在世界里的哪——照抄 vanilla 四槽
        /// 可能挂的是空组。有了这张表就能对准真正的组，也能看出**面有没有跟着实体走**。
        /// 明细同时进 DebugLogger（便于跨两次调用对比面心有没有变）。
        /// </summary>
        private static string ScanFaces(Mission mission)
        {
            if (PlateSpikeState.NavIdStart < 0)
                return "[Plate] no navmesh group yet - spawn first";

            List<string> faces = CollectFaces(mission, PlateSpikeState.NavIdStart);
            string detail = string.Join(" | ", faces);
            DebugLogger.Log($"[Plate] scan [{PlateSpikeState.NavIdStart},{PlateSpikeState.NavIdStart + 9}] -> {faces.Count} face(s): {detail}");

            return faces.Count == 0
                ? $"[Plate] scan: NO faces in groups [{PlateSpikeState.NavIdStart},{PlateSpikeState.NavIdStart + 9}] - import did not land there"
                : $"[Plate] scan: {faces.Count} face(s):\n  " + string.Join("\n  ", faces);
        }

        /// <summary>收集我们区间内的面（面序号 / 组 id / 面心）。</summary>
        private static List<string> CollectFaces(Mission mission, int start)
        {
            var list = new List<string>();
            int count = mission.Scene.GetNavMeshFaceCount();
            for (int i = 0; i < count; i++)
            {
                int gid = mission.Scene.GetIdOfNavMeshFace(i);
                if (gid < start || gid > start + 9)
                    continue;

                Vec3 c = Vec3.Zero;
                mission.Scene.GetNavMeshCenterPosition(i, ref c);
                list.Add($"face#{i} group={gid} center=({c.x:F2},{c.y:F2},{c.z:F2})");
                if (list.Count >= 24) break;   // 防刷屏
            }
            return list;
        }

        private static string AlignUnderPlayer()
        {
            Agent p = Agent.Main;
            if (p == null) return "[Plate] no player agent";
            if (PlateSpikeState.Plate == null) return "[Plate] no plate - spawn first";
            Vec3 pp = p.Position;
            return MoveTo(pp.x, pp.y, pp.z + StepUpHeight);
        }

        private static string MoveTo(float? x, float? y, float? z, float? dz = null)
        {
            if (PlateSpikeState.Plate == null)
                return "[Plate] no plate - spawn first";

            MatrixFrame f = PlateSpikeState.Plate.GetFrame();
            float nx = x ?? f.origin.x;
            float ny = y ?? f.origin.y;
            float nz = dz.HasValue ? f.origin.z + dz.Value : (z ?? f.origin.z);
            f.origin = new Vec3(nx, ny, nz);
            PlateSpikeState.Plate.SetFrame(ref f);

            return $"[Plate] plate -> ({nx:F1},{ny:F1},{nz:F2})";
        }
    }

    // ═══════════ 实验指令（原 Debug/MyCommands.cs，飞行尝试专用）═══════════
    public static class FlySpikeExperimentCommands
    {
        // ═══════════════════════════════════════════════════════════════
        // 🔴 外观帧抬升实验（2026-09-18）——目前最有希望的一条
        // 关键认识：**agent 的「逻辑位置」和「外观」是两个东西**。
        //   `Agent.Position`（逻辑）被导航网格锁死（Z 写不动，实测）；
        //   但 `MBAgentVisuals` 有自己的世界帧，**`SetFrame` 是公开的**。
        //   若外观能独立往上挪 → 逻辑老实待在地上（导航/碰撞/攻击全正常），
        //   渲染出来的身体在天上——**而且外观自带全部装备与动画，不需要镜像 mesh**。
        // 旁证：XiuXian 的反射成员表里就有 `set_Frame` / `get_Frame`。
        // ═══════════════════════════════════════════════════════════════

        /// <summary>外观抬升量（米，0 = 关）。</summary>
        public static float AgentVisualLift;

        /// <summary>
        /// 抬升玩家**外观**（不动逻辑位置）。
        /// 用法：custom.vlift          # 报状态
        ///       custom.vlift 10      # 外观抬高 10 米
        ///       custom.vlift 0       # 复位
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("vlift", "custom")]
        public static string SetVisualLift(List<string> args)
        {
            try
            {
                Agent player = Agent.Main;
                if (player == null) return "[VLift] no player agent";

                if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
                    return $"[VLift] amount={AgentVisualLift:F2}m " +
                           $"agentZ={player.Position.z:F2} visualZ={VisualZ(player):F2} - usage: custom.vlift <meters|0>";

                if (!float.TryParse(args[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    return $"[VLift] '{args[0]}' is not a number -> ignored. amount={AgentVisualLift:F2}m";

                AgentVisualLift = v;
                TickVisualLift();   // 立刻写一次

                DebugLogger.Log($"[VLift] amount={v:F2}m agentZ={player.Position.z:F2} visualZ={VisualZ(player):F2}");

                return $"[VLift] amount={v:F2}m agentZ={player.Position.z:F2} visualZ={VisualZ(player):F2} " +
                       $"(per-frame rewrite active; if visualZ climbs while agentZ stays, it works)";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[VLift] failed: {ex}");
                return $"[VLift] failed: {ex.Message}";
            }
        }

        private static float VisualZ(Agent a)
        {
            try
            {
                MBAgentVisuals v = a.AgentVisuals;
                if (v == null || !v.IsValid()) return float.NaN;
                return v.GetGlobalFrame().origin.z;
            }
            catch { return float.NaN; }
        }

        private static float _vLiftLogTimer;

        /// <summary>
        /// 🔴 每帧把外观帧抬到「逻辑位置 + 抬升量」（绝对值，不累积）。
        /// 关键诊断：**先读后写**并把读到的值记下来——若下一帧读回的是"逻辑位置"（而不是我们写的偏移），
        /// 说明引擎每帧把外观重置了 → 换更晚的写入点即可；若读回的是我们的偏移，说明渲染压根不读它 → 死路。
        /// </summary>
        internal static void TickVisualLift()
        {
            if (AgentVisualLift == 0f) return;
            try
            {
                Agent player = Agent.Main;
                if (player == null) return;
                MBAgentVisuals v = player.AgentVisuals;
                if (v == null || !v.IsValid()) return;

                Vec3 p = player.Position;
                MatrixFrame pre = v.GetGlobalFrame();   // ← 先读（上一帧的写入还在不在？）
                float preZ = pre.origin.z;

                pre.origin = new Vec3(p.x, p.y, p.z + AgentVisualLift);
                v.SetFrame(ref pre);

                // 每秒一行：preZ（上一帧残留）vs agentZ vs 我们的目标
                _vLiftLogTimer += 1f / 60f;
                if (_vLiftLogTimer >= 1f)
                {
                    _vLiftLogTimer = 0f;
                    DebugLogger.Log(
                        $"[VLift] preZ(上一帧残留)={preZ:F2} agentZ={p.z:F2} wrote={p.z + AgentVisualLift:F2} " +
                        $"survived={(Math.Abs(preZ - (p.z + AgentVisualLift)) < 0.5f ? "YES" : "NO(reset by engine)")}");
                }
            }
            catch (Exception ex)
            {
                AgentVisualLift = 0f;
                DebugLogger.Log($"[VLift] tick disabled after exception: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 骑手坐点抬升实验（2026-09-18）
        // 思路：骑手的世界坐标 = 坐骑骨架里 `rider_sit_bone` 的世界位置。
        //   若能把这根骨抬高，**骑手就被顶到空中，而马本体一动不动**（不用放大、不用改骨架资产）。
        // API 链（全部 public，已核实）：
        //   Agent.AgentVisuals → MBAgentVisuals.GetSkeleton() → Skeleton.SetBoneLocalFrame(idx, frame)
        //   骨索引：Monster.RiderSitBoneIndex（get-only，公开）
        // 🔴 必须在**动画更新之后**每帧重写（骨骼每帧被动画覆盖），所以挂在 MissionScreen.OnFrameTick。
        // ═══════════════════════════════════════════════════════════════

        /// <summary>坐点抬升量（米，0 = 关）。</summary>
        public static float AgentLiftAmount;

        /// <summary>上次用到的骨索引（-1 = 未初始化）。</summary>
        private static int _agentLiftBone = -1;

        /// <summary>
        /// 抬升骑手坐点骨 / 坐骑根骨。
        /// 用法：custom.lift          # 报状态
        ///       custom.lift 10      # 坐点骨（rider_sit_bone）抬高 10 米
        ///       custom.lift root 10 # 改成抬**坐骑根骨**（pelvis）——测"整匹马升空，骑手跟不跟"
        ///       custom.lift 0       # 复位
        /// 🔴 每帧**先读后写**并回报残值：区分「写了被动画覆盖」与「读它的人不跟」。
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("lift", "custom")]
        public static string SetAgentLift(List<string> args)
        {
            try
            {
                Agent player = Agent.Main;
                if (player == null) return "[Lift] no player agent";
                Agent mount = player.MountAgent;
                if (mount == null) return "[Lift] not mounted - ride a horse first";

                if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
                    return $"[Lift] amount={AgentLiftAmount:F2}m bone={_agentLiftBone}({_agentLiftBoneName}) useRoot={AgentLiftUseRoot} " +
                           $"riderZ={player.Position.z:F2} mountZ={mount.Position.z:F2} - usage: custom.lift [root] <meters|0>";

                bool useRoot = false;
                string a0 = args[0].Trim().ToLowerInvariant();
                if (a0 == "root")
                {
                    useRoot = true;
                    if (args.Count < 2 || string.IsNullOrWhiteSpace(args[1]))
                        return "[Lift] 'root' needs a number: custom.lift root <meters|0>";
                    a0 = args[1].Trim();
                }

                if (!float.TryParse(a0, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    return $"[Lift] '{a0}' is not a number -> ignored. amount={AgentLiftAmount:F2}m";

                AgentLiftUseRoot = useRoot;
                AgentLiftAmount = v;
                _agentLiftBone = useRoot ? mount.Monster.PelvisBoneIndex : mount.Monster.RiderSitBoneIndex;
                _liftBaseCaptured = false;   // 换骨/换量 → 重取基准帧

                Skeleton sk = mount.AgentVisuals?.GetSkeleton();
                int boneCount = sk != null ? (int)sk.GetBoneCount() : -1;
                _agentLiftBoneName = sk != null && _agentLiftBone >= 0 ? sk.GetBoneName((sbyte)_agentLiftBone) : "?";

                TickAgentLift();   // 立刻写一次

                DebugLogger.Log(
                    $"[Lift] amount={v:F2}m useRoot={useRoot} boneIndex={_agentLiftBone} boneName='{_agentLiftBoneName}' " +
                    $"boneCount={boneCount} riderZ={player.Position.z:F2} mountZ={mount.Position.z:F2}");

                return $"[Lift] amount={v:F2}m useRoot={useRoot} bone={_agentLiftBone}('{_agentLiftBoneName}') boneCount={boneCount} " +
                       $"riderZ={player.Position.z:F2} mountZ={mount.Position.z:F2} (per-frame rewrite; watch [Lift] survived=)";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Lift] failed: {ex}");
                return $"[Lift] failed: {ex.Message}";
            }
        }

        private static bool AgentLiftUseRoot;
        private static string _agentLiftBoneName = "";
        private static float _liftLogTimer;
        private static bool _liftBaseCaptured;
        private static MatrixFrame _liftBaseFrame;

        /// <summary>
        /// 每帧重写目标骨（先读后写 + 残值诊断）。
        /// 🔴 残值读数回答的是：**上一帧我们写进去的偏移，这一帧还在不在**——
        /// 不在 = 被动画系统覆盖（那就要找更晚的写入点）；在 = 写了有效，但"读这根骨的人"不跟。
        /// </summary>
        internal static void TickAgentLift()
        {
            if (AgentLiftAmount == 0f) return;
            try
            {
                Agent player = Agent.Main;
                if (player == null) return;
                Agent mount = player.MountAgent;
                if (mount == null) return;

                MBAgentVisuals visuals = mount.AgentVisuals;
                if (visuals == null || !visuals.IsValid()) return;
                Skeleton sk = visuals.GetSkeleton();
                if (sk == null) return;

                if (_agentLiftBone < 0)
                {
                    _agentLiftBone = AgentLiftUseRoot ? mount.Monster.PelvisBoneIndex : mount.Monster.RiderSitBoneIndex;
                    if (_agentLiftBone < 0) return;
                }

                // 🔴 记住**基准帧**（第一次读到的原始局部帧），每帧写「基准 + 偏移」。
                //    不能写「当前值 + 偏移」——若上一帧的写入存活了，就会逐帧累加、骨飞天上去。
                if (!_liftBaseCaptured)
                {
                    _liftBaseFrame = visuals.GetBoneEntitialFrame((sbyte)_agentLiftBone, useBoneMapping: false);
                    _liftBaseCaptured = true;
                }

                MatrixFrame pre = visuals.GetBoneEntitialFrame((sbyte)_agentLiftBone, useBoneMapping: false);
                float preZ = pre.origin.z;

                MatrixFrame w = _liftBaseFrame;
                w.origin = new Vec3(w.origin.x, w.origin.y, w.origin.z + AgentLiftAmount);
                sk.SetBoneLocalFrame((sbyte)_agentLiftBone, w);

                float postZ = visuals.GetBoneEntitialFrame((sbyte)_agentLiftBone, useBoneMapping: false).origin.z;

                _liftLogTimer += 1f / 60f;
                if (_liftLogTimer >= 1f)
                {
                    _liftLogTimer = 0f;
                    DebugLogger.Log(
                        $"[Lift] bone preZ={preZ:F2} postZ={postZ:F2} targetZ={_liftBaseFrame.origin.z + AgentLiftAmount:F2} " +
                        $"writeTook={Math.Abs(postZ - (_liftBaseFrame.origin.z + AgentLiftAmount)) < 0.1f} " +
                        $"riderZ={player.Position.z:F2} mountZ={mount.Position.z:F2}");
                }
            }
            catch (Exception ex)
            {
                AgentLiftAmount = 0f;
                DebugLogger.Log($"[Lift] tick disabled after exception: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 「凳子」实验（2026-09-18）：agent **使用**可交互物件（椅子等）时，
        //   位置由物件给的 user frame 决定——这是引擎**主动把 agent 放在物件上**的通路，
        //   与导航件（§7）完全不同的机制。若成立，抬物件就能抬人。
        // 要求：先坐到椅子上（原版交互），再 `custom.chairlift 5`。
        // ═══════════════════════════════════════════════════════════════

        /// <summary>物件抬升量（米，0 = 关）。</summary>
        public static float ChairLiftAmount;
        private static float _chairBaseZ = float.NaN;
        private static float _chairLogTimer;

        // ═══════════════════════════════════════════════════════════════
        // 🔴 空中态实验（2026-09-18，用户提示）：跳跃/坠落是**唯一一个 Z 被模拟而非贴地解算的状态**。
        //   全程贴地时写 Z 无效（§2 实测），但**离地期间**是否收——没验过。
        //   两条指令配套：`custom.airhold` 布防（离地即接管高度）→ `custom.airjump` 触发跳跃。
        // ═══════════════════════════════════════════════════════════════

        /// <summary>空中接管是否布防。</summary>
        public static bool AirHoldArmed;

        /// <summary>离地后每秒爬升多少米（0 = 原地悬停）。</summary>
        public static float AirHoldClimb = 2f;

        private static float _airLogTimer;
        private static float _airLastWant = float.NaN;

        /// <summary>
        /// 布防「离地即接管高度」。
        /// 用法：custom.airhold        # 报状态
        ///       custom.airhold 2      # 布防，离地后每秒 +2 米
        ///       custom.airhold 0      # 布防但只悬停（不升）
        ///       custom.airhold off    # 撤防
        /// 🔴 只有在 `IsOnLand()==False` 时才会写位置——正是要验「空中态收不收 Z」。
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("airhold", "custom")]
        public static string AirHold(List<string> args)
        {
            try
            {
                Agent player = Agent.Main;
                if (player == null) return "[Air] no player agent";

                if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
                    return $"[Air] armed={AirHoldArmed} climb={AirHoldClimb:F1}m/s onLand={player.IsOnLand()} z={player.Position.z:F2} " +
                           $"- usage: custom.airhold <m/s|0|off>";

                string a = args[0].Trim().ToLowerInvariant();
                if (a == "off")
                {
                    AirHoldArmed = false;
                    return "[Air] disarmed";
                }

                if (!float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    return $"[Air] '{args[0]}' is not a number -> ignored (armed={AirHoldArmed})";

                AirHoldClimb = v;
                AirHoldArmed = true;
                DebugLogger.Log($"[Air] armed climb={v:F1}m/s onLand={player.IsOnLand()} z={player.Position.z:F2}");
                return $"[Air] armed climb={v:F1}m/s - now get airborne (custom.airjump ctrl, or jump off something). " +
                       $"Takeover starts the moment onLand=False";
            }
            catch (Exception ex)
            {
                return $"[Air] failed: {ex.Message}";
            }
        }

        /// <summary>
        /// 触发跳跃事件（CSDN 让 NPC 跳的配方：放开控制权 + MovementFlags 清零 + Jump 位）。
        /// 用法：custom.airjump          # 直接发 Jump 位
        ///       custom.airjump ctrl     # 先放开控制权（Controller=None）再发——配方原样
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("airjump", "custom")]
        public static string AirJump(List<string> args)
        {
            try
            {
                Agent player = Agent.Main;
                if (player == null) return "[Air] no player agent";

                bool releaseCtrl = args.Count > 0 && args[0].Trim().ToLowerInvariant() == "ctrl";
                bool before = player.IsOnLand();

                if (releaseCtrl)
                {
                    V.SetAgentControllerNone(player);
                    // 🔴 必须登记，否则自动交还路径不知道控制权被拿走了（上一版漏了这行，
                    //    结果 airjump ctrl 之后输入失效、镜头丢失，只能靠 custom.set_controller player 手动救）
                    CampaignMode.FlySpikeState.ControllerTaken = true;
                }

                player.SetAgentFlags(player.GetAgentFlags() | AgentFlag.CanJump);
                player.MovementFlags = 0;
                player.EventControlFlags |= Agent.EventControlFlag.Jump;

                DebugLogger.Log($"[Air] airjump fired releaseCtrl={releaseCtrl} onLandBefore={before} canJump={(player.GetAgentFlags() & AgentFlag.CanJump) != 0}");
                return $"[Air] jump fired (releaseCtrl={releaseCtrl}, onLand before={before}) - " +
                       $"watch [Air] lines; arm with custom.airhold first if you want takeover";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Air] airjump failed: {ex}");
                return $"[Air] airjump failed: {ex.Message}";
            }
        }

        /// <summary>每帧：**仅在离地时**把高度接管过来（这就是要验的那件事）。</summary>
        internal static void TickAirHold(float dt)
        {
            if (!AirHoldArmed) return;
            try
            {
                Agent player = Agent.Main;
                if (player == null || !player.IsActive()) return;

                bool onLand = player.IsOnLand();
                if (onLand)
                {
                    _airLogTimer += dt;
                    if (_airLogTimer >= 2f)
                    {
                        _airLogTimer = 0f;
                        DebugLogger.Log($"[Air] waiting for airborne... onLand=True z={player.Position.z:F2}");
                    }
                    return;
                }

                Vec3 pos = player.Position;
                float preZ = pos.z;

                // 🔴 真正有意义的读数：**上一帧我们写进去的高度，这一帧还在不在**。
                //    （原来那个 `took = |after-want|<0.05` 是假阳性：climb=2 时每帧增量只有 0.033，比容差还小。）
                bool survived = !float.IsNaN(_airLastWant) && Math.Abs(preZ - _airLastWant) < 0.02f;

                float want = preZ + AirHoldClimb * dt;
                player.TeleportToPosition(new Vec3(pos.x, pos.y, want));
                float afterZ = player.Position.z;
                _airLastWant = want;

                _airLogTimer += dt;
                if (_airLogTimer >= 0.5f)
                {
                    _airLogTimer = 0f;
                    DebugLogger.Log(
                        $"[Air] AIRBORNE onLand=False preZ={preZ:F2} (lastWant={(_airLastWant == want ? "=" : "≠")}) " +
                        $"survived={survived} want={want:F2} after={afterZ:F2} climb={AirHoldClimb:F1}");
                }
            }
            catch (Exception ex)
            {
                AirHoldArmed = false;
                DebugLogger.Log($"[Air] tick disabled after exception: {ex.Message}");
            }
        }

        /// <summary>
        /// 🔴 列出附近**真正可交互**的物件（`UsableMissionObject`）。
        ///
        /// 为什么需要它：场景里的"座位"有两类（见 Knowledge/场景Tag与StandingPoint系统分析.md §2）——
        ///   ① **纯造型座位**（实体无脚本、只挂 tag）：坐姿只是 AI 行为，**根本不建交互绑定 → 没有"坐下"选项**；
        ///   ② 带脚本的（`Chair` / `AnimationPoint` 等）：走 `UseGameObject`，会绑定 `CurrentlyUsedGameObject`。
        /// 本指令只列②，用来区分「附近没有能坐的」与「我们搬的椅子不在可交互列表里」。
        ///
        /// 用法：custom.usable_near [半径]   # 默认 15 米
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("usable_near", "custom")]
        public static string UsableNear(List<string> args)
        {
            try
            {
                Mission mission = Mission.Current;
                Agent player = Agent.Main;
                if (mission == null || player == null)
                    return "[Usable] no mission / player agent";

                float range = 15f;
                if (args.Count > 0 && !string.IsNullOrWhiteSpace(args[0]))
                    float.TryParse(args[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out range);
                if (range < 1f) range = 1f;

                var sb = new StringBuilder();
                int n = 0;
                string usingNow = player.CurrentlyUsedGameObject?.GetType().Name ?? "<none>";

#if MB2_V1212
                foreach (MissionObject obj in mission.MissionObjects)
                {
                    if (!(obj is UsableMissionObject umo)) continue;
                    GameEntity e = obj.GameEntity;
                    if (e == null) continue;
                    Vec3 ep = e.GlobalPosition;
                    float d = (ep - player.Position).Length;
                    if (d > range) continue;
                    n++;
                    if (n <= 20)
                        sb.AppendLine($"[Usable]   {umo.GetType().Name} d={d:F1}m at ({ep.x:F1},{ep.y:F1},{ep.z:F2})");
                }
#endif

                DebugLogger.Log($"[Usable] {n} usable object(s) within {range:F0}m; player using = {usingNow}");
                return n == 0
                    ? $"[Usable] NO UsableMissionObject within {range:F0}m (player using={usingNow}) - " +
                      $"nearby 'seats' are probably decorative (no script) => no sit option is expected"
                    : $"[Usable] {n} usable object(s) within {range:F0}m (player using={usingNow}):\n{sb}";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Usable] failed: {ex}");
                return $"[Usable] failed: {ex.Message}";
            }
        }

        /// <summary>
        /// 🔴 把场景里**最近的椅子**搬到你面前（2026-09-18）。
        ///
        /// 为什么不"召唤"一个新椅子：`Chair` 是 SandBox 的 `MissionObject`，靠场景的 script component 注册；
        /// `GameEntity.Instantiate` 只能拿到网格、拿不到可交互逻辑。**搬现成的椅子才有"使用"行为**。
        ///
        /// 用法：custom.chair_to_me [距离]   # 默认 1.5 米
        /// 配套：搬完走过去坐下 → `custom.chairlift 5`（测「使用中的物件抬高，人跟不跟」）。
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("chair_to_me", "custom")]
        public static string ChairToMe(List<string> args)
        {
            try
            {
                Mission mission = Mission.Current;
                Agent player = Agent.Main;
                if (mission == null || player == null)
                    return "[Chair] no mission / player agent";

                float dist = 1.5f;
                if (args.Count > 0 && !string.IsNullOrWhiteSpace(args[0]))
                    float.TryParse(args[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out dist);
                if (dist < 0.5f) dist = 0.5f;

#if MB2_V1212
                // 🔴 关键：`Chair : UsableMachine`，"能坐"的东西是它的 `StandingPoint`（UsableMissionObject），
                //    而站位点往往是**独立实体**——只搬椅子 mesh 的话站位点留在原地，所以没有"坐下"选项。
                MissionObject best = null;
                StandingPoint bestSpot = null;
                float bestD = float.MaxValue;
                int chairCount = 0, occupied = 0, disabled = 0;

                foreach (MissionObject obj in mission.MissionObjects)
                {
                    if (!(obj is Chair chair)) continue;
                    chairCount++;
                    if (chair.IsDeactivated) continue;

                    foreach (StandingPoint sp in chair.StandingPoints)
                    {
                        if (sp == null || sp.GameEntity == null) continue;
                        if (sp.UserAgent != null) { occupied++; continue; }
                        if (sp.IsDisabledForAgent(player)) { disabled++; continue; }

                        float d = (sp.GameEntity.GlobalPosition - player.Position).LengthSquared;
                        if (d < bestD) { bestD = d; best = obj; bestSpot = sp; }
                    }
                }

                if (best == null || bestSpot == null)
                    return $"[Chair] no free sittable point ({chairCount} chair(s): {occupied} occupied, {disabled} disabled for player) - " +
                           $"try a tavern / keep interior";

                // 1) 先记下椅子与站位点的原位置
                GameEntity chairEntity = best.GameEntity;
                MatrixFrame frame = chairEntity.GetFrame();
                Vec3 oldPos = frame.origin;

                var spots = new List<(StandingPoint sp, Vec3 oldP)>();
                foreach (StandingPoint sp in ((UsableMachine)best).StandingPoints)
                {
                    if (sp?.GameEntity == null) continue;
                    Vec3 p0 = sp.GameEntity.GlobalPosition;
                    spots.Add((sp, p0));
                    float d0 = (p0 - player.Position).LengthSquared;
                    if (d0 < bestD) { bestD = d0; bestSpot = sp; }   // 以真实站位点距离为准
                }

                // 2) 搬椅子到玩家面前
                Vec3 look = player.LookDirection;
                float hl = MathF.Sqrt(look.x * look.x + look.y * look.y);
                Vec3 fwd = hl > 1e-3f ? new Vec3(look.x / hl, look.y / hl, 0f) : new Vec3(0f, 1f, 0f);
                Vec3 target = player.Position + fwd * dist;
                Vec3 delta = target - oldPos;

                frame.origin = target;
                Vec3 back = target - player.Position;
                float bl = MathF.Sqrt(back.x * back.x + back.y * back.y);
                if (bl > 1e-3f)
                    frame.rotation = Mat3.CreateMat3WithForward(in back);
                chairEntity.SetFrame(ref frame);

                // 3) 站位点若没跟着走（独立实体），按同一 delta 补移
                int moved = 0;
                foreach (var (sp, oldP) in spots)
                {
                    Vec3 want = oldP + delta;
                    Vec3 now = sp.GameEntity.GlobalPosition;
                    if ((now - want).Length > 0.05f)
                    {
                        MatrixFrame sf = sp.GameEntity.GetFrame();
                        sf.origin += (want - now);
                        sp.GameEntity.SetFrame(ref sf);
                        moved++;
                    }
                }

                // 4) 不靠交互提示：直接让玩家使用那个站位点
                string used;
                try
                {
                    player.UseGameObject(bestSpot);
                    used = player.CurrentlyUsedGameObject != null ? "OK" : "called-but-not-bound";
                }
                catch (Exception exUse)
                {
                    used = "FAILED: " + exUse.Message;
                    DebugLogger.Log($"[Chair] UseGameObject failed: {exUse}");
                }

                DebugLogger.Log(
                    $"[Chair] chair x{chairCount} (occ={occupied} dis={disabled}) moved {MathF.Sqrt(bestD):F1}m -> " +
                    $"({target.x:F1},{target.y:F1},{target.z:F2}); standingPoints={spots.Count} (correlated={moved}) ; " +
                    $"UseGameObject={used} playerZ={player.Position.z:F2}");

                return $"[Chair] chair(s)={chairCount} occ={occupied} dis={disabled}; moved to front; " +
                       $"standingPoints={spots.Count} extra-moved={moved} ; UseGameObject={used} ; playerZ={player.Position.z:F2}\n" +
                       $"Now: custom.chairlift 5";
#else
                return "[Chair] chair_to_me is only implemented for v1.2.12 (GameEntity/WeakGameEntity type differs on newer builds)";
#endif
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Chair] chair_to_me failed: {ex}");
                return $"[Chair] chair_to_me failed: {ex.Message}";
            }
        }

        /// <summary>
        /// 抬升玩家**正在使用的物件**。
        /// 用法：custom.chairlift      # 报状态
        ///       custom.chairlift 5    # 把椅子抬高 5 米（先坐上去）
        ///       custom.chairlift 0    # 复位
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("chairlift", "custom")]
        public static string SetChairLift(List<string> args)
        {
            try
            {
                Agent player = Agent.Main;
                if (player == null) return "[Chair] no player agent";
                UsableMissionObject used = player.CurrentlyUsedGameObject;
                if (used == null)
                    return $"[Chair] not using any object - sit on a chair first (IsUsingGameObject={player.IsUsingGameObject})";

                if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
                    return $"[Chair] amount={ChairLiftAmount:F2}m used={used.GetType().Name} playerZ={player.Position.z:F2} " +
                           $"- usage: custom.chairlift <meters|0>";

                if (!float.TryParse(args[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    return $"[Chair] '{args[0]}' is not a number -> ignored";

                ChairLiftAmount = v;
                _chairBaseZ = float.NaN;   // 重新取基准
                TickChairLift();

                DebugLogger.Log($"[Chair] amount={v:F2}m used={used.GetType().Name} playerZ={player.Position.z:F2}");
                return $"[Chair] amount={v:F2}m used={used.GetType().Name} playerZ={player.Position.z:F2} " +
                       $"(per-frame; if playerZ climbs with the chair, this is a real carry channel)";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Chair] failed: {ex}");
                return $"[Chair] failed: {ex.Message}";
            }
        }

        /// <summary>每帧把"正在使用的物件"抬到「原始高度 + 抬升量」（绝对值）。</summary>
        internal static void TickChairLift()
        {
            if (ChairLiftAmount == 0f) return;
            try
            {
                Agent player = Agent.Main;
                if (player == null) return;
                UsableMissionObject used = player.CurrentlyUsedGameObject;
                if (used == null) return;

                GameEntity e = used.GameEntity;
                if (e == null) return;

                MatrixFrame f = e.GetFrame();
                if (float.IsNaN(_chairBaseZ)) _chairBaseZ = f.origin.z;
                f.origin = new Vec3(f.origin.x, f.origin.y, _chairBaseZ + ChairLiftAmount);
                e.SetFrame(ref f);

                _chairLogTimer += 1f / 60f;
                if (_chairLogTimer >= 1f)
                {
                    _chairLogTimer = 0f;
                    DebugLogger.Log(
                        $"[Chair] chairZ={e.GetFrame().origin.z:F2} baseZ={_chairBaseZ:F2} " +
                        $"playerZ={player.Position.z:F2} using={used.GetType().Name}");
                }
            }
            catch (Exception ex)
            {
                ChairLiftAmount = 0f;
                DebugLogger.Log($"[Chair] tick disabled after exception: {ex.Message}");
            }
        }

        /// <summary>
        /// 🔴 坐骑缩放实验（2026-09-18）：验证「把坐骑放大 → 骑手被顶到坐点高度」这条路。
        ///
        /// 机制（反编译实证 Mission.BuildAgent，mission.cs:3787-3797）：
        ///   建号时若 `SpawnEquipment[EquipmentIndex.ArmorItemEndSlot]` 非空且其 `HorseComponent.BodyLength != 0`
        ///   → `agent.SetInitialAgentScale(0.01f * BodyLength)`。
        ///   🔴 **`ArmorItemEndSlot = 10 = Horse`（同一个槽位值）**——骑手身上就带着马这件装备，
        ///   所以这段对**骑手也跑一遍**：马放大几倍、骑手也放大几倍（"巨马 + 巨人"）。
        ///
        /// 本指令做的事 = 放大坐骑后**把骑手缩放改回 1.0**（`MBAPI.IMBAgent.SetAgentScale`，公开绑定），
        /// 看骑手的位置会不会留在被抬高的坐点上——若会，就能得到「正常身材的骑手悬在高处」。
        ///
        /// 用法：custom.mount_scale          # 报当前缩放与坐标
        ///       custom.mount_scale 3        # 坐骑放大 3 倍，骑手归 1 倍
        ///       custom.mount_scale 1        # 复原
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("mount_scale", "custom")]
        public static string MountScale(List<string> args)
        {
            try
            {
                Agent player = Agent.Main;
                if (player == null)
                    return "[MountScale] no player agent";
                Agent mount = player.MountAgent;
                if (mount == null)
                    return "[MountScale] not mounted - ride a horse first";

                if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
                    return $"[MountScale] mountScale={mount.AgentScale:F2} riderScale={player.AgentScale:F2} " +
                           $"mountZ={mount.Position.z:F2} riderZ={player.Position.z:F2} - usage: custom.mount_scale <0.1-20>";

                if (!float.TryParse(args[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float s)
                    || s < 0.1f || s > 20f)
                    return $"[MountScale] '{args[0]}' is not a scale in 0.1-20 -> ignored. " +
                           $"current mountScale={mount.AgentScale:F2} riderScale={player.AgentScale:F2}";

                MBAPI_Unavailable_Skip();   // 见下方说明：native 绑定不可达，改走反射

                SetAgentScaleViaReflection(mount, s);
                SetAgentScaleViaReflection(player, 1f);   // 骑手恢复原尺寸

                DebugLogger.Log(
                    $"[MountScale] set mount->{s:F2} rider->1.00 ; " +
                    $"readback mountScale={mount.AgentScale:F2} riderScale={player.AgentScale:F2} " +
                    $"mountZ={mount.Position.z:F2} riderZ={player.Position.z:F2} onLand={player.IsOnLand()}");

                return $"[MountScale] mount->{s:F2} rider->1.00 ; readback mountScale={mount.AgentScale:F2} " +
                       $"riderScale={player.AgentScale:F2} mountZ={mount.Position.z:F2} riderZ={player.Position.z:F2} " +
                       $"(re-run custom.print_pos_dir after a few seconds to see if it sticks)";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[MountScale] failed: {ex}");
                return $"[MountScale] failed: {ex.Message}";
            }
        }

        private static void MBAPI_Unavailable_Skip() { }

        /// <summary>
        /// 反射调 `Agent.SetInitialAgentScale(float)`。
        /// 🔴 为什么必须反射：该方法在 Agent 上是 **internal**，而其底层的 native 绑定
        /// `MBAPI.IMBAgent.SetAgentScale` 也**从 mod 程序集不可达**
        /// （实测编译错误：`MBAPI` 不含 `IMBAgent` 成员；且 `Agent` 不公开 native 指针 `Pointer`）。
        /// 反射是本条路唯一入口。
        /// </summary>
        private static bool SetAgentScaleViaReflection(Agent agent, float scale)
        {
            try
            {
                MethodInfo mi = typeof(Agent).GetMethod("SetInitialAgentScale",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (mi == null) return false;
                mi.Invoke(agent, new object[] { scale });
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[MountScale] reflection SetInitialAgentScale failed: {ex.Message}");
                return false;
            }
    }

        }
}