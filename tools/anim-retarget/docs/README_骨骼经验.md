# 骨骼经验：骑砍2 `human_skeleton` 与跨骨架重定向

> 📁 **路径说明**：本文写于旧目录结构；现已重组为 `input/ pipeline/ output/ viewer/`（见顶层 `README.md`）。
> 文档里的 `out/xxx` 对应现在的 `output/fbx|trf/`，脚本对应 `pipeline/`。**内容结论不变**。

> 沉淀自「战国无双2 铁炮兵 → 骑砍2」「UE5 动作 → 骑砍2」两条线的实操
> 所有数字都是**本机实测**，不是推测。配套脚本在同目录与子目录内。

---

## 0. 三套骨架速查表（Blender 5.2 实测）

| 项 | **骑砍2** `human_skeleton` | **UE5 Mannequin** | **战国无双2 铁炮兵** |
|---|---|---|---|
| 骨数 | **28** | 67 | 47（其中**21 根变形骨**） |
| 根骨 | `pelvis`（唯一根） | `pelvis` + `ik_foot_root` + `ik_hand_root` | `bone_0` |
| 叶片骨数 | **5** | 25 | — |
| 骨盆世界高度 | 0.915 m | 0.968 m | 114.3 cm |
| 单位 | 米 | 米（源 FBX 骨架 obj scale=0.01） | **厘米** |
| rest 站姿 | **A-pose** | **T-pose** | **T-pose** |
| Blender 世界朝向 | 面朝 **+Y** | 面朝 **−Y** | 面朝 **−Y** |
| 右侧 | **+X** | −X | **−X** |
| 导出到 glTF 后朝向 | **−Z** | +Z | +Z |

**帧变换**：源面朝 −Y、骑砍面朝 +Y → `F = Rot(Z, 180°)`。
这是**正旋转**，所以 `右→右、左→左`，**不需要 X 镜像**（与手写脚本里常见的镜像写法不同，别照抄）。

---

## 1. 骑砍 `human_skeleton` 骨架结构（28 骨）

```
pelvis                                  ← 唯一根；腿与脊柱的【共同父骨】
├─ l_thigh → l_calf → l_foot → l_toe0            ★ l_toe0 是叶片骨
├─ r_thigh → r_calf → r_foot → r_toe0            ★ r_toe0 是叶片骨
└─ spine → spine1 → spine2
        ├─ neck → head                            ★ head 是叶片骨
        ├─ l_clavicle → l_upperarm_twist → l_upperarm_twist1     ← twist1 是【内联】骨
        │                └→ l_foretwist → l_foretwist1 → l_hand → l_finger0   ★ finger0 叶片骨
        └─ r_clavicle → r_upperarm_twist → r_upperarm_twist1
                         └→ r_foretwist → r_foretwist1 → r_hand → r_finger0  ★ finger0 叶片骨
```

**必须记住的 5 根叶片骨**（没有子骨的骨）：
`head`、`l_toe0`、`r_toe0`、`l_finger0`、`r_finger0`

> 这 5 根就是后面「ModKit 报骨骼数 33 vs 28」的根源 —— Blender FBX 导出器会给每根叶片骨补一根 `_end` 骨。

**两类容易搞混的细分骨**：
- `*_upperarm_twist1` / `*_foretwist1`：**内联**（在链条中间，子骨是下一段），动它会带动下游
- `*_finger0` / `*_toe0`：**末端**（叶片骨），动它只影响自身

---

## 2. ModKit 导入的 5 条骨骼硬规格

来源：`reexport_for_modkit.py`（对照「自定义战斗.md 第 1 步：造动画」+ 官方 animations 文档）

| # | 规格 | 不满足会怎样 |
|---|---|---|
| 1 | **骨数必须与 Skeleton 完全一致 = 28** | 报错 `Animation bone count is 33. Skeleton bone count is 28. They are not compatible` |
| 2 | 根节点名 **`human_skeleton_notused`** | 引擎会去找名为 `human_skeleton` 的骨架，冲突 |
| 3 | **Z-up**（`axis_up='Z'`, `axis_forward='-Y'`） | 与游戏自带 FBX（UpAxis=2）不一致，动画朝向错 |
| 4 | **单位**：导出时 `scene.unit_settings.scale_length = 1.0` | ⚠️ **设成 0.01 会让产物比官方小 100 倍**（2026-09-19 实测修正：0.01 时重新导入骨架高 0.015，1.0 时为 1.492，官方 1.575） |
| 5 | **只导骨架、不导网格**（`object_types={'ARMATURE'}`） | 动画文件里混进 mesh，导入报错/膨胀 |

