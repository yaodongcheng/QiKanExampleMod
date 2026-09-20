// ═══════════════════════════════════════════════════════════════════════════
// 控制台指令日志（2026-09-20）
//
// 目的：`~` 控制台敲的**每一条指令**（输入 + 引擎返回）都落进 DebugLogger，
//       不再出现「指令打出去了、控制台刷过去了、日志里什么都没有」。
//
// 🔴 补丁点从哪来（1.2.12 反编译实证，整条链路）：
//     native 控制台
//       → TaleWorlds.DotNet 的 [LibraryCallback] CallCommandlineFunction(name, args)
//         → CommandLineFunctionality.CallFunction(concatName, concatArguments, out found)
//   所以**打在 `CallFunction` 上就能收到全部指令**（原版 + 所有 mod 一网打尽），
//   `__result` 就是控制台显示的那句话本身。
//
// 🔴 附带一个诊断价值（本次就是为它加的）：
//   **敲了指令却没有 [Cmd] 行 = 这条命令根本没进引擎。**
//   最典型的是「零参数命令不触发」——骑砍2 控制台的已知怪癖，也是「首参可弃」纪律的成因。
//   有了这行日志，这种事一眼可辨，不用再猜。
//
// 🔴 只读不改：纯 Postfix、不碰返回值、全程 try/catch 兜死（日志系统绝不能影响游戏）。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 控制台走的那条重载（native 桥 → 字符串参数版）。
    ///
    /// 🔴 为什么用 `TargetMethod()` 而不是 `[HarmonyPatch(typeof(X), "CallFunction", new[]{…})]`：
    ///    `CallFunction` **有两个重载**，必须给参数类型数组来消歧；而 `out bool` 的类型只能写成
    ///    `typeof(bool).MakeByRefType()` —— 那是**方法调用**，不能出现在特性实参里（实测 CS0182 编译失败）。
    ///    `TargetMethod()` 正是 Harmony 给这种情形的官方出口：运行时解析 MethodInfo，重载 + out 参数都能精确指定。
    ///
    /// 🔴 方法名是**字符串目标**——Harmony 编译期不校验，找不到会静默跳过；
    ///    此处已按 1.2.12 反编译实证（`CallFunction(string, string, out bool)` 存在）。
    ///    `out bool found` 故意不接：返回串里本来就会写 "Could not find the command X"，接了也没多信息。
    /// </summary>
    [HarmonyPatch]
    public static class CommandLineLoggerPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(TaleWorlds.Library.CommandLineFunctionality), "CallFunction",
                new[] { typeof(string), typeof(string), typeof(bool).MakeByRefType() });
        }

        [HarmonyPostfix]
        public static void Postfix(string concatName, string concatArguments, string __result)
        {
            try
            {
                DebugLogger.Log($"[Cmd] {concatName} {concatArguments} -> {__result}");
            }
            catch { /* 日志系统绝不能影响游戏正常运行 */ }
        }
    }

    /// <summary>
    /// 托管侧调用用的 List 参数版（原版自己也走这条，例：性能设置页触发 `benchmark.cpu_benchmark`）。
    /// 与控制台那条不重叠，各记各的。
    /// </summary>
    [HarmonyPatch]
    public static class CommandLineLoggerPatchList
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(TaleWorlds.Library.CommandLineFunctionality), "CallFunction",
                new[] { typeof(string), typeof(List<string>), typeof(bool).MakeByRefType() });
        }

        [HarmonyPostfix]
        public static void Postfix(string concatName, List<string> argList, string __result)
        {
            try
            {
                string joined = argList == null ? "" : string.Join(" ", argList);
                DebugLogger.Log($"[Cmd] {concatName} {joined} -> {__result}");
            }
            catch { /* 日志系统绝不能影响游戏正常运行 */ }
        }
    }
}
