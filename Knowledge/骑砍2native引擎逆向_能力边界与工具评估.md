# 骑砍2 native 引擎逆向 —— 能力边界与工具评估

> 立档 2026-10-13。回答一句话问题：**TaleWorlds 的 C++ 引擎（`TaleWorlds.Native.dll`）我们到底读不读得动？**
> 结论：**读得动，比预想好得多** —— 但拿到的是**伪代码，不是源码**。

## 一、结论（先看这三条）

1. **能读。** 这个二进制**没做符号剥离**：RTTI 是开着的，能捞出 **281 个引擎自家类名**（全是原名，不是 `FUN_140001230` 那种）+ **557 条带完整参数类型的函数签名**。三台客户端（1.2.12 / 1.3.15 / 1.5.x）**一致**，不是某一版的偶然。
2. **但不是源码。** 拿到的是伪代码：变量名 / 注释 / 宏定义全无，发布版还把多个小函数**重度内联揉成一坨**。你看到的是"编译器嚼过的逻辑"。
3. **所以定一条决策规则**：**黑盒实验 5 分钟能答的，永远先做实验**；只有**实验根本答不了**的问题（"这条路到底存不存在"），才值得上 Ghidra。理由：**单个问题是小时级，不是分钟级**。

## 二、实测证据（2026-10-13）

样本 = 三台客户端的 `bin\Win64_Shipping_Client\TaleWorlds.Native.dll`

| 客户端 | 体积 | rgl 类名 | `jump_acceleration` | PDB 路径 |
|---|---|---|---|---|
| 1.2.12（备份） | 12.4 MB | 273 | 有 | 有 |
| 1.3.15（备份） | 13.5 MB | 281 | 有 | 有 |
| 1.5.x（Steam 主目录） | 13.6 MB | 281 | 有 | 有 |

x86-64 native PE。它是 `bin` 里**唯一 TaleWorlds 自家的 native DLL**（其余 native 都是第三方：PhysX / EOS / Galaxy / DLSS / FMOD）。

### 2.1 二进制里留下的是什么"路牌"

| 路牌类型 | 实测数量 | 说明 |
|---|---|---|
| RTTI 类型描述符 | **557** | MSVC 的 `.?AV…` 修饰名，**带完整函数签名和参数类型** |
| 引擎自家类名（`rgl` 前缀） | **281** | 引擎内部命名的**原名** |
| 源文件名 `.cpp` | 135 唯一 | ⚠️ 其中 128 个一扫就是第三方（fmod / PhysX），**自家的只有个位数** |
| 源文件名 `.h` | 21 唯一 | 同上，基本都是第三方 |
| 带行号的断言（`文件.cpp:行号`） | **0** | 没有这个更细的路牌 |
| PDB 路径 | 有 | `C:\BuildAgent\work\mb3\WOTS\bin\Win64_Shipping_Client\TaleWorlds.Native.pdb`（**只有路径，PDB 本体没随包发布**） |

自家 `.cpp` 全部就这 7 个：`monster_usage.cpp` · `rglEngine_interface.cpp` · `rglResource_compiler.cpp` · `rglGranite.cpp` · `rglGpu_particle_system_component.cpp` · `util-log.cpp` · `aSfxDsp.cpp`

⇒ **导航主要靠 RTTI 类名，不靠源文件名。**

### 2.2 我们卡住的问题，钥匙在不在（直接实测）

| 我们关心的问题 | 关键字符串 | 命中 |
|---|---|---|
| 飞行的「跳参 native 独占、无运行时接口」 | `jump_acceleration` / `jump_speed_limit` | ✅ 各 1 次 |
| 同上（解析它的源文件） | `monster_usage` + `monster_usage.cpp` | ✅ 20 次 + 文件名在 |
| 人形运动模式表 | `movement_sets` / `full_movement_sets` | ✅ 2 / 1 次 |
| 动作系统 | `action_set` | ✅ 5 次 |
| 自建头 / 表情的变形数据 | `deform_key` | ✅ 3 次 |
| 布娃娃 | `ragdoll` | ✅ 17 次 |

