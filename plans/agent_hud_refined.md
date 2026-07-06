# AgentHUD — 3D 角色头上通用 HUD 重构方案

> **目标**：在现有 `BubbleSay*` 系统基础上升级，从"冒泡说话"扩展为 **通用 3D 角色头上 HUD**，
> 统一管理：名字、说话冒泡、血条、伤害数字、警戒值眼睛 — 五大元素。
> **原地升级**，不新建目录，必要文件/类改名即可。

---

## 一、文件改名映射（原地升级）

| 旧路径 | 新路径 | 旧类名 | 新类名 |
|--------|--------|--------|--------|
| `Bubble/BubbleSayVM.cs` | `AgentHUD/AgentHudVM.cs` | `BubbleSayVM` | `AgentHudVM` |
| `Bubble/BubbleSayNeaybyVM.cs` | `AgentHUD/AgentHudCollectionVM.cs` | `BubbleSayNeaybyVM` | `AgentHudCollectionVM` |
| `Bubble/BubbleSayMissionView.cs` | `AgentHUD/AgentHudMissionView.cs` | `BubbleSayMissionView` | `AgentHudMissionView` |
| `GUI/Prefabs/BubbleSayNearby.xml` | `GUI/Prefabs/AgentHudNearby.xml` | — | — |

**文件夹 `Bubble/` → `AgentHUD/`**，连同里面三个 `.cs` 文件一起改名。**不保留旧文件、不搞 Obsolete 转发**，全局替换引用即可。

---

## 二、AgentHudVM — 五大元素的显隐规则

### 2.1 五大元素

| 元素 | VM 属性 | 显隐条件 | 显示时长 | FOV 限制 |
|------|---------|----------|----------|:---:|
| **名字** | `AgentName` + `ShowName` | **FOV 内任意其他元素显示时** | 跟随其他元素 | ✅ |
| **说话** | `SpeechText` + `ShowSpeech` | 调用 `Speak()` 时 | `4s + text.Length * 0.1s` | ✅ |
| **血条** | `CurrentHealthWidth` + `ShowHealth` | 拔武器 OR 战斗中 OR 血量<95% | 持续（条件消失后隐藏） | ✅ |
| **伤害** | `DamageText` + `ShowDamage` | 受到伤害瞬间 | 2s | ✅ |
| **警戒** | `AlertFillHeight` + `ShowAlert` | 警戒值 > 0 | 持续（归零后隐藏） | ❌ **豁免** |

### 2.2 名字总领规则

```
ShowName = ShowSpeech || ShowHealth || ShowDamage
```

**名字只在 FOV 内显示。** 警戒眼睛（`ShowAlert`）不触发名字——玩家在看不到 NPC 时只知道"那个方向有人盯我"，不知道是谁。转身面对 NPC（进入 FOV）后名字浮现，信息补全。

### 2.3 容器可见性

```
IsVisible = ShowName || ShowAlert   // 警戒眼睛可以独立触发容器显示
```

### 2.4 血条显示条件细化

当前代码判断 `isHealthLow || _weaponDrawn`。细化后：

```csharp
// ShowHealth 条件（满足任一即显示）：
bool isWeaponDrawn = !agent.WieldedWeapon.IsEmpty;
bool isFighting = brain?.CurrentAction is FightEnemyAction;
bool isHealthLow = hpPercentage < 0.95f && currentHp > 0;
bool isAlerted = agent.IsAlarmed() || agent.IsCautious();

_showHealth = isWeaponDrawn || isFighting || isHealthLow || isAlerted;
```

### 2.5 伤害数字

保持现有逻辑：检测血量下降 → 显示 `-XX` → 2 秒 timer → 自动隐藏。

### 2.6 🔴 警戒 FOV 豁免

**警戒眼睛不受 FOV 角度限制。** 信息性质决定裁剪规则：

