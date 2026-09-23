# HikageRising 忍者体系技术实现分析

> **分析对象**：`Modules/HikageRising`（模块 id `HikageRising`，v1.1.0，程序集 = `CalradiaAwakens.dll`）
> **位置**：1.2.12 备份客户端 `MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\HikageRising`
> **日期**：2026-09-22 ｜ **方法**：`ilspycmd` 全量反编译（1874 行 C#）+ ModuleData 全量 XML 巡检
> **反编译产物**：`Debug/offline/hikage_decompile/`（离线产物，不进库）

---

## 〇、一句话结论（先看这个）

**Hikage Rising 不是"给骑砍加了一套忍者战斗系统"，而是「把原版六个文化改名换皮成忍者门派」+「用原版投掷武器管线冒充忍术」。**

- 它的全部技术含量 = **6 个 Mission 层钩子 + 若干数据表**，一共 1874 行 C#。
- **没有**新的武器类、**没有**施法/法力系统、**没有**潜行/暗杀/攀爬机制。
- "忍术" = 投掷武器 + 隐形手持模型 + 飞行模型 + 拖尾粒子 + 换一套 ready/release 动作。

**对我们（LWN / Taikou 内容包）的价值**：它验证了三条我们**完全没有**的能力，且**都是通用件、都能数据驱动**——① 骨骼挂粒子（武器附魔光效 / 受击部位特效）② 多发弹幕（一次扔 N 枚）③ 伤害因子补丁（让 XML 写的伤害数字真正生效）。另有一条**我们已经开通但没用起来**的：④ 自定义战斗动作（走 `project.mbproj`）。详见 §6。

---

## 一、模块构成与代码地图

### 1.1 一句话看懂这个模块的体量分布

| 层 | 体量 | 说明 |
|---|---|---|
| **C# 代码** | **1874 行 / 11 个类** | 极薄。真正的逻辑全在这 11 个类里 |
| ModuleData XML | ~4 MB（含 968K + 661K 两个 xslt 覆盖包） | 装备 / 兵种 / 法术 / 动作 / 音效 |
| AssetPackages | 1.1 GB（`pack0.tpac` 735MB + `pack1.tpac` 353MB） | 网格 / 贴图 / 粒子，**已编译不可读** |
| ModuleSounds | 56 MB（69 个 wav） | 48kHz / 16bit / 立体声 PCM |

> 注：程序集名是 `CalradiaAwakens`（类名 `CalradiaAwakens.*`、Harmony id `com.calradiaawakens.patches`），SubModule 名却是 "Hikage Rising"。
> **即本 mod 是在 Calradia Awakens（奇幻 overhaul：兽人 / 精灵 / 火枪 / 魔法）的代码底座上做的忍者主题再包装**——`items_magic.xml`、`skins_orc.xml`、`ca_musket_*` 音效都是那个底座的遗产。

### 1.2 C# 全景（11 个类，逐个说清干什么）

| 类 | 基类/类型 | 干什么 | 触发点 |
|---|---|---|---|
| `SubModule` | `MBSubModuleBase` | 入口：`PatchAll` + 注册下面所有东西 | `OnBeforeInitialModuleScreenSetAsRoot` |
| **`CAItemParticleEffect`** | `MissionBehavior` | 🔴 **手持物品 → 往武器骨骼挂粒子**（80 条硬编码表） | 每帧 `OnMissionTick` |
| **`CADamageParticleModel`** | `MissionLogic` | 🔴 **命中 → 命中点爆点 + 受击部位骨骼挂粒子**（~70 条表） | `OnAgentHit` |
| **`CAShotgunEffectMissionLogic`** | `MissionLogic` | 🔴 **射击 → 用 `AddCustomMissile` 补发 N 发**（~25 条表） | `OnAgentShootMissile` |
| `XMLMeleeFix` | Harmony | 🔴 改 `WeaponComponentData.SetDamageFactors` → 因子 = `伤害值/20` | `WeaponComponentData.SetDamageFactors` |
| `NoFriendlyFireDamageModel` | `SandboxAgentApplyDamageModel` | 友伤归零 + **空手伤害 ×5**（体术） | `CalculateDamage` |
| `CAStatModel` | `SandboxAgentStatCalculateModel` | Athletics 技能 → 移速 `min(1+运动/600, 1.5)` | `UpdateAgentStats` |
| `RecruitProductionPatch` | Harmony | 按 `ca_recruits.xml` 权重表决定村庄产什么兵 | `DefaultVolunteerModel.GetBasicVolunteer` |
| `CAHarmonyPatches` | Harmony | 用 `ca_managed_core_parameters` **整体替换**原版 ManagedParameters | `Game.InitializeParameters` |
| `CARaceCampaignBehavior` | `CampaignBehaviorBase` | 存/读档保留 Hero 的 `Race`（兽人等自建种族） | `OnBeforeSaveEvent` / `OnSessionLaunchedEvent` |
| `CARaceMapSaveableTypeDefiner` | `SaveableTypeDefiner` | 注册 `Dictionary<string,int>` 存档容器（definer id `911918`） | — |
| `LoadCAConstantsBehavior` | `CampaignBehaviorBase` | 用 `XmlSerializer` 反序列化 `ca_armoury.xml` / `ca_recruits.xml` | `RegisterEvents` |
| `ArmouryBehavior` | `CampaignBehaviorBase` | 城镇加一项菜单「忍术馆」→ 按表筛选物品开交易界面 | `OnSessionLaunchedEvent` |
| `MissionSettlementPrepareView_SetOwnerBanner_Patch` | Harmony | 接管城镇场景旗帜贴图（`bd_banner_b` + tableau 混合） | `MissionSettlementPrepareView.SetOwnerBanner` |

**值得抄的两条工程习惯**：
1. **数据表全部硬编码在 C# 构造函数里**（三个粒子/弹幕类各自一个 `Dictionary`）。好处是零 IO、改起来一目了然；代价是**加一种元素就要重新编译**。我们已有更好的范式（`FirearmFxRegistry` 走 XML 契约），**不要照抄这个做法**。
2. **`SubModule.xml` 的 Xmls 列表里没有 `action_sets_mod` / `item_usages_sets_mod` / `er_combat_parameters` / `skins_orc` 这些 `_mod` 文件的注册项**——它们靠 `project.mbproj` 的编辑器工程清单 + `*.xslt` 覆盖原版路径进游戏（详见 §6）。

---

## 二、忍者外观（装备资产体系）

### 2.1 一句话结论

忍者的"外观体系"**零代码**，全部是物品 XML + 美术资产。核心手法只有一条：

🔴 **换色 = 同一套模型做「每个颜色一份独立 mesh + 一份独立贴图」，不是材质切换、不是 item modifier、不是 culture 颜色字段。**

这就是为什么资产包有 **1 GB**（`pack0.tpac` 735 MB + `pack1.tpac` 353 MB）——6 个门派色 × 24 件头饰 × (本体 + 流亡版) × 6 级 LOD，全是独立的网格与贴图。

### 2.2 物品定义结构（完整样例）

**最简忍者甲**（`items_shinobi_body.xml:666-688`）：

```xml
<Item id="hr_body_shinobi_shozoku"
      name="{=hr_body_shinobi_shozoku}[HR]Shinobi Shozoku"
      mesh="hr_body_shinobi_shozoku"
      culture="Culture.neutral_culture"
      value="100" weight="2.0" appearance="1" Type="BodyArmor">
  <ItemComponent>
    <Armor body_armor="10" leg_armor="10" arm_armor="10"
           has_gender_variations="true" covers_body="true"
           modifier_group="cloth_unarmoured" material_type="Cloth" />
  </ItemComponent>
  <Flags UseTeamColor="true" Civilian="true" />
</Item>
```

**最强忍者甲**（`items_shinobi_body.xml:787-809`）：同样结构，`body_armor="70"` / `value="10000"` / `material_type="Plate"`。

**头盔**（`items_shinobi_helm.xml:5-27` 布头巾 / `:774-797` 领主阶 70 防）：结构同上，多 `subtype="head_armor"` + `hair_cover_type` / `beard_cover_type`（控制是否盖住头发/胡子——**头巾类必须写 `all`，否则头发会穿出来**）。

**字段速查**（我们做装备时直接对照）：

| 字段 | 取值 | 注意 |
|---|---|---|
| `mesh` | 与 `id` 几乎总是一一对应 | **例外**：`hr_body_kimono_plain_team` 复用 `hr_body_kimono_plain` 的 mesh（队伍色版共用模型） |
| `culture` | 绝大多数 = `Culture.neutral_culture` | 🔴 **不影响外观颜色**（见 §2.3） |
| `Armor@has_gender_variations` | 布衣 `true` / 甲胄 `false` | 男女共模型还是两版 |
| `Armor@covers_body` | 布衣 `false` / 甲 `true` | 影响外观显隐 |
| `Armor@material_type` | `Cloth` / `Plate` / `Leather` | 影响命中音效与穿透 |
| `Flags@UseTeamColor` | 布衣/流亡版多为 `true` | **勾了会用 `_c` 队伍色遮罩贴图叠加染色**（见下） |
| `Flags@Civilian` | **全部 `true`** | 🔴 与我们的铁律 33 一致——**平民装可用是硬门槛**，这个 mod 每件都勾了 |

🔴 **全物品 XML 里没有任何 `<Materials>` 节点、没有任何 `color` 属性**（已全量 grep 确认）。唯一 `<Materials>` 出现在 `weapon_pieces.xml`，那是锻造配方（`Material id="Iron1"`），与颜色无关。

### 2.3 换色的真实机制（本次最有价值的一条）

#### ① 门派色 = 6 个"颜色家族"，与 6 个原版文化硬绑定

| 颜色家族 | 绑定文化 | 门派名（来自 §3.7 的改名） |
|---|---|---|
| `dusk` 暮 | aserai | Dusksand Syndicate |
| `grayspire` 灰塔 | vlandia | Grayspire Hold |
| `moon` 月 | empire | Moonveil Shogunate |
| `shadowglade` 影林 | sturgia | Shadowglade Province |
| `stormwind` 风暴 | khuzait | Stormwind Reach |
| `verdant` 翠 | battania | Verdant Veil Sect |

**绑定发生在装备名册层**（`EquipmentRosters_*`），**实测每个文化的两个名册文件对该门派色物品有 620+ 处引用**（六个文化全部如此，无一例外）：

| 文化 | 门派色 | `EquipmentRosters_troops_*` + `_npc_*` 两册引用数（实测） |
|---|---|---|
| vlandia | grayspire | 622 |
| aserai | dusk | 622 |
| battania | verdant | 622 |
| sturgia | shadowglade | 622 |
| empire | moon | 624 |
| khuzait | stormwind | 626 |

例（`EquipmentRosters_troops_vlandia.xml:4-11`）：

```xml
<EquipmentRoster id="hr_vlandia_recruit_template" culture="Culture.vlandia">
  <EquipmentSet>
    <Equipment slot="Head" id="Item.hr_headband_grayspire" />   ← vlandia 用 grayspire
    ...
```

🔴 **即：文化配色不是靠 item 的 `culture` 或 `color`，而是靠「名册里给这个文化的兵挑哪个颜色的物品 id」。** 这跟我们的做法（`Culture.csv` 配色 + 模板装备）是两条路——他们的更直白但更啰嗦。

#### ② 每个颜色家族 = 独立 mesh + 独立贴图（tpac 实证）

在 `pack0.tpac` 里做只读字符串扫描，能看到每个颜色**各有一整套 LOD 链**：

```
hr_helm_kabuto_primary_dusk        hr_helm_kabuto_primary_dusk.lod1.0
hr_helm_kabuto_primary_dusk.0      hr_helm_kabuto_primary_dusk.lod2.0
hr_helm_kabuto_primary_dusk.1      ... lod3 / lod4 / lod5 ...
hr_helm_kabuto_primary_dusk_exile.0     ← 流亡版也有自己的独立 mesh
```

贴图同样各一份：`hr_helm_shinobi_headwrap_d.png` / `_n.png` / `_s.png`（漫反射/法线/高光）。

**共 13 个头盔文件** = 1 个基础款（含领主阶）+ 6 颜色 × (本体 + `_exile`)。

#### ③ `_exile` 的唯一实质差异 = mesh

已用「剥离 `id`/`name`/`mesh`/`UseTeamColor` 后 diff」验证：**同款的本体版与流亡版，数值/字段 100% 相同**，只有三处差别：

| 差异 | 本体 | 流亡版 |
|---|---|---|
| `mesh` | `hr_headband_moon` | `hr_headband_moon_exile` |
| `id` / `name` | `[HR]Moon Shinobi Headband` | `[HR]Exiled Moon Shinobi Headband` |
| `UseTeamColor` | 常省略（默认 false） | 显式写出 |

→ **`_exile` 是纯换色 + 换名的复制品**，用来区分"体制内 / 体制外"（见 §3.7）。

#### ④ 🔴 对我们的启示：有便宜的路和贵的路，他们两条都用了

| 路线 | 做法 | 代价 | 适用 |
|---|---|---|---|
| **贵的路（他们主用）** | 每个配色**烘一套独立 mesh + 贴图** | 资产量 × N（1 GB 就是这么来的） | 配色不能靠染色实现（花纹位置不同、材质不同） |
| **便宜的路（他们也用）** | **一个 mesh + 一张 `_c` 队伍色遮罩贴图** + `<Flags UseTeamColor="true">` | 几乎零成本，跟着队伍色自动变 | **只是整体换色调**的场景 |

证据：`pack1.tpac` 里有 `hr_helm_shinobi_mask_team_c.png`（`_c` = color/team-color mask 通道）。

⚠️ 但注意 **`UseTeamColor` 只能跟着"队伍色"变，不能按门派挑色**——所以对"6 个门派各有固定配色"的需求，还是得走贵的路。**我们如果做 Taikou 的门派配色，要先想清楚是"跟队伍色"还是"门派固定色"**：前者一张遮罩贴图搞定，后者要 N 份 mesh。

### 2.4 mesh 清单与自建/原版区分

忍者相关的 `mesh` 去重共 **420 个**，其中 **406 个是 mod 自建**（`hr_` 前缀）、**14 个是复用原版或外部 mod**：

| 前缀 | 数量 | 用途 |
|---|---|---|
| `hr_helm_*` | 299 | 头盔（6 色 × 2 变体全在内） |
| `hr_body_*` | 33 | 甲（和服 / 忍者装束 / 武士甲） |
| `hr_hat_*` | 28 | 斗笠 |
| `hr_headband_*` | 24 | 额带/颈带（**只在 6 色变体文件里**） |
| `hr_weapon_*` | 7 | 投掷/盾 |
| `hr_shoulder_*` / `hr_legs_*` / `hr_gloves_*` / `hr_shinobi_*` / `hr_cape_*` | 6 / 4 / 3 / 1 / 1 | 肩甲 / 鞋 / 护腕 / 披风 |

**14 个复用的外部 mesh**（全在 `items_shinobi_other.xml` 与 weapons）：

- 原版件：`aserai_plated_shoulder`、`aserai_shoulder_a`、`battania_cloak_a`、`battania_shoulder_strap`、`battania_woodland_cloak`、`battanian_leather_shoulder_a`、`empire_cape_a`、`sturgia_cape_a`、`female_peasant_cape_b`、`stone_holster`
- 外部 mod（Calradia Awakens 遗产）：`cla_invisible_mesh`、`cla_invisible_holster`、`cla_bomb_grenade`

