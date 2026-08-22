using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 2026-08-22（Steam Deck 软键盘退格失效——用户实测「提交后游戏框删不掉」）：
    /// Steam 直通模式软键盘发的是**文本事件流**（非键事件）——打字字符经 native 转键码进
    /// EditableTextWidget.HandleInput 的 lastKeysPressed → AppendCharacter 上屏（用户实测打字有效）；
    /// 但退格在文本流里是控制字符 \b(8)、删除是 \x7f(127)——vanilla HandleInput 的字符过滤器
    /// `num &gt;= 32 &amp;&amp; (num &lt; 127 || num &gt;= 160)`（反编译 TaleWorlds.GauntletUI.dll 实锤）
    /// **把控制字符全吞掉**；而引擎删字只走原始按键轮询 `Input.IsKeyPressed(BackSpace)`
    /// （键事件才有）——直通键盘不发键事件 → 永不触发 → 退格彻底失效。
    /// 物理键盘不受影响（键事件路径照旧，字符过滤器对其不适用）。
    ///
    /// 修复：Prefix 处理 lastKeysPressed 里的 8/127 → 反射调 protected DeleteChar（引擎同款删字，
    /// 更新 RealText + 光标，绑定照常推送）；并兜底「键沿已捕捉但同帧 IsKeyDown=false」的极短脉冲
    /// 撕裂（vanilla 的 _keyboardAction 同帧置 None 吞掉删除）。组合态（ImeCompositionHelper）
    /// 不处理——中文输入法组词期间的退格是输入法消费的，放行由既有 IME 补丁拦截。
    ///
    /// ⚠️ 版本兼容：补丁目标 HandleInput(IReadOnlyList&lt;int&gt;) 与命名空间三锚点已核实
    ///（EditableTextImePatch 同目标同命名空间，input.md 轮子）；DeleteChar 用反射调用
    ///（protected 实例方法，三版本签名一致，GetMethod null 时跳过降级）。
    /// </summary>
    [HarmonyPatch(typeof(EditableTextWidget), "HandleInput")]
    public static class EditableTextBackspacePatch
    {
        private const int CharBackSpace = 8;   // \b —— Steam 直通软键盘的退格（文本事件流）
        private const int CharDelete = 127;    // \x7f —— 直通软键盘的删除键

        private static MethodInfo _deleteCharMethod;

        private static MethodInfo DeleteCharMethod
        {
            get
            {
                if (_deleteCharMethod == null)
                    _deleteCharMethod = typeof(EditableTextWidget)
                        .GetMethod("DeleteChar", BindingFlags.Instance | BindingFlags.NonPublic);
                return _deleteCharMethod;
            }
        }

        private static void DeleteOne(EditableTextWidget widget, bool nextChar)
        {
            MethodInfo m = DeleteCharMethod;
            if (m == null)
            {
                DebugLogger.Log("[TextInput] DeleteChar 反射未找到（版本差异）——退格降级不处理");
                return;
            }
            try
            {
                m.Invoke(widget, new object[] { nextChar });
            }
            catch (System.Exception ex)
            {
                DebugLogger.Log($"[TextInput] DeleteChar 调用异常（已吞）: {ex.Message}");
            }
        }

        [HarmonyPrefix]
        public static bool Prefix(EditableTextWidget __instance, IReadOnlyList<int> lastKeysPressed)
        {
            if (__instance.IsDisabled) return true;                     // 与原方法一致：disabled 不处理
            if (ImeCompositionHelper.IsComposing()) return true;        // 输入法组合态：退格是输入法消费的，放行（IME 补丁拦截）

            bool deleted = false;
            // ① 文本事件流的退格(8)/删除(127)控制字符：vanilla 字符过滤器 num>=32 直接吞掉——
            //    Steam Deck 直通软键盘的退格就死在这一点（用户实测：提交后游戏框删不掉）
            for (int i = 0; i < lastKeysPressed.Count; i++)
            {
                int code = lastKeysPressed[i];
                if (code == CharBackSpace) { DeleteOne(__instance, false); deleted = true; }
                else if (code == CharDelete) { DeleteOne(__instance, true); deleted = true; }
            }
            // ② 键沿撕裂兜底：按下沿已捕捉但同帧 IsKeyDown=false（极短脉冲/直通键事件）——
            //    vanilla 的 _keyboardAction 同帧置 None 把删除吞掉。物理键按下沿时 IsKeyDown 必 true，
            //    此分支只命中撕裂帧，与原逻辑不冲突
            if (!deleted && Input.IsKeyPressed(InputKey.BackSpace) && !Input.IsKeyDown(InputKey.BackSpace))
            {
                DeleteOne(__instance, false);
                deleted = true;
            }
            if (deleted) DebugLogger.Log("[TextInput] 软键盘退格/删除已处理（文本事件流或键沿撕裂）");
            return true;    // 放行原方法：8/127 被其 num>=32 过滤器忽略；物理键路径照旧
        }
    }
}
