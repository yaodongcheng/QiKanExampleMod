# FlexibleCombatSystem（UE4.27）施法体系 —— 实现分析

> **为什么写这份**：这是目前能找到的**唯一一个已落地的完整施法系统**（不是文档、不是构想）。
> 法印工程的 [法术体系-通用施法框架](../plans/法术体系-通用施法框架.md) 在做「法术 = 四段拼装」的架构，本工程用**实际代码**给出了每个族的字段清单与生命周期，是逐条对照/取经的对象。
> **素材**：`D:/UEProjects/【UE5】FlexibleCombatSystem`（蓝图工程，无 C++，逻辑全在 `.uasset`），2026-09-23 用字符串挖掘法提取。
> **读法**：§二 是总设计（一行看懂），§三 是数据块，§四 是逐族实现，§六 是"我们能抄什么"。
> ⚠️ **精度声明**：变量类型多为按名/按调用点推断（uasset 的 FName 表按字母序存放，无法可靠配对类型标签）；标 `?` 的是推断值。

---

## 零、一分钟速览

| 问题 | 它的答案 |
|---|---|
| 一个法术是什么？ | **数据表 `DT_SpellsInfo` 里的一行**，行里填一个**共享头 + 若干"族块"之一** |
| 一行里有什么？ | 名字/射程/伤害/冷却/图标/动画/元素/伤害类型/挂点/音效 + **投射物块、引导块、放置块、持续块、召唤武器块** |
| 谁决定"是什么族"？ | **哪个块填了**；落地的逻辑类是行里的 `SpellParentClass` |
| 有几个族？ | **13 个族进表**（另有 3 个族内部次级类不直接进表） |
| 施法方式怎么表达？ | 独立的一轴：`E_SpellCastType` = **Normal / Channel / Instant** |
| 施法生命周期？ | `LoadSpell → BeginMagicCharge（起蓄）→ [吟唱] → TriggerMagicSpell（触发）→ SendMagicSpell（落地）→ StopMagicCharge / DestroySpell`；打断 = `CastFailed` + 蒙太奇 `OnInterrupted` |
| 法术怎么触发？ | 🔴 **动画通知** `AN_TriggerMagicAbility`（挂在出手帧那一拍），**代码不管时机** |
| 资源？ | `ManaCost` + `GoldCost` 两种；`E_CharacterResource` = **Mana / Stamina** |
| 音效几段？ | 🔴 **四段**：`SpellCharge` / `SpellFire` / `SpellHit` / `SpellLoop` |
| 粒子几段？ | 🔴 **三段 × 每法术**：`NS_<法术>Charge` / `...Hit` / `...Loop`，外加每族的通用件（如 `NS_PlacementCircle`） |
| 手势？ | **按投送方式分**：`M_Projectile`（投掷）/ `M_SkyfallSpell`（举天）/ `M_EruptionSpell`（下压）/ `M_MagicAttack_RH_Up` / `M_SpellChannel1·2`（引导两套） |
| 有暴击？ | 有 —— `CalculateMagicDamage` 输出 `Spell_Damage` + **`Critical_Hit`** |
| AI 会几类？ | **7 类**：`Projectile / Channel / SkyChannel / PlacementSpell / Barrier / Heal / Teleport` |
| UI？ | 法术书 `WB_Spellbook` · 快捷栏 `WB_SpellHotbar`（带冷却）· 施法准星 `WB_MagicCrosshair` · 增益剩余时间条 `WB_BuffDurationBar` |

### 全法术清单（`DT_SpellsInfo` 的实际行，命名规约 = `族-法术名`）

| 族前缀 | 实例 |
|---|---|
| `Projectile-` | Fireball · Frostbolt · Lightningbolt · MadnessBolt · Poisonbolt（各 Rank1~3） |
| `Channel-` | ChainLightning · DrainLife · Flamethrower · IcyWinds · PoisonSpray |
| `ChannelPlace-` | AcidRain · Blizzard · RainOfFire |
| `MoveChannel-` | FireStorm · IceStorm |
| `PlaceDmg-` | IceSpikes · LightningStrike · Meteor |
| `PlaceTick-` | FirePit · PoisonEruption |
| `Place-` | MadnessZone |
| `MagicWep-` | 2H · Bow · Dagger · Sword |
| `Rune-` | Fire · Ice · Lightning · Madness · Poison |
| `Barrier-` | Energy · Fire · Ice · Lightning · Poison |
| `Buff-` | Agility · Armor · Crit · Intellect · Strength |
| `Heal-` | Once · OverTime · Channel |

