# 存档失败（Cannot create save data）— 诊断与修复计划

> **状态**：✅ 阶段二（修复）完成 —— 8 类 + 1 struct + 6 枚举已注册（issue 路径已验证）；✅ 对峙 NRE 崩溃根因已定位并修复（`CommissionIssueContext.PrimaryCategory` 的 Nullable 字段，见 1.6/2.5）；✅ 存-读-存循环 + 读档 issue 标题还原（_context 往返）验证通过；✅ SaveErrorReporter 决定**保留为常驻诊断工具**（玩家可见文本已正式化走 LWN key，英文条目）；剩余：2.2 全路径验证 → 双版本发布
> **来源**：外网玩家反馈"无法存档"（弹窗 `str_save_unsuccessful_title` / `str_game_save_result.GeneralFailure` = "无法创建存档数据"）+ 本地复现（含对峙阶段 NRE 崩溃）
> **相关代码**：`Debug/SaveErrorReporter.cs`（诊断补丁）、`Story/StoryContext.cs`（SaveDefiner）、`Quests/Commissions/CommissionHubIssue.cs`（_context 存档 + Nullable 修复）、`WorldEvent/`、`Core/SafeLordPartyComponent.cs`、`Core/CustomPartyComponent.cs`

---

## 🔑 交接清单（session 重启必读）

**已改动的文件（git 未提交）**：
1. `ExampleModVS/ExampleMod/ExampleMod/Story/StoryContext.cs` — SaveDefiner 注册 8 类 + 1 struct + 6 枚举（`CommissionHubIssue`=15、`QuestData`=11、`GenericQuest`=12、`CommissionQuest`=13、`CommissionData`=14、`SafeLordPartyComponent`=16、`CustomPartyComponent`=17、`CommissionIssueContext` struct=18、枚举 20-25）；Obsolete 类型行包 `#pragma warning disable CS0618`
2. `ExampleModVS/ExampleMod/ExampleMod/Quests/Commissions/CommissionHubIssue.cs` — `_context` 加 `[SaveableField(1)]`；`CommissionIssueContext` struct 12 字段加 `[SaveableField(1-12)]`；补 `using TaleWorlds.SaveSystem`
3. `ExampleModVS/ExampleMod/ExampleMod/Debug/SaveErrorReporter.cs` — 弹窗 Reporter + `SaveSerializeDiagPatch`（序列化 NRE 定位：ObjectSaveData.SaveTo Prefix 记录对象类型、VariableSaveData.SaveTo Prefix 打 `[SaveReporter-Null]`；⚠️ TargetMethod 必须 public static）
4. `ExampleModVS/ExampleMod/ExampleMod/ExampleMod.csproj` — 加 `Debug\SaveErrorReporter.cs` Compile Include

**编译状态**：`dotnet msbuild ExampleMod.csproj -p:Configuration=Debug` 已通过，DLL 已输出到游戏目录（`Modules/LivingWorldNpcs/bin/Win64_Shipping_Client/`），带诊断补丁的版本可直接进游戏复现。

**已完成的验证**：
- 11:54 复现：偷鹅 → issue 挂载 → 存档失败（CommissionHubIssue 未注册）✅
- 12:45 验证：注册后偷鹅 → issue 挂载 → **存档成功** ✅
- 12:51 复现：接任务 → 存档失败（CommissionQuest 未注册）→ 已随 8 类注册修复
- 13:04 复现：对峙走人 → 存档 **NRE 崩溃**（VariableSaveData.SaveTo）→ **根因已定位并修复（14:xx，见 1.6）**
- 已知问题：读档后 issue 标题变默认"委托任务"（_context 未存档）→ 已修（_context 加 SaveableField），但**修复前存的旧档 issue 无法还原**，需新事件重挂

**🔴 新发现并已修复：读档后任务被取消（2026-08-03）**：
- 现象：接"调查：特维亚失窃案"→ 存档 → 读档 → 立即"任务取消"（官方 `QuestManager.OnGameLoaded`）
- 根因：官方读档校验——活动 quest 必须有关联 Issue（`issue.Value?.IssueQuest == questBase`），否则 `CompleteQuestWithCancel()`；关联只在官方 `IssueBase.StartIssueWithQuest()` 里建立（`IssueQuest = GenerateIssueQuest(...)`）
- 我们的 `AcceptQuest`（AccountabilityIntents 犯罪追责路径）直接 GenerateIssueQuest，**跳过关联** → IssueQuest=null → 读档取消（CommissionIntent 常规委托走官方 `IssueManager.StartIssueQuest` 本就正常）
- 修复：`CommissionHubIssue.AcceptQuest()` 改为 `StartIssueWithQuest()` → 取 `IssueQuest` → `CompleteIssueWithQuest()`；已编译，待验证

