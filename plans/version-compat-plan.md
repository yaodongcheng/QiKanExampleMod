# 版本兼容策略 — 三锚点验证，禁止交叉编译

## 当前状态（2026-08-03）

### ✅ 已完成

| 项目 | 状态 | 说明 |
|------|:----:|------|
| **v1.3.15 三锚点验证** | ✅ | 用 1.3.15 DLL（游戏目录）+ 1.2.12/1.4.6 备份 DLL 逐 API 反编译对比，27 个 V 方法 + 14 处注册表 #if 全部验证 |
| **v1.5.1 第四锚点验证（2026-08-23）** | ✅ | 开发机升级 v1.5.1（Latest）后 `dotnet build -c Debug` 0 错误 0 警告；27 个 Harmony 字符串补丁目标二进制 grep 全存活；1.5.x 与 1.4.x 签名一致，`MB2_GE_150` 尚无分支使用 |
| **RaidSettlement 修复** | ✅ | VersionCompat.cs：`GetActionForRaidingSettlement` 1.3.x=4 参 / 1.4.x=5 参，新增 `#elif MB2_GE_130` 分支 |
| **CanPlayerTakeQuestConditions 修复** | ✅ | CommissionHubIssue.cs:413：1.2.12~1.3.x 基类 4 参 / 1.4.x 基类 5 参，override 改 `MB2_GE_140` 三分支 |
| **本机 v1.3.15 编译** | ✅ | `dotnet build -c Debug` **0 errors**（1 warning = `LivingWorldCampaignGameManager._heroSelectOpened` 未使用字段，历史遗留） |
| **v1.3.15 兼容回归（2026-09-23）** | ✅ | 后续新功能（法印弹 / NavMesh 调试 / 飞行）只在 1.2.12 上编过 → 1.3.15 一编炸 18 处；新增 7 个 V 封装收口后，**1.3.15 / 1.2.12 / 1.5.x 三档全绿**（差异清单见下「1.3.0 变更」） |
| **1.4.8 编译验证（2026-09-23）** | ✅ | `dotnet build -c Debug` 0 errors —— **四档全绿**（1.2.12 / 1.3.15 / 1.4.8 / 1.5.x） |
| **Harmony 补丁目标全量核查（2026-09-23）** | ✅ | 1.3.15 一开局就崩在 `PatchAll`（目标被 1.3.0 删掉）→ 新增 `Debug/offline/_check_harmony_targets.ps1`（扫**编译产物** + 四档对比 + 负面测试）；三个 1.2.12-only 守卫整类 `#if` 圈掉；挂载改**逐类**（单类失败不再掐断其余） |
| **1.3.15 / 1.4.8 实机验证（2026-09-23）** | ✅ | 修完「内容包专属补丁越界」（必备清单雷 156）+ 上面那些之后，**纯功能包模式可正常建号进游戏**（1.3.15 与 1.4.8 均实测通过） |
| **Taikou 联接到 1.3.15 / 1.4.8（2026-09-23）** | ✅ | `set_junction.py` 改**表驱动**（并补上原先漏掉的 1.4.8）；`Taikou / TaikouAnim / Shokuho_CNs / LivingWorldNpcs` × 三客户端 = **12 格全 `[OK]`**。⚠️ 联接只是让模块可见，**要不要加载由启动器勾选决定** |
| **战役模式移植到 1.3.x+（2026-09-23）** | ✅ | 三处 `#else` 空壳补成真实现（`LivingWorldCampaign` 出生点置位 / `LivingWorldCampaignGameManager.OnLoadFinished` / `LivingWorldCharacterCreationContent` 的 1.3.x 形态）。1.3.15 编译 0 错 0 警；1.4.8 与 1.5.2 逐成员签名核对一致。**1.2.12 分支一行未动**。差异清单见下方「1.3.0 变更」表（新增 `MobileParty.Position2D` 一行）；接入点全貌见必备清单 ⑧；移植中撞出的新雷 = 必备清单 160~163。**实机验证待做** |
| csproj 累积阈值宏 | ✅ | v1.3.x → `MB2_V1212`+`MB2_GE_130` 自动侦测，无需改动 |
| VersionCompat.cs 注册表注释 | ✅ | CommissionHubIssue 行更新为三分支说明 |

### ❌ 待办

| 项目 | 优先级 | 说明 |
|------|:------:|------|
| 🔴 **距离缓存放到 1.3.x 的新位置** | **P0** | 雷 164：1.3.x 读 `ModuleData/DistanceCaches/settlements_distance_cache_Default.bin`，Taikou 的还在 1.2.12 的老路径 ⇒ 缓存没注册 ⇒ `CalculateAverageDistanceBetweenTowns` NRE。**先对头部字节判格式**（相同 = 复制改名；不同 = 开 1.3.15 ModKit 用 `SettlementPositionScript` 的 `ComputeAndSaveSettlementDistanceCache` 重算）。⚠️ 模块加载顺序必须让 Taikou 排在 SandBox 之后 |
| **实机复验 1.3.15 / 1.4.8 开局** | **P0** | 缓存修好后：两个客户端各勾 Taikou 开一局（选两个不同时代）→ 验「进建号/选人 → 落地进大地图、身份/家族/部队正确」。⚠️ **别拿编给 1.3.15 的 DLL 去跑 1.5.3**（2026-09-23 18:36 那次原生 AV 就是这么来的，与本次移植无关） |
| **1.2.12 分支回归编译** | **P0** | 三处 `#if MB2_V1212` 分支本轮一行未动，但共用代码有改动（`OnLoadFinished` 合成一份、新增 `V.SetMainPartyPosition`）→ 需在 1.2.12 机上 `dotnet build` 复核 |
| `GameDatabase.Initialize` 的 `KeyNotFoundException` | **P1** | 必备清单**雷 159**：1.3.15 上策划表数据没加载（引擎日志每次启动一行，被自己的 catch 吞掉）。**先查真因再修**，别加兜底补丁 |
| 同类「越界」补丁收窄 | P2 | 必备清单**雷 156**：`BackstoryCampaignBehaviorPatch` / `CharacterCreationCultureStageSortPatch` / `CharacterCreationCultureVisualFallbackPatch` / FaceGen 三个（`FaceGenOnSelectRaceGuard` / `FaceGenRaceDefaultBodyPatch` / `FaceGenRaceGenderFilterPatch`）→ 按"没装内容包时它有意义吗"逐条收窄 |
| `EncyclopediaHeroHelmetPatch` 机制坐实（可选） | P3 | 雷 156 记着「机制未查明」：把 `GameTextsFindNullProbe` 排到**挂载顺序最前**再复现一次，抓"谁在 GameTexts 未初始化时提前碰了它"的调用栈 |
| **v1.2.12 编译验证** | 🔴 P0 | 在 v1.2.12 电脑上 `dotnet build -c Release` 确认 0 errors |
| ~~v1.4.6+ 编译验证~~ | ✅ | 已随 v1.4.8 开发机验证；1.5.1 升级后再度验证（RaidSettlement 5 参 / requiredGold 分支均编译通过） |
| ~~发布策略确认~~ | ✅ | **已确认：三版全出**（1.2.12 / 1.3.15 / 1.5.x 各一台机器出 DLL；1.4.x 成为历史，需要时可用 MB2_1.4.8 备份客户端临时编译） |
| CampaignAgentComponent 死代码 | 🟢 P2 | MyCommands.cs 中 4 处被 `#if false` 包裹，需找到正确 API 或彻底删除 |

