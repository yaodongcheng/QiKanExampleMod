# 已造轮子速查

> **总纲：加功能前先查本表。命中就复用，不要重写。**
> 路径均相对 `ExampleModVS/ExampleMod/ExampleMod/`。签名为核实后的真实签名。

---

## 配置与世界观 — `Core/Settings.cs`

全局配置单例 + 世界观参数化。

```csharp
Settings.Instance.IsLLMReady           // LLM 总闸（三字段非空才 true）
Settings.Instance.LLMBaseUrl/LLMApiKey/LLMModel
Settings.Instance.WorldDescription     // 默认卡拉迪亚，TaikouContent 注入战国
Settings.Instance.EraDescription / SpeechStyle / WarriorTerms / FemaleSelfAddress
Settings.Reload();                     // 重载 config.json
```

世界观相关字串**只能**从这里取，禁止硬编码（见 [worldview.md](worldview.md)）。需要新 flavor 字段就往 Settings 加，默认值给卡拉迪亚版。

## 双配置体系 — `Core/Settings.cs`（config.json）+ `Core/MCMSettings.cs`（MCM Mod 选项 UI）

**🔴 同一个配置项只能存在于一边（禁止交叉）；允许 MCMSettings 单向读写 Settings（facade 透传）。** 详细纪律见 CLAUDE.md「双配置体系」。小白玩家高频调整的开关/文本框 → `MCMSettings`（游戏内 选项→Mod 选项→Living World NPCs）；开发者调试/世界观参数/列表型 → `Settings` + config.json。

```csharp
// MCM（Bannerlord.MBOptionScreen）设置页：继承 AttributeGlobalSettings<T> 即被 MCM 启动时自动扫描注册（AppDomain 全程序集扫描），无需手动注册
public sealed class MCMSettings : AttributeGlobalSettings<MCMSettings>
{
    public override string Id => "LivingWorldNpcsSettings_v1";  // 文件名 = Id + ".json"
    public override string DisplayName => new TextObject("{=LWN_mcm_display_name}Living World NPCs").ToString();
    public override string FolderName => "LivingWorldNpcs";     // 存储目录名
    public override string FormatType => "json2";               // 存 {USERPROFILE}\Documents\Mount and Blade II Bannerlord\Configs\ModSettings\Global\{FolderName}\

    [SettingPropertyText("{=LWN_mcm_llm_base_url}LLM API Base URL", Order = 0, RequireRestart = false, HintText = "{=LWN_mcm_llm_base_url_hint}...")]
    [SettingPropertyGroup("{=LWN_mcm_grp_llm}LLM Configuration")]
    public string LLMBaseUrl { get => Settings.Instance.LLMBaseUrl; set => Settings.Instance.LLMBaseUrl = value; }  // facade 透传核心 Settings
}
```

- **加新 UI 配置项** = 加一个 `[SettingPropertyXxx]` 属性（v2 特性：Bool/Integer/FloatingInteger/Dropdown/Text/Button + Group 支持 `"组/子组"` 嵌套，下拉用 `MCM.Common.Dropdown<T>`），显示名用 `{=LWN_mcm_*}`（铁律 13），**再补** `std_LivingWorldNpcs_strings.xml`（英）+ `CNs/`（中）条目。
- **MCM 特性参数是编译期常量**：显示名只能写 `{=KEY}fallback` 字面量（引擎显示时查表），不能调 `LWNTextHelper`。
- **MCM json 只序列化带 `[SettingProperty]` 的属性**（`BaseSettingsJsonConverter` 遍历 `GetAllSettingPropertyDefinitions`）——隐藏字段放 MCMSettings 既不显示也不存盘（数据丢），隐藏变量必须留 config.json。
- **`MCMSettings.Instance` 在 MCM 注册前为 null**（`GlobalSettings<T>.Instance` 查容器），业务代码禁止读它，一律走 `Settings.Instance`（永不 null，铁律 1 保障）。LLM 三字段已 `[JsonIgnore]` 切断 config.json 侧（唯一来源 = MCM UI），新增玩家可配置字段照此办理。

**文件位置**：`Core/MCMSettings.cs`（MCM 设置页）、`Core/Settings.cs`（config.json 内部源）。csproj 引用：`<Reference Include="MCMv5">` → `$(MB2_PATH)\Modules\Bannerlord.MBOptionScreen\bin\Win64_Shipping_Client\MCMv5.dll`（`Private=False`，各锚点电脑必须装 MBOptionScreen——SubModule.xml 已声明硬依赖）。

## Mission 非战斗互动开关 — `Settings.DisabledInteractionMissionModes`

**设计意图**：战场中很多系统是多余的——敌人不需要"警戒"你拔刀（已经在打了），满屏血条碍眼，击晕/偷窃/对话也不该在两军对垒时触发。统一用一个可配置列表控制这些非战斗系统的开关。

### 受控系统 × 场景矩阵

| 系统 | 和平场景（城镇/村庄/大厅） | 战场（Battle/Deployment/Duel） |
|------|:---:|:---:|
| **NpcSightSystem** 视野追踪 | ✅ 追踪谁在看谁 | ❌ tick 跳过 |
| **AgentBrain** 警戒值认知 | ✅ 拔刀/蹲下/偷窃 → 警戒值累积 | ❌ 冻结 |
| **警戒眼睛** (ShowAlert) | ✅ 显示，FOV 豁免 | ❌ 强制隐藏 |
| **血条** (ShowHealth) | ✅ 战斗中/掉血/戒备（WatchState）时显示 | 🔶 仅玩家攻击过的 Agent |
| **IsPlayerSeeing** (投影+遮挡) | ✅ 正常 | ✅ 正常 |
| **击晕** (TryKnockoutAgent) | ✅ 允许 | ❌ 禁止 |
| **偷窃** (TryStealFromAgent) | ✅ 允许 | ❌ 禁止 |
| **偷动物** (TryStealAnimal) | ✅ 允许 | ❌ 禁止 |
| **对话/闲聊** (F/G 键) | ✅ 允许 | ❌ 禁止 |
| **搜刮尸体** (LootAgent) | ✅ 允许 | ✅ 保留 |

### 场景分类（MissionMode 枚举全量 8 个值）

| MissionMode | 用途 | 默认行为 | 理由 |
|:---|------|:---:|------|
| 默认(0) | 城镇街道/城主大厅/城堡内部/村庄 | ✅ 全部开放 | 和平场景 |
| `Battle` | 野战/攻城/藏身处 | ❌ 关闭 | 两军交战 |
| `Deployment` | 战前布阵阶段 | ❌ 关闭 | 大量士兵站一起 |
| `Duel` | 竞技场决斗 | ❌ 关闭 | 竞技场内 |
| `Conversation` | 对话 | ✅ 全部开放 | 对话中 |
| `Barter` | 交易 | ✅ 全部开放 | 交易 UI 覆盖 |
| `CutScene` | 过场动画 | ✅ 全部开放 | 过场自动处理 |
| `Replay` | 回放 | ✅ 全部开放 | 回放模式 |
| `Stealth` | 潜入任务 | ✅ 全部开放 | 潜入核心玩法 |

### API

```csharp
Settings.Instance.DisabledInteractionMissionModes   // List<string>，默认 ["Battle", "Deployment", "Duel"]
Settings.Instance.IsInteractionDisabled()           // → bool — 当前 Mission 是否应关闭非战斗互动

// config.json 配置例：想保留竞技场互动，删掉 "Duel"；想让潜入也关闭，加 "Stealth"
// "DisabledInteractionMissionModes": ["Battle", "Deployment"]
```

### 消费点

| 消费方 | 调用位置 | 效果 |
|--------|---------|------|
| `NpcSightSystem.OnMissionTick` | tick 入口 | 跳过观察者追踪 |
| `AgentBrain.Tick` | tick 入口 | 冻结 Tick 全部逻辑（警戒值/行为队列/默认行为） |
| `AgentBrain.ReceiveEvent` | 事件入口 | 🛡️ 最终兜底，任何路径的事件均拦截 |
| `AgentAIController.SendEventToAgent` | 事件分发 | 🛡️ 源头拦截，单目标事件不发送 |
| `AgentAIController.BroadcastEventInRange` | 事件广播 | 🛡️ 源头拦截，广播事件不发送 |
| `AgentHudMissionView` | `alertValue` 赋值 | 强置 0，隐藏警戒眼睛 |
| `AgentHudVM.UpdateLogic` | `ShowHealth` 赋值 | 叠加 `IsAgentAttackedByPlayer` 过滤 |
| `AgentHudVM.UpdateLogic` | `ShowIntentDebug` 赋值 | 强制 false，隐藏 Intent 调试文本 |
| `InteractionMissionView.HandleInput` | F/G 键入口 | 阻断击晕/偷窃/对话，保留搜刮 |
| `InteractionMissionView.OnMissionTick` | tick 入口 | 跳过交互 UI 全部逻辑 |
| `InteractionMissionView.TryKnockoutAgent` | 击晕入口 | 防御守卫 |
| `InteractionMissionView.TryStealFromAgent` | 偷窃入口 | 防御守卫 |
| `InteractionMissionView.TryStealAnimal` | 动物偷窃入口 | 防御守卫 |

**文件位置**：`Core/Settings.cs`

### 玩家攻击追踪 — `AttackTriggerMissionLogic.IsAgentAttackedByPlayer`

`OnAgentHit` 中记录玩家实际命中（造成伤害）的敌方 Agent（`HashSet<int>` 按 Agent.Index），供战场血条过滤查询。**注意**：记录在 `OnAgentHit` 而非 `OnRegisterBlow`——只有真正造成伤害才算，格挡/空挥不计；且通过 `Team.IsEnemyOf` 过滤友军。

```csharp
AttackTriggerMissionLogic.Instance.IsAgentAttackedByPlayer(agent);  // → bool
```

per-Mission 生命周期，新 Mission 自动清空。

**文件位置**：`Combat/AttackTriggerMissionLogic.cs`

## Gauntlet UI：双版本 XML 布局兼容 — `GUI/StackLayoutVerticalSwapPatch.cs`

**问题**：v1.2.12 的 `StackLayout.LayoutLinearVertical` 有 bug——`VerticalBottomToTop` 和 `VerticalTopToBottom` 实现互换了。v1.3.0+ 修复了该 bug，但导致同一套 XML 在两个版本上视觉顺序相反。

**方案**：Harmony patch `StackLayout.OnLayout` 的 Prefix，对标记了 `Id="LWN_xxx"` 的 ListPanel 做 swap。只在 `#if MB2_GE_130`（v1.3.0+）编译。v1.2.12 不编译此 patch，保持 bug 行为。双版本共用同一套 XML。

### 用法：需要 swap 的 ListPanel 加 Id

```xml
<!-- XML：在需要 swap 的 ListPanel 上直接加 Id="LWN_xxx" -->
<ListPanel Id="LWN_MainList_InteractArea" StackLayout.LayoutMethod="VerticalBottomToTop" ...>
```

```csharp
// C# 自动匹配：widget.Id.StartsWith("LWN") → swap VerticalBottomToTop ↔ VerticalTopToBottom
// 无需注册新 Id，只需确保前缀 "LWN"
```

### 关键踩坑

- **Id 必须写在 `<ListPanel>` 自身上**，不能写在父级 `<Window>` 上。`<Window>` 是 `CustomWidgetType`（从单独 XML 加载），其内部 widget 树结构与外层不同，`ParentWidget` 链可能不通。
- **`Tag` 属性在 GauntletUI XML 中不生效**——XML 解析器不把 `Tag` 映射到 `Widget.Tag`。
- **`LayoutMethod` 直接属性无效**——GauntletUI ListPanel 的有效属性是 `StackLayout.LayoutMethod`（attached property）。

**文件位置**：`GUI/StackLayoutVerticalSwapPatch.cs`

## Gauntlet UI：新增 VM 属性 → 必同步改 XML

**铁律**：给 ViewModel（`.cs`）新增 `[DataSourceProperty]` 属性时，**必须同时修改对应的 `.xml` widget 绑定文件**。只加 C# 属性不改 XML，Gauntlet 不会自动绑定——属性白写了。

### 涉及文件对

| VM (.cs) | Widget (.xml) |
|----------|---------------|
| `AgentHUD/AgentHudVM.cs` | `GUI/Prefabs/AgentHudNearby.xml` |
| 其他 VM | 对应 Prefabs 目录下的 xml |

### 典型场景：新增 bool 可见性开关

```csharp
// ① VM 侧：加 [DataSourceProperty] bool
[DataSourceProperty]
public bool ShowIntentDebug { get => ...; set { ...; OnPropertyChangedWithValue(value, "ShowIntentDebug"); } }

// ② 构造函数初始化：设 false
ShowIntentDebug = false;

// ③ UpdateLogic 中设值
ShowIntentDebug = !Settings.Instance.IsInteractionDisabled() && intent != null;
```

```xml
<!-- ④ XML 侧：Widget 绑定 IsVisible="@ShowIntentDebug" -->
<RichTextWidget ... IsVisible="@ShowIntentDebug" Text="@NpcIntentDebugText" />
```

**检查清单**（新增 VM 属性后逐条确认）：
1. `[DataSourceProperty]` 特性已加
2. `OnPropertyChangedWithValue(value, "PropertyName")` 字符串与属性名一致
3. 构造函数 `InitializeForAgent` / `ResetAllDisplay` 中已初始化默认值
4. `.xml` 中对应 Widget 的绑定属性已写（`IsVisible` / `Text` / `SuggestedWidth` 等）
5. XML 绑定名 `@PropertyName` 与 C# 属性名大小写严格一致

## LLM 调用 — `LLM/LLMService.cs`

单例，内置 3 次重试、HttpClient 复用。

```csharp
await LLMService.Instance.ChatAsync(systemPrompt, max_tokens = 150, needJson = true);  // 通用
await LLMService.Instance.SummarizeAsync(systemPrompt);    // 记忆总结（短）
await LLMService.Instance.MergeMemoryAsync(systemPrompt);  // 远期记忆合并
LLMService.CleanJson(raw);             // 静态，剥离 markdown ```json 包裹
```

调用前查 `IsLLMReady`；返回的 JSON 必须防御性处理（见 [defensive-coding.md](defensive-coding.md)）。

## Prompt 构建 — `LLM/PromptBuilder.cs`

按场景的静态 prompt 工厂。加新对话场景 = 在这里加一个 `BuildXxxPrompt` 静态方法，**不要在业务代码里拼 prompt 字串**。现有方法覆盖：开场冲突、技能检定结果、闲聊、谈判（核心）、社交事件分析、记忆长期化、对话总结、导演梗概、演出脚本生成。

## NPC 动作 / 走位 / 朝向 — `Core/AgentControlHelper.cs`

**做 NPC 演出、移动、锁定一律走这里**，不要直接调 `Agent.SetScriptedPosition` 等裸 API。

```csharp
// 动画
AgentControlHelper.SetPose(agent, actionId);  GetPose(agent);  IsPlayingPose(agent, actionId);
// 强制动画（绕过 action_set 限制，临时切换到 as_human_warrior）
AgentControlHelper.ForcePlayAction(agent, actionId, restoreAfter = false);
// 移动（async 自动寻路+等待）
await AgentControlHelper.MoveTo(agent, targetVec, targetDir, stopDistance = 0.5f);
await AgentControlHelper.MoveToActor(npc, actor, stopDistance = 0.5f);
await AgentControlHelper.MovePrepare(npc);          // 移动前清 AI/停交互
AgentControlHelper.MoveEndAndInteractPrepare(npc[, initPos]);  // 到位后锁定进对话
// 朝向 / 锁定
AgentControlHelper.LookAtAgent(agent, target);  StopLooking(agent);
AgentControlHelper.FaceToActor(turnAgent, targetAgent);
AgentControlHelper.ForceUnlockAgent(agent);  StopAndReset(agent);  // 恢复自由
// 人形判定（人类或儿童 human_child——引擎把儿童排除在 IsHuman 外，玩家认知里小孩也是人）
AgentControlHelper.IsHumanOrChild(agent);     // 所有「人形角色」判定统一用它，见「引擎级非战斗人员」专节
// 信息抽取（拼 prompt 用）
AgentControlHelper.GetPartyInfo(hero);  GetBagInfo(hero, IsPrompt = false);
// 资源操作（铁律4 —— 金钱=特殊物品，三类各有纪律，禁止裸调 ChangeHeroGold/ItemRoster.AddToCounts）
// ① 转移 Transfer（守恒，贿赂/罚款/赏赐/买卖）—— null 任一端 = 对接「世界」（收发②）
int g = AgentControlHelper.TransferGold(from, to, amount, notify = true);   // 不足自动截断、绝不变负，返回实际值
int n = AgentControlHelper.TransferItems(from, to, item, count);            // item 可传 ItemObject 或 EquipmentElement(保品质)
AgentControlHelper.SetGold(hero, targetGold, notify = false);               // 绝对赋值（剧本/调试上帝指令，非守恒）
// ③ 转换 Convert（按配方非守恒，守卫+原子；引擎外自定义资源走 onConverted 钩子）
bool ok = AgentControlHelper.TryConvert(owner, inputs, outputs, onConverted);
//   inputs/outputs = IList<ResourceCost>；ResourceCost.Gold(n) / ResourceCost.Of(item, n)
//   例：吃苹果回饱腹 → TryConvert(player, [ResourceCost.Of(apple,1)], null, () => satiety += 10)
AgentControlHelper.HasResource(owner, ResourceCost.Of(item, n));            // 单项库存校验
// 婚姻
AgentControlHelper.ApplyMarriageLogic(h1, h2);  OnPlayerSelect_MarryNewLover(newLover);
```

## 记忆系统三件套 — `Memory/`

```csharp
// 入口：拿某 NPC 的记忆系统（惰性创建）
SingNpcMemorySystem mem = AllNpcMemoryManager.GetMemoryForAgent(agent);
SingNpcMemorySystem mem = AllNpcMemoryManager.GetMemory(stringId);   // 按英雄 id
AllNpcMemoryManager.ClearTemporaryMemories();   // 清临时士兵记忆，防泄漏
```

- `SingNpcMemorySystem`：单 NPC 的 `RecentHistory`（对话）/`DynamicMemories`（近期）/`PermanentMemory`（远期）/`GlobalNews`/`CurrentNegotiationState`/`KnownEvents`。
- `NPCProfile`：人设容器。`GetPersonaPrompt()` 聚合全部人设；`CalCurrentMotivation()` 推动机；`CalculateEstimatedValue()` 算身价；`GetCloseRelations(...)` 取关系网。
- 给 NPC 加新「记忆维度 / 人设字段」时往这三件套加，不要另起 NPC 数据类。

## 设计数据加载 — `Data/DesignDataLoad.cs`

通用 CSV ORM。**新增可配置设计数据走 CSV，不要硬编码进 .cs**。

```csharp
DataTable t = GameDatabase.Heroes;            // 已有表：Heroes/Music/TagPoint/Camera/Emotion/NpcSpeech
DynamicRecord r = t.GetByID("hero_001");      // 按英文 ID
DynamicRecord r = t.GetByScriptName("某中文名"); // 按中文名（反向索引）
r.GetString(key)/GetInt(key)/GetFloat(key)/GetBool(key)/GetList(key);
GameDatabase.Initialize();                    // Mod A 启动时
GameDatabase.LoadTablesFromPath(path);        // 内容包注入入口（TaikouContent 用）
```

## Emotion ↔ NpcSpeech 一致性铁律 — `ModuleData/DesignData/Emotion.csv`

**`NpcSpeech.csv` 的 `Emotion` 列值必须是 `Emotion.csv` 中已定义的 ID。** 禁止使用未定义的 emotion。

```
NpcSpeech.csv Emotion 列 ──外键──→ Emotion.csv ID 列
      alert, threat, rage, …          alert → act_conversation_warrior_start
                                      threat → act_conversation_threat_body
                                      rage → act_conversation_rage
