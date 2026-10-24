using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using HarmonyLib;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View;

namespace LivingWorldNpcs
{
    /// <summary>
    /// **进战场"等装备网格装载"的保险丝**（2026-10-24 立）——
    /// 把引擎那个**没有超时的死循环**换成带超时的版本：正常一模一样，卡住就记日志放行。
    ///
    /// ## 为什么必须有它（内存转储实证，不是猜）
    /// 进战场时引擎会先预装载**参战角色身上的每一件装备**，然后调
    /// `PreloadHelper.WaitForMeshesToBeLoaded()` 原地死等（1.2.12 反编译原文）：
    /// <code>
    /// do {
    ///     num = 0;
    ///     foreach (var m in _uniqueMetaMeshes) num += m.Item1.CheckResources();   // 没装完 +1
    ///     foreach (var s in _uniqueDynamicPhysicsShapeName)
    ///         num += PhysicsShape.GetFromResource(s, true) == null ? 1 : 0;       // 查不到 +1
    ///     Thread.Sleep(1);
    /// } while (num != 0);          // ← 没有超时
    /// </code>
    /// **只要有一件装备的网格永远装不完、或某个碰撞体名查不到，这一帧就永不返回**：
    /// loading 界面不撤、画面冻死、战吼照放 —— 玩家看到的就是"进不去战场"。
    ///
    /// 已实测踩中两次（症状一模一样，根因完全不同）：
    /// ① 太阁弓的 `body_name` 写了个不存在的 `bo_longbow_a`（弓足轻人人有 ⇒ 场场卡）；
    /// ② 玩家报「装备钩索进战场就卡，不装备没事」（钩索的某件资产装不出来）。
    ///
    /// ## 本补丁做什么
    /// Prefix **替引擎等**：同一个循环、同样的 Sleep(1)，但**带超时**
    /// （`Settings.PreloadWaitTimeoutSeconds`，默认 20 秒，改 config.json 生效，0 = 关掉保险回到原版）。
    /// · 等到了 → 行为与引擎完全一致（原方法跳过）；
    /// · 超时   → 打一行日志**点名卡住的资产**，然后照样放行 —— 宁可少一件装备的网格，也不能卡死玩家。
    ///
    /// 判据：日志里搜 `[Preload]`。出现 `超时 ... 放行` 就是它救了一次场，
    /// 后面几行会写出具体是哪个网格 `pending=` / 哪个碰撞体 `查不到`。
    /// </summary>
    [HarmonyPatch(typeof(PreloadHelper), "WaitForMeshesToBeLoaded")]
    internal static class PreloadWaitGuardPatch
    {
        /// <summary>超时日志每次 Mission 只打一次（同一个 helper 会从多个视图被调）。</summary>
        private static Mission _loggedMission;

        /// <summary>
        /// **多久没进展就直接放行**（毫秒）。名字写错/资产永久缺失永远不自愈 ⇒ 不必干等满总超时。
        /// 正常装载 pending 是持续下降的，连续 5 秒一个都没少 = 卡死了。
        /// </summary>
        private const int StallMs = 5000;

