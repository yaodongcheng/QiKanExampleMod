# -*- coding: utf-8 -*-
# warp_pool_female.py — 女池贴图「整脸 TPS 变形」生成器 v2
# 思路(贴图倒推法): xxFemaleHead 池 diffuse 本身 = UV 摊开的脸皮,
#   五官在贴图上的位置直接读图可得 -> 不需要解析头网格/uvview。
# v2 修复: ①母版先抠脸(FACE_OVAL 轮廓->凸包->脸外填肤色) 消灭头发污染
#          ②眉锚点不用 mediapipe(刘海易骗) 改由眼角位置推导
#          ③下巴/颏下间距收窄 ④输出叠加对比图 preview_overlay.png
# 用法: python warp_pool_female.py <母版jpg> <原版池diffuse.png> <outdir>
import sys, os
import numpy as np
import cv2
import mediapipe as mp

# ---- 池图 2048 空间目标锚点(目测 v1, v 从顶向下; 游戏反馈后微调) ----
# mediapipe 索引: 33=左眼外角 133=左内角 362=右外角 263=右内角
#   468=左瞳 473=右瞳 | 168=鼻梁顶 1=鼻尖 2=鼻尖下
#   61=左嘴角 291=右嘴角 13=上唇 17=下唇 | 152=下巴尖 175=颏下
# 眉(70/63/300/293) 不采信, 由眼角在代码中推导。
DST_PTS = [
    (530, 1005), (831, 1005), (1448, 1005), (1153, 1005),   # 眼外/内角 L,R
    (683, 1020), (1306, 1020),                              # 瞳孔 L,R
    (560, 855), (895, 850), (1415, 855), (1110, 850),       # 眉 L外内 R外内(推导目标)
    (1012, 1050), (1030, 1321), (1030, 1367),               # 鼻梁/鼻尖/尖下
    (847, 1510), (1188, 1505),                              # 嘴角 L,R
    (1020, 1469), (1020, 1545),                             # 上唇/下唇
    (1025, 1688), (1015, 1712),                             # 下巴尖/颏下
]
EYE_IDX = [33, 133, 362, 263, 468, 473]
FACE_IDX = [168, 1, 2, 61, 291, 13, 17, 152, 175]
# 眉源推导(源=母版): (x = 眼角x ± dx, y = 眼y - 60)  外角向外15, 内角向内8
BROW_DX = {"outer": 15, "inner": 8}

# 脸区 bbox(x0,y0,x1,y1, 池图 2048, 顶=0): 脸皮岛边界, bbox 外保留原版
# v8b: 左右扩到 470/1500(盖原版眼线弧), 下缘 1760(盖颈带), 顶 700 无额帽(避开平台)
FACE_BBOX = (470, 700, 1500, 1760)
# 5 张变体亮度因子: x15, x18, x20, x21, x25
VARIANTS = [("x15", 1.00), ("x18", 0.97), ("x20", 0.95), ("x21", 1.03), ("x25", 1.01)]


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


def mask_face(lm, img):
    """抠脸: FACE_OVAL 轮廓 -> 凸包多边形填充 + 羽化; 脸外填平均肤色。
    (bbox 顶边收窄后不再需要额区抬升, 外推带采样=凸包内额肤)"""
    edges = set()
    for e in mp.solutions.face_mesh.FACEMESH_FACE_OVAL:
        edges.add((e[0], e[1]))
    pts = set()
    for a, b in edges:
        pts.add(a); pts.add(b)
    oval = np.array([[lm[i][0], lm[i][1]] for i in pts], np.float32)
    hull = cv2.convexHull(oval.astype(np.int32)).astype(np.int32)
    mask = np.zeros(img.shape[:2], np.uint8)
    cv2.fillConvexPoly(mask, hull, 255)
    skin = img[mask == 255].reshape(-1, 3).mean(axis=0)
    mask = cv2.GaussianBlur(mask, (81, 81), 0) / 255.0
    filled = img.copy()
    filled[mask < 0.05] = skin[None, None, :].astype(np.uint8)
    return (img.astype(np.float32) * mask[:, :, None]
            + filled.astype(np.float32) * (1.0 - mask[:, :, None])).astype(np.uint8)


