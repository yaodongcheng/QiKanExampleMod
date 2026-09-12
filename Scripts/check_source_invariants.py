#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""C# 源码不变量体检（启动链纪律的离线防线）
============================================================================
**查什么**：那些「写进清单/台账、但只能靠人记住」的 C# 侧纪律——它们**没有任何编译期或数据侧断言**，
  删掉/漏掉都不会报错，只会在实机某个时点静默失效。本脚本把这些纪律变成 grep 断言。

规则三条（全部先拿现网源码验过，避免拿设计当缺陷）：

  ① **相机缩放复位的两行必须留**（清单 1.5）：`ResetCamera(true, true)` 与
     `TeleportCameraToMainParty(` 至少各出现一次。理由：原版建号内容同款调用，
     它兼管**镜头缩放复位**（新相机默认距离 2.5 = 贴脸 → 复位成 15）——删了镜头贴脸，
     而"相机不对准玩家"的正解是出生点提前置位（另一条，见 ②），两者不是一回事。
  ② **出生点必须在「世界创建期」写**（清单 1.5 / 雷 30/33）：`StartingPosition` 虚属性 +
     在 `OnNewGameCreatedPartialFollowUp` 里使用它。理由：`MapCameraView.Initialize` 读的是
     **主队运行时坐标**，读点早于建号完成回调 → 写在建号回调里 = 相机读不到（零补丁做法）。
  ③ **注释停用/未登记的文件，不得被活跃源码引用**（雷 40 同族；GameMenuLogger 实录）：
     csproj 里被注释掉的 `<Compile Include>` 名单 → 剥掉 C# 注释后全仓 grep 类名，
     有命中 = 活跃代码引用了不参与编译的类 → **编译期报错或运行期 null**。
     `GameMenuLogger` 就是「看似诊断、实为状态源」被下线时靠编译错误抓出来的——
     本条把它提前到离线。

⚠️ 本脚本**只 grep 源码、不编译**（编译是另一条：`dotnet build` 由人/CI 跑）。
   grep 断言的边界：能防「被删/被改坏」，防不了「写错逻辑」——写错逻辑靠实机与单测。

Usage:
  python Scripts/check_source_invariants.py [--repo PATH]
  --repo 缺省 = 脚本上一级（仓库根）
