#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""无头截图验收：?shot=1 把画布塞进 #shotdata，--dump-dom 取回解码成 PNG"""
import subprocess, re, base64, sys, os, json, time
CHROME = r"C:/Program Files/Google/Chrome/Application/chrome.exe"
PORT = os.environ.get("PORT", "8810")
OUT = os.environ.get("SHOT_OUT") or os.path.join(os.path.dirname(os.path.abspath(__file__)), "verify")
os.makedirs(OUT, exist_ok=True)

def shot(clip, t, tag, extra=""):
    url = "http://127.0.0.1:%s/viewer.html?shot=1&clip=%s&t=%s%s" % (PORT, clip, t, extra)
    cmd = [CHROME, "--headless=old", "--disable-gpu", "--enable-unsafe-swiftshader",
           "--no-sandbox", "--window-size=1400,900",
           "--virtual-time-budget=200000", "--dump-dom", url]
    t0 = time.time()
    with open(os.path.join(OUT, tag + ".dom.html"), "wb") as f:
        r = subprocess.run(cmd, stdout=f, stderr=subprocess.PIPE)
    dom = open(os.path.join(OUT, tag + ".dom.html"), "rb").read().decode("utf-8", "replace")
    m = re.search(r'id="shotdata"[^>]*>([^<]+)<', dom)
    png = os.path.join(OUT, tag + ".png")
    if not m:
        err = re.search(r'载入失败[^<]*', dom)
        print("%-22s ✗ 没拿到 #shotdata  (%.0fs) %s" % (tag, time.time()-t0, err.group(0)[:90] if err else ""))
        return None
    b = m.group(1).strip()
    data = base64.b64decode(b.split(",")[-1])
    open(png, "wb").write(data)
    print("%-22s ✓ %s  (%.0f KB, %.0fs)" % (tag, os.path.basename(png), len(data)/1024, time.time()-t0))
    return png

if __name__ == "__main__":
    jobs = json.loads(sys.argv[1]) if len(sys.argv) > 1 else [
        ["002_01", "0.4", "g_002_01_行走", ""],
        ["013_17", "0.5", "g_013_17", ""],
        ["002_06", "0.5", "s_002_06_跳跃", ""],
        ["Anim_CS_SP", "0.4", "g_Anim_CS_SP", ""],
    ]
    for j in jobs:
        shot(*j)