⇒ 这些**以前只能靠黑盒试的参数，现在有条索可查了**。

### 2.3 反面证据（别误判成"引擎没这功能"）

| 查不到 | 为什么 |
|---|---|
| `SetActionChannel` = 0 次 | 那是 **C# 侧方法名**，native 侧叫别的名字 |
| `non-tested code execution` = 0 次 | 那句断言在 **ModKit / 编辑器侧**的二进制里，不在游戏 DLL |
| `Bone` = 0 个类名 | 骨骼没有独立的 C++ 类，挂在 `rglSkeleton*` / `rglAnim_node*` 底下 |

## 三、能查到哪些我们关心的东西（类名索引）

全部实测捞自 1.3.15，按**我们的问题域**分：

- **动画（16）** — `rglAnim_tree`（动画树）· `rglAnim_node_layer_system`（**图层系统：通道归谁、怎么混合，全在这一类**）· `rglAnim_node_inverse_kinematics` · `rglAnim_node_human_adjustments` · `rglAnim_node_horse_adjustments` · `rglAnim_node_horse_movement` · `rglDefault_anim_system` · `rglAnimation_memory_manager_task`
- **骨架 / 模型（7）** — `rglSkeleton` · `rglSkeleton_model` · `rglSkeleton_anim` · `rglAsset_metadata_skeleton[_anim]` · `rglAsset_package_item_skeleton[_anim]`
- **变形 / 表情（3）** — `rglMorph_anim` · `rglAsset_metadata_morph_anim` · `rglAsset_package_item_morph_anim`
- **物理（19）** — `rglPhysics_engine_world` · `rglPhysics_engine_body` · `rglPhysics_engine_shape` · `rglPhysics_engine_constraint` · `rglPhysics_engine_heightfield` · `rglPhysics_material` · `rglPhysics_filter_callback`
- **角色（2）** — `rglAgent_capsule_world` · `rglAgent_capsule_world_physx`（角色的碰撞体 —— **位置 / 贴地相关的看这里**）
- **粒子（10）** — `rglGpu_particle_system` · `rglParticle_effect` · `rglParticle_emitter` · `rglParticle_system_instanced` · `rglParticle_system_interpolator_base|_definition` · `rglReplay_particle_system_data`
- **地形（5）** — `rglTerrain` · `rglTerrain_node` · `rglTerrain_renderer` · `rglTerrain_layer_instance` · `rglTerrain_shaders_generate_headers_task`
- **音效（6）** — `rglSound_manager` · `rglSound_event` · `rglFMOD_Sound_manager` · `rglFMOD_Sound_event` · `rglNull_Sound_manager` · `rglNullSound_event`
- **网格 / 材质（12 + 4）** — `rglMeta_mesh` · `rglMesh` · `rglMeta_mesh_clothing_properties` · `rglMesh_vertex_buffer_data` · `rglManaged_mesh_edit_operations` · `rglMaterial` · `rglPhysics_material`
- **相机 / 场景 / 实体** — `rglCamera_object` · `rglScene` · `rglScene_controller` · `rglScene_view` · `rglEntity` · `rglEntity_component` · `rglFlora_entity`

**资产编译器（ModKit）侧**另有一整套 `rglAsset_metadata_*` / `rglAsset_package_item_*`（geometry_file / material / metamesh / morph_anim / physics_shape / shader / skeleton / skeleton_anim / texture / gpu_particle / procedural_vector_field）—— **做资产管线时值得查**。

## 四、工具：Ghidra 干活，REA 搭桥

| 件 | 是什么 | 为什么要 |
|---|---|---|
| **Ghidra** | NSA 出的免费反汇编器（Apache 2.0） | **真正干活的是它** —— 把机器码变成伪代码 |
| **REA**（`morluto/rea`，MIT） | MCP 服务器：把自然语言问题转成 Ghidra 查询，返回**带证据**（字节区间 / 指令地址）的结论 | **搭桥** —— 没有它就得有人坐在 Ghidra 界面里自己点自己读；有它 Claude 能在会话里直接驱动、把结果读回来 |

