# IM 层挂载 / 密信入口 / 缩略输入 / 手柄支持 / 呼出按钮 — 设计修订

> **状态**：✅ **设计定稿**（2026-08-17，含多轮反编译验证 + 用户裁定 + 完善性检查；剩 6 项实机小验证，见验证计划）
> **主题**：五件事——① IM 放大/缩略版挂载层核查 ② 密信按钮「关屏再开」 ③ 缩略模式鼠标屏蔽根因与位置感知 mask ④ 手柄玩家操作 IM 的完整映射（十字键导航/焦点视觉/软键盘打字）⑤ 常驻呼出按钮（三入口等价）
> **关联**：[im-chat-system.md](im-chat-system.md)（IM 主文档）、[im-compact-mode.md](im-compact-mode.md)（缩略模式）、[im-secret-letter-button.md](im-secret-letter-button.md)（密信按钮）、[rules/pitfalls.md](rules/pitfalls.md)（2026-08-11 拦鼠标实机记录）、[rules/wheels.d/input.md](rules/wheels.d/input.md)（玩法行输入模型）

---

## 背景与需求（用户四问，2026-08-17）

1. IMCHAT 放大版/缩略版目前调用的地方是否挂在全局层（而非家族/队伍 UI 层）？
2. 家族/队伍 UI 点密信后，能否关掉当前 UI，在 campaign/mission 界面才打开 IM 放大或缩略？
3. 缩略模式下如何处理对鼠标操作的屏蔽——目前缩小后鼠标仍无法控制攻击和转向，给推荐方案。
4. 手柄玩家如何操作 IM 的放大/缩小版本，考虑与原版键位冲突。

---

## 一、挂载层核查结论（Q1：✅ 已核实，全局层）

**结论：IM 两种形态共享同一个全局层，不挂在家族/队伍 UI 的 widget 树里。**

| 事实 | 证据 |
|---|---|
| 两种形态共享同一 layer | `Open()` → `V.NewLayer(400, "ImChatLayer")` + `ScreenManager.TopScreen.AddLayer(_layer)`；形态差异仅在 `LoadMovie` 的 prefab（`ImChat` / `ImChatCompact`），`SwitchMode()` 换 prefab 重载（ImChatView.cs:220-242） |
| 层序 400 | 高于全部地图玩法 UI（定居点菜单 202 / 百科 310），低于系统菜单 4400 |
| 打开挂/关闭摘 | `IsOpen => _layer != null`，非常驻 |
| 层跟随 TopScreen | 从哪个屏打开就叠在哪个屏上——家族/队伍屏点密信 = IM 层叠在 `ClanScreen`/`PartyScreen` 之上 |

**🔴 顺带发现的隐患**：2026-08-17 的层归属迁移崩溃修复（关家族屏 → PopScreen 销毁层 → C# 引用指向已释放 native → NRE）**只写在 `HandleCompactInput`（缩略分支）**（ImChatView.cs:975-999）；**完整模式没有此保护**——关屏后滚动缓存（`_messageScrollPanel`）指向已释放树，访问被 try/catch 吞掉 → 滚动静默失效。若 Q2 采用「关屏再开」，此隐患在密信入口路径上整体消失；若保留层叠，需把迁移逻辑提升到 `Tick()` 顶层。

---

## 二、密信入口「关屏再开」（Q2：✅ 可行，推荐方案 A'）

### 现状与黑屏教训

`OpenSecretLetter` 现为**层叠方案**（IM 400 层叠在 Party/ClanScreen 上，不关屏）。2026-08-17 曾试「`PopScreen()` + 延迟 0.1s 再开」，实测黑屏——`PopScreen` 后底层屏激活是帧边界异步的，过渡期内 `TopScreen` 存在但层未渲染，IM 层叠在空屏上（SecretLetterButtonInjector.cs:344-349 注释）。

### 推荐方案 A'：关屏 + 下一帧延迟打开

1. `OnClicked`：队伍屏有未应用变更 → 先走原版 `Apply Changes?` Inquiry 流程（复用 `PartyVM.ExecuteTalk` 语义，im-secret-letter-button.md §2.6 方案 A 描述）；`ClanScreen` 无未保存变更概念，直接关。
   - 🔴 **实现依赖标注**：现有注入按钮是纯 C# 动态插入，「触发原版 Apply Changes Inquiry」需要反射调 `PartyVM` 的既有方法（实现时反编译 `PartyVM.ExecuteOpenConversation` / `DoneLogic` 确认调用链）；**反射失败 → 降级为直接 PopScreen + `DebugLogger` 日志**（不阻塞，但未保存变更会丢——风险表记录）。
2. `ScreenManager.PopScreen()`。
3. 目标 Hero 存入静态 `_pendingSecretLetterHeroId`（不立即 Open）；**连点多个密信按钮 → 覆盖式（后点者胜）**。
4. 既有钩子 `ImChatView.OnScreenFrameTick`（`ScreenBase.OnFrameTick` Postfix，每帧跑）加分支：pending 非空 && 当前 TopScreen 已稳定 && `CanOpen()` → `Open(conv)` + 清空 pending。
   - 🔴 **失败路径**：`CanOpen()` false（过渡帧异常/模态残留）→ pending 保留，**超时 ~2s 未成功则丢弃 + 日志**（防永久卡住）；`Close()` 时一并清 pending。

**为什么下一帧就行**：下一帧 `PopScreen` 已完成、`TopScreen` 已是 `MapScreen`（队伍/家族屏只能从大地图打开，Mission 内不存在这两个屏 → 本方案只涉及大地图场景），层挂上去即渲染，天然避开过渡竞态。心智与探查板「传信」（关面板 → 回地图 → IM 打开）一致（设计哲学：反馈明确、不打断流程）。

