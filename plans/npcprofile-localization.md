# NPCProfile 本地化方案

> 状态：⏳ 实施中

## Context

`NPCProfile.cs` 的 6 个方法全部硬编码中文，既用于 LLM prompt 也用于探查 UI（`NpcInfoVM.cs`）。

## 文件变更

```
ModuleData/Languages/
├── std_LivingWorldNpcs_strings.xml          ← 不变
├── std_LivingWorldNpcs_prompts.xml          ← NEW — English prompt 模板
├── language_data.xml                        ← 加 prompts.xml 引用
├── CNs/
│   ├── std_LivingWorldNpcs_strings.xml      ← 不变
│   ├── std_LivingWorldNpcs_prompts.xml      ← NEW — Chinese prompt 模板
│   └── language_data.xml                    ← 加 prompts.xml 引用
```

## Key 设计（~80 个）

- **大块模板 key**（GetClanInfo/GetKingdomInfo/GetSelfInfo/GetSelfWorthDescription/GetCloseRelations）~20 个
- **动机目标 key**（CalCurrentMotivation 各分支 LifeGoal/ShortGoal）~30 个
- **trait 显示值 key**（TemperStr/DesireStr 等属性值映射）~30 个

## 实施步骤

1. 创建 `std_LivingWorldNpcs_prompts.xml`（EN）
2. 创建 `CNs/std_LivingWorldNpcs_prompts.xml`（CN）
3. 两个 `language_data.xml` 各加引用
4. `LWNTextHelper.InitializeEnglishFallback()` 改为扫描 `std_*.xml`
5. `NPCProfile.cs` 替换 + trait helper
6. `validate_localization.py` 移除 `NPCProfile.cs` 豁免
7. build + validator 验证

## 🔴 待解决：英文模式下 CJK 名字显示为 `?`

### 现象
- 玩家中文名（如 "亨利"）在英文模式下，**AgentHUD 头顶**可以正常显示
- 同一个名字在 **NpcInfoVM 探查面板**（标题、家族名、部队名等）显示为 `?`
- 职业（Occupation）等通过 `LocalizeTrait` 映射的 trait 值已正常显示英文

### 已尝试的方案

| 方案 | 结果 |
|------|:--:|
| `agent.Name.ToString()` 直接拼字符串 | ❌ 仍然是 `?` |
| `ResolveCompoundMixed` 用 `MBTextManager.SetTextVariable(TextObject)` | ❌ 仍然是 `?` |
| `BuildCompoundTextObject` 返回 `TextObject` 给 VM 属性 | ❌ GauntletUI binding 报错：`TextObject cannot be converted to String` |
| `TitleText` 属性改为 `TextObject` 类型 | ❌ 同上 |

### 关键差异
- **AgentHUD**：`AgentName = agent.Name` — TextObject 赋值给 `string` 属性，C# 编译器隐式转换（`implicit operator string`），GauntletUI 拿到的是已经转好的 `string`
- **NpcInfoVM**：名字嵌入在复合模板中（`{=LWN_KEY}...{NAME}...`），模板本身是 English TextObject，变量替换后的最终 string 可能丢失了 CJK 的字体上下文

### 推测根因
GauntletUI 的 `TextWidget` 在渲染时根据文本来源语言选择字体。当文本来自 `{=LWN_KEY}English fallback` 的 `TextObject` 时，整个文本（包括通过 `SetTextVariable` 替换进去的 CJK 名字）都走英文字体渲染，导致 CJK 字形丢失。

AgentHUD 不经过 `{=LWN_KEY}` 模板，名字是独立的 `{=!}亨利` TextObject，GauntletUI 保留原始字体上下文。

### 待探索方向
- 在复合模板中将名字包裹为 `{=!}亨利` 格式（而非已展开的 "亨利" 字符串）
- 修改 GauntletUI XML 布局，将名字和标签分开为两个 TextWidget
- 使用 `TextObject` 拼接而非 `string` 拼接，让 `MBTextManager` 处理语言上下文
- 字体 fallback 配置：添加 CJK 字体作为英文模式下的 fallback 字体
