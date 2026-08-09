using System;
using System.Runtime.ExceptionServices;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;
using TaleWorlds.SaveSystem.Load;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 存档加载诊断（排查"载入存档时发生了一个错误"弹窗用）。
    ///
    /// 原版链路：SaveManager.Load → LoadContext.Load 内部 try/catch 吞掉真实异常，
    /// 只调 Debug.Print(ex.Message)（1.3.15 的 DebugManager 不落盘，消息丢失）。
    ///
    /// 方案：patch SaveManager.Load 的三参重载（MBSaveLoad.LoadSaveGameData 调用的），
    /// 在 Load 期间挂 AppDomain.FirstChanceException —— 捕获期间每一次抛出的异常
    /// （含完整堆栈），包括被内部 catch 吞掉的。写 Debug/StoryEngine_RuntimeLog.txt。
    ///
    /// 排查完成后可整文件删除。
    /// </summary>
    public static class SaveLoadDiagnostics
    {
        private static EventHandler<FirstChanceExceptionEventArgs> _handler;
        private static bool _capturing;
        private static int _count;

        [HarmonyPatch(typeof(SaveManager), "Load",
            new Type[] { typeof(string), typeof(ISaveDriver), typeof(bool) })]
        public static class SaveManagerLoadPatch
        {
            [HarmonyPrefix]
            public static void Prefix()
            {
                try
                {
                    if (_capturing) return;
                    _capturing = true;
                    _count = 0;
                    _handler = (s, e) =>
                    {
                        try
                        {
                            if (++_count > 300) return; // 限量防刷屏
                            var ex = e.Exception;
                            DebugLogger.Log($"[LoadDiag#{_count}] {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
                        }
                        catch { }
                    };
                    AppDomain.CurrentDomain.FirstChanceException += _handler;
                    DebugLogger.Log("[LoadDiag] SaveManager.Load started, capturing first-chance exceptions...");
                }
                catch { }
            }

            [HarmonyPostfix]
            public static void Postfix(LoadResult __result)
            {
                try
                {
                    if (!_capturing) return;
                    _capturing = false;
                    AppDomain.CurrentDomain.FirstChanceException -= _handler;
                    bool ok = __result != null && __result.Successful;
                    DebugLogger.Log($"[LoadDiag] SaveManager.Load ended: successful={ok}, captured={_count} exceptions");
                }
                catch { }
            }
        }
    }

    /// <summary>
    /// ReadBytes watchdog：读档期间读到超大/负数长度时打印完整调用链，
    /// 精确定位是哪个对象/字段在反序列化时错位（异常本身的 StackTrace 被 newarr 截断）。
    /// </summary>
    [HarmonyPatch(typeof(BinaryReader), "ReadBytes")]
    public static class ReadBytesWatchdog
    {
        [HarmonyPrefix]
        public static void Prefix(BinaryReader __instance, ref int length)
        {
            try
            {
                if (length >= 0 && length <= 500_000_000) return;
                DebugLogger.Log($"[LoadDiag-ReadBytes] SUSPICIOUS length={length}, stack:\n" +
                    (Environment.StackTrace.Length > 3000 ? Environment.StackTrace.Substring(0, 3000) : Environment.StackTrace));
            }
            catch { }
        }
    }
}
