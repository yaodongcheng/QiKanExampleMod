# SwordBeam（剑气 mod）实现分析 —— 「自管实体飞行物」的完整范本

> **为什么写这份**：法印工程（[plans/法印施法体系-实施计划.md](../plans/法印施法体系-实施计划.md) §十六）已裁定
> **弃 `flying_mesh`、改自管实体**（引擎导弹渲染约 110 米必剔除网格）。SwordBeam 是一个**实机跑得通的自管实体飞行物**，
> 而且**零粒子、零导弹管线**，正好是我们要抄的那条路。
> **素材** = `Modules/SwordBeam`（1.3.15 客户端；1.5.x 版内部结构逐字相同）反编译实证，2026-09-23。
> **读法**：§五 是能直接抄的 API 速查；§六 是我们工程的落地清单。

---

## 零、一分钟速览

| 问题 | 它的答案 |
|---|---|
| 飞行物是什么？ | **普通场景实体**（`GameEntity`）—— **完全不走导弹管线**：没有 `flying_mesh`、没有 `AddCustomMissile`、不占用物品 |
| 谁推它？ | **自己**：每帧 `Entity.SetGlobalFrame(位置 += 速度 × dt)` |
| 谁判命中？ | **自己**：每 0.05 秒拿「上一帧判定点 → 当前判定点」连成线段，跟 agent 的**碰撞胶囊**做距离判定 |
| 伤害怎么进引擎？ | 手搓 `Blow`（`DamageCalculated = true`）+ 手搓 `AttackCollisionData` → `victim.RegisterBlow(...)` |
| 飞多久/多远？ | 自己的 `_maxDistance`（21 米）/ `_maxLifetime`（≥15 秒），到点 `Entity.Remove(0)` |
| 视觉靠什么？ | **网格 + 自发光材质副本 + 一盏点光源 + 音效** —— **零粒子** |
| 出发方向？ | 玩家视线（`LookDirection`，**z 清零 = 贴地平飞**）；**倾斜角来自攻击动作方向**（左/右斩各偏 ±30°） |
| 谁触发？ | **动作阶段跳变**（不是按键监听）：上半身通道 stage `None→AttackRelease` 且进度 > 0.4 |

---

## 一、任意剑身自发光（`SwordBeam.Beam.WeaponChargeGlow`）

**结论**：**复制一份材质再打开 `self_illumination` 标记**，绝不动原材质；发光强度走 **Vector Argument 的 w 分量**；用完**逐项还原**。

### 1.1 三段式：捕获 → 每帧施加 → 还原

```
CaptureAndApplyMaterials(武器实体, 槽位)      ← 只在「换武器 / 首次蓄力」时做一次
Update(agent, chargePower)                   ← 每帧（蓄力中）
Restore()                                    ← 蓄力结束 / 收刀 / 换武器 / agent 没了
```

### 1.2 捕获（怎么"任意"剑都能发光）

```csharp
// ① 拿到「手里那把武器」的【视觉实体】—— 关键入口
EquipmentIndex idx = agent.GetPrimaryWieldedItemIndex();
WeakGameEntity weaponEntity = agent.GetWeaponEntityFromEquipmentSlot(idx);

// ② 实体 + 全部子件一起遍历（剑可能是多件拼的）
var list = new List<WeakGameEntity>();
weaponEntity.GetChildrenRecursive(ref list);
list.Add(weaponEntity);

foreach (var e in list)
  for (int i = 0; i < e.MultiMeshComponentCount; i++) {
      MetaMesh mm = e.GetMetaMesh(i);
      _metaMeshStates.Add(new MetaMeshColorState(mm, mm.GetFactor1(), mm.GetFactor2()));  // 存原色因子

      for (int j = 0; j < mm.MeshCount; j++) {
          Mesh mesh = mm.GetMeshAtIndex(j);
          Material orig = mesh.GetMaterial();

          // ③ 找到 self_illumination 的 flag 位
          ulong mask = orig.GetShader()?.GetMaterialShaderFlagMask("self_illumination", true) ?? 0;
          if (mask == 0) mask = 524288;                    // 兜底常量（Shader 拿不到时）

          // 🔴 ④ 复制材质再开 flag —— 不污染共享材质（同一把剑的所有实例共用一份 Material）
          Material glow = orig.CreateCopy();
          glow.SetShaderFlags(orig.GetShaderFlags() | mask);

          _meshStates.Add(new MeshMaterialState(mesh, orig, glow,
              mesh.GetVectorArgument(), mesh.Color, mesh.Color2));   // 存原状态
          mesh.SetMaterial(glow);
      }
  }
```

