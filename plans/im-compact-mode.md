# IM 缩略模式 + 密信 ninjareport 通知 — 实施计划

> 状态：待实现（2026-08-15）。用户裁定：外层 sprite 硬编码换不同样式，**不**加配置项。
> 相关轮子：`plans/rules/wheels.d/im.md`（IM 行为层）、`wheels.d/ui.md`（UI 层）。

## 需求

1. **缩略模式**：小面板（贴底居中，偷窃 UI 同位置），只显示 频道下拉 + 消息区 + 输入框 + 发送；消息区 = 未决锚点卡与最新消息，**共存时显示 2 条紧凑排布**（用户裁定）；标题行带 放大/关闭；完整模式标题带关闭按钮**左侧**加 缩略 按钮。
2. **可拖动**：按住标题行拖动，位置会话内记忆，默认贴底居中（StealBar 同款 `Center + Bottom + MarginBottom=70`）。
3. **频道切换**：自绘**上开式**下拉（仿原版 Options 下拉项样式）。
4. **密信通知**：两种模式都关闭时，Direct 会话（密信）来新消息 → ninjareport 形式圆环通知，**外层 sprite 用不同的（硬编码，不配置）**；Mission 内可用性按未验证假设处理 + 自动消失兜底。

## 关键技术结论（已反编译验证，Plan agent 复核）

- **ScreenManager.EarlyUpdate 输入分发**（TaleWorlds.ScreenSystem.dll）：图层只在鼠标位于层内 `DoNotAcceptEvents=false` 的 widget 矩形时获得鼠标输入；键盘只在层内 EditableTextWidget 聚焦时获得。→ 缩略面板层常驻 `MouseButtons|Keyboardkeys`（+下拉开时 `MouseWheels`）mask，面板矩形外场景输入天然不被吞。
- **`UIContext.EventManager.ClearFocus()`** 与 `GauntletLayer.IsFocusedOnInput()`（= `FocusedWidget is EditableTextWidget`）公共可用。
- **单条 VM 绑定**：容器 `DataSource="{X}"` + 子控件相对绑定 = Gauntlet 标准机制（vanilla 先例 BannerBuilderScreen.xml:376）。
- **ItemTemplate 命令解析**：Command.Click 在数据项 VM 上反射（项目先例 ImChannelVM.ExecuteSelect / ImButtonVM.Execute）。
- **同 VM ReleaseMovie+LoadMovie 复用安全**（vanilla OnResourceRefreshBegin/End 同款流程）。
- `Widget.PositionXOffset/PositionYOffset/GlobalPosition/Size` 可编程读写；`ScreenManager.UsableArea` 静态。
- 竖排 ListPanel：`Id="LWN_*"` + `VerticalBottomToTop` + 子元素按视觉顺序（双版本 swap patch 惯例）。
- `InputUsageMask`（TaleWorlds.Library）：Invalid=0 / MouseButtons=1 / MouseWheels=2 / Keyboardkeys=4 / Mouse=3 / All=7。

## 风险处置清单（审查落点）

| 风险 | 处置 |
|------|------|
| P0-1 通知 Mission 安全未验证（反编译门控支持乐观面，但 2026-08-11 实机记录矛盾，见 pitfalls.md:8-34） | 当**未验证假设**：实现后第一步实机测（弹通知 → 攻击/格挡/滚轮/移动各测一遍）；失败 → 降级为 Mission 内不弹（沿用旧守卫）或 mask 降 `InputUsageMask.Invalid`。自动消失 10s 兜底必须有 |
| P0-2 面板死区穿透 | 🔴 面板容器 `DoNotAcceptEvents=false`（半模态岛：矩形内点击被层吞不穿透场景），全屏根 true |
| P1-1 锚点 ≠ 最新消息 | 用户裁定：两者共存显示 2 条紧凑排布（行 A 锚点卡 + 行 B 最新消息） |
| P1-2 广播穿透缺口 | CompactAnchor/Latest = `_vm.Messages` 中**同一实例**（UpdateCardAnchors/NotifyMessageShapeChanged/NotifyPlanStateChanged 都遍历 `_vm.Messages` 广播） |
| P1-3 模式切换清理 | SwitchMode() 集中：ReleaseMovie→ClearFocus→LoadMovie→清缓存→重放位置→RefreshAll（顺序不可乱，防同 name 双 movie 叠显 / 死 widget 引用 / movie 泄漏） |
| P1-4 拖动 vs 按钮 | 位移阈值 ≥4px 才进入拖动态；把手 = 拉伸标题容器（横排剩余空间，rect 天然排除按钮簇） |
| P2-1 通知无 Tick 宿主 | 计时挂 ImChatView.Tick（`!IsOpen` 提前 return **之前**） |
| P2-2 下拉收起/溢出/z-order | Tick 轮询收起（根无点击盾）；浮层自带深色底 + MaxHeight/ClipContents；声明在输入行**之后**（z-order）；切模式重置 IsChannelListOpen=false；下拉开时不 ClearFocus |
| P2-3 通知点击失败路径 | onConfirm 先 `CanOpen()`，失败**不关通知**（或 DisplayMessage 降级） |
| P2-5 ClearFocus 附加效应 | 用 `_layer.IsFocusedOnInput()` 判定；只在**点击面板外**时清（打字中鼠标悬停面板外不打断）；接受 ≤1 帧键盘残留 |

