# 骑砍2 动画外部导入与 UE5 重定向 — 引擎能力与实现路径

> 来源：官方 animations 文档 + docs.bannerlordmodding.lt 社区 animations 页 + CSDN 专栏第 8 篇 + **本机实测**（2026-09-09，游戏 v1.5.x，tpaccli 本地 fork）。
> 目标问题：能不能把 UE5 小白人（Manny）动画库自动重定向成骑砍2 能播的动画？

## 0. 结论速览

| 问题 | 结论 |
|---|---|
| ModKit 能否导入外部 FBX 骨骼动画？ | ✅ 能。资源浏览器导入 → 生成骨骼动画资源 → 建 Animation Clip |
| 能否"万能直接导入"？ | ❌ 不能。动画**按骨名绑定**到引擎骨架，UE5 骨名 ≠ 骑砍骨名，直接导入 = T-pose |
| 能否自动重定向？ | ✅ 能。Blender 脚本半自动：先建一次骨名映射表，同一骨架库之后**批处理通吃** |
| 风险分级 | 待机/休闲 ✅ 首选 · 移动/走跑 ⚠️ 可调 · 攻击/格挡 🔴 禁止（判定帧耦合） |

## 1. 核心机制：动画绑定骨架，与 LOD/网格无关

```
【动画链】anim_*（骨骼动画资源）─► 按骨名/骨序写入 human 骨架变换
                                         │
                                         ▼
                                    同一副骨架（顶点蒙皮驱动）
                                         │
【网格链】模型资源含 LOD0~4（高模→低模）┘ 按距离切换显示哪个 LOD，全部蒙皮同一骨架
```

- 动画从不对着 mesh 播，只动骨架；LOD 切换只是同一骨架换网格表达 → **动画只做一份，LOD0~4 自动通用**。
- 引擎绑定规则：导入的动画按**骨名**匹配到引擎已有骨架；骨名不匹配 = 通道落空（不动）。

## 2. 资源位置：动画全在 TPAC 包，不在 ModuleData

