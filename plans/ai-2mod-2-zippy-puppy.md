> **给未来在另一台电脑上接手此 plan 的 Claude Code 的备注**
>
> 此文件是通过 git 跨电脑同步的 plan 文档。本地的 `.claude/plans/` 目录 **不在** git 仓库内（每台机器独立）。
>
> 当用户在新电脑上让你继续这个 plan 时，请先把本文件复制（或建立软链接）到本机的 `.claude/plans/ai-2mod-2-zippy-puppy.md`，这样 Claude Code 的 plan 工作流才能识别它。文件名保持不变。
>
> 复制方式举例（Windows）：
> `copy "<repo>/plans/ai-2mod-2-zippy-puppy.md" "%USERPROFILE%\.claude\plans\ai-2mod-2-zippy-puppy.md"`

---

# 拆分计划：通用玩法 mod + 织丰内容包

> **状态: 🔄 大部分完成** (最后更新 2026-06-09)
>
> | Phase | 状态 | 说明 |
> |-------|------|------|
> | Phase 0 — 目录重组 + namespace | ✅ 完成 | 21 子目录，namespace 已改 LivingWorldNpcs |
> | Phase 1 — 通用化代码 | 🔄 大部分 | 见下方明细 |
> | Phase 1 — 改动 0 API Key | ✅ | MySubModule.cs 已移除 |
> | Phase 1 — 改动 1 Settings | ✅ | Core/Settings.cs 已建 |
> | Phase 1 — 改动 2 PromptBuilder | 🔄 大部分 | 系统字段已参数化，示例对话仍有残留 |
> | Phase 1 — 改动 3 LLM 总闸 | 🔄 进行中 | G 键 + ForceTalkAction 已加；其他入口可能遗漏 |
> | Phase 1 — 改动 4 翻脸逻辑 | ✅ | AttackTriggerMissionLogic.cs 已实现 |
> | Phase 1 — 改动 5 遗留字串 | 🔄 大部分 | DesignDataLoad.cs:260 还有一条 "织丰" |
> | Phase 2 — 物理拆分 | ❓ 待确认 | SubModule.xml / Mod B 创建状态未知 |
> | Phase 3 — MCM | ⏳ 未开始 | 不阻塞发布 |
>
> **备注**: 从本 plan 提取的持久化规则已写入 `plans/rules/`，每次会话通过 `CLAUDE.md` 引用。

## Context

[ExampleMod](e:/SteamLibrary/steamapps/common/Mount%20%26%20Blade%20II%20Bannerlord/Modules/ExampleMod) 当前把通用玩法增强（KCD2 风格交互、潜行偷窃、挥刀触发战斗、AI 对话、太阁5 风格剧情演出引擎、社交事件系统、任务系统、LLM 自由聊天等）和**日本战国题材内容**（Shokuho 氏族/英雄/聚落 XMLs、StoryJson 剧本、织丰世界观 prompt 字串）耦合在一个 mod 里发布。

**目标**：让玩**原版骑砍 2** 的玩家也能享受这套通用玩法，把唯一与日本战国题材绑定的**内容数据**抽出成独立内容包。所有引擎、UI、AI、LLM、剧情演出框架、社交叙事框架都属于通用玩法，都留在主 mod 里。

**目标产物**：

- **Mod A `LivingWorldNpcs`**—— **所有 .cs 代码 + 所有 GUI prefab 整体留下**，按职责重组目录；namespace 从 `ExampleMod.*` 统一改为 `LivingWorldNpcs.*`；PromptBuilder 中硬编码的"日本战国"字串参数化成 `Settings` 中的可配置字段（默认值=卡拉迪亚中性世界观），外加 LLM 功能用 `Settings.IsLLMConfigured` 总闸控制（玩家未配置自定义模型时降级到 vanilla 对话）。先发布、独立迭代，原版骑砍玩家直接装上就能玩。
- **Mod B `TaikouContent`**（接管 ExampleMod 的 ModuleData）—— **纯内容包**：Shokuho XMLs、StoryJson 剧本、UpdateSettlementsOwner 补丁。一个 ~30 行的 DLL 用来在加载时设置 `Settings` 中的世界观相关字段和 ShokuhoCampaign GameType 注册。`<DependedModule Id="LivingWorldNpcs"/>`。

---

## 推荐方案

