#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
set_mb2_path.py —— 切换 LivingWorldNpcs 编译 / F5 调试目标游戏版本（环境变量方式）。

原理：csproj 的 DLL 引用、版本宏都读 $(MB2_PATH)，它来自用户环境变量 MB2_PATH。
本脚本用 setx 改写该环境变量（持久化到注册表，无需管理员权限）。

用法:
    【VSCode 方式（推荐）】改下面的 DEFAULT_VERSION 变量 → 点运行按钮 = 切换，不用终端
    【命令行方式】python set_mb2_path.py 1.3.15   （带参数，优先于变量）
    python set_mb2_path.py                        （变量留空时 = 只查询当前值）

⚠️ 关键：VS2022 在启动时捕获环境变量——改完后必须【重启 VS2022】才生效。
重启后:
    F6 编译 = 该版本 DLL 引用 + 版本宏（csproj 自动检测 Version.xml）
    F5 调试 = 启动该版本的 Bannerlord.exe（csproj.user 的 StartProgram 用 $(MB2_PATH)，自动跟随）
"""
import os
import subprocess
import sys

# ===== 目标版本 = 你要填的"初始参数"（VSCode 里改这里，点运行按钮即切换）=====
# 填 "1.2.12" / "1.3.15" / "1.4.8"：无参数运行（VSCode 点运行）时切换到该版本
# 留空 ""：无参数运行 = 只查询当前值，不切换
# 命令行带参数（python set_mb2_path.py 1.3.15）优先于本变量
DEFAULT_VERSION = "1.4.8"

# 版本 -> 游戏根目录（不带尾斜杠；新增客户端位置时改这里）
TARGETS = {
    "1.4.8":  r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord",
    "1.3.15": r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.3.15\Mount & Blade II Bannerlord",
    "1.2.12": r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord",
}


def current_value():
    """读取用户级持久化环境变量（注册表）。"""
    out = subprocess.run(
        ["powershell", "-NoProfile", "-Command",
         "[System.Environment]::GetEnvironmentVariable('MB2_PATH','User')"],
        capture_output=True, text=True).stdout.strip()
    return out


def main():
    # 版本来源（优先级）：① 命令行参数 ② 脚本开头 DEFAULT_VERSION 变量
    # 都没有 = 只查询当前值（啥也不改）
    ver = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_VERSION
    src = "命令行参数" if len(sys.argv) > 1 else "DEFAULT_VERSION 变量"

    if not ver:
        cur = current_value()
        print("当前 MB2_PATH (用户环境变量): %s" % (cur if cur else "(未设置)"))
        print("要切换版本：在脚本开头修改 DEFAULT_VERSION 变量后点运行按钮，")
        print("或命令行执行: python set_mb2_path.py <1.4.8|1.3.15|1.2.12>")
        return

    if ver not in TARGETS:
        print("未知版本: %s，可选: %s" % (ver, ", ".join(TARGETS)))
        sys.exit(1)

    path = TARGETS[ver]
    vxml = os.path.join(path, "bin", "Win64_Shipping_Client", "Version.xml")
    if not os.path.exists(vxml):
        print("[错误] 找不到 %s —— 该版本完整客户端不在预期位置，请检查本脚本的路径表" % vxml)
        sys.exit(1)
    with open(vxml, encoding="utf-8") as f:
        content = f.read()
    if ("v" + ver) not in content:
        print("[警告] Version.xml 内容不匹配 v%s（实际: %s），仍继续，请确认拷贝无误" % (ver, content.strip()))

    subprocess.run(["setx", "MB2_PATH", path], check=True)

    print("已修改用户环境变量 MB2_PATH = %s（来源: %s）" % (path, src))
    print()
    print("⚠️ 重启 VS2022 后生效（VS 启动时才读取环境变量）")
    print("   F6 编译 = %s 版 DLL（引用 + 版本宏自动检测）" % ver)
    print("   F5 调试 = 启动 %s 版的 Bannerlord.exe" % ver)


if __name__ == "__main__":
    main()
