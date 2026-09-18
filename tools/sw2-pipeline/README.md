# sw2-pipeline —— 战国无双2 角色 → 骑砍2 的「认零件 + 出配方」工具链

> 服务于 [Knowledge/战国无双换装工程.md](../../Knowledge/战国无双换装工程.md) 的批量阶段：
> 把《战国无双2》28 个有名武将的**脸**、**甲**、**武器**做进游戏。
> 具体执行靠两条现成管线（`tools/face-pipeline/` 出头、`tools/armor-pipeline/` 出甲），
> **本工具链只解决它们共同缺的那一环：认出源模型里哪块零件是谁。**
> 计划：[plans/战国无双换装批量落地.md](../../plans/战国无双换装批量落地.md)

---

## 一、为什么需要它

战无2 的 58 个源模型，零件名字全是这种编号：

```
model_0_submesh_6_noesis_meshnode_0006
```

没有 `head` / `eye` / `armor` 这种字。而：

- `build_head.py` 的挑件靠**关键字**（`--pick face=head,...`）→ 一个都匹配不上
- `build_armor.py` 的选件靠**子网格序号**（`--parts body_kimono_arms`）→ 是给幸村一个人调死的

28 个角色 × 平均 13 块零件，靠肉眼一块块认不现实。所以先做这个工具。

---

## 二、怎么跑

```bash
# 1) 单个角色（调试用）
"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b \
    --python tools/sw2-pipeline/scripts/identify_parts.py -- \
    --src "D:/BrainMaker/战国无双2资产解包分析/export/fbx/L02_nobunaga.fbx" \
    --out "Debug/offline/外观批量导入/sw2_parts"

# 2) 批量（28 个有名武将 / 23 个兵种与护卫）
python tools/sw2-pipeline/run_identify.py                # 武将
python tools/sw2-pipeline/run_identify.py --set troops   # 兵种 + 护卫
python tools/sw2-pipeline/run_identify.py --only L02_nobunaga L00_yukimura
```

**产出**（`Debug/offline/外观批量导入/sw2_parts/`，离线产物不进 git）：

| 文件 | 内容 |
|---|---|
| `<角色>_parts.csv` | 每块零件的测量数据 |
| `<角色>_sheet.png` | 接触图（正面）——每格一块零件，**左上角黄色数字 = CSV 行号** |
| `<角色>_sheet_B.png` | 接触图（背面） |

---

## 三、零件表读法

`<角色>_parts.csv` 每行一块零件：

| 列 | 含义 |
|---|---|
| `idx` | 序号（= 接触图格子上的黄色数字，行优先） |
| `ident` / `conf` / `why` | 机器的初判 + 置信度 + 依据（**初判不可全信，看下面的硬规则**） |
| `verts` / `faces` | 顶点数 / 面数 |
| `mats` | 材质名（`\|` 分隔） |
| `top_bones` | 主导骨骼（前 3 根 + 权重占比） |
| `color` | UV 采样得到的平均色（`R,G,B`）——判断「皮肤还是金属」用 |
| `zrel` | 包围盒高度占全身的比例（0=脚底，1=头顶） |

**🔴 `zrel` 是比例不是米**：源模型是厘米（骨头在 z≈165 而不是 1.65），
写死米制阈值会把所有零件都判成「头」——这个坑已经踩过一次，别再改回去。

---

## 四、已实锤的硬规则（28 人实测）

| 认什么 | 怎么认 | 命中率 |
|---|---|---|
| **眼球** | 顶点数 **38** / 面数 **60** | 27/28（宫本武藏例外：19/30 两块） |
| **武器** | 材质名带 **`mat_w_`** | 28/28 |
| **布料驱动件** | 材质名带 **`driver_`** | 28/28 |
| **脸** | 主导骨骼里 **`bone_46`** 占比最高（45%~67%）+ 主色是皮肤色 | 已核 8/8，批量核对中 |
| **头发 / 兜** | 主导骨骼是 `bone_11`（头骨）的件 **需要看图区分**（毛发 vs 金属头盔） | 靠接触图 |

**脸那条规则为什么成立**：战无2 给面部单独分了一根骨（`bone_46`），
脸壳（含脖子/头皮）几乎整块绑在它上面，而头盔、头发、身体都不绑。
眼球另有自己的骨（`bone_61`/`bone_62`）。

---

## 五、目录