| 信息类型 | 回答的问题 | 需要 FOV 角度？ | 需要距离？ |
|----------|------------|:---:|:---:|
| 血条 / 说话 / 名字 | "我看到了什么" | ✅ 需要 — 你看不见他就不该显示 | ✅ 50m |
| 警戒眼睛 | "谁在看我" | ❌ **不需要** — 他在背后盯你，你更该知道 | ✅ 50m |

**设计理由**：

- NPC 在玩家身后 20m 警戒值飙红 → 玩家必须感知到"有人在注意我"，否则信息不对称不公平
- 对标 KCD2：偷窃时即便守卫不在视野内，UI 也会有警示信号（兔子图标变色等）
- **名字只在 FOV 内显示**：警戒眼睛显示时，如果 NPC 在 FOV 外，名字不显示——玩家只知道"那个方向有人盯我"，不知道是谁。转身面对 NPC 后名字浮现，信息补全

**屏幕边缘处理**：警戒眼睛在屏幕外时**不隐藏，而是吸附到边缘**：

```
警戒值 > 0？
  ├─ 投影在屏幕内 → 正常位置显示眼睛
  └─ 投影在屏幕外 → clamp 到屏幕边缘（最近边），眼睛贴边指示方向
```

这是一个**方向指示器**，告诉玩家"转身看看那个方向"。

**容器可见性修正**：
```
IsVisible = ShowSpeech || ShowHealth || ShowDamage   // FOV 内的常规元素
         || ShowAlert                                 // FOV 豁免，可以独立触发容器显示
ShowName  = ShowSpeech || ShowHealth || ShowDamage    // 名字只在 FOV 内显示（不含 ShowAlert）
```

注意事项：
- `ShowAlert` 单独为 true 时，容器显示但只有眼睛图标+方向指示，没有名字
- 当 NPC 同时有警戒值且在 FOV 内时，名字才出现——玩家看清"是谁在盯我"

---

## 三、警戒值系统

### 3.1 数据来源

警戒值由 `NpcSightSystem` 维护（不是原版 `AlarmedBehaviorGroup`——那只对守卫有效）。

**为什么不复用 AlarmedBehaviorGroup？**
- `AlarmedBehaviorGroup` 只存在于原版守卫 Agent 上，平民/村民/商队没有
- 原版的 `AlarmFactor` 只对"视觉检测到敌人"累加，不感知玩家可疑行为（蹲下、开偷窃UI等）
- 我们需要一个**对所有 NPC 通用**的警戒值系统

### 3.2 存储结构

在 `NpcSightSystem` 中新增：

```csharp
// 每个 NPC 对玩家的警戒值（key = Agent.Index）
private Dictionary<int, float> _alertValues = new Dictionary<int, float>();

// 公开查询接口
public float GetAlertValue(Agent npc);  // 不存在返回 0
```

### 3.3 计算公式

在 `NpcSightSystem.OnMissionTick` 中，对每个**能看到玩家**的 NPC 计算警戒值变化：

```
如果 NPC 能看到玩家 (CanNpcSeePlayer):
    alertDelta = dt * (IdentityValue + ActionSuspiciousValue)
否则:
    alertDelta = dt * (-DecayValue)
```

| 参数 | 值 | 说明 |
|------|-----|------|
| `IdentityValue` | 0.15 (敌人) / 0 (其他人) | 敌对阵营的 NPC 天生更警惕 |
| `ActionSuspiciousValue` | 0 (正常) / 0.15 (蹲下) / 0.3 (偷窃UI打开) / 2.0 (击晕/偷窃/攻击瞬间) | 暴增值为一次性脉冲，不走 dt 累积 |
| `DecayValue` | 0.15 | 看不到玩家时每秒衰减 |

**脉冲事件**（一次性直接加，不走 dt）：
- `OnPlayerKnockout` → +2.0
- `OnPlayerSteal` → +2.0
- `OnPlayerAttackAlly` → +2.0

**接口设计**：
```csharp
// NpcSightSystem 新增
public void AddAlertPulse(Agent npc, float amount);  // 一次性脉冲
public float GetAlertValue(Agent npc);                 // 查询
```

