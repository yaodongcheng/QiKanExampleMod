# 输入系统升级：短按/长按 + 键位冲突修复 + config.json 化

> 状态：待评审。对应玩家反馈："手柄踢人和我们一个功能重合"。
> 参考 UE4 Action Mapping 的 Pressed/Released 双沿设计；长按进度条视觉参考 KCD（天国拯救）小圆圈。

---

## 1. 背景与目标

1. **双沿输入**：目前所有业务输入都监听 press（`ModInput.Pressed`），无法区分短按/长按。升级为 pressed + released 双沿状态机，**需要确认的互动（犯罪、战斗中的认输决策、搜刮）改成长按，给玩家确认时间；普通互动保持短按**（用户拍板原则）。
2. **UI 区分长按**：长按项在按键提示旁显示进度（KCD 小圆圈为参考，实现为 **4 段进度条组成的正方形框**——用户拍板）；短按项不显示。键帽底图从圆形改为**方块**。
3. **键位冲突核查 + 可配置**：全面核对原版键位，修复"手柄 X=踢人"等冲突；玩法键位移入 **config.json**（不进 MCM——不暴露给小白玩家，资深玩家可改）。

---

## 2. 现状盘点（已核实）

### 输入层 `Input/ModInput.cs`
- `enum ModInputAction { Interact, AltInteract, Inspect, StealAttempt, StealLeave }`，映射表 `Map`（静态只读，键=动作，值=Binding{键盘/手柄/三套字形}）。
- 已有 `Pressed` / `Released` / `Glyph` / `UsingGamepad` / `IsPlayStation`——**双沿基础设施已存在，缺的是"短/长按判定"状态机**。
- 手柄键位对照：`RDown=A/✕　RRight=B/○　RUp=Y/△　RLeft=X/□　LThumb=L3`（ilspycmd 核实 InputKey 枚举：`ControllerLUp..LRight`=十字键 240-243，`ControllerRUp..RRight`=XYAB 244-247，`ControllerLThumb/RThumb`=L3/R3 252-253）。

### 业务消费点（全部在 `Interaction/InteractionMissionView.cs`）
| 位置 | 当前监听 | 行为 |
|------|---------|------|
| `:375-379` | `Pressed(Interact)` | 近箱子开保管箱（撬锁入口） |
| `:384-420` | `Pressed(Interact)` | 大杂烩：偷动物 / 搜刮 / 认输 / 背后击晕 / 背后扒窃 / 对话 |
| `:421-430` | `Released(AltInteract)` | 接受认输 / 闲聊（已是 released 沿） |
| `:735-741` | `Released(Inspect)` | 探查信息板（已是 released 沿） |
| `:1017-1019` | `Pressed(StealAttempt/Leave)` | 偷窃条节奏玩法（按下出手/收手） |
| `:1073-1096` | — | `FreezePlayerControl()` 偷窃/扒窃期间冻结玩家控制（`V.SetPlayerControlFrozen`） |

### UI 层
- `GUI/Prefabs/InteractArea.xml`：按键提示 = `Sprite="BlankWhiteCircle"` 圆徽 30×30 + `@KeyText`；动作名 `@ActionText`。
- `Interaction/InteractionVM.cs`：`InteractionItemVM{ActionText, KeyText, _inputAction}`，`UpdateTarget(name, List<(string, ModInputAction?)>)` 构建列表；`RefreshGlyphs()` 设备切换刷新。

### 关键既有防线
- `InteractionMissionView.cs:2503` `AgentInteractPatch`（Harmony Prefix `MissionConversationLogic.OnAgentInteraction` → return false）：**原版对话/互动入口已被 mod 拦截**（战斗等场景放行）。
- 偷窃条 Space/A 与跳跃的冲突：**靠冻结玩家控制解决**（`FreezePlayerControl`），用户确认此方案有效、保持不动。

---

## 3. 原版键位冲突核查（ilspycmd 实测 v1.4.7 编译产物）

### 3.1 键盘（`CombatHotKeyCategory` 等，MountAndBlade.dll）

| 我们的键 | 原版占用 | 冲突等级 | 处理 |
|---------|---------|---------|------|
| F（Interact） | `Action`=F（原版互动/对话键） | 中 | **保留 F**。对话入口已被 AgentInteractPatch 拦截；世界物互动（门/马）残留重叠，与现状相同，不动 |
| G（AltInteract） | `DropWeapon`=G（丢武器） | 🔴 高 | **改绑 Q** |
| Space（StealAttempt） | `Jump`=Space | 低 | 保留（冻结机制已解决，用户确认） |
| Tab（StealLeave） | `Leave`=Tab（菜单层） | 低 | 保留（mission 内无副作用） |
| H（Inspect） | 仅 `PhotoMode.HideUI`=H | 无 | 保留 |

**Q 键可用性已核实**：原版 Q 仅 `MapRotateLeft`（大地图）与 `CameraRollLeft`（拍照模式），**mission 内完全空闲**。E=踢人、Z=蹲、R=切视角、X=切换武器模式均被占用，不可用。

### 3.2 手柄

