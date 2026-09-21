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
    /// custom.flight stop            强制落地
    /// custom.flight verbose on|off  开逐帧诊断日志
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
                    return WithBehavior(b => b.ForceStop());

                case "verbose":
                {
                    if (args.Count >= 2)
                    {
                        string v = args[1].ToLowerInvariant();
                        FlightTuning.VerboseLog = v == "on" || v == "1" || v == "true";
                    }
                    return $"OK. verbose={(FlightTuning.VerboseLog ? "on" : "off")}";
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
                for (int i = 0; i < FlightCameraRig.PresetNames.Length; i++)
                    sb.AppendLine("  " + FlightCameraRig.Describe((FlightCamPreset)i));
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
                return "ERR usage: custom.flight cam <preset> <param> <value> | param: arm pitch yaw pivotx pivoty pivotz socketx sockety socketz selfyaw selfpitch selfroll fov";

            int idx = Array.IndexOf(FlightCameraRig.PresetNames, k);
            if (idx < 0)
                return $"ERR unknown preset '{k}' | hover cruise boost aim";

            string field = args[2].ToLowerInvariant();
            if (!float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                return $"ERR '{args[3]}' is not a number";

            FlightCamPreset preset = (FlightCamPreset)idx;
            SpringArmCameraParam p = FlightCameraRig.Presets[idx];
            switch (field)
            {
                case "arm": p.ArmLength = v; break;
                case "pitch": p.ArmPitch = v; break;
                case "yaw": p.ArmYaw = v; break;
                case "pivotx": p.PivotX = v; break;
                case "pivoty": p.PivotY = v; break;
                case "pivotz": p.PivotZ = v; break;
                case "socketx": p.SocketX = v; break;
                case "sockety": p.SocketY = v; break;
                case "socketz": p.SocketZ = v; break;
                case "selfyaw": p.SelfYaw = v; break;
                case "selfpitch": p.SelfPitch = v; break;
                case "selfroll": p.SelfRoll = v; break;
                case "fov": p.Fov = v; break;
                default:
                    return $"ERR unknown param '{field}' | arm pitch yaw pivotx pivoty pivotz socketx sockety socketz selfyaw selfpitch selfroll fov";
            }
            FlightCameraRig.Presets[idx] = p;

            DebugLogger.Log("[FlightCam] " + FlightCameraRig.Describe(preset));
            return "OK. " + FlightCameraRig.Describe(preset);
        }

        private static string Tune(List<string> args)
        {
            if (args.Count < 3)
                return "ERR usage: custom.flight tune <key> <value> | 动画: blend | 机位: presetblend camblend camhandover camhandback | gesture: dbljump longpressjump landtap landheight landdive divedepth descend descendrate landtouch landeps landgrace landanim landmax gentleland takeoffanim takeoffdelay takeoffblend takeoffskip | camera: camsens caminvertx caminverty campitchmin campitchmax | flight: cruise boost accel longpress pitch pitchout turnrate spawngap settle | (hover/clearance/maxalt/vrate 已退役)";

            string key = args[1].ToLowerInvariant();
            if (!float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                return $"ERR '{args[2]}' is not a number";

            switch (key)
            {
                case "cruise": FlightTuning.CruiseSpeed = v; break;
                case "boost": FlightTuning.BoostSpeed = v; break;
                case "accel": FlightTuning.Accel = v; break;
                case "hover": FlightTuning.HoverAltitude = v; break;            // 🪦 已退役（板不再自动抬升，值不起作用）
                case "clearance": FlightTuning.MinClearance = v; break;         // 🪦 已退役
                case "maxalt": FlightTuning.MaxAltitude = v; break;             // 🪦 已退役
                case "vrate": FlightTuning.VerticalRate = v; break;             // 🪦 已退役（板只按 WASD 动）
                case "takeoffanim": FlightTuning.TakeoffAnimSeconds = v; break; // 起飞入姿动画时长（秒；0=立刻交给飞行）
                case "takeoffdelay": FlightTuning.TakeoffSpawnDelay = v; break; // 先切动作→晚这么久再召唤板（秒；0=同帧）
                case "takeoffblend": FlightTuning.TakeoffBlendIn = v; break;    // 起飞动作淡入时长（秒；越小越"立刻起势"）
                case "takeoffskip": FlightTuning.TakeoffSkipSeconds = v; break; // 跳过起飞 clip 开头（秒）
                case "landgrace": FlightTuning.LandTouchGraceSeconds = v; break; // 进空中态后多久内不判撞地（秒）
                case "landrate": FlightTuning.LandRate = v; break;
                case "autoland": FlightTuning.AutoLandSeconds = v; break;
                case "longpress": FlightTuning.LongPressSeconds = v; break;
                case "pitch": FlightTuning.PitchThreshold = v; break;
                case "pitchout": FlightTuning.PitchExitThreshold = v; break;
                case "blend": FlightTuning.AnimBlendIn = v; break;
                case "presetblend": FlightTuning.CamBlendIn = v; break;         // 只改【机位之间】的过渡时长（动画交叉淡化不变）
                case "camhandover": FlightTuning.UseCamHandover = v != 0f; break;   // 进出相机是否做交接（0=硬切，旧行为）
                case "camhandback": FlightTuning.CamHandBackLook = v != 0f; break;  // 归还时是否把朝向写回引擎
                case "turnrate": FlightTuning.TurnRateDegPerSec = v; break;     // 机身转向角速度（度/秒；0=瞬时，回到旧行为）
                case "spawngap": FlightTuning.CarrierSpawnGap = v; break;       // 板面比碰撞体底面再低多少（米）
                case "feetoffset": FlightTuning.CarrierSpawnGap = v; break;     // 旧键名，等价 spawngap（口径已改）
                case "settle": FlightTuning.TakeoffSettleSeconds = v; break;    // 起飞等踩上板的最长等待（秒；0=不等）
                case "gentleland": FlightTuning.LandAnimOnGentle = v != 0f; break; // 空格落地也播落地动画？（0=不播，默认）
                // 起飞 / 落地手势（2026-09-21 N2 起改）
                case "dbljump": FlightTuning.TakeoffByDoubleJump = v != 0f; break;      // 二段跳起飞
                case "longpressjump": FlightTuning.TakeoffByLongPress = v != 0f; break; // 长按起飞（后备）
                case "landtap": FlightTuning.LandByTap = v != 0f; break;                // 短按落地总闸
                case "landheight": FlightTuning.LandTapMaxHeight = v; break;            // 短按落地的高度闸（米）
                case "landdive": FlightTuning.LandTapWhileDiving = v != 0f; break;      // 俯冲时短按可落地
                case "divedepth": FlightTuning.LandTapDivePitch = v; break;             // "冲向地面"判据（0.42≈低头25°）
                case "descend": FlightTuning.LandByLongPressDescend = v != 0f; break;   // 长按=持续下降
                case "descendrate": FlightTuning.DescendRate = v; break;                // 下降速率（米/秒）
                case "landtouch": FlightTuning.LandOnGroundTouch = v != 0f; break;      // 撞地自动落地
                case "landeps": FlightTuning.LandTouchEps = v; break;
                case "landanim": FlightTuning.LandAnimSeconds = v; break;       // 落地动画时长（触地后至少等这么久再收摊）
                case "landmax": FlightTuning.LandMaxSeconds = v; break;         // 落地阶段硬上限
                // 相机接管后的"看"（2026-09-21）
                case "camfov": FlightTuning.UseFlightCamera = v != 0f; break;   // 同 cam on|off
                case "camsens": FlightTuning.CamLookSensitivity = v; break;     // 鼠标灵敏度（度/像素基准）
                case "caminvertx": FlightTuning.InvertCamX = v != 0f; break;    // 左右反向
                case "caminverty": FlightTuning.InvertCamY = v != 0f; break;    // 上下反向
                case "campitchmin": FlightTuning.CamPitchMin = v; break;
                case "campitchmax": FlightTuning.CamPitchMax = v; break;
                case "camblend": FlightTuning.CamBlendIn = v; FlightTuning.AnimBlendIn = v; break;  // 两个一起调（两个字段本来就该同源）
                default:
                    return $"ERR unknown key '{key}'";
            }

            return $"OK. {key}={v} | {FlightTuning.Describe()}";
        }
    }
}