```

**检查方式**：加载 `NpcSpeech.csv` 时遍历所有行，`GameDatabase.Emotion.GetByID(emotion)` 判空，未命中记错误日志 + 回落 `normal`。

```csharp
// NpcSpeech.csv 加载后的校验
foreach (var row in GameDatabase.NpcSpeech.GetAll())
{
    string emotion = row.GetString("Emotion", "normal");
    if (GameDatabase.Emotion.GetByID(emotion) == null)
        DebugLogger.Log($"[NpcSpeech] 未定义的 Emotion: '{emotion}' in row {row.GetString("ID")}");
}
```

**动作存在性校验（已知限制）**：`Emotion.csv` 中的 `Animations` 列（如 `act_conversation_threat_body`）由 `AgentControlHelper.ForcePlayAction` 播放。但动画是否真正可用取决于 Agent 的 `action_set`——平民、守卫、儿童各自继承了不同的 action_set，部分动画可能不存在于当前 Agent 的 action_set 中导致静默失败。**目前无编译时或加载时校验手段**（action_set 是 C++ native 层，C# 层只能 try-catch 运行时错误）。对策：① Emotion 只用已验证可用的动画 ID（参见 `Knowledge/击晕机制_引擎能力与实现踩坑.md` 的 action_set 继承链分析）；② `ForcePlayAction` 内部已有临时切换 `as_human_warrior` 的绕过逻辑；③ 新增动画前在实际游戏中验证。

## 日志 — `Debug/DebugLogger.cs`

```csharp
DebugLogger.Log("消息");   // 线程安全，落盘到 Configs/StoryEngine_RuntimeLog.txt
```

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

## 安全建部队 — `Core/SafeLordPartyComponent.cs`

为英雄创建独立部队时用它做最小 PartyComponent（带 null 防护），避免裸建 component 崩溃。

## 大地图 Party 出生点验证 — `WorldEvent/WorldEventSimulator.FindReachableSpawnPosition`

在大地图上为 party 选定出生点时，**必须验证出生点到目标定居点的寻路可达性**。仅检查 navmesh 面是否有效（`GetFaceIndex().IsValid()`）不够——山顶/隔水区域也有 navmesh 面，但和定居点不连通，mouse 光标会变禁用圈。

三层验证管线：

```csharp
// 入口：只需传目标定居点，内部自动生成多方向候选 + 验证
Vec2 spawnPos = FindReachableSpawnPosition(targetSettlement);

// 内部验证：
// ① GetLastPointOnNavigationMeshFromPositionToDestination — 几何投影到 navmesh
// ② AreFacesOnSameIsland(projectedFace, settlementFace) — 🔑 同一连通岛？（排除隔山/隔水）
// ③ GetPathDistanceBetweenAIFaces(projectedFace, settlementFace, …, 100f, out pathDist) — 🔑 寻路距离合理？
// ④ pathDist <= straightDist * 3 — 排除绕远路的孤立路径
// 候选：定居点周围 3 圈 × 8 个方向（18/30/42 单位半径），取第一个通过全部验证的
// 兜底：GetAccessiblePointNearPosition(settlementPos, 30f) — 引擎原生
```

**禁止**在任何 party 出生点计算中裸用 `GetFaceIndex().IsValid()` 作为可达性判断——这与鼠标变禁用图标不是同一套逻辑。鼠标禁用图标用的是 `AreFacesOnSameIsland`。辅助 party 的 `NearInstigator`/`BetweenParties` 用 `GetAccessiblePointNearPosition` 即可（参照物是已有 party，本身在可达区域）。

## 通知防刷屏冷却 — `WorldEvent/WorldEventDirector`

高频检查（如 `OnCampaignTick` 每 2 秒扫一次）里推送通知时，**必须加 per-event 冷却字典**，否则玩家站在事件附近每 2 秒弹一次同一条通知。

```csharp
// 字段
private static readonly Dictionary<string, float> _interceptCooldowns = new Dictionary<string, float>();
private const float INTERCEPT_COOLDOWN_DAYS = 0.15f; // ~3.6 小时

// 使用
if (_interceptCooldowns.TryGetValue(selected.EventId, out float lastDay)
    && currentDay - lastDay < INTERCEPT_COOLDOWN_DAYS)
    return; // 冷却中，跳过
_interceptCooldowns[selected.EventId] = currentDay;

// 定期清理过期记录（>1 天），防止内存泄漏
var expired = _interceptCooldowns.Where(kv => currentDay - kv.Value > 1f).Select(kv => kv.Key).ToList();
foreach (var key in expired) _interceptCooldowns.Remove(key);
```

---

# 三大可扩展引擎（核心资产 —— 加玩法优先挂这里）

## Story 命令引擎 — `Story/`

JSON 脚本驱动的剧情演出引擎，**命令模式**。`CommandManager`（注册分发）+ `StoryEngine`（栈式执行）+ `VisualCommands`/`SystemCommands`/`LogicCommands`（指令实现）+ `StageDirector`（站位/出入场）。

**加一条新剧情指令的正确姿势**：

```csharp
public delegate bool CommandHandler(ScriptNode node, StoryEngine engine);

// 1. 写 handler（放进对应的 Visual/System/Logic Commands 类）
public static bool HandleMyCmd(ScriptNode node, StoryEngine engine) { /* ... */ return true; }

// 2. 在 CommandManager.RegisterAll() 里注册
Register("我的指令", VisualCommands.HandleMyCmd);
```

- 返回值约定：`true` = 阻塞、等玩家输入/动画；`false` = 立即执行下一行。
- **禁止**改 `CommandManager.Execute` 或写 if-else 指令链——只 `Register`。
- 已有指令：對話/自語/旁白/對話選擇、人物登场/退场/別、選擇、變量賦值/分歧/更新/代入、ＢＧＭ變更/ＳＥ開始/進入設施 等。

LLM 自动生成剧本走 `Story/AIStoryGenerator.cs`（`StartGeneration` → 后台 `GenerateTaskAsync` → `PromptBuilder.BuildDirectorPrompt`/`BuildShowPrompt` → `AIStoryAdapt` 转成 `ScriptNode[]` → `StoryEngine.StartEvent`）。

## AgentBrain 行为队列 — `AI/`

每个 NPC 一个 `AgentBrain`（按 `Agent.Index` 存于 `AgentAIController`），用 `IAtomicAction` 队列做行为链。

**加一个新 NPC 行为的正确姿势**：

```csharp
// 1. 实现接口（放进 AI/Actions/AtomicAction.cs）
public interface IAtomicAction {
    void OnStart(Agent agent);
    void OnTick(Agent agent, float dt);
    bool IsFinished(Agent agent);
    void OnEnd(Agent agent);
}

// 2. 触发行为 — ⚠️ 外部代码只能通过事件投递，禁止直接操作 Brain
AgentAIController.Instance.SendEventToAgent(target, "事件名", args);
// AgentBrain.ReceiveEvent 内部自行管理 EnqueueAction / ClearAllActions / Suspend / Resume
// EnqueueAction、ClearAllActions 均为 private，外部不可调用
```

- **已有的 Action（先复用，别重写）**：`FollowAgentAction`、`MoveToPositionAction`、`LookAtAction`、`TurnToDirectionAction`、`PlayAnimAction`、`FightEnemyAction`、`DrawWeaponAction`、`StayAction`、`ForceTalkAction`、`PrepareOpeningAction`、`ReactionDecisionAction`、`FleeFromAction`（儿童恐惧逃离，见下方专节）。
- **什么才该放进原子 Action 库**：只有**高可复用**（多种行为链都会用到，如移动、朝向、播放动画）或**不可再拆分**（最小行为单元，拆了就没意义）的行为，才进 `AtomicAction.cs`。一次性的、只服务某个具体玩法的复合流程**不要**塞进来——那应该是「多个原子 Action 入队组合」。
- 复杂行为 = 多个原子 Action 入队组合，而不是写一个大 Action。

## 引擎级非战斗人员（儿童 human_child）— `AI/Actions/AtomicAction.cs` + `AI/AgentBrain.cs`

**引擎把儿童排除在 `Agent.IsHuman` 之外**（无 IsHumanoid 标志、非战斗人员设定），但玩家认知里小孩也是人：对话/警戒/感知/战斗事件必须与大人同等对待。凡原本判定 `agent.IsHuman` 且语义为「人形角色」的地方统一改用 `IsHumanOrChild`；凡「进入战斗」流程对儿童替换为恐惧逃离。

```csharp
// ① 人形判定（AgentControlHelper.IsHumanOrChild — 已接入 AgentAIController/NpcSightSystem/
//    AttackTriggerMissionLogic/InteractionMissionView/VisualCommands 全部替换点）
AgentControlHelper.IsHumanOrChild(agent);
//    = agent.IsHuman || agent.Monster?.StringId?.Contains("child") == true（null-safe）

// ② 儿童身份判定（AgentBrain.IsChildOwner — Monster StringId 含 "child" 即儿童，
//    不写死 "human_child"，兼容其他 mod 的儿童 monster 命名）
bool isChild = Owner != null && Owner.Monster != null && Owner.Monster.StringId?.Contains("child") == true;

// ③ 儿童逃离动作：远离威胁 8~14m ±45° 抖动，walk 逃跑，跑完恢复原版 AI
EnqueueAction(new FleeFromAction(threatAgent));
//   OnStart 照动物挣脱轮子（StealManager.OnAnimalStruggleFlee）：6 次随机方向取第一个 navmesh
//   有效点（V.NavMesh 版本封装），兜底直线逃离（引擎自动修正 navmesh）
//   OnTick 每 200ms 刷新 ScriptedMoveToPoint(isRun:false)（as_human_child 无 run 动画）
//   OnEnd → AgentControlHelper.ForceUnlockAgent（恢复原版 AI，不像 MoveToPositionAction 锁定进对话）
```

**儿童不参战三处替换点**（`AgentBrain.ReceiveEvent`，儿童一律 `FleeFromAction` 替代 `FightEnemyAction`）：

| 事件 | 大人行为 | 儿童行为 |
|------|---------|---------|
| `order_attack`（玩家下令攻击） | `FightEnemyAction` | `FleeFromAction` |
| `DeferredCombat`（威胁失败延迟开战） | `FightEnemyAction` | `FleeFromAction` |
| 护主参战（`event_agent_damaged` 旁观者/受害者） | `FightEnemyAction` + CombatJoin 台词 | `FleeFromAction` + 求救台词（`LWN_brain_child_flee`） |

**击晕免疫判定同样用 `Contains("child")` 而非 `== "human_child"`**（`InteractionMissionView`）：child monster 骨骼比例（臂长 0.6/眼高 1.2）与 adult 不同，`death_fall_front` 动画无法在其骨架播放，成功率强制 0（100% 免疫）。精确匹配会漏掉其他 mod 的儿童命名。

**文件位置**：`Core/AgentControlHelper.cs`（IsHumanOrChild）、`AI/AgentBrain.cs`（IsChildOwner + 三处替换）、`AI/Actions/AtomicAction.cs`（FleeFromAction）

---

## NPC 互动意图引擎 — `Interaction/Intents/`

NPC 互动菜单（求婚/招募/策反/送礼/招募入伍/寒暄…）的**注册式**引擎。每个意图 = 一个类，只声明三件事：**资格(Evaluate)、目标(Goal)、成败后果(OnInstant/OnSuccess/OnFail)**；通用机制（成功率公式、掷骰、冷却、台词、置灰）在共享层。无 LLM 时对抗意图走 C# 单次检定，有 LLM 时走谈判盘。详见 [plans/no-llm-interaction.md](../no-llm-interaction.md)。

**加一个新互动选项的正确姿势**：

```csharp
// 1. 写一个意图类（放进 Interaction/Intents/ 的某个分组文件）
public class MyIntent : IntentBase {
    public override InteractionOptionType Type => InteractionOptionType.Xxx;
    public override InteractionCategory Category => InteractionCategory.Social;
    public override string DisplayName => "【我的选项】...";
    public override NegotiationGoalType? Goal => NegotiationGoalType.Xxx; // null = 即时类(不掷骰)
    public override NegotiationTactic Tactic => NegotiationTactic.Flatter; // 对抗类用，决定查哪个属性

    public override Eligibility Evaluate(IntentContext ctx) =>      // 三态
        !ctx.IsHero ? Eligibility.Hide()
      : ctx.Relation < 0 ? Eligibility.Grey("关系不够")
      : Eligibility.Show();

    public override void OnInstant(IntentContext ctx) { /* 即时类结算 */ }
    public override void OnSuccess(IntentContext ctx) { /* 对抗类成功 */ }
    // OnFail 基类默认：掉好感 + 进冷却，通常无需 override
}

// 2. 在 IntentRegistry.RegisterDefaults() 注册一行（内容包可在自己代码里 IntentRegistry.Register）
Register(new MyIntent());
```

- **禁止**改 `RegisterAllOptions`（已删）或在 `InteractionOptionManager` 写 if-else——只 `Register`。`InteractionOptionManager.BuildOptionVMs` 是薄壳，自动把可见意图转成 `StoryOptionVM`（含成功率预览/置灰）。
- `IntentContext`（`IntentContext.Build(agent, controller)`）：开对话时一次性算好身份/关系——`IsHero/Relation/SameFaction/EnemyFaction/IsLiege/IsClanLeader/IsWanderer/IsMarried/OppositeSex/PlayerHasNoKingdom/IsMySoldier/IsEnemyAgent/IsRecruitableCivilian`，及 `OnCooldown(goal)/CooldownDaysLeft(goal)`。资格判定直接读这些，别在意图里重复取数。
- **单次检定公式** `SingleRollResolver.Compute(ctx, goal, tactic, offerValue)`：复用 `NegotiationState`(难度/开局/性格Trait) + `SkillCheckSystem.CalculateSkillCheck`(技能胜率) + `NegotiationRegistry.CalculateMultiplier`(性格倍率)，**不要新造第4套公式**。
- **失败冷却** `IntentCooldownStore.IsOnCooldown/Set/DaysLeft`（per NPC+目标，经 `MyBehavior.SyncData` 跨存档）。
- **模板台词** `DialogueTemplateHelper.Get(...)`：查 CSV（`ModuleData/DesignData/Dialogue.csv`，ID=`{Goal}_{Success/Fail}`），世界观占位符走 `Settings.Instance`，空表自动兜底。
- **已有意图（先复用，别重写）**：求婚/送礼/茶席/切磋（Social）、登庸/劝诱倒戈/策反/要军资/仕官（Diplomacy）、情报/命令/跟随/寒暄/离开（General）、应募入伍（RecruitSoldier，普通平民花钱招文化基础兵+魅力砍价）。
- **结算后果统一走 `ActionHandler`**（LLM 路径也用），别在意图里裸调单边 API；金钱进出走 `AgentControlHelper`。

---

# 🔴 对话 Intent 的场景判别铁律 — 真场景 vs 大地图临时对话 Mission

**解决什么问题**：对话 Intent 的 `OnInstant/OnSuccess/OnFail` 常按「在不在 Mission 里」分流后果（当场开打 vs 召唤复仇队、现场价 vs 远程价、FadeOut vs 直接消失）。但 `ctx.IsInMission`（= `Mission.Current != null`）**区分不了真场景和大地图的临时对话 Mission**——后者也是真 Mission，只是光秃秃的对话场景。写任何对话 Intent 必须先想清楚后果发生在三种场合的哪一种：

| 场合 | `Mission.Current` | `ConversationMissionLogic` 行为 | 特征 |
|------|------|:---:|------|
| ① 真场景 Mission（村庄/城镇/酒馆漫游） | 非 null | **无**（城镇中心 27 个行为里没有它） | 周围有村民/守卫/道具，能场景内开打、围堵、围观 |
| ② 大地图临时对话 Mission | 非 null | **有** | `OpenConversationMission` 开的对话场景（仅 5 个行为），只有对话双方，没有"村子" |
| ③ 纯大地图对话（inquiry，无 Mission） | null | — | 无 Agent 层，只能动 Campaign 层状态 |

**判别范式（已封装在 IntentContext）**：

```csharp
// ✅ 后果依赖「周围有其他 NPC / 场景道具」（开打、围堵、广播 order_attack、叫守卫、现场价）：
if (ctx.InRealScene) { ... }   // = IsInMission && !IsTempConversationMission
//    在 ② 里漏掉这个排除 → 对着空场景广播，叙事和机制双出戏

// ✅ 后果只作用在对话对象自己身上（FadeOut、TakePrisoner）：
//    ctx.Agent != null 即可，不用排 ②（临时 Mission 里 Agent 真实存在）
```

**`IsTempConversationMission` 是原生判别**（反编译核实）：临时对话 Mission 的行为列表带 `ConversationMissionLogic`，真场景 Mission 没有；引擎随 Mission 生灭维护，无静态标志泄漏风险，且**原版地图对话同样覆盖**（不限于本 mod 的遭遇管线）。

```csharp
// IntentContext 构造时一次性算好（IntentContext.cs）：
IsTempConversationMission =
    Mission.Current?.GetMissionBehavior<ConversationMissionLogic>() != null;  // SandBox.Conversation.MissionLogics