### 备选 B'：保留层叠 + 迁移提升

把层归属迁移从 `HandleCompactInput` 提升到 `Tick()` 顶层（完整模式也受益）。改动小，但屏幕没关、IM 仍叠在原屏上（视野遮挡、ESC 语义不干净）——仅在用户想保留「边看面板边操作队伍」时选用。

---

## 三、缩略模式鼠标屏蔽：根因与推荐方案（Q3：🔴 设计假设漏洞实锤）

### 根因（双证据，非猜测）

- **项目实机记录**（pitfalls.md:8-34，2026-08-11）：`SetInputRestrictions(true, InputUsageMask.Mouse)` 的层存在 → **左键攻击/右键格挡/滚轮全失效、移动（WASD）正常**。「任何 Gauntlet 层只要含 Mouse 拦截，在战斗场景都是攻击/格挡杀手」。
- **input.md:39-56**：键盘物理轮询（`Input.IsKeyDown`）拦不住（mod 才需要手动门控玩法行）；反编译确认 `Input.IsKeyPressed` 直通 native `InputManager.IsKeyPressed`——鼠标键在 native 层有「UI 捕获」判定，**与鼠标位置无关**。

**结论**：im-compact-mode.md 的关键假设「hit-test 门控 → 面板矩形外场景输入天然不被吞」**只对 UI 事件分发（EventManager）成立，对原始轮询不成立**。缩略层 mask 常驻 `MouseButtons|Keyboardkeys`（ImChatView.cs:154-157）→ 位置无关地全局拦鼠标 → 攻击死；鼠标被 UI 捕获 → 视角（转向）也死。**引擎没有区域化输入限制 API，这是绕不开的事实。**

### 推荐方案：位置感知 mask（约 10 行，替换 HandleCompactInput 现有 mask 计算）

```csharp
// HandleCompactInput 内（复用 _lastCompactMask 缓存模式，只在变化时 SetInputRestrictions）
bool overUi = _layer != null && _layer.HitTest();   // GauntletLayer.HitTest() 公共 API，本文件诊断日志已在用
InputUsageMask mask = InputUsageMask.Keyboardkeys;  // 打字常驻（键盘拦不住物理轮询，留着不影响 WASD）
if (overUi) mask |= InputUsageMask.MouseButtons;    // 鼠标在面板/下拉上 → 才拦（点按钮不挥刀）
if (_vm != null && _vm.ChannelSelector.IsChannelListOpen) mask |= InputUsageMask.MouseWheels;  // 现状已有
if (mask != _lastCompactMask) { _lastCompactMask = mask; _layer.InputRestrictions.SetInputRestrictions(true, mask); }
```

- 鼠标在面板矩形内 → mask 含 Mouse → 按钮点击被层消费、该帧不攻击（维持现状可用行为）；
- 鼠标移出面板 → mask 摘 Mouse → 攻击/格挡/视角全部还给游戏；
- `_layer.HitTest()` 按 widget 命中，天然覆盖浮出面板的频道下拉；
- 边界：点击与「鼠标进入面板」同帧的 1 帧竞态 → 该帧按面板外语义处理（挥一刀），无害；要更稳可加 150ms 缓滞，非必需。
- 🔴 **MouseWheels 一致性**：现状下拉开时 MouseWheels **位置无关**地进 mask（面板外滚轮也不能缩放镜头）——下拉开时间短，**接受现状**（实现简单）；若实机不适，可并入 overUi 判定（同一位置感知逻辑）。

### 🔴 同根因隐患：密信通知圆环

`ImSecretNotifyManager` 也是 `mask = Mouse`（ImSecretNotifyManager.cs:48-50）。im-compact-mode.md 的 P0-1「未验证假设」现可**判定为假**（pitfalls 记录 + 用户实机反馈双证）——Mission 内弹密信通知期间同样拦攻击。建议同一方案处理：圆环矩形内才拦（圆环小，效果≈现状）。

---

## 四、手柄支持（Q4：完整操作映射设计）

> **核心理念：手柄玩家不需要为每个功能配一个键。** 面板打开后进入「UI 导航模式」——摇杆/十字键导航 + A 确认 + B 取消，与原版背包/部队/对话面板的手柄操作完全同构，引擎原生支持。**只有「呼出」这一个动作需要一个新键。**
>
> **反编译地基（v1.4.8 实锤）**：`ScreenBase.GetActiveMouseKeys()` 把 `ControllerRDown`（A/✕）作确认键**映射为鼠标左键点击**、`ControllerRRight`（B/○）为取消（`IsEnterButtonRDown` 由 native 引擎按平台判定）——手柄 A 按下 = 聚焦按钮被点击，**Command.Click 自动触发，零额外代码**。GauntletLayer 的 `GetIsAvailableForGamepadNavigation`（`LastActiveState && IsActive`）对自定义层天然成立。

### 现状核对

- IM 玩法行 = `键盘 O`，**手柄不占键**（Settings.cs:200：`Gamepad = ""`，注释「手柄不占键，走通知点击」）；
- 关闭 = ESC / 手柄 B（`ControllerRRight`）已有（ImChatView.cs:929-933）；
- 放大/缩小/频道切换 = 鼠标点击（ButtonWidget）；
- 缩略频道切换另有 ◀中心▶ 按钮 + 下拉。

### 4.1 键位映射总表

