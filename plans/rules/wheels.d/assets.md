# 3D 资产轮子（外部模型 → 骑砍资产，2026-09-14 登记）

> **场景**：把外部资产（别的游戏解包的角色/甲/武器、自己建的模型）做成骑砍能用的 tpac；
> 或排查「网格不跟骨架动 / 编辑器一导入就崩 / 材质找不到 / 穿反了」。
> **来源**：换头工程（[Knowledge/蒂法换头工程.md](../../../Knowledge/蒂法换头工程.md)）+ 盔甲工程（[Knowledge/骑砍2盔甲资产工程.md](../../../Knowledge/骑砍2盔甲资产工程.md)）两轮实战。
> **操作手册**：[tools/armor-pipeline/README.md](../../../tools/armor-pipeline/README.md)（一条命令 + 参数表 + 17 条坑表）。
> **工具**：[tools/armor-pipeline/](../../../tools/armor-pipeline/)（甲）、[tools/face-pipeline/](../../../tools/face-pipeline/)（头/通用）。

---

## 一、外部绑定模型 → 骑砍骨架（重定向 / retarget）——**换任何源游戏都适用**

**解决**：源模型的骨架是自己那套（骨名无意义，如 `bone_0..123`），要搬到骑砍 28 骨 `human_skeleton` 上。
四个天然障碍：**单位不同**（源常是厘米）、**姿态不同**（源常 T-pose，骑砍 A-pose）、**比例不同**、**朝向可能相反**。

**做法**（范本 `tools/armor-pipeline/scripts/build_armor.py`，一行公式）：

```
v' = Σᵢ wᵢ · ( T_bl[k(i)] · (MIRROR · v) )
T_bl[b] = Translate(骑砍骨头) · Rot(仅手臂) · S · Translate(-(MIRROR · 源骨头))
```

- `wᵢ` = **顶点原有的蒙皮权重，原样保留，只换骨名**（不重算权重）
- `S` = 缩放，**拆「径向 / 沿骨轴」两个方向**（见裁定 3）

### 四条实测裁定（每条都是血换的，别凭直觉推翻）

| # | 裁定 | 为什么 |
|---|---|---|
| 1 | 🔴 **源与骑砍"正面"相反时用 Y 轴镜像，不用 180° 绕 Z 旋转** | 旋转会**连左右一起翻**。镜像是反射（行列式 −1），**做完必须反转面绕序**否则法线朝里。判定法：看脚趾/面部骨相对脚踝在 ±Y 哪一侧 |
| 2 | 🔴 **旋转只给手臂链推，其余骨一律不转** | 两侧都有"朝向信息是垃圾"的骨：源侧骨盆骨的最长子骨是腿根（头→子骨得到"朝下"，与朝上的 `spine_9` 差 **147.7°**）；骑砍 `bone.matrix_local` 的**骨轴不沿肢体**（`spine_9` 头 z=1.0064、尾 y=+0.157 指向正前方）。给每根骨都推 = 垃圾朝向互相打架 = 甲扭麻花 |
| 3 | 🔴 **手臂缩放必须拆「径向 / 沿骨轴」** | 各向同性放大会把手臂**同时拉长**：前臂 25.97cm × 0.0165 = 0.429m，而骑砍肘→腕只有 0.267m → **籠手末端超出拳头 16cm**。沿轴改用**解剖段长**（两边关节坐标实时求比值），径向才用手调值 |
| 4 | **比例差用「躯干一个 R + 手臂一个 R」吸收，不做逐骨 λ** | 逐骨拉伸在关节处产生剪切（试过：顶点被吹到几十米外）。判定法：逐 z 带量「甲半径 vs 身体半径」，甲必须**处处更大**（`Debug/offline/armor_probe/_fitcheck.py`） |

### 骨映射（按**关节坐标**对，不按名字）

