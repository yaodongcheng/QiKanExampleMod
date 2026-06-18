# Barter 面板嫁接 Intent 系统

> **状态**：想法（未开始）
> **优先级**：低（之后再做）

## 想法

原版 Barter（以物易物筹码面板）交互直观——拖拽金币/物品/封地、实时估值、两边比价。当前的 Intent 系统纯靠选项文本，缺少"讨价还价"的手感。

## 方案

原版 Barter 做 UI 层，成交结算走 Intent 系统：

```
玩家点"贿赂" / "军资谈判" / "策反跳槽费"
  → 打开原版 Barter 面板（引擎原生 UI）
  → 双方拖拽筹码
  → 成交 → 不走原版 BarterManager
        → 走 IntentBase.OnSuccess / SingleRollResolver
        → 人格匹配倍率、信任冷却、世界事件上下文
```

## 适合嫁接的场景

- 贿赂（开价让对方办一件事）
- 策反跳槽费（给领主出价让他叛变）
- 军资谈判（要多少钱/多少兵）
- 赎金/停战费

## 不适合的场景

- 求婚（情感不量化）
- 送礼（单边）
- 寒暄/闲谈（无交易性质）
- 切磋/决斗（战斗结算）

## 参考

- 原版 `BarterManager` / `BarterVM` — UI 层
- `InteractionOptionManager` + `SingleRollResolver` — 结算层
