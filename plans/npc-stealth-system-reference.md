# 骑砍2原版潜入/警戒/视野系统 — 逆向分析报告

> 通过 `ilspycmd` 反编译 SandBox.dll / TaleWorlds.MountAndBlade.dll / TaleWorlds.CampaignSystem.dll 得出。
> 用于指导 LivingWorldNpcs 的 NpcSightSystem 增强和潜入玩法开发。

---

## 一、总览：三层警戒体系

```
┌──────────────────────────────────────────────────────────┐
│ 第一层：视觉检测 (AlarmedBehaviorGroup.GetVisualFactor)    │
│   每 tick 每个守卫对每个目标计算「我能看到你吗」            │
│   → 椭圆FOV + 光照 + 掩体 + 蹲伏 + 装备 + 距离            │
│                                                          │
│ 第二层：警戒因子 (AlarmFactor 0.0 → 2.0+)                │
│   多个目击者叠加、声音传播、累积衰减                        │
│   → Normal → Cautious → Alarmed → Fight                 │
│                                                          │
│ 第三层：玩家嫌疑度 (PlayerSuspiciousLevel 0.0 → 1.0)       │
│   移动模式分析、武器状态、个人空间                          │
│   → ≥0.95 守卫激活潜行模式                                 │
└──────────────────────────────────────────────────────────┘
```

---

## 二、视觉检测核心：AlarmedBehaviorGroup

**文件**: `SandBox.Missions.AgentBehaviors.AlarmedBehaviorGroup`  
**DLL**: `SandBox.dll`

### 2.1 椭圆视野锥 (Elliptical FOV)

```csharp
// 不是简单的圆锥！是横宽纵窄的椭圆锥，模拟人眼真实视野
float hFov = π × 19/40;   // ≈ 1.49 rad → 水平半角 85.5° → 全角 171°
float vFov = π × 57/200;  // ≈ 0.895 rad → 垂直半角 51.3° → 全角 102.6°
```

计算方法：
1. 将目标方向转到观察者的本地坐标系
2. 计算水平角 `Atan2(z, x)` 和仰角 `Acos(y)`
3. 椭圆边界：`hFov × vFov / Sqrt(hFov²×sin²θ + vFov²×cos²θ)`
4. 如果仰角超出边界 → 不可见；否则 `visibility = (boundary - angle) / boundary`

### 2.2 视觉强度计算 (GetVisualStrength)

```
visualStrength = 基础可见度
  × 蹲伏修正: 静止0.8 / 移动0.9
  × 近距离惩罚: 距离 < 6.5×身体半径 → ×15（太近反而极易发现）
  × 装备潜行加成: GetEquipmentStealthBonus(agent)
  × 光照系数: 白天 0.7/1.0, 夜晚 0.2/0.15
  × 模型可见度: GetVisualStrengthOfAgentVisual (LOD/遮挡/贴图)

阈值: visualStrength > 0.3 才算「看到了」
```

### 2.3 光照系统

```csharp
// 默认值（无 StealthIndoorLightingArea 覆盖）
Day:   ambientLight = 0.7,  sunMoonLight = 1.0   → 非常容易发现
Night: ambientLight = 0.2,  sunMoonLight = 0.15  → 很难发现

// StealthIndoorLightingArea（VolumeBox，场景中手动放置）
// 被它覆盖的区域使用自定义光照值 — 「暗处」机制
StealthIndoorLightingArea.AmbientLightStrength
StealthIndoorLightingArea.SunMoonLightStrength
```

### 2.4 掩体系统 (StealthBox)

```csharp
// 场景实体，如灌木、木箱
class StealthBox : ScriptComponentBehavior

// 检测目标是否被掩体覆盖
IsAgentCoveredByAStealthBox(agent) → 完全不可见
```

### 2.5 GetVisualFactor 完整流程

```
对每个「观察者 agent → 目标 currentAgent」:
  1. 计算视线向量 (target.EyePos - observer.EyePos)
  2. 点积检查：是否在观察者视线前方？
  3. IsAgentCoveredByAStealthBox → 是则跳过
  4. GetVisualStrength(方向, 观察者视线方向, 目标, 是否移动, 距离, 装备加成)
     → 返回 0~1 的视觉强度
  5. visualStrength × GetVisualStrengthOfAgentVisual (渲染可见度)
     → 这个 Native 方法检查目标模型是否真的在屏幕上有像素
  6. 阈值: > 0.3 → 累积到 AlarmFactor
     → 如果是敌人 → hasVisualOnEnemy = true
     → 如果目标是尸体 → hasVisualOnCorpse = true
     → 如果目标是玩家盟友 → visualStrength × 0.5
```