```

⚠️ `MapEncounterDialogState.Active` **不是**场景判别器，别再用它干这个——它只是「本 mod 遭遇对话管线」的闸门（抑制原版 ConversationMissionLogic tick、定位 Partner），原版地图对话时它是 false，但场合②的语义依然成立。

**新 Intent 自查**：
1. 这个 Intent 的后果依赖「周围有其他 NPC/场景」吗？→ 是则条件必须用 `ctx.InRealScene`，禁止裸写 `ctx.IsInMission`
2. 在 ② 里的替代后果形态是什么？（范本：拔剑 → 真场景当场开打 / ②③ 召唤大地图复仇队，**二者只取其一**，不叠加）
3. 玩家可见文案（DisplayMessage/Inquiry）在三种场合下分别读得通吗？（"围了过来"在 ② 里出戏）

**落地范本**：`FightVillagersIntent.OnInstant`（AccountabilityIntents.cs）——`ctx.InRealScene` 分流场景内开打 vs 召唤复仇队。

**存量审计（2026-07-28 已完成）**：全部 `ctx.IsInMission` / `Mission.Current != null` 用点已逐一过堂——🔴 真修 2 处（PayRestitution 现场价 ②中错算 2 倍、CrimeDialogueBuilder 威胁失败"来人！"②中没人可来）；🟡 自文档化 8 处（Alert 质问类/WalkAway 围堵挣脱/Comply/CombatSurrender，②中本不可达，统一改 `ctx.InRealScene`）；✅ 保留 1 处（LureArrest FadeOut 只动对话对象自己，②中正确）。新代码一律用 `ctx.InRealScene`，别再引入裸 `ctx.IsInMission` 场景判断。

**文件位置**：`Interaction/Intents/IntentContext.cs`（`IsInMission` / `IsTempConversationMission` / `InRealScene`）、`Interaction/Intents/AccountabilityIntents.cs`（FightVillagersIntent 范本）、`Interaction/Dialogue/MapEncounterDialogState.cs`（管线闸门，非判别器）

---

# Campaign Action 类 — 官方游戏状态变更 API

**所有对游戏世界产生影响的"动作"都应以 `*Action` 静态类为入口。** 这些是 TaleWorlds 官方的封装层，内部处理了事件广播、日志、校验、连锁反应。禁止绕过它们直接操作底层数据结构。

查找规则：需要做什么 → `ilspycmd TaleWorlds.CampaignSystem.dll | grep "class.*Action"` → 找到对应的类 → `ilspycmd -t` 看签名。

全部 60 个 Action 类位于 `TaleWorlds.CampaignSystem`（通过 `.csproj` 引用 `TaleWorlds.CampaignSystem.dll` 即可使用）：

| 类别 | Action 类 | 用途 |
|------|----------|------|
| **Party AI** | `SetPartyAiAction` | 🆕 命令 party 执行特定行为（巡逻/劫掠/围城/追击/护送/拜访） |
| **Hero 生死/状态** | `KillCharacterAction`, `MakeHeroFugitiveAction`, `DisableHeroAction`, `EndCaptivityAction` | 杀角色、设为逃犯、禁用、结束囚禁 |
| **Hero 移动/归属** | `TeleportHeroAction`, `AddHeroToPartyAction`, `RemoveCompanionAction`, `AddCompanionAction`, `AdoptHeroAction`, `TakePrisonerAction`, `TransferPrisonerAction` | 传送英雄、加入部队、移除同伴、收养、俘虏 |
| **关系/婚姻** | `ChangeRelationAction`, `ChangeRomanticStateAction`, `MarriageAction`, `MakePregnantAction`, `ApplyHeirSelectionAction` | 改关系、改恋爱状态、结婚、怀孕 |
| **声望/犯罪** | `GainRenownAction`, `ChangeCrimeRatingAction`, `PayForCrimeAction` | 加声望、改犯罪等级、付赎金 |
| **经济** | `GiveGoldAction`, `GiveItemAction`, `SellGoodsForTradeAction`, `SellItemsAction`, `SellPrisonersAction`, `BribeGuardsAction` | 给钱、给物品、交易、贿赂 |
| **定居点** | `EnterSettlementAction`, `LeaveSettlementAction`, `ChangeOwnerOfSettlementAction`, `ClaimSettlementAction`, `ChangeVillageStateAction`, `ChangeGovernorAction`, `IncreaseSettlementHealthAction`, `LeaveTroopsToSettlementAction` | 进出定居点、改所有权、改村庄状态、改总督 |
| **工坊** | `ChangeOwnerOfWorkshopAction`, `ChangeProductionTypeOfWorkshopAction`, `InitializeWorkshopAction` | 工坊所有权/类型 |
| **战争/外交** | `DeclareWarAction`, `MakePeaceAction`, `BeHostileAction`, `StartBattleAction`, `LiftSiegeAction`, `SiegeAftermathAction`, `BreakInOutBesiegedSettlementAction` | 宣战/和平/敌对/开战/围城 |
| **军团** | `GatherArmyAction`, `DisbandArmyAction` | 集结/解散军团 |
| **家族/王国** | `ChangeClanLeaderAction`, `ChangeClanInfluenceAction`, `ChangeKingdomAction`, `GainKingdomInfluenceAction`, `DestroyClanAction`, `DestroyKingdomAction` | 改族长/影响力/王国、摧毁家族/王国 |
| **Party** | `DestroyPartyAction`, `DisbandPartyAction`, `MergePartiesAction` | 摧毁/解散/合并部队 |

**⚠️ 铁律 4 的补充**：`GiveGoldAction.ApplyBetweenCharacters` 是官方 API，但**仍要通过 `AgentControlHelper.TransferGold` 调用**（确保守恒/日志/世界观参数化）。同样 `GiveItemAction` → `AgentControlHelper.TransferItems`。Action 类是我们写 helper 时的参考，不是绕过的借口。

## SetPartyAiAction — Party AI 控制

**不用再裸调 `SetMoveGoToSettlement` + 猜测 `SetDoNotMakeNewDecisions` 了。** 用原生 Action，全部搭配 `SetDoNotMakeNewDecisions(true)` 锁死。

```csharp
// 行军（泛用，不区分敌友）
party.Ai.SetMoveGoToSettlement(targetSettlement);
party.Ai.SetDoNotMakeNewDecisions(true);

// 巡逻（已到达后围城阶段）
SetPartyAiAction.GetActionForPatrollingAroundSettlement(party, settlement);
party.Ai.SetDoNotMakeNewDecisions(true);

// 🔥 攻击：按定居点类型选择
if (settlement.IsVillage)
    SetPartyAiAction.GetActionForRaidingSettlement(party, settlement);     // 劫掠村庄
else if (settlement.IsFortification)
    SetPartyAiAction.GetActionForBesiegingSettlement(party, settlement);   // 围攻城堡/城镇
party.Ai.SetDoNotMakeNewDecisions(true);

// 追击指定部队
SetPartyAiAction.GetActionForEngagingParty(party, targetParty);
party.Ai.SetDoNotMakeNewDecisions(true);

// 其他可用：GetActionForDefendingSettlement / GetActionForEscortingParty / GetActionForGoingAroundParty / GetActionForVisitingSettlement
```

**发现方式**：`campaign.ai_raid_village` / `campaign.ai_siege_settlement` 等控制台指令 → `ilspycmd | grep` → 找到 `SetPartyAiAction`。

**文件位置**：`TaleWorlds.CampaignSystem.dll` → `SetPartyAiAction`（全局命名空间，using TaleWorlds.CampaignSystem 即可）。

## Agent 脚本化移动 — `SetScriptedPosition`（含 agent.goto）

**`agent.goto` 控制台指令**的底层实现。我们已封装在 `AgentControlHelper.MoveTo` 里，不要再裸调。

```csharp
// ✅ 走封装（自动寻路 + 等待）
await AgentControlHelper.MoveTo(agent, targetVec, targetDir, stopDistance = 0.5f);

// ❌ 禁止裸调
agent.SetScriptedPosition(ref pos, ...);  // 绕过寻路，不处理 AI 状态
agent.SetScriptedPositionAndDirection(...);
```

**控制台对照**：`agent.goto [AgentIndex] [X] [Y] [Z]` → 内部调 `MBAPI.IMBAgent.SetScriptedPosition`。C# 层只能看到函数签名，实现是 native C++。

**文件位置**：`Core/AgentControlHelper.cs`（已封装），底层 `TaleWorlds.MountAndBlade.dll` → `Agent.SetScriptedPosition`。

---

| 需求 | 继承的基类 | 范本文件 |
|------|-----------|---------|
| 战斗内每帧逻辑 / 监听 Agent 生灭 | `MissionLogic` | `AI/AgentAIController.cs` |
| 战斗内 UI 图层（Gauntlet） | `MissionView` | `Interaction/InteractionMissionView.cs` |
| UI 数据绑定 | `ViewModel` | `Interaction/StoryDialogVM.cs` |
| 大地图事件 / 存档 | `CampaignBehaviorBase` | `Core/MyBehavior.cs`、`Story/StoryContext.cs` |
| 自定义可存档类型 | `SaveableTypeDefiner` | `Story/StoryContext.cs`（SaveDefiner） |

存档：字段加 `[SaveableField(n)]`，`CampaignBehaviorBase.SyncData(IDataStore)` 里 `dataStore.SyncData("key", ref field)`，自定义类型在 `SaveDefiner` 注册。

## 战斗回调职责划分

引擎两个 hit 回调语义不同，**不要都往里塞**：

| 回调 | 触发条件 | 职责 |
|------|----------|------|
| `MissionLogic.OnRegisterBlow` | 攻击判定注册（伤害为 0 也触发，和平区域也触发） | **攻击意图检测**：广播事件、触发敌对、开战信号 |
| `MissionLogic.OnAgentHit` | 实际造成伤害时（伤害 > 0） | **伤害处理**：切磋虚拟血量、死亡收集、伤害统计 |

- 和平城镇挥刀 → `OnRegisterBlow` 点火，`OnAgentHit` 不点火（引擎拦截了伤害）
- **Team 切换不要在手写回调里做**，交给 `FightEnemyAction` → `CombatManager.StartFight` 管道处理
- 见 `Combat/AttackTriggerMissionLogic.cs` 为实际落地案例

## 大世界地图对话 → 真对话 Mission 接入

**咽喉补丁 `CampaignMapConversation.OpenConversation` + inquiry 分流 + 真 Mission + 自定义对话管线复用。**

覆盖场景：玩家在大世界沙盘遇到中立/未开战部队时，弹 inquiry 让玩家选「原版对话 / 新版对话」，选新版则开真对话 Mission（真实 Agent + MissionScreen），自动触发本 mod 的 `InteractionMissionView` 自定义对话管线，零重构复用现有 Agent 演出/镜头/意图引擎。

```csharp
// 1. 设置静态标志（在 inquiry 回调里，开 mission 前）
MapEncounterDialogState.Active = true;
MapEncounterDialogState.Partner = conversationPartnerData.Character;
CampaignMission.OpenConversationMission(p, q);   // 开真对话 mission

// 2. Harmony 拦截咽喉（自动生效，PatchAll 注册）
[HarmonyPatch(typeof(CampaignMapConversation), nameof(CampaignMapConversation.OpenConversation))]
public static class ConversationEntryPatch
{
    [HarmonyPrefix]  // 大地图遇敌 → 弹 inquiry 分流
    [HarmonyPostfix] // 定居点对话 → 犯罪事件注入
}

// 3. Harmony 抑制原版 ConversationMissionLogic.OnMissionTick（仅对我们的 mission）
[HarmonyPatch(typeof(ConversationMissionLogic), "OnMissionTick")]
public static class SuppressVanillaConversationMissionPatch
{
    [HarmonyPrefix]
    public static bool Prefix() => !MapEncounterDialogState.Active; // Active → 跳过原版 tick
}

// 4. InteractionMissionView 自动触发 + 收尾（已在 OnMissionTick/OnDialogueEnded/Finalize 中集成）
//    - OnMissionTick：检测 Active → 按 Partner CharacterObject 在 Mission.Current.Agents 中精确定位 partner Agent
//    - StartFreeConversationFlow(partnerAgent)：复用现有对话管线（VM/控制器/镜头/意图引擎）
//    - OnDialogueEnded：MapEventHelper.OnConversationEnd() → Mission.Current.EndMission() → 回大地图
//    - OnMissionScreenFinalize：安全清标志（防 ESC 退出泄漏）
```

**关键文件**：`Interaction/Dialogue/MapEncounterDialogState.cs`（静态标志）、`Interaction/Dialogue/ConversationEntryPatch.cs`（对话入口统一拦截 + 犯罪对话注入 + 原版 tick 抑制）、`Interaction/InteractionMissionView.cs`（自动触发/收尾）。

**边界**：只对 Hero 生效（无 Hero 放行原版）；仅自家的 conversation mission 抑制（静态 gate）；settlement 内点 NPC / 请求会面不受影响；LLM 路径走 `IsLLMReady` 总闸。

---

# 对话中标记 → EndConversation 延迟处理 — `Interaction/Dialogue/ConversationEntryPatch.cs`

**Intent 在对话中途（OnSuccess/OnInstant）触发了 Mission 层副作用（Agent FadeOut / 战斗 / 关押），但副作用如果在对话窗口关闭前执行会导致视觉异常（NPC 一边说话一边消失、战斗覆盖对话 UI）。解决方案：Intent 只设静态标记，副作用延迟到 `ConversationManager.EndConversation` Postfix 统一执行。**

## 已接入的延迟操作

| 标记字段 | 设置位置 | EndConversation 消费 |
|----------|---------|---------------------|
| `WalkAwayIntent.PendingInquiryTitle/Body` | `WalkAwayIntent.OnInstant` | `InformationManager.ShowInquiry` 弹窗 |
| `AlertForceConversationAction.PendingAlertScript/Label` | Alert 注入流程 | 清理残留 |
| `AlertForceConversationAction.ActiveConversationAgent` | Alert 注入流程 | `BroadcastEvent("EndInteraction")` 释放 NPC |
| `ThreatIntent.PendingCombatAgent` | `ThreatIntent.OnFail` | `SendEventToAgent("DeferredCombat")` 延迟开战 |
| `SurrenderJailIntent.PendingJailExit` | `SurrenderJailIntent.OnSuccess` | `TakePrisonerAction.Apply(settlement.Party, Hero.MainHero)` 坐牢 |
| `LureArrestIntent.PendingFadeAgent` | `LureArrestIntent.OnSuccess` | `Agent.FadeOut(false, true)` 淡出消失 |

## 模式模板

```csharp
// 1. Intent 侧 — 设标记（不直接执行 Mission 层操作）
public class MyIntent : IntentBase
{
    public static Agent PendingFadeAgent; // 或其它待消费的状态

    public override void OnSuccess(IntentContext ctx)
    {
        // Campaign 层操作可以立即执行（与 Mission 视觉无关）
        TakePrisonerAction.Apply(...);
        InformationManager.DisplayMessage(...);

        // Mission 层副作用 → 只设标记，不立即执行
        if (ctx.IsInMission && ctx.Agent != null)
            PendingFadeAgent = ctx.Agent;
    }
}

// 2. ConversationEntryPatch.ResetCrimeDialogueOnConversationEndPatch.Postfix — 消费标记
[HarmonyPatch(typeof(ConversationManager), nameof(ConversationManager.EndConversation))]
public static class ResetCrimeDialogueOnConversationEndPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        // ... 其它清理 ...

        var fadeAgent = MyIntent.PendingFadeAgent;
        if (fadeAgent != null)
        {
            MyIntent.PendingFadeAgent = null;
            try
            {
                if (fadeAgent.IsActive())
                    fadeAgent.FadeOut(false, true);
            }
            catch (Exception ex) { DebugLogger.Log($"[ConvEnd] FadeOut failed: {ex.Message}"); }
        }
    }
}
```

## 为什么不能直接在 Intent 里 FadeOut

`ExecuteIntentAction` → `OnSuccess` 在 `AddPlayerLine` 的 `onConsequence` 回调中执行，早于对话引擎推进到下一句。如果此时 FadeOut，NPC Agent 会在一句台词还没说完时就消失——视觉出戏。延后到 `EndConversation` 则确保所有对话文本播放完毕、窗口关闭后 Agent 才淡出。

**关键文件**：`Interaction/Dialogue/ConversationEntryPatch.cs`（EndConversation Patch + 所有延迟消费）、`Interaction/Intents/AccountabilityIntents.cs`（LureArrestIntent / ThreatIntent / WalkAwayIntent 等标记字段）。

---

# 世界事件引擎 — `WorldEvent/`

## 架构

四层：**模拟器**（DailyTick 生成事件 + party）→ **数据库**（Event CRUD + JSON 持久化）→ **导演**（五种推送控制可见性）+ **通知控制器**（NinjaReport → Inquiry 书信）→ **宿敌追踪**（交手记录 → 伤疤 → 复仇）。

## 核心入口

```csharp
// 生成管线（WorldEventSimulator.TryGenerateNewEvent）
// ① 动机驱动真人冲突（优先！）
TryGenerateMotivatedEvent()  // 扫 Hero 关系/仇恨/性格 → 真人冲突（NobleConflict/Betrayal/Assassination…）
// ② 回落随机事件
roll → 选类型 → 选定居点 → 选人 → SpawnEventParty → 存入 DB

// 动机扫描四层：跨clan仇恨 → 同clan内斗 → 经济冲突 → 野心扩张

// 征用真人部队（WorldEventSimulator.SpawnEventParty）
// instigator 正带队 → 调遣真实部队（LeaderHero、Scouting=300加速、不改位置）
// instigator 在定居点 → 新建 party（定位目标附近）
// 到场才触发后果：CheckEventPartyArrivals → dist < 3 单位 → ApplyExpiryConsequences

// 控制台调试
custom.worldevent_list       // 列出所有活跃事件
custom.worldevent_force [类型] [严重度]  // 强制生成（默认BanditRaid）
custom.worldevent_status     // 内部状态
```

## 给世界事件加新婚事件的正确姿势

1. 在 `WorldEventType` 枚举加类型
2. 在 `WorldEventConfig` 静态构造里 `Register(new WorldEventConfig{...})`
3. 在 `WorldEventDirector` / `WorldEventNotificationController` 的 switch 里加对应文本
4. 在 `Narrative.csv` 加 `WorldEvent_Greeting_{Type}_Victim` / `Instigator` 条目

---

# 案情文案事实派生层 — `WorldEvent/WorldEvent.cs`

**解决什么问题**：犯罪事件的玩家可见文案曾按 EventType 静态模板（`Config.CrimeVerb*`）硬套——但 PendingWorldEvent 永远是 Misconduct 万用容器类型，模板描述不了"击晕+搜刮"这类复合罪行（曾把击晕搜刮报成"偷牲口"，量词写死"只/牲口"，gold 还不计赔偿估值）。现在**一切案情文本从记账事实派生**：新犯罪玩法只要把事实记进 `WitnessTestimonies` + `AssaultVictimNames`，通知/Quest/对话/传闻文案自动如实还原，不用改任何消费点。

## 事实派生 API（WorldEvent 上，一处生成处处消费）

```csharp
// 案情事实句（村民视角，不知是谁干的）——发现通知/Issue/Quest/传闻/对话统一入口
evt.BuildDiscoveryFacts();
//   袭击+失窃 → "帝国农民被人打晕了，还少了一件扣带束腰衣、一件扎带皮靴等4项财物"
//   仅袭击   → "帝国农民被人打晕了"（多人 → "有3人被人打晕了"）
//   仅失窃   → "少了一只羊"
//   都无     → 回落 Config.CrimeVerbPast