🔴 **注意后三个**：`cla_invisible_mesh` / `cla_invisible_holster` 是**隐形网格**——这是整个"法术/拳法/透明肩甲"体系的基石（手持看不见 = 空手施法观感）。**我们做法术时同样需要这个件**（若没有，就得自己造一个不可见 mesh）。

另有 **39 个武器 mesh 定义在 `weapon_pieces.xml`**（不在 items 目录里），命名规律 `hr_weapon_<刀种>_blade[_enhanced|_enchanted|_energy_{blue|red|void}]` + `_handle` + `_scabbard`。

### 2.5 防具数值（明显的超模）

| 项 | HikageRising | 原版最高 | 差距 |
|---|---|---|---|
| 甲 `body_armor` | **70**（`hr_body_shinobi_samurai_full`） | **57**（`imperial_scale_armor`） | **+13（强 23%）** |
| 头盔 `head_armor` | **70**（34 件领主阶） | **54** | **+16（强 30%）** |

甲的三档台阶（同一套模型递进）：
`hr_body_shinobi_samurai`（30/15/15，2500 第纳尔）→ `_half`（60/25/25，5000）→ `_full`（**70/30/30**，10000）

头盔的 head_armor 分布：`20`(布头巾) → `30` → `35` → `40` → `45` → `55` → `60` → **`70`（领主阶 34 件）**

🔴 **结论：数值明显超模**。如果我们要参考它的数值设计，**不能照抄**——它的定位是"爽 mod"，我们是"沉浸优先"（铁律 6）。可参考的是**档位划分方式**（同一模型 3 档 / 数值梯度均匀），不是数值本身。

其它部位：腿 `leg_armor` 15→30、手 `arm_armor` 10→30、披风 `hr_shoulder_shinobi_pauldrons_*` body/arm 10→20→30。
另有一组 **`hr_shoulder_invisible_1/2/3`**（`mesh="cla_invisible_mesh"`，body 10/20/20）——**隐形护肩**：只吃防御数值、不显示外观。这是很实用的一招（"我想要这个属性加成但不想看见它"）。

### 2.6 武器

`items_shinobi_weapons.xml` = **16 个直写数值 `<Item>` + 38 个 `<CraftedItem>`**（37 自建 + 1 个替换原版 `western_spear_3_t3`）。

**A. 直写数值的 16 件**（这部分数值**可直接读**）：

| id | 类型 | 关键数值 |
|---|---|---|
| `hr_weapon_fist_regular` | OneHandedSword | 挥/刺 **300/300**（最强，配合空手 ×5） |
| `hr_weapon_fist_{qi,qi_red,lightning,fire,earth,wind}` | OneHandedSword | 100/100 ×6 件（元素拳） |
| `ca_throwing_bomb` | Stone | 刺 150，`stack_amount=5`，`AffectsArea` + `CanKnockDown` |
| `hr_weapon_kunai` / `_enhanced` / `_3` | Stone + OneHandedSword（**双组件**） | 近战 130~150 / 投掷 50~70，`stack_amount=5` |
| `hr_weapon_shuriken` / `_5` / `_10` | ThrowingKnife | 投掷 30，`ammo_limit` 30/20/20 |
| `hr_weapon_shuriken_large` / `_2` | Stone | 投掷 **150**，`stack_amount=3` |

**B. `<CraftedItem>` 37 件**（🔴 **XML 里查不到成品数值**——由锻造模型按 `damage_factor` 运行时算）：

| id | 模板 | 总长 | 刀身重量 |
|---|---|---|---|
| `hr_weapon_tanto` 短刀 | `Hr_Dagger` | 39.5 | 0.35 |
| `hr_weapon_wakizashi` 胁差 | `Hr_OneHandedSword` | 80.1 | 0.6 |
| `hr_weapon_ninjato` 忍者刀 | `Hr_TwoHandedSword` | 100.1 | 0.75 |
| `hr_weapon_katana` 打刀 | `Hr_TwoHandedSword` | 105.8 | 1.5 |
| `hr_weapon_nodachi` 野太刀 | `Hr_TwoHandedSword` | **142.5** | 2.5 |
| `hr_weapon_yari` 枪 | `Hr_TwoHandedPolearm` | **207.1** | 0.28 |
| `hr_weapon_naginta` 薙刀 | `Hr_TwoHandedPolearm` | 239.6 | 0.5 |

**每个刀种 5 个附魔变体**（`_qi` / `_qi_red` / `_qi_void` / `_lightning` / `_fire`），**各自有独立 mesh**（`_energy_blue|red|void` 是真实新网格，不是换贴图）——即附魔刀 = 新网格 + 粒子（§5.7）。

**C. 盾 8 件**（`items_shield.xml`）：数值全部相同（`body_armor=3`、刺 65、`hit_points=300`），**只有 mesh 不同**——即"忍者盾"就是各种刀插在手上当盾。

**D. 锻造模板 4 个**：`Hr_TwoHandedSword` / `Hr_OneHandedSword` / `Hr_Dagger` / `Hr_TwoHandedPolearm`，`StatsData` 上限拉到原版顶格（**挥砍/穿刺伤害上限 500**）。
🔴 这个 500 + DLL 的 `XMLMeleeFix`（因子 = 伤害/20）= **25 倍刀刃系数**——这是"日本刀能锻出变态伤害"的完整链条（见 §6.3 的警告）。

### 2.7 种族 / 皮肤 / 脸：**没有新增任何 race**

这是本节最干脆的结论（也是对我们最重要的一条）：

| 文件 | 内容 | 结论 |
|---|---|---|
| `skins.xml` | `<skins>` 节点**为空** | 不新增 skin |
| `skins.xslt` | **只有删除模板**（删掉 20 多个发型 `hair_mesh`） | 目的是精简发型避免头巾穿模，**不新增** |
| `skins_orc.xml`（441KB） | 定义 `race id="orc"` + 10 个年龄段 skin（绿皮、脸模 `head_orc_male_a`） | 🔴 **是兽人种族，与忍者无关**；且**未在 SubModule.xml 声明**、DLL 里 grep 不到 → **孤儿文件** |
| `monsters_mod.xml` | 新增 `orc` / `undead` 两个 monster（各带 `_child`/`_settlement*` 派生共 10 个） | 同样与忍者无关 |
| `snj_bodyproperties.xml` | 5 个 `BodyProperty` 预设（`*_tetsojin`、`tetsojin_oni` 极壮体型） | 🔴 **是"体型/脸部参数预设"，不是脸模资源**——不引入新脸 mesh |

🔴 **即：忍者的"脸"完全靠原版人类 race + BodyProperty 参数。** 他们没走"自建 race"那条重路（对比我们为换头做的 `lwn_` race，见 wheels `campaign-mode.md` §十三）。

**另一条要警惕的全局改动**（`monsters.xslt`）：

```xml
<xsl:template match="Monster[@id='human']/@hit_points">
    <xsl:attribute name='hit_points'>300</xsl:attribute>   <!-- 原版 100 -->
```

🔴 **人类血量 100 → 300（翻三倍），全局生效。** 这是 XSLT 直接改原版 monster 的做法——**能改，但会和所有其它 mod 抢改同一个属性值**（XSLT 是链式覆盖，顺序决定谁赢）。我们若要用这招，必须登记冲突风险。

### 2.8 AssetPackages 清单

| 文件 | 大小 | 内容 |
|---|---|---|
| `pack0.tpac` | **735 MB** | mod 自建外观资产（头盔全家族 + LOD 链、和服、贴图） |
| `pack1.tpac` | **353 MB** | 部分 shinobi 资产（头巾/面具 `_d/_n/_s` + **`_team_c` 队伍色遮罩**、冰锥法术贴图） |
| **合计** | **1.09 GB** | |

mesh 以 `.0` / `.1` / `.lod1.0`~`.lod5.1` 组织 LOD；贴图以 `_d`（漫反射）/ `_n`（法线）/ `_s`（高光）/ `_c`（队伍色遮罩）后缀命名。

---

## 三、兵种体系

### 3.1 一句话结论

🔴 **全模块以「忍者」命名的兵种只有 1 个，而且是土匪**：`looter`（显示名 `Rogue Shinobi`，Lv6，`Culture.looters`）。
**它不能在聚居点招募**，只能作为土匪在野外刷出；升级一跳就并入帝国兵线，**没有更高层的忍者**。

整个 mod 的"忍者化"发生在**外观层**（所有文化的士兵都穿忍者罩衫 + 头巾，见 §2），**不在兵种层**——兵种层只动了一个 looter 和几个土匪 id。

### 3.2 兵种树（完整，就这么大）

```
NPCCharacter.looter   Lv6  "Rogue Shinobi"   ⟨Faction.looters · Culture.looters⟩
  └─→ NPCCharacter.hr_empire_infantry_2   Lv11 "Moonveil Initiate"  ⟨Culture.empire⟩
        └─→ NPCCharacter.hr_empire_infantry_3   （帝国兵线，非忍者）

NPCCharacter.hr_bandits_rogue_samurai   Lv6 "Rogue Ronin"   → 死路
NPCCharacter.mounted_pillager / mounted_ransacker           → 死路
```

三点必须知道：

| 事实 | 证据 | 含义 |
|---|---|---|
| **没有 T2/T3 忍者** | `bandits.xml:8` 唯一升级目标 = `hr_empire_infantry_2`（帝国兵） | 忍者升级是"流出"，不是"成长" |
| **looter 是纯根节点** | 全 ModuleData `grep 'upgrade_target id="NPCCharacter.looter"'` **零命中** | 没有任何兵种能升成忍者 |
| **没有 `troops/troops_bandit.xml`** | `ModuleData/troops/` 只有 6 个原版文化文件 | 忍者没有自己的兵种树文件 |

`hr_bandits_rogue_samurai` 的升级目标是**显式清空**的（`bandits.xml:19-20` 的 `<upgrade_targets></upgrade_targets>`）——半成品。

### 3.3 文化是个空壳（最明显的未完成痕迹）

`Culture.looters`（`ModuleData/cultures/cultures_bandits.xml:21-37`）：

| 字段 | 值 | 说明 |
|---|---|---|
| `basic_troop` / `elite_basic_troop` | 两者都 = `looter` | 🔴 **没有精英线** |
| `is_bandit` | `true` | 土匪文化 |
| `can_have_settlement` | `false` | 不能占城 |
| `color` / `color2` | `0xff8B7C73` | 与所有土匪同色，**无独立配色** |
| **`bandit_boss` / `bandit_chief` / `bandit_raider`** | 🔴 **三个字段全缺** | 对比同文件其它 5 个流亡文化都有完整的 4 层（bandit / raider / chief / boss）——**忍者只有第 1 层** |
| `banner_bearer_replacement_weapons` | 4 把附魔胁差（`wakizashi_qi` / `_qi_void` / `_lightning` / `_fire`） | 旗手自动换附魔刀 |

另有一段**被注释掉**的 `hr_bandits_rogue_samurai_culture`（`cultures_bandits.xml:3-20`，name "Rogue Samurai"）——作者曾想做独立的武士文化，最终放弃、改为复用 `looters`。

### 3.4 派系与队伍模板

两个派系**共用同一个 `Culture.looters`**，靠队伍模板区分兵员：

| 派系 id | 显示名 | 队伍模板 | 配比 |
|---|---|---|---|
| `looters` | **"Looters"（没改名）** | `PartyTemplate.looters_template` | `4~50` × looter |
| `hr_bandits_rogue_samurai` | Rogue Ronin | `hr_bandits_rogue_samurai_partytemplate` | `20` × samurai |

另有：
- `looters_quest_template` — 固定 `15` × looter（任务用）
- `kingdom_hero_party_caravan_ambushers` — `8` ransacker + `16` pillager（商队伏击；这两个兵带 `is_hidden_encyclopedia="true"`，图鉴里看不到）

🔴 **对比**：原版 sea_raiders 等流亡文化都是**多级配比**（bandit 2~36 / raider 0~12 / chief 0~3，boss 队另有配比）。忍者只有**单一兵种一级配比**。

### 3.5 装备：26 套是"随机外观池"，不是等级阶梯

`EquipmentRosters_troops_bandit.xml`（633 行）**全文件只有 2 个 roster**，都属 `Culture.looters`：忍者 52 套（26 战斗 + 26 平民）、Ronin 28 套。

忍者那 26 套战斗装备的构成：

| 槽位 | 内容 | 变化？ |
|---|---|---|
| Body | `hr_body_shinobi_shozoku` | ❌ 26 套全一样 |
| Leg | `hr_legs_shinobi_boots` | ❌ 全一样 |
| Gloves | `hr_gloves_shinobi` | ❌ 几乎全一样（50/52） |
| **Head** | 6 种（`_moon_exile` 后缀各款头带/头巾/斗笠） | ✅ 随机 |
| **Cape** | 10 种（围巾/斗篷/披挂…） | ✅ 随机 |
| **主手** | 苦无/胁差/短刀/忍者刀/打刀 + **法术弹** | ✅ 随机 |

🔴 **最关键的一条：52 套里有 24 套的主手是法术弹**（`ca_magic_qi_red_beam` 4 套 / `lightning_bolt` 4 / `ice_shard` 4 / `fire_bolt` 4 / `shadow_bolt` 2 / `qi_red_bolt` 2 / `earth_bolt` 2 / `air_bolt` 2 —— 合计 24，其余 28 套是冷兵器）。

> 复核命令：`head -420 EquipmentRosters_troops_bandit.xml | grep -o '<Equipment slot="Item0" id="Item\.[^"]*"' | sort | uniq -c`
> （52 套 = 14 苦无 + 8 胁差 + 24 法术 + 2 短刀 + 2 忍者刀 + 2 打刀）

**即：这个 Lv6 的杂兵靠"手里有法术"来拉强度，而不是靠装备等级。** 这也解释了为什么 §5 的 36 个法术物品里有大量出现在 `EquipmentRosters_troops/`（击杀忍者能掉法术）。

> 顺带：这也是**最强的"忍者感"来源**——一个 Lv6 小兵朝你扔火球/冰锥。

### 3.6 招募：忍者不在招募表里

`ca_recruits.xml`（1036 行，12 个兵种带权重）里 **`shinobi` / `ninja` / `looter` / `bandit` / `rogue` 全部零命中**，也没有 `<culture>looters</culture>`。

所以忍者的出现渠道只有两条：
1. **野外土匪生成**（`Faction.looters` + `looters_template` 4~50 人）
2. **商队伏击事件**（8 ransacker + 16 pillager）

**升级是流出方向**（忍者 → 帝国兵），不是获得渠道。

### 3.7 `_exile` 后缀的含义（贯穿全 mod 的关键约定）

🔴 **`_exile` = 流亡/土匪版，无后缀 = 正规军版。**

- `items_shinobi_helm_moon_exile.xml:9` 里物品的 `culture="Culture.neutral_culture"`
- 无后缀的 `hr_helm_shinobi_headwrap_moon` 被**帝国正规军**使用（`EquipmentRosters_troops_empire.xml`）
- **忍者（looter）穿的全是 `_exile` 款**——即"月影幕府的流亡忍者"

这条约定值得记：**同一套外观做两版，靠 `_exile` 后缀和 `culture` 字段区分"体制内 / 体制外"**。我们做 Taikou 的"正规军 vs 浪人/野武士"可以直接照搬这个模式。

