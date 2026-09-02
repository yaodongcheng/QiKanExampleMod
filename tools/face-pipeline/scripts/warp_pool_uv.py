# -*- coding: utf-8 -*-
# warp_pool_uv.py — gpt-image UV 展开图 -> xxFemaleHead 池贴图(TPS 对齐原版画布表)
# 源: gpt 生成 U V 展开图(1024², 秃头/闭眼/唇耳分离岛)
# 目标: 原版画布真值表(眼1005/眉855/嘴1510/下巴1688——已用原版 x21 渲染验证)
# 输出: 2048² 池贴图; 源域 = 皮肤(色差分段>背景棕), 其余保留原版底图
# 用法: python warp_pool_uv.py <生成uv.png> <原版池diffuse.png> <outdir> <outname>
import sys, os
import numpy as np
import cv2
import mediapipe as mp

# ---- 目标(池 2048, 原版画布真值表) ----
DST_PTS = [
    (530, 1005), (831, 1005), (1448, 1005), (1153, 1005),   # 眼外/内角 L,R
    (683, 1020), (1306, 1020),                              # 瞳孔 L,R
    (560, 855), (895, 850), (1415, 855), (1110, 850),       # 眉 L外内 R外内
    (1012, 1050), (1030, 1321), (1030, 1367),               # 鼻梁/鼻尖/尖下
    (847, 1510), (1188, 1505), (1020, 1469), (1020, 1545),  # 嘴角 L,R / 上唇 / 下唇
    (1025, 1688), (1015, 1712),                             # 下巴尖/颏下
    (760, 540), (1000, 500), (1240, 540),                   # 额头 左/中/右
]
FACE_IDX = [168, 1, 2, 61, 291, 13, 17, 152, 175]
FACE_BBOX = (470, 430, 1500, 1760)   # 池图脸皮岛
EYE_BAND = (460, 925, 1530, 1140)    # 眼带原版化矩形(x0,y0,x1,y1): 网格眼区必须原版猫眼形
BG = (139, 113, 92)                  # 生成图背景棕(用于源肤域判定)


def detect(src_path):
    img = cv2.imread(src_path)
    h, w = img.shape[:2]
    scale = 1.0
    work = img
    if max(h, w) > 1600:
        scale = 1600.0 / max(h, w)
        work = cv2.resize(img, (int(w * scale), int(h * scale)))
    with mp.solutions.face_mesh.FaceMesh(
            static_image_mode=True, max_num_faces=1, refine_landmarks=True) as fm:
        res = fm.process(cv2.cvtColor(work, cv2.COLOR_BGR2RGB))
        if not res.multi_face_landmarks:
            raise SystemExit("no face detected")
    lm = np.array([(p.x, p.y) for p in res.multi_face_landmarks[0].landmark])
    lm[:, 0] *= w
    lm[:, 1] *= h
    return lm


def build_anchors(lm):
    """生成 UV 图: 大脸/正脸/无刘海 -> 直接采信 mediapipe 眼眉角点(无需重建)"""
    eye = [(lm[33][0], lm[33][1]), (lm[133][0], lm[133][1]),
           (lm[362][0], lm[362][1]), (lm[263][0], lm[263][1]),
           (lm[468][0], lm[468][1]), (lm[473][0], lm[473][1])]
    brow = [(lm[70][0], lm[70][1]), (lm[63][0], lm[63][1]),
            (lm[300][0], lm[300][1]), (lm[293][0], lm[293][1])]
    face = [(lm[i][0], lm[i][1]) for i in FACE_IDX]
    face[-1] = (face[-2][0], face[-2][1] + 12)
    f_y = (brow[0][1] + brow[2][1]) / 2 - 60.0
    fore = [(brow[0][0], f_y), ((brow[0][0] + brow[1][0]) / 2, f_y), (brow[2][0], f_y)]
    return eye + brow + face + fore   # 22 点, 顺序对齐 DST_PTS


def skin_domain(img, max_y_frac=0.72):
    """源肤域 mask: 与背景棕色距离差 > 阈值; 下缘截断(脸底以下=颈部带, 不进池图)"""
    img = img.astype(np.float32)
    d = np.abs(img - np.array(BG)).sum(axis=2)
    m = (d > 90).astype(np.uint8) * 255
    H = img.shape[0]
    m[int(max_y_frac * H):, :] = 0
    return m


