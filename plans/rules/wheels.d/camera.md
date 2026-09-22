# camera — 轮子速查分卷（wheels.md 索引导航）

> 路径相对 `ExampleModVS/ExampleMod/ExampleMod/`。这一卷管**演出/过场相机**（把镜头接管过来、按机位摆好）。

## 🔴 弹簧臂相机（模板机位 + 跟随模式）— `Camera/`（2026-09-22 登记）

**三个件**：

| 件 | 作用 |
|---|---|
| `Camera/SpringArmCameraView.cs` | MissionView（`MySubModule` 注册）。**两个入口**：静态机位 / 跟随机位 |
| `Camera/SpringArmMath.cs` | 纯数学 `ComputeFrame` / `Lerp` / `Ease` —— **与飞行相机共用同一份**（`Flight/FlightCameraRig.cs`），别再抄第二份 |
| `ModuleData/DesignData/Camera.csv` | 机位模板表（10 个模板；上游导入文件，表头列结构勿动） |

```csharp
// ① 静态机位：只按"那一刻"的角色帧算一次 —— 角色一动就出画（对话/剧情演出在用）
SpringArmCameraView.UseCameraTemlate("xm_Any_Side45_Far_R", speaker, listener, Vec3.Zero);
// ② 🔴 跟随机位 —— 每帧按角色当前帧重算，角色走到哪镜头跟到哪。**两个入口，按用途挑**：
//    ②a 表演镜头的默认做法（2026-09-22 用户裁定）：照抄引擎相机开演那一刻的机位，方向冻住、只跟位置
bool ok = SpringArmCameraView.ApplyFollowFromEngineCamera(agent, seconds /* ≤0 = 不限时 */);
//    ②b 要指定"看角色的哪个角度"时用模板（方向按模板，**跟着角色转**；臂长/FOV 从引擎相机渐变过去）
bool ok2 = SpringArmCameraView.ApplyFollowTemplate("sp_lordshall", agent, seconds);
SpringArmCameraView.StopFollowCamera();      // 立刻归还（幂等）；到点会自己渐变归还
```

🔴 **②a 与 ②b 的区别 = 方向跟不跟角色转**：②a **世界锚定**（方向锁死在开演那一刻，角色转身镜头不甩）——
"视角还是玩家原来的视角，只是跟着人平移"；②b 锚在角色身上（`ArmYaw=0` 就是角色正后方，角色转身镜头跟着转）。
表演类需求默认用 ②a；只有明确要"必须看角色背后/侧面"时才用模板。

**机位口径**（模板列）：`Pivot*` 锚点偏移（锚 = 角色脚底 + 眼高 1.4626 m）· `ArmLength` 臂长 = 相机离角色多远 ·
**`ArmYaw = 0` = 角色正后方**（仅在 `IsAnchorWorld=false` 的模板路径下是这个含义）· `ArmPitch` 俯仰 ·
`Socket*` 相机额外偏移 · `Self*` 相机自身旋转 · `Fov` 视场 · `AttachType`（Player/Speaker/Listener/AnchorWorld）
**只喂静态入口**，两个跟随入口都不看它（锚 = 你显式传的 agent）。几个现成模板：`sp_lordshall`（领主背后，
约 4 m 后方 + 低 0.76 m）· `sp_eye`（第一人称）· `xm_Self_Side30_Mid_R` / `xm_Any_Side45_Far_R`（对话过肩/侧 45°）。

🔴 **照抄引擎机位的方向换算走 `Vec3.RotationZ` / `RotationX`**（`TryBuildEngineCameraParam`），
**不是 `atan2(y, x)`** —— 两者差 90°，飞行相机 2026-09-21 在这上面栽过一次（接管瞬间镜头横甩 90°）。

🔴 **接管相机 = 必须接管"看"**：一挂 `MissionScreen.CustomCamera`，`MissionScreen.CheckForUpdateCamera` 整段早退
⇒ 引擎不再处理鼠标 look，`CameraBearing/CameraElevation` **冻在接管那一刻**。几秒的表演镜头无所谓；
要长时间能自由看，得像 `Flight/FlightCameraRig.cs` 那样自己读鼠标（`ApplyLook`）+ 归还时用反射把朝向写回引擎。

🔴 **进出场只渐"臂长 / FOV"，不渐方向** —— 方向按模板一次到位；渐方向 = 镜头绕着人转一圈（飞行相机实机踩过）。

**判据 / 坑**：
- 模板名不在表里 → 两个入口都**明确失败**（`ApplyFollowTemplate` 返回 false + `[FollowCam]` 日志），**不接管**，
  引擎相机继续用；别让"什么都不发生"静默过去。
- 跟随模式的锚点没了（换场景 / agent 被移除）→ 自己收摊（日志 `[FollowCam] 已归还相机`）。
- 想边调边看：`custom.openSpringArmCamDebugger`（带滑杆的调试 UI）· `custom.useSpringArmCamera <模板> [agentId]`。