骑砍 28 骨的名字后缀就是引擎骨索引（`bip01_pelvis_0` … `bip01_r_finger0_27`）。
完整映射表见 [Knowledge/骑砍2盔甲资产工程.md](../../../Knowledge/骑砍2盔甲资产工程.md) §1。
- **左右按 x 符号对齐**：源 −x 侧配骑砍 `l_*`
- 骑砍把上臂/前臂**各拆两根**（`upperarm_twist_15`+`twist1_16` / `foretwist_17`+`foretwist1_18`），源只有一根 → **映射到前一根即可**（同轴，位置正确）
- 源件的**布料骨**（随便什么名字）走**父链兜底**：往上找到第一根已映射的骨

### 源的"部位识别"（别靠肉眼猜）

逐件单显渲染 + **按 UV 采样贴图颜色聚类**（程序判定）。范本 `Debug/offline/armor_probe/partmap_solo.py` / `classify_sw2.py`。
🔴 **排武器件不能只按子网格号**：`submesh_1`（衣服）与 `submesh_1.001`（矛柄）解析出同一个号 → 判据改用**材质名 `mat_w_*` + 有无 UV**。

---

## 二、编辑器侧（人做，Claude 做不到的那半）

| # | 事项 | 漏了的症状 |
|---|---|---|
| 1 | 导入 FBX：`Convert to unit = m` ／ **不勾** Z-up ／ **只勾 Import meshes** | 勾 `Import skeletons` 会建**重复骨架资产**跟游戏自带的 `human_skeleton` 撞名 |
| 2 | 🔴 **材质勾 `Vertex Layout → Bumpmap` + `Skinning`** | **甲的位置大小全对，就是不跟骨架动**。它是**材质**属性，FBX 不携带，**重导必重勾** |
| 3 | 贴图挂三槽：`tex[0]=_d` / `tex[2]=_n` / `tex[4]=_s`，法线的纹理类型选 **Normal Map** | 金属感/表面细节不对 |
| 4 | **Publish 目标选模块外** | Publish **会清空目标目录**；选模块内还会得到 `Modules/X/X/` 嵌套 |
| 5 | 产出的 `pack0.tpac` 拷进目标模块的 `AssetPackages/` | 游戏读不到 |
| 6 | 玩之前让模块处于**游戏模式**（`Assets` 必须改名 `Assets_disabled`） | 引擎按 `Assets`→`AssetPackages`→… 取**第一个存在的**，空 `Assets/` 会**遮蔽全部 tpac** |

### 🔴 资产命名关系（tpaccli list 实测）

```
编辑器写入的文件名         资产**内部名**
<名>_geo.tpac      ←→     <名>          （网格）
<名>_mtl.tpac      ←→     <名>          （材质）
<名>_d_tex.tpac    ←→     <名>_d        （贴图）
```

**`_geo` / `_mtl` / `_tex` 是文件名后缀，不是资产名。** 网格与材质**本来就同名**（靠类型 GUID 区分）。
⇒ **FBX 里的材质名必须 = 网格名**，编辑器靠名字把两者对上；对不上就每个 LOD 弹一次
`RGL CONTENT WARNING: Unable to find material for mesh <名字>`。

---

## 三、通用工具（两个工程共用）

| 工具 | 用途 |
|---|---|
| `tpaccli list/dump/metaparts/morphinfo/makepack/assetclone/inspect` | 读写 tpac：看资产名、量 LOD/骨骼/bbox、导 FBX（**带逐顶点权重**）、打包贴图 |
| **二进制 grep 定位资源** | `grep -c -a "<网格名>" <各模块>/AssetPackages/*.tpac` —— **0 命中 = 肯定不在；≥1 = 在**。先于开工具，秒级 |
| `fbx_probe.py` | **关卡 1**：进编辑器前验导出规格（`USF=100 / UpAxis=2 / 网格节点零变换`） |
| 硬链接单文件加载 | `AssetManager.Load(dir)` 会载入目录下**所有** tpac → 用只含目标包的硬链接目录当 `--packdir`（2.3GB 包加载 0.2 秒） |
| `png_for_editor.py` | 进工程源的 PNG 必须 **8bit RGB + 只有 IHDR/IDAT/IEND**；带附加块/alpha 会被编辑器**连源图带产物一起删** |

---

## 四、验证手法（省时间的关键）

