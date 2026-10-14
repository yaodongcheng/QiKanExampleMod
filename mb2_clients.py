#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""
mb2_clients.py —— 找出本机装了哪几份骑砍2（不写死任何绝对路径）

给 set_mb2_path.py / set_junction.py 共用，省得两边各维护一份路径表。

怎么找（两步，缺一不可）
    ① MB2_PATH（**现读注册表**，不看进程快照 —— 铁律 19）→ 从它往上找到
       `steamapps\common` 那一层，就是本机的 Steam 库根。
    ② 拿不到就问 Steam 自己：注册表 SteamPath → `steamapps\libraryfolders.vdf`
       → 列出所有库根。（换台机器、MB2_PATH 还没设过时靠这条。）

在每个库根下扫两处
    common\\Mount & Blade II Bannerlord                ← Steam 主目录（跟随官方更新）
    common\\MB2_Version\\*\\Mount & Blade II Bannerlord  ← 各版本备份客户端
只有带 `bin\\Win64_Shipping_Client\\Version.xml` 的才算数 —— **版本号也从它现读**，
所以 Steam 更新到 1.5.6 也没人会写错。

所以：
    · 本机只有一份 Steam 正式包 → 列表里就一个，照常能用
    · 多装几份备份客户端 → 多几个条目
    · 一个都没有 → 返回空表，由调用方给提示，这里不抛异常

单独运行 = 打印本机清单：
    python mb2_clients.py
