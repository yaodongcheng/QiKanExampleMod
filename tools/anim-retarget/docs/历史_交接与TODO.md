# 骑砍2 动画重定向 · 交接说明与 TODO

> 📁 **路径说明**：本文写于旧目录结构；现已重组为 `input/ pipeline/ output/ viewer/`（见顶层 `README.md`）。
> 文档里的 `out/xxx` 对应现在的 `output/fbx|trf/`，脚本对应 `pipeline/`。**内容结论不变**。

> 交接日期：2026-09-18 · 主要产出在 `骑砍2动画重定向/` 下的两个子目录
> 本文档：现状 → 交付物索引 → **待办 TODO（含命令）** → 踩过的坑 → 环境与复现

---

## 0. 一句话现状

> **2026-09-19 更新**：交接清单里的 **T1 / T2 / T3 已全部完成**（280 段导出 → 重定向 → 三级筛选查看器），
> 并在此过程中**又抓到一个会误导全部对比结论的 bug**（源侧 GLB 只动了 10 根骨）——见 §8。
> 新的查看器：`ue5_失败原因复查/web_slim/`（280 段 · ground 181 + src 99）。

两条线都跑通了，并且**定位并修掉了原项目「UE5 重定向失败」的 3 个根因**。
**重定向流程的最后一环已补齐：每次导出 FBX 时会自动再导一份 TRF（ModKit 用的骨骼动画容器）。**

| 线 | 规模 | 结果 |
|---|---|---|
| ① 战国无双2 铁炮兵 → 骑砍2 | 40 段（含 p006） | align 模式，肢段方向误差 mean **10.9°**（19 根骨里 16 根 ≤0.03°） |
| ② UE5 小白人 → 骑砍2 | 29 段地面动作 + **130 段飞行动画** | mean **6.93°** / max 28.05°（原方案 37.53° / 146°） |

配套 3 个「双人并排」对比查看器（源 vs 骑砍2，选动画同时播放）。

---

## 0.5 算法选型结论（2026-09-19 定稿）

> **交付只走 `align` 模式，其余算法已归档。**

`--pose` 三种模式的实测对比（同一份 SW2 铁炮兵 p006，逐骨"肢段方向"误差，越小越像源）：

| 模式 | mean | 说明 | 处置 |
|---|---|---|---|
| **`align`** ★ | **10.93°** | 世界空间旋转增量 + **逐骨静止对齐 `A⁻¹`**。19 根可度量骨里 16 根 ≤0.03° | **唯一交付** |
| `delta` | 20.29° | 只搬增量、不做静止对齐 → 把源 T-pose 与目标 A-pose 的站姿差原样保留（手臂恒偏 25~33°） | 已归档 `out/archive/`，仅作对照证据 |
| `absolute` | 155.81° | 直接写源的绝对朝向 → 被骨轴约定毁掉，**骨盆多转 90°、人倒挂** | 同上，反例 |

- 脚本里 `--pose` 的**默认值本来就是 `align`**，日常不用传
- 归档位置：`战国无双2铁炮兵_p006_重定向/out/archive/`（含 delta/absolute + BoneAnimCopy / BioSculpt 两个插件的对照产物）
- **不要再把 delta/absolute 的产物拿去交付或接入**

## 0.6 标准产线（一条命令走完）

```
源动画 ──① 重定向(align)──▶ ② 导出 FBX(ModKit 规格) ──③ 自动导 TRF──▶ 交付
         retarget_sw2_to_bannerlord.py / ue_align.py            (脚本内置)
```

```bash
# SW2 线（默认 --pose align；FBX 与 TRF 会同时产出）
cd "D:/BrainMaker/骑砍2动画重定向/战国无双2铁炮兵_p006_重定向"
blender -b --python retarget_sw2_to_bannerlord.py -- --name sw2_gunner_p006_alig
#   -> output/fbx/sw2_gunner_p006_alig.fbx   (28 骨 / 只导骨架 / Z-up / cm / 根名 human_skeleton_notused)
#   -> output/trf/sw2_gunner_p006_alig.trf   (绝对局部变换语义，已过 CHECK_OK / CHECK_POS)
# 加 --no_trf 可只导 FBX
```