| 我们的键 | 原版占用 | 冲突等级 | 处理 |
|---------|---------|---------|------|
| X/□（Interact） | 🔴 **`Kick`=ControllerRLeft** | 🔴 致命（玩家反馈的 bug） | **改绑** |
| Y/△（AltInteract） | `Action`=ControllerRUp（原版"与人互动"=Y） | 🔴 高（双重触发） | **改绑** |
| A/✕（StealAttempt） | `Jump`=ControllerRDown | 低 | 保留（冻结机制） |
| B/○（StealLeave） | `Leave`=ControllerRRight | 低 | 保留（语义一致） |
| L3（Inspect） | `CameraToggle`=ControllerLThumb（切第一/第三人称） | 高（城镇常用） | **改绑 R3** |

**十字键也基本被占**：`LUp=Cheer`、`LDown=Crouch`、`LLeft=ViewCharacter`、`LRight=PushToTalk`（多人）——全部不可用作主互动键。

### 3.3 为什么不能"拦截踢人"，只能改绑
Kick 的输入消费点在 **native 引擎层**（ilspycmd 全 DLL 搜不到 managed 消费点，只有 `AgentAttackType.Kick` 攻击类型与网络踢人），Harmony 补丁管不到。改键位是唯一可靠方案。

---

## 4. 设计决策

### 4.1 短按/长按状态机（UE4 Action Mapping 风格）

`ModInput` 增加每玩法行状态机，**按帧驱动**（调用方 = `InteractionMissionView.OnMissionTick`）：

```
Idle ──按──▶ PressedAt(t0) ──按住 ≥ 阈值──▶ Ready（满框待命，金色提示"可松手"）
                      │                            │
                      └─阈值前松开─▶ 取消           │ 保持按住 = 继续待命（等目标转身/目击者走开）
                                                     │ 松开 = LongFired（触发一次）──▶ Idle
```

**互斥保证**：长按进入 Ready 后，松开触发长按（不触发短按）；短按在阈值前松开时触发。**同一玩法行短按/长按二者必居其一，不双重触发**。长按为 KCD 语义——**满框不自动执行，玩家选时机松手才执行**（用户拍板，2026-08-06；原"跨阈值瞬间触发"废弃）。

```csharp
// 新增 API（Input/ModInput.cs）——状态机按玩法行跟踪，事件/查询按玩法 ID
public static void Tick(float dt);                    // 驱动全部玩法行状态机（MissionTick 每帧调用）
public static bool ShortFired(string interactionId);  // 短按：松开且按住时长 < 该玩法阈值的一瞬间
public static bool LongFired(string interactionId);   // 长按（KCD）：满框待命后松开的一瞬间（时机由玩家掌控）
public static bool PressedFired(string interactionId);// 按下沿（节奏玩法专用：偷窃条出手/收手，配置层仍只有 Short/Long）
public static bool IsHeld(string interactionId);      // 按住中（供进度 UI）
public static float HoldProgress(string interactionId);// 0..1（按住时长 / 该玩法阈值）
public static void Reset(string interactionId);       // 目标丢失/UI 隐藏时取消按住状态
// ModInputAction 枚举保留为内部语义分组（兼容存量调用），业务侧只用玩法 ID
```

**阈值进 config.json**：`LongPressDurationMs` 默认 **450ms**（KCD 手感）；短按判定 = 阈值前松开。

**按法 = 配置维度（PressMode）**：按法下沉到**玩法交互**层——不是"Interact 这个动作是长是短"，而是**"对话=短按 F、击晕=长按 F"**，每个玩法交互独立配置（键 + 按法）。**玩家只区分两种按法**：

```csharp
public enum ModInputPressMode { Short, Long }
// Short  短按：快速按下松开即触发（按住超过阈值则取消，转入长按路径）
// Long   长按（KCD 语义）：按住蓄力跨越阈值进入待命（UI 进度框满、变金色），
//        玩家选时机松开才执行；阈值前松开不触发
```

**状态机按玩法行跟踪，同键共享一次物理按下**（同一物理键可挂多个玩法交互）：
- `ModInput` 对**每个玩法行**维护独立状态（该行当前是否按住、按下时刻、该行自己的阈值 = `HoldMs ?? LongPressDurationMs`）；**同一物理键按下时，挂该键的全部玩法行同时进入计时**，各自按各自阈值触发各自事件；
- 对外事件按玩法 ID：`ShortFired(id)`（松开且未超该行阈值）/ `LongFired(id)`（**满框待命后的松开沿**——KCD 语义，玩家掌控执行时机）；`IsHeld(id)` / `HoldProgress(id)` 供进度 UI；
- 调用点按**当前上下文可见的玩法行**消费：对话行挂 (F, Short) → 听到 `ShortFired("Talk")` 触发；击晕行挂 (F, Long) → 满框待命后松开听到 `LongFired("Knockout")` 触发——**同一个 F 按下，短/长互斥，谁可见谁响应**；
- 配置把击晕改成 `"Keyboard": "E"` → 状态机自动跟踪 E，击晕在 E 上长按触发，**调用点代码零修改**。
- 语法层面无"Both / Press / Release"——一键多义由"多个玩法行挂同一物理键"表达；探查（原松开沿）收敛为 Short（体验一致，见下表注）。
- ⚠️ 偷窃条出手/收手原为**按下沿即时触发**：Short 的松开触发会晚一次点按（节奏手感发粘）——**已实施 plan 兜底：内部保留按下沿判定（`PressedFired`），偷窃条消费按下沿，配置层仍只有 Short/Long**（2026-08-06 实装）。

