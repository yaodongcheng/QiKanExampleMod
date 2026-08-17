# 队伍/家族面板「密信」按钮 — 实现方案

> **状态**：✅ **已实施（2026-08-17，编译通过零警告，待实机验证）**：`GUI/SecretLetterButtonInjector.cs`（动态注入/可见性/hover/行映射/关屏再开）+ `ImChatView.OnScreenFrameTick` 挂载 + `LWN_SecretLetter` Brush + `LWN_im_secret_letter_hint` 本地化（EN/CN）。
> **主题**：① 队伍屏（PartyScreen）行级密信按钮 ② 家族屏（ClanScreen）详情面板密信按钮 ③ 点击 → 既有 IM 私聊管线
> **关联**：[im-chat-system.md](im-chat-system.md)（IM 主文档 §三 私聊入口）、[rules/wheels.d/im.md](rules/wheels.d/im.md)（IM 轮子）、[rules/wheels.d/ui.md](rules/wheels.d/ui.md)（GauntletLayer 层序 / VM↔XML 铁律）
> **技术路线（用户裁定 2026-08-17）**：**不覆写原版 prefab**（避免与其他 UI mod 同名互斥），**纯 C# 动态插入**——扫描原版 widget 树 + `AddChild` 注入按钮 + `ClickEventHandlers` 接点击，行→Hero 映射读原版绑定已赋值的 widget 属性（`CharacterID` / `CharStringId`），零 prefab 覆写、零原版 VM 补丁。

---

## 一、背景与现状（查证结论，v1.4.8 本机反编译/XML 实测）

**问题**：私聊（密信）入口目前只有探查板（H）的「传信」按钮（`NpcInfoVM.ExecuteSendMessage`），玩家想主动找人私聊没有常驻入口。

| 现状 | 位置 | 说明 |
|---|---|---|
| 队伍屏交谈按钮 | `Modules/SandBox/GUI/Prefabs/Party/PartyTroopTuple.xml` | 行模板内 `TalkButton`（ButtonWidget，`Command.Click="ExecuteTalk"`，容器 `IsVisible="@IsTalkableCharacter"`）→ `PartyVM.ExecuteOpenConversation` → `CampaignMission.OpenConversationMission`（大地图临时对话场景，即用户说的"进 campaign 临时聊天"） |
| 家族屏交谈按钮 | `Modules/SandBox/GUI/Prefabs/Clan/ClanMembers.xml` | **单英雄详情面板**（`DataSource="{CurrentSelectedMember}"` = `ClanLordItemVM`，选中谁给谁），`Id="TalkToHeroButton"` 容器（`IsVisible="@IsTalkVisible"`，自动排除主英雄）→ `ClanLordItemVM.ExecuteTalk` → `_onTalk` 委托 |
| 行模板动态机制 | 同上两文件 | **单模板 + IsVisible 绑定显隐**（hero 行=TalkButton、兵种行=UpgradesPanel 升级路线、囚犯行=Recruit/Execute 按钮），不是多模板——动态插入天然兼容 |
| 现有私聊链路 | `ImChatManager.GetDirectConversation(heroId)` → `ImChatView.Open(conv)` | `NpcInfoVM.ExecuteSendMessage` 同款（探查板先例） |

**反编译实证的关键 API**（全部验证过）：

| 机制 | 结论 |
|---|---|
| 动态插入 | `ScreenBase.Layers` public、`GauntletLayer.UIContext` public、`Widget.AddChild/AddChildAtIndex` public（GauntletUI.dll:1976）、`ButtonWidget.ClickEventHandlers` **public `List<Action<Widget>>`**（引擎 `Command.Click` 绑定就是往这个列表加 handler，GauntletUI.dll:15426）——**不需要自定义 widget 类，`new ButtonWidget(context)` + 加 handler 即可** |
| 队伍行→Hero | 行根 `PartyTroopTupleButtonWidget`（`Id="PartyTroopTuple"`）属性 `CharacterID="@TroopID"`，`PartyCharacterVM` 里 `TroopID = Character.StringId`（反编译实锤）——Hero 行的 `CharacterID` = Hero 的 StringId |
| 家族行→Hero | 详情面板 `CharacterTableauWidget CharStringId="@CharStringId"`，`CharacterViewModel.CharStringId = character.StringId`（Core.ViewModelCollection.dll:671 实锤） |
| IM 在队伍/家族屏上打开 | `ImChatView.CanOpen()` 通过：`ModInput.IsSystemModalActive` 只查 `Inquiry` 屏名，Party/ClanScreen 非 Inquiry；非 Mission；层序 400 层可叠任意 `TopScreen` |
| prefab 覆写机制 | `WidgetFactory.GetPrefabNamesAndPathsFromCurrentPath` 按**文件名**注册、后加载模块覆盖——可行但**与其他 UI mod 同名互斥**（本次弃用，用户裁定） |

