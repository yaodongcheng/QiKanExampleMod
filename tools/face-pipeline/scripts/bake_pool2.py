# -*- coding: utf-8 -*-
# bake_pool2.py — 女脸池贴图「数据流烘焙」 v2（复用 render_uvview 的正面临视口→UV 映射）
# 步骤: 1) render_uvview(女头skin) 得 uvview.png(屏幕色值=UV)  2) 母版正脸 → 映射区 bbox 对齐 → 逐屏幕像素烫进 UV 图
# 输出: lwn_face_f1..f5_d 2048 + f1_n/f1_s + manifest
# 用法: python bake_pool2.py <母版jpg> <uvview.png> <outdir> [flip_v=0|1]
import os, sys, json
import numpy as np
from PIL import Image

OUT = 2048

def main():
    master_p, uvview_p, outdir = sys.argv[1], sys.argv[2], sys.argv[3]
    flip_v = len(sys.argv) > 4 and sys.argv[4] == '1'
    os.makedirs(outdir, exist_ok=True)
    mi = Image.open(master_p).convert('RGB')
    W, H = mi.size
    # 母版正脸裁切(与 uvview 视口同框比例): 脸居中, 取脸高约占 60%
    cx, cy = W // 2, int(H * 0.40)
    half_w, half_h = int(W * 0.33), int(H * 0.36)
    m = np.array(mi.crop((cx - half_w, cy - half_h, cx + half_w, cy + half_h)), dtype=np.uint8)
    mh, mw = m.shape[:2]
    uv = np.array(Image.open(uvview_p).convert('RGB'), dtype=np.float32) / 255.0
    vh, vw = uv.shape[:2]
    # 视口有效区域 bbox(非背景色 60,60,70)
    valid = (np.abs(uv[:, :, 0] * 255 - 60) + np.abs(uv[:, :, 1] * 255 - 60) + np.abs(uv[:, :, 2] * 255 - 70)) > 30
    ys, xs = np.where(valid)
    if len(xs) == 0:
        print('no valid uv pixels'); return
    vx0, vx1, vy0, vy1 = xs.min(), xs.max(), ys.min(), ys.max()
    print('viewport bbox', (vx0, vy0, vx1, vy1), 'pix', len(xs))
    out = np.zeros((OUT, OUT, 3), np.uint8)
    out[:, :] = (196, 178, 168)
    for py in range(vy0, vy1 + 1):
        row = uv[py]; mx = (py - vy0) / max(vy1 - vy0, 1)
        sy = int(mx * (mh - 1))
        for px in range(vx0, vx1 + 1):
            c = row[px]
            u, v = c[0], c[1]
            if u < 0.01 and v < 0.01:
                continue
            if u >= 1 or v >= 1:
                continue
            sx = int((px - vx0) / max(vx1 - vx0, 1) * (mw - 1))
            ux = int(u * (OUT - 1)); vy2 = int(v * (OUT - 1))
            if flip_v:
                vy2 = OUT - 1 - vy2
            out[vy2, ux] = m[sy, sx]
    Image.fromarray(out).save(os.path.join(outdir, 'lwn_face_f1_d.png'))
    from PIL import ImageEnhance as IE
    for i in range(2, 6):
        fi = Image.fromarray(out)
        fi = IE.Brightness(fi).enhance(0.95 + 0.02 * i)
        if i % 2 == 0:
            fi = IE.Color(fi).enhance(0.97)
        fi.save(os.path.join(outdir, f'lwn_face_f{i}_d.png'))
    n = np.zeros((OUT, OUT, 3), np.uint8); n[:, :] = (128, 128, 254)
    rng = np.random.default_rng(7)
    n[:, :, 0] = 124 + rng.integers(0, 8, (OUT, OUT)); n[:, :, 1] = 124 + rng.integers(0, 8, (OUT, OUT))
    Image.fromarray(n).save(os.path.join(outdir, 'lwn_face_f1_n.png'))
    Image.fromarray(np.full((OUT, OUT), 175, np.uint8)).save(os.path.join(outdir, 'lwn_face_f1_s.png'))
    texs = []
    for i in range(1, 6):
        texs.append({'name': f'lwn_face_f{i}_d', 'png': f'{outdir}/lwn_face_f{i}_d.png', 'width': OUT, 'height': OUT})
        texs.append({'name': f'lwn_face_f{i}_n', 'png': f'{outdir}/lwn_face_f1_n.png', 'width': OUT, 'height': OUT})
        texs.append({'name': f'lwn_face_f{i}_s', 'png': f'{outdir}/lwn_face_f1_s.png', 'width': OUT, 'height': OUT})
    manifest = {'outDir': outdir, 'packs': [{'packName': 'lwnart_female', 'textures': texs}]}
    with open(os.path.join(outdir, 'manifest_female.json'), 'w', encoding='utf-8') as f:
        json.dump(manifest, f, ensure_ascii=False, indent=1)
    print('done', len(texs), 'textures')

if __name__ == '__main__':
    main()