**导出参数（直接抄）**：
```python
bpy.ops.export_scene.fbx(
    filepath=OUT, use_selection=False,
    object_types={'ARMATURE'},          # ① 只导骨架
    add_leaf_bones=False,               # ② 不补 *_end 叶骨  ← 28/33 的关键
    axis_up='Z', axis_forward='-Y',     # ③ Z-up
    primary_bone_axis='Y', secondary_bone_axis='X',
    apply_unit_scale=True, global_scale=1.0,   # ④ 单位（scale_length 必须 = 1.0，切勿设 0.01）
    bake_anim=True, bake_anim_use_all_bones=True,
    bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_step=1.0,
    bake_anim_simplify_factor=0.0, path_mode='AUTO',
)
```

> **`add_leaf_bones` 默认是 True** —— 这是 99% 的人第一次导入 ModKit 会踩的坑。
> 28 + 5 根叶片骨补出来的 `_end` = **33**，与报错数字完全对上。

---

## 2.5 导出流程（更正版）—— 代码里的实际状态

### 先回答两个常见疑问

**Q：`add_leaf_bones=False` 是不是主要就这一个变量？**
**A：对「骨数 28 → 33」这个问题，是它，且只有它。** 骑砍骨架恰好 5 根叶片骨，Blender 默认给每根补一个 `_end`。

**Q：但"要以官方骨骼数量来导、不要自己乱加"就这一个吗？**
**A：不是，涉及两个变量**：

| 变量 | 作用 | 不加会怎样 |
|---|---|---|
| `add_leaf_bones=False` | 不补 `*_end` 叶骨 | 28 → **33**（骨数不一致，ModKit 直接拒） |
| `object_types={'ARMATURE'}` | **只导骨架**，不把 mesh 等其它对象写进动画文件 | 动画 FBX 里混进网格，导入报错/体积膨胀 |

而要**直接进 ModKit**，还需另外 3 项（根名 / Z-up / cm）—— 一共 5 项，见 §2 表格。

### 哪些脚本已更正（本机实测）

| 脚本 | 状态 |
|---|---|
| `reexport_for_modkit.py` | ✅ 完整规格（原本就是按官方文档写的，5 项齐全） |
| `pipeline/rigs/sw2_gunner/retarget.py` | ✅ **已改为一步直出 ModKit 规格**（5 项齐全） |
| `pipeline/rigs/ue_mannequin/retarget.py` | ✅ 同上 |
| `drive_boneanimcopy.py` / `drive_biosculpt.py` | ✅ 已加 `add_leaf_bones=False` + 骨架-only + Z-up（对照产物，未做根名/cm） |

### 验证（用更正后的 `retarget_sw2_to_bannerlord.py` 重导 p006）

```
[sw2-retarget] 重置场景 fps = 30
[sw2-retarget] 导出(ModKit 规格): output/fbx/sw2_gunner_p006_alig.fbx
→ 重新导入核对:
   骨数: 28                ✅（官方数量，无自己乱加）
   *_end 残留: 无          ✅
   网格数: 0               ✅（只导骨架）
   动画: human_skeleton_notused|Scene   帧 (1, 41)   ✅
```

> ⚠️ 注意：**更正前**的产物（09-18 那批 33 骨 FBX）仍在 `out/` 下；`out/modkit/` 那个 28 骨版是我早期用 `reexport_for_modkit.py` 单独重导的。
> 若要把 40 段全量产出，直接用**更正后的** `retarget_sw2_to_bannerlord.py`（加 `--action <名>`）即可，不必再跑两遍。

---

## 2.6 最后一步：FBX → **TRF**（ModKit 用的骨骼动画容器）

> 工具：`pipeline/common/fbx_to_trf.py`（**全工程唯一版**；2026-09-20 起原 `tools/OpenTrf/fbx_to_trf_fixed.py` 已并入它）
> 完整说明见 `docs/TRF规范.md`。**现已接入重定向脚本，每次导 FBX 时自动再导一份 TRF。**

### TRF 格式与三条硬规则

