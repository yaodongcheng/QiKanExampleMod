using System;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using HarmonyLib;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View;
#if !MB2_GE_130
using TaleWorlds.MountAndBlade.View.Tableaus;      // TableauCacheManager（1.2.12；1.3+ 改名 ThumbnailCacheManager）
#endif

namespace LivingWorldNpcs
{
	/// <summary>
	/// **钩索的"图标补丁"**（2026-10-08 立）—— 让**钩**这件物品在**背包 / 装备 / 交易界面**里显示成一枚真钩，
	/// 而**实机（真 mission）里一点都不变**（手里那枚钩仍由运行时实体负责、能绕小臂转）。
	///
	/// 🔴 为什么需要它（问题的形状）：
	///   一件物品的 **2D 图标**和它**在世界上长什么样**，在引擎里是**同一个字段**：
	///     · 箭类物品（我们的钩，`Type=Arrows`）的图标取 `holster_mesh` —— 而同一个 `holster_mesh`
	///       就是它挂在**身上鞘位**时的那件 ⇒ 直接把 `holster_mesh` 设成钩 = 图标有钩了，
	///       但**实机腰上/背上也会挂一枚钩**，跟手里那枚转的钩同框（用户明确不要）。
	///     · 弓类物品（绳那件）的图标取 `mesh` —— 同一个 `mesh` 就是**左手**那件。
	///   ⇒ 想要"图标是钩、实机不出现第二枚钩"，只能**在画图标那条链上换网格**（就是本补丁）。
	///
	/// 🔴 为什么可以这么做（关键事实，反编译 + 引擎日志实证）：
	///   界面的 2D 图标（列表格子 + 点开的大图）**和实机是两条不同的渲染链**：
	///     · 图标 = `TableauCacheManager` 拿物品网格**离屏渲染成一张贴图**（场景 `scn_item_tableau`，
	///       启动时就和 `scn_soldier` / `inventory_character_scene` 一起当 tableau 场景加载）；
	///     · 实机 = `AgentVisuals` 把装备网格挂到角色骨头上。
	///   本补丁打在 `ItemCollectionElementViewExtensions.GetItemMeshForInventory` 上 —— 反编译确认
	///   **全游戏只有 View.dll 里的 `TableauCacheManager` 调它**（三个调用点全在 tableau 方法里）
	///   ⇒ 打它 = 只影响图标，**碰不到实机**。
	///
	/// 行为：只对**钩那件物品**（`GrappleFirePatch.HookItemId`）生效，其余物品一行都不多做（返回 true 走原版）；
	///   解析不到网格、或任何异常 ⇒ 一律放行（引擎画它自己的代理，最多图标空白，**绝不崩**）。
	///   每次返回一份 `CreateCopy()` 的**新副本**（`AddItem` 会把网格交给场景，缓存实例共享会出事）。
	///
	/// 开关：`custom.grapple iconhook 0|1`（即时生效，重开背包界面刷新图标）。
	/// 🔴 2026-10-09：钩索物品已搬进**本模块**（`ModuleData/items/grapple.xml`）⇒ 本条**永远挂载**
	///   （已从 `MySubModule` 的 contentPackOnly 清单摘除）；物品缺席时网格解析失败即交回引擎，不崩。
	/// </summary>
	[HarmonyPatch(typeof(ItemCollectionElementViewExtensions), "GetItemMeshForInventory")]
	internal static class GrappleIconPatch
	{
		/// <summary>画图标时替上去的网格（自造三爪钩，与运行时实体、`flying_mesh` 同一件）。</summary>
		public const string HookMeshName = "lwn_grapple_hook";

		/// <summary>补丁开关（`custom.grapple iconhook 0|1`）。</summary>
		public static bool Enabled = true;

