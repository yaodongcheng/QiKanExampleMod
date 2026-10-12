---
name: bannerlord-anim-retarget
description: 骑砍2 动画重定向产线的入口与出厂闸门。凡涉及"把外部动作变成骑砍2 动画"——UE5 小白人 / 战国无双2 / 3ds Max Biped 重定向、图片或视频提关键点转骨骼动画、TRF 导出与 ModKit 导入填值、双人动作（处决 / 伏击 / 点穴 / 拥抱 / 扛人 / 被绑）的站位与挂接、重定向后抖动或穿地排查、飞行 / 施法 / 钩索的自制动作合成——都从本技能起步。它只做三件事：给选路、给"跑完必须过"的闸门、指到工程总纲的对应章节。不要凭记忆猜 TRF 语义或 ModKit 填值。
---

# 骑砍2 动画重定向 — 入口

> **本技能是门铃，不是仓库。** 全部知识只有一份：`tools/anim-retarget/项目总纲.md`（2364 行，唯一事实来源）。
> 🔴 **本文件禁止抄总纲正文** —— 只放"去哪读第几节"。抄了就是两处维护，必然分叉。

## 一句话

外部动画（**5 种源**：UE5 小白人 / 战国无双2 / 3ds Max Biped / 图片视频关键点 / 库内检索合成）
→ 统一产线 → **`.trf`（★ 最终交付物）+ ModKit 规格 FBX** → ModKit 导入 → 进游戏。

🔴 **交付物是 `.trf`，不是 `.fbx`。FBX 只是中间载体与预览来源。跑完没出 TRF = 未完成。**

## 何时用

- 要把一段外部动作变成骑砍2 能播的动画（重定向 / 关键点解算 / 剪辑合成）。
- 双人动作：处决、伏击、点穴、拥抱、**扛人 / 被绑**，以及骨骼挂接（socket）。
- 重定向件"看着不对"：抖动、穿地、浮空、朝向反、人趴地、慢半拍。
- 自制动画要进游戏：TRF → clip 元数据 → 接线 → 装机。

**何时不用**：只是想**看 / 解析** UE 工程里的东西（蓝图结构、数据表、动画通知时间、粒子参数）
→ 走 `tools/ue-dissect/`（导 T3D 文本，秒级）。静态网格 / 材质 / 贴图 → 走 `wheels.d/assets.md`。

## 一、先选路（决定后面所有步骤）

| 来源 | 走法 | 细节 |
|---|---|---|
| UE5 小白人 / SW2 / Biped（**已有骨架的动画**） | `run_retarget.py --rig <ue_mannequin\|sw2_gunner\|biped>` | 总纲 §1 / §0.14 |
| 图片 / 视频（**没有骨架，要提关键点**） | `vid2pose.py` → `--rig pose_mediapipe` | 总纲 **§0.8**（4 步 + 2 闸门） |
| 库内已有动作**拼 / 叠 / 借** | `scan_pose_metrics.py`（找）→ `trf_edit.py`（切）→ `trf_compose.py`（叠） | 总纲 **§0.9**（三条路线决策树） |

🔴 **开工前先读 §0.9**（十维度对比 + 决策树）。选错路 = 白做一轮。

## 二、跑完必须过的闸门（**不过不算做完**）

> 工作目录必须是数据根 `D:\BrainMaker\骑砍2动画重定向`（`pipeline/` 是 junction；脚本靠"向上找同时含
> `pipeline/` 与 `input/` 的目录"定位根，在仓库里跑会解析到错的根）。

