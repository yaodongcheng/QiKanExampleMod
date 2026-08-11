# 废弃系统速查（🔴 尽量别碰）

> **原则**：这些系统已废弃。**不要在它们上面加功能**；修 bug 前先确认该路径是否还在被调用——废弃路径上的「修复」往往只是给死代码续命。
> 现行对话 = **原版对话流（vanilla ConversationManager）+ 新版 IM chat + AgentSay**（DialogueComponent 统一台词管线，见 [dialogue.md](dialogue.md)）；切磋/打架 = **CombatManager**（StartFight 多队模型）。
> 登记时间：2026-08-11（用户裁定）。

| 废弃系统 | 关键文件/入口 | 替代方案 | 已知坑 |
|---------|--------------|---------|--------|
| **旧对话 UI：StoryDialogVM / DialogChoice.xml** | `InteractionMissionView._dialogueLayer/_dialogueVM`（Mission 启动即创建）、`InteractionController._vm`、事件链 `_vm.Close()` → `OnDialogClosed` → `OnDialogueEnded` → `GenerateEventAsync` | 原版对话流 + IM chat + AgentSay | 🔴 **IM 弹窗确认路径（ATTACK/DUEL/KNOCKOUT/STEAL 的 confirmFight）不得调 `_vm.Close()`**——`_memory` 只在 `StartInteraction` 赋值，无当面对话时 Close 触发 OnDialogClosed → `GenerateEventAsync` 第 2600 行 `_memory.DynamicMemories` → **NullReferenceException**（实机 2026-08-11 11:13:37 崩溃实录）。确认回调只做现行事（`ChatActionFlow.TryExecute` → CombatManager） |
| **旧切磋 UI：DuelMissionView / DuelUI（DuelVM）** | `Combat/DuelMissionView.cs`（Layer 100，StartDuelUI） | `CombatManager.StartFight`（拆队模型：队2 玩家侧/队3 敌方/队4 切磋；`_originalTeams` 恢复） | 无人调用（控制台遗留），勿接新功能 |

**通用检查**：改到这些文件/字段（`InteractionMissionView._dialogueVM`、`InteractionController._vm`、`StoryDialogVM`、`DuelMissionView`、`OnDialogueEnded`、`GenerateEventAsync`）前，先确认调用点是否还在现行路径上——IM 弹窗确认、IM 闲聊动作、CombatManager 之外的调用一律视为死代码，不修不扩。