## 改动文件

### 1. 新 `GUI/Prefabs/ImChatCompact.xml`（模块根，运行时加载）
- 根：全屏透明 `DoNotAcceptEvents="true"`（不挡场景、无点击盾牌）。
- 面板 `Id="LWN_ImChat_CompactPanel"`：Fixed 宽 560、**CoverChildren 高**（1 条 ~165px / 2 条 ~210px，贴底向上增长不抖动），`Center + Bottom + MarginBottom=70`，`canvas_dark` + `frame_9` 边框（Inquiry 三层构造），🔴 `DoNotAcceptEvents=false`。
  - 竖向 ListPanel（LWN 惯例）：
    - ① 标题行（横排）：`[Id="LWN_ImChat_CompactDragHandle" 拉伸容器（内嵌 `@Title` 频道名 + `@CompactStatusText` 待决徽标）][放大 ExecuteExpand][关闭 ExecuteClose]`
    - ② 消息区两行：
      - 行 A `DataSource="{CompactAnchor}"` `IsVisible="@HasCompactAnchor"`：卡片气泡（名字行 SenderName/NameColor + 修改版徽标 + **CompactContent 单行截断正文** + 横/竖按钮行——绑定 CardButtons/VerticalCardButtons/IsHorizontalButtons/IsVerticalButtonsVisible，竖排 Id 带 LWN_ 前缀）
      - 行 B `DataSource="{CompactLatest}"` `IsVisible="@HasCompactLatest"`：迷你气泡（SenderName + CompactContent，无按钮）
    - ③ 输入行（横排）：`[下拉按钮 `@SelectedChannelText` + ExecuteToggleChannelList][EditableTextWidget Id="LWN_ImChat_CompactInput" `@InputText`/`@PlaceholderText`][发送 ExecuteSend `@SendText`/`CanSend`]`
  - 频道下拉浮层（**声明在 body 之后**）：`Id="LWN_ImChat_ChannelList"`，Fixed 320x200，`VerticalAlignment="Bottom"` 贴输入行上方向上生长（可溢出面板顶部，自带 `#100404FF` 深色底 + frame_9），`IsVisible="@IsChannelListOpen"`，内部 ScrollablePanel（仿 ChannelScroll 结构：ClipRect+InnerPanel+滚动条 AutoHideScrollBars）+ `DataSource="{ChannelOptions}"` 项（`Standard.DropdownItem`/`SPOptions.Dropdown.Item.Text` 原版观感，`Command.Click="ExecuteSelect"` → 数据项方法），MaxHeight + ClipContents 防出屏。

### 2. 新 `GUI/Prefabs/ImSecretNotify.xml`
- 仿 CustomNotify.xml：右缘外层 130x130 `Id="LWN_ImSecretNotify_Ring"`（🔴 **Sprite = `BlankWhiteCircleOutlined` + Color="#C89B4CFF"**——硬编码换不同 sprite，用户裁定不加配置）+ 内层按钮 100x100 新笔刷 `Brush_CircleButton_SecretLetter` + hover 展开摘要 + 关闭 X（照抄 CustomNotify 结构）。

