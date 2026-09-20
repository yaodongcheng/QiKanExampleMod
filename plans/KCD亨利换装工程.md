# KCD 亨利换装工程（计划）

> **目标**：把《天国拯救》（Kingdom Come: Deliverance）的亨利 —— 源包 `NPC_Henry.fbx` —— 做成骑砍2 资产：**脸 / 甲 / 武器** 三件。
> **脸的精度基准** = [蒂法换头工程.md](../Knowledge/蒂法换头工程.md) 里**萨菲罗斯**那条链的规格（见 §三）。
> **成品落地模块** = `TifaHead2`（用户 2026-09-19 裁定）。
> **头盔不做** —— 源包里没有金属盔（用户 2026-09-19 裁定：就按包里的来）。

---

# ★★ 交接（2026-09-20 会话②）—— 「张嘴」专题：**结构已对，预览图未过关**

> ## 🔴 下一任第一件事：**只在预览图上迭代，别碰 FBX、别开 ModKit**
> **用户明令（2026-09-20）**：停掉 FBX/编译流程，**先把预览图做到"像原版一样张开"** ——
> 目标 = 嘴部呈现**一个完整的蛋形开口**（原版参照图 `Debug/offline/_mouthcmp/T_ref_front.png`），
> 做到了再叫他看、再谈重编译。**判据 = 我自己看图对比**，不是"参数调好了"。

## 一句话现状

亨利"说话不张嘴"这条链上，**接线、数据、结构三层都已修对**；卡在**最后一层观感**：
开口里看到的是**被照亮的内衬面**（不是凹进去的暗腔）→ 看着仍像"没张开"。

## 🔴 这一晚查清的五件事（每条都有实锤，别再重走）

| # | 根因 | 状态 | 位置 |
|---|---|---|---|
| 1 | **表情段（f60..100）从来没搬过** → 编译后被 morphfix 补成"原地不动" | ✅ 修 | `transfer_channels.py`：`CH_MAX=100` + `--anim-src/--anim-objects`（**按件对位**：眼球转动在眼球件、牙齿跟下颌在嘴件；男 脸/眼/嘴·女 脸/嘴/眼/睫） |
| 2 | 3 点最近邻加权**把唇线那道折线抹平** → 上唇跟着下唇走 | ✅ 修 | `--anim-map proj --proj-max 18`（沿法线投影+重心插值；18mm 是因为**两头表面在嘴部相距 7~13mm**，量法 `_probe_registration.py`）+ 内部面判据 + `--anim-smooth` |
| 3 | 「唇线」取的是**最前突点**＝**上唇**（亨利上唇 z≈1.643 才是全嘴最前） → 零位移线被顶到上唇 → 整张嘴一起下滑 | ✅ 修 | `mouth_line_z()`：改用**口腔内衬连通域的边界圈**定唇缝（亨利 z≈1.606） |
| 4 | 下颌场那段代码**插在 `rebuild()` 之后** → 形状键早写完，等于从没执行（"改了没变化"的真凶） | ✅ 修 | 顺序必须是 平滑 → **下颌/增益** → `rebuild` |
| 5 | **嘴唇在网格里是缝死的一整片连续面**（KCD 靠骨骼驱动嘴，不是 morph）→ 下颌一动只能把中间那片皮拉长 | ✅ 修（**用户点出的正解**） | `split_lips()`：沿唇线**分 7 段**（唇线是弯的，一个水平面切会把嘴角切歪）`bisect_plane` + `split_edges` → 上下唇成为两片独立的面 |

**试错记录（都别再试）**：只切边不删面 → 锯齿挂帘 ✗；整面删除 → 阶梯锯齿 ✗；一个水平面切 → 嘴角留一圈连着 → "中间小洞+两侧褶皱" ✗；`--mouth-deepen 6` 判据太宽 → 把鼻子附近也推了 ✗。

## 当前最好产物 + 复现

| 东西 | 路径 |
|---|---|
| 产物 | `Debug/offline/自定义头/henry_build/head_henry_a_v17.fbx`（md5 `BEB35597…`，= 试验里的 v20） |
| 配方（一条命令重跑整链） | `python tools/face-pipeline/scripts/build_head_chain.py --recipe henry`（jaw-gain **2.2** 已固化） |
| **只重跑通道**（几何用 v13，快） | `blender -b --python tools/face-pipeline/scripts/transfer_channels.py -- --src …/_chansrc/head/head_xxfemale_a.fbx --src-object head_xxfemale_a.002.0 --anim-src …/core_game/fbx/head/head/head_male_a.fbx --anim-objects head_male_a,head_male_a.1,head_male_a.2 --anim-map proj --proj-max 18 --anim-smooth 1 --anim-jaw --anim-jaw-gain 2.2 --dst …/henry_build/head_henry_a_v13.fbx --out <新版本>.fbx` |
| 渲染（**这一轮的判据工具**） | `blender -b --python tools/face-pipeline/scripts/_render_mouth.py -- <fbx> 71 <输出前缀> <对象名> <形变强度>` |
| 度量 | `_probe_mouth_apart.py`（按 z 分层看位移）、`_mouthcmp/gap.py`（张口量）、`_probe_registration.py`（两头表面距离） |

**已量到的数字**：f71 张口量 **7.59mm（原版 8.55mm = 89%）**、上唇 −1.59mm（原版 −1.27，不再跟着下唇走）。

## 🔴🔴 渲染判据的硬规矩（这一晚最大的教训，血换的）

1. **Workbench（无阴影）判"张嘴"完全不可信** —— 它把浅腔渲成一块**发亮平面**，"唇间暗缝"永远渲不出来 → 我为此连改三版数据、游戏里毫无变化，**因为判断基准本身是假的**。
2. 必须 **EEVEE** + 一盏 SUN；🔴 **光要从斜上方**（脚本里把相机欧拉 x 减 0.55 ≈ 抬 32°）—— 顺着镜头打会把口腔内壁照亮 → **明明张着也看着像闭着**。
3. **"亮面" ≠ "开口"**：开口的判据是**凹进去的暗腔**（原版 `T_ref_front.png` 里那个蛋形暗区），不是"有个轮廓"。
4. 渲染图**仍不等于实机**（光照/阴影差得远），但**用户本轮要求先把预览图做到像原版**，照办。

## 仍未解决 + 下一步候选（按推荐顺序）

| # | 打法 | 说明 |
|---|---|---|
| **A** | **把口腔内壁往后推（挖深）** | 开口里看到的亮面 = 内衬/唇内壁。**判据要收窄**：只动"嘴腔盒子内、且在唇面之后"的面（上次 `--mouth-deepen` 用的 `abs(x)<=0.03 & z 1.56~1.65` 太宽**把鼻子也推了** ✗）。现有 `deepen_mouth()` 收窄范围即可复用 |
| **B** | **删掉唇后那层"内衬"的迎光面** | 让开口后面直接是**真腔**（暗）+ 牙齿/舌头（`head_henry_a.2`）。原版看起来就是这样：蛋形暗区 + 里面见牙 |
| **C** | 渲染侧先对齐再谈几何 | 用**与原版完全相同的相机/光/曝光**渲我们（已经是 `T_*` 那套），先把"暗腔感"调到接近，再判断是几何问题还是光照问题 |
| **D** | 换源思路 | 如果 A/B 都不行 → 考虑**从源模型 KCD 侧**要嘴（它有 ~200 根 SSDR 骨驱动嘴，说明原始模型是能开的；我们只是丢了那套驱动）|

## 交接清单（下一任照做）

1. 读本段 + `Knowledge/蒂法换头工程.md` §23.6/§23.7（表情段与搬运保真度）。
2. 渲一组对照：`T_ref_front.png`（原版靶子）vs 我们最新产物 —— **同机位、同光**。
3. 按 A → B → C 顺序改 `transfer_channels.py`，**每改一版渲一张图自己看**，直到开口读起来像原版的蛋形暗腔。
4. 过关后再找用户：**不碰 ModKit、不碰 AssetSources**（用户明令）。
5. 定稿后：跑 chain 合出终版（要带上另一条线的领口 v12/v13 修复）→ 分发 → ModKit Publish → `install_pack --gate` → 实机。

---

# ★★ 交接（2026-09-20 会话①）—— 「头身两张皮」根因查清 + 领口几何已重建（**待 ModKit 编译**）

> **一句话**：头身不像一张皮，**不是分界线的问题，是两件事叠加** ——
> ① 脸贴图是**照片**（引擎还要再乘一次肤色）；② 头的"胸兜"是**一块平贴胸口的围兜板**。
> 两条都已修，产物 `head_henry_a_v12.fbx` 离线渲染通过。
> **现在卡在两件事上**：🔴 另有 agent 在并行修「嘴张不开」（`transfer_channels.py`），
> **我的 v12 不含他们的修复** → 等他们定稿后**跑一次 chain 合出最终版**，再走一次 ModKit 编译（清单见 §三 P0）。

---

## 🔴 一、查清的机制（**通用结论，蒂法/萨菲罗斯/战无2×28 全适用**）

### 1. 头身一致的硬条件 = **两张贴图底色同族**

引擎对**头和身体用同一个肤色乘子**（源码 `Shaders/Sources/forward_face_functions.rsh`）：

```
calculate_albedo_face:   tex_col.rgb *= g_mesh_factor_color.rgb
```

⇒ 头要和身体连成一张皮，**两边贴图底色就得同族**。实测**低频色调跨度**（只算网格真正用到的纹素）：

| 贴图 | 低频色调跨度 |
|---|---|
| xxFemale 脸 | **18** |
| 织丰男头 / 男身 | 21 / **11** |
| 原版女身 | **19** |
| 原版女头 | 45 |
| **亨利（KCD 源）** | **113** ← **差 2.5~10 倍** |

**原版系的脸贴图是"平底图"**（整张脸一个色调，明暗全交给引擎的乘子 + 光照）；我们的是照片（自带阴影/血色）。
**这就是"生硬的两张皮肤"。**

### 2. xxFemaleHead 的原理：**它一个几何都没动**

拆包（`tpaccli list`）看它装了什么：**网格只有 2 件**（头 + 眉毛）；材质 8 件（5 张自带脸贴图材质 + 睫毛 + 两个 body 材质）；贴图 10 张。

- 那两份 `body_female_a` / `body_kid_female_a` **只 dump 出材质、没有网格**；与原生逐字段 diff **只差一个 `skinning_precise`**（蒙皮精度开关，跟颜色无关）
- 它的头 mesh z 跨度 **1.4066~1.8019 = 原版一模一样**；**1.40~1.47 段与原版头逐点零距离**
- ⇒ **它是拿原版头雕的脸**，功夫全花在**整张脸贴图的色调口径**上

### 3. 几何"无缝"的真相：**共用顶点，不是 UV 对齐**

| 头 ↔ 同性别身体 | **3D 位置完全重合**的顶点数 |
|---|---|
| 原版女头 ↔ 女身 | **18** |
| xxFemale ↔ 女身 | **14** |
| 原版男头 ↔ 男身 | **18** |
| **亨利 ↔ 男身** | **0** |