### 1.3 每帧施加（亮度随蓄力涨）

```csharp
float t   = Math.Clamp(chargePower / 100f, 0f, 1f);
float hdr = (t >= 1f) ? 300f : t * 50f;          // 满蓄力 300，未满按比例封顶 50
Color c   = LightColor;                           // 剑仙蓝 (0.048, 0.220, 1.0)

foreach (var s in _metaMeshStates) { s.MetaMesh.SetFactor1(c.ToUnsignedInteger()); s.MetaMesh.SetFactor2(...); }
foreach (var s in _meshStates) {
    s.Mesh.Color = c; s.Mesh.Color2 = c;
    s.GlowMaterial.SetMeshVectorArgument(c.x, c.y, c.z, hdr);   // 🔴 强度 = VectorArgument.w
    s.Mesh.SetVectorArgument(c.x, c.y, c.z, hdr);               //    网格侧也设一份
}
```

### 1.4 还原（逐项回填，全程 try/catch 单个吞）

```
Mesh.SetMaterial(原材质) · Mesh.Color/Color2 回填 · Mesh.SetVectorArgument(原 w 分量)
MetaMesh.SetFactor1/SetFactor2 回填 · 清列表 · 武器指针/槽位置空
```

### 1.5 四条值得记的点

1. 🔴 **必须 `CreateCopy()`** —— 材质是共享资产，直接改 flag 会让**同一把剑在世界上的所有实例**一起发光。
2. 🔴 **`self_illumination` 的 flag 位不写死**：`Shader.GetMaterialShaderFlagMask("self_illumination", true)` 按名字查，查不到才用常量 `524288`。
3. 🔴 **发光强度不是独立属性，是 `VectorArgument.w`** —— 与[网格贴图动画](骑砍2网格贴图动画_引擎能力与实现.md)里那条"三个特性抢同一个 Vector Argument"同源。
4. **失效兜底**：武器换了（槽位或实体指针变了）、`chargePower <= 0`、agent 不活跃 → 一律 `Restore()`；异常也 `Restore()`（catch 里就是它）。

---

## 二、飞行物全生命周期（召唤 → 定向 → 飞行 → 命中 → 回收）

### 2.1 时间线

```
[蓄力中]  每帧 UpdateHero：读上半身动作通道 → 累积 chargePower（+ 剑身发光 + 蓄力音效）
[松手]    动作 stage: None(0) → AttackRelease(2) 且 actionType == 20
          ├ chargePower >= 50 → state.PrepareRelease()（**挂起**，不是立刻发射）
          └ ResetChargeState(preservePendingRelease: true)
[出剑]    动作 progress > 0.4 且 CanReleaseBeam（挂起标记）
          ├ 实体 = CreateEntityFromMetaMesh(scene, 手持武器, "jianqi_ok")
          ├ direction = agent.GetCurrentActionDirection(1)     ← 左斩/右斩/刺/劈
          ├ new SwordBeamProjectile(实体, 施法者, direction, 主人, 释放档位)
          └ 音效 swordbeam/release ×2 层
[飞行]    每帧：位置 += 速度×dt → SnapToGround → SetGlobalFrame → 光源跟随 → 累积里程
          命中检测：每 0.05 秒一次扫掠判定
[结束]    里程 >= maxDistance 或 存活 >= maxLifetime → Entity.Remove(0) + 光源实体 Remove(0)
```

### 2.2 召唤：**从"手里的武器"造实体**

```csharp
// MbHelpers.CreateEntityFromMetaMesh(Scene scene, MissionWeapon weapon, string metaMeshName)
MetaMesh copy = MetaMesh.GetCopy("jianqi_ok", true, false);      // 按名取网格【副本】
if (copy == null || copy.MeshCount == 0) return null;
GameEntity e = GameEntityExtensions.Instantiate(scene, weapon, false, false);  // 用武器实例化
if (e == null) return null;
e.RemoveAllChildren();          // 清掉自带件
e.ClearOnlyOwnComponents();
e.AddMultiMesh(copy, true);     // 换上剑气网格
return e;
```
⚠️ 网格名 `"jianqi_ok"` 是**写死的常量**（`BeamMetaMeshName`）；取不到就只弹一次 UI 提示，不崩。