### 3. `ExampleModVS/ExampleMod/ExampleMod/ImChat/ImChatVM.cs`
- 新类 `ImChannelOptionVM : ViewModel`：`StringItem`（标题+未读数，setter 广播）、`ConversationId`、`ExecuteSelect()` → `ImChatView.SelectConversation`。
- `ImChatVM` 新增：
  - `ImMessageVM CompactAnchor/CompactLatest` + `bool HasCompactAnchor/HasCompactLatest`（DataSourceProperty）
  - `MBBindingList<ImChannelOptionVM> ChannelOptions`、`bool IsChannelListOpen`、`string SelectedChannelText`、`string CompactStatusText`（待决徽标）
  - `CompactButtonText`（缩略）/`ExpandButtonText`（放大）——本地化文案属性（照 MakePlanText 先例）
  - `ExecuteToggleChannelList()` / `ExecuteToggleCompact()` / `ExecuteExpand()`
- `ImMessageVM` 加 `[DataSourceProperty] string CompactContent`：Content 截断 ~48 字 + "…"（普通 TextWidget 用原文不带富文本标签）。

### 4. `ExampleModVS/ExampleMod/ExampleMod/ImChat/ImChatView.cs`
- `enum ImChatMode { Full, Compact }` + `_mode`（默认 Full）；`Open()` 的 LoadMovie 按 `_mode` 选 prefab。
- `SwitchMode()` 集中（P1-3 顺序）：①`ReleaseMovie(_movie)`；`_movie=null` ②`_layer.UIContext.EventManager.ClearFocus()` ③`LoadMovie(_mode==Compact?"ImChatCompact":"ImChat", _vm)`；`_movie` 赋新值 ④清缓存（`_messageScrollPanel`/`_compactPanel`/`_compactDragHandle`/下拉/输入框引用 = null）⑤重放 `_compactPosX/Y` ⑥`_vm.IsChannelListOpen=false` ⑦`RefreshAll()`。
- `ToggleCompact()/ToggleExpand()`；`OpenCompact(conv)`：已开 → 切缩略+SelectConversation；未开 → `_mode=Compact`+`Open(conv)`。
- `RefreshMessages()` 末尾调 `RefreshCompact()`（覆盖全部既有调用点）：
  - 锚点 vm = `_vm.Messages` 中 `IsCardAnchor==true` 的实例；最新 vm = 末条；同一消息时最新置空（P1-2 实例复用）。
  - 刷新 `SelectedChannelText`（标题+未读）、`ChannelOptions`（顺序同 RefreshChannels：附近/队伍/家族/王国/私聊）、`CompactStatusText`（锚点卡存在时 = `LWN_im_compact_pending`）。
- `Tick` 缩略模式分支：
  - 拖动：`Input.IsKeyDown(MouseKeyLeft)`；按下于把手 rect → 待命，位移 ≥4px → 拖动；`_compactPanel.PositionXOffset/YOffset = 起始 + 位移`；clamp 用渲染矩形推导：`x ∈ [-(usableW-560)/2, (usableW-560)/2]`，`y ∈ [70-usableH, 70+panelH]`；位置存静态字段。
  - 焦点释放（P2-5）：`_layer.IsFocusedOnInput() && Input.IsKeyPressed(MouseKeyLeft) && !overPanel && !IsChannelListOpen` → `ClearFocus()`。
  - 下拉收起（P2-2）：`IsChannelListOpen && IsKeyPressed(MouseKeyLeft) && !overPanel && !overChannelList` → 关。
  - `ImSecretNotifyManager.Tick(dt)`（提前 return 之前）。
- `NotifyIncoming` 分流：`conv.Type == Direct` → `ImSecretNotifyManager.Show(summary, () => OpenCompact(conv))`（summary 格式沿用现 NotifyIncoming）；其余 → 旧 NinjaNotificationManager 不变（Campaign only）。
- 静态字段：`_mode`、`_compactPanel/_compactDragHandle`、`_compactPosX/Y`、`_compactDragging/_dragStartMouse/_dragStartX/Y`。

