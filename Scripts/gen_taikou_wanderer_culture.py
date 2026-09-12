#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""英雄身份文化 —— 游荡者 → `ronin`；大航海联动 → `pirate`；南蛮 → `namban`
============================================================================
> 文件名保留 `wanderer_culture`（历史），**实际覆盖三条规则**；`run_all_checks` 里的描述以规则为准。

**规则一：游荡者 → `ronin`**（用户裁定 2026-09-12「浪人给游荡者」）
  口径：**六代全程无家**（`ClanID_1554…1598` 六格全空）= 游荡者。
  依据：家族按侍奉关系重建后「无家」= 浪人/无所属 = 骑砍侧的**游荡者**（酒馆可招募），见 plan 0.2。
  ⚠️ 「无家」是**逐年代**属性而 `CultureID` 是**单列** → 只取「六代全空」这一档；中途无家的人不动。

**规则二：大航海时代联动角色 → `pirate`**（用户裁定 2026-09-12）
  这批是 DX 追加的联动角色（海上的汉子们），文化统一给**海贼**。
  名单与证据（每条都能复核，别再加没证据的）：
    · `lord_tk5_1160` 海雷丁 —— 别名 `ハイレディン`；**游戏自己就把他挂在海贼众（组织=安东水军）** ✓
    · `lord_tk5_1161` 佐伯杏太郎 —— 用户点名
    · `lord_tk5_1172` 里璐 —— 项目《未识别女性_史实提案》记为「大航海时代联动（リル）」
    · `lord_tk5_1173` 拉斐耶鲁 —— `EnglishName=Raphael`
    · `lord_tk5_1242` 李华梅 —— 别名 `マリア`（＝マリア・ホアメイ）；用户点名
    · `lord_tk5_1243` 蒂雅
  ⚠️ **不在此列**（史实南蛮人，不是联动角色）：`佛罗伊斯`（Frois，耶稣会士）/ `阿鲁梅达`（Almeida）。

**改数据一律改本脚本再重跑**（铁律 22）。只动 `CultureID` 一列。
⚠️ 规则一依赖 `ClanID_<年>` → **必须在 `gen_taikou_clan_csv.py --apply` 之后跑**。

Usage:
  python Scripts/gen_taikou_wanderer_culture.py            # 报告（不写盘）
  python Scripts/gen_taikou_wanderer_culture.py --apply    # 落盘
  python Scripts/gen_taikou_wanderer_culture.py --check    # 进一键体检
