# 骑砍2 Agent 运动与位置机制

> 结论来自 1.2.12 反编译 + 2026-09-18/19/20 实机实测（用户亲自跑的控制台实验）。**要做飞天/位移类功能，先读本文档，别重踩。**
>
> 🔴 **2026-09-20 重大更新**：**「让角色本人离地」已经找到解了** —— 见 **§6.6**。
> §6 那 9 条是失败记录（照旧保留，防止重蹈）；**第 10 条路（实心场景道具 + 逐帧瞬移）成立**。

---

## 一、结论速查（六条硬结论）

1. **位置写入：X/Y 可以任意设，Z 设不进去——设多大都自动贴地。**
2. **这跟玩家控制权无关。不需要切 controller。**（切了反而会被引擎搬走，见 §5）
3. **引擎没有 agent 级重力开关**，也没有"飞行/悬停"态。
4. **"运动模式"不是可扩展的注册表**：人形只有 4 个模式，名字写死在 C++ 里；XML 只能给这 4 个模式配动画。
5. **引擎唯一"带着 agent 移动"的通道 = 动态导航件**（带 navmesh 面的可移动物体，攻城塔就是这么带兵上城的）。**没有现成的电梯物件**。
6. 🔴 **"让角色本人离地"已有解**（2026-09-20 实机验证）—— **实心场景道具 + 逐帧瞬移 = 把人抬起来**，见 **§6.6**。
   2026-09-18/19 的 9 条路（§6）全部失败并保留；**第 10 条路成立**，且已推到「玩家站在木板上被抬到 157 米」。
   仍成立的旧结论：**能飞的是相机**（XiuXian 那条，零件我们已有）；**§6.5「改跳跃参数 + 空中反复起跳」仍未做**。

---

## 二、位置写入的真实语义（本次会话最重要的结论）

### 2.1 实测事实

| 操作 | 结果 |
|---|---|
| `Agent.TeleportToPosition` 改 **X** | ✅ 生效 |
| 改 **Y** | ✅ 生效 |
| 改 **Z**（任何值，包括 +3 / +20 / +34 米） | ❌ **无效，自动贴回地面** |
| 不切控制权直接写 | ✅ X/Y 生效（**不需要切**） |

**实测方式**：`custom.set_controller player|ai|none` × `custom.move <dx> <dy> <dz>` 3×3 矩阵，逐格用 `custom.print_pos_dir` 读回。结论：**控制权这一维完全无关**。

### 2.2 为什么 Z 写不进去（引擎设计）

- **`Agent.Position` 是直接解引用 native 内存的实时读**（`AgentHelper.GetAgentPosition`，`*(Vec3*)ptr`）——不是缓存。所以"写完同帧读回还是旧值"意味着 **native 真的没改**。
- **`SetPosition` 是同步直调 native**（`MBAPI.IMBAgent.SetPosition` → `call_SetPositionDelegate`），没有排队、没有门控。引擎收到了、执行了、就是不落 Z。
- **vanilla 自己唯一用到 `TeleportToPosition` 的地方，永远传 `GetGroundVec3()`（地面投影）**——引擎自己的用法就承认了"这是在地面上挪人"。
- **`AgentHelper.SetAgentPosition`（直接写内存那条）挂着 `Debug.FailedAssert("Do not use this!")`**——位置归 native 独占。
- **`WorldPosition` 结构只有 `GetNavMeshVec3` / `GetGroundVec3` / `ZValidityState`**；连 `Agent.SetTargetPosition` 都只吃 `Vec2`（没有 Z 参数）。
- 结论：**引擎对 agent 的 Z 只有一个语义——"你脚下那块地的高度"。** Z 是解算出来的，不是存起来的。

### 2.3 试过但无效的路径（别再试）

| 试过什么 | 结果 |
|---|---|
| 从 `MissionBehavior.OnMissionTick` 每帧写 | ❌ 被吞（`Mission.OnTick` 里 agent tick 在它之后） |
| 从 `MissionScreen.OnFrameTick`（晚钩子）每帧写 | ❌ 同样被吞（两侧 delta 只差计时错位 0.015） |
| 先切 `Controller = AI`（StoryEngine 那套）再写 | ❌ 写入仍无效，且 agent 被搬到兜底坐标（见 §5） |
| 先切 `Controller = None` 再写 | ❌ 同上 |

