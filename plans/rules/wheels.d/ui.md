# ui — 轮子速查分卷（wheels.md 索引导航）
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


---

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

// ③ UpdateLogic 中设值（ShowIntentDebug 现已被 MCM 开关 Settings.ShowNpcIntent 门控，默认开）
//    两个过滤缺一不可：①空闲意图（NpcIntentType.None）不算内容——所有 NPC 默认意图就是 None，
//    不过滤会导致满屏"空闲"+名字；②文本非空（intent != null 不够，ToString 可能返回空串）
NpcIntentDebugText = intent != null && intent.Type != NpcIntentType.None ? intent.ToString() : "";
ShowIntentDebug = Settings.Instance.ShowNpcIntent
    && !Settings.Instance.IsInteractionDisabled()
    && !string.IsNullOrWhiteSpace(NpcIntentDebugText);
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
6. 🔴 **绑定值初始值必须可解析（实机踩过崩溃）**：绑定建立时引擎会读取属性初始值推给 Widget——绑 `Color` 的 string 属性若初始为 `null`/空串，`Color.ConvertStringToColor` 直接 `ArgumentOutOfRangeException` 崩溃（InteractionVM.SegColor 踩过：Short 项进度框不可见但绑定仍存在）。**凡是绑 Color 的 string 字段必须在声明处初始化合法颜色串**，绑数值的 float/int 默认 0 合法无需处理。
7. 🔴 **引擎颜色只支持 `#RRGGBBAA`（8 位 hex）**（实机踩过崩溃）：写 6 位 hex（`#RRGGBB`，7 字符）会在 Alpha 解析时 `Substring(7, 2)` 越界崩溃（InteractionVM.SegColor 踩过：`#33CCFF`/`#FFE97F` 皆崩，补齐 `FF` 为 `#33CCFFFF`/`#FFE97FFF` 即好）。**项目惯例全部颜色必须 9 字符**（`#FFFFFF33`、`#8B0000FF`…）；新增颜色写完后用正则 `#[0-9A-Fa-f]{6,8}` 扫一遍长度。
8. 🔴 **颜色顺序 = RRGGBBAA（alpha 在最后两位），纯黑必须写 `#000000FF`**（实机踩过不显示）：按 HTML 习惯把 alpha 写前头的 `#FF000000`，在引擎里解析为 **R=255,G=0,B=0,A=00 = 全透明红 → 永远不可见**（InteractArea 蓄力进度黑条踩过：绑定版与 XML 写死版均无影，同层白边 `#FFFFFFFF` 因两序同义渲染正常，误导排查方向）。`#FFFFFFFF`/`#FFE97FFF`/`#B5F0E8FF` 等 alpha 天然在尾的色不受影响。**写颜色按"RR GG BB AA"四段核对**：想表达不透明 X 色 → `#XXYYZZFF`。


---

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


---

## 五大元素与显隐规则

| 元素 | VM 属性 | 显隐条件 | 持续时间 | FOV |
|------|---------|----------|----------|:---:|
| **名字** | `ShowName` + `AgentName` | ShowSpeech \|\| ShowHealth \|\| ShowDamage \|\| ShowAlert \|\| ShowIntentDebug（任意元素真的显示；MCM 血条开关关闭时 ShowHealth/ShowDamage 恒 false、意图开关关闭时 ShowIntentDebug 恒 false） | 跟随触发元素 | ✅ |
| **说话** | `ShowSpeech` + `SpeechText` | `Speak(text)` 调用 | `4s + text.Length * 0.1s` | ✅ |
| **血条** | `ShowHealth` + `CurrentHealthWidth` | 战斗中/血量<95%/戒备（`CurrentWatchState` Alarmed，敌意驱动，不看持械）；**MCM 开关 `Settings.ShowAgentHealthBar`（默认开）关闭 → 血条与伤害数字一并隐藏** | 持续（条件消失隐藏） | ✅ |
| **伤害** | `ShowDamage` + `DamageText` | 受伤害瞬间；随 MCM 血条开关关闭而隐藏 | 2s | ✅ |
| **警戒** | `ShowAlert` + `AlertFillHeight/EyeBgColor/EyeFillColor` | 警戒值 > 0，**且非战斗状态**（战场 `IsInteractionDisabled()` / 个体 `brain.IsInCombat` 时强置 0） | 持续（归零隐藏） | ❌ **豁免** |

**警戒 FOV 豁免**：警戒眼睛不受 FOV 角度限制——NPC 在玩家身后盯你，更该知道。屏幕外时 clamp 到边缘做方向指示。名字只在 FOV 内显示，玩家转身面对 NPC 后名字浮现。

**名字总领规则**：`ShowName = ShowSpeech || ShowHealth || ShowDamage || ShowAlert || ShowIntentDebug`（UpdateLogic 只在 FOV 内执行，故 FOV 外名字不计算、眼睛独立显示）。**MCM 血条开关（`Settings.ShowAgentHealthBar`）关闭时 `ShowHealth`/`ShowDamage` 被强制置 false；意图开关（`Settings.ShowNpcIntent`）关闭时 `ShowIntentDebug` 恒 false**——名字只在说话/警戒/意图真的显示时浮现，不会出现光秃秃的头顶名字。⚠️ **意图项必须三处联动**（否则意图单独显示时 HUD 整个不出现）：①名字总领规则 ②`UpdateFrame` 最终可见性兜底检查（`!ShowSpeech && !ShowDamage && !_showHealth && !ShowAlert && !ShowIntentDebug`）③MissionView FOV 外分支 `hud.ShowIntentDebug = false` 防残留。

**警戒抑制**：战场（Mission Mode 在 `DisabledInteractionMissionModes`）与个体战斗中（`brain.IsInCombat` = `IsCurrentOrPending<FightEnemyAction>()`）警戒眼强置 0 不显示——战斗中血条已表达敌意，警戒指示无意义。两者覆盖场景不同：战场=Mission 级（整场抑制），个体战斗=Agent 级（城镇斗殴等可互动场景里打起来的 NPC 只留血条，战斗结束警戒眼恢复）。


---

## 性能：距离分级

