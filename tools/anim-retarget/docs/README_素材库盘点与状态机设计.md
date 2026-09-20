# 骑砍2 动画素材库盘点与状态机设计 — 经验总结

> 作者：BrainMaker 天工 2.0 · 日期：2026-09-19 · 项目：`D:\BrainMaker\骑砍2动画重定向`
> 本文档复盘"**6 大动作素材池的盘点、分类、去重与骑砍2 状态机设计**"全流程：做了什么、关键发现、踩过的坑、交付物与下一步。
> 姊妹文档：`README_复盘.md`（UE5→骑砍2 骨骼重定向的技术复盘）。

---

## 0. TL;DR（一句话结论）

**6 个动作素材池共 4812 条可导出动画，已全部盘点、并集分类、来源溯源并去重；并据此设计了面向骑砍2 的 13 个角色动作状态机。**

三个最重要的经验：

1. **"动作包"未必是 FBX** —— 两个 1.2G/1.5G 的压缩包里是 **UE4 工程（.uasset）**，不是 FBX，必须先经 UE 批量导出。
2. **uasset 内部保存原始导入路径** —— 扫描二进制里的 `RelativeFilename` 可**精确溯源**，"是否重复"从此不用靠文件名猜。
3. **骑砍2 需要的是"补缺"而非"替换"** —— 原生已有移动/奔跑/骑乘，真正缺的是**冲刺、各武器战斗、弹反处决、飞行、法术**。

---

## 1. 背景与目标

原始目标：把手上零散的动作资源整理成**能真正用于骑砍2 mod** 的一套动作，并设计角色动作状态机。

素材来源分散在 3 个位置、共 6 个池子：