---

## ✅ P0：让 Taikou 在 1.3.15 / 1.4.8 上能开局（2026-09-23 立 → 代码当日完成 → 实机又挖出一颗新雷）

**目标（一句话）**：1.3.15 / 1.4.8 客户端上勾选 Taikou → 主菜单「剧本」→ 选时代 → **进建号/选人 → 落地进大地图**（与 1.2.12 同等体验）。

**验收判据**：至少两个时代各开一局 —— 能进图、玩家身份/家族/部队正确。**存档读档不在本轮**（2026-09-23 裁定：本轮只到「能开局进图」）。

**代码结果**：三处 `#else` 空壳补成真实现；1.3.15 `dotnet build` **0 错 0 警**；1.4.8 / 1.5.2 用反编译**逐成员核对签名**（与 1.3.15 一字不差）；**1.2.12 分支一行未动**。

### 🔴 实机结果（2026-09-23 18:55，1.3.15 客户端）

**移植本身全通**——链路一直走到世界加载的最后一段才停：

| 时点 | 日志 | 判定 |
|---|---|---|
| 18:55:25.844 | `[MenuSoundtrack] 主题重映射：MainTheme(5) → 10000005` | **主菜单 ✓**（含 `WireMainMenu` 改列表，1.3.x 上没炸） |
| 18:55:32.109 | `[EraCatalog] 已选剧本：TaikouCampaign1560` | **剧本入口 + 选时代 ✓** |
| 18:55:32.178 | `[HeroSelect] 选人屏已开（年份 1560，树模式）` | **选人屏 ✓** |
| 18:55:35.691 | `[StartingHero] 已选开局英雄：lord_tk5_195` | **选人落地交接 ✓** |
| 18:55:36.829 | `[LWN-cc13] 建号内容行为已挂入战役` | **新写的建号接线 ✓** |
| 18:55:36.9x | 💥 | **卡在世界加载** |

崩点（用户带调试器抓到）：`Campaign.CalculateCachedValues → CalculateAverageDistanceBetweenTowns` →
`DefaultMapDistanceModel.GetDistance(...)` **NullReferenceException**。

### 🔴 新拦路石 = 距离缓存换了存放位置（雷 164，**数据活，不是代码活**）

1.3.x 引擎找距离缓存的路径与 1.2.12 **完全不同**：

| 版本 | 引擎实际去读的路径 | 文件格式 |
|---|---|---|
| 1.2.12 | `Modules/<模块>/ModuleData/settlements_distance_cache.bin` | 旧格式（该版 `SandBox.View.dll` 里**没有** `SandBoxNavigationCache` 类型） |
| **1.3.x / 1.4.x** | `Modules/<模块>/ModuleData/**DistanceCaches**/settlements_distance_cache_**Default**.bin` | 新格式 `SandBoxNavigationCache`（官方 SandBox 就是这么放的，2.5MB） |

Taikou 的缓存文件**在，也在 junction 里**（文件没丢），只是待在 1.2.12 的位置上 ⇒ **1.3.x 扫过去不看那一格**。
引擎找不到时走 `SettlementPositionScript.OnInit` 的兜底，而兜底一旦抛异常就被那个 `catch` 吞掉
⇒ 距离缓存永不注册 ⇒ 后面 `GetDistance` 空引用（**日志零线索**，与雷 156 同一个套路）。

**下一步（用户 2026-09-23 定）**：开 **1.3.15 的 ModKit**，用地图场景里 `SettlementPositionScript` 实体自带的
`ComputeAndSaveSettlementDistanceCache` 开关**重新生成 1.3.x 格式的缓存**并放到新路径。
⚠️ 动手前先做**一分钟判据**：拿老文件和新路径官方文件的**头部字节**对一下 —— 若格式相同，复制改名即可（不用开 ModKit）；
不同才需要重算。⚠️ 另注意引擎那段循环是**最后一个命中者胜**（`text = filePath` 覆盖），
所以 Taikou 的模块加载顺序必须排在 SandBox / SandBoxCore 之后。

**为什么当时不行（已解决，留档）**：`CampaignMode/` 那套是 `#if MB2_V1212` 全量实现 / `#else` 空壳 ⇒ 世界**建得起来**（内容包数据、EquipmentRosters 补载都跑），但 `LivingWorldCampaignGameManager.OnLoadFinished` 在 1.3.x 分支上**只有 `base` 一句** ⇒ **卡在加载完成后**（不是崩）。

