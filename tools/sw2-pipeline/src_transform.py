# -*- coding: utf-8 -*-
"""src_transform.py —— 逐角色「源变换 T」的唯一来源（头 / 甲 / 兜 三件共用这一把尺）。

要解决的问题
------------
改之前三件各按各的基准定标：头按**源模型眼↔嘴距离**归一化（把眼球送到原版固定眼位），
甲按**一个全局径向系数 R=0.0120** 缩放并逐骨钉到骑砍骨架上。两个基准不同源 →
拼不回原角色：实测宁宁的脖子下沿比甲领口上沿**高 8.7cm**（整圈断在甲上方），
甲还比人粗 28%（R 0.0120 vs 身高对齐的 0.0094）。

T 的定义（本文件是唯一实现，三件都调它）
----------------------------------------
    s    = 1.569 / (源模型 bone_11 世界 z)               # 1.569 = 原版头骨 bip01_head_13 的世界 z
    T(v) = ( s·x,  −s·y,  s·z )                          # Y 轴镜像 + 等比缩放（源原点就是地面，不额外平移）

三个决定各有一条实测依据：

1. **缩放锚在「源模型头骨」上，不用整体身高**（2026-09-16 实测 28 人定的）。
   计划 §9.2 原写「s = 目标身高 ÷ 源模型整体身高（含发/兜）」—— 实测这个口径对**戴高冠的角色
   会算矮**：源模型的总高把兜/头巾一起量进去了，家康（大黑头巾）s_net/s_hair = **1.365**，
   等于身体被压小 36%，会变成「矮子顶个大帽子」。锚到头骨的高度与头发/兜无关，
   且天然满足「脚底→颈顶 = 原版头骨高度 1.569」→ **所有人的身体都与原版身体等高**，
   三件永远贴合原版身体，身高差留给第 4 步（游戏侧缩放）产生。

2. **镜像翻前后，不翻左右**。源模型脸朝 −Y、骑砍脸朝 +Y，所以把前后对调
   （`(x, −y, z)`，镜像面是额状面）——**不能用绕 Z 转 180°**：那会把源模型的左腿甩到 +x 去，左右反了
   （实测误差因此虚高 18cm）。镜像是反射（行列式 −1）→ **必须反转面绕序**，否则法线朝里、实机整片不可见。

3. 🔴 **不做「最低顶点落 z=0」的平移** —— 源模型的**原点就是地面**（实测 28 人的脚部部件最低点
   全在 ±0.09 内、绝大多数 ≈0：秀吉 +0.06 / 兼续 +0.09 / 谦信 +0.02 …），而「全体最低顶点」
   会被两类几何带偏：**归蝶的鞋底画到 −11.26**（模型原点以下 11cm）、小太郎 −0.08、义弘 −0.07。
   拿全体最低顶点当脚底 = 把整人抬高 11cm = 实机「人浮在地面上」。所以 z_sole 恒为 0，
   表里另存 foot_min 只做体检（|foot_min| > 0.05 打警告）。

已知边界（不是 bug）
--------------------
源骨架与骑砍骨架**不是等比关系**（同一个人的各关节要求的 s 散布 10~17%，见计划 §9.3）——
所以 T 不是「把骨骼对齐到 5mm」的工具，它的用途是**让头/甲/兜共用一把尺**，
让三件在源模型里怎么拼、到骑砍空间里还怎么拼。

用法
----
    # 1) 生成/刷新逐角色表（Blender，写 tools/sw2-pipeline/out/srcT.json）
    blender -b --python src_transform.py -- --write ../out/srcT.json
    blender -b --python src_transform.py -- --keys L47_nene --print

    # 2) 非 Blender 侧读表（批处理脚本 / 闸门 / C# 数据）
    from src_transform import load, lookup
    tab = load("tools/sw2-pipeline/out/srcT.json"); row = lookup(tab, "L47_nene")

表里的字段
----------
    s            缩放（米/源单位）
    z_sole       源模型脚底 z（源单位）
    h_bone       脚底 → 头骨（bone_11）的源单位高度 = 「净身高」口径，第 4 步按它算身高比
    h_src        脚底 → 头顶（含发/兜）的源单位高度，仅参考
    height_ratio h_bone ÷ 全体中位数 —— 第 4 步写进角色数据的相对身高（>1 = 比平均高）
"""
import argparse
import csv
import io
import json
import os
import sys

