# 战国无双2「铁炮兵」p006 → 骑砍2 动画重定向 · 可行性结论

> 📁 **路径说明**：本文写于旧目录结构；现已重组为 `input/ pipeline/ output/ viewer/`（见顶层 `README.md`）。
> 文档里的 `out/xxx` 对应现在的 `output/fbx|trf/`，脚本对应 `pipeline/`。**内容结论不变**。

> 日期：2026-09-18 · 工作目录：`D:/BrainMaker/骑砍2动画重定向/战国无双2铁炮兵_p006_重定向`
> 输入项目：`D:/BrainMaker/骑砍2动画重定向`（骑砍2 骨骼与重定向经验）、`D:/BrainMaker/战国无双2资产解包分析`（SW2 解包与 G1A 破解）
> 核对页：`index.html`（含数值表 + 五列逐帧对比图）

---

## 0. TL;DR

**能做，而且已经做出来了。**

- 目标资产：SW2 铁炮兵 = `L256_GUNNER`；`p006` 来自其 Player（幻化）动作包 `gunner_p.pac`，**40 帧 / 1.333s / 30fps**（已确认它在 `input/source/sw2_gunner/L256_GUNNER_anim.gltf` 的 40 段动画里）。
- 结果：p006 已重定向到骑砍2 `human_skeleton`，导出 **`output/fbx/sw2_gunner_p006_alig.fbx`**（41 帧烘焙动画，含 `human_lod_4` 网格）。
- 质量：逐骨「肢段方向」误差 **mean 10.93° / RMS 21.13°**，19 根可度量骨中 **16 根 ≤0.03°**（即肢体朝向与源逐帧完全一致）。
- 用你给的两个插件也各跑了一遍（均为开箱自动配置）：**BoneAnimCopy 42.44°、BioSculpt Retargeter 68.54°**，都明显差于自研方案；原因见 §4。

---

## 1. 从两个既有项目里接过来的前提

| 来自 | 用到了什么 |
|---|---|
| `战国无双2资产解包分析` | G1A 破解成果（`input/source/sw2_gunner/L256_GUNNER_anim.gltf` 已含骨架 + 网格 + 40 段动画）；G1M/G1MS 骨架语义；`detect_hand_bone.py` 已验证「角色朝向 +Z、右手在 −X」 |
| `骑砍2动画重定向` | 骑砍2 `human_skeleton` 骨架与 `human_lod_4` 网格；「世界空间 rest-aware 增量法」思路与 `ue_to_bannerlord_retarget_world.py`；自动贴地、逐骨数值校验、并排渲染对比的做法；README 里已明确记录的**能力边界**（手写脚本无法对齐四肢绝对位置） |

---

## 2. 骨架对照（全部实测，不是推测）

| 项 | 源（SW2 铁炮兵） | 目标（骑砍2） |
|---|---|---|
| 骨数 | 47（**21 根变形骨**） | 28 |
| 单位 | 厘米（髋高 114.3） | 米（pelvis 高 0.915） |
| Blender 世界朝向 | 面朝 **−Y**、右侧 **−X**、上 +Z | 面朝 **+Y**、右侧 **+X**、上 +Z |
| rest 站姿 | **T-pose**（双臂沿 ±X 平举） | **A-pose**（手臂下垂外张） |
| 根/髋结构 | `bone_0` 总根 → 腿支 `bone_26→bone_1`、躯干支 `bone_33→bone_8` | `pelvis` 同时是腿与脊柱的**唯一**共同父骨 |

**帧变换** = `Rot(Z, 180°)`。它是正旋转，所以 **右→右、左→左，不需要额外做 X 镜像**（这点与项目里 UE→骑砍的链路不同，那条链路用的是 X 镜像矩阵）。

映射表：`sw2_to_bannerlord_map.json`，**20 对**（`bone_0→pelvis`、`bone_9→spine`、`bone_10→spine2`、`bone_11→head`，左右各 8 根肢体骨）。骑砍的 `spine1 / neck / *_twist1 / *_finger0` 在 SW2 侧无对应源骨，保持 rest 由父级刚性继承。


