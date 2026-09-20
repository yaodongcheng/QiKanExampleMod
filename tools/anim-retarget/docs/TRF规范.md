# FBX → TRF 骨骼动画转换 + ModKit 导入避坑

目录：代码真身已入库 `tools/anim-retarget/`；数据面在 `D:\BrainMaker\骑砍2动画重定向\`
最后更新：2026-09-20（**第 3 版：修正平移语义** —— 旋转才是绝对，平移是纯增量）

> **改过两次，别只记住一半**：
> · 第 1 版写「旋转和平移都存相对 rest 的增量」→ **旋转那半是错的**（第 2 版改对）。
> · 第 2 版写「旋转和平移都存绝对局部变换」→ **平移那半是错的**（本版改对）。
> **正确说法**：**旋转 = 绝对局部变换；平移 = 相对静止姿势的纯增量**。两条不一样，见 §1「核心语义」。

---

## 0. 目录内容

| 文件 | 状态 | 说明 |
|---|---|---|
| `教程存档/骑砍Ⅱ霸主MOD开发-…SkeletonAnimation.mp4` | 参考 | 教程原片（5:53–5:55 是导出脚本镜头） |
| `docs/教程存档/trf_skeleton_animation_exporter.py` | ⚠️ **语义有坑** | 从教程 5:53–5:55 还原出的脚本（133 行，原样）。它**两条轨道都写增量**：旋转写 `pose_bone.rotation_quaternion`（错，只有骨骼静止朝向规整时才碰巧等价于绝对）；平移写 `location` 而不补 `rest_rot`（也错）。**别照抄** |
| `_legacy/trf_deprecated/retarget_to_official.py` | ⚠️ **作废** | 基于「rest 基准必须一致」的错误推论；旋转用绝对语义后不需要重定向 |
| **`pipeline/common/fbx_to_trf.py`** | ✅ **正确** | 旋转绝对 + 平移纯增量，**当前唯一该用的**（原有 `fbx_to_trf_fixed.py` 与它是同一支，2026-09-20 已并入） |
| `output/fbx/sw2_gunner_p006_alig.fbx` | ✅ 输入 | 28 骨、0 网格、根节点 `human_skeleton_notused` |
| **`tools/OpenTrf/out/sw2_gunner_p006_alig_abs.trf`** | ✅ **实机验证通过的黄金样本** | md5 `b78076e31c1e5f38…`，位置轨首帧 `(0, 0, 0)` = 纯增量。同一份已进 `TaikouAnim/AssetSources/animations/gun/` |
| `output/trf/sw2_gunner_p006_alig_abs.trf` | ❌ 作废（本表原标 ✅，**标反了**） | 名字里的 `_abs` 就是它的问题：位置轨首帧 `(0, 0.0202, 0.8600)` = 绝对语义，实机抬高约 6cm |
| `output/trf/sw2_gunner_p006_alig.trf` | ❌ 作废 | 同上，绝对语义 |
| `_legacy/trf_deprecated/sw2_gunner_p006_retarget.trf` | ❌ 作废 | 更早一版 |
| `docs/教程存档/README.md` | 存档说明 | 为什么那份还原脚本不能照抄 |
| `rest_pose_diff.png` | 过程证据 | 证明「增量当绝对用」会把姿态炸开 |
| `_legacy/trf_deprecated/README.md` | 作废件清单 | 说明每个文件为什么作废 |

> ⚠️ 上表里的 `output/...`、`_legacy/...` 路径是**迁移前**的（当时整个工程还在 `D:\BrainMaker\OpenTrf\`）。
> 现在代码真身在 `tools/anim-retarget/`，数据面在 `D:\BrainMaker\骑砍2动画重定向\`。

---

## 1. TRF 是什么

TaleWorlds 的**纯文本资源容器**，格式是 `rfver 4` + `<子类型> <版本>`。

**mesh 型**（取自游戏本体 `building_opt.trf`）：

```
rfver 4
mesh 4
mi_ship_2.0 0 map_icons_01
126
0.454374 -0.867794 0.067052
...
```

**skeleton_anim 型**：

```
rfver 4
skeleton_anim 1
<动画名> 1
 <骨骼数>
  <该骨骼的旋转帧数>
  <帧号> <x> <y> <z> <w>          ← 四元数，6 位小数
  ...
  <下一根骨骼 ...>
 <根骨骼的位移帧数>
  <帧号> <x> <y> <z>
