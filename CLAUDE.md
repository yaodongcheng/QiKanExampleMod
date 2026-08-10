# LivingWorldNpcs — 项目规则

> **会话必读（写任何代码前先做）：读一遍 [plans/rules/wheels.md](plans/rules/wheels.md) 索引（~40 行），定位任务命中的域 → 打开 [wheels.d/](plans/rules/wheels.d/) 对应分卷。**
> 这是已造轮子速查，避免重复造轮子 / 绕过既有引擎。**不查索引不准动手写新功能。**
> ⚠️ wheels.d/ 分卷按需加载——**只读命中域的卷**，禁止整卷全读（正文共 2200+ 行，全读会烧掉大量上下文）。

详细规则见 `plans/rules/`。**wheels.md 索引每次会话必读**，其余按需加载：

| 规则文件 | 主题 |
|----------|------|
| [wheels.md](plans/rules/wheels.md) | 🔴**【必读】已造轮子速查（索引）**：先看索引定位域 → 打开 `wheels.d/` 对应分卷，命中即复用 |
| [llm-optional.md](plans/rules/llm-optional.md) | **LLM 是可选功能**，IsLLMConfigured 总闸，所有入口点必须检查 |
| [im.md](plans/rules/wheels.d/im.md) | **IM 传讯/群聊轮子速查**：群聊回复管线（延迟调度+丢弃纪律）、群聊记忆参与度写入、回应模式人格化、事件广播线程模型（🔴 主线程禁止同步等 LLM）、选人增强 |
| [worldview.md](plans/rules/worldview.md) | **禁止硬编码日本战国字串**，世界观通过 Settings.Instance 参数化 |
| [defensive-coding.md](plans/rules/defensive-coding.md) | **LLM JSON 响应必须 null-guard**，JSON key 必须匹配 [JsonProperty] |
| [architecture.md](plans/rules/architecture.md) | Namespace (`LivingWorldNpcs.*`)、目录结构、Mod A/B 拆分 |
| [coding-style.md](plans/rules/coding-style.md) | 命名/单例/异步/异常/ViewModel 绑定 等编码约定 |
| [pitfalls.md](plans/rules/pitfalls.md) | **坑点速查（疑难杂症）**，踩到 AccessViolation/native 崩溃等诡异症状时按需查 |
| [narrative-design.md](plans/rules/narrative-design.md) | 🔴**【必读】叙事设计铁律**：禁止上帝视角，情报必须来自渠道 |
| [design-philosophy.md](plans/rules/design-philosophy.md) | 🔴**【必读】设计哲学四原则**：反馈明确、自由感、NPC接得住、信息塑造目标 |

**运行时调试日志**：`Debug/StoryEngine_RuntimeLog.txt`（`DebugLogger.Log` 写入，内容随调试需求变动）。排查问题或验证行为时可直接 `Read` 分析。

## 八条铁律

