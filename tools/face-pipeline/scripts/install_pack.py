# install_pack.py —— 编辑器 Publish 之后的【一键后处理 + 装机】
#
# 为什么需要它：编辑器 Publish 出来的是"白编译"包（材质被刷回默认、morph 帧不全），
#   必须再跑 morphfix + skinfix --fullmat 才是成品。这一步漏过一次，实机症状是
#   "眼球糊上脸皮 + 整张脸像糊了一层皮"（见 Knowledge/蒂法换头工程.md §15）。
#   本脚本把【两道补丁 + 关卡 2 + 双端装机 + md5 校验】串成一条命令。
#
# 用法（系统 python，不需要 Blender）：
#   python install_pack.py                 # 默认：TifaHead2 模块 + head_tifa_a
#   python install_pack.py --module <模块目录> --filter <网格名子串>
#   python install_pack.py --dry-run       # 只体检报告，不写任何文件
#
# 退出码：0 = 全部通过并装机；1 = 中途失败（不装机）
import argparse
import hashlib
import os
import shutil
import subprocess
import sys

# 控制台可能是 GBK，中文/符号会 UnicodeEncodeError；统一兜底成可替换
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))     # LivingWorldNpcs 模块根
TPACCLI = os.path.join(REPO, "tools", "face-pipeline", "tpactool", "TpacToolCLI",
                       "bin", "Release", "net9.0", "tpaccli.exe")
WORK = os.path.join(REPO, "Debug", "offline", "tifa_postpublish")

# 两个客户端（模块目录；发布版本按本机实际路径改这里）
CLIENTS = [
    r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\TifaHead2",
    r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\TifaHead2",
]
# 白编译预警线：原始产出约 9~17 MB，打过补丁的成品约 26 MB
RAW_WARN_BYTES = 20 * 1024 * 1024


def md5(path):
    h = hashlib.md5()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest().upper()


def run(cmd, tag):
    print("\n$ %s" % " ".join(str(c) for c in cmd))
    p = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    out = (p.stdout or "") + (p.stderr or "")
    tail = [ln for ln in out.splitlines() if ln.strip()][-12:]
    for ln in tail:
        print("   " + ln)
    if p.returncode != 0:
        print("   [!] %s 退出码 %d" % (tag, p.returncode))
    return out