**TRF 三个要点（详见 `docs/TRF规范.md`）**：
1. 存的是**绝对局部变换**，不是增量（增量语义会把姿态炸开）
2. **骨序=唯一契约**（文件里不存骨骼名，纯索引）→ 必须 28 骨且顺序为权威序
3. 时间列 = Blender 帧号 → ModKit 里 `Source 1 / Source 2` 要填成该 TRF 的帧范围

## 1. 交付物索引

| 目录 | 内容 |
|---|---|
| `战国无双2铁炮兵_p006_重定向/` | SW2 线：重定向脚本、40 段产物、map、核对页 `index.html`、查看器 `web/` |
| `ue5_失败原因复查/` | UE 线：根因复查报告、修好的重定向脚本、**3 个查看器**、校验脚本 |
| `ue5_失败原因复查/web/` | 查看器①：29 段地面动作（源 UE 小白人 vs 骑砍2） |
| `ue5_失败原因复查/web_flight/` | 查看器②：**130 段飞行动画** |
| `ue5_失败原因复查/web_slim/` | **查看器④（新）：动作精简清单 280 段**，三级筛选读 CSV，逐段自动选烘焙模式 |
| `UEAnims/exported_slim/` | **新增**：280 段精简清单 FBX（精确匹配导出，0 漏 0 失败） |
| `战国无双2铁炮兵_p006_重定向/web/` | 查看器③：SW2 铁炮兵 40 段 |
| `UEAnims/exported_fbx/` | 29 段源 FBX（**原有**） |
| `UEAnims/exported_flight/` | **新增**：130 段飞行 FBX（本次从 UE 4.26 导出） |

必读文档：
- `docs/UE5重定向失败原因复查.md` —— 3 个根因 + 实测对比
- `_legacy/old_viewers/ue_basic/说明_查看器.md`、`_legacy/old_viewers/ue_flight/说明_飞行动画查看器.md`
- `docs/SW2_可行性结论.md`

---

## 2. 已完成（带可复核数字）

### 2.1 三个根因（原项目「失败」的真正原因）

| # | Bug | 现象 | 修法 |
|---|---|---|---|
| 1 | **导入骑砍 FBX 会改场景帧率**（→24），导出时长膨胀 3.4%~**25%** | 播放"慢半拍/跟不上" | 导入后把 `scene.render.fps` 还原；按**时间**重采样到统一 30fps |
| 2 | 公式缺 **逐骨静止对齐** `A⁻¹` | 姿态差 37~53°（弯腰驼背、手臂偏） | `T_pose = R·A⁻¹·T_rest`（align 模式） |
| 3 | 映射表漏掉 **扭骨**（`upperarm/lowerarm_twist_01_*`） | 小臂/手偏 30~44° | 接到骑砍的 `*_upperarm_twist1` / `*_foretwist1` |

### 2.2 复测数字

| 资产 | 对比对象 | 误差 |
|---|---|---|
| `ue_mannequin_src.glb`（29 段源） | 源 FBX | mean **0.29°** / max 7.03° |
| `bannerlord_from_ue.glb`（29 段重定向） | 源 FBX | mean **6.93°** / max 28.05° |
| 原 `output_world/*.fbx` | 源 FBX | mean 37.53° / max 146.35° |

### 2.3 本次额外修掉的坑（查看器侧）

- **查看器量身高方法错**：`geometry.boundingBox × matrixWorld` 对 `SkinnedMesh` 不成立（没算 bind 矩阵），把 UE 小白人量成 182.57（实际 1.83m）→ 缩放算成 0.0096，人缩成地面一小块。已改成**用骨骼静止位置量身高**（3 个查看器都改了）。
- **`Mannequin_src.fbx` 单位不一致**：骨架被套在 `scale=0.01` 的空物体下 → 导出的 GLB 里"骨骼静止在 cm(96.75)、动画值在 m(1.168)"，整人被拽到地面。已改为导出前摘掉该父级 + 刷新 depsgraph + 位移做单位换算。
- **飞行不能用贴地**：飞行是离地的，逐帧贴地会把人粘在地面 → 飞行线改用 `--pelvis src`（复制源骨盆位移，以**静止姿态**为基准）。

---

## 3. TODO（接手待办）

### ~~T1~~ ✅ 已完成（2026-09-19）导出「骑砍2动作精简清单」280 段（P0 132 + P1 148）

