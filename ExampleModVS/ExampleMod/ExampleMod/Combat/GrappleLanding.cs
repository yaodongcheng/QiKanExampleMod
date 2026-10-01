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
	///   ② 每个采样点从高处**向下问地面**（`GetGroundHeightAtPositionMT` 带法线），
	///      同时满足「站得住（法线够竖直）+ 高度在钩点附近 + 头顶有净空」才算候选；
	///   ③ 打分取最优 = 飞行终点；**一个都没有** = 从钩点沿射线退一段（自由落体，见 §3.7 第 3 条）。
	///
	/// 🔴 **引擎 API 事实（反编译 1.2.12 实证，别改写法）**：
	///   · 只有带 **MT** 的重载 `GetGroundHeightAtPositionMT(pos, out normal, flags)` 真填法线；
	///     不带 MT 的同名重载是引擎绑定 bug（把 normal 填成 Invalid 就返回了）。
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
			/// <summary>逐环明细（**单行**，probe 命令直接打印）。</summary>
			public string Detail;
		}

		// ───────────────────────────── 解算 ─────────────────────────────

		/// <summary>
		/// 解算飞行终点。<paramref name="hookPoint"/> = 准星射线命中点；
		/// <paramref name="playerPos"/> = 玩家当前位置（只用于"没平台"那条兜底的方向）。
		/// </summary>
		public static Result Solve(Scene scene, Vec3 hookPoint, Vec3 playerPos)
		{
			Result r = new Result
			{
				Found = false,
				Endpoint = hookPoint,
				Normal = Vec3.Up,
				ProbePoint = hookPoint,
				Detail = "",
			};

			if (scene == null)
			{
				r.Detail = "no scene";
				return r;
			}

			// 探针起点高度：高于"允许的最高平台"，这样平台在钩点上方时也能被下方探针找到。
			float probeTopZ = hookPoint.z + MaxDz + 1.0f;

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
				detail.Append(string.Format("r{0:F1}: ok={1} {2}", radius, acceptedThisRing, sampleNote));
			}

			r.Detail = detail.ToString();

			if (bestRing >= 0)
			{
				r.Found = true;
				r.Endpoint = bestPoint;
				r.Normal = bestNormal;
				r.ProbePoint = bestProbe;
				return r;
			}

			// ── 兜底：没平台 → 沿「玩家→钩点」方向退回一段，玩家到那儿自由落体 ──
			Vec3 back = hookPoint - playerPos;
			back.z = 0f;                                // 水平方向就够了（高度沿用钩点所在的那条射线）
			if (back.LengthSquared > 1e-4f)
			{
				back.Normalize();
				r.Endpoint = hookPoint - back * FallbackBackoff;
			}
			else
			{
				r.Endpoint = hookPoint;
			}
			r.Detail = r.Detail + " || no platform -> backoff " + FallbackBackoff.ToString("F1") + "m";
			return r;
		}

		/// <summary>问"这个位置下方有没有可站的地面"。返回 false = 没有/哨兵值/查询异常。</summary>
		private static bool QueryGround(Scene scene, Vec3 probe, out float groundZ, out Vec3 normal)
		{
			groundZ = 0f;
			normal = Vec3.Up;
			try
			{
				float z = scene.GetGroundHeightAtPositionMT(probe, out normal, BodyFlags.CommonCollisionExcludeFlags);
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

		/// <summary>候选平台上方有没有 <see cref="Headroom"/> 米的净空（一条细射线，向上）。</summary>
		private static bool HasHeadroom(Scene scene, Vec3 groundPoint)
		{
			try
			{
				Vec3 from = new Vec3(groundPoint.x, groundPoint.y, groundPoint.z + 0.25f);
				Vec3 to = new Vec3(groundPoint.x, groundPoint.y, groundPoint.z + 0.25f + Headroom);
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
			return string.Format("found={0} ep=({1:F2},{2:F2},{3:F2}) n=({4:F2},{5:F2},{6:F2}) probes={7} ok={8} [{9}]",
				r.Found ? 1 : 0,
				r.Endpoint.x, r.Endpoint.y, r.Endpoint.z,
				r.Normal.x, r.Normal.y, r.Normal.z,
				r.Probes, r.Accepted, r.Detail ?? "-");
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
		}
	}
}
