# 骑砍2 骨骼级动画 API 与运行时姿势合成

> 日期: 2026-09-21
> 结论: `Agent.SetActionChannel` **改造不了**——它是直通 native 的薄壳，只传「播哪段动画」，不传「摆什么姿势」，
> 混合逻辑全在 C++，C# 侧没有介入点。
> 但引擎另有**一整套骨骼级 API**（1.2.12 ~ 1.5.1 全都有，公开可用），四个环节齐全：
> 采样单动画的逐骨变换 → 按比例混合 → 写回骨骼 → 强刷上屏。
> 唯一没验的门槛 = **写进去的骨骼帧能不能上屏**（5 分钟可验，见 §四）。

---

## 一、`SetActionChannel` 是什么（反编译实证）

```csharp
// TaleWorlds.MountAndBlade.Agent（1.2.12 在 Agent.cs:2076；1.5.1 在 Agent.cs:2410，签名同构）
public bool SetActionChannel(int channelNo, ActionIndexCache actionIndexCache, bool ignorePriority = false,
    ulong additionalFlags = 0uL, float blendWithNextActionFactor = 0f, float actionSpeed = 1f,
    float blendInPeriod = -0.2f, float blendOutPeriodToNoAnim = 0.4f, float startProgress = 0f,
    bool useLinearSmoothing = false, float blendOutPeriod = -0.2f, int actionShift = 0,
    bool forceFaceMorphRestart = true)
{
    int index = actionIndexCache.Index;
    return MBAPI.IMBAgent.SetActionChannel(GetPtr(), channelNo, index + actionShift, ...);  // ← 全丢给 native
}
```

| 问题 | 答案 |
|---|---|
| 传的是「什么姿势」吗？ | **不是**。传的是「哪段动画」——一个 int 编号，native 拿去动画表里取预烘好的 clip |
| `index` 是什么？ | `ActionIndexCache.Index` = 动作名换成的编号。名字先存着，第一次用才去查表（`ActionIndexCache.cs` 里叫「延迟解析」） |
| 能调的旋钮有哪些？ | 通道号（0 / 1）、淡入淡出时长、播放速度、起始进度、优先级、一堆开关位 |
| 有骨骼 / 变换 / 权重参数吗？ | **一个都没有** |

**引擎自带的「两个动画同时播」只有一种，而且是硬切不是比例混合**：

- 通道 0（全身）+ 通道 1（上身）按**身体区域**叠加，靠 `anf_enforce_lowerbody` / `anf_enforce_all` 控制通道 1 要盖住哪些部位。
- **没有 `SetActionChannelWeight`**——只有读的 `GetActionChannelWeight`（Agent.cs:2463）。比例这个旋钮引擎压根不给你。

**顺带记一条死路，省得再查**：`AnimFlags` 里有两个遗留常量 `anf_animation_layer_flags_mask = 0xFFFF000000000` / `anf_animation_layer_flags_bits = 0x24`（看着像「动画叠加层」，位段在 bit 44~59、移位 36）。
**全库检索确认：没有任何 C# 代码消费它们，也没有配套的层枚举**——`AnimFlags.cs` 里孤零零两个常量。别顺着「图层」这个词往下猜。

---

## 二、能干这活的工具台（全部公开，双版本可用）

分两层：`TaleWorlds.Engine.Skeleton`（**渲染骨架本体**）+ `MBSkeletonExtensions`（骨架扩展方法，`TaleWorlds.MountAndBlade` 命名空间）。

> 🔴 **版本结论**：这两个类在 **1.2.12 与 1.5.1 上成员完全一致**，可以直接写，不需要 `#if` 分支。
> 唯一的版本差异在**另一个入口**：1.5.1 多了个 `IMBAgent.GetBoneEntitialFrameAtAnimationProgress`（agent 级采样器），1.2.12 没有。
> **两种写法都用下面的 `Skeleton` 扩展方法版本即可**，它双端都在。

