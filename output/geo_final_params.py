# -*- coding: utf-8 -*-
"""定稿首过换算参数：等比例 1.75 + 京锚精确穿过（用户手工校准点零误差，最保守选择）。"""
import io, json, os

ROOT = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
W, H = 704, 448
AX, AY = 214.0, 183.0                                     # 京之町（太阁坐标）
PX0, PY0 = 969.424 / 2048.0 * W, (1 - 421.563 / 1280.0) * H
K = 1.75
ox, oy = PX0 - K * AX, PY0 - K * AY
print("京锚像素 = (%.2f, %.2f)  →  ox=%.3f oy=%.3f" % (PX0, PY0, ox, oy))
p = os.path.join(ROOT, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "_analysis", "mod_coord_transform.json")
json.dump({"kx": K, "ox": round(ox, 3), "ky": K, "oy": round(oy, 3),
           "img": [W, H], "world": [2048, 1280],
           "anchor": {"tk5": [AX, AY], "world": [969.424, 421.563]},
           "note": "首过估算：等比例 1.75（素材图 = 太阁坐标空间 402x256 的 1.75 倍）+ 京锚精确穿过；"
                   "残差约 50~100m（太阁图为风格化变形画法），待游戏内目视校准后改本文件重跑"},
          io.open(p, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("已写", p)
for nm, (tx, ty) in (("京之町(町213)", (214, 183)), ("二条城(城105)", (214, 180)),
                     ("松前/胜山馆", (383, 52)), ("博多(町233)", (70, 182)),
                     ("鹿儿岛(町241)", (54, 233)), ("江户(城41)", (317, 191))):
    px, py = K * tx + ox, K * ty + oy
    print("  %-14s 世界(%.0f, %.0f)" % (nm, px / W * 2048, (1 - py / H) * 1280))
