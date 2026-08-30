# -*- coding: utf-8 -*-
"""
stage_inject.py — 演绎剧本舞台注入（v1）
================================================================
设计规格：08b §三（08b-转化器规格-自动化翻译流水线.md）
输入：tk5_to_json.py 生成的 story/*.jsonc（第一版：lines + T# 注释 + _src 源行）
输出：第二版 story/*.jsonc（actors 表 + present 标记 + slot 槽位）

推导规则（05 源格式补充纪律 4-7）：
    1. actors = 说话人集合 ∪ 对话对象集合（对象从 _src 注释解析 `對話:(說話人,對象)`）
    2. 沉默观众位：有出场无台词的角色（对象 - 说话人）→ "present": true（演出开始即入场）
    3. 说话人 → 编译器自动入场（05 纪律 6①），不写 present
    4. slot 分配（StageDirector.cs 槽位语义参考）：
       最高频说话人 = throne（主位）；对话对象优先 = side；其余 = gate
    5. 形态约束（05 纪律 7）：actor_*/actors 仅 scene 形态合法；
       map_dialogue/menu_dialogue 纯对白序列禁止 actors（立绘常驻）
"""
import json
import os
import re
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
DEFAULT_STORY = os.path.join(REPO_ROOT, "plans", "scenario-campaign-mode", "story_event_json")

RE_DIALOG = re.compile(r"對話:\(([^,]+),([^)]+)\)")


def parse_src_speakers(text):
    """从 _src 注释解析 说话人/对象。"""
    pairs = []
    for m in RE_DIALOG.finditer(text):
        pairs.append((m.group(1).strip(), m.group(2).strip()))
    return pairs


def load_segment(path):
    """读 jsonc（剥注释）→ dict；同时恢复 T# 注释配对（_t/_src 防丢，铁律 7 锚点）。"""
    with open(path, encoding="utf-8") as f:
        raw = f.read()
    clean = re.sub(r"^\s*//.*$", "", raw, flags=re.MULTILINE)
    seg = json.loads(clean)
    # 恢复 T# 注释：// T{n} {源行} 与 lines 演出行按序配对
    comments = re.findall(r"// T(\d+) (.+)", raw)
    ci = 0
    for ln in seg.get("lines", []):
        if ci < len(comments) and ln.get("cmd") in ("dialogue", "narrator"):
            ln["_t"] = int(comments[ci][0])
            ln["_src"] = comments[ci][1].strip()
            ci += 1
    return seg


def dump_segment(seg):
    """dict → jsonc（保持 T# 注释）。"""
    lines = seg.get("lines", [])
    parts = ["{"]
    parts.append(f'  "id": "{seg["id"]}",')
    parts.append(f'  "form": "{seg.get("form", "scene")}",')
    if seg.get("actors"):
        parts.append('  "actors": [')
        for i, a in enumerate(seg["actors"]):
            comma = "," if i < len(seg["actors"]) - 1 else ""
            parts.append(f"    {json.dumps(a, ensure_ascii=False)}{comma}")
        parts.append("  ],")
    parts.append("  \"lines\": [")
    items = []
    for ln in lines:
        if "_t" in ln:
            t_no = ln.pop("_t")
            src = ln.pop("_src")
            items.append(f"    // T{t_no} {src.strip()}")
            items.append("    " + json.dumps(ln, ensure_ascii=False))
        else:
            items.append("    " + json.dumps(ln, ensure_ascii=False))
    parts.append(",\n".join(items))
    parts.append("  ]\n}")
    return "\n".join(parts)


