# armor-pipeline — 外部角色模型上的甲 → 骑砍可用的 tpac 资产

> 状态：**2026-09-14 首件全链路实机验证通过**（真田幸村当世具足，战国无双2 解包件）。
> 已装机：`Taikou/AssetPackages/pack0.tpac`，物品 id `taikou_yukimura_do_a`（两套装备栏都能穿）。
> 目标：把「任意来源的角色模型上穿着的甲」重定向骨架后绑到骑砍 `human_skeleton`，做成 Item。
> 上游工程文档：[Knowledge/骑砍2盔甲资产工程.md](../../Knowledge/骑砍2盔甲资产工程.md)（结构规格 / 参考物裁定）。

---

## 1. 一句话

**输入**一份带绑定的角色 FBX → **输出**一份 6 级 LOD、已绑 `human_skeleton` 的甲 FBX，可直接进 ModKit 编辑器 Publish。

---

## 2. 怎么跑

### 推荐：一条命令跑完全流程（`build_armor_chain.py`）

```bash
python tools/armor-pipeline/build_armor_chain.py     --src     "<源角色.fbx>"     --diffuse "<源件原始漫反射图集.png>"     --name    taikou_yukimura_do_a
```

它按**固定顺序**跑：网格 → 贴图 → 关卡 1 体检 → 打印产出与后续编辑器步骤。
零件与缩放已按首件调好（`body_kimono_arms` / `--r 0.0120` / `--r-arms 0.0160`），**换角色只改三个参数**。

🔴 **为什么要有它**：那两步是**有序且不可反**的（贴图依赖网格产出的干净 UV；`--diffuse` 指错会覆盖源图）——
把顺序固定在代码里，这两个坑就不可能再犯。**实测可复现**：连跑两次，三张贴图逐字节一致、顶点数逐级一致。

### 分步跑（要调参数时）

**第 1 步：网格（选件 → 重定向骨架 → 6 级 LOD → 导出）**

```bash
SKEL="<游戏根>/modding_resources/skeletons/human_skeleton.fbx"
BLENDER="C:/Program Files/Blender Foundation/Blender 5.2/blender.exe"
OUT="tools/armor-pipeline/out"

"$BLENDER" -b --python tools/armor-pipeline/scripts/build_armor.py -- \
    --src  "<源角色.fbx>" \
    --skel "$SKEL" \
    --out  "$OUT" \
    --name taikou_yukimura_do_a \
    --parts body_kimono_arms --kimono-torso-only --no-hands \
    --r 0.0120 --r-arms 0.0160
```

**第 2 步：贴图（裁图集 + 生成 _n / _s + 重映射 UV）**

```bash
"$BLENDER" -b --python tools/armor-pipeline/scripts/build_textures.py -- \
    --armor   "$OUT/taikou_yukimura_do_a.fbx" \
    --src     "<源角色.fbx>" \
    --diffuse "$OUT/_src_L00_yukimura.png" \
    --out     "$OUT" --name taikou_yukimura_do_a \
    --parts body_kimono_arms --ao 0.35
```
🔴 **`--diffuse` 必须指向原始图集，不能指向输出**（脚本里有防呆，同路径直接报错退出）。
🔴 **`build_textures.py` 不幂等** —— 它把自己输出的 FBX 再跑一遍会把 UV 映射两次。
脚本用「甲的 UV 是否超出源件 UV 范围」自动判定并拒跑；要重跑就先重跑 `build_armor.py`。

**看效果**（两件事都做：验合身 + 验贴图）

```bash
# 合身（平色，一眼看轮廓）
"$BLENDER" -b --python tools/armor-pipeline/scripts/check_fit.py -- \
    "$OUT/<name>.fbx" "Debug/offline/core_game/fbx/body/body/body_male_a.fbx" "$OUT" v1

# 贴图（PBR：_d 挂底色、_n 挂法线、_s 的 R→金属度 / G→1−粗糙度）
"$BLENDER" -b --python tools/armor-pipeline/scripts/render_textured.py -- \
    "$OUT/<name>.fbx" "$OUT" "$OUT" "<name>" \
    "Debug/offline/core_game/fbx/body/body/body_male_a.fbx"
```

