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

		/// <summary>
		/// 飞行速度（米/秒）。UE 参考工程换算 ≈ 33~35（12 米用 0.36 秒飞完）。
		/// 🔴 2026-10-04 抬到 42：动画时间轴要求"**0.5 秒前钩头应已命中拉直**"（用户定的节点），
		///    而钩头实际飞 `距离 + 0.6` 米（见 Launch 的射程）⇒ 20 米射程 ÷ 42 ≈ 0.49 秒 ✓（35 时是 0.59，超节点）。
		/// </summary>
		public static float Speed = 42f;

		/// <summary>命中射线的粗细（米）。给人用的连续碰撞；对墙/地那条查询恒用细射线（0.01）。</summary>
		public static float HitRadius = 0.35f;

		/// <summary>碰撞检查间隔（秒）。**不是每帧** —— 省原生调用（法术那边的纪律）。</summary>
		public static float CheckInterval = 0.05f;

		/// <summary>钩头网格候选（铁律 5 两轮策略：逐个试，全不行 = 隐形飞，不崩）。
		/// 🔴 2026-10-07 用户选定形状：原版那把**纯三爪钩**（尾环在原点、钩体沿 +Z、0.664 m）。
		///    ⚠️ **别直接引用原版 `hook`** —— 它在包里是**场景道具**那一类资产，运行时按名字取不到
		///       （实机日志：`网格 'hook' 查不到` ⇒ 实体是空壳 = 手里看不见钩）。
		///       所以把它转成我们自己的资产 `lwn_grapple_hook`（生成器 `tools/grapple-model/scripts/build_hook_from_obj.py`，
		///       坐标原样搬、往返自检 0 偏差），由用户在 ModKit 导入后即可按名字引用。
		///    ⚠️ `grappling_hook`（绳圈+钩混装）与 `kitchen_hook`（吊钩）也不行：前者不是纯钩、后者同样取不到。
		///    运行时可用 `custom.grapple hmesh &lt;网格名&gt;` / `hscale &lt;倍率&gt;` 现场换、现场调（不用重编译）。</summary>
		public static string[] MeshCandidates = { "lwn_grapple_hook", "push_fork" };

		/// <summary>钩头网格缩放。🔴 **默认 1.0** —— 尺寸已经**烘进资产**（`lwn_grapple_hook.fbx` 实测
		/// 0.107 × 0.114 × **0.199 m**，即 20 cm 的钩）。想看大/小用 `custom.grapple hscale &lt;倍率&gt;` 现场调。
		/// （2026-10-07 修：早前资产是 0.664 m 的原版尺寸、靠这里的 0.30 缩到 20 cm —— 那种"隐藏的 0.3 倍"
		///   让编辑器预览与实机对不上，已改为烘进资产。）</summary>
		public static float MeshScale = 1.0f;

		/// <summary>网格自身沿 +Z 的长度（米，**缩放前**）—— 摆位时把"钩尖"顶到点上，而不是把"尾环"顶上去
		/// （否则钩会有一截埋进墙里）。资产实测：尾环在原点、钩尖在 +Z **0.175 m** 处（已含烘进去的缩放）。</summary>
		public static float MeshLengthZ = 0.175f;

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
			float remaining = _maxDistance - _travelled;

			// 🔴🔴 **本帧会到达/越过终点 ⇒ 只飞到终点，并强制做一次碰撞检查**（2026-10-03 实机定死的 bug）：
			//    碰撞检查是限频的（每 0.05 秒 ≈ 1.75 米一段），而**最后一段**原来永远没人查 ——
			//    检查段是"上次检查点 → 本次新位置"，可钩头在到达终点那一帧就直接被判"打空"，
			//    下一检查点（更远）根本没机会执行 ⇒ **落在最后一小段（最多 1.75 米）里的目标必然判定打空**
			//    （实机：瞄 12.3 米的目标，检查段只覆盖到 12.25 米 → 飞满 12.9 米报"打空"，绳随即收掉）。
			//    ⇒ 到达终点这一帧把位置钳到终点、并且**无条件**扫最后一段 [_checkFrom → 终点]。
			bool reachedEnd = step >= remaining;
			if (reachedEnd)
			{
				step = MathF.Max(0f, remaining);
			}
			Vec3 next = Position + _dir * step;
			_travelled += step;

			// ── 碰撞检查（限频；到达终点那一帧强制做一次）──
			// 🔴 线段 = **上一次检查点 → 这次的新位置**（不是"本帧起止"）——
			//    两次检查之间飞过的距离（35 m/s × 0.05s ≈ 1.75 米）也必须落在某条线段里，
			//    否则薄墙/薄人会被整段跳过去（法术那边同款口径）。
			_checkTimer += dt;
			if (_checkTimer >= CheckInterval || reachedEnd)
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

			if (reachedEnd)
			{
				DebugLogger.Log($"[Grapple] 钩头打空（飞满 {_maxDistance:F1} 米，末段已查）@ {Fmt(Position)}");
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
			_parked = false;              // 手里待命态一并清掉（发射前会先 Clear）
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

		/// <summary>摆到当前位置。
		/// · **飞行 / 钉住**：局部 Z = 飞行方向（用绳那套已验证的基底，别用 CreateMat3WithForward），
		///   并沿飞行方向**后退一个钩长** —— 让**钩尖**落在 <see cref="Position"/> / 命中点上
		///   （否则钩有一截埋进墙里、看着"环在前钩在后"）。
		/// · **手里待命**（<see cref="_parked"/>）：位置与朝向由外部每帧喂（见 <see cref="Park"/>），
		///   **不做钩尖补偿** —— 待命时要的是**尾环**正好落在绳端上（绳就系在环上）。
		/// 🔴 三爪钩绕自身轴每 120° 对称 ⇒ 滚转随便取，`BasisWithLocalZ` 的 roll 不用管。</summary>
		private void MoveEntity()
		{
			if (_entity == null || _entity.Pointer == UIntPtr.Zero)
			{
				return;
			}
			try
			{
				Vec3 pos;
				Vec3 zdir;
				if (_parked)
				{
					pos = _parkPos;
					zdir = _parkDir;
					if (_parkIdentity)
					{
						// 诊断模式：**不转**，与 `custom.spawn_mesh` 的 `Mat3.Identity` 完全同款
						MatrixFrame idf = new MatrixFrame(Mat3.Identity, pos);
						idf.Scale(new Vec3(MeshScale, MeshScale, MeshScale));
						_entity.SetGlobalFrame(idf);
						return;
					}
				}
				else
				{
					zdir = _dir;
					pos = (State == Phase.Flying || State == Phase.Attached)
						? Position - _dir * (MeshLengthZ * MeshScale)
						: Position;
				}
				if (zdir.LengthSquared < 1e-8f)
				{
					return;   // 方向退化 = 基向量算不出来，这一帧不摆（下一帧再说）
				}
				MatrixFrame frame = new MatrixFrame(GrappleRope.BasisWithLocalZ(zdir), pos);
				frame.Scale(new Vec3(MeshScale, MeshScale, MeshScale));
				_entity.SetGlobalFrame(frame);
			}
			catch (Exception)
			{
				// 单帧摆位失败不拖垮飞行
			}
		}

		// ───────────────── 手里待命（设计 B，2026-10-07）：钩停在手上、绕手转 ─────────────────

		private bool _parked;
		private Vec3 _parkPos;
		private Vec3 _parkDir;
		private bool _parkIdentity;      // 诊断：用 Identity 朝向（= custom.spawn_mesh 同款）而不是径向朝向

		/// <summary>
		/// **手里待命**：把实体停在 <paramref name="pos"/>（= 绳端 / 尾环位置）、钩尖朝 <paramref name="zdir"/>
		/// （待命时 = 离心方向）。位置由 <see cref="GrappleLogic"/> 每帧算好喂进来。
		/// 发射时不需要先 Unpark —— <see cref="Launch"/> 会先 <see cref="Clear"/>（顺带把待命态清掉）。
		/// </summary>
		public void Park(Scene scene, Vec3 pos, Vec3 zdir, bool applyFrameEveryTick = true, bool identityRotation = false)
		{
			_parkPos = pos;
			_parkDir = zdir;
			_parkIdentity = identityRotation;
			bool wasParked = _parked;
			_parked = true;
			if (_entity == null || _entity.Pointer == UIntPtr.Zero)
			{
				SpawnEntity(scene);          // 飞行收尾 Clear() 会把实体拆掉 ⇒ 待命期按需重建
				wasParked = false;
			}
			ShowEntity();
			// 🔴 `applyFrameEveryTick = false` = **只在建出来那一刻摆一次**，之后不再动 ——
			//    与 `custom.spawn_mesh`（已知能正常渲染）完全同款，用来二分"每帧摆位是不是元凶"。
			if (applyFrameEveryTick || !wasParked)
			{
				MoveEntity();
			}
		}

		/// <summary>收掉手里那枚（钩索不在手上 / 换武器 / 关开关）。实体留着，下次 <see cref="Park"/> 复用。</summary>
		public void Unpark()
		{
			if (!_parked)
			{
				return;
			}
			_parked = false;
			HideEntity();
		}

		private void ShowEntity()
		{
			if (_entity == null || _entity.Pointer == UIntPtr.Zero)
			{
				return;
			}
			try
			{
				_entity.SetVisibilityExcludeParents(true);
			}
			catch (Exception)
			{
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

		/// <summary>诊断用：当前手里待命那枚实体的状态（网格名 / 实体是否建出来）。</summary>
		public string ParkState()
		{
			string meshName = "(未知)";
			try
			{
				MetaMesh m = ResolveHookMesh();
				meshName = m != null ? MeshCandidates[0] + " ✓" : "**全部查不到 = 空实体**";
			}
			catch (Exception)
			{
			}
			bool alive = _entity != null && _entity.Pointer != UIntPtr.Zero;
			return $"parked={_parked} entity={(alive ? "已建" : "null")} mesh={meshName} pos=({_parkPos.x:F2},{_parkPos.y:F2},{_parkPos.z:F2})";
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
