# 动画「带位移」到底怎么跑：根运动 vs 代码推位移

> 日期：2026-09-20 · 场景：重定向出一条**带位移的攻击动画**（GhostSamurai_Execution02 Root）后，问"骑砍2 怎么才能跑这种动画，而不是演完又回到原点"。
> 结论状态：🟢 **全链路实机验证通过（2026-09-20）** —— 上游资源层跑通；引擎层走**路 B（clip 的 `displacement` 字段）成立**，
> 实测位移量与向量模长**差 0.016%**、方向夹角**差 0.04°**。未验项见 §六。
> 相关：[自定义战斗.md](自定义战斗.md) §13（根运动 vs 原地 · 全 flag 表）、[骨骼动画TRF格式与增量陷阱.md](骨骼动画TRF格式与增量陷阱.md)（TRF 语义）、[骑砍2Agent运动与位置机制.md](骑砍2Agent运动与位置机制.md)（位置写入的真实语义）

---

## 〇 一句话结论

**骑砍2 的默认是「原地动画」。**

位移哪怕完整烘进了骨骼（TRF 的位置轨），动画播完骨骼回到静止姿势，角色就"瞬移回原点" ——
因为**动的是骨骼，不是 agent 的世界坐标**。要让角色真的被动画带走，只有两条路：

| 路 | 做法 | 成熟度 |
|---|---|---|
| **A 代码推位移** | 动画**原地做**（零净位移）+ 代码逐帧 `TeleportToPosition` 推 agent | ✅ **三个实机 mod（SCO/ACC/ArtemCore）全部这么干** |
| **B 引擎根运动** | clip 开 `displace_position` + **在 `displacement` 用法里填一个向量** → 引擎自己按它更新 agent 世界位置 | ✅ **2026-09-20 实机跑通**（见 §3.3）|

> 🔴 **一句话记住这条路最关键的一件事**：把位移烘进 TRF 的骨骼轨**没有用** —— 引擎不看它。
> 真正推动 agent 的是 clip 里那个**手填的 `displacement` 向量**（口径 = 角色本地水平面、单位米）。
> 这件事此前全网没文档，本轮由「原版 337 条带位移 clip 反推 + 实机正面验证」两头夹出来的。

> 本轮新增的 `--pelvis src` 解决的是**上游**：以前重定向把源侧根位移**整段丢掉**，TRF 里根本没有位移可谈（§二）。

---

## 一、症状拆三层

| 现象 | 直接原因 | 在哪一层 |
|---|---|---|
| 动画演完角色**瞬移回原点** | 位移只烘在**骨骼**上，agent 的**世界坐标**自始至终没动 | **引擎层**（默认原地，见 §三） |
| TRF 里**根本没有水平位移** | 重定向只做"逐帧贴地"，源侧根位移被丢弃 | **资源层**（本次修掉，见 §二） |
| clip 能播但游戏流程不认 | clip 元数据没配（`Source 1/2`、`Priority`、`Continue to action`…） | **ModKit 层**（见 TRF 陷阱文档 §九） |

**为什么"骨骼动了"会被误判成"角色动了"**：ModKit 预览 / 离线查看器里看的就是骨骼，
骨盆相对骨架原点走了 3.68 m，肉眼看就是"人走过去了"。
但**引擎里 agent 的位置由导航/移动系统解算**，跟骨骼走多远毫无关系；动画一结束（或一切到下一个动作），
骨盆回到静止姿势 → 角色"弹回去"。

---

## 二、资源层：把位移做进 TRF（本轮已跑通）

### 2.1 源侧根位移挂在哪里（实测）

**这批 UE 的 `Root` 变体 FBX，根位移挂在【骨架对象】上，不在骨骼上。**

| 项 | 实测 |
|---|---|
| 源骨架 | 67 骨，**没有 `root` 骨**（`pelvis` 就是最上层骨）；骨架对象名 `root`，**scale = 0.01** |
| 根位移载体 | 骨架对象的 `location` fcurve（`obj_paths` 里那条裸 `location`），不是 `root`/`pelvis` 的 `.location` |
| `Execution02`（Root） | 骨架对象 `(0,0,0)` → `(-0.246, -3.673, 0)` = **3.6816 m，纯水平** |
| `Execution02`（Inplace） | 同一条曲线：**恒 0** —— 正好当 A/B 对照 |