**🔴 新发现并已修复：读档后 PartyComponent 字段丢失 NRE（2026-08-03）**：
- 现象：自首→对抗→被打死→坐牢→坐牢界面存档→读档 → `SafeLordPartyComponent.get_HomeSettlement()` NRE（`_leader` null，SafeLordPartyComponent.cs:34）
- 根因：类型注册（id 16/17）只解决"读档能解析类型"；`_leader`/`_homeSettlement`/`_displayName` 未标 `[SaveableField]` → 读档后为 null。CustomPartyComponent 有 `?? Settlement.All.FirstOrDefault()` 兜底不崩，SafeLordPartyComponent 直接解引用崩
- 修复：SafeLordPartyComponent `_leader` 加 `[SaveableField(1)]`；CustomPartyComponent `_homeSettlement`(1)/`_displayName`(2)；两处属性（Name/HomeSettlement）null-guard 兜底（旧档字段缺失不崩）；参考原版 `LordPartyComponent` 同款 `[SaveableField(30)] Hero _leader`
- 经验：**类型已注册 ≠ 字段已存档**，两者缺一不可——wheels.md「存档错误诊断」排查流程第 4 条已固化

**诊断补丁调整**：`[SaveReporter-Null]` 只报 Object/Container/CustomStruct 的 null（String null 安全——`GetStringId(null)` 返回 -1，原版 null string 是常态，之前是噪音）

**下一步（待办主线）**：
1. 进游戏复现：对峙走人 → 存档（应**成功**，不再崩溃）✅ 已通过（存-读-存循环 + 读档 issue 标题验证）
2. 顺带验证：存-读-存循环、读档后 issue 标题正确（_context 往返）✅ 已通过（2026-08-03）
3. 2.2 全路径验证 → 双版本发布；~~移除 SaveErrorReporter.cs~~ → **已决定保留为常驻诊断工具**（玩家可见文本已正式化：LWN key 英文条目，见 wheels.md「存档错误诊断」）

**待确认项**：`CommissionTargetType` 是否被 Saveable 字段引用（决定是否注册，id 可给 26）。

---

## 阶段总览

| | 阶段一：诊断与复现 | 阶段二：修复与验证 |
|---|---|---|
| **目标** | 找出**所有**触发路径；用弹窗 Reporter 让玩家/本地复现截图取证 | SaveDefiner 补注册；全路径验证；双版本发布 |
| **产出** | 触发源清单 + 复现方式清单 + `[SaveDebug]` 弹窗截图 | 修复版 DLL + 验证记录 |
| **状态** | ✅ 完成（issue/quest/party 三类根因确认；**新增发现**：对峙阶段序列化 NRE 崩溃，类型待定位） | 🔴 进行中（8 类+1 struct+6 枚举已注册；issue 路径已验证；NRE 遗留待修） |

---

## 阶段一：诊断与复现（当前阶段）

### 1.1 报错链路（反编译确认）

玩家点存档 → `MBSaveLoad.SaveAsCurrentGame` → `OverwriteSaveAux` → `SaveGame` → `Game.Current.Save` → `SaveManager.Save` → `SaveContext.Save` 序列化存档图 → 异常 → `SaveResult.GeneralFailure` → `MBSaveLoad.ShowErrorFromResult` 弹窗 "Cannot create save data."

`SaveResult` 各值触发点（TaleWorlds.SaveSystem.dll / TaleWorlds.Core.dll）：

| SaveResult | 触发点 |
|-----------|--------|
| `GeneralFailure` | ① **存档图里出现无类型定义的对象**（`ObjectSaveData` 构造抛 `"Could not find type definition of type: X"`）② `SaveContext.Save` 序列化异常 ③ `driver.Save` 抛异常 ④ 同类型内 SaveId 重复（`DefinitionContext.GotError`） |
| `FileDriverFailure` | `FileHelper.SaveFile`（native 写盘）失败：磁盘满/权限/文件被占用（杀毒、Steam 云同步） |
| `PlatformFileHelperFailure` | 平台文件助手报错 |
| `NoSpace` | 磁盘空间不足 |
| `SaveLimitReached` | 存档数达上限 |

