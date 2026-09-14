# -*- coding: utf-8 -*-
"""
check_head_space.py — 头部网格"落点检查"：把编译产物和能用的参照头量在一起比。

为什么需要它（2026-09-13 教训）：
  蒂法换头工程卡了三轮"头看不见"，真因是编译出来的网格被放大 100 倍 + 绕 X 翻转
  180°（FBX 节点上挂了 scale=100，且声明 UpAxis=Y 导致引擎多做一次轴转换）。
  这类错误**在编辑器预览里看不出来**——预览会自动取景，放大 100 倍的头和正常的头
  长得一模一样。唯一的照妖镜是**量坐标**：把编译后的顶点包围盒和"实机验证过能用"
  的参照头摆在一起比。

用法：
  python check_head_space.py --pack <AssetPackages 目录> [--mesh head_tifa_a]
                             [--ref-pack <参照模块的 AssetPackages>] [--ref head_xxfemale_a]

退出码：0 = 通过；1 = 不合格（可用于流水线门禁）。
"""
import argparse
import glob
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
TPACCLI = os.path.normpath(os.path.join(
    HERE, '..', 'tpactool', 'TpacToolCLI', 'bin', 'Release', 'net9.0', 'tpaccli.exe'))
DEFAULT_REF_PACK = (r'H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord'
                    r'\Modules\xxFemaleHead\AssetPackages')

# 参照头（head_xxfemale_a，实机验证可用的第三方替换头）的实测着陆窗口。
# 位置是硬约束；尺寸给宽容区间，因为不同头型的宽窄本来就有差别。
REF_WINDOW = {
    'x': (-0.11, 0.11),
    'y': (-0.09, 0.17),   # 正面 = +Y（眼睛在前）
    'z': (1.40, 1.80),    # 站立时头部离地高度（米）
}
SIZE_TOL = 2.0  # 尺寸允许误差倍数


