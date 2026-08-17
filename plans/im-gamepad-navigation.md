# IM 手柄手动导航 — 完整设计（逐按钮 × 方向转移矩阵）

> **状态**：📝 设计稿（2026-08-17，待实现；v2 修订：回落点规则 / 卡片按钮焦点视觉 / 手柄滚消息 / 重建节流）
> **主题**：IM 面板（完整/缩略）手柄导航的**手动实现**——每个可聚焦元素定义「按 ↑↓←→ 各移动到哪 + A 激活动作」，确定性可控，替代引擎 `<NavigationScopeTargeter>` 黑盒（实测：prefab 已声明 scope 但十字键无效果、无焦点视觉）。
> **关联**：[im-layer-and-input-design.md](im-layer-and-input-design.md)（Q4 手柄支持，本文件为其 §4.4/§4.7 的落地实现方案）

**v2 修订记录（2026-08-17 评审后）**：
1. **回落点规则**：离开频道列后（标题带 ↓ / 锚点卡 ←↑）的回落点 = **当前选中会话所在行 C_sel**（按 `_selected.Id` 定位），不再硬编码「第一个频道行 C3」——原设计在焦点 C7 时 ↑→↓ 会触发「移动即激活」把会话误切到第一个频道（Fix 1）。
2. **卡片按钮行焦点视觉**：CB 按钮的 `GetWidget` = 锚点卡气泡按钮行 ListPanel 按 CardButtons 顺序取子项，`SetState("Hovered")`——不再依赖「自身选中态」（整行高亮 ≠ 按钮级焦点，玩家看不见光标在哪个按钮上，违反「焦点必须可见」目标）（Fix 2）。
3. **手柄滚消息**：新增 LB/RB 翻页滚动（完整模式；复用 HandleManualScroll 的 scrollbar 语义）——原设计手柄玩家完全无法翻阅消息历史（Fix 3，待实机确认手感）。
4. **重建节流**：`_padNavDirty` 只在**结构变化**时置位（锚点卡引用变 / 按钮集数量变 / 模式切换 / 切会话 / 下拉开关），0.3s 轮询 RefreshMessages 不直接置 dirty——防 0.3s 一重建的焦点跳变与长按抖动（Fix 4）。

---

## 一、背景与裁定

1. **引擎 scope 实测失败**（2026-08-17 手柄实机）：两个 prefab 已加 `<NavigationScopeTargeter>` + `GamepadNavigationIndex`，但 IM 打开后**十字键无任何效果、无焦点视觉**。引擎导航激活是多环节黑盒（scope 注册 → 引擎输入分发 → 初始焦点），跨版本属性名差异（v1.2.12）风险未消。
2. **用户裁定**（2026-08-17）：「设计下每一个按钮 hover 状态时候，下一个应该自动移动到什么地方，然后用 A 来按下」——**手动导航**：显式定义每个元素的焦点转移 + A 激活动作。
3. **已有基础**：Mission 内手柄模态冻结（`SetPlayerControlFrozen`，冻结只冻角色 Agent 不冻 UI）；B 键收下拉/关闭已有；`UpdatePadFocus` 轮询点已在 Tick（改造为手动导航驱动）；手柄提示行/键帽已有；缩略下拉手动命中 SetState 先例（HandleCompactInput）。

---

## 二、导航模型