Exit: 0 无 ERROR / 1 有 ERROR / 2 fatal。
"""
import argparse
import re
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = Path(__file__).resolve().parent
DEFAULT_REPO = HERE.parent

# 排除的目录：构建产物、第三方、对象目录
SKIP_DIRS = {"bin", "obj", ".vs", "packages", "node_modules", "_archive"}

# 剥离 C# 注释：块注释 + 行注释（**必须先剥注释再 grep**——本仓大量「已退役 XX」注释里
# 就写着被删类名，不剥注释必然假红；2026-09-12 实测 HeroSelectOverlay/MapScreenCameraPatch 两例）
BLOCK_COMMENT = re.compile(r"/\*.*?\*/", re.S)
LINE_COMMENT = re.compile(r"//[^\n]*")

# 规则 ①：相机复位两行（缺 = 镜头贴脸；正解是出生点，不是删这行）
CAMERA_RESET = [
    (re.compile(r"ResetCamera\s*\(\s*true\s*,\s*true\s*\)"),
     "ResetCamera(true, true) —— 兼管镜头缩放复位（构造默认 2.5 → 15）"),
    (re.compile(r"TeleportCameraToMainParty\s*\("),
     "TeleportCameraToMainParty() —— 把镜头移到主队"),
]

# 规则 ②：出生点三件（缺任一 = 相机读不到出生点）
SPAWN_POINT = [
    (re.compile(r"StartingPosition"), "StartingPosition（内容包覆写的出生点虚属性）"),
    (re.compile(r"OnNewGameCreatedPartialFollowUp"), "OnNewGameCreatedPartialFollowUp（世界创建期时点）"),
]


def strip_comments(text):
    return LINE_COMMENT.sub("", BLOCK_COMMENT.sub("", text))


def iter_cs(repo):
    for p in repo.rglob("*.cs"):
        if any(part in SKIP_DIRS for part in p.parts):
            continue
        yield p


def main():
    ap = argparse.ArgumentParser(description="C# source invariants checker")
    ap.add_argument("--repo", default=str(DEFAULT_REPO))
    # 兼容 run_all_checks 的接口（它给每个脚本追加 --module <内容包路径>）——
    # 🔴 新脚本不接这个参数 = argparse exit 2 静默变红（雷 92 实录），一律加上。
    ap.add_argument("--module", default=None, help="兼容 run_all_checks 的接口（本检查用不到）")
    ap.add_argument("-v", "--verbose", action="store_true")
    args = ap.parse_args()

    repo = Path(args.repo)
    if not repo.is_dir():
        print("[FATAL] repo not found: %s" % repo, file=sys.stderr)
        return 2
    cs = sorted(iter_cs(repo))
    if not cs:
        print("[FATAL] 仓库下没有 .cs：%s" % repo, file=sys.stderr)
        return 2

    errors, warns = [], []
    print("C# 源码不变量体检（%s）" % repo)
    print("  扫描 %d 个 .cs（已剥注释后做引用判定）" % len(cs))

    # ── 规则 ① & ②：启动链关键调用留存 ──
    print("\n== 启动链关键调用留存（清单 1.5；删了不报错，只在新档实机暴露） ==")
    for label, pats in (("相机复位两行", CAMERA_RESET), ("出生点（世界创建期）", SPAWN_POINT)):
        for pat, why in pats:
            hits = [p for p in cs if pat.search(p.read_text(encoding="utf-8", errors="replace"))]
            if hits:
                rel = [str(h.relative_to(repo)) for h in hits][:3]
                print("  [ OK ] %s ← %s%s" % (why.split("——")[0].strip(), ", ".join(rel),
                                              " 等" if len(hits) > 3 else ""))
            else:
                errors.append("%s 缺失（%s）" % (label, why))
                print("  [ERROR] 找不到 %s —— %s" % (why.split("——")[0].strip(), why))

    # ── 规则 ③：注释停用/未登记的文件不得被活跃源码引用 ──
    csproj = next(iter(repo.glob("ExampleModVS/**/*.csproj")), None)
    print("\n== 注释停用/未登记的文件 ↔ 活跃源码引用（雷 40 同族；GameMenuLogger 实录） ==")
    if csproj is None:
        warns.append("未找到 .csproj，规则 ③ 跳过")
        print("  [WARN] 未找到 .csproj，跳过")
    else:
        txt = csproj.read_text(encoding="utf-8", errors="replace")
        # csproj 里**注释掉**的 Compile 条目 → 文件名 → 类名（= 去掉扩展名的 basename）
        commented = re.findall(r"<!--\s*<Compile\s+Include=\"([^\"]+)\"", txt)
        print("  csproj 注释停用条目：%d 个" % len(commented))
        # 活跃源码（剥注释）合并成一份，逐类名 grep
        active = {p: strip_comments(p.read_text(encoding="utf-8", errors="replace")) for p in cs}
        bad = 0
        for inc in commented:
            name = re.sub(r"\.cs$", "", inc.replace("\\", "/").split("/")[-1])
            # 该类名在活跃源码里被引用吗（排除文件名本身——文件可能还在磁盘上但不参与编译）
            holders = [str(p.relative_to(repo)) for p, t in active.items()
                       if re.search(r"\b%s\b" % re.escape(name), t)
                       and p.stem != name]
            if holders:
                bad += 1
                errors.append("注释停用的 %s 仍被活跃源码引用：%s" % (name, ", ".join(holders[:3])))
                print("  [ERROR] %s（csproj 已注释停用）仍被引用 ← %s" % (name, ", ".join(holders[:3])))
            elif args.verbose:
                print("  [ OK ] %s 无活跃引用" % name)
        if not bad:
            print("  [ OK ] %d 个停用条目均无活跃源码引用" % len(commented))

    print("\nSummary: errors=%d warnings=%d" % (len(errors), len(warns)))
    if errors:
        print("\n❌ 有 %d 条硬错误（上面逐条）：这些纪律删掉不报错，只在新档实机静默失效。" % len(errors))
        return 1
    print("\n✅ 硬错误 0：启动链关键调用在位、停用的类没被活跃代码引用")
    return 0


if __name__ == "__main__":
    sys.exit(main())