def tps_solve(src, dst, lam=1e4):
    n = len(dst)
    dd = dst[:, None, :] - dst[None, :, :]
    r = np.sqrt(np.sum(dd * dd, axis=2))
    K = r * r * np.log(r + 1e-12)
    P = np.ones((n, 3)); P[:, 1] = dst[:, 0]; P[:, 2] = dst[:, 1]
    A = np.zeros((n + 3, n + 3))
    A[:n, :n] = K + lam * np.eye(n); A[:n, n:] = P; A[n:, :n] = P.T
    out = []
    for k in range(2):
        b = np.zeros(n + 3); b[:n] = src[:, k]
        out.append(np.linalg.solve(A, b))
    return out


def tps_map(points, dst, solx, soly):
    n = len(dst)
    d = points[:, None, :] - dst[None, :, :]
    r2 = np.sum(d * d, axis=2)
    U = r2 * np.log(np.sqrt(r2) + 1e-12) * 0.5
    x = U @ solx[:n] + solx[n] + solx[n + 1] * points[:, 0] + solx[n + 2] * points[:, 1]
    y = U @ soly[:n] + soly[n] + soly[n + 1] * points[:, 0] + soly[n + 2] * points[:, 1]
    return np.stack([x, y], axis=1)


def solve_similarity(src3, dst3):
    """3 对点 LSQ 一般仿射变换(6 参数, 眼/嘴全锁): dst = A*src + t"""
    A = []; B = []
    for (sx, sy), (dx, dy) in zip(src3, dst3):
        A.append([sx, sy, 1, 0, 0, 0]); B.append(dx)
        A.append([0, 0, 0, sx, sy, 1]); B.append(dy)
    A = np.array(A, np.float64); B = np.array(B, np.float64)
    a, b, tx, c, d, ty = np.linalg.lstsq(A, B, rcond=None)[0]
    M = np.array([[a, b, tx], [c, d, ty]], np.float64)
    return M


def main():
    uv_p, base_p, outdir = sys.argv[1], sys.argv[2], sys.argv[3]
    outname = sys.argv[4] if len(sys.argv) > 4 else "head_female_x20_d"
    os.makedirs(outdir, exist_ok=True)
    uv = cv2.imread(uv_p)
    H, W = uv.shape[:2]
    print("uv size", W, H)
    lm = detect(uv_p)
    src = np.array(build_anchors(lm), np.float64)
    dst = np.array(DST_PTS, np.float64)
    # 相似变换锚: 瞳 L(4), 瞳 R(5), 嘴下唇中(16)
    M = solve_similarity([src[4], src[5], src[16]], [dst[4], dst[5], dst[16]])
    print("similarity M:", M.flatten().round(3))
    warped = cv2.warpAffine(uv, M, (2048, 2048),
                            flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)

    # 源肤域 -> 池图对应遮罩 (生成图背景棕不进入)
    m_dom = skin_domain(uv)
    m_res = cv2.warpAffine(m_dom, M, (2048, 2048),
                           flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=0)
    inside = (m_res > 128).astype(np.float32)
    inside = cv2.GaussianBlur(inside, (41, 41), 0)

    base = cv2.imread(base_p)
    x0, y0, x1, y1 = FACE_BBOX
    bbox = np.zeros((2048, 2048), np.float32)
    bbox[y0:y1, x0:x1] = 1.0
    mask = inside * bbox

    out = (base.astype(np.float32) * (1 - mask[:, :, None])
           + warped.astype(np.float32) * mask[:, :, None]).astype(np.uint8)

    # 眼带原版化: 网格眼区比例必须原版猫眼形(gpt 眼形 1/8 仍宽) -->
    # 眼带矩形取原版 base, 边缘 30px 羽化(防硬边框)
    xe0, ye0, xe1, ye1 = EYE_BAND
    band_mask = np.zeros((2048, 2048), np.float32)
    band_mask[ye0:ye1, xe0:xe1] = 1.0
    band_mask = cv2.GaussianBlur(band_mask, (101, 101), 0)
    out = (out.astype(np.float32) * (1 - band_mask[:, :, None])
           + base.astype(np.float32) * band_mask[:, :, None]).astype(np.uint8)
    cv2.imwrite(os.path.join(outdir, "warped_full.png"), warped)
    cv2.imwrite(os.path.join(outdir, outname + ".png"), out)
    print("saved", outname + ".png")


if __name__ == "__main__":
    main()
