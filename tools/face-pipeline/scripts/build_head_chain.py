# build_head_chain.py —— 「源模型 → 可用头部 FBX」的【一条命令】（含每角色的参数配方）
#
# 为什么需要它：整条链要跑两步 Blender（build_head.py → transfer_channels.py）+ 一道门禁，
#   而每步的参数（--pick / --gender / --cut-z / --fit-rim / --weld-seam / --weights-from …）
#   又长又容易记错。**参数不记下来，产物就没法重建** —— 光有备份只能回滚、不能重造。
#   本脚本把「参数配方」固化进代码，一条命令重跑整条链。
#
# 用法（系统 python，不需要 Blender）：
#   python build_head_chain.py --recipe sephiroth            # 生成新版本
#   python build_head_chain.py --recipe sephiroth --dry-run  # 只打印将要跑的命令
#   python build_head_chain.py --recipe sephiroth --ver 5    # 指定版本号（默认自动 +1）
#   python build_head_chain.py --list                        # 列出所有配方
#
# 产出：<work>/<name>_v<ver>.fbx（并自动**同时**复制进 --backup 目录；同名已存在则拒绝覆盖）
#   🔴 规矩：产物一律【升版本号】，绝不覆盖同名文件（早期被覆盖过 6 次，见 §21 备份审计）。
#
import argparse
import os
import re
import shutil
import subprocess
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))   # LivingWorldNpcs 模块根
SCRIPTS = os.path.join(REPO, "tools", "face-pipeline", "scripts")
WORK = os.path.join(REPO, "Debug", "offline", "自定义头", "seph_build")
BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"

# 搬【脸形段】通道（1..59）用的源：**xxFemale 原版头**（拉杆位移场的真正源头，两性通用 ——
# 通道是"拉杆驱动"，与脸型无关，新头模一律从这个文件搬）。表情段（60..100）另有其源，见下方 ANIM_SRC_MALE。
#
# 🔴🔴 2026-09-19 换源：旧源 `head_tifa_a_v10.fbx`（D:\BrainMaker\...\backup_20260913\）
#     的 59 条位移场**被前后（Y 轴）镜像过** —— 拉"鼻子"动的其实是后脑勺。
#     实测：装机态鼻帧质心 y=−0.024，而源头/原版都在 y=+0.14（鼻尖方向）；
#     镜像假设残差 0.025 m vs 不镜像 0.111 m。v10 之后的所有头（蒂法/萨菲罗斯/亨利）全部继承，
#     蒂法文档 §21.9「额头/颅顶被拉变形、鼻子上凸出一块」就是它。**旧源已退役，别再指回去。**
#
# 源的重新生成（离线产物，不进 git；tpac 里的是权威数据）：
#   tpaccli dump --packdir "<游戏>\Modules\xxFemaleHead\AssetPackages" \
#                --filter head_xxfemale --format fbx --out "Debug\offline\自定义头\_chansrc"
# 🔴 该 dump 的 morph 缺 FullWeights，Blender 5.2 导入器会断言崩 —— 已由 transfer_channels.py
#    内置的 `patch_fbx_importer()` 内存级兜住。
# 验收判据（换源后必须过）：`_probe_chan_src.py` 看 KeyTime_31 的位移质心 y ≈ +0.14（鼻尖方向）。
CHAN_SRC = os.path.join(REPO, "Debug", "offline", "自定义头", "_chansrc", "head", "head_xxfemale_a.fbx")
CHAN_SRC_OBJ = "head_xxfemale_a.002.0"

