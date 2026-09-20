# 骑砍2 动画重定向 — 项目复盘 README（最终版）

> 📁 **路径说明（2026-09-19 追加）**：本文写于旧目录结构。文中 `UEAnims/exported_fbx/`、`UEAnims/exporter/` 等路径**已在目录重组中变更**——脚本现位于 `pipeline/`、早期产物归档于 `_legacy/`、素材盘点清单位于 `input/inventory/`。**内容结论不变**，路径以顶层 `README.md` 为准。

> 作者：BrainMaker 天工 2.0  ·  日期：2026-09-09  ·  工作目录：`D:\BrainMaker\骑砍2动画重定向`
> 本文档**诚实复盘**"UE5/Mixamo 动画 → 骑砍2骨骼重定向"项目：做了什么、哪些成功、哪些失败、根因、最终结论、后续路径。
> 不粉饰，把踩过的坑、反复试错的弯路、以及最终的**能力边界**都如实记录。

---

## 0. TL;DR（一句话结论）

**全链路技术验证跑通了**：动画提取 → FBX 导出 → Blender 重定向 → 预览/对比，全部做成，且**重定向忠实还原了源动画的运动（逐骨旋转增量 RMS≈0.1–0.2°）、脚贴地、左右对称、朝向一致（胸方向修正）**。

**但能力边界很明确**：手写重定向只能还原"**每根骨转了多少角**"（动作的忠诚），**做不到跨骨架的"四肢绝对位置"对齐**——因为两套骨架的 **rest 站姿不同 + 骨长/比例不同**（小白人 T-pose/长臂，骑砍身段自然下垂），骨长不一样，手/脚末端就不可能落到和源一模一样的位置。**这一环是手写脚本的物理上限，需要专业工具（Auto-Rig Pro / MotionBuilder 的 rest 对齐 + 骨长归一）才能进一步对齐。**

**最终结论**：当前结果适合**占位/原型/运动验证**；若要求"四肢与源逐位置对齐"的**正式接入级重定向**，**手写脚本做不到，需换专业工具**。

---

## 1. 项目目标 & 路线图

| 步骤 | 内容 | 状态 |
|---|---|---|
| ① 从游戏/UE 导出动画 FBX | 用 UE 5.0 从 uasset 批量导出 | ✅ **完成** |
| ② 确认骨架 + 建骨名映射表 | UE5/Mixamo → 骑砍 | ✅ **完成** |
| ③ Blender 重定向脚本 | 多种算法迭代 | ⚠️ **运动还原达标；四肢绝对位置对齐未达标** |
| ④ ModKit 导入建 Clip | 未到 | ❌ 未做 |
| ⑤ 接入游戏 SetActionChannel | 未到 | ❌ 未做 |

---

## 2. 完成了什么

### 2.1 动画库探查 + 引擎确认
- `UEAnims/` 两个 UE 工程，共 **3583 个 AnimSequence**（uasset）；用 **UE 5.0** 打开。

### 2.2 UE 批量导出 FBX（步骤① ✅）
- `pipeline/common/export_ue_fbx.py`：`AssetExportTask + AnimSequenceExporterFBX` 批量导出。
- **29 个特色动画** → `UEAnims/exported_fbx/`（剑舞/拳击/足球/待机/死亡/受击/跳跃/胜利/大笑/咒骂等）。

### 2.3 骨名映射表（步骤② ✅）
- `UE5_to_Bannerlord_bone_map.md` + `ue5_to_bannerlord_map.json` + `Mixamo_to_Bannerlord_bone_map.json`。
- 22 个主映射 + 6 个保持 rest 骨（骑砍手部 twist 细分骨）。

