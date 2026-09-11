# -*- coding: utf-8 -*-
"""三点标定：用户在 ModKit 里找到的三个砦 ↔ 太阁坐标 → 拟合仿射变换（6 参数），推算全表。"""
import csv
import io
import json
import numpy as np
from PIL import Image, ImageDraw
Image.MAX_IMAGE_PIXELS = None

ROOT = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
CSV = ROOT + r"\Knowledge\太阁5\骑砍2织丰角色ID对应\csv\Settlements.csv"
SRC = ROOT + r"\tools\ExportHeightMatMap\SourceMap\TaikouMap2.png"
OUT = ROOT + r"\output"

# 用户在 ModKit 里量的世界坐标
CALIB = {
    "castle_tk258": (1775.0, 976.0),   # 十三凑之砦
    "castle_tk273": (214.0, 190.0),    # 坊津之砦
    "castle_tk266": (868.0, 400.0),    # 洲本之砦
}

rows = {r["id"]: r for r in csv.DictReader(io.open(CSV, encoding="utf-8-sig"))}
P = []
for sid, (wx, wy) in CALIB.items():
    r = rows[sid]
    tk = (float(r["TK5_X"]), float(r["TK5_Y"]))
    P.append((tk, (wx, wy), r["Name_All"]))
    print("%-14s %-6s 太阁(%.0f, %.0f)  ↔ 骑砍(%.0f, %.0f)" % (sid, r["Name_All"], tk[0], tk[1], wx, wy))

# 解仿射：world = A·tk5 + b（3 点 = 6 方程，恰好定 6 未知）
M = np.array([[t[0], t[1], 1.0] for t, _w, _n in P])
W = np.array([w for _t, w, _n in P])
sol = np.linalg.solve(M, W)          # 3x3 解 → 每列一个输出分量
A = sol[:2, :].T                     # [[a, b],[c, d]]  world_x = a*tx + b*ty + e ; world_y = c*tx + d*ty + f
e, f = sol[2, 0], sol[2, 1]
print("\n仿射: world_x = %.5f*tx + %.5f*ty + %.2f" % (A[0, 0], A[0, 1], e))
print("      world_y = %.5f*tx + %.5f*ty + %.2f" % (A[1, 0], A[1, 1], f))

# 几何解读：缩放 / 旋转 / 是否翻转
sx = np.hypot(A[0, 0], A[1, 0])
sy = np.hypot(A[0, 1], A[1, 1])
det = A[0, 0] * A[1, 1] - A[0, 1] * A[1, 0]
print("  x 轴缩放 %.3f m/太阁格    y 轴缩放 %.3f    det=%.1f（负 = 含镜像翻转）" % (sx, sy, det))
print("  （参照：1.75 等比假设相当于 5.09 m/格）")


def tk5_to_world(tx, ty):
    return A[0, 0] * tx + A[0, 1] * ty + e, A[1, 0] * tx + A[1, 1] * ty + f


# 校验：三个标定点应零误差；另看「京」这类既有手工点
print("\n== 校验 ==")
for sid in CALIB:
    r = rows[sid]
    wx, wy = tk5_to_world(float(r["TK5_X"]), float(r["TK5_Y"]))
    print("  %-14s 拟合后(%.1f, %.1f)  给定(%.1f, %.1f)  Δ=(%.2f, %.2f)"
          % (sid, wx, wy, CALIB[sid][0], CALIB[sid][1], wx - CALIB[sid][0], wy - CALIB[sid][1]))
for sid, note in (("village_tk212", "京之町（旧京手工校准 969.4/421.6）"),
                  ("town_tk000", "胜山馆/松前（应在北海道西南）"),
                  ("town_tk041", "江户城（应在关东）"),
                  ("village_tk233", "博多（应在九州北）"),
                  ("village_tk241", "鹿儿岛（应在九州南）")):
    r = rows.get(sid)
    if not r:
        continue
    wx, wy = tk5_to_world(float(r["TK5_X"]), float(r["TK5_Y"]))
    print("  %-14s %-28s → (%.0f, %.0f)" % (sid, note, wx, wy))

# 全表落点叠加到素材图（看是否贴着日本列岛）
im = Image.open(SRC).convert("RGB")
W_, H_ = im.size
ov = im.copy()
d = ImageDraw.Draw(ov)
for r in rows.values():
    wx, wy = tk5_to_world(float(r["TK5_X"]), float(r["TK5_Y"]))
    px, py = wx / 2048.0 * W_, (1.0 - wy / 1280.0) * H_
    col = {"城": (255, 40, 40), "町": (255, 200, 0), "里": (170, 60, 255), "砦": (0, 130, 255)}[r["TK5Type"]]
    d.ellipse([px - 2, py - 2, px + 2, py + 2], fill=col)
ov.save(OUT + r"\geo_overlay_calib3.png")
print("\n叠加图 →", OUT + r"\geo_overlay_calib3.png")

json.dump({"affine": {"m11": float(A[0, 0]), "m12": float(A[0, 1]), "tx": float(e),
                      "m21": float(A[1, 0]), "m22": float(A[1, 1]), "ty": float(f)},
           "world": [2048, 1280],
           "calib_points": {k: {"world": v, "tk5": [float(rows[k]["TK5_X"]), float(rows[k]["TK5_Y"])]}
                            for k, v in CALIB.items()},
           "note": "三点标定（用户 ModKit 实测）→ 仿射变换，精确穿过三个标定点；"
                   "改标点=改本文件重跑（gen_settlements_csv.py 会重新算 MOD_X/MOD_Y）"},
          io.open(ROOT + r"\Knowledge\太阁5\骑砍2织丰角色ID对应\_analysis\mod_coord_transform.json",
                  "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("参数 → _analysis/mod_coord_transform.json")