| # | 命令 | 拦什么 |
|---|---|---|
| 1 | `python pipeline/common/check_trf_pos.py <TRF目录>` | **整批**扫「位置轨首帧 ≈0」——单文件看不出的混入坏件（绝对语义 ⇒ 实机人悬空 1m） |
| 2 | `python pipeline/common/check_fbx_bindpose.py` | FBX 绑定姿势被烘成躺姿（根因在 FBX，改 TRF 治不好） |
| 3 | `python pipeline/common/check_glb_feet.py` | 离线复算脚底高度（判"预览里看着好"是真是假） |
| 4 | `python pipeline/common/verify_modkit_fbx.py` | 28 骨 / 根名 / 叶骨 / 尺寸量级（进 ModKit 前的骨架体检） |
| 5 | `python pipeline/common/trf_root_travel.py --trf <x.trf>` → `python pipeline/common/make_import_sheet.py` | 带位移的动作：算出该填的 `displacement` / `endProgress` + 整批填值表 |
| **6** | **双人动作另加**：`pipeline/tools/check_trf_axis.py` + **总纲 §0.14 §十三 的四步初始位置校验** | 左右摆反 / 朝向偏 90°（🔴 判据必须过「分辨力检查」，别拿不动脑子的数下结论） |
| 7 | `python pipeline/common/check_doc_links.py`（改过文档时） | 文档引用失效（交接前必跑，0 失效才算过） |

## 三、交付链（TRF → 游戏）

1. **ModKit 导入**：`Source 1 / Source 2` 填该 TRF 的帧范围（导出 1–41 就填 1 / 41），`Duration > 0`；
   报 `pos ipo[2,42] does not fit sources 0.00 and 0.00` = 这个没填，**不是 TRF 写错**。
2. **clip 元数据整组抄同类原版**（`Continue to action` / `Param 2` / `Priority` / `Blend in period`）——
   **能播 ≠ 流程认**；装填类是两段式，链断 = 弹药永不进膛。
3. **Publish**（目标选**模块外**）→ 产出 `Modules\Publish\<沙箱>\AssetPackages\`。
4. 🔴 **Publish ≠ 进游戏**：要**改名**拷成 `Modules\LivingWorldNpcs\AssetPackages\lwn_core.tpac` 才算装机。
5. 🔴 **复核要对着【装机包】读**，不是对着发布产物：`tpaccli animpos` / `animrot` / `animlist`（查 ORPHAN）。

## 四、要去哪查（章节索引 —— 别在这里找答案，去总纲）

| 要什么 | 去哪 |
|---|---|
| **TRF 的 XYZ 到底是什么**（+Y 脸朝向 / +X 右手 / +Z 上 / 原点脚底） | 总纲 **§0.14 §十一**（唯一权威口径） |
| **双人动作交付闸门**（左右怎么定、尺子怎么量） | 总纲 **§0.14 §十三** |
| 重定向 + 查看器验收 SOP | 总纲 **§0.15** |
| **37 条硬约束**（踩过的坑，逐条对号） | 总纲 **§6** |
| TRF 文件格式与两条轨道的语义差异（旋转=绝对 / 平移=纯增量） | 总纲 **§12** + `docs/TRF规范.md` |
| 出厂验收与 ModKit 填值 | 总纲 **§11** |
| 抖动 / 抽搐的三个来源与三层修法 | 总纲 **§0.11** |
| 生成或剪辑动作片段的完整性（收手回 idle、剪掉片尾静止） | 总纲 **§0.10** |
| 姿态过渡桥（跪 ⇄ 倒地 ⇄ 站起） | 总纲 **§0.16** |
| 目录结构 / 脚本分层 / 查看器 | 总纲 **§2 / §3 / §4** |
| 报错 → 原因 → 修法 速查 | `docs/README_骨骼经验.md` §6 |
| 引擎侧（clip 元数据 / action_set / movement_set / 通道分层） | `plans/rules/wheels.d/assets.md` |

## 五、纪律

- 🔴 **知识只写一处**：本技能只导航；发现新知识 → 写进**总纲**对应章节，**不要写进本文件**。
- 🔴 **总纲 §6 硬约束是"违反就出事"的清单**，换骨架 / 换素材时逐条对号。
- 🔴 **不可再生资产动手前先备份**（`input/source|target` 的 FBX、查看器 assets 的 GLB），
  一次性脚本先核对 `argv` 索引（§10 事故：脚本把输入当输出路径写回，覆盖了唯一副本）。
- **环境**：Blender 用全路径 `C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`（不在 PATH）；
  查看器**只能**用 `viewer\启动查看器.bat`（`python -m http.server` 不带 `no-store`，会看到旧版还存不了盘）；
  Git Bash 跑 UE 导出要加 `MSYS_NO_PATHCONV=1`。
