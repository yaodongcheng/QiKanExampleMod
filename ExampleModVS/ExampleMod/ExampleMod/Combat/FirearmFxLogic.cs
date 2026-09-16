using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 火器开火表现 —— 枪声（按听者距离分三档）+ 枪口烟。**战场表现层，不属玩法逻辑**。
	///
	/// 判定口径（唯一）：射手手里的武器 <c>PrimaryWeapon.AmmoClass</c> 能在
	/// <see cref="FirearmFxRegistry"/> 的表里查到 → 是火器，播表现；查不到 → 直接返回。
	/// 所以弓弩开火（Bolt / Arrow）不受任何影响 —— 它们不在表里。
	/// 🔴 内容包若要新增火器口径，只加一行 &lt;Profile&gt;（AssetRegistry/FirearmFx.xml），本类不动。
	///
	/// 距离分档：听者 = 玩家（<c>Mission.MainAgent</c>），玩家不在场时退化为相机位置。
	///   distSq &lt; nearDist²  → 近档；distSq ≥ farDist² → 远档；中间 → 中档。
	///
	/// 容错（铁律 1）：音效名/粒子名查不到（内容包没装、名字拼错、资源缺失）→ 跳过该项，
	///   只记一次日志；回调整体包 try/catch，异常绝不上抛到引擎（引擎的回调链无异常保护）。
	///
	/// ⚠️ 挂载位置：**必须挂在 <c>Settings.IsInteractionDisabled()</c> 闸门之前**（照
	///   NavMeshDebugMissionView 的做派）—— 火器音效正是要在战场/攻城里响，被玩法闸门拦掉就没意义了。
	///   无火器在场时每发子弹只做一次「查表未命中」判定，零开销。
	/// </summary>
	public class FirearmFxLogic : MissionLogic
	{
		/// <summary>音效名 → 引擎事件 id 的缓存（宁可不播也不重复查）。</summary>
		private readonly Dictionary<string, int> _soundIds = new Dictionary<string, int>(StringComparer.Ordinal);

		/// <summary>粒子名 → 引擎运行时 id 的缓存。</summary>
		private readonly Dictionary<string, int> _particleIds = new Dictionary<string, int>(StringComparer.Ordinal);

		/// <summary>本帧生成的枪口粒子实体（下一帧清理，照织丰做派）。</summary>
		private readonly List<GameEntity> _particles = new List<GameEntity>();

		/// <summary>已记过日志的键（防刷屏）。</summary>
		private readonly HashSet<string> _logged = new HashSet<string>(StringComparer.Ordinal);

		public override void OnAgentShootMissile(Agent shooterAgent, EquipmentIndex weaponIndex, Vec3 position,
			Vec3 velocity, Mat3 orientation, bool hasRigidBody, int forcedMissileIndex)
		{
			base.OnAgentShootMissile(shooterAgent, weaponIndex, position, velocity, orientation, hasRigidBody, forcedMissileIndex);
			try
			{
				if (shooterAgent == null || Mission == null || Mission.Scene == null)
				{
					return;
				}
				MissionWeapon weapon = shooterAgent.WieldedWeapon;
				if (weapon.IsEqualTo(MissionWeapon.Invalid))
				{
					return;
				}
				ItemObject item = weapon.Item;
				if (item == null)
				{
					return;
				}
				WeaponComponentData wcd = item.PrimaryWeapon;
				if (wcd == null)
				{
					return;
				}

				// 🔴 火器的唯一判据：口径在表里。不在 = 弓弩/投掷物 → 静默返回。
				FirearmFxRegistry.Profile profile = FirearmFxRegistry.Find(wcd.AmmoClass);
				if (profile == null)
				{
					return;
				}

				PlayFireSound(profile, shooterAgent);
				SpawnMuzzleParticle(profile, position, orientation);
			}
			catch (Exception ex)
			{
				LogOnce("开火回调异常", ex);
			}
		}

		public override void OnMissionTick(float dt)
		{
			base.OnMissionTick(dt);
			if (_particles.Count == 0)
			{
				return;
			}
			for (int i = 0; i < _particles.Count; i++)
			{
				try
				{
					if (_particles[i] != null)
					{
						_particles[i].Remove(0);
					}
				}
				catch (Exception)
				{
					// 实体可能已被引擎回收 —— 忽略即可
				}
			}
			_particles.Clear();
		}

		/// <summary>按听者距离选档并播放。名字为空 / 查不到 → 不播。</summary>
		private void PlayFireSound(FirearmFxRegistry.Profile profile, Agent shooterAgent)
		{
			Vec3 listenerPos = Mission.MainAgent != null
				? Mission.MainAgent.Position
				: Mission.GetCameraFrame().origin;
			float distSq = shooterAgent.Position.DistanceSquared(listenerPos);
			float farSq = profile.FarDist * profile.FarDist;
			float nearSq = profile.NearDist * profile.NearDist;

			string soundName = distSq >= farSq ? profile.FarSound
				: distSq >= nearSq ? profile.MidSound
				: profile.NearSound;
			if (string.IsNullOrEmpty(soundName))
			{
				return;
			}

			int soundId = ResolveSoundId(soundName);
			if (soundId < 0)
			{
				return;
			}
			SoundEvent sound = SoundEvent.CreateEvent(soundId, Mission.Scene);
			if (sound == null)
			{
				return;
			}
			sound.SetPosition(shooterAgent.Position);
			sound.Play();
		}

		/// <summary>在弹丸出膛位置生成枪口烟。<c>position</c> / <c>orientation</c> 由引擎给出（即枪口与射向）。</summary>
		private void SpawnMuzzleParticle(FirearmFxRegistry.Profile profile, Vec3 position, Mat3 orientation)
		{
			if (string.IsNullOrEmpty(profile.Particle))
			{
				return;
			}
			int particleId = ResolveParticleId(profile.Particle);
			if (particleId < 0)
			{
				return;
			}
			GameEntity entity = GameEntity.CreateEmpty(Mission.Scene, true);
			if (entity == null)
			{
				return;
			}
			MatrixFrame frame = new MatrixFrame(orientation, position);
			entity.SetGlobalFrame(frame);
			MatrixFrame localFrame = MatrixFrame.Identity;
			ParticleSystem.CreateParticleSystemAttachedToEntity(particleId, entity, ref localFrame);
			_particles.Add(entity);
		}

		private int ResolveSoundId(string name)
		{
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
				DebugLogger.Log($"[FirearmFx] 音效 '{name}' 查不到 —— 该档静音（检查内容包 module_sounds.xml）");
			}
			return id;
		}

		private int ResolveParticleId(string name)
		{
			int id;
			if (_particleIds.TryGetValue(name, out id))
			{
				return id;
			}
			try
			{
				id = ParticleSystemManager.GetRuntimeIdByName(name);
			}
			catch (Exception ex)
			{
				id = -1;
				LogOnce("粒子名解析异常 " + name, ex);
			}
			_particleIds[name] = id;
			if (id < 0)
			{
				DebugLogger.Log($"[FirearmFx] 粒子 '{name}' 查不到 —— 该火器不放烟（检查内容包粒子资产）");
			}
			return id;
		}

		private void LogOnce(string key, Exception ex)
		{
			if (!_logged.Add(key))
			{
				return;
			}
			DebugLogger.Log($"[FirearmFx] {key}：{ex.GetType().Name} {ex.Message}");
		}
	}
}
