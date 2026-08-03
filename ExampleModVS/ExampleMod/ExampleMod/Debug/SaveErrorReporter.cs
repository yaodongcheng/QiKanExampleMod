using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using TaleWorlds.SaveSystem.Save;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 存档失败诊断补丁（排查期使用，定位问题后可移除）：
    /// ① SaveManager.Save Postfix — 缓存存档失败时的底层错误详情（SaveOutput.Errors，
    ///    如 "Could not find type definition of type: X" / "SaveContext Error: ..."）。
    /// ② MBSaveLoad.ShowErrorFromResult Prefix — 拦截存档失败弹窗，把详情追加到弹窗正文，
    ///    玩家截图即可反馈具体原因。定位后应删除本文件。
    /// </summary>
    public static class SaveErrorReporter
    {
        private static readonly object _lock = new object();
        private static string _lastErrorDetail = "";

        private static string LastErrorDetail
        {
            get { lock (_lock) return _lastErrorDetail; }
        }

        // ── ① 捕获 SaveManager.Save 的错误详情 ──
        [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save))]
        public static class SaveManagerSavePatch
        {
            [HarmonyPostfix]
            public static void Postfix(SaveOutput __result)
            {
                try
                {
                    if (__result == null) return;
                    if (__result.Successful)
                    {
                        lock (_lock) _lastErrorDetail = "";
                        return;
                    }

                    var messages = new List<string>();
                    if (__result.Errors != null)
                    {
                        foreach (var err in __result.Errors)
                        {
                            if (err != null && !string.IsNullOrEmpty(err.Message))
                                messages.Add(err.Message);
                        }
                    }
                    lock (_lock) _lastErrorDetail = string.Join("\n", messages);
                }
                catch (Exception ex)
                {
                    // 诊断代码绝不影响存档流程
                    lock (_lock) _lastErrorDetail = "SaveErrorReporter exception: " + ex.Message;
                }
            }
        }

        // ── ② 拦截存档失败弹窗，追加详细原因 ──
        [HarmonyPatch(typeof(MBSaveLoad), "ShowErrorFromResult")]
        public static class ShowErrorFromResultPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(SaveResult result)
            {
                try
                {
                    if (result == SaveResult.Success) return true; // 成功：放行原方法（原方法直接 return）

                    // 文本走标准本地化（Native 的 str_* key，引擎表已注册，命中中文翻译）
                    string title = GameTexts.FindText("str_save_unsuccessful_title")?.ToString() ?? "Save Failed!";
                    string baseMsg = GameTexts.FindText("str_game_save_result", result.ToString())?.ToString() ?? "Cannot create save data.";

                    string detail = LastErrorDetail;
                    if (string.IsNullOrEmpty(detail)) detail = "(no error detail captured)";
                    try
                    {
                        string platformErr = Common.PlatformFileHelper.GetError();
                        if (!string.IsNullOrEmpty(platformErr)) detail += "\n[Platform] " + platformErr;
                    }
                    catch { }

                    string body = baseMsg + "\n\n[SaveDebug] Result=" + result + "\n" + detail;

                    InformationManager.ShowInquiry(new InquiryData(
                        title, body, true, false,
                        GameTexts.FindText("str_ok")?.ToString() ?? "OK", "",
                        null, null), false, false);

                    DebugLogger.Log($"[SaveErrorReporter] Save failed: Result={result}, detail={detail}");
                    return false; // 阻止原方法重复弹窗
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[SaveErrorReporter] Patch failed: {ex.Message}");
                    return true; // 诊断代码出问题 → 放行原方法，绝不阻断存档流程
                }
            }
        }
    }

    /// <summary>
    /// 序列化崩溃定位（阶段二补充诊断）：记录"当前正在保存的对象类型"，
    /// 并在 VariableSaveData.Value==null（会导致 (int)Value 解箱 NRE 崩溃）时打印对象类型 + 成员。
    /// 目标类型是 internal（SaveSystem），用 AccessTools 动态绑定 + 反射读值。
    /// ⚠️ TargetMethod 必须 public static（Harmony 按 public 反射查找，private 会静默跳过补丁）。
    /// 定位后随 SaveErrorReporter 一起移除。
    /// </summary>
    public static class SaveSerializeDiagPatch
    {
        private static string _currentSavingType = "?";

        // ── ObjectSaveData.SaveTo Prefix：记录当前保存的对象类型 ──
        [HarmonyPatch]
        public static class ObjectSaveToPatch
        {
            public static MethodBase TargetMethod()
            {
                var m = AccessTools.Method("TaleWorlds.SaveSystem.Save.ObjectSaveData:SaveTo");
                DebugLogger.Log($"[SaveReporter-Bind] ObjectSaveData.SaveTo bound={m != null}");
                return m;
            }

            [HarmonyPrefix]
            public static void Prefix(object __instance)
            {
                try
                {
                    var typeProp = __instance.GetType().GetProperty("Type",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var t = typeProp?.GetValue(__instance) as Type;
                    _currentSavingType = t?.FullName ?? "?";
                }
                catch { }
            }
        }

        // ── VariableSaveData.SaveTo Prefix：Value==null 时打印定位信息 ──
        [HarmonyPatch]
        public static class VariableSaveToPatch
        {
            public static MethodBase TargetMethod()
            {
                var m = AccessTools.Method("TaleWorlds.SaveSystem.Save.VariableSaveData:SaveTo");
                DebugLogger.Log($"[SaveReporter-Bind] VariableSaveData.SaveTo bound={m != null}");
                return m;
            }

            [HarmonyPrefix]
            public static void Prefix(object __instance)
            {
                try
                {
                    object value = null;
                    object memberType = null;
                    object saveId = null;
                    foreach (var p in __instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        switch (p.Name)
                        {
                            case "Value": value = p.GetValue(__instance); break;
                            case "MemberType": memberType = p.GetValue(__instance); break;
                            case "MemberSaveId": saveId = p.GetValue(__instance); break;
                        }
                    }
                    if (value == null)
                    {
                        // 只报危险类型：Object/Container/CustomStruct 的 null 会导致 (int)Value 解箱 NRE；
                        // String 分支 null 安全（GetStringId(null) 返回 -1），不报（原版对象 null string 是常态）。
                        string mt = memberType?.ToString() ?? "?";
                        if (mt != "String")
                        {
                            DebugLogger.Log($"[SaveReporter-Null] 对象={_currentSavingType} MemberType={mt} SaveId={saveId}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[SaveReporter-DiagErr] {ex.Message}");
                }
            }
        }
    }
}
