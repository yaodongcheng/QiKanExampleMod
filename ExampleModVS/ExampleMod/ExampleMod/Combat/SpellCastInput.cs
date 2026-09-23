using System;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 起手轴：**"施法中"那台状态机**（阶段 3）—— 一台机器管三种施法方式，外加蓄力档位。
	///
	/// | <c>cast_type</c> | 玩家按住施法键时 | 松开时 |
	/// |---|---|---|
	/// | `normal`（有前摇） | 蓄力（手上核长起来），进度决定 <c>Power</c> | **发一发**（蓄满与不蓄满伤害明显不同） |
	/// | `channel`（按住持续） | 攒够 <c>charge_time</c> 后**开始持续投送** | **停**（投送结束） |
	/// | `instant`（瞬发） | 按下**立刻**发（无前摇） | 什么都不做 |
	///
	/// 🔴 它替代的是阶段 1 的"脚手架"（引擎弩开火事件）：起手从此走**我们自己的键 + 我们自己的相位机**，
	///   投送与结算那一层一行没动 —— 这正是计划 §5.2 把起手写成"函数调用"的意义。
	/// 🔴 动画相位（计划 §5.5 的 FCS 模型）：骑砍没有动画通知，所以**出手时刻由"按住/松开"这一拍决定**
	///   （龙之信条式的按住蓄力），数据里的 <c>release_at</c>/<c>end_at</c> 留给以后接自建动作时用。
	/// 🔴 打断：换武器 / 换法术 / 目标失效 / 施法者倒下 / 场景结束 → 一律 <see cref="Cancel"/>（核与粒子当场清掉）。
	///
	/// 玩家专属（NPC 没有输入；NPC 的起手见 <see cref="SpellNpcCaster"/>）。
	/// </summary>
	public sealed class SpellCastInput
	{
		private enum Phase
		{
			Idle,
			Charging,
			Channeling,
		}

		private Phase _phase = Phase.Idle;
		private SpellDef _spell;
		private float _heldSeconds;
		private float _power = 1f;

		/// <summary>正在持续的引导（松手时要把它结束掉）。</summary>
		private SpellShot _channelShot;

		/// <summary>手里那颗蓄力核的实体 + 粒子。</summary>
		private GameEntity _coreEntity;
		private ParticleSystem _coreParticle;

		/// <summary>上一次施法之后键是否松开过（防"按住不放 = 连发"）。</summary>
		private bool _releasedSinceCast = true;

		/// <summary>输入缓冲窗口（松手到动作结束之间按下的下一次施法要接住）。</summary>
		private float _bufferUntil;

		private const float BufferWindowSeconds = 0.35f;

		/// <summary>当前是否正在施法中（诊断/UI 用）。</summary>
		public bool IsCasting
		{
			get { return _phase != Phase.Idle; }
		}

		/// <summary>当前蓄力进度 0~1（诊断用）。</summary>
		public float Power
		{
			get { return _power; }
		}

		public void Tick(float dt)
		{
			Mission mission = Mission.Current;
			Agent player = mission != null ? mission.MainAgent : null;
			if (player == null || !AgentControlHelper.SafeIsActive(player))
			{
				Cancel();
				return;
			}

			bool held = ModInput.IsHeld(InteractionIds.SpellCast);
			if (!held)
			{
				_releasedSinceCast = true;
			}
			SpellDef wielded = ResolveWieldedSpell(player);

			switch (_phase)
			{
				case Phase.Idle:
					UpdateIdle(player, wielded, held);
					break;
				case Phase.Charging:
					UpdateCharging(player, wielded, held, dt);
					break;
				case Phase.Channeling:
					UpdateChanneling(wielded, held);
					break;
			}
		}

		/// <summary>场景卸载 / 换场景时清干净。</summary>
		public void Cancel()
		{
			EndChannel();
			ClearCore();
			_phase = Phase.Idle;
			_spell = null;
			_heldSeconds = 0f;
			_power = 1f;
		}

		// ─────────────────────────── 三态 ───────────────────────────

		private void UpdateIdle(Agent player, SpellDef wielded, bool held)
		{
			if (!held || wielded == null)
			{
				return;
			}
			bool buffered = MissionTime() <= _bufferUntil;
			if (!_releasedSinceCast && !buffered)
			{
				return;   // 按住不放 = 不连发（要松一次手才认下一发）
			}
			_spell = wielded;
			_heldSeconds = 0f;

			string castType = _spell.CastType ?? "normal";
			if (castType == "instant")
			{
				// 瞬发：抬手即出，无前摇
				_power = 1f;
				Release(player, _spell, _power);
				return;
			}
			_phase = Phase.Charging;
			UpdateCore(player, 0f);
		}

		private void UpdateCharging(Agent player, SpellDef wielded, bool held, float dt)
		{
			if (_spell == null || wielded != _spell)
			{
				// 换武器 / 换法术 → 打断（核当场灭掉）
				Cancel();
				return;
			}
			_heldSeconds += dt;
			float chargeTime = _spell.ChargeTime;
			_power = chargeTime > 0f ? MathF.Min(1f, _heldSeconds / chargeTime) : 1f;
			UpdateCore(player, _power);

			string castType = _spell.CastType ?? "normal";
			if (castType == "channel")
			{
				// 引导：攒够前摇就开始"一直放"，之后由按住维持
				if (chargeTime <= 0f || _heldSeconds >= chargeTime)
				{
					if (BeginChannel(player, _spell, _power))
					{
						ClearCore();
						_phase = Phase.Channeling;
					}
				}
				return;
			}

			if (!held)
			{
				Release(player, _spell, _power);
			}
		}

		private void UpdateChanneling(SpellDef wielded, bool held)
		{
			if (!held || _spell == null || wielded != _spell)
			{
				Cancel();   // 松手 / 换法术 → 停
			}
		}

		// ─────────────────────────── 发 / 停 ───────────────────────────

		private void Release(Agent player, SpellDef spell, float power)
		{
			ClearCore();
			_phase = Phase.Idle;
			_spell = null;
			_releasedSinceCast = false;
			_bufferUntil = MissionTime() + BufferWindowSeconds;

			Vec3 origin = player.Position;
			origin.z += player.GetEyeGlobalHeight();
			Vec3 direction = player.LookDirection;
			SpellCastFlow.Cast(player, spell, origin, direction, power);
			_power = 1f;
			_heldSeconds = 0f;
		}

		private bool BeginChannel(Agent player, SpellDef spell, float power)
		{
			Vec3 origin = player.Position;
			origin.z += player.GetEyeGlobalHeight();
			Vec3 direction = player.LookDirection;
			_channelShot = SpellCastFlow.CastAndReturn(player, spell, origin, direction, power);
			if (_channelShot == null)
			{
				Cancel();
				return false;
			}
			DebugLogger.Log($"[Spell] 开始引导 '{spell.Id}'（Power={power:F2}）");
			return true;
		}

		private void EndChannel()
		{
			if (_channelShot == null)
			{
				return;
			}
			SpellProjectileLogic host = Mission.Current != null
				? Mission.Current.GetMissionBehavior<SpellProjectileLogic>()
				: null;
			if (host != null)
			{
				host.RetireShot(_channelShot);
			}
			_channelShot = null;
		}

		// ─────────────────────────── 蓄力核的视觉 ───────────────────────────

		/// <summary>
		/// 手心的蓄力核（数据：<c>charge_mesh</c> + <c>charge_particle</c>）。
		/// ⚠️ 位置是**近似的手部位置**（身前偏上），不是真挂在骨骼上 —— 阶段 3 的"法阵 prefab"接上之后
		///   换成 prefab 挂点即可（数据字段不用改）。
		/// </summary>
		private void UpdateCore(Agent player, float power)
		{
			if (_spell == null || string.IsNullOrEmpty(_spell.ChargeMesh))
			{
				return;
			}
			Vec3 look = player.LookDirection;
			look = look.LengthSquared < 1e-8f ? Vec3.Forward : look.NormalizedCopy();
			Vec3 at = player.Position;
			at.z += 1.25f;
			at += look * 0.55f;

			float scale = 0.5f + power;   // 核 ⌀0.72 m：半亮到满亮长一倍
			if (_coreEntity == null)
			{
				_coreEntity = SpellWorld.SpawnMeshEntity(_spell.ChargeMesh, at, Mat3.Identity, scale);
				if (_coreEntity == null)
				{
					return;
				}
				_coreParticle = SpellWorld.AttachParticle(_spell.ChargeParticle, _coreEntity);
				return;
			}
			try
			{
				Mat3 rotation = Mat3.Identity;
				rotation.ApplyScaleLocal(scale);
				_coreEntity.SetGlobalFrame(new MatrixFrame(rotation, at));
			}
			catch (Exception)
			{
				_coreEntity = null;
			}
		}

		private void ClearCore()
		{
			if (_coreEntity == null)
			{
				return;
			}
			try
			{
				if (_coreParticle != null)
				{
					_coreEntity.RemoveComponent(_coreParticle);
				}
				_coreEntity.Remove(0);
			}
			catch (Exception)
			{
				// 实体可能已被引擎回收 —— 正常
			}
			_coreEntity = null;
			_coreParticle = null;
		}

		/// <summary>取"手上这一发打的是哪个法术" —— 与法印补丁、落点指示圈同一套口径（弹药 = 法术）。</summary>
		private static SpellDef ResolveWieldedSpell(Agent agent)
		{
			MissionWeapon wielded = agent.WieldedWeapon;
			if (wielded.IsEqualTo(MissionWeapon.Invalid) || wielded.Item == null)
			{
				return null;
			}
			WeaponComponentData usage = wielded.CurrentUsageItem;
			if (usage == null)
			{
				return null;
			}
			ItemObject ammo = usage.IsRangedWeapon && usage.IsConsumable ? wielded.Item : wielded.AmmoWeapon.Item;
			return ammo != null ? SpellRegistry.FindByAmmo(ammo.StringId) : null;
		}

		private static float MissionTime()
		{
			return MBCommon.GetTotalMissionTime();
		}
	}
}
