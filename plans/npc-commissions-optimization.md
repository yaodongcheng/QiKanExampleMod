# 委托系统优化方案（8 点）

> **上游设计**：本方案是对 [npc-commissions.md](npc-commissions.md) 已实现效果的优化迭代。
> **状态**：Point 7（日志）已部分实施（5 条视线日志已删，~6 条委托日志已加）。

---

## 用户反馈 → 优化一览

| # | 问题 | 解法 |
|---|------|------|
| 1 | NPC 没感叹号 | `SettlementEntered` 钩子 + 强制 issue 激活 |
| 2 | 直接委托人该在对话里说，不弹窗 | 对话系统中展示叙事文本 |
| 3 | 报酬直接到账太假 | 改回委托人处领取，支持结账人≠委托人 |
| 4 | 中转人列表太机械化 | 删除总览，改为"信"格式逐条浏览 |
| 5 | 目标十万八千里 | 距离加权随机选取 |
| 6 | 村防贿赂没实现 | 地图遭遇 → 3D 对话场景 → 贿赂/战斗选择 |
| 7 | 日志缺失 + 视线日志刷屏 | 删视线日志 + 补全委托全链路日志 |
| 8 | 叙事质量 | CSV 驱动，接取+结账双阶段，性格×信任×评级匹配 |

**三条设计原则**（用户明确要求）：
- 接取和结账叙事同等重要
- 先做纯模板版（无 LLM），跑通后再接入 LLM 润色
- 叙事配置不进代码，放 CSV

---

## Point 1：修复 NPC 感叹号不显示

**根因**：`OnCheckForIssueEvent` 只对领主/要人等特定 NPC 类型触发。商人、工匠、浪人等有效委托发布者永远不会进这个事件。

**修改文件**：`CommissionHubIssue.cs`

1. 新增 `CampaignEvents.SettlementEntered` 钩子，玩家进定居点时遍历所有名人/领主/浪人，主动调 `OnCheckForIssue`
2. `AddPotentialIssueData` 后如果 `hero.Issue` 仍为空，直接实例化赋值
3. `IssueStayAliveConditions` 加入 `HasCommissionsFor` 检查

---

## Point 2：直接委托人对话叙事

**目标**：直接委托人当面说委托时，不走弹窗，在对话系统中展示故事。

**修改文件**：`CommissionIntent.cs`

- `RequestCommissionIntent.OnInstant`：检测是直接委托人 → 用 `StoryDialogVM` 设置叙事文本（来自 `CommissionNarrative.BuildOpening`）
- 委托选项直接以对话菜单项呈现

---

## Point 3：报酬领取 + 结账人分离

**修改文件**：`CommissionData.cs`、`CommissionQuest.cs`、`CommissionIntent.cs`、`IntentRegistry.cs`

- `CommissionData` 新增 `[SaveableField(53)] Hero RewardPayer`（默认 = QuestGiver）
- `UpdateProgress`：满进度时不再立即 `CompleteQuestWithSuccess`，改为设 `IsObjectivesComplete = true` + 日志提示"返回结账人处领报酬"
- 新增 `CompleteWithRewardCollection()`：包含原 `OnCompleteWithSuccess` 的全部结算逻辑
- 新增 `CollectCommissionRewardIntent`：与结账人对话时出现，点击"领取"才转账
- 生成委托时 90% `RewardPayer = QuestGiver`

---

## Point 4：中转人委托列表 → 信格式

**修改文件**：`CommissionIntent.cs`

- 删除 `ShowOverviewThenBrowse()` 总览页
- 改为 `ShowCommissionLetter(index)`：一页一信，左"接取"右"下一个→"（末尾"合上"）
- 叙事内容用 `CommissionNarrative.BuildOpening`（来自 Point 8 的 CSV 模板）

---

## Point 5：委托目标就近选取

**修改文件**：`CommissionGenerator.cs`

- `FillTargetSettlement`：排序公式 `distance * (0.5f + Random * 1.5f)`，护送/供货类 top 3 最近 60% 概率
- `FillTargetHero`：同公式，用目标当前位置/家乡算距离
- 村防类严格限制为附近村庄

---

## Point 6：大世界遭遇 → 3D 对话场景 → 贿赂

**核心洞察**：原生游戏 `CampaignMission.OpenConversationMission` 可从地图遭遇加载 3D 对话场景。mod 的 `InteractionMissionView` 已在 `MissionConversationLogic.OnAgentInteraction` 打了 Harmony 补丁，进了 3D 场景就能接管。

**流程**：
```
地图遭遇匪徒 → MapEventStarted 拦截 → OpenConversationMission → 3D 场景
→ InteractionMissionView 初始化 → ForceTalkAction 自动触发对话
→ 玩家选"贿赂"或"战斗" → 结局
```

**修改文件**：`CommissionQuest.cs`（+ 可能需要 `AtomicAction.cs` 新增 ForceTalk 入口）

**待验证**：`CampaignMission.OpenConversationMission` 精确签名（需 ilspycmd 反编译确认）

---

## Point 7：日志大修（已部分实施）

**已完成**（仅删日志行，文件本身保留不变）：
- `NpcSightSystem.cs`：删除了 3 条 `[NpcSight]` 日志 + `_debugTickCount` 字段
- `InteractionMissionView.cs`：删除了 2 条 `[SightBubble]` 日志

