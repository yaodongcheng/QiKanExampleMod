# gen_transition_pair.py —— 生成「过渡对」：两张**互补 alpha** 的贴图（2026-10-06）
#
# 干什么：做"冰面被火焰从西边入侵、最终全烧成火"这类**地表过渡**。
#
# 原理（详见 Knowledge/骑砍2贴花系统.md 卷首「2026-10-06 定案」§十）：
#   · 贴图的 alpha 就是一张**高度场** f —— 亮=1「先进方(火)的地盘」、暗=0「后退方(冰)的地盘」
#   · 分界线 = f 的等高线（水位线）；运行期只推**一个数**（水位 L），分界线就整条扫过去
#   · 两层 alpha **互补**（一张是另一张的反相）⇒ 绘制区域**不重叠** ⇒ 多层也不怕绘制顺序
#   · 运行期（SurfaceDecalFx）：火 factor.a = 阈值/L，冰 factor.a = 阈值/(1−L)
#     （有效阈值 = 阈值÷factor.a 的老关系，这里用它来**推分界线位置**）
#
# 🔴 阈值要取小（如 0.01）+ alpha 下限也要小（如 0.02）—— 否则水位扫到两端会**留残边**
#    （factor.a 最大只能到 1 ⇒ 有效阈值最低只能到「阈值」）。
#
# 用法：
#   python gen_transition_pair.py <先进方源png> <后退方源png> <输出目录> [方向=we] [alpha下限=0.02]
#     方向：we=西→东（火从西来）/ ew=东→西 / ns=北→南 / sn=南→北
#     产出：<输出目录>/<先进方名>_A.png 与 <后退方名>_B.png
import sys, os, struct
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

import numpy as np
from PIL import Image
from scipy import ndimage


def strip_chunks(data: bytes) -> bytes:
    out = bytearray(data[:8]); p = 8
    while p + 8 <= len(data):
        ln = struct.unpack(">I", data[p:p + 4])[0]
        typ = data[p + 4:p + 8]
        if typ in (b"IHDR", b"IDAT", b"IEND"):
            out += data[p:p + 12 + ln]
        p += 12 + ln
    return bytes(out)


def fill_rgb(path, S):
    """把源图的 RGB 等比放大铺满整幅。
    🔴 源图**透明区（以及半透明边缘）的 RGB 通常是 0（黑）** —— 直接缩放铺满会在画面上带出黑块
       （2026-10-07 实机：火层边缘一条黑带、冰层看着像"没盖住"）。
    ✅ 做法：用**距离变换**把每个待填像素指向**最近的不透明像素**、抄它的 RGB
       （颜色自然向外延伸）。⚠️ 别用"中位色" —— 岩浆图的中位色是暗的（大部分是暗岩、亮的是裂缝），
       填出来整张还是暗的 ✗。"""
    im = Image.open(path).convert("RGBA")
    arr = np.asarray(im).astype(np.float32)
    a = arr[:, :, 3]
    rgb = arr[:, :, :3].copy()

    solid = a > 40                       # 阈值取高一点：半透明边缘的 RGB 往往也是黑的，一起填掉
    if solid.any():
        idx = ndimage.distance_transform_edt(~solid, return_distances=False, return_indices=True)
        rgb = rgb[idx[0], idx[1]]        # 每个像素 → 最近的不透明像素的 RGB
    else:
        rgb[:, :] = 128

    ys, xs = np.nonzero(a > 8)
    if len(ys) == 0:
        ys, xs = np.array([0]), np.array([0])
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    rgb_crop = rgb[y0:y1, x0:x1]

    mh, mw = rgb_crop.shape[:2]
    k = S / max(mw, mh)
    nw, nh = max(1, int(round(mw * k))), max(1, int(round(mh * k)))
    big = np.asarray(Image.fromarray(rgb_crop.astype(np.uint8)).resize((nw, nh), Image.LANCZOS)).astype(np.float32)
    out = np.zeros((S, S, 3), np.float32)
    if solid.any():
        out[:, :] = np.median(rgb[solid], axis=0)    # 画布没铺到的边角：用形状内中位色垫底（不再留黑）
    ox, oy = (S - nw) // 2, (S - nh) // 2
    cw, ch = min(nw, S - max(0, ox)), min(nh, S - max(0, oy))
    out[max(0, oy):max(0, oy) + ch, max(0, ox):max(0, ox) + cw] = \
        big[max(0, -oy):max(0, -oy) + ch, max(0, -ox):max(0, -ox) + cw]
    return out


def main():
    a_src, b_src, outdir = sys.argv[1], sys.argv[2], sys.argv[3]
    d = sys.argv[4].lower() if len(sys.argv) > 4 else "we"
    lo = float(sys.argv[5]) if len(sys.argv) > 5 else 0.02
    S = 1024
    os.makedirs(outdir, exist_ok=True)

    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    t = (S - 1)
    ramp = {
        "we": 1.0 - xx / t,      # 西(1) → 东(0)：先进方从西边来
        "ew": xx / t,
        "ns": 1.0 - yy / t,      # 北(1) → 南(0)
        "sn": yy / t,
    }.get(d, 1.0 - xx / t)

    # 🔴 autha 区间必须**上下都留一点**：
    #   下限 > 材质阈值（本项目 0.01）⇒ 水位推到 0 时先进方才能**盖满**；
    #   上限 < 1 ⇒ 水位推到 1 时后退方才能被**裁干净**（若上限=1，那一小条 alpha=1 的像素会留下来 = 残边）。
    hi = 1.0 - lo
    f = lo + (hi - lo) * ramp

    def save(alpha01, rgb_path, name):
        rgb = fill_rgb(rgb_path, S)
        arr = np.dstack([rgb, np.clip(alpha01 * 255.0, 0, 255)]).astype(np.uint8)
        p = os.path.join(outdir, name); tmp = p + ".tmp.png"
        Image.fromarray(arr, "RGBA").save(tmp)
        open(p, "wb").write(strip_chunks(open(tmp, "rb").read()))
        os.remove(tmp)
        chk = np.asarray(Image.open(p).convert("RGBA"))[:, :, 3] / 255.0
        print(f"  写出 {name}: alpha 范围 [{chk.min():.3f}, {chk.max():.3f}]  "
              f"左/中/右 = {chk[S//2, S//8]:.2f} / {chk[S//2, S//2]:.2f} / {chk[S//2, S*7//8]:.2f}")

    base_a = os.path.splitext(os.path.basename(a_src))[0]
    base_b = os.path.splitext(os.path.basename(b_src))[0]
    print(f"方向={d}  alpha 区间=[{lo:.2f}, {hi:.2f}]")
    save(f, a_src, base_a + "_A.png")                      # 先进方（火）
    save(1.0 - f, b_src, base_b + "_B.png")                # 后退方（冰）—— **精确互补**
    print("  （互补自检：A + B 应恒为 1 ⇒ 同一水位 L 处 A 显示 f≥L、B 显示 f≤L，不重叠也不留缝）")


if __name__ == "__main__":
    main()
