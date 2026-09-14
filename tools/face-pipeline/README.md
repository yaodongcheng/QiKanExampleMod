# face-pipeline —— 骑砍2 自定义头部资产管线

把任意外部模型（FBX/OBJ）的头部，做成骑砍2 里**能跟骨架动、能被拉杆/表情驱动、贴图正确**的脸。

> 🔴 **知识文档在 [`Knowledge/蒂法换头工程.md`](../../Knowledge/蒂法换头工程.md)**：
> **§13.7 自建头部的四条硬规格**（做任何新头先照这四条）、**§19 拓展新脸模的完整管线**、§15~§18 三轮排查史。
>
> 🔗 **本工具链的通用部分（`fbx_probe.py` 关卡 1、`tpaccli` 各命令、硬链接单文件加载、两态 bat、备份纪律）
> 同样服务盔甲工程** —— 见 [`Knowledge/骑砍2盔甲资产工程.md`](../../Knowledge/骑砍2盔甲资产工程.md) §6。
> ⚠️ 反过来：`morphfix` / `skinfix --fullmat` / `transfer_channels.py` / `check_head_space.py` 是**脸部专用**，盔甲不用。

## 归口（按 CLAUDE.md 铁律 26，别在别处开新目录）

| 东西 | 放哪 |
|---|---|
| 本工具链（源码入库、产物整目录忽略） | `tools/face-pipeline/` |
| 脚本 | `tools/face-pipeline/scripts/` |
| tpac 读写 CLI（TpacToolCLI + TpacTool.Lib/IO） | `tools/face-pipeline/tpactool/` |
| 跑出来的离线产物 / dump / 探针输出 | 模块根 `Debug/offline/`（不进库） |
| 权威备份（源 FBX 链 + 编辑器工程） | 模块外，如 `D:\BrainMaker\blend_projects\tifa_export\backup_<日期>\` |

## 当前工作流（一条线走完）

```
源模型（可以是全身 FBX，含身体/衣服/头发都行）
   │  ⓪ 取头：挑件（head/eyeball/eyelash/mouth）→ 同角色多件归并 → 只做偏航定向 → 锚点标定
   │     （缩放 = 原版眼↔嘴竖直距离 0.0728 ÷ 源的同距离；平移 = 眼球中心送到 (0,0.1291,1.6795)）
   │     🔴 整个装配（壳+附件）用同一个变换，别单独动某一件
   │  ① 结构：4 件、顺序 脸→嘴→眼→睫；对象改名 .0/.1/.2/.3 + 材质命名
   │  ② 形变通道：59 条 KeyTime 位移场 → 壳 + 各附件（最近邻 3 点反距离加权）
   │  ③ 帧序：Basis → KeyTime_0(0.1mm 占位) → KeyTime_1…59
   │  ④ 骨架：官方 human_skeleton，刚性绑 bip01_head_13
   ▼  ⑤ 导出：FBX_SCALE_UNITS + axis_forward='Y' + axis_up='Z' + 场景单位=米
头部 FBX ──[关卡 1] fbx_probe.py ──► ModKit 编辑器（人做：导入 → 检查 → Publish）
   │
   │  [后处理] install_pack.py  ← 一条命令跑完下面全部
   │     morphfix（补帧到 101 + 同步 VertexKeyCount）
   │     skinfix --fullmat（四角色材质配方 + MaterialFlags）
   │     [关卡 2] check_head_space.py
   ▼     双端装机 + md5 校验
游戏验证
```

**接脖子（可选，让头能接到骑砍2 身体上）**：

```
head_tifa_a_v10.fbx ──► build_neck.py ──► head_tifa_a_v11.fbx
   ├─ 脖子：源模型 body 自带的脖子（换算到我们空间，z 裁到 1.522）
   ├─ 领口：按【实测的身体领口轮廓】现铺，截面走三次 Hermite（两端取真实表面切向 → 不折棱）
   └─ 权重：从原版 head_female_a 最近点抄 → 头/颈/脊/锁骨 平滑过渡
        │
        └─ [体检] check_seam.py（射线法，与原版对照；同为 0 洞才算同档）
