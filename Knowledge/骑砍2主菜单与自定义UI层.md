# 骑砍2 主菜单与自定义 UI 层（反编译实证）

> **来源**：2026-09-10「时代剧本切换」工程实机排雷（选剧本界面）。
> **适用**：任何要在主菜单加东西（按钮/入口/子界面）的 mod。
> **证据等级**：全部反编译+实机验证，行号/类型名可复核。

---

## 一、主菜单的构成（改它之前必须先知道）

### 1.1 数据源 = `Module.CurrentModule` 的选项列表

| 事实 | 证据 |
|---|---|
| 主菜单每一项 = 一个 `InitialStateOption(id, name, orderIndex, action, isDisabledAndReason, enabledHint)` | `TaleWorlds.MountAndBlade.InitialStateOption` |
| 列表由各模块在启动时 `AddInitialStateOption` 填充；**取用时按 `OrderIndex` 排序** | `Module.GetInitialStateOptions()` = `_initialStateOptions.OrderBy(s => s.OrderIndex)` |
| 界面 = `GauntletInitialScreen`（**Native 模块** `Modules/Native/bin/Win64_Shipping_Client/TaleWorlds.MountAndBlade.GauntletUI.dll`），它 `LoadMovie("InitialScreen", vm)`，vm 是 `InitialMenuVM`（在 `TaleWorlds.MountAndBlade.ViewModelCollection.dll`） | 反编译实证 |
| `InitialMenuVM.RefreshMenuOptions()` 里就是 `foreach (Module.CurrentModule.GetInitialStateOptions())` 重建按钮 | 同上 |

**关键推论**：改主菜单按钮 = 改 `Module.CurrentModule` 那个列表。

### 1.2 刷新链（官方通道，不用自己找 ViewModel）

```
改 Module 的选项列表
  → InitialState.RefreshContentState()
  → 触发 InitialState.OnGameContentUpdated
  → GauntletInitialScreen.OnGameContentUpdated()
  → _dataSource.RefreshMenuOptions()
  → UI 立即重建按钮列表
```

证据（`GauntletInitialScreen` 实读）：
```csharp
// OnInitialize：
((MBInitialScreenBase)this)._state.OnGameContentUpdated += new OnGameContentUpdatedDelegate(OnGameContentUpdated);
// 处理器：
private void OnGameContentUpdated() { InitialMenuVM dataSource = _dataSource; if (dataSource != null) dataSource.RefreshMenuOptions(); }
```

`InitialState` 从 `GameStateManager.Current.ActiveState` 拿（主菜单时它就是当前 GameState）。

---

## 二、🔴 坑：主菜单列表被 MCM 用 Harmony 盯着——**不能改短**

