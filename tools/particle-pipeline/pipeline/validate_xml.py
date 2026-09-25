# -*- coding: utf-8 -*-
"""validate_xml.py -- 对生成的 particle XML 做结构化自检：
  * 是否 XML 合法
  * 每个 emitter 是否恰好 21 个 flag + 55 个 parameter（原版硬约束）
  * 材质名是否都在原版 prt_shd_* 白名单里（写错 = 静默不播）
"""
import sys, os, glob, xml.etree.ElementTree as ET
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.realpath(__file__))))
import paths          # 两根定位 TOOL/DATA —— 见工具链根 paths.py（realpath 穿透 junction）

# 🔴 材质白名单 = **原版粒子 XML 实际用过的 33 个**（`vanilla_prt_shd_materials.txt`）。
#    2026-09-25 实锤：以前用的是 `..._native.txt`（44 个，从引擎字符串池抓的），它**放过了
#    `prt_shd_lightning`** —— 那个名字在包索引里存在（`tpaccli dump` 能解析、还带贴图），
#    但**引擎的粒子材质表里没有它** ⇒ 编成资产后 ModKit 直接弹
#    `RGL CONTENT WARNING: Unable to find material{...} for particle effect lwn_ns_chainlightning::Llightning_0`。
#    判据：**"能 dump 出来" ≠ "能用"**；只有原版粒子真正引用过的材质才是被验证过能加载的。
WL = paths.out("vanilla_prt_shd_materials.txt")
def load_wl():
    try:
        return set(l.strip() for l in open(WL, encoding="utf-8") if l.strip())
    except Exception:
        return set()

def check(path, wl):
    bad = []
    try:
        root = ET.parse(path).getroot()
    except Exception as e:
        return ["XML 解析失败: %s" % e], 0, 0, 0
    ne = nflags = nparams = 0
    for eff in root.findall("effect"):
        for em in eff.findall("emitters/emitter"):
            ne += 1
            fl = em.find("flags"); pa = em.find("parameters")
            nf = len(fl.findall("flag")) if fl is not None else 0
            np_ = len(pa.findall("parameter")) if pa is not None else 0
            nflags += nf; nparams += np_
            if nf != 21: bad.append("emitter %s: flag=%d (应 21)" % (em.get("name"), nf))
            if np_ != 55: bad.append("emitter %s: param=%d (应 55)" % (em.get("name"), np_))
            for p in (pa.findall("parameter") if pa is not None else []):
                if p.get("name") == "material":
                    v = p.get("value")
                    if wl and v not in wl:
                        bad.append("emitter %s: material '%s' 不在原版白名单" % (em.get("name"), v))
    return bad, ne, nflags, nparams

def main():
    d = sys.argv[1] if len(sys.argv) > 1 else paths.out("xml")
    wl = load_wl()
    print("原版材质白名单: %d 条" % len(wl))
    files = sorted(glob.glob(os.path.join(d, "*.xml")))
    allbad = 0; tot_em = 0
    for f in files:
        bad, ne, nf, np_ = check(f, wl)
        tot_em += ne
        st = "OK " if not bad else "BAD"
        print("%s %-34s emitters=%-3d flags=%-4d params=%-4d" % (st, os.path.basename(f), ne, nf, np_))
        for b in bad[:6]: print("      ! " + b)
        allbad += len(bad)
    print("== 共 %d 文件 / %d emitter / 问题 %d 条 ==" % (len(files), tot_em, allbad))
    return 0 if allbad == 0 else 1

if __name__ == "__main__":
    sys.exit(main())