> 🔴 **命名规约值得直接抄**：**族名当行名前缀**。配表时一眼看出是什么族，也防止把 A 族的字段填进 B 族。

---

## 一、挖掘方法（给后来者）

蓝图工程的逻辑在 `.uasset` 二进制里，但**名字表是可读的**。字符串提取即可拿到：变量名、自定义事件名、函数名、结构体字段名、枚举项、资产引用名。

- 工具：`Debug/offline/_ue_strings.py`（本项目，一次性探针）——从 `.uasset` 抠可读串；`--all` 连路径一起打。
- 关键信号：**自定义事件** `K2Node_CustomEvent_<名>`；**结构体字段**是 `<字段名>_<序号>_<GUID>` 形式，裸名也在名字表里；**行名**在数据表的名字表里。
- 🔴 **坑一：部分名字被 +1 位移混淆**（凯撒位移）。例如 `Cmvfqsjou` = `Blueprint`、`Dibsbdufs` = `Character`、`BcjmjuzIpmefs` = `AbilityHolder`。
  **还原规则 = 每个字母往前退一位**。混淆对象主要是**引擎类型名与控件名**；玩法名（`Fireball` / `BeginMagicCharge` / `SpellSpeed`）多数是明文。
- ⚠️ **坑二：字段类型不可靠**。FName 表按字母序存放，`FloatProperty` / `BoolProperty` 这类类型标签**无法与具体字段配对**。所以本文的类型标注是**按名与调用点推断**的，不是读出来的。

---

## 二、总设计：共享头 + 族块（一行一个法术）

```
S_SpellInfo（数据表 DT_SpellsInfo 的行结构，字段序 = MakeStruct 引脚号）
├── 共享头
│      Name(31) GoldCost(7) Range(11) ManaCost(13) AbilityImage(16) BaseDamage(38)
│      SocketAttachName(44) SpellCategory(67) DamageType(80)
│      SpellDescription(99) SpellRequiresChannel(101) SpellSoundFX(102)
│      SpecialDamageInfo(104) SpellTrainerInfo(97)
│      SpellParentClass(85)  ← 🔴 这一行用哪个能力类
├── 族块（填哪个 = 哪个族）
│      SpellProjectile(74)   ← 投射物
│      SpellChannel(88)      ← 引导
│      SpellPlacement(79)    ← 放置／区域
│      SpellDurations(81)    ← 持续／增益
│      SpellSpawnWeapon(94)  ← 召唤武器
│      SpellChargePS(84)     ← 蓄力粒子（跨族共用）
```

**一句话**：**共享头管"是什么法术"，族块管"怎么放 / 放完干什么"，`SpellParentClass` 管"跑起来的逻辑"。**

### 2.1 共享头逐字段

| 字段 | 类型 | 含义 |
|---|---|---|
| `Name` | Text | 法术名 |
| `Range` | float | 射程 |
| `BaseDamage` | float | 基础伤害 |
| `GoldCost` / `ManaCost` | float | **两种资源消耗**（钱 / 蓝） |
| `AbilityImage` | Texture2D | 图标 |
| `SocketAttachName` | Name | 🔴 **挂点**：粒子/音效挂在角色骨骼的哪个 socket 上 |
| `SpellCategory` | E_SpellCategory | **元素**：Fire / Frost / Poison / Electric / Madness / Arcane / General |
| `DamageType` | E_DamageType | **伤害类型**：Physical / Fire / Frost / Electric / Poison / Madness / Magic / Energy / Polymorph |
| `SpellDescription` | S_ItemDescription | 描述文本 |
| `SpellRequiresChannel` | bool | 是否需要引导 |
| `SpecialDamageInfo` | S_SpecialDamageInfo | 命中后附加的特殊效果块（§3.4） |
| `SpellSoundFX` | S_SpellSounds | 四段音效（§3.5） |
| `SpellChargePS` | NiagaraSystem | **蓄力粒子（跨族共用 —— 所有族的蓄力表现走这一个字段）** |
| `SpellParentClass` | class | 🔴 逻辑类 |
| `SpellTrainerInfo` | S_SpellTrainerInfo | 找谁学（单机 RPG 层，与我们无关） |