1. **LLM 不可用时游戏不能崩** — 任何 LLM 代码路径入口检查 `Settings.Instance.IsLLMConfigured`，不存在就降级或 return
2. **LLM 返回的 JSON 不可信任** — 每个 `foreach` 前 null check，每个字段用 `?.` 传播
3. **LivingWorldNpcs 是通用 mod** — 代码里不能出现 `Shokuho`/`日本战国`/`太阁`/`织丰` 等字串
4. **资源进出统一归口、禁止半截操作** — 凡「看上去像资源进出」的地方都走 `AgentControlHelper`（**金钱 = 特殊物品**，Item==null），禁止业务层裸调 `Hero.ChangeHeroGold` / `ItemRoster.AddToCounts` 等单边 API。三类操作各有纪律：①**转移 Transfer**（贿赂/罚款/赏赐/买卖）守恒，一方扣一方加，**禁止只做半截**（钱扣了没人收）；②**收发 Grant/Sink**（战利品/凭空奖励/消耗）单边对接「世界」，用 `null` 显式标注虚空来源/去向，**合法非违规**；③**转换 Convert**（冶炼/工坊/吃苹果回饱腹）按配方刻意非守恒，但必须**守卫 + 原子**（输入不足则整体不发生）。
5. **禁止硬编码游戏资源 ID** — 任何通过 `MBObjectManager.Instance.GetObject<T>("hardcoded_id")` 查找物品/角色/城镇/Culture 的逻辑，都可能被其他 mod（织丰/Shokuho 等）屏蔽导致返回 null。**必须使用两轮策略**：①第一轮尝试预设 ID 列表（从 XML 验证过的已知 ID）；②第二轮用 `MBObjectManager.Instance.GetObject<T>(predicate)` 动态遍历内存中已注册的对象做兜底。参看 `AgentControlHelper.TryGiveAnyMeleeWeapon` 为范本。**装备、NPC 模板、城镇、文化、兵种等全部适用此规则。**
6. **以 KCD2 / 荒野大镖客 2 的水准要求自己** — 每次思考实现方案、每次审查产出时，问自己：这个设计在 KCD2 里合格吗？玩家体验会不会出戏？沉浸感有没有被破坏？不是功能跑通就算完——要跑到让玩家觉得"这个 mod 像是原生游戏的一部分"。叙事、交互、UI、节奏、信息传递，每一项都适用。做不到就改，改到合格为止。
7. **设计哲学四原则** — 任何新系统/新功能设计必须对照 [design-philosophy.md](plans/rules/design-philosophy.md) 逐条检查：①反馈明确 ②自由感 ③任意 NPC 接得住 ④信息塑造目标。设计评审不通过四原则 → 先改设计，再写代码。
8. **所有 Agent 平等互动** — 玩家可以和任意 Agent 互动——无论它有 HeroObject（有名有姓的 Hero）还是模板 NPC（普通士兵/村民/守卫）。对话、战斗、偷窃、贿赂、威胁、投降等所有互动入口必须兼容 `speaker/partner == null`。**只拦截真正依赖 Hero 身份才能运作的场景**（如栽赃陷害——必须把罪名记到具体 Hero 头上），通用互动一律放行。模板 NPC 的身份匹配用 `TemplateId`（CharacterObject.StringId），不用 Hero StringId。
9. 🔴**WorldEvent 双源查找（已内置）** — 框架中存在 `PendingWorldEvent`（`AgentAIController.Instance?.PendingWorldEvent`）概念：Mission 内刚检测到的犯罪事件，尚未持久化到 `WorldEventStore`。**`WorldEventStore.FindOnGoing(settlementId)` 已内置 PendingWorldEvent 兜底**，调用方直接 `WorldEventStore.FindOnGoing(settlementId)` 即可，**不需要**手动 `?? AgentAIController.Instance?.PendingWorldEvent`。`GetMisconductEvent(Agent)` 等直接访问 PendingWorldEvent 的 Helper 保留不变（它们走的是 Agent→Pending 而非 settlement→FindOnGoing 路径）。
10. 🔴**赔偿对话纪律** — 所有赔钱相关的对话选项，**禁止玩家在 NPC 开价前说出具体金额**。流程必须是：玩家"我愿意赔偿"（不标价）→ NPC 在 `restitution_demand` 节点里算账开价（明细 + 倍率 + 总价）→ 玩家接受/砍价/拒绝。**实现**：所有 `INTENT:PayRestitution` 入口改为 `Action="NONE"` + `NextNodeOnSuccess="restitution_demand"`，子树末尾调 `BuildRestitutionSubtree(nodes, r, ctx)`。详参 [plans/rules/wheels.md](plans/rules/wheels.md)「赔偿对话子图」章节。
11. 🔴**赔偿金统一计算入口** — 所有犯罪相关的金额（赔偿/罚款/私了/悬赏）统一走 `CrimePenaltyCalculator.ComputeCost(evt, CostType.Restitution)`。**禁止**同一场对话中出现两个不同公式算出的价格（如 `ComputeCost(Restitution)` vs `ComputePenalty→ComputeCost(Fine)`）。`{AlertFineCost}` 占位符废弃，统一用 `{RestitutionCost}`。
12. 🔴**每个选项必须有代价或检定——禁止零成本最优解** — 对话中的每一个出口，要么考验玩家能力（技能检定），要么付出资源（赔钱/坐牢），要么承担后果（拔剑开打/关系恶化/追击部队）。**绝不允许出现"既不用检定、又不付代价、还能安全脱身"的选项。** 这种选项一旦存在，其他所有选项都失去意义——玩家永远会选它。Example：RealScene 对峙中"我走了"= 零成本脱身 → 禁止。大地图 WalkAway = 关系惩罚 + 追击 party → 合法。
13. 🔴**所有玩家可见文本走标准本地化系统** — 任何 `InformationManager.DisplayMessage` / `AddQuickInformation` / 对话节点 / UI 标签 / 飘字等**玩家能看到**的文本，**必须**通过 `LWNTextHelper` 获取，最终走 Bannerlord 的 `{=LWN_KEY}English fallback` 机制。流程：C# 代码 → `LWNTextHelper.ResolveText/Resolve/ResolveCompound` → `TextObject("{=LWN_KEY}fallback")` → 引擎查 `Languages/{lang}/std_*.xml` → 命中用翻译，未命中用 fallback。**禁止**：① C# 硬编码中文字符串（`"中文"`）② `{=!}` 标记（跳过翻译表）③ `DebugLogger.Log` 之外的裸中文字面量。`DebugLogger.Log` / 注释 / LLM prompt 豁免。
14. 🔴**语言 XML 禁止 emoji 和 BMP 外字符** — `Languages/` 下所有 XML 文件**不得包含** emoji 等 Unicode 码点 > U+FFFF 的字符。游戏引擎的 UTF-16 XML 解析器不支持代理对，遇到直接崩溃，导致整个语言加载失败，连锁反应为系统菜单变英文、语言选项只剩当前语言。**Python 检测**：`ord(ch) > 0xFFFF`。validator 待加此检查。
15. 🔴**禁止手动调用 LoadLocalizationXmls** — 引擎在启动时**自动扫描**各模块 `Languages/` 子目录加载语言包，**不需要**在 `OnSubModuleLoad` 里手动调 `LocalizedTextManager.LoadLocalizationXmls()`。手动调反而会干扰全局语言注册表，导致 Native 的语言列表被挤掉、系统菜单退化为英文、可选语言只剩 mod 注册的语种。

