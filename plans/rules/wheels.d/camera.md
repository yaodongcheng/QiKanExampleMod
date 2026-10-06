# camera — 轮子速查分卷（wheels.md 索引导航）

> 路径相对 `ExampleModVS/ExampleMod/ExampleMod/`。这一卷管**演出/过场相机**（把镜头接管过来、按机位摆好）。

## 🔴🔴 【硬规则】相机的一切改动只走这一套框架 —— 要拓展功能，也在框架里拓展（2026-10-06 用户裁定）

**任何涉及自定义相机的东西 —— 加/改机位、接管镜头、跟拍、演出取景、载具/坐骑镜头、任何玩法要"看哪" —— 一律用下面三件，不得另起炉灶：**

| 件 | 是什么 | 你该做的 |
|---|---|---|
| `Camera/CameraService.cs` | **唯一入口 + 持有者仲裁**；全项目**唯一**写 `MissionScreen.CustomCamera` 与 `CameraLook` 登记的地方 | 只说"**用哪一行 case**" + 一个 **owner 标签**：`Play / PlayEnginePose / Adopt / Switch / ApplyPose / SetXxx / RequestHandBack / Stop` |
| `Camera/SpringArmRig.cs` | **唯一的相机机器**（接管 / 渐变 / 归还 / 鼠标驱动 / 锚点 / 弹簧滞后 / 运动驱动） | 要加**新能力**（新的驱动量、新的归还行为）→ 加在**机器里**（或 case 的列/开关里），**别在业务侧再写一台相机** |
| `ModuleData/DesignData/Camera.csv` 的**一行**（解析入口 `Camera/CameraCase.cs`） | **唯一的机位数据** | 新机位 = **加一行**；新参数 = **加一列**（数值列只许追加、语义冻结）。中文只写在**第 3 行标签行**（纯人读） |

**禁止清单**（都有前科）：
- ❌ 直接 `MissionScreen.CustomCamera = …` —— 唯一合法写点 = `CameraService.Present / ReleasePresentedIf`。**新代码里出现一次就是错。**
- ❌ 直接读写 `CameraLook.Provider` —— 唯一合法写点 = `CameraService` 那一个"路由器"提供者（用 `CameraLook.Set/Clear(owner)` 成对写法，防单槽互踩）。
- ❌ 自建 `Camera.CreateCamera()` 接管画面 / 自己每帧算帧 = **第 N 台相机机器**（2026-10-05 已把两套合并成一套）。
- ❌ 把机位数值硬编码进代码 —— 数值住表；**表缺行 = 不接管 + 明确日志，不做代码兜底**。
- ❌ 给 NPC / 别的系统另造"玩家相机 UI"（UI 专属通道的边界见铁律 18）。

**改完自查（三行 grep，括号里的命中范围 2026-10-06 实测过）**：
```powershell
# 1) 真写入只应命中 Camera/CameraService.cs（`CameraDebuggerView.cs` 未编译、待删；其余命中是注释）
rg -n "CustomCamera\s*="      ExampleModVS --glob "*.cs"
# 2) 登记"看"只应命中 Camera/CameraService.cs 的 `CameraLook.Set(...)`；直写 `Provider` 只应出现在注释里
rg -n "CameraLook\.(Set|Clear)\(" ExampleModVS --glob "*.cs"
rg -n "CameraLook\.Provider\s*=" ExampleModVS --glob "*.cs"
# 3) 过渡期例外（阶段 4 合并后删）：SpringArmCameraView.cs（视图那台）+ FlightCameraRig.cs（旧飞行机器）
rg -n "Camera\.CreateCamera"   ExampleModVS --glob "*.cs"
```

**过渡期已知例外**（不是新写法，别照抄）：`Camera/CameraDebuggerView.cs` + `CameraDebuggerVM.cs` 已从编译摘除（🪦 阶段 0）；`Flight/FlightCameraRig.cs` 是**旧飞行机器**（自摆相机、向服务登记为"外部持有者"），由 `FlightTuning.UseMergedRig` 决定用不用 —— **阶段 4 A/B 合格后连同它一起删**，此后全项目只剩上面那三件。