> 探针脚本：Blender 里 `import_scene.fbx` → 逐帧读 `arm.matrix_world.translation` 与 `pose.bones["pelvis"].head`。
> 实测 `pelvis` 的净位移与骨架对象的净位移**逐位相同**（3.6816 m）→ 位移 100% 来自对象节点，骨骼自身只做局部摆动。

### 2.2 旧管线的漏洞：`--pelvis ground|none` 都丢位移

`pipeline/rigs/ue_mannequin/retarget.py` 原有两个骨盆策略，**都不带水平位移**：

| 模式 | 原行为 | 后果 |
|---|---|---|
| `ground` | 逐帧把最低脚拉回地面（**只改 z**） | 水平位移整段丢 |
| `none` | 不写位移轨（飞行/离地用） | 水平位移整段丢 |

**对 `output/trf/` 全量 TRF 的扫描**（本轮新增前 196 个；逐文件明细见 `output/verify/trf_pos_track_scan.csv`，
新增两条后重扫为 198 个）：

| 统计 | 值 |
|---|---|
| 位置轨非零的 TRF | **63 个 / 196** |
| 其中 **水平分量（x/y）range 非零**的 | **0 个** |
| 位置轨恒零的 TRF | 133 个 / 196 |
| （新增后）| 198 个里水平非零 **只有 1 个** = `gs_execution02_root.trf` |

> 即：**本工程此前没有任何一个带水平位移的 TRF**。所有"动"都只是骨盆上下起伏（range 全是 `[0, 0, 0.xx]`）。
> 这不是 bug 而是设计缺口 —— 重定向一直按"原地动画"做，源侧那份位移从来没被搬过来。

### 2.3 修法：`--pelvis src`（新增，`ue_mannequin` 线）

```bash
python pipeline/run_retarget.py --rig ue_mannequin \
  --clip GhostSamurai_Execution02 --name gs_execution02_root --pelvis src \
  --animdir "<...>/Apose/Execution/Root"
```

实现要点（三条，都是别的坑换来的）：

1. **在世界空间算完再转回骨架空间**（`tgt.matrix_world.inverted()`）—— 硬约束 11；
2. **用与旋转同一套帧变换 `F = Rot(Z,180°)`** 转位移方向，否则朝向对了位移方向反；
3. **竖直仍走原来的逐帧贴地**，水平叠加在上面 —— 这样"已实机验证过的贴地行为"一个字节没动，只多了一层水平位移。

脚本自带两条新自检（每次运行都打印）：

```
CHECK_TRAVEL: 源水平 3.6816 m vs 目标水平 3.6816 m，差 0.0000 m（相对 0.00%）
骨盆位移轨完成（--pelvis src）: 目标侧净 [0.2462, 3.6734, 0.0378] m（水平 3.6816 / 竖直 0.0378）
```

并且 `--pelvis ground|none` 遇到"源侧有水平位移"时会**主动告警**（防止再无声丢位移）：

```
!! 源侧有 3.682 m 水平位移，但 --pelvis ground 会【丢弃】它 —— 要带位移请用 --pelvis src
```

### 2.4 验收判据（位置轨）

| 判据 | 期望 | 本次实测 |
|---|---|---|
| 位置轨**首帧** | `≈ (0,0,0)`（纯增量） | `(0,0,0)` ✅ |
| 水平净位移 | 与源一致 | 3.6816 m vs 3.6816 m，**差 0.00%** ✅ |
| 反变换后逐帧偏差 | ≈ 0 | **0.00001 m** ✅ |
| 骨数 / 帧范围 | 28 / 2–102 @30fps | ✅ |
| 旋转绝对公式交叉核对 | < 0.5° | `CHECK_OK 0.0560°` ✅ |
| FBX 体检 | 骨数 28 / 根名 `human_skeleton_notused` / 无叶骨 / 无 mesh / 米制 | `PASS（0 项不合格）` ✅ |

> ⚠️ 首帧必须是 0：导出时**帧 1 的姿态会被烘成 rest**（`fbx_to_trf.py` 文件头记录的老坑），
> 所以 TRF 位置轨的语义是「**相对首帧的增量**」。首帧若出现米级数字（~0.85）= 写成了绝对语义 → 实机会整体抬高。

### 2.5 位移方向：这是【关卡空间】的直线，不是角色本地前向

用「头相对骨盆的水平偏移」当朝向口径（硬约束 18），实测：