- **焦点项**：`PadItem { Id, Label, OnActivate(Action), GetWidget(Func<Widget>), 组标识 }` 列表 `_padItems`。
- **焦点索引** `_padIndex`：唯一当前焦点；`-1` = 无焦点（面板刚开/非手柄）。
- **重建**（`_padNavDirty` → `RebuildPadNavigation`）：**只在结构变化时置 dirty**——① 模式切换；② 切会话；③ 锚点卡结构变化（`UpdateCardAnchors` 的 `latestCard` 引用变更，或锚点卡 `CardButtons` 数量变更——缓存 `_padNavAnchorRef`（ImMessage 引用）+ 按钮数，每轮比对）；④ 下拉开/关。**0.3s 定时 RefreshMessages 不置 dirty**（不满足上述条件 = 结构未变，重建无意义且抖动焦点）。
- **移动**：十字键按下沿移动；按住 0.4s 后每 0.18s 重复（手感；每方向独立计时，抬起即复位）。
- **激活**：A（`ControllerRDown`）按下沿 → `_padItems[_padIndex].OnActivate()`。
- **滚动**：LB（`ControllerL1`）/ RB（`ControllerR1`）按下沿 + 长按重复 → 消息流上翻/下翻一页（`sb.ValueFloat = clamp(±0.4 × MaxValue)`，复用 HandleManualScroll 的 scrollbar 语义与贴底状态机：上翻解锁 `_pinnedToBottom=false`，下翻到底 `IsMessageAtBottom()` 重新锁定）。仅完整模式；缩略模式无消息流滚动 → LB/RB 无操作；下拉打开 / 输入框聚焦（软键盘）期间暂停。
- **视觉**：焦点项 `SetState("Hovered")`（复用 hover 视觉零新 Brush）；旧焦点按身份复位（`ButtonWidget.IsSelected ? "Selected" : "Default"`）。
  - 频道行（无独立 widget 引用）：焦点 = 移动即激活 → 选中态跟随，自身 IsSelected 视觉即焦点视觉，无额外处理。
  - **卡片按钮（Fix 2）**：`GetWidget` = 锚点卡气泡的按钮行 ListPanel 按 `CardButtons` 顺序取第 j 个子项 → `SetState("Hovered")`；旧按钮按身份复位。查找失败（按钮行未构建/不可见）→ 该项仍可 A 激活，视觉暂缺（瞬态，重建后补齐）。
  - 下拉项：`SetState("Hovered")`（手动命中逻辑已有同类 SetState 先例）。
- **回落点规则（Fix 1）**：完整模式离开频道列后的回落点 = **当前选中会话所在行 C_sel**（`_selected.Id` 在频道行列表中定位；不在列表的边缘情况 → 兜底 C3 第一个频道行）。回落命中当前行 = 激活无操作（同一会话），**不误切会话**。
- **初始焦点** = 索引 0（打开/模式切换后立即可见——解决「看不到焦点在哪」）。
- **门控**：仅 `ModInput.UsingGamepad`（设备切换 → 焦点复位，已有范式）；鼠标玩家不跑。

---

## 三、完整模式（ImChat.xml）转移矩阵

**可聚焦元素清单**（构建顺序 = 屏幕视觉顺序）：

| 焦点 ID | 元素 | 视觉 widget | A 激活 |
|---|---|---|---|
| C1 | 标题带「缩略」按钮 | `LWN_BtnCompact`（**prefab 新增 Id**） | `ToggleCompact()` |
| C2 | 标题带「关闭」按钮 | `LWN_BtnClose`（**prefab 新增 Id**） | `Close()` |
| C3..CN | 左栏频道行（动态，跳过分组标题） | 无（自身 IsSelected 视觉；移动即激活） | 移动即实时 `SelectConversation`（微信式预览） |
| CB1..CBK | 锚点卡按钮（动态，`_vm.Messages` 锚点卡 `CardButtons` 逐按钮） | **锚点卡气泡按钮行按 CardButtons 顺序取子项（Fix 2）** | 各 `ImButtonVM.Execute()`（批准/拒绝/自审/中止/制定计划…） |
| CM | 「有新消息」提示条（仅 `HasNewMessageHint` 可见时入列） | `LWN_BtnNewMsg`（**prefab 新增 Id**） | `ExecuteNewMessageClick()` |
| CT | 输入框 | `LWN_ImChat_Input`（Id 已有） | `EventManager.SetFocusedWidget`（弹软键盘） |
| CS | 发送按钮 | `LWN_BtnSend`（**prefab 新增 Id**） | `ExecuteSend()` |

**转移矩阵**（行 = 当前焦点，列 = 按方向后去向；「—」= 该方向无操作，保持当前；**C_sel = 当前选中会话所在行，兜底 C3**）：