```
tools/sw2-pipeline/
├── README.md                 本文件
├── run_identify.py           批量驱动（系统 python）
├── build_heads.py            一条命令出 28 张脸（挑件→切嘴→标定→通道→贴图→关卡 1）
├── build_armors.py           一条命令出 28 套甲（读骨普查选件 → 甲管线）
├── build_helmets.py          挑件表 helmet 列出头盔（9 人戴盔，8 人已做）
├── build_weapons.py          28 件武将武器（`--set troops` = 6 件兵种通用武器）
├── gen_weapon_items.py       武将武器物品定义 + 中文名 + 两张 CSV 登记 + 弹药自给
├── gen_troop_weapon_items.py 兵种通用武器物品定义（独立文件 troop_weapons.xml）
├── check_materials.py        🔴 导入编辑器之前的闸门（材质名三件齐 + 可选脖子，秒级，纯 python）
├── check_regression.py       信长回归闸门（改任何几何步骤后必跑）
├── check_assembly.py         🔴 拼装闸门：三件按**共用源变换 T** 拼回一个整体，量三条硬判
│                             ① 脖子一圈「最长连续露缝弧」≤ `--max-arc`（默认 60°）
│                             ①' 任何**单档** gap ≤ `--gap-max`（默认 30mm，弧判据之外独立生效）
│                             ③ 不穿模（领口上沿 +5mm 之上，头/兜/脖子与甲不得互相插入）
│                             ④ 无孤立浮片（碎片到其它任何片的最小距离 ≤ `--float-tol`，默认 50mm）
│                             （逐档 gap 表 / 头壳开口沿旧口径对照 = 诊断，不参与总判；
│                              `--key` 单人细表 / `--all` 28 人汇总；已接进 `Scripts/run_all_checks.py`）
│                             🔴 两个"量法"要点：①头侧下沿 = `parts_table.neck_args(key)` 抠出来的
│                             那截脖子（抠到就只认它）；②领口环从「甲件 + **待看**件」里长
│                             （待看 = 普查没定论，不是"不是甲"；实测武藏领口缺的半圈就在待看件里）
├── verify_table.py           挑件表 × 零件表 逐项核对（381 项）
├── parts_table.py            28 人的挑件表
└── scripts/
    ├── identify_parts.py     零件识别（Blender，测量 + 初判 + 出接触图）
    ├── count_islands.py      数每块件的连通域数（排查"一块里混了几样东西"）
    ├── make_sw2_textures.py  贴图来源（两条路线，见下方「贴图升级」）
    ├── build_weapon.py       单件武器核心：挑件 → 握持点/长轴规范化 → 出 FBX
    ├── render_weapons.py     武器对照图（--side 看侧面 / --textures 带贴图渲染）
    └── render_heads.py       批量产物排成对照图（⚠️ 见下方"已知问题" / 只画几何，不能验贴图）
```

**导入编辑器之前先跑这个**（顺序：`build_heads.py` → `check_materials.py` → 人工导入 → `install_pack.py`）：

```bash
python tools/sw2-pipeline/check_materials.py              # 28/28 合规才动手
python tools/sw2-pipeline/check_materials.py --self-test  # 自证：造坏数据必须被拒
```

它查两件事：**必备文件齐**（`head_*_a_v2.fbx` + `_d/_n/_s/_eye_d/_mouth_d` 5 张贴图；脖子那三张
`_neck_*.png` 可选，但要么齐要么都没有）与
**材质名 `<裸名>` / `<裸名>_eye` / `<裸名>_mouth` 必须齐、每个恰好出现一次，可选多一个 `<裸名>_neck`**。
第二条是铁律 27 的闸门：后处理 `skinfix --fullmat` 按材质名判角色（`MatRole()` 只认 mouth/lash/brow/shadow/eye，
其余一律当 face），名字重复 = 几件刷成同一个配方 = **眼睛和嘴糊上脸皮**，且要到实机才看得出来。
🔴 2026-09-16 起角色词扩到含 **neck**（脖子 = 头的第 4 个部件，材质名 `<裸名>_neck` 会自动拿到脸壳配方）。

产出落 `Debug/offline/外观批量导入/sw2_parts/`（模块根唯一产物根下，见 CLAUDE.md 铁律 26）。

---

## 六、坑（写工具时踩过的）