### 2.3 定向与缩放

```csharp
_direction = attacker.LookDirection;  _direction.z = 0f;   // 🔴 贴地平飞（忽略俯仰）
_direction.Normalize();                                    // 长度≈0 时退化成 Vec3.Forward

OrientImportedBeam(ref frame, _direction, 攻击方向);        // 见 §四
rotation.ApplyScaleLocal(ref new Vec3(visualScale, visualScale, visualScale, -1f));

// 缩放口径 = 「目标尺寸 ÷ 网格原始包围盒最长边」
entity.RecomputeBoundingBox();
BoundingBox b = entity.GetLocalBoundingBox();
visualScale = requestedSize / max(三轴尺寸);
// 🔴 判定半径 = 缩放后包围盒的【半对角线】
hitRadius = sqrt((hx·visualScale)² + (hy·visualScale)² + (hz·visualScale)²);
//     （包围盒拿不到时才退化成 requestedSize × 0.5）
```

### 2.4 贴地（剑气沿地面滑行）

```csharp
float groundY = scene.GetGroundHeightAtPosition(position, (BodyFlags)544321929);
if (!float.IsNaN(groundY) && !float.IsInfinity(groundY))
    position.z = groundY + clearance;      // clearance ≈ 由网格半高算出的离地量（下限 1.3 m）
```

### 2.5 每帧推进

```csharp
_elapsed += dt;
MatrixFrame f = Entity.GetGlobalFrame();
f.origin = SnapToGround(scene, f.origin + _velocity * dt, _groundClearance);
Entity.SetGlobalFrame(ref f, true);
UpdateLightPosition(GetVisualCenter(f));           // 光源跟着「视觉中心」走（不是原点）
_traveled += 位移长度;
UpdateCollisions(dt);
if (_traveled >= _maxDistance || _elapsed >= _maxLifetime) FinishTravel();
```

### 2.6 命中判定（**核心手法：线段↔胶囊扫掠**）

```csharp
// 每 0.05 秒一次（不是每帧 —— 省；速度 27 m/s 时一步约 1.35 m，够密）
Vec3 prev = _lastCollisionCheckPosition, cur = GetVisualCenter(frame);
_lastCollisionCheckPosition = cur;

foreach (Agent a in Mission.Current.Agents) {
    if (a.IsActive() && a.Health > 0 && CanDamageAgent(a)
        && BeamGeometry.IsAgentWithinSweptBeam(a, prev, cur, _hitRadius)   // ← 扫掠判定
        && _hitAgents.Add(a))                                              // ← 去重：一目标只吃一次
    {
        MakeMissionSound(a, "swordbeam/hit");
        KillAgentCheatFix(a, _attacker, _damage, cur, _direction, _weapon);
    }
}
// 可破坏物同理：GetEntityBoundsCenter(实体) 落在线段半径内 → DamageDestructable(..., 伤害 × 33, ...)
```

```csharp
// BeamGeometry.IsAgentWithinSweptBeam：线段↔胶囊 的平方距离
CapsuleData cap = target.CollisionCapsule;              // 🔴 Agent 的碰撞胶囊（P1/P2/Radius）
float r = beamRadius + Math.Max(0f, cap.Radius);
return DistanceSquaredBetweenSegments(prev, cur, cap.P1, cap.P2) <= r * r;
```
> 🔴 **为什么用线段而不是点**：飞行物每步移动 1 米以上，用"当前点球判定"会**穿透**（这一步在目标身前、下一步在身后）。
> 用「上一步 → 这一步」的线段跟胶囊求距离 = **零成本的连续碰撞**（比射线便宜、比点判定可靠）。

### 2.7 伤害怎么进引擎（手搓 Blow → RegisterBlow）

