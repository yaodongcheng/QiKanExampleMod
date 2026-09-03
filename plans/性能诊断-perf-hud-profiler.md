# 帧率辅助显示 + 模块性能分析 + 通用 DLL 归属（性能诊断面板）

## Context（为什么做）

玩家反馈「有时候很卡」——单靠描述无法定位：卡在 LivingWorldNpcs 自己的逻辑？卡在原版/引擎？还是卡在别的 mod 的 DLL？决定做一个诊断工具：
1. 左上角实时 FPS / 帧时间（玩家卡的时候看数字）；
2. 精确测出**本 mod 每个子系统每帧耗多少毫秒**（A 层：手动插桩）；
3. 自动归因**其他 mod 的 DLL**（B 层：动态包裹它们每帧调用的方法，无需对方配合、不维护名单），玩家改好发日志回来即可确诊；
4. 卡的一帧自动写日志（TOP 模块 + DLL 归属），附带「mod 占比」指标区分「mod 卡」还是「引擎卡」。

**三个 MCM 开关（默认全关）**：① 帧率显示 ② 模块耗时分析 ③ 通用 DLL 分析。

## 机制总览（已全部实锤验证，无新增 #if）

| 层 | 机制 | 覆盖 | 精度 |
|---|---|---|---|
| A | 手动插桩 `PerfProfiler.Accum(slot, t0)`（15 根槽 + 5 明细槽） | 本 mod 每帧子系统 | 精确 ms/帧 |
| B | `harmony.Patch(MethodInfo)` 运行时动态包裹**所有 mod 标准每帧入口** | 第三方 mod 的 MissionBehavior tick / SubModule 帧钩子 | 精确 ms/帧 |
| C | 左上角 Gauntlet 面板 + 卡顿捕获落盘 | — | — |

**验证过的关键事实（三锚点一致）**：
- `Mission.MissionBehaviors` = `public List<MissionBehavior> { get; }`（1.2.12:1229 / 1.5.1:1346）——mission 场景枚举全部 mod 行为实例。
- **B 层 campaign 侧不能包 behaviors**：`CampaignBehaviorBase` 无 OnTick（反射实锤，基类只是 ICampaignBehavior）；每帧 campaign 逻辑走 `CampaignEvents.TickEvent` **委托分发，无法按 DLL 归因**——如实标注局限。可归因的 campaign 每帧入口 = `Module.GetInstance().SubModules` 的 `OnApplicationTick` override（每个 mod 一个钩子，反射验证 public virtual）+ `CampaignEntityComponent.OnTick`（引擎兜底，低覆盖）。
- 动态 patch 先例：`Debug/AgentDamageModelCultureNullFix.TryInstallPatches`（运行时反射枚举 + Harmony 2.x Patch，同款模式）。
- `Mission.OnTick` 方法确实存在（三锚点签名一致）但**不需要 patch**——mission 驱动由 MissionView 生命周期承接。

**已否决的方案**：20Hz StackTrace 栈采样——托管层抓不到「当帧任意执行点」（只能看钩子自己的调用链），且单次抓栈 ~300μs、20Hz 即 0.6%+ 常驻开销。动态包裹是**更精确、零性能风险**的替代（真耗时而非占比）。

## 新文件（8 cs + 1 XML + 2 本地化追加）

代码根：`ExampleModVS/ExampleMod/ExampleMod/`，新目录 `Diagnostics/`（命名空间 `LivingWorldNpcs`）。**🔴 csproj 是显式 `<Compile>` 清单（221 条，无 glob）——每个新文件必须登记，漏登记 = 编译不到。**