        /// <summary>返回 false = 原方法（死等版）不再执行。</summary>
        [HarmonyPrefix]
        private static bool Prefix(PreloadHelper __instance)
        {
            int timeoutMs;
            try
            {
                timeoutMs = Math.Max(0, Settings.Instance.PreloadWaitTimeoutSeconds) * 1000;
            }
            catch
            {
                return true;                 // 配置读不出来 → 交给原版（宁可卡也不乱来）
            }

            if (timeoutMs == 0) return true; // 玩家显式关掉保险 = 完全原版行为

            try
            {
                if (__instance == null) return true;

                Traverse tr = Traverse.Create(__instance);
                var meshes = tr.Field("_uniqueMetaMeshes")
                               .GetValue<IEnumerable<(MetaMesh, bool, bool)>>();
                var shapes = tr.Field("_uniqueDynamicPhysicsShapeName")
                               .GetValue<IEnumerable<string>>();

                if (meshes == null && shapes == null) return true;   // 字段拿不到 = 结构变了，交还原版

                var clock = Stopwatch.StartNew();
                int lastPending = -1;
                long lastChangeMs = 0;
                while (true)
                {
                    int pending = 0;

                    if (meshes != null)
                    {
                        foreach (var entry in meshes)
                        {
                            try { pending += entry.Item1.CheckResources(); }
                            catch { /* 单个网格查不了不改变判定 */ }
                        }
                    }
                    if (shapes != null)
                    {
                        foreach (string shape in shapes)
                        {
                            try
                            {
                                if (PhysicsShape.GetFromResource(shape, true) == null) pending++;
                            }
                            catch { pending++; }
                        }
                    }

                    if (pending == 0) return false;      // 全就绪 → 我们等完了，跳过原方法的死等

                    long nowMs = clock.ElapsedMilliseconds;

                    // 🔴 **无进展提前放行**：名字写错/资产永久缺失这种**永远不会自愈**，
                    //    干等满 20 秒纯属浪费玩家时间 —— 连续 StallMs 一个都没少就直接放。
                    //    （正常装载 pending 是**持续下降**的：25→18→7→0，不会连续几秒纹丝不动。）
                    if (pending != lastPending)
                    {
                        lastPending = pending;
                        lastChangeMs = nowMs;
                    }
                    else if (nowMs - lastChangeMs >= StallMs)
                    {
                        LogTimeout(__instance, shapes, nowMs, timeoutMs, "无进展 " + (StallMs / 1000f).ToString("F0") + "s");
                        return false;
                    }

                    if (nowMs >= timeoutMs)
                    {
                        LogTimeout(__instance, shapes, nowMs, timeoutMs, "总超时");
                        return false;                    // 超时放行
                    }

                    Thread.Sleep(1);                     // 与引擎同款节拍
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[Preload] 保险丝自身异常（交还原版继续等）: " + ex.Message);
                return true;
            }
        }

        /// <summary>超时：把"到底谁没就绪"逐个点名（每个 Mission 只打一次）。</summary>
        private static void LogTimeout(PreloadHelper instance, IEnumerable<string> shapes, long waitedMs, int timeoutMs, string reason)
        {
            try
            {
                Mission mission = Mission.Current;
                if (mission != null)
                {
                    if (ReferenceEquals(_loggedMission, mission)) return;
                    _loggedMission = mission;
                }

                Traverse tr = Traverse.Create(instance);

                int total = 0, stuck = 0;
                var meshNames = tr.Field("_uniqueMetaMeshNames")
                                  .GetValue<IEnumerable<(string, bool, bool)>>();
                if (meshNames != null)
                {
                    foreach (var entry in meshNames)
                    {
                        total++;
                        string name = entry.Item1;
                        int pending = -2;
                        try
                        {
                            MetaMesh mm = MetaMesh.GetCopy(name, false, true);
                            pending = mm != null ? mm.CheckResources() : -1;
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.Log("[Preload] 查网格 '" + name + "' 抛异常: " + ex.Message);
                            continue;
                        }

                        if (pending != 0)
                        {
                            stuck++;
                            DebugLogger.Log("[Preload] 网格 '" + name + "' pending=" + pending);
                        }
                    }
                }

                int shapeTotal = 0, shapeStuck = 0;
                if (shapes != null)
                {
                    foreach (string shape in shapes)
                    {
                        shapeTotal++;
                        bool ok;
                        try { ok = PhysicsShape.GetFromResource(shape, true) != null; }
                        catch { ok = false; }
                        if (!ok)
                        {
                            shapeStuck++;
                            DebugLogger.Log("[Preload] 物理体 '" + shape + "' 查不到（游戏里没有这个名字）");
                        }
                    }
                }

                DebugLogger.Log("[Preload] " + reason + "：等了 " + (waitedMs / 1000f).ToString("F1")
                                + "s / 上限 " + (timeoutMs / 1000f).ToString("F0") + "s → 放行进战场"
                                + "（网格 " + stuck + "/" + total + " 未就绪，物理体 "
                                + shapeStuck + "/" + shapeTotal + " 查不到）");
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[Preload] 超时日志自身异常（已忽略）: " + ex.Message);
            }
        }
    }
}
