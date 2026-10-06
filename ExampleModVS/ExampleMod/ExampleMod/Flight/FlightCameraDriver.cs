using System;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.Flight
{
    /// <summary>
    /// **飞行相机驱动**（2026-10-05 阶段 4）—— 行为层要的那几个动作，两台机器各实现一份：
    /// <list type="bullet">
    ///   <item><see cref="FlightCameraRig"/> = **旧机器**（自己摆相机、自己读鼠标；阶段 2 起向相机服务登记为
    ///         "外部持有者"）；</item>
    ///   <item><see cref="ServiceFlightDriver"/> = **合并机器**（走 <see cref="CameraService"/> +
    ///         <see cref="SpringArmRig"/>，与跟随/钩索同一份机器）。</item>
    /// </list>
    /// 由 <see cref="FlightTuning.UseMergedRig"/> 选（**默认 0 = 旧机器**；实机 A/B 合格再切 1）。
    /// </summary>
    public interface IFlightCameraDriver
    {
        /// <summary>接管相机（返回 false = 没接上，继续用引擎相机）。</summary>
        bool Enter(Agent agent);

        /// <summary>立刻归还（异常 / 收摊路径用；正常走 <see cref="BeginHandBack"/> 渐变）。</summary>
        void Exit();

        /// <summary>开始"渐变回默认相机"（滑完自动撒手）。</summary>
        void BeginHandBack();

        /// <summary>喂鼠标增量（**合并机器自己读鼠标 ⇒ 空实现**，调用方不用改）。</summary>
        void ApplyLook(float mouseDx, float mouseDy);

        /// <summary>喂本帧运动量（竖直速率 / 航向角速度 → FOV·臂长·侧倾）。</summary>
        void SetMotion(in SpringArmMotion motion);

        /// <summary>切机位（同一台机器内换 case；只有真的变了才重启渐变）。</summary>
        void SetPreset(FlightCamPreset preset, float transitionSeconds);

        /// <summary>每帧推进（**合并机器由相机服务的每帧 Tick 推进 ⇒ 空实现**）。</summary>
        void Tick(Agent agent, float dt);

        /// <summary>是否正在接管。</summary>
        bool IsActive { get; }

        /// <summary>归还渐变是否在走。</summary>
        bool IsHandingBack { get; }

        /// <summary>当前朝向（诊断用）。</summary>
        float LookYaw { get; }

        /// <summary>相机前向 / 右向（飞行方向与施法方向都用它）。</summary>
        bool TryGetBasis(out Vec3 forward, out Vec3 right);
    }

    /// <summary>
    /// **合并机器的飞行驱动**（阶段 4）—— 把飞行接到相机服务上：于是**全项目只剩一个
    /// `CustomCamera` 写者 + 一个机器**（跟随 / 钩索 / 飞行共用 <see cref="SpringArmRig"/>）。
    ///
    /// 与旧机器的对应关系：
    /// <list type="bullet">
    ///   <item>`Enter` = 引擎机位起播（`Seed=Engine` ⇒ 接管瞬间方向逐度照抄引擎、不甩）+ 立刻切巡航档（渐变）；</item>
    ///   <item>鼠标 / 弹簧滞后 / 运动驱动 / 机位渐变 = 机器按 `fly_*` 行自动生效（**这里不用管**）；</item>
    ///   <item>`BeginHandBack` = 请求渐变归还（方向归还由机器 chase 引擎实时值——与钩索同一套）。</item>
    /// </list>
    /// 🔴 **A/B 时留意一处口径差**（旧机器 vs 合并机器）：写回引擎朝向的**时机** ——
    ///    旧机器是"滑行结束时写回"、合并机器是"滑行开始时写回"（与跟随/钩索那条路一致）。
    ///    若实机对比发现落地瞬间镜头有差，先看这里（它是 `_policy.WriteBackLook` 那一路的实现细节）。
    /// </summary>
    internal sealed class ServiceFlightDriver : IFlightCameraDriver
    {
        private const string Owner = "flight";
        private FlightCamPreset _preset = FlightCamPreset.Cruise;

        /// <summary>归还策略：飞行**没有冻结/解冻**（玩家一直在操作），所以不清特殊修正、不预置俯仰；
        /// 写回朝向跟着 `FlightTuning` 那两个开关走（与旧机器一致）。</summary>
        private static CameraReturnPolicy Policy => new CameraReturnPolicy
        {
            WriteBackLook = FlightTuning.UseCamHandover && FlightTuning.CamHandBackLook,
            ClearSpecial = false,
            PredictReset = false,
        };

        public bool Enter(Agent agent)
        {
            // 已经有人在管相机（钩索 / 演出）⇒ 飞行是主要玩法，**顶掉它**（旧机器就是直接抢的；
            // 服务会先让那位放手，日志可查）。
            if (CameraService.IsHeld && !CameraService.IsHeldBy(Owner))
            {
                DebugLogger.Log($"[FlightCam] 起飞顶掉当前相机持有者 {CameraService.Holder}（飞行优先）");
                CameraService.Stop();
            }

            // 方向逐度照抄引擎机位（接管不甩、机身本来就朝那边）+ 不限时（何时还由玩法决定）
            if (!CameraService.PlayEnginePose(agent, 0f, Owner, Policy))
                return false;
            CameraService.SetTimeout(0f);

            _preset = FlightCamPreset.Cruise;
            Switch(_preset, FlightTuning.UseCamHandover ? FlightTuning.CamBlendIn : 0.01f);
            return true;
        }

        public void Exit() => CameraService.Stop();

        /// <summary>归还滑行时长用 <see cref="FlightTuning.CamBlendIn"/>（与旧机器的 `_blendDur` 同源）。</summary>
        public void BeginHandBack() => CameraService.RequestHandBack(FlightTuning.CamBlendIn);

        /// <summary>合并机器**自己读鼠标**（`fly_*` 行的 `MouseLook=1`）⇒ 这条是空实现。</summary>
        public void ApplyLook(float mouseDx, float mouseDy) { }

        public void SetMotion(in SpringArmMotion motion) => CameraService.SetMotion(in motion);

        public void SetPreset(FlightCamPreset preset, float transitionSeconds)
        {
            if (preset == _preset)
                return;
            _preset = preset;
            Switch(preset, transitionSeconds);
        }

        private static void Switch(FlightCamPreset preset, float blendSeconds)
        {
            string caseName = "fly_" + FlightCameraRig.PresetNames[(int)preset];
            CameraService.Switch(caseName, Math.Max(0.01f, blendSeconds));
        }

        /// <summary>推进由相机服务负责（`SpringArmCameraView.OnMissionTick` 每帧调 `CameraService.Tick`）⇒ 空实现。</summary>
        public void Tick(Agent agent, float dt) { }

        public bool IsActive => CameraService.IsHeldBy(Owner);

        public bool IsHandingBack => CameraService.IsReturning;

        public float LookYaw => CameraService.Machine.LookYaw;

        public bool TryGetBasis(out Vec3 forward, out Vec3 right) => CameraService.TryGetBasis(out forward, out right);
    }
}
