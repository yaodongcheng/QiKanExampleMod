# 骨骼动画 TRF（skeleton_anim）格式与「增量当绝对」陷阱

> 日期：2026-09-19 · 场景：把重定向好的骑砍骨骼动画导进 ModKit，模型预览里人物趴在地上、四肢乱折
> 结论状态：**方法已确认**（2026-09-19 用户确认）。修法见 §四，脚本 = [tools/OpenTrf/fbx_to_trf_fixed.py](../tools/OpenTrf/fbx_to_trf_fixed.py)

---

## 〇、一句话结论

**TRF 的骨骼旋转/平移必须写「绝对局部变换」，不能写 Blender 的 `pose_bone.rotation_quaternion` / `location`（那两个是相对静止姿势的增量）。**

| | 写增量（错） | 写绝对（对） |
|---|---|---|
| 旋转 | `pose_bone.rotation_quaternion` | `rest_local.to_quaternion() @ pose_bone.rotation_quaternion` |
| 平移 | `pose_bone.location` | `rest_local.translation + rest_local.to_3x3() @ pose_bone.location` |
| 实机表现 | 人趴在地上、四肢乱折 | 正常 |

---

## 一、症状与根因

**症状**：自制/重定向的动画导进 ModKit，模型预览里人物趴在地上、腿折到背后，但**不报任何错**。

**根因**：导出脚本把 Blender 的 pose 通道值直接写进了 trf。
`pose_bone.rotation_quaternion` 与 `pose_bone.location` 在 Blender 里的语义都是「**相对静止姿势的增量**」——
没动过的骨头 = `(0,0,0,1)` 和 `(0,0,0)`。引擎按「绝对局部变换」解读这份数据，
等于把每根骨强行摆到「零旋转」，于是全部错位。

**为什么这条特别隐蔽**：文件格式完全合法、导入零报错、Kit 的 clip 也能保存，**只有模型预览是错的**。

---

## 二、TRF skeleton_anim 格式（实测）

```
rfver 4
skeleton_anim 1
<动画名> 1
 <bone_count>
 <rot_frame_count>                 ← 每根骨一组，按骨索引顺序
 <帧号> <qx> <qy> <qz> <qw>        ← 四元数按 x y z w，6 位小数
 ...（每根骨各一组，组内按帧号递增）
 <pos_frame_count>                 ← 位置轨只有一条，写在根骨上
 <帧号> <x> <y> <z>
end
```

要点：

- **骨索引顺序 = 引擎骨序**（验证方法见 §五）
- **位置轨只有一条**，写在根骨（`pelvis`）上
- 行首缩进、四元数分量顺序（x y z w）、`end` 不带换行 —— 都是格式的一部分，照抄别改
- `rfver 4` 是版本号；本次实测的是这一版

---

## 三、判据：一条 trf 坏没坏，看三个数

| 检查 | 坏文件 | 好文件 |
|---|---|---|
| **首帧逐骨角度** | **28 根骨整齐地全是 0.00°**（= 单位四元数） | 每根骨是**它自己的静止朝向**，各不相同 |
| **没动过的骨** | 全程恒定 0.00° | 恒定在**它的静止值**（实测例：79.6° / 163.0° / 165.5°，不是 0） |
| **位置轨首帧** | ≈ `0.00`（人沉到地上） | 真实骨盆高度（本例 **0.86 m**） |

🔴 **最快的一条**：看位置轨首帧。`0.00` = 增量写法，直接判死。

为什么这三条能定性：绝对值的文件里，每根骨的首帧**必然**是它自己的静止朝向，
28 根不可能整齐划一；而增量的文件里，没动的骨**必然**是精确的 0。

---

## 四、修法

```python
def rest_local(pose_bone):
    """该骨相对父骨的静止矩阵。Blender 的 bone.matrix_local 是「骨架空间」的，
    所以要拿父骨的静止阵求逆再乘自己；根骨直接用自身。"""
    if pose_bone.parent:
        return pose_bone.parent.bone.matrix_local.inverted() @ pose_bone.bone.matrix_local
    return pose_bone.bone.matrix_local.copy()

# 旋转轨道
rest = rest_local(pb)
q = rest.to_quaternion() @ pb.rotation_quaternion            # 绝对局部旋转

# 位置轨道（只写根骨）
rest = rest_local(root_pb)
loc = rest.translation + rest.to_3x3() @ root_pb.location    # 绝对局部平移
```

**脚本**：[tools/OpenTrf/fbx_to_trf_fixed.py](../tools/OpenTrf/fbx_to_trf_fixed.py)（用法与旧的 `fbx_to_trf.py` 一致，可直接换用）。

**脚本自带三条自检**（每次运行都打印）：

