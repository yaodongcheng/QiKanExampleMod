# FlexibleCombatSystem（UE4.27）施法体系 —— 全量拆解

> **这份文档是什么**：对 UE 商店资产 **FlexibleCombatSystem（FCS）** 里「法术战斗」全部实现的一次**逐字段、逐类、逐帧**拆解，供在骑砍2 里把法术战斗做成 FCS 那个样子。
> **为什么值得拆**：它是目前能找到的**唯一一个已落地的完整施法系统**（不是文档、不是构想），而且**纯数据驱动**——67 个法术全是数据表里的一行，逻辑只有 19 个类。这套形状与我们的《法术体系-通用施法框架》几乎同构，可以逐条对照。
> **证据在哪**：`Debug/offline/fcs_dump/`（原始 T3D 文本 577MB + 结构化摘要 + 每表 CSV），本文所有数字都能在那里面查证。
> **可信度**：本文标注 **✅ 读出** = 从工程里直接导出的原文；**⚠️ 推断** = 由上下文推的。没有第三种。

---

## 零、一分钟速览

| 问题 | FCS 的答案 |
|---|---|
| 一个法术是什么？ | **数据表 `DT_SpellsInfo` 的一行**（67 行） |
| 一行里有什么？ | 22 个字段：名字/耗蓝/射程/伤害/冷却/图标/动画/蓄力粒子/四段音效/描述/是否引导/元素/伤害类型/特殊伤害块/挂点/逻辑类/训练师信息 + **6 个族块之一** |
| 谁决定"这是什么族"？ | 行里的 `SpellParentClass`（指向哪个类），族块只是参数 |
| 有几个族？ | **进表的 14 个逻辑类**（投射 15 行 / 召唤武器 16 行 / 增益护盾 11 行 / 引导 5 / 符文 5 / 区域跳伤 3 / 天降 3 / Niagara 驱动 2 / 移动法术 2 / 区域单次 1 / 传送 1 / 单次治疗 1 / 引导治疗 1 / 变形 1） |
| 施法怎么触发？ | 🔴 **动画通知** `AN_TriggerMagicAbility` 挂在出手帧，代码不管时机 |
| 完整生命周期？ | `Initialize` → `LoadSpell` → `BeginMagicCharge` →（吟唱）→ `TriggerMagicSpell` → `SendMagicSpell` → `StopMagicCharge` / `DestroySpell`；打断走 `Spell-Interupted` |
| 伤害怎么算？ | 🔴 精确公式（✅ 读出）：`(基础伤害 + 智力) × 随机(0.75~1.0) × (暴击?2:1)` |
| 资源？ | 蓝（Mana）+ 耐力（Stamina）+ 金币（训练师）；三种分开 |
| 冷却？ | 🔴 **组件级**，不挂在法术行上（AI 另有一套 CD） |
| 音效？ | **四段**：Charge / Loop / Fire / Hit |
| 粒子？ | **三段 × 每法术**（Charge / Hit / Loop）+ 每族通用件（落点圈、爆炸、屏障）；武器附魔单独一套 |
| 手势？ | 6 条蒙太奇：投掷 / 举天 / 下压 / 举手上 / 引导两套；触发帧已量出（见 §七） |
| AI 会几类法术？ | **7 类**（投射/引导/天降/放置/屏障/治疗/传送），带权重随机 + 分级距离 + 走位 |
| 3C？ | 相机分 5 档（含 MagicCombat）；施法**不站桩**，走位由 AI 服务驱动；瞄准基于**相机相对方向** |
| UI？ | 法术书（分页/分类重购）+ 快捷栏（带冷却）+ 施法准星 + 增益剩余条 + 提示 |

**一句话总结形状**：**「数据行决定是什么，逻辑类决定怎么做，动画通知决定何时放，组件管资源与冷却」**。

---

## 一、这份拆解是怎么做的（方法与可信度）

### 1.1 三层证据（都可复现）

| 层 | 手段 | 拿到了什么 |
|---|---|---|
| ① 资产清单与依赖 | UE 编辑器无头（`UE4Editor-Cmd -run=PythonScript`）+ 资产注册表 | 2695 个资产的路径/类型、互相引用 |
| ② **对象全量文本导出** | 引擎自带 `ObjectExporterT3D` 把每个资产导成 T3D 文本 | 🔴 **蓝图每个节点的函数引用、每个引脚的默认值与连线**；CDO 默认值；动画通知与时间；控件树；结构体字段与类型；材质参数 |
| ③ 数据表取值 | 从二进制挖出用户结构体字段全名（`字段_序号_GUID`）→ 喂给 `DataTableFunctionLibrary.get_data_table_column_as_string` | 🔴 **每张表每一行每一格的值** |

> 🔴 **方法论文档正文 = [tools/ue-dissect/README.md](../tools/ue-dissect/README.md)**（全仓唯一的 UE 资产导出/解析底座：流程、四条军规、已知边界、换工程要改什么）。本节只留结论。
>
> 🔴 **最大的一个发现（方法论层面）**：UE 的 Python 在命令行下**读不到**蓝图变量、节点、枚举值（属性没标 `EditAnywhere`，Python 反射拒绝），但**T3D 文本导出器不受这个限制**——它按"带标签属性"整棵导出。等于**不装任何第三方工具**就拿到了等价于反编译的信息量。以后拆任何 UE 工程都能这么干。

### 1.2 踩过的坑（下次直接绕开）

1. **`unreal.EditorAssetLibrary` 不存在** —— 它属于 EditorScriptingUtilities 插件，本工程没启用。改用 `unreal.load_asset(path)`。
2. **PowerShell 的 `Set-Content -Encoding utf8` 会写 BOM** —— UE 的 Python 执行时首字符变 `﻿`，报 `invalid character in identifier`。写脚本一律 UTF-8 无 BOM。
3. **`Start-Process` 不给含空格的参数加引号** —— 内联传给 `-script=` 的代码被拆散。要么用调用运算符 `&`，要么不做内联。
4. **T3D 文本是"两遍结构"**：先声明对象（`Begin Object Class=X Name=Y` 空块），再按名字重开（`Begin Object Name=Y`）填属性。解析器必须**按名字合并两遍**。
5. 🔴 **蓝图节点的名字是"按图局部"的** —— 同一个包里 `K2Node_CallFunction_0` 出现 22 次（每个图各一个）。**必须按 EdGraph 子树作用域合并**，全局合并会把不同函数的引脚搅在一起（我第一次就栽在这）。
6. **纯函数（无 exec 引脚）不能用"沿执行线走"的方式读** —— 要用"数据流"视图（看每个输入引脚来自哪个节点/什么字面量），伤害公式就是这么读出来的。

### 1.3 产物位置

