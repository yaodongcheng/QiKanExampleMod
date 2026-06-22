# LivingWorldNpcs — 项目规则

> **会话必读（写任何代码前先做）：完整读一遍 [plans/rules/wheels.md](plans/rules/wheels.md)。**
> 这是已造轮子速查，避免重复造轮子 / 绕过既有引擎。**不读 wheels.md 不准动手写新功能。**

详细规则见 `plans/rules/`。**wheels.md 每次会话必读**，其余按需加载：

| 规则文件 | 主题 |
|----------|------|
| [wheels.md](plans/rules/wheels.md) | 🔴**【必读】已造轮子速查**，加功能前先查再写，命中即复用 |
| [llm-optional.md](plans/rules/llm-optional.md) | **LLM 是可选功能**，IsLLMReady 总闸，所有入口点必须检查 |
| [worldview.md](plans/rules/worldview.md) | **禁止硬编码日本战国字串**，世界观通过 Settings.Instance 参数化 |
| [defensive-coding.md](plans/rules/defensive-coding.md) | **LLM JSON 响应必须 null-guard**，JSON key 必须匹配 [JsonProperty] |
| [architecture.md](plans/rules/architecture.md) | Namespace (`LivingWorldNpcs.*`)、目录结构、Mod A/B 拆分 |
| [coding-style.md](plans/rules/coding-style.md) | 命名/单例/异步/异常/ViewModel 绑定 等编码约定 |
| [pitfalls.md](plans/rules/pitfalls.md) | **坑点速查（疑难杂症）**，踩到 AccessViolation/native 崩溃等诡异症状时按需查 |
| [narrative-design.md](plans/rules/narrative-design.md) | 🔴**【必读】叙事设计铁律**：禁止上帝视角，情报必须来自渠道 |

**运行时调试日志**：`Debug/StoryEngine_RuntimeLog.txt`（`DebugLogger.Log` 写入，内容随调试需求变动）。排查问题或验证行为时可直接 `Read` 分析。

## 六条铁律

1. **LLM 不可用时游戏不能崩** — 任何 LLM 代码路径入口检查 `Settings.Instance.IsLLMReady`，不存在就降级或 return
2. **LLM 返回的 JSON 不可信任** — 每个 `foreach` 前 null check，每个字段用 `?.` 传播
3. **LivingWorldNpcs 是通用 mod** — 代码里不能出现 `Shokuho`/`日本战国`/`太阁`/`织丰` 等字串
4. **资源进出统一归口、禁止半截操作** — 凡「看上去像资源进出」的地方都走 `AgentControlHelper`（**金钱 = 特殊物品**，Item==null），禁止业务层裸调 `Hero.ChangeHeroGold` / `ItemRoster.AddToCounts` 等单边 API。三类操作各有纪律：①**转移 Transfer**（贿赂/罚款/赏赐/买卖）守恒，一方扣一方加，**禁止只做半截**（钱扣了没人收）；②**收发 Grant/Sink**（战利品/凭空奖励/消耗）单边对接「世界」，用 `null` 显式标注虚空来源/去向，**合法非违规**；③**转换 Convert**（冶炼/工坊/吃苹果回饱腹）按配方刻意非守恒，但必须**守卫 + 原子**（输入不足则整体不发生）。
5. **禁止硬编码游戏资源 ID** — 任何通过 `MBObjectManager.Instance.GetObject<T>("hardcoded_id")` 查找物品/角色/城镇/Culture 的逻辑，都可能被其他 mod（织丰/Shokuho 等）屏蔽导致返回 null。**必须使用两轮策略**：①第一轮尝试预设 ID 列表（从 XML 验证过的已知 ID）；②第二轮用 `MBObjectManager.Instance.GetObject<T>(predicate)` 动态遍历内存中已注册的对象做兜底。参看 `AgentControlHelper.TryGiveAnyMeleeWeapon` 为范本。**装备、NPC 模板、城镇、文化、兵种等全部适用此规则。**
6. **以 KCD2 / 荒野大镖客 2 的水准要求自己** — 每次思考实现方案、每次审查产出时，问自己：这个设计在 KCD2 里合格吗？玩家体验会不会出戏？沉浸感有没有被破坏？不是功能跑通就算完——要跑到让玩家觉得"这个 mod 像是原生游戏的一部分"。叙事、交互、UI、节奏、信息传递，每一项都适用。做不到就改，改到合格为止。

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

**版本参考 DLL**：项目根下的 `Modules/` 目录存放了其他版本的 DLL 副本，**仅用于 `ilspycmd` 反编译对比 API 差异，不参与编译**：

| 目录 | 版本 | 用途 |
|------|------|------|
| `Modules/1.2.12DLL/` | v1.2.12 | 在 Latest 电脑上查 v1.2.12 的 API 签名 |
| `Modules/1.4.6DLL/` | v1.4.6 | 在 1.2.12 电脑上查 Latest 的 API 签名 |

开发时先反编译当前版本看签名，再反编译另一个版本对比，确定 `VersionCompat.cs` 里该用 `#if MB2_V1212` 还是 `#if !MB2_V1212`。

```bash
# 对比两个版本的同个方法
ilspycmd Modules/1.2.12DLL/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
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
| [架势耐力系统_引擎能力与可行性研究](Knowledge/架势耐力系统_引擎能力与可行性研究.md) | 🔴 架势/耐力机制引擎能力边界、竞品分析（RBM/Stamina System）、决策：不自研，前置依赖 RBM | 战斗系统规划、架势崩防 × AgentBrain 联动设计 |
| [AIInfluenceProject_技术实现分析](Knowledge/AIInfluenceProject_技术实现分析.md) | 参考 mod 的 DiplomacyManager 设计 | 外交系统参考 |

## 工作流约定

**每完成一个功能后，必须主动询问用户：是否要把本次产出提炼成新的轮子并登记进 [wheels.md](plans/rules/wheels.md)。**

- 判断标准：本次是否产生了可复用的基础设施、新的引擎扩展点、或值得固化的模式。
- 若用户同意 → 在 wheels.md 增补条目（解决什么问题 + 关键签名 + 调用范例 + 文件路径），与现有格式一致。
- 即使本次只是用了已有轮子、没产出新轮子，也简短说明一句"无新轮子"，不要跳过这一步。

## 拆分架构

- **LivingWorldNpcs**（本 mod）= 通用玩法引擎，卡拉迪亚世界观
- **TaikouContent**（Mod B）= 纯内容包，往 Settings.Instance 注入日本战国 flavor
- 完整计划：`plans/ai-2mod-2-zippy-puppy.md`