**长按归属表**（原则一句话：**①触发犯罪/目击/严重后果 → 长按（给玩家确认时间）；②战斗中的重大决策（认输/接受认输）→ 长按；③搜刮 → 一律长按，无论对象是谁（保持玩家习惯）；否则短按**。实施时逐项核对 WitnessSystem 触发点微调；玩法 ID 即 §4.2 配置键名，键列为默认值；**所有 Long 玩法 = 满框待命后松手执行（KCD 语义）**）：

| 玩法交互（ID） | 键 | 按法 | 理由 |
|----------------|-----|------|------|
| Talk 对话 | F | Short | 无风险 |
| Loot 搜刮（已死尸体 / 死动物 / 昏迷者） | F | Long | 一律长按（用户拍板：保持玩家习惯，无论对象） |
| PlayerSurrender 玩家认输（投降） | F | Long | 战斗中的重大决策（用户拍板） |
| Knockout 击晕 | F | Long | 攻击他人 = 犯罪 |
| Pickpocket 扒窃 | F | Long | 盗窃 = 犯罪（KCD：偷窃按住同款） |
| StealAnimal 偷活物 | F | Long | 盗窃 = 犯罪 |
| Lockpick 撬锁开箱 | F | Long | 开锁取物 = 偷盗 |
| AcceptSurrender 接受认输 | Q | Long | 战斗中的决策（用户拍板）。当前是 Released 沿，改为长按阈值触发 |
| Inspect 探查信息板 | H | Short | 原 released 沿收敛（点按松开触发，体验一致） |
| StealAttempt / StealLeave 偷窃条 | Space / Tab | Short | 按下沿即时触发（内部 `PressedFired` 通道，与旧实现手感一致——已实装，非松开触发）；节奏玩法，冻结机制已隔离 |

> 闲聊已屏蔽（`EnableSmallTalk` 现状关闭），不参与键位与长按分配；若未来恢复，走"同键多上下文"分发（见下），无需改键位。
> 同键多交互（F 挂 7 个玩法）：一次物理按下 = 挂 F 的全部玩法行同时计时，各按各的阈值与按法触发（互斥），运行时 UI 只显示当前上下文可用的玩法行，该行消费自己的事件——对话行听到 `ShortFired("Talk")`，击晕行听到 `LongFired("Knockout")`。互不干扰。

### 4.2 键位默认值调整 + config.json 化

#### 默认交互表（全部可在 config.json 覆盖）

分配逻辑（按 频率 × 语义 从高到低排，先保主互动，再保低频决策）：

| 玩法交互 | 键盘 | 手柄 | 按法 | 合理性 |
|---------|------|------|------|--------|
| Talk 对话 | F | Y/△ | Short | 主互动同键，原版 Action 肌肉记忆；手柄"Y=说话"惯例，根除踢人冲突 |
| Loot 搜刮 | F | Y/△ | Long | 一律长按（用户拍板：保持玩家习惯，无论对象） |
| Knockout 击晕 | F | Y/△ | Long | 攻击他人 = 犯罪 |
| Pickpocket 扒窃 | F | Y/△ | Long | 盗窃 = 犯罪（KCD：偷窃按住同款） |
| StealAnimal 偷活物 | F | Y/△ | Long | 盗窃 = 犯罪 |
| Lockpick 撬锁开箱 | F | Y/△ | Long | 开锁取物 = 偷盗 |
| PlayerSurrender 玩家认输 | F | Y/△ | Long | 战斗中的重大决策 |
| AcceptSurrender 接受认输 | Q | LB | Long | 战斗决策；G=丢武器（战斗瞬间掉武器）不可用，E=踢人/R=切视角被占，Q 是 mission 空闲键；手柄：LB=ShowIndicators 纯视觉城镇安全 |
| Inspect 探查 | H | R3 | Short | 原 released 沿收敛为 Short（点按松开触发，体验一致）；H 原版仅拍照模式空闲；L3=切视角冲突，R3=右摇杆点击（"鹰眼/情报"惯例位），原版 R3=LockTarget 仅战斗生效城镇空闲 |
| StealAttempt 偷窃条出手 | Space | A/✕ | Short | 原按下沿收敛为 Short（松开触发，晚一次点按 ~100ms，手感见 §6 验证）；冻结机制已解决跳跃冲突（用户确认） |
| StealLeave 偷窃条收手 | Tab | B/○ | Short | 原按下沿收敛为 Short；与原版 Leave 语义一致 |

> 同键多交互（F 上挂了 7 行）= 运行时按上下文只显示当前可用的一行（对话场景显示 Talk、背后场景显示 Knockout/Pickpocket），一次按下由可见行消费。
> 需实机验证的项（写进验证清单）：SP 下 LB=ShowIndicators 是否无副作用；R3 城镇是否无动作；D-pad 上=Cheer 在 SP 是否生效（若 LB 意外触发问题，备选 AcceptSurrender=D-pad 上）。

#### Settings 新增（config.json 侧，不进 MCM）

**交互项 = 配置一等公民**：每个玩法交互一行，自报 (键盘键, 手柄键, 按法)。同一物理键可挂多行；玩家改一行只影响该玩法。

