# 移动避障：卡门与顶人（Movement Avoidance）

> 状态：设计稿（2026-08-21）。背景实机日志 `Debug/StoryEngine_RuntimeLog.txt`（吕卡隆偷窃三连）。
> 关联轮子：[[wheels.d/agent.md]]（移动目标分派/AgentControlHelper）、[[wheels.d/planner.md]]（扒窃绕背走位）。
> 前置：绕背闸门已放宽（Behind 相位改「视野圆锥外 + 距离带 + 已停步」，InlineSteps.cs:771-791，2026-08-21 已改）。

## 🔴 2026-08-21 实现修订（实机一测 + 用户裁定，已落地）

首版实现实机（吕卡隆，随从偷帝国军团步兵#50）：**0 条 [MoveAvoid]，Behind 8s 超时 impossible 作罢**——新 DLL 确认在跑（编译 16:53:58 < 游戏启动 16:54:13），三闸门全静默。原因与修订：

| 原设计 | 实机问题 | 修订（用户裁定） |
|---|---|---|
| 闸门 1 保护带 +1.5m（≤4m 直发） | 「距终点 4m 内」不代表路径通——近程恰恰最需要绕（门/目标本人就在眼前），直发 = 顶上去 | 保护带收窄到 **+0.5m**：真正贴脸才直发；近程（0.5~4m）留给闸门 2/3 绕 |
| 闸门 2 线段穿碰撞圆柱 < 0.7m | 只治正面直冲：agent 从侧向接近（玩家在目标侧前方）时直线全程距目标 > 0.7m → 静默 → 顶目标面前 8s 死等 | 判据改 **「agent 在目标前方视野锥内（dot ≥ 0.5，与 Behind 闸门同口径）+ 线段距目标 < 1.5m」**→ 终点挪侧翼 1.2m，覆盖正面+侧向接近 |
| 闸门 3 射线不限距离（命中 < 3.5m 才避） | 远程（11m 外）射线命中点必在 10m+，几何没定 nudge 无意义；近程被闸门 1 接管 | 闸门 3 改**近程专用：距终点 ≤ 4m 才打射线**（用户：「4m 外不需要射线检测，还早着呢」）；去掉 hitDist 窗口，命中即避 |
| — | 闸门 2 nudge 后闸门 3 再 nudge = 1.2+1.2 过头 | **闸门 2 已触发时跳过闸门 3**（目标本人在场时实体检测意义低） |

另：TryFindBehindSpot 45° 侧后候选点 dot=0.707 ≥ 0.5 在目标视野锥内（选了也进不了 Behind 闸门）——死结嫌疑**待实机验证**（本次日志无法确认目标是否贴墙）。

## 🔴 2026-08-21 二测修订（实机二测 + 用户裁定，已落地）

二测实机（吕卡隆，偷帝国弩手#48 / 弓箭手#50）：**避障 14 条 [MoveAvoid] 全部触发，move_to 阶段正常到位**——但 Behind 相位仍 8s 超时 impossible。新失败形态：agent 距绕背点**恒 3.2~4m 不接近**，绕背点坐标每秒漂 ~0.6m（= 目标 idle 转头 → `_behindPick` 正后 2.2m 绕着目标转 → agent 追不上）。另暴露首帧瑕疵：[MoveAvoid] 距终点 596.5m 终点=(0.9,-0.8)（Behind 首帧 `_behindPick` 未初始化）。

| 原实现 | 实机问题 | 修订（用户裁定） |
|---|---|---|
| TryFindBehindSpot 四固定点（正后 2.2m / ±45° 2.5m / 正后 3.5m） | ① 只有正后点，agent 必须绕过 target 本人才能到——目标转头时绕背点漂移追不上（距终点恒 3.2~4m）；② 45° 候选 dot=0.707 在视野锥内，选了也进不了闸门 | 重写为**视野外全扇区（7 方向：正后→±90°，30° 步进）× 距离带 0.5~3m（6 档）网格采样**，距离升序优先近点；侧翼点（±90° dot=0）同样合法，agent 从正面两步就到 |
| Behind 闸门距离带 1.0~2.5m | 与放开后的选点范围不一致（选 0.5~1m 点会进不了闸门） | 闸门距离带对齐 **0.5~3.0m**（近端 0.5 由碰撞圆柱 ~0.7m 自然顶开） |
| 0.25s 无条件重选绕背点 | 目标转小角度也重选 → 绕背点漂移 → agent 追不上 | **防抖**：旧点仍合法（`IsBehindSpotValid`：距离带 + dot<0.5）→ 沿用 |
| Behind 首帧用未初始化 `_behindPick` 发移动指令 | [MoveAvoid] 距终点 596.5m 指向原点（一帧即覆盖，无害但日志难看） | 首帧未选点不发移动指令（`_behindPicked` 标志） |
| 绕背视野口径 120°（dot < 0.5 = 背后） | 侧面 60°~90° 的点也算背后——NPC 有身体，侧翼贴太近一转脸就看见，不算真背后 | **150° FOV**（半角 75°，dot < cos75° ≈ 0.2588 = `BehindConeDot` 常量，Behind 闸门/闸门 2/IsBehindSpotValid 三处同源）；⚠️ 与目击检查（NpcSightSystem 仍 120°）独立，绕背站位比目击判定更保守 |