# 表情/口型段（通道 60..100）的源：**原版同性别头**。
# 🔴 为什么表情段不像脸形段那样也用 xxFemale：
#   · 脸形段（1..59）是"拉杆场"，两性几乎一样（原版男/xxFemale 的地标值重合到 0.002m），
#     且整套 deform_keys 幅度是按它调的 → 沿用 xxFemale（别动，动了要重验拉杆）。
#   · 表情段（60..100）是**解剖动作**（张嘴/闭眼/抬眉），量级按性别不同（JawDrop 原版男 27mm / xxFemale 16mm）；
#     我们的男头是按**男表**标定的（眼球 (0,0.128,1.6839)、眼↔嘴 0.0795），所以男头用男头源。
#   · 且表情段必须**按件对位**：原版每个子网格各带自己的场（眼球转动在眼球件、牙齿跟下颌在嘴件），
#     把脸壳的场无脑套到眼球件上 = 眼球只能被眼睑蹭 1mm、转不起来。
#   男头件序 = 脸/眼/嘴（女头是 脸/嘴/眼/睫，别照抄）。
ANIM_SRC_MALE = os.path.join(REPO, "Debug", "offline", "自定义头", "core_game", "fbx", "head", "head",
                             "head_male_a.fbx")
ANIM_OBJS_MALE = "head_male_a,head_male_a.1,head_male_a.2"