---

## 二、方案总览（纯 C# 动态插入）

```
ScreenBase.OnFrameTick（既有 ImScreenFrameTickPatch）
→ SecretLetterButtonInjector.TickInject(dt)  （0.3s 节流）
→ TopScreen 是 PartyScreen/ClanScreen 时扫描 UIContext.Root
→ 找 Id="TalkButton"（队伍行）/ Id="TalkToHeroButton"（家族详情）
→ 容器内无 Id="LWN_SecretLetterBtn" → AddChild(new ButtonWidget) + ClickEventHandlers.Add
→ 点击 → 解析 Hero StringId → ImChatManager.GetDirectConversation → ImChatView.Open
```

### 2.1 注入扫描

- **挂载点**：`ImChatView.OnScreenFrameTick` 已有每帧钩子（`ImScreenFrameTickPatch` → `ScreenBase.OnFrameTick`，Campaign 屏分支），注入扫描加在这里或独立静态类被它调用。
- **节流与过滤**：0.3s 一次；`ScreenManager.TopScreen.GetType().Name` 匹配 `PartyScreen`/`ClanScreen`（同 `ModInput.IsSystemModalActive` 的判名风格）才扫；其余屏零开销。
- **树访问**：`ScreenManager.TopScreen.Layers` → `GauntletLayer` → `.UIContext.Root` → 递归 `FindWidgetById`（mod 已有先例 `ImChatView.FindWidgetById`）。
- **幂等**：注入按钮带 `Id="LWN_SecretLetterBtn"`，容器内已有则跳过；家族屏 tab 切换重建树 → 扫描自动重注入（幂等天然覆盖）。

### 2.2 按钮构造（C# 侧，仿原版样式）

```csharp
var btn = new ButtonWidget(context)
{
    Id = "LWN_SecretLetterBtn",
    WidthSizePolicy = WidthSizePolicy.Fixed,
    HeightSizePolicy = WidthSizePolicy.Fixed,
    SuggestedWidth = 50f, SuggestedHeight = 50f,   // 队伍行用 !Party.Slot.Width/Height，家族 50×50 同 TalkToHeroButton
    HorizontalAlignment = HorizontalAlignment.Left,
    VerticalAlignment = VerticalAlignment.Center,
    MarginLeft = 10f, MarginRight = 10f,
    Brush = BrushFactory 取 "Party.TalkSlot.Background"（或自建 LWN_SecretLetter Brush）
};
btn.ClickEventHandlers.Add(OnClicked);
```

- **图标**：子节点 `Widget`（Sprite=`PartyScreen\talk_icon` 同款或查 NativeSpriteData 找信封/文书类 sprite，HueFactor 调色区分于交谈图标）。
- **hover 提示**：子节点 `HintWidget` + `new HintViewModel(new TextObject(本地化))`，文本走 `LWNTextHelper`（铁律 13）。

### 2.3 可见性（跟随原版绑定 + PlotEnabled 总闸）

- 队伍行：`btn.IsVisible = talkButton.ParentWidget.IsVisible` 每帧同步（引擎绑定系统负责原容器 `@IsTalkableCharacter` 的求值，mod 只跟随）→ hero 行显示、兵种/囚犯行自动隐藏。
- 家族屏：跟随 `TalkToHeroButton` 容器（`@IsTalkVisible`）。
- 叠加 `Settings.Instance.PlotEnabled`（MCM 密聊开关关闭时按钮隐藏，与"密聊入口整体隐藏"用户裁定一致——**动态方案红利：prefab 覆写方案做不到这个叠加**）。

### 2.4 行→Hero 解析（点击时）

```csharp
// 队伍行：向上找行根（Id="PartyTroopTuple"）→ 反射读 CharacterID
// 家族行：向上找 CharacterTableauWidget → 读 CharStringId（public 属性）
static string ResolveHeroId(Widget button, bool isParty)
{
    // 全 null-guard（铁律 2）；解析失败 → null → 静默 return + DebugLogger
}
```

- 反射读属性（`GetType().GetProperty("CharacterID")`）不依赖强类型——`PartyTroopTupleButtonWidget` 类程序集归属未定位（二进制 grep 未命中，可能名字被压缩），反射最稳。
- 拿到 StringId 后 `Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id)` 匹配；非 Hero（兵种行误点）→ null → 静默。

### 2.5 打开私聊（复用既有轮子）

```csharp
var conv = ImChatManager.GetDirectConversation(heroId);   // ImChatManager.cs:123
if (conv == null) return;
ImChatView.Open(conv);                                     // CanOpen 内部已查 PlotEnabled/战斗/系统弹窗
```