def stage_dir(name):
    d = os.path.join(WORK, name)
    shutil.rmtree(d, ignore_errors=True)
    os.makedirs(d, exist_ok=True)
    return d


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--module", default=CLIENTS[0], help="模块目录（默认 1.2.12 客户端）")
    ap.add_argument("--filter", default="head_tifa_a", help="网格名子串")
    ap.add_argument("--clients", nargs="*", default=CLIENTS, help="要装机的模块目录列表")
    ap.add_argument("--check-ref", default=None, help="关卡 2 的参照头（默认脚本自带；男头传 head_male_a）")
    ap.add_argument("--check-ref-pack", default=None, help="关卡 2 参照头所在 AssetPackages（男头传 core_game 硬链接目录）")
    ap.add_argument("--check-window", default=None, help='关卡 2 着陆窗口 "x0,x1,y0,y1,z0,z1"')
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    pack = os.path.join(args.module, "AssetPackages", "pack0.tpac")
    if not os.path.exists(pack):
        print("FAIL: 找不到 %s" % pack)
        return 1

    size = os.path.getsize(pack)
    print("=" * 72)
    print("编辑器产出：%s" % pack)
    print("            %s B  (%.1f MB)  改动时间 %s"
          % (f"{size:,}", size / 1048576.0,
             __import__("datetime").datetime.fromtimestamp(os.path.getmtime(pack)).strftime("%m-%d %H:%M:%S")))
    if size < RAW_WARN_BYTES:
        print("  [!] 小于 %.0f MB —— 很可能是【白编译包】（没打补丁），继续跑补丁即可（这正是本脚本的目的）"
              % (RAW_WARN_BYTES / 1048576.0))
    else:
        print("  （尺寸看着像已打过补丁的成品；补丁脚本幂等，重跑无害）")
    print("=" * 72)

    if not os.path.exists(TPACCLI):
        print("FAIL: 找不到 tpaccli：%s（先 dotnet build -c Release）" % TPACCLI)
        return 1
    if args.dry_run:
        print("\n--dry-run：到此为止，未写任何文件")
        return 0

    # 1) 取输入
    d_in = stage_dir("in")
    shutil.copy2(pack, os.path.join(d_in, "pack0.tpac"))

    # 2) morphfix：补 morph 帧到 101 + 同步 VertexKeyCount
    # 🔴 这里【不能】再调 stage_dir("m1")：它 rmtree 清空目录，会把 morphfix 刚写出的包删掉
    #    （实测踩过：m1 变空 → 下面 copy 抛 FileNotFoundError）。stage_dir 只用于"给外部工具腾输出目录"。
    m1 = os.path.join(WORK, "m1")
    out1 = run([TPACCLI, "morphfix", "--packdir", d_in, "--filter", args.filter, "--out", m1], "morphfix")
    src1 = os.path.join(m1, "pack0.tpac")
    d_m1 = os.path.join(WORK, "s1")
    stage_dir("s1")
    if os.path.exists(src1) and os.path.getsize(src1) > 0:
        shutil.copy2(src1, os.path.join(d_m1, "pack0.tpac"))
    else:
        print("   （morphfix 未产出，用输入包继续 —— 帧数可能不足，实机会崩）")
        shutil.copy2(os.path.join(d_in, "pack0.tpac"), os.path.join(d_m1, "pack0.tpac"))

    # 3) skinfix --fullmat：四角色材质配方 + MaterialFlags
    m2 = os.path.join(WORK, "m2")
    out2 = run([TPACCLI, "skinfix", "--packdir", d_m1, "--filter", args.filter, "--out", m2,
                "--fullmat"], "skinfix")
    d_m2 = os.path.join(WORK, "s2")
    stage_dir("s2")
    src2 = os.path.join(m2, "pack0.tpac")
    if os.path.exists(src2) and os.path.getsize(src2) > 0:
        shutil.copy2(src2, os.path.join(d_m2, "pack0.tpac"))
    else:
        print("   （skinfix 无输出，用上一步的包继续 —— 材质会是白编译状态）")
        shutil.copy2(os.path.join(d_m1, "pack0.tpac"), os.path.join(d_m2, "pack0.tpac"))

    # 4) 关卡 2：编译产物落点
    print("\n---- 关卡 2（编译产物落点）----")
    chk = [sys.executable, os.path.join(REPO, "tools", "face-pipeline", "scripts", "check_head_space.py"),
           "--pack", d_m2, "--mesh", args.filter]
    if args.check_ref:
        chk += ["--ref", args.check_ref]
    if args.check_ref_pack:
        chk += ["--ref-pack", args.check_ref_pack]
    if args.check_window:
        # 🔴 必须拼成 "--window=-0.12,0.12,..." 单个 token：窗口值以 '-' 开头，
        #    拆成两个 argv 元素时 argparse 会把它当成选项 → "expected one argument"（实测踩到）
        chk.append("--window=" + args.check_window)
    p = subprocess.run(chk, capture_output=True, text=True, encoding="utf-8", errors="replace")
    print((p.stdout or "") + (p.stderr or ""))
    if p.returncode != 0:
        print("FAIL: 关卡 2 未通过，不装机")
        return 1

    # 5) 体检：子网格数与帧数
    print("---- 结构体检 ----")
    info = run([TPACCLI, "morphinfo", "--packdir", d_m2, "--filter", args.filter], "morphinfo")
    for ln in info.splitlines():
        s = ln.strip()
        if s.startswith("== ") or ("帧" in s and ("head_tifa" in s or "verts=" in s)):
            print("   " + s)

    # 6) 装机 + md5 校验
    final = os.path.join(d_m2, "pack0.tpac")
    digest = md5(final)
    print("\n---- 装机 ----")
    print("成品：%s B  md5 %s" % (f"{os.path.getsize(final):,}", digest))
    ok = True
    for c in args.clients:
        dst_dir = os.path.join(c, "AssetPackages")
        if not os.path.isdir(dst_dir):
            print("  跳过（目录不存在）：%s" % c)
            continue
        dst = os.path.join(dst_dir, "pack0.tpac")
        shutil.copy2(final, dst)
        got = md5(dst)
        flag = "OK" if got == digest else "MD5 不一致！"
        print("  -> %-70s %s" % (dst, flag))
        ok = ok and (got == digest)

    print("\n---- 收尾 ----")
    print("  1) 若模块在编辑模式（有 Assets\\、没有 ModuleData\\skins.xslt）：跑 to_game_mode.bat")
    print("  2) 启动游戏验证")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
