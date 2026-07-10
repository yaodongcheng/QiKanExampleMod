# DialogueInjector 全局调用审计

> 审计日期：2026-07-10

## 一、DialogueInjector 概述

`DialogueInjector`（[Interaction/Dialogue/DialogueInjector.cs](../../ExampleModVS/ExampleMod/ExampleMod/Interaction/Dialogue/DialogueInjector.cs)）是动态对话注入引擎的核心，负责将 `DialogueInjectScript` 对象直接注册到原版 `ConversationManager`，不依赖 `DialogFlow` 建造者。

### 公开 API

| 方法 | 用途 |
|------|------|
| `InjectFromJson(string jsonPath)` | JSON 文件 → 注入对话 |
| `InjectScript(DialogueInjectScript, string label)` | 内存脚本 → 注入（gateway PlayerLine 模式，priority 125） |
| `InjectScriptAsOpening(DialogueInjectScript, string label)` | 内存脚本 → 注入（开场白模式，NPC 先说话，priority 200） |
| `ClearAll()` | 清除全部注入 |
| `RemoveRelatedLines(string label)` | 按标签清除注入 |
| `FindJsonFile(string fileName)` | 定位 JSON 测试文件 |
| `GetSearchPathsDescription(string fileName)` | 错误信息辅助 |

## 二、调用全景图

```
                        ┌── MyCommands.cs (调试指令)
                        │   ├── ClearAll()
                        │   ├── FindJsonFile() → InjectFromJson()
                        │   └── GetSearchPathsDescription()
                        │
                        ├── MyBehavior.cs (离开定居点)
                        │   └── RemoveRelatedLines($"crime_{evt.EventId}")
                        │
                        ├── CrimeDialogueBehavior.cs (调试/手动触发)
                        │   ├── RemoveRelatedLines($"crime_{evt.EventId}")
                        │   └── CrimeDialogueBuilder.BuildScript() → InjectScript()
                        │
                        ├── ConversationEntryPatch.cs (对话入口自动注入)
DialogueInjector ────────┼── RemoveRelatedLines(tag) [残留清理+注入前清理]
                        │   └── CrimeDialogueBuilder.BuildScript() → InjectScript()
                        │   [含延迟注入: PendingAlertScript → InjectScript()]
                        │
                        ├── AtomicAction.cs (AlertForceConversationAction)
                        │   ├── PendingAlertScript 静态字段
                        │   └── CrimeDialogueBuilder.BuildAlertInterceptScript()
                        │       → InjectScriptAsOpening()
                        │
                        └── CombatManager.cs (战斗认输) ⚠ 问题区
                            ├── PlayerSurrenderToAgent: 手写脚本 → InjectScriptAsOpening()
                            └── AcceptAgentSurrender: 手写脚本 → InjectScriptAsOpening()
```

## 三、发现的六个问题

### 🔴 问题 1：CombatManager 手动构建 DialogueInjectScript — 重复造轮子

`CombatManager.PlayerSurrenderToAgent`（~70行）和 `AcceptAgentSurrender`（~55行）在业务代码里手写 `new DialogueInjector.DialogueInjectScript { Turns = ... }` 的完整脚本结构。项目已有 `CrimeDialogueBuilder` 作为规范的脚本构造器——认输对话也应走 Builder 模式。

**影响**：硬编码中文在引擎 mod 里、脚本结构与 Intent 耦合在 CombatManager 中、修改台词需要改动业务逻辑代码。

### 🔴 问题 2：ExecuteIntentAction 和 BuildOptionCondition 内部逻辑重复

[DialogueInjector.cs:340-419](../../ExampleModVS/ExampleMod/ExampleMod/Interaction/Dialogue/DialogueInjector.cs#L340-L419) 和 [DialogueInjector.cs:626-677](../../ExampleModVS/ExampleMod/ExampleMod/Interaction/Dialogue/DialogueInjector.cs#L626-L677) 中，同一文件内两处几乎完全相同的 `IntentContext` 构造：获取 `partnerAgent` → `IntentContext.Build()` → `Hero` 回退 → `ActionParam`/`ActiveEvent` 注入。约 30 行重复代码，应抽取为 `BuildIntentContext(Hero npc, string actionParam)` 私有方法。

### 🟡 问题 3：认输对话无清理机制

`CombatManager` 两处注入后从不调用 `RemoveRelatedLines`。对比其他调用方：

| 调用方 | 清理策略 |
|--------|---------|
| `CrimeDialogueBehavior` | 注入前 RemoveRelatedLines ✅ |
| `ConversationEntryPatch` | 注入前 + 残留清理 ✅ |
| `MyBehavior` | 离开定居点时清理 ✅ |
| `CombatManager` | **无清理** ❌ |

### 🟡 问题 4：标签命名三套互不兼容的约定

| 来源 | 标签格式 | 可被 RemoveRelatedLines 清理 |
|------|----------|---------------------------|
| Crime | `crime_{EventId}` | ✅ 三处统一 |
| Alert | `AlertL3_{AgentName}` | ⚠️ 仅 AtomicAction |
| Surrender | `Surrender_Player_{Index}` / `Surrender_NPC_{Index}` | ❌ 无清理代码 |

建议统一为 `domain_subtype_id` 格式。

### 🟡 问题 5：CombatManager 硬编码中文字符串

认输台词（"喘着粗气，收起武器"、"算你识相。滚吧！"等）硬编码在 CombatManager 中。对比 `CrimeDialogueBuilder` 使用 `PlaceholderResolver` 做模板化。这些中文内容应可配置化。

### 🟢 问题 6：InjectScript vs InjectScriptAsOpening 选用规则无文档

两种模式语义不同（主动拦截 vs 被动选项），但选用规则散落在各处注释中，无集中说明。

## 四、改进优先级

| 优先级 | 问题 | 行动 |
|--------|------|------|
| P0 | 问题 1 | CombatManager 认输脚本抽取到 CrimeDialogueBuilder |
| P1 | 问题 2 | 抽取 BuildIntentContext 消除内部重复 |
| P1 | 问题 3 | 认输对话加 RemoveRelatedLines 清理 |
| P2 | 问题 4 | 统一标签命名规范 |
| P2 | 问题 5 | 认输台词参数化 |
| P3 | 问题 6 | 写选用规则文档 |

## 五、已修复记录

### 2026-07-10 第一轮

**问题 1（已修复）**：CombatManager 手动构建 DialogueInjectScript → 抽取到 `CrimeDialogueBuilder`。
- 新增 `CrimeDialogueBuilder.BuildPlayerSurrenderScript()` 
- 新增 `CrimeDialogueBuilder.BuildNpcSurrenderScript(string npcName)`
- CombatManager `PlayerSurrenderToAgent`: 80行 → 18行
- CombatManager `AcceptAgentSurrender`: 65行 → 18行

**调试日志去重（附加修复）**：`AtomicAction` 和 `CrimeDialogueBuilder` 中各有一套脚本结构打印逻辑，功能相同、格式略异。
- 新增 `DialogueInjector.LogScript(DialogueInjectScript script, string label)` 统一入口
- AtomicAction 和 CrimeDialogueBuilder 各删除 ~15行内联日志，改为一行调用