## 双配置体系 — `Core/MCMSettings.cs`（小白 UI） vs `Core/Settings.cs`（config.json 高级配置）

**新增可配置项时先想清楚它属于哪一边，两边禁止交叉。**

| | `MCMSettings`（游戏内 Mod 选项） | `Settings`（config.json） |
|---|---|---|
| 面向用户 | 小白玩家：游戏内 选项 → Mod 选项 → Living World NPCs 改 | 高级玩家/开发者：手动编辑 `Modules/LivingWorldNpcs/config.json` |
| 存储文件 | `{USERPROFILE}\Documents\Mount and Blade II Bannerlord\Configs\ModSettings\Global\LivingWorldNpcs\LivingWorldNpcsSettings_v1.json`（MCM json2，改即自动存） | `Modules/LivingWorldNpcs/config.json`（`JsonConvert.PopulateObject` 启动时加载，`Settings.Reload()` 热重载） |
| 字段特征 | 玩家高频调整、需要即时反馈的开关/文本框 | 开发者调试、世界观参数、列表型配置、内容包（Mod B）注入 |
| 目前字段 | `LLMBaseUrl` / `LLMApiKey` / `LLMModel` | 世界观 flavor（`WorldDescription` 等 5 个）、`DisabledInteractionMissionModes`、`ShowDebugMessages`、`WitnessSystemEnabled`、`AlertDialogueMode` |

**🔴 禁止交叉配置**：同一个配置项**只能**存在于一边——要么进 MCM UI，要么进 config.json。两边都写 = 玩家不知道哪个生效。LLM 三字段已用 `[JsonIgnore]` 从 config.json 侧切断（唯一来源 = MCM UI），新字段照此办理。

**允许单向读取（facade 模式）**：`MCMSettings` 可以读写核心 `Settings`（getter/setter 透传）；`Settings` **禁止**反向引用 `MCMSettings`——业务代码只认 `Settings.Instance`（永不 null，铁律 1 天然保障），不感知 MCM 生命周期。

**判断标准**：小白玩家需要在游戏里改这项吗？→ 需要 → 加进 `MCMSettings`（一个 `[SettingPropertyXxx]` 属性 + `{=LWN_mcm_*}` 本地化条目）；不需要 → 放 `Settings` + config.json（不用动 MCMSettings）。

## API 探索：反编译 DLL 禁止瞎猜

**骑砍2 大量 API 是 native C++ 实现，C# 层只是薄封装。** 分析 API 行为前，先用 `ilspycmd` 反编译相关 DLL 看实现和调用上下文，禁止仅凭名字推断。

### 🚀 捷径：控制台指令 → 反编译找官方实现

**想实现某个功能时，优先查 [plans/native_commands.md](plans/native_commands.md)**。里面整理了游戏的全部控制台指令（`campaign.ai_attack_party` / `campaign.ai_siege_settlement` / 等）。流程：