### 1.2 触发源清单（已确认 3 条，全部会命中 GeneralFailure①）

**🔴 源头统一：发布状态下所有触发源都源于"玩家犯罪"**（mod 核心玩法，玩这个 mod 的玩家必发生）：

```
玩家犯罪（偷窃/击晕/搜刮，Mission 内检测）
  → PendingWorldEvent → FinalizePendingWorldEvent → WorldEventStore（lwn_crime_events）  ← 玩家行为直接产生，不依赖自动生成
  → 阶段推进（ProcessDormant→Emerging，走 MyBehavior.DailyTick，不受 SuppressAutoGeneration 影响）
  → 权威 NPC 挂 CommissionHubIssue（源 B）
  → 玩家接取追责/调查任务（源 A，可选）／ 对峙走人（源 C1，可选）
```

- **不犯罪 → 无任何触发**（常规委托入口 `OnSettlementEntered` 监听已注释 `CommissionHubIssue.cs:439`，`OnCheckForIssue` 首闸 `evt == null → return`）→ 存档正常（复现 #1 基线验证）
- 犯罪事件 Resolved/Unsolved → issue 清除（`OnWorldEventStageChanged` 分支）→ 存档恢复——玩家规避窗口存在，但修复前不可依赖

**触发源 A — Quest：`GenericQuest` / `CommissionQuest`（玩家犯罪后，接追责/调查任务）**

```
玩家接委托任务 → CommissionHubIssue.GenerateIssueQuest
  → new CommissionQuest(id, data) → quest.StartQuest()          [CommissionHubIssue.cs:364]
  → OnQuestStarted → QuestManager.OnQuestStarted { _quests.Add(quest) }
  → 存档 → Campaign.QuestManager（[SaveableProperty(8)]）
  → _quests（[SaveableField(0)] MBList<QuestBase>）
  → 序列化元素 → GetType() = CommissionQuest / GenericQuest（实际派生类）
  → GetClassDefinition(类型) = null（未注册）
  → ObjectSaveData 构造 throw → SaveContext catch → GeneralFailure → 弹窗
```

- 连带类型：`QuestData`（GenericQuest 持有）、`CommissionData`（CommissionQuest 持有）+ 枚举 `QuestType`/`CommissionCategory`/`ResolutionPath`/`CommissionTier`/`CommissionGrade`

**触发源 B — Issue：`CommissionHubIssue`（玩家犯罪后自动挂，无需接任务）**
- `IssueManager._issues` = `[SaveableField(1)] Dictionary<Hero, IssueBase>`（反编译确认）
- `CommissionIssueBehavior` 订阅原版 `CampaignEvents.OnCheckForIssueEvent`（每 2 天一轮）+ `WorldEventStore.OnEventStageChanged`（阶段变更即时刷新）；**首闸要求定居点有活跃犯罪事件**（`OnCheckForIssue` `evt == null → return`）→ 权威/notable NPC 挂蓝 ! 标记
- **触发条件**：玩家犯罪后，事件进入 store 并推进到 Emerging/Active/Confrontation 即挂；**玩家接不接任务都会中招**

**触发源 C — 自定义 PartyComponent：`SafeLordPartyComponent` / `CustomPartyComponent`（party 创建即进存档图）**
- `MobileParty._partyComponent` = `[SaveableField(210)]`（反编译确认）——party 创建即进存档图
- **C1 正常路径（玩家可触发）**：quest 内部 party（护送/押运/追击，`CommissionQuest.cs` 5 处）、报复部队（`InvestigationEngine.SpawnRetaliationParty`，对峙走人 `AccountabilityIntents.cs:795/984/1241`、干活抵债违约 `WorldEvent.cs:1434`）
- **C2 控制台路径**：`custom.worldevent_force` → `ForceGenerateEvent` → `SpawnEventParty`（`:1094`）
- ~~WorldEvent 每日自动生成~~：**发布状态关闭**——`SuppressAutoGeneration = true`（`:42`，作者确认发布意图：功能不完善），全库无置 false 代码；`OnDailyTick` 直接 return，级联事件/教程事件/复仇队（`:229`）同被挡

**触发时间线**（发布状态下，正常玩家触发面——起点都是玩家犯罪）：

