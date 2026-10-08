using System;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **钩索这一套的根实体**（2026-10-08 用户要求："钩锁、绳子、环必须是同一个 gameentity 下严格的父子关系"）。
	///
	/// 结构：`root`（空实体，本节创建，随本局存活）
	///        ├─ 手上那枚**钩**（<see cref="GrappleHook"/> 的实体，网格 `lwn_grapple_hook`）
	///        ├─ **绳**（<see cref="GrappleRope"/> 的环/管段实体）
	///        └─ **左手环**（本节自绘，网格 `lwn_grapple_ring`，每帧跟左手骨）
	///
	/// 🔴 **为什么引擎挂的那两件进不来**（已跟用户讲清的硬约束）：物品网格是**引擎按骨绑定的武器实体**
	/// （`Agent.EquipWeaponWithNewEntity` → native `WeaponEquipped` 把网格绑到骨架的骨上），它们的父级是角色骨架，
	/// 我们只能读帧、不能改父级 ⇒ 那两件退回**隐形占位**（`lwn_proxy_invisible`），"左手环"改由我们自绘 ✓。
	///
	/// 🔴 **销毁纪律：不复用 `RemoveChild`** —— 全游戏 DLL 里**只有定义、没有任何一处调用**（无先例可抄，
	/// 5 个参数只能猜）；而它的 `keepPhysics` / `keepScenePointer` 参数说明它的用途是
	/// "**把一个还活着的子件摘下来**"，不是"销毁子件"（销毁由引擎自己解链）。
	/// ⇒ 子件由各自的所有者照旧 `Remove(0)`。**故障表征** = 下一次 `AddChild` 崩 —— 那时再加显式摘链。
	///
	/// 🔴 **位置口径**：子件仍按**世界坐标**每帧摆（本仓已验证的那套）；父级目前是**结构上的从属**
	/// （"整件东西能当一个东西被移动/丢弃"要再把子件改成根下的局部坐标 —— 那是另一档工作量，用户 2026-10-08 未选）。
	/// </summary>
	internal sealed class GrappleRig
	{
		/// <summary>左手环的网格（内容包资产；两轮兜底在 <see cref="SpellWorld.ResolveMesh"/> 里）。</summary>
		public const string RingMesh = "lwn_grapple_ring";

		/// <summary>
		/// 左手环的**自身轴指向哪**（`custom.grapple ringface &lt;0|1|2&gt;`）。
		/// 🔴 环的自身轴 = **局部 Z**（装机包实测：`tpaccli dump` 出的环是 XY 平面上的扁环，X 0.080 / Y 0.080 / **Z 0.015**；
		/// 早前文档里"环面 = XZ、轴 = Y"是 FBX/Blender 侧读数被换过轴 —— 与钩那次同一个坑）。
		/// · **0（默认）= 轴沿小臂**（肘→手）⇒ 环面 ⊥ 小臂（像一只松垮套在手腕上的镯子）
		/// · 1 = 轴 = **世界竖直** ⇒ 环面水平（平端在掌心）
		/// · 2 = 轴 = **角色右方向** ⇒ 环面竖直、朝前后（竖着拎）
		/// </summary>
		public static int RingFaceMode = 0;

		/// <summary>左手环缩放（`custom.grapple ringscale &lt;倍率&gt;`；默认 1 = 资产原尺寸 8 cm 外径）。</summary>
		public static float RingScale = 1f;

		/// <summary>
		/// **左手环要不要显示**（`custom.grapple handring 0|1`；**默认 false = 不显示**）。
		/// 🔴 为什么默认关（2026-10-08）：左手那件现在由**物品网格**负责 —— 绳那件（`taikou_grapple_rope`）的 `mesh`
		/// 是 `lwn_grapple_rope`（一盘绳），引擎把它挂在**左手骨**上；环是上一版的做法（运行时实体、每帧跟左手骨），
		/// 两个一起上 = 同一只手上叠着"环 + 绳"。绳的近端锚点在**右手**（<see cref="GrappleLogic.GetRopeAnchor"/>），
		/// 与环无关 ⇒ 关掉不丢任何功能。
		/// </summary>
		public static bool HandRingEnabled = false;

		/// <summary>
		/// **环沿自身轴外移**（米，`custom.grapple ringface &lt;档&gt; [米]`；默认 **0.06**）——
		/// 🔴 与钩的支点同一个道理：骨点 = **腕关节**，不是掌心（用户 2026-10-08 实机："环不在手掌里"）。
		///    沿"环的轴"往外挪一点，环才落在掌中而不是套在腕上。设 0 = 正好压在骨点上。
		/// </summary>
		public static float RingPalmOffset = 0.06f;

		private GameEntity _root;
		private GameEntity _ring;
		private bool _ringWarned;

		/// <summary>左手环**此刻的世界位置**（= "环 → 右手"那截绳的起点）。没摆上时无意义。</summary>
		public Vec3 RingPosition { get; private set; }

		/// <summary>左手环此刻摆上了吗（<see cref="TickRing"/> 成功摆位 = true；收起 = false）。</summary>
		public bool RingPlaced { get; private set; }

		/// <summary>环的轴最近一次是**从骨读出来的**（false = 退回世界竖直的兜底）—— 诊断用。</summary>
		public bool RingAxisFromBones { get; private set; }

		/// <summary>环的轴最近一次的值（诊断用）。</summary>
		public Vec3 LastRingAxis { get; private set; }

		/// <summary>
		/// **把根实体摆到世界某点**（钩索这件"东西"本身在哪）—— 2026-10-08 起 root 会动。
		/// 🔴 口径（用户 2026-10-08：**要符合真实世界的逻辑**，这样才能"丢地上/捡起来"）：
		///    **root = 这件东西的挂点（手/载体）**，子件全部用**局部坐标**挂在它下面
		///    ⇒ 以后"把整件东西挪走/丢下"只需摆 root 一件事，全套跟着走 ✓。
		///    🔴 每帧**必须先摆 root、再摆子件**（子件写的是"相对 root 的局部帧"，用到的 root 帧要是本帧的）。
		/// </summary>
		public void PlaceRoot(Vec3 worldPos, Scene scene = null)
		{
			try
			{
				// 🔴 **要传 scene**（2026-10-08 修）：不传的话，第一次 `PlaceRoot` 时根实体还不存在 ⇒
				//    `Root(null)` 直接返回 null、什么都不做；根实体随后在 `Adopt()`（建第一个子件时）才被创建，
				//    **位置在世界原点** ⇒ 那一帧所有子件记下的"局部帧"其实是**世界坐标**（几百米）。
				//    同一帧里子件会被重新摆一次（`Place` 会重算局部帧）所以看不出问题，
				//    但**没被重摆的子件**（退化被隐藏的那些）会把那份几百米的偏移一直挂着 —— 根一动它们就飞。
				//    传了 scene ⇒ 根实体在**摆根这一步**就按"手的位置"建出来，从第一帧起坐标口径就是对的。
				GameEntity root = Root(scene);       // 已建过就直接拿；scene 非空且没建过 = 现在就建
				if (root == null || root.Pointer == UIntPtr.Zero)
				{
					return;
				}
				root.SetGlobalFrame(new MatrixFrame(Mat3.Identity, worldPos));
			}
			catch (Exception)
			{
			}
		}

		/// <summary>
		/// **摆一个子件**：给的是**世界**帧（我们的数学照旧全在世界空间算），这里换算成**相对 root 的局部帧**再写。
		/// 🔴 为什么要换算：子件挂在 root 下，写世界帧会被"父 × 子局部"的引擎重算顶掉（混用会漂移）；
		///    统一走这一个入口，坐标口径就只有一处。root 不可用时退回写世界帧（至少不崩、位置也对）。
		/// </summary>
		public void Place(GameEntity e, MatrixFrame world)
		{
			if (e == null || e.Pointer == UIntPtr.Zero)
			{
				return;
			}
			try
			{
				GameEntity root = Root(null);
				if (root == null || root.Pointer == UIntPtr.Zero)
				{
					e.SetGlobalFrame(world);
					return;
				}
				MatrixFrame local = root.GetGlobalFrame().TransformToLocal(world);
				e.SetFrame(ref local);
			}
			catch (Exception)
			{
				try
				{
					e.SetGlobalFrame(world);
				}
				catch (Exception)
				{
				}
			}
		}

		/// <summary>
		/// 给实体**起名**（我们自己发的身份证）—— `GameEntity.Name` **可读可写** ✓。
		/// 命名法 = `lwn_<标签>_<类型><序号>`（如 `lwn_B_link003` / `lwn_hook` / `lwn_ring`）。
		/// 🔴 为什么必须有编号：**一条绳几十上百个实体**（飞行时 200+），只报"首/末"根本说不清是哪个；
		///    起了名之后日志能逐个体对上，也能用 `Scene.FindEntityWithName()` 反查存活。
		/// </summary>
		public static void Name(GameEntity e, string name)
		{
			try
			{
				if (e != null && e.Pointer != UIntPtr.Zero && !string.IsNullOrEmpty(name))
				{
					e.Name = name;
				}
			}
			catch (Exception)
			{
			}
		}

		/// <summary>实体的一行身份（日志用）：`名字@指针`。名字缺失时退化成 `(unnamed)@指针`。</summary>
		public static string Describe(GameEntity e)
		{
			try
			{
				if (e == null || e.Pointer == UIntPtr.Zero)
				{
					return "null";
				}
				string nm = null;
				try
				{
					nm = e.Name;
				}
				catch (Exception)
				{
				}
				return (string.IsNullOrEmpty(nm) ? "(unnamed)" : nm) + "@" + Ptr(e);
			}
			catch (Exception)
			{
				return "(describe failed)";
			}
		}

		/// <summary>
		/// 实体的**身份串**（日志用）—— 打原生指针。
		/// 🔴 为什么用指针而不是 GUID：`GetGuid()` 只对**编辑器/资产实体**有效，运行时 `CreateEmpty` 出来的实体
		///    多半 `IsGuidValid()==false`；而指针在本会话内唯一（**会被回收复用** —— 正好用来发现"销毁后地址又被新实体拿走"，
		///    那正是悬空指针/双重销毁的signature）。想让实体更可读可以用 `Name`（可读可写）自己起名。
		/// </summary>
		public static string Ptr(GameEntity e)
		{
			try
			{
				if (e == null || e.Pointer == UIntPtr.Zero)
				{
					return "null";
				}
				return "0x" + e.Pointer.ToUInt64().ToString("X");
			}
			catch (Exception)
			{
				return "(ptr read failed)";
			}
		}

		/// <summary>
		/// 根实体当前**挂着几个子件**（诊断）。
		/// 🔴 这是验证"**销毁子件时引擎会不会自动解链**"的直接判据（2026-10-08 悬空子件嫌疑）：
		///    绳每次重建都会先 `Teardown()` 拆掉自己的段/环实体 —— 对比"拆前 / 拆后"的 children 数：
		///    · 减少了 = 引擎**会**解链 ✓（悬挂嫌疑排除）
		///    · **没减少** = 悬空子件坐实 ✗（那就必须补显式摘链，或把绳实体改成"池子：建一次反复用"）
		/// </summary>
		public int ChildCount
		{
			get
			{
				try
				{
					return _root != null && _root.Pointer != UIntPtr.Zero ? _root.ChildCount : -1;
				}
				catch (Exception)
				{
					return -2;
				}
			}
		}

		/// <summary>根实体（懒建、整局复用）。拿不到（场景没了）返回 null。</summary>
		public GameEntity Root(Scene scene)
		{
			if (_root != null && _root.Pointer != UIntPtr.Zero)
			{
				return _root;
			}
			if (scene == null)
			{
				return null;
			}
			try
			{
				_root = GameEntity.CreateEmpty(scene, true);
				Name(_root, "lwn_rig_root");                               // 自己的编号
				DebugLogger.Log($"[Rope] root 召唤实体：{Describe(_root)}（钩/绳/环都收在它下面）");
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 根实体创建失败：{ex.GetType().Name} {ex.Message}");
				_root = null;
			}
			return _root;
		}

		/// <summary>
		/// 把一个实体**收进根下**（`autoLocalizeFrame: true` = 保持它此刻的世界位置不变）。
		/// 钩（<see cref="GrappleHook.SpawnEntity"/>）与绳（<see cref="GrappleRope"/> 建实体处）都调它。
		/// </summary>
		public void Adopt(Scene scene, GameEntity child)
		{
			if (child == null || child.Pointer == UIntPtr.Zero)
			{
				return;
			}
			try
			{
				GameEntity root = Root(scene);
				if (root == null || root.Pointer == UIntPtr.Zero)
				{
					return;
				}
				root.AddChild(child, autoLocalizeFrame: true);
			}
			catch (Exception)
			{
				// 单个子件挂不上不拖垮其余（钩/绳照旧按世界坐标摆，观感不受影响）
			}
		}

		/// <summary>
		/// 每帧：**左手环**跟到左手骨上（<paramref name="show"/> = false 时收起来）。
		/// 读骨走与手挂点**同一套**已验证的接口：`GetRealBoneIndex(HumanBone.HandL)` + <see cref="SpellCastInput.TryReadBoneWorld"/>
		/// （双解释：原生骨帧给的是角色局部坐标，实测过 d=575 m vs 1.4 m）。
		/// </summary>
		public void TickRing(Scene scene, Agent player, bool show)
		{
			if (!show || player == null || scene == null)
			{
				HideRing();
				return;
			}

			if (!EnsureRing(scene))
			{
				return;
			}

			MBAgentVisuals v = player.AgentVisuals;
			if (v == null || !v.IsValid())
			{
				HideRing();
				return;
			}
			sbyte bone = v.GetRealBoneIndex(HumanBone.HandL);
			if (!SpellCastInput.TryReadBoneWorld(player, bone, 1.5f, out Vec3 hand, out _))
			{
				HideRing();
				return;
			}
			Vec3 axis = RingAxisDir(player);
			if (axis.LengthSquared < 1e-6f)
			{
				HideRing();
				return;
			}
			LastRingAxis = axis;
			try
			{
				// 环的自身轴 = 局部 Z（装机包实测）⇒ 让局部 Z 对准 axis，环面自然 ⊥ 它
				Vec3 at = hand + axis * RingPalmOffset;      // 从腕关节挪到掌心
				MatrixFrame frame = new MatrixFrame(GrappleRope.BasisWithLocalZ(axis), at);
				if (Math.Abs(RingScale - 1f) > 0.001f && RingScale > 0f)
				{
					frame.Scale(new Vec3(RingScale, RingScale, RingScale));
				}
				Place(_ring, frame);
				_ring.SetVisibilityExcludeParents(true);
				RingPosition = at;      // 供"环 → 右手"那截绳取起点
				RingPlaced = true;
			}
			catch (Exception)
			{
			}
		}

		/// <summary>一行状态（命令回执用，纯英文 —— 控制台纪律）。</summary>
		public string Describe()
		{
			int kids = -1;
			try
			{
				if (_root != null && _root.Pointer != UIntPtr.Zero)
				{
					kids = _root.ChildCount;
				}
			}
			catch (Exception)
			{
			}
			bool ring = _ring != null && _ring.Pointer != UIntPtr.Zero;
			return $"rig: root={(kids >= 0 ? "built" : "null")} children={kids} ring={(ring ? "built" : "null")}"
				+ $" ringFace={RingFaceMode} ringPalm={RingPalmOffset:F2} ringAxis={(RingAxisFromBones ? "bones" : "fallback")}"
				+ $" axis=({LastRingAxis.x:F2},{LastRingAxis.y:F2},{LastRingAxis.z:F2})";
		}

		/// <summary>整局收尾（`GrappleLogic.OnEndMission`）：环先撤，根随后 —— 场景本来也要拆了。</summary>
		public void Teardown()
		{
			try
			{
				if (_ring != null && _ring.Pointer != UIntPtr.Zero)
				{
					DebugLogger.Log("[Rope] ring 销毁实体（场景收尾）");
					_ring.Remove(0);
				}
			}
			catch (Exception)
			{
			}
			_ring = null;
			try
			{
				if (_root != null && _root.Pointer != UIntPtr.Zero)
				{
					_root.Remove(0);
				}
			}
			catch (Exception)
			{
			}
			_root = null;
		}

		// ───────────────────────────── 内部 ─────────────────────────────

		/// <summary>环的实体（懒建 + 收进根下）。返回 false = 网格查不到 / 建不出来（已记一条日志）。</summary>
		private bool EnsureRing(Scene scene)
		{
			if (_ring != null && _ring.Pointer != UIntPtr.Zero)
			{
				return true;
			}
			MetaMesh mesh = SpellWorld.ResolveMesh(RingMesh);
			if (mesh == null)
			{
				if (!_ringWarned)
				{
					_ringWarned = true;
					DebugLogger.Log($"[Grapple] 左手环网格 '{RingMesh}' 查不到 —— 环不显示（检查内容包资产名）");
				}
				return false;
			}
			try
			{
				_ring = GameEntity.CreateEmpty(scene, true);
				if (_ring == null)
				{
					return false;
				}
				_ring.AddMultiMesh(mesh, true);
				_ring.SetVisibilityExcludeParents(true);
				Name(_ring, "lwn_ring");                                   // 自己的编号
				Adopt(scene, _ring);
				DebugLogger.Log($"[Rope] ring 召唤实体：网格={RingMesh} {Describe(_ring)}（已收进根实体）");
				return true;
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[Grapple] 左手环实体生成异常：{ex.GetType().Name} {ex.Message}");
				_ring = null;
				return false;
			}
		}

		/// <summary>环的自身轴（局部 Z）指向哪 —— 三档见 <see cref="RingFaceMode"/>。
		/// 顺手把"是不是从骨读出来的"记进 <see cref="RingAxisFromBones"/>（诊断：退回兜底时环会歪/会被顶到上方）。</summary>
		private Vec3 RingAxisDir(Agent player)
		{
			switch (RingFaceMode)
			{
				case 1:
					RingAxisFromBones = false;
					return Vec3.Up;
				case 2:
				{
					RingAxisFromBones = false;
					Vec3 fwd = player.LookDirection;
					if (fwd.LengthSquared < 1e-6f)
					{
						fwd = Vec3.Forward;
					}
					Vec3 r = Vec3.CrossProduct(fwd.NormalizedCopy(), Vec3.Up);
					return r.LengthSquared < 1e-6f ? Vec3.Forward : r.NormalizedCopy();
				}
				default:
				{
					// 0 = 轴沿**小臂**（肘 → 手）—— 与钩的绕转轴同一套语义骨接口，左右手对称
					MBAgentVisuals v = player.AgentVisuals;
					if (v != null && v.IsValid())
					{
						sbyte fore = v.GetRealBoneIndex(HumanBone.ForearmL);
						sbyte hand = v.GetRealBoneIndex(HumanBone.HandL);
						if (SpellCastInput.TryBoneToBoneAxis(player, fore, hand, 0.10f, 0.60f, out Vec3 ax, out _))
						{
							RingAxisFromBones = true;
							return ax;
						}
					}
					RingAxisFromBones = false;
					return Vec3.Up;      // 读不到就退回竖直（不让环乱翻）
				}
			}
		}

		private void HideRing()
		{
			RingPlaced = false;
			if (_ring == null || _ring.Pointer == UIntPtr.Zero)
			{
				return;
			}
			try
			{
				_ring.SetVisibilityExcludeParents(false);
			}
			catch (Exception)
			{
			}
		}
	}
}
