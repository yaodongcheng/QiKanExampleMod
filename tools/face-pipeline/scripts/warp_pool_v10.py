# -*- coding: utf-8 -*-
# warp_pool_v10.py — 全岛铺满实验（对照原版"整图一张脸皮"）
# v10 三改: ①母版秃额补全(额皮纹理拉伸, 非纯色) ②TPS 加 λ 正则(防外推发散)
#           ③额顶 3 锚点(源=补全额头皮) + bbox 扩到全岛 200-1760
# 用法: python warp_pool_v10.py <母版jpg> <原版池diffuse.png> <outdir> <outname>
import sys, os
import numpy as np
import cv2
import mediapipe as mp

DST_PTS = [
    (530, 1005), (831, 1005), (1448, 1005), (1153, 1005),
    (683, 1020), (1306, 1020),
    (560, 855), (895, 850), (1415, 855), (1110, 850),
    (1012, 1050), (1030, 1321), (1030, 1367),
    (847, 1420), (1188, 1415), (1020, 1380), (1020, 1455),
    (1025, 1600), (1015, 1625),
    (760, 540), (1000, 500), (1240, 540),       # 额头 左/中/右(补全源对应)
]
FACE_IDX = [168, 1, 2, 61, 291, 13, 17, 152, 175]
FACE_BBOX = (480, 420, 1490, 1780)   # v19: 全岛 + 大范围软融合
LAMBDA = 1e5          # TPS 正则化(相对 K~1e6 量级 10%)
FORE_SRC = None   # 动态: 媒体面额轮廓 108/10/337 下移 30px 入额皮(build_anchors 内算)


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
    return lm, img


def bald_forehead(lm, img):
    """脸域 mask(凸包, 含所有自然皮肤区)。v13: 母版天然露额, 不再需要拉伸补全"""
    h, w = img.shape[:2]
    mask = np.zeros((h, w), np.uint8)
    pts = set()
    for e in mp.solutions.face_mesh.FACEMESH_FACE_OVAL:
        pts.add(e[0]); pts.add(e[1])
    oval = np.array([[lm[i][0], lm[i][1]] for i in pts], np.float32)
    cv2.fillConvexPoly(mask, cv2.convexHull(oval.astype(np.int32)), 255)
    mask = cv2.GaussianBlur(mask, (81, 81), 0) / 255.0
    return img, mask


def build_anchors(lm):
    lpc = np.array([lm[468][0], lm[468][1]])
    rpc = np.array([lm[473][0], lm[473][1]])
    hw = 48.0
    eye = [lpc - (hw, 0), lpc + (hw, 0), rpc + (hw, 0), rpc - (hw, 0), lpc, rpc]
    brow = [
        (eye[0][0] - 15, eye[0][1] - 55), (eye[1][0] + 8, eye[1][1] - 59),
        (eye[2][0] + 15, eye[2][1] - 55), (eye[3][0] - 8, eye[3][1] - 59),
    ]
    face = [(lm[i][0], lm[i][1]) for i in FACE_IDX]
    face[-1] = (face[-2][0], face[-2][1] + 12)
    # 额锚 3 点: 眉中心 y-60(必在凸包内皮肤带), x=眉外/中/外
    f_y = (brow[0][1] + brow[2][1]) / 2 - 60.0
    fore = [(brow[0][0], f_y), ((brow[0][0] + brow[1][0]) / 2, f_y), (brow[2][0], f_y)]
    return eye + brow + face + fore   # 22 点, 顺序对齐 DST_PTS


def tps_solve(src, dst, lam):
    n = len(dst)
    d = dst[:, None, :] - dst[None, :, :]
    r = np.sqrt(np.sum(d * d, axis=2))
    K = r * r * np.log(r + 1e-12)
    P = np.ones((n, 3))
    P[:, 1] = dst[:, 0]
    P[:, 2] = dst[:, 1]
    A = np.zeros((n + 3, n + 3))
    A[:n, :n] = K + lam * np.eye(n)
    A[:n, n:] = P
    A[n:, :n] = P.T
    out = []
    for k in range(2):
        b = np.zeros(n + 3)
        b[:n] = src[:, k]
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


def main():
    master_p, base_p, outdir = sys.argv[1], sys.argv[2], sys.argv[3]
    outname = sys.argv[4] if len(sys.argv) > 4 else "head_female_x20_d"
    os.makedirs(outdir, exist_ok=True)
    lm, img_orig = detect(master_p)
    img, m_face = bald_forehead(lm, img_orig)

    src_pts = np.array(build_anchors(lm), np.float64)
    dst_pts = np.array(DST_PTS, np.float64)
    print("anchors:", len(src_pts), "lambda:", LAMBDA)
    solx, soly = tps_solve(src_pts, dst_pts, LAMBDA)
    ys, xs = np.mgrid[0:2048, 0:2048]
    pts = np.stack([xs.ravel(), ys.ravel()], axis=1)
    mp_xy = tps_map(pts, dst_pts, solx, soly)
    map_x = mp_xy[:, 0].reshape(2048, 2048).astype(np.float32)
    map_y = mp_xy[:, 1].reshape(2048, 2048).astype(np.float32)
    warped = cv2.remap(img, map_x, map_y, cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)

    base = cv2.imread(base_p)
    x0, y0, x1, y1 = FACE_BBOX
    # v19: 距离场融合——bbox 内每个像素按"到边界距离"线性淡入(300px 渐变带)，
    #      消灭"硬贴照片"的接缝观感
    inner = np.zeros((2048, 2048), np.uint8)
    inner[y0:y1, x0:x1] = 1
    dist = cv2.distanceTransform(inner, cv2.DIST_L2, 5)
    m_blend = np.clip(dist / 300.0, 0, 1).astype(np.float32)
    out = (base.astype(np.float32) * (1.0 - m_blend[:, :, None])
           + warped.astype(np.float32) * m_blend[:, :, None]).astype(np.uint8)
    cv2.imwrite(os.path.join(outdir, "preview_" + outname + ".png"), out)
    cv2.imwrite(os.path.join(outdir, outname + ".png"), out)
    cv2.imwrite(os.path.join(outdir, "warped_full.png"), warped)
    print("saved", outname + ".png (v10)")


if __name__ == "__main__":
    main()