| 动作 | 键 | 机制 |
|---|---|---|
| **呼出 IM**（面板外，唯一新键） | **↑ 十字短按**（`ControllerLUp`，config 可改） | `InteractionIds.IM` 玩法行；短按 = 打开上次形态（`_mode` 记忆） |
| 面板内导航 | **十字键**（↑↓←→） | 🔴 **引擎默认就是十字键**（用户裁定 2026-08-17：禁止摇杆移焦点）：`GetMovementForInput` 只认 `ControllerLUp/Down/Left/Right`（反编译实锤），摇杆完全不参与导航——**零配置** |
| **确认/点击**（任意按钮：放大/缩小/频道/计划卡/发送…） | **A**（`ControllerRDown`） | 引擎映射为鼠标左键 → `Command.Click` 自动触发 |
| **关闭** | **B**（`ControllerRRight`） | 已有监听（Tick 顶层） |
| 缩略下拉打开中 | **B** = 先收下拉，再按才关面板 | 新增小逻辑（`IsChannelListOpen` 时 B → `CloseChannelList()`） |
| 发送文本 | 🔴 **手柄可行（2026-08-17 翻案）**：手柄聚焦输入框 → 引擎自动弹平台软键盘（`ITwoDimensionPlatform.OpenOnScreenKeyboard`，public 接口，反编译实锤）→ 打完 `OnOnScreenkeyboardTextInputDone` 回填 → A 确认发送。软键盘体验一般但链路完整，发送按钮不再恒置灰 |

### 4.2 短按/长按的利用（结论：只用短按）

- **↑ 十字短按 = 呼出**；**长按不占用**——↑ 十字长按 = 原版 CheerBark 表情菜单（按住弹出），原版物理轮询无法拦截，长按必双触发 → 放弃长按方案。
- 面板内无长按需求：导航 + A/B 已覆盖全部操作（原版 UI 同款心智，长按在 UI 导航模式下手柄误触率高，不引入）。
- 形态切换（缩略⇄完整）**不设专用键**：导航到标题行「缩略/放大」按钮 + A（缩略模式标题行右侧已有放大/关闭按钮，加 `GamepadNavigationIndex` 即可）。

### 4.3 缩略模式场景走查（「点击缩略之后」）

| 操作 | 走查 |
|---|---|
| 切换频道 | 导航到标题行 ◀/中心/▶ → A：◀▶ = 上一/下一频道（`SelectPreviousChannel`/`SelectNextChannel` 已有）；中心 = 开下拉 → 导航列表项 → A 选中（`ExecuteSelect` 已有）→ B 收下拉 |
| 发送 | 手柄无法打字 → 发送按钮置灰不可确认（`CanSend` 已有）；**手柄玩家在 IM 的主要操作 = 读消息 + 计划卡/决策卡按钮（导航 + A 确认）** |
| 关闭 | B（已有） |
| 放大 | 导航到标题行「放大」按钮 + A（`ExecuteExpand` 已有） |

### 4.4 视觉提示（手柄操作教学，设备感知）

- **面板内提示行**：`ImChatVM` 加 `PadHintText`/`HasPadHint`——仅 `ModInput.UsingGamepad` 时非空。文案 = `A 确认 · B 关闭 · 摇杆导航`（Xbox）/ `✕ 确认 · ○ 关闭 · 摇杆导航`（PS，`ModInput.IsPlayStation`）。字形不写死：`ModInput` 暴露 `GlyphForKey(InputKey)`（内部 `GlyphsFor` 已有，只需 public 化），按最近设备动态取字形。**位置统一放输入区上方 12px 灰字**（完整模式五轮修复后 TypingText 已移入标题带，输入区上方空闲；标题带空间不足——正在思考/模式状态/关闭已挤满）。
- **首次打开引导**（已有 `LWN_im_first_open` DisplayMessage）：手柄设备时补一句「手柄：↑ 十字呼出 · 十字键操作 · 聚焦输入框可弹软键盘打字」。
- 按钮本身：导航聚焦高亮由引擎自动（focus 状态）；发送按钮置灰 = 不可用已视觉传达。

### 4.4b 🔴 焦点视觉（用户裁定 2026-08-17：焦点必须可见，否则玩家看不到光标在哪）

**引擎不自动**（反编译实锤：`ButtonWidget.RefreshState` 只处理 Disabled/Selected/Pressed/Hovered/Default，**无 Focused 分支**；`OnGamepadNavigationFocusGained` 回调存在但无 Lost 回调）。

**推荐实现（最简，覆盖动态项）**：
```csharp
// ImChatView.Tick 内（手柄设备才跑，与 PadHint 同门控）：
var padFocus = GauntletGamepadNavigationManager.Instance?.LastTargetedWidget;  // public 实锤
if (padFocus != _lastPadFocus)
{
    if (_lastPadFocus != null && _lastPadFocus != padFocus)
        _lastPadFocus.SetState(IsSelectedOf(_lastPadFocus) ? "Selected" : "Default");  // 按身份复位（选中行回 Selected）
    _lastPadFocus = padFocus;
    if (padFocus != null && IsOurPanelWidget(padFocus))   // 🔴 只高亮自己面板的按钮
        padFocus.SetState("Hovered");                     // 复用 hover 视觉（零新 Brush）
    else _lastPadFocus = null;                            // 鼠标移出面板/引擎返回 null → 复位即可，不高亮外部 widget
}
```