1. 骨序逐行（确认索引与引擎一致）
2. 位置轨首帧数值（验收判据：应是真实骨盆高度，不是 0）
3. **绝对公式 vs Blender 自带 `pose_bone.matrix` 的交叉核对** —— 两条独立途径算同一个量，偏差 > 0.5° 报警。实测噪音约 0.05°（四元数归一化误差）。

> 自检的设计原则：**拿 Blender 自己算好的 pose 矩阵当对照**，而不是自己验自己。
> 只用一种算法算完自己看一遍，是「不会失败的验证」（社区文档对这个有专章，见 §六）。

### 改动清单（这次 A/B 的前提）

| 改了什么 | 没改什么（全部冻结） |
|---|---|
| 旋转：`pose_bone.rotation_quaternion` → `rest ∘ delta` | 帧范围（2–42） |
| 平移：`pose_bone.location` → `rest.translation + rest_rot @ delta` | 骨数（28） |
| — | 骨序（`pose.bones` 顺序） |
| — | 动画名（`human_skeleton_notused\|Scene`） |
| — | 文件格式、缩进、四元数分量序、小数位、`end` 不带换行 |

**只动「数值怎么算」，其余一个字符都不动** —— 所以新旧两份文件逐字节同构，导入 Kit 后姿势的差别**只能**来自那两行公式，不存在"顺便改了别的"这种解释空间。

🔴 **这是改数据管线的通用纪律**：要证明"就是这一处导致的"，就得把其余变量全部冻成常量。
反例警示：同时改两处再对照，结果变好了也说不清是哪处起的作用；变差了更不知道该回退哪一处。

---

## 五、顺带验掉的：骨序 = 引擎骨序

**验证方法（可复用）**：拿**左右对称骨**当探针 —— 它们在正确骨序下必须落在成对的索引上、给出近似对称的静止值。

实测（28 骨，索引从 0）：

| 索引对 | 骨 | 静止角 |
|---|---|---|
| 4 / 8 | `l_toe0` / `r_toe0` | 79.6° / 76.9° |
| 16 / 23 | `l_upperarm` / `r_upperarm` | 6.0° / **6.0°** |
| 20 / 27 | `l_finger0` / `r_finger0` | 163.0° / 165.5° |

三对全部对称 → 骨序正确。

**28 骨序速查**（骨名后缀的数字就是索引）：

```
0 骨盆 pelvis
1-4   左腿  l_thigh / l_calf / l_foot / l_toe0
5-8   右腿  r_thigh / r_calf / r_foot / r_toe0
9-11  脊柱  spine / spine1 / spine2
12    脖    neck
13    头    head
14-20 左臂  l_clavicle / l_upperarm_twist / l_upperarm / l_foretwist / l_forearm / l_hand / l_finger0
21-27 右臂  （同上，r_ 侧）
```

---

## 六、为什么教程里能用、骑砍原生骨架不能用

TRF 这套写法出自一个教程（`ModdingKit + OpenTrf 导入导出 SkeletonAnimation`），
而教程演示的是**自制骨架**。

**自制骨架的骨头静止朝向常常接近单位四元数**（骨头基本沿父骨方向），
此时「增量 ≈ 绝对」，两种写法**碰巧等价** —— 教程因此是对的，但**方法是错的**。
换成骑砍原生 `human_skeleton`（A-pose，每根骨静止朝向都不同），错法立刻暴露。

这同时解释了社区文档里那句一直没被解释的话 ——
[Knowledge/bannerlordmodding_lt/guides/custom_creature_animation.md](bannerlordmodding_lt/guides/custom_creature_animation.md)
「An honest status note」：

> 在**自制骨架**（蜘蛛/大象/狼）上创作的 clip 一切正常；
> 这个警告**专指在你没参与搭建的原生骨架上写新动画**，残余的过度旋转未被完全解释。

**根因就是本页这条**：不是引擎挑骨架，是自制骨架的静止朝向碰巧接近单位四元数。

---

## 七、工艺纪律