**相机命令的三条分界线**（别的模块里还留着的相机相关命令，按这个判该不该搬）：
| 类别 | 住哪 | 现存例子 |
|---|---|---|
| **机位数据**（镜头长什么样） | `Camera.csv` 一行；改它 = `custom.cam set` | `custom.grapple cam <米>`（写 `grapple_pull` 行的 ArmLength）· `custom.flight tune camsens\|campitchmin\|campitchmax\|camlag`（写 `fly_*` 行的内存值） |
| **玩法政策 / 时序**（何时接管、何时还、滑多久） | 留玩法模块，作 `Adopt`/`Play` 的**调用参数**（§2.3 明确不进表） | `custom.grapple cam on\|off`（钩索相机总开关）· `camret` / `lookret`（归还时机与时长）· `custom.flight cam on\|off` |
| **全局标定 / 设备偏好** | 代码（`FlightTuning`） | `tune cammotion`（运动驱动总增益）· `caminvertx/y`（鼠标反向）· `tune mergedrig` |
| 🪦 **已退役（返回迁移提示）** | — | `custom.grapple aimcam\|aimanchor\|aimlift\|aimsens` · `custom.grapple cam t:<模板>` · `custom.flight cam <档> <参数> <值>` |

## 🔴🔴 先认两台相机：引擎相机 vs 自定义相机（2026-09-24 立 · CLAUDE.md 铁律 35 的分卷细则）

| | **引擎相机**（默认） | **自定义相机**（我们挂上去的） |
|---|---|---|
| 谁在用 | 原版战斗 / 大地图视角 | 接管方 = **`Camera/CameraService`**（唯一入口；机器 = `Camera/SpringArmRig`，跟随 / 钩索 / 演出 / 飞行共用）· `Flight/FlightCameraRig`（旧飞行机器，`FlightTuning.UseMergedRig` 决定用不用）· ~~`Camera/CameraDebuggerView`（调试 UI）~~ 🪦 2026-10-05 阶段 0 已删（它是第 3 个 `CustomCamera` 写者；相机调试走 `custom.cam log\|stat\|info\|test\|lift\|play\|stop\|set\|show\|list`） |
| 判据 | `MissionScreen.CustomCamera == null` | `MissionScreen.CustomCamera != null` |
| 鼠标 look | 引擎处理 ⇒ `CameraBearing` / `CameraElevation` **实时** | **引擎整段跳过**（`CheckForUpdateCamera` 只做 `FillParametersFrom` + 从**相机实体**取帧 + `SetCamera`）⇒ 那两个角度**冻在接管那一刻** |
| 视线怎么取 | `Mat3.Identity` 绕 Up 转 `CameraBearing`、绕 Side 转 `CameraElevation`，取 **`.f`**（范本 `SpellPieces.CameraForward()`） | **问接管方自己**：飞行 = `FlightCameraRig.TryGetBasis(out forward, out right)`；演出 = 开演那一刻的机位口径 |
| `Mission.GetCameraFrame()` | `.origin` 可读；🔴 **取方向一律别用它**（2026-09-24 日志实测基向量：`.f` 是**"上"**、`.u` 是**视线的反向**（≈ −视线）、`.s` 是右向）—— 要视线得写 `-rotation.u`，**极易再错一次** | `.origin` = **自定义相机的位置**（引擎每帧从它的实体填进去）⇒ 取位置/算距离没问题，**只有方向会错** |

**铁则**：写任何"看向哪 / 朝哪算"的代码之前先问一句 —— **现在是谁在管相机？**
接管中还用引擎角度 = 画面与计算脱钩。

**✅ 代码侧唯一入口 = `Camera/CameraLook.cs`**（2026-09-24 立）：`CameraLook.TryGet(out forward)`
—— 接管中自动问接管方（`ICameraLookProvider`；飞行 = `PlayerFlightBehavior` 已注册），没接管才用引擎算法；
失败时**调用方回退**（身体朝向 / 保持上一帧），别猜一个值。已接的消费者：
`SpellPieces.CastDirection`（法术/投射方向）· `Compass/CompassHud`（罗盘 yaw）。
🔴 **那一条实机证据**（2026-09-24 日志实测相机帧基向量）：`.f` = **"上"**、`.u` = **视线的反向**、
`.s` = 右向 ⇒ 想要视线得写 `-rotation.u`，**极易再错一次**，所以「取方向」一律别碰 `GetCameraFrame()`。