🔴 **手柄→鼠标切换的行为（反编译实锤，2026-08-17）**：
- **鼠标 hover 由引擎自己管**：`IsHovered` 属性变化 → `RefreshState` → 按钮显示 hover——鼠标移到任何按钮上必然刷新，与我们手动 SetState 无关；
- **引擎导航焦点跟随鼠标**：鼠标在 scope 区域内移动，引擎自动把导航焦点跳到鼠标附近按钮（`HandleInput` 的 `IsPointInsideGamepadCursorArea(MousePosition)` 判定）→ `LastTargetedWidget` 随之变化 → 我们的轮询自动复位旧/高亮新——**残留自动清理**；
- **鼠标移动时十字键导航让位**：`SetCurrentNavigatedWidget` 带 `Input.MouseMoveX/Y == 0` 守卫——鼠标在动时十字键不抢焦点，不打架；
- **🔴 唯一残留缝隙 = 设备切换瞬间**：动鼠标 → `UsingGamepad` 立即 false → 轮询停止，最后的高亮可能残留。处置：**设备切换检测（下降沿）里执行一次焦点视觉清理**（复位 `_lastPadFocus`）；
- **🔴 外部 widget 守卫**：`LastTargetedWidget` 是全局活跃 scope 的聚焦项——鼠标移到面板外的原版 UI 时它可能指向原版按钮，**必须 `IsOurPanelWidget`（向上找 `LWN_ImChat` 根）守卫**，否则会对原版按钮 SetState 造成视觉错乱；
- **null 语义**：鼠标不在聚焦按钮范围内且无导航动画时返回 null（反编译实锤 getter）——轮询把 null 当「焦点丢失」处理（复位）。
- 触屏鼠标场景：鼠标 hover 时 LastTargetedWidget 可能同步变化（引擎按鼠标位置找最近 scope）——用 `ModInput.UsingGamepad` 门控，键盘/鼠标玩家不跑此逻辑。
- 关闭面板时 `_lastPadFocus = null`（widget 树销毁）。

### 4.5 Mission 内与角色操作不冲突（🔴 核心机制）

**手柄键没有「区域」概念（无鼠标位置），面板内/外无法按位置分流**——且原版 UI 确认键 A = Jump、RT = Attack 与面板按钮全重叠，混合必然冲突。因此：

| 设备/场景 | 面板打开时输入归属 | 机制 |
|---|---|---|
| **手柄 · Mission**（`ModInput.UsingGamepad`） | **模态**：角色输入整体冻结，全部手柄键归 UI | `V.SetPlayerControlFrozen(MainAgent, true)`（版本兼容层已有轮子）——**不依赖 InputRestrictions 拦手柄键**（键盘已证拦不住，冻结是确定性方案）；摇杆冻结后无角色效果，导航走十字键 |
| **手柄 · 大地图** | **十字键分流**：左摇杆 = 地图移动（原版照常），**十字键 = 面板导航** | 🔴 用户裁定 + 引擎默认（导航只认十字键，反编译实锤）——**完美分流，无模态需求，大地图缺口消灭**；A 确认在大地图无原版绑定（MapHotKeyCategory 实锤），B 无角色副作用 |
| **键盘玩家** | 完整 = 模态；缩略 = 半模态岛 | Q3 位置感知 mask（键盘有鼠标位置可用） |

- **冻结/解冻时机**：`Open()` 成功后冻结（仅 `UsingGamepad && Mission.Current != null`）；`Close()` 解冻；`OnMissionScreenFinalize` 兜底（Close 路径已有，Mission 退出自动恢复）。
- **设备切换**：Tick 检测 `UsingGamepad` 变化 → 手柄→鼠标：解冻 + 走位置感知 mask；鼠标→手柄：冻结。与 input.md「设备检测范式」（缓存逐帧对比）一致。
- **玩法行**：`ModInput.Tick` 门控已含 `ImChatView.IsOpen`（面板内手柄不会误触发探查/击晕等）。
- **战斗**：`CanOpen()` 已禁战场模式（`IsInteractionDisabled`），战斗中 ↑ 十字不呼出。

### 4.5b 🔴 大地图手柄模态缺口 —— ✅ 已被「十字键分流」消灭（2026-08-17 用户裁定）

原缺口：`SetPlayerControlFrozen` 是 Mission/Agent 层 API，大地图无 Agent；MapScreen 层叠照常 tick，若摇杆同时驱动地图移动 + UI 导航则冲突。

**现方案**：用户裁定「面板打开只允许十字键移动焦点」+ 引擎默认（导航方向只认 `ControllerLUp/Down/Left/Right`，反编译实锤）→ 大地图手柄 = **左摇杆移动地图（原版照常）+ 十字键操作面板，天然分流，无需任何模态机制**。分水岭验证不再决定大地图方案。

**剩余分水岭影响面（缩小为一项）**：手柄 B 关闭——`Input.IsKeyPressed(ControllerRRight)` 物理轮询，若手柄键被 InputRestrictions 拦（像鼠标）则收不到。处置：
- 拦不住 → 现状代码直接工作；
- 被拦 → 兜底 = 层内取消事件（EventManager 导航取消键，实机验证）或接受 ESC/按钮关闭（手柄玩家可接受的降级，标注）。

### 反编译冲突表（v1.4.8 实锤：`CombatHotKeyCategory` / `MapHotKeyCategory` / `GenericGameKeyContext`）——**没有空闲键**

| 手柄键 | 原版占用（Combat/Mission） | 原版占用（Campaign 地图） |
|---|---|---|
| RT / LT | Attack / Defend | 地图缩放 |
| X | Kick | 时间暂停 |
| Y | Action（F 对话） | — |
| A | Jump | — |
| B | —（Leave 通用返回） | 返回 |
| **↑ 十字** | **Cheer（口哨/表情菜单）** | **无绑定** |
| ↓ 十字 | Crouch | — |
| ← 十字 | ViewCharacter | — |
| → 十字 | PushToTalk | — |
| R3 | LockTarget / ToggleZoom / 丢武器 | **追踪定居点** |
| L3 | CameraToggle | 镜头跟随 |
| LB / RB | ShowIndicators / EquipmentSwitch | — / 快进 |

