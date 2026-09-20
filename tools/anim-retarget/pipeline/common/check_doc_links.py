#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""文档引用校验器：扫描所有 .md，检查里面出现的「仓内路径」是否真实存在。
   用途：目录重组之后跑一次，防止文档变成「照着走扑空」。
   用法： python pipeline/common/check_doc_links.py          # 只查活跃文档
         python pipeline/common/check_doc_links.py --all    # 连 _legacy/** 一起查
   退出码：0 = 无失效引用；1 = 有失效引用（可直接接入 CI/交接前自检）
"""
import os, re, sys

# 中文 Windows 控制台默认 GBK，打印 ✅/❌ 会 UnicodeEncodeError 崩掉 —— 强制 UTF-8
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# 🔴 代码与数据现在分两处，文档里的相对路径三种写法都有，所以三个根都要试：
#     · 工具根 tools/anim-retarget/    —— docs/、pipeline/…（本文档自己）
#     · 仓库根 Modules/LivingWorldNpcs/ —— plans/…、Knowledge/…、tools/OpenTrf/…
#     · 数据根 D:/BrainMaker/骑砍2动画重定向/ —— input/…、output/…、viewer/…、UEAnims/…
#    （拆分前整个工程是一个根，老检查器只试那一个；拆完不试多根就会满屏假失败。）
#    ⚠️ 路径刻意用【普通字符串 + 正斜杠】：raw 字符串 + 反斜杠 + 中文 三者凑一起时，
#       中文一旦被某个环节转义成字面 \uXXXX 就再也解析不出来了（2026-09-20 踩过）。
DATA_ROOT = "D:/BrainMaker/骑砍2动画重定向"
_REPO_ROOT = os.path.dirname(os.path.dirname(ROOT))
ROOTS = [ROOT]
if os.path.isdir(os.path.join(_REPO_ROOT, "tools")):
    ROOTS.append(_REPO_ROOT)
if os.path.isdir(DATA_ROOT):
    ROOTS.append(DATA_ROOT)

PAT = re.compile(r'(?<![\w/])((?:\.\./|\./)?(?:[A-Za-z0-9_\u4e00-\u9fff\.\-]+/)+[A-Za-z0-9_\u4e00-\u9fff\.\-]+\.[A-Za-z0-9]{1,6})')

# 白名单：非「仓内相对路径」的串（外部工具路径 / 被正则截断 / 文档占位示例 / 已失效的历史引用）
WHITELIST_SUB = [
    ".uproje",                 # 正则把 .uproject 截断了
    "Engine/Binaries",         # D:/UNREAL/UE_5.0/Engine/... 这类绝对路径的碎片
    "blender.exe",
    "EmAssetPackages/",        # Bannerlord ModKit 内部路径，不在本仓
    "modding_resources/",
    "1.2G",                    # 容量描述
    "viewer.html/serve.py",
    "xxx.", "yyy",             # 文档里的占位示例
    # ── 项目总纲.md（整个工程的总纲，含大量外部/历史路径）──
    "2/0.8/0.95/1.7",          # §8.5 里「别用试数值代替算」举的撞数值例子，被当成路径
    "mySekiro/UE5.3",          # UE 工程标识（外部）
    "config/external.json",    # T12 待办里「建议新增」的文件，尚未存在
    "fbx_to_trf_fixed.py",     # 已于 2026-09-20 并入 anim-retarget，各处只作历史提及
    "ue_pair7.glb",            # §10 事故记录：该文件已被脚本覆盖损毁，本就不存在
]

# 豁免文件：这些文档按设计就会包含「已失效的旧路径」
#   docs/路径索引.md —— 它本身就是「旧→新」对照表
SKIP_FILES = {"docs/路径索引.md"}

def tokens(text):
    return set(PAT.findall(text))

def collect(scan_all):
    docs = []
    for root, dirs, files in os.walk(ROOT):
        dirs[:] = [d for d in dirs if d not in ("node_modules", ".git") and not d.startswith("path_fix_backup")]
        rel = os.path.relpath(root, ROOT).replace(os.sep, "/")
        if not scan_all and (rel.startswith("_legacy") or "/_legacy/" in rel):
            continue
        for f in files:
            if f.lower().endswith(".md"):
                docs.append(os.path.join(root, f))
    return sorted(docs)

def main():
    scan_all = "--all" in sys.argv
    docs = collect(scan_all)
    bad = {}
    for p in docs:
        rel_doc = os.path.relpath(p, ROOT).replace(os.sep, "/")
        if rel_doc in SKIP_FILES:
            continue
        text = open(p, encoding="utf-8", errors="replace").read()
        for tok in sorted(tokens(text)):
            if any(w in tok for w in WHITELIST_SUB):
                continue
            if any(os.path.exists(os.path.join(r, tok)) for r in ROOTS):      # 相对【任一】根
                continue
            if os.path.exists(os.path.normpath(os.path.join(os.path.dirname(p), tok))):  # 相对本文档
                continue
            bad.setdefault(tok, []).append(rel_doc)
    print("扫描文档 %d 个（%s）" % (len(docs), "含 _legacy" if scan_all else "仅活跃文档"))
    print("  解析根：%s" % "  |  ".join(ROOTS))
    if not bad:
        print("✅ 未发现失效引用")
        return 0
    print("❌ 发现 %d 个失效引用：\n" % len(bad))
    for tok, ds in sorted(bad.items()):
        print("  %-58s ← %s" % (tok, ", ".join(ds[:3]) + (" …" if len(ds) > 3 else "")))
    return 1

sys.exit(main())