| 当前焦点 | ↑（上） | ↓（下） | ←（左） | →（右） |
|---|---|---|---|---|
| **C1 缩略** | C2（标题带内上循环） | **C_sel**（选中行；无频道行 → CT） | — | C2 |
| **C2 关闭** | C1 | **C_sel**（选中行；无频道行 → CT） | C1 | — |
| **C3..CN 频道行（第 i 行）** | 上一行 C(i-1)；已是第一行 → C1 | 下一行 C(i+1)；已是最后一行 → 有锚点卡 ? CB1 : CT | — | 有锚点卡 ? CB1 : — |
| **CB1..CBK 锚点卡按钮（第 j 个）** | **C_sel**（选中行；无频道行 → C1） | CM（有新消息条）? CM : CT | **C_sel**（选中行；无频道行 → —） | 下一个卡按钮 CB(j+1)；最后一个 → CM（有新消息条）? CM : CT |
| **CM 新消息条** | 上一项（CBK 或 C_sel 或 C1） | CT | — | — |
| **CT 输入框** | 上一项（CM ? CM : CBK ? CBK : CN ? CN : C1） | CS | — | CS |
| **CS 发送** | CT | C1（主干底部循环回顶部） | CT | — |

**规则摘要**：
1. **垂直主干**：C1→C2→频道行[1..N]→锚点卡[1..K]→CM→CT→CS→(循环回 C1)——↑↓ 沿主干移动，分支点按矩阵。
2. **水平**：标题带 C1↔C2；输入区 CT↔CS；频道行→锚点卡（右）；锚点卡→频道行（左，回落选中行）。
3. **循环**：CS ↓ → C1（主干首尾循环）。
4. **回落点（Fix 1）**：标题带 ↓ / 锚点卡 ←↑ 一律回 **C_sel**（当前选中行）而非 C3——「移动即激活」下落到别的行 = 误切会话。C_sel 不在列表（边缘）→ C3 兜底；此时落 C3 会切会话，属边缘可接受（正常不可达）。
5. **动态收缩**：无频道行（空列表）→ C1↓直达 CT；无锚点卡 → 频道行↓直达 CM/CT；CM 不可见 → 从矩阵剔除（锚点卡↓直达 CT）。
6. **滚动**：LB/RB 翻页（见导航模型；与焦点移动互不干扰，焦点不因滚动变化）。

---

## 四、缩略模式（ImChatCompact.xml）转移矩阵

**可聚焦元素清单**（构建顺序 = 屏幕视觉顺序）：

| 焦点 ID | 元素 | 视觉 widget | A 激活 |
|---|---|---|---|
| K1 | ◀ 左箭头 | `LWN_BtnPrev`（**prefab 新增 Id**） | `SelectPreviousChannel()` |
| K2 | 中心按钮 | `LWN_BtnCenter`（**prefab 新增 Id**） | `ToggleChannelList()` |
| K3 | ▶ 右箭头 | `LWN_BtnNext`（**prefab 新增 Id**） | `SelectNextChannel()` |
| K4 | 放大按钮 | `LWN_BtnExpand`（**prefab 新增 Id**） | `ToggleExpand()` |
| K5 | 关闭按钮 | `LWN_BtnCloseC`（**prefab 新增 Id**） | `Close()` |
| KB1..KBK | 锚点卡按钮（动态，同完整模式） | **同完整模式（Fix 2）** | 各 `ImButtonVM.Execute()` |
| KT | 输入框 | `LWN_ImChat_CompactInput`（Id 已有） | `EventManager.SetFocusedWidget` |
| KS | 发送按钮 | `LWN_BtnSendC`（**prefab 新增 Id**） | `ExecuteSend()` |

**转移矩阵**：

| 当前焦点 | ↑（上） | ↓（下） | ←（左） | →（右） |
|---|---|---|---|---|
| **K1 ◀** | K2（标题行内循环） | 有锚点卡 ? KB1 : KT | K5（标题行横向循环尾） | K2 |
| **K2 中心** | K3 | 有锚点卡 ? KB1 : KT | K1 | K3 |
| **K3 ▶** | K4 | 有锚点卡 ? KB1 : KT | K2 | K4 |
| **K4 放大** | K5 | 有锚点卡 ? KB1 : KT | K3 | K5 |
| **K5 关闭** | K1（标题行循环） | 有锚点卡 ? KB1 : KT | K4 | — |
| **KB1..KBK 锚点卡按钮（第 j 个）** | K1（标题行首） | KT | — | 下一个卡按钮 KB(j+1)；最后一个 → KT |
| **KT 输入框** | 上一项（KBK ? KBK : K1） | KS | — | KS |
| **KS 发送** | KT | K1（底部循环回标题行） | KT | — |