end
```

### ★ 核心语义：旋转写「绝对局部变换」，平移写「纯增量」

这是整个管线最容易搞错的一条 —— **两条轨道的语义不一样**：

```
旋转       = rest_local ∘ delta        （绝对：该骨相对父骨的完整变换）
平移(根骨)  = rest_rot @ delta_loc      （纯增量：🔴 **不叠加** rest_local.translation）
```

其中 `rest_local` = 该骨骼相对父骨的静止矩阵（根骨则相对骨架原点），`delta` 才是 Blender 的 `pose_bone.rotation_quaternion` / `.location`。

**两条各自容易怎么错**：

| 轨道 | 写错的方式 | 实机后果 |
|---|---|---|
| 旋转 | 直接用 `pose_bone.rotation_quaternion`（增量） | 每根骨被摆到零旋转 → **人趴地上、四肢乱折**（骑砍 `human_skeleton` 是 A-pose，每根骨静止朝向都不同，增量 ≠ 绝对） |
| 平移 | 多叠了 `rest_local.translation`（绝对） | **人整体被抬高约 6cm**（引擎自带的动画要适配不同身高的角色，位置轨必然存「相对静止姿势的增量」） |

**两个验收判据**：

| 判据 | 期望 | 错误时的表现 |
|---|---|---|
| 位置轨首帧 | **≈ 0**（纯增量） | 量级是米（≈0.86）= 误用了绝对语义，**产物作废** |
| 旋转的绝对公式 vs `pose_bone.matrix` 交叉核对 | < 0.5°（数值噪音） | 会显著偏离 |

还有一个**与骨架无关性**（这条只依赖旋转那半）：绝对语义下，消费端骨序链乘 `R_arm(b) = R_arm(parent) · R_abs(b)` 只取决于 abs 值本身 —— 所以**不需要**把动画重定向到官方骨架，直接导即可。

> 出处：`plans/rules/wheels.d/assets.md` §15.2（雷 1 旋转）/ §15.3（雷 2 平移），两条都实机踩过。

### 三条格式规则

1. **全骨骼写旋转，只有 `bones[0]`（根骨）写位移**，位移块在所有旋转块之后单独写一段。
2. **文件里不存骨骼名**，消费端**按索引**对号入座 → 输入 FBX 的骨骼顺序必须与目标骨架一致（见 §3）。
3. **时间列就是 Blender 的帧号**，所以 TRF 的时间跨度 = 导出帧范围 → 直接决定 ModKit 里 `Source 1 / Source 2` 该填多少（见 §4）。

补充：行尾统一 CRLF；缩进教程脚本用 1 个空格、ModKit 自己导出的 `EmAssetPackages/TRF/test.trf` 用 Tab —— 两种都在能用的工程里出现过，缩进不是硬要求。

---

## 2. 正确流程（端到端）

```
① 骨架准备
     确认骨数 = 28、骨序 = 权威序、父骨关系 = 官方   （见 §3）
     ├─ 多出 _end 叶骨 ──▶ 从源头关掉 add_leaf_bones 重导（不要靠后处理）
     └─ 干净 ──────────▶ 进入 ②
② 在 28 骨骨架上做重定向 / 逐帧烘焙（此时索引 == 权威序，不会错位）
③ 导出 FBX（按 ModKit 规格，见下表）
④ 导 TRF（用 `pipeline/common/fbx_to_trf.py`，语义见 §1）
⑤ ModKit 导入 → 填 Source 1 / Source 2 / Duration（见 §4）
```

### 步骤 ③ 的 ModKit FBX 规格

| # | 规格项 | 要求 | 复核状态 |
|---|---|---|---|
| 1 | 网格 | 动画导出**只导骨架**，不带 mesh | 现件 0 网格 ✅ |
| 2 | 叶骨 | **28 根**（`add_leaf_bones=False`） | 我用导出实验复核过 ✅ |
| 3 | 根节点名 | `human_skeleton_notused` | 现件如此 ✅ |
| 4 | 轴向 | Z-up | 转述自另一 agent，**我未独立复核** |
| 5 | 单位 | cm（导出时 `scene.unit_settings.scale_length=1.0`） | ✅ 2026-09-19 实测复核：产物骨架高 1.492，与官方 `human_lod_4.fbx`（1.575）同量级 |

> 注：轴向/单位只影响 **ModKit 消费的那个 FBX**；对 TRF 本身无影响（TRF 存的是骨骼局部量，链式相乘与全局轴向无关）。

### 步骤 ④ 命令

```bash
"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" \
  --background --factory-startup --python-exit-code 1 \
  --python "D:/BrainMaker/骑砍2动画重定向/pipeline/common/fbx_to_trf.py" -- \
  --fbx "D:/BrainMaker/骑砍2动画重定向/output/fbx/sw2_gunner_p006_alig.fbx" \
  --out "D:/BrainMaker/骑砍2动画重定向/output/trf/sw2_gunner_p006_alig.trf" \
  --name "sw2_gunner_p006_alig"
