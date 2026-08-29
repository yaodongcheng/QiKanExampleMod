# find_eyes.py — 在织丰原版 diffuse 上自动定位眼睛(红瞳)/脸颊特征, 输出 uv 坐标
import numpy as np
from PIL import Image

IMG = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\face-pipeline\data\sho_head_male_japanese_d.png"
img = np.asarray(Image.open(IMG).convert("RGB")).astype(np.float32)
H, W = img.shape[:2]
R, G, B = img[:,:,0], img[:,:,1], img[:,:,2]

# 红瞳: 红高, 绿蓝低
pupil = (R > 150) & (G < 110) & (B < 110) & (G < R*0.7) & (B < R*0.7)
# 只保留脸区(左半图)
pupil[:, W//2:] = False
print("pupil px:", pupil.sum())

# 连通域
from scipy import ndimage
lab, n = ndimage.label(pupil)
print("blobs:", n)
for i in range(1, n+1):
    ys, xs = np.nonzero(lab == i)
    if len(ys) < 80: continue
    cy, cx = ys.mean(), xs.mean()
    print(f"blob{i}: size={len(ys)} center_px=({cx:.0f},{cy:.0f}) uv=({cx/W:.4f},{cy/H:.4f})")