| 触发源 | 触发时机 | 玩家操作 |
|--------|---------|---------|
| CommissionHubIssue（源 B） | 犯罪后，事件推进 + 每 2 天检查（或阶段变更即时） | 犯罪（mod 核心玩法，必发生） |
| Quest（源 A） | 犯罪 → issue 挂载后接取 | 接任务（可选） |
| 报复部队（源 C1） | 对峙走人 / 干活抵债违约 | 犯罪流程内选项（可选） |
| WorldEvent 自动生成（源 C2） | ❌ 发布状态关闭 | 仅控制台 |

**为什么未注册**：`SaveDefiner`（`Story/StoryContext.cs:188`）只注册了 `GeneratedStoryResult`（id 10）；csproj 没有配置 `TaleWorlds.MountAndBlade.SaveSystem.CodeGenerator` 的 Build Target → 无自动生成的 `IAutoGeneratedSaveManager`。原版派生类由官方 CodeGenerator 注册，mod 派生类必须自己注册。

**已排除**：同类型内 SaveId 冲突（脚本扫描 5 个带 Saveable 标记的类，SaveId 均唯一）。

### 1.3 复现方式清单（阶段一任务：逐一复现，弹窗取证）

| # | 复现方式 | 预期弹窗类型名 | 状态 |
|---|---------|--------------|:---:|
| 1 | 本地：开新档 → 推进 2-5 天**不犯罪** → 存档 | ✅ **存档成功（基线对照，证明触发=犯罪）** | ⬜ 未复现 |
| 2 | 本地：犯罪一次（偷窃/击晕）→ 等事件推进 → 存档 | `CommissionHubIssue`（最快复现路径） | ✅ **已复现**（11:54:47 弹窗确认，犯罪 9 秒后失败） |
| 3 | 本地：控制台 `custom.worldevent_force BanditRaid 3` → 立刻存档 | `CustomPartyComponent` / `SafeLordPartyComponent` | ⬜ 未复现 |
| 4 | 本地：犯罪 → 接追责/调查任务 → 存档 | `CommissionQuest` / `CommissionData`（quest 内 party 还可能带出 component） | ✅ 已复现（12:51:54 弹窗确认）→ 已随 8 类注册修复 |
| 5 | 本地：犯罪 → 对峙 → 对话选"走人" → 存档 | 🔴 **NRE 崩溃**（`VariableSaveData.SaveTo`，类型待定位） | ✅ 已复现（13:04:22 `[Crash] UnhandledException`）→ 遗留问题 |
| 6 | 玩家：发带 Reporter 的测试版 → 玩家复现存档失败 → 截图 `[SaveDebug]` 弹窗反馈 | 未知（验证与清单吻合） | ⬜ 未做 |

**判定**：任一复现的弹窗类型名 ∈ 触发源 A/B/C 清单 → 阶段一证据成立，进入阶段二。

### 1.4 弹窗 Reporter（已完成，阶段一取证工具）

**文件**：`Debug/SaveErrorReporter.cs`（已加入 csproj，`PatchAll()` 自动注册；v1.2.12 与 Latest 签名一致，已对比 `Modules/1.2.12DLL`）

两个 Harmony 补丁：
1. `SaveManager.Save` Postfix — 缓存存档失败的底层错误详情（`SaveOutput.Errors`，含 `"Could not find type definition of type: X"` / `"SaveContext Error: ..."` / SaveId 冲突信息）
2. `MBSaveLoad.ShowErrorFromResult` Prefix — 拦截存档失败弹窗，正文追加：

```
Cannot create save data.

[SaveDebug] Result=GeneralFailure
Could not find type definition of type: LivingWorldNpcs.CustomPartyComponent
```

- 弹窗标题/正文仍走标准本地化（`str_save_unsuccessful_title` / `str_game_save_result.<枚举名>`），仅追加诊断信息
- 同时写 `DebugLogger.Log`（`[SaveErrorReporter]` 前缀，落 `Debug/StoryEngine_RuntimeLog.txt`）
- 防御性：null-guard + catch 兜底，诊断代码出错放行原方法，绝不阻断存档流程
- 序列化崩溃定位补丁（`SaveSerializeDiagPatch`）：见 1.6 —— 用于对峙 NRE 崩溃的类型定位

### 1.5 存档字段全盘点（背景材料，2026-08-03 扫描）

#### 1.5.1 [SaveableField]/[SaveableProperty] 直接标记 — 需注册（49 字段 / 5 类型）