```
rfver 4
skeleton_anim 1
fly_A_Flight_Idle_A 1                ← 【动画名】（= 该次输出名）。🔴 不是骨架名！
 28                                   ← 骨骼数（必为 28）
 41                                   ← 根骨旋转帧数
 1 -0.437035 -0.524949 -0.486732 0.544537     ← 帧号 + 四元数(x y z w)
 ...
 41 -0.000000 0.000000 0.000000               ← 位移块：只有 bones[0]（根骨）写位移，且是【纯增量】
end
```
> 位移块那次是 **≈ 0**（本 clip 原地不动）；**量级是米（0.86）反而是错的**，见下面第 1 条。

| # | 规则 | 踩错会怎样 |
|---|---|---|
| 1 | **旋转**存**绝对局部变换**（该骨相对父骨的完整变换）；**平移**存**纯增量**（`rest_rot @ location`，**不叠加** `rest.translation`）。两条语义不一样 | 旋转写成增量 → 实机"人趴地上、四肢乱折"（骑砍是 A-pose，每根骨静止朝向都不同）；平移写成绝对 → **人整体抬高约 6cm** |
| 2 | **全骨骼写旋转，只有 `bones[0]` 写位移**（位移块单独写在最后） | 位移错位 |
| 3 | 文件**不存骨骼名**，消费端**按索引**对号入座 | 骨序错 → 从错位那根起全错（33 骨版从第 5 块起就废） |

### 两个验收判据（导出后必看）

```
CHECK_OK:  绝对公式与 Blender pose 矩阵一致（最大偏差 0.0396°，属数值噪音）   ← 应 < 0.5°
CHECK_POS: 位置轨首帧 = (0.0000, 0.0000, 0.0000)                            ← 应 ≈ 0（纯增量）
```

若 `CHECK_POS` 首帧量级是**米**（≈0.86）→ 误用了绝对语义，导出作废。

### ModKit 里还要配一次时间窗（TRF 没问题也会报错）

报错 `… pos ipo[2.00, 42.00] does not fit … sources 0.00 and 0.00`：
**不是 TRF 写错**，是新建的 `new_animation_clip` 的 `Source 1 / Source 2` 还是默认 0。
→ 打开检视器，把 `Source 1 / Source 2` 填成该 TRF 的**帧范围**（如导出 1–41 就填 1 / 41），`Duration > 0`。

### 在流程里的位置

```
① 骨架准备（28 骨 / 权威骨序）  ② 重定向(align) + 逐帧烘焙
③ 导出 FBX（ModKit 规格）      ④ 导 TRF（脚本自动）        ⑤ ModKit 导入 + 填 Source1/2
```

---

## 3. 跨骨架重定向的 4 个骨骼坑（都带实测数字）

### 3.1 骨轴约定不同 → **绝对不能用 `absolute`（绝对朝向）模式**

两套骨架"骨骼自身轴向"的定义不同（源骨 Y 轴常呈水平、目标骨另一套）。
把源的绝对世界朝向直接写给目标 → **骨盆被多转 90°、整个人倒挂**：

| 模式 | SW2 铁炮兵实测（肢段方向误差） |
|---|---|
| `absolute` 绝对朝向 | **155.81°**（倒挂） |
| `delta` 世界增量 | 20.29° |
| **`align` 增量 + 逐骨静止对齐** | **10.93°** ✅ ← **唯一交付模式**（`--pose` 默认值） |

### 3.2 rest 站姿不同（T-pose vs A-pose）→ 必须做**逐骨静止对齐** `A⁻¹`（这就是 `align` 模式）

> **结论：交付一律用 `align`（脚本默认值）。** `delta` / `absolute` 仅作对照，产物已归档到
> `战国无双2铁炮兵_p006_重定向/out/archive/`，不再用于交付。

纯 `delta` 只搬"转了多少"，会把两套骨架的**静止站姿差原样保留**（实测误差恰好等于静止差常数：手臂 25~33°、大腿 57°）。

补上对齐项即可（这也是原 UE5 项目"失败"的真正原因，实测 **37.53° → 3.10°**）：

```
D_src(i)  = S_pose(i) · S_rest(i)⁻¹                  源骨世界旋转增量
R(i)      = F · D_src(i) · F⁻¹                       换到目标帧（F=Rot(Z,180°)）
A(i)      = 「源静止肢段方向(F旋转后) → 目标静止肢段方向」的最小旋转
T_pose(i) = R(i) · A(i)⁻¹ · T_rest(i)                ← 少了 A⁻¹ 就会"弯腰驼背、手臂偏"
```
**多分支骨**（骑砍 `pelvis` / `spine2` 同时挂两条分支）：只认「子树包含 `head` 的那条躯干延续子骨」，
否则会把一条腿的对齐强行加给另一条腿（实测右腿一度偏到 159°）。

