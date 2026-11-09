#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 `.mdl` 库直接导出成**单档 TRF**（LOD0，不生成 LOD0-6 链）。

用途（2026-11-08 第一版）：
  · 交付包里先给出一版**没有 LOD 链**的模型，验证导入/材质/摆位链路；
  · 后续再改用「场景 blend 直出 + 7 档 LOD」的正式管线。

输入：
  · deliverable/manifests/asset_import_models.csv   （import=1 的 24,424 个模型）
  · assets/lib_native/index.json                    （模型表 + 贴图表）
  · assets/lib_native/<hh>/<hash>.mdl               （RMDL1 几何）

输出：
  · deliverable/models/<name>.trf
  · deliverable/manifests/models_export.csv         （name/hash/mdl/trf/faces/bytes）

口径：
  · 坐标 = .mdl 模型局部坐标（米，Z 上）；不应用场景实例变换；
  · UV 的 V 翻成 1-v（与 `blender_continent.load_model_mesh` 的 `UV_V_FLIP` 一致，
    否则所有文字/贴花上下颠倒）；
  · 材质名 = albedo 贴图名（与 `textures/alb/<名>.png` 一致）；无 albedo = `no_tex`；
  · 一个子网格一个 TRF mesh（名字 `<name>` 或 `<name>.<i>`）。

用法：
    python Scripts/export_rdr2_trf_mdl.py --limit 20      # 先抽 20 个验证
    python Scripts/export_rdr2_trf_mdl.py                  # 全量
