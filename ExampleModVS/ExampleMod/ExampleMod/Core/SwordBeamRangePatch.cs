// ═══════════════════════════════════════════════════════════════════════════
// SwordBeam（第三方剑气 mod）飞行距离 —— 兼容/诊断补丁（2026-09-23 立）
//
// 目的：SwordBeam 的剑气**自己管距离**（`BeamConfig.Distance => 21f` 写死，到 21 米就
//       `Entity.Remove(0)` 自删）—— 这正是我们法印工程要验证的东西：
//       **自管实体的飞行物到底有没有引擎上限**。把它的 21 米放开，剑气能飞多远就一目了然。
//       （法印侧的完整背景见 `plans/法印施法体系-实施计划.md` §十六。）
//
// 🔴 边界（自用/诊断可以，别对外）：距离 / 速度 / 尺寸在该 mod 里是**赞助版（付费）功能**，
//    本补丁相当于把那三个旋钮里的"距离"打开。本地验证用没问题，**不要打进对外发布的内容**。
//
// 实现：
//   · 目标 = `SwordBeam.Settings.BeamConfig` 的 `Distance` 属性 getter（`internal static` 类，
//     所以按**类型名反射**找，不引它的程序集）。
//   · postfix 直接换掉返回值 ⇒ `SwordBeamProjectile` 构造里的
//     `_maxDistance = BeamConfig.Distance * releaseProfile.Scale` 随之变大。
//   · ⚠️ `_maxLifetime = Math.Max(15f, _maxDistance / speed + 1f)`：距离放开后寿命跟着涨，
//     所以不会出现"距离没到但寿命先到"的怪象（实机若见"飞一半没了"，先查这里）。
//   · 🔴 **没装该 mod 时静默跳过**（反射查不到类型就返回），绝不影响其它任何东西。
//
// 用法：跟其它 `custom.*` 一样（首参可弃）——
//   custom.swordbeam_range 300     把距离改成 300 米
//   custom.swordbeam_range off     还原成它自己的 21 米
//   custom.swordbeam_range         看当前值
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>SwordBeam 飞行距离补丁（可选目标：没装就静默跳过）。</summary>
    public static class SwordBeamRangePatch
    {
        /// <summary>补丁总开关（false = 用它自己的 21 米）。</summary>
        public static bool Enabled = true;

        /// <summary>接管后的飞行距离（米）。诊断默认给 200 米（27 m/s ⇒ 约 7.4 秒）。</summary>
        public static float DistanceOverride = 200f;

        /// <summary>目标是否被找到并接管（控制台用来报告状态）。</summary>
        public static bool Installed;

        private const string TargetTypeName = "SwordBeam.Settings.BeamConfig";

        /// <summary>在 <c>MySubModule.OnSubModuleLoad</c> 里调用（Harmony 装完之后）。</summary>
        public static void TryInstall(Harmony harmony)
        {
            try
            {
                Type configType = AccessTools.TypeByName(TargetTypeName);
                if (configType == null)
                {
                    DebugLogger.Log("[SwordBeamPatch] 未检测到 SwordBeam（或它换了类型名）—— 跳过，不影响任何东西");
                    return;
                }
                MethodInfo getter = AccessTools.PropertyGetter(configType, "Distance");
                if (getter == null)
                {
                    DebugLogger.Log($"[SwordBeamPatch] 找到 {TargetTypeName} 但没有 Distance 属性 —— 跳过（该 mod 可能改版了）");
                    return;
                }
                MethodInfo postfix = AccessTools.Method(typeof(SwordBeamRangePatch), nameof(DistancePostfix));
                harmony.Patch(getter, postfix: new HarmonyMethod(postfix));
                Installed = true;
                DebugLogger.Log($"[SwordBeamPatch] 已接管 SwordBeam 的飞行距离：{DistanceOverride:F0} 米"
                    + "（它自己写死 21 米；改值用 custom.swordbeam_range <米>）");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[SwordBeamPatch] 安装失败（已忽略，不影响游戏）：{ex.GetType().Name} {ex.Message}");
            }
        }

        /// <summary>属性 getter 的 postfix：换掉返回值。</summary>
        private static void DistancePostfix(ref float __result)
        {
            try
            {
                if (Enabled)
                {
                    __result = DistanceOverride;
                }
            }
            catch (Exception)
            {
                // 绝不能抛 —— 这个 getter 在剑气构造里调用
            }
        }
    }

    /// <summary>控制台入口（返回文本纯英文；详情走 DebugLogger）。</summary>
    public static class SwordBeamRangeCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("swordbeam_range", "custom")]
        public static string SetRange(List<string> args)
        {
            try
            {
                if (!SwordBeamRangePatch.Installed)
                {
                    return "swordbeam_not_found: SwordBeam mod not installed (or its BeamConfig type changed)";
                }
                if (args == null || args.Count == 0)
                {
                    return Status();
                }
                if (string.Equals(args[0], "off", StringComparison.OrdinalIgnoreCase))
                {
                    SwordBeamRangePatch.Enabled = false;
                    DebugLogger.Log("[SwordBeamPatch] 已还原成它自己的 21 米");
                    return Status();
                }
                float meters;
                if (!float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out meters))
                {
                    // 首参可弃（占位容忍）：给了 1 之类解析不出来的 → 只报状态，不报错
                    return Status() + $" [note: '{args[0]}' is not a number -> showing status]";
                }
                SwordBeamRangePatch.Enabled = true;
                SwordBeamRangePatch.DistanceOverride = Math.Max(5f, Math.Min(5000f, meters));
                DebugLogger.Log($"[SwordBeamPatch] 剑气飞行距离 → {SwordBeamRangePatch.DistanceOverride:F0} 米"
                    + "（寿命会跟着自动放大，见文件头注释）");
                return Status();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[SwordBeamPatch] 命令异常: {ex}");
                return "swordbeam_range_failed: " + ex.Message;
            }
        }

        private static string Status()
        {
            return $"swordbeam_range enabled={SwordBeamRangePatch.Enabled} "
                + $"distance={SwordBeamRangePatch.DistanceOverride:F0} (mod default 21)";
        }
    }
}