**归还时也要管方向**：接管期间引擎角度是死的 ⇒ `CustomCamera = null` 之前必须把朝向写回引擎（飞行用反射写），否则归还瞬间镜头跳一下。

**同一个坑的三次实机记录**（都写在这里，别再犯第四次）：

| 时间 | 踩法 | 症状 |
|---|---|---|
| 2026-09-21 飞行 | `atan2(look.y, look.x)` 当 yaw（与 `RotateAboutUp` 差 90°） | 接管瞬间镜头横甩 90° |
| 2026-09-21 飞行 | 拿 `GetCameraFrame().rotation.f` 当视线（那是"上"） | 按 W 不往前飞、一路窜到 **160 米天花板** |
| 2026-09-24 法术 | 同上，拿 `rotation.f` 当施法方向 | 对天开火月牙**朝地飞**；改走 `CameraBearing/Elevation` 算法后日志四发逐一吻合 ✓ |
| 2026-09-24 飞行中施法 | 接管期间直接读 `CameraBearing`（冻值） | 月牙会朝"接管那一刻看的方向"飞 ⇒ `CastDirection` 走飞行分支问 rig ✓ |
| **2026-08-20 起就存在、09-24 才发现** | `Compass/CompassHud` 拿 `GetCameraFrame().rotation.f` 算罗盘 yaw（那是"上"向量） | 罗盘刻度带**低头/抬头时乱跳**、朝向读数不可信 ⇒ 已改走 `CameraLook.TryGet`（同一入口） |

## 🔴 弹簧臂相机 = `CameraService`（唯一入口）— `Camera/`（2026-09-22 登记 · 2026-10-05 整合）

**四个件**（自 2026-10-05「服务化 + 合并机器」起）：

| 件 | 作用 |
|---|---|
| `Camera/CameraService.cs` | **全项目唯一入口 + 持有者仲裁**；也是**唯一写 `MissionScreen.CustomCamera` 与 `CameraLook` 登记**的地方 |
| `Camera/SpringArmRig.cs` | **唯一的相机机器**（接管/渐变/归还/锚点/鼠标驱动/弹簧滞后/运动驱动）—— 跟随、钩索、飞行（开关打开时）共用这一份 |
| `Camera/CameraCase.cs` | `Camera.csv` 的一行 → 参数 + 行为开关（唯一解析入口） |
| `Camera/SpringArmCameraView.cs` | 只剩**视图**：生命周期 / 相机实体 / 调试滑杆 UI。**别在这里写任何"每帧摆相机"的逻辑** |
| `Camera/SpringArmMath.cs` | 纯数学 `ComputeFrame` / `Lerp` / `Ease` / `WithMotion` / `LagToward` |

```csharp
// ① 一次性静态机位：只按"那一刻"的角色帧算一次 —— 角色一动就出画（对话/剧情演出在用）
SpringArmCameraView.UseCameraTemlate("xm_Any_Side45_Far_R", speaker, listener, Vec3.Zero);   // 签名保留（5 处调用点不动）
CameraService.ApplyPose("xm_Any_Side45_Far_R", speaker, listener, Vec3.Zero);   // = 同一件事；**会抢占**当前持有者
// ② 🔴 跟随/接管 —— 一切机位都是 Camera.csv 的一行；调用方只说"用哪一行"
//    ②a 表演镜头的默认做法（Seed=Engine）：照抄引擎相机开演那一刻的机位，方向冻住、只跟位置
bool ok  = CameraService.PlayEnginePose(agent, seconds /* ≤0 = 不限时 */, "perf:exec_pair");
//    ②b 要指定"看角色的哪个角度"时用行（Seed=Row：方向按行、臂长/FOV 从引擎相机渐变过去）
bool ok2 = CameraService.Play("sp_lordshall", agent, seconds, "perf:exec_pair");
// ③ 同一位持有者换阶段（不重播种，钩索收编走这条）/ 换 case（飞行机位切换）
CameraService.Adopt("grapple_pull", "grapple", new CameraStage { Seconds = 4f, Policy = policy, HasPolicy = true });
CameraService.Switch("fly_boost", 0.45f);
// ④ 现场改（仅当前持有者）/ 归还 / 查询
CameraService.SetArmLength(8f); CameraService.SetLook(yaw, pitch); CameraService.SetMotion(in motion);
CameraService.BeginLookReturn(1.2f); CameraService.RequestHandBack(1.2f); CameraService.Stop();
CameraService.IsHeld / Holder / CurrentCase / IsHeldBy("flight") / TryGetBasis(out f, out r);
```

