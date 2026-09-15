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
                         face=[11], eye=[12], hair=[], helmet=[], weapons=[4, 7],   # 2026-09-15 用户裁定：头顶那块是头发，不是盔（曾误解为兜）
                         note="idx3 是六文銭头巾的两条垂带（bone_11）；🔴 idx5 看图是深色盔内衬 + "
                              "两片飞到两侧的碎片（碎片把包围盒撑到 ±51cm → 并进脸壳会让『收领口』狂收 543mm），"
                              "判为兜/内衬，**不并**。脸壳 idx11 自带发带，本身就是完整的头"),
    "L01_keiji": dict(cn="前田庆次", taikou="lord_tk5_657", asset="head_keiji_a", gender="male",
                      face=[14], eye=[15], hair=[17], helmet=[], weapons=[4, 9],   # 2026-09-15 用户裁定：头顶那块是头发，不是盔（曾误解为兜）
                      note="idx6 只见金色花形前立+胸前绳结，按「有前立」算兜"),
    "L02_nobunaga": dict(cn="织田信长", taikou="lord_tk5_195", asset="head_nobunaga_a", gender="male",
                         face=[11], eye=[12], hair=[5], helmet=[], weapons=[3],
                         note="🔴 已完成（race lwn_nobunaga）。头顶黑发已在脸壳里，idx5 是后发/刺发团"),
    "L03_mitsuhide": dict(cn="明智光秀", taikou="lord_tk5_14", asset="head_mitsuhide_a", gender="male",
                          face=[10], eye=[11], hair=[5], helmet=[], weapons=[3],   # 2026-09-15 用户裁定：头顶那块是头发，不是盔（曾误解为兜）
                          note="idx6 是「整套衣服」一块（盔/头巾夹在里面），要按骨骼筛"),
    "L05_kenshin": dict(cn="上杉谦信", taikou="lord_tk5_119", asset="head_kenshin_a", gender="male",
                        face=[11], eye=[12], hair=[], helmet=[], weapons=[4, 7],
                        note="idx15 是两条长布垂带（无发丝纹理）——按披发读也可算头发，先不并"),
    "L06_oichi": dict(cn="阿市", taikou="lord_tk5_1181", asset="head_oichi_a", gender="female",
                      face=[4], eye=[5], hair=[6, 7], helmet=[], weapons=[1, 3],
                      note="idx6 = 发髻 + 带金流苏的浅蓝肩衣（复合件，要按骨骼筛）"),
    "L07_okuni": dict(cn="出云阿国", taikou="lord_tk5_1208", asset="head_okuni_a", gender="female",
                      face=[4], eye=[6], hair=[9], helmet=[], weapons=[1, 3, 5, 7],   # 2026-09-15 用户裁定：头顶那块是头发，不是盔（曾误解为兜）
                      note="idx8 是金色前立+两侧笄的金饰组（不是金属盔）"),

    # ---- 组 2 ----
    "L09_magoichi": dict(cn="杂贺孙一", taikou="lord_tk5_321", asset="head_magoichi_a", gender="male",
                         face=[5], eye=[6], hair=[8, 9], helmet=[], weapons=[1, 3],
                         note="idx8 = 头发 + 长外套（复合件，要按骨骼筛）；idx9 是脑后发尾"),
    "L10_shingen": dict(cn="武田信玄", taikou="lord_tk5_449", asset="head_shingen_a", gender="male",
                        face=[7], eye=[8], hair=[10, 9], helmet=[6], weapons=[2, 4],
                        note="idx6=金角+额甲+盔侧圆环（金属兜）；idx9 是白色长毛帘；idx10=红发+腰间红披（复合件）"),
    "L11_masamune": dict(cn="伊达政宗", taikou="lord_tk5_466", asset="head_masamune_a", gender="male",
                         face=[9], eye=[10], hair=[], helmet=[12, 7], weapons=[3, 5],
                         note="头发在脸壳里；idx7=身甲+大金月牙前立（复合件）；idx11=口部小月牙片"),
    "L12_nouhime": dict(cn="归蝶", taikou="lord_tk5_1194", asset="head_nouhime_a", gender="female",
                        face=[8], eye=[9], hair=[6], helmet=[], weapons=[3, 5],
                        note="idx6=身体+发髻（复合件）；idx11=蝴蝶+珠串+耳饰（头饰，不是头发）"),
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
                          face=[5], eye=[6], hair=[], helmet=[4], weapons=[1],
                          note="戴盔，头发被遮/并进脸壳"),
    "L39_inahime": dict(cn="稻姬", taikou="lord_tk5_1198", asset="head_inahime_a", gender="female",
                        face=[6], eye=[7], hair=[8], helmet=[], weapons=[1, 3],
                        note="🔴 表里名是「小松」（EnglishName=Sanada Komatsu，父=本多忠胜 lord_tk5_652，"
                             "夫=真田信之 lord_tk5_357）——2026-09-15 查明，**不需要新增行**；"
                             "她的 Alias 列已补「稻姬|稲姫|小松姬」。idx10 是头后深色锥形条"),
    "L40_ieyasu": dict(cn="德川家康", taikou="lord_tk5_506", asset="head_ieyasu_a", gender="male",
                       face=[10], eye=[11], hair=[], helmet=[9], weapons=[2, 4, 6, 8],
                       note="戴盔"),
    "L41_mitsunari": dict(cn="石田三成", taikou="lord_tk5_75", asset="head_mitsunari_a", gender="male",
                          face=[7], eye=[8], hair=[9], helmet=[], weapons=[1, 3, 5], note=""),   # 2026-09-15 用户裁定：头顶那块是头发，不是盔（曾误解为兜）
    "L42_nagamasa": dict(cn="浅井长政", taikou="lord_tk5_16", asset="head_nagamasa_a", gender="male",
                         face=[8], eye=[9], hair=[], helmet=[7], weapons=[2, 4], note="戴盔"),
    "L43_sakon": dict(cn="岛左近", taikou="lord_tk5_386", asset="head_sakon_a", gender="male",
                      face=[8], eye=[9], hair=[11], helmet=[], weapons=[2, 4],
                      note="idx7 是腰间刀（材质非 mat_w_，别当武器件）；idx10 头顶小环=发髻环"),
    "L44_yoshihiro": dict(cn="岛津义弘", taikou="lord_tk5_395", asset="head_yoshihiro_a", gender="male",
                          face=[8], eye=[10], hair=[14], helmet=[], weapons=[3, 6],
                          note="idx8 的 bone_46 只占 43%（该件含双臂拉低了占比），仍是脸；idx4 头顶小毛簇可能也算头发"),

    # ---- 组 4 ----
    "L45_ginchiyo": dict(cn="訚千代", taikou="lord_tk5_1189", asset="head_ginchiyo_a", gender="female",
                         face=[8], eye=[9], hair=[11], helmet=[], weapons=[2, 4],   # 2026-09-15 用户裁定：头顶那块是头发，不是盔（曾误解为兜）
                         note="idx10 是金属冠饰（上翘双角=前立）但同块混了籠手+颈环，要按骨骼筛"),
    "L46_kanetsugu": dict(cn="直江兼续", taikou="lord_tk5_526", asset="head_kanetsugu_a", gender="male",
                          face=[7], eye=[8], hair=[], helmet=[6], weapons=[3],
                          note="短发烘在脸壳上，没有独立发件"),
    "L47_nene": dict(cn="宁宁", taikou="lord_tk5_1179", asset="head_nene_a", gender="female",
                     face=[4], eye=[6], hair=[10], helmet=[], weapons=[1, 3, 5, 7],
                     note="idx2 是金属环+两片大翼，主导骨是胸骨不是头骨 → 判为颈/肩饰"),
    "L48_kotaro": dict(cn="风魔小太郎", taikou="lord_tk5_613", asset="head_kotaro_a", gender="male",
                       face=[13], eye=[17], hair=[1, 4], helmet=[], weapons=[2, 12, 14, 16, 18],
                       note="idx1 含头顶小发髻（与袴同网格）；idx4 是罩住头的深色蓬松团（兜帽或乱发）"),
    "L49_musashi": dict(cn="宫本武藏", taikou="lord_tk5_700", asset="head_musashi_a", gender="male",
                        face=[4], eye=[5, 6], hair=[7], helmet=[], weapons=[1, 3],
                        note="🔴 唯一没有 38/60 眼球的：眼球是 idx5/idx6 两块（各 19顶点30面，bone_61/62 各 100%）；"
                             "idx7 是背心+头顶乱发同网格（复合件）"),
    "L100_kojiro": dict(cn="佐佐木小次郎", taikou="lord_tk5_343", asset="head_kojiro_a", gender="male",
                        face=[7], eye=[8], hair=[9], helmet=[], weapons=[1, 3, 5],
                        note="idx9 是黑发帽+一条长垂发（另含 2 个小金件）；idx11 是背后黑色长筒（非 mat_w_）"),
    "L101_katsuie": dict(cn="柴田胜家", taikou="lord_tk5_379", asset="head_katsuie_a", gender="male",
                         face=[7], eye=[8], hair=[10], helmet=[], weapons=[2],
                         note="idx10 顶部小件是发髻（与髋部毛裙同网格）；全模型无金属盔"),
}