HEAD_BONE_Z = 1.569          # 原版 bip01_head_13 世界 z（build_head.py / build_armor.py 的校准目标）
ANCHOR_BONE = "bone_11"      # 源模型的头骨（脖子以上第一节）

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
SRC_DIR = r"D:\BrainMaker\战国无双2资产解包分析\export\fbx"
CENSUS = os.path.join(REPO, "Debug", "offline", "外观批量导入", "sw2_census")
DEFAULT_OUT = os.path.join(HERE, "out", "srcT.json")


# --------------------------------------------------------------------------- 纯函数（三件共用）
def T_of(s, z_sole):
    """返回 T 变换函数：源坐标(元组/Vector) → 骑砍空间三元组。"""
    def T(p):
        return (p[0] * s, -p[1] * s, (p[2] - z_sole) * s)
    return T


def compute(arm, foot_min, h_src):
    """由源骨架 + 脚部最低点算出这个角色的 s。arm 可以为 None（退回用 h_src 的旧口径）。

    z_sole 恒为 0（源原点 = 地面，见文件头第 3 条）；foot_min 只进表做体检。
    """
    h_bone = None
    if arm is not None:
        b = arm.data.bones.get(ANCHOR_BONE)
        if b is not None:
            h_bone = (arm.matrix_world @ b.head_local).z
    if not h_bone or h_bone <= 1e-6:
        # 兜底：没有骨架/没有头骨时退回「整体身高（含发/兜）」口径，并在表里标出来
        return dict(s=HEAD_BONE_Z / max(h_src, 1e-6) if h_src else None, z_sole=0.0,
                    h_bone=None, h_src=h_src, foot_min=foot_min, anchor="h_src(兜底)")
    return dict(s=HEAD_BONE_Z / h_bone, z_sole=0.0, h_bone=h_bone, h_src=h_src,
                foot_min=foot_min, anchor=ANCHOR_BONE)


# --------------------------------------------------------------------------- 表读写（非 Blender 侧用）
def load(path=DEFAULT_OUT):
    with io.open(path, encoding="utf-8") as fh:
        return json.load(fh)


def lookup(table, key):
    """取一个角色的变换行；不在表里直接抛错（宁可停，也不要静默用错尺子）。"""
    chars = table.get("chars", table)
    row = chars.get(key)
    if not row:
        raise KeyError("srcT 表里没有 %s —— 先跑 src_transform.py --write 刷新" % key)
    return row