| 类型 | 字段 ID | 主要字段 | 注册状态 |
|------|---------|---------|:---:|
| `GeneratedStoryResult` | Property(1) | `string RawJson` | ✅ 已注册（SaveDefiner id=10） |
| `QuestData` | Field 1-9 | `QuestType Type`(枚举), `string TargetId`, `int TargetCount`, `Hero TargetHero`, `string TargetSettlementId`, `float StartValue`, `int GivenGold`, `string GivenItemId`, `int GivenItemCount` | ❌ 未注册 |
| `GenericQuest : QuestBase` | Field 10-13 | `QuestData _data`, `int _currentProgress`, `JournalLog _progressLog`, `bool _hasInteractedWithTarget` | ❌ 未注册 |
| `CommissionQuest : QuestBase` | Field 40-54 | `CommissionData _data`, `int _currentProgress`, `JournalLog _progressLog`, `int _totalProgress`, `int _playerCasualtiesAtStart`, `bool _isTargetCaptured`, `CommissionGrade _finalGrade`(枚举), `bool _depositRepaid`, `string _escortPartyId`, `JournalLog _findGiverLog`, `JournalLog _rewardLog`, `bool _bribeAttempted`, `bool _bribeSuccessful`, `bool _suspectIdentifiedLogged` | ❌ 未注册 |
| `CommissionData` | Field 20-36, 50, 53, 60, 61 | `string DefId`, `CommissionCategory Category`(枚举), `Hero QuestGiver`, `Hero BrokerHero`, `bool IsNarrativePhase`, `Hero TargetHero`, `string TargetSettlementId`, `string TargetItemId`, `int TargetItemCount`, `int NegotiatedReward`, `int DepositAmount`, `bool DepositRepaid`, `float TimeRemainingHours`, `int CurrentPhase`, `int PhaseProgress`, `ResolutionPath ChosenPath`(枚举), `CommissionTier Tier`(枚举), `bool IsObjectivesComplete`, `Hero RewardPayer`, `string WorldEventId`, `bool IsGenericInstigator` | ❌ 未注册 |

被引用但本身无标记的 mod 枚举（进对象图即需注册，共 5-6 个）：`QuestType`、`CommissionCategory`、`ResolutionPath`、`CommissionTier`、`CommissionGrade`（`CommissionTargetType` 未确认是否被 Saveable 字段引用）。

#### 1.5.2 SyncData 键值 — 无需注册，天然兼容旧档（27 个键 / 5 个 behavior）

键缺失 → 默认值（空态），不进入类型定义注册需求。全部 JSON 字符串 / 标量，Deserialize 均防御式回落。

| Behavior | 文件 | 键 |
|----------|------|----|
| `MyBehavior` | `Core/MyBehavior.cs` | `lwn_intent_cooldowns`、`lwn_settlement_honor`、`lwn_commission_trust`、`lwn_commission_infamy`、`lwn_commission_tiers`、`lwn_commission_narrative`、`lwn_world_director`、`lwn_nemesis`、`lwn_conspiracy`、`lwn_infiltration`、`lwn_stability`、`lwn_animal_theft`、`lwn_crime_events`、`lwn_theft_ledger`（14 个 JSON 键） |
| `PlayerDetentionBehavior` | `WorldEvent/PlayerDetentionBehavior.cs` | `lwn_detention_days`、`lwn_detention_event`、`lwn_detention_fine`、`lwn_detention_jailed`、`lwn_detention_release_day`、`lwn_detention_release_reason`、`lwn_detention_settlement`、`lwn_detention_stage`（8 个标量键） |
| `WorldEventSimulator` | `WorldEvent/WorldEventSimulator.cs` | `lwn_sim_state` |
| `AIStoryGenerator` | `Story/AIStoryGenerator.cs` | `AIStory_Result` |
| `StoryContext`（GlobalVariableBehavior） | `Story/StoryContext.cs` | `_extendedProperties`、`_globalStates`（`_npcProfiles` 已注释） |

#### 1.5.3 SaveableTypeDefiner 注册（1 个）

`Story/StoryContext.cs:188` — `SaveDefiner : SaveableTypeDefiner`（基类 id=123456789），当前只注册 `GeneratedStoryResult`（类 id=10）+ 2 个 `Dictionary<string,string>` 容器定义。

#### 1.5.4 非游戏存档的持久化（不在存档体系内，仅备注）

`config.json`（Settings）、`Debug/StoryEngine_RuntimeLog.txt`（运行时日志）、`ModuleData/DesignData/*.csv`（设计数据，只读）。

