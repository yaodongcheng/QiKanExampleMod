# -*- coding: utf-8 -*-
"""check_chan_anatomy.py —— 自建头「101 条顶点位移场落在正确部位」的常驻闸门。

## 为什么需要它
拉杆/表情与网格帧的对应关系由【帧号】决定（脸形段看皮肤 `deform_keys` 的 `key_time_point`；
表情段看引擎 `morph_anims` 的片段），而**帧里装的是什么**完全靠移植管线保证。两次事故都是它没兜住：
1. 2026-09-19 早：59 条脸形位移场被**前后（Y 轴）镜像**过 —— 拉"鼻子"动的是后脑勺
   （蒂法文档 §21.9「额头/颅顶被拉变形、鼻子上凸出一块」）。
2. 2026-09-19 晚：**表情/口型段（f60..100）压根没搬** —— 编译后被 morphfix 补成"原地不动"的空帧
   → 实机**说话时嘴不动、整张脸没有表情**（用户报"亨利没表情"）。
   本闸门因此扩到 **101 条全覆盖**，并把"眼球要能转""下颌件要跟着动"也纳入判据。

## 判据来源（都是实测，不是拍脑袋）
坐标约定：头朝 +Y、Z 朝上、原点在脚底。鼻尖 y≈+0.15~0.18，后脑 y≈−0.06。
脸形段（载体 → 判据 → 三套实测值）：

| 载体 | f9 鼻角 | f31 鼻宽 | f35 嘴宽 | f51 藏耳(位移最大点) |
|---|---|---|---|---|
| 原版 `head_male_a` | +0.142 | +0.146 | +0.139 | (−0.093, y0.014) |
| `head_xxfemale_a`（源头） | +0.142 | +0.144 | +0.142 | ( 0.093, y0.030) |
| 修好后（蒂法/萨菲/亨利） | +0.143~0.148 | +0.146~0.152 | +0.143~0.149 | (±0.094~0.099, y0.026~0.030) |
| 🔴 镜像事故（旧装机） | +0.014~+0.029 | −0.036~−0.024 | −0.010~−0.001 | ( 0.070, y0.113) |

表情段（帧名见 `TpacTool.IO/Model/MorphNameMapping.cs`；数值 = 位移最大点）：

| 帧 | 语义 | 判据 | 原版男壳 | xxfemale 壳 | 亨利(2026-09-19 修后) |
|---|---|---|---|---|---|
| 64 EyebrowRaise | 眉 | my ≥ 0.10 且 mz ≥ 1.67 | (0.153,1.706) | (0.142,1.698) | (0.163,1.705) |
| 71 JawDrop | 下颌 | my ≥ 0.10 且 mz ≤ 1.63 | (0.140,1.564) | (0.146,1.606) | (0.148,1.556) |
| 86 CloseRightEye | 右眼(+x) | mx ≥ +0.02 | +0.034 | +0.034 | +0.035 |
| 87 CloseLeftEye | 左眼(−x) | mx ≤ −0.02 | −0.034 | −0.034 | −0.035 |
| 88 CloseEyes | 双眼 | mz ≥ 1.66 | (0.134,1.688) | (0.137,1.681) | (0.126,1.701) |
| 95 Laugh | 嘴 | my ≥ 0.10 且 mz ≤ 1.68 | (0.135,1.605) | (0.135,1.604) | (0.135,1.606) |
| 99 Speak | 嘴 | my ≥ 0.10 且 mz ≤ 1.68 | (0.144,1.606) | (0.142,1.606) | (0.155,1.598) |

件级判据（🔴 原版头的**每个子网格各带自己的位移场**，脸壳的场套不到眼球上）：
· **眼球要能转**：除脸壳外的某个件，在 f60..63 上 ≥50% 顶点有位移且最大 ≥4mm
  （原版男 `.1` 109/109 · 10.05mm；xxfemale `.2` 594/594 · 8.33mm；亨利修后 1400/1400 · 9.52mm）
· **下颌件要跟着动**：除脸壳外的某个件，在 f71 上 ≥50% 顶点有位移且最大 ≥5mm
  （原版男 `.2` 158/212 · 23.44mm；xxfemale `.1` 177/242 · 18.31mm；亨利修后 1242/1346 · 23.40mm）

## 用法
    python check_chan_anatomy.py --packdir <模块\\AssetPackages> --filter head_tifa_a
    python check_chan_anatomy.py --morphmap <已存的 morphmap 文本>      # 离线复检
退出码 0 = 过，1 = 不过（并逐条打印实测值与判据）。
"""
import argparse
import io
import os
import re
import subprocess
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
TPACCLI = os.path.join(REPO, "tools", "tpactool", "TpacToolCLI",
                       "bin", "Release", "net9.0", "tpaccli.exe")