---

## 三、运动模式体系（能不能自定义"走路/奔跑/蹲走"之外的模式）

**答案：不能新增模式，只能给现有模式换外观。**

### 3.1 四层结构

| 层 | 文件 / 位置 | 内容 | C# 能不能碰 |
|---|---|---|---|
| ① 模式枚举 | **native 硬编码** | `walking` / `running` / `crouch_walking` / `crouch_running`（native 二进制里就是这四个字面量） | 只能读（`Agent.WalkMode` / `Agent.CrouchMode`） |
| ② 模式 → 动画集 | `Native/ModuleData/full_movement_sets.xml` | 按 `movement_mode="walking"` + `left_stance` 绑到具体动画集 id | ❌ 零 API（`MovementSet` 在三个 DLL 里 0 命中） |
| ③ 动画集 → 18 个方向动作 | `Native/ModuleData/movement_sets.xml`（101 条） | `forward` `backward` `left` `right` `left_to_right` `rotate` + 8 个 `_adder` | ❌ |
| ④ 动作名 → 骨架动画 | `action_sets.xml` + `action_types.xml` | `<action type="动作名" animation="clip名">` | ✅ `MBActionSet.GetActionSet(名字)` / `Agent.SetActionSet` |
| ⑤ 生物参数 | `monsters.xml` | `walking_speed_limit` / `crouch_walking_speed_limit` / `jump_*` / flags | ✅ `Monster` 类型 |
| ⑥ 运行时开关 | — | `Agent.EventControlFlag.Walk/Run/Crouch/Stand` | ✅ |

**模式 = (走/跑) × (站/蹲) = 4 档**，两把开关都能用 `EventControlFlags` 直接拨（原版 `MissionMainAgentController` 里就两行：WalkMode?Run:Walk、CrouchMode?Stand:Crouch，位值 2048/4096/8192/16384）。

### 3.2 钥匙在第 ④ 层

`movement_sets.xml` 里写的是动作**名**（`act_walk_forward_unarmed`），这个名字**在每个 action set 里各解析一遍**。所以**换 action set = 换掉整套走/跑/蹲的外观**（可把 locomotion 动作名指向飞行/悬停 clip），而模式仍是那 4 个。

范本（我们已有可跑代码）：`Core/AgentControlHelper.cs` 的 `ForcePlayAction`（`MBActionSet.GetActionSet` → `Monster.FillAnimationSystemData` → `agent.SetActionSet`）。
⚠️ 换 action set 会与异步 AI tick 抢，可能 AccessViolation——照抄那里的"已经是 warrior 就跳过"守卫。

### 3.3 人形 vs 非人形：两套并行表

| | 人形 | 四足 / 动物 / 坐骑 |
|---|---|---|
| 移动动画表 | `movement_sets.xml` | `monster_usage_sets.xml` 的 `monster_usage_movements` |
| 模式怎么分 | **4 个固定模式** × 武器 × 左右手 | **`pace`（步态）** × 9 方向 |
| 模式条数 | 固定 4 | **数据说了算**（马 `num_paces=6`、牛/鹅/鸡/羊 = 5） |
| C# 能否选择 | ✅ `EventControlFlags` 直接拨 | ❌ **引擎按速度自己选**，native 只有 `GetMonsterUsageIndex` 读，没有 setter |

→ 「换个非人形定义就能自定义运动模式」**是反的**：非人形的 pace 只能靠调速间接影响，人形的 4 档反而是能直接拨的。

---

## 四、没有的东西（省得再查）

| 找过什么 | 结果 |
|---|---|
| `SetGravity` / `GravityMultiplier` / `ZeroGravity` / `NoGravity` / `GravityScale` / `UseGravity`（全客户端 DLL） | **0 命中** |
| `GameEntityExtensions.DisableGravity` | 有，但是给**场景实体（道具）**用的，管不到 agent 运动 |
| `AgentMovementMode` 之类的模式枚举 | 没有（`MovementMode` 只命中编辑器幽灵相机） |
| `AgentFlag.CanFly` / 任何飞行相关 monster 字段 / 任何 `fly` 动画 | **全部没有**（4874 个 `act_` 名字里 0 个 fly） |
| `Elevator` / `Lift` / `MoveablePlatform` | **全 0 命中——骑砍2 没有电梯这种东西** |
| `DrivenProperty`（84 项）里的重力项 | 没有 |
| `Mission` 里的 gravity | 没有 |

