# save — 轮子速查分卷（wheels.md 索引导航）

> 🔴 **三合一（2026-08-18）**：原 `Debug/SaveErrorReporter.cs` + `Debug/SaveFileReadOnlyGuard.cs` 已并入 `Debug/SaveGuard.cs`（存档三合一防线：①字符串超长防护 ②错误诊断 ③只读属性防护），下文三节都指同一个文件。

## 存档错误诊断 — `Debug/SaveGuard.cs`（含 SaveSerializeDiagPatch，三合一文件内）

**🔴 常驻诊断工具（不删）。新增 Saveable 类型后遇存档问题（未注册类型 / 序列化 NRE / 字段丢失）的第一取证入口。** 玩家存档失败弹窗会追加 `[SaveDebug]` 诊断详情（结果码 + 引擎错误消息），序列化崩溃时日志定位到具体字段。Harmony 补丁，`PatchAll()` 自动注册，无调用点：

```csharp
// ① SaveManager.Save Postfix — 缓存失败详情（"Could not find type definition of type: X" 等）
// ② MBSaveLoad.ShowErrorFromResult Prefix — 失败弹窗正文追加 [SaveDebug] Result=X + 详情，玩家截图即可反馈
// ③ SaveSerializeDiagPatch — ObjectSaveData/VariableSaveData.SaveTo Prefix，
//    序列化 Value==null（Object/Container/CustomStruct 类型）时打 [SaveReporter-Null] 对象=X MemberType=Y SaveId=Z
```

**排查流程**：
1. **存档失败弹窗** → 截图 `[SaveDebug] Result=GeneralFailure / Could not find type definition of type: X` → X 就是没注册的类型 → `Story/StoryContext.cs` SaveDefiner 补 `AddClassDefinition`（类段 10-17 / struct 18 / 枚举段 20-25，取新 ID 前查段内占用）
2. **存档直接崩溃** → `Debug/StoryEngine_RuntimeLog.txt` 搜 `[SaveReporter-Null]` → SaveId+MemberType 定位字段 → 常见根因：**Nullable 字段**（box 空值 → CustomStruct 兜底 → `(int)Value` 解箱 NRE；SaveSystem 无 Nullable 定义，用「裸枚举 + HasXxx 标志位」替代，范本 `CommissionIssueContext.PrimaryCategory`）
3. **读档字段丢失** → 搜 `[SaveReporter-Bind]` 确认诊断补丁绑定成功（对象类型名 `_currentSavingType` 在并行序列化下会竞态污染，仅供参考；SaveId/MemberType 从 `__instance` 反射读取始终准确）
4. **读档后 NRE（字段 null）** → 自定义类型（PartyComponent 等）**只注册类型不够，字段必须标 `[SaveableField(n)]`**，否则读档后字段为 null（范本：`SafeLordPartyComponent._leader` 未标记时坐牢存档→读档→`get_HomeSettlement` NRE 崩溃；原版 `LordPartyComponent` 同款 `[SaveableField(30)] Hero _leader`）。**类型已注册 ≠ 字段已存档，两者缺一不可**；且属性访问（Name/HomeSettlement 等引擎必读点）必须 null-guard 兜底，旧档字段缺失时不崩（SafeLordPartyComponent / CustomPartyComponent 为范本，字段 ID 从 1 起步进编号）

**日志关键词**：`[SaveErrorReporter]`（失败详情）、`[SaveReporter-Null]`（null 成员定位）、`[SaveReporter-Bind]`（绑定验证）、`[Crash]`（崩溃现场）。

**本地化**：玩家可见文本走 LWN key（英文条目，`std_LivingWorldNpcs_strings.xml` 存档诊断段）：`LWN_save_error_title/body/ok/no_detail/platform/debug_line`（`{DETAIL}` 为引擎错误原文，不可翻译原样透传）。

**踩坑**：① Harmony `TargetMethod()` 必须 **public static**（private 静默跳过，补丁不生效）；② `MemberType=String` 的 null 合法不崩，只报 Object/Container/CustomStruct；③ 补丁目标方法是 internal（SaveSystem），用 `AccessTools.Method("Type:Method")` 动态绑定。

**文件位置**：`Debug/SaveGuard.cs`（存档三合一：错误诊断 + 字符串超长防护 + 只读防护）；补注册入口 `Story/StoryContext.cs`（SaveDefiner）；排查范例 [plans/outnet_fix_plans/save-failure-fix.md](../outnet_fix_plans/save-failure-fix.md)。

## 存档字符串超长防护 — `Debug/SaveGuard.cs`（🔴 常驻：救档 + 双向监控 + 降级弹窗）

**问题**：SaveSystem Strings 表每条字符串长度字段是 16 位 **signed short（上限 32767）**。单条字符串 > 32767 字节 → **写入时静默溢出成负数（不抛异常）** → 整张表错位 → 读档 `ReadBytes(负数)` → OverflowException 被吞 → 弹"载入存档时发生了一个错误"。**写入侧无异常、玩家无感知、新存档已写坏**——此 bug 最阴险的特征。