| # | 坑 | 症状 | 修法 |
|---|---|---|---|
| 1 | **源模型是厘米不是米** | 写死米制 z 阈值 → 15 块零件全判成「头」 | 一律换算成**占全身高度的比例**；全身高度要排除武器/驱动件（长枪会把范围撑出去） |
| 2 | **Blender 的相对渲染输出路径** | 打印了「已写出」但磁盘上没有文件 | 渲染输出一律 `os.path.abspath()`（`filepath` 是相对 blend 文件解析的） |
| 3 | **编号牌被相机裁掉** | 接触图上完全没有编号 | 相机退到很远（正交相机距离不影响大小）；编号牌放在「比所有零件更靠近相机」的位置 |
| 4 | **UV 采样慢到不可用** | 每采一个面就复制一次整张图 | 图片像素每种材质只取一次，放在面循环**外面** |
| 5 | 战无2 FBX 的 TWT morph 缺 FullWeights | Blender 导入器断言崩溃 | 内存级补丁（`patch_importer()`，范本同 `build_head.py`） |
| 6 | 🔴 **摆放零件时漏乘原物体的 `matrix_world`** | **武器件在接触图上是空白格**（其实画到别的格子里去了） | 包围盒按**世界**坐标算，摆放却对**局部**顶点做 → 末尾必须补 `@ ob.matrix_world`。FBX 导入的 `.001` 武器件自带宽高变换，最容易中招 |
| 7 | **武器/驱动件的原材质渲染不出来** | 即使摆对了位置也是空白 | 给这两类套一层纯色兜底材质（武器=金色、驱动=蓝色） |
| 8 | 🔴 **一块件里混着语义不同的几样东西** | 例：杂贺孙市 idx8 = 头发+长外套；归蝶 idx6 = 身体+发髻；秀吉 idx0 = 甲+兜+日轮冠。整块拿 → 头发里拖着外套飞到头上去 | 按**碎片的主导骨骼**分组筛选（**不是**按连通域——所有件本身就是几十上百片碎片云）。见下方「七」 |

---

## 七、🔴 「一个对象 ≠ 一样东西」—— 取件时怎么把不相干的东西甩掉

### 实测事实（`count_islands.py`，全量实测）

**源模型的所有零件都是「碎片云」，不是一整块。**（PS2 时代的模型本来就不焊顶点）

| 角色 | 件 | 顶点 | 连通域数 |
|---|---|---|---|
| 丰臣秀吉 | idx 0 | 736 | **66** |
| 归蝶 | idx 6 | 1241 | **86** |
| 杂贺孙市 | idx 8 | 551 | **40** |

**所以「按连通域拆分」这条走不通**（切完还是几十片）。

### 真正的现象：一块件里住着**语义不同的几样东西**

多个角色实测（逐件单独渲染 + 按骨骼核对）：

| 角色 | 件 | 里面混了什么 |
|---|---|---|
| 杂贺孙市 | 8 | 头发 **+ 长外套** |
| 武田信玄 | 10 | 红发 **+ 腰间红披** |
| 伊达政宗 | 7 | 身甲 **+ 大月牙前立** |
| 归蝶 | 6 | 身体 **+ 发髻** |
| 服部半藏 | 10 | 脸 **+ 布头罩** |
| 丰臣秀吉 | 0 | 甲 **+ 兜 + 日轮冠** |

**为什么必须处理**：换头/换甲是**整块拿走**的。整块拿 → 头发里会拖着一件外套飞到头上去。

### 拆法：**按骨骼分组**，不按连通域

碎片虽然多，但**每片绑在哪根骨头是明确的**——
头发那几十片全绑 `bone_11`（头骨），外套那几十片绑脊柱/手臂。
所以：**按「碎片的主导骨骼」分组，只留目标那一组**。判据沿用本文档已有的那三条
（主导骨骼 / 包围盒高度 / 主色），不用新造。

**状态**：已识别，**拆分工具待写**（见 `plans/战国无双换装批量落地.md`）。

---

## 八、武器（阶段 3.5，2026-09-15）

### 为什么不能直接拿源件：源模型是 T-pose，武器**横躺在手上**

实测 28 人：武器件长轴几乎全是 **±X**（水平横放，穿过手掌），而骑砍2 的武器网格约定是
**原点=握持点、长轴 +Z、刃宽沿 X、刃厚沿 Y**（依据：原版 `sturgian_blade_9` 拼件、
`torch_g` 整体武器、织丰 `sho_bokken_katana` / `sho_new_yumi_1` 三处实测一致）。
所以必须做一次刚体变换。