### 3.3 扭骨：**源侧必须映射，目标侧不要映射**（★ 本次实验结论）

UE5 Mannequin 的扭骨是**旁支叶骨**（`lowerarm_twist_01_l` 没有子骨，不在 `hand` 链路上）；
骑砍的 `l_foretwist1` 是**内联骨**（`hand` 是它的子级）。两者拓扑不同，处理方式相反：

| 场景 | 做法 | 实测 |
|---|---|---|
| **源侧显示**（UE→UE 自映射） | **必须映射** 22 标准骨 + 8 扭骨 | 不映射 **15.98°** → 映射后 **0.29°** |
| **目标侧**（UE→骑砍） | **不要映射**扭骨到 `*_twist1` | 映射 **6.93°** → 不映射 **3.30°** |

原因：把旁支骨的旋转搬到内联骨上，等于给手部链路**多注入一次旋转**。
（`ue_batch_glb.py` 的 `--twist` 默认为关，就是这个结论。）

### 3.4 根骨位移：**"髋空间增量" + 单位换算**

- SW2 的 glTF 把根骨平移写成**相对首帧的髋空间增量**，静止位移 `114.3` 被丢掉 →
  直接播会整体下沉 114cm。要取"**相对静止姿态**"的增量再补回。
- 飞行/闪避类动作**不能用"首帧"当基准**（首帧本身就是低姿态）→ 目标会站在自己的静止高度上，比源高一截
  （实测两人高度差 **198px → 48px**）。
- 源与目标单位不同时（cm vs m），位移增量要按**两者静止高度比**换算，否则差 100 倍。

---

## 4. Blender 骨骼操作坑（都踩过）

| # | 坑 | 表现 | 修法 |
|---|---|---|---|
| 1 | `add_leaf_bones` 默认 True | 28 → 33 骨，ModKit 直接拒绝 | 导出时 `add_leaf_bones=False` |
| 2 | **导入 FBX 会改 `scene.render.fps`** | 骑砍 FBX 带 24fps、UE 带 25/30fps → 导出时长膨胀 3.4%~**25%**（"慢半拍"） | 导入后立刻 `scene.render.fps = 你想要的` |
| 3 | 改 `obj.parent` 后不刷新 | `matrix_world` 还是旧的（带父级 0.01 缩放）→ 基准高度算错 | 改完必须 `bpy.context.view_layer.update()` |
| 4 | 蒙皮网格的 bbox | `geometry.boundingBox × matrixWorld` **对 SkinnedMesh 不成立**（没算 bind 矩阵），UE 小白人被量成 182.57（实际 1.83m）→ 缩放算成 0.0096，人缩成一小块 | 用**骨骼静止位置**量身高 |
| 5 | 静态 FBX 外面套 `scale=0.01` 空物体 | 导出的 GLB 里"骨骼静止在 cm(96.75)、动画值在 m(1.168)"，单位不一致 → 人塌到地面 | 导出前把骨架从那个空物体上摘下来 |
| 6 | 子串过滤会多带 | 按 `Anim_JU_in_PL` 过滤会同时带出 `_land`/`_fall_loop` | 多带可接受、少带不可接受；导完核对数量 |

---

## 5. 骨骼体检（进 ModKit 前先跑这个）

**判据清单**：骨数=28？根名=`human_skeleton_notused`？无 `*_end`？无 mesh？Z-up？cm？

一行命令快速体检（把路径换成你的 FBX）：
```bash
cat > /tmp/sk.py <<'EOF'
import bpy, sys
p = sys.argv[sys.argv.index("--")+1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=p)
arm = next(o for o in bpy.data.objects if o.type=='ARMATURE')
B = arm.data.bones
print("骨数:", len(B), "| 根名:", [b.name for b in B if b.parent is None])
print("叶骨:", [b.name for b in B if not b.children])
print("*_end 残留:", [b.name for b in B if b.name.endswith('_end')])
print("网格数:", len([o for o in bpy.data.objects if o.type=='MESH']))
print("动画:", arm.animation_data.action.name if arm.animation_data and arm.animation_data.action else "无")
EOF
blender -b --python /tmp/sk.py -- "你的.fbx"
```