🔴 **仲裁规则**（2026-10-05 用户审定）：常规起播（`Play` / `PlayEnginePose` / `Adopt`）**被占用 = 拒绝** + 日志；
`ApplyPose`（演出/对话的一次性机位）= **抢占**（先停旧持有者）；静态机位可以被新 `Play` 顶掉；
**只有持有者能置 `CustomCamera = null`**（老 bug：飞行 `Exit` 无条件置 null，会踩掉别人的相机）。
**飞行**（阶段 4 前）以"外部持有者"身份登记（`ICameraExternalHolder`）—— 别人起播被拒、演出抢占会叫它放手。

🔴 **②a 与 ②b 的区别 = 方向从哪来**：`Seed=Engine` **世界锚定**（方向锁死在开演那一刻，角色转身镜头不甩）——
"视角还是玩家原来的视角，只是跟着人平移"；`Seed=Row` 锚在角色身上（`ArmYaw=0` 就是角色正后方）。
表演类需求默认用 `Seed=Engine`（`follow_engine` 行）；只有明确要"必须看角色背后/侧面"时才用演出模板行。
两个 `Seed` 的行都由**同一台机器**跑（差别只在播种那一步）。

**机位口径**（行里的列）：`Pivot*` 锚点偏移（锚 = 角色脚底 + 眼高，口径见 `UseEngineEyeHeight`）· `ArmLength` 臂长 ·
**`ArmYaw = 0` = 角色正后方**（仅 `IsAnchorWorld=0` 的行是这个含义）· `ArmPitch` 俯仰 · `Socket*` 相机额外偏移 ·
`Self*` 相机自身旋转 · `Fov` 视场 · `AttachType`（Player/Speaker/Listener/AnchorWorld，**只喂一次性机位那条路**）。
现成行：`sp_lordshall`（领主背后，约 4 m 后方 + 低 0.76 m）· `sp_eye`（第一人称）·
`xm_Self_Side30_Mid_R` / `xm_Any_Side45_Far_R`（对话过肩/侧 45°）· `follow_engine`（照抄引擎机位）·
`fly_*` 四档（飞行）· `grapple_shot` / `grapple_pull`（钩索）。

🔴 **照抄引擎机位的方向换算走 `Vec3.RotationZ` / `RotationX`**（`SpringArmRig.TryBuildEngineCameraParam`），
**不是 `atan2(y, x)`** —— 两者差 90°，飞行相机 2026-09-21 在这上面栽过一次（接管瞬间镜头横甩 90°）。

🔴 **接管相机 = 必须接管"看"**：一挂 `MissionScreen.CustomCamera`，`MissionScreen.CheckForUpdateCamera` 整段早退
⇒ 引擎不再处理鼠标 look，`CameraBearing/CameraElevation` **冻在接管那一刻**。行的 `MouseLook=1` = 机器自己读鼠标
（灵敏度 = 行的 `LookSens` × 引擎 `Input.MouseSensitivity`）；演出那种几秒的镜头不填 = 不驱动。

🔴 **进出场只渐"臂长 / FOV"，不渐方向** —— 方向一次到位；渐方向 = 镜头绕着人转一圈（飞行相机实机踩过）。

