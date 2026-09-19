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
#   python install_pack.py --clear-flags   # ⚠️ 只在装「UV 沿用源模型布局」的头时才加（战无2 的 28 张脸 /
#                                          #    织田信长）；蒂法 / 萨菲罗斯务必**不加**，加了眼睛糊掉（见第 3.5 步）
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
WORK = os.path.join(REPO, "Debug", "offline", "自定义头", "tifa_postpublish")

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
    ap.add_argument("--check-ref-pack", default=None, help="关卡 2 参照头所在 AssetPackages（男头传 `Debug/offline/自定义头/core_game` 硬链接目录）")
    ap.add_argument("--check-window", default=None, help='关卡 2 着陆窗口 "x0,x1,y0,y1,z0,z1"')
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--gate", action="store_true",
                    help="装机后跑 check_chan_anatomy 闸门（**装头部必加**；甲/武器不要加）"
                         "—— 防「装进去的其实不是本轮产物」这类事故（2026-09-19 陈旧 m1 实录）")
    ap.add_argument("--clear-flags", action="store_true",
                    help="清空脸部角色标记（MaterialFlags）—— **只对「UV 沿用源模型布局」的头用**"
                         "（战无2 的 28 张脸 / 织田信长）；蒂法 / 萨菲罗斯（UV 对齐原版画布）"
                         "必须保留标记，加了此开关会把他们的眼睛弄糊。详见第 3.5 步注释")
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
    # 🔴 这里【不能】在 morphfix 之后再调 stage_dir("m1")：它 rmtree 清空目录，会把刚写出的包删掉
    #    （实测踩过：m1 变空 → 下面 copy 抛 FileNotFoundError）。stage_dir 只用于"给外部工具腾输出目录"。
    # 🔴🔴 必须**在跑之前**清空 m1/m2：工具"跳过"时（例如 morphfix 判定"已对齐"就不产出）
    #    脚本会回落到 `os.path.exists(src1)` 判断 —— 若 m1 里躺着**上一轮**的包，它会静默把陈旧包
    #    当成本轮产物一路带下去。2026-09-19 实锤：Publish 出来的新包（含表情帧）全程没被用上，
    #    最终装进模块的是 19:51 那轮的旧头（表情帧全空）。与"经验 #3（s3 会静默给陈旧产物）"同一类坑。
    m1 = os.path.join(WORK, "m1")
    stage_dir("m1")
    out1 = run([TPACCLI, "morphfix", "--packdir", d_in, "--filter", args.filter, "--out", m1], "morphfix")
    src1 = os.path.join(m1, "pack0.tpac")
    d_m1 = os.path.join(WORK, "s1")
    stage_dir("s1")
    if os.path.exists(src1) and os.path.getsize(src1) > 0:
        shutil.copy2(src1, os.path.join(d_m1, "pack0.tpac"))
    else:
        print("   （morphfix 未产出 = 判定已对齐，用输入包继续）")
        shutil.copy2(os.path.join(d_in, "pack0.tpac"), os.path.join(d_m1, "pack0.tpac"))

    # 3) skinfix --fullmat：四角色材质配方 + MaterialFlags
    m2 = os.path.join(WORK, "m2")
    stage_dir("m2")          # 同上：必须在跑之前清，否则"跳过"时会捡到上一轮的陈旧包
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

    # 3.5) metaparts --clearflags：清脸部角色标记 —— **默认不做，必须显式加 --clear-flags**
    #
    # 🔴 清不清，看这个头的 **UV 走哪套布局**（2026-09-16 晚修正；此前这里写反过，把蒂法弄糊了）：
    #
    #   ① UV 沿用**源模型自带布局**的头（战无2 那 28 张脸、织田信长）→ **必须清空**。
    #      带标记 = 引擎按**原版画布布局**把五官画到脸贴上；这类头的贴图是源模型图集（战无2 的脸
    #      挤在图集角落），两边对不上 → 实机症状「眼睛/嘴糊成一片、头发那块被涂成肤色」。
    #   ② UV **对齐原版画布**的头（蒂法 / 萨菲罗斯——当年专门做过对齐）→ **必须保留**。
    #      清掉 = 脸部贴图生成器认不出哪块是脸/嘴/眼/睫 → **把脸皮合成贴到了眼球上** → 同样是
    #      「眼睛糊掉」，症状与①一模一样，所以两条一起发作时极难归因。
    #
    #   ⇒ 「自定义头必须清标记」这句话**对①成立、对②正好相反**，不能一刀切。
    #     🔴 09-16 按①一刀切跑了一遍全包 → 把蒂法 / 萨菲罗斯的标记一起扫了（用户报「蒂法眼睛不对」）。
    #        当时的注释还错写成「蒂法 / 萨菲罗斯验收时 MaterialFlags 都是空的」——**历史包实测证伪**
    #        （Debug/offline/自定义头/tifa_flag_repair/backup、Debug/offline/自定义头/neck_tint/pack0_before.tpac 里
    #        两人四个/三个标记齐全）。误判来源 = 把 SW2 侧的**件位顺序**结论（`[0]脸[1]嘴[2]眼`，
    #        见 tools/sw2-pipeline/parts_table.py）当成了「标记为空」。
    #        修复命令（窄 filter 逐个补回）：`skinfix --fullmat`，见 Debug/offline/自定义头/tifa_flag_repair/。
    #
    #   ⚠️ 标记是被上面两步**主动补上**的：`morphfix` 与 `skinfix --fullmat` 都有「为空就按材质名补标记」
    #      的兜底（当年为防脸部生成器空指针加的）。所以清必须**在它们之后**，否则下次装机又补回来。
    #   完整来龙去脉：Knowledge/蒂法换头工程.md §16 + plans/rules/pitfalls.md「脸部贴图糊成一片」条。
    if args.clear_flags:
        m3 = os.path.join(WORK, "m3")
        out3 = run([TPACCLI, "metaparts", "--packdir", d_m2, "--filter", args.filter,
                    "--out", m3, "--clearflags"], "metaparts --clearflags")
        d_m3 = os.path.join(WORK, "s3")
        stage_dir("s3")
        src3 = os.path.join(m3, "pack0.tpac")
        if os.path.exists(src3) and os.path.getsize(src3) > 0:
            shutil.copy2(src3, os.path.join(d_m3, "pack0.tpac"))
        else:
            print("   ⚠️ metaparts 无输出，用上一步的包继续 —— 脸部标记没清，实机会眼睛/嘴糊成一片")
            shutil.copy2(os.path.join(d_m2, "pack0.tpac"), os.path.join(d_m3, "pack0.tpac"))
        d_m2 = d_m3          # 后续步骤（关卡 2 / 装机）都接在清完标记的包上
    else:
        print("\n[3.5] 跳过 clearflags（默认）：MaterialFlags 保留原样。")
        print("      只有「UV 沿用源模型布局」的头才需要清（战无2 的 28 张脸 / 织田信长）→ 那种情况加 --clear-flags；")
        print("      蒂法 / 萨菲罗斯的 UV 对齐原版画布，**清了眼睛会糊**，别加。")

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

    # 7) 闸门：装完直接在**装机后的包**上验位移场落点（可选，头部必加）
    if args.gate:
        print("\n---- 闸门（101 条通道落点，验装机后的包）----")
        g = [sys.executable, os.path.join(REPO, "tools", "face-pipeline", "scripts", "check_chan_anatomy.py"),
             "--packdir", os.path.join(args.clients[0], "AssetPackages"), "--filter", args.filter]
        p = subprocess.run(g, capture_output=True, text=True, encoding="utf-8", errors="replace")
        print((p.stdout or "") + (p.stderr or ""))
        if p.returncode != 0:
            print("\nFAIL: 闸门未通过 —— 包已装进模块但**先别进游戏**（说明装的可能不是本轮产物，"
                  "或位移场有问题）；旧包在各阶段目录里可回滚")
            return 1

    print("\n---- 收尾 ----")
    print("  1) 若模块在编辑模式（有 Assets\\、没有 ModuleData\\skins.xslt）：跑 to_game_mode.bat")
    print("  2) 启动游戏验证")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