**症状**（实机 2026-09-10）：
```
System.ArgumentOutOfRangeException: Index must be within the bounds of the List.
  at System.Collections.ObjectModel.Collection`1.Insert(Int32 index, T item)
  at MCM.UI.Functionality.DefaultGameMenuScreenHandler.RefreshMenuOptionsPostfix(InitialMenuVM __instance, MBBindingList`1& ____menuOptions)
```

**根因**：MCM（Mod Configuration Menu）Harmony postfix 在 `InitialMenuVM.RefreshMenuOptions` 上，
**按固定下标往列表里插自己的「Mod 选项」按钮**。我们把自己的菜单换成「返回 + 两个剧本」这种**更短的列表**后，
它算出的下标越界 → 直接崩。

**结论**：
- ❌ **不要用"替换主菜单选项列表"来做子菜单/多级菜单**——列表长度或顺序一变就可能踩 MCM（以及未来任何同类 mod）。
- ✅ 那条路只在**不改动列表长度/顺序**的场景可用（比如只改某个按钮的文案/禁用态）。
- ✅ 要做「点进去看更多」的界面，**自建 Screen**（见下节）——完全不碰那个列表。

---

## 二·补、🔴 深坑：**自建 ScreenBase + PushScreen 会在某些时点画不出来**

**症状**（2026-09-10 实机，选人界面）：屏明明在栈顶，状态全对，**但屏幕上一个像素都没有**——
诊断实测：`ScreenManager.TopScreen == 本屏`、`IsActive=True`、层 `IsActive=True`、焦点也拿到了，全绿。

**根因**：引擎的屏栈在**换代空档期**（世界刚建好 / GameState 正在切换）时，新推入的屏不进渲染。
本仓库**所有能正常显示的界面**（AgentHud / IM 面板 / 相机调试 / 切磋条）
**无一例外**都是同一个写法——**挂到引擎现成的屏上，不自己造屏**：

```csharp
GauntletLayer layer = V.NewLayer(order, "MyLayer");
V.LoadMov(layer, "MyPrefab", vm);
layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
ScreenManager.TopScreen.AddLayer(layer);      // ← 关键：宿主用引擎现成的屏
// 🔴 不要设 IsFocusLayer / 不要 TrySetFocus——能工作的范本都不抢焦点，抢了会和引擎的焦点管理打架
```

**推论**：需要"全屏覆盖式界面"时，**优先找当前 TopScreen 挂层**，而不是 `PushScreen` 自建屏。
（自建屏在 MissionScreen / MapScreen 之外自成一套时仍可用，但**不要在 GameState 切换期用**。）

### 🔴 挂层时机：必须"待办 + 重试"，不能一次性挂

`OnLoadFinished` 那一刻可能**没有屏可挂**（`ScreenManager.TopScreen == null`，屏栈正在换代）。
一次性挂 = **静默挂空**（界面不出现、日志一片空白，排查极难——实机踩过）。

**做法**：请求 + 每帧重试（驱动点用应用层 `OnApplicationTick` 单点）：

```csharp
public static void Request() { _pending = true; }                 // 只登记
public static void Tick() {                                       // 每帧试
    if (!_pending || _shown) return;
    if (++_retryTicks > 600 && _retryTicks % 30 != 0) return;      // 前 10 秒每帧，之后低频
    ScreenBase host = ScreenManager.TopScreen;
    if (host == null) return;                                      // 空档 → 下帧再试
    /* 挂层…… */
}
```

---

## 二·补2、🔴 `OnLoadFinished` 会被**反复调用**（引擎没守卫）

```csharp
// TaleWorlds.MountAndBlade.GameLoadingState.OnTick（反编译实锤）
protected override void OnTick(float dt) {
    base.OnTick(dt);
    if (!_loadingFinished) { _loadingFinished = _gameLoader.DoLoadingForGameManager(); return; }
    GameStateManager.Current = Game.Current.GameStateManager;
    _gameLoader.OnLoadFinished();        // ← 无条件，每帧都调
}
```

**只对 `DoLoadingForGameManager` 有"完成"守卫，对 `OnLoadFinished` 没有。**
原版没暴露问题，是因为建号状态 / 地图状态随即接管、`GameLoadingState` 随即退出。

⇒ **在 `OnLoadFinished` 里做任何"一次性"的事（弹界面 / 建对象 / 注册）都必须自己加幂等守卫**，
否则 = 每帧重建 → 玩家的点击永远被下一次重建吞掉（症状：看着像卡死在 loading）。

## 三、两种界面形态：什么时候挂层、什么时候自建屏

> ⚠️ **先读二·补**：自建 `ScreenBase` 在 GameState 切换期（世界刚建好 / 加载中）**画不出来**。
> 所以选形态之前先问一句：**这个界面出现的时点，引擎的屏栈稳不稳？**

| 形态 | 什么时候用 | 范本 |
|---|---|---|
| **挂层到现成屏**（`ScreenManager.TopScreen.AddLayer`） | **首选**。界面出现在游戏进行中（地图屏 / MissionScreen 已在），或时点可能撞上屏栈换代 | `AgentHudMissionView` / IM 面板 / `HeroSelectOverlay` |
| **自建 `ScreenBase` + `PushScreen`** | 界面**自己就是一个独立场景**（如主菜单里点开的全屏界面），且时点**不在** GameState 切换期 | `ScenarioSelectScreen`（主菜单 → 选剧本，已验证可用） |

**判别口诀**：时点在**主菜单**（屏栈静止）→ 自建屏没问题；时点在**进图/加载/状态切换**中 → 一律挂层。

### 3.1 自建 Screen 的骨架（仅在上表第二种场景用）

```csharp
public class MyScreen : ScreenBase
{
    private GauntletLayer _layer;
    private MyVM _vm;

    public static void Open()                       // 主菜单入口调它
    {
        if (ScreenManager.TopScreen is MyScreen) return;   // 防重复开
        ScreenManager.PushScreen(new MyScreen());          // 主菜单屏留在栈下，返回即还原
    }

    protected override void OnInitialize()
    {
        base.OnInitialize();
        _vm = new MyVM(Close, /* 业务回调 */);
        _layer = V.NewLayer(200, "LWN_MyLayer");           // 🔴 层构造签名有版本差异，用仓库的 V.NewLayer
        V.LoadMov(_layer, "MyPrefab", _vm);                // prefab 名 = GUI/Prefabs/<名>.xml
        _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);  // 屏蔽下层输入
        AddLayer(_layer);
        _layer.IsFocusLayer = true;
        ScreenManager.TrySetFocus(_layer);
    }

    protected override void OnFinalize()
    {
        if (_layer != null) { RemoveLayer(_layer); _layer = null; }   // 摘层守卫：RemoveLayer 会连带 Finalize，禁止二次
        _vm = null;
        base.OnFinalize();
    }

    private void Close() => ScreenManager.PopScreen();
}
```

**ScreenManager 公开 API**（`TaleWorlds.ScreenSystem.ScreenManager`）：
`PushScreen` / `PopScreen` / `CleanAndPushScreen` / `CleanScreens` / `ReplaceTopScreen` / `SetAndActivateRootScreen` / `TopScreen` / `FocusedLayer`。

### 3.2 三件套职责

| 文件 | 职责 |
|---|---|
| `GUI/Prefabs/<名>.xml` | 布局（**框架自带 prefab 加载，无需注册**；名字与 `LoadMovie` 第一参数一致） |
| `<名>VM : ViewModel` | 数据 + 命令（`Command.Click="ExecuteXxx"` / `Command.HoverBegin` 绑定到同名公开方法） |
| `<名>Screen : ScreenBase` | 挂层 / 摘层 / 开关 |

### 3.3 写法要点（都是踩过的）

- **列表绑定**：`<ListPanel DataSource="{Items}">` + `<ItemTemplate>`；项内 `@属性名` 绑定。
  🔴 VM 里的列表属性**必须是同一个实例**（`public MBBindingList<T> Items { get; }` 在构造里 new 一次）——
  每次 get 新建列表会割断绑定。
- **文本颜色**：`Brush.FontColor="@TextColor"`（仓库有先例）。
- **`IsVisible` 取反**（`!@Flag`）**仓库无先例、有风险**——改用「底图常驻 + 上层 `IsVisible="@Flag"`」的叠法。
- **按钮列顺序**：`StackLayout.LayoutMethod` 可选 `VerticalTopToBottom` / `VerticalBottomToTop`。
- **选中态**：在 item VM 上放 `IsSelected` 布尔 + `OnPropertyChangedWithValue`，prefab 里绑 `IsVisible` 做描边。
- 🔴 **`TextObject` 的 fallback 必须写"要显示的文字"**：
  `new TextObject("{=LWN_key}Scenarios")` ✅ / `new TextObject("{=LWN_key}LWN_key")` ❌
  （后者在本地化查不到时**把键名印在界面上**——实机见过）。

### 3.4 开战役的顺序（内容包场景）

```csharp
private void OnPicked(Era era)
{
    ScreenManager.PopScreen();          // ① 先关界面
    MBGameManager.StartNewGame(new XxxGameManager(era.Id));   // ② 再开战役
}
```
🔴 顺序要紧：`StartNewGame` 会推入 `GameLoadingState` 并替换主菜单屏；
界面若不先退出，会挂在一个正在销毁的屏上。

---

## 四·前、🔴🔴 跳过引擎标准流程时，**必须手动镜像它的收尾**（2026-09-10 实机卡死）

**场景**：我们不走官方建号（直接"选一位现成领主"开局），于是跳过了
`CharacterCreationState.FinalizeCharacterCreation()`。**那一整套收尾不能省**——省一步就是静默卡死。

### 引擎原版收尾（反编译实锤，**逐步照做，一步不落**）

```csharp
public void FinalizeCharacterCreation() {
    CharacterCreation.ApplyFinalEffects();                                        // ① 应用建号结果
    Game.Current.GameStateManager.UnregisterActiveStateDisableRequest(this);      // ② 🔴 最易漏
    Game.Current.GameStateManager.CleanAndPushState(CreateState<MapState>());      // ③ 推入大地图
    PartyBase.MainParty.SetVisualAsDirty();                                       // ④ 视觉刷新
    _handler?.OnCharacterCreationFinalized();
    CurrentCharacterCreationContent.OnCharacterCreationFinalized();               // ⑤ 通知建号内容
    CampaignEventDispatcher.Instance.OnCharacterCreationIsOver();                 // ⑥ 广播事件
}
```

### 🔴 第 ② 步为什么是"卡死"的元凶

`GameStateManager.ActiveStateDisabledByUser => _activeStateDisableRequests.Count > 0`。
建号状态在 `OnInitialize` 里 `RegisterActiveStateDisableRequest(this)`——
**不撤销就永远为真 → 地图状态永不激活 → 画面在、但无法操作**（日志表现为一直 `scene=CampaignPaused`，不崩）。

### 哪一步可以省：只有 ①

`ApplyFinalEffects()` 的第一句是 `Clan.PlayerClan.Renown = 0f;`（**把玩家家族声望清零**）——
对"捏脸开局"是对的，对"魂穿一位现成领主"是**错的**（会把所选领主家族声望清零）。
所以：**① 按语义决定省不省，②~⑥ 一步不能省。**

### 另一条：宿主屏要自己造

若界面要挂在建号屏上（覆盖官方建号流程），**先把建号状态推起来**（`PushCharacterCreation`），
它既提供可挂的屏、又把玩家看到的第一层遮住；它的禁用请求由上面第 ② 步撤销。

---

## 四、顺带记录：引擎的两处脆弱链（排雷时撞到）

### 4.1 `Kingdom.RulingClan` / `Leader` 的赋值时机

- `Kingdom.Leader => _rulingClan?.Leader`；
- `RulingClan` 全引擎只有一条自动赋值路径——**读 Kingdom 节点时**：
  ```csharp
  RulingClan = (objectManager.ReadObjectReferenceFromXml("owner", typeof(Hero), node) as Hero)?.Clan;
  ```
  （另一处是废位时的 `ChangeRulingClanAction`）
- 而 `Kingdom.OnNewGameCreated` **无条件**写 `InitialHomeLand = Leader.HomeSettlement` —— **没有判空**。
- ⇒ 段加载顺序（Kingdoms→Factions→Heroes）下 `RulingClan` 可能为 null → 新战役直接崩。
- 处置：LWN 的 `CampaignMode/KingdomOnNewGameCreatedGuardPatch.cs` 在 `Leader` 为 null 时跳过（不做猜测性兜底，
  交给家宅置位段）。

### 4.2 `<Hero>` 只在有同名 `<NPCCharacter>` 模板时才生效（雷 54）

- 缺模板 → 该条**被引擎静默吞掉**（语法合法、交叉引用也解析得了）；
- 后果：家族没有可用领主 → `HeroSpawnCampaignBehavior.GetBestAvailableCommander` NRE → 新战役崩（堆栈全指引擎）；
- 离线防线：`Scripts/check_hero_templates.py`（详见必备清单雷 54）。

---

## 五、Reusability 小结（下次要动主菜单时照着走）

1. **只是想加按钮** → `Module.CurrentModule.AddInitialStateOption(...)`（本仓库 `CampaignModeActivator` 已在做）。
2. **想加"点进去看更多"** → 按本文档第三节判别形态：**主菜单里点开** = 自建 Screen；**游戏进行中/加载期** = 挂层到 TopScreen。**两者都别动主菜单列表长度**。
3. **需要程序化刷新主菜单**（如禁用一个已存在的按钮）→ `InitialState.RefreshContentState()`；但注意 MCM 会跟着跑它的 postfix。
4. **绝对不要**为了做子菜单而"清空列表再塞短的"——MCM 会崩。
