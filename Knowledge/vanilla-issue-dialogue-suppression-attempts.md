---
name: vanilla-issue-dialogue-suppression-attempts
description: 尝试屏蔽原版 Issue 对话入口句（"我听说你有个问题需要帮助。"）的完整失败记录
metadata:
  type: project
---

# 背景

当 `hero.Issue` 非空（我们的 `CommissionHubIssue` 已分配）时，原版引擎会在 `hero_main_options` 下自动注册 Issue 入口句 `"我听说你有个问题需要帮助。"`，与我们的犯罪对话入口 `"村长，听说特维亚出了点事？"` 同时出现。目标是在玩家看到选项前删除原版句，只保留我们的入口。

---

# 引擎架构关键认知（踩坑后总结）

## _sentences 是全局池

1444 条句子不属于任何特定 NPC。引擎靠 `_tags`（95 个条件标签）在运行时过滤——满足条件的句子才显示，不满足的不显示。

## 下游懒加载

对话图不是一次性展开的。`hero_main_options` 显示时，只有玩家选项句在 `_sentences` 里，NPC 回应句要等玩家点击后才被创建。这意味着无法通过"看下游内容"来预判一个选项句的身份。

## hero.Issue 不能临时置空

`Hero.Issue` 是 `public IssueBase Issue { get; private set; }`。可以通过 `Traverse` 反射写入绕过 `private set`，但**置空会导致游戏崩溃**：

```
System.ArgumentException: An item with the same key has already been added.
   at IssueManager.CreateNewIssue(PotentialIssueData& pid, Hero issueOwner)
   at IssuesCampaignBehavior.OnSettlementTick(...)
   at Campaign.Tick()
```

根因：`Campaign.Tick()` 在对话期间持续运行。`IssuesCampaignBehavior.OnSettlementTick` 发现 `hero.Issue == null`，认为"这个名人没有 Issue"，尝试通过 `IssueManager.CreateNewIssue()` 创建新 Issue。但我们的 `CommissionHubIssue` 还在 `IssueManager` 内部注册表中（只是 `hero.Issue` 属性被我们置空了），**Key 冲突导致崩溃**。

**结论：Null Issue 这条路彻底死了。** 不是时机问题，是引擎设计就不允许 `hero.Issue` 为 null 的同时 IssueManager 内部还有该 Hero 的活跃 Issue。

---

# 尝试过的路径

## 路径 1：Prefix → Postfix 迁移

**原理**：`ProcessSentence` 的 Harmony Patch 从 Prefix 改为 Postfix，使 `_sentences` 在扫描时已包含当前 token 内容。

**结果**：时序正确（`scanned=64`），但匹配逻辑失败（`removed=0`）。Postfix 时序没问题，问题在怎么识别目标句。

## 路径 2：增强诊断日志

**改动**：`_firstCleanupLogged` 延迟到 `hero_main_options` 出现时采样；每轮扫描打印 `scanned=X removed=Y`；未匹配候选句打印下游文本和 token。

**结果**：诊断日志帮助发现了路径 3 的根因。

## 路径 3：图遍历匹配（下游文本 Contains 匹配）

**原理**：扫描 `hero_main_options` 下的玩家句 → 沿 `OutputToken` 找下游 NPC 回应 → 与 `hero.Issue` 的 Title/IssueBrief 做 `Contains` 匹配 → 匹配到则删除。

**结果**：**失败**。关键日志：
```
candidate '我听说你有个问题需要帮助。' outToken=333 → no downstream found in textByToken
```
下游懒加载使此方案从根本上不可行。玩家点击后下游才出现，此时删除已太晚。

## 路径 4：临时置空 hero.Issue（ProcessSentence Prefix）

**原理**：`ProcessSentence` Prefix 中置空 `hero.Issue`，Postfix 恢复。

**结果**：第一次 `hero_main_options` 时原版句仍在（太晚），但**第二次回到 `hero_main_options` 时原版句消失了**——证明"构建时 Issue 为 null → 不生成原版句"这个方向是对的。

## 路径 5：延迟恢复到对话结束（EndConversation Postfix）

