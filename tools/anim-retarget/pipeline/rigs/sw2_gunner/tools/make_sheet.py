# -*- coding: utf-8 -*-
"""把 src / tgt_align / tgt_delta 的同帧同视角帧图拼成对照表（列=资产，行=帧）。"""
import os, sys
from PIL import Image, ImageDraw, ImageFont

SRC = ["src", "r_biosculpt", "r_bac", "tgt_delt", "tgt_alig"]
LABEL = {"src": "SW2 GUNNER p006 (源)", "r_biosculpt": "BioSculpt 插件", "r_bac": "BoneAnimCopy 插件", "tgt_delt": "自研 delta", "tgt_alig": "自研 align"}
FR = [0, 5, 10, 15, 20, 25, 30, 35, 40]
VIEW = sys.argv[1] if len(sys.argv) > 1 else "front"
here = os.path.dirname(os.path.abspath(__file__))
FD = os.path.join(here, "out", "frames")

def load(p, res=(220, 293)):
    im = Image.open(p).convert("RGB")
    return im.resize(res, Image.LANCZOS)

def font(sz):
    for f in ("C:/Windows/Fonts/msyh.ttc", "C:/Windows/Fonts/simhei.ttf", "C:/Windows/Fonts/arial.ttf"):
        if os.path.exists(f):
            try: return ImageFont.truetype(f, sz)
            except Exception: pass
    return ImageFont.load_default()

CW, CH = 186, 248
PAD, TOP, LEFT = 6, 44, 140
W = LEFT + len(SRC) * (CW + PAD) + PAD
H = TOP + len(FR) * (CH + PAD) + PAD
sheet = Image.new("RGB", (W, H), (24, 26, 30))
d = ImageDraw.Draw(sheet)
f_big, f_sm = font(19), font(15)
title = "源(SW2 铁炮兵) vs 骑砍2重定向  —— 视角: %s" % ("正面" if VIEW == "front" else "侧面")
d.text((LEFT, 8), title, fill=(240, 240, 245), font=f_big)
for ci, pre in enumerate(SRC):
    x = LEFT + ci * (CW + PAD) + CW // 2
    t = LABEL[pre]
    d.text((x - d.textlength(t, font=f_sm) // 2, TOP - 22), t, fill=(200, 205, 215), font=f_sm)
for ri, f in enumerate(FR):
    y = TOP + ri * (CH + PAD)
    d.text((10, y + CH // 2 - 10), "frame %d" % f, fill=(230, 190, 120), font=f_sm)
    for ci, pre in enumerate(SRC):
        p = os.path.join(FD, "%s_%s_f%03d.png" % (pre, VIEW, f))
        x = LEFT + ci * (CW + PAD)
        if os.path.exists(p):
            sheet.paste(load(p), (x, y))
        else:
            d.rectangle([x, y, x + CW, y + CH], outline=(120, 60, 60))
            d.text((x + 10, y + 10), "missing", fill=(200, 80, 80), font=f_sm)
out = os.path.join(here, "out", "sheet_%s.png" % VIEW)
sheet.save(out)
print("saved", out, sheet.size)