| 内容 | 路径 |
|---|---|
| 原始 T3D 文本（蓝图/数据表/动画/结构体/枚举/粒子/材质/控件/行为树…） | `Debug/offline/fcs_dump/out/t3d/`（577 MB，2396 个资产，零失败） |
| 结构化摘要（每个蓝图：图清单+调用表+默认值；结构体字段；枚举；动画通知） | `Debug/offline/fcs_dump/out/digest/` |
| 数据表 CSV（一行一个法术） | `Debug/offline/fcs_dump/out/tables/DT_SpellsInfo.csv` 等 50 张 |
| 挖掘脚本 | `Debug/offline/fcs_dump/{dump_fcs,dump_t3d,t3d_tools,digest,mine_columns,dt_catalog}.py` |
| 🔴 **伪代码全文包**（19 个法术类 + 总控 + AI 任务，逐图线性化成可读文本） | `Debug/offline/fcs_dump/out/digest/PSEUDOCODE/` |
| 🔴 **粒子特效全量拆解**（116 个特效：emitter/渲染器/模块/参数值/材质/贴图） | `Debug/offline/fcs_dump/out/digest/VFX_BREAKDOWN.md` |
| 🔴 **人读详情树**（410 页，镜像工程目录：逐蓝图变量/CDO 默认值/接口/组件/调用/伪代码/**引脚级连线明细**，逐枚举/结构体/数据表） | `Knowledge/FCS详情解析/` —— ⚠️ **不进 git**（可完全重生成：`python tools/ue-dissect/detail_tree.py`，薄版加 `--no-pins`） |
| **工具链正本**（可复用于任意 UE 工程，路径参数化） | `tools/ue-dissect/`（含 README：流程/军规/边界） |

---

## 二、系统总览

### 2.1 三层结构

```
① 数据层   DT_SpellsInfo（67 行 × 22 字段）  ← 一个法术 = 一行
              └ 子结构体：投射块 / 引导块 / 放置块 / 持续块 / 召唤武器块 / 音效块 / 特殊伤害块
② 逻辑层   19 个蓝图类（BP_AbilityParent 及其子类）  ← 一行指向其中一个
              └ 总控：MagicComponent（挂在角色身上）
③ 组件层   MagicComponent 资源·冷却·法术书 / BuffComponent 增益 / ResourceComponent 耐力蓝条
              TargetingComponent / CombatStatusComponent / InputBufferComponent / EquipmentComponent（属性来源）
```

### 2.2 从按键到落地（完整时序）

```
玩家按键
 → MagicComponent.BeginMagicCharge
     └ SpellConditions()  检查（蓝够不够 / 冷却好没好 / 输入缓冲）
     └ BeginSpell()       取出选中法术 → SpawnSpell() 生成"能力 Actor"
          └ 按名字把数据行里的值写进 Actor 的属性（SetFloatPropertyByName 等）
          └ 播放蓄力粒子 + 蓄力音效
 → 蓄力动画播放，到出手帧触发 ①
 → AN_TriggerMagicAbility（动画通知）
     └ MagicComponent.TriggerMagicSpell → 转发给能力 Actor
 → 能力 Actor 落地：生成弹丸 / 起引导计时器 / 摆落点圈 / 挂自身增益 …  ← 各族不同
 → 再往后一帧 AN_TriggerNextAbility → NextSpell() → 衔接下一发（输入缓冲）
 → StopMagicCharge / DestroySpell 收尾；被打断走 Spell-Interupted
```

### 2.3 资产规模（FCS 目录下 2695 个）

| 类别 | 数量 | 与法术的关系 |
|---|---|---|
| 粒子（Niagara 系统/发射器） | 724 | 法术表现的主体 |
| 动画（蒙太奇/序列/混合空间） | 425 | 施法手势 + 移动集 |
| 网格 / 音效 / 控件 | 390 / 203 / 305 | 法师套装 / 98 个法术音效 / 法术 UI |
| 蓝图 | 289 | 其中法术族类 19 个（含 5 个不进表的次级类）+ 组件 20 + AI 任务 40 |
| 数据表 / 结构体 / 枚举 | 50 / 64 / 40 | 法术数据全在这 |

---

## 三、数据层（全字段、全枚举、全表）

### 3.1 `S_SpellInfo` —— 22 个字段（✅ 全读出，含类型与默认值）

| # | 字段 | 类型 | 说明 |
|---|---|---|---|
| 0 | `Name` | 文本 | 法术名（本地化文本） |
| 1 | `ManaCost` | 整型 | 蓝耗 |
| 2 | `Range` | 整型 | 射程（法术普遍填 3000，引导族填 1000） |
| 3 | `BaseDamage` | 整型 | 基础伤害（进公式，见 §五） |
| 4 | `Cooldown` | 浮点 | 🔴 **冷却字段确实存在**（旧分析说"没有"是错的）；实测法术行普遍填 0，只有 MadnessBolt/PlaceTick/Barrier 等填了 10~30 |
| 5 | `AbilityImage` | Texture2D | 图标 |
| 6 | `Animation` | AnimMontage | 🔴 **出手动画（数据行指定，不是类别写死）** |
| 7 | `SpellChargePS` | Niagara | **蓄力粒子（跨族共用，所有族的蓄力都走这个字段）** |
| 8 | `SpellSoundFX` | S_SpellSounds | 四段音效（§3.4） |
| 9 | `SpellDescription` | S_ItemDescription | 描述 + Rank 号 |
| 10 | `SpellRequiresChannel` | 布尔 | 是否引导 |
| 11 | `SpellCategory` | 字节枚举 | **元素**：E_SpellCategory |
| 12 | `DamageType` | 字节枚举 | **伤害类型**：E_DamageType |
| 13 | `SpecialDamageInfo` | S_SpecialDamageInfo | 命中后附加状态（§5.2） |
| 14 | `SocketAttachName` | 名称 | 🔴 **挂点**：粒子/音效挂骨骼哪个 socket（全表统一填 `RightHandSpell`） |
| 15 | `SpellParentClass` | 类引用 | 🔴 **逻辑类**（决定了族） |
| 16 | `SpellTrainerInfo` | S_SpellTrainerInfo | 找谁学（金币 + 等级门槛） |
| 17 | `SpellProjectile` | S_SpellProjectile | **族块**：投射物 |
| 18 | `SpellChannel` | S_SpellChannel | **族块**：引导 |
| 19 | `SpellPlacement` | S_SpellPlacement | **族块**：放置/区域 |
| 20 | `SpellDurations` | S_DurationSpells | **族块**：持续/增益/屏障 |
| 21 | `SpellSpawnWeapon` | S_SpawnWeaponInfo | **族块**：召唤武器 |

> 🔴 **注意：没有"施法方式"字段**。`E_SpellCastType`（Normal/Channel/Instant）是**枚举存在但不进数据行**——实测它只出现在 AI 黑板键 `SpellCastType` 上。**引导与否由 `SpellRequiresChannel` + 逻辑类共同表达**。

### 3.2 族块与子结构体（✅ 逐字段读出）

| 结构体 | 字段（类型） | 说明 |
|---|---|---|
| **S_SpellProjectile** | `SpellSpeed`(浮点) `SpellHitPS`(Niagara) | 只有两个：飞行速度 + 命中粒子。**发射解算与碰撞判定在类里** |
| **S_SpellChannel** | `SpellChannelPS`(Niagara) | 引导时持续照着的那条线/那股流 |
| **S_SpellPlacement** | `SpellPlacement`(Niagara) `SpellPS`(Niagara) `SpellRadius`(浮点,默认500) `TickFrequency`(浮点,默认0.5) `TickDuration`(浮点,默认10) | 🔴 **持续型法术的完整参数：半径 + 多久算一次 + 算多久** |
| **S_DurationSpells** | `SpellPS`(Niagara) `SpellDuration`(浮点) `SpellAmount`(浮点) `SpellDurationType`(枚举) `BarrierReflectDamage`(浮点) | 增益/屏障/治疗共用；"加在什么上"由枚举决定 |
| **S_SpellSounds** | `SpellCharge` `SpellLoop` `SpellFire` `SpellHit`（都是 SoundBase） | 四段音效 |
| **S_WeaponVFXInfo** | `WeaponVFX`(Niagara) `Height`(60) `Radius`(20) `Size`(3) `SpawnRate`(1) | 给武器挂持续特效（附魔） |
| **S_SpawnWeaponInfo** | `SpawnWeaponType`(枚举) `Weapon Data Table Info`(行句柄→DT_ItemData) `Weapon VFX Info` | 召唤武器指向物品表 |
| **S_SpellTrainerInfo** | `Gold Cost`(整型) `Level Requirement`(整型) | 学法术的门槛 |
| **S_SpecialDamageInfo** | 7 个子块：Fire / Ice / Frost / Electric / Poison / Madness / Polymorph + `Arrow Specific Damage Info` | 见 §5.2 |
| **S_RuneSaveInfo** | `RuneLocation`(Transform) `RuneActorName`(串) `Spell Damage` `Critical` `Spell Info` | 🔴 **符文要存档** |
| **S_BuffSaveInfo** | `Time Remaining` `NiagaraSystem` `Buff Type` `Buff Duration` `Buff Power` `Barrier Hit Damage` `Barrier Damage Type` `Barrier Damage Rank` `SpawnedWeapon` | 🔴 **增益/屏障也要存档**（含剩余时间） |

### 3.3 枚举全值（40 个，关键 12 个逐项列出）

```
E_SpellCategory  0=Fire 1=Frost 2=Poison 3=Electric 4=Madness 5=Arcane 6=General
E_DamageType     0=Physical 1=Fire 2=Ice 3=Electric 4=Poison 5=Madness 6=Magic 7=Energy 8=Frost 9=Polymorph
                 ⚠️ Ice 与 Frost 是两项，别混
E_SpellDurationType  0=Spawn Bow&Arrow 1=Strength 2=Agility 3=Intellect 4=Crit Chance 5=Spawn Two Handed
                     6=Barrier 7=Armor 8=Heal 9=Spawn Daggers 10=Spawn Sword&Shield
                     （注意：枚举顺序 ≠ 声明顺序，序号是资产里的存储顺序）
E_SpawnWeaponType    0=Two-Handed 1=Sword&Shield 2=Bow&Arrow 3=Daggers
E_SpellCastType      0=Normal 1=Channel 2=Instant        （不进数据表，只做 AI 黑板）
E_AISpellOptions     0=Projectile 1=Channel 2=PlacementSpell 3=Barrier 4=Heal 5=Teleport 6=SkyChannel
E_CharacterAction    0=LightAttack 1=Lunge 2=Block 3=BowAim 4=None 5=Roll 6=Sprint 7=CastSpell 8=ChargeSpell
                     9=HeavyAttack 10=BlockBreak 11=DrawWeapon 12=SheatheWeapon 13=Arrow 14=Spell
                     15=BarrierReflect 16=FistAttack 17=UnblockableSpell 18=SpellIgnoreHit    ← 施法是角色级动作状态
E_CombatTextType     0=Normal 1=Fire 2=Ice 3=Frost 4=Electric 5=Poison 6=Madness 7=Magic 8=Energy 9=Polymorph
                     10=Block 11=Parry 12=BlockBreak 13=Heal 14=Barrier 15=BarrierBreak      ← 飘字按伤害类型分色
E_AIBehaviour        18 项（含 16=MagicAttack）    E_AttackStyle 0=Melee 1=Ranged 2=Magic
E_AICombatStyle      5=Mage (Magic)                E_WeaponType  8=Magic
E_CharacterResource  0=Stamina 1=Mana              E_DamageCategory 3=MagicDamage
E_StatType           0=Vitality 1=Dexterity 2=Wisdom 3=Strength 4=Agility 5=Intellect 6=CritChance 7=MeleeDPS 8=RangeDPS 9=Armor
E_CameraChanges      0=MeleCombat 1=DefaultLocomotion 2=RangedCombat 3=MagicCombat 4=Crouched
E_ConsumableType     2=Mana 8=Barrier 9=HealOverTime   （药水能回蓝/上盾/持续治疗）
```
（其余 28 个枚举：物品/背包/任务/对话/画面设置等，全文见 `out/digest/_enums.txt`）

### 3.4 法术总表（67 行，✅ 全读出）

> 完整 22 列在 `out/tables/DT_SpellsInfo.csv`；此处列核心列。伤害/蓝耗/冷却为原始值。

| 行名 | 逻辑类 | 伤害 | 蓝 | 射程 | 元素 | 伤害类型 | 冷却 |
|---|---|---|---|---|---|---|---|
| Projectile-Fireball / Rank2 / Rank3 | ProjectileParent | 40/80/160 | 20/40/60 | 3000 | Fire | Fire | 0 |
| Projectile-Frostbolt ×3 | ProjectileParent | 35/70/140 | 20/40/60 | 3000 | Frost | Frost | 0 |
| Projectile-Poisonbolt ×3 | ProjectileParent | 25/50/100 | 20/40/60 | 3000 | Poison | Poison | 0 |
| Projectile-Lightningbolt ×3 | ProjectileParent | 40/80/160 | 20/40/60 | 3000 | Electric | Electric | 0 |
| Projectile-MadnessBolt ×3 | ProjectileParent | 1/0/0 | 20/40/60 | 3000 | Madness | Madness | **10/15/20** |
| Channel-Flamethrower / IcyWinds / PoisonSpray / ChainLightning | ChannelParent | 5 | 5 | 1000 | 各元素 | 各元素 | 0 |
| Channel-DrainLife | ChannelParent | 5 | 5 | 1000 | Madness | **Magic** | 0 |
| PlaceTick-FirePit / PoisonEruption | AreaAttackTickParent | 2 | 50 | 3000 | Fire/Poison | 同 | 10 |
| Place-MadnessZone | AreaAttackTickParent | 5 | 70 | 3000 | Madness | Madness | 10 |
| PlaceDmg-IceSpikes | AreaAttackSingleParent | 40 | 30 | 3000 | Frost | **Ice** | 0 |
| PlaceDmg-Meteor | NSTriggerSpellAttackParent | 100 | 50 | 3000 | Fire | Fire | 8 |
| PlaceDmg-LightningStrike | NSTriggerSpellAttackParent | 250 | 50 | 3000 | Electric | Electric | 8 |
| ChannelPlace-AcidRain / Blizzard / RainOfFire | ChannelSkyParent | 10 | 5 | 3000 | Poison/Frost/Fire | 同 | 0 |
| MoveChannel-FireStorm / IceStorm | MovingSpellParent | 15 | 5 | 3000 | Fire/Frost | 同 | 0 |
| Rune-Fire/Ice/Lightning/Poison | RuneParent | 70 | 0/60 | 3000 | 各元素 | 各元素 | 0 |
| Rune-Madness | RuneParent | 10 | 60 | 3000 | Madness | Madness | 0 |
| Buff-Armor/Strength/Agility/Crit/Intellect | BuffParent | 100 | 20 | 3000 | Arcane | Physical | 0 |
| Heal-OverTime | BuffParent | 100 | 30 | 3000 | Arcane | Physical | 0 |
| Heal-Once | HealSingleParent | 100 | 50 | 3000 | Arcane | Physical | 10 |
| Heal-Channel | HealChannelParent | 20 | 5 | 3000 | Arcane | Physical | 0 |
| Barrier-Energy/Fire/Poison/Ice/Lightning | BuffParent | 20 | 5 | 3000 | 各元素 | 各元素/Energy | 30 |
| Spawn2H{Fire,Frost,Lightning,Poison} | SpawnMagicItemParent | 20 | 5 | 3000 | 各元素 | 各元素 | 0 |
| SpawnDaggers{Fire,Frost,Lightning,Poison} | SpawnMagicItemParent | 7 | 5 | 3000 | 各元素 | 同 | 0 |
| SpawnSword&Shield{...} | SpawnMagicItemParent | 10 | 5 | 3000 | 各元素 | 同 | 0 |
| SpawnBow{Frost,Fire,Lightning,Poison} | SpawnMagicItemParent | 20 | 5 | 3000 | 各元素 | 同 | 0 |
| Teleport | Teleport | 80 | **0** | 3000 | Arcane | Fire | 10 |
| Polymorph | Polymorph | 1 | 30 | 3000 | Arcane | Polymorph | 20 |

**✅ 全表统一**：`SocketAttachName = RightHandSpell`（67/67）、`Name` 字段全部是本地化文本、每个法术都有自己的图标/动画/粒子/音效引用。

**读出的一条配置规律**：**Rank1/2/3 = 伤害 ×2、蓝耗 ×2、等级门槛 +2**（如 Fireball 40→80→160 伤害、20→40→60 蓝、门槛 1→3→5）。这是"同一法术三档"的标准做法。

---

## 四、逻辑层（19 个类 + 总控）

### 4.1 类继承树（✅ 从工程的 ParentClass 逐条读出）

```
BP_AbilityParent                        ← 基类：生命周期/蓄力/命中判定/资源/相机
├── BP_ProjectileParent                 ← 投射物（自带 ProjectileMovement + 碰撞球）
│    └── BP_Polymorph                   ← 变形 = 投射物 + 换结算（只覆写命中）
├── BP_ChannelParent                    ← 引导（地面/自身）
│    └── BP_ChannelSkyParent            ← 天降引导（打地面区域）
│         （BP_HealChannelParent 也继承 ChannelParent，见下）
│    └── BP_HealChannelParent           ← 治疗引导 = 引导族，只把"每跳伤害"换成"每跳治疗"
├── BP_SpellPlacementParent             ← 放置族基类（摆/跟随/移除，自身不打伤害）
│    ├── BP_AreaAttackSingleParent      ← 区域伤害·只打一次
│    ├── BP_AreaAttackTickParent        ← 区域伤害·按 TickFrequency 反复打
│    ├── BP_RuneParent → BP_PhysicalRune← 符文：地上踩中触发的陷阱，可存档
│    ├── BP_Teleport                    ← 传送（EQS 找落点）
│    └── BP_NSTriggerSpellAttackParent  ← 用粒子回调驱动伤害（陨石/落雷）
├── BP_MovingSpellParent                ← 飞行法术：飞行段与落地段是两个 Actor
│    （配 BP_MovingDamagingSpell，它直接继承 Actor，不是 AbilityParent）
├── BP_SelfCastSpell                    ← 自施法（Buff/Barrier/Heal 的公共父类）
│    ├── BP_BuffParent                  ← 增益 / 屏障
│    └── BP_HealSingleParent            ← 单次治疗
└── BP_SpawnMagicItemParent             ← 召唤魔法武器
```

> 🔴 **进数据表的是 14 个类**（行数分布见 §零）；`BP_MovingDamagingSpell` / `BP_PhysicalRune` / `BP_SelfCastSpell`（以及 `_C` 生成类）是族内部次级类，不直接进表。

### 4.2 基类 `BP_AbilityParent`（✅ 全变量 + 全函数读出）

**自有变量与默认值**（CDO 导出，前 16 个是它的，后面是 Actor 自带的）：

```
NiagaraSystem=None        AbilityCollision=None     SpellInfo=(空结构体)
CharacterActorRef=None    CharacterRef=None         CharacterMesh=None
FollowCamera=None         SpellVelocity=(0,0,0)     MagicComponent=None
EquipmentComponent=None   SpellPlacementLocation=(0,0,0)
ShieldDamageAmount=0.000000                        AI_Spell=False
EnemyTarget=None          Niagara System Ref=None   InputBufferComponent=None
PrimaryActorTick.bCanEverTick=False   ← 🔴 基类默认不 Tick（要 Tick 的子类自己开）
```

**函数清单**（30 个图）：

| 函数 | 干什么（✅ 从图里读出的调用） |
|---|---|
| `Initialize` / `LoadSpell` / `DestroySpell` | 装载与销毁；`Initialize` 的顺序是：写 SpellInfo → 写粒子引用 → `SetAsset()` → `Activate()` → `SetupVariables & References()` → `BeginMagicCharge()` |
| `BeginMagicCharge` / `StopMagicCharge` | 起蓄 / 收蓄（本体只有转发，实现在总控） |
| `TriggerMagicSpell` / `SendMagicSpell` | 触发（动画通知打进来）/ 落地（真正生成法术） |
| `ReleaseSpellChannel` | 松手结束引导 |
| `CalculateSpellVelocity` | 🔴 **弹道解算**：`LineTraceSingle` 找瞄准点 → `BlueprintSuggestProjectileVelocity`（配 `bFavorHighArc` 偏好高弧线）解出初速向量 → `SelectVector` 二选一 |
| `UpdateSpellPlacement` | 🔴 **落点标记每帧跟随**：射线打到地面 → `MakeRotFromZ` 摆正 → `K2_SetWorldTransform` 移动落点圈 → 可 `SetHiddenInGame` |
| `RemovePlacementMarker` | 移除落点圈 |
| `SetupVariables & References` | 用 `GetComponentByClass` 抓 MagicComponent / EquipmentComponent / InputBufferComponent，并 cast 出角色；`PassCamera` 把相机接进来 |
| `Get Camera Location` / `Get Camera Forward` / `GetCameraRelative` / `Get Camera Player Level` | 🔴 **一套"相机相对"瞄准工具**：取玩家相机位置与朝向，算出相对角色的水平偏移——**法术从手部发出但要朝着准星走** |
| `Spell-Interupted` | 被打断：判断是不是引导法术 → 决定 `NextSpell` 还是 `TriggerMagicSpell` 重入 |
| `CalculateSpellVelocity` 用到的 `GetCameraRelative` | 保证弹道与准星一致 |

### 4.3 逐族实现要点（✅ 每族的自有变量与调用都读出）

| 族（类） | 自有变量（默认值） | 命中判定 | 结算 |
|---|---|---|---|
| **BP_ProjectileParent** | `ProjectileMovement` `HitInfo` `LoopSound` `SpellVelocity` | 🔴 **物理碰撞事件**（绑定 `ComponentHit` + `ComponentBeginOverlap`） | `ApplyPointDamage` + `CalculateMagicDamage` + `SpawnSystemAtLocation` 命中粒子 + `PlayHitSound`（含 `ReportNoiseEvent` 制造噪音） |
| **BP_ChannelParent** | `HitInfo` `SpellStart/Loop/End`(蒙太奇) `SpellDamage` `CriticalHit` `Channeling` `ChannelTickTime` | **球扫多目标** `SphereTraceMultiForObjects`（每跳一次） | `ApplyTraceDamage`（玩家）/ `ApplyAITraceDamage`（AI）；`DrainLife` 单独一支（吸血）；伤害随引导时长用 `Get Camera Player Level` 调整 |
| **BP_ChannelSkyParent** | `SpellStart` `SpellLoop` `SpellEnd` `StopLoop` `SpellPlacementTimer` `SpellPS` `Channeling` `DamageChannel` | 球扫 | 同引导，但打的是地面区域；`SetupMarker` 摆落点 |
| **BP_HealChannelParent** | `NiagaraLeftHand` `ChannelTickTime=**0.25**` `SpellDamage` `CriticalHit` `ChannelLoopSound` | 继承引导的球扫 | 🔴 **只把每跳伤害换成每跳治疗**（模板方法复用的活范例） |
| **BP_SpellPlacementParent** | `SpellPlacementTimer` `AnimationSound` | 🔴 自身只收 `ActorBeginOverlap`（真伤害交子类） | 无（空壳：只负责摆/跟随/移除） |
| **BP_AreaAttackSingleParent** | `ChannelTickTime=**0.5**` `SpellTickTimer` `SpellDamage` `CriticalHit` `Hit Actor`(数组) | 球扫（半径来自放置块） | 定时器只跳一次 → `ApplyPointDamage` 逐目标 |
| **BP_AreaAttackTickParent** | 同上 + `NiagaraPS` | 球扫 | 🔴 **循环定时器**：每 `TickFrequency` 秒打一次，持续 `TickDuration` |
| **BP_RuneParent** | （继承放置族） | 碰撞重叠触发 | 生成 `BP_PhysicalRune` |
| **BP_PhysicalRune** | `RuneOwner` `RuneLocation` `RadialForceComponent` `FireArrowBurstDamage` | 🔴 **踩中触发** + 球扫补范围 | `CalculateMagicDamage` → `ApplyPointDamage`；`SaveInfo` 支持存档 |
| **BP_Teleport** | `TeleportLocation` `EnemyTarget` `MaxRange` `bTeleportPhysics` | 🔴 **EQS 查询** `EQS_AroundTarget` 取候选点 → 球扫验证可站立 | 无伤害（纯位移） |
| **BP_MovingSpellParent** | `SpellStart`(蒙太奇/通知名) `MoveSpeed` `SpawnTransform` | 自身不判 | 飞到点后**换成伤害 Actor**（`BP_MovingDamagingSpell`） |
| **BP_MovingDamagingSpell** | `MoveSpeed` `DamageEnemy` `TargetArray` | 双驱动：`ReceiveTick` + 周期定时器，球扫 | `ApplyPointDamage` + `PassDamageInfo` |
| **BP_SelfCastSpell** | `Niagara_0` `SpellPlacementTimer` | 无（目标是自己） | 把法术挂到自身 |
| **BP_BuffParent** | `BuffPS`(NiagaraComponent) | 无 | `ActivateBuff` → 计时 → 到期移除；屏障反弹走 `ApplyPointDamage` |
| **BP_HealSingleParent** | `NiagaraPS` | 不做射线，用上层传入的 Target | 改血量 + 广播（**不走伤害链**）+ `E_CombatTextType::Heal` 飘字 |
| **BP_Polymorph** | （只覆写命中，继承投射物） | 弹丸碰撞重叠 | 🔴 不改血量，套 `E_DamageType::Polymorph` + 换成羊模型（`SK_Sheep` + 动画 + 时长） |
| **BP_SpawnMagicItemParent** | `SpawnMagicItem` / `SpawnSpecificItem` | 无 | 🔴 `GetDataTableRowFromName` 读物品表 → `MakeStruct` 拼出物品 → 按 `SpawnWeaponType` 生成武器；`CreateSpellDurationBar` 给个剩余时间条 |
| **BP_NSTriggerSpellAttackParent** | （继承放置族） | 🔴 **粒子回调** `ReceiveParticleData` + 球扫补判 | `CalculateMagicDamage` → `ApplyPointDamage`（陨石/落雷用） |

### 4.4 两条实读出来的执行链（伪代码原文见 PSEUDOCODE 包）

**投射物命中（`BP_ProjectileParent.ApplyDamage`）**：
```
DoOnce → IsValid → CalculateMagicDamage() → IsValid → PassDamageInfo() → ApplyPointDamage() → SpawnSystemAtLocation()
```
> 🔴 `DoOnce` = **防重复命中**（弹丸碰撞会连续触发，靠它只结算一次）。

**引导每跳（`BP_ChannelParent.ApplyTraceDamage`）**：
```
Array_Clear → K2_SetWorldRotation（转向目标）→ SphereTraceMultiForObjects（球扫）
→ ForEachLoop（逐目标）→ IsValid → Array_Add（列表去重）→ PassDamageInfo → ApplyPointDamage
→ [DrainLife 分支] → ReduceMana（🔴 每跳都扣蓝）
```
> 🔴 **引导是"每跳扣蓝"的**（不是起手一次扣完）——这条直接决定引导法术的续航手感。

### 4.5 总控 `MagicComponent`（✅ 58 个图；关键事件流程逐条读出）

**事件清单（14 个自定义事件 = 对外 API）**：
`InitializeMagicComponent` `AddSpell` `SelectSpell` `SetupCooldown` `EndCooldown` `StartAICooldown` `UpdateSpell` `BeginMagicCharge` `TriggerMagicSpell` `ReleaseMagicCharge` `SendMagicSpell` `ReleaseSpellChannel` `NextSpell` `ToggleAbilityHotbar`

**关键事件流程**（✅ 沿执行线读出）：

```
BeginMagicCharge → SpellConditions() → 分支 → BeginSpell()
                    （条件不过就直接不发，也就是 CanCast 的落点）

TriggerMagicSpell → 分支 → TriggerMagicSpell()（转发给能力 Actor）
                            → SpellChannel()  /  SpellCharged()  /  StartAICooldown()

SendMagicSpell → 分支 → CanAnimationPlay() → 分支 → OnActionBegin() → 分支 → SendMagicSpell()（转发）
                          （🔴 动画没播到位就不落地——这是"过早松手"的保护）

NextSpell → SpellConditions() → 分支 → BeginSpell()  或  ReleaseMagicCharge()

ReleaseMagicCharge → UpdateStatus() → 分支 → SpellCharged()/SpellChannel() → ConsumeBuffer()
                                            + ClearLowPriority() + bOrientRotationToMovement

SetupCooldown → Spell Cooldown()（写时长） + Cooldown Time()（起计时）

UpdateSpell → ReleaseSpellChannel() → ReleaseMagicCharge() → 分支 → BeginMagicCharge()
```

**其它关键机制（✅ 从图调用读出）**：

| 机制 | 实现 |
|---|---|
| **法术书** | `Setup Spells & Spellbook` / `LoadDefaultSpells`（从 DT_Trainer-AllSpells 读默认法术）/ `UpdateHotbarVariable` / `UpdateStatus`（建 `WB_BuffDurationBar`/法术条） |
| 🔴 **数据行 → Actor 的注入方式** | `SpawnSpell`：`BeginDeferredActorSpawnFromClass` → **`SetBoolPropertyByName` / `SetFloatPropertyByName` / `SetObjectPropertyByName` / `SetStringPropertyByName` / `SetStructurePropertyByName`** → `FinishSpawningActor`。**按属性名逐项写入**，所以 Actor 的变量名必须与数据表字段名一致 |
| **冷却** | 组件级：`Spell Cooldown`(写)、`Cooldown Time`(计时句柄)、`BeginCooldown`/`EndCooldown`/`LowerCooldown`；**AI 单独一套** `StartAICooldown` |
| **输入缓冲** | `InputBufferComponent`：`ConsumeBuffer`（消费）/ `ClearBuffer`（清空）/ `ClearLowPriority`（掉低优先级）／近战/弓箭/受击动画上也挂 `AN_InputBufferSwitch` 切换缓冲状态 |
| **动画闸门** | `CanAnimationPlay()` / `OnActionBegin()` —— 施法前问动画系统"能不能播"，避免动作被吞 |
| **存档** | `LoadSaveData`（`LoadGameFromSlot`）+ `SpawnRunes`（读档重建地上的符文）+ 增益的 `S_BuffSaveInfo`（含剩余时间） |
| **相机** | `PassCamera`（基类里把相机传给能力 Actor，用于相机相对瞄准）；`E_CameraChanges` 有 `MagicCombat` 一档 |

---

## 五、伤害与结算（✅ 公式逐节点读出）

### 5.1 伤害公式（`MagicComponent.CalculateMagicDamage`，纯函数）

```
输入：S_SpellInfo（整行）
① 基础       = float(SpellInfo.BaseDamage)
② 智力加成   = 基础 + EquipmentComponent.Intellect          ← 属性直接加在基础值上
③ 随机浮动   = RandomFloatInRange(② × 0.75, ②)             ← 🔴 75%~100%，宏名 RandomMinMaxValue
④ 暴击判定   = RandomFloatInRange(0, 100) < EquipmentComponent.CritChance
⑤ 暴击倍率   = SelectFloat(bPickA=④, A=2.0, B=1.0)          ← 🔴 暴击 ×2，普通 ×1
输出：Spell Damage = ③ × ⑤ ，  Critical Hit = ④
```

> 用法：结果作为 `ApplyPointDamage` 的伤害值传出去；`Critical Hit` 用来决定飘字/特效。**近战/弓箭有各自的伤害路径，只有法术走这个函数**（`E_DamageCategory::MagicDamage` 单独一项）。

### 5.2 特殊伤害（命中之后附加的状态，✅ 全字段 + 默认值）

| 块 | 字段 | 工程里的默认值 |
|---|---|---|
| **S_SpecialFire**（燃烧） | `BurnDamage` `BurnDuration` `BurnTickRate` | 32 / 8 秒 / 每 0.25 秒一跳 |
| **S_SpecialFrost**（减速） | `SlowPercentage` `SlowDuration` | 20% / 8 秒 |
| **S_SpecialIce**（冻结） | `FreezeDuration` | 3 秒 |
| **S_SpecialElectric**（电跳） | `ElectricJumpDamage` | 20（跳伤） |
| **S_SpecialPoison**（中毒） | `PoisonDamage` `PoisonDuration` `PoisonTickRate` | 95 / 30 秒 / 每 1.5 秒 |
| **S_SpecialMadness**（疯狂） | `MadnessDuration` | 8 秒 |
| **S_SpecialPolymorph**（变形） | `PolymorphMesh`(SkeletalMesh) `PolymorphAnimation`(蒙太奇) `PolymorphDuration` | 羊模型 + 羊动画 |

> 🔴 **一个法术可以同时挂着七种状态**（`S_SpecialDamageInfo` 是"七个块全带"，每行只填用得上的那几个）。实测 Fireball 的行里同时填了 Fire（燃烧）和 Ice（冻结 3 秒）——**说明作者是把它当"可选挂件清单"用的**。

### 5.3 屏障 / 增益（`S_DurationSpells` + `BuffComponent`）

- **屏障**：`SpellAmount`（盾量）+ `SpellDuration`（时长）+ `BarrierReflectDamage`（默认 **5.0**，反弹伤害）+ `BarrierDamageType`（按伤害类型免疫/吸收）；按元素的屏障粒子 `NS_<元素>_Barrier` 五套 + `NS_Normal_Barrier`。
- **反弹**：走 `ApplyPointDamage` 打回攻击者（`E_CharacterAction::BarrierReflect`、`E_CombatTextType::BarrierBreak` 都有对应项）。
- **增益对象**：三属性（力量/敏捷/智力）+ 暴击 + 护甲 + 治疗，由 `E_SpellDurationType` 决定加在哪。
- **存档**：`S_BuffSaveInfo` 连"剩余时间/粒子引用/盾量/盾类型/伤害档"一起存。

### 5.4 治疗

- **单次治疗**（`BP_HealSingleParent`）：进入 → 挂粒子 → `Delay`/`Duration` → 结算 → 广播；**不走伤害链**，直接改血量 + 广播 + `E_CombatTextType::Heal` 飘字。
- **引导治疗**（`BP_HealChannelParent`）：完全复用引导族，每 `ChannelTickTime = 0.25 秒` 一跳，把每跳伤害换成每跳治疗。

---

## 六、资源 / 冷却 / 施法条件

| 项 | 实现（✅） |
|---|---|
| 蓝条 / 耐力条 | `E_CharacterResource = Stamina / Mana`；`ResourceComponent` 管；UI 上 `E_StatBarType` 有 Health/Stamina/Mana/XP 四条 |
| 扣蓝 | `MagicComponent.ReduceMana`（能力基类里也有转发） |
| 金币 | 只在**学法术**与**商店**用（`SpellTrainerInfo.GoldCost`） |
| 冷却 | **组件级**（`SetupCooldown`/`BeginCooldown`/`EndCooldown`/`LowerCooldown`）+ **AI 专用 CD**（`SetupAISpellCD`/`StartAICooldown`/`TeleportOnCD` 黑板键）。数据行的 `Cooldown` 字段也被读走（只有少数几个法术填了值） |
| 施法条件 | `SpellConditions()`：蓝够 + 冷却好 + 动画可播；失败则不进入 `BeginSpell` |
| 抗性/减伤 | 走 `CombatStatusComponent` / `EquipmentComponent`（护甲、格挡）；法术伤害类型独立于物理 |

---

## 七、触发与输入（含逐动画触发帧）

### 7.1 三个动画通知（✅ 类名 + 调用读出）

| 通知 | 实现内容 | 挂在哪 |
|---|---|---|
| 🔴 `AN_TriggerMagicAbility` | `Received_Notify` → `GetOwner` → `GetComponentByClass(MagicComponent)` → **`TriggerMagicSpell`** | **所有出手帧** |
| `AN_TriggerNextAbility` | 同上，但调 **`NextSpell`** | **收尾帧** |
| `AN_InputBufferSwitch` | 切换输入缓冲组件状态（带时长） | 施法首段、近战、弓箭、受击 |

### 7.2 🔴 出手帧实测表（✅ 从蒙太奇资源里读出的通知时间）

| 动画（蒙太奇） | 长度(秒) | RateScale | **触发帧** | 下一发帧 | 输入缓冲窗 |
|---|---|---|---|---|---|
| `M_Projectile`（投掷） | 2.300 | 1.8 | **0.828**（36%） | 1.947 | 0.740 +0.730 |
| `M_MagicAttack_RH_Up`（举手上，屏障类） | 2.300 | 1.4 | **0.976**（42%） | 1.642 | 0.903 +0.650 |
| `M_EruptionSpell`（下压，放置类） | 2.167 | 1.4 | **1.231**（57%） | 1.815 | 0.736 +1.000 |
| `M_SkyfallSpell`（举天，天降类） | 2.967 | 1.4 | **1.190**（40%） | 2.353 | 1.088 +1.184 |
| `M_SpellChannel1_Start` / `01` | 1.023 | 1.2 | **0.774**（76%） | — | — |
| `M_SpellChannel2Start` / `01` | 1.819 | 1.2 | **1.060**（58%） | — | — |
| `SpellChannelIntro` | 1.551 | — | **1.042**（67%） | — | — |
| `M_SpellChannel1_End` | 0.858 | 1.4 | — | 0.684 | 0.254 +0.549 |
| `M_SpellChannel2End` | 1.058 | — | — | 0.749 | 0.244 +0.464 |

**引导族的循环段**：`M_SpellChannel1`(3.333s) / `M_SpellChannel1_Mid`(1.254s) / `M_SpellChannel2`(4.300s) / `M_SpellChannel2Mid`(1.058s) —— **没有触发通知**，因为伤害由计时器每跳打（`ChannelTickTime`），不是每帧。

> 🔴 **对我们的意义**：骑砍没有动画通知，只能每帧读动画进度轮询。**上表的百分比可以直接当阈值用**（投掷类约 36%、举天/举手下压约 40~57%、引导起手 58~76%）。

### 7.3 输入缓冲

`InputBufferComponent`：`ConsumeBuffer` / `ClearBuffer` / `ClearLowPriority`；`AN_InputBufferSwitch` 在动画的特定窗口开/关缓冲。效果：**出手后到收招前按键 → 记下来 → `AN_TriggerNextAbility` 触发时立刻接下一发**。

---

## 八、表现层

### 8.1 粒子（724 个资产；✅ 全清单按族列出）

| 族/用途 | 资产 |
|---|---|
| 投射物 | `NS_Fireball` `NS_FireballCharge` `NS_Frostbolt` `NS_LightningBolt` `NS_MadnessBolt` `NS_PoisonBolt` `NS_SheepMagic` `NS_ExplosionGroundBig` |
| 命中爆炸（按元素） | `NS_FireExplosion` `NS_FrostExplosion` `NS_LightningExplosion` `NS_MadnessExplosion` `NS_PoisonExplosion` |
| 引导 | `NS_ChainLightning` `NS_DrainLife`(+Charge/Hit) `NS_FlameThrower` `NS_IcyWinds` `NS_PoisonSpray` |
| 天降 | `NS_AcidRain` `NS_Blizzard` `NS_LightningStrike` `NS_Meteor` `NS_RainOfFire` |
| 放置/区域 | `NS_FirePit` `NS_Ice_Circle` `NS_MadnessZone` `NS_PoisonEruption` |
| **落点指示圈** | `NS_PlacementCircle`（放置族必备件） |
| 符文 | `NS_Rune_Fire/Ice/Lightning/Madness/Poison` |
| 屏障（按元素） | `NS_Fire_Barrier` `NS_Frost_Barrier` `NS_Lightning_Barrier` `NS_Poison_Barrier` `NS_Normal_Barrier` |
| 增益/治疗 | `NS_AgilityBuff` `NS_CritBuff` `NS_DefenceBuff` `NS_IntellectBuff` `NS_StrengthBuff` `NS_SingleHeal` `NS_HealingOverTime` |
| 传送 | `NS_Skill_TeleportationIn` / `Out` |
| 移动法术（龙卷） | `NS_FireTornado` `NS_IcyTornado` |
| 🔴 **武器附魔** | 网格特效 `NE_Fire_Mesh` `NE_Frost_Mesh` `NE_Lightning_Mesh` `NE_Poison_Mesh` + 武器流 `NS_FX_Sword_Fire/Frost/Lightning/Poison` + 换武器 `NE_WeaponSwitch_01` |

**每法术的三段式**：`NS_<法术>Charge`（蓄力）/ `...Hit`（命中）/ `...Loop`（飞行或持续循环）——与数据行的 `SpellChargePS` / `SpellHitPS` / `SpellChannelPS` 一一对应。

#### 8.1.1 🔴 每个特效的完整组成（已全量解出，58 个系统 + 58 个网格特效）

> ⚠️ **两代粒子系统并存**：本工程 **65 个 Niagara + 34 个 Cascade**。**法术特效 100% 是 Niagara**（法术表引用 Niagara 132 次、Cascade 0 次）；34 个 Cascade 全在近战/弓箭（`MeleeVFX` 5 + `RangedVFX` 29）。Cascade 的解析与「跨引擎复刻交接规范」见 [骑砍2粒子系统.md](骑砍2粒子系统.md) **§十一**（含它比 Niagara 更贴近骑砍的对照表）。

**组成链条**：`系统 NS_* → emitter（N 个）→ 渲染器（Sprite/Mesh/Ribbon）→ 材质 → 贴图` ＋ `emitter → 模块栈（按阶段）→ 模块参数值`。

**逐系统清单**：`out/digest/VFX_BREAKDOWN.md`（116 个资产，人读）+ `vfx_breakdown.json`（机读）。

**范例：`NS_Fireball` 的完整拆解**（✅ 实读）

```
NS_Fireball（4 个 emitter）
├ Embers001_10   局部空间·插值生成
│   渲染器 SpriteRenderer → 材质 MI_Embers，Alignment=VelocityAligned，SubImageSize=(2,2)  ← 2×2 图集
│     贴图: Texture←T_Embers_01 · UseTextureAlpha?←T_Slash_01
│           DissolveTexture(经 MF_Dissolve)←T_NoiseCells · DistortionMask(经 MF_Distortion)←T_Mask_01
│   模块[Emitter级]:     SpawnRate → EmitterState
│   模块[ParticleSpawn]: InitializeParticle → SphereLocation → AddVelocity
│   模块[ParticleUpdate]:ParticleState → ScaleSpriteSize → Color → CurlNoiseForce
│   模块[动态输入/求解器]: Vector2DFromCurve → RandomRangeVector → SolveForcesAndVelocity
│   参数值（56 条，节选）：
│      SpawnRate.SpawnRate = 15        SphereLocation.Sphere Radius = 30
│      InitializeParticle.Lifetime Min/Max = 0.30 / 1.25
│      Sprite Size Min/Max = (2,2) / (3,3)    Uniform Sprite Size Min/Max = 50 / 80
│      CurlNoiseForce: Noise Strength=300, Noise Frequency=50, Cone Mask Angle=45°
│      SolveForcesAndVelocity: Speed Limit=1000, Acceleration Limit=9999
│      Color.Scale Alpha=1.0, Scale Color=(1,1,1)
├ Embers_6（默认材质 DefaultSpriteMaterial）
├ Fire_8 → MI_Fire      └ Smoke_7 → MI_Fire（局部空间）
```

**🔴 三条关于它美术做法的实测结论**：

1. 🔴 **两条路线混用（全量扫过，别只说一半）**：
   - **序列帧图集**：**火焰/烟雾/爆炸的"核心"**用 8×8 与 4×4 图集 —— `T_Fire_8x8`（材质 `MI_Fire_01_8X8`）、`T_SmokeSheet8x8_01`（`MI_SmokeFlipbook_01/02`）、`T_FireFlipbook_02`、`T_Fire_01`（`MI_FireFlipbook1`）、`T_Explosion4x4_01`（`MI_ExplosionFlipbook`）。使用者：火坑 / 落雷 / 陨石 / 火雨 / 毒爆 / 酸雨 / 暴雪。
   - **程序化噪声+遮罩**：**余烬、消散、扭曲**靠噪声贴图 + Curl Noise 力场 + 曲线驱动 —— `T_NoiseCells`（被引用 162 次，全工程第一）、`T_Mask_01`(76)、`T_Slash_01`(32)、`T_Noise_08/10/Plasma`、`T_Cloud_Noise_01`、`T_Beam_Gradient`。
   - 判断口径：**"一大团火/烟"用图集，"细碎火星/边缘消散"用噪声**。
2. **力学是标准 Niagara 配方**：`InitializeParticle`（寿命/尺寸/旋转）→ `AddVelocity/ApplyInitialForces`（初速）→ `CurlNoiseForce`（扰流）→ `SolveForcesAndVelocity`（积分）→ `ScaleSpriteSize/Color/ColorFromCurve`（随生命周期变化）→ `ParticleState`（收尾）。**换元素就是换材质贴图 + 换颜色曲线**（Fire/Frost/Poison/Lightning/Madness 五套共用同一套模块栈）。
3. **另有"网格特效"一支**（`NE_*`，58 个）：用 **Mesh 渲染器**（如 `NE_Fire_Mesh` 用 `SM_Sphere`、`NE_Chrome` 用 `SM_Shield_02`）做武器附魔/爆炸球/地面结霜 —— 材质是 `M_Flame` / `MI_Glow_Fire` / `M_Spark` 等，**这一支才是"能看见实体几何"的特效**。

**参数值是怎么解出来的**（可复现）：Niagara 把模块参数存在 `NiagaraScript.RapidIterationParameters` 里 —— `SortedParameterOffsets=(Offset,Name,TypeDefHandle)` + `ParameterData=(字节)`。按偏移排序取差值当长度，再按类型索引解码：`55`=float、`59`=Vector2D、`60`=Vector3、`62`=颜色(RGBA)、`4 字节无类型`=int。脚本：`Debug/offline/fcs_dump/vfx_breakdown2.py`。

**系统级设置**：`WarmupTime` / `FixedBounds` 等（导出的系统属性里）。

### 8.2 音效（98 个法术音效；✅ 全清单）

- **四段**：`SC_<法术>Charge` / `Loop` / `Fire` / `Hit`（数据行 `S_SpellSounds` 四个字段）。
- 通用件：`SC_Barrier` `SC_Buff` `SC_Heal` `SC_HealChannel` `SC_LearnSpell` `SC_OpenSpellbook_01~03` `SC_ProjectileThrow` `SC_RunePlacement` `SC_Skyfall` `SC_SpawnMagicWeapon` `SC_Teleport` `SC_EruptionSpell`。
- 命名分两档：`SW_*` = 原始音频；`SC_*` = 音效蓝图（SoundCue，可加调制/随机）。
- 🔴 **缺什么**：这套音效里**没有"施法失败/打断"音**，也没有魔法专用的受击音。

### 8.3 动画（422 个；法术相关全清单）

| 类别 | 资产 |
|---|---|
| **法术手势**（6 条主线） | `M_Projectile`（投掷）`M_MagicAttack_RH_Up`（举手上）`M_EruptionSpell`（下压）`M_SkyfallSpell`（举天）`M_SpellChannel1*`（引导一组）`M_SpellChannel2*`（引导二组） |
| 引导分段 | Start / Mid / End 三段（`M_SpellChannel1_Start/_Mid/_End`），Start 段带触发通知 |
| **瞄准偏移** | `AO_SpellChannel` / `AO_SpellChannel2` + `SpellChannel1_Up/Mid1/Down`、`SpellChannel2Up/Mid/Down`（上中下三个瞄准姿势） |
| **施法移动集** | `MagicIdle` `MagicWalkForwards/Back/Left/Right` `MagicRunForwards/Back/Left/Right` + 混合空间 `BS_MagicLoco` `BS_Magic_AIStrafe` |
| 每族姿势 | `ProjectileSpell` `SkyfallSpell` `EruptionSpell` `BarrierSpell` `HandSplitSpell`（法术序列本体，蒙太奇引用它们） |
| 变形 | `Animation/Sheep`（羊的动画） |

### 8.4 相机与震动

- `E_CameraChanges` 五档：`MeleCombat` / `DefaultLocomotion` / `RangedCombat` / **`MagicCombat`** / `Crouched` —— 施法有独立相机档。
- ⚠️ **CameraShake 只有 4 个**：`CS_ArrowExecuteShake` `CS_DrawBow` `CS_MeleeHit` `CS_TakeHit` —— **没有魔法专用震屏**（它的施法反馈靠粒子+音效，不靠抖屏）。这是我们可选的加分项。

---

## 九、3C（相机 / 操作 / 角色）

| 项 | FCS 做法（✅） | 备注 |
|---|---|---|
| 瞄准 | 🔴 **相机相对**：`GetCameraRelative` / `Get Camera Forward` / `Get Camera Location` 从相机算方向，法术从手部 socket 发出但朝准星走 | 骑砍对应物 = 玩家朝向 + 准星；我们可照抄"手部起点 + 相机方向"的分离 |
| 落点 | 射线打地面 → `MakeRotFromZ` 摆正 → 每帧 `UpdateSpellPlacement` 跟随 → 画 `NS_PlacementCircle` | 落点圈是**放置族的必备件**，不是可选打磨 |
| 施法移动 | **不站桩**：`S_MageStrafe` 服务驱动走位（前后左右 + 对角共 6 向），`E_StrafeMovement` 枚举；`AddMovementInput` | 施法与走位并行 |
| 朝向 | `ReleaseMagicCharge` 里会切 `bOrientRotationToMovement`（移动导向） | 引导时朝向目标，移动时朝移动方向 |
| 动作状态 | `E_CharacterAction` 里 `CastSpell` / `ChargeSpell` / `BarrierReflect` / `UnblockableSpell` / `SpellIgnoreHit` —— 施法是**角色级动作状态**，不是纯特效 | 骑砍里对应"攻击动作/武器状态" |
| 相机震动 | 无魔法专用（见 §8.4） | |

---

## 十、AI（行为树 + 黑板 + 任务链）

### 10.1 行为树 `BT_MixedCombat` 的魔法分支（✅ 拓扑读出）

```
Selector(根)
├─ …近战/弓箭/翻滚/巡逻等分支（每个分支由一个黑板装饰器把守）
└─ [装饰器] AttackStyle Is Equal To Magic          ← 🔴 施法风格由黑板键 AttackStyle 决定
   Sequence
   ├─ T_StopMovement（停步）
   ├─ T_SetMovementSpeed（调速度）
   └─ Selector
      ├─ [Target Set? = NotSet] T_SetEnemyAsFocus（先锁定目标）
      └─ [Target Set? = Set]
         └─ [AI_Behaviour Is Equal To MagicAttack]   ← 行为状态 = 16 MagicAttack
            Selector "Magic"
            ├─ [SpellSelected NotSet] T_DetermineMagicSpell   （选法术）
            ├─ [SpellSelected Set] [SpellCharged NotSet] T_ChargeSpell   （吟唱）
            ├─ [SpellCharged Set] T_FireSpell / T_FireChannelSpell       （放）
            └─ [SpellSelected Set] T_ReleaseSpell                        （收招/放弃）
```

> 🔴 装饰器上带 `FlowAbortMode`（`Self`/`Both`/`LowerPriority`）—— 黑板键一变，正在跑的分支会被打断（这就是"吟唱到一半发现目标没了 → 放弃吟唱"的实现方式）。

### 10.2 黑板 `BB_AI`（33 个键，✅ 全部读出）

```
SelfActor · TargetLocation · AttackPlayer? · AI_Behaviour(枚举) · Block · StrafeLocation
StartLocationRef · AtNoiseLocation? · StoredStartLocation? · CanHearPlayer? · StartRotationRef
AlliesToKill · StoredPlayerLocation · EnemyTarget(对象) · EngagedCombat · WeaponDrawn · Rolling
MeleAttackType(枚举) · AttackReset · OutsideAttackGroup · StrafeLeft · ShouldLunge · SendLunge
AttackStyle(枚举) · ArrowLoaded · ClearForShot · SpellCharged · SpellCastType(枚举) · SpellSelected
HasMana · TargetSet · TeleportOnCD · LungeOnCD
```
> **法术相关 8 个**：`SpellCharged` `SpellCastType` `SpellSelected` `HasMana` `TeleportOnCD` + `EnemyTarget` `AttackStyle` `AI_Behaviour`。

### 10.3 施法任务链（✅ 每个任务的流程读出）

| 任务 | 流程 |
|---|---|
| `T_DetermineMagicSpell` | 🔴 `ReceiveExecuteAI` → 抓 PawnRef / cast I_AIController / `ToggleStrafe` / 抓 MagicComponent+WizardType+EquipmentComponent → `CalculateSpell()`（判定）→ `SendSpell()`（落到黑板）。**判定条件**：到目标的距离（`Less_FloatFloat`）、朝向是否对准（`FindLookAtRotation` 得到的 rotator 与自身 `NearlyEqual`）、`RandomBoolWithWeight` 加权、`TeleportCDCheck` 取反 —— 决定这次是不是传送；再按 `E_AISpellOptions`（7 类：投射/引导/放置/屏障/治疗/传送/天降）挑一个，`GetDataTableRowFromName` 取出该族的法术行写进 `ActiveSpell` |
| `T_ChargeSpell` | 🔴 `IsPlayingMontage` **防重入** → `BeginMagicCharge`（失败就走 cast failed）→ 置 `SpellCharged` |
| `T_FireSpell` | 校验 `SpellCharged && SpellSelected` → `SendMagicSpell`；**同时 `ToggleStrafe` 让 AI 走位** |
| `T_FireChannelSpell` | 同上但引导版（不打断引导） |
| `T_ReleaseSpell` | 收招：清黑板 → `ReleaseMagicCharge` → `ClearBuffer` |
| `S_MageStrafe`（服务） | 🔴 `AddMovementInput` 按 `StrafeMovement` 六向走位；`FloorToStandOn()` 用球扫找地面；`IsPlayingMontage` 判断是否在施法（施法中也走位） |
| `D_ShouldTeleport`（装饰器） | 若该传送：先 `ReleaseMagicCharge` + `ClearBuffer`（**放弃当前吟唱**）再走传送 |

> 🔴 **两条可抄的纪律**：① **`IsPlayingMontage` 防重入**（否则连续触发同一法术）② **施法期间照常走位**（不是定身站桩）。

### 10.4 传送的寻点（`BP_Teleport` + `EQS_AroundTarget`）

`FindTeleportLocation` → `RunEQSQuery`（`EQS_AroundTarget`）取候选点 → 事件回调 `OnQueryFinishedEvent` → 球扫验证可站立 → `Teleport`。**骑砍没有 EQS**，对应物是 `Scene.RayCastForClosestEntityOrTerrain` + 自定义打分。

---

## 十一、UI（✅ 控件树与变量读出）

| 控件 | 关键变量 | 结构（从控件树读） |
|---|---|---|
| **WB_Spellbook**（法术书） | `SpellBookSlots` `SpellsLearnt` `CurrentPageNumber` `CurrentCategory` `SpellsOfCategory` | 下一页/上一页按钮 + **按元素分类的"重购"按钮**（Restock Fire/Frost/Poison/Electric/Madness/Arcane）+ SizeBox 排版 |
| **WB_SpellHotbar**（快捷栏） | `AbilitySlots` `EquippedAbilities` `NumberOfAbilitySlots` | `HorizontalBox AbilityHolder` 横排槽位 |
| **WB_MagicCrosshair**（施法准星） | `crosshair_spread` `crosshair_thickness` `crosshair_length` `Height` | CanvasPanel + 四个 Border（上/下/左/右）+ **扩散值**（开火/移动时张开） |
| **WB_BuffDurationBar**（增益剩余） | `CurrentPercent` `TotalTime` `CurrentTime` `BuffType` `DurationTimer` `BuffSaveInfo` `NiagaraPS` | 图标 + `ProgressBar StatBar` + `TextBlock BuffTimeRemaining` + `TextBlock ShieldAbsorb`（盾吸收量单独显示） |
| 其它 | `WB_SpellSlot` `WB_SpellBookSlot` `WB_SpellTooltip` `WB_SkillPreview` `WB_SkillTrainer` `WB_DragItemAbility` | 槽位/提示/训练师界面 |

---

## 十二、存档

| 存什么 | 结构 | 读档怎么恢复 |
|---|---|---|
| 玩家状态 | `S_PlayerCombatStatus`（BP_SaveGame 的一个字段） | `LoadGameFromSlot` |
| **地上的符文** | `S_RuneSaveInfo`（位置/名字/伤害/暴击/整行法术） | `MagicComponent.SpawnRunes` 重建 |
| **身上的增益/屏障** | `S_BuffSaveInfo`（剩余时间/粒子/类型/时长/强度/盾量/盾类型/伤害档/是否召唤武器） | BuffComponent 按剩余时间续上 |
| 背包/装备/任务 | 各自 SaveData | — |

> 🔴 **一条硬结论**：**"留在世界上的法术"（符文、火墙、区域）必须存档**，而且要连"整行法术数据"一起存（它存了 `S_SpellInfo`），否则读档后重建的法术没有参数。

---

## 十三、玩法与成长

| 玩法 | 实现（✅） |
|---|---|
| 学法术 | `DT_Trainer-*` 14 张表（按族分：AllSpells/Buffs/Barriers/Channel/Heals/MagicWep/MovingSpells/PlaceChannel/Placement/Polymorph/Projectiles/Runes/Teleport）→ 训练师对话（`E_DialogueEvents::AbilityTrainer`）→ 花金币 + 等级门槛 |
| 法术分档 | Rank1/2/3（伤害×2、蓝×2、门槛+2） |
| 法师类型 | `E_MageType` = Fire/Frost/Poison/Lightning Mage（AI 用 `WizardType` 变量决定会放哪系） |
| 元素体系 | 元素（`E_SpellCategory` 7 种）× 伤害类型（`E_DamageType` 10 种）× 特殊状态（7 种）——**三者独立**，可交叉 |
| 战斗风格 | `E_AICombatStyle::Mage` + `E_AttackStyle::Magic` + `E_WeaponType::Magic`（三处都有魔法身份） |
| 消耗品联动 | 药水能回蓝 / 上盾 / 持续治疗（`E_ConsumableType`） |
| 装备联动 | 法术伤害吃 `Intellect` 与 `CritChance`（来自 `EquipmentComponent`，即装备加属性 → 加法术伤害） |

---

## 十四、对骑砍2 的落地映射（照这个抄）

> 本节全部是给**我们的**《法术体系-通用施法框架》用的；左侧是 FCS 的做法，右侧是我们的落点。

### 14.1 一级结论（三条）

1. 🔴 **"法术 = 数据行 + 逻辑类"直接可用**：我们的"法术 = 弹药物品 + 四段框架"可以再叠一层**数据行**（`SpellInfo`），把四段的参数一次填完，行里的 `SpellParentClass` 换成我们的"族预设 + 逻辑入口"。
2. 🔴 **族块互斥 vs 我们自由组合**：FCS 一行只能填一个族块（投射/引导/放置/持续/召唤武器 五选一），**我们没有这个限制**——这是优势，但**必须提供族预设行**，否则每行填八个轴字段必填错。
3. 🔴 **它给的全是"手感参数"，不是"实现"**：75%~100% 伤害浮动、0.75 秒输入缓冲窗、每 0.25 秒一跳的持续治疗、首次生效延迟带随机抖动——**这些数值比架构更值钱**。

### 14.2 逐项映射表

| # | FCS 的做法 | 我们要不要 | 怎么落 |
|---|---|---|---|
| 1 | 共享头 + 族块，一行一个法术 | ✅ **抄** | 四段之上加"法术族预设" |
| 2 | **族名当行名前缀**（`Projectile-Fireball`） | ✅ **抄** | 配表一眼看出族，防串族 |
| 3 | 14 个逻辑类 | 🟡 **抄形状不抄粒度** | 它的"区域·单次 / 区域·跳伤"两个类 = 我们一个 `TickFrequency` 参数 |
| 4 | 22 字段的表结构 | ✅ **抄** | 我们已经有的四段字段可直接对上（见 14.3） |
| 5 | `TickFrequency` + `TickDuration` + `SpellRadius` | ✅ **抄** | 持续型法术的三个参数 |
| 6 | 🔴 `InitialStartDelay` + **`InitialStartDelayVariance`** | ✅ **抄** | 落地生效带随机抖动（不是齐刷刷同时）——纯手感细节 |
| 7 | **四段音效** Charge/Loop/Fire/Hit | ✅ **抄** | 我们阴魔斩是 charge/burst/trail 三段，**缺"发射"音** |
| 8 | 粒子三段 `NS_<法术>Charge/Hit/Loop` | ✅ **已在做** | 阴魔斩的 charge/burst/trail 就是这三段 |
| 9 | **落点指示圈 `NS_PlacementCircle`** + 每帧跟随 | ✅ **抄** | 落点圈是**必备件**不是打磨项 |
| 10 | 手势按投送方式分（投掷/举天/下压/举手上/引导） | ✅ **抄** | 触发帧百分比直接用（§7.2） |
| 11 | 瞄准用**相机相对方向**，法术从手部 socket 出 | ✅ **抄** | 起点与方向分离 |
| 12 | `SocketAttachName` 挂点（全表统一 `RightHandSpell`） | ✅ **抄** | 我们挂"武器实体 / 骨骼 socket" |
| 13 | 🔴 伤害公式：`(基础+智力) × 随机(0.75~1)   × (暴击?2:1)` | ✅ **抄** | 属性吃进基础值 + 浮动 ±25% + 暴击 ×2 |
| 14 | 弹道解算 `BlueprintSuggestProjectileVelocity` + `bFavorHighArc` | ✅ **抄思路** | 瞄准轴该产出**"发射解（初速向量）"而不是"方向"**——有重力时要打中点必须解抛物线 |
| 15 | `bIsHomingProjectile` + `SnapToTarget`（追踪弹） | ✅ **抄** | 把"追踪"做成一个参数 |
| 16 | 七种特殊状态（燃烧/减速/冻结/电跳/中毒/疯狂/变形） | ✅ **抄** | 结算轴的状态清单；注意它的默认数值（§5.2） |
| 17 | 屏障是一等公民（独立盾量/时长/反弹/按元素粒子） | ✅ **抄** | `BarrierReflectDamage` 默认 5.0 |
| 18 | 治疗**不走伤害链**，直接改血 + 专用飘字 | ✅ **抄** | 我们对应 `Heal` 飘字类型 |
| 19 | 引导治疗 = 引导族换结算（模板方法） | ✅ **抄** | 印证"复用流程、只换结算" |
| 20 | `IsPlayingMontage` 防重入 + 施法时走位 | ✅ **抄** | 两条 AI/玩家通用纪律 |
| 21 | 引导伤害随引导时长涨 | ✅ 抄 | 手感项 |
| 22 | 冷却**组件级**，不进法术行 | ✅ **抄** | 冷却属技能槽层 |
| 23 | 输入缓冲（0.7~1.2 秒窗 + 收尾帧接下一发） | ✅ **抄** | 阶段 3 的连招/预输入 |
| 24 | 法术书 + 快捷栏 + 准星 + 增益条 | 🟡 **以后抄** | 形态可借 |
| 25 | 训练师 + 金币 + 等级门槛 + Rank1/2/3 | 🟡 参考 | 与我们的"弹药即法术"取法不同，只借分档规律 |
| 26 | 🔴 **留在世界上的法术要存档**（符文/增益连剩余时间） | ⚠️ **必须做** | 骑砍走 `SaveableTypeDefiner` |
| 27 | 施法是**角色级动作状态**（`E_CharacterAction`） | ✅ 参考 | 骑砍的武器流程天然提供 |
| 28 | 数据行 → Actor **按属性名逐项注入** | 🟡 参考 | 我们是"读表填字段"，思路一致 |

### 14.3 字段映射（我们的四段 → FCS 的字段）

| 我们的段 | FCS 对应字段 |
|---|---|
| **起手**（蓄力/引导/瞬发） | `Animation`（出手蒙太奇）+ `SpellChargePS` + `SpellRequiresChannel` + `SpellSoundFX.SpellCharge/SpeechLoop` |
| **瞄准** | `Range` + `SocketAttachName` + `SpellProjectile.SpellSpeed` + 类里的 `CalculateSpellVelocity`/`bFavorHighArc`（无字段，靠类） |
| **投送** | `SpellParentClass`（选族）+ `SpellProjectile` / `SpellPlacement` / `SpellChannel` 块 |
| **结算** | `BaseDamage` + `DamageType` + `SpecialDamageInfo` + `SpellDurations`（增益/屏障） |

### 14.4 明确抄不了的（引擎差异，不是做法优劣）

| 它的做法 | 为什么抄不了 | 我们的替代 |
|---|---|---|
| 投射物靠**物理碰撞事件**判命中 | 骑砍自管实体没有物理体，收不到"撞到谁" | **线段↔胶囊扫掠 + 世界射线**（`Mission.RayCastForClosestAgent` / `Scene.RayCastForClosestEntityOrTerrain`） |
| **动画通知**决定出手时机 | 骑砍无动画通知 | 每帧读动作进度 + §7.2 的阈值表 |
| Niagara 粒子体系 | 引擎不同 | 骑砍粒子系统 / 网格贴图动画（见 `Knowledge/骑砍2网格贴图动画_引擎能力与实现.md`） |
| EQS 查询落点 | 骑砍没有 EQS | 射线 + 自定义打分 |
| 蓝图的可视化逻辑 | 我们是 C# | 已经用四段框架表达 |
| 引擎自带 Montage/Slot 机制 | 骑砍的 action_set 是另一套 | `tools/anim-retarget` 已转 28 条施法动画 |

### 14.5 建议的落地顺序（按"抄什么最值钱"排）

1. **数据行**：把 `S_SpellInfo` 的 22 字段裁成我们的版本（保留：名字/蓝耗/射程/伤害/冷却/图标/动画/蓄力粒子/四段音效/描述/是否引导/元素/伤害类型/特殊伤害/挂点/族预设），先落 5 个法术（火球/冰锥/引导火焰/火墙/护盾）。
2. **伤害公式**：`(基础 + 属性) × 0.75~1.0 × 暴击2`，接我们现成的 `RegisterBlow` 管线。
3. **触发帧**：照 §7.2 的百分比做进度阈值打点。
4. **落点圈 + 持续结算**：`SpellRadius` / `TickFrequency` / `TickDuration` 三个参数打通。
5. **四段音效**：补上缺的"发射"音。
6. **AI**：先做"选法术 → 吟唱 → 放 → 收招"四步 + `IsPlayingMontage` 防重入 + 施法走位。
7. **存档**：地上的法术（火墙/符文/区域）必须先设计存档结构再实现。

---

## 十五、附录

### 15.1 未展开的（诚实清单）

| 项 | 状态 |
|---|---|
| ~~Niagara 发射器内部模块参数~~ | ✅ **已补齐**：58 个系统的 emitter / 渲染器 / 模块栈 / **参数值**全解出，见 §8.1.1 与 `out/digest/VFX_BREAKDOWN.md` |
| 曲线（Curve）资产的**逐关键帧数值** | ⚠️ 未展开（`VFX/CurveAssets` 3 个 + 模块里的 Vector2D/Color 曲线，曲线数据在 DataInterface 的字节里，未解码） |
| 骨骼 **socket 的坐标** | ⚠️ 未导出（T3D 里没有 socket 表；`RightHandSpell` 的名字确认存在，偏移未取到） |
| 部分结构体字段的**默认值是陈旧的** | ⚠️ 观察到 `SpellDescription` 的默认值里含已改名的子结构（`FireDamageInfo`），说明资产里存的是历史缓存值，**以数据表实际填的值为准** |
| 蓝图里的**字符串字面量**（提示文案） | ⚠️ 未逐条提取（如需可再跑一次 T3D 解析取 `DefaultValue` 里的字符串） |
| 商店/物品/任务/对话系统 | ⚠️ 与法术无关的部分未展开（数据表 CSV 都在，可用时再挖） |

### 15.2 复用清单（这次产出的可复用件）

| 件 | 位置 | 用途 |
|---|---|---|
| UE 全量 T3D 导出脚本 | `Debug/offline/fcs_dump/dump_t3d.py` | 换任意 UE 工程改两行即可 |
| T3D 解析 + 蓝图伪代码化 | `Debug/offline/fcs_dump/t3d_tools.py` | `graph` 模式出执行伪代码、`dataflow` 模式读纯函数公式 |
| 用户结构体字段名挖掘 | `Debug/offline/fcs_dump/mine_columns.py` | 从 .uasset/.uexp 挖 `字段_序号_GUID`，用于取数据表整格值 |
| 数据表转 CSV | `Debug/offline/fcs_dump/dt_catalog.py` | 任意表 → 一行一实体 |
| 🔴 **粒子特效全量拆解器** | `Debug/offline/fcs_dump/vfx_breakdown2.py` | 系统→emitter→渲染器→模块栈→参数值→材质→贴图，输出 `VFX_BREAKDOWN.md` |

> 这套工具是**跨工程通用**的（拆任何 UE 工程的蓝图/数据表都行）。是否收编进 `wheels`/`tools`，等你说。