> ⚠️ `S_SpellInfo` 里**没有"冷却"字段**（初版分析曾据 `Cooldown` 字样推断有，实为 `MagicComponent.SetupCooldown/BeginCooldown/EndCooldown` 那套**组件级**机制）。冷却由 `MagicComponent` 统一管，不挂在法术行上。

### 2.2 施法方式：独立的一轴

```
E_SpellCastType = Normal | Channel | Instant
```

- **Normal** —— 有前摇，放一发
- **Channel** —— 按住持续（配合 `SpellRequiresChannel` 与 `ReleaseSpellChannel`）
- **Instant** —— 瞬发，无前摇

🔴 **它与"族"正交**：投射物可以是 Normal 也可以是 Instant；引导族本身就是 Channel。
**对我们的意义**：这条轴 = 我们模型里的**起手轴**，而且比我们的二分（蓄力 / 引导）多出"瞬发"这一档。

---

## 三、数据块（族块）逐字段

### 3.1 投射物（`S_SpellProjectile`）

| 字段 | 类型 | 含义 |
|---|---|---|
| `SpellSpeed` | float | 飞行速度 |
| `SpellHitPS` | NiagaraSystem | 命中粒子 |

> 只有两个 —— 因为**发射解算与命中判定都在能力类里**（`CalculateSpellVelocity` / 碰撞事件）。
> 但能力类上另有一组**投射物专属变量**（不在这块里）：`SpellVelocity` · `ProjectileGravityScale` · **`bIsHomingProjectile`** · **`SnapToTarget`**。
> 🔴 **它有追踪弹**（`bIsHomingProjectile` + `SnapToTarget`）—— 印证我们《通用施法框架》§3.3 把"追踪"从"不做"翻成"一个参数"是对的。

### 3.2 引导（`S_SpellChannel`）

| 字段 | 类型 | 含义 |
|---|---|---|
| `SpellChannelPS` | NiagaraSystem | 引导粒子（持续照着的那条线/那股流） |

> 引导的手感参数在能力类上：`ChannelTickTime`（每跳间隔）· `SpellChannelDmgAdjust`（**伤害随引导时长变化**）· `MaxRange`。

### 3.3 放置／区域（`S_SpellPlacement`）—— 🔴 最重要的一块

| 字段 | 类型 | 含义 |
|---|---|---|
| `SpellPlacement` | ? | 放置物本身 |
| `SpellPS` | NiagaraSystem | 放置粒子 |
| `SpellRadius` | float | **作用半径** |
| 🔴 `TickFrequency` | float | **重复结算的间隔** |
| 🔴 `TickDuration` | float | **持续多久** |

> 🔴 **这就是"持续型法术"的完整参数**：半径 + 多久算一次 + 算多久。
> 对照我们的模型：这正是**投送层负责节流、结算层只认单次命中**（计划 §3.2 契约 3）。
> 它靠**拆成两个能力类**表达（`AreaAttackSingle` 一次性 / `AreaAttackTick` 按间隔），我们用一个"重复间隔"参数即可。

### 3.4 持续／增益（`S_DurationSpells`）

| 字段 | 类型 | 含义 |
|---|---|---|
| `SpellAmount` | float | 数值 |
| `SpellDuration` | float | 时长 |
| `SpellDurationType` | E_SpellDurationType | **加在什么上**：Strength / Agility / Intellect / **Barrier** / **Armor** / **Heal** |
| `SpellPS` | NiagaraSystem | 表现 |
| `BarrierReflectDamage` | bool/float | 屏障反弹伤害 |

> 六种作用对象 = 三属性 + 屏障 + 护甲 + 治疗。**屏障是一等公民**（有独立的反弹参数与按元素的屏障粒子 `NS_<元素>_Barrier`）。

### 3.5 音效（`S_SpellSounds`）—— 四段

`SpellCharge`（蓄力）· `SpellFire`（发射）· `SpellHit`（命中）· `SpellLoop`（飞行/持续循环）

> 🔴 与《通用施法框架》计划里列的四条音效**完全一致**。我们的阴魔斩粒子是 charge / burst / trail 三段，**缺一个"发射"音**。

### 3.6 召唤武器（`S_SpawnWeaponInfo`）

`DataTableRowHandle`（指向物品表 `DT_ItemDataInfo` 取一件物品）+ `E_SpawnWeaponType`（= `Daggers`）+ `S_WeaponVFXInfo`。

