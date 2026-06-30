---
name: narrative-placeholder-extension
description: 为新犯罪类型/新玩法/新对话场景自动分析并补全叙事占位符逻辑
---

# 叙事占位符扩展技能

## 触发条件

用户提出以下需求时调用：
- "新增犯罪类型 {TypeName}"
- "这个新玩法的对话占位符够不够"
- "帮我检查 {TypeName} 的叙事覆盖"
- "扩展叙事占位符"

## 流程

### Step 1：读取现状

读三个文件：
1. `plans/narrative-placeholder-system.md` — 全部占位符词汇表 + 全部场景模板
2. `plans/crime-consequence-composable-v3.md` 中的 `EventTemplates` 注册表（已有的全部 `EventConfig`）
3. `ExampleModVS/ExampleMod/ExampleMod/Interaction/DialogueInjector.cs` — 当前 `InjectFromJson` 和 `ExecuteAction`

### Step 2：分析新玩法的独特信息维度

对每个**新信息维度**（现有占位符无法描述的事物），判断：
- 这个维度是否需要在对话中提及？
- 如果需要 → 新增占位符，确定 C# 查询来源
- 如果不需要 → 跳过

**常见新增维度示例**：
- 纵火 Arson：`{BurnedBuilding}` → 被烧的建筑名（仓库/民房/军营）
- 走私 Smuggling：`{ContrabandType}` → 违禁品类型
- 绑架 Kidnapping：`{KidnappedName}` → 被绑者名
- 抢劫 Robbery：`{RobberyLocation}` → 抢劫发生地（商路/酒馆外）

### Step 3：分析新玩法的独有对话场景

对照 `narrative-placeholder-system.md` 第二章的全部 50+ 场景模板，检查：

| 说话者身份 | 现有模板数 | 新玩法是否需要新分支？ |
|-----------|-----------|---------------------|
| Authority | ~25 | 如"族长对暗杀的反应"是否与"村长对偷羊的反应"有本质差异？ |
| Witness | ~5 | 新玩法的目击方式是否不同？（如"听到枪声"vs"看到人影"） |
| Suspect | ~6 | 新玩法的嫌犯行为是否不同？（如"逃犯"vs"被陷害者"） |
| Victim | ~3 | 新受害方类型是否需要独有模板？ |
| Bystander | ~5 | 流言内容是否不同？ |
| Companion | ~3 | 同伴反应是否不同？ |
| Mission | ~7 | 当场发现的机制是否不同？ |
| Retaliation | ~5 | 报复形式是否不同？ |

### Step 4：产出

输出一份结构化的扩充清单：

```markdown
## {新玩法名称} — 叙事占位符扩充清单

### 新增占位符

| 占位符 | 分类 | C# 查询来源 | 示例值 |
|--------|------|------------|--------|
| {NewPlaceholder} | A/B/... | 精确的 C# 字段路径或方法调用 | "示例" |

### 新增/覆写场景模板

| 场景编号 | 说话者 | 触发条件 | NPC台词模板 |
|---------|--------|---------|------------|
| A26 | Authority | {条件} | "{占位符组成的模板}" |

### 需新增的 Intent

| Intent 名 | Evaluate 条件 | OnSuccess 效果 |
|-----------|--------------|----------------|

### CrimeDialogueBuilder 改动

- [ ] `BuildAuthorityScript` switch 加 {新Stage/分支}
- [ ] 新增 `Build{SceneName}Turn` 方法
- [ ] `PlaceholderResolver.ResolveOne` switch 加 {新占位符}

### 叙事覆盖矩阵（新玩法）

| | Emerging | Active | Confrontation | Resolved |
|---|---------|--------|--------------|---------|
| Authority | ✅ A1/A2 | ✅ 新模板A26 | ✅ A19 | ✅ A21 |
| Witness | ✅ W1 | — | — | — |
| Suspect | — | ✅ S1-S4 | — | — |
| ... | | | | |

### 与现有占位符的差异对照

| 现有占位符 | 偷牲口值 | 暗杀值 | {新玩法}值 |
|-----------|---------|--------|----------|
| {CrimeVerb} | "偷了" | "杀了" | "{新动词}" |
| {CrimeScene} | "牲口圈" | "{victim}家附近" | "{新现场}" |
| {AuthorityRole} | "村长" | "族长" | "{新角色}" |
| {VictimLabel} | "村子" | "死者家族" | "{新受害方}" |
```

### Step 5：验证

- 对照 `narrative-placeholder-system.md` 的占位符表，确认没有遗漏的信息维度
- 生成一个"叙事覆盖矩阵"：犯罪类型 × 阶段 × 说话者身份 → 场景模板编号
- 对于标记为"⚠️"的缺口，提出补充方案

## 输出格式

最终输出直接以 Markdown 代码块的形式呈现，可直接追加到 `narrative-placeholder-system.md` 末尾作为新的一章（"## N. {新玩法名称} 扩展"）。
