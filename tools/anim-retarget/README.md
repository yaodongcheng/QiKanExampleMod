# tools/anim-retarget — 骑砍2 动画重定向工具链

> **一句话**：把外部动画（UE5 小白人 / 战国无双2 铁炮兵）重定向到骑砍2 骨架，
> 一次产出 ModKit 规格 FBX + **`.trf`（最终交付物，ModKit 要导的就是它）**。
>
> 最后更新：2026-09-20 ｜ 入库来源：`D:\BrainMaker\骑砍2动画重定向\`

---

## 一、🔴 数据在哪（先读这条，否则跑不起来）

**本仓库里只有代码和文档。数据面（源动画 / 产物 / 查看器 / 原始素材包）全部在 D 盘：**

```
D:\BrainMaker\骑砍2动画重定向\
├─ pipeline   ->  junction，指向本目录的 pipeline/   ← 同一份文件，改哪边都是改同一份
├─ docs       ->  junction，指向本目录的 docs/
├─ viewer     ->  junction，指向本目录的 viewer/
├─ README.md  ->  符号链接，指向本目录的 项目总纲.md  （junction 只支持目录，文件跨盘符要用符号链接）
├─ UEAnims\       8.0 GB  六个原始素材包（CMU / 3583 / GhostSamurai / SuperheroFlight …）—— 不可再生
├─ input\         2.2 GB  源 FBX + 盘点表 + 骑砍目标骨架
├─ output\        238 MB  产物：fbx/ trf/ glb/ verify/
└─ _legacy\       250 MB  历史归档
```

**跑管线必须从 D 盘那边跑**——脚本靠「向上找同时含 `pipeline/` 与 `input/` 的目录」定位项目根，
在仓库里跑会解析到错的根。`run_retarget.py` 已加防呆，会直接报错并告诉你正确路径。

> 为什么用 junction 而不是复制两份：**避免分叉**。junction 是「一个文件两个路径」，
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

**跑完必须看两行自检**（脚本自己会打印）：

| 打印 | 合格线 | 不合格说明 |
|---|---|---|
| `CHECK_OK` | 与 Blender pose 矩阵偏差 < 0.5° | 绝对公式写错了 |
| `CHECK_POS` | 位置轨首帧 **≈ 0** | 量级是米（~0.86）⇒ 误用了绝对语义，**产物作废** |

然后 ModKit 导入：`Source 1 / Source 2` 填该 TRF 的帧范围（导出 1–41 就填 1 / 41），`Duration > 0`。
报 `pos ipo[2,42] does not fit sources 0.00 and 0.00` 就是这个没填，不是 TRF 写错。

---

## 三、🔴 两条轨道的语义不一样 —— 这是本工具链最容易写反的地方

**引擎读 TRF 时：旋转按「绝对局部变换」读，平移按「相对静止姿势的增量」读。**

```python
# pipeline/common/fbx_to_trf.py 的 sample() 里那两行
q   = rest.to_quaternion() @ pb.rotation_quaternion        # 旋转：绝对
loc = rest.to_3x3() @ pb.location                          # 平移：纯增量，不叠加 rest.translation
```

**写反的两种方式，都实机踩过：**

| 写反的方式 | 实机后果 |
|---|---|
| 旋转写增量（直接用 `pb.rotation_quaternion`） | 每根骨被摆到「零旋转」→ **人趴在地上、四肢乱折** |
| 平移写绝对（`rest.translation + rest_rot @ loc`） | **人整体被抬高约 6cm** |

> 2026-09-20 事故：本管线的 `fbx_to_trf.py` 曾把平移写成绝对（回归了 `tools/OpenTrf` 那份
> 已经修好的 bug）。已合并修正，全工程现在只有这一个 TRF 导出器。
> 详情记在 `plans/rules/wheels.d/assets.md` §15.2 / §15.3。

---

## 四、目录结构

```
tools/anim-retarget/
├─ README.md                      本文件（工具怎么跑）
├─ 项目总纲.md                     ★ 整个工程的总纲：27 条硬约束、事故记录、算法结论 —— 动手前先读
├─ pipeline/                      ★ 代码真身
│   ├─ common/                    【通用层】与「哪套骨架」无关
│   │   ├─ fbx_to_trf.py          ★ TRF 导出器（唯一的，引擎语义见上一节）
│   │   ├─ verify_modkit_fbx.py   ★ 骨架体检（骨数/根名/叶骨/mesh/尺寸 + 自动算 ModKit 填值）
│   │   ├─ fbx_to_glb.py          FBX → GLB 批量烘焙（查看器用）
│   │   ├─ export_ue_fbx*.py      从 UE 工程批量导 FBX
│   │   ├─ check_*.py             质量校验 / 源忠实度 / 文档引用自检
│   │   └─ shot_headless.py       无头截图验收
│   ├─ rigs/                      【骨架专有层】换骨架只加一个目录
│   │   ├─ bannerlord_target.json 骑砍 28 骨序 / 根名 / 轴向 / 单位（目标骨架唯一事实来源）
│   │   ├─ ue_mannequin/          UE5 小白人 → 骑砍2（map.json 22 标准骨 + 扭骨规则）
│   │   └─ sw2_gunner/            战国无双2 铁炮兵 → 骑砍2（源为 glTF）
│   └─ run_retarget.py            ★ 统一入口（调度层，不含算法）
├─ docs/                          知识文档真身（9 篇）
└─ viewer/                        通用查看器真身
    ├─ viewer.html                页面（成对调参台 / ?probe=1 / 调试覆盖层，零硬编码文件名）
    ├─ serve.py                   本地服务（带 no-store + POST /api/save-pairs）
    ├─ 启动查看器.bat              一键起（自动找 Python / 挑端口 / 开浏览器）
    ├─ lib/                       three.js（本地化）+ twist_map.json
    └─ datasets/                  每集一个目录：dataset.json（+ 可选 manifest.json）
        └─ <集>/assets/           该集的 GLB —— 🔴 不进 git（见下）