## 3. 三个坑（决定这次能不能成）

### 坑 1 · glTF 的根骨位移是「髋空间增量」，直接播会整体下沉 114cm
SW2 的 G1A 把根骨平移写在「以髋为原点」的编辑空间里（原始 ≈116.8cm）。Noesis 导出 glTF 时按「相对首帧增量」写盘，因此 `bone_0` 的动画位移只剩 0 → −10.5 的小量，而节点静止位移是 114.3。
**直接播放 → 人物沉到地面以下**（实测第 10 帧脚在 z≈−100、头在 z≈46）。
**解法**：取「相对首帧的世界位移增量」使用，坑即消除。

### 坑 2 · 源 rest 是 T-pose、目标 rest 是 A-pose，纯增量法系统性偏 ~20°
「只搬每根骨转了多少角度」的世界增量法忠实保留运动，但目标会保留自身站姿差。
**实测证据**：纯 delta 模式下每根骨的误差**恰好等于它自己的静止姿态差**（是常数，不随帧变化）——手臂 25–33°、大腿段 57°。

### 坑 3 · 连「骨骼自身轴向约定」都不一样，absolute（绝对朝向）法会崩
SW2 源骨在 Blender 里多数呈「Y 轴水平」，骑砍目标骨是另一套约定（其静态 FBX 尾骨沿 +Y）。把源的绝对世界朝向直接写给目标 → **骨盆被额外转 90°、整个人倒挂**（实测 mean 155.81°）。
**结论：既不能用纯 delta，也不能用纯 absolute。**

---

## 4. 最优解：align 模式（在增量法上做逐骨静止对齐）

```
F       = Rot(Z, 180°)                      # 源世界帧 -> 目标世界帧
D_src(i)= S_pose(i) · S_rest(i)⁻¹           # 源骨的世界旋转增量
R(i)    = F · D_src(i) · F⁻¹
A(i)    = 「源静止肢段方向(F 旋转后) → 目标静止肢段方向」的最小旋转
D_tgt(i)= R(i) · A(i)⁻¹
T_pose(i)= D_tgt(i) · T_rest(i)             # 父先子后写入
```

**推导性质**：src 处于 rest 时目标落回自身 rest；任意帧下目标的**肢段方向与源逐帧精确相等**（因为肢段方向 = `D_父 · 静止肢段向量`，代入即得 `R·ũ_s`）。实测 16/19 骨 ≤0.03° 印证。

**多分支骨的处理**：`pelvis` / `spine2` 同时挂两条分支，一根骨只能有一个朝向。规则是「多分支骨只认子树包含 `head` 的那条躯干延续子骨，否则不做对齐」，避免把一条腿的对齐强行加给另一条腿（早前版本正是因为这个把右腿弄坏到 159°）。

**额外一处关键修正**：`pelvis` 的**旋转**改取 SW2 的躯干支基骨 `bone_8`（位移仍取总根 `bone_0`）。因为骑砍 `pelvis` 是腿与脊柱的唯一共同父骨，而 SW2 把两支分开了；不改的话躯干恒偏 6.9°，改后降到 **0.02°**。

### 三种模式实测（肢段方向误差）—— **交付只走 align**

> **结论（2026-09-19 定稿）**：`align` 是唯一交付模式（也是脚本 `--pose` 默认值）。
> `delta` / `absolute` 保留为对照证据，产物已归档到 `out/archive/`，**不要再拿去交付或接入**。


| 模式 | 全局 mean | RMS | max | 躯干 | 左臂 | 右臂 | 左腿 | 右腿 |
|---|---|---|---|---|---|---|---|---|
| **align（推荐）** | **10.93°** | 21.13° | 66.70° | **1.49°** | 7.99° | 11.41° | 15.12° | 16.29° |
| delta | 20.29° | 26.86° | 66.70° | 1.86° | 25.13° | 27.71° | 20.55° | 21.60° |
| absolute | 155.81° | 161.27° | 180° | 178.19° | 172.46° | 174.85° | 128.79° | 130.34° |