---

## 五、控制权：不需要切，切了有副作用

**规则：做位移功能时不要碰玩家 agent 的 `Controller`。**

- **切了没用**：X/Y 在 `Controller == Player` 下就能写，Z 在哪种控制权下都写不进去。
- **切了有害**：把主 agent 的 `Controller` 从 `Player` 改掉后，实机观察到 agent 被搬到固定兜底坐标 `(-100, 10385, 15.06)`（`AI` 与 `None` 两次落点一致），镜头随之丢失。
  ⚠️ 该观察**变量未完全分离**（当时同时存在我们的位置写入），但结论不变：**不动控制权**。
- 参考：`V.SetPlayerControlFrozen` / `V.SetAgentAI` / `V.SetAgentPlayer` / **`V.SetAgentControllerNone`**（版本兼容封装，1.3+ 是顶级 `AgentControllerType`，业务层禁止裸写 `Agent.ControllerType`）。
  另有 `Core/StoryEngine.cs` 的成对用法（脚本演出）可作参考。

---

## 六、🔴 离地尝试全记录（2026-09-18/19 两轮跑完，**9 条全死**）

> 全部为**实机实测**（用户亲自跑控制台，日志在 `Debug/StoryEngine_RuntimeLog.txt`）。
> **想再做飞天/悬浮类功能，先把这张表读完——不要重试其中任何一条。**

| # | 路子 | 做法 | 实测证据 | 结论 |
|---|---|---|---|---|
| 1 | **直写 Z** | `Agent.TeleportToPosition(pos.x, pos.y, z+5)` | 多轮日志 `readback` 与写入前**完全一致**；3×3 控制权矩阵全试过 | ❌ **X/Y 生效、Z 无效** |
| 2 | **切控制权** | 先 `Controller=AI`（StoryEngine 同款）/ `None` 再写 | 写入仍无效；且 agent 被搬到固定兜底坐标 `(-100, 10385, 15.06)`，镜头丢失 | ❌ **无效且有害** |
| 3 | **骑马设马的 Z** | 骑马后 `custom.move 0 0 5` | `[MOUNTED riderZ=72.76 mount=(366.18,540.11,72.76) mountOnLand=True]` ——骑手与马**都没动** | ❌ **马与人是同一个 Z 规则** |
| 4 | **放大坐骑当电梯** | 把马 item 的 `body_length` 拉大（骑手被顶到更高坐点） | 机制成立（见下方根因），但见两条硬约束 | ⚠️ **上限只有几米，且副作用大** |
| 5 | **抬骑手坐点骨** | `Skeleton.SetBoneLocalFrame(Monster.RiderSitBoneIndex, frame + Z)` | `[Lift] amount=5.00m boneIndex=2 boneName='horsespine2' boneCount=32 riderZ=35.41 mountZ=35.41`（设 100 米亦然）——**骨找对、写进去了，骑手纹丝不动** | ❌ **骑手位置不从该骨实时姿势算** |
| 6 | **抬 agent 外观帧** | `MBAgentVisuals.SetFrame(全局帧 + Z)` | `[VLift] amount=5.00m agentZ=35.47 visualZ=40.47` / `amount=100.00m agentZ=35.17 visualZ=135.17`——**写入精确生效**（差值分毫不差），**但画面完全不动** | ❌ **外观帧可写，渲染不读它**（遗留疑点见下） |
| 7 | **动态导航件升降板** | 运行时导入 navmesh 面挂到实体上、抬高实体（§7） | 导入**成功**（场景面数 `15656→15657`，`scan` 在组区间内找到 1 个面）；板位置正确、留 0.3m 台阶、`isConnected` 两种都试——**玩家导航面 id 始终是场景自己的 `2`，`onPlate` 恒 False** | ❌ **agent 不会转移到新挂的面上** |
| 8 | **物件载人（椅子）** | 搬椅子到面前 → `UseGameObject` 强制坐上 → 抬「正在使用的物件」 | `[Chair] chairZ=5.00 baseZ=0.00 playerZ=35.17 using=ChairUsePoint`；抬到 10 米，**`playerZ` 全程 35.17 不动** | ❌ **物件载人不成立** |
| 9 | **空中态（跳跃/坠落）** | `custom.airhold` 布防，**只在 `onLand=False` 时**每帧写 Z | `[Air] AIRBORNE onLand=False preZ=35.01 want=35.03 after=34.99`——**写完当场读回就是旧值**（与贴地时一模一样） | ❌ **空中态的 Z 也写不进去** |