```

**新增一个源骨架 = 只加一个 `pipeline/rigs/<名>/`（`map.json` + `retarget.py`）+ 在
`run_retarget.py` 的 `RIGS` 注册表加一条。通用层不动。**

### 什么进 git、什么不进

| | 内容 | 为什么 |
|---|---|---|
| ✅ 进 | `项目总纲.md`、`README.md`、`pipeline/**`、`docs/**`（除教学视频）、`viewer/{viewer.html,serve.py,*.bat,lib/}`、`viewer/datasets/**/*.json` | 代码 + 文档 + 数据集元数据。**🔴 成对动画那 11 组手调站位参数就在 `viewer/datasets/ue_exec_pair/dataset.json` 里**，它是纯手工产出、重建不了，是全仓库最该保的东西之一 |
| ❌ 不进 | `viewer/datasets/*/assets/`（137MB GLB） | 烘焙产物，可由 `input/source/*.fbx` 重生成 |
| ❌ 不进 | `viewer/datasets/*/_backup/` | `serve.py` 保存时自动滚动的备份。**`dataset.json` 进 git 后，版本历史取代了它的作用** |
| ❌ 不进 | `docs/教程存档/*.mp4`（24MB） | 教学视频，不算源 |
| ❌ 不进 | `input/` `output/` `UEAnims/` `_legacy/` | 数据面，都在 D 盘；规则是兜底用的 |

---

## 五、交付物现在有一笔待办

🔴 **`output/trf/` 下的 196 个 TRF 是「平移写绝对」那版产出的，尚未按修正后的语义重生成。**

| 分组 | 数量 | 重跑成本 |
|---|---|---|
| 位置轨是常量（飞行 130 + ue_flight 3 …） | 133 | 低（位置块本就可以直接归零） |
| 位置轨真有位移 | 63 | 必须过 Blender |

**下次导入 ModKit 之前记得重生成。** 一条命令一条，见 `run_retarget.py --help`。

---

## 六、相关的别处

| 要什么 | 去哪 |
|---|---|
| **27 条硬约束 / 事故记录 / 算法结论** | [项目总纲.md](项目总纲.md)（= `D:\BrainMaker\骑砍2动画重定向\README.md`，同一份文件） |
| 骨骼映射与报错速查 | `docs/README_骨骼经验.md` |
| TRF 格式与语义 | `docs/TRF规范.md` |
| 查看器（看动画/调成对站位） | `D:\BrainMaker\骑砍2动画重定向\viewer\启动查看器.bat`，或本仓库的 `tools/anim-retarget/viewer/启动查看器.bat`——两边同一份。**别用 `python -m http.server`**，它不带 `no-store` 也没有保存接口 |
| `.trf` **网格**（非动画）导入导出 | `tools/OpenTrf/`（地形/地图域，与动画无关） |
| 动画导入的引擎侧知识 | `Knowledge/动画导入与UE5重定向_引擎能力与实现路径.md` |
| 怎么把 TRF 导进游戏 | `plans/自定义移动管线.md` · `Knowledge/骨骼动画TRF格式与增量陷阱.md` |
