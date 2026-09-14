# armor-pipeline — 外部角色模型上的甲 → 骑砍可用的 tpac 资产

> 状态：**2026-09-14 首件跑通**（真田幸村 主体甲，战国无双2 解包件）。
> 目标：把「任意来源的角色模型上穿着的甲」重定向骨架后绑到骑砍 `human_skeleton`，做成 Item。
> 上游工程文档：[Knowledge/骑砍2盔甲资产工程.md](../../Knowledge/骑砍2盔甲资产工程.md)（结构规格 / 参考物裁定）。

---

## 1. 一句话

**输入**一份带绑定的角色 FBX → **输出**一份 6 级 LOD、已绑 `human_skeleton` 的甲 FBX，可直接进 ModKit 编辑器 Publish。

---

## 2. 怎么跑

```bash
SKEL="<游戏根>/modding_resources/skeletons/human_skeleton.fbx"
BLENDER="C:/Program Files/Blender Foundation/Blender 5.2/blender.exe"

"$BLENDER" -b --python tools/armor-pipeline/scripts/build_armor.py -- \
    --src  "<源角色.fbx>" \
    --skel "$SKEL" \
    --out  "tools/armor-pipeline/out" \
    --name taikou_yukimura_do_a \
    --parts body_kimono --kimono-torso-only --r 0.0120
```

跑完 `out/` 下得到 `<name>.fbx`（含 6 级 LOD + `human_skeleton`）、`<name>.blend`。
**体检**（进编辑器之前必跑，验导出规格）：

```bash
python tools/face-pipeline/scripts/fbx_probe.py "tools/armor-pipeline/out/<name>.fbx"
```
期望：`UpAxis=2 FrontAxis=1 FrontAxisSign=-1 CoordAxis=0 CoordAxisSign=1 UnitScaleFactor=100`，
且每个 Geometry 的 Model 节点**零缩放零旋转**。（骨骼自身带旋转是正常的。）

**看合不合身**（把甲套到原版身体上渲染三视图）：

```bash
"$BLENDER" -b --python tools/armor-pipeline/scripts/check_fit.py -- \
    "tools/armor-pipeline/out/<name>.fbx" \
    "Debug/offline/core_game/fbx/body/body/body_male_a.fbx" \
    "tools/armor-pipeline/out" v1
```
（身体 FBX 由 `tpaccli dump --format fbx --filter body_male_a` 产出，见 §5）

### 参数

| 参数 | 默认 | 说明 |
|---|---|---|
| `--parts` | `body` | 取哪几件。`body`=胴+草摺；`body_kimono`=再加内衬着物；`head`/`arms` 预留 |
| `--r` | `0.01` | **径向缩放 = 甲与身体之间的余量旋钮**。大了甲发飘，小了身体顶穿甲。实测原版身体上 **0.0120** 合适 |
| `--kimono-torso-only` | 关 | 按主骨过滤掉着物里的头和手（否则出现"头顶飘块 + 袖子变形"） |
| `--cut-z` | `0` | 只保留高于该高度（米）的面；用于日后拆腿甲 |
| `--lod` | `0.834,0.563,0.249,0.140,0.072` | LOD1~5 的减面比（抄原版 `aserai_cavalry_armor` 的顶点递减） |
| `--debug` | 关 | 打印逐骨旋转角与平移偏移 |

---

## 3. 原理（改代码前先读）

每个顶点按它在源骨 `i` 的局部坐标，搬到对应骑砍骨 `k(i)` 的局部坐标：

```
v' = Σᵢ wᵢ · ( T_bl[k(i)] · (MIRROR · v) )
T_bl[b] = Translate(骑砍骨头) · Rot(仅手臂) · Scale(R) · Translate(-(MIRROR · 源骨头))
```

- `wᵢ` = **顶点原有的蒙皮权重，原样保留，只换骨名**（美术做的权重比"最近点抄"准）
- 权重不重算 ⇒ 草摺被左右腿对称驱动，走路不撕扯

### 四条关键裁定（每条都是实测换来的）

| # | 裁定 | 为什么 |
|---|---|---|
| 1 | **源模型正面与骑砍相反时，用 Y 轴镜像，不用 180° 绕 Z 旋转** | 旋转会连左右一起翻。镜像是反射（行列式 −1），**做完必须反转面绕序**否则法线朝里 |
| 2 | **只给手臂链加旋转，其余骨一律不转** | 两侧都有"朝向信息是垃圾"的骨：源侧骨盆骨的最长子骨是腿根（头→子骨得到"朝下"，与朝上的 `spine_9` 差 147.7°）；骑砍 `bone.matrix_local` 的骨轴不沿肢体（`spine_9` 头 z=1.0064、尾 y=+0.157，指向正前方）。躯干/腿两边都竖直，不转才对 |
| 3 | **骨的锚点用 head 位置，缩放用统一的 R，不做逐骨 λ 拉伸** | 逐骨拉伸会在关节处产生剪切。比例差改用 R 调余量吸收 |
| 4 | **`R` 是唯一的手调旋钮** | 判定方法：逐 z 带量「甲的半径 vs 身体的半径」，甲必须处处更大 |

---

## 4. 踩过的坑（真金白银）