### 规范化怎么算（`scripts/build_weapon.py`，全自动）

| 步 | 判据 | 实测 |
|---|---|---|
| **握持点** | 离武器最近的**手骨**（战无2 手骨族 `bone_18/19/26~45`）在长轴上的投影 | 手骨到武器表面 0.2~40cm（多数 <3cm） |
| **长轴** | 武器顶点 PCA 第一主成分 | 28/28 都是 ±X |
| **刀尖朝哪端** | 长轴上离握持点**更远**的那一端 | |
| **旋转** | 长轴(刀尖向)→+Z、次轴→+X、第三轴→+Y（三轴正交，det=+1） | 与原版 blade 逐轴一致 |
| **平移/缩放** | 握持点→原点；源件是厘米，×0.01 | 握把跨原点（原版 torch 也是） |

**⚠️ 37cm 那次虚惊**：幸村的手骨离武器"最近顶点"40cm —— 不是握持点错了，是**网格稀疏**
（枪杆只有 3 个截面 x=-230/-132/-34，手骨落在长面中间）。判据看的是"手骨在长轴上投影落不落在武器范围内"。

### 类型与数值（`gen_weapon_items.py`）

武器类型**按形态指定**（`WEAPON_OF` 表，不按长度自动分——军配/弓/铁炮的长度和用法不成比例）：

| 类型 | Type | weapon_class | item_usage | 谁 |
|---|---|---|---|---|
| `polearm` | TwoHandedWeapon | TwoHandedPolearm | `polearm_block_thrust` | 12 人（枪/薙刀/伞柄） |
| `sword2h` | TwoHandedWeapon | TwoHandedSword | `twohanded_block_swing_thrust` | 8 人（太刀） |
| `sword1h` | OneHandedWeapon | OneHandedSword | `onehanded_block_swing_thrust` | 6 人（短刀/军配/镰/忍具） |
| `bow` | Bow | Bow | `bow` | 稻姬 |
| `gun` | Crossbow | Crossbow | `crossbow` | 杂贺孙一（铁炮） |

🔴 `weapon_class` 取值必须 ∈ 引擎 `WeaponClass` 枚举、`item_usage` 必须 ∈ 原版
`Native/ModuleData/item_usage_sets.xml`（**织丰的 `musket` 是它自定义的 usage，不能用** —— 铁则 6 零依赖）。

**`weapon_length`** = 近战取「握持点→刀尖」（`above_m`），远程取全长；下限 40cm。

**远程必须配弹药**：弓/铁炮挂进装备栏时 `Item1/Item2` 放箭/弩矢（原版弓手就是 `Item0=弓 Item1=箭`），
否则拿着射不出去。弹药 id 写在 `TaikouHero.csv` 的「弹药」列。

### 坑

| # | 坑 | 症状 | 修法 |
|---|---|---|---|
| 1 | **`Item.` 引用必须自给** | 接完线 `check_taikou_xml_references.py` 报 `Item.piercing_arrows` / `Item.bolt_e` 悬空 | Taikou 虽依赖 SandBoxCore，但按内容包自给纪律原版物品**不算**可用 → 从 SandBoxCore 拷定义进 Taikou（`gen_weapon_items.ensure_ammo()`，culture 改 `ikoku`） |
| 2 | 🔴 **缺 UV 的小件会拖垮整件** | 小次郎/政宗/归蝶的武器导出来没 UV（贴图不生效） | 这 3 件各有一个**无 UV 的子件**；合并时按环逐个取、缺的补 (0,0)，**不能整件放弃 UV** |
| 3 | **加中文名后英文层过期** | `run_all_checks` 报 `gen_taikou_english_strings.py` 红 | 英文层从 XML 内联 fallback 抽取 → 加完键要重跑它 |
| 4 | 🔴🔴 **武器贴图 ≠ 角色图集** | 庆次的枪杆渲染成**金色**、枪头**丢金属银**（查看器里是深色杆 + 银灰枪头） | 战无2 给**每把武器配了独立贴图**（28/28）：读 `web/weapons/manifest.json` 的 `models[<角色>].tex` → `web/weapons/tex/<tex>.png`。武器的 UV 是**相对那张贴图**画的，拿角色图集去采 = 采到不相干区域。**甲的贴图仍旧用角色图集**（甲与角色共用图集，那条是对的） |
| 5 | **武器贴图不要放大** | 28 张 diffuse 占 **25.4 MB** | 源图实测只有 128×32 ~ 256×512（19 张是 256×64），放大到 2048 只是插值变糊 + 体积涨 20 倍 → `make_sw2_textures.py --no-upscale`（只缩不放）→ **0.98 MB** |