### 3.7 武器特效（`S_WeaponVFXInfo`）

`WeaponVFX`(Niagara) + `Size` + `Height` + `Radius` + `SpawnRate` —— 给手上的武器挂持续特效（附魔）。

### 3.8 特殊伤害（`S_SpecialDamageInfo` 及同族）

分化七种：`S_SpecialFire` / `Frost` / `Ice` / `Poison` / `Electric` / `Madness` / `Polymorph`。
**这是"命中之后附加什么"的块**（燃烧 / 减速 / 中毒 / 麻痹 / 疯狂 / 变形），对应我们模型里的**结算轴**。

---

## 四、能力类（逻辑层）

### 4.1 继承树

```
BP_AbilityParent                     ← 基类：生命周期 + 蓄力 + 命中判定 + 资源 + 相机
├── BP_ProjectileParent              ← 投射物  → BP_Polymorph（变形 = 投射物命中后换模型）
├── BP_ChannelParent                 ← 引导（地面/自身）
│   ├── BP_ChannelSkyParent          ← 引导（天上，配合放置块打地面区域）
│   └── BP_HealChannelParent         ← 治疗引导（🔴 继承引导族，只把"每跳伤害"换成"每跳治疗"）
├── BP_SpellPlacementParent          ← 放置族基类（空壳：只负责摆放 / 跟随 / 移除）
│   ├── BP_AreaAttackSingleParent    ← 区域伤害 · 只打一次
│   ├── BP_AreaAttackTickParent      ← 区域伤害 · 按 TickFrequency 反复打
│   ├── BP_RuneParent → BP_PhysicalRune   ← 符文（地上待触发的陷阱，五种元素，可存档）
│   ├── BP_Teleport                  ← 传送
│   └── BP_NSTriggerSpellAttackParent ← 用 Niagara 粒子回调驱动伤害
├── BP_MovingSpellParent → BP_MovingDamagingSpell  ← 飞行法术：飞行段与落地段是两个 actor
├── BP_SelfCastSpell                 ← Buff / Barrier / Heal 的公共"自施法"父类
│   ├── BP_BuffParent                ← 增益 / 护盾
│   └── BP_HealSingleParent          ← 单次治疗
└── BP_SpawnMagicItemParent          ← 召唤魔法武器
```

> 🔴 **进表的只有 13 个族**；`MovingDamagingSpell` / `PhysicalRune` / `SelfCastSpell` 是族内部次级类，不直接进数据表。

### 4.2 基类 `BP_AbilityParent` 的关键名字（= 我们"起手轴"的完整内容）

| 名字 | 干什么 |
|---|---|
| `LoadSpell` / `BeginSpell` / `DestroySpell` | 装载 / 开始 / 销毁 |
| 🔴 `BeginMagicCharge` / `StopMagicCharge` | **蓄力开始 / 蓄力结束** |
| 🔴 `TriggerMagicSpell` | **触发**（由动画通知在出手帧打进来） |
| 🔴 `SendMagicSpell` | **落地**（真正生成法术实体 / 挂上效果） |
| `ReleaseSpellChannel` | 松手结束引导 |
| `AttemptSpellAgain` | 打断后重试 |
| `CalculateSpellVelocity` → `BlueprintSuggestProjectileVelocity` | 🔴 **弹道解算**：给起点+终点+速度，解出初速向量；配 `bFavorHighArc`（偏好高弧线）、`LaunchSpeed`、`OverrideGravityZ` |
| `SpellPlacementLocation` / `UpdateSpellPlacement` / `RemovePlacementMarker` | 🔴 **落点标记**的取点 / 每帧跟随刷新 / 移除 |
| `LineTraceSingle` + `BreakHitResult` / `MakeHitResult` | 命中用**射线**（可配 `TraceChannel` / `bTraceComplex` / 调试绘制颜色） |
| `ConsumeInputBuffer` / `ClearBuffer` | **输入缓冲**（连招预输入） |
| `Stunned` | **打断** |
| `ReduceMana` | 扣蓝 |
| `AbilityCollision`（SphereComponent + `SphereRadius`） | 能力自带的判定球 |
| `ShieldDamageAmount` | 对盾牌的伤害 |
| `bIgnoreSelf` / `ActorsToIgnore` | 忽略自身与友军 |

### 4.3 逐族实现要点

