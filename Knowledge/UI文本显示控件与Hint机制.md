---
name: ui-text-display-hint-mechanism
description: 骑砍2 UI文本显示控件全览 — HintWidget/HintViewModel tooltip、InitialStateOption禁用提示、TextObject本地化、SavedGameVM存档禁用原因
metadata:
  type: reference
---

# UI文本显示控件与 Hint 提示机制

## 两个文字显示体系

骑砍2 有 **两套完全独立的文字显示体系**，不可混淆：

| 体系 | API | 用途 | 显示方式 |
|------|-----|------|---------|
| **Hint/Tooltip** | `HintViewModel` + `HintWidget` | hover 弹出提示、按钮禁用原因 | GauntletUI tooltip（跟随鼠标/按钮） |
| **Toast/QuickInfo** | `MBInformationManager.AddQuickInformation()` | 事件驱动的屏幕浮动消息 | 原生 C++ 引擎渲染（左下角淡入淡出） |

**两者零交集**：Hint 体系不走 `AddQuickInformation`，Toast 体系不走 `HintViewModel`。

---

## 核心控件

| 控件 | 层级 | 用途 |
|------|------|------|
| `HintWidget` | GauntletUI Prefab (XML) | 绑定 `HintViewModel`，hover 时弹出 tooltip |
| `HintViewModel` | ViewModel (C#) | 包装 `TextObject`，作为 `HintWidget` 的 DataSource |
| `TextWidget` | GauntletUI Prefab (XML) | 静态文本显示，`Text="@PropertyName"` 绑定 VM 属性 |
| `RichTextWidget` | GauntletUI Prefab (XML) | 富文本显示，支持 BBCode 样式标签 |

## HintWidget / HintViewModel — tooltip 弹出机制

### 原理

`HintWidget` 是一个透明覆盖层，放在目标控件上方，监听 hover 事件：

```xml
<!-- 典型用法：覆盖在按钮上的 tooltip -->
<ButtonWidget Id="MyButton" ... Command.Click="DoSomething">
  <Children>
    <TextWidget Text="@ButtonLabel" ... />
    <HintWidget DataSource="{MyHint}"
                WidthSizePolicy="StretchToParent"
                HeightSizePolicy="StretchToParent"
                Command.HoverBegin="ExecuteBeginHint"
                Command.HoverEnd="ExecuteEndHint" />
  </Children>
</ButtonWidget>
```

`HintViewModel` 是 C# 层的数据源：

```csharp
// TaleWorlds.Core.ViewModelCollection.dll
public class HintViewModel : ViewModel
{
    public HintViewModel(TextObject hintText, string uniqueName = null);
    public TextObject HintText;  // 公共字段（非属性），类型为 TextObject，需 .ToString() 取字符串
}
```

### 在 ViewModel 中使用

```csharp
[DataSourceProperty]
public HintViewModel MyHint { get; set; }

// 构造函数中
MyHint = new HintViewModel(new TextObject("{=hintId}提示文本", null));
```

### 在 SandBox 中的关键用例

**存档列表 — 禁用存档的提示原因**（`SavedGameVM`）：

```csharp
// SandBox.ViewModelCollection.dll → SavedGameVM.RefreshValues()
(bool, TextObject) isDisabledWithReason = GetIsDisabledWithReason();
IsDisabled = isDisabledWithReason.Item1;
DisabledReasonHint = new HintViewModel(isDisabledWithReason.Item2, null);
```

```csharp
// SavedGameVM.GetIsDisabledWithReason()
private (bool IsDisabled, TextObject Reason) GetIsDisabledWithReason()
{
    ApplicationVersion saveVersion = MetaDataExtensions.GetApplicationVersion(Save.MetaData);
    ApplicationVersion currentVersion = Utilities.GetApplicationVersionWithBuildNumber();
    if (currentVersion < saveVersion)
    {
        TextObject reason = new TextObject(
            "{=9svpUWeo}Save version ({SAVE_VERSION}) is higher than the current version ({CURRENT_VERSION}).",
            null);
        reason.SetTextVariable("SAVE_VERSION", saveVersion.ToString());
        reason.SetTextVariable("CURRENT_VERSION", currentVersion.ToString());
        return (IsDisabled: true, Reason: reason);
    }
    return (IsDisabled: false, Reason: TextObject.Empty);
}
```

存档/读档界面 XML（`SandBox/GUI/Prefabs/SaveLoad/SaveLoadScreen.xml`）中的 `HintWidget` 绑定到此 VM 属性。

## InitialStateOption — 主菜单按钮的禁用/提示机制

### 注册（`SandBoxViewSubModule.OnSubModuleLoad()`）

```csharp
// SandBox.View.dll → SandBoxViewSubModule
Module.CurrentModule.AddInitialStateOption(new InitialStateOption(
    id: "SandBoxNewGame",
    name: new TextObject("{=171fTtIN}SandBox", null),
    orderIndex: 3,
    onAction: () => MBGameManager.StartNewGame(new SandBoxGameManager()),
    isDisabledAndReason: () => IsSandboxDisabled(),    // Func<(bool, TextObject)>
    enabledHint: _sandBoxAchievementsHint              // TextObject — 即使按钮可用也显示
));
```

### InitialStateOption 定义

```csharp
// TaleWorlds.MountAndBlade.dll
public class InitialStateOption
{
    public string Id { get; }
    public TextObject Name { get; }
    public int OrderIndex { get; }
    public Func<(bool, TextObject)> IsDisabledAndReason { get; }  // 返回 (是否禁用, 原因文案)
    public TextObject EnabledHint { get; }                         // 按钮可用时的提示（null=不显示）
}
```

### InitialMenuOptionVM — 绑定到 UI

```csharp
// TaleWorlds.MountAndBlade.ViewModelCollection.dll
public class InitialMenuOptionVM : ViewModel
{
    public HintViewModel DisabledHint { get; set; }   // 按钮禁用时 hover 显示
    public HintViewModel EnabledHint { get; set; }    // 按钮可用时 hover 显示（来自 EnabledHint）
    public bool IsDisabled => InitialStateOption.IsDisabledAndReason().Item1;

    public InitialMenuOptionVM(InitialStateOption option)
    {
        DisabledHint = new HintViewModel(option.IsDisabledAndReason().Item2);
        EnabledHint = new HintViewModel(option.EnabledHint);  // 可为 null
    }
}
```

**行为**：
- `IsDisabled == true` → 按钮灰显，hover 显示 `DisabledHint`（原因文案）
- `IsDisabled == false` → 按钮正常，hover 显示 `EnabledHint`（如果非 null）
- `EnabledHint` 的典型用例：SandBox 新游戏按钮即使可用也显示"由于非官方模组，成就已禁用。"

## TextObject — 本地化文本包装

### 格式

```csharp
// 带本地化 key（key 由 game engine 自动生成）
new TextObject("{=R0AbAxqX}Achievements are disabled due to unofficial modules.", null);

// 纯文本（无本地化）
new TextObject("Hello World", null);

// 带变量
var text = new TextObject("{=ABC123}你有 {GOLD} 金币。", null);
text.SetTextVariable("GOLD", gold.ToString());
```

### 关键 API

| 方法 | 说明 |
|------|------|
| `.ToString()` | 解析为最终显示文本（查本地化表替换） |
| `.SetTextVariable(key, value)` | 设置 `{KEY}` 占位符的值 |
| `TextObject.Empty` | 静态空文本单例 |

### 本地化 XML 定义

```xml
<!-- SandBoxCore/ModuleData/Languages/std_SandBox.xml -->
<string id="R0AbAxqX" text="Achievements are disabled due to unofficial modules." />

<!-- 简体中文 -->
<!-- SandBoxCore/ModuleData/Languages/CNs/std_SandBox-zho-CN.xml -->
<string id="R0AbAxqX" text="由于非官方模组，成就已禁用。" />
```

**查找优先级**：`TextObject.ToString()` → 按当前语言查 XML 表 → 找到则用翻译 → 否则用 fallback（`{=ID}` 后面的原文）。

## 逃避菜单项 — EscapeMenuItemVM

### 定义

```csharp
// TaleWorlds.MountAndBlade.ViewModelCollection.dll
public class EscapeMenuItemVM : ViewModel
{
    public EscapeMenuItemVM(
        TextObject item,
        Action<object> onExecute,
        object identifier,
        Func<Tuple<bool, TextObject>> getIsDisabledAndReason,  // 跟 InitialStateOption 同模式
        bool isPositiveBehaviored = false
    );
}
```

游戏内 ESC 菜单中的"保存"、"读取"等选项也使用 `(isDisabled, reason)` 委托来控制灰显和 tooltip。

## 实战模式：如何给按钮/列表项加提示

### 模式 A：静态提示（始终显示）

```csharp
// VM 层
public HintViewModel MyStaticHint { get; set; }
MyStaticHint = new HintViewModel(new TextObject("{=hintId}这是一个提示", null));

// XML 层
<HintWidget DataSource="{MyStaticHint}" ... />
```

### 模式 B：条件禁用 + 原因提示

```csharp
// 注册时传入 delegate
new InitialStateOption("MyOption", name, 0, action,
    isDisabledAndReason: () => {
        if (someCondition)
            return (true, new TextObject("原因文案", null));
        return (false, TextObject.Empty);
    },
    enabledHint: null
);
```

### 模式 C：存档列表项 — 每项独立的禁用原因

参考 `SavedGameVM.GetIsDisabledWithReason()` 模式 — 在 `RefreshValues()` 中评估条件，赋值 `DisabledReasonHint`。

## MBInformationManager.AddQuickInformation — Toast 快速消息（屏幕左下角浮动文字）

### 定义

```csharp
// TaleWorlds.Core.dll — 静态事件广播类（非 ViewModel）
public static class MBInformationManager
{
    // 事件：C++ 原生引擎订阅此事件，收到字符串后在 GauntletUI 层渲染文字
    public static event Action<string, int, BasicCharacterObject, string> FiringQuickInformation;

    public static void AddQuickInformation(
        TextObject message,                    // 显示文本（调用 .ToString() 解析本地化）
        int extraTimeInMs = 0,                 // 额外驻留时间（默认已有基础时长）
        BasicCharacterObject announcerCharacter = null,  // 播报者头像（null=无头像）
        string soundEventPath = "")            // 音效路径（""=无音效）
    {
        // 1. 广播事件 → C++ 引擎订阅，渲染到屏幕
        MBInformationManager.FiringQuickInformation?.Invoke(
            message.ToString(), extraTimeInMs, announcerCharacter, soundEventPath);
        // 2. 同步写 Debug 日志
        Debug.Print(message.ToString(), 0, Debug.DebugColor.White, 1125899906842624uL);
    }
}
```

### 显示链条（完整端到端）

```
TextObject("{=Z9mcDuDi}成就被禁用！")
  ↓ .ToString() → 查本地化 XML → "成就被禁用！"
MBInformationManager.AddQuickInformation(reason, 4000, null, "")
  ├─ FiringQuickInformation?.Invoke("成就被禁用！", 4000, null, "")
  │     ↓ 订阅者：TaleWorlds.Native.dll（C++ 引擎，ilspycmd 不可反编译）
  │     ↓ 引擎创建 GauntletUI TextWidget → 定位左下角 → 淡入 → 驻留 → 淡出销毁
  └─ Debug.Print("成就被禁用！") → 写入引擎日志
```

### 关键事实

- **订阅者在 C++ 引擎层**：搜遍全部 60+ 个 TaleWorlds .NET DLL，`FiringQuickInformation` 只有声明/调用/Clear，**零托管订阅者**。渲染由 `TaleWorlds.Native.dll` 通过 `TaleWorlds.DotNet` 托管↔原生桥接完成。
- **不是 ViewModel**：`MBInformationManager` 是静态事件广播类，不继承 `ViewModel`，不绑定 XML Prefab。
- **单重载**：`AddQuickInformation` 只有一个签名，没有其它重载。
- **与 `InformationManager.DisplayMessage` 无关**：后者在 `TaleWorlds.Library.dll`，是另一套消息管道，用于调试/脚本输出。

### 拦截/日志

Harmony Prefix 可以拦截所有 toast：

```csharp
[HarmonyPatch(typeof(MBInformationManager), "AddQuickInformation")]
public static class AddQuickInformationLoggerPatch
{
    [HarmonyPrefix]
    public static void Prefix(TextObject message)  // ⚠️ 参数名必须匹配！实际方法签名是 TextObject message
    {
        if (message != null)
        {
            string textStr = message.ToString();
            if (!string.IsNullOrEmpty(textStr))
                DebugLogger.Log($"[AddQuickInformation] \"{textStr}\"");
        }
    }
}
```

**坑点**：Harmony Prefix 按参数**名称**匹配；实际方法第一个参数叫 `message`，Patch 里写 `TextObject text` → `Parameter "text" not found` 异常。

### 典型调用场景

```csharp
// 成就禁用提示（StoryMode.dll → AchievementsCampaignBehavior.DeactivateAchievements）
MBInformationManager.AddQuickInformation(reason, 4000, null, "");

// 存档失败
MBInformationManager.AddQuickInformation(new TextObject("{=u9PPxTNL}Save Error!"));

// 金钱不足
MBInformationManager.AddQuickInformation(GameTexts.FindText("str_warning_you_dont_have_enough_money"));

// 距离商队太近警告（带额外时间 + 播报者头像）
MBInformationManager.AddQuickInformation(
    new TextObject("{=ki1CWgcP}Warning! You are too close to the caravan..."), 100,
    PartyBaseHelper.GetVisualPartyLeader(caravanParty.Party));
```

## 成就禁用文案的完整调用链

`{=Z9mcDuDi}`（"成就被禁用！"）是兜底文案，与 `IsGameIntegrityAchieved` 产出的具体原因文案走**同一条管道**：

```
StoryMode.dll: AchievementsCampaignBehavior.{
    OnConfigChanged / OnGameLoadFinished
}
  ↓ 调用
CheckAchievementSystemActivity(out reason)
  ↓ behavior == null 或 _deactivateAchievements 已为 true → reason 保持 TextObject.Empty
  ↓ IsGameIntegrityAchieved(ref reason) 返回 false → reason 被设为具体原因文案
  ↓ 返回 false
DeactivateAchievements(reason)
  ↓ reason == null || reason == TextObject.Empty → 兜底：
  ↓   reason = new TextObject("{=Z9mcDuDi}Achievements are disabled!", null)
  ↓ reason 来自 IsGameIntegrityAchieved → 直接使用（{=R0AbAxqX} / {=sO8Zh3ZH} / {=dt00CQCM}）
MBInformationManager.AddQuickInformation(reason, 4000, null, "")
```

**两种文案互斥关系**：

| 条件 | reason 来源 | 玩家看到的 |
|------|------------|-----------|
| `IsGameIntegrityAchieved` 返回 false | `{=sO8Zh3ZH}` / `{=R0AbAxqX}` / `{=dt00CQCM}` | 具体原因：作弊 / 非官方模块 / 版本降级 |
| `behavior == null` 或 `_deactivateAchievements` 已置位 | `{=Z9mcDuDi}`（兜底） | 泛泛一句："成就被禁用！" |

## 相关字符串 ID 速查

| String ID | 英文原文 | 代码位置 |
|-----------|---------|---------|
| `R0AbAxqX` | Achievements are disabled due to unofficial modules. | `SandBox.dll` → `DumpIntegrityCampaignBehavior.IsGameIntegrityAchieved()` |
| `sO8Zh3ZH` | Achievements are disabled due to cheat usage. | 同上 |
| `dt00CQCM` | Achievements are disabled due to version downgrade. | 同上 |
| `Z9mcDuDi` | Achievements are disabled! | `StoryMode.dll` → `AchievementsCampaignBehavior.DeactivateAchievements()`（兜底） |
| `j09m7S2E` | Achievements are disabled in SandBox mode! | `SandBox.View.dll` → `SandBoxViewSubModule._sandBoxAchievementsHint` |
| `9svpUWeo` | Save version ({SAVE_VERSION}) is higher than the current version ({CURRENT_VERSION}). | `SandBox.ViewModelCollection.dll` → `SavedGameVM.GetIsDisabledWithReason()` |

## 关键 DLL 速查

| DLL | 包含内容 |
|-----|---------|
| `TaleWorlds.MountAndBlade.dll` | `InitialStateOption` 定义 |
| `TaleWorlds.MountAndBlade.ViewModelCollection.dll` | `InitialMenuOptionVM`, `EscapeMenuItemVM`, `EscapeMenuVM` |
| `TaleWorlds.Core.dll` | `MBInformationManager` — Toast 快速消息事件广播器；`TextObject` |
| `TaleWorlds.Core.ViewModelCollection.dll` | `HintViewModel` |
| `SandBox.dll` | `DumpIntegrityCampaignBehavior.IsGameIntegrityAchieved()` — 成就完整性检查 |
| `SandBox.View.dll` | `SandBoxViewSubModule` — 注册主菜单选项 |
| `SandBox.ViewModelCollection.dll` | `SaveLoadVM`, `SavedGameVM` — 存档界面 VM |
| `StoryMode.dll` | `AchievementsCampaignBehavior` — 成就禁用检查 & `DeactivateAchievements` |