		/// <summary>
		/// 🔴 **图标里那件东西占画面的宽度比例**（`custom.grapple iconscale &lt;绳 0..1.2&gt; [钩]`）——
		/// 2026-10-08 用户报"两件道具在背包里显示得非常小、而且不居中"的修法。
		///
		/// **为什么会小**（反编译 `TableauCacheManager.GetItemPoseAndCamera` 实证）：背包/装备界面的 2D 图标
		/// **不是**按物件实际大小取景的，而是**按物品类型挑一台写死的相机**：
		/// `Type=Bow` → `bow_cam`/`bow_frame`（按**真弓 ≈1.2 m** 摆的）· `Type=Arrows` → `arrow_cam`/`arrow_frame`
		/// （按**真箭 ≈0.85 m** 摆的）；**只有 "goods"（杂货）那条兜底分支**才按包围盒自动居中 + 按尺寸自动拉相机
		/// （所以葡萄/谷物的图标永远是对的）。⇒ 我们的绳只有 **13 cm**、钩只有 **20 cm**，顶着"弓/箭"的取景框
		/// ⇒ **又小又偏**（钩还会被推出画面）。
		///
		/// **在哪治**：`GrappleIconPosePatch`（1.2.12 专用，打在 `GetItemPoseAndCamera` 上 —— 那里才有 camera）。
		/// 本类只负责**换网格**（钩那件实物是隐形代理，图标得自己解析真钩）；1.3+ 没有取景补丁，
		/// 于是这里退回"把网格副本按对角线放大"的近似做法（`ScaleAndCenter`）。
		/// 数值口径 = **占画面宽度的比例**（0.6 ≈ 葡萄那种大小）。设 0 = 不缩放（A/B）。
		/// </summary>
		public static float RopeIconFill = 0.62f;

		/// <inheritdoc cref="RopeIconFill"/>
		public static float HookIconFill = 0.62f;

		[HarmonyPrefix]
		private static bool Prefix(ItemRosterElement rosterElement, bool isFemale, ref MetaMesh __result)
		{
			try
			{
				if (!Enabled)
				{
					return true;
				}
				ItemObject item = rosterElement.EquipmentElement.Item;
				if (item == null)
				{
					return true;
				}
				bool isHook = item.StringId == GrappleFirePatch.HookItemId;
				bool isRope = item.StringId == GrappleFirePatch.RopeItemId;
				if (!isHook && !isRope)
				{
					return true;                  // 不是钩索那两件 → 原版行为
				}

				// 钩那件在世界上是**隐形代理** ⇒ 图标得自己去解析真钩的网格；
				// 绳那件物品网格本身就是那盘绳 ⇒ 走引擎同款取法（`GetItemMeshForInventory` 的弓类分支）。
				MetaMesh source = isHook
					? SpellWorld.ResolveMesh(HookMeshName)
					: rosterElement.EquipmentElement.GetMultiMesh(isFemale, hasGloves: false, needBatchedVersion: false);
				if (source == null)
				{
					DebugLogger.Log($"[Grapple] 图标补丁：{(isHook ? HookMeshName : "rope mesh")} 解析不到 → 交回引擎");
					return true;
				}
				MetaMesh copy = source.CreateCopy();
#if MB2_GE_130
				// 1.3+ 没有取景补丁（那版的取景代码搬走了）⇒ 退回"按对角线放大"的近似做法。
				ScaleAndCenter(copy, isHook ? 0.85f : 1.15f);
#endif
				__result = copy;
				return false;
			}
			catch (Exception)
			{
				return true;                      // 出错退回原版（补丁永不成为新的崩溃源）
			}
		}

