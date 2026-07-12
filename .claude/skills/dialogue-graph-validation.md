---
name: dialogue-graph-validation
description: 检查对话图结构的逻辑完整性 — CheckType、路由可达性、Intent对齐、Node拓扑
---

# 对话图结构校验

## 触发条件

用户提出以下需求时调用：
- "检查对话图" / "检查对话逻辑" / "验证对话结构"
- "这个对话有没有问题" / "帮我看看对话流"
- 新增/修改 `CrimeDialogueBuilder` 的 builder 方法后
- 新增/修改 `DialogueInjectScript` 的 JSON 模板后
- 新增 Intent 并配了对话 Transition 后

## 前置阅读

校验前必须先读以下文件：
1. `plans/dialogue-graph-design.md` — 数据模型定义 + 校验规则
2. 被检查的对话构建代码（如 `CrimeDialogueBuilder.cs` 或 JSON 文件）
3. 涉及到的 Intent 实现（如 `AccountabilityIntents.cs`）

## 校验流程

### Step 1：提取图结构

从代码中提取完整图结构，输出为标准化表示：

```
Node: <Id>
  NpcLine: "<文本>"  [terminal: 是/否]
  Transitions:
    → "<PlayerLine>"  [<CheckType>]  <Action>  ──success──▶ <NextNodeOnSuccess>
                                              ──fail─────▶ <NextNodeOnFail>
```

如果代码分散在多个方法里（如 `CrimeDialogueBuilder` 的 `Build*Node` 方法），先拼接成完整图再分析。

### Step 2：CheckType 与 Intent 对齐

对每个 `CheckType.SkillCheck` 的 Transition：
- [ ] 定位 `Action` 字段引用的 Intent 类（如 `INTENT:CharmDefense` → `CharmDefenseIntent`）
- [ ] 确认 `Intent.Goal != null`（否则 error：Transition 声明了 SkillCheck 但 Intent 无 Goal）
- [ ] 确认 `Intent.OnSuccess` 和 `Intent.OnFail` 均有实质性实现或正确继承 base

对每个 `CheckType.None` + `INTENT:xxx` 的 Transition：
- [ ] 确认 `Intent.Goal == null`（否则 warning：Intent 有检定但 Transition 声明了 None，NPC 回应不会分支）

### Step 3：路由完整性

对每个 Transition：
- [ ] `NextNodeOnSuccess` 为空 → 合法（terminal，关窗）。但如果该 Transition 的 CheckType 是 SkillCheck，追问：成功就关窗是设计意图吗？
- [ ] `NextNodeOnSuccess` 非空 → 确认目标 Node.Id 在图结构中存在
- [ ] `CheckType.SkillCheck` 时：
  - `NextNodeOnFail` 为空 → warning："失败 fallback 到 NextNodeOnSuccess，失败后的 NPC 反应和成功后走同一个 Node，确认这是意图？"
  - `NextNodeOnFail` 非空 → 确认目标 Node.Id 存在
- [ ] `CheckType.None` 时：
  - `NextNodeOnFail` 非空 → warning："无检定 Transition 设了 NextNodeOnFail，此字段不会被使用"

### Step 4：图拓扑

- [ ] **可达性**：列出所有 Node.Id，标记哪些被至少一个 Transition 引用。未被引用的（且非 EntryNode）→ error："不可达 Node"
- [ ] **EntryNode 存在**：`script.EntryNode` 指向的 Node 必须在 Nodes 列表中存在
- [ ] **死路检测**：`Transitions` 为 `null` 的 Node → error（应至少是空列表 `[]` 表示 terminal）。`Transitions` 为 `[]` 且 `NpcLine` 为空 → error（terminal Node 也必须有台词）
- [ ] **自循环**：`NextNodeOnSuccess` 或 `NextNodeOnFail` 指向 Node 自身 → warning：说明理由或改为合法设计

### Step 5：叙事一致性

对每个有检定的 Transition：
- [ ] **成败分叉合理性**：`NextNodeOnSuccess` 和 `NextNodeOnFail` 指向同一个 Node → warning："成败同目的地，NPC 成败反应不同但后续对话相同——叙事是否断裂？"
- [ ] **terminal 前后一致性**：检定失败 → `NextNodeOnFail = ""` → 检查失败 Node 的 `NpcLine` 是否表达了"对话终结"的语义（如"没什么好说的"、"滚"、"来人"），而非开放性语句（如"你再去查查"）
- [ ] **continue_chat 滥用**：统计多少 Transition 指向 `continue_chat`。如果某个 Transition 的 NPC 回应表达了"终结/愤怒/不信任"但目的地是 `continue_chat`（"还有什么别的想说的吗？"）→ error：叙事断裂

### Step 6：占位符封闭性（仅 CrimeDialogueBuilder）

对使用 `PlaceholderResolver` 的 Node：
- [ ] `NpcLine` 中的每个 `{Placeholder}` 在 `PlaceholderResolver.ResolveOne` 中有对应 case
- [ ] 新增的占位符是否在 `narrative-placeholder-system.md` 中登记

## 输出格式

```markdown
## 对话图校验报告：<脚本名称>

### 图结构概览
（Step 1 的输出：Node 列表 + Transition 列表的树形文本）

### 发现的问题

| # | 严重度 | 类别 | 位置 | 描述 |
|---|--------|------|------|------|
| 1 | 🔴 error | 路由 | Node X → Transition Y | NextNodeOnSuccess='foo' 但 Node 'foo' 不存在 |
| 2 | 🟡 warning | CheckType | Node X → Transition Y | Intent 有 Goal 但 CheckType 声明为 None |
| ... | | | | |

### 叙事一致性专项

（Step 5 的发现：哪些 Transition 成败同目的地、哪些 continue_chat 被滥用）

### 不可达 Node

（Step 4 的发现：哪些 Node 在图里但没有 Transition 指向它）

### 建议修复
（按优先级排列的具体修复方案）
```

## 注意事项

- 区分 **error**（图结构损坏，对话会死锁/崩）和 **warning**（逻辑可疑，可能是设计意图也可能是遗漏）
- 如果代码正在重构中（部分 Node 还是旧字段 `NextNode`/`NpcResponse`），先标注旧字段使用位置，建议迁移优先级
- 涉及 LLM 生成的 JSON 对话模板时，额外检查 JSON key 是否匹配 `[JsonProperty]` 映射
- 校验结果记录到 `plans/dialogue-graph-validation-reports/` 目录下，文件名 `YYYY-MM-DD_<脚本名>.md`