### Phase 0：目录重组（零逻辑改动）

把现有 50+ 散落在根目录的 .cs 文件按职责分到子文件夹；**namespace 从 `ExampleMod.*` 全局替换为 `LivingWorldNpcs.*`**（趁发布前改，不留历史包袱）。仅移动文件 + 更新 .csproj 中的 `<Compile Include="..." />` 路径 + 全局替换 namespace。

```
ExampleModVS/ExampleMod/ExampleMod/
├── Core/         MySubModule.cs, AgentControlHelper.cs, SafeLordPartyComponent.cs,
│                 Settings.cs (新建), MyBehavior.cs
├── Interaction/  InteractionMissionView.cs, InteractionVM.cs,
│                 InteractionOptionManager.cs, NPCInfoVM.cs,
│                 InteractionController.cs (对话编排器：LLM调用+UI驱动),
│                 StoryDialogVM.cs (对话UI的ViewModel)
├── Stealth/      StealManager.cs, StealVM.cs
├── Combat/       AttackTriggerMissionLogic.cs, CombatManager.cs,
│                 DuelMissionView.cs, DuelVM.cs,
│                 ArtisanBeerMissionView.cs (战斗中按Q消耗道具回血)
├── AI/           AgentAIController.cs, AgentBrain.cs, GroupStageManager.cs,
│                 Actions/AtomicAction.cs
├── Bubble/       BubbleSayMissionView.cs, BubbleSayVM.cs, BubbleSayNeaybyVM.cs
├── Notify/       NinjaNotificationMissionView.cs, NinjaNotificationVM.cs,
│                 CustomNotify.cs (空壳，待实现)
├── Camera/       CameraDebuggerView.cs, CameraDebuggerVM.cs,
│                 SpringArmCameraDebuggerVM.cs, SpringArmCameraView.cs
├── LLM/          LLMService.cs, PromptBuilder.cs
├── Memory/       MemoryManager.cs → 拆为 7 个文件：
│                 ChatMessage.cs, RecentMemory.cs,
│                 PlayerGeneratedOption.cs, PlayerResources.cs,
│                 NPCProfile.cs, SingNpcMemorySystem.cs,
│                 AllNpcMemoryManager.cs
│                 （不改逻辑，仅拆类到独立文件）
├── Negotiation/  NegotiationSystem.cs (~1759行，完整的太阁5风格谈判小游戏：
│                 谈判状态机、卡牌系统、筹码估值、技能检定、
│                 NPC主动性/冲突检测、LLM输出协议)
├── Social/       SocialEventManager.cs
├── Script/       ReadStory.cs (通用 JSON 脚本加载器)
├── Spawner/      HeroSpawnerMissionBehavior.cs
├── Quest/        QuestManager.cs
├── Story/        (= 现 StoryEngineBag/) AIStoryAdapt, AIStoryGenerator,
│                 CommandManager, LogicCommands,
│                 StageDirector, StoryContext, StoryEngine,
│                 SystemCommands, Text2Anim, VisualCommands
│                 —— 太阁5 风格剧情演出引擎（纯引擎，剧本数据由 Mod B 提供）
├── Data/         DesignDataLoad.cs
├── Debug/        MyCommands.cs, DebugBehavior.cs, DebugLogger.cs,
│                 MyCustomUIVM.cs (F9 调出的测试 UI)
└── Properties/   AssemblyInfo.cs (不动)
```