### 6.1 根因（一句话）

**`Agent.TeleportToPosition` → native `SetPosition` 根本不写 Z 这个分量**——不是"被地面解算拉回去"，是**写进去当场就是旧值**（贴地 #1、空中 #9 两次实测，读完即刻读回都是原值）。
配套事实：

- 任何**绕过逻辑位置**的改法（外观帧 #6、骨骼 #5）→ 值改得动，**画面上不出现**。
- 马的 Z 与人的 Z 同一套规则（#3），换载体没用。
- 导航件板（#7）理论上能改"脚下是什么面"，但**引擎不会把已在场景面上的 agent 转移到新面上**。
- 物件载人（#8）不成立——引擎不按"你在用什么物件"实时算你的位置。

### 6.2 路子 #4 的两条硬约束（放大坐骑）

机制（反编译实证，`Mission.BuildAgent` mission.cs:3787-3797）：

```csharp
if (!agent.SpawnEquipment[EquipmentIndex.ArmorItemEndSlot].IsEmpty)
    if (e.Item.HorseComponent.BodyLength != 0)
        agent.SetInitialAgentScale(0.01f * e.Item.HorseComponent.BodyLength);
```

1. 🔴 **`ArmorItemEndSlot = 10 = Horse`（同一个槽位值）** —— 骑手身上就带着马这件装备，
   所以这段**对骑手也跑一遍**：马放大 3 倍 → **骑手也放大 3 倍**（"巨马 + 巨人"）。
2. 🔴 **抬升量 ∝ 坐骑尺寸** —— 骑手坐在 `rider_sit_bone` 上，马放大 N 倍则坐点离地也是 N 倍。
   正常马背高约 1.3 米 → **放大 3 倍只有约 4 米**；想飞 30 米**需要 20 倍大的马（约 50 米长）**，
   而 `body_capsule` 也一起缩放 → 巨型隐形碰撞体足以搅乱整张图。
3. **mod 侧改不回去**：`Agent.SetInitialAgentScale` 是 **internal**，
   底层 native 绑定 `MBAPI.IMBAgent.SetAgentScale` **从 mod 程序集不可达**
   （编译实锤：`MBAPI` 不含 `IMBAgent` 成员；`Agent` 也不公开 native 指针）。
   实测指令 `custom.mount_scale` 因此改走**反射**。

### 6.3 🔴 遗留疑点（#6 没验完的一刀，5 分钟可验）

**没区分开两件事**：

- (a) 外观帧**被引擎每帧重置**（我们写到渲染前又被改回去）→ **还有救**：换更晚的写入点。
- (b) 渲染**根本不读**外观帧 → 死路。

**当时为什么没分开**：日志是"写完立刻读"，两种解释都成立。
**验证方法**：在 tick 里**先读后写**，把读到的值记下来——
- 下一帧读回的是"逻辑位置" → 是 (a)，写入被重置；
- 读回的是我们写的偏移 → 是 (b)，渲染不读它。

**若判定为 (a)，下一步**：把写入挂到 `MBAgentVisuals.Tick`（引擎自己的外观更新）的 **Harmony postfix**，
那是能拿到的最晚时机（比 `MissionScreen.OnFrameTick` 更贴近渲染）。

### 6.4 剩下能做的：相机（唯一"能飞"的东西）

- **XiuXian**（唯一找到的"御剑飞行" mod）的形状 = **相机补丁 + 武器网格 + 隐藏身体 + 控制权**，
  与"身体飞不了"的结论完全吻合——**它是把引擎约束当成了设计**。
  反编译证据：`[HarmonyPatch(typeof(MissionScreen), "UpdateCamera")]`（Prefix Priority 800 + Postfix）、
  反射成员表含 `get_CombatCamera`/`set_CameraBearing`/`set_Frame`/`set_VisibilityMask`/`set_CullingMode`/`set_MovementInputVector`。