```csharp
// Core/Settings.cs
public class InteractionBindingConfig
{
    public string Keyboard { get; set; } = "";   // 键盘：InputKey 枚举名（"F"/"Q"/"Space"…，枚举名即人话）
    public string Gamepad { get; set; } = "";    // 手柄：人类可读别名（"Y"/"LB"/"R3"…，见别名表）
    public string PressMode { get; set; } = "";  // Short / Long（空 = 内置默认）
    public int HoldMs { get; set; } = 0;         // 可选：覆盖全局 LongPressDurationMs（0 = 用全局）
}
public Dictionary<string, InteractionBindingConfig> Interactions { get; set; }   // 键 = 玩法 ID，默认填全表（示例即真实默认值）
public int LongPressDurationMs { get; set; } = 450;
```

```jsonc
// ══════════════════════════════════════════════════════════════
// 玩法键位配置（键名 = 玩法 ID；删行/改值即改键，重启或控制台热重载生效）
//
// ── Gamepad 用"逻辑键"命名（Xbox 惯例），PS 玩家对照表：──────
//    Y  = △ 三角     A  = ✕ 叉       X  = □ 方块     B  = ○ 圆圈
//    LB = L1         RB = R1         LT = L2          RT = R2
//    L3 = 左摇杆按下  R3 = 右摇杆按下
//    DUp/DDown/DLeft/DRight = 十字键 上 / 下 / 左 / 右
//    View = 触控板    Menu = Options
//    PS 玩家也可直接写 PS 名（Triangle / Cross / Square / Circle
//    / L1 / R1 / L2 / R2 / Options…），与 Xbox 名等价；显示时按当前
//    手柄自动切换字形（配置写 Y，Xbox 显示 Y、PS 显示 △）。
//
// ── PressMode：Short=短按（快速按下松开即触发）/ Long=长按（按住蓄力，
//    UI 显示进度框，满框待命（变金色），松手执行）
// ── HoldMs：可选，单独调该玩法的长按阈值（毫秒）；0 或省略 = 用全局
//    LongPressDurationMs
// ══════════════════════════════════════════════════════════════
"Interactions": {
  "Talk":            { "Keyboard": "F",     "Gamepad": "Y",  "PressMode": "Short" },
  "Loot":            { "Keyboard": "F",     "Gamepad": "Y",  "PressMode": "Long" },
  "Knockout":        { "Keyboard": "F",     "Gamepad": "Y",  "PressMode": "Long" },
  "Pickpocket":      { "Keyboard": "F",     "Gamepad": "Y",  "PressMode": "Long" },
  "StealAnimal":     { "Keyboard": "F",     "Gamepad": "Y",  "PressMode": "Long" },
  "Lockpick":        { "Keyboard": "F",     "Gamepad": "Y",  "PressMode": "Long" },
  "PlayerSurrender": { "Keyboard": "F",     "Gamepad": "Y",  "PressMode": "Long" },
  "AcceptSurrender": { "Keyboard": "Q",     "Gamepad": "LB", "PressMode": "Long" },
  "Inspect":         { "Keyboard": "H",     "Gamepad": "R3", "PressMode": "Short" },
  "StealAttempt":    { "Keyboard": "Space", "Gamepad": "A",  "PressMode": "Short" },
  "StealLeave":      { "Keyboard": "Tab",   "Gamepad": "B",  "PressMode": "Short" }
  // 可选：单独调阈值，如击晕想更久： "Knockout": { ..., "HoldMs": 600 }
},
"LongPressDurationMs": 450
```

**Gamepad 值用玩家能懂的名字**（禁止引擎内部名 `ControllerRUp`——玩家会以为是"右摇杆向上"）。配置写**逻辑键**，Xbox 名与 PS 名等价解析，**归一到同一引擎键**（Xbox `"Y"` 和 PS `"Triangle"` → 同一 `ControllerRUp`，共享同一次物理按下）；**显示字形按当前手柄类型实时切换**（`ModInput.IsPlayStation`，两套字形）：

| 配置写法（大小写不敏感） | 引擎键 | Xbox 显示 | PS 显示 |
|--------------------------|--------|-----------|---------|
| `Y`（兼容 `Triangle`） | ControllerRUp | Y | △ |
| `A`（兼容 `Cross`） | ControllerRDown | A | ✕ |
| `X`（兼容 `Square`） | ControllerRLeft | X | □ |
| `B`（兼容 `Circle`） | ControllerRRight | B | ○ |
| `LB`（兼容 `L1`） | ControllerLBumper | LB | L1 |
| `RB`（兼容 `R1`） | ControllerRBumper | RB | R1 |
| `LT`（兼容 `L2`） | ControllerLTrigger | LT | L2 |
| `RT`（兼容 `R2`） | ControllerRTrigger | RT | R2 |
| `L3` | ControllerLThumb | L3 | L3 |
| `R3` | ControllerRThumb | R3 | R3 |
| `DUp` / `DDown` / `DLeft` / `DRight` | ControllerLUp / LDown / LLeft / LRight | ↑ / ↓ / ← / → | ↑ / ↓ / ← / → |
| `View`（兼容 `Touchpad`） | ControllerLOption | View | Touchpad |
| `Menu`（兼容 `Options`） | ControllerROption | Menu | Options |
| （兜底）引擎枚举名 `ControllerRUp` 等也接受 | | | |

