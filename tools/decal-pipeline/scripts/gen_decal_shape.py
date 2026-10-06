# gen_decal_shape.py —— 生成「贴花形状贴图」：近圆 + **抛物面** alpha 坡（2026-10-06 实机通过）
#
# 解决什么问题：
#   贴花要能"慢慢消失"。硬裁（`alpha_test`）下这件事有数学约束，拍脑袋调不出来 —— 本脚本把配方固化。
#   原理与推导：[Knowledge/骑砍2贴花系统.md](../../../Knowledge/骑砍2贴花系统.md) 卷首「2026-10-06 定案（续）」
#   可复用配方：[plans/rules/wheels.d/assets.md](../../../plans/rules/wheels.d/assets.md) §21.11
#
# 三条要点：
#   ① 形状要**接近内切圆**。焦痕自己的轮廓"带缺口"：同样外接尺寸下面积远小于圆
#      （实测外接 84% 时面积只有 47%；内切圆是 π/4 = 78.5%）。
#      半径按角度加三组正弦扰动（±10%）即可 —— 有起伏、不是呆板的正圆。
#   ② alpha 随半径要成**抛物面**，不是直线坡：
#          alpha(r) = 1 − (1 − 阈值) × (r / 半径)²
#      因为 面积 ∝ 半径²，要让面积**线性**缩就得 半径 ∝ √(1−p)。
#      附带白赚：形状边缘的 alpha 恰好 = 阈值（正好卡在裁剪线上）⇒ **满亮时不被啃**；
#      直线坡会被啃掉 阈值 那一圈（满亮只剩 1−阈值 直径、面积只剩 (1−阈值)²）。
#   ③ **按阈值归一化**：系数 (1−阈值) 必须与你材质里设的 `alphaTest` **一致**，
#      否则"满亮正好卡在裁剪线"这个性质就没了。
#
# 用法：python gen_decal_shape.py <痕迹源png> <输出png> [起伏幅度=0.10] [阈值=0.20]
#   源 png 用 ModKit 工程 `AssetSources/` 里的**原始贴图**（别用从 tpac 导出的解码版 —— 那是二次压缩）。
#   输出之后走 `tpaccli texreplace --alpha` 写回发布包（见 tools/tpactool）。
#   ⚠️ 离线改发布产物 = 与工程源分叉，**每次 Publish 都会冲掉**。
import sys, os, struct
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

import numpy as np
from PIL import Image


def strip_chunks(data: bytes) -> bytes:
    out = bytearray(data[:8]); p = 8
    while p + 8 <= len(data):
        ln = struct.unpack(">I", data[p:p + 4])[0]
        typ = data[p + 4:p + 8]
        if typ in (b"IHDR", b"IDAT", b"IEND"):
            out += data[p:p + 12 + ln]
        p += 12 + ln
    return bytes(out)


def main():
    src, dst = sys.argv[1], sys.argv[2]
    amp = float(sys.argv[3]) if len(sys.argv) > 3 else 0.10
    thr = float(sys.argv[4]) if len(sys.argv) > 4 else 0.20   # 与材质 alphaTest 一致（抛物面按它归一化）

    im = Image.open(src).convert("RGBA")
    arr = np.asarray(im).astype(np.float32)
    S = arr.shape[0]
    rgb, a0 = arr[:, :, :3], arr[:, :, 3] / 255.0

    # ① RGB：焦痕外接矩形等比放大到铺满整幅（RGB 与 alpha 分开缩放，防透明区 RGB 被清零）
    ys, xs = np.nonzero(a0 > 2 / 255.0)
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    mh, mw = y1 - y0, x1 - x0
    k = S / max(mw, mh)
    nw, nh = max(1, int(round(mw * k))), max(1, int(round(mh * k)))
    rgb_big = np.asarray(Image.fromarray(rgb[y0:y1, x0:x1].astype(np.uint8))
                         .resize((nw, nh), Image.LANCZOS)).astype(np.float32)
    rgb_new = np.zeros((S, S, 3), np.float32)
    ox, oy = (S - nw) // 2, (S - nh) // 2
    rgb_new[oy:oy + nh, ox:ox + nw] = rgb_big[:min(nh, S - oy), :min(nw, S - ox)]

    # ② alpha：近圆边界 + **抛物面**径向坡
    #
    # 🔴 为什么是抛物面而不是直线坡：
    #   可见面积 ∝ 半径²。要让"面积随淡出均匀缩小"（观感才均匀），必须让 半径 ∝ √(1−p)。
    #   又因为有效阈值 T 已经被补偿成**线性**推进（kAlpha = 阈值 ÷ T），于是要求
    #        面积 ∝ (1−T)/(1−阈值)   ⇒   半径² ∝ (1−T)   ⇒   T = 1 − (1−阈值)·(r/半径)²
    #   即 **alpha(r) = 1 − (1−阈值)·(r/半径)²**（抛物面）。
    #   直线坡会让面积按 (1−T)² 收缩 = 前快后慢。
    #   附带好处：抛物面按阈值归一化后，圆边的 alpha **恰好等于阈值**（卡在裁剪线上）
    #   ⇒ 满亮时是**完整的圆**（直线坡会被啃掉 阈值 那一圈，只剩 1−阈值 直径）。
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    cy = cx = (S - 1) / 2.0
    dx, dy = xx - cx, yy - cy
    r = np.sqrt(dx * dx + dy * dy) / (S / 2.0)          # 0=圆心, 1=内切圆
    th = np.arctan2(dy, dx)
    # 三组不同频率的正弦 ⇒ 起伏不规则但周长圆滑（不是正圆）
    edge = 1.0 + amp * (0.55 * np.sin(3 * th + 1.1)
                        + 0.30 * np.sin(7 * th + 0.4)
                        + 0.15 * np.sin(11 * th + 2.3))
    a_new = np.clip(1.0 - (1.0 - thr) * (r / np.maximum(edge, 1e-6)) ** 2, 0.0, 1.0)

    out = np.dstack([rgb_new, np.clip(a_new * 255, 0, 255)]).astype(np.uint8)
    os.makedirs(os.path.dirname(dst) or ".", exist_ok=True)
    tmp = dst + ".tmp.png"
    Image.fromarray(out, "RGBA").save(tmp)
    open(dst, "wb").write(strip_chunks(open(tmp, "rb").read()))
    os.remove(tmp)

    chk = np.asarray(Image.open(dst).convert("RGBA"))[:, :, 3].astype(np.float32) / 255.0
    print(f"写出 {dst}  (近圆 + 抛物面坡，按阈值 {thr} 归一化；扰动 ±{amp*100:.0f}%)")
    print(f"  非零 {100*(chk>0).mean():.1f}%  (内切圆理论 78.5%)   alpha 恰好=1.0 占比 {100*(chk>=1.0).mean():.3f}%")
    for T in (0.0235, 0.1, 0.15, 0.2, 0.3, 0.5, 0.7, 0.9):
        v = chk >= T
        ys2, xs2 = np.nonzero(v)
        d = (xs2.max() - xs2.min() + 1) if len(xs2) else 0
        print(f"    阈值 {T:<7} 直径 {d:4d} px = 方框 {100*d/S:4.0f}%   面积 {100*v.mean():5.1f}%")


if __name__ == "__main__":
    main()
