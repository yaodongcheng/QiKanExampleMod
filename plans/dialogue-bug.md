# 对话注入"选项消失"Bug — 未解决备忘

> **日期**：2026-07-29 上午
> **状态**：已回退，未解决
> **关联**：[[dialogue-injector-audit-没改完待继续]] [[crime-dialogue-flow-fix]]

---

## 现象

NPC 犯罪对话（Alert 质问 / 普通犯罪对话）注入后，NPC 开场白播放完毕，轮到玩家选项时——**选项不显示**（玩家卡在对话里，没有任何可点的选项）。

不是每次都触发，但模板 NPC（村民/守卫，HeroObject==null）的 Alert 强制对话路径高概率复现。

---

## 排查过程 & 尝试的修复

### 修复 1：DialogueInjector 注入顺序 — 两阶段

**旧逻辑**：
1. 先 `AddNodeNpcLine`（注册 NPC 台词）
2. 再 `RegisterNodeTransitions`（注册玩家选项）

**假设**：引擎按 token 顺序评估，NPC 台词挂在前面的 token 上，引擎走到 NPC 台词 token 时还没看到后面注册的玩家选项 → `CurOptions=0`。

**修复**：两阶段——
1. Phase 1：遍历全部 node，**先注册玩家选项**（`RegisterNodeTransitions`），非 terminal 节点收集到 `deferredNpcNodes`
2. Phase 2：再注册全部 NPC 台词（`AddNodeNpcLine`）

结果：**没解决。**

---

### 修复 2：BaseLabel 去版本化

**旧逻辑**：`BaseLabel = fileTag`（含 `_v{N}` 版本后缀）。

**问题**：`RemoveRelatedLines` 按 `BaseLabel` 匹配清理旧线。BaseLabel 如果带版本号，清理代码永远匹配不上，NoOpening 旧线只增不减——残留线可能干扰引擎 token 路由。

**修复**：`BaseLabel = baseLabel`（不带版本号），`FileName` 保留版本号用于调试。

结果：**没解决**（但这是正确改动，应保留）。

---

### 修复 3：防重复注入从精确匹配改为布尔锁

**旧逻辑**：`_lastInjectedEventId == eventKey + "_" + partnerKey`（精确匹配 event+partner）。

**问题**：同一场对话中 `MissionStartPrefix` 和 `MissionStartPostfix` 两次调用，partner 解析可能不一致（ConversationAgent 过期/null → `"(template)"` vs Hero.StringId），匹配失效 → 走到步骤 6 的 `RemoveRelatedLines` → **把正在播放的注入脚本整棵树删掉**。实测 Hero Alert 质问开场白播完后玩家选项全部消失——这是最可能的主因。

**修复**：`_lastInjectedEventId` 只要非 null 就整体跳过，不再做精确匹配。

结果：**没解决**（但确实阻止了重复注入删树的问题）。

---

### 修复 4：模板 NPC start token 直挂注入

**旧逻辑**：模板 NPC（partner==null）Alert 一律不消费 trigger，留给 `ProcessSentence` Postfix 延迟注入（deferred 路径）。

**问题**：deferred 路径在开场白播放后才注入，开场白不被跳过，且 gateway 入口文本依赖 `script.EntryOption`——Alert 脚本不设 EntryOption，显示调试占位符。

**假设**：start token 挂 NPC 台词不依赖 `hero_main_options`（优先级 200 碾压原版 `town_or_village_start` 优先级 100），可以跳过开场白直接让 NPC 说质问台词。

**修复**：新增 `allowTemplateNpcStartTokenInjection` 参数，`MissionStartPrefix` 拿到有效对话 Agent 时传 true 走直挂。

结果：**没解决**（注入成功了但选项还是消失）。

---

### 修复 5：CrimeDialogueBuilder Agent 解析

**旧逻辑**：`ConversationManager.OneToOneConversationAgent`。

**问题**：Alert 强制对话在 `StartConversation` 之前注入，此时 `OneToOneConversationAgent` 尚未就位或残留上轮值。

**修复**：优先 `AlertForceConversationAction.ActiveConversationAgent`。

结果：**没解决**（Agent 解析正确了，但不影响选项消失问题）。

---

### 诊断工具：ConversationOptionDiagPatch

新增 [ConversationOptionDiagPatch.cs](../../ExampleModVS/ExampleMod/ExampleMod/Interaction/Dialogue/ConversationOptionDiagPatch.cs)，挂 `ConversationManager.GetPlayerSentenceOptions` Postfix，每次引擎询问选项时打印 `ActiveToken`、`CurOptions.Count`、前 6 个选项文本。

目的：区分 H-A（选项注册成功但被引擎/UI 吞了）和 H-B（根本没注册上）。

**关键发现**：Patch 打出日志显示 `CurOptions=0` 且 token 停在原版 `start`/`town_or_village_start`——说明**引擎根本没走到我们注入的 token**，不是选项被过滤，是路由就没过去。

---

## 仍未解决的疑问

1. **为什么引擎停在 `start` token 不走我们的注入 token？** 优先级 200 的 NPC 台词挂在 `start` token 上，理论上应该碾压原版开场白。但实际上引擎可能走了不同的分支（`town_or_village_start` vs `start` 是不同的入口？）

2. **模板 NPC 的对话树根 token 到底是什么？** 有名有姓的 Hero 走 `hero_main_options`，模板 NPC 走的可能是另一套 token 体系。

3. **`AddDialogFlow` 的时机窗口**：引擎什么时候"锁定" token 评估？如果在 `StartConversation` 之后注入，引擎可能已经评估过 `start` token 并缓存了结果。

4. **引擎内部 token 评估机制**：`ConversationManager.ProcessSentence` 内部的 token 状态机（见 `Knowledge/原版对话流引擎逆向分析.md`）——注入的句子是否在引擎的 `_sentences` 大表中被正确索引？

---

## 回退操作

```bash
git checkout -- ExampleModVS/ExampleMod/ExampleMod/Interaction/Dialogue/DialogueInjector.cs
git checkout -- ExampleModVS/ExampleMod/ExampleMod/Interaction/Dialogue/ConversationEntryPatch.cs
git checkout -- ExampleModVS/ExampleMod/ExampleMod/Interaction/Dialogue/CrimeDialogueBuilder.cs
git checkout -- ExampleModVS/ExampleMod/ExampleMod/ExampleMod.csproj
rm ExampleModVS/ExampleMod/ExampleMod/Interaction/Dialogue/ConversationOptionDiagPatch.cs
```

---

## 下一步建议

1. **先搞清楚模板 NPC 的对话树入口 token**——用 `ilspycmd` 反编译 `ConversationManager` 看 `town_or_village_start` / `village_start` / `start` 的分支逻辑，确认模板 NPC 到底走哪个 token。
2. **读 `原版对话流引擎逆向分析.md`** 里关于 `_sentences` 大表索引的部分，理解注入的句子什么时候被引擎"看到"。
3. **考虑不走 token 注入，改在 `ProcessSentence` 更早阶段（如 FindValidNode）拦截**——可能比跟原版 token 体系抢路由更可靠。
4. **最小化复现**：写一个最简单的测试（一个 NPC 台词 + 一个玩家选项），不涉及犯罪系统，纯粹测试 `DialogueInjector.InjectScriptAsOpening` 在模板 NPC 上是否工作。
