using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using TaleWorlds.SaveSystem.Load;
using TaleWorlds.SaveSystem.Save;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 存档三合一防线（🔴 常驻——新增 Saveable 类型 / 遇存档问题 / 存档失败的第一取证入口）。
    /// 原三文件（SaveStringGuard / SaveErrorReporter / SaveFileReadOnlyGuard）合并为 SaveGuard.cs（2026-08-18）。
    ///
    /// 三个防线：
    /// ① 字符串超长防护（救档 + 双向监控 + 根因防线）——排查"载入存档时发生了一个错误"用。
    ///    背景：SaveSystem 的 Strings 表每条字符串长度字段是 16 位 signed short（上限 32767）。
    ///    单条字符串 &gt; 32767 字节 → 写入时溢出成负数 → 整张表错位 → 读档必崩
    ///    （ArchiveDeserializer.LoadFrom 读负长度 → ReadBytes(负数) → OverflowException → Load 返回 null → 弹窗）。
    /// ② 存档错误诊断（弹窗追加 [SaveDebug] 详情 + 序列化 NRE 定位）——存档失败弹窗截图即可反馈。
    /// ③ 存档文件只读防护（写盘前清除 ReadOnly）——外部工具把 .sav 设只读后报
    ///    PlatformFileHelperFailure / Access denied，C# 侧捕获不到，SaveDiag 显示成功但实际没写成。
    ///
    /// 全部 Harmony 补丁，PatchAll() 自动注册，无调用点。
    /// </summary>

    // ════════════════════════════════════════════════════════════════════
    // 防线① 字符串超长防护
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 字符串超长防护核心：统一守卫 + 降级提醒。
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
                    // 🔴 禁止 item.ToString(Formatting)（单参重载）——Newtonsoft 13.0.2 才新增，
                    // 而游戏自带 Newtonsoft.Json.dll 是 13.0.1（编译引用 packages 13.0.4）：
                    // 编译过、运行期 MissingMethodException → 存档必崩（星星眼）。两参重载全版本都有。
                    string itemStr = item.ToString(Formatting.None, (JsonConverter[])null);
                    int itemBytes = Encoding.UTF8.GetByteCount(itemStr);
                    int overhead = kept.Count == 0 ? 1 : 2; // 首元素无前导分隔，后续加 ","
                    if (totalBytes + overhead + itemBytes > maxBytes) break;
                    kept.Add(item);
                    totalBytes += overhead + itemBytes;
                }
                if (kept.Count == 0) return "[]";
                if (kept.Count == arr.Count) return null; // 全部保留（不可能走到这，防御）
                DebugLogger.Log($"[SyncDataGuard] 结构感知截断：保留 {kept.Count}/{arr.Count} 条完整记录");
                return kept.ToString(Formatting.None, (JsonConverter[])null);
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
                    // 本地化：LWN_save_trim_title（玩家可见文本）
                    LWNTextHelper.ResolveText("LWN_save_trim_title", "Save Warning"),
                    // 本地化：LWN_save_trim_body（玩家可见文本）
                    LWNTextHelper.ResolveCompound("LWN_save_trim_body",
                        "The save completed, but some data exceeded the safe size limit and was trimmed: {KEYS}. The oldest affected records were discarded.",
                        ("KEYS", keys)),
                    true, false,
                    // 本地化：LWN_save_trim_ok（玩家可见文本）
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
    [HarmonyPatch(typeof(TaleWorlds.Library.BinaryReader), "ReadBytes")]
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

    // ════════════════════════════════════════════════════════════════════
    // 防线② 存档错误诊断（弹窗追加 [SaveDebug] 详情 + 序列化 NRE 定位）
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 存档失败诊断补丁（🔴 常驻诊断工具——新增 Saveable 类型后如遇存档问题，靠它取证）：
    /// ① SaveManager.Save Postfix — 缓存存档失败时的底层错误详情（SaveOutput.Errors，
    ///    如 "Could not find type definition of type: X" / "SaveContext Error: ..."）。
    /// ② MBSaveLoad.ShowErrorFromResult Prefix — 拦截存档失败弹窗，把详情追加到弹窗正文，
    ///    玩家截图即可反馈具体原因。
    /// 玩家可见文本走标准本地化（LWN key，英文条目注册于 std_LivingWorldNpcs_strings.xml）。
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

                    // 文本走标准本地化：官方 key（str_*，引擎表已注册，命中各语言翻译）优先，
                    // fallback 走 LWN key 机制（英文条目注册于 std_LivingWorldNpcs_strings.xml）
                    string title = GameTexts.FindText("str_save_unsuccessful_title")?.ToString()
                        // 本地化：LWN_save_error_title（玩家可见文本）
                        ?? LWNTextHelper.ResolveText("LWN_save_error_title", "Save Failed!");
                    string baseMsg = GameTexts.FindText("str_game_save_result", result.ToString())?.ToString()
                        // 本地化：LWN_save_error_body（玩家可见文本）
                        ?? LWNTextHelper.ResolveText("LWN_save_error_body", "Cannot create save data.");

                    string detail = LastErrorDetail;
                    if (string.IsNullOrEmpty(detail))
                        // 本地化：LWN_save_error_no_detail（玩家可见文本）
                        detail = LWNTextHelper.ResolveText("LWN_save_error_no_detail", "(no error detail captured)");
                    try
                    {
                        string platformErr = Common.PlatformFileHelper.GetError();
                        if (!string.IsNullOrEmpty(platformErr))
                            // 本地化：LWN_save_error_platform（玩家可见文本）
                            detail += "\n" + LWNTextHelper.ResolveText("LWN_save_error_platform", "[Platform] ") + platformErr;
                    }
                    catch { }

                    // 诊断行走 LWN key（玩家可见）；{DETAIL} 为引擎原始错误消息（不可翻译，原样透传）
                    string debugLine = LWNTextHelper.ResolveCompound("LWN_save_error_debug_line",
                        "[SaveDebug] Result={RESULT}\n{DETAIL}",
                        ("RESULT", result.ToString()), ("DETAIL", detail));
                    string body = baseMsg + "\n\n" + debugLine;

                    InformationManager.ShowInquiry(new InquiryData(
                        title, body, true, false,
                        GameTexts.FindText("str_ok")?.ToString()
                            // 本地化：LWN_save_error_ok（玩家可见文本）
                            ?? LWNTextHelper.ResolveText("LWN_save_error_ok", "OK"), "",
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
    /// 常驻诊断工具，随 SaveErrorReporter 保留。
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
                // 1.4.x 新增了 SaveTo(BinaryWriter, ref int) 重载，只传名字会 AmbiguousMatch。
                // internal 类型 (SaveEntryFolder/IArchiveContext) 无法被 TypeByName/GetType 解析，
                // 所以直接枚举 ObjectSaveData 的方法，按参数名排除 BinaryWriter 重载（v1.4.x 独有）。
                var asm = typeof(SaveManager).Assembly;
                var type = asm.GetType("TaleWorlds.SaveSystem.Save.ObjectSaveData");
                if (type == null) return null;
                MethodBase found = null;
                foreach (var mi in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (mi.Name != "SaveTo") continue;
                    var parms = mi.GetParameters();
                    // 选非 BinaryWriter 的重载（SaveEntryFolder 版本），两个版本通用
                    if (parms.Length == 2 && !parms[0].ParameterType.Name.Contains("BinaryWriter"))
                    {
                        found = mi;
                        break;
                    }
                }
                // 兜底：如果上面没找到（如 BinaryWriter 版改名），取第一个找到的 SaveTo
                if (found == null)
                    found = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .FirstOrDefault(mi => mi.Name == "SaveTo");
                DebugLogger.Log($"[SaveReporter-Bind] ObjectSaveData.SaveTo bound={found != null}");
                return found;
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

    // ════════════════════════════════════════════════════════════════════
    // 防线③ 存档文件只读属性防护
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 存档文件只读属性防护（🔴 常驻——外部工具把 .sav 设成只读后，游戏覆盖写入被 Windows 拒绝，
    /// 弹"存档失败！无法创建存档数据"，底层是原生文件层错误 PlatformFileHelperFailure / Access denied
    /// ——C# 侧 SaveManager 捕获不到（SaveDiag 显示成功但实际没写成），SaveErrorReporter 也拿不到详情）。
    ///
    /// 机制：FileDriver.Save 是所有磁盘存档（含自动存档）的唯一出口，Prefix 在写盘前检查目标
    /// .sav 文件的 ReadOnly 属性并清除。
    /// 路径经 PlatformFilePath.FileFullPath 走原生层解析（Common.PlatformFileHelper.GetFileFullPath），
    /// 与引擎实际写盘路径严格一致——不猜 Documents 重定向，也不依赖 GetSaveFilePath（1.2.12 里是 private）。
    ///
    /// 纪律：
    /// - 只清 ReadOnly（症状修复），不保证外部工具不再设置（病因另查，[SaveReadOnlyGuard] 日志可定位）。
    /// - 全部 try/catch：任何异常不得打断存档流程（铁律 1 精神：mod 出错不能坏游戏）。
    /// - 只动目标存档文件，不批量改目录内其他文件属性。
    /// </summary>
    public static class SaveFileReadOnlyGuard
    {
        /// <summary>与 FileDriver.SavePath 同款目录名（FileDriver.Save 内部就是拼这个目录 + saveName + ".sav"）。</summary>
        private const string SaveDirectoryName = "Game Saves\\";

        [HarmonyPatch(typeof(FileDriver), nameof(FileDriver.Save))]
        public static class FileDriverSavePatch
        {
            [HarmonyPrefix]
            public static void Prefix(string saveName)
            {
                try
                {
                    if (string.IsNullOrEmpty(saveName)) return;

                    // 与 FileDriver.Save 同款路径构建（PlatformFilePath.FileFullPath 三版本都有，
                    // 走原生层解析，路径与引擎严格一致）
                    var path = new PlatformFilePath(
                        new PlatformDirectoryPath(PlatformFileType.User, SaveDirectoryName),
                        saveName + ".sav").FileFullPath;

                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

                    var attrs = File.GetAttributes(path);
                    if ((attrs & FileAttributes.ReadOnly) == 0) return;

                    File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
                    DebugLogger.Log($"[SaveReadOnlyGuard] 存档文件带只读属性（外部设置），已强制清除: {path}");
                }
                catch (Exception ex)
                {
                    // 只读清除失败不阻断保存——万一文件其实可写（权限在别处），原样放行
                    DebugLogger.Log($"[SaveReadOnlyGuard] 清除只读失败（不影响本次保存）: {ex.Message}");
                }
            }
        }
    }
}