**判据 / 坑**：
- 行名不在表里 → **明确失败**（`Play` 返回 false + 日志），**不接管**，引擎相机继续用；别让"什么都不发生"静默过去。
- 跟随模式的锚点没了（换场景 / agent 被移除）→ 自己收摊（日志 `[FollowCam] 锚点没了…已归还相机`）。
- 想边调边看：`custom.openSpringArmCamDebugger`（带滑杆的调试 UI）· `custom.useSpringArmCamera <行名> [agentId]` ·
  `custom.cam play <行名> [秒]`（**首选**，不用开 UI）。

### 🔴 机位数据 = Camera.csv（2026-10-05 阶段 1「数值搬家」已落地）

- **唯一来源** = `ModuleData/DesignData/Camera.csv`（**UTF-8 无 BOM + CRLF + 三行表头**：①英文键 ②类型 ③中文标签；
  数据从第 4 行起）。**数值列只许追加**（语义冻结）—— 老行缺列 = 0 = 旧行为。
  🔴 **2026-10-06 两处清理**：① `ScriptName` 列**已删**（中文描述、全仓零消费者；顺带修掉"中文进控制台"隐患）——
  改中文标签只改**第 3 行**，那是纯人读行，代码一行不碰；② 编码 **GBK → UTF-8**（与同目录 `Emotion.csv` 一致；
  老 GBK 在编辑器里整片乱码，且加载器 `CsvLoader` 本就按 UTF-8 读 ⇒ 旧的中文列在内存里一直是乱码）。
  新增 14 列 = `IsAnchorWorld` · `UseEngineEyeHeight` · `LagSpeed`/`LagMaxDistance` ·
  `FovPerVz`/`ArmPerVz`/`RollPerYawRate` · `Seed`(Engine/Row) · `MouseLook`/`LookSens`/`PitchMin`/`PitchMax` ·
  `AnchorFollowHead`/`AnchorHeight`。
- **解析入口只有一个** = `Camera/CameraCase.cs`（`TryGet(行名, out case)` / `All()`）→ 产
  `SpringArmCameraParam` + 行为开关。🔴 0/1 开关的**类型行写 `float`**（`bool.TryParse("1")` 为 false、
  `DynamicRecord.GetFloat` 只认 float），代码读 `GetFloat(...) > 0.5f`。
- **数值在表里的消费者**：飞行四档（`fly_hover`/`fly_cruise`/`fly_boost`/`fly_aim`，`FlightCameraRig.EnsureCases()` 装载）·
  钩索两档（`grapple_shot`/`grapple_pull`）· 跟随/一次性机位走同一个 `CameraCase`。
  **缺行 = 不接管相机 + 明确日志（不做代码兜底）** —— 代码里留一份"看起来还在"的值只会掩盖表格坏掉。
- **命令（2026-10-05 阶段 3 全部收编到 `custom.cam`）**：`play <case> [秒]`（起播任意行；替代钩索 `aimcam lock/test`
  与飞行逐参数命令）· `stop` · `set <case> <列> <值>`（**热改内存行，改的正是当前那行 ⇒ 实时可见**）·
  `show [case]` / `list`（静态核对）· 以及原有的 `log|stat|info|test|lift`。
  旧名一律返回"已迁移到 custom.cam …"提示（`custom.grapple aimcam|aimanchor|aimlift|aimsens`、
  `custom.flight cam <档> <参数> <值>`、`custom.grapple cam t:<模板>`），**不静默**。
- `custom.flight tune camsens|campitchmin|campitchmax|camlag` 改的也是**表行（内存态）** ——
  `FlightTuning` 里那三个全局字段（`CamLookSensitivity`/`CamPitchMin`/`CamPitchMax`）已删，别再往回加。

### 🔴 合并机器（2026-10-05 阶段 4 · `FlightTuning.UseMergedRig`，默认 0）

- 打开 = 飞行改用 `ServiceFlightDriver`（`Flight/FlightCameraDriver.cs`）走 `CameraService` + `SpringArmRig`
  —— **全项目一台机器、一个 `CustomCamera` 写者**；关 = 旧的 `FlightCameraRig`（自己摆相机，仍向服务登记持有权）。
- 行为层只认接口 `IFlightCameraDriver`（`Enter/Exit/BeginHandBack/ApplyLook/SetMotion/SetPreset/Tick/IsActive/
  IsHandingBack/LookYaw/TryGetBasis`）—— 两台机器各实现一份，**别在行为层写 `if (UseMergedRig)`**。
