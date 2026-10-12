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

# \ud83d\udd34 \u300c\u6570\u503c\u8bfb\u6570\u300d\u4e0d\u662f\u8def\u5f84\uff1a\u6b63\u5219\u4f1a\u628a `0.000/0.112/0.092`\u3001`24\u5e27/0.8s`\u3001`30.6/-130.2/153.5/153.4`
#    \u8fd9\u7c7b**\u91cf\u51fa\u6765\u7684\u6570\u5b57**\u4e5f\u5339\u914d\u6210 `\u76ee\u5f55/\u6587\u4ef6.\u6269\u5c55\u540d` \u7684\u5f62\u72b6\uff08\u672b\u6bb5 `0.092` \u770b\u7740\u5c31\u50cf `\u540d.\u540e\u7f00`\uff09\u3002
#    \u4ee5\u524d\u662f\u9010\u4e2a\u5f80 WHITELIST_SUB \u91cc\u52a0\uff08`2/0.8/0.95/1.7` \u5c31\u662f\u8fd9\u4e48\u6765\u7684\uff09\u2014\u2014 \u52a0\u4e00\u6761\u6f0f\u4e00\u6761\uff0c\u5fc5\u7136\u590d\u53d1\u3002
#    \u6539\u6210\u901a\u7528\u5224\u636e\uff1a**\u6bcf\u4e2a / \u5206\u6bb5\u90fd\u53ea\u662f\u300c\u6570\u5b57 + \u81f3\u591a\u4e24\u4e2a\u5355\u4f4d\u7684\u5b57\u300d\u21d2 \u5224\u4e3a\u8bfb\u6570\uff0c\u8df3\u8fc7**\u3002
#    \uff08\u771f\u8def\u5f84\u7684\u5206\u6bb5\u662f\u8bcd\uff0c\u4e0d\u4f1a\u5168\u662f\u6570\u5b57\uff1b`1.2.12DLL` \u8fd9\u79cd 3 \u4e2a\u5b57\u6bcd\u7684\u5355\u4f4d\u4e5f\u7167\u6837\u653e\u884c\u3002\uff09
_READING_SEG = re.compile(r'^[+\-]?\d+(?:\.\d+)?(?:[A-Za-z\u4e00-\u9fff]{0,2})?$')

def is_reading(tok):
    segs = tok.split('/')
    return len(segs) > 1 and all(_READING_SEG.match(s) for s in segs)

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
    # ── 已被有意删除 / 从未在本仓的东西（引用是历史记录，不是指路）──
    "work/bridge/",            # §0.16 姿态过渡桥的一次性工作目录，事后已清理
    "骑砍2动画重定向/skills/",  # §0.15 记录的那个本地技能：流程并进总纲后技能本体已删
    "4DHumans/",               # docs/图视频…：外部研究项目（HMR2.0），不在本仓也不在数据面
    "pose_dianxue_pair/assets", # 09-28 移除的实验数据集（同一个 GLB 装两条动画那份）
    "pose_dianxue_v8/assets",   # 同上，v8 那份
    "../../viewer/viewer.html", # 总纲 §5：这是在【描述展示页里该怎么写链接】（展示页在数据面
                                #   output/交付总览/ 下），不是本仓路径 —— 解析根够不着它
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
            if is_reading(tok):
                continue
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
