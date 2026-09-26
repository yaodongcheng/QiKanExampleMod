# SuperheroFlightAnimations（UE 超人飞行）—— 动画状态机与过渡完整解析

> **一句话**：这套飞行手感 = **3 个循环状态 + 1 个起飞入姿 + 1 个落地 + 4 个闪避**；
> 其余全部是**加法层**里按「速度分量 / 角速度」采样的 **3~5 点稀疏混合空间**。
> 🔴 **参数不驱动权重**（28 个加法节点的 Alpha 全是常量），只驱动**混合空间采样点** —— 这是全篇最重要的一条。
>
> **谁读这份**：做骑砍2 飞行/坐骑/载具那套自建动画状态机的人（[plans/玩家飞行-实施方案.md](../plans/玩家飞行-实施方案.md)、
> [plans/动画状态机-XML化与编辑器.md](../plans/动画状态机-XML化与编辑器.md)）。
> **对照口径**：别人家怎么组织 → 我们能抄什么 → 哪些抄不了（见 §8）。

---

## 0. 证据与复核方法（先看这段，别拿"听说"当结论）

| 件 | 在哪 | 说明 |
|---|---|---|
| **资产文本导出** | `Debug/offline/_flight_t3d/`（92 个资产）+ `Debug/offline/_flight_t3d_seq/`（130 个 AnimSequence） | 引擎自带的 `ObjectExporterT3D` 导出（等价**反编译**，不是截图/听说）。工具链 = [tools/ue-dissect/](../tools/ue-dissect/README.md) |
| **逐节点报告（上一会话）** | `Debug/offline/_flight_t3d/ABP_解析.md` | 状态机骨架 / 转移条件 / 蒙太奇通知全量。⚠️ 那个目录是 `Debug/offline`（**gitignored**），不进库 |
| **引脚归属解析器（本会话新写）** | `Debug/offline/_t3d_graph.py` | T3D 的坑：`Begin Object Class=X Name="Y"` 只在**前半段**出现，节点体在后半段写作 `Begin Object Name="Y"` ⇒ 普通正则拿不到"这个引脚属于谁"。这份带栈解析补上了它（也是本会话能追出"抬头/低头到底接的哪个变量"的原因） |

**读法（自己复核时的三步）**：
1. 找节点体：`Begin Object Name="AnimGraphNode_BlendSpacePlayer_0"` 之后即为该节点的属性（`Pins` 用 `CustomProperties Pin (...LinkedTo=(...))` 写）。
2. 引脚连线 = `LinkedTo=(<32 位十六进制 PinId>,)` ⇒ 拿这个 id 反查是哪个引脚 ⇒ 那个引脚的宿主节点就是来源。
3. 转移条件 = 转移结果节点（`AnimGraphNode_TransitionResult`）的输入引脚链。

🔴 **局限（别越界下结论）**：T3D 只有**结构**（节点 / 引脚 / 默认值 / 连线）。
**看不到运行时求值** —— 曲线资产、混合空间内部的插值实现、CDO 里的数值（未导出的变量默认值）都看不到。
本文件凡是"未能确定"的地方都标了，没有一处是靠猜补的。

---

## 1. 顶层骨架（`ABP_Player` 的 `Default` 状态机）

进入状态 = `Ground`。`MovementConduit` 是**汇合点**（不是状态），把三条进入路径收敛后统一判去向。

| 状态 | 内容 |
|---|---|
| `Ground` | 走/跑（原版第三人称） |
| `Land` | 落地过渡 |
| `Flying` | 内含 `Flying State Machine`（§2） |
| `InAir` | 内含 `InAir State Machine`（原版跳跃，与飞行无关） |