**目标**：把 `input/inventory/骑砍2动作精简清单.csv` 里 280 条动画导出成 FBX。
（原路径 `UEAnims/anim_inventory/`，2026-09-19 目录重组后移入 `input/inventory/`，下同）

**关键事实（已核实）**：
- 这 280 段**全部在同一个工程里**：`UEAnims/2-3583动捕动作包 官方骨骼动作/3583.uproject`（**UE 5.0**）
  - `Content/AdventureAnimset/` 1054 个 uasset + `Content/MocapCMU-InPlace/` 2548 个 uasset
- 清单字段：`优先级(P0/P1) / 武器来源(7类) / 动作功能 / 动作名 / 来源包 / 语义描述 / 资产路径`

**怎么做**：
```bash
cd "D:/BrainMaker/骑砍2动画重定向/UEAnims"
export BM_OUT_DIR="D:/BrainMaker/骑砍2动画重定向/UEAnims/exported_slim"   # 新目录
export BM_ANIM_FILTER="<从 csv 的 资产路径 拼的子串过滤，用 ; 连接>"
export BM_MAX_N=0
"D:/UNREAL/UE_5.0/Engine/Binaries/Win64/UnrealEditor.exe" \
  "D:/BrainMaker/骑砍2动画重定向/UEAnims/2-3583动捕动作包 官方骨骼动作/3583.uproject" \
  -run=pythonscript -script="D:/BrainMaker/骑砍2动画重定向/UEAnims/exporter/ue_export_fbx.py" \
  -unattended -nosplash -nullrhi -stdout
```
- 过滤是**子串匹配**（`any(t in asset_path)`），280 条路径建议按目录前缀压成少数几个子串（如 `Animations/Sword/`），否则串太长
- 注意子串会**多带**（例：`Anim_JU_in_PL` 会带出 `_land`/`_fall_loop`）—— 多带可接受，少带不可接受
- 输出到 `exported_slim/`（**新目录，不要覆盖 `exported_fbx/`**）

**产物**：`UEAnims/exported_slim/*.fbx` + `export_trace.log`
**预估**：导出 280 段约 5~15 分钟（引擎冷启动 1~3 分钟）

### ~~T2~~ ✅ 已完成（2026-09-19）批量重定向这 280 段 → GLB，并入查看器

```bash
cd "D:/BrainMaker/骑砍2动画重定向/ue5_失败原因复查"
# 源侧（UE 小白人自身的动作，供查看器左侧显示）
blender -b --python ue_batch_glb.py -- --mode ue --animdir ".../UEAnims/exported_slim" \
        --pelvis ground --out viewer/datasets/ue_slim/assets/ue_mannequin_ground.glb
# 骑砍侧（重定向结果）
blender -b --python ue_batch_glb.py -- --mode bl --animdir ".../UEAnims/exported_slim" \
        --pelvis ground --out viewer/datasets/ue_slim/assets/bannerlord_ground.glb
```
**注意**：
- 清单里**混有**地面动作与特殊位移动作（爬行/潜行/翻越）。地面类用 `--pelvis ground`（逐帧贴地）；**带位移/离地的必须改 `--pelvis src`**，否则会被压在地面
- 建议**按 `动作功能` 分组分批跑**，每批一个 GLB，`--pelvis` 分别设置

**产物**：`web/assets/*_slim.glb`（每个含全套动画，名字与源一致）；若这批要进 ModKit，还要按 §0.6 走 FBX+TRF
**预估**：280 段 × 平均 2~3 秒 → 单侧约 10~20 分钟

### ~~T3~~ ✅ 已完成（2026-09-19）查看器筛选升级：读 CSV / manifest，三级筛选

现状：查看器的「动画类型」下拉是**按名字正则猜**的（`viewer/viewer.html` 里的 `CATS`）。
目标：改成读 `input/inventory/骑砍2动作精简清单.csv`，支持**三级筛选**：

```
优先级(P0/P1) × 武器来源(CMU/剑·单手/弓/通用/通用·徒手/特殊技/掩体) × 动作功能(A1_行走 …)
```
**做法**：写一个小脚本把 CSV 转成 `manifest.json`（`{clip名: {prio, weapon, func, pack, desc}}`），
查看器 `fetch` 它，下拉/多选筛选；找不到条目的 clip 归入「未分类」。
**注意**：CSV 的 `动作名` 就是 FBX/GLB 里的 clip 名（例 `079_49`），可直接做键。