### 5. 新 `ExampleModVS/ExampleMod/ExampleMod/Notify/ImSecretNotifyManager.cs` + `ImSecretNotifyVM.cs`
- Manager（静态，仿 NinjaNotificationManager）：`Show(text, onConfirm)` → 建层 `V.NewLayer(400,...)` + LoadMovie("ImSecretNotify")，mask = `Mouse`（hit-test 门控）；`Tick(dt)`：自动消失计时（**硬编码 10s**）+ Show 前 Close 旧层；`Close()` 仿旧。
- onConfirm（P2-3）：先 `ImChatView.CanOpen()`，失败 → 不关通知；成功 → `OpenCompact(conv)` + Close。
- VM：照抄 NinjaNotificationVM（IsHovered/ShouldExpand/ReportText/ExecuteOnHoverBegin/End/ExecuteCloseHoverBegin/End/ExecuteSelect/ExecuteClose）。

### 6. `GUI/Brushes/MyBrush.xml`
- 新笔刷 `Brush_CircleButton_SecretLetter`：Default sprite = `SPGeneral\MapNotification\notification_illustration_conspiracy_quest`，样式集照抄 `Brush_CircleButton_NinjaReport`（Default/Hovered/Pressed/Selected），Default 色 `#C89B4CFF`、Hovered 亮 `#E8C55AFF`。

### 7. 本地化（`ModuleData/Languages/std_LivingWorldNpcs_strings.xml` EN + `CNs/` 同文件；铁律 13/14 无 emoji）
- `LWN_im_btn_compact` = "Collapse" / "缩略"
- `LWN_im_btn_expand` = "Expand" / "放大"
- `LWN_im_compact_pending` = "Pending" / "待决"

### 8. `ExampleModVS/ExampleMod/ExampleMod/ExampleMod.csproj`
- 登记 `Notify\ImSecretNotifyManager.cs`、`Notify\ImSecretNotifyVM.cs`。

## 明确不做（用户裁定/范围外）

- ❌ `ImSecretNotifySprite`/`ImSecretNotifySeconds` 配置项——sprite 硬编码不同样式，时长硬编码 10s（Settings/config.json 均不动）。
- ❌ 群聊（队伍/家族/王国/附近）消息在 Mission 的通知——维持现状（旧 NinjaNotification Campaign only）。
- ❌ 缩略模式位置跨存档持久化——会话内静态字段即可。
- ❌ 手柄支持（下拉/拖动走鼠标）——维持现有 IM 的桌面优先路线。

## 设计纪律对照

- 铁律 13/14：新文案全走 LWNTextHelper + 语言 XML。
- 双配置体系：本次**零新增配置**。
- 规则 18（平权）：无新增玩家/NPC 双份逻辑；锚点/按钮行复用 ImMessageVM 单一管线。
- ui.md：LWN 前缀 + VerticalBottomToTop 惯例、Inquiry 三层构造、贴内容气泡（MaxWidth 在 TextWidget）。

## 验证

1. `dotnet build`（MB2_PATH 已配置，v1.4.8）编译通过。
2. `Scripts/validate_localization.py` 通过（新增键无占位符）。
3. 游戏内手测（DebugLogger RuntimeLog 辅助）：
   - **第一优先（P0-1）**：Mission 内弹密信通知 → 左键攻击/右键格挡/滚轮/移动各测一遍；失败即按处置降级。
   - 完整模式标题带「缩略」→ 切缩略：频道名/消息区/输入框/发送齐全，放大/关闭可用；来回切换无残留（无叠显、滚动正常）。
   - 缩略下 WASD 移动 + 攻击正常（面板外点击/滚轮不被吞）；面板矩形内点击不穿透场景；点输入框打字正常；点击场景后键盘释放回游戏（打字中悬停面板外不打断）。
   - 拖动标题行 → 面板跟随、clamp 不出屏；点放大/关闭不误拖；切模式后位置保持。
   - 频道下拉上开显示 队伍/家族/王国/附近/私聊，点选切换会话、未读数正确；点外部自动收起。
   - 计划卡片待决 + 后续聊天消息 → 2 条紧凑排布（卡片带按钮可点、最新消息可读）；仅最新消息时 1 条。
   - 关闭面板后私聊 NPC 发密信 → 圆环通知出现（Mission/Campaign 都测）、hover 展开、点击打开缩略模式定位会话、~10s 自动消失。