def inject_stage(path):
    """对单个演绎剧本注入 actors。"""
    seg = load_segment(path)
    form = seg.get("form", "scene")
    if form != "scene":
        return None  # 立绘形态禁止 actors（05 纪律 7）

    lines = seg.get("lines", [])
    if not lines:
        return None

    # 说话人集合 + 对话对象（listener 字段，v4.1 结构化——不再从 _src 注释正则反推）
    speakers = set()
    obj_counter = {}
    for ln in lines:
        if "speaker" in ln:
            speakers.add(ln["speaker"])
        if "listener" in ln:
            obj_counter[ln["listener"]] = obj_counter.get(ln["listener"], 0) + 1
    # 对象 → DSL 引用（与 tk5_to_json 归一表同源；此处用源词做 key 报告）
    speaker_freq = {}
    for ln in lines:
        if "speaker" in ln:
            speaker_freq[ln["speaker"]] = speaker_freq.get(ln["speaker"], 0) + 1

    # actors 表：说话人 + 对象（对象 = 沉默观众位候选）
    actors = []
    seen = set()
    # 主位 = 最高频说话人
    main_speaker = max(speaker_freq, key=speaker_freq.get) if speaker_freq else None
    for sp, freq in sorted(speaker_freq.items(), key=lambda x: -x[1]):
        if sp in seen:
            continue
        seen.add(sp)
        slot = "throne" if sp == main_speaker else "side"
        if sp.startswith("Hero::"):
            actors.append({"heroId": sp, "slot": slot})
        else:   # 模板角色/占位 → agentId（05 演员表：有 Hero 用 heroId，模板用 agentId）
            actors.append({"agentId": sp, "slot": slot})
    # 沉默观众位（对象非说话人）→ present: true
    for obj, cnt in obj_counter.items():
        if obj in seen or obj == "主人公":
            continue
        seen.add(obj)
        # 🔴 2026-08-30 v6：agentId = 纯 DSL 引用（listener 已翻译，槽/特殊值亦合法引用）；
        #   「待 07 确认角色池成员」语义放 _note 注记层——禁止把 🔴待07 前缀写进 agentId（参数污染）
        actors.append({"agentId": obj, "slot": "gate", "present": True,
                       "_note": f"对话对象（沉默观众位，05 纪律 5）；{obj} 为槽/特殊引用，"
                                "角色池成员待 07 确认"})

    seg["actors"] = actors
    return seg


def main():
    import argparse
    ap = argparse.ArgumentParser(description="演绎剧本舞台注入")
    ap.add_argument("--story", default=DEFAULT_STORY)
    ap.add_argument("--scenario", default="okehazama")
    args = ap.parse_args()

    story_dir = os.path.join(args.story, args.scenario, "story")
    if not os.path.isdir(story_dir):
        print(f"[ERR] 找不到 {story_dir}")
        sys.exit(1)

    n_scene = n_skip = 0
    for fn in sorted(os.listdir(story_dir)):
        if not fn.endswith(".jsonc"):
            continue
        path = os.path.join(story_dir, fn)
        seg = inject_stage(path)
        if seg is None:
            n_skip += 1
            continue
        with open(path, "w", encoding="utf-8") as f:
            f.write(dump_segment(seg))
        n_scene += 1
        print(f"[OK] {fn}: actors={len(seg['actors'])}")

    # 🔴 重建人读合并版 story.jsonc（与分文件保持同步——注入后 actors 一致）
    #   合法 JSON 数组：外层 [ ] 包裹、段间逗号、横幅注释为 // 注释行（jsonc 合法——注释可行，
    #   剥注释后 = json.loads 可解析）；引擎读取源 = story/*.jsonc 分文件，合并版仅供人读/搜索。
    merged = ["["]
    files = sorted(f for f in os.listdir(story_dir) if f.endswith(".jsonc"))
    for fi, fn in enumerate(files):
        with open(os.path.join(story_dir, fn), encoding="utf-8") as f:
            raw = f.read().rstrip()
        if not raw.endswith("}"):
            raw = raw.rstrip(",")          # 防御：分文件尾不该有逗号，有则剥
        merged.append(f"// ============ {fn[:-6]} ============")
        merged.append(raw)
        merged.append("," if fi < len(files) - 1 else "")
    merged.append("]")
    with open(os.path.join(args.story, args.scenario, "story.jsonc"), "w", encoding="utf-8") as f:
        f.write("\n".join(merged) + "\n")

    print(f"\n完成：scene 形态注入 {n_scene} 个，立绘形态跳过 {n_skip} 个；story.jsonc 已重建")


if __name__ == "__main__":
    main()