### T4 把修复合并回原脚本（**需先与用户确认**）

目前**没有改动**原 `ue_to_bannerlord_retarget_world.py`、`output_world/`、`output_high/`。
若确认要合并：把 §2.1 的三处（fps 还原 / align 的 `A⁻¹` / 扭骨映射）移植进去，并重跑 29 段。

### T4.5 ✅ 已完成：导出流程按 ModKit 规格更正

`retarget_sw2_to_bannerlord.py` / `ue_align.py` 的 FBX 导出已改为**一步直出 ModKit 规格**（只导骨架 / `add_leaf_bones=False` / 根名 `human_skeleton_notused` / Z-up / cm）；
`drive_boneanimcopy.py`、`drive_biosculpt.py` 也已补 `add_leaf_bones=False` + 骨架-only。
实测 p006 重导：**28 骨 / 无 `*_end` / 0 网格**。

> 详见 `README_骨骼经验.md` §2 与 §2.5。**遗留**：40 段全量还没用新流程重导（见 T2）。

### T5 SW2 线剩余项

- 武器**未随动**：铁炮挂在 SW2 自己的手骨上，未挂到骑砍手骨
- **未接入游戏**：FBX 未经骑砍 ModKit 导入建 Clip、未接 `SetActionChannel`、未做 foot IK 验证

### T6 可选：其它素材包

`input/inventory/GhostSamurai_SuperheroFlight盘点.csv` 还有 1571 条
（GhostSamurai 1381 = Mannequin 版 685 + 原角色版 696；SuperheroFlight 190），
`FCS战斗动画盘点.csv`（422）、`mySekiro射击动画盘点.csv`（193）等也未见导出。同一套流程可直接复用。

> 📌 另见 `input/inventory/INDEX.md`（盘点产物索引）与 `README_素材库盘点与状态机设计.md`（6 池盘点方法论 + 骑砍2 状态机设计）。

---

## 4. 踩过的坑（接手前必读）

1. **`bpy.ops.import_scene.fbx()` 会改 `scene.render.fps`**（骑砍 FBX 带 24fps、UE 带 25/30fps）→ 导出时长错。**每次导入完都要把 fps 设回你要的值**。
2. **改 `obj.parent` 后必须 `bpy.context.view_layer.update()`**，否则 `matrix_world` 还是旧的（本次因此多绕了一轮）。
3. 两套骨架**骨骼轴向约定不同** → 不能用 `absolute`（绝对朝向）模式，会把骨盆多转 90°、人倒挂。
4. **扭骨必须映射**：UE 动画动 67 根骨，只映射 22 根标准骨会让小臂/手偏 30~44°。
5. 骑砍 FBX 编辑器可执行名：**UE4 是 `UE4Editor.exe`，UE5 是 `UnrealEditor.exe`**。
6. **UE4 工程的 Python 插件默认没开**：需在 `.uproject` 里加 `PythonScriptPlugin`（本次已给 SuperheroFlight 加，原文件备份为 `.bak_before_python_plugin`）。
7. 导出过滤是**子串匹配**，会多带同名前缀的动画。
8. **无头 Chrome 的 `--screenshot` 对 WebGL 画布不可靠**（截到空白）。改用页面 `?shot=1` 把画布 `toDataURL` 塞进 DOM 的 `#shotdata`，再 `--dump-dom` 取回解码。
9. **`--virtual-time-budget` + 永不停止的 RAF 会挂死**：截图模式要在渲染 N 帧后停住 RAF（本项目已内置）。
10. **Python `http.server` 默认单线程**，两个大 GLB 并发会互相阻塞（曾导致页面"一直转圈"）。已改 `ThreadingHTTPServer`（`viewer/serve.py`）。
11. **`.bat` 含中文要存 GBK**，否则 cmd 乱码。
12. **`Mannequin_src.fbx` 外面套着 `scale=0.01` 的空物体** → 导出的 GLB 单位不一致（见 §2.3）。
13. bash 里调 Blender 要用**完整路径**（`blender` 不在 PATH）。

---

## 5. 环境与路径