**已添加**（~6 条委托日志）：
- `CommissionHubIssue.cs`：OnCheckForIssue
- `CommissionGenerator.cs`：HasCommissionsFor、GenerateCommissions、GenerateCommissionData
- `CommissionIntent.cs`：RequestCommission Evaluate、OnInstant、AcceptCommission

**待添加**（`CommissionQuest.cs` 全生命周期 ~14 条）：启动、确认、完成、超时、失败、每日、胜利、部队生成、变故、旅途事件

---

## Point 8：CSV 驱动的 NPC 第一人称叙事

### 现状：`CommissionNarrative.cs` 只有"系统旁白"

| 现有方法 | 谁对谁说 |
|----------|---------|
| `GetIntroduction()` | 系统→玩家 |
| `CheckTrustMilestone()` | 系统→玩家 |
| `CheckTierUnlock()` | 系统→玩家 |
| `GetPlayerStatusHeader()` | 系统→玩家 |

**完全没有 NPC 第一人称。**

### 新增方法到 `CommissionNarrative.cs`

```csharp
// 构建接取开场叙事（NPC 第一人称）
// 从 CSV 按 Category + 性格 + 信任 匹配模板，替换占位符
public static string BuildOpening(CommissionData data, NPCProfile giverProfile)

// 构建结账结局叙事（NPC 第一人称）
// 按 Category + 性格 + 信任 + 评级 匹配。结账人≠委托人时叠加 payer 台词
public static string BuildClosure(CommissionData data, NPCProfile giverProfile,
                                   NPCProfile payerProfile, CommissionGrade grade)
```

### 调用点

| 场景 | 调用 | 调用位置 |
|------|------|---------|
| 直接委托人当面说 | `BuildOpening` | `RequestCommissionIntent.OnInstant` |
| 中转人看的信 | `BuildOpening` | `ShowCommissionLetter` |
| 找委托人当面确认 | `BuildOpening` | `ConfirmCommissionIntent.OnInstant` |
| 回来领报酬 | `BuildClosure` | `CollectCommissionRewardIntent.OnInstant` |

### CSV 结构：`ModuleData/DesignData/CommissionNarrative.csv`

| 字段 | 用途 |
|------|------|
| `ID` | 唯一标识 |
| `Category` | `BountyHunt` / `VillageDefense` 等 |
| `Phase` | `Opening`（接取）/ `Closure`（结账） |
| `PersonalityTrait` | 匹配 `NPCProfile` 性格标签，`Any` 兜底 |
| `TrustMin` / `TrustMax` | 信任度区间（0-100） |
| `Grade` | 仅 Closure：`Perfect` / `Good` / `Passable` / `Failed` |
| `Text` | 模板，占位符：`{TARGET}` `{LOCATION}` `{ITEM}` `{REWARD}` `{DEPOSIT}` `{GIVER}` |
| `Emotion` | `urgent` `sad` `grateful` `impressed` `disappointed` `normal` |

### 匹配逻辑

```
BuildOpening: Category 精确 > PersonalityTrait 精确 > Trust 区间 > 随机选一 > 替换占位符
BuildClosure: Category + Grade 精确 > PersonalityTrait 精确 > Trust 区间 > 随机选一 > 替换占位符
```

### 模板数量目标

~16 Category × 每种至少 Opening 3 变体 + Closure 4 变体 ≈ **112 条模板**

---

## 实施顺序

| 顺序 | 点 | 理由 |
|------|-----|------|
| 1 | Point 7 收尾 | 剩余 CommissionQuest 日志，快速完成 |
| 2 | Point 5 | 纯数据层，无副作用 |
| 3 | Point 8 | CSV 叙事模板 → Point 2/3/4 都依赖它 |
| 4 | Point 2+4 | 委托 UI/对话流（中转→信，直接→对话叙事） |
| 5 | Point 1 | "!" 标记修复 |
| 6 | Point 3 | 结账分离 + 报酬领取，依赖 Point 8 的 Closure 模板 |
| 7 | Point 6 | 最复杂：需反编译 + 遭遇→对话桥接 |

---

## 涉及文件

| 文件 | Points |
|------|--------|
| `Quests/Commissions/CommissionQuest.cs` | 2, 3, 6, 7 |
| `Quests/Commissions/CommissionIntent.cs` | 2, 4, 7 |
| `Quests/Commissions/CommissionGenerator.cs` | 5, 7 |
| `Quests/Commissions/CommissionHubIssue.cs` | 1, 7 |
| `Quests/Commissions/CommissionNarrative.cs` | 8（新增 BuildOpening / BuildClosure 两个方法） |
| `Quests/Commissions/CommissionData.cs` | 3（新增 RewardPayer、IsObjectivesComplete 字段） |
| `Interaction/Intents/IntentRegistry.cs` | 3（新增 CollectCommissionRewardIntent 注册） |
| **新增** `ModuleData/DesignData/CommissionNarrative.csv` | 8 |
| `Core/GameDatabase.cs` / `Data/DesignDataLoad.cs` | 8（注册新表） |
| `AI/NpcSightSystem.cs` | 7（仅删 3 行日志，文件其余不变） |
| `Interaction/InteractionMissionView.cs` | 7（仅删 2 行日志，文件其余不变） |
