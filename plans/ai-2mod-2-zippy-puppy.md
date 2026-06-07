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

## Context

[ExampleMod](e:/SteamLibrary/steamapps/common/Mount%20%26%20Blade%20II%20Bannerlord/Modules/ExampleMod) 当前把通用玩法增强（KCD2 风格交互、潜行偷窃、挥刀触发战斗、AI 对话、太阁5 风格剧情演出引擎、社交事件系统、任务系统、LLM 自由聊天等）和**日本战国题材内容**（Shokuho 氏族/英雄/聚落 XMLs、StoryJson 剧本、织丰世界观 prompt 字串）耦合在一个 mod 里发布。

**目标**：让玩**原版骑砍 2** 的玩家也能享受这套通用玩法，把唯一与日本战国题材绑定的**内容数据**抽出成独立内容包。所有引擎、UI、AI、LLM、剧情演出框架、社交叙事框架都属于通用玩法，都留在主 mod 里。

**目标产物**：

- **Mod A `BannerlordInteractionPlus`**（暂名）—— **所有 .cs 代码 + 所有 GUI prefab 整体留下**，按职责重组目录；唯一的代码改动是把 PromptBuilder 中硬编码的"日本战国"字串参数化成 `Settings.WorldSettingPromptOverride`，外加 LLM 功能用 `Settings.IsLLMReady` 总闸控制（玩家未配置自定义模型时降级到 vanilla 对话）。先发布、独立迭代，原版骑砍玩家直接装上就能玩。
- **Mod B `ShokuhoContent`**（接管 ExampleMod 的 ModuleData）—— **纯内容包**：Shokuho XMLs、StoryJson 剧本、UpdateSettlementsOwner 补丁。可选含一个 ~20 行的 DLL 用来在加载时设置 `Settings.WorldSettingPromptOverride` 和 ShokuhoCampaign GameType 注册。`<DependedModule Id="BannerlordInteractionPlus"/>`。

## 推荐方案

### Phase 0：目录重组（零逻辑改动）

把现有 50+ 散落在根目录的 .cs 文件按职责分到子文件夹。**不改 namespace**（保持 `ExampleMod.*` 平坦命名空间，不破坏存档的 SaveableType）。仅移动文件 + 更新 .csproj 中的 `<Compile Include="..." />` 路径。

```
ExampleModVS/ExampleMod/ExampleMod/
├── Core/         Settings.cs (新建), AgentControlHelper.cs, SafeLordPartyComponent.cs
├── Interaction/  InteractionMissionView.cs, InteractionVM.cs,
│                 InteractionOptionManager.cs, NPCInfoVM.cs (通用信息板)
├── Stealth/      StealManager.cs, StealVM.cs
├── Combat/       AttackTriggerMissionLogic.cs, CombatManager.cs
├── AI/           AgentAIController.cs, AgentBrain.cs, GroupStageManager.cs,
│                 Actions/AtomicAction.cs
├── Dialog/       DialogBehavior.cs, MyCustomUIVM.cs (通用对话/选项 VM)
├── Bubble/       BubbleSayMissionView.cs, BubbleSayVM.cs, BubbleSayNeaybyVM.cs
├── LLM/          LLMService.cs, PromptBuilder.cs
├── Memory/       MemoryManager.cs (含 SingNpcMemorySystem,
│                 AllNpcMemoryManager, NPCProfile)
├── Social/       SocialEventManager.cs (通用社交事件框架)
├── Script/       ReadStory.cs (通用 JSON 脚本加载器)
├── Spawner/      HeroSpawnerMissionBehavior.cs
├── Quest/        QuestManager.cs
├── Story/        (= 现 StoryEngineBag/) AIStoryAdapt, AIStoryGenerator,
│                 CommandManager, InteractionController, LogicCommands,
│                 NegotiationSystem, StageDirector, StoryContext,
│                 StoryDialogVM, StoryEngine, SystemCommands, Text2Anim,
│                 VisualCommands —— 太阁5 风格剧情演出引擎，
│                 引擎本身通用，剧本数据由 Mod B 提供
├── Data/         DesignDataLoad.cs
├── Debug/        MyCommands.cs
└── MySubModule.cs (留根)
```

