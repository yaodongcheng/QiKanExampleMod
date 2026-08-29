# 图片资源打包进 TPAC 并在游戏引擎内读取 —— 操作指南

> 用途：把一张（或多张）图片变成骑砍2 引擎能读的资源包（mod），替换游戏内同名贴图资源。
> 示例场景：换脸 mod（本项目 `tools/face-pipeline/`）。
> 状态：已在 v1.2.12 原版实测跑通（A 包框架验证 + 全红/全黑/全绿/星图探针全部生效）；
> 2026-08-29。

## 结论先行

- **覆盖机制**：引擎无覆盖清单。mod 的 `AssetPackages/*.tpac` 里放**与目标同名**的资源
  （如 `head_male_a_d`），排 Native 之后加载 = 自动替换原生同名资源。SubModule.xml 只负责注册 mod。
- **可换层**：diffuse/normal/specular 三张独立（`_d/_n/_s`）。**只换 `_d`（diffuse)**
  保留原版法线/高光最稳（形变/光泽不走样）。
- **不用动原版文件**、不用新档（新贴图对即时生成角色立刻生效；见"**脸缓存**"）。

## 完整链路（六步）

| 步骤 | 工具 | 说明 |
|---|---|---|
| 1. 生成图片 | 任意（这里=拼脸 Python 管线） | 输出 2048/4096 的 PNG（注意 UV 布局必须对齐目标资源，见"校准"章） |
| 2. 转 DDS | `texconv in.png -ft dds -f BC1_UNORM -m 12 -y` | 格式/层数要与目标一致；男头原版=2048/DXT1/12 mips；织丰头=4096/DXT1/13 mips |
| 3. 打包 tpac | `tpaccli makeface --packdir <模板包> --out <mod目录> --name <资源名> --diffuse x.dds [--normal --spec]` | 必须以**已存在的可读纹理对象**（模板，如 GT_Face pack0 里的 2048 纹理）为骨架拷字段，不能从零 new；见"五个坑" |
| 4. 写 SubModule.xml | 手写/拷贝 | 见 `tools/face-pipeline/dist/FaceCustomLWN/SubModule.xml`（依赖 Native e1.0.1 以上即可跨版本） |
| 5. 装游戏 | 复制到 `Modules/<ModName>/` | 启动器勾选，排在 Native 之后 |
| 6. 验证 | 游戏内看；配合"探针法"定位问题 | 见"校准" |

## 五个坑（全踩过）

1. **模板字段复制**：`AssetItem.Clone()` 没实现（抛 NotImplemented）——必须逐字段拷贝
   （`Width/Height/MipmapCount/ArrayCount/Format/Unknown*/Flags/SystemFlags/GeneratedAssets/UnknownUlong2`
   等，源代码在 `TpacToolCLI/Program.cs` `MakeFace()`）。字段漏了 = 崩/读不出来。
2. **`Source` 字段必须清空**：从模板继承了 `$BASE/Modules/...` 外壳路径，引擎可能按路径校验。
   设 `""` 最稳。
3. **`MipmapCount` 与数据层数一致**：texconv 输出的 mip 数（-m 12）与 `MipmapCount` 字段、以及
   `TexturePixelData.UserData[KEY_MIPMAP]` 三处一致——不一致=越界读=崩溃（首次 B 包崩溃根因之一）。
4. **数据段= 压缩存储**：tpac 数据段有压缩，147KB 包装 2.8MB 数据正常——**别以文件大小判"数据缺失"**。
   验证用 `tpaccli dump --packdir <mod>/AssetPackages --filter 名字 --out .`（回读成像素，能出图=数据完整）。
5. **Bash 视图分叉**：Claude Code 的 Bash 是隔离视图，Bash 产出的文件宿主（IDE/游戏）不可见。
   **交付/装游戏必须走 PowerShell（或 Write/Read）**。

## 探针法（校准换脸坐标的通用手段）

- **纯色探针**（红/黑/绿）：验证"这个资源名有没有被引擎用"——主角全脸红/黑 = 覆盖率 100% 生效。
- **网格探针**（0.1 黑线+白底）：验证"网格在哪些 3D 区域显示" → 判断"我们的贴图内容覆盖哪些位置"。
- **UV 星图**（每像素 R=u, G=v + 黑线）：**给引擎算 UV 的最终手段**——截图上读任何部位的
  (R,G)=(u,v)。比"解包网格再软渲染"精确（引擎的渲染/UV 展开与工具导出的不完全一致，必须实机校准）。
- **蓝块描边**：标记目标矩形周围，验证坐标差方向（蓝色出现在额头= v 偏大需下移等）。
- **A/B 包法**：A 包=不覆盖任何东西的超窄资源（验证 mod 框架本身能不能进游戏）；B 包=真覆盖。

## 已知未决

- **面部"合成五官层"**：男头表面的眼睛/嘴/眉等仍显示引擎合成纹理（颜色探针全部生效、拼脸五官块被
  挤到额头/下颌= 基础贴图 UV 布局与软渲染不一致——**以星图实机读数为准**（2026-08-29 进行中）。
- **脸缓存**：`face_mesh_cache="true"` NPC（织丰领主们）旧存档脸固化；主角/新档实时生成。
- **织丰适配**：男性 skin face_meta_mesh=`sho_head_male_japanese`（自建 4096 贴图
  `2sho_head_male_japanese_d`）——换脸需覆盖**织丰自己的资源名**（本卡按原版 head_male_a 设计）。

## 关键文件速查

- CLI 全部命令：`tools/face-pipeline/README.md`
- makeface 源代码骨架：`tpactool/TpacToolCLI/Program.cs`（MakeFace/ParseDds/roundtrip）
- 有效参考包：`Modules/GT_Face/AssetPackages/pack0.tpac`（换脸 mod 卡，模板纹理来源）
- 打包产物示例：`tools/face-pipeline/dist/FaceCustomLWN/`
- 系统知识：`Knowledge/脸部系统分析.md`