**规则摘要**：
1. **标题行 = 横向组**：K1→K2→K3→K4→K5 单向环（→ 顺序、← 逆序、↑ 下一项、↓ 出组到锚点卡/输入框）。
2. **垂直主干**：标题行[K1..K5]→锚点卡[1..K]→KT→KS→(循环回 K1)。
3. **动态收缩**：无锚点卡 → 标题行↓直达 KT。
4. **KB ↑ 回 K1 可接受（与完整模式 C_sel 规则不冲突）**：缩略模式的标题行是环（落地不激活会话），无「回落误切」风险；完整模式的 C_sel 规则只为防「移动即激活」误切。
5. **无消息流滚动**：缩略面板固定行 A/B（锚点卡 + 最近两条），LB/RB 无操作。

**下拉打开时（特殊状态）**：
- 焦点接管为下拉项[1..M]（`LWN_ImChat_ChannelListInner` 子项）：
  | 当前 | ↑ | ↓ | ← | → | A | B |
  |---|---|---|---|---|---|---|
  | 下拉项 j | 上一项 j-1（首项 → 末项循环） | 下一项 j+1（末项 → 首项循环） | — | — | `ExecuteSelect`（选中 + 收起下拉，焦点回 K2） | `CloseChannelList()`（收下拉不关面板，焦点回 K2） |
- 视觉：项 `SetState("Hovered")`（手动命中逻辑已有同类 SetState 先例）。
- LB/RB 在下拉打开期间无操作（焦点已接管为下拉项）。

---

## 五、特殊状态与边界

| 状态 | 处理 |
|---|---|
| 软键盘弹出（KT 聚焦后） | 引擎接管输入；**导航轮询（移动/激活/滚动）暂停**（门控：`EventManager.FocusedWidget` 是输入框）；软键盘关闭后焦点回 KT（引擎回填链路——**🔴 待实机验证**，失败则降级预案：软键盘期间保持 `_padIndex` 于 KT，关闭后直接继续，不依赖引擎回填） |
| 设备切鼠标 | `UsingGamepad` 下降沿 → `_padIndex = -1` + 焦点视觉复位（已有范式） |
| 面板关闭/模式切换 | `_padIndex = -1` + `_padNavDirty = true`（下次打开重建） |
| 手柄 ↔ 鼠标同帧 | 按下沿检测用上一帧状态比较（`_lastPadUp` 等），无冲突 |
| 战斗/模态（`CanOpen` false） | 面板打不开，导航不激活 |
| 下拉打开中按 ↑↓ 之外 | 见下拉矩阵（◀▶ LB/RB 无操作） |
| 滚动与焦点共存 | LB/RB 翻页不改变焦点；滚动解锁/重锁贴底（与 HandleManualScroll 同语义） |

---

## 六、动态项构建规则（RebuildPadNavigation）

1. **频道行**（完整模式）：遍历 `_vm.ChannelList`，跳过 `IsGroupHeader`；每行一个焦点项，`OnActivate = SelectConversation(conv)`；**移动时即激活**（微信式：上下移动频道行 = 实时切换会话，A 无附加动作——已在当前行时 A 无操作）。**构建时记录 `_selected.Id` 对应的行索引 → C_sel 回落点**（Fix 1；`SelectConversation` 后置 dirty 重建，回落点恒最新）。
2. **锚点卡按钮**（两模式）：取 `_vm.Messages` 中 `IsCardAnchor` 的 VM → `CardButtons` 逐按钮 → 每个按钮一个焦点项，`OnActivate = () => ImButtonVM.Execute()`。
   - **视觉（Fix 2）**：`GetWidget` = 锚点卡气泡（`IsCardAnchor` 消息对应 bubble）内按钮行 ListPanel 按 `CardButtons` 顺序取第 j 个子项；查找失败 → 返回 null（该项可激活，视觉暂缺）。
   - **dirty（Fix 4）**：`_padNavAnchorRef`（锚点 ImMessage 引用）+ 按钮数缓存；`UpdateCardAnchors` 每轮比对，引用变或按钮数变 → 置 dirty。