**新增任何持久化字符串（JSON 池 / `[SaveableField]` string / 字典值）前必读**，五件套：

| 件 | 类 | 作用 | 覆盖面 |
|----|----|------|--------|
| ① 读档侧救档 | `ReadBytesFix`（BinaryReader.ReadBytes Prefix） | 负数且在 **-32768..-1** → `+65536` 还原真实长度（**无损**，数据完整只长度字段溢出）；int32 真损坏（< -32768）保持抛异常不掩盖 | 全局：任何 mod 任何形态超长都救得活 |
| ② 业务层守卫 | `SaveStringGuard.GuardJson(key, json)` | 超长裁剪 + `[SyncDataGuard]` 日志**精确到 key**；**先结构感知截断**（数组型 JSON 逐元素保留，JSON 始终合法只丢最老），非数组回退硬截断 | 本项目 JSON 池 |
| ③ 写入侧全局 watchdog | `SaveContextStringGuard`（SaveContext.AddOrGetStringId Prefix） | text > 30000 字节 → `[SaveStrGuard]` 日志 + 调用栈（栈显示正在序列化的对象类型）；粗筛 `Length < 7000` 零开销 | 全局：任何 mod / 原生字段（**key 信息只存在于写入侧，读档侧永远无法定位 key——这是定位主战场**） |
| ④ 双向 FirstChance | `SaveLoadFirstChance`（SaveManager.Save/Load 前后挂 AppDomain.FirstChanceException） | `[SaveDiag#n]` / `[LoadDiag#n]` 捕获期间每次异常（含被内部 catch 吞掉的，1.3.15 Debug.Print 不落盘） | 全局 |
| ⑤ 降级提醒弹窗 | `ShowTrimNoticeIfAny`（SaveManager.Save Postfix） | 保存**成功**但发生过裁剪 → Inquiry 弹窗告知玩家（**静默写坏是最阴险处，玩家必须知情**）；LWN key：`LWN_save_trim_title/body/ok` | 玩家可见文本走本地化（铁律 13） |

**根因纪律（第 3 层：膨胀型存储逐个加"队列 + 老数据淘汰"）**：
- 🔴 **淘汰必须保证引用完整性**——**活引用永不淘汰**（Quest 进行中引用的 WorldEventId、扣押中 `PlayerDetentionBehavior.CurrentEventId`）；结案数据保留 N 天缓冲再淘汰；淘汰打 `[Trim]` 日志可追溯；调用方 null 兜底逐处审计（双保险）。
- **字节预算核算**：中文 1 字 = 3 字节，预算 = 条数上限 × 单条约 250B-1KB，总量留 27KB/30KB 余量。预算口径统一按 **UTF-8 字节**（字符数判断会被中文绕过：30000 字符 = 90000 字节）。
- **范本**：`TheftLedger.BuildSerializeList`（未清 60 + 已清 50 by Day）、`WorldEventStore.TrimResolvedForSerialize`（活跃 Dormant 只淘汰未发现案件 + 结案 10 + `GetProtectedEventIds` 豁免）。

**调用范例**（MyBehavior.SyncData 统一接入，新 key 照抄）：
```csharp
string theftLedgerJson = SaveStringGuard.GuardJson("lwn_theft_ledger", TheftLedger.Serialize());
dataStore.SyncData("lwn_theft_ledger", ref theftLedgerJson);
```

**日志关键词**：`[SyncDataGuard]`（裁剪到 key）、`[SaveStrGuard]`（全局超长）、`[LoadDiag-ReadBytes]`（溢出还原/SUSPICIOUS）、`[LoadDiag#n]`/`[SaveDiag#n]`（FirstChance）、`[TheftLedger] Trim`/`[WorldEventStore] Trim`（根因裁剪）、`[ExtPropsGuard]`。

**文件位置**：`Debug/SaveGuard.cs`（Harmony `PatchAll()` 自动注册，无调用点）；接入点 `Core/MyBehavior.cs`；排查案例 [plans/save-string-overflow-fix.md](../../save-string-overflow-fix.md)。

**离线体检/修复工具**：`Scripts/save_inspect.py`（解析 .sav → 体检 Strings 表 → `--dump=<key>` 查看具体 JSON → `--fix --apply` 定点手术修复，自动备份）。玩家发来坏档时先跑它定位超长 key，再决定游戏内修还是工具修。格式细节与实测数据见 [Knowledge/存档机制深度解析.md](../../../Knowledge/存档机制深度解析.md) 第 10/12/13 章。

## 存档文件只读防护 — `SaveFileReadOnlyGuard`（防线③，同 `Debug/SaveGuard.cs` 文件内）