**验收图**：`render_weapons.py --dir tools/armor-pipeline/out --out <目录>`（`--side` 看侧面、`--textures` 带贴图）。

---

## 八之二、兵种通用武器（阶段 3.6，2026-09-15）

### 为什么要挑：源资产的通用武器有 10 件，只有 6 件能融进骑砍的武器体系

战无2 的兵种模型里嵌着 10 件通用武器（材质名 `mat_w_*`）。逐件建出来看形态后，
**4 件不能用**——骑砍没有对应的物品类型或碰撞形状：

| 砍掉的 | 实际形态 | 为什么不行 |
|---|---|---|
| `w_ironball` | 直径 32cm 的**球** | 无柄无刃；近战要网格有握持点+长轴，投掷要飞镖/飞刀形状 |
| `w_bombA` | 带引信的**陶壶** | 骑砍没有爆炸物物品类型 |
| `w_rolling` | 0.55m 弯粗棍 | 形态不明、与打刀价值重叠 |
| `w_pcB0` | 与 `w_longspear` **同一把枪** | 包围盒 x/y 完全相同，只长度差 → 纯重复 |

保留的 6 件（`build_weapons.TROOP_WEAPONS` 是唯一真源）：

| mesh | 形态 | 长度 | 类型 | 谁在用 |
|---|---|---|---|---|
| `taikou_troop_yari_weapon_a` | 长枪 | 285cm | TwoHandedPolearm | 枪足轻/刀足轻/侍/农民/九州兵×2 |
| `taikou_troop_naginata_weapon_a` | 薙刀 | 193cm | TwoHandedPolearm | 头目 |
| `taikou_troop_uchigatana_weapon_a` | 打刀 | 90cm | OneHandedSword | 精锐足轻/侍大将/护卫×6 |
| `taikou_troop_shinobigatana_weapon_a` | 忍刀 | 78cm | OneHandedSword | 下忍/中忍/飞忍/上忍 |
| `taikou_troop_yumi_weapon_a` | 弓 | 196cm | Bow | 弓足轻 |
| `taikou_troop_teppo_weapon_a` | 铁炮 | 138cm | Crossbow | 铁炮足轻 |

### 🔴 兵种武器**必须**单开文件和单开生成器

`gen_weapon_items.py` 的 `prune()` 会**删掉所有不在 28 武将名单里**的 `taikou_*_weapon_a`
条目 —— 兵种武器写进同一个文件，下次跑武将生成器就被清掉。

⇒ 兵种武器落 `taikou_items/troop_weapons.xml`，中文名走自己的哨兵。

✅ **不需要改 SubModule**：引擎按**目录**加载物品表
（`SubModule.xml` 只有 `<XmlName id="Items" path="taikou_items"/>`），新文件丢进去自动生效。

### 数值口径：沿用武将那张 `KINDS` 表，**不按兵种分档**

同一个 mod 里同一类武器只能有一套数。兵种强弱由 `spnpccharacters.xml` 的
level + skill_template 区分 —— 骑砍的伤害 = 武器伤害 × 技能倍率，
同一把枪在 Lv6 足轻和 Lv25 武将手里差得很远，不需要靠武器数值分层。

### 命名口径：兵种是**通用装备（无归属者）**，直接给裸名

不加 `[xx之武]` 归属结构（依据 `全角色武器甲胄兜名表.md` §三）。
所以英文 fallback 也是裸名（"Long Spear" 而非 "X's Spear"）——
`gen_weapon_items.item_block()` 为此多了 `fb_name` / `label` 两个可选参数（向后兼容）。

### 跑法