**🧹 清理**：Agent 死亡/失效时，从 `_alertValues` 中移除其条目，防止字典无限增长。

### 3.4 警戒值阶段与视觉（动态颜色绑定）

**颜色方案**：

| 范围 | 阶段 | 背景色 `EyeBgColor` | 填充色 `EyeFillColor` | 含义 |
|------|------|--------|--------|------|
| 0 ~ 1 | 怀疑 | `#FFFFFFFF` (白) | `#FFD700FF` (黄) | 这人在留意我 |
| 1 ~ 2 | 警戒 | `#FFD700FF` (黄) | `#FF0000FF` (红) | 这人很可疑，在找我 |
| ≥ 2 | 质问 | `#FF0000FF` (红) | `#FF0000FF` (红，满) | 马上要来抓我了！ |

**XML：单个眼睛 Widget，颜色动态绑定**（已验证 `Color="@stringProperty"` 可用，参考 `DialogChoice.xml` 中 `Color="@TraitColor"`）：

```xml
<!-- 警戒眼睛：Color 绑定 VM 的 string 属性，Gauntlet 自动做 hex→Color 转换 -->
<Widget IsVisible="@ShowAlert" WidthSizePolicy="Fixed" HeightSizePolicy="Fixed"
        SuggestedWidth="30" SuggestedHeight="20" HorizontalAlignment="Center"
        Sprite="MPGeneral\MPScoreboard\view_profile_icon" Color="@EyeBgColor">
    <Children>
        <!-- Clip 容器：底部对齐，高度=AlertFillHeight，裁掉上部 -->
        <Widget WidthSizePolicy="StretchToParent" HeightSizePolicy="Fixed"
                SuggestedHeight="@AlertFillHeight" VerticalAlignment="Bottom"
                ClipContents="true">
            <Children>
                <Widget WidthSizePolicy="Fixed" HeightSizePolicy="Fixed"
                        SuggestedWidth="30" SuggestedHeight="20"
                        VerticalAlignment="Bottom"
                        Sprite="MPGeneral\MPScoreboard\view_profile_icon"
                        Color="@EyeFillColor" />
            </Children>
        </Widget>
    </Children>
</Widget>
```

**VM 中颜色和填充高度计算**：

```csharp
float maxIconHeight = 20f;

if (alertValue <= 0.01f)
{
    ShowAlert = false;
}
else if (alertValue <= 1f)
{
    ShowAlert = true;
    EyeBgColor = "#FFFFFFFF";   // 白底
    EyeFillColor = "#FFD700FF";  // 黄进度
    AlertFillHeight = alertValue / 1f * maxIconHeight;        // 0~20
}
else if (alertValue <= 2f)
{
    ShowAlert = true;
    EyeBgColor = "#FFD700FF";   // 黄底
    EyeFillColor = "#FF0000FF";  // 红进度
    AlertFillHeight = (alertValue - 1f) / 1f * maxIconHeight; // 0~20
}
else
{
    ShowAlert = true;
    EyeBgColor = "#FF0000FF";   // 纯红
    EyeFillColor = "#FF0000FF";
    AlertFillHeight = maxIconHeight;                           // 满
}
```

### 3.5 AI 行为联动（本次不做，后续单独实施）

> ⚠️ 本次只做 UI 显示。警戒值 → AIStateFlag 的行为联动后续再搞。

---

## 四、性能优化：距离分级

### 4.1 三级距离策略

对所有 Human Agent 生效，但按距离分级处理：

| 距离 | 范围 | 更新频率 | 做什么 |
|------|------|----------|--------|
| **近** | ≤ 15m | 每 10 帧更新逻辑 | 完整：血条 + 警戒值 + 说话 + 坐标 |
| **中** | 15m ~ 50m | 每 30 帧更新逻辑 | 仅警戒值 + 坐标，不显示血条细节 |
| **远** | > 50m | 不处理 | 不创建 HUD / 已有 HUD 标记隐藏 |

### 4.2 近距判断复用 NpcSightSystem

