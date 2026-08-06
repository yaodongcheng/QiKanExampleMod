# save — 轮子速查分卷（wheels.md 索引导航）
## 存档错误诊断 — `Debug/SaveErrorReporter.cs`（含 SaveSerializeDiagPatch）

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

**文件位置**：`Debug/SaveErrorReporter.cs`（两个诊断类同文件）；补注册入口 `Story/StoryContext.cs`（SaveDefiner）；排查范例 [plans/outnet_fix_plans/save-failure-fix.md](../outnet_fix_plans/save-failure-fix.md)。