| 族 | 生命周期 | 独有参数 | 怎么判命中 | 怎么结算 |
|---|---|---|---|---|
| **投射物** | 起蓄 → 出手生成弹丸 → **引擎物理推进（自己不 Tick）** → 销毁 | `SpellVelocity` `ProjectileGravityScale` `bIsHomingProjectile` `SnapToTarget` | 🔴 **物理碰撞事件** `OnComponentHit`（+ `OnComponentBeginOverlap` 兜底） | `CalculateMagicDamage` → `ApplyPointDamage`；命中特效 `SpawnSystemAtLocation` + `SpawnSoundAttached` |
| **引导** | 起蓄 → 触发 → `ActivateSpellTick`（**定时器**）→ `ReleaseSpellChannel` 收 | `ChannelTickTime` `SpellChannelDmgAdjust`（伤害随引导时长涨）`MaxRange` `SpellChannelNo` | **球扫 `SphereTraceMultiForObjects`** 逐目标 | 玩家/ AI 两套：`ApplyTraceDamage` / `ApplyAITraceDamage` |
| **天降引导** | 同上，但打的是**地面一块区域**；三段蒙太奇 Start/Mid/End | `DamageChannel` `SpellPlacementTimer` `LevelRequirement` `FadeOutDuration/FadeCurve`（循环音淡出） | 球扫 | `ApplyPointDamage`（不分玩家/AI） |
| **放置（基类）** | 摆 → `UpdateSpellPlacement` **每 Tick 跟随刷新落点** → `RemovePlacementMarker` | 🔴 `InitialStartDelay` + **`InitialStartDelayVariance`**（首次生效延迟 + 随机抖动）· `bLooping` · `SpellPlacementTimer` | 自身只收 `ActorBeginOverlap`（真伤害交子类） | **无**（空壳） |
| **区域·一次性** | 触发 → 定时器只跳一次 → 结束 | `SpellEffect` `TargetArray` | 球扫（`SphereRadius` + `Radius`） | `CalculateMagicDamage` → 逐目标 `ApplyPointDamage` |
| **区域·按间隔** | 触发 → **循环定时器**（`bLooping`，`Delay`/`Duration`）→ 每跳 `SendSpellTick` | `TickFrequency` / `TickDuration`（来自放置块）+ `InitialStartDelay(+Variance)` | 球扫 | `CalculateMagicDamage` → `ApplyPointDamage` + `PassDamageInfo` |
| **移动法术** | 起蓄 → 生成**会飞的施法体** → **到点转成伤害法术**（另一个 actor）→ 收 | `MoveSpeed` `SpellStart`（蒙太奇段/通知名）`SpawnTransform` | 自身不判，交飞行伤害体 | 无（转交） |
| **飞行伤害体** | 双驱动：`ReceiveTick` + `ActivateSpellTick` 周期定时器 | `MoveSpeed` `DamageEnemy` `TargetArray` | 球扫 | `CalculateMagicDamage` → `ApplyPointDamage` + `PassDamageInfo` |
| **自施法（基类）** | 起蓄 → 触发 → **把法术挂到自身** → 收 | `SpellEffect` `SpellPlacementTimer` | 无（目标是自己） | 走 `MagicComponent` 挂 Buff/Heal |
| **增益 / 护盾** | `ActivateBuff` → 计时（`SpellDuration`）→ 到期移除；UI 用 `WB_BuffDurationBar` | `BuffType` `BuffAmount` `BuffDuration` `BarrierHitDamage` `BarrierDamageType` `BarrierReflectDamage` | 无 | 护盾反弹走 `ApplyPointDamage`；扣蓝 `ReduceMana` |
| **单次治疗** | 进入 → `SpawnSystemAttached` 播特效 → `Delay`/`Duration` 结算 → `UpdateHealthDispatch` 广播 | `Target` `Health/MaxHealth` `E_CombatTextType`（治疗飘字） | 不做射线，用上层传入的 `Target` | 改 `Health` + 广播（**不走伤害链**） |
| **引导治疗** | 🔴 **完全复用引导族的流程**，只把每跳伤害换成每跳治疗 | `NiagaraLeftHand` | 继承父类球扫 | 改 `Health` + 广播 + 飘字（不调 `CalculateMagicDamage`） |
| **符文** | 放置 → `BP_PhysicalRune` 落地 → **踩中触发** → 销毁；可存档 | `RuneOwner` `RuneLocation` `FireArrowBurstDamage` `RadialForceComponent` `S_RuneSaveInfo` | 🔴 **碰撞重叠**触发，再球扫补一次范围判定 | `CalculateMagicDamage` → `ApplyPointDamage` |
| **传送** | 蓄满 → `FindTeleportLocation` → 位移 → 起点/落点特效 | `TeleportLocation` `EnemyTarget` `MaxRange` `bTeleportPhysics` `Radius` | 🔴 **EQS 查询** `RunEQSQuery(EQS_AroundTarget)` 取候选点 → `SphereTraceSingle` 验证可站立 | 无伤害 |
| **召唤武器** | 起蓄 → 生成/装备（可连发多件） | `ItemID` `ItemType` `WeaponDamage` `Spawns` `SpawnRate` `WeaponVFXInfo` | 无 | 武器伤害走武器系统，非法术链 |
| **变形** | 🔴 **继承投射物**：弹道命中 → 换成羊模型 → 维持 → 还原 | `SK_Sheep` `RelativeScale3D` | 弹丸碰撞重叠 | 不改血量，套 `E_DamageType::Polymorph` 状态 |
| **Niagara 驱动** | 触发 → 起粒子 → **粒子回调**驱动结算 | `NiagaraParticleCallbackHandler` `SetNiagaraVariableObject` | 🔴 **Niagara 粒子碰撞回调** + 球扫补判 | `CalculateMagicDamage` → `ApplyPointDamage` |