### 3.8 命名与人名

- **忍者没有独立人名**。looter / pillager / ransacker 都是普通兵（无 `is_hero`）。唯一名字带 Shinobi 的派系 `hidden_hand`（"Moonveil Demonic Shinobi"）是**原版黑帮小派系只改了显示名**，与 looter 兵种无兵员关系。
- **兵种名/文化名内联在定义处**（`bandits.xml:3` 的 `{=hr_looter}Rogue Shinobi`），**没有进 `module_strings.xml`**——该文件里只有 3 条带 Shinobi 的，全是商店文案。
- 🔴 **这个 mod 完全没有本地化层**（实测）：
  - `hr_looter` / `hr_rogue_shinobi_culture` 两个 key 在**整棵 Modules 树只出现在各自定义处一次**，**没有任何语言文件覆盖**
  - HikageRising **没有 `Languages/` 目录**（任何一级都没有）
  - **后果**：游戏内直接显示英文 fallback——忍者兵种名永远是 `Rogue Shinobi`，中文玩家看到的是英文
  - 对照：语言层惯例位置是 **`ModuleData/Languages/`**（不是模块根）——原版 `Native`、我们的 `LivingWorldNpcs`（含 `CNs/`）与 `Taikou` 都在这个位置
- **全套忍者物品 = 161 个独立 id / 16 个文件**（`items_shinobi_*`）。

### 3.9 统计

| 项 | 数量 |
|---|---|
| 兵种（id 含 shinobi/ninja） | **0** |
| 兵种（用忍者装备或忍者名） | **9**（looter / samurai / pillager / ransacker / 3×caravanmaster / 2×cutscene） |
| 忍者文化 | **1**（`Culture.looters`，空壳） |
| 忍者派系 | **1**（`Faction.looters`） |
| EquipmentRoster | **2** |
| EquipmentSet（忍者） | **52**（26 战斗 + 26 平民） |
| 忍者物品 id | **161**（16 个文件） |
| 招募表条目 | **0** |

---

## 四、战斗招数与动作系统

### 4.1 一句话结论

两条反直觉的结论：

1. 🔴 **忍者招数不是"新建动作集"，而是往原版 `as_human_warrior` 里追加 138 条自定义 action。**
2. 🔴 **"一次投 10 枚手里剑"和"忍者跑得快"都不是动画的功劳**——前者是 DLL 硬编码补发，后者是**改参数**（§4.6）。

另外：**手里剑/苦无没有任何自定义 `item_usage`**，投掷动作 100% 走原版 `throwing_knife`。

### 4.2 🔴 加载机制：`project.mbproj` / soln 体系（本节最重要的一条）

`action_types_mod.xml` / `action_sets_mod.xml` / `item_usages_sets_mod.xml` / `er_*.xml` / `skins_orc.xml` / `monsters_mod.xml` 这些文件**全部不在 `SubModule.xml` 的 `<Xmls>` 里**——它们的真正入口是：

**`ModuleData/project.mbproj`（引擎的 "soln" 体系）**

```xml
<base type="solution">
  <file id="soln_action_sets"  name="ModuleData/action_sets_mod.xml"  type="action_set" />
  <file id="soln_action_types" name="ModuleData/action_types_mod.xml" type="action_type" />
  <file id="soln_item_usage_sets" name="ModuleData/item_usages_sets_mod.xml" type="item_usage_set" />
  <file id="soln_combat_system" name="ModuleData/er_combat_parameters.xml" type="animation_combat_parameters" />
  <!--<file id="soln_monsters" name="ModuleData/monsters_mod.xml" type="monster" />-->   ← 被注释 = 死数据
</base>
```

**这是引擎原生机制，不是 mod 自造**（已在 1.2.12 上验证）：

| 证据 | 位置 |
|---|---|
| 字面量 `ModuleData/project.mbproj` + 报错串 `You cannot add more than %d files for the same xml type on project.mbproj!` | `bin/Win64_Shipping_Client/TaleWorlds.Native.dll` |
| `GetMbprojPath` / `MbprojXmls` / `GetMergedXmlForNative` / `MergeTwoXmls` / `GetXsltPath` | `TaleWorlds.ObjectSystem.dll`（MBObjectManager） |
| 字面量 `soln_action_sets` + `action_set`、`soln_skins`、`soln_animations`、`soln_voice_definitions` … | `TaleWorlds.MountAndBlade.dll` |
| **我们自己的实测注释** | 🔴 `Modules/Taikou/ModuleData/project.mbproj` |

#### 🔴 我们已经在用这个机制（重要）

我们的 `Taikou/ModuleData/project.mbproj` 里已经**逐行记录了实测结论**，并已启用 7 行：

| 已启用 | 用途 |
|---|---|
| `soln_soundtrack` | 主菜单音乐 |
| `soln_skins` | 织田信长专用 race（2026-09-14） |
| `soln_module_sound` | 火器音效（2026-09-16） |
| `soln_item_holsters` | 火器背后斜挂（2026-09-16） |
| `soln_action_sets` + `soln_action_types` | 铁炮装填动画（2026-09-19） |
| `soln_item_usage_sets` | 探针（验证引擎读不读） |

**并且我们已经踩过那个坑**：`action_sets.xml` **逐字拷贝 Native** → 同 soln id 合并出重复定义 → `CreateProcessedActionSetsXMLForNative` 抛 `KeyNotFoundException` **实崩**（2026-09-08）。

**引擎的合并语义**（我们已反编译确认，HikageRising 的行为与此一致）：
- **同 id 的 `action_set` 由 C# 层「追加合并」进第一个** → 往 `as_human_warrior` 加动作是**纯追加，安全**（原版几千个动作不会被挤掉）
- `item_holsters` 的合并**没有去重**，是原样直送 native → **新 id 是硬要求**

🔴 **对我们的直接意义**：想给 Taikou 加"忍者式招数"，只需 ① `action_types.xml` 声明 action ② `action_sets.xml` 绑定 animation ③ `item_usage_sets.xml` 接到 usage —— **而这三行的 soln 注册我们已经开通了**。成本比想象的低得多。

### 4.3 自定义 action 清单（`action_types_mod.xml`）

文件里 198 处 `<action` 字样，**142 条生效、55 条被注释掉**（数数必须剥注释，否则虚高）。

| 分类 | 条数 | 前缀 | 说明 |
|---|---|---|---|
| 火器/法器 | 44 | `act_*_cla` | 火枪/手枪/左轮/炮/栓动步枪/法杖/魔法枪的 ready/release/reload 全套 |
| Gladius 近战招 | 36 | `cla_act_*_1h_gladius*` | 上挑/下刺 × 右式/左式 × 骑乘/平衡，每套 7 条（ready/quick_release/release/quick_blocked/blocked/quick_stuck/stuck） |
| 🔴 **忍术（唯一 `snj` 命名空间）** | **3 生效** | `act_snj_bending_*` | 见下 |
| 长跳循环 | 4 | `act_jump_*_long` | 给 `monster_usage_sets` 用的长距跳 |
| 查克拉/魔法 | 55 | `act_ca_magic_*` | 八系 × ready/release × 变体 |

#### 🔴 忍者专属动作：做好了大半，但绝大部分被注释掉

**生效的只有 3 条**（`action_types_mod.xml:306-308`，注释写着 `<!-- Jutsu -->`）：

```xml
<action name="act_snj_bending_ready_lightning"          type="actt_ready_ranged"   action_stage="as_attack_ready" />
<action name="act_snj_bending_ready_lightning_continue" type="actt_ready_ranged"   action_stage="as_attack_ready" />
<action name="act_snj_bending_release_lightning"        type="actt_release_throwing" action_stage="as_attack_release" />
```

**被注释掉的 `act_snj_*` 共 46 条**：

| 位置 | 内容 |
|---|---|
| `:247-288` | 🔴 **忍者跳跃全套**：`act_snj_jump`、`_loop`、`_forward_loop`、`_backward_loop`、`_left/right_loop`、`_end`、`_end_hard`、`_loop_long`、`_forward_right/left`、各 `_left_stance` 变体 |
| `:295-303` | `act_snj_jutsu_ready_lightning` / `_release_lightning` / `act_snj_reload_lightning`~`_5` |

🔴 **结论：作者做了一整套忍者跳跃动画（含前后左右方向的空中循环），最后全部注释掉了——只留了一条雷系忍术。** 这是这个 mod 最大的一块"未完成"。

### 4.4 action_set 挂载（`action_sets_mod.xml`）

**86 个生效 `<action_set>`，3054 条生效 `<action>`（另有 402 条 `<action>` 躺在 XML 注释里）**；`hr_` 前缀出现 0 次（招数全部用 `cla_`/`ca_`/`snj_` 前缀，`hr_` 只留给美术/物品）。

> 🔴 **读这个 mod 的 XML 必须先剥注释再数数**：`action_sets_mod.xml` 里有 402 条注释掉的 `<action>`（占全部 `<action>` 字样的 12%），`action_types_mod.xml` 里有 55 条注释掉的声明（28%）。**不剥注释地静态统计会把停用内容当成生效内容，虚高 12%~28%**——我第一轮就是这么数错的。

**唯一的"人类"动作集 = `as_human_warrior`，被整段重写**（`:3`）：

```xml
<action_set id="as_human_warrior" skeleton="human_skeleton" movement_system="bipedal">
```

块内 **138 条生效的自定义 action**（另有一批在注释里），忍者相关按注释分段：`<!-- SNJ -->`(`:190`)、`<!-- Jutsu -->`(`:243`)、`<!-- Magic -->`(`:245`)。

**它引用了 135 段不同的动画**（已剥注释统计），其中 **51 段是 `snj_*` 忍者动画**：

| 动画族 | 段数 | 干什么 | 生效？ |
|---|---|---|---|
| `snj_bending_*` | **23** | 元素操控（"御术"）：`ready_earth[_2/_3/_continue]` / `release_shadow[_2/_3]` / `ready_wind` … | ✅ 生效（供 55 条 `act_ca_magic_*` 用） |
| `snj_jutsu_*` | **21** | 忍术释放 | ✅ 生效 |
| `snj_martial_*` | **7** | 体术（含 `_both` 双手） | ✅ 生效 |
| `snj_jump_*` | **24** | 🔴 忍者跳跃全套：`loop_long` / `forward_loop` / `backward_loop_left_stance` / `forwards_roll` / `backwards_roll` / 各 `_end` `_end_hard` | ❌ **全部在注释里，一段都没生效** |

🔴 **这条最值得记**：作者做了 **24 段忍者跳跃动画**（含前/后/左/右空中循环、前滚翻、后滚翻、软/硬落地），**最后一段都没启用**——忍者跳跃是"做完了但关掉了"。真正生效的跳跃只有 4 条走原版动作名（`act_jump_*_long`）的长距跳（§4.6 ④）。

🔴 **关键安全性质**（已逐条验证）：这个 `action_set` **没有 `base_set`、且 0 条原版动作名**（`act_ready_bow` / `act_release_bow` / `act_guard` / `act_ready_swing_right_stance` / `act_ready_thrust_stance` 命中数**全为 0**）→ 属**纯追加**，不触发我们踩过的 `KeyNotFoundException`。

**动画资源命名规律**：

| 前缀 | 含义 |
|---|---|
| `snj_jutsu_*` | 忍术（手/体术释放） |
| `snj_martial_*` | 体术（`_both` = 双手） |
| `snj_bending_*` | 元素操控（"bending"，影射降世神通式的御术） |

**随机变体机制**：每条 action 可带 `alternative_group="ca_jutsu_<元素>_alt"`（8 个组）→ 同一招随机播不同动画。**这是"打起来不重样"的低成本手法，值得抄。**

**继承链**：`as_orc_warrior`（`base_set="as_human_warrior"`）→ `as_orc_female_warrior` → …共 18 级一级链 + 70 多个 orc 变体集（村民/酒馆/儿童/守卫/舞者/领主/乞丐/各种搬运姿态）。

⚠️ **orc 链里重定义了原版 action 名**（如 `act_run_forward_1h`）——**落在新 action_set id 上是安全的，但挪进 `as_human_warrior` 就会撞上 KeyNotFoundException**。这是我们今后加动作时必须守的线。

### 4.5 投掷招数：手里剑/苦无**没有**任何自定义 usage

`item_usages_sets_mod.xml` = 26 个 `item_usage_set`，**全是新 id（纯追加）**，`hr_` 只有 1 处：

| 类别 | usage set |
|---|---|
| 火器 | `cla_musket` / `_fast` / `cla_pistol` / `cla_revolver` / `cla_cannon` / `cla_flint_rifle` / `cla_bolt_rifle` / `cla_automatic_rifle` / `cla_breechloader_rifle` |
| 魔法 | `cla_magic_energy_2h` / `_fire_2h` / `_water_2h` / `_lightning_2h` / `_earth_2h` / `_shadow_2h` / `_wind_2h` / `_holy_2h` / `cla_bending_lightning_2h` + 枪/杖/2h 枪 |
| 近战 | `cla_onehanded_thrust_usage`、`ca_bayonet_usage`、`ca_bayonet_thrust_usage`、`cla_bomb` |
| **`hr_no_weapon`** | 🔴 **唯一的 hr_ usage**，整块只有 `<idles>`（空壳）——给隐形武器/空手物品占位用 |

**手里剑/苦无 `shuriken|kunai|ninjato|wakizashi|hr_weapon|ninja|shinobi` 命中 0** → 它们用 `<Weapon item_usage="throwing_knife">`，即**原版** usage（定义在 `Native/ModuleData/item_usage_sets.xml`）。**HR 只改物品层**（mesh / flying_mesh / damage / particle / rotation）。

🔴 **两层解耦的设计（值得学）**：

```
物品 → item_usage（"用哪套动作"）  →  usage 的 ready_action / release_action（"调哪个 action 名"）
                                            ↓
                                     action_set 里 action 名 → animation（"播哪段动画"）
```

**`usage` 层只引用 action 名字，动画绑定全在 `action_set` 层。** 所以换动画不用动物品，换物品不用动动画。

**`usage` 的字段口径**（全文件统计）：`style`×118、`require_free_left_hand`×54、`is_mounted`×54、`strike_type`×42、`ready_action`/`release_action`/`ready_continue_ranged_action` 各×42、`reload_action`/`reload_continue_action` 各×24、`defend_*`×12+12+12。
🔴 **注意：`usage` 里没有 `weapon_flags` / `shoot_animation` / `skeleton`**——这些概念不在这一层。

魔法 usage 的标准形态（每个 `cla_magic_*_2h` 同构）：

```xml
<item_usage_set id="cla_magic_fire_2h" has_single_stance="true" ...>
  <flags><flag name="requires_no_shield" /></flags>
  <idles><idle action="act_idle_unarmed_1" is_left_stance="False" require_free_left_hand="False" /> ...</idles>
  <movement_sets><movement_set id="onehanded_swing_cantblock" /></movement_sets>
  <usages>
    <usage style="attack_any" ready_action="act_ca_magic_ready_fire"
           release_action="act_ca_magic_release_fire"
           ready_continue_ranged_action="act_ca_magic_ready_fire_continue"
           is_mounted="False" require_free_left_hand="true" strike_type="thrust" />
    <usage ... is_mounted="True" ... />     ← 骑乘另有一套
  </usages>
</item_usage_set>
```