def tpaccli_dump(pack_dir, mesh, out_dir):
    if not os.path.exists(TPACCLI):
        sys.exit('tpaccli not found: %s (run: dotnet build -c Release in tools/face-pipeline/tpactool/TpacToolCLI)' % TPACCLI)
    subprocess.run([TPACCLI, 'dump', '--packdir', pack_dir, '--filter', mesh,
                    '--format', 'obj', '--out', out_dir],
                   check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def obj_bboxes(path):
    """返回 {对象名: (minx,maxx,miny,maxy,minz,maxz)}"""
    cur, bb = None, {}
    with open(path, encoding='utf-8', errors='ignore') as f:
        for line in f:
            if line.startswith('o '):
                cur = line[2:].strip()
                bb[cur] = [1e9, -1e9, 1e9, -1e9, 1e9, -1e9]
            elif line.startswith('v ') and cur:
                x, y, z = map(float, line[2:].split()[:3])
                b = bb[cur]
                b[0] = min(b[0], x); b[1] = max(b[1], x)
                b[2] = min(b[2], y); b[3] = max(b[3], y)
                b[4] = min(b[4], z); b[5] = max(b[5], z)
    return bb


def merged(bb):
    """把子网格合成一个总包围盒（排除 morph 帧那种退化对象）"""
    pts = [v for v in bb.values() if v[1] > v[0] or v[3] > v[2] or v[5] > v[4]]
    if not pts:
        return None
    return (min(p[0] for p in pts), max(p[1] for p in pts),
            min(p[2] for p in pts), max(p[3] for p in pts),
            min(p[4] for p in pts), max(p[5] for p in pts))


def fmt(b):
    return ('x[%8.4f,%8.4f] y[%8.4f,%8.4f] z[%8.4f,%8.4f]  size %.4f x %.4f x %.4f'
            % (b[0], b[1], b[2], b[3], b[4], b[5],
               b[1] - b[0], b[3] - b[2], b[5] - b[4]))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--pack', required=True, help='待检查模块的 AssetPackages 目录')
    ap.add_argument('--mesh', default='head_tifa_a')
    ap.add_argument('--ref-pack', default=DEFAULT_REF_PACK)
    ap.add_argument('--ref', default='head_xxfemale_a')
    ap.add_argument('--window', default=None,
                    help='着陆窗口，格式 "x0,x1,y0,y1,z0,z1"（默认用 REF_WINDOW = 女头的量值）。'
                         '男头比女头大：实测 head_male_a 是 x±0.093 y[-0.071,0.172] z[1.414,1.811]，'
                         '跑男头时要给 --window "-0.12,0.12,-0.09,0.19,1.40,1.83"')
    a = ap.parse_args()

    if a.window:
        v = [float(t) for t in a.window.split(',')]
        if len(v) != 6:
            sys.exit('--window 需要 6 个数：x0,x1,y0,y1,z0,z1')
        REF_WINDOW['x'] = (v[0], v[1])
        REF_WINDOW['y'] = (v[2], v[3])
        REF_WINDOW['z'] = (v[4], v[5])
        print('=== window override: x%s y%s z%s ===' % (REF_WINDOW['x'], REF_WINDOW['y'], REF_WINDOW['z']))

    tmp = tempfile.mkdtemp(prefix='headspace_')

    print('=== reference head: %s ===' % a.ref)
    tpaccli_dump(a.ref_pack, a.ref, os.path.join(tmp, 'ref'))
    ref_objs = glob.glob(os.path.join(tmp, 'ref', '**', '*.obj'), recursive=True)
    if not ref_objs:
        sys.exit('reference dump failed: %s @ %s' % (a.ref, a.ref_pack))
    ref = merged(obj_bboxes(ref_objs[0]))
    print('   ' + fmt(ref))

    print('=== candidate: %s ===' % a.mesh)
    tpaccli_dump(a.pack, a.mesh, os.path.join(tmp, 'cur'))
    cur_objs = glob.glob(os.path.join(tmp, 'cur', '**', '*.obj'), recursive=True)
    if not cur_objs:
        sys.exit('candidate dump failed: %s @ %s' % (a.mesh, a.pack))
    cur = merged(obj_bboxes(cur_objs[0]))
    print('   ' + fmt(cur))

    print('=== verdict ===')
    fails = []

    # 1) 整体尺度
    ref_span = max(ref[1] - ref[0], ref[3] - ref[2], ref[5] - ref[4])
    cur_span = max(cur[1] - cur[0], cur[3] - cur[2], cur[5] - cur[4])
    ratio = cur_span / ref_span if ref_span else float('inf')
    if ratio > SIZE_TOL or ratio < 1.0 / SIZE_TOL:
        fails.append('size off by %.1fx (reference %.4f m, candidate %.4f m)'
                     ' -- usually a non-unit scale on FBX Model nodes baked in by the editor' % (ratio, ref_span, cur_span))
    else:
        print('  [OK] size ratio = %.3f' % ratio)

    # 2) 三轴落点
    for i, axis in enumerate('xyz'):
        lo, hi = REF_WINDOW[axis]
        c_lo, c_hi = cur[i * 2], cur[i * 2 + 1]
        if c_hi < lo or c_lo > hi:
            fails.append('axis %s fully outside window: candidate [%.4f, %.4f], expected near [%.2f, %.2f]'
                         % (axis, c_lo, c_hi, lo, hi))

    # 3) 脸朝向（眼睛必须在 +Y 一侧）
    if cur[3] <= 0:
        fails.append('face points backwards: max +Y = %.4f (front must face +Y, eyes at y>0)' % cur[3])

    # 4) 高度必须是正的（负 = 翻到地面以下）
    if cur[5] < 0:
        fails.append('head is below ground: max z = %.4f < 0 (classic symptom of an extra axis rotation)' % cur[5])

    if fails:
        for f in fails:
            print('  [FAIL] ' + f)
        print('\nVERDICT: FAIL -- do not launch the game, fix the FBX first.')
        return 1

    print('\nVERDICT: PASS -- landing matches the reference head, safe to test in game.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
