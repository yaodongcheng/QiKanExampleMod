#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""
set_mb2_path.py —— 切换 LivingWorldNpcs 编译 / F5 调试目标游戏版本（环境变量方式）。

原理：csproj 的 DLL 引用、版本宏都读 $(MB2_PATH)，它来自用户环境变量 MB2_PATH。
本脚本用 setx 改写该环境变量（持久化到注册表，无需管理员权限）。

🔴 路径不写死：本机装了哪几份骑砍2、各自什么版本，由 mb2_clients.py 现扫现读
   （MB2_PATH 注册表值 + Steam libraryfolders.vdf → 各库 common 根 → Version.xml）。
   所以换台机器、只装一份 Steam 正式包、或者备份包多装几个，都不用改这个脚本。

用法:
    【VSCode 方式（推荐）】改下面的 DEFAULT_VERSION 变量 → 点运行按钮 = 切换，不用终端
    【命令行方式】python set_mb2_path.py 1.3.15   （带参数，优先于变量）
    python set_mb2_path.py                        （变量留空时 = 只查询当前值）
    python set_mb2_path.py steam                  （切到 Steam 主目录那份，不管它是哪个版本）
    python set_mb2_path.py --list                 （只看本机有哪些，不切换）

⚠️ 关键：VS2022 在启动时捕获环境变量——改完后必须【重启 VS2022】才生效。
重启后:
    F6 编译 = 该版本 DLL 引用 + 版本宏（csproj 自动检测 Version.xml）
    F5 调试 = 启动该版本的 Bannerlord.exe（csproj.user 的 StartProgram 用 $(MB2_PATH)，自动跟随）
"""
import subprocess
import sys

import mb2_clients

# ===== 目标版本 = 你要填的"初始参数"（VSCode 里改这里，点运行按钮即切换）=====
# 填本机**实际装了**的版本号（跑 `python set_mb2_path.py --list` 看有哪些）
# 也可以填 "steam" = 切到 Steam 主目录那份（跟随官方更新，版本号会变，所以别写死版本号）
# 留空 ""：无参数运行 = 只查询当前值，不切换
# 命令行带参数（python set_mb2_path.py 1.4.8）优先于本变量
DEFAULT_VERSION = "1.2.12"


def main():
    for s in (sys.stdout, sys.stderr):
        if hasattr(s, "reconfigure"):
            s.reconfigure(encoding="utf-8", errors="replace")

    # 版本来源（优先级）：① 命令行参数 ② 脚本开头 DEFAULT_VERSION 变量
    asked = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_VERSION
    if asked in ("--list", "-l", "list"):
        _print_clients()
        return

    clients = mb2_clients.discover()
    table = mb2_clients.by_version(clients)

    if not asked:
        cur = mb2_clients.current_mb2_path()
        print("当前 MB2_PATH (用户环境变量): %s" % (cur if cur else "(未设置)"))
        print()
        print("本机骑砍2客户端：")
        print(mb2_clients.describe(clients))
        print()
        print("要切换版本：在脚本开头改 DEFAULT_VERSION 后点运行按钮，")
        print("或命令行执行: python set_mb2_path.py <版本号>  /  python set_mb2_path.py steam")
        return

    if not clients:
        print("[错误] 本机没找到任何骑砍2客户端。")
        print(mb2_clients.describe(clients))
        sys.exit(1)

    # "steam" = 跟随官方更新的那份主目录，不管它现在什么版本
    if asked.lower() == "steam":
        pick = next((c for c in clients if c.steam), None)
        if pick is None:
            print("[错误] 本机没找到 Steam 主目录那份（只有备份客户端）。")
            _print_clients(clients)
            sys.exit(1)
        ver, path = pick.version, pick.path
        src = "Steam 主目录（跟随官方更新）"
    else:
        pick = table.get(asked)
        if pick is None:
            print("[错误] 本机没有 %s 这份客户端。" % asked)
            _print_clients(clients)
            print()
            print("可用写法: %s" % mb2_clients.keys_advice(clients))
            sys.exit(1)
        ver, path = pick.version, pick.path
        src = "命令行参数" if len(sys.argv) > 1 else "DEFAULT_VERSION 变量"

    subprocess.run(["setx", "MB2_PATH", str(path)], check=True)

    tag = "Steam 主目录" if pick.steam else "备份客户端"
    print("已修改用户环境变量 MB2_PATH = %s" % path)
    print("  版本: v%s（%s，来源: %s）" % (ver, tag, src))
    print()
    print("⚠️ 重启 VS2022 后生效（VS 启动时才读取环境变量）")
    print("   F6 编译 = %s 版 DLL（引用 + 版本宏自动检测）" % ver)
    print("   F5 调试 = 启动 %s 版的 Bannerlord.exe" % ver)


def _print_clients(clients=None):
    clients = clients if clients is not None else mb2_clients.discover()
    print("MB2_PATH (注册表现读) = %s" % (mb2_clients.current_mb2_path() or "(未设置)"))
    print()
    print("本机骑砍2客户端：")
    print(mb2_clients.describe(clients))


if __name__ == "__main__":
    main()