配套的移动集：`movement_sets.xml` 新增 4 集（`ca_walk_magic` / `_left_stance` / `ca_run_magic` / `_leftstance`），**idle = `act_ca_magic_casting_2h_idle`** → 拿着法术弹走路时是"施法姿态"而不是"持械姿态"。这套在 `full_movement_sets.xml` 里绑到 walking/running/crouch。

### 4.6 🔴 "忍者手感"的真正来源：改参数，不是做动画

这是本节最实用的一条。三份参数文件：

#### ① `er_native_parameters.xml` — 195 条 id 与原版一致，**改了 13 个值**

| 参数 | HR | 原版 | 效果 |
|---|---|---|---|
| `bipedal_speed_multiplier` | **13.0** | 6.2 | 🔴 移速翻倍 |
| `bipedal_sprint_speed_ratio` | 0.5 | 0.37 | 冲刺更快 |
| `bipedal_jump_end_speed_multiplier` | **25.0** | 0.75 | 🔴 起跳末速 33 倍 |
| `bipedal_jump_end_hard_speed_multiplier` | **25.0** | 0.1 | 同上（重落地） |
| `jump_cooldown` | **0.0** | 1.6 | 🔴 跳跃无冷却（可连跳） |
| `ready_speed_multiplier` | 4.0 | 2.2 | 起手快 |
| `release_speed_multiplier` | 1.8 | 1.2 | 收招快 |
| `defend_speed_multiplier` | 4.5 | 3.2 | 防御快 |
| `minimum_defend_duration` | 0.35 | 0.25 | 格挡持续更久 |
| `running_sides_speed_multiplier` | 0.8 | 0.65 | 侧移快 |
| `running_backward_speed_multiplier` | 0.9 | 0.5 | 后退快 |
| `bipedal_combat_speed_deceleration_blend_multiplier` | 0.9 | 0.6 | 战斗降速少 |
| `on_weapon_hit_slow_down_factor_swing` | 0.1 | 0.6 | 🔴 命中后几乎不减速（砍完能立刻接下一招） |

**→ 一句话：忍者感 = 跑得快 + 跳得高 + 无跳跃冷却 + 起手收招快 + 命中不卡顿。全是数值。**

#### ② `ca_managed_core_parameters.xml`（DLL 直读，非覆盖原版文件）

由 `CAHarmonyPatches` 劫持 `Game.InitializeParameters` 送进 `ManagedParameters.Instance.Initialize(...)`。关键值：

| 参数 | 值 | 效果 |
|---|---|---|
| `FistFightDamageMultiplier` | **25.0** | 🔴 拳脚伤害 ×25（配合 §5.6 的隐形拳套） |
| `FallDamageMultiplier` | **0.0** | 🔴 **免摔伤**（配合忍者跳） |
| `FallDamageAbsorption` | 100.0 | 同上 |
| `AirFrictionKnife` / `AirFrictionAxe` | 0.007 | 手里剑/苦无飞行手感 |
| `AirFrictionJavelin` / `Arrow` / `Bullet` | 0.002 / 0.003 / 0.004 | 其它弹体 |
| `MissileMinimumDamageToStick` | 3.0 | 插住阈值 |
| `ProjectileMaxPenetrationSpeed` | 120.0 | 穿透 |
| `ShieldPenetrationOffset` / `Factor` | 30.0 / 3.0 | 破盾 |
| `StunPeriodAttackerSwing` / `Thrust` | 0.1 / 0.67 | 打击硬直 |
| `BipedalRangedReloadSpeedMultiplier` | 1.95 | 远程装填近 2 倍 |
| `HeavyAttackMomentumMultiplier` | 1.15 | 重击动量 |

#### ③ `er_combat_parameters.xml`（140KB）— **原版文件的整份改写副本**

**152 条 `<combat_parameter>` 的值与原版不同**（id 集合相同）。改的是**逐招式的动画时序 / 旋转限位 / 碰撞窗口**：

| 改什么 | 例子 |
|---|---|
| 碰撞窗口 | `collision_check_starting_percent` / `_ending_percent` / `collision_damage_starting_percent` |
| 视角旋转限位 | `vertical_rot_limit_multiplier_up/down`、`left/right_rider_rot_limit`、`rider_look_down_limit` |
| 命中骨骼 | `hit_bone_index="27"`、`shoulder_hit_bone_index="21"` |
| 武器偏移 | `weapon_offset` |
| 连招冷却 | `alternative_attack_cooldown_period` |
| 🔴 自定义碰撞胶囊 | 3 条带 `<custom_collision_capsule p1="..." p2="..." r="0.1" />`（`weapon_bash_params_1h/spear/2h`）——**给武器撞击单独定制碰撞体形状** |

配套 98 个 `<def>` 常量（`narrow/wide/widest_vertical_rot_limit`、`fist_vertical_rot_limit=0.3`、`swing_vertical_rot_limit=0.75`、各种 `*_bash_cooldown`）。

🔴 **`hr_` / `shinobi` / `ninja` 在此文件命中 0** → 忍者**没有专属战斗参数**，全靠复用（`fist_*` 给空手、`throwing_*` 给投掷）。

#### ④ `monster_usage_sets.xslt` — "忍者长距跳"的落地点

用 XSLT 给原版 `monster_usage_set[@id='human']` **追加 4 条 `monster_usage_jump`**：

```xml
act_jump_left_loop_long / act_jump_right_loop_long /
act_jump_backward_loop_left_stance_long / act_jump_backward_loop_long   全 is_hard="True"
```

**→ 即：把原版的"跳跃"扩展成"带方向的长距跳跃循环"**。这是纯 XSLT 增量，不需要改 monster 定义本身。

### 4.7 死数据与半成品清单（避坑用）

| 项 | 状态 | 说明 |
|---|---|---|
| `monsters_mod.xml`（orc/undead 共 10 个 Monster） | 🔴 **死数据** | 唯一注册行在 `project.mbproj` 里被注释掉；`SubModule.xml` 也没有 |
| 但 `as_orc_*` 等 70+ 个 action_set | ✅ **照常加载** | 走 `soln_action_sets` → 现状是"一堆没人用的 orc 动作集" |
| `act_snj_jump_*` 等 46 条忍者跳跃/忍术 | 🔴 **注释掉** | 作者做完又关了 |
| `skins_orc.xml`（441KB 兽人种族） | 🔴 **孤儿** | 未在 SubModule 声明，DLL 里 grep 不到 |
| `er_combat_parameters.xml` 里的 `hr_` | 不存在 | 忍者无专属战斗参数 |
| `monsters_mod.xml` 里的 `action_set="as_orc_warrior"` 等引用 | 指向已加载的集 | 但因怪物本身没加载，这些引用无效 |

🔴 **这张表的价值**：告诉我们"文件存在 ≠ 生效"。**我们排查自己的内容包时同样适用**——一个 XML 没进任何注册体系（SubModule Xmls 或 project.mbproj）就是死的，静态检查查不出来。

### 4.8 落地要点（给我们）

| 要点 | 说明 |
|---|---|
| **加招数只需 3 个文件** | `action_types.xml`（声明）→ `action_sets.xml`（绑 animation）→ `item_usage_sets.xml`（接 usage）；**soln 注册我们已开通** |
| **往 `as_human_warrior` 加动作是安全的** | 引擎 C# 层追加合并；但**禁止逐字拷贝 Native**（KeyNotFoundException，我们已踩过） |
| **换动画不用动物品** | usage 只引 action 名，动画绑定在 action_set 层——两层解耦，改哪层动哪层 |
| **"手感"优先改参数** | 忍者感 90% 来自 §4.6 的 13 个参数，不是动画。做任何"某种兵种感觉不一样"的需求，先想参数 |
| **`alternative_group` 随机变体** | 同一招挂多个动画随机播，零成本解决"打起来重样" |

---

## 五、忍术 / 法术体系（「忍术馆」）

### 5.1 一句话结论

**没有施法系统。36 个"法术"全是 `Type="Thrown"` 的投掷武器**，`weapon_class` 只用原版的 `Stone` / `Boulder`。
"法术感"由四件套拼出来：

| 件 | 值 | 作用 |
|---|---|---|
| `mesh` | `cla_invisible_holster` | **手持时看不见**（空手施法的观感） |
| `flying_mesh` | `hr_energy_sphere_small` 等 | **飞出去才现形** |
| `trail_particle_name` | `cla_energy_blue_throwing_small` 等 | 拖尾 |
| `item_usage` | `cla_magic_energy_2h` 等 | 换掉 ready / release 动作 |

再加 DLL 侧的两条：**手持时挂粒子**（§1.2 `CAItemParticleEffect`）+ **命中时爆点**（`CADamageParticleModel`）。

### 5.2 命名规律与家族全表

**规律 = `ca_magic_<元素>_<形态>[_multi_N]`**

| 元素 | bolt 弹 | ball 球 | beam 光束 | 备注 |
|---|---|---|---|---|
| `qi` 蓝气 | ✔ + `_multi_5` | ✔ + `_multi_5` | ✔ + `_beam_large` | 主元素 |
| `qi_red` 红气 | ✔ + `_multi_5` | ✔ + `_multi_5` | ✔ + `_red_beam_large` | |
| `fire` 火 | ✔ + `_multi_5` | ✔ + `_multi_3` | ✘ | |
| `lightning` 雷 | ✔ + `_multi_5` | ✔ + `_multi_3` | ✘ | 弹速 180（最快弹） |
| `earth` 土 | ✔ + `_multi_5` | ✔ + `_multi_3` | ✘ | 伤害类型 Blunt |
| `ice` 冰 | ✘ | ✘ | ✘ | 形态是 `shard`(+`_multi_5`) / `spike`(+`_multi_4`) |
| `air` 风 | ✔ + `_multi_5` | ✔ + `_multi_3` | ✘ | 伤害类型 Blunt |
| `shadow` 影 | ✔ + `_multi_5` | ✔ + `_multi_3` | ✘ | 弹药量最大 40 |
| ~~`holy` 圣~~ | **整段被注释停用** | ✘ | ✘ | 物品、音效、物理材质残留 |

**共 36 个生效条目**（`ModuleData/items/items_magic.xml:502` 起）。

### 5.3 三种形态的实现方式（含关键数值）

| 形态 | 实现手法 | 关键数值 |
|---|---|---|
| **bolt 弹** | 普通投掷弹 | `weapon_length=24`、`missile_speed=60`、`stack_amount=25`、伤害 50、`value=2000` |
| **ball 球** | 真实物理弹 + 击倒 | `stack_amount=3`、伤害 80~100、`weapon_length=61~150`、加 `MissileWithPhysics` + `CanKnockDown` + 自旋 `rotation_speed` |
| **beam 光束** | 🔴 **一根 20.68 米长的弹** | `weapon_length=2068`、`missile_speed=300`（是普通弹 5 倍）、伤害/弹药/价格与 bolt 相同 |
| `beam_large` | 上面的 AOE 版 | `stack_amount=3`、`AffectsArea="true"`、伤害 70 |

> **"光束"的真相**：不是射线、不是持续伤害，就是**一根超长超快的模型掠过**。这个取巧手法值得记一笔——但它只在"掠过去"的观感上成立，命中判定仍是单点。

### 5.4 `_multi_N` 的 N 在哪里？（重要）

**N 完全不在 XML 里。** 对比结果：

- `ca_magic_qi_bolt` vs `ca_magic_qi_bolt_multi_5`：`stack_amount` / `thrust_damage` / `missile_speed` / `weapon_length` **逐字相同**，唯一差别是多一个 `WeaponFlags MultiplePenetration="true"`。
- `ca_magic_qi_ball` vs `_multi_5`：**连 `<WeaponFlags>` 都逐字相同**。

**真正的弹数硬编码在 DLL**：`CAShotgunEffectMissionLogic` 的 `NumProjectiles` 是**额外补发数**（`AddCustomMissile` 凭空生成、不扣弹药）：

| 物品名 | DLL `NumProjectiles` | 玩家实际看到的总弹数 |
|---|---|---|
| `..._multi_5` | 4 | **5 发** |
| `..._multi_4`（ice_spike） | 3 | 4 发 |
| `..._multi_3` | 2 | **3 发** |
| `hr_weapon_shuriken_10` | 9 | **10 枚** |
| `hr_weapon_shuriken_5` | 4 | 5 枚 |
| `hr_weapon_kunai_3` | 2 | 3 枚 |

**代价**：一发弹药 = N 个弹体，`_multi_5` 的 25 发弹药袋 = 125 个弹体。

⚠️ **数据侧的三处不一致**（照抄会踩）：
1. `_multi_3` 的物品**名字写成 "x 2"**（`ca_magic_fire_ball_multi_3` 名叫 `[Magic]Fire Ball x 2`），实际发 3 发。
2. `ca_magic_qi_red_ball` 与它的 `_multi_5` 版**用了不同的 `physics_material`**（`cla_explosion` vs `cla_qi_red_explosion`）。
3. `hr_weapon_shuriken_5` / `_10` 的 `ammo_limit`（20）**反而比基础版**（30）**小**，`_10` 还把 `thrust_speed` 从 150 降到 102。

### 5.5 忍具（手里剑 / 苦无）——与法术是两套写法

| | 手里剑 `hr_weapon_shuriken*` | 苦无 `hr_weapon_kunai*` |
|---|---|---|
| `weapon_class` | `ThrowingKnife` | `Stone` |
| 弹药字段 | `ammo_limit="30"`（`_5`/`_10` 为 20） | `stack_amount="5"` |
| `<Weapon>` 组件数 | 1（纯投掷） | **2（投掷 + `OneHandedSword` 近战）** |
| 伤害 | 投掷 30 | 近战 130~150 + 投掷 50~70 |
| `item_usage` | 原版 `throwing_knife`（未改） | 投掷原版 / 近战自定义 `cla_onehanded_thrust_usage` |
| 特殊字段 | `rotation="180,0,0"`、`trail_particle_name="hr_particle_shuriken_trail"`、`sticking_position/rotation`、`UnloadWhenSheathed`、`AmmoSticksWhenShot` | 同左 + `passby_sound_code`（掠过音） |

🔴 **苦无的双 `<Weapon>` 组件是原版投掷武器没有的写法**——一件物品同时是飞刀和匕首。这是"忍者苦无"手感的关键，也是我们做忍具/手里剑类物品时可直接抄的范本。

🔴 **忍具与法术的差异是刻意的**：忍具**不用**新物理材质（走 `metal_weapon` / `ballista_missile`），所以**会插在目标身上**（有实感）；法术用新材质 `dont_stick_missiles="true"`，**命中即消失**（有"能量体"观感）。

### 5.6 拳法 / 掌法（体术）

`items_shinobi_weapons.xml:19-262` 的 7 个 `hr_weapon_fist_*` **是近战武器不是法术**：

- `Type="OneHandedWeapon"` + `weapon_class="OneHandedSword"` + **`mesh="cla_invisible_holster"`**（看不见的武器）
- `item_usage="hr_no_weapon"` = **空手动作**
- 伤害 100~300，`<Flags Burning="true" CanPenetrateShield="true">`（火拳能穿盾）
- 配合 `NoFriendlyFireDamageModel` 的**空手伤害 ×5**，才有"体术"的手感

**即：拳法 = 空手动作 + 隐形武器 + 元素粒子。** 同理"元素刀"（`hr_weapon_katana_fire` 等）也是**同一把刀换 mesh/粒子**，不是新武器类。

