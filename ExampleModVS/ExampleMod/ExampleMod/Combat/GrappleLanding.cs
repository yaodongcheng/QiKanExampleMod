using System;
using System.Text;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
	/// <summary>
	/// **落点平台解算**（步骤 2 的一部分；方案 = plans\钩索-实施计划.md §3.7）。
	///
	/// 要解决的问题：钩中一面墙/一棵树，直接飞向钩点 = 撞在面上。真人要**落到钩点附近的平台上**
	/// （墙顶、屋顶、岩台）—— UE 参考工程是给每个钩点手工摆了 LandingZone，我们改成**运行时自动算**。
	///
	/// 三步（详见 §3.7）：
	///   ① 在钩点四周做环形采样（8 方向 × 几档半径）；
	///   ② 每个采样点从高处**向下问地面**（`V.GroundHeightAndNormal`，带法线），
	///      同时满足「站得住（法线够竖直）+ 高度在钩点附近 + 头顶有净空」才算候选；
	///   ③ 打分取最优 = 飞行终点；**一个都没有** = 从钩点沿射线退一段（自由落体，见 §3.7 第 3 条）。
	///
	/// 🔴 **引擎 API 事实（反编译实证，别改写法）**：
	///   · 带法线的地面查询**只有一条真填法线的路**，两版各不同（都收在 `V.GroundHeightAndNormal` 里）：
	///     v1.2.12 = `GetGroundHeightAtPositionMT(pos, out normal, flags)` —— 注意**只有带 MT 的**那个
	///     重载真填法线，不带 MT 的同名重载是引擎绑定 bug（把 normal 填成 Invalid 就返回了）；
	///     v1.3.0+ = `Scene` 上两个公开重载**都不填法线**，走 internal `IScene.GetGroundHeightAndNormalAtPosition`
	///     的反射桥（native 里是同一个函数）。
	///   · 返回值可能是 `NaN` / `±1e5` 哨兵 ⇒ 一律当"此处没有地面"（与 `GrappleRope.SampleFloors` 同款判据）。
	///   · flags 一律 `BodyFlags.CommonCollisionExcludeFlags`（不打自己人/自己身上的东西）。
	///
	/// 纯静态 + 无状态（参数是公开字段，命令层直接改）：**不跑任何游戏逻辑也能单测**
	/// —— `custom.grapple probe` 就是它的验收入口（只解算、不发射）。
	/// </summary>
	internal static class GrappleLanding
	{
		// ───────────────────────────── 可调参数（custom.grapple 命令改） ─────────────────────────────

		/// <summary>环形采样半径（米）。第一档给得很小：钩点本身就是台面（打中屋顶正面）时直接落原地。</summary>
		public static float[] Radii = { 0.4f, 0.8f, 1.3f, 2.0f };

		/// <summary>地面法线的竖直度阈值（`normal.z` ≥ 它才算"站得住"）。0.7 ≈ 45° 以下坡。</summary>
		public static float MinNormalZ = 0.7f;

		/// <summary>平台相对钩点的高度窗口（米）：低于 <see cref="MinDz"/> 的是"墙脚那块地"，不要；高于 <see cref="MaxDz"/> 的够不着。</summary>
		public static float MinDz = -1.0f;
		public static float MaxDz = 4.0f;

		/// <summary>头顶净空（米）：从候选平台往上射这么远，被挡就不算能站。</summary>
		public static float Headroom = 2.0f;

		/// <summary>没找到平台时，从钩点沿「玩家→钩点」方向**退回**的距离（米）—— 这一段射线已验证无遮挡。</summary>
		public static float FallbackBackoff = 1.5f;

		/// <summary>
		/// 🔴 **盖板判据**（2026-10-15「室内勾天花板 → 人被抬到房顶上」事故；方案 §二十一）。
		///
		/// 两条病灶（都有实机日志）：① 探针是从**钩点上方**往下打的，室内那一打正好打在天花板/屋顶的**背面**
		/// ⇒ 那块背面被当成了合法落脚台（实机：钩点 7.05 → 落点算到 7.53，比钩点还高）；② "没平台"的兜底落点
		/// **沿用钩点高度** ⇒ 脚直接放到天花板那一层（实机：钩点 11.69 → 板载坠落当场报"着地（板面=11.69 地面=11.69）"）。
		///
		/// 判据 = **盖板 ="钩点正上方最近的那块东西"**（天花板 / 楼板 / 屋檐 / 桥底 / 树枝…）。有盖板时：
		///   ① 落在盖板**上面**的候选平台**作废** —— 除非它头顶是**露天**（见 <see cref="LidTopSkyClearance"/>；
		///      2026-10-23 加：室外墙头 / 檐口 / 桥面必须能爬上去，否则"勾墙永远上不去"）；
		///   ② 没平台时的兜底落点**压到盖板下面**（留 <see cref="LidClearance"/> 米身高）—— 人会被拽到盖板下方，
		///      再随板自由落体掉回地面（用户 2026-10-15 选的表现："拉上去再掉回地面"）。
		///
		/// 🔴 **为什么锚在"钩点正上方"，而不是"玩家头顶"**（2026-10-16 用户纠正）：
		///    玩家是**斜着**飞过去的（目标在斜方向），"我头顶有块板子"根本不代表那条斜线过不去
		///    （板子可能就盖在我头上一点，我却从它边上出去）。真正拦路的是**钩点正上方那块东西**：
		///    勾天花板时，它就是天花板自己；勾墙时，它是墙上方那块檐口/楼板（没有 = 露天，随便飞）。
		///    这条同时也是"勾住的面朝下"这件事的**几何等价物**，但不用碰法线
		///    （三探针估出来的法线在粗糙面上会飘，且还要定一个"多斜才算朝下"的阈值 —— 射线不会飘）。
		/// 关掉（`custom.grapple lid 0`）= 回到旧行为（会飞上天花板/房顶），留作对照。
		/// </summary>
		public static bool LidCheck = true;

		/// <summary>落点必须比"盖板"低这么多米（= 身高 + 余量；顺带保证"板载坠落"那个地面查询从盖板下面起问）。</summary>
		public static float LidClearance = 2.2f;

		/// <summary>
		/// 量盖板那条射线的起点要**离开钩点多远**（米）—— 两个方向各用一次：
		/// ① **朝玩家方向水平挪**：钩子是从玩家那一侧咬上去的 ⇒ 挪一点就一定**离开了**被咬的那块面
		///    （不会从实体内部起打，也就不必依赖法线准不准）；
		/// ② **再往下压同样的距离**：只做水平偏移时，**勾天花板**那一枪的起点会正好落在**板面上**
		///    ⇒ 射线朝上出发直接进板子内部、什么都打不到（2026-10-17 实机：`lid=none` ⇒ 判据失效 ⇒ 人又飞上房顶）。
		/// </summary>
		public static float LidOffset = 0.25f;

		/// <summary>盖板射线的最大长度（米）——打这么远还没东西 = 钩点上方没有盖板。</summary>
		public static float LidProbeReach = 60f;

		/// <summary>
		/// **保险绳阈值**（2026-10-17 加）：射线万一**打空**（起点贴着面、退化几何…），而钩子咬的是一块
		/// **朝下的面**（钩点法线 z ≤ 它，默认 -0.5 ≈ 面往下斜 30° 以上）⇒ 就**用钩点自己当盖板**
		/// （= "勾住朝下的面，就不许被放到它上面" —— 用户 2026-10-16 提的那条判据）。
		///
		/// 🔴 **只在这一种情况下生效**：射线问出东西时一律以射线为准（法线是三探针**估**出来的平面，
		///    粗糙面上会飘，不能拿它当主判据）。加它是因为"判据静默失效"的代价太大 ——
		///    2026-10-17 实机：射线打空 ⇒ `lid=none` ⇒ 人又被拉到天花板那一层飞上房顶，而日志里只有一行 `lid=none`。
		/// </summary>
		public static float LidNetNormalZ = -0.5f;

		/// <summary>
		/// **盖板上面那些候选的放行条件（2026-10-23 加）**：候选台面在盖板上面时**不再一律作废** ——
		/// 从台面往上打这么长的射线，**头顶没有遮挡**（= 露天）才放行。
		///
		/// 🔴 为什么（实机 23:40:27 / 23:40:36，用户"勾墙好几次上不去"）：
		///    旧口径"候选在盖板上面 ⇒ 作废"只在**室内勾天花板**时是对的。一旦在**室外**勾到墙头/檐口/桥面
		///    这些**朝下的面**（实测法线 z = −0.74 / −0.90），那块板本身就是"盖板"，而玩家想去的正是它**上面**
		///    ⇒ 候选全被否光 → 兜底压到板下 → **拉上去再掉回地面**
		///    （日志：`blocked=16 lid=77.79 clamp=1` / `blocked=7 lid=86.16 clamp=1`）。
		///    室外与室内的几何区别 = **那块台面头顶是天空，还是又一层楼板/天花板** ⇒ 用它当判据。
		///    20 米保守（骑砍室内挑高很少超过它）；现场调 `custom.grapple lidsky &lt;米&gt;`，设 0 = 关掉放行（回到旧口径）。
		/// </summary>
		public static float LidTopSkyClearance = 20f;

		/// <summary>
		/// **玩家自己头顶的净空要求**（2026-10-23 加，与 <see cref="LidTopSkyClearance"/> 配对）：
		/// 玩家头顶这么多米内没有遮挡，才允许把落点放到盖板**上面**。
		///
		/// 🔴 为什么还要这一条：单层小屋的**屋顶**从里面看，屋顶上表面同样是"头顶露天"的合法候选 ——
		///    只靠候选那条检查挡不住"站在屋里勾天花板 ⇒ 被拉上屋顶"（就是 2026-10-15 那起事故的形态）。
		///    人在屋里 ⇒ 头顶几米内就是天花板/屋顶 ⇒ 这里直接挡掉。5 米：骑砍室内层高一般 3~4 米。
		/// </summary>
		public static float PlayerOpenClearance = 5f;

		/// <summary>八方向采样点（单位圆上的方向）。固定 8 个，不开放。</summary>
		private static readonly float[] DirX = { 1f, 0.7071f, 0f, -0.7071f, -1f, -0.7071f, 0f, 0.7071f };
		private static readonly float[] DirY = { 0f, 0.7071f, 1f, 0.7071f, 0f, -0.7071f, -1f, -0.7071f };

		// ───────────────────────────── 结果 ─────────────────────────────

		public struct Result
		{
			/// <summary>找到平台？false = 走"退一段 + 自由落体"的兜底。</summary>
			public bool Found;
			/// <summary>飞行终点（**脚底位置**）。找不到平台时 = 钩点退回 <see cref="FallbackBackoff"/> 米处。</summary>
			public Vec3 Endpoint;
			/// <summary>命中平台的法线（没找到 = Up）。</summary>
			public Vec3 Normal;
			/// <summary>选中候选的探针点（诊断用）。</summary>
			public Vec3 ProbePoint;
			/// <summary>采样次数 / 通过次数（诊断用）。</summary>
			public int Probes;
			public int Accepted;
			/// <summary>被「盖板」否掉的候选数（诊断用；`custom.grapple probe` 会打）。</summary>
			public int Blocked;
			/// <summary>「在盖板上面、但头顶露天」被**放行**的候选数（诊断用；2026-10-23 加）。</summary>
			public int LidPassed;
			/// <summary>最终选中的落点在**盖板上面**（= 这次是靠"头顶露天"放行才爬上去的）。</summary>
			public bool EndpointAboveLid;
			/// <summary>盖板的高度 = **钩点正上方**那块东西（`float.MaxValue` = 钩点上方没有东西 ⇒ 不受这条判据管）。</summary>
			public float LidZ;
			/// <summary>没平台时，兜底落点被压到盖板下面了吗（= 本次会"拉上去再掉回地面"）。</summary>
			public bool LidClamped;
			/// <summary>逐环明细（**单行**，probe 命令直接打印）。</summary>
			public string Detail;
		}

		// ───────────────────────────── 解算 ─────────────────────────────

		/// <summary>
		/// 解算飞行终点。<paramref name="hookPoint"/> = 准星射线命中点；
		/// <paramref name="playerPos"/> = 玩家当前位置（"没平台"那条兜底的方向 + 量盖板时"朝玩家挪一点"的方向）；
		/// <paramref name="hookNormal"/> = 钩点所在面的法线（迎着来向；不知道就传 `Vec3.Up`）——
		/// **只做保险绳**（射线打空 + 这块面朝下 ⇒ 用钩点自己当盖板），不参与主判据。
		/// </summary>
		public static Result Solve(Scene scene, Vec3 hookPoint, Vec3 playerPos, Vec3 hookNormal)
		{
			Result r = new Result
			{
				Found = false,
				Endpoint = hookPoint,
				Normal = Vec3.Up,
				ProbePoint = hookPoint,
				Detail = "",
				LidZ = float.MaxValue,
			};

			if (scene == null)
			{
				r.Detail = "no scene";
				return r;
			}

			// 🔴 **盖板 = 钩点正上方最近的那块东西**（2026-10-16 定稿；见 LidCheck 那一大段）：
			//    从钩点**朝玩家方向挪 25 厘米**（一定离开被咬的那块面）往上打一条射线 —— 打到什么，
			//    那个高度就是这次拉拽不许越过的"盖板"。打不到 = 钩点上方是露天 ⇒ 不受这条判据管。
			//    勾天花板 ⇒ 盖板就是天花板（落点不许算到它上面）；勾墙 ⇒ 盖板是墙上的檐口/楼板（没有就随便飞）。
			float lidZ = LidCheck ? FindLid(scene, hookPoint, playerPos) : float.MaxValue;
			// 🔴 **保险绳**（2026-10-17 加）：射线打空（起点贴着面 / 退化几何…）**而钩子咬的是朝下的面**
			//    ⇒ 用钩点自己当盖板。判据的失败方式太安静（`lid=none` 一行日志，人就飞上房顶了），
			//    宁可用一个"只在打空时才生效"的保守兜底。见 <see cref="LidNetNormalZ"/>。
			if (LidCheck && lidZ >= float.MaxValue * 0.5f && hookNormal.z <= LidNetNormalZ)
			{
				lidZ = hookPoint.z;
			}
			r.LidZ = lidZ;

			// 探针起点高度：高于"允许的最高平台"，这样平台在钩点上方时也能被下方探针找到。
			float probeTopZ = hookPoint.z + MaxDz + 1.0f;

			// 🔴 2026-10-23：**盖板上面的候选**只有"人在露天 + 台面头顶也露天"才放行。
			//    玩家的检查只做一次（与候选无关），见 PlayerOpenClearance 的注释 ——
			//    它挡的是"站在单层小屋里勾天花板 ⇒ 被拉上屋顶"（屋顶上表面头顶也是天空）。
			bool playerOpenAbove = LidCheck && LidTopSkyClearance > 0f
				&& HasClearanceAbove(scene, playerPos, PlayerOpenClearance);

			Vec3 bestPoint = Vec3.Invalid;
			Vec3 bestNormal = Vec3.Up;
			Vec3 bestProbe = Vec3.Invalid;
			float bestScore = float.MaxValue;
			int bestRing = -1;

			StringBuilder detail = new StringBuilder();

			for (int ring = 0; ring < Radii.Length; ring++)
			{
				float radius = Radii[ring];
				int acceptedThisRing = 0;
				int blockedThisRing = 0;
				string sampleNote = "-";

				for (int k = 0; k < 8; k++)
				{
					Vec3 probe = new Vec3(
						hookPoint.x + DirX[k] * radius,
						hookPoint.y + DirY[k] * radius,
						probeTopZ);
					r.Probes++;

					float groundZ;
					Vec3 normal;
					if (!QueryGround(scene, probe, out groundZ, out normal))
					{
						continue;
					}
					if (normal.z < MinNormalZ)
					{
						continue;                       // 太陡，站不住
					}
					float dz = groundZ - hookPoint.z;
					if (dz < MinDz || dz > MaxDz)
					{
						continue;                       // 不在"钩点附近的台"这个高度窗口里
					}
					if (!HasHeadroom(scene, new Vec3(probe.x, probe.y, groundZ)))
					{
						continue;                       // 头顶被挡，站不直
					}
					// 🔴 盖板（2026-10-15/16 室内事故）：这块台面在**钩点正上方那块板**的上面 ⇒ 本来要作废
					//    （室内勾天花板时，从上方往下问出来的"地面"正是天花板的背面）。
					// 🔴 2026-10-23 改：**先看那块台面头顶是不是露天** ——
					//    露天 = 室外墙头 / 檐口 / 桥面 ⇒ 放行（用户要的就是"勾墙能爬上去"）；
					//    不露天 = 室内上层楼板（上面还有天花板/屋顶）⇒ 照旧作废（防穿顶上房）。
					if (groundZ + LidClearance > lidZ)
					{
						if (!playerOpenAbove
							|| LidTopSkyClearance <= 0f
							|| !HasClearanceAbove(scene, new Vec3(probe.x, probe.y, groundZ), LidTopSkyClearance))
						{
							blockedThisRing++;
							r.Blocked++;
							continue;
						}
						r.LidPassed++;                  // 放行（诊断计数）——继续走下面的打分
					}

					acceptedThisRing++;
					r.Accepted++;

					// 打分：越贴近钩点高度、越近越好。半径权重 0.5 —— 先看"平不平/贴不贴"，再看远近。
					float score = MathF.Abs(dz) + radius * 0.5f;
					if (score < bestScore)
					{
						bestScore = score;
						bestPoint = new Vec3(probe.x, probe.y, groundZ);
						bestNormal = normal;
						bestProbe = probe;
						bestRing = ring;
					}

					if (acceptedThisRing == 1)
					{
						sampleNote = string.Format("first z={0:F2} nz={1:F2}", groundZ, normal.z);
					}
				}

				if (detail.Length > 0)
				{
					detail.Append(" | ");
				}
				detail.Append(string.Format("r{0:F1}: ok={1} block={2} {3}", radius, acceptedThisRing, blockedThisRing, sampleNote));
			}

			r.Detail = detail.ToString();

			if (bestRing >= 0)
			{
				r.Found = true;
				r.Endpoint = bestPoint;
				r.Normal = bestNormal;
				r.ProbePoint = bestProbe;
				r.EndpointAboveLid = LidCheck && bestPoint.z > lidZ;
				return r;
			}

			// ── 兜底：没平台 → 沿「玩家→钩点」方向退回一段，玩家到那儿自由落体 ──
			Vec3 back = hookPoint - playerPos;
			back.z = 0f;                                // 水平方向就够了（高度沿用钩点所在的那条射线）
			Vec3 ep = hookPoint;
			if (back.LengthSquared > 1e-4f)
			{
				back.Normalize();
				ep = hookPoint - back * FallbackBackoff;
			}

			// 🔴 **兜底落点也要过"盖板"这一关**（2026-10-15 室内事故的第二条病灶）：
			//    兜底沿用钩点高度，勾天花板时 = 把脚直接放到天花板那一层（实机：板载坠落当场报"着地"）。
			//    ⇒ 压到"盖板下面留出身高"的位置（用户选的表现：**拉上去 → 再掉回地面**）。
			//    ⚠️ 不许压到玩家脚底以下（那会变成"往下拽"）—— 到头了就原地不动（板载坠落会立刻着地，无害）。
			if (ep.z + LidClearance > lidZ)
			{
				ep = new Vec3(ep.x, ep.y, MathF.Max(playerPos.z, lidZ - LidClearance));
				r.LidClamped = true;
			}
			r.Endpoint = ep;

			r.Detail = r.Detail
				+ (r.Blocked > 0 ? " || " + r.Blocked + " candidate(s) above the lid (z=" + lidZ.ToString("F2") + ")" : "")
				+ (r.LidPassed > 0 ? " || " + r.LidPassed + " above-lid candidate(s) accepted (open sky)" : "")
				+ " || no platform -> backoff " + FallbackBackoff.ToString("F1") + "m"
				+ (r.LidClamped ? " (clamped under the lid z=" + lidZ.ToString("F2") + ")" : "");
			return r;
		}

		/// <summary>问"这个位置下方有没有可站的地面"。返回 false = 没有/哨兵值/查询异常。</summary>
		private static bool QueryGround(Scene scene, Vec3 probe, out float groundZ, out Vec3 normal)
		{
			groundZ = 0f;
			normal = Vec3.Up;
			try
			{
				float z = V.GroundHeightAndNormal(scene, probe, out normal, BodyFlags.CommonCollisionExcludeFlags);
				if (float.IsNaN(z) || z > 1e5f || z < -1e5f || float.IsNaN(normal.z))
				{
					return false;                       // 哨兵 = 此处没有地面（同 GrappleRope.SampleFloors 的口径）
				}
				groundZ = z;
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>
		/// 盖板的高度 = **钩点正上方最近的那块东西**（`float.MaxValue` = 钩点上方没有东西）。
		///
		/// 射线起点 = 钩点**朝玩家方向挪 <see cref="LidOffset"/> 米**：钩子是从玩家那一侧咬上去的，
		/// 朝玩家挪一点就一定**离开了**被咬的那块面 —— 于是既不会从实体内部起打，也不必依赖法线准不准。
		/// 之后笔直往上打 <see cref="LidProbeReach"/> 米：打到什么（天花板 / 檐口 / 楼板 / 树枝 / 桥面），
		/// 什么就是这次拉拽的"盖板"。
		/// 🔴 问不出来一律当**没有盖板** —— 这条路只用来"拦"，失败时宁可不拦（别把露天场景误杀）。
		/// </summary>
		private static float FindLid(Scene scene, Vec3 hookPoint, Vec3 playerPos)
		{
			try
			{
				Vec3 flat = playerPos - hookPoint;
				flat.z = 0f;                             // 水平分量：朝玩家退开，别让射线蹭到自己咬的那块面
				Vec3 from = hookPoint;
				if (flat.LengthSquared > 1e-6f)
				{
					from += flat.NormalizedCopy() * LidOffset;
				}
				// 🔴🔴 **还必须往下压一段**（2026-10-17 实机教训，日志实锤）：只做水平偏移时，**勾天花板**那一枪的起点
				//    会**正好落在板面上**（钩点 z = 板面 z = 11.69）⇒ 射线朝上出发直接进了板子内部、什么都打不到
				//    ⇒ `lid=none` ⇒ 判据整个失效，人又被拉到 11.69（= 又飞到房顶上）。压下去这 0.25 米之后，
				//    射线从板**下面**的空气中起打 ⇒ 打到的第一块就是那块板自己 = 盖板 ✓。
				//    ⚠️ 勾墙时多压这一点不碍事（射线仍在墙面外侧 0.25 米处往上走，能正常越过墙头）。
				from.z -= LidOffset;
				Vec3 to = new Vec3(from.x, from.y, from.z + LidProbeReach);
				float distance;
				Vec3 hitPoint;
				bool blocked = scene.RayCastForClosestEntityOrTerrain(from, to, out distance, out hitPoint,
					0.01f, BodyFlags.CommonCollisionExcludeFlags);
				return blocked ? hitPoint.z : float.MaxValue;
			}
			catch (Exception)
			{
				return float.MaxValue;
			}
		}

		/// <summary>候选平台上方有没有 <see cref="Headroom"/> 米的净空（一条细射线，向上）。</summary>
		private static bool HasHeadroom(Scene scene, Vec3 groundPoint)
			=> HasClearanceAbove(scene, groundPoint, Headroom);

		/// <summary>
		/// 从台面往上打一条细射线：<paramref name="height"/> 米内**没有遮挡** ⇒ true。
		/// 两处消费者：① 候选的头顶净空（<see cref="Headroom"/>）② **盖板上方候选"头顶是不是露天"**
		/// （<see cref="LidTopSkyClearance"/>，2026-10-23）。查询本身出错 = false（宁可保守）。
		/// </summary>
		private static bool HasClearanceAbove(Scene scene, Vec3 groundPoint, float height)
		{
			try
			{
				Vec3 from = new Vec3(groundPoint.x, groundPoint.y, groundPoint.z + 0.25f);
				Vec3 to = new Vec3(groundPoint.x, groundPoint.y, groundPoint.z + 0.25f + height);
				float distance;
				Vec3 hitPoint;
				bool blocked = scene.RayCastForClosestEntityOrTerrain(from, to, out distance, out hitPoint,
					0.01f, BodyFlags.CommonCollisionExcludeFlags);
				return !blocked;
			}
			catch (Exception)
			{
				return false;                           // 查询本身出问题 = 当作不能站（宁可保守）
			}
		}

		// ───────────────────────────── 诊断 ─────────────────────────────

		/// <summary>一行明细（**单行** —— 控制台里换行会串版）。</summary>
		public static string Describe(Result r)
		{
			string lid = r.LidZ >= float.MaxValue * 0.5f ? "none" : r.LidZ.ToString("F2");
			return string.Format("found={0} ep=({1:F2},{2:F2},{3:F2}) n=({4:F2},{5:F2},{6:F2}) probes={7} ok={8} blocked={9} lid={10}{11}{13} [{12}]",
				r.Found ? 1 : 0,
				r.Endpoint.x, r.Endpoint.y, r.Endpoint.z,
				r.Normal.x, r.Normal.y, r.Normal.z,
				r.Probes, r.Accepted, r.Blocked, lid,
				r.LidClamped ? " clamp=1" : "",
				r.Detail ?? "-",
				(r.LidPassed > 0 ? " lidpass=" + r.LidPassed : "")
					+ (r.EndpointAboveLid ? " above=1" : ""));
		}

		/// <summary>把参数恢复默认（命令 `custom.grapple probe reset` 用）。</summary>
		public static void ResetDefaults()
		{
			Radii = new float[] { 0.4f, 0.8f, 1.3f, 2.0f };
			MinNormalZ = 0.7f;
			MinDz = -1.0f;
			MaxDz = 4.0f;
			Headroom = 2.0f;
			FallbackBackoff = 1.5f;
			LidCheck = true;
			LidClearance = 2.2f;
			LidOffset = 0.25f;
			LidProbeReach = 60f;
			LidNetNormalZ = -0.5f;
			LidTopSkyClearance = 20f;
			PlayerOpenClearance = 5f;
		}
	}
}