| # | 从 → 到 | 条件 |
|---|---|---|
| 0 | MovementConduit → `Land` | `(MovementMode == MOVE_Walking) AND (NOT IsAiming)` |
| 1 | `Land` → `Ground` | `GetRelevantAnimTimeRemainingFraction(Land) < 0.1`（**剩 10% 就切走**） |
| 2 | `Ground` → MovementConduit | `(MovementMode != MOVE_Walking) OR IsAiming` |
| 3 | MovementConduit → `Flying` | `IsFlying` |
| 4 | `InAir` → MovementConduit | `(MovementMode != MOVE_Falling)` |
| 5 | MovementConduit → `InAir` | `(MovementMode == MOVE_Falling)`（交叉淡入 0.25s；转移开始通知 `StopFlightTrailVFX`） |
| 6 | `Flying` → MovementConduit | `NOT IsFlying` |

---

## 2. 飞行子机（`Flying State Machine`）—— **过渡分析的核心，只有 3 个状态**

| 状态 | 含义 |
|---|---|
| `Idle / Hover` | 悬停（不冲刺） |
| `Start` | 冲刺起飞过渡（**一次性**，播 `A_Flight_FastMove_Start_x`） |
| `MoveLoop` | 高速巡航循环（趴姿） |

| # | 从 → 到 | 条件 | 附带 |
|---|---|---|---|
| 10 | `Idle/Hover` → **`Start`** | `IsSprint` | — |
| 12 | `Start` → **`MoveLoop`** | `GetRelevantAnimTimeRemainingFraction(Start) < 0.2` | 交叉淡入 **0.5s**；转移开始通知 `StartFlightTrailVFX` |
| 2 | `MoveLoop` → **`Idle/Hover`** | `NOT IsSprint` | 转移开始通知 `StopFlightTrailVFX` |
| 13 | `Start` → **`Idle/Hover`** | `NOT IsSprint` | 转移开始通知 `StopFlightTrailVFX` |

🔴 **三条可直接抄的口径**（与我们在骑砍2 里做的完全同构）：
1. **进冲刺必须先播一次"入姿"**（`Start`），入姿**剩 20%** 时切到循环 —— 这正是我们 XML 里
   `进入飞行 → 悬浮飞行 anim-rem-pct="15"` 与 `fastmoveStart` 那条 `after-finish` 的同一套做法。
2. **退出条件就是"松开加速"**（`NOT IsSprint`）—— 我们在 `冲刺飞行 → 悬浮飞行` 上用的 `keys="Shift" not="true"` 与此一致。
3. 入姿途中松开加速 ⇒ **直接回悬停**（#13），不必先回循环 —— 我们的"条件优先、演完兜底"是同一件事。

---

## 3. 每个状态的动画链：**三层加法叠出来的**（本次最重要的结构）

### 3.1 8 个"缓存姿态"（`SaveCachedPose`）——加法层的原料

| 缓存名 | 内容 |
|---|---|
| `FastFlightBasePose` | 冲刺巡航基础（`A_Flight_FastMove_x`） |
| `HoverFlightIdlePose` / `HoverFlightMovePose` | 悬停待机 / 悬停移动 |
| `HoverFlightUpDownAddPose` | **上下偏移**（UaD 混合空间） |
| `HoverFlightMoveAddPose` | 水平偏移（前后左右） |
| `HoverFlightLeanAddPose` | 倾斜（Lean） |
| `FastFlightMoveAddPose` | 冲刺·水平偏移（用它装"升降"） |
| `FastFlightLeanAddPose` | 冲刺·倾斜（含上下） |

### 3.2 三个状态的层结构