- **接缝处的 UV 根本不连贯**：共用顶点的 UV 差 **0.45~0.87**（贴图上完全不同的两个位置）
- **那为什么看不出来？** 两侧贴图在那个位置**都是平的**（跨度 18~19）→ 色差表现为**没有形状的均匀台阶**，眼睛读不出"接缝"
- ⇒ **无缝 = ① 共享顶点 + ② 两侧贴图都平 + ③ 同一个肤色乘子。三条缺一条就露。**

### 4. 脸的 `_s` 是 **spec/gloss/AO 口径，不是金属度**

脸部着色器（源码实证）：

```
calculate_specular_face:          specularity.x *= g_specular_coef   // R = 反射强度
                                  specularity.y *= g_gloss_coef      // G = 光泽
compute_specular_face_lighting:   reflectivity = x * 5.0f
                                  roughness    = 1 - y
calculate_ao_face_forward:        occ = specular_sample.z            // B = 环境光遮蔽
```

实测中位：参照 **R 93 / G 104 / B 194**；我们 **R 0 / G 27 / B 255**
⇒ **脸的反射强度 0 = 完全没有高光**（一块死哑光板），旁边的身体有正常皮肤光泽 → 一眼两种材质。

🔴 `make_head_textures.py` 头注释写「与 Native `body_female_a` 同款」**是错的**：`body_female_a` 是**标准着色器**的金属度口径，**脸不是**。同一条链上的**蒂法 / 萨菲罗斯 / 亨利 / 战无2×28 全中**。

### 5. 亨利的领口缺陷（实机那条"袖口边"的来源）

脸壳自带一块 **488 顶点的"肩/胸口"独立岛** = 平贴在胸口的**围兜板**：

- 两侧比身体领口**低 4.5~8cm** → 趴在身体**外面**，边缘一刀切出硬边 = 那条"袖口边"
- 正前又**短 1.56cm** → 够不到身体 V 领口最低点（z 1.4144）

---

## 🔴 二、本轮做完的

### 贴图（**不用 ModKit**，可 `texreplace` 快测）

| 新工具 | 做什么 | 实测 |
|---|---|---|
| `tools/face-pipeline/scripts/flatten_face_tone.py` | **低频压平**（频率分离：低频→目标底色，中高频原样） | 跨度 **101 → 42**（参照区间 18~45） |
| `tools/face-pipeline/scripts/fix_face_spec.py` | **`_s` 口径修正**（R/G 映射到参照区间；B 取源 RMA 的真 AO） | R **0→88** / G **27→97** / B **255→200** |

### 几何（**要 ModKit 编译**）

| 改动 | 文件 |
|---|---|
| 新增 `drop_wide_lower_islands()` + 步骤 **3b-bis 删肩/胸口板**（判据：整块 z<1.56 且横向半径>0.09 的独立连通域；脖子那块 469 顶点半径只有 0.06，**不命中**） | `build_head.py` |
| `fill_neck_to_rim` 加 **Hermite 多环放样**（`--neck-loft-rings`，默认 1 = 老行为逐字节不变；两端相切 = 不折棱） | `build_head.py` |
| 接线 + 配方开 `drop_lower_plate` / `neck_fill` | `build_head_chain.py` |

🔴 **两轮的差别（这轮最大的教训）**：

| 版本 | 做了什么 | 结果 |
|---|---|---|
| **v10** | 只加 Hermite 多环放样 | ❌ **"衬衫领"** —— 一圈外翻的硬板。因为 `fill_neck_to_rim` 铺的是**整块围兜板的自由边**，从板的外沿继续往外散 |
| **v12** | **先删板、再从脖子真正的底环铺** | ✅ **对了** |

**产物**：`Debug/offline/自定义头/henry_build/head_henry_a_v12.fbx`（**102 形状键已带**：脸形 59/59、表情 39/41）
**离线渲染**：`henry_build/render_v12/head_contact.png`（四视角，前/侧/背都是平滑 V 领漏斗）

---

## 🔴 三、TODO

### ⚠️ 先读：**有两个 agent 在并行改亨利，这里划清分工**

| 谁 | 改哪个文件 | 状态 |
|---|---|---|
| **本 agent（领口几何 + 贴图）** | `build_head.py`（`drop_wide_lower_islands()` + `--neck-loft-rings`）、`build_head_chain.py`（配方）、`flatten_face_tone.py`、`fix_face_spec.py` | ✅ **已定稿**（v12 离线渲染通过），**不会再动** |
| **另一个 agent（嘴张不开）** | `transfer_channels.py`（`deepen_mouth` + 沿唇缝把上下唇切成两片独立面 + 表情段） | 🔶 **迭代中** |

🔴 **两边的改动天然可合并 —— 别各自出 FBX**：

```
build_head_chain.py --recipe henry      # 一条命令串起两个脚本
    build_head.py          ← 几何（我）
    transfer_channels.py   ← 嘴 + 表情通道（他们）
    + 关卡 1 门禁
```

**⇒ 正解：等他们都定稿，只跑一次 chain，一次拿到「几何 + 嘴 + 表情通道」三样。**

🔴 **时间戳实锤（别搞混）**：我的 `head_henry_a_v12.fbx` 建于 **09:34**，而 `transfer_channels.py` 最后改于 **10:03**
⇒ **v12 不含嘴部修复，拿它去编译嘴还是张不开。**

🔴 **别同时跑**：两个 Blender 会同时写 `Debug\offline\自定义头\henry_build\`（版本号自动 +1 不覆盖，但会出一堆半成品版本，编译时容易选错）。
**建议由他们跑**（他们还在迭代）；跑完把**版本号**告诉本 agent，验一下几何那部分还在不在，再走编译。

#### 转给「嘴」那边的三条线索

1. 🔴 v12 日志里 **嘴件（`head_henry_a.2`）的表情通道只命中 27/41 条**（脸壳 39/41、眼球 39/41）—— 嘴件可能缺了 14 条，值得先查。
2. 常驻闸门 = **`check_chan_anatomy.py`**（表情段 7 个地标 + 101 条全覆盖 + 件级"要有件跟下颌"）；**负面测试已做**（旧装机包必挂、原版/xxFemale 必过）。
3. 本文档 **[追加二](#-2026-09-19-追加二表情口型通道f60100从来没搬过--亨利没表情已修待重编译)** 记着：表情段（60..100）**历史上从来没搬过**，`morphfix` 会把缺的帧补成"原地不动"（帧数够、不崩、但不动）。**验收判据 = 捏人界面"试听声音"看嘴动不动。**

---

### P0 —— 编译（**卡在两件事上**）

| # | 事 | 前置 |
|---|---|---|
| **1** | 🔴 **等「嘴」那边定稿 → 跑一次 `build_head_chain.py --recipe henry` 出最终版** | 他们改完 `transfer_channels.py`；**只跑一次，别两边各跑** |
| **2** | **编译最终版并装机** | 🔴 **要用户开 ModKit**（铁律 31：分发进 AssetSources 必须等 ModKit 开着）+ 本 agent 验过"几何还在" |
| **3** | **实机验收（三条一起看）** | #2 |
| | ① 领口：那条"袖口边"消失、脖子→胸口是连续曲面 | |
| | ② 嘴：捏人界面"**试听声音**"看嘴动不动 | |
| | ③ 贴图：脸和身体像不像一张皮；脸**不能太假** | 太假 → `flatten_face_tone.py --keep-low 0.20→0.35` 重出，texreplace 1 分钟一轮 |

#### 编译清单（#2 展开）

🔴 **这一次编译要一起带上四样**：① 领口几何 ② **嘴部修复** ③ 表情/口型段 ④ 脸贴图压平 + `_s` 口径修正。

```
① 跑 Modules\KCD\to_editor_mode.bat  →  开 ModKit
② 分发（两份镜像都要，🔴 文件名一字不改，否则 metamesh 名变了要重新接线）：
     AssetSources\head\henry\head_henry_a_v3.fbx      ← 最终版 FBX 的内容（chain 产出的那个版本号）
     AssetSources\KCD\head_henry\head_henry_a_v3.fbx  ← 同上
     AssetSources\head\henry\head_henry_a_d.png       ← neck_tint\henry_flat_v1.png（压平版）
     AssetSources\head\henry\head_henry_a_s.png       ← neck_tint\henry_s_v3.png（口径修正版）
