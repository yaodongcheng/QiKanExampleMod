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

**🔴 注册签名铁律（2026-08-31 实机教训）**：引擎委托是 `Func<List<string>, string>`——带 `[CommandLineFunctionality.CommandLineArgumentFunction("x", "custom")]` 的方法必须写：

```csharp
public static string Xxx(List<string> args)   // ✅ 唯一合法签名（1.2.12 ~ 1.5.x 反编译一致）
```

```csharp
public static string Xxx(string[] args)       // ❌ 编译能过，启动必崩
```

- **为什么崩**：`CommandLineFunctionality.CollectCommandLineFunctions()` 启动时反射扫描**所有**已加载程序集里的特性方法，逐个 `Delegate.CreateDelegate(typeof(Func<List<string>, string>), methodInfo)`——签名不匹配 → `ArgumentException: Cannot bind to the target method...` → 整场启动直接炸（与 Harmony 字符串补丁同理：**无编译期检查**）。
- **自查**：新增/修改指令后 grep `string\[\] args`，命中必改。
- **调用范例**：`args.Count`（不是 `args.Length`）、`string.Join(" ", args)`。
- **已用位置**：`Debug/MyCommands.cs`（70+ 指令）、`Scenario/ScenarioCommands.cs`（scn_*/playback_*/dsl_*）、`Story/StoryEngine.cs`、`Quests/QuestManager.cs` 等，全库同款签名；控制台名 = `{GroupName}.{Name}`（如 `custom.scn_list`）。

```
custom.alert_status [agentStringId]    # 查看 NPC 分类警戒值明细
custom.alert_force_intercept <npcId>   # 强制触发 L3 质问
custom.alert_dialogue_mode <mode>      # StoryVM / Vanilla
```


---

<a id="scene-environment"></a>

## 通用场景天气与时间控制（2026-11-17，用户批准登记）

**解决什么问题**：在指定场景入场时设置时辰、雨雪与积雪，或在场景内临时调整并恢复；既有环境参数，也有可见雨滴／雪花。后续演出、玩法或调试需求复用本节的入场接入、Scene控制和降水显示，具体环境值留在需求自己的数据里。

**适用范围**：已就绪的户外战役Mission，必须有原版入场记录的完整插值大气；没有场景、正在结束、室内、多人及不满足大气前提的场景拒绝修改。场景写入在游戏主线程执行；当前类型为LWN程序集内能力，不是外部DLL公开API，也没有新增Story DSL动作token。

### 数据与版本边界

现有剧情阶段支持可选的`Environment`对象：

```json
{"Hour":7,"Weather":"rain","Density":0.7,"SnowCover":0}
```

| 字段 | 含义与校验 |
|---|---|
| `Hour` | 必填，小数小时，范围`[0,24)`；7是07:00，7.5是07:30。只改本场环境小时，不推进战役日期。 |
| `Weather` | 必填，只接受`clear`、`rain`、`snow`；雨雪互斥。`clear`表示停止降水。 |
| `Density` | 必填，雨雪强度`(0,1]`；clear必须为0。临时weather命令省略强度时雨雪默认0.7。 |
| `SnowCover` | 可选，默认0，范围`[0,1]`；原版冬季覆盖系数，不是厚度。与降水独立，下雨或停雨不会自动清掉已有积雪。 |

省略整个Environment沿用原版战役环境；空对象、未知字段、错误类型、非有限数字和越界值拒绝发布。临时命令也先校验完整参数，多余参数拒绝；直接调用底层`SceneEnvironment.Execute`时调用方须先完成同等校验。

现有维护源为`ModuleData/DesignData/StoryGateSetup.json`的`Stages.first_meet.Environment`及`StoryOpeningTimeline.json`顶层Environment，生成到`StoryScenePlan.json`的阶段Environment；其他需求需接入自己的数据源，不会因任意JSON出现这个字段就自动生效。不要手改生成物。生成命令：

```powershell
python -X utf8 "H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Scripts\gen_story_stage_v2.py"
```

此命令用于改完现有两处维护源后发布正式布局；会按生成器规则更新生成物与内容版本，不运行游戏、不移动人物或结算财物。预期校验通过且重复生成字节一致。游戏已打开时在大地图执行`custom.story_probe reload`重读整套剧情配置，下次入场使用新版本；该命令不传送、不重置剧情或财物，已有场景环境不会随重载自动改变。