---

## 三、警戒因子 (AlarmFactor)

### 3.1 数值与状态

```
AlarmFactor: 0.0 ──────── 0.25 ──────── 1.0 ──────── 2.0+
状态:         正常          怀疑          警戒(Cautious)  战斗(Alarmed)

每 tick 变化 = Σ visualFactor × dt × 潜行难度系数
```

### 3.2 状态转换

| 条件 | 新状态 | 行为 |
|------|--------|------|
| AlarmFactor ≥ 1.0 | Cautious — `SetAlarmState(1)` | 停止巡逻，左右张望 |
| AlarmFactor ≥ 2.0 + 看到敌人 | Alarmed — `SetAlarmState(3)` | 进入战斗，喊叫警报 |
| AlarmFactor ≥ 2.0 + 看到尸体 | `SetAlarmState(2)` | 调查尸体位置 |
| 没看到敌人 + 时间流逝 | 衰减 0.025~0.125/s | 逐渐冷静 |

### 3.3 声音传播

```csharp
// OnAddSoundAlarmFactor — 守卫的警戒可以通过声音传给附近守卫
Mission.OnAddSoundAlarmFactorToAgents
```

### 3.4 可疑位置记录

当 AlarmFactor 增长时，守卫记录目标的最后已知位置。Cautious/PatrollingCautious 状态的守卫会朝该位置移动调查。

---

## 四、守卫 AI 行为：CautiousBehavior

**文件**: `SandBox.Missions.AgentBehaviors.CautiousBehavior`  
**DLL**: `SandBox.dll`

### 4.1 两种警戒模式

| 模式 | Agent 状态 | 行为 |
|------|-----------|------|
| **Cautious** (固定哨) | `agent.IsCautious()` | 原地站立，定期左右张望 (`act_guard_cautious_look_around_1`) |
| **PatrollingCautious** (巡逻哨) | `agent.IsPatrollingCautious()` | 沿路线巡逻 → 到可疑位置停下 → 随机走到新点 → 继续巡逻 |

### 4.2 DoNotCheckForAlarmFactorIncrease

```csharp
// DisguiseMissionLogic 用这个开关控制守卫的检测行为
value.DoNotCheckForAlarmFactorIncrease = !isActive;
// 为 true 时，AlarmedBehaviorGroup 跳过视觉检测 — 守卫「闭眼」
```

---

## 五、玩家嫌疑度系统：DisguiseMissionLogic

**文件**: `SandBox.Missions.MissionLogics.DisguiseMissionLogic`  
**DLL**: `SandBox.dll`

### 5.1 嫌疑度公式

```csharp
PlayerSuspiciousLevel += GetPlayerSuspiciousFactor(dt) × dt × GetDisguiseDifficultyMultiplier()
```

### 5.2 增减因子

| 玩家状态 | 嫌疑度变化 |
|----------|-----------|
| 没人看到你 | **-0.07/s** |
| 被守卫看到 (IsVisible) | **+0.04/s × 看到你的守卫数** |
| 闯入军官贴身区 (3.5m) | **+0.13/s**（极强） |
| 跑步（非走、非交互中） | **+0.3/s** |
| 手上有武器 | **+0.55/s** |
| 冲刺 | **+0.75/s** |
| 蹲伏移动 | **+0.15/s** |
| 正在交互（坐、靠墙） | **-0.07/s** |
| 静止 + 没被看到 | **+0.1/s** |

### 5.3 移动分析

**不规则移动 (Erratic Movement)**:
- 每帧记录移动方向角度变化
- 角度差 > 0.02 弧度 → 累积到 `cumulativeDiff`
- 累积值衰减：-2.0 × dt
- 范围 Clamp：0.2 ~ 0.6
- 效果：`0.1 × cumulativeDiff` 加成到嫌疑度

**绕圈移动 (Circular Movement)**:
- 追踪玩家平均位置（缓慢 lerp 到当前位置）
- 如果在 2m 半径内兜圈 → `circularFactor` 趋近 1.0
- 效果：`0.1 × circularFactor` 加成到嫌疑度

### 5.4 守卫激活阈值

```
PlayerSuspiciousLevel ≥ 0.95 → SetStealthModeToDisguiseAgents(true)
  → DoNotCheckForAlarmFactorIncrease = false  （守卫开始检测）
  → 所有看到玩家的守卫 AlarmFactor 至少跳到 0.25
  → 玩家无法离开场景（OnEndMissionRequest 拒绝）
```