- **零件我们已有**：`Camera.CreateCamera()` + `missionScreen.CustomCamera = cam` 这套
  （`custom.cam_face` / `custom.cam_reset` 已跑通；`CameraDebuggerView` / `SpringArmCameraView` 是两套现成相机模式；
  `StoryEngine` 也在用）。
- **代价**：身体留在地上。隐藏身体 → 第一人称飞天；不隐藏 → 看得到自己在地上。

---

### 6.5 🔴 跳跃的上升速度由什么决定（唯一还有理论空间的一条）

**答案：`monster.xml` 的 `jump_acceleration` + `jump_speed_limit`，native 消费，C# 零接口。**

| monster | `jump_acceleration` | `jump_speed_limit` |
|---|---|---|
| **human** | 4.0 | **0** ← 人形 |
| horse | 6.5 | 3.5 |
| goose | 3.0 | 6.5 |

查过三处都没有运行时改的口子：`AnimationSystemData`（随 action set 下发的那包参数，只有 `WalkingSpeedLimit`/`CrouchWalkingSpeedLimit`/`NumPaces`/`MonsterUsageSetIndex`）、`DrivenProperty`（84 项，无 jump）、native agent API 表（203 个方法，只有 `IsOnLand` 一个读口）。
这两个字段只出现在 **native 的 monster 结构体**里——**加载时读一次，之后只读不写**。

**⚠️ 玩家按空格是有效的**（`MissionMainAgentController` 读 GameKey 14 → `EventControlFlags |= Jump(8)`；徒步就是 Jump，骑马静止是 Rear(4)、骑马移动是 Jump）。日志实测两次 `onLand=False`——离地发生了，只是 `jump_speed_limit=0` 让它几乎立刻落地。

**唯一还站着的思路（本次未做，用户决定收手）**：

1. **给 `human` 做 monster 覆盖**，把 `jump_speed_limit` 调大（如 8）→ 跳跃变高。
2. 若**空中能重新触发 `EventControlFlag.Jump`**（每次补一点上升速度）→ **反复起跳 = 维持高度 = 真·飞天**。
   这条路性质与前面九条完全不同：**身体真的在空中**（引擎自己的物理）、**动画正常**（跳跃/坠落动画本就是给空中准备的）、**完全不用写 Z**。

代价：`human` 是全局 monster，所有 NPC 跟着变（但 AI 不发 Jump，实际只有玩家会跳高）。
接线参考 wheels 卷十三「自建 race/monster 完整清单」。

**⚠️ 副作用提醒**：`custom.airhold` 每帧的 `TeleportToPosition` 会**把跳跃的上升速度清掉**（日志现象：arming 后"跳不起来了"）。用 `custom.airhold off` 撤防。

---

## 6.6 🔴 成立的那条路：实心场景道具 + 逐帧瞬移（2026-09-20 实机验证）

**一句话：生成一块看得见、有物理体的场景道具（`wooden_platform_a`），玩家站上去，每帧 `SetFrame()` 把它往上挪 —— 玩家会被一起抬起来。**

实测（玩家站在板上，板上以 1 m/s 匀速上升）：

```
14:56:31.699  propZ=9.31   playerZ=9.67   dz=+0.36
14:56:34.704  propZ=12.31  playerZ=12.68  dz=+0.36
14:56:38.703  propZ=16.31  playerZ=16.68  dz=+0.36
```

`dz = playerZ − 板原点`，**全程 +0.36 分毫不差**（板顶面在局部 `z≈0.37`）；同一手法一路抬到 **157 米**仍精确跟随。

> 🔴 **这条推翻了 §6 表 #8「物件载人」的结论**。那次失败的是"抬玩家**坐着**的椅子"（脚本化使用动画），
> 与"站在**实心板**上"完全是两回事 —— 后者引擎认。

### 照做清单