# ---------------- 每角色参数配方 ----------------
# 字段说明见文件头与 Knowledge/蒂法换头工程.md §21
RECIPES = {
    "sephiroth": dict(
        name="head_sephiroth_a",
        src=r"F:\下载\萨菲罗斯\02.blend",
        gender="male",
        parts="face,eye,mouth",                       # 男头 3 件（原版 head_male_a 同构）
        pick="face=body.cut,eye=Eyeballs,mouth=mouth",  # 头藏在 body.cut 里
        anim_src=ANIM_SRC_MALE,                       # 表情/口型段（60..100）→ 原版男头，按件对位
        anim_objects=ANIM_OBJS_MALE,
        cut_z="1.4144",                               # 裁到原版男头的最低点
        fit_rim=True,                                 # 收进男身体的 V 领口（源模型肩膀宽 6cm）
        weld_seam=True,                               # 合上源模型"前后两块壳"的缝（0.66~10mm）
        weights_from=os.path.join(REPO, "Debug", "offline", "自定义头", "core_game", "fbx", "head", "head",
                                  "head_male_a.fbx"),  # 🔴 男性头，别用女性
        neck_z="1.600",
        neck_band="0.05",
        work=WORK,                                    # 不写 = 用默认的 seph_build
        backup=r"D:\BrainMaker\blend_projects\sephiroth_export\backup_20260914",
    ),

    # ---------------- KCD 亨利（2026-09-19） ----------------
    # 源 = `Debug\offline\_kcd_recon\NPC_Henry.fbx`（《天国拯救》亨利，米制、脸朝 −Y、头网格 8323 顶点）
    # 与萨菲罗斯那条链的**四处差异**（照抄会翻车）：
    #   ① `--pick` 用**精确匹配**（关键字前缀 `=`）—— `m_head_henry` 是 `m_head_henry_Teeth`/
    #      `_Eyelashes`/`_Eyeshadows`/`_Tearline` 的子串，子串匹配会把后三件并进脸壳；
    #      而男头只有 3 个子网格位（脸/眼/嘴），多出来的件会被引擎回落到脸皮材质 → 眼睛嘴全糊。
    #   ② `--no-prune` —— `prune_far` 的免死白名单是战无2 的 `bone_10/bone_11`，KCD 骨名是
    #      `Head`/`Neck`，白名单永不命中 → 远端几何会被当"离群碎片"清掉。
    #   ③ `--no-head-only` —— 1.3/1.4b 的判据同样建立在战无2 骨名上；本条是保险，
    #      实际对 KCD 是空转（1.3 找不到 bone_46..62 会自己跳过；1.4b 只冲 hair 件，本配方不挑 hair）。
    #   ④ **不用 `--weld-seam`** —— 那是给"前后两块不相连的壳"用的（萨菲罗斯），
    #      亨利的脸壳是**一整块**（8323 顶点 = 8 个连通域，最大那块 6328 自己就是连通的）；
    #      强行合缝只会去配"最近的一对自由边环"（= 两个眼窝 / 领口），有把眼窝焊死的风险。
    #   ⑤ 也不用 `--t-s`（T 模式）—— 它的锚骨 `bone_11` 对 KCD 取不到。
    "henry": dict(
        name="head_henry_a",
        src=os.path.join(REPO, "Debug", "offline", "_kcd_recon", "NPC_Henry.fbx"),
        gender="male",
        parts="face,eye,mouth",
        anim_src=ANIM_SRC_MALE,                       # 表情/口型段（60..100）→ 原版男头，按件对位
        anim_objects=ANIM_OBJS_MALE,
        pick=("face==m_head_henry,"
              "eye==_EyeLBall_Shader_Mesh+_EyeRBall_Shader_Mesh"
              "+_EyeLIris_Shader_Mesh+_EyeRIris_Shader_Mesh,"
              "mouth==m_head_henry_Teeth"),
        cut_z="1.4144",                               # 原版男头最低点（亨利最低的"肩"在 1.429，切不到）
        fit_rim=True,                                 # 🔴 必需：源模型的"肩/胸口"那块宽 ±0.146m，
                                                      #    远超男身体 V 领口（±0.085~0.123）→ 不收会从肩膀穿出来
        no_prune=True,
        no_head_only=True,
        apply_src_xform=True,                         # 🔴 必需：FBX 源的顶点是**厘米 + Y-up**，
                                                      #    对象矩阵里带着 0.01 缩放 + 90° 转轴，不烘进网格就全错
        # 🔴 2026-09-20 第二轮：**删胸兜板 + 从脖子底环铺**（蒂法那条路）。
        #    实机症状（用户截图）：头的"胸兜"像围兜**平贴在胸口上**，边缘一刀切 → 硬"袖口边"；
        #    两侧比身体领口低 4.5~8cm（趴在身体外面），正前又短 1.56cm。
        #    第一轮只加 Hermite 多环放样（不算差但不对）：fill_neck_to_rim 铺的是**整块板的自由边**，
        #    于是从板的外沿继续往外散 → 一圈外翻的硬板（"衬衫领"，render_v10）。
        #    ⇒ 本轮先 `--drop-lower-plate` 删掉那块 488 顶点的独立岛（判据 z<1.56 且 r>0.09），
        #      脖子（469 顶点，半径只有颈粗 ≈0.06）不命中 → 铺下摆就从**脖子真正的底环**起。
        drop_lower_plate=True,
        drop_plate_z="1.560",
        drop_plate_r="0.090",
        neck_fill=True,
        neck_fill_top="1.545",                        # 起铺高度（脖子自由边所在）
        neck_loft_rings="6",                          # Hermite 中间环数（1=老直线桥接）
        weights_from=os.path.join(REPO, "Debug", "offline", "自定义头", "core_game", "fbx", "head", "head",
                                  "head_male_a.fbx"),
        neck_z="1.600",
        neck_band="0.05",
        work=os.path.join(REPO, "Debug", "offline", "自定义头", "henry_build"),
        backup=os.path.join(REPO, "Debug", "offline", "自定义头", "henry_build", "backup"),
    ),
}


def run(cmd, tag):
    print("\n$ %s" % " ".join('"%s"' % c if " " in str(c) else str(c) for c in cmd))
    p = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    out = (p.stdout or "") + (p.stderr or "")
    for ln in out.splitlines():
        if any(k in ln for k in ("头壳中心", "标定", "裁 ", "收领口", "合缝", "权重来源",
                                 "权重组映射", "脖子/领口权重", "落位", "全头包围盒",
                                 "材质", "[OK]", "EXPORTED", "顶点", "FATAL", "Error",
                                 "脸形源", "表情源", "对位")):
            print("   " + ln.strip())
    if p.returncode != 0:
        print("   [!] %s 退出码 %d" % (tag, p.returncode))
    return p.returncode == 0