	/// <summary>
	/// 把一份网格副本**居中 + 缩放**到目标对角线尺寸（米）—— 只动这一份副本，**碰不到资产本身、也碰不到实机**。
	/// 做法：① 求全部子网格的局部包围盒 → ② `k = 目标对角线 / 实际对角线`（缩放它的帧）
	/// → ③ 把包围盒中心反向平移，使**中心落在帧原点上**（这一条就是"不居中"的修法）。
	/// 🔴 写 `MetaMesh.Frame` 是引擎自己的用法（`AddItem` 对锻造武器就改它的 `Frame` 来抬升图标）；
	///    但**蒙皮网格**写它会把蒙皮绑定冲掉（`IceFxBehavior` 2026-10-07 实机栽过）⇒ 只对**静态件**这么干。
	/// </summary>
	private static void ScaleAndCenter(MetaMesh mesh, float targetDiagonal)
	{
		if (mesh == null || targetDiagonal <= 0.01f)
		{
			return;                            // 目标 ≤0 = 不缩放（A/B 用）
		}
		Vec3 mn = new Vec3(1e6f, 1e6f, 1e6f);
		Vec3 mx = new Vec3(-1e6f, -1e6f, -1e6f);
		for (int i = 0; i < mesh.MeshCount; i++)
		{
			Mesh m = mesh.GetMeshAtIndex(i);
			if (m == null)
			{
				continue;
			}
			mn = Vec3.Vec3Min(mn, m.GetBoundingBoxMin());
			mx = Vec3.Vec3Max(mx, m.GetBoundingBoxMax());
		}
		Vec3 size = mx - mn;
		float diag = size.Length;
		if (diag <= 1e-4f)
		{
			return;                            // 空网格 / 退化包围盒：原样交出去
		}
		float k = targetDiagonal / diag;
		Vec3 center = (mn + mx) * 0.5f;
		MatrixFrame f = mesh.Frame;
		f.rotation.ApplyScaleLocal(k);                          // 先缩放
		f.origin -= f.rotation.TransformToParent(center);       // 再把（已缩放的）中心挪到原点
		mesh.Frame = f;
	}
	}

#if !MB2_GE_130
	/// <summary>
	/// 🔴 **图标取景补丁**（1.2.12 专用）—— 把两件钩索物品**摆到相机正前方**、按"占画面宽度"定尺寸、
	/// 并把**展示轴对准相机**（钩要"平躺"、绳要看得见一圈圈）。
	///
	/// **为什么非打这一刀**（反编译 `TableauCacheManager.GetItemPoseAndCamera` 实证）：
	///   背包图标的取景**按物品类型写死** —— Bow 用 `bow_cam`/`bow_frame`（按真弓 ≈1.2 m 摆）、
	///   Arrows 用 `arrow_cam`/`arrow_frame`（按真箭 ≈0.85 m 摆）；**只有 "goods"（杂货）那条兜底分支**
	///   才自动居中 + 自动定距。我们 13 cm 的绳 / 20 cm 的钩顶着弓/箭的框 ⇒ 小、偏、出画
	///   （实测截图：钩被推到右下、尾环裁掉）。
	///   🔴 也先试过"只在网格副本上居中+缩放"（`GetItemMeshForInventory` 那条）——**缩放生效、居中没生效**
	///   （钩仍从自己的原点往外长 ⇒ 整体偏一侧）⇒ 必须在**手里有 camera 的这个函数**里摆位。
	///
	/// **做法**（位置/尺寸照抄引擎"杂货"分支的思路，但距离按视场角算，不依赖预制相机的旧距离）：
	///   ① 量该物品图标用网格的包围盒 → ② 距离 = (对角线/2) ÷ tan(fov/2) ÷ 填充比例
	///   （⇒ 对角线正好占画面宽度的"填充比例"，见 <see cref="GrappleIconPatch.RopeIconFill"/>）
	///   → ③ 位置 = 相机原点沿**视线反向**（相机帧的 `u`，铁律 35）推这个距离 ⇒ 落在相机正前方（天然居中）
	///   → ④ 朝向 = 网格的**展示轴**对准相机、**长轴**躺在画面横向：
	///      · **绳**：局部 **+Z**（盘轴）→ 相机 ⇒ 看到一圈圈的绳（而不是"管子口"）· +Y → 画面向上
	///      · **钩**：局部 **+X** → 相机 · **+Y（长轴）→ 画面向右** ⇒ 钩**平躺**、三爪朝上张开
	/// </summary>
	[HarmonyPatch(typeof(TableauCacheManager), "GetItemPoseAndCamera")]
	internal static class GrappleIconPosePatch
	{
		[HarmonyPostfix]
		private static void Postfix(ItemObject item, ref Camera camera, ref MatrixFrame itemFrame)
		{
			try
			{
				if (!GrappleIconPatch.Enabled || item == null || camera == null)
				{
					return;
				}
				bool isHook = item.StringId == GrappleFirePatch.HookItemId;
				bool isRope = item.StringId == GrappleFirePatch.RopeItemId;
				if (!isHook && !isRope)
				{
					return;                        // 不是钩索那两件 → 一行都不多做
				}

				MetaMesh mesh = isHook
					? SpellWorld.ResolveMesh(GrappleIconPatch.HookMeshName)
					: item.GetMultiMeshCopy();
				if (mesh == null || !GetLocalBounds(mesh, out Vec3 mn, out Vec3 mx))
				{
					return;
				}
				Vec3 size = mx - mn;
				float diag = size.Length;
				if (diag < 1e-4f)
				{
					return;
				}

				MatrixFrame cam = camera.Frame;
				Vec3 back = cam.rotation.u;        // 相机帧 u = 视线反向（铁律 35）
				Vec3 up = cam.rotation.f;          // 相机帧 f = 画面向上
				Vec3 right = cam.rotation.s;       // 相机帧 s = 画面向右

				// 展示轴（网格局部）→ 世界：view 对准相机、second 躺画面横向
				Vec3 viewL = isHook ? new Vec3(1f, 0f, 0f) : new Vec3(0f, 0f, 1f);
				Vec3 secondL = isHook ? new Vec3(0f, 1f, 0f) : new Vec3(0f, 1f, 0f);
				Vec3 thirdL = Vec3.CrossProduct(viewL, secondL);          // 右手系补齐
				Vec3 secondW = isHook ? right : up;                        // 钩：长轴躺横向 · 绳：+Y 朝上
				Vec3 thirdW = Vec3.CrossProduct(back, secondW);

				Mat3 R = Mat3.Identity;
				R.s = back * viewL.x + secondW * viewL.y + thirdW * viewL.z;
				R.f = back * secondL.x + secondW * secondL.y + thirdW * secondL.z;
				R.u = back * thirdL.x + secondW * thirdL.y + thirdW * thirdL.z;

				// 距离：让"对角线"占画面宽度的 fill 比例
				float fov = 1f;
				try { fov = camera.HorizontalFov; } catch (Exception) { }
				if (fov < 0.05f || fov > 3f) { fov = 1f; }                 // 读数异常就退回 1 rad
				float fill = isHook ? GrappleIconPatch.HookIconFill : GrappleIconPatch.RopeIconFill;
				if (fill <= 0.01f) { fill = 0.62f; }
				float dist = (diag * 0.5f) / MathF.Tan(fov * 0.5f) / fill;

				// 物品的**包围盒中心**落在相机正前方那个点上（帧原点也摆在那儿 ⇒ 天然居中）
				Vec3 centerLocal = (mn + mx) * 0.5f;
				Vec3 at = cam.origin - back * dist;
				itemFrame = new MatrixFrame(R, at - R.TransformToParent(centerLocal));
			}
			catch (Exception)
			{
				// 出任何问题就保持引擎原来算好的帧（最多图标还是老样子，绝不崩）
			}
		}

		/// <summary>网格全部子件的**局部**包围盒（图标摆位用；空网格 = false）。</summary>
		private static bool GetLocalBounds(MetaMesh mesh, out Vec3 mn, out Vec3 mx)
		{
			mn = new Vec3(1e6f, 1e6f, 1e6f);
			mx = new Vec3(-1e6f, -1e6f, -1e6f);
			for (int i = 0; i < mesh.MeshCount; i++)
			{
				Mesh m = mesh.GetMeshAtIndex(i);
				if (m == null)
				{
					continue;
				}
				mn = Vec3.Vec3Min(mn, m.GetBoundingBoxMin());
				mx = Vec3.Vec3Max(mx, m.GetBoundingBoxMax());
			}
			return mx.x > mn.x && mx.y > mn.y && mx.z > mn.z;
		}
	}
#endif
}
