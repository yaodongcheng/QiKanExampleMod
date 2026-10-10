#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""源 WAV → LivingWorldNpcs/ModuleSounds/：**改名 + 响度规整**（生成器，产物勿手改）。

🔴 为什么需要"规整"而不是游戏内调音量：引擎**没有音量 API**（SoundEvent 全成员无 SetVolume，
反编译实证；见 Knowledge/骑砍2音效系统_引擎能力与实现.md §一.2）⇒ 响度只能烘进素材。

🔴 为什么必须从**源目录**重新读（而不是就地放大现有文件）：
   就地放大重跑 = 叠加两次增益（产物会被烧糊）。本脚本以 `SRC` 为唯一真源，
   每次运行都是"源 → 处理 → 覆盖产物"，**幂等**、参数改了直接重跑（铁律 22：生成物禁止手改）。

实测（2026-10-10 首跑前的原始状态）：6 条风里 4 条（1/4/5/6）短时 RMS 只有 -23~-25 dB，
另两条 -6~-7 dB —— 相差 ~19 dB，听感就是"偶尔有一阵大的、其余几乎听不见"。
本脚本把每条都拉到同一目标（顺带消掉这个差），并对绳声做压缩（它峰值已顶 0 dB，只能靠压缩提响度）。

用法：
    python Scripts/gen_lwn_module_sounds.py            # 生成（覆盖 ModuleSounds/ 里对应文件）
    python Scripts/gen_lwn_module_sounds.py --dry-run  # 只看前后响度表，不写文件