| 能力 | 1.2.12 | 1.3.15／1.5.7及当前1.3+分支 |
|---|---|---|
| JSON入场时辰与雨雪／积雪 | 支持，原版初始化器计算完整大气 | 支持，沿用各版原初始化器 |
| 临时weather／snowcover／reset | 支持 | 支持；reset只恢复本场曾应用的临时修改 |
| 临时time／set | 支持，刷新入场已加载的完整插值对象 | **尚未接通，整条在任何写入前拒绝**；不能仅凭同名托管API存在解除限制 |

### 关键入口与接入范例

| 真实签名 | 职责 |
|---|---|
| `StoryEnvironment.Open(string sceneName, StoryStageDef stage, int version)` → `EntryScope` | 为下一次匹配的原版入场限定小时输入和环境数据；固定本次数据版本。 |
| `EntryScope.Attach(Mission mission)`／`Dispose()` | 把入场回执绑定到返回的Mission；using确保成功或异常都清作用域。 |
| `StoryEnvironment.ReportActual(Mission mission)` | Tick中等到当前户外Scene就绪，补一次入场雨雪／积雪及降水显示，再打印实际回读。 |
| `EnvironmentCommands.Execute(List<string> args)` → `string` | 校验并执行当前场景控制；参数从status／time等子命令开始，返回纯英文回执。 |
| `SceneEnvironment.Execute(SceneEnvironment.Change change)` → `string` | 通用Scene修改与恢复；Change含Operation／Weather及可空Hour／Density／SnowCover。 |
| `SceneEnvironment.CheckMission(Mission mission)` → `string` | null表示场景运行状态及户外／非多人检查通过，修改时另核大气前提。 |
| `SceneEnvironment.ApplyEntryWeather(Mission mission, AtmosphereInfo atmosphere)` | 场景就绪后从已初始化的入场大气补天气及显示，不重算光照；其他入场调用方自行保证只补一次。 |
| `SceneEnvironment.Describe(Mission mission)` → `string` | 只读实际环境、临时请求、版本支持及降水宿主状态。 |

场景内复用范例（同一LWN程序集，主线程调用）：

```csharp
// 已有参数校验、场景检查、失败恢复和雨滴宿主，不另写裸Scene setter。
string result = EnvironmentCommands.Execute(new List<string> { "weather", "rain", "0.7" });
string status = EnvironmentCommands.Execute(new List<string> { "status" });
string reset = EnvironmentCommands.Execute(new List<string> { "reset" });
```

现有原版入场接入范例，`sceneName`须与初始化器本次场景名一致，`stage`来自已经校验的发布数据；`encounter`、`location`、`spawnTag`由原入场调用方提供：

```csharp
IMission openedMission;
using (var environment = StoryEnvironment.Open(sceneName, stage, version))
{
    openedMission = encounter.CreateAndOpenMissionController(location, null, null, spawnTag);
    environment.Attach(openedMission as Mission);
}
```

在返回Mission的已有行为中，每次Tick调用；内部等到当前户外Scene就绪后只补一次：

```csharp
StoryEnvironment.ReportActual(Mission);
```

入场小时覆盖只限本线程、本次初始化器、指定场景和原版`DefaultMapWeatherModel`实例；嵌套其他场景不继承，Postfix／Finalizer／Dispose负责清理。补丁禁用、模型未消费小时或原版模型不满足前提时明确拒绝；不重建Mission或复制原版行为列表。

### 完整验收指令

下列修改只影响当前Scene，不移动人物、不改JSON／存档／剧情进度／财物／战役时钟；退出后按正常入场规则加载。临时控制保留已加载的天空／云层预设，clear不承诺把阴云换成晴空。

