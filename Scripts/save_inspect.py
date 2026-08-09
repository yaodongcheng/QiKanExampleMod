#!/usr/bin/env python3
"""save_inspect.py -- Bannerlord save file inspector / repair tool.

解析 .sav（MetaData JSON + raw-deflate GameData），体检 Strings 表：
  - 检测 short 溢出（负长度 = 单条字符串 > 32767 字节写坏的表 → 读档必崩）
  - 列出超长 entry（定位是哪个 key / 什么内容超长）
  - --fix 修复：定点手术——只截断超长字符串（结构感知：数组型 JSON 逐元素保留，
    保证 JSON 合法）+ 修正长度字段，其余字节原封不动 + 重压缩写回

格式依据：Knowledge/存档机制深度解析.md 第十章（2026-08 反编译 + Python 实测验证）。

⚠ 修复原理：负长度 entry 之后的表内容在"读取视角"已错位，但**原始字节完好**——
修复只对坏 entry 的 payload 截断 + 重写长度字段，后续 entry 字节原样前移，
不经过"解析-重建"（解析路径读到的全是错位垃圾，重建会损坏存档）。

用法：
  python save_inspect.py <save.sav>                      # 体检（不改文件）
  python save_inspect.py <save.sav> --keys               # 只列 lwn_* 前缀条目（SyncData key 体检）
  python save_inspect.py <save.sav> --strings [N]        # 列出 Strings 表全部条目（前 N 字符，默认 80）
  python save_inspect.py <save.sav> --fix [--max 30000]  # 修复预览（自动备份 .bak.N）
  python save_inspect.py <save.sav> --dump=lwn_crime_events  # 查看某个 SyncData key 的具体 JSON 值
  python save_inspect.py <save.sav> --fix --apply        # 修复并写回
  python save_inspect.py <save.sav> --output=report.txt   # 体检报告落盘（UTF-8），控制台静默
"""

import json
import os
import shutil
import struct
import sys
import zlib

# Windows 下强制 UTF-8 输出：默认 GBK 代码页会让中文内容乱码
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

SHORT_MAX = 32767          # Strings 表长度字段上限（signed short）
SAFE_MAX = 30000           # 修复目标上限（留余量）


class SaveParseError(Exception):
    pass


# ── 存档文件层 ──