| 项 | 路径 / 说明 |
|---|---|
| UE 5.0 | `D:/UNREAL/UE_5.0/Engine/Binaries/Win64/UnrealEditor.exe` |
| UE 4.26 | `D:/UNREAL/UE_4.26/Engine/Binaries/Win64/UE4Editor.exe` |
| （另有 UE 4.27 / 5.1 / 5.3） | `D:/UNREAL/` |
| Blender | `C:/Program Files/Blender Foundation/Blender 5.2/blender.exe` |
| 3583 工程（UE 5.0） | `UEAnims/2-3583动捕动作包 官方骨骼动作/3583.uproject` ← **含 AdventureAnimset + MocapCMU-InPlace** |
| 飞行工程（UE 4.26） | `UEAnims/SuperheroFlightAnimations/SuperheroFlightAnimations.uproject`（已加 Python 插件） |
| **TRF 转换工具** | `pipeline/common/fbx_to_trf.py`（唯一正确版，绝对语义；`_deprecated/` 是作废版） |
| CMU 原始工程（UE 4.17，未用） | `UEAnims/1-CMU .../MocapCMU_InPlaceV2.uproject` |

**当前在跑的服务（都是本次起的，实测端口如下，可随需重启）**：

| 端口 | 内容 | 目录 |
|---|---|---|
| 8794 | **UE 29 段查看器**（地面动作） | `ue5_失败原因复查/web/` |
| 8797 | **UE 130 段飞行查看器**（推荐，多线程实例） | `ue5_失败原因复查/web_flight/` |
| 8791 / 8792 | SW2 铁炮兵 40 段查看器（8792 是重复实例，可忽略） | `战国无双2铁炮兵_p006_重定向/web/` |
| 8796 | 飞行查看器（旧单线程实例，建议弃用） | `ue5_失败原因复查/web_flight/` |

> 每个查看器目录下的 `serve.py` 会从各自起始端口往后找空闲端口（`web` 起 8792、`web_flight` 起 8796、SW2 起 8791），
> 所以**重启后端口会变**，以控制台打印为准；双击对应目录的 `启动查看器.bat` 会自动开浏览器。

---

## 6. 复现命令速查

```bash
# 单体：SW2 p006 -> 骑砍2
cd "D:/BrainMaker/骑砍2动画重定向/战国无双2铁炮兵_p006_重定向"
blender -b --python retarget_sw2_to_bannerlord.py -- --pose align --pelvis ground \
        --name sw2_gunner_p006_alig --dump output/verify/dump_align.json

# 批量：UE 动作 -> 骑砍2 GLB（--animdir 指向任意 FBX 目录）
cd "D:/BrainMaker/骑砍2动画重定向/ue5_失败原因复查"
blender -b --python ue_batch_glb.py -- --mode bl --animdir "<FBX 目录>" \
        --pelvis ground|src --out viewer/datasets/ue_slim/assets/xxx.glb

# 单独导 TRF（FBX → TRF；平时重定向脚本已自动调用）
blender --background --factory-startup --python-exit-code 1   --python "D:/BrainMaker/骑砍2动画重定向/OpenTrf/fbx_to_trf_fixed.py" --   --fbx "<in.fbx>" --out "<out.trf>"

# 校验（穷举 8 种朝向变换取最优，对双方公平）
blender -b --python srcglb_check.py -- --clip 010_01    # 源 GLB 是否忠实
blender -b --python glb_check.py   -- --clip 010_01    # 骑砍 GLB 重定向质量

# 查看器
cd web_flight && python serve.py     # 自动找空闲端口并开浏览器
```

---

## 7. 已知未解决 / 局限

- 骑砍骨架**没有手指骨**（只有一根 `*_finger0`）、**没有 thigh/calf twist** → 这几组无法映射（真实骨架差异，非 bug）
- 飞行动画两人**仍有约 48px 高度差**（约 0.29m）：主要是两个模型静帧姿势/体型差异，骨盆高度本身已对齐（都是"静止+0.20m"）
- SW2 线**未做武器随动**与**游戏内接入**
- 早期遗留的 http.server 实例（8792/8793/8796）可能还在，重启电脑即清理

---

## 8. 2026-09-19 进展：T1~T3 完成 + 一个会误导全部对比结论的 bug