| # | 做法 | 关键点 |
|---|---|---|
| 1 | `GameEntity.Instantiate(scene, "wooden_platform_a", frame)` | 自带网格 + 静态物理体（`createPhysics: true` 是默认值） |
| 2 | 板生成在玩家**正前方 3 米**，自己走过去站上 | 别瞬移到脚下 —— 面/碰撞的记账在**移动**时才更新 |
| 3 | 每帧 `f.origin += v·dt; entity.SetFrame(ref f)` | 纯瞬移，**不要碰物理积分**（见下） |
| 4 | 判据：`playerZ` 跟着 `propZ` 涨、`dz` 恒定 | 打日志同时记 `Agent.Position`（逻辑）和 `Agent.VisualPosition`（渲染） |

**`wooden_platform_a` 实测尺寸**（tpaccli dump 量的包围盒）：`5.29 × 5.16 × 0.40 m`，**原点在中心底面**，顶面局部 `z≈0.37`。
备选 `wooden_platform_c` = `2.40 × 5.16 × 0.40`。

### 🔴 五条硬结论（全部实测）

| # | 结论 | 证据 |
|---|---|---|
| 1 | ✅ **能载人**（垂直） | `dz` 恒 +0.36，8 m → 157 m |
| 2 | ✅ **动态体不影响载人** | `SetMobility(dynamic)` 之后照样托人（一度误判为动态体的锅，**是错的**） |
| 3 | ❌ **物理积分移动不托人** | 冲量让物理体自己匀速运动 → 板从人身体**穿过去**，`playerZ` 纹丝不动 |
| 4 | ✅ **水平也能载人**（10 m/s 跟了 8 秒） | 见下方「更正」 |
| 5 | ⚠️ **"卡腰"= 起步约 1 秒的渲染过渡** | 见下。**但只要载具是隐藏的（出货版就是），这条不成问题** —— 玩家看到的是法阵特效，不是木板 |

> 🔴 **2026-09-20 更正（第 4 条）**：本条一度写成「水平高速甩人」，**是错的** —— 那次是**用户自己跳下去**的。
> 回看数据：板以 `v=(10,0,0)` 飞了 8 秒（= 80 米），而 `rel`（玩家 − 板的水平偏移）**全程恒定 ≈ (+0.1,+0.3)**。
> 若人没跟着走，`rel` 早该涨到 -80 ⇒ **人是跟着板一起飞的**。日志里那串 `playerZ` 掉到 0、`rel` 涨到 -700，
> 是跳下去之后的自由落体 + 板继续飞走拉开的距离。
> ⚠️ **但这仍是推断，不是直证**：日志**从来没打过板的 x/y**（`propZ` 只是 z）。
> 补一行 x/y 日志即可钉死 —— 在那之前，"水平载人"标 ✅ 但带保留。

### 🔴 渲染 vs 逻辑：两套数（"板卡在腰间"的真相）

日志同时打两个位置：`playerZ` = `Agent.Position`（**逻辑**，引擎算的落脚地面）、`visZ` = `Agent.VisualPosition`（**渲染**，真正画出来的）。

起步瞬间（板从静止切到 1 m/s）：

```
.200   dz=+0.36   vdz=+0.36   ← 起步前，完美
.699   dz=+0.36   vdz=-0.14   ← 🔴 逻辑立刻跟上，渲染那一拍没动
1.199  dz=+0.36   vdz=+0.18   ← 追赶中
1.699  dz=+0.36   vdz=+0.36   ← 恢复，之后全程 +0.36
```

- **逻辑位置当场跟上**（`dz` 全程精确）
- **渲染位置滞后约 0.5~1 秒** → 视觉上"板卡在腰间"；停速后求解器把人完全推出来 ⇒ 恢复脚底
- **与驱动方式无关**，是引擎算"落脚高度"的节拍问题。缓解手段：降速 / 软启动（未验证）。
- 🔴 **出货场景下不用管它**：载具是**隐藏**的（玩家看的是飞行环 / 法阵特效），
  起步那一秒最多是"法阵浮了一下"，不会出现"木板插进腰里"。**木板只是实验期的可视化替身。**

### 🔴 两条走死的物理驱动路（别再试）

