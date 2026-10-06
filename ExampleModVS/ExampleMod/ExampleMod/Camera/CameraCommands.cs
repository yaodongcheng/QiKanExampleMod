using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **相机调试台（`custom.cam`）** —— 相机自己的模块（`Camera/`），**唯一的相机命令族**
	/// （2026-10-05 阶段 3：钩索的 `aim*` 与飞行的逐参数命令都并进这里）。
	///
	/// | 子命令 | 干什么 |
	/// |---|---|
	/// | `log &lt;0\|1&gt;` | 诊断日志总开关（**默认 0 = 关**；关着只留生命周期行） |
	/// | `stat` | 立刻采一行"引擎默认相机"到日志（站着不动也能敲） |
	/// | `info` | 一行报告"我们的相机"现状（在跟 / case / 臂长 / 方向 / 鼠标驱动…） |
	/// | `test [秒]` | **黄金测试**：原地切到引擎机位 N 秒再自动归还（坐标厘米级一致、画面零变化） |
	/// | `lift [米]` | 跟随期间固定高度修正（正常 0） |
	/// | `play &lt;case&gt; [秒]` | 起播任意机位 case（默认 3 秒；`Agent.Main` 为锚） |
	/// | `stop` | 立刻把相机还给引擎 |
	/// | `set &lt;case&gt; &lt;列&gt; &lt;值&gt;` | **热改内存 case**（`list`/`show` 看列名） |
	/// | `show [case]` | 打印该行解析出的全部数值 + 行为开关（静态核对用） |
	/// | `list` | 列出 Camera.csv 里的全部 case |
	///
	/// 首参可弃、返回纯英文（项目控制台纪律）。常配套：`log 1` → `play follow_engine 3` → 读日志。
	/// </summary>
	internal static class CameraCommands
	{
		/// <summary>命令起播用的持有者标签（诊断用完自己还）。</summary>
		private const string CommandOwner = "camcmd";

		[CommandLineFunctionality.CommandLineArgumentFunction("cam", "custom")]
		public static string Execute(List<string> args)
		{
			string sub = "stat";
			int at = 0;
			if (args != null && args.Count > 0 && args[0] != null)
			{
				string s = args[0].Trim().ToLowerInvariant();
				if (s == "log" || s == "stat" || s == "info" || s == "test" || s == "lift" || s == "help"
					|| s == "show" || s == "list" || s == "play" || s == "stop" || s == "set")
				{
					sub = s;
					at = 1;
				}
				else
				{
					// 首参可弃（项目纪律）：认不出就当占位，回落到 status 并注明
					return "cam: unknown subcommand '" + args[0] + "' (use: log|stat|info|test|lift|play|stop|set|show|list). Status: "
						+ "logging=" + (SpringArmRig.DebugLogging ? 1 : 0)
						+ " lift=" + SpringArmRig.CameraLiftMeters.ToString("F2");
				}
			}

			switch (sub)
			{
				case "help":
					return "cam: log <0|1> | stat | info | test [seconds] | lift [meters] | play <case> [seconds] | stop | set <case> <column> <value> | show [case] | list";

				case "log":
				{
					string v = ArgAt(args, at);
					if (string.IsNullOrEmpty(v))
					{
						return $"cam: debug logging = {(SpringArmRig.DebugLogging ? 1 : 0)} (default 0)";
					}
					bool on = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase)
					            || v.Equals("off", StringComparison.OrdinalIgnoreCase));
					SpringArmRig.DebugLogging = on;
					return $"cam: debug logging = {(on ? 1 : 0)}"
						+ (on ? " (per-frame / handoff / post-release diagnostics ON)" : " (diagnostics OFF)");
				}

				case "stat":
				{
					if (Mission.Current == null)
					{
						return "Error: not in mission.";
					}
					CameraService.LogEngineCameraNow("cam.stat");
					return "OK: engine camera dumped to log ([FollowCam] cam.stat ...)";
				}

				case "info":
				{
					if (Mission.Current == null)
					{
						return "Error: not in mission.";
					}
					return CameraService.DumpCameraNow("cam.info");
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
					// 黄金测试：方向/臂长/FOV 全部照抄引擎此刻的值（不拉远、不动人）⇒ 接管瞬间理论零变化；
					// 归还时清掉引擎"特殊相机"冻结修正（没有解冻就不会有引擎重置，所以**不预置俯仰**）。
					bool ok = CameraService.PlayEnginePose(Agent.Main, secs, CommandOwner,
						new CameraReturnPolicy { ClearSpecial = true });
					return ok
						? $"OK: camera switched to ours for {secs:F1}s (engine look/arm kept), then hands back automatically."
						: "Error: camera takeover failed (held by someone else? see log).";
				}

				case "lift":
				{
					string v = ArgAt(args, at);
					if (string.IsNullOrEmpty(v))
					{
						return $"cam: lift = {SpringArmRig.CameraLiftMeters:F2}m";
					}
					float m = ParseF(args, at, -999f);
					if (m <= -900f || m < -2f || m > 2f)
					{
						return "Error: lift meters should be within -2 .. 2";
					}
					SpringArmRig.CameraLiftMeters = m;
					return $"cam: lift = {m:F2}m (takes effect immediately)";
				}

				case "play":
				{
					if (Mission.Current == null || Agent.Main == null)
					{
						return "Error: not in mission.";
					}
					string caseName = ArgAt(args, at);
					if (string.IsNullOrEmpty(caseName))
					{
						return "Error: play needs a case name (custom.cam list). e.g. custom.cam play grapple_shot 5";
					}
					float secs = ParseF(args, at + 1, 3f);
					if (secs < 0.5f || secs > 120f)
					{
						return "Error: play seconds should be 0.5 .. 120";
					}
					bool ok = CameraService.Play(caseName, Agent.Main, secs, CommandOwner);
					return ok
						? $"OK: playing case '{caseName}' for {secs:F1}s (hands back automatically; custom.cam info to inspect)."
						: $"Error: cannot play '{caseName}' (missing in Camera.csv? or camera held by someone else -- see log).";
				}

				case "stop":
				{
					string held = CameraService.Holder;
					CameraService.Stop();
					return "OK: camera handed back to the engine" + (held != null ? $" (was held by {held})" : " (nothing was holding it)");
				}

				case "set":
				{
					// `custom.cam set <case> <column> <value>` —— 热改**内存里的 case**（表是只读的；
					// 想持久化就改 ModuleData/DesignData/Camera.csv 再重启）。
					string caseName = ArgAt(args, at);
					string column = ArgAt(args, at + 1);
					string value = ArgAt(args, at + 2);
					if (string.IsNullOrEmpty(caseName) || string.IsNullOrEmpty(column) || string.IsNullOrEmpty(value))
					{
						return "Error: set needs <case> <column> <value>. Columns: arm pitch yaw pivotx pivoty pivotz "
							 + "socketx sockety socketz selfyaw selfpitch selfroll fov lag lagmax fovvz armvz rollyaw "
							 + "anchorworld engineeye seed mouse sens pitchmin pitchmax anchorhead anchorheight";
					}
					if (!CameraCase.TryGet(caseName, out CameraCase kase))
					{
						return "cam: no case '" + caseName + "' in Camera.csv (use: cam list)";
					}
					string err = ApplyColumn(kase, column.ToLowerInvariant(), value);
					if (err != null)
					{
						return err;
					}
					CameraService.NotifyCaseEdited(kase);   // 改的是当前在用的那一行 ⇒ 立刻生效（实时可见）
					string line = kase.Describe();
					DebugLogger.Log("[FollowCam] cam.set " + line);
					return line;
				}

				case "show":
				{
					// 静态核对用：打印 Camera.csv 里**这一行解析出来的全部机位数值 + 行为开关**。
					string name = ArgAt(args, at);
					if (string.IsNullOrEmpty(name))
					{
						return ListCases();
					}
					if (!CameraCase.TryGet(name, out CameraCase kase))
					{
						return "cam: no case '" + name + "' in Camera.csv (use: cam list)";
					}
					string line = kase.Describe();
					DebugLogger.Log("[FollowCam] cam.show " + line);
					return line;
				}

				case "list":
					return ListCases();
			}
			return "Error: unknown camera subcommand.";
		}

		// ───────────────────────────── set 的列映射 ─────────────────────────────

		/// <summary>
		/// 改一个列。**0/1 开关写 0/1**（跟表里的类型行一致）；`seed` 认 `engine`/`row`。
		/// 返回 null = 成功；否则是一句错误（英文）。
		/// </summary>
		private static string ApplyColumn(CameraCase kase, string col, string raw)
		{
			// 开关类（不许走 float 解析）
			if (col == "seed")
			{
				if (raw.Equals("engine", StringComparison.OrdinalIgnoreCase)) { kase.Seed = CameraSeed.Engine; return null; }
				if (raw.Equals("row", StringComparison.OrdinalIgnoreCase)) { kase.Seed = CameraSeed.Row; return null; }
				return "Error: seed value must be 'engine' or 'row'";
			}
			if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
			{
				return $"Error: '{raw}' is not a number";
			}

			switch (col)
			{
				case "arm": case "armlength": kase.Param.ArmLength = v; return null;
				case "pitch": case "armpitch": kase.Param.ArmPitch = v; return null;
				case "yaw": case "armyaw": kase.Param.ArmYaw = v; return null;
				case "pivotx": kase.Param.PivotX = v; return null;
				case "pivoty": kase.Param.PivotY = v; return null;
				case "pivotz": kase.Param.PivotZ = v; return null;
				case "socketx": kase.Param.SocketX = v; return null;
				case "sockety": kase.Param.SocketY = v; return null;
				case "socketz": kase.Param.SocketZ = v; return null;      // 老 `aimlift` 的等价物
				case "selfyaw": kase.Param.SelfYaw = v; return null;
				case "selfpitch": kase.Param.SelfPitch = v; return null;
				case "selfroll": kase.Param.SelfRoll = v; return null;
				case "fov": kase.Param.Fov = v; return null;
				case "lag": case "lagspeed": kase.Param.LagSpeed = v; return null;
				case "lagmax": case "lagmaxdistance": kase.Param.LagMaxDistance = v; return null;
				case "fovvz": case "fovpervz": kase.Param.FovPerVz = v; return null;
				case "armvz": case "armpervz": kase.Param.ArmPerVz = v; return null;
				case "rollyaw": case "rollyawrate": kase.Param.RollPerYawRate = v; return null;
				case "anchorworld": case "isanchorworld": kase.Param.IsAnchorWorld = v > 0.5f; return null;
				case "engineeye": case "useengineeyeheight": kase.Param.UseEngineEyeHeight = v > 0.5f; return null;
				case "mouse": case "mouselook": kase.MouseLook = v > 0.5f; return null;
				case "sens": case "looksens": kase.LookSens = v; return null;
				case "pitchmin": kase.PitchMin = v; return null;
				case "pitchmax": kase.PitchMax = v; return null;
				case "anchorhead": case "anchorfollowhead": kase.AnchorFollowHead = v > 0.5f; return null;
				case "anchorheight": kase.AnchorHeight = v; return null;
			}
			return "Error: unknown column '" + col + "'. Columns: arm pitch yaw pivotx pivoty pivotz "
				 + "socketx sockety socketz selfyaw selfpitch selfroll fov lag lagmax fovvz armvz rollyaw "
				 + "anchorworld engineeye seed mouse sens pitchmin pitchmax anchorhead anchorheight";
		}

		// ───────────────────────────── 小工具 ─────────────────────────────

		private static string ArgAt(List<string> args, int i)
		{
			return args != null && i >= 0 && i < args.Count && args[i] != null ? args[i].Trim() : null;
		}

		private static float ParseF(List<string> args, int i, float fallback)
		{
			string s = ArgAt(args, i);
			float v;
			if (!string.IsNullOrEmpty(s)
				&& float.TryParse(s, NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out v))
			{
				return v;
			}
			return fallback;
		}

		/// <summary>列出 Camera.csv 里全部 case（`custom.cam list`；表没加载 = 明确说清楚）。全英文（控制台纪律）。</summary>
		private static string ListCases()
		{
			List<string> all = CameraCase.All();
			if (all.Count == 0)
				return "cam: Camera.csv not loaded / empty (GameDatabase.Camera) — check ModuleData/DesignData/Camera.csv";
			var sb = new System.Text.StringBuilder();
			sb.Append("cam: ").Append(all.Count).Append(" cases:");
			foreach (string id in all)
				sb.Append("\n  ").Append(id);
			return sb.ToString();
		}
	}
}