剩下没对齐的全部是**多分支骨的那两个「挂点段」**：`pelvis→thigh`（55–60°，属 SW2 与骑砍髋关节挂点高度不同的**骨长固有差异**）与 `spine2→clavicle`（20–30°，锁骨段短、视觉影响小）。**每根肢体骨自身的朝向都已精确对齐**（如 `l_calf` 1.02–1.70°）。

---

## 5. 两个插件的对比结果（都是开箱自动配置）

### 5.1 BoneAnimCopy（`https://github.com/kumopult/blender_BoneAnimCopy`）
机理：owner（骑砍）骨上挂 `COPY_ROTATION`（WORLD/WORLD）+ 常量旋转偏移约束（`TRANSFORM`），再 `nla.bake`。建立映射时插件自动算「静止姿态差」当偏移，可选近似到 90° 倍数。

| 变体 | mean | RMS | max |
|---|---|---|---|
| 自动偏移（正交关） | 42.44° | 50.43° | 123.22° |
| 插件默认（正交开） | 43.84° | 53.46° | 133.28° |

视觉复核：躯干与腿正确，**但双臂普遍抬不到位，丢失「举枪抬臂」这个动作核心**。
→ 它是「通用工具 + 需人工逐骨调偏移 / 开 IK」的定位；本次只跑自动配置，未做人工调参。

### 5.2 BioSculpt Retargeter 2.02
机理：建代理骨架（只留映射骨、断开 Hip/Root 父级）→ 对齐代理骨 Roll → 约束代理跟随源 → 空物体桥接传递到目标 → `nla.bake` + 清理。

| 变体 | mean | RMS | max |
|---|---|---|---|
| 默认（hip 位移拷贝开） | 68.54° | 75.55° | 171.47° |
| 关闭 hip 位移拷贝 | 68.54° | 75.55° | 171.47° |

视觉复核：**姿态被前后翻转对折**（头/臀位置互换），9 帧全部与源无法对应。
→ 它的 Roll 对齐只用骨骼 X 轴在 XY 平面的投影角（`atan2`），遇到本例「T-pose 源 + A-pose 目标 + 两套骨轴约定不同」的组合会失配；它原本面向 Mixamo→BioSculpt 骨架，本例属超纲用法。
（关闭 hip 位移拷贝结果完全不变，说明**不是根位移引起的**，是旋转传递链本身失配。）

### 5.3 插件兼容修复（已落地）
- BioSculpt：安装后直接可用。
- **BoneAnimCopy 在 Blender 5.2 上「烘焙动画」会报错**：`AttributeError: 'Bone' object has no attribute 'select'`（`Bone.select` 在 5.x 已移除，改用 `PoseBone.select`）。
  已做最小兼容补丁（安装目录内）：`utilfuncs.py` 新增 `bone_select()` / `bone_is_selected()`，把 `mapping.py` 3 处、`__init__.py` 2 处 `Bone.select` 调用改为该兼容函数；原目录已备份为 `blender_BoneAnimCopy.bak_pre502patch`。补丁后 5 步流程全部 `FINISHED`。

---

## 5.5 追加：时长膨胀 25% 的 bug（2026-09-18 复盘）

用户反馈「重定向后的模型会比重定向之前的播放慢半拍」。用数据一查，**不是"半拍"，是整整慢 25%**：

| 动画 | 源时长 | 修复前 | 修复后 |
|---|---|---|---|
| a013 | 0.500 s | 0.625 s | 0.500 s |
| p006 | 1.333 s | 1.667 s | 1.333 s |
| a000 | 1.000 s | 1.250 s | 1.000 s |
| p007 | 2.500 s | 3.125 s | 2.500 s |