3. **下拉项**（缩略）：`_vm.ChannelSelector.ItemList` 逐项（仅 `IsChannelListOpen` 时）。
4. **静态按钮**（C1/C2/CM/CS/K1..K5/KS）：prefab 加 Id 后 `FindWidgetById` 缓存 widget 引用（视觉 SetState 用）。

---

## 七、移动/激活/滚动输入轮询（UpdatePadFocus 重构）

```csharp
// Tick 内（UsingGamepad 门控；下拉打开 / 输入框聚焦（软键盘）时导航暂停，引擎接管）：
if (_padNavDirty) RebuildPadNavigation();
if (_padItems.Count == 0) return;
// 按下沿 + 长按重复（↑↓←→ / A / LB / RB；每方向独立 hold 计时，抬起复位）：
bool up = Input.IsKeyPressed(InputKey.ControllerLUp);
if (up && !_lastPadUp) { _padHoldUp = 0f; MovePad(-1); }            // 按下沿：立即移动一次 + 计时起点
else if (up && _padHoldUp > 0.4f) { _padRepeatUp += dt; if (_padRepeatUp > 0.18f) { _padRepeatUp = 0f; MovePad(-1); } }
else _padHoldUp = 0f;                                                // 抬起 → 复位
if (up) _padHoldUp += dt;
_lastPadUp = up;
// ↓←→ 同构（_padHoldDown/_padHoldLeft/_padHoldRight 各一组）；A：
bool a = Input.IsKeyPressed(InputKey.ControllerRDown);
if (a && !_lastPadA) ActivatePad();
_lastPadA = a;
// LB/RB 滚动（完整模式 + 下拉未开 + 输入框未聚焦；复用 HandleManualScroll scrollbar 语义）：
if (!_isDropdownOpen && !_isInputFocused)
{
    bool lb = Input.IsKeyPressed(InputKey.ControllerL1);
    if (lb && !_lastPadLB) ScrollPage(-1f);   // 上翻一页（解锁贴底）
    _lastPadLB = lb;
    bool rb = Input.IsKeyPressed(InputKey.ControllerR1);
    if (rb && !_lastPadRB) ScrollPage(1f);    // 下翻一页（到底重新锁定）
    _lastPadRB = rb;
    // 长按重复同 ↑↓ 模式（0.4s/0.18s），略
}
// 视觉：旧焦点复位（Fix 2：卡片按钮按按钮行子项身份复位）+ 新焦点 Hovered
```

- **MovePad(delta)**：按矩阵查表（当前焦点 → 目标焦点），越界钳制；频道行回落点按 C_sel。
- **ActivatePad()**：`_padItems[_padIndex].OnActivate?.Invoke()`；频道行焦点项 OnActivate = 已在当前会话 → 无操作。
- **ScrollPage(dir)**：`sb.ValueFloat = clamp(sb.ValueFloat + dir × 0.4 × MaxValue, 0, MaxValue)`；`dir<0` → `_pinnedToBottom=false`；`dir>0` 且 `IsMessageAtBottom()` → `_pinnedToBottom=true`。

---

## 八、改动文件清单

| # | 文件 | 改动 |
|---|---|---|
| 1 | `ImChat/ImChatView.cs` | `UpdatePadFocus` 重构为手动导航（`_padItems/_padIndex/_padNavDirty`/`RebuildPadNavigation`/`MovePad`/`ActivatePad`/`ScrollPage`/按下沿+长按重复）；`SelectConversation`/`SwitchMode`/`RefreshMessages` 结构变化才置 `_padNavDirty`（锚点引用/按钮数比对）；`Close` 清 `_padIndex` |
| 2 | `GUI/Prefabs/ImChat.xml` | 标题带缩略/关闭、新消息条、发送按钮加 Id（C1/C2/CM/CS 四个） |
| 3 | `GUI/Prefabs/ImChatCompact.xml` | ◀/中心/▶/放大/关闭/发送加 Id（K1..K5/KS 六个） |
| 4 | `ImChat/ImChatVM.cs` | （无改动——锚点卡按钮动作走既有 `ImButtonVM.Execute`） |