evt.CaseLabel;    // 案件定性标签：刑案(伤人+失窃)/伤人案/失窃案/案件 —— 标题/简述用
evt.HasAssault;   // 是否有击晕/袭击记账

// 赃物描述（量词分类：牲畜→只、装备/货物→件、金→"N第纳尔"；3+ 混合尾巴：纯牲畜"等N只牲口"，否则"等N项财物"）
evt.BuildStolenItemsDescription();
evt.TotalStolenCount;  // 赃物总项数（金只算 1 项——悬赏按件定价用）
evt.TotalStolenValue;  // 赃物总市值（物品市值 + 金按面值计入——赔偿估值用）
```

## 记账侧：ActionRecord.Count

gold 面额必须走 `Count` 字段（不再只嵌在 ItemName 字符串里）；普通物品默认 1；旧存档 Count=0 → 聚合按 1 兜底（序列化兼容）。

```csharp
AgentAIController.Instance?.RegisterUnwitnessedTheft("gold", $"{actual} 第纳尔", targetName, count: actual);
AgentAIController.Instance?.RegisterTheftWitnesses(heroIds, templates, itemId, itemName, targetName, count);
```

## 消费点清单（新案情叙事禁止绕过）

| 消费方 | 用法 |
|--------|------|
| `WorldEventNotificationController.OnCrimeDiscovered` | `e.BuildDiscoveryFacts()` |
| `CommissionQuest.OnStartInvestigation` | `evt.BuildDiscoveryFacts()` |
| `CommissionData.GetFlavorDescription` / `CommissionHubIssue` Title/Brief | `evt.CaseLabel` |
| `CommissionHubIssue` Description | context 预取 `DiscoveryFacts` + `AuthorityRole` |
| `SocialEventManager.BuildSocialEventDescription` | 犯罪类走 `BuildDiscoveryFacts()` |
| `ConfessIntent` 自首日志 | `evt.BuildDiscoveryFacts()` |
| `PlaceholderResolver` | `{DiscoveryFacts}` / `{StolenItemDesc}`（委托统一实现，禁止本地重写量词逻辑） |
| `CrimeDialogueBuilder` 模板 | 用 `{DiscoveryFacts}`，不再 `{CrimeScene}{CrimeVerbPast}{StolenItemClause}` 三段拼接 |

**铁律**：①禁止按 `evt.Type` / `Config.CrimeVerb*` 拼案情文案；②赔偿对话的损失描述走 `BuildLossDescription()`（赃物市值+袭击身价合并成句）；③新占位符走两步流程（`PlaceholderResolver.ResolveOne` 加 case + 模板引用），且 `ResolveOne` 返回 null 会原样输出 `{KEY}`。

**文件位置**：`WorldEvent/WorldEvent.cs`（事实派生 API）、`AI/AlertTypes.cs`（`ActionRecord.Count`）、`AI/AgentAIController.cs`（记账 count 参数）。

---

# AgentHUD — 3D 角色头上通用 HUD 系统

**原地升级替换旧 `BubbleSay*` 系统**。为所有 Human Agent 提供统一的 3D 头上 HUD，管理五大元素：名字、说话冒泡、血条、伤害数字、警戒眼睛。

## 核心入口

```csharp
// 获得单例（MissionView）
AgentHudMissionView.Instance

// 让 Agent 说话（原 BubbleSayMissionView.AgentBubbleSay）
AgentHudMissionView.AgentSay(agent, text);         // 静态快捷方法
AgentHudMissionView.AgentSay(stringId, text);

// 确保 Agent 有 HUD（延迟创建策略：有内容要显示才创建 VM）
AgentHudMissionView.Instance.EnsureHud(agent);

// 控制台
custom.agentHud_say <agentStringId> <text>
```

**文件位置**：`AgentHUD/AgentHudMissionView.cs`（MissionView）、`AgentHUD/AgentHudVM.cs`（VM）、`AgentHUD/AgentHudCollectionVM.cs`（MBBindingList 容器）、`GUI/Prefabs/AgentHudNearby.xml`（Gauntlet Prefab）。

## 五大元素与显隐规则

| 元素 | VM 属性 | 显隐条件 | 持续时间 | FOV |
|------|---------|----------|----------|:---:|
| **名字** | `ShowName` + `AgentName` | ShowSpeech \|\| ShowHealth \|\| ShowDamage | 跟随触发元素 | ✅ |
| **说话** | `ShowSpeech` + `SpeechText` | `Speak(text)` 调用 | `4s + text.Length * 0.1s` | ✅ |
| **血条** | `ShowHealth` + `CurrentHealthWidth` | 战斗中/血量<95%/戒备（`CurrentWatchState` Alarmed\|Cautious，敌意驱动，不看持械） | 持续（条件消失隐藏） | ✅ |
| **伤害** | `ShowDamage` + `DamageText` | 受伤害瞬间 | 2s | ✅ |
| **警戒** | `ShowAlert` + `AlertFillHeight/EyeBgColor/EyeFillColor` | 警戒值 > 0 | 持续（归零隐藏） | ❌ **豁免** |

**警戒 FOV 豁免**：警戒眼睛不受 FOV 角度限制——NPC 在玩家身后盯你，更该知道。屏幕外时 clamp 到边缘做方向指示。名字只在 FOV 内显示（ShowAlert 不触发名字），玩家转身面对 NPC 后名字浮现。

**名字总领规则**：`ShowName = ShowSpeech || ShowHealth || ShowDamage`（不含 ShowAlert）。

**容器可见性**：`IsVisible = ShowName || ShowAlert`（警戒眼睛可独立触发容器显示）。

## 性能：距离分级

| 距离 | 范围 | 更新频率 | 做什么 |
|------|------|----------|--------|
| **近** | ≤ 15m | 每 10 帧 | 完整：血条 + 警戒值 + 说话 + 坐标 |
| **中** | 15m ~ 50m | 每 30 帧 | 仅警戒值 + 坐标 |
| **远** | > 50m | 不处理 | 不创建 HUD / 隐藏 |

**延迟创建**：不是一开始就给所有 Agent 创建 HUD，而是按需创建（有警戒值/说话/战斗 → 创建）。

## AgentHudVM 关键属性

```csharp
// 注入数据（由 MissionView 调用）
hud.AlertValue = NpcSightSystem.GetAlertValue(agent);  // 警戒值 0~2+，每帧注入
hud.UpdateLogic();   // 低频：血量/血条条件/名字总领（近距10帧/中距30帧）
hud.UpdateFrame(dt); // 高频：动画插值 + 计时器（每帧）
hud.Speak(text);     // 说话入口

// 可绑定属性（DataSourceProperty）
PosX, PosY, Scale, IsVisible, BubbleWidth, BubbleHeight,
AgentName, ShowName,
SpeechText, ShowSpeech,
CurrentHealthWidth, ShowHealth,
DamageText, ShowDamage,
AlertFillHeight, ShowAlert, EyeBgColor, EyeFillColor
```

## 警戒值系统（NpcSightSystem 维护）

```csharp
// 查询/操作
float val = NpcSightSystem.GetAlertValue(npc);  // 不存在返回 0
NpcSightSystem.AddAlertPulse(npc, amount);       // 一次性脉冲（不走 dt）

// 内部计算（OnMissionTick 中每秒触发）：
// 能看到玩家 → dt * (IdentityValue + ActionSuspiciousValue)
// 看不到玩家 → dt * (-DecayRate)
// IdentityValue: 0.15 (敌) / 0 (其他)
// ActionSuspiciousValue: 0.15 (蹲下) / 0 (正常)
// DecayRate: 0.15/s
// 脉冲事件: +2.0 (击晕/偷窃/攻击友军)
```

**文件位置**：`AI/NpcSightSystem.cs`（`_alertValues` 字典 + `GetAlertValue`/`AddAlertPulse`/`UpdateAlertValue`/`CleanupDeadAlertEntries`）。

# UI 交互模式

## 原生弹窗面板构造（Inquiry 同款）— canvas + frame_9 Extend + 标题带

**任何自定义面板想要原生弹窗观感，照抄 Inquiry（`Native/GUI/Prefabs/Information/Inquiries/SingleQueryPopup.xml`）的三层构造**：

```xml
<Widget SuggestedWidth="760" SuggestedHeight="280" ...>   <!-- 主面板本身无 Sprite！ -->
  <Children>
    <!-- ① 底图：StdAssets\Popup\canvas（亮羊皮纸 512×645）或 canvas_dark（深色 699×666），平纹拉伸无形变 -->
    <Widget WidthSizePolicy="StretchToParent" HeightSizePolicy="StretchToParent" Sprite="StdAssets\Popup\canvas_dark" ... />
    <!-- ② 内容：标题带（46px 深色方块 #000000B3 + Popup.Title.Text 笔刷金字，实例覆盖 TextHorizontalAlignment/FontSize）
              + 分隔线 StdAssets\Popup\divider + 正文... -->
    <!-- ③ 边框：frame_9 九宫格（27px 边），Extend 18 画在逻辑盒外 18px，放最后=最上层压边 -->
    <Widget WidthSizePolicy="StretchToParent" HeightSizePolicy="StretchToParent"
            Sprite="frame_9" ExtendLeft="18" ExtendTop="18" ExtendRight="18" ExtendBottom="18" IsEnabled="false" ... />
  </Children>
</Widget>
```

**为什么 Extend 18 是关键**：边框画在逻辑盒**外** 18px，内容对齐逻辑盒就永远凸不出边框（边框反而压内容 9px）——原生弹窗的层次感和"内容不凸出"都是这么来的。

**⚠️ 反面教材（踩过的坑）**：`SPGeneral\OverlayPopup\portrait_slot` 是 **145×119 头像框**、非九宫格，当 760×280 面板底图被 ×5.2/×2.35 不等比拉伸，且贴图可见边框内缩（透明 padding 被放大 5 倍）→ 子元素全部凸出"面板"。**选 sprite 前查 `Native/GUI/NativeSpriteData.xml` 确认原生尺寸和是否 NineRegionSprite**——名字带 `_9` 的才是九宫格可安全拉伸。

## Gauntlet 行内多色富文本 — RichTextWidget + `<span style>` + 笔刷命名 Style

**一句话内分段变色**（如【完美】绿 /【普通】黄 /【失败】红同排显示）。反编译确认的机制链：RichText 支持 `<img src>` / `<a style>` / `<span style="X">` 三种标签 → span 的 style 名推入 `_styleStack` → 渲染时 `Brush.GetStyleOrDefault(part.Style)` 解析到**该 widget 笔刷的命名 Style**（找不到回落 Default；Style 未指定的属性也回落 Default）。

```xml
<!-- ① 笔刷：Default 定基底，命名 Style 只覆盖差异属性（FontColor） -->
<Brush Name="StealBar.RuleText" Font="FiraSansExtraCondensed-Regular" TextHorizontalAlignment="Center">
    <Styles>
        <Style Name="Default" FontColor="#BBBBBBFF" FontSize="15" ... />
        <Style Name="Perfect" FontColor="#55CC55FF" />
        <Style Name="Normal"  FontColor="#E8C55AFF" />
        <Style Name="Fail"    FontColor="#E06055FF" />
    </Styles>
</Brush>

<!-- ② Prefab：RichTextWidget + 该笔刷 -->
<RichTextWidget Text="@RuleText" Brush="StealBar.RuleText" ... />
```

```csharp
// ③ VM 字符串内嵌 span（绑定值运行时解析标签，与百科全书链接 <a style="Link"> 同机制）
RuleText = "<span style=\"Perfect\">【完美】绿区偷窃。</span><span style=\"Normal\">【普通】黄区偷窃。</span>";
```

**关键文件**：`GUI/Brushes/MyBrush.xml`（StealBar.RuleText 为范本）、`GUI/Prefabs/StealBar.xml`、`Stealth/StealBarVM.cs`。

## NinjaNotification → Inquiry 书信流

**一切重要通知的标准流**：右侧悬浮环（hover 一行摘要）→ 点击弹 Inquiry 书信（详情 + 双按钮）。

```csharp
// 不要直接往 NinjaNotification 塞长文本！走这个模式：
string shortSummary = "⚠ 雷别莱特村 · 匪患";  // 一行，hover 显示
string fullBody = "德瑟特·哈米尔正带人劫掠…";   // 详情，Inquiry 显示

NinjaNotificationManager.Show(shortSummary, () =>
{
    InformationManager.ShowInquiry(new InquiryData(
        "标题", fullBody,
        hasOk, hasCancel, "去看看", "知道了",
        onOk, onCancel));
});
```

**关键文件**：`Notify/NinjaNotificationMissionView.cs`（管理器）、`Notify/NinjaNotificationVM.cs`（VM）、`GUI/Prefabs/CustomNotify.xml`（Prefab）。

## KCD2 式轮次对话

**所有 NPC 交互统一流程**：NPC 先说开场白（右侧无选项）→ 玩家点"继续" → 选项出现。

```csharp
// StartInteraction 模式：
_vm.Show(name, openingLine);       // NPC 说话
_vm.AreOptionsVisible = false;      // 隐藏选项

_vm.OnClickContinue = () =>         // 玩家点"继续"
{
    RefreshInitialOptions();         // 选项出现
};
```

**关键文件**：`Interaction/StoryDialogVM.cs`（`OnClickContinue` 回调 + `ShowContinueHint` 属性）、`Interaction/InteractionController.cs`（`StartInteraction`）。

---

# 屏幕上方快速提示 — `MBInformationManager.AddQuickInformation`

**静态方法，屏幕上方弹出简短提示，几秒后自动消失（toast 风格）。与 `InformationManager.DisplayMessage`（左下角消息日志，持久保留）是不同显示位置。**

```csharp
// 静态方法，直接调
MBInformationManager.AddQuickInformation(TextObject text);

// 典型用法：任务进度、检定结果、系统瞬间通知
MBInformationManager.AddQuickInformation(new TextObject("{=...}你已消灭 {COUNT}/{TOTAL} 队匪徒")
    .SetTextVariable("COUNT", 3)
    .SetTextVariable("TOTAL", 5));

// 简单文本
MBInformationManager.AddQuickInformation(new TextObject("潜行检定成功"));

// 和 DisplayMessage 的区别：
//   AddQuickInformation → 屏幕上方，短暂弹出，自动消失（类似成就弹出）
//   DisplayMessage      → 左下角消息日志，持久保留，可翻阅
```

**DLL**: `TaleWorlds.CampaignSystem.dll` → `MBInformationManager`（namespace `TaleWorlds.CampaignSystem`）

**适用场景**：任务进度更新、技能检定成功/失败、瞬间反馈通知。**不适用**：需要玩家回顾查阅的长文本、历史记录。

**调试日志**：所有 `AddQuickInformation` 调用已通过 `AddQuickInformationLoggerPatch`（Harmony Prefix）自动写入 `DebugLogger`，搜 `[AddQuickInformation]` 即可追踪。

**文件位置**：`Debug/AddQuickInformationLoggerPatch.cs`（Harmony 日志补丁）

---

# 日志纪律

**铁律**：① `DebugLogger.Log` 只记录玩家可感知的事 + 关键后台状态变更。② Per-NPC 循环日志是垃圾——每轮扫描最多一条汇总。③ 错误始终记录。

```csharp
// ✅ 好的日志
DebugLogger.Log($"[Player] NinjaReport: {summary}");            // 玩家看到通知
DebugLogger.Log($"[Player] Inquiry: '去看看' — {loc}");          // 玩家做出选择
DebugLogger.Log($"[Player] Talk to: {npcName}");                 // 玩家跟谁说话
DebugLogger.Log($"[WorldEvent] New event: {type} at {loc}");     // 事件创建
DebugLogger.Log($"[WorldEvent] Motivated conflict: A → B");     // 真人冲突
DebugLogger.Log($"[CommissionIssue] {settlement}: scanned {n} NPCs, created {m} issues"); // 轮次汇总

// ❌ 垃圾日志（每条占一行，2500行淹没13行有用信息）
// GetAvailableDefs / HasCommissionsFor / OnCheckForIssue — 逐NPC日志全砍
```

**日志前缀约定**：`[Player]` = 玩家感知事件，`[WorldEvent]` = 世界事件生命周期，`[Commission*]` = 委托系统关键节点。

---

# 村庄动物偷窃与库存同步 — `Stealth/VillageAnimalTracker.cs` + `Interaction/InteractionMissionView.cs`

场景动物（羊/牛/猪/鹅/鸡）偷窃系统，带持久化追踪、自然恢复、ItemRoster 自动同步、价格修正。

## 动物识别 — `InteractionMissionView.IsAnimalAgent` / `GetLivestockItemForAnimal`

```csharp
// 判断是否为动物 Agent（村庄场景中 IsHuman=false 的牲畜）
InteractionMissionView.IsAnimalAgent(agent);  // → bool

// Monster.StringId → 牲畜 ItemObject 静态缓存（两轮查找：精确 ID + 遍历 Animal 类型 + 兜底名字匹配）
ItemObject item = InteractionMissionView.GetLivestockItemForAnimal(monsterId, animalName);
```

动物 monster ID 白名单：`sheep`, `cow`, `hog`, `goose`, `chicken`（`InteractionMissionView.AnimalMonsters`）。

## 偷窃持久化 — `VillageAnimalTracker`

三层持久化数据（按 `"settlementId|monsterId"` 为 key，JSON 序列化，`MyBehavior.SyncData` 跨存档）：

```csharp
// 记录偷窃
VillageAnimalTracker.RecordTheft(settlementId, monsterId, count = 1);
// 查询被偷数
int stolen = VillageAnimalTracker.GetStolenCount(settlementId, monsterId);
// 每日自然恢复（每种每天恢复 1 只）
VillageAnimalTracker.DecayDaily();  // MyBehavior.DailyTick 中调用