```

> `--name` 是 **TRF 第 3 行的动画名**，**不要填骨架名**（`human_skeleton_notused` 是 FBX 侧的事）。
> 缺省值 = 输入 FBX 的文件名（去扩展），本工程的命名天然就是对的，所以一般不用管这个参数。

输出里必须看到这两行：

```
CHECK_OK:  绝对公式与 Blender pose 矩阵一致（最大偏差 0.0560°，属数值噪音）
CHECK_POS: 位置轨首帧 = (0.0000, 0.0000, 0.0000)   ← 应 ≈ 0（纯增量）；若是 ~0.86 则误用了绝对语义，作废
```

`--end` **含**最后一帧（内部 `range(start, end+1)`）；缺省取动作自身 `frame_range`。

---

## 3. 叶骨 28 ↔ 33：机制与正确流程

### 机制：`add_leaf_bones`

Blender 的 FBX **导出器**有个默认开启的 `add_leaf_bones`，会给每一根叶子骨（没有子骨的骨）在末端补一根 `_end` 骨来表示骨骼朝向终点。骑砍 `human_skeleton` 正好有 5 根叶子骨：

| 叶子骨 | 补出来的骨 |
|---|---|
| `head` | `head_end` |
| `l_toe0` / `r_toe0` | `l_toe0_end` / `r_toe0_end` |
| `l_finger0` / `r_finger0` | `l_finger0_end` / `r_finger0_end` |

**28 + 5 = 33** —— 数字对得上。

我用同一个源骨做了导出实验，完全复现：

| 操作 | 骨数 | 结果 |
|---|---|---|
| 源 FBX | 28 | `pelvis … r_finger0` |
| 导出 `add_leaf_bones=True`（默认） | **33** | `_end` **内插**在 idx **5 / 10 / 16 / 24 / 32** |
| 导出 `add_leaf_bones=False` | **28** | 与源骨顺序逐根一致 ✅ |

### 为什么这 5 根会毁掉一切

TRF 是**纯索引**格式（不存骨骼名，见 §1 规则 2）。而这 5 根 `_end` 不是排在末尾，是**内插在链中间**的：

```
idx  5  l_toe0_end        ← 插在 l_toe0(4) 和 r_thigh 之间
idx 10  r_toe0_end
idx 16  head_end
idx 24  l_finger0_end
idx 32  r_finger0_end
```

于是从 idx 5 起后面所有骨骼的索引全部 +1（到 head 之后 +2，到 clavicle 之后 +3）—— 33 骨版本导出的 TRF 从第 5 块开始就是错的。
（注：删掉这 5 根后顺序**自动回到权威 0..27**，所以"导出后再删"能救回来，但只是兜底。）

### 权威索引怎么认

`modding_resources/skeletons/human_skeleton.fbx` 里骨骼名带 `_N` 后缀，**这个 `_N` 就是权威索引**：`bip01_pelvis_0` → 0，`bip01_r_finger0_27` → 27。

另有 3 根 `_nub_notused`（`l_toe0_nub_notused` / `toe0_nub_notused` / `head_nub_notused`），它们在 Blender 导入后会内插在 idx 5/10/16，但**不参与 `_N` 编号**，是 `notused` 残骨，游戏侧按 `_N` 取，不影响。

### 正确流程

| | 做法 | 评价 |
|---|---|---|
| **A（推荐）** | 从**源头**修：导出 FBX 时 `add_leaf_bones=False` | 一次就是 28 骨，无需后处理，不会漏 |
| B（兜底） | 已经导成 33 骨了，再删掉那 5 根 `_end` | 能救回，但只是补救 —— 中间任何一步（烘焙 / 重定向 / 导 TRF）如果跑在 33 骨上，索引已经错了 |

**不要**把 B 当常规流程用 —— 因为它只在"已经导错"之后才生效，挡不住中间环节用错索引。

### 每次拿到骨架都要过的三个检查点

1. 骨数 == **28**
2. 骨序 == 权威 `_N` 序（`pelvis`=0 … `r_finger0`=27）
3. 父骨关系与官方一致

现件的实测结果：骨数 28 ✅／顺序 28/28 一致 ✅／父骨关系 0/28 不一致 ✅

---

## 4. ModKit 导入报错与解决

### 现象

```
Pre-metadata update failed for item new_animation_clip.
Error: … resources' pos ipo[2.00, 42.00] does not fit with new_animation_clip anim_clip's sources 0.00 and 0.00
Error: … resources' bone(0)[2.00, 42.00] quat ipo does not fit with new_animation_clip anim_clip's sources 0.00 and 0.00
…（bone 0 ~ bone 27 每根一条 + 1 条 pos）
Animation clip: new_animation_clip runtime data is not valid. Animation clip will not be saved!
```

### 根因

**不是 TRF 写错了，是动画片段的时间窗没对齐。**

- TRF 里每根骨的 IPO 时间跨度是 `[2.00, 42.00]`（= 导出帧范围，见 §1 规则 3）
- 而 ModKit 新建的 `new_animation_clip`，`Source 1 / Source 2` 还是默认的 `0.00 / 0.00`
- 两者对不上 → 预元数据更新失败 → clip 不保存

### 解决（全在 ModKit 里，三步）

1. 打开 `new_animation_clip` 的**检视器**，看 `Source 1` / `Source 2` / `Duration` 三个字段
2. 填值：`Source 1 = 2`、`Source 2 = 42`、`Duration` 确保 **> 0**
3. 保存 / 重新导入

> **Source 1 / Source 2 必须等于该次 TRF 的时间跨度，不是固定的 0..N。**
> 换了 FBX 或改了导出帧范围，这两个值要跟着改（例如导出 1–41 就填 1 / 41）。

---

## 5. 已知坑（按踩坑顺序）

1. **★ 增量 vs 绝对** —— **旋转**存绝对局部变换、**平移**存纯增量。教程那份 `trf_skeleton_animation_exporter.py` **两条都写增量**，**不能直接照抄它的写法**。验收看「位置轨首帧 ≈ 0」和 `CHECK_OK`。
2. **★ 叶骨 28↔33** —— 见 §3。导出侧 `add_leaf_bones=False`，别靠导出后再删。
3. **★ 四元数分量顺序** —— TRF 里是 `x y z w`，Blender 的 `rotation_quaternion` 是 `(w, x, y, z)`。消费端读 TRF 时直接按顺序赋会得到一个 180° 错误旋转（我踩过：全 identity 的一帧被写成了绕 Z 轴 180°）。
4. **骨序 = 唯一契约** —— TRF 不存骨骼名，全按索引对号入座。权威索引看官方骨骼名里的 `_N` 后缀。
5. **`bones[0]` 必须是根骨** —— 位移块是脱离旋转块单独写的，写错骨骼 = 位移错位。
6. **"读 rest 位置"不等于"读姿态"** —— 做验证脚本时，`pose_bone.bone.head_local` 是静止位置、不随姿态变化；要读姿态必须用 `pose_bone.head` / `pose_bone.matrix`。我在这上面也踩过一次，差点得出反向结论。
7. **`range(start, end)` 左闭右开** —— 教程原脚本写 `range(1, 12)` 实际只出 11 帧。`pipeline/common/fbx_to_trf.py` 的 `--end` 按**含末帧**处理。
8. **Blender 5.x API 变更** —— `action.fcurves` 在 4.4+ 的 slot 化 Action 后已不可用，需走 `action.layers[].strips[].channelbags[].fcurves`。
9. **骨骼 rest 朝向差异是"结果"不是"原因"** —— 早期我测出「两具骨架 rest 朝向整体差 ~90°」，并据此推论要重定向到官方骨架。那是被增量语义误导：绝对语义下消费端骨架的 rest 根本不参与运算（链式相乘自动抵消）。

---

## 6. 复用

```bash
blender --background --factory-startup --python-exit-code 1 \
  --python "D:/BrainMaker/骑砍2动画重定向/pipeline/common/fbx_to_trf.py" -- \
  --fbx "<输入.fbx>" --out "<输出.trf>" [--skeleton 名] [--start N] [--end N] [--name 动画名] [--root 骨名]
```

- `--name` = TRF 第 3 行的【**动画名**】。缺省 = 输入 FBX 的文件名（去扩展），即本工程的 `<输出名>`。
  🔴 **不是骨架名** —— 骨架对象必须叫 `human_skeleton_notused`（引擎硬要求），那是 **FBX 侧**的事；
  两者同名会让 **ModKit 里所有导入的动画资源撞同一个名字**。
  （2026-09-20 修正：此前缺省写成 `arm.name` 就是这个错；两条 rig 管线现已显式传 `--name <输出名>`）
- 脚本自己会打印骨序逐行、位置轨首帧、以及 `CHECK_OK/CHECK_WARN`
- 导出后记得到 ModKit 里按 §4 填 `Source 1 / Source 2`