### 4.6 呼出键选型与冲突分析（唯一新键）

**↑ 十字（`ControllerLUp`）**，config.json 可改（玩法行体系天然支持改绑）：

| 候选 | Mission 冲突（原版） | Campaign 地图冲突 | 结论 |
|---|---|---|---|
| **↑ 十字** | 仅 Cheer（口哨，单机几乎不用） | **无绑定** | ✅ 冲突最小，代价 = 短按吹一声口哨（无法拦截，接受） |
| R3 | LockTarget / ToggleZoom / 丢武器（常用） | TrackSettlement 追踪定居点（常用） | ❌ 不推荐 |
| LB 短按 | ShowIndicators + mod LB 长按已挂 3 行 | 指示物 | ❌ 混用风险 |
| Y | Action 对话（最常用） | — | ❌ 误触风险高 |

### 实施验证点

1. **导航激活（机制已解，见 §4.7）**：IM prefab 加 `<NavigationScopeTargeter>`（ScopeParent = 面板根 / IsDefaultNavigationScope = true）→ 实机验证初始焦点、**十字键移动（摇杆不移动焦点，用户裁定）**、A 确认；跨版本属性名（v1.2.12）实机确认；
2. 注入密信按钮（原版屏内、NavigationScope 外）手柄聚焦——失败则保持探查板传信（L3 可达）为手柄入口；
3. `V.SetPlayerControlFrozen` 冻结后手柄 UI 导航仍可用（冻结只冻角色 Agent，不冻 UI 层输入——实机确认）；
4. ↑ 十字短按的 Cheer 口哨双触发实机确认（可接受即保留，不可接受再议 R3）；
5. **手柄键是否被层 InputRestrictions 拦截**（4.5b：只影响 B 关闭实现）——Mission 面板内 B 关闭实测；被拦 → 层内取消事件兜底；
6. ListPanel ItemTemplate 项（完整模式左栏频道行/计划卡按钮）导航——scope 自动收集 `GamepadNavigationIndex != -1` 的子项（反编译实锤 `CollectNavigatableChildrenOfWidget`），ItemTemplate 项需显式设索引或依赖引擎 sibling fallback，实机确认；
7. **焦点视觉（4.4b）**：十字键移动 → 聚焦按钮 Hovered 高亮跟随、旧焦点复位；动态生成的 ItemTemplate 项聚焦高亮正常；鼠标/键盘模式不跑此逻辑；
8. **手柄打字（翻案）**：手柄聚焦输入框 → 平台软键盘弹出 → 输入回填 → A 确认发送 → 消息发出 + NPC 回复（软键盘体验与回填链路实测）。

### 4.7 🔴 导航激活机制（反编译实锤，2026-08-17）：`<NavigationScopeTargeter>` 就是答案

**结论：自定义层手柄导航「自动激活」是伪命题——引擎不会为没有 scope 的层建导航图。正确做法 = prefab 里声明 scope，机制与代码路径已全部确认：**

| 环节 | 反编译证据 |
|---|---|
| 导航体系 = 全局单例 `GauntletGamepadNavigationManager`（跨 UIContext，监听 `Input.OnGamepadActiveStateChanged`——手柄插拔自动刷新） | GauntletUI.dll |
| 导航基本单元 = `GamepadNavigationScope`：`ParentWidget` 锚定 + **自动收集子树内 `GamepadNavigationIndex != -1` 的子 widget**（`CollectNavigatableChildrenOfWidget` 实锤） | GamepadNavigationScope.cs |
| scope 注册 = `Context.GamepadNavigation.AddNavigationScope(scope, true)` → 写进 `NavigationScopeParents` 字典（按 ParentWidget 索引） | GauntletGamepadNavigationManager.cs |
| **prefab 声明方式 = `<NavigationScopeTargeter>` 元素**（Widget 子类，`TaleWorlds.MountAndBlade.GauntletUI.Widgets` 命名空间，游戏自带 DLL——**prefab 系统按名字反射实例化，零 C# 引用改动**）。原版先例：EscapeMenu.xml:13 `<NavigationScopeTargeter ScopeID="..." ScopeParent="..\EscapeMenu" ScopeMovements="Vertical" HasCircularMovement="true" IsDefaultNavigationScope="true"/>` | EscapeMenu.xml 实锤 |
| 属性：`ScopeID` / `ScopeParent`（相对路径）/ `ScopeMovements`（Vertical/Horizontal）/ `AlternateScopeMovements` / `HasCircularMovement` / `IsDefaultNavigationScope`（**初始焦点自动进入** = 「激活」的真相）/ `IsScopeEnabled`（绑定开关） | NavigationScopeTargeter.cs |

**落地（改动清单 XML 行补充）**：`ImChat.xml` 面板根容器下加 `<NavigationScopeTargeter ScopeID="LWN_ImChat_Scope" ScopeParent="..\面板根" ScopeMovements="Vertical" IsDefaultNavigationScope="true"/>`；缩略模式 `ImChatCompact.xml` 同理（ScopeParent = CompactPanel 根；频道三件套是横向按钮组 → 可加 `AlternateScopeMovements="Horizontal"` 或拆两个 scope）。按钮已有 `GamepadNavigationIndex` 计划 → scope 自动收编。

---

## 五、常驻呼出按钮（右侧 UI 按钮，三入口等价）

