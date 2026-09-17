# -*- coding: utf-8 -*-
"""check_assembly.py —— 拼装闸门：按【源变换 T】把头/甲/兜拼回一个整体，量三条判据。

T（源变换）的定义 —— **唯一实现在 tools/sw2-pipeline/src_transform.py**，本脚本只读它的表
-------------------------------------------------------------------------------
    s    = 1.569 / 源模型头骨（bone_11）世界 z      # 1.569 = 原版 bip01_head_13 的世界 z
    T(v) = ( s·x,  s·(−y),  s·(z − z_sole) )        # Y 轴镜像（保 x）+ 等比缩放 + 源脚底落 z=0

🔴 是 **Y 轴镜像**（镜像面 = 额状面：保 x、翻 y），**不是 180° 绕 Z 偏航** —— 后者会把源模型的
   左腿甩到 +x 去（左右反，实测误差虚高 18cm），与 `build_armor.py` 的 MIRROR_Y 同一约定。
   （反射的行列式 = −1 → 三个 builder 镜像完必须反转面绕序，否则法线朝里；闸门只量点，不管绕序。）
🔴 s / z_sole 一律读 `out/srcT.json`（管线用的同一把尺）；表里没有该角色才回退到"自算"口径并打警告
   —— 自算那份锚的是**含发/兜总高**，与管线的**头骨锚**口径不同，实测差 3~37%（家康大黑头巾 1.365）。

三条判据（全过才算过）—— 🔴 2026-09-17 用户裁定：口径 = **整体观感**
---------------------------------------------------------------
  ① 对接（整体观感）：把脖子一圈分成 12 档（每档 30°），逐档量
     「头侧下沿 vs 甲领口上沿」，**只看"最长连续不重叠弧"**：
       · 某档 **重叠** = 头侧下沿 ≤ 领口上沿（含相等 = 共用面/共用顶点/共用边，允许）；
         该档 **露缝** = 头侧下沿 > 领口上沿（中间那段脖子没人盖）。
       · 硬判 = **一圈里最长的一条连续露缝弧 ≤ --max-arc**（默认 60°，可调）。
       · 逐档「必须 ≥ 5mm 余量」的老口径**降级为诊断**（印出来看，不参与总判）——
         用户原话："允许在头、铠甲的接缝处存在共用面/共用顶点/共用边，所谓严丝合缝是整体观感的"。
  ①' 单档 gap（2026-09-17 加）：**任何一档**的 gap > `--gap-max`（默认 30mm）即**不过** ——
     在弧判据之外**独立生效**。为什么还要它：弧判据只数"连了几档"，一档孤零零的大缝
     （前后都被重叠档夹着）照样算 30° 通过，可那一档仍然是肉眼可见的洞
     （实测秀吉 30° 档 +49mm、长政 240° 档 +28mm）。两条判据一起才是完整口径。
      · 头侧下沿 = `build_head.carve_neck_part` 抠出来的那截脖子的最低点 —— **直接调它的同一个
        实现**（判据、阈值、肤色判据都不在闸门里抄第二份），甲侧同步摘掉被它搬走的顶点。
        🔴 **参照色必须与管线同源**：管线（build_heads.py --pick-idx）的 face 组**含头发**、
        闸门原来只取 `parts_table.face` → 参照色不同 → 抠出的脖子件名单不一致
        （实测兰丸：管线抠到 26 顶点、闸门一个都看不见）。现统一走 `parts_table.face_idx()`。
        🔴 **不是每个人都有脖子件**（实测宁宁：脸壳本身就盖住整个可见颈部，几何上唯一"像脖子"
        的是甲自己的领口金箍，已被 carve 的肤色判据正确拒掉）→ 抠不到就用**头侧几何的最低点**
        （脸壳 + 头发 + 眼 + 兜合起来取最低；只取脸壳会出假数 —— 实测本多忠胜后脑是发/兜，
        脸壳在背后几档一块顶点都没有，单取脸壳会得到 1.6671 的假下沿），
        并在输出与 JSON 里标明用的是哪一个（这直接决定"这个人到底有没有缝"）。
        **没有脖子件 ≠ 不过**；两侧都取不到数据才不过。
      · 甲领口上沿 = **包住脖子的那条开口环**的上沿（见 `collar_ring`）—— 不能拿"所有近轴
        自由边的最高点"，那会被跨过 V 领的肩带/衬里顶高（实测差 5cm 量级）。
  ② 重合：给了成品 FBX（`--built-head` / `--built-armor`）时，接缝带顶点到「T(源件)」的最近距离
          → 要求 p95/max < 5mm（不给就跳过）。抓的是**实现走样**：T 是构造性的，本身偏差为 0。
  ③ 不穿模：领口上沿 + 5mm 之上，头/兜/脖子顶点不得落在甲网格**内部**，甲顶点也不得落在头网格内部
          （BVH 射线奇偶判定）。
  ④ 无孤立浮片（2026-09-17 加）：把「头 / 甲 / 兜」的件按连通域切开，量每片**到其它任何一片的
     最小距离**；> `--float-tol`（默认 50mm）的算「与本体脱离、落在别处」的碎片（战无2 源模型的
     老毛病：头旁边飘着甲片 —— 慶次就是这么被看出来的）。见 `float_check`。
  ⑤ 🔴 **成品对成品**（2026-09-17 加，**只在给了 `--built-head` 时生效**）：
     · 口径替换：给了 `--built-head` / `--built-armor` 时，**头侧下沿 / 甲领口上沿改用成品量**
       （成品头 FBX / 成品甲 FBX，都在游戏空间，环判据与源侧同一套实现）。
       理由：实机露馅的是**成品**，源件与成品之间隔着整条管线。实测宁宁：源件口径 12/12 档
       重叠 5~6cm（PASS），实机却是一圈看得见的缝 —— 见下面 ⑤'。
     · ⑤' **领口上方不许有朝内面**：成品头在「甲领口上沿 + 5mm ~ + 0.12 米」这一带里，
       面法线不得朝向轴（单面材质 + 背面剔除 → 朝内 = 看不见 = 透背景）。
       实测宁宁 8 个朝里面的中心 z 1.531~1.551 / r 0.047~0.096，正落在甲领口上沿之上。
       只数**脸壳件**（材质名 `_mouth` / `_eye` 的件排除：嘴腔/眼球内侧朝里是正常的）。

用法:
  blender -b --python check_assembly.py -- --key L47_nene [--target-h 1.802] [--margin 0.005]
             [--built-head <fbx> --built-armor <fbx>] [--json out.json] [--ring-verbose]
  blender -b --python check_assembly.py -- --all [--json out/assembly_all.json]
退出码: 0 全过 / 1 有不过 / 2 输入缺失（源模型/工具不在）
"""
import argparse
import bmesh
import collections
import csv
import io
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)                                                    # parts_table / src_transform
FACE_SCRIPTS = os.path.join(REPO, "tools", "face-pipeline", "scripts")      # build_head.py
sys.path.insert(0, FACE_SCRIPTS)

import bpy                                                    # noqa: E402
from mathutils import Vector                                  # noqa: E402
from mathutils.bvhtree import BVHTree                         # noqa: E402
import src_transform                                          # noqa: E402
from parts_table import TABLE, neck_src_idx, face_idx, neck_args   # noqa: E402

# 🔴 build_head.py 结尾是**裸调 main()**（没有 __name__ 守卫）→ 直接 import 会把整条构建管线跑一遍。
#    这里把最后那行摘掉再 exec（只改内存副本，不落盘、不动那个文件）。
_BH_PATH = os.path.join(FACE_SCRIPTS, "build_head.py")
_bh_src = io.open(_BH_PATH, encoding="utf-8").read()
_bh_lines = _bh_src.rstrip().splitlines()
if _bh_lines and _bh_lines[-1].strip() == "main()":
    _bh_src = "\n".join(_bh_lines[:-1]) + "\n"
BUILD_HEAD = {"__name__": "build_head_loaded", "__file__": _BH_PATH}
exec(compile(_bh_src, _BH_PATH, "exec"), BUILD_HEAD)
carve_neck_part = BUILD_HEAD["carve_neck_part"]
patch_importer = BUILD_HEAD["patch_importer"]