| # | 素材池 | 位置 | 可导出动画 |
|---|---|---|---|
| 1 | 1-CMU 卡耐基动捕 | `D:\BrainMaker\骑砍2动画重定向\UEAnims\` | 2548 |
| 2 | 2-3583 官方动作集 | 同上 | 1035 |
| 3 | mySekiro AnimStarterPack | `D:\UEProjects\mySekiro\` | 134 |
| 4 | FCS FlexibleCombatSystem | `D:\UEProjects\【UE5】FlexibleCombatSystem\` | 275 |
| 5 | GhostSamurai_katana | `D:\BrainMaker\骑砍2动画重定向\UEAnims\` | 683（Mannequin 版） |
| 6 | SuperheroFlightAnimations | 同上 | 137 |
| | **合计** | | **4812** |

---

## 2. 做了什么（事实清单）

### 2.1 素材盘点与分类

- 逐池扫描 uasset，区分 **AnimSequence（可导出）** / AnimMontage / BlendSpace / AimOffset / Blueprint
- CMU 部分用**卡耐基官方索引表**（`cmu-mocap-index-text.txt`）做语义映射，覆盖率 **95.5%**（2435/2548）
- 建立**面向骑砍2 用途**的分类体系：12 个动作域（D1 基础移动 … D12 法术施法）

### 2.2 去重与溯源

- 扫描 uasset 二进制中的 `RelativeFilename` 字段，还原**每一个动画的原始导入路径**
- 据此判定重复来源，而不是靠文件名相似度

### 2.3 状态机设计

- 设计 **13 个状态机**（M1 移动 … M13 法术），含 **119 条状态-动作映射**、**31 条状态转移**
- 给出**分阶段落地路线**（阶段0 最小验证 → 阶段5 完整）

---

## 3. 关键经验（重点）

### 3.1 "动作包"未必是 FBX —— 先验证文件真身

两个 zip 各 1.2G / 1.5G，用户以为是 FBX 合集，实际是**完整 UE4 工程**：

```
1-CMU 2500+动捕动作包.zip   →  MocapCMU_InPlaceV2/（UE 工程，2623 个 .uasset）
2-3583动捕动作包.zip        →  3583.uproject（UE 工程，3758 个 .uasset）
```

**教训**：拿到"动作资源"第一件事是**看扩展名分布**，不要默认它是 FBX。UE 工程要走"打开工程 → 批量导出 FBX"路径。

### 3.2 uasset 内部保存原始导入路径 —— 溯源利器

`.uasset` 二进制里保留了 FBX 导入时的记录：

```json
[{ "RelativeFilename" : "H:/Grruzam_Animation_Assets_UR4_v4.10.4/
   UR4_Assets_Parts/Katana_Nomesh/Katana_Blade@Attack_3Combo_1.FBX" }]
```

**用这个方法解决的问题**：

| 问题 | 结论 |
|---|---|
| mySekiro 的刀剑动画和 GhostSamurai 重复吗？ | **不重复**（来源分别是 Grruzam UR4 包 / GhostSamurai 独立包，零交叉引用） |
| mySekiro 内部有哪些重复？ | **23 对**（`XXX_Retargeted` 与 `UE4_XXX` 内容完全相同，仅差 28 字节 = 名字长度） |
| mySekiro 有哪些和 3583 包重复？ | **12 条** `Anim_CS_*`，来源同为 `FILMSTORM/AdventureAnimset` |
| mySekiro 到底混了几个来源？ | **7 个**（CMU/3583、CLazy 跑酷、Grruzam 刀剑、TPS 教程、官方 ASP、AG PROJECT） |

**方法论**：判断"是否重复"不要看名字，**看来源链 + 文件大小**。

### 3.3 CMU 是科研动捕，重复率极高

| 维度 | 数据 |
|---|---|
| CMU 2548 条 → 唯一描述 | 仅 **994 种**（平均重复 2.6 次） |
| 行走 681 条里 | 95 条同名 `walk`、37 条 `walk on uneven terrain`、20 条 `walk sideways and turn` |

**原因**：科研动捕同一个动作换了受试者/速度/场景就重采一次。
**对策**：CMU 只做**少量精选**，主力用专业游戏动作库（FCS/GhostSamurai/3583包）。

### 3.4 "可以合并"要分两种含义

对 3583 包扫描后：

| 类型 | 数量 | 能否合并 |
|---|---|---|
| **Start/Loop/End 三段式**（如 `Anim_R_Start_2` + `_Loop_2` + `_End_2`） | **仅 6~7 组** | ✅ 可拼成完整动作 |
| 同类多变体（`_01`~`_12`、`W_180_L/R`、`W_90_L/R`） | 131 组 / 2663 条 | ❌ 不能拼，只能归类/精选 |

**教训**：用户直觉说"应该有不少可以合并"，实测真正能拼接的只有 6~7 组；大部分"重复"是**同类多样本**，只能精简不能拼接。

### 3.5 各素材池骨架统一 —— 重定向链路可复用

```
FCS        → UE4_Mannequin_Skeleton（UE 4.27）
mySekiro   → UE4ASP / UE4 Mannequin
GhostSamurai → UE4_Mannequin_Skeleton（UE 4.21）
SuperheroFlight → UE4_Mannequin_Skeleton
3583包 / CMU → UE Mannequin
```

**好消息**：骨名映射表与重定向脚本可直接复用——正式产线脚本为 `pipeline/rigs/ue_mannequin/retarget.py`（旧版 `ue5_to_bannerlord_retarget_world.py` 已归档至 `_legacy/scripts_2026-09-09/`）。
**注意**：FCS 是 UE 4.27 工程，导出脚本 `ue_export_fbx.py`（针对 UE5.0.3）的 API 需小适配。

### 3.6 FCS 的武器分类天然对齐骑砍2 ActionSet

FCS 每个武器系统都配了 **Combat / Locomotion / Strafe / Brake** 四套，这正是骑砍2 ActionSet 的组织方式。

```
Duelist-Right(单手) / TwoHanded(双手) / MainAndShield(剑盾) / OffhandShield(副手盾)
Dual-Wield(双持) / Fists(徒手) / Bow(弓) / Magic(法术) / Assassinations(暗杀)
```

### 3.7 偏斜/处决是"成对动画"—— 骑砍2 原生不支持

GhostSamurai 的偏斜机制命名极其规范：

```
GhostSamurai_LAttack_DeflectL90   → 敌方左手攻击来袭，左偏斜绕背 90°
GhostSamurai_LAttack_DeflectL180  → 绕背 180°（到正后方）
GhostSamurai_*_DeflectL_CounterExecution  → 偏斜成功后反击处决（我方视角）
GhostSamurai_*_DeflectL_CounterExecuted   → 被偏斜处决（敌方视角）
GhostSamurai_*_LFail / RFail / RFail180   → 偏斜失败（硬直，会被反杀）
```

**关键坑**：`CounterExecution` / `CounterExecuted` 是**一对同步动画**，需要双角色位置对齐 + 动画同步，**骑砍2 原生只有单角色动画通道**。这块工作量远大于"单纯导动画"，建议先做单人验证。

### 3.8 骑砍2 的需求是"补缺"不是"替换"

| 骑砍2 已有 | 骑砍2 缺失（本次要补） |
|---|---|
| 移动、奔跑、骑乘、基础战斗 | **冲刺 Sprint** |
| | **各武器战斗动画**（单手/双手/剑盾/双持/武士刀） |
| | **弹反偏斜 + 处决** |
| | **飞行/悬浮** |
| | **法术** |
| | 火器射击 |

---

## 4. 踩过的坑

- **坑1 — 误以为动作包是 FBX**：应先看扩展名分布再定策略。
- **坑2 — 文件名相似 ≠ 重复**：别用名字猜，要用 uasset 内部来源链。
- **坑3 — CMU 语义映射要补前导零**：索引表是 `02_07`，资产名是 `002_07`，不归一化会大量匹配失败。
- **坑4 — 脚本分类的"前缀坑"**：资产名带 `Anim_` 前缀，正则写 `^TA` 匹配不到 `Anim_TA_N_01`，必须**先去前缀再匹配**（本轮踩了两次）。
- **坑5 — Windows 下 heredoc 写长脚本会被截断**：单次内容过长导致 `here-document delimited by end-of-file`，改为**分段写入 + 追加**。
- **坑6 — 反斜杠转义**：`replace('\','/')` 在 heredoc 里容易出错，改用 `chr(92)` 规避。
- **坑7 — 先验证链路再放量**：全量导出 3583 条要几小时，必须先小批量验证（早期 `UEAnims/exported_fbx/` 里的 29 条样本就是这么来的；该目录已在目录重组中归档）。

---

## 5. 交付物清单

全部位于 `D:\BrainMaker\骑砍2动画重定向\input\inventory\`
（原为 `UEAnims/anim_inventory/`，2026-09-19 目录重组为规范产线后移入 `input/inventory/`）：

### 汇总与设计（主线交付）
| 文件 | 内容 |
|---|---|
| `动作库并集分类总表.xlsx` | 12 动作域明细 4812 条 / 域×池矩阵 / 汇总统计 |
| `骑砍2角色动作状态机设计.xlsx` | 13 状态机 / **119 条状态-动作映射** / 31 条转移 / 7 阶段路线 |

### 各素材池盘点
| 文件 | 内容 |
|---|---|
| `FCS战斗动画盘点.xlsx` | 422 资产（275 AnimSequence），按武器系统 |
| `mySekiro射击动画盘点.xlsx` | 193 资产（134 AnimSequence） |
| `mySekiro素材溯源.xlsx` | **来源溯源 + 重复清单（A类23对 / B类12条）** |
| `GhostSamurai_SuperheroFlight盘点.xlsx` | 武士刀 683 + 飞行 137 |
| `骑砍2动作资产分类总表.xlsx` / `骑砍2动作精简清单.xlsx` | CMU+3583 包的分类与精简（P0 132 / P1 280） |

### 数据（CSV，供脚本读取）
`动作库并集分类.csv`、`状态动作映射.csv`、`状态转移表.csv`、`状态机总览.csv`、`MVP应用建议.csv`、各池盘点 CSV

---

## 6. 下一步

1. **按阶段生成导出清单**：建议从「阶段0+阶段1」（约 75 条：冲刺 + 受击 + 单手战斗）开始，产出可直接执行的 FBX 导出任务。
2. **改造导出脚本**：支持跨工程（UE4.27 / UE5.0）批量导出 + 按用途分目录归档。
3. **重定向接入**：复用 `pipeline/rigs/ue_mannequin/retarget.py`（旧版 `ue5_to_bannerlord_retarget_world.py` 已归档），并处理 FCS 的 UE4.27 API 适配。

> 📌 **2026-09-19 目录重组提示**：项目已整理为规范产线（`docs/` 知识文档 · `input/` 素材与盘点 · `pipeline/` 脚本 · `output/` 产物 · `viewer/` 查看器 · `_legacy/` 归档）。本文件所在的盘点产物现位于 `input/inventory/`；项目入口请读根目录 `README.md`。本文其余章节的路径已按新结构校正。
4. **偏斜/处决的双角色同步**：单独评估（骑砍2 原生不支持，需要额外机制）。
5. **待确认**：GhostSamurai 用 Mannequin 版还是原角色版；SuperheroFlight 用哪一套飞行风格（A~E）。

---

## 附：本文档的方法论提炼

| 原则 | 落地方式 |
|---|---|
| 先验证文件真身 | 看扩展名分布，不预设格式 |
| 数据说话，不靠猜 | uasset 内部 `RelativeFilename` 溯源 |
| 先立分类框架再填数据 | 面向用途的 12 动作域，而不是按来源分 |
| 先小批量验证再放量 | 29 条样本 → 全量导出 |
| 区分"能拼接"与"能归类" | Start/Loop/End 拼接 vs 同类变体精选 |
| 面向目标做设计 | 骑砍2 缺什么补什么，而不是把 4812 条全塞进去 |