| 要做的事 | API | 出处 |
|---|---|---|
| **取某动画在某进度的逐骨变换** | `skeleton.GetBoneEntitialFrameAtAnimationProgress(bone, animIndex, progress)` | `MBSkeletonExtensions` |
| **写一根骨** | `skeleton.SetBoneLocalFrame(boneIndex, frame)` | `Skeleton:70` |
| **把写进去的帧算出来**（关键，别漏） | `skeleton.UpdateEntitialFramesFromLocalFrames()` 或 `skeleton.ForceUpdateBoneFrames()` | `Skeleton:159 / :90` |
| **停掉动画系统的覆盖** | `skeleton.Freeze(true)` / `IsFrozen()` | `Skeleton:60` |
| 取绑定姿势（当混合基准用） | `GetBoneEntitialRestFrame` / `GetBoneLocalRestFrame` | `Skeleton:179 / :186` |
| 取某通道当前姿势的逐骨帧 | `skeleton.GetBoneEntitialFrameAtChannel(channelNo, bone)` | `Skeleton:200` |
| 在骨架上播动画（按编号或名字） | `skeleton.SetAnimationAtChannel(animIndex, channel, speed, blendIn, startProgress)` | `MBSkeletonExtensions` |
| 把动画拖到任意进度（定格用） | `skeleton.SetAnimationParameterAtChannel(channel, 0..1)` | `Skeleton:259` |
| 推进动画 / 推进并强刷 | `TickAnimations(...)` / `TickAnimationsAndForceUpdate(...)` | `Skeleton:244 / :249` |
| 动作名 → 动画编号 | `MBActionSet.GetAnimationIndexOfAction(actionSet, actionIndexCache)` | `MBActionSet` |
| 动画时长 / 名字 / 位移向量 | `MBActionSet.GetActionAnimationDuration` / `GetActionAnimationName` / `GetActionDisplacementVector` | `MBActionSet` |
| 拿 agent 的渲染骨架 | `agent.AgentVisuals.GetSkeleton()` | `Agent.cs:930` + `MBAgentVisuals` |
| **绕开 Agent 逻辑层直接设动作通道** | `agent.AgentVisuals.SetAgentActionChannel(channel, actionCode, ...)`（吃 `ActionIndexCache.Index`） | `MBAgentVisuals` |

> 🔴 **别把两套索引搞混（这是最容易白费一下午的坑）**：
> - **动作码** = `ActionIndexCache.Index`（动作名字换来的编号）→ 给 `SetAgentActionChannel` / `Agent.SetActionChannel`。
> - **动画编号** = 动画 id 换来的编号 → 给 `SetAnimationAtChannel` / `GetBoneEntitialFrameAtAnimationProgress`。
> - **两者之间的桥** = `MBActionSet.GetAnimationIndexOfAction(actionSet, actionIndexCache)`；
>   或直接问骨架「你通道 0 现在播的是哪个动画」= `skeleton.GetAnimationIndexAtChannel(0)`（先例 mod 就是这么拿的）。

**两个容易被忽略的好东西**：

- `SetAgentActionChannel`（设在**骨架**上，不经过 `Agent`）——**agent 自己的动作状态和 AI 完全不知情**。想做「表现层覆盖、逻辑层不动」时这是比 `Agent.SetActionChannel` 更干净的口子。
- `SetAnimationParameterAtChannel` + `Freeze(true)` —— 这套组合能把角色**定格在动画的任意一点**，是下面所有「定格/定格调参」用法的基础。

---

## 三、运行时两动画混合：方案骨架

```csharp
// 每帧，在动画推进之后
Skeleton sk = agent.AgentVisuals.GetSkeleton();
int n = sk.GetBoneCount();                       // 人形约 30 根（上限 64）
for (sbyte b = 0; b < n; b++)
{
    MatrixFrame fA = sk.GetBoneEntitialFrameAtAnimationProgress(b, animA, pA);
    MatrixFrame fB = sk.GetBoneEntitialFrameAtAnimationProgress(b, animB, pB);
    Quaternion q = Quaternion.Slerp(fA.rotation.ToQuaternion(), fB.rotation.ToQuaternion(), t);
    MatrixFrame f = new MatrixFrame(Quaternion.Mat3FromQuaternion(q),
                                    Vec3.Lerp(fA.origin, fB.origin, t));
    sk.SetBoneLocalFrame(b, f);
}
sk.UpdateEntitialFramesFromLocalFrames();        // 不调这个，世界帧和蒙皮矩阵大概率还是旧的
```