### 5.7 附魔刀剑的粒子表（`hr_weapon_*`）

`CAItemParticleEffect` 里 5 个刀种 × 5 种元素，**粒子挂在武器骨骼上（骨索引 27）**：

| 刀种 | 元素变体 |
|---|---|
| `wakizashi` 胁差 | `qi` / `qi_red` / `qi_void` / `fire` / `lightning` |
| `ninjato` 忍者刀 | 同上 5 种 |
| `katana` 打刀 | 同上 5 种 |
| `nodachi` 大太刀 | 同上 5 种 |
| `naginta` 薙刀 | 同上 5 种 |
| `yari` 枪 | `qi` / `qi_red` 两种 |
| `fist` 拳 | `qi` / `qi_red` / `lightning` / `fire` / `earth` / `wind` |

粒子名规律：`cla_{qi_blue|qi_red|energy_red|darkness|fire|lightning}_effect_sword{_short|_long|_curved}`——**同一个刀种共用一套粒子，靠刀种后缀区分尺寸**。

另外有 4 条 `hr_shield_*`（盾牌挂粒子，骨索引 **20**）——即**盾也能带元素光效**，骨索引与武器不同。

### 5.8 音效与物理材质

**音效**（`ModuleData/module_sounds_mod.xml` + `ModuleSounds/` 56MB）：
- 按元素分目录：`energy/` `flame/` `water/` `wind/` `undead/` `firearms/` `firearms_bolt/`
- 统一格式 **48kHz / 16bit / 立体声 PCM WAV**（无损、体积大：单文件 0.3~1.4 MB）
- ⚠️ **一处数据隐患**：`ca_airbending` 在 `:46`（单文件）与 `:62`（4 个 variation）**重复注册**同一 StringId，实际生效取决于加载顺序
- ⚠️ 反向残留：`cla_magic_holy` 有音效定义，但它对应的物品被注释停用了

**物理材质**（`ModuleData/physics_materials.xml`，新文件非 patch）：新增 16 个，**属性值 100% 相同、只有 id 不同**：

```
dont_stick_missiles="true"     ← 唯一的功能性差异
static_friction=0.700  dynamic_friction=0.300  restitution=0.400
```

**用途**：让法术弹命中后**不插在目标身上**（配合 `AmmoBreaksOnBounceBack` + `<Flags QuickFadeOut="true">` 三者合一 = 命中即消散）。
**注意**：这些材质**不带音效/穿透定义**，值就是标准爆炸类参数——所以"爆点音效"其实是 DLL 侧粒子 + 引擎默认命中音，不是材质配的。

### 5.9 「忍术」怎么获得（四重渠道）

1. **忍术馆**（主要渠道）：城镇菜单新增「Shinobi Technique Pavilion 忍术技术馆」（`ArmouryBehavior` + `GameTexts` 键 `cla_armoury_enter`）。
   商品清单 = `ModuleData/CustomValues/ca_armoury.xml`，**510 件**，全部 `settlementCulture=any` / `kingdomCulture=any` / `factionOnly=false`。
   > ⚠️ 文案里写了"只有与本派结盟者才能窥见更深奥的技艺"，但**数据侧 `factionOnly` 一个 true 都没有**——门派限制是**写了没启用**。
2. **文化物品池**：6 个文化各挂 1 条法术 + 手里剑（`cultures_*.xml:80-83`）→ 进常规市场/锻造池。
3. **NPC 装备掉落**：`EquipmentRosters_troops/` 与 `_npc/` 里大量 `<Equipment slot="Item1" id="Item.ca_magic_*"/>` → **击杀带法术的兵可以掉**。
4. **锻造不适用**：法术与忍具**都没有 `crafting_template`**，不可锻造（锻造只覆盖日本刀/长杆，见 §6）。

### 5.10 模块字符串的处理方式（值得注意的反面教材）

🔴 **法术与忍具的名称全部内联写在物品定义里**（`name="{=ca_magic_qi_bolt}[Qi]Qi Bolt"`），**没有进 `module_strings.xml`**。
后果：本地化词表散落在各物品文件里，翻译方要翻 36 个物品文件才能拿到全部词条。
**我们的做法更好**（铁律 13：走 `LWNTextHelper` + 统一语言 XML），**不要学这个**。

---

## 六、与我们项目的对照：能直接拿走的四个能力

### 6.1 我们有 / 没有（已逐条 grep 核实）

| HikageRising 的能力 | 我们已有的对应物 | 差距 |
|---|---|---|
| `CAItemParticleEffect` 手持挂粒子 | `Combat/FirearmFxRegistry.cs` + `FirearmFxLogic.cs`（**数据驱动、按 ammoClass**，比它先进） | 🔴 **他们挂在骨骼上做常驻光效**（武器/盾），我们只在开火瞬间放枪口粒子 → **能力缺失** |
| `CADamageParticleModel` 命中爆点 | 我们 `CreateBurstParticle` 只用在调试命令（`MyCommands.cs:1336`）与剧本演出（`StageDirector.cs:446`） | 🔴 **战斗命中链未接** → 能力缺失 |
| `CAShotgunEffectMissionLogic` 多发弹幕 | **无** | 🔴 **完全缺失** |
| `XMLMeleeFix` 伤害因子补丁 | **无** | 🔴 **完全缺失**（我们的自定义武器伤害走的是原版因子算法） |
| 🔴 **自定义战斗动作**（138 条追加进 `as_human_warrior`） | ✅ **soln 注册已开通**（`Taikou/ModuleData/project.mbproj` 已启用 `soln_action_sets` / `soln_action_types` / `soln_item_usage_sets`，2026-09-19） | **不是缺失，是没用起来**——机制与坑我们都摸清了 |
| `er_native_parameters` 参数改手感（移速/跳跃） | **无** | 可选，但**"忍者感"主要来自这里**（§4.6） |
| `CAStatModel` 技能→移速 | **无** | 可选（轻量，10 行） |
| `NoFriendlyFireDamageModel` 空手 ×5 | **无** | 可选（体术玩法用） |

**核实命令**（三个能力在我们代码库里 0 命中）：
```
grep -rn "AddComponentToBone\|CreateParticleSystemAttachedToBone\|CollisionBoneIndex" --include=*.cs .
grep -rn "AddCustomMissile" --include=*.cs .
grep -rn "SwingDamageFactor\|ThrustDamageFactor\|SetDamageFactors" --include=*.cs .
```

### 6.2 三个能力的 API 双端可用性（**已实测，可直接移植**）

| API | 1.2.12 | 1.5.1 | 结论 |
|---|---|---|---|
| `Mission.AddCustomMissile(...)` | `void` | `Missile` | 🔴 只有**返回值**不同；**不接返回值（当语句调用）就无需版本分支** |
| `MBAgentVisuals.CreateParticleSystemAttachedToBone(string/int, sbyte, ref MatrixFrame)` | ✔ | ✔ | 两端一致 |
| `MBAgentVisuals.AddChildEntity(GameEntity)` | ✔ | ✔ | 两端一致 |
| `Skeleton.AddComponentToBone(sbyte, GameEntityComponent)` | ✔ | ✔ | 两端一致 |
| `Scene.CreateBurstParticle(int, MatrixFrame)` | ✔ | ✔ | 两端一致 |
| `ParticleSystemManager.GetRuntimeIdByName(string)` | ✔ | ✔ | 两端一致 |

> 即 **`AddCustomMissile` 不需要写 `V.xxx()` 版本分支**——这条省掉一次三锚点核对。

### 6.3 移植时的设计建议（基于本次分析）

1. **弹数/粒子表走数据契约，不要硬编码字典**。
   HikageRising 三个类各有一个 80 行的 `Dictionary` 写在构造函数里；我们已有 `FirearmFxRegistry` 那套「内容包放 `ModuleData/AssetRegistry/*.xml`、基座读取」的范式（见 [自定义世界内容包从零起步必备清单](自定义世界内容包从零起步必备清单.md)）。
   **建议**：新增的附魔光效表与多发弹幕表均按 `FirearmFxRegistry` 的样式做**内容包可加行**的 XML 契约（基座零改动）。

2. **多发弹幕必须与弹药消耗一起设计**。
   HikageRising 的 `AddCustomMissile` **不扣弹药**——所以「一次投 10 枚手里剑」实际只花 1 发弹药。这是它的设计取舍（爽感优先），但**如果我们要做"命中率/资源"平衡，需要自己补扣弹药逻辑**。

3. **骨骼挂粒子要处理「换手 / 收刀 / 死亡」三个时机**。
   HikageRising 的做法值得抄：**每帧 diff「双手持物的 StringId」，变了才重挂**（`_lastHeldItems` 字典），而不是每帧重挂。它同时也在 `RemoveParticleEffects` 里显式 `RemoveComponent`——不清理会残留（尤其 agent 死亡后视觉残留）。

4. 🔴 **做"门派/势力配色"之前先选路线**（§2.3）：
   - **要跟队伍色变** → 一个 mesh + 一张 `_c` 遮罩贴图 + `UseTeamColor="true"`，**几乎零成本**
   - **要门派固定色** → 只能每个配色烘一套独立 mesh + 贴图（HikageRising 花了 1 GB 走的就是这条）
   **这两条路不能混**：`UseTeamColor` 只认队伍色，不会因为你换了 culture 就变门派色。

5. **`_exile` 模式值得直接抄**（§3.7）：
   同一套外观做两版——本体版（体制内/正规军用）+ `_exile` 版（流亡/土匪用），靠 `mesh` 后缀 + `culture` 字段区分。
   **映射到 Taikou**：「幕府正规军 vs 浪人/野武士」「大名旗本 vs 落武者」用同一套底模 + 两套配色即可，**不需要做两套完全独立的装备**。

6. 🔴 **"手感"优先改参数，不要先想着做动画**（§4.6）：
   HikageRising 的"忍者感"90% 来自 13 个数值（移速 ×2、跳跃无冷却、落地加速 33 倍、起手收招快 2 倍、命中不减速）+ `FallDamageMultiplier=0`。
   **做任何"某兵种感觉不一样"的需求，第一站是 `native_parameters` / `managed_core_parameters`，最后一站才是新动画。**

7. ⚠️ **两个"数值炸弹"要警惕，别照抄**：
   - `XMLMeleeFix` 让 `伤害因子 = XML伤害值/20`，配合锻造模板上限 500 = **25 倍刀刃系数**。我们要用这个补丁，必须**同时收紧自己的伤害数值**，否则武器会变成斩铁如泥。
   - `monsters.xslt` 把人类血量 100→**300**（全局、XSLT 抢改原版属性）。我们若要用，必须登记与其它 mod 的覆盖冲突风险。

8. ⚠️ **`AddCustomMissile` 的返回值差异**（唯一一条版本坑）：
   1.2.12 返回 `void`、1.5.x 返回 `Missile`。**当语句调用（不接返回值）就不用写 `V.xxx()` 分支**；一旦想接返回值，就必须加三锚点分支。

---

## 分析完整度

| 章节 | 状态 | 依据 |
|---|---|---|
| §1 模块构成与代码地图 | ✅ 完整 | ilspycmd 全量反编译（11 个类 1874 行，逐个读） |
| §2 忍者外观 | ✅ 完整 | 13 个头盔文件 + 420 个 mesh + tpac 只读扫描 + 原版数值对照 |
| §3 兵种体系 | ✅ 完整 | bandits / cultures / spclans / partyTemplates / rosters / ca_recruits 全量巡检 |
| §4 战斗招数与动作 | ✅ 完整 | action_types / action_sets / item_usages_sets / 三份参数文件 / project.mbproj |
| §5 忍术 / 法术体系 | ✅ 完整 | items_magic / physics_materials / module_sounds / ca_armoury 全量巡检 |
| §6 与我们项目的对照 | ✅ 完整 | 本仓库 grep 实证 + 1.2.12/1.5.1 双端 DLL 反编译验证 |

### 本次分析中修正/否定的原始假设

| 假设 | 实际 | 依据 |
|---|---|---|
| "8 个头盔变体" | 实为 **6 个门派色家族 × (本体 + 流亡版) = 12 + 1 基础 = 13 个文件** | §2.3 |
| 换色靠材质/队伍色 | **靠每个颜色一份独立 mesh + 贴图**；`UseTeamColor` 只是第二层叠加 | §2.3（tpac 实证） |
| 忍者有完整兵种树 | **只有 1 个 Lv6 土匪兵**，文化缺 boss/chief/raider 三层 | §3.1–3.3 |
| 法术是自定义武器类 | 全是 `Type="Thrown"`，`weapon_class` 只用原版 `Stone`/`Boulder` | §5.1 |
| `_multi_N` 的 N 写在 XML 里 | **XML 里完全没有 N**，纯 DLL 硬编码字典 | §5.4 |
| 忍术靠新动作集 | **往原版 `as_human_warrior` 追加 138 条 action** | §4.4 |
| "一次投 10 发"是动画/XML | **DLL 在 `OnAgentShootMissile` 里补发 9 发** | §5.4 / §4.3 |
| 忍者感来自动画 | **90% 来自 13 个参数改动**（移速/跳跃/起手） | §4.6 |
| 有忍者专属种族/脸 | **没有**——`skins.xml` 空、`skins_orc.xml` 是兽人且未注册 | §2.7 |

### 数据隐患清单（照抄会踩）

| 位置 | 问题 |
|---|---|
| `module_sounds_mod.xml:46` 与 `:62` | `ca_airbending` **重复注册**同一 StringId，生效取决于加载顺序 |
| `items_magic.xml` | `ca_magic_holy_bolt` 整段被注释，但 `ca_armoury.xml` / 音效表 / 物理材质仍留残留 |
| `items_magic.xml` | `_multi_3` 的物品**名字写成 "x 2"**（实际发 3 发） |
| `items_magic.xml` | `ca_magic_qi_red_ball` 与其 `_multi_5` 版**用了不同 physics_material** |
| `items_shinobi_weapons.xml` | `hr_weapon_kunai_3` 名"x3"但 `stack_amount=5`、投掷 `weapon_length=10`（其余苦无 49） |
| `items_shinobi_weapons.xml` | `hr_weapon_shuriken_large_2` 名"x2"但 `stack_amount=3` |
| `items_shinobi_helm_*.xml` | 同款 `headwrap_full`：dusk 是 `hair_cover_type="all"`，moon 是 `"none"`（疑笔误） |
| `items_shinobi_helm_*.xml` | `hr_helm_eboshi_primary` 数值漂移：基础文件 55、颜色变体 60 |
| `project.mbproj` | `monsters_mod.xml` 注册行被注释 → orc/undead **死数据**，但 `as_orc_*` 一族动作集照常加载 |
| `skins_orc.xml`（441KB） | **孤儿文件**（未注册、DLL 无引用），仍占体积 |
| `monsters.xslt` | 把 `human` 血量 100→300，**全局生效**，且会与其它 mod 抢改同一属性 |

---

# 附录 A：机制全链路拆解（动作 + 特效）

> 本附录把「自定义动作」与「忍术特效」两条链拆到可照抄的粒度。行号、骨索引、枚举值均已实证。

## A.1 自定义动作：五层链路

**一个自定义动作要穿过 5 层，每层各管一件事。改哪层动哪层——这是这套系统最好的一点。**