# 本轮先不做（用户裁定或数据缺）：
#   L05_kenshin_ghost（谦信怨灵）—— 只是谦信的变身形态，不单独成角色
SKIP = ["L05_kenshin_ghost"]


def build_head_args(key):
    """把一行配方拼成 build_head.py 的命令行参数。"""
    r = TABLE[key]
    face = list(r["face"]) + list(r.get("hair") or [])       # 头发并进脸壳
    pick = "face=%s,eye=%s" % ("+".join(str(i) for i in face), "+".join(str(i) for i in r["eye"]))
    # 🔴 一律 3 件（脸/眼/嘴）—— 战无2 的源模型**没有睫毛件**（男女人物都没有），
    #    而「4 件」那套是蒂法/原版女头的东西。3 件正好对上原版女头布局的前三格
    #    （原版女头 = 脸/嘴/眼/睫，前三个就是脸/嘴/眼），第 4 格空着 = 不渲染睫毛。
    #    ⚠️ 待实机验证：女性角色的脸如果不正常，第一件事是回来查这里。
    parts = "face,eye,mouth"
    return parts, pick


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
        parts, pick = build_head_args(k)
        print("%-16s %-10s %-8s parts=%-18s cut=%s  pick-idx=%s%s"
              % (k, v["cn"], v["asset"], parts, bool(v["eye"]) and bool(v["face"]), pick,
                 ("  兜=" + str(v["helmet"])) if v["helmet"] else ""))