```csharp
// 在 AgentHudMissionView.OnMissionTick 中：
float distSq = agent.Position.DistanceSquared(cameraPos);
float dist = MathF.Sqrt(distSq);

if (dist > MaxDisplayDistance)  // 50m，已有常量
{
    hud.IsVisible = false;
    continue;
}

// 根据距离决定更新内容
bool isClose = dist <= 15f;
if (isClose)
{
    // 全量更新：血条、警戒值、说话等
    hud.UpdateLogic();
}
else if ((i + _tickCounter) % 30 == 0)
{
    // 中距离低频：只更新警戒值
    hud.AlertValue = _sightSystem?.GetAlertValue(agent) ?? 0f;
}
```

### 4.3 延迟创建

不是一开始就给所有 Agent 创建 HUD，而是**按需创建**：
- 有警戒值的 NPC → 创建（中距离即可触发）
- NPC 说话 → 创建
- NPC 进入战斗/拔武器 → 创建
- 其他 NPC → 不创建，省 MBBindingList 条目

**初始扫描保留**但只做轻量注册（记下 Agent 引用），不创建 VM。真正的 VM 创建推迟到首次"有内容要显示"时。

---

## 五、AgentHudMissionView 改造

### 5.1 核心变化

- 类名 `BubbleSayMissionView` → `AgentHudMissionView`
- 不再只管理"说话冒泡"，而是管理每个 Agent 的完整 HUD
- 从 `NpcSightSystem` 读取警戒值
- 按需创建 VM（延迟创建策略）
- 保持现有的视野裁剪、距离缩放、分频更新

### 5.2 OnMissionTick 主循环

```csharp
public override void OnMissionTick(float dt)
{
    // ... 现有逻辑：初始扫描、缓存屏幕参数 ...

    for (int i = _dataSource.Huds.Count - 1; i >= 0; i--)
    {
        var hud = _dataSource.Huds[i];
        var agent = hud.TargetAgent;

        // ── 第一层：基础校验 ──
        if (agent == null || !agent.IsActive()) { /* 标记移除 */ continue; }

        // ── 第二层：距离硬裁剪（50m，所有元素统一） ──
        float dist = agent.Position.Distance(cameraPos);
        if (dist > MaxDisplayDistance) { hud.IsVisible = false; continue; }

        // ── 第三层：屏幕坐标计算（所有元素共用） ──
        // WorldToScreen...

        // ── 第四层：FOV 查询（只影响血条/说话/名字，不影响警戒） ──
        bool inFov = _sightSystem != null && _sightSystem.IsPlayerSeeing(agent);

        // ── 第五层：警戒值更新（FOV 豁免，距离内始终追踪） ──
        float alertValue = _sightSystem?.GetAlertValue(agent) ?? 0f;
        hud.AlertValue = alertValue;

        if (alertValue > 0.01f)
        {
            // 警戒眼睛始终显示，不管 FOV
            // 屏幕外的 → clamp 到边缘做方向指示
            hud.ShowAlert = true;
            hud.PosX = offScreen ? ClampToEdgeX(pixelX) : pixelX;
            hud.PosY = offScreen ? ClampToEdgeY(pixelY) : pixelY;
        }
        else
        {
            hud.ShowAlert = false;
        }

        // ── 第六层：FOV 内的常规元素（血条/说话/名字） ──
        if (inFov)
        {
            // 分频更新
            bool isClose = dist <= 15f;
            int updateInterval = isClose ? 10 : 30;

            if ((i + _tickCounter) % updateInterval == 0)
            {
                hud.UpdateLogic();  // 血条、说话、名字等
            }

            if (hud.IsVisible)
            {
                hud.UpdateFrame(dt);
                hud.PosX = pixelX;   // 屏幕内正常位置
                hud.PosY = pixelY;
                hud.Scale = ComputeScale(dist);
            }
        }
        else
        {
            // FOV 外：常规元素全部隐藏
            hud.ShowHealth = false;
            hud.ShowSpeech = false;
            hud.ShowDamage = false;
            // ShowName 自动跟随 → false（除非 ShowAlert 为 true，容器仍显示眼睛）
        }
    }
}
```