## 一、两个实机问题

### A. NPC 找人的时候被大门卡住

**现象**：`move_to → 人`（FollowAgentAction）路径穿过有门区域时，NPC 走到门口顶住，反复尝试几十秒（卡死预算按距离算：50m 目标 ≈ 50s），期间无任何绕行。

**根因**：引擎 `SetScriptedPosition` 走 navmesh 寻路。门洞 navmesh 视为「可通过」→ 路径直线穿门 → 物理碰撞体封死 → 顶门。**navmesh 认为通 ≠ 物理通**，静态障碍的绕行只发生在 navmesh 层，对「navmesh 通、物理不通」的实体引擎无解。

### B. 想绕到人背后的时候，直接面对着对方顶

**现象**：绕背相位（Behind）把 `_behindPick`（目标正后 2.2m）发给 `ScriptedMoveToPoint`，NPC 从正面/侧面接近时**路径直线穿过目标身体**（目标不在 navmesh 里，寻路无视它）→ 被碰撞体顶在 ~0.7-1m 处，既到不了绕背点，又呈现「顶着对方」的诡异姿势。

**根因**：动态角色是引擎寻路的盲区——寻路终点就在目标身上时，引擎的局部避障把目标当「终点」而非「障碍」，不会绕。

> 注：引擎对「路径途中的其他路人」有原生 steering 避让（人群不叠穿），这两个洞只发生在**寻路终点附近**（目标本人 / 门）——所以方案只补这两处，不碰全局。

## 二、设计：共享避障入口 `SmartMoveToPoint`

**位置**：`AgentControlHelper`（新方法）。**不动** `ScriptedMoveToPoint` 本体——逃跑/回岗/脚本移动等既有调用零影响（爆炸半径最小化）。

```csharp
/// 移动避障：目标点被近处实体（门/墙）或目标本人挡住时，横向偏移寻路终点绕开。
/// 到点优先：距目标 ≤ stopDistance + 1.5m 时不避障（保证贴脸目标可达）。
/// 偏移只影响「本帧下发的寻路终点」，完成判定仍用真实目标点（IsFinished 不受影响）。
public static void SmartMoveToPoint(Agent agent, Vec3 goal, bool run,
    float stopDistance, Agent goalAgent = null)
```

### 闸门 1：到点优先（治「避障导致到不了目标」）

```
dist = agent.Position.Distance(goal)
if dist <= stopDistance + 1.5f → ScriptedMoveToPoint(agent, goal, run); return   // 直发，不避障
```

保护带 1.5m 覆盖全部近点场景：follow 停止距离 1.5~2m、绕背点 2.2m——**目标近前永远直接冲**，物理碰撞体自然停在 ~0.5-1m（视觉 = 到位）。这是用户担心的「目标点离对方很近时避障把自己推远」的总闸。

### 闸门 2：目标本人贴身检测（治 B「顶人」）

2D 线段-圆柱检测（XZ 平面，`.AsVec2` 丢 z，代码既有惯例）：

```
if goalAgent != null 且活跃:
    p1 = agent.Position.AsVec2;  p2 = goal.AsVec2;  c = goalAgent.Position.AsVec2
    d = pointToSegmentDist(c, p1, p2)                    // 目标到「你→目标点」直线的最近距离
    if d < 0.7f:                                          // 直线从目标碰撞圆柱里穿过
        side = perp(goal - agent).AsVec2.Normalized()    // 垂直线段方向（XY 互换取负）
        goal += side * 1.2f                               // 寻路终点挪到目标身侧
```

- 用 2D 不做 3D：人的碰撞体是竖圆柱，「会不会顶到」只取决于水平距离，z 参与反而引入层高噪声。
- 硬编码 `perp` 方向带符号由引擎重算路径自洽（两帧间 goal 方向变化小，不会左右抖）。

### 闸门 3：前方实体检测（治 A「卡门」）

RayCast 眼高 → goal（复用 `V.RayCastForClosestEntityOrTerrain` 版本兼容封装 + `BodyFlags.CommonCollisionExcludeFlags`，与目击遮挡同套路）：