```
① 物品层   <Item> 的 <Weapon item_usage="cla_magic_fire_2h" …/>
           ↓ 作用：这个物品"用哪套动作"

② usage 层 item_usages_sets_mod.xml 里该 set 的
           <usage ready_action="act_ca_magic_ready_fire"
                  release_action="act_ca_magic_release_fire"
                  ready_continue_ranged_action="act_ca_magic_ready_fire_continue"
                  is_mounted="False" require_free_left_hand="true" strike_type="thrust" />
           ↓ 作用：这个用法"在什么时机调哪个 action 名"

③ 声明层   action_types_mod.xml
           <action name="act_ca_magic_ready_fire" type="actt_ready_ranged"
                   action_stage="as_attack_ready" />
           ↓ 作用：这个 action 名"是哪个阶段的动作"（决定引擎何时允许播它）

④ 绑定层   action_sets_mod.xml 的 as_human_warrior 块
           <action type="act_ca_magic_ready_fire" animation="snj_jutsu_ready_fire"
                   alternative_group="ca_jutsu_fire_alt" />
           ↓ 作用：这个 action"播哪段动画"

⑤ 资产层   动画 clip 编译在 AssetPackages/pack1.tpac 里（名字 = snj_jutsu_ready_fire）
```

### 层间规则（必须知道）

| 规则 | 说明 |
|---|---|
| **② 只引名字，不引动画** | usage 层完全不知道动画长什么样；换动画只动 ④，换武器只动 ①。**两层解耦是这套设计的核心** |
| **③ 的 `action_stage` 决定"何时能播"** | 全 mod 只用了 4 个值：`as_attack_ready`(56) / `as_attack_release`(65) / `as_reload_mid_phase`(9) / `as_reload_last_phase`(15)。**写错 stage = 动作永远不播**（引擎按阶段派发） |
| **③ 的 `type` 是动作的语义分类** | 142 条生效声明里的实际分布：`actt_ready_ranged` 52 / `actt_release_throwing` 31 / `actt_reload` 24 / `actt_release_melee` 24 / `actt_jump` 24 / `actt_release_ranged` 10 / `actt_jump_end_hard` 8 / `actt_jump_end` 8 / `actt_blocked_melee` 8 / `actt_ready_melee` 4 / `actt_jump_start` 2 |
| **④ 的 `alternative_group` = 随机变体** | 🔴 **引擎原生属性**（原版 `Native/ModuleData/action_sets.xml` 用了 1330 次）。同一 action 挂多条、共享一个 group 名 → 每次播放随机挑一条。**零成本解决"打起来重样"** |
| **① 的两个门控** | `require_free_left_hand="true"` = 施法必须空出左手（拿盾就不能施法）；`has_single_stance="true"` = 不区分左右架势 |
| **④ 的安全性质** | `as_human_warrior` 是**纯追加**（0 条原版动作名），引擎 C# 层把同 id 的 action_set **追加合并**进第一个 → 结果是「原版几千条 + 这 138 条」。**但禁止逐字拷贝 Native 的内容进来**（合并出重复定义 → `KeyNotFoundException` 实崩，我们 2026-09-08 踩过） |

### 加载路径（别忘这一步）

这 3 个文件**不在 `SubModule.xml` 的 `<Xmls>` 里**，必须靠 `ModuleData/project.mbproj` 注册：

```xml
<file id="soln_action_sets"      name="ModuleData/action_sets_mod.xml"      type="action_set" />
<file id="soln_action_types"     name="ModuleData/action_types_mod.xml"     type="action_type" />
<file id="soln_item_usage_sets"  name="ModuleData/item_usages_sets_mod.xml" type="item_usage_set" />
```

**不开这三行 = 整套自定义动作静默不存在**（不报错、不崩，就是不生效）。

## A.2 忍术特效：五条并行通道

特效不是一条链，是**各自独立触发**的几条通道。搞清这条才不会把"手上的火"和"命中爆点"当成一回事。

| 通道 | 挂什么 | 挂在哪根骨/位置 | 谁触发 | 生命期 | 实现方 |
|---|---|---|---|---|---|
| ① **手持常驻** | 粒子（`cla_fire_effect`） | 🔴 **持物骨**：主手索引 **27** / 副手 **20** | 每帧 diff 双手持物 StringId | 手持期间；换手/收刀即摘 | `CAItemParticleEffect` |
| ② **飞行拖尾** | 粒子（`trail_particle_name`） | 弹药实体自身 | **引擎原版机制**（mod 零代码） | 飞行期间 | 引擎 |
| ③ **命中爆点** | 粒子 burst（`cla_explosion_small`） | **命中点世界坐标** | `OnAgentHit` | 一次性 | `CADamageParticleModel` |
| ④ **受击部位** | 粒子（`cla_fire_bone`） | **受击者的碰撞骨**（`CollisionBoneIndex`） | `OnAgentHit` + **`CollisionResult == StrikeAgent`** | 一次性 | `CADamageParticleModel` |
| ⑤ **多发弹幕** | 额外弹体 | 从枪口/手部位置 | `OnAgentShootMissile` | 立即 | `CAShotgunEffectMissionLogic` |

### 通道 ①：手持常驻

```csharp
// 骨骼索引直接写死（正确写法见 A.4）
_equipmentParticleMappings["hr_weapon_katana_fire"] = [("cla_fire_effect_sword_curved", [27])];
_equipmentParticleMappings["hr_shield_ninjato_fire"] = [("cla_fire_effect_sword",       [20])];

// 挂法：建空实体 → 建粒子 → 实体挂到 agent visuals → 粒子挂到骨
childEntity = GameEntity.CreateEmpty(scene, true);
ps = ParticleSystem.CreateParticleSystemAttachedToEntity(particleId, childEntity, ref identity);
agent.AgentVisuals.AddChildEntity(childEntity);
agent.AgentVisuals.GetSkeleton().AddComponentToBone(boneIndex, ps);
```

两个工程要点：
- **每帧 diff 而非每帧重挂**：缓存 `Dictionary<Agent, Tuple<string,string>> _lastHeldItems`（双手物品 StringId），**只在变化时**摘旧的挂新的。场上 200 人时这是"每帧 200 次字典查 + 0 次引擎调用"，而不是 200 次粒子重建。
- **摘的时候要显式 `RemoveComponent`**：`agent.AgentVisuals.GetSkeleton().RemoveComponent(ps)`。不摘会残留。

### 通道 ③④：命中特效（判断顺序别搞反）

```csharp
public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent,
                                in MissionWeapon affectorWeapon, in Blow blow,
                                in AttackCollisionData attackCollisionData)
{
    // ① 先按「弹药 StringId」查表（远程命中的 affectorWeapon 是弹药，不是武器）
    if (!itemToParticleMap.TryGetValue(affectorWeapon.Item.StringId, out var fx)) return;

    // ② 爆点：无条件放（打地形也放）
    scene.CreateBurstParticle(ParticleSystemManager.GetRuntimeIdByName(fx.Item1),
                              new MatrixFrame(Mat3.Identity, attackCollisionData.CollisionGlobalPosition));

    // ③ 受击骨粒子：三重门 —— 名字非空 + 骨索引 >= 0 + CollisionResult == StrikeAgent
    if (string.IsNullOrEmpty(fx.Item2)) return;
    if (attackCollisionData.CollisionBoneIndex < 0) return;
    if ((int)attackCollisionData.CollisionResult != 1) return;   // 1 = StrikeAgent
    affectedAgent.AgentVisuals.CreateParticleSystemAttachedToBone(
        ParticleSystemManager.GetRuntimeIdByName(fx.Item2),
        attackCollisionData.CollisionBoneIndex, ref MatrixFrame.Identity);
}
```

🔴 **`CombatCollisionResult` 枚举全值**（反编译实证）：
```
None = 0    StrikeAgent = 1    HitWorld = 2    Blocked = 3    Parried = 4    ChamberBlocked = 5
```
**`== 1` 就是"真砍到人身上"**——打地形(2)、被盾挡(3)、被弹开(4) 都不挂受击骨粒子。这是"火从被砍中的那条胳膊烧起来"能成立的关键判断。

### 通道 ② 的正确认知：拖尾是引擎送的

`trail_particle_name` 是**原版物品字段**（原版燃烧罐 `psys_game_burning_jar_trail` 就是这么用的）。引擎生成弹体时自动把粒子挂到弹体上并带着走，**mod 一行代码都不用写**。
🔴 即：想让法术弹有拖尾，**只要物品 XML 填对这个字段**。我们的月牙弹（`taikou_spell_crescent`）也走这条路——只是粒子资产还没接（见 `Combat/SpellMissileTrace.cs` 文件头注释）。

## A.3 一次「火球术」的完整时序（两条链叠起来）

以 `ca_magic_fire_ball` 为例：

| # | 时刻 | 发生什么 | 属于 |
|---|---|---|---|
| 1 | 玩家按投掷键 | 手上是 `ca_magic_fire_ball`，`mesh="cla_invisible_holster"` → **手上看不见东西**（"空手施法"观感） | 物品 |
| 2 | 起手 | `item_usage=cla_magic_fire_2h` → `ready_action=act_ca_magic_ready_fire` → 绑的动画 `snj_jutsu_ready_fire`（约 1 秒蓄力） | 动作 ①②③④⑤ |
| 3 | **蓄力期间（每帧）** | 读到主手持有该弹药 → 在**主手持物骨(27)** 挂 `cla_fire_effect` → **手心冒火** | 特效 ① |
| 4 | 释放瞬间 | `release_action=act_ca_magic_release_fire` → 动画 `snj_jutsu_release_fire` | 动作 |
| 5 | 弹体出膛 | 引擎按 `flying_mesh="hr_energy_sphere_large"` 生成可见弹体 + 按 `trail_particle_name` 自动挂拖尾 | 特效 ② |
| 5b | 同上 | `CAShotgunEffectMissionLogic` 查表：`ca_magic_fire_ball` **不在表里**（只有 `_multi_3` 在）→ 单发。若是 `_multi_3` → **补发 2 发**（散布 0.2、速度 ±30%） | 特效 ⑤ |
| 6 | 命中 | 按**弹药** StringId 查表 → 命中点放 `cla_explosion`；若 `CollisionResult==StrikeAgent` 且在受击骨上挂 `cla_fire_bone` | 特效 ③④ |
| 6b | 同上 | 物理材质 `cla_explosion` 带 `dont_stick_missiles="true"` + `AmmoBreaksOnBounceBack` + `<Flags QuickFadeOut>` → **弹体不插在身上，命中即消散** | 数据 |
| 7 | 收手 | 仍持同一物品（`stack_amount=25` 还有货）→ StringId 没变 → **粒子不重挂**；换武器/耗尽 → 下一帧 diff 到变化 → 摘粒子 | 特效 ① |

🔴 **三张查表是分开的，别搞混**：

| 表 | key 是什么 | 管什么 |
|---|---|---|
| `CAItemParticleEffect._equipmentParticleMappings` | **手持物品**（武器**或**弹药） | 手上的光 |
| `CADamageParticleModel.itemToParticleMap` | **命中时的 `affectorWeapon.Item`**（远程 = 弹药 StringId） | 爆点与受击部位 |
| `CAShotgunEffectMissionLogic._itemEffects` | **开火的弹药**（非消耗品则取 `AmmoWeapon`） | 发几发 |

**同一个物品可以给这三张表配三种不同表现，三张表互不知情。**

## A.4 骨索引 27 / 20 的正确取法（重要）

HikageRising 把 `27` / `20` 硬编码进 C#。它们的真身（**已实证**）：

| 硬编码值 | 骨名 | 引擎语义属性 | 证据 |
|---|---|---|---|
| **27** | `r_finger0`（右手手指骨） | `Monster.MainHandItemBoneIndex` | 原版 `Native/ModuleData/monsters.xml` 的 human 块写 `main_hand_item_bone = r_finger0` |
| **20** | `l_finger0`（左手手指骨） | `Monster.OffHandItemBoneIndex` | 同文件 `off_hand_item_bone = l_finger0` |

骨索引 ↔ 骨名的对照我们仓库里已有：`Knowledge/骨骼动画TRF格式与增量陷阱.md`（`20 / 27 = l_finger0 / r_finger0`）+ `Knowledge/骑砍2盔甲资产工程.md`（`bip01_r_finger0_27`）。

🔴 **我们的正确写法 = 走语义名**：

```csharp
sbyte mainHand = agent.Monster.MainHandItemBoneIndex;   // ✅ 换 skeleton 也对
sbyte offHand  = agent.Monster.OffHandItemBoneIndex;    // ✅
// 不要写成 new List<sbyte> { 27 } / { 20 }             // ❌ 换 skeleton 就错
```

理由：原版是用**骨名字符串**声明（`main_hand_item_bone="r_finger0"`），引擎启动时经 `Monster.GetBoneIndexWithId` 转成索引。**换 skeleton（`human` / `human_child` / 兽人 / 自建 race）索引值会变**——硬编码 27 在儿童或兽人骨架上就是另一根骨头。
（反编译确认 `Monster` 有全套语义属性：`MainHandBoneIndex` / `MainHandItemBoneIndex` / `OffHandItemBoneIndex` / `OffHandItemSecondaryBoneIndex` / `OffHandShoulderBoneIndex` …）

## A.5 生命周期与性能（照抄前必须知道）

| 事项 | 实情 | 我们该怎么做 |
|---|---|---|
| **每帧成本** | `OnMissionTick` **遍历全场 agent**（含 NPC），逐个查双手持物 | 可接受（纯字典查），但**别在这个循环里加引擎调用**——它已经做到"只在变化时调引擎" |
| 🔴 **清理有缺口** | 循环开头 `if (!IsHuman \|\| !IsActive() \|\| AgentVisuals == null) continue;` → **死亡/非活体 agent 永远走不到 `RemoveParticleEffects`** | 两个字典以 `Agent` 引用为 key，**没有清理路径** → 长战斗中只增不减（强引用还挡 GC）。我们实现时**必须补 `OnAgentRemoved` 清理** |
| **受击粒子会累积** | `CreateParticleSystemAttachedToBone` 每次命中都新建，**无回收代码** | 靠粒子资产自身寿命结束（duration）。复用前需确认粒子是"播完自毁"型 |
| **命中特效频率** | 只在 `OnAgentHit` 触发，有命中才有 → 成本天然有界 | 安全 |
| **多发弹幕成本** | `_multi_5` 一发弹药 → 5 个弹体；25 发弹药袋 → **125 个弹体** | 🔴 要评估**同时存在的弹体数**（引擎对导弹数有上限，超出会挤掉旧的） |

## A.6 一句话记住两条链

- **动作链**：`物品 → usage(动作名) → 声明(阶段+类型) → 绑定(动画) → tpac(资产)`——**五层各自可换**，靠 `project.mbproj` 三行注册才生效。
- **特效链**：**五条独立通道**（手持常驻 / 飞行拖尾 / 命中爆点 / 受击部位 / 多发弹幕），靠**三张各自独立的 StringId 查表**驱动，key 分别是「手持物品」「命中弹药」「开火弹药」。

---

# 附录 B：忍术全量清单（逐项）

> **数据来源 = 脚本连接**（不是手抄）：`Debug/offline/join_hikage_jutsu.py` → 报告 `hikage_jutsu_report.md` + 表 `hikage_jutsu_inventory.csv`
> 连接了四个源：DLL 三张表（反编译） × `items_magic.xml` × `item_usages_sets_mod.xml` × `action_sets_mod.xml`。所有计数已剥 XML 注释。

