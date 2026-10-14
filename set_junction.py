#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""
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
🔴 路径不写死：主项目 = 本脚本自己往上找（…\Modules\LivingWorldNpcs → …\Modules）；
   其他客户端由 mb2_clients.py 现扫现读（MB2_PATH 注册表值 + Steam 各库 + Version.xml）。
   所以换台机器、只装一份 Steam 正式包都不会出错（那种情况就是"没有别的客户端"）。
🔴 加了新模块 → 改下面 MODULES 表即可，逻辑不用动。
⚠️ 联接只是"让模块可见"；**要不要加载由启动器勾选决定**（`_MODULES_` 列表），
   建完联接不会自动启用任何模块。
"""
import os
import subprocess
import sys
from pathlib import Path

import mb2_clients

# 主项目模块根（真源）= 本脚本所在模块的**上一层**。
#   本脚本在 <客户端>\Modules\LivingWorldNpcs\ 下 ⇒ 上一层就是 Modules。
#   换台机器、换个盘符都不用改（以前这里写死了 H:\SteamLibrary\…）。
MOD_ROOT = Path(__file__).resolve().parent        # …\Modules\LivingWorldNpcs
MAIN_MODULES = str(MOD_ROOT.parent)               # …\Modules       ← 真源
MAIN_CLIENT = MOD_ROOT.parent.parent              # 主项目所在的那份客户端根

# ===== 本次要对哪些客户端动手（VSCode 里改这里，点运行按钮即执行）=====
#   —— 与 set_mb2_path.py 的 DEFAULT_VERSION 同一个习惯：**开头一个变量 = 这次干谁**。
#   键 = 各客户端 Version.xml 里**现读**的版本号（跑 `python set_junction.py --info` 看本机有哪几个）。
#   留空 [] = 不动手（等价于 --info）。
#   ⚠️ 本机只装了一份 Steam 正式包时，这里没有可填的项 —— 脚本会直接告诉你"没有别的客户端"。
TARGET_CLIENTS = ["1.2.12", "1.3.15", "1.4.8"]


def other_clients():
    """→ {键: Client}，只含"不是主项目所在那份"的客户端。

    键 = mb2_clients.display_key()：备份客户端用版本号，Steam 主目录那份用 `steam`
    （它的版本号会跟着官方更新变，不能当标识）。

    🔴 不写死路径：由 mb2_clients.py 现扫现读
       （MB2_PATH 注册表值 + Steam 各库 libraryfolders.vdf + 各客户端 Version.xml）。
    """
    out = {}
    for c in mb2_clients.discover():
        try:
            if Path(c.path).resolve() == MAIN_CLIENT.resolve():
                continue
        except OSError:
            pass
        out[mb2_clients.display_key(c)] = c
    return out


# 要联接的模块（= 主项目 Modules\ 下的目录名）
#   LivingWorldNpcs —— 基座（玩法框架）
#   Taikou          —— 日本战国数据包（内容包）
#   LwnAnim         —— 资产沙箱（2026-10-09 由旧名 TaikouAnim 改名；给 LWN 编通用玩法的资产，
#                      本身不注册任何东西，运行时不要勾选）
#   Shokuho_CNs     —— 中文语言包（Taikou 名字池借用的中文来源）
MODULES = ["Taikou", "LwnAnim", "Shokuho_CNs", "LivingWorldNpcs"]


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


def show_info(clients):
    print("主项目模块根(真源): %s" % MAIN_MODULES)
    print("主项目所在客户端  : %s" % MAIN_CLIENT)
    print()
    if not clients:
        print("本机只有主项目这一份客户端 —— 没有需要联接的目标（别的机器上才有多份）。")
        return
    for cname, c in sorted(clients.items(), key=lambda kv: mb2_clients.version_key(kv[1].version),
                           reverse=True):
        tag = ("Steam 主目录，当前 v%s（会变）" % c.version) if c.steam else "备份客户端"
        print("[%s] (%s)  %s" % (cname, tag, c.path))
        for m in MODULES:
            lp = link_path(c.path, m)
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
    for s in (sys.stdout, sys.stderr):
        if hasattr(s, "reconfigure"):
            s.reconfigure(encoding="utf-8", errors="replace")

    # 主项目位置是"自己往上找"来的，先确认布局没跑偏（跑偏了会往错地方建联接）
    if MOD_ROOT.parent.name.lower() != "modules":
        print("[错误] 本脚本不在 <客户端>\\Modules\\<模块>\\ 下（现在在 %s），无法定位主项目。" % MOD_ROOT)
        return

    clients = other_clients()
    info_only = len(sys.argv) > 1 and sys.argv[1] in ("--info", "-i", "info")

    show_info(clients)

    if info_only:
        return

    if not clients:
        print("没有别的客户端要动手，结束。")
        return

    if not TARGET_CLIENTS:
        print("TARGET_CLIENTS 为空 → 本次只查看、不动手。要执行就在脚本开头填客户端名（版本号）。")
        return

    unknown = [c for c in TARGET_CLIENTS if c not in clients]
    if unknown:
        print("[错误] TARGET_CLIENTS 里有本机没有的客户端: %s" % ", ".join(unknown))
        print("       本机可用的: %s" % " / ".join(sorted(clients, key=lambda k: mb2_clients.version_key(
            clients[k].version), reverse=True)))
        return

    print("=== 本次目标客户端：%s ===" % ", ".join(TARGET_CLIENTS))
    created, skipped, failed = 0, 0, 0
    for cname in TARGET_CLIENTS:
        croot = clients[cname].path
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
    for cname, c in clients.items():
        for m in MODULES:
            print("  [%s] %-17s %s" % (cname, m, link_state(link_path(c.path, m), main_path(m))))
    print()
    print("提醒：联接只是让模块可见；**要不要加载由启动器勾选决定**（_MODULES_ 列表）。")


if __name__ == "__main__":
    main()