| # | 做什么 | 验收 | 状态 |
|---|---|---|---|
| **T0** | **调研 1.3.x 建号体系的接入点**（当时的唯一未知量） | ✅ **链路查明**（1.3.15 反编译）：`CharacterCreationState`（无参构造）→ 内部 new `CharacterCreationManager` → 构造期广播 `OnCharacterCreationInitializedEvent` → 战役行为在此注册 `ICharacterCreationContentHandler` → 随即逐个调 `InitializeContent`（加阶段/文化/菜单）。**3D 界面由游戏自带**：`SandBox.View.dll` 的 `CharacterCreationScreen` 带 `[GameStateScreen(typeof(CharacterCreationState))]`，状态一激活自动挂上，再按**阶段类型**配 view ⇒ **只复用原版阶段类型，零自建 UI**。优先级：原版内容 800 / 剧情 900 / 我们 1000（要等原版建完菜单才删得掉）。范本 = 原版 `CharacterCreationCampaignBehavior` + 剧情 `StoryModeCharacterCreationCampaignBehavior`。链路与接口全貌已沉淀进必备清单 ⑧（新内容包/新版本照它做） | ✅ |
| **T1** | `LivingWorldCampaign.OnInitialize` 的 1.3.x 版 | ✅ EquipmentRosters 补载（原样保留）+ **出生点置位**（挂 `OnNewGameCreatedPartialFollowUpEvent`，i==0 时 `V.SetMainPartyPosition`）。**家宅/王都那套不移植** —— 反编译查明 `Kingdom.InitialHomeSettlement` 在 1.3.x **全代码库无人读**，1.2.12 那个 NRE 隐患不存在了（必备清单雷 163）。另查明：1.3.x 引擎会按 `Culture.StartingPoint` 覆写主队位置，而内容包 `spcultures.xml` 没写 `start_point_position_*` ⇒ 不会冲掉我们的坐标（雷 162） | ✅ |
| **T2** | `LivingWorldCampaignGameManager.OnLoadFinished` 的 1.3.x 分支 | ✅ **与 1.2.12 共用一份实现**（本体用到的 API 两版一致，已逐条核对）；唯一版本差异收在 `PushCharacterCreation()` 内部：1.2.12 把内容实例当构造参数传 / 1.3.x 无参 `CreateState<CharacterCreationState>()` + `CleanAndPushState(state, 0)`（范本 = `SandBoxGameManager.LaunchSandboxCharacterCreation`） | ✅ |
| **T3** | `LivingWorldCharacterCreationContent` 的 1.3.x 形态 | ✅ 改成 **`CampaignBehaviorBase` + `ICharacterCreationContentHandler`**（优先级 1000），由 `MySubModule.OnGameStart` 挂进战役。复刻 1.2.12 的四阶段：摘掉引擎默认的 家纹/家名/难度 三阶；文化表按 `IsMainCulture` 自己填（雷 160）；删原版 6 个叙事菜单、换成自建出身菜单（雷 161） | ✅ |
| **T4** | `StartingHero.FinalizeCampaignStart` 的 1.3.x 版收尾 | ✅ **无需改动**——它照抄的四步（撤销禁用请求 / 推 MapState / `SetVisualAsDirty` / 广播 `OnCharacterCreationIsOver`）在 1.3.15 全部存在且语义相同。1.3.x 走「自定义人物」那条路时，收尾由引擎自己的 `CharacterCreationState.FinalizeCharacterCreationState` 完成（与我们的实现同构） | ✅ |
| **T5** | 复查 1.2.12-only 守卫在 1.3.x 的等价物 | ⚠️ **部分**。三个守卫（`KingdomOnNewGameCreated` / `MapDistanceNullSettlement` / `MapDistanceInvalidFace`）在 1.3.x 已被 `#if MB2_V1212` 圈掉 —— 但**「补丁被圈掉」≠「问题不存在」**（本轮教训）。实机证明 **1.3.x 上距离模型照样出事**（只是形态变了：不再是「无地势力 null」或「非法导航面」，而是**距离缓存压根没注册**→ 雷 164）。旧的 1.2.12 守卫**不要恢复**，按 1.3.x 的机制另做（优先走数据：把缓存文件放对地方）。另：两个 CC 补丁（`CharacterCreationCultureStageSortPatch` / `CharacterCreationCultureVisualFallbackPatch`）已加进 `contentPackOnly` 名单 —— 它们只对内容包世界有意义，挂在原版战役上会**实打实改掉原版文化的排序**（雷 156 同族） | ⚠️ |

**已知风险 / 纪律**
- `EraCatalog.StartCampaign` 走 `MBGameManager.StartNewGame(new LivingWorldCampaignGameManager(era))` —— 1.3.x 同签名**编译已过** ✓，但**运行期行为要实测**（1.3.x 的 GameManager 装配链可能不同）。
- 🔴 **第一次实机若崩在 `CharacterCreationNarrativeStageView` 构造**（`…GainedPropertiesVM..ctor → CampaignUIHelper..cctor → GameTexts.FindText`）：先照必备清单**雷 156** 的手法二分 —— `config.json` 的 `DisabledPatchClasses` 加 `EncyclopediaHeroHelmetPatch`（改配置重启即生效、零重编）。雷 156 说那个崩溃的**机制未查明**，而它的触发路径恰好是「叙事阶段 + 内容包补丁挂着」，**正是我们这次要走的路**。
- 🔴 **移植没做完之前不要用 Taikou 测 1.3.x "兼容性"**（雷 158）—— 本条已解除：战役模式已接，现在测出来的问题**都是有诊断价值的真问题**。
- **一次只打通一个版本**：先在 1.3.15 上走通，再验 1.4.8（两者 API 一致）。
- **数据层不用动**（内容包纯数据 + `EquipmentSet` 民用标记双属性已合规）。
- 三个守卫补丁在 1.3.x 上是 `#if MB2_V1212` 圈掉的 ⇒ 若 1.3.x 实测发现仍需同类保护，**按 1.3.x 的 API 重写**，不要去恢复 1.2.12 的补丁。
- **本轮不含存档读档**（2026-09-23 裁定）⇒ 「继续战役」按钮仍是禁用的；下一轮做存档时按 `plans/version-compat-plan.md` 的 Saveable 纪律走。

---

## 核心原则

**不支持跨版本编译。** 不要试图用一台装了 v1.3.15 的电脑去编译 v1.2.12 的 DLL，反之亦然。API 差异不是简单的 `#if` 能完全隔离的（DLL 引用本身就不兼容）。

## 三锚点编译工作流

每台电脑装一个目标版本，**同一份源码**，分别在每台电脑上编译，产出多份 DLL，分别打包发布（标注版本号）。
**每台电脑的实际版本 = 其 `MB2_PATH` 指向的游戏安装版本（csproj 自动读 Version.xml 检测，无需手动指定）**：