| 手法 | 用途 |
|---|---|
| **把底色当自发光渲染** | 干净 = 贴图/UV 没问题，毛病在光照或网格重叠。**怀疑"贴图坏了"时第一件事** |
| **渲染前 `hide_render` 掉非目标 LOD** | 多 LOD 网格位置重合，未挂材质的会带"指向失效文件的贴图节点"→ Blender 用**品红**画，看起来像贴图坏了 |
| **渲染脚本从包围盒自动取景** | 写死取景会把 A-pose 伸出去的四肢裁掉 → 看起来像"缺件"，其实是画框外 |
| **诊断脚本** | `_armzoom.py`（怼近看某部位：甲单独 vs 甲+身体且身体染色）/ `_fitcheck.py`（逐 z 带量甲/身体半径） |

---

## 五、踩过的坑（按症状查）

| 症状 | 根因 |
|---|---|
| 甲被转到几十米外、bbox 爆表 | 源侧有长度 0.01 的"空节点"骨（如脚趾），逐骨 λ 算出来 900+ |
| 躯干甲塌成一张平片 / 甲整体扭麻花 | 用了骨的 `matrix_local` 旋转 / 给每根骨都推了朝向 |
| **甲穿反**（正面朝背） | 源与骑砍正面相反，没做镜像（或做了 180° 旋转把左右也翻了） |
| 到处露白（身体顶穿甲） | 甲半径 < 身体半径 → 调大径向缩放；**手臂要单独调** |
| **籠手盖住拳头** | 手臂缩放各向同性 → 沿骨轴也被拉长 |
| **拳头被甲包住** | 源件网格带**手和手指**的几何 → 按主骨切掉（但**别整件丢**，那件可能还带上臂袖子） |
| **肩帯/某条整件消失** | 按主骨过滤时**作用在了合并后的整块网格上** → 过滤只能作用于目标那一件 |
| 编辑器报 `Unable to find material` + 一设材质就崩 | FBX 从源件继承了脏材质：名字对不上 + **带着指向不存在文件的贴图节点** |
| 导出把源模型全部网格一起打进去 | `use_selection=False` 会导出场景里所有网格 → 导出前清场 |
| 源图集被自己的输出覆盖 | 脚本的输出和输入是同一路径 → 加同路径防呆；源图另存 `_src_*` |
| UV 被映射两次 | 贴图脚本不幂等，拿自己的输出当输入重跑 → 用「甲的 UV 是否超出源件 UV 范围」自动拒跑 |

---

## 六、已完成

| 资产 | 来源 | 状态 |
|---|---|---|
| 蒂法头 / 萨菲罗斯头 | 外部模型 | ✅ 已装机（`Knowledge/蒂法换头工程.md`）|
| **真田幸村当世具足** | 战国无双2 解包件 | ✅ 全链路实机验证（1692 顶点 / 6 LOD / 三族贴图 / `taikou_yukimura_do_a`）|

**未根治**：上臂中段露身体（源件那里本来是布袖子，源件着物紧贴胳膊而骑砍身体更粗）。
撑到能盖住手臂就变香肠、不撑就露。干净解法 = 程序生成一段布袖（绕上臂骨的筒）。

---

## 七、换「默认脸」：xslt 覆盖 Native 皮肤（2026-09-14 登记，实机验证）

**要解决的问题**：让**一整类角色**（所有男性 / 所有女性）用上自定义头 —— 跟"给某一个人换头"（走**专属 race**，见 [campaign-mode.md](campaign-mode.md) 卷十三 §三）**分层共存**：
**默认脸改一类人、专属 race 改一个人**，专属 race 优先（不被默认脸覆盖 —— 前提是下面纪律 1 那条）。

**机制**：引擎**按文件名自动读**各模块的 `ModuleData/skins.xslt`，对**全模块合并后的 skins 文档**做 XSLT 变换。
**不需要**在 `SubModule.xml` 注册（TifaHead2 的 `<Xmls>` 是空的，照样生效）。

**最小可用写法**（`Modules/Taikou/ModuleData/skins.xslt`，把 human 男脸换掉）：