**盘点结论**：阶段二修复范围 = 1.5.1 的未注册类型（4 类）+ 触发源 B/C 的类型（3 类）+ 5-6 枚举 = **8 类 + 5-6 枚举**；1.5.2/1.5.3 无需改动。

### 1.6 🔴 新发现：对峙阶段存档 NRE 崩溃（2026-08-03，类型待定位）

**现象**：走人/对峙路径后存档，不再弹窗而是直接崩溃：
```
[Crash] UnhandledException: System.NullReferenceException
   at TaleWorlds.SaveSystem.Save.VariableSaveData.SaveTo(IWriter writer)   // (int)Value 解箱 null
   at TaleWorlds.SaveSystem.Save.ObjectSaveData.SaveTo(...)                // 177 行成员 / 194 行 childStructs 递归
   at TaleWorlds.SaveSystem.Save.SaveContext.SaveSingleObject(...)
```
**触发场景**（13:02:49 → 13:04:22 日志）：对峙对话选"转身就走" → 事件 Active → **完成旧调查 quest**（`CompleteInvestigationExternally ... betrayed`）→ **新 CommissionHubIssue 挂载**（stage=Active，SuspectName 有值）→ 2.5 分钟后存档崩溃。

**崩溃机制（反编译确认）**：
- `VariableSaveData.InitializeData` 的所有正常分支都保证 `Value` 非 null（null 引用 → `Value=-1` 写 null 标记）
- **唯一漏洞**：类型**没有定义**的字段 → 落 CustomStruct 兜底分支 `Value = data` 原样保留 → 字段为 null 时 `Value = null` → SaveTo `(int)Value` NRE
- 异常逃逸 `SaveContext.Save` 的 catch（`TWParallel.For` 并行序列化吞异常/不传播）→ 玩家侧是**真崩溃**不是弹窗

**✅ 根因已定位（2026-08-03 14:xx 诊断补丁抓到）**：

```
[14:10:43.801] [SaveReporter-Null] 对象=HeroDeveloper MemberType=CustomStruct SaveId=(1,8)
```

- SaveId=(1,8) = `LocalSaveId=8` = **`CommissionIssueContext.PrimaryCategory`（`CommissionCategory?`，[SaveableField(8)]）**
- **机制**：C# box 空 `Nullable<T>` 得 null → `Nullable<CommissionCategory>` 在类型定义表无定义 → `InitializeData` else 兜底 `Value=data=null` → `SaveTo` CustomStruct 分支 `(int)Value` 解箱 → NRE
- 反编译 `SaveTo` 确认：只有 **Object/Container/CustomStruct** 分支 `(int)Value` 会崩；String 分支 `(string)Value` 对 null 合法（日志里 174 条 `MemberType=String` 是正常 null 字符串，非崩溃点）
- ⚠️ `[SaveReporter-Null] 对象=HeroDeveloper` 是**并行序列化竞态污染**（`_currentSavingType` 是共享 static，被 TWParallel 多线程覆盖）——HeroDeveloper 自己只有 5 个 Saveable 成员（ID 100/130/101/102/103），无 ID=8；真正归属是 struct `CommissionIssueContext`（字段 ID 与 LocalSaveId 一一对应）
- 12:45 验证成功 vs 13:04 崩溃的差异：12:45 时 `_context` 尚无 `[SaveableField]`（未序列化）；13:00 加标记后一序列化 struct 即崩——**崩溃正是 _context 序列化引入的**
- **plan 2.1 的错误判断**："官方 `Nullable<>` 已有定义可存 `CommissionCategory?`" — ❌ 不成立。官方 SaveDefiner 按具体类型注册（反编译未发现泛型 Nullable 定义）；泛型 Nullable 字段在 mod 自研 struct 里落兜底分支

**✅ 修复（2026-08-03，已编译）**：struct 去掉 Nullable ——
- `CommissionCategory? PrimaryCategory` → `CommissionCategory PrimaryCategory`（[SaveableField(8)] 不变）+ 新增 `bool HasPrimaryCategory`（[SaveableField(13)]）
- 4 处读取改 `HasPrimaryCategory` / 裸枚举；2 处写入补 `?? default` + 标志位；1 处日志加三元
- 全库 grep 确认**仅此一处** Nullable Saveable 字段
- 存档兼容：struct 在修复前从未成功存过档（存了就崩），无旧档包袱；`_context` 修复前丢字段的旧档问题见 2.1