```
Start   = ApplyAdditive(0.999) ─ BASE: ApplyAdditive(0.999) ─ BASE: Slot("Dodge") → FastMove_Start 序列
                                                    └ ADD:  FastFlightLeanAddPose
                              └ ADD:  FastFlightMoveAddPose
MoveLoop= ApplyAdditive(1.0)  ─ BASE: ApplyAdditive(1.0)  ─ BASE: FastFlightBasePose
                                                    └ ADD:  FastFlightLeanAddPose
                              └ ADD:  FastFlightMoveAddPose
Idle/Hover = ApplyAdditive(1.0)  ─ BASE: Slot("HoverStart")
             └ BASE: ApplyAdditive(1.0)  ─ BASE: ApplyAdditive(1.0)  ← 第1层：上下(UaD)
                                          │      └ BASE: BlendListByBool(HasMovementInput, 1.0s)
                                          │         ├ [有输入] HoverFlightMovePose
                                          │         └ [无输入] LookAt(neck_01, Clamp=40, Lerp=2.0, **Alpha=0.85**) → HoverFlightIdlePose
                                          └ ADD:  HoverFlightUpDownAddPose
                                         └ ADD:  HoverFlightMoveAddPose
                                         └ ADD:  HoverFlightLeanAddPose   ← 第3层：倾斜
```

### 3.3 🔴 Alpha 实测（本会话重核，**修正旧报告"全是 1.0"的说法**）

| 节点类型 | Alpha 取值 | 是否被变量驱动 |
|---|---|---|
| `AnimGraphNode_ApplyAdditive`（**28 个**） | `1.0` × 20 · `0.999` × 8 | ❌ **全部是常量** |
| `AnimGraphNode_LookAt`（**4 个**） | `0.85` | ❌ 常量 |

⇒ **没有任何一个 Alpha 被变量驱动**。方向 / 倾斜的**强弱不是靠淡入淡出**，
而是靠**加法层内部那个混合空间采样的位置**（见 §4）。

**外部还有两个槽位**（`Slot`）：`Dodge`（挂在 `Start` 与 `FastFlightBasePose` 上，供闪避蒙太奇插播）、
`HoverStart`（悬停层最外），主干上还有一个 `DefaultSlot`（落地蒙太奇走它）。

---

## 4. 混合空间全表（**这是"参数化"真正发生的地方**）

全部是 **2D / 1D、两轴都 [−1, 1]、GridNum=2、插值时间 0.3s** 的**稀疏采样**：
**每根轴只有 −1 / 0 / +1 三个采样点**（2D 空间是"中心 + 四方向"的**十字 5 点**，不是 9 点网格）。

| 资产（A 变体） | 维 | 轴名 | 采样点 → 动画 | ABP 里喂它的量 |
|---|---|---|---|---|
| `BS_Flight_HoverMove_A` | 2D | `Speed_Y` / `Speed_X` | (0,0)→`HoverMove_A_Add`；(0,1)→`_F_Add`（前）；(0,−1)→`_B_Add`（后）；(−1,0)→`_L_Add`；(+1,0)→`_R_Add` | `HoverSpeed_Y` / `HoverSpeed_X` |
| `BS_Flight_HoverMove_A_Lean` | **1D** | — | 0→`HoverMove_A_Add`；−1→`HoverLean_A_L_Add`；+1→`HoverLean_A_R_Add` | `Lean.X`（Y 未连） |
| `BS_Flight_HoverMove_A_UaD` | **1D** | `Speed_Z` | 0→`HoverMove_A_Add`；−1→`HoverMove_A_D_Add`（俯冲）；+1→`HoverMove_A_U_Add`（爬升） | **`HoverSpeed_Z`** |
| `BS_Flight_FastMove_A` | **1D** | `Speed_Z` | 0→`A_FM_A_Pose`；−1→`A_FM_A_Pose_D`；+1→`A_FM_A_Pose_U` | **`HighSpeedVerticalSpeed`** |
| `BS_Flight_FastMove_Lean_A` | 2D | `LeanX` / `LeanY` | (0,0)→`A_FM_A_Pose`；(±1,0)→`A_FM_A_Lean_L/R`；(0,±1)→`A_FM_A_Lean_D/U` | `Lean.X` / `Lean.Y` |
| `BS_ThirdPerson_IdleRun_2D` | 2D | `CurrentSpeed` | 原版走跑（非飞行，Ground 状态用） | `CurrentSpeed` |

