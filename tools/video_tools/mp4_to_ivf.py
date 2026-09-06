# -*- coding: utf-8 -*-
"""mp4 → 骑砍2 开场视频三件套（ ivf[VP8] + ogg[vorbis] ）转换脚本。

参考格式（已验证：MB2 1.2.12 原版 TWLogo 与织丰 Shokuho_4K）：
  - video: VP80 (VP8) in IVF container, 30 fps（原版 1920x1080 / 织丰 3840x2160）
  - audio: vorbis in ogg

用法：
  python tools/video_tools/mp4_to_ivf.py <源.mp4> --name MyMod_4K \
      [--width 3840] [--height 2160] [--fps 30] [--outdir out]
  输出：<outdir>/<name>.ivf + <outdir>/<name>.ogg，并打印 ivf 头校验报告。

ffmpeg 查找顺序：tools/video_tools/ffmpeg*/ffmpeg.exe → PATH。
"""
import argparse
import os
import shutil
import struct
import subprocess
import sys
import zipfile
import glob

FFMPEG_URL = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
HERE = os.path.dirname(os.path.abspath(__file__))


def find_ffmpeg():
    cand = glob.glob(os.path.join(HERE, "ffmpeg*/**", "ffmpeg.exe"), recursive=True)
    if cand:
        return cand[0]
    # pip 版（imageio-ffmpeg），install: pip install imageio-ffmpeg
    try:
        import imageio_ffmpeg
        return imageio_ffmpeg.get_ffmpeg_exe()
    except ImportError:
        pass
    return shutil.which("ffmpeg")


def ensure_ffmpeg():
    exe = find_ffmpeg()
    if exe:
        return exe
    print("本地未找到 ffmpeg，尝试下载:", FFMPEG_URL)
    zpath = os.path.join(HERE, "ffmpeg.zip")
    subprocess.run(["curl", "-L", "-o", zpath, FFMPEG_URL], check=True)
    with zipfile.ZipFile(zpath) as z:
        z.extractall(HERE)
    os.remove(zpath)
    exe = find_ffmpeg()
    if not exe:
        raise RuntimeError("ffmpeg 下载/解压后仍找不到 ffmpeg.exe")
    return exe


def read_ivf_header(path):
    with open(path, "rb") as f:
        d = f.read(32)
    magic = d[0:4]
    fourcc = d[8:12]
    w, h = struct.unpack("<HH", d[12:16])
    rate, scale = struct.unpack("<II", d[16:24])
    frames, = struct.unpack("<I", d[24:28])
    fps = rate / scale if scale else 0.0
    dur = frames * scale / rate if scale and rate else 0.0
    return {"magic": magic, "fourcc": fourcc, "w": w, "h": h, "fps": fps,
            "frames": frames, "duration_s": round(dur, 2)}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("input", help="源 mp4 路径")
    ap.add_argument("--name", default="MyMod_4K", help="输出文件名（不带扩展名）")
    ap.add_argument("--width", type=int, default=3840)
    ap.add_argument("--height", type=int, default=2160)
    ap.add_argument("--fps", type=int, default=30)
    ap.add_argument("--videobitrate", default="8M", help="VP8 码率（织丰 4K≈8.4Mbps）")
    ap.add_argument("--outdir", default=None, help="输出目录（默认=脚本目录/out）")
    args = ap.parse_args()

    ff = ensure_ffmpeg()
    outdir = args.outdir or os.path.join(HERE, "out")
    os.makedirs(outdir, exist_ok=True)
    ivf_path = os.path.join(outdir, args.name + ".ivf")
    ogg_path = os.path.join(outdir, args.name + ".ogg")
    # 不覆盖已有产物（发现则提示改 --name）
    if os.path.exists(ivf_path) or os.path.exists(ogg_path):
        print("!! 产物已存在:", ivf_path, "或", ogg_path, "-- 换 --name 或先删除")
        sys.exit(1)

    # 1) 视频：VP8 + IVF，30fps，lanczos 缩放到目标分辨率（不放大已有更高分辨率）
    vf = f"scale={args.width}:{args.height}:force_original_aspect_ratio=decrease:flags=lanczos,pad={args.width}:{args.height}:(ow-iw)/2:(oh-ih)/2"
    vcmd = [ff, "-y", "-i", args.input, "-an", "-c:v", "libvpx",
            "-b:v", args.videobitrate, "-r", str(args.fps),
            "-vf", vf, "-f", "ivf", ivf_path]
    print("== 视频:", " ".join(vcmd))
    subprocess.run(vcmd, check=True)

    # 2) 音频：vorbis + ogg
    acmd = [ff, "-y", "-i", args.input, "-vn", "-c:a", "libvorbis",
            "-q:a", "5", ogg_path]
    print("== 音频:", " ".join(acmd))
    subprocess.run(acmd, check=True)

    # 3) 校验
    hdr = read_ivf_header(ivf_path)
    status = "OK" if (hdr["magic"] == b"DKIF" and hdr["fourcc"] == b"VP80") else "UNEXPECTED"
    print("== 校验: %s | %s %dx%d @%.1ffps | %d frames | %.1fs | ivf=%dKB ogg=%dKB"
          % (status, hdr["fourcc"].decode(), hdr["w"], hdr["h"], hdr["fps"],
             hdr["frames"], hdr["duration_s"],
             os.path.getsize(ivf_path) // 1024, os.path.getsize(ogg_path) // 1024))
    if status != "OK":
        sys.exit(2)


if __name__ == "__main__":
    main()
