# NPC 认知系统 — 最小化统一设计

> **状态**：设计阶段
> **动机**：现有 `KnownEvents` 缺"来源追溯"，无名 NPC 无认知。在现有基础上做最小加法，不推翻重来。
> **对标原则**：[design-philosophy.md](design-philosophy.md) 原则三。

---

## 问题诊断

现在 `KnownEvents` 只有三个字段：`EventId`、`PerceivedSeverity`、`DecayCounter`。

缺两样东西：
1. **不知道 NPC 怎么知道的**——亲眼所见？听人说的？酒馆流言？对话时无法区分"我亲眼看见的"和"好像听人提过"
2. **无 HeroId 的 NPC 完全没认知**——守卫、村民在玩家面前偷了东西也毫无反应

---

## 设计：两个最小加法

### 加法 1：KnownEvents 加一个 `Source` 字段

```csharp
// 现有 KnownEvent（NewsSpreadSystem 内），只加 2 个字段：
public class KnownEvent
{
    // --- 现有字段不变 ---
    public string EventId;
    public float PerceivedSeverity;
    public int DecayCounter;

    // --- 新增 ---
    public KnowledgeSource Source;        // 怎么知道的
    public string HeardFromHeroId;        // 如果 Source=HeardFrom，听谁说的（null = 不适用）
}

public enum KnowledgeSource
{
    DirectWitness,      // 亲眼所见 → 对话: "我看见了"
    HeardFrom,          // 听特定某人说的 → 对话: "老王告诉我的……"
    HeardAbout          // 传闻/流言/村里在传 → 对话: "听说……"
}
```

**就这些。** 不改 `KnownEvent` 的其他字段，不新增类。现有的传播/衰减/严重度逻辑全部复用。

### 加法 2：SettlementPublicMemory（无名 NPC 兜底）

```csharp
public class SettlementPublicMemory
{
    public string SettlementId;
    public List<KnownEvent> KnownEvents;  // 复用的同一个 KnownEvent，全村共享

    // 集体态度：全村人对特定 Hero 的共识（-1~+1）
    // 由 KnownEvents 自动推导，不需要手动维护
    public float GetAttitudeToward(string heroId);
}
```

无 HeroId 的 Agent 对话时，读 `SettlementPublicMemory.{当前村庄}.KnownEvents`，行为和普通 NPC 一样。

---

## 对话判定（极简版）

```
对话入口
  │
  ├─ 有 HeroId？→ 读 SingNpcMemorySystem.KnownEvents
  └─ 无 HeroId？→ 读 SettlementPublicMemory.KnownEvents
  │
  └─ 根据 Source 确定语气：
      DirectWitness → "我亲眼看见……"        （确定）
      HeardFrom(id) → "{id}告诉我的……"      （较确定）
      HeardAbout    → "听说……好像……"       （不确定）
```

不需要 Attitude 类、不需要 Concern 类、不需要公式。对话系统读到 KnownEvent 后自己判断怎么措辞。

---

## 与现有系统的关系

| 现有部件 | 改动 |
|---------|------|
| `KnownEvent` | 加 `Source` + `HeardFromHeroId` 两个字段 |
| `NewsSpreadSystem.BroadcastEvent` | 传播时设置 Source：当事人=DirectWitness，关系网传播=Rumor，写入村庄公共记忆=PublicKnowledge |
| `SingNpcMemorySystem.KnownEvents` | 不动，KnowEvent 结构变了自然跟着变 |
| `SettlementPublicMemory` | **新增**，一个 Dictionary<string, SettlementPublicMemory> 全局管理 |
| `HiddenAttitudeTowardPlayer` | **暂时不动**，后续改为从 KnownEvents 推导 |
| `ActiveConflict` / `CurrentUrgentEvent` 等 | **暂不动**，跟本次村庄偷窃系统无关 |
| LLM 对话记忆（RecentHistory 等） | **不动**，那是另一层 |

---

## 实施：只加 2 个文件

| 文件 | 内容 |
|------|------|
| `NpcCognition/SettlementPublicMemory.cs` | 约 50 行：SettlementPublicMemory 类 + 全局 Manager |
| （改动）`NewsSpreadSystem.cs` 的 `KnownEvent` | 加 2 个字段 + 1 个枚举 |

---

## 村庄偷窃场景验证

```
玩家偷羊，老王目击
  → 老王.KnownEvents.Add(new KnownEvent {
        Source = DirectWitness,
        PerceivedSeverity = 80,
        ...
    })

NewsSpreadSystem 传播
  → 老李（老王的朋友）.KnownEvents.Add(new KnownEvent {
        Source = HeardFrom, HeardFromHeroId = "老王",
        PerceivedSeverity = 80 × 0.5 = 40,
        DecayCounter = 1
    })

写入村庄公共记忆
  → SettlementPublicMemory["village_1"].KnownEvents.Add(new KnownEvent {
        Source = HeardAbout,
        PerceivedSeverity = 50,
    })

三天后玩家回村，跟守卫（无 HeroId）对话：
  → 守卫读 SettlementPublicMemory → 有 HeardAbout 记录
  → 对话："村里最近不太平，听说丢了牲口。你最好别惹麻烦。"
```
