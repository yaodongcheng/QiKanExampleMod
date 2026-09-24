# ue-dissect —— UE 工程拆解工具链

> **一句话**：把任意 UE 工程的**蓝图 / 数据表 / 动画 / 粒子 / UI** 拆成文本与可读详情 —— **不需要任何第三方反编译工具**。
> **威力来源**：引擎自带的 `ObjectExporterT3D`（把资产的「带标签属性」整棵写成文本）。等于让引擎自己把二进制读出来。

首次落地案例：`FlexibleCombatSystem`（UE4.27）—— 2396 个资产零失败，产物见 `Debug/offline/fcs_dump/`，
结论文档见 `Knowledge/FlexibleCombatSystem施法设计_UE实现分析.md` 与 `Knowledge/骑砍2粒子系统.md` §八/§十一。

## 🔴 定位：这是**全仓唯一**的 UE 资产导出/解析底座（2026-09-24 合并）

| 谁 | 用它的什么 | 各自的领域层（不在这里） |
|---|---|---|
| `tools/particle-pipeline/` | T3D 数据（`out/t3d/vfx/`，超集）+ 解析底座 `t3d_tools.py`（`walk_compat` / `decode_rapid_iteration`） | Niagara/Cascade → 骑砍粒子 XML 的映射（`ue2bannerlord.py` / `gen_particle_effect.py` / `validate_xml.py` / `preview`） |
| `tools/anim-retarget/` | 无（走 FBX 路线）；共享「UE 无头启动 + 资产过滤 + trace 日志」的做法 | Blender 重定向 → ModKit FBX + TRF、viewer |
| `Debug/offline/fcs_dump/` | 就是它的产物目录（不进 git） | — |

**规矩**：UE 侧要新加导出（导什么资产、导成什么格式）→ 加在这里；各管线的「翻译成骑砍资产」→ 留在各自工具链。
**已退役**：`particle-pipeline/pipeline/export_t3d_all.py`（与 `export_t3d.py` 重复，保留仅作历史记录）。

---

## 为什么需要它（三条硬事实）

| 需求 | 常规做法为什么不行 | 本工具链怎么解 |
|---|---|---|
| 读蓝图变量/节点/枚举值 | UE Python 在命令行下**读不到**（属性没标 `EditAnywhere`，反射拒绝）；`unreal.EditorAssetLibrary` 还可能因缺插件不存在 | **T3D 文本导出**不受此限，节点函数引用、引脚默认值与连线全在 |
| 读数据表每一格的值 | Python 读不到行数据；工程里常有没装 CSV 导出器 | 从 `.uasset/.uexp` 正则挖**字段全名**（`字段_序号_GUID`）→ 喂 `get_data_table_column_as_string` |
| 读 Niagara 模块参数 | 参数不在引脚上 | 解 `RapidIterationParameters`：偏移表 + 字节流，按类型索引解码 |

## 流程（五步）

```bash
# 0) 配置：改 paths.py，或设环境变量 UE_PROJECT / UE_ENGINE / UE_DUMP / UE_MIRROR
python tools/ue-dissect/paths.py

# 1) 全量 T3D 导出（在 UE 里跑，无头）—— 见下方「怎么调起来」
python tools/ue-dissect/export_t3d.py            # 由 UE 执行，不是普通 python

# 2) 资产清单 / 依赖 / 枚举 / 结构体 / 数据表 / 蓝图 / 动画 / 控件 / 粒子（分阶段可挑）
FCS_PHASES=enums,structs,datatables python tools/ue-dissect/dump_assets.py

# 3) 数据表取值 → CSV
python tools/ue-dissect/table_columns.py
python tools/ue-dissect/tables_to_csv.py

# 4) 人读详情树（镜像工程目录结构，一资产一 .md，含伪代码 + 引脚级明细）
python tools/ue-dissect/detail_tree.py           # 加 --no-pins 出薄版（3MB vs 21MB）

# 5) 粒子逐 emitter 拆解
python tools/ue-dissect/vfx_breakdown.py         # Niagara（系统→emitter→模块→参数值→材质→贴图）
python tools/ue-dissect/cascade_breakdown.py     # Cascade（老粒子，结构更接近骑砍）
```

### 怎么把脚本交给 UE 跑（内联 bootstrap，绕开 UE 只认代码串的问题）