**进编辑器之前体检**（验导出规格）：

```bash
python tools/face-pipeline/scripts/fbx_probe.py "tools/armor-pipeline/out/<name>.fbx"
```
期望：`UpAxis=2 FrontAxis=1 FrontAxisSign=-1 CoordAxis=0 CoordAxisSign=1 UnitScaleFactor=100`，
且每个 Geometry 的 Model 节点**零缩放零旋转**。（骨骼自身带旋转是正常的。）

（身体 FBX 由 `tpaccli dump --format fbx --filter body_male_a` 产出，见 §5）

### 参数

| 参数 | 默认 | 说明 |
|---|---|---|
| `--parts` | `body` | 取哪几件。`body`=胴+草摺；`body_kimono`=再加内衬着物；**`body_kimono_arms`=再加 袖(sode)+籠手(kote)**（推荐，跟原版 `body_armor` 覆盖手臂的做法一致）；`arms`/`head` 预留 |
| `--r` | `0.01` | **躯干径向缩放 = 甲与身体之间的余量旋钮**。大了甲发飘，小了身体顶穿甲。实测原版身体上 **0.0120** 合适 |
| `--r-arms` | = `--r` | **手臂的径向缩放**（只放径向，见下条）。原版身体的胳膊比源件粗，躯干调够之后手臂仍会顶穿 → 单独放到 **0.0160** |
| —— | —— | 🔴 **手臂的缩放必须拆成「径向 / 沿骨轴」两个方向**。踩过：一开始用 `Matrix.Scale(R_ARMS)` 各向同性地放，径向确实盖住了身体，但**沿骨轴也被拉了**（前臂 25.97cm × 0.0165 = 0.429m，而骑砍肘→手腕只有 0.267m）→ **籠手末端超出拳头 16cm**。现在沿轴按**解剖段长**算（`seg_ratio()`：源件肘→腕 与 骑砍肘→腕 的比值，由两边关节坐标实时算），径向才用 `R_ARMS` |
| `--no-hands` | 关 | 按**主骨**切掉手(`bone_18/19`)与手指(`bone_26~45`)的顶点。籠手该止于手腕，手留给骑砍身体 |
| `--kimono-torso-only` | 关 | 按主骨过滤掉着物里的头和手（否则出现"头顶飘块 + 袖子变形"）。**只作用于着物那一件** |
| `--cut-z` | `0` | 只保留高于该高度（米）的面；用于日后拆腿甲 |
| `--lod` | `0.834,0.563,0.249,0.140,0.072` | LOD1~5 的减面比（抄原版 `aserai_cavalry_armor` 的顶点递减） |
| `--pad` | `0.012` | （贴图）裁图集时四周留边 |
| `--nrm` | `0.30` | （贴图）法线细节强度。**默认刻意做轻**：源贴图已画好明暗，加狠了会"重光照" |
| `--ao` | `0.6` | （贴图）AO 强度。亮度按 90 分位归一化后做代理。**首件用的是 0.35**（弱一点更自然，源图已画好缝隙阴影）|
| `--pot` | 关 | （贴图）输出压到 2 的幂。**别默认开**：本例 512×696 取整到 512×512 白丢 26% 分辨率 |
| `--debug` | 关 | 打印逐骨旋转角 / 平移偏移 / 区域标签图 |

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
| 3 | **骨的锚点用 head 位置；缩放拆「径向 / 沿骨轴」两个方向，不做全骨 λ 拉伸** | 逐骨 λ 在关节处产生剪切（试过，甲被吹飞/切变）。沿骨轴只在**手臂链**上按解剖段长对齐（因为各向同性放大会把手臂也拉长）|
| 4 | **手调旋钮 = `--r`（躯干）+ `--r-arms`（手臂径向）** | 判定法：`_fitcheck.py` 逐 z 带量「甲半径 vs 身体半径」，甲必须处处更大 |

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
| 到处露白（身体顶穿甲） | 甲的半径小于身体 | 调大 `--r`（实测 0.010 → 0.0120）；**手臂要单独调 `--r-arms`**（躯干够了两条胳膊仍会露一大条） |
| 🔴 **肩帯（胴的过肩带）整条消失** | 着物瘦身过滤**作用在了合并后的整块甲上**，而保留列表里没有锁骨骨 `bone_12/bone_13` —— 而肩帯正好由锁骨驱动 | 过滤**只能作用于着物那一件**（已改到合并之前、按子网格号只挑 submesh_1） |
| 🔴 **上臂外侧露出一大条身体** | 原版身体的胳膊比源件粗；`--r` 是按躯干调的，手臂不够 | `--r-arms` 单独放（0.0120 → **0.0160**）。判定法：`Debug/offline/armor_probe/_armzoom.py` 怼近左臂渲一张"甲单独 vs 甲+身体（身体染红）"对比 |
| 🔴 **拳头被甲整个包住** | 收进来的源件网格带**手和手指**的几何（`submesh_3` 263 顶点里 230 个在左手/手指骨上；`submesh_1` 也带手）。籠手该止于手腕，手留给骑砍身体 | `--no-hands`：按**主骨**判断，落在 `bone_18/19`（手）或 `bone_26~45`（手指）上就删。🔴 **别整件丢 `submesh_3`** —— 它除了手还带上臂的袖子，丢了会导致上臂露白（两头都要） |
| 🔴 **籠手盖住拳头（手腕没伸出来）** | 手臂缩放各向同性 → 沿骨轴也被拉长，籠手末端越过手腕 | 拆成径向/沿轴两个方向（见参数表）。**判定法**：`_armzoom.py` 怼近看 |
| ⚠️ **上臂中段露身体（未根治，两难点）** | 源件那里本来是**着物的布袖子**盖的；而着物是紧贴胳膊的，骑砍身体更粗 → 着物陷在身体里。想靠袖/籠手盖住只能**径向猛撑**，但撑到能盖住（≈0.0185）手臂就变香肠；不撑（≈0.0130）露一大块。**当前取中间值 0.0160** | 干净的解法是**加一段程序生成的布袖**（绕上臂骨的筒，权重给 `upperarm_twist_15`，UV 抄最近着物顶点）。尚未做 |
| 🔴 **渲染图里"只有上臂、没有小臂"** | **相机取景写死**，A-pose 伸出去的小臂落到画框外——是裁掉了，不是没生成 | 两个渲染脚本已改成 `autoframe()`：从**可见网格的包围盒**算 `ortho_scale` 与中心。**凡是看渲染图怀疑"缺件"，先量 bbox 再看画框** |
| 骨架导入后对象带 0.9975 缩放 | Blender 把 `global_scale` 折成"对象缩放 × 单位换算" | 导入后对骨架 `transform_apply` 烘平 |
| 量到的"洞"其实是模型本来顶点稀疏 | 源模型在踝上方 8cm 内 0 顶点（低模） | 先量源模型的 z 直方图，别急着修 |
| 🔴 **满身品红斑块**（看着像贴图坏了） | **渲染脚本的锅，不是数据**：FBX 里 6 个 LOD 网格位置完全重合，而导出时 `embed_textures=False` → 其余 LOD 带着**指向已失效文件的贴图节点**。Blender 用**品红**画"贴图丢失"的面，和 LOD0 抢像素 | 渲染脚本里把非目标 LOD `hide_render = True`（两个渲染脚本已修）。**判定法：把底色当自发光渲染 —— 干净就说明贴图/UV 没问题，毛病在光照或重叠** |
| 🔴 **源图集被自己的输出覆盖** | 脚本的输出 `_d.png` 和输入 `--diffuse` 是同一个路径，重跑时读到上一轮的裁切结果 | 加同路径防呆（已加）；`--diffuse` 永远指向原始图集 |
| 🔴 **UV 被映射两次** | `build_textures.py` 不幂等，拿自己的输出当输入重跑 | 用「甲的 UV 是否超出源件 UV 范围」自动拒跑（已加） |
| 🔴 **编辑器报 `Unable to find material for mesh <名字>` + 一设材质就崩** | FBX 从源件继承了脏材质：名字是 `mat_L00_yukimura`（编辑器按名字找项目里的材质资产，找不到），**还带着指向不存在文件的贴图节点**（源件路径已失效）→ 编辑器去加载 → 崩 | `build_armor.py` 已改成：材质名固定为 `<name>`（见下条命名关系）、节点树清空重建、**一个贴图节点都不挂**；导出后加自检扫贴图引用与 `mat_*` 脏名 |