---

## 六、潜行失败系统：StealthFailCounterMissionLogic

**文件**: `SandBox.Missions.StealthFailCounterMissionLogic`  
**DLL**: `SandBox.dll`

```
任何守卫进入 Alarmed 状态 → 倒计时启动（默认 5s，劫狱 15s）
所有守卫恢复平静 → 倒计时重置
倒计时归零 → 弹出 "Mission Failed! You have been compromised."
           → OnStealthMissionCounterFailedEvent
           → Mission 结束
```

---

## 七、潜入区域系统：StealthAreaMissionLogic

**文件**: `SandBox.Missions.MissionLogics.StealthAreaMissionLogic`  
**DLL**: `SandBox.dll`

场景中的 `StealthAreaUsePoint` + `StealthAreaMarker` 定义「潜入区域」。
- 区域内敌人被标记为 Sentry
- 清除所有哨兵 → 区域被「触发」→ 可呼叫增援
- 用于 Hideout Mission 的 stealth→combat 切换

---

## 八、UI 层

### 8.1 嫌疑度条（底部中央）
**文件**: `SandBox\GUI\Prefabs\Mission\Disguise\MissionMainAgentDetection.xml`
- `MissionSuspicionFillerBrushWidget` 自定义引擎控件
- 显示 `PlayerSuspiciousLevel` (0→1) 作为填充条
- 圆形伪装图标 + 高嫌疑时出现感叹号

### 8.2 守卫屏幕标记
**文件**: `SandBox\GUI\Prefabs\Mission\Disguise\MissionDetectionMarkers.xml`
- `DisguiseMarkerBrushWidget` — 3D→2D 投影标记
- `OffenseTypeIdentifier`: `IsVisible` / `IsInPersonalZone`
- 看到玩家的守卫显示在屏幕对应位置

### 8.3 失败倒计时条（顶部中央）
**文件**: `SandBox\GUI\Prefabs\Mission\Disguise\MissionStealthFailCounter.xml`
- 倒计时文本 + FillBarWidget
- `FailCounterSeconds` 由控制器设置

### 8.4 失去目标警告
**文件**: `SandBox\GUI\Prefabs\Mission\Disguise\MissionLosingTarget.xml`
- 接头人距离 > 5000 单位 → 警告

---

## 九、StealthOffenseTypes 枚举

```csharp
public enum StealthOffenseTypes
{
    None,             // 无威胁
    IsVisible,        // 守卫能看到玩家
    IsInPersonalZone  // 玩家闯入守卫贴身范围
}
```

---

## 十、与 LivingWorldNpcs 现有系统的对比

| 维度 | NpcSightSystem (已有) | 原版 |
|------|----------------------|------|
| **FOV 形状** | 圆锥 (Cos半角) | **椭圆锥** (横171°纵102°) |
| **距离** | 硬半径 | 距离 × 身体半径倍数 + 远近不同惩罚 |
| **光照** | ❌ | Day/Night/室内体积三套系数 |
| **掩体** | ❌ (仅RayCast) | StealthBox + RayCast 双重 |
| **蹲伏** | ❌ | ×0.8~0.9 系数 |
| **装备** | ❌ | GetEquipmentStealthBonus |
| **警戒层级** | 二值 (看到/没看到) | **四级**: Normal→Cautious→Alarmed→Fight |
| **嫌疑度** | ❌ | 0→1 连续值，≥0.95 触发守卫警觉 |
| **移动分析** | ❌ | 不规则移动 + 绕圈检测 |
| **声音传播** | ❌ | OnAddSoundAlarmFactor |
| **失败机制** | ❌ | 连续计时 + 所有守卫平静才重置 |
| **事件系统** | OnAgentStart/StopObserving | OnStealthMissionCounterFailedEvent |
| **UI 标记** | 无 | 嫌疑度条 + 屏幕守卫标记 + 倒计时条 |

---

## 十一、关键 API 速查

