using System;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

// 🔴 **命名空间 = `LivingWorldNpcs`（平铺），不是 `LivingWorldNpcs.Camera`** ——
//    本目录既有文件（SpringArmCameraView 等）都是平铺的；而且 `LivingWorldNpcs.Camera` 会与
//    引擎类型 `TaleWorlds.Engine.Camera` **撞名**（`Camera.CreateCamera()` 之类全部报 CS0118）。
namespace LivingWorldNpcs
{
	/// <summary>
	/// **"相机现在看向哪"的提供者** —— 谁接管了相机、谁就实现它。
	/// 目前唯一的实现 = <see cref="LivingWorldNpcs.Flight.PlayerFlightBehavior"/>（飞行相机由鼠标自驱动）；
	/// 演出机位（<see cref="SpringArmCameraView"/>）暂时没注册（它自己算帧、不对外供方向）。
	/// </summary>
	public interface ICameraLookProvider
	{
		/// <summary>把"相机中心朝向"给出去（世界空间单位向量）。返回 false = 这一帧拿不到。</summary>
		bool TryGetLook(out Vec3 forward);
	}

	/// <summary>
	/// **相机视线的唯一入口**（CLAUDE.md 铁律 35 的落地件，2026-09-24 立）。
	///
	/// 为什么要有这个类：同一个坑已经栽过四次（飞行 ×2 · 法术 ×1 · 飞行中施法 ×1），
	/// 每次都是"自己去读一个相机量"，而**两台相机**的口径不同：
	///
	/// | | 引擎相机 | 自定义相机（`MissionScreen.CustomCamera != null`） |
	/// |---|---|---|
	/// | 鼠标 look | 引擎处理 ⇒ `CameraBearing/CameraElevation` **实时** | **引擎整段跳过**（`CheckForUpdateCamera` 只把自定义相机塞给 CombatCamera）⇒ 那两个角度**冻在接管那一刻** |
	/// | 视线怎么取 | `Mat3.Identity` 绕 Up 转 bearing、绕 Side 转 elevation，取 `.f`（照抄 `MissionMainAgentController.LookTick`） | **问接管方自己**（<see cref="ICameraLookProvider"/>） |
	///
	/// 🔴 **禁止**用 `Mission.GetCameraFrame()` 取方向：实测它的基向量是 `.f` = **"上"**、
	///    `.u` = **视线的反向**、`.s` = 右向 —— 要视线得写 `-rotation.u`，极易再错一次，所以别碰。
	///    （`.origin` 是**自定义相机的位置**，取位置/算距离没问题。）
	///
	/// 用法：<c>if (CameraLook.TryGet(out Vec3 look)) { /* 用 look */ }</c>，
	/// 失败 = 这一帧没有可信方向（接管中但没人注册）⇒ 调用方回退（身体朝向 / 保持上一帧），**不要**猜一个。
	/// </summary>
	public static class CameraLook
	{
		/// <summary>当前接管相机的那位（没接管 = null）。接管方进入时挂、退出时清。</summary>
		public static ICameraLookProvider Provider { get; set; }

		/// <summary>
		/// 相机视线。优先级：① **接管方自己**（它知道鼠标把镜头转到哪了）② 没接管时用引擎相机角度。
		/// 返回 false = 这一帧没有可信来源。
		/// </summary>
		public static bool TryGet(out Vec3 forward)
		{
			forward = Vec3.Zero;

			ICameraLookProvider provider = Provider;
			if (provider != null)
			{
				try
				{
					if (provider.TryGetLook(out forward) && forward.LengthSquared > 1e-6f)
					{
						return true;
					}
				}
				catch (Exception)
				{
					// 接管方这一帧拿不到 —— 落到引擎分支（多半也拿不到，交给调用方回退）
				}
			}

			return TryGetEngineLook(out forward);
		}

		/// <summary>
		/// **引擎相机**的视线（照抄引擎 `MissionMainAgentController.LookTick`）。
		/// 🔴 **接管期间返回 false**（那时 bearing/elevation 是冻的旧值，读了就是"画面 A、计算 B"）。
		/// </summary>
		public static bool TryGetEngineLook(out Vec3 forward)
		{
			forward = Vec3.Zero;
			try
			{
				if (ScreenManager.TopScreen is MissionScreen ms)
				{
					if (ms.CustomCamera != null)
					{
						return false;   // 铁律 35：接管中，引擎角度不可信
					}
					Mat3 m = Mat3.Identity;
					m.RotateAboutUp(ms.CameraBearing);
					m.RotateAboutSide(ms.CameraElevation);
					if (m.f.LengthSquared > 0.0001f)
					{
						forward = m.f.NormalizedCopy();
						return true;
					}
				}
			}
			catch (Exception)
			{
				// 取不到就交给调用方回退
			}
			return false;
		}
	}
}