混合数学的零件都在 `TaleWorlds.Library` 里，齐的：`Mat3.ToQuaternion()` / `Quaternion.Slerp` / `Quaternion.Mat3FromQuaternion` / `Mat3.Lerp` / `Vec3.Lerp`。

### 代价（两条，都不是小事）

| 代价 | 量级 | 说明 |
|---|---|---|
| **性能** | 一个人约 30 骨 → **每帧 90 次托管↔原生跨界调用** | 一两个主角级 agent 可行；整队 / 战场上批量用必卡 |
| **写入时机** | 必须晚于动画推进 | 否则下一帧被动画系统覆盖。最贴近渲染的钩子 = `AgentVisuals.Tick`（`TaleWorlds.MountAndBlade.View/AgentVisuals.cs:367` → `MBAgentVisuals.Tick`），Harmony postfix 挂那里最稳 |

---

## 四、🔴 两个必须先验的未知（决定成败）

### 未知 1：写进去的骨骼帧到底上不上屏

**这是整条路唯一的门槛，5 分钟可验，但至今没验过。**

`ExampleModVS/ExampleMod/ExampleMod/CampaignMode/Tools/FlySpike.cs:1971` 的「抬坐点骨」实验确实调用过 `SetBoneLocalFrame`，但：

1. **它没调 `UpdateEntitialFramesFromLocalFrames()` / `ForceUpdateBoneFrames()`。** 这两个 API 存在的意义就是「外部改了骨、强制重算」——不调，世界帧与蒙皮矩阵大概率还是旧的。
2. **它当时看的是 `riderZ` 没动**（骑手的**逻辑位置**），**不是「网格形不形变」**（**渲染读数**）。这两件事从来没分开验过。
3. 该实验的结论「骑手位置不从该骨实时姿势算」本身是合理的（骑手位置可能压根不读马骨架）——**但它证明不了「骨写不进渲染」**。

**⚠️ 因此对 `Knowledge/骑砍2Agent运动与位置机制.md` §6 的更正**：该表 #5 行结论只覆盖「逻辑读者不跟」；
§6.1 原本那句统括式的「任何绕过逻辑位置的改法（外观帧 #6、骨骼 #5）→ 值改得动，**画面上不出现**」是**外推，不是当场的观测**（该句已按本条就地更正）。
「骨写不进渲染」**目前是未验证状态，不是已证死的结论**。

**验证做法**（复用现成的 `custom.lift`，加两行）：

- 挑一根形变明显的骨（`spine` 或 `r_head`），写完**立刻**调 `UpdateEntitialFramesFromLocalFrames()` + `ForceUpdateBoneFrames()`；
- **肉眼看角色扭不扭**（这是唯一判据，日志读数不作数——日志读的是写入值，不是渲染值）；
- 分两组对照：①只写不刷 ②写 + 强刷。两组差别就是答案。

### 未知 2：坐标系口径（`entitial` 与 `local` 是不是同一个空间）

- **读口** `GetBoneEntitialFrame*` 拿到的是**相对实体**的帧。实证：`Mission.cs:6187` 里 `agentVisuals.GetGlobalFrame().TransformToParent(skeleton.GetBoneEntitialFrameWithIndex(bone))` 才得到**世界帧**。
- **写口** 叫 `SetBoneLocalFrame`（local）。
- 两者是不是同一个空间**没验过**。若 local 指「相对父骨」，写之前得先换算：`local = inverse(父骨 entitial) × 目标 entitial`（父骨索引走 `GetParentBoneIndex`）。
- 实验 1 顺带读一次数就能判定：写一根非根骨，写完读回 `GetBoneEntitialFrame(bone)` —— 数值一致 = 同一空间；差一个父骨变换 = 需要换算。

---

## 五、先例与旁证