- **改 `UseMergedRig` 要重进场景**（驱动在行为对象构造时选定）。
- 🔴 **A/B 时盯一处口径差**：写回引擎朝向的**时机** —— 旧机器"滑行结束时写回"、合并机器"滑行开始时写回"
  （与跟随/钩索那条路一致）。落地瞬间镜头有差就先看这里。

## 🔴 弹簧臂的两个共用件：**位置滞后** + **运动驱动**（2026-09-28 立）

> **解决什么**：① 相机焊死在角色身上 ⇒ 角色永远在画面正中央，**运动的方向差在画面上看不见**（看着"没有质量"）；
> ② 镜头参数只按机位**死值**摆，没有速度感（俯冲和悬停一样广）。
> 两件都是**纯数学 + 可选填参**：不填 = 行为与加它之前**逐字节一致**（演出相机就没填）。

| 件 | 在哪 | 对应 UE `SpringArmComponent` |
|---|---|---|
| `SpringArmLagState`（结构体）+ `SpringArmMath.LagToward` | `Camera/SpringArmMath.cs` | `bEnableCameraLag` + `CameraLagSpeed`（`VInterpTo` 口径逐字照抄）|
| 参数 `LagSpeed` / `LagMaxDistance` | `SpringArmCameraParam`（`Camera/SpringArmCameraView.cs`）| `CameraLagSpeed` / `CameraLagMaxDistance` |
| `SpringArmMotion`（结构体）+ `SpringArmMath.WithMotion` | `Camera/SpringArmMath.cs` | 无直接对应（UE 那份靠动画通知 `BPANS_SetCameraLag` 按状态改参数）|
| 参数 `FovPerVz` / `ArmPerVz` / `RollPerYawRate` | 同上 | —— |

**接法（消费者侧就三行）** —— 范本 = `Flight/FlightCameraRig.cs` 的 `Tick`：

```csharp
private SpringArmLagState _lag;          // 字段：滞后状态
private SpringArmMotion _motion;         // 字段：本帧运动量（行为层每帧 SetMotion 喂）
// 帧内（算完参数、摆相机之前）：
p = SpringArmMath.WithMotion(in p, in _motion);                       // 运动驱动叠到 FOV/臂长/侧倾
SpringArmMath.ComputeFrame(agent, in p, out MatrixFrame frame, out float fov);
frame.origin += _lag.Update(agent.LookFrame.origin, p.LagSpeed, p.LagMaxDistance, dt, lagFade);
```

🔴 **三条口径（推过/踩过才写）**：
1. **滞后的是"角色锚点"，不是相机位置** —— 相机位 = 锚点 − 前向 × 臂长 ⇒ 锚点滞后多少相机就挪多少，
   与 UE 的 pivot 滞后等价（**前提 PivotX/Y/Z = 0**；飞行四个机位都是 0，哪天真填了 pivot 偏移这条要重推）。
2. **松开滞后必须渐隐**（`SpringArmLagState.FadeSpeed` = 8/秒）：冲刺稳态尾巴 = 速度 ÷ LagSpeed ≈ 6.5 米，
   硬关 = 镜位一帧跳 6.5 米（肉眼就是"镜头被弹一下"）；**归还相机时再乘 `1 − 渐变进度`** ⇒ 撒手那一刻正好归零。
3. **运动驱动只喂"连续量"**：我们的飞行速度**大小**是离散的（0 / 9 / 26，Shift 一按一换）⇒ 拿它驱 FOV 只有三个台阶。
   真正连续的是 **竖直速率**（俯冲/爬升多快 ⇒ 视场变广、镜头拉远）与 **航向角速度**（转得多快 ⇒ 侧倾；
   **必须先平滑**，照 UE `FInterpTo(…, 5)`，否则"转到位"那一帧角速度从 180 突降到 0、侧倾会顿一下）。
   竖直速率在 `WithMotion` 内部钳到 **20 m/s**（出机坠落 36 m/s，不钳 FOV 会被拉到失真）。