Exit: 0 正常 / 1 有硬问题 / 2 fatal。
"""
import argparse
import csv
import io
import os
import shutil
import sys
import time

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from csv_dual import read_table  # noqa: E402

CSV_DIR = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")
HERO = os.path.join(CSV_DIR, "TaikouHero.csv")
SETT = os.path.join(CSV_DIR, "Settlements.csv")

ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]
RONIN, PIRATE = "ronin", "pirate"

# 🔴 规则四（2026-09-12 用户裁定「全员按驻在城改」）：
#    英雄文化 = 该英雄**驻在城**（`City_<年>`，取最后一个查得到的年代）的「地」→ 地域文化；
#    驻在城查不到 → 退回**据点归属**（他是城主的城，`Settlements.csv` 的 `Owner_<年>`）。
#    为什么覆盖身份文化：武士被分封到哪儿就是哪儿的人（属地口径 = 引擎文化语义，管兵种/名字池/城镇 NPC）；
#    唯独两条**点名裁定**不参与覆盖：大航海联动（→ pirate）、南蛮（→ namban）。
CHI2CULT = {
    "九州": "saikai", "四国": "nankai", "中部": "sanyo", "近畿": "kinai",
    "东北": "ou", "关东": "kanto", "东海": "tokai", "北陆": "hokuriku", "甲信": "tosan",
}

_CACHE = {}


def name_culture_map():
    """{(年, 城名): 地域文化} —— 从 `Settlements.csv` 的 `Name_<年>` + `Chi`（同年代的名字对同年代的城）。"""
    if "name" in _CACHE:
        return _CACHE["name"]
    m = {}
    if os.path.isfile(SETT):
        cn, en, raw = read_table(SETT, head=2)
        ci = en.index("Chi") if "Chi" in en else None
        if ci is not None:
            for r in raw:
                c = CHI2CULT.get((r[ci] or "").strip() if len(r) > ci else "")
                if not c:
                    continue
                for e in ERAS:
                    k = "Name_" + e
                    if k in en:
                        i = en.index(k)
                        nm = (r[i] or "").strip() if len(r) > i else ""
                        if nm:
                            m.setdefault((e, nm), c)
    _CACHE["name"] = m
    return m


def owner_culture_map():
    """{英雄id: 地域文化} —— **据点归属**口径（`Owner_<年>`，取最后当城主的年代）；兜底用。"""
    if "owner" in _CACHE:
        return _CACHE["owner"]
    out = {}
    if os.path.isfile(SETT):
        cn, en, raw = read_table(SETT, head=2)
        oi = {e: en.index("Owner_" + e) for e in ERAS if "Owner_" + e in en}
        ci = en.index("Chi") if "Chi" in en else None
        if ci is not None:
            for r in raw:
                c = CHI2CULT.get((r[ci] or "").strip() if len(r) > ci else "")
                if not c:
                    continue
                for e in ERAS:                    # 时间顺序 → 后者覆盖前者 = 最后当城主的年代
                    i = oi.get(e)
                    if i is not None and len(r) > i and (r[i] or "").strip():
                        out[(r[i] or "").strip()] = c
    _CACHE["owner"] = out
    return out


def hero_culture_map():
    """{英雄id: 地域文化}：**驻在城优先**（`City_<年>`，取最后一个查得到的年代），据点归属兜底。"""
    if "hero" in _CACHE:
        return _CACHE["hero"]
    m = dict(owner_culture_map())
    n2c = name_culture_map()
    with io.open(HERO, encoding="utf-8-sig", newline="") as fh:
        rows = list(csv.reader(fh))
    hdr = rows[1]
    ii = hdr.index("ID")
    cix = {e: hdr.index("City_" + e) for e in ERAS if "City_" + e in hdr}
    for r in rows[2:]:
        if len(r) <= ii or not (r[ii] or "").strip():
            continue
        c = None
        for e in ERAS:                            # 时间顺序 → 最后查得到的年代
            i = cix.get(e)
            nm = (r[i] or "").strip() if i is not None and len(r) > i else ""
            x = n2c.get((e, nm))
            if x:
                c = x
        if c:
            m[r[ii].strip()] = c
    _CACHE["hero"] = m
    return m

# 规则二：大航海时代联动角色（证据见文件头）
COLLAB = {
    "lord_tk5_1160": "海雷丁（别名 ハイレディン；游戏里挂海贼众·安东水军）",
    "lord_tk5_1161": "佐伯杏太郎（用户点名）",
    "lord_tk5_1172": "里璐（项目史料提案记为「大航海时代联动（リル）」）",
    "lord_tk5_1173": "拉斐耶鲁（EnglishName=Raphael）",
    "lord_tk5_1242": "李华梅（别名 マリア＝マリア・ホアメイ；用户点名）",
    "lord_tk5_1243": "蒂雅（用户点名）",
}


# 规则三：南蛮（西洋/外国人）—— 用户裁定 2026-09-12「标记南蛮」
#   ⚠️ 只收**外国人**；天正遣欧使節的日本人基督徒（千千石米格尔/伊东满所/内田托马）**不在此列**。
NANBAN = {
    "lord_tk5_1170": "佛罗伊斯（Frois，葡萄牙耶稣会士）",
    "lord_tk5_1171": "阿鲁梅达（Almeida，葡萄牙商人/传教士）",
    "lord_tk5_958": "弥助（非洲出身，信长近臣；⚠️ 非欧洲人，用户如另有安排可摘）",
    "lord_tk5_959": "三浦按针（William Adams，英国航海士）",
}


def is_wanderer(r):
    """六代全程无家 = 游荡者。"""
    return all(not (r.get("ClanID_" + e) or "").strip() for e in ERAS)


# 🔴 非人物行（模板/变量）的文化（2026-09-12 用户裁定）：
#   「忍者、海贼、商人等**身份明确**的按身份给，**其余一律 `neutral_culture`**」。
#   只列前 3 类 + 浪人；表里没有的模板与全部变量行 → neutral_culture（含「贼」——用户没点名，
#   如需算盗匪文化（looters），把 template_yotto 加进来即可）。
TEMPLATE_IDENT = (
    ("template_ninja", "ninja"), ("template_jounin", "ninja"), ("template_chunin", "ninja"),
    ("template_genin", "ninja"), ("template_kunoichi", "ninja"),
    ("template_kashira", "ninja"),            # 头目 = 忍者众当主（本表 IDENT 口径）
    ("template_kaizoku", "pirate"),           # 海贼/女海贼/头领
    ("template_funa_daisho", "pirate"), ("template_sentou", "pirate"), ("template_suifu", "pirate"),
    ("template_merchant", "trader"),          # 商人/行商人/明朝·朝鲜·琉球商人
    ("template_za_shihai", "trader"), ("template_detchi", "trader"), ("template_tenpo", "trader"),
    ("template_umaya_tenpo", "trader"), ("template_komeya_tenpo", "trader"),
    ("template_sakaba_onna", "trader"), ("template_yadoya_onna", "trader"),
    ("template_ryotei_onna", "trader"), ("template_toudou", "trader"),   # 当家 = 商家当主
    ("template_ronin", "ronin"),
)


def template_culture(tid):
    for pre, c in TEMPLATE_IDENT:
        if tid.startswith(pre):
            return c
    return "neutral_culture"


def is_person(rec):
    """人物行判据（与 check_taikou_world_tables 一致）：`模板NPC` 非空 = 非人物行
    （通用 NPC 模板 `template_*` / 变量行 `pronoun_*`）。"""
    return not (rec.get("TemplateNPC") or "").strip()


def want_culture(rec):
    """本脚本对某英雄的期望 CultureID；None = 不管（保持原值）。

    优先级（2026-09-12 用户裁定「全员按驻在城改」）：
      ① 非人物行（模板/变量行）→ 身份模板文化 / neutral_culture
      ② 两条点名裁定不参与覆盖：大航海联动 → pirate；南蛮 → namban
      ③ **驻在城/据点文化** → `hero_culture_map()`（主规则，覆盖身份文化）
      ④ 六代无家（游荡者）→ ronin
      ⑤ 其余 → None（保持）
    """
    if not is_person(rec):
        # 非人物行：身份明确的模板给身份文化，其余（含全部变量行）→ neutral_culture
        return template_culture(rec.get("ID", ""))
    if rec.get("ID") in COLLAB:
        return PIRATE
    if rec.get("ID") in NANBAN:
        return "namban"
    lc = hero_culture_map().get(rec.get("ID"))
    if lc:
        return lc
    if is_wanderer(rec):
        return RONIN
    return None


def build():
    with io.open(HERO, encoding="utf-8-sig", newline="") as fh:
        rows = list(csv.reader(fh))
    # TaikouHero.csv 双行表头（中文行 + 英文行）→ 键取第 2 行、数据从第 3 行起
    cn_hdr, hdr = rows[0], rows[1]
    body = [r for r in rows[2:] if r and any(x.strip() for x in r)]
    ic, ik = hdr.index("CultureID"), hdr.index("ID")
    hmap = hero_culture_map()
    wand, lords, changes = [], [], []
    for r in body:
        rec = dict(zip(hdr, r))
        want = want_culture(rec)
        if want is None:
            continue
        hid = r[ik]
        if want == hmap.get(hid):
            lords.append(hid)                      # 规则四命中（驻地/据点文化）
        elif want == RONIN:
            wand.append(hid)                       # 规则一命中（游荡者）
        if r[ic].strip() != want:
            changes.append((hid, rec.get("CNName", ""), r[ic].strip(), want))
    return {"cn_hdr": cn_hdr, "hdr": hdr, "body": body, "idx": ic,
            "wand": wand, "lords": lords, "changes": changes}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--module", default=None, help="兼容 run_all_checks 的接口（忽略）")
    args = ap.parse_args()
    if not os.path.isfile(HERO):
        print("[FATAL] 缺 %s" % HERO, file=sys.stderr)
        return 2
    d = build()
    print("规则四·有据点（文化 = 最后当城主年代的据点「地」）：%d 人" % len(d["lords"]))
    print("规则一·游荡者（六代全程无家）：%d 人" % len(d["wand"]))
    print("其中 CultureID 需改的共 %d 人：" % len(d["changes"]))
    for hid, cn, a, b in d["changes"][:15]:
        print("   %-24s %-8s %s → %s" % (hid, cn, a or "(空)", b))
    if len(d["changes"]) > 15:
        print("   …（其余 %d 人同类）" % (len(d["changes"]) - 15))
    if args.check:
        if d["changes"]:
            print("[CHECK] ❌ 有 %d 人的英雄文化未落（跑 --apply）" % len(d["changes"]))
            return 1
        print("[CHECK] ✅ 英雄文化一致（据点 %d 人 + 游荡者 %d 人）"
              % (len(d["lords"]), len(d["wand"])))
        return 0
    if not args.apply:
        print("\n（未加 --apply，只报告不写盘）")
        return 0
    if not d["changes"]:
        print("\n无需改动。")
        return 0
    stamp = time.strftime("%Y%m%d_%H%M%S")
    shutil.copy2(HERO, HERO + ".bak_ronin_" + stamp)
    n = 0
    for r in d["body"]:
        rec = dict(zip(d["hdr"], r))
        want = want_culture(rec)
        if want and r[d["idx"]].strip() != want:
            r[d["idx"]] = want
            n += 1
    with io.open(HERO, "w", encoding="utf-8-sig", newline="") as fh:
        w = csv.writer(fh, lineterminator="\r\n", quoting=csv.QUOTE_MINIMAL)
        for r in [d["cn_hdr"], d["hdr"]] + d["body"]:
            w.writerow(r)
    back = list(csv.reader(io.open(HERO, encoding="utf-8-sig", newline="")))
    print("\n✅ 已写出 TaikouHero.csv（改 %d 行；往返读回 %d 行 / %d 列）"
          % (n, len(back) - 2, len(back[1])))
    again = build()
    print("   幂等复跑：剩余应改 %d 行（应为 0）" % len(again["changes"]))
    return 1 if again["changes"] else 0


if __name__ == "__main__":
    sys.exit(main())