> **需求（用户 2026-08-17 追加）**：界面右侧一个常驻 UI 按钮，点击呼出聊天（前提 `Settings.Instance.PlotEnabled` 打开）；有新消息时按钮右上角显示数字并跳动；**键盘 O / 鼠标点击按钮 / 手柄 ↑ 十字 = 三个等价入口**；层级 campaign + mission 都要有，**不超过各种选项界面的层次**。

### 5.1 层序与挂载

| 项 | 值 | 依据 |
|---|---|---|
| 层序 | **350** | > 310（百科/地图玩法 UI，按钮必须可见可点）、< 400（IM 面板——IM 打开时全屏遮罩自然盖住按钮 + 事件被拦，**零额外隐藏逻辑**）、< 4400（系统菜单/选项界面——满足「不超过选项界面层次」） |
| **输入限制** | 🔴 **零 mask（不调 `SetInputRestrictions`）** | 按钮层是**常驻层**，任何输入拦截 = 常驻拦输入 = pitfalls 灾难（「任何 Gauntlet 层只要含 Mouse 拦截，在战斗场景都是攻击/格挡杀手」）；按钮点击靠引擎 hit-test 门控（层在鼠标位于层内 `DoNotAcceptEvents=false` 的 widget 矩形时获得鼠标输入），不需要 mask |
| 按钮位置 | 右缘**下部**（`VerticalAlignment="Bottom"` + MarginBottom ~140），56×56 | 🔴 **避让通知圆环**：密信通知（ImSecretNotify.xml:9 实锤：右缘垂直居中 + MarginRight 20，130×130）与 NinjaReport 金环都占右缘中部——按钮放下部避免重叠/遮挡 |
| Mission | `ImChatMissionView.OnMissionTick` 驱动 | 既有驱动点（im-chat-system.md 架构） |
| Campaign | `ImChatView.OnScreenFrameTick` 驱动 | 既有每帧钩子（ScreenBase.OnFrameTick Postfix，暂停照常） |
| 层归属迁移 | 照抄 `HandleCompactInput` 的 `_layerOwnerScreen` 迁移模式（TopScreen 切换 → 摘旧屏挂新屏；旧屏已销毁 → Close） | 2026-08-17 崩溃修复同款 |
| 显示条件 | `PlotEnabled && !ImChatView.IsOpen && !战斗模式(Mission)` | 与 O 键行为一致（战斗/开关关闭时按钮隐藏，点了也开不了） |
| 系统菜单打开时 | 350 < 4400 被盖住 + 事件被拦，无需额外处理 | 层序红利 |

### 5.2 按钮本体与未读徽标

- **样式**：复用 `Brush_CircleButton_SecretLetter`（密信通知圆环同款笔刷，`notification_illustration_conspiracy_quest` 图标）——同一系统的视觉语言；右缘下部（`VerticalAlignment="Bottom"` + MarginBottom ~140，避让右缘中部的通知圆环带），56×56 图标按钮。
- **未读徽标**：按钮右上角小圆底（ImageWidget）+ 数字（TextWidget）。口径 = **总未读**（所有会话之和）：`ImChatStore` 新增 `GetTotalUnread()`（遍历 party/clan/kingdom 三固定频道 + `_directIndex` 私聊的未读求和）。**数字 0 → 徽标隐藏**；**>99 → 显示「99+」**（小徽标宽度）。
- **刷新**：`ImChatManager.MessageArrived` 事件即时刷（数字变化即触发跳动）+ Tick 0.3s 轮询兜底（打开面板/查看会话后 ClearUnread → 数字回落，与左栏徽标同口径）。
- **跳动动画**：C# 定时脉冲（无引擎动画依赖，可控可停）：新消息到达 → `_bounceTotal = 3s`；Tick 内按 0.35s/脉冲播放「上跳 6px 回落 + Alpha 0.7→1」正弦脉冲（`badge.PositionYOffset` + `SetGlobalAlphaRecursively`）。跳完归位。
- **hover 提示**：手动 hit-test + `MBInformationManager.ShowHint`（项目先例），文案 `LWN_im_open_button_hint` = "Open messaging (key: O)"（本地化，铁律 13）。
- **点击**：`ImChatView.Open()`——与 O 键完全等价（Open 内部已查 `IsOpen`/`CanOpen`/PlotEnabled，缩略模式开着时点击 = Open 返回 false 无副作用）。

### 5.3 三入口等价（一致性与冲突）

| 入口 | 触发 | 行为 |
|---|---|---|
| 键盘 | `InteractionIds.IM`（O，config 可改）→ `ShortFired && !IsOpen` | `ImChatView.Open()` |
| 鼠标 | 按钮 `Command.Click` → `ImChatView.Open()` | 同上 |
| 手柄 | ↑ 十字短按（Q4 玩法行） | 同上 |

- 三个入口都走 `ImChatView.Open()` 单管线 → 行为恒一致（恢复上次频道、战斗/模态门控、PlotEnabled 总闸）。
- 按钮层**不设 `GamepadNavigationIndex`**（不参与手柄导航，防手柄导航在世界 UI 与按钮间混乱；手柄玩家用 ↑ 十字，不需要按钮）。
- 与密信通知（ImSecretNotify 圆环）共存：通知 = 事件驱动的临时圆环（点击开缩略定位会话）；按钮 = 常驻入口（点击开完整模式）——职责不同，不冲突。

---

## 改动文件清单（总表，Q1-Q5 全部改动）

