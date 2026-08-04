#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
set_junction.py —— 把备份客户端里的 Modules\\LivingWorldNpcs 指向当前项目（目录联接 junction）。

效果：1.2.12 / 1.3.15 拷贝加载这个模块时，读到的就是主项目目录这一份
      （SubModule.xml、ModuleData、GUI、音效、bin DLL 全部同一份，自动同步）。

用法：
    python set_junction.py        # 创建两个联接（先确保目标位置已删除）
    python set_junction.py --info # 只查看当前状态，不改动

前提：拷贝里 Modules\\LivingWorldNpcs 必须不存在（junction 不能建在已有目录上）。
      如果存在，脚本会报错并提示，不会自动删除（避免误删）。
"""
import os
import subprocess
import sys

MAIN = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
LINKS = [
    r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs",
    r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.3.15\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs",
]


def link_state(path):
    """返回 '已指向主项目' / '普通文件夹(残留)' / '不存在' / '指向其他目标'"""
    if not os.path.exists(path) and not os.path.islink(path):
        return "不存在"
    # 判断是不是指向 MAIN 的 junction：穿透后比较真实路径
    try:
        real = os.path.realpath(path)
    except OSError:
        return "异常"
    if real == os.path.realpath(MAIN):
        return "已指向主项目 [OK]"
    if os.path.isdir(path):
        return "普通文件夹(残留，需先删除)"
    return "指向其他目标: %s" % real


def show_info():
    print("主项目: %s" % MAIN)
    for p in LINKS:
        print("  %s -> %s" % (p, link_state(p)))
    print("前提检查: %s" % ("主项目存在 [OK]" if os.path.isdir(MAIN) else "主项目缺失 [!!]"))


def create_junction(link):
    if link_state(link) != "不存在":
        print("[跳过] %s\n      当前状态: %s" % (link, link_state(link)))
        return False
    os.makedirs(os.path.dirname(link), exist_ok=True)
    r = subprocess.run(["cmd", "/c", "mklink", "/J", link, MAIN], capture_output=True, text=True)
    ok = os.path.exists(link)
    print("%s %s" % ("[成功]" if ok else "[失败]", link))
    if r.stdout.strip():
        print("      %s" % r.stdout.strip())
    if r.stderr.strip():
        print("      %s" % r.stderr.strip())
    return ok


def main():
    if len(sys.argv) > 1 and sys.argv[1] in ("--info", "-i", "info"):
        show_info()
        return

    show_info()
    print()
    for p in LINKS:
        create_junction(p)
    print()
    print("=== 验证 ===")
    for p in LINKS:
        print("  %s" % link_state(p))
    print()
    print("完成。现在拷贝里加载的 LivingWorldNpcs 就是主项目这一份。")


if __name__ == "__main__":
    main()
