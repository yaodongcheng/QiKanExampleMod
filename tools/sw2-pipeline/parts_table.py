# -*- coding: utf-8 -*-
"""parts_table.py —— 战无2 28 个有名武将的【挑件表】。

🔴 **helmet 列的资格（2026-09-15 用户裁定，别再靠数据猜）**：战无2 里**只有 9 人戴盔** ——
   上杉谦信 / 武田信玄 / 伊达政宗 / 服部半藏 / 丰臣秀吉 / 本多忠胜 / 德川家康 / 浅井长政 / 直江兼续。
   **其余 19 人"头顶那块"就是整个头（含头发），不是盔** —— 曾经把幸村/光秀的**头发**当兜做成了头盔
   （渲染出来是一顶"头罩"），已撤销。判断角色设定**必须问用户**，数据里推不出来。

序号含义：`<角色>_parts.csv` 的 `idx` 列 = 接触图格子上的黄色数字 = **按对象名排序的行号**。
    ⚠️ 不是子网格号！例：织田信长的脸是 idx 11，它的对象名叫 `..._submesh_6_...`。
    搞混过一次（写 face=6 结果挑中了 submesh_1），所以这张表统一只写 idx。

怎么来的：`identify_parts.py` 量出每块零件的骨骼/高度/主色 → 机器按硬规则初判
    （眼球=38顶点60面、武器=材质名带 mat_w_、脸=主导骨 bone_46 占比最高），
    头发/兜 由接触图 + 逐件单独渲染人工确认。
    逐条核对记录见 `Debug/offline/sw2_parts/`（离线产物）。

三个身份字段：
    face     脸壳（**必须**）
    eye      眼球（**必须**）
    hair     角色自己的头发 —— 会**并进脸壳**（不并就是光头/怪相）
    helmet   兜（金属头盔）—— **这一轮不做**（推迟到护头管线），列出来只是防止误当头发并进去
    weapons  武器件 —— 阶段 3.5 做武器时用
    mouth    源模型都没有，由 build_head.py `--cut-mouth` 从脸壳切出来

gender 决定 `--parts`：
    male   -> face,eye,mouth       （3 件，照原版 head_male_a）
    female -> face,mouth,eye,lash  （4 件，照原版 head_female_a；战无2 源模型无独立睫毛件，
                                    睫毛走脸皮贴图，所以 lash 用脸壳复制或省略，见批量脚本注释）
"""

