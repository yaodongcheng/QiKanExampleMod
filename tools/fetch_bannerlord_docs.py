#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
fetch_bannerlord_docs.py — 两个 Bannerlord modding 文档站的抓取生成器（重跑可刷新）

站点:
  A. docs.bannerlordmodding.lt   (社区站, MkDocs)  -> Knowledge/bannerlordmodding_lt/
  B. moddocs.bannerlord.com      (TaleWorlds 官方站, Docusaurus, GitHub 源) -> Knowledge/bannerlord_official_docs/

用法:
  python tools/fetch_bannerlord_docs.py all    # 抓全部
  python tools/fetch_bannerlord_docs.py lt     # 只抓 .lt 站
  python tools/fetch_bannerlord_docs.py official   # 只复制官方仓 docs/
  python tools/fetch_bannerlord_docs.py lt --limit 3 --dry  # 试跑只抓 3 页不写盘

生成物纪律（铁律22）: 本目录 md 为生成物，改内容 = 改此脚本重跑。
"""
import argparse
import json
import re
import subprocess
import sys
import tempfile
import urllib.request
from datetime import date
from pathlib import Path

try:
    from bs4 import BeautifulSoup
    from markdownify import markdownify as md
except ImportError as e:
    print(f"缺少依赖: {e} -> 运行: python -m pip install beautifulsoup4 markdownify")
    sys.exit(1)

ROOT = Path(__file__).resolve().parents[1]  # 项目根 LivingWorldNpcs/
KNOWLEDGE = ROOT / "Knowledge"

# ---------------------------------------------------------------------------
# A. docs.bannerlordmodding.lt (MkDocs + Material)
# ---------------------------------------------------------------------------
LT_BASE = "https://docs.bannerlordmodding.lt"
LT_INDEX = f"{LT_BASE}/search/search_index.json"
LT_OUT = KNOWLEDGE / "bannerlordmodding_lt"

# MkDocs Material 正文容器
CONTENT_SELECTOR = "article.md-content__inner"


def lt_fetch(dest: Path, max_pages: int | None = None):
    """从 search_index.json 拿页面清单 -> 逐页 HTML -> markdown。"""
    print(f"[lt] 下载 page list: {LT_INDEX}")
    with urllib.request.urlopen(LT_INDEX, timeout=60) as r:
        data = json.loads(r.read().decode("utf-8"))

    # 页面 = location 不含 '#'；内容按 MkDocs 结构每个二级节也有独立条目，
    # 但 HTML 里已有完整正文，所以我们只按页面下载 HTML。
    pages = {}
    for doc in data["docs"]:
        loc = doc["location"]
        if "#" in loc:
            continue
        pages[loc] = doc.get("title", "")

    print(f"[lt] 页面总数（去锚点）: {len(pages)}")
    if max_pages:
        pages = dict(list(pages.items())[:max_pages])

    import time

    ok, fail = 0, []
    for loc, title in pages.items():
        url = LT_BASE + ("" if loc == "" else "/" + loc.rstrip("/")) + "/"
        rel = "index" if loc == "" else loc.rstrip("/")
        parent = LT_OUT / Path(rel).parent
        out = parent / (rel.split("/")[-1] + ".md")
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
            with urllib.request.urlopen(req, timeout=60) as r:
                html = r.read().decode("utf-8", errors="replace")
            soup = BeautifulSoup(html, "html.parser")
            article = soup.select_one(CONTENT_SELECTOR)
            if article is None:
                fail.append((loc, "no article"))
                continue
            # 正文自带 H1，先剔除再由我们统一加（避免重复标题）
            h1_tag = article.find("h1")
            h1 = h1_tag.get_text(strip=True) if h1_tag else title
            if h1_tag:
                h1_tag.decompose()
            text = md(str(article), heading_style="ATX", strip=["script", "style"])
            text = re.sub(r"\n{3,}", "\n\n", text)
            # 站内资源路径 -> 绝对 URL（本地未下载图片，保留可点击链接）
            text = re.sub(r'\(/(pics/[^)]+)\)', lambda m: f"({LT_BASE}/{m.group(1)})", text)
            body = f"# {h1}\n\n<!-- 源: {url} | 抓取日期: {date.today()} -->\n\n{text.strip()}\n"
            parent.mkdir(parents=True, exist_ok=True)
            out.write_text(body, encoding="utf-8")
            ok += 1
            print(f"  [lt] {loc} -> {out.relative_to(ROOT)} ({len(body)}B)")
        except Exception as e:  # noqa: BLE001
            fail.append((loc, str(e)))
        time.sleep(0.15)
    print(f"[lt] 完成: {ok} 成功, {len(fail)} 失败")
    for loc, err in fail:
        print(f"  [lt FAIL] {loc}: {err}")


# ---------------------------------------------------------------------------
# B. moddocs.bannerlord.com — 源仓库 https://github.com/TaleWorlds/Documentations
# ---------------------------------------------------------------------------
OFFICIAL_REPO = "https://github.com/TaleWorlds/Documentations"
OFFICIAL_OUT = KNOWLEDGE / "bannerlord_official_docs"


def official_fetch(lang: str = "english"):
    # 方案: 部分克隆只拿树清单(轻) -> 正文逐个走 raw.githubusercontent.com 下载(稳, 带重试)
    # 不 checkout(partial clone 懒加载 blob 偶发掉包: "unable to read sha1 file")
    with tempfile.TemporaryDirectory(prefix="lwn_docs_") as tmp:
        clone_dir = Path(tmp) / "repo"
        print(f"[official] git clone --depth 1 --filter=blob:none --no-checkout {OFFICIAL_REPO}")
        r = subprocess.run(
            ["git", "clone", "--depth", "1", "--filter=blob:none",
             "--no-checkout", OFFICIAL_REPO, str(clone_dir)],
            capture_output=True, text=True,
        )
        if r.returncode != 0:
            print(f"[official] clone 失败: {r.stderr}")
            sys.exit(1)

        prefix = f"docs/content/{lang}/"
        r = subprocess.run(
            ["git", "-C", str(clone_dir), "ls-tree", "-r", "--name-only", "HEAD"],
            capture_output=True, text=True,
        )
        if r.returncode != 0:
            print(f"[official] ls-tree 失败: {r.stderr}")
            sys.exit(1)
        paths = [l for l in r.stdout.splitlines()
                 if l.startswith(prefix) and l.endswith(".md")]
        if not paths:
            print(f"[official] {prefix} 下没有 md; 可选项见仓库 docs/content/")
            sys.exit(1)

        import time
        from urllib.parse import quote

        ok, fail = 0, []
        for rel in paths:
            out = OFFICIAL_OUT / rel.removeprefix(prefix)
            out.parent.mkdir(parents=True, exist_ok=True)
            url = f"https://raw.githubusercontent.com/TaleWorlds/Documentations/master/{quote(rel)}"
            for attempt in range(1, 4):
                try:
                    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
                    with urllib.request.urlopen(req, timeout=60) as resp:
                        data = resp.read()
                    out.write_bytes(data)
                    ok += 1
                    print(f"  [official] {rel.removeprefix(prefix)} ({len(data)}B)")
                    break
                except Exception as e:  # noqa: BLE001
                    if attempt == 3:
                        fail.append((rel, str(e)))
                    else:
                        time.sleep(1 * attempt)
            time.sleep(0.05)
        print(f"[official] 完成: {ok} 成功, {len(fail)} 失败 (lang={lang})")
        for rel, err in fail:
            print(f"  [official FAIL] {rel}: {err}")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("target", choices=["all", "lt", "official"])
    ap.add_argument("--limit", type=int, default=None, help="lt: 只抓前 N 页（试跑用）")
    ap.add_argument("--lang", default="english", choices=["english", "schinese", "russian"],
                    help="official: 语言版本（源仓 docs/content/<lang>）")
    args = ap.parse_args()

    if args.target in ("all", "lt"):
        lt_fetch(LT_OUT, args.limit)
    if args.target in ("all", "official"):
        official_fetch(args.lang)