- **逻辑键归一**：Xbox 名 / PS 名 / 引擎枚举名三种写法解析到同一引擎键后，**Xbox 与 PS 玩家共用同一份配置**——PS 玩家不用把配置改成 Triangle，显示层自动给 △；想用 PS 名写也行。
- **config.json 顶部内嵌对照注释**：实施时 config 文件自带上述 Xbox↔PS 对照表（见下方示例注释块，`Y=△ 三角`、`LB=L1`…），PS 玩家打开文件即见对应关系，无需查文档。
- 字形两套表与上表同源（显示 = 别名表反查），Xbox 玩家看到 `Y`、PS 玩家看到 `△`；键盘 glyph 派生自按键名（`Space`/`Tab` 走既有 `LWN_input_key_space` 本地化先例，铁律 13）。
- 设备切换：`InteractionMissionView.OnMissionTick` 已有的 `UsingGamepad` 变化检测 → `RefreshGlyphs()` 刷新全部按键提示（现有机制，字形表扩展即可）。

- `ModInput` 的静态映射表改为**由 Settings 构建**（懒加载 + `Settings.Reload()` 后重建，提供 `RebuildBindings()`）。
- 解析顺序：Gamepad 先查别名表（大小写不敏感）→ `Enum.TryParse<InputKey>` 兜底；键盘直接枚举名；PressMode 走 `Enum.TryParse<ModInputPressMode>`。**全部失败 → 该项回落内置默认 + `DebugLogger.Log` 警告**（铁律 2 风格防御）。
- 解析后按**玩法行**重建状态（F 按下 → 挂 F 的 7 行同时计时，各按各自阈值/HoldMs 触发）；字形显示按配置反查上表（键盘 glyph 派生自按键名，`Space`/`Tab` 走既有 `LWN_input_key_space` 本地化先例；手柄 glyph 按 `IsPlayStation` 查上表 Xbox/PS 两列，铁律 13）。
- **配置冲突校验**：同一物理键 + 同一按法挂多个玩法行（如玩家把 Talk 和 Inspect 都设 F/Short）→ `DebugLogger.Log` 警告"同键同按法多玩法，同时可用时可能双触发"（玩家自担责，运行时照常执行）。
- 阈值：交互项级 `HoldMs`（>0 覆盖全局）→ 否则全局 `LongPressDurationMs`。
- 热重载：复用/新增控制台指令触发 `Settings.Reload()` + `ModInput.RebuildBindings()`。
- **不进 MCM**：小白玩家看不到（用户决策）；双配置纪律无交叉。

### 4.3 UI 改造（InteractArea.xml + InteractionVM）

**键帽改方块**：`Sprite="BlankWhiteCircle"` → `Sprite="BlankWhiteSquare_9"`（mod 已在用的 sprite），30×30 保持。

**进度条：4 段方框**（用户拍板——12 段小方块方案被否，体验不连续）：
- 方块键帽内嵌 4 条进度条组成**正方形框**：上/右/下/左各 1 条（厚 3px，距边 2px），围绕居中键名，**顺时针填充（上 → 右 → 下 → 左）**——类似"方块加载框"，KCD 空心圈同款思路（平时淡白空心框提示"这里要按住"，按住时逐边点亮成实框）。
- **实现（零新增依赖，复用血条模式）**：纯 XML Sprite + `SuggestedWidth/@float`、`SuggestedHeight/@float` 绑定——即 `GUI/Prefabs/AgentHudNearby.xml` 血条（`SuggestedWidth="@CurrentHealthWidth"` 每帧改宽度，99-102 行）与警戒眼睛（`SuggestedHeight="@AlertFillHeight"` 竖填充，40-52 行）的现成模式，**该 XML 在 1.2.12 / 1.3.15 / 1.4.6 三版本均已实机运行**。
  - 每条 = 白色进度条底（**常显 100% 纯白 `#FFFFFFFF`**——没蓄力时的空白状态）+ 进度填充（`Color=@SegColor`，**蓄力中黑 `#000000FF` → 蓄力完成金 `#FFE97FFF`**，覆盖白底之上，100% 不透明，声明在底之后渲染在上层）；三色方案（用户拍板 2026-08-06）：①没蓄力=白边 ②蓄力中=黑（进度覆盖到哪哪变黑，顺时针推进）③完成=金（待命"可以松手"）；填充条 `WidthSizePolicy="Fixed"` / `HeightSizePolicy="Fixed"`，尺寸绑 VM 每帧算好的像素值。
  - **键帽底纹按按法区分（用户拍板 2026-08-06）**：**Short 纯白 `#FFFFFFFF`、无四周边；Long 青绿 `#B5F0E8FF` + 白色四周边**——玩家一眼看出交互方式（键帽底色 = 按法标识）；键名黑字 `#000000FF`（白/青绿底上均可读）。
  - 🔴 引擎颜色只支持 `#RRGGBBAA`（8 位 hex）：① 6 位 hex 会在 Alpha 解析时 `Substring` 越界崩溃（实机踩过）；② **顺序是 RRGGBBAA（alpha 在最后两位）**——写黑色若按 HTML 习惯写 `#FF000000`，在引擎里 = **R=255,G=0,B=0,A=00 → 全透明红，永远不可见**（实机踩过：进度黑条绑定版/写死版均无影，白边 `#FFFFFFFF` 恰好两序同义所以正常）；**纯黑必须写 `#000000FF`**。写颜色时按"RR GG BB AA"四段核对 alpha 结尾是 FF。
  - 条长常量 L=24px，第 i 条填充长度 = `clamp(progress*4 − i, 0, 1) × L`（i=0..3）。
  - 锚定方向构成**顺时针连续闭环**（左上→右上→右下→左下→左上，段间无跳变）：上条 `HorizontalAlignment="Left"` + `VerticalAlignment="Top"`（左→右）→ 右条 `HorizontalAlignment="Right"` + `VerticalAlignment="Top"`（上→下）→ 下条 `HorizontalAlignment="Right"` + `VerticalAlignment="Bottom"`（右→左）→ 左条 `HorizontalAlignment="Left"` + `VerticalAlignment="Bottom"`（下→上）。
    - ⚠️ **方向易错点（实机踩过两版）**：① Gauntlet 双轴独立，每条必须同时显式写两个轴（缺一个默认 Left/Top，全堆左上角）；② 右条/左条若用 Bottom/Top 锚定会变成逆时针 + 段间跳变（顶边→右缘↑→底边←→左缘↓），正确应为右条 Top、左条 Bottom。
  - ⚠️ **Gauntlet 双轴独立坑（实机踩过）**：`HorizontalAlignment` 与 `VerticalAlignment` 是独立两轴，每条必须**同时显式写全两个轴**——只写一个轴时未写轴默认 `Left/Top`，右条/下条会全部堆到左上角与左条/上条重叠（表现为"四条边只在左和上"）。
