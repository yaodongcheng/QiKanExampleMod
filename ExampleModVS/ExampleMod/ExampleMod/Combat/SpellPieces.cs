using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	// ═══════════════════════════════════════════════════════════════════════════
	// 法术的四段积木：三张接口 + 三张注册表 + 阶段 1 的三个实现
	//
	// 为什么要切成四段：DOS2 的 15 个法术形态、虚幻引擎的投射物（Actor + 移动组件 + 碰撞 + 命中效果），
	//   本质都是同一批字段的排列组合。切成四段之后，**加一个新法术形态 = 在某一段加一个实现**，
	//   其余三段原地不动。逐条对照见 plans/法术体系-通用施法框架.md §2.2 / §3.4。
	//
	// 🔴 三条契约（现在写对是免费的，阶段 2 才发现就是三轴同时返工 —— 计划 §3.2）：
	//   ① 瞄准的产出是**一张"施法意图"表**（阶段 1 恒 1 条），不是"一个方向"
	//   ② 投送是**一个有生命周期的对象**（Begin / Tick / End 三拍），不是"造一个飞出去的实体"
	//   ③ 结算只认"收到一次命中"；**多久上报一次归投送管**（投送节流，结算永远来一次算一次）
	//
	// 与具体世界观无关（铁律 3）：本文件里没有任何法术名、没有任何内容包词汇 —— 认的全是数据里的 id。
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// 一行"施法意图" —— 瞄准轴的产出（契约 1）。
	/// 阶段 1 恒 1 条；阶段 2 的软锁（锁 N 个目标）会产出 N 条，投送与结算一行不用改。
	/// </summary>
	public sealed class SpellCastIntent
	{
		/// <summary>施法者（可为 null 的场景理论上不存在，但一律按可空写）。</summary>
		public Agent Caster;

		/// <summary>这一发用哪个法术的数据。</summary>
		public SpellDef Spell;

		/// <summary>序号（多发时 0..N-1；装备散布/命名/日志用）。</summary>
		public int Index;

		/// <summary>起点（玩家壳给的是引擎出膛点）。</summary>
		public Vec3 Origin;

		/// <summary>朝向（单位向量）。</summary>
		public Vec3 Direction;

		/// <summary>
		/// 🔴 **瞄准点 / 落点** —— 这一发"要打的那个地方"（世界坐标）。
		/// 天降（从落点上方砸下来）与放置族（留在落点）都靠它；阶段 1 的直线飞不读它。
		/// </summary>
		public Vec3 AimPoint;

		/// <summary>
		/// 🔴 初速度向量（含重力下的抛物线解算）—— 瞄准轴必须给出"发射解"而不是"方向"：
		///   有重力时要打中一个点，必须解抛物线（UE 那套系统同样用 `CalculateSpellVelocity` 解）。
		/// </summary>
		public Vec3 Velocity;

		/// <summary>
		/// 可选的目标引用（软锁/追踪用，阶段 2）。
		/// 🔴 有关键用途：追踪弹与"选定目标天降"要的是**目标现在所在的位置**，
		///   而不是施法那一刻目标在的位置 —— 有了引用就能在落地那一瞬重新取。
		/// </summary>
		public Agent Target;

		/// <summary>
		/// **这一发的力度 0~1**（阶段 3 的蓄力档位）：瞄准轴按"按住多久"算出来，投送带着它走，
		/// 结算按 <c>damage × (1 + charge_bonus × Power)</c> 放大。
		/// 非蓄力法术恒 1（= 满力）。**这是"起手改效果"的唯一通道**，不需要动投送与结算的代码。
		/// </summary>
		public float Power = 1f;
	}

	/// <summary>一次命中的描述（结算轴收到的就是这个；<see cref="Victim"/> 为 null = 打到墙/地）。</summary>
	public sealed class SpellHit
	{
		/// <summary>被打到的人；null = 打到世界（墙、地、物件）。</summary>
		public Agent Victim;

		/// <summary>命中点（世界坐标）—— 半径伤害以它为圆心。</summary>
		public Vec3 Position;

		/// <summary>命中时的飞行方向（单位向量）。</summary>
		public Vec3 Direction;

		/// <summary>施法者。</summary>
		public Agent Caster;

		/// <summary>这一发用的法术数据。</summary>
		public SpellDef Spell;

		/// <summary>这一发的力度 0~1（蓄力档位；非蓄力恒 1）—— 结算按它放大伤害。</summary>
		public float Power = 1f;
	}

	/// <summary>一次施法请求 —— 起手轴交给瞄准轴的东西（谁、放什么、从哪、朝哪）。</summary>
	public sealed class SpellCastRequest
	{
		public Agent Caster;
		public SpellDef Spell;
		public Vec3 Origin;
		public Vec3 Direction;

		/// <summary>这一发的力度 0~1（起手轴的蓄力档位；非蓄力恒 1）。</summary>
		public float Power = 1f;
	}

	/// <summary>
	/// 一发在飞的投送物 = 意图 + 投送实现 + 结算实现们。
	/// 宿主（<see cref="SpellProjectileLogic"/>）每帧调 <c>Delivery.Tick</c>；
	/// 投送撞到东西时调 <see cref="ReportHit"/> 把命中派给全部结算实现。
	/// </summary>
	public sealed class SpellShot
	{
		public readonly SpellCastIntent Intent;

		/// <summary>投送实现的实例（有生命周期的那一半）。</summary>
		public ISpellDeliveryInstance Delivery;

		/// <summary>这一发挂的结算实现（同一个命中事件，每个都收到一次）。</summary>
		public readonly List<ISpellPayload> Payloads = new List<ISpellPayload>();

		public SpellShot(SpellCastIntent intent)
		{
			Intent = intent;
		}

		/// <summary>把一次命中派给全部结算实现（结算永远"来一次算一次"，节流是投送的事）。</summary>
		public void ReportHit(SpellHit hit)
		{
			for (int i = 0; i < Payloads.Count; i++)
			{
				try
				{
					Payloads[i].OnHit(hit);
				}
				catch (Exception ex)
				{
					DebugLogger.Log($"[Spell] 结算 '{Payloads[i].Id}' 异常：{ex.GetType().Name} {ex.Message}");
				}
			}
		}
	}

	// ─────────────────────────── 三张接口 ───────────────────────────

	/// <summary>瞄准轴：法术往哪去（准星 / 软锁 / 落点 / 自身为心）。</summary>
	public interface ISpellTargeting
	{
		/// <summary>数据里写的实现 id（<c>targeting="aim"</c>）。</summary>
		string Id { get; }

		/// <summary>产出一张意图表（写进 <paramref name="output"/>；一条都不产出 = 这次施法没发生）。</summary>
		void BuildIntents(SpellCastRequest request, List<SpellCastIntent> output);
	}

	/// <summary>投送轴：从施法者到落点之间怎么过去（直线飞 / 抛物 / 追踪 / 持续光束 / 位移）。</summary>
	public interface ISpellDelivery
	{
		/// <summary>数据里写的实现 id（<c>delivery="projectile"</c>）。</summary>
		string Id { get; }

		/// <summary>进入：为这一条意图建立一个投送物。返回 null = 这次投送没起来。</summary>
		ISpellDeliveryInstance Begin(SpellShot shot);
	}

	/// <summary>投送物的生命周期（契约 2：进入 / 每帧 / 结束 —— 持续光束以后只是换一个实现）。</summary>
	public interface ISpellDeliveryInstance
	{
		/// <summary>每帧推进。<c>false</c> = 这一发结束（宿主会立刻回收并调用 <see cref="End"/>）。</summary>
		bool Tick(float dt);

		/// <summary>结束：清掉自己造出来的东西（实体、粒子、光源…）。必须可重入（宿主保证只调一次）。</summary>
		void End();
	}

	/// <summary>
	/// 结算轴：到了之后发生什么（单体伤害 / 半径伤害 / 状态 / 地面区域 / 召唤）。
	/// 🔴 契约 3：只管"收到一次命中就结算一次"，**不做节流** —— 持续型法术"多久上报一次"归投送管。
	/// </summary>
	public interface ISpellPayload
	{
		/// <summary>数据里写的实现 id（<c>payloads="damage,area"</c>）。</summary>
		string Id { get; }

		/// <summary>收到一次命中。</summary>
		void OnHit(SpellHit hit);
	}

	// ─────────────────────────── 三张注册表 ───────────────────────────

	/// <summary>瞄准轴实现注册表（注册制：以后加"软锁/落点/自身为心"都是往这里注册一个实现）。</summary>
	public static class SpellTargetingRegistry
	{
		private static readonly Dictionary<string, ISpellTargeting> _all =
			new Dictionary<string, ISpellTargeting>(StringComparer.Ordinal);

		static SpellTargetingRegistry()
		{
			Register(new AimTargeting());      // 准星直视
			Register(new GroundTargeting());   // 落点
			Register(new LockedTargeting());   // 软锁（可锁 N 个）
			Register(new SelfTargeting());     // 自身为心（光环/领域）
		}

		public static void Register(ISpellTargeting impl)
		{
			if (impl != null && !string.IsNullOrEmpty(impl.Id))
			{
				_all[impl.Id] = impl;
			}
		}

		/// <summary>查不到 = 该轴还没实现 → 调用方跳过这个法术并记日志（不是崩、也不是静默换别的）。</summary>
		public static ISpellTargeting Get(string id)
		{
			ISpellTargeting impl;
			return !string.IsNullOrEmpty(id) && _all.TryGetValue(id, out impl) ? impl : null;
		}
	}

	/// <summary>投送轴实现注册表。</summary>
	public static class SpellDeliveryRegistry
	{
		private static readonly Dictionary<string, ISpellDelivery> _all =
			new Dictionary<string, ISpellDelivery>(StringComparer.Ordinal);

		static SpellDeliveryRegistry()
		{
			Register(new ProjectileDelivery());   // 直线飞 / 抛物 / 追踪 / 天降（都是它的参数）
			Register(new PlaceDelivery());        // 留在落点上，按间隔反复结算
			Register(new ChannelDelivery());      // 持续（光束/领域；结束时刻由起手轴决定）
		}

		public static void Register(ISpellDelivery impl)
		{
			if (impl != null && !string.IsNullOrEmpty(impl.Id))
			{
				_all[impl.Id] = impl;
			}
		}

		public static ISpellDelivery Get(string id)
		{
			ISpellDelivery impl;
			return !string.IsNullOrEmpty(id) && _all.TryGetValue(id, out impl) ? impl : null;
		}
	}

	/// <summary>结算轴实现注册表（同一个法术可以挂多个 —— <c>payloads="damage,area"</c>）。</summary>
	public static class SpellPayloadRegistry
	{
		private static readonly Dictionary<string, ISpellPayload> _all =
			new Dictionary<string, ISpellPayload>(StringComparer.Ordinal);

		static SpellPayloadRegistry()
		{
			Register(new DamagePayload());
			Register(new AreaPayload());
			Register(new StatusPayload());
			Register(new AuraPayload());
		}

		public static void Register(ISpellPayload impl)
		{
			if (impl != null && !string.IsNullOrEmpty(impl.Id))
			{
				_all[impl.Id] = impl;
			}
		}

		public static ISpellPayload Get(string id)
		{
			ISpellPayload impl;
			return !string.IsNullOrEmpty(id) && _all.TryGetValue(id, out impl) ? impl : null;
		}
	}

	// ─────────────────────────── 纯数学（不碰引擎）───────────────────────────

	/// <summary>弹道与姿态的数学（纯函数，不碰引擎 —— 好读、好改、好验算）。</summary>
	public static class SpellMath
	{
		/// <summary>
		/// 由「飞行方向 + 横滚角」拼出网格的姿态矩阵。
		/// 🔴 约定：网格的**飞行轴 = 本地 +Z**（原版弹丸一致：<c>bolt_bl_a</c> 尖端在本地 z=+0.4785）。
		///   所以这里把本地 Z（<c>Mat3.u</c>）对齐飞行方向；剩下两个轴取「世界朝上」为基准（横滚 0 = 不翻）。
		///   朝向看着不对时**先调数据里的 tilt_deg**（绕飞行轴转），别急着改网格几何。
		/// </summary>
		public static Mat3 BuildFlightRotation(Vec3 direction, float tiltDeg)
		{
			Vec3 u = direction;
			if (u.LengthSquared < 1e-8f)
			{
				u = Vec3.Forward;
			}
			else
			{
				u = u.NormalizedCopy();
			}

			// 参考"上"：世界朝上在垂直于飞行方向平面上的投影（飞行方向接近竖直时退化成世界前方）
			Vec3 f = Vec3.Up - u * Vec3.DotProduct(Vec3.Up, u);
			if (f.LengthSquared < 1e-6f)
			{
				f = Vec3.Forward - u * Vec3.DotProduct(Vec3.Forward, u);
			}
			f = f.NormalizedCopy();
			Vec3 s = Vec3.CrossProduct(f, u);   // 右手系：X = Y × Z

			Mat3 m = new Mat3(s, f, u);
			if (Math.Abs(tiltDeg) > 0.001f)
			{
				m.RotateAboutAnArbitraryVector(u, tiltDeg * (MathF.PI / 180f));
			}
			return m;
		}

		/// <summary>
		/// 让网格**躺在地面上**的姿态（放置区域、落点指示圈用）。
		/// 🔴 约定：这些网格的**平面法线 = 本地 +Y**（`lwn_flight_sigil` 就是这样 ——
		///   它在 LWN 的 prefab 里必须补一句 `rotation_euler="1.571,0,0"` 才躺得平）。
		///   所以这里把本地 +Y 对齐世界朝上；水平面内不转（网格自己的朝向即它在世界里的朝向）。
		/// </summary>
		public static Mat3 BuildGroundRotation()
		{
			// s = X = 世界 +X，f = Y = 世界上，u = Z = 世界 +Y（右手系：X × Y = Z）
			return new Mat3(Vec3.Side, Vec3.Up, Vec3.Forward);
		}

		/// <summary>
		/// 解算初速度：想从 <paramref name="origin"/> 打到 <paramref name="aimPoint"/>，初速固定为
		/// <paramref name="speed"/> 时该朝哪个方向、以什么速度飞（重力 0 时就是直线）。
		/// 无解（目标太远/太低，这个初速够不到）→ 退回直线朝瞄准点飞（宁可打不到，也不要歪到别处）。
		/// 🔴 <paramref name="gravity"/> **带符号**：正 = 往下掉（解抛物线打准瞄准点）·
		///    **负 = 反重力**（不平抛解算，直接照瞄准方向打出去 —— 向上的加速度自然把弹道抬起来；
		///    真要解抛物线的话会算出一个"先往下压"的发射角，看着莫名其妙）。
		/// </summary>
		public static Vec3 SolveLaunchVelocity(Vec3 origin, Vec3 aimPoint, float speed, float gravity)
		{
			Vec3 flat = new Vec3(aimPoint.x - origin.x, aimPoint.y - origin.y, 0f);
			float range = flat.Length;
			if (speed <= 0f || Math.Abs(gravity) < 1e-4f || range < 1e-3f || gravity < 0f)
			{
				Vec3 dir = aimPoint - origin;
				dir = dir.LengthSquared < 1e-8f ? Vec3.Forward : dir.NormalizedCopy();
				return dir * speed;
			}

			float dz = aimPoint.z - origin.z;
			float g = gravity;
			float s2 = speed * speed;
			// 标准斜抛解：tanθ = (s² ± √(s⁴ − g(g·x² + 2·z·s²))) / (g·x)
			float disc = s2 * s2 - g * (g * range * range + 2f * dz * s2);
			if (disc < 0f)
			{
				Vec3 dir = aimPoint - origin;
				dir = dir.LengthSquared < 1e-8f ? Vec3.Forward : dir.NormalizedCopy();
				return dir * speed;
			}
			// 取低伸弹道（−√）—— 直射武器的直觉；高抛留给以后的数据开关
			float tanTheta = (s2 - MathF.Sqrt(disc)) / (g * range);
			Vec3 flatDir = flat * (1f / range);
			// (水平方向 × cosθ + 世界朝上 × sinθ) × 初速
			float cos = 1f / MathF.Sqrt(1f + tanTheta * tanTheta);
			float sin = tanTheta * cos;
			return flatDir * (speed * cos) + Vec3.Up * (speed * sin);
		}
	}

	// ─────────────────── 引擎触点（造实体 / 粒子 / 音效 / 伤害）───────────────────
	// 全项目只有这一个地方碰这些引擎 API —— 换版本、出问题都只看这一处。
	// 每一步都带 null 保护（铁律 1：内容包没装、资产缺失、场景已卸载都不能崩）。

	/// <summary>法术的引擎触点门面（造飞行实体 / 挂粒子 / 播音效 / 落地伤害）。</summary>
	public static class SpellWorld
	{
		private static readonly Dictionary<string, MetaMesh> _meshes = new Dictionary<string, MetaMesh>(StringComparer.Ordinal);
		private static readonly HashSet<string> _meshFailed = new HashSet<string>(StringComparer.Ordinal);
		private static readonly Dictionary<string, int> _particleIds = new Dictionary<string, int>(StringComparer.Ordinal);
		private static readonly Dictionary<string, int> _soundIds = new Dictionary<string, int>(StringComparer.Ordinal);
		private static readonly HashSet<string> _logged = new HashSet<string>(StringComparer.Ordinal);

		/// <summary>
		/// 按名取网格：先 <c>GetMultiMesh</c>（已在内存的共享件），失败再 <c>GetCopy</c>（可按需加载）。
		/// 解析一次整局缓存（飞行物的网格是共享件，**不要每发都 GetCopy** —— 那是每发一份副本）。
		/// 空名字或查不到 = null（调用方不画飞行物，但法术照常飞、照常命中）。
		/// </summary>
		public static MetaMesh ResolveMesh(string name)
		{
			if (string.IsNullOrEmpty(name) || _meshFailed.Contains(name))
			{
				return null;
			}
			MetaMesh cached;
			if (_meshes.TryGetValue(name, out cached))
			{
				return cached;
			}
			MetaMesh mesh = null;
			try
			{
				mesh = MetaMesh.GetMultiMesh(name);
			}
			catch (Exception)
			{
				// 落到 GetCopy 再试
			}
			if (mesh == null)
			{
				try
				{
					mesh = MetaMesh.GetCopy(name, showErrors: false, mayReturnNull: true);
				}
				catch (Exception ex)
				{
					LogOnce("网格解析异常 " + name, ex);
				}
			}
			if (mesh == null)
			{
				_meshFailed.Add(name);
				DebugLogger.Log($"[Spell] 网格 '{name}' 查不到 —— 该法术不画飞行物（检查 `lwn_core.tpac` 里的资产名）");
				return null;
			}
			_meshes[name] = mesh;
			return mesh;
		}

		/// <summary>在指定位置/姿态造一个场景实体并挂上网格。<paramref name="rotation"/> 的基向量长度即缩放。</summary>
		public static GameEntity SpawnMeshEntity(string meshName, Vec3 position, Mat3 rotation, float scale)
		{
			Mission mission = Mission.Current;
			if (mission == null || mission.Scene == null)
			{
				return null;
			}
			MetaMesh mesh = ResolveMesh(meshName);
			if (mesh == null)
			{
				return null;
			}
			try
			{
				GameEntity entity = GameEntity.CreateEmpty(mission.Scene, true);
				if (entity == null)
				{
					return null;
				}
				entity.AddMultiMesh(mesh, true);
				Mat3 rot = rotation;
				if (Math.Abs(scale - 1f) > 0.001f && scale > 0f)
				{
					rot.ApplyScaleLocal(scale);
				}
				entity.SetGlobalFrame(new MatrixFrame(rot, position));
				return entity;
			}
			catch (Exception ex)
			{
				LogOnce("飞行实体生成异常 " + meshName, ex);
				return null;
			}
		}

		/// <summary>把粒子挂在实体上（引擎自动带着它走 —— 这就是原版 <c>trail_particle_name</c> 的工作方式）。</summary>
		public static ParticleSystem AttachParticle(string particleName, GameEntity host)
		{
			if (string.IsNullOrEmpty(particleName) || host == null)
			{
				return null;
			}
			int id = ResolveParticleId(particleName);
			if (id < 0)
			{
				return null;
			}
			try
			{
				MatrixFrame local = MatrixFrame.Identity;
				return ParticleSystem.CreateParticleSystemAttachedToEntity(id, host, ref local);
			}
			catch (Exception ex)
			{
				LogOnce("挂粒子异常 " + particleName, ex);
				return null;
			}
		}

		/// <summary>在世界坐标炸一次粒子（命中爆散）。</summary>
		public static void BurstParticle(string particleName, Vec3 position)
		{
			Mission mission = Mission.Current;
			if (string.IsNullOrEmpty(particleName) || mission == null || mission.Scene == null)
			{
				return;
			}
			int id = ResolveParticleId(particleName);
			if (id < 0)
			{
				return;
			}
			try
			{
				MatrixFrame frame = new MatrixFrame(Mat3.Identity, position);
				mission.Scene.CreateBurstParticle(id, frame);
			}
			catch (Exception ex)
			{
				LogOnce("爆散粒子异常 " + particleName, ex);
			}
		}

		/// <summary>在指定位置播一条内容包音效（名字查不到 = 静默跳过，只记一次日志）。</summary>
		public static void PlaySound(string soundName, Vec3 position)
		{
			Mission mission = Mission.Current;
			if (string.IsNullOrEmpty(soundName) || mission == null || mission.Scene == null)
			{
				return;
			}
			int id = ResolveSoundId(soundName);
			if (id < 0)
			{
				return;
			}
			try
			{
				SoundEvent sound = SoundEvent.CreateEvent(id, mission.Scene);
				if (sound == null)
				{
					return;
				}
				sound.SetPosition(position);
				sound.Play();
			}
			catch (Exception ex)
			{
				LogOnce("音效播放异常 " + soundName, ex);
			}
		}

		/// <summary>
		/// 落地伤害 —— 走引擎全管线（护甲、减伤、死亡判定都在 <c>RegisterBlow</c> 后面，白送）。
		/// 实现 = 手搓 <c>Blow</c> + <c>AttackCollisionData</c>，再交给既有轮子
		/// <see cref="AgentDamageHelper.CastBlow"/>（照抄 SwordBeam 的实证路子，见
		/// Knowledge/SwordBeam剑气_实现分析.md §2.7；`DamageCalculated = true` = 声明"伤害已算过"）。
		/// </summary>
		public static void Damage(Agent victim, float damage, DamageTypes damageType,
			Vec3 position, Vec3 direction, Agent attacker)
		{
			if (victim == null || damage <= 0f || !AgentControlHelper.SafeIsActive(victim) || victim.Health <= 0f)
			{
				return;
			}
			try
			{
				Vec3 dir = direction.LengthSquared < 1e-8f ? Vec3.Forward : direction.NormalizedCopy();
				Blow blow = new Blow(attacker != null ? attacker.Index : -1);
				blow.DamageType = damageType;
				blow.BoneIndex = victim.Monster != null ? victim.Monster.HeadLookDirectionBoneIndex : (sbyte)0;
				blow.VictimBodyPart = BoneBodyPartType.Chest;
				blow.GlobalPosition = position;
				blow.BaseMagnitude = damage;
				blow.WeaponRecord.FillAsMeleeBlow(null, null, -1, -1);
				blow.InflictedDamage = (int)damage;
				blow.SwingDirection = dir;
				blow.Direction = dir;
				blow.DamageCalculated = true;

				AttackCollisionData collision = AttackCollisionData.GetAttackCollisionDataForDebugPurpose(
					_attackBlockedWithShield: false, _correctSideShieldBlock: false, _isAlternativeAttack: false,
					_isColliderAgent: true, _collidedWithShieldOnBack: false, _isMissile: false,
					_isMissileBlockedWithWeapon: false, _missileHasPhysics: false, _entityExists: false,
					_thrustTipHit: false, _missileGoneUnderWater: false, _missileGoneOutOfBorder: false,
					CombatCollisionResult.StrikeAgent, -1, 0, (int)damageType, blow.BoneIndex,
					BoneBodyPartType.Chest, -1, Agent.UsageDirection.AttackLeft, -1,
					CombatHitResultFlags.NormalHit, 0.5f, 1f, 0f, 0f, 0f, 0f, 0f, 0f,
					Vec3.Up, dir, position, Vec3.Zero, Vec3.Zero, victim.Velocity, Vec3.Up);

				AgentDamageHelper.CastBlow(victim, in blow, in collision, damage, logTag: "Spell");
			}
			catch (Exception ex)
			{
				LogOnce("伤害落地异常", ex);
			}
		}

		private static int ResolveParticleId(string name)
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
				DebugLogger.Log($"[Spell] 粒子 '{name}' 查不到 —— 该表现不放粒子"
					+ "（检查 LivingWorldNpcs 的 project.mbproj 挂了 soln_particle_systems、且 lwn_core.tpac / lwn_yinmo_prt.tpac 已装机）");
			}
			return id;
		}

		private static int ResolveSoundId(string name)
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
				DebugLogger.Log($"[Spell] 音效 '{name}' 查不到 —— 该档静音（检查 module_sounds.xml）");
			}
			return id;
		}

		/// <summary>
		/// **施法朝向** —— 玩家用**相机朝向**（第三人称下相机 ≠ 身体朝向，2026-09-24 实机报的 bug），
		/// 其他人用身体朝向。FCS 也是这么分的：起点在手上、方向从相机算（计划 §14.2 第 11 条）。
		/// </summary>
		public static Vec3 CastDirection(Agent caster)
		{
			if (caster == null)
			{
				return Vec3.Forward;
			}
			Mission mission = Mission.Current;
			if (mission != null && mission.MainAgent == caster)
			{
				// 🔴 玩家：方向 = **相机中心**，走全项目唯一入口 `CameraLook`（CLAUDE.md 铁律 35）：
				//    相机被接管（飞行 / 演出）时问接管方自己；没接管才用引擎角度。
				//    ⚠️ 曾经直接读 `MissionScreen.CameraBearing` —— 接管期间那是**冻的旧值**
				//       （症状：飞行中放法术永远朝"起飞时看的方向"飞；2026-09-21 在飞行上栽过同一条）。
				if (CameraLook.TryGet(out Vec3 look)
					&& look.LengthSquared > 1e-6f)
				{
					return look.NormalizedCopy();
				}
			}
			Vec3 bodyLook = caster.LookDirection;
			return bodyLook.LengthSquared < 1e-8f ? Vec3.Forward : bodyLook.NormalizedCopy();
		}

		/// <summary>
		/// **镜头视线** —— 现在只是 <see cref="CameraLook.TryGet"/> 的薄壳
		/// （**唯一实现**搬到了 `Camera/CameraLook.cs`：罗盘等别的系统也走那一个入口 —— CLAUDE.md 铁律 35）。
		/// 保留这个方法名，是因为诊断日志（<see cref="DescribeAimSources"/>）与既有调用点都认它。
		/// 🔴 正确写法（没接管时）= `Mat3.Identity` 绕 Up 转 `CameraBearing`、绕 Side 转 `CameraElevation`，取 `.f`。
		/// 🔴🔴 **绝不要用 `Mission.GetCameraFrame()` 取方向**（2026-09-24 实测它的基向量：`.f` 是"上"、
		///   `.u` 是视线的**反向**、`.s` 是右向）—— 项目在这上面栽过：飞行 2026-09-21"按 W 窜到 160 米天花板"、
		///   法术"对天开火月牙朝地飞"。
		/// </summary>
		public static Vec3 CameraForward()
		{
			return CameraLook.TryGet(out Vec3 forward) ? forward : Vec3.Zero;
		}

		/// <summary>
		/// **方向取证行**（2026-09-24 用户要求）：把这一发用到的方向与所有候选来源打一行，
		/// 用来一眼判定"哪个来源才是真正的视线"。只在**玩家自己施法**时打（NPC 不进日志）。
		/// 各来源含义：
		///   取用 = 实际喂给瞄准轴的向量（X 键 = `CameraForward()`；左键 = 引擎给的出膛速度方向）
		///   look = `Agent.LookDirection`（**身体**朝向）· move = `Agent.GetMovementDirection()`（移动方向）
		///   camBearing/camElevation = 引擎相机角度（原值）
		///   frameF/frameU = `Mission.GetCameraFrame().rotation.f / .u` —— 🔴 **已知不可用**（留着做反面对照）
		/// </summary>
		public static string DescribeAimSources(Agent caster, Vec3 used)
		{
			try
			{
				Vec3 look = caster.LookDirection;
				Vec2 move = caster.GetMovementDirection();
				float bearing = float.NaN, elevation = float.NaN;
				if (TaleWorlds.ScreenSystem.ScreenManager.TopScreen is TaleWorlds.MountAndBlade.View.Screens.MissionScreen ms)
				{
					bearing = ms.CameraBearing;
					elevation = ms.CameraElevation;
				}
				Vec3 frameF = Vec3.Zero, frameU = Vec3.Zero;
				Mission mission = Mission.Current;
				if (mission != null)
				{
					MatrixFrame cam = mission.GetCameraFrame();
					frameF = cam.rotation.f;
					frameU = cam.rotation.u;
				}
				return $"取用=({used.x:F2},{used.y:F2},{used.z:F2})"
					+ $" look=({look.x:F2},{look.y:F2},{look.z:F2})"
					+ $" move=({move.x:F2},{move.y:F2})"
					+ $" camBearing={bearing:F3} camElevation={elevation:F3}"
					+ $" frameF=({frameF.x:F2},{frameF.y:F2},{frameF.z:F2})"
					+ $" frameU=({frameU.x:F2},{frameU.y:F2},{frameU.z:F2})";
			}
			catch (Exception ex)
			{
				return $"取证失败：{ex.GetType().Name}";
			}
		}

		/// <summary>
		/// 取"这个 agent 现在准备放哪个法术" —— **唯一实现**（相位机 / 落点圈 / NPC 施法者三处共用）。
		/// 判据顺序：
		///   ① 手持武器的弹药（弩/弓类：弹药挂在手持件下面；投掷类：手持件本身就是弹药）
		///   ② 手持件本身（有些状态下手持的就是法术弹）
		///   ③ 🔴 **扫装备槽兜底**（2026-09-24 实机 bug：弩开火后弹药槽会空一会儿 →
		///      只按 ① 判会返回 null，X 键**静默失效**；扫槽 = "这个人身上带着法术弹"就认）
		/// </summary>
		public static SpellDef ResolveWieldedSpell(Agent agent)
		{
			if (agent == null)
			{
				return null;
			}
			MissionWeapon wielded = agent.WieldedWeapon;
			SpellDef found = FromWeapon(wielded);
			if (found != null)
			{
				return found;
			}
			// 兜底：扫四个武器槽（含弹药槽），谁在法术表里就用谁
			try
			{
				for (EquipmentIndex slot = EquipmentIndex.Weapon0; slot <= EquipmentIndex.Weapon3; slot++)
				{
					found = FromWeapon(agent.Equipment[slot]);
					if (found != null)
					{
						return found;
					}
				}
			}
			catch (Exception)
			{
				// 取不到装备就当没有
			}
			return null;
		}

		private static SpellDef FromWeapon(MissionWeapon weapon)
		{
			if (weapon.IsEqualTo(MissionWeapon.Invalid) || weapon.Item == null)
			{
				return null;
			}
			WeaponComponentData usage = weapon.CurrentUsageItem;
			ItemObject ammo = usage != null && usage.IsRangedWeapon && usage.IsConsumable
				? weapon.Item
				: weapon.AmmoWeapon.Item;
			SpellDef def = ammo != null ? SpellRegistry.FindByAmmo(ammo.StringId) : null;
			return def ?? SpellRegistry.FindByAmmo(weapon.Item.StringId);
		}

		private static void LogOnce(string key, Exception ex)
		{
			if (!_logged.Add(key))
			{
				return;
			}
			DebugLogger.Log($"[Spell] {key}：{ex.GetType().Name} {ex.Message}");
		}
	}

	// ─────────────────────────── 阶段 1 的三个实现 ───────────────────────────

	/// <summary>
	/// **伤害解算的唯一入口**（阶段 5；公式采纳计划 §24 第 6 条的 FCS 那套）：
	/// <code>伤害 = damage × (1 + charge_bonus × power) × (1 − damage_variance × rand) × (暴击 ? crit_mult : 1)</code>
	/// 🔴 浮动与暴击**默认都是 0（关）** ⇒ 旧法术逐字不变（阶段 5 验收第 2 条）；
	///   挂上对应的宝石之后才有"伤害有浮动、暴击有可读峰值"。
	/// 🔴 三个结算实现（damage / area / aura）**都走它**，所以"伤害怎么算"只有一处。
	/// </summary>
	internal static class SpellDamage
	{
		public static float Roll(SpellDef spell, float power, out bool crit)
		{
			crit = false;
			if (spell == null)
			{
				return 0f;
			}
			float p = MathF.Max(0f, MathF.Min(1f, power));
			float damage = spell.Damage * (1f + spell.ChargeBonus * p);
			if (spell.DamageVariance > 0f)
			{
				damage *= 1f - MathF.Min(1f, spell.DamageVariance) * MBRandom.RandomFloat;
			}
			if (spell.Crit > 0f && MBRandom.RandomFloat < spell.Crit)
			{
				crit = true;
				damage *= spell.CritMult > 0f ? spell.CritMult : 1f;
			}
			return damage;
		}
	}

	/// <summary>
	/// **扫掠判定**（投送轴的公共件）—— "从 from 到 to 这一小段，最先撞到的是什么"。
	///
	/// 两条引擎查询各给一个距离，**取近的那个**：
	///   ① <c>Mission.RayCastForClosestAgent</c>：打到谁（引擎内部走空间索引，1 次原生调用）
	///   ② <c>Scene.RayCastForClosestEntityOrTerrain</c>：打到墙/地（1 次原生调用）
	/// 🔴 为什么不用"给实体挂碰撞体"：物理体 API 存在，但**引擎没有任何碰撞回调**
	///   （全 DLL 搜 OnCollisionBegin / ContactPoint 零命中）—— 挂了也永远不告诉你撞到了谁。
	/// 🔴 **绝不遍历全场 agent 读 CollisionCapsule**（每读一个就是一次原生跨界调用；
	///   几百人 × 20 Hz = 十万次/秒）。这也是把这段收成一个共享件的原因：写错一次就全局写错。
	///
	/// 用法：投送（直线飞/追踪/天降）与引导（持续光束）都调它 —— 判定口径因此**只有一处**。
	/// </summary>
	internal static class SpellSweep
	{
		/// <summary>
		/// 找这一段里最近的一次碰撞。
		/// 返回 false = 什么都没撞到（<paramref name="victim"/> / <paramref name="point"/> 无意义）。
		/// 返回 true 且 <paramref name="victim"/> 非空 = 打到人（point = 躯干高度处）；
		/// 返回 true 且 <paramref name="victim"/> 为空 = 打到墙/地（point = 表面点）。
		/// <paramref name="alreadyHit"/> = 已经打过的目标（穿透时不重复报；null = 不去重）。
		/// </summary>
		/// <summary>一次扫掠里最多跳过几个人（防"所有人都在已命中表里"时死循环）。</summary>
		private const int MaxSkipPerSweep = 8;

		/// <summary>
		/// 找这一段里最近的一次碰撞（**不带修正过滤**的旧口径 —— 引导/领域那条路继续用它）。
		/// 返回 false = 什么都没撞到（<paramref name="victim"/> / <paramref name="point"/> 无意义）。
		/// </summary>
		public static bool FindNearestHit(Mission mission, Vec3 from, Vec3 to, float agentRadius,
			int excludeAgentIndex, HashSet<int> alreadyHit, out Agent victim, out Vec3 point)
		{
			Vec3 normal;
			return FindNearestHit(mission, from, to, agentRadius, excludeAgentIndex, null, alreadyHit, null,
				out victim, out point, out normal);
		}

		/// <summary>
		/// 同上，但带上**阶段 5 的三个旋钮**（都由 <paramref name="spell"/> 决定，null = 全用现状口径）：
		///   · <c>collide_agent</c> / <c>collide_terrain</c> = **这条查询做不做**（③ 命中查询）
		///   · <c>hit_same_team</c> = 打不打自己人（④ 命中裁决；默认打 = 引擎现状）
		/// 外加 <paramref name="normal"/>：撞地形/墙面时的**表面法线**（弹跳按它反射速度）。
		/// </summary>
		public static bool FindNearestHit(Mission mission, Vec3 from, Vec3 to, float agentRadius,
			int excludeAgentIndex, Team casterTeam, HashSet<int> alreadyHit, SpellDef spell,
			out Agent victim, out Vec3 point, out Vec3 normal)
		{
			victim = null;
			point = to;
			normal = Vec3.Up;
			if (mission == null || mission.Scene == null)
			{
				return false;
			}

			float agentDistance = float.MaxValue;
			float worldDistance = float.MaxValue;
			bool worldHit = false;
			Vec3 worldPoint = to;

			if (spell == null || spell.CollideAgent)
			{
				try
				{
					// 🔴 引擎只给"最近的那一个"：要过滤（已命中 / 自己人）就得往后挪一点再问一次。
					//    跳过量有上限（MaxSkipPerSweep），"整队人都在已命中表里"也不会转不出来。
					Vec3 cursor = from;
					float consumed = 0f;
					for (int guard = 0; guard < MaxSkipPerSweep; guard++)
					{
						float distance;
						Agent found = V.RayCastForClosestAgent(mission, cursor, to, excludeAgentIndex, agentRadius, out distance);
						if (found == null || found.Health <= 0f)
						{
							break;
						}
						if (!SkipAgent(found, casterTeam, alreadyHit, spell))
						{
							victim = found;
							agentDistance = consumed + distance;
							break;
						}
						Vec3 rest = to - cursor;
						float restLength = rest.Length;
						if (restLength < 1e-4f)
						{
							break;
						}
						float advance = distance + 0.05f;
						if (advance >= restLength)
						{
							break;
						}
						cursor += rest * (advance / restLength);
						consumed += advance;
					}
				}
				catch (Exception ex)
				{
					DebugLogger.Log($"[Spell] 人物扫掠异常：{ex.GetType().Name} {ex.Message}");
				}
			}

			if (spell == null || spell.CollideTerrain)
			{
				try
				{
					Vec3 hitPoint;
					float distance;
					// 场景查询用细射线（0.01）：命中点 = 真正的表面（"命中半径"是给人用的，不给墙）
					worldHit = mission.Scene.RayCastForClosestEntityOrTerrain(from, to, out distance, out hitPoint,
						0.01f, BodyFlags.CommonCollisionExcludeFlagsForMissile);
					if (worldHit)
					{
						worldDistance = distance;
						worldPoint = hitPoint;
					}
				}
				catch (Exception ex)
				{
					DebugLogger.Log($"[Spell] 场景扫掠异常：{ex.GetType().Name} {ex.Message}");
				}
			}

			if (victim != null && agentDistance <= worldDistance)
			{
				point = victim.Position;
				point.z += victim.GetEyeGlobalHeight() * 0.5f;   // 视觉上打在躯干，不是脚底
				return true;
			}
			if (worldHit)
			{
				victim = null;
				point = worldPoint;
				normal = ResolveWorldNormal(mission, worldPoint, to - from);
				return true;
			}
			return false;
		}

		/// <summary>这个目标要不要跳过（已命中过 / 自己人且法术不打自己人）。</summary>
		private static bool SkipAgent(Agent agent, Team casterTeam, HashSet<int> alreadyHit, SpellDef spell)
		{
			if (alreadyHit != null && alreadyHit.Contains(agent.Index))
			{
				return true;
			}
			// hit_same_team 默认 true（引擎现状就是打自己人）；只有数据显式关掉才滤
			if (spell != null && !spell.HitSameTeam && casterTeam != null && agent.Team == casterTeam)
			{
				return true;
			}
			return false;
		}

		/// <summary>
		/// 撞到世界的**表面法线**（弹跳按它反射速度）。
		/// ① 地形：引擎直接给（<c>GetTerrainHeightAndNormal</c>，一次调用、精确）；
		/// ② 墙面/物件：引擎的射线不给法线 ⇒ 打三根探针估一个平面（弹跳是低频事件，多两次射线无所谓）。
		/// 估不出来时退回 <c>-来向</c>（= 正面撞上去弹回来），不抛异常（铁律 1）。
		/// </summary>
		private static Vec3 ResolveWorldNormal(Mission mission, Vec3 point, Vec3 travel)
		{
			Vec3 dir = travel.LengthSquared > 1e-8f ? travel.NormalizedCopy() : Vec3.Forward;
			try
			{
				float height;
				Vec3 terrainNormal;
				mission.Scene.GetTerrainHeightAndNormal(new Vec2(point.x, point.y), out height, out terrainNormal);
				if (terrainNormal.LengthSquared > 1e-6f && Math.Abs(point.z - height) < 1.5f)
				{
					return terrainNormal.NormalizedCopy();
				}
			}
			catch (Exception)
			{
				// 取不到地形法线 → 走探针
			}
			return ProbeNormal(mission, point, dir);
		}

		/// <summary>三探针估平面法线（命中点 + 横向两点的外积）。</summary>
		private static Vec3 ProbeNormal(Mission mission, Vec3 point, Vec3 dir)
		{
			Vec3 side = Vec3.CrossProduct(dir, Vec3.Up);
			if (side.LengthSquared < 1e-6f)
			{
				side = Vec3.CrossProduct(dir, Vec3.Forward);
			}
			side = side.NormalizedCopy();
			Vec3 up = Vec3.CrossProduct(side, dir);
			up = up.LengthSquared < 1e-6f ? Vec3.Up : up.NormalizedCopy();

			const float offset = 0.25f;    // 横向偏多少
			const float back = 0.5f;       // 从命中点前面多远起打
			const float reach = 1.2f;      // 探针打多长
			Vec3 origin = point - dir * back;
			Vec3 hitA;
			Vec3 hitB;
			bool okA = CastForPoint(mission, origin + side * offset, dir, reach, out hitA);
			bool okB = CastForPoint(mission, origin + up * offset, dir, reach, out hitB);
			if (!okA || !okB)
			{
				return -dir;
			}
			Vec3 normal = Vec3.CrossProduct(hitA - point, hitB - point);
			if (normal.LengthSquared < 1e-8f)
			{
				return -dir;
			}
			normal = normal.NormalizedCopy();
			return Vec3.DotProduct(normal, dir) > 0f ? -normal : normal;   // 法线要迎着来向
		}

		private static bool CastForPoint(Mission mission, Vec3 origin, Vec3 dir, float reach, out Vec3 hit)
		{
			hit = origin;
			try
			{
				float distance;
				return mission.Scene.RayCastForClosestEntityOrTerrain(origin, origin + dir * reach,
					out distance, out hit, 0.01f, BodyFlags.CommonCollisionExcludeFlagsForMissile);
			}
			catch (Exception)
			{
				return false;
			}
		}
	}

	/// <summary>
	/// 瞄准的公共动作（三个瞄准实现共用）：找瞄准点 / 摊多发 / 拼一条意图。
	/// </summary>
	internal static class SpellAim
	{
		/// <summary>
		/// 沿这条射线找"准星真正指着的那个表面"。打不到东西就是"最远射程处"。
		/// 用**细射线（0.01）**：这里要的是"表面在哪"，不是"能不能碰到"。
		/// </summary>
		/// <param name="clampToGround">
		/// 🔴 **射线什么都没打到时，要不要把落点按到地面上**：
		///   · 落点类（放置 / 天降）→ **true**（法术总得落在地上）
		///   · 准星直射（aim）→ **false** —— 否则**对天开火时瞄准点会被按到 120 米外的地面，
		///     射线解出来的方向就朝下走**（2026-09-24 用户实机报的 bug："我对天发射，月牙朝地飞"）
		/// </param>
		public static Vec3 ResolveSurfacePoint(Vec3 origin, Vec3 direction, float maxDistance,
			bool clampToGround = true)
		{
			Vec3 aimPoint = origin + direction * maxDistance;
			Mission mission = Mission.Current;
			if (mission == null || mission.Scene == null)
			{
				return aimPoint;
			}
			try
			{
				float distance;
				Vec3 point;
				if (mission.Scene.RayCastForClosestEntityOrTerrain(origin, aimPoint,
					out distance, out point, 0.01f, BodyFlags.CommonCollisionExcludeFlagsForMissile))
				{
					aimPoint = point;
				}
				else if (clampToGround)
				{
					// 没打到东西（对着天/对着空）→ 把落点贴到那个位置的地面上
					// ⚠️ 只有落点类法术才这么干（准星直射不能 —— 见参数注释）
					float ground = mission.Scene.GetGroundHeightAtPosition(aimPoint, BodyFlags.CommonCollisionExcludeFlagsForMissile);
					if (!float.IsNaN(ground) && !float.IsInfinity(ground))
					{
						aimPoint.z = ground;
					}
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Spell] 瞄准点射线异常：{ex.GetType().Name} {ex.Message}");
			}
			return aimPoint;
		}

		/// <summary>
		/// 按 <c>count</c> / <c>spread_deg</c> 把"一发"摊成 N 条意图（计划 §2.3 投送参数里的"发数"）。
		/// **多发由瞄准轴展开** —— 契约 1 的那张意图表本来就是干这个的；投送与结算一行不用改。
		/// </summary>
		public static void Emit(SpellCastRequest request, Vec3 aimPoint, Vec3 direction, Agent target,
			List<SpellCastIntent> output)
		{
			SpellDef spell = request.Spell;
			int count = spell.Count > 0 ? spell.Count : 1;
			// 基准发射解（有重力时是抛物线解）——散布是在它上面加角度偏移，不是重新解一遍
			// 🔴 速度取 **EffectiveSpeed**（= speed × speed_scale）：这是初速被读的**唯一一处**
			//    （另一处是天降的起落速度），乘两遍会让"加速"宝石变成平方。
			Vec3 baseVelocity = SpellMath.SolveLaunchVelocity(request.Origin, aimPoint, spell.EffectiveSpeed, spell.Gravity);
			Vec3 baseDir = baseVelocity.LengthSquared < 1e-8f
				? direction.NormalizedCopy()
				: baseVelocity.NormalizedCopy();
			float speed = baseVelocity.Length;
			for (int i = 0; i < count; i++)
			{
				Vec3 dir = direction;
				Vec3 velocity = baseVelocity;
				if (count > 1 && spell.SpreadDeg > 0f)
				{
					dir = Scatter(baseDir, spell.SpreadDeg);
					velocity = dir * speed;
				}
				output.Add(new SpellCastIntent
				{
					Caster = request.Caster,
					Spell = spell,
					Index = output.Count,
					Origin = request.Origin,
					AimPoint = aimPoint,
					Direction = dir,
					Velocity = velocity,
					Target = target,
					Power = request.Power,
				});
			}
		}

		/// <summary>
		/// 在一个圆锥内随机偏一个方向（散布角是圆锥的**全角**）。
		/// 多发（本类）与**分裂**（<see cref="ProjectileDelivery"/> 的 ⑤ 触发）共用它 —— 同一个散布口径。
		/// </summary>
		public static Vec3 Scatter(Vec3 direction, float spreadDeg)
		{
			float half = spreadDeg * 0.5f * (MathF.PI / 180f);
			float cosLimit = MathF.Cos(half);
			float z = cosLimit + MBRandom.RandomFloat * (1f - cosLimit);   // 圆锥内均匀
			float phi = MBRandom.RandomFloat * MathF.PI * 2f;
			float r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));

			Vec3 u = direction.NormalizedCopy();
			Vec3 side = Vec3.CrossProduct(u, Vec3.Up);
			if (side.LengthSquared < 1e-6f)
			{
				side = Vec3.CrossProduct(u, Vec3.Forward);
			}
			side = side.NormalizedCopy();
			Vec3 up = Vec3.CrossProduct(side, u);
			Vec3 v = side * (r * MathF.Cos(phi)) + up * (r * MathF.Sin(phi)) + u * z;
			return v.NormalizedCopy();
		}
	}

	/// <summary>
	/// 瞄准轴「准星直视」（<c>targeting="aim"</c>）：法术沿"眼睛看的方向"出去。
	/// 顺带解一次抛物线：有重力时先找到准星真正指着的那个点，再解算初速度
	/// （局部的"看哪打哪"= 准星指哪，弹就落到哪）。
	/// </summary>
	internal sealed class AimTargeting : ISpellTargeting
	{
		public string Id
		{
			get { return "aim"; }
		}

		public void BuildIntents(SpellCastRequest request, List<SpellCastIntent> output)
		{
			if (request == null || request.Spell == null)
			{
				return;
			}
			Vec3 direction = request.Direction;
			direction = direction.LengthSquared < 1e-8f ? Vec3.Forward : direction.NormalizedCopy();
			// 🔴 准星直射：**不按地面**（对天开火就得朝天上飞）
			Vec3 aimPoint = SpellAim.ResolveSurfacePoint(request.Origin, direction, request.Spell.MaxDistance,
				clampToGround: false);
			SpellAim.Emit(request, aimPoint, direction, null, output);
		}
	}

	/// <summary>
	/// 瞄准轴「落点」（<c>targeting="ground"</c>）：法术落在**看哪儿的地面/墙面上**，
	/// 而不是从施法者手里直线飞出去。
	/// 用途：放置族（地上留一片）与天降族（落点正上方砸下来 —— 起落高度是**投送**的参数）。
	/// </summary>
	internal sealed class GroundTargeting : ISpellTargeting
	{
		public string Id
		{
			get { return "ground"; }
		}

		public void BuildIntents(SpellCastRequest request, List<SpellCastIntent> output)
		{
			if (request == null || request.Spell == null)
			{
				return;
			}
			Vec3 direction = request.Direction;
			direction = direction.LengthSquared < 1e-8f ? new Vec3(0f, 1f, -0.4f).NormalizedCopy() : direction.NormalizedCopy();
			// 落点 = 视线打到的东西（打不到就贴到那处的地面，见 SpellAim）
			Vec3 point = SpellAim.ResolveSurfacePoint(request.Origin, direction, request.Spell.MaxDistance);
			SpellAim.Emit(request, point, direction, null, output);
		}
	}

	/// <summary>
	/// 瞄准轴「软锁」（<c>targeting="locked"</c>）：把视线锥内的目标锁住，**一个目标一条意图**。
	/// 用途：多目标火球（锁 N 个各追一个）、选定目标雷电（雷落到"目标现在所在的位置"）。
	///
	/// 🔴 目标引用（<see cref="SpellCastIntent.Target"/>）是它存在的理由：追踪与天降要的是
	///   **目标现在的位置**，不是施法那一刻的位置 —— 投送每帧重新取。
	/// 🔴 粗筛用 <c>Mission.GetNearbyAgents</c>（1 次原生调用），**不遍历全场 agent**（计划 §4.4 纪律 1/2）。
	/// 锥内一个目标都没有 → 退化成"照准星打一发"（凭手感，不是失败），日志记一次。
	/// </summary>
	internal sealed class LockedTargeting : ISpellTargeting
	{
		/// <summary>粗筛结果缓冲（复用，不在施法路径 new）。</summary>
		private static readonly MBList<Agent> _nearby = new MBList<Agent>();

		/// <summary>已记过"没锁到人"日志的施法者（防刷屏）。</summary>
		private static readonly HashSet<int> _loggedEmpty = new HashSet<int>();

		public string Id
		{
			get { return "locked"; }
		}

		public void BuildIntents(SpellCastRequest request, List<SpellCastIntent> output)
		{
			SpellDef spell = request?.Spell;
			Mission mission = Mission.Current;
			if (spell == null || mission == null || request.Origin == null)
			{
				return;
			}
			Vec3 look = request.Direction;
			look = look.LengthSquared < 1e-8f ? Vec3.Forward : look.NormalizedCopy();
			float range = spell.LockRange > 0f ? spell.LockRange : 40f;
			float cosLimit = MathF.Cos(MathF.Max(1f, spell.LockAngle) * (MathF.PI / 180f));
			int max = spell.LockMax > 0 ? spell.LockMax : 1;

			int locked = 0;
			try
			{
				_nearby.Clear();
				mission.GetNearbyAgents(new Vec2(request.Origin.x, request.Origin.y), range, _nearby);
				// 从粗筛结果里挑：活着、不是施法者自己、在锥内、按距离由近到远
				while (locked < max)
				{
					Agent best = null;
					float bestSq = float.MaxValue;
					for (int i = 0; i < _nearby.Count; i++)
					{
						Agent agent = _nearby[i];
						if (agent == null || agent == request.Caster || !AgentControlHelper.SafeIsActive(agent) || agent.Health <= 0f)
						{
							continue;
						}
						if (IsLocked(agent, output))
						{
							continue;
						}
						Vec3 to = agent.Position - request.Origin;
						float lengthSq = to.LengthSquared;
						if (lengthSq > range * range || lengthSq < 1e-4f)
						{
							continue;
						}
						if (Vec3.DotProduct(to.NormalizedCopy(), look) < cosLimit)
						{
							continue;   // 不在锥内
						}
						if (lengthSq < bestSq)
						{
							bestSq = lengthSq;
							best = agent;
						}
					}
					if (best == null)
					{
						break;
					}
					Vec3 aim = best.Position;
					aim.z += best.GetEyeGlobalHeight() * 0.5f;   // 瞄躯干
					SpellAim.Emit(request, aim, (aim - request.Origin).NormalizedCopy(), best, output);
					locked++;
					if (spell.Count > 1)
					{
						break;   // 多发法术用 count 摊散布，不再按目标数翻倍（免得 3 目标 × 3 发 = 9）
					}
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Spell] 软锁异常：{ex.GetType().Name} {ex.Message}");
			}

			if (locked == 0)
			{
				if (request.Caster != null && _loggedEmpty.Add(request.Caster.Index))
				{
					DebugLogger.Log($"[Spell] 法术 '{spell.Id}' 软锁没锁到人（锥内无目标）→ 照准星打一发");
				}
				Vec3 point = SpellAim.ResolveSurfacePoint(request.Origin, look, spell.MaxDistance);
				SpellAim.Emit(request, point, look, null, output);
			}
		}

		/// <summary>这个目标已经被锁过了吗（同一发法术不重复锁同一个人）。</summary>
		private static bool IsLocked(Agent agent, List<SpellCastIntent> output)
		{
			for (int i = 0; i < output.Count; i++)
			{
				if (output[i].Target == agent)
				{
					return true;
				}
			}
			return false;
		}
	}


	/// <summary>
	/// 投送轴「直线飞行」（<c>delivery="projectile"</c>）：造一个自管实体飞出去，自己判命中。
	///
	/// 🔴 **为什么不走引擎导弹**：引擎的导弹渲染在约 110 米外必定剔除网格（与资产、大小、flag 都无关，
	///   七次实机实验的证据链见 plans/法术体系-通用施法框架.md 附录 D）。飞行物自管之后，寿命、朝向、
	///   爆炸半径、转向速率**全部变成我们的参数**（旧路线这些全写死在引擎里）。
	///
	/// 命中判定走**引擎原生查询**（不是自己遍历全场 agent 读碰撞胶囊 —— 那是最容易把性能写死的写法）：
	///   ① <c>Mission.RayCastForClosestAgent</c>：打到谁（引擎内部走空间索引，1 次原生调用）
	///   ② <c>Scene.RayCastForClosestEntityOrTerrain</c>：打到墙/地（1 次原生调用）
	///   两条各给一个距离 → **取近的那个** = 这一步真正撞到的东西。
	///   线段 = 「上一次检查点 → 这一次检查点」⇒ 天然是连续碰撞（不会因为一帧跨 3 米而穿过人）。
	/// </summary>
	internal sealed class ProjectileDelivery : ISpellDelivery
	{
		/// <summary>碰撞检查间隔（秒）。**不是每帧** —— 直接省 2/3 次原生调用（计划 §4.4 纪律 3）。</summary>
		private const float CheckInterval = 0.05f;

		public string Id
		{
			get { return "projectile"; }
		}

		public ISpellDeliveryInstance Begin(SpellShot shot)
		{
			if (shot == null || shot.Intent == null || shot.Intent.Spell == null)
			{
				return null;
			}
			return new Instance(shot);
		}

		private sealed class Instance : ISpellDeliveryInstance
		{
			private readonly SpellShot _shot;
			private readonly SpellDef _spell;

			private GameEntity _entity;
			private ParticleSystem _trail;

			/// <summary>施法者的队伍（<c>hit_same_team=false</c> 时用来滤掉自己人；取不到 = null = 不过滤）。</summary>
			private readonly Team _casterTeam;

			private Vec3 _position;
			private Vec3 _velocity;
			private float _elapsed;
			private float _travelled;
			private float _sinceCheck;
			private int _hitCount;

			/// <summary>还能弹几下（④ 命中裁决；0 = 撞到就结束）。</summary>
			private int _bouncesLeft;

			/// <summary>这一帧弹跳过（位置已由弹跳逻辑摆好，别再往前推）。</summary>
			private bool _bounced;

			/// <summary>② 定向：<c>orient_mode="velocity"</c> 时每帧按速度重算网格姿态。</summary>
			private readonly bool _orientByVelocity;

			// ⑤ 触发：一次性的事件各记"放过了没有"
			private bool _timerFired;
			private bool _splitDone;

			/// <summary>leave_surface 的"阶段 8 才生效"告警只打一次。</summary>
			private bool _leaveSurfaceWarned;

			/// <summary>已被打中过的人（防重复上报；穿透时也用来排除）。</summary>
			private readonly HashSet<int> _hitAgents = new HashSet<int>();

			private bool _ended;

			public Instance(SpellShot shot)
			{
				_shot = shot;
				_spell = shot.Intent.Spell;
				_position = shot.Intent.Origin;
				_velocity = shot.Intent.Velocity;
				_bouncesLeft = _spell.BounceCount > 0 ? _spell.BounceCount : 0;
				_orientByVelocity = string.Equals(_spell.OrientMode, "velocity", StringComparison.OrdinalIgnoreCase);
				_casterTeam = shot.Intent.Caster != null ? shot.Intent.Caster.Team : null;

				// 🔴 天降（start_height > 0）：不从手上飞出去，而是从**落点正上方**朝落点砸下来。
				//    瞄准轴只负责给落点，起落高度是投送的事（计划 §2.2 ProjectileStrike 行）。
				if (_spell.StartHeight > 0f)
				{
					// 🔴 别在 AimPoint 这个对象上就地改（Vec3 是引用类型，改了会把意图也改掉）
					Vec3 drop = new Vec3(shot.Intent.AimPoint.x, shot.Intent.AimPoint.y,
						shot.Intent.AimPoint.z + _spell.StartHeight);
					_position = drop;
					Vec3 down = shot.Intent.AimPoint - drop;
					down = down.LengthSquared < 1e-6f ? new Vec3(0f, 0f, -1f) : down.NormalizedCopy();
					// 速度同样走 EffectiveSpeed（初速被读的**另一处**，与 SpellAim.Emit 同口径）
					_velocity = down * MathF.Max(1f, _spell.EffectiveSpeed);
					// 天降的"飞满距离"从落点算起，不然它一下来就把寿命耗光
					_travelled = _spell.StartHeight;
				}

				float tilt = SpellDebug.TiltOverrideDeg ?? _spell.TiltDeg;
				Mat3 rotation = SpellMath.BuildFlightRotation(_velocity, tilt);
				_entity = SpellWorld.SpawnMeshEntity(_spell.Mesh, _position, rotation,
					SpellDebug.ScaleOverride ?? _spell.Scale);
				if (_entity != null)
				{
					_trail = SpellWorld.AttachParticle(_spell.TrailParticle, _entity);
				}
			}

			public bool Tick(float dt)
			{
				if (_ended)
				{
					return false;
				}

				_elapsed += dt;

				// ── ① 推进：转向 / 阻力 / 沿程加速 / 重力（属性只在这一处被读）──

				// 追踪（turn_rate > 0）：每帧把速度方向朝目标转一点（限速转弯）。
				// 目标优先取**目标引用**的位置（软锁锁的那个人现在在哪），没有就取瞄准点。
				// turn_delay = 起飞后先直飞一段再开始追（默认 0 = 立刻追，与旧行为一致）。
				if (_spell.TurnRate > 0f && _elapsed >= _spell.TurnDelay)
				{
					SteerTowardTarget(dt);
				}
				// 阻力：每秒按比例掉速度（0 = 不掉，默认）
				if (_spell.Drag > 0f)
				{
					_velocity *= MathF.Max(0f, 1f - _spell.Drag * dt);
				}
				// 沿程加速：沿**当前**飞行方向加（负数 = 边飞边慢）
				if (Math.Abs(_spell.Accel) > 1e-4f)
				{
					Vec3 heading = FlightDirection;
					_velocity += heading * (_spell.Accel * dt);
				}
				// 重力**带符号**：正 = 往下掉 · 0 = 平飞 · 负 = 往上飘（反重力）。
				// 🔴 2026-09-28 改：原来这里写的是 `-Math.Abs(gravity)` —— 负号被吃掉了，
				//    "反重力"根本不存在（只有"失重"）。计划 §16.1 的 Noita 对照表里
				//    反重力是 `gravity += / ×`，所以这条路必须真的通。
				if (Math.Abs(_spell.Gravity) > 1e-4f)
				{
					_velocity += new Vec3(0f, 0f, -_spell.Gravity * dt);
				}

				Vec3 next = _position + _velocity * dt;

				// ── ③④ 命中查询与裁决（每 0.05 秒一次，线段 = 上一次检查点 → 这一次检查点）──
				_sinceCheck += dt;
				bool alive = true;
				if (_sinceCheck >= CheckInterval)
				{
					_sinceCheck = 0f;
					alive = Sweep(_position, next);
				}

				// 弹跳那一拍**自己把位置摆在表面外**了（见 OnWorldHit）⇒ 这一帧别再往前推
				if (_bounced)
				{
					_bounced = false;
				}
				else
				{
					Vec3 step = next - _position;
					_travelled += step.Length;
					_position = next;
				}
				MoveEntity();

				if (!alive)
				{
					return false;
				}

				// ── ⑤ 触发：定时器 / 分裂（都发生在"还在飞"的时候）──
				if (!_timerFired && !string.IsNullOrEmpty(_spell.OnTimerCast)
					&& _spell.TimerSeconds > 0f && _elapsed >= _spell.TimerSeconds)
				{
					_timerFired = true;
					FireTrigger(_spell.OnTimerCast, _position, FlightDirection);
				}
				if (!_splitDone && _spell.SplitAt > 0f && _spell.SplitCount > 0
					&& _travelled >= _spell.SplitAt * MathF.Max(1f, _spell.MaxDistance))
				{
					_splitDone = true;
					SplitNow();
				}

				// ── ⑥ 到期：飞满距离或活满时间，先到先算 ──
				if (_travelled >= _spell.MaxDistance || _elapsed >= _spell.MaxLifetime)
				{
					FireTrigger(_spell.OnExpireCast, _position, FlightDirection);
					WarnLeaveSurface();
					return false;
				}
				return true;
			}

			/// <summary>当前飞行方向（零速度时退回世界前方，避免除零）。</summary>
			private Vec3 FlightDirection
			{
				get { return _velocity.LengthSquared < 1e-8f ? Vec3.Forward : _velocity.NormalizedCopy(); }
			}

			public void End()
			{
				if (_ended)
				{
					return;
				}
				_ended = true;
				try
				{
					if (_entity != null)
					{
						if (_trail != null)
						{
							_entity.RemoveComponent(_trail);
						}
						_entity.Remove(0);
					}
				}
				catch (Exception)
				{
					// 实体可能已被引擎回收 —— 正常
				}
				_entity = null;
				_trail = null;
			}

			/// <summary>
			/// 推进这一帧的视觉（只改原点 —— 直线飞行不会翻滚）。
			/// ② 定向：<c>orient_mode="velocity"</c> 时**每帧按速度方向重算姿态**（追踪弹才需要；
			/// 默认 <c>fixed</c> = 进入时算一次，与旧行为逐字一致）。
			/// </summary>
			private void MoveEntity()
			{
				if (_entity == null)
				{
					return;
				}
				try
				{
					MatrixFrame frame = _entity.GetGlobalFrame();
					frame.origin = _position;
					if (_orientByVelocity)
					{
						frame.rotation = SpellMath.BuildFlightRotation(_velocity,
							SpellDebug.TiltOverrideDeg ?? _spell.TiltDeg);
					}
					_entity.SetGlobalFrame(frame);
				}
				catch (Exception)
				{
					_entity = null;
				}
			}

			/// <summary>
			/// 追踪：把速度方向朝"目标现在的位置"转，**每帧最多转 turn_rate × dt 度**（限速转弯）。
			/// 目标位置取 <c>意图.Target</c>（软锁给的那个人）—— 拿不到就退化成瞄准点。
			/// 速度大小不变（只是转向，不加速）。
			/// </summary>
			private void SteerTowardTarget(float dt)
			{
				Vec3 toTarget;
				Agent target = _shot.Intent.Target;
				if (target != null && AgentControlHelper.SafeIsActive(target) && target.Health > 0f)
				{
					Vec3 aim = target.Position;
					aim.z += target.GetEyeGlobalHeight() * 0.5f;
					toTarget = aim - _position;
				}
				else
				{
					toTarget = _shot.Intent.AimPoint - _position;
				}
				float length = toTarget.Length;
				if (length < 0.05f)
				{
					return;
				}
				Vec3 desired = toTarget * (1f / length);
				float speed = _velocity.Length;
				if (speed < 1e-3f)
				{
					return;
				}
				Vec3 current = _velocity * (1f / speed);
				float maxTurn = _spell.TurnRate * (MathF.PI / 180f) * dt;   // 这一帧最多转这么多弧度
				float angle = MathF.Acos(MathF.Max(-1f, MathF.Min(1f, Vec3.DotProduct(current, desired))));
				if (angle <= maxTurn || angle < 1e-4f)
				{
					_velocity = desired * speed;
					return;
				}
				float t = maxTurn / angle;                                   // 朝目标方向插值（球面近似）
				Vec3 turned = current * (1f - t) + desired * t;
				turned = turned.LengthSquared < 1e-8f ? desired : turned.NormalizedCopy();
				_velocity = turned * speed;
			}

			/// <summary>扫掠这一小段：返回 false = 这一发到此为止。</summary>
			private bool Sweep(Vec3 from, Vec3 to)
			{
				Mission mission = Mission.Current;
				if (mission == null)
				{
					return true;
				}
				// 判定口径全在 SpellSweep 里（人物 / 墙地两条查询取近的那个 + 已命中排除
				// + 阶段 5 的三个旋钮：collide_agent / collide_terrain / hit_same_team）
				int exclude = _shot.Intent.Caster != null ? _shot.Intent.Caster.Index : -1;
				Agent victim;
				Vec3 point;
				Vec3 normal;
				if (!SpellSweep.FindNearestHit(mission, from, to, SpellDebug.HitRadiusOverride ?? _spell.HitRadius,
					exclude, _casterTeam, _hitAgents, _spell,
					out victim, out point, out normal))
				{
					return true;   // 这一段什么都没撞到
				}
				return victim != null ? OnAgentHit(victim) : OnWorldHit(point, normal);
			}

			private bool OnAgentHit(Agent victim)
			{
				_hitAgents.Add(victim.Index);
				_hitCount++;
				Vec3 direction = FlightDirection;
				Vec3 point = victim.Position;
				point.z += victim.GetEyeGlobalHeight() * 0.5f;   // 视觉上打在躯干，不是脚底

				SpellWorld.PlaySound(_spell.SoundHit, point);
				SpellWorld.BurstParticle(_spell.ImpactParticle, point);
				_shot.ReportHit(new SpellHit
				{
					Victim = victim,
					Position = point,
					Direction = direction,
					Caster = _shot.Intent.Caster,
					Power = _shot.Intent.Power,
					Spell = _spell,
				});
				// ⑤ 触发：打人的这一发算"命中"（术语口径见 OnWorldHit 的注释）
				FireTrigger(_spell.OnHitCast, point, direction);
				WarnLeaveSurface();

				// 还能穿透就继续飞（pierce = 最多能命中几个目标）
				return _hitCount < _spell.Pierce;
			}

			/// <summary>
			/// 撞到墙/地的那一击。三条出口按优先级：
			///   ① <c>drill</c>（钻地）—— 地形不当回事：不停、不报、继续飞（飞行距离照算）
			///   ② <c>bounce_count</c> 还有剩 —— 按命中法线**反射速度**、速度乘衰减、放"弹跳触发"
			///   ③ 否则 —— **这一发到此为止**：放命中触发 + 报一次结算
			/// 🔴 **触发口径**：<c>on_hit_cast</c> 只在"真的停下来的那一击"响；
			///   弹跳那一拍响的是 <c>on_bounce_cast</c>（否则一颗弹跳弹会把命中触发器连响 N 次）。
			/// </summary>
			private bool OnWorldHit(Vec3 point, Vec3 normal)
			{
				Vec3 direction = FlightDirection;

				// ① 钻地
				if (_spell.Drill)
				{
					return true;
				}

				// ② 弹跳
				if (_bouncesLeft > 0)
				{
					_bouncesLeft--;
					Vec3 n = normal.LengthSquared < 1e-8f ? -direction : normal.NormalizedCopy();
					Vec3 reflected = _velocity - n * (2f * Vec3.DotProduct(_velocity, n));
					_velocity = reflected * MathF.Max(0f, _spell.BounceDamping);
					// 从表面里挪出来一点，否则下一个检查点会立刻又撞同一面（贴着墙抖）
					_travelled += (point - _position).Length;
					_position = point + n * 0.05f;
					_bounced = true;
					SpellWorld.BurstParticle(_spell.ImpactParticle, point);
					FireTrigger(_spell.OnBounceCast, point, FlightDirection);
					// 弹得几乎没速度了 = 这一发到此为止（不然它会永远贴着地面蹭）
					return _velocity.LengthSquared > 1f;
				}

				// ③ 结束
				SpellWorld.PlaySound(_spell.SoundHit, point);
				SpellWorld.BurstParticle(_spell.ImpactParticle, point);
				_shot.ReportHit(new SpellHit
				{
					Victim = null,
					Position = point,
					Direction = direction,
					Caster = _shot.Intent.Caster,
					Power = _shot.Intent.Power,
					Spell = _spell,
				});
				FireTrigger(_spell.OnHitCast, point, direction);
				WarnLeaveSurface();
				return false;
			}

			/// <summary>
			/// ⑤ 触发：在触发点**派一条新的施法意图**（起点 = 投射物当时的位置，方向 = 继承飞行方向），
			/// 走的是**同一条 <see cref="SpellCastFlow"/>** ⇒ "召唤一颗新子弹 / 炸一片 / 留一地火"
			/// 全都是它（子法术自己能带范围、追踪、留地表，不需要为每种效果新造机制）。
			/// 🔴 子法术带 <c>Resolved</c> 标记 ⇒ **不会**再叠一遍施法者的配装（§16.4：子块独立解算）。
			/// </summary>
			private void FireTrigger(string subId, Vec3 position, Vec3 direction)
			{
				if (string.IsNullOrEmpty(subId))
				{
					return;
				}
				SpellTriggerPayload payload;
				if (!_spell.Triggers.TryGetValue(subId, out payload) || payload == null || payload.Spell == null)
				{
					if (_warnedTriggers.Add(_spell.Id + ":" + subId))
					{
						DebugLogger.Log($"[Spell] 法术 '{_spell.Id}' 的触发子块 '{subId}' 没有定义"
							+ " → 这一下不会响（触发器要由宝石的 <Sub id=\"{subId}\"> 提供）");
					}
					return;
				}
				SpellCastFlow.Cast(_shot.Intent.Caster, payload.Spell, position, direction,
					MathF.Max(0f, MathF.Min(1f, _shot.Intent.Power)));
			}

			/// <summary>⑤ 触发：分裂 —— 在当前位置摊出 N 发同款（子体不再分裂，防指数增长）。</summary>
			private void SplitNow()
			{
				int count = _spell.SplitCount > 0 ? _spell.SplitCount : 1;
				SpellDef child = SpellResolver.StripSplit(_spell);
				float spread = _spell.SpreadDeg > 0f ? _spell.SpreadDeg : 20f;   // 没配散布就给个默认扇面
				Vec3 direction = FlightDirection;
				for (int i = 0; i < count; i++)
				{
					Vec3 dir = count > 1 ? SpellAim.Scatter(direction, spread) : direction;
					SpellCastFlow.Cast(_shot.Intent.Caster, child, _position, dir,
						MathF.Max(0f, MathF.Min(1f, _shot.Intent.Power)));
				}
			}

			/// <summary>
			/// <c>leave_surface</c> 的读取点（阶段 5 只**登记 + 告警**）：地表系统属阶段 8（计划 §16.5 D 组），
			/// 现在没人会真的造出那片地表 —— 说一声，免得数据作者以为自己配错了。
			/// </summary>
			private void WarnLeaveSurface()
			{
				if (_leaveSurfaceWarned || string.IsNullOrEmpty(_spell.LeaveSurface))
				{
					return;
				}
				_leaveSurfaceWarned = true;
				DebugLogger.Log($"[Spell] 法术 '{_spell.Id}' 配了 leave_surface='{_spell.LeaveSurface}'，"
					+ "但地表系统属阶段 8 → 这次不会真的留下地表（字段已登记，等世界层接上）");
			}

			/// <summary>已报过"触发子块缺失"的法术+子块（防每帧刷屏）。</summary>
			private static readonly HashSet<string> _warnedTriggers = new HashSet<string>(StringComparer.Ordinal);
		}
	}

	/// <summary>
	/// 投送轴「放置 / 区域」（<c>delivery="place"</c>）：法术**留在落点上**，按间隔反复结算。
	///
	/// 与投射物的差别全在生命周期上（契约 2 的价值就在这）：
	///   投射物 = 造一个实体飞出去，撞到就没了；
	///   放置物 = 在落点生成一片**不动的区域**，活满 <c>duration</c>，每 <c>repeat_interval</c> 秒
	///            向结算上报一次命中（Victim = null = "打到这片地"，落点 = 区域中心）。
	///
	/// 🔴 **节流在投送手里**（契约 3）：结算（`area`）只管"收到一次命中就结算一次"，
	///   "多久报一次"是这里的事 —— 所以火墙是"每秒烧一次"，而不是"每帧烧一次秒杀"。
	/// 🔴 第一次生效延迟带随机抖动（`start_delay` + `start_delay_variance`，抄 FCS）：
	///   一次放好几个陷阱时不会整齐划一地一起响。
	/// </summary>
	internal sealed class PlaceDelivery : ISpellDelivery
	{
		public string Id
		{
			get { return "place"; }
		}

		public ISpellDeliveryInstance Begin(SpellShot shot)
		{
			if (shot == null || shot.Intent == null || shot.Intent.Spell == null)
			{
				return null;
			}
			return new Instance(shot);
		}

		private sealed class Instance : ISpellDeliveryInstance
		{
			private readonly SpellShot _shot;
			private readonly SpellDef _spell;
			private readonly Vec3 _center;

			private GameEntity _entity;
			private ParticleSystem _particle;

			private float _elapsed;
			private float _duration;
			private float _nextTick;
			private bool _ended;

			public Instance(SpellShot shot)
			{
				_shot = shot;
				_spell = shot.Intent.Spell;
				_center = shot.Intent.AimPoint;
				_duration = _spell.Duration > 0f ? _spell.Duration : _spell.MaxLifetime;
				float jitter = _spell.StartDelayVariance > 0f
					? (MBRandom.RandomFloat * 2f - 1f) * _spell.StartDelayVariance
					: 0f;
				_nextTick = MathF.Max(0.05f, _spell.StartDelay + jitter);

				// 区域实体：数据里的 mesh 躺在落点上（网格约定 = 平面法线本地 +Y，见 SpellMath.BuildGroundRotation）
				Mat3 rotation = SpellMath.BuildGroundRotation();
				_entity = SpellWorld.SpawnMeshEntity(_spell.Mesh, _center, rotation,
					SpellDebug.ScaleOverride ?? _spell.Scale);
				if (_entity != null)
				{
					_particle = SpellWorld.AttachParticle(_spell.TrailParticle, _entity);
				}
				SpellWorld.BurstParticle(_spell.ImpactParticle, _center);
				DebugLogger.Log($"[Spell] 放置 '{_spell.Id}' 落于 ({_center.x:F1},{_center.y:F1},{_center.z:F1})"
					+ $"，时长 {_duration:F1}s，每 {_spell.RepeatInterval:F1}s 结算一次，首次 {_nextTick:F2}s");
			}

			public bool Tick(float dt)
			{
				if (_ended)
				{
					return false;
				}
				_elapsed += dt;
				if (_elapsed >= _duration)
				{
					return false;
				}
				if (_elapsed >= _nextTick)
				{
					_nextTick = _elapsed + MathF.Max(0.05f, _spell.RepeatInterval);
					// 上报一次"打到这片地" —— 谁在这片区域里由 area 结算自己算
					_shot.ReportHit(new SpellHit
					{
						Victim = null,
						Position = _center,
						Direction = Vec3.Up,
						Caster = _shot.Intent.Caster,
						Power = _shot.Intent.Power,
						Spell = _spell,
					});
				}
				return true;
			}

			public void End()
			{
				if (_ended)
				{
					return;
				}
				_ended = true;
				try
				{
					if (_entity != null)
					{
						if (_particle != null)
						{
							_entity.RemoveComponent(_particle);
						}
						_entity.Remove(0);
					}
				}
				catch (Exception)
				{
					// 实体可能已被引擎回收 —— 正常
				}
				_entity = null;
				_particle = null;
			}
		}
	}

	/// <summary>
	/// 结算轴「状态」（<c>payloads="status"</c>）：命中后给目标挂一个持续伤害状态（燃烧 / 中毒 / 流血…）。
	/// 状态本体由 <see cref="SpellStatusManager"/> 每帧推进（跳伤害 + 视觉），这里只负责"挂上去"。
	/// 🔴 同一个目标 + 同一个状态 = **刷新时长，不叠伤害**（否则连打几发就是秒杀，计划 §3.2 契约 3 的同源问题）。
	/// </summary>
	internal sealed class StatusPayload : ISpellPayload
	{
		public string Id
		{
			get { return "status"; }
		}

		public void OnHit(SpellHit hit)
		{
			if (hit == null || hit.Victim == null || hit.Spell == null)
			{
				return;   // 打到地/墙不给状态（状态要挂在人身上）
			}
			if (string.IsNullOrEmpty(hit.Spell.StatusId) || hit.Spell.StatusDamage <= 0f)
			{
				return;
			}
			SpellStatusManager.Apply(hit.Victim, hit.Spell, hit.Caster, hit.Direction);
		}
	}

	/// <summary>
	/// 持续状态管理器（跳伤害 + 视觉）—— **任务范围内**的东西：场景一卸载就全部清掉
	/// （法术现在只在战场上用，跨存档的"地上留火"是战役层的事，等有战役层法术再说，见计划 §九-9）。
	/// 🔴 与玩家/NPC 无关：谁被挂上谁能跳伤害，走的是同一条 <see cref="SpellWorld.Damage"/>（铁律 18）。
	/// </summary>
	public static class SpellStatusManager
	{
		private sealed class Active
		{
			public Agent Victim;
			public SpellDef Spell;
			public Agent Caster;
			public Vec3 Direction;
			public float Damage;
			public float Interval;
			public float ExpiresAt;
			public float NextTick;
			public GameEntity Visual;
			public ParticleSystem Particle;
		}

		private static readonly List<Active> _active = new List<Active>();
		private static readonly List<Active> _pool = new List<Active>();

		/// <summary>挂一个新状态（同目标同法术 = 刷新时长）。</summary>
		public static void Apply(Agent victim, SpellDef spell, Agent caster, Vec3 direction)
		{
			if (victim == null || spell == null || !AgentControlHelper.SafeIsActive(victim))
			{
				return;
			}
			// 已经挂过同一个法术的状态 → 只把时间往后推（不叠伤害）
			// 🔴 比 **id 不比引用**：阶段 5 之后每一发都可能是"解算出来的副本"（配了宝石时），
			//    拿引用比 = 永远不相等 = 同一条法术连打几发就叠成秒杀（契约 3 的同源问题）。
			for (int i = 0; i < _active.Count; i++)
			{
				if (_active[i].Victim == victim && _active[i].Spell != null && _active[i].Spell.SameAs(spell))
				{
					_active[i].ExpiresAt = MissionTime() + spell.StatusDuration;
					_active[i].NextTick = MathF.Min(_active[i].NextTick,
						MissionTime() + MathF.Max(0.05f, spell.StatusInterval));
					return;
				}
			}

			Active status = _pool.Count > 0 ? _pool[_pool.Count - 1] : new Active();
			if (_pool.Count > 0)
			{
				_pool.RemoveAt(_pool.Count - 1);
			}
			float now = MissionTime();
			status.Victim = victim;
			status.Spell = spell;
			status.Caster = caster;
			status.Direction = direction;
			status.Damage = spell.StatusDamage;
			status.Interval = MathF.Max(0.05f, spell.StatusInterval);
			status.ExpiresAt = now + MathF.Max(0.1f, spell.StatusDuration);
			status.NextTick = now + status.Interval;
			status.Visual = null;
			status.Particle = null;

			// 视觉：一个跟着目标走的空实体 + 挂在它身上的粒子（照 FirearmFxLogic / 诊断件那套）
			if (!string.IsNullOrEmpty(spell.StatusParticle))
			{
				GameEntity entity = TryCreateEmpty(victim.Position);
				if (entity != null)
				{
					status.Visual = entity;
					status.Particle = SpellWorld.AttachParticle(spell.StatusParticle, entity);
				}
			}

			_active.Add(status);
			DebugLogger.Log($"[Spell] 状态 '{spell.StatusId}' 挂上 {victim.Name}(Idx={victim.Index})："
				+ $"每 {status.Interval:F1}s 跳 {status.Damage:F0}，持续 {spell.StatusDuration:F1}s");
		}

		/// <summary>每帧推进（由 <see cref="SpellProjectileLogic"/> 调）。</summary>
		public static void Tick(float dt)
		{
			if (_active.Count == 0)
			{
				return;
			}
			float now = MissionTime();
			for (int i = _active.Count - 1; i >= 0; i--)
			{
				Active status = _active[i];
				bool drop = status.Victim == null
					|| !AgentControlHelper.SafeIsActive(status.Victim)
					|| status.Victim.Health <= 0f
					|| now >= status.ExpiresAt;
				if (!drop && now >= status.NextTick)
				{
					status.NextTick = now + status.Interval;
					Vec3 position = status.Victim.Position;
					position.z += status.Victim.GetEyeGlobalHeight() * 0.5f;
					SpellWorld.Damage(status.Victim, status.Damage, status.Spell.DamageType,
						position, status.Direction, status.Caster);
				}
				if (!drop && status.Visual != null)
				{
					try
					{
						MatrixFrame frame = status.Visual.GetGlobalFrame();
						Vec3 at = status.Victim.Position;
						at.z += 0.9f;
						frame.origin = at;
						status.Visual.SetGlobalFrame(frame);
					}
					catch (Exception)
					{
						status.Visual = null;
					}
				}
				if (drop)
				{
					Release(i);
				}
			}
		}

		/// <summary>场景卸载 / 换场景时全清（由宿主在构造与卸载时调）。</summary>
		public static void Reset()
		{
			for (int i = _active.Count - 1; i >= 0; i--)
			{
				Release(i);
			}
			_active.Clear();
		}

		private static void Release(int index)
		{
			Active status = _active[index];
			_active.RemoveAt(index);
			try
			{
				if (status.Visual != null)
				{
					if (status.Particle != null)
					{
						status.Visual.RemoveComponent(status.Particle);
					}
					status.Visual.Remove(0);
				}
			}
			catch (Exception)
			{
				// 实体可能已被引擎回收 —— 正常
			}
			status.Victim = null;
			status.Spell = null;
			status.Caster = null;
			status.Visual = null;
			status.Particle = null;
			_pool.Add(status);
		}

		private static GameEntity TryCreateEmpty(Vec3 position)
		{
			Mission mission = Mission.Current;
			if (mission == null || mission.Scene == null)
			{
				return null;
			}
			try
			{
				GameEntity entity = GameEntity.CreateEmpty(mission.Scene, true);
				if (entity != null)
				{
					entity.SetGlobalFrame(new MatrixFrame(Mat3.Identity, position));
				}
				return entity;
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static float MissionTime()
		{
			return MBCommon.GetTotalMissionTime();
		}
	}

	/// <summary>
	/// 瞄准轴「自身为心」（<c>targeting="self"</c>）：法术以施法者为原点（光环、自爆、领域）。
	/// 落点 = 施法者自己，方向 = 世界朝上（投送按需自己决定往哪照）。
	/// </summary>
	internal sealed class SelfTargeting : ISpellTargeting
	{
		public string Id
		{
			get { return "self"; }
		}

		public void BuildIntents(SpellCastRequest request, List<SpellCastIntent> output)
		{
			if (request == null || request.Spell == null || request.Caster == null)
			{
				return;
			}
			Vec3 origin = request.Caster.Position;
			origin.z += request.Caster.GetEyeGlobalHeight() * 0.5f;
			SpellAim.Emit(request, origin, Vec3.Up, null, output);
		}
	}

	/// <summary>
	/// 投送轴「持续」（<c>delivery="channel"</c>）：**没有"飞出去"这回事，是一条一直在那儿的线**
	/// （彗星亚兹勒 / 雷电领域那一类）。由**起手轴的相位机**决定它什么时候结束（松手 / 被打断 / 没资源）。
	///
	/// 每 <c>repeat_interval</c> 秒沿朝向打一条射线：
	///   · 打到人 → 上报"命中那个人"（`damage` 结算打他、`status` 给他挂状态）
	///   · 打到墙/地 → 上报"打到那里"（爆散粒子 + `area` 结算打那一片）
	///   · 什么都没打到 → 上报"打在射程尽头"（同上，位置 = 射程末端）
	/// 🔴 这就是契约 3 的意义：**每帧都在照到人，但每 `repeat_interval` 才结算一次** ——
	///   不然每帧一次伤害 = 秒杀。
	/// </summary>
	internal sealed class ChannelDelivery : ISpellDelivery
	{
		public string Id
		{
			get { return "channel"; }
		}

		public ISpellDeliveryInstance Begin(SpellShot shot)
		{
			if (shot == null || shot.Intent == null || shot.Intent.Spell == null)
			{
				return null;
			}
			return new Instance(shot);
		}

		private sealed class Instance : ISpellDeliveryInstance
		{
			private readonly SpellShot _shot;
			private readonly SpellDef _spell;

			private float _elapsed;
			private float _nextTick;
			private bool _ended;

			// 🔴 弧的视觉（2026-09-25 加）：数据里的 `mesh` + `trail_particle` 拉成一条从起点到命中点的弧。
			//    引导投送以前**只做判定不画东西** —— 手里有球、命中处有爆散，中间那条线是空的。
			private GameEntity _arcEntity;
			private ParticleSystem _arcParticle;

			public Instance(SpellShot shot)
			{
				_shot = shot;
				_spell = shot.Intent.Spell;
				_nextTick = MathF.Max(0.05f, _spell.RepeatInterval);
				DebugLogger.Log($"[Spell] 引导 '{_spell.Id}' 开始（每 {_spell.RepeatInterval:F1}s 结算一次）");
			}

			public bool Tick(float dt)
			{
				if (_ended)
				{
					return false;
				}
				_elapsed += dt;
				if (_elapsed >= _spell.MaxLifetime)
				{
					return false;   // 硬上限（正常由起手轴松手结束）
				}
				UpdateArc();        // 视觉：每帧摆弧（不结算）
				if (_elapsed >= _nextTick)
				{
					_nextTick = _elapsed + MathF.Max(0.05f, _spell.RepeatInterval);
					Fire();
				}
				return true;
			}

			/// <summary>这一拍照到了什么（判定走共享的 SpellSweep）。</summary>
			private void Fire()
			{
				Vec3 origin = _shot.Intent.Origin;
				Vec3 direction = _shot.Intent.Direction;
				direction = direction.LengthSquared < 1e-8f ? Vec3.Forward : direction.NormalizedCopy();
				Vec3 end = origin + direction * _spell.MaxDistance;
				int exclude = _shot.Intent.Caster != null ? _shot.Intent.Caster.Index : -1;
				Agent victim;
				Vec3 point;
				bool hit = SpellSweep.FindNearestHit(Mission.Current, origin, end, _spell.HitRadius, exclude, null,
					out victim, out point);
				if (!hit)
				{
					point = end;   // 什么都没照到：结算落在射程尽头
				}

				if (victim == null && !string.IsNullOrEmpty(_spell.ImpactParticle))
				{
					SpellWorld.BurstParticle(_spell.ImpactParticle, point);
				}
				_shot.ReportHit(new SpellHit
				{
					Victim = victim,
					Position = point,
					Direction = direction,
					Caster = _shot.Intent.Caster,
					Spell = _spell,
					Power = _shot.Intent.Power,
				});
			}

			public void End()
			{
				if (_ended)
				{
					return;
				}
				_ended = true;
				ReleaseArc();
				DebugLogger.Log($"[Spell] 引导 '{_spell.Id}' 结束（持续 {_elapsed:F1}s）");
			}

			/// <summary>
			/// 弧的**视觉**：每帧沿朝向扫一次（与 <see cref="Fire"/> 同一条射线），把数据里的
			/// <c>mesh</c> 从起点拉到命中点、<c>trail_particle</c> 挂在这条网格上。
			///
			/// 🔴 **只做视觉，不结算** —— 伤害仍只在 <see cref="Fire"/> 那一拍上报（契约 3：
			///   每帧都在照到人，但每 <c>repeat_interval</c> 才结算一次）。
			/// 🔴 网格约定（`build_lightning_arc.py`）：**长轴 = 本地 +Z、长度基准 1 m、原点在起点端**
			///   ⇒ 摆法 = 起点 + 朝向命中点 + **把 Z 基向量乘上距离（米）**（"基向量长度即缩放"）。
			/// ⚠️ 网格只造一次、之后每帧只改帧（每帧重建会漏实体）。
			/// </summary>
			private void UpdateArc()
			{
				if (string.IsNullOrEmpty(_spell.Mesh))
				{
					return;   // 没配网格 = 这条法术不要弧（数据说不要就不要）
				}
				Vec3 origin = _shot.Intent.Origin;
				Vec3 direction = _shot.Intent.Direction;
				direction = direction.LengthSquared < 1e-8f ? Vec3.Forward : direction.NormalizedCopy();
				Vec3 end = origin + direction * _spell.MaxDistance;
				int exclude = _shot.Intent.Caster != null ? _shot.Intent.Caster.Index : -1;
				Agent victim;
				Vec3 point;
				bool hit = SpellSweep.FindNearestHit(Mission.Current, origin, end, _spell.HitRadius, exclude, null,
					out victim, out point);
				if (!hit)
				{
					point = end;
				}

				Vec3 delta = point - origin;
				float length = delta.Length;
				if (length < 0.05f)
				{
					return;   // 贴脸太短就不摆（旋转矩阵会退化）
				}

				if (_arcEntity == null)
				{
					_arcEntity = SpellWorld.SpawnMeshEntity(_spell.Mesh, origin, Mat3.Identity, 1f);
					if (_arcEntity == null)
					{
						return;
					}
					_arcParticle = SpellWorld.AttachParticle(_spell.TrailParticle, _arcEntity);
				}
				try
				{
					Mat3 rotation = SpellMath.BuildFlightRotation(delta * (1f / length), 0f);
					// 🔴 粗细 = 数据 `scale`（倍率），**不写死在网格里** —— 写死就得"每改一次粗细重导一次资产"。
					//    运行时还能 `custom.spell scale <x>` 临场调（SpellDebug.ScaleOverride 优先）。
					//    网格基准宽 0.12 m ⇒ scale 1 = 12 cm、scale 3 = 36 cm（传奇那种粗电柱往 3~5 走）。
					float width = SpellDebug.ScaleOverride ?? _spell.Scale;
					if (Math.Abs(width - 1f) > 0.001f && width > 0f)
					{
						rotation.s = rotation.s * width;   // X 基向量 = 横向
						rotation.f = rotation.f * width;   // Y 基向量 = 另一横向（十字双片）
					}
					rotation.u = rotation.u * length;      // 本地 Z 的基向量长度 = 弧长
					_arcEntity.SetGlobalFrame(new MatrixFrame(rotation, origin));
				}
				catch (Exception)
				{
					_arcEntity = null;      // 实体可能已被引擎回收 —— 下一帧重建
					_arcParticle = null;
				}
			}

			/// <summary>摘掉弧（实体 + 挂在它上面的粒子），照弹体那套收尾口径。</summary>
			private void ReleaseArc()
			{
				if (_arcEntity != null)
				{
					try
					{
						if (_arcParticle != null)
						{
							_arcEntity.RemoveComponent(_arcParticle);
						}
						_arcEntity.Remove(0);
					}
					catch (Exception)
					{
						// 实体可能已被引擎回收 —— 正常
					}
				}
				_arcEntity = null;
				_arcParticle = null;
			}
		}
	}

	/// <summary>
	/// 范围类结算的**敌我过滤**（area / aura 共用，与扫掠同一条口径）：
	/// <c>hit_same_team = false</c> 时连自己一起跳过 —— 不然"火球穿队友飞过去、爆开还是把他炸了"。
	/// 默认 true（打自己人）= 引擎现状，旧法术一行不变。
	/// </summary>
	internal static class SpellTeamFilter
	{
		public static bool Skip(SpellDef spell, Agent caster, Agent agent)
		{
			if (spell == null || spell.HitSameTeam || caster == null || agent == null)
			{
				return false;
			}
			return caster.Team != null && agent.Team == caster.Team;
		}
	}

	/// <summary>
	/// 结算轴「光环」（<c>payloads="aura"</c>）：以**施法者自己为圆心**把半径内的人一起打
	/// （雷电领域 / 大地加护那一类）。与 <c>area</c> 的区别只在圆心 —— area 以命中点为心，aura 以施法者为心。
	/// 有 <c>status</c> 就顺带给半径内每个人挂上（"站在圈里持续被烧/被电"）。
	/// 🔴 与 area 同款纪律：**粗筛用 <c>Mission.GetNearbyAgents</c>**，不遍历全场 agent。
	/// </summary>
	internal sealed class AuraPayload : ISpellPayload
	{
		private static readonly MBList<Agent> _nearby = new MBList<Agent>();

		public string Id
		{
			get { return "aura"; }
		}

		public void OnHit(SpellHit hit)
		{
			if (hit == null || hit.Spell == null || hit.Caster == null)
			{
				return;
			}
			SpellDef spell = hit.Spell;
			float radius = spell.Radius;
			if (radius <= 0f)
			{
				return;
			}
			// 伤害走唯一入口（蓄力 / 浮动 / 暴击都在里面）；**暴击每个人各掷一次**
			Mission mission = Mission.Current;
			if (mission == null)
			{
				return;
			}
			Vec3 center = hit.Caster.Position;
			center.z += hit.Caster.GetEyeGlobalHeight() * 0.5f;
			try
			{
				_nearby.Clear();
				mission.GetNearbyAgents(new Vec2(center.x, center.y), radius, _nearby);
				for (int i = 0; i < _nearby.Count; i++)
				{
					Agent agent = _nearby[i];
					if (agent == null || !AgentControlHelper.SafeIsActive(agent) || agent.Health <= 0f)
					{
						continue;
					}
					if (SpellTeamFilter.Skip(spell, hit.Caster, agent))
					{
						continue;
					}
					Vec3 delta = agent.Position - center;
					float distanceSq = delta.LengthSquared;
					if (distanceSq > radius * radius)
					{
						continue;
					}
					float t = MathF.Sqrt(distanceSq) / radius;
					float falloff = 1f - spell.RadiusFalloff * t;
					if (falloff <= 0f)
					{
						continue;
					}
					if (spell.Damage > 0f)
					{
						bool crit;
						float damage = SpellDamage.Roll(spell, hit.Power, out crit) * falloff;
						if (crit)
						{
							DebugLogger.Log($"[Spell] 暴击！'{spell.Id}' 光环打 {agent.Name} {damage:F1}");
						}
						SpellWorld.Damage(agent, damage, spell.DamageType, agent.Position, delta, hit.Caster);
					}
					if (!string.IsNullOrEmpty(spell.StatusId) && spell.StatusDamage > 0f)
					{
						SpellStatusManager.Apply(agent, spell, hit.Caster, delta);
					}
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Spell] 光环结算异常：{ex.GetType().Name} {ex.Message}");
			}
		}
	}

	/// <summary>
	/// 结算轴「单体伤害」（<c>payloads="damage"</c>）：命中一个人就按数据里的伤害值打他。
	/// 打到墙/地（Victim = null）什么都不做 —— 那是 <see cref="AreaPayload"/> 的事。
	/// </summary>
	internal sealed class DamagePayload : ISpellPayload
	{
		public string Id
		{
			get { return "damage"; }
		}

		public void OnHit(SpellHit hit)
		{
			if (hit == null || hit.Victim == null || hit.Spell == null)
			{
				return;
			}
			bool crit;
			float damage = SpellDamage.Roll(hit.Spell, hit.Power, out crit);
			if (crit)
			{
				DebugLogger.Log($"[Spell] 暴击！'{hit.Spell.Id}' 打 {hit.Victim.Name} {damage:F1}"
					+ $"（{hit.Spell.Crit:P0} × {hit.Spell.CritMult:0.#}）");
			}
			SpellWorld.Damage(hit.Victim, damage, hit.Spell.DamageType,
				hit.Position, hit.Direction, hit.Caster);
		}
	}

	/// <summary>
	/// 结算轴「半径伤害」（<c>payloads="area"</c>）：以命中点为圆心，把半径内的人一起打到。
	///
	/// 🔴 **不重复打"直接被命中的那位"**：单体伤害归 <c>damage</c> 载荷管。
	///   否则一条 <c>damage=60 radius=3.5</c> 的法术会让被打中的人吃两份伤害（80/20 的配表直觉会错）。
	/// 🔴 **不遍历全场 agent**：用 <c>Mission.GetNearbyAgents</c>（1 次原生调用，引擎内部走空间索引）
	///   粗筛出附近的人，再对筛出来的做精算。战场上几百上千个 agent 时这是唯一的活路（计划 §4.4 纪律 1/2）。
	/// 边缘衰减：距离 t（0~1）× `radius_falloff` ⇒ 边缘保留 <c>1 − falloff</c> 的伤害。
	/// </summary>
	internal sealed class AreaPayload : ISpellPayload
	{
		/// <summary>粗筛结果缓冲（复用，不在热路径 new —— 计划 §4.4 纪律 4）。</summary>
		private static readonly MBList<Agent> _nearby = new MBList<Agent>();

		public string Id
		{
			get { return "area"; }
		}

		public void OnHit(SpellHit hit)
		{
			if (hit == null || hit.Spell == null || hit.Spell.Radius <= 0f)
			{
				return;
			}
			Mission mission = Mission.Current;
			if (mission == null)
			{
				return;
			}
			SpellDef spell = hit.Spell;
			float radius = spell.Radius;
			try
			{
				_nearby.Clear();
				mission.GetNearbyAgents(new Vec2(hit.Position.x, hit.Position.y), radius, _nearby);
				for (int i = 0; i < _nearby.Count; i++)
				{
					Agent agent = _nearby[i];
					if (agent == null || !AgentControlHelper.SafeIsActive(agent) || agent.Health <= 0f)
					{
						continue;
					}
					if (hit.Victim != null && agent == hit.Victim)
					{
						continue;
					}
					if (SpellTeamFilter.Skip(spell, hit.Caster, agent))
					{
						continue;
					}
					Vec3 delta = agent.Position - hit.Position;
					float distanceSq = delta.LengthSquared;
					if (distanceSq > radius * radius)
					{
						continue;
					}
					float t = MathF.Sqrt(distanceSq) / radius;          // 0 = 正中，1 = 边缘
					float factor = 1f - spell.RadiusFalloff * t;
					if (factor <= 0f)
					{
						continue;
					}
					// 伤害走唯一入口（蓄力 / 浮动 / 暴击都在里面）；**暴击每个人各掷一次**
					bool crit;
					float damage = SpellDamage.Roll(spell, hit.Power, out crit) * factor;
					if (crit)
					{
						DebugLogger.Log($"[Spell] 暴击！'{spell.Id}' 半径伤害打 {agent.Name} {damage:F1}");
					}
					SpellWorld.Damage(agent, damage, spell.DamageType,
						agent.Position, delta, hit.Caster);
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Spell] 半径伤害异常：{ex.GetType().Name} {ex.Message}");
			}
		}
	}
}
