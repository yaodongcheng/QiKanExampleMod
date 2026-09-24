using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using LivingWorldNpcs.Flight;

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

		/// <summary>
		/// 本次施法用的是**飞行手势**（右键蓄力 / 左键放 / 松右键取消）吗。
		/// 🔴 **起手那一刻定死**、放出或取消时才清 —— 半路起飞/落地不会把同一发弄成两种收势。
		/// </summary>
		private bool _flightGesture;

		// ─────────────────── 施法手势动画（🔴 播在**通道 1 = 上身层**）───────────────────
		//
		// 目标 = **只动上半身**（腿保持飞行姿势）。做法：手势走通道 1，飞行姿势留在通道 0；
		// 🔴 而**通道 1 一动会把通道 0 的动作挤掉** ⇒ 飞行侧每帧守通道 0、空了就补回来
		//    （`PlayerFlightBehavior` 第 ⑦′ 条 + `AgentAnimStateMachine.Reassert`）。
		// 🔴 通道 1 之前"收下了不播"的根因是 **clip 元数据**（Priority / Right hand pose / Blend out period /
		//    Flags.allow_head_movement），已在编辑器修好并发布（计划 §A4）—— 不是引擎不认我们的动作。
		// 回退：`FlightTuning.CastOnUpperChannel = false` ⇒ 改由飞行状态机走通道 0 播全身施法姿势。

		/// <summary>蓄力循环动作名（与内容包 `action_types.xml` 的声明一致）。</summary>
		private const string ActCastCharge = "act_cast_charge";

		/// <summary>释放动作名（同上）。</summary>
		private const string ActCastProjectile = "act_cast_projectile";

		private const float CastAnimBlendIn = 0.12f;    // 进手势的淡入（短一点，按键要"立刻有反应"）
		private const float CastAnimBlendOut = 0.25f;   // 收回通道 1 的淡出

		private enum CastAnim
		{
			None,
			Charging,    // 蓄力循环在播
			Releasing,   // 释放动作在播（播完自动收）
		}

		private CastAnim _castAnim = CastAnim.None;

		/// <summary>已报过警的动作名（防每帧刷屏）。</summary>
		private static readonly HashSet<string> _animWarned = new HashSet<string>(StringComparer.Ordinal);

		/// <summary>蓄力手势（循环）：进蓄力那一刻播一次，之后靠 clip 自带的 cyclic 转。</summary>
		private void PlayChargeAnim(Agent player)
		{
			if (_castAnim == CastAnim.Charging)
			{
				return;
			}
			if (SetChannelOne(player, ActCastCharge, cyclic: false))
			{
				_castAnim = CastAnim.Charging;
			}
		}

		/// <summary>释放手势（一次性）：放出去那一拍播；播完由 <see cref="TickCastAnim"/> 收回通道 1。</summary>
		private void PlayReleaseAnim(Agent player)
		{
			if (SetChannelOne(player, ActCastProjectile, cyclic: false))
			{
				_castAnim = CastAnim.Releasing;
			}
		}

		/// <summary>
		/// 每帧收势：① 释放动作播完 ⇒ 收回通道 1 ② 蓄力中但相位机已回 Idle（被取消）⇒ 同样收回。
		/// 收回 = 通道 1 置 `act_none` ⇒ 上身交还给飞行姿势。
		/// </summary>
		private void TickCastAnim(Agent player)
		{
			if (_castAnim == CastAnim.None)
			{
				return;
			}
			bool done = false;
			if (_castAnim == CastAnim.Releasing)
			{
				try { done = player.GetCurrentActionProgress(1) >= 0.98f; }
				catch (Exception) { done = true; }
			}
			else if (_phase == Phase.Idle)
			{
				done = true;
			}
			if (done)
			{
				ClearCastAnim(player);
			}
		}

		/// <summary>把动作放到通道 1。返回 false = 没注册 / 播失败（静默，不抛）。</summary>
		private static bool SetChannelOne(Agent player, string actionId, bool cyclic)
		{
			try
			{
				ActionIndexCache idx = ActionIndexCache.Create(actionId);
				if (idx == ActionIndexCache.act_none)
				{
					if (_animWarned.Add(actionId))
					{
						DebugLogger.Log($"[Spell] 手势动作 '{actionId}' 没注册（act_none）—— 施法照常，只是没有上手姿势");
					}
					return false;
				}
				float duration = 0f;
				try { duration = MBActionSet.GetActionAnimationDuration(player.ActionSet, idx); }
				catch (Exception) { }
				if (duration <= 0f && _animWarned.Add(actionId + ":dur"))
				{
					DebugLogger.Log($"[Spell] 手势动作 '{actionId}' 时长 0.00s —— 注册了但 clip 解析不到");
				}
				player.SetActionChannel(1, idx, ignorePriority: true,
					additionalFlags: cyclic ? (ulong)AnimFlags.anf_cyclic : 0UL,
					blendInPeriod: CastAnimBlendIn,
					blendOutPeriodToNoAnim: 0f);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>收回通道 1（幂等）：上身交还给飞行姿势。</summary>
		private void ClearCastAnim(Agent player)
		{
			if (_castAnim == CastAnim.None)
			{
				return;
			}
			_castAnim = CastAnim.None;
			try
			{
				player.SetActionChannel(1, ActionIndexCache.act_none, ignorePriority: true,
					additionalFlags: 0UL, blendInPeriod: 0f,
					blendOutPeriodToNoAnim: CastAnimBlendOut);
			}
			catch (Exception)
			{
				// agent 没了 —— 通道本来就没人播了
			}
		}

		// ─────────────────────────── 出手时机：法术等动作的"出手帧" ───────────────────────────
		//
		// 🔴 释放动作 `ProjectileSpell` 有 **0.83 秒前摇**（出手帧 36% × 2.30 s）——
		//    点左键立刻飞出去 = 人还在抬手、月牙已经撞墙上了。
		//    数据字段 `release_at`（0~1，clip 的进度）就是为这一刻留的：延迟 = `release_at × 释放动作时长`。
		//    · `release_at = 0`（当前数据值）⇒ **即时出手**（现在的行为，最跟手）
		//    · `release_at = 0.36` ⇒ 等到抬手动作做完那一拍才飞出去（最像"投出去"，代价是 0.83 s 延迟）
		//    运行时试：`custom.spell lead <秒>`（0 = 即时；不用重启、不用改数据）。

		/// <summary>释放动作 `act_cast_projectile` 的时长（秒）—— 换素材时同步改这里（`MagicIdle` 是 1.80）。</summary>
		private const float ReleaseClipSeconds = 2.33f;   // ≈ `FlightTuning.CastReleaseSeconds`（同一颗 clip，改一个别忘另一个）

		private bool _hasPendingRelease;
		private float _pendingReleaseTimer;
		private SpellDef _pendingSpell;
		private float _pendingPower = 1f;

		/// <summary>延迟多少秒才真正放出去（数据 `release_at` × 释放动作时长；运行时旋钮优先）。</summary>
		private static float ReleaseLeadSeconds(SpellDef spell)
		{
			if (SpellDebug.ReleaseLeadOverride.HasValue)
			{
				return MathF.Max(0f, SpellDebug.ReleaseLeadOverride.Value);
			}
			float frac = spell != null ? spell.ReleaseAt : 0f;
			return frac > 0f ? frac * ReleaseClipSeconds : 0f;
		}

		/// <summary>挂起这一发（等动作走到出手帧再放）。</summary>
		private void QueueRelease(SpellDef spell, float power, float lead)
		{
			_hasPendingRelease = true;
			_pendingReleaseTimer = lead;
			_pendingSpell = spell;
			_pendingPower = power;
		}

		/// <summary>到点就放；玩家没了 / 落地了就把这一发丢掉（不补发）。</summary>
		private void TickPendingRelease(Agent player, float dt)
		{
			if (!_hasPendingRelease)
			{
				return;
			}
			_pendingReleaseTimer -= dt;
			if (_pendingReleaseTimer > 0f)
			{
				return;
			}
			SpellDef spell = _pendingSpell;
			float power = _pendingPower;
			_hasPendingRelease = false;
			_pendingSpell = null;
			if (spell == null)
			{
				return;
			}
			CastNow(player, spell, power);
		}

		/// <summary>真正把法术放出去（起点与方向都在**这一刻**算 —— 抬手的 0.8 秒里镜头可能已经转了）。</summary>
		private void CastNow(Agent player, SpellDef spell, float power)
		{
			Vec3 origin = player.Position;
			origin.z += player.GetEyeGlobalHeight();
			SpellCastFlow.Cast(player, spell, origin, SpellWorld.CastDirection(player), power);
		}

		/// <summary>输入缓冲窗口（松手到动作结束之间按下的下一次施法要接住）。</summary>
		private float _bufferUntil;

		private const float BufferWindowSeconds = 0.35f;

		/// <summary>当前是否正在施法中（诊断/UI 用）。</summary>
		public bool IsCasting
		{
			get { return _phase != Phase.Idle; }
		}

		/// <summary>
		/// 本场景的玩家施法输入机（没挂 = null）。给**别的系统**查询状态用 —— 目前一个消费者：
		/// 飞行（施法中要把身体转向相机，见 <see cref="IsPlayerAiming"/>）。
		/// 🔴 每个 <c>Tick</c> 开头重设一次（幂等），宿主回收时清掉。
		/// </summary>
		public static SpellCastInput Current { get; private set; }

		/// <summary>宿主场景回收时调用：把"对外可见的当前施法机"摘掉（幂等；不清的话场景结束后别人还读得到）。</summary>
		public static void ClearCurrent(SpellCastInput owner)
		{
			if (Current == owner)
			{
				Current = null;
			}
		}

		/// <summary>
		/// 玩家**正在瞄准施法**吗（蓄力中 / 引导中）。
		/// 用途：飞行中施法时，飞行系统据此把**身体朝相机方向**转（"看相机无限远处"），
		/// 而不是朝移动方向 —— 见 <c>Flight/PlayerFlightBehavior</c> 第 ⑧ 条机身朝向那段。
		/// 松手 / 取消 / 放完 ⇒ 自动回 false，身体立刻回到原来的规则。
		/// </summary>
		public static bool IsPlayerAiming
		{
			get
			{
				SpellCastInput cur = Current;
				return cur != null && cur._phase != Phase.Idle;
			}
		}

		/// <summary>当前蓄力进度 0~1（诊断用）。</summary>
		public float Power
		{
			get { return _power; }
		}

		public void Tick(float dt)
		{
			Current = this;                       // 给别的系统查（飞行：施法时把身体转向相机）
			Mission mission = Mission.Current;
			Agent player = mission != null ? mission.MainAgent : null;
			if (player == null || !AgentControlHelper.SafeIsActive(player))
			{
				Cancel();
				return;
			}

			// 🔴 **本阶段只在空中施法**（2026-09-24 用户裁定）：地面有常规攻击模式，
			//    施法手势与施法动作会跟它打架，所以地面的蓄力手势**暂时停用**（`X` 键不再起手）——
			//    地面怎么兼容以后再看。落地 = 当场取消（含"飞着蓄力一半落地"，免得球挂在手上不放）。
			//    ⚠️ 不受影响的两条：① `custom.spell cast`（控制台，测试用）② 法印开火拦截那条路
			//       （引擎开火 → SpellSealFirePatch 改发我们的实体，它不需要手势、也不加动作）。
			TickCastAnim(player);              // 收势：释放播完 / 蓄力被取消 ⇒ 收回通道 1
			TickPendingRelease(player, dt);    // 出手帧到了 ⇒ 把挂起的那一发真放出去
			if (!IsFlyingNow())
			{
				// 落地/退出飞行：挂起的这一发**丢掉不补发**（人都落地了，月牙才飞出来最出戏）
				_hasPendingRelease = false;
				_pendingSpell = null;
				Cancel();
				return;
			}

			// 🔴 **空中手势**（2026-09-24 用户裁定）：**按住右键蓄力**（球长大、粒子变浓）→
			//    **点左键放**（朝相机中心）；**松右键 = 取消**（不放）。
			//    右键同时还是"瞄准机位"，一举两得。地面那套（X 蓄力、松手放）本阶段停用，见上。
			//    ⚠️ `_flightGesture` 仍然记着"这一发是空中起的"——半路落地时收势才不会走错分支。
			bool held = FlightInput.AimHeld;
			if (!held)
			{
				_releasedSinceCast = true;
			}
			// 左键"发射"**只在蓄力/引导期间消费**（在 Idle 里消费 = 白吞一次点击）
			bool fire = _phase != Phase.Idle && FlightInput.ConsumeFirePress();
			SpellDef wielded = ResolveFlightSpell(player);

			switch (_phase)
			{
				case Phase.Idle:
					UpdateIdle(player, wielded, held, true);
					break;
				case Phase.Charging:
					UpdateCharging(player, wielded, held, dt, fire);
					break;
				case Phase.Channeling:
					UpdateChanneling(wielded, held);
					break;
			}
		}

		/// <summary>玩家现在在飞吗（飞行系统接管中）。飞中：**不要求装备**、方向取飞行相机中心。</summary>
		private static bool IsFlyingNow()
		{
			PlayerFlightBehavior flight = PlayerFlightBehavior.Current;
			return flight != null && flight.IsFlying;
		}

		/// <summary>
		/// 飞行中"这一发放什么"：手里认得出法术就用它，认不出（飞行中允许空手）→ 用飞行默认法术
		/// （表里第一条 projectile 族，见 <see cref="SpellRegistry.DefaultFlightSpell"/>）。
		/// </summary>
		private static SpellDef ResolveFlightSpell(Agent player)
		{
			return SpellWorld.ResolveWieldedSpell(player) ?? SpellRegistry.DefaultFlightSpell;
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
			_flightGesture = false;
		}

		// ─────────────────────────── 三态 ───────────────────────────

		private void UpdateIdle(Agent player, SpellDef wielded, bool held, bool flying)
		{
			if (!held || wielded == null)
			{
				return;
			}
			if (_hasPendingRelease)
			{
				return;   // 上一发还在等出手帧 —— 先让它走完，别叠第二发
			}
			bool buffered = MissionTime() <= _bufferUntil;
			if (!_releasedSinceCast && !buffered)
			{
				return;   // 按住不放 = 不连发（要松一次手才认下一发）
			}
			_flightGesture = flying;      // 本次施法的手势，一路沿用到放出 / 取消
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
			if (_flightGesture && FlightTuning.CastOnUpperChannel)
			{
				PlayChargeAnim(player);      // 手势（通道 1 · 循环）
			}
		}

		private void UpdateCharging(Agent player, SpellDef wielded, bool held, float dt, bool fire)
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

			if (_flightGesture)
			{
				// 飞行手势：**左键 = 放**（按当前蓄力，不必等满——满蓄力只是伤害最高档）；**松右键 = 取消**（不放）
				if (fire)
				{
					Release(player, _spell, _power);
					return;
				}
				if (!held)
				{
					Cancel();
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
			// 地面手势：要松一次手才认下一发（防按住连发）。飞行手势：**右脚本就是蓄力键**，
			// 放完后还按着就是要接着蓄下一发（发射另有"左键点一下"把关，不会连发）⇒ 直接放行。
			_releasedSinceCast = !_flightGesture;
			_bufferUntil = MissionTime() + BufferWindowSeconds;

			// 🔴 法术**什么时候真飞出去**：数据 `release_at`（0~1，释放动作的进度）× 动作时长。
			//    0 = 点键即出（跟手）；0.36 = 等动作抬到手才出（像真的"投出去"，代价 0.83 s 延迟）。
			if (_flightGesture && FlightTuning.CastOnUpperChannel)
			{
				PlayReleaseAnim(player);     // 手势（通道 1 · 一次性）
			}
			float lead = _flightGesture ? ReleaseLeadSeconds(spell) : 0f;
			if (lead > 0.01f)
			{
				QueueRelease(spell, power, lead);      // 抬手那段走完再放（起点与方向到那时才算）
			}
			else
			{
				CastNow(player, spell, power);
			}
			_power = 1f;
			_heldSeconds = 0f;
		}

		private bool BeginChannel(Agent player, SpellDef spell, float power)
		{
			Vec3 origin = player.Position;
			origin.z += player.GetEyeGlobalHeight();
			_channelShot = SpellCastFlow.CastAndReturn(player, spell, origin,
				SpellWorld.CastDirection(player), power);
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
			Vec3 at = CoreAnchor(player);

			// 核的大小：数据 `charge_scale` = **满蓄力时**的放大倍率（0.375 ⇒ 满蓄力 ⌀0.27 m，2026-09-24 由原值 1/4 定下）。
			// 🔴 2026-09-24 用户裁定：**从 0 开始长**（"蓄力过程比较直观"）—— 起手那一刻几乎什么都没有，
			//    随着蓄力匀速长大，**蓄满就不再变**（`power` 会钳在 1）。钳一个极小值是为了别产生零缩放矩阵。
			float full = SpellDebug.ChargeScaleOverride ?? _spell.ChargeScale;
			float scale = MathF.Max(0.01f, full * power);
			if (_coreEntity == null)
			{
				_coreEntity = SpellWorld.SpawnMeshEntity(_spell.ChargeMesh, at, Mat3.Identity, scale);
				if (_coreEntity == null)
				{
					return;
				}
				_coreParticle = SpellWorld.AttachParticle(_spell.ChargeParticle, _coreEntity);
			}
			else
			{
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

			// 🔴 特效随蓄力**变浓**（2026-09-24 用户要求"球变大且特效变浓"）：
			//    同一颗粒子不重建，只调**发射率倍数**（`SetRuntimeEmissionRateMultiplier`，引擎为此专门开的接口）。
			if (_coreParticle != null)
			{
				try
				{
					_coreParticle.SetRuntimeEmissionRateMultiplier(ChargeDensityAt(power));
				}
				catch (Exception)
				{
					// 粒子可能已被引擎回收 —— 下次进 UpdateCore 会重新挂
					_coreParticle = null;
				}
			}
		}

		/// <summary>
		/// 蓄力粒子的浓淡：起手近乎没有（0.08 倍）→ 满蓄力浓（1.5 倍）。
		/// 🔴 与球的"从 0 长起"配套（2026-09-24 用户要求蓄力过程直观）—— 别让火先于球出现。
		/// </summary>
		private static float ChargeDensityAt(float power)
		{
			return 0.08f + 1.42f * MathF.Max(0f, MathF.Min(1f, power));
		}

		/// <summary>
		/// 右手骨的**世界位置**（蓄力球挂点）——骨索引 = `Monster.MainHandBoneIndex`（主手骨 = 右手，武器挂的就是它）。
		/// 读法 = `AgentVisuals.GetBoneEntitialFrame(bone, useBoneMapping: false)` —— 与 `FlySpike.cs:1962` 读 pelvis/rider 骨同一套
		/// （entitial = **当前动画帧**的世界帧，不是绑定姿势 ⇒ 手怎么动球怎么动）。
		/// 取不到（骨架没建 / 索引为负 / 异常）→ 返回 false，调用方退回"身体坐标近似右手位"。
		/// 诊断：游戏内 <c>custom.spell hand</c> 会把这里用到的全部数字打出来。
		/// </summary>
		public static bool TryGetRightHandAnchor(Agent agent, out Vec3 anchor)
		{
			anchor = Vec3.Zero;
			try
			{
				if (agent == null || agent.Monster == null)
				{
					return false;
				}
				sbyte bone = agent.Monster.MainHandBoneIndex;
				if (bone < 0)
				{
					return false;
				}
				MBAgentVisuals visuals = agent.AgentVisuals;
				if (visuals == null || !visuals.IsValid())
				{
					return false;
				}
				Vec3 hand = visuals.GetBoneEntitialFrame(bone, useBoneMapping: false).origin;
				if (hand.LengthSquared < 1e-6f)
				{
					return false;
				}
				// 🔴 **离角色太远 = 这个骨帧不可信**（空间不对 / 索引指向了别的东西）——宁可退回近似位，
				//    也别把球丢到地图另一头（那在实机上就是"球看不见"）。3 m 远超过手臂长度了。
				float dx = hand.x - agent.Position.x, dy = hand.y - agent.Position.y, dz = hand.z - agent.Position.z;
				if (dx * dx + dy * dy + dz * dz > 9f)
				{
					return false;
				}
				anchor = hand + Vec3.Up * HandAnchorUpOffset;
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>蓄力球挂在右手骨**上方**多少米（真挂手骨时用；观感不合就调这个数）。</summary>
		public const float HandAnchorUpOffset = 0.18f;

		/// <summary>
		/// 蓄力球挂在哪。
		/// · **地面**：身前近似手位（阶段 3 起就是这样；等"法阵 prefab"接上再换真挂点）
		/// · **飞行中**：**右手骨上方**（2026-09-24 用户要求"把右手骨骼的位置查清楚"）——
		///   先问骨骼（<see cref="TryGetRightHandAnchor"/>），取不到才退回"身体坐标 + 右偏 + 上抬"的近似位
		/// </summary>
		private Vec3 CoreAnchor(Agent player)
		{
			Vec3 look = player.LookDirection;
			look = look.LengthSquared < 1e-8f ? Vec3.Forward : look.NormalizedCopy();

			Vec3 at = player.Position;
			if (_flightGesture)
			{
				if (TryGetRightHandAnchor(player, out Vec3 hand))
				{
					return hand;
				}
				// 兜底：右 = 身体朝向绕 Up 转 90°（与飞行相机同一套角约定：`Mat3.Identity` 绕 Up 转 yaw，`.s` 就是右）
				Mat3 m = Mat3.Identity;
				m.RotateAboutUp(look.RotationZ);
				Vec3 right = m.s.LengthSquared < 1e-6f ? Vec3.Zero : m.s.NormalizedCopy();
				at.z += player.GetEyeGlobalHeight() * 0.9f;   // 手的高度（≈眼高九成）往上一点
				at += right * 0.32f;                          // 偏到右手侧
				at += look * 0.28f;                           // 稍往前，别嵌进身体
				return at;
			}

			at.z += 1.25f;
			at += look * 0.55f;
			return at;
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