// 缓存/读取场景自然生成数（首次进场景时记录）
VillageAnimalTracker.SetNaturalCount(settlementId, monsterId, count);
int natural = VillageAnimalTracker.GetNaturalCount(settlementId, monsterId);
bool has = VillageAnimalTracker.HasNaturalCache(settlementId);
```

## ItemRoster 补足 — `InteractionMissionView.TopUpRosterToNaturalCounts`

```csharp
// 按缓存自然数补足村庄 ItemRoster：expected = naturalCount - stolenCount，只补不删
// 可从场景进入或村庄菜单调用（无场景依赖）
InteractionMissionView.TopUpRosterToNaturalCounts(settlement);
```

## 偷动物 — `InteractionMissionView.TryStealAnimal` → 抓动物偷窃条 → `CompleteAnimalSteal`

```csharp
// 蹲下才能偷：UI 三层门槛（站着提示「蹲下才能偷」/输入层拦截/TryStealAnimal 防御守卫）
// TryStealAnimal：守卫 → 开 StealBar Animal 模式（子弹时间+冻结，一次出手定胜负）
//   命中（AnimalCaught）→ CompleteAnimalSteal(async)：面向 → act_pickup_down_begin → 400ms
//     → 动物存活+距离≤5m 复查 → GetLivestockItemForAnimal → StealManager.StealAnimal
//     → animal.FadeOut → act_pickup_down_end → 「获得了 xx！」
//   手滑（AnimalFled）→ VM 内 OnAnimalStruggleFlee：目击者警戒脉冲 + WitnessCrime 围堵 + 逃跑，动物留下可再抓
// _isStealingAnimal 并发守卫贯穿条+动画全程；CloseStealInterface 统一复位（含 _stealAnimalTarget 清空）
```

**犯罪记账对齐**：`RecordAnimalTheft` 有目击时除证词登记外，还会 `BroadcastEventInRange("WitnessCrime")` 立即围堵（victim=null，与保管箱偷窃同一处理方式）。

## 动物近距离检测

`ProcessAgentCandidate` 中 `NpcSightSystem.IsPlayerSeeing` 对动物永远返回 false（`TickTrackedTarget` 过滤非人类 Agent），动物跳过此预检，只依赖距离+点积判定。

## 价格修正 — `VillageAnimalPricePatch`

非本地特产动物（不在 `Village.VillageType.Productions` 中）：买入 5 倍、卖出 0.3 倍。只对玩家交易生效。

```csharp
// Harmony Postfix on VillageMarketData.GetPrice(EquipmentElement, MobileParty, bool, PartyBase)
// 自动生效，PatchAll 注册
```

## 触发点一览

| 触发时机 | 补丁 / 方法 | 作用 |
|----------|------------|------|
| 进村庄场景 | `SyncSceneAnimalsWithInventory` (MissionView.OnMissionTick 首帧) | 缓存自然数 + 裁剪被偷动物 + 补 Roster |
| 开村庄菜单 | `VillageMenuAnimalPatch` (Harmony Postfix on `GameMenu.SwitchToMenu("village")`) | 补 Roster（读缓存，不进场景也能触发） |
| 交易界面打开 | `TradeScreenAnimalLoggerPatch` (Harmony Prefix on `InventoryManager.OpenScreenAsTrade`) | 打印 ItemRoster 动物日志 |
| 价格查询 | `VillageAnimalPricePatch` (Harmony Postfix on `VillageMarketData.GetPrice`) | 非本地动物价格修正 |

**文件位置**：`Stealth/VillageAnimalTracker.cs`、`Interaction/InteractionMissionView.cs`（`SyncSceneAnimalsWithInventory` / `TryStealAnimal` / `TopUpRosterToNaturalCounts` / `ProcessAgentCandidate` 及全部 Patch 类）、`Core/MyBehavior.cs`（`DailyTick` 衰减 + `SyncData` 持久化）。

---

# 场景感知分发模式（ChestContext）— `Stealth/StealManager.cs`

**解决什么问题**：定居点有多个子场景（城镇 = 中心/酒馆/领主大厅/暗巷/竞技场），任何「按场景差异化」的系统（偷窃金库、贿赂、情报、锚点 NPC）都要回答四个问题：**①我在哪个场景 ②这个场景分多少资源 ③这个场景出什么内容 ④文案/锚点怎么随场景变**。这个模式用一张枚举 + 四张 switch 表统一回答，加新场景 = 每张表加一行，不写 if-else 链。

## 四件套结构

```csharp
// ① 场景枚举
public enum ChestContext { Village, TownTavern, TownCenter, LordsHall, Alley, Arena, Dungeon, Castle, Unknown }

// ② 场景识别：Location.StringId 优先（精确到子场景），回退定居点类型
ChestContext ctx = StealManager.GetCurrentChestContext();
//   locId 含 "tavern"→TownTavern / "lordshall"→LordsHall / "alley"→Alley / "arena"→Arena
//   "prison"|"dungeon"→Dungeon（原版地牢 id 为 "prison"，dungeon 兼容其他 mod 命名）
//   "center" 或含 "village" → 按 Settlement.IsVillage 分 Village/TownCenter
//   回退：IsVillage→Village / IsTown→TownCenter / IsCastle→Castle / else Unknown

// ③ 资源权重表 + 内容过滤表（各一张 switch）
float w = StealManager.GetChestContextGoldWeight(ctx);   // 中心.40/大厅.30/酒馆.15/暗巷.10/竞技场0/村·城堡1.0/Unknown.20
bool ok = StealManager.IsItemAllowedInContext(item, ctx); // 酒馆=食物杂货，大厅=武器防具马匹书籍，暗巷=赃物轻甲，动物一律false

// ④ 场景化锚点 + 文案
Agent a = StealManager.FindChestAnchorAgent();              // 酒馆→Tavernkeeper（优先级有序），大厅/城堡→IsLord，暗巷→GangLeader，村庄→Headman，多级兜底
Vec3 pos = StealManager.ResolveChestSpawnPosition(scene, a); // 锚点正后方 2.0m→1.2m 逐级收缩 + navmesh 验证，兜底 +X 2m
var (hint, title, prefix) = InteractionMissionView.GetChestTexts(ctx);  // (提示语, 标题, 内容前缀) 三元组
// 生成：StealManager.SpawnStorageChestProp(scene, pos, anchor?.Position) — 0.5× 缩放（ChestScale 常量），正面朝向锚点
```

## 复合键防重复

同一「定居点+场景」只分配一次，用 `$"{settlement.StringId}|{locationId}"` 复合键替代纯定居点 ID——Town 内部各子场景独立分配（进酒馆分一次、进大厅再分一次），村庄/城堡单场景行为不变。

## 复用要点（搬到新系统时）

- **`CampaignMission.Current.Location.StringId` 是第一手信号**，比 Settlement 类型精确——能区分同一城镇的不同室内场景。
- **每个维度一张 switch 表**（权重/过滤/文案/锚点），维度之间不交叉引用。
- **禁用场景三处一致关闭**：权重返回 0 + 过滤返回 false + 文案返回空串（参考 Arena / Dungeon 不刷保管箱）。
- **Unknown 兜底给保守值**：权重 0.20、只放行 Goods+Food，宁可少给不出戏。

## 实体名黑名单（扫描场景实体防误伤）

扫描场景实体做「储物道具克隆」时，引擎内部实体会命中名字关键词评分（`__skybox__` 含 "box" → 85 分误伤夺冠，天空盒被克隆成保管箱）。`IsBlacklistedEntityName(name)` 统一拦截：

| 匹配方式 | 名单 |
|---------|------|
| 精确 | `__skybox__` |
| 前缀 | `torch_` `flame_` `light_` `smoke_` `sound_` `fire_` `particle_` `vfx_` |
| 包含 | `_collision_` `_hitbox_` `_water_` `_trigger_` |

任何「遍历 Scene 实体 → 按名字打分选候选」的逻辑都应先过这道黑名单再评分。

**文件位置**：`Stealth/StealManager.cs`（`ChestContext` 枚举 + `GetCurrentChestContext` + `GetChestContextGoldWeight` + `IsItemAllowedInContext` + `FindChestAnchorAgent` + `ResolveChestSpawnPosition` + `IsBlacklistedEntityName`）、`Interaction/InteractionMissionView.cs`（`GetChestTexts` + SpawnChest/开箱 Inquiry 消费点）。

## 附：场景锁簧片数表

`StealManager.GetLockpickPinCount(ctx)` —— 撬锁难度的场景分发（沿用本模式）：村庄 2 / 城镇中心·酒馆·暗巷 3 / 城堡·领主大厅 4 / 兜底 2。锁难度 = 世界规则，与场景枚举同住 StealManager，不放 View。

---

# 时机判定条小游戏引擎（StealBar）— `Stealth/StealBarVM.cs` + `GUI/Prefabs/StealBar.xml`

**解决什么问题**：「光标-目标区」时机判定小游戏（大侠立志传式偷窃/撬锁/抓动物）。一个 VM **三模式**（枚举切换），纯动画状态在 C# 侧由 View 每帧驱动，空格/按钮共用同一出手方法。任何新的「抓时机」玩法（钓鱼、打铁、拆解、追踪）都可复用这套骨架。

## 结构（开/关/tick 三段式）

```csharp
// VM（继承 ViewModel；运动状态纯 C# 非绑定，绑定只同步渲染值）
var vm = new StealBarVM(StealBarMode.Pickpocket, targetAgent, closeAction);
var vm = new StealBarVM(StealBarMode.Lockpick, pinCount, title, closeAction);
var vm = new StealBarVM(StealBarMode.Animal, animalAgent, animalName, isLarge, closeAction); // 抓动物：大动物判定区右扣 40%，一次出手定胜负
vm.UpdateFrame(dt);          // ← MissionView.OnMissionTick 每帧驱动；dt 为 Scene 缩放时间（子弹时间自洽）
vm.ExecuteAttempt();         // 空格/按钮共用的出手（命中判定），Command.Click 可直接绑
vm.CloseReason               // StealBarCloseReason 枚举 — VM 不直接关 UI，View 轮询消费统一收口

// View（InteractionMissionView 为范本）
_stealLayer = V.NewLayer(201);
V.LoadMov(_stealLayer, "StealBar", vm);
_stealLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.Mouse); // 鼠标留给按钮
// tick 内：vm.UpdateFrame(dt) → Input.IsKeyPressed(Space/Tab) → vm.CloseReason 消费 → 关层
```

**CloseReason 轮询收口**：VM 想关 UI（目标走开/警觉拉满/完成）只置 `CloseReason`，不碰 Layer；View 每帧读它统一走 `CloseStealInterface`——所有关闭路径（收手 Tab/强制/完成）一个收口函数，配套资源（子弹时间/输入冻结/IsUIOpen）不可能漏回收。**收手键 = Tab**（ESC 与游戏菜单冲突，已让出）。⚠️ Tab 原版占用：`Mission.Tick` 中 `IsFriendlyMission`（城镇/村庄漫游）下长按 Tab 0.6s 离开场景（`IsGameKeyDown(4)` 按住状态计时，松开清零；敌人 5m 内拦截）——我们用按下沿 `IsKeyPressed`，轻点收手不触发离场，长按则先收手再离场，语义自洽无需屏蔽。

**CloseReason 全量**：`TargetGone`（目标走开/死亡）/ `Alarmed`（警觉拉满或质问锁占用）/ `Completed`（撬锁完成→接开箱 Inquiry）/ `NothingLeft`（摸空→自动收口+提示）/ `AnimalCaught`（抓动物命中→View 接 `CompleteAnimalSteal`，**关层前先抢出 `_stealAnimalTarget`**，收口会清空它）/ `AnimalFled`（手滑，VM 内已惊叫逃跑，View 只收口）。

## 扒窃盲盒：钱袋独立目标 + 摸空自动收口

**金钱 = 特殊物品，独立偷窃（铁律 4），禁止「偷装备顺手白摸钱」。**

```csharp
// StealManager
StealManager.HasPurseGold(agent);       // 身上分配金 > 0？
StealManager.StealPurseGold(agent);     // → int 实偷面额；整袋端走（分配金全拿）。族长 Hero.Gold 摸不到（=全族资金，只能战场击败部队获得）
StealManager.HasAnythingToSteal(agent); // 任一装备槽或钱袋 → 开条前预检

// StealBarVM.NextPickpocketRound（盲盒回合）：
//   装备槽随机抽 → 身上有钱时 PurseChance(0.35) 覆盖为「钱袋回合」(_pendingIsPurse)
//   装备摸空但有钱 → 必摸钱袋；全空 → CloseReason = NothingLeft
//   钱袋预览「摸到一个沉甸甸的钱袋。」，难度定价 1.0（标准）
```

- **开条前**：`TryStealFromAgent` 先查 `HasAnythingToSteal`，没东西 → DisplayMessage 直接不开条。
- **偷到一半摸空**：`NextPickpocketRound` 置 `NothingLeft` → View 自动关条 + 「他身上已经没什么可偷的了。」

## Animal 模式（抓动物）与「动物无 Brain」边界

- 难度表达：动物**无 AgentBrain**（`AgentAIController` 只给 `IsHuman` 注册脑）→ 无警戒值 → 判定区不扣警戒、不游动，难度纯由体型定价（`_itemTierFactor`：大动物 0.6 / 小动物 1.0）；Roguery 减速浮标照常生效。
- **动物行为不走 Brain 事件体系**：惊叫/逃跑由 `StealManager.OnAnimalStruggleFlee` 一次性处理（目击者警戒脉冲 + WitnessCrime 广播 + `ScriptedMoveToPoint` 脚本移动逃跑 8~14m），不入队 IAtomicAction。
- `PollForceClose` 对 Animal 生效（溜走 4.5m/死亡 → TargetGone），`GetBrainForAgent` 返回 null 天然跳过警觉检查。

## 减法五色条（信号贡献可视化）

基础宽**左端**扣警戒、**右端**扣物品，剩余 = 有效判定区，下限 = 完美区宽（钳满 = 全或无，每次命中即完美）。每层一个 Widget 绑 `float MarginLeft/SuggestedWidth`。**二元色相分离**：可偷=琥珀黄 `#D4AF37`、完美=绿芯 `#3DA53D`（安全暖色族）；不可偷=红族（界外=最深黑红 `#2E1010` / 潜在区黑红亮一档 `#4A1C1C`——红族底色必须全不透明且明度拉开，半透明或太暗会被面板底色吃成纯黑 / 警戒血红 `#A81F1F` / 物品橙红 `#B5502A`——橙红与黄区相邻必须偏红偏暗防混淆）。结果闪烁 = 所中区域变亮：成功亮金 `#FFE97F` / **完美白闪 `#FFFFFF`**（不用绿闪——会把黄区染成"全是绿芯"摧毁语义）/ 失败红 `#DD4444`。⚠️ 闪烁计时走缩放 dt：`ResultFlashSeconds = 0.1` 缩放秒在 0.35× 慢动作下 ≈0.3 真实秒（体感一闪而过）——**此类计时常量必须按"真实时长 = 常量/0.35"换算**，且闪烁期间新一回合已开始，颜色不能伪装成任何区域语义色。成因区分降为红族内明度差，解释交给动态文本行。条下两行说明：规则行①固定（`RuleText`，构造时按模式设）、规则行②动态（`CursorZoneText/Color`，`UpdateCursorZoneHint` 每帧按浮标位置判定完美/有效/警戒扣/物品扣/界外，文本+颜色跟随）。
**铁律**：宽度域每个色块成因必须唯一可读——技能等加成**禁止混进宽度**（技能走浮标速度通道：`260 ×(1−Roguery/300×25%)`）。结果文本不占控件，走 `InformationManager.DisplayMessage`，条上只留颜色闪烁做即时反馈。
**文本变色通道**：TextWidget 无 `TextColor` 属性（写了静默无效）；动态文本色走 `Brush.FontColor="@ColorProp"`（原版 MPMissionMarkerFlag 有绑定先例）。

## 双动体 + 2.5× 铁律

| | 浮标（玩家的手） | 子横条（猎物心神） |
|---|---|---|
| 运动 | 线性 ping-pong（匀速撞墙折返） | 正弦游弋（缓入缓出，仅 Cautious≥1.0 起） |
| 速度 | 260px/s ×技能减速（撬锁 pin ×1.15ⁿ） | 55→100px/s 随警戒，且 **≤浮标/2.5 动态封顶** |

**铁律**：浮标速度 ≥ 游动 2.5 倍——同速双动体追踪超出人类反应，退化成运气。由**游动侧动态封顶**实现（`DriftSpeed ≤ CursorSpeed/2.5`），技能减速浮标后比例自动成立。

## 子弹时间 — `Mission.AddTimeSpeedRequest`（含坑）

```csharp
Mission.Current.AddTimeSpeedRequest(new Mission.TimeSpeedRequest(0.35f, requestId));  // 请求队列，取最小值
// ⚠️ 移除必须先查！RemoveTimeSpeedRequest 对未知 ID 直接 RemoveAt(-1) 抛 ArgumentOutOfRangeException：
if (mission.GetRequestedTimeSpeed(requestId, out _))
    mission.RemoveTimeSpeedRequest(requestId);
```

- dt 缩放链路（反编译确认）：`Scene.TimeSpeed` → `OnTick(dt=缩放, realDt=真实)` → `OnMissionTick(dt)`——VM 动画/警戒累积/节流计时全走缩放 dt，子弹时间下世界与 UI 同步变慢，难度不被白嫖。
- 配 `_stealSlowmoActive` 幂等标志；关闭每条路径 + `OnMissionScreenFinalize` 兜底回收。

## 玩家输入冻结 — `V.SetPlayerControlFrozen(agent, bool)`

模态小游戏 UI 打开时，键盘拦不住（ScreenManager 键盘路径只看 `FocusedLayer`，层 mask 无效），剥 `EventControlFlags` 也无效（原生控制器在托管 tick 之后才写标志）。**正解：切控制器**（详见 [pitfalls.md](pitfalls.md)「模态 UI 键盘输入拦不住」）。

```csharp
V.SetPlayerControlFrozen(Agent.Main, true);   // 开 UI：v1.2.12 切 ControllerType.AI → 主角待机
V.SetPlayerControlFrozen(Agent.Main, false);  // 关 UI：切回 Player（自动重指 MainAgent + 广播）
// Latest：ControllerType→AgentControllerType 改名，setter 仍可用。配 _playerControlFrozen 幂等标志。
```

安全性：`AgentBrain.Tick` 对 `Owner == Agent.Main` 早退——本 mod brain 不会接管主角；SandBox 官方有同款切 AI 用法。

**文件位置**：`Stealth/StealBarVM.cs`（引擎 + `StealPinVM`）、`GUI/Prefabs/StealBar.xml`（五层条 + 双按钮行）、`Interaction/InteractionMissionView.cs`（`TickStealBar`/`StartStealSlowmo`/`FreezePlayerControl` 为接线范本）、`Core/VersionCompat.cs`（`V.SetPlayerControlFrozen`）。

---

# 统一输入映射层 — `Input/ModInput.cs`

**解决什么问题**：① 业务代码裸写 `InputKey.F` 导致手柄完全无法游玩；② 按键提示（InteractArea 圆徽、偷窃条按钮文本）硬编码键盘字串，手柄玩家看到错误提示；③ 改键要满世界找 `IsKeyPressed`。UE4 风格 Action Mapping：**业务层只认语义动作，不认物理键**。

## 三件套

