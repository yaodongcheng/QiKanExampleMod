# -*- coding: utf-8 -*-
"""把对话 JSON 里的文本键注释成「键 + 中文原文」，方便人审核。

用法：
    python -B -X utf8 Scripts/annotate_dialogue_zh.py
    python -B -X utf8 Scripts/annotate_dialogue_zh.py --check   # 只检查不写

做什么：
    · 读 ModuleData/Languages/CNs/std_LivingWorldNpcs_strings.xml（中文是审核语言）
    · 对每个 story_probe_*.json 里的三处文本字段：
        EntryOption  -> EntryOptionZh
        NpcKey       -> NpcKeyZh
        PlayerKey    -> PlayerKeyZh
      把中文原文写在**紧邻的下一个字段**（保持原有字段顺序，不改别的结构）
    · 幂等：*Zh 字段每次都先删后建，跟着 XML 走；XML 里查不到就写「（缺中文）」

边界：
    🔴 只增改 `*Zh` 三个字段，其余内容原样写回。
    🔴 中文真源仍然是语言 XML（`_story_text_*.py` → XML 那条链）；本脚本是**只读消费者**，
       不改任何台词。改台词请改生成链，然后重跑本脚本。
    🔴 运行时（Newtonsoft）忽略未知字段，新增 *Zh 不影响游戏；`--check` 可让漂移在体检里暴露。
"""
import argparse
import io
import json
import os
import sys
import xml.etree.ElementTree as ET

TEXT_FIELDS = ("EntryOption", "NpcKey", "PlayerKey")
MISSING = "（缺中文）"


def load_lang(path):
    """id -> text；解析失败直接抛，避免半份数据静默通过。"""
    root = ET.parse(path).getroot()
    out = {}
    for node in root.iter("string"):
        key = node.get("id")
        if key:
            out[key] = node.get("text") or ""
    return out


def annotate(obj, cn):
    """就地重建：文本字段后面紧跟一个 <字段>Zh。返回 (新增数, 缺中文键列表)。"""
    added, missing = 0, []

    def walk(value):
        nonlocal added
        if isinstance(value, dict):
            rebuilt = {}
            for key, item in value.items():
                if key.endswith("Zh") and key[:-2] in TEXT_FIELDS:
                    continue                      # 旧的注释字段先丢掉，下面按 XML 重建
                rebuilt[key] = item
                if key in TEXT_FIELDS and isinstance(item, str):
                    text = cn.get(item)
                    if text is None:
                        missing.append(item)
                        text = MISSING
                    rebuilt[key + "Zh"] = text
                    added += 1
                elif isinstance(item, (dict, list)):
                    walk(item)
            value.clear()
            value.update(rebuilt)
        elif isinstance(value, list):
            for item in value:
                walk(item)

    walk(obj)
    return added, missing


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
    files = sorted(f for f in os.listdir(folder) if f.startswith("story_probe_") and f.endswith(".json"))
    if not files:
        print("ERROR: 没有找到 story_probe_*.json: " + folder)
        return 1

    total, changed, missing_all = 0, 0, []
    for name in files:
        path = os.path.join(folder, name)
        with io.open(path, encoding="utf-8") as stream:
            original = stream.read()
        data = json.loads(original)
        added, missing = annotate(data, cn)
        total += added
        missing_all.extend(missing)
        rendered = json.dumps(data, ensure_ascii=False, indent=2) + "\n"
        dirty = rendered != original
        if dirty:
            changed += 1
            if not args.check:
                with io.open(path, "w", encoding="utf-8", newline="\n") as stream:
                    stream.write(rendered)
        print("  %-32s fields=%-4d %s" % (name, added, "changed" if dirty else "unchanged"))

    print("annotate_dialogue_zh: files=%d changed=%d fields=%d missing_cn=%d"
          % (len(files), changed, total, len(set(missing_all))))
    for key in sorted(set(missing_all))[:10]:
        print("  missing CN: " + key)
    if args.check and changed:
        print("CHECK: %d file(s) are out of date; run without --check to rewrite." % changed)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
