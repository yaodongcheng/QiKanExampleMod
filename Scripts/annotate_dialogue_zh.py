# -*- coding: utf-8 -*-
"""把对话 JSON 里的文本键注释成「键 + 中文原文」，方便人审核。

用法：
    python -B -X utf8 Scripts/annotate_dialogue_zh.py
    python -B -X utf8 Scripts/annotate_dialogue_zh.py --check   # 只检查不写

做什么：
    · 读 ModuleData/Languages/CNs/std_LivingWorldNpcs_strings.xml（中文是审核语言）
    · 扫 Dialogues/ 下唯一维护源 *.dialog.json：EntryOption / TextKey / PlayerKey
      把中文原文写在**紧邻的下一行**（字段顺序、缩进、紧凑数组等其余格式一律不动）
    · 幂等：`*Zh` 行每次都先删后建，跟着 XML 走；XML 里查不到就写「（缺中文）」

边界：
    🔴 只增删 `*Zh` 行，其余文本原样保留。**按行插入，不整份重排** ——
       旧版用 json.dumps 重写会把 `gate_interrogation.dialog.json` 的紧凑数组撑开
       （252 行 → 511 行），真实改动淹进格式 diff 里没法审。
    🔴 中文真源仍然是语言 XML（`_story_text_*.py` → XML 那条链）；本脚本是**只读消费者**，
       不改任何台词。改台词请改生成链，然后重跑本脚本。
    🔴 新格式解析器是白名单制（C# `StoryDialogueParser` 与 `check_story_probe.py` 各一份）：
       `*Zh` 写进 *.dialog.json 之前，两处白名单都必须放行 `*Zh`，
       否则 unknown_key → 装载器「任一失败整批不发布」→ 一句剧情对话都进不去。
"""
import argparse
import io
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

TEXT_FIELDS = ("EntryOption", "TextKey", "PlayerKey")
MISSING = "（缺中文）"

# 只认「整行就是一个文本字段」的行（正式源格式就是这么写的；脚本会核对条数，漏一条就报错）
FIELD_RE = re.compile(r'^(\s*)"(' + "|".join(TEXT_FIELDS) + r')":\s*"((?:[^"\\]|\\.)*)"(\s*,?)\s*$')
ZH_LINE_RE = re.compile(r'^\s*"(' + "|".join(TEXT_FIELDS) + r')Zh"\s*:')
# 单行对象：`{ "Id": "x", "PlayerKey": "y", ... },`（gate_interrogation 的选项就是这么写的）
OBJ_LINE_RE = re.compile(r'^(\s*)\{\s*(.*?)\s*\}(\s*,?)\s*$')


def split_top_level(body):
    """按顶层逗号切一个对象体（认得字符串转义与嵌套的 [] {}）。"""
    parts, depth, buf, in_str, esc = [], 0, [], False, False
    for ch in body:
        if in_str:
            buf.append(ch)
            if esc:
                esc = False
            elif ch == "\\":
                esc = True
            elif ch == '"':
                in_str = False
            continue
        if ch == '"':
            in_str = True
            buf.append(ch)
            continue
        if ch in "[{":
            depth += 1
        elif ch in "]}":
            depth -= 1
        if ch == "," and depth == 0:
            parts.append("".join(buf).strip())
            buf = []
            continue
        buf.append(ch)
    tail = "".join(buf).strip()
    if tail:
        parts.append(tail)
    return parts


def expand_inline_objects(lines):
    """把「含文本字段的单行对象」展开成多行 —— 否则中文副本没地方落，只能挤在一行里。

    只动这种对象；紧凑数组（`"Participants": ["questioner", "player"]`）等其它写法原样保留。
    """
    out = []
    for line in lines:
        m = OBJ_LINE_RE.match(line)
        if not m:
            out.append(line)
            continue
        indent, body, comma = m.group(1), m.group(2), m.group(3).strip()
        if not any('"' + f + '":' in body for f in TEXT_FIELDS):
            out.append(line)
            continue
        parts = split_top_level(body)
        if len(parts) < 2:
            out.append(line)
            continue
        inner = indent + "  "
        out.append(indent + "{")
        for i, part in enumerate(parts):
            out.append(inner + part + ("," if i < len(parts) - 1 else ""))
        out.append(indent + "}" + comma)
    return out


def load_lang(path):
    """id -> text；解析失败直接抛，避免半份数据静默通过。"""
    root = ET.parse(path).getroot()
    out = {}
    for node in root.iter("string"):
        key = node.get("id")
        if key:
            out[key] = node.get("text") or ""
    return out


def count_text_fields(data):
    """JSON 里真正的文本字段条数（用来核对按行扫描有没有漏）。"""
    total = 0

    def walk(value):
        nonlocal total
        if isinstance(value, dict):
            for key, item in value.items():
                if key in TEXT_FIELDS and isinstance(item, str):
                    total += 1
                elif isinstance(item, (dict, list)):
                    walk(item)
        elif isinstance(value, list):
            for item in value:
                walk(item)

    walk(data)
    return total