**根因**：`bpy.ops.import_scene.fbx()` 导入骑砍2 的 `human_lod_4.fbx` 时，Blender 按 FBX 自带的帧率把
`scene.render.fps` 从 30 改成 **24**（实测：factory reset=24 → 手动设 30 → 导 glTF 后仍 30 → **导骑砍 FBX 后变回 24**）。
之后导出按「帧 ÷ 当前 fps」算时间 → 全部 ×1.25。

**修复**：在导入 FBX **之后**再把 `scene.render.fps` 设回 30（`batch_retarget_glb.py`、
`retarget_sw2_to_bannerlord.py`、`drive_boneanimcopy.py`、`drive_biosculpt.py` 四处都已加）。
同时把烘焙帧范围由 `int()` 改为「起点 `round`、终点 `ceil`」，避免源关键帧落在非整数帧（如 14.5 帧）时被截断尾巴
——修完后 40 段时长最大偏差只剩 **0.5 帧**。

**顺手加的保险**：查看器里做**相位锁定** `actB.timeScale = speed × (目标时长/源时长)`，
即使残余半帧差也保证两人始终同一动作相位。

**同相位校验**（在 Node 中运行与浏览器**同一套 three.js 运行时**，加载两个模型后逐相位比对肢体朝向）：
40 段全部可配对，夹角 **中位 10.1° / 平均 10.1° / 最大 11.9°**，与 Blender 侧实测的 10.93° 一致
→ 既无相位差，姿态还原也达标。

---

## 6. 视觉复核（独立视觉模型，四方案对比）

> **关于侧面视角的一次自查（重要）**：首版侧面对比图里「源朝左、align 朝右」，看起来像被镜像了。
> 排查结论：**是我渲染相机的约定问题，不是重定向错误**。源在 Blender 世界系面朝 `−Y`、骑砍面朝 `+Y`（两者本就相差 180°，这正是需要 `Rot(Z,180°)` 帧变换的原因）；把侧向相机固定放在同一侧时，朝向相反的两者必然被拍成一左一右。
> 已改为「按角色各自朝向选相机侧」（面朝 `−Y` 用 +X 相机、面朝 `+Y` 用 −X 相机），重渲染后 4 列全部朝同一屏幕方向。
> **朝向一致的硬证据（数值）**：帧 0 源经帧变换后的脚尖方向 `(0.83, 0.23, −0.51)` 与骑砍 align 结果**逐位相同**；全程 `l_toe0` 夹角 mean **2.08°**、`r_toe0` **1.89°**、`head`（躯干朝向）**1.94°**。



源动作：整段「站立 + 双臂抬起在胸前/面部高度握铁炮」，躯干不转体，腿部仅小幅度前后重心切换。

| 方案 | 复核结论 |
|---|---|
| 自研 align | 躯干/臂/腿方向均与源一致，多数帧双臂高度贴近源；**未发现结构性缺陷**（相机约定修正后复验：9 帧躯干前倾/手臂高度/腿部姿态全部吻合，无不一致帧） |
| 自研 delta | 与 align 几乎同档，个别帧（25/30）手臂略低 |
| BoneAnimCopy | 躯干与腿正确，**双臂偏低、举枪动作丢失** |
| BioSculpt | **姿态崩溃（翻转对折）**，不可用 |

排序：**自研 align ≈ 自研 delta ≫ BoneAnimCopy ≫ BioSculpt**。四方案均未出现左右手镜像反、脚悬空或插地。

---

## 7. 诚实的边界（还没做到什么）

1. **四肢绝对位置对不齐**：源与目标骨长/比例不同（SW2 髋高 114.3cm、身高约 202.6cm；骑砍 pelvis 0.915m），「方向一致」不等于「关节落点一致」。这是跨骨架固有差异（与 `docs/README_复盘.md` 记录的结论一致），要做到需 rest 对齐 + **骨长归一**（Auto-Rig Pro / MotionBuilder 的强项）。
2. **手部 twist / 手指无源可映射**：骑砍 `*_twist1`、`*_finger0` 在 SW2 侧没有对应骨。
3. **根位移**：p006 基本原地（髋部 ±10cm 上下、≈5cm 前后），本次用「自动贴地」保证脚在地面；带位移动作可用脚本的 `--pelvis delta`（厘米→米 ×0.01）。
4. **未做游戏内接入**：FBX 尚未经骑砍 ModKit 导入建 Clip、未接 `SetActionChannel`、未做 foot IK 验证。
5. **武器未随动**：铁炮挂在 SW2 自己的手骨上；本次只重定向骨架动画，未把武器模型挂到骑砍手骨。

