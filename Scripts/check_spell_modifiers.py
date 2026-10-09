#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Spell modifier (gem) table checker —— 修正宝石表体检
============================================================================
背景（2026-09-28，阶段 5「修正机制」）：
  宝石 = **对属性的一组增量**（计划 §16.1）。它的"合法性"完全取决于两件事：
    ① 改的字段**在属性表里**（`Combat/SpellModifier.cs` 的 `SpellFields`，唯一真源）
    ② 引用的东西**都存在**（触发子块 / 子法术 / 子宝石 / 手环物品）
  这两件事错了，运行期的表现都是**静默失效**（加载期记一条日志就过去了）——
  所以必须有离线体检把它们挡在发布之前。

判据（逐条）：
  1. 每个 `<Modifier>` 的 `field` ∈ C# 属性表（**从源码抽**，不另抄一份名单）
  2. `Add`/`Mul` 只能用在**数值**字段；数值字段的值必须能解析成数字
  3. `Set` 到 `on_hit_cast`/`on_timer_cast`/`on_expire_cast`/`on_bounce_cast` 的 `sub`
     必须在本宝石内有同名 `<Sub>`
  4. 每个 `<Sub>` 的 `<Base spell>` 必须在某份 `Spells.xml` 里存在
  5. `<Sub><Gems>` 引的每颗宝石必须在宝石表里
  6. `<Loadout seal>` 指向的物品必须在内容包里定义
  7. `stage` 只能是 cast / aim / deliver / payload
  8. `name` 必须是 `{=KEY}fallback` 形式（铁律 13：玩家可见文本走本地化）
  9. 每颗宝石至少有一条操作（没有操作的宝石 = 什么都不做）
  10. id 不重复

Usage:
  python Scripts/check_spell_modifiers.py [--module PATH]
  python Scripts/check_spell_modifiers.py --selftest     # 负面测试：造坏数据必须全部抓到