| 采样 | 源（UE） | 目标（骑砍） |
|---|---|---|
| 首帧：朝向 vs 行进方向夹角 | **34.84°** | **34.50°**（差 0.34°） |
| 末帧：朝向 vs 行进方向夹角 | **145.16°**（角色已转身） | 137.15°（同口径噪声内） |

**读法**：角色在**一条固定的世界直线上**被推着走，行进中自己会转身/挥砍 —— 这不是"本地前向根运动"。
这与 [README 硬约束 16](../tools/anim-retarget/README.md) 的观察一致：这批成对动画的位移是
「**从 UE 关卡原始站位走到共享点**」，方向按关卡算，不按角色算。

> 工程含义：**成对动画（处决/伏击）用根运动时，两人必须按同一套关卡站位摆**（现有 `pairAnchor` 只对首帧，
> 后面对齐交给动画自身 → 正好吃得住这条路）。

---

## 三、引擎层：让 agent 真的被带走（两条路）

### 3.1 对照表（转述自 [自定义战斗.md](自定义战斗.md) §13）

| | **A 原地 + 代码推位移** | **B 根运动（root motion）** |
|---|---|---|
| 怎么开 | 默认，什么都不用做 | clip 开 `displace_position` + 配 `displacement_data` |
| 谁负责位移 | 引擎移动系统 / 你的代码 | **动画自己**（`anf_displace_position`：*"Updates world position of the agent during the animation using the displacement data."*） |
| 适用 | 循环动作（走/跑/待机）、原地挥砍、格挡 | **一次性位移**（处决扑击、翻滚、冲锋、倒地） |
| 🔴 禁忌 | — | **循环动作绝不能开**（引擎自己也在推位移 → 双倍速 + 滑步） |
| 实机先例 | **SCO（翻滚）/ ACC（处决）/ ArtemCore 全部如此** | 只有 ACC 的"处决受击方"那条 clip **开了 flag**（元数据逐字节实证），但**代码从不靠它移动执行者** |

### 3.2 路 A 的标准写法（照抄 SCO）

```
每帧：position.x += dir.x * RollTravelPerSecond * dt      # SCO 默认 6 m/s = DodgeDistance / 0.5
      agent.TeleportToPosition(position)
      position.z = Scene.GetGroundHeightAtPosition(position, bodyFlags)   # 贴地
```
ACC 走的是"一次性瞬移"：`TeleportToPosition(受害者位置 + 受害者朝向 × teleportDistance)`（成对字典里的 `1.75`）。

**要点**：① 每帧推、不要一次推到终点；② 自己贴地（Z 只能靠地面解算，见 [骑砍2Agent运动与位置机制.md](骑砍2Agent运动与位置机制.md) §2）；
③ 动画要**原地做**，否则骨骼位移 + 代码位移叠加 = 双倍。

> **A 与 B 是互斥的**：选了 B 就别再在代码里推；选了 A，TRF 的位置轨就应该是 0（本次 `gs_execution02_inplace.trf` 即是这个形态）。

### 3.3 路 B 的接线清单（🟢 2026-09-20 实机跑通）

| 步 | 做什么 | 状态 |
|---|---|---|
| 1 | 资源：位移进 TRF | ✅ §二。🔴 **但引擎不看它**，见事实① |
| 2 | ModKit：建 clip 时勾 **`displace_position`** | ✅ flag 位 `0x400000000000`；[官方 animations.md](bannerlord_official_docs/Asset%20Management/Asset%20Types/animations.md) L92 |
| 3 | **在 `clip_usage_data` 折叠区给 `displacement` 填 `(X, Y, Z)` + `endProgress`** | ✅ **这就是那件"没人知道填什么"的事** —— 口径见事实② |
| 4 | 接线：`action_types` + `action_sets` 把它做成一个动作 | ✅ 用 `custom.do_anim <动作名>` 验 |

> 边界仍在：**C# 读不到 clip 的大部分 flag**（只有 `anf_synch_with_ladder_movement` 与 `anf_displace_position` 两个在托管侧被 bit-test），所以这条路是"纯数据接线、零代码"。

#### 🔴 三条实测事实（本节的价值全在这）

**① TRF 里那条位移轨，引擎不认。**

`gs_execution02_root.trf` 的根骨位置轨实打实有 3.6816 m 水平位移（§二验过）。装进游戏播它：

| | agent 位置 |
|---|---|
| 播之前 | `(409.0954, 321.2711, 36.37994)` |
| 播之后 | `(409.0954, 321.2711, 36.37994)` ← **6 位小数全同，一位没动** |