| 证据 | 说明 |
|---|---|
| **第三方 mod `DismembermentPlus`**（`Modules/ArtemsCinematicCombat/`） | 已在用 `GetBoneEntitialFrameAtAnimationProgress` 取碰撞骨骼的三维位置（`DismembermentPlusMissionLogic.cs:202`），配 `GetAnimationIndexAtChannel(0)` 拿当前动画编号。**证明这套采样器在实机 mod 里跑得通** |
| **引擎自己的编辑器预览脚本 `CharacterSpawner.cs`** | `Freeze(false)` → `TickAnimationsAndForceUpdate` → `SetAnimationParameterAtChannel(进度)` → `Freeze(true)`——**角色定格在任意姿势**。证明「冻结 + 参数驱动 + 强刷」渲染是认的 |
| **引擎自己的 `HandPose.cs`** | 同样的冻结 + 参数套路，配一个手部专用动作 `act_tableau_hand_armor_pose` |
| **`AgentVisuals.Tick`**（`TaleWorlds.MountAndBlade.View/AgentVisuals.cs:367`） | agent 渲染骨架的动画推进点，写入时机的锚 |
| **`Skeleton.ActivateRagdoll()`** | 布娃娃物理直接驱动骨骼且网格跟着动——旁证渲染读的就是骨骼帧 |
| **游戏自己的 `ForceUpdateBoneFrames()` 调用** | `Mission.cs:2851 / 3010`（从尸体上掉武器前先强刷）、`SiegeLadder.cs`（攻城梯每帧刷）。说明「外部改了骨得手动刷」是引擎既定用法 |
| **`Skeleton.Freeze` 的游戏用法** | 城门（`CastleGate.cs:361`）、远程攻城器械（`RangedSiegeWeapon.cs:655`）——**引擎自己在运行期冻结骨架**，不是编辑器专属 |

---

## 六、什么时候别走这条路：离线烘更省

**只有当混合比例是运行时连续变量**（被速度 / 伤势 / 姿态实时驱动）时，才值得走上面的运行时路。

**固定比例或少量档位 → 离线烘一个新 clip**：零运行时开销、零引擎风险、编辑器里能直接看效果。
本项目已有完整离线管线：

- 管线入口：[tools/anim-retarget/项目总纲.md](../tools/anim-retarget/项目总纲.md)（源 → Blender 重定向 → TRF → ModKit 导入）
- 相关文档：`Knowledge/骨骼动画TRF格式与增量陷阱.md`、`Knowledge/动画导入与UE5重定向_引擎能力与实现路径.md`、`Knowledge/动画带位移_RootMotion与代码推位移.md`

---

## 附：反编译命令与产物位置

```bash
# 反编译某个类型（DLL 路径以 .csproj 的 <Reference> 为准，游戏根 = $(MB2_PATH)）
ilspycmd "$MB2_PATH/bin/Win64_Shipping_Client/TaleWorlds.MountAndBlade.dll" -t "TaleWorlds.MountAndBlade.IMBAgent"
ilspycmd "$MB2_PATH/bin/Win64_Shipping_Client/TaleWorlds.Engine.dll"      -t "TaleWorlds.Engine.Skeleton"

# 整包反编译（慢，但之后随便 grep）
ilspycmd <dll> -o <目标目录> -p
```

本次会话留下的离线转储（`_` 前缀临时物，不进 git，可随时删）：

| 目录 | 内容 |
|---|---|
| `Debug/offline/_decomp_mb1212/` | 1.2.12 `TaleWorlds.MountAndBlade.dll` 全量（含 `Agent.cs` / `MBActionSet.cs` / `AnimFlags.cs` / `MBSkeletonExtensions.cs`） |
| `Debug/offline/_decomp_view/` | `TaleWorlds.MountAndBlade.View.dll` 全量（含 `AgentVisuals.cs` / `CharacterSpawner.cs` / `HandPose.cs`） |
| `Debug/offline/_decomp_dismember/` | `DismembermentPlus.dll`（先例 mod） |

**关键类型速查**：`TaleWorlds.Engine.Skeleton`（渲染骨架本体，写口在这）· `TaleWorlds.MountAndBlade.MBSkeletonExtensions`（骨架扩展方法，采样口在这）· `TaleWorlds.MountAndBlade.MBActionSet`（动画元数据）· `TaleWorlds.MountAndBlade.MBAgentVisuals`（拿骨架 / 绕逻辑层设通道）· `TaleWorlds.Core.Monster`（语义骨索引：`PelvisBoneIndex` / `HeadLookDirectionBoneIndex` / `MainHandItemBoneIndex` …）