SRC_DIR = r"D:\BrainMaker\战国无双2资产解包分析\export\fbx"
SRC_TEX = r"D:\BrainMaker\战国无双2资产解包分析\web\textures"
TEX_BATCH = r"D:\BrainMaker\战国无双2资产解包分析\work\tex_batch"   # 贴图升级产物 <key>_d.png（管线优先用它）
CENSUS = os.path.join(REPO, "Debug", "offline", "sw2_census")
SRCT_JSON = os.path.join(HERE, "out", "srcT.json")
HEAD_SW = set(["bone_10", "bone_11"] + ["bone_%d" % i for i in range(46, 63)])
NB = 12
ANGLES = {0: "右0°", 3: "前90°", 6: "左180°", 9: "后270°"}
# carve_neck_part 的阈值 = build_head.py 的默认值（0.10 / 0.13 / 1.49 / 6 / 底切 1.41 / 肤色 0.16）。
# 🔴 **唯一来源 = parts_table.neck_args(key)**（默认 + 逐人覆写）—— 闸门要复现管线抠出来的
#    同一份脖子件，改一处就要改两处；谁都不许在这里另抄一份阈值。
NECK_ARGS = neck_args  # 兼容旧引用名：NECK_ARGS(key) → dict

# 🔴 硬判默认参数（2026-09-17 用户裁定「整体观感」口径）——都能用命令行覆盖
ARC_MAX = 60.0        # ① 一圈里最长连续"不重叠"弧的上限（度）
GAP_TOL = 0.0         # ① 逐档 gap ≤ 此值算「重叠」（0 = 共用面/顶点/边也算重叠，单位米）
GAP_MAX = 0.030       # ①' 任何**单档** gap 的上限（米）—— 弧判据之外独立生效，见 check_one
FLOAT_TOL = 0.05      # ④ 孤立浮片：与其它件的最小距离上限（米）

_CACHE = {}          # 逐角色缓存（frags / free_edges 都是按网格名重算的大头）