### 安装成本与坑（都已核实）

- 要自装 **Ghidra 12.1.x + JDK 21+**（本机两样都没有）。Node 要 22.19+ / 24.11+（本机 v24.14.1 ✅ 达标）。
- **Windows 支持 = 只读 P0 / 实验性**：只覆盖 x86-64 PE 的 25 个只读操作。**"只读"够用** —— 我们本来就不改二进制。
- 🔴 **启动路径不支持命令解释器元字符** ⇒ Ghidra 工程目录**别放在含 `&` 的仓库路径下**（工程几 GB，本来也不该进仓库）。目标盘符要 **Fixed + NTFS**（H: 实测符合）。
- ⚠️ **首轮自动分析可能几十分钟起步**（13.5 MB 优化过的 C++，不算大但也不快）；之后复用工程。

## 五、复现命令（可复制粘贴）

```powershell
$enc = [System.Text.Encoding]::GetEncoding(28591)
$p = "<TaleWorlds.Native.dll 的完整路径>"
$t = $enc.GetString([System.IO.File]::ReadAllBytes($p))

# 1) 引擎自家类名
[regex]::Matches($t,'\.\?AV(rgl[A-Za-z0-9_]{2,50})@@') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique

# 2) 源文件名 / PDB 路径
[regex]::Matches($t,'[A-Za-z0-9_\-]{3,60}\.cpp') | ForEach-Object { $_.Value } | Sort-Object -Unique
[regex]::Matches($t,'[A-Za-z]:\\[^\x00]{0,120}\.pdb') | ForEach-Object { $_.Value }

# 3) 某关键字在不在（找线索入口）
([regex]::Matches($t,'jump_acceleration')).Count
```

⚠️ 必须用 `[System.Text.Encoding]::GetEncoding(28591)`（Latin1）—— **PowerShell 5.1 没有** `[System.Text.Encoding]::Latin1`（那是 .NET Core 才有的），用错会**静默返回 null、所有计数变 0**，看起来像"什么都没找到"（本次立档时就先踩了这一下）。

## 六、明确不做 / 已知坑

- ❌ **.NET 侧继续用 `ilspycmd`，别换。** REA **不反编译**（只出元数据 / CIL 清单）—— 比 ilspycmd 弱。REA 在 .NET 侧**唯一**有用的东西是 `inspect_managed_native_boundaries`（列出**哪些托管方法声明了 native 调用**），那是"该去 Ghidra 追哪个函数"的路标。
- ❌ REA 文档里的 `harmony-analysis.md` 是**华为鸿蒙**，与 .NET 的 Harmony 补丁库**同名撞车**，别被文件名骗。
- ❌ 飞行的「9 条路全死」结论**不受影响** —— 那些是**实机实测**结论；Ghidra 只是**多给一条**"查证引擎侧到底有没有接口"的路，**不推翻**已有实测。

## 七、将来真开工的顺序

1. **先只装 npm 包**（几 MB，不下载 Ghidra）：跑 `inspect_managed_native_boundaries` 拿「托管 → native 调用索引」。零重装成本，产出的索引本身就是"该追哪个 native 函数"的路标。
2. **再装 Ghidra**，**首跑必须是"已知答案的验证题"**：拿我们黑盒试出来的结论去对（例如"agent 位置 Z 写不进去"），**对上了这条线才可信**。
3. 之后才打真题。**最有价值的两道**：
   - 飞行的「`jump_acceleration` / `jump_speed_limit` 到底有没有可写路径」← 字符串已在二进制里、`monster_usage.cpp` 文件名也在，**条索最清楚的一道**
   - `rglAnim_node_layer_system` 的**图层混合逻辑**（通道归谁、为什么会每帧互踢）—— 解释动画状态机那类抖动的根因