# 判据表：帧号 → (帧名, 检查类型, 阈值)
#   cy  = 移动点质心 y 必须 ≥ 阈值（面部正面特征都该在头的前半）
#   ear = 位移最大点必须 |x| ≥ XMIN 且 y ≤ YMAX（耳朵在两侧、不在正面）
LANDMARKS = [
    (9,  "NoseAngle",       "cy",  0.10),
    (14, "EyeDepth",        "cy",  0.09),
    (23, "EyePosition",     "cy",  0.08),
    (27, "NoseLength",      "cy",  0.10),
    (29, "NoseTipHeight",   "cy",  0.10),
    (31, "NoseWidth",       "cy",  0.10),
    (35, "MouthWidth",      "cy",  0.10),
    (51, "HideEars",        "ear", (0.06, 0.08)),
]
# 表情段地标（类型见文件头判据表）
#   fy_max = 位移最大点 my ≥ 阈值           fy_min = my ≥ 阈值
#   fz_min = 位移最大点 mz ≥ 阈值           fz_max = mz ≤ 阈值
#   x_pos / x_neg = 位移最大点 mx ≥ +阈值 / ≤ −阈值（抓左右被镜像）
ANIM_LANDMARKS = [
    (64,  "EyebrowRaise",   "fy_min", (0.10, 1.67)),   # (my 阈值, mz 阈值)
    (71,  "JawDrop",        "fy_max", (0.10, 1.63)),
    (86,  "CloseRightEye",  "x_pos",  0.02),
    (87,  "CloseLeftEye",   "x_neg",  0.02),
    (88,  "CloseEyes",      "fz_min", 1.66),
    (95,  "Laugh",          "fy_max", (0.10, 1.68)),
    (99,  "Speak",          "fy_max", (0.10, 1.68)),
]
# 全局帧：HeadScaling 应几乎牵动整块网格（少于这个比例 = 场被削没了）
GLOBAL_FRAME = (46, "HeadScaling", 0.85)

# 覆盖检查：脸形帧 1..59 必须**全部**有位移。
# 🔴 刻意不用「移动点占比」当判据 —— 那个比例取决于头模里脸壳/脖子的几何占比，
#    不同头天生不同（旧蒂法 84.8% vs 新蒂法 95.3%），拿它当阈值会误伤长脖子的头。
#    本项只抓"场被削没/整条通道丢失"这一类，占比只打印供参考。
FACE_FRAMES = range(1, 60)
# 表情/口型帧 60..100 同理全覆盖，**除了 f90 / f92**：
#   这两个在原版就是 Unknown（见 MorphNameMapping），原版男头与 xxfemale 都是空帧 —— 空是对的。
ANIM_FRAMES = [n for n in range(60, 101) if n not in (90, 92)]

# 件级判据：帧号 → (检查名, 最少移动点占比, 最小最大位移 mm)
PART_FRAMES = [
    (60, "眼球转动（f60..63 EyesRight/Left/Up/Down）", 0.50, 4.0),
    (71, "下颌件跟动（f71 JawDrop）",                  0.50, 5.0),
]


def parse_morphmap(text):
    """返回 [(子网格名, 基础顶点数, {帧: dict}), ...]，顺序 = 文件顺序 = 引擎件序。

    🔴 子网格头与帧数据必须**一起锁**：各件帧号相同，
       只锁帧不锁头 → 基础顶点数会被最后一个子网格覆盖（曾把 5229 读成 222）。
    """
    subs = []
    cur_sub, cur_base, cur_frames = None, 0, None
    for line in text.splitlines():
        m = re.match(r"^\s+(head_\S+)\s+帧=\s*(\d+)\s+基础顶点=\s*(\d+)", line)
        if m:
            if cur_sub is not None:
                subs.append((cur_sub, cur_base, cur_frames))
            cur_sub, cur_base, cur_frames = m.group(1), int(m.group(3)), {}
            continue
        m = re.match(r"^\s*f(\d+)\s+(\S+)\s+(\d+)\s+([\d.]+)\s+"
                     r"\(([-\d.]+),([-\d.]+),([-\d.]+)\)"
                     # 🔴 标签写成 \S*?= 而不是字面「位移最大点=」：不依赖中文，换编码也不会失效
                     r"(?:\s+\S*?=\(([-\d.]+),([-\d.]+),([-\d.]+)\))?", line)
        if m and cur_frames is not None:
            cur_frames[int(m.group(1))] = dict(
                name=m.group(2), moved=int(m.group(3)), maxmm=float(m.group(4)),
                cx=float(m.group(5)), cy=float(m.group(6)), cz=float(m.group(7)),
                mx=float(m.group(8)) if m.group(8) else None,
                my=float(m.group(9)) if m.group(9) else None,
                mz=float(m.group(10)) if m.group(10) else None,
            )
            continue
        # 🔴「未形变」行没有坐标元组 —— 不单独认它，这些帧会被当成"缺帧"（措辞误导，
        #    把"通道是空的"报成"帧不存在"）。认下来记 moved=0，判据措辞才对得上。
        m = re.match(r"^\s*f(\d+)\s+(\S+)\s+0\s+([\d.]+)\s+\S*未形变", line)
        if m and cur_frames is not None:
            cur_frames[int(m.group(1))] = dict(
                name=m.group(2), moved=0, maxmm=float(m.group(3)),
                cx=None, cy=None, cz=None, mx=None, my=None, mz=None,
            )
    if cur_sub is not None:
        subs.append((cur_sub, cur_base, cur_frames))
    return subs