| # | 文件 | 改动 |
|---|---|---|
| 1 | `ImChat/ImChatView.cs` | ① `OnScreenFrameTick` 加 pending 密信延迟打开分支（含 ~2s 超时丢弃）② `HandleCompactInput` 改位置感知 mask（含 MouseWheels 现状保留）③ 层归属迁移提升到 `Tick()` 顶层（B'，含 Q5 按钮层迁移复用）④ 手柄模态：`Open()` 冻结 / `Close()` 解冻（仅 `UsingGamepad && Mission`）+ `UsingGamepad` 变化检测 ⑤ B 键：`IsChannelListOpen` 时收下拉、否则关面板 ⑥ Campaign 侧驱动按钮 Manager Tick |
| 2 | `GUI/SecretLetterButtonInjector.cs` | `OpenSecretLetter` 改「PopScreen + 写 pending」（Apply Changes 反射失败 → 直接 PopScreen + 日志）；注入按钮加 `GamepadNavigationIndex` |
| 3 | `Notify/ImSecretNotifyManager.cs` + `ImSecretNotifyVM` | 通知层 mask 改位置感知（圆环矩形内才拦，同 HitTest 思路）；通知圆环加导航索引 |
| 4 | `GUI/Prefabs/ImChatCompact.xml` + `ImChat.xml` | 各按钮（缩略三件套/放大/关闭/发送、完整标题带/左栏/输入框）加 `GamepadNavigationIndex`；**两 prefab 加 `<NavigationScopeTargeter>`（§4.7 方案）**；两模式输入区上方加手柄提示行 |
| 5 | `Core/Settings.cs` | `DefaultInteractions` IM 行手柄键 = `"LUp"` |
| 6 | `Input/ModInput.cs` | 别名表补 `LUp`/`上`；`GlyphForKey(InputKey)` public 化（内部 `GlyphsFor` 已有）；`Glyph()` 显示「↑」 |
| 7 | `GUI/Prefabs/ImSecretNotify.xml` | 圆环按钮导航索引 |
| 8 | `ImChat/ImChatVM.cs` | `PadHintText`/`HasPadHint`（`UsingGamepad` 时非空，字形经 `GlyphForKey` 拼） |
| 9 | `ImChat/ImChatStore.cs` | 新增 `GetTotalUnread()`（三固定频道 + 私聊索引求和） |
| 10 | `ImChat/ImChatMissionView.cs` | `OnMissionTick` 驱动按钮 Manager Tick；`OnMissionScreenFinalize` 摘按钮层兜底 |
| 11 | 新 `Notify/ImChatOpenButtonManager.cs` | 静态：挂载/迁移/显示条件/徽标刷新/脉冲动画/点击（仿 NinjaNotificationManager + ImChatView 迁移模式） |
| 12 | 新 `GUI/Prefabs/ImChatOpenButton.xml` | 全屏根 `DoNotAcceptEvents="true"` 穿透 + 右缘下部按钮 + 右上角徽标；按钮 `Command.Click` 直连 Manager |
| 13 | 语言 XML（EN/CN） | `LWN_im_open_button_hint`、`LWN_im_pad_hint`、`LWN_im_first_open` 补手柄句——铁律 13 |

## 验证计划

1. `dotnet build`（Debug/Release，v1.4.8 本机）编译通过。
2. **Q2**：队伍屏转移部队未应用 → 点密信 → Apply Changes? → 关屏 → 地图上 IM 打开定位私聊（无黑屏、无崩溃）；家族屏直接关屏同验；连点两行不重开。
3. **Q3**：Mission 内缩略模式 → 鼠标移出面板 → 左键攻击/右键格挡/滚轮正常、WASD 正常、视角可转；鼠标移入面板 → 点按钮不挥刀；下拉开时列表滚轮正常；完整模式行为不回归（模态语义不变）。
4. **Q3 附**：Mission 内密信通知弹出 → 攻击/格挡/移动各测一遍（位置感知后应放行）；10s 自动消失兜底。
5. **Q4**：手柄连接 → ↑ 十字短按呼出 IM（口哨同响可接受）→ 摇杆导航各按钮 + A 确认（缩略⇄完整、切频道、计划卡按钮全可达）→ B 关闭；手柄模式下面板打开 → 角色冻结（摇杆/攻击全停）→ B 关闭后恢复；中途拔手柄/动鼠标 → 设备切换自动解冻/冻结、键盘缩略回位置感知；通知圆环手柄聚焦打开；注入密信按钮手柄聚焦（失败 → 标注鼠标/键盘专用，探查板为手柄入口）。
6. **Q4b（4.5b 分水岭）**：手柄键是否被层 InputRestrictions 拦截——Mission 面板内 B 关闭 / 大地图摇杆归属 / 大地图手柄模态是否成立；引擎导航方向是否认十字键（降级方案前提）。
7. **Q5（呼出按钮）**：大地图右缘下部按钮常显（PlotEnabled 开）→ 点击呼出 IM 完整模式；关闭开关 → 按钮消失；Mission 内非战斗显示、战斗中隐藏；有新消息 → 徽标数字 +1 且跳动 ~3s、查看会话后回落、数字 0 徽标隐藏、99+ 截断；打开 IM 时按钮被面板遮罩盖住（点击无效）；ESC 系统菜单盖住按钮；队伍屏/家族屏/定居点菜单上按钮可见可点；**按钮层零输入限制实机确认：按钮弹出/常驻期间攻击/格挡/移动全正常（pitfalls 纪律回归）**；按钮与通知圆环不重叠（右缘下部 vs 中部）；手柄导航不聚焦按钮（无导航索引）；键盘 O / 鼠标点击 / 手柄 ↑ 十字三入口行为一致。
8. 回归：IM 完整模式打字/滚轮/ESC 不回归；`Scripts/validate_localization.py` 通过（如新增键）。

## 风险与取舍