- 短按项：整个方框 `IsVisible=false`（用户要求：短按无进度条）。
- ⚠️ **不采用 `FillBarWidget`**（三版本核实：数值条 `CurrentAmount/MaxAmount` 整数、无方向属性，无法平滑竖填），改用上述血条绑定模式。

**VM 改造**（`InteractionVM.cs`）：
- `InteractionItemVM` 新增：`RequiresHold`（bool，= 该玩法配置 `PressMode == Long`）、`SegFillWidth0` / `SegFillHeight1` / `SegFillWidth2` / `SegFillHeight3`（4 个像素 float，条长 L=24：`clamp(progress*4 − i, 0, 1) × L`）、`FrameVisible`（= RequiresHold）。
- `UpdateTarget` 的 actions 元组扩为 `(string action, string interactionId)`——键位/按法由 `ModInput.GetBinding(interactionId)` 从配置取，UI 与输入共享同一份配置。
- `OnMissionTick`：对 `PressMode==Long` 的可见项每帧 `progress = ModInput.HoldProgress(该玩法 ID)` → 重算 4 个像素值（触发 OnPropertyChanged）。
- 设备切换刷新逻辑（`RefreshGlyphs`）不变。
- **requiresHold 由配置推导**：玩法行 `"PressMode": "Long"` → UI 显示进度框；玩家把该行改 `"Short"` → 进度框消失、变点按，**UI 与行为一起跟随配置，无需任何代码改动**。

### 4.4 取消机制
- 目标丢失 / `_interactVM` 隐藏 / `IsHandlingInteraction` 归 false → `ModInput.Reset(玩法 ID)`，进度框立即消退（不残留）。
- **available 列表变化**（§4.5）：退出列表的玩法自动 `Reset`（长按作废、进度框消退），上下文切换不误触发。

### 4.5 显隐与响应统一（上下文 = 唯一真相源）

**问题**：现有代码里"按钮显隐"（`PerformPerformanceHeavyLogic` 每 3 帧构建 actions）与"按键响应"（`HandleInput` 每帧分发）是**两棵独立的 if-else 树**，靠同一组缓存变量（`_lastWasAnimal` / `_lastIsBehind` / `_lastNpcIntentType`…）"巧合同步"——改一边忘另一边就会出现"UI 显示按钮但按键不响应 / 按键响应但没显示"的错位（眼睛和键盘对不上）。

**方案**：上下文判定收敛为**唯一真相源**——`PerformPerformanceHeavyLogic` 构建一次"当前可用玩法 ID 列表"，**同一份列表同时驱动显隐与响应**：

```csharp
// 1. 上下文 → 可用玩法列表（忠实复刻现有显示条件，行为不变）
List<string> available = BuildAvailableInteractions();   // 例：[Knockout, Inspect]

// 2. UI 显示：列表 → (本地化文本, 玩法 ID) → _interactVM.UpdateTarget(...)
// 3. 按键响应：HandleInput 遍历 available，命中即执行（不同键可同帧各自触发）：
foreach (string id in _availableIds)
{
    if (ModInput.LongFired(id))  ExecuteInteraction(id);
    if (ModInput.ShortFired(id)) ExecuteInteraction(id);
}
// ExecuteInteraction(id)：静态 switch 玩法 ID → TryKnockoutAgent / TryStealFromAgent / OpenChest / ...
// 无 break：同物理键的玩法行互斥由状态机保证（一次按下只短或长其一），
// 不同键（如长按 F 途中点 H）各自命中各自执行，不互相吞事件。
```

- **响应门控**：`HandleInput` 的调用条件从 `_interactVM.IsVisible`（现状 755 行）改为 **`available 非空`**——`IsVisible` 只管 UI 显示；无目标场景 available=[Inspect] 且 UI 隐藏时，探查键依然响应（保留现状"无 focus 看自己"）。
- **Inspect 迁入统一通道**：删除现状 `OnMissionTick` 735-741 行的全局监听（探查键），行为由 available=[Inspect] 承接——UI 有按钮与无按钮两种场景共用同一响应路径。