# taikou = Taikou 的 StringId（对照表见 Knowledge/战国无双换装工程.md §7.15）
TABLE = {
    # ---- 组 1 ----
    "L00_yukimura": dict(cn="真田幸村", taikou="lord_tk5_361", asset="head_yukimura_a", gender="male",
                         face=[11], eye=[12], hair=[5], helmet=[], weapons=[4, 7],   # 2026-09-15 用户裁定：这块是头发/头饰（不是盔）→ 挪进 hair 并进脸壳
                         note="idx3 是六文銭头巾的两条垂带（bone_11）；🔴 idx5 看图是深色盔内衬 + "
                              "两片飞到两侧的碎片（碎片把包围盒撑到 ±51cm → 并进脸壳会让『收领口』狂收 543mm），"
                              "判为兜/内衬，**不并**。脸壳 idx11 自带发带，本身就是完整的头"),
    "L01_keiji": dict(cn="前田庆次", taikou="lord_tk5_657", asset="head_keiji_a", gender="male",
                      face=[14], eye=[15], hair=[17, 6], hard=True, helmet=[], weapons=[4, 9],
                      note="🔴 2026-09-15：idx6（sub11，491 顶点）是【长发 + 整套衣服】混装件 —— "
                           "绑头骨 bone_11 的 254 顶点是头发（源 z188.7~235.2），"
                           "其余是衣服（手臂 bone_16/17 在 x±85、躯干 bone_1/2/4、胸口 bone_9 在 z134~187）"
                           "—— 两边 z 完全不重叠，所以走 hard 硬判据能干净切开。"
                           "不加 hard 的后果（用户实机截图）：头旁边飘着一堆虎纹甲片，头包围盒 1.83 米宽"),
    "L02_nobunaga": dict(cn="织田信长", taikou="lord_tk5_195", asset="head_nobunaga_a", gender="male",
                         face=[11], eye=[12], hair=[5], helmet=[], weapons=[3],
                         note="🔴 已完成（race lwn_nobunaga）。头顶黑发已在脸壳里，idx5 是后发/刺发团"),
    "L03_mitsuhide": dict(cn="明智光秀", taikou="lord_tk5_14", asset="head_mitsuhide_a", gender="male",
                          face=[10], eye=[11], hair=[5, 6, 13], helmet=[], weapons=[3],
                          seal=[6],   # 🔴 idx6 的发块底边有个【方口】（源 z157~161），原模型靠和服立领挡着；
                                      #    头里没有领子 → 剪影上一条 16cm→3cm 的缺口。seal 把它补上。
                          note="idx6 是「整套衣服」一块（盔/头巾夹在里面），要按骨骼筛；"
                               "🔴 idx13 接触图上是一整条裙子（机器判 head_area，人眼看成裙子漏掉了）——"
                               "实际裙子只占它 2/3，另藏着【前刘海框】：绑 bone_11 的 70 顶点 + 绑面部骨的 38 顶点，"
                               "过一遍「剔非头部」正好剩这 108 顶点、裙子全被剔掉。不并 → 前面刘海和两侧垂发整块没有"),
    "L05_kenshin": dict(cn="上杉谦信", taikou="lord_tk5_119", asset="head_kenshin_a", gender="male",
                        # 🔴 2026-09-15 用户裁定（归属完备性）：**模型里每一个部件都必须恰好属于
                        #    头 / 武器 / 甲 / 兜 / 四肢 之一，不允许"谁都不属于"**。
                        #    白頭巾（含两条垂带）原本混在 `idx9`（sub3，760 顶点，普查判「甲件（袖/籠手）」）
                        #    里 → 甲侧 `--drop-head-idx` 把这 197 个绑头骨的顶点剔掉后，
                        #    它既不在甲、也不在头 → **按裁定它就是头盔**，加进 helmet 列。
                        #    实测：`check_partition.py` 报「漏 22 片 / 299 顶点」，其中 197 顶点是它。
                        #    （金属兜 idx4 仍做不出来，见下面 note 的 T7。）
                        #    `idx10`（sub4）同理：它是复合件（额头金前立 + 胸/肩甲片），
                        #    甲侧剔掉它 18 个绑头骨的顶点后同样"无归属" → 按裁定归头盔。
                        #    ⚠️ 2026-09-15 更正：`idx15`（sub9）**不是头盔垂带** —— 按件渲图
                        #    （`Debug/offline/_hL_kenshin_sub9.png`）是**两片 1.3 米长的大布片/袍**，
                        #    披在身上 → 归【甲】，由 `build_armors.FORCE_ARMOR` 补进去。
                        #    `idx15`（sub9）是**混合件**：身体部分是那两片 1.3 米长大布片（→ 甲，
                        #    见 build_armors.FORCE_ARMOR），头部那 30 个绑头骨的顶点归【兜】
                        #    （骨判据自动切，**不要**用 helmet_whole —— 那会把布片整块并进兜，实机踩过）。
                        face=[11], eye=[12], hair=[], helmet=[9, 10, 15], weapons=[4, 7],
                        note="🔴 2026-09-15 深夜：兜件已指认出来（idx4 = submesh_0..._0000.001，242 顶点，"
                             "碗 + 双角 + 耳庇 + 两条垂带），**但这一轮做不出来，暂时留空** —— "
                             "该件是**无蒙皮的静态网格（一个顶点组都没有）**，走兜管线会连踩三坑："
                             "① `--keep-head-frags` 按主导骨留件 → 主导骨为空 → 整块删光（实测余 0）；"
                             "② `build_armor.py` 假设「顶点已在骨架空间」直接清 `matrix_world` → 兜散架；"
                             "③ 补 bone_11 权重 + 烘到骨架空间后不再散架，**但重定向把它压成尖刺状**（形状错）。"
                             "⇒ 下一轮：查 retarget 对「整件单骨」的处理，或改走 head 管线（build_head.py 有完整的"
                             "标定/绑骨路径）。**别在没解决 ③ 之前把 helmet 填上** —— 会产出坏资产。"
                             "idx10（submesh_4，288 顶点）= 复合件（额头金前立 + 胸/肩甲片）。"
                             "idx15 是两条长布垂带（无发丝纹理）——按披发读也可算头发，先不并"),
    "L06_oichi": dict(cn="阿市", taikou="lord_tk5_1181", asset="head_oichi_a", gender="female",
                      face=[4], eye=[5], hair=[6, 7], helmet=[], weapons=[1, 3],
                      note="idx6 = 发髻 + 带金流苏的浅蓝肩衣（复合件，要按骨骼筛）"),
    "L07_okuni": dict(cn="出云阿国", taikou="lord_tk5_1208", asset="head_okuni_a", gender="female",
                      face=[4], eye=[6], hair=[9, 8], helmet=[], weapons=[1, 3, 5, 7],   # 2026-09-15 用户裁定：这块是头发/头饰（不是盔）→ 挪进 hair 并进脸壳
                      note="idx8 是金色前立+两侧笄的金饰组（不是金属盔）"),

    # ---- 组 2 ----
    "L09_magoichi": dict(cn="杂贺孙一", taikou="lord_tk5_321", asset="head_magoichi_a", gender="male",
                         face=[5], eye=[6], hair=[8, 9], helmet=[], weapons=[1, 3],
                         neck=[0], neck_r=12.0, neck_z0=154.0,
                         # 🔴 脖子从身体件补（2026-09-15 深夜，用户实机："杂贺只有头没有颈部"）：
                         #    他的脸壳件（idx5/submesh_3）在眼睛以下只延伸 12.2 单位（28 人里最短），
                         #    做出来就是个悬空的头。脖子画在身体件 idx0/submesh_0 里 ——
                         #    实测是两块 bone_9 碎片：33 顶点 / z 129~171 / |x| ≤ 9.7。
                         #    `neck_r=12` 排除肩甲（|x|=20）和手臂（|x|=93）。
                         #    `neck_z0=154` = **目标空间 z=1.44 换算回源坐标**的值
                         #      （眼源 z=175.40、缩放 0.011256 → 175.40 − (1.6839−1.44)/0.011256 = 153.7）。
                         #    源模型的脖子一直画到胸口（z=129），不裁的话头会拖出 0.5 米长的脖子穿进身体。
                         note="idx8 = 头发 + 长外套（复合件，按骨骼筛）；idx9 是脑后发尾。"
                              "🔴 他没有「发绳」——那个绿的是**束马尾的绿色头绳**，画在发尾根上，"
                              "属于 idx8/idx9 里的一小撮顶点，不是独立件（2026-09-15 深夜逐件扫过）"),
    "L10_shingen": dict(cn="武田信玄", taikou="lord_tk5_449", asset="head_shingen_a", gender="male",
                        face=[7], eye=[8], hair=[10, 9], helmet=[6], weapons=[2, 4],
                        note="idx6=金角+额甲+盔侧圆环（金属兜）；idx9 是白色长毛帘；idx10=红发+腰间红披（复合件）"),
    "L11_masamune": dict(cn="伊达政宗", taikou="lord_tk5_466", asset="head_masamune_a", gender="male",
                         face=[9], eye=[10], hair=[], helmet=[12, 7], weapons=[3, 5],
                         strap_bone="bone_59",  # 下巴那一横条（并进脸壳主网格，按碎片摘不掉）
                         strap=[[4.6,-5.7,156.1],[-4.6,-5.7,156.1],[-5.8,-6.6,160.3],[0.1,-10.6,165.0],
                             [-3.4,-9.7,162.3],[8.0,0.6,163.2]],   # 🔴 头盔颏带（美术画在脸壳件里，原模型靠兜的吹返挡着）：种子点=碎片重心，由逐碎片渲染人工确认（2026-09-15）。头里摘掉（build_head --strap-seed），做兜时加回去（见 build_helmets 的 STRAP）
                         note="头发在脸壳里；idx7=身甲+大金月牙前立（复合件）；idx11=口部小月牙片"),
    "L12_nouhime": dict(cn="归蝶", taikou="lord_tk5_1194", asset="head_nouhime_a", gender="female",
                        face=[8], eye=[9], hair=[6, 11], helmet=[], weapons=[3, 5],
                        note="idx6=身体+发髻（复合件）；🔴 idx11=蝴蝶+珠串+耳饰（头饰，不是头发）——"
                             "2026-09-15 深夜用户报「头饰消失」，漏件图实锤它在未挑中的件里，已补进 hair。"
                             "该件 44 顶点，进 1.3 会被判「非复合」整块放过，不会被误删"),
    "L13_hanzo": dict(cn="服部半藏", taikou="lord_tk5_587", asset="head_hanzo_a", gender="male",
                      face=[10], eye=[11], hair=[], helmet=[12], weapons=[2, 5, 7],
                      note="idx10=脸+布质头罩（复合件）；🔴 idx12 看图是「双角+顶刺」像兜，"
                           "但主导骨是 bone_14/15/17（肩臂族）不是头骨 → 实为**带角的头罩/披风**，"
                           "不是金属兜。两种解释都不并进头，所以不影响本轮"),
    "L14_rammaru": dict(cn="森兰丸", taikou="lord_tk5_736", asset="head_rammaru_a", gender="male",
                        face=[6], eye=[7], hair=[8, 10], helmet=[], weapons=[1, 3],
                        note="idx8=头发+顶髻；idx10=脑后马尾"),
    "L36_hideyoshi": dict(cn="丰臣秀吉", taikou="lord_tk5_517", asset="head_hideyoshi_a", gender="male",
                          face=[4], eye=[6], hair=[], helmet=[0, 7], weapons=[1, 3, 5],
                          note="idx0=甲+兜+日轮冠（复合件）；idx7=兜前日轮盘+双翼饰；眼球挂 bone_11 不是 bone_61/62（正常）"),

    # ---- 组 3 ----
    "L38_tadakatsu": dict(cn="本多忠胜", taikou="lord_tk5_652", asset="head_tadakatsu_a", gender="male",
                          # 🔴 2026-09-15 实机补件：**圆顶（钵）在 idx0 里**（sub0，391 顶点，
                          #    普查判「甲件」—— 它混着身体，整块进了甲，兜上就缺钵）。
                          #    定位方法 = 按件着色渲图（`Debug/offline/_hG_tada_color_front.png`：
                          #    红色那块就是它；青色=角/原兜件、紫色=脸）。
                          #    钵 = sub0 里 `n=32×2`（x±9.7 / z 198.8~214.1 / 绑 bone_11）共 64 顶点。
                          #    加进 helmet 列后：兜侧 `--keep-head-frags` 留下这 64 点；
                          #    甲侧 `--drop-head-idx`（已对全部件生效）同步从甲里剔掉 → 不重复。
                          face=[5], eye=[6], hair=[], helmet=[4, 0], weapons=[1],
                          strap_bone="bone_59",  # 下巴那一横条（并进脸壳主网格，按碎片摘不掉）
                          strap=[[3.7,-8.8,186.3],[-3.7,-8.8,186.3],[3.8,-7.5,186.3],[-3.8,-7.5,186.3],[3.4,-9.7,190.6],[-3.4,-9.7,190.6]],   # 🔴 头盔颏带（美术画在脸壳件里，原模型靠兜的吹返挡着）：种子点=碎片重心，由逐碎片渲染人工确认（2026-09-15）。头里摘掉（build_head --strap-seed），做兜时加回去（见 build_helmets 的 STRAP）
                          note="戴盔，头发被遮/并进脸壳"),
    "L39_inahime": dict(cn="稻姬", taikou="lord_tk5_1198", asset="head_inahime_a", gender="female",
                        face=[6], eye=[7], hair=[8, 4, 5], helmet=[], weapons=[1, 3],
                        note="🔴 表里名是「小松」（EnglishName=Sanada Komatsu，父=本多忠胜 lord_tk5_652，"
                             "夫=真田信之 lord_tk5_357）——2026-09-15 查明，**不需要新增行**；"
                             "她的 Alias 列已补「稻姬|稲姫|小松姬」。"
                             "🔴 发绳/额环的定位过程（两次才找对，别再猜）："
                             "idx10（submesh_8，54 顶点）**看错了** —— 那是一根从胸口垂到头顶的长锥"
                             "（源 z 114→170，比头本身还高 3 倍），加进去让整个头变成 0.70 米高、"
                             "和头断开飘着（用户实机看出来的）。"
                             "**粉发绳在 idx4（submesh_2，302 顶点）**、**金色额环在 idx5（submesh_3，129 顶点）** "
                             "—— 逐块单独渲染确认。两块都是复合件（发绳混着身体皮、额环混着甲片），"
                             "靠 1.2/1.3 的判据筛（头骨碎片豁免那两条正好管它们）"),
    "L40_ieyasu": dict(cn="德川家康", taikou="lord_tk5_506", asset="head_ieyasu_a", gender="male",
                       face=[10], eye=[11], hair=[], helmet=[9], weapons=[2, 4, 6, 8],
                       strap_bone="bone_59",  # 下巴那一横条（并进脸壳主网格，按碎片摘不掉）
                       strap=[[3.5,-10.5,141.0],[3.2,-9.2,138.6],[-3.5,-10.5,141.0],[-3.2,-9.2,138.6],[7.6,-6.0,146.3],[-7.6,-6.0,146.3]],   # 🔴 头盔颏带（美术画在脸壳件里，原模型靠兜的吹返挡着）：种子点=碎片重心，由逐碎片渲染人工确认（2026-09-15）。头里摘掉（build_head --strap-seed），做兜时加回去（见 build_helmets 的 STRAP）
                       note="戴盔"),
    "L41_mitsunari": dict(cn="石田三成", taikou="lord_tk5_75", asset="head_mitsunari_a", gender="male",
                          face=[7], eye=[8], hair=[9, 6], helmet=[], weapons=[1, 3, 5], note=""),   # 2026-09-15 用户裁定：这块是头发/头饰（不是盔）→ 挪进 hair 并进脸壳
    "L42_nagamasa": dict(cn="浅井长政", taikou="lord_tk5_16", asset="head_nagamasa_a", gender="male",
                         # 🔴 2026-09-15：idx11（sub8，18 顶点，head=100%）= **大金前立（弯月）**。
                         #    定位方法 = 按件着色渲图（`Debug/offline/_h4_naga_color_front.png`，青色那块）。
                         #    原来 helmet=[7] 漏了它 → 没任何管线加载 → 实机「长政头盔缺少上面部件」。
                         face=[8], eye=[9], hair=[], helmet=[7, 11], weapons=[2, 4], strap_bone="bone_59",  # 下巴那一横条（并进脸壳主网格，按碎片摘不掉）
                                                                                 strap=[[2.8,-6.4,154.7],[-2.8,-6.4,154.7]],   # 🔴 头盔颏带（美术画在脸壳件里，原模型靠兜的吹返挡着）：种子点=碎片重心，由逐碎片渲染人工确认（2026-09-15）。头里摘掉（build_head --strap-seed），做兜时加回去（见 build_helmets 的 STRAP）
                                                                                 note="戴盔"),
    "L43_sakon": dict(cn="岛左近", taikou="lord_tk5_386", asset="head_sakon_a", gender="male",
                      face=[8], eye=[9], hair=[11], helmet=[], weapons=[2, 4],
                      note="idx7 是腰间刀（材质非 mat_w_，别当武器件）；idx10 头顶小环=发髻环"),
    "L44_yoshihiro": dict(cn="岛津义弘", taikou="lord_tk5_395", asset="head_yoshihiro_a", gender="male",
                          face=[8], eye=[10], hair=[14], helmet=[], weapons=[3, 6],
                          note="idx8 的 bone_46 只占 43%（该件含双臂拉低了占比），仍是脸；idx4 头顶小毛簇可能也算头发"),

    # ---- 组 4 ----
    "L45_ginchiyo": dict(cn="訚千代", taikou="lord_tk5_1189", asset="head_ginchiyo_a", gender="female",
                         face=[8], eye=[9], hair=[11, 10], helmet=[], weapons=[2, 4],   # 2026-09-15 用户裁定：这块是头发/头饰（不是盔）→ 挪进 hair 并进脸壳
                         note="idx10 是金属冠饰（上翘双角=前立）但同块混了籠手+颈环，要按骨骼筛"),
    "L46_kanetsugu": dict(cn="直江兼续", taikou="lord_tk5_526", asset="head_kanetsugu_a", gender="male",
                          face=[7], eye=[8], hair=[], helmet=[6], weapons=[3],
                          strap_bone="bone_59",  # 下巴那一横条（并进脸壳主网格，按碎片摘不掉）
                          strap=[[5.7,-3.9,165.7],[-5.7,-3.9,165.7],[-7.8,0.5,169.7],[7.7,0.2,171.0],[-7.7,0.2,171.0]],   # 🔴 头盔颏带（美术画在脸壳件里，原模型靠兜的吹返挡着）：种子点=碎片重心，由逐碎片渲染人工确认（2026-09-15）。头里摘掉（build_head --strap-seed），做兜时加回去（见 build_helmets 的 STRAP）
                          note="短发烘在脸壳上，没有独立发件"),
    "L47_nene": dict(cn="宁宁", taikou="lord_tk5_1179", asset="head_nene_a", gender="female",
                     face=[4], eye=[6], hair=[10], helmet=[], weapons=[1, 3, 5, 7],
                     note="idx2 是金属环+两片大翼，主导骨是胸骨不是头骨 → 判为颈/肩饰"),
    "L48_kotaro": dict(cn="风魔小太郎", taikou="lord_tk5_613", asset="head_kotaro_a", gender="male",
                       face=[13], eye=[17], hair=[1, 4, 10], helmet=[], weapons=[2, 12, 14, 16, 18],
                       note="idx1 含头顶小发髻（与袴同网格）；idx4 是罩住头的深色蓬松团（兜帽或乱发）；"
                            "🔴 idx10（submesh_17，48 顶点）=【双耳金耳环钩】——2026-09-15 深夜用户报"
                            "「耳环挂坠消失」，按子网格着色的漏件图实锤（两个黄绿钩子坐在橙色甲上），已补进 hair"),
    "L49_musashi": dict(cn="宫本武藏", taikou="lord_tk5_700", asset="head_musashi_a", gender="male",
                        face=[4], eye=[5, 6], hair=[7], helmet=[], weapons=[1, 3],
                        note="🔴 唯一没有 38/60 眼球的：眼球是 idx5/idx6 两块（各 19顶点30面，bone_61/62 各 100%）；"
                             "idx7 是背心+头顶乱发同网格（复合件）"),
    "L100_kojiro": dict(cn="佐佐木小次郎", taikou="lord_tk5_343", asset="head_kojiro_a", gender="male",
                        face=[7], eye=[8], hair=[9, 11, 4], helmet=[], weapons=[1, 3, 5],
                        note="idx9 是黑发帽+一条长垂发（另含 2 个小金件）；"
                             "🔴 idx11（submesh_8，98 顶点）= 束发筒（发髻外面那个黑筒）；"
                             "🔴 **idx4（submesh_2，302 顶点）= 金色花纹发环本身** —— "
                             "2026-09-15 深夜用户报「发绳消失」，按「哪块件里有绑 bone_11 的小碎片」"
                             "全件扫了一遍才定位（它是一块 **z=193、眼在 182** 的独立小环，"
                             "混在 302 顶点的复合件里；同一件里其余是手臂/身体）。"
                             "⚠️ 别再靠『漏件图里看着像』来挑件 —— 那个方法在稻姬和佐佐木身上各错了一次"),
    "L101_katsuie": dict(cn="柴田胜家", taikou="lord_tk5_379", asset="head_katsuie_a", gender="male",
                         face=[7], eye=[8], hair=[10], helmet=[], weapons=[2],
                         note="idx10 顶部小件是发髻（与髋部毛裙同网格）；全模型无金属盔"),
}