```xml
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
  <xsl:template match="@*|node()">                <!-- identity：其余全部原样 -->
    <xsl:copy><xsl:apply-templates select="@*|node()"/></xsl:copy>
  </xsl:template>
  <xsl:template match="race[@id='human']/skin[@gender='0'][@mesh_maturity_type='adult']/@face_meta_mesh">
    <xsl:attribute name="face_meta_mesh">head_xxx_a</xsl:attribute>
  </xsl:template>
  <xsl:template match="race[@id='human']/skin[@gender='0'][@mesh_maturity_type='adult']/face_textures">
    <face_textures group_id="1">
      <face_texture name="head_xxx_a" lod_material="head_xxx_a" color="0xFFFFFFFF" tags="face_texture1,face_texture2"></face_texture>
      <!-- 条数 4、tag 分布照抄原版 man，一条不能少 -->
    </face_textures>
  </xsl:template>
</xsl:stylesheet>
```

**六条纪律（每条都踩过）**：

1. 🔴 **匹配必须锚定 race**：`match="skin[@name='man']"` 这种"按名字匹配"在**合并文档里是跨 race 的** —— 它会把专属 race（如信长的 `lwn_nobunaga`，它的 skin 也叫 `man`）一起改掉，**"特殊人物用自己的脸"当场失效**。必须写 `race[@id='human']/skin[@name='man']`。加新专属 race 后**回头复查这条**。
2. 🔴 **改头网格必须同时改 `face_textures`**，且**条数 / tag 分布照抄原版**（条数变了 → 引擎按索引取 → 越界）。
3. **`mouth_textures` / `eyebrow_meshes` 照原版不动**（蒂法工程实机验证过）。女脸那边额外要处理 `deform_keys` + `constraints`（蒂法头的形变通道与原版女头不同），照 `TifaHead2\ModuleData\skins.xslt.master` 抄。
   🔴 **`deform_keys` 男女都要换**：它的 `key_min/key_max` 是**幅度乘数，必须与位移场来源同源**。原版男/女两套**不一样**（实测 63 条里 **56 条不同**）—— 只换女不换男 → **男脸被拉坏、动画不对**（2026-09-14 实机踩到）。模板用 `|` 同时匹配男女即可，不必复制那 500 行：
   ```xml
   <xsl:template match='race[@id="human"]/skin[@name="woman"]/deform_keys | race[@id="human"]/skin[@name="man"]/deform_keys'>
   ```
   **教训**：判断"两套参数能不能互用"，只比"条目在不在"不够，**必须比条目的值** —— 当年就是只对了通道号（序列 62 条全同）就下了"可直接对齐"的结论。
4. **切换 bat 化，`skins.xslt.master` 是唯一真源**：`to_game_mode.bat` = 复制成 `skins.xslt`（生效）+ `Assets`→`Assets_disabled`；`to_editor_mode.bat` = 反向。**删掉 `skins.xslt` 即完全还原**，无副作用。
5. 🔴 **配置必须放在「模块加载列表里一定有的模块」**（2026-09-14 栽在这）：游戏加载哪些模块由**启动参数**决定 ——
   `/singleplayer _MODULES_*A*B*C*_MODULES_`，**启动器里的 `IsSelected` 会被它覆盖**。
   实测：启动器里 `TifaHead2` 明明勾着，但启动参数列表里没有它 → 放在那边的 `skins.xslt` **永远不加载**，查了半天"改了没生效"。
   → 承载模块优先选**双端 junction 同源**的（本工程是 `Taikou`）—— 改一处，两个客户端都生效；
   **非 junction 的模块（如 `TifaHead2`）是两份独立拷贝**，改一边另一边不动。
6. 🔴 **后处理（`install_pack.py`）是终点，"白编译 ≠ 可用"**：ModKit publish 出来的包必须再跑
   `morphfix`（补 morph 帧 **60 → 101** + 同步 VertexKeyCount）+ `skinfix --fullmat` + 关卡 2 才叫成品。
   **判定有没有跑过**：`morphinfo` 的 `VertexKeyCount` 应为 **101**、`metaparts` 的 flags 非空（白编译 = 60 / 空）。
   ⚠️ 但**UV 沿用源模型布局的自定义头必须把 flags 清掉**（带 flags 时引擎按原版画布重生成脸贴图 → 错位）——
   **不同头的正确状态可能相反**（蒂法/萨菲罗斯要 flags、织田信长要空），别一刀切。