> ⚠️ 悬停那组的轴名与实际接法是**交叉**的：轴 0 叫 `Speed_Y`、轴 1 叫 `Speed_X`，
> 而蓝图把 `HoverSpeed_Y` 接给 X、`HoverSpeed_X` 接给 Y —— 抄的时候别按名字对，按**接线**对。
> ⚠️ 5 个变体（A~E）各有一套同名资产，结构相同。

---

## 5. 🔴 参数怎么算（"触发逻辑"在这里，**全是速度/角速度，不是镜头角度、不是按键**）

### 5.1 抬头 / 低头（UaD 轴）—— 用户最常问的一条

**结论：两族的"抬头/低头"都看"**竖直速度分量**"，但算法不同**：

| 族 | 变量 | 计算式（引脚链实读） |
|---|---|---|
| **悬停家** | `HoverSpeed_Z` | `MapRangeClamped( BreakVector( RotateVector( GetVelocity(), ActorRotation ) ).Z , InRangeA = −CurrentMaxFlySpeed , InRangeB = +CurrentMaxFlySpeed )` ⇒ **角色本地速度的 Z ÷ 最大飞行速度**，钳制到 −1..+1 |
| **冲刺家** | `HighSpeedVerticalSpeed` | `FInterpTo( HighSpeedVerticalSpeed , BreakVector( Normal( GetVelocity() ) ).Z , dt , **5.0** )` ⇒ **速度单位向量的 Z 分量**（= sin(俯仰角)），**平滑 5.0** |

读法差异值得注意：
- 悬停家用**本地系**（`RotateVector`：世界速度 → 角色局部）⇒ 身体趴下/侧倾时"上"跟着身体走；
- 冲刺家用**世界系方向的 Z**（`Normal(Velocity).Z`）⇒ 就是"往天上飞还是往地下扎"，与身体朝向无关；
- 悬停家按**最大速度归一化**（半速爬升只给 −0.5 档），冲刺家是**纯方向**（半速爬升也给 +1 档），后者再被 `FInterpTo` 抹平。

⇒ 骑砍2 侧对应：我们的 `look-up` / `look-down` 现在是**相机俯仰档**（`PitchBand`，迟滞 0.42/0.30）。
**要更像原版**，这两条应当改成"**本地/世界速度的竖直分量**"（我们手上就有 `_velocity`）。

### 5.2 倾斜（Lean）—— **角速度**，不是按键

```
LastVelocityRotation      = MakeRotator(Roll=ActorRotation.Roll, Pitch=速度方向的Pitch, Yaw=ActorRotation.Yaw)
YawVelocityDifference     = NormalizedDeltaRotator(LastVelocityRotation, PreviousVelocityRotation).Yaw   / dt
PitchVelocityDifference   = NormalizedDeltaRotator(LastVelocityRotation, PreviousVelocityRotation).Pitch / dt
PreviousVelocityRotation  = LastVelocityRotation         ← 每帧存上一帧
Lean.X = FInterpTo(Lean.X, MapRangeClamped(YawVelocityDifference,   ±180 → −1..+1), dt, **5**)
Lean.Y = FInterpTo(Lean.Y, MapRangeClamped(PitchVelocityDifference, ±180 → −1..1), dt, **15**)
```
⇒ **"转得越快，倾得越多"**（自动驾驶式压弯）；我们目前是"按 A/D"（离散输入）——这是观感差距的主因之一。

### 5.3 其余参数