"""

import os
import re
import sys
from collections import namedtuple
from pathlib import Path

MAIN_INSTALL = "Mount & Blade II Bannerlord"          # Steam 里那份的目录名（官方默认名）
BACKUP_DIR = "MB2_Version"                            # 各版本备份客户端放这儿
VERSION_XML = os.path.join("bin", "Win64_Shipping_Client", "Version.xml")

# 版本号 → 客户端。steam=True 表示这是 Steam 主目录（跟随官方更新的那份）
Client = namedtuple("Client", "version path steam root")


# ── 环境变量 / 注册表 ────────────────────────────────────────────────

def user_env(name):
    """读用户级持久环境变量（setx 写的地方就是它）。读不到返回空串。"""
    try:
        import winreg
    except ImportError:
        return ""
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as k:
            v, _ = winreg.QueryValueEx(k, name)
            return v if isinstance(v, str) else ""
    except OSError:
        return ""


def current_mb2_path():
    """MB2_PATH 的真实值（先用户级，再机器级）。现读注册表，不信进程快照。"""
    v = user_env("MB2_PATH")
    if v:
        return v
    try:
        import winreg
        with winreg.OpenKey(
                winreg.HKEY_LOCAL_MACHINE,
                r"SYSTEM\CurrentControlSet\Control\Session Manager\Environment") as k:
            val, _ = winreg.QueryValueEx(k, "MB2_PATH")
            return val if isinstance(val, str) else ""
    except (ImportError, OSError):
        return ""


# ── 库根发现 ─────────────────────────────────────────────────────────

def find_common_root(start):
    """从某个客户端根目录往上找 `steamapps\\common` 那一层；找不到返回 None。

    两种形态都认：
        …\\steamapps\\common\\Mount & Blade II Bannerlord                （主目录，上一层就是）
        …\\steamapps\\common\\MB2_Version\\MB2_1.2.12\\Mount & Blade …    （备份，上三层）
    判据是目录名，不是层数 —— 所以客户端目录被改过名也不怕。
    """
    if not start:
        return None
    p = Path(start)
    for anc in [p, *p.parents]:
        if anc.name.lower() == "common" and anc.parent.name.lower() == "steamapps":
            return anc
    return None


def _steam_install_dirs():
    """Steam 客户端自己的安装目录（可能不止一个来源）。"""
    dirs = []
    try:
        import winreg
    except ImportError:
        return dirs
    sources = ((winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam"),
               (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\Valve\Steam"),
               (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Valve\Steam"))
    for hive, key in sources:
        try:
            with winreg.OpenKey(hive, key) as k:
                for name in ("SteamPath", "InstallPath"):
                    try:
                        v, _ = winreg.QueryValueEx(k, name)
                    except OSError:
                        continue
                    if v:
                        dirs.append(Path(str(v).replace("/", "\\")))
        except OSError:
            continue
    return dirs


def _steam_common_roots():
    """读 libraryfolders.vdf，列出所有 Steam 库的 common 根。"""
    out = []
    for steam in _steam_install_dirs():
        vdf = steam / "steamapps" / "libraryfolders.vdf"
        if not vdf.is_file():
            continue
        try:
            text = vdf.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        for m in re.finditer(r'"path"\s+"([^"]+)"', text):
            lib = Path(m.group(1).replace("\\\\", "\\"))
            out.append(lib / "steamapps" / "common")
    return out


def common_roots():
    """本机的 Steam 库 common 根（去重，只留存在的）。"""
    roots = []
    r = find_common_root(current_mb2_path())
    if r is not None:
        roots.append(r)
    for r in _steam_common_roots():
        if r not in roots:
            roots.append(r)
    return [r for r in roots if r.is_dir()]


# ── 扫描客户端 ───────────────────────────────────────────────────────

def read_version(client_root):
    """从 Version.xml 现读版本号；读不到返回 None。"""
    vx = Path(client_root) / VERSION_XML
    if not vx.is_file():
        return None
    try:
        text = vx.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return None
    m = re.search(r'Value\s*=\s*"v?([0-9][0-9.]*)"', text)
    return m.group(1) if m else None


def _candidates_in(root):
    """某个库根下的候选客户端：先 Steam 主目录，再各版本备份。"""
    out = []
    main = root / MAIN_INSTALL
    if (main / VERSION_XML).is_file():
        out.append((main, True))
    bdir = root / BACKUP_DIR
    if bdir.is_dir():
        for sub in sorted(bdir.iterdir()):
            nested = sub / MAIN_INSTALL
            if (nested / VERSION_XML).is_file():
                out.append((nested, False))
            elif (sub / VERSION_XML).is_file():      # 备份目录本身就是客户端根
                out.append((sub, False))
    return out


def discover():
    """→ [Client, …]，按版本号从高到低。本机有几份就返回几个。"""
    found, seen = [], set()

    def add(path, is_steam, root):
        path = Path(path)
        key = str(path).lower()
        if key in seen:
            return
        ver = read_version(path)
        if not ver:
            return
        seen.add(key)
        found.append(Client(ver, path, is_steam, root))

    for root in common_roots():
        for path, is_steam in _candidates_in(root):
            add(path, is_steam, root)

    # 兜底：MB2_PATH 指向的那份一定算数 —— 哪怕目录名被改过、或不在标准库里
    cur = current_mb2_path()
    if cur and (Path(cur) / VERSION_XML).is_file():
        root = find_common_root(cur)
        # 直接躺在 common 下 = Steam 主目录那份；在 MB2_Version 底下 = 备份
        add(cur, root is not None and Path(cur).parent == root, root)

    return sorted(found, key=lambda c: (version_key(c.version), c.steam), reverse=True)


def version_key(v):
    """'1.5.5' → (1,5,5)，用来排序。非数字段落当 0。"""
    parts = []
    for p in str(v).split("."):
        parts.append(int(p) if p.isdigit() else 0)
    return tuple(parts)


def display_key(c):
    """给用户看的键名。

    🔴 Steam 主目录那份叫 `steam`，**不用版本号当键** —— 它的版本号是"当前读数"
       （官方一更新就变），拿它当标识早晚会写错。备份客户端不更新，用版本号当键。
    """
    return "steam" if c.steam else c.version


def by_version(clients=None):
    """→ {版本号: Client}。同一个版本装了多份时，Steam 主目录优先。"""
    table = {}
    for c in (clients if clients is not None else discover()):
        old = table.get(c.version)
        if old is None or (c.steam and not old.steam):
            table[c.version] = c
    return table


def describe(clients=None):
    """人看的清单（多行文本）。

    Steam 主目录那份显示成 `steam`，版本号只作为"当前读数"附注 —— 它会跟着官方更新变。
    """
    clients = clients if clients is not None else discover()
    if not clients:
        return ("没找到任何骑砍2客户端。\n"
                "  查过的地方：MB2_PATH 指向的库、以及 Steam libraryfolders.vdf 里列出的各库。\n"
                "  若游戏装在别处，先在注册表里设好 MB2_PATH 或用 Steam 启动一次游戏。")
    lines = []
    for c in clients:
        lines.append("  %-8s →  %s" % (display_key(c), c.path))
        if c.steam:
            lines.append("  %-8s    （Steam 主目录；当前 v%s，会跟着官方更新变 —— "
                         "所以写法用 `steam`，别写版本号）" % ("", c.version))
    return "\n".join(lines)


def keys_advice(clients=None):
    """给用户抄的"可用写法"：steam 打头，其余是备份客户端的版本号。"""
    clients = clients if clients is not None else discover()
    parts = []
    if any(c.steam for c in clients):
        parts.append("steam（主目录）")
    parts += sorted((c.version for c in clients if not c.steam),
                    key=version_key, reverse=True)
    return " / ".join(parts) if parts else "(没有可用的客户端)"


def main():
    for s in (sys.stdout, sys.stderr):
        if hasattr(s, "reconfigure"):
            s.reconfigure(encoding="utf-8", errors="replace")
    print("MB2_PATH (注册表现读) = %s" % (current_mb2_path() or "(未设置)"))
    print("Steam 库 common 根：")
    for r in common_roots():
        print("  %s" % r)
    print()
    print("本机骑砍2客户端：")
    print(describe())


if __name__ == "__main__":
    main()
