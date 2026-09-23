#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
set_junction.py —— 把各备份客户端里的模块目录指向主项目（目录联接 junction）。

效果：备份客户端加载这些模块时，读到的就是主项目那一份
      （SubModule.xml / ModuleData / GUI / 音效 / bin DLL 全部同一份，自动同步）。

用法：
    【VSCode 方式（推荐）】改脚本开头的 **TARGET_CLIENTS**（本次要对哪些客户端动手）
        → 点运行按钮即执行，不用终端。
    python set_junction.py          # 按 TARGET_CLIENTS 创建缺失的联接
    python set_junction.py --info   # 只查看状态（列出**全部**客户端 × 全部模块），不改动

🔴 安全约定：junction 不能建在已有目录上。若目标位置已有**普通目录**（残留拷贝），
   脚本只报告、**绝不自动删除** —— 要删由你确认后自己删（避免误删）。
🔴 加了新模块 / 新客户端 → 改下面两个表即可，逻辑不用动。
⚠️ 联接只是"让模块可见"；**要不要加载由启动器勾选决定**（`_MODULES_` 列表），
   建完联接不会自动启用任何模块。
"""
import os
import subprocess
import sys

# 主项目模块根（真源；各客户端都指向这里）
MAIN_MODULES = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules"

# ===== 本次要对哪些客户端动手（VSCode 里改这里，点运行按钮即执行）=====
#   —— 与 set_mb2_path.py 的 DEFAULT_VERSION 同一个习惯：**开头一个变量 = 这次干谁**。
#   填下面 CLIENTS 表里的键名；`--info` 永远只查看、不改动。
#   留空 [] = 不动手（等价于 --info）。
TARGET_CLIENTS = ["1.2.12", "1.3.15", "1.4.8"]

# 客户端：显示名 → 游戏根目录
CLIENTS = {
    "1.2.12":  r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord",
    "1.3.15":  r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.3.15\Mount & Blade II Bannerlord",
    "1.4.8":   r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.4.8\Mount & Blade II Bannerlord",
}

# 要联接的模块（= 主项目 Modules\ 下的目录名）
#   LivingWorldNpcs —— 基座（玩法框架）
#   Taikou          —— 日本战国数据包（内容包）
#   TaikouAnim      —— 资产中转沙箱（本身不注册任何东西；带上是为了与 1.2.12 同构）
#   Shokuho_CNs     —— 中文语言包（Taikou 名字池借用的中文来源）
MODULES = ["Taikou", "TaikouAnim", "Shokuho_CNs", "LivingWorldNpcs"]


def main_path(module):
    return os.path.join(MAIN_MODULES, module)


def link_path(client_root, module):
    return os.path.join(client_root, "Modules", module)


def link_state(link, main):
    """'已指向主项目 [OK]' / '普通目录(残留，需你手动删)' / '不存在' / '指向其他目标: …' / '死链'"""
    if not os.path.exists(link) and not os.path.islink(link):
        # os.path.exists 对死链返回 False，islink 对 junction 可能为 True —— 两者都不成立才是真不存在
        return "不存在"
    try:
        real = os.path.realpath(link)
    except OSError:
        return "异常（读不到真实路径）"
    if real == os.path.realpath(main):
        return "已指向主项目 [OK]"
    if os.path.isdir(link):
        return "普通目录(残留，需你手动删)"
    return "指向其他目标: %s" % real


def show_info():
    print("主项目模块根: %s" % MAIN_MODULES)
    print()
    for cname, croot in CLIENTS.items():
        print("[%s]  %s" % (cname, croot))
        for m in MODULES:
            lp = link_path(croot, m)
            mp = main_path(m)
            main_ok = "有" if os.path.isdir(mp) else "**缺失**"
            print("    %-17s 主项目:%s  ←  %s" % (m, main_ok, link_state(lp, mp)))
        print()


def create_junction(link, main):
    state = link_state(link, main)
    if state != "不存在":
        print("  [跳过] %s\n         当前状态: %s" % (link, state))
        return False
    if not os.path.isdir(main):
        print("  [失败] 主项目模块不存在: %s" % main)
        return False
    os.makedirs(os.path.dirname(link), exist_ok=True)
    r = subprocess.run(["cmd", "/c", "mklink", "/J", link, main],
                       capture_output=True, text=True)
    ok = os.path.exists(link)
    print("  %s %s" % ("[成功]" if ok else "[失败]", link))
    for stream in (r.stdout, r.stderr):
        if stream.strip():
            print("         %s" % stream.strip())
    return ok


def main():
    if len(sys.argv) > 1 and sys.argv[1] in ("--info", "-i", "info"):
        show_info()
        return

    show_info()

    if not TARGET_CLIENTS:
        print("TARGET_CLIENTS 为空 → 本次只查看、不动手。要执行就在脚本开头填客户端名。")
        return

    unknown = [c for c in TARGET_CLIENTS if c not in CLIENTS]
    if unknown:
        print("[错误] TARGET_CLIENTS 里有未知客户端名: %s" % ", ".join(unknown))
        print("       可用: %s" % ", ".join(CLIENTS.keys()))
        return

    print("=== 本次目标客户端：%s ===" % ", ".join(TARGET_CLIENTS))
    created, skipped, failed = 0, 0, 0
    for cname in TARGET_CLIENTS:
        croot = CLIENTS[cname]
        for m in MODULES:
            lp = link_path(croot, m)
            mp = main_path(m)
            if link_state(lp, mp) == "不存在":
                print("[%s] %s" % (cname, m))
                if create_junction(lp, mp):
                    created += 1
                else:
                    failed += 1
            else:
                skipped += 1
    print()
    print("=== 结果：新建 %d / 已存在跳过 %d / 失败 %d ===" % (created, skipped, failed))
    print()
    print("=== 复核 ===")
    for cname, croot in CLIENTS.items():
        for m in MODULES:
            print("  [%s] %-17s %s" % (cname, m, link_state(link_path(croot, m), main_path(m))))
    print()
    print("提醒：联接只是让模块可见；**要不要加载由启动器勾选决定**（_MODULES_ 列表）。")


if __name__ == "__main__":
    main()
