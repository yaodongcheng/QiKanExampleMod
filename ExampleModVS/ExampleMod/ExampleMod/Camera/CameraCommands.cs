using System;
using System.Collections.Generic;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **相机调试台（`custom.cam`）** —— 相机自己的模块（`Camera/`），与钩索解耦
	/// （2026-10-04 用户要求："相机的调试日志转移到自定义相机自己的模块里，不太适合放在 grapple"）。
	///
	/// 子命令（首参可弃、返回纯英文 —— 项目控制台纪律）：
	///   custom.cam log &lt;0|1&gt;   诊断日志总开关（**默认 0 = 关**）：控制 `[FollowCam]` 的逐帧 / 交班明细 /
	///                          撒手后采样 / 引擎内参等诊断输出；生命周期行（接管/归还/异常）始终打。
	///                          会话级：重进游戏重置为关。
	///   custom.cam stat          立刻采一行"**引擎默认相机**"（机位 / Δ眼 / 角度 / 引擎内参）到日志。
	///                            站着不动也能敲（**别在拉拽过程中敲** —— 那时相机在我们手上，读到的是过期值）。
	///   custom.cam info          一行报告"**我们的跟随相机**"现状（是否在跟 / 臂长 / 俯仰 / yaw / 眼高 / 抬升项）。
	///   custom.cam test [&lt;秒&gt;]   原地切换标定台：站着不动把相机交给我们的跟随相机 N 秒（默认 3）再自动归还 ——
	///                            方向/臂长/FOV 照抄引擎、不动人 ⇒ 专看"接管那一下 / 归还那一下"有没有视觉突变。
	///   custom.cam lift [&lt;米&gt;]   跟随期间固定高度修正（正值 = 我们的相机抬高；默认 0 = 不修）。
	///                            正常应为 0 —— 引擎那条 +0.484 米的抬高已按公式补进 `SpringArmMath.ComputeEngineLift`；
	///                            这个旋钮只作应急/微调。
	///
	/// 常配套使用：`log 1` → `test`（原地切一次）→ 读日志（接管/交班/撒手后采样+时间序列）→ 定位是哪一项差。
	/// </summary>
	internal static class CameraCommands
	{
		[CommandLineFunctionality.CommandLineArgumentFunction("cam", "custom")]
		public static string Execute(List<string> args)
		{
			string sub = "stat";
			int at = 0;
			if (args != null && args.Count > 0 && args[0] != null)
			{
				string s = args[0].Trim().ToLowerInvariant();
				if (s == "log" || s == "stat" || s == "info" || s == "test" || s == "lift" || s == "help")
				{
					sub = s;
					at = 1;
				}
				else
				{
					// 首参可弃（项目纪律）：认不出就当占位，回落到 status 并注明
					return "cam: unknown subcommand '" + args[0] + "' (use: log|stat|info|test|lift). Status: "
						+ "logging=" + (SpringArmCameraView.DebugLogging ? 1 : 0)
						+ " lift=" + SpringArmCameraView.CameraLiftMeters.ToString("F2");
				}
			}

			switch (sub)
			{
				case "help":
					return "cam: log <0|1> | stat | info | test [seconds] | lift [meters]";

				case "log":
				{
					string v = ArgAt(args, at);
					if (string.IsNullOrEmpty(v))
					{
						return $"cam: debug logging = {(SpringArmCameraView.DebugLogging ? 1 : 0)} (default 0)";
					}
					bool on = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase)
					            || v.Equals("off", StringComparison.OrdinalIgnoreCase));
					SpringArmCameraView.DebugLogging = on;
					return $"cam: debug logging = {(on ? 1 : 0)}"
						+ (on ? " (per-frame / handoff / post-release diagnostics ON)" : " (diagnostics OFF)");
				}

				case "stat":
				{
					if (Mission.Current == null)
					{
						return "Error: not in mission.";
					}
					SpringArmCameraView.LogEngineCameraNow("cam.stat");
					return "OK: engine camera dumped to log ([FollowCam] cam.stat ...)";
				}

				case "info":
				{
					if (Mission.Current == null)
					{
						return "Error: not in mission.";
					}
					return SpringArmCameraView.DumpFollowCameraNow("cam.info");
				}

				case "test":
				{
					if (Mission.Current == null || Agent.Main == null)
					{
						return "Error: not in mission.";
					}
					float secs = ParseF(args, at, 3f);
					if (secs < 0.5f || secs > 30f)
					{
						return "Error: test seconds should be 0.5 .. 30";
					}
					// 方向/臂长/FOV 全部照抄引擎此刻的值（不拉远、不动人）⇒ 接管瞬间理论上零变化；
					// 归还时不再预置俯仰（predictReset 关：没有解冻就不会有引擎重置，预置反而制造差）。
					bool ok = SpringArmCameraView.ApplyFollowFromEngineCamera(Agent.Main, secs,
						writeBackLookOnReturn: false, clearSpecialCameraOnReturn: true);
					return ok
						? $"OK: camera switched to ours for {secs:F1}s (engine look/arm kept), then hands back automatically."
						: "Error: camera takeover failed.";
				}

				case "lift":
				{
					string v = ArgAt(args, at);
					if (string.IsNullOrEmpty(v))
					{
						return $"cam: lift = {SpringArmCameraView.CameraLiftMeters:F2}m";
					}
					float m = ParseF(args, at, -999f);
					if (m <= -900f || m < -2f || m > 2f)
					{
						return "Error: lift meters should be within -2 .. 2";
					}
					SpringArmCameraView.SetCameraLiftMeters(m);
					return $"cam: lift = {m:F2}m (takes effect immediately)";
				}
			}
			return "Error: unknown camera subcommand.";
		}

		private static string ArgAt(List<string> args, int i)
		{
			return args != null && i >= 0 && i < args.Count && args[i] != null ? args[i].Trim() : null;
		}

		private static float ParseF(List<string> args, int i, float fallback)
		{
			string s = ArgAt(args, i);
			float v;
			if (!string.IsNullOrEmpty(s)
				&& float.TryParse(s, System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out v))
			{
				return v;
			}
			return fallback;
		}
	}
}