| 距离 | 范围 | 更新频率 | 做什么 |
|------|------|----------|--------|
| **近** | ≤ 15m | 每 10 帧 | 完整：血条 + 警戒值 + 说话 + 坐标 |
| **中** | 15m ~ 50m | 每 30 帧 | 仅警戒值 + 坐标 |
| **远** | > 50m | 不处理 | 不创建 HUD / 隐藏 |

**延迟创建**：不是一开始就给所有 Agent 创建 HUD，而是按需创建（有警戒值/说话/战斗 → 创建）。


---

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

**🔴 意图行合并 + 警戒眼双色（2026-08-13 用户裁定）**：
- **单行语义**：`NpcIntentDebugText` 一个文本变量——计划执行中（`PlanExecutor.GetExecutorFor(TargetAgent)` 的 CurrentSummary 非空）→ `LWN_hud_plan_executing`「执行计划中：{STEP}」；否则意图文本。一行一开关 `ShowNpcIntent`（旧橙色 `PlanSummaryText` 行已删，`ShowPlanSummary` 全仓库归零）。
- **警戒眼双色**：`hud.AlertTargetIsPlayer`（普通属性**不加 [DataSourceProperty]**，参照 AlertValue 模式）——true=暖色（黄/红，针对玩家）/ false=冷青蓝（围观别人犯法）。颜色全 9 字符 `#RRGGBBAA`。**注入顺序纪律：先 `AlertTargetIsPlayer` 后 `AlertValue`**（setter 内部触发 UpdateAlertVisuals 读色系，反了晚一帧变错颜色）。
- 名字总领规则三处联动：`ShowName` 含 `ShowIntentDebug`；FOV 外兜底隐藏 `ShowIntentDebug`；UpdateFrame 最终可见性检查含 `ShowIntentDebug`。


---

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


---

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


---

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


---

## GauntletLayer 层序选择 — 原生地图/菜单层序表（反编译实测）

**问题**：`V.NewLayer(order)` 的 order 选多少？选低了被原生 UI 盖住（IM 曾用 20，被定居点菜单覆盖），选高了压住系统菜单（ESC 菜单 4400）体验更差。

**方案**：反编译 `SandBox.GauntletUI.dll`（v1.4.7 实测）拿到原生层序表，自定义 UI 直接查表选值：

| 原生层（层名） | 层序 | 说明 |
|------|------|------|
| 地图名标 MapNameplateLayer | 90 | 大地图标识 |
| MapMenuView / MapNotification | 100 | 地图菜单主层/通知 |
| MapBattleSimulation | 101 | 坐镇模拟 |
| MapArmyOverlay | 201 | 军团覆盖 |
| **MapBar / MapMenuOverlay** | **202** | 🔴 地图顶栏 + 定居点菜单/城镇菜单覆盖层（点击定居点弹出的菜单） |
| MapIncidents / HeirSelection / MapMarriageOffer | 203 | 事件/继承人/求婚弹层 |
| MapConversation | 205 | 地图对话层 |
| MapRecruit / MapTownManagement / MapTroopSelection / MapTournamentLeaderboard | 206 | 征召/城镇管理/部队选择/锦标赛 |
| MapBar_ArmyManagement / MapArmyManagement | 300 | 军团管理 |
| EncyclopediaBar | 310 | 百科全书 |
| MapEscapeMenu / MapCampaignOptions / MapCheats | 4400+ | 🔴 系统菜单（ESC），自定义 UI 必须低于它 |
| MapReadyBlocker / MapSave | 9999+ | 加载遮罩，全场景最顶 |

Mission 侧（同 DLL 实测）：NameMarker=1、MissionQuestBar/AlarmState=10、Conversation=ViewOrderPriority。

**调用范例**（IM 聊天窗，2026-08-10）：`V.NewLayer(400, "ImChatLayer")`——高于全部地图玩法 UI（≤310），低于系统菜单（4400），系统菜单照常覆盖（符合「系统菜单自然覆盖」惯例）。

**关键文件**：`Core/VersionCompat.cs`（V.NewLayer）、`ImChat/ImChatView.cs:90`。查表出处：`ilspycmd Modules/SandBox/bin/Win64_Shipping_Client/SandBox.GauntletUI.dll | grep "new GauntletLayer("`。

## 🔴 GauntletLayer 摘层纪律 — 已 Finalize 的层禁止任何引擎操作（1.2.12 二次 Finalize = NRE）

**问题**：层随屏销毁被 `HandleFinalize` 后，`Close()` 里再 `RemoveLayer` → 崩溃。实机堆栈（2026-08-22，1.2.12）：`EventManager.OnFinalize → UIContext.OnFinalize → GauntletLayer.OnFinalize → ScreenBase.RemoveLayer → ImChatOpenButtonManager.Close`，NRE 在 `EventManager.OnFinalize` 的 `foreach (_widgetContainers)`（`_widgetContainers` 已置 null）。

**引擎事实（版本差异，反编译实锤）**：

| | 1.2.12 | 1.4.8 |
|---|---|---|
| `ScreenLayer.HandleFinalize` | `OnFinalize(); Finalized = true;` — **无幂等防护**，二次调 = 二次 `OnFinalize` | `if (IsFinalized) { FailedAssert; return; }` — 有防护 |
| `GauntletLayer.OnFinalize` | **不置 `UIContext = null`** → 二次 Finalize 能一路进到 `EventManager.OnFinalize()` 内部 → foreach null 字典 NRE | `ClearContext()` 里置 `UIContext = null` → 二次 Finalize 在 `UIContext.EventManager` 访问处 NRE |

**屏销毁不清 `_layers`**：`ScreenBase.HandleFinalize` 对 `_layers` 里每层调 `HandleFinalize()` 但**不移除**——层 Finalize 后 `HasLayer` 仍误报 true（`_layers.Contains` 只看列表）。1.2.12 的 `ScreenLayer.HandleFinalize` 无防护时，`HasLayer=true → RemoveLayer` 必然二次 Finalize。