### 4.4 🔴 命中判定方式的家族规律（对我们最有用的一条）

| 族类 | 判定方式 |
|---|---|
| **投射物 / 变形 / 符文** | **物理碰撞事件**（`OnComponentHit` / `OnComponentBeginOverlap`） |
| **引导 / 天降 / 区域 / 飞行伤害体** | **多体球扫** `SphereTraceMultiForObjects` |
| **传送** | **EQS 查询** + 单球扫验证 |
| **Niagara 驱动** | **粒子碰撞回调** + 球扫补判 |

> 🔴 **为什么这条对我们最有用**：它的投射物靠**物理引擎的碰撞事件**，因为 UE 的弹丸**有真实碰撞体**。
> **骑砍的自管实体没有物理体** —— 所以我们必须用「**线段↔胶囊扫掠 + 世界射线**」自己判（计划 §4.3）。
> 这不是我们绕远路，是**引擎能力不同导致的必然替代**；它那套球扫思路（选敌）与我们一致。

### 4.5 两条值得注意的复用范例

1. **`BP_HealChannelParent` 继承 `BP_ChannelParent`** —— 治疗引导 = 引导族，只把"每跳伤害"换成"每跳治疗"。
   → 印证我们"三轴复用、不重写"的思路：**同一套流程换个结算即可**。
2. **`BP_Polymorph` 继承 `BP_ProjectileParent`** —— 变形术 = 投射物 + 换个结算。
   → 同理，**投送复用、结算换掉**。

---

## 五、触发与 AI

### 5.1 动画通知（`AN_*`）

| 通知 | 逻辑 | 挂在哪 |
|---|---|---|
| 🔴 `AN_TriggerMagicAbility` | 只有一句：`Received_Notify` → `GetOwner` → `GetComponentByClass(MagicComponent)` → **`TriggerMagicSpell`** | **所有出手帧**：`M_MagicAttack_RH_Up` · `M_Projectile` · `M_EruptionSpell` · `M_SkyfallSpell` · 引导首段 `M_SpellChannel1_Start` / `M_SpellChannel2Start` / `SpellChannelIntro` |
| `AN_TriggerNextAbility` | → `MagicComponent.NextSpell`（接下一发 / 连招） | **收尾帧**：`M_SpellChannel1_End` / `M_SpellChannel2End` 及各出手动画末尾 |
| `AN_InputBufferSwitch` | 切换输入缓冲 | 近战 / 弓箭 / 受击动画 |

> 🔴 **设计要点：代码不管"何时放出法术"，时机完全交给动画**（通知挂在出手帧那一拍）。
> **对我们的意义**：骑砍**没有动画通知**，只能每帧读 `GetCurrentActionProgress` 轮询 —— 本工程早有此结论（`Knowledge/自定义战斗.md` §3「进度阈值打点表」就是它的等价物）。
> 它的 `AN_TriggerNextAbility`（收尾帧接下一发）对应我们的**连招/预输入**，阶段 3 的输入缓冲要做到。

