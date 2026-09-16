#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
家纹体检的负面测试（雷 130 / 131 · 配套 `Scripts/check_taikou_banner_icons.py`）
========================================================================
纪律：检查器写完**必须**做负面测试——故意造坏数据，确认它真能抓到（红 + exit 1）。
本脚本在临时目录里搭一个最小内容包（banner_icons.xml + spclans.xml + 假 tpac），
逐个注入缺陷，断言对应检查项变红；最后留一个**正向对照**（好数据必须绿）。

Usage:
  python Scripts/test_negative_banner_icons.py
Exit: 0 全部符合预期 / 1 有不符合
"""
import os
import shutil
import subprocess
import sys
import tempfile

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))
CHECKER = os.path.join(HERE, "check_taikou_banner_icons.py")

# ── 最小夹具 ───────────────────────────────────────────────────────────────
GOOD_ICONS = """<?xml version="1.0" encoding="utf-8"?>
<base xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema"
\ttype="string">
\t<BannerIconData>
\t\t<!-- 生成物·禁止手改：注释必须在 BannerIconData **里面**（雷 130） -->
\t\t<BannerIconGroup
\t\t\tid="9"
\t\t\tname="{=TAIKOU_banner_group_mon}Taikou Family Crests"
\t\t\tis_pattern="false">
\t\t\t<Icon
\t\t\t\tid="840"
\t\t\t\tmaterial_name="taikou_cl_mon_kinki_1"
\t\t\t\ttexture_index="0" />
\t\t\t<Icon
\t\t\t\tid="841"
\t\t\t\tmaterial_name="taikou_cl_mon_kinki_1"
\t\t\t\ttexture_index="1" />
\t\t</BannerIconGroup>
\t</BannerIconData>
</base>
"""

GOOD_CLANS = """<?xml version="1.0" encoding="utf-8"?>
<Factions>
\t<Faction id="clan_a_1" banner_key="11.146.146.1536.1536.764.764.1.0.0.840.171.171.483.483.764.764.0.0.0"/>
\t<Faction id="clan_b_1" banner_key="11.146.146.1536.1536.764.764.1.0.0.841.171.171.483.483.764.764.0.0.0"/>
\t<Faction id="clan_c_1" banner_key="11.146.146.1536.1536.764.764.1.0.0"/>
</Factions>
"""


def build(root, icons=GOOD_ICONS, clans=GOOD_CLANS, with_material=True):
    md = os.path.join(root, "ModuleData")
    ap = os.path.join(root, "AssetPackages")
    os.makedirs(md, exist_ok=True)
    os.makedirs(ap, exist_ok=True)
    open(os.path.join(md, "banner_icons.xml"), "w", encoding="utf-8", newline="").write(icons)
    open(os.path.join(md, "spclans.xml"), "w", encoding="utf-8", newline="").write(clans)
    # 假 tpac：检查器只做二进制 grep，所以把材质名字节塞进去就够
    blob = b"fake tpac\x00"
    if with_material:
        blob += b"taikou_cl_mon_kinki_1\x00"
    open(os.path.join(ap, "fake.tpac"), "wb").write(blob)


def run(root):
    r = subprocess.run([sys.executable, CHECKER, "--module", root],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    return r.returncode, (r.stdout or "") + (r.stderr or "")


def main():
    cases = []

    # 正向对照
    cases.append(("好数据（正向对照）", {}, 0, None))
    # 雷 130：注释错位两种
    cases.append(("注释夹在 XML 声明与 <base> 之间",
                  {"icons": GOOD_ICONS.replace(
                      '?>\n<base', '?>\n<!-- oops -->\n<base')}, 1, "结构"))
    cases.append(("注释夹在 <base> 与 <BannerIconData> 之间",
                  {"icons": GOOD_ICONS.replace(
                      '\t<BannerIconData>\n', '\t<!-- x -->\n\t<BannerIconData>\n')}, 1, "结构"))
    # 段位
    cases.append(("图标 id 重复", {"icons": GOOD_ICONS.replace('id="841"', 'id="840"')}, 1, "段位"))
    cases.append(("图标 id 侵入原版段（<840）",
                  {"icons": GOOD_ICONS.replace('id="840"', 'id="100"')}, 1, "段位"))
    # 材质
    cases.append(("材质不在自家 tpac 里", {"with_material": False}, 1, "材质"))
    # 几何（雷 131）
    cases.append(("两套不同几何",
                  {"clans": GOOD_CLANS.replace(
                      'clan_b_1" banner_key="11.146.146.1536.1536.764.764.1.0.0.841.171.171.483.483.764.764',
                      'clan_b_1" banner_key="11.146.146.4922.4922.764.764.1.0.0.841.171.171.471.471.764.687')},
                  1, "几何"))
    cases.append(("banner_key 引用未定义图标 id",
                  {"clans": GOOD_CLANS.replace('.840.', '.999.')}, 1, "几何"))

    bad = 0
    for name, kw, want_code, want_word in cases:
        root = tempfile.mkdtemp(prefix="lwn_neg_banner_")
        try:
            build(root, **kw)
            code, out = run(root)
            ok = (code == want_code) and (want_word is None or want_word in out)
            print("%s %-34s exit=%s%s" % ("[OK] " if ok else "[FAIL]", name, code,
                                          "" if ok else "  期望 exit=%s 且提到「%s」" % (want_code, want_word)))
            if not ok:
                bad += 1
                print("      —— 输出尾部 ——")
                for line in [l for l in out.splitlines() if l.strip()][-6:]:
                    print("      " + line)
        finally:
            shutil.rmtree(root, ignore_errors=True)

    print("\n结果：%s（%d/%d 符合预期）" % ("绿" if bad == 0 else "红", len(cases) - bad, len(cases)))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