| 变量 | 计算式 |
|---|---|
| `CurrentSpeed` | `VSize(Velocity)` |
| `CurrentMaxFlySpeed` | `CharacterMovement.MaxFlySpeed`（悬停/冲刺两档，不同值） |
| `HoverSpeed_X / Y / Z` | 见 5.1（同一个 `MapRangeClamped(局部速度分量, ±MaxFlySpeed)`） |
| `HoverMoveRate` | `FInterpTo(prev, MapRangeUnclamped(CurrentSpeed, 0→MaxFlySpeed, **1.0→1.5**), dt, 1.5)` ⇒ 接给悬停移动序列的 **PlayRate**（飞得越快，循环放得越快） |
| `LookAtLocation` | 常态 = 角色位置 + 默认高度；飞行 = 再 + 前向 × 5000，然后 `VInterpTo(dt, 10)`；落地/瞄准改取 socket |
| `HasMovementInput` | `GetLastInputVector() != (0,0,0)` |
| `IsFlying` / `IsSprint` / `IsAiming` / `FlightType` | 从组件 `GetXxxFunc()` 每帧刷回 |

---

## 6. 蒙太奇 / 槽位 / 通知（细节见 `ABP_解析.md` §E–§G）

| 件 | 位置 | 要点 |
|---|---|---|
| **闪避** ×20（A~E × 四方向） | `Slot("Dodge")` | 长 1.833s；BlendIn 0.1 / BlendOut 0.3；通知：**方向枚举**（0→0.995s）、`IsDodgeing`（0→0.665s）、脚下扬尘（整段）、`NS_Flight_Dodge` 粒子 @0.0998s 挂 pelvis |
| **落地** ×5 | `DefaultSlot` | 长 2.0s；BlendIn 0.1 / **无 BlendOut**；通知：`IsSuperherolanding`（0→1.331s）、震屏 @0.133s、**FOV 渐变 0.133→1.0s**、落地 VFX @0.134s、**尾段 1.334→2.0s 若已有移动输入则提前打断** |
| **状态机通知事件**（不是序列通知） | ABP 内部事件 | `HoverStrat`（进悬停）/ `StartFlightTrailVFX`（`Start→MoveLoop`）/ `StopFlightTrailVFX`（三处） |
| **序列上的通知** | 130 个序列里**只有 15 个**带通知 | 循环类（Idle/HoverMove/FastMove）**没有**通知；通知都挂在"起手/过渡"上（`FastMove_Start` 1.0s：扬尘 + 震屏 + 拖尾粒子 + FOV） |
| ⚠️ **空壳通知别抄** | `AnimNotify_HoverStrat` / `StartFlightTrailVFX` / `StopFlightTrailVFX` | 图形体是**空的**（不产生任何效果），效果在别处 |
| **材质 → 特效** | `GetFlightUnderDustVFX_Func` / `GetSuperheroLnadingVFX_Func` | `Select(PhysicalSurface)` → 索引 → `GetArrayItem(数组, 索引)`：`Default/1 → 0`、`2 → 1`、`3 → 2`、`4 → 3`、`5 → 4`（资产命名对应 Concrete / Grass / Ground / Sand / Water） |

---

## 7. 风格变体 A~E（`EFlightType`）

- **整套复制**：每个"状态 / 缓存姿态 / 混合空间"都有一份 A~E 共 5 套 —— 变体 = **换素材**，不是换参数。
- 驱动 = `AnimGraphNode_BlendListByEnum` ×28，变量 `FlightType`；每个枚举节点 **6 个槽**（槽 0 = 兜底，接到与 A 相同的资产；槽 1..5 = A~E）。
- 混合时间接变量 `FlightTypeBlendTime`（**全工程只读不写** ⇒ 实际 = 类默认值，T3D 不含 CDO ⇒ **未能确定**；节点自带默认值 0.1s（Start/FastMove 组）/ 0.5s（Hover/Idle 组））。
- 切换：`ChangeFlightTypeFunc` = `FlightType − 1`（到 0 之前回卷到 4）⇒ **A→E→D→C→B→A 循环**，绑一个键。

---

## 8. 映射到骑砍2（我们手上有什么 / 差在哪）

