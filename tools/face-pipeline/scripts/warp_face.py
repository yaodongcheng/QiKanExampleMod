# warp_face v4 — 组件式拼脸: 男头 UV 的"五官岛"是分离的小区（镜像共用设计）
# 每个部件: source窗口(mediapipe landmark 裁剪) -> 仿射到目标 uv 矩形
# 其余区域 = 中性肤色填充。渲染预览用于校准窗口/矩形。
# 用法: python warp_face.py <参考图> <输出目录>
import sys
import os
import numpy as np
import cv2
import mediapipe as mp

# ---- 目标 UV 矩形 (u0,v0,u1,v1) v:0=底部 — 来自 geo_anchor_v4 面片中心测量 ----
RECTS = {
    # uv_pick.py 确定性测量 (数值桥: 3D解剖点 -> UV)
    "eye":  (0.040, 0.256, 0.100, 0.316),   # 单眼岛(镜像共用)
    "brow": (0.450, 0.150, 0.555, 0.195),   # 眉岛(独立于面区,在图中部!)
    "nose": (0.000, 0.222, 0.028, 0.248),
    "mouth":(0.000, 0.165, 0.058, 0.205),
    "chin": (0.020, 0.098, 0.075, 0.128),
}

def detect(img_path):
    mp_face_mesh = mp.solutions.face_mesh
    img = cv2.imread(img_path)
    h, w = img.shape[:2]
    scale = 1.0
    work = img
    if max(h, w) > 1600:
        scale = 1600.0 / max(h, w)
        work = cv2.resize(img, (int(w * scale), int(h * scale)))
    with mp_face_mesh.FaceMesh(static_image_mode=True, max_num_faces=1, refine_landmarks=True) as fm:
        res = fm.process(cv2.cvtColor(work, cv2.COLOR_BGR2RGB))
        if not res.multi_face_landmarks:
            raise SystemExit("no face")
    lm = np.array([(p.x, p.y) for p in res.multi_face_landmarks[0].landmark])
    lm[:, 0] *= w; lm[:, 1] *= h
    return lm, img

def src_windows(lm, W, H):
    """mediapipe 关键点 -> (部件名, (x0,y0,x1,y1) 源窗口矩形)"""
    def mx(i): return lm[i][0]
    def my(i): return lm[i][1]
    # 单眼: 取画面左眼 (33=外角,133=内角) 扩充
    ex0, ex1 = min(mx(33), mx(133)), max(mx(33), mx(133))
    ey0, ey1 = min(my(159), my(145)), max(my(159), my(145))   # 上眼睑/下眼睑
    pad = 0.25
    ey = (ex0 - (ex1 - ex0) * pad, ey0 - (ey1 - ey0) * pad,
          ex1 + (ex1 - ex0) * pad, ey1 + (ey1 - ey0) * pad)
    # 眉: 用眉线外中点 70(左眉外),293(右眉内) 与 300 定位眉行
    bx0 = mx(70); bx1 = mx(300)
    by = (my(70) + my(300)) * 0.5
    bw = abs(bx1 - bx0)
    bh = abs(my(71) - my(80)) * 2.0   # 眉上下厚度
    br = (bx0 - bw * 0.05, by - bh, bx1 + bw * 0.05, by + bh)
    # 鼻: 鼻梁 168 -> 鼻尖 1
    nx0, nx1 = mx(1) - abs(mx(49) - mx(59)) * 0.6, mx(1) + abs(mx(49) - mx(59)) * 0.6
    ny0, ny1 = my(168), my(1) + (my(2) - my(1)) * 0.4
    # 嘴: 61/291 外角, 上下 13/15
    mx0, mx1 = mx(61), mx(291)
    my0, my1 = my(13), my(17)
    mouth = (mx0, my0, mx1, my1)
    # 下巴窗: 157(下巴下) -> 152
    cx, cy = mx(152), my(152)
    ch = abs(my(152) - my(17)) * 0.30
    chin = (cx - ch, cy - ch, cx + ch, cy)
    return {"eye": ey, "brow": br, "nose": (nx0, ny0, nx1, ny1),
            "mouth": mouth, "chin": chin}

def main():
    src_path = sys.argv[1]
    out_dir = sys.argv[2] if len(sys.argv) > 2 else "output"
    os.makedirs(out_dir, exist_ok=True)
    lm, img = detect(src_path)
    W, H = 2048, 2048
    canvas = np.zeros((H, W, 3), np.uint8)
    skin = np.array([168, 138, 118], np.uint8)   # 适配参考脸肤色基调(信长偏暖)
    canvas[:] = skin

    wins = src_windows(lm, img.shape[1], img.shape[0])
    for part, (u0, v0, u1, v1) in RECTS.items():
        x0, y0, x1, y1 = wins[part]
        x0 = max(0, int(x0)); y0 = max(0, int(y0))
        x1 = min(img.shape[1] - 1, int(x1)); y1 = min(img.shape[0] - 1, int(y1))
        if x1 - x0 < 2 or y1 - y0 < 2:
            print("skip", part, wins[part]); continue
        crop = img[y0:y1, x0:x1]
        tw = int(abs(u1 - u0) * W); th = int(abs(v1 - v0) * H)
        tw = max(tw, 2); th = max(th, 2)
        resized = cv2.resize(crop, (tw, th), interpolation=cv2.INTER_CUBIC)
        px = int(u0 * W); py = int((1.0 - v1) * H)
        canvas[py:py + th, px:px + tw] = resized[:, :, ::-1]
        print(f"part {part}: src {wins[part]} -> uvrect {RECTS[part]} size {tw}x{th}")

    out = os.path.join(out_dir, "diffuse_v1.png")
    cv2.imwrite(out, cv2.cvtColor(canvas, cv2.COLOR_RGB2BGR))
    print("saved", out)

if __name__ == "__main__":
    main()
