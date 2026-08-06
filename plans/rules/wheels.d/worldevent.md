# worldevent — 轮子速查分卷（wheels.md 索引导航）
## 安全建部队 — `Core/SafeLordPartyComponent.cs`

为英雄创建独立部队时用它做最小 PartyComponent（带 null 防护），避免裸建 component 崩溃。


---

## 大地图 Party 出生点验证 — `WorldEvent/WorldEventSimulator.FindReachableSpawnPosition`

在大地图上为 party 选定出生点时，**必须验证出生点到目标定居点的寻路可达性**。仅检查 navmesh 面是否有效（`GetFaceIndex().IsValid()`）不够——山顶/隔水区域也有 navmesh 面，但和定居点不连通，mouse 光标会变禁用圈。

三层验证管线：

```csharp
// 入口：只需传目标定居点，内部自动生成多方向候选 + 验证
Vec2 spawnPos = FindReachableSpawnPosition(targetSettlement);

// 内部验证：
// ① GetLastPointOnNavigationMeshFromPositionToDestination — 几何投影到 navmesh
// ② AreFacesOnSameIsland(projectedFace, settlementFace) — 🔑 同一连通岛？（排除隔山/隔水）
// ③ GetPathDistanceBetweenAIFaces(projectedFace, settlementFace, …, 100f, out pathDist) — 🔑 寻路距离合理？
// ④ pathDist <= straightDist * 3 — 排除绕远路的孤立路径
// 候选：定居点周围 3 圈 × 8 个方向（18/30/42 单位半径），取第一个通过全部验证的
// 兜底：GetAccessiblePointNearPosition(settlementPos, 30f) — 引擎原生
```

**禁止**在任何 party 出生点计算中裸用 `GetFaceIndex().IsValid()` 作为可达性判断——这与鼠标变禁用图标不是同一套逻辑。鼠标禁用图标用的是 `AreFacesOnSameIsland`。辅助 party 的 `NearInstigator`/`BetweenParties` 用 `GetAccessiblePointNearPosition` 即可（参照物是已有 party，本身在可达区域）。


---

## 通知防刷屏冷却 — `WorldEvent/WorldEventDirector`

高频检查（如 `OnCampaignTick` 每 2 秒扫一次）里推送通知时，**必须加 per-event 冷却字典**，否则玩家站在事件附近每 2 秒弹一次同一条通知。

```csharp
// 字段
private static readonly Dictionary<string, float> _interceptCooldowns = new Dictionary<string, float>();
private const float INTERCEPT_COOLDOWN_DAYS = 0.15f; // ~3.6 小时

// 使用
if (_interceptCooldowns.TryGetValue(selected.EventId, out float lastDay)
    && currentDay - lastDay < INTERCEPT_COOLDOWN_DAYS)
    return; // 冷却中，跳过
_interceptCooldowns[selected.EventId] = currentDay;

// 定期清理过期记录（>1 天），防止内存泄漏
var expired = _interceptCooldowns.Where(kv => currentDay - kv.Value > 1f).Select(kv => kv.Key).ToList();
foreach (var key in expired) _interceptCooldowns.Remove(key);
```

---

# 三大可扩展引擎（核心资产 —— 加玩法优先挂这里）


---

## 架构

四层：**模拟器**（DailyTick 生成事件 + party）→ **数据库**（Event CRUD + JSON 持久化）→ **导演**（五种推送控制可见性）+ **通知控制器**（NinjaReport → Inquiry 书信）→ **宿敌追踪**（交手记录 → 伤疤 → 复仇）。


---

## 核心入口

```csharp
// 生成管线（WorldEventSimulator.TryGenerateNewEvent）
// ① 动机驱动真人冲突（优先！）
TryGenerateMotivatedEvent()  // 扫 Hero 关系/仇恨/性格 → 真人冲突（NobleConflict/Betrayal/Assassination…）
// ② 回落随机事件
roll → 选类型 → 选定居点 → 选人 → SpawnEventParty → 存入 DB

// 动机扫描四层：跨clan仇恨 → 同clan内斗 → 经济冲突 → 野心扩张

// 征用真人部队（WorldEventSimulator.SpawnEventParty）
// instigator 正带队 → 调遣真实部队（LeaderHero、Scouting=300加速、不改位置）
// instigator 在定居点 → 新建 party（定位目标附近）
// 到场才触发后果：CheckEventPartyArrivals → dist < 3 单位 → ApplyExpiryConsequences

// 控制台调试
custom.worldevent_list       // 列出所有活跃事件
custom.worldevent_force [类型] [严重度]  // 强制生成（默认BanditRaid）
custom.worldevent_status     // 内部状态
```


---

## 给世界事件加新婚事件的正确姿势

