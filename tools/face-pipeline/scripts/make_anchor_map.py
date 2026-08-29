# 画"锚点标记图"并渲染, 用于校准男 UV 五官锚点(自我校验闭环)
# 流程: TGT 锚点画在 2048 画布(数字十字) -> render_head 渲染正脸
#       -> 观察渲染图中哪个标记落在眼睛/鼻/嘴/下巴的正确 3D 位置 -> 修正 TGT
import numpy as np
import cv2

TGT = {
    "eyeR": (0.104, 0.288),
    "eyeL": (0.172, 0.288),
    "browC": (0.110, 0.335),
    "nose": (0.021, 0.236),
    "noseBase": (0.021, 0.212),
    "mouth": (0.060, 0.190),
    "mouthR": (0.092, 0.190),
    "mouthL": (0.028, 0.190),
    "chin": (0.135, 0.089),
    "tempL": (0.205, 0.310),
    "tempR": (0.030, 0.300),
}

W = H = 2048
canvas = np.full((H, W, 3), 70, np.uint8)
colors = [(255, 0, 0), (0, 255, 0), (0, 128, 255), (255, 255, 0), (255, 0, 255),
          (0, 255, 255), (255, 128, 0), (128, 255, 128), (255, 255, 255),
          (128, 0, 255), (0, 0, 128)]
for i, (name, (u, v)) in enumerate(TGT.items()):
    x = int(u * W); y = int((1.0 - v) * H)
    cv2.drawMarker(canvas, (x, y), colors[i % len(colors)], cv2.MARKER_CROSS, 40, 3)
    cv2.putText(canvas, str(i), (x + 18, y - 12), cv2.FONT_HERSHEY_SIMPLEX, 0.9,
                (0, 0, 0), 4)
    cv2.putText(canvas, str(i), (x + 18, y - 12), cv2.FONT_HERSHEY_SIMPLEX, 0.9,
                colors[i % len(colors)], 2)
    print(i, name, u, v)
cv2.imwrite(r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\face-pipeline\data\output\anchor_mark.png", canvas)
print("saved")
