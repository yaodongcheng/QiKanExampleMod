### 复用部分已有系统

## 零-B、偷窃当场检测：统一目击系统

**偷动物和偷村民装备都属于偷窃行为，在偷窃动作当场就需要检测目击者。**
目击者看到 → 当场锁定嫌犯。没人看到 → 事后才发现东西少了 → 进入猜测流程。


偷窃执行一瞬间，进行目击检测NpcSightSystem.GetObserversOf （FOV + RayCast）
并决定谁来主动找玩家质疑，谁来单纯围观等。
具体Npc的表现需要走 AgentBrain的逻辑，通过给Npc发不同事件来影响后面的行为。
AgentAIController.Instance.SendEventToAgent(victim, "event_theft", attacker, victim );




| 偷动物 | `InteractionMissionView.TryStealAnimal` | 尝试转移到StealManager里面，这样可以复用StealManager.GetWitnesses等逻辑

> **注意**：`StealManager.GetWitnesses` 使用 `NpcSightSystem.GetObserversOf`（FOV + RayCast），但其内部 `ProcessAgentCandidate` 过滤了非人类 Agent。偷动物时 victim 是动物 Agent，在偷窃执行之后，被偷的agent会被fadeout销毁。并且偷窃时候被其他动物Agent看到并不算被目击。



---

## 零-C、NPC 警戒值系统 + 头顶警戒条

**偷窃过程中，玩家需要实时观察每个 NPC 的警戒程度——谁在看我？谁快要去报警了？**

### 架构三层
架构还是走NpcSightSystem，但是需要维护一个对玩家的警戒值变量。
警戒值的UI渲染，走BubbleSayVM，和血条、冒泡等一起管理。复用已有的与AIStateFlag系统




警戒因子 (AlarmFactor)与AIStateFlag 的映射关系

```
AlarmFactor: 0.0 ──────── 0.25 ──────── 1.0 ──────── 2.0+
状态:         正常          怀疑          警戒索敌(Cautious)  质问/战斗(Alarmed)

| 条件 | 新状态 | 行为 |
|------|--------|------|
| AlarmFactor ≥ 1.0 | Cautious | 停止巡逻，左右张望 |
| AlarmFactor ≥ 2.0 + 看到嫌犯 | Alarmed  | 进入质问/战斗，喊人围观 |
| AlarmFactor ≥ 2.0 + 看到尸体 | Alarmed  | 搜索嫌犯 |
| 没看到敌人 + 时间流逝 | 衰减 0.025~0.125/s | 逐渐冷静 |


### AlarmFactor警戒值计算公式
在NpcSightSystem里面，如果看到玩家，警戒值的每帧变化公式如下
alertDelta = dt * ( IdentityValue + ActionSuspiciousValue); 
如果没看到玩家，那么则自然随时间衰减。
alertDelta = dt * (- NoSeeValue)

-- IdentityValue: 因为身份立场导致的警戒值增加，如果是敌人则为0.15f，否则为0
-- PlayerActionSuspicious: 玩家可疑行为。如果单纯蹲下就是0.15f,如果打开了偷窃界面但是没确认就是0.3f，如果干坏事一瞬间（击晕、偷窃、攻击友方），会立刻暴增到2
-- NoSeeValue: 如果没看到玩家，警戒值会逐渐衰减,设置为0.15f



### 警戒值阶段与视觉
本质是另一种形式的多层血条。但是是从到上累计进度的。
警戒值0-1，白底，进度部分是黄色
警戒值1-2，黄底，大于1的进度部分是橙色
警戒值>=2, 纯红色



---