⇒ 位移轨只动**骨骼**，不动 **agent 世界坐标**（与 §一 的判断一致）。要真的被带走，**必须填 `displacement` 字段**。

**② `displacement` 的口径 = 角色本地水平面，单位米。**（原版 337 条带位移 clip 反推 + 本次实机正面验证）

| 分量 | 含义 | 依据（原版同名对照，四对全在这） |
|---|---|---|
| **+X** | 角色**右手**侧 | `stagger_left_lvl2 = (-1, 0, 0)` · `stagger_right_lvl2 = (+1, 0, 0)` |
| **+Y** | 角色**正前**方 | `strike_knock_back_chest_back`（背后挨打 → 往前飞）`= (0, +1.7, 0)`；`..._chest_front = (0, -1.7, 0)` |
| **Z** | **恒 0**（纯水平） | 337 条里 Z **无一例外**全是 0 |
| `endProgress` | 位移"走完"的进度点（占全长比例） | 原版实测 **0.4 ~ 1.0** |

> 原版的值是 1.0 / 1.7 / 1.5 这种**整数** ⇒ 这栏是**动画师手填**的，引擎**不会**从动画自动算
> —— 这正是"裸导的 clip 里它是空的、以及没人知道该填什么"的来源。

**③ 值从哪来 = 读 TRF 根骨位置轨的净位移。**

```bash
python Debug\offline\trf_root_travel.py --trf "<xxx.trf>"
# 输出：净位移 (X,Y,Z) · 该填的 (X,Y,0) · endProgress 建议值 · 轨迹直线度诊断
```

#### 实机验证数字（2026-09-20）

| 项 | 期望 | 实测 |
|---|---|---|
| 位移量 | 3.6816 m（向量模长） | **3.6822 m**（差 0.0006 m = **0.016%**）|
| 行进方向 vs 角色朝向夹角 | 3.83°（向量相对正前方的夹角） | **3.79°**（差 **0.04°**）|
| 末端弹回 | 不弹回 | 不弹回 ✅ |

⇒ **引擎按「向量 + `endProgress`」插值推进 agent，位移量精确等于向量模长。**

#### 两个已知边界

- **`endProgress` 与轨迹形状不匹配会滑步**：引擎按比例插值，而动画的位移常常**不是匀速**。
  本次源动画"前 1/3 就冲完 3.7 m"，按 `endProgress=0.4` 铺开会中途滑一点。
  要贴准，要么调 `endProgress`，要么把源动画的位移做成匀速。
- **A / B 互斥**（老结论仍成立）：开了 `displace_position` 就别再在代码里 `TeleportToPosition`，否则翻倍。
  本次**没有**翻倍（3.68 而不是 7.36）—— 但那是因为我们没写代码推位移，只算半个答案。

### 3.4 建议（2026-09-20 修订：两条路都通了，取舍换了）

原先建议"先走 A"（理由：三个实机 mod 都走 A、B 连怎么填都没文档）。**现在 B 也通了**，取舍变成：

| | **A 代码推位移** | **B 引擎根运动** |
|---|---|---|
| 位移曲线 | **你说了算** —— 可逐帧自定义，想推 1.75 m 就 1.75 m | 引擎按**一条直线 + `endProgress`** 插值，形状固定 |
| 代码量 | 要写每帧推 + 自己贴地 | **零代码**（填一栏）|
| 适用 | 位移要按游戏内站位现算（成对动画的 `pairAnchor`）、或需要非直线轨迹 | 位移本来就是一条直线、且源动画走的距离就合适 |

**本次选 B 的场合**：处决扑击这类"一次性直线位移"，源动画自己走的距离正好是我们想要的。
**该选 A 的场合**：位移方向/距离要按站位现算（`pairAnchor` 那条路），或要非直线。

> §二 那个 `--pelvis src` 现在有两个用处：① 走 B 时把动画的位移做进 TRF（同时让离线查看器/ModKit 预览里看得见位移）；
> ② 走 A 时也要知道"源动画实际走了多远、朝哪个方向"，才能把代码推的位移量对齐得像同一件事。

---

## 四、本轮产物