---

## 九、验证计划（手柄实机）

1. 完整模式打开 → 初始焦点 = C1（缩略按钮 Hovered 高亮可见）→ ↑↓ 沿主干移动、←→ 标题带/输入区水平切换 → 频道行移动实时切换会话 → 锚点卡按钮逐个聚焦（有卡时，**按钮级 Hovered 可见**）→ A 激活各按钮动作（批准/拒绝等）→ CS ↓ 循环回 C1。
2. **回落点（Fix 1）**：焦点到第 5+ 个频道行 → ↑ 到标题带 → ↓ 回**同一行**（会话未变）→ → 到锚点卡 → ← 回**同一行**（会话未变）。
3. 缩略模式打开 → 初始焦点 K1 → 标题行 ←→ 单向环、↓ 出组到锚点卡/输入框 → 中心 A 开下拉 → 下拉项 ↑↓ 循环 + A 选中收起 + B 收下拉不关面板 → 发送/放大/关闭激活。
4. 无锚点卡 / 无频道行 / 有新消息条 三种动态收缩场景的转移正确（矩阵分支）。
5. **滚动（Fix 3）**：LB 上翻解锁贴底（新消息不再自动弹出）、RB 下翻到底重新锁定；滚动不改变焦点；下拉打开/输入框聚焦时 LB/RB 不误滚。
6. **重建节流（Fix 4）**：消息流 0.3s 轮询刷新期间焦点不跳变；锚点卡按钮集变化（批准后按钮消失）→ 焦点钳制到剩余项且不残留高亮。
7. 按住 ↑ 0.4s 后 0.18s 重复移动；快速连点不跳格。**频道行长按重复切会话的手感/性能**（每 0.18s 一次 RefreshAll——卡顿则切备选：长按期间只移焦点不切会话，松开落地）。
8. 设备切鼠标 → 焦点复位无残留高亮；切回手柄 → 焦点重建。
9. **软键盘（🔴 待实机验证）**：聚焦输入框 → 引擎弹键盘 → 输入回填 → A 发送；软键盘期间导航不抢键；关闭后焦点留在 KT（失败 → 降级预案：不依赖引擎回填，轮询自行保持 KT 焦点）。
10. 回归：鼠标操作、B 关闭、ESC 关闭、缩略模式位置感知 mask、Mission 冻结不回归。

---

## 十、风险与取舍

| 风险 | 影响 | 对策 |
|---|---|---|
| 手动导航与引擎 scope 并存（prefab 已声明） | 引擎若部分生效可能抢焦点 | 观察实机；冲突则删除 prefab 的 NavigationScopeTargeter |
| 动态项重建时机漏点（消息异步到达） | 锚点卡按钮列表过期 | `_padNavDirty` 置位点覆盖全部入口（含 `NotifyMessageShapeChanged`）——**但只结构变化置位（Fix 4），非每帧/每轮询** |
| 长按重复手感不适 | 移动过快/过慢 | 阈值可调常量（0.4s/0.18s） |
| 频道行移动即切换 = 误触切换 | 玩家只是想看焦点 | 保留（微信式预览用户裁定方向）；若不适改「A 才切换」。**长按重复 5.5 次/s 全量重建的性能风险**：实机验证第 7 步，卡则长按只移焦点、松开落地 |
| 卡片按钮视觉查找失败（按钮行未构建/时序） | 焦点按钮无高亮 | 返回 null 视觉暂缺，下次重建补齐；不影响 A 激活 |
| 软键盘回填链路未验证 | 焦点回 KT 失效 | 降级预案：不依赖引擎回填，轮询保持 KT 焦点（见验证第 9 步） |
| LB/RB 翻页手感 | 翻页幅度不合适 | 页幅常量（0.4×MaxValue）可调；贴底状态机复用既有闭环 |
