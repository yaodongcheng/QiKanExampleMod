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
WORK = os.path.join(REPO, "Debug", "offline", "seph_build")
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
        weights_from=os.path.join(REPO, "Debug", "offline", "core_game", "fbx", "head", "head",
                                  "head_male_a.fbx"),  # 🔴 男性头，别用女性
        neck_z="1.600",
        neck_band="0.05",
        backup=r"D:\BrainMaker\blend_projects\sephiroth_export\backup_20260914",
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
    ap.add_argument("--work", default=WORK)
    ap.add_argument("--backup", default=None, help="覆盖配方里的备份目录")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--list", action="store_true")
    a = ap.parse_args()

    if a.list:
        for k, v in RECIPES.items():
            print("%-12s name=%s  gender=%s  parts=%s" % (k, v["name"], v["gender"], v["parts"]))
        return 0
    if a.recipe not in RECIPES:
        print("未知配方 %r；可用：%s" % (a.recipe, list(RECIPES)))
        return 1
    r = RECIPES[a.recipe]
    name = r["name"]
    ver = a.ver if a.ver else next_version(a.work, name)
    backup = a.backup or r["backup"]

    os.makedirs(a.work, exist_ok=True)
    v_stage1 = os.path.join(a.work, "%s_v%d.fbx" % (name, ver))          # 几何（无通道）
    v_final = os.path.join(a.work, "%s_v%d.fbx" % (name, ver + 1))       # 成品（含通道）

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