```csharp
Blow blow = new Blow(attacker.Index);
blow.DamageType      = (DamageTypes)2;              // 斩击
blow.BoneIndex       = victim.Monster.HeadLookDirectionBoneIndex;
blow.BaseMagnitude   = damage;
blow.InflictedDamage = (int)damage;
blow.SwingDirection  = impactDirection;
blow.Direction       = impactDirection;
blow.DamageCalculated = true;                       // 🔴 声明「已算过」→ 引擎不再套伤害模型
blow.WeaponRecord.FillAsMeleeBlow(weapon.Item, weapon.CurrentUsageItem, attackerIdx, mainHandBoneIdx);

AttackCollisionData data = AttackCollisionData.GetAttackCollisionDataForDebugPurpose(
    /* 一串 bool 开关 */ isColliderAgent: true, …, (CombatCollisionResult)1, /*…*/ );
victim.RegisterBlow(blow, ref data);

// 兜底：打完血没掉 → 自己直接扣（ApplyDirectDamageFallback）
```
> 🔴 `AttackCollisionData.GetAttackCollisionDataForDebugPurpose(...)` 是**手搓碰撞数据的官方口子**（名字带 Debug，但是公开可用的）；
> `Agent.RegisterBlow(Blow, ref AttackCollisionData)` 是外部唯一能用的伤害落地入口（`Mission.RegisterBlow` 是 private）。
> **伤害值来源**：优先取**手持武器的 `SwingDamage`**（剑气 = 剑的延伸）；拿不到就用 Hero 双手技能 `RandomInt(skill-10, skill+10)`；再兜底 30。

### 2.8 回收

```csharp
_lightEntity?.Remove(0);
Entity?.Remove(0);
Entity = null;
```

---

## 三、粒子特效：**没有**

全 DLL 搜 `Particle` / `psys_` —— **零命中**。视觉全靠三件：**网格 + 自发光材质 + 点光源**（+ 音效）。
音效 5 条（`ModuleData/module_sounds.xml`，均为自定义 wav）：

| 事件名 | 用途 | 备注 |
|---|---|---|
| `swordbeam/charge` | 蓄力中（循环） | `is_2d="true" sound_category="ui"` |
| `swordbeam/charge_full` | 蓄满 | 同上 |
| `swordbeam/release` | 出剑（`MakeMissionSound(agent, …, 2)` = 叠 2 层） | 同上 |
| `swordbeam/hit` | 命中（挂在被击者身上播） | 位置音 |
| `swordbeam/explosion` | —— | 🔴 **代码从不调用**：是赞助版功能留下的资源 |

> **可借鉴**：不用粒子也能做出"发光的飞行物"——**网格自发光 + 一盏跟随点光源**就很像样，
> 而且**完全不吃 LOD/剔除那套**（我们的法印被这个问题坑了一整天）。

---

## 四、3C 与视觉细节（含"剑气方向怎么控"）

### 4.1 输入侧：用**动作阶段**当触发器，不监听按键

```csharp
ActionStage stage = agent.GetCurrentActionStage(1);      // 通道 1 = 上半身
ActionCodeType type = agent.GetCurrentActionType(1);
// 释放窗口 = 「上一帧 stage None(0) → 这一帧 AttackRelease(2)」且 actionType == 20
// 出剑点   = GetCurrentActionProgress(1) > 0.4   ← 动画进度当"打点时钟"
UsageDirection dir = agent.GetCurrentActionDirection(1); // 左斩/右斩/刺/上劈
```
> 🔴 引擎**没有动画结束回调**（本工程早有结论），这里同样靠**每帧轮询进度**。
> 好处：动作与剑气天然同步（换动作集、改动画时长都不影响）；坏处：只能在动作的特定相位出剑。

### 4.2 方向解算（`OrientImportedBeam`）—— **剑气会跟着你的斩向倾斜**

```csharp
Vec3 side = normalize(cross(travelDirection, Vec3.Up));     // 水平面的侧向

if (是横斩类方向 1/2/3) {
    frame.rotation = new Mat3(-travelDirection, side, Vec3.Up);        // 面朝前、刃在侧面
    if (方向 == 2) RotateAbout(travelDirection, +30°);                 // 🔴 左斩 → 整体倾 30°
    else if (方向 == 3) RotateAbout(travelDirection, -30°);            //    右斩 → 反着倾
} else {                                                                // 纵劈类
    frame.rotation = new Mat3(-travelDirection, Vec3.Up, -side);        // 立起来
}
frame.rotation.RotateAboutAnArbitraryVector(frame.rotation.f, 180°);    // 绕自身前轴翻正
```
> **一句话**：**飞行方向决定剑气的"去向"，攻击动作方向决定它的"姿态（roll）"** —— 玩家左斩，剑气就往左倾。
> 这是很省钱的 3C：不用做多套网格，一套网格 + 两行旋转就做出了"斩击感"。