| 机器 | 游戏版本 | 产出 |
|------|---------|------|
| A | v1.2.12 | `LivingWorldNpcs.dll`（v1.2.12 版） |
| B | v1.5.x（当前开发机，实测 v1.5.1） | `LivingWorldNpcs.dll`（Latest 版） |
| C | v1.3.15（备份客户端 MB2_1.3.15） | `LivingWorldNpcs.dll`（v1.3.15 版） |

### 🔴 必选检查项：备份客户端 Modules 下必须有 LivingWorldNpcs junction（2026-08-24）

备份游戏（`MB2_Version\MB2_1.2.12` / `MB2_1.3.15` / `MB2_1.4.8`）**不做独立拷贝**，
`Modules\LivingWorldNpcs` 一律是 **junction** 指向主游戏
`H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs`
（三份已建，2026-08-24 核对：1.2.12 ✅ / 1.3.15 ✅ / 1.4.8 ✅）。

**每次出现新版本备份目录 / 换机 / 目录改动后，编译前必查**（缺失 = 游戏加载不到 mod；
普通目录 = 版本隔离失效，改源码两处不同步）：

```powershell
Get-Item "<备份版>\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs" -Force | fl LinkType, Target
# 期望：LinkType=Junction，Target=主游戏 LivingWorldNpcs 路径；Test-Path 为 False = 死链需重建
```

创建命令：

```powershell
New-Item -ItemType Junction -Path "<备份版>\...\Modules\LivingWorldNpcs" -Target "<主游戏>\...\Modules\LivingWorldNpcs"
```

## 版本检测机制（自动，无需手动干预）

csproj 在编译时自动读取本地游戏的 `Version.xml`：

```
$(MB2_PATH)\bin\Win64_Shipping_Client\Version.xml
```

### 累积阈值宏体系（GE = "Greater or Equal"）

**不做精确版本匹配，而是定义"从哪个版本开始有这个 API"的累积宏**：

| 本地游戏版本 | 自动定义的宏 |
|-------------|------------|
| v1.2.12 | `MB2_V1212` |
| v1.3.x | `MB2_GE_130` |
| v1.4.x | `MB2_GE_130` + `MB2_GE_140` |
| v1.5.x | `MB2_GE_130` + `MB2_GE_140` + `MB2_GE_150` |

🔴 `MB2_V1212` 是**精确匹配** v1.2.12（csproj 判定 = Version.xml.Contains('v1.2.12')），不是累积宏；
`#else` 分支语义 = "≤ v1.2.12"（v1.2.12 是支持的最低版本）。只有 GE_* 宏才是累积的。

代码按阈值从高到低写分支：`#if MB2_GE_150 / #elif MB2_GE_140 / #elif MB2_GE_130 / #else`。

🔴 **1.5.x 已实机验证（2026-08-23）**：开发机升级 v1.5.1 后 `dotnet build -c Debug` **0 错误 0 警告**——
项目全部 API 在 1.5.x 签名与 1.4.x 一致，`MB2_GE_130`/`MB2_GE_140` 分支继续覆盖；27 个 Harmony
字符串补丁目标二进制 grep 全存活（唯一 MISSING 为 1.2.12-only 条件编译的 FillPartyStacks）。
`MB2_GE_150` 已自动定义但**尚无代码分支使用**。

## 🔴 三锚点验证结论（2026-08-03；2026-08-23 增补 1.5.1 第四锚点）

之前只有 1.2.12 / 1.4.6 两个端点，所有差异都归到 `MB2_GE_130` 且无法验证 1.3.x 中间形态。
现在有 **1.2.12 / 1.3.15 / 1.4.6 / 1.5.1 四个锚点**（1.3.15 用游戏目录 DLL，1.2.12/1.4.6 用备份 DLL，
1.5.1 用升级后的开发机），用 ilspycmd 逐 API 反编译对比，结论：

🔴 **1.5.1 第四锚点验证（2026-08-23）**：开发机升级 v1.5.1（Latest，Version.xml 实测）后
`dotnet build -c Debug` **0 错误 0 警告**——项目全部 API 在 1.5.x 签名与 1.4.x 一致；
27 个 Harmony 字符串补丁目标二进制 grep 全存活（唯一 MISSING = 1.2.12-only 条件编译的
`FillPartyStacks`，预期）。`MB2_GE_150` 已自动定义但尚无代码分支使用；今后发现 1.5.x
独有差异时在 VersionCompat.cs 对应方法插入 `#if MB2_GE_150` 分支即可（阈值从高到低排）。

### 与 1.4.6 一致的 API（`MB2_GE_130` 分支正确，无需改动）

以下 API 在 **1.3.15 与 1.4.6 签名完全一致**（均已反编译验证）：

- `MobileParty.GetPosition2D` / `Position`(CampaignVec2 setter) / `SetMove*` 家族 / `MoveTargetParty` / `CreateParty(2参)` / `InitializeMobilePartyAtPosition(CampaignVec2)`
- `DestroyPartyAction.Apply` / `ChangeKingdomAction.ApplyByJoinToKingdomByDefection(5参带CampaignTime)`
- `Kingdom.CurrentTotalStrength` / `Kingdom.All` + `IsAtWarWith`（`FactionManager.GetEnemyKingdoms` 已删）
- `Campaign.Models.CampaignTimeModel.CampaignStartTime` / `TextObject.GetEmpty()`
- `Agent.IsAIControlled` / `AgentControllerType`（1.3.15 定义在 **TaleWorlds.Core.dll**）/ `GetPrimaryWieldedItemIndex` / `GetCurrentActionType`
- `Mission.RayCastForClosestAgent(out 在最后)` / `Scene.RayCastForClosestEntityOrTerrain(out WeakGameEntity)` / `GetNavigationMeshForPosition(in, UIntPtr)`
- `GauntletLayer(string, int)` 构造 / `LoadMovie` 返回 `GauntletMovieIdentifier`
- `SetPartyAiAction.GetActionForPatrollingAroundSettlement(5参)` / `BesiegingSettlement(4参)` / `EngagingParty(4参)`
- `ChangeKingdomAction.ApplyByJoinToKingdom`（4参带 CampaignTime）/ `EndCaptivityAction.ApplyByEscape`（3参带 showNotification）/
  `CampaignEvents.HeroPrisonerReleased`（5参带 bool isPlayer）/ `CampaignEvents.BeforeHeroesMarried`（1.2.12 无此事件，同名同签名为 `HeroesMarried`）/
  `MapWeatherModel.WeatherEvent.Storm`（枚举成员 1.3.0+ 新增，1.2.12 无）—— **2026-08-17 三版本 ilspycmd 实锤**，
  新增 `V.JoinKingdom` / `V.EndCaptivityEscape` / `V.WeatherWord`（Storm 分支必须 `#if MB2_GE_130`）