### 8.1 已完成（可复核）

| 项 | 结果 | 证据 |
|---|---|---|
| **T1** 导出精简清单 280 段 | **280/280 精确命中，0 漏 0 失败** | `input/source/ue_mannequin/clips_slim/export_trace_slim.log` |
| **T2** 重定向 → GLB | 骑砍侧 `bannerlord_ground.glb`(181) + `bannerlord_src.glb`(99)；源侧 6 个分片 | `web_slim/assets/` |
| **T2** 覆盖自检 | **280/280 两侧齐备、mode 一致、逐段时长差 0.0000 s、无多余段** | `pipeline/common/verify_shards.mjs` |
| **T3** 查看器三级筛选 | `manifest.json`（读 CSV，280 条）+ 优先级×武器来源×动作功能 + 搜索 | `web_slim/` |
| 视觉验收 | 8 格对照图，**8/8 通过** | `output/verify/slim_verify/对照表_修复后8段.png` |

新增脚本：`pipeline/common/export_ue_fbx_slim.py`（清单精确导出）、`pipeline/common/make_manifest.py`、
`pipeline/common/verify_shards.mjs`、`web_slim/check_*.mjs`、`pipeline/common/shot_headless.py`（无头截图）。

### 8.2 ★ 本轮抓到的关键 bug：查看器左侧的"源"GLB 只有 10 根骨在动

- **现象**：查看器左右两侧姿态对不上（源垂手 / 骑砍举手、源平躺 / 骑砍站立…），
  且任何"源 vs 骑砍"的自动比对都稳定给出 ~30° 的大误差。
- **根因**：`ue_batch_glb.py --mode ue` 里
  `CORE = list(UEMAP.values()) + [8 根扭骨]` —— `UEMAP.values()` 是**骑砍的骨名**
  （`l_upperarm_twist` / `spine1` / `l_foretwist`…），拿去过滤 **UE 骨架**只剩
  `pelvis`、`head` 命中。于是源侧 GLB 实际只有 **10 根骨有动画**
  （pelvis、head + 8 根扭骨），**上臂/前臂/大腿/小腿/脚/脊柱全部冻在 bind pose**。
  实测确认：`ue_mannequin_ground.glb[002_01]` 旋转轨道 67 条，**真正在动(>2°) 只有 10 条**。
- **修法**：源码模式改用**源骨架自己的骨名**全骨自映射
  （`CORE = [b.name for b in base.data.bones]`，并把 `--bones` 默认值改为 `all`）；
  重烘后 `映射基数=67`。
- **验证**：项目原 `glb_check.py`（以**源 FBX** 为基准）实测
  「源 GLB vs 源 FBX」从 **mean 25.35° / max 35.52°** → 见 §8.3 的复测；
  同时修复后 8 格对照图视觉复检 **8/8 通过**，上一轮点名的 3 格（`002_07`/`002_06`/`Anim_CS_KD_F`）全部对齐。

> **教训**：把"目标骨架的骨名"用在"源骨架"上，不会报错、只会静默失效——
> 骨骼映射表**必须分方向**校验（源侧用源名、目标侧用目标名），并**实测"真正在动的骨数"**。

### 8.3 ★ 度量陷阱：跨骨架比对，用错口径会得出完全错误的结论

本轮先后试了 4 种自动口径要量"重定向误差"，**前 4 种全部作废**，原因各不相同：

| 口径 | 为什么不可用 | 静止姿态基线 |
|---|---|---|
| 关节相对骨盆的位置差（米） | 混入两套骨架的**骨长/比例差**（UE 小白人大头小身 vs 骑砍写实） | — |
| 肢段方向角（父→子，米/度） | 两边**父骨命名不同**（`hand_l` 的父是 `lowerarm_l`，而目标 `l_hand` 的父是 `l_foretwist1`） | 12.52° |
| 骨的世界朝向 | 量的是**局部轴约定**，不是动画 | **113.73°** |
| 相对静止的旋转增量 ΔR | 两套骨架 **bind 姿势本身不同**，且 align 的 `A⁻¹` 修正使 ΔR 本就不该相等 | — |
| **✅ 以源 FBX 为基准 + 穷举 8 种朝向变换取最优** | 项目原 `glb_check.py` 口径：对两边都公平，且能吃掉全局朝向/镜像差异 | ≈0 |