| 完整指令 | 作用、使用时机与预期结果 |
|---|---|
| `custom.story_probe environment status` | 随时只查询发布环境、当前Scene及降水状态；入场配置与临时请求各有自己的matches比较。开阔处下雨时应有kind=rain、雨资源、attached=True、emitting=True；查询不改变环境。 |
| `custom.story_probe environment time 7.5` | 1.2.12场景内临时设07:30，预期actualHour=7.50并改变光照；保留天气、积雪和模拟速度。1.3+整条拒绝。 |
| `custom.story_probe environment weather rain 0.7` | 场景内临时设雨强度0.7、雪密度0并接雨滴显示；开阔处观察快速下落雨滴。保留时辰和积雪。 |
| `custom.story_probe environment weather snow 0.7` | 场景内临时设雪强度0.7、雨密度0并接雪花显示；开阔处观察雪花。保留时辰和积雪。 |
| `custom.story_probe environment weather clear` | 场景内临时停止雨雪并清显示，用于画面对比；预期雨雪密度0、attached=False。保留时辰、积雪和天空预设。 |
| `custom.story_probe environment snowcover 0` | 场景内临时去掉动态积雪，0为无覆盖、1为最大覆盖；只改地面覆盖，保留降水和时辰。 |
| `custom.story_probe environment set 7 rain 0.7 0` | 1.2.12场景内同时设07:00、雨0.7、积雪0，预期四项读数一致且有雨滴。全部参数合法才执行；1.3+整条拒绝，天气与积雪也不写。 |
| `custom.story_probe environment reset` | 调试结束后恢复本场原入场环境与对应降水效果；只在本次曾改时辰时恢复原光照。无临时修改时重复执行不写Scene；不重置剧情或人物。不能回溯旧DLL写坏的光照／模拟速度。 |
| `custom.story_probe environment help` | 随时只查完整语法与版本限制，不改变环境。 |

### 显示、清理与踩坑纪律

- **雨雪共用一个活跃宿主**：`SceneEnvironmentMissionBehavior.Get(Mission)`取得已挂载行为，`Prepare(float rain, float snow)`准备候选，`SetDensity(float rain, float snow)`提交显示。雨资源为Native的`psys_game_rain`／回落`psys_game_rain_close`；雪为`psys_game_snow`／回落`psys_game_snow_2`。同类调密度复用，换天气先建停用且发射倍率0的候选，Scene写成功才换；失败清候选并保留旧效果。
- **只跟实际渲染视点位置**：`Scene.LastFinalRenderCameraFrame.origin`上方4米放发射器，不接管相机或读取方向。每0.2秒向上30米射线，屋檐下暂停、离开恢复；这是视点遮挡判定，不是每颗粒子的建筑碰撞。clear、退场及行为移除清理；已切Mission时旧行为只丢托管引用，不调用旧场景原生实体。
- **禁止把TimeSpeed当日照时钟**：它是模拟速度，设0会被原生钳到约0.0001，运动和降水几乎停住。环境控制保持原模拟速度，不写0或强行改回1。
- **调时复用完整原版大气**：1.2.12由`V.RefreshSceneEnvironmentHour(Scene scene, float hour)`调用入场已加载对象，内部小时乘10；调用方传普通小时，不重复乘10。禁止直接拼写太阳／曝光／雾字段或临时加载大地图大气。1.3+通过`V.SceneEnvironmentTimeError`门控，不能绕过。
- **密度生效不等于雨雪可见**：1.2.12原入场漏复制雪密度；此前雨密度已为0.7却无雨滴宿主。现在入场和命令共用`ApplyScene`与降水显示。看不到效果时查kind／resource／attached／emitting／sheltered／failed和simulationSpeed，不能仅凭matches=True或XML里有资源宣布画面通过。原版粒子运行期来自资产包，复用现有宿主与清理规则见[粒子轮子](spells.md)“运行期挂粒子的两条硬事实”。
- **失败恢复按Mission隔离**：`ConditionalWeakTable<Mission, Session>`保留入场值及临时请求；异常尝试回滚、保留恢复凭据，跨场景不继承。新调用方复用原子入口，不自行做半套环境更新或另造雨／雪行为。

**源码位置**：`Core/SceneEnvironment.cs`、`Core/SceneEnvironmentMissionBehavior.cs`、`Core/VersionCompat.cs`；入场接入在`Interaction/StoryProbe/StoryEnvironment.cs`及`StoryProbeBehavior.cs`，参数定义／解析在`StoryLayoutDefs.cs`；控制台实现为`CampaignMode/Tools/EnvironmentCommands.cs`，由`StoryProbeCommands.cs`分派。MySubModule在互动系统闸门前挂降水行为，无降水请求不建资源。

