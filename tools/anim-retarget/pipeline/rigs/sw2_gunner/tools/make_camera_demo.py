# -*- coding: utf-8 -*-
"""生成"侧面相机约定"图解：说明为什么固定同侧相机会让朝向相反的两套骨架看起来被镜像。"""
import os
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__))
DD = os.path.join(HERE, "..", "..", "..", "..", "output", "verify", "demo")
FR = [0, 5, 10, 15, 20]
COLS = [
    ("A_src", "① 源 SW2（面朝 −Y）", "相机 +X", "#5fd18a"),
    ("B_badcam", "② 骑砍 align（面朝 +Y）", "相机固定 +X  ← 看起来反了", "#e4675f"),
    ("C_okcam", "③ 骑砍 align（面朝 +Y）", "相机 −X（按各自朝向选）", "#5fd18a"),
]
CW, CH, PAD, TOP, LEFT = 260, 347, 8, 96, 128

def font(sz):
    for f in ("C:/Windows/Fonts/msyh.ttc", "C:/Windows/Fonts/simhei.ttf", "C:/Windows/Fonts/arial.ttf"):
        if os.path.exists(f):
            try: return ImageFont.truetype(f, sz)
            except Exception: pass
    return ImageFont.load_default()

W = LEFT + len(COLS) * (CW + PAD) + PAD
H = TOP + len(FR) * (CH + PAD) + PAD + 54
img = Image.new("RGB", (W, H), (18, 20, 26))
d = ImageDraw.Draw(img)
f_t, f_c, f_s, f_n = font(21), font(15), font(14), font(13)

d.text((14, 10), "侧面视角「看起来相反」的真相：相机约定问题，不是重定向错误", fill=(255, 255, 255), font=f_t)
d.text((14, 40), "源面朝 −Y、骑砍面朝 +Y（本就相差 180°，这正是需要 Rot(Z,180°) 帧变换的原因）。", fill=(160, 168, 182), font=f_n)
d.text((14, 58), "侧向相机若固定放在同一侧，朝向相反的两者必然被拍成一左一右 —— 换边拍即同向。", fill=(160, 168, 182), font=f_n)
d.text((14, 76), "两张 align 图是同一份 FBX、同一帧，只是相机换了一侧。", fill=(217, 164, 65), font=f_n)

for ci, (pre, t1, t2, col) in enumerate(COLS):
    x = LEFT + ci * (CW + PAD)
    d.text((x, 100 - 34), t1, fill=(235, 238, 244), font=f_c)
    d.text((x, 100 - 15), t2, fill=col, font=f_s)

for ri, f in enumerate(FR):
    y = TOP + ri * (CH + PAD)
    d.text((10, y + CH // 2 - 8), "frame %d" % f, fill=(230, 190, 120), font=f_s)
    for ci, (pre, _, _, _) in enumerate(COLS):
        p = os.path.join(DD, "%s_side_f%03d.png" % (pre, f))
        x = LEFT + ci * (CW + PAD)
        if os.path.exists(p):
            im = Image.open(p).convert("RGB").resize((CW, CH), Image.LANCZOS)
            img.paste(im, (x, y))
            d.rectangle([x, y, x + CW, y + CH], outline=(60, 66, 80))

y = TOP + len(FR) * (CH + PAD) + 6
d.text((LEFT, y), "结论：② 与 ① 屏幕朝向相反（看着像被镜像）；③ 换了相机侧后与 ① 同向，姿态逐帧吻合。", fill=(95, 209, 138), font=f_c)
out = os.path.join(HERE, "..", "..", "..", "..", "output", "verify", "camera_convention.png")
img.save(out)
print("saved", out, img.size)