# --------------------------------------------------------------------------- Blender 侧生成
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", default=DEFAULT_OUT, help="输出 json（默认 tools/sw2-pipeline/out/srcT.json）")
    ap.add_argument("--keys", nargs="*", default=None)
    ap.add_argument("--print", action="store_true", help="打到屏幕")
    ap.add_argument("--src-dir", default=SRC_DIR)
    a = ap.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])

    import bpy
    if HERE not in sys.path:
        sys.path.insert(0, HERE)          # Blender 跑 -b --python 时不会自动加脚本目录
    from parts_table import TABLE
    try:
        from troop_parts_table import TROOP_TABLE      # 兵种 23 套：同样要 T（它们的甲也要贴合原版身体）
    except Exception:
        TROOP_TABLE = {}

    keys = a.keys or (list(TABLE.keys()) + list(TROOP_TABLE.keys()))
    out = {}
    for key in keys:
        r = TABLE.get(key) or TROOP_TABLE.get(key)
        if r is None:
            print("!! %s 不在 parts_table / troop_parts_table" % key); continue
        src = os.path.join(a.src_dir, key + ".fbx")
        if not os.path.isfile(src):
            print("!! 源模型缺失 %s" % src); continue
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=src)
        bpy.context.view_layer.update()
        meshes = sorted([o for o in bpy.data.objects if o.type == 'MESH'], key=lambda o: o.name)
        arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
        verdict = {}
        cf = os.path.join(CENSUS, key + "_census.csv")
        if os.path.isfile(cf):
            verdict = {int(x["idx"]): x["verdict"]
                       for x in csv.DictReader(io.open(cf, encoding="utf-8"))}
        # 脚部部件（腿/脚/趾骨主导的顶点）的最低点：只做体检（|值| > 0.05 源单位 = 作者把脚画离地/穿地）
        FOOT_BONES = ("bone_2", "bone_3", "bone_4", "bone_5", "bone_6", "bone_7",
                      "bone_24", "bone_25")
        foot_min = None
        for i, ob in enumerate(meshes):
            vd = verdict.get(i, "")
            if vd.startswith(("武器", "驱动件")):
                continue
            mw = ob.matrix_world
            for j, vv in enumerate(ob.data.vertices):
                if not ob.vertex_groups or not vv.groups:
                    continue
                bn = ob.vertex_groups[max(vv.groups, key=lambda g: g.weight).group].name
                if bn in FOOT_BONES:
                    z = (mw @ vv.co).z
                    foot_min = z if foot_min is None else min(foot_min, z)
        # 头骨 z = 源原点 → 头骨的世界 z（不再减脚底）
        hi = [meshes[i].matrix_world @ v.co
              for i in (list(r.get("face") or []) + list(r.get("hair") or [])
                        + list(r.get("helmet") or []))
              for v in meshes[i].data.vertices]
        z_crown = max(p.z for p in hi) if hi else 0.0
        row = compute(arm, foot_min, z_crown)
        row["cn"] = r.get("cn", "")
        row["gender"] = r.get("gender", "")
        out[key] = row
        warn = "  ⚠️ 脚离地/穿地 %+.3f" % foot_min if (foot_min is not None and abs(foot_min) > 0.05) else ""
        print("%-16s %-8s  头骨高 %8.2f  s=%.6f（锚 %s）  含发高 %.2f  脚底 %s%s"
              % (key, r.get("cn", ""), row["h_bone"] or -1, row["s"] or -1,
                 row["anchor"], row["h_src"],
                 ("%+.3f" % foot_min) if foot_min is not None else "-", warn))
        sys.stdout.flush()

    # height_ratio：净身高 ÷ 全体中位数（第 4 步写角色数据用；>1 = 比平均高）
    # 🔴 中位数只取【有名武将】（TABLE 的 28 人），**不含兵种**：兵种是杂兵模板、同源骨架
    #    （23 个里 20 个 h_bone 完全相同），混进来会把中位数从 159.43 抬到 161.39，
    #    等于把 28 个武将的身高比整体压低 1.2% —— 第 4 步的身高全跟着偏。
    hs = sorted(v["h_bone"] for k, v in out.items() if v.get("h_bone") and k in TABLE)
    if hs:
        med = hs[len(hs) // 2]
        for k, v in out.items():
            v["height_ratio"] = (round(v["h_bone"] / med, 4)
                                 if v.get("h_bone") and k in TABLE else None)
        table = dict(_meta=dict(anchor=ANCHOR_BONE, head_bone_z=HEAD_BONE_Z,
                                median_h_bone=round(med, 3), n=len(out), n_lords=len(hs),
                                note="s = 1.569 / 脚底→头骨；height_ratio = 武将净身高/武将中位数"),
                     chars=out)
        print("\n武将中位净身高 %.2f 源单位（%d 人；表共 %d 条含兵种）" % (med, len(hs), len(out)))
    else:
        table = dict(_meta=dict(note="没有任何角色取到头骨"), chars=out)

    if a.write:
        os.makedirs(os.path.dirname(os.path.abspath(a.write)), exist_ok=True)
        with io.open(a.write, "w", encoding="utf-8") as fh:
            fh.write(json.dumps(table, ensure_ascii=False, indent=1))
        print("→ %s" % os.path.abspath(a.write))
    if a.print:
        print(json.dumps(table, ensure_ascii=False, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