```csharp
// 设置潜入模式
Mission.Current.SetMissionMode(MissionMode.Stealth, true);

// 核心视觉检测
AlarmedBehaviorGroup.GetVisualFactor(lookDir, target, lightingAreas,
    ref hasVisualOnCorpse, ref hasVisualOnEnemy);

// ⚠️ 以下 API 经 ilspycmd 验证不存在于任何版本，仅为文档错误：
// MissionGameModels.Current.AgentStatCalculateModel.GetEquipmentStealthBonus(agent)
//   → 实际：装备潜行加成在 AlarmedBehaviorGroup 内部计算，不暴露为独立 API

// 难度系数
Campaign.Current.Models.DifficultyModel.GetDisguiseDifficultyMultiplier();
Campaign.Current.Models.DifficultyModel.GetStealthDifficultyMultiplier();

// 警戒状态
agent.SetAlarmState(AIStateFlag.Alarmed);   // 战斗
agent.SetAlarmState(AIStateFlag.Cautious);  // 警戒
agent.SetAlarmState(AIStateFlag.None);      // 正常（不是 (AIStateFlag)0）
agent.IsAlarmed()
agent.IsCautious()
agent.IsAlarmStateNormal()

// 伪装装备
Campaign.Current.IsMainHeroDisguised
Hero.MainHero.StealthEquipment    // ⚠️ 仅 1.4.6，1.2.12 不存在
```

---

## 十二、可复用模式

1. **椭圆 FOV** — 直接抄 `GetVisualStrength` 的椭圆公式，替换 NpcSightSystem 的简单圆锥
2. **光照衰减** — 检测 `StealthIndoorLightingArea` + DayTime 标志
3. **多级警戒** — NpcSightSystem 的 tick 事件已注册了 OnAgentStartObserving，在其回调里累加 AlarmFactor 就行
4. **移动分析** — 在 NpcSightSystem tick 里加 Vec2 位置历史，算 Erratic + Circular
5. **UI** — 已有 BubbleSayMissionView 的头顶 UI 系统，可以复用渲染逻辑显示嫌疑度条

---

## 十三、版本兼容性（⚠️ 关键）

> 验证日期：2026-06-21，通过 `ilspycmd` 反编译 `Modules/1.2.12DLL/` 和 `Modules/1.4.6DLL/` 交叉对比。

### 13.1 两版本差异总表

| API / 类 | 1.4.6 (Latest) | 1.2.12 |
|----------|---------------|--------|
| `MissionMode.Stealth` | ✅ | ✅ |
| `Campaign.IsMainHeroDisguised` | ✅ | ✅ |
| `DisguiseDetectionModel` | ✅ 含 `GetStealthDifficultyMultiplier` / `GetDisguiseDifficultyMultiplier` | ✅ 仅有 `CalculateDisguiseDetectionProbability` |
| `AIStateFlag` 枚举（Alarmed/Cautious） | ✅ | ✅ |
| `AlarmedBehaviorGroup` | ✅ **完整**：GetVisualFactor / AlarmFactor / AddAlarmFactor / DoNotCheckForAlarmFactorIncrease / 声音传播 / 光照/掩体检测 | ✅ **极简**：仅 behavior group 选择器，无视觉检测/无 AlarmFactor/无声音传播 |
| `DisguiseMissionLogic` | ✅ 完整嫌疑度系统 + 移动分析 | ❌ **不存在** |
| `StealthFailCounterMissionLogic` | ✅ | ❌ **不存在** |
| `StealthAreaMissionLogic` | ✅ | ❌ **不存在** |
| `CautiousBehavior` | ✅ | ❌ **不存在** |
| `StealthOffenseTypes` 枚举 | ✅ | ❌ **不存在** |
| `Agent.SetAlarmState()` / `IsAlarmed()` / `IsCautious()` | ✅ | ❌ **不存在**（通过 raw `AIStateFlags` 位操作） |
| `Hero.StealthEquipment` | ✅ | ❌ **不存在** |
| `Mission.OnStealthMissionCounterFailedEvent` | ✅ | ❌ **不存在** |
| `Mission.OnAddSoundAlarmFactorToAgents` | ✅ | ❌ **不存在** |
| `BehaviorSets` 静态类 | ✅ 14 种行为模板 | ✅ 12 种（缺 `AddFixedGuardBehaviors` / `StealthAgentBehaviors` / `AddPatrollingThugBehaviors`，多了 `AddAmbushPlayerBehaviors`） |

### 13.2 关键结论

- **1.2.12 的潜入系统是骨架**：有 `MissionMode.Stealth` 枚举和 `AIStateFlag`，但没有任何实质的视觉检测、嫌疑度计算、警戒状态机。`AlarmedBehaviorGroup` 只是一个行为选择器（Flee vs Fight）。
- **1.4.6 大幅扩展了潜入系统**：完整的椭圆 FOV 视觉检测、多级警戒状态机、嫌疑度系统、移动模式分析、声音传播——全部是 1.2.12 之后新增的。
- **UI 层（嫌疑度条/守卫标记/失败倒计时）在 1.2.12 完全不存在**，1.4.6 也仅在藏身处任务加载。