③ 编辑器里重导 FBX → Publish
④ python tools\face-pipeline\scripts\install_pack.py --filter head_henry_a
⑤ to_game_mode.bat → 实机
```

⚠️ **顺序铁律**：贴图**必须在这一次编译里带上** —— ModKit 重编译会从工程源重出包，**把 `texreplace` 调好的贴图覆盖回原照片**。

### P1

| # | 事 | 备注 |
|---|---|---|
| 4 | **正面那条很淡的竖棱** | 闭正中缝焊了 4 对顶点没完全消掉；实机若明显 → 再抹一轮 |
| 5 | **把两个贴图工具折进生成器**（铁律 22） | 现在 `flatten_face_tone.py` / `fix_face_spec.py` 是独立工具；正解是并进 `make_head_textures.py`，让配方一条命令出全套 |
| 6 | **蒂法 / 萨菲罗斯 / 战无2×28 走同样两条** | `_s` 口径（共用生成器，改一处全体受益）+ 脸贴图压平；**领口那条按各自源模型定**（蒂法没有胸兜板，无此问题） |
| 7 | **`fill_neck_to_rim` 的 UV 拉伸** | 领口那圈沿用上方顶点的 UV（纵向拉伸）；实机若见"拉丝/木纹" → 照蒂法改成**柱面 UV 投影** |
| 8 | **§22.8 的萨菲罗斯 v7 悬案** | 已装机、待实机判定（×1.11 + 10cm 渐变） |
| 9 | 自建 race `lwn_henry` | 原 P0-2，本轮未动 |
| 10 | 剑 / 鸢盾 / 剑鞘 | 原 P0-3 / §八 风险 1、2 |

### 归档待清（本 agent 留下的，用户未确认删除）

全在 `Debug/offline/` 下（**不进 git、不进发布包**）：

- `自定义头/neck_tint/` 的本轮中间产物：`henry_bands.png`、`henry_flat_v1.png`、`henry_s_v1~v3.png`、`pack_A/`、`pack_B/`、`pack_*/verify/`、`manifest_A/B.json`、`pack0_KCD_before_neckfix.tpac`（**这个是回滚包，先别删**）
- `Debug/offline/_neckcmp.py` / `_neckcmp2.py` / `_neckcmp3.py`（三个临时探针）、`_user_neckbase.png`、`_s_compare.png`、`_uv3.png`、`_neck_zoom*.png`、`_render_*.png`
- 两个**已被取代的探路工具**（`tools/face-pipeline/scripts/`）：`patch_neck_skin.py`（被 `flatten_face_tone.py` 取代）、`transfer_face_spec.py`（被 `fix_face_spec.py` 取代）

---

# ★ 交接（2026-09-19 收工）—— 下一轮从这里开始

## 🔴🔴 2026-09-19 追加三：表情段「搬运保真度」——**下巴动但嘴张不开**（已修，待重编译）

**用户报告**：亨利**下巴在动、但嘴根本没张开**（原版角色会张嘴）。

**根因（量出来的，不是猜）**：表情段的搬运算法是「最近邻 3 点反距离加权」—— 它会把源网格上的
**折线**（"上唇不动 / 下唇下移"）**抹成渐变** → 目标上唇跟着下唇走 = 嘴唇不分开。

| 同一把尺（正面中线按 z 分层） | 原版男头 | 修前 | 修后 |
|---|---|---|---|
| f71 唇线位移（z1.59~1.62） | −10.4 mm | −7.8 | **−9.0** |
| f71 **唇/下巴比** | 0.69 | 0.50 | **0.57** |
| f99（Speak）唇线位移 | −3.0 mm | −1.5 | **−2.1** |
| f99 **唇/下巴比** | 0.77 | 0.34 | **0.52** |

**修法**（全部写进生成器，配方已固化在 `build_head_chain.py`）：

| # | 改动 | 为什么 |
|---|---|---|
| 1 | 表情段改**沿法线投影到源网格 + 重心坐标插值** | 把源三角形内的线性场原样复制 → 折线仍是折线（`--anim-map proj`） |
| 2 | **`--proj-max 18`**（mm） | 🔴 两颗头的表面在嘴部**相距 7~13mm**（量法 `_probe_registration.py`）：亨利下半脸比原版男头**前突 1cm+**。6mm 门限 → 嘴部几乎全回退 = 白改 |
| 3 | **内部面判据**（同一 (x,z) 格子内 y 靠后 >3mm = 内部面 → 回退最近邻） | 口腔内衬的法线也朝前，18mm 投影会打到前面的**下巴**上 → 内衬拿到下巴的场 → 撕尖刺 |
| 4 | **`--anim-smooth 2`**（1 环平均，权重 0.5） | 投影会让上下唇分界处的相邻顶点落到源头**不同三角形**（各差 10~20mm）→ 唇缘撕裂；2 轮抹掉 |

**对照渲染**：`Debug\offline\_mouthcmp\`（`src_f71_*` 原版 / `our_f71_*` 修前 / `p18s_f71_*` 修后）。

**⚠️ 换别的头要先量距离定门限**：`_probe_registration.py` 报的"最近距中位"就是该头的 `--proj-max` 起点。

**新产物**：亨利 **v7**（取代 v5）。替换 `AssetSources` 那两个同名文件 → ModKit 重编 → `install_pack --gate`。

## 🔴🔴 2026-09-19 追加二：**表情/口型通道（f60..100）从来没搬过 → 亨利没表情**（已修，**待重编译**）

**用户报告**：捏脸界面「试听声音」时，原版头嘴会动，**亨利头嘴不动、整张脸零表情**。

**根因**：头顶点通道是 **101 条**，分两段 ——
`1..59` = 捏脸拉杆用（`deform_keys` 驱动，一直在搬）；**`60..100` = 表情/口型用**
（引擎 `morph_anims` 片段 `Speak`/`JawDrop`/`CloseEyes`… 驱动，**从来没搬过**）。
编译后 `morphfix` 把缺的帧补成"原地不动" —— 帧数够了（不崩），**但不会有任何动作**。
**三颗头（蒂法/萨菲罗斯/亨利）全中**（同一个管线产物）。

**已修**（改的是生成器，不是产物 —— 铁律 22）：

| 改了什么 | 文件 |
|---|---|
| 通道上限 59 → **100**；新增 `--anim-src` / `--anim-objects`（**表情段按件对位搬**：眼球转动在眼球件、牙齿跟下颌在嘴件） | `tools/face-pipeline/scripts/transfer_channels.py` |
| 常驻闸门扩到 **101 条全覆盖** + 表情段 7 个地标 + 件级两条（"要有件能转眼球""要有件跟下颌"） | `check_chan_anatomy.py`（**负面测试过**：旧装机包必挂，原版男头与 xxFemale 必过） |
| 配方里加表情源（脸形段仍用 xxFemale；**表情段用原版同性别头**，男头按件对位 `head_male_a,.1,.2`） | `build_head_chain.py` 的 `RECIPES` |

**三个新产物**（都已过关卡 1 + 备份）：

| 头 | 新产物 | 上一版 |
|---|---|---|
| 亨利 | `Debug/offline/自定义头/henry_build/head_henry_a_v5.fbx`（10.3 MB，102 形状键） | v3 |
| 萨菲罗斯 | `Debug/offline/自定义头/seph_build/head_sephiroth_a_v9.fbx` | v7 |
| 蒂法 | `D:\BrainMaker\blend_projects\tifa_export\backup_20260913\head_tifa_a_v13.fbx` | v12 |

**核验过的**：三件的形状键都是 **102 个**（Basis + KeyTime_0..100，顺序对）；脸形段数值与上一版**逐帧一致**（没动过）；
表情段实测落位正确（JawDrop 最大点在 y+0.148/z1.556 下颌、CloseRightEye 最大点在 +x、EyebrowRaise 在眉高 z1.705）。

**⚠️ 还没生效** —— 按 [铁律 31](../../CLAUDE.md) 流程（**分发进 AssetSources 必须等你把 ModKit 开起来之后**）：
把下面 4 个文件的**内容**换掉（**文件名一字不改**）：

```
KCD\AssetSources\head\henry\head_henry_a_v3.fbx          ← head_henry_a_v5.fbx 的内容
KCD\AssetSources\KCD\head_henry\head_henry_a_v3.fbx      ← 同上
TifaHead2\AssetSources\head\sephiroth\head_sephiroth_a_v6.fbx  ← head_sephiroth_a_v9.fbx
TifaHead2\AssetSources\head\tifa\head_tifa_a_v11.fbx     ← head_tifa_a_v13.fbx
```
然后：ModKit 重编该头 → Publish → `python install_pack.py --filter head_xxx` → 实机验收。
🔴 **验收判据 = `check_chan_anatomy.py` 必须过**（`--packdir <模块>\AssetPackages --filter head_henry_a`），
它现在会查表情段；再实机捏脸「试听声音」看嘴动不动。

### 🔴 跟贴图定档的**先后顺序**（这条决定"现在要不要重编"）

**新事实**：表情段只活在**网格的 morph 帧**里 —— 下面那条 `texreplace` 快路**只换贴图数据段**，
**补不了表情段**（它连"101 帧形变原样保留"都是刻意保住的）。⇒ **表情修复没有捷径，必须走 ModKit 重编。**

⇒ 两条路选一条（顺序铁律见下方「🔴 顺序警告：先定贴图档位，再重编译」，**两条都严禁"定完档重编却不带 PNG"**）：

| 路 | 做法 | 代价 |
|---|---|---|
| **② 先验表情（推荐）** | 现在就换 FBX 重编（贴图维持原档）→ 先看嘴动不动 | 定档后**还要再重编一次**（否则 texreplace 调好的贴图被工程源里的旧 PNG 覆盖回去） |
| ① 合并（省一轮） | 先按下面「调脸贴图参数的循环」把脸贴图档位定下来 → 选定 PNG 落进 `AssetSources\head\henry\head_henry_a_d.png` → **再**换 FBX，一次重编带上全部 | 表情修复要等贴图定档 |

**为什么推荐 ②（2026-09-19 晚，证据变了）**：① **102 个形状键过编辑器是全新路径**（从没编过），
早验早暴露问题，别跟贴图那摊缠一起；② 贴图定档仍在试档（当前包 = `tint_v3` = 0.75 档），
挂着表情修复等它不划算 —— 反正定档后为拿完整 mipmap **必须要再编一次**。
③ 走 ② 时若已认定某一档，**顺手把那张 PNG 也落进 `AssetSources`**，这次重编就顺便把贴图定下来（省一轮）。

## 🔴🔴 2026-09-19 追加一：59 条脸形位移场「前后镜像」已修（**同上，待重编译**）

**问题**：用户实机发现**拉鼻子、后脑勺动**。查下来不是界面问题，是**网格里的位移场被前后（Y 轴）镜像过**
（详见 [蒂法换头工程.md §21.12](../Knowledge/蒂法换头工程.md)）。**亨利是继承的**，源头在蒂法 v10，
三个头（蒂法/萨菲罗斯/亨利）全中。

**已做**（只重建形状键，**几何一个顶点没动**）：

| 头 | 新产物 | md5 |
|---|---|---|
| 亨利 | `Debug/offline/自定义头/henry_build/head_henry_a_v3.fbx` | `f1f8c359d16b1fac901f7e9095968dfa` |
| 萨菲罗斯 | `Debug/offline/自定义头/seph_build/head_sephiroth_a_v7.fbx` | `360eee2d310bc87d3815267bdd6b5930` |
| 蒂法 | `D:\BrainMaker\blend_projects\tifa_export\backup_20260913\head_tifa_a_v12.fbx` | `99b8adcbe222ce181fd572a53d8926f2` |

**验收**：鼻帧质心从 y=−0.014~−0.036 回到 **y=+0.146~+0.162（鼻尖方向）**；
`check_chan_anatomy.py` 对原版/源头过、对旧装机包全挂（两面验证过）。

**⚠️ 还没生效** —— 要按 [铁律 31](../../CLAUDE.md) 的流程走（**分发进 AssetSources 必须等你把 ModKit 开起来之后**）。
🔴 **替换清单以上方【追加二】那张表为准**（那是最新版号 v5/v9/v13；本条原来列的 v3/v7/v12 已被取代），
**文件名保持不变**，否则 metamesh 名变了要重新接线。
然后：ModKit 重编该头 → Publish → `python install_pack.py --filter head_xxx` → 实机拉杆验收。

**🔴 顺带要重估的**：TODO P1-4「眼睛瞪」的候选②就是"脸形权重的形变" —— 那正是被镜像的那批场。
修完先看眼睛还瞪不瞪，再决定要不要继续按 §21.9 折腾离线对比。

**🔴 全线 TODO（上面两条对所有自建头都成立）**：蒂法 / 萨菲罗斯 / 亨利 **+ 战无2 的 28 个头**（同一个管线产物，
同样只有 59 条通道、没有表情段）全部重编译；脸贴图统一"洗底" → 见 [蒂法换头工程.md §23.5](../Knowledge/蒂法换头工程.md)。

## 🔴 调脸贴图参数的循环 = `tpaccli texreplace`（**不需要 ModKit**）

**一句话**：改脸贴图（洗底 / 调明暗 / 调冷暖）**不用开编辑器、不用重 Publish、不用重打补丁** ——
直接改**已编译好**的 `pack0.tpac` 里那张贴图的**数据段**。一轮约 1 分钟（代价 = 要重启游戏）。

```bash
# 1) 出图：把源贴图按增益洗一版（--level 0=不动 / 1=完全对齐参照；带高光滚降不削顶）
python tools\face-pipeline\scripts\tint_face_texture.py \
  --src "<模块>\AssetSources\head\henry\head_henry_a_d.png" \
  --out "Debug\offline\_texref\out\new.png" \
  --ref "Debug\offline\_texref\(unparsed)\head_female_x20_d.png" --level 0.75