### 1. `Diagnostics/PerfProfiler.cs`（A 层核心）
- `enum PerfSlot`（15 根槽 + 5 明细槽，见插桩清单）+ 对齐的 `SlotNames[]`
- `Now()` → `Stopwatch.GetTimestamp()`；`Accum(slot, t0)`——**不配对**（提前 return 无副作用，单点调用安全）
- `OnFrameTick()`：帧心跳（挂 MySubModule.OnApplicationTick）——墙钟帧间隔 → 滚动 1s 窗口（读快照自动切窗）；`>250ms` 视为加载/挂起重置基线不计数；`>40ms` → `LogStutter()`（10s 限流）
- **🔴 帧心跳覆盖暂停与读档界面**：`OnApplicationTick` 是引擎应用层每帧回调，**Campaign 暂停（时间流速 0）与读档/加载界面期间照常每帧触发**（引擎主循环不停）——暂停时 UI 卡顿、织丰读档界面里点「读取」的同步重算卡顿，都能被测到并写卡顿行。落盘 = 卡顿行（面板在加载屏被原生层盖住，卡顿日志是主交付物）
- **scene 标记四态**：`Mission` / `Campaign` / `Campaign-Paused`（`Campaign.Current?.TimeSpeed <= 0f`）/ `UI-Save`（TopScreen 类型名含 SaveLoad/Loading）
- `TakeSnapshot()` → struct（FPS/均帧/max 帧/根槽合计 ms+次数/明细合计/TopN）
- **🔴 A 层插桩常开**（27 槽 × 2 次 GetTimestamp ≈ 2μs/帧，不可感知）——卡顿行永远有 mod 数据；MCM 只控「面板显示 + 是否包裹 + 卡顿行是否带 [Wrap] 段」
- **卡顿捕获常开**（不挂开关）：每帧 1 次 GetTimestamp（~30ns）；行文：
  `[Perf] Stutter 46.2ms scene=Mission | mod 12.3ms (27%) | AIC_BrainAll 7.1ms | [Wrap] SomeMod.dll::SomeMod.Init.OnMissionTick 9.0ms`

### 1b. UI 层汇总槽（暂停/读档场景的归因关键）
暂停时 Mission 无、TickEvent 停发——**唯一每帧在跑的托管链 = UI 层**（MapScreen/SaveLoad 屏/Screen 的 OnFrameTick + 各 mod SubModule.OnApplicationTick）。两个汇总槽量化「UI 层每帧花多少」：
- `UI_ScreenTick` 根槽：`Diagnostics/PerfUiTickPatches.cs` 独立补丁类挂 `ScreenBase.OnFrameTick`（MapScreen 等走基类的屏照跑；**暂停时也照跑**）—自己的 Prefix 记 t0 / Postfix Accum
- `UI_MissionUIFrame` 根槽：同文件挂 `MissionScreen.OnFrameTick`（mission UI 层，与 IM 的 ImMissionButtonRefreshPatch 同目标多 postfix 共存已验证安全）

### 2. `Diagnostics/PerfWrapper.cs`（B 层核心）
- `ConcurrentDictionary<MethodInfo, int>` 槽 id + `List<WrapSlotInfo>`（显示名 = `Assembly 短名 + DeclaringType.Name.MethodName`）
- 目标三类（**幂等，HashSet\<MethodInfo\> 去重**——Harmony 对同 MethodInfo 重复 Patch 会重复执行）：
  a) Mission：枚举 `MissionBehaviors`，`GetMethods(Instance|Public|NonPublic).Where(m => m.Name=="OnMissionTick" && !m.IsStatic)` + 同法 `OnPreDisplayMissionTick`（**防 AmbiguousMatchException：不用 GetMethod**，声明在该类型上的 override；没 override 的落到基类方法、天然合并成一个包裹点）
  b) 进程级 once：`Module.GetInstance().SubModules` → 各 SubModule override 的 `OnApplicationTick`（**跳过 DeclaringType==MBSubModuleBase 的空基方法**）
  c) Campaign 兜底：`CampaignEntityComponents` 的 `OnTick(float,float)`（覆盖量低，保留）
- **排除 `typeof(MySubModule).Assembly`**（与 A 层双计）
- prefix/postfix：注入 `MethodBase __originalMethod` 查 id（零反射）；`[ThreadStatic] Stack<(int,long)>` 配对（AI 并行线程不串，栈深 >64 清空自愈）；**全部 try/catch**（包裹绝不能把第三方行为搞崩）
- `Enabled` 门控（= MCM ③）——patch 常驻（~3μs/帧底噪），③ 只控计时/显示/卡顿行 [Wrap] 段，关→开即时生效
- 初始化一次性成本：patch 单个方法 0.1–0.5ms，30–60 个 ≈ 5–30ms，发生在首帧，打日志可接受

### 3. `Diagnostics/PerfHudVM.cs`
- `FpsLine`（"FPS 59 | 16.8ms (max 23.4) | Mission"）、`IsVisible`、`ProfilerRows`（MBBindingList，固定 6）、`WrapRows`（MBBindingList，固定 6）；`PerfRowVM.Text`
- **初值纪律**：FpsLine = "FPS --"（非空可解析；无 Color 绑定则无崩溃坑）；IsVisible 初值 false（防加载首帧闪）
- 1s 或 0.25s 分频刷新；**字符串相同不触发 OnPropertyChanged**（CompassVM 纪律）；文案每 1s 由 host 侧 `LWNTextHelper.ResolveText` 重解析（Compass 切语言教训同款防滞留）