### 5.2 `MagicComponent`（施法总控 = 我们的 `SpellCastFlow`）

关键接口：`AddSpell` / `SelectSpell` / `BeginSpell` / **`CanCast` / `CastFailed`** / `BeginMagicCharge` / `ReleaseMagicCharge` / `TriggerMagicSpell` / `SendMagicSpell` / `SpawnSpell` / `NextSpell` / `ReduceMana` / **`SetupCooldown` / `BeginCooldown` / `EndCooldown`** / `SetupAISpellCD` / `SpawnRunes`（读档重建） / `LoadDefaultSpells` / `InitializeSpellbook` / **`CalculateMagicDamage`**。
事件：`EventSpellAiming` / `EventSpellChanneling` / `EventSpellChannelNumber`。
UI：`WB_Spellbook` / `WB_SpellHotbar` / `WB_BuffDurationBar`。

> 🔴 **冷却与法术书是"组件级"的，不是"法术行级"的** —— 它不在 `S_SpellInfo` 里放 `Cooldown` 字段。
> 对我们的意义：我们的"弹药即法术"取法天然不需要法术书；**冷却属于阶段 5 的技能槽，不进数据行**。

### 5.3 AI 施法完整顺序（行为树 `BT_MixedCombat`）

黑板键：`SpellSelected` · `SpellCharged` · `SpellCastType` · `HasMana` · `TeleportOnCD` · `EnemyTarget` · `TargetLocation` · `StrafeLeft` · `StrafeLocation` · `AI_Behaviour`

```
① D_ShouldTeleport（装饰器）
     若该传送：先 ReleaseMagicCharge + ClearBuffer（🔴 放弃当前吟唱）→ 走传送
② T_DetermineMagicSpell（选法术）
     HasMana 检查 → 按 E_AISpellOptions 分支（7 类）→ 带权随机（RandomBoolWithWeight）
     → 读 MageType / WizardType / SpellType / SpellCategory / Cooldown → 写黑板
③ T_ChargeSpell（吟唱）
     🔴 IsPlayingMontage 防重入 → MagicComponent.BeginMagicCharge（失败走 CastFailed）
     → 播蓄力蒙太奇 → 置 SpellCharged
④ T_FireSpell / T_FireChannelSpell（发）
     校验 SpellCharged && SpellSelected → MagicComponent.SendMagicSpell
     🔴 同时 ToggleStrafe / StrafeDirection 让 AI 走位（施法不等于站着不动）
     实际法术在蒙太奇出手帧由 AN_TriggerMagicAbility → TriggerMagicSpell 打出
⑤ T_ReleaseSpell（收招 / 放弃）
     清黑板 → ReleaseMagicCharge → InputBufferComponent.ClearBuffer
```

**玩家侧同构**：输入 → `BeginMagicCharge` → 通知 `AN_TriggerMagicAbility` → `TriggerMagicSpell` → `SendMagicSpell`；再一帧 `AN_TriggerNextAbility` → `NextSpell` 衔接下一发。

> 🔴 **两条可直接抄的纪律**：① **`IsPlayingMontage` 防重入**（否则连续触发同一法术）② **施法期间照常走位**（不是定身站桩）。

---

## 六、我们能抄什么（对照法印工程）