> **教训**：跨骨架比对**必须先算"静止姿态基线"**。基线不为 0 的口径直接作废。
> 另外，**不能用"源 GLB"当基准**去量"骑砍 GLB"——源 GLB 本身就有 §8.2 那个 bug，
> 用它当尺子会把源侧的误差算到骑砍头上（本轮就是这么被误导了好几轮）。

**权威数字（以源 FBX 为基准，`glb_check_slim.py`，采样 21 个时间点，全 280 段）**：

```
骑砍2 重定向 280 段:          mean 13.79°   max 179.77°
查看器左侧源 GLB 280 段:      mean 21.39°
```

> **怎么读这两个数字（很重要）**：连「把源动作原样过一遍 GLB 管线」都读到 **21.39°**，
> 说明这个口径里有一条**约 20° 的"地板误差"，来自 FBX 导入路径 与 GLB 导入路径 的骨架空间差异**
> （而**不是**动画本身）。对照证据：项目原 `glb_check.py` 当年跑 29 段时，
> 源侧 GLB 读到 **0.29°**，那是因为那批源 GLB 是**FBX 直接导出**的（没有重打关键帧）。
> 本轮源侧是"重打关键帧"烘的（保住了朝向、但骨骼在场上的位置取自基底骨架 `Mannequin_src.fbx` 的 rest 偏移），
> 于是与源 FBX 之间就多出这条几何地板误差。
> **骑砍侧 13.79° 已经低于源侧自身的 21.39°** → 偏差主要来自"两套骨架 + 导入路径"，
> 而不是重定向算法本身。**因此这个数字不能当"重定向误差"的绝对值用**，
> 只能当"有没有崩坏"的筛查（配合 8 格视觉复核与覆盖/时长自检一起看）。
>
> 新增 TODO（T7）：把源侧 GLB 改成"导入后**不重打关键帧**、只对 fcurve 时间轴做 30fps 重标定"，
> 让源侧地板误差回到 ~0.3°，这个口径才有绝对意义。见 §8.6。

### 8.4 视觉验收要配合数值复核

视觉模型两轮都自洽，但**它会误读"离地高度差"**：第一轮它说"源腾空、骑砍踩地"，
实测两人「最低脚世界高度」在所有相位都只差 0.01~0.02 m（都在地面）。
因此视觉结论必须用**骨盆高度 / 最低脚高度**这类单义数值复核，不能直接采信。

### 8.6 本轮新增的遗留项（T7 起）

- **T7**：源侧 GLB 改为「导入后不动骨骼、只把 fcurve 时间轴重标定到 30fps」再导出
  （不上面的"重打关键帧"路径），把 §8.3 里那条 ~20° 的地板误差压回 ~0.3°，质量口径才有绝对意义。
  同时这条路径**快得多**（不用逐帧逐骨 `view_layer.update()`：本轮 280 段重打关键帧耗时 ~76 min，
  且必须切成 6 片并行才压到 ~20 min）。
- `web_slim/assets/` 最终只留 **4 个 GLB**：源侧 `ue_mannequin_ground.glb`(181) / `ue_mannequin_src.glb`(99)，
  骑砍侧 `bannerlord_ground.glb`(181) / `bannerlord_src.glb`(99)。分片已删（内容等价，已逐张截图比对过）。
- 两人身高按"骨骼包围盒"各自归一到 1.75 m，但**骨架比例不同** →
  同一姿态下骨盆/肩的绝对高度差几厘米到十几厘米；要做像素级对照需再乘一个统一比例（未做）。
- T4 / T5 / T6 仍未动。

---

## 9. 2026-09-19 追加：用户实测反馈「闪避重定向后穿到地面以下」→ 又抓到一个 2 倍系数的硬 bug

### 9.1 现象与定位

用户点名的片段：`Anim_CS_DODGE_R`（剑/单手 · 闪避位移特技 · src 模式）。
量出来：源骨盆水平位移 4.49 m，骑砍侧 **8.60 m（≈1.92 倍）**，且最低脚跑到 **−0.72 m**（穿到地面以下）。

在 Blender 里直接打印中间量，一眼看到：