Exit: 0 全绿 / 1 有问题 / 2 fatal。
"""
import argparse
import re
import sys
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = Path(__file__).resolve().parent.parent
FIELD_TABLE_SRC = REPO / "ExampleModVS" / "ExampleMod" / "ExampleMod" / "Combat" / "SpellModifier.cs"
STAGES = {"cast", "aim", "deliver", "payload"}
TRIGGER_FIELDS = {"on_hit_cast", "on_timer_cast", "on_expire_cast", "on_bounce_cast"}
# C# 里的三个书写工具：N(数值) / B(布尔) / T(文本)
FIELD_DECL = re.compile(r'^\s*([NBT])\("([A-Za-z_][A-Za-z0-9_]*)"')


def read_field_table():
    """从 C# 源码抽出属性表 —— **不另抄一份名单**（抄一份 = 两份会漂移）。

    返回 {字段名: 'num'|'bool'|'text'}；源码读不到 = fatal。
    """
    if not FIELD_TABLE_SRC.is_file():
        return None
    kinds = {"N": "num", "B": "bool", "T": "text"}
    table = {}
    for line in FIELD_TABLE_SRC.read_text(encoding="utf-8").splitlines():
        m = FIELD_DECL.match(line)
        if m:
            table[m.group(2)] = kinds[m.group(1)]
    return table


def is_number(raw):
    if raw is None:
        return False
    try:
        float(raw)
        return True
    except ValueError:
        return False


def collect_spell_ids(module: Path):
    ids = set()
    for f in module.rglob("Spells.xml"):
        try:
            root = ET.parse(str(f)).getroot()
        except Exception:
            continue
        for sp in root.findall("Spell"):
            sid = sp.get("id")
            if sid:
                ids.add(sid.strip())
    return ids


def collect_item_ids(module: Path):
    ids = set()
    for f in module.rglob("*.xml"):
        try:
            root = ET.parse(str(f)).getroot()
        except Exception:
            continue
        if root.tag != "Items":
            continue
        for it in root.findall("Item"):
            iid = it.get("id")
            if iid:
                ids.add(iid.strip())
    return ids


def check_document(root, source, fields, spell_ids, item_ids):
    """体检一份 <Modifiers> 文档。返回问题清单（每条 = (归属, 说明)）。"""
    problems = []
    gems = {}
    for mod in root.findall("Modifier"):
        gid = (mod.get("id") or "").strip()
        where = gid or "(缺 id)"
        if not gid:
            problems.append((where, "Modifier 缺 id"))
            continue
        if gid in gems:
            problems.append((where, "id 重复"))
        gems[gid] = mod

        stage = (mod.get("stage") or "payload").strip()
        if stage not in STAGES:
            problems.append((where, f"stage '{stage}' 不合法（只能 {'/'.join(sorted(STAGES))}）"))

        name = (mod.get("name") or "").strip()
        if name and not re.match(r"^\{=[^}]+\}.+", name):
            problems.append((where, f"name '{name}' 不是 {{=KEY}}fallback 形式（铁律 13：玩家可见文本走本地化）"))

        sub_ids = set()
        for sub in mod.findall("Sub"):
            sid = (sub.get("id") or "").strip()
            if not sid:
                problems.append((where, "<Sub> 缺 id"))
                continue
            sub_ids.add(sid)
            base = sub.find("Base")
            spell = (base.get("spell") if base is not None else None) or ""
            spell = spell.strip()
            if not spell:
                problems.append((where, f"子块 '{sid}' 缺 <Base spell=\"…\">"))
            elif spell not in spell_ids:
                problems.append((where, f"子块 '{sid}' 引的法术 '{spell}' 不在任何 Spells.xml 里"))
            gems_node = sub.find("Gems")
            if gems_node is not None and (gems_node.text or "").strip():
                for ref in re.split(r"[\s,|]+", (gems_node.text or "").strip()):
                    if ref and ref not in _all_gem_ids(root):
                        problems.append((where, f"子块 '{sid}' 的 <Gems> 引了不存在的宝石 '{ref}'"))

        ops = [c for c in mod if c.tag in ("Set", "Add", "Mul")]
        if not ops:
            problems.append((where, "一条操作都没有（这颗宝石什么也不做）"))

        for op in ops:
            field = (op.get("field") or "").strip()
            sub = (op.get("sub") or "").strip()
            value = (op.get("value") or "").strip()
            if not field:
                problems.append((where, f"<{op.tag}> 缺 field"))
                continue
            kind = fields.get(field)
            if kind is None:
                problems.append((where, f"字段 '{field}' 不在属性表里（SpellFields）——拼错了，或者该补属性了"))
                continue
            if op.tag in ("Add", "Mul") and kind != "num":
                problems.append((where, f"<{op.tag} field=\"{field}\"> 是 {kind} 字段，不能 + / ×"))
                continue
            if kind == "num" and not sub and not is_number(value):
                problems.append((where, f"数值字段 '{field}' 的值 '{value}' 不是数字"))
                continue
            if kind == "bool" and not sub and value.lower() not in ("true", "false", "1", "0", "on", "off"):
                problems.append((where, f"布尔字段 '{field}' 的值 '{value}' 不是 true/false"))
                continue
            if field in TRIGGER_FIELDS:
                if not sub:
                    problems.append((where, f"<{op.tag} field=\"{field}\"> 缺 sub（触发字段要引一个 <Sub id>）"))
                elif sub not in sub_ids:
                    problems.append((where, f"触发字段 '{field}' 引的子块 '{sub}' 在本宝石里不存在"))

    for loadout in root.findall("Loadout"):
        seal = (loadout.get("seal") or "").strip()
        slots = (loadout.get("slots") or "").strip()
        if not seal:
            problems.append(("(Loadout)", "缺 seal"))
        elif item_ids and seal not in item_ids:
            problems.append(("(Loadout)", f"seal 物品 '{seal}' 在内容包里没有定义"))
        if not slots.isdigit():
            problems.append(("(Loadout)", f"slots '{slots}' 不是非负整数"))
    return problems


def _all_gem_ids(root):
    return {(m.get("id") or "").strip() for m in root.findall("Modifier")}


def run(module: Path, fields):
    files = sorted(module.rglob("Modifiers.xml"))
    if not files:
        print(f"[FATAL] 找不到 Modifiers.xml（在 {module} 下）")
        return 2
    spell_ids = collect_spell_ids(module)
    item_ids = collect_item_ids(module)
    problems = []
    total = 0
    for f in files:
        try:
            root = ET.parse(str(f)).getroot()
        except Exception as e:
            problems.append((f.name, f"XML 解析失败：{e}"))
            continue
        if root.tag != "Modifiers":
            continue
        total += len(root.findall("Modifier"))
        problems.extend(check_document(root, f.name, fields, spell_ids, item_ids))

    if problems:
        print(f"[FAIL] 宝石表体检：{len(problems)} 处问题（共 {total} 颗宝石）")
        for where, msg in problems:
            print(f"  - {where}: {msg}")
        return 1
    print(f"[OK] 宝石表体检：{total} 颗宝石 / 属性表 {len(fields)} 个字段 / "
          f"法术引用 {len(spell_ids)} 条 —— 字段全部在表、引用全部存在")
    return 0


# ─────────────────────────── 负面测试 ───────────────────────────

BAD_CASES = [
    ("字段不在属性表里", '<Modifier id="g1"><Mul field="damagee" value="1.5" /></Modifier>', "不在属性表里"),
    ("Add 用在布尔字段", '<Modifier id="g2"><Add field="drill" value="1" /></Modifier>', "不能 + / ×"),
    ("数值字段填了非数字", '<Modifier id="g3"><Mul field="damage" value="很强" /></Modifier>', "不是数字"),
    ("触发字段引了不存在的子块", '<Modifier id="g4"><Set field="on_hit_cast" sub="nope" /></Modifier>', "在本宝石里不存在"),
    ("子块引的法术不存在", '<Modifier id="g5"><Sub id="s"><Base spell="no_such_spell" /></Sub></Modifier>', "不在任何 Spells.xml 里"),
    ("stage 不合法", '<Modifier id="g6" stage="投送"><Mul field="damage" value="2" /></Modifier>', "不合法"),
    ("name 没走本地化", '<Modifier id="g7" name="伤害强化"><Mul field="damage" value="2" /></Modifier>', "本地化"),
    ("一条操作都没有", '<Modifier id="g8" />', "什么也不做"),
    ("id 重复", '<Modifier id="dup"><Mul field="damage" value="2" /></Modifier>'
                '<Modifier id="dup"><Mul field="damage" value="3" /></Modifier>', "id 重复"),
    ("Loadout 缺 seal", '<Loadout slots="3" />', "缺 seal"),
    ("Loadout slots 不是数字", '<Loadout seal="lwn_spell_seal" slots="三" />', "非负整数"),
    ("子块 Gems 引了不存在的宝石", '<Modifier id="g9"><Sub id="s"><Base spell="projectile_yinmo_zhan" />'
                                    '<Gems>gem_nope</Gems></Sub></Modifier>', "不存在的宝石"),
]


def selftest(module: Path, fields):
    """负面测试：每条坏数据都必须被**恰好抓到**（抓不到 = 体检形同虚设）。"""
    spell_ids = collect_spell_ids(module)
    item_ids = collect_item_ids(module)
    failed = 0
    for title, body, expect in BAD_CASES:
        root = ET.fromstring(f"<Modifiers>{body}</Modifiers>")
        problems = check_document(root, "selftest", fields, spell_ids, item_ids)
        hit = any(expect in msg for _, msg in problems)
        if not hit:
            print(f"  [FAIL] 负面测试「{title}」没被抓到（期望含 '{expect}'，实得 {problems}）")
            failed += 1
    # 正向对照：一份好数据不许被误报
    good = ET.fromstring(
        '<Modifiers><Loadout seal="lwn_spell_seal" slots="3" />'
        '<Modifier id="gem_ok" stage="payload" name="{=X}Ok"><Mul field="damage" value="1.5" /></Modifier>'
        '<Modifier id="gem_trig" stage="deliver"><Set field="on_hit_cast" sub="s" />'
        '<Sub id="s"><Base spell="projectile_yinmo_zhan" /><Gems>gem_ok</Gems></Sub></Modifier></Modifiers>')
    good_problems = check_document(good, "selftest", fields, spell_ids, item_ids)
    if good_problems:
        print(f"  [FAIL] 正向对照被误报：{good_problems}")
        failed += 1
    if failed:
        print(f"[FAIL] 负面测试：{failed} 例不合格")
        return 1
    print(f"[OK] 负面测试：{len(BAD_CASES)} 例坏数据全部抓到 + 正向对照零误报")
    return 0


def main():
    ap = argparse.ArgumentParser(description="Spell modifier table checker")
    ap.add_argument("--module",
                    default=r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs")
    ap.add_argument("--selftest", action="store_true", help="只跑负面测试（造坏数据必须抓到）")
    args = ap.parse_args()

    fields = read_field_table()
    if not fields:
        print(f"[FATAL] 读不到属性表源码：{FIELD_TABLE_SRC}")
        return 2
    module = Path(args.module)
    if not module.is_dir():
        print(f"[FATAL] 内容包目录不存在：{module}")
        return 2
    if args.selftest:
        return selftest(module, fields)
    return run(module, fields)


if __name__ == "__main__":
    sys.exit(main())
