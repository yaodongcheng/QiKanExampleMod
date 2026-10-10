using System;
using System.Collections.Generic;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.Flight
{
	/// <summary>
	/// 飞行风噪：飞起来按速度密度播"一阵一阵"的风声（2026-10-10 立）。
	///
	/// 🔴 **播放方式 = 3D 音效 + 每帧把位置跟到玩家身上**（用户 2026-10-10 定的口径：
	///    "做成 3D，跟 main agent"）。锚点 = `Agent.Main.Position`（玩家不在时兜底用相机，
	///    见 <see cref="AnchorPosition"/>）—— 玩家处 ≈ 听者处 = 满音量不衰减 = 听感等价 2D，
	///    但走的是**已验证能响的 3D 通道**：
	///    ⚠️ 2D（`is_2d="true"` + 无位置播放）**本项目实测不出声**（2026-10-10：绳声 3D 响、
	///    风声 2D 全程静音，事件 id 解析正常）。
	///    **为什么要每帧跟**：阵风最长 1.8 秒，冲刺 26 m/s 时世界锚定的一阵会漂到 45 米外 ⇒ 尾巴被距离吃掉；
	///    跟着玩家走才始终是"身上的风"。
	///
	/// 🔴 **为什么用"密度"而不是音量**：引擎没有音量 API（SoundEvent 只有位置 / 速度 / 参数），
	///    而素材 `lwn_flight_wind_1~6` 是带衰减包络的阵风（不是无缝循环）——所以强度靠
	///    "速度越快、两阵之间越短"表达：悬停没风 → 巡航约 1.6 秒一阵 → 冲刺约 0.45 秒一阵。
	///
	/// **谁在喂速度**（两条都喂同一个调度器，机制见 <see cref="Tick"/>）：
	/// `PlayerFlightBehavior.OnMissionTick`（飞行模式：起飞/空中/坠落）· `GrapplePull.Tick`（被钩索拽着飞）。
	/// 参数全在 <see cref="FlightTuning"/>（`custom.flight tune windmin|windmax|windgapmin|windgapmax`）。
	/// </summary>
	internal static class FlightWindFx
	{
		private static readonly Random _rng = new Random();

		/// <summary>正在响的阵风句柄 —— 每帧把它们的**位置跟到玩家**上（见 <see cref="FollowAnchor"/>）。</summary>
		private static readonly List<SoundEvent> _live = new List<SoundEvent>();

		/// <summary>这些句柄属于哪个场景 —— 换场景 = 句柄随旧场景作废，**绝不能再碰**
		/// （同 GrappleCommands 体检板的场景守卫口径：对已销毁的东西调任何函数都可能直接崩）。</summary>
		private static UIntPtr _liveScene = UIntPtr.Zero;

		/// <summary>同时最多跟几个句柄（防御：万一 IsPlaying 判不干净，也不会无限堆积）。</summary>
		private const int MaxLive = 8;

		/// <summary>距下一阵风的秒数（≤0 = 该来风了）。</summary>
		private static float _timer;

		/// <summary>上一次 Tick 速度是否在门槛以上（"重新飞起来 / 刚开始拉拽 ⇒ 尽早来一阵"的标记）。
		/// 🔴 它是**两路调用者共存**的关键：见 <see cref="Tick"/> 的注释。</summary>
		private static bool _armed;

		/// <summary>累计阵风数 / 上一阵的间隔（`custom.flight sound` 回执用，纯取证）。</summary>
		private static int _gusts;
		private static float _lastInterval;

		/// <summary>
		/// 风声锚点 = **玩家本体**（`Agent.Main.Position`，与火器枪声的定位置法同款）。
		/// 玩家不在（换场景 / 阵亡）时兜底用相机位置 —— 宁可能听见，也别让风停不下来或炸掉。
		/// </summary>
		public static Vec3 AnchorPosition()
		{
			Agent main = Agent.Main;
			if (main != null && AgentControlHelper.SafeIsActive(main))
			{
				return main.Position;
			}
			return SoundFx.ListenerPosition();
		}

		/// <summary>
		/// **立刻来一阵**（**不受速度门槛管**）—— 供"拉拽开始"这类**事件型**触发用。
		///
		/// 🔴 为什么要它（2026-10-10 实机：用户"被钩锁拉起来时没听到风声"）：拉拽全程只有 ~1 秒，
		///    且速度曲线末段掉到门槛以下（Hermite 末斜率 0.286）⇒ 只靠"按速度密度"可能只赶上半阵、还偏晚；
		///    起拽那一刻本来就是最该有"呼"一声的时候 ⇒ 直接给一阵，之后按正常节奏接。
		/// 开关关着 / 名字为空 / 不在场景里 = 不播（返回 false）。
		/// </summary>
		public static bool Kick()
		{
			if (!FlightTuning.WindSoundEnabled || string.IsNullOrEmpty(FlightTuning.WindSoundName))
			{
				return false;
			}
			if (Mission.Current?.Scene == null)
			{
				return false;
			}
			_armed = true;
			_timer = FlightTuning.WindIntervalSlow * 0.5f;   // 起了这一阵之后，下一次按正常节奏
			_gusts++;
			PlayGust();
			return true;
		}

		/// <summary>
		/// 每帧喂一次。<paramref name="speed"/> = 当前真实速度（m/s），非飞行 / 悬停喂 0。
		///
		/// 🔴 **两路调用者共用同一个调度器**（2026-10-10 加第二路）：
		///   · `PlayerFlightBehavior.OnMissionTick` —— **每帧都喂**（空中 = `_velocity.Length`、坠落 = `_fallRideVel`、
		///     其余相位喂 0）；
		///   · `GrapplePull.Tick` —— 只在拉拽进行中喂（拉拽期间玩家被板带走，飞行那边仍在喂 0）。
		///
		/// 🔴 **为什么两路不会打架 / 不会双倍出风**：速度低于门槛的那一次调用**只解除"待发"标记、
		///    不动计时器**（老写法是"每帧把计时器封顶"——两路一起调就会把高速时的密集节奏压成固定值）。
		///    计时器只在"速度达标"的那一次调用里推进 ⇒ 同一帧里 0 与真速度各来一次时，只有真速度那次算数。
		/// </summary>
		public static void Tick(float speed, float dt)
		{
			// 先跟句柄（哪怕这一帧不出新阵风；关开关也照跟 —— 让已经在响的那阵自然收尾）
			FollowAnchor();

			if (!FlightTuning.WindSoundEnabled || string.IsNullOrEmpty(FlightTuning.WindSoundName))
			{
				_armed = false;
				return;
			}

			if (speed < FlightTuning.WindSpeedMin)
			{
				// 悬停 / 起降 / （飞行行为在拉拽期间喂的 0）：不出声。
				// 🔴 **只解除待发标记，不动计时器** —— 理由见方法注释（两路调用者共存的关键）。
				_armed = false;
				return;
			}

			if (!_armed)
			{
				// 刚恢复飞行（或刚开始拉拽）：计时器封顶到半间隔 ⇒ 最多半间隔就有一阵风（起飞即有反馈）。
				// 只做这一次 —— 之后交给正常节奏，高速时的密集不会被压掉。
				_armed = true;
				float armCap = Math.Max(0.05f, FlightTuning.WindIntervalSlow * 0.5f);
				if (_timer > armCap)
				{
					_timer = armCap;
				}
			}

			_timer -= dt;
			if (_timer > 0f)
			{
				return;
			}

			_lastInterval = IntervalFor(speed);
			_timer = _lastInterval;
			_gusts++;
			PlayGust();
		}

		/// <summary>播一阵风，并把句柄收进"跟着玩家走"的清单。</summary>
		private static void PlayGust()
		{
			Mission mission = Mission.Current;
			Scene scene = mission?.Scene;
			if (scene == null)
			{
				return;
			}
			SoundEvent sound = SoundFx.Play3D(FlightTuning.WindSoundName, AnchorPosition());
			if (sound == null)
			{
				return;
			}
			if (_liveScene != scene.Pointer)
			{
				_live.Clear();          // 上一场的句柄不留（已随旧场景作废）
				_liveScene = scene.Pointer;
			}
			if (_live.Count >= MaxLive)
			{
				_live.RemoveAt(0);
			}
			_live.Add(sound);
		}

		/// <summary>把还在响的阵风位置跟到玩家身上；响完的 / 旧场景的丢掉。</summary>
		private static void FollowAnchor()
		{
			if (_live.Count == 0)
			{
				return;
			}
			Scene scene = Mission.Current?.Scene;
			if (scene == null || scene.Pointer != _liveScene)
			{
				// 换场景 / 出场景：句柄随旧场景一起没了 —— **只丢引用，不碰它们**
				_live.Clear();
				return;
			}
			Vec3 at = AnchorPosition();
			for (int i = _live.Count - 1; i >= 0; i--)
			{
				try
				{
					SoundEvent sound = _live[i];
					if (sound == null || !sound.IsPlaying())
					{
						_live.RemoveAt(i);
						continue;
					}
					sound.SetPosition(at);
				}
				catch (Exception)
				{
					// 句柄失效（引擎回收）——丢掉即可，绝不外抛（调用方在 tick 上）
					_live.RemoveAt(i);
				}
			}
		}

		/// <summary>速度 → 阵风间隔（含 ±jitter 随机，防节拍感）。</summary>
		private static float IntervalFor(float speed)
		{
			float span = Math.Max(0.01f, FlightTuning.WindSpeedFast - FlightTuning.WindSpeedMin);
			float t = (speed - FlightTuning.WindSpeedMin) / span;
			if (t < 0f)
			{
				t = 0f;
			}
			else if (t > 1f)
			{
				t = 1f;
			}
			float interval = FlightTuning.WindIntervalSlow
				+ (FlightTuning.WindIntervalFast - FlightTuning.WindIntervalSlow) * t;
			float jitter = 1f + ((float)_rng.NextDouble() * 2f - 1f) * FlightTuning.WindJitter;
			return Math.Max(0.05f, interval * jitter);
		}

		/// <summary>给 `custom.flight sound` 的单行回执（🔴 纯英文，工作流约定）。</summary>
		public static string Describe()
		{
			return string.Format(
				"enabled={0} name={1} | speedMin={2} speedFast={3} | gapSlow={4} gapFast={5} jitter={6} | gusts={7} liveA={8} lastGap={9:F2}s",
				FlightTuning.WindSoundEnabled, FlightTuning.WindSoundName,
				FlightTuning.WindSpeedMin, FlightTuning.WindSpeedFast,
				FlightTuning.WindIntervalSlow, FlightTuning.WindIntervalFast, FlightTuning.WindJitter,
				_gusts, _live.Count, _lastInterval);
		}
	}
}
