# sample_grid.py — 从 numgrid4 截图采样解剖点颜色, 匹配 16 色表 -> 器官格号
# 光照鲁棒: 采样色需匹配 l*色卡 (l 扫 0.35~1.4)
import numpy as np
import cv2

INBOX = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\face-pipeline\inbox"
# 16 格色卡 (与 numgrid4.py colors 一致, RGB): row*4+col
CARDS = {
  0:(230,220,200),1:(200,230,220),2:(220,200,230),3:(230,230,200),
  4:(200,200,230),5:(230,200,200),6:(200,230,200),7:(210,225,235),
  8:(235,210,225),9:(225,235,210),10:(215,215,245),11:(245,215,215),
  12:(215,245,225),13:(235,225,245),14:(245,235,225),15:(225,225,200)
}

def match(rgb):
    best, bestd = None, 1e18
    for idx, c in CARDS.items():
        for l in np.arange(0.35, 1.45, 0.05):
            d = np.sum((np.array(rgb) - np.array(c) * l) ** 2)
            if d < bestd:
                bestd, best = d, idx
    return best, bestd

def sample(img, x, y, r=3):
    h, w = img.shape[:2]
    x = max(r, min(w - 1 - r, x)); y = max(r, min(h - 1 - r, y))
    blk = img[y - r:y + r + 1, x - r:x + r + 1].reshape(-1, 3)
    return np.median(blk, axis=0)

# 解剖点(相对图宽高比例): x,y 为 0-1 归一化坐标
POINTS = {
    "forehead": (0.50, 0.22),
    "L_eye":    (0.43, 0.38),  # 观察者左眼
    "R_eye":    (0.57, 0.38),
    "nose":     (0.50, 0.48),
    "mouth":    (0.50, 0.57),
    "chin":     (0.50, 0.66),
    "L_ear":    (0.17, 0.42),
    "R_ear":    (0.83, 0.42),
    "neck":     (0.50, 0.85),
}

for f in ["{caffeb88-846d-4a8d-b29d-5f8bb79f91c2}.png",
          "{c4eb39ac-8497-41cd-8ed5-9c32dd04e139}.png",
          "{e2930c38-f509-4154-8af1-46c0e31e1f65}.png"]:
    img = cv2.imread(INBOX + "\\" + f)
    if img is None:
        continue
    h, w = img.shape[:2]
    print("==", f[:15], f"{w}x{h}")
    for name, (nx, ny) in POINTS.items():
        sx, sy = int(nx * w), int(ny * h)
        med = sample(img, sx, sy)
        # cv2 读为 BGR -> RGB
        rgb = med[::-1]
        idx, d = match(rgb)
        print(f"  {name:9s} px({sx},{sy}) rgb=({rgb[0]:.0f},{rgb[1]:.0f},{rgb[2]:.0f}) -> grid {idx//4}{idx%4} (d={d:.0f})")