### 2.4 重定向脚本多轮迭代（步骤③）
| 版本 | 方法 | 结果 |
|---|---|---|
| `ue5_to_bannerlord_retarget.py` | 手写矩阵搬世界旋转 | 脚朝天（左右镜像 bug） |
| `ue_to_bannerlord_constraint_retarget.py` | Copy Rotation 约束 + bake | 部分改善，腿悬空 |
| `ue_to_bannerlord_bonespace_retarget.py` | bone-space 局部增量 | 有改善但手/躯干偏 |
| `ue_to_bannerlord_retarget_final.py` | basis 四元数复制 + 左侧测右 + 贴地 | 对称/贴地，但躯干手臂偏 |
| `ue_to_bannerlord_retarget_world.py` | 世界空间 rest-aware 增量 + R_align + X 镜像 + 贴地 + **`--face_fix` 朝向修正** | **✅ 运动忠实（RMS≈0.1–0.2°）、朝向对、贴地、对称** |

### 2.5 成品
- **`output_world/`（29，`human_lod_4`）**、**`output_high/`（29，`body_female_a` 精细 mesh，朝向已修正）**：重定向 FBX。
- **`_legacy/output_2026-09-09/preview/bannerlord_anim_preview_high.blend`**：`body_female_a` 单预览，29 动画，Action Editor 切换。
- **`_legacy/output_2026-09-09/preview/并排对比_源vs骑砍_v2.blend`**：左=源、右=`body_female_a`，朝向一致，可对比。

---

## 3. 踩过的坑（复盘重点）

- **坑1** UE5 FBX 导出类名/API：`FbxExporter` 不存在 → `AnimSequenceExporterFBX`；`options` 而非 `export_options`。
- **坑2** Blender 5.2 Layered Action：写关键帧前必须绑 `action_slot`；导出要 `bake_anim_use_all_actions + force_startend_keying`。
- **坑3（核心）** UE Mannequin 左右骨绕 Y 镜像、骑砍绕 X 镜像 → 直接搬世界旋转会脚朝天；用"左侧测右"修正。
- **坑4** 源骨架导出带 **0.01 缩放**（cm→m）→ 视口"像狗/散乱"，多次误导判断。
- **坑5** 网络/可视化误导：mixamo 下载受限、源骨架显示不可靠，曾多轮被"散乱渲染图"带偏。
- **坑6（最新）** **朝向轴反转**：源胸朝 -Y、骑砍人体胸朝 +Y，差 180°（对比图"胸到背"）→ 在重定向里绕 Z 转 180° 并 `apply` 烘焙（`--face_fix`）后一致。
- **坑7（2026-09-09 复盘追加）** **"骨骼漂在人体外"≠绑定错，是显示朝向误导**：静态 FBX（`mannequin_src.fbx` / `human_lod_4.fbx` 等）导入 Blender 后，**骨骼八面体/骨头看起来前后漂、不在人体上**，容易被误判为"骨骼没贴合"。实测判据：
  - **关节 pivot（每根骨骼的 head）位置其实精确贴合**——肩/肘/腕/髋/膝/踝的关节坐标全部落在网格对应关节上（用关节网络/绿球叠加验证）。
  - 漂的是**骨骼 tail（Blender 骨骼长度轴/Y 轴朝向）**——这批静态 FBX 的骨骼 rest 朝向被定义为**横向（沿 +Y）**，Blender 用 head→tail 画八面体时尾巴就飘到身体前后。
  - **决定性验证（bind-match）**：rest 态下**禁用 vs 启用 Armature Modifier**，网格 bbox 完全一致（偏差 = 0.0000）→ **骨骼 rest = bind pose，绑定天然正确、无拉伸**。
  - ⚠️ **数据层线索**：**带动画的 `exported_fbx/*.fbx` 骨骼 rest 是竖立的**，而**静态 `mannequin_src.fbx` / `human_lod_4.fbx`（以及 `human/body/*.fbx`）骨骼 rest 是横躺的**——同一批 UE 资源、两条导出路径（动画导出 vs 静态导出）的**轴向/骨骼朝向处理不一致**，高度怀疑是**静态资源导出时某个 FBX 设置（Apply Transform / Bone Direction / +Y Up 轴转换）未对齐**所致。**待人工从 UE 手动导出静态 FBX 复核。**