```powershell
$ue = "D:\UNREAL\UE_4.27\Engine\Binaries\Win64\UE4Editor-Cmd.exe"   # 路径别写错版本
$proj = "D:\UEProjects\<工程>\<工程>.uproject"
$py = "H:/.../tools/ue-dissect/export_t3d.py"
$code = "exec(open(r'$py', encoding='utf-8-sig').read())"
& $ue "`"$proj`"" -run=PythonScript -script="$code" -unattended -nosplash -nullrhi -nopause -stdout -log
```

## 脚本清单

| 脚本 | 跑在哪 | 干什么 |
|---|---|---|
| `paths.py` | 本地 | **唯一要改的配置**（工程/引擎/产物根/镜像根） |
| `export_t3d.py` | **UE 内** | 全量 T3D 导出 + 数据表取值（主力） |
| `dump_assets.py` | **UE 内** | 分阶段导出：清单/依赖/枚举/结构体/数据表/蓝图/动画/控件/粒子/声音/网格/材质 |
| `t3d_tools.py` | 本地 | T3D 解析器：`graph` 执行线伪代码 / `dataflow` 纯函数公式 / `node_pin_detail` 引脚级明细 |
| `detail_tree.py` | 本地 | 生成「详情树」：镜像工程目录，一资产一 .md |
| `vfx_breakdown.py` | 本地 | Niagara 逐系统拆解（含参数值解码） |
| `cascade_breakdown.py` | 本地 | Cascade 逐系统拆解 + 模块/分布普查 |
| `table_columns.py` / `tables_to_csv.py` | 本地 | 数据表取值与转 CSV |
| `pseudocode.py` | 本地 | 关键蓝图 → 伪代码全文包 |
| `digest.py` / `digest_anim.py` | 本地 | 蓝图摘要 / 动画通知摘要 |

## 四条军规（全是踩出来的，别重犯）

1. 🔴 **脚本文件必须 UTF-8 无 BOM** —— PowerShell `Set-Content -Encoding utf8` 会加 BOM，UE 执行时报
   `invalid character in identifier`。读的时候用 `encoding='utf-8-sig'` 兜底。
2. 🔴 **T3D 是"两遍结构"** —— 先声明（`Begin Object Class=X Name=Y` 空壳）后按名重开填属性；
   解析必须**按名合并**，取首现会拿到空对象。
3. 🔴 **K2Node 名字是"按图局部"的** —— 同一个包里 `K2Node_CallFunction_0` 会出现 22 次；
   **必须按 EdGraph 子树作用域合并**，全局合并会把不同函数的引脚搅在一起。
4. 🔴 **纯函数没有 exec 引脚** —— 沿执行线走读不到；读公式要用**数据流视图**（看每个输入引脚的字面量/来源节点）。

## 换一个 UE 工程要改什么

1. `paths.py` 的 `UE_PROJECT` / `UE_ENGINE`（或环境变量）—— 其余照旧。
2. 蓝图不在 `/Game/<子目录>/` 下 → 改 `CONTENT_SUBDIR`。
3. UE 版本不同 → `export_t3d.py` 里 `EngineVersion.VER_UE4_27` 改成对应版本。
4. 只想拆一部分 → `dump_assets.py` 支持 `FCS_PHASES` 环境变量挑阶段（见脚本头）。

## 已知边界（拿结果前先知道）

| 项 | 状态 |
|---|---|
| **SCS 组件模板**（组件挂点/层级/初始变换） | ❌ T3D 导不出 → 详情页的「用到的组件」是**引用闭包推断** |
| Niagara **曲线关键帧**、骨骼 **socket 坐标** | ❌ 未解（数据在 DataInterface 字节里 / 不在 T3D 里） |
| 伪代码 | ⚠️ 是**线性化导读**（按 exec 连线展开），不是字节级反编译；要咬死行为回 T3D 原文 |
| 大工程体量 | ⚠️ T3D 体积约 3MB/资产（Node 密集的蓝图更大）；2396 个资产 ≈ 577MB。产物放 `Debug/offline/`，别进 git |

## 产物落在哪

- **原始 T3D / 解析 JSON / CSV** → `DUMP_ROOT`（默认 `Debug/offline/fcs_dump/out/`，**不进 git**）
- **人读详情树** → `MIRROR_ROOT`（默认 `Knowledge/FCS详情解析/`）——
  ⚠️ 本仓库已把该目录 **gitignore**（理由：可由本工具链完全重生成，410 页 ≈ 21MB 没必要进库；
  薄版 `--no-pins` ≈ 3MB）。**换仓库用时按需决定是否入库**
- 脚本本身 → 本目录（进 git）
