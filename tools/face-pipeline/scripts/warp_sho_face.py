# warp_sho_face.py — 织丰头版: 把参考图五官 warp 进织丰 diffuse 的五官区(微调锚点可迭代)
# 目标 UV 来自织丰 diffuse 特征(read zoom: 双瞳 find_eyes 0.212/0.243, 0.260,0.270)
# 策略: 只替换五官+羽化融合, 其余保留织丰原贴图(面色/发际/耳朵等)
import numpy as np
import cv2
import mediapipe as mp
import sys, os

SHO = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\face-pipeline\data\sho_head_male_japanese_d.png"
SRC = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\face-pipeline\data\input_oda_x4.png"
OUTDIR = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\face-pipeline\data\output"

# (u0,v0,u1,v1) 织丰头 UV（v:0=底部, 与 4096 图像行 y=(1-v)*H 对应）
RECTS = {
    # v_sho3: u=头纵向(高度), v=左右环绕 —— 星图+"全躲眉上"反推
    "eye1": (0.265, 0.600, 0.345, 0.720),
    "eye2": (0.265, 0.280, 0.345, 0.400),
    "nose": (0.185, 0.400, 0.265, 0.600),
    "mouth":(0.115, 0.400, 0.190, 0.600),
    "chin": (0.035, 0.400, 0.120, 0.600),
}

def detect(src_path):
    img = cv2.imread(src_path)
    h, w = img.shape[:2]
    scale = 1600.0 / max(h, w) if max(h, w) > 1600 else 1.0
    work = cv2.resize(img, (int(w*scale), int(h*scale)))
    mp_face_mesh = mp.solutions.face_mesh
    with mp_face_mesh.FaceMesh(static_image_mode=True, max_num_faces=1, refine_landmarks=True) as fm:
        res = fm.process(cv2.cvtColor(work, cv2.COLOR_BGR2RGB))
    lm = np.array([(p.x, p.y) for p in res.multi_face_landmarks[0].landmark])
    lm[:, 0] *= w; lm[:, 1] *= h
    return lm, img

def win_of(lm, a, b, padx=0.35, pady=0.35):
    """landmark 对 -> 窗口 (x0,y0,x1,y1)"""
    x0, x1 = min(lm[a][0], lm[b][0]), max(lm[a][0], lm[b][0])
    y0, y1 = min(lm[a][1], lm[b][1]), max(lm[a][1], lm[b][1])
    wd, hd = (x1-x0), max(y1-y0, (x1-x0)*0.4)
    return (x0 - wd*padx, y0 - hd*pady, x1 + wd*padx, y1 + hd*pady)

def main():
    lm, src = detect(SRC)
    W = 4096
    canvas = cv2.imread(SHO).copy()

    wins = {
        "eye1": win_of(lm, 33, 133),          # 画面左眼
        "eye2": win_of(lm, 362, 263),
        "nose": win_of(lm, 168, 1, 0.25, 0.1),
        "mouth": win_of(lm, 61, 291, 0.25, 0.45),
        "chin": win_of(lm, 17, 152, 0.35, 0.05),
    }
    for name, (u0, v0, u1, v1) in RECTS.items():
        x0, x1 = int(u0*W), int(u1*W)
        y0, y1 = int((1-v1)*W), int((1-v0)*W)
        sx0, sy0, sx1, sy1 = wins[name]
        sx0,sy0 = max(0,int(sx0)), max(0,int(sy0))
        sx1,sy1 = min(src.shape[1],int(sx1)), min(src.shape[0],int(sy1))
        cropped = src[sy0:sy1, sx0:sx1]
        tw, th = x1-x0, y1-y0
        patch = cv2.resize(cropped, (tw, th), interpolation=cv2.INTER_CUBIC)
        # 羽化 mask
        mask = np.zeros((th, tw), np.uint8)
        margin = max(3, min(tw, th)//5)
        mask[margin:th-margin, margin:tw-margin] = 255
        mask = cv2.GaussianBlur(mask, (0,0), sigmaX=max(2, margin/2))
        mask3 = cv2.cvtColor(mask, cv2.COLOR_GRAY2BGR) / 255.0
        region = canvas[y0:y1, x0:x1].astype(np.float32)
        canvas[y0:y1, x0:x1] = (region*(1-mask3) + patch.astype(np.float32)*mask3).astype(np.uint8)
        print(f"{name}: target uv({u0},{v0})-({u1},{v1}) px({x0},{y0})-({x1},{y1}) src {wins[name]}")

    out = os.path.join(OUTDIR, "diffuse_sho.png")
    cv2.imwrite(out, canvas)
    print("saved", out)

if __name__ == "__main__":
    main()