| 风险 | 影响 | 对策 |
|---|---|---|
| 位置感知 mask 的 1 帧竞态 | 极低概率点面板同时挥一刀 | 无害；可选 150ms 缓滞 |
| 自定义层手柄导航不激活 | 手柄面板内操作失败 | **机制已解（§4.7）：prefab 声明 `<NavigationScopeTargeter>` 即激活**——不再是「自动激活」的未知数；剩余风险 = 跨版本属性名（v1.2.12 实机确认），失败再退化为「手动设初始焦点」 |
| ↑十字 口哨同响 | 轻微出戏 | config 可改绑；文档注明 |
| `SetPlayerControlFrozen` 冻结后 UI 导航不可用 | 手柄模态方案崩 | 实机验证（冻结只冻角色 Agent）；不行 → 依赖 InputRestrictions 拦手柄键（未验证，需实测） |
| 🔴 **手柄键未被 InputRestrictions 拦截**（4.5b） | 影响面已缩小：**只影响 Mission 内 B 关闭**（物理轮询失效） | 兜底 = 层内取消事件 / ESC / 按钮关闭（标注）；大地图方案已被十字键分流免疫，无需处理 |
| 手柄设备判定抖动（`UsingGamepad` = 连接 && !鼠标活跃） | 动一下鼠标切回键盘模式 | 设备切换检测已有范式（input.md）；切回键盘 = 解冻 + 位置感知，行为正确 |
| 注入密信按钮手柄聚焦失败 | 队伍/家族屏手柄无入口 | 保持探查板传信（L3 可达）为手柄入口，标注 |
| 完整模式层归属迁移未做（若只做 A' 不做 B'） | 密信入口路径外仍有叠屏场景（如其他入口在屏上打开） | 建议 A' + B' 一起做（迁移提升成本低） |
| Q2 Apply Changes 反射失败 | 队伍屏未保存变更丢失 | 降级 = 直接 PopScreen + 日志；实机验证原版调用链后消除 |
| 呼出按钮与通知圆环位置重叠 | 通知盖住按钮 10s | 按钮右缘下部（MarginBottom ~140）避让中部通知带，已实锤位置（ImSecretNotify.xml:9） |
| 平台软键盘体验差/不可用（Steam/Xbox 平台差异） | 手柄打字链路断 | 引擎流程 public 实锤（OpenOnScreenKeyboard + 回填）；实机验证；失败 → 回退「手柄不打字」（发送置灰），不影响其余 |
| 焦点视觉复用 Hovered 与鼠标 hover 冲突 | 手柄+鼠标混用时视觉串 | UsingGamepad 门控（手柄模式不读鼠标 hover）；备选自定义 Focused Brush |

## 轮子登记建议（实施后）

- 位置感知 mask = 「引擎无区域化输入限制 → 以鼠标位置为开关的全局 mask 模拟半模态岛」——值得登记进 `wheels.d/ui.md`（或 input.md）。
- GamepadNavigationIndex 自定义层可用性结论——登记进 `wheels.d/ui.md`。
- **常驻 UI 层零输入限制纪律**（按钮层不调 SetInputRestrictions，点击靠 hit-test 门控）——pitfalls 拦鼠标教训的正面应用，登记进 `wheels.d/ui.md`。

---

## 完善性检查记录（2026-08-17 评审，已修正 6 处）

> 从头到尾对照代码与引擎事实逐节核查后修正；每项标注原缺口 → 处置。

| # | 缺口 | 处置 |
|---|---|---|
| 1 | 🔴 **Q5 按钮层输入限制未写**——常驻层若照 NinjaNotification 抄 `SetInputRestrictions` = 常驻拦输入 = pitfalls 灾难 | §5.1 补「零 mask」纪律行；验证计划 Q5 补「按钮常驻期间攻击/格挡/移动正常」回归项 |
| 2 | 🔴 **大地图手柄模态缺口**——`SetPlayerControlFrozen` 是 Mission/Agent 层 API，大地图无 Agent；MapScreen 层叠照常 tick，摇杆 = 地图移动 + UI 导航双响应；且「手柄键是否被 InputRestrictions 拦」未验证（键盘已证拦不住） | 新增 §4.5b：分水岭验证 + 降级方案（大地图手柄退化为只开/关/查看 + 十字键导航）；验证计划补 Q4b；风险表补「手柄键未被拦截」行 |
| 3 | Q2 Apply Changes 触发方式未定（动态注入按钮怎么触发原版 Inquiry） | 补实现依赖标注：反编译 `PartyVM.ExecuteOpenConversation`/`DoneLogic` 确认；反射失败 → 直接 PopScreen + 日志 |
| 4 | Q2 pending 失败路径——`CanOpen()` false 时永久卡住；连点多按钮 | 补 ~2s 超时丢弃 + 覆盖式语义 + `Close()` 清 pending |
| 5 | 按钮与密信通知圆环位置重叠（右缘垂直居中，ImSecretNotify.xml:9 实锤） | 按钮改右缘下部（MarginBottom ~140） |
| 6 | MouseWheels 位置感知一致性（下拉开时面板外滚轮不能缩放镜头） | 接受现状（时间短）+ 可选并入 overUi 判定，已注明 |
| 7 | 改动清单两张表重叠（§5.4 vs 总表） | 合并为一张 13 行总表 |
| 8 | 提示行位置：完整模式标题带已挤（正在思考/模式状态/关闭） | 统一放输入区上方（五轮修复后该区空闲） |
| 9 | 徽标细节未定（数字 0、超 99） | 补 0 → 隐藏、>99 → 「99+」 |
| 10 | ListPanel ItemTemplate 项导航未确认（引擎 GetSiblingIndex fallback） | 验证点补第 6 条（大概率自动，不自动则显式加索引） |
