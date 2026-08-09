using System;
using System.Runtime.ExceptionServices;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;
using TaleWorlds.SaveSystem.Load;
using TaleWorlds.SaveSystem.Save;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 存档字符串超长防护（🔴 常驻——救档 + 双向监控 + 根因防线，排查"载入存档时发生了一个错误"用）。
    ///
    /// 背景：SaveSystem 的 Strings 表每条字符串长度字段是 16 位 signed short（上限 32767）。
    /// 单条字符串 &gt; 32767 字节 → 写入时溢出成负数 → 整张表错位 → 读档必崩
    /// （ArchiveDeserializer.LoadFrom 读负长度 → ReadBytes(负数) → OverflowException → Load 返回 null → 弹窗）。
    ///
    /// 三层职责：
    /// ① ReadBytesFix（读档侧救档）：short 溢出还原——负数且在 short 范围内（-32768..-1）
    ///    表示"长度字段被 short 溢出截断"，+65536 即真实长度。数据本身无损，还原即可读。
    ///    int32 层面的真损坏（负值 &lt; -32768）保持抛异常暴露问题，不掩盖。
    /// ② 双向监控（定位超长 key）：
    ///    - SaveContext.AddOrGetStringId watchdog：写入侧全局监控，任何超长字符串打日志 + 调用栈
    ///      （栈可显示正在序列化的对象类型）——key 信息只存在于写入侧，这是定位主战场。
    ///    - SaveManager.Load/Save FirstChance：挂 AppDomain.FirstChanceException 捕获期间每次
    ///      异常（含被内部 catch 吞掉的，1.3.15 的 Debug.Print 不落盘，消息会丢）。
    /// ③ GuardJson（业务层统一守卫）：MyBehavior 的 SyncData key 逐个接入，超长裁剪 + key 定位日志。
    /// </summary>
    public static class SaveStringGuard
    {
        /// <summary>Strings 表长度字段的安全阈值（short 上限 32767，留余量取 30000）。</summary>
        public const int MaxStringBytes = 30000;

        // ── 降级提醒（保存成功但发生超长裁剪 → Save Postfix 弹窗告知玩家）──
        private static readonly object _lock = new object();
        private static int _trimCount;
        private static readonly StringBuilder _trimKeys = new StringBuilder();

        /// <summary>
        /// 统一守卫：SyncData key 的 JSON 超长即裁剪（UTF-8 字节口径）。
        /// 🔴 必须按 UTF-8 字节数判断（存档长度字段是字节数）——字符数判断会被中文绕过
        /// （1 中文字 = 3 字节：30000 字符 = 90000 字节 > 32767）。
        /// 优先结构感知截断（数组型 JSON 逐元素保留，JSON 始终合法，只丢最老记录）；
        /// 非数组 / 解析失败回退硬截断（切坏也仅该 key 读档后清空，Deserialize 有容错，不崩档）。
        /// </summary>
        public static string GuardJson(string key, string json, int maxBytes = MaxStringBytes)
        {
            if (json == null) return json;
            int bytes = Encoding.UTF8.GetByteCount(json);
            if (bytes <= maxBytes) return json;
            DebugLogger.Log($"[SyncDataGuard] {key} 超长 {bytes}B → 裁剪到 {maxBytes}B");

            var trimmed = TrimJsonArrayElements(json, maxBytes);
            if (trimmed == null) trimmed = TruncateByBytes(json, maxBytes);
            NotifyTrimmed(key);
            return trimmed;
        }

        /// <summary>
        /// 结构感知截断：JSON 根为数组时逐元素保留完整记录直到字节预算内
        /// （JSON 始终合法，只丢尾部元素——数组顺序 = 记录插入顺序，丢尾部 = 丢最老）。
        /// 非数组 / 解析失败返回 null（调用方回退硬截断）。
        /// </summary>
        private static string TrimJsonArrayElements(string json, int maxBytes)
        {
            try
            {
                var arr = JArray.Parse(json);
                var kept = new JArray();
                int totalBytes = 2; // "[]"
                foreach (var item in arr)
                {
                    string itemStr = item.ToString(Formatting.None);
                    int itemBytes = Encoding.UTF8.GetByteCount(itemStr);
                    int overhead = kept.Count == 0 ? 1 : 2; // 首元素无前导分隔，后续加 ","
                    if (totalBytes + overhead + itemBytes > maxBytes) break;
                    kept.Add(item);
                    totalBytes += overhead + itemBytes;
                }
                if (kept.Count == 0) return "[]";
                if (kept.Count == arr.Count) return null; // 全部保留（不可能走到这，防御）
                DebugLogger.Log($"[SyncDataGuard] 结构感知截断：保留 {kept.Count}/{arr.Count} 条完整记录");
                return kept.ToString(Formatting.None);
            }
            catch { return null; }
        }

        /// <summary>记录一次超长裁剪（供 Save Postfix 弹窗提醒玩家；key 为空表示未知字段，详情看日志）。</summary>
        public static void NotifyTrimmed(string key)
        {
            lock (_lock)
            {
                _trimCount++;
                string label = string.IsNullOrEmpty(key) ? "(未知字段, 详情见 [SaveStrGuard] 日志)" : key;
                if (_trimKeys.Length > 0) _trimKeys.Append(", ");
                _trimKeys.Append(label);
            }
        }

        /// <summary>
        /// 保存成功但发生了超长裁剪 → 弹窗提醒玩家（"保存完成，但部分数据超长被裁剪"）。
        /// 铁律 13：玩家可见文本走 LWN key（英文 fallback，条目注册于 std_LivingWorldNpcs_strings.xml）。
        /// </summary>
        public static void ShowTrimNoticeIfAny()
        {
            string keys;
            lock (_lock)
            {
                if (_trimCount == 0) return;
                keys = _trimKeys.ToString();
                _trimCount = 0;
                _trimKeys.Clear();
            }
            try
            {
                DebugLogger.Log($"[SaveStringGuard] 本次保存发生超长裁剪: {keys}");
                InformationManager.ShowInquiry(new InquiryData(
                    LWNTextHelper.ResolveText("LWN_save_trim_title", "Save Warning"),
                    LWNTextHelper.ResolveCompound("LWN_save_trim_body",
                        "The save completed, but some data exceeded the safe size limit and was trimmed: {KEYS}. The oldest affected records were discarded.",
                        ("KEYS", keys)),
                    true, false,
                    LWNTextHelper.ResolveText("LWN_save_trim_ok", "OK"), "",
                    null, null), false, false);
            }
            catch (Exception ex) { DebugLogger.Log($"[SaveStringGuard] Trim notice failed: {ex.Message}"); }
        }

        /// <summary>按 UTF-8 字节截断（避免切坏多字节字符：回退到合法字符边界）。</summary>
        public static string TruncateByBytes(string text, int maxBytes)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (Encoding.UTF8.GetByteCount(text) <= maxBytes) return text;
            var bytes = Encoding.UTF8.GetBytes(text);
            int cut = maxBytes;
            while (cut > 0 && (bytes[cut] & 0xC0) == 0x80) cut--;  // 回退到字符边界
            return Encoding.UTF8.GetString(bytes, 0, cut);
        }

        public static string ShortStackTrace()
        {
            string stack = Environment.StackTrace;
            return stack.Length > 3000 ? stack.Substring(0, 3000) : stack;
        }
    }

    /// <summary>
    /// 读档侧救档（第 1 层，核心修复）：BinaryReader.ReadBytes 负长度还原 + watchdog。
    /// 超长字符串的长度字段被 short 溢出成负数：-32768..-1 → +65536 即真实长度（无损还原）。
    /// 真损坏（int32 层面负值 &lt; -32768）保持抛异常暴露问题，仅打日志。
    /// </summary>
    [HarmonyPatch(typeof(BinaryReader), "ReadBytes")]
    public static class ReadBytesFix
    {
        [HarmonyPrefix]
        public static void Prefix(ref int length)
        {
            try
            {
                if (length >= 0) return;
                if (length >= -32768)
                {
                    // short 溢出还原：存档格式固有边界（16 位长度字段）
                    DebugLogger.Log($"[LoadDiag-ReadBytes] short 溢出还原 length={length} → {length + 65536}\n{SaveStringGuard.ShortStackTrace()}");
                    length += 65536;
                }
                else
                {
                    // 真正的数据损坏：保持抛异常（OverflowException），仅打日志定位
                    DebugLogger.Log($"[LoadDiag-ReadBytes] SUSPICIOUS length={length}, stack:\n{SaveStringGuard.ShortStackTrace()}");
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// 写入侧全局 watchdog（第 2.5 层-A）：SaveContext.AddOrGetStringId 超长字符串打日志 + 调用栈。
    /// key 信息只存在于写入侧（Strings 表只有文本没有 key），读档侧永远无法定位——
    /// 这里是定位超长 key 的主战场。覆盖任何 mod / 任何存储形态（JSON 池 / SaveSystem 原生 string）。
    /// 粗筛 7000 字符以下直接跳过（7000×4B/字符=28000B &lt; 30000B 安全），正常字符串零开销。
    /// </summary>
    [HarmonyPatch(typeof(SaveContext), "AddOrGetStringId")]
    public static class SaveContextStringGuard
    {
        [HarmonyPrefix]
        public static void Prefix(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text) || text.Length < 7000) return;
                int bytes = Encoding.UTF8.GetByteCount(text);
                if (bytes <= SaveStringGuard.MaxStringBytes) return;
                DebugLogger.Log($"[SaveStrGuard] 超长字符串 {bytes}B\n{SaveStringGuard.ShortStackTrace()}");
                SaveStringGuard.NotifyTrimmed(null); // 未知字段（非本项目 JSON 池），详情见日志栈
            }
            catch { }
        }
    }

    /// <summary>
    /// 保存/加载期间 FirstChanceException 捕获（第 2.5 层双向监控）。
    /// 原版链路会吞掉真实异常（LoadContext.Load 内部 catch 只调 Debug.Print，1.3.15 不落盘）。
    /// 挂 AppDomain.FirstChanceException 捕获期间每一次抛出的异常（含完整堆栈）。
    /// </summary>
    public static class SaveLoadFirstChance
    {
        private static EventHandler<FirstChanceExceptionEventArgs> _handler;
        private static bool _capturing;
        private static int _count;
        private static string _side;

        private static void Begin(string side)
        {
            try
            {
                if (_capturing) return;
                _capturing = true;
                _count = 0;
                _side = side;
                _handler = (s, e) =>
                {
                    try
                    {
                        if (++_count > 300) return; // 限量防刷屏
                        var ex = e.Exception;
                        DebugLogger.Log($"[{_side}Diag#{_count}] {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
                    }
                    catch { }
                };
                AppDomain.CurrentDomain.FirstChanceException += _handler;
                DebugLogger.Log($"[{_side}Diag] SaveManager started, capturing first-chance exceptions...");
            }
            catch { }
        }

        private static void End(string side, bool ok)
        {
            try
            {
                if (!_capturing) return;
                _capturing = false;
                AppDomain.CurrentDomain.FirstChanceException -= _handler;
                DebugLogger.Log($"[{_side}Diag] SaveManager ended: successful={ok}, captured={_count} exceptions");
            }
            catch { }
        }

        [HarmonyPatch(typeof(SaveManager), "Load",
            new Type[] { typeof(string), typeof(ISaveDriver), typeof(bool) })]
        public static class SaveManagerLoadPatch
        {
            [HarmonyPrefix]
            public static void Prefix() { Begin("Load"); }

            [HarmonyPostfix]
            public static void Postfix(LoadResult __result)
            {
                End("Load", __result != null && __result.Successful);
            }
        }

        [HarmonyPatch(typeof(SaveManager), "Save",
            new Type[] { typeof(object), typeof(MetaData), typeof(string), typeof(ISaveDriver) })]
        public static class SaveManagerSavePatch
        {
            [HarmonyPrefix]
            public static void Prefix() { Begin("Save"); }

            [HarmonyPostfix]
            public static void Postfix(SaveOutput __result)
            {
                bool ok = __result != null && __result.Successful;
                End("Save", ok);
                // 保存成功但发生了超长裁剪 → 弹窗提醒（静默写坏存档是此 bug 最阴险的特征，玩家必须知情）
                if (ok) SaveStringGuard.ShowTrimNoticeIfAny();
            }
        }
    }
}
