# make_eye.py — 从参考图取信长眼球部件, 生成 512 眼球纹理 (中央虹膜/瞳孔+边缘透明)
# 与原生 eye_a_d 结构一致: 中央圆盘(虹膜+瞳孔) + 眼白向边缘渐透明
import numpy as np
import cv2
import mediapipe as mp

SRC = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\face-pipeline\data\input_oda_x4.png"
OUT = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\face-pipeline\data\output\eye_lwn.png"

img = cv2.imread(SRC)
h, w = img.shape[:2]
scale = 1600.0 / max(h, w)
work = cv2.resize(img, (int(w*scale), int(h*scale)))
with mp.solutions.face_mesh.FaceMesh(static_image_mode=True, max_num_faces=1, refine_landmarks=True) as fm:
    res = fm.process(cv2.cvtColor(work, cv2.COLOR_BGR2RGB))
lm = np.array([(p.x, p.y) for p in res.multi_face_landmarks[0].landmark])
lm[:, 0] *= w; lm[:, 1] *= h

# 信长右眼视野 (画面左眼, 33 外角 133 内角)
x0, x1 = min(lm[33][0], lm[133][0]), max(lm[33][0], lm[133][0])
y0, y1 = min(lm[159][1], lm[145][1]), max(lm[159][1], lm[145][1])
padx = (x1-x0)*0.25; pady = (y1-y0)*0.45
ex0, ex1 = max(0, int(x0-padx)), min(w-1, int(x1+padx))
ey0, ey1 = max(0, int(y0-pady)), min(h-1, int(y1+pady))
eye = img[ey0:ey1, ex0:ex1]
print("eye crop", eye.shape)

# 瞳孔中心: 33/133 中点 -> 原图内瞳孔位置 (mediapipe 眼周最黑中心 -> 用 landmark 306? 用眼球中心= (159 145 33 133) 均值)
cx = float(lm[133][0]); cy = float(lm[159][1])  # 内角/上睑 -> 瞳中心近似
# 精确瞳孔: 在 eye crop 找最黑质心
e = cv2.cvtColor(eye, cv2.COLOR_BGR2GRAY)
me = e.min()
mask = (e < me + 25).astype(np.uint8) * 255
M = cv2.moments(mask, True)
if M["m00"] > 10:
    px, py = M["m10"]/M["m00"], M["m01"]/M["m00"]
else:
    px, py = e.shape[1]*0.55, e.shape[0]*0.45
print("pupil at", px, py)

# 瞳孔半径 = 最黑区域等效半径
r = np.sqrt(M["m00"]/np.pi) if M["m00"] > 10 else 20

# 构建 512 纹理: iris 半径 60px(约占 360/512 的 0.7= 原生 iris 占 ~ (gaus): from eye_a_d: iris直径~0.45*512=230px
S = 512
out = np.zeros((S, S, 4), np.uint8)
# 从 eye crop 取以瞳孔为中心的正方形 (半径 = iris_ratio*S*0.55 / 2...)
iris_diam_px = 230
half = int(iris_diam_px/2)
cx0 = max(0, int(px-half)); cy0 = max(0, int(py-half))
cx1 = min(e.shape[1], int(px+half)); cy1 = min(e.shape[0], int(py+half))
part = eye[cy0:cy1, cx0:cx1]
# resize 到 230x230 放中央
part = cv2.resize(part, (230, 230))
# 虹膜圆形 mask: 以照片瞳孔为中心的圆 (115 radius), 去掉皮肤四角
py_g, px_g = np.mgrid[0:230, 0:230]
plx = (px - cx0) / max(1, (cx1 - cx0)) * 230
ply = (py - cy0) / max(1, (cy1 - cy0)) * 230
pr = np.sqrt((px_g - plx) ** 2 + (py_g - ply) ** 2)
circle = (pr <= 112).astype(np.float32)[:, :, None]
part = (part * circle).astype(np.uint8)
out[141:371, 141:371, :3] = part[:, :, ::-1]
out[141:371, 141:371, 3] = (circle[:, :, 0]*255).astype(np.uint8)
# 边缘眼白: 白色填充内圈 (柔化)
white = np.full((S, S), 255, np.uint8)
# alpha 遮罩: 中心圆 1.0 -> 边缘 0
yy, xx = np.mgrid[0:S, 0:S]
rr = np.sqrt((xx-S/2)**2 + (yy-S/2)**2)
alpha = np.clip(1.0 - (rr-115)/115, 0, 1)  # 115~230 渐隐
alpha = (alpha*255).astype(np.uint8)
out[:, :, 3] = np.maximum(out[:, :, 3]*255//255, alpha)  # 以 alpha 遮罩为准
# 眼白区域 (alpha>0 且非瞳孔内) 用淡白/肤色混合
gray = alpha > 60
ring = gray & (rr > 140)
out[ring] = (225, 218, 205, 255)  # 眼白暖白
cv2.imwrite(OUT, cv2.cvtColor(out, cv2.COLOR_RGBA2BGRA))
print("saved", OUT)