- `IMapScene.GetAccessiblePointNearPosition(in CampaignVec2)` / `GetFaceIndex(in CampaignVec2)` / `GetPathDistanceBetweenAIFaces(10参)`
- `IMapStateHandler.StartCameraAnimation(CampaignVec2, float)`
- 注册表各位置：`OnRegisterBlow(WeakGameEntity)` / `GetDefaultComponentBanner` / `GameMenu.MenuOverlayType` / `MissionObject.GameEntity→WeakGameEntity` / `AgentInteractionInterfaceVM(Missions.Interaction)` / `DisguiseMissionLogic`+`StealthFailCounterMissionLogic` / `MobilePartyHelper.FillPartyManuallyAfterCreation` / `SandBox.Missions` 命名空间

### 🔴 需要 `MB2_GE_140` 分支的 API（1.3.15 ≠ 1.4.6）

| API | 1.2.12 | 1.3.15 | 1.4.6 | 处理 |
|-----|--------|--------|-------|------|
| `SetPartyAiAction.GetActionForRaidingSettlement` | 2 参 | **4 参**（navType, isFromPort） | **5 参**（+isTargetingPort） | `V.RaidSettlement` 三分支 ✅ |
| `IssueBase.CanPlayerTakeQuestConditions` | 4 参 | **4 参**（同 1.2.12） | **5 参**（+out int requiredGold） | CommissionHubIssue override 三分支 ✅ |

**注意**：这两个 API 的 1.3.x 形态是 1.3.x **独有**（既不是 1.2.12 也不是 1.4.6）——
RaidingSettlement 的 4 参版本、CanPlayerTakeQuestConditions 的 4 参版本只在 1.3.x 存在。
`MB2_GE_140` 不再只是"预留"，已是实际使用的分支。

### 1.2.12 独有（`#else` / `MB2_V1212` 分支，已验证 1.3.15 无）

`MobileParty.Position2D`(Vec2 setter) / `Ai.SetMove*` / `Ai.MoveTargetParty` / `CreateParty(3参)` / `RemoveParty()` /
`Agent.ControllerType` 嵌套枚举 / `TextObject.Empty` / `GetWieldedItemIndex(HandIndex)` / `FactionManager.GetEnemyKingdoms` /
`RayCastForClosestAgent(out 在第3位)` / `Scene.RayCastForClosestEntityOrTerrain(out GameEntity)` /
`GetNavigationMeshForPosition(ref, bool)` / `GauntletLayer(int, string)` / `ChangeKingdomAction(3参)` /
`IMapScene.AreFacesOnSameIsland` / `SetPartyAiAction.GetActionFor*(2参)` / `Vec2` 版 IMapScene/StartCameraAnimation /
`InventoryManager.OpenScreenAsLoot`（搜刮流）/ `FillPartyStacks` /
`Mission.Missiles`（1.3.0+ 改名 `MissilesList`）/ `Texture.SaveToFile(1 参)` / `MobileParty.TargetPosition`(Vec2) /
`CharacterCreationContentBase`（整类，1.3.0+ 无）/ `Scene.GetNavMeshFaceIndex`(4 参，无 `isRegion1`) /
`Scene.GetPathBetweenAIFaces`(8 参带默认值) / `ScriptComponentBehavior.GameEntity` 返回 `GameEntity`(类)

### 🔴 1.3.0 变更（2026-09-23 实机编译补录）

背景：2026-09 的新功能（法印弹示踪 / NavMesh 调试 / 玩家飞行）只在 1.2.12 上编过，切到 1.3.15 一次炸 18 处。
结论：**1.3.0 是个真变更点，且两处报错信息完全不指向真因**。全部已收进 `V`：

| API | 1.2.12 形态 | 1.3.0+ 形态 | 收口 |
|-----|------------|------------|------|
| `Mission.Missiles` | 属性 → `IEnumerable<Mission.Missile>` | **改名** `MissilesList` → `MBReadOnlyList<Mission.Missile>` | `V.Missiles(mission)` |
| `MobileParty.TargetPosition` | `Vec2` | **同名换类型** → `CampaignVec2`（读值要 `.ToVec2()`） | `V.TargetPos(party)` |
| `MobileParty.Position2D` | `Vec2`，**可写** | **可写属性没了**（只剩只读的 `GetPosition2D => Position.ToVec2()`）→ 改写 `Position`（`CampaignVec2`，须裹 `new CampaignVec2(v, isOnLand: true)`） | `V.SetMainPartyPosition(v)` |
| `Texture.SaveToFile` | `(string path)` | **加参** → `(string path, bool isRelativePath)` | `V.SaveTextureToFile(tex, path)` |
| `Scene.GetNavMeshFaceIndex` | `(ref rec, Vec2, bool checkIfDisabled, bool ignoreHeight=false)` | **第 3 位插入** `bool isRegion1` → `(ref rec, Vec2, isRegion1, checkIfDisabled, ignoreHeight=false)` | `V.NavMeshFaceIndex(...)` |
| `Scene.GetPathBetweenAIFaces` | `(int,int,Vec2,Vec2,float,NavigationPath, int[]=null, float=1)` | 追加 `regionSwitchCostTo0/1`，**且原有两个默认值一并取消** → 必须补满 10 参 | `V.PathBetweenFaces(...)` |
| `CharacterCreationContentBase` | 建号内容基类（`Instance`） | **整类移除**（建号换成 `CharacterCreationManager`，无同名等价入口） | `V.NotifyCharacterCreationFinalized()`（非 1.2.12 = 空操作） |
| `ScriptComponentBehavior.GameEntity` | 返回 `GameEntity`（**类**，判空 `== null`） | 返回 `WeakGameEntity`（**结构体**，判空只能 `IsValid`） | 返回类型不同，封不进 V → 裸 `#if`（已登记合规例外） |