**① `ApplyAccelerationToDynamicBody` 闭环校正 → 数值爆炸。**
每帧施加 `a = (目标速度 − 当前速度) / dt`：**半秒内 `bodyV` 从 0 涨到 5.97e14**、`propZ` 冲到 1.15e12 米。
**3 m/s 和 0.1 m/s 都炸** ⇒ 是控制回路正反馈，不是参数问题。
**该 API 不是"当帧施加一次"的语义**（施加量跨帧累积）。

**② 冲量开环 → 速度精确，但人不跟。**
`ApplyLocalImpulseToDynamicBody(0, 质量 × 目标速度)`：`bodyV` 精确 = 目标、匀速稳定，
**但玩家完全不被抬**（板穿过身体）。
⇒ **结论：「物理积分移动」的物体不被 agent 当作落脚面。**

> 顺带否掉一个曾经的假设：瞬移版能托人**不是**因为"求解器不知道板在动"——
> 恰恰相反，**瞬移才是能托人的那种移动方式**。

### 相关 API（1.2.12 实测存在）

| 能力 | API | 备注 |
|---|---|---|
| 读物理速度 | `GameEntity.GetLinearVelocity()` | **没有 setter**（`IGameEntity` 全表只有 getter） |
| 施加加速度 | `ApplyAccelerationToDynamicBody(Vec3)` | ⚠️ 语义不是"当帧一次"，见上 |
| 施加冲量 | `ApplyLocalImpulseToDynamicBody(localPos, impulse)` | 冲量 = 质量 × 速度增量，一次性 |
| 🔴 **关重力** | `GameEntity.DisableGravity()` | **道具侧独有**（agent 侧没有，见结论 3） |
| 动态体 | `EnableDynamicBody()` / `SetPhysicsState(bool,bool)` / `SetMobility` | `Mobility` 枚举成员名两版不同（1.2.12 小写），按**序数**转 |
| 阻尼 | `SetDamping(linear, angular)` | — |

> 实验脚手架全在 `ExampleModVS/ExampleMod/ExampleMod/CampaignMode/Tools/FlySpike.cs` 的 `custom.prop` 命令族。

---

## 七、动态导航件 = 引擎唯一"带人移动"的机制（电梯方案的依据，但 #7 已证明用不上）

完整机制见 [骑砍2动态Navmesh机制分析.md](骑砍2动态Navmesh机制分析.md) §4。要点：

```csharp
DynamicNavmeshIdStart = Mission.Current.GetNextDynamicNavMeshIdStart();
GameEntity.Scene.ImportNavigationMeshPrefab(NavMeshPrefabName, DynamicNavmeshIdStart);
GetEntityToAttachNavMeshFaces().AttachNavigationMeshFaces(DynamicNavmeshIdStart + 1, isConnected: false);   // Inside
GetEntityToAttachNavMeshFaces().AttachNavigationMeshFaces(DynamicNavmeshIdStart + 2, isConnected: true);    // Enter
GetEntityToAttachNavMeshFaces().AttachNavigationMeshFaces(DynamicNavmeshIdStart + 3, isConnected: true);    // Exit
GetEntityToAttachNavMeshFaces().AttachNavigationMeshFaces(DynamicNavmeshIdStart + 4, isConnected: false, isBlocker: true); // Blocker
```

- **导航件 = 编辑器导出的面预制**，落在 `Modules/<模块>/NavMeshPrefabs/*.bin`（Native 里有 20+ 个现成范本，如 `siege_tower_12m_dnm.bin`、`ballista_a.bin`）。
- 文件是**明文二进制**：版本头 + `"NMG4"` 魔数 + 顶点数 + 顶点（3 个小端 float + 20 字节）+ int32 拓扑表。最小的 `ballista_a.bin` 才 329 字节 = 一块 4 顶点方板。
- **`isConnected: true` = 接到主 navmesh**（Enter/Exit 槽）；⚠️ **给会飞的平台连通主 navmesh，等于在场景里挂一条通往空中的路 → 可能污染全场景 AI 寻路**。

### 7.1 四个未知数——已实测，方案**封**（见 §6 表 #7）