1. 在 `native_commands.md` 找到最相关的指令
2. `ilspycmd <DLL> | grep -A 30 "指令名"` 看官方实现
3. 提取真正调用的 API（如 `SetPartyAiAction.GetActionForBesiegingSettlement`）

**这比猜 API 名称或裸调 `SetMoveGoToSettlement` 精准十倍。** 刚才我们就靠这个发现了 `SetPartyAiAction` 全家桶——控制台指令的代码路径就是官方的"正确用法示范"。

### 工具

```bash
# 安装（一次性，已安装 v8.2）
dotnet tool install -g ilspycmd --version 8.2.0.7535

# 反编译单个类型
ilspycmd <dll路径> -t "TaleWorlds.MountAndBlade.Agent"

# 管道搜索
ilspycmd <dll路径> -t <类型名> | grep -A 15 "方法名"
ilspycmd <dll路径> | grep -n "关键字"    # 全 DLL 搜索
```

### DLL 路径

**不要手写 DLL 列表。** 项目引用的所有 TaleWorlds DLL 及其完整路径均以项目中的 `.csproj` 文件（`glob: **/*.csproj`）的 `<Reference>` 节点为准。游戏根目录通过 `$(MB2_PATH)` 解析，典型值：`H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord`。

常用反编译目标（路径由 `.csproj` 锁定）：

| 典型 DLL 文件名 | 主要内容 |
|-----|----------|
| `TaleWorlds.MountAndBlade.dll` | Agent, Mission, Team, HumanAIComponent 等战斗层 |
| `TaleWorlds.Core.dll` | EquipmentIndex, AgentFlag, WeaponClass, ItemObject 等核心类型 |
| `TaleWorlds.CampaignSystem.dll` | Hero, Clan, Settlement, CampaignBehaviorBase 等大地图层 |
| `TaleWorlds.ObjectSystem.dll` | MBObjectManager — 所有游戏资源的注册表 |

实际使用时先用 `Glob` 找 `.csproj`，再从 `<HintPath>` 取完整路径。

**版本参考 DLL**：项目根下的 `Modules/` 目录存放了其他版本的 DLL 副本，**🔴 仅用于 `ilspycmd` 反编译对比 API 差异，禁止用于交叉编译**：

| 目录 | 版本 | 用途 |
|------|------|------|
| `Modules/1.2.12DLL/` | v1.2.12 | 反编译查 v1.2.12 的 API 签名（任意电脑可用） |
| `Modules/1.3.15DLL/` | v1.3.15 | 反编译查 v1.3.15 的 API 签名（1.3.x 独有的中间形态，非 1.2.12 亦非 1.4.x） |
| `Modules/1.4.6DLL/` | v1.4.6 | 反编译查 Latest 的 API 签名（1.4.6 与 1.4.7 签名一致，可代表整套 1.4.x） |

**🔴 不要交叉编译**：不要用 `Debug_v1.2.12` 等配置去编译——该配置已废弃。编译只走 `Debug`/`Release`，每台电脑用自己的游戏 DLL，版本由 `Version.xml` 自动检测。

开发时先反编译当前版本看签名，再反编译其他版本对比，确定 `VersionCompat.cs` 里该走哪个阈值分支（`MB2_GE_140` / `MB2_GE_130` / `#else`）。**🔴 1.3.x 有独有形态**：如 `SetPartyAiAction.GetActionForRaidingSettlement`（1.3.x=4参）和 `IssueBase.CanPlayerTakeQuestConditions`（1.2.12~1.3.x=4参）——遇到"1.3.x 与 1.2.12 相同、1.4.x 不同"的 API 必须写 `MB2_GE_140` 三分支，不能沿用 `!MB2_V1212` 二分（override 签名会编译失败）。详细差异清单见 `plans/version-compat-plan.md`「三锚点验证结论」。

```bash
# 对比三个版本的同个方法
ilspycmd Modules/1.2.12DLL/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
ilspycmd Modules/1.3.15DLL/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
ilspycmd Modules/1.4.6DLL/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
```

**限制**：`MBAPI.IMBAgent.xxx` 最终调 C++ native engine，反编译看不到内部实现，只能看到**调用上下文**和**参数用法**。