🔴 **两个「按报错找不到东西」的坑**：
① `GetNavMeshFaceIndex` 少写参数 → 编译器**不报「参数不够」，而是退到 `Vec3` 重载报「参数 2 无法从 Vec2 转换为 Vec3」**。按报错去找 Vec3 永远找不到真因。
② `GetPathBetweenAIFaces` 的默认值是**在 1.3.0 被取消**的（1.2.12 有默认值，所以 6 参调用在旧版能编）——**在 1.2.12 上永远复现不出来**。

⚠️ **遗留未核实项**：`isRegion1: false`。该参数 1.3.0 新增，语义未反编译核实（推断 = 导航区域 0/1，false = 主区域）。
当前只有 NavMeshDebug* 调试绘制消费它，日后实机看 navmesh 叠图时顺手核对即可。

## VersionCompat.cs：版本差异统一入口

[Core/VersionCompat.cs](../ExampleModVS/ExampleMod/ExampleMod/Core/VersionCompat.cs) — `V` 静态类封装了全部 API 差异。

**纪律**：
- 凡是跨版本 API 不同的调用，**一律走 `V.xxx()`**，禁止在业务代码里裸写 `#if`
- 新增 V 方法后，**必须在每台目标版本电脑上分别编译通过**
- 四锚点已验证：除 RaidSettlement 外所有 V 方法的 `MB2_GE_130` 分支覆盖 v1.3.0~v1.5.x 正确（1.5.1 编译验证，2026-08-23）；
  **2026-09-23 再补**：`Missiles`/`TargetPos`/`SaveTextureToFile`/`NavMeshFaceIndex`/`PathBetweenFaces`/`SameIsland`/`NotifyCharacterCreationFinalized`
  七个新 V 方法已在 **1.3.15 / 1.2.12 / 1.5.x 三档分别编译通过**（`dotnet build -c Debug` 三绿）
- 遇到 1.3.x 与 1.4.x 不同而 1.3.x 与 1.2.12 相同的 API（如 `CanPlayerTakeQuestConditions`），**必须用 `MB2_GE_140` 三分支**，不能沿用 `!MB2_V1212` 二分

### 不可迁入 V 的 #if（合规例外登记表）

以下类别的 `#if` **不能**封装为 `V.xxx()` 方法，直接写在业务文件里是合法的。每次新增版本时必须逐条核查：

| 类别 | 文件:行号 | 原因 |
|------|----------|------|
| override | `SafeLordPartyComponent.cs:41` | `GetDefaultComponentBanner()` 只存在于 1.3.0+ 基类虚方法 |
| override | `CustomPartyComponent.cs:47` | 同上 |
| override | `AttackTriggerMissionLogic.cs:391` | `OnRegisterBlow` 第三参 `GameEntity`→`WeakGameEntity`（1.3.15 已验证） |
| override | `CommissionHubIssue.cs:413` | 🔴 `CanPlayerTakeQuestConditions`：**1.2.12~1.3.x 4 参 / 1.4.x 5 参**，用 `MB2_GE_140` 三分支（不是 `!MB2_V1212` 二分） |
| type | `MySubModule.cs:344` | 字段类型 `IGauntletMovie`→`GauntletMovieIdentifier`（1.3.15 已验证） |
| type | `CameraDebuggerView.cs:34` | 同上 |
| type | `SpringArmCameraView.cs:40` | 同上 |
| type | `NinjaNotificationMissionView.cs:19` | 同上 |
| type | `MyCommands.cs:646` | `MissionObject.GameEntity` 返回 `WeakGameEntity`（1.3.15 已验证） |
| type | `FlySpike.cs:2385` | `UsableMissionObject.GameEntity`：1.2.12 返回 `GameEntity`(类) / 1.3.0+ 返回 `WeakGameEntity`(**结构体** → 判空必须 `IsValid`)。返回类型不同，封不进 V |
| type | `PlayerDetentionBehavior.cs:9,358` | `GameOverlays.MenuOverlayType`→`GameMenu.MenuOverlayType`（1.3.15 已验证） |
| Harmony | `InteractionMissionView.cs:2550` | F-to-talk 补丁：`AgentInteractionInterfaceVM` 命名空间从顶层移到 `Missions.Interaction`（1.3.15 已验证） |
| Harmony | `InteractionMissionView.cs:2582` | 村庄交易日志补丁：`InventoryManager.OpenScreenAsTrade` 三版本都存在（1.2.12 第 4 参 `DoneLogicExtrasDelegate` vs 1.3.15+ `Action`），补丁只在 1.2.12 编译，功能缺失不影响 |
| Harmony | `DebugLogger.cs:18` | `FillPartyStacks`→`FillPartyManuallyAfterCreation`（1.3.15 已验证 MobilePartyHelper 存在） |
| Harmony | `KingdomOnNewGameCreatedGuardPatch.cs` | 目标 `Kingdom.OnNewGameCreated` **1.3.0 起被引擎删除**（1.3.15 / 1.4.6 实测均无）→ 整类 `#if MB2_V1212` |
| Harmony | `MapDistanceNullSettlementGuardPatch.cs` | 目标 `DefaultMapDistanceModel.GetDistance(Settlement,Settlement)` **1.3.0 起无 2 参重载**（只剩 5/6 参形态）→ 整类 `#if MB2_V1212` |
| Harmony | `MapDistanceInvalidFaceGuardPatch.cs` | 目标 `GetClosestSettlementForNavigationMesh(PathFaceRecord)` **1.3.0 起被引擎删除** → 整类 `#if MB2_V1212` |

### 🔴🔴 Harmony 补丁目标找不到 = 抛异常掐断整个 PatchAll（2026-09-23 实机崩溃）

**结论**：`[HarmonyPatch(typeof(X), "方法名")]` 的目标解析不到时，Harmony **不是静默跳过**，而是
`throw ArgumentException("Undefined target method ...")` → 异常冒到 `OnSubModuleLoad` →
**① 排在该补丁类之后的所有补丁全部没打上；② `OnSubModuleLoad` 里 `PatchAll` 之后的代码一行都不执行**
（实机连带跳过：伤害模型补丁 / SwordBeam 补丁 / 崩溃钩子 / 动画状态机注册 / `GameDatabase.Initialize`）。