### 4. `Diagnostics/PerfHudMissionView.cs`（Mission 宿主）
- MissionView；`OnMissionScreenInitialize` 建层 `V.NewLayer(30,"PerfHudLayer")` + `V.LoadMov(layer,"PerfHud",vm)` + AddLayer；`OnMissionTick`：全关 → 摘层；开 → 懒调 `PerfWrapper` mission 段 + 1s 节流 VM 刷新；`OnMissionScreenFinalize` 摘层
- **挂载在 MySubModule.OnMissionBehaviorInitialize 双闸门之前**（`Campaign.Current == null` return 之前）——自定义战斗/战场也显示 FPS（不依赖 campaign API，纯显示安全）

### 5. `Diagnostics/PerfHudManager.cs`（Campaign 宿主）
- `OnScreenFrameTick(dt)`：**`Mission.Current != null → return`**（与 Mission 视图互斥）；层失效检测照抄 `ImChatOpenButtonManager` 判废范式（owner 屏 ≠ TopScreen 或 `V.LayerFinalized` → Close + 下帧重挂）；Loading 屏不挂；层序 204（>MapBar 202，<4400）；懒调 `PerfWrapper` campaign 段
- `Mount()/Close()` 用 **ImChatOpenButtonManager 鲁棒版**（LayerFinalized + ReleaseMovie + 从 `_layerOwnerScreen` 摘 + HasLayer + try/catch），**不用 CompassHud 简版**（无守卫 = 1.2.12 二次 Finalize 崩溃史）
- 无 InputRestrictions（纯显示层零输入）

### 6. `Diagnostics/PerfScreenFrameTickPatch.cs`
- `[HarmonyPatch(typeof(ScreenBase), "OnFrameTick")]` postfix → `PerfHudManager.OnScreenFrameTick(dt)`——**独立补丁类**（仓库惯例按职责拆类；与 ImScreenFrameTickPatch 同目标多 postfix 共存已验证安全）；Mission 内 MissionScreen override 不走基类、天然隔离

### 6b. `Diagnostics/PerfUiTickPatches.cs`（暂停/读档归因的 UI 总量）
- `[HarmonyPatch(typeof(ScreenBase), "OnFrameTick")]` Prefix+Postfix 自己配对，Accum `UI_ScreenTick`（MapScreen 走基类、暂停照跑 → 暂停时 UI 消耗的量化指标；与 PerfScreenFrameTickPatch 同目标多 postfix 共存）
- `[HarmonyPatch(typeof(MissionScreen), "OnFrameTick")]` 同款 → Accum `UI_MissionUIFrame`（Mission UI 层，含 ESC 暂停期间，与 ImMissionButtonRefreshPatch 同目标共存）

### 7. `Diagnostics/PerfCommands.cs`
- `custom.perf_status`（快照打印到 DebugLogger）、`custom.perf_threshold <ms>`（卡顿阈值，默认 40）——签名铁律 `Func<List<string>,string>`

### 8. `GUI/Prefabs/PerfHud.xml`（**模块根** GUI\Prefabs\，不是源码目录）
- Window Id="LWN_PerfHud" → 根 Widget StretchToParent DoNotAcceptEvents → 左上容器 Fixed ~300 宽、8px 边距、`BlankWhiteSquare_9` Color="#00000088"、IsVisible="@IsVisible"
- FPS 行 TextWidget（MyBrush_16_Left FontSize 14）+ 两个 ListPanel（`Id="LWN_PerfHud_Profiler"`/`Id="LWN_PerfHud_Wrap"`）+ ItemTemplate（TextWidget Text="@Text"）；垂直排布 VerticalTopToBottom（LWN 前缀由 StackLayoutVerticalSwapPatch 双版本自动校正，实机目验）
- 全链 DoNotAcceptEvents；FPS 色 ≥50 白 / 30–50 黄 / <30 红（9 字符 `#RRGGBBAA`）

### 9. 本地化（追加两文件末尾，不动现有条目）
- `ModuleData/Languages/std_LivingWorldNpcs_strings.xml`（英）+ `ModuleData/Languages/CNs/std_LivingWorldNpcs_strings.xml`（中）
- 键：`LWN_mcm_perf_fps/hint`、`LWN_mcm_perf_profiler/hint`、`LWN_mcm_perf_sampler/hint`、`LWN_perf_scene_mission/campaign/ui`（场景标识）。模块名/方法名/DLL 名英文原样（铁律 13 豁免）；禁 emoji/码点>U+FFFF（铁律 14）

## 修改文件（15 cs + 1 csproj + 2 本地化）