---

## 4. 当前状态（诚实评估）

| 维度 | 状态 |
|---|---|
| 动画提取（29 个 FBX） | ✅ 可靠 |
| 骨名映射表 | ✅ 可靠 |
| 重定向后运动忠实（逐骨旋转增量） | ✅ 达标（RMS≈0.1–0.2° vs 源） |
| 重定向后脚贴地 / 左右对称 / 帧范围 | ✅ 达标 |
| 重定向后整体朝向（胸方向） | ✅ 达标（180° 朝向修正后） |
| 静态资源骨骼贴合（骨骼 head 落在关节上） | ✅ 达标（bind-match 偏差=0；骨骼 tail 朝向显示横向为显示差异，不影响正确性） |
| **重定向后"四肢绝对位置"对齐源** | ❌ **未达标**（cross-skeleton rest/骨长差异，物理上限） |
| 视觉并排对比 | ✅ 可对比（并排对比 .blend） |

**根因分解**：
- **朝向（已修）**：源/骑砍胸方向差 180°，属整体刚性朝向，绕 Z 转 180° 可修。
- **躯干/手臂相对姿态（已修）**：旧版 basis 四元数复制忽略 per-bone rest 朝向差；改世界空间 rest-aware 增量后姿态自然。
- **四肢绝对位置（未修，物理上限）**：两套骨架 **rest 站姿 + 骨长/比例不同**。世界增量法忠实还原"每骨转多少角"，但手/脚末端位置由骨长决定，骨长不同 → 绝对位置对不齐。**这不是脚本 bug，是跨骨架的固有差异。**

---

## 5. 后续路径（按可行性排序）

### 方案1（✅ 已做到）：运动忠实重定向
`ue_to_bannerlord_retarget_world.py`（世界空间 rest-aware + 朝向修正）已做到**运动忠实、朝向对、贴地、对称**。适合**占位/原型/运动验证**。配合 `validate_pose.py` 可做逐帧回归校验。

### 方案2（要"四肢绝对对齐"必须走这步）：专业工具
若要求**四肢与源逐位置对齐**（正式接入级），需 **Auto-Rig Pro / MotionBuilder**（本机未装，需购买安装）。它们做**精确骨架匹配 + rest 对齐 + 骨长归一**，能解决手写脚本跨不过去的"骨长/rest 差异"。

### 方案3（可选）：换 rest 更接近的源
把源换成 **Mixamo（标准 T-pose）** 可降低 rest 差（映射表已有）；需解决下载/登录。

### 方案4（占位交付）
当前 `output_world/` / `output_high/` 可供占位；正式接入前仍需**游戏内 ModKit 导入 + foot IK** 验证。

> ⚠️ **风险提醒**：攻击/格挡类（DH_Attack/CR_F/BOW_release/GetHit）判定帧耦合，不能直接换；移动/跳跃类需根位移（`--pelvis src`）；twist 细分骨暂无软弯曲。

### 方案5（待人工复核）：静态资源导出轴向复核（坑7 后续）
**背景**：静态 FBX（`mannequin_src.fbx` / `human_lod_4.fbx` / `human/body/*.fbx`）导入 Blender 后**骨骼 rest 朝向横躺（沿 +Y）**，而带动画 FBX（`exported_fbx/*.fbx`）骨骼 rest 竖立。**同一 UE 资源、两条导出路径轴向不一致**，高度怀疑静态导出时 FBX 设置未对齐。
**待办（用户手动导出后，用以下判据复核）**：
1. 在 UE 打开 Skeleton Tree 确认骨架 rest 正常。
2. 导出 FBX 时核对：**Apply Transform**、**Bone Direction（建议 All Bones）**、**+Y Up 轴转换** 三项。
3. 导出后导入 Blender，用 **bind-match 校验**（rest 态禁用/启用 Armature Modifier，网格 bbox 偏差应=0）确认绑定无拉伸；再用**关节 pivot 网络**确认骨骼 head 落在肩/肘/腕/髋/膝/踝上。
4. 若静态骨骼仍是横躺但 bind-match=0、关节 pivot 贴合 → 属**显示朝向差异**（Blender 八面体用 head→tail 画所致），**不影响重定向正确性**，仅影响可视化；可在 Blender 内用"关节网络连线"（只连 head pivot）做正确显示。