## B.1 「忍术」一共四类，共 83 项

这个 mod 没有单一的"忍术系统"。**「忍术」在它这里是四类东西拼出来的**：

| 类 | 数量 | 是什么 | 有特效吗 |
|---|---|---|---|
| **① 法术（投掷）** `ca_magic_*` | **36** | 8 元素 × 弹/球/光束/冰锥 | ✅ **五条通道全用** |
| **② 附魔武器** `hr_weapon_*_<元素>` | **37** | 6 刀种 + 拳 + 盾，各配元素 | ✅ 手持常驻 + 命中（无弹幕） |
| **③ 体术（拳法）** `hr_weapon_fist_*` | 7（已计入 ②） | 隐形武器 + 空手动作 | ✅ 走 ② 的机制 |
| **④ 忍具** 手里剑/苦无/炸弹 | **9** | 纯物理投掷 | ❌ **零特效**（只有飞行拖尾） |

**合计 36 + 37 + 9 = 82 项**（拳法 7 项含在 ② 里，不重复计）。

## B.2 ① 法术 36 项（8 元素 × 形态）

### 元素 × 形态矩阵

| 元素 | 项数 | 形态 | 对应动画族 | usage |
|---|---|---|---|---|
| `fire` 火 | 4 | bolt ×2 / ball ×2 | **`snj_jutsu_*`** | `cla_magic_fire_2h` |
| `ice` 冰 | 4 | shard ×2 / spike ×2 | **`snj_jutsu_*`**（water） | `cla_magic_water_2h` |
| `qi` 蓝气 | 6 | bolt ×2 / ball ×2 / beam ×2 | **`snj_jutsu_*`**（energy） | `cla_magic_energy_2h` |
| `qi_red` 红气 | 6 | bolt ×2 / ball ×2 / beam ×2 | **`snj_jutsu_*`**（energy，同 qi） | `cla_magic_energy_2h` |
| `air` 风 | 4 | bolt ×2 / ball ×2 | **`snj_bending_*`**（wind） | `cla_magic_wind_2h` |
| `earth` 土 | 4 | bolt ×2 / ball ×2 | **`snj_bending_*`**（earth） | `cla_magic_earth_2h` |
| `lightning` 雷 | 4 | bolt ×2 / ball ×2 | **`snj_bending_*`**（lightning） | `cla_magic_lightning_2h` |
| `shadow` 影 | 4 | bolt ×2 / ball ×2 | **`snj_bending_*`**（shadow） | `cla_magic_shadow_2h` |

🔴 **动画分两族，有讲究**：`fire`/`ice`/`qi`/`qi_red` 用 **`snj_jutsu_*`（忍术族）**，`air`/`earth`/`lightning`/`shadow` 用 **`snj_bending_*`（御术族，"bending" = 降世神通式元素操控）**。
即作者把「忍术」与「元素操控」当两种不同打法做了两套动画。`qi` 与 `qi_red` **共用一套动画**（`cla_magic_energy_2h`），只有粒子颜色不同。

### 逐项数值表（36 项）

| id | 元素 | 形态 | 伤害 | 弹速 | 弹药 | 多发总弹数 | 手持粒子@骨 | 命中爆点 | 受击骨粒子 | 拖尾粒子 |
|---|---|---|---|---|---|---|---|---|---|---|
| `ca_magic_fire_bolt` | fire | bolt | 50 P | 60 | 25 | 1 | `cla_fire_effect`@20,27 | `cla_explosion_small` | `cla_fire_bone` | `cla_fire_small_throwing` |
| `ca_magic_fire_bolt_multi_5` | fire | bolt | 50 P | 60 | 25 | **5** | 同上 | 同上 | 同上 | 同上 |
| `ca_magic_fire_ball` | fire | ball | 80 P | 60 | 3 | 1 | 同上 | `cla_explosion` | 同上 | `cla_fire_large_throwing` |
| `ca_magic_fire_ball_multi_3` | fire | ball | 80 P | 60 | 3 | **3** | 同上 | 同上 | 同上 | 同上 |
| `ca_magic_ice_shard` | ice | shard | 70 P | 80 | 30 | 1 | `cla_ice_effect`@20,27 | 🔴 **无** | 🔴 **无** | 🔴 **无** |
| `ca_magic_ice_shard_multi_5` | ice | shard | 70 P | 80 | 25 | **5** | 同上 | 🔴 **无** | 🔴 **无** | 🔴 **无** |
| `ca_magic_ice_spike` | ice | spike | **150** P | 90 | 7 | 1 | 同上 | 🔴 **无** | 🔴 **无** | `cla_ice_small` |
| `ca_magic_ice_spike_multi_4` | ice | spike | **150** P | 90 | 7 | **4** | 同上 | 🔴 **无** | 🔴 **无** | 同上 |
| `ca_magic_qi_bolt` | qi | bolt | 50 P | 60 | 25 | 1 | `cla_qi_blue_effect`@20,27 | `cla_qi_blue_explosion_small` | `cla_qi_blue_bone` | `cla_energy_blue_throwing_small` |
| `ca_magic_qi_bolt_multi_5` | qi | bolt | 50 P | 60 | 25 | **5** | 同上 | 同上 | 同上 | 同上 |
| `ca_magic_qi_ball` | qi | ball | 100 P | 60 | 3 | 1 | 同上 | `cla_qi_blue_explosion` | 同上 | 同上 |
| `ca_magic_qi_ball_multi_5` | qi | ball | 100 P | 60 | 3 | **5** | 同上 | 同上 | 同上 | 同上 |
| `ca_magic_qi_beam` | qi | beam | 50 P | **300** | 25 | 1 | 同上 | `cla_qi_blue_explosion_small` | 同上 | 同上 |
| `ca_magic_qi_beam_large` | qi | beam | 70 P | **300** | 3 | 1 | 同上 | `cla_qi_blue_explosion` | 同上 | 同上 |
| `ca_magic_qi_red_bolt` | qi_red | bolt | 50 P | 60 | 25 | 1 | `cla_qi_red_effect`@20,27 | `cla_qi_red_explosion_small` | `cla_qi_red_bone` | `cla_energy_red_throwing_small` |
| `ca_magic_qi_red_bolt_multi_5` | qi_red | bolt | 50 P | 60 | 25 | **5** | 同上 | 同上 | 同上 | 同上 |
| `ca_magic_qi_red_ball` | qi_red | ball | 100 P | 60 | 3 | 1 | 同上 | `cla_qi_red_explosion` | 同上 | 同上 |
| `ca_magic_qi_red_ball_multi_5` | qi_red | ball | 100 P | 60 | 3 | **5** | 同上 | 同上 | 同上 | 同上 |
| `ca_magic_qi_red_beam` | qi_red | beam | 50 P | **300** | 25 | 1 | 同上 | `cla_qi_red_explosion_small` | 同上 | 同上 |
| `ca_magic_qi_red_beam_large` | qi_red | beam | 70 P | **300** | 3 | 1 | 同上 | `cla_qi_red_explosion` | 同上 | 同上 |
| `ca_magic_air_bolt` | air | bolt | 70 **B** | 80 | 30 | 1 | `cla_wind_effect`@20,27 | `cla_explosion_wind_small` | 🔴 **无** | `cla_wind_small` |
| `ca_magic_air_bolt_multi_5` | air | bolt | 70 B | 80 | 25 | **5** | 同上 | 同上 | 🔴 **无** | 同上 |
| `ca_magic_air_ball` | air | ball | 70 B | 30 | 3 | 1 | 同上 | `cla_explosion_wind` | 🔴 **无** | `cla_wind_large` |
| `ca_magic_air_ball_multi_3` | air | ball | 70 B | 30 | 3 | **3** | 同上 | 同上 | 🔴 **无** | 同上 |
| `ca_magic_earth_bolt` | earth | bolt | 60 P | 80 | 30 | 1 | `cla_earth_effect`@20,27 | `cla_explosion_earth_small` | 🔴 **无** | `cla_earth_small` |
| `ca_magic_earth_bolt_multi_5` | earth | bolt | 60 P | 80 | 25 | **5** | 同上 | 同上 | 🔴 **无** | 同上 |
| `ca_magic_earth_ball` | earth | ball | 70 B | 80 | 3 | 1 | 同上 | `cla_explosion_earth` | 🔴 **无** | `cla_earth_large` |
| `ca_magic_earth_ball_multi_3` | earth | ball | 70 B | 80 | 3 | **3** | 同上 | 同上 | 🔴 **无** | 同上 |
| `ca_magic_lightning_bolt` | lightning | bolt | 60 P | **180** | 30 | 1 | `cla_lightning_effect`@20,27 | `cla_explosion_lightning_small` | `cla_lightning_bone` | `cla_lightning_bolt_throwing` |
| `ca_magic_lightning_bolt_multi_5` | lightning | bolt | 60 P | **180** | 25 | **5** | 同上 | 同上 | 同上 | 同上 |
| `ca_magic_lightning_ball` | lightning | ball | 80 P | 80 | 3 | 1 | 同上 | `cla_explosion_lightning` | 同上 | `cla_lightning_large_throwing` |
| `ca_magic_lightning_ball_multi_3` | lightning | ball | 80 P | 80 | 3 | **3** | 同上 | 同上 | 同上 | 同上 |
| `ca_magic_shadow_bolt` | shadow | bolt | 60 P | 60 | **40** | 1 | `cla_shadow_effect`@20,27 | `cla_explosion_shadow_small` | 🔴 **无** | `cla_shadow_small_throwing` |
| `ca_magic_shadow_bolt_multi_5` | shadow | bolt | 60 P | 60 | 25 | **5** | 同上 | 同上 | 🔴 **无** | 同上 |
| `ca_magic_shadow_ball` | shadow | ball | 60 P | 60 | 3 | 1 | 同上 | `cla_explosion_shadow` | 🔴 **无** | `cla_shadow_large_throwing` |
| `ca_magic_shadow_ball_multi_3` | shadow | ball | 60 P | 60 | 3 | **3** | 同上 | 同上 | 🔴 **无** | 同上 |

（P = Pierce 穿刺，B = Blunt 钝击。**同行 `同上` = 与上一行同值**。）

### 逐项动画（起手 / 释放）

| 元素 | usage | 起手动画 | 释放动画（含随机变体组） |
|---|---|---|---|
| fire | `cla_magic_fire_2h` | `snj_jutsu_ready_fire` | `snj_jutsu_release_fire`（组 `ca_jutsu_fire_alt`，另有 `snj_martial_release_fire` / `_fire_both`） |
| ice | `cla_magic_water_2h` | `snj_jutsu_ready_water` | `snj_jutsu_release_water` |
| qi / qi_red | `cla_magic_energy_2h` | `snj_jutsu_ready_energy` | `snj_jutsu_release_energy` |
| air | `cla_magic_wind_2h` | `snj_bending_ready_wind` | `snj_bending_release_wind`（组 `ca_jutsu_wind_alt`） |
| earth | `cla_magic_earth_2h` | `snj_bending_ready_earth` | `snj_bending_release_earth`（组 `ca_jutsu_earth_alt`） |
| lightning | `cla_magic_lightning_2h` | `snj_bending_ready_lightning` | `snj_bending_release_lightning`（组 `ca_jutsu_lightning_alt`，另有 `snj_martial_release_lightning_both`） |
| shadow | `cla_magic_shadow_2h` | `snj_bending_ready_shadow` | `snj_bending_release_shadow`（组 `ca_jutsu_shadow_alt`） |

**每个元素都带 `require_free_left_hand="true"`**（施法必须空左手）+ 步战/骑乘两套 usage（同动画）。

## B.3 ② 附魔武器 37 项（刀种 × 元素 → 粒子 + 骨）

**全部挂在骨 27（主手持物骨 `r_finger0`）**，只有拳加副手、盾挂骨 20：

| 刀种 | 元素变体 | 手持粒子名 | 命中爆点 / 受击骨 |
|---|---|---|---|
| `wakizashi` 胁差 | qi / qi_red / qi_void / fire / lightning（5） | `cla_energy_blue_sword_short` / `_red_` / `cla_darkness_effect_sword_short` / `cla_fire_effect_sword_short` / `cla_lightning_effect_sword_short` | `cla_*_cut` / `cla_*_bone` |
| `ninjato` 忍者刀 | 同上 5 种 | `..._sword`（无 short/long 后缀） | 同上 |
| `katana` 打刀 | 同上 5 种 | `..._sword_curved` | 同上 |
| `nodachi` 野太刀 | 同上 5 种 | `..._sword_long` | 同上 |
| `naginta` 薙刀 | 同上 5 种 | `cla_*_naginta` | fire/lightning/qi_void 🔴 **无命中特效**；qi/qi_red 有 |
| `yari` 枪 | qi / qi_red（2） | `cla_energy_blue_yari` / `_red_` | 有 |
| `fist` 拳 | qi / qi_red / fire / lightning / earth / wind（6） | `cla_*_effect`@**20,27** | fire/lightning/qi/qi_red 有受击骨；earth/wind 🔴 无（只有爆点） |
| `shield` 盾 | wakizashi_fire / _lightning、ninjato_fire / _lightning（4） | `cla_*_effect_sword[_short]`@**20** | 🔴 **全无命中特效**（盾只发光） |

🔴 **命名规律（可照抄）**：`cla_<元素>_effect_<刀种英文>[_short|_long|_curved]`
- `qi` → `cla_energy_blue_*`，`qi_red` → `cla_energy_red_*`，`qi_void` → `cla_darkness_effect_*`
- **同一个刀种共用一套粒子名，靠后缀区分尺寸**（short = 胁差，无后缀 = 忍者刀，curved = 打刀，long = 野太刀）

## B.4 ④ 忍具 9 项：🔴 零特效

| id | 类型 | 伤害 | 弹药 | 多发总弹数 | 特效 |
|---|---|---|---|---|---|
| `hr_weapon_kunai` | Stone（双组件） | 50 | 5 | 1 | 仅拖尾 `hr_particle_shuriken_trail` |
| `hr_weapon_kunai_enhanced` | Stone（双组件） | 70 | 5 | 1 | 仅拖尾 |
| `hr_weapon_kunai_3` | Stone（双组件） | 70 | 5 | **3** | 仅拖尾 |
| `hr_weapon_shuriken` | ThrowingKnife | 30 | 30 | 1 | 仅拖尾 |
| `hr_weapon_shuriken_5` | ThrowingKnife | 30 | 20 | **5** | 仅拖尾 |
| `hr_weapon_shuriken_10` | ThrowingKnife | 30 | 20 | **10** | 仅拖尾 |
| `hr_weapon_shuriken_large` | Stone | **150** | 3 | 1 | 🔴 **连拖尾都没有** |
| `hr_weapon_shuriken_large_2` | Stone | **150** | 3 | **2** | 🔴 无 |
| `ca_throwing_bomb` | Stone | 150 | 5 | 1 | 爆点 `cla_explosion` + 引信拖尾 `cla_fuse` |

🔴 **忍具不在任何一张 DLL 表里**（`_equipmentParticleMappings` / `itemToParticleMap` 都查不到 shuriken/kunai）——它们**完全走原版投掷管线**，只有 `trail_particle_name` 这个原版字段。**"手里剑"和"法术弹"是两套完全不同的做法。**

## B.5 🔴 特效覆盖率矩阵（最有价值的一张）

