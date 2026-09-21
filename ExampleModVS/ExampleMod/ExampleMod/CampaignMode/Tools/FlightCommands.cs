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
    /// custom.flight hide on|off     是否隐藏木板（阶段 1 默认显示，便于肉眼验收）
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

                case "freeze":
                    return Freeze(args);

                case "hide":
                {
                    // 阶段 1 让板【显示】便于肉眼验收；出货前再关掉
                    if (args.Count >= 2)
                    {
                        string v = args[1].ToLowerInvariant();
                        FlightTuning.HideCarrier = v == "on" || v == "1" || v == "true";
                    }
                    return $"OK. carrier mesh hidden={(FlightTuning.HideCarrier ? "on" : "off")} (takes effect on next takeoff)";
                }

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

        private static string Tune(List<string> args)
        {
            if (args.Count < 3)
                return "ERR usage: custom.flight tune <key> <value> | keys: cruise boost accel hover clearance maxalt vrate longpress pitch pitchout blend sigillift | dbljump longpressjump landtap landheight landdive divedepth descend descendrate landtouch landeps";

            string key = args[1].ToLowerInvariant();
            if (!float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                return $"ERR '{args[2]}' is not a number";

            switch (key)
            {
                case "cruise": FlightTuning.CruiseSpeed = v; break;
                case "boost": FlightTuning.BoostSpeed = v; break;
                case "accel": FlightTuning.Accel = v; break;
                case "hover": FlightTuning.HoverAltitude = v; break;
                case "clearance": FlightTuning.MinClearance = v; break;
                case "maxalt": FlightTuning.MaxAltitude = v; break;
                case "vrate": FlightTuning.VerticalRate = v; break;
                case "landrate": FlightTuning.LandRate = v; break;
                case "autoland": FlightTuning.AutoLandSeconds = v; break;
                case "longpress": FlightTuning.LongPressSeconds = v; break;
                case "pitch": FlightTuning.PitchThreshold = v; break;
                case "pitchout": FlightTuning.PitchExitThreshold = v; break;
                case "blend": FlightTuning.AnimBlendIn = v; break;
                case "feetoffset": FlightTuning.CarrierFeetOffset = v; break;
                case "sigillift": FlightTuning.SigilLiftZ = v; break;
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
                case "landeps": FlightTuning.LandTouchEps = v; break;                   // 撞地容差（米）
                default:
                    return $"ERR unknown key '{key}'";
            }

            return $"OK. {key}={v} | {FlightTuning.Describe()}";
        }
    }
}
