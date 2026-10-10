using System;
using System.Collections.Generic;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 通用音效播放小助手（2026-10-10 立，钩索绳声 / 飞行风噪接入时抽出的共用件）。
	///
	/// 名字 = <c>ModuleData/module_sounds.xml</c> 里声明的事件名（soln 体系 —— 必须挂在
	/// <c>ModuleData/project.mbproj</c> 的 <c>soln_module_sound</c> 行上，见那个文件的注释）。
	/// 播放写法照抄 <see cref="FirearmFxLogic"/> 里**已实机验证**的那几行：
	/// GetEventIdFromString → CreateEvent → SetPosition → Play。
	///
	/// 容错（铁律 1/2）：名字为空 / 查不到 / 引擎抛异常 → 静默跳过，只记一次日志；
	/// 绝不外抛 —— 调用方都在 tick 或战斗回调链上，异常会打断上层节奏。
	///
	/// ⚠️ 同款小实现另有两份（<c>SpellWorld.PlaySound</c> / <c>FirearmFxLogic</c>）——
	/// 那两份属已实机验证的旧代码，**不动**；新消费点一律走本类（避免第四份复制）。
	/// </summary>
	internal static class SoundFx
	{
		/// <summary>音效名 → 引擎事件 id 的缓存（宁可不播也不重复查字符串）。-1 = 查过、不存在。</summary>
		private static readonly Dictionary<string, int> _soundIds = new Dictionary<string, int>(StringComparer.Ordinal);

		/// <summary>已记过日志的键（防刷屏）。</summary>
		private static readonly HashSet<string> _logged = new HashSet<string>(StringComparer.Ordinal);

		/// <summary>已打过"衰减距离"日志的事件名（每名一次）。</summary>
		private static readonly HashSet<string> _distLogged = new HashSet<string>(StringComparer.Ordinal);

		/// <summary>
		/// 在指定位置播一次（3D —— 声音随听者转向、随距离衰减）。
		/// 返回值 = 句柄（查不到 / 播不出 = null）；**要"跟着听者走"的调用方**留着它，
		/// 每帧 <c>SetPosition</c> 跟到相机上（飞行风噪范本见 <see cref="ListenerPosition"/> 与
		/// <c>Flight/FlightWindFx.cs</c>）；不需要的话忽略返回值即可。
		/// </summary>
		public static SoundEvent Play3D(string name, Vec3 position)
		{
			Mission mission = Mission.Current;
			if (string.IsNullOrEmpty(name) || mission?.Scene == null)
			{
				return null;
			}
			return Play(name, mission.Scene, position);
		}

		/// <summary>
		/// 相机位置（引擎每帧把相机实体的位置填进 <c>Mission.GetCameraFrame()</c>）。
		/// 现在的用途 = **兜底锚点**：玩家不在场时的替代位置（范本 `Flight/FlightWindFx.AnchorPosition`）。
		///
		/// 🔴 **"自己身上的声音"的正确挂法**（飞行风噪就这么播）：**3D 音效 + 每帧把位置跟到锚点上**
		///    —— 锚点首选**玩家本体**（`Agent.Main.Position`），听者处 ≈ 零距离 = 满音量不衰减
		///    = 听感等价于 2D，但走的是**已验证能响的 3D 通道**。
		///
		/// ⚠️ **不要走 2D**（`module_sounds.xml` 里 `is_2d="true"` + 无位置播放）：**本项目实测不出声**
		///    （2026-10-10：`custom.grapple sound` 3D 响、`custom.flight sound` 2D 全程静音，
		///    事件 id 解析正常、分类合法）。原因在 native，不在我们能查的 C# 层 —— 绕开它。
		///
		/// ⚠️ 相机接管期间（`CustomCamera != null`）**位置照读没问题**，只有**方向**会错
		///    （CLAUDE.md 铁律 35）—— 这里只用位置。
		/// </summary>
		public static Vec3 ListenerPosition()
		{
			Mission mission = Mission.Current;
			if (mission == null)
			{
				return Vec3.Zero;
			}
			return mission.GetCameraFrame().origin;
		}

		private static SoundEvent Play(string name, Scene scene, Vec3 position)
		{
			int id = TryResolve(name);
			if (id < 0)
			{
				return null;
			}
			try
			{
				SoundEvent sound = SoundEvent.CreateEvent(id, scene);
				if (sound == null)
				{
					return null;
				}
				sound.SetPosition(position);
				sound.Play();
				LogAttenuationOnce(name, sound);
				return sound;
			}
			catch (Exception ex)
			{
				LogOnce("音效播放异常 " + name, ex);
			}
			return null;
		}

		/// <summary>解析事件名 → 引擎 id（带缓存）。-1 = 查不到（已记一次日志）。</summary>
		public static int TryResolve(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return -1;
			}
			int id;
			if (_soundIds.TryGetValue(name, out id))
			{
				return id;
			}
			try
			{
				id = SoundEvent.GetEventIdFromString(name);
			}
			catch (Exception ex)
			{
				id = -1;
				LogOnce("音效名解析异常 " + name, ex);
			}
			_soundIds[name] = id;
			if (id < 0)
			{
				DebugLogger.Log($"[SoundFx] 音效 '{name}' 查不到 —— 静音。"
					+ "检查 ModuleData/module_sounds.xml 是否声明、以及 ModuleData/project.mbproj 是否挂了 soln_module_sound 行");
			}
			return id;
		}

		/// <summary>给控制台命令的单行回执（🔴 纯英文，工作流约定）。</summary>
		public static string Describe(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return "name=(empty) -> silent";
			}
			int id = TryResolve(name);
			return id >= 0
				? $"'{name}' -> event id {id}, OK"
				: $"'{name}' -> NOT FOUND (check module_sounds.xml + soln_module_sound line in project.mbproj)";
		}

		/// <summary>
		/// **一次性**把该事件的 3D 衰减距离打进日志（每个名字只打一次）。
		///
		/// 为什么要它（2026-10-10 排查"声音太小/听不见"）：3D 音效听不听得见取决于
		/// **听者（相机）到声源的距离 vs 事件的 min/max 距离** —— 拉拽机位臂长 8 米、
		/// 飞行机位也在身后几米，这个数是判断"是不是被距离衰减吃掉了"的唯一实测依据。
		/// 打印三个分量是想全（`x`/`y` 哪个是 min/max 没在官方文档里写明，先看实测值）。
		/// </summary>
		private static void LogAttenuationOnce(string name, SoundEvent sound)
		{
			if (!_distLogged.Add(name))
			{
				return;
			}
			try
			{
				Vec3 mm = sound.GetEventMinMaxDistance();
				DebugLogger.Log($"[SoundFx] '{name}' 衰减距离 vec=({mm.x:F1}, {mm.y:F1}, {mm.z:F1})"
					+ "（相机会话距离超过它就会被衰减 —— 排查'太小/听不见'先看这行）");
			}
			catch (Exception)
			{
				// 读不到就算了 —— 纯诊断，绝不影响播放
			}
		}

		private static void LogOnce(string key, Exception ex)
		{
			if (!_logged.Add(key))
			{
				return;
			}
			DebugLogger.Log($"[SoundFx] {key}：{ex.GetType().Name} {ex.Message}");
		}
	}
}