```

**两步是硬性的，缺了必出怪相**：
- **关卡 1**（进编辑器前）：`fbx_probe.py` —— `USF=100` / `UpAxis=2` / 网格节点零变换
- **后处理两道补丁**（Publish 之后）：编辑器产出是"白编译"包（材质刷回默认、帧不全），不打补丁就是"眼球糊脸皮"那类症状

## 脚本清单

### 主链（做新版头只用这些）

| 脚本 | 干什么 |
|---|---|
| **`build_head.py`** | ★ **从零到头部 FBX**：全身/任意源模型 → 挑件 → 归并 4 件 → 偏航定向 → 锚点标定 → 绑官方骨架 → 导出（做新角色第一步就跑它） |
| **`build_neck.py`** | ★ **给头接脖子 + 领口**（`--stage diag`／`uvscan`／`build`）—— 骑砍2 的脖子是**头给的**不是身体给的（身体只有个大 V 领口）；本脚本搬源模型的脖子 + 按实测领口轮廓现铺领口 + 抄原版权重。见 [§20](../../Knowledge/蒂法换头工程.md) |
| **`transfer_channels.py`** | **独立通道移植**：59 条 KeyTime 位移场从源网格搬到目标网格（自带 `--selftest`，实测偏差 0.000000 m） |
| `bind_skeleton_v10.py` | 蒂法这一版的实际产出脚本（结构 + 通道 + 帧序 + 源忠实位置 + 骨架 + 导出）——`build_head.py` 之外的特例修正在这里 |
| `install_pack.py` | ★ **Publish 后一键**：两道补丁 + 关卡 2 + 双端装机 + md5（幂等，可重复跑） |
| `fbx_probe.py` | **关卡 1**：FBX 规格门禁（USF/UpAxis/节点变换/逐 Geometry 包围盒） |
| `check_head_space.py` | **关卡 2**：编译产物落点与参照头对比（退出码可做门禁） |
| **`check_seam.py`** | **接缝体检**：沿领口一圈逐角度逐高度打射线，看「整条射线上有没有正面可渲染的命中」；同时跑原版头+身体做对照，**同为 0 才算同档**。🔴 只看第一击会误判（背面 + 后面有正面 = 没洞） |
| `probe_collar_repro.py` | 复现编辑器里看到的领口现象（纯色/带贴图、正/背/3-4 角度、可开背面剔除） |
| `restore_fbx.py` | 从备份回填 FBX 到 `AssetSources`（ModKit 删网格资产会连带删源 FBX） |
| `bind_skeleton.py` + `bind_skeleton_v5/v6/v7/v8/v9.py` | 历史版本，各解决一层问题（最早的尺度未修正版、v6 空间/朝向、v7 锚点标定、v8 附属件、v9 结构+通道+帧序）—— 排查时对照用 |

### 诊断 / 探针

| 脚本 | 干什么 |
|---|---|
| `probe_mesh_layout.py` | 打印 FBX 的 对象/材质/形状键布局与形状键完整顺序（判断帧号错位的第一步） |
| `probe_source_ref.py` | 从源模型反解"源→我们"变换 + 折算各配件目标 bbox（标定基准的来源） |
| `probe_iris_dir.py` | UV 采样眼球贴图 → 算虹膜朝向（判断"眼睛不对"是朝向还是位置问题） |
| `probe_eye_depth.py` | 量眼球与眼窝开口的深度关系 |
| `dump_fbx.py` / `dump_v3.py` / `dump_fbxparams.py` / `dump_skeleton.py` | FBX 结构/参数/骨架编号基准 |
| `verify_v5.py` / `check_bind.py` | 骨架头骨位置、绑定自检 |

### 早期阶段（"贴图风 / 池投影"路线，保留作参考，当前管线不用）

`warp_*.py` / `bake_pool2.py` / `gen_faces.py` / `gen_uv.py` / `geo_anchor*.py` / `uv_pick*.py` /
`sample_grid.py` / `plot_uv.py` / `make_*.py` / `find_eyes.py` / `anchor_v2.py` / `render_*.py`

### 一次性探针（`_` 前缀，改完即弃，**不进 git**）

`_export_axes.py` / `_export_matrix.py` / `_probe_appendages.py` / `_probe_neck.py` /
`_probe_tifa_body.py` / `_probe_vanilla_head.py`（后者含 Blender FBX 导入器内存补丁范本）

## tpaccli 子命令（`tpactool/TpacToolCLI`，`dotnet build -c Release` 后可用）

| 命令 | 用途 |
|---|---|
| `metaparts` | 重排/裁剪子网格（**零成本验证部件顺序**，不必重导 FBX） |
| `morphinfo` | 网格诊断：逐帧位移签名、未形变帧数、骨骼分布 |
| `morphfix` | 补 morph 帧 + 同步 VertexKeyCount |
| `skinfix --fullmat` | 补蒙皮数据 / MaterialFlags / 四角色材质配方 |
| `meshdiff` | 全字段反射差分（与参照包逐行对比） |
| `dump` / `inspect` / `segs` / `list` / `roundtrip` / `assetclone` / `makepack` / `texreplace` | 导出与打包 |