def next_version(work, name):
    n = 0
    for f in os.listdir(work) if os.path.isdir(work) else []:
        m = re.match(re.escape(name) + r"_v(\d+)\.fbx$", f)
        if m:
            n = max(n, int(m.group(1)))
    return n + 1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--recipe", default="sephiroth")
    ap.add_argument("--ver", type=int, default=None, help="版本号（默认自动 +1）")
    ap.add_argument("--work", default=None, help="覆盖配方里的产物目录（默认取配方的 work）")
    ap.add_argument("--backup", default=None, help="覆盖配方里的备份目录")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--list", action="store_true")
    a = ap.parse_args()

    if a.list:
        for k, v in RECIPES.items():
            print("%-12s name=%-18s gender=%s  parts=%s\n             工作目录 %s"
                  % (k, v["name"], v["gender"], v["parts"], v.get("work") or WORK))
        return 0
    if a.recipe not in RECIPES:
        print("未知配方 %r；可用：%s" % (a.recipe, list(RECIPES)))
        return 1
    r = RECIPES[a.recipe]
    name = r["name"]
    # 🔴 每个配方有**自己的产物目录**（配方里没写才回落到默认的 seph_build）——
    #    别把新角色的产物混进萨菲罗斯的目录，否则「哪一版是谁的」立刻变成糊涂账。
    work = a.work or r.get("work") or WORK
    ver = a.ver if a.ver else next_version(work, name)
    backup = a.backup or r["backup"]

    os.makedirs(work, exist_ok=True)
    v_stage1 = os.path.join(work, "%s_v%d.fbx" % (name, ver))          # 几何（无通道）
    v_final = os.path.join(work, "%s_v%d.fbx" % (name, ver + 1))       # 成品（含通道）

    # 不许覆盖：产物必须是新版本号
    for p in (v_stage1, v_final):
        if os.path.exists(p):
            print("FAIL: %s 已存在 —— 本脚本不覆盖，请 --ver 指定新的版本号" % p)
            return 1

    cmd1 = [BLENDER, "--background", "--python", os.path.join(SCRIPTS, "build_head.py"), "--",
            "--src", r["src"], "--out", v_stage1, "--name", name,
            "--gender", r["gender"], "--parts", r["parts"], "--pick", r["pick"],
            "--cut-z", r["cut_z"]]
    if r.get("fit_rim"):
        cmd1.append("--fit-rim")
    if r.get("weld_seam"):
        cmd1.append("--weld-seam")
    # 🔴 这两条是给「骨名不是战无2 那套」的源模型用的（KCD）：判据里的白名单是 bone_10/11，
    #    对 KCD 的 `Head`/`Neck` 永不命中 → 不关掉会把远端几何当离群碎片清掉。
    if r.get("no_prune"):
        cmd1.append("--no-prune")
    if r.get("no_head_only"):
        cmd1.append("--no-head-only")
    if r.get("drop_lower_plate"):
        cmd1.append("--drop-lower-plate")
        for k, cli in (("drop_plate_z", "--drop-plate-z"), ("drop_plate_r", "--drop-plate-r")):
            if r.get(k):
                cmd1 += [cli, str(r[k])]
    if r.get("neck_fill"):
        cmd1.append("--neck-fill")
        # 🔴 2026-09-20：补下摆的两个旋钮 —— 起铺高度 + 放样中间环数。
        #    环数 ≥2 走三次 Hermite（两端相切），消掉「一圈硬棱面」。
        if r.get("neck_fill_top"):
            cmd1 += ["--neck-fill-top", str(r["neck_fill_top"])]
        if r.get("neck_loft_rings"):
            cmd1 += ["--neck-loft-rings", str(r["neck_loft_rings"])]
    # 🔴 把对象世界矩阵烘进网格数据 —— FBX 源必需（导入器把「单位换算 + Y-up→Z-up」放在对象矩阵里，
    #    KCD 源的顶点是厘米 + Y-up）。默认不开，见 build_head.py 那段注释。
    if r.get("apply_src_xform"):
        cmd1.append("--apply-src-xform")
    if r.get("weights_from"):
        cmd1 += ["--weights-from", r["weights_from"],
                 "--neck-z", r["neck_z"], "--neck-band", r["neck_band"]]

    cmd2 = [BLENDER, "--background", "--python", os.path.join(SCRIPTS, "transfer_channels.py"), "--",
            "--src", CHAN_SRC, "--src-object", CHAN_SRC_OBJ,
            "--dst", v_stage1, "--out", v_final]
    # 表情/口型段（60..100）的源：不指定 = 用脸形源那一件套到所有件（旧行为，= 表情是空的）
    if r.get("anim_src"):
        cmd2 += ["--anim-src", r["anim_src"], "--anim-objects", r["anim_objects"]]
        # 🔴 搬法：法线投影 + 18mm 门限 + 2 轮轻平滑（2026-09-19 实测定型）——
        #    · 只用最近邻 3 点加权：**唇线那道折线被抹平** → 嘴张不开（唇线位移只有源头的一半；
        #      实测 唇/下巴比 0.34，原版 0.77）
        #    · 投影：把源三角形内的线性场原样复制 → 唇线保住（唇/下巴比回到 0.52，唇线位移 −2.13mm vs 原版 −3.02）
        #    · 18mm：两颗头的表面在嘴部相距 7~13mm（亨利下半脸比原版男头前突 1cm+），
        #      6mm 门限会让嘴部几乎全回退 = 白改
        #    · 2 轮平滑：投影会在上下唇分界处让相邻顶点落到源头不同三角形 → 唇缘撕尖刺，平滑抹掉
        #    ⚠️ 换别的头要先量 `_probe_registration.py` 的"最近距中位"，按它定 --proj-max
        cmd2 += ["--anim-map", "proj", "--proj-max", "18", "--anim-smooth", "1"]
        # 🔴 下颌**直接算**（绕颌关节旋转），不再靠搬运 —— 搬运来的下颌要过"源粗→抹平→撕裂→平滑→又钝掉"
        #    一串，而"张嘴"本质就是下颌绕关节转一下。增益 2.0 = 小幅度时也看得出嘴在动（实机 clip 权重小）。
        cmd2 += ["--anim-jaw", "--anim-jaw-gain", "2.2"]  # 唇缝切开 + 分7段沿唇线切

    cmd3 = [sys.executable, os.path.join(SCRIPTS, "fbx_probe.py"), v_final]

    if a.dry_run:
        for c in (cmd1, cmd2, cmd3):
            print("$ %s" % " ".join('"%s"' % x if " " in str(x) else str(x) for x in c))
        print("\n--dry-run：到此为止")
        return 0

    if not run(cmd1, "build_head"):
        return 1
    if not run(cmd2, "transfer_channels"):
        return 1
    print("\n---- 关卡 1（FBX 规格门禁）----")
    p = subprocess.run(cmd3, capture_output=True, text=True, encoding="utf-8", errors="replace")
    for ln in (p.stdout or "").splitlines():
        if any(k in ln for k in ("UpAxis ", "FrontAxis", "CoordAxis", "UnitScaleFactor", "Geometry 节点数", "另有")):
            print("   " + ln.strip())
    if p.returncode != 0:
        print("FAIL: 关卡 1 未通过")
        return 1

    # 立即备份（规矩：产出即备份，别等）
    os.makedirs(backup, exist_ok=True)
    for p in (v_stage1, v_final):
        dst = os.path.join(backup, os.path.basename(p))
        if os.path.exists(dst):
            print("FAIL: 备份已存在同名 %s —— 拒绝覆盖" % dst)
            return 1
        shutil.copy2(p, dst)
        print("  备份 -> %s" % dst)

    print("\n完成。下一步：ModKit 导入 %s → Publish → python install_pack.py --filter %s" % (v_final, name))
    print("  🔴 导入前把 AssetSources 里的旧版本移走，只留这一份，免得选错")
    return 0


if __name__ == "__main__":
    sys.exit(main())