**验收依据**：2026-11-17用户确认即时调时可用，并在清晨雨天、无积雪的入场与雨滴显示修复后明确反馈“成功了”。这确认当前实机路径，不代表1.3+即时调时已接通，也不替代屋檐／全部命令的逐项画面验收。三安装隔离编译0错误0警告，命令／粒子／失败恢复117／106／106项及各62项入场环境回归通过；离线原生渲染为替身。详细用户记录见[归档问题21](../../逆水寒511/用户反馈的问题清单-已解决归档.md#feedback-21)，实施依据见[总纲§16.25](../../逆水寒511/逆水寒511-总纲与实施计划.md)。后续扩版本须保留原生安全门控并补实际证据。

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
- **2026-08-17 三版本实锤新增**：`ChangeKingdomAction.ApplyByJoinToKingdom`（1.2.12=3参无 CampaignTime，1.3+=4参）→ `V.JoinKingdom`；
  `EndCaptivityAction.ApplyByEscape`（1.2.12=2参，1.3+=3参带 showNotification）→ `V.EndCaptivityEscape`；
  `MapWeatherModel.WeatherEvent.Storm`（枚举成员 1.3.0+ 新增，1.2.12 无）→ `V.WeatherWord`（Storm 分支 `#if MB2_GE_130`）；
  `CampaignEvents.HeroPrisonerReleased`（1.2.12=4参，1.3+=5参带 bool）与 `CampaignEvents.BeforeHeroesMarried`（1.2.12 无，用同名同签名 `HeroesMarried`）
  → 事件注册无法封装 V 方法，登记为 MyBehavior.cs:33,45 的 structural 裸 #if（lambda 适配）
- **2026-08-21 UI 层三件套实锤新增**（1.2.12 编译 CS 错误逐个反编译定位，阈值均 `MB2_GE_130`，使用点 = IM 层死层判定/焦点再固守/软键盘探测）：
  `ScreenLayer.Finalized` → `ScreenLayer.IsFinalized`（🔴 类名是 **ScreenLayer** 不是 Layer，两版本同名，`GauntletLayer : ScreenLayer`）→ `V.LayerFinalized(layer)`；
  `EventManager.FocusedWidget` setter 1.2.12 是 **private set**（1.3+ 公开，带软键盘守卫逻辑；`SetWidgetFocused(w, fromClick)` 方法仅 1.2.12 存在，1.3+ 删除）→ `V.SetFocusedWidget(em, w)`（1.2.12 走 SetWidgetFocused，语义等价：OnLoseFocus/OnGainFocus + 控制器激活弹软键盘）；
  `Input.IsOnScreenKeyboardActive` 1.2.12 **无此 API**（InputSystem.dll 二进制 grep "OnScreenKeyboard" 0 命中）→ `V.IsOnScreenKeyboardActive()` 恒 false 降级（1.2.12 无软键盘机制，"十字键退出输入态"路径不受影响）；
  🔴 **Widget 类两版本都在 `TaleWorlds.GauntletUI.BaseTypes` 命名空间**（不在 `TaleWorlds.GauntletUI` 下——1.2.12 编译 CS0234 实锤，V 方法参数类型必须写 `TaleWorlds.GauntletUI.BaseTypes.Widget`）

**🔴 `InventoryManager` 类改名**（2026-08-14 反编译验证，四版本二进制 grep + ilspycmd）：

| | v1.2.12 | v1.3.15+ / v1.4.x |
|---|---|---|
| 物品级界面类 | `InventoryManager`（`TaleWorlds.CampaignSystem.Inventory`） | 改名 **`InventoryScreenHelper`**（`Helpers`） |
| 部队级界面类 | 同在 InventoryManager | 拆到新类 **`PartyScreenHelper`**（`Helpers`） |

物品级 `OpenScreen*` 方法签名全部不变（Loot/Stash/Warehouse/ReceiveItems/Inventory/Trade 逐一对得上）；部队级（TroopRoster 版 Loot/Ransom/ManageTroops）搬到 PartyScreenHelper。封装：`V.OpenLootScreen(Dictionary<PartyBase, ItemRoster>)`（`#if MB2_GE_130` → InventoryScreenHelper / `#else` → InventoryManager）。
⚠️ **教训**：曾因只查 Latest DLL 找不到 `InventoryManager` 类名，误判"Latest 无此 API"，给开箱/搜刮"自己挑选"写回退全部拿走逻辑（实机跑了很久）——类改名 ≠ API 移除。给签名下结论前必须二进制 grep 类名字符串 + 三版本 ilspycmd 对比（本次流程见上文"反编译验证流程"）。

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

## 🔴 模块启用判据（2026-09-07 登记）：判断「内容包是否被勾选」唯一入口 = `Utilities.GetModulesNames()`

**问题**：守卫写成 `ModuleHelper.GetModuleInfo("Taikou") != null`，导致**未勾选 Taikou 也判"已加载"**——① CampaignMode 误接线清主菜单按钮 → IndexOutOfRange（实机 2026-09-07）② splash/BGM/DesignData 三资源注入通道误替换（纯原版启动也播太阁片头/太阁 BGM，日志 `[SplashVideo] 替换成功` 实锤）。
**根因**：1.2.12 的 `ModuleHelper._allFoundModules` = **物理目录扫描表**（`GetPhysicalModules()` 扫 `Modules/` 下所有有 SubModule.xml 的目录，无启用过滤）——文件夹在 = 返回，与 launcher 勾选无关。

**三个不可用判据（反编译实锤，1.2.12 实库 DLL）**：

| 判据 | 为什么不行 |
|---|---|
| `GetModuleInfo(id) != null` | 物理扫描表，未勾选也非 null |
| `info.IsSelected` | 游戏本体进程内唯一写点 = `LoadWithFullPath` 的 `IsSelected = IsNative`（ModuleManager.dll:381）→ 非 Native 恒 false，**勾上也判未启用**（launcher 勾选只存在于 launcher 进程：Launcher.Library:1232 写、:1108-1113 拼 `_MODULES_` 命令行传 C++） |
| `GetModuleFullPath` 依赖抛异常 | 目录在 = 不抛（只有目录不存在才抛 KeyNotFoundException） |

**正确判据**：`TaleWorlds.Engine.Utilities.GetModulesNames()` = `IUtil.GetModulesCode().Split('*')`——launcher 传入的启用列表，引擎 `LoadSubModules` 装配 DLL 同一来源；**1.2.12~1.5.1 四版本同签名，无版本分支**。

**落地**：`ModuleActivationHelper.IsModuleEnabled(string moduleId)`（Any 忽略大小写；任何异常保守判 false + `[ModuleActivation]` 日志）。**已统一替换的 4 个调用点**（禁止再出现第二套判据）：`Core/SplashVideoReplacePatch` / `Core/MenuSoundtrackPatch` / `Data/DesignDataLoad` / `CampaignMode/CampaignModeActivator.IsModuleLoaded`；新文件 `Core/ModuleActivationHelper.cs`。

## 🔴 Harmony 补丁的宿主类跨版本搬家（2026-10-12 登记，钩索图标实战）—— 补丁体一份 + 两版空壳 + 生效自证日志

**问题**：补丁**目标方法一字没改**，但**宿主类在某个版本改了名 / 搬了家** ⇒ 当年照老版本写的补丁被 `#if` 圈掉，
新版本上**静默不起作用**。症状固定长这样：**同一件东西，老版本上是对的、新版本上又斜又偏 / 干脆没效果**——
⚠️ 这种失效**不报错、不崩溃、日志里也没有**（整块被编译掉），只能靠"现象对比"发现。

**实例（2026-10-12）**：物品 2D 图标的取景函数 `GetItemPoseAndCamera` ——
1.2.12 挂在 `TableauCacheManager`（`…View.Tableaus`）；**1.3+ 搬到 `ItemThumbnailCache`**（`…View.Tableaus.Thumbnails`）。
四份 `View.dll`（1.2.12 / 1.3.15 / 1.4.6 / 1.5.1）逐行比对：**方法体完全相同，只有宿主类换了**
⇒ 补丁当年用 `#if !MB2_GE_130` 整块圈掉，于是 1.3+ 的钩索图标**又斜又偏**（只剩一条"放大网格"的近似做法在撑）。

**写法（范本 `Combat/GrappleIconPatch.cs` 尾部 `GrappleIconPosePatch` + `GrappleIconPose`）**：

```csharp
#if MB2_GE_130
	[HarmonyPatch(typeof(ItemThumbnailCache), "GetItemPoseAndCamera")]      // 1.3+ 的宿主
#else
	[HarmonyPatch(typeof(TableauCacheManager), "GetItemPoseAndCamera")]     // 1.2.12 的宿主
#endif
	internal static class GrappleIconPosePatch
	{
		[HarmonyPostfix]
		private static void Postfix(ItemObject item, ref Camera camera, ref MatrixFrame itemFrame)
		{
			GrappleIconPose.Apply(item, ref camera, ref itemFrame);             // 唯一实现，两版共用
		}
	}
```

三条纪律：
1. **补丁体只写一份**（`GrappleIconPose.Apply`）—— 两版各挂一个"空壳"类，壳里只有转发一行。
   照抄两份 = 下次改逻辑要改两遍（铁律 18 的同款毛病）。
2. **`#if` 只包 `[HarmonyPatch(...)]` 那一行**，不要包整个类（包整类 = 又走回老路）；`using` 也跟着 `#if`
   （两边命名空间不同）。**私有方法照样能打**（Harmony 走 NonPublic + 字符串名匹配，两版的取景函数都是 private）。
3. 🔴 **加一行"生效自证"日志** —— 一次性布尔 `_logged` + `DebugLogger.Log($"[域] …生效（{HostName}）")`，
   `HostName` 本身就是 `#if` 出来的常量（`ItemThumbnailCache/1.3+` vs `TableauCacheManager/1.2.12`）。
   这是**唯一能一眼区分"补丁没挂上"和"挂上了但逻辑不对"**的手段，否则排查只能靠猜。

**迁移前的核实动作**（别凭"应该一样"就搬）：把两个宿主类**各自反编译出来，逐行比对目标方法体**
（`Modules/<版本>DLL/` 有现成参考副本，见本卷"版本兼容三锚点"）。
确认差异**只在宿主类名**才照搬；方法体真有差异 ⇒ 那是真·版本差异，老实写分支。

**同族已知坑**：`InventoryManager` → `InventoryScreenHelper` + `PartyScreenHelper`（见本卷上一节）——
**类改名 ≠ API 移除**。给"某版本没有这个 API"下结论之前，先二进制 grep 类名字符串 + 三版本 `ilspycmd` 对比。

## 🔴 崩溃转储看「托管栈」—— `dotnet-dump`（2026-11-11 登记；两个崩溃都是它定的位）

**什么时候用**：游戏崩了，但 `rgl_log_*.txt` 里**没有托管栈**（只有引擎日志尾巴），`watchdog_log` 只给一个异常码。
🔴 **不要凭异常码下"这是 native 崩的"结论** —— `0xC0000094`（整数除零）很可能就是**托管 `DivideByZeroException` 的首次异常通知**，`0xC0000005` 同理。
判据：dump 里 `clrthreads` 能列出 `System.XXXException` 就说明是托管异常。

**三步**

```powershell
dotnet tool install -g dotnet-dump            # 一次性
dotnet-dump analyze "<崩溃目录>\dump.dmp" -c "clrthreads" -c "exit"
#   → 在 Apt/Exception 列找 System.XXXException，记下第一列 DBG 号
dotnet-dump analyze "<崩溃目录>\dump.dmp" -c "setthread <DBG号>" -c "pe" -c "clrstack" -c "exit"
```

- `pe` = 异常对象 + 生成栈（含函数偏移）；`clrstack` = 带源码行号的托管调用栈
- 栈顶那一帧就是元凶方法 → 用 `ilspycmd` 反编译该类型，确认是哪个字段为 null / 哪个除数是 0

**⚠️ 崩溃目录名的时区跟系统时间不一致**（实测差 8 小时）——找最新目录别按截图时间拼路径：

```powershell
ls -dt "C:/ProgramData/Mount and Blade II Bannerlord/crashes/"*/ | head -3
```

**本轮实例**：`AlleyCampaignBehavior.OnNewGameCreated` 的 `i % source.Count()`（城里 0 个帮派头目）、
`GuardsCampaignBehavior.CreatePrisonGuard` 的 `culture.PrisonGuard`（文化缺 `prison_guard` 属性）。
两条都在 `Knowledge/自定义世界内容包从零起步必备清单.md` 雷 176/177。