| # | 未知数 | 实测结果 |
|---|---|---|
| ① | 面垂直移动时 agent 跟不跟着上 | **没验到**（因为②没过，人根本没上板） |
| ② | agent 怎么"上"到板面 | ❌ **过不去**。板建在玩家脚下（同 X/Y）、留 0.3m 台阶、`custom.move` 也挪过——玩家导航面 id **始终是场景自己的 `2`**，`onPlate` 恒 False |
| ③ | `isConnected` 该给什么 | 两种都试过（`on`/`off` 重挂），都不改变② |
| ④ | 同 (x,y) 有场景地面与板面时引擎选哪个 | 实测：**选场景自己的面**（板面被忽略） |

**实测细节**：`Scene.ImportNavigationMeshPrefab("ballista_a", idStart)` 是**成功的**
（场景面数 `15656→15657`；`custom.plate scan` 在 `[idStart, idStart+9]` 里找到 1 个面），
板的实体位置也正确——**唯一卡住的就是"人不上板"**。

**若将来还要碰这条路**：唯一没试的方向是"让玩家**走上**板"（而不是把板瞬移到脚下），
但那要求板与主 navmesh 连通，会污染全场景 AI 寻路。

---

## 八、本次会话留下的测试指令（都在 `Debug/MyCommands.cs`）

> ⚠️ 这些是**实验脚手架**，不是玩法功能。结论已全部并入本文档；将来要复验某条路时直接拿来用。

| 指令 | 作用 | 对应表 # |
|---|---|---|
| `custom.print_pos_dir [StringId]` | 打印坐标（无参 = 玩家），输出可直接粘进 C# | 全部 |
| `custom.teleport <x> <y> <z> player` | 绝对坐标（老指令） | 1 |
| `custom.move <dx> <dy> <dz>` | **增量**移动；骑乘时会分别报 `riderZ` / `mount` 坐标 | 1、3 |
| `custom.set_height <z>` / `+d` / `-d` | 只改 Z | 1 |
| `custom.set_controller player\|ai\|none` | 控制权（**结论：做位移不需要它；拨掉反而会丢镜头**） | 2 |
| `custom.mount_scale <倍数>` | 坐骑缩放 + 骑手归 1（**走反射**，因 native 绑定不可达） | 4 |
| `custom.lift [root] <米>` | 抬骑手坐点骨；加 `root` = 抬坐骑根骨（每帧重写 + `survived` 残值诊断） | 5 |
| `custom.vlift <米>` | 抬 agent 外观帧（`MBAgentVisuals.SetFrame`） | 6 |
| `custom.plate spawn\|scan\|under\|z\|up\|stop\|connect\|localize\|remove` | 动态导航件升降板 | 7 |
| `custom.chair_to_me [距离]` / `custom.chairlift <米>` / `custom.usable_near [半径]` | 搬椅子+强制坐上 / 抬正在用的物件 / 列附近可交互物件 | 8 |
| `custom.airhold <米每秒\|0\|off>` / `custom.airjump [ctrl]` | 空中态接管（**只在 onLand=False 时写**）/ 触发跳跃（★ 空格即可，`ctrl` 有害） | 9 |
| `custom.fly` / `custom.flytest` | 飞天 spike（最早期的脚手架） | 1、2 |

**代码位置**（2026-09-19 已合并整理）：
- **`CampaignMode/Tools/FlySpike.cs`（1842 行，全部飞行尝试都在这一个文件里）**：
  `FlySpikeState` / `FlySpikeCommands`（fly、flytest）/ `FlySpikeMissionView` / `FlySpikeFrameTickPatch` /
  `PlateSpike*`（plate）/ **`FlySpikeExperimentCommands`**（vlift、lift、airhold、airjump、mount_scale、chair_to_me、chairlift、usable_near）
- `Debug/MyCommands.cs`：只留**通用**调试指令（`set_height` / `move` / `set_controller` / `print_pos_dir` / `find_chairs`）——它们不属于飞行，任何调试都可能用

---

## 相关文档

- [骑砍2动态Navmesh机制分析.md](骑砍2动态Navmesh机制分析.md) — 导航件/面组开关全量 API（§4 动态导航件管线）
- [Agent_AI底层原理.md](Agent_AI底层原理.md) — 五层 AI 控制参数体系、`AIScriptedFrameFlags`、DrivenProperties
- [原版场景跟随系统分析.md](原版场景跟随系统分析.md) — `AgentNavigator.SetTargetFrame` 等丝滑移动三件套