def build_anchors(lm):
    """源锚点(母版像素), 顺序与 DST_PTS 19 项对应。
    mediapipe 眼/颏点对 s2002 检出语义错乱(外内角/颏上下翻转) -> TPS 折叠雪崩。
    修复: 眼4点由瞳孔对内插重构(拓扑强制与目标一致), 颏下=下巴尖+12px。"""
    lpc = np.array([lm[468][0], lm[468][1]])   # 左瞳(可信)
    rpc = np.array([lm[473][0], lm[473][1]])   # 右瞳(可信)
    hw = 48.0                                   # 半眼宽(由媒体面检差确定)
    lout = lpc - (hw, 0); lin = lpc + (hw, 0)   # 左眼外/内角
    rout = rpc + (hw, 0); rin = rpc - (hw, 0)   # 右眼外/内角
    brow_dy = 55.0
    eye = [lout, lin, rout, rin, lpc, rpc]
    d = BROW_DX
    brow = [
        (lout[0] - d["outer"], lout[1] - brow_dy),      # 左眉外
        (lin[0] + d["inner"], lin[1] - brow_dy - 4),    # 左眉内
        (rout[0] + d["outer"], rout[1] - brow_dy),      # 右眉外
        (rin[0] - d["inner"], rin[1] - brow_dy - 4),    # 右眉内
    ]
    face = [(lm[i][0], lm[i][1]) for i in FACE_IDX]
    # 颏下 175: 强制在下巴尖 152 下方 12px
    face[-1] = (face[-2][0], face[-2][1] + 12)
    return eye + brow + face


def tps_solve(src, dst):
    n = len(dst)
    d = dst[:, None, :] - dst[None, :, :]
    r = np.sqrt(np.sum(d * d, axis=2))
    K = r * r * np.log(r + 1e-12)
    P = np.ones((n, 3))
    P[:, 1] = dst[:, 0]
    P[:, 2] = dst[:, 1]
    A = np.zeros((n + 3, n + 3))
    A[:n, :n] = K
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
    os.makedirs(outdir, exist_ok=True)
    lm, img_orig = detect(master_p)
    img = mask_face(lm, img_orig)

    src_pts = np.array(build_anchors(lm), np.float64)
    dst_pts = np.array(DST_PTS, np.float64)
    print("anchor pairs (src -> dst):")
    for s, d in zip(src_pts, dst_pts):
        print(f"  ({s[0]:.0f},{s[1]:.0f}) -> ({d[0]},{d[1]})")

    solx, soly = tps_solve(src_pts, dst_pts)
    ys, xs = np.mgrid[0:2048, 0:2048]
    pts = np.stack([xs.ravel(), ys.ravel()], axis=1)
    mp_xy = tps_map(pts, dst_pts, solx, soly)
    map_x = mp_xy[:, 0].reshape(2048, 2048).astype(np.float32)
    map_y = mp_xy[:, 1].reshape(2048, 2048).astype(np.float32)
    warped = cv2.remap(img, map_x, map_y, cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)
    cv2.imwrite(os.path.join(outdir, "warped_full.png"), warped)

    base = cv2.imread(base_p)
    x0, y0, x1, y1 = FACE_BBOX
    mask = np.zeros((2048, 2048), np.float32)
    mask[y0:y1, x0:x1] = 1.0
    mask = cv2.GaussianBlur(mask, (121, 121), 0)

    out = (base.astype(np.float32) * (1.0 - mask[:, :, None])
           + warped.astype(np.float32) * mask[:, :, None]).astype(np.uint8)
    outname = sys.argv[4] if len(sys.argv) > 4 else "head_female_x15_d"
    cv2.imwrite(os.path.join(outdir, "preview_" + outname + ".png"), out)
    cv2.imwrite(os.path.join(outdir, outname + ".png"), out)
    print("saved", outname + ".png")


if __name__ == "__main__":
    main()
