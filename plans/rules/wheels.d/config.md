# config — 轮子速查分卷（wheels.md 索引导航）
## 配置与世界观 — `Core/Settings.cs`

全局配置单例 + 世界观参数化。

```csharp
Settings.Instance.IsLLMConfigured           // LLM 总闸（三字段非空才 true）
Settings.Instance.LLMBaseUrl/LLMApiKey/LLMModel
Settings.Instance.WorldDescription     // 默认卡拉迪亚，TaikouContent 注入战国
Settings.Instance.EraDescription / SpeechStyle / WarriorTerms / FemaleSelfAddress
Settings.Reload();                     // 重载 config.json
```

世界观相关字串**只能**从这里取，禁止硬编码（见 [worldview.md](worldview.md)）。需要新 flavor 字段就往 Settings 加，默认值给卡拉迪亚版。


---

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

    // ⚠️ 排序坑（见下方「MCM 设置页排序与渲染」）：列表显示顺序 = Order 排序的倒序
    // （VerticalBottomToTop 从底往上渲染）。期望显示 地址→密钥→…，Order 必须反着赋
    // （回血=0 … 地址=4）。所有设置归入单组，避免 footer 式标题错位。
    [SettingPropertyText("{=LWN_mcm_llm_base_url}LLM API Base URL", Order = 4, RequireRestart = false, HintText = "{=LWN_mcm_llm_base_url_hint}...")]
    [SettingPropertyGroup("{=LWN_mcm_grp_main}Settings")]  // 全 mod 单组（LWN_mcm_grp_main）
    public string LLMBaseUrl { get => Settings.Instance.LLMBaseUrl; set => Settings.Instance.LLMBaseUrl = value; }  // facade 透传核心 Settings
}
```

- **加新 UI 配置项** = 加一个 `[SettingPropertyXxx]` 属性（v2 特性：Bool/Integer/FloatingInteger/Dropdown/Text/Button + Group 支持 `"组/子组"` 嵌套，下拉用 `MCM.Common.Dropdown<T>`），显示名用 `{=LWN_mcm_*}`（铁律 13），**再补** `std_LivingWorldNpcs_strings.xml`（英）+ `CNs/`（中）条目。
- **MCM 特性参数是编译期常量**：显示名只能写 `{=KEY}fallback` 字面量（引擎显示时查表），不能调 `LWNTextHelper`。
- **MCM json 只序列化带 `[SettingProperty]` 的属性**（`BaseSettingsJsonConverter` 遍历 `GetAllSettingPropertyDefinitions`）——隐藏字段放 MCMSettings 既不显示也不存盘（数据丢），隐藏变量必须留 config.json。
- **`MCMSettings.Instance` 在 MCM 注册前为 null**（`GlobalSettings<T>.Instance` 查容器），业务代码禁止读它，一律走 `Settings.Instance`（永不 null，铁律 1 保障）。LLM 三字段已 `[JsonIgnore]` 切断 config.json 侧（唯一来源 = MCM UI），新增玩家可配置字段照此办理。
- 🔴 **MCM 设置页排序与渲染（v5.11.4 + 游戏 v1.4.7 实测，反编译 MCMv5.dll / Bannerlord.MBOptionScreen.v1.4.1.dll / 嵌入 prefab 验证）**：
  - **所有列表 `VerticalBottomToTop` 从底往上渲染** → 显示顺序 = Order 排序结果的**倒序**（组列表和组内设置列表都是）。想要显示 地址→密钥，`Order` 必须反着赋（本项目：击杀回血=0 … LLM 地址=4）。
  - **组标题是 footer 式**：每组渲染 = `[正文][分隔行][标题]`，标题在分隔行下方（SettingsPropertyGroupView.xml 的 child 顺序 [标题][分隔行][正文] 被倒序布局翻转为 [正文][分隔行][标题]）。标题永远紧贴**下一组**的正文 → 多组 + 单设置组时标题看起来属于别的组，`GroupOrder` 怎么排都救不了。
  - **组排序两层方向相反**：MCMv5 `SortDefault` = 组 Order **降序** + 组名降序；UI 层 `SettingsPropertyGroupVMComparer` = 组 Order **升序** + 组名升序。实测生效的是「UI 升序 + 列表倒序渲染」= 净效果**降序**（GroupOrder 数值大的显示靠前）。
  - **不设 GroupOrder = 全部平局** → 退化为本地化组名排序（AlphanumComparatorFast，ordinal 码点）→ 中文组名（战斗>世界事件>LLM 配置）乱序，曾把击杀回血顶到最上面、LLM 沉底。
  - **结论**：本项目 5 项设置全部归入单组 `LWN_mcm_grp_main`（消除标题错位），`Order` 按显示倒序赋值。若未来拆组：`GroupOrder` 升序 + 显示倒序 = **数值大的组显示靠前**（LLM=2 → 最上）。

**文件位置**：`Core/MCMSettings.cs`（MCM 设置页）、`Core/Settings.cs`（config.json 内部源）。csproj 引用：`<Reference Include="MCMv5">` → `$(MB2_PATH)\Modules\Bannerlord.MBOptionScreen\bin\Win64_Shipping_Client\MCMv5.dll`（`Private=False`，各锚点电脑必须装 MBOptionScreen——SubModule.xml 已声明硬依赖）。


---

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


---

## 设计数据加载 — `Data/DesignDataLoad.cs`

通用 CSV ORM。**新增可配置设计数据走 CSV，不要硬编码进 .cs**。

```csharp
DataTable t = GameDatabase.Heroes;            // 已有表：Heroes/Music/TagPoint/Camera/Emotion
DynamicRecord r = t.GetByID("hero_001");      // 按英文 ID
DynamicRecord r = t.GetByScriptName("某中文名"); // 按中文名（反向索引）
r.GetString(key)/GetInt(key)/GetFloat(key)/GetBool(key)/GetList(key);
GameDatabase.Initialize();                    // Mod A 启动时
GameDatabase.LoadTablesFromPath(path);        // 内容包注入入口（TaikouContent 用）
```


---

## Emotion ↔ 台词模板一致性 — 模板里写 `{SPEAKER_EMOTION}` 占位符

**CSV 时代的 `NpcSpeech.csv` Emotion 外键校验已随迁移删除**（`GameDatabase.NpcSpeech` 表已不存在）。现在 `LWN_speech_*` XML 模板里的情绪统一写 `{SPEAKER_EMOTION}` 占位符，由 `PlaceholderResolver.ResolveOne("SpeakerEmotion")` 运行时按说话者情绪解析（`PlaceholderResolver.cs:133,338`）——模板不写死 emotion ID，天然不会引用未定义的 emotion，无需加载时校验。

```xml
<string id="LWN_speech_l3_deter_weapondrawn" text="*{SPEAKER_EMOTION}* Put that {ITEM} away! ..." />
```

**动作存在性校验（已知限制）**：`Emotion.csv` 中的 `Animations` 列（如 `act_conversation_threat_body`）由 `AgentControlHelper.ForcePlayAction` 播放。但动画是否真正可用取决于 Agent 的 `action_set`——平民、守卫、儿童各自继承了不同的 action_set，部分动画可能不存在于当前 Agent 的 action_set 中导致静默失败。**目前无编译时或加载时校验手段**（action_set 是 C++ native 层，C# 层只能 try-catch 运行时错误）。对策：① Emotion 只用已验证可用的动画 ID（参见 `Knowledge/击晕机制_引擎能力与实现踩坑.md` 的 action_set 继承链分析）；② `ForcePlayAction` 内部已有临时切换 `as_human_warrior` 的绕过逻辑；③ 新增动画前在实际游戏中验证。


---

## 日志 — `Debug/DebugLogger.cs`

```csharp
DebugLogger.Log("消息");   // 线程安全，落盘到 Configs/StoryEngine_RuntimeLog.txt
```


---

## 控制台调试指令 — `Debug/MyCommands.cs`

```
custom.alert_status [agentStringId]    # 查看 NPC 分类警戒值明细
custom.alert_force_intercept <npcId>   # 强制触发 L3 质问
custom.alert_dialogue_mode <mode>      # StoryVM / Vanilla
```


---

## Settings 新增开关

```csharp
Settings.Instance.AlertDialogueMode  // AlertDialogueMode — StoryVM（默认）或 VanillaConversation
```

**文件位置**：`Core/Settings.cs`


---

## 版本兼容三锚点 — `Core/VersionCompat.cs`（🔴 加新 API 前必查）

**问题**：骑砍 2 API 在 1.2.12 / 1.3.x / 1.4.x 三个版本段有**三种形态**——有些 1.3.x 与 1.4.x 一致（绝大多数），
有些 1.3.x 与 1.2.12 一致（如 `IssueBase.CanPlayerTakeQuestConditions`），有些 1.3.x 是**独有形态**
（如 `SetPartyAiAction.GetActionForRaidingSettlement`：1.3.x=4参、1.4.x=5参）。**没有 1.3.x 的 DLL 之前这些差异不可见。**

**三锚点**（反编译对比基线）：

| 锚点 | DLL 来源 | 用途 |
|------|---------|------|
| v1.2.12 | `Modules/1.2.12DLL/` | 旧 API 签名 |
| v1.3.15 | `Modules/1.3.15DLL/`（或当前游戏目录） | 中间形态签名 |
| v1.4.6 | `Modules/1.4.6DLL/` | Latest 签名（=1.4.8，本机实测） |

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