**证据**：反编译 0Harmony `PatchClassProcessor.PatchWithAttributes` —— 抛点原文可见：
```csharp
lastOriginal = patchMethod.info.GetOriginalMethod();
if ((object)lastOriginal == null)
    throw new ArgumentException("Undefined target method for patch method " + ...);
```

**同族两个坑**（同一处反编译实锤，修法选择时要知道）：

| 写法 | 目标解析不到时 |
|---|---|
| 属性式 `[HarmonyPatch(typeof(X),"Y")]` | 🔴 抛 `ArgumentException`，掐断 PatchAll |
| `[HarmonyTargetMethod] static MethodBase TargetMethod()` 返回 null | 🔴 **也抛**（`"returned an unexpected result: null"`） |
| `[HarmonyTargetMethods]` 返回**空集合** | ✅ 真·静默跳过（唯一安全的降级写法） |

⚠️ 由此推论：现有两处"返回 null 就算跳过"的写法（`SaveGuard.cs` 的 `ObjectSaveToPatch` / `VariableSaveToPatch`）
**并不安全**，只是它们的目标在 1.2.12~1.5.x 四版都还在才没炸。改这两个补丁时要注意。

**自查脚本**：`Debug/offline/_check_harmony_targets.ps1`（扫**编译产物**的 `[HarmonyPatch]` 属性并逐个核目标存在性——
扫产物而非扫源码，是因为 `#if` 掉的东西本来就不在 DLL 里，扫源码会误报）。
🔴 两个已知陷阱：① 只能算带 `[HarmonyPrefix]`/`[HarmonyPostfix]`/`[HarmonyTranspiler]`/`[HarmonyFinalizer]` 的方法，
否则补丁类里的**普通辅助方法**会被当成补丁方法去核目标（第一版就这么误报了 7 条）；
② PowerShell 管道会把 `Type[]` **展开**，取签名必须用 `foreach` 赋值而不是 `| Select -First 1`（否则签名只剩第一个类型，又误报一轮）。

**当前状态（2026-09-23 修完后）**：1.2.12 = 66 个属性式目标全存活；1.3.15 / 1.5.x = 63 个全存活（少的 3 个 = 上表三个 1.2.12-only 守卫）；动态目标 4 个（`CommandLineFunctionality.CallFunction` ×2 / `ObjectSaveData.SaveTo` / `VariableSaveData.SaveTo`）在 1.3.15 实测全部存活。
| structural | `WorldEventSimulator.cs:1668,1719` | `AreFacesOnSameIsland` 移除（1.3.15 已验证）；`GetPathDistanceBetweenAIFaces` 1.3.15 已是 10 参 |
| structural | `MyBehavior.cs:33,45` | `CampaignEvents` 事件注册差异（2026-08-17 三版本实锤）：`HeroPrisonerReleased` 4参(1.2.12) / 5参(1.3+，lambda 适配)；`BeforeHeroesMarried` 1.3+ / 1.2.12 为同名同签名 `HeroesMarried`（婚后触发） |
| structural | `InteractionMissionView.cs:1930,2385` | 搜刮 Loot 流（`InventoryManager.OpenScreenAsLoot` 1.2.12 only，1.3.15 走自研 fallback） |
| structural | `MyCommands.cs:1619` | stealth_debug 命令（`DisguiseMissionLogic` 等 1.3.15 已存在，同 1.4.6） |
| namespace | `MyCommands.cs:30` | `SandBox.Missions` 命名空间三版本都存在，仅 1.2.12 用不上 |

### 🔴 数据层跨版本差异（XML 属性名，不是 C# —— 2026-09-12 新增）

数据文件（内容包 XML）两个客户端**共用同一份**（junction 同源），所以凡「引擎两版本读不同属性名」的地方，**两个属性都得写**，否则某个版本静默语义错：

| 位置 | v1.2.12 读 | v1.5.x 读 | 写法（两边都对） | 错写的后果 |
|------|-----------|-----------|------------------|-----------|
| `EquipmentSet`（NPC `<Equipments>` 里 / 装备集定义里）的民用标记 | `civilian="true"`（bool） | `equipmentType="Civilian"`（枚举） | `equipmentType="Civilian" civilian="true"` | 只写一个 → 另一版本把民用装备当**战斗**装备收下（城镇里穿甲带刀） |

反编译出处（两版各自实证）：`MBEquipmentRoster.InitEquipment` + `BasicCharacterObject.Deserialize` 的 `EquipmentSet` 分支。
Taikou 实例：`taikou_equipment_sets.xml` 的 `taikou_civil_common` / `taikou_civil_bandit`（两个属性都写 ✓）；
既有的 `taikou_civil_gangster_t1..3` 只写了 `equipmentType`（在 1.2.12 上当战斗变体用，效果正确）——**同一份数据两版本语义不同，待统一**（改时要连 `<EquipmentSet id=…>` 引用一起改）。

## Modules/ 目录：仅用于 ilspycmd，不参与编译

| 目录 | 版本 | 用途 |
|------|------|------|
| `Modules/1.2.12DLL/` | v1.2.12 | **反编译对比 API 差异**（在任意电脑上查 1.2.12 的方法签名） |
| `Modules/1.3.15DLL/` | v1.3.15 | **反编译对比 API 差异**（在非 1.3.15 电脑上查 1.3.15 的签名） |
| `Modules/1.4.6DLL/` | v1.4.6 | **反编译对比 API 差异**（1.4.x 历史锚点；1.4.6/1.4.7/1.4.8 签名一致） |
| `Modules/1.5.1DLL/` | v1.5.1 | 🔴 **反编译查 Latest 的 API 签名**（2026-08-23 备份；可代表整套 1.5.x） |

🔴 **跨版本比签名：优先反编译（ilspycmd）或「一个版本一个进程」的反射，禁止在一个进程里连续 `Assembly.LoadFrom` 多个版本**（2026-09-23 自踩）：
`LoadFrom` **按程序集标识复用已加载实例**（各版本 `TaleWorlds.*.dll` 的标识都是 1.0.0.0，完全一样）——
同一进程里先加载 1.2.12 再加载 1.3.15，第二次拿回的还是 **1.2.12 那个对象**，
**症状 = 几个版本的查询结果一字不差**（看起来"四版签名完全一致"，其实全在复读第一个版本）。
判据就是这句「一字不差」：正常情况四个版本不可能完全相同。要批量查就用 `ilspycmd`，或用反射时**每个版本单起一个 PowerShell 进程**。