| UE | 我们（`ModuleData/statemachine_flight.xml`） | 状态 |
|---|---|---|
| `Flying` 3 状态 + 入姿 + `NOT IsSprint` 退出 | `悬浮飞行` / `冲刺飞行` 两族 + `fastmoveStart` 入姿 + `keys="Shift" not="true"` 退出 | ✅ 同构 |
| 入姿剩 20% 切循环 | `进入飞行 → 悬浮飞行 anim-rem-pct="15"`、`fastmoveStart` 的 `after-finish` | ✅ 同构 |
| `BS_..._Lean`（1D 三点：L/0/R） | `hovermoveLeanL/R` 状态 + 交叉淡化 | ✅ 档位同数（原版也就 3 点） |
| `BS_..._UaD`、`BS_Flight_FastMove_x`（升降 1D 三点） | `hovermovePitchU/D`、`fastmovePitchU/D` | 🔶 素材已烘、边已接；**冲刺那两条素材是空的**（`FM_A_Pose_U/D` 的动量 85~90° 全在根骨、被合成口径摘掉）⇒ 要换 `FM_A_Lean_U/D` 重烘 |
| `BS_Flight_HoverMove_x`（前后左右 5 点） | 用不上：我们定速两档 + 机身朝移动方向 | ⛔ 不做（《实施方案》§3.3 B 已裁定） |
| `Lean` = **角速度** | 我们 = 按 A/D（离散输入） | ⚠️ 观感差距主因；要改就改 C# 的谓词来源（各一行） |
| 升降 = **速度竖直分量** | 我们 = 相机俯仰档（`PitchBand`） | ⚠️ 同上 |
| **多轴并行叠加**（倾斜 + 升降 + 水平同时生效） | 一次只播一条 clip（通道 0） | ❌ 抄不了；只能**预烘组合档**（3×3=9/族）或用通道 1 做"上身层" |
| **半档连续插值**（输入停在 0.5 给半姿势） | 最近档 + 交叉淡化 | 🔶 差距有限 —— **原版采样点也只有 3 档** |
| 变体 A~E 随机 | 只导了 A 套 | ⬜ 要"每次不一样"再按《实施方案》§3.4 阶梯加导 |

**相关的骑砍2 侧事实**（别在别处重复维护）：
- 《玩家飞行-实施方案.md》§3.12′（跳转线定稿）、§3.11（趴姿腿硬 = 引擎编译丢低偏移骨轨道）、§3.8.1（增量合成口径 `--root-scale`）。
- 我们自己的状态机：`Animation/AgentAnimStateMachine.cs`（求值）+ `Animation/AnimMachineLoader.cs`（装载校验）+ `ModuleData/statemachine_flight.xml`（定义）。

---

## 9. 复核清单（下次想再确认任何一条时照这个做）

```bash
# ① 资产是否已导出（缺了就重跑 tools/ue-dissect）
ls Debug/offline/_flight_t3d/ | grep BS_           # 混合空间 25 个
ls Debug/offline/_flight_t3d_seq/ | wc -l          # 序列 130 个

# ② 用解析器读引脚归属（例：谁在喂 UaD 那根轴）
cd Debug/offline/_flight_t3d && python -c "
import sys; sys.path.insert(0,'.')
import _t3d_graph as G
objs, by_pin = G.parse('abp_player.utf8.t3d')
for o in objs:
    if o.cls=='K2Node_VariableSet' and o.props.get('varname')=='HoverSpeed_Z':
        print([ (p['name'], p['links']) for p in o.pins ]); break
"

# ③ 混合空间的轴与采样点（原始文本，最可信）
grep -o 'SampleData([0-9])=(Animation=[^,]*,SampleValue=(X=[-0-9.]*' \
  Debug/offline/_flight_t3d/*HoverMove_A_UaD*.t3d
grep -o 'BlendParameters(0)=(DisplayName="[^"]*",Min=[-0-9.]*,Max=[-0-9.]*' \
  Debug/offline/_flight_t3d/*HoverMove_A_UaD*.t3d
```