**现成消费者 + 旋钮**：飞行相机（`Flight/FlightCameraRig.cs`）四档机位各带一套值 ——
`custom.flight cam <hover|cruise|boost|aim> lag|lagmax|fovvz|armvz|rollyaw <值>`；
一键开关 = `custom.flight tune camlag 0`（关滞后）/ `custom.flight tune cammotion 0`（关运动驱动）。
**瞄准档两样都固定关**（要瞄目标时镜头必须是硬的）。
新消费者自己接的话：**别改 `SpringArmMath.ComputeFrame`**，照上面三行接在它外面。

### 🔴 「相机射线」会泡在几何体里 —— 打空时要有兜底（2026-10-03 钩索实机）

**症状**：拿 `相机位置 + 视线` 打射线做"准星指哪"（武器瞄准/拾取目标那类），
**第一发正常、之后每一发都"什么都没打到"** —— 换个站位又好。
**根因**：第三人称瞄准机位（尤其举弓/举弩时）会**贴到角色肩后**，玩家背靠墙/柱时**相机原点进到几何体内部**：
射线从实体内部出发**没有入射面**（多数射线不报"内部出发"的命中），于是一路穿出去、20 米内什么都没有。

**规避（两条一起上）**：
1. **兜底射线**：能用"引擎自己算的那一枪/那一次交互的起点与方向"就用它 —— 武器开火在
   `Mission.OnAgentShootMissile` 里有现成的 `position`/`velocity`（起点在武器上、方向是引擎算的弹道），
   相机射线打空时拿它再打一条。范本 = `Combat/GrappleFirePatch.cs` + `GrappleLogic.ThrowFromShot`。
2. **打空必留证据**：把射线**起点 / 方向 / 终点**打进日志（"泡在几何体里"一眼可辨）；
   再配一条"命中点离相机 < 1 米 ⇒ 判为相机贴进几何体、拒发并说明"的退化闸。

## 🔴 脚本驱动的短期接管：四条归还纪律（2026-10-03 钩索拉拽四轮实机，全部有日志/反编译证据）

> 场景 = "脚本推着玩家动 1~2 秒（拉拽/演出/载具），期间用自家相机跟拍"。四条按踩坑顺序：
> 范本 = `Combat/GrapplePull.cs`（`EnterCamera/ExitCamera`）+ `SpringArmCameraView` 的
> `ApplyFollowFromEngineCamera(…, writeBackLookOnReturn)` 重载 + `WriteBackLookToEngine`。

1. 🔴 **方向别用"模板相对角色"的机位**：模板的 `ArmYaw` 是**相对角色朝向**算的（`SpringArmMath.ComputeFrame` 用 `agent.LookFrame.rotation` 起算）⇒ 角色一转，相机绕着他转 = "镜头猛转"。
   **脚本接管时的正解 = 世界锚定**：抄**接管那一刻的引擎机位**（`ApplyFollowFromEngineCamera`）——视角保持你出发时的样子，只跟着人平移。
   拉远只需 `SetFollowArmLength(米)`（演出相机是硬跟随、无弹簧滞后；快镜头下这正是要的）。
2. 🔴 **接管快照不能被覆盖**：`SetFollowArmLength` **只改 `_followTarget`**（进场平滑变长、归还平滑回落）；
   `_followFrom` 是"接管那一刻的引擎相机快照"，**归还渐变的终点就是它** —— 覆盖了 = 归还没有可回落的终点 = 撒手一帧弹回。