- **分层明确**：config 只管"键 + 按法"；显隐/生效条件仍在代码（上下文构建）——玩家改 config 改不了上下文，两者通过 available 列表桥接。
- **上下文清单**（现状复刻，行为不变）：动物活+蹲=`[StealAnimal]`；动物死=`[Loot]`；活人战斗意图=`[PlayerSurrender(+AcceptSurrender)]`；背后+蹲=`[Pickpocket, Inspect]`；背后=`[Knockout, Inspect]`；正面=`[Talk, Inspect]`；昏迷/尸体=`[Loot]`；**无目标=`[Inspect]`**（保留现状"探查键无 focus 看自己"，统一为迷你列表，不再游离于响应树外）；**近箱子=`[Lockpick]`**（原箱子分支并入）。
- **偷窃条 = 独立输入通道（available 体系之外）**：`TickStealBar` 打开期间独占输入（现状 687-691 行先于 available 逻辑 return），直接监听 `ShortFired("StealAttempt")` / `ShortFired("StealLeave")`，不走 available——节奏玩法与上下文列表解耦；玩家控制冻结（`FreezePlayerControl`）不影响状态机（冻结在 Agent 控制层，物理键轮询照常）。
- **available 变化 → 失效清理**：列表变化时，**退出项与进入项均** `ModInput.Reset(id)`——退出：长按中目标转身/走开，进度框立即消退、不会误触发；进入：清零跨上下文继承的按住电荷（状态机按物理键计时，同键挂多行同时计时——若沿用上一上下文的电荷，"对话长按 F → 目标转身 → 击晕行凭空满金、松手直接犯罪"），该行在本上下文重新按下才重新蓄力；不影响"满框待命"（行一直在 available 中时无列表变化，不走此路径）。
- UI 与响应共享同一份列表，不存在"显示与响应不同步"的结构性可能。

---

## 5. 实施步骤

| Phase | 内容 | 文件 |
|-------|------|------|
| 1 | ModInput 状态机：按玩法行跟踪——Tick/ShortFired/LongFired/IsHeld/HoldProgress/Reset（签名按玩法 ID）；同键共享按下、各按各自阈值；阈值先读全局 `Settings.LongPressDurationMs`（新增字段），玩法级 HoldMs 随 Phase 2 生效 | `Input/ModInput.cs`、`Core/Settings.cs` |
| 2 | 玩法交互配置：Settings.Interactions（Dictionary<玩法ID, InteractionBindingConfig>）+ Gamepad 别名表（Xbox/PS 双字形）+ 解析回落默认 + 同键同按法冲突校验 + RebuildBindings | `Core/Settings.cs`、`Input/ModInput.cs` |
| 3 | 业务迁移：上下文唯一真相源——`PerformPerformanceHeavyLogic` 构建 available 玩法列表（复刻现有显示条件）同时驱动 UI 与响应；`HandleInput` 门控改为 available 非空（IsVisible 只管显示）、遍历 available + `ExecuteInteraction` 玩法分发（switch 玩法 ID → 既有业务方法，无 break）；删除 735 行 Inspect 全局监听迁入统一通道；箱子分支并入 Lockpick 玩法；`TickStealBar` 直连 `ShortFired("StealAttempt"/"StealLeave")`（独立通道）；available 变化 → Reset；StealBarVM 字形/事件玩法 ID 化（闲聊已屏蔽，不涉及） | `Interaction/InteractionMissionView.cs`、`Stealth/StealBarVM.cs` |
| 4 | UI：方块键帽 + 4 段方框进度条（血条模式：SuggestedWidth/Height @float 绑定）+ RequiresHold 接线 + Tick 驱动 progress | `GUI/Prefabs/InteractArea.xml`、`Interaction/InteractionVM.cs` |
| 5 | 双版本编译 + 实机验证（见 §6） | — |

---

## 6. 验证清单

**编译**：v1.2.12 电脑 + 本机（v1.4.7）各 build 一次。InputKey 枚举三版本一致（§7 已核实），无需 V. 包装；XML 改动保持双版本兼容写法（照抄现有文件结构）。