def strip_zh(value):
    """递归剥掉 `*Zh` 审核注释，只留真数据（自检③ 用）。"""
    if isinstance(value, dict):
        return {k: strip_zh(v) for k, v in value.items()
                if not (k.endswith("Zh") and k[:-2] in TEXT_FIELDS)}
    if isinstance(value, list):
        return [strip_zh(v) for v in value]
    return value


def annotate_text(text, cn):
    """按行重建：每个文本字段行后面紧跟一行 `<字段>Zh`。

    返回 (新文本, 字段数, 缺中文键列表, 未匹配字段数)。
    """
    eol = "\r\n" if "\r\n" in text else "\n"
    lines = text.split(eol)
    trailing_eol = bool(lines) and lines[-1] == ""
    if trailing_eol:
        lines.pop()

    out = []
    matched, missing = 0, []
    for line in expand_inline_objects(lines):
        if ZH_LINE_RE.match(line):
            continue                                  # 旧的注释行先丢掉，下面按 XML 重建
        m = FIELD_RE.match(line)
        if not m:
            out.append(line)
            continue
        indent, key, raw_value, comma = m.group(1), m.group(2), m.group(3), m.group(4)
        try:
            value = json.loads('"' + raw_value + '"')
        except ValueError:
            out.append(line)                          # 解不出来就别动这行，交给下面的条数核对报警
            continue
        zh = cn.get(value)
        if zh is None:
            missing.append(value)
            zh = MISSING
        # 插了一行 ⇒ 原行必须带逗号；新行继承原来的「是否最后一项」
        out.append(indent + '"' + key + '": "' + raw_value + '",')
        out.append(indent + '"' + key + 'Zh": ' + json.dumps(zh, ensure_ascii=False) + comma.strip())
        matched += 1

    rendered = eol.join(out) + (eol if trailing_eol else "")
    return rendered, matched, missing


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--module", default=os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    ap.add_argument("--check", action="store_true", help="只报告差异，不写文件")
    args = ap.parse_args()

    module = os.path.abspath(args.module)
    cn_path = os.path.join(module, "ModuleData", "Languages", "CNs", "std_LivingWorldNpcs_strings.xml")
    if not os.path.exists(cn_path):
        print("ERROR: 中文语言表缺失: " + cn_path)
        return 1
    cn = load_lang(cn_path)

    folder = os.path.join(module, "ModuleData", "DesignData", "Dialogues")
    files = sorted(f for f in os.listdir(folder) if f.endswith(".dialog.json"))
    if not files:
        print("ERROR: 没有找到对话 JSON: " + folder)
        return 1

    total, changed, missing_all, errors = 0, 0, [], []
    for name in files:
        path = os.path.join(folder, name)
        with io.open(path, encoding="utf-8", newline="") as stream:
            original = stream.read()
        rendered, fields, missing = annotate_text(original, cn)
        total += fields
        missing_all.extend(missing)

        # 自检①：语法还在（写完坏 JSON = 游戏里整批对话不发布）
        try:
            parsed = json.loads(rendered)
        except Exception as ex:
            errors.append("%s: 输出不是合法 JSON (%s)" % (name, ex))
            continue
        # 自检②：按行扫到的条数 == JSON 里实际条数（不等 = 有字段没独占一行，脚本会漏）
        want = count_text_fields(json.loads(original))
        if fields != want:
            errors.append("%s: 按行匹配 %d 条，JSON 里实际 %d 条（该字段没独占一行）"
                          % (name, fields, want))
            continue
        # 自检③：除 *Zh 外数据完全不变
        if json.dumps(strip_zh(parsed), sort_keys=True, ensure_ascii=False) != \
                json.dumps(strip_zh(json.loads(original)), sort_keys=True, ensure_ascii=False):
            errors.append("%s: 除 *Zh 外内容有变化，已中止" % name)
            continue

        dirty = rendered != original
        if dirty:
            changed += 1
            if not args.check:
                with io.open(path, "w", encoding="utf-8", newline="") as stream:
                    stream.write(rendered)
        print("  %-36s fields=%-4d %s" % (name, fields, "changed" if dirty else "unchanged"))

    print("annotate_dialogue_zh: files=%d changed=%d fields=%d missing_cn=%d"
          % (len(files), changed, total, len(set(missing_all))))
    for key in sorted(set(missing_all))[:10]:
        print("  missing CN: " + key)
    for err in errors:
        print("ERROR: " + err)
    if errors:
        return 3
    if args.check and changed:
        print("CHECK: %d file(s) are out of date; run without --check to rewrite." % changed)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