```bash
# 对比四个版本的同个方法
ilspycmd Modules/1.2.12DLL/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
ilspycmd Modules/1.4.6DLL/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
ilspycmd Modules/1.5.1DLL/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
# 当前机器的 1.3.15 用游戏目录 DLL：
ilspycmd bin/Win64_Shipping_Client/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
```

**这些 DLL 不参与编译。** 不要试图用备份 DLL 配置去交叉编译——该配置已废弃。

**ilspycmd 注意**：`-t` 参数一次只能传一个类型（传多个会参数解析失败，输出 "Specify --help"）。
类型全名以 `ilspycmd <dll> -l c`（类）/ `-l e`（枚举）列出的为准，例如 `MobileParty` 的全名是
`TaleWorlds.CampaignSystem.Party.MobileParty`（中间有 `Party`），`AgentControllerType` 定义在
`TaleWorlds.Core.dll` 而非常见的 MountAndBlade。

## 发布步骤

```bash
# 任意电脑：版本 = 本机 MB2_PATH 指向的游戏版本（自动检测）
dotnet build -c Release
# → 本机游戏版本的 DLL（版本见 Version.xml）

# 发布多版本：到对应版本的电脑上
git pull
dotnet build -c Release
# → 该电脑游戏版本的 DLL
```

各版本 DLL 分别打包，发布时标注版本号。

## 新增游戏版本时

TaleWorlds 出新版本时，执行以下检查清单（🔴 v1.5.0 检查已按此清单执行完毕，2026-08-23 记于各步）：

### 1. 更新 csproj 版本侦测
```xml
<!-- 新增版本系列侦测 -->
<MB2_IsV15x Condition="$(MB2_VersionFileContent.Contains('v1.5.'))">true</MB2_IsV15x>
<!-- 已有 GE_* 的 Or 链追加新版本 -->
<MB2_VersionDefines Condition="... Or '$(MB2_IsV15x)' == 'true'">...</MB2_VersionDefines>
<!-- 新增 GE_150 阈值 -->
<MB2_VersionDefines Condition="'$(MB2_IsV15x)' == 'true'">$(MB2_VersionDefines);MB2_GE_150</MB2_VersionDefines>
```
✅ v1.5 侦测已提前就位（升级前 csproj 就带 v1.5 分支），升级后自动生效，未改动。

### 2. 对比 API 差异
用 ilspycmd 逐条对比 VersionCompat.cs 中所有 `V` 方法涉及的 API：
```bash
ilspycmd Modules/1.5.1DLL/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
ilspycmd bin/Win64_Shipping_Client/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
```
✅ v1.5.1 实测：**编译 0 错误 0 警告**（快于逐条反编译——编译即覆盖全部用到的 API 签名），无需逐条 ilspycmd。

### 3. 根据差异等级行动

| 情况 | 行动 |
|------|------|
| 新版本 API 与 1.4.x 完全一致 | 无需改动，`MB2_GE_130`/`MB2_GE_140` 分支继续覆盖 |
| 某个 API 在新版本变更 | 在 VersionCompat.cs 对应方法中插入 `#if MB2_GE_150` 分支（阈值从高到低排） |
| 某个 API 在新版本被删除 | 可能需要 `#if !MB2_GE_150` 排除新版本，或新增 V 方法封装替代方案 |

✅ v1.5.1 = 第一行情形（签名完全一致），`MB2_GE_150` 无分支使用。

### 4. 核查 #if 注册表
逐条检查本文件「不可迁入 V 的 #if」登记表的每一行，确认：
- override/abstract 基类签名是否变化
- type-level 字段类型是否需要新增分支
- Harmony 补丁目标是否移动
- structural 差异是否需要调整算法

✅ v1.5.1：注册表逐条核查 + **27 个 Harmony 字符串补丁目标二进制 grep 全存活**
（唯一 MISSING = 1.2.12-only 条件编译的 `FillPartyStacks`，预期），注册表无改动。

### 5. 更新备份 DLL
把 `Modules/<旧 Latest>DLL/` 的 Latest 地位替换为新版本（旧版保留作历史锚点），更新此文档中的版本号引用。
✅ v1.5.1：已新建 `Modules/1.5.1DLL/`（41 文件，清单 = 三套旧备份并集；唯一缺失
`TaleWorlds.GauntletUI.TooltipExtensions.dll` 为 1.2.12-only，csproj Exists 守卫自动跳过）；
`Modules/1.4.6DLL/` 保留为 1.4.x 历史锚点。1.5.1 备份流程见 `.claude/skills/backup-version-dlls.md`。

## 踩过的坑（不要重犯）

1. **交叉编译**：试图在一台电脑上用备份 DLL 编译另一个版本的 DLL。DLL 引用级别就不兼容，编译报错会铺天盖地，且修复了也不代表运行时正确。
2. **正则批量替换 C# 代码**：嵌套括号、lambda 会错位。
3. **只在一台电脑上验证**：改完 VersionCompat.cs 必须每台目标版本电脑分别 build。
4. **`!MB2_V1212` 二分陷阱**：默认假设"1.3.x 与 1.4.x 一样"；遇到 1.3.x 与 1.2.12 相同的 API（如 `CanPlayerTakeQuestConditions`）时 `!MB2_V1212` 分支会编译失败（override 签名不匹配）。**有 1.3.x 的锚点前，此类差异不可见**——这正是本次三锚点验证的价值。
5. **GauntletLayer 参数顺序**：v1.2.12 是 `(int order, string name)`，v1.3.0+ 是 `(string name, int order)`——两个参数反了，不是增加/减少参数。
6. **ControllerType 枚举**：v1.2.12 是 `Agent.ControllerType` 嵌套枚举，v1.3.0+ 是 `TaleWorlds.Core.AgentControllerType` 顶层枚举（注意在 Core.dll，不在 MountAndBlade.dll）。
7. **ilspycmd 多类型**：`-t` 一次只能一个类型，多传会整体失败（输出 "Specify --help"）；类型全名要先 `-l c`/`-l e` 确认（如 `Party.MobileParty` 的中间命名空间易漏）。