现成脚本（本项目内）：
- `pipeline/rigs/sw2_gunner/reexport_for_modkit.py` —— 把已重定向的 FBX 按 ModKit 规格重导
- `pipeline/common/check_quality.py` / `srcglb_check.py` —— 重定向质量校验（穷举 8 种朝向变换取最优）
- `pipeline/common/fbx_to_glb.py` —— 批量重定向（`--pelvis ground|src|none`、`--twist`）

---

## 6. 报错 → 原因 → 修法 速查

| 报错 / 现象 | 原因 | 修法 |
|---|---|---|
| `bone count is 33. Skeleton bone count is 28` | Blender 补了 5 根 `*_end` 叶骨 | 导出加 `add_leaf_bones=False` |
| 动作倒挂 / 骨盆转 90° | 用了 `absolute` 绝对朝向模式 | 改用 `align`（含 `A⁻¹` 静止对齐） |
| 手臂抬不起来、弯腰驼背 | 缺静止对齐项（纯 delta） | 补 `A⁻¹` |
| 手/小臂朝向偏 30~44° | 源侧漏映射扭骨（源显示不忠实） | 源侧 22 标准骨 + 8 扭骨全映射 |
| 目标侧手部多转一次 | 把源**旁支**扭骨映射到目标**内联**扭骨 | 目标侧**不映射**扭骨 |
| 播放"慢半拍"、跟不上源 | 导入骑砍 FBX 把场景 fps 改成 24，导出时长 ×1.25 | 导入后把 `scene.render.fps` 设回；按**时间**重采样 |
| 整体下沉 ~114cm | glTF 根骨位移是"相对首帧的髋空间增量" | 以**静止姿态**为基准取增量 |
| 人缩成地面一小块 | SkinnedMesh 的 bbox 量法错 / 骨架被套 0.01 空物体 | 用骨骼静止位置量身高；导出前摘掉该父级 |
| 动画文件导入报错/膨胀 | 把 mesh 一起导出了 | `object_types={'ARMATURE'}` |
| 实机"人趴地上、四肢乱折" | TRF 的**旋转**用了增量语义当绝对用 | 用 `pipeline/common/fbx_to_trf.py`，看 `CHECK_POS` 首帧 ≈0 与 `CHECK_OK` |
| 实机"人被整体抬高约 6cm" | TRF 的**平移**用了绝对语义（多叠 `rest.translation`） | 同上；判据 = 位置轨首帧应 ≈0 而非 ≈0.86 |
| `… pos ipo[2,42] does not fit … sources 0.00 and 0.00` | ModKit 里 `Source 1/2` 没填 | 填成 TRF 的帧范围（如 2/42） |
| **FBX 比官方小 100 倍**（骨架高 0.015 vs 官方 1.575） | 导出时 `scene.unit_settings.scale_length = 0.01` | 改为 **1.0**（README 硬约束 4b） |
| **两侧位移差 100 倍 → "一前一后"** | `--pelvis src` 把**世界坐标**直接赋给 **Armature 空间**的 `pb.matrix`；两侧 base 的 `matrix_world` 缩放不同（0.01 vs 1.0） | 世界空间算完再 `base.matrix_world.inverted() @ ...`（硬约束 11） |
| **竖直位移整个丢失（人像被压平）** | 护栏 `abs(off.z) > 1.5` 是「米」语义，却用在**换算放大 100 倍后**的值上 → 71/71 帧全被清零 | 护栏改用**换算前**的物理量判断（硬约束 12） |
| **查看器里两人走向相反（一个冲进画面一个冲出）** | 用 `group.rotation.y` 对齐朝向时，把**根位移也翻了 180°** | 对未旋转侧做根位移水平反向（硬约束 13，`viewer.html` 已自动处理） |
| TRF 报「场景里有多个骨架 [...]」 | UE 源 FBX 带额外 `root` 骨架 | 导出前清理，只留目标骨架；或 TRF 传 `--skeleton` |
| 导出日志刷屏、耗时暴涨（帧数 × 次导出） | 导出段被误包在**逐帧循环**里 | 取消缩进，移出循环 |
| TRF 时长差 4 倍（385 帧本应 97 帧） | 源 **120fps** 未重采样到骑砍基准 30fps | 按时间重采样（`TGT_FPS=30`，`SRC_STEP = SRC_FPS/TGT_FPS`） |
| 数值自检都过了，ModKit 里幅度仍错 | 只看 `CHECK_OK`，没核对**尺寸量级** | 跑 `verify_modkit_fbx.py`，并与官方 `human_lod_4.fbx` 比 |
