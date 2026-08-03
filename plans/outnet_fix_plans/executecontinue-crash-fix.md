# ExecuteContinue NRE（结束对话时崩溃）— 诊断与修复计划

> **状态**：🟢 阶段一（诊断）完成；✅ 阶段二（防御修复）已实施（2026-08-03，修复 1+2 已编译通过），待本地实测 + 发玩家验证
> **来源**：外网玩家反馈 —— 结束对话（点"…/继续"按钮）时游戏报错
> **相关代码**：`Interaction/Dialogue/DialogueInjector.cs`（修复 1）、`Interaction/Dialogue/ConversationEntryPatch.cs`（修复 2）

---

## 1. 玩家报错原文

```
[ERROR] ：Exception occurred inside invoke: ExecuteContinue
Target type: TaleWorlds.CampaignSystem.ViewModelCollection.Conversation.MissionConversationVM
Argument count: 0
Inner message: Object reference not set to an instance of an object.
```

玩家补充：**点击了 `inj_opt_injectedStart` 和 `prison_break_player_meet` 两个选项后，出现"继续对话"，点击后报错**（发生在结束对话时）。

---

## 2. 诊断结论（已确认，2026-08-03）

### 2.1 报错格式 = 游戏原生崩溃报告，不是 Harmony

本地游戏日志（`Documents/Mount and Blade II Bannerlord/Configs/ModLogs/default20260731.log:991`）有同格式记录：

```
[FTL]: Crash Report: Exception occurred inside invoke: ExecuteAction
Target type: TaleWorlds.CampaignSystem.ViewModelCollection.Conversation.ConversationItemVM
```

这是 `MBDebugManager` 对 **ViewModel 命令调用**的崩溃报告。所以：`MissionConversationVM.ExecuteContinue`（原版对话 UI 的"…/继续"按钮）执行时抛了 NRE，无需第三方 Harmony 补丁参与。

### 2.2 反编译确认的调用链与 NRE 点

`ExecuteContinue`（`TaleWorlds.CampaignSystem.ViewModelCollection.dll`）：

```csharp
public void ExecuteContinue()
{
    Debug.Print("ExecuteContinue");
    _conversationManager.ContinueConversation();   // ← NRE 发生在这里
    _isProcessingOption = false;
}
```

`ContinueConversation`（`TaleWorlds.CampaignSystem.dll`，反编译）：

```csharp
public void ContinueConversation()
{
    if (CurOptions.Count > 1) return;
    if (IsConversationEnded()) { EndConversation(); return; }   // ActiveToken==4 (close_window) → 正常结束
    if (!ProcessPartnerSentence() && ListenerAgent.Character == Hero.MainHero.CharacterObject)  // ← 唯一 NRE 点
    { EndConversation(); return; }
    DoConversationContinuedCallback();
    if (CampaignMission.Current != null) { CampaignMission.Current.OnConversationContinue(); }
}
```

**NRE 条件**：`ListenerAgent`（即 `ConversationManager._listenerAgent`）为 null，且当前 token 没有下一条 NPC 台词（`ProcessPartnerSentence()` 返回 false）。

`_listenerAgent` 只在两种情况下为 null：
1. **对话已被拆解**（`EndConversation` 已把 `_listenerAgent/_mainAgent` 置 null、`_conversationAgents.Clear()`），"…"按钮仍可点（双击 / UI 关闭动画期间点击）→ 二次进入 `ContinueConversation` → `ProcessPartnerSentence()` 找不到句子 → 撞 `ListenerAgent.Character` → NRE
2. 某句台词的 `IsListener` 委托与对话 agents 不匹配，listener 从未被设置（vanilla 台词恒匹配；**LLM/第三方动态台词可能不匹配**）

### 2.3 触发机制：死胡同 token

对话流走到一个**死胡同状态**——当前 `ActiveToken` 没有注册任何玩家选项、也没有下一条 NPC 台词、且 `!= close_window`——UI 弹出"…/继续"，点击即走上面的 NRE 路径。

我们的注入脚本（`DialogueInjector`）产生死胡同的路径：
- `RegisterDirectTransition`（DialogueInjector.cs:662-676）：`NextNodeOnSuccess` 引用脚本里**不存在的节点 Id** → 选项输出 token 上没有任何 NPC 台词 → 死胡同
- `RegisterSkillCheckTransition`（DialogueInjector.cs:679-726）：consequence 里 `cm.ActiveToken = cm.GetStateIndex(NodeToken(fileTag, dest))` — `GetStateIndex` 对未知 token **自动注册**（不返回 -1），但那个 token 上没有 NPC 台词 → 死胡同
- 当前各 Builder（CrimeDialogueBuilder 等）实测图是干净的（13:02 本地日志），悬空引用是潜在缺口，未实际触发

### 2.4 两个选项 ID 的来源（回答玩家问题）

| ID | 来源 | 说明 |
|---|---|---|
| `inj_opt_injectedStart` | **我们的 mod** | `DialogueInjector.cs:671/689`：`$"inj_opt_{node.Id}"`，节点 Id = `injectedStart`（默认入口节点）。**同一个节点的所有选项共用一个 ID**（引擎靠 SentenceNo 区分）。玩家从游戏日志看到——原版每处理一个选项打印 `P -> (id) - 文本` |
| `prison_break_player_meet` | **非本 mod，出处未锁定** | 全库搜索无此字符串（LivingWorldNpcs / TaikouContent / 原版 XML 均无）。~~[deepseek]AIChat~~ **已排除**（玩家 2026-08-03 提供 mod 列表 20 个，未装该 mod）。玩家机器上无静态来源 → 大概率是第三方 mod 运行时动态生成的对话节点（如 FQ_Editor2 等动态对话类），或玩家对日志 ID 的转述误差。**不再追查——兜底修复对所有来源的死胡同都生效** |