### 插桩点（17 根槽 + 5 明细槽；模式：`long t0 = PerfProfiler.Now(); ...原逻辑...; PerfProfiler.Accum(PerfSlot.X, t0);`）

| # | 文件 | 位置 | 槽 | 类型 |
|---|---|---|---|---|
| 1 | `AI/AgentAIController.cs` | OnMissionTick brains 循环 | AIC_BrainAll | 根 |
| 2 | 同上 | PlanExecutor.TickAll 调用处 | AIC_PlanExecutor | 根 |
| 3 | 同上 | ReactiveAgent.TickAll 调用处 | AIC_ReactiveAgent | 根 |
| 4 | 同上 | DialogueComponent.TickContinuations | AIC_DialogueContinuations | 根 |
| 5 | 同上 | SpeechChannel.TickAll | AIC_SpeechChannel | 根 |
| 6 | `Interaction/InteractionMissionView.cs` | ModInput.Tick | IMV_ModInput | 根 |
| 7 | 同上 | ImChatOpenButtonManager.Tick | IMV_ImChatOpenButton | 根 |
| 8 | 同上 | PlanReplan.Tick | IMV_PlanReplan | 根 |
| 9 | 同上 | CompassHud.OnTick | IMV_Compass | 根 |
| 10 | 同上 | gate 后交互块 + 射线检测 | IMV_Interact / IMV_Raycast | 根 |
| 11 | `AgentHUD/AgentHudMissionView.cs` | OnMissionTick | HV_AgentHud | 根 |
| 12 | `AI/NpcSightSystem.cs` | OnMissionTick | NV_NpcSight | 根 |
| 13 | `Combat/AttackTriggerMissionLogic.cs` | OnMissionTick | CV_CombatLogic | 根 |
| 14 | `Camera/SpringArmCameraView.cs` | OnMissionTick | CAM_SpringArm | 根 |
| 15 | `ImChat/ImChatView.cs` | Tick 内：ModInput.TickInputSource / WorldBackgroundBehavior.OnFrameTick / ImChatManager.Tick | CT_InputSource / CT_WorldBackground / CT_ImChatManager | 根 |
| 16 | `ImChat/ImChatManager.cs` | Tick 内 5 段 | ImMgr_Reply / ImMgr_CommandFlow / ImMgr_EventBroadcaster / ImMgr_AutonomyProposal / ImMgr_DelayedMsgs | **明细** |
| 17 | `Story/StoryEngine.cs` | OnTick | ST_StoryEngine | 根 |
| 18 | `Core/MyBehavior.cs` | TickEvent OnTick | CP_MyBehavior | 根 |
| 19 | `WorldEvent/PlayerDetentionBehavior.cs` | TickEvent OnTick | CP_Detention | 根 |
| 20 | `WorldEvent/WorldEventSimulator.cs` | OnCampaignTick | CP_WorldEventSim | 根 |
| 21 | `Diagnostics/PerfUiTickPatches.cs` | ScreenBase.OnFrameTick（UI 链，暂停/读档照跑） | UI_ScreenTick | 根 |
| 22 | `Diagnostics/PerfUiTickPatches.cs` | MissionScreen.OnFrameTick（Mission UI 链） | UI_MissionUIFrame | 根 |

**🔴 嵌套双计纪律（度量正确性关键）**：`mod 占比`只加**根槽**（17 个最外层）之和；ImMgr_* 5 个**明细槽**只进 TOP 列表不进 modShare（否则 ImChatManager 与内部段重复计数）。明细行打标记（如 `[细]`）防误读。

**不插桩**（如实）：LLM 网络调用（后台线程非 tick）；每个 agent 内循环（BrainAll 已覆盖）；GUI 布局引擎；一次性 lazy 初始化。

### 其他修改
- `Core/MySubModule.cs`：① 双闸门**之前**挂 `mission.AddMissionBehavior(new PerfHudMissionView());`（第 100–106 行之间）；② `OnApplicationTick` 加 `PerfProfiler.OnFrameTick();`（帧心跳唯一来源）
- `Core/MCMSettings.cs`：ShowCompass（Order=-4）之后追加三个 `[SettingPropertyBool]`（Order=-5/-6/-7，Order 越小越靠列表底部），facade 透传
- `Core/Settings.cs`：`[Newtonsoft.Json.JsonIgnore] public bool ShowPerfHud/ShowPerfProfiler/ShowPerfSampler { get; set; } = false;`
- `ExampleMod.csproj`：显式 `<Compile Include>` 登记 8 个新 .cs（PerfProfiler/PerfWrapper/PerfHudVM/PerfHudMissionView/PerfHudManager/PerfScreenFrameTickPatch/PerfUiTickPatches/PerfCommands）
- `Debug/MyCommands.cs`（或 PerfCommands 单独文件）

