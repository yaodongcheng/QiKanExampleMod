# 坑点速查（疑难杂症）

> **按需加载**：不是每次会话必读。踩到诡异症状（AccessViolation、引擎 native 崩溃、状态错乱）时来这里查有没有同款坑。
> 每条格式：**症状 → 根因 → 规避**。根因尽量记到反编译确认的调用链，别凭名字猜。

---

## 对尸体/昏迷 Agent 刷新带武器的装备 → AccessViolation

**症状**
- `agent.UpdateSpawnEquipmentAndRefreshVisuals(newEquipment)` 抛 `System.AccessViolationException`（读写受保护内存）。
- 只在**死人/昏迷**的 Agent 上发生；活人正常。
- "全部拿走/扒光"不崩，"自己挑选只拿一部分"才崩；"一件没拿"（不触发刷新）也不崩。

**根因**（反编译 `TaleWorlds.MountAndBlade.Agent` 确认）

```
UpdateSpawnEquipmentAndRefreshVisuals(newEquipment)
  └─ WieldInitialWeapons()                        // 仅当 newEquipment 里还留着武器才往下走
        └─ TryToWieldWeaponInSlot(GetPtr(), ...)   // 纯 native，无 IsActive 守卫
```

- 死人骨骼已交给物理系统（ragdoll），native 再去"把武器握进手里"就操作到失效骨骼内存 → 崩。
- **崩的只有「武器 wield」这一步**。防具留在 `newEquipment` 里**不崩**——防具走 `AddSkinMeshes`，纯渲染挂网格，不碰骨骼物理。
- "全部拿走"安全的真正原因不是时机（不是因为尸体新鲜），而是**武器被拿光、`WieldInitialWeapons` 空操作**。

**规避**
- 对 `!agent.IsActive()` 的 Agent（死亡/昏迷都算）刷新前，**无条件清空所有武器槽**，让刷新等价于"全部拿走"——无武器可 wield 即安全。防具仍可按需精准扒/保留。
- 活人不受限制，照常刷新（活人能正常重新 wield 剩余武器）。
- 落地范例：`Stealth/StealManager.cs` → `StripAgentEquipment`（`bool isCorpse = !agent.IsActive();` 时武器槽过滤器传 `null`）。
- 同理：任何对尸体调 `UpdateSpawnEquipmentAndRefreshVisuals` 的新路径（如未来 `StealSpecificItem` 作用到尸体），都要先清武器槽。
