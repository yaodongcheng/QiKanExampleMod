# tools/anim-retarget — 骑砍2 动画重定向工具链

> **一句话**：把外部动画（UE5 小白人 / 战国无双2 / 图片视频关键点）重定向到骑砍2 骨架，
> 一次产出 ModKit 规格 FBX + **`.trf`（最终交付物，ModKit 要导的就是它）**。
>
> 🔴 **本文件只写【不会变】的东西：怎么跑、去哪查。** 会变的内容（坑 / 待办 / 数据集清单 / 算法结论）
> **只在 [项目总纲.md](项目总纲.md) 一份** —— 别在这里抄第二遍：抄了必然烂
> （2026-09-28 这里就烂过一处：「交付物待办」还写着"196 条全待重生成"，而总纲早就记着那批已修完）。
>
> 最后更新：2026-09-28 ｜ 入库来源：`D:\BrainMaker\骑砍2动画重定向\`

---

## 零、什么时候**不要**用这条线

本线只产**动画资产**（FBX + TRF）。如果你只是想**看 / 解析** UE 工程里的东西 ——
蓝图结构、数据表数值、**动画通知的触发时间**、粒子参数 —— 走 `tools/ue-dissect/`
（全仓唯一的 UE 资产导出/解析底座，导出 T3D 文本，秒级无损）。两条线互补，别互相替代。

---

## 一、数据在哪（先读这条，否则跑不起来）

**本仓库里只有代码和文档。数据面（源动画 / 产物 / 查看器素材 / 原始素材包）全部在 D 盘：**

```
D:\BrainMaker\骑砍2动画重定向\
├─ pipeline / docs / viewer   ->  junction，指向本目录（同一份文件，改哪边都是改同一份）
├─ README.md                  ->  符号链接，指向本目录的 项目总纲.md
├─ UEAnims\   8.0 GB   六个原始素材包（CMU / 3583 / GhostSamurai / SuperheroFlight …）—— 不可再生
├─ input\     2.2 GB   源 FBX + 盘点表 + 骑砍目标骨架
├─ output\    238 MB   产物：fbx/ trf/ glb/ verify/
└─ _legacy\   250 MB   历史归档
```

**跑管线必须从 D 盘那边跑** —— 脚本靠「向上找同时含 `pipeline/` 与 `input/` 的目录」定位项目根，
在仓库里跑会解析到错的根（`run_retarget.py` 已加防呆，会直接报错并告诉你正确路径）。

> 为什么用 junction 而不是复制两份：**避免分叉**。junction = 一个文件两个路径，
> 在本仓库改脚本，D 盘那边立刻就是新的，不存在"改哪份"的问题。

---

## 二、怎么跑

```bash
# 从 D 盘的数据根执行（那边是 junction，跑的就是本目录这份代码）
cd /d D:\BrainMaker\骑砍2动画重定向

python pipeline/run_retarget.py --rig ue_mannequin --clip 010_01 --name ue_010_01
python pipeline/run_retarget.py --rig sw2_gunner   --clip p006   --name sw2_gunner_p006_alig
python pipeline/run_retarget.py --rig sw2_gunner   --clip p006   --dry-run     # 只看命令不执行
```

产物：`output/fbx/<name>.fbx`（中间）+ `output/trf/<name>.trf`（★ 交付物）。

**跑完必须看两行自检**（脚本自己会打印；判据与坏件修法见总纲 §11，
整目录闸门 = `python pipeline/common/check_trf_pos.py <TRF目录或文件…>`）：

| 打印 | 合格线 | 不合格说明 |
|---|---|---|
| `CHECK_OK` | 与 Blender pose 矩阵偏差 < 0.5° | 绝对公式写错了 |
| `CHECK_POS` | 位置轨首帧 **≈ 0** | 量级是米（~0.86）⇒ 误用了绝对语义，**产物作废** |

**ModKit 导入**：`Source 1 / Source 2` 填该 TRF 的帧范围（导出 1–41 就填 1 / 41），`Duration > 0`。
报 `pos ipo[2,42] does not fit sources 0.00 and 0.00` 就是这个没填，不是 TRF 写错。
带位移的还要填 `displacement` / `endProgress` —— `python pipeline/common/make_import_sheet.py`
一键出整批填值表（口径与实机验证见总纲 §11.4）。

> 全部选项（`--pelvis` 三档 / `--animdir` / `--obj_rot`）与飞行、带位移动作的特例：总纲 §1 与 §9。

---

## 三、什么进 git

| | 内容 | 为什么 |
|---|---|---|
| ✅ 进 | `README.md`、`项目总纲.md`、`pipeline/**`、`docs/**`、`viewer/{viewer.html,serve.py,*.bat,lib/}`、`viewer/datasets/**/*.json` | 代码 + 文档 + 数据集元数据 |
| ❌ 不进 | `viewer/datasets/*/assets/`（137MB GLB）· `viewer/datasets/*/_backup/` · `docs/教程存档/*.mp4` · `input/` `output/` `UEAnims/` `_legacy/` | 烘焙产物 / 滚动备份 / 教学视频 / 数据面（都在 D 盘） |

> 🔴 **成对动画那 11 组手调站位参数**在 `viewer/datasets/ue_exec_pair/dataset.json` 里 ——
> 纯手工产出、**重建不了**，是全仓库最该保的东西之一。动它之前先 `git diff` 看一眼。

---

## 四、要去哪查

| 要什么 | 去哪 |
|---|---|
| **工程总纲（唯一事实来源）**：选路 / 坑 / 交接 / 待办 / 算法结论 | [项目总纲.md](项目总纲.md)（= `D:\BrainMaker\骑砍2动画重定向\README.md`，同一份文件） |
| **TRF 两条轨道的语义**（旋转 = 绝对 / 平移 = 纯增量，写反的后果与判据） | 总纲 §12.2 + 硬约束 5 |
| 骨骼映射、ModKit 报错速查 · TRF 文件格式 | `docs/README_骨骼经验.md` · `docs/TRF规范.md` |
| 查看器（看动画 / 调成对站位） | `D:\BrainMaker\骑砍2动画重定向\viewer\启动查看器.bat`（或本目录同路径）。**别用 `python -m http.server`**，它不带 `no-store` 也没有保存接口 |
| `.trf` **网格**（非动画）导入导出 | `tools/OpenTrf/`（地形/地图域，与动画无关） |
| 动画导入的引擎侧知识 · 怎么把 TRF 导进游戏 | `Knowledge/动画导入与UE5重定向_引擎能力与实现路径.md` · `Knowledge/骨骼动画TRF格式与增量陷阱.md` · `plans/自定义移动管线.md` |
