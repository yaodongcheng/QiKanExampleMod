#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""SubModule 时代段注册迁移器（2 代 → 6 代）
============================================================================
**做什么**：把 `Taikou/SubModule.xml` 的段注册从「2 个时代（1560 / 1582）」扩到 **6 个时代**
（1554 / 1560 / 1568 / 1575 / 1582 / 1598）。两类节点分别处理：

  ① **共用段**（列了多个 GameType 的节点）→ 补上 4 个新 GameType
     （判据 = 该节点 `<GameType>` 条目 **>1 个**；独占段只有 1 个 → 不动它）
  ② **独占段**（每个时代一份、GameType 互斥）→ 为 4 个新年代**新增节点**：
     `Kingdoms`(spkingdoms_<年>) · `Factions`(spclans_<年>) · `Settlements`(settlements_<年>)
     · `Heroes`(taikou_heroes_<年>) · `NPCCharacters`(taikou_lords_<年> —— 领主模板按代切段)

🔴 为什么领主模板也要按代切：模板上的 `age`/装备是**按该代算的**（1560 的秀吉 24 岁、
1598 的秀吉 62 岁），共用一份 = 五个年代年龄全错。

🔴 互斥纪律（`check_era_segments.py` 常驻守）：同名 XmlName 注册多份时引擎是**合并加载**，
   两套段同时命中同一 GameType = 同名对象定义两遍。

Usage:
  python Scripts/migrate_era_registration.py --check     # 只报要不要改（幂等：改完再跑应无差异）
  python Scripts/migrate_era_registration.py             # 写入（先备份到同目录 .bak）