**离线回归验证**（改完必跑；加新专属 race 后也建议跑 —— 用 lxml 模拟"引擎视角的合并文档"）：

```python
doc = etree.parse('Native/ModuleData/skins.xml')
for rc in etree.parse('Taikou/ModuleData/skins.xml').getroot().xpath('//race'):
    doc.getroot().append(rc)                       # 合并
res = etree.XSLT(etree.parse('<模块>/ModuleData/skins.xslt'))(doc)
# 断言：human 男/女 = 目标头；lwn_nobunaga 等专属 race = 各自的头
```

**已用实例**：`TifaHead2\ModuleData\skins.xslt`（男萨菲罗斯 + 女蒂法）、`Taikou\ModuleData\skins.xslt`（男脸兜底，不依赖玩家勾选模块）。

**配套规格**：FBX 侧要求**材质名 ↔ 贴图名对应**（见 [蒂法换头工程.md](../../../Knowledge/蒂法换头工程.md) §13.7 第⑤条）；材质命名格式见 CLAUDE.md 铁律 27。

---

## 八、武器：静态网格 + 物理体分离（2026-09-15 登记，反编译 + tpac 实测）

### 🔴 一句话：**武器网格只管好看，碰撞和手感都不看它**

武器在骑砍2 里是**三层分开**的，别指望"把网格做准"就能匹配武器类型：

| 层 | 由什么决定 | 谁写 | 证据 |
|---|---|---|---|
| **视觉** | `mesh`（我们做的 FBX） | 导出管线 | —— |
| **物理碰撞** | **`body_name`** → 引擎内置 **PhysicsShape** 资产 | XML 一个属性 | 见下 |
| **手感** | `weapon_length` + `weapon_class` + `item_usage` + 伤害 | XML 数值 | `WeaponComponentData` 反编译 |

### 武器 = **静态网格体**（不需要骨架、不需要蒙皮）

| 证据 | 结果 |
|---|---|
| 原版武器 `torch_g` / `sturgian_blade_9`（tpaccli dump） | **`skel=none, anim=none`** |
| 织丰武器 `sho_bokken_katana`（已跑通的参照） | 同样 `skel=none` |
| 编辑器导入 | 只勾 `Import meshes`；**材质别勾 `Skinning`**（那是给甲/头那种蒙皮网格的） |

引擎把手骨变换直接套到网格上 → **网格原点必须落在握持点**（下条）。

### 武器网格的坐标约定（四处实测归纳）

| 轴 | 含义 | 实测依据 |
|---|---|---|
| **原点** | **握持点**（握把跨原点，不是刀柄末端） | 原版 `torch_g` 握把 z[-0.08, 0.28]；织丰 bokken z[-0.23, 0] |
| **+Z** | **指向武器头**（刀尖/枪尖/弓梢） | 同上（火焰/刀尖都在 +Z 侧） |
| **X** | 刃宽方向 | 原版 `sturgian_blade_9` x±2.8cm（宽）/ y±0.42cm（厚） |
| **Y** | 刃厚方向（刃面法线） | 同上；织丰弓 x[-0.04,0.37]（弦-背）/ y±1.6cm（弓臂宽） |

⚠️ **源模型若是 T-pose，武器是横躺在手上的**（战无2 实测：28 人长轴几乎全是 ±X），必须做一次刚体变换 —— 算法见 `tools/sw2-pipeline/scripts/build_weapon.py`（手骨定握持点 → PCA 定长轴 → 刀尖朝离握持点更远那端 → 三轴重排）。

### 🔴 碰撞走 `body_name`，**不是**网格

**反编译证据**：`TaleWorlds.Core.WeaponComponentData` 里**没有任何形状字段** —— 只有
`WeaponLength` / `WeaponBalance` / `TotalInertia` / `CenterOfMass` / `SweetSpotReach` / `PhysicsMaterial`。