**摘层守卫三件套（全部要）**：`HasLayer`（层可能已随屏销毁）+ `!V.LayerFinalized(_layer)`（**已死层跳过一切引擎操作**）+ try/catch（native 侧 double-release 仍有兜底风险）。已 Finalize 的层：`ReleaseMovie` / `RemoveLayer` / `InputRestrictions.ResetInputRestrictions` **全部禁止**——层死即引擎资源已释放，引用置空、下帧重挂新层即可（层残留屏 `_layers` 无影响，屏也已销毁）。

**调用范例**（`ImChatOpenButtonManager.Close` / `ImChatView.Close` / `NinjaNotificationMissionView.Close` 三处同款，2026-08-22）：
```csharp
bool layerDead = V.LayerFinalized(_layer);
try
{
    if (!layerDead)
    {
        if (_movie != null) { _layer.ReleaseMovie(_movie); _movie = null; }
        if (_layerOwnerScreen != null && _layerOwnerScreen.HasLayer(_layer))
            _layerOwnerScreen.RemoveLayer(_layer);
        else if (ScreenManager.TopScreen != null && ScreenManager.TopScreen.HasLayer(_layer))
            ScreenManager.TopScreen.RemoveLayer(_layer);
        _layer.InputRestrictions.ResetInputRestrictions();   // 按需
    }
}
catch (Exception ex) { try { DebugLogger.Log($"[X] Close 失败: {ex.Message}"); } catch { } }
_layer = null; _layerOwnerScreen = null; _movie = null;
```

**排查提示**：已 Finalize 层二次摘层，1.4.8 表现为 FailedAssert（弹窗/日志「Screen layer is already finalized」），1.2.12 直接 NRE——两版本都要按三件套守卫，不能只在 1.4.8 验证通过就以为安全。

**关键文件**：`Notify/ImChatOpenButtonManager.cs`（Close）、`ImChat/ImChatView.cs`（Close + MigrateLayerIfNeeded 判定）、`Notify/NinjaNotificationMissionView.cs`（Close）、`Core/VersionCompat.cs`（V.LayerFinalized）。


---

## 贴内容气泡（微信式）— TextWidget CoverChildren + MaxWidth

**问题**：聊天气泡要「贴文字宽度」（微信式），而非固定宽度方块。直接 StretchToParent 会全宽（文字看着左/右没对齐）；直接 CoverChildren + WordWrapping 不折行（无限撑宽）。

**方案**：**MaxWidth 必须放 TextWidget 上**，气泡（普通 Widget）CoverChildren 即可——反编译 `TaleWorlds.GauntletUI.dll` 实测两条机制：
- `TextLayout.MeasureChildren`：`fixedWidth = WidthSizePolicy != CoverChildren || MaxWidth != 0f` → TextWidget 自身 CoverChildren + MaxWidth≠0 时 `GetPreferredSize` 按 MaxWidth 折行；
- 默认 `LayoutImp.MeasureChildren`：父测量 = `子 MeasuredSize + 子 Margin` → 文本 Margin 天然当 padding 被气泡包住（无需额外 padding 容器）。

```xml
<!-- 单元素气泡：贴内容；HorizontalAlignment=Right 即右对齐（QQ 式文本再 TextHorizontalAlignment="Right"） -->
<Widget WidthSizePolicy="CoverChildren" HeightSizePolicy="CoverChildren"
        HorizontalAlignment="Right" Sprite="BlankWhiteSquare_9" Color="#3DA53D33">
  <Children>
    <TextWidget WidthSizePolicy="CoverChildren" HeightSizePolicy="CoverChildren"
                MaxWidth="520"  <!-- 🔴 折行关键：MaxWidth 在 TextWidget 上（🔴 禁止放气泡上：会把子 Margin 裁掉，长消息文字溢出底纹——实机踩过） -->
                Text="@Content" Brush.TextHorizontalAlignment="Right"
                WordWrapping="Wrap"
                MarginLeft="12" MarginRight="12" MarginTop="8" MarginBottom="8"/>
  </Children>
</Widget>
```

**🔴 多元素气泡（名字行+内容）必须内嵌垂直 ListPanel 堆叠**——普通 Widget 的 `OnLayout` 把**所有子元素 Layout 到同一个矩形**（反编译实测：`child.Layout(left2, bottom2, right2, top2)` 全部同一 rect），多个子元素必然完全重叠（「文字叠在一起」实机踩过）。结构：气泡（Sprite 底纹）→ 子 ListPanel（`Id="LWN_xxx"` + `VerticalBottomToTop`，双版本 swap 兼容，与消息流同款写法 → 先添加的子元素在顶部）→ 名字行/内容各自带 Margin（父测量含子 Margin 自动包边）。

```xml
<!-- 多元素气泡：名字行（上）+ 内容（下），ListPanel 堆叠防重叠 -->
<Widget WidthSizePolicy="CoverChildren" HeightSizePolicy="CoverChildren"
        HorizontalAlignment="Left" Sprite="BlankWhiteSquare_9" Color="#FFFFFF1A">
  <Children>
    <ListPanel Id="LWN_ImChat_BubbleOther" WidthSizePolicy="CoverChildren" HeightSizePolicy="CoverChildren"
               StackLayout.LayoutMethod="VerticalBottomToTop">
      <Children>
        <ListPanel StackLayout.LayoutMethod="HorizontalLeftToRight" MarginLeft="12" MarginTop="8">
          <Children><!-- 名字 + 时间 --></Children>
        </ListPanel>
        <TextWidget MaxWidth="520" Text="@Content" WordWrapping="Wrap"
                    MarginLeft="12" MarginRight="12" MarginTop="4" MarginBottom="8"/>
      </Children>
    </ListPanel>
  </Children>
</Widget>
```

**同坑引申**：普通 Widget 内多个子元素若想不重叠，只能靠 HorizontalAlignment/VerticalAlignment 对齐定位（如"标题 Stretch 左 + 徽标 CoverChildren 右"）——需要垂直排布的必须 ListPanel。

**关键文件**：`GUI/Prefabs/ImChat.xml`（他人/自己气泡两处，`LWN_ImChat_BubbleOther/Self`）。


---

## ScrollablePanel 贴底语义（Bottom 对齐聊天流）— val=MaxValue 才是贴底

**问题**：Bottom 对齐的聊天列表（新消息在最下、列表向上生长）的 `ScrollbarWidget.ValueFloat` 贴底语义极容易误判——曾两轮判反（「发送没拉到底」反复复现）。且内容增长时即使值设对也会漂移出新消息。