```
脚本里用的 unit = base骨盆.y / |源骨盆.y| = 0.020 / 0.011 = 1.90976     ← 拿某个"坐标分量"当比例
按骨架尺度（骨盆→头 长度比）应为          0.95724                        ← 正好差 2 倍
```

`--pelvis src` 的实现是 `目标骨盆 = 目标静止骨盆 + (源骨盆 − 源静止骨盆) × unit`，
**`unit` 错了 2 倍 → 所有 src 段（99 段：跳跃/攀爬/闪避/坠落/倒地）的骨盆位移被整体放大 2 倍**，
于是人冲得更远、竖向过冲、脚穿到地面以下。

### 9.2 修法（三处，都在 `ue_batch_glb.py`）

| # | 问题 | 修法 |
|---|---|---|
| 1 | `unit` 用坐标分量当比例（`base.y / |src.y|`） | 新增 `rig_span()`：用**骨盆静止位 → 头静止位**的世界距离当骨架尺度，`unit = 目标span / 源span`（实测 0.95724） |
| 2 | 个别素材**源数据本身不合理**（`Anim_PR_Up` 源骨盆 1.27 s 内从 0.34 m 升到 **5.77 m**；`Anim_VUp` 升到 2.31 m） | 加护栏：`|竖直位移| > 1.5 m` 即判为源数据异常，该帧**只传水平位移** |
| 3 | 目标"任何骨最低点"仍可能比源低（VUp −1.04 m、R_JU −0.28 m） | 加**单向上限修正**：逐帧比较目标/源的最低点，若目标更低超过 0.05 m 就把骨盆**只往上抬**到对齐（只抬不压，对正常段落无副作用） |

### 9.3 复测数字（全 280 段 · 全骨骼最低点 vs 地面）

| 指标 | 修复前 | 修复后 |
|---|---|---|
| `Anim_CS_DODGE_R` 最低脚（源 −0.135） | **−0.72 m** | **−0.134 m**（与源一致） |
| 源/骑砍「最低点高度差」中位数 | — | **0.006 m** |
| 75 分位 | — | 0.049 m |
| 贯穿地面（<−0.03 m）段数 | 45 | **37** |
| 最差 1~4 名 | VUp −1.04 / PR_Up −0.47 / Dash −0.36 / 4×R_JU −0.28 | VUp、PR_Up、4×R_JU **全部消除** |

### 9.4 仍然存在的残留（新 TODO T8）

剩下 37 段"最低点 < −0.03 m"，但其中**多数是源自己就低**（例：`Anim_Fall_2` 源 −0.446 / 骑砍 −0.322，
骑砍其实比源还高；`Anim_H`、`Anim_Sw`、`111_21`、`Anim_EM_BOW_victory_01`、`085_15` 同理），
**真正"骑砍明显比源低"的只剩 7~8 段**：

| 片段 | 骑砍最低点 | 源最低点 | 差 |
|---|---|---|---|
| `Anim_Dash` | −0.357 | 0.000 | 0.357 |
| `Anim_Death_1` | −0.231 | −0.063 | 0.168 |
| `Anim_KO_FromAir` | −0.203 | 0.000 | 0.203 |
| `Anim_Fwd_KO_L_1` | −0.192 | −0.009 | 0.183 |
| `Anim_CS_DODGE_F` | −0.188 | 0.000 | 0.188 |
| `Anim_BileCh_KO` | −0.137 | 0.000 | 0.137 |
| `075_20` | −0.138 | −0.097 | 0.041（ground 模式） |

**T8**：这 7~8 段是"单向上限修正"没吃到（修正写在关键帧上，可能被帧间插值/其它分量抵掉），
需要逐段查为什么护栏未生效；建议顺带把 `output/glb/ue_flight/bannerlord_flight.glb` 也重烘一遍
（那批 130 段同样用了老的 `unit`，即同样存在 2 倍位移问题）。

### 9.5 度量教训（补充 §8.3）

- 只看"脚骨"会漏掉**躯干/头穿地**：本轮视觉模型说 dodge 躯干埋地、而脚骨量出来是好的，
  补量**全骨骼最低点**才看清真相（结论：躯干没穿地，视觉判断在该格有误；但"只量脚"确实是不完整的口径）。
- **视觉结论与数值冲突时，先怀疑自己的指标是否覆盖不全**（这次就是），再决定是否采信视觉。