```csharp
// ① 语义动作枚举：加新互动 = 加一个枚举值 + 映射表加一行
public enum ModInputAction { Interact, AltInteract, Inspect, StealAttempt, StealLeave }

// ② 输入轮询（键盘+手柄双通道同时监听，玩家中途换设备无需切换逻辑）
ModInput.Pressed(ModInputAction.Interact);    // 按下沿，键盘 F 或手柄 X/□ 任一命中即算
ModInput.Released(ModInputAction.StealLeave); // 松开沿

// ③ 提示字形（按"最近一次输入设备"自动分键盘/Xbox/PS 三套文本）
ModInput.Glyph(ModInputAction.Interact);  // → "F" / "X" / "□"
ModInput.UsingGamepad;                    // 当前设备是否手柄
ModInput.IsPlayStation;                   // 手柄是否 PS 系
```

## 当前映射表（改键唯一入口）

| 动作 | 键盘 | Xbox | PS | 用途 |
|------|------|------|-----|------|
| Interact | F | X | □ | 对话/偷窃/击晕/搜刮/撬锁 |
| AltInteract | G | Y | △ | 闲聊/接受认输 |
| Inspect | H | L3 | L3 | 探查 NPC 信息板 |
| StealAttempt | 空格 | A | ✕ | 偷窃条出手 |
| StealLeave | Tab | B | ○ | 偷窃条收手 |

**键位选择纪律**：主互动用 X/□ 不用 A/✕——A 是跳跃，漫游时会误触；偷窃条内可以用 A（条打开时玩家控制已冻结）。手柄键位对照：`RDown=A/✕` `RRight=B/○` `RUp=Y/△` `RLeft=X/□` `LThumb=L3`。

## 设备检测原理（引擎原生，与原版 UI 判定一致）

- 最近设备：`Input.IsGamepadActive`（= `IsControllerConnected && !IsMouseActive`，引擎每帧 `Input.Update()` 维护）——**不要自己造键盘/手柄检测**。
- Xbox/PS 区分：`Input.ControllerType.IsPlaystation()`（DualShock/DualSense → true）。
- PS 字形 □△✕○ 走 CJK 符号区，中文字体可渲染；若 ✕ 实机豆腐块，改映射表 `PsGlyph` 一行即可。
- v1.2.12 / v1.4.6 双版本 API 一致（已核实），无需 `V.` 包装。

## 接入范式（UI 按键提示随设备切换）

```csharp
// View 侧：缓存设备状态逐帧对比，变化时刷新全部按键提示（InteractionMissionView.OnMissionTick 为范本）
bool usingGamepad = ModInput.UsingGamepad;
if (usingGamepad != _lastUsingGamepad)
{
    _lastUsingGamepad = usingGamepad;
    _interactVM?.RefreshGlyphs();        // InteractArea：item 存 ModInputAction?，重算 KeyText
    _stealBarVM?.RefreshButtonTexts();   // 偷窃条：重算 AttemptButtonText/LeaveButtonText
}
```

**VM 侧纪律**：按键提示文本**禁止**写死 `"F"`/`"[空格]"` 字串——item/按钮存 `ModInputAction`，显示时 `ModInput.Glyph()` 实时解析（范本：`InteractionItemVM.RefreshKeyText`、`StealBarVM.RefreshButtonTexts`）。XML 一律绑 `@KeyText`/`@XxxButtonText`，不写裸文本。

**文件位置**：`Input/ModInput.cs`（枚举 + 映射表 + 轮询/字形 API）、`Interaction/InteractionVM.cs`（`RefreshGlyphs` 范本）、`Stealth/StealBarVM.cs`（`RefreshButtonTexts` 范本）、`Interaction/InteractionMissionView.cs`（设备切换检测范本）。

---

# 版本兼容层 — `Core/VersionCompat.cs`

**同一份源码，多版本编译。** `V` 静态类封装了全部跨版本 API 差异。每个方法用**累积阈值宏**（`MB2_GE_130`、`MB2_GE_140`）分支，而非精确版本匹配。

**宏体系**：csproj 编译时读 `Version.xml` 自动定义累积宏：
| 游戏版本 | 定义的宏 |
|----------|---------|
| v1.2.12 | `MB2_V1212` |
| v1.3.x  | `MB2_V1212` + `MB2_GE_130` |
| v1.4.x  | `MB2_V1212` + `MB2_GE_130` + `MB2_GE_140` |
| v1.5.x  | 全部上述 + `MB2_GE_150` |

GE = "Greater or Equal"。代码按阈值从高到低写：`#if MB2_GE_150` / `#elif MB2_GE_130` / `#else`（v1.2.12）。

**使用纪律**：
- 凡是两个版本 API 不一样的调用，**一律走 `V.xxx()`，禁止在业务代码里裸写版本 `#if`**（Harmony 补丁 / override / type-level 差异例外，详见「不可迁 #if 注册表」）
- 新加 V 方法后**必须在两台电脑上分别编译通过**（v1.2.12 + Latest 各 build 一次）
- 1.4.6 和 1.4.7 的 API 经逐方法对比确认**完全一致**，`MB2_GE_130` 分支覆盖 v1.3.0 ~ v1.4.x 全系列

```csharp
// ── 位置（v1.2.12: .Position2D / Latest: .GetPosition2D）
V.Pos(party)              // Vec2 — MobileParty
V.Pos(settlement)         // Vec2 — Settlement
V.SetPos(party, pos)      // void — 设置 party 位置

// ── 部队移动（v1.2.12: party.Ai.SetMove* / Latest: party.SetMove*）
V.SetMoveTo(party, pos)           V.SetMoveEngage(party, target)
V.SetMoveToTown(party, settlement) V.SetMovePatrol(party, pos)
V.SetMoveEscort(party, target)     V.MoveTarget(party) → MobileParty

// ── 部队生命周期
V.MakeParty(id, component)          // CreateParty 3参/2参
V.DelParty(party)                   // RemoveParty / DestroyPartyAction
V.InitPartyPos(party, template, pos) // InitializeMobilePartyAtPosition Vec2/CampaignVec2
V.SetPartyName(party, name)         // SetCustomName / Party.SetCustomName

// ── Agent 控制
V.IsAgentAI(agent) → bool           V.SetAgentAI(agent)
V.IsAgentPlayer(agent) → bool       V.SetAgentPlayer(agent)
V.SetPlayerControlFrozen(agent, frozen)  // 冻结/恢复玩家控制（v1.2.12: ControllerType.AI/Player；Latest: AgentControllerType.AI/Player）

// ── 武器 / 动作
V.MainWpn(agent) → EquipmentIndex   V.OffWpn(agent) → EquipmentIndex
V.ActName(agent, channelIndex = 0) → string

// ── UI
V.NewLayer(order, name = null) → GauntletLayer  // 构造参数顺序反了
V.LoadMov(layer, name, vm)                       // 返回类型不同，v1.2.12 存 object

// ── 其他
V.GetStartTime() → CampaignTime      V.KingdomStr(kingdom) → float
V.EmptyText() → TextObject           V.NavMesh(scene, pos, out faceIndex) → bool
V.JoinDefect(clan, from, to)         V.GetEnemyKingdoms(kingdom) → IEnumerable<Kingdom>

// ── SetPartyAiAction 重载（v1.2.12: 2参 / v1.3.0+: 3~5参）
V.PatrolAround(party, settlement)     // GetActionForPatrollingAroundSettlement
V.RaidSettlement(party, settlement)   // GetActionForRaidingSettlement
V.BesiegeSettlement(party, settlement)// GetActionForBesiegingSettlement
V.EngageParty(party, target)          // GetActionForEngagingParty

// ── 导航网格 / 地图
V.NavMeshSnap(scene, ref pos)         // GetNavigationMeshForPosition in/ref 差异
V.AccessiblePointNear(wrapper, pos, r)→ Vec2  // GetAccessiblePointNearPosition Vec2/CampaignVec2
V.FaceIndex(wrapper, pos) → PathFaceRecord    // GetFaceIndex Vec2/CampaignVec2
V.CameraAnimate(mapState, pos, dur)   // StartCameraAnimation Vec2/CampaignVec2
```

**文件位置**：`Core/VersionCompat.cs`（约 420 行）。

**版本参考 DLL**：`Modules/1.2.12DLL/` 和 `Modules/1.4.6DLL/` 存放了另一版本的 DLL 副本，**仅 `ilspycmd` 反编译用，不参与编译**。在 v1.2.12 电脑上开发时查 `1.4.6DLL/` 看 Latest API，反之亦然。方法：`ilspycmd Modules/1.4.6DLL/TaleWorlds.CampaignSystem.dll -t <Type> | grep "Method"`。

---

# csproj 版本自动检测（累积阈值宏）

**一个配置通吃所有版本**，无需手动切换。编译时读 `Version.xml`，通过版本系列侦测 + `Or` 链自动定义累积阈值宏。

```xml
<!-- 版本系列侦测（精确到 minor） -->
<MB2_IsV12x Condition="$(MB2_VersionFileContent.Contains('v1.2.'))">true</MB2_IsV12x>
<MB2_IsV14x Condition="$(MB2_VersionFileContent.Contains('v1.4.'))">true</MB2_IsV14x>

<!-- 累积阈值：GE_130 = v1.3.x OR v1.4.x OR ... -->
<MB2_VersionDefines Condition="'$(MB2_IsV14x)' == 'true'">$(MB2_VersionDefines);MB2_GE_130;MB2_GE_140</MB2_VersionDefines>
<!-- 各配置引用 $(MB2_VersionDefines) -->
<DefineConstants>DEBUG;TRACE;$(MB2_VersionDefines)</DefineConstants>
```

**结果**：
| 电脑 | Version.xml | 定义的宏 |
|------|-----------|---------|
| v1.2.12 | `v1.2.12` | `DEBUG;TRACE;MB2_V1212` |
| v1.4.7  | `v1.4.7`  | `DEBUG;TRACE;MB2_V1212;MB2_GE_130;MB2_GE_140` |

**新增版本**：TaleWorlds 出新版本（如 v1.5.0）时：
1. 加 `<MB2_IsV15x>` 侦测行
2. 在已有 `GE_*` 的 `Or` 链中追加 `'$(MB2_IsV15x)' == 'true'`
3. 加 `MB2_GE_150` 的定义行

完整清单见 [plans/version-compat-plan.md](../version-compat-plan.md)。

**文件位置**：`ExampleMod.csproj` PropertyGroup 段。

---

# 不可迁入 V 的 #if（合规例外注册表）

以下类别的 `#if` **不能**封装为 `V.xxx()`，直接写在业务文件里是合法的。每次新增版本时必须逐条核查：

| 类别 | 典型文件:行号 | 原因 |
|------|-------------|------|
| override | `SafeLordPartyComponent.cs:41` 等 4 处 | 基类虚方法签名跨版本不同 |
| type | `MySubModule.cs:344` 等 5 处 | 字段类型 `IGauntletMovie`→`GauntletMovieIdentifier` |
| type | `PlayerDetentionBehavior.cs:9,312` | `GameOverlays.MenuOverlayType`→`GameMenu.MenuOverlayType` |
| Harmony | `InteractionMissionView.cs:2529,2559` | 补丁目标类/方法跨版本不同 |
| Harmony | `DebugLogger.cs:18` | `FillPartyStacks`→`FillPartyManuallyAfterCreation` |
| structural | `InteractionMissionView.cs:1909,2364` | 搜刮 Loot 流（`InventoryManager` 不可用） |
| structural | `WorldEventSimulator.cs:1716,1771` | `AreFacesOnSameIsland` 移除 |
| structural | `MyCommands.cs:1619` | stealth_debug 命令（依赖仅 Latest 存在） |
| namespace | `MyCommands.cs:30` | `SandBox.Missions.*` 命名空间仅 Latest 存在 |

完整注册表在 [VersionCompat.cs class doc comment](../../ExampleModVS/ExampleMod/ExampleMod/Core/VersionCompat.cs) 和 [version-compat-plan.md](../version-compat-plan.md)。

---

# Harmony 补丁版本兼容

Harmony 补丁在 `PatchAll()` 时如果找不到目标方法会**直接抛异常崩溃**。跨版本时必须处理：

1. **方法消失了** → `#if MB2_V1212` / `#else` 写两套，各版本补各自的目标
2. **方法签名变了** → 同上，用 `#if` 分支写不同的 Prefix/Postfix 参数
3. **编译时找不到类型**（如全局命名空间 vs using 冲突）→ 用 `AccessTools.Method("TypeName:MethodName")` 动态查找
4. **类型所在子命名空间变了** → `typeof()` 用完全限定名 + `#if` 分支

```csharp
// 场景 1：两版本各补各的方法
#if MB2_V1212
[HarmonyPatch(typeof(MobileParty), "FillPartyStacks")]
public static class DebugCrashPatch
{
    public static void Prefix(MobileParty __instance, PartyTemplateObject pt, int troopNumberLimit) { ... }
}
#else
[HarmonyPatch]
public static class DebugCrashPatch
{
    // AccessTools 运行时查找，绕过编译时类型不可见
    private static MethodBase TargetMethod() => AccessTools.Method("MobilePartyHelper:FillPartyManuallyAfterCreation");
    public static void Prefix(MobileParty mobileParty, PartyTemplateObject partyTemplate, int desiredMenCount) { ... }
}
#endif

// 场景 2：方法拆分了，每个版本补一个
#if MB2_V1212
[HarmonyPatch(typeof(AgentInteractionInterfaceVM), "SetAgent")]
public static class ChangeInteractionTextPatch
{
    public static void Postfix(AgentInteractionInterfaceVM __instance, Agent focusedAgent)
    {
        __instance.SecondaryInteractionMessage = "";
        __instance.PrimaryInteractionMessage = "";
    }
}
#else
[HarmonyPatch(typeof(TaleWorlds.MountAndBlade.ViewModelCollection.Missions.Interaction.AgentInteractionInterfaceVM), "SetHumanAgent")]
public static class ChangeInteractionTextPatch
{
    public static void Postfix(TaleWorlds.MountAndBlade.ViewModelCollection.Missions.Interaction.AgentInteractionInterfaceVM __instance, Agent focusedAgent)
    {
        // ⚠️ 不能 Clear()！ResetFocus() 会按索引访问 [0]/[1]，列表空了就 ArgumentOutOfRangeException
        __instance.PrimaryInteractionMessages?.ApplyActionOnAllItems(x => x.ResetData());
        __instance.SecondaryInteractionMessages?.Clear(); // Secondary 安全，只被 .Count 检查
    }
}
#endif
```

**排查方法**：`ilspycmd <DLL> -t <TypeName> | grep "方法名"` 确认目标方法在两个版本中的存在性和签名。如果 `typeof()` 编译报错但 ilspycmd 确定类型存在（可能是命名空间遮蔽），用 `AccessTools.Method` 绕过。

**注意**：编译通过不代表运行时能跑——Harmony 是运行时绑定的。必须在目标版本的实际游戏里测试。

**MBBindingList 坑点**：v1.4.6 里 `PrimaryInteractionMessages` / `SecondaryInteractionMessages` 从 `string` 变成了 `MBBindingList<T>`。清空内容时**不能 `Clear()`**——后续代码（如 `ResetFocus()`）可能按索引 `[0]`/`[1]` 直接访问，列表空了直接 `ArgumentOutOfRangeException`。正确做法是 `ApplyActionOnAllItems(x => x.ResetData())` 清空内容但保留占位。

---

# 原版对话流注入 — `Interaction/Dialogue/DialogueInjector.cs`

**JSON 驱动的原版 `ConversationManager` 对话注入器。当 NPC 对话需要走原版 UI（而不是 StoryDialogVM）时，优先用 JSON 注入，禁止硬编码 `DialogFlow` 链式调用。**

## 设计原则

| 场景 | 对话 UI | 何时用 |
|------|---------|--------|
| **Quest / Issue 对话** | 🔴 原版 `ConversationManager` + JSON 注入 | 任务接取、进行中讨论、任务目标对话——老玩家熟悉的原版体验 |
| **闲聊 / 自由对话** | `StoryDialogVM`（已有轮子） | 非任务场景的 NPC 互动、LLM 自由生成 |

**优先原版**：凡是能挂到 `hero_main_options` / `issue_offer` / `quest_offer` token 的，走 JSON 注入。只有原版 token 体系覆盖不了的场景（如大地图偶遇、无 Hero 的平民）才用 StoryDialogVM。

## 🔴 对话注入铁律

> **总原则见 CLAUDE.md 铁律 8「所有 Agent 平等互动」。** 以下为对话系统的具体落地要求。

### 铁律 A：对话入口不因"无 Hero"拒绝

所有对话入口（`TryInjectCrimeDialogue`、`BuildScript`、`PlaceholderResolver`、`IntentContext`）必须兼容 `speaker == null`。模板 NPC 自然走完身份分派链，不命中 Hero 身份检查时落 `BuildBystanderScript`。

```csharp
// ❌ 禁止：在对话入口处拦截模板 NPC
if (partner == null) return;

// ✅ 正确：模板 NPC 自然走完分派链，null-conditional 防 NRE
if (IsAuthority(speaker, evt)) ...                          // null-safe: npc?.Occupation
else if (evt.WitnessHeroIds?.Contains(speaker?.StringId) == true) ...
else if (evt.SuspectHeroId == speaker?.StringId) ...
else result = BuildBystanderScript(r, ctx);                  // 自然兜底
```

拦截白名单：**只有必须记录 Hero StringId 的场景才拦截**（如栽赃陷害 `INTENT:FrameSuspect`），通用互动（战斗、偷窃、贿赂、威胁、投降、八卦）一律放行。

### 铁律 B：对话注入统一收口 `StartConversation`

**所有对话注入——不管是玩家主动交谈、NPC 主动质问、还是战斗投降——都必须经过 `MissionConversationLogic.StartConversation`（或其 Prefix/Postfix `TryInjectCrimeDialogue`）统一处理。** 禁止调用方自己调 `DialogueInjector.InjectScript` 然后自己调 `StartConversation`。

```csharp
// ❌ 禁止：调用方自己注入 + 自己开对话
BuildAlertInterceptScript(r, ctx);
DialogueInjector.InjectScript(script, label);
conversationLogic.StartConversation(agent, true, false);

// ✅ 正确：调用方只设 trigger，TryInjectCrimeDialogue 统一注入
ConversationEntryPatch._pendingTrigger = DialogueTrigger.Alert;
ConversationEntryPatch._pendingConfrontation = detail;
ConversationEntryPatch._pendingTriggerAction = primaryAction;
conversationLogic.StartConversation(agent, true, false);
// → Prefix/Postfix 触发 TryInjectCrimeDialogue → BuildScript(trigger=Alert) → InjectScript
```

**为什么**：`StartConversation` 的 Patch 是唯一能保证"每次对话启动时只注入一次"的关口。调用方各自注入会导致双重注入、token 竞争、以及 `_lastInjectedEventId` 防重复机制失效。

**Prefix vs Postfix 分工**：