| 产物 | 说明 |
|---|---|
| `output/fbx/gs_execution02_root.fbx` + `output/trf/gs_execution02_root.trf` | **带位移版**：位移烘进骨盆位置轨，水平净 3.6816 m |
| `output/fbx/gs_execution02_inplace.fbx` + `output/trf/gs_execution02_inplace.trf` | **原地对照版**：同一条动画，位置轨水平恒 0（路 A 用这个） |
| `output/verify/execution02_root_travel.svg` | 俯视三联图：源轨迹 / 目标轨迹 / 反变换后叠加（两条曲线重合，最大逐帧偏差 0.00001 m） |
| `output/verify/execution02_root_travel.json` | 同一份数据的机器可读版 |
| `pipeline/rigs/ue_mannequin/retarget.py` | 新增 `--pelvis src` + 丢位移告警 + `CHECK_TRAVEL` 自检 |

**引擎侧产物（2026-09-20 装机，实机验证通过）**：

| 产物 | 说明 |
|---|---|
| `Taikou\AssetPackages\lwn_taikou_anim.tpac` | 含 clip `execution02`（`displacement = (0.2462, 3.6734, 0)`、`endProgress = 0.4`、flag `displace_position`）· 配套受击侧 clip `executed02`（`(0.63, 1.04, 0)`、`endProgress 0.7`、时长 4.2 s）|
| `Taikou\ModuleData\action_types.xml` / `action_sets.xml` | 声明动作 `act_execution02` 并绑到 clip（`animation="execution02"`）· 受击侧 `act_executed02` → `executed02`（**2026-09-22 改名**：动作名去掉 `_root` 后缀 —— 动作名只说"演什么"，"带不带位移"是 clip 自己的事）|
| 备用包 | `Debug\offline\anim_pack_backup\` 两份（ModKit 原产版 / 更早一版）|

**怎么实机验（配对检查台，2026-09-22 新增）**：`custom.exec_pair`（[MyCommands.cs](../ExampleModVS/ExampleMod/ExampleMod/Debug/MyCommands.cs)）
—— 把 **interact 焦点**上的 NPC 拉到玩家正前方 2 米（第 2 参可改距离，0.5~5 m）、让他面朝玩家，然后**同一帧**起播
玩家 `act_execution02` + 他 `act_executed02`。玩家自己单播仍走 `custom.do_anim act_execution02`。
首参可弃（`custom.exec_pair 1` 也能跑，解析不出就回落焦点并在返回里注明）。

ModKit 填值：`Source 1 = 2` / `Source 2 = 102` / **`Duration = 3.367`（秒）**；`displacement` 填 `X=0.2462 Y=3.6734 Z=0 endProgress=0.4`。

> **Duration 的口径（2026-09-22 查清）**：官方文档写的是 "Duration of this animation clip **in seconds**"；
> 把原版 6177 条 clip 的「帧数 ÷ 时长」统计出来，**920 条落在 60.00、270 条落在 30.00**（其余是 40 / 50 / 31.25 等非整数 fps）
> ⇒ **Duration(秒) = 帧数(S2 − S1 + 1) ÷ fps**（例：某原版 clip Duration 1.3 而 S1..S2 = 0..77 → 78 ÷ 60 = 1.3，精确吻合）。
> 本 clip：101 帧 @30fps ⇒ **3.3667 s**；受击方 126 帧 ⇒ **4.2 s**。
> **Flags（2026-09-22 从装机包 `execution_02.meta` 逐字解出，共 9 个，其余全不勾）**：
> `ignore_all_collisions` · `client_prediction` · `disable_hand_ik` · `use_left_hand_during_attack` ·
> `lock_movement` · `enforce_lowerbody` · `enforce_all` · `disable_foot_ik` · **`displace_position`**。
> 同 block 里还有 `BlendIn = 0.3` / `BlendOut = 0`（meta 的 @96 = 0.3 与官方 clipinfo 布局一致）。
> 注意 **`Do not optimize`、`cycle`、`enforce_root_rotation`、`disable_agent_agent_collisions`、`align_with_ground`、
> `affected_by_movement`、`update_bounding_volume` 都不要勾**（装机验证的那条就是没勾）。
> 受击方 `executed_02` 建议照抄同一套（`use_left_hand_during_attack` 对受击方无实际作用，留着无害）。
> 另：装机包 `execution_02.meta` 已核实 **`Source 1 = 2` / `Source 2 = 102`**（浮点 @8=2.0、@12=102.0）、BlendIn 0.3、
> `displacement = (0.2462, 3.6734, 0)`、末端 0.4（endProgress）—— 与上面填值单逐位一致。

**配套：受击方 `executed_02`（2026-09-22 补测，同一条计算路径）**

| 项 | 值 |
|---|---|
| 资源 | `output/trf/ue_GhostSamurai_Executed02__Root.trf`（受击方·带位移）+ `output/fbx/ue_GhostSamurai_Executed02__Root.fbx` |
| 位移轨实测 | 首帧 2 `(0,0,0)` → 末帧 127 `(0.6360, 1.0444, -0.6980)`；**水平净 1.2228 m**（`CHECK_TRAVEL` 1.2228 vs 1.2228，差 0.00%） |
| ModKit 填值 | `Source 1 = 2` / `Source 2 = 127` / `Duration > 0`；`displacement` 填 **`X=0.6360 Y=1.0444 Z=0`**（口径 = 水平面；竖直的 -0.698 是"被摔倒"不进该字段）；`endProgress` 建议 **0.4**（与攻击方一致）或 **0.7**（实测位移在 ~70% 处走完） |
| 值可信度 | 与攻击方同一条代码路径（`retarget.py --pelvis src` 的"目标侧净"）；攻击方算出来 = **装机实测值 (0.2462, 3.6734, 0)**，**逐位一致** ⇒ 受击方这份同样可信 |
| 方向 | 角色本地坐标系：主要是 +Y（3.67 m vs 1.04 m 都在 +Y）⇒ 受击方是**被顺着攻击方向推出去**的 |

> 两条 clip 的 `displace_position` flag 都要勾；**两边档位要配套**（攻击方 Root 3.68 m + 受击方 Root 1.22 m）。
> **资源落位（2026-09-22）**：两个 Root 版已放进 Kit 的资产源目录
> `Modules/TaikouAnim/AssetSources/Execute/ue_GhostSamurai_Execution02__Root.trf` 与 `…/ue_GhostSamurai_Executed02__Root.trf`
> （原地版 `…__Inplace.trf` 仍留在同目录，两档并存、按需挂）。
> 攻击方 Root 的重新导出版（改名 `ue_GhostSamurai_Execution02__Root`）与装机验证过的 `gs_execution02_root.trf`
> **只差第 3 行的资源名**，其余 2962 行逐字节相同 ⇒ 位移/旋转数据与实机验证过的完全一致。
> 2026-09-22 重导出的 TRF 位移轨 **与旧产物逐位一致**（攻击方仍 = 0.2462 / 3.6734 / 0）；变的只是**旋转**（补回骨架对象那 180° 转身，见硬约束 29）。

---

### 🔴 待办（2026-09-20 挂）：把 `displacement` 的值回填进 ModKit 工程

**现状**：这个值**只存在于装机的 tpac 里**（由离线 `tpaccli clipset` 写进去的），
**ModKit 工程里 `execution_02` 那个字段仍然是空的**。

**为什么必须回填**：ModKit 工程是**源头**，tpac 是**产物**。Publish 出来的永远是工程里的值
⇒ **只要再 Publish 一次，我手填的值就被洗掉**。
症状会是「动画又不动了」—— 和"引擎不认位移轨"长得一模一样，**极易误判成引擎问题**（本轮就在这上面绕了一整圈）。

**要做的事**（值已定稿、实机验证过，**不需要再调**）：

1. Kit 里给 `execution_02` 的 `displacement` 填 `X = 0.2462` / `Y = 3.6734` / `Z = 0` / `endProgress = 0.4`
2. 保存 → Publish（目标 `Modules\Publish\TaikouAnim`）
3. 产物**改名**拷进 `Taikou\AssetPackages\lwn_taikou_anim.tpac`（🔴 绝不能叫 `pack0.tpac`，同名会覆盖内容包自己的 299 MB 包）

**分工约定（2026-09-20 拟定，⚠️ 用户尚未正式裁定 —— 别当既定规则引用）**：
**调参阶段由 Claude 离线手填**（一轮几秒、不用开编辑器）；**值定稿后必须由用户在 Kit 回填**（回到源头，否则下次 Publish 必丢）。

**可选保险（未采纳，待定）**：把定稿值写进 `run_all_checks.py` 的某个体检脚本，
每次核对装机包里那四个数 —— 被某次 Publish 洗掉就报红，不必等实机才发现。

---

## 五、复现

```bash
cd D:/BrainMaker/骑砍2动画重定向