### 4.3 其他视觉/听觉件

| 件 | 做法 |
|---|---|
| **点光源跟随** | `Light.CreatePointLight(Math.Max(3f, 尺寸×3f))` → 颜色 = 剑气色 → `entity.AddLight(light)`；每帧把光源实体摆到**视觉中心**（不是实体原点） |
| **贴地滑行** | 每帧 `SnapToGround`（§2.4）—— 剑气沿地形起伏走，不是直线穿地 |
| **UI 层音效** | 蓄力/蓄满/出剑用 `is_2d="ui"`（不衰减）；命中用位置音 |
| **手感提示** | 出剑音叠 2 层；命中音挂被击者 |
| **相机 / 慢动作 / 屏幕特效** | ❌ **全无** —— 这条路上它没做（我们可以补：命中顿帧、屏幕震动、慢动作） |
| **UI 提示** | 网格没加载时只弹一次 `InformationManager.DisplayMessage`（防刷屏范本） |

---

## 五、可复用 API 速查（本工程直接抄的清单）

### 5.1 武器视觉（给剑身加特效）

| 用途 | API |
|---|---|
| 手持武器槽位 | `Agent.GetPrimaryWieldedItemIndex()` |
| **武器的视觉实体** | `Agent.GetWeaponEntityFromEquipmentSlot(EquipmentIndex)` → `WeakGameEntity` |
| 实体 + 子件遍历 | `WeakGameEntity.GetChildrenRecursive(ref List<WeakGameEntity>)` |
| 网格遍历 | `entity.MultiMeshComponentCount` → `GetMetaMesh(i)` → `MetaMesh.MeshCount` → `GetMeshAtIndex(j)` |
| 材质 | `Mesh.GetMaterial()` / `SetMaterial()` / `Material.CreateCopy()` / `GetShaderFlags()` / `SetShaderFlags(ulong)` |
| **按名取 shader flag 位** | `Material.GetShader().GetMaterialShaderFlagMask("self_illumination", true)` |
| **自发光强度** | `Mesh.SetVectorArgument(x, y, z, w)` / `Material.SetMeshVectorArgument(...)`（w = 强度） |
| 颜色因子 | `MetaMesh.SetFactor1/2(uint)` / `Mesh.Color` / `Color2` |

### 5.2 飞行物本体

| 用途 | API |
|---|---|
| 取网格副本 | `MetaMesh.GetCopy(名, showErrors, mayReturnNull)` |
| 造实体 | `GameEntityExtensions.Instantiate(scene, weapon, …)` 或 `GameEntity.CreateEmpty(scene, …)` + `AddMultiMesh(mesh, true)` |
| 缩放 | `Mat3.ApplyScaleLocal(ref Vec3)` |
| 包围盒 | `entity.RecomputeBoundingBox()` / `GetLocalBoundingBox()` / `GetGlobalBoundingBox()` |
| **贴地** | `Scene.GetGroundHeightAtPosition(pos, BodyFlags)` |
| 光源 | `Light.CreatePointLight(r)` + `entity.AddLight(light)` / `entity.GetLight()` |
| 回收 | `GameEntity.Remove(int)` |

### 5.3 判定与结算

| 用途 | API |
|---|---|
| **Agent 碰撞胶囊** | `Agent.CollisionCapsule` → `CapsuleData { P1, P2, Radius }` |
| 线段↔胶囊距离 | 自写 `DistanceSquaredBetweenSegments`（`BeamGeometry` 里有完整实现可直接搬） |
| **手搓伤害** | `new Blow(attackerIndex)` + 字段 + `DamageCalculated = true` + `WeaponRecord.FillAsMeleeBlow(...)` |
| **手搓碰撞数据** | `AttackCollisionData.GetAttackCollisionDataForDebugPurpose(...)` |
| **落地** | `Agent.RegisterBlow(Blow, ref AttackCollisionData)` |
| 可破坏物 | `DestructableComponent.IsDestroyed` + 自己算伤害 |
| 音效 | `SoundEvent.CreateEvent(id, scene)` / `Mission.MakeSound(id, pos, …)` |

### 5.4 输入/状态门禁