| Patch | 处理哪些 Trigger | 注入时机 | 注入模式 |
|-------|-----------------|---------|---------|
| **Prefix** | `PlayerSurrender` / `NpcSurrender` / `Alert` | `StartConversation` **之前** | `SkipVanillaOpening=true` — NPC 台词挂在 `start` token（优先级 200）覆盖原版开场白 |
| **Postfix** | `Normal` | `StartConversation` **之后** | Gateway 模式 — 在 `hero_main_options` 挂 PlayerLine入口，保留原版开场白 |

**为什么 Prefix 必须处理 SkipVanillaOpening 的 trigger**：`InjectScriptNoOpening` 往 `start` token 注入高优先级 NPC 台词来覆盖原版开场白。这必须在 `StartConversation` 处理 `start` token **之前**完成。Postfix 注入时 `start` token 已经被原版引擎评估完毕，注入的台词要到下一轮对话才生效——原版开场白已经播放了。

**防重复注入**：Prefix 消费 trigger 后会设 `_lastInjectedEventId`。Postfix 中的 `TryInjectCrimeDialogue` 检查 dedup 命中 → 跳过，不会二次注入。

## v2 新模型（2026-07-12 重构）

**核心原则：Transition 只管路由，NPC 台词统一在 DialogueNode.NpcLine。**

```
Node = NPC 说一句话 + 玩家可选的动作集合
Transition = 玩家选了一个动作 → 执行 → 路由到下一个 Node（或关窗）
```

### 数据类型

```csharp
public enum TransitionCheckType { None, SkillCheck }

public class DialogueNode {
    public string Id = "injectedStart";
    public string NpcLine;                        // NPC 台词唯一入口
    public Func<string> LazyNpcLine;              // 惰性求值（设置后覆盖 NpcLine）
    public List<DialogueTransition> Transitions;  // [] = terminal, null = 非法
}

public class DialogueTransition {
    public string PlayerLine;                     // 玩家选项文本
    public TransitionCheckType CheckType = None;  // None=直连 / SkillCheck=检定分叉
    public string Action = "NONE";                // NONE / INTENT:xxx
    public string ActionParam = null;             // 字符串参数（系统 Intent 用数值的字符串表示）
    public string NextNodeOnSuccess;              // 成功/无检定 → 目标 Node Id。""/null = 关窗
    public string NextNodeOnFail;                 // 检定失败 → 目标 Node Id（仅 SkillCheck）
}
```

### 路由逻辑

- **CheckType.None**：PlayerLine 直连目标 Node 的 entry token（或 close_window）
- **CheckType.SkillCheck**：桥接 token + 3 条 silent DialogLine（成功/失败/安全网）→ 目标 Node

### JSON 格式（v2）

文件放在 `ModuleData/DesignData/Dialogues/*.json`。

```json
{
  "InjectAtToken": null,
  "EntryOption": "（闲聊）…",
  "EntryNode": "injectedStart",
  "Nodes": [
    {
      "Id": "injectedStart",
      "NpcLine": "啊，你来得正好！",
      "Transitions": [
        {
          "PlayerLine": "什么怪事？",
          "NextNodeOnSuccess": "more_detail"
        },
        {
          "PlayerLine": "帮你处理，有什么好处？",
          "Action": "INTENT:GiveGold",
          "ActionParam": "100",
          "NextNodeOnSuccess": "give_gold_ack"
        }
      ]
    },
    {
      "Id": "give_gold_ack",
      "NpcLine": "你果然是个精明人——100第纳尔，怎么样？",
      "Transitions": [
        { "PlayerLine": "…", "NextNodeOnSuccess": "more_detail" }
      ]
    }
  ]
}
```

**已删除的旧字段**：`SpeakerIndex`（硬编码 0）、`NpcResponse` / `NpcResponseOnSuccess` / `NpcResponseOnFail` / `LazyNpcResponse`（台词统一在 Node.NpcLine）、`NextNode`（→ NextNodeOnSuccess）、`ActionValue`（→ ActionParam）。

**旧式 Action 迁移**：`INCREASE_RELATION` → `INTENT:IncreaseRelation`、`DECREASE_RELATION` → `INTENT:DecreaseRelation`、`GIVE_GOLD` → `INTENT:GiveGold`、`TAKE_GOLD` → `INTENT:TakeGold`。数值从 `ActionValue` 迁移到 `ActionParam`。

## 核心 API

```csharp
// JSON 注入
DialogueInjector.InjectFromJson(jsonPath);    // → string 结果描述
// 运行时构建注入（CrimeDialogueBuilder 用）
//   script.SkipVanillaOpening == false → Gateway 模式：在 hero_main_options 挂 PlayerLine 入口
//   script.SkipVanillaOpening == true  → 直挂模式：NPC 台词直接挂在 start token（优先级 200），覆盖原版开场白
DialogueInjector.InjectScript(script, debugLabel);
// 清理
DialogueInjector.ClearAll();
DialogueInjector.RemoveRelatedLines(label);
// 调试
DialogueInjector.LogScript(script, label);
```

> **已删除**：`InjectScriptAsOpening` 已合并到 `InjectScript`。旧代码设 `InjectAtToken = "start"` + 调 `InjectScriptAsOpening`，新代码设 `SkipVanillaOpening = true` + 调 `InjectScript`。`InjectScript` 内部读取 `SkipVanillaOpening` 自动选择 `InjectScriptNoOpening`（直挂）或 `InjectScriptGateway`（入口选项）。

## CrimeDialogueBuilder 辅助方法

```csharp
// Node 工厂
Node(id, npcLine, next=null)         // NPC 说一句 → next 为 null 关窗，非 null 跳转
LazyNode(id, lazyNpcLine, next=null) // 惰性求值版，同上

// Transition 工厂
WalkAway(playerLine)               // INTENT:WalkAway → 关窗
ContinueOptions(walkAwayLine)      // ["我得走了。"→关窗]

// continue_chat
BuildContinueChatNode(r)           // "还有什么别的想说的?" + walk→farewell
BuildFarewellNode(r)               // 惰性告别语（阶段感知）
AddContinueChatWithFarewell(nodes, r) // 同时加两个
```

## CrimeDialogueBuilder 子树自包含原则

**一个函数内构造的 `DialogueNode`，其 `Transitions` 中出现的每一个 `NextNodeOnSuccess` / `NextNodeOnFail`，必须在本函数内有明确的归宿——要么 `nodes.Add()` 创建，要么显式声明复用已有节点。读一个函数就应该能看到完整的对话子图，不需要跳到调用方去拼。**

### 正确 vs 错误

```csharp
// ❌ 错误：BuildConfessNode 定义了 transition → "charm_ok"，
//    但 charm_ok 在本函数内既没有创建也没有声明依赖——
//    读到这里不知道 "charm_ok" 是什么、谁加的、加了没有。
nodes.Add(BuildConfessNode(r, ctx));         // 函数返回就结束了，NextNode 下落不明
nodes.Add(AckNode("charm_ok", ...));         // 目标节点在调用方——跳来跳去拼图
nodes.Add(AckNode("charm_fail", ...));

// ✅ 正确：子树方法内，每个 NextNode 都能在同函数里找到 nodes.Add() 或嵌套子树调用。
BuildConfessSubtree(nodes, r, ctx);          // 一行，所有下游归宿在函数体内可见
```

### 实现方式

**方式 A — 子树方法（推荐）**：`void` 方法，接收 `List<DialogueNode> nodes`。本函数内构造的 Node，其 Transition 指向的每个目标，都在本函数内通过 `nodes.Add()` 或嵌套子树完成注册。

```csharp
static void BuildConfessSubtree(List<DialogueNode> nodes, PlaceholderResolver r, IntentContext ctx)
{
    // 本函数创建的 confess 节点，transition 引用了三个 NextNode：
    //   "charm_ok"      → 下一行 nodes.Add(AckNode(...)) 创建 ✅
    //   "charm_fail"    → 再下一行 nodes.Add(AckNode(...)) 创建 ✅
    //   "restitution_detail" → BuildRestitutionSubtree 内部创建 ✅
    nodes.Add(new DialogueNode { Id = "confess", NpcLine = "...", Transitions = {
        new() { PlayerLine = "我愿意赔。", NextNodeOnSuccess = "restitution_detail" },
        new() { PlayerLine = "开个玩笑…", NextNodeOnSuccess = "charm_ok", NextNodeOnFail = "charm_fail" },
    }});
    nodes.Add(AckNode("charm_ok", "..."));
    nodes.Add(AckNode("charm_fail", "..."));
    BuildRestitutionSubtree(nodes, r, ctx);  // 嵌套子树，restion_detail + pay_ack 在内部创建
}
```

**方式 B — 依赖共享 Node**：目标 Node 是 `continue_chat`、`farewell` 等全局共享节点。本函数不重复创建，但**必须在注释中显式声明依赖**，且调用方通过 `AddContinueChatWithFarewell` 等统一入口添加。

```csharp
/// <summary>证人对话。依赖调用方已添加 continue_chat / farewell（通过 AddContinueChatWithFarewell）。</summary>
static void BuildWitnessScript(...)
{
    // "witness_silence_ack" → next="continue_chat"  ← 共享节点，注释已声明依赖
    nodes.Add(AckNode("witness_silence_ack", "……好吧，我什么也没看见。"));
}
```

### 模式速查

| 模式 | 何时用 | NextNode 归宿在哪 |
|------|--------|-------------------|
| `AckNode(id, line, next)` 工厂 | 单节点，下游是共享节点 | 调用方添加（`next` 默认 `"continue_chat"`） |
| `TerminalNode(id, line)` 工厂 | 单节点无 transition | 无 NextNode，自包含 |
| `BuildXxxSubtree(nodes, ...)` void | 节点有复杂 transition | **全部在本函数内** `nodes.Add()` 或嵌套子树 |
| `BuildXxxSubtree(nodes, ...)` void 共用 | 被多个子树复用 | **全部在本函数内**，调用方不感知细节 |
| ❌ `BuildXxxTransitions(...)` 返回 `List<DialogueTransition>` | — | **禁止**：返回的 Transition 引用外部 Node Id，对调用方有隐式依赖（见下方反模式） |

### ❌ 反模式：返回裸 Transition 列表

**"当前函数" = 构造 Transition 的函数**（`new DialogueTransition { NextNodeOnSuccess = "xxx" }` 写在哪，哪就是当前函数）。这与返回的是 Node 还是 `List<DialogueTransition>` 无关——**只看谁写了 `NextNodeOnSuccess`/`NextNodeOnFail` 的字面值，谁就必须能兑现。**

唯一合法形式：`void BuildXxxSubtree(List<DialogueNode> nodes, ...)`。Transition 构造和 Node 定义在同一函数内闭环。

### 新增/修改对话构建方法时自查

1. **本函数内每个 `new DialogueTransition { NextNodeOnSuccess = "xxx" }`**，**"xxx" 能在本函数体内找到 `nodes.Add()` 或嵌套 `BuildXxxSubtree(nodes, ...)` 吗？** 找不到 → **违规**。
2. 如果有共享依赖（如 `continue_chat`），本函数的注释里**显式声明**了吗？

## AckNode 使用纪律：禁止无意义"…"拆句

**`AckNode`（NPC 说一句 → 玩家点"…" → 跳到 next）只能用于收束对话分支，禁止用来把同一段 NPC 发言拆成两个气泡。**

### AckNode 的合法用途

| 场景 | 示例 | 为什么合法 |
|------|------|-----------|
| 分支收束 → 闲聊 | `AckNode("xxx_ack", "知道了。")` → `continue_chat` | 玩家做了选择，NPC 给了最终回应，对话自然收束 |
| 分支收束 → 告别 | `AckNode("xxx_ack", "好，去吧。")` → `farewell` | 同上 |
| 信息确认后继续 | `AckNode("witness_desc_ack", "那人……高个子，红头发。")` → `continue_chat` | 玩家问了具体问题，NPC 回答，合理停顿 |

### ❌ 非法用法：用"…"当胶水

```csharp
// ❌ 同一段 NPC 发言被 "…" 拆成两句——玩家要多点一次，纯摩擦
nodes.Add(AckNode("confess_ack", "你？！……好，既然认了，咱们可以商量。", "confess"));
nodes.Add(BuildConfessNode(r, ctx));  // NpcLine = "有什么要说的？"

// 实机体验：
//   NPC: "你？！……好，既然认了，咱们可以商量。"    ← 情绪反应
//   玩家: "…"                                      ← 无意义点击
//   NPC: "有什么要说的？"                            ← 本应和上一句连在一起
//   玩家: ①赔钱 ②狡辩 ③走人                         ← 终于有选择了
```

### ✅ 修复：合并为一个节点

```csharp
// NPC 的情绪反应和提问在同一句 NpcLine 里，直接给玩家选项
nodes.Add(new DialogueNode {
    Id = "confess",
    NpcLine = "你？！……好，既然认了，咱们可以商量。有什么要说的？",
    Transitions = { 赔钱 / 狡辩 / 走人 }
});
// 实机体验：
//   NPC: "你？！……好，既然认了，咱们可以商量。有什么要说的？"
//   玩家: ①赔钱 ②狡辩 ③走人                         ← 直接选
```

### 自查

给对话图加 AckNode 时问自己：

1. **这个 AckNode 的 next 指向 `continue_chat` / `farewell` / 关窗吗？** → 如果不是（比如指向另一个有实质内容的 node），**大概率是非法拆句**。
2. **能把 AckNode 的 NpcLine 和 next 指向的那个 Node 的 NpcLine 合并成一句吗？** → 如果能合并且不失自然，**就不该拆**。
3. **这个"…"是玩家在确认收到信息（OK），还是 NPC 还没说完话？** → 前者合法，后者必须合并。

## Transition 检定纪律：影响 NPC 决策的选项必须有 SkillCheck

**凡是玩家试图影响 NPC 决策的选项——说服、贿赂、威胁、欺骗、讨价还价——必须加 `CheckType = SkillCheck`，禁止写死 NPC 必然接受。玩家不能靠点一个选项就无代价地改变 NPC 行为。**

### 需要检定的典型场景

| 玩家行为 | 对应 Intent 示例 | 不检定的后果 |
|----------|-----------------|-------------|
| 给钱封口 | `INTENT:SilenceWitness` + `ActionParam="bribe"` | 花 50 块钱就能让所有目击者闭嘴——零风险零难度 |
| 威胁恐吓 | `INTENT:SilenceWitness` + `ActionParam="threat"` | 威胁不需要魅力/威慑力，点就有效 |
| 花言巧语开脱 | `INTENT:CharmDefense` | NPC 永远吃这套，毫无挑战 |
| 栽赃陷害 | `INTENT:FrameSuspect` | 随便指一个人 NPC 就信 |

### 正确 vs 错误

```csharp
// ❌ 错误：写死了 NPC 必然接受——贿赂变成免费午餐
new DialogueInjector.DialogueTransition
{
    PlayerLine = "（给些钱）这事你别往外说……",
    Action = "INTENT:SilenceWitness",
    NextNodeOnSuccess = "witness_silence_ack"   // 无检定，必然成功
    // 缺 CheckType、NextNodeOnFail
}

// ✅ 正确：检定决定 NPC 是否被说服，失败有对应的 NPC 回应
new DialogueInjector.DialogueTransition
{
    PlayerLine = "（给些钱）这事你别往外说……",
    CheckType = TransitionCheckType.SkillCheck,   // 检定决定成败
    Action = "INTENT:SilenceWitness",
    ActionParam = "bribe",
    NextNodeOnSuccess = "witness_silence_ack",    // NPC 收了钱
    NextNodeOnFail = "witness_silence_fail"       // NPC 拒绝并扬言举报
}
```

### 不需要检定的例外

| 场景 | 理由 |
|------|------|
| 玩家表示"我先想想"/"我还有事" | 玩家不做决策，只是推迟/离开 |
| NPC 主动提出的交易（悬赏等） | NPC 已经决定，玩家只是接受/不接受 |
| 玩家认栽自首 | 玩家放弃抵抗，NPC 接受是合理的 |
| 无关紧要的信息询问（"详细说说？"） | 不涉及 NPC 利益权衡 |

### 自查

新增 Transition 时问自己：

1. **这个选项是在改变 NPC 的意愿吗？** → 如果是（让他闭嘴、相信你、原谅你、配合你），**必须检定**。
2. **检定失败 NPC 会说什么？** → 必须提供 `NextNodeOnFail` 指向的节点，NPC 拒绝 + 可能有后果。
3. **相关的 Intent 有 Goal 吗？** → 没有 Goal 的 Intent 不会掷骰，检查 `Intent.Goal` 是否已配置。

## Intent Tactic 必须响应 ActionParam——不同手段不能共用同一技能

**同一个 Intent 被多次复用时（通过 `ActionParam` 区分手段），其 `Tactic` 和 `GetOfferValue` 必须根据 `ActionParam` 动态选择。禁止所有手段共用同一个固定的 `Tactic`。**

### 问题场景

```csharp
// ❌ 对话层区分了手段，但 Intent 层不区分
// 对话："（给些钱）…"→ActionParam="bribe"  /  "（威胁）…"→ActionParam="threat"
// Intent：两者都走 Tactic = Flatter → Charm 检定
// 结果：拿刀威胁目击者 → 魅力检定。这说不通。

public class SilenceWitnessIntent : IntentBase
{
    public override NegotiationTactic Tactic => NegotiationTactic.Flatter; // 写死
    // 没有 override GetOfferValue → 永远返回 0f
}
```

**为什么这样不对**：
- 塞钱封口 → 应该看玩家出价是否够高（`GetOfferValue`）
- 拿刀威胁 → 应该看玩家流氓习气（`Tactic = Threaten → Roguery`）
- 写死 `Flatter` + `GetOfferValue = 0` → 两个完全不同的手段，变成了同一个魅力检定

### 正确做法

利用 `Evaluate` 先于检定执行的事实，在 `Evaluate` 中根据 `ctx.ActionParam` 缓存状态，`Tactic` 和 `GetOfferValue` 返回缓存值：

```csharp
public class SilenceWitnessIntent : IntentBase
{
    private NegotiationTactic _tactic = NegotiationTactic.Flatter;
    private float _offerValue = 0f;

    public override NegotiationTactic Tactic => _tactic;
    public override float GetOfferValue(IntentContext ctx) => _offerValue;

    public override Eligibility Evaluate(IntentContext ctx)
    {
        // ... 基础 eligibility 检查 ...

        switch (ctx.ActionParam)
        {
            case "bribe":
                _tactic = NegotiationTactic.Bribe;       // 贿赂 → Charm
                _offerValue = 0.3f;                      // 小额献礼加成
                break;
            case "threat":
                _tactic = NegotiationTactic.Threaten;     // 威胁 → Roguery
                _offerValue = 0f;                         // 威胁不靠出价
                break;
            default:
                _tactic = NegotiationTactic.Flatter;
                _offerValue = 0f;
                break;
        }
        return Eligibility.Show();
    }
}
```