**实机**：
- [ ] 手柄：靠近 NPC 按新 Interact 键（Y）→ 正常对话，**不再触发踢人**（原 X 已让位，踢人回归 X 本体功能）
- [ ] 手柄：按旧 X 键 → 踢人正常，不弹任何 mod UI
- [ ] 键盘：Q 长按接受认输，**战斗中 G 不再丢武器**
- [ ] 长按项（击晕/扒窃/偷活物/搜刮/撬锁/玩家认输/接受认输）：按住 F/Y → 方块四边进度条**顺时针逐边填充**（上→右→下→左）→ **满框变金色（待命）→ 玩家松手才执行**；中途（阈值前）松开不触发；**满后保持按住不执行**（KCD 语义：可等目标转身/目击者走开再松手）
- [ ] 四边填充方向正确且接缝连续：上条左→右、右条上→下、下条右→左、左条下→上（**顺时针连续**：左上→右上→右下→左下→左上，段间无跳变）；满框金色与蓄力琥珀切换正常
- [ ] **操作前区分 Long/Short**：Long 键帽白中偏青绿 + 四条空心框可见；Short 键帽淡白无框；按住 Long 项时琥珀填充在青绿底上清晰可见（填充过程不糊）
- [ ] 短按项（对话/探查）：无进度框，即按即触发；探查点按开信息板（与现状一致）
- [ ] 偷窃条手感：出手/收手 = **按下即触发**（`PressedFired` 按下沿通道，与旧实现一致，无粘滞感）
- [ ] F 同键多玩法：面对对话目标快速点按 F/Y → 对话（`ShortFired("Talk")`）；面对可击晕/搜刮目标按住 → 进度框填充变金 → 松手触发长按行为（`LongFired("Knockout")`），点按不触发
- [ ] 长按中途目标丢失/UI 隐藏 → 进度框消退、不触发
- [ ] **显隐=响应一致**：每个场景（正面/背后/蹲姿/动物/箱子/无目标）UI 显示的玩法与按键响应的玩法逐一对应，无"显示不响应 / 响应不显示"
- [ ] 无目标探查：不看任何人时 UI 无按钮，但 H/R3 点按仍打开"自己"信息板（available=[Inspect] 且 UI 隐藏，响应照常）
- [ ] 偷窃条独立通道：条打开期间按 A/Space、B/Tab 正常出手/收手，available 上下文变化不影响条内输入
- [ ] 长按中上下文切换（按住击晕时目标转身/走开）→ 进度框消退、不误触发
- [ ] 设备切换（键盘↔手柄）→ 字形与环正常刷新
- [ ] **双手柄字形**：同一份配置下 Xbox 显示 Y/A/X/B/LB/RB，PS 显示 △/✕/□/○/L1/R1；PS 玩家配置里写 `"Y"` 或 `"Triangle"` 都解析到同一键、显示 △；`L3`/`R3` 双设备显示一致
- [ ] config.json 顶部注释含 Xbox↔PS 对照表（`Y=△`、`LB=L1`…），PS 玩家按注释能理解每一行键位含义
- [ ] config.json 改键（如把 `Talk` 手柄改成 `X`）→ 热重载后生效，且只影响 Talk；非法值回落默认 + 日志警告
- [ ] **PressMode 数据驱动**：`AcceptSurrender` 改 `"Short"` → 接受认输变点按（无进度框、即按即触发）；改回 `"Long"` 恢复长按；`Knockout` 改 `"HoldMs": 600` 生效；PressMode 非法值回落默认 + 日志警告
- [ ] 偷窃条玩法流程不变（冻结机制照旧、按钮文字随配置字形刷新）；出手/收手 = 按下沿即时触发（`PressedFired`，已实装）
- [ ] SP 下 LB=ShowIndicators、R3 城镇无副作用（若异常 → AcceptSurrender 换 D-pad 上）
- [ ] 长按阈值手感（450ms）实机确认，偏急/偏拖可调

---

## 7. 铁律与设计哲学对照

- **铁律 13（玩家可见文本）**：新增文本仅字形（符号非文案）与既有 `LWN_input_key_*` 本地化键；无新增硬编码中文字面量。
- **铁律 12（零成本最优解）**：长按 = 蓄意确认（犯罪、战斗决策、搜刮），天然多一层"你确定要干"；不产生任何无代价出口。
- **设计哲学**：①反馈明确——四边进度框实时反馈蓄力进度；②自由感——键位 config.json 可定制；④信息塑造目标——进度框的"蓄力仪式感"暗示击晕/扒窃/撬锁是重决定。
- **双配置纪律**：键位只进 config.json（高级玩家侧），不进 MCM；与现有 LLM 三字段方向相反（LLM 走 MCM），无交叉。
- **版本兼容**（`Modules/` 三版本 DLL 逐一核实，本机 v1.4.7 实测）：
  - `InputKey` 枚举：v1.2.12 / v1.3.15 / v1.4.6 **逐值一致**（ControllerLUp=240 … ControllerRThumb=253，手柄键名/值三版全同），config.json 按名字解析**无版本分支**。
  - 进度条**不依赖 `FillBarWidget`**（三版本核实：数值条 `CurrentAmount/MaxAmount` 整数、无方向属性，无法平滑竖填）——改走 `GUI/Prefabs/AgentHudNearby.xml` 血条模式（`SuggestedWidth/@float` + `SuggestedHeight/@float` 绑定），**该 XML 三版本均已实机运行**，零新增控件依赖。
  - 无新 Harmony 补丁（Kick 原生层不可补，改绑解决）。

## 8. 风险与边界（明确不做）

- **不做**：拦截原生踢人/跳跃（native 层，不可补丁）；不做游戏内改键 UI；不改偷窃条节奏玩法与冻结机制；主互动玩法（Talk 等）键盘仍为 F（肌肉记忆优先，世界物互动重叠是既有状态，不在本次范围）。
- **已知残留**：键盘 F 与原版 Action 的世界物互动（门/马/梯）重叠——若玩家对门按 F 恰逢 mod 提示可见，双方都可能触发；现状如此，如需根治另开专项（Harmony 拦 `Mission.Current` 的世界物互动消费点，实施时评估可行性）。

## 9. 轮子登记

实施完成后按工作流约定，向 `plans/rules/wheels.d/input.md` 增补：短按/长按状态机 API、config.json 键位化范式、方块键帽+4 段方框进度条 UI 范式。