def run_morphmap(cli, packdir, filt):
    """跑 tpaccli 并**按字节收**再自适应解码。

    🔴 tpaccli 是 .NET 控制台程序，输出走系统 OEM 代码页（简中是 GBK），不是 UTF-8。
       直接 encoding="utf-8" 读会得到 U+FFFD 乱码 —— 数值还好，中文标签（位移最大点）就匹配不上了。
    """
    cmd = [cli, "morphmap", "--packdir", packdir, "--filter", filt]
    p = subprocess.run(cmd, capture_output=True)
    raw = (p.stdout or b"") + (p.stderr or b"")
    for enc in ("utf-8", "gbk", "cp936", "latin-1"):
        try:
            return raw.decode(enc)
        except UnicodeDecodeError:
            continue
    return raw.decode("utf-8", errors="replace")


def check_frame_landmarks(frames, table, bad, has_max_point=True):
    for f, nm, kind, thr in table:
        d = frames.get(f)
        if d is None:
            bad.append("f%d %s：帧缺失" % (f, nm))
            print("%-4d %-16s %-40s %s" % (f, nm, "(缺帧)", "❌"))
            continue
        if d["moved"] == 0:
            bad.append("f%d %s：帧里没有位移（通道是空的）" % (f, nm))
            print("%-4d %-16s %-40s %s" % (f, nm, "(未形变)", "❌"))
            continue
        if kind == "cy":
            ok = d["cy"] >= thr
            got = "质心 y=%+.3f（移动 %d 点）" % (d["cy"], d["moved"])
            rule = "质心 y ≥ %+.2f（面部正面）" % thr
        elif kind == "ear":
            xmin, ymax = thr
            ok = d["mx"] is not None and abs(d["mx"]) >= xmin and d["my"] <= ymax
            got = "位移最大点 x=%+.3f y=%+.3f" % (d["mx"], d["my"]) if d["mx"] is not None else "(无)"
            rule = "|x| ≥ %.2f 且 y ≤ %+.2f（在两侧）" % (xmin, ymax)
        elif kind == "fy_min":
            ythr, zthr = thr
            ok = d["my"] is not None and d["my"] >= ythr and d["mz"] >= zthr
            got = "最大点 y=%+.3f z=%.3f" % (d["my"], d["mz"]) if d["my"] is not None else "(无)"
            rule = "最大点 y ≥ %+.2f 且 z ≥ %.2f（上半脸）" % (ythr, zthr)
        elif kind == "fy_max":
            ythr, zthr = thr
            ok = d["my"] is not None and d["my"] >= ythr and d["mz"] <= zthr
            got = "最大点 y=%+.3f z=%.3f" % (d["my"], d["mz"]) if d["my"] is not None else "(无)"
            rule = "最大点 y ≥ %+.2f 且 z ≤ %.2f（下半脸/嘴）" % (ythr, zthr)
        elif kind == "fz_min":
            ok = d["mz"] is not None and d["mz"] >= thr
            got = "最大点 z=%.3f" % d["mz"] if d["mz"] is not None else "(无)"
            rule = "最大点 z ≥ %.2f（眼部高度）" % thr
        elif kind in ("x_pos", "x_neg"):
            ok = d["mx"] is not None and (d["mx"] >= thr if kind == "x_pos" else d["mx"] <= -thr)
            got = "最大点 x=%+.3f" % d["mx"] if d["mx"] is not None else "(无)"
            rule = "最大点 x %s %.2f（%s）" % ("≥ +" if kind == "x_pos" else "≤ −", thr,
                                             "右眼在 +x" if kind == "x_pos" else "左眼在 −x")
        else:
            raise AssertionError(kind)
        print("%-4d %-16s %-40s %s %s" % (f, nm, got, rule, "✅" if ok else "❌"))
        if not ok:
            bad.append("f%d %s：%s" % (f, nm, got))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--packdir")
    ap.add_argument("--filter", default="head_")
    ap.add_argument("--morphmap", help="直接用已存的 morphmap 文本（不跑 tpaccli）")
    ap.add_argument("--tpaccli", default=TPACCLI)
    a = ap.parse_args()

    cli = a.tpaccli

    if a.morphmap:
        text = io.open(a.morphmap, encoding="utf-8", errors="replace").read()
    elif a.packdir:
        if not os.path.exists(cli):
            print("FAIL: 找不到 tpaccli：%s（先 dotnet build -c Release）" % cli)
            return 1
        text = run_morphmap(cli, a.packdir, a.filter)
    else:
        print("需要 --packdir 或 --morphmap")
        return 1

    subs = parse_morphmap(text)
    if not subs:
        print("FAIL: 没解析到任何 morphmap 帧数据（包/过滤器对不对？）")
        print(text[:800])
        return 1
    sub, base, frames = subs[0]          # 第一个子网格 = 脸壳
    print("== 脸壳子网格 %s  基础顶点 %d  帧 %d   其余件 %s =="
          % (sub, base, len(frames), [s[0] for s in subs[1:]]))
    print()
    bad = []

    # ---- 脸形段（1..59）：地标 + 全覆盖 ----
    print("---- 脸形段 1..59（捏脸拉杆驱动）----")
    check_frame_landmarks(frames, LANDMARKS, bad)
    empty = [f for f in FACE_FRAMES if frames.get(f) is None or frames[f]["moved"] == 0]
    print("帧 1..59 全覆盖：%s" % ("✅ 59/59 都有位移" if not empty else "❌ 空帧 %s" % empty))
    if empty:
        bad.append("脸形帧 %s 无位移（通道被削没）" % empty)

    # ---- 表情段（60..100）：地标 + 全覆盖 ----
    print("\n---- 表情/口型段 60..100（引擎 morph_anims 驱动）----")
    check_frame_landmarks(frames, ANIM_LANDMARKS, bad)
    empty_anim = [f for f in ANIM_FRAMES if frames.get(f) is None or frames[f]["moved"] == 0]
    print("帧 60..100（f90/f92 原版本就空）全覆盖：%s"
          % ("✅ %d/%d 都有位移" % (len(ANIM_FRAMES), len(ANIM_FRAMES))
             if not empty_anim else "❌ 空帧 %s" % empty_anim))
    if empty_anim:
        bad.append("表情帧 %s 无位移（= 说话不张嘴、脸没表情；成因：只搬了 1..59）" % empty_anim)

    # ---- 件级：眼球要能转、下颌件要跟着动（脸壳的场套不到它们头上）----
    print("\n---- 件级（除脸壳外，每个件带自己的位移场）----")
    for f, label, min_ratio, min_mm in PART_FRAMES:
        hit = None
        detail = []
        for s_name, s_base, s_frames in subs[1:]:
            d = s_frames.get(f)
            if d is None:
                detail.append("%s:(缺帧)" % s_name)
                continue
            ratio = d["moved"] / float(s_base) if s_base else 0.0
            detail.append("%s: %d/%d=%.0f%% · %.2fmm" % (s_name, d["moved"], s_base, ratio * 100, d["maxmm"]))
            if ratio >= min_ratio and d["maxmm"] >= min_mm:
                hit = s_name
        ok = hit is not None or not subs[1:]
        print("f%-3d %-34s %s %s" % (f, label, " ✅ " + hit if hit else "❌ 无件满足",
                                     "  判据：某件 ≥%.0f%% 顶点且最大 ≥%.1fmm ｜ 实测 %s"
                                     % (min_ratio * 100, min_mm, "; ".join(detail) or "无其余件")))
        if not ok:
            bad.append("f%d %s：没有任何附属件达标（%s）" % (f, label, "; ".join(detail)))

    d = frames.get(GLOBAL_FRAME[0])
    if d is not None and base:
        print("\n（参考）f%d %s 牵动 %d/%d = %.0f%% —— 只作参考，不同头几何占比不同，不判过不过"
              % (GLOBAL_FRAME[0], GLOBAL_FRAME[1], d["moved"], base, 100.0 * d["moved"] / base))

    print()
    if bad:
        print("❌ 不通过 —— 位移场缺失或落在错误部位（典型成因：移植时前后/左右镜像、只搬了 1..59、或换了错误的通道源）")
        for b in bad:
            print("   · " + b)
        print("\n修法：重跑 transfer_channels.py：脸形段源 = head_xxfemale_a 的 dump（--src），"
              "表情段源 = 原版同性别头的 dump（--anim-src + --anim-objects，按件对位）；"
              "配方见 build_head_chain.py")
        return 1
    print("✅ 通过：脸形 %d 个地标 + 表情 %d 个地标 + 件级 %d 项，全部落在正确部位"
          % (len(LANDMARKS), len(ANIM_LANDMARKS), len(PART_FRAMES)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