"""
import argparse
import csv
import io
import json
import os
import struct
import sys

import numpy as np

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
LIB = os.path.join(RDR2, "assets", "lib_native")
MANIFESTS = os.path.join(RDR2, "deliverable", "manifests")
OUT_DIR = os.path.join(RDR2, "deliverable", "models")
OUT_EXTRA = os.path.join(RDR2, "deliverable", "models_extra")
IMPORT_CSV = os.path.join(MANIFESTS, "asset_import_models.csv")
INVENTORY_CSV = os.path.join(MANIFESTS, "asset_inventory_models.csv")
MAGIC = b"RMDL1" + bytes(3)
SUB = struct.Struct("<7I")


# --------------------------------------------------------------------- TRF
class TrfMesh(object):
    def __init__(self):
        self.mesh_name = ""
        self.mesh_materials = []
        self.vertex_count = 0
        self.vertex_fvf_count = 0
        self.morph_key_count = 0
        self.face_count = 0
        self.vertices = []
        self.vertex_fvfs = []
        self.faces = []


class TrfVertex(object):
    __slots__ = ("x", "y", "z")


class TrfVertexFvf(object):
    __slots__ = ("vertex_index", "vertex_color", "normal_x", "normal_y", "normal_z",
                 "uv_x", "uv_y", "uv2_x", "uv2_y")


class TrfFace(object):
    __slots__ = ("vertex_fvf_indices",)


def _f(num):
    return "%.6f" % float(num)


def export_trf_file(meshes, path):
    with io.open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("rfver 4\n")
        f.write("mesh %d\n" % len(meshes))
        for m in meshes:
            f.write((m.mesh_name + " 0 " + " ".join(m.mesh_materials)).rstrip() + "\n")
            f.write("%d\n" % m.vertex_count)
            for v in m.vertices:
                f.write("%s %s %s\n" % (_f(v.x), _f(v.y), _f(v.z)))
            f.write("0\n%d\n" % m.vertex_fvf_count)
            for v in m.vertex_fvfs:
                f.write("%d %d %s %s %s\n" % (v.vertex_index, v.vertex_color,
                                              _f(v.normal_x), _f(v.normal_y), _f(v.normal_z)))
                f.write("%s %s\n" % (_f(v.uv_x), _f(v.uv_y)))
                f.write("%s %s\n" % (_f(v.uv2_x), _f(v.uv2_y)))
            f.write("0\n")          # morph_key_count
            f.write("%d\n" % m.face_count)
            for fc in m.faces:
                f.write("%d %d %d\n" % tuple(fc.vertex_fvf_indices))


# --------------------------------------------------------------------- MDL
def read_mdl(path, tex_names):
    """返回 [(trf_mesh)]；一个子网格一个 TrfMesh。"""
    buf = open(path, "rb").read()
    if buf[:8] != MAGIC:
        raise ValueError("bad RMDL1: %s" % path)
    nsub = struct.unpack_from("<I", buf, 8)[0]
    base = 12 + 24
    out = []
    for si in range(nsub):
        tid, vc, ic, po, no, uo, io_ = SUB.unpack_from(buf, base + si * SUB.size)
        if vc <= 0 or ic <= 0:
            continue
        pos = np.frombuffer(buf, dtype="<f4", count=vc * 3, offset=po).reshape(-1, 3)
        if no and vc:
            nrm = np.frombuffer(buf, dtype="<f4", count=vc * 3, offset=no).reshape(-1, 3)
        else:
            nrm = np.zeros((vc, 3), dtype=np.float32)
        if uo and vc:
            uv = np.frombuffer(buf, dtype="<f4", count=vc * 2, offset=uo).reshape(-1, 2)
        else:
            uv = np.zeros((vc, 2), dtype=np.float32)
        idx = np.frombuffer(buf, dtype="<u4", count=ic, offset=io_ if io_ else 0)
        if idx.size != ic:
            continue
        mat = tex_names.get(tid, "no_tex") if tid not in (0xFFFFFFFF, None) else "no_tex"
        if not mat:
            mat = "no_tex"

        m = TrfMesh()
        m.mesh_name = "%s" % path
        m.mesh_materials = [mat]
        m.vertex_count = vc
        m.vertex_fvf_count = vc
        m.face_count = ic // 3
        # 顶点
        vs = []
        for i in range(vc):
            v = TrfVertex()
            v.x, v.y, v.z = float(pos[i, 0]), float(pos[i, 1]), float(pos[i, 2])
            vs.append(v)
        m.vertices = vs
        # FVF：一顶点一条；UV 的 V 翻转
        fs = []
        for i in range(vc):
            f = TrfVertexFvf()
            f.vertex_index = i
            f.vertex_color = 4294967295
            f.normal_x, f.normal_y, f.normal_z = (float(nrm[i, 0]), float(nrm[i, 1]), float(nrm[i, 2]))
            f.uv_x = float(uv[i, 0])
            f.uv_y = 1.0 - float(uv[i, 1])
            f.uv2_x = 0.0
            f.uv2_y = 0.0
            fs.append(f)
        m.vertex_fvfs = fs
        faces = []
        for t in range(0, ic, 3):
            fc = TrfFace()
            fc.vertex_fvf_indices = [int(idx[t]), int(idx[t + 1]), int(idx[t + 2])]
            faces.append(fc)
        m.faces = faces
        out.append(m)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--limit", type=int, default=0, help="只导前 N 个（0 = 全部）")
    ap.add_argument("--set", choices=["import", "extra"], default="import",
                    help="import = 导入子集（24,424）；extra = 场景用到但不在导入子集（15,566）")
    ap.add_argument("--out", default="")
    ap.add_argument("--min-faces", type=int, default=0)
    a = ap.parse_args()

    idx = json.load(io.open(os.path.join(LIB, "index.json"), encoding="utf-8"))
    tex = idx["tex"]
    tex_names = {i: (t.get("n") or "") for i, t in enumerate(tex)}

    if a.set == "extra":
        # 场景用到、但不在 16 类导入子集里（地形块/地下/水面/占位/地形 LOD）
        rows = [r for r in csv.DictReader(io.open(INVENTORY_CSV, encoding="utf-8-sig"))
                if r.get("in_scenes") == "1" and r.get("import") != "1"]
        out_dir = a.out or OUT_EXTRA
        manifest = "models_export_extra.csv"
    else:
        rows = [r for r in csv.DictReader(io.open(IMPORT_CSV, encoding="utf-8-sig"))
                if r.get("import") == "1"]
        out_dir = a.out or OUT_DIR
        manifest = "models_export.csv"
    rows = [r for r in rows if int(float(r.get("faces") or 0)) >= a.min_faces]
    if a.limit > 0:
        rows = rows[:a.limit]
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(MANIFESTS, exist_ok=True)

    out_rows = []
    ok = fail = 0
    for n, r in enumerate(rows, 1):
        name = r["name"]
        mdl = os.path.join(LIB, r["mdl"])
        trf = os.path.join(out_dir, name + ".trf")
        try:
            meshes = read_mdl(mdl, tex_names)
            if not meshes:
                raise ValueError("no submesh")
            for i, m in enumerate(meshes):
                m.mesh_name = name if len(meshes) == 1 else "%s.%d" % (name, i)
            export_trf_file(meshes, trf)
            out_rows.append([name, r["model_hash"], r["mdl"], name + ".trf",
                             r["faces"], os.path.getsize(trf)])
            ok += 1
        except Exception as e:
            fail += 1
            print("[mdl-trf] FAIL %s: %r" % (name, e), flush=True)
        if n % 500 == 0:
            print("[mdl-trf] %d/%d ok=%d fail=%d" % (n, len(rows), ok, fail), flush=True)

    with io.open(os.path.join(MANIFESTS, manifest), "w",
                 encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        w.writerow(["name", "model_hash", "mdl", "trf", "faces", "bytes"])
        w.writerows(out_rows)
    print("[mdl-trf] %s done ok=%d fail=%d -> %s" % (a.set, ok, fail, out_dir))


if __name__ == "__main__":
    main()
