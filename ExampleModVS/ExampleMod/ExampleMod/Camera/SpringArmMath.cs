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
    ///
    /// 🔴 **本文件还有两件"给所有相机共用"的东西**（2026-09-27/28 加，都在本类之外、同命名空间）：
    /// · <see cref="SpringArmMotion"/> + <see cref="WithMotion"/> = **运动驱动**输入口
    ///   （竖直速率 → FOV/臂长、航向角速度 → 侧倾；全零 = 无影响）
    /// · <see cref="SpringArmLagState"/> + <see cref="LagToward"/> = **弹簧跟随**（相机位置滞后）
    ///   —— 对应 UE `SpringArmComponent` 的 `CameraLagSpeed` / `CameraLagMaxDistance`
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
            r.LagSpeed = a.LagSpeed + (b.LagSpeed - a.LagSpeed) * t;
            r.LagMaxDistance = a.LagMaxDistance + (b.LagMaxDistance - a.LagMaxDistance) * t;
            r.FovPerVz = a.FovPerVz + (b.FovPerVz - a.FovPerVz) * t;
            r.ArmPerVz = a.ArmPerVz + (b.ArmPerVz - a.ArmPerVz) * t;
            r.RollPerYawRate = a.RollPerYawRate + (b.RollPerYawRate - a.RollPerYawRate) * t;
            return r;
        }

        /// <summary>竖直驱动量的**上限**（m/s）—— 出机坠落能到 36 m/s，不钳的话 FOV 会被拉到失真。</summary>
        public const float MotionVzCap = 20f;

        /// <summary>
        /// 把运动量**叠加**到一帧的机位参数上（纯函数，不改调用方那一份）。
        /// 消费者：飞行相机（`Flight/FlightCameraRig`）；演出相机不喂运动量 ⇒ 结果与传进来的完全一样。
        /// </summary>
        public static SpringArmCameraParam WithMotion(in SpringArmCameraParam p, in SpringArmMotion m)
        {
            SpringArmCameraParam r = p;

            float vz = Math.Min(Math.Abs(m.Vz), MotionVzCap);
            if (vz > 0f)
            {
                r.Fov += r.FovPerVz * vz;
                r.ArmLength += r.ArmPerVz * vz;
            }
            r.Fov = MBMath.ClampFloat(r.Fov, 20f, 110f);      // 别让叠加把视场拉出可用区间

            if (m.YawRate != 0f)
                r.SelfRoll += r.RollPerYawRate * m.YawRate;

            return r;
        }

        /// <summary>
        /// 弹簧跟随（相机**位置**滞后）—— 口径照抄 UE 的 <c>FMath::VInterpTo</c>：
        /// 每帧朝目标走 <c>速度 × dt</c> 的比例、钳到 1（dt 很大时直接到位，不会过冲）。
        ///
        /// **谁在用**：飞行相机（<c>Flight/FlightCameraRig</c>）—— 就是 UE 那边
        /// <c>SpringArmComponent.CameraLagSpeed</c> 的等价物。演出相机不填 LagSpeed ⇒ 不走这条路。
        ///
        /// **它带来什么**：相机不再"焊死"在角色身上，而是**拖着一条尾巴**追。稳态拖尾距离 = 速度 ÷ LagSpeed。
        /// 于是"角色实际航向 vs 镜头方向"的那个夹角**会显示在画面上**——
        /// 转弯时角色先滑到画面一侧、航向追上后再滑回中间（就是 UE 超人那套"弹性"）。
        /// </summary>
        public static Vec3 LagToward(Vec3 current, Vec3 target, float lagSpeed, float dt)
        {
            if (lagSpeed <= 0f)
                return target;

            float a = lagSpeed * dt;
            if (a >= 1f)
                return target;
            return current + (target - current) * a;
        }

        /// <summary>平滑（smoothstep）：两端速度为 0，中段快 —— 渐变看着不"顿"。</summary>
        public static float Ease(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return t * t * (3f - 2f * t);
        }
    }

    /// <summary>
    /// **本帧的运动量**（2026-09-28）—— 运动驱动的唯一输入口：谁有这些量谁填，没有就全零（= 无影响）。
    ///
    /// 🔴 **为什么是这两个量、不是"速度"**：我们的飞行速度**大小**是离散的（0 / 9 / 26，Shift 一按一换，
    /// 没有加速过程）⇒ 拿它驱 FOV 只会得到三个台阶。真正**连续**的只有：
    /// · <see cref="Vz"/> = 竖直速率（爬升/俯冲多快）—— 俯冲时拉广视场 = 速度感
    /// · <see cref="YawRate"/> = 航向角速度（转得多快）—— 这正是 UE 用"角速度驱动侧倾"的那个量，
    ///   我们此前只能按 A/D 键近似（见 `SuperheroFlight飞行系统_解析.md` §5.2）
    /// </summary>
    public struct SpringArmMotion
    {
        /// <summary>竖直速率（m/s）—— **升降都算**（<see cref="SpringArmMath.WithMotion"/> 内部取绝对值）。</summary>
        public float Vz;
        /// <summary>航向角速度（度/秒，正值 = 向左转）。建议先平滑再用（UE 那套是 `FInterpTo(..., 5)`）。</summary>
        public float YawRate;
        /// <summary>速度大小（m/s）—— 目前只做诊断用（飞行只有 0/9/26 三档，不拿它驱参数）。</summary>
        public float Speed;
    }

    /// <summary>
    /// **弹簧跟随的状态**（相机位置滞后）—— 相机侧唯一需要自己存的东西（2026-09-28 抽成共用件）。
    ///
    /// 抽出来的理由：滞后的**数学**（<see cref="SpringArmMath.LagToward"/>）本来就在共用文件里，
    /// 但它的**状态**（滞后锚点 / 偏移 / 是否播种）原来长在飞行相机里 ⇒ 别的相机想用就得再抄一份。
    /// 现在任何相机（飞行 / 演出跟随 / 以后的坐骑载具镜头）**填个 LagSpeed、再每帧调一次
    /// <see cref="Update"/>、把返回值加到相机位置上**，就有了同样的弹性。
    /// </summary>
    public struct SpringArmLagState
    {
        /// <summary>偏移的**渐隐/渐显速率**（1/秒）—— 松开滞后（瞄准档 / 归还相机 / LagSpeed=0）时
        /// 偏移以这个速率回到 0，而不是一帧跳回去（冲刺尾巴 6.5 米，硬关 = 镜头被弹一下）。</summary>
        public const float FadeSpeed = 8f;

        private Vec3 _anchor;
        private Vec3 _offset;
        private bool _seeded;

        /// <summary>当前**实际生效**的滞后偏移（世界向量；诊断用 —— 调用方把它加在相机位置上）。</summary>
        public Vec3 Offset => _offset;

        /// <summary>清空状态（接管/收摊时调 —— 下次当帧对齐，不带着上次的尾巴）。</summary>
        public void Reset()
        {
            _anchor = Vec3.Zero;
            _offset = Vec3.Zero;
            _seeded = false;
        }

        /// <summary>
        /// 推进一帧，返回要加在相机位置上的偏移。
        /// </summary>
        /// <param name="anchor">理想锚点（角色位置；眼高是常量、与滞后量无关，直接给脚底即可）。</param>
        /// <param name="lagSpeed">滞后速率（≤0 = 不滞后，偏移渐隐回 0）。</param>
        /// <param name="maxDistance">拖尾上限（米；≤0 = 不限）—— 对应 UE `CameraLagMaxDistance`。</param>
        /// <param name="dt">帧时长。</param>
        /// <param name="fadeScale">额外淡出系数（归还渐变期间传 `1 − 进度` ⇒ 保证撒手那一刻正好归零）。</param>
        public Vec3 Update(Vec3 anchor, float lagSpeed, float maxDistance, float dt, float fadeScale = 1f)
        {
            Vec3 target = Vec3.Zero;
            if (lagSpeed > 0f)
            {
                if (!_seeded)
                {
                    _anchor = anchor;                 // 首帧不插值（否则会从"上一次的位置"慢慢滑过来）
                    _seeded = true;
                }
                else
                {
                    _anchor = SpringArmMath.LagToward(_anchor, anchor, lagSpeed, dt);
                }

                target = _anchor - anchor;
                if (maxDistance > 0f && target.LengthSquared > maxDistance * maxDistance)
                    target = target.NormalizedCopy() * maxDistance;
            }
            else
            {
                _seeded = false;
            }

            _offset = SpringArmMath.LagToward(_offset, target, FadeSpeed, dt);
            if (fadeScale < 1f)
                _offset = _offset * fadeScale;
            return _offset;
        }
    }
}