**tpac 证据**：dump `bo_spear_b` 返回的是 **`META PhysicsShape bo_spear_b`** —— 几百字节的**参数化形状**资产（不是网格）。

**可选物理体清单**：`Native/ModuleData/CoreGameReferences/physics_shape.txt`（542 条，`bo_` 前缀的是武器体），近战能用的就十来个：

| body_name | 用途 |
|---|---|
| `bo_spear_b` | 枪/长柄 |
| `bo_sword_one_handed` | 刀剑（**双手剑也用这个** —— 原版没有 two_handed 版本） |
| `bo_knife_a` | 短兵/匕首 |
| `bo_mace_a` | 钝器/**通用兜底**（原版 Banner 全用它） |
| `bo_axe_longer_a` / `bo_axe_short` | 长柄斧 / 短斧 |
| `bo_push_fork` | 长杆（叉） |
| `bo_longbow_a` / `bo_shortbow_a` | 弓 |
| `bo_composite_crossbows` / `bo_cross_bow_heavy` | 弩 |
| `bo_capsule_arrow` | 箭/弩矢 |

**选法 = 按武器语义挑一个语义相同的体**（形状差不差无所谓）。原版自己也是这么挑的 ——
拼件（blade 类）实测统计：`bo_sword_one_handed`×118 · `bo_spear_b`×50 · `bo_mace_a`×35 · `bo_knife_a`×22。

⚠️ **全用 `bo_mace_a` 是坑**：它虽然能跑（原版 Banner 就用它），但 2.6m 的长枪配上"短棍"物理体，拾取/掉落/碰撞感全不对。

### 已落地（`tools/sw2-pipeline/`，28 件武将武器）

| 脚本 | 作用 |
|---|---|
| `build_weapons.py` → `scripts/build_weapon.py` | 挑件（材质 `mat_w_`）→ 规范化（握持点/长轴）→ 出 FBX + 尺寸表 |
| `gen_weapon_items.py` | 物品定义（含 `body_name` 按类型分配 + 弹药自给）+ 中文名 + 两张 CSV 登记 + 装备栏接线 |
| `extract_official_names.py` | 官方正式名表（html 源 → CSV，武器/铠甲/头盔三列） |
| `scripts/render_weapons.py` | 验收对照图（`--side` 看侧面 / `--textures` 带贴图） |

**武器类型表**（`gen_weapon_items.py` 的 `WEAPON_OF`/`BODY_OF`）：`weapon_class` + `item_usage` 必须 ∈ 引擎枚举与原版 `item_usage_sets.xml`；
远程武器**必须配弹药**（`Item1/Item2` 放箭/弩矢）且弹药定义也要在内容包内自给。

### 🔴 武器贴图**不共用角色图集**（2026-09-15 实机抓到）

战无2 的角色是"一张图集 + UV"模式，**但武器有各自独立的贴图** —— 28 把实测全有：

| | 贴图来源 | 说明 |
|---|---|---|
| **角色/甲/头** | `web/textures/<角色>.png` | 一张图集覆盖全身，靠 UV 取子区域 |
| **武器** | `web/weapons/tex/<tex>.png` | **每把武器一张**；`tex` 从 `web/weapons/manifest.json` 的 `models[<角色>].tex` 读（查看器的角色→武器→贴图映射表） |

**用错的症状**：UV 是**相对武器贴图**画的，拿角色图集去采 = 采到完全不相干的区域。
实测：前田庆次的枪杆渲染成金色、枪头丢掉金属银（正确贴图 `w_keiji0.png` 里杆是深色、枪头是银灰）。

**武器贴图很小，别放大**：源图实测 128×32 ~ 256×512（19 张是 256×64）。
放大到 2048 只是插值变糊 + 体积涨 20 倍（28 张 **25.4MB → 0.98MB**）→ 用 `make_sw2_textures.py --no-upscale`（只缩不放）。
（角色图集是 512×1024，放大到 2048 那条是已实机验证的，别一起改。）

### 🔴 换头「该并哪一块」三则判据（2026-09-15 登记）

**问题**：源模型是全身件，做"头"时要挑出脸/眼/头发。**一个对象里常混着几样东西**，
挑错了 = 脸上挂甲片 / 光头 / 缺刘海 / 脸上多一条带子。下面三则是本轮逐个渲染实证出来的。

| 症状 | 判据 | 落地 |
|---|---|---|
| 头旁挂**衣服/甲片**（件是"头发+衣服混装"）| **硬判据**：逐碎片看主导骨，只留 `{bone_10, bone_11}` | `parts_table` 该行 `hard=True` → 头发独立成 `hair` 角色 |
| **整块头发没了**，或日志出现"判据要削掉 ≥90% → 判为误伤，跳过" | 说明它在走 `keep_head_only` 的**离群判据**（"离面部骨簇远就删"）—— 那条件**会把头发判成杂质** | 同上，改走 hard（`build_head.py` 1.4b；白名单**只认头骨**，不收面部骨族）|
| 脸上一条**带子**（头盔颏带被画在脸壳件里）| ①耳侧那几块（独立碎片）→ **摘掉并进兜**；②下巴那一横条 = **下颌皮面本身** → **只改 UV，绝不删** | `parts_table`：`strap=[种子点]` + `strap_bone="bone_59"` |
| 头上一个**方形缺口**（发块底边本来就空一段，原模型靠领子挡）| 发块开口底边**补面** | `parts_table` `seal=[idx]` |

**🔴 三条铁律（血泪）**

1. **连在主网格上的几何，一律只改 UV，不删顶点。** 颏带的"下巴条"看着是配件，实际就是**下颌的皮面**；
   耳侧那几块看着像飘在表面外的碎片，**也是脸颊皮面的一部分**。删 ⇒ 镂空 + 露出头壳内表面
   （那部分 UV 在图集占位区 = 一块**棋盘格**）。
   **判据：先用 `_jit_inside.py` 把它单独渲出来，确认它离开之后脸还完整不完整。**
2. **整片一个 UV = 一块死板色块**（"皮肤出现较多不自然情况"）。要**逐 loop 取最近**肤色顶点的 UV，
   且候选点**限制在近邻半径内**（≈3 源单位）—— 取"全脸最近"会跨 UV 缝挑到后脑/头顶（UV 落在图集占位区）。
3. **改判据前先对账顶点数**：头删掉的 = 兜加上的（本轮 32/51/40/26/22 逐个相等）。对不上就是没搬对。

**接线**（改数据/改判据只改这几处）

```
parts_table.py ──► build_heads.py ──► build_head.py      （头：1.3c 摘碎片 / 1.3d 改UV / 1.4b 硬判据 / 1.4c 头发并脸壳 / 1.6 封底）
      └─────────► build_helmets.py ──► build_armor.py    （兜：--strap-from/--strap-seed 把颏带并回来）
```
🔴 **挑件串只有一个来源** = `parts_table.build_head_args()`；`build_heads.py` 里曾另有一份重复拷贝
（`pick_of()`），**改一份不生效**，已删。

**已知未决**：兜的贴图（`build_textures.py`）会报 `甲的 UV 超出了源件范围` —— 颏带 UV 在**脸壳**图集区、
不在兜件范围里。图仍出，但**带子在兜上的颜色要人工确认**；不对就把 UV 范围改成「兜件 ∪ 脸壳件」。

**排查工具**（`Debug/offline/_jit_*.py`，一次性、不进 git）：`_jit_textured`（贴源图集渲染，复现编辑器观感）·
`_montage_face`（**只渲脸壳件**拼图，"脸上多出来的带子"就是这么找出来的）· `_jit_inside`（按**种子点**选碎片单渲，
**定种子点必用**）· `_jit_small`（只渲小碎片）· `_jit_cut`（复现离群判据删了哪些顶点）· `_jit_bonecolor`/`_jit_dim`（按骨骼染色/分组）。

**相邻先例**：本轮全程照 **織田信长**（头）与 **真田幸村**（甲）两个已验收先例的参数；
判据改动后**必须自己先渲染验一遍再交付**（用户明令："你搞完之后自己用视觉模型识别没问题了再给我"）。