### 5.3 公开 API

```csharp
// 说话（原 AddSpeechBubble，改名）
public void AddSpeech(Agent agent, string text);

// 静态快捷方法（原 AgentBubbleSay，改名）
public static void AgentSay(Agent agent, string text);
public static void AgentSay(string agentStringId, string text);

// 🆕 确保 Agent 有 HUD（警戒值或其他系统调用）
public void EnsureHud(Agent agent);
```

---

## 六、AgentHudVM 属性完整清单

### 6.1 位置/缩放（已有）
| 属性 | 类型 | 说明 |
|------|------|------|
| `PosX` | float | 屏幕 X |
| `PosY` | float | 屏幕 Y |
| `Scale` | float | 距离缩放 |
| `IsVisible` | bool | 容器可见性 |
| `BubbleWidth` | float | 容器宽 |
| `BubbleHeight` | float | 容器高 |

### 6.2 名字
| 属性 | 类型 | 说明 |
|------|------|------|
| `AgentName` | string | NPC 名字 |
| `ShowName` | bool | **总领开关**：任意元素显示则为 true |

### 6.3 说话
| 属性 | 类型 | 说明 |
|------|------|------|
| `SpeechText` | string | 说话内容 |
| `ShowSpeech` | bool | 正在显示说话冒泡 |

### 6.4 血条
| 属性 | 类型 | 说明 |
|------|------|------|
| `CurrentHealthWidth` | float | 当前血量宽度（动画插值） |
| `ShowHealth` | bool | 显示血条 |

### 6.5 伤害
| 属性 | 类型 | 说明 |
|------|------|------|
| `DamageText` | string | 伤害数字 |
| `ShowDamage` | bool | 显示伤害 |

### 6.6 警戒 🆕
| 属性 | 类型 | 说明 |
|------|------|------|
| `AlertValue` | float | 警戒值 0~2+（由 MissionView 每帧注入） |
| `AlertFillHeight` | float | 眼睛填充高度（0~20），VM 内部根据 AlertValue 计算 |
| `ShowAlert` | bool | 警戒值 > 0.01 |
| `EyeBgColor` | string | 眼睛底色 hex（`"#FFFFFFFF"` / `"#FFD700FF"` / `"#FF0000FF"`） |
| `EyeFillColor` | string | 眼睛填充色 hex（`"#FFD700FF"` / `"#FF0000FF"`） |

---

## 七、XML Prefab 改造要点

### 7.1 根容器显隐

```xml
<Widget ... IsVisible="@IsVisible">
```

`IsVisible` 由 VM 的 `ShowName` 驱动。

### 7.2 各元素独立显隐

```xml
<!-- 警戒眼睛：单个 Widget，颜色动态绑定 -->
<Widget IsVisible="@ShowAlert" Sprite="..." Color="@EyeBgColor">
    <Widget ClipContents="true" SuggestedHeight="@AlertFillHeight">
        <Widget Sprite="..." Color="@EyeFillColor" />
    </Widget>
</Widget>

<!-- 名字 -->
<RichTextWidget IsVisible="@ShowName" Text="@AgentName" ... />

<!-- 说话 -->
<RichTextWidget IsVisible="@ShowSpeech" Text="@SpeechText" ... />

<!-- 血条区域 -->
<Widget IsVisible="@ShowHealth"> ... </Widget>

<!-- 伤害数字 -->
<RichTextWidget IsVisible="@ShowDamage" Text="@DamageText" ... />
```

### 7.3 布局顺序

`VerticalBottomToTop` 实际表现为 TopToBottom，从上到下：

```
  1. 警戒眼睛 (ShowAlert)    ← 最上方
  2. 名字 (ShowName)
  3. 说话冒泡 (ShowSpeech)
  4. 伤害数字 (ShowDamage)
  5. 血条 (ShowHealth)        ← 最下方
```

---

## 八、实施顺序