def bucket(x, y):
    return int(((math.degrees(math.atan2(y, x)) % 360.0) + 15.0) // (360.0 / NB)) % NB


def dom_i(ob, i):
    v = ob.data.vertices[i]
    if not ob.vertex_groups or not v.groups:
        return None
    return ob.vertex_groups[max(v.groups, key=lambda g: g.weight).group].name


def frags(ob):
    """连通域（碎片）+ 每片的主导骨名。"""
    ck = ("frag", ob.name)
    if ck in _CACHE:
        return _CACHE[ck]
    n = len(ob.data.vertices)
    par = list(range(n))

    def find(a):
        while par[a] != a:
            par[a] = par[par[a]]; a = par[a]
        return a
    for e in ob.data.edges:
        a, b = find(e.vertices[0]), find(e.vertices[1])
        if a != b:
            par[a] = b
    g = collections.defaultdict(list)
    for i in range(n):
        g[find(i)].append(i)
    out = []
    for vs in g.values():
        c = collections.Counter(dom_i(ob, i) for i in vs)
        out.append((vs, c.most_common(1)[0][0] if c else None))
    _CACHE[ck] = out
    return out


def free_edges(ob):
    ck = ("fe", ob.name)
    if ck in _CACHE:
        return _CACHE[ck]
    bm = bmesh.new(); bm.from_mesh(ob.data); bm.verts.ensure_lookup_table()
    s = set()
    for e in bm.edges:
        if len(e.link_faces) == 1:
            s.add(e.verts[0].index); s.add(e.verts[1].index)
    bm.free()
    _CACHE[ck] = s
    return s


def load_fbx(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    bpy.context.view_layer.update()
    return sorted([o for o in bpy.data.objects if o.type == 'MESH'], key=lambda o: o.name)


def side_pts(meshes, idxs, side, T, free_only=False, skip=()):
    """side='head' 只留头骨族（bone_10/11/46..62）碎片；'armor' 把它们剔掉（那部分归头/脖子）。
    free_only=True 只取自由边顶点（开口沿）。skip = 「脖子归头」已被 carve 拿走的顶点（(件号,顶点号)）。"""
    out = []
    for i in idxs:
        if not (0 <= i < len(meshes)):
            continue
        ob = meshes[i]; mw = ob.matrix_world
        fe = free_edges(ob) if free_only else None
        for vs, b in frags(ob):
            if (side == "head") != (b in HEAD_SW):
                continue
            for vi in vs:
                if fe is not None and vi not in fe:
                    continue
                if (i, vi) in skip:
                    continue
                out.append(T(mw @ ob.data.vertices[vi].co))
    return out


def ring_loops(meshes, idxs, side, T, skip=()):
    """把若干件的自由边按共享顶点并成环，返回 [(T空间点…, 主导骨) …]（环判据照 Debug/offline/_neck_gap.py：
    自由边 → 并查集按共享顶点分组 → 顶点数 < 4 的组不算环）。
    side 与 side_pts 同一套筛法（'armor' 剔头骨族碎片 + 剔已被 carve 拿走的脖子顶点）。"""
    parent = {}
    bone_of = {}

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]; x = parent[x]
        return x
    for i in idxs:
        if not (0 <= i < len(meshes)):
            continue
        ob = meshes[i]
        fe = free_edges(ob)
        for vs, b in frags(ob):
            if (side == "head") != (b in HEAD_SW):
                continue
            for vi in vs:
                if vi in fe and (i, vi) not in skip:
                    parent.setdefault((i, vi), (i, vi))
                    bone_of[(i, vi)] = b
        for e in ob.data.edges:
            a, b = (i, e.vertices[0]), (i, e.vertices[1])
            if a in parent and b in parent:
                ra, rb = find(a), find(b)
                if ra != rb:
                    parent[ra] = rb
    grp = collections.defaultdict(list)
    for k in parent:
        grp[find(k)].append(k)
    out = []
    for keys in grp.values():
        if len(keys) < 4:
            continue
        pts = [T(meshes[i].matrix_world @ meshes[i].data.vertices[vi].co) for (i, vi) in keys]
        cb = collections.Counter(bone_of[k] for k in keys).most_common(1)
        out.append((pts, cb[0][0] if cb else None))
    return out


Z_BAND = 0.06      # 领口上沿只在「头侧断口以下 6cm 内」取（见 collar_ring 判据④）
Z_LOW_ALLOW = 0.05  # 领口环至少要到「头侧下沿再往下 5cm」才算够高（见 collar_ring 判据②）


def collar_ring(loops, r_neck, z_hit, r_big=0.60, z_low=0.9, cover=10, z_band=Z_BAND):
    """挑「**包住脖子的那圈开口**」并给出逐档上沿。

    为什么不能拿「所有近轴自由边的最高点」当领口上沿（旧口径的错）：正前 V 领的上沿会被
    **跨过领口的肩带 / 衬里**顶高（实测两人各口径差 5cm 量级），而真正决定"脖子能不能被挡住"
    的是**包住脖子的那圈开口**。判据（每条都对着 `--ring-verbose` 量出来的真实环结构定，
    反例是实测的）：
      ① 最低点 ≥ z_low（太低 = 裙摆/腰线）· 最大半径 ≤ r_big（大环 = 裙摆/袖口，实测袖/摆环
         r 0.44~0.88）· 环上有一部分落进近轴带（min r ≤ r_neck —— 袖口/肩甲整环都在 0.12 以外，
         实测幸村肩甲环 min r 0.149）
      ② **最高点 ≥ z_hit − Z_LOW_ALLOW**（头侧下沿再往下放 5cm）—— 领口得够高，才盖得住头侧
         断口；不设这条会混进腰/胯那圈（实测宁宁腰环 z ≤ 1.135、12/12 档全覆盖，就落在近轴带里
         → 假领口，比头侧下沿低 27cm）。留 5cm 余量是因为"领口比头侧断口还低"本身就是判据①
         要报的**真 gap**（实测服部半藏领口环顶 1.520、头侧下沿 1.55 —— 差 3cm 是对的，不该被筛没）
      ③ **拼起来要绕满一圈**（环的角向覆盖合起来 ≥ cover/12 档）—— 甲是多片拼的，单条环常常
         只是半圈：实测幸村领口就是两个半环（各 7/12 档）拼成的一圈；按"单条得绕满"筛 =
         那个人判据① 直接没数据
      ④ 逐档上沿 = 该档上**所有贡献顶点的最高 z**（贡献 = 落在近轴带内 r ≤ r_neck、
         且 z ≥ z_hit − z_band 的自由边顶点）= 那圈开口的上沿。
         z_band 窗口 = 领口上沿只在头侧断口以下 6cm 内取（实测稻姬的门襟环一路掉到 z 1.17，
         不设窗口会拿门襟当领口 → 假 gap +352mm）；**跨过领口的那条肩带/衬里不是"绕一圈的环"，
         被判据①的环筛选挡在外面**（这正是旧口径"所有近轴自由边取最大"的病根）
    loops = [(点…, 主导骨) …]（见 ring_loops）。返回 ({档: 上沿 z}, [贡献环（点…, 骨）…])。"""
    cands, cov = [], set()
    for vs, bone in loops:
        if _ring_reject(vs, r_neck, z_hit, r_big, z_low):
            continue
        cands.append((vs, bone))
        cov |= set(bucket(p.x, p.y) for p in vs)          # 绕没绕满一圈：看整条环（不限近轴带）
    if len(cov) < cover:               # 拼起来也绕不满一圈 → 领口判不出来（调用方按"无数据"处理）
        return {}, cands
    collar = {}
    for vs, _b in cands:
        for p in vs:
            if math.hypot(p.x, p.y) > r_neck or p.z < z_hit - z_band:
                continue
            k = bucket(p.x, p.y)
            collar[k] = p.z if k not in collar else max(collar[k], p.z)
    return collar, [(vs, b) for vs, b in cands
                    if any(math.hypot(p.x, p.y) <= r_neck and p.z >= z_hit - z_band for p in vs)]


def _ring_reject(vs, r_neck, z_hit, r_big, z_low):
    """环没被采纳的原因（'' = 采纳）。逐条与 collar_ring 的判据①②一一对应。"""
    rr = [math.hypot(p.x, p.y) for p in vs]
    if min(p.z for p in vs) < z_low:
        return "太低(裙摆/腰线)"
    if max(rr) > r_big:
        return "太大(裙摆/袖口)"
    if min(rr) > r_neck:
        return "不沾近轴带"
    if max(p.z for p in vs) < z_hit - Z_LOW_ALLOW:
        return "够不到头侧下沿"
    return ""


def by_bucket_min(pts):
    """逐档最低点（脖子上断口）：脖子是筒、网格可能比 12 档疏，某档没顶点时借最近的档
    （±2 档 = ±60°），借过的档号记下来。返回 ({档: z}, [借过的档])。"""
    d = {}
    for p in pts:
        k = bucket(p.x, p.y)
        d[k] = p.z if k not in d else min(d[k], p.z)
    return _fill_nearest(d)


def _fill_nearest(d):
    filled = {}
    for k in range(NB):
        if k in d:
            filled[k] = d[k]
            continue
        for off in (1, -1, 2, -2):
            kk = (k + off) % NB
            if kk in d:
                filled[k] = d[kk]
                break
    return filled, sorted(set(filled) - set(d))


def per_angle(pts, pick, r_max):
    d = {}
    for q in pts:
        if math.hypot(q.x, q.y) > r_max:
            continue
        k = bucket(q.x, q.y)
        d[k] = pick(d[k], q.z) if k in d else q.z
    return d


def longest_open_arc(overlap, nb=NB):
    """数**最长的一条连续"不重叠"弧**（判据① 的硬指标）。

    overlap = 逐档「这一档算重叠吗」（True = 头侧下沿没高过领口上沿 = 盖住了）。
    返回 (弧长档数, 弧起始档)；全档都重叠 → (0, None)。
    环是圆的：从**第一条重叠档之后**起数、绕一整圈 —— 这样每条弧两头都落在重叠档上，
    不会被起点切断（不这么做，跨 0 档的那条弧会被少算）。
    缺数据的档由调用方填成"不重叠"（保守：量不出来当有缝；两侧全没数据另有总闸判不过）。
    """
    n = len(overlap)
    if not any(overlap):
        return n, 0                        # 一圈全是缝：弧 = 整圈
    i0 = overlap.index(True)
    best = cur = 0
    best_start = start = None
    for j in range(1, n + 1):
        i = (i0 + j) % n
        if not overlap[i]:
            if cur == 0:
                start = i
            cur += 1
            if cur > best:
                best, best_start = cur, start
        else:
            cur = 0
    return best, best_start


NN_MIN = 8          # float_check 的「近邻抽查」条数

# ---------------------------------------------------------------- 成品对成品（2026-09-17 加）
# 🔴 为什么要有这一段：闸门原来**只量源件**（源件按 T 拼），而实机露馅的是**成品**。
#   成品与源件之间还隔着整条管线（剔件 / 刚性归属 / 去重复 / 翻面绕序 / 补薄片背面…），
#   源件那边"对得上"不代表成品那边对得上。
#   实测（宁宁，2026-09-17）：源件口径 12/12 档"重叠 5~6cm"（闸门 PASS），实机却是
#   **领口上沿与下巴之间一圈看得见的缝**——根因是成品头颈部有 8 个**法线朝里**的面：
#   骑砍材质单面 + 背面剔除 → 朝里的面**看不见** → 直接透出背景（不是缺面，自由边一条都没有）。
#   源件那边完全没有"面朝向"这个概念，所以这条判据只能落在成品上。
# ⇒ 给了 `--built-*` 时：**接口径换成成品**（甲领口用成品甲量、头侧下沿用成品头量），
#   并新增判据⑤「领口上方不许有朝内面」。位置/半径两个常量写在 ⑤ 的实现里。
INWARD_Z = 0.10      # ⑤ 领口上沿往上多高算"露在外面那一带"（米）—— 再高就是下巴/头发
INWARD_R = 0.12      # ⑤ 只看近轴带内（= 脖子自身的粗细；再宽就把头发/脸颊算进来）
LOD_TAG = ".lod"


def built_main_meshes(path, z_game=5.0):
    """读成品 FBX → 主网格：去 `.lod*`，并排除**源空间残留**。

    🔴 残留必须排：成品 FBX 里还挂着**没被转换的源子网格**（实测宁宁头 FBX：5 件
    `model_0_submesh_*`，z 0~160 **厘米**，材质 `mat_L47_nene`）。它们的顶点会以"游戏空间里的
    近距离"混进按 z 取的带里（比如 z 1.5 附近），把判据带偏。判据与 `build_armor.py` 的同名规则
    一致：整件最高点 > 5 米（游戏空间最高件是兜 ≈2.3 米）= 残留。
    """
    out = []
    for o in load_fbx(path):
        if LOD_TAG in o.name or not len(o.data.vertices):
            continue
        if max((o.matrix_world @ v.co).z for v in o.data.vertices) > z_game:
            continue
        out.append(o)
    return out


def built_head_probe(path):
    """成品头：→ (逐档下沿 dict, 借用档, 取法名, 朝内面清单, 该带面总数)。

    下沿口径：有 `_neck` 件（材质名以 `_neck` 结尾）→ **只用脖子件**；没有 → 全部头件。
    ⑤ 面朝向：只数**脸壳件**（排除材质名 `_mouth` / `_eye` 的件）—— 嘴腔与眼球内侧朝里是正常的；
    而且**只有"从外面看得见"的朝里面才算缺陷**：朝里的面被背面剔除 → 那个位置透出背景。
    可见性判据 = 从面心沿**径向朝外**打一条射线，30cm 内没打到任何头件几何 → 没东西挡着 → 看得见。
    （不查可见性会把"包在头发/头巾里侧"的面也算进来 —— 实测谦信 18 个面里 16 个朝内，
      那是他头巾的内壁，实机根本看不到。）
    """
    ms = built_main_meshes(path)
    neck = [o for o in ms if (o.data.materials and o.data.materials[0]
                              and o.data.materials[0].name.endswith("_neck"))]
    use = neck or ms
    pts = [o.matrix_world @ v.co for o in use for v in o.data.vertices]
    bot, borrowed = by_bucket_min(pts)
    face_like = [o for o in use if not (o.data.materials and o.data.materials[0]
                                        and o.data.materials[0].name.endswith(("_mouth", "_eye")))]
    # 遮挡体 = 头 FBX 的**全部件**（含嘴/眼/发；只看脸壳会漏掉"被头发挡住"的情形）
    verts, faces = [], []
    for ob in ms:
        mw = ob.matrix_world
        b = len(verts)
        verts += [mw @ v.co for v in ob.data.vertices]
        faces += [[b + i for i in p.vertices] for p in ob.data.polygons]
    bvh = BVHTree.FromPolygons(verts, faces, all_triangles=False, epsilon=0.0) if faces else None
    inward, n_all = [], 0
    for ob in face_like:
        mw3 = ob.matrix_world.to_3x3()
        for poly in ob.data.polygons:
            c = ob.matrix_world @ poly.center
            r = math.hypot(c.x, c.y)
            if r > INWARD_R or r < 1e-6:
                continue
            n_all += 1
            nrm = mw3 @ poly.normal
            if (nrm.x * c.x + nrm.y * c.y) >= 0:
                continue
            d = Vector((c.x, c.y, 0.0)).normalized()
            if bvh is not None and bvh.ray_cast(c + d * 3e-4, d, 0.30)[0] is not None:
                continue                      # 外面还有几何挡着（发/头巾内壁…）→ 看不见，不算
            inward.append(dict(bucket=bucket(c.x, c.y), r=r, z=c.z))
    return bot, borrowed, ("脖子件" if neck else "全部头件"), inward, n_all


def built_collar_loops(path):
    """成品甲的开口环（**缓存**：环里只有纯 Vector，之后网格被清场也不影响）。"""
    ms = built_main_meshes(path)
    return ring_loops(ms, list(range(len(ms))), "armor", lambda p: p)


def auto_built(key):
    """按角色 key 推**成品**路径（存在就用）：头 = `Debug/offline/sw2_build/<key>/<asset>_v1.fbx`，
    甲 = `tools/armor-pipeline/out/taikou_<slug>_do_a.fbx`。

    🔴 为什么要有（`--built-auto`）：口径要"以成品为准"就必须**逐角色**把成品找出来，
    人肉传两条路径在一次跑一个人的时候还行，`--all` 就没法用了 —— 而"进批次前以成品为准"
    正是这条尺子的用途。有文件就用、没有就退回源件口径（打仗时不会因为没产物而全红）。
    """
    r = TABLE.get(key)
    if not r:
        return None, None
    hd = os.path.join(REPO, "Debug", "offline", "sw2_build", key, r["asset"] + "_v1.fbx")
    slug = r["asset"][len("head_"):-len("_a")]
    ar = os.path.join(REPO, "tools", "armor-pipeline", "out", "taikou_%s_do_a.fbx" % slug)
    return (hd if os.path.isfile(hd) else None), (ar if os.path.isfile(ar) else None)


def float_check(meshes, idxs, T, tol):
    """🔴 判据④ 孤立浮片：把「头 / 甲 / 兜」的件按**连通域**切开，逐片量
    「到其它任何一片的最小距离」，> tol 的算「与本体脱离、落在别处」的碎片。

    为什么要有这条：战无2 源模型的老毛病 —— 头发里混着甲片、脸件里混着兜，切出来之后
    多出一块飘在旁边的几何（實测慶次：头旁边一堆虎纹甲片）。
    几何上"接上了"= 与别的片有 ≤ tol 的近距离（共用面/顶点/边当然算），所以上面那条就是判据。

    算法（先抽样、再兜底，别改成 N² 全量 —— 28 人跑一遍会很慢）：
      · 每个点查 8 近邻：**有外片点、且在 tol 之内** → 这片跟别的东西挨着 → 整片跳过
      · 8 近邻**全是自己片** → 第 8 近邻的距离是「到别片」的**下界**（更近的别片点不存在，
        否则它就该出现在 8 近邻里）→ 全片下界都 > tol ⇒ 确实是浮片
      · 其余情况（8 近邻里有外片点但都超阈值 / 点数太少 8 近邻装得下别的片…）→ **判不了**，
        对该片做**精确球查询**（find_range(tol)）兜底
      ⚠️ 判据必须是「**有外片点在 tol 内**」而不是「8 近邻里有外片」——后者在点云稀疏时
        （比如一块 4 点的小碎片、旁边 1 米外才有别的片）会把远处的东西当成"挨着"，
        于是**一块真浮片都报不出来**（2026-09-17 合成用例实锤：`Debug/offline/_float_test.py`）。
    返回 [dict(obj, bone, n, d, c) …]（d = 到最近别片的距离，米；c = 片重心，米）。
    """
    pts, cid, meta = [], [], []
    for i in idxs:
        if not (0 <= i < len(meshes)):
            continue
        ob = meshes[i]
        mw = ob.matrix_world
        for vs, bone in frags(ob):
            c = len(meta)
            vl = []
            for vi in vs:
                vl.append(len(pts))
                pts.append(T(mw @ ob.data.vertices[vi].co))
                cid.append(c)
            meta.append(dict(obj=ob.name, bone=bone, n=len(vs), idx=vl))
    if not pts or len(meta) < 2:
        return []
    import mathutils.kdtree as kd
    kt = kd.KDTree(len(pts))
    for i, p in enumerate(pts):
        kt.insert(p, i)
    kt.balance()

    out = []
    for c, m in enumerate(meta):
        touched = False
        bound = float("inf")                    # 「到别片」的下界（0 = 判不了）
        for i in m["idx"]:
            nb = kt.find_n(pts[i], min(NN_MIN, len(pts)))
            foreign = [d for _co, j, d in nb if cid[j] != c]
            if foreign and min(foreign) <= tol:          # 真挨着（tol 之内有别片点）
                touched = True
                break
            if foreign:
                bound = 0.0                              # 8 近邻里有外片但都太远 → 判不了
            elif nb:
                bound = min(bound, nb[-1][2])            # 全是自己片 → 第 8 近邻距离是下界
        if touched:
            continue
        if bound > tol:                         # 下界都超阈值 → 确认是浮片
            pass
        else:                                   # 判不了 → 精确球查询兜底
            hit = False
            for i in m["idx"]:
                if any(cid[j] != c for _co, j, _d in kt.find_range(pts[i], tol)):
                    hit = True
                    break
            if hit:
                continue
        # 到这儿 = 浮片。报一个真数值：逐点放大 k，直到 8/16/64/… 近邻里出现别片
        # （某点的最近外片一定出现在 k 近邻里 → 那个 min 就是该点的精确值；取各点最小）
        d = None
        for i in m["idx"]:
            for k in (NN_MIN, 16, 64, 256, 1024):
                if k > len(pts):
                    break
                ds = [dd for _co, j, dd in kt.find_n(pts[i], k) if cid[j] != c]
                if ds:
                    d = min(min(ds), d) if d is not None else min(ds)
                    break
        if d is None:                           # 整个点云里再没有别片（只剩这一片）
            d = bound
        n = float(m["n"])
        cen = tuple(round(sum(pts[i][q] for i in m["idx"]) / n, 3) for q in range(3))
        out.append(dict(obj=m["obj"], bone=m["bone"], n=m["n"], d=d, c=cen))
    out.sort(key=lambda x: -x["d"])
    return out


def check_one(a, key, srct, show_detail=True):
    """跑一个角色 → 结果 dict（明细打到 stdout）。"""
    _CACHE.clear()
    r = TABLE[key]
    cn = r.get("cn", "")
    target_h = a.target_h or (1.802 if r.get("gender") == "female" else 1.811)
    res = dict(key=key, cn=cn, s=None, s_from="", joint={}, drift=None,
               penetration=None, pass_=False, error=None)

    src = a.src or os.path.join(SRC_DIR, key + ".fbx")
    if not os.path.isfile(src):
        print("!! 源模型不存在：%s" % src)
        res["error"] = "源模型不存在"
        return res
    census = os.path.join(CENSUS, key + "_census.csv")
    if not os.path.isfile(census):
        print("!! 普查表不存在：%s（先跑 run_census.py）" % census)
        res["error"] = "普查表不存在"
        return res
    verdict = {int(x["idx"]): x["verdict"]
               for x in csv.DictReader(io.open(census, encoding="utf-8"))}
    if not verdict:
        print("!! 普查表是空的：%s" % census)
        res["error"] = "普查表空"
        return res
    armor_idx = sorted(i for i, v in verdict.items() if v.startswith(("甲件", "内衬着物")))
    # 🔴 2026-09-17 加：**普查判「待看（碎片分散）」的件也算甲侧**。普查表里那一档的本意是
    #    "机器没定论"，不是"不是甲"—— 实测武藏：他领口环缺的那半圈就在 idx7（普查判「待看」，
    #    里面是**背心 + 头顶乱发**的复合件）里，只按「甲件」取 → 环只绕 7/12 档、领口判不出来
    #    （闸门直接报"判不出 = 不过"）；把待看件并进来 → 12/12 档。这与脖子那侧同口径
    #    （`neck_src_idx` 本来就包含待看件），不是放宽阈值、是**把"没定论"当"定论"的漏收补回来**。
    #    甲侧多收的件只影响"领口环从哪些件里长出来"与"甲顶点有没有插进头里"两项，两条都是
    #    按几何判的，多给件不会凭空造出环。
    armor_wide = sorted(set(armor_idx) | set(i for i, v in verdict.items() if v.startswith("待看")))
    weap_idx = [i for i, v in verdict.items() if v.startswith("武器")]
    head_idx = list(r.get("face") or []) + list(r.get("hair") or []) + list(r.get("eye") or [])
    helm_idx = list(r.get("helmet") or [])
    neck_idx = neck_src_idx(key)
    # 抠脖子的**肤色判据**要读源图集（参照色取脸壳下巴一带）——与管线同一取法（见 build_heads.py）：
    # 优先贴图升级产物 <key>_d.png，退回原图集 <key>.png；都没有 = 肤色判据关闭（会抠到甲领口金箍）。
    neck_atlas = a.neck_atlas
    if not neck_atlas:
        up = os.path.join(TEX_BATCH, key + "_d.png")
        neck_atlas = up if os.path.isfile(up) else os.path.join(SRC_TEX, key + ".png")
    if not os.path.isfile(neck_atlas):
        print("  ⚠️ 源图集不在（%s）→ 抠脖子的肤色判据关闭，可能把甲领口当成脖子" % neck_atlas)
        neck_atlas = None

    # ---- 成品侧（🔴 必须在导入源模型**之前**量：`load_fbx` 会 read_factory_settings 清场，
    #      之后那些 Blender 对象就全没了 —— 环/点都先抓成纯 Vector 再往下走）
    bl_head = bl_collar = None
    if a.built_head and os.path.isfile(a.built_head):
        bl_head = built_head_probe(a.built_head)
    if a.built_armor and os.path.isfile(a.built_armor):
        bl_collar = built_collar_loops(a.built_armor)

    meshes = load_fbx(src)

    # ---- s / z_sole：读 srcT.json（管线同一把尺）；没有才回退自算（口径不同 → 打警告）
    s = z_sole = None
    h_src = None
    if srct:
        try:
            row = src_transform.lookup(srct, key)
            s, z_sole, h_src = row["s"], row.get("z_sole", 0.0), row.get("h_bone")
            res["s_from"] = "srcT.json(锚 %s)" % row.get("anchor", "?")
        except KeyError:
            pass
    if s is None:
        allp = [meshes[i].matrix_world @ v.co for i, ob in enumerate(meshes)
                if i not in weap_idx and not verdict.get(i, "").startswith("驱动件")
                for v in ob.data.vertices]
        z_sole = min(p.z for p in allp)
        z_crown = max((meshes[i].matrix_world @ v.co).z for i in (head_idx + helm_idx)
                      if 0 <= i < len(meshes) for v in meshes[i].data.vertices)
        s = target_h / max(z_crown - z_sole, 1e-6)
        res["s_from"] = "自算(含发总高，兜底)"
        print("  ⚠️ srcT.json 里没有 %s → 回退自算口径（锚含发/兜总高）—— 与管线口径（锚头骨）"
              "实测差 3~37%%，结果仅供对照" % key)
    res["s"] = s

    def T(p):
        return Vector((p.x * s, p.y * -s, (p.z - z_sole) * s))

    # ---- 头侧（判据①的下沿来源）：脖子 = build_head.carve_neck_part 抠出来的那一截
    #   🔴 **不是每个人都有脖子件**（2026-09-16 实测宁宁：她的脸壳本身就盖住整个可见颈部，
    #      几何判据唯一能选中的"像脖子"的东西是甲自己的领口金箍 —— 已被 carve 的肤色判据拒掉）。
    #      所以下沿取法：**有脖子件 → 脖子件的最低点；没有 → 回落脸壳件的最低点**，
    #      输出里必须标明用的是哪一个（这直接决定"这个人到底有没有缝"）。没有脖子件 ≠ 不过。
    neck_objs = [meshes[i] for i in neck_idx if 0 <= i < len(meshes)]
    used_idx = [i for i in neck_idx if 0 <= i < len(meshes)]
    neck_pts = []
    taken = set()          # 「脖子归头」被 carve 拿走的顶点 (件号, 顶点号) —— 甲侧要从环集合里摘掉它们
    nargs = neck_args(key)     # 🔴 逐人覆写与管线同源：parts_table.neck_args（默认见那里）
    if neck_objs:
        ret = carve_neck_part(neck_objs, s, z_sole, tag="neck_src", atlas=neck_atlas,
                              # 🔴 face 组必须与管线（build_heads.py --pick-idx）**同一份**：
                              #    管线那份含头发（`hard` 角色除外），原来这里只取 face →
                              #    参照色不同 → 抠出的脖子件名单不同（实测兰丸：管线有、闸门没）。
                              #    拼法只在 parts_table.face_idx() 里写一份，两边都调它。
                              face_objs=[meshes[i] for i in face_idx(key) if 0 <= i < len(meshes)],
                              **nargs)
        neck_ob, moved = ret if isinstance(ret, tuple) else (ret, [])     # 现签名固定回二元组
        if neck_ob is not None:
            mw = neck_ob.matrix_world
            neck_pts = [T(mw @ v.co) for v in neck_ob.data.vertices]
        oid = {id(o): i for o, i in zip(neck_objs, used_idx)}
        for it in (moved or []):
            if isinstance(it, tuple) and len(it) == 2:
                i = oid.get(id(it[0]))
                if i is not None:
                    taken.update((i, vi) for vi in it[1])
    else:
        print("  ⚠️ 普查表里没有「甲件 / 内衬着物」→ 没得抠脖子")
    head_pts = side_pts(meshes, head_idx, "head", T, free_only=True)      # 头壳开口沿（旧口径，仅诊断）
    helm_pts = side_pts(meshes, helm_idx, "head", T, free_only=True) if helm_idx else []
    head_all = side_pts(meshes, head_idx, "head", T) + \
        (side_pts(meshes, helm_idx, "head", T) if helm_idx else [])
    if neck_pts:
        hbot, borrowed = by_bucket_min(neck_pts)
        head_bottom_from = "脖子件"
    else:
        # 没脖子件 → 回落「头侧几何的最低点」= 脸壳 + 头发 + 眼（+兜）**合起来**的最低点。
        #   🔴 只取脸壳件会出假数：实测本多忠胜脸壳在背后那几档一块顶点都没有（后脑是发/兜），
        #      单取脸壳 → 240° 得 1.6671 → 假 gap +157.8mm。
        hbot, borrowed = by_bucket_min(head_all)
        head_bottom_from = "脸壳/头件（没抠到脖子件，回落）"
    armor_free = side_pts(meshes, armor_idx, "armor", T, free_only=True, skip=taken)
    armor_all = side_pts(meshes, armor_idx, "armor", T, skip=taken)

    z_hit = min((p.z for p in neck_pts), default=None)
    if z_hit is None:                       # 没脖子件 → 领口环的"够高"门槛用头侧下沿（脸壳底）
        z_hit = min(hbot.values()) if hbot else 1.40
    # 🔴 领口环只认「待看也算甲」的那份（见 armor_wide 的说明）；其余判据（穿模/浮片/走样）
    #    仍走原来的拼法，避免这一处改动把别的判据一起带偏。
    all_loops = ring_loops(meshes, armor_wide, "armor", T, skip=taken)
    collar_raw, rings = collar_ring(all_loops, a.r_neck, z_hit)
    collar, collar_borrowed = _fill_nearest(collar_raw)
    res["head_bottom_from"] = head_bottom_from

    # ---- 换口径：给了成品 → **头侧下沿 / 甲领口上沿改用成品量**（成品对成品，见文件头那段）
    res["built"] = dict(head=a.built_head, armor=a.built_armor, used_head=False, used_armor=False,
                        inward=None)
    if bl_head or bl_collar:
        if bl_head:
            hbot, borrowed = bl_head[0], bl_head[1]
            head_bottom_from = "成品头（%s）" % bl_head[2]
            res["head_bottom_from"] = head_bottom_from
            res["built"]["used_head"] = True
        z_hit_b = min(bl_head[0].values()) if bl_head else z_hit
        if bl_collar:
            c_raw, r_rings = collar_ring(bl_collar, a.r_neck, z_hit_b)
            c_b, c_bor = _fill_nearest(c_raw)
            if c_b:
                collar, collar_borrowed = c_b, c_bor
                res["built"]["used_armor"] = True
                res["built"]["rings"] = r_rings
            else:
                print("  ⚠️ 成品甲的领口环判不出来（%d 条环绕不满一圈）→ 领口仍用源件口径"
                      % len(bl_collar))
        print("  ⚠️ 口径 = **成品对成品**（%s%s）—— 头侧下沿 %s、甲领口上沿 %s"
              % ("成品头" if res["built"]["used_head"] else "",
                 "＋成品甲" if res["built"]["used_armor"] else "",
                 "成品" if res["built"]["used_head"] else "源件",
                 "成品" if res["built"]["used_armor"] else "源件"))

    if show_detail:
        print("=" * 88)
        print("拼装闸门 %s（%s）  s=%.6f  尺=%s  H_src(头骨)=%s  H_target=%.3f"
              % (key, cn, s, res["s_from"], ("%.1f" % h_src) if h_src else "-", target_h))
        print("  头侧下沿取法：%s" % head_bottom_from)
        print("  脖子：抠出 %d 顶点（源件 %s，阈值 %.2f/%.2f/%.2f/%d/底切 %.2f/肤色容差 %.2f%s）；"
              "甲侧已摘掉这 %d 个顶点（脖子归头）  领口环：%d 条绕轴环"
              % (len(neck_pts), "+".join(str(i) for i in used_idx) or "无",
                 nargs["r_max"], nargs["y_max"], nargs["z_top"],
                 nargs["n_sect"], nargs["z_cut"], nargs["skin_tol"],
                 "" if neck_atlas else "，⚠️无图集→肤色判据关", len(taken), len(rings)))
        if a.ring_verbose:
            print("  甲侧自由边环共 %d 条（采纳 %d 条）—— 打 ★ 的进领口上沿：" % (len(all_loops), len(rings)))
            for vs, bone in sorted(all_loops, key=lambda x: -len(x[0])):
                rr = [math.hypot(p.x, p.y) for p in vs]
                zs = [p.z for p in vs]
                why = _ring_reject(vs, a.r_neck, z_hit, 0.60, 0.9)
                print("    %s n=%-4d r %.3f..%.3f  z %.3f..%.3f  覆盖 %d/12  %-14s 骨 %s"
                      % ("★" if not why else " ", len(vs), min(rr), max(rr), min(zs), max(zs),
                         len(set(bucket(p.x, p.y) for p in vs)), why or "采纳", bone))
        if borrowed or collar_borrowed:
            print("  （%s%s%s 借了邻档）"
                  % (("头侧" + ", ".join("%d°" % (k * 30) for k in borrowed)) if borrowed else "",
                     "；" if borrowed and collar_borrowed else "",
                     ("领口" + ", ".join("%d°" % (k * 30) for k in collar_borrowed)) if collar_borrowed else ""))

    # ---- 判据① 对接（🔴 2026-09-17 新口径「整体观感」：只看**最长连续不重叠弧**）
    #   逐档 gap = 头侧下沿 − 领口上沿；gap ≤ gap_tol（含共面/共点/共边）算**重叠**（允许），
    #   否则那一档是**露缝**。硬判 = 最长的一条连续露缝弧 ≤ --max-arc（默认 60°）。
    #   老的「逐档必须 ≥5mm 余量」降级为诊断（仍印出来，不参与总判）。
    ok = True
    joint = {}
    arc = None
    if not hbot or not collar:
        ok = False
        # "判不出来"算**不过**（不是输入错）：闸门的职责就是"没过闸不许进批次"，
        # 判据① 没有数据 ≠ 通过。单跑时退出码仍是 1（2 只留给源模型/普查表这类输入错）。
        res["nodata"] = ("头侧下沿没有数据" if not hbot else
                         "领口环拼不满一圈（绕不到 %d/12 档）" % 10)
        print("   !! %s → 判不出来，不算过" % res["nodata"])
    else:
        if show_detail:
            print("-" * 88)
            print("判据① 对接（整体观感口径：最长连续不重叠弧 ≤ %.0f°；逐档 5mm 余量只作诊断）"
                  % a.max_arc)
            print("   档        头侧下沿(%s)   领口上沿    gap        5mm档(仅诊断)"
                  % ("脖子件" if neck_pts else "脸壳/头件"))
        for k in range(NB):
            hb, cl = hbot.get(k), collar.get(k)
            if hb is None or cl is None:                 # 这一档两侧没数据（罕见）→ 当"没盖上"算
                joint[k] = dict(head_bottom=hb, collar_top=cl, gap=None,
                                overlap=False, missing=True, pass_=False)
                if show_detail:
                    print("   %-7s （缺数据）" % ANGLES.get(k, "%d°" % (k * 30)))
                continue
            gap = hb - cl
            over = gap <= a.gap_tol
            joint[k] = dict(head_bottom=hb, collar_top=cl, gap=gap, overlap=over, missing=False,
                            pass_=(gap <= -a.margin))    # ← 老 5mm 口径，仅诊断
            if show_detail:
                print("   %-7s %.4f          %.4f    %+8.1f mm  %s   %s"
                      % (ANGLES.get(k, "%d°" % (k * 30)), hb, cl, gap * 1000,
                         "重叠" if over else "露缝", "PASS" if joint[k]["pass_"] else "fail"))
        n_open, st = longest_open_arc([joint[k]["overlap"] for k in range(NB)])
        deg = n_open * (360.0 / NB)
        arc_ok = deg <= a.max_arc
        ok = ok and arc_ok
        if n_open:
            a1, a2 = st * 360.0 / NB, ((st + n_open - 1) % NB) * 360.0 / NB
            span = "%.0f°~%.0f°" % (a1, a2)
        else:
            span = "—"
        arc = dict(deg=deg, n=n_open, span=span, max_deg=a.max_arc, pass_=arc_ok,
                   buckets=[k for k in range(NB) if not joint[k]["overlap"]])
        res["arc"] = arc
        if show_detail:
            print("   → 最长连续不重叠弧 = %.0f°（%s，共 %d 档）  头侧下沿取法：%s   %s"
                  % (deg, span, n_open, head_bottom_from.replace("（没抠到脖子件，回落）", ""),
                     "PASS" if arc_ok else "FAIL（上限 %.0f°）" % a.max_arc))
            if set(hbot) & set(collar) == set():
                print("   （一档都没对上：头侧 %d 档 / 领口 %d 档）" % (len(hbot), len(collar)))
        # ---- 判据①' 单档最大 gap（与弧判据**独立**生效）：允许"一档大缝"是弧口径的性质，
        #   但那一档仍然是肉眼可见的洞（实测秀吉 30° 档 +49mm 过了弧判据）。任何一档超上限即不过。
        _gs = [(k, joint[k]["gap"]) for k in range(NB) if joint[k]["gap"] is not None]
        wk, wg = (max(_gs, key=lambda t: t[1]) if _gs else (None, None))
        gap_ok = (wg is None) or (wg <= a.gap_max)
        ok = ok and gap_ok
        res["gap_max"] = dict(tol=a.gap_max, worst_bucket=wk,
                              worst_angle=(ANGLES.get(wk, "%d°" % (wk * 30)) if wk is not None else None),
                              worst_gap=wg, n=len(_gs), pass_=gap_ok)
        if show_detail:
            print("   → 单档最大 gap = %s（@%s，共 %d 档有数据）  上限 %.0fmm   %s"
                  % (("%+.1fmm" % (wg * 1000)) if wg is not None else "无数据",
                     res["gap_max"]["worst_angle"] or "—", len(_gs), a.gap_max * 1000,
                     "PASS" if gap_ok else "FAIL"))
    res["joint"] = joint
    if joint:
        good = [k for k in joint if joint[k]["gap"] is not None]
        if good:
            wk = max(good, key=lambda k: joint[k]["gap"])
            res["worst"] = dict(bucket=wk, angle=ANGLES.get(wk, "%d°" % (wk * 30)),
                                gap=joint[wk]["gap"], collar_top=joint[wk]["collar_top"],
                                head_bottom=joint[wk]["head_bottom"],
                                head_bottom_from=head_bottom_from)
    # 旧口径对照（诊断用，不参与总判）：头壳开口沿下沿 vs 所有近轴自由边的最高点
    old_bot = per_angle(head_pts + helm_pts, min, a.r_neck)
    old_top = per_angle(armor_free, max, a.r_neck)
    okeys = sorted(set(old_bot) & set(old_top))
    if okeys:
        ow = max(okeys, key=lambda k: old_bot[k] - old_top[k])
        res["old"] = dict(bucket=ow, angle=ANGLES.get(ow, "%d°" % (ow * 30)),
                          gap=old_bot[ow] - old_top[ow], head_shell_bottom=old_bot[ow],
                          collar_all_free=old_top[ow])
        if show_detail:
            print("   （旧口径对照：头壳下沿 %.4f vs 全部近轴自由边 %.4f → gap %+.1fmm @%s；"
                  "口径不同，仅作对照）" % (old_bot[ow], old_top[ow], (old_bot[ow] - old_top[ow]) * 1000,
                                          res["old"]["angle"]))

    # ---- 判据② 重合：成品接缝带 vs T(源件)
    drift = None
    if a.built_head or a.built_armor:
        if show_detail:
            print("-" * 88)
            print("判据② 重合（成品接缝带顶点 → T(源件) 最近距离 < %.0fmm）" % (a.tol * 1000))
        src_pts = head_all + neck_pts + armor_all
        import mathutils.kdtree as kd
        kt = kd.KDTree(len(src_pts))
        for i, p in enumerate(src_pts):
            kt.insert(p, i)
        kt.balance()
        drift = {}
        for tag, path in (("head", a.built_head), ("armor", a.built_armor)):
            if not path:
                continue
            load_fbx(path)
            # 🔴 同 built_main_meshes：源空间残留（整件最高点 > 5 米）必须排 —— 它 z 一路铺到
            #    1.3~1.8 带里，会以"游戏空间的近距离"混进接缝带（实测宁宁头 FBX 有 5 件）。
            seam = []
            for ob in [o for o in bpy.data.objects if o.type == 'MESH' and len(o.data.vertices)
                       and max((o.matrix_world @ v.co).z for v in o.data.vertices) <= 5.0]:
                for p in (ob.matrix_world @ v.co for v in ob.data.vertices):
                    if math.hypot(p.x, p.y) <= a.r_neck + 0.05 and 1.30 <= p.z <= 1.80:
                        seam.append(p)
            if not seam:
                print("   %-6s （接缝带没取到顶点，跳过）" % tag); continue
            ds = sorted(kt.find(p)[2] for p in seam)
            p95 = ds[int(len(ds) * 0.95)]
            good = max(ds) < a.tol
            # 🔴 **降级为诊断**（不参与总判，2026-09-17）：这条量的是"成品 vs 纯 T 的源件"，
            #    而 T 模式的成品里**甲的手臂件本来就带了 A-pose 姿态修正**（源是 T-pose 直臂，
            #    骑砍是 A-pose 斜臂，实测差 4~6cm，肩膀一带更多）—— 那个差是**设计如此**，不是走样。
            #    全量实测（28 人 --built-auto）：armor 走样 max 54~140mm **人人超**，head 也有 4 人超
            #    （脖子件/兜并入头之后，头那侧也带了姿态差）。⇒ 要当硬判必须换参照（T + 手臂链的
            #    "理想成品"），否则它只会把所有人判红。数字仍然印出来：非手臂件应当 ~0。
            drift[tag] = dict(n=len(ds), max=max(ds), p95=p95, pass_=good, hard=False)
            print("   %-6s n=%-5d 偏差 p95=%.2fmm  max=%.2fmm  %s（诊断，不参与总判：成品含 A-pose"
                  " 手臂修正，与纯 T 的源件本来就差 4~6cm）"
                  % (tag, len(ds), p95 * 1000, max(ds) * 1000, "OK" if good else "偏差大"))
    res["drift"] = drift

    # ---- 判据③ 不穿模（领口上沿 + 5mm 之上，头/兜/脖子 与 甲 不得互相插入）
    # 🔴 判据② 那一步把成品 FBX 导了进来，而 `load_fbx` = `read_factory_settings` **清场** ——
    #    源对象在 ② 之后已经不存在了，③④ 还要用它们（实测：加了 --built-* 之后 ③ 直接
    #    `ReferenceError: StructRNA of type Object has been removed`，这条一直没被发现，
    #    因为 ② 从来没有被真正跑过）。这里把源模型重新导一遍：同一个文件 → 顶点索引一致，
    #    按名字缓存的 frags/free_edges 照样命中。
    if a.built_head or a.built_armor:
        meshes = load_fbx(src)
    if show_detail:
        print("-" * 88)
        print("判据③ 不穿模（领口上沿 + 5mm 之上，头/兜/脖子 与 甲 不得互相插入）")
    pen = 0
    cvals = list(collar.values())
    c_def = (sum(cvals) / len(cvals)) if cvals else 1.60      # 该档缺数据时用全档均值兜底
    for tag, subject_pts, target_idxs in (
            ("头/兜/脖子 在甲内", head_all + helm_pts + neck_pts, armor_idx),
            ("甲 在头内", armor_all, head_idx + helm_idx)):
        if not subject_pts or not target_idxs:
            continue
        verts, faces = [], []
        for i in target_idxs:
            if not (0 <= i < len(meshes)):
                continue
            ob = meshes[i]
            mw = ob.matrix_world
            base = len(verts)
            verts += [mw @ v.co for v in ob.data.vertices]
            faces += [[base + vi for vi in p.vertices] for p in ob.data.polygons]
        if not faces:
            continue
        bvh = BVHTree.FromPolygons(verts, faces, all_triangles=False, epsilon=0.0)
        n = 0
        ntest = 0
        for q in subject_pts:
            k = bucket(q.x, q.y)
            zmin = collar.get(k, c_def) + 0.005
            if q.z <= zmin or math.hypot(q.x, q.y) > a.r_neck + 0.05:
                continue
            ntest += 1
            # 从点向上打一条长射线，穿过面数为奇数 = 在内部
            hit = 0
            o = Vector((q.x, q.y, q.z))
            d = Vector((0.0, 0.0, 1.0))
            while True:
                res_ = bvh.ray_cast(o, d, 3.0)
                if res_[0] is None:
                    break
                hit += 1
                o = res_[0] + d * 1e-4
            if hit % 2 == 1:
                n += 1
        if show_detail:
            print("   %-16s 受检顶点 %-6d 插入顶点 %d 个  %s"
                  % (tag, ntest, n, "PASS" if n == 0 else "FAIL"))
        ok = ok and (n == 0)
        pen += n
    res["penetration"] = pen

    # ---- 判据④ 无孤立浮片（头 / 甲 / 兜 里不许有"跟本体脱开、落在别处"的碎片）
    if show_detail:
        print("-" * 88)
        print("判据④ 无孤立浮片（片到其它任何片的最小距离 > %.0fmm 才算浮片）" % (a.float_tol * 1000))
    floats = float_check(meshes, head_idx + helm_idx + armor_idx, T, a.float_tol)
    ok = ok and not floats
    res["float"] = dict(tol=a.float_tol, n=len(floats), pass_=not floats, items=floats)
    if show_detail:
        if floats:
            for f in floats:
                print("   ✗ %s 骨 %-8s n=%-4d 离最近片 %.0fmm  重心 %s"
                      % (f["obj"].split("_")[-1][:14], f["bone"], f["n"], f["d"] * 1000, f["c"]))
        print("   %s（浮片 %d 片）" % ("PASS" if not floats else "FAIL", len(floats)))

    # ---- 判据⑤ 领口上方无朝内面（只对**成品头**生效；见文件头「成品对成品」那段）
    # 骑砍材质单面 + 背面剔除 → 朝里的面在实机里**看不见**，等于透出背景的一个"缝"。
    # 实测宁宁：成品头颈部 8 个朝里面的中心在 z 1.531~1.551、r 0.047~0.096 —— 正好落在
    # 甲领口上沿（1.505~1.538）的**上方** → 实机就是"领口与下巴之间那一圈看得见的缝"。
    if bl_head:
        c_lo = min(collar.values()) if collar else None
        z0 = (c_lo + 0.005) if c_lo is not None else 1.36
        z1 = z0 + INWARD_Z
        bad = [f for f in bl_head[3] if z0 <= f["z"] <= z1]
        res["built"]["inward"] = dict(band=[z0, z1], r_max=INWARD_R, n=len(bad),
                                      n_faces=bl_head[4], items=bad)
        good = not bad
        ok = ok and good
        if show_detail:
            print("-" * 88)
            print("判据⑤ 领口上方无朝内面（带 z %.3f~%.3f，r≤%.2f；成品头，单面材质下朝内=看不见）"
                  % (z0, z1, INWARD_R))
            for f in bad[:8]:
                print("   ✗ 朝内面 档%-4d z %.4f  r %.3f"
                      % (f["bucket"] * 360 // NB, f["z"], f["r"]))
            print("   %s（朝内面 %d 个 / 该带面 %d 个）"
                  % ("PASS" if good else "FAIL", len(bad), bl_head[4]))

    if show_detail:
        print("-" * 88)
        print("总判：%s" % ("PASS" if ok else "FAIL"))
    res["pass_"] = ok
    return res


def fmt_worst(res):
    w = res.get("worst")
    if not w:
        return "-"
    return "%s %+.1fmm" % (w["angle"], w["gap"] * 1000)


def fmt_arc(res):
    arc = res.get("arc")
    if not arc:
        return res.get("nodata") or res.get("error") or "无数据"
    return "%.0f°" % arc["deg"] if not arc["deg"] else "%.0f°(%s)" % (arc["deg"], arc["span"])


def fail_why(r):
    """一行说清这个人为什么不过（给 --all 的尾行用）。"""
    w = []
    if r.get("nodata"):
        w.append(r["nodata"])
    arc = r.get("arc")
    if arc and not arc["pass_"]:
        w.append("露缝弧 %.0f°>%.0f°（%s）" % (arc["deg"], arc["max_deg"], arc["span"]))
    gm = r.get("gap_max")
    if gm and not gm["pass_"]:
        w.append("单档 gap %+.0fmm>%.0fmm（@%s）"
                 % (gm["worst_gap"] * 1000, gm["tol"] * 1000, gm["worst_angle"]))
    if not (r.get("float") or {}).get("pass_", True):
        fl = r["float"]
        w.append("浮片 %d 片（最远 %.0fmm）"
                 % (fl["n"], max(f["d"] for f in fl["items"]) * 1000 if fl["items"] else 0))
    if r.get("penetration"):
        w.append("插模 %d 点" % r["penetration"])
    _bi = ((r.get("built") or {}).get("inward") or {})
    if _bi.get("n"):
        w.append("领口上方朝内面 %d 个（单面材质下看不见=缝）" % _bi["n"])
    dr = r.get("drift") or {}
    for tag, dv in dr.items():
        if not dv["pass_"] and dv.get("hard", True):
            w.append("%s 走样 max %.1fmm" % (tag, dv["max"] * 1000))
    if not w and r.get("error"):
        w.append(r["error"])
    return "; ".join(w) or "不过（原因未分类）"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--key", default=None, help="角色 key（parts_table 的键）")
    ap.add_argument("--all", action="store_true", help="全部角色挨个跑，打汇总表；任一不过 exit 1")
    ap.add_argument("--src", default=None)
    ap.add_argument("--target-h", type=float, default=None, help="仅兜底口径（srcT.json 缺该角色）时用")
    ap.add_argument("--margin", type=float, default=0.005,
                    help="逐档对接余量（米），默认 5mm —— 🔴 只作诊断：硬判看 --max-arc")
    ap.add_argument("--tol", type=float, default=0.005, help="重合判据上限（米），默认 5mm")
    ap.add_argument("--r-neck", type=float, default=0.12, help="近轴带半径（米），默认 0.12")
    ap.add_argument("--max-arc", type=float, default=ARC_MAX,
                    help="判据① 硬指标：最长连续不重叠弧的上限（度），默认 %.0f（0=不允许露缝）" % ARC_MAX)
    ap.add_argument("--gap-tol", type=float, default=GAP_TOL,
                    help="判据① gap ≤ 此值算「重叠」（米），默认 0（共面/共点/共边也算重叠）")
    ap.add_argument("--gap-max", type=float, default=GAP_MAX,
                    help="判据①' 任何**单档** gap 的上限（米），默认 %.3f；在弧判据之外独立生效"
                         % GAP_MAX)
    ap.add_argument("--float-tol", type=float, default=FLOAT_TOL,
                    help="判据④ 浮片阈值：片与其它片的最小距离超过它才算浮片（米），默认 %.2f" % FLOAT_TOL)
    ap.add_argument("--neck-atlas", default=None, help="抠脖子肤色判据读的源图集（默认自动找）")
    ap.add_argument("--built-head", default=None)
    ap.add_argument("--built-armor", default=None)
    ap.add_argument("--built-auto", action="store_true",
                    help="按角色自动找**成品**头/甲（口径换成成品对成品；找不到就退回源件口径）")
    ap.add_argument("--json", default=None, help="结构化结果；--all 时写成一张总表")
    ap.add_argument("--ring-verbose", action="store_true", help="把候选领口环逐条打出来（排查口径用）")
    a = ap.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])

    try:
        patch_importer()      # 自建头 FBX 带 59 条形变通道，不打这个补丁导入会断言崩
    except Exception as e:                                    # noqa
        print("  (fbx morph 补丁跳过: %s)" % e)

    srct = None
    if os.path.isfile(SRCT_JSON):
        try:
            srct = src_transform.load(SRCT_JSON)
        except Exception as e:                                # noqa
            print("!! srcT.json 读不了（%s）→ 全部回退自算口径" % e)
    else:
        print("!! 没有 %s → 全部回退自算口径（含发总高，与管线口径差 3~37%%）"
              % os.path.relpath(SRCT_JSON, REPO))

    if a.all:
        keys = list(TABLE.keys())
        results = []
        for key in keys:
            if a.built_auto:
                a.built_head, a.built_armor = auto_built(key)
            results.append(check_one(a, key, srct, show_detail=False))
            sys.stdout.flush()
        results.sort(key=lambda r: (r["pass_"], -(((r.get("arc") or {}).get("deg")) or -1),
                                    -((r.get("worst") or {}).get("gap") or -9)))
        print("=" * 88)
        print("拼装闸门 · 批量（%d 人）  尺 = %s  ① 露缝弧上限 %.0f° ①' 单档 gap 上限 %.0fmm "
              "④ 浮片阈值 %.0fmm"
              % (len(results), "out/srcT.json" if srct else "自算(兜底)", a.max_arc,
                 a.gap_max * 1000, a.float_tol * 1000))
        print("-" * 88)
        print("  %-22s %-8s %-14s %-15s %-7s %-6s %s"
              % ("角色", "头侧下沿", "判据①露缝弧", "最差单档 gap(硬判)", "判据③", "判据④", "总判"))
        for r in results:
            fc = r.get("float") or {}
            print("  %-22s %-8s %-14s %-15s %-7s %-6s %s"
                  % ("%s %s" % (r["key"], r["cn"]),
                     "脖子" if str(r.get("head_bottom_from", "")).startswith("脖子") else "脸壳",
                     fmt_arc(r), fmt_worst(r),
                     r["penetration"] if r["penetration"] is not None else "-",
                     fc.get("n", "-"), "PASS" if r["pass_"] else "FAIL"))
        bad = [r for r in results if not r["pass_"]]
        print("-" * 88)
        print("结果：%d 过 / %d 不过" % (len(results) - len(bad), len(bad)))
        for r in bad:
            print("   ✗ %-16s %s" % (r["key"] + " " + r["cn"], fail_why(r)))
        # 🔴 诊断（**不参与总判**）：整圈都过闸（含 ①' 的 30mm 上限）、但某**单档**缝 > 10mm 的人
        #    —— 还在"不会一眼看出洞"的范围里，但已经是需要盯着的擦边档。列出来让人自己决定
        #    要不要再修那个人；30mm 以上的已经由判据 ①' 挡在闸门外面了。
        wide = [r for r in results if r["pass_"] and (r.get("worst") or {}).get("gap", -9) > 0.010]
        if wide:
            print("（诊断，不参与总判；>%.0fmm 的已在判据①'退出）过闸但单档 gap > 10mm 的 %d 人：%s"
                  % (a.gap_max * 1000, len(wide),
                     "; ".join("%s %s(%+.0fmm)" % (r["key"], r["worst"]["angle"],
                                                   r["worst"]["gap"] * 1000)
                               for r in wide)))
        if a.json:
            with io.open(a.json, "w", encoding="utf-8") as fh:
                fh.write(json.dumps(dict(margin=a.margin, max_arc=a.max_arc, gap_tol=a.gap_tol,
                                         gap_max=a.gap_max, float_tol=a.float_tol, n=len(results),
                                         passed=len(results) - len(bad), failed=len(bad),
                                         chars={r["key"]: r for r in results}),
                                    ensure_ascii=False, indent=1))
            print("→ %s" % a.json)
        return 0 if not bad else 1

    if not a.key:
        print("!! 要么给 --key，要么给 --all")
        return 2
    if a.key not in TABLE:
        print("!! %s 不在 parts_table 里（可用键见 python parts_table.py）" % a.key)
        return 2
    if a.built_auto:
        a.built_head, a.built_armor = auto_built(a.key)
    res = check_one(a, a.key, srct)
    if a.json:
        with io.open(a.json, "w", encoding="utf-8") as fh:
            fh.write(json.dumps(res, ensure_ascii=False, indent=1))
        print("→ %s" % a.json)
    if res.get("error"):
        return 2
    return 0 if res["pass_"] else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except SystemExit:
        raise
    except Exception:                                     # noqa: BLE001
        import traceback
        traceback.print_exc()
        # 🔴 脚本自己崩了也必须非 0：blender 对脚本异常**默认仍 exit 0**，不拦一下的话
        #    体检表会把"闸门脚本坏了"显示成"数据全过"（2026-09-16 实测踩到）。
        sys.exit(1)
