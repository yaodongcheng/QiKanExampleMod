# face-pipeline — 骑砍2 换脸管线工具集

> 用途：把「任意正脸参考图像」变换成骑砍2 主角/NPC 用的脸部贴图资源包（资源覆盖型 mod），
> 保留原生五官动画（morph 顶点动画，不参与修改）。
> 状态：工具链已打通（解包/打包/渲染预览/UV 锚点）。warp 生成器（参考图 → 男 UV 贴图）为待接入 TODO。

## 目录

```
tools/face-pipeline/
  tpactool/          # TpacTool 源码（第三方，MIT，作者 szszss/hunharibo）+ TpacToolCLI（本项目扩编的解包/打包 CLI）
  scripts/           # Python 分析/渲染脚本
  data/
    meshes/head_male_a.obj   # 从 Native AssetPackages 导出的男头网格（顶点+UV+索引）
    uv_layout.png            # 男头 UV 布局（按 submesh 着色）
    uvview.png               # UV 着色渲染图（像素色值 = 该处 UV 坐标）
    front_female_on_male.png # 对照实验：女贴图贴男头（证明男女 UV 布局不同）
```

## 前置

- dotnet SDK ≥ 9（构建 tpaccli）
- Python 3.12 + `pip install mediapipe numpy pillow`（warp 阶段需要 mediapipe；分析脚本只需 numpy+pil）

## 快速开始

```bash
# 1) 构建 CLI（一次）
cd tools/face-pipeline/tpactool/TpacToolCLI && dotnet build

# 2) 列出/导出资源（示例：从 1.2.12 native 包导出男头贴图与网格）
dotnet run --no-build -- list   --packdir "<Native AssetPackages 路径>"
dotnet run --no-build -- dump   --packdir "<...>" --filter "head_male_a" --out "<dest>" --format obj
dotnet run --no-build -- dump   --packdir "<...>" --filter "head_male_a_d" --out "<dest>"
# 注意：原版贴图像素在私有 Bannerlord.gts（Graphine）里，TpacTool 解不出 → 换脸贴图全新建，不依赖原版像素。

# 3) 打包往返验证（关键：确认修改后的包引擎能读）
dotnet run --no-build -- roundtrip --packdir "<某 mod 的 AssetPackages>" --filter "pack0.tpac" --out "<dest>"
# 已验证：对 GT_Face 全包重写后字节数一致、回读 16/16 项。（打包 = AssetPackage.Save()，不依赖官方编辑器）

# 4) 渲染/锚点脚本（python，路径已指向 data/）
python scripts/render_uvview.py   # UV 着色渲染（读色值 = UV 坐标）
python scripts/render_head.py <纹理.png> <out.png> [skin|all]  # 带贴图正脸预览（迭代校准用）
python scripts/plot_uv.py         # UV 布局图
python scripts/geo_anchor.py      # 几何→UV 锚点（打印 + frontal_uv.csv）
python scripts/anchor_v2.py       # 锚点 v2（当前最优，输出五官紫 UV 均值）
```

## 已确定的关键事实（管线设计依据）

1. **脸部 = 一张 2048 贴图**：头网格（8 submesh：4 级 LOD + 脸皮 + 附属件）引用 `head_male_a_d/_n/_s` 三贴图（diffuse/normal/specular），加上独立眼球贴图 `eye_a_d`、独立眉毛网格（`male_eyebrow_N`，可选件）。
2. **五官能动 = morph 顶点动画**（`morph_anims.tpac` 的 `face_01~12` 等 clip，`Agent.SetAgentFacialAnimation(channel, name, loop)` 播放，通道为 High/Low）。变的是网格顶点，贴图跟随——换脸贴图不破坏动画。
3. **男头脸区在贴图左下角**（u 0.01–0.20, v 0.02–0.36），与女头布局（脸区居中）完全不同 → 锚点必须走几何提取（scripts/anchor_v2.py）。
4. **锚点表**（男头，UV 空间，来自 anchor_v2 输出）：

| 五官 | UV 中心 | 范围 |
|---|---|---|
| 眼（左右共用带） | (0.138, 0.288) | u[0.063,0.197] v[0.271,0.306] |
| 眉 | (0.11, 0.34) | u[0.06,0.22] v[0.32,0.36] |
| 鼻尖 | (0.021, 0.236) | u[0.013,0.030] v[0.236,0.237] |
| 嘴 | (0.060, 0.190) | u[0.023,0.084] v[0.170,0.228] |
| 下巴 | (0.135, 0.089) | v[0.024,0.136] |

5. **覆盖机制**：mod 的 `AssetPackages/*.tpac` 里放**同名资源**（如 `head_male_a_d`）→ 按加载顺序覆盖 Native。范本：已装的 `GT_Face`（换女头）、`WomeninCalradia`（换全部头，`head_malfoy_a_*`）。**给单个角色专用脸**需要自建 skin/race 拆分（后续接线，见 TODO）。

## TODO（warp 生成器）

- `warp_face.py`：mediapipe(468点) landmark → 参考图五官 5 点 → 对齐男 UV 锚点（上表）→ 仿射 + 薄板样条嵌入脸区（大 UV 岛）→ 肤色渐变填边 → 输出 diffuse 2048。
- normal 生成（Sobel+噪声；或中性法线兜底）。spec 直接沿用/白图。
- 打包脚本：用 tpaccli 的 Save API 把新贴图写进 `AssetPackages/*.tpac`（1.2.12 织丰环境验证用）。
- 引擎实测：游戏内跑一次，对照截图微调锚点（校准循环 = render_head.py 软渲染 + 人工看图迭代）。

## 许可证与来源

- TpacTool：MIT，Copyright (c) 2020-2022, szszss（仓库：github.com/szszss/TpacTool，上游 hunharibo/TpacTool v0.4.0）。
- 网格数据：`MB2_Version/MB2_1.2.12/.../Modules/Native/AssetPackages/`（TpacTool 导出）。
- 对照贴图样本：`GT_Face` mod（mod 内贴图，仅作坐标系对照用，未并入输出包）。