**动态资源查找（铁律 5 的关键 API）**：
```csharp
// 按 ID 查找（mod 屏蔽返回 null）
MBObjectManager.Instance.GetObject<ItemObject>("some_id");

// 按条件遍历内存中所有已注册对象（不受 mod 屏蔽影响）
MBObjectManager.Instance.GetObject<ItemObject>(item => item.PrimaryWeapon != null && item.PrimaryWeapon.IsMeleeWeapon);

// 泛型 T 支持：ItemObject, CharacterObject, Settlement, CultureObject 等所有 MBObjectBase 子类
```

## 版本兼容与发布

🔴 **禁止交叉编译。** 两台电脑各装一个目标版本，同一份源码分别编译。踩过坑，不要重犯。

### 三锚点编译策略

**🔴 当前版本完全由 `MB2_PATH` 环境变量指向的游戏安装决定**：csproj 编译时读
`$(MB2_PATH)\bin\Win64_Shipping_Client\Version.xml` 自动检测版本并定义累积宏——
本机装的是哪个版本，编出来的 DLL 就是哪个版本，**不需要也不允许手动指定**。
本仓库没有「主环境」概念：换一台电脑（改 `MB2_PATH` 指向另一份游戏），编出来的就是那份游戏的版本。
查看某台电脑当前版本：`cat "$MB2_PATH/bin/Win64_Shipping_Client/Version.xml"`。

| 机器 | 游戏版本 | 产出 |
|------|---------|------|
| 1.2.12 电脑 | v1.2.12 | `LivingWorldNpcs.dll`（v1.2.12 版） |
| Latest 电脑 | v1.4.6+ | `LivingWorldNpcs.dll`（Latest 版） |

> 本仓库当前开发机（H: 盘）：**v1.4.7**（Version.xml 实测；1.4.6 与 1.4.7 签名一致，见下方 VersionCompat 章节）。

### 累积阈值宏体系

csproj 编译时读 `Version.xml` 自动定义累积宏（GE = "Greater or Equal"）：

| 游戏版本 | 定义的宏 |
|----------|---------|
| v1.2.12 | `MB2_V1212` |
| v1.3.x | `MB2_V1212` + `MB2_GE_130` |
| v1.4.x | `MB2_V1212` + `MB2_GE_130` + `MB2_GE_140` |
| v1.5.x | 全部 + `MB2_GE_150` |

代码按阈值从高到低写分支：
```csharp
#if MB2_GE_150
    // v1.5.0+ 的新 API（预留）
#elif MB2_GE_130
    // v1.3.0+ 的 API（当前 Latest）
#else
    // v1.2.12 的旧 API
#endif
```

**为什么用阈值宏而非精确版本匹配**：99% 的 API 变更只发生在一个版本边界。阈值宏只为真正发生变更的边界写分支，避免穷举所有版本号。

### VersionCompat.cs

所有 API 差异走 `Core/VersionCompat.cs` 的 `V.xxx()` 静态方法。**业务代码禁止裸写版本 `#if`**。

**合规例外**（不可迁入 V，必须直接写在业务文件里）：override/abstract 签名差异、type-level 字段类型差异、Harmony 补丁目标差异、structural 多语句算法差异、namespace 差异。完整注册表见 `VersionCompat.cs` class doc comment 和 [plans/version-compat-plan.md](plans/version-compat-plan.md)。每次新增版本时必须逐条核查注册表。

**1.4.6 与 1.4.7 的 API 签名经逐方法对比确认完全一致**——`MB2_GE_130` 分支覆盖 v1.3.0 ~ v1.4.x 全系列。

### 发布步骤

```bash
# 任意一台电脑：版本 = 本机 MB2_PATH 指向的游戏版本（自动检测，无需指定）
dotnet build -c Release   # → 本机游戏版本的 DLL（版本见 Version.xml）

# 发布多版本：到对应版本的电脑上
git pull && dotnet build -c Release   # → 该电脑游戏版本的 DLL
```

各版本 DLL 分别打包发布。详细策略见 [plans/version-compat-plan.md](plans/version-compat-plan.md)。

## 参考资料：CSDN 付费专栏

**[Knowledge/csdn_column_articles/](Knowledge/csdn_column_articles/INDEX.md)** 存放了骑砍2 MOD 开发教程（霸王奉先专栏，共 35 篇）。**实现新模块或排查疑难杂症时，可以先到这里找灵感参考。**