Exit: 0 成功/已最新 / 1 --check 发现待改 / 2 fatal。
"""
import argparse
import io
import re
import shutil
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

DEFAULT_SUBMODULE = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12"
                     r"\Mount & Blade II Bannerlord\Modules\Taikou\SubModule.xml")

NEW_ERAS = ["1554", "1568", "1575", "1598"]
ALL_ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]
BASE_ERAS = ["Campaign", "CampaignStoryMode", "TaikouCampaign"]
# (XmlName id, 文件名模板——1560 用无后缀)
EXCLUSIVE = [("Kingdoms", "spkingdoms_%s"), ("Factions", "spclans_%s"),
             ("Settlements", "settlements_%s"), ("Heroes", "taikou_heroes_%s"),
             ("NPCCharacters", "taikou_lords_%s")]
# 🔴 **按年代独占的段路径**（每个时代一份）：这些 path 的节点**不能**被当成共用段补 GameType。
#    2026-09-12 实测踩坑：判据若用「GameType 条目数 > 1」会把**基数段**也算成共用段——
#    基数段（spclans/spkingdoms/settlements/taikou_heroes）为了兼容 `Campaign`/`TaikouCampaign`
#    本身就列了 4 个 GameType（含 TaikouCampaign1560）→ 补完立刻与新时代段撞车
#    （check_era_segments 一次报 8 条互斥冲突）。
ERA_OWNED_PATHS = ("spkingdoms", "spclans", "settlements", "taikou_heroes", "taikou_lords")
# 基数段（1560）该服务的 GameType —— 沿用现网写法（兼容原版入口 + 别名）
BASE_NODE_TYPES = ["Campaign", "CampaignStoryMode", "TaikouCampaign", "TaikouCampaign1560"]


def path_for(tmpl, era):
    return tmpl % era if era != "1560" else tmpl % ""


def is_era_owned(path):
    """该段路径是不是「每代一份」的（含 1560 的无后缀写法）。"""
    stem = path.split("_")[0] if not path.startswith("taikou_lords") else "taikou_lords"
    return any(path == p or path.startswith(p + "_") for p in ERA_OWNED_PATHS)


def node_block(xml_id, path, game_type):
    return ('\t\t<XmlNode>\n'
            '\t\t\t<XmlName id="%s" path="%s"/>\n'
            '\t\t\t<IncludedGameTypes>\n'
            '\t\t\t\t<GameType value="%s"/>\n'
            '\t\t\t</IncludedGameTypes>\n'
            '\t\t</XmlNode>\n' % (xml_id, path, game_type))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--submodule", default=DEFAULT_SUBMODULE)
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()

    raw = io.open(args.submodule, "rb").read()
    text = raw.decode("utf-8-sig")
    eol = "\r\n" if "\r\n" in text else "\n"
    flat = text.replace("\r\n", "\n")

    # ── ① 共用段：补 4 个新 GameType（**跳过按年代独占的段**）──
    out, added_gt, n_shared, skipped_owned = [], 0, 0, 0
    i = 0
    lines = flat.split("\n")
    while i < len(lines):
        line = lines[i]
        # 找本 XmlNode 的 path —— 🔴 必须取窗口里**最后一个** XmlName（最近的那个）：
        #    用 re.search（第一个）会在「上一个节点的 XmlName 也在 10 行窗口内」时认错节点
        #    （2026-09-12 实测：spworkshops / taikou_bodyproperties / taikou_location_complex_templates
        #     三个共用段被误判成独占段而漏补 → check_module_registration 报「缺段」）。
        ctx = "\n".join(lines[max(0, i - 10):i + 1])
        pms = re.findall(r'<XmlName id="[^"]*" path="([^"]+)"', ctx)
        owned = bool(pms) and is_era_owned(pms[-1])
        out.append(line)
        if "<IncludedGameTypes>" in line:
            # 收集本块的 GameType
            j = i + 1
            body = []
            while j < len(lines) and "</IncludedGameTypes>" not in lines[j]:
                body.append(lines[j])
                j += 1
            vals = [re.search(r'value="([^"]+)"', b).group(1) for b in body
                    if "<GameType" in b and re.search(r'value="([^"]+)"', b)]
            if owned:
                skipped_owned += 1                 # 按年代独占 → 不补（补了就与时代段撞车）
            elif len(vals) > 1:
                n_shared += 1
                indent = re.match(r"\s*", body[0]).group(0) if body else "\t\t\t\t"
                for e in NEW_ERAS:
                    gt = "TaikouCampaign" + e
                    if gt not in vals:
                        body.append('%s<GameType value="%s"/>' % (indent, gt))
                        added_gt += 1
            out.extend(body)
            i = j
            continue
        i += 1
    text = "\n".join(out)

    # ── ② 独占段：为 4 个新年代新增节点 ──
    new_nodes, added_nodes = [], 0
    for era in NEW_ERAS:
        anchor = "</Xmls>" if "<Xmls>" in text else None
        for xml_id, tmpl in EXCLUSIVE:
            gt = "TaikouCampaign" + era
            # 幂等判据用**文件路径**而不是 GameType 值——共用段补完 GameType 后，
            # 值一定已存在，拿值判会把「独占段还没建」误判成已注册（实测踩过）。
            if 'path="%s"' % path_for(tmpl, era) in text:
                continue
            new_nodes.append((xml_id, path_for(tmpl, era), gt))
            added_nodes += 1

    if not new_nodes and not added_gt:
        print("✅ 已是最新（6 代注册齐；共用段 %d 个、独占段无缺）" % n_shared)
        return 0
    if args.check:
        print("❌ 需要迁移：共用段补 %d 条 GameType · 新增独占段节点 %d 个" % (added_gt, added_nodes))
        for x in new_nodes[:8]:
            print("      + %s ← %s（%s）" % (x[0], x[1], x[2]))
        return 1

    blocks = ""
    # ③ 基数段补登记：`taikou_lords`（1560 那份）与 `taikou_lords_1582` 原先谁都没注册
    #    （1560/1582 的领主模板段是本次新引入的文件族）→ 不补 = 该代英雄全都没有模板。
    if 'path="taikou_lords"' not in text:
        blocks += ("\t\t<!-- 领主模板（1560 基数段）：与 spnpccharacters 分开存，因为模板要按代算年龄/装备 -->\n"
                   "\t\t<XmlNode>\n\t\t\t<XmlName id=\"NPCCharacters\" path=\"taikou_lords\"/>\n"
                   "\t\t\t<IncludedGameTypes>\n"
                   + "".join('\t\t\t\t<GameType value="%s"/>\n' % g for g in BASE_NODE_TYPES)
                   + "\t\t\t</IncludedGameTypes>\n\t\t</XmlNode>\n")
        added_nodes += 1
    if 'path="taikou_lords_1582"' not in text:
        blocks += node_block("NPCCharacters", "taikou_lords_1582", "TaikouCampaign1582")
        added_nodes += 1
    for era in NEW_ERAS:
        blocks += ("\t\t<!-- %s 时代：与 1560 套**互斥**（同名 XmlName 多段 = 引擎合并加载） -->\n" % era)
        for xml_id, tmpl in EXCLUSIVE:
            blocks += node_block(xml_id, path_for(tmpl, era), "TaikouCampaign" + era)
    idx = text.rfind("</Xmls>")
    if idx < 0:
        print("[FATAL] 找不到 </Xmls>", file=sys.stderr)
        return 2
    text = text[:idx] + blocks + text[idx:]
    shutil.copy2(args.submodule, args.submodule + ".bak")
    io.open(args.submodule, "wb").write(
        text.replace("\n", eol).encode("utf-8-sig"))
    print("✅ 迁移完成：共用段补 %d 条 GameType（%d 个节点）· 新增独占段节点 %d 个"
          % (added_gt, n_shared, added_nodes))
    print("   备份：%s.bak" % args.submodule)
    return 0


if __name__ == "__main__":
    sys.exit(main())
