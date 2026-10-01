using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **钩头 = 自管实体投射物**（方案 = plans\钩索-实施计划.md §3.2；配方 = wheels.d/spells.md「自管实体投射物」）。
	///
	/// 为什么不走引擎导弹：引擎导弹约 110 米必被剔除网格（法术那边实测），而且我们要**勾住不放**——
	/// 引擎导弹命中即消失，给不了"钉在墙上"这个状态。
	///
	/// 飞行：直线 + 每 <see cref="CheckInterval"/> 秒做一次"连续碰撞"扫掠（线段 = 上次检查点 → 这次检查点，
	/// 天然不会因为一帧跨几米而穿人/穿墙），命中判定复用现成的共享件 <see cref="SpellSweep.FindNearestHit"/>：
	/// ① 打到谁（引擎空间索引）② 打到墙/地 —— 两条各给距离，**取近的那个**。
	///
	/// 🔴 与法术投射物的差异（别照抄那边）：
	///   · 命中**不消失** —— 停在命中点进入 <see cref="Phase.Attached"/>，绳跟着钉住；
	///   · 没有弹道下坠 / 寿命 / 伤害 —— 它是"钩"，不是"弹"；
	///   · 网格解析走 <see cref="SpellWorld.ResolveMesh"/>（两轮兜底 + 整局缓存），**别每发 GetCopy**。
	/// </summary>
	internal sealed class GrappleHook
	{
		public enum Phase
		{
			Idle,
			Flying,
			Attached,
		}

		/// <summary>Tick 的返回：这一步发生了什么。</summary>
		public enum StepResult
		{
			None,        // 什么都没发生（还在飞 / 已停）
			Flying,      // 这一帧在飞（位置已更新）
			HitWorld,    // 这一步撞到墙/地并已钉住（Position = 命中点）
			HitAgent,    // 这一步打到人并已挂住（AttachedAgent 非空）
			Missed,      // 飞满了 max_distance 什么都没碰到
		}

		// ───────────────────────────── 可调参数（命令改） ─────────────────────────────

		/// <summary>飞行速度（米/秒）。UE 参考工程换算 ≈ 33~35（12 米用 0.36 秒飞完）。</summary>
		public static float Speed = 35f;

		/// <summary>命中射线的粗细（米）。给人用的连续碰撞；对墙/地那条查询恒用细射线（0.01）。</summary>
		public static float HitRadius = 0.35f;

		/// <summary>碰撞检查间隔（秒）。**不是每帧** —— 省原生调用（法术那边的纪律）。</summary>
		public static float CheckInterval = 0.05f;

		/// <summary>占位网格候选（铁律 5 两轮策略：逐个试，全不行 = 隐形飞，不崩）。第一版是**原版推草叉**（双齿，形似钩爪）。</summary>
		public static string[] MeshCandidates = { "push_fork", "bolt_bl_a", "throwing_stone" };

		/// <summary>占位网格缩放（推草叉偏大，缩小到像"钩头"）。</summary>
		public static float MeshScale = 0.35f;

		// ───────────────────────────── 状态 ─────────────────────────────

		public Phase State { get; private set; } = Phase.Idle;

		/// <summary>当前世界位置（飞行中 = 钩头；Attached = 钉住点）。</summary>
		public Vec3 Position { get; private set; }

		/// <summary>勾到的人（勾到墙时为空）。</summary>
		public Agent AttachedAgent { get; private set; }

		/// <summary>命中点（Attached 后有效，供落点解算与绳用）。</summary>
		public Vec3 AttachedPoint { get; private set; }

		private GameEntity _entity;
		private Vec3 _dir;
		private float _travelled;
		private float _maxDistance;
		private float _checkTimer;
		private Vec3 _checkFrom;                  // 上一次碰撞检查的位置（线段 = 它 → 本次位置，天然连续）
		private bool _meshWarned;

		// ───────────────────────────── 发射 / 推进 / 收掉 ─────────────────────────────

		/// <summary>
		/// 从 <paramref name="from"/> 朝 <paramref name="dir"/> 发射，最多飞 <paramref name="maxDistance"/> 米。
		/// 返回 false = 参数不合法（没发射，状态保持原样）。
		/// </summary>
		public bool Launch(Scene scene, Vec3 from, Vec3 dir, float maxDistance)
		{
			if (scene == null || dir.LengthSquared < 1e-6f || maxDistance <= 0.1f)
			{
				return false;
			}

			Clear();                                     // 幂等：上一根先收掉

			_dir = dir.NormalizedCopy();
			Position = from;
			_checkFrom = from;
			_travelled = 0f;
			_maxDistance = maxDistance;
			_checkTimer = 0f;
			AttachedAgent = null;
			AttachedPoint = from;
			State = Phase.Flying;

			SpawnEntity(scene);
			MoveEntity();
			return true;
		}

		/// <summary>
		/// 推进一帧。命中即钉住（<see cref="StepResult.HitWorld"/> / <see cref="StepResult.HitAgent"/>）。
		/// <paramref name="excludeAgentIndex"/> 一般是玩家自己的 Index（别打到自己）。
		/// </summary>
		public StepResult Tick(Mission mission, float dt, int excludeAgentIndex)
		{
			if (State != Phase.Flying)
			{
				return StepResult.None;
			}
			if (mission == null || dt <= 0f)
			{
				return StepResult.None;
			}

			float step = Speed * dt;
			Vec3 next = Position + _dir * step;
			_travelled += step;

			// ── 碰撞检查（限频）──
			// 🔴 线段 = **上一次检查点 → 这次的新位置**（不是"本帧起止"）——
			//    两次检查之间飞过的距离（35 m/s × 0.05s ≈ 1.75 米）也必须落在某条线段里，
			//    否则薄墙/薄人会被整段跳过去（法术那边同款口径）。
			_checkTimer += dt;
			if (_checkTimer >= CheckInterval)
			{
				_checkTimer = 0f;

				Agent victim;
				Vec3 point;
				bool hit = false;
				try
				{
					hit = SpellSweep.FindNearestHit(mission, _checkFrom, next, HitRadius,
						excludeAgentIndex, null, out victim, out point);
				}
				catch (Exception)
				{
					victim = null;
					point = next;
				}
				_checkFrom = next;

				if (hit)
				{
					Position = point;
					AttachedPoint = point;
					AttachedAgent = victim;
					State = Phase.Attached;
					MoveEntity();
					if (victim != null)
					{
						DebugLogger.Log($"[Grapple] 钩头勾住人：{victim.Name} @ {Fmt(point)}");
						return StepResult.HitAgent;
					}
					DebugLogger.Log($"[Grapple] 钩头勾住地形 @ {Fmt(point)}");
					return StepResult.HitWorld;
				}
			}

			Position = next;
			MoveEntity();

			if (_travelled >= _maxDistance)
			{
				DebugLogger.Log($"[Grapple] 钩头打空（飞满 {_maxDistance:F1} 米）@ {Fmt(Position)}");
				State = Phase.Idle;
				HideEntity();
				return StepResult.Missed;
			}

			return StepResult.Flying;
		}

		/// <summary>收掉（实体拆掉；下次发射重建）。</summary>
		public void Clear()
		{
			RemoveEntity();
			State = Phase.Idle;
			AttachedAgent = null;
			_travelled = 0f;
			_checkTimer = 0f;
		}

		// ───────────────────────────── 实体 ─────────────────────────────

		private void SpawnEntity(Scene scene)
		{
			try
			{
				MetaMesh mesh = ResolveHookMesh();
				_entity = GameEntity.CreateEmpty(scene, true);
				if (_entity == null)
				{
					return;
				}
				if (mesh != null)
				{
					_entity.AddMultiMesh(mesh, true);
				}
				_entity.SetVisibilityExcludeParents(true);
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 钩头实体生成异常: {ex.GetType().Name} {ex.Message}");
				_entity = null;
			}
		}

		/// <summary>摆到当前位置（局部 Z = 飞行方向 —— 用绳那套已验证的基底，别用 CreateMat3WithForward）。</summary>
		private void MoveEntity()
		{
			if (_entity == null || _entity.Pointer == UIntPtr.Zero)
			{
				return;
			}
			try
			{
				MatrixFrame frame = new MatrixFrame(GrappleRope.BasisWithLocalZ(_dir), Position);
				frame.Scale(new Vec3(MeshScale, MeshScale, MeshScale));
				_entity.SetGlobalFrame(frame);
			}
			catch (Exception)
			{
				// 单帧摆位失败不拖垮飞行
			}
		}

		private void HideEntity()
		{
			if (_entity == null || _entity.Pointer == UIntPtr.Zero)
			{
				return;
			}
			try
			{
				_entity.SetVisibilityExcludeParents(false);
			}
			catch (Exception)
			{
			}
		}

		private void RemoveEntity()
		{
			if (_entity == null)
			{
				return;
			}
			try
			{
				if (_entity.Pointer != UIntPtr.Zero)
				{
					_entity.Remove(0);
				}
			}
			catch (Exception)
			{
			}
			_entity = null;
		}

		/// <summary>占位网格：候选表逐个试（铁律 5 两轮策略的第一轮；第二轮 = ResolveMesh 内部的动态兜底）。</summary>
		private MetaMesh ResolveHookMesh()
		{
			for (int i = 0; i < MeshCandidates.Length; i++)
			{
				MetaMesh mesh = SpellWorld.ResolveMesh(MeshCandidates[i]);
				if (mesh != null)
				{
					return mesh;
				}
			}
			if (!_meshWarned)
			{
				_meshWarned = true;
				DebugLogger.Log("[Grapple] 钩头占位网格全部查不到 —— 钩头隐形飞（机制不受影响，检查网格名）");
			}
			return null;
		}

		// ───────────────────────────── 诊断 ─────────────────────────────

		public string Describe()
		{
			return string.Format("hook={0} pos=({1:F2},{2:F2},{3:F2}) flown={4:F1}/{5:F1}",
				State, Position.x, Position.y, Position.z, _travelled, _maxDistance);
		}

		private static string Fmt(Vec3 v)
		{
			return string.Format("({0:F2},{1:F2},{2:F2})", v.x, v.y, v.z);
		}
	}
}