## 实施顺序

1. **地基**：Settings + MCMSettings 三开关 + 本地化键 → 编译 → 进 MCM 看三个开关在最底部
2. **A 层核心**：PerfProfiler + OnApplicationTick 挂 OnFrameTick + 试点槽（AIC_BrainAll）→ `custom.perf_status` 验证墙钟/卡顿日志（切窗口造 40ms+ 帧、暂停时拖动大地图造 40ms+）
3. **A 层全量 + Mission 显示**：全部插桩、UI 两汇总槽（PerfUiTickPatches）、PerfHudVM、PerfHudMissionView、PerfHud.xml、csproj → 战场实机 TOP6 + modShare
4. **B 层**：PerfWrapper + PerfHudManager + PerfScreenFrameTickPatch + csproj → 大地图开③看「其他 DLL」行
5. **打磨**：卡顿行限流、摘层兜底（ESC 开/关、快速进出 mission）、双语言目验、1.2.12 验证机编译

## 验证

- **编译**：`dotnet build` 零错误（开发机 v1.5.x；1.2.12 机器再编一次）；无新增 #if（全部 API 三锚点已验证）；csproj 7 条 Compile 齐全；GUI XML 在**模块根**；双语 XML 键数相等
- **实机**：
  1. 默认全关 → 画面无新 UI，卡顿日志仍写；开① → 战场/custom 战斗左上 FPS 行（自定义战斗无 campaign 也显示 = 闸门前挂载验收）
  2. 开② → TOP6 与 modShare；帧 25ms+ 但 mod 合计 <5ms → 归属引擎（指标语义验收）
  3. 开③ → [Wrap] 行出现（至少见 SandBox.View 地图组件 + 其他 mod SubModule 的 OnApplicationTick）；**LivingWorldNpcs 自身不得出现**在 [Wrap] 行
  4. ESC 打开 → 面板被盖（Mission 30<50 / Campaign 204<4400）；进出 mission 往返 3 次无层泄漏/死层/crash（鲁棒摘层验收）
  5. 卡顿：`custom.perf_threshold 20` + 切窗口 → RuntimeLog 出现 `[Perf] Stutter` 行（10s 限流不重复）
  6. 全关 → 面板 1s 消失；再开 → 1s 恢复
  7. **暂停专项**：大地图时间流速设 0，拖动地图/开菜单制造 40ms+ 帧 → 卡顿行 scene 应为 `Campaign-Paused`，且 UI_ScreenTick 槽有数值（暂停时 UI 层消耗量化）
  8. **读档专项**：织丰环境打开存档界面，点「读取」制造卡顿 → 卡顿行 scene 应为 `UI-Save`（帧心跳在引擎主循环转场期间照常触发）；面板被原生屏盖住属预期（卡顿日志是主交付物）
  9. 开③后 SubModule 字段核对：本机 mod 的 `OnApplicationTick` override 均出现为包裹点
- **铁律自查**：无 CSV/生成物改动（22/24 不涉及）；git 只读（23）；玩家可见文案全走 LWN 本地化（13）；无 emoji（14）

## 局限（如实交代）

1. **原生 C++ 引擎层不可归因**（托管帧才可见）——「mod 占比」兜底：帧大但 mod 合计小 = 引擎/渲染问题，玩家至少知道排查方向
2. **CampaignEvents.TickEvent 委托链无法按 DLL 归因**（引擎事件广播物理层面不暴露归属）——本 mod 自己的 campaign 每帧已插桩；第三方采用 TickEvent 的每帧逻辑归入「引擎层」；采用 SubModule.OnApplicationTick 与 MissionBehavior 的 mod 覆盖面最广
3. 背包/队伍等全屏 UI 打开时面板可能被原生层盖住（重点诊断场景 = Mission/Campaign 主场景）；**读档/加载界面同理**——此时主交付物是卡顿日志行（帧心跳仍测）
4. **保险语**：暂停/读档期间的卡顿**测得到事件**（墙钟帧心跳 + scene 标记），但归因粒度取决于卡顿发生在哪个被包裹/插桩的方法内；卡在「引擎原生 UI 布局/渲染」或「mod 非 tick 回调」时只归「引擎/UI 层」——托管层物理边界，如实记录而非假装定位
5. `CampaignEntityComponent.OnTick` 是引擎 campaign 每帧兜底（SandBox.View 地图组件已包含）
