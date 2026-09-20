# UE5 Manny → 骑砍2 human_skeleton 骨名映射表

> 生成时间：2026-09-09  ·  依据：Blender 5.2 实测导入 `human_lod_4.fbx`（骨架 `human_skeleton`，28 pose bone）
> 用途：步骤②一次性产物。之后 @重定向脚本 `ue5_to_bannerlord_retarget.py` 直接引用 JSON 版（`ue5_to_bannerlord_map.json`）逐帧采样烘焙。
> 源骨架假设：UE5 默认 **Manny / Mannequin（SK_Mannequin）** 骨名。

---

## 1. 目标骨架（骑砍 human_skeleton / 27 变形骨）

从 FBX 实测层级（`pelvis` 为根，无独立 `root` 骨）：

```
pelvis
├─ l_thigh         → l_calf → l_foot → l_toe0
├─ r_thigh         → r_calf → r_foot → r_toe0
└─ spine → spine1 → spine2
        ├─ neck → head
        ├─ l_clavicle → l_upperarm_twist → l_upperarm_twist1
        │                └→ l_foretwist → l_foretwist1 → l_hand → l_finger0
        └─ r_clavicle → r_upperarm_twist → r_upperarm_twist1
                         └→ r_foretwist → r_foretwist1 → r_hand → r_finger0
```

- 手臂比 UE5 多两级 **twist 细分骨**（`*_twist1`、`*_foretwist1`），用来让肘/前臂在上臂与腕之间分摊旋转。
- 手指只有一级 `*_finger0`（UE5 无手指骨，保持 rest）。
- `l_toe0 / r_toe0` ≈ UE5 的 `ball_l / ball_r`。

## 2. 一一映射（UE5 骨名 → 骑砍骨名）

| 区域 | UE5 Manny | 骑砍 human_skeleton | 说明 |
|---|---|---|---|
| 根/骨盆 | `pelvis` | `pelvis` | 双方同名，作为重定向根。UE5 `root` 位移丢弃（骑砍没有独立 root，`pelvis` 承担根语义） |
| 脊柱 | `spine_01` | `spine` | 一一对应 |
| 脊柱 | `spine_02` | `spine1` | 一一对应 |
| 脊柱 | `spine_03` | `spine2` | 一一对应 |
| 颈 | `neck_01` | `neck` | 一一对应 |
| 头 | `head` | `head` | 一一对应 |
| 肩 | `clavicle_l` | `l_clavicle` | 左右互换 |
| 肩 | `clavicle_r` | `r_clavicle` | 左右互换 |
| 上臂 | `upperarm_l` | `l_upperarm_twist` | **主上臂骨（肩→肘上段）** |
| 上臂 | `upperarm_r` | `r_upperarm_twist` | 左右互换 |
| 前臂 | `lowerarm_l` | `l_foretwist` | **主前臂骨（肘→前臂上段）** |
| 前臂 | `lowerarm_r` | `r_foretwist` | 左右互换 |
| 手 | `hand_l` | `l_hand` | 一一对应 |
| 手 | `hand_r` | `r_hand` | 左右互换 |
| 大腿 | `thigh_l` | `l_thigh` | 一一对应 |
| 大腿 | `thigh_r` | `r_thigh` | 左右互换 |
| 小腿 | `calf_l` | `l_calf` | 一一对应 |
| 小腿 | `calf_r` | `r_calf` | 左右互换 |
| 脚 | `foot_l` | `l_foot` | 一一对应 |
| 脚 | `foot_r` | `r_foot` | 左右互换 |
| 脚尖 | `ball_l` | `l_toe0` | 一一对应 |
| 脚尖 | `ball_r` | `r_toe0` | 左右互换 |

> 共 **22 个主映射**。上臂/前臂的旋转只落到 `*_twist` / `*_foretwist`（主段），下段的 twist 细分骨保持 rest，由父级刚性跟随（第一版够用；脚贴地/手贴合等精调后续再补，见 §4）。

## 3. 保持 rest pose 的骑砍骨（UE5 无对应）

| 骑砍骨 | 处理 |
|---|---|
| `l_upperarm_twist1` / `r_upperarm_twist1` | 保持 rest（上臂下段，跟随 `*_twist` 刚性转动） |
| `l_foretwist1` / `r_foretwist1` | 保持 rest（前臂下段/腕，跟随 `*_foretwist` 刚性转动） |
| `l_finger0` / `r_finger0` | 保持 rest（手指，UE5 无对应） |

处理策略：烘焙时这些骨**不写关键帧**，让其继承父级（跟随主段转动）。若后续需要前臂弯曲分布在两段，再按权重拆分（进阶项）。

## 4. 已知限制 / 进阶项（步骤③之后）

| 项 | 说明 |
|---|---|
| 单位换算 | UE5 FBX 导出常为 cm，骑砍为 m（pelvis 高约 0.915m）。重定向脚本按 FBX `unit_scale` 自动折算，必要时以 `force_unit_scale=0.01` 兜底 |
| 轴对齐 | 骑砍 Z 向上、脸朝 +Y；UE5 Manny 脸朝 +X。脚本统一在 Blender 内做轴向归一（假设两侧都已导入为 +Z 朝上，脸朝方向差异通过骨架 rest pose 自然消化，不额外旋转骨架） |
| 骨骼缩放 | 双方骨架骨长不同（骑砍有 twist 细分）。脚本只搬旋转（location 仅根 `pelvis`），避免缩放对骨骼变形产生拉伸感 |
| 脚贴地 / 手贴手 | 无法全自动，需后续人工精调 |
| root 位移 | UE5 `root` 的水平位移不搬（骑砍无 root 骨），避免角色原地滑行异常；如需位移，需单独导出到 `pelvis` |

## 5. 验证情况

- ✅ 步骤①：`human_lod_4.fbx` 已导出（本表依据），FBX 内含 `human_skeleton` 骨架（28 pose bone / 27 变形骨）+ 蒙皮网格 + 动画栈。
- ✅ 步骤②：本表（+JSON 版）已完成。
- ⏳ 步骤③：重定向脚本见 `ue5_to_bannerlord_retarget.py`（需 UE5 源动画 FBX 实测）。
- ⏳ 步骤④⑤：ModKit 导入建 Clip / 代码接 `SetActionChannel`。