1.5.x 实测：`Modules\Native\ModuleData\` 下**没有** `.anim` 文件；动画/骨架/模型全打包在：
`$(MB2_PATH)\Modules\Native\AssetPackages\`（实测 151 个包 / 40819 个资源）。

关键资源名（`tpaccli list` 实测）：

| 资源 | 说明 |
|---|---|
| `human_skeleton` | 人类主流骨架（`human_low_skeleton` 实测是同一资源重复条目） |
| `human_lod_4` / `human_shadow_mesh` | 模型（Metamesh），含蒙皮 |
| `anim_human` / `anim_human_stand` / `anim_human_02` | 通用人类动画源资源 |
| `anim_1h_without_shield_stand_idle_1~6`（含 `_left_stance`） | 待机动画按武器类别分：持盾/单剑/双手/徒手 |
| `human_low_anim` / `human_low_skeleton` | 远景低配旁路资源（千人战场降级用） |

## 3. 导出骨架（第一步，已实测跑通）

用仓库本地 fork 的 tpaccli（`tools/face-pipeline/tpactool/TpacToolCLI/bin/Release/net9.0/tpaccli.exe`，
即社区流程说的 "TpacTools"）：

```powershell
$exe = "H:\...\tools\face-pipeline\tpactool\TpacToolCLI\bin\Release\net9.0\tpaccli.exe"
$pk  = "H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\Native\AssetPackages"
$out = "<输出目录>"
& $exe dump --packdir $pk --filter human_lod_4 --format fbx --out $out
```

- `dump` 逻辑：Metamesh 含蒙皮（SkinDataSize>0）时，自动从资源池选 `human_skeleton` +
  首个匹配动画一起打进 FBX（导出器源码：`TpacToolCLI/Program.cs` dump 分支）。
  - 选骨优先级：名字含 "human" 的骨架 → 动画骨架多数派 → 任意骨架。
  - 动画配对：`sa.Skeleton == skel.Guid` 优先，回退 `sa.GeometryGuid == meta.Guid`。
  - 环境变量 `TPAC_NO_SKEL` 存在时去掉骨骼（回导时用）。
- 产物二进制核查：FBX 内含骨架（骨名 `spine / neck / head / clavicle / l_upperarm / r_upperarm /
  l_forearm / r_forearm / l_thigh / r_thigh / l_calf / r_calf / l_foot / r_foot` 实测命中）+
  动画栈（AnimStack/AnimCurve 节点齐全）。
- **用途**：该 FBX 即重定向目标骨架模板；网格留着当动画对照预览（别删）。

## 4. FBX 导入规格（官方文档要点 = 导入门槛）

| 项 | 要求 |
|---|---|
| 帧率 | 30 FPS（优）或 60 FPS |
| 骨骼上限 | **64 根** |
| root 骨命名 | 名称需以 `_notused` 结尾 |
| 导入设置 | "Use scene name" 必勾；轴：**Z 向上**；单位/约束设置按官方截图 |
| 首次导出 | FBX 含骨架+网格；之后只导骨骼动画，**不导骨架/网格**（引擎已有该骨架，重复导入会出错） |
| 回导结果 | 资源浏览器出现新骨骼动画资源 → **右键 Create override**（直接新建非 override 有 bug）→
  改名/设置 clip 参数（start/end 帧、blend_in/out、priority、flags 等） |

Clip 关键属性：`blend_in_period`（融合时间）、`blend_out_period`（提前结束融合）、`priority`、
`flags`（`cyclic` 循环 / `enforce_all` 全身 / `disable_foot_ik` / …）——**FBX 只带纯骨骼数据，这些属性须在资源浏览器里重设**。
战斗结构（四向攻击、取消、攻击判定时机）由 `action_types.xml` 分类属性（`type / usage_direction / action_stage`）+ `combat_parameters.xml` 驱动，替换战斗动画 = 判定错位，🔴 勿动。

## 5. 千人战斗与 LOD 分层（为什么动画不用管 LOD）

模拟层（CPU，真正的主体）：
1. 队形系统 Formation：一队士兵跟"队形锚点"走，寻路按队算一次；只有近战接触区士兵开完整战斗 AI。
2. 距离分档：离玩家越远 tick 频率越低、行为越简化。
3. 战斗判定事件化：武器命中只在接触区逐帧检测；远处是简化演出。
4. 动画降级：远景用 `human_low_*` 低配骨架/动画旁路。
5. 尸体/物理/粒子限流。

渲染层（GPU，配套）：LOD 距离表（官方实测）——LOD0→1 @15m、LOD2→3 @30m、LOD4→5 @70m、LOD5→6 @130m、**210m+ 剔除**；实例化批量绘制；阴影/材质距离限制。

→ 替换**待机动画**只影响高细节档（玩家身边），远景引擎自己降级，不存在"动画拖垮千人战"的问题。

## 6. 重定向路线图与当前进度

| 步骤 | 内容 | 状态 |
|---|---|---|
| ① 从游戏导人形骨架 FBX | 见 §3 | ✅ 已完成 |
| ② Blender 确认骨架 + 建 UE5→骑砍骨名映射表 | 一次性的活 | ⏳ 待做 |
| ③ Blender 自动重定向脚本 | 导入 UE5 动画 → 映射改名/对齐 rest pose → 逐帧采样烘焙到骑砍骨架 → 导出 | ⏳ 待做 |
| ④ ModKit 导入建 Clip | 只导骨骼动画 → Create override 建 clip | ⏳ 待做 |
| ⑤ 接入游戏 | 不动全局 action_sets（全局映射=所有角色都换）；**代码 `Agent.SetActionChannel` 播新 clip**（LWN AgentBrain 空闲态接入；社区已验 cheer 例子） | ⏳ 待做 |

重定向脚本自动化边界：映射表建一次后批处理通吃；骑砍骨架里有 UE 没有的骨（武器槽/头发等）保持 rest pose；
调质量（踩地/手贴合）无法全自动。

## 7. 踩坑记录

| 坑 | 说明 |
|---|---|
| 以为动画在 ModuleData | ❌ 1.5.x 的动画/骨架/模型全在 TPAC（`AssetPackages/*.tpac`），`.anim` 文件不存在于 ModuleData |
| "LOD 专用动画" | 主战场 LOD0~4 共享 `human_skeleton` 一份动画；`human_low_*` 是远景低配旁路，不是 LOD 网格专用 |
| Blender 导入 TWT FBX | 已知 morph 断言崩溃坑（Blender 5.2，`head_xxfemale_a` 那种含 102 morph 的网格需内存补丁）；
  `human_lod_4` 是身体网格无 morph，大概率免疫；真崩了按 [[blender-fbx-viewing]] 的方式处理 |
| tpaccli 输出噪音 | dump 时大量 `meta parse skip`（自然景观纹理解析失败）属正常，看产物文件即可 |