**引擎公式（反编译 `TaleWorlds.GauntletUI.dll` ScrollablePanel.UpdateScrollablePanel，v1.4.7 实测）**：
- Bottom 对齐（InnerPanel `VerticalAlignment="Bottom"`）：每帧 `offset = MaxValue - val`，`AdjustVerticalScrollBar` 同步 `val = MaxValue - offset`；
- **val=MaxValue → offset=0 = 贴底**（Bottom 对齐自然位，显示最新）；**val=0 → offset=MaxValue → 面板下移露出最旧消息**；
- **🔴 最大陷阱：内容不溢出时引擎强制 val=0 且 offset=0（全可见）**——看着像「val≈0=贴底」，实际是「内容短所以全可见」的巧合，一旦溢出语义立即翻转；
- 滚轮方向佐证：引擎 `offset += DeltaMouseScroll`（上拉=历史）⇔ 手动接管 `val -= delta` ⟹ `offset=MaxValue-val`；
- 手柄 `UpdateHandleByValue`：val=MaxValue → 手柄在底（与内容一致，勿再反向 patch）。

**🔴 第二个坑（内容增长漂移）**：贴底时新消息使 `MaxValue` 变大、val 不变 → `offset=MaxValue-val` 漂移出新消息。**数值判底（`val ≥ max-8`）在增长同帧必误判**（max 未更新）→ 弹提示/停跟新。**必须用状态机，不用数值**：

```csharp
private static bool _pinnedToBottom;                 // 贴底锁定态
// Tick 每帧：锁定态持续重 pin（防漂移；val=max → offset=0）
if (_pinnedToBottom) ScrollToBottom();
// 滚轮接管里：上拉（delta>0=历史）解锁；下拉被 clamp 顶到 max → 重新锁定
if (delta > 0f) _pinnedToBottom = false;
else if (IsMessageAtBottom()) _pinnedToBottom = true;
// ScrollToBottom(): ValueFloat = MaxValue + _pinnedToBottom = true（发送/提示条点击/切会话都走它）
```

**调用范例**（`ImChat/ImChatView.cs`，2026-08-10 十一轮）：`IsMessageAtBottom()` 判 `val ≥ MaxValue-8`；`ScrollToBottom()` 设 `val=MaxValue` 并锁定；`RefreshMessages` 的 hadNew 分支用 `_pinnedToBottom` 而非数值判底；滚轮 clamp `[0, max]`（防 val 越界把内容顶出底部）。

**关键文件**：`ImChat/ImChatView.cs`（Tick / HandleManualScroll / ScrollToBottom / IsMessageAtBottom）、`GUI/Prefabs/ImChat.xml`（MessageScroll：InnerPanel Bottom 对齐 + AutoHideScrollBars）。反编译出处：`ilspycmd bin/Win64_Shipping_Client/TaleWorlds.GauntletUI.dll -t TaleWorlds.GauntletUI.BaseTypes.ScrollablePanel`。完整排查记录：`plans/im-chat-system.md` 十轮/十一轮。

---

## ScrollablePanel 滚动三件套 — 🔴 holder 必须兄弟节点 + AutoHideScrollBars 超限才显

**问题**：静态长文本面板（NPC 探查板各 Tab：记忆/背包/部队）需要可滚动；`ClipRect/InnerPanel/VerticalScrollbar` 三件套有两个路径坑 + 一个行为误解。引擎没有「文本+滚动条」一体控件，ScrollablePanel+TextWidget 就是标准做法（Native SPChatlog 同构）。