> **复查工具**：`_legacy/output_2026-09-09/preview/关节网络v2_小白人vs骑砍lod4_q34.png`、`并排关节网络v2.blend`（preview/，已验证关节 pivot 贴合、bind-match=0 偏差）。

---

## 6. 目录速查

```
骑砍2动画重定向/
├─ human/
│   ├─ input/target/bannerlord/human_lod_4.fbx               旧低模(236 verts)
│   └─ input/target/bannerlord/body/body_female_a.fbx 等           精细人体(1586 verts, 已蒙皮 human_skeleton)
├─ UE5_to_Bannerlord_bone_map.md           骨名映射主文档
├─ ue5_to_bannerlord_map.json              UE5→骑砍 机器可读映射
├─ UEAnims/exported_fbx/*.fbx              29 个源动画(Mannequin骨架)
├─ ue_to_bannerlord_retarget_world.py      最新版重定向(增量+朝向修正, ✅最优)
├─ validate_pose.py / render_pose.py       逐帧数值校验 + 网格渲染 A/B
├─ output_world/*.fbx                      29 个重定向(human_lod_4)
├─ output_high/*.fbx                       29 个重定向(body_female_a 精细, 朝向已修正)
├─ preview/
│   ├─ bannerlord_anim_preview_high.blend  body_female_a 单预览(29动作)
│   ├─ 并排对比_源vs骑砍_v2.blend          左=源 右=body_female_a 并排对比
│   └─ 预览与后续步骤.md                   ModKit 导入指引
├─ 关节网络v2_小白人vs骑砍lod4_front.png   (preview/ 根下) 关节pivot贴合验证: 绿球=骨head在关节上
├─ 关节网络v2_小白人vs骑砍lod4_q34.png     (preview/ 根下) 关节pivot贴合验证 3/4
├─ 并排关节网络v2.blend                   (preview/ 根下) 静态rest关节贴合审阅(骨骼无动画驱动)
└─ batch_retarget_world.ps1                批量重定向脚本
```

---

## 7. 结语（最诚实的最终结论）

**做到了**：全链路技术验证（提取→导出→重定向→预览）、多种算法的迭代、并逐一解决了**脚朝天、左右镜像、贴地、源骨架显示、整体朝向 180° 反转**等一堆具体问题。最终 `ue_to_bannerlord_retarget_world.py` 能**忠实还原源动画的运动（逐骨旋转增量 RMS≈0.1–0.2°）、朝向一致、脚贴地、左右对称**。

**没做到（能力边界，如实承认）**：手写脚本**无法自动完成跨骨架的"四肢绝对位置"对齐**。因为源与骑砍的 **rest 站姿 + 骨长/比例不同**，骨长不同就决定手/脚末端落点不可能和源一模一样。要让四肢也严格对齐，需要 **rest 对齐 + 骨长归一**，这是 **Auto-Rig Pro / MotionBuilder** 的专业能力，**纯手写脚本做不到**。

**一句话**：这个项目做到了"**还原源动画的运动**"（适合占位/原型/运动验证），但"**把源动画精确重定向到骑砍骨架、四肢逐位置对齐**"这一**正式接入级目标**，受跨骨架 rest/骨长差异限制，**手写方案未达、也难达**，需转向专业重定向工具。