"""
import argparse
import os
import sys
import wave

import numpy as np
from scipy.ndimage import maximum_filter1d

# ── 源（只读；改素材请改这里，别改产物）──
SRC = r"D:\BrainMaker\骑砍2动画重定向\UEAnims"

# ── 产物 ──
_REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DST = os.path.join(_REPO, "ModuleSounds")

# ── 每条：源文件名 → 产物名 → 目标短时 RMS（dBFS）→ 压缩阈值（dB，None = 不压）→ 压缩比 ──
# 目标口径：短时（50 ms 滑窗）RMS 的**最大值** ≈ 听感响度。
# 参考：一般游戏音效 -12~-18、前景重音 -8~-10。用户 2026-10-10 反馈"都太小" ⇒ 取偏响的一档。
# 🔴 这些素材**峰值/RMS 比很高**（尖瞬态）：不压缩只整体放大 ⇒ 一放大就顶破封顶、响度上不去
#     （首版实测：4 条风声只能到 -12~-16）。压缩把峰均比压下来，才有提响度的空间。
FILES = [
    # 源文件              产物名                      目标    阈值   比例
    ("RopeGrapple.WAV", "lwn_grapple_rope.wav",  -7.0, -18.0, 5.0),   # 甩绳：峰值已顶 0 dB ⇒ 较重压缩
    ("clothwind1.WAV",  "lwn_flight_wind_1.wav", -10.0, -24.0, 3.0),
    ("clothwind2.WAV",  "lwn_flight_wind_2.wav", -10.0, -24.0, 3.0),
    ("clothwind3.WAV",  "lwn_flight_wind_3.wav", -10.0, -24.0, 3.0),
    ("clothwind4.WAV",  "lwn_flight_wind_4.wav", -10.0, -24.0, 3.0),
    ("clothwind5.WAV",  "lwn_flight_wind_5.wav", -10.0, -24.0, 3.0),
    ("clothwind6.WAV",  "lwn_flight_wind_6.wav", -10.0, -24.0, 3.0),
]
ATTACK_MS = 3.0      # 起音（短 = 保住瞬态）
RELEASE_MS = 120.0   # 释放（长 = 不抽气）

CEILING_DB = -1.0    # 最终封顶（超出的样本硬限幅；瞬态上几个样本听不出来）
ST_WINDOW_MS = 50.0  # 短时 RMS 窗口


def read_wav(path):
    with wave.open(path, "rb") as w:
        sr = w.getframerate()
        ch = w.getnchannels()
        sw = w.getsampwidth()
        n = w.getnframes()
        raw = w.readframes(n)
    if sw != 2:
        raise SystemExit(f"{path}: 只支持 16bit PCM（现状 {sw * 8}bit）——先转格式再跑")
    d = np.frombuffer(raw, dtype="<i2").astype(np.float32) / 32768.0
    return d.reshape(-1, ch), sr


def write_wav(path, data, sr):
    """data = (n, ch) float32 → 16bit PCM。"""
    n, ch = data.shape
    x = np.clip(data, -1.0, 1.0)
    raw = (x * 32767.0).round().astype("<i2").tobytes()
    with wave.open(path, "wb") as w:
        w.setnchannels(ch)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(raw)


def db(x):
    return 20.0 * np.log10(max(float(x), 1e-9))


def st_peak_rms(mono, sr):
    """短时（ST_WINDOW_MS）RMS 的滑动最大值 —— 听感响度的代理。"""
    win = max(1, int(sr * ST_WINDOW_MS / 1000.0))
    if len(mono) <= win:
        return float(np.sqrt((mono ** 2).mean()))
    c = np.convolve(mono.astype(np.float64) ** 2, np.ones(win) / win, mode="valid")
    return float(np.sqrt(c.max()))


def envelope(mono, sr):
    """包络跟随（起音/释放指数平滑）—— 压缩器的检波器。"""
    a_att = np.exp(-1.0 / max(1.0, sr * ATTACK_MS / 1000.0))
    a_rel = np.exp(-1.0 / max(1.0, sr * RELEASE_MS / 1000.0))
    env = np.empty(len(mono), dtype=np.float64)
    e = 0.0
    for i in range(len(mono)):
        v = abs(float(mono[i]))
        e = a_att * e + (1.0 - a_att) * v if v > e else a_rel * e + (1.0 - a_rel) * v
        env[i] = e
    return env


def compress(mono, sr, thr_db, ratio):
    """压缩：**只有超过阈值的部分**按 ratio 压，阈值以下原样（增益 = 1）。返回逐样本增益。

    🔴 别写成 `target = thr + over/ratio` —— 那样 env < thr 时 gain = thr/env > 1，
       等于把安静段（尾巴/底噪）整个顶到阈值的电平（首版就栽在这，绳声被算成 -15.6 dB）。
    """
    thr = 10.0 ** (thr_db / 20.0)
    env = np.maximum(envelope(mono, sr), 1e-9)
    target = np.where(env > thr, thr + (env - thr) / ratio, env)
    return target / env


def limit(y, sr, ceiling_db, release_ms=60.0):
    """**前视限幅**：用 3 ms 前视窗（`maximum_filter1d` 居中 ⇒ 增益在峰值到来前就开始压），
    把 |y| 钉在封顶以下；增益恢复走 60 ms 释放（防抽气）。

    🔴 为什么必须前视：单极点包络（3 ms 起音）**追不上单样本尖峰** —— 压缩器看不见瞬态、
    增益在尖峰处仍是 1 ⇒ 峰均比降不下来、响度提不上去（首版实测：4 条风声卡在 -12~-18）。
    """
    ceil = 10.0 ** (ceiling_db / 20.0)
    absmax = np.abs(y).max(axis=1) if y.ndim > 1 else np.abs(y)
    win = maximum_filter1d(absmax, size=max(3, int(sr * 0.003)), mode="nearest")
    gain = np.minimum(1.0, ceil / np.maximum(win, 1e-9))

    a = np.exp(-1.0 / max(1.0, sr * release_ms / 1000.0))
    sm = np.empty_like(gain)
    g = 1.0
    for i in range(len(gain)):
        v = gain[i]
        g = v if v < g else a * g + (1.0 - a) * v   # 往下立刻压、往上慢慢放
        sm[i] = g
    return y * sm[:, None] if y.ndim > 1 else y * sm


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true", help="只看响度表，不写文件")
    args = ap.parse_args()

    if not os.path.isdir(SRC):
        raise SystemExit(f"源目录不存在：{SRC}（改了素材位置就改脚本里的 SRC）")
    os.makedirs(DST, exist_ok=True)

    print(f"源  : {SRC}")
    print(f"产物: {DST}")
    print(f"{'产物名':26s} {'处理前':>9s} {'处理后':>9s} {'限幅样本':>9s}")
    ok = True
    for src_name, dst_name, target_db, thr_db, ratio in FILES:
        src_path = os.path.join(SRC, src_name)
        if not os.path.isfile(src_path):
            print(f"  [ERROR] 源文件缺失：{src_path}")
            ok = False
            continue

        data, sr = read_wav(src_path)
        mono = data.mean(axis=1)
        before = db(st_peak_rms(mono, sr))

        # 链路：压缩（压峰均比）→ 缩放到目标短时 RMS → **前视限幅**（保证不顶破封顶）
        gain = compress(mono, sr, thr_db, ratio) if thr_db is not None else np.ones(len(mono))
        y = data * gain[:, None]
        cur = st_peak_rms(y.mean(axis=1), sr)
        y = y * (10.0 ** ((target_db - db(cur)) / 20.0))
        y = limit(y, sr, CEILING_DB)

        after = db(st_peak_rms(y.mean(axis=1), sr))
        peak_after = db(np.abs(y).max())

        if not args.dry_run:
            write_wav(os.path.join(DST, dst_name), y, sr)
        print(f"{dst_name:26s} {before:6.1f} dB {after:6.1f} dB   peak {peak_after:6.1f} dB")

    if not ok:
        sys.exit(1)
    print("完成" + ("（dry-run，未写文件）" if args.dry_run else ""))


if __name__ == "__main__":
    main()