**诊断工具经验（留存）**：
- `SaveSerializeDiagPatch`（SaveErrorReporter.cs 内）已就位：`ObjectSaveData.SaveTo` Prefix 记录当前保存对象类型 + `VariableSaveData.SaveTo` Prefix 在 `Value==null` 时打印 `[SaveReporter-Null] 对象=X MemberType=Y SaveId=Z`
- ⚠️ 踩坑记录：① Harmony `TargetMethod()` 必须 **public static**（private 会被静默跳过，补丁不生效）——已修复并加 `[SaveReporter-Bind]` 绑定验证日志；② `_currentSavingType` 共享 static 在 TWParallel 并行下竞态污染，对象名仅供参考——**SaveId 与 MemberType 从 __instance 反射读取，始终准确**；③ `MemberType=String` 的 null 是合法 null 字符串（`(string)Value` 不崩），只有 Object/Container/CustomStruct 的 null 才是崩溃点——诊断过滤条件应排除 String

**后续嫌疑排查**（走人路径新增对象）：新 CommissionHubIssue 的 `_context`（已实证是崩溃源）、完成旧 quest 的残留状态、报复部队相关——修复后复现若仍有崩溃/弹窗按同样流程取证。

### 1.7 阶段一出口标准

- [ ] 复现方式 #1-#4 至少完成 2 条，弹窗截图确认类型名与清单吻合
- [ ] 发带 Reporter 的测试版给反馈玩家，收集 ≥1 张玩家截图
- [ ] 确认无第 4 条触发路径（复现时留意弹窗类型是否超出清单）

达成 → 进入阶段二。

---

## 阶段二：修复与验证（待实施）

### 2.1 SaveDefiner 补注册（`Story/StoryContext.cs`）

**实施进度**：✅ 8 类 + 1 struct + 6 枚举全部注册并编译（2026-08-03）——
- `CommissionHubIssue`(15) 已注册并验证：犯罪 → issue 挂载 → 存档成功（11:54 失败 vs 12:45 成功对比）
- `QuestData`(11)、`GenericQuest`(12)、`CommissionQuest`(13)、`CommissionData`(14)、`SafeLordPartyComponent`(16)、`CustomPartyComponent`(17)、struct `CommissionIssueContext`(18)、枚举 `QuestType`(20)/`CommissionCategory`(21)/`ResolutionPath`(22)/`CommissionTier`(23)/`CommissionGrade`(24)/`EventStage`(25)
- `QuestData`/`GenericQuest`/`QuestType` 已标 Obsolete（quest 统一迁移），注册行用 `#pragma warning disable CS0618` 压制（旧档兼容必须保留注册）
- 反编译确认 15 条注册全部在 DLL
- **遗留**：对峙阶段序列化 NRE 崩溃（见 1.6，类型待定位）——定位后补修

**新增问题（2026-08-03 发现并修复）— 读档后 Issue 标题丢失**：
- 现象：读档后 NPC 头像的可接任务名称变默认"委托任务"（`LWN_issue_title_generic`）
- 根因：`CommissionHubIssue._context`（`CommissionIssueContext` struct）**无 `[SaveableField]`** → 不随存档 → 读档后全默认值 → `TitleForContext` 的 `IsCrimeEvent=false` → 落通用标题
- 修复：`_context` 加 `[SaveableField(1)]` + struct 12 个字段加 `[SaveableField(1-12)]` + `AddStructDefinition(CommissionIssueContext, 18)` + `AddEnumDefinition(EventStage, 25)`（struct 的 `CrimeEventStage` 依赖）
- ⚠️ **原判断"官方 `Nullable<>` 已有定义可存 `CommissionCategory?`"错误**：泛型 Nullable 字段序列化落 CustomStruct 兜底，空值 box 为 null → 存档 NRE 崩溃（见 1.6）。**struct 已改非 Nullable**（`PrimaryCategory` + `HasPrimaryCategory`，字段 ID 8/13）
- ⚠️ 兼容约束：**修复前保存的存档里 issue 的 `_context` 已丢**，读旧档仍显示"委托任务"，直到该 issue 被新事件重新创建

ID 段错开：类 10-19 / 枚举 20-29。**已注册的 `GeneratedStoryResult` id=10 保持不变**（已发布的存档依赖它，改动会破坏旧档）。

