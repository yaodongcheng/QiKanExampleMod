#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""RDR2Map 批量导入驱动（v2 · 2026-11-11）。

设计要点（对应用户要求）：
  · **每批一个独立控制台窗口** —— 能看见实时日志，也能按 F12 / Ctrl+Alt+F12 急停。
  · **F12 会被严格执行** —— ps1 用退出码 20 表示「用户主动停止」，本驱动见到 20
    或发现停止牌 `%TEMP%\\RDR2_IMPORT_STOP` 就**立刻整体收工**，绝不自动重试。
  · **批大小**：贴图 800 / 网格 450（旧文档实测：贴图约 1,000、网格约 560 会崩）。
  · **断点续跑**：按 Assets 里已有的 `_tex.tpac` / `_geo.tpac` 名字跳过。
  · **失败处理**：非用户原因失败 → 重启重试一次；连续 3 次失败 → 中止并报告。

用法：
  python Scripts/run_rdr2map_import.py --stage textures
  python Scripts/run_rdr2map_import.py --stage meshes
  python Scripts/run_rdr2map_import.py --stage textures --batch 100 --limit 1   # 冒烟
  # 想中途停：直接在跑批的控制台按 F12 / Ctrl+Alt+F12，
  #           或手工建立 %TEMP%\\RDR2_IMPORT_STOP