### 2.6 屏幕关闭策略（决策点，默认「关屏再开」）

| 方案 | 行为 | 评价 |
|---|---|---|
| **A（默认）** | 点击 → 队伍屏有未应用变更则弹原版 Inquiry（复用 `PartyVM.ExecuteTalk` 的 Apply Changes? 流程：确认 → `DoneLogic` → 关屏 → 开 IM）→ `ScreenManager.PopScreen()` → `ImChatView.Open` | 与探查板「传信」同款心智（关面板 → 回地图 → IM 打开定位私聊）；零 ESC 冲突；ClanScreen 无未保存变更概念，直接关屏 |
| B（实验备选） | 不关屏，IM 层（层序 400）叠在 PartyScreen 上 | 上下文不丢，但 ESC 双消费风险（IM Tick 与 PartyScreen 各自轮询 ESC，实机需验证先后；`ImScreenFrameTickPatch` 是 Postfix，IM 的 ESC 消费在 Screen 自身 Update 之后——可能队伍屏先关、IM 层随屏销毁） |

---

## 三、改动清单

| # | 文件 | 改动 |
|---|---|---|
| 1 | 新建 `GUI/SecretLetterButtonInjector.cs` | 静态类：`TickInject(dt)`（节流扫描）+ `InjectPartyRow`/`InjectClanDetail`（幂等插入）+ `OnClicked`/`ResolveHeroId`（解析+打开）+ `OnParallelUpdate` 可见性跟随 |
| 2 | `ImChat/ImScreenFrameTickPatch.cs`（或 `ImChatView.OnScreenFrameTick`） | 挂 `SecretLetterButtonInjector.TickInject(dt)` |
| 3 | `Languages/` 语言 XML | 新增键：`LWN_im_secret_letter_hint`（hover 提示，如"密信：给对方发私聊消息"）——按铁律 13 走 `{=LWN_*}` 机制，**禁止**裸中文字串 |
| 4 | `GUI/Brushes/MyBrush.xml`（可选） | 自建 `LWN_SecretLetter` Brush（或直接复用 `Party.TalkSlot.Background` 零改动） |
| 5 | 版本兼容 | **无需 #if**：Id 定位 + 反射读属性，1.2.12 若 Id/属性名不同 → 扫描不命中 → 功能静默不注入（安全降级，不崩） |

---

## 四、验证计划

1. **编译**：`dotnet build -c Debug`（本机 v1.4.8，MB2_PATH 自动检测）
2. **实机（队伍屏）**：开队伍屏 → hero 行出现密信按钮（与交谈按钮并排）、兵种行/囚犯行无 → 点击 → IM 打开定位私聊 → 发消息 → NPC 回复（记忆 im_user/im_npc 行写入）→ 关闭 IM 回地图
3. **实机（家族屏）**：选中成员 → 详情面板密信按钮 → 点击 → 同上；切 tab 再回来 → 按钮仍在（幂等重注入）
4. **变更检查**：队伍屏转移部队未应用时点密信 → 弹 Apply Changes? 提示
5. **PlotEnabled 关闭**：MCM 密聊开关关 → 按钮隐藏；打开恢复
6. **列表滚动/排序**：行增删后按钮跟随（幂等扫描补插）
7. **1.2.12 机器**：功能可用或安全降级（不崩、无报错刷屏）

---

## 五、风险与取舍

| 风险 | 影响 | 对策 |
|---|---|---|
| 反射读 `CharacterID`/`CharStringId` 跨版本属性名变化 | 1.2.12 可能解析失败 | null → 静默（安全降级）；1.2.12 实机验证补丁 |
| 手柄玩家到不了注入按钮（不进原版 `NavigationScope`） | 手柄体验缺失 | 实现时设 `GamepadNavigationIndex` 实测；不可用则标注鼠标/键盘专用（IM 本身手柄支持有限，可接受） |
| 全树扫描性能 | PartyScreen 树 ~几百 widget | 0.3s 节流 + TopScreen 类型过滤，可忽略 |
| ESC 双消费（方案 B 层叠屏） | 队伍屏意外关闭 | 默认走方案 A（关屏再开）规避；B 仅作实验 |
| 其他 mod 也往 TalkButton 容器插按钮 | 布局重叠 | 注入按钮用 LWN_ 前缀 Id + 靠边布局；与 prefab 覆写类 mod（占用同名文件）**完全共存** |

---

## 六、轮子登记建议

本次研究沉淀了可复用机制：**「往原版 GauntletUI 屏幕动态插入按钮」模式**（Screen.Layers→UIContext.Root 扫描 + AddChild + ClickEventHandlers + 读原版绑定已赋值的 widget 属性做数据桥）。实施完成后建议登记进 `plans/rules/wheels.d/ui.md`（若用户同意）。