| 通道 | 法术 36 项 | 附魔武器 37 项 | 忍具 9 项 |
|---|---|---|---|
| **手持常驻粒子** | ✅ **36/36（全覆盖）** | ✅ 37/37 | ❌ **0** |
| **飞行拖尾** | ✅ 34/36（**冰 shard ×2 缺**） | —（近战无反） | ⚠️ 7/9（大手里剑 ×2 缺） |
| **命中爆点** | ⚠️ **32/36**（🔴 **冰系 4 项全缺**） | 33/37（盾 4 全缺） | ❌ 0（仅炸弹有） |
| **受击骨粒子** | ⚠️ **20/36**（🔴 **air/earth/ice/shadow **全 16 项缺**） | 大多数有，盾与 naginta 三款缺 | ❌ 0 |
| **多发弹幕** | 17/36（`_multi_*` 16 项生效 + 1 项 holy 已停用） | ❌ | ✅ 4/9 |

**读法**：
1. **手持粒子是唯一"全覆盖"的通道**——作者的优先级是"手上一定要有光"（视觉第一眼）。
2. 🔴 **冰系是最不完整的**：8 条通道里缺 3 条（爆点、受击骨、shard 的拖尾）——像是做到一半停了。
3. 🔴 **受击骨粒子只给了 fire / lightning / qi / qi_red 四系**（"+受击部位"这个最贵的表现只用在四个元素上）。
4. **忍具彻底没有特效**——它靠原版物理手感（`AmmoSticksWhenShot` 会插在身上）而不是靠光。
5. **炸弹 `ca_throwing_bomb` 是唯一"忍具里带特效"的**（爆点 + 引信拖尾）。

## B.6 数据残留（DLL 表里的死条目，逐条实测）

**判定方法**：把三张表的 key 与全 `ModuleData/items/` 下**实际生效的 511 个物品 id**（`<Item>` + `<CraftedItem>`，已剥注释）对表，找对不上的。

| 表 | 表内条目 | **死条目** | 死的都是谁 |
|---|---|---|---|
| 手持粒子 `_equipmentParticleMappings` | 75 | **2** | `ca_magic_holy_bolt` / `ca_magic_holy_bolt_multi_5`（圣光系已停用） |
| 命中特效 `itemToParticleMap` | 71 | **8** | holy ×2 + 🔴 **`hr_weapon_ninjato_energy_blue` / `_energy_red` / `_energy_void`** + `ca_elf_exploding_barbed_arrows_multi` + `ca_cannon_ammo` + `ca_musket_ammo_explosive` |
| 多发弹幕 `_itemEffects` | 25 | **5** | `ca_magic_holy_bolt_multi_5` + `ca_elf_exploding_barbed_arrows_multi` + `ca_elf_barbed_arrows_multi_5` + `ca_musket_blunderbuss_brown` / `_black` |

### 🔴 最值得记的一条：改名没清旧表项

命中特效表里 **`hr_weapon_ninjato_energy_blue/red/void` 这三条是死条目**——生效物品里没有这三个 id（`hr_weapon_ninjato_blade_energy_blue` 是**锻造部件**，不是成品武器）。

对比两张表的 ninjato 条目：

| 表 | ninjato 条目 | 状态 |
|---|---|---|
| **手持粒子表** | `hr_weapon_ninjato_qi` / `_qi_red` / `_qi_void` / `_fire` / `_lightning`（**5 条**） | ✅ 全是新名字 |
| **命中特效表** | 新的 5 条 **＋ 旧的 `_energy_blue` / `_energy_red` / `_energy_void` 3 条**（**8 条**） | ⚠️ 新旧混装 |

**即：作者曾把这三种气系变体命名为 `_energy_blue/red/void`，后来改名为 `_qi/_qi_red/_qi_void`——手持粒子表是重写的（干净），命中特效表是在旧表上追加的（留了 3 条残渣）。**

后果：**没有功能 bug**（残渣 key 永远查不到，等于死代码），但**读代码的人会被误导**——看到 `_energy_blue` 会以为存在这个物品。

🔴 **给我们两条教训**：
1. **停用内容要连表一起清**（圣光系的残留横跨物品注释、DLL 表、音效、物理材质四处）
2. **改名要全链路 grep 旧名**——凡是"用字符串 id 做 key 的硬编码表"，改名就等于埋死条目

## B.7 复现方式

```bash
cd Debug/offline
PYTHONIOENCODING=utf-8 python join_hikage_jutsu.py --csv hikage_jutsu_inventory.csv > hikage_jutsu_report.md
```

- 脚本：`Debug/offline/join_hikage_jutsu.py`（只读四源，不写 mod 目录）
- 报告：`Debug/offline/hikage_jutsu_report.md`（四张表）
- 数据：`Debug/offline/hikage_jutsu_inventory.csv`（36 行 × 15 列，可直接 Excel 打开）
- 🔴 **两个坑**：① 脚本必须 `PYTHONIOENCODING=utf-8`，否则 Windows GBK 控制台把中文写花；② **数数一律先剥 XML 注释**（本 mod 注释量 12%~28%）

---

# 附录 C：粒子资产能不能解析复刻（可行性评估）

> 问的是：附录 A/B 里那些 `cla_*` 粒子特效，我们能不能解析出来、复刻成自己的？
> **结论：能，而且路径清晰。缺的不是能力，是一段导出代码 + 一次对齐。**

## C.1 三句话结论

| 问题 | 答案 |
|---|---|
| 粒子在 tpac 里**是可识别的资产**吗？ | ✅ **是**。122 个粒子，类型 GUID `6de14d67-dd9a-45be-9463-0281c3d8dd51`（= `TpacTool.Lib/Particle/Particle.cs` 的 `Particle.TYPE_GUID`） |
| 我们的工具**能读**吗？ | ⚠️ **库能读、命令行没出口**。`ParticleEffectData.ReadData` 已**逐字段**解析整份二进制；但 `Emitter` 的流构造函数**把字段读进局部变量就丢了**，且 tpaccli 没有粒子导出命令 |
| 参数名对不上怎么办？ | ✅ **有 ground truth**：原版同一种粒子**同时存在于 tpac（二进制）和 XML（已解码）**，拿它做对齐即可反推「二进制槽位 → 参数名」 |

## C.2 已实测的证据

```
$ tpaccli list --packdir <HikageRising/AssetPackages> --filter cla_fire_effect
cla_fire_effect                    6de14d67-dd9a-45be-9463-0281c3d8dd51
cla_fire_effect_naginta            6de14d67-...
cla_fire_effect_sword              6de14d67-...
cla_fire_effect_sword_curved       6de14d67-...
cla_fire_effect_sword_long         6de14d67-...
cla_fire_effect_sword_short        6de14d67-...
```

| 项 | 数量 | 来源 |
|---|---|---|
| HikageRising 的粒子资产 | **122**（121 `cla_*` + 1 `hr_particle_shuriken_trail`） | `tpaccli list` 实测 |
| 原版 Native 的粒子资产 | **271** | `tpaccli list` 实测 |
| 原版粒子 XML（活体 218 + 死档 140） | **358** 个 effect | `particle_systems*.xml` 实测 |

**原版同名验证**（tpac 名 ↔ XML 名）：抽查 5 个，**3 个两边都在**（`aserai_torch_fire_spark` / `battleground_smoke_far` / `battle_ground_smoke_3`），2 个只在 tpac（`campaign_rain_clouds` / `cutscene_dust`，疑似 GPU 粒子或战役地图专用）。
→ **有 3 个可对齐的样本就够建立映射表**（原版 XML↔tpac 对齐 → 槽位语义 → 再套到 HikageRising 的 122 个上）。

## C.3 复刻能力我们已有（盘点）

| 需要的能力 | 我们有吗 | 位置 |
|---|---|---|
| 粒子 XML **格式全解**（21 flag + 55 参数 + 曲线） | ✅ | `Knowledge/骑砍2粒子系统.md` |
| **生成器**（紧凑 spec → 完整 XML，含自检） | ✅ | `tools/particle-pipeline/gen_particle_effect.py` |
| **离线预览**（three.js，验证形状/密度/尺度/节奏） | ✅ | `tools/particle-pipeline/preview/` |
| **注册机制**（内容包加粒子） | ✅ 机制明确 | `project.mbproj` 的 `soln_particle_systems`（⚠️ 未实机验证过） |
| **贴图 dump** | ✅ 有先例 | 原版 `smoke_d` 就是从 `particles.tpac` dump 后缩到 256 |
| 从 tpac **读出粒子数值** | ❌ **缺一段代码** | 见 C.4 第①步 |

## C.4 要做三件事（工作量可估）

| # | 做什么 | 难度 | 依据 |
|---|---|---|---|
| ① | 给 tpaccli 加 `particledump` 命令 —— 把 `ParticleEffectData.Emitter` 的那些局部变量**改成属性存下来**（约 60 个字段）+ 写文件 | **机械劳动**（库里数据已解析，只是被丢弃） | `ParticleEffectData.cs:50-172` 逐字段已读 |
| ② | **建映射表**：拿原版同名粒子做 XML↔tpac 对齐 → 确定「第 N 个槽位 = XML 的哪个参数」 | **一次性的智力活**，做完通用 | 原版 3/5 同名可对齐 |
| ③ | 输出成 XML → 用现有生成器/预览器核对 | **已有工具** | `gen_particle_effect.py` |

**已命名的槽位**（原作者已对上一部分，可直接用）：`gravity` / `billboard_type` / `emit_volume_type` / `texture_sprite_count` / `diffuse_multiplier` / `emissive_multiplier` / `backlight_multiplier` / `heatmap_multiplier` / `cone_emit_angle` / `particle_size_base` / `particle_size_bias` / `particle_size_curve_op` / `collision_behaviour` / `emission_velocity_model` / `fixed_billboard_direction` / `decal_*` / `quad_*` / `max_alive_particle_count` / `emitterSoundCode` / `flags` / `name`。
**仍是匿名槽**的：`f1`~`f34`、`u3`~`u8`、`i1`~`i5`、`v1`~`v7` —— 这批就是第②步要对出来的。

另外 `EmitterParameter` = `(UnknownUInt1, UnknownUInt2, UnknownFloat1, UnknownFloat2, Curve)` —— 高度疑似就是 XML 里的 **`base` + `bias` + 曲线** 三元组（XML 的"随机型/曲线型参数"正是这个形状）。

## C.5 拿不到什么（诚实边界）

| 拿不到 | 为什么 | 影响 |
|---|---|---|
| **native shader 内部计算** | 编译在引擎里，反编译看不到 | 预览 ≠ 游戏观感（我们文档 §2 已立此结论：**材质决定混合模式，shader 决定最终样子**） |
| 材质的**到底哪张贴图** | 材质资产在 tpac 里可 dump，但引用链要一条条追 | 可解，但要额外工作 |
| 粒子之间的**调用时序** | 那是 DLL 里的逻辑（附录 A.3） | 已经有了（本次分析） |

## C.6 ⚠️ 一条非技术提醒

这批粒子是**第三方 mod 的原创资产**。我们项目对"借用外部数据"已有裁定先例（名字池借用织丰：**先标出处，发布前需授权，替换排在后面**）——这里同理：**技术可行 ≠ 可以直接发布**。

三条可选路线，按"像不像"排序：
1. **解析后原样复刻**（最像，但版权风险最高）→ 内部参考/学习可行，发布前必须换
2. **解析后重制等效粒子**（用它的参数当配方参考，自己写数值）→ **推荐**：既拿到"为什么好看"的知识，又避开直接复制
3. **只学做法、完全自造** → 最安全，但会重新踩一遍调参的坑

## C.7 🔴 顺带纠正一条：粒子有**两条**进游戏的路（本次实证）

> 这条不只关乎 HikageRising，**它补上了我们自己粒子文档的一处空白**（[骑砍2粒子系统.md](骑砍2粒子系统.md) §1.5 只写了 XML 一条路，且把"内容包注册 soln_particle_systems"标为「未实机验证」）。

### 两条路都在用，而且都活

| 路径 | 做法 | 谁在用（实证） |
|---|---|---|
| **① XML 注册** | `ModuleData/particle_systems_*.xml` + `project.mbproj` 挂 `soln_particle_systems` | **原版 Native**：mbproj 里挂了 **7 行**（misc1/misc2/general/map_icon/outdoor/basic + gpu_particle_systems），合计 **218 个活体 effect** |
| **② tpac 粒子资产** | 粒子作为 `Particle` 资产编译进 `AssetPackages/*.tpac` | **HikageRising**：**一个粒子 XML 都没有**，122 个粒子全部只以 tpac 资产存在，且**确实生效** |

### 判决性证据（HikageRising 反证）

- 我扫过他整个模块：`grep -rl "particle_effects\|<effect"` → **只有主菜单场景文件**（那是引用粒子名，不是定义）
- `ModuleData` 里**没有任何粒子定义文件**，也没有 xslt 覆盖
- 而他的 DLL 大量按名字 spawn 粒子（`CAItemParticleEffect` / `CADamageParticleModel`），**这是这个 mod 的核心卖点**——不可能全是哑弹
- 旁证：原版 `particles.tpac` 有 **271 个**粒子，而注册 XML 只定义 **218 个**；抽查 `campaign_rain_clouds` / `cutscene_dust` → **全 Modules 树的 ModuleData 里零引用**，却活在 tpac 里

→ **结论：引擎的粒子来源 = 注册的 XML ∪ AssetPackages 里的 Particle 资产。两条路都通。**

（诚实边界：我没有逐行追到引擎里"注册 tpac 粒子"的那段代码——`ParticleSystemManager` 的调用方没 grep 出来。**结论是靠 HikageRising 反证的，不是靠反编译确认的**。要坐实可以补一次实机验证。）

### 🔴 对用户提问的直接回答：ModKit 打不开他的粒子

**对，打不开。** 原因：

- ModKit 编辑器要打开的是**编辑器工程**（`Assets/` + `AssetSources/` 镜像布局，见 CLAUDE.md 铁律 31）
- HikageRising **两样都没有**——他的目录只有 `AssetPackages / bin / ModuleData / ModuleSounds / SceneObj / SubModule.xml`
- `AssetPackages/*.tpac` 是 **Publish 的产物**，编辑器不认它当工程（同 CLAUDE.md 里"引擎挑资产目录按固定顺序取第一个存在的"那条：tpac 是运行期读的，不是编辑器源）

**但这不影响复刻**——因为复刻走的是**解析 + 重写 XML**（附录 C.4 三步），**本来就不经过 ModKit**。真要在编辑器里"看"粒子反而做不到；要**实机看**倒是有路（他的 mod 已装，粒子已注册，写个命令按名字 spawn 即可）。

### 这条知识对我们的实际价值

| 影响 | 说明 |
|---|---|
| **做粒子多了一条路** | 除了"写 XML + mbproj 注册"，还可以"编辑器做粒子 → Publish 进 tpac"。前者零编辑器依赖（我们已有生成器），后者适合要在编辑器里调效果 |
| **我们的粒子文档该补一句** | §1.5 只写了 XML 路；§7「未实机验证」那条依然成立（我们仍**没验证过**自己注册的 XML 能不能生效），但现在知道**就算 XML 路不通，还有 tpac 路兜底** |
| **判断"粒子在不在"的口径** | 不能只看 `particle_systems*.xml`——**AssetPackages 里也要查**。反过来，一个 mod 没有粒子 XML ≠ 它没有自定义粒子 |
