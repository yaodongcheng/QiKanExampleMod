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
				// 手里待命走 ParkBasis（可按 hookface / hookroll 现场翻朝向）；飞行/钉住仍用纯径向基底
				Mat3 basis = _parked ? ParkBasis(zdir) : GrappleRope.BasisWithLocalZ(zdir);
				MatrixFrame frame = new MatrixFrame(basis, pos);
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
		/// **手里待命时"哪个物体局部轴朝离心向外"**（`custom.grapple hookface &lt;0..3&gt;`；0=+Z · 1=−Z · 2=+Y · 3=−Y）。
		///
		/// 🔴🔴 **引擎空间的真值（2026-10-08 定案）**：钩的**长轴是局部 +Y**（长 0.199 m、尾环在原点、钩尖 +0.175），
		/// 不是 +Z。证据 = `tpaccli dump` 装机包（`Debug/offline/_hookdump/`）：X ±0.054 · **Y −0.024..+0.175** · Z ±0.057。
		/// ⚠️ **我先前判成"+Z"是两处错**：① 信了 FBX/Blender 侧的读数（那条路会做轴换算）；② 后来 dump 出 Y 长，
		/// 又自己套了个"OBJ 是 Y-up、要换回 Z-up"的假设把答案改回去 —— **tpaccli 读的是编译后的网格、就是引擎口径，不用换**。
		/// ⇒ **凡"朝向/摆位"结论一律以 tpaccli/装机包为准，FBX/Blender 侧只当中间产物。**
		///
		/// 🔴 **默认 = 3（−Y 朝外）+ <see cref="HandRollDeg"/> 180**（用户实机逐个试出来的"对的形态"，2026-10-08）：
		/// 效果 = 长轴(+Y)径向向外（钩尖朝外、尾环朝内）+ 局部 +Z 指向圆的**切向正向**。
		/// 中间那半天我在 4 档 × 任意滚转里让用户自己摸 —— 这是流程错误，正确做法见下面 `hookface` 命令的说明。
		/// </summary>
		public static int HandFaceMode = 3;

		/// <summary>手里待命时**绕"离心向外"那根轴滚转**（度，`custom.grapple hookroll &lt;度&gt;`）——
		/// 三爪钩的爪朝哪个方向弯（观感项）；飞行时不受影响。
		/// 🔴 **默认 180**（2026-10-08 用户实测定稿：`hookface 3` + `hookroll 180` 才是对的形态）。</summary>
		public static float HandRollDeg = 180f;

		/// <summary>待命朝向的基底：先按 <see cref="HandFaceMode"/> 选"哪个物体局部轴朝外"，再按 <see cref="HandRollDeg"/> 绕该轴滚转。</summary>
		private static Mat3 ParkBasis(Vec3 outward)
		{
			// BasisWithLocalZ 给的 (s,f,u) 满足 u = outward、三者正交单位 ⇒ 直接拿来做轴交换/滚转的地基
			Mat3 b = GrappleRope.BasisWithLocalZ(outward);
			Mat3 m = Mat3.Identity;
			switch (HandFaceMode)
			{
				case 1:      // −Z 朝外（整体翻转 180°：绕 s 轴转）
					m.s = b.s;
					m.f = -b.f;
					m.u = -b.u;
					break;
				case 2:      // +Y 朝外（绕 s 轴转 90°：u→f、f→−u）
					m.s = b.s;
					m.f = b.u;
					m.u = -b.f;
					break;
				case 3:      // −Y 朝外
					m.s = b.s;
					m.f = -b.u;
					m.u = b.f;
					break;
				default:     // +Z 朝外（资产实测口径，默认）
					m.s = b.s;
					m.f = b.f;
					m.u = b.u;
					break;
			}
			if (Math.Abs(HandRollDeg) > 0.01f)
			{
				float r = HandRollDeg * 0.017453292f;      // 度 → 弧度
				float c = (float)Math.Cos(r), s = (float)Math.Sin(r);
				Vec3 s0 = m.s, f0 = m.f;
				m.s = s0 * c + f0 * s;                     // 在 s-f 平面里转 = 绕 u（朝外那根轴）滚转
				m.f = f0 * c - s0 * s;
			}
			return m;
		}

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

		/// <summary>
		/// 读回实体的**实际**世界帧原点 —— 回答"我们要它去哪"与"它真在哪"是否一致。
		/// 🔴 为什么必须有这一条：<see cref="ParkState"/> 里的 `pos=` 打的是**我们喂进去的值**（`_parkPos`），
		///    **不是实体自己的帧**。万一 `SetGlobalFrame` 被引擎忽略或抛异常（<see cref="MoveEntity"/> 的
		///    catch 会把它吞掉），日志照样显示"坐标正确、在转"，而实体其实停在"建出来那一刻"的位置
		///    ⇒ **看不见，但所有自证都"正常"** —— 2026-10-07 排查就卡在这个盲区上。
		/// </summary>
		public string ActualFrame()
		{
			if (_entity == null || _entity.Pointer == UIntPtr.Zero)
			{
				return "actual=(no entity)";
			}
			try
			{
				MatrixFrame f = _entity.GetGlobalFrame();
				return $"actual=({f.origin.x:F2},{f.origin.y:F2},{f.origin.z:F2})";
			}
			catch (Exception ex)
			{
				return "actual=(read failed: " + ex.GetType().Name + ")";
			}
		}

		/// <summary>诊断用：当前手里待命那枚实体的状态（网格名 / 实体是否建出来）。
		/// ⚠️ **返回文本一律英文**（控制台纪律）—— 它同时被 `custom.grapple spin` 与日志行用。
		/// 🔴 `pos=` = **我们要求的位置**；`actual=` = **实体自己报的帧**。两者不一致 = 摆位没生效。</summary>
		public string ParkState()
		{
			string meshName = "(unknown)";
			try
			{
				MetaMesh m = ResolveHookMesh();
				meshName = m != null ? MeshCandidates[0] + " OK" : "NONE FOUND (empty entity)";
			}
			catch (Exception)
			{
			}
			bool alive = _entity != null && _entity.Pointer != UIntPtr.Zero;
			return $"parked={_parked} entity={(alive ? "built" : "null")} mesh={meshName}"
				+ $" pos=({_parkPos.x:F2},{_parkPos.y:F2},{_parkPos.z:F2}) {ActualFrame()}";
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