| 用途 | API |
|---|---|
| 动作阶段（打点） | `Agent.GetCurrentActionStage/Progress/Type/Direction(通道号)` |
| 只服务玩家 | `agent.IsMainAgent` |
| 近战判定 | `MissionWeapon.CurrentUsageItem.IsMeleeWeapon && !IsRangedWeapon` |
| **防菜单吃输入** | `MBCommon.IsPaused` + `ScreenManager.FocusedLayer == missionScreen.SceneLayer` |
| 模块是否真被加载 | `AppDomain.CurrentDomain.GetAssemblies()` 按名找（**不是** `ModuleHelper.GetModuleInfo`，那个只查目录） |

---

## 六、对我们（法印工程）的落地清单

| # | 事项 | 结论 | 出处 |
|---|---|---|---|
| 1 | **飞行物实体** | ✅ **直接照抄** §2.2 三段（取副本 → 造实体 → 挂网格）；我们把 `metaMeshName` 换成 `lwn_yinmo_crescent` | §2.2 |
| 2 | **推进/贴地/回收** | ✅ 直接照抄 §2.4/2.5/2.8；贴地可以先关（法术不一定贴地飞） | §2.4-2.8 |
| 3 | **命中判定** | ✅ 照抄「线段↔胶囊 + 每 0.05 秒 + HashSet 去重」；**比我们现在用引擎碰撞可靠得多** —— ⚠️ **2026-09-23 追加修正**：骑砍其实有原生查询可替代这套手写（`Mission.RayCastForClosestAgent` 带 `rayThickness`、`Scene.RayCastForClosestEntityOrTerrain`、`Scene.BoxCast`），**优先用原生、手写这套留作兜底**；详见 [法术体系-通用施法框架](../plans/法术体系-通用施法框架.md) §4.3（含三个待实测点） | §2.6 |
| 4 | **伤害落地** | ✅ 照抄「手搓 Blow + `RegisterBlow`」；伤害值建议也取**武器 SwingDamage**（法器面板伤害） | §2.7 |
| 5 | **剑身自发光** | ✅ 照抄 `WeaponChargeGlow` 三段式 —— **正好补上我们"蓄力期手上发光"的空白**（而且不污染共享材质） | §一 |
| 6 | **方向控制** | 🟡 我们已有"跟随视线"；**可加**它那套「横斩/纵劈 + 左右各倾 30°」的姿态倾斜（省钱又出效果） | §4.2 |
| 7 | **触发时机** | 🟡 我们用武器/弩的引擎流程（更好，天然有抬起→瞄准→释放）；它这套"动作阶段跳变"是**没有武器流程时**的替代品 | §4.1 |
| 8 | **粒子** | 🟡 它零粒子也能看；但我们的拖尾/爆散粒子已有管线，**该用还是用** —— 只是记住：**粒子不受网格剔除影响**，别拿它当"网格还在"的证据 | §三 |
| 9 | **相机/顿帧/震动** | 🔴 它完全没做 —— **这是我们的加分位**（KCD2 水准要求下，命中反馈值得做） | §4.3 |
| 10 | **点光源** | ✅ 可抄；注意 `Light.CreatePointLight` 在本工程尚无先例，**先小范围试** | §4.3 |

---

## 七、边界与已知限制（别照抄错）

1. 🔴 **只服务玩家**（`agent.IsMainAgent`）—— 它没有 NPC 施法者路径；我们要做 NPC 平权（铁律 18）得自己加。
2. 🔴 **网格名写死** `"jianqi_ok"` —— 内容包换名要同步改；取不到网格只提示不崩（可以学它的"只提示一次"）。
3. 🔴 **`XXexpand*` 闸门**：引擎启用列表里若有该前缀模块，mod 自我停用（防与赞助扩展冲突）。
4. 🔴 **赞助版边界**：速度/尺寸/距离/光强/颜色档在免费版里是**写死的常量**；`swordbeam/explosion` 音效**有资源无调用**。
   本工程若做补丁解锁仅供**自用与诊断**，不进对外发布内容（见 `Core/SwordBeamRangePatch.cs`）。
5. ⚠️ **`CreateCopy()` 出来的材质不会自动回收** —— 它靠 `Restore()` 把原材质换回去；如果 capture/restore 不配对，会逐次泄漏材质实例。
6. ⚠️ **命中检测每 0.05 秒**：速度很快的飞行物一步跨度大，判定半径要够（它用"缩放后包围盒半对角线"就是为了这个）。