3. 🔴 **pitch 别窄钳位**：抄引擎机位时若把 pitch 钳到 `[−75,45]`，**抬头场景**（钩索瞄屋顶、仰视演出）就会与引擎实际角度**系统性对不上**，归还时镜头被迫转回差值。放宽到 **±85**（只防臂翻转）。
4. 🔴🔴 **归还 = 把我们的朝向写回引擎，不是渐回引擎角度**（**这条最难，四轮才定**）：
   **引擎的 `CameraBearing/Elevation` 在接管期间也会变**（实测：开火后瞄具复位把 bearing 转了 ~87°）⇒ "渐回引擎角度" = 落地瞬间镜头**扫 87°**；"不渐" = **硬跳**。两个都错。
   **正解 = 照飞行工程 `HandBackLookToEngine`：接管结束前把我们当前的 yaw/pitch 写回 `CameraBearing/Elevation`**（反射写私有 setter）⇒ 引擎从我们停的地方接着看、**零旋转**；反射失败才退回"渐回"（不硬跳）。
   角度口径**可逆、无需符号校准**：引擎 look = `RotateAboutUp(bearing)` 再 `RotateAboutSide(elevation)`，而 `Vec3.RotationZ/RotationX` 正好是它的分解（反编译 `Mat3.RotateAboutUp/RotateAboutSide` 实证；⚠️ `RotationZ = atan2(−x, y)`，与"角色移动方向"那族 `atan2(y,x)` **差 90°**，混用会把好数据判成坏数据）。

## 🔴🔴 相机与引擎"严丝合缝"的完整公式与归还三步（2026-10-04 收官 —— **取代上一条的第 4 点**）

> **完整版（逐字公式 + 反编译行号 + 排查记事）= [Knowledge/骑砍2相机系统_自定义相机与引擎对齐.md](../../Knowledge/骑砍2相机系统_自定义相机与引擎对齐.md)**。这里只留必须记住的：

1. **相机 = 5 个参数**：锚点 / 臂长 / pitch / yaw / fov。**5 个全同 ⇒ 画面全同**；"切相机时跳一下"永远是其中某个**实际不同**，按公式逐项对，别猜。
2. **引擎锚点比我们以前抄的多两项**（`SpringArmMath.ResolveEngineEyeHeight` + `ComputeEngineLift`）：
   - 基准点 = **`Agent.VisualPosition`**（**不是** `Position` —— 被木板/载具搬运或视觉插值时能差几十厘米，实测 −0.36m）；
   - 高度 = 站姿 `(StandingEyeHeight+0.2)×scale` · 蹲坐 · **骑马（另加坐骑项 —— 最容易漏的一支）** · 倒地/特殊动画 `0.5`；
   - 🔴 **再沿"画面向上"抬 `0.7×scale×cos(1/((臂长/scale − 0.2)×30 + 20))^3500`** —— 臂长 1.83m 时 = **0.484 米**。**漏这一项 = 交接瞬间相机高度差半米**（我们为此排查了两轮，最后靠它收官）。
3. **我们持有相机期间，引擎有一批状态被冻结**（`_cameraSpecial*` 8 项 / `_cameraAddedElevation` / 锚高平滑）⇒ **撒手第一帧一次性释放 = 跳变**。**撒手前清零**：`SpringArmCameraView.ClearEngineSpecialCameraAdds`（钩索专用开关 `clearSpecialCameraOnReturn`）。
4. **归还三步**（**取代**上面那条的第 4 点"写回引擎"）：
   ① 撒手前清 `_cameraSpecial*`；
   ② 方向在归还滑行期**追引擎的实时值**（`ChaseEngineLook`：指数平滑 + **yaw 走最短弧** + 收尾收紧），**差 ≤2° 才撒手**，超时上限兜底；
   ③ **不要写回角度** —— 会被"解冻触发的一次性相机重置"打掉（见 5）。
5. **切 Controller（冻结/解冻玩家）⇒ 引擎重置相机**：解冻（`Agent.Controller = Player`）→ `Mission.MainAgent = this` → 下一帧 `HandleUserInput`（**每帧跑、不受 CustomCamera 挡板保护**）执行 `CameraBearing = 角色移动方向; CameraElevation = 0`。⇒ **任何"冻结玩家"的功能（钩索/飞行/未来载具），解冻后都必须按这条重新对齐**。
6. **调试工具已归位相机模块**（2026-10-04）：`custom.cam log|stat|info|test|lift`（`Camera/CameraCommands.cs`）；`[FollowCam]` 诊断日志**默认关**（`custom.cam log 1` 打开，会话级）。
   **黄金测试** = `custom.cam test` 原地切换相机：两边**坐标厘米级一致、画面零变化**即通过（收官实测四方坐标 `(420.61,391.81,6.97)` 完全相同）。