**问题**：外部工具（杀软/备份/手动勾选）把 `.sav` 设为只读后，游戏覆盖写入被 Windows 拒绝，弹"存档失败！无法创建存档数据"。这是**原生文件层**错误（`PlatformFileHelperFailure` / `[Platform] Access denied`）——C# 侧 `SaveManager` 捕获不到，`[SaveDiag]` 显示 `successful=True` 但实际没写成，`SaveErrorReporter` 也拿不到详情（2026-08-18 实机案例）。

**机制**：`FileDriver.Save` 是所有磁盘存档（含自动存档）的唯一出口，Prefix 写盘前检查目标 `.sav` 的 `ReadOnly` 属性并清除。路径经 `PlatformFilePath.FileFullPath` 走原生层解析（`Common.PlatformFileHelper.GetFileFullPath`），与引擎实际写盘路径严格一致——不猜 Documents 重定向，也不依赖 `GetSaveFilePath`（**1.2.12 里是 private**，1.3.15+ 才 public static）。

**版本兼容**：`FileDriver.Save(string, int, MetaData, GameData)` 签名 + `PlatformFilePath.FileFullPath` + `PlatformFileType.User` 三锚点全一致（1.2.12/1.3.15/1.4.6 实测），无需 `#if`。

**纪律**：只清 `ReadOnly`（症状修复），不保证外部工具不再设置（病因另查，`[SaveReadOnlyGuard]` 日志可定位复发）；全部 try/catch 不阻断存档；只动目标文件不批量改属性。

**日志关键词**：`[SaveReadOnlyGuard]`（清除成功/失败）。

**文件位置**：`Debug/SaveGuard.cs`（`SaveFileReadOnlyGuard.FileDriverSavePatch`，`PatchAll()` 自动注册）。


---

## 🔴 记忆存档读档周期（`AllNpcMemoryManager`）— 2026-08-21 实施

**解决**：读档后未互动 NPC 的记忆静默丢失——`SerializeSlot` 只遍历 `_activeMemories`（static，新进程读档为空）→ 保存只写本会话互动过的 NPC → 未互动 NPC 记忆逐轮覆盖丢失（实机实证：147藏身处.sav party store 19.6KB 幸存、npc_mem 全空；频道消息在 store 而记忆无 = 本 bug）。

**四件套**（对应 [plans/npc-memory-save-restore-fix.md](../../../plans/npc-memory-save-restore-fix.md) A/B/C/D）：
1. **A 双源写回**：`SerializeSlot` = `_pendingRestores`（读档权威数据）优先写回 + `_activeMemories` 补写 + `seen` HashSet 防同 NPC 双写（`TryMergePendingRestore` 合并即移除 → pending/active 互斥）；`IsEmptyEntry` 双签名（NpcMemorySaveEntry / SingNpcMemorySystem 两套字段）。
2. **B 读档清双字典**：`ResetActiveMemories(bool clearPendingRestores = true)`——SyncData IsLoading 分支**循环外**清双字典；**🔴 `CampaignEvents.OnGameLoadedEvent` 在 SyncData 之后触发，二次清空只能清 `_activeMemories`（`clearPendingRestores: false`）——清 pending = 读档记忆全丢**（实机 History=0 根因，2026-08-21）。事件名经反编译核实。
3. **C 槽内排序**：`LastActivityOf`（历史/记忆最大时间戳）降序——GuardJson 结构感知截断"丢尾部 = 丢最老"语义成立。
4. **D 读档钳制**：写回前 `ClampEntryToCap`（`CapsFor` 与 `ComputeCap` 同公式，save 时点 heat 可信）+ `RestoreFromSave` 末尾硬钳（动态 FIFO + 永久截断，无 LLM）——**钳制不能放 DeserializeSlot**（heat key 在 mem 槽之后反序列化，读档瞬间档位不可信，过度裁剪 = 数据丢失）。

**🔴 锁纪律**：`_dictLock` 覆盖 `_activeMemories`/`_pendingRestores` 全部读写（GetMemory 查-建-加原子化——LLM 回调/IM 后台线程会并发写）；锁序 `_dictLock` → 实例 `_lock` 单向（防死锁）。

**日志关键词**：`[NPCInfo-Mem]`（面板记忆快照）、`[ImChatStore]`（IM 打开频道状态）——对时间戳判断先后。

---

## 🔴 `save_inspect.py --keys` 大小显示不可靠 — 2026-08-21 实证

**症状**：`--keys` 显示 `lwn_im_group_party (22B)` 实际 19,624B（49 条消息）、`lwn_npc_mem_20 (18B)` 实际 5,248B——疑似 key/值 entry 取错（key 与值成对相邻，--keys 取到了相邻 entry 大小）。

**纪律**：排查存档内容一律用 `--dump=<key>`（按 key 定位值 entry，实测准确）；`--keys` 只用于看 key 是否存在。修复待办。