操作清单：
1. 新建 16 个子文件夹。
2. 按上表移动现有文件；重命名 `StoryEngineBag/` → `Story/`。
3. 编辑 `ExampleMod.csproj` 的 `<Compile Include>` 路径。
4. 编译 + 启动游戏验证：所有功能与重组前完全一致。
5. 提交 commit："refactor: 按职责重组目录"。

### Phase 1：通用化代码改动（仅 2 处）

**改动 1 —— 新增 [Core/Settings.cs](ExampleModVS/ExampleMod/ExampleMod/Core/Settings.cs)**

读取 `Modules/BannerlordInteractionPlus/config.json`（不存在时用默认值）：

```json
{
  "EnableCustomLLM": false,
  "LLMEndpoint": "",
  "LLMApiKey": "",
  "LLMModel": "",
  "WorldSettingPromptOverride": null
}
```

派生属性 `IsLLMReady`：三个 LLM 字段非空时为 true。

**改动 2 —— 参数化 PromptBuilder 中的世界观字串**

[PromptBuilder.cs:962](ExampleModVS/ExampleMod/ExampleMod/PromptBuilder.cs#L962) 的硬编码字串 `"你是生活在骑马与砍杀2织丰Mod塑造的日本战国世界中的…"` → 改成：

```csharp
$"你是生活在 {Settings.Instance.WorldSettingPromptOverride ?? "骑马与砍杀2 卡拉迪亚中世纪世界"} 中的…"
```

A 单独跑 → 用卡拉迪亚默认；A+B 跑 → B 启动时把 Override 设为日本战国字串。

**改动 3（可选，建议做）—— LLM 功能总闸**

为了让"原版骑砍玩家不配 LLM 也能跑"，在以下入口加 `if (Settings.Instance.IsLLMReady)` 守卫：
- [InteractionMissionView.cs](ExampleModVS/ExampleMod/ExampleMod/InteractionMissionView.cs) F 键对话：未启用 LLM 时降级调用 vanilla `MissionConversationLogic.Current.StartConversation(...)` 而不是 StoryDialogVM
- G 键自由聊天、H 键 NPC 信息板：未启用时静默忽略或显示提示
- [DialogBehavior.cs](ExampleModVS/ExampleMod/ExampleMod/DialogBehavior.cs) / [MemoryManager.cs](ExampleModVS/ExampleMod/ExampleMod/MemoryManager.cs) / [Story/StoryEngine.cs](ExampleModVS/ExampleMod/ExampleMod/StoryEngineBag/StoryEngine.cs) 中调 LLMService 的点：包 `if (IsLLMReady)`，否则 fallback 到固定回复或跳过该路径

**改动 4 —— 实装 [AttackTriggerMissionLogic.cs:228-232](ExampleModVS/ExampleMod/ExampleMod/AttackTriggerMissionLogic.cs#L228-L232) 翻脸逻辑**

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

提交 commit："feat: Settings 总闸 + 世界观参数化 + 攻击翻脸逻辑"。

### Phase 2：物理拆分

#### Mod A 收束

把 `Modules/ExampleMod/` 改名为 `Modules/BannerlordInteractionPlus/`，编辑 SubModule.xml：

- `<Id value="BannerlordInteractionPlus"/>`
- DependedModules 维持原样（Native/SandBoxCore/Sandbox/CustomBattle/StoryMode/StartAsAnyone）
- **删除**所有带 `IncludedGameTypes=ShokuhoCampaign` 的 XmlNode（共 7 个：spcultures、clans、spkingdoms、lords、heroes、my_helmets、UpdateSettlementsOwner）
- **保留**所有带 Campaign/CampaignStoryMode/Sandbox/SandBoxCore 的 XmlNode 和 `GUI/SpriteParts/ui_character_illustration`
- **删除** `ModuleData/Shokuho/`、`ModuleData/StoryJson/`、`ModuleData/Patches/UpdateSettlementsOwner`（迁去 B）
- **保留** `ModuleData/DesignData/`、`ModuleData/Native/`、`ModuleData/Languages/` 中通用部分

DLL 输出名：`BannerlordInteractionPlus.dll`。

#### Mod B 创建

新建 `Modules/ShokuhoContent/`：

```
Modules/ShokuhoContent/
├── SubModule.xml
├── bin/Win64_Shipping_Client/ShokuhoContent.dll  (~20 行代码)
└── ModuleData/
    ├── Shokuho/         (从 A 搬过来)
    │   ├── spcultures.xml
    │   ├── clans.xml
    │   ├── spkingdoms.xml
    │   ├── lords.xml
    │   ├── heroes.xml
    │   └── my_helmets.xml
    ├── StoryJson/       (从 A 搬过来)
    └── Patches/
        └── UpdateSettlementsOwner.xml
```

`SubModule.xml`：

```xml
<Module>
  <Name value="Shokuho Content Pack"/>
  <Id value="ShokuhoContent"/>
  <Version value="v1.0.0"/>
  <SingleplayerModule value="true"/>
  <DependedModules>
    <DependedModule Id="Native"/>
    <DependedModule Id="SandBoxCore"/>
    <DependedModule Id="Sandbox"/>
    <DependedModule Id="StoryMode"/>
    <DependedModule Id="BannerlordInteractionPlus"/>
  </DependedModules>
  <SubModules>
    <SubModule>
      <Name value="ShokuhoContent"/>
      <DLLName value="ShokuhoContent.dll"/>
      <SubModuleClassType value="ShokuhoContent.MySubModule"/>
    </SubModule>
  </SubModules>
  <Xmls>
    <!-- 把 A 中删掉的 7 个 IncludedGameTypes=ShokuhoCampaign XmlNode 整段搬过来 -->
  </Xmls>
</Module>
```

`MySubModule.cs`（约 20 行）：

```csharp
using ExampleMod;  // 引用 A 的 namespace
using TaleWorlds.MountAndBlade;

namespace ShokuhoContent {
    public class MySubModule : MBSubModuleBase {
        protected override void OnSubModuleLoad() {
            base.OnSubModuleLoad();
            Settings.Instance.WorldSettingPromptOverride =
                "骑马与砍杀2织丰Mod塑造的日本战国世界";
        }
    }
}
```

#### 存档兼容关键

- **保留 A 内所有 namespace 为 `ExampleMod.*`**（仅 Module Id 改成 BannerlordInteractionPlus，namespace 不动）。MemoryManager 中的 `AllNpcMemoryManager` 等 SaveableType 不能换 namespace 否则老存档读不出。
- 老 ExampleMod 玩家存档升级路径：把老 ExampleMod 文件夹重命名为 BannerlordInteractionPlus（手动或脚本），存档仍能读，因为存档存的是 namespace 不是 Module Id。

## 验证

| 场景 | 期望 |
|---|---|
| 仅启用 A，未配置 LLM (IsLLMReady=false) | KCD2 提示工作；F 弹**vanilla 骑砍对话**；偷窃/搜刮/翻脸/气泡/巡逻 AI 全部工作；社交事件框架空跑（无脚本输入） |
| 仅启用 A，配置了 LLM | F 弹 StoryDialogVM 走 LLM 自由聊天；G 自由聊；目击犯罪触发谈判；persona prompt 用"卡拉迪亚中世纪世界" |
| A + B，未配置 LLM | 行为同"仅 A 不配 LLM"；ShokuhoCampaign 选项可见，进入后氏族/英雄/补丁加载正常；但所有 LLM 路径仍降级 |
| A + B，配置了 LLM | persona prompt 自动变为"日本战国世界"；剧情/谈判/演出全部生效 |
| 仅 B（不勾 A） | 启动器报缺失依赖 BannerlordInteractionPlus |
| 老 ExampleMod 存档 + A | 存档可读（namespace 未变） |

代码层验证：

- `grep -ri "Shokuho\|织丰\|日本战国\|太阁" Mod_A_工程目录` → 仅余 PromptBuilder 中默认 fallback 字串和注释；无硬编码 Shokuho 题材引用
- A 编译产物只引用 `TaleWorlds.*` + `HarmonyLib` + `Newtonsoft.Json`，无对 B 的引用
- A 单独装入 vanilla 骑砍 2 + Sandbox → 进城看 NPC 应右下角弹"对话/偷窃/搜刮"提示
- 删除 `Modules/BannerlordInteractionPlus/config.json` 后启动 → A 应自动用默认值，IsLLMReady=false 不崩
- B 装入但不装 A → 启动器明确报错