**引擎路径解析（反编译 `TaleWorlds.GauntletUI.PrefabSystem.dll` WidgetExtensions.SetWidgetAttributeFromStringAux + `TaleWorlds.GauntletUI.dll` Widget.FindChild，v1.4.8 实测）**：Widget 类型属性（ClipRect/InnerPanel/VerticalScrollbar/Handle）的字符串值按 `BindingPath` 在**属性宿主控件**上解析——`..` = **跳到宿主父级**再按 Id 找直接子级，`\` 分隔，找不到返回 null（静默，不报错）。

- 🔴 **坑一（holder 必须是 ScrollablePanel 的兄弟节点）**：`VerticalScrollbar="..\Holder\Bar"` 的 `..` 指向 ScrollablePanel 的**父级**——holder 若放在面板**内部**，路径解析 null → `VerticalScrollbar=null` → `UpdateScrollablePanel` 竖向分支**整个跳过** + `OnMouseScroll` 滚轮输入被吞 → 滚动条画着但完全无用（NPCInfoBoard 记忆 Tab 2026-08-21 实录；与 ImChat 七轮 InnerPanel 路径 null 同族事故——**凡是 Widget 引用属性，先模拟 FindChild 验证路径**）。
- 🔴 **坑二（InnerPanel 路径必须与实际 ListPanel Id 完全一致）**：路径错 → InnerPanel=null → 引擎滚动更新异常中断（ImChat 七轮，日志 `inner=-1` 确诊）。
- **Handle 同理**：ScrollbarWidget 的 `Handle` 属性相对自身解析，Handle 必须是 ScrollbarWidget 的直接子级。

**AutoHideScrollBars 行为（误解高发）**：内容高度 ≤ ClipRect 高度时滚动条**自动隐藏**（`UpdateScrollablePanel` 里 `if (AutoHideScrollBars) flag3=false`）——「面板里没看到滚动条」= **内容没超限，正常**，不是 bug。对照验证法：长内容 Tab（如记忆调试全量视图）有滚动条 + 短内容 Tab 没有 = 系统正常。想常显改 `AutoHideScrollBars="false"`（内容短时挂空轨道，不推荐）。

**文本高度测量（单文本框也能驱动滚动）**：TextWidget `HeightSizePolicy="CoverChildren"` + `WordWrapping="Wrap"` 由 `TextLayout.MeasureChildren → _text.GetPreferredSize(fixedWidth, x, fixedHeight=false, …)` 返回**完整换行高度**（TextLayout 反编译 v1.4.8）——不必把长文本拆成多行控件，单个 TextWidget 高度正确时滚动范围按实际文本高度计算。`IsVisible` 要放 ScrollablePanel 上（隐藏时 `Measure` 直接返回 Zero 不测子级；切回 Tab 当帧自愈）。

**调用范例**（`GUI/Prefabs/NPCInfoBoard.xml`，7 个 Tab 全同构）：ScrollablePanel `LWN_InfoBoard_{Tab}Scroll`（IsVisible 绑定 + AutoHideScrollBars + `ClipRect="{Tab}Clip" InnerPanel="{Tab}Clip\{Tab}Inner" VerticalScrollbar="..\{Tab}ScrollbarHolder\{Tab}Scrollbar"`）→ 子级 ClipRect（ClipContents="true" + DoNotAcceptEvents）→ 内 ListPanel（CoverChildren 高度）；**holder 是 ScrollablePanel 的兄弟节点**（同内容区下），内部 ScrollbarWidget `AlignmentAxis="Vertical"` + Handle 直接子级 + Native `SPChatlog.Scrollbar.Handle` 笔刷。

**编辑纪律（脚本化替换 XML 事故实录）**：用 Python 按注释 header 做块替换时，end marker（如 `</Children>`）必须**唯一且位于 start 之后**——`str.index()` 会匹配到文件**更早处**的相同缩进标签（如标题栏的 `</Children>`），导致整段文件被重组复制（840 行事故，2026-08-21）。改完必须：①`xml.dom.minidom` 语法校验 ②模拟 FindChild 逐属性验证 ③Id 全局唯一检查。

**关键文件**：`GUI/Prefabs/NPCInfoBoard.xml`（7 Tab 滚动化范本）、`GUI/Prefabs/ImChat.xml`（MessageScroll + MessageScrollbarHolder 同构出处）。反编译出处：`ilspycmd bin/Win64_Shipping_Client/TaleWorlds.GauntletUI.PrefabSystem.dll`（SetWidgetAttributeFromStringAux 路径解析）、`TaleWorlds.GauntletUI.dll`（ScrollablePanel/Widget.FindChild/TextLayout）。

---

## 长文本 UI 显示纪律 — 有界预览 + 只读摘要 + 布局刷新节流（借鉴 BannerlordTalk ManagerTextPreviewPolicy）

**问题**：6–9 万字长文本（常识整库/大 prompt/长记忆）全文绑进 RichTextWidget/多行编辑控件 → Gauntlet 排版持续处理长文本卡死 UI（BannerlordTalk v1.0.2 实机 bug；v1.0.3 根治）。

**三件套**（`Knowledge/BannerlordTalk_逆向/v1.0.3/BannerlordTalk.UI.ManagerTextPreviewPolicy.decompiled.cs`，反编译范本）：

1. **有界预览**：`CreateBoundedPreview(value, notice)` 超 6000 字符截断 + 追加「预览已截断；完整内容仍保存/仍会完整导入」通告。截断三规则：①**UTF-16 代理对安全**——边界字符是 HighSurrogate+LowSurrogate 时前移一位（不截坏 emoji/生僻字，游戏 XML 解析器同样不认代理对）②后半段找最近换行回退到行首（不切半行）③截断通告带完整字符数。
2. **只读摘要**：`CreateKnowledgeSummary(ruleCount, charCount)` 固定尺寸摘要（512 上限），页面正文编辑器 `IsVisible=false` 隐藏，摘要 TextWidget 用 `DoNotAcceptEvents="true"` + `ClipContents="true"` + 固定高度——**预览与数据分离**：解析/校验/存储/复制仍走完整文本，截断只在显示层。
3. **布局刷新节流**：`RefreshResponsiveLayout` 先算全部布局值，**任一真正变化才触发 OnPropertyChanged**（v1.0.0 每帧无守卫全量通知 18 个属性）——分辨率/UI 缩放未变时不再每帧触发布局。VM 属性 setter 同样「值未变不发通知」。

**调用范例**：ChatterManagerVM（`Knowledge/BannerlordTalk_逆向/v1.0.3/BannerlordTalk.UI.ChatterManagerVM.decompiled.cs`）：知识页 RefreshPage 置 `EditorText=""`/`SecondaryText=""` + `KnowledgeSummaryText = CreateKnowledgeSummary(...)`；导入预览 `_importPreviewText = CreateKnowledgeImportPreview(FormatKnowledgePreview(...))`。

**适用场景**：本 mod 任何把长文本绑进 Gauntlet 控件的地方（IM 长消息渲染、config 整库展示、LLM 大 prompt 预览）。先抄 ManagerTextPreviewPolicy 静态方法，再套刷新节流。

---

## Brush 素材缝隙 — Extend 才是 9 宫格开关（缺它 = 整图拉伸 = 按钮拼接缝）

**问题**：竖排按钮 `MarginTop/Bottom=0` 布局本应无缝（反编译 `LayoutLinearVertical` 实证：`num2=num` 逐项紧贴，间距只来自 margin），但屏幕上按钮之间仍有明显缝隙——**缝隙不在布局，在 Brush 渲染**。

**根因（反编译 `TaleWorlds.GauntletUI.dll` BrushLayer 实证）**：
- **BrushLayer 没有 Type 属性**（不存在 `Type="Sliced"` 写法），`ExtendLeft/Top/Right/Bottom` 本身就是 9 宫格开关；
- `Extend=0`（默认）= **整张素材均匀拉伸**进 widget → 素材顶部/底部的透明/阴影边缘跟着缩放露出来 → 相邻按钮之间拼出「缝」；
- `Extend>0` = 九宫格：角落按原像素尺寸渲染、边拉伸、中间拉伸——素材边缘即按钮边缘，缝隙消失。

**Extend 取值惯例**（参照原版 `Modules/Native/GUI/Brushes/Main.xml` ButtonBrush2）：大按钮 271×84 用四边 `Extend 22`；矮按钮 `main_button_regular_big`（480×64）用 `ExtendLeft/Right=22, ExtendTop/Bottom=12`。**🔴 上下 Extend 必须 ≤ 按钮高/2**（否则角落 2×Extend > 高 → 中区为负，native 渲染行为不可控）——26px 按钮取 12（中区 2px），任意高度安全。

**调用范例**（`GUI/Brushes/MyBrush.xml` `LWN_Btn_Message`，2026-08-20）：三态三图层（Default=`main_button_regular` 深色 / Hovered=`main_button_done_hover` 金色高亮 / Pressed=`main_button_done` 按压缩暗），每层 Extend 22/12/22/12，Styles 用 IsHidden 切换图层。**三态素材的 Extend 必须一致**，否则 hover 时边框跳动。

**关键文件**：`GUI/Brushes/MyBrush.xml`、`GUI/Prefabs/ImChat.xml` + `ImChatCompact.xml`（消息按钮 `Brush="LWN_Btn_Message"`）。

---

## 🔴 全屏 UI / ESC 菜单统一检测 — `GUI/UiFullScreenHelper.cs`（2026-08-23）

**问题**：常驻层（IM 呼出按钮层 350 等）在玩家打开全屏 UI（技能/背包/队伍/家族/王国/任务）或 ESC 菜单时必须「不显示 + 不响应」，否则穿透到界面之上（挡操作 + 出戏）。

**方案**：两个静态判定，均为**字符串判定不引引擎强引用**（`ModInput.IsSystemModalActive` 同款：漏判最坏多显一次，不崩）：
- `IsFullScreenUiOpen()`：`ScreenManager.TopScreen.GetType().Name` Contains 匹配 marker 表。🔴 marker 必须收**实机类名变体**：个人技能屏实机 = `GauntletCharacterDeveloperScreen`（**不含** "CharacterScreen" 子串，2026-08-23 日志定位漏网）；`GauntletOptionsScreen`（ESC 选项屏）同样漏过。每个新全屏屏先 `custom.print_topscreen` 看类名再登记。
- `IsEscapeMenuOpen()`：遍历 `TopScreen.Layers` 找 `GauntletLayer.Name` 含 "EscapeMenu"（层存在 = 打开，RemoveLayer = 关闭；`ScreenLayer.Name { get; private set; }` 可读）。

**ESC 菜单两条路径（反编译实锤 2026-08-23）**：
| 场景 | 形态 | 层序 vs 按钮层 350 |
|------|------|------|
| Mission | `MissionEscapeMenu` 层（`MissionGauntletEscapeMenuBase`，MissionBehavior，`ViewOrderPriority=50`） | **50 < 350 → 穿透，必须显式判定** |
| Campaign | `MapEscapeMenu` 层（`GauntletMapEscapeMenuView`，MapView，层序 4400） | 4400 > 350 → 层序压盖，判定仅保险 |
| 其他屏（教育/捏脸/旗帜） | 屏自己的 GauntletLayer `LoadMovie("EscapeMenu")`（无独立层，私有 `_isEscapeOpen`） | 已被 IsFullScreenUiOpen 覆盖 |

**接入范式（显示 + 激活分治，三处共用同一判定）**：显示 = `ShouldShow` / VM `IsVisible`；激活 = `ModInput.Tick` 模态门控 `ResetAll`（物理键轮询 `Input.IsKeyDown` 拦不住，必须状态机清）；打开兜底 = `CanOpen`。🔴 **禁止只做显示不做激活**（2026-08-23 用户裁定：要么一起显示+激活，要么都不）。

**性能**：`TopScreen.Layers` 数（Map ~9 / Mission ~5）每帧一次短字符串 IndexOf < 1μs，可忽略，不做缓存/事件驱动（层开闭由原版 view 管，hook 复杂度不值）。

**关键文件**：`GUI/UiFullScreenHelper.cs`、`Notify/ImChatOpenButtonManager.cs`（ShouldShow + OnButtonClick）、`Input/ModInput.cs`（Tick 门控）、`ImChat/ImChatView.cs`（CanOpen）。

---

## 🔴 补丁目标必须精确到实际调用链的类 — ScreenBase.OnFrameTick vs MissionScreen.OnFrameTick（2026-08-23 实机教训）

**现象**：把 Mission 侧驱动迁到 `ScreenBase.OnFrameTick` 补丁 → Mission 内完全不触发，功能静默失效（呼出按钮 Mission 内无人驱动 → 显示没了；日志 Mission 会话零条挂载记录实锤）。

**根因**：**MissionScreen override 了 OnFrameTick 不走基类**——Harmony 补丁挂在基类方法上，override 替换方法体后补丁永不触发。同一条补丁两条命运：Campaign（MapScreen 走基类）正常，Mission（MissionScreen override）失效。

**纪律**：
- Campaign 钩子 = `ScreenBase.OnFrameTick`（`ImScreenFrameTickPatch`，暂停也跑）；
- Mission 兜底 = `MissionScreen.OnFrameTick` **独立补丁类**（`ImMissionButtonRefreshPatch`），Postfix **直调目标函数**（`ImChatOpenButtonManager.Tick`），不经过 Campaign 专用入口（`OnScreenFrameTick`）；
- `MissionView.OnMissionTick` 在 Mission ESC（`MBCommon.PauseGameEngine`）期间可能停摆——UI 刷新兜底挂 `MissionScreen.OnFrameTick`（UI 层回调，暂停也触发：ESC 菜单本身要渲染交互）；
- 🔴 类级多 `[HarmonyPatch]` 属性**不要用于**跨基类/派生类目标（架构混，上轮教训）；需要多目标就拆独立类。

**关键文件**：`ImChat/ImScreenFrameTickPatch.cs`（`ImScreenFrameTickPatch` + `ImMissionButtonRefreshPatch`）、`ImChat/ImChatView.cs`（OnScreenFrameTick）、`Interaction/InteractionMissionView.cs`。

---

## 🔴 往原版 GauntletUI 屏动态插入按钮（版本无关插入点）— `GUI/SecretLetterButtonInjector.cs`（2026-08-29 实机修复后登记）

**解决**：不覆写原版 prefab（避开与其他 UI mod 同名互斥），纯 C# 在队伍屏/家族屏「交谈按钮」旁注入「密信」按钮：扫描原版 widget 树 → 插入 → 点击 → IM 私聊（关屏再开）。

- **扫描**：`ScreenManager.TopScreen.Layers` → `GauntletLayer.UIContext.Root` → DFS 按 Id/类型名找（`FindWidgetById` / `FindAllWidgetsById` / `FindWidgetByType` 三个助手）；0.3s 节流 + TopScreen 类名过滤（`Contains("PartyScreen")` 等，覆盖各版本命名变体）。
- **🔴 插入点必须是「有布局算法的容器」**：`AddChildAtIndex(btn, idx + 1)` 的目标若是普通 Widget（无 StackLayout），子节点渲染在其原点、必然叠在目标按钮上（2026-08-29 实机事故：H盘 1.5.2 行结构 `TalkButton→容器→ButtonsList` 与旧客户端 `TalkButton→ButtonsList`（无容器）两代形态差异，旧代码插进 ButtonCarrier 直接叠在交谈按钮上）。**版本无关统一算法**：
  ```csharp
  Widget wrapper = talkWidget;
  if (!(talkWidget.ParentWidget is ListPanel))
      wrapper = talkWidget.ParentWidget;   // 有容器包装（1.5.2 形态）：跟在容器后
  Widget insertInto = wrapper.ParentWidget; // 两种形态下都是列表本体
  if (!(insertInto is ListPanel)) return false;   // 结构未知 → 安全跳过（不注入、不崩）
  ```
- **数据桥**：读原版绑定已赋值的 widget 属性（反射，属性名跨版本稳定）：队伍行根 `PartyTroopTupleButtonWidget.CharacterID`（=`Character.StringId`）；家族详情 `CharacterTableauWidget.CharStringId`。**行根匹配用类型名不能用 Id**——CustomType 实例化时外层模板 Id 被覆盖为 null（反编译实锤）；家族行根是普通 Widget 类型不唯一 → 从 UIContext.Root DFS 找全局唯一的 `CharacterTableauWidget`。
- **可见性**：每帧跟随锚点可见性（队伍=交谈按钮最外层包装 `@IsTalkableCharacter`；家族=含 tableau 的详情面板容器）+ 总闸（`PlotEnabled && IsLLMConfigured`——未配置 LLM 按钮同步隐藏，传讯入口整体封死纪律）。hover 提示 = 手动 hit-test（`IsPointInRect(Input.MousePositionPixel, …)` + `MBInformationManager.ShowHint`）。🔴 **hover 提示两个引擎坑（2026-08-29 实机反馈）**：①**屏激活门控**——判定再加 `TopScreen` 必须是注入按钮所属屏（Party/ClanScreen），否则屏关闭后树销毁窗口期按钮矩形仍是旧坐标，鼠标扫过 = campaign 上凭空弹提示；②**周期重发**——引擎 tooltip 显示后自动淡出，代码只在「进入瞬间」Show 一次 → 悬停中不重显，持续悬停每 ~3s 重发 `ShowHint`。详见 pitfalls「ShowHint tooltip 寿命 + 销毁窗口期旧矩形」。
- **点击 → 关屏再开**：`ImChatManager.GetDirectConversation(heroId)` → 原版关屏路径（队伍屏 `PartyScreenHelper.CloseScreen` 反射 / 家族屏 `GameStateManager.PopState(0)`——**裸 PopScreen 绕过 GameState 栈 = 地图黑屏**，两次实机复现）→ `ImChatView.SetPendingSecretLetter(heroId)`（下帧 TopScreen 稳定后开 IM 定位私聊）。
- **英雄门**：注入前用同一个行映射反射判定「英雄行且非玩家行」（`IsMainHero` 行根反射 + `Hero.AllAliveHeroes` 校验）——非英雄行根本不注入。省去「全行注入 + 可见性隐藏」的隐藏按钮布局盒空缺坑，也免除每 0.3s 扫描对全行创建-摘除的布局抖动。
- **🔴 翻转为可见时强制重排**：`SetSiblingIndex(GetSiblingIndex(), force: true)`（引擎坑见 pitfalls「不可见子节点无布局盒」；签名 1.2.12~1.5.x 一致）。

**关键文件**：`GUI/SecretLetterButtonInjector.cs`（注入/可见性/hover/点击全链路）、`ImChat/ImChatView.cs`（OnScreenFrameTick 驱动 + `SetPendingSecretLetter` + `CanOpen(screenStateAgnostic)`）、语言 XML `LWN_im_secret_letter_hint`。设计文档：`plans/im-secret-letter-button.md`。

---

## 🔴 立绘/头像 Sprite 按需加载 + 内容包契约 — `GUI/SpriteAssetsManager.cs` + `Data/PortraitRegistry.cs`（2026-08-31 实机验收通过后登记）

**解决**：内容包（如 ShokuhoTaikouExpansionPack）打进 tpac 的 2000+ 张立绘/头像，怎么"显示一张" + "内存不爆"。引擎 SpriteCategory 默认整分类全量加载（2700 张 ≈ 600MB 变鸭梨），引擎原生 partial-load API 直接可用，封装成两个静态管理器。

**引擎机制（反编译实锤，先懂再用）**：
- 引擎 UI 初始化时**自动**做两件事，内容包零注册：①挂载所有模块 `AssetPackages/*.tpac`；②解析所有模块 `GUI/*SpriteData.xml`（文件名必须 `*SpriteData.xml` 结尾，放 GUI/ 下即可）。
- `SpriteCategory.Load` → 纹理名 = `SpriteSheets\{Category}\{Category}_{N}`（N 1-based，取最后一段 → `GetFromResource("{Category}_{N}")`）。
- partial 三件套（1.2.12~1.5.1 签名一致，无 #if）：`InitializePartialLoad()` 进入按需模式 / `PartialLoadAtIndex(ctx, depot, sheetIndex)` 加载单张 / `PartialUnloadAtIndex(idx)` 释放单张。
- SpritePart 每卡一格（SheetID=N, SheetX/Y=0, SheetSize=卡尺寸）→ UV=0..1；`SpritePart.Texture` 读 `SpriteSheets[SheetID-1]`（partial 兼容）。

**API（接入方只用两个类）**：
```csharp
// ① 显示一张：唯一入口，每次显示时调用，**不要缓存返回的 Sprite 对象**
Sprite s = SpriteAssetsManager.GetOrLoad("lwnprof_bustup_517");
if (s != null) { icon.Sprite = s; icon.IsVisible = true; }   // 先例：SecretLetterButtonInjector 的 ImageWidget

// ② 角色→哪张卡（阶段数据）：StringId → 阶段列表（含 stage/tkid/sprite 名）
var stages = PortraitRegistry.GetStagePortraits("lord_1_kinoshita");   // 秀吉 4 阶段
var cur = PortraitRegistry.GetStagePortrait("lord_1_kinoshita", "藤吉郎");
string emoSprite = PortraitRegistry.GetEmotionSpriteName("361", "happy", isBustup: true);
```

**内存纪律（LRU 已内建）**：bustup 桶 12 张 / minihead 桶 64 张（按字节分档）；驱逐 = `PartialUnloadAtIndex` + 250ms（≈2 帧）宽限 + 只逐最近未用；跨场景/读档引擎卸载后会自动重建 partial 状态。全程 try/catch → null 降级（铁律 1）。

**内容包契约（数据怎么喂进来）**：内容包自备三件（由 `ArtSource/scripts/build_profile_pack.py` 生成，禁手改）——
- `AssetPackages/lwnprof_*.tpac`（texture 名 = `{Category}_{N}`）
- `GUI/LWProfilesSpriteData.xml`（4 个 category：lwnprof_bustup/mini/emobustup/emomini；sprite 名 = `lwnprof_{kind}_{tkid}` / `lwnprof_em{kind}_{tkid}_{emo}`）
- `ModuleData/AssetRegistry/ProfileStages.csv`（列：StringId,stage,tkid,bustupSprite,miniheadSprite）+ `ProfileEmotion.csv`（tkid,emotion,bustupSprite,miniheadSprite）
PortraitRegistry 扫**所有模块**的 `ModuleData/AssetRegistry/*.csv`（列名大小写敏感，TextFieldParser 处理 BOM/CRLF；CsvLoader 两行表头格式不可复用）。

**坑（实机采集，防重踩）**：
- 🔴 **ImageWidget 半透明垫底必须设 `bg.Brush.Color`**——渲染走 BrushRenderer，色调 = `Brush.DefaultStyle.GetLayer("Default").Color`（默认纯白 100% 不透明）；`Widget.Color` 不参与渲染（文字等用途）。这就是"立绘显示正常但背景白块"两次实机截图根因（已修）。
- 🔴 **UI 调试热重载（CheckForChanges → SpriteData.Reload）会替换全部 SpritePart/SpriteCategory 对象**：不缓存 Sprite/Category 引用（每次现取 `GetSprite`/`part.Category`），否则旧引用指向已卸载纹理 → 黑块。
- 内容包没装/tpac 名打错 → 返回 null + `[SpriteAssets]` 日志，不崩（铁律 1）。

**关键文件**：`GUI/SpriteAssetsManager.cs`（GetSprite/GetOrLoad/EnsureLoaded/Release/ReleaseAll + LRU）、`Data/PortraitRegistry.cs`（GetStagePortraits/GetStagePortrait/GetEmotionSpriteName + CSV 扫描）、`Core/VersionCompat.cs`（`V.UIResourceDepot()`/`V.GetSpriteCategory()`——1.2.12 与 1.3+ 差异适配）。生产工具：`tools/face-pipeline/tpactool/TpacToolCLI`（makepack/inspect 命令，`TpacToolCLI/Bc3Encoder.cs` 内嵌 DXT5/BC3 编码器），产物生成器 `ShokuhoTaikouExpansionPack/ArtSource/scripts/build_profile_pack.py`。完整计划：`plans/scenario-campaign-mode/附录-立绘显示接入与分发方案.md`。

---

## tpac 打包链：任意 PNG → 引擎原生纹理包 — TpacToolCLI makepack/inspect（2026-08-31 登记）

**解决**：无官方 TPAC 编译器，第三方 `TpacTool`（MIT, szszss）能读写但不含打包命令。本项目扩了 CLI 两个命令（`TpacToolCLI/Program.cs` + `MakePack.cs` + `Bc3Encoder.cs`）：

- `tpaccli inspect --packdir <dir> --filter <名字>`——打印 texture **全字段**（Flags/SystemFlags/格式/OwnerGuid 等）。**模板字段第一站**（做新包前先 inspect 一个同款现成纹理，照着填：Flags=[dont_degrade,dont_delay_loading]、SystemFlags=[has_alpha]、Source=""、u3=1/byte=2/u4=1/u6=4/u7=1701736302、OwnerGuid==asset.Guid 实锤）。
- `tpaccli makepack --manifest <json> --out <dir>`——manifest 描述 `{name, png, width, height}` → PNG（TpacTool.IO 内置 BigGustave 解码，零 NuGet）→ 内嵌 BC3/DXT5 编码（`Bc3Encoder.cs`：PCA 主轴端点 + c0>c1 强制（否则解码器切 3 色模式出透明黑）+ 端点重定位；直 alpha 不 premultiplied）→ 组装 Texture asset（模板字段 + **确定性 GUID**（名字 hash）+ 段 OwnerGuid 同值）→ `AssetPackage.Save`（数据段自动 LZ4HC）。
- **验证闭环**（每轮打包必做）：`tpaccli dump --format png` 回解 → 与源 PNG 逐像素 diff（mean 1.78/255、alpha 0.83 上限 = DXT5 物理极限；全量 2720/2720 PASS）。
- 纹理格式定版：**DXT5（BC3）无 mipmap**（织丰官方 UI 同款；原版部分 UI 是 BC7 也兼容，但 TpacTool 回解链只解 DXT1-5，选 DXT5 保离线验证）。

**关键文件**：`tools/face-pipeline/tpactool/TpacToolCLI/`（Program.cs/MakePack.cs/Bc3Encoder.cs）、`TpacTool.Lib`（AssetPackage/Texture/TexturePixelData，仅只读使用）、`ShokuhoTaikouExpansionPack/ArtSource/scripts/build_profile_pack.py`（生成 manifest + SpriteData XML + 内容包 CSV 的完整链路）。参考：`Knowledge/tpac资源替换打包指南.md`（换脸场景 + 五个坑）。