### 2.5 排除项（已核查）

- `prison_break` CommissionDef（`CommissionData.cs:237`）：只提供元数据（标题/技能/职业/路径），**不生成任何对话节点**；委托对话走 `NarrativeResolver` → Narrative.csv（不在仓库）。与玩家点击的节点无关
- 我们 mod 无 `ExecuteContinue` / `MissionConversationVM` patch；无 `ConditionRunned/ConsequenceRunned` 订阅；`EndConversation` Postfix 全部有 try/catch（即使抛错也会报成 `EndConversation` 而非 `ExecuteContinue`）

### 2.6 最可能的完整因果链

玩家对话混入了我们注入线之外的第三方动态对话（`prison_break_player_meet` 出处未锁定，玩家 20 个 mod 中可能有动态对话类）。第三方 LLM/动态对话图结尾断裂（悬空引用 / 对话中途改图 / IsListener 不匹配），把 vanilla 对话机停在死胡同 → "…" → `ContinueConversation` → NRE。

**决策（2026-08-03）**：玩家装什么 mod 无法控制 → 不做来源追查，做**兜底修复**（修复 1 治本：我们自己的图不可能再造死胡同；修复 2 治标：任何来源的死胡同/二次结束都变成干净收场）。

---

## 3. 修复计划（✅ 已实施，2026-08-03）

### ✅ 修复 1：DialogueInjector 悬空节点校验（治本，防我们自己的图造死胡同）

位置：`DialogueInjector.cs` — `RegisterDirectTransition` / `RegisterSkillCheckTransition` / 新增 `CollectNodeIds` + `ResolveDestinationNode`。

实施：
- `InjectScript` / `InjectFromJson` 入口先 `CollectNodeIds(script)` 收集节点 Id 集合（null 过滤）
- `RegisterTransition` 链（含 RegisterNodeTransitions / RegisterDirectTransition / RegisterSkillCheckTransition）穿参 `HashSet<string> nodeIds`
- `RegisterDirectTransition`：`NextNodeOnSuccess` 悬空 → DebugLogger 警告 + 改走 `close_window`
- `RegisterSkillCheckTransition`：consequence 中 `dest` 悬空 → 警告 + `cm.ActiveToken = GetStateIndex("close_window")`
- 空/未配 `NextNodeOnSuccess` 维持原行为（本来就是关窗），零行为变化

### ✅ 修复 2：`ContinueConversation` Prefix 兜底（治标，防一切死胡同/二次结束）

位置：`ConversationEntryPatch.cs` 新增 `ContinueConversationGuardPatch`。

实施：
- `[HarmonyPatch(typeof(ConversationManager), nameof(ConversationManager.ContinueConversation))]` Prefix
- 兜底条件：`ListenerAgent == null && !IsConversationEnded() && (CurOptions == null || CurOptions.Count <= 1)` → 引擎状态已坏 → `__instance.EndConversation()` + `return false`，把 NRE 变成干净收场
- 正常对话 `_listenerAgent` 恒非 null，此分支只命中异常状态；整个 Prefix 包 try/catch，出错放行原方法
- 双版本签名一致（无参方法），无需 `#if`；本机 Latest 编译通过（0 警告 0 错误）

### 修复 3（可选，暂缓）：`ExecuteContinue` 层面兜底

`MissionConversationVM.ExecuteContinue` Prefix：先查 `ConversationManager.IsConversationEnded() || ListenerAgent == null`，是则跳过原方法。优先级低于修复 2（patch 原版 VM，改动面大），修复 2 已覆盖其场景，暂不实施。

---

## 4. 验证计划

| # | 操作 | 预期 | 状态 |
|---|---|---|---|
| 1 | 本地：犯罪对话完整走一遍（接案→结案/认输/走人各分支）后点"…"结束 | 无 NRE，正常关窗 | ⬜ 待实测 |
| 2 | 本地：对话结束瞬间双击"…" | 无 NRE（修复 2 兜底生效） | ⬜ 待实测 |
| 3 | 构造测试：注入一个 `NextNodeOnSuccess` 指向不存在节点的测试 JSON（`custom.inject_dialogue`） | 日志出现 `[DialogueInjector] ⚠️ 悬空节点引用` 警告，对话走 close_window 正常结束 | ⬜ 待实测 |
| 4 | 双版本编译（1.2.12 / Latest） | 编译通过，行为一致 | 🟡 Latest 已过；1.2.12 待另机 |
| 5 | 发测试版给反馈玩家 → 确认崩溃消失 | 玩家确认 | ⬜ 待发 |

---

## 5. 待收集信息（✅ 已收齐，无需再追）

- [x] 玩家 mod 列表（2026-08-03）：20 个 mod，无 LLM 对话类 —— `prison_break_player_meet` 来源不再追查，兜底修复覆盖
- [ ] 玩家验证修复版后反馈（崩溃是否消失、是否出现"对话被强制结束"的日志）

---

## 6. 教训与后续纪律

- **动态对话图必须校验引用完整性**：LLM/数据驱动的节点图注入引擎前，先做节点引用校验（对 `NextNodeOnSuccess/OnFail` 的悬空检测），写进 wheels.md「对话注入」章节
- **与第三方 LLM 对话 mod 共存**：两个 mod 同时注入 vanilla ConversationManager 时，speaker/listener 全局状态互相覆盖，死胡同 NRE 是典型症状。修复 2 的兜底对所有 mod 的死胡同都生效