操作清单：
1. 新建 18 个子文件夹（Properties 已存在；Dialog/ 不建——DialogBehavior.cs 已确认死代码，删除）。
2. 按上表移动现有文件；重命名 `StoryEngineBag/` → `Story/`；**拆分 `MemoryManager.cs` 为 7 个独立文件**（不改逻辑，仅把类分到各自 .cs 文件）；**删除 `DialogBehavior.cs`**（死代码：原版对话系统测试残留 + 无人调用的反射工具），同步删除 [MySubModule.cs:139](ExampleModVS/ExampleMod/ExampleMod/MySubModule.cs#L139) 的 `AddBehavior(new DialogBehavior())` 注册。
3. 编辑 `ExampleMod.csproj` 的 `<Compile Include>` 路径 + `<RootNamespace>` + `<AssemblyName>` 使其匹配。
4. 全局替换 namespace：`ExampleMod` → `LivingWorldNpcs`，`ExampleMod.AI` → `LivingWorldNpcs`（后统一拍平），`ExampleMod.StoryEngineBag` → `LivingWorldNpcs.Story`。
5. 编译 + 启动游戏验证：所有功能与重组前完全一致。
6. 提交 commit："refactor: 目录重组 + namespace 重命名 ExampleMod→LivingWorldNpcs"。

---

### Phase 1：通用化代码改动

#### 改动 0 —— 移除硬编码 API Key（安全修复）

**现状**：[MySubModule.cs:47](ExampleModVS/ExampleMod/ExampleMod/MySubModule.cs#L47)

```csharp
LLMService.Initialize("sk-db03887a984d43caaaf2d30767e81bcd");
```

**改为**：删除此行，LLMService 的初始化推迟到需要时懒加载，从 `Settings.Instance` 读取：

```csharp
// 在 LLMService 首次调用时：
if (Settings.Instance.IsLLMConfigured)
    LLMService.Initialize(Settings.Instance.LLMApiKey);
```

---

#### 改动 1 —— 新建 `Core/Settings.cs`

**1a. 发布时附带的 `config.json`**（仅 LLM 连接，玩家侧的）：

```json
{
  "LLMBaseUrl": "",
  "LLMApiKey": "",
  "LLMModel": ""
}
```

**1b. Settings.cs 结构**——LLM 配置从 JSON 读，世界观 flavor 硬编码默认值：

```csharp
public class Settings {
    public static Settings Instance { get; } = Load();

    // ── 玩家 LLM 配置（从 config.json 读取）──
    public string LLMBaseUrl { get; set; } = "";
    public string LLMApiKey { get; set; } = "";
    public string LLMModel { get; set; } = "";
    public bool IsLLMConfigured => !string.IsNullOrWhiteSpace(LLMBaseUrl)
                           && !string.IsNullOrWhiteSpace(LLMApiKey)
                           && !string.IsNullOrWhiteSpace(LLMModel);

    // ── 世界观 flavor（硬编码卡拉迪亚默认，供 Mod B 代码覆盖）──
    // 不从 JSON 读取，不需要玩家关心
    public string WorldDescription { get; set; } = "骑马与砍杀2 卡拉迪亚中世纪世界";
    public string EraDescription { get; set; } = "中世纪卡拉迪亚大陆";
    public string SpeechStyle { get; set; } = "风格口语化、符合中世纪背景。不要使用现代网络用语。";
    public string WarriorTerms { get; set; } = "使用\"大人\"、\"爵士\"等符合中世纪语境的词汇。";
    public string FemaleSelfAddress { get; set; } = "";
}
```

**设计原则**：
- `config.json` 只管 LLM 连接三要素，默认全空 → `IsLLMConfigured=false` → 普通玩家无需任何配置
- 世界观 flavor 不从 JSON 读——硬编码卡拉迪亚默认，Mod B 启动时用代码覆盖
- 两条线完全解耦：LLM 是玩家的事，世界观是内容包的事
- 以后接 MCM 时，MCM 只管 LLM 三个字段；世界观 flavor 也可暴露给 MCM 作为可选高级项

---

#### 改动 2 —— 参数化 PromptBuilder 中的全部世界观字串

**范围远超规划初版估计的一处**。经 grep 确认，PromptBuilder.cs 中有 **~10 处**硬编码了日本战国内容：

| 行号 | 硬编码内容 | 替换方式 |
|------|-----------|---------|
| 84 | `口吻符合日本战国背景……大河剧风格……妾身` | `Settings.Instance.SpeechStyle` + `Settings.Instance.FemaleSelfAddress` |
| 134 | 同上 | 同上 |
| 246 | `当前处于日本战国时代` | `Settings.Instance.EraDescription` |
| 308 | 同 84 | 同 84 |
| 662 | `日本战国RPG游戏` | `{Settings.Instance.WorldDescription} 中的RPG游戏` |
| 815 | 同 84 | 同 84 |
| 963 | `织丰Mod塑造的日本战国世界` | `Settings.Instance.WorldDescription` |
| 1005 | `符合日本战国背景` | `{Settings.Instance.SpeechStyle}` |
| 1177 | `日本战国武家风格……在下、主公、混账` | `Settings.Instance.WarriorTerms` |

**实现方式**：在 PromptBuilder 顶部加一个静态引用：

```csharp
private static Settings S => Settings.Instance;
```

然后把各行的硬编码字串替换为 `S.WorldDescription`、`S.SpeechStyle` 等。Mod A 中不再残留任何"日本战国"原文。

---

#### 改动 3 —— LLM 功能总闸

**当前代码实际行为**（经核实）：

| 按键 | 当前 | 需要 LLM？ | `!IsLLMConfigured` 时的处理 |
|------|------|-----------|----------------------|
| F | `StartVanillaConversation()` → 原版对话 | **不需要** | 无改动，本来就能用 |
| G | `StartFreeConversationFlow()` → InteractionController → LLM | **需要** | 隐藏提示/不响应，或显示"请先配置 LLM" |
| H | `OpenNPCInfoBoard()` → 读取游戏内数据 | **不需要** | 无改动，本来就能用 |

**实际只需改一处**：[InteractionMissionView.cs](ExampleModVS/ExampleMod/ExampleMod/InteractionMissionView.cs) G 键入口：

```csharp
// G 键：仅 IsLLMConfigured 时可用
if (Settings.Instance.IsLLMConfigured)
    _ = StartFreeConversationFlow(_lastFocusedAgent);
else
    InformationManager.DisplayMessage(new InformationMessage("请先在 config.json 中配置 LLM 后方可使用自由聊天。"));
```

**其他 LLM 调用点**（`MemoryManager` 记忆总结、`StoryEngine` 等）：内部已有 try-catch，LLMService 未初始化时会自然降级。不需要额外守卫。

---

#### 改动 4 —— 实装 `AttackTriggerMissionLogic.cs:228-232` 翻脸逻辑

当前空槽（玩家攻击非敌对人类）填实：

```csharp
var victimHero = victim.Character?.HeroObject;
foreach (var nearby in Mission.Current.GetAgentsInRange(victim.Position.AsVec2, 50f)) {
    if (!nearby.IsHuman || nearby == Agent.Main) continue;
    var nearbyHero = nearby.Character?.HeroObject;
    bool sameClan = nearbyHero?.Clan != null && nearbyHero.Clan == victimHero?.Clan;
    if (nearby == victim || sameClan) {
        nearby.SetTeam(Mission.Current.PlayerEnemyTeam, true);
    }
}
AgentAIController.Instance?.BroadcastEventInRange(victim.Position, 50, "event_agent_damaged", attacker, victim);
```

这是通用机制（KCD2 风格"攻击平民引发整个聚落敌对"），无需任何 LLM/Shokuho 依赖。

---

#### 改动 5 —— 清理 MySubModule.cs 和其他文件中的 Shokuho/Taikou 遗留字串

| 文件 | 行 | 现状 | 改为 |
|------|-----|------|------|
| MySubModule.cs | 33 | `"LoadTaikouEvents"` | `"LoadStoryEvents"` |
| MySubModule.cs | 59 | `$"[ShokuhoMod] Failed..."` | `$"[LivingWorldNpcs] Failed..."` |
| MySubModule.cs | 173 | `"Shokuho_Actions_Dump.txt"` | `"LivingWorldNpcs_Actions_Dump.txt"` |
| MySubModule.cs | 225/229 | `$"[Shokuho] ..."` | `$"[LivingWorldNpcs] ..."` |
| InteractionOptionManager.cs | 119 | `太阁V风格` | 删除或改为通用描述 |
| MemoryManager.cs | 237/511 | `太阁5` 注释 | 改为通用注释 |
| CommandManager.cs | 37 | `太阁里通常…` | `脚本里通常…` |
| StoryContext.cs | 55/230 | `太阁` 注释 | 改为通用注释 |
| DialogBehavior.cs | 全文 | 死代码：原版对话测试+未使用的反射工具 | **删除整个文件**，同步删除 MySubModule.cs 中的注册 |

---

#### 提交

commit："feat: Settings 总闸 + 世界观参数化(全部10处) + API Key移除 + 攻击翻脸逻辑 + 遗留字串清理"

---

### Phase 2：物理拆分

#### Mod A 收束

`Modules/ExampleMod/` → `Modules/LivingWorldNpcs/`

**SubModule.xml**：
- `<Id value="LivingWorldNpcs"/>`
- `<SubModuleClassType value="LivingWorldNpcs.MySubModule"/>`
- DependedModules 维持原样（Native/SandBoxCore/Sandbox/CustomBattle/StoryMode/StartAsAnyone）
- **删除**所有带 `IncludedGameTypes=ShokuhoCampaign` 的 XmlNode（共 7 个）
- **保留**所有带 Campaign/CampaignStoryMode/Sandbox/SandBoxCore 的 XmlNode 和 GUI prefab
- **删除** `ModuleData/Shokuho/`、`ModuleData/StoryJson/`、`ModuleData/Patches/UpdateSettlementsOwner`（迁去 B）
- **保留** `ModuleData/DesignData/`、`ModuleData/Native/`、`ModuleData/Languages/` 中通用部分
- **附带** `config.json`（字段全空/默认）

DLL 输出名：`LivingWorldNpcs.dll`。
Namespace：`LivingWorldNpcs.*`（Phase 0 已改）。

#### Mod B 创建

新建 `Modules/TaikouContent/`：

```
Modules/TaikouContent/
├── SubModule.xml
├── bin/Win64_Shipping_Client/TaikouContent.dll  (~30 行代码)
└── ModuleData/
    ├── Shokuho/         (从 A 搬过来)
    │   ├── spcultures.xml
    │   ├── clans.xml
    │   ├── spkingdoms.xml
    │   ├── lords.xml
    │   ├── heroes.xml
    │   └── my_helmets.xml
    ├── StoryJson/       (从 A 搬过来，含全部 ~90 个 .json 剧本)
    └── Patches/
        └── UpdateSettlementsOwner.xml
```

**SubModule.xml**：

```xml
<Module>
  <Name value="Taikou Content Pack"/>
  <Id value="TaikouContent"/>
  <Version value="v1.0.0"/>
  <SingleplayerModule value="true"/>
  <DependedModules>
    <DependedModule Id="Native"/>
    <DependedModule Id="SandBoxCore"/>
    <DependedModule Id="Sandbox"/>
    <DependedModule Id="StoryMode"/>
    <DependedModule Id="LivingWorldNpcs"/>
  </DependedModules>
  <SubModules>
    <SubModule>
      <Name value="TaikouContent"/>
      <DLLName value="TaikouContent.dll"/>
      <SubModuleClassType value="TaikouContent.MySubModule"/>
    </SubModule>
  </SubModules>
  <Xmls>
    <!-- 把 A 中删掉的 7 个 IncludedGameTypes=ShokuhoCampaign XmlNode 整段搬过来 -->
  </Xmls>
</Module>
```

**MySubModule.cs**（约 30 行）：

```csharp
using LivingWorldNpcs;  // 引用 A 的 namespace
using TaleWorlds.MountAndBlade;

namespace TaikouContent {
    public class MySubModule : MBSubModuleBase {
        protected override void OnSubModuleLoad() {
            base.OnSubModuleLoad();
            // 注入日本战国世界观——Mod A 的 PromptBuilder 自动生效
            Settings.Instance.WorldDescription =
                "骑马与砍杀2织丰Mod塑造的日本战国世界";
            Settings.Instance.EraDescription =
                "日本战国时代";
            Settings.Instance.SpeechStyle =
                "风格口语化、口吻符合日本战国背景。使用符合时代的\"大河剧\"风格口语。多用反问、感叹。";
            Settings.Instance.WarriorTerms =
                "使用\"在下\"、\"主公\"、\"混账\"等日本战国武家词汇。";
            Settings.Instance.FemaleSelfAddress =
                "如果你是女子，需要有女子的说话风格，如\"妾身\"。";
        }
    }
}
```

**注意**：Mod B 只覆盖世界观 flavor 字段，**不碰** `LLMBaseUrl`/`LLMApiKey`/`LLMModel`——那些是玩家的私人 LLM 配置。

#### 存档兼容

- **未经发布的 mod，没有老玩家存档需要兼容**。Namespace 和 Module Id 趁现在一并改掉。
- 如果将来需要迁移自己的测试存档：Module Id 变了，旧存档不会自动关联新 mod。但 Phase 0 之前没有公开发布，这不是问题。

---

### Phase 3：MCM 设置界面（后续增强，不阻塞拆分发布）

**目标**：让玩家在游戏原生的 Options 页面里修改 LLM 配置，无需手动编辑 JSON。

**为什么 AIInfluence 不需要你额外装 MCM**：它把 MCM 的 DLL 打包在自己的 `bin/` 目录里了（或者根本没用到 MCM）。你玩的时候自然无感。

**现状**：config.json 已经能满足 30% 硬核玩家的配置需求——打开文件填 API Key 即可。MCM 是锦上添花，**不是必要条件**。

**如果以后要加**：
1. 两种方式选一：
   - **打包 DLL**（像 AIInfluence）：把 MCM 及其前置的 DLL 放进 `Modules/LivingWorldNpcs/bin/Win64_Shipping_Client/`，玩家零安装
   - **可选依赖**（社区常见）：SubModule.xml 设 `Optional="true"`，玩家可自行安装 MCM 获得图形化设置
2. 新建 `LLMSettingsMCM.cs`，继承 `AttributeGlobalSettings<T>`，用 `[SettingPropertyText]`（`IsPassword = true` 掩码 API Key）暴露 LLM 三个字段
3. Settings.cs 做桥接：MCM 侧写入时同步到 `Settings.Instance`

**但 Phase 2 发布时完全不需要等 MCM**。config.json 够用了。

---

## 验证

| 场景 | 期望 |
|---|---|
| 仅启用 A，未配置 LLM (IsLLMConfigured=false) | KCD2 提示工作；F 弹 **vanilla 骑砍对话**；偷窃/搜刮/翻脸/气泡/巡逻 AI 全部工作；社交事件框架空跑（无脚本输入） |
| 仅启用 A，配置了 LLM | F 弹 StoryDialogVM 走 LLM 自由聊天；G 自由聊；目击犯罪触发谈判；persona prompt 用卡拉迪亚中性字串 |
| A + B，未配置 LLM | 行为同"仅 A 不配 LLM"；ShokuhoCampaign 选项可见，进入后氏族/英雄/补丁加载正常；LLM 路径降级 |
| A + B，配置了 LLM | persona prompt 自动变为日本战国风格（Mod B 注入）；剧情/谈判/演出全部生效 |
| 仅 B（不勾 A） | 启动器报缺失依赖 LivingWorldNpcs |
| 仅启用 A、config.json 全空 → 启动 | IsLLMConfigured=false，不崩，走全部默认值 |
| 玩家手动修改 config.json 设 LLM 三字段 | 重启后 IsLLMConfigured=true，LLM 功能自动启用 |
| 玩家设了 LLM + 附带改了 Settings | LLM 可用；世界观经由 Mod B 注入（如有） |

代码层验证：

- `grep -ri "Shokuho\|织丰\|日本战国\|太阁" Mod_A_工程目录` → **零命中**（包括 PromptBuilder、MySubModule.cs、所有注释）
- A 编译产物只引用 `TaleWorlds.*` + `HarmonyLib` + `Newtonsoft.Json`，无对 B 的引用
- A 单独装入 vanilla 骑砍 2 + Sandbox → 进城看 NPC 应右下角弹"对话/偷窃/搜刮"提示
- 删除 `Modules/LivingWorldNpcs/config.json` 后启动 → Settings 自动使用所有默认值，不崩
- B 装入但不装 A → 启动器明确报错
- PromptBuilder.cs 中所有世界观相关字串均通过 `Settings.Instance` 引用，无硬编码题材

---

## Mod B 注入字段速查

Mod B 的 `OnSubModuleLoad` 设置的所有字段及其卡拉迪亚默认值对比：

| Settings 字段 | 卡拉迪亚默认（硬编码） | Mod B 注入值 |
|--------------|----------------------|-------------|
| `WorldDescription` | "骑马与砍杀2 卡拉迪亚中世纪世界" | "骑马与砍杀2织丰Mod塑造的日本战国世界" |
| `EraDescription` | "中世纪卡拉迪亚大陆" | "日本战国时代" |
| `SpeechStyle` | 中性中世纪口语 | 大河剧风格 |
| `WarriorTerms` | "大人"、"爵士" | "在下"、"主公"、"混账" |
| `FemaleSelfAddress` | 空 | "妾身" |
| `LLMBaseUrl` / `LLMApiKey` / `LLMModel` | **不碰** | **不碰** |