# 带位移（Root 变体）
python pipeline/run_retarget.py --rig ue_mannequin --clip GhostSamurai_Execution02 \
  --name gs_execution02_root --pelvis src \
  --animdir "input/source/ue_mannequin/clips_exec/GhostSamurai_katana/Mannequin/Animation/Apose/Execution/Root"

# 原地对照（Inplace 变体）
python pipeline/run_retarget.py --rig ue_mannequin --clip GhostSamurai_Execution02 \
  --name gs_execution02_inplace --pelvis ground \
  --animdir "input/source/ue_mannequin/clips_exec/GhostSamurai_katana/Mannequin/Animation/Apose/Execution/Inplace"
```

> 🔴 **`--animdir` 必须给到 `Root/` 或 `Inplace/` 这一层**：两边文件名完全相同，给到 `Execution/` 会随机命中其中一个。

---

## 六、验证状态（2026-09-20 更新）

| # | 原未验项 | 结论 |
|---|---|---|
| 1 | TRF 位置轨 与 引擎 `displacement_data` 是不是同一条数据 | ❌ **不是**。位置轨单独**不推动角色**（§3.3 事实①）；`displacement` 是另填的 |
| 2 | `displace_position` 实测效果（走多远 / 弹不弹回） | ✅ **走 3.6822 m、不弹回**（§3.3 验证数字）|
| 3 | `Agent.ComputeAnimationDisplacement(dt)` 的返回值语义 | ⬜ **仍未验** —— 这条路不需要它，暂不追 |
| 4 | 与代码推位移叠加会不会双倍 | 🟡 **半个答案**：只开 B 不翻倍；A+B 同开未测 |
| 5 | 竖直分量（本次竖直仍由"逐帧贴地"给，源竖直位移 = 0） | ⬜ 未验。换带跳跃/腾空的源（`Anim_JU_*`）再跑一次 `--pelvis src` |

**本轮新增的两条待验**：

| # | 待验项 | 怎么验（便宜的做法） |
|---|---|---|
| 6 | `endProgress` 到底怎么插值（线性？还是按别的曲线） | 同一条 clip 填 `0.4` / `1.0` 各播一次，对比逐帧位置 |
| 7 | 每个资产后面那 8 字节字段（库注释写 "wtf checksum"）**引擎校不校验** | 目前只有旁证：手工建的包该字段**全 0**、而材质/贴图在实机正常（`taikou_banners.tpac` 40 个全 0）；**动画 clip 上没有直接证据** |

---

## 七、坑（本轮新增）

| # | 坑 | 现象 | 修法 |
|---|---|---|---|
| 1 | **源根位移在【骨架对象】上，不在骨骼上** | 只看 `pelvis` 的 `.location` 轨 → 以为"源没有位移" | 读 `arm.matrix_world.translation` |
| 2 | **`--pelvis ground` 静默丢水平位移** | TRF 位置轨看着有变化（竖直起伏），一实机就"回原点" | 用 `--pelvis src`；脚本现已在"源有位移却没启用 src"时告警 |
| 3 | **位移方向要过帧变换 `F`** | 朝向对了、位移却朝反方向（成对动画里就是"两人走散"） | 与旋转同一套 `F = Rot(Z,180°)` |
| 4 | **`Root/` 与 `Inplace/` 同名文件** | `--animdir` 给到父目录会随机命中 | 明确指到 `Root/` / `Inplace/` |
| 5 | **TRF 首帧必须是 0** | 首帧 ≈0.85（米级）= 绝对语义，实机整体抬高 | 导出时"帧 1 姿态烘成 rest"是引擎侧既有行为，判据只认"首帧 ≈ 0" |
| 6 | **以为"位移烘进 TRF 就完事"** | clip 能播、姿势也对，但角色**一位不动**（白跑一轮实机）| 位移轨引擎不看；必须再填 clip 的 `displacement` 字段（§3.3）|
| 7 | **`clip_usage_data` 折叠区找不到** | 在 Flags 复选框列表里来回翻 | 它**不在 Flags 里**，是检视器下方**另一个折叠栏**（§3.3 步 3）|
| 8 | **一个 flag 不构成一条路** | 勾了 `displace_position` 却没填向量 = 引擎按 (0,0,0) 推 = 等于没开 | 两个都要：flag + 向量 |
| 9 | **`endProgress` 与轨迹形状不匹配 → 滑步** | 位移量对了、方向也对，但人物脚在地上滑 | 引擎按直线线性插值，动画往往非匀速；调 `endProgress` 或把源动画位移做成匀速 |
