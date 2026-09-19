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

# 搬通道用的源（蒂法 v10：59 条脸形位移场的**权威来源**，两性通用 —— 通道本身是"拉杆驱动"，
# 与脸型无关，新头模一律从这个文件搬，别从别的版本搬）
CHAN_SRC = r"D:\BrainMaker\blend_projects\tifa_export\backup_20260913\head_tifa_a_v10.fbx"
CHAN_SRC_OBJ = "head_tifa_a.0"

# ---------------- 每角色参数配方 ----------------
# 字段说明见文件头与 Knowledge/蒂法换头工程.md §21
RECIPES = {
    "sephiroth": dict(
        name="head_sephiroth_a",
        src=r"F:\下载\萨菲罗斯\02.blend",
        gender="male",
        parts="face,eye,mouth",                       # 男头 3 件（原版 head_male_a 同构）
        pick="face=body.cut,eye=Eyeballs,mouth=mouth",  # 头藏在 body.cut 里
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
        # ⚠️ 没开 `--neck-fill`：源模型自带"脖子 + 肩/胸口"（比战无2 的只到下巴强），
        #    被 `--fit-rim` 收进领口后**颈部剪影是平滑的**；铺下摆反而铺出一圈**硬棱面 + 拉伸的 UV**，
        #    还会在颈后正中留一道竖缝。实测对比图见 henry_build\render_neckfill\（变体产物在
        #    `variant_neckfill\`）。唯一残留：正前 V 领口最低处（z=1.4144）比头的下沿（1.4300）低 1.6cm
        #    —— 是否看得见待实机确认，**这条要用户拍板**。
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
                                 "材质", "[OK]", "EXPORTED", "顶点", "FATAL", "Error")):
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
    if r.get("neck_fill"):
        cmd1.append("--neck-fill")
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