- 先看 [INDEX.md](Knowledge/csdn_column_articles/INDEX.md) 按主题定位相关文章
- 内容涵盖：RGL 配置、Mission/Scene 架构、GameEntity 体系、AI 系统、物理/布料/骨骼动画、存档、Shader 等
- ⚠️ **不必严格遵循**：专栏作者的写法可能基于旧版本，API 签名和调用方式需以当前项目实际引用的 DLL 为准
- 代码示例仅供参考思路，具体实现走本项目已有的轮子和规范

## 参考资料：Knowledge 库（反编译分析）

**[Knowledge/](Knowledge/)** 存放对原版骑砍2引擎和 API 的反编译分析文档。**规划新系统或理解原版行为时，先查这里。**

| 文档 | 主题 | 适用场景 |
|------|------|---------|
| [原版骑砍2战略层分析](Knowledge/原版骑砍2战略层分析.md) | 🔴 **王国→家族→军团→部队 四层决策金字塔**，含 500 行战争评分公式分解、KingdomDecision 提案系统、Army 状态机、MobilePartyAi.GetBehaviors 决策流、60 个 Action 类全览 | 规划王国层外交/军团扩展、理解原版 AI 与本 mod 的边界 |
| [Agent_AI底层原理](Knowledge/Agent_AI底层原理.md) | Agent 装配管线、五层控制参数、战斗 AI 决策流、NavMesh | Mission 层 Agent 控制 |
| [Agent_AI冲突解决与接管策略](Knowledge/Agent_AI冲突解决与接管策略.md) | SuspendVanillaAI/ResumeVanillaAI、AgentNavigator/DailyBehaviorGroup 接管机制 | NPC 行为接管、原子 Action 开发 |
| [🔴 原版场景跟随系统分析](Knowledge/原版场景跟随系统分析.md) | 🔴 **队伍成员进场景跟随完整链路**：ClanMemberRolesCampaignBehavior 名单（触发时机表/资格条件/位置白名单）+ MissionAgentHandler 出生+挂载 + FollowAgentBehavior 源码分析（状态机/多跟随者排队/视线校验）、与本 mod AgentBrain 冲突点、复用 API | 实现「队友/随从常驻跟随」、理解原版跟随行为、避免与 Brain 接管打架 |
| [架势耐力系统_引擎能力与可行性研究](Knowledge/架势耐力系统_引擎能力与可行性研究.md) | 🔴 架势/耐力机制引擎能力边界、竞品分析（RBM/Stamina System）、决策：不自研，前置依赖 RBM | 战斗系统规划、架势崩防 × AgentBrain 联动设计 |
| [原版骑砍2任务系统分析](Knowledge/原版骑砍2任务系统分析.md) | 🔴 **40 种 NPC 委托任务全览**，Issue→Quest 双层架构、三种解决路径、触发机制、IssueEffect 惩罚、对话集成 | 委托任务（CommissionQuest）系统设计，理解原版 Issue/Quest 边界 |
| [AIInfluenceProject_技术实现分析](Knowledge/AIInfluenceProject_技术实现分析.md) | 参考 mod 的 DiplomacyManager 设计 | 外交系统参考 |
| [偷盗系统分析与优化方案](Knowledge/偷盗系统分析与优化方案.md) | 🔴 **偷盗系统全链路分析**：StealVM/StealManager/触发/博弈/结算/后果闭环，对标 Skyrim/DOS2/大侠立志传的乐趣差距诊断，P0-P2 优化路线图 | 偷盗系统优化、新玩法设计、沉浸感打磨 |
| [原版Quest案例源码分析](Knowledge/quest_example.md) | 🔴 **5 个原版 Quest 源码级案例分析**：MerchantNeedsHelpWithOutlaws / NotableWantsDaughterFound / FamilyFeud / RevenueFarming / EscortMerchantCaravan，含完整调用链、反编译代码、横向对比、设计模板 | 新增 Issue/Quest 的架构参考、理解原版事件驱动模式 |
| [🔴 原版40+任务完整分析](Knowledge/vanilla_quests/README.md) | 🔴 **40+ 任务全目录 + 可复用模式 + 完整 API 参考**：按表现力/进度/NPC/事件/经济/道德抉择/部队AI/资源互斥分类的可复用接口目录，43 个任务的快速参考卡，15 个深度分析 | **设计新任务/新委托前的第一站** — 查模式、找接口、copy API 签名 |
| [🔴 原版过场动画系统完整参考](Knowledge/vanilla_cutscenes/README.md) | 🔴 **25 个 SceneNotification 过场动画完整列举**：每个场景的 SceneID、角色槽位、可替换的 CharacterObject/Equipment、文本 ID 与变量、触发事件。含婚礼/加冕/死亡/建国/新生儿/处决/龙旗任务等 | **新增过场动画或替换场景角色时的第一站** — 查可用场景模板、复用引擎 SceneID |
| [骑砍2大地图联机技术原理](Knowledge/骑砍2大地图联机技术原理.md) | 🔴 **Campaign 联机架构全览**：Server-Authoritative 模型、ProtoBuf 序列化、Harmony Transpiler 注入、时间流逝同步（TickMapTime/IsMainPartyWaiting）、场景切换矛盾（强制同队 vs 世界不暂停 vs 冻结）、坐镇 vs 亲自战斗收益平衡、BannerlordCoop 与希绝 Online 技术对比 | 规划联机功能、理解 Campaign/Mission 并行化矛盾、未来 LLM-NPC 联机行为同步 |
| [🔴 存档机制深度解析](Knowledge/存档机制深度解析.md) | 🔴 **SaveableField/SaveableProperty/SyncData/SaveableTypeDefiner 四件套**：field ID 作用域（类级别非全局）、步进编号惯例、SyncData JSON 模式、InitQuestOnGameLoad 读档重建、支持/不支持类型清单、8 个常见坑点、本项目存档架构总览 | 新增需要持久化的字段/子系统前必读、排查存档损坏/字段丢失、理解为什么不同 mod 用同样的 ID 不冲突 |
| [🔴 击晕机制 — 引擎能力与实现踩坑](Knowledge/击晕机制_引擎能力与实现踩坑.md) | 🔴 **背后击晕完整实现**：action_set 继承链陷阱、ForcePlayAction 绕过方案、human/human_child 骨骼差异、Brain auto-Resume 竞争、IsUsingGameObject vs InConversation、动画 ID 验证、完整调用链 | 新增击晕/强制动画相关功能前必读 |
| [🔴 原版对话流引擎逆向分析](Knowledge/原版对话流引擎逆向分析.md) | 🔴 **DialogFlow 底层token状态机逆向 + 动态化方案**：`ConversationManager._sentences` 大表模型、`DialogFlow` 只是建造者（非必需品）、`AddPlayerLine`/`AddDialogLineMultiAgent` 直接操作引擎、`PersuasionTask` 嵌入机制（`HasPersuasion` 标记）、LLM JSON → DynamicDialogueTurn 完整链路、与原版对话共存机制（`RemoveRelatedLines` 按归属清理） | 设计自定义 Quest 对话流、LLM 驱动动态对话、理解说服/技能检定挂接方式、实现"JSON/LLM输出直接变成游戏对话" |
| [Ollama 本地模型接入](Knowledge/Ollama本地模型接入.md) | ✅ **代码零改动支持本地模型**：Ollama OpenAI 兼容端点逐字段实测验证（json_object/max_tokens/Bearer 全通）、**玩家配置两处与 Reddit 教程不同（BaseUrl 必须带 `/v1`、API Key 必须填占位符）**、小模型质量边界（3b~7b 计划生成锚定示范 → 建议 ≥14b 或云端）、冷启动延迟预算、Windows 部署速查 | 玩家问"能不能用本地模型"时的标准答复、排查本地端点 404/配置问题 |

## 工作流约定

**每完成一个功能后，必须主动询问用户：是否要把本次产出提炼成新的轮子并登记进 [wheels.d/](plans/rules/wheels.d/) 对应域文件（[wheels.md](plans/rules/wheels.md) 是索引）。**

- 判断标准：本次是否产生了可复用的基础设施、新的引擎扩展点、或值得固化的模式。
- 若用户同意 → 在 `plans/rules/wheels.d/` 对应域文件增补条目（解决什么问题 + 关键签名 + 调用范例 + 文件路径），与现有格式一致。
- 即使本次只是用了已有轮子、没产出新轮子，也简短说明一句"无新轮子"，不要跳过这一步。

## 拆分架构

- **LivingWorldNpcs**（本 mod）= 通用玩法引擎，卡拉迪亚世界观
- **TaikouContent**（Mod B）= 纯内容包，往 Settings.Instance 注入日本战国 flavor
- 完整计划：`plans/ai-2mod-2-zippy-puppy.md`