```
if RayCast(agent 眼高, goal, out hitDist) 且 hitDist < 3.5f:
    hitPoint = agent + dir * hitDist
    // 偏移方向：从命中点向两侧各探 1.5m 短射线，选通的一侧（无持久状态，不会左右抖）
    for side in {+perp, -perp}:
        if !RayCast(hitPoint, hitPoint + side * 1.5f): 选中该侧; break
    两侧都堵 → 保持原向（交给卡死瞬移兜底）
    goal += side * 1.2f
ScriptedMoveToPoint(agent, goal, run)
```

- 只在 repath 时打（0.2~0.25s 间隔），每次至多 3 次 RayCast，开销可忽略。
- 偏移 1.2m < 保护带 1.5m：即使 nudge 后仍受阻，也绝不超过「到点优先」的保证范围。

### 接入点（3 处，全部改走共享入口）

| 位置 | 现调用 | 改后 | goalAgent |
|---|---|---|---|
| `FollowAgentAction.MoveToTarget`（[AtomicAction.cs:894](ExampleModVS/ExampleMod/ExampleMod/AI/Actions/AtomicAction.cs#L894)，repath 时） | `ScriptedMoveToPoint(_currentIdealPosition)` | `SmartMoveToPoint(agent, _currentIdealPosition, _run, _stopDistance, _target)` | ✅ 目标本人 |
| `MoveToPositionAction.OnTick`（[AtomicAction.cs:544](ExampleModVS/ExampleMod/ExampleMod/AI/Actions/AtomicAction.cs#L544)，200ms） | `ScriptedMoveToPoint(_targetPos)` | `SmartMoveToPoint(agent, _targetPos, _run, _stopDistance)` | — |
| `InlineSteps` Behind 相位（[InlineSteps.cs:834](ExampleModVS/ExampleMod/ExampleMod/Planner/InlineSteps.cs#L834)，每帧） | `ScriptedMoveToPoint(_behindPick, dist>5f)` | `SmartMoveToPoint(_agent, _behindPick, dist>5f, 2.5f, target)` | ✅ 扒窃目标 |

**完成判定零改动**：`FollowAgentAction.IsFinished` 仍用 `_currentDistanceSq`（真实理想点）、`MoveToPositionAction.IsFinished` 仍用 `_targetPos`、Behind 闸门仍用真实 `dist`——偏移只是 steering 的临时终点。

## 三、为什么不选 ORCA（用户提问）

ORCA（Optimal Reciprocal Collision Avoidance）是**速度空间**的互惠避障：每帧把每个邻居的速度障碍圆锥并起来求合法速度集，假设对方也在避你、对半分避让量——专治密集人群动态避让。

**不选的理由**：

1. **引擎已有局部避障**：`SetScriptedPosition` 的 steering 会让 agent 互相让路。真正失败的是两个洞（终点=目标本人 / navmesh 通物理堵的门）——套 ORCA 会跟引擎原生避让**互相拉扯**（两边同时改速度）。
2. **状态与开销**：ORCA 需持续跟踪邻居速度、每帧解速度集；本方案无持久状态、只有 repath 时 1~3 次 RayCast。
3. **不对症**：互惠语义（双方对半分）在「目标本人」场景反而有害——你避它、它不避你（NPC 原地站岗），半量避让让两方都不到位。

本方案属于**航点偏移启发式**（waypoint nudging）：只在直线被挡时把终点横挪，清楚时归零。

## 四、边界与兜底（诚实声明）

- **真·封死的门绕不过**：门洞 navmesh 通、物理完全封死、两侧无缝隙时，任何 nudge 都无效（路径必经门洞瓶颈）——保留现有**卡死瞬移兜底**（无进展 3s → 瞬移，MoveToPositionAction/FollowAgentAction 既有逻辑）。多数场景门（城门半开/门框缝/双开门）nudge 后引擎会绕缝——实测见分晓。
- 每帧多 1~3 次 RayCast 只在 repath 间隔执行；`BodyFlags.CommonCollisionExcludeFlags` 复用目击遮挡惯例，不误伤视线语义。

## 五、验证清单（实机）

1. 命令随从「去找 XX」穿过带门区域 → 观察绕门（不再顶门几十秒）；封死门 → 接受瞬移兜底
2. 命令随从偷站在人群/墙边的士兵 → 绕背不再顶着目标身体，闸门（圆锥外 + 距离带 + 已停步）正常进 Rolling
3. 命令随从「过来」贴玩家身后 → 停到身侧不穿模（闸门 2 生效）
4. 回归：逃跑（FleeFrom）与回岗（Unlock 收尾）行为不变（不经过新入口）
5. 回归：NPC↔NPC 对话/群聊移动不受影响