```bash
python tools/sw2-pipeline/build_weapons.py --set troops          # 建 6 件网格 + 贴图 + 尺寸表
python tools/sw2-pipeline/gen_troop_weapon_items.py              # 物品定义 + 中文名 + item.csv
python tools/sw2-pipeline/gen_troop_weapon_items.py --check      # 幂等校验
python Scripts/gen_taikou_english_strings.py                     # 🔴 加了中文键必须重跑，否则体检红
python tools/sw2-pipeline/stage_for_import.py --set troops       # 归拢到 TifaHead2/AssetSources/sw2/troop/
```

**已知欠账**：`gen_troop_weapon_items.py --check` **没接进** `Scripts/run_all_checks.py` ——
同类的那三个 sw2 生成器（`gen_armor_items` / `gen_weapon_items` / `gen_helmet_items`）也没接，
要接就四个一起接（runner 要求脚本在 `Scripts/` 下且认 `--module` 参数）。

---

## 八之三、🔴 贴图升级（2026-09-16）

**一句话**：贴图的**来源图**换成源工程超分过的图（`D:\BrainMaker\战国无双2资产解包分析\work\tex_batch\`），
脸的 `_n` 从**纯色平法线**换成**真法线**，甲/武器的输出分辨率顺势翻倍。**管线结构没动，只改"读哪张图"。**

### 为什么必须换（实测）

| 项 | 旧 | 新 |
|---|---|---|
| 脸/甲的 `_d` 清晰度（拉普拉斯方差） | **71.9**（= 纯插值放大的值，零真实细节） | **2456** |
| 脸的 `_n` | 纯色 `(128,128,255)`，std = **0** | 真法线，std ≈ **19** |
| 甲输出 | 512×780 | **1024×1548** |
| 武器输出 | 256×64 | **512×128** |

> 超分图与原图的**低频相关性 0.99998** → UV 锚点零位移，换图不会错位。

### 三条路线怎么走

| 脚本 | 开关 | 行为 |
|---|---|---|
| `build_heads.py` | 默认升级；`--no-tex-upgrade` 回退 | 脸 `_d`+`_n` 用升级图，`_s` 仍纯色 |
| `build_armors.py` / `build_helmets.py` | 默认升级；`--no-tex-upgrade` 回退 | 甲 `_d` 用升级图 + `--tex-scale 2`；`_n`/`_s` **仍自己生成** |
| `build_weapons.py` | 默认升级；`--no-tex-upgrade` 回退 | 武器 `_d`+`_n` 用升级图，`_s` 仍纯色 |

### 为什么甲**不用**源工程的法线

甲有**自己的**法线管线（`build_textures.py` 从漫反射提高频），它会避开图集里
**画上去的光影** —— 画师的阴影不是几何，源工程的法线不区分这个。
武器和脸则相反：它们**没有**法线管线（原来就是纯色），换上真法线是净收益。

### 坑（本轮真实踩到）

| 坑 | 现象 | 修法 |
|---|---|---|
| 🔴 **留边不能按 UV 定** | 放大 2 倍后同一个 UV 留边 = 12 像素 → 信长甲裁成 1024×**2048**（顶到图集边界），多出来的是**没用的图集区域（脸）**：白涨体积 + 法线把脸当表面细节 | `build_textures.py` 的留边改成**按像素**定（6 px，换算基数固定 512 宽）→ 2 倍裁 1024×**1548**，1 倍仍是 512×**780**（与旧版逐像素相同）|
| 🔴 **`--tex-scale` 别当"放大倍率"用** | 一度把像素坐标也乘上它 → 重复放大，裁剪框跑到界外 | 它的真义 = **"图集相对基准（512 宽）放大了几倍"**，**只**用来消掉留边的尺寸效应 |
| 🔴 **`render_heads.py` 验不了贴图** | 它只挂纯色材质画**几何**，图上那片粉色不是皮肤 | 要验贴图走 `armor-pipeline/scripts/render_textured.py`（挂 `_d`/`_n`/`_s` 三张），或直接量通道统计 |

---

## 九、已知问题

| # | 问题 | 现状 |
|---|---|---|
| 1 | 🔴 **`render_heads.py` 画出来的头是拉长的**（纵向约 2 倍），不能用来判观感 | 已排出相机取景、骨架姿态、蒙皮三条嫌疑（都验过不是），**未定位**。数字侧的另一条验收线是好的：`fbx_probe.py` 量包围盒（26/28 在正常范围）+ `check_regression.py` 信长回归全绿。**别拿这张图下结论**，要改成"逐头方图 + numpy 拼"之外的方案 |