```csharp
protected override void DefineClassTypes()
{
    AddClassDefinition(typeof(GeneratedStoryResult), 10);
    AddClassDefinition(typeof(QuestData), 11);
    AddClassDefinition(typeof(GenericQuest), 12);
    AddClassDefinition(typeof(CommissionQuest), 13);
    AddClassDefinition(typeof(CommissionData), 14);
    AddClassDefinition(typeof(CommissionHubIssue), 15);      // 触发源 B
    AddClassDefinition(typeof(SafeLordPartyComponent), 16);  // 触发源 C
    AddClassDefinition(typeof(CustomPartyComponent), 17);    // 触发源 C
}

protected override void DefineEnumTypes()
{
    AddEnumDefinition(typeof(QuestType), 20);
    AddEnumDefinition(typeof(CommissionCategory), 21);
    AddEnumDefinition(typeof(ResolutionPath), 22);
    AddEnumDefinition(typeof(CommissionTier), 23);
    AddEnumDefinition(typeof(CommissionGrade), 24);
    // CommissionTargetType：确认被 Saveable 字段引用后补上（未被引用则不必注册）
}
```

**注意事项**：
- 类 ID / 枚举 ID 在同一 Definer 内唯一（`AddClassDefinition` 与 `AddEnumDefinition` 的 SaveId 命名空间不同，但按段错开更安全）
- `TypeDefinition.CollectFields` 遍历继承链——`GenericQuest`/`CommissionQuest` 收集 `QuestBase` 官方字段，`CommissionHubIssue` 收集 `IssueBase` 官方字段，`MemberTypeId` 按声明类层级计算，**不会与官方定义冲突**（原版派生类同机制）
- `SafeLordPartyComponent`/`CustomPartyComponent` 无 Saveable 标记字段（`readonly Hero _leader` 等不保存），但**类型本身必须注册**——`ObjectSaveData` 按实际类型查定义，实例进图即需要
- **⚠️ 存档兼容**：分配固定 ID 后，未来新增/删除字段会破坏该类型旧档（SaveId 布局变化）。当前玩家存档反正无法保存，无历史包袱；此后改动字段布局需按存档知识文档的 ID 步进纪律操作
- 旧档（修复前的存档）加载时：旧档里没有任何 mod 类型实例（进图就会存档失败），无兼容问题

### 2.2 验证计划（修复后全路径）

| # | 操作 | 预期 |
|---|------|------|
| 1 | 开新档 → 推进 2-5 天（不接任务）→ 存档 | 存档成功（源 B 已注册） |
| 2 | `custom.worldevent_force BanditRaid 3` → 事件 party 存活时存档 | 存档成功 |
| 3 | 接委托/追责任务 → 存档 | 存档成功（源 A 已注册） |
| 4 | 存档后读档 → 任务状态保留（QuestData/CommissionData 字段往返） | 数据完整 |
| 5 | 任意 NPC 挂 ! 标记时存档 | 存档成功（源 B 已注册） |
| 6 | 自动存档路径（大地图跑动触发 autosave） | 正常 |
| 7 | 读档后继续游玩 3-5 天再存档（存-读-存循环） | 全程正常 |

### 2.3 双版本发布

- 修复涉及 `SaveableTypeDefiner` 注册（纯 C# API，两版本签名一致，已反编译 1.2.12DLL 确认 `AddClassDefinition`/`AddEnumDefinition` 存在）
- v1.2.12 电脑与 Latest 电脑分别 `dotnet build -c Release` 编译打包
- `SaveErrorReporter` 的 patch 目标方法两版本签名一致，无需 `#if`

### 2.4 发布后收尾

1. 发布说明写明：存档 bug 修复；被卡住的玩家需读 bug 之前的旧档
2. 让反馈玩家更新验证；收集确认（存档正常）
3. ~~移除 `SaveErrorReporter.cs`~~ → **保留为常驻诊断工具**（2026-08-03 决定）：玩家可见文本已正式化走 LWN key（英文条目），后续新增 Saveable 类型遇存档问题靠它取证，wheels.md「存档错误诊断」有排查流程
4. 日志观测：`Debug/StoryEngine_RuntimeLog.txt` 搜 `[SaveErrorReporter]`（存档失败详情）、`[SaveReporter-Null]`（序列化 null 成员定位）、`[SaveReporter-Bind]`（诊断补丁绑定验证）、`[Crash]`（崩溃现场）；`rgl_log.txt` 搜 `Could not find type definition` / `Unable to create save game data`（存档异常时）