### 13.3 对 LivingWorldNpcs 的影响

如果要构建**通用的 NPC 警戒系统**（任意场景、任意 NPC、双版本兼容）：

| 层 | 1.4.6 策略 | 1.2.12 策略 |
|----|-----------|------------|
| 视觉检测（椭圆 FOV） | 可复用 `AlarmedBehaviorGroup.GetVisualFactor` 公式 | **从零自建** |
| 警戒状态机 | 可复用 `AlarmFactor` 累积 + `SetAlarmState()` | **从零自建**，用 `AIStateFlags` 位操作 |
| 光照/掩体 | 可复用 `StealthIndoorLightingArea` / `StealthBox` | **从零自建** |
| UI 标记 | 需自建（`DisguiseMissionLogic` 仅藏身处） | **从零自建** |
| 声音传播 | 可复用 `OnAddSoundAlarmFactorToAgents` | **从零自建** |

**结论：1.4.6 可以站在原版肩膀上，1.2.12 需要全部自建。** 建议用 `#if !MB2_V1212` 在 1.4.6 复用原版引擎，1.2.12 走自建路径。

---

## 十四、引擎挂载范围与激活条件（⚠️ 重大发现）

> 验证日期：2026-06-21，反编译 `BehaviorSets` + `AlarmedBehaviorGroup.Tick()`。

### 14.1 引擎挂载范围：所有 NPC

`BehaviorSets.AddBehaviorGroups()` 是 NPC 行为初始化的公共基座，**被所有 NPC 类型调用**：

```
BehaviorSets.AddStandGuardBehaviors()      → 城镇站岗守卫
BehaviorSets.AddPatrollingGuardBehaviors() → 巡逻守卫
BehaviorSets.AddWandererBehaviors()        → 普通路人/村民
BehaviorSets.AddQuestCharacterBehaviors()  → 任务 NPC
BehaviorSets.AddCompanionBehaviors()       → 同伴
BehaviorSets.AddBodyguardBehaviors()       → 保镖
BehaviorSets.AddFixedCharacterBehaviors()  → 固定位置 NPC
... 共 14 种行为模板
```

每个都内部调用 `AddBehaviorGroups()`，也就都挂上了 `AlarmedBehaviorGroup`。**凡是走 BehaviorSets 初始化的 NPC，身上都有 AlarmedBehaviorGroup。**

### 14.2 激活条件：IsEnemyOf 闸门

**挂了 ≠ 在算。** `AlarmedBehaviorGroup.Tick()` 每帧遍历所有 Agent，但有闸门：

```csharp
foreach (Agent allAgent in allAgents)
{
    // 跳过条件：不是人类、没有 CanAttack、不是敌人……
    if (... || !base.OwnerAgent.IsEnemyOf(allAgent))  // ← 闸门！
    {
        continue;  // 不是敌人 → 跳过，不计算视野
    }
    // ↓ 只有 IsEnemyOf=true 的才进入这里
    num3 += GetVisualFactor(vb, allAgent, ...);  // 椭圆 FOV、光照、掩体……
}
```

**在和平城镇/村庄场景中**：玩家和所有 NPC 互為中立，`IsEnemyOf()` 全返回 false → Tick 遍历全体但全部 `continue` → **视野计算完全休眠，AlarmFactor 恒为 0。**

**什么时候激活？**
- 战斗爆发（敌人出现）→ `IsEnemyOf` 变 true → 开始计算视野
- 藏身处任务 → 敌人预设存在 → 始终激活
- `DisguiseMissionLogic` 场景 → 守卫和玩家之间虽然不是敌人，但 `DisguiseMissionLogic` 通过 `PlayerSuspiciousLevel ≥ 0.95` 单独触发潜行模式

### 14.3 对自建通用警戒系统的意义

原版引擎在 1.4.6 已经**全场景部署**了视觉检测管线（椭圆 FOV / 光照 / 掩体 / 警戒状态机），只是被 `IsEnemyOf` 闸门挡住了。

自建通用系统时，**不需要替换整个 AlarmedBehaviorGroup**。只需要：
1. **绕开 `IsEnemyOf` 闸门** → 让 NPC 对非敌人（玩家）也计算视野
2. **接入自己的嫌疑度逻辑** → 替代 `IsEnemyOf` 作为激活条件
3. **驱动自己的 UI 层** → 显示警戒标记和嫌疑度条

对 1.4.6 来说，这是"撬锁"而非"拆房"——引擎已经有了，只是钥匙（`IsEnemyOf`）不对。
