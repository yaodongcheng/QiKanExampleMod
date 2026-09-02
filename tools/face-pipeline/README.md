# face-pipeline — 骑砍2 换脸管线工具集

> **状态（2026-09-02）**：管线转型完成——目标从「拼脸贴图替换」（判定不可达）转为
> **「xxFemaleHead 式自建素材包 + skins.xslt 全量接管」**（机制破译见
> `Knowledge/脸部系统分析.md` §十 破译档案 / §十一 五步换脸验证战略）。
> 第一步（解包→重打包管线验证）已完成；命令/脚本变更以本文件为准。

## 目录

```
tools/face-pipeline/
  tpactool/          # TpacTool(第三方 MIT) + TpacToolCLI(我方管线 CLI) —— 完整源码
  scripts/           # Python 渲染/测量/烘焙脚本
  data/              # 参考数据(网格 obj/对照图/素材)——历史产物, 不入 git
```

## 🔴 核心代码资产（勿删，缺一即断链）

| 路径 | 职责 |
|---|---|
| `tpactool/TpacToolCLI/Program.cs` | CLI 入口（list/dump/inspect/makepack/roundtrip/assetclone） |
| `tpactool/TpacToolCLI/AssetClone.cs` | **assetclone**：依赖图深拷贝改名重指（mesh+材质+贴图 → lwn_* 自包含包）+ raw 元数据 GUID 补丁 |
| `tpactool/TpacToolCLI/MakePack.cs` | PNG→DXT5 贴图→tpac（manifest 驱动；确定性 GUID = 名字 hash） |
| `tpactool/TpacToolCLI/Bc3Encoder.cs` | BC3/DXT5 编码器（stb_dxt 移植，4 色模式，无 NuGet 依赖） |
| `tpactool/TpacTool.Lib/**` | 包解析库 + **我方补丁**：`AssetItem.RawMeta`（捕获原文）、`AssetPackage`（RawMeta 直写+解析不匹配兜底）、`Material.cs`（WriteMetadata/SubVersion 补全）、`Metamesh.cs`（CloneVersion）、`Mesh.cs`（计数元数据 public）、`Utils.cs`（WriteVec4）、BigGustave public 化 |
| `tpactool/TpacTool.IO/` | 导出器（obj/fbx/png/材质 .mat.txt） |
| `scripts/render_uvview.py` | 正脸视口→UV 色图（烘焙校准源；轴模式参数化：男 xy / 女 zx） |
| `scripts/render_head.py` | 贴图→头网格预览（校准回显） |
| `scripts/bake_pool2.py` | 女池「投影烘焙」生成器（第 2 步素材替换主产线） |
| `scripts/decode_miniface.py` | TK5 MINIFACE DDS→PNG 解码（素材源） |
| `scripts/geo_anchor*.py / anchor_v2.py / uv_pick*.py / warp_face.py / warp_sho_face.py / find_eyes.py / make_anchor_map.py / plot_uv.py` | 五官 UV 锚点测量族（男/织丰/女头通用） |

## CLI 命令速查（`dotnet build -c Release` → `bin/Release/net9.0/tpaccli.exe`）

```bash
tpaccli list     --packdir <dir> [,<dir2>] [--filter s]      # 列出包内资源
tpaccli dump     --packdir <dir> --filter s --out d [--format obj|fbx|png|dds]
tpaccli inspect  --packdir <dir> --filter s                  # 纹理全字段(模板提取用)
tpaccli roundtrip --packdir <dir> --filter pack0.tpac --out d  # 解包→原样重打包(md5/解出对照)
tpaccli makepack --manifest <json> --out <dir>               # PNG→DXT5→tpac
# ★ assetclone: 从任意包克隆「网格+依赖材质+贴图」→ lwn_* 自包含包
tpaccli assetclone --packdir <a>,<b> --src <name> --newname <lwn_name> \
                   [--extra <name>...] --out <dir> [--packname x.tpac]
```

## 已确定的关键事实（管线设计依据，全部实测）

1. **贴图渲染链**：基底(basemesh d/n/s) → 脸皮 diffuse → **FaceGen 运行时合成槽**（材质 tex[10/11]，
   五官层；只涂 diffuse 底色看不到五官）；眼球/嘴 = 头网格子网格+独立材质。
2. **xxFemaleHead 万能包配方**：自建头网格 + 5 池材质（shader `8c88213c-…`、槽位 (d,d,n,n,s)）+
   5 张「整脸画在 UV 上」的池 diffuse（眼/眉/鼻/嘴/耳直接画，脸区居中）+
   XSLT 对女性皮肤**全量接管**（头/眉/池/发/纹面/眼色/**deform_keys+constraints 逐皮肤重写**）。
   资源名与来源 mod 无关 → 原版/织丰通吃。
3. **skins 机制**：`skins.xml`（可空壳）+ 同名 `skins.xslt` -> 引擎把**前载模块合并后的整库**交给
   xslt 变换；`project.mbproj` 注册 `type="skin"`。XSLT 不改自己的空壳文件。
4. **打包保险三件套**（人工新建贴图时）：`Source=""`、MipmapCount 与数据层一致（13 级织丰头）、
   字段随模板（织丰 pack6 实例）。
5. **库缺陷与对策**：TpacTool.Lib 对部分老纹理元数据解析越界（227 类「meta parse skip」）→
   **RawMeta 捕获 + Save 原样直写**（只改名字索引区 + 依赖 GUID 二进制替换）；`EditmodeMiscData`
   库无写回 → 克隆时剔除。**任何新产物按 `roundtrip → 解出比对` 做回归**。
6. **覆盖机制**：同名资源后载覆盖先载（引擎按名字全局唯一）；女头 mesh 材质引用 = GUID，
   `Mesh.Material/SecondMaterial` 均为 AssetDependence（可重指）。

## 五步验证战略（进度指针 → Knowledge §十一）

1. ✅ 解包→重打包管线验证（原封不动，资源名不改）——女包已装 `ShokuhoTaikouExpansionPack`。
2. ⏳ 只换贴图不动资源名（bake_pool2 产物 → 同名单覆盖）。
3. ⏳ 改资源名重打包（assetclone lwn_* 全命名）——证明自家管线跑通。
4. ⏳ 男头替换（信长脸；织丰男头网格 + nobunaga.png 母版）。
5. ⏳ 包体不膨胀 + 每个人有自己面孔（贴图之外的面部参数研究，如 deform_keys/池依赖）。

## 许可证与来源

- TpacTool：MIT，Copyright (c) 2020-2022, szszss（github.com/szszss/TpacTool，上游 hunharibo v0.4.0）。
- 反编译/实测：Native/织丰/GT_Face/WomeninCalradia/xxFemaleHead 实文件。