### 调用时序保证

`Evaluate` → `Tactic` / `GetOfferValue` → `SimpleCompute` 的执行顺序是有保证的：

```
DialogueInjector.ExecuteIntentAction():
  Evaluate(ctx)           ← 第一步：缓存 _tactic / _offerValue
  intent.Tactic           ← 第二步：读取缓存值（传给 SimpleCompute）
  intent.GetOfferValue()  ← 第二步：读取缓存值
  SimpleCompute(...)      ← 第三步：公式计算
```

### 自查

新增/修改复用了 `ActionParam` 的 Intent 时问自己：

1. **不同 ActionParam 对应不同的玩家手段吗？** → 如果是（给钱 vs 威胁 vs 说服），**Tactic 必须不同**。
2. **涉及金钱/物品付出的手段有 `GetOfferValue` 吗？** → 如果没有，出价不参与检定，玩家付多付少没区别。
3. **默认分支（ActionParam == null）的 Tactic 合理吗？** → 必须有合理兜底。

## 控制台指令

```
custom.inject_dialogue test_talk       → 加载并注入 test_talk.json
custom.inject_dialogue clear           → 清除所有注入
```

**文件位置**：`Interaction/Dialogue/DialogueInjector.cs`（注入引擎）、`Interaction/Dialogue/CrimeDialogueBuilder.cs`（运行时构建器）、`Interaction/Intents/SystemIntents.cs`（系统 Intent）。JSON 示例：`ModuleData/DesignData/Dialogues/test_talk.json`。

---

# 🆕 意图/行动/任务统一重构（2026-07-04 新增）

## IntentBase 新 API — NPC 与玩家平权

```csharp
// 意图来源（Player / Npc / Both）
public virtual IntentSource Source => IntentSource.Both;

// NPC 意图响应的事件类型白名单
public virtual string[] TriggerEvents => Array.Empty<string>();

// 深度事件匹配（EventType 匹配后，检查 Args 是否满足条件）
public virtual bool CanHandle(AIEvent aiEvent, IntentContext ctx) => true;

// OnInstant / OnSuccess / OnFail 不变，保留 imperative 风格
public virtual void OnInstant(IntentContext ctx) { }
public virtual void OnSuccess(IntentContext ctx) { }
public virtual void OnFail(IntentContext ctx) { /* 基类默认：掉好感 + 进冷却 */ }


**文件位置**：`Interaction/Intents/IntentBase.cs`

## IntentContext.BuildForNpc — NPC 视角上下文

```csharp
// NPC 视角构建：NPC 发起意图时的上下文（交互目标是玩家）
var ctx = IntentContext.BuildForNpc(npcAgent, npcHero);
// 返回 null = 无法发起意图（无 Agent 也无 Hero）
// ctx.NpcLevel: None / AgentOnly（仅 Mission 行为）/ Full（有 Hero，完整功能）
```

**文件位置**：`Interaction/Intents/IntentContext.cs`

## IntentRegistry 新方法 — NPC 意图查询

```csharp
// 取 NPC 可发起的意图（Source 含 Npc 标志，且 Evaluate 通过）
IntentRegistry.GetNpcInitiatives(ctx);

// 按类名查找 NPC 意图
IntentRegistry.FindNpcIntent("GuardInterceptIntent");
```

**文件位置**：`Interaction/Intents/IntentRegistry.cs`

## AgentBrain 新事件分发 — IntentRegistry 优先 + 兜底

```csharp
// ReceiveEvent 改造：先查 IntentRegistry（MatchesEvent 两层匹配）
// → 命中 → intent.OnInstant(ctx) → AgentBrain 入队 IAtomicAction
// → 未命中 → HandleLegacyAtomicAction（旧 if/else 兜底）

// MatchesEvent：① TriggerEvents 白名单 ② intent.CanHandle(aiEvent, ctx) 深度匹配
```

**文件位置**：`AI/AgentBrain.cs`

## 新建 NPC 意图类 — NpcInitiativeIntents.cs

7 个 NPC 主动意图类，按 IntentBase 格式：`NewsConflictIntent` / `GuardInterceptIntent` / `CrimeAccusationIntent` / `RevengeIntent` / `GreetingIntent`（Both）/ `OfficialBusinessIntent` / `CrushIntent`

每个实现 `TriggerEvents` + `CanHandle` + `OnInstant`（创建 PrepareOpeningAction）。

**文件位置**：`Interaction/Intents/NpcInitiativeIntents.cs`


## InteractionOptionType 扩展 — 追责类型合并

```csharp
// 新增 14 个 InteractionOptionType 值（从 AccountabilityOptionType 迁移）：
PayRestitution / CharmDefense / FrameSuspect / Threat / Investigate / Confess /
SilenceWitness / LeadRetaliation /
BetrayQuest / InnocenceProof / Settle / AcceptBountyQuest / LureArrest / Arrest
// （WorkOffDebt 干活抵债已于 2026-08-03 删除——入口从未接入对话，属死代码，勿复活）

// 新增 InteractionCategory.Accountability
// AccountabilityOptionType 枚举已删除
// InteractionOptionCategoryMap 已补全新分类映射
```

**文件位置**：`Interaction/InteractionOptionManager.cs` / `Interaction/Intents/AccountabilityIntents.cs`

## SettlementHonorStore — 独立文件

从 `InteractionOptionManager.cs` 末尾抽出到独立文件 `Interaction/SettlementHonorStore.cs`（纯数据存储，与交互管理解耦）。

## 叙事迁移 — QuestManager 硬编码字串清理

`QuestManager.GetQuestDescription()` 的 ~120 行日本战国硬编码字串已替换为通用简化描述。`GetQuestTitle()` 同步清理。叙事全部走 `NarrativeResolver` → CSV 管道。

---

# 🆕 NPC 警戒值系统 — 三级响应（2026-07-07）

## 类型定义 — `AI/AlertTypes.cs`

```csharp
// 玩家行为分类（警戒值累加维度）
public enum PlayerActionType { Crouching, WeaponDrawn, StealUIOpen, Steal, AttackAlly, Knockout }
// 警戒阶段（UI 颜色 + NPC 行为分级）
public enum AlarmPhase { Normal, Suspicious, Cautious, Alarmed }
// L3 质问意图
public enum NpcInterceptIntent { Deter, Search, Recover, Stop }
// 对话模式开关
public enum AlertDialogueMode { StoryVM, VanillaConversation }
// 警戒条目（值 + 脉冲上下文）
public struct AlertEntry { float Value; string TargetName; string ItemName; }
```

**文件位置**：`AI/AlertTypes.cs`

## AgentBrain 警戒值字段与方法 — `AI/AgentBrain.cs`

警戒值状态从 `NpcSightSystem` 迁移到每个 `AgentBrain` 实例。每个 NPC 独立维护自己对玩家的警戒值明细。

```csharp
// ── 公开查询 ──
brain.AlertValue     // float — 所有条目的总和
brain.AlertPhase     // AlarmPhase — 由 AlertValue 自动计算
brain.PrimaryAction  // PlayerActionType? — 当前最高警戒值的来源

// ── 脉冲操作 ──
brain.AddAlert(PlayerActionType.Steal, 2.0f);  // 加值（持续累加或脉冲）

// ── BubbleSay ──
brain.BubbleSay("文本");  // 通用冒泡说话入口
```

**认知更新**：节流循环（默认 100ms），`Tick` → `UpdateAlertCognition` → 可见→累加 / 不可见→按比例衰减 → `CheckPhaseTransition` → 阶段穿越发事件。

**阶段穿越事件**（在 `ReceiveEvent` 中平级处理）：
- `"BecomeSuspicious"` → `BubbleSayOnce`
- `"BecomeCautious"` → `LookAtAction(Agent.Main, 2.0f)` + `BubbleSayOnce`
- `"BecomeAlarmed"` → `StartL3Confrontation()`（脉冲抑制检查）
- `"CalmDown"` → 清理 bubbled 记录 + 行为链清理

**L3 质问**：`StartL3Confrontation` 按 `Settings.Instance.AlertDialogueMode` 分叉：
- `StoryVM`（默认）→ `PrepareOpeningAction` → `ForceTalkAction` → StoryDialogVM
- `VanillaConversation` → `AlertForceConversationAction` → `CrimeDialogueBuilder.BuildAlertInterceptScript` → `DialogueInjector.InjectScript` → 原版对话 UI

**`WitnessCrime_GatherOnLook` 犯罪类型分类**（`ProcessEvent` 中，criminal==玩家时）：
1. `IsKnockedOut(victim)` → `PlayerActionType.Knockout` + `ConfrontationType.Stop`
2. `CombatManager.IsAgentFightingPlayer(victim)` 或 `IsPlayerInCombat` → `PlayerActionType.AttackAlly` + `ConfrontationType.Stop`（斗殴，非偷窃）
3. 其余 → `PlayerActionType.Steal` + `ConfrontationType.Recover`（兜底：偷窃）

**文件位置**：`AI/AgentBrain.cs`（新增约 250 行警戒相关代码）

## NpcSpeech.csv + NpcSpeechResolver — 模板台词统一数据源

模板思路替代枚举思路。极简三列 `ID,Template,Emotion`。
**两阶段回落**：`NpcSpeechResolver.Resolve(id, speaker, listener, evt, targetName, itemName, narrativeFallback)` 内部先查 NpcSpeech.csv → 未命中自动回落 Narrative.csv（过渡期）→ 均未命中返回 null。**调用方只需 `??` 硬编码兜底**，不应再手动调 NarrativeResolver。

```csharp
// ✅ 调用方标准写法：两阶段回落
string line = NpcSpeechResolver.Resolve(templateId, speaker, listener,
    narrativeFallback: new NarrativeFilters { ... })
    ?? HardcodedFallback(r, intent, action);

// ❌ 禁止：调用方手动写三层 if-null 回落
// ❌ 禁止：调用方直接调 NarrativeResolver.Resolve/NarrativeResolver.TryResolveText
```

**长期方向**：Narrative.csv 逐步迁移到 NpcSpeech.csv 后，`narrativeFallback` 参数和内部回落代码删除，`NpcSpeechResolver` 回归纯 CSV 查询薄层。

**文件位置**：
- `ModuleData/DesignData/NpcSpeech.csv`（~18 行：12 BubbleSay + 6 L3 开场白）
- `Interaction/Dialogue/NpcSpeechResolver.cs`

## PlaceholderResolver 增强 — Mission 层脉冲上下文

新增构造参数 `targetName`/`itemName`，新增占位符：
- `{PLAYER}` / `{SPEAKER}` / `{SPEAKER_SELF}` / `{SPEAKER_PLAYER_ADDR}` / `{SPEAKER_EMOTION}`
- `{TARGET}` / `{ITEM}` / `{StolenItemName}` / `{LOCATION}`

**文件位置**：`Interaction/Dialogue/PlaceholderResolver.cs`

## PlaceholderResolver 扩展指南 — 新增占位符两步流程

**调用链路**：`NpcSpeechResolver.Resolve(id, speaker, listener, evt, targetName, itemName, narrativeFallback)` → ① 查 `NpcSpeech.csv` 取模板文本 → ② 未命中自动回落 `NarrativeResolver.TryResolveText(narrativeFallback)`（过渡期） → `new PlaceholderResolver(...)` → `r.Resolve(template)` → 正则 `\{(\w+)\}` 扫描 `{KEY}` → 逐个调 `ResolveOne(key)` 替换。

**三种构造 → 三种数据可用范围**：

| 构造 | 使用场景 | Speaker/Listener | TargetName/ItemName | WorldEvent |
|------|---------|:-:|:-:|:-:|
| `(speaker, listener, targetName, itemName)` | 警戒 BubbleSay | ✅ | ✅ | ❌ null |
| `(evt, speaker, listener)` | Campaign 犯罪对话 | ✅ | ❌ null | ✅ |
| `(evt, speaker, listener, targetName, itemName)` | L3 质问台词 | ✅ | ✅ | ✅ |

**新增占位符两步**：

1. **`ResolveOne` 加 case**（[PlaceholderResolver.cs:94](Interaction/Dialogue/PlaceholderResolver.cs:94)）：在 `switch (key)` 中添加 `case "NEW_KEY": return ...;`。注意判断数据来源是否可能为 null（`evt?.` / `TargetName ?? ""`）。
2. **`NpcSpeech.csv` 用上**：在模板文本中写入 `{NEW_KEY}`，`Resolve` 自动替换。

**关键守卫**：`ResolveOne` 返回 `null` 时，正则替换**保留原样 `{KEY}`**（玩家会看到原始占位符 = bug）。新增占位符后务必在对应场景实测，确保不会走到 `default: return null`。

## AlertForceConversationAction — L3 路径 B 原子 Action

走到玩家面前后强制开启原版对话。**不再自己调 `InjectScript`**，只设 `_pendingTrigger = DialogueTrigger.Alert` + 调 `StartConversation`，由 `MissionConversationStartPatch.Prefix` 统一注入。

```csharp
// 用法（AgentBrain 内部）：
EnqueueAction(new AlertForceConversationAction());
// OnStart 中自动：查 brain.PrimaryAction → 确定 ConfrontationType + PlayerActionType
// → 设 _pendingTrigger/Confrontation/TriggerAction → StartConversation
// → Prefix 触发 TryInjectCrimeDialogue → BuildScript(trigger=Alert) → InjectScriptNoOpening
```

**文件位置**：`AI/Actions/AtomicAction.cs`（新增在文件末尾）

## CrimeDialogueBuilder.BuildAlertInterceptScript — L3 质问对话构建

与 `BuildAuthorityScript` / `BuildWitnessScript` 同属 `CrimeDialogueBuilder`。
台词通过 `NpcSpeechResolver.Resolve(..., narrativeFallback:)` 内部两阶段回落（NpcSpeech.csv → Narrative.csv），调用方仅 `?? HardcodedAlertLine()` 兜底。

```csharp
var script = CrimeDialogueBuilder.BuildAlertInterceptScript(
    speaker, NpcInterceptIntent.Recover, PlayerActionType.Steal);
if (script != null) DialogueInjector.InjectScript(script, "AlertL3_NpcName");
```

**文件位置**：`Interaction/Dialogue/CrimeDialogueBuilder.cs`（新增约 200 行）

## 控制台调试指令 — `Debug/MyCommands.cs`

```
custom.alert_status [agentStringId]    # 查看 NPC 分类警戒值明细
custom.alert_force_intercept <npcId>   # 强制触发 L3 质问
custom.alert_dialogue_mode <mode>      # StoryVM / Vanilla
```

## Settings 新增开关

```csharp
Settings.Instance.AlertDialogueMode  // AlertDialogueMode — StoryVM（默认）或 VanillaConversation
```

**文件位置**：`Core/Settings.cs`

## NpcSightSystem 清理

旧 `_alertValues` 字典、`GetAlertValue`、`AddAlertPulse`、`GetAllAlertValues`、`UpdateAlertValue`、`CleanupDeadAlertEntries` 全部删除。`NpcSightSystem` 回归纯感知工具——只回答"能不能看到"，不维护认知状态。

**文件位置**：`AI/NpcSightSystem.cs`（删除约 100 行警戒值相关代码）

## 版本兼容三锚点 — `Core/VersionCompat.cs`（🔴 加新 API 前必查）

**问题**：骑砍 2 API 在 1.2.12 / 1.3.x / 1.4.x 三个版本段有**三种形态**——有些 1.3.x 与 1.4.x 一致（绝大多数），
有些 1.3.x 与 1.2.12 一致（如 `IssueBase.CanPlayerTakeQuestConditions`），有些 1.3.x 是**独有形态**
（如 `SetPartyAiAction.GetActionForRaidingSettlement`：1.3.x=4参、1.4.x=5参）。**没有 1.3.x 的 DLL 之前这些差异不可见。**

**三锚点**（反编译对比基线）：

| 锚点 | DLL 来源 | 用途 |
|------|---------|------|
| v1.2.12 | `Modules/1.2.12DLL/` | 旧 API 签名 |
| v1.3.15 | `Modules/1.3.15DLL/`（或当前游戏目录） | 中间形态签名 |
| v1.4.6 | `Modules/1.4.6DLL/` | Latest 签名（=1.4.7） |

**🔴 铁律：遇到"1.3.x 与 1.2.12 相同、1.4.x 不同"的 API，必须写 `MB2_GE_140` 三分支**，不能沿用 `!MB2_V1212` 二分——
override 签名不匹配基类会直接编译失败（踩过：`CommissionHubIssue.CanPlayerTakeQuestConditions`）。

```csharp
// ✅ 三分支范式（阈值从高到低）
#if MB2_GE_140
    SetPartyAiAction.GetActionForRaidingSettlement(party, settlement, NavigationType.Default, false, false); // 1.4.x: 5参
#elif MB2_GE_130
    SetPartyAiAction.GetActionForRaidingSettlement(party, settlement, NavigationType.Default, false);        // 1.3.x: 4参
#else
    SetPartyAiAction.GetActionForRaidingSettlement(party, settlement);                                        // 1.2.12: 2参
#endif
// ❌ 禁止：用 !MB2_V1212 二分处理 1.3.x/1.4.x 有差异的 override
```

**已确认的三版本差异清单**（2026-08-03 反编译验证，详表见 `plans/version-compat-plan.md`「三锚点验证结论」）：
- `SetPartyAiAction.GetActionForRaidingSettlement`：2参 / **4参** / 5参（唯一需要 `MB2_GE_140` 的 V 方法）
- `IssueBase.CanPlayerTakeQuestConditions`：4参 / 4参 / **5参**（唯一需要 `MB2_GE_140` 的 override）
- 其余 25 个 V 方法 + 13 处注册表 #if：1.3.15 与 1.4.6 完全一致，`MB2_GE_130`/`MB2_V1212` 分支正确

**反编译验证流程**（给签名下结论前先跑）：
```bash
# 1. 找类型全名（全名可能藏命名空间，如 MobileParty 是 TaleWorlds.CampaignSystem.Party.MobileParty）
ilspycmd <dll> -l c | grep -i <类型名>
# 2. 反编译单个类型（⚠️ -t 一次只能一个类型，多传整体失败输出 "Specify --help"）
ilspycmd <dll> -t <全名> | grep "<方法名>"
# 3. 三个版本对比（1.3.15 用游戏目录或 Modules/1.3.15DLL/）
# 缓存：Modules/decompile/<版本>/<类型名>.cs 已入库，对比前先查缓存
```

**文件位置**：`Core/VersionCompat.cs`（V 方法全部差异）；`ExampleModVS/ExampleMod/ExampleMod.csproj`（版本宏自动侦测）；详细策略 `plans/version-compat-plan.md`。
