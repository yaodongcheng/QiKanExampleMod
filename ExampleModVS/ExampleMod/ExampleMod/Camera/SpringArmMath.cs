using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 弹簧臂相机的**纯数学**（2026-09-21 从 <c>SpringArmCameraView.ApplySpringArmCamera</c> 抽出来共用）。
    ///
    /// 🔴 **为什么抽出来**：飞行运动相机（`Flight/FlightCameraRig.cs`）要用同一套算法。
    ///    按铁律 18 的精神（同一件事只允许一份实现），**不许复制第二份** —— 抽成静态纯函数，
    ///    两边都调它：`SpringArmCameraView`（原主）与 `FlightCameraRig`（飞行）。
    ///
    /// **它算什么**（与原实现逐行一致，只是把 `targetAgent` 变成参数）：
    /// <code>
    /// 锚点  = 角色 LookFrame 原点（脚底）+ 眼高
    /// pivot = 锚点 + (s·PivotX + f·PivotY + u·PivotZ)
    /// 臂旋转 = 角色 LookFrame.rotation （世界模式则用 Identity）→ RotateAboutUp(−ArmYaw) → RotateAboutSide(ArmPitch)
    /// 相机位 = pivot − 臂前向 × ArmLength + (s·SocketX + f·SocketY + u·SocketZ)
    /// 相机旋 = 臂旋转 → 叠加 SelfYaw/Pitch/Roll → RotateAboutSide(π/2)「相机朝天修正」
    /// </code>
    ///
    /// 🔴 **`IsAnchorWorld` 的坑（照抄原样，没顺手改）**：原实现在 <c>if (p.IsAnchorWorld)</c> **之前**
    ///    就写了 `p.IsAnchorWorld = false;` —— 所以世界模式分支是**死代码**，永远走角色朝向那一支。
    ///    本次抽出来时**保留了原行为**（本函数尊重传进来的值，由调用方决定；`SpringArmCameraView`
    ///    在调用前照旧强制置 false，行为与改动前逐字节一致）。
    ///    ⇒ **世界模式在本项目里从未被验证过**，要用得自己验。
    /// </summary>
    public static class SpringArmMath
    {
        /// <summary>站姿眼高（米）—— 原实现里的常量，别改，改了两边一起变。</summary>
        public const float StandEyeHeight = 1.4626f;

        /// <summary>坐姿眼高（米），由 <c>VariableManager</c> 的 "IsSit" 变量触发。</summary>
        public const float SitEyeHeight = 1.024f;

        private const float DegToRad = MathF.PI / 180.0f;

        /// <summary>
        /// 算出相机该摆在哪个帧、FOV 多少（**纯函数，不碰引擎对象**）。
        /// </summary>
        /// <param name="targetAgent">锚点角色（不能为 null —— 调用方先判）。</param>
        /// <param name="p">弹簧臂参数。</param>
        /// <param name="frame">输出：相机的世界帧。</param>
        /// <param name="fovDeg">输出：垂直 FOV（**度** —— 调用方自己转弧度，因为 SetFovVertical 要弧度）。</param>
        public static void ComputeFrame(Agent targetAgent, in SpringArmCameraParam p,
                                        out MatrixFrame frame, out float fovDeg)
        {
            fovDeg = p.Fov;

            MatrixFrame anchorFrame = targetAgent.LookFrame;

            float eyeHeightOffset = StandEyeHeight;
            // 坐姿是 VariableManager 里的标志位（表演系统在用）。🔴 Character 可能为 null（模板 NPC），
            // 原实现没判 —— 这里补上，否则模板 NPC 上跑会 NRE。
            try
            {
                if (targetAgent.Character != null
                    && VariableManager.GetV(targetAgent.Character.StringId, "IsSit") == "True")
                {
                    eyeHeightOffset = SitEyeHeight;
                }
            }
            catch
            {
                // 变量系统取不到就按站姿算 —— 眼高不该阻断相机
            }

            Vec3 anchorEyePos = anchorFrame.origin;
            anchorEyePos.z += eyeHeightOffset;

            Vec3 pivotOffset = (anchorFrame.rotation.s * p.PivotX)
                             + (anchorFrame.rotation.f * p.PivotY)
                             + (anchorFrame.rotation.u * p.PivotZ);
            Vec3 pivotPos = anchorEyePos + pivotOffset;

            Mat3 armRotMat;
            if (p.IsAnchorWorld)
            {
                // 世界模式：完全由参数控制，不跟随角色转向。⚠️ 本项目从未验证过这条（见类型注释）
                armRotMat = Mat3.Identity;
                armRotMat.RotateAboutUp(p.ArmYaw * DegToRad);
                armRotMat.RotateAboutSide(p.ArmPitch * DegToRad);
            }
            else
            {
                armRotMat = targetAgent.LookFrame.rotation;
                armRotMat.RotateAboutUp(-p.ArmYaw * DegToRad);
                armRotMat.RotateAboutSide(p.ArmPitch * DegToRad);
            }

            Vec3 socketOffset = (armRotMat.s * p.SocketX)
                              + (armRotMat.f * p.SocketY)
                              + (armRotMat.u * p.SocketZ);

            Vec3 cameraPos = pivotPos - (armRotMat.f * p.ArmLength) + socketOffset;

            Mat3 finalCamRot = armRotMat;
            if (Math.Abs(p.SelfYaw) > 0.001f) finalCamRot.RotateAboutUp(-p.SelfYaw * DegToRad);
            if (Math.Abs(p.SelfPitch) > 0.001f) finalCamRot.RotateAboutSide(p.SelfPitch * DegToRad);
            if (Math.Abs(p.SelfRoll) > 0.001f) finalCamRot.RotateAboutForward(p.SelfRoll * DegToRad);
            finalCamRot.RotateAboutSide(MathF.PI / 2);   // 相机朝天修正（原实现原样保留）

            finalCamRot.Orthonormalize();

            frame = MatrixFrame.Identity;
            frame.origin = cameraPos;
            frame.rotation = finalCamRot;
        }

        /// <summary>两组机位参数之间线性插值（<paramref name="t"/> = 0 取 a，1 取 b）。相机渐变用。</summary>
        public static SpringArmCameraParam Lerp(in SpringArmCameraParam a, in SpringArmCameraParam b, float t)
        {
            SpringArmCameraParam r = default;
            r.PivotX = a.PivotX + (b.PivotX - a.PivotX) * t;
            r.PivotY = a.PivotY + (b.PivotY - a.PivotY) * t;
            r.PivotZ = a.PivotZ + (b.PivotZ - a.PivotZ) * t;
            r.ArmLength = a.ArmLength + (b.ArmLength - a.ArmLength) * t;
            r.ArmYaw = a.ArmYaw + (b.ArmYaw - a.ArmYaw) * t;
            r.ArmPitch = a.ArmPitch + (b.ArmPitch - a.ArmPitch) * t;
            r.SocketX = a.SocketX + (b.SocketX - a.SocketX) * t;
            r.SocketY = a.SocketY + (b.SocketY - a.SocketY) * t;
            r.SocketZ = a.SocketZ + (b.SocketZ - a.SocketZ) * t;
            r.SelfYaw = a.SelfYaw + (b.SelfYaw - a.SelfYaw) * t;
            r.SelfPitch = a.SelfPitch + (b.SelfPitch - a.SelfPitch) * t;
            r.SelfRoll = a.SelfRoll + (b.SelfRoll - a.SelfRoll) * t;
            r.Fov = a.Fov + (b.Fov - a.Fov) * t;
            r.IsAnchorWorld = t < 0.5f ? a.IsAnchorWorld : b.IsAnchorWorld;
            return r;
        }

        /// <summary>平滑（smoothstep）：两端速度为 0，中段快 —— 渐变看着不"顿"。</summary>
        public static float Ease(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return t * t * (3f - 2f * t);
        }
    }
}