| # | 它的做法 | 要不要 | 备注 |
|---|---|---|---|
| 1 | **共享头 + 族块，一行一个法术** | ✅ **抄** | 我们在四段之上加"法术族预设"，见计划 §2.4 |
| 2 | **族名当行名前缀**（`Projectile-Fireball`） | ✅ **抄** | 配表一眼看出族，防串族 |
| 3 | 族 = 能力类，13 个进表 | 🟡 **抄形状不抄粒度** | 它两个区域族 = 我们一个"重复间隔"参数 |
| 4 | `E_SpellCastType` = Normal / Channel / Instant | ✅ **抄** | 补进起手轴（我们原来只有两档） |
| 5 | `TickFrequency` + `TickDuration` + `SpellRadius` | ✅ **抄** | 就是持续型法术的三个参数 |
| 6 | 🔴 `InitialStartDelay` + **`InitialStartDelayVariance`** | ✅ **抄** | 落地生效有**随机抖动**，不是齐刷刷同时 —— 手感细节 |
| 7 | 四段音效 Charge / Fire / Hit / Loop | ✅ **抄** | 与我们的口径一致；阴魔斩缺"发射"音 |
| 8 | 粒子三段 `NS_<法术>Charge/Hit/Loop` | ✅ **已在做** | 阴魔斩的 charge / burst / trail 就是这三段 |
| 9 | 手势按投送方式分（投掷 / 举天 / 下压 / 引导） | ✅ **抄** | 我们有 `tools/anim-retarget`（28 条施法动画素材） |
| 10 | `SocketAttachName` 挂点 | ✅ **抄** | 我们对应"挂在武器实体 / 骨骼" |
| 11 | `NS_PlacementCircle` + `UpdateSpellPlacement` / `RemovePlacementMarker` | ✅ **抄** | 落点指示圈是**放置族的必备件**，不是可选打磨 |
| 12 | 🔴 `CalculateSpellVelocity` / `bFavorHighArc` | ✅ **抄思路** | 瞄准轴该产出**"发射解（初速向量）"而不是"方向"** —— 有重力时要打中一个点必须解抛物线 |
| 13 | `bIsHomingProjectile` + `SnapToTarget` | ✅ **抄** | 印证我们把"追踪"从"不做"翻成"一个参数" |
| 14 | `IsPlayingMontage` 防重入 + 施法时走位 | ✅ **抄** | 两条 AI/玩家通用纪律 |
| 15 | 引导伤害随引导时长涨（`SpellChannelDmgAdjust`） | ✅ 抄 | 阶段 3 的手感项 |
| 16 | `E_CharacterAction` 里有 `CastSpell` / `ChargeSpell` | ✅ 参考 | 施法是**角色级动作状态**；骑砍的武器流程天然提供 |
| 17 | 法术书 + 快捷栏 + 冷却 + 施法准星 + 增益剩余时间条 | 🟡 **以后抄** | 形态可借；冷却属组件级、不进法术行 |
| 18 | AI 只用 7 类法术 + 带权随机 + `IsPlayingMontage` 防重入 | ✅ 参考 | 给 NPC 阶段划范围 |
| 19 | `SpecialDamageInfo` 七种特殊伤害 | ✅ 参考 | 结算轴的状态清单 |
| 20 | 放置类法术**要存档**（`S_RuneSaveInfo` + `SpawnRunes` 读档重建） | ⚠️ **我们的问题** | 骑砍走 `SaveableTypeDefiner`；符文/火墙这类"留在世界上的法术"必须存档 |
| 21 | 暴击（`Critical_Hit`） | 🟡 以后 | 结算轴可选参数 |
| 22 | 动画通知触发 `AN_TriggerMagicAbility` | ❌ **抄不了** | 骑砍没有动画通知，只能轮询进度（我们的 `Knowledge/自定义战斗.md` §3 已是等价物） |
| 23 | 投射物用**物理碰撞事件**判命中 | ❌ **不能抄**（但也不用自己全写） | 骑砍**没有碰撞回调**（物理体挂了也收不到"撞到谁"），但**有带粗细的查询 API**：`Mission.RayCastForClosestAgent` + `Scene.RayCastForClosestEntityOrTerrain` + `Scene.BoxCast` —— 手写线段↔胶囊只作兜底 |
| 24 | 传送用 EQS 查询落点 | 🟡 思路可抄 | 骑砍无 EQS；对应物 = `Scene.RayCastForClosestEntityOrTerrain` + 自定义打分 |

---

## 七、边界（别照抄错的地方）

1. 🔴 **它是单机 UE 模板工程，不是骑砍**：命中 / 伤害 / 资源 / 存档全是自己写的；我们复用骑砍的引擎管线（`RegisterBlow` 等）。
2. 🔴 **它的族是"互斥单选"**：一行只能填一个族块。**我们没有这个限制**（四段可自由组合），这是我们比它灵活的地方 —— 但配表时**必须提供族预设**，否则每行填八个轴字段容易填错。
3. 🔴 **投射物靠物理碰撞、我们靠扫掠** —— 这是引擎能力差异，不是做法优劣。
4. ⚠️ **变量类型是推断的**（§一 坑二），引用前请以实际调用点为准。
5. `S_SpellTrainerInfo` / `DT_Trainer-*` / `WB_Spellbook` 是单机 RPG 的"找训练师学法术"玩法，与我们的"弹药即法术"取法不同，只借 UI 形状。