### Phase 1：原地改名（行为不变）
1. `BubbleSayVM.cs` → `AgentHudVM.cs`，类名 `BubbleSayVM` → `AgentHudVM`
2. `BubbleSayNeaybyVM.cs` → `AgentHudCollectionVM.cs`，类名改名
3. `BubbleSayMissionView.cs` → `AgentHudMissionView.cs`，类名改名
4. `BubbleSayNearby.xml` → `AgentHudNearby.xml`
5. 全局替换所有引用（`InteractionMissionView`、`MyCommands` 等）
6. `MySubModule.cs` 中 `AddMissionBehavior` 注册名更新
7. **编译 + 游戏内验证**：行为与旧版完全一致

### Phase 2：新显隐规则（本次核心，不涉及警戒值）
1. `AgentHudVM` 新增 `ShowName` 属性 + 名字总领逻辑
2. 细化 `ShowHealth` 条件（增加 `IsAlarmed`/`IsCautious` 判断）
3. `UpdateLogic` 重构：各元素独立计算 Show*
4. XML：各元素绑定独立 `IsVisible`（名字/说话/血条/伤害）
5. **编译 + 游戏内验证**：血条/说话/伤害显隐符合新规则

### Phase 3：警戒值计算（NpcSightSystem）
1. `NpcSightSystem` 新增 `_alertValues` 字典 + 清理逻辑
2. 实现 `GetAlertValue` / `AddAlertPulse`
3. 在 `TickTrackedTarget` 中为能看到玩家的 NPC 计算 `alertDelta`
4. 玩家蹲下检测、脉冲事件挂钩（`AddAlertPulse` 预留接口，具体事件调用后续再补）
5. `custom.stealth_status` 输出中包含自定义警戒值
6. **编译 + 游戏内验证**：控制台能看到警戒值变化

### Phase 4：警戒值 UI 渲染
1. `AgentHudVM` 新增警戒属性（`AlertValue`, `AlertFillHeight`, `ShowAlert`, `EyeBgColor`, `EyeFillColor`）
2. `AgentHudMissionView.OnMissionTick` 从 `NpcSightSystem` 读取警戒值传入 VM
3. XML：单个眼睛 Widget + 动态颜色绑定 + Clip 填充
4. 性能优化：远距离 Agent 不创建 HUD / 中距离只更新警戒值
5. **编译 + 游戏内验证**：眼睛图标随警戒值变化

### Phase 5：收尾
1. 删除旧 `Bubble*` 文件（已改名，旧的不留）
2. 更新 `wheels.md`：新增 "AgentHUD 通用头上系统" 条目
3. 控制台指令 `bubbleSay` → `agentHud_say`（直接改名，不保留别名）

---

## 九、设计哲学对照检查

| 原则 | 检查 |
|------|------|
| ① 明确反馈 | ✅ 警戒值从 0→1→2 逐步可视化，三个阶段颜色变化清晰 |
| ② 自由感 | ✅ 玩家实时看到谁在警惕自己 → 决定绕开/继续/放弃 |
| ③ NPC 接得住 | ✅ 所有 Human Agent（平民/守卫/村民）都有警戒值，不只是守卫 |
| ④ 信息塑造目标 | ✅ 眼睛图标告诉玩家"这个人盯上你了"，暗示下一步行动 |

---

## 十、KCD2 水准自检

- 玩家蹲下靠近村庄 → NPC 警戒值缓慢上升 → 白底黄进度 → 黄底红进度 → 纯红 → **玩家实时感知到"我被注意了"** ✅
- 玩家偷东西瞬间 → 附近 NPC 警戒值暴增 → 红色眼睛闪烁 → **像原生游戏一部分** ✅
- 不相关 NPC（背对玩家/远处）→ 头上干干净净，HUD 不创建 → **信息不淹没 + 性能友好** ✅
- 名字在"有事发生时才显示"→ 围观战斗时名字浮现 → **不破坏沉浸感** ✅
- 远距离不运算、中距离低频更新 → **性能可控** ✅