1. 🔴 **trf 是生成物 —— 改脚本重跑，禁止手改 trf**（铁律 22）。手改 = 与生成器分叉，下次重跑即丢。
2. **导出脚本的输出去哪，跟着工作副本走。** 本工具链的工作副本在 `D:\BrainMaker\OpenTrf\`（含教程视频与各版 trf），仓库 [tools/OpenTrf/](../tools/OpenTrf/) 是入库副本。改哪份要说一声，别让两份分叉。
3. **A/B 用新文件名，不覆盖旧产物**（本例：`sw2_gunner_p006_alig_abs.trf` 对 `sw2_gunner_p006_alig.trf`）。Kit 里会生成一条新资源，能和旧的并排对照；覆盖了就没得比了。
4. **一次只改一处，其余冻结**（清单见 §四）。A/B 成立的前提是"只有目标那一处不同" —— 所以改管线时先把不打算动的东西明确列出来，再动手。
5. **分发进 `AssetSources` 要在 ModKit 开着的时候做**（铁律 31：它是文件监视，开之前的改动不会被补拉）。

---

## 八、还没验 / 留档

- **平移轨的参考原点**：本页按「相对骨架根」写（根骨 = 骨架空间的静止头位 + 旋转后的增量）。本例按此写法导出后姿势正常（2026-09-19 用户确认），但引擎是否可能要求别的原点，未做对照实验。
- **`UnitScaleFactor`**：本机 Blender 导出为 `1.0`，官方骨架 FBX 是 `100`
  （见 [蒂法换头工程.md](蒂法换头工程.md) §11.3）。对旋转无影响，尺度上是否有影响未验。
- 本页的 `human_skeleton` 相关数字（28 骨、骨盆高度）来自本机 v1.2.12 资产。

---

## 九、从"能播"到"能进游戏流程"：clip 元数据 + 自定义 usage（2026-09-19 实机打通）

> 背景：动画能播（`custom.do_anim` 能播 ✓）**不等于**游戏流程会用它。
> 引擎判断"这是不是一个正经的装填动作"，靠的是 **clip 的元数据**；我们踩了整一轮才定位到这里。

### 9.1 clip 元数据：裸导是不够的

**症状**：铁炮掏出武器后点不出装填（能拔枪、`do_anim` 也能播那条动画，但装填起不来）。

**根因**：裸导进去的 clip 只填了 `Source 1/2`，**没有装填该有的元数据** ⇒ 引擎不把它当装填动作。

**对照原版同类的字段（实测值）**：

| 字段 | 原版 `reload_crossbow` | 缺了的表现 |
|---|---|---|
| 🔴 **`Continue to action`** | `act_reload_crossbow_continue` | **装填是两段式**（主段 + 收尾段）：主段播完必须链到收尾段，**链断了就永远走不到"装填完成"** ⇒ 弹药进不了膛 |
| 🔴 **`Param 2`** | `0.570` | 怀疑是"弹药进膛时点"（占全长比例）—— 0 = 永不进膛 |
| 🔴 **`Priority`** | `11.000` | 动作优先级，0 可能起不来 |
| `Blend in / out period` | `0.300` / `0.100` | 融合手感 |
| `Loading Type` | `Always keep in memory` | — |
| `Step points` | `X=0, Y/Z/W=-1` | 脚步音时点 |
| `Sound code` | `/missile/foley/crossbowload` | 音效（不阻塞）|
| `Flags`（40 多个复选框） | **全部未勾** | 实测两边一致 ⇒ **不是差异点** |
| `Clip usages`（折叠栏）| 编辑器里**点不开** | 两边一样，未知 |

🔴 **做法**：在 ModKit 里把**原版同类 clip 的元数据整组抄过来**，只换动画数据（Source 区间 / 骨架动画）。
「裸导一条 clip + 只填 Source 1/2」= 交付了一个**没有说明书**的零件。

### 9.2 二分排查法（引擎不打印流程，日志没用）

装填起不来时，日志里**什么都不会有**（引擎不 log 这条流程）。唯一有效的办法是**一行二分**：

> 把 usage 的 `reload_action` **临时指回原版动作** → 能装 ⇒ 病灶在我们的动作/clip；不能 ⇒ 在别处。
> **一行改动 + 一次重启就能定性**，比翻日志快得多。

### 9.3 自定义 item_usage 的写法

- 🔴 **不要用 `base_set="crossbow"` 继承**（2026-09-19 实测**失败**：能拔枪但装填起不来 —— 未声明的段**不继承**）。
- ✅ **逐字抄原版那一整块**（例：`crossbow` 211 行）+ **只改两处**：`id` 和 `reload_action`。
  - 抄完做一次对账：**把两处改回原样应与原版逐字节相同**。
- **骑马那条 `usage` 保持原版动作**（自制动画多半是站姿，马上播会怪）。
- 物品侧 `item_usage` 指过来即可；**回退 = 改回原版 usage 名（一行）**。

### 9.4 附带发现（同轮，非动画域）

- **物品缺 `reload_phase_count="2"`**（原版 9 把弩全有）—— 补了**仍然不能装填**，所以它**不是**装填的元凶（实验排除）。含义：它是原版必备字段，但不是这一环的原因，排查时别往这条上想。
- **跑生成器会冲掉手改**：`gen_weapon_items.py` 的表项里还写着旧的 `ammo_class="Bolt"`，一跑就把文件的 `Cartridge` 冲掉（火器身份丢失）。**纪律**：改数据先改生成器（铁律 22）；跑生成器前**先备份、跑完逐属性对账**（这次靠对账才发现）。
- **物品要能在"日常装"里穿，必须有 `Civilian="true"`**（没有的拖不进 civilian 装备栏）。批量补完**必须同步给生成器打补丁**，并用生成器自带的 `--check` 验幂等。