def read_save(path):
    """返回 (meta_dict, game_data_bytes)。"""
    with open(path, "rb") as f:
        raw = f.read()
    if len(raw) < 8:
        raise SaveParseError(f"{path} 太小（{len(raw)}B），不是有效存档")
    meta_len = struct.unpack("<i", raw[0:4])[0]
    if meta_len <= 0 or 4 + meta_len > len(raw):
        raise SaveParseError(f"MetaData 长度前缀非法: {meta_len}")
    try:
        meta = json.loads(raw[4:4 + meta_len].decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as e:
        raise SaveParseError(f"MetaData 解析失败: {e}")
    try:
        game_data = zlib.decompress(raw[4 + meta_len:], -15)  # raw deflate
    except zlib.error as e:
        raise SaveParseError(f"GameData 解压失败: {e}")
    return meta, game_data


def write_save(path, meta, game_data):
    """写回：MetaData JSON（无校验和，可安全修改）+ 长度前缀 + 重压缩 GameData。
    🔴 必须 raw deflate（-15）——zlib.compress 默认 zlib 包装格式，读档会解压失败。"""
    meta_bytes = json.dumps(meta, ensure_ascii=False).encode("utf-8")
    compressor = zlib.compressobj(9, zlib.DEFLATED, -15)
    compressed = compressor.compress(game_data) + compressor.flush()
    with open(path, "wb") as f:
        f.write(struct.pack("<i", len(meta_bytes)))
        f.write(meta_bytes)
        f.write(compressed)


def backup_save(path):
    """自动备份：path.bak → path.bak.1 → path.bak.2 …"""
    for i in range(100):
        bak = f"{path}.bak" if i == 0 else f"{path}.bak.{i}"
        if not os.path.exists(bak):
            shutil.copy2(path, bak)
            return bak
    raise SaveParseError("备份失败：已存在 100 个备份")


# ── GameData 层（Write 顺序：Header → Objects → Containers → Strings）──

def parse_game_data(gd):
    off = 0

    def rd(n):
        nonlocal off
        if off + n > len(gd):
            raise SaveParseError(f"GameData 越界读取 {n}B @ {off} (总 {len(gd)}B)")
        b = gd[off:off + n]
        off += n
        return b

    def rd_int():
        return struct.unpack("<i", rd(4))[0]

    def rd_len_prefixed():
        n = rd_int()
        if n < 0:
            raise SaveParseError(f"长度前缀为负: {n} @ {off - 4}")
        return rd(n)

    header_len = rd_int()
    header = rd(header_len)
    object_count = rd_int()
    objects = [rd_len_prefixed() for _ in range(object_count)]
    container_count = rd_int()
    containers = [rd_len_prefixed() for _ in range(container_count)]
    strings_len = rd_int()
    strings_block = rd(strings_len)
    return header, objects, containers, strings_block


# ── ArchiveDeserializer 块层（folder/entry 通用格式）──
#
# entry 布局（Strings 块实测验证）：
#   { 3B folderId, 3B id, 1B extension, 2B short length, payload }
#   payload = 4B int32 str_len + str_len 字节 UTF-8；length 字段 = 4 + str_len
#   （str_len 是 int32 不会溢出；length 是 short 会溢出——坏点就在这里）

def walk_entries(block):
    """按真实偏移遍历块内 entry（不做任何对齐假设）。
    yield (entry_header_offset, length, payload_offset)。
    负长度 entry 的 payload 按 length + 65536 读取真实数据——之后的偏移依然正确
    （原始字节完好，只是长度字段溢出；这使修复成为"定点手术"成为可能）。"""
    off = 0

    def rd(n):
        nonlocal off
        if off + n > len(block):
            raise SaveParseError(f"块越界读取 {n}B @ {off} (块 {len(block)}B)")
        b = block[off:off + n]
        off += n
        return b

    folder_count = struct.unpack("<i", rd(4))[0]
    if folder_count < 0 or folder_count > 1_000_000:
        raise SaveParseError(f"folderCount 非法: {folder_count}")
    for _ in range(folder_count):
        rd(10)  # 3B parent + 3B global + 3B local + 1B ext
    entry_count = struct.unpack("<i", rd(4))[0]
    if entry_count < 0 or entry_count > 10_000_000:
        raise SaveParseError(f"entryCount 非法: {entry_count}")
    for _ in range(entry_count):
        header_off = off
        rd(7)  # 3B folder + 3B id + 1B ext
        length = struct.unpack("<h", rd(2))[0]
        payload_off = off
        if length >= 0:
            rd(length)
        else:
            # 长度字段溢出：真实数据 = 负长度 + 65536（数据本身完整无损）
            rd(length + 65536)
        yield header_off, length, payload_off


def entry_str(payload):
    """payload → 字符串内容：跳过 4B int32 str_len 头。返回 (str_len, content_bytes)。"""
    if len(payload) < 4:
        return 0, b""
    str_len = struct.unpack("<i", payload[0:4])[0]
    if str_len < 0 or 4 + str_len > len(payload):
        return 0, b""
    return str_len, payload[4:4 + str_len]


def parse_block_entries(block):
    """体检用：解析出 entries 列表（负长度 entry 的 payload 为空标记，检测用）。
    注意：负长度 entry 之后的 entries 因错位是"假数据"，仅用于统计/展示。
    entry 含 payload_off（真实数据偏移，dump 用）。"""
    entries = []
    try:
        for header_off, length, payload_off in walk_entries(block):
            if length < 0:
                entries.append({"offset": header_off, "length": length,
                                "payload": b"", "payload_off": payload_off,
                                "real_len": length + 65536})
            else:
                payload = block[payload_off:payload_off + length]
                _, content = entry_str(payload)
                entries.append({"offset": header_off, "length": length,
                                "payload": content, "payload_off": payload_off,
                                "real_len": length})
    except SaveParseError as e:
        raise SaveParseError(f"Strings 块解析失败: {e}")
    return entries


def repair_strings_block(block, max_bytes):
    """定点手术修复：只截断超长/溢出 entry 的字符串内容 + 重写长度字段，其余字节原样。
    返回 (new_block, [(orig_len, new_len)])；无坏 entry 返回 (block, [])。"""
    entries = []
    try:
        for header_off, length, payload_off in walk_entries(block):
            entries.append((header_off, length, payload_off))
    except SaveParseError:
        return block, []  # 结构都解析不动 → 不碰（解析失败本身是另一层问题）

    plan = []
    new_block = bytearray()
    pos = 0
    for header_off, length, payload_off in entries:
        if header_off > pos:  # 头前有残留（不应发生，防御）
            new_block += block[pos:header_off]
            pos = header_off
        # entry 头（9 字节）：3B folder + 3B id + 1B ext + 2B length
        head = block[header_off:header_off + 9]
        real_total = length + 65536 if length < 0 else length
        raw_payload = block[payload_off:payload_off + real_total]
        if length < 0 or real_total > max_bytes:
            # 只截断字符串内容，保留 4B str_len 头
            str_len, content = entry_str(raw_payload)
            if str_len > 0:
                trimmed = truncate_json_array(content, max_bytes - 4)
                if trimmed is None:
                    trimmed = truncate_utf8(content, max_bytes - 4)
                new_payload = struct.pack("<i", len(trimmed)) + trimmed
                head = head[:7] + struct.pack("<h", len(new_payload))
                plan.append((real_total, len(new_payload)))
                raw_payload = new_payload
            else:
                # 无有效 str_len 头（异常结构）：整段硬截断保块可读
                trimmed = truncate_utf8(raw_payload, max_bytes)
                head = head[:7] + struct.pack("<h", len(trimmed))
                plan.append((real_total, len(trimmed)))
                raw_payload = trimmed
        new_block += head + raw_payload
        pos = payload_off + real_total
    # 尾部残留（不应发生，防御）
    if pos < len(block):
        new_block += block[pos:]

    if not plan:
        return block, []
    # 校验：修复后的块必须能干净遍历、无负长度、str_len 与 length 自洽
    try:
        for h, l, p in walk_entries(bytes(new_block)):
            if l < 0:
                raise SaveParseError("修复后仍存在负长度（手术失败）")
            sl, _ = entry_str(bytes(new_block)[p:p + l])
            if l >= 4 and sl + 4 != l:
                raise SaveParseError(f"修复后 entry @{h} str_len+4({sl + 4}) != length({l})（手术失败）")
    except SaveParseError as e:
        raise SaveParseError(f"修复结果校验失败: {e}")
    return bytes(new_block), plan


# ── 字符串工具 ──

def truncate_utf8(b, max_bytes):
    """按 UTF-8 字节截断（回退到合法字符边界）。"""
    if len(b) <= max_bytes:
        return b
    cut = max_bytes
    while cut > 0 and (b[cut] & 0xC0) == 0x80:
        cut -= 1
    return b[:cut]


def truncate_json_array(b, max_bytes):
    """结构感知截断：payload 是 JSON 数组/对象 → 保留完整元素直到字节预算（JSON 始终合法）。
    对象型尝试截断第一个列表字段（如 WorldEventStore 的 events 数组）。
    非数组 / 解析失败返回 None（调用方回退硬截断）。"""
    if not b:
        return b""
    text = b.decode("utf-8", errors="replace").lstrip()
    if not (text.startswith("[") or text.startswith("{")):
        return None
    try:
        obj = json.loads(text)
    except json.JSONDecodeError:
        return None

    def trim_list(lst):
        kept = []
        total = 2  # "[]"
        for item in lst:
            s = json.dumps(item, ensure_ascii=False)
            item_bytes = len(s.encode("utf-8"))
            overhead = 1 if not kept else 2
            if total + overhead + item_bytes > max_bytes:
                break
            kept.append(item)
            total += overhead + item_bytes
        return kept

    if isinstance(obj, list):
        kept = trim_list(obj)
        if not kept:
            return b"[]"
        if len(kept) == len(obj):
            return None  # 全部保留，无需截断
        return json.dumps(kept, ensure_ascii=False).encode("utf-8")
    if isinstance(obj, dict):
        for key, val in obj.items():
            if isinstance(val, list):
                kept = trim_list(val)
                if kept and len(kept) < len(val):
                    obj[key] = kept
                    return json.dumps(obj, ensure_ascii=False).encode("utf-8")
    return None


# ── 主流程 ──

def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    flags = {a.split("=", 1)[0]: True for a in sys.argv[1:] if a.startswith("--")}
    max_bytes = SAFE_MAX
    dump_key = None
    for a in sys.argv[1:]:
        if a.startswith("--max="):
            max_bytes = int(a.split("=", 1)[1])
        elif a.startswith("--dump="):
            dump_key = a.split("=", 1)[1]
    max_bytes = min(max(max_bytes, 1), SHORT_MAX)  # 防御钳制

    if not args:
        print(__doc__)
        return 1
    path = args[0]

    # --output=<file>：体检报告落盘（UTF-8），控制台不再输出
    out_path = None
    for a in sys.argv[1:]:
        if a.startswith("--output="):
            out_path = a.split("=", 1)[1]
    if out_path:
        try:
            sys.stdout = open(out_path, "w", encoding="utf-8")
            print(f"# save_inspect report: {os.path.basename(path)} @ {__import__('datetime').datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
        except OSError as e:
            print(f"🔴 无法写入输出文件 {out_path}: {e}", file=sys.stderr)
            return 1

    print(f"== 解析 {os.path.basename(path)} ==")
    meta, gd = read_save(path)
    meta_list = meta.get("List", {})
    print(f"  MetaData: ApplicationVersion={meta_list.get('ApplicationVersion', '?')}")
    mods = str(meta_list.get("Modules", ""))
    print(f"  Modules: {mods[:120]}{'…' if len(mods) > 120 else ''}")
    print(f"  GameData: {len(gd):,} 字节")

    header, objects, containers, strings_block = parse_game_data(gd)
    print(f"  Header {len(header)}B, Objects {len(objects)} 块, Containers {len(containers)} 块, "
          f"Strings {len(strings_block)}B")

    print(f"\n== Strings 表扫描（安全阈值 {max_bytes}B / short 上限 {SHORT_MAX}B）==")
    entries = parse_block_entries(strings_block)
    print(f"  folders/entries: {len(entries)}")

    bad = [e for e in entries if e["length"] < 0]
    over_safe = [e for e in entries if 0 <= e["length"] > max_bytes]

    if bad:
        print(f"  🔴 发现 {len(bad)} 条 short 溢出 entry（负长度 → 读档必崩）！")
        for e in bad[:5]:
            print(f"    offset#{e['offset']}: length={e['length']}（真实数据 {e['real_len']:,}B）")
        # 定位上一个正常 entry 内容（溢出点前一条 = 损坏 key 的邻居，常用于识别归属）
        for e in bad[:3]:
            idx = entries.index(e)
            for j in range(idx - 1, max(-1, idx - 4), -1):
                p = entries[j]["payload"]
                if p:
                    print(f"      前文 entry#{j}: {p[:60].decode('utf-8', errors='replace')}")
                    break
    else:
        print("  ✓ 无负长度 entry（无 short 溢出）")

    if over_safe:
        print(f"  ⚠ {len(over_safe)} 条 entry 超过安全阈值 {max_bytes}B（未溢出但贴近上限）：")
        for e in over_safe[:10]:
            print(f"    len={e['length']:,}B 内容: "
                  f"{e['payload'][:60].decode('utf-8', errors='replace')}…")
    else:
        print(f"  ✓ 无 entry 超过 {max_bytes}B")

    if flags.get("--keys"):
        print("\n== lwn_* 条目 ==")
        for e in entries:
            if e["length"] <= 0:
                continue
            try:
                text = e["payload"].decode("utf-8")
            except UnicodeDecodeError:
                continue
            if text.startswith("lwn_") and len(text) < 100:
                print(f"  {text}  ({e['length']:,}B)")

    if flags.get("--strings"):
        preview_len = int(args[1]) if len(args) > 1 and args[1].isdigit() else 80
        print(f"\n== Strings 全表（前 {preview_len} 字符）==")
        for i, e in enumerate(entries):
            if e["length"] < 0:
                print(f"  #{i}: length={e['length']} (损坏, 真实 {e['real_len']:,}B)")
                continue
            preview = e["payload"][:preview_len].decode("utf-8", errors="replace")
            print(f"  #{i}: len={e['length']:,}  {preview}")

    # ── 查看某个 SyncData key 的具体 JSON 值 ──
    if dump_key:
        print(f"\n== dump: {dump_key} ==")
        found = False
        for i, e in enumerate(entries):
            if e["length"] < 0:
                continue
            try:
                content = e["payload"].decode("utf-8")
            except UnicodeDecodeError:
                continue
            if content != dump_key:
                continue
            found = True
            val = entries[i + 1] if i + 1 < len(entries) else None
            if val is None:
                print(f"  ⚠ {dump_key} 之后没有值 entry")
                break
            if val["length"] < 0:
                payload = strings_block[val["payload_off"]:val["payload_off"] + val["real_len"]]
            else:
                payload = strings_block[val["payload_off"]:val["payload_off"] + val["real_len"]]
            _, val_content = entry_str(payload)
            text = val_content.decode("utf-8", errors="replace")
            print(f"  值大小: {len(payload)}B（内容 {len(val_content)}B）")
            try:
                data = json.loads(text)
                print(f"  JSON 解析 OK: {type(data).__name__}")
                full = json.dumps(data, ensure_ascii=False, indent=1)
                if out_path:
                    print(full)  # 落盘：完整内容
                else:
                    print(full[:4000])  # 终端：截断防刷屏
                    if len(full) > 4000:
                        print(f"  …（共 {len(full):,} 字符，完整内容加 --output=xxx.txt 落盘查看）")
            except json.JSONDecodeError:
                print(f"  （非 JSON 或解析失败，原文前 1500 字符）")
                print(text[:1500])
            break
        if not found:
            print(f"  Strings 表中找不到该 key（值可能内联在 Objects 块，暂不支持）")

    # ── 修复 ──
    if flags.get("--fix"):
        print(f"\n== 修复预览（--max {max_bytes}B）==")
        if not bad and not over_safe:
            print("  无需修复 ✓")
            return 0
        new_block, plan = repair_strings_block(strings_block, max_bytes)
        if not plan:
            print("  修复器未产生任何改动（异常路径，放弃）")
            return 1
        print(f"  将截断 {len(plan)} 个 entry:")
        for i, (a, b) in enumerate(plan[:10]):
            print(f"    {a:,}B → {b:,}B")
        if len(plan) > 10:
            print(f"    …另有 {len(plan) - 10} 个")
        print(f"  Strings 块: {len(strings_block):,}B → {len(new_block):,}B")

        if not flags.get("--apply"):
            print("\n  预览结束（未写回）。加 --apply 真正修复。")
            return 0

        new_gd = bytearray()
        new_gd += struct.pack("<i", len(header)) + header
        new_gd += struct.pack("<i", len(objects))
        for o in objects:
            new_gd += struct.pack("<i", len(o)) + o
        new_gd += struct.pack("<i", len(containers))
        for c in containers:
            new_gd += struct.pack("<i", len(c)) + c
        new_gd += struct.pack("<i", len(new_block)) + new_block
        if len(new_gd) != len(gd) - len(strings_block) + len(new_block):
            print("  🔴 重建尺寸校验失败，放弃写回")
            return 1

        bak = backup_save(path)
        print(f"  备份 → {bak}")
        write_save(path, meta, bytes(new_gd))
        print("  已修复写回 ✓ 建议：启动游戏读档验证 → 再跑本工具复检应全绿")
    return 0


if __name__ == "__main__":
    sys.exit(main())