# 本轮先不做（用户裁定或数据缺）：
#   L05_kenshin_ghost（谦信怨灵）—— 只是谦信的变身形态，不单独成角色
SKIP = ["L05_kenshin_ghost"]


def build_head_args(key):
    """把一行配方拼成 build_head.py 的命令行参数。

    返回 (parts, pick, seal)：
      parts  --parts 的件位
      pick   --pick-idx 的参数
      seal   --seal-bottom 的行号（底边带方形缺口的发块，见 build_head.py seal_open_bottom）

    🔴 头发的两种走法（`hard` 字段决定，2026-09-15）：
      · 默认：头发**并进 face 角色** → 过 1.3「离面部骨族远就删」的离群判据。
        适合「头发和衣服同块、衣服绑的是胸/腿骨」的角色（光秀）。
      · `hard=True`：头发**单独走 hair 角色** → 过 1.4b 的硬判据
        （碎片主导骨 ∉ {bone_10, bone_11, bone_46..62} → 丢），过滤完再并进脸壳。
        🔴 用在「1.3 会把整块头发判成杂质」的角色上 —— 慶次的 submesh_9 是 100% 绑头骨的头发，
        1.3 要删 153/153（靠 90% 安全网才没删），submesh_11 要删 419/457。

    🔴 `neck` / `neck_r` / `neck_z0`（2026-09-15 深夜加）—— **从身体件里给头补脖子**：
      战无2 把**脖子画在身体件里**，脸壳件只到下巴下面一点点（实测 28 人的脸壳件
      在眼睛以下的延伸：雑賀 12.2 最短 / 信長 19.2 / 島津 36.2 最长）。
      延伸不够的角色做出来就是个"悬空的头"（雑賀最明显）。
      用法：`neck=[身体件idx]` + `neck_r`/`neck_z0`（源坐标）→ build_head 只留
      「最宽 ≤ neck_r 且最低 ≥ neck_z0」的连通域（细而高 = 脖子；肩甲 |x| 20、手臂 |x| 93 都排除）。
      实测雑賀：脖子 = submesh_0 里两个 bone_9 碎片，33 顶点 / z 129~171 / |x| ≤ 9.7。
    """
    r = TABLE[key]
    hair = list(r.get("hair") or [])
    if hair and r.get("hard"):
        face = list(r["face"])
        pick = "face=%s,eye=%s,hair=%s" % ("+".join(str(i) for i in face),
                                           "+".join(str(i) for i in r["eye"]),
                                           "+".join(str(i) for i in hair))
    else:
        face = list(r["face"]) + hair                            # 头发并进脸壳
        pick = "face=%s,eye=%s" % ("+".join(str(i) for i in face), "+".join(str(i) for i in r["eye"]))
    neck = list(r.get("neck") or [])
    if neck:
        pick += ",neck=%s" % "+".join(str(i) for i in neck)
    # 🔴 一律 3 件（脸/眼/嘴）—— 战无2 的源模型**没有睫毛件**（男女人物都没有），
    #    而「4 件」那套是蒂法/原版女头的东西。
    #
    # 🔴🔴 **件位顺序【分性别】**（2026-09-16 实机修正，推翻 09-15 那条"与性别无关"）：
    #      女头 = **脸 → 嘴 → 眼**（(+睫)）；男头 = **脸 → 眼 → 嘴**
    #    引擎给脸部件分配贴图**按子网格的位置算**（`Knowledge/蒂法换头工程.md` §13.7 ①/§16）。
    #    三条实证（都是"已实机验收"的东西，不是推的）：
    #      · 原版女头 `head_female_a`：`.2`=嘴(242v)  `.6`=眼(98v)  `.7`=睫(72v)
    #      · 原版男头 `head_male_a`  ：`.1`=eye_mat(109v)  `.2`=mouth_mat(212v)
    #      · 本工程两个验收通过的自定义头（`tpaccli metaparts` 实测）：
    #          蒂法     `[0]脸 [1]嘴 [2]眼 [3]睫`
    #          萨菲罗斯 `[0]脸 [1]眼(458v) [2]嘴(3698v)`   ← 男头第 2 件是眼
    #      · 织丰在售的 `sho_head_male_japanese` 同样是 3 件（脸/眼/嘴）
    #    🔴 **踩坑史（08-16 用户实机，气到）**：09-15 深夜照女头（+蒂法）把 28 人**统一**改成
    #      `face,mouth,eye` —— 6 个女角色对了，**22 个男角色装反**；症状（眼睛不对 / 嘴开花）
    #      与另一条坑（`MaterialFlags` 没清）**长得一模一样**，两条一起爆发时极难归因。
    #      历史物证：2026-09-14 手工版信长的构建日志（用户验收通过的那版）就是
    #      `face→.0 / eye→.1 / mouth→.2`。
    #    ⚠️ 改这一行 = 22 颗男头要重导，别只改一半。
    parts = "face,eye,mouth" if r.get("gender") != "female" else "face,mouth,eye"
    seal = list(r.get("seal") or [])
    return parts, pick, seal


def strap_bones(key):
    """颏带里"按骨骼"才能切掉的那部分（下巴横条），见 build_head.drop_verts_by_bone。"""
    b = TABLE[key].get("strap_bone")
    return [b] if b else []


def strap_seeds(key):
    """该角色的【头盔颏带】种子点（源坐标），见 build_head.py drop_frag_by_seed。
    每颗种子选中"重心离它最近的碎片"；种子由人眼在逐碎片渲染图上定（2026-09-15）。"""
    return list(TABLE[key].get("strap") or [])


def weapon_ids(key):
    """该角色的武器件序号（阶段 3.5 用）。"""
    return list(TABLE[key].get("weapons") or [])


if __name__ == "__main__":
    import sys
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
    for k, v in TABLE.items():
        parts, pick, seal = build_head_args(k)
        print("%-16s %-10s %-8s parts=%-18s cut=%s  pick-idx=%s%s%s"
              % (k, v["cn"], v["asset"], parts, bool(v["eye"]) and bool(v["face"]), pick,
                 ("  兜=" + str(v["helmet"])) if v["helmet"] else "",
                 ("  封底=" + str(seal)) if seal else ""))