"""
import argparse
import csv
import io
import os
import shutil
import subprocess
import sys
import time

LWN = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
MAP = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\RDR2Map"
IMP = os.path.join(LWN, "Scripts", "rdr2_modkit_batch_import.ps1")
STAGE_ROOT = os.path.join(MAP, "AssetSources", "ImportReady")
STAGING = os.path.join(MAP, "AssetSources", "_staging")
ASSETS_TEX = os.path.join(MAP, "Assets", "textures")
ASSETS_MDL = os.path.join(MAP, "Assets", "meshes")
BATCH_DIR = os.path.join(MAP, "AssetSources", "_batches")
RUN_TAG = time.strftime("%y%m%d_%H%M%S")      # 每次运行的唯一标记，防止清单互相覆盖
LOG = os.path.join(LWN, "Debug", "RD2构造相关", "rdr2map_import_run.log")
STOP_FLAG = os.path.join(os.environ.get("TEMP", r"C:\Windows\Temp"), "RDR2_IMPORT_STOP")

TEX_CHANNELS = ("albedo", "normal", "specular")
MESH_CLASSES = ("main_map_lod", "bldg", "rail", "prop", "part", "interior",
                "unknown", "junk", "veh", "struct", "ruin", "rock", "stone")
EXIT_STOP = 20          # ps1: STOPPED_BY_REQUEST


def log(msg):
    line = "[%s] %s" % (time.strftime("%H:%M:%S"), msg)
    print(line, flush=True)
    os.makedirs(os.path.dirname(LOG), exist_ok=True)
    with io.open(LOG, "a", encoding="utf-8") as f:
        f.write(line + "\n")


def stopped():
    return os.path.exists(STOP_FLAG)


# ---- 2026-11-12：驱动自己也监听 F12 ----
# 原因：ps1 原来只看 GetAsyncKeyState 的 0x0001 位，那个位会被任何进程的下一次调用清掉，
# 所以「按了 F12 没反应」。现在驱动在自己的 2 秒轮询里做**边沿检测**，
# 不依赖 ps1 是否活着、也不依赖系统的 0x0001 位。
try:
    _u32 = ctypes.WinDLL("user32", use_last_error=True)
    _u32.GetAsyncKeyState.restype = ctypes.c_short
    _u32.GetAsyncKeyState.argtypes = [ctypes.c_int]
except Exception:                                    # 非 Windows 或受限环境
    _u32 = None


def f12_down():
    """F12 是否按下。

    🔴 2026-11-12 修：原来只看 0x8000（当前处于按下状态），而驱动是 1 秒轮询一次，
       用户"快速点一下"F12（<1 秒）大概率落在两次轮询之间 → 完全检测不到，
       表现就是「F12 失效」。这正是 10:02~10:10 那轮崩溃重试时无法急停的原因。
       现在同时看 0x0001（自上次调用以来按下过）——这个位就是为"抓点按"设计的，
       而且驱动是这个进程里唯一的调用者，不会被别的进程清掉。
    """
    if _u32 is None:
        return False
    return (_u32.GetAsyncKeyState(0x7B) & 0x8001) != 0


def set_stop_flag(why):
    try:
        with io.open(STOP_FLAG, "w", encoding="ascii") as f:
            f.write("%s %s" % (time.strftime("%Y-%m-%dT%H:%M:%S"), why))
    except Exception:
        pass


def kill_tree(pid):
    """杀整棵进程树：ps1 的孙进程（启动器/游戏）也要死。"""
    subprocess.run(["taskkill", "/T", "/F", "/PID", str(pid)], capture_output=True)
    kill_editor()


def done_stems(root, suffix):
    out = set()
    if not os.path.isdir(root):
        return out
    for dp, _, fn in os.walk(root):
        for f in fn:
            if f.lower().endswith(suffix):
                out.add(f[: -len(suffix)].lower())
    return out


def chunk_balanced(files, batch, hard_cap=1100):
    """尽量把 N 个文件分成**大小接近**的 k 批，而不是"满批 + 一个尾巴"。

    2026-11-12 用户要求：最后的 68 张不值得单开一批（要重启一次编辑器，2 分钟）。
    规则：
      · 上限取 min(batch*1.15, hard_cap)。hard_cap=1100 来自 1600 批崩溃实测
        （约 1,212 张时编辑器崩），留 8% 余量。
      · k = ceil(N / 上限)，然后 N 平均分给 k 批（余数摊给前几批）。
    """
    n = len(files)
    if n == 0:
        return []
    limit = max(1, min(int(batch * 1.15), hard_cap))
    k = (n + limit - 1) // limit
    k = max(1, k)
    size, rem = divmod(n, k)
    out, i = [], 0
    for j in range(k):
        s = size + (1 if j < rem else 0)
        out.append(files[i:i + s])
        i += s
    return out


def tpac_stems(kind):
    """当前 Assets 里已导入的资产名（小写）。"""
    if kind == "texture":
        return done_stems(ASSETS_TEX, "_tex.tpac")
    return done_stems(ASSETS_MDL, "_geo.tpac")


def write_batch_manifest(tag, idx, files, kind, result, t0, t1):
    """每批写一个清单 CSV + 追加总索引，逐文件标 ok / missing。"""
    os.makedirs(BATCH_DIR, exist_ok=True)
    have = tpac_stems(kind)
    # 2026-11-12 修：文件名必须带「本次运行」的时间戳。
    # 原来只按 tag+序号命名，第二次运行会**覆盖**第一次的清单
    # （实测：specular 的 800 批次清单被后来的 1600 批次覆盖，历史丢失）。
    path = os.path.join(BATCH_DIR, "%s_%s_%02d.csv" % (tag, RUN_TAG, idx))
    ok = 0
    stamp = time.strftime("%Y-%m-%dT%H:%M:%S", time.localtime(t1))
    with io.open(path, "w", encoding="utf-8-sig", newline="") as fh:
        w = csv.writer(fh)
        w.writerow(["file", "bytes", "status", "checked_at"])
        for src in files:
            stem = os.path.splitext(os.path.basename(src))[0]
            st = "ok" if stem.lower() in have else "missing"
            if st == "ok":
                ok += 1
            try:
                sz = os.path.getsize(src)
            except OSError:
                sz = ""
            w.writerow([os.path.basename(src), sz, st, stamp])
    with io.open(os.path.join(BATCH_DIR, "index.csv"), "a", encoding="utf-8-sig", newline="") as fh:
        w = csv.writer(fh)
        w.writerow([tag, idx, len(files), ok, result,
                    time.strftime("%H:%M:%S", time.localtime(t0)),
                    time.strftime("%H:%M:%S", time.localtime(t1)),
                    os.path.basename(path)])
    return ok


def write_snapshot():
    """全量快照：已导入 / 未导入，按通道与分类分文件。"""
    os.makedirs(BATCH_DIR, exist_ok=True)
    rows_done, rows_pend = [], []
    for kind, roots in (("texture", (("textures\\albedo", ASSETS_TEX, "_tex.tpac", ".dds"),
                                     ("textures\\normal", ASSETS_TEX, "_tex.tpac", ".dds"),
                                     ("textures\\specular", ASSETS_TEX, "_tex.tpac", ".dds"))),
                        ("mesh", tuple(("meshes\\" + c, ASSETS_MDL, "_geo.tpac", ".trf")
                                       for c in MESH_CLASSES))):
        have = tpac_stems(kind)
        for dest_rel, _, _, ext in roots:
            d = os.path.join(STAGE_ROOT, dest_rel.replace("\\", os.sep))
            if not os.path.isdir(d):
                continue
            for f in sorted(os.listdir(d)):
                if not f.lower().endswith(ext):
                    continue
                stem = os.path.splitext(f)[0]
                (rows_done if stem.lower() in have else rows_pend).append((dest_rel, f))
    for name, rows in (("imported_all.csv", rows_done), ("pending_all.csv", rows_pend)):
        with io.open(os.path.join(BATCH_DIR, name), "w", encoding="utf-8-sig", newline="") as fh:
            w = csv.writer(fh)
            w.writerow(["dest", "file"])
            w.writerows(rows)
        log("  快照 %-18s %6d 行 -> %s" % (name, len(rows), os.path.join(BATCH_DIR, name)))


def kill_editor():
    subprocess.run(["taskkill", "/F", "/IM", "TaleWorlds.MountAndBlade.Launcher.exe"],
                   capture_output=True)
    subprocess.run(["taskkill", "/F", "/IM", "Watchdog.exe"], capture_output=True)
    time.sleep(3)


def start_watchdog():
    """拉起独立热键看守：100ms 轮询 F12，命中就写停止牌并杀整棵树。

    为什么必须独立：GUI 自动化脚本会抢焦点，它内部的 F12 轮询 6~8 秒一次，
    而且驱动被杀后 ps1 仍会继续抢鼠标（2026-11-12 两次事故）。
    """
    ps1 = os.path.join(LWN, "Scripts", "rdr2_hotkey_watchdog.ps1")
    if not os.path.isfile(ps1):
        log("  WARN 找不到热键看守脚本 %s（F12 只能靠驱动自身轮询）" % ps1)
        return None
    ps = os.path.join(os.environ.get("SystemRoot", r"C:\Windows"),
                      "System32", "WindowsPowerShell", "v1.0", "powershell.exe")
    if not os.path.isfile(ps):
        ps = "powershell.exe"
    try:
        proc = subprocess.Popen(
            [ps, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ps1,
             "-ParentPid", str(os.getpid()), "-PollMs", "100"],
            creationflags=subprocess.CREATE_NO_WINDOW)
        log("  F12 看守已启动 (pid=%d, 100ms 轮询)" % proc.pid)
        return proc
    except Exception as e:
        log("  WARN 看守启动失败: %s" % e)
        return None


def stop_watchdog(wd):
    if wd is None:
        return
    try:
        if wd.poll() is None:
            wd.terminate()
            try:
                wd.wait(timeout=5)
            except subprocess.TimeoutExpired:
                wd.kill()
    except Exception:
        pass


def run_batch(files, dest_rel, wildcard, kind):
    """跑一批。返回 'ok' / 'stopped' / 'fail'。

    用独立控制台窗口启动 ps1：用户能看见日志、能按 F12。
    """
    if os.path.isdir(STAGING):
        shutil.rmtree(STAGING, ignore_errors=True)
    os.makedirs(STAGING)
    for src in files:
        shutil.copy2(src, os.path.join(STAGING, os.path.basename(src)))

    ps = os.path.join(os.environ.get("SystemRoot", r"C:\Windows"),
                      "System32", "WindowsPowerShell", "v1.0", "powershell.exe")
    if not os.path.isfile(ps):
        ps = "powershell.exe"
    args = [ps, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", IMP,
            "-SourceDir", STAGING, "-DestRel", dest_rel, "-Kind", kind,
            "-ModuleRoot", MAP, "-Wildcard", wildcard, "-TimeoutMin", "90"]

    log("  启动批控制台：%s  %d 个  （F12 / Ctrl+Alt+F12 = 急停）" % (dest_rel, len(files)))
    wd = start_watchdog()
    proc = subprocess.Popen(args, creationflags=subprocess.CREATE_NEW_CONSOLE)
    try:
        return _wait_batch(proc, files, dest_rel, kind)
    finally:
        stop_watchdog(wd)
        # 无论走哪条路，都必须把 ps1 进程树杀干净，否则它会继续抢鼠标
        try:
            if proc.poll() is None:
                kill_tree(proc.pid)
        except Exception:
            pass
        shutil.rmtree(STAGING, ignore_errors=True)


def _wait_batch(proc, files, dest_rel, kind):
    prev_f12 = f12_down()
    while True:
        rc = proc.poll()
        if rc is not None:
            break
        # ① 驱动自己检测 F12 的"按下沿"（不依赖 ps1，也不依赖 0x0001 位）
        cur_f12 = f12_down()
        if cur_f12 and not prev_f12:
            log("  F12 按下 → 写停止牌 + 杀整棵进程树")
            set_stop_flag("F12")
            kill_tree(proc.pid)
            return "stopped"
        prev_f12 = cur_f12
        # ② 停止牌（外部写入，或 ps1 自己写的）
        if stopped():
            log("  !! 检测到停止牌 → 中止本批（连游戏一起杀）")
            kill_tree(proc.pid)
            return "stopped"
        time.sleep(0.25)
    if rc == EXIT_STOP:
        return "stopped"
    return "ok" if rc == 0 else "fail"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--stage", choices=("textures", "meshes", "all"), required=True)
    ap.add_argument("--batch", type=int, default=0, help="0 = 按阶段默认（贴图 800 / 网格 450）")
    ap.add_argument("--limit", type=int, default=0, help="只跑前 N 个批次（冒烟用）")
    ap.add_argument("--snapshot", action="store_true", help="只生成 已导入/未导入 快照，不导入")
    args = ap.parse_args()

    if args.snapshot:
        write_snapshot()
        return 0
    if stopped():
        log("停止牌已存在（%s）→ 先删掉它再跑" % STOP_FLAG)
        return 3

    batch = args.batch or (450 if args.stage == "meshes" else 800)
    log("=" * 72)
    log("开始 stage=%s 批大小=%d limit=%s" % (args.stage, batch, args.limit or "-"))
    log("急停：在批控制台按 F12 / Ctrl+Alt+F12，或建立 %s" % STOP_FLAG)

    tex_have = done_stems(ASSETS_TEX, "_tex.tpac")
    mdl_have = done_stems(ASSETS_MDL, "_geo.tpac")

    jobs = []
    if args.stage in ("textures", "all"):
        for ch in TEX_CHANNELS:
            d = os.path.join(STAGE_ROOT, "textures", ch)
            if os.path.isdir(d):
                files = [os.path.join(d, f) for f in sorted(os.listdir(d)) if f.lower().endswith(".dds")]
                jobs.append(("textures\\%s" % ch, "*.dds", "texture", files, tex_have))
    if args.stage in ("meshes", "all"):
        for c in MESH_CLASSES:
            d = os.path.join(STAGE_ROOT, "meshes", c)
            if os.path.isdir(d):
                files = [os.path.join(d, f) for f in sorted(os.listdir(d)) if f.lower().endswith(".trf")]
                jobs.append(("meshes\\%s" % c, "*.trf", "mesh", files, mdl_have))

    total_pend = 0
    plan = []
    for dest_rel, wildcard, kind, files, have in jobs:
        pend = [f for f in files if os.path.splitext(os.path.basename(f))[0].lower() not in have]
        total_pend += len(pend)
        log("%-22s 源 %6d  已导 %6d  待导 %6d  → %d 批"
            % (dest_rel, len(files), len(files) - len(pend), len(pend),
               len(chunk_balanced(pend, batch))))
        for chunk in chunk_balanced(pend, batch):
            plan.append((dest_rel, wildcard, kind, chunk))
    log("待导合计 %d 个 / 约 %d 批" % (total_pend, (total_pend + batch - 1) // batch))

    if args.limit:
        plan = plan[: args.limit]
    plan = [p for p in plan if p[3]]
    if not plan:
        log("没有待导批次，收工")
        return 0
    log("本次要跑 %d 个批次" % len(plan))

    fails = 0
    seq = {}
    for idx, (dest_rel, wildcard, kind, files) in enumerate(plan, 1):
        if stopped():
            log("检测到停止牌 → 整体收工（第 %d/%d 批前）" % (idx, len(plan)))
            return 3
        log("[%d/%d] %s  %d 个" % (idx, len(plan), dest_rel, len(files)))
        t0 = time.time()
        st = run_batch(files, dest_rel, wildcard, kind)
        tag = dest_rel.replace("\\", "_")
        seq[tag] = seq.get(tag, 0) + 1
        okn = write_batch_manifest(tag, seq[tag], files, kind, st, t0, time.time())
        log("  清单 -> _batches\\%s_%s_%02d.csv  逐文件 ok %d/%d"
            % (tag, RUN_TAG, seq[tag], okn, len(files)))
        if okn < len(files):
            log("  ⚠️ PARTIAL：本批只导进 %d / %d，缺 %d（下次启动会自动补）"
                % (okn, len(files), len(files) - okn))
        if st == "stopped":
            log("用户急停 → 整体收工（已导的可续跑）")
            return 3
        if st == "fail":
            fails += 1
            log("  失败（连续第 %d 次）→ 重启重试一次" % fails)
            kill_editor()
            st = run_batch(files, dest_rel, wildcard, kind)
            if st == "stopped":
                log("用户急停 → 整体收工")
                return 3
            if st == "fail":
                if fails >= 3:
                    log("连续 %d 次失败 → 中止，等你处理" % fails)
                    return 2
                log("  重试仍失败，跳过本批")
                continue
        fails = 0
        log("  完成，用时 %.1f 分钟" % ((time.time() - t0) / 60.0))

    write_snapshot()
    log("全部批次完成")
    return 0


if __name__ == "__main__":
    sys.exit(main())