---

## 8. 文件清单

| 文件 | 说明 |
|---|---|
| `output/fbx/sw2_gunner_p006_alig.fbx` | ★ **推荐成品**：p006 重定向到骑砍2 `human_lod_4`，41 帧烘焙动画（28 骨 / 只导骨架 / Z-up / cm / 根名 `human_skeleton_notused`） |
| **`output/trf/sw2_gunner_p006_alig.trf`** | ★ **ModKit 用的 TRF**（绝对局部变换语义），由重定向脚本**自动**导出，已过 `CHECK_OK` / `CHECK_POS` |
| `_legacy/algorithm_archive/sw2_gunner_p006_delt.fbx` | delta 模式对照成品 |
| `_legacy/algorithm_archive/sw2_gunner_p006_abso.fbx` | absolute 模式（反例，用于说明坑 3） |
| `_legacy/algorithm_archive/bac_sw2_p006.fbx`、`_legacy/algorithm_archive/bac_ortho.fbx` | BoneAnimCopy 产出（两种偏移设置） |
| `_legacy/algorithm_archive/biosculpt_sw2_p006.fbx` | BioSculpt 产出 |
| `sw2_to_bannerlord_map.json` | 20 骨映射表 + 帧变换 + 根旋转源 + 单位换算 |
| `retarget_sw2_to_bannerlord.py` | 自研重定向脚本（`--pose align/delta/absolute`，`--pelvis ground/delta/none`） |
| `drive_boneanimcopy.py` / `drive_biosculpt.py` | 两个插件的无头驱动脚本 |
| `measure_retarget.py` / `compare_methods.py` | 同口径度量（肢段方向）与汇总 |
| `render_clip.py` / `make_sheet.py` / `make_html.py` | 逐帧渲染 / 对比图 / 核对页生成 |
| `index.html` | **核对页**（结论 + 数值表 + 逐骨误差 + 五列对比图） |
| `output/verify/sheet_front.png`、`output/verify/sheet_side.png` | 五列逐帧对比图（源 / BioSculpt / BoneAnimCopy / 自研 delta / 自研 align） |
| `out/measure_*.json`、`out/dump_*.json` | 原始度量数据（可复算） |

---

## 9. 复现命令

```bash
# 自研重定向（align 最佳 / delta 对照）
blender -b --python retarget_sw2_to_bannerlord.py -- --pose align --pelvis ground \
        --name sw2_gunner_p006_alig --dump output/verify/dump_align.json

# BoneAnimCopy 插件
blender -b --python drive_boneanimcopy.py -- --name bac_sw2_p006

# BioSculpt Retargeter 插件
blender -b --python drive_biosculpt.py -- --name biosculpt_sw2_p006

# 同口径度量 + 汇总
blender -b --python measure_retarget.py -- --cand output/fbx/sw2_gunner_p006_alig.fbx --name mine_align
python compare_methods.py

# 对比帧图 + 核对页
blender -b --python render_clip.py -- --kind fbx --path output/fbx/sw2_gunner_p006_alig.fbx \
        --face=+Y --frames 0,5,10,15,20,25,30,35,40 --out out/frames --prefix tgt_alig --views front,side
python make_sheet.py front ; python make_html.py
```

---

## 10. 版权

《战国无双2》全部资产版权归 **光荣特库摩** 所有；《骑马与砍杀2：霸主》相关资产版权归 **TaleWorlds** 所有。
本项目仅用于个人学习与技术研究，**不得再分发或商用**。
