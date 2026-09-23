// ═══════════════════════════════════════════════════════════════════════════
// SwordBeam（第三方剑气 mod）飞行距离 —— 兼容/诊断补丁（2026-09-23 立，当日删除后按用户要求重加）
//
// 目的：SwordBeam 的剑气**自己管距离**（`BeamConfig.Distance => 21f` 写死，到 21 米就
//       `Entity.Remove(0)` 自删）—— 这正是我们法印工程要验证的东西：
//       **自管实体的飞行物到底有没有引擎上限**。把它的 21 米放开，剑气能飞多远就一目了然。
//       （法印侧的完整背景见 `plans/法印施法体系-实施计划.md` §十六。）
//
// 🔴🔴 **判据 = 引擎到底把它加载了没 —— 不看文件路径、不看模块列表**（2026-09-23 用户裁定）：
//   ① **程序集是否已装配进 AppDomain**（枚举 `AppDomain.CurrentDomain.GetAssemblies()` 找
//      `SwordBeam`）—— 这才是"骑砍2真的加载了它"的直接证据。时机没问题：
//      引擎 `LoadSubModules` 先把所有**激活**模块的 DLL 装配进 AppDomain，**全部装配完**才依次
//      回调 `OnSubModuleLoad`（反编译实证，本工程 `AgentDamageModelCultureNullFix` 同样靠这条）。
//      **不许用 `ModuleHelper.GetModuleInfo(id) != null`** —— 它查「物理安装目录扫描表」，
//      文件夹在就返回，**与 launcher 勾没勾选无关**（2026-09-07 本工程因此误判崩过）。
//      **更不许判文件存不存在** —— 装了没启用 = DLL 没进 AppDomain = 我们的补丁没有意义。
//   ② **类型与属性真的可解析**（从那个程序集里拿 `SwordBeam.Settings.BeamConfig` 的 `Distance` getter）
//      —— 挡住"该 mod 改版换了内部名"。
//   只有两闸都过才碰 `harmony.Patch` —— 绝不把 null 目标递进去（那会抛异常并掐断整个挂载流程，
//   见 CLAUDE.md「Harmony 补丁目标找不到 = 当场抛异常、掐断整个 PatchAll」那条教训）。
//
// 🔴 边界（自用/诊断可以，别对外）：距离 / 速度 / 尺寸 / 光强 这些在该 mod 里是**赞助版（付费）功能**，
//    本补丁相当于把那几个旋钮打开。本地验证用没问题，**不要打进对外发布的内容**。
//
// 三个目标（各自独立挂载，一个挂了不影响其余）：
//   ① `BeamConfig.Distance`（写死 21）→ postfix 换返回值
//   ② `BeamConfig.Size`（写死 3.6）  → postfix 换返回值
//      🔴 连带：`GetVisualMetrics` 里 `visualScale = Size` + `hitRadius = Size × 0.5`
//         ⇒ 尺寸翻倍 = 视觉翻倍 **且判定半径翻倍**（连打人更容易）；光半径也跟着走
//         （`CreateLight` 里 `Math.Max(3f, beamSize * 3f)`）。
//   ③ 光强（写死 80，**`const` 内联、没有 getter 可挂**）→ 改挂 `BeamVisuals.CreateLight`
//      的 postfix：拿 `GameEntity.GetLight()` 取回那盏灯改 `Intensity`。
//
// 实现：postfix 换掉 `Distance` 属性 getter 的返回值 ⇒ `SwordBeamProjectile` 构造里的
//   `_maxDistance = BeamConfig.Distance * releaseProfile.Scale` 随之变大。
//   ⚠️ `_maxLifetime = Math.Max(15f, _maxDistance / speed + 1f)`：距离放开后寿命跟着涨，
//      不会出现"距离没到但寿命先到"的怪象（实机若见"飞一半没了"，先查这里）。
//
// 版本：1.3.15 与 1.5.x 的 SwordBeam 内部结构**逐字相同**（`SwordBeam.Settings.BeamConfig`
//   + `BeamVisuals.CreateLight`，两版反编译核对过）；1.2.12 / 1.4.8 客户端**没装该 mod** ⇒ 闸门①直接跳过。
//
// 用法（跟其它 `custom.*` 一样，首参可弃）：
//   custom.swordbeam_range              看状态（三个值 + 各自挂没挂上）
//   custom.swordbeam_range 300          距离 → 300 米（旧用法不变）
//   custom.swordbeam_range size 10      尺寸 → 10（原 3.6；⚠️ 判定半径同时变）
//   custom.swordbeam_range light 300    光强 → 300（原 80）
//   custom.swordbeam_range off / on     全部还原 / 全部打开
// 开关：config.json 的 `DisabledPatchClasses` 写 `SwordBeamRangePatch`（或 `*`）即不挂。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>SwordBeam 飞行距离补丁（双闸门；目标 mod 没被引擎加载就什么都不做）。</summary>
    public static class SwordBeamRangePatch
    {
        /// <summary>目标程序集名（`AssemblyName.Name`，两客户端实测均为 `SwordBeam` v1.0.0.0）。</summary>
        private const string TargetAssemblyName = "SwordBeam";

        /// <summary>目标类型全名（1.3.15 与 1.5.x 两版反编译核对，逐字相同）。</summary>
        private const string TargetTypeName = "SwordBeam.Settings.BeamConfig";

        /// <summary>视觉类的类型全名（光强挂在它的 `CreateLight` 上）。</summary>
        private const string TargetVisualsTypeName = "SwordBeam.Beam.BeamVisuals";

        /// <summary>补丁总开关（false = 用它自己的 21 米）。</summary>
        public static bool Enabled = true;

        /// <summary>接管后的飞行距离（米）。诊断默认 200 米（27 m/s ⇒ 约 7.4 秒；它自己写死 21）。</summary>
        public static float DistanceOverride = 200f;

        /// <summary>
        /// 接管后的剑气尺寸（它自己写死 3.6）。默认 **2 倍 = 7.2**。
        /// 🔴 **连带效应（它的原设计，不是 bug）**：`GetVisualMetrics` 里
        ///   `visualScale = requestedSize` ＋ `hitRadius = requestedSize * 0.5`
        ///   ⇒ 尺寸翻倍 = **视觉翻倍 + 判定半径翻倍**（3.6 → 判定半径 1.8 m；7.2 → 3.6 m）= 更容易打中。
        ///   光半径也跟着走（`CreateLight` 里 `Math.Max(3f, beamSize * 3f)`）。
        /// </summary>
        public static float SizeOverride = 7.2f;

        /// <summary>
        /// 接管后的光源强度（它自己写死 80）。默认 **2 倍 = 160**。
        /// 🔴 实现走 `BeamVisuals.CreateLight` 的 postfix：拿 `GameEntity.GetLight()` 改 `Intensity`
        ///   —— 因为原值是 **`const` 内联**（没有 getter 可挂），只能改"造出来的那盏灯"。
        /// </summary>
        public static float LightIntensityOverride = 160f;

        /// <summary>是否已挂上（控制台与日志用）。false = 没加载 / 被配置禁用 / 挂载失败。</summary>
        public static bool Installed;

        /// <summary>三个目标各自的挂载状态（逐个报告，别用一个标志糊过去）。</summary>
        public static bool InstalledDistance;
        public static bool InstalledSize;
        public static bool InstalledLight;

        /// <summary>跳过或失败的一句话原因（控制台报告用，纯英文）。</summary>
        public static string SkipReason = "not_attempted";

        /// <summary>在 <c>MySubModule.OnSubModuleLoad</c> 逐类挂载那一段之后调用。</summary>
        public static void TryInstall(Harmony harmony)
        {
            try
            {
                // ── 配置闸门（与逐类挂载那套保持一致：DisabledPatchClasses 点名 / `*` 全关）──
                string rawDisabled = Settings.Instance.DisabledPatchClasses ?? "";
                if (rawDisabled.Trim().Length > 0)
                {
                    foreach (string piece in rawDisabled.Split(','))
                    {
                        string trimmed = piece.Trim();
                        if (trimmed == "*" || string.Equals(trimmed, nameof(SwordBeamRangePatch), StringComparison.Ordinal))
                        {
                            SkipReason = "disabled_by_config";
                            DebugLogger.Log("[SwordBeamPatch] 被 config.json 的 DisabledPatchClasses 关掉 → 不挂");
                            return;
                        }
                    }
                }

                // ── 闸门①：程序集是否真的被引擎装配进来了（= "骑砍2 加载了它"）──
                Assembly beamAssembly = FindLoadedAssembly(TargetAssemblyName);
                if (beamAssembly == null)
                {
                    SkipReason = "assembly_not_loaded";
                    DebugLogger.Log($"[SwordBeamPatch] 引擎本次没有加载 {TargetAssemblyName} 程序集"
                        + "（没装 / 装了没勾选 / 已卸载）→ 不挂，不影响任何东西");
                    return;
                }
                string asmVer;
                try { asmVer = beamAssembly.GetName().Version.ToString(); }
                catch (Exception) { asmVer = "?"; }

                // ── 闸门②：类型与属性真的可解析（该 mod 改版换名就停手）──
                Type configType = beamAssembly.GetType(TargetTypeName, throwOnError: false)
                    ?? AccessTools.TypeByName(TargetTypeName);
                if (configType == null)
                {
                    SkipReason = "type_not_found";
                    DebugLogger.Log($"[SwordBeamPatch] 已加载 {TargetAssemblyName} v{asmVer}，但找不到类型 {TargetTypeName}"
                        + " —— 该 mod 可能改版了 → 不挂");
                    return;
                }

                // ── 逐个目标挂载，互不拖累（今天那条教训：一个目标抛异常不该影响其余）──
                InstalledDistance = TryPatchGetter(harmony, configType, "Distance", nameof(DistancePostfix), "距离");
                InstalledSize = TryPatchGetter(harmony, configType, "Size", nameof(SizePostfix), "尺寸");

                Type visualsType = beamAssembly.GetType(TargetVisualsTypeName, throwOnError: false)
                    ?? AccessTools.TypeByName(TargetVisualsTypeName);
                MethodInfo createLight = visualsType == null ? null : AccessTools.Method(visualsType, "CreateLight");
                if (createLight != null)
                {
                    try
                    {
                        MethodInfo postfix = AccessTools.Method(typeof(SwordBeamRangePatch), nameof(CreateLightPostfix));
                        harmony.Patch(createLight, postfix: new HarmonyMethod(postfix));
                        InstalledLight = true;
                    }
                    catch (Exception exLight)
                    {
                        DebugLogger.Log($"[SwordBeamPatch] 光强补丁挂载失败（已跳过）：{exLight.Message}");
                    }
                }

                Installed = InstalledDistance || InstalledSize || InstalledLight;
                SkipReason = Installed ? "" : "no_target_patched";
                DebugLogger.Log($"[SwordBeamPatch] 已接管 SwordBeam：距离={(InstalledDistance ? DistanceOverride.ToString("F0") + " 米" : "未挂")}"
                    + $" 尺寸={(InstalledSize ? SizeOverride.ToString("F1") : "未挂")}"
                    + $" 光强={(InstalledLight ? LightIntensityOverride.ToString("F0") : "未挂")}"
                    + $"（它自己写死 21 米 / 3.6 / 80；程序集 v{asmVer}；改值用 custom.swordbeam_range）");
            }
            catch (Exception ex)
            {
                SkipReason = "install_exception";
                DebugLogger.Log($"[SwordBeamPatch] 安装失败（已忽略，不影响游戏）：{ex.GetType().Name} {ex.Message}");
            }
        }

        /// <summary>挂一个「属性 getter 换返回值」的 postfix；目标缺失只记一行、不抛。</summary>
        private static bool TryPatchGetter(Harmony harmony, Type type, string propertyName, string postfixName, string label)
        {
            try
            {
                MethodInfo getter = AccessTools.PropertyGetter(type, propertyName);
                if (getter == null)
                {
                    DebugLogger.Log($"[SwordBeamPatch] {type.Name} 没有 {propertyName} 属性 → {label}补丁不挂");
                    return false;
                }
                MethodInfo postfix = AccessTools.Method(typeof(SwordBeamRangePatch), postfixName);
                harmony.Patch(getter, postfix: new HarmonyMethod(postfix));
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[SwordBeamPatch] {label}补丁挂载失败（已跳过）：{ex.Message}");
                return false;
            }
        }

        /// <summary>在已加载程序集里按名字找（不看磁盘、不看模块列表 —— 只看引擎真的加载了谁）。</summary>
        private static Assembly FindLoadedAssembly(string simpleName)
        {
            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string name;
                    try { name = asm.GetName().Name; }
                    catch (Exception) { continue; }
                    if (name != null && string.Equals(name, simpleName, StringComparison.OrdinalIgnoreCase))
                    {
                        return asm;
                    }
                }
            }
            catch (Exception)
            {
                // AppDomain 枚举异常 → 当作"没加载"，交给闸门②兜底
            }
            return null;
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

        /// <summary>尺寸 getter 的 postfix（连带影响视觉缩放 + 判定半径 + 光半径，见字段注释）。</summary>
        private static void SizePostfix(ref float __result)
        {
            try
            {
                if (Enabled)
                {
                    __result = SizeOverride;
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// `BeamVisuals.CreateLight` 的 postfix：把造出来的那盏灯改成我们的强度。
        /// 原值是 `const`，编译期内联进方法体（没有 getter 可挂）⇒ 只能事后改对象。
        /// 用 `GameEntity.GetLight()` 取回光源（1.3.15 / 1.5.x 都有这个 API）。
        /// </summary>
        private static void CreateLightPostfix(GameEntity __result)
        {
            try
            {
                if (!Enabled || __result == null)
                {
                    return;
                }
                Light light = __result.GetLight();
                if (light != null)
                {
                    light.Intensity = LightIntensityOverride;
                }
            }
            catch (Exception)
            {
                // 绝不能抛 —— 这段在剑气构造路径上
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
                    return $"swordbeam_not_patched: {SwordBeamRangePatch.SkipReason} "
                        + "(need the SwordBeam module actually loaded by the game)";
                }
                if (args == null || args.Count == 0)
                {
                    return Status();
                }

                string key = args[0].ToLowerInvariant();
                if (key == "size" || key == "light")
                {
                    if (args.Count < 2)
                    {
                        return $"usage: swordbeam_range {key} <value>";
                    }
                    float value;
                    if (!float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    {
                        return Status() + $" [note: '{args[1]}' is not a number -> showing status]";
                    }
                    if (key == "size")
                    {
                        // 尺寸连带判定半径（hitRadius = Size×0.5），所以下限给 1，防手滑输 0 把剑气弄没
                        SwordBeamRangePatch.SizeOverride = Math.Max(1f, Math.Min(100f, value));
                        DebugLogger.Log($"[SwordBeamPatch] 剑气尺寸 → {SwordBeamRangePatch.SizeOverride:F1}"
                            + "（连带视觉缩放 + 判定半径 + 光半径一起变，见文件头）");
                    }
                    else
                    {
                        SwordBeamRangePatch.LightIntensityOverride = Math.Max(0f, Math.Min(5000f, value));
                        DebugLogger.Log($"[SwordBeamPatch] 剑气光强 → {SwordBeamRangePatch.LightIntensityOverride:F0}");
                    }
                    return Status();
                }

                if (key == "on")
                {
                    SwordBeamRangePatch.Enabled = true;
                    DebugLogger.Log("[SwordBeamPatch] 覆盖已打开");
                    return Status();
                }
                if (key == "off")
                {
                    SwordBeamRangePatch.Enabled = false;
                    DebugLogger.Log("[SwordBeamPatch] 已全部还原成它自己的值（21 米 / 3.6 / 80）");
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
                + $"distance={SwordBeamRangePatch.DistanceOverride:F0}{(SwordBeamRangePatch.InstalledDistance ? "" : "(NOT_patched)")} "
                + $"size={SwordBeamRangePatch.SizeOverride:F1}{(SwordBeamRangePatch.InstalledSize ? "" : "(NOT_patched)")} "
                + $"light={SwordBeamRangePatch.LightIntensityOverride:F0}{(SwordBeamRangePatch.InstalledLight ? "" : "(NOT_patched)")} "
                + "(mod defaults: 21 / 3.6 / 80)";
        }
    }
}