### 🔴 编辑器资产命名关系（tpaccli list 实测，2026-09-14）

```
编辑器写入的文件名              资产**内部名**
<名>_geo.tpac          ←→     <名>            （网格）
<名>_mtl.tpac          ←→     <名>            （材质）
<名>_d_tex.tpac        ←→     <名>_d          （贴图）
<名>.fbx               ←→     （源文件引用）
```

**`_geo` / `_mtl` / `_tex` 是文件名后缀，不是资产名。**资产名就是你在编辑器里输入的那个。
**网格与材质本来就同名**（靠类型 GUID 区分）——范本：蒂法头 `head_tifa_a_v11_geo.tpac` 与 `head_tifa_a_mtl.tpac`，内部名都是 `head_tifa_a`。

⇒ 因此 FBX 里的材质名必须 = 网格名 = `<name>`，**别自作聪明加 `_mtl`**。编辑器靠名字把 FBX 的网格和项目里的材质资产对上；对不上就每个 LOD 弹一次警告。

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
| 3 | 贴图三族：`_d` 裁图集 / `_n` 法线 / `_s` 金属度·粗糙度·AO | ✅ |
| 4 | **编辑器 Publish → 进游戏看** | ⏳ 人做 |
| 5 | 拆件：脛当+靴 → `leg_armor`；両腕 → `hand_armor`（用 `--parts arms` + `--cut-z`） | ⏳ |
| 6 | 兜 → `head_armor`（注意工程文档 §3.5：头盔会随脸形参数自动缩放，未实测） | ⏳ |
| 7 | 靴子形状：源模型低模，脚部是块状，可考虑重建 | ⏳ |
| 8 | `_s` 分区可再细化：现在按「UV 区域 + 贴图颜色」自动分五类，金饰占比只有 0.2%，阈值可调 | ⏳ |

### 贴图那一步做了什么（`_s` 的分区依据）

不是靠猜颜色，是**两个信息合起来判**：

| 依据 | 用来判 |
|---|---|
| **UV 区域**（把哪件网格的三角形烤进 UV 空间） | 内衬着物 → 整片按布处理（非金属、高粗糙） |
| **贴图颜色**（在甲片区内再分） | 亮金 → 金属 0.90 / 粗糙 0.32；饱和红（漆面札板）→ 半金属 0.35 / 粗糙 0.38；白（系威绳、白帯）→ 非金属 / 粗糙 0.88；暗褐（皮）→ 非金属 / 粗糙 0.68；其余（袴、布）→ 非金属 / 粗糙 0.88 |

AO（`_s` 的 B 通道）= 亮度做代理，**按 90 分位归一化**（直接拿绝对值会把整张甲压暗，因为源图的缝隙阴影本来就画在 diffuse 里了）。