**原理**：`OpenConversation` Postfix 置空，`EndConversation` Postfix 恢复。覆盖整个对话周期。

**结果**：**游戏崩溃**。`Campaign.Tick()` 在对话期间继续运行，`OnSettlementTick` 发现 `hero.Issue == null`，触发 `IssueManager.CreateNewIssue()`，Key 冲突。

---

# 死路总结

| 方案 | 失败原因 |
|------|---------|
| _sentences 层面删除（路径 1-3） | 下游懒加载 + 全局池无 NPC 隔离 |
| 临时置空 hero.Issue（路径 4-5） | 引擎设计不允许：对话期间 Campaign.Tick 运行，IssueManager 状态不一致导致崩溃 |

# 可行的下一步方向（✅ 已实施）

**不碰 `hero.Issue`，而是 Patch 对话引擎中添加 Issue 入口句的具体方法。** 需要反编译定位原版引擎中"检测到 `hero.Issue != null` 后向对话图注册 '我听说你有个问题需要帮助。' 及其下游句子"的代码路径，然后 Harmony Prefix 拦截。

可能的切入点：
- `ConversationManager` 中负责 `hero_main_options` 句子填充的方法（需要反编译 `ProcessSentence` 内部调用链）
- `AddPlayerLine` / `AddDialogLine` 中与 Issue 相关的分支
- 可能在 `Campaign.Current.ConversationManager` 初始化时通过某种 `ConversationSentence` 工厂方法注册

# 反编译定位结果（2026-07-02）

## 关键代码路径

**DLL**: `TaleWorlds.CampaignSystem.dll`
**Namespace**: `TaleWorlds.CampaignSystem.CampaignBehaviors`
**Class**: `LordConversationsCampaignBehavior`
**Method**: `conversation_hero_main_options_have_issue_on_condition()` (private instance)

## 句子注册

在 `AddHeroGeneralConversations()` 中：
```csharp
starter.AddPlayerLine(
    "hero_give_issue",                                          // ID
    "hero_main_options",                                        // InputToken
    "issue_offer",                                              // OutputToken
    "{=Kfbqriuh}I heard you may need some help with a problem?", // Text
    conversation_hero_main_options_have_issue_on_condition,      // OnCondition delegate
    null,                                                       // OnConsequence
    110,                                                        // Priority
    conversation_hero_main_options_have_issue_on_clickable_condition  // ClickableCondition
);
```

## 条件方法源码

```csharp
private bool conversation_hero_main_options_have_issue_on_condition()
{
    if (Hero.OneToOneConversationHero == null || Hero.OneToOneConversationHero.IsPrisoner)
        return false;
    IssueBase issue = Hero.OneToOneConversationHero.Issue;
    if (Hero.OneToOneConversationHero != null && issue != null)
        return issue.IsOngoingWithoutQuest;
    return false;
}
```

当 `hero.Issue is CommissionHubIssue` 且 `IsOngoingWithoutQuest == true` 时，此方法返回 `true`，
导致 "我听说你有个问题需要帮助。" 出现在 `hero_main_options` 中。

## 实施方案

Harmony Prefix 拦截 `conversation_hero_main_options_have_issue_on_condition`：
- 如果 `hero.Issue is CommissionHubIssue` → 强制返回 `false`，阻止原版 Issue 入口句出现
- 否则 → 放行，原版 Issue 正常显示

优势：
- 不碰 `hero.Issue` 引用（避开 IssueManager 状态不一致崩溃）
- 不依赖 `_sentences` 扫描和下游文本匹配（避开懒加载问题）
- 对原版 Issue 零影响

实现位置：[SuppressVanillaIssueConditionPatch](ExampleModVS/ExampleMod/ExampleMod/Interaction/Dialogue/ConversationEntryPatch.cs)

# 代码位置

- 主文件：[ConversationEntryPatch.cs](ExampleModVS/ExampleMod/ExampleMod/Interaction/Dialogue/ConversationEntryPatch.cs)
- 相关：`CrimeDialogueBuilder`, `DialogueInjector`, `CommissionHubIssue`