# 2) 规范格式（进包/进工程源都要 8bit RGB + 只有 IHDR/IDAT/IEND）
python tools\face-pipeline\scripts\png_for_editor.py "Debug\offline\_texref\out\new.png"
# 3) 替换（manifest 里 name = 贴图资产名，png = 第 2 步产物，width/height 必填）
tpaccli texreplace --packdir "<模块>\AssetPackages" --filter pack0 --mapping <manifest.json> --out <临时目录>
# 4) 备份旧包 → 新包拷进 <模块>\AssetPackages\pack0.tpac → 重启游戏
```

**保住了什么**：贴图 **GUID 不变**（材质接线不断）、101 帧形变与四角色材质配方**原样保留**。
**代价**：输出 **DXT1 + 单级 mip**（工具写死）→ 远景没有 mipmap、**理论上会闪**。
⇒ 🔴 **它只适合调参；定版必须回 ModKit 重编**（那样才带完整 mipmap），并把最终 PNG 落进 `AssetSources`
（铁律 31：落镜像这一步要等 ModKit 开着）。

**回滚**：每轮前把旧包备份到 `Debug\offline\自定义头\henry_build\backup\`，说一声就能换回来。
**备好的档位**：`Debug\face_tint\henry_tint_0.50 ~ 0.90.png`（8 档，皮肤亮度 143~178；原始未调 = 97）。

> 原始记录与配套手法（色带定位法等）见 [蒂法换头工程.md §22.3](../Knowledge/蒂法换头工程.md)。
> 🔴 **在线换贴图的 `custom.face_tex` 指令已实测不可信**（任何档位只把脸变蓝）—— 别用，只走上面这条离线路。

### 🔴 顺序警告：**先定贴图档位，再重编译**

`texreplace` 改的是**已编译包**；而 **ModKit 重编译 + Publish 会从工程源重出包 → 把调好的贴图覆盖回原图**。
所以顺序必须是：

```
① texreplace 离线定档（改的是编译产物，见上）
② 把选定 PNG 落进 AssetSources\head\<角色>\<头>_a_d.png   ← 铁律 31：要等 ModKit 开着
③ 再用带表情段的新 FBX 重编 + Publish                    ← 一次把「表情段 + 形变修复 + 贴图」全带上
```

**先重编译 = 贴图那轮的功夫白费。**

## 一句话现状

亨利（KCD）已做成**独立模块 `KCD`**，**头和 4 件甲在实机跑通**（脸是亨利的、眼睛/嘴已修好、甲有物品定义）；
**剑没编译**；**自建 race 没做**（所以现在全大陆男性都是亨利的脸 —— 这是当前的已知副作用，见 TODO P0-2）。

## 已完成

| 阶段 | 产出 |
|---|---|
| 0 勘察 | 源包 25 网格认件 / 8 连通域 / 贴图规格 / Blender 导入器补丁 |
| 1 认件 | 零件表 + 接触图（`Debug/offline/KCD/kcd_parts/`） |
| 2 武器 | 长剑网格 + 3 贴图（`Debug/offline/KCD/kcd_build/`） |
| 3 甲 | **4 件**（身/腿/手/披风），各 6 级 LOD + 图集贴图 |
| 4 脸 | 3 子网格男头 + 5 张贴图（睫毛/眉毛已烘进脸贴图）+ **101 条通道**（59 拉杆 + 41 表情） |
| 5 装机 | **KCD 独立模块**（junction 双端）+ 后处理 + 物品定义 + 注册 |

## 🔴 KCD 模块的现状（这是交付主体）

```
H:\...\MB2_1.2.12\...\Modules\KCD\          （junction → Steam 客户端同源）
├── SubModule.xml                 Id=KCD；Xmls 注册了 <XmlName id="Items" path="kcd_items"/>
├── AssetPackages\pack0.tpac      77.7 MB / 29 项 = 头 + 4 件甲（含材质贴图）
├── ModuleData│   ├── skins.xslt               ✅ 重写过的 KCD 专用版（只动 human 男皮肤 → head_henry_a）
│   ├── skins.xslt.master        同上（编辑模式回滚真源）
│   ├── skins.xml                59 B 合法最小文件（🔴 空的会崩，见坑 1）
│   └── kcd_itemsrmor.xml      4 件甲的物品定义（is_merchandise=true，商店可买）
├── Assets_disabled\             编辑器工程（游戏模式=改名）
└── AssetSources\                源（head/armor/weapon 三类的 henry 子目录）
```

## TODO

### P0（下一轮必须先做）

1. **用户验收**：重进游戏看**甲能不能穿上**（商店买 → 四个槽位分别穿）。物品定义是本轮新写的，**数值是按原版量级估的、不是实测**。
2. **自建 race `lwn_henry`** —— 用户诉求：**只有玩家能用、开局捏人选**，不要全大陆都是亨利。详见下方「race 交接」。
3. **剑**：把 `KCD\AssetSources\weapon\henry\` 的源在 ModKit 里导入编译 → 进包 → 放开 `kcd_itemsrmor.xml` 末尾注释块里的剑物品定义。
   （剑是**静态网格**，**不走 morphfix/skinfix**，只需进包 + 放物品定义。）

### P1

4. **眼睛"瞪"的诊断** —— 眼球的位置和大小都已实测正确（中心 `(0,0.128,1.684)` 误差 0、直径 ≈24mm）。所以"瞪"来自**眼睑开合**，两个可能：① 源件几何如此 ② **脸形权重的形变**（位移场来自蒂法/xxFemale，权重却是引擎按 BodyParameters 给的 → 可能把眼皮拉开）。**🔴 2026-09-19 更新：候选②的原因已确认并修复（位移场被前后镜像，见 §23.2）→ 先看修完后还瞪不瞪，再决定要不要走 §21.9 的离线对比。**
5. **清 Taikou 里的残留**（我留下的，用户未确认删除）：
   - `Taikou\AssetPackages\kcd_henry.tpac`（77.7 MB，**与 KCD 里那份重复**）
   - `Taikou\ModuleData	aikou_items\kcd_henry_items.xml`（**悬空** —— 引用的 mesh 不存在）
   - ⚠️ 同目录 `kcd_henry_items.xml.bak_civilian_20260919_1432` **不是我建的**，别动
6. ~~§4.2 / §八 风险 8 的 `--clear-flags` 结论要改~~ → ✅ **本轮已改**（"清空"→"保留"，旧推导已折叠留档）。
7. **补轮子库**（见下方「经验」）。

## race 交接（P0-2 展开）

**用户诉求**：给亨利注册一个 race，**只有玩家能用**（不给任何 NPCCharacter 挂 `race=`），**开局捏人能选**。

**✅ 好消息**：这个诉求**本来就成立** —— 总纲 323 行记着「**一人一 race**；副作用 = 建号捏脸的「种族」下拉里会堆一排 `lwn_*`」。所以自建 race **会自动出现在捏人下拉里**，且不挂 NPC 就不会影响路人。

**要做的 5 项接线**（照织田信长 `lwn_nobunaga`）：

| # | 项 | 关键点 |
|---|---|---|
| 1 | `skins.xml` 加 `<race id="lwn_henry">` | man 皮肤 → `head_henry_a`；**deform_keys 非零**（脸才可捏） |
| 2 | `monsters.xml` **整族复刻** | 🔴 引擎按后缀查定义、缺一个就在该路径崩：`lwn_henry` + `_child` + `_settlement` + `_settlement_fast` + `_settlement_slow` 共 **5 个 id**（照 Native 的 `human` 一族） |
| 3 | `SubModule.xml` 注册 monsters | `<XmlName id="Monsters" path="..."/>` |
| 4 | 🔴 **性别表离线表** | `RaceGenders.xml`（**雷 135：性别不符的 race 不置灰 → AV 崩**，实测踩过） |
| 5 | 过滤补丁 | `CampaignMode/FaceGenRaceGenderFilterPatch.cs`（**已存在、通用**，在 LWN 里） |

**🔴 开工前必须先解决一个耦合问题**：上面这套机器**全是 Taikou 专属的** ——
生成器 = `Scripts/gen_taikou_sw2_heads.py`；性别表路径 = `Taikou\ModuleData\AssetRegistry\RaceGenders.xml`；过滤补丁从那个路径读表。
而 **KCD 是独立模块（无 Taikou）** → 照搬的话补丁读不到性别表 → **雷 135 的 AV 崩会复现**。
**先定**：性别表路径怎么给 KCD 用（放同一个路径=依赖 Taikou 目录；还是把补丁改成"扫所有已加载模块"）。

## 经验（要进轮子库 `plans/rules/wheels.d/assets.md`）

| # | 教训 | 症状 → 根因 → 修法 |
|---|---|---|
| 1 | **空 XML 文件 = 崩** | `Root element is missing` @ `CreateProcessedSkinsXMLForNative` → `ModuleData\skins.xml` 只有 3 字节 → 写合法最小文件（`<?xml?>` + `<skins></skins>`）。🔴 **别用文件大小的 KB 取整判断空不空**（`xxFemaleHead` 那份显示 "0 KB" 实际是 83 B 的合法文件） |
| 2 | **`--clear-flags` 判反** | 眼睛/嘴变成脸的贴图（"脸皮合成贴到了眼球上"）→ 清掉了不该清的 → **亨利必须【保留】flags**。🔴 **判据不能靠离线量 UV 落点**（8×8 覆盖两套都填满、区分不出来），**以实机症状为准** |
| 3 | **`install_pack.py` 的 `s3` 会静默给你陈旧产物** | 不带 `--clear-flags` 时脚本**跳过 metaparts** → `s3` 不重新生成 → 拿到的还是上一轮清过 flags 的包（不报错、不提示）→ **取产物要认阶段：带 clearflags 取 s3，不带取 s2** |
| 4 | **照抄别的模块的 `skins.xslt` 会带进它的副作用** | 无声 native 崩（无托管栈）→ KCD 的 xslt 是从 **Taikou 的**复制改的，连"改女皮肤→`head_tifa_a`"的模板一起搬了 → 女性头指向**不存在的资产** → **应照 `xxFemaleHead`（纯资源替换参照 mod）的路子重写，只留目标性别的模板** |
| 5 | **换新源时"源网格对象的变换"必须烘进顶点** | 单位换算与转轴只写在对象矩阵里，顶点本身是「源单位+源朝上轴」→ 谁把矩阵当单位阵丢掉就错 100 倍（`build_armor.py` 甲 bbox 从 ±0.49m 炸到 x±66.8；`build_head.py` 头壳中心算到 y=161）。**战无2 的 FBX 恰好是单位阵，所以这个假设藏了几个月** |
| 6 | **`assetclone` 出来的包几何是空的** | 顶点数 0（`metaparts` 仍能报 `verts=8603` —— 那是**元数据**不是几何）→ **`missingrefs`/`Source`/`segment owner` 三验全过也没用** → 判据必须是**从克隆包 dump 出 OBJ 跟源包对顶点数** |
| 7 | **图集两个坑** | ① 源件可能用**平铺 UV**（KCD 武装衣主槽 `u∈[1.0,2.0]`）→ 重排前必须 `u%1` 折回，否则整片采到隔壁格子 ② **空占位材质槽是真几何**（`Empty_col.png`=纯黑，武装衣 1379 面 / 腿甲靴子处 782 面）→ 也要占一格填黑，不能不管 |
| 8 | 🔴 **`install_pack.py` 的 `m1`/`m2` 也会静默给你陈旧产物**（第 3 条的同一类坑，2026-09-19 实锤） | **症状**：Publish 出的新包**全程没被用上**，装进模块的是**上一轮**的旧包；而 morphfix / skinfix / 关卡 2 / 装机**全部报成功**（"改了但没变"）。**根因**：`morphfix` 判定"已对齐→跳过"时**不产出文件**，而 `m1/` 没在跑之前清空 → 脚本 `os.path.exists(src1)` 命中**上一轮躺在那儿的包**，一路带到底。**修法**：跑工具**之前**调 `stage_dir("m1")`/`stage_dir("m2")`（改在 `install_pack.py`，已修）+ **装机后加 `--gate`**（就地跑 `check_chan_anatomy.py`，过不了返回 1）。**判据**：`in`（Publish 原包）与 `s2`（后处理完）**逐阶段跑闸门**，哪一段挂了就是哪一段干的 |

## 关键路径速查

| 东西 | 路径 |
|---|---|
| **KCD 模块（交付主体）** | `H:\...\MB2_1.2.12\...\Modules\KCD\` |
| 源包解包 | `LivingWorldNpcs\Debug\offline\_kcd_recon\` |
| 认件产物 | `LivingWorldNpcs\Debug\offline\KCD\kcd_parts\` |
| 甲/武器 FBX + 贴图 | `LivingWorldNpcs\Debug\offline\KCD\kcd_build\` |
| 脸的工程 | `LivingWorldNpcs\Debug\offline\自定义头\henry_build\` |
| KCD 工具链 | `LivingWorldNpcs	ools\kcd-pipeline\`（`split_rma.py` / `build_shield.py` / `bake_face_overlays.py` / `map_kcd_henry.json` / `dump_skeletons.py`） |
| 后处理工作目录 | `LivingWorldNpcs\Debug\offline\自定义头	ifa_postpublish\`（m1/m2/m3/s1/s2/s3） |
| **盾** | 未做（要面积对齐 + 出贴图；朝向的映射已定：`v_new = (−z, y, x)`） |

---

## 一、源包里有什么（已勘察，数字都是实测）

| 项 | 值 |
|---|---|
| 源文件 | `TifaHead2\AssetSources\KCD\Henry_of_Skalitz_Noble_Nuremberg_Armor_1403.zip`（453MB） |
| 主模型 | `NPC_Henry.fbx` —— 11.7MB，FBX **7.5**，3ds Max 导出 |
| 骨架 | **1 个，484 根骨**（CryEngine 命名：`Head` / `Spine4` / `Neck1` / `mhenry_SSDR_*_joint_N`） |
| 网格 | **25 个**（见下表） |
| 材质 / 贴图 | 27 材质 / 63 张贴图（8bit PNG；`RMA` = 粗糙度·金属度·AO 打包，CryEngine 惯例） |
| 坐标 | Blender 里 Z 轴朝上、**米制**，**脸朝 −Y**（跟萨菲罗斯源一致） |
| 身高 | 1.81m（发顶 z=1.813） |
| **头盔** | ❌ **没有** |

### 25 个网格 —— 已认件（✅ 阶段 1 接触图 + 材质↔贴图核对完毕）

| 归组 | 网格 | 顶点 | 贴图 | 说明 |
|---|---|---|---|---|
| **脸** | `m_head_henry` | 8323 | `m_head_henry_*` | 脸壳（**8 个连通域**，见下） |
| | `m_head_henry_Teeth` | 1336 | **`m_head_henry_*`（跟头共用）** | 上下牙 + 舌头 |
| | `_Eye{L,R}Ball_Shader_Mesh` | 498×2 | `EYES_COLOR/NORM` + `EYES_MFCA` | 眼球 |
| | `_Eye{L,R}Iris_Shader_Mesh` | 202×2 | 同上 | 虹膜 |
| | `m_head_henry_Eyelashes` | 912 | `eyelash_*` | 睫毛 |
| | `m_head_henry_Eyeshadows` | 186 | `eye_overlay_COLOR`（无 N/RMA） | 眼影 |
| | `m_head_henry_Tearline` | 263 | `eye_water_*` | 泪线 |
| **头发** | `m_hair_henry_Cap` | 474 | `haircap_v02_*` | 发帽（绑 Head 88%） |
| | `m_hair_henry_Cards` | 8991 | `haircards_v01_*` | 发片（绑 Head 100%） |
| **身体** | `male_body` | 9243 | `male_body_*` | 裸身（骑砍身体由引擎按体型生成，**不导出**） |
| **甲** | `cuirass07_torso` | 5237 | `cuirass07_m01_torso_*` | 胸甲（绑 Spine4/Spine1） |
| | `gambesonlong03_arms` | 2983 | `gambesonlong03_m01_arms_*`（submat0）+ **`mailshort01_m01_arms_*`（submat3）** + 2 个空槽 | 武装衣袖 + 袖上锁子甲（一个网格两种材质） |
| | `gambesonlong03_waist_mat_mtl_mat` | 725 | `gambesonlong03_m01_torso_*` | 武装衣·腰裙（绑 Spine1） |
| | `gambesonlong03_waist002_FINAL` | 659 | **`mailshort01_m01_skirt_*`** | ⚠️ 名字叫 gambeson，**实际是锁子甲裙**（材质名 `Standardmaterial`） |
| | `gauntlets05` | 3213 | `gauntlets05_m01_hands_*` | 护手（绑 LeftHand/RightHand） |
| | `hood06` | 1255 | `hood06_m01_neck_*` | 兜帽（绑 Spine4 52% / Neck / 双肩 —— 是**披在肩上的斗篷式兜帽**） |
| | `legsplate03` | 3775 | `legsplate03_m01_legs_*` + 1 空槽 | 腿甲（绑 LeftFoot/LeftLeg） |
| **武器** | `0000_long_sword_henry_mesh` | 2627 | `long_sword_henry_weapon_*` | 长剑·剑身（绑自带骨 `long_sword_henry_mesh`） |
| | `0001` / `0002_long_sword_henry_mesh` | 20 / 20 | `decal_henry_sword_diff.tif` / `mj_diff.tif` | 剑身贴花两片（⚠️ `mj_diff.tif` 无 PNG 版，缺） |
| | `scabbard_longswordcapon` | 2197 | `scabbard_longswordcapon_scabbard_*` | 剑鞘（绑 `LeftWeaponSheath` 50% —— 挂在左胯） |
| | `0000_shield_kite_blank_mesh` | 3374 | `shield_kite_petr_dvojity_agent_*` | 鸢盾·盾面（绑自带骨） |
| | `0001_shield_kite_blank_mesh` | 455 | `shield_kite_blank_alphas_*` | 鸢盾·撕边 |

**三条对后面有用的**：
1. **甲各件各有独占贴图**（不像战无2 是"整身一张图集"）→ **不用裁图集**，贴图这一步比战无2 简单。
2. **牙齿用头的贴图** → "嘴"件（骑砍第 3 槽位）可以直接吃脸贴图。
3. **`Empty_col.png` 是空占位图** —— 出现在 `gambesonlong03_arms`（submat1/2）、`legsplate03`（submat1）、`male_body` 的 TransparentColor。这些是**没用到的材质槽**，导出时剔掉，别当真贴图。

### 🔴 脸壳的 8 个连通域（决定"哪些件并进脸、哪些剔掉"）

| 顶点数 | 位置（z 米） | 绑的骨 | 是什么 |
|---|---|---|---|
| 6328 | 1.541~1.782 | Head 35% | **脸壳 + 头皮**（主体） |
| 708 | 1.570~1.631 | Head + 嘴角骨 | **口腔内衬** |
| 488 | 1.444~1.540 | Spine4 61% / Neck 22% / 肩 7% | **肩/胸口**（要收进骑砍身体的 V 领口） |
| 469 | 1.498~1.592 | Neck 50% / Neck1 18% | **脖子** |
| 111 / 111 | ≈1.67 | 眼周骨 | 左右**眼窝内衬** |
| 54 / 54 | ≈1.67 | 眼睑骨 | 左右**泪点** |

**好消息**：
1. **脖子（469 顶点）+ 肩（488 顶点）已经长在脸壳网格里了** —— 萨菲罗斯当年只有半张脸壳 + 一个"脖子"，亨利是自带完整下颌→颈→肩，起点更好。
2. **脸贴图独占**，UV 铺满 [0,1]²（`m_head_henry_COLOR.png` 2048²）→ 脸实得 **2048² texel**，远高于萨菲罗斯的 834²。
3. **源骨架会被整块删掉**（`build_head.py` 导出前清掉全部非网格对象）→ 484 根 CryEngine 骨**不参与**裁剪/标定/导出任何环节，不需要做骨映射。

**坏消息**：亨利的脸**没有形变通道**（无 blendshape），表情靠 ~200 根 `mhenry_SSDR_*` 骨骼驱动。骑砍头要的是 **101 条顶点位移场**（59 拉杆 + 41 表情，见 [§23.6](../Knowledge/蒂法换头工程.md)），跟骨骼动画是两套东西（见 §三 ②③⑥）。

> ⚠️ **注意**：源里那套 `Neck 50% / Spine4 61%` 的权重**会被丢掉** —— `build_head.py` 把源骨架整个删掉、重新刚性绑到官方骨架的一根 `bip01_head_13` 上。所以脖子的权重还是要靠 `--weights-from` 从原版男头抄（跟萨菲罗斯同一套做法）。

---

## 二、已解决的阻断：Blender 导入崩溃

**症状**：`NPC_Henry.fbx` 一导入 Blender 5.2 就崩：`KeyError: None @ link_hierarchy`。
**根因**：剑/盾那几件蒙皮到的骨头**不在骨架子树下**（在 `RightWeaponRoot` 那支），导入器给它们建的登记表键是 `None`，之后按「骨架对象」去查就查不到。
**修法**：导入器源码**内存级补丁**（不碰 Blender 安装文件，跟战无2 的 morph 断言补丁同一套手法）。
**状态**：✅ 已进 `tools/sw2-pipeline/scripts/identify_parts.py`。⚠️ **`build_head.py` 里还有一份独立的 `patch_importer()`（:115-124），只有 morph 那一道，必须补上这道** —— 否则换头脚本连源都导不进来。

---

## 三、脸的精度标准 —— 抄萨菲罗斯那条链

萨菲罗斯头的组成（= 验收清单，逐条来自 [蒂法换头工程.md §13.7](../Knowledge/蒂法换头工程.md)）：

| # | 规格 | 亨利怎么落 |
|---|---|---|
| ① | **子网格件数与顺序固定**：男头 **3 件 脸→眼→嘴**（女头才是 4 件带睫毛） | 脸壳 / 眼球（4 件合 1）/ 嘴（牙齿+口腔）。⚠️ **睫毛·眼影·泪线是第 4/5/6 件 → 男头没这个位**，多出来的件会被引擎回落到脸皮材质 → 糊 |
| ② | **附属件带形变通道**，否则脸动它不动 | **要做**（用户裁定可捏脸）。**分两段两种搬法**：脸形段（1..59）= 把脸壳的位移场按最近邻 3 点搬到附属件；**表情段（60..100）= 按件对位搬**（眼球转动在眼球件、牙齿跟下颌在嘴件，见 §23.6） |
| ③ | **形状键顺序** Basis → 占位键 → KeyTime_1…**100** | 同上；`transfer_channels.py` 已内置这个顺序（占位键顶住帧 0） |
| ④ | **标定参照物用源模型自身反解**，男头用男表 | `build_head.py --gender male` 现成：眼球目标 `(0, 0.1280, 1.6839)`、眼↔嘴竖直距 `0.0795` |
| ⑤ | **材质名 ↔ 贴图名同源**：脸壳裸名 `head_henry_a`、其余加 `_eye` / `_mouth` | 直接照办 |
| ⑥ | **`deform_keys` 的幅度要跟位移场来源同源**（男女两套不同，只比"有没有"会踩坑） | **要做**：亨利的位移场来自蒂法头（xxFemale 那套）→ `deform_keys` 幅度也必须用那套 |
| ⑦ | **后处理 `install_pack.py` 是终点**：morphfix → skinfix → metaparts → 装机。"白编译 ≠ 可用" | 照跑 |

### ✅ 换头脚本对 KCD 源**基本可用**（这是本轮最好的消息）

已逐条核过 `build_head.py`（2606 行）：**定向、缩放、骨架绑定、导出规格四块都与骨名无关**，KCD 源直接能用。
- 定向 = 「头壳中心 → 眼球中心」自动算偏航（:2068-2078），不看骨名、不看源轴制
- 缩放 = `--scale auto` 用「眼↔嘴竖直距离」归一 —— **源是米制就 k≈1，是厘米制就 k≈0.01，自动兼容**
- 骨架 = 源骨架整块删掉，重新绑官方 `human_skeleton.fbx`

**要显式给的东西**（不改代码就能跑）：`--gender male`、`--cut-z`、`--pick`（显式挑件，**头发必须写进去**，否则被默认黑名单丢掉）、`--weights-from <原版 head_male_a.fbx>`、`--neck-z 1.600 --neck-band 0.05`。

**必须改代码的 3 处**：

| # | 位置 | 问题 | 修法 |
|---|---|---|---|
| 1 | `build_head.py:115-124` | `patch_importer()` 缺 armature_setup 那道补丁 | 补上（照 `identify_parts.py` 已改的版本） |
| 2 | `build_head.py:1793-1830` | 硬判据 `_HEAD_BONES={"bone_10","bone_11"}` → KCD 头发（绑 `Head`）会被**整块删光** | 传 `--no-head-only`（或把白名单参数化） |
| 3 | `build_head.py:665`、:718-726 | `prune_far` 默认 `keep_bones=("bone_10","bone_11")` 对 KCD 永不命中 → 远端发片 `m_hair_henry_Cards` 当离群碎片清掉 | 传 `--no-prune`，或把默认参数化 |

🔴 **不要用 `--t-s`（T 模式）** —— 它整套判据都是战无2 的厘米/骨名假设（锚骨 `bone_11`），对 KCD 取不到。

---

## 四、脸：路线已定 —— **全规格（可捏脸）**，对齐萨菲罗斯

用户 2026-09-19 裁定：**亨利的脸要能被捏脸参数拉动** → 走萨菲罗斯那条全规格链，②③⑥ 三条规格全做。

### 4.1 落在哪：专属 race `lwn_henry`

`TifaHead2` 现在 `human` 种族的 `man` 皮肤 = 萨菲罗斯、`woman` = 蒂法。亨利**另开一个 race**（范本 = 织田信长的 `lwn_nobunaga`，已在 `Taikou\ModuleData\skins.xml` 跑通），该 race 的 `man` 皮肤挂亨利头 + **非零的 `deform_keys`** → 这个 race 下的角色脸型可被捏脸参数拉动。

> ⏳ **待落实的一个点**：玩家怎么在捏人界面"选到"亨利（自建 race 进捏人的接线，参见 wheels 卷十三 §三「自建 race 开新人物外观完整清单」与雷 139「捏人『选 race 套默认身型』」）。阶段 4 开工前查清。

### 4.2 要跑的三步（萨菲罗斯同款）

```
build_head.py      挑件 → 切嘴 → 标定（男表）→ 合缝 → 抄权重 → 出 FBX
transfer_channels.py  搬 101 条 KeyTime 位移场（纯几何最近邻，两段两源 —— 见 [§23.6](../Knowledge/蒂法换头工程.md)）
make_head_textures.py 源贴图 → 5 张引擎槽位贴图（_d/_n/_s/_mouth_d/_eye_d）
  ▼ 关卡 1 fbx_probe.py 验导出规格
  ▼ ModKit 编辑器编译 Publish（🔴 要你开 ModKit）
  ▼ install_pack.py：morphfix（补帧 60→101）→ skinfix --fullmat（四角色材质配方）→ 装机