1. 在 `WorldEventType` 枚举加类型
2. 在 `WorldEventConfig` 静态构造里 `Register(new WorldEventConfig{...})`
3. 在 `WorldEventDirector` / `WorldEventNotificationController` 的 switch 里加对应文本
4. 在 `Narrative.csv` 加 `WorldEvent_Greeting_{Type}_Victim` / `Instigator` 条目

---

# 案情文案事实派生层 — `WorldEvent/WorldEvent.cs`

**解决什么问题**：犯罪事件的玩家可见文案曾按 EventType 静态模板（`Config.CrimeVerb*`）硬套——但 PendingWorldEvent 永远是 Misconduct 万用容器类型，模板描述不了"击晕+搜刮"这类复合罪行（曾把击晕搜刮报成"偷牲口"，量词写死"只/牲口"，gold 还不计赔偿估值）。现在**一切案情文本从记账事实派生**：新犯罪玩法只要把事实记进 `WitnessTestimonies` + `AssaultVictimNames`，通知/Quest/对话/传闻文案自动如实还原，不用改任何消费点。


---

## 事实派生 API（WorldEvent 上，一处生成处处消费）

```csharp
// 案情事实句（村民视角，不知是谁干的）——发现通知/Issue/Quest/传闻/对话统一入口
evt.BuildDiscoveryFacts();
//   袭击+失窃 → "帝国农民被人打晕了，还少了一件扣带束腰衣、一件扎带皮靴等4项财物"
//   仅袭击   → "帝国农民被人打晕了"（多人 → "有3人被人打晕了"）
//   仅失窃   → "少了一只羊"
//   都无     → 回落 Config.CrimeVerbPast

evt.CaseLabel;    // 案件定性标签：刑案(伤人+失窃)/伤人案/失窃案/案件 —— 标题/简述用
evt.HasAssault;   // 是否有击晕/袭击记账

// 赃物描述（量词分类：牲畜→只、装备/货物→件、金→"N第纳尔"；3+ 混合尾巴：纯牲畜"等N只牲口"，否则"等N项财物"）
evt.BuildStolenItemsDescription();
evt.TotalStolenCount;  // 赃物总项数（金只算 1 项——悬赏按件定价用）
evt.TotalStolenValue;  // 赃物总市值（物品市值 + 金按面值计入——赔偿估值用）
```


---

## 记账侧：ActionRecord.Count

gold 面额必须走 `Count` 字段（不再只嵌在 ItemName 字符串里）；普通物品默认 1；旧存档 Count=0 → 聚合按 1 兜底（序列化兼容）。

```csharp
AgentAIController.Instance?.RegisterUnwitnessedTheft("gold", $"{actual} 第纳尔", targetName, count: actual);
AgentAIController.Instance?.RegisterTheftWitnesses(heroIds, templates, itemId, itemName, targetName, count);
```


---

## 消费点清单（新案情叙事禁止绕过）

| 消费方 | 用法 |
|--------|------|
| `WorldEventNotificationController.OnCrimeDiscovered` | `e.BuildDiscoveryFacts()` |
| `CommissionQuest.OnStartInvestigation` | `evt.BuildDiscoveryFacts()` |
| `CommissionData.GetFlavorDescription` / `CommissionHubIssue` Title/Brief | `evt.CaseLabel` |
| `CommissionHubIssue` Description | context 预取 `DiscoveryFacts` + `AuthorityRole` |
| `SocialEventManager.BuildSocialEventDescription` | 犯罪类走 `BuildDiscoveryFacts()` |
| `ConfessIntent` 自首日志 | `evt.BuildDiscoveryFacts()` |
| `PlaceholderResolver` | `{DiscoveryFacts}` / `{StolenItemDesc}`（委托统一实现，禁止本地重写量词逻辑） |
| `CrimeDialogueBuilder` 模板 | 用 `{DiscoveryFacts}`，不再 `{CrimeScene}{CrimeVerbPast}{StolenItemClause}` 三段拼接 |

**铁律**：①禁止按 `evt.Type` / `Config.CrimeVerb*` 拼案情文案；②赔偿对话的损失描述走 `BuildLossDescription()`（赃物市值+袭击身价合并成句）；③新占位符走两步流程（`PlaceholderResolver.ResolveOne` 加 case + 模板引用），且 `ResolveOne` 返回 null 会原样输出 `{KEY}`。

**文件位置**：`WorldEvent/WorldEvent.cs`（事实派生 API）、`AI/AlertTypes.cs`（`ActionRecord.Count`）、`AI/AgentAIController.cs`（记账 count 参数）。

---

# AgentHUD — 3D 角色头上通用 HUD 系统

**原地升级替换旧 `BubbleSay*` 系统**。为所有 Human Agent 提供统一的 3D 头上 HUD，管理五大元素：名字、说话冒泡、血条、伤害数字、警戒眼睛。
