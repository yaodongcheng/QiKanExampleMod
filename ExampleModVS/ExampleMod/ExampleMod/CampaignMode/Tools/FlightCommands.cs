using System;
using System.Collections.Generic;
using System.Globalization;
using LivingWorldNpcs.Flight;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.CampaignMode
{
    /// <summary>
    /// 飞行调试命令 <c>custom.flight</c>（2026-09-21）。
    ///
    /// 用法：
    /// <code>
    /// custom.flight                 查状态（首参可弃：认不出的首参当占位符，回落 status）
    /// custom.flight start           强制起飞（绕过长按空格）
    /// custom.flight stop|drop|exit  强制出机 ⇒ 引擎原生掉落接管（掉着按空格 / 再敲一次 drop 可回飞）
    /// custom.flight dodge           强制闪避一次（只出位移；闪避**姿态**照常由冲刺中按 Z 触发）
    /// custom.flight log on|off [full]  飞行 tick 日志总闸（默认关；full = 连每帧那行也开）
    /// custom.flight verbose on|off  逐帧日志子开关（要配合总闸）
    /// custom.flight tune &lt;键&gt; &lt;值&gt;  热调一个参数（见下）
    /// custom.flight reset           参数回出厂值
    /// </code>
    ///
    /// 🔴 两条项目纪律：返回文本**纯英文**（要显示在游戏内控制台）；**首参可弃**。
    /// </summary>
    public static class FlightCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("flight", "custom")]
        public static string Flight(List<string> args)
        {
            string sub = (args != null && args.Count > 0) ? args[0].ToLowerInvariant() : "status";
            string note = string.Empty;

            switch (sub)
            {
                case "start":
                    return WithBehavior(b => b.ForceStart());

                case "stop":
                case "land":
                case "exit":
                case "drop":
                    return WithBehavior(b => b.ForceDrop());

                case "dodge":
                    return WithBehavior(b => b.ForceDodge());

                case "fx":
                    return Fx(args);

                case "trail":
                    return Trail(args);

                case "log":
                {
                    // 🔴 飞行 tick 日志总闸（2026-09-22 用户要求：默认关，要查时再开）。
                    //    管：[Flight-Diag] 三行 / [Flight] 姿态 → +屏幕提示 / [Flight] air v=…。
                    //    **状态机那档不归它管**（2026-09-25 起在 `Animation/AnimDebug.cs`，用 `custom.anim_log`）。
                    //    **异常路径的日志不受它管**（冻结失败/载具召唤失败/tick 异常/抖动自检）—— 那些永远留着。
                    if (args.Count >= 2)
                    {
                        string v = args[1].ToLowerInvariant();
                        if (v == "on" || v == "1" || v == "true")
                        {
                            FlightTuning.DebugLog = true;
                            // 第三参 full = 连每帧那行也开
                            FlightTuning.VerboseLog = args.Count >= 3 &&
                                (args[2].ToLowerInvariant() is "full" or "verbose" or "all");
                        }
                        else
                        {
                            FlightTuning.DebugLog = false;
                            FlightTuning.VerboseLog = false;
                        }
                    }
                    return $"OK. debugLog={(FlightTuning.DebugLog ? "on" : "off")} "
                         + $"verbose={(FlightTuning.VerboseLog ? "on" : "off")} "
                         + "| usage: custom.flight log on|off [full]";
                }

                case "verbose":
                {
                    if (args.Count >= 2)
                    {
                        string v = args[1].ToLowerInvariant();
                        FlightTuning.VerboseLog = v == "on" || v == "1" || v == "true";
                    }
                    return $"OK. verbose={(FlightTuning.VerboseLog ? "on" : "off")} "
                         + $"(note: per-frame line also needs the master switch: debugLog={(FlightTuning.DebugLog ? "on" : "off")})";
                }

                case "tune":
                    return Tune(args);

                case "cam":
                    return Cam(args);

                case "freeze":
                    return Freeze(args);

                case "hide":
                    // 🪦 已退役（2026-09-21 合一版）：载具本身就是法阵，没有"隐藏"这一步了。
                    //    隐藏木板会把碰撞一起干掉（实机摔死过主角），这条路已被证伪。
                    return "REMOVED. carrier is the sigil itself now (no hide step). see plans/flight plan, 2026-09-21.";

                case "reset":
                    FlightTuning.ResetToDefaults();
                    return "OK. flight tuning reset to defaults. " + FlightTuning.Describe();

                case "status":
                case "info":
                    break;

                default:
                    // 首参可弃纪律：认不出就当占位符，回落 status 并注明
                    note = $" [note: '{args[0]}' is not a subcommand -> status]";
                    break;
            }

            return WithBehavior(b => b.Status()) + note;
        }

        private static string WithBehavior(Func<PlayerFlightBehavior, string> action)
        {
            Mission mission = Mission.Current;
            if (mission == null)
                return "ERR no active mission (enter a scene first)";

            PlayerFlightBehavior behavior = null;
            try
            {
                behavior = mission.GetMissionBehavior<PlayerFlightBehavior>();
            }
            catch (Exception ex)
            {
                return $"ERR cannot query mission behavior: {ex.Message}";
            }

            if (behavior == null)
                return "ERR flight behavior not mounted (gate ordering? see MySubModule registration)";

            try
            {
                return "OK. " + action(behavior);
            }
            catch (Exception ex)
            {
                return $"ERR {ex.Message}";
            }
        }

        /// <summary>
        /// <c>custom.flight freeze [模式]</c> —— 查/切「飞行时怎么让主角别自己走」的手法。
        /// 不带参数 = 只查；飞行中切换**立即生效**（不用落地重飞、不用重编译）。
        /// </summary>
        private static string Freeze(List<string> args)
        {
            if (args.Count < 2)
                return $"OK. freeze={FlightTuning.Freeze} | modes: ctrloff aipause aidetach ai none flags off";

            string name = args[1].ToLowerInvariant();
            FlightFreezeMode mode;
            switch (name)
            {
                case "ctrloff": mode = FlightFreezeMode.CtrlOff; break;
                case "off": mode = FlightFreezeMode.Off; break;
                case "flags": mode = FlightFreezeMode.Flags; break;
                case "ai": mode = FlightFreezeMode.Ai; break;
                case "aipause": mode = FlightFreezeMode.AiPaused; break;
                case "aidetach": mode = FlightFreezeMode.AiDetach; break;
                case "none": mode = FlightFreezeMode.None; break;
                default:
                    return $"ERR unknown freeze mode '{name}' | modes: ctrloff aipause aidetach ai none flags off";
            }

            return WithBehavior(b => b.SetFreezeMode(mode));
        }

        /// <summary>
        /// <c>custom.flight fx [粒子名]</c> —— **在脚下地面放一次粒子**（验收用，不用真的飞一遍）。
        ///
        /// · 不带参数 = 放**落地特效**（= `flight.xml` 里 `超人落地` 的 `enter="land-fx"` 那一个）
        /// · 带名字   = 放指定的粒子系统，如 <c>custom.flight fx lwn_manual_fly_land</c>
        ///
        /// 🔴 **为什么要这条命令**：落地特效只有"真的飞一次并撞地"才看得到，一次试错好几分钟。
        ///    这条把它拆成"敲一下就看"，也是 CLAUDE.md 那条「每做完一个功能必须交付验收指令」的要求。
        ///
        /// 🔴 走的**就是状态机那条路**（同一个 <see cref="FlightAnimConditions.PlayFxAtFeet"/>），
        ///    所以能同时验两件事：① 粒子本身做没做出来 ② 位置口径对不对（脚下地面）。
        ///    ⚠️ 但它**不验 enter 挂没挂上** —— 那条要真飞一次撞地，或看日志里
        ///    `[Anim:flight] … → 超人落地` 那行之后有没有 `[Flight-Fx] 播放 …`。
        /// </summary>
        private static string Fx(List<string> args)
        {
            string name = (args != null && args.Count > 1 && !string.IsNullOrWhiteSpace(args[1]))
                ? args[1].Trim()
                : FlightAnimConditions.LandingFxName;

            string error;
            bool ok = FlightAnimConditions.PlayFxAtFeet(name, out error);
            return ok
                ? $"OK. played '{name}' at your feet."
                : $"ERR {error}";
        }

        /// <summary>
        /// <c>custom.flight trail [on &lt;粒子名&gt;|off &lt;粒子名&gt;|off]</c> —— **骨挂持续粒子的手动挂 / 摘**。
        ///
        /// · 不带参数        = 列出当前挂着的粒子（没挂 = `none attached`）
        /// · <c>on &lt;粒子名&gt;</c>  = 手动挂一颗粒子（**不用真去冲刺**）—— 名字 = 内容包发布包里注册的那个
        /// · <c>off &lt;粒子名&gt;</c> = 摘掉指定那颗粒子
        /// · <c>off</c>          = 全摘
        ///
        /// 🔴 **为什么要这条命令**：这几颗平时只有"冲刺中"才看得到，想单独看 / 想验"摘了到底会不会
        ///    当场消失"都得先飞起来。这条把它拆成"敲一下就看"（CLAUDE.md 那条
        ///    「每做完一个功能必须交付验收指令」）。
        /// ⚠️ 它**不验"轨道声明对不对"** —— 那条要真冲刺一次，看日志里
        ///    `[Anim:flight] … enter[容器]` 之后有没有 `[Flight-Fx] '<粒子名>' 已挂上`。
        ///    ⚠️ `on` 需要显式给 `bone=` 那几根骨 —— 命令不方便写骨名，所以**只支持飞行的三颗**
        ///    （名字在 <see cref="FlightAnimConditions"/>）；要试别的走 XML 轨道。
        /// </summary>
        private static string Trail(List<string> args)
        {
            string a1 = (args != null && args.Count > 1) ? args[1].Trim().ToLowerInvariant() : "status";
            switch (a1)
            {
                case "on":
                {
                    string particle = (args.Count > 2) ? args[2].Trim() : null;
                    if (string.IsNullOrEmpty(particle))
                    {
                        return "ERR usage: custom.flight trail on <particleName> | known: "
                             + KnownParticles();
                    }
                    string bones = BonesFor(particle);
                    if (bones == null)
                    {
                        return "ERR '" + particle + "' is not a flight trail particle | known: " + KnownParticles();
                    }
                    bool ok = FlightBoneFx.Instance.Attach(particle, bones.Split('+'));
                    return ok
                        ? $"OK. '{particle}' attached ({FlightBoneFx.Instance.Describe()})."
                        : $"ERR '{particle}' attach failed (see log: id -1 = not published / no bones)";
                }
                case "off":
                {
                    string particle = (args.Count > 2) ? args[2].Trim() : null;
                    if (string.IsNullOrEmpty(particle))
                    {
                        FlightBoneFx.Instance.DetachAll();
                        return $"OK. all detached ({FlightBoneFx.Instance.Describe()}).";
                    }
                    bool had = FlightBoneFx.Instance.Detach(particle);
                    return had
                        ? $"OK. '{particle}' detached ({FlightBoneFx.Instance.Describe()}) -- watch the leftover particles."
                        : $"OK. '{particle}' was not attached (nothing to detach).";
                }
                default:
                    return $"OK. {FlightBoneFx.Instance.Describe()} | usage: custom.flight trail "
                         + "[on <particleName> | off [particleName]] | known: " + KnownParticles();
            }
        }

        /// <summary>验收命令认识的三颗（骨名给定；换挂点走 XML 轨道）。</summary>
        private static string KnownParticles()
            => FlightAnimConditions.HandTrailFxName + " / " + FlightAnimConditions.BodyTrailFxName
             + " / " + FlightAnimConditions.BoostLoopFxName;

        /// <summary>粒子名 → 命令默认用的骨名单（认不出 = null）。</summary>
        private static string BonesFor(string particle)
        {
            if (particle == FlightAnimConditions.HandTrailFxName) return "HandL+HandR";
            if (particle == FlightAnimConditions.BodyTrailFxName) return "Abdomen";
            if (particle == FlightAnimConditions.BoostLoopFxName) return "Abdomen";
            return null;
        }

        /// <summary>
        /// <c>custom.flight cam [机位] [参数] [值]</c> —— 飞行运动相机的**热调**（N5）。
        ///
        /// · 不带参数            = 列出 4 个机位的全部参数
        /// · <c>&lt;机位&gt; &lt;参数&gt; &lt;值&gt;</c> = 改一个（飞行中立即生效，下一帧的渐变就会用新值）
        /// · <c>on|off</c>            = 开关"飞行时接管相机"（下次起飞生效）
        ///
        /// 机位：hover / cruise / boost / aim
        /// 参数：arm pitch yaw | pivotx pivoty pivotz | socketx sockety socketz | selfyaw selfpitch selfroll | fov
        ///
        /// 🔴 **参数是"第一版猜测值"，没实机调过** —— 尤其 `pitch` 的正负（原实现自己注了
        ///    "左右手定则需测试"）。飞起来边看边调，调好把值报回来我写死成默认。
        /// </summary>
        private static string Cam(List<string> args)
        {
            if (args.Count < 2)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"OK. flightCamera={FlightTuning.UseFlightCamera} blend={FlightTuning.CamBlendIn}s aimOnRMB={FlightTuning.AimOnRightClick}");
                if (FlightCameraRig.EnsureCases())
                {
                    for (int i = 0; i < FlightCameraRig.PresetNames.Length; i++)
                        sb.AppendLine("  " + FlightCameraRig.Describe((FlightCamPreset)i));
                }
                else
                {
                    sb.AppendLine("  ERR: Camera.csv is missing the fly_* rows -> flight camera will NOT take over"
                                + " (fix ModuleData/DesignData/Camera.csv; no code fallback by design)");
                }
                sb.Append("usage: custom.flight cam <hover|cruise|boost|aim> <param> <value> | cam on|off");
                return sb.ToString();
            }

            string k = args[1].ToLowerInvariant();
            if (k == "on" || k == "off")
            {
                FlightTuning.UseFlightCamera = k == "on";
                return $"OK. flightCamera={FlightTuning.UseFlightCamera} (takes effect on next takeoff)";
            }

            if (args.Count < 4)
                return "ERR usage: custom.flight cam <preset> <param> <value> | param: arm pitch yaw pivotx pivoty pivotz socketx sockety socketz selfyaw selfpitch selfroll fov lag lagmax fovvz armvz rollyaw";

            int idx = Array.IndexOf(FlightCameraRig.PresetNames, k);
            if (idx < 0)
                return $"ERR unknown preset '{k}' | hover cruise boost aim";

            // 🪦 2026-10-05 阶段 3：逐参数设置**已迁移到 `custom.cam set`**（机位数值的唯一来源 = Camera.csv，
            //    命令统一到相机模块）。旧写法返回迁移提示，**不静默**。
            return $"ERR: 'custom.flight cam <preset> <param> <value>' retired -> use "
                 + $"'custom.cam set fly_{k} <column> <value>' (e.g. custom.cam set fly_{k} arm 5). "
                 + "'custom.cam show fly_" + k + "' to inspect.";
        }

        private static string Tune(List<string> args)
        {
            if (args.Count < 3)
                return "ERR usage: custom.flight tune <key> <value> | anim: blend hoverblend pitchblend | camPreset: presetblend camblend camhandover camhandback mergedrig | gesture: dbljump longpressjump landtouch landeps landgrace landanim landmax falloff takeoffanim takeoffdelay takeoffblend takeoffskip | dodge: dodgeinboost dodgedist dodgetime dodgecd dodgeanim dashanim | fall: fallcam fallgate fallride fallg fallterm fallbrake | camLook: camsens caminvertx caminverty campitchmin campitchmax camlag cammotion | flight: cruise boost accel longpress pitch pitchout turnrate steer steerboost steeridle boostfwd spawngap settle";

            string key = args[1].ToLowerInvariant();
            if (!float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                return $"ERR '{args[2]}' is not a number";

            switch (key)
            {
                case "cruise": FlightTuning.CruiseSpeed = v; break;
                case "boost": FlightTuning.BoostSpeed = v; break;
                case "accel": FlightTuning.Accel = v; break;
                case "takeoffanim": FlightTuning.TakeoffAnimSeconds = v; break; // 起飞入姿动画时长（秒；0=立刻交给飞行）
                case "takeoffdelay": FlightTuning.TakeoffSpawnDelay = v; break; // 先切动作→晚这么久再召唤板（秒；0=同帧）
                case "takeoffblend": FlightTuning.TakeoffBlendIn = v; break;    // 起飞动作淡入时长（秒；越小越"立刻起势"）
                case "takeoffskip": FlightTuning.TakeoffSkipSeconds = v; break; // 跳过起飞 clip 开头（秒）
                case "landgrace": FlightTuning.LandTouchGraceSeconds = v; break; // 进空中态后多久内不判撞地（秒）
                case "landrate": FlightTuning.LandRate = v; break;
                case "longpress": FlightTuning.LongPressSeconds = v; break;
                case "pitch": FlightTuning.PitchThreshold = v; break;
                case "pitchout": FlightTuning.PitchExitThreshold = v; break;
                case "blend": FlightTuning.AnimBlendIn = v; break;
                case "hoverblend": FlightTuning.HoverBlendSeconds = v; break;   // 悬停⇄巡航 这两条边的过渡时长（秒；默认 1.0）
                case "pitchblend": FlightTuning.PitchBlendSeconds = v; break;   // 升降姿态（升⇄平⇄降）那几条边（秒；默认 0.45）
                case "presetblend": FlightTuning.CamBlendIn = v; break;         // 只改【机位之间】的过渡时长（动画交叉淡化不变）
                case "camhandover": FlightTuning.UseCamHandover = v != 0f; break;   // 进出相机是否做交接（0=硬切，旧行为）
                case "camhandback": FlightTuning.CamHandBackLook = v != 0f; break;  // 归还时是否把朝向写回引擎
                case "turnrate": FlightTuning.TurnRateDegPerSec = v; break;     // 机身转向角速度（度/秒；0=瞬时，回到旧行为）
                // 航向惯性（2026-09-27）：实际"往哪走"的转向角速度 —— 与上面 turnrate 分工见 FlightTuning
                case "steer": FlightTuning.SteerRateDegPerSec = v; break;            // 悬停/巡航档（度/秒；0=瞬时=旧行为）
                case "steerboost": FlightTuning.SteerRateBoostDegPerSec = v; break;  // 冲刺档（度/秒；更低=高速转向更"重"）
                case "steeridle": FlightTuning.SteerIdleResetSeconds = v; break;     // 停稳多久算"没有航向动量"（秒）
                // 冲刺档：只按 Shift、一个方向键都没按 ⇒ 当作按着 W 往前飞（0 = 关掉，回到"原地趴着悬停"）
                case "boostfwd": FlightTuning.BoostImpliesForward = v != 0f; break;
                case "spawngap": FlightTuning.CarrierSpawnGap = v; break;       // 板面比碰撞体底面再低多少（米）
                case "feetoffset": FlightTuning.CarrierSpawnGap = v; break;     // 旧键名，等价 spawngap（口径已改）
                case "settle": FlightTuning.TakeoffSettleSeconds = v; break;    // 起飞等踩上板的最长等待（秒；0=不等）
                // 起飞 / 落地手势（2026-09-21 N2 起改）
                case "dbljump": FlightTuning.TakeoffByDoubleJump = v != 0f; break;      // 二段跳起飞
                case "longpressjump": FlightTuning.TakeoffByLongPress = v != 0f; break; // 长按起飞（后备）
                case "landtouch": FlightTuning.LandOnGroundTouch = v != 0f; break;      // 撞地自动落地
                case "landeps": FlightTuning.LandTouchEps = v; break;
                case "landanim": FlightTuning.LandAnimSeconds = v; break;
                case "falloff": FlightTuning.FallOffDistance = v; break;        // 离板多远算"掉下去了"（米）       // 落地动画时长（触地后至少等这么久再收摊）
                case "landmax": FlightTuning.LandMaxSeconds = v; break;         // 落地阶段硬上限
                // 冲刺入姿 / 闪避（2026-09-22；闪避键 2026-09-27 改成 C）
                case "dodgeinboost": FlightTuning.DodgeInBoost = v != 0f; break;     // 冲刺中按 Z = 闪避（0=关掉闪避的位移一侧）
                case "dodgedist": FlightTuning.DodgeDistance = v; break;        // 闪避位移距离（米）
                case "dodgetime": FlightTuning.DodgeDisplaceSeconds = v; break; // 闪避位移走完用时（秒）
                case "dodgecd": FlightTuning.DodgeCooldownSeconds = v; break;   // 两次闪避的冷却（秒）
                case "dodgeanim": FlightTuning.DodgeClipSeconds = v; break;     // 闪避姿态动画时长（秒；重导 clip 后改）
                case "dashanim": FlightTuning.BoostStartSeconds = v; break;     // 冲刺入姿动画时长（秒；重导 clip 后改）
                // 出机掉落（2026-09-27）：掉落的物理/动画是引擎的，这两条只管"我们留什么"
                case "fallcam": FlightTuning.KeepCameraWhileFalling = v != 0f; break;  // 坠落期间相机跟着（0=出机当场还给引擎）
                case "fallgate": FlightTuning.HoldInputWhileFalling = v != 0f; break;  // 坠落期间保留输入闸（0=出机当场解冻）
                case "fallride": FlightTuning.FallRide = v != 0f; break;               // 坠落方式：1=板载（默认，支撑不断）/ 0=拆板自由落体
                case "fallg": FlightTuning.FallRideAccel = v; break;                   // 板载坠落的加速度（m/s²，默认 9.3 ≈ 0.95g）
                case "fallterm": FlightTuning.FallRideTerminal = v; break;             // 板载坠落速度上限（m/s）
                case "fallbrake": FlightTuning.FallRideBrake = v; break;               // 回飞时收干下坠速度的减速度（m/s²）
                // 压弯（2026-09-22）
                case "bank": FlightTuning.BankThreshold = v; break;             // 进压弯的横移阈值（|A/D|；0=关掉压弯）
                case "bankout": FlightTuning.BankExitThreshold = v; break;      // 退出压弯的阈值（迟滞）
                case "statemsg": FlightTuning.ShowStateMessages = v != 0f; break; // 姿态变化时屏幕弹提示（0=关）
                // 相机接管后的"看"（2026-09-21；🔴 2026-10-05 起这三项是 **Camera.csv 的逐 case 列**，
                // 命令改的是**表行内存态**，不再有全局 FlightTuning 字段）
                case "camfov": FlightTuning.UseFlightCamera = v != 0f; break;   // 同 cam on|off
                // 🔴 **合并相机机器**（2026-10-05 阶段 4）：1 = 飞行走相机服务 + 合并机器（全项目一台机器）。
                //    **改完要重进场景**（驱动在行为对象构造时选定）。A/B 用：一趟 0、一趟 1，比 [FlightCam] 日志与手感。
                case "mergedrig": FlightTuning.UseMergedRig = v != 0f; break;
                case "camsens":
                    if (!FlightCameraRig.SetAllLookSens(v))
                        return "ERR: Camera.csv fly_* rows missing -> camera cases not loaded (nothing changed)";
                    break;                                                       // 鼠标灵敏度（度/像素基准）
                case "caminvertx": FlightTuning.InvertCamX = v != 0f; break;    // 左右反向
                case "caminverty": FlightTuning.InvertCamY = v != 0f; break;    // 上下反向
                case "campitchmin":
                    if (!FlightCameraRig.SetAllPitchMin(v))
                        return "ERR: Camera.csv fly_* rows missing -> camera cases not loaded (nothing changed)";
                    break;
                case "campitchmax":
                    if (!FlightCameraRig.SetAllPitchMax(v))
                        return "ERR: Camera.csv fly_* rows missing -> camera cases not loaded (nothing changed)";
                    break;
                // 弹簧跟随速率（相机位置滞后）—— 一次改三档（悬停/巡航/冲刺），**瞄准档固定不滞后**
                // （瞄目标时镜头必须是硬的，滞后会让准心飘）。0 = 相机焊死在角色身上 = 2026-09-27 之前的行为。
                case "camlag":
                    if (!FlightCameraRig.SetAllLagSpeed(v))
                        return "ERR: Camera.csv fly_* rows missing -> camera cases not loaded (nothing changed)";
                    break;
                // 运动驱动的**总增益**（竖直速率 / 航向角速度 → FOV·臂长·侧倾）。0 = 一键关掉整套。
                case "cammotion": FlightTuning.CamMotionGain = v; break;
                case "camblend": FlightTuning.CamBlendIn = v; FlightTuning.AnimBlendIn = v; break;  // 两个一起调（两个字段本来就该同源）
                default:
                    return $"ERR unknown key '{key}'";
            }

            return $"OK. {key}={v} | {FlightTuning.Describe()}"
                 + (key == "mergedrig" ? " | NOTE: takes effect after re-entering the scene (driver is picked when the behavior is created)" : "");
        }
    }
}