| 症状 | 根因 | 修法 |
|---|---|---|
| 甲被转到几十米外、bbox 爆到 −30 | 源侧有长度 0.01 的"空节点"骨（如脚趾），逐骨 λ 算出来 900+ | 短骨不拉伸 + λ 钳制；后改为完全不用 λ |
| 躯干甲塌成一张平片 | 拿 `bone.matrix_local` 的旋转做重定向，而骑砍骨轴不沿肢体 | 旋转只从**关节位置**推 |
| 甲整体切变、扭麻花 | 给**每根**骨都算了朝向，垃圾朝向互相打架 | 只给手臂链转（见 §3 裁定 2） |
| 甲穿反（面朝背） | 源模型正面 −Y、骑砍正面 **+Y** | Y 轴镜像 + 反转面绕序 |
| 导出把源模型 16 个网格全打包进 FBX | `use_selection=False` 会导出场景里所有网格 | 导出前清场，只留甲 + LOD + 骨架 |
| 着物带进来"头顶飘块 + 袖子变形" | 该网格把头和手的皮也包在里面 | `--kimono-torso-only` 按主骨过滤 |
| 武器件混进选件 | `submesh_1`（着物）与 `submesh_1.001`（矛柄）解析出同一个号 | 按材质 `mat_w_*` + 有无 UV 排掉 |
| 到处露白（身体顶穿甲） | 甲的半径小于身体 | 调大 `--r`（实测 0.010 → 0.0120） |
| 骨架导入后对象带 0.9975 缩放 | Blender 把 `global_scale` 折成"对象缩放 × 单位换算" | 导入后对骨架 `transform_apply` 烘平 |
| 量到的"洞"其实是模型本来顶点稀疏 | 源模型在踝上方 8cm 内 0 顶点（低模） | 先量源模型的 z 直方图，别急着修 |

---

## 5. 依赖与上游

| 项 | 位置 |
|---|---|
| 官方骨架 | `<游戏根>/modding_resources/skeletons/human_skeleton.fbx`（28 骨，名字自带索引） |
| 原版身体（含权重） | `tpaccli dump --packdir Debug/offline/core_game --filter body_male_a --format fbx --out Debug/offline/core_game/fbx/body` |
| tpaccli | `tools/face-pipeline/tpactool/TpacToolCLI/bin/Release/net9.0/tpaccli.exe` |
| Blender | `C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`（自带 numpy，KD-tree 用 `mathutils.kdtree`） |
| 源件（本例） | `D:\BrainMaker\战国无双2资产解包分析\export\fbx\L00_yukimura.fbx` |

**本工具链不做的事**（脸部专用，别搬过来）：`morphfix` / `skinfix --fullmat` / 变形通道。

---

## 6. 🔴 编辑器那一段（人做，Claude 做不了）

1. 跑模块的 `to_editor_mode.bat`（若该模块是编辑器工程 + 运行期资产包分离的形态）
2. 删旧网格资产 → **导入** `out/<name>.fbx`
   设置：`Convert to unit = m` ／ 不勾 `Convert to Z-up` ／ 只勾 `Import meshes`
3. **逐个材质勾 `Vertex Layout → Skinning`**
   🔴 **这一步最容易漏**：它是**材质**属性，FBX 不携带，重导必重勾。
   漏了的症状极具迷惑性：**甲的位置大小全对，就是不跟骨架动**。
4. 贴图挂到材质上（漫反射用 `out/<name>_d.png`；法线/高光暂缺，引擎会用默认）
5. `Publish` —— **目标目录别选进模块里**（否则得到 `Modules/X/X/` 嵌套）
6. 把产出的 `pack0.tpac` 放进模块的 `AssetPackages/` → 跑 `to_game_mode.bat`
   （🔴 `Assets/` 必须改名 `Assets_disabled`，否则引擎读编辑器半成品）

**物品已注册**（本轮已写）：`Modules/Taikou/ModuleData/taikou_items/body_armors.xml` 里的
`<Item id="taikou_yukimura_do_a" mesh="taikou_yukimura_do_a" …>`。
`mesh=` 必须等于编辑器产出的资源名（= FBX 里的 Model 节点名 = 本工具的 `--name`）。
本地化：`{=TAIKOU_yukimura_do_a}` 键已进根级 + CNs 两份 `std_Taikou_strings.xml`。

---

## 7. 下一步

| # | 事项 | 状态 |
|---|---|---|
| 1 | 主体甲（胴+草摺+佩楯+袴+脛当+靴）重定向 | ✅ |
| 2 | Item XML + 本地化 | ✅ |
| 3 | **编辑器 Publish → 进游戏看** | ⏳ 人做 |
| 4 | 贴图：法线 / 高光通道（`_n` / `_s`），纹理按甲的 UV 裁切（现在整张图集带进去，浪费） | ⏳ |
| 5 | 拆件：脛当+靴 → `leg_armor`；両腕 → `hand_armor`（用 `--parts arms` + `--cut-z`） | ⏳ |
| 6 | 兜 → `head_armor`（注意工程文档 §3.5：头盔会随脸形参数自动缩放，未实测） | ⏳ |
| 7 | 靴子形状：源模型低模，脚部是块状，可考虑重建 | ⏳ |