```
🔴 **一条命令重跑整条链**：`python tools/face-pipeline/scripts/build_head_chain.py --recipe henry`（源、按件对位、参数全在配方里）。

🔴🔴 **`install_pack.py --clear-flags` 已裁定：`--clear-flags` 必须【不加】（保留 flags）** —— 2026-09-19 实机验证推翻了当天早些时候的"清空"裁定。

**经过**：先按"清空"跑了一版 → 实机**眼睛和嘴变成脸的贴图**（正是 `install_pack.py:143-160` 注释里写的"清掉 = 脸部贴图生成器认不出哪块是脸/嘴/眼/睫 → **把脸皮合成贴到了眼球上**"）→ 重跑一版**不加** `--clear-flags` → 标记齐全（`face_base_mesh` / `face_eye_mesh` / `face_mouth_mesh`）→ 实机眼睛正常。

🔴 **下面的推导过程全部作废**（拿 UV 落点离线量来判断属于哪一类 —— 8×8 覆盖两套都填满、区分不出来，那个量法**不够格当判据**）。**硬判据只有实机症状。**

<details><summary>（作废的推导，留档）</summary>



判据出处 = `install_pack.py:143-160` 的注释，分两类：**① UV 沿用源模型自带布局**（战无2 那 28 张脸、织田信长）→ **必须清空**；**② UV 对齐原版画布**（蒂法/萨菲罗斯 —— 注释原文「**当年专门做过对齐**」）→ **必须保留**。两类症状**一模一样**（眼睛糊成一片），判错极难归因（09-16 按①一刀切把蒂法/萨菲罗斯一起扫过，是事故记录）。

**亨利属①**，三条依据：
1. **没做过 UV 对齐** —— ②的前提是"专门做过对齐"这一步，亨利没做（UV 是源模型自己的排布，只是恰好铺满 [0,1]²）。
2. **五官落点与原版完全对不上**（实测解剖点 → UV）：鼻尖 亨利 (0.500, 0.612) vs 原版 (0.995, 0.991)；头顶 (0.097, 0.960) vs (0.013, 0.764)；后脑 (0.980, 0.217) vs (0.506, 0.382)。
3. 引擎带标记的行为 = **按原版画布布局把五官画到脸贴上** —— 落点对不上就是画到错的地方。

**代价知情**：清空 = 引擎的 FaceGen 不会给亨利自动适配肤色/年龄（贴图是我们自己出的，视觉上不依赖它）。
**若以后要走②**：需要额外做一步「把亨利的 UV 重排成原版画布」—— 独立的活，不难但要专门做。

</details>

**现任结论：亨利属②（保留 flags）。**

### 4.3 亨利的脸要按 3 件重做（男头规则）

| 骑砍男头槽位 | 亨利的源件 | 动作 |
|---|---|---|
| **脸**（裸名 `head_henry_a`） | `m_head_henry`（8323 顶点 / 8 连通域） | 收肩领口（`--cut-z` + 标定）、8 域归类 |
| **眼**（`head_henry_a_eye`） | `_Eye{L,R}Ball` + `_Eye{L,R}Iris` 4 件 | 合并成 1 件 |
| **嘴**（`head_henry_a_mouth`） | `m_head_henry_Teeth`（牙齿+舌头）+ 脸壳里的口腔内衬（708 顶点） | 合成 1 件 |
| ❌ 无此槽位 | `m_head_henry_Eyelashes`（睫毛 912）/ `_Eyeshadows`（眼影）/ `_Tearline`（泪线） | 睫毛倾向**烘进脸贴图**；眼影/泪线丢弃 |

**多出来的件必须处理掉** —— 引擎给脸部件分配贴图是**按子网格位置**算的，第 4 件之后会回落到脸皮材质 → 眼球/嘴/眉全糊上脸的贴图。

### 4.4 必须改的代码（`build_head.py`）—— ✅ 已落地；实测**实际是 5 处**（原判 3 处，阶段 4 开工补齐另 2 处）

| # | 位置 | 问题 | 修法 |
|---|---|---|---|
| 1 | `patch_importer()` | 缺 armature_setup 那道补丁，**且导入源模型时根本没调用它**（原脚本只在导 `--weights-from` 时打）→ KCD 源导不进来（`KeyError: None @ link_hierarchy`） | 补上第二道 + 导入源模型前也调一次（补丁幂等，只在该崩时兜底） |
| 2 | 1.4b 的 `_HEAD_BONES={"bone_10","bone_11"}` | KCD 头发绑 `Head` → 会被**整块删光** | 传 `--no-head-only`（本配方不挑 hair，实际空转，作保险） |
| 3 | `prune_far` 的 `keep_bones=("bone_10","bone_11")` | 对 KCD 永不命中 → 远端碎片被当离群清掉 | 传 `--no-prune` |
| 4 | 归并之后那行 `matrix_world = Identity` | 🔴 **对象世界矩阵被丢掉** —— 导入器把「单位换算 + Y-up→Z-up」放在**对象矩阵**里，KCD 源顶点是**厘米 + Y-up**；丢掉 = 头壳中心算到 y=161、"眼↔嘴"标定按厘米制缩 100 倍 | 新增 `--apply-src-xform`：把世界矩阵**烘进网格数据**。默认不开（战无2 那条线是在"丢掉矩阵"的前提下调通的，改默认会打乱它整套判据） |
| 5 | 落位自检那行 `"目标 %s" % tuple(...)` | 单占位符喂 3 元组 → `TypeError`，**非 T 模式必崩**（T 模式走另一分支所以一直没暴露） | 加尾逗号成 1 元组 |

顺带新增两个能力（都在 `build_head.py`，**默认行为一字未改**）：
- `--pick` 关键字前缀 `=` = **整名精确匹配**（`face==m_head_henry`）—— KCD 的 `m_head_henry` 是 `_Teeth`/`_Eyelashes`/`_Eyeshadows`/`_Tearline` 的子串，子串匹配会把睫毛/眼影/泪线一起并进脸壳。
- `--apply-src-xform`（见上表第 4 条）。

🔴 **不要用 `--t-s`（T 模式）** —— 它整套判据都是战无2 的厘米/骨名假设（锚骨 `bone_11`），对 KCD 取不到。

---

## 五、甲：照 [骑砍2盔甲资产工程.md](../Knowledge/骑砍2盔甲资产工程.md) 的既有裁定做

### 5.1 🔴 三条**不能照抄战无2**的（2026-09-19 实测，照抄必翻车）

| # | 战无2 幸村甲 | **KCD 亨利** | 依据 |
|---|---|---|---|
| 1 | **Y 轴镜像**（源是镜像件：前 −Y / 左 −X；反射 → 必须**反转面绕序**） | 🔴 **绕 Z 转 180°**（源是正常人形：前 −Y / 左 **+X**；骑砍前 +Y / 左 −X）→ **刚体旋转，不用反转面绕序** | 五条独立证据一致：脚趾朝 −Y、`mhenry_eye_left_ext` 在 +X、`legsplate03_KneePlate_l` 在 +X（美术命名与骨命名互证）、`hood06_liripipe_01` 与 `BackSlot_shield` 在 +Y（在身后）、头网格 y[−0.156, 0.088] |
| 2 | `--r 0.0120` / `--r-arms 0.0160`（含 20% 的"原模型偏小"补偿） | 🔴 **`--r ≈ 0.99`** —— 亨利骨架本来就是米级真人尺寸 | 16 段段长比：中位 **0.992**，极差 15.2%（大腿 0.996 / 小腿 0.963 / 上臂 1.053 / 前臂 0.993 / 总高 0.981） |
| 3 | 手臂姿态差 33.5° | **差 ~14.5°**（源是 A-pose 下垂 47.5°，骑砍 33.4°）→ 出甲要转：上臂 14.5° / 前臂 16.4° / 锁骨 13.0° / 足 15.6°；**腿不用修**（2.9°/1.8°） | 逐关节方向夹角 |

### 5.2 直接沿用那份文档的结论（不重新论证）

| 裁定 | 对亨利意味着什么 |
|---|---|
| 🔴 **权重用源件自带的，不从原版身体抄**（§0.4 #1） | KCD 源本来就绑好了（`cuirass07_torso` 16 骨、`gauntlets05` 40 骨）→ **直接吃源权重** |
| 🔴 **旋转只能从关节位置推，且只给手臂链推**；不要用 `bone.matrix_local` 的骨轴（§0.4 #3/#4） | 原样照做 |
| 🔴 **手臂缩放拆「径向 / 沿骨轴」**（§0.4 #5） | 原样照做；径向粗细**骨骼量不出来**，起步给 0.99，靠渲染核 |
| **LOD 0~5**（不是 0~6），减面比 `0.834/0.563/0.249/0.140/0.072`（§3.1） | 原样照做 |
| **参考物**：原版甲当结构模板、原版身体当权重来源、素材模型当轮廓真源（§2.3） | 亨利甲的结构规格照原版甲 |
| **甲不打后处理补丁**（`morphfix`/`skinfix` 是脸部专用）（§5） | 省一步 |
| **物品注册**：目录级、`mesh=` 扁平名、`<Item>` 内部禁塞注释（§4.1/§4.2/§7 坑 12） | 照办 —— ⚠️ 且 `taikou_items/weapons.xml` 是 `gen_weapon_items.py` 的**输出**（铁律 22 禁手改）→ 亨利单开一个文件 |

### 5.3 ✅ 骨映射表已交付（阶段 3 的前置，本次做完）

产物在 `tools/kcd-pipeline/`：

| 文件 | 内容 |
|---|---|
| `map_kcd_henry.json` | **映射表数据**：94 条显式映射 + 19 条解剖段定义 + `flip=z180` —— 出甲脚本直接读 |
| `scripts/dump_skeletons.py` | 骨架对照（`kcd`/`bl`/`pair` 三模式，`pair` 打残差/段长比/方向夹角） |
| `scripts/dump_bone_usage.py` | 逐网格统计「哪根骨真在驱动它」的权重占比 |
| `out/` | `skel_kcd.csv`(484 骨) · `skel_bl.csv`(31 骨) · `pair.txt`(完整 94 行残差表) · `bone_usage.*` |

**总账**：KCD 484 骨 = 显式映射 **94** + 父链兜底 **388** + 丢弃 **2**（rig 根骨与 FBX 残留节点，权重都是 0）；骑砍 28 根真骨 **28/28 全命中**。

**🔴 一处与工程既有惯例的故意偏离**：盔甲文档 §1.2 说「骑砍上臂/前臂各拆两根、源只有一根 → 映射到前一根」。**KCD 不是一根** —— 它沿肢体插了助手骨（上臂 3 根：0%/33%/67%；前臂 4 根：0%/25%/50%/75%），而骑砍这两段**各自恰好对半切**。所以按「落在前半段→前一根，后半段→后一根」分。**不是洁癖**：袖网格 23.3% 的权重压在两根 `ArmRoll` 上、籠手最大前臂权重是 `LeftForeArmRoll2` —— 全塞给肘部那一根，腕口/袖中段会不跟手。

**⚠️ 别把装备挂件骨当低优先级**：`cuirass07_torso` 有 **14.1%** 权重压在 `satchel_cuirass07_torso_01`（背包）上；`scabbard_longswordcapon` 有 **49.8%** 压在 `LeftWeaponSheath` 上。映射错了 = 甲上挖洞。

### 5.3 ⚠️ 一个还没验的前提：贴图通道

`build_textures.py` 吃的是**原图集 PNG**（`--diffuse`）+ 程序生成 `_n`/`_s`。亨利有现成的 `COLOR`/`NORM`/`RMA` 三张，**比战无2 条件好**，但：

- 🔴 **`_s` 的通道约定** = `R 金属度 / G 255−粗糙度 / B 环境光遮蔽`（盔甲文档 §5）；KCD 的 `RMA` 是打包图，**三通道顺序仓库里没有任何记录**（战无2 源工程记的是 R=AO/G=粗糙/B=金属，**与骑砍正好相反**）。**喂错 = 金属感全乱**。阶段 3 开工前先验通道。
- 甲件贴图是 4096²，直接进编辑器太重 → 按参照的尺寸预算裁/缩。

### 5.4 武器（长剑 + 剑鞘 + 鸢盾）

**静态网格**（不要骨架、不勾 Skinning），按武器约定规整：**原点在握持点 · 长轴 +Z · 刃宽 X · 刃厚 Y**；碰撞不走网格，走引擎内置的 `body_name`（近战可用物理体就十来个，按语义挑，别全用同一个）。

现有 `build_weapon.py` 三处写死（材质前缀 `mat_w_`、手骨族 `bone_18/19/26~45`、**厘米→米 ×0.01**——KCD 是米制会被缩成 1/100），KCD 源会在 :93 直接 `sys.exit("!! 骨架里没有手骨")` → **要写 KCD 版**，但逻辑最简单。

---

## 六、分阶段

| 阶段 | 做什么 | 产出 | 谁做 |
|---|---|---|---|
| **0** | 解包 + 结构勘察 + 导入器补丁 | ✅ **已完成** | 我 |
| **1** | 零件表 + 接触图（认件） | ✅ **已完成** —— 25 件认实，纠正 1 处（`waist002_FINAL` 是锁子甲裙不是"腰第二层"） | 我 |
| **2** | **武器** | 🔶 **长剑做完**（网格+3 贴图+物品定义）；**鸢盾/剑鞘悬置**（见 §八 风险 1/2） | 我 |
| **3** | **甲** | 🔶 **首版甲已出**（7 件合一，6 级 LOD，合身检查通过）；贴图/收尾未做 | 我 |
| **4** | **脸**（`build_head` → `transfer_channels` → `make_head_textures`，按 §四 全规格） | 🔶 **产物全出**（FBX + 5 贴图 + 渲染图，关卡 1 与材质闸门通过）→ 见 §十 | 我 |
| **5** | 编辑器编译 + `install_pack.py` 后处理 + 装机 | ⏳ 🔴 **要你开 ModKit** | 你+我 |

---

## 七、要动的工具 / 新增的目录

| 东西 | 归属 | 状态 |
|---|---|---|
| 导入器补丁（`identify_parts.py`） | `tools/sw2-pipeline/scripts/` | ✅ 已改 |
| `build_weapon.py` 参数化（`--pick-name`/`--hand-bones`/`--grip-mode`/`--scale`）+ 修一处潜伏 bug（`hold = c + e1*th` 应为 `c + u*th`，刀尖反向时把握持点算到另一头） | `tools/sw2-pipeline/scripts/` | ✅ 默认值等价原行为 |
| `render_weapons.py` 加 `--glob` | 同上 | ✅ |
| 导入器补丁（`build_head.py`） | `tools/face-pipeline/scripts/` | ⏳ 阶段 4 前补（§4.4 第 1 条） |
| **KCD 工具链** `tools/kcd-pipeline/` | 新目录 | ✅ 已建：`split_rma.py`（RMA→_s）· `map_kcd_henry.json`（骨映射）· `dump_skeletons.py` · `dump_bone_usage.py` |
| KCD 版出甲脚本 | 同上 | ⏳ 阶段 3 |
| KCD 版出武器（盾/鞘） | 同上 | ⏳ 悬置 |
| 源包解包产物 | `Debug/offline/_kcd_recon/` + `Debug/offline/KCD/` | ✅ |

**能直接复用的**（已核）：`transfer_channels.py`、`make_head_textures.py`、`install_pack.py`、`fbx_probe.py`、`tpaccli` 全家、`png_for_editor.py`。

---

## 八、风险 / 未解

1. **🔴 鸢盾朝向没解决 —— 悬置**。原版 `kite_shield_a` 的网格空间是**平放**的（盾面朝 Z、高度沿 Y、握把在 −Z 背面），再靠 Item 的 `rotation="0.0,10.0,40.00"` 摆正。要把亨利的盾对到同一套空间、且握住点落在握把上，得先搞清那套旋转的语义。**没搞清前不猜**（猜错 = 白跑一轮 ModKit）。
2. **剑鞘没有天然物品位**。骑砍的"收鞘"是 `item_holsters` 机制（挂在骨上的占位），鞘本身不是独立物品。要让鞘出现，得并进甲网格或做成披风类附件。**等你定**。
3. **手臂 Roll 骨的分段归属** —— 置信度中（与工程既有惯例的故意偏离，见 §5.3）。实机若见腕口/袖中段不跟手，优先怀疑这条。
4. **`Neck1`→`head_13` 还是 `neck_12`** —— 置信度中；若兜帽领口跟头转得太凶，改 `neck_12`。
5. **z180 轴向没有实机验证** —— 坐标证据 5 条一致，但第一次出甲要先验：甲背朝前 = 判断反了。
6. **径向粗细（`--r-arms`）完全没测** —— 骨骼量不出粗细，起步 0.99，靠渲染核。
7. **脸的 8 个连通域归类** + **睫毛/眼影/泪线去留**（男头只有 3 件）。
8. ~~`--clear-flags` 判据未定~~ → ✅ **已裁定：【不加】`--clear-flags`（保留 flags）** —— 2026-09-19 实机验证；此前"清空"的裁定已作废。见 §4.2。
9. **皮肤颜色接不上** —— 萨菲罗斯的遗留问题（§21.9）。
10. **亨利怎么在捏人界面被选到** —— 自建 race 进捏人的接线。
11. **🆕 成品进哪个模块（§六阶段 5 之前必须定）** —— 用户答的是 `TifaHead2`，但按工程记录 **TifaHead2 不在游戏启动参数里**（它的 `SubModule.xml` 是空 `<SubModules/>` + 空 `<Xmls/>`），`skins.xslt` 当初就是因为这个才挪到 Taikou 的。**按既有做法：制作工程 = TifaHead2，运行期资产与 XML = Taikou**。本次已按这条把长剑的物品定义写进 `Taikou/ModuleData/taikou_items/kcd_henry_items.xml`（**新文件，不碰生成物**）—— 若你要改，说一声就搬。
12. **编辑器编译必须你开 ModKit** —— 阶段 5 的硬前置（铁律 31）。

---

## 九、本次已完成

**阶段 0（勘察）**：源包解包到 `Debug\offline\_kcd_recon\`；25 网格认件 / 8 连通域 / 关键骨位 / 贴图规格；Blender 导入器补丁（进正式工具）。

**阶段 1（认件）**：25 块零件的测量表 + 正/背两张接触图 → `Debug\offline\KCD\kcd_parts\`；材质↔贴图全对应表（发现牙齿与头共用贴图、`Empty_col.png` 是空占位槽）。

**阶段 2（武器）**：**长剑全链做完** ——
- `kcd_henry_sword_a.fbx`：2667 顶点，原点=握持点、长轴 +Z（刀尖朝上）、护手沿 X；全长 125.5cm
- `kcd_henry_sword_a_d/_n/_s.png`（2048²，都已过 `png_for_editor.py`）
- 物品定义 `Taikou/ModuleData/taikou_items/kcd_henry_items.xml`

**🔴 顺带解决的三个前置问题**：
1. **`--grip-mode guard`** —— KCD 的武器是**摆在原点的道具散件**（不在手里），手骨启发式必然失效（实测手离剑 55cm）。新判据 = 找最宽横截面（护手）定刀尖向与握持点。刀尖朝上的结论由两个**贴花件在 Z=12~35cm**独立佐证。
2. **RMA 通道顺序实锤** —— `R=粗糙度 / G=金属度 / B=AO`。证据：皮肤与布 G=0.00、钢板 0.46、剑 0.76、锁子甲 0.99。骑砍 `_s` 是 `R金属 / G(255−粗糙) / B AO` —— **直接拿 RMA 当 `_s` 用会让金属和粗糙对调**。转换脚本 `tools/kcd-pipeline/scripts/split_rma.py`。
3. **KCD → 骑砍骨映射表**（见 §5.3）—— 94 条显式映射 + 388 条父链兜底，骑砍 28 根真骨全命中；**并且纠正了"KCD 同战无2 需 Y 镜像"这个错判**（实为绕 Z 转 180°）。

---

## 九、本次已完成

- 源包解包到 `Debug\offline\_kcd_recon\`（FBX + 63 张贴图 + 7 张实拍图）
- 结构勘察：25 网格认件 / 8 连通域 / 关键骨位 / 贴图规格（脚本 `_kcd_struct.py` `_kcd_meshes.py` `_kcd_head.py` `_kcd_islands.py` `_kcd_probe.py` 同目录，`_` 前缀 = 临时物，不进 git）
- Blender 导入器补丁（已进正式工具）
- 换头/出甲/出武器三条现有管线对「非战无2 源」的可用性普查

---

## 十、阶段 4（脸）本轮结果 —— 2026-09-19

**一句话**：亨利的头已经跑成骑砍2 头部资产（3 子网格 + 5 贴图），关卡 1 与材质闸门全过，渲染图形似本人；**下一步是开 ModKit 编译**。

### 10.1 一条命令重跑

```
python tools/face-pipeline/scripts/build_head_chain.py --recipe henry
```
产物落到 **`Debug\offline\自定义头\henry_build\`**（萨菲罗斯的在 `seph_build\`，两者不混）。
贴图另跑 `Debug\offline\自定义头\henry_build\_tex.py`，渲染跑 `_render.py`。
参数配方都在 `build_head_chain.py` 的 `RECIPES["henry"]` 里，**不用手敲**。

### 10.2 标定结果（自动算出来的，不是手输的）

| 项 | 值 | 与参照比 |
|---|---|---|
| 缩放 k | **1.1216**（源眼↔嘴 0.0709 m → 男表 0.0795） | 源是米制，但标定按**比例**走，不是按单位 |
| 眼球落位 | (0.000, 0.128, 1.6839) | 原版男头目标值，误差 0 |
| 全头包围盒 | x[-0.094, 0.106] y[-0.063, 0.186] z[1.430, 1.809] | 原版男头 x±0.093 y[-0.071,0.172] z[1.414,1.811] |
| 发顶 z | 1.809 | 原版 1.811（差 2mm）—— 头骨比例对上了 |
| 收领口 | 332 顶点、最大收进 90.3mm | 源模型的"肩/胸口"宽 ±0.146m，必须收 |

### 10.3 三条**没做/没验证**的（交接要点）

1. **睫毛 / 眼影 / 泪线 = 直接丢弃**（没烘进脸贴图）。男头没有第 4/5/6 个槽位，留着会糊眼睛。**睫毛要不要烘进贴图仍待定**（用户裁定）。
2. **正前 V 领口最低处（z=1.4144）比头的下沿（1.4300）低 1.6cm** —— 理论上会露出一条 1.6cm 的缝。试过 `--neck-fill`（铺下摆）补救，**结果更差**（铺出硬棱面 + 拉伸 UV + 颈后一道竖缝，对比图在 `henry_build\render_neckfill\`）→ 先按"不收下摆"出。**是否看得见要实机定**。
3. ~~`install_pack.py --clear-flags` 判据未定~~ → ✅ **已裁定：【不加】`--clear-flags`（保留 flags）**（见 §4.2，实机验证）。⚠️ 取产物时注意：不加 clearflags 时脚本**跳过 metaparts**，`s3` 是陈旧产物 —— **要拿 `s2`**。

### 10.4 顺带确认的机制事实

- KCD 源（FBX + 对象矩阵）的顶点是**厘米 + Y-up**，"米制"只在对象矩阵里 —— 凡 KCD 源进 `build_head.py` 都必须 `--apply-src-xform`。
- 亨利的脸壳是**一整块连通壳**（不像萨菲罗斯分前后两块）→ **不用 `--weld-seam`**，强行合缝有把眼窝焊死的风险。
- 嘴件（牙齿+舌头）**与脸共用同一张图集** → `_mouth_d` 必须是**整张脸图**（给 512 会把牙缩糊）；眼球有独立图（1024²）。

---

## 十、🔴 导入核对表（材质名 + 贴图 + 编辑器设置）—— 开编辑器前照着核

> **为什么材质名是硬的（铁律 27）**：编译后的后处理 `install_pack.py` 的 `skinfix --fullmat` **按材质名判角色**
> （`MatRole()` 只看名字里含不含 `mouth`/`lash`/`eye`，**不看子网格顺序**）。
> 名字错或重名 = 几件刷成同一个配方 = **眼睛和嘴糊上脸皮**，而且要到实机才看得出来。
> 自建头的命名规则：**脸壳用裸名，其余件加角色后缀**，角色词固定 `eye`/`mouth`/`lash`/`neck`。

### 10.1 脸 —— `head_henry_a_v2.fbx`（3 件，男头顺序 脸→眼→嘴）

| 子网格序号 | 网格 | 顶点 | **材质名（必须逐字一致）** | 挂哪些贴图 |
|---|---|---|---|---|
| 0（第 1 件） | `head_henry_a.0` | 8323 | **`head_henry_a`** | `head_henry_a_d.png` / `_n.png` / `_s.png` |
| 1（第 2 件） | `head_henry_a.1` | 1400 | **`head_henry_a_eye`** | `head_henry_a_eye_d.png` |
| 2（第 3 件） | `head_henry_a.2` | 1346 | **`head_henry_a_mouth`** | `head_henry_a_mouth_d.png` |

- 亨利是**男头**，只有 3 件 → **没有 `_lash`**（睫毛已烘进脸贴图）
- 脖子长在脸壳里，**没有 `_neck`**
- 眼/嘴**只有 `_d`**（`make_head_textures.py` 不产那两件的 n/s）

### 10.2 甲 ×4 / 武器 / 盾 —— 每件 1 个材质，材质名 = 资源裸名

| FBX | **材质名** | 贴图 | LOD |
|---|---|---|---|
| `kcd_henry_body_a.fbx` | `kcd_henry_body_a` | `_d` / `_n` / `_s` | 0~5（6 级） |
| `kcd_henry_legs_a.fbx` | `kcd_henry_legs_a` | `_d` / `_n` / `_s` | 0~5（6 级） |
| `kcd_henry_arms_a.fbx` | `kcd_henry_arms_a` | `_d` / `_n` / `_s` | 0~5（6 级） |
| `kcd_henry_cape_a.fbx` | `kcd_henry_cape_a` | `_d` / `_n` / `_s` | 0~5（6 级） |
| `kcd_henry_sword_a.fbx` | `kcd_henry_sword_a` | `_d` / `_n` / `_s` | 单件（**武器不要骨架/不勾 Skinning**） |
| `kcd_henry_shield_a.fbx` | `kcd_henry_shield_a` | 待出 | 单件 |

### 10.3 编辑器设置（照 [骑砍2盔甲资产工程.md](../Knowledge/骑砍2盔甲资产工程.md) §0.3）

- 导入 FBX：**unit = m** / **不勾 Z-up** / **只勾 Import meshes**
- 材质勾 **`Bumpmap` + `Skinning`**
  🔴 **`skinning` 在材质上、FBX 不携带** —— 漏了 = 网格钉死在绑定姿势、完全不跟骨架动
  （武器**不要**勾 Skinning —— 它是静态网格）
- 贴图挂到 `tex[0]` / `tex[2]` / `tex[4]`（= `_d` / `_n` / `_s`）
- Publish **目标选模块外**（选模块内会得到 `Modules/X/X/` 嵌套）
- 🔴 每次重导 FBX，材质的 shader/flags/VertexLayout **会被刷回默认** → 导完回看三样还在不在